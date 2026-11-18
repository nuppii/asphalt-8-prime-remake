using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

// The native dispatcher owns transient per-car physics changes. These hooks
// remain installed with zero masks when disabled, so no executing cave is freed.
internal static class MutatorEngine
{
    private const int AllocationSize = 0x30000;
    private const int ImageOffset = 0x1000;
    private const int MaximumImageSize = 0x20000;
    private const int StateOffset = 0x22000;
    private const int TrampolineOffset = 0x600;
    private const uint ProcessAccess = 0x0008 | 0x0010 | 0x0020 | 0x0400;
    private const uint ThreadAccess = 0x0002 | 0x0008;
    private const uint PageExecuteReadWrite = 0x40;
    private const uint MemCommitReserve = 0x1000 | 0x2000;
    private const uint MemRelease = 0x8000;
    private static readonly object Sync = new object();
    private static readonly int[] HookRvas = { 0x5531E0, 0x55345B, 0x5536B5 };
    private static readonly int[] WrapperOffsets = { 0, 0x200, 0x400 };
    private static readonly byte[][] Originals =
    {
        new byte[] { 0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8 },
        new byte[] { 0x8B, 0x87, 0x14, 0xFF, 0xFF, 0xFF },
        new byte[] { 0x8B, 0x4F, 0x34, 0x8B, 0x01 }
    };
    private static int processId;
    private static long processStartTicks;
    private static long allocationAddress;
    private static byte[][] installedPatches;

    internal static long StateAddress { get { lock (Sync) return allocationAddress==0?0:allocationAddress+StateOffset; } }
    internal static int ProcessId { get { lock (Sync) return processId; } }

    [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint type);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] data, int size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] data, int size, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool VirtualProtectEx(IntPtr process, IntPtr address, UIntPtr size, uint protect, out uint original);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr OpenThread(uint access, bool inherit, uint threadId);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern uint SuspendThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetThreadContext(IntPtr thread, IntPtr context);

    internal static bool EnsureInstalled(out string error)
    {
        lock (Sync)
        {
            error="";
            Process game=null;
            IntPtr process=IntPtr.Zero;
            IntPtr cave=IntPtr.Zero;
            long targetModule=0;
            bool committed=false;
            bool rollbackSucceeded=true;
            var touched=new List<int>();
            var paused=new List<PausedThread>();
            try
            {
                if(IntPtr.Size!=4)throw new InvalidOperationException("The mutator trainer must be compiled for x86.");
                game=GetGame();
                if (game==null) { ForgetProcess(); error="Game not found."; return false; }
                long started=game.StartTime.ToUniversalTime().Ticks;
                long module=Address(game.MainModule.BaseAddress);
                targetModule=module;
                process=OpenProcess(ProcessAccess,false,game.Id);
                if (process==IntPtr.Zero) throw Failure("Cannot open the game process");
                if (processId==game.Id && processStartTicks==started && allocationAddress!=0)
                {
                    ValidateHooks(process,module,installedPatches,"An installed mutator hook has changed. Restart the game.");
                    return true;
                }
                ForgetProcess();
                ValidateHooks(process,module,Originals,"Unexpected mutator signature");
                cave=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)AllocationSize,MemCommitReserve,PageExecuteReadWrite);
                if (cave==IntPtr.Zero) throw Failure("Cannot allocate the native mutator engine");
                byte[] payload=BuildPayload(module,Address(cave));
                Write(process,Address(cave),payload);
                if (!FlushInstructionCache(process,cave,(UIntPtr)AllocationSize)) throw Failure("Cannot synchronize the mutator code");
                byte[][] patches=new byte[HookRvas.Length][];
                for (int i=0;i<patches.Length;i++)
                    patches[i]=Jump(module+HookRvas[i],Address(cave)+WrapperOffsets[i],Originals[i].Length);

                PauseGameThreads(game,paused);
                ValidateThreadPositions(paused,module);
                ValidateHooks(process,module,Originals,"Mutator signature changed during installation");
                for (int i=0;i<patches.Length;i++)
                {
                    // Include a failed or short write in rollback as well.
                    touched.Add(i);
                    WriteProtected(process,module+HookRvas[i],patches[i]);
                }
                processId=game.Id;
                processStartTicks=started;
                allocationAddress=Address(cave);
                installedPatches=patches;
                committed=true;
            }
            catch (Exception ex)
            {
                error=Describe(ex);
                for (int i=touched.Count-1;i>=0;i--)
                {
                    int hook=touched[i];
                    try { WriteProtected(process,targetModule+HookRvas[hook],Originals[hook]); }
                    catch (Exception rollbackError)
                    {
                        rollbackSucceeded=false;
                        error+=" • Hook rollback failed: "+Describe(rollbackError);
                    }
                }
                if (!rollbackSucceeded) error+=" • Restart the game before retrying; the allocation was retained for safety.";
            }
            finally
            {
                // Rollback happens before resuming. An allocation that might
                // still be referenced by a hook must never be released.
                if (!committed && rollbackSucceeded && cave!=IntPtr.Zero && process!=IntPtr.Zero)
                    VirtualFreeEx(process,cave,UIntPtr.Zero,MemRelease);
                string resumeError=ResumeGameThreads(paused);
                if (resumeError.Length!=0) error+=(error.Length==0?"":" • ")+resumeError;
                if (process!=IntPtr.Zero) CloseHandle(process);
                if (game!=null) game.Dispose();
            }
            return committed && error.Length==0;
        }
    }

    internal static bool Configure(int target, int parameter, bool enabled, float multiplier, out string error)
    {
        lock (Sync)
        {
            error="";
            if (target<0 || target>1 || parameter<0 || parameter>6 || float.IsNaN(multiplier) ||
                float.IsInfinity(multiplier) || multiplier<0f || multiplier>100f)
            { error="Invalid mutator target, parameter or multiplier (0.00–100.00)."; return false; }
            if (!EnsureInstalled(out error)) return false;
            IntPtr process=OpenProcess(ProcessAccess,false,processId);
            if (process==IntPtr.Zero) { error="Cannot access the mutator state."; return false; }
            try
            {
                long state=allocationAddress+StateOffset;
                long maskAddress=state+8+target*4;
                uint mask=BitConverter.ToUInt32(Read(process,maskAddress,4),0);
                uint bit=1u<<parameter;
                long factorAddress=state+16+(target*7+parameter)*4;
                if (enabled)
                {
                    Write(process,factorAddress,BitConverter.GetBytes(multiplier));
                    Write(process,maskAddress,BitConverter.GetBytes(mask|bit));
                }
                else
                {
                    Write(process,maskAddress,BitConverter.GetBytes(mask&~bit));
                    Write(process,factorAddress,BitConverter.GetBytes(multiplier));
                }
                return true;
            }
            catch (Exception ex) { error=Describe(ex); return false; }
            finally { CloseHandle(process); }
        }
    }

    internal static bool ResetTarget(int target, out string error)
    {
        lock (Sync)
        {
            error="";
            if (target<0 || target>1) { error="Invalid mutator target."; return false; }
            Process game=null;
            try
            {
                game=GetGame();
                if (game==null) { ForgetProcess(); return true; }
            }
            catch (Exception ex) { error=Describe(ex); return false; }
            finally { if(game!=null)game.Dispose(); }
            if (!EnsureInstalled(out error)) return false;
            IntPtr process=OpenProcess(ProcessAccess,false,processId);
            if(process==IntPtr.Zero){error="Cannot access the mutator state.";return false;}
            try
            {
                long state=allocationAddress+StateOffset;
                Write(process,state+8+target*4,new byte[4]);
                byte[] factors=new byte[28];
                for(int i=0;i<7;i++) Buffer.BlockCopy(BitConverter.GetBytes(1f),0,factors,i*4,4);
                Write(process,state+16+target*28,factors);
                return true;
            }
            catch(Exception ex){error=Describe(ex);return false;}
            finally{CloseHandle(process);}
        }
    }

    internal static bool ReadStateBytes(int offset, int length, out byte[] bytes, out string error)
    {
        lock(Sync)
        {
            bytes=null;error="";
            if(offset<0 || length<0 || (long)offset+length>AllocationSize-StateOffset)
            {error="Invalid mutator diagnostic range.";return false;}
            if(allocationAddress==0){error="Mutators are not installed.";return false;}
            IntPtr process=OpenProcess(ProcessAccess,false,processId);
            if(process==IntPtr.Zero){error="Cannot access the mutator state.";return false;}
            try{bytes=Read(process,allocationAddress+StateOffset+offset,length);return true;}
            catch(Exception ex){error=Describe(ex);return false;}
            finally{CloseHandle(process);}
        }
    }

    internal static bool ReadDiagnostics(out byte[] bytes, out string error)
    {
        return ReadStateBytes(0,272,out bytes,out error);
    }

    private static Process GetGame()
    {
        Process[] games=Process.GetProcessesByName("Asphalt8");
        if(games.Length==0)return null;
        if(games.Length==1)return games[0];
        foreach(Process game in games)game.Dispose();
        throw new InvalidOperationException("Open exactly one Asphalt 8 game instance.");
    }

    private static void ForgetProcess()
    {
        processId=0;processStartTicks=0;allocationAddress=0;installedPatches=null;
    }

    private static byte[] BuildPayload(long module, long cave)
    {
        byte[] code=new byte[AllocationSize];
        int entry=LoadNativeImage(code,cave+ImageOffset);
        long state=cave+StateOffset;
        Store(code,StateOffset,module);
        Store(code,StateOffset+4,cave+TrampolineOffset);
        for(int i=0;i<14;i++)Buffer.BlockCopy(BitConverter.GetBytes(1f),0,code,StateOffset+16+i*4,4);
        byte[] trampoline=new byte[Originals[0].Length+5];
        Buffer.BlockCopy(Originals[0],0,trampoline,0,Originals[0].Length);
        Buffer.BlockCopy(Jump(cave+TrampolineOffset+Originals[0].Length,module+HookRvas[0]+Originals[0].Length,5),0,
            trampoline,Originals[0].Length,5);
        Buffer.BlockCopy(trampoline,0,code,TrampolineOffset,trampoline.Length);

        var full=new List<byte>();
        full.AddRange(new byte[]{0x6A,0x00}); // push stage zero
        Push(full,state);
        full.Add(0x51); // push the original ECX/PhysicsCar
        Relative(full,cave,0xE8,cave+entry);
        full.AddRange(new byte[]{0x83,0xC4,0x0C,0xC3});
        full.CopyTo(code,WrapperOffsets[0]);
        for(int stage=1;stage<=2;stage++)
        {
            long wrapper=cave+WrapperOffsets[stage];
            var body=new List<byte>();
            body.AddRange(new byte[]{0x9C,0x60,0x8B,0xDC,0x81,0xEC,0x10,0x02,0x00,0x00,0x83,0xE4,0xF0,
                0x0F,0xAE,0x04,0x24}); // flags, GPRs and aligned FXSAVE
            body.AddRange(new byte[]{0x6A,(byte)stage});
            Push(body,state);
            body.Add(0x57); // EDI is the PhysicsCar for engine and drift stages
            Relative(body,wrapper,0xE8,cave+entry);
            body.AddRange(new byte[]{0x83,0xC4,0x0C,0x0F,0xAE,0x0C,0x24,0x8B,0xE3,0x61,0x9D});
            body.AddRange(Originals[stage]);
            Relative(body,wrapper,0xE9,module+HookRvas[stage]+Originals[stage].Length);
            if(body.Count>0x200)throw new InvalidDataException("Mutator wrapper exceeds its reserved space.");
            body.CopyTo(code,WrapperOffsets[stage]);
        }
        return code;
    }

    private static int LoadNativeImage(byte[] payload, long address)
    {
        byte[] file;
        using(Stream resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("MutatorPhysics.x86"))
        {
            if(resource==null)throw new InvalidDataException("Native mutator resource is missing.");
            using(var stream=new MemoryStream()){resource.CopyTo(stream);file=stream.ToArray();}
        }
        Need(file,0,64);
        if(file[0]!=0x4D || file[1]!=0x5A)throw new InvalidDataException("Invalid native mutator DOS header.");
        int pe=BitConverter.ToInt32(file,0x3C);
        Need(file,pe,24);
        if(BitConverter.ToUInt32(file,pe)!=0x4550 || BitConverter.ToUInt16(file,pe+4)!=0x14C)
            throw new InvalidDataException("Native mutators require an x86 PE image.");
        int header=pe+24;
        int optionalSize=BitConverter.ToUInt16(file,pe+20);
        Need(file,header,optionalSize);
        if(optionalSize<144 || BitConverter.ToUInt16(file,header)!=0x10B)
            throw new InvalidDataException("Invalid native mutator PE32 optional header.");
        int imageSize=BitConverter.ToInt32(file,header+56);
        int headerSize=BitConverter.ToInt32(file,header+60);
        int entry=BitConverter.ToInt32(file,header+16);
        uint preferred=BitConverter.ToUInt32(file,header+28);
        if(imageSize<=0 || imageSize>MaximumImageSize || entry<0 || entry>=imageSize || headerSize<0 || headerSize>imageSize)
            throw new InvalidDataException("Native mutator image exceeds its reserved space.");
        Need(file,0,headerSize);
        Buffer.BlockCopy(file,0,payload,ImageOffset,headerSize);
        int sections=BitConverter.ToUInt16(file,pe+6);
        int table=header+optionalSize;
        Need(file,table,sections*40);
        bool executableEntry=false;
        for(int i=0;i<sections;i++)
        {
            int section=table+i*40;
            int virtualSize=BitConverter.ToInt32(file,section+8);
            int target=BitConverter.ToInt32(file,section+12);
            int length=BitConverter.ToInt32(file,section+16);
            int source=BitConverter.ToInt32(file,section+20);
            if(target<0 || virtualSize<0 || length<0 || (long)target+Math.Max(length,virtualSize)>imageSize)
                throw new InvalidDataException("Invalid native mutator section range.");
            Need(file,source,length);
            Buffer.BlockCopy(file,source,payload,ImageOffset+target,length);
            if(entry>=target && (long)entry<target+Math.Max(length,virtualSize) &&
                (BitConverter.ToUInt32(file,section+36)&0x20000000)!=0)executableEntry=true;
        }
        if(!executableEntry)throw new InvalidDataException("Native mutator entry is not executable.");
        // The freestanding native image must not require a Windows loader.
        if(BitConverter.ToInt32(file,header+96+8)!=0 || BitConverter.ToInt32(file,header+100+8)!=0)
            throw new InvalidDataException("Native mutator imports are unsupported.");
        int relocations=BitConverter.ToInt32(file,header+96+5*8);
        int relocationSize=BitConverter.ToInt32(file,header+100+5*8);
        long delta=address-preferred;
        if(relocations<0 || relocationSize<0 || (long)relocations+relocationSize>imageSize ||
            (delta!=0 && relocationSize==0))throw new InvalidDataException("Native mutator relocations are missing or invalid.");
        int finish=ImageOffset+relocations+relocationSize;
        for(int block=ImageOffset+relocations;block<finish;)
        {
            if(finish-block<8)throw new InvalidDataException("Truncated native mutator relocation block.");
            int page=BitConverter.ToInt32(payload,block);
            int size=BitConverter.ToInt32(payload,block+4);
            if(page<0 || size<8 || (size&1)!=0 || size>finish-block)
                throw new InvalidDataException("Invalid native mutator relocation block.");
            for(int offset=block+8;offset<block+size;offset+=2)
            {
                int encoded=BitConverter.ToUInt16(payload,offset);
                int type=encoded>>12;
                if(type==0)continue;
                if(type!=3)throw new InvalidDataException("Unsupported native mutator relocation type.");
                long relative=(long)page+(encoded&0xFFF);
                if(relative<0 || relative+4>imageSize)throw new InvalidDataException("Native mutator relocation is out of range.");
                int target=ImageOffset+(int)relative;
                Store(payload,target,BitConverter.ToUInt32(payload,target)+delta);
            }
            block+=size;
        }
        return ImageOffset+entry;
    }

    private static void Need(byte[] file, int offset, int length)
    {
        if(offset<0 || length<0 || (long)offset+length>file.Length)
            throw new InvalidDataException("Truncated native mutator image.");
    }

    private static void Push(List<byte> code, long value){code.Add(0x68);code.AddRange(BitConverter.GetBytes(unchecked((int)value)));}
    private static void Relative(List<byte> code,long start,byte opcode,long target)
    {code.Add(opcode);code.AddRange(BitConverter.GetBytes(unchecked((int)(target-(start+code.Count+4)))));}
    private static void Store(byte[] data,int offset,long value)
    {Buffer.BlockCopy(BitConverter.GetBytes(unchecked((int)value)),0,data,offset,4);}
    private static byte[] Jump(long from,long to,int length)
    {
        byte[] code=new byte[length];for(int i=0;i<length;i++)code[i]=0x90;
        code[0]=0xE9;Buffer.BlockCopy(BitConverter.GetBytes(unchecked((int)(to-from-5))),0,code,1,4);return code;
    }

    private static void ValidateHooks(IntPtr process,long module,byte[][] expected,string message)
    {
        for(int i=0;i<HookRvas.Length;i++)
        {
            byte[] observed=Read(process,module+HookRvas[i],expected[i].Length);
            for(int j=0;j<observed.Length;j++)
                if(observed[j]!=expected[i][j])throw new InvalidOperationException(message+" (+"+HookRvas[i].ToString("X")+").");
        }
    }

    private static byte[] Read(IntPtr process,long address,int length)
    {
        byte[] data=new byte[length];IntPtr count;
        if(!ReadProcessMemory(process,Pointer(address),data,length,out count) || count.ToInt64()!=length)
            throw Failure("Cannot read mutator memory");
        return data;
    }

    private static void Write(IntPtr process,long address,byte[] data)
    {
        IntPtr count;
        if(!WriteProcessMemory(process,Pointer(address),data,data.Length,out count) || count.ToInt64()!=data.Length)
            throw Failure("Cannot write mutator memory");
    }

    private static void WriteProtected(IntPtr process,long address,byte[] data)
    {
        uint original;
        IntPtr pointer=Pointer(address);
        if(!VirtualProtectEx(process,pointer,(UIntPtr)data.Length,PageExecuteReadWrite,out original))
            throw Failure("Cannot protect mutator hook memory");
        try
        {
            Write(process,address,data);
            byte[] observed=Read(process,address,data.Length);
            for(int i=0;i<data.Length;i++)if(observed[i]!=data[i])throw new InvalidOperationException("Mutator hook readback differs from the written bytes.");
            if(!FlushInstructionCache(process,pointer,(UIntPtr)data.Length))throw Failure("Cannot synchronize a mutator hook");
        }
        finally
        {
            uint ignored;
            if(!VirtualProtectEx(process,pointer,(UIntPtr)data.Length,original,out ignored))
                throw Failure("Cannot restore mutator hook memory protection");
        }
    }

    private sealed class PausedThread
    {
        internal readonly int Id; internal readonly IntPtr Handle;
        internal PausedThread(int id,IntPtr handle){Id=id;Handle=handle;}
    }

    private static void PauseGameThreads(Process game,List<PausedThread> paused)
    {
        var seen=new HashSet<int>();
        // Repeat the snapshot until no just-created game thread remains active.
        for(int pass=0;pass<4;pass++)
        {
            bool added=false;game.Refresh();
            foreach(ProcessThread thread in game.Threads)
            {
                using(thread)
                {
                    if(seen.Contains(thread.Id))continue;
                    IntPtr handle=OpenThread(ThreadAccess,false,(uint)thread.Id);
                    if(handle==IntPtr.Zero)throw Failure("Cannot open game thread "+thread.Id);
                    if(SuspendThread(handle)==uint.MaxValue)
                    {
                        int failure=Marshal.GetLastWin32Error();CloseHandle(handle);
                        throw new InvalidOperationException("Cannot suspend game thread "+thread.Id+" (Win32 "+failure+").");
                    }
                    paused.Add(new PausedThread(thread.Id,handle));seen.Add(thread.Id);added=true;
                }
            }
            if(!added)
            {
                if(paused.Count==0)throw new InvalidOperationException("The game has no accessible threads.");
                return;
            }
        }
        throw new InvalidOperationException("Game threads are still changing. Try enabling mutators again.");
    }

    private static void ValidateThreadPositions(List<PausedThread> paused,long module)
    {
        IntPtr storage=Marshal.AllocHGlobal(716+15);
        IntPtr context=new IntPtr(unchecked((int)((storage.ToInt64()+15)&~15L)));
        try
        {
            byte[] empty=new byte[716];
            foreach(PausedThread thread in paused)
            {
                Marshal.Copy(empty,0,context,empty.Length);
                Marshal.WriteInt32(context,0,0x10001); // x86 CONTEXT_CONTROL
                if(!GetThreadContext(thread.Handle,context))throw Failure("Cannot inspect game thread "+thread.Id);
                long instruction=unchecked((uint)Marshal.ReadInt32(context,184));
                for(int i=0;i<HookRvas.Length;i++)
                    if(instruction>=module+HookRvas[i] && instruction<module+HookRvas[i]+Originals[i].Length)
                        throw new InvalidOperationException("A game thread is inside a mutator hook. Try enabling mutators again.");
            }
        }
        finally{Marshal.FreeHGlobal(storage);}
    }

    private static string ResumeGameThreads(List<PausedThread> paused)
    {
        string error="";
        for(int i=paused.Count-1;i>=0;i--)
        {
            PausedThread thread=paused[i];
            if(ResumeThread(thread.Handle)==uint.MaxValue)
                error+=(error.Length==0?"":"; ")+"Cannot resume game thread "+thread.Id+" (Win32 "+Marshal.GetLastWin32Error()+")";
            CloseHandle(thread.Handle);
        }
        return error;
    }

    private static Exception Failure(string message)
    {return new InvalidOperationException(message+" (Win32 "+Marshal.GetLastWin32Error()+").");}
    private static long Address(IntPtr pointer){return unchecked((uint)pointer.ToInt32());}
    private static IntPtr Pointer(long address){return new IntPtr(unchecked((int)address));}
    private static string Describe(Exception error)
    {
        if(error is Win32Exception)return "Windows could not access the game (Win32 "+((Win32Exception)error).NativeErrorCode+").";
        return error.Message;
    }
}
