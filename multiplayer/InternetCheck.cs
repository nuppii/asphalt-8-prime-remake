using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using OfflineLab.Multiplayer;

internal static class InternetCheck
{
    private static int checks;
    private static void Check(bool ok,string description){if(!ok)throw new Exception(description);checks++;}
    private static byte[] Hex(string text){byte[] b=new byte[text.Length/2];for(int i=0;i<b.Length;i++)b[i]=Convert.ToByte(text.Substring(i*2,2),16);return b;}
    private static byte[] Reply(byte[] request,IPAddress address,int port)
    {
        byte[] ip=address.GetAddressBytes(),b=new byte[28+ip.Length];b[0]=1;b[1]=1;b[3]=(byte)(b.Length-20);
        Buffer.BlockCopy(request,4,b,4,16);b[21]=0x20;b[23]=(byte)(4+ip.Length);b[25]=(byte)(ip.Length==4?1:2);
        int encoded=port^0x2112;b[26]=(byte)(encoded>>8);b[27]=(byte)encoded;
        for(int i=0;i<ip.Length;i++)b[28+i]=(byte)(ip[i]^request[4+i]);return b;
    }
    private static int SelfTest()
    {
        byte[] tx,request=StunBinding.CreateRequest(out tx);IPEndPoint result;
        Check(request.Length==20&&tx.Length==12,"Binding request size");
        foreach(string ip in new[]{"203.0.113.7","2001:db8::1234"})
        {
            byte[] packet=Reply(request,IPAddress.Parse(ip),45678);
            Check(StunBinding.TryReadResponse(packet,tx,out result)&&result.Equals(new IPEndPoint(IPAddress.Parse(ip),45678)),"XOR mapping");
            for(int n=0;n<packet.Length;n++){byte[] shortPacket=new byte[n];Buffer.BlockCopy(packet,0,shortPacket,0,n);Check(!StunBinding.TryReadResponse(shortPacket,tx,out result),"Truncated response accepted");}
            packet[8]^=1;Check(!StunBinding.TryReadResponse(packet,tx,out result),"Transaction mismatch accepted");
        }
        byte[] invalid=Reply(request,IPAddress.Loopback,1234);invalid[21]=2;
        Check(!StunBinding.TryReadResponse(invalid,tx,out result),"Unknown required attribute accepted");
        // Independent CRC fixture generated with Python zlib.crc32, not this codec.
        byte[] fingerprint=Hex("010100142112a442000102030405060708090a0b002000080001a147e112a64380280004690e4207");
        byte[] fixedTx=Hex("000102030405060708090a0b");
        Check(StunBinding.TryReadResponse(fingerprint,fixedTx,out result)&&result.Equals(new IPEndPoint(IPAddress.Parse("192.0.2.1"),32853)),"FINGERPRINT fixture");
        fingerprint[fingerprint.Length-1]^=1;Check(!StunBinding.TryReadResponse(fingerprint,fixedTx,out result),"Invalid CRC accepted");
        var random=new Random(8489);
        for(int i=0;i<1000;i++)
        {
            byte[] malformed=new byte[20+4*random.Next(0,24)];random.NextBytes(malformed);
            Buffer.BlockCopy(request,0,malformed,0,20);malformed[0]=1;malformed[1]=1;malformed[2]=0;malformed[3]=(byte)(malformed.Length-20);
            StunBinding.TryReadResponse(malformed,tx,out result); // Arbitrary TLVs must remain bounded.
        }
        using(var server=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))
        using(var other=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))
        using(var client=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp))
        {
            server.Client.ReceiveTimeout=2000;client.Bind(new IPEndPoint(IPAddress.Loopback,0));client.ReceiveTimeout=777;
            Exception failure=null;
            var responder=new Thread(()=>{try{
                IPEndPoint peer=new IPEndPoint(IPAddress.Any,0);byte[] query=server.Receive(ref peer);
                byte[] spoof=Reply(query,IPAddress.Parse("203.0.113.99"),1);other.Send(spoof,spoof.Length,peer);
                byte[] wrong=(byte[])query.Clone();wrong[8]^=1;byte[] wrongReply=Reply(wrong,peer.Address,peer.Port);server.Send(wrongReply,wrongReply.Length,peer);
                // Wait for retransmission: same transaction and same local source port.
                IPEndPoint again=new IPEndPoint(IPAddress.Any,0);byte[] retry=server.Receive(ref again);
                if(!peer.Equals(again)||Convert.ToBase64String(query)!=Convert.ToBase64String(retry))throw new Exception("Retry changed socket or transaction");
                byte[] response=Reply(retry,again.Address,again.Port);server.Send(response,response.Length,again);
            }catch(Exception ex){failure=ex;}}){IsBackground=true};responder.Start();
            result=StunBinding.Probe(client,(IPEndPoint)server.Client.LocalEndPoint,1800);
            Check(responder.Join(2200)&&failure==null,"STUN responder failed");
            Check(result.Equals(client.LocalEndPoint),"Reflexive socket endpoint differs");
            Check(client.ReceiveTimeout==777,"Socket timeout not restored");
            bool timeout=false;try{StunBinding.Probe(client,(IPEndPoint)other.Client.LocalEndPoint,120);}catch(TimeoutException){timeout=true;}
            Check(timeout&&client.ReceiveTimeout==777,"Bounded failure or timeout restoration failed");
        }
        Console.WriteLine("PASS: "+checks+" STUN codec/socket checks; no Internet service contacted.");return 0;
    }
    public static int Main(string[] args)
    {
        try
        {
            if(args.Length==1&&args[0]=="selftest")return SelfTest();
            IPAddress address;int port;
            if(args.Length!=3||args[0]!="stun"||!IPAddress.TryParse(args[1],out address)||!int.TryParse(args[2],out port)||port<1||port>65535)
            {Console.WriteLine("Usage: InternetCheck selftest | stun <explicit-server-IP> <port>");return 2;}
            using(var socket=new Socket(address.AddressFamily,SocketType.Dgram,ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(address.AddressFamily==AddressFamily.InterNetwork?IPAddress.Any:IPAddress.IPv6Any,0));
                IPEndPoint mapped=StunBinding.Probe(socket,new IPEndPoint(address,port),5000);
                Console.WriteLine("Observed UDP endpoint: "+mapped+". This is not a peer reachability check or a native game connection.");
            }
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
    }
}
