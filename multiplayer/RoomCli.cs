using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using OfflineLab.Multiplayer;

internal static class RoomCli
{
    private static readonly object OutputGate=new object();
    private static int Main(string[] args)
    {
        try
        {
            if(args.Length==1&&args[0]=="selftest")return SelfTest();
            if(args.Length==1&&args[0]=="migrationtest")return MigrationTest();
            if(args.Length<4){Console.WriteLine("Usage: RoomCli host|join <port> <password-or-dash> <nickname> [address]");return 2;}
            string password=args[2]=="-"?"":args[2];
            var profile=new PlayerProfile{Nickname=args[3]};
            RoomClient client=args[0]=="host"
                ?RoomClient.Create("Room integration test",profile,new RoomSettings{GridSlots=4,Laps=3,FillBots=true},password,int.Parse(args[1]))
                :RoomClient.Join(args.Length>4?args[4]:"127.0.0.1",int.Parse(args[1]),password,profile);
            using(client)
            {
                client.SnapshotChanged+=s=>Write("SNAP|"+SnapshotText(s));
                client.ErrorReceived+=message=>Write("ERROR|"+message);
                client.Disconnected+=message=>Write("CLOSED|"+message);
                Write("READY|"+(client.HostedServer==null?int.Parse(args[1]):client.HostedServer.Port)+"|"+client.LocalId);
                string line;
                while((line=Console.ReadLine())!=null)
                {
                    try
                    {
                        int split=line.IndexOf(' ');string command=split<0?line:line.Substring(0,split);
                        string value=split<0?"":line.Substring(split+1);
                        if(command=="QUIT")break;
                        if(command=="STATE")Write("STATE|"+SnapshotText(client.Snapshot));
                        else if(command=="META")Write("META|"+client.Snapshot.RoomId+"|"+client.Snapshot.HostId+"|"+client.LocalId+"|"+client.HostPort+"|"+(client.HostedServer!=null));
                        else if(command=="VOTE")client.Vote(value);
                        else if(command=="KICK")client.Kick(value);
                        else if(command=="PROMOTE")client.Promote(value);
                        else if(command=="NEXT")client.SelectNextMap();
                        else if(command=="RENAME")client.UpdateProfile(new PlayerProfile{Nickname=value,Status="Testing",GroupTag="LAB"});
                        else if(command=="SLOTS")client.UpdateSettings(new RoomSettings{GridSlots=int.Parse(value),Laps=3,FillBots=true});
                        else if(command=="START")client.RequestRaceStart();
                    }
                    catch(Exception ex){Write("ERROR|"+ex.Message);}
                }
            }
            return 0;
        }
        catch(Exception ex){Write("FAIL|"+ex.Message);return 1;}
    }
    private static string SnapshotText(RoomSnapshot state)
    {
        return state.Members.Count+"|"+state.BotCount+"|"+state.MapId+"|"+state.Votes.Count+"|"+
            string.Join(",",state.Members.Select(m=>m.Nickname+":"+m.IsModerator+":"+m.RttMs))+"|"+
            state.RaceBridgeReady+"|"+state.HostMigrationReady+"|"+state.IsPrivate;
    }
    private static void Write(string value){lock(OutputGate)Console.WriteLine(value);}
    private sealed class Child : IDisposable
    {
        internal readonly Process Process;
        private readonly List<string> lines=new List<string>();
        internal Child(string arguments)
        {
            Process=new Process{StartInfo=new ProcessStartInfo{FileName=System.Reflection.Assembly.GetExecutingAssembly().Location,
                Arguments=arguments,UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true}};
            Process.OutputDataReceived+=(_,e)=>{if(e.Data!=null)lock(lines){lines.Add(e.Data);Monitor.PulseAll(lines);}};
            Process.Start();Process.BeginOutputReadLine();
        }
        internal string Wait(string prefix,int timeout=8000)
        {
            var watch=Stopwatch.StartNew();lock(lines)
            {
                for(;;)
                {
                    string found=lines.FirstOrDefault(s=>s.StartsWith(prefix,StringComparison.Ordinal));
                    if(found!=null){lines.Remove(found);return found;}
                    if(watch.ElapsedMilliseconds>=timeout)throw new Exception("Timed out waiting for "+prefix+". Output: "+string.Join(" / ",lines));
                    Monitor.Wait(lines,100);
                }
            }
        }
        internal void Send(string command){Process.StandardInput.WriteLine(command);Process.StandardInput.Flush();}
        public void Dispose()
        {
            if(!Process.HasExited){try{Send("QUIT");}catch{}if(!Process.WaitForExit(4000))Process.Kill();}
            Process.Dispose();
        }
    }
    private static int SelfTest()
    {
        using(var host=new Child("host 0 test-secret Host"))
        {
            string[] ready=host.Wait("READY|").Split('|');int port=int.Parse(ready[1]);
            using(var wrong=new Child("join "+port+" incorrect Wrong"))
            {wrong.Wait("FAIL|Incorrect room password.");}
            using(var alice=new Child("join "+port+" test-secret Alice"))
            using(var bob=new Child("join "+port+" test-secret Bob"))
            {
                string aliceId=alice.Wait("READY|").Split('|')[2];bob.Wait("READY|");
                host.Wait("SNAP|3|1|");
                alice.Send("VOTE Iceland");bob.Send("VOTE Iceland");host.Wait("SNAP|3|1|Sector 8|2|");
                alice.Send("SLOTS 1");alice.Wait("ERROR|Only the room host");
                host.Send("SLOTS 2");host.Wait("ERROR|The grid cannot be smaller");
                host.Send("SLOTS 3");host.Wait("SNAP|3|0|");
                using(var full=new Child("join "+port+" test-secret Full")){full.Wait("FAIL|The room is full.");}
                host.Send("NEXT");host.Wait("SNAP|3|0|Iceland|0|");
                host.Send("PROMOTE "+aliceId);alice.Wait("SNAP|3|0|Iceland|0|");
                alice.Send("RENAME AliceNew");host.Wait("SNAP|3|0|Iceland|0|");
                host.Send("STATE");string state=host.Wait("STATE|");
                if(!state.Contains("AliceNew:True"))
                {Thread.Sleep(150);host.Send("STATE");state=host.Wait("STATE|");if(!state.Contains("AliceNew:True"))throw new Exception("Promotion/profile state was not synchronized.");}
                host.Send("START");host.Wait("ERROR|Synchronized racing is unavailable");
                Thread.Sleep(3200);host.Send("STATE");state=host.Wait("STATE|");
                if(state.Contains(":-1"))throw new Exception("RTT heartbeat was not measured.");
                host.Send("KICK "+aliceId);alice.Wait("CLOSED|You were excluded");host.Wait("SNAP|2|1|");
                bob.Send("QUIT");host.Wait("SNAP|1|2|");
            }
        }
        // Default means the stock-event placeholder, not zero or infinite.
        var defaults=new RoomSettings{GridSlots=0,Laps=0};defaults.Validate();
        if(defaults.EffectiveGridSlots!=8||defaults.EffectiveLaps!=3)throw new Exception("Default fallback mismatch.");
        var large=new RoomSettings{GridSlots=40,FillBots=true};large.Validate();
        if(large.EffectiveGridSlots-20!=20)throw new Exception("Bot fill calculation mismatch.");
        Console.WriteLine("PASS: separate-process room authentication, joins, capacity, voting, authorization, profiles, promotion, RTT, kick, leave, default and bot-fill checks.");
        return 0;
    }
    private static int MigrationTest()
    {
        using(var host=new Child("host 0 transfer-secret Host"))
        {
            string[] ready=host.Wait("READY|").Split('|');int port=int.Parse(ready[1]);
            host.Send("META");string roomId=host.Wait("META|").Split('|')[1];
            using(var alice=new Child("join "+port+" transfer-secret Alice"))
            using(var bob=new Child("join "+port+" transfer-secret Bob"))
            {
                string aliceId=alice.Wait("READY|").Split('|')[2];
                string bobId=bob.Wait("READY|").Split('|')[2];
                host.Wait("SNAP|3|1|");
                alice.Send("VOTE Iceland");bob.Send("VOTE Nevada");host.Wait("SNAP|3|1|Sector 8|2|");
                host.Send("PROMOTE "+aliceId);
                host.Wait("SNAP|3|1|Sector 8|2|");
                Thread.Sleep(3300);
                host.Send("STATE");string beforeHandover=host.Wait("STATE|");
                string[] members=beforeHandover.Split('|')[5].Split(',');
                int aliceRtt=int.Parse(members.First(m=>m.StartsWith("Alice:",StringComparison.Ordinal)).Split(':')[2]);
                int bobRtt=int.Parse(members.First(m=>m.StartsWith("Bob:",StringComparison.Ordinal)).Split(':')[2]);
                string expectedHost=aliceRtt<bobRtt?aliceId:bobRtt<aliceRtt?bobId:
                    string.CompareOrdinal(aliceId,bobId)<0?aliceId:bobId;
                host.Send("QUIT");
                if(!host.Process.WaitForExit(7000)||host.Process.ExitCode!=0)throw new Exception("The original host did not leave cleanly.");
                alice.Wait("SNAP|2|2|Sector 8|2|",10000);bob.Wait("SNAP|2|2|Sector 8|2|",10000);
                alice.Send("META");bob.Send("META");
                string[] a=alice.Wait("META|").Split('|'),b=bob.Wait("META|").Split('|');
                if(a[1]!=roomId||b[1]!=roomId||a[3]!=aliceId||b[3]!=bobId||a[2]!=b[2])throw new Exception("The handover changed room/player identities.");
                if(a[2]!=expectedHost)throw new Exception("The handover did not select the lowest-RTT eligible peer.");
                bool aliceHosts=a[5]=="True";
                alice.Send("STATE");string promotedState=alice.Wait("STATE|");
                if(!promotedState.Contains("Alice:True"))throw new Exception("The handover lost an existing moderator role.");
                Child nextHost=aliceHosts?alice:bob,other=aliceHosts?bob:alice;
                nextHost.Send("SLOTS 5");nextHost.Wait("SNAP|2|3|");other.Wait("SNAP|2|3|");
                other.Send("SLOTS 6");other.Wait("ERROR|Only the room host");
                nextHost.Send("NEXT");nextHost.Wait("SNAP|2|3|Iceland|0|");other.Wait("SNAP|2|3|Iceland|0|");
                nextHost.Send("QUIT");
                if(!nextHost.Process.WaitForExit(7000)||nextHost.Process.ExitCode!=0)throw new Exception("The second host did not leave cleanly.");
                other.Wait("SNAP|1|4|Iceland|0|",10000);other.Send("META");
                string[] last=other.Wait("META|").Split('|');
                if(last[1]!=roomId||last[5]!="True"||last[2]!=last[3])throw new Exception("The second handover did not preserve the room.");
                int migratedPort=int.Parse(last[4]);
                using(var wrong=new Child("join "+migratedPort+" wrong Rejected")){wrong.Wait("FAIL|Incorrect room password.");}
                using(var joined=new Child("join "+migratedPort+" transfer-secret Newcomer"))
                {joined.Wait("READY|");other.Wait("SNAP|2|3|Iceland|0|");}
            }
        }
        Write("PASS: three separate processes, graceful host leave, room/member ID retention, vote/settings/map preservation, two consecutive transfers, continued host permissions and private-room authentication.");
        return 0;
    }
}
