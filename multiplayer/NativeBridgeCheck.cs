using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using OfflineLab.Multiplayer;

internal static class NativeInjectionEngine
{
    internal static bool NativeDirectDiscoveryReady(int pid){return false;}
    internal static byte[] State=new byte[3784];internal static int Pid=111;
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
            }
            File.WriteAllText(Path.Combine(folder,"native-profile.json"),"{\"Policy\":20,\"Grid\":0,\"Laps\":0,\"RoomName\":\"Test\",\"Nickname\":\"Driver\",\"DirectoryUrl\":\"https://[broken\"}");
            using(var malformed=new NativeLobbyBridge(folder,false)){Tick(malformed);Check(Text(436,1024)=="","Malformed persisted URL was not reset safely.");}
            Console.WriteLine("PASS: native bridge social HTTP operations, invitation resolve-only, protected identity persistence, PID changes and disposal.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex.ToString());return 1;}
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
