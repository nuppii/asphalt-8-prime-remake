using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

// Explicit opt-in test installer. Never runs the trainer entry point or enumerates games.
internal static class HookHarness
{
    private const string OriginalHash="6a5d3933f92809297631f1299c9906ec40fbb9d585a8c77115f321bfe733134c";
    private sealed class Hook
    {
        internal string Name;internal int Rva,Size;internal byte[] Expected,Saved,Code,Patch;
        internal bool[] Mask;internal Func<long,long,byte[]> Build;internal IntPtr Cave;
    }
    private sealed class PausedThread
    {
        internal int Id;internal IntPtr Handle;
    }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] data,int length,out IntPtr count);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool WriteProcessMemory(IntPtr process,IntPtr address,byte[] data,int length,out IntPtr count);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr VirtualAllocEx(IntPtr process,IntPtr address,UIntPtr size,uint kind,uint protection);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool VirtualProtectEx(IntPtr process,IntPtr address,UIntPtr size,uint protection,out uint old);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool FlushInstructionCache(IntPtr process,IntPtr address,UIntPtr size);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr OpenThread(uint access,bool inherit,uint threadId);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern uint SuspendThread(IntPtr thread);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetThreadContext(IntPtr thread,IntPtr context);
    private static void Pause(Process target,List<PausedThread> paused)
    {
        var seen=new HashSet<int>();
        for(int pass=0;pass<4;pass++)
        {
            bool added=false;target.Refresh();
            foreach(ProcessThread thread in target.Threads)
            using(thread)
            {
                if(seen.Contains(thread.Id))continue;
                IntPtr handle=OpenThread(0x004a,false,(uint)thread.Id);
                Require(handle!=IntPtr.Zero,"Cannot open target thread "+thread.Id+" (Win32 "+Marshal.GetLastWin32Error()+").");
                if(SuspendThread(handle)==uint.MaxValue)
                {int failure=Marshal.GetLastWin32Error();CloseHandle(handle);throw new InvalidOperationException("Cannot suspend target thread "+thread.Id+" (Win32 "+failure+").");}
                paused.Add(new PausedThread{Id=thread.Id,Handle=handle});seen.Add(thread.Id);added=true;
            }
            if(!added){Require(paused.Count>0,"No accessible target threads.");return;}
        }
        throw new InvalidOperationException("Target thread membership did not stabilize; installation refused.");
    }
    private static bool InHook(long instruction,long module,IEnumerable<Hook> hooks)
    {foreach(Hook hook in hooks)if(instruction>=module+hook.Rva&&instruction<module+hook.Rva+hook.Expected.Length)return true;return false;}
    private static void CheckPositions(List<PausedThread> paused,long module,List<Hook> hooks)
    {
        IntPtr storage=Marshal.AllocHGlobal(731);
        IntPtr context=new IntPtr(unchecked((int)((storage.ToInt64()+15)&~15L)));
        try
        {
            byte[] empty=new byte[716];
            foreach(PausedThread thread in paused)
            {
                Marshal.Copy(empty,0,context,empty.Length);Marshal.WriteInt32(context,0,0x10001);
                Require(GetThreadContext(thread.Handle,context),"Cannot inspect target thread "+thread.Id+" (Win32 "+Marshal.GetLastWin32Error()+").");
                long instruction=unchecked((uint)Marshal.ReadInt32(context,184));
                Require(!InHook(instruction,module,hooks),"Target thread "+thread.Id+" is inside a hook site; installation refused.");
            }
        }
        finally{Marshal.FreeHGlobal(storage);}
    }
    private static void Resume(List<PausedThread> paused)
    {
        var errors=new List<string>();
        for(int i=paused.Count-1;i>=0;i--)
        {
            PausedThread thread=paused[i];
            if(ResumeThread(thread.Handle)==uint.MaxValue)errors.Add("thread "+thread.Id+" (Win32 "+Marshal.GetLastWin32Error()+")");
            CloseHandle(thread.Handle);
        }
        Require(errors.Count==0,"Cannot resume target "+String.Join(", ",errors.ToArray()));
    }
    private static string Hash(byte[] data){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static byte[] Read(IntPtr process,long address,int length)
    {byte[] data=new byte[length];IntPtr count;Require(ReadProcessMemory(process,new IntPtr(address),data,length,out count)&&count.ToInt64()==length,"Cannot read complete target memory.");return data;}
    private static void Write(IntPtr process,long address,byte[] data)
    {IntPtr count;Require(WriteProcessMemory(process,new IntPtr(address),data,data.Length,out count)&&count.ToInt64()==data.Length,"Cannot write complete target memory.");}
    private static void Branch(IntPtr process,long address,byte[] data)
    {
        uint old;Require(VirtualProtectEx(process,new IntPtr(address),(UIntPtr)data.Length,0x40,out old),"Cannot protect hook site.");
        try{Write(process,address,data);Require(FlushInstructionCache(process,new IntPtr(address),(UIntPtr)data.Length),"Cannot flush hook site.");}
        finally{uint ignored;Require(VirtualProtectEx(process,new IntPtr(address),(UIntPtr)data.Length,old,out ignored),"Cannot restore hook-site protection.");}
    }
    private static bool Equal(byte[] first,byte[] second)
    {if(first.Length!=second.Length)return false;for(int i=0;i<first.Length;i++)if(first[i]!=second[i])return false;return true;}
    private static T Field<T>(object value,string name)
    {return (T)value.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(value);}
    private static Hook Definition(object value)
    {return new Hook{Name=Field<string>(value,"Name"),Rva=Field<int>(value,"Rva"),Size=Field<int>(value,"AllocationSize"),Expected=Field<byte[]>(value,"Original"),Mask=Field<bool[]>(value,"OriginalMask"),Build=Field<Func<long,long,byte[]>>(value,"BuildCode")};}
    private static void VerifyGame(string path)
    {
        byte[] data=File.ReadAllBytes(path);
        if(Hash(data)==OriginalHash)return;
        Require(data.Length>0xBBAE5A,"Unsupported game image length.");
        byte[] force={0xc6,0x83,0xa5,0,0,0,1};
        for(int i=0;i<64;i++)Require(data[0xBBAE1A+i]==(i<force.Length?force[i]:(byte)0x90),"Unsupported on-disk compliance patch.");
        Require(data[0x4AA48B]==0xeb&&data[0x4AA48C]==0x5e,"Unsupported on-disk age branch.");
        // Verify against the trusted original backup, then derive exactly the supported patch.
        string backup=Path.Combine(Path.GetDirectoryName(path),"_offline_patch_backup","Asphalt8.exe.original");
        Require(File.Exists(backup),"Patched image requires the original hash-verified backup.");
        byte[] original=File.ReadAllBytes(backup);Require(Hash(original)==OriginalHash,"Original backup hash mismatch.");
        Buffer.BlockCopy(force,0,original,0xBBAE1A,force.Length);for(int i=force.Length;i<64;i++)original[0xBBAE1A+i]=0x90;
        original[0x4AA48B]=0xeb;original[0x4AA48C]=0x5e;
        Require(Equal(data,original),"The game differs from the supported original plus exact offline patches.");
    }
    private static Dictionary<string,string> Options(string[] args)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(string arg in args)if(arg=="--apply"){Require(!result.ContainsKey(arg),"Duplicate option.");result[arg]="yes";}
        for(int i=0;i<args.Length;i++)
        {
            string name=args[i];if(name=="--apply")continue;
            Require(name=="--pid"||name=="--module"||name=="--trainer"||name=="--trainer-sha256","Unknown option: "+name);
            Require(i+1<args.Length&&!result.ContainsKey(name),"Missing or duplicate option: "+name);result[name]=args[++i];
        }
        foreach(string name in new[]{"--pid","--module","--trainer","--trainer-sha256"})Require(result.ContainsKey(name),"Required option: "+name);
        return result;
    }
    public static int Main(string[] args)
    {
        IntPtr process=IntPtr.Zero;
        try
        {
            if(args.Length==1&&args[0]=="--selftest")return SelfTest();
            Require(IntPtr.Size==4,"Build and run this harness as x86.");
            var options=Options(args);int pid;Require(int.TryParse(options["--pid"],out pid)&&pid>0,"Invalid explicit PID.");
            Require(pid!=Process.GetCurrentProcess().Id,"The harness cannot target itself.");
            string module=Path.GetFullPath(options["--module"]),trainer=Path.GetFullPath(options["--trainer"]);
            Require(string.Equals(Path.GetFileName(module),"Asphalt8.exe",StringComparison.OrdinalIgnoreCase),"Module must be Asphalt8.exe.");
            Require(Hash(File.ReadAllBytes(trainer))==options["--trainer-sha256"].ToLowerInvariant(),"Trainer assembly hash mismatch.");
            VerifyGame(module);
            using(var target=Process.GetProcessById(pid))
            {
                DateTime started=target.StartTime;
                Require(!target.HasExited&&string.Equals(Path.GetFullPath(target.MainModule.FileName),module,StringComparison.OrdinalIgnoreCase),"PID module path differs from the explicit target.");
                long address=target.MainModule.BaseAddress.ToInt64();int moduleSize=target.MainModule.ModuleMemorySize;
                Assembly assembly=Assembly.LoadFrom(trainer);Type catalog=assembly.GetType("InjectionCatalog",true);
                var hooks=new List<Hook>();
                hooks.Add(Definition(catalog.GetMethod("Find",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"Wallet Editor"})));
                foreach(string name in new[]{"MultiplayerEntry","MultiplayerTile","NativeRoomName"})hooks.Add(Definition(catalog.GetField(name,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)));
                bool apply=options.ContainsKey("--apply");
                process=OpenProcess(apply?0x0038U:0x0010U,false,pid);Require(process!=IntPtr.Zero,"Cannot open the explicit target process.");
                foreach(Hook hook in hooks)
                {
                    Require(hook.Rva>=0&&hook.Expected.Length>=5&&hook.Rva<=moduleSize-hook.Expected.Length,"Hook lies outside target module.");
                    Require(hook.Size>=4096&&hook.Size<=0x40000,"Unsupported allocation size.");
                    hook.Saved=Read(process,address+hook.Rva,hook.Expected.Length);
                    for(int i=0;i<hook.Expected.Length;i++)Require(!hook.Mask[i]||hook.Saved[i]==hook.Expected[i],"Signature mismatch: "+hook.Name+"; use a fresh unhooked process.");
                    Console.WriteLine("Verified: "+hook.Name+" RVA 0x"+hook.Rva.ToString("X"));
                }
                Require(target.StartTime==started&&!target.HasExited,"Target lifetime changed.");
                if(!apply){Console.WriteLine("DRY RUN: no target memory was modified. Use --apply for explicit installation.");return 0;}
                // Wallet builder sets NativeLobbyHook.StateAddress before the room-name builder runs.
                foreach(Hook hook in hooks)
                {
                    hook.Cave=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)hook.Size,0x3000,0x40);Require(hook.Cave!=IntPtr.Zero,"Allocation failed: "+hook.Name);
                    hook.Code=hook.Build(address,hook.Cave.ToInt64());Require(hook.Code.Length<=hook.Size,"Builder exceeds allocation: "+hook.Name);
                    Write(process,hook.Cave.ToInt64(),hook.Code);Require(FlushInstructionCache(process,hook.Cave,(UIntPtr)hook.Code.Length),"Cannot flush generated code.");
                    hook.Patch=new byte[hook.Expected.Length];for(int i=0;i<hook.Patch.Length;i++)hook.Patch[i]=0x90;hook.Patch[0]=0xe9;
                    Buffer.BlockCopy(BitConverter.GetBytes(unchecked((int)(hook.Cave.ToInt64()-address-hook.Rva-5))),0,hook.Patch,1,4);
                }
                var paused=new List<PausedThread>();
                try
                {
                    Pause(target,paused);CheckPositions(paused,address,hooks);
                    Require(target.StartTime==started&&!target.HasExited,"Target lifetime changed before publication.");
                    foreach(Hook hook in hooks)Require(Equal(Read(process,address+hook.Rva,hook.Saved.Length),hook.Saved),"Hook site changed after suspension.");
                    int published=0;
                    try
                    {
                        foreach(Hook hook in hooks){published++;Branch(process,address+hook.Rva,hook.Patch);Require(Equal(Read(process,address+hook.Rva,hook.Patch.Length),hook.Patch),"Published branch readback mismatch.");}
                    }
                    catch
                    {
                        for(int i=published-1;i>=0;i--)try{Branch(process,address+hooks[i].Rva,hooks[i].Saved);}catch{Console.Error.WriteLine("Restoration failed: "+hooks[i].Name);}
                        throw;
                    }
                }
                finally{Resume(paused);}
                // Retain caves until target exit, including failed installs: never free possibly executing code.
                Console.WriteLine("INSTALLED explicit PID "+pid+". Restart that target to remove hooks. No bridge timer or gameplay commands were started.");
                Console.WriteLine("Shared state: 0x"+(hooks[0].Cave.ToInt64()+0x38000).ToString("X")+"; ABI 3784 bytes.");
                return 0;
            }
        }
        catch(Exception ex){Console.Error.WriteLine(ex.GetType().Name+": "+ex.Message);return 1;}
        finally{if(process!=IntPtr.Zero)CloseHandle(process);}
    }
    private static int SelfTest()
    {
        Require(Hash(new byte[]{0x61,0x62,0x63})=="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad","SHA256 fixture failed.");
        var parsed=Options(new[]{"--pid","123","--module","game","--trainer","trainer","--trainer-sha256","digest"});
        Require(!parsed.ContainsKey("--apply"),"Default must remain read-only.");
        var ranges=new List<Hook>{new Hook{Rva=0x100,Expected=new byte[6]}};
        Require(!InHook(0x40ff,0x4000,ranges)&&InHook(0x4100,0x4000,ranges)&&InHook(0x4105,0x4000,ranges)&&!InHook(0x4106,0x4000,ranges),"Instruction-pointer hook boundaries failed.");
        bool refused=false;try{Options(new[]{"--pid","123"});}catch(InvalidOperationException){refused=true;}Require(refused,"Missing target parameters were accepted.");
        refused=false;try{Options(new[]{"--unknown","value"});}catch(InvalidOperationException){refused=true;}Require(refused,"Unknown option was accepted.");
        string temporary=Path.Combine(Path.GetTempPath(),"a8-harness-invalid-"+Guid.NewGuid().ToString("N")+".bin");
        try{File.WriteAllBytes(temporary,new byte[]{0x4d,0x5a});refused=false;try{VerifyGame(temporary);}catch(InvalidOperationException){refused=true;}Require(refused,"Unsupported executable was accepted.");}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
        Console.WriteLine("PASS: source-only hash, explicit-option, read-only-default and unsupported-image guards. No target process opened.");return 0;
    }
}
