using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;
using OfflineLab.Multiplayer;

internal static class NativeTransportCheck
{
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void AssertNative(LanRoomListing room,string id)
    {Check(room.IsNative&&room.EffectiveTransport==RoomTransports.Asphalt480Udp&&room.NativeRoomId==id&&room.DiscoveryPort==6529&&room.Port==49001,"Native transport metadata was lost.");}
    public static int Main(string[] args)
    {
        try
        {
            var room=new LanRoomListing{RoomId=Guid.NewGuid().ToString("N"),Name="Native transport fixture",Address="127.0.0.1",Port=49001,Players=1,Capacity=8,
                Transport=RoomTransports.Asphalt480Udp,NativeRoomId="0123456789abcdef",DiscoveryPort=6529};
            Check(LanRoomDiscovery.ValidTransport(room),"Valid native listing was refused.");
            using(var stream=new MemoryStream())
            {
                var serializer=new DataContractJsonSerializer(typeof(LanRoomListing));serializer.WriteObject(stream,room);stream.Position=0;
                AssertNative((LanRoomListing)serializer.ReadObject(stream),room.NativeRoomId);
            }
            var legacy=new LanRoomListing();Check(legacy.EffectiveTransport==RoomTransports.RoomControlTcp&&!legacy.IsNative&&LanRoomDiscovery.ValidTransport(legacy),"Missing transport did not preserve legacy semantics.");
            room.DiscoveryPort=47780;Check(!LanRoomDiscovery.ValidTransport(room),"Wrong native discovery port was accepted.");room.DiscoveryPort=6529;
            room.NativeRoomId="invalid";Check(!LanRoomDiscovery.ValidTransport(room),"Invalid native ID was accepted.");room.NativeRoomId="0123456789abcdef";
            if(args.Length==1)
            {
                var directory=new Uri(args[0]);Check(directory.IsLoopback,"Only a local test service is supported.");
                using(var advertiser=new PublicRoomAdvertiser(directory,()=>room))
                {
                    LanRoomListing resolved=null;
                    for(int i=0;i<100;i++)
                    {
                        var choices=PublicRoomDirectory.Search(directory,room.Name);
                        if(choices.Count>0){resolved=choices.Find(x=>x.RoomId==room.RoomId);if(resolved!=null)break;}
                        Thread.Sleep(30);
                    }
                    Check(resolved!=null,"Native advertisement did not reach the directory.");AssertNative(resolved,room.NativeRoomId);
                    AssertNative(PublicRoomDirectory.Resolve(directory,room.RoomId),room.NativeRoomId);
                    var fetched=PublicRoomDirectory.Fetch(directory).Find(x=>x.RoomId==room.RoomId);AssertNative(fetched,room.NativeRoomId);
                    bool refused=false;
                    try{RoomInvites.ResolveForJoin(RoomInvites.CreateDirectory(room.RoomId,room.Name,directory));}
                    catch(InvalidOperationException error){refused=error.Message.Contains("native Asphalt UDP");}
                    Check(refused,"Native UDP directory reference was allowed into the TCP invitation resolver.");
                }
            }
            else Check(args.Length==0,"Usage: NativeTransportCheck [loopback-directory-url]");
            Console.WriteLine("PASS: native transport validation, metadata serialization and safe protocol separation.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
