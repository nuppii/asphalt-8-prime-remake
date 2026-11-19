using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace OfflineLab.Multiplayer
{
    // RFC 8489 Binding subset, not an ICE agent or a reachability guarantee.
    // The caller owns the socket and must serialize receives while probing it.
    public static class StunBinding
    {
        private const uint Cookie=0x2112a442;
        private static int U16(byte[] b,int p){return (b[p]<<8)|b[p+1];}
        private static uint U32(byte[] b,int p){return ((uint)b[p]<<24)|((uint)b[p+1]<<16)|((uint)b[p+2]<<8)|b[p+3];}
        private static bool Same(byte[] a,int offset,byte[] b){for(int i=0;i<b.Length;i++)if(a[offset+i]!=b[i])return false;return true;}
        public static byte[] CreateRequest(out byte[] transaction)
        {
            transaction=new byte[12];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(transaction);
            byte[] request=new byte[20];request[1]=1;request[4]=0x21;request[5]=0x12;request[6]=0xa4;request[7]=0x42;
            Buffer.BlockCopy(transaction,0,request,8,12);return request;
        }
        public static bool TryReadResponse(byte[] packet,byte[] transaction,out IPEndPoint mapped)
        {
            mapped=null;
            if(packet==null||transaction==null||transaction.Length!=12||packet.Length<20||packet.Length>65535)return false;
            int length=U16(packet,2);
            if(U16(packet,0)!=0x0101||U32(packet,4)!=Cookie||length%4!=0||length!=packet.Length-20||!Same(packet,8,transaction))return false;
            IPEndPoint candidate=null;
            for(int p=20;p<packet.Length;)
            {
                if(p+4>packet.Length)return false;
                int type=U16(packet,p),size=U16(packet,p+2),start=p+4;
                if(size>packet.Length-start)return false;
                int next=start+((size+3)&~3);if(next>packet.Length)return false;
                if(type==0x0020)
                {
                    if(size<4)return false;
                    int family=packet[start+1],bytes=family==1?4:family==2?16:0;
                    if(bytes==0||size!=bytes+4)return false;
                    int port=U16(packet,start+2)^0x2112;if(port==0)return false;
                    byte[] address=new byte[bytes];
                    for(int i=0;i<bytes;i++)address[i]=(byte)(packet[start+4+i]^(i<4?packet[4+i]:transaction[i-4]));
                    if(candidate==null)candidate=new IPEndPoint(new IPAddress(address),port);
                }
                else if(type==0x8028)
                {
                    if(size!=4||next!=packet.Length)return false;
                    uint crc=0xffffffff;
                    for(int i=0;i<p;i++){crc^=packet[i];for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320U:0U);}
                    if(U32(packet,start)!=((crc^0xffffffff)^0x5354554e))return false;
                }
                else if(type<0x8000&&type!=0x0001&&type!=0x0008&&type!=0x001c)
                    return false; // Unknown comprehension-required attribute.
                p=next;
            }
            // This unauthenticated Binding subset must not imply integrity validation.
            // Reject integrity attributes rather than silently accepting unverifiable MACs.
            for(int p=20;p<packet.Length;p+=4+((U16(packet,p+2)+3)&~3))
                if(U16(packet,p)==0x0008||U16(packet,p)==0x001c)return false;
            mapped=candidate;return mapped!=null;
        }
        public static IPEndPoint Probe(Socket socket,IPEndPoint server,int timeoutMilliseconds)
        {
            if(socket==null||server==null)throw new ArgumentNullException();
            if(socket.SocketType!=SocketType.Dgram||socket.ProtocolType!=ProtocolType.Udp||socket.Connected||!socket.Blocking)
                throw new ArgumentException("Use an unconnected, blocking UDP socket with an exclusive receive owner.");
            if(socket.AddressFamily!=server.AddressFamily||server.Port==0)throw new ArgumentException("The STUN endpoint must match the socket address family.");
            if(timeoutMilliseconds<100||timeoutMilliseconds>10000)throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            byte[] transaction,request=CreateRequest(out transaction),buffer=new byte[65535];
            int oldTimeout=socket.ReceiveTimeout;var clock=Stopwatch.StartNew();long nextSend=0;int interval=500;
            try
            {
                while(clock.ElapsedMilliseconds<timeoutMilliseconds)
                {
                    long now=clock.ElapsedMilliseconds;
                    if(now>=nextSend){socket.SendTo(request,server);nextSend=now+interval;interval=Math.Min(interval*2,4000);}
                    socket.ReceiveTimeout=(int)Math.Max(1,Math.Min(nextSend-clock.ElapsedMilliseconds,timeoutMilliseconds-clock.ElapsedMilliseconds));
                    EndPoint source=server.AddressFamily==AddressFamily.InterNetwork?new IPEndPoint(IPAddress.Any,0):new IPEndPoint(IPAddress.IPv6Any,0);
                    int count;
                    try{count=socket.ReceiveFrom(buffer,ref source);}
                    catch(SocketException ex){if(ex.SocketErrorCode==SocketError.TimedOut||ex.SocketErrorCode==SocketError.WouldBlock||ex.SocketErrorCode==SocketError.ConnectionReset)continue;throw;}
                    if(!server.Equals(source))continue;
                    byte[] packet=new byte[count];Buffer.BlockCopy(buffer,0,packet,0,count);IPEndPoint mapped;
                    if(TryReadResponse(packet,transaction,out mapped))return mapped;
                }
                throw new TimeoutException("No matching STUN Binding response arrived. Public reachability is not established.");
            }
            finally{socket.ReceiveTimeout=oldTimeout;}
        }
    }
}
