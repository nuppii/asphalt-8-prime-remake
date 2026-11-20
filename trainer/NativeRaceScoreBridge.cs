using System;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

// The game measures RTT on its UDP connection. This read-only TCP channel
// carries the host's existing measurements and standings to guests.
internal sealed class NativeRaceScoreBridge : IDisposable
{
    private const int Size=NativeNetworkState.RaceBoardBytes, Port=NativeNetworkState.InternetRacePort;
    private TcpListener listener;
    private int pending, revision, requestedPid;
    private DateTime nextBind, nextFetch;
    private ulong requestedRoom;
    private Task<byte[]> fetch;

    private static uint Word(byte[] data,int offset){return BitConverter.ToUInt32(data,offset);}
    private static ulong Room(byte[] data,int offset){return BitConverter.ToUInt64(data,offset);}
    internal static bool Valid(byte[] data,int offset,ulong room)
    {
        if(data==null||offset<0||data.Length-offset<Size||room==0)return false;
        uint seq=Word(data,offset),count=Word(data,offset+16);
        if(seq==0||(seq&1)!=0||seq!=Word(data,offset+Size-4)||Word(data,offset+4)!=0x38444352||
           Room(data,offset+8)!=room||count<1||count>12)return false;
        int hosts=0;
        for(int i=0;i<count;i++)
        {
            int row=offset+24+NativeNetworkState.RaceRowBytes*i;
            uint kind=Word(data,row+16);
            int ping=BitConverter.ToInt32(data,row+20);
            if(kind<1||kind>3||ping < -1||ping>60000||data[row+55]!=0)return false;
            if(kind==1)hosts++;
        }
        return hosts==1;
    }

    internal void Poll(int pid,byte[] data)
    {
        int offset=NativeNetworkState.HostRaceBoard;
        ulong host=Room(data,offset+8),guest=Room(data,NativeNetworkState.RaceGuestLow);
        bool hosting=unchecked((uint)Environment.TickCount-Word(data,offset+20))<=2000&&Valid(data,offset,host);
        if(hosting)Serve(data,offset,host);
        else Stop();
        if(fetch!=null&&fetch.IsCompleted)
        {
            byte[] board=fetch.Result;
            fetch=null;
            if(!hosting&&board!=null&&requestedPid==pid&&requestedRoom==guest)Publish(pid,board);
        }
        if(hosting||guest==0||Word(data,NativeNetworkState.RaceTabHeld)!=1||fetch!=null||DateTime.UtcNow<nextFetch)return;
        int at=NativeNetworkState.RaceGuestAddress;
        if(data[at]==0||data[at]>=224)return;
        var address=new IPAddress(Slice(data,at,4));
        byte[] key=Room(data,NativeNetworkState.RaceProofRoom)==guest&&Word(data,NativeNetworkState.GuestPasswordReady)==1?
            Slice(data,NativeNetworkState.GuestPasswordKey,32):null;
        requestedPid=pid;requestedRoom=guest;nextFetch=DateTime.UtcNow.AddMilliseconds(450);
        fetch=Task.Run(()=>Get(address,guest,key));
    }

    private void Serve(byte[] data,int offset,ulong room)
    {
        bool locked=(Word(data,32)&32)!=0;
        if(locked&&Word(data,NativeNetworkState.HostPasswordReady)!=1){Stop();return;}
        try
        {
            if(listener==null)
            {
                if(DateTime.UtcNow<nextBind)return;
                listener=new TcpListener(IPAddress.Any,Port);listener.Start(12);
            }
            for(int count=0;count<3&&listener.Pending();count++)
            {
                TcpClient client=listener.AcceptTcpClient();
                if(Interlocked.Increment(ref pending)>12){Interlocked.Decrement(ref pending);client.Close();continue;}
                byte[] board=Slice(data,offset,Size),key=locked?Slice(data,NativeNetworkState.HostPasswordKey,32):null;
                for(int row=0;row<12;++row)Array.Clear(board,24+row*NativeNetworkState.RaceRowBytes,16);
                Task.Run(() => {try{Send(client,room,key,board);}finally{Interlocked.Decrement(ref pending);}});
            }
        }
        catch(SocketException){Stop();nextBind=DateTime.UtcNow.AddSeconds(3);}
    }

    private static byte[] Slice(byte[] data,int offset,int count)
    {var result=new byte[count];Buffer.BlockCopy(data,offset,result,0,count);return result;}
    private static byte[] Read(NetworkStream stream,int count)
    {
        var bytes=new byte[count];
        var timeout=Stopwatch.StartNew();
        for(int offset=0;offset<count;)
        {
            int remaining=650-(int)timeout.ElapsedMilliseconds;
            if(remaining<1)throw new IOException("Race standings read timed out.");
            stream.ReadTimeout=remaining;
            int read=stream.Read(bytes,offset,count-offset);
            if(read==0)throw new EndOfStreamException();
            offset+=read;
        }
        return bytes;
    }
    private static byte[] Proof(byte[] key,byte[] challenge,ulong room)
    {
        var input=new byte[24];Buffer.BlockCopy(challenge,0,input,0,16);
        Buffer.BlockCopy(BitConverter.GetBytes(room),0,input,16,8);
        using(var mac=new HMACSHA256(key))return mac.ComputeHash(input);
    }
    private static void Timeouts(TcpClient client){client.ReceiveTimeout=650;client.SendTimeout=650;}
    private static void ClearKey(byte[] key){if(key!=null)Array.Clear(key,0,key.Length);}

    internal static void Send(TcpClient client,ulong room,byte[] key,byte[] board)
    {
        using(client)
        try
        {
            Timeouts(client);
            var stream=client.GetStream();
            var challenge=new byte[16];
            using(var random=RandomNumberGenerator.Create())random.GetBytes(challenge);
            stream.Write(challenge,0,challenge.Length);
            var request=Read(stream,40);
            if(Room(request,0)!=room)return;
            if(key!=null)
            {
                byte[] proof=Proof(key,challenge,room);
                int mismatch=0;
                for(int i=0;i<proof.Length;i++)mismatch|=proof[i]^request[i+8];
                if(mismatch!=0)return;
            }
            stream.Write(board,0,board.Length);
        }
        catch(IOException){}
        catch(SocketException){}
        finally{ClearKey(key);}
    }
    internal static byte[] Get(IPAddress address,ulong room,byte[] key,int port=Port)
    {
        using(var client=new TcpClient(AddressFamily.InterNetwork))
        try
        {
            var opening=client.BeginConnect(address,port,null,null);
            using(var wait=opening.AsyncWaitHandle)if(!wait.WaitOne(450))return null;
            client.EndConnect(opening);Timeouts(client);
            var stream=client.GetStream();
            byte[] challenge=Read(stream,16),request=new byte[40];
            Buffer.BlockCopy(BitConverter.GetBytes(room),0,request,0,8);
            if(key!=null)Buffer.BlockCopy(Proof(key,challenge,room),0,request,8,32);
            stream.Write(request,0,request.Length);
            byte[] board=Read(stream,Size);
            return Valid(board,0,room)?board:null;
        }
        catch(IOException){return null;}
        catch(SocketException){return null;}
        finally{ClearKey(key);}
    }

    private void Publish(int pid,byte[] board)
    {
        int offset=NativeNetworkState.GuestRaceBoard;
        revision=unchecked(revision+2);
        byte[] complete=BitConverter.GetBytes(revision),writing=BitConverter.GetBytes(revision|1);
        Buffer.BlockCopy(BitConverter.GetBytes(Environment.TickCount),0,board,20,4);
        Buffer.BlockCopy(writing,0,board,0,4);
        Buffer.BlockCopy(complete,0,board,Size-4,4);
        if(NativeInjectionEngine.WriteNativeLobbyState(pid,offset,writing)&&NativeInjectionEngine.WriteNativeLobbyState(pid,offset,board))
            NativeInjectionEngine.WriteNativeLobbyState(pid,offset,complete);
    }
    private void Stop(){if(listener!=null){listener.Stop();listener=null;}}
    public void Dispose(){Stop();}
}
