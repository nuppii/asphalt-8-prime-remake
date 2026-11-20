using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace OfflineLab.Multiplayer
{
    public sealed class RoomServer : IDisposable
    {
        private readonly object gate=new object();
        private readonly Dictionary<string,Peer> peers=new Dictionary<string,Peer>();
        private readonly Dictionary<string,string> votes=new Dictionary<string,string>();
        private readonly byte[] salt=RoomProtocol.RandomBytes(24);
        private readonly byte[] hostKey=RoomProtocol.RandomBytes(32);
        private readonly byte[] passwordKey;
        private readonly string roomId;
        private readonly string name;
        private readonly bool isPrivate;
        private RoomSettings settings;
        private string hostId;
        private string mapId="Sector 8";
        private long revision;
        private TcpListener listener;
        private Timer heartbeat;
        private Timer rttPublish;
        private bool rttDirty;
        private volatile bool stopped;
        private int handshakes;
        private readonly ManualResetEvent handoverFinished=new ManualResetEvent(false);
        private readonly HashSet<string> inheritedModerators=new HashSet<string>();
        private readonly Dictionary<string,int> inheritedSlots=new Dictionary<string,int>();
        private readonly Dictionary<string,string> sessionTickets=new Dictionary<string,string>();
        private readonly HashSet<string> handoverPending=new HashSet<string>();
        private string handoverCandidate;
        private string handoverNonce;
        private bool handingOver;
        private Timer handoverTimer;
        public int Port { get; private set; }
        public string RoomId { get { return roomId; } }
        internal byte[] HostKey { get { return hostKey; } }

        public LanRoomListing CreateListing(bool listPasswordProtected = false)
        {
            lock(gate)
            {
                if(stopped||Port==0||peers.Count==0)return null;
                return new LanRoomListing
                {
                    RoomId=roomId,Name=name,Port=Port,Players=peers.Count,Capacity=settings.EffectiveGridSlots,
                    IsPrivate=isPrivate&&!listPasswordProtected,PasswordProtected=isPrivate
                };
            }
        }

        public RoomServer(string name,RoomSettings settings,string password) : this(name,settings,password,null,null) { }
        internal RoomServer(string name,RoomSettings settings,string password,RoomSnapshot seed,Dictionary<string,string> tickets)
        {
            this.name=RoomProtocol.Clean(name,48,false,"Room name");
            if(settings==null)throw new ArgumentNullException("settings");
            settings.Validate();this.settings=settings.Copy();
            if(password!=null&&password.Length>128)throw new ArgumentException("The room password is too long.");
            isPrivate=!string.IsNullOrEmpty(password);
            passwordKey=RoomProtocol.Derive(password,salt);
            roomId=seed==null?Guid.NewGuid().ToString("N"):seed.RoomId;
            if(seed!=null)
            {
                mapId=seed.MapId;
                foreach(var vote in seed.Votes)if(vote.Key!=seed.HostId)votes[vote.Key]=vote.Value;
                foreach(RoomMember member in seed.Members)
                {
                    if(member.Id==seed.HostId)continue;
                    if(member.IsModerator)inheritedModerators.Add(member.Id);
                    if(member.GridSlot>0&&member.GridSlot<=this.settings.EffectiveGridSlots)
                        inheritedSlots[member.Id]=member.GridSlot;
                }
                revision=seed.Revision+1;
                if(tickets!=null)foreach(var ticket in tickets)if(ticket.Key!=seed.HostId)sessionTickets[ticket.Key]=ticket.Value;
            }
        }
        public void Start(int port)
        {
            if(listener!=null)throw new InvalidOperationException("The room server has already started.");
            if(port<0||port>65535)throw new ArgumentOutOfRangeException("port");
            listener=new TcpListener(IPAddress.Any,port);
            listener.Start(64);Port=((IPEndPoint)listener.LocalEndpoint).Port;
            rttPublish=new Timer(PublishRtt,null,Timeout.Infinite,Timeout.Infinite);
            var thread=new Thread(AcceptLoop){IsBackground=true,Name="Asphalt 8 Prime Remake room listener"};thread.Start();
            heartbeat=new Timer(Heartbeat,null,3000,3000);
        }
        private void AcceptLoop()
        {
            while(!stopped)
            {
                TcpClient socket=null;
                try
                {
                    socket=listener.AcceptTcpClient();
                    if(Interlocked.Increment(ref handshakes)>64)
                    {Interlocked.Decrement(ref handshakes);socket.Close();continue;}
                    TcpClient selected=socket;
                    new Thread(()=>AcceptPeer(selected)){IsBackground=true,Name="Asphalt 8 Prime Remake room handshake"}.Start();
                }
                catch(SocketException){if(stopped)return;if(socket!=null)socket.Close();}
                catch(ObjectDisposedException){return;}
            }
        }
        private void AcceptPeer(TcpClient socket)
        {
            Peer peer=null;
            try
            {
                socket.NoDelay=true;socket.ReceiveTimeout=10000;socket.SendTimeout=3000;
                NetworkStream stream=socket.GetStream();
                string nonce=Convert.ToBase64String(RoomProtocol.RandomBytes(32));
                RoomProtocol.Write(stream,new WirePacket{Type="challenge",Id=roomId,Salt=Convert.ToBase64String(salt),Nonce=nonce,Iterations=RoomProtocol.PasswordIterations});
                WirePacket join=RoomProtocol.Read(stream);
                if(join.Type!="join"||join.Profile==null)throw new InvalidOperationException("Expected a room join request.");
                join.Profile.Validate();
                if(string.IsNullOrEmpty(join.Nonce)||join.Nonce.Length>96)throw new InvalidOperationException("Invalid client challenge.");
                Guid sessionIdentity;
                if(!Guid.TryParseExact(join.Id,"N",out sessionIdentity))throw new InvalidOperationException("Invalid player session identity.");
                string proof=RoomProtocol.Proof(passwordKey,roomId,nonce,join.Nonce,join.Profile,join.Id);
                if(!RoomProtocol.EqualProof(proof,join.Proof))throw new InvalidOperationException("Incorrect room password.");
                bool hostClaim=RoomProtocol.EqualProof(RoomProtocol.Proof(hostKey,roomId,nonce,join.Nonce,join.Profile,join.Id),join.HostProof);
                lock(gate)
                {
                    if(stopped)throw new InvalidOperationException("The room server is closing.");
                    if(handingOver)throw new InvalidOperationException("The room is transferring to a new host. Retry at its new address.");
                    if(hostId==null&&!hostClaim)throw new InvalidOperationException("The host is still initializing the room.");
                    if(peers.Count>=settings.EffectiveGridSlots)throw new InvalidOperationException("The room is full.");
                    if(hostClaim&&hostId!=null)throw new InvalidOperationException("The room already has a host.");
                    string id=join.Id;
                    if(peers.ContainsKey(id))throw new InvalidOperationException("This player session is already connected.");
                    string ticket;
                    if(sessionTickets.TryGetValue(id,out ticket))
                    {
                        string sessionProof=RoomProtocol.Proof(Convert.FromBase64String(ticket),roomId,nonce,join.Nonce,join.Profile,id);
                        if(!RoomProtocol.EqualProof(sessionProof,join.SessionProof))throw new InvalidOperationException("Invalid migrated player session proof.");
                    }
                    else ticket=Convert.ToBase64String(RoomProtocol.RandomBytes(32));
                    ulong occupied=OccupiedSlots(settings.EffectiveGridSlots);
                    int previousSlot;
                    int gridSlot=inheritedSlots.TryGetValue(id,out previousSlot) &&
                        previousSlot<=settings.EffectiveGridSlots && (occupied&(1UL<<(previousSlot-1)))==0
                        ?previousSlot:FirstFreeSlot(occupied,settings.EffectiveGridSlots);
                    var member=new RoomMember{Id=id,Nickname=join.Profile.Nickname,Status=join.Profile.Status,GroupTag=join.Profile.GroupTag,
                        IsHost=hostClaim,IsModerator=hostClaim||inheritedModerators.Contains(id),RttMs=-1,
                        GridSlot=gridSlot,Endpoint=socket.Client.RemoteEndPoint.ToString()};
                    peer=new Peer(this,socket,member);
                    peers.Add(id,peer);sessionTickets[id]=ticket;inheritedSlots.Remove(id);if(hostClaim)hostId=id;
                    ++revision;
                    RoomProtocol.Write(stream,new WirePacket{Type="welcome",Id=id,SessionTicket=ticket,Snapshot=SnapshotLocked()});
                }
                socket.ReceiveTimeout=15000;
                peer.StartWriter();Publish();
                while(!stopped&&!peer.Closed)
                {
                    WirePacket packet=RoomProtocol.Read(stream);
                    if(!peer.AdmitMessage())throw new InvalidOperationException("Room message rate exceeded.");
                    Handle(peer,packet);
                }
            }
            catch(Exception ex)
            {
                if(peer==null)
                {try{RoomProtocol.Write(socket.GetStream(),new WirePacket{Type="error",Text=PublicError(ex)});}catch{}socket.Close();}
                else Drop(peer,PublicError(ex));
            }
            finally {Interlocked.Decrement(ref handshakes);if(peer!=null)Drop(peer,"Connection closed.");}
        }
        private static string PublicError(Exception ex)
        {
            if(ex is InvalidOperationException||ex is ArgumentException)return ex.Message;
            return "The room connection was interrupted.";
        }
        private void Handle(Peer sender,WirePacket packet)
        {
            bool changed=false;
            try
            {
                lock(gate)
                {
                    if(!peers.ContainsKey(sender.Member.Id))return;
                    switch(packet.Type)
                    {
                        case "pong":
                            if(packet.Ping!=sender.LastPing)break;
                            sender.LastPong=Stopwatch.GetTimestamp();
                            double milliseconds=(sender.LastPong-packet.Ping)*1000.0/Stopwatch.Frequency;
                            sender.Member.RttMs=Math.Max(0,Math.Min(60000,(int)milliseconds));
                            if(!rttDirty && !stopped)
                            {
                                rttDirty=true;
                                rttPublish.Change(100,Timeout.Infinite);
                            }
                            break;
                        case "takeover-ready":
                            CompleteNomination(sender,packet);return;
                        case "profile":
                            if(packet.Profile==null)throw new ArgumentException("A profile is required.");
                            packet.Profile.Validate();sender.Member.Nickname=packet.Profile.Nickname;
                            sender.Member.Status=packet.Profile.Status;sender.Member.GroupTag=packet.Profile.GroupTag;changed=true;break;
                        case "vote":
                            votes[sender.Member.Id]=RoomProtocol.Clean(packet.Text,80,false,"Map name");changed=true;break;
                        case "settings":
                            RequireHost(sender);
                            if(packet.Settings==null)throw new ArgumentException("Room settings are required.");
                            packet.Settings.Validate();
                            if(packet.Settings.EffectiveGridSlots<peers.Count)throw new ArgumentException("The grid cannot be smaller than the connected player count.");
                            int capacity=packet.Settings.EffectiveGridSlots;
                            ulong occupied=OccupiedSlots(capacity);
                            foreach(Peer member in peers.Values.Where(p=>p.Member.GridSlot>capacity).OrderBy(p=>p.Member.GridSlot))
                            {
                                int slot=FirstFreeSlot(occupied,capacity);
                                member.Member.GridSlot=slot;
                                occupied|=1UL<<(slot-1);
                            }
                            settings=packet.Settings.Copy();changed=true;break;
                        case "kick":
                            if(!sender.Member.IsModerator)throw new InvalidOperationException("Only the host or a moderator can exclude players.");
                            Peer victim;
                            if(!peers.TryGetValue(packet.Id??"",out victim))throw new ArgumentException("Player not found.");
                            if(victim.Member.IsHost||victim.Member.Id==sender.Member.Id)throw new InvalidOperationException("The host and yourself cannot be excluded.");
                            if(victim.Member.IsModerator&&!sender.Member.IsHost)throw new InvalidOperationException("Only the host can exclude another moderator.");
                            peers.Remove(victim.Member.Id);votes.Remove(victim.Member.Id);sessionTickets.Remove(victim.Member.Id);inheritedModerators.Remove(victim.Member.Id);
                            victim.Send(new WirePacket{Type="closed",Text="You were excluded from the room."});changed=true;break;
                        case "promote":
                            RequireHost(sender);Peer promoted;
                            if(!peers.TryGetValue(packet.Id??"",out promoted))throw new ArgumentException("Player not found.");
                            promoted.Member.IsModerator=true;changed=true;break;
                        case "select-map":
                            RequireHost(sender);
                            string[] activeVotes=votes.Where(v=>peers.ContainsKey(v.Key)).Select(v=>v.Value).ToArray();
                            if(activeVotes.Length!=0)mapId=activeVotes.GroupBy(x=>x).OrderByDescending(x=>x.Count()).ThenBy(x=>x.Key,StringComparer.Ordinal).First().Key;
                            votes.Clear();changed=true;break;
                        case "start":
                            RequireHost(sender);throw new InvalidOperationException("Synchronized racing is unavailable: the native race bridge is not connected.");
                        case "leave":
                            Drop(sender,"You left the room.");return;
                        default: throw new InvalidOperationException("Unsupported room command.");
                    }
                    if(changed)++revision;
                }
                if(changed)Publish();
            }
            catch(Exception ex){sender.Send(new WirePacket{Type="error",Text=PublicError(ex)});}
        }
        private static void RequireHost(Peer sender)
        {if(!sender.Member.IsHost)throw new InvalidOperationException("Only the room host can perform this action.");}
        private ulong OccupiedSlots(int capacity)
        {
            ulong occupied=0;
            foreach(Peer peer in peers.Values)
            {
                int slot=peer.Member.GridSlot;
                if(slot>0&&slot<=capacity)occupied|=1UL<<(slot-1);
            }
            return occupied;
        }
        private static int FirstFreeSlot(ulong occupied,int capacity)
        {
            for(int slot=1;slot<=capacity;++slot)
                if((occupied&(1UL<<(slot-1)))==0)return slot;
            throw new InvalidOperationException("The room is full.");
        }
        private RoomSnapshot SnapshotLocked()
        {
            return new RoomSnapshot{RoomId=roomId,Name=name,HostId=hostId,MapId=mapId,Settings=settings.Copy(),
                IsPrivate=isPrivate,Members=peers.Values.Select(p=>p.Member.Copy()).OrderByDescending(p=>p.IsHost).ThenBy(p=>p.Id,StringComparer.Ordinal).ToList(),
                Votes=votes.Where(v=>peers.ContainsKey(v.Key)).ToDictionary(v=>v.Key,v=>v.Value),BotCount=settings.FillBots?Math.Max(0,settings.EffectiveGridSlots-peers.Count):0,
                RaceBridgeReady=false,HostMigrationReady=true,AbruptHostMigrationReady=false,Revision=revision};
        }
        private void Publish()
        {
            Peer[] targets;RoomSnapshot snapshot;
            lock(gate){if(stopped||handingOver)return;targets=peers.Values.ToArray();snapshot=SnapshotLocked();}
            foreach(Peer peer in targets)peer.Send(new WirePacket{Type="snapshot",Snapshot=snapshot});
        }
        private void Heartbeat(object ignored)
        {
            Peer[] targets;lock(gate){if(stopped)return;targets=peers.Values.ToArray();}
            long now=Stopwatch.GetTimestamp();
            foreach(Peer peer in targets)
            {
                if((now-peer.LastPong)/(double)Stopwatch.Frequency>15){Drop(peer,"Heartbeat timeout.");continue;}
                peer.LastPing=now;peer.Send(new WirePacket{Type="ping",Ping=now});
            }
        }
        private void PublishRtt(object ignored)
        {
            lock(gate)
            {
                if(stopped||!rttDirty)return;
                rttDirty=false;
                ++revision;
            }
            Publish();
        }
        private void Drop(Peer peer,string reason)
        {
            bool removed;bool hostLeft=false;
            lock(gate)
            {
                removed=peers.Remove(peer.Member.Id);votes.Remove(peer.Member.Id);sessionTickets.Remove(peer.Member.Id);inheritedModerators.Remove(peer.Member.Id);
                if(removed){hostLeft=peer.Member.IsHost;++revision;}
            }
            peer.Close();
            // Moderator promotion changes room permissions, never network ownership.
            // A process-to-process host handover is deliberately not advertised.
            if(hostLeft){Dispose();return;}
            if(handingOver){RedirectDelivered(peer.Member.Id);return;}
            if(removed)Publish();
        }
        public void Dispose()
        {
            Peer[] targets;
            lock(gate)
            {if(stopped)return;stopped=true;targets=peers.Values.ToArray();peers.Clear();votes.Clear();}
            if(heartbeat!=null)heartbeat.Dispose();if(rttPublish!=null)rttPublish.Dispose();if(handoverTimer!=null)handoverTimer.Dispose();if(listener!=null)listener.Stop();
            foreach(Peer peer in targets)peer.Close();
            handoverFinished.Set();
        }
        // This transfers the room control plane only. The game race bridge is
        // independent, and an abrupt loss of this process is not recoverable yet.
        internal bool TransferHost(int timeoutMilliseconds)
        {
            Peer candidate;
            RoomSnapshot seed;
            lock(gate)
            {
                if(stopped)return false;
                candidate=peers.Values.Where(p=>!p.Member.IsHost&&!p.Closed)
                    .OrderBy(p=>p.Member.RttMs<0?int.MaxValue:p.Member.RttMs).ThenBy(p=>p.Member.Id,StringComparer.Ordinal).FirstOrDefault();
                if(candidate==null)return false;
                handingOver=true;handoverCandidate=candidate.Member.Id;
                handoverNonce=Convert.ToBase64String(RoomProtocol.RandomBytes(24));
                seed=SnapshotLocked();
                seed.Members.RemoveAll(m=>m.IsHost);
                seed.Votes.Remove(seed.HostId);
                var tickets=new Dictionary<string,string>(sessionTickets);tickets.Remove(hostId);
                candidate.Send(new WirePacket{Type="takeover",Id=handoverNonce,Snapshot=seed,Tickets=tickets});
            }
            return handoverFinished.WaitOne(timeoutMilliseconds);
        }
        private void CompleteNomination(Peer sender,WirePacket packet)
        {
            if(!handingOver||sender.Member.Id!=handoverCandidate||packet.Id!=handoverNonce||packet.Port<1||packet.Port>65535)
                throw new InvalidOperationException("Unexpected host handover confirmation.");
            string address=sender.RemoteAddress;
            Peer[] targets=peers.Values.Where(p=>!p.Member.IsHost&&p.Member.Id!=handoverCandidate).ToArray();
            handoverPending.Clear();foreach(Peer peer in targets)handoverPending.Add(peer.Member.Id);
            foreach(Peer peer in targets)peer.Send(new WirePacket{Type="redirect",Text=address,Port=packet.Port,Id=roomId});
            if(handoverPending.Count==0)FinishHandoverSoon();
        }
        private void RedirectDelivered(string id)
        {
            lock(gate)
            {
                if(!handingOver||handoverPending.Count==0)return;
                if(handoverPending.Remove(id)&&handoverPending.Count==0)FinishHandoverSoon();
            }
        }
        private void FinishHandoverSoon()
        {if(handoverTimer==null)handoverTimer=new Timer(_=>Dispose(),null,250,Timeout.Infinite);}
        private sealed class Peer
        {
            internal readonly RoomMember Member;
            private readonly RoomServer server;
            private readonly TcpClient socket;
            private readonly BlockingCollection<WirePacket> outbound=new BlockingCollection<WirePacket>(32);
            private int closed;
            private long rateWindow=Stopwatch.GetTimestamp();
            private int rateCount;
            internal long LastPing;
            internal long LastPong=Stopwatch.GetTimestamp();
            internal string RemoteAddress { get { return ((IPEndPoint)socket.Client.RemoteEndPoint).Address.ToString(); } }
            internal bool Closed {get{return Interlocked.CompareExchange(ref closed,0,0)!=0;}}
            internal Peer(RoomServer server,TcpClient socket,RoomMember member){this.server=server;this.socket=socket;Member=member;}
            internal bool AdmitMessage()
            {
                long now=Stopwatch.GetTimestamp();
                if((now-rateWindow)/(double)Stopwatch.Frequency>=1){rateWindow=now;rateCount=0;}
                return ++rateCount<=30;
            }
            internal void StartWriter()
            {
                new Thread(()=>
                {
                    try{foreach(WirePacket packet in outbound.GetConsumingEnumerable())
                    {RoomProtocol.Write(socket.GetStream(),packet);if(packet.Type=="redirect")server.RedirectDelivered(Member.Id);if(packet.Type=="closed")break;}}
                    catch{}finally{server.Drop(this,"Connection closed.");}
                }){IsBackground=true,Name="Asphalt 8 Prime Remake room sender"}.Start();
            }
            internal void Send(WirePacket packet)
            {if(Closed)return;try{if(!outbound.TryAdd(packet))server.Drop(this,"Room client cannot keep up.");}catch(InvalidOperationException){}}
            internal void Close()
            {if(Interlocked.Exchange(ref closed,1)!=0)return;outbound.CompleteAdding();socket.Close();}
        }
    }
}
