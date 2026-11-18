using System;
using System.Linq;
using System.Threading;
using OfflineLab.Multiplayer;
internal static class DiscoveryCheck
{
    private static void Require(bool condition,string error){if(!condition)throw new Exception(error);}
    private static void Main(string[] args)
    {
        Require(LanRoomDiscovery.Discover(200).Count == 0,"An empty LAN scan returned rooms");
        Require(RoomErrors.Display(new System.Net.Sockets.SocketException(10054)).Contains("ConnectionReset"),"Network errors must use English status codes");
        Console.WriteLine("PASS empty LAN scan and English network errors");
        using(var host=RoomClient.Create("Discovery integration",new PlayerProfile{Nickname="Host"},new RoomSettings{GridSlots=40},"",0))
        {
            bool hidden=false;
            Func<LanRoomListing> listing=()=>new LanRoomListing{RoomId=host.Snapshot.RoomId,Name=host.Snapshot.Name,
                Address="127.0.0.1",Port=host.HostedServer.Port,Players=host.Snapshot.Members.Count,
                Capacity=40,IsPrivate=hidden};
            using(var lan=new LanRoomAdvertiser(listing))
            {
                Require(LanRoomDiscovery.Discover(400).Any(r=>r.RoomId==host.Snapshot.RoomId),"Public LAN room missing");
                hidden=true;
                Require(!LanRoomDiscovery.Discover(400).Any(r=>r.RoomId==host.Snapshot.RoomId),"Private room leaked into LAN discovery");
                hidden=false;
                Console.WriteLine("PASS LAN public discovery and private exclusion");
            }
            var directory=new Uri(args[0]);
            using(var publisher=new PublicRoomAdvertiser(directory,listing))
            {
                bool found=false;
                for(int i=0;i<25;i++)
                {
                    if(PublicRoomDirectory.Fetch(directory).Any(r=>r.RoomId==host.Snapshot.RoomId)){found=true;break;}
                    Thread.Sleep(100);
                }
                Require(found,"C# host was not published in the Python directory");
                Console.WriteLine("PASS C# announcement and public HTTP directory browse");
            }
            Require(!PublicRoomDirectory.Fetch(directory).Any(r=>r.RoomId==host.Snapshot.RoomId),"Directory lease was not removed");
            Console.WriteLine("PASS directory cleanup on host leave");
            bool rejected=false;
            try{PublicRoomDirectory.Validate(new Uri("http://example.test"));}catch(ArgumentException){rejected=true;}
            Require(rejected,"Non-local insecure directory accepted");
            Console.WriteLine("PASS HTTPS requirement");
        }
    }
}
