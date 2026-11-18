using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

// Isolated x86 fixture: never opens another process or starts the trainer/game.
// Only generated lap code and early-return native UI paths run in owned memory.
internal static class NativePayloadCheck
{
    const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool VirtualFree(IntPtr address, UIntPtr size, uint type);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Tick(uint module, IntPtr screen, IntPtr state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Run();
    static int checks;
    static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); ++checks; }
    static uint U(IntPtr p) { return unchecked((uint)p.ToInt32()); }
    static IntPtr Plus(IntPtr p,int n) { return new IntPtr(unchecked(p.ToInt32()+n)); }
    static byte[] Snapshot(IntPtr p,int size) { byte[] b=new byte[size]; Marshal.Copy(p,b,0,size); return b; }
    static bool Equal(byte[] a,byte[] b) { if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;++i)if(a[i]!=b[i])return false;return true; }
    static void Clear(IntPtr p,int n) { for(int i=0;i<n;++i)Marshal.WriteByte(p,i,0); }
    static IntPtr Allocate(int n) { IntPtr p=VirtualAlloc(IntPtr.Zero,new UIntPtr((uint)n),0x3000,0x40);if(p==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();return p; }
    static int I(byte[] b,int n) { return BitConverter.ToInt32(b,n); }
    static uint W(byte[] b,int n) { return BitConverter.ToUInt32(b,n); }
    static ushort S(byte[] b,int n) { return BitConverter.ToUInt16(b,n); }
    static Func<long,long,byte[]> Builder(object hack) { return (Func<long,long,byte[]>)hack.GetType().GetField("BuildCode",Any).GetValue(hack); }
    static byte[] Resource(Assembly a) { using(Stream s=a.GetManifestResourceStream("NativeLobbyUi.x86")){Check(s!=null,"embedded native UI resource");using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}} }
    static byte[] NativeMapped(byte[] file,out int entry,out int reloc,out int relocBytes) {
        int pe=I(file,0x3c),h=pe+24;Check(W(file,pe)==0x4550&&S(file,pe+4)==0x14c&&S(file,h)==0x10b,"x86 PE headers");
        int size=I(file,h+56);Check(size>0&&size<0x36000,"native image fits allocation");
        Check(I(file,h+104)==0&&I(file,h+108)==0,"native image has no import directory");
        byte[] result=new byte[size];int sections=S(file,pe+6),table=h+S(file,pe+20);entry=I(file,h+16);bool executableEntry=false;
        for(int j=0;j<sections;++j){int q=table+j*40,rva=I(file,q+12),n=I(file,q+16),raw=I(file,q+20),virtualSize=I(file,q+8);
            Check(rva>=0&&n>=0&&rva<=size-n&&raw>=0&&raw<=file.Length-n,"section bounds "+j);
            if(n!=0)Buffer.BlockCopy(file,raw,result,rva,n);
            if(entry>=rva&&entry<rva+Math.Max(n,virtualSize)&&(W(file,q+36)&0x20000000)!=0)executableEntry=true;
        }
        Check(executableEntry,"native entry lies in executable section");reloc=I(file,h+136);relocBytes=I(file,h+140);return result;
    }
    static void Wallet(Assembly a,Type catalog,Type hook,IntPtr cave) {
        object wallet=catalog.GetMethod("Find",Any).Invoke(null,new object[]{"Wallet Editor"});Check(wallet!=null,"Wallet Editor catalog entry");
        Check((int)wallet.GetType().GetField("AllocationSize",Any).GetValue(wallet)==0x40000,"Wallet allocation is 0x40000");
        byte[] code=Builder(wallet)(0x10000000,U(cave));Check(code.Length==0x40000,"composed payload length");Check(code[0]==0x9c&&code[1]==0x60,"initial PUSHFD/PUSHAD");
        Check((long)hook.GetField("StateAddress",Any).GetValue(null)==(long)U(cave)+0x38000,"shared-state address assigned");
        int entry,reloc,remaining;byte[] file=Resource(a),image=NativeMapped(file,out entry,out reloc,out remaining);
        Check(code[30]==0xe8&&unchecked((uint)(U(cave)+35+I(code,31)))==U(cave)+0x2000+(uint)entry,"composed native call target");
        int h=I(file,0x3c)+24;uint delta=unchecked(U(cave)+0x2000-W(file,h+28));int count=0;
        while(remaining>0){Check(remaining>=8&&reloc>=0&&reloc<=image.Length-8,"relocation block bounds");int page=I(image,reloc),block=I(image,reloc+4);Check(block>=8&&block<=remaining&&(block&1)==0&&reloc<=image.Length-block,"relocation block size");
            for(int off=8;off<block;off+=2){int item=S(image,reloc+off),kind=item>>12;if(kind==0)continue;int target=page+(item&4095);Check(kind==3&&target>=0&&target<=image.Length-4,"HIGHLOW relocation target");Check(W(code,0x2000+target)==unchecked(W(image,target)+delta),"HIGHLOW relocated value");++count;}
            reloc+=block;remaining-=block;
        }
        Check(count>0,"native relocation table exercised");
        Marshal.Copy(code,0,cave,code.Length);
        Tick tick=(Tick)Marshal.GetDelegateForFunctionPointer(Plus(cave,0x2000+entry),typeof(Tick));IntPtr screen=Plus(cave,0x37000),state=Plus(cave,0x38000);Clear(screen,0x200);Clear(state,3784);
        // Calls below cannot reach game APIs: nulls, foreign vtable, invalid
        // contract, or a null layout root all terminate before API lookup.
        byte[] before=Snapshot(state,3784);tick(0,screen,state);tick(0x10000000,IntPtr.Zero,state);tick(0x10000000,screen,IntPtr.Zero);tick(0x10000000,screen,state);Check(Equal(before,Snapshot(state,3784)),"native null and foreign-screen guards");
        Marshal.WriteInt32(screen,unchecked((int)(0x10000000+0x1d45bac)));Marshal.WriteInt32(state,123);before=Snapshot(state,3784);tick(0x10000000,screen,state);Check(Equal(before,Snapshot(state,3784)),"native invalid magic guard");
        Marshal.WriteInt32(state,unchecked((int)0x3849554e));Marshal.WriteInt32(state,4,2);Marshal.WriteInt32(state,8,3784);before=Snapshot(state,3784);tick(0x10000000,screen,state);Check(Equal(before,Snapshot(state,3784)),"native invalid version guard");
        Marshal.WriteInt32(state,4,1);Marshal.WriteInt32(state,8,1);before=Snapshot(state,3784);tick(0x10000000,screen,state);Check(Equal(before,Snapshot(state,3784)),"native short shared-state guard");
        Clear(state,3784);tick(0x10000000,screen,state);Check(Marshal.ReadInt32(state)==unchecked((int)0x3849554e)&&Marshal.ReadInt32(state,4)==1&&Marshal.ReadInt32(state,8)==3784,"native default contract initialized with null root");
        Console.WriteLine("PASS composed wallet: allocation, preservation prefix, native call, "+count+" relocations and early-return UI guards.");
    }
    static void Imm(List<byte> code,uint v){code.AddRange(BitConverter.GetBytes(v));}
    static void Mov(List<byte> code,byte opcode,uint v){code.Add(opcode);Imm(code,v);}
    static void Store(List<byte> code,byte modrm,uint address){code.Add(0x89);code.Add(modrm);Imm(code,address);}
    static void LapCase(Func<long,long,byte[]> build,IntPtr mem,int laps,int mode,int privateRoom,int initial,int expected) {
        IntPtr state=Plus(mem,0x2000),server=Plus(mem,0x3000),scratch=Plus(mem,0x4000),observed=Plus(mem,0x5000);Clear(state,3784);Clear(server,0x200);Clear(scratch,0x80);Clear(observed,32);
        Marshal.WriteInt32(state,40,laps);Marshal.WriteInt32(server,0x108,privateRoom);Marshal.WriteInt32(server,0x120,mode);Marshal.WriteInt32(server,0x130,initial);Marshal.WriteInt32(scratch,0x3c,16);
        byte[] body=build(0x10000000,U(mem));Check(body.Length>=5&&body[body.Length-5]==0xe9,"lap tail is rel32 jump");
        // Only the final jump is redirected, to RET in the same owned block.
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked((int)(U(mem)+0x800-(U(mem)+(uint)body.Length)))),0,body,body.Length-4,4);
        Marshal.Copy(body,0,mem,body.Length);Marshal.WriteByte(Plus(mem,0x800),0xc3);
        var w=new List<byte>();w.Add(0x9c);w.Add(0x60);Mov(w,0xb8,0x11223344);Mov(w,0xb9,0x22334455);Mov(w,0xba,0x33445566);Mov(w,0xbb,0x44556677);Mov(w,0xbd,0x55667788);Mov(w,0xbe,U(scratch));Mov(w,0xbf,U(server));
        w.Add(0xe8);Imm(w,unchecked(U(mem)-(U(mem)+0x1000+(uint)w.Count+4)));
        w.Add(0xa3);Imm(w,U(observed));Store(w,0x0d,U(observed)+4);Store(w,0x15,U(observed)+8);Store(w,0x1d,U(observed)+12);Store(w,0x2d,U(observed)+16);Store(w,0x35,U(observed)+20);Store(w,0x3d,U(observed)+24);w.Add(0x9c);w.Add(0x8f);w.Add(0x05);Imm(w,U(observed)+28);w.Add(0x61);w.Add(0x9d);w.Add(0xc3);
        byte[] wrapper=w.ToArray();Marshal.Copy(wrapper,0,Plus(mem,0x1000),wrapper.Length);((Run)Marshal.GetDelegateForFunctionPointer(Plus(mem,0x1000),typeof(Run)))();
        Check(Marshal.ReadInt32(server,0x130)==expected,"lap result requested="+laps+" mode="+mode+" private="+privateRoom);
        int[] regs={0x11223344,0x22334455,0x33445566,0x44556677,0x55667788,unchecked((int)(U(scratch)+0x28)),server.ToInt32()};for(int i=0;i<regs.Length;++i)Check(Marshal.ReadInt32(observed,i*4)==regs[i],"lap register effect "+i);
        Check((Marshal.ReadInt32(observed,28)&0x40)!=0,"original displaced CMP sets ZF");
    }
    public static int Main(string[] args) {
        IntPtr wallet=IntPtr.Zero,lap=IntPtr.Zero;FieldInfo stateField=null;long saved=0;
        try { Check(IntPtr.Size==4,"fixture must run x86");Check(args.Length==1&&File.Exists(args[0]),"explicit trainer assembly path required");Assembly a=Assembly.LoadFrom(Path.GetFullPath(args[0]));Type catalog=a.GetType("InjectionCatalog",true),hook=a.GetType("NativeLobbyHook",true);stateField=hook.GetField("StateAddress",Any);saved=(long)stateField.GetValue(null);
            wallet=Allocate(0x40000);Wallet(a,catalog,hook,wallet);lap=Allocate(0x6000);stateField.SetValue(null,(long)U(lap)+0x2000);object hack=catalog.GetField("NativeClassicLaps",Any).GetValue(null);Func<long,long,byte[]> build=Builder(hack);
            LapCase(build,lap,0,0,1,2,2);LapCase(build,lap,1,0,1,2,1);LapCase(build,lap,60,0,1,2,60);LapCase(build,lap,61,0,1,2,2);LapCase(build,lap,-1,0,1,2,2);LapCase(build,lap,3,1,1,2,2);LapCase(build,lap,3,0,0,2,2);LapCase(build,lap,0,0,1,7,7);
            Console.WriteLine("PASS isolated native lap execution: default, 1, 60, invalid range, mode/private gates, original register/tail effects.");Console.WriteLine("PASS "+checks+" assertions. This does not validate the game UI, network peers, or races.");return 0;
        } catch(Exception e){Console.Error.WriteLine("FAIL "+e);return 1;}
        finally{if(stateField!=null)stateField.SetValue(null,saved);if(lap!=IntPtr.Zero)VirtualFree(lap,UIntPtr.Zero,0x8000);if(wallet!=IntPtr.Zero)VirtualFree(wallet,UIntPtr.Zero,0x8000);}
    }
}
