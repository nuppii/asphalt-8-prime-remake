using System;
using System.Net.Sockets;
using System.Threading;

namespace OfflineLab.Multiplayer
{
    public sealed class RoomClient : IDisposable
    {
        private readonly object sendGate=new object();
        private readonly TcpClient socket;
        private readonly NetworkStream stream;
        private int closed;
        private volatile RoomSnapshot snapshot;
        private readonly string password;
        private readonly string sessionId;
        private readonly string connectionAddress;
        private readonly int connectionPort;
        private byte[] sessionTicket;
        private PlayerProfile profile;
        private string localId;
        private RoomServer hostedServer;
        private volatile RoomClient successor;
        public event Action<RoomSnapshot> SnapshotChanged;
        public event Action<string> Disconnected;
        public event Action<string> ErrorReceived;
        public string LocalId { get { return successor==null?localId:successor.LocalId; } }
        public RoomServer HostedServer { get { return successor==null?hostedServer:successor.HostedServer; } }
        public RoomSnapshot Snapshot { get { return successor==null?snapshot:successor.Snapshot; } }
        public bool IsConnected { get { return successor==null?Interlocked.CompareExchange(ref closed,0,0)==0:successor.IsConnected; } }
        public string HostAddress { get { return successor==null?connectionAddress:successor.HostAddress; } }
        public int HostPort { get { return successor==null?connectionPort:successor.HostPort; } }

        private RoomClient(string address,int port,string password,PlayerProfile profile,byte[] hostKey,string identity,byte[] ticket)
        {
            if(profile==null)throw new ArgumentNullException("profile");
            profile.Validate();
            this.profile=profile;this.password=password;sessionId=identity;connectionAddress=address;connectionPort=port;
            if(port<1||port>65535)throw new ArgumentOutOfRangeException("port");
            socket=new TcpClient();
            try
            {
                var connect=socket.BeginConnect(address,port,null,null);
                try{if(!connect.AsyncWaitHandle.WaitOne(5000))throw new TimeoutException("The room connection timed out.");socket.EndConnect(connect);}
                finally{connect.AsyncWaitHandle.Close();}
                socket.NoDelay=true;socket.ReceiveTimeout=10000;socket.SendTimeout=3000;stream=socket.GetStream();
                WirePacket challenge=RoomProtocol.Read(stream);
                if(challenge.Type!="challenge"||challenge.Iterations!=RoomProtocol.PasswordIterations)
                    throw new InvalidOperationException("The server uses an unsupported room authentication protocol.");
                byte[] salt=Convert.FromBase64String(challenge.Salt??"");
                byte[] nonce=Convert.FromBase64String(challenge.Nonce??"");
                if(salt.Length!=24||nonce.Length!=32||string.IsNullOrEmpty(challenge.Id)||challenge.Id.Length>64)
                    throw new InvalidOperationException("The server supplied an invalid room challenge.");
                string clientNonce=Convert.ToBase64String(RoomProtocol.RandomBytes(24));
                byte[] key=RoomProtocol.Derive(password,salt);
                var join=new WirePacket{Type="join",Id=sessionId,Profile=profile,Nonce=clientNonce,
                    Proof=RoomProtocol.Proof(key,challenge.Id,challenge.Nonce,clientNonce,profile,sessionId)};
                if(hostKey!=null)join.HostProof=RoomProtocol.Proof(hostKey,challenge.Id,challenge.Nonce,clientNonce,profile,sessionId);
                if(ticket!=null)join.SessionProof=RoomProtocol.Proof(ticket,challenge.Id,challenge.Nonce,clientNonce,profile,sessionId);
                RoomProtocol.Write(stream,join);
                WirePacket response=RoomProtocol.Read(stream);
                if(response.Type=="error")throw new InvalidOperationException(response.Text??"Room join rejected.");
                if(response.Type!="welcome"||response.Snapshot==null||string.IsNullOrEmpty(response.Id))
                    throw new InvalidOperationException("The room did not confirm the join request.");
                localId=response.Id;snapshot=response.Snapshot;socket.ReceiveTimeout=15000;
                sessionTicket=Convert.FromBase64String(response.SessionTicket??"");
                if(sessionTicket.Length!=32)throw new InvalidOperationException("The room supplied an invalid player session ticket.");
                new Thread(ReadLoop){IsBackground=true,Name="Offline Lab room client"}.Start();
            }
            catch{socket.Close();throw;}
        }
        public static RoomClient Create(string name,PlayerProfile profile,RoomSettings settings,string password,int port=47781)
        {
            var server=new RoomServer(name,settings,password);server.Start(port);
            try{var client=new RoomClient("127.0.0.1",server.Port,password,profile,server.HostKey,Guid.NewGuid().ToString("N"),null);client.hostedServer=server;return client;}
            catch{server.Dispose();throw;}
        }
        public static RoomClient Join(string address,int port,string password,PlayerProfile profile)
        {return new RoomClient(address,port,password,profile,null,Guid.NewGuid().ToString("N"),null);}
        public void Vote(string mapId) {Send(new WirePacket{Type="vote",Text=mapId});}
        public void Kick(string memberId) {Send(new WirePacket{Type="kick",Id=memberId});}
        public void Promote(string memberId) {Send(new WirePacket{Type="promote",Id=memberId});}
        public void UpdateProfile(PlayerProfile profile)
        {if(profile==null)throw new ArgumentNullException("profile");profile.Validate();this.profile=profile;if(successor!=null)successor.profile=profile;Send(new WirePacket{Type="profile",Profile=profile});}
        public void UpdateSettings(RoomSettings settings)
        {if(settings==null)throw new ArgumentNullException("settings");settings.Validate();Send(new WirePacket{Type="settings",Settings=settings});}
        public void SelectNextMap(){Send(new WirePacket{Type="select-map"});}
        public void RequestRaceStart(){Send(new WirePacket{Type="start"});}
        private void Send(WirePacket packet)
        {
            if(successor!=null){successor.Send(packet);return;}
            SendDirect(packet);
        }
        private void SendDirect(WirePacket packet)
        {
            lock(sendGate)
            {
                if(Interlocked.CompareExchange(ref closed,0,0)!=0)throw new InvalidOperationException("You are not connected to a room.");
                try{RoomProtocol.Write(stream,packet);}
                catch{CloseConnection("The room connection was interrupted.");throw;}
            }
        }
        private void ReadLoop()
        {
            string reason="The room connection closed.";
            try
            {
                while(IsConnected)
                {
                    WirePacket packet=RoomProtocol.Read(stream);
                    if(packet.Type=="ping")Send(new WirePacket{Type="pong",Ping=packet.Ping});
                    else if(packet.Type=="snapshot"&&packet.Snapshot!=null&&packet.Snapshot.Revision>=snapshot.Revision)
                    {snapshot=packet.Snapshot;Invoke(SnapshotChanged,snapshot);}
                    else if(packet.Type=="snapshot")continue;
                    else if(packet.Type=="error")Invoke(ErrorReceived,packet.Text??"The room command failed.");
                    else if(packet.Type=="takeover"&&packet.Snapshot!=null)
                    {
                        RoomSnapshot seed=packet.Snapshot;
                        var server=new RoomServer(seed.Name,seed.Settings,password,seed,packet.Tickets);server.Start(0);
                        RoomClient next=null;
                        try
                        {
                            next=new RoomClient("127.0.0.1",server.Port,password,profile,server.HostKey,sessionId,sessionTicket);
                            next.hostedServer=server;
                            SendDirect(new WirePacket{Type="takeover-ready",Id=packet.Id,Port=server.Port});
                            Adopt(next);break;
                        }
                        catch{if(next!=null)next.Dispose();else server.Dispose();throw;}
                    }
                    else if(packet.Type=="redirect")
                    {
                        if(packet.Id!=snapshot.RoomId)throw new InvalidOperationException("Host handover room identity mismatch.");
                        var next=new RoomClient(packet.Text,packet.Port,password,profile,null,sessionId,sessionTicket);
                        if(next.Snapshot.RoomId!=snapshot.RoomId){next.Dispose();throw new InvalidOperationException("Host handover room identity changed.");}
                        Adopt(next);break;
                    }
                    else if(packet.Type=="closed"){reason=packet.Text??reason;break;}
                    else throw new InvalidOperationException("The server sent an unsupported room message.");
                }
            }
            catch(Exception ex)
            {if(ex is InvalidOperationException)reason=ex.Message;}
            finally{CloseConnection(reason);}
        }
        private static void Invoke<T>(Action<T> action,T value)
        {
            if(action==null)return;
            foreach(Action<T> handler in action.GetInvocationList())try{handler(value);}catch{}
        }
        private void CloseConnection(string reason)
        {
            if(Interlocked.Exchange(ref closed,1)!=0)return;
            socket.Close();if(successor==null)Invoke(Disconnected,reason);
        }
        private void Adopt(RoomClient next)
        {
            next.SnapshotChanged+=value=>Invoke(SnapshotChanged,value);
            next.ErrorReceived+=value=>Invoke(ErrorReceived,value);
            next.Disconnected+=value=>Invoke(Disconnected,value);
            successor=next;Invoke(SnapshotChanged,next.Snapshot);
        }
        public void Leave()
        {
            if(successor!=null){successor.Leave();return;}
            if(IsConnected&&hostedServer!=null)try{hostedServer.TransferHost(5000);}catch{}
            else if(IsConnected)try{Send(new WirePacket{Type="leave"});}catch{}
            CloseConnection("You left the room.");
            if(hostedServer!=null)hostedServer.Dispose();
        }
        public void Dispose(){Leave();}
    }
}
