using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using OfflineLab.Multiplayer;

internal static class NativeInjectionEngine
{
    internal static bool DirectReady;
    internal static bool NativeDirectDiscoveryReady(int pid){return DirectReady;}
    internal static byte[] State=new byte[NativeNetworkState.BufferBytes];internal static int Pid=111;
    internal static bool ReadNativeLobbyState(out int pid,out byte[] state){pid=Pid;state=(byte[])State.Clone();return true;}
    internal static void WriteNativeLobbyState(int pid,int offset,byte[] data){if(pid!=Pid)throw new Exception("Stale process write.");Buffer.BlockCopy(data,0,State,offset,data.Length);}
    internal static bool QueueDriverName(string name,out string error){error=null;return true;}
}
internal static class NativeBridgeCheck
{
    private static readonly MethodInfo poll=typeof(NativeLobbyBridge).GetMethod("Poll",BindingFlags.Instance|BindingFlags.NonPublic);
    private static void Tick(NativeLobbyBridge bridge){poll.Invoke(bridge,null);}
    private static void Word(int offset,int value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,NativeInjectionEngine.State,offset,4);}
    private static int Word(int offset){return BitConverter.ToInt32(NativeInjectionEngine.State,offset);}
    private static void Text(int offset,int length,string text){Array.Clear(NativeInjectionEngine.State,offset,length);byte[] data=Encoding.UTF8.GetBytes(text);Buffer.BlockCopy(data,0,NativeInjectionEngine.State,offset,data.Length);}
    private static string Text(int offset,int length){return Encoding.UTF8.GetString(NativeInjectionEngine.State,offset,length).TrimEnd('\0');}
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message+" Status: "+Text(52,160));}
    private static void Request(NativeLobbyBridge bridge,int kind)
    {Word(20,kind);Word(12,Word(12)+1);Tick(bridge);}
    private static void Await(NativeLobbyBridge bridge,Func<bool> ready)
    {for(int i=0;i<300;i++){Thread.Sleep(20);Tick(bridge);if(ready())return;}throw new Exception("Bridge operation timed out: "+Text(52,160));}
    [STAThread] public static int Main(string[] args)
    {
        string folder=Path.Combine(Path.GetTempPath(),"a8-native-social-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            if(args.Length!=1||!new Uri(args[0]).IsLoopback)throw new Exception("A local test service URL is required.");
            SocialIdentity peerIdentity=SocialClient.Register(new Uri(args[0]),"Test peer");
            var peer=new SocialClient(new Uri(args[0]),peerIdentity);string localId;
            using(var bridge=new NativeLobbyBridge(folder,false))
            {
                Tick(bridge);Text(436,1024,args[0]);Request(bridge,10);
                Await(bridge,()=>Word(44)==12);
                string searchName="Dropdown "+Guid.NewGuid().ToString("N");
                var listing=new LanRoomListing{RoomId=Guid.NewGuid().ToString("N"),Name=searchName,Address="127.0.0.1",Port=47781,Players=4,Capacity=4};
                using(var advertiser=new PublicRoomAdvertiser(new Uri(args[0]),()=>listing))
                {
                    Await(bridge,()=>PublicRoomDirectory.Search(new Uri(args[0]),searchName).Count==1);
                    Text(308,64,searchName);Request(bridge,2);
                    Check(Word(48)==300&&Word(1460)==0&&Word(1464)==-1,"Search did not clear old selection while loading.");
                    Await(bridge,()=>Word(48)==301);
                    Check(Word(1460)==1&&Word(1464)==-1&&Text(1480,80).Contains("4/4  FULL")&&Text(1560,64)==listing.RoomId,"Dropdown results or explicit-selection state differ from the directory.");
                    Text(308,64,"No match "+Guid.NewGuid().ToString("N"));Request(bridge,2);Await(bridge,()=>Word(48)==302);
                    Check(Word(1460)==0&&Word(1464)==-1,"Empty search kept a stale room selection.");
                    Text(308,64,searchName);Request(bridge,2);Text(308,64,"Changed while searching");Await(bridge,()=>Word(48)==306);
                    Check(Word(1460)==0&&Word(1464)==-1,"Stale query completion repopulated the dropdown.");
                }
                Request(bridge,3);Await(bridge,()=>Text(52,160).Contains("Your player ID:"));
                localId=Text(52,160).Split(new[]{"Your player ID: "},StringSplitOptions.None)[1];
                Text(308,64,peer.PlayerId);Request(bridge,11);Await(bridge,()=>Text(52,160).Contains("Friend request sent"));
                var pending=peer.GetRequests();Check(pending.Count==1,"Bridge did not send a real request.");peer.Accept(pending[0].RequestId);
                Request(bridge,3);Await(bridge,()=>Word(1468)==1);
                Check(Text(2712,64)==peer.PlayerId,"Wrong friend row ID.");
                Text(436,1024,RoomInvites.Encode(RoomInvites.CreateDirect("bridge-invite-test","Native test reference","127.0.0.1",47781,true)));
                Request(bridge,8);Await(bridge,()=>Text(52,160).Contains("Invitation delivered"));
                Check(peer.GetInvitations().Count==1,"Native invite was not delivered to the selected friend.");
                var third=new SocialClient(new Uri(args[0]),SocialClient.Register(new Uri(args[0]),"Accept test peer"));
                third.RequestFriend(localId);Request(bridge,17);
                Await(bridge,()=>Text(2632,80).StartsWith("Request:"));
                Request(bridge,12);Await(bridge,()=>Text(52,160).Contains("Friend request accepted"));
                Check(third.GetFriends().Count==1,"Native acceptance did not create friendship.");
                var fourth=new SocialClient(new Uri(args[0]),SocialClient.Register(new Uri(args[0]),"Reject test peer"));
                fourth.RequestFriend(localId);Request(bridge,17);
                Await(bridge,()=>Text(2632,80).Contains("Reject test peer")&&Word(1468)==1);
                Request(bridge,13);Await(bridge,()=>Text(52,160).Contains("Friend request rejected"));
                Check(fourth.GetFriends().Count==0&&fourth.GetRequests().Count==0,"Native rejection failed.");
                peer.SendInvitation(localId,RoomInvites.CreateDirect("bridge-test-room","Test room","127.0.0.1",47781,true));
                Request(bridge,14);Await(bridge,()=>Text(2632,80).StartsWith("Invite:"));
                Request(bridge,15);Await(bridge,()=>Text(52,160).Contains("not joined"));
                Request(bridge,16);Await(bridge,()=>Word(1468)==0&&Text(52,160).Contains("dismissed"));
                Check(Directory.GetFiles(folder,"*.private.bin").Length==1,"Private credentials not persisted.");
                byte[] raw=File.ReadAllBytes(Directory.GetFiles(folder,"*.private.bin")[0]);
                Check(!Encoding.UTF8.GetString(raw).Contains(localId),"Credentials were not protected.");
                NativeInjectionEngine.Pid=222;Tick(bridge);Request(bridge,3);
                NativeInjectionEngine.Pid=333;Tick(bridge);Await(bridge,()=>Word(44)==12);
            }
            using(var again=new NativeLobbyBridge(folder,false))
            {
                Tick(again);Check(Text(436,1024)==new Uri(args[0]).AbsoluteUri,"Configured directory was not restored in the edit buffer.");
                Request(again,3);Await(again,()=>Word(1468)==2);Check(peer.GetFriends()[0].PlayerId==localId,"Identity did not survive bridge restart.");
                Text(436,1024,"");Request(again,10);
                Check(Word(44)==8&&Word(1468)==0&&Text(436,1024)=="","Disconnect did not clear social readiness and results.");
                Request(again,3);Check(Text(52,160).Contains("Configure a directory URL"),"Cleared directory was still used.");
                Text(308,64,"Test room");Request(again,2);Check(Word(48)==303&&Word(1460)==0&&Word(1464)==-1,"Unavailable directory retained dropdown results.");
                Text(436,1024,args[0].TrimEnd('/')+"/missing");Request(again,10);Word(28,2);Text(52,160,"Room list status stays visible.");Word(48,301);
                Tick(again);Await(again,()=>!(bool)typeof(NativeLobbyBridge).GetField("busy",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(again));
                Check(Text(52,160)=="Room list status stays visible."&&Word(48)==301,"Background social failure overwrote the room-search status.");
                Text(436,1024,"");Request(again,10);
            }
            // Simulated native extension, real HTTP directory. No native game is joined.
            NativeInjectionEngine.Pid=444;NativeInjectionEngine.DirectReady=true;
            Word(NativeNetworkState.Offset,unchecked((int)NativeNetworkState.Magic));Word(NativeNetworkState.Offset+4,1);Word(NativeNetworkState.Offset+8,NativeNetworkState.Bytes);
            using(var native=new NativeLobbyBridge(folder,false))
            {
                Tick(native);Text(436,1024,args[0]);Request(native,10);Await(native,()=>(Word(44)&4)!=0);
                string nativeName="Native join "+Guid.NewGuid().ToString("N");
                var listing=new LanRoomListing{RoomId=Guid.NewGuid().ToString("N"),Name=nativeName,Address="127.0.0.1",Port=49001,Players=1,Capacity=8,Transport=RoomTransports.Asphalt480Udp,NativeRoomId="1122334455667788",DiscoveryPort=6529};
                using(var advertisement=new PublicRoomAdvertiser(new Uri(args[0]),()=>listing))
                {
                    Await(native,()=>PublicRoomDirectory.Search(new Uri(args[0]),nativeName).Count==1);
                    Text(308,64,nativeName);Request(native,2);Await(native,()=>Word(48)==301);
                    Check(Word(NativeNetworkState.JoinableMask)==1&&(Word(44)&2)!=0,"Available native listing was not marked joinable.");
                    Word(1464,0);Request(native,7);Await(native,()=>Word(NativeNetworkState.JoinSeq)==1);
                    Check(Word(NativeNetworkState.JoinAck)==0&&Word(NativeNetworkState.JoinPhase)==1&&Text(NativeNetworkState.JoinAddress,16)=="127.0.0.1"&&Word(NativeNetworkState.JoinLobbyPort)==49001,"Native join payload or queued state is wrong.");
                    Check(Word(NativeNetworkState.JoinIdLow)==0x55667788&&Word(NativeNetworkState.JoinIdHigh)==0x11223344,"Directory native ID changed byte order.");
                    Request(native,7);Check(Word(NativeNetworkState.JoinSeq)==1&&Text(52,160).Contains("already in progress"),"Repeated join replaced an active native attempt.");
                    Word(NativeNetworkState.JoinAck,1);Word(NativeNetworkState.JoinPhase,5);
                }
                Request(native,7);Await(native,()=>Word(48)==308);Check(Word(NativeNetworkState.JoinSeq)==1,"Expired lease was queued into the game.");
                Word(NativeNetworkState.HostRevision,2);Word(NativeNetworkState.HostEndRevision,2);Word(NativeNetworkState.HostAlive,1);Word(NativeNetworkState.HostIdLow,0x55667788);Word(NativeNetworkState.HostIdHigh,0x11223344);Word(NativeNetworkState.HostLobbyPort,49002);Word(NativeNetworkState.HostDiscoveryPort,6529);Word(NativeNetworkState.HostPlayers,2);Word(NativeNetworkState.HostCapacity,8);Word(NativeNetworkState.HostPrivate,0);Text(NativeNetworkState.HostName,64,"Native publication fixture");
                Tick(native);Await(native,()=>PublicRoomDirectory.Search(new Uri(args[0]),"Native publication fixture").Count==1);
                var host=PublicRoomDirectory.Search(new Uri(args[0]),"Native publication fixture")[0];Check(host.IsNative&&host.NativeRoomId=="1122334455667788"&&host.Port==49002&&host.Players==2&&host.RoomId!=host.NativeRoomId,"Native host observation was not published with separate directory identity.");
                Word(NativeNetworkState.HostRevision,3);Word(NativeNetworkState.HostEndRevision,3);Word(NativeNetworkState.HostAlive,0);Tick(native);Thread.Sleep(100);
                Check(PublicRoomDirectory.Search(new Uri(args[0]),"Native publication fixture").Count==1,"In-progress native observation withdrew a live advertisement.");
                Word(NativeNetworkState.HostRevision,4);Word(NativeNetworkState.HostEndRevision,4);Word(NativeNetworkState.HostAlive,1);Word(NativeNetworkState.HostPrivate,1);Tick(native);Await(native,()=>PublicRoomDirectory.Search(new Uri(args[0]),"Native publication fixture").Count==0);
                Word(NativeNetworkState.HostAlive,0);Text(436,1024,"");Request(native,10);
            }
            NativeInjectionEngine.DirectReady=false;
            File.WriteAllText(Path.Combine(folder,"native-profile.json"),"{\"Policy\":20,\"Grid\":0,\"Laps\":0,\"RoomName\":\"Test\",\"Nickname\":\"Driver\",\"DirectoryUrl\":\"https://[broken\"}");
            using(var malformed=new NativeLobbyBridge(folder,false)){Tick(malformed);Check(Text(436,1024)=="","Malformed persisted URL was not reset safely.");}
            Console.WriteLine("PASS: native bridge social HTTP operations, invitation resolve-only, protected identity persistence, PID changes and disposal.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex.ToString());return 1;}
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
