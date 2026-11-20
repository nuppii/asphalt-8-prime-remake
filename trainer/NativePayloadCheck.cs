using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

// Isolated x86 fixture: never opens another process or starts the trainer/game.
// Generated lap/direct-discovery code and native UI guards run in owned memory.
// Direct-discovery engine callbacks are explicit fixture stubs, not game peers.
internal static class NativePayloadCheck
{
    const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool VirtualFree(IntPtr address, UIntPtr size, uint type);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Tick(uint module, IntPtr screen, IntPtr state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Run();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint BeginDirect(uint module,IntPtr menu,IntPtr state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint ParseIPv4(IntPtr text,IntPtr destination);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint SoloBotRace(uint module);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint RosterPage(uint requested,uint humans,ref uint page);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint AssignRoster(IntPtr roster,IntPtr slots,IntPtr sources);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint Admission(uint module,IntPtr state,IntPtr lobby,IntPtr connection,IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void RememberRoster(IntPtr roster);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr RaceText(uint module,IntPtr state);
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
    static int Export(Type hook,string name){return (int)hook.GetMethod("ExportRva",Any).Invoke(null,new object[]{name});}
    static void Put(IntPtr where,params byte[] bytes){Marshal.Copy(bytes,0,where,bytes.Length);}
    static void Query(IntPtr state,string value){Clear(Plus(state,308),64);byte[] text=System.Text.Encoding.ASCII.GetBytes(value);Marshal.Copy(text,0,Plus(state,308),text.Length);}
    static void DirectCase(Type catalog,Type hook,IntPtr wallet)
    {
        IntPtr module=Allocate(0x1e60000),branch=Allocate(0x2000);
        try{
            IntPtr state=Plus(wallet,0x38000),screen=Plus(module,0x1000),finder=Plus(module,0x2000),discoverer=Plus(module,0x3000),observed=Plus(module,0x4000),menu=Plus(module,0x7000),control=Plus(module,0x8100);
            Clear(state,3784);Marshal.WriteInt32(state,unchecked((int)0x3849554e));Marshal.WriteInt32(state,4,1);Marshal.WriteInt32(state,8,3784);
            Marshal.WriteInt32(screen,unchecked((int)(U(module)+0x1d4db14)));Marshal.WriteInt32(screen,0x264,finder.ToInt32());Marshal.WriteInt32(Plus(module,0x1e5147c),screen.ToInt32());
            Marshal.WriteInt32(finder,4,discoverer.ToInt32());Marshal.WriteInt32(discoverer,unchecked((int)(U(module)+0x1d69be0)));
            Marshal.WriteInt32(Plus(module,0x1d69be0+0x18),Plus(module,0x5000).ToInt32());Marshal.WriteInt32(Plus(module,0x1d69be0+0x1c),Plus(module,0x6000).ToInt32());
            var directStub=new List<byte>(new byte[]{0x8b,0x44,0x24,0x04,0x8b,0x50,0x04,0x89,0x15});Imm(directStub,U(observed));directStub.AddRange(new byte[]{0x0f,0xb7,0x50,0x02,0x89,0x15});Imm(directStub,U(observed)+4);directStub.AddRange(new byte[]{0xff,0x05});Imm(directStub,U(observed)+8);directStub.AddRange(new byte[]{0xc2,0x04,0x00});Put(Plus(module,0x5000),directStub.ToArray());
            var fallback=new List<byte>(new byte[]{0xff,0x05});Imm(fallback,U(observed)+12);fallback.Add(0xc3);Put(Plus(module,0x6000),fallback.ToArray());
            Marshal.WriteInt32(menu,unchecked((int)(U(module)+0x1d45bac)));Marshal.WriteInt32(menu,0x26c,Plus(module,0x8000).ToInt32());Marshal.WriteInt32(menu,0x270,control.ToInt32());Marshal.WriteInt32(control,4,1);
            Put(Plus(module,0x9c76e0),0x8b,0x44,0x24,0x08,0xff,0x48,0x04,0xc2,0x08,0x00);
            var dispatch=(Tick)Marshal.GetDelegateForFunctionPointer(Plus(wallet,0x2000+Export(hook,"NativeLanDispatch")),typeof(Tick));
            var begin=(BeginDirect)Marshal.GetDelegateForFunctionPointer(Plus(wallet,0x2000+Export(hook,"NativeLanBeginDirect")),typeof(BeginDirect));
            var parse=(ParseIPv4)Marshal.GetDelegateForFunctionPointer(Plus(wallet,0x2000+Export(hook,"NativeLanIPv4")),typeof(ParseIPv4));
            IntPtr text=Plus(module,0x9000),destination=Plus(module,0x9100);
            foreach(string invalid in new[]{"","1.2.3","256.2.3.4","0.2.3.4","224.2.3.4","1.2.3.4:6529","host.example","1.2.3.4x","1..3.4","-1.2.3.4"}){
                Clear(text,128);byte[] ascii=System.Text.Encoding.ASCII.GetBytes(invalid);Put(text,ascii);Check(parse(text,destination)==0,"IPv4 rejected "+invalid);
            }
            Put(text,System.Text.Encoding.ASCII.GetBytes("203.0.113.7\0"));Check(parse(text,destination)==1,"IPv4 accepted");Check(Marshal.ReadInt16(destination)==2&&Marshal.ReadByte(destination,2)==0x19&&Marshal.ReadByte(destination,3)==0x81&&Marshal.ReadByte(destination,4)==203&&Marshal.ReadByte(destination,7)==7,"sockaddr network encoding");
            dispatch(U(module),finder,state);Check(Marshal.ReadInt32(observed,12)==1,"inactive discovery uses original callback");
            Query(state,"203.0.113.7");Check(begin(U(module),menu,state)==1,"explicit address begins native browser");Check(Marshal.ReadInt32(control,4)==1,"original click consumes retained control");
            dispatch(U(module),finder,state);Check(Marshal.ReadInt32(observed,8)==1&&Marshal.ReadInt32(observed)==0x077100cb&&Marshal.ReadInt32(observed,4)==0x8119,"direct callback receives requested sockaddr");
            Marshal.WriteInt32(screen,0x264,0);dispatch(U(module),finder,state);Check(Marshal.ReadInt32(observed,12)==2,"foreign finder uses original callback");Marshal.WriteInt32(screen,0x264,finder.ToInt32());
            Query(state,"not-an-ip");Check(begin(U(module),menu,state)==0,"invalid begin refused");dispatch(U(module),finder,state);Check(Marshal.ReadInt32(observed,12)==3,"invalid attempt cancels previous target");
            Query(state,"203.0.113.7");Check(begin(U(module),menu,state)==1,"direct attempt can restart");
            var tick=(Tick)Marshal.GetDelegateForFunctionPointer(Plus(wallet,0x2000+Export(hook,"TickUi")),typeof(Tick));tick(U(module),screen,state);Marshal.WriteInt32(screen,0);tick(U(module),screen,state);Marshal.WriteInt32(screen,unchecked((int)(U(module)+0x1d4db14)));dispatch(U(module),finder,state);Check(Marshal.ReadInt32(observed,12)==4,"leaving browser cancels direct target");
            object hack=catalog.GetField("NativeDirectDiscovery",Any).GetValue(null);byte[] body=Builder(hack)(U(module),U(branch));
            Check(body[0]==0x9c&&body[1]==0x60&&body[body.Length-5]==0xe9,"discovery wrapper preservation and tail");Check(unchecked(U(branch)+(uint)body.Length+(uint)I(body,body.Length-4))==U(module)+0x110ce7d,"discovery original continuation");
            // Execute the generated wrapper with the original ESI/finder contract.
            Put(Plus(module,0x110ce7d),0xc3);Put(branch,body);
            var w=new List<byte>(new byte[]{0x9c,0x60,0xbe});Imm(w,U(finder));w.Add(0xe8);Imm(w,unchecked(U(branch)-(U(branch)+0x1000+(uint)w.Count+4)));w.AddRange(new byte[]{0x61,0x9d,0xc3});Put(Plus(branch,0x1000),w.ToArray());
            ((Run)Marshal.GetDelegateForFunctionPointer(Plus(branch,0x1000),typeof(Run)))();Check(Marshal.ReadInt32(observed,12)==5,"generated discovery wrapper dispatches original fallback");
            Query(state,"203.0.113.7");Check(begin(U(module),menu,state)==1,"timeout attempt begins");Marshal.WriteInt32(screen,0);
            for(int i=0;i<901;i++)tick(U(module),screen,state);
            Marshal.WriteInt32(screen,unchecked((int)(U(module)+0x1d4db14)));dispatch(U(module),finder,state);
            Check(Marshal.ReadInt32(state,48)==205&&Marshal.ReadInt32(observed,12)==6,"bounded direct attempt expires and restores original discovery");
            Console.WriteLine("PASS isolated direct discovery: IPv4 parsing, sockaddr, scoped callback, fallback, cancellation and generated wrapper execution. Game peers are not simulated.");
        }finally{VirtualFree(branch,UIntPtr.Zero,0x8000);VirtualFree(module,UIntPtr.Zero,0x8000);}
    }
    static void Mov(List<byte> code,byte opcode,uint v){code.Add(opcode);Imm(code,v);}
    static void NativeNetworkCase(Type hook,IntPtr wallet)
    {
        IntPtr module=Allocate(0x1e60000);
        try{
            IntPtr screen=Plus(module,0x1000),menu=Plus(module,0x2000),lobby=Plus(module,0x3000),room=Plus(module,0x4000),owner=Plus(module,0x5000),local=Plus(module,0x6000),transport=Plus(module,0x7000),state=Plus(wallet,0x38000),network=Plus(state,3784);
            Clear(state,4040);Marshal.WriteInt32(state,unchecked((int)0x3849554e));Marshal.WriteInt32(state,4,1);Marshal.WriteInt32(state,8,3784);Marshal.WriteInt32(state,28,2);
            Marshal.WriteInt32(screen,unchecked((int)(U(module)+0x1d4ac78)));Marshal.WriteInt32(screen,0x270,lobby.ToInt32());Marshal.WriteByte(screen,0x284,1);
            Marshal.WriteInt32(lobby,unchecked((int)(U(module)+0x1d6a474)));Marshal.WriteInt32(lobby,8,local.ToInt32());Marshal.WriteInt32(room,unchecked((int)(U(module)+0x1d6a510)));Marshal.WriteInt32(room,0x58,owner.ToInt32());Marshal.WriteInt32(room,0x5c,8);Marshal.WriteInt16(room,0x78,unchecked((short)49001));Marshal.WriteInt32(room,0x50,0x55667788);Marshal.WriteInt32(room,0x54,0x11223344);
            foreach(var user in new[]{owner,local}){Marshal.WriteInt32(user,unchecked((int)(U(module)+0x1d6a568)));Marshal.WriteInt32(user,0xe8,1);}
            var getRoom=new List<byte>();Mov(getRoom,0xb8,U(room));getRoom.Add(0xc3);Put(Plus(module,0x8000),getRoom.ToArray());Marshal.WriteInt32(Plus(module,0x1d6a474+0x44),Plus(module,0x8000).ToInt32());
            Put(Plus(module,0x8100),0x8d,0x41,0x1c,0xc3);Marshal.WriteInt32(Plus(module,0x1d6a510+8),Plus(module,0x8100).ToInt32());
            Put(Plus(room,0x1c),System.Text.Encoding.ASCII.GetBytes("Native fixture\0"));Marshal.WriteInt32(room,0x1c+16,14);Marshal.WriteInt32(room,0x1c+20,15);
            Tick tick=(Tick)Marshal.GetDelegateForFunctionPointer(Plus(wallet,0x2000+Export(hook,"TickUi")),typeof(Tick));tick(U(module),screen,state);
            Check(Marshal.ReadInt32(network)==unchecked((int)0x38544e4e)&&Marshal.ReadInt32(network,8)==256,"network extension initialized independently");
            Check(Marshal.ReadInt32(network,16)==1&&Marshal.ReadInt32(network,36)==1&&Marshal.ReadInt32(network,40)==8&&Marshal.ReadInt32(network,28)==49001,"actual host observation fields");
            Check(Marshal.ReadInt32(network,12)==Marshal.ReadInt32(network,232)&&(Marshal.ReadInt32(network,12)&1)==0,"host observation seqlock committed");
            Check(System.Text.Encoding.ASCII.GetString(Snapshot(Plus(network,48),64)).StartsWith("Native fixture"),"display name getter uses virtual+8");
            Marshal.WriteInt32(state,32,32);tick(U(module),screen,state);Check(Marshal.ReadInt32(network,44)==1,"private requested policy published");
            Marshal.WriteInt16(room,0x78,0);tick(U(module),screen,state);Check(Marshal.ReadInt32(network,16)==0,"invalid host port fails closed");Marshal.WriteInt16(room,0x78,unchecked((short)49001));
            Marshal.WriteByte(screen,0x284,0);Marshal.WriteInt32(local,0xe8,2);Marshal.WriteInt32(lobby,0x10,5);Marshal.WriteByte(lobby,0x2d,1);Marshal.WriteInt32(lobby,0x70,transport.ToInt32());Marshal.WriteInt32(transport,unchecked((int)(U(module)+0x1d6a364)));Put(Plus(module,0x8200),0x33,0xc0,0xc3);Marshal.WriteInt32(Plus(module,0x1d6a364+0x10),Plus(module,0x8200).ToInt32());
            tick(U(module),screen,state);Check(Marshal.ReadInt32(network,112)==1&&Marshal.ReadInt32(network,116)==0x55667788&&Marshal.ReadInt32(network,120)==0x11223344,"accepted live client observation");
            Marshal.WriteByte(lobby,0x2d,0);tick(U(module),screen,state);Check(Marshal.ReadInt32(network,112)==0,"client navigation without acknowledgement is not connected");Marshal.WriteByte(lobby,0x2d,1);
            // Wire the original browser callback and a Finder/row to owned stubs.
            IntPtr finder=Plus(module,0x9000),discoverer=Plus(module,0xa000),button=Plus(module,0xb000),control=Plus(module,0xb100),row=Plus(module,0xc000),vector=Plus(module,0xd000),rowVector=Plus(module,0xd100),joins=Plus(module,0xd200);
            Marshal.WriteInt32(menu,unchecked((int)(U(module)+0x1d45bac)));Marshal.WriteInt32(menu,0x26c,button.ToInt32());Marshal.WriteInt32(menu,0x270,control.ToInt32());Marshal.WriteInt32(control,4,1);Put(Plus(module,0x9c76e0),0x8b,0x44,0x24,0x08,0xff,0x48,0x04,0xc2,0x08,0x00);
            Marshal.WriteInt32(finder,Plus(module,0xe000).ToInt32());Marshal.WriteInt32(finder,4,discoverer.ToInt32());Marshal.WriteInt32(discoverer,unchecked((int)(U(module)+0x1d69be0)));Marshal.WriteInt32(Plus(module,0x1d69be0+0x18),Plus(module,0x8300).ToInt32());Put(Plus(module,0x8300),0xc2,0x04,0x00);
            var enumerate=new List<byte>(new byte[]{0x8b,0x44,0x24,0x04,0xc7,0x00});Imm(enumerate,U(vector));enumerate.AddRange(new byte[]{0xc7,0x40,0x04});Imm(enumerate,U(vector)+4);enumerate.AddRange(new byte[]{0xc7,0x40,0x08});Imm(enumerate,U(vector)+4);enumerate.AddRange(new byte[]{0xc2,0x04,0x00});Put(Plus(module,0x8400),enumerate.ToArray());Marshal.WriteInt32(Plus(module,0xe000+8),Plus(module,0x8400).ToInt32());Marshal.WriteInt32(vector,room.ToInt32());Put(Plus(module,0xeaa00),0xc3);
            Put(Plus(module,0x8500),0x33,0xc0,0xc3);Marshal.WriteInt32(Plus(module,0x1d6a510+0x20),Plus(module,0x8500).ToInt32());
            Marshal.WriteInt32(Plus(module,0x1e5147c),screen.ToInt32());Marshal.WriteInt32(screen,0x264,finder.ToInt32());Marshal.WriteInt32(screen,0x278,rowVector.ToInt32());Marshal.WriteInt32(screen,0x27c,Plus(rowVector,4).ToInt32());Marshal.WriteInt32(rowVector,row.ToInt32());Marshal.WriteInt32(row,0x64,screen.ToInt32());Marshal.WriteInt32(row,0x68,room.ToInt32());Marshal.WriteInt32(row,0x44,button.ToInt32());Marshal.WriteInt32(row,0x48,control.ToInt32());Marshal.WriteInt32(button,0x14,room.ToInt32());Marshal.WriteInt16(room,0x80,2);Marshal.WriteInt32(room,0x84,0x077100cb);
            var join=new List<byte>(new byte[]{0xff,0x05});Imm(join,U(joins));join.AddRange(new byte[]{0x8b,0x44,0x24,0x08,0xff,0x48,0x04,0xc2,0x08,0x00});Put(Plus(module,0xaf03e0),join.ToArray());
            Marshal.WriteInt32(network,128,1);Marshal.WriteInt32(network,136,1);Marshal.WriteInt32(network,140,0x55667788);Marshal.WriteInt32(network,144,0x11223344);Marshal.WriteInt32(network,236,49001);Put(Plus(network,148),System.Text.Encoding.ASCII.GetBytes("203.0.113.7\0"));
            tick(U(module),menu,state);Check(Marshal.ReadInt32(network,132)==1&&Marshal.ReadInt32(network,136)==2,"queued native join acknowledged as discovery only");
            Marshal.WriteInt32(screen,unchecked((int)(U(module)+0x1d4db14)));Marshal.WriteInt32(room,0x84,0x087100cb);
            for(int i=0;i<15;i++)tick(U(module),screen,state);Check(Marshal.ReadInt32(joins)==0&&Marshal.ReadInt32(network,136)==2,"wrong observed IPv4 is never autojoined");Marshal.WriteInt32(room,0x84,0x077100cb);Marshal.WriteInt16(room,0x78,unchecked((short)49002));
            for(int i=0;i<15;i++)tick(U(module),screen,state);Check(Marshal.ReadInt32(joins)==0&&Marshal.ReadInt32(network,136)==2,"wrong lobby port is never autojoined");Marshal.WriteInt16(room,0x78,unchecked((short)49001));Marshal.WriteInt32(room,0x54,0x11223345);
            for(int i=0;i<15;i++)tick(U(module),screen,state);Check(Marshal.ReadInt32(joins)==0&&Marshal.ReadInt32(network,136)==2,"wrong native room ID is never autojoined");
            Marshal.WriteInt32(room,0x54,0x11223344);for(int i=0;i<15;i++)tick(U(module),screen,state);Check(Marshal.ReadInt32(joins)==1&&Marshal.ReadInt32(network,136)==3,"matching native row dispatches original join without claiming connection");
            Marshal.WriteInt32(screen,unchecked((int)(U(module)+0x1d4ac78)));Marshal.WriteByte(lobby,0x2d,0);tick(U(module),screen,state);Check(Marshal.ReadInt32(network,136)==3,"navigation alone cannot confirm a queued join");
            Marshal.WriteByte(lobby,0x2d,1);Marshal.WriteInt32(room,0x54,0x11223345);tick(U(module),screen,state);Check(Marshal.ReadInt32(network,136)==3,"a different accepted lobby cannot confirm the target");
            Marshal.WriteInt32(room,0x54,0x11223344);tick(U(module),screen,state);Check(Marshal.ReadInt32(network,136)==4&&Marshal.ReadInt32(state,48)==212,"only matching accepted live lobby confirms the join");
            var begin = (BeginDirect)Marshal.GetDelegateForFunctionPointer(
                Plus(wallet,0x2000+Export(hook,"NativeLanBeginDirect")),typeof(BeginDirect));
            Query(state,"203.0.113.7");
            Check(begin(U(module),menu,state)==1,"direct Internet join starts the native browser");
            Marshal.WriteInt32(screen,unchecked((int)(U(module)+0x1d4db14)));
            Marshal.WriteInt16(room,0x78,0);
            for(int i=0;i<15;i++)tick(U(module),screen,state);
            Check(Marshal.ReadInt32(joins)==1,"direct discovery ignores a room with no live lobby port");
            Marshal.WriteInt16(room,0x78,6530);
            for(int i=0;i<15;i++)tick(U(module),screen,state);
            Check(Marshal.ReadInt32(joins)==2&&Marshal.ReadInt32(state,48)==209,"direct Internet join waits for acceptance");
            tick(U(module),IntPtr.Zero,state);
            Marshal.WriteInt32(screen,unchecked((int)(U(module)+0x1d4ac78)));
            Marshal.WriteInt16(room,0x78,unchecked((short)49001));tick(U(module),screen,state);
            Check(Marshal.ReadInt32(state,48)==209,"a lobby on a different port cannot confirm a direct join");
            Marshal.WriteInt16(room,0x78,6530);tick(U(module),screen,state);
            Check(Marshal.ReadInt32(state,48)==212&&Marshal.ReadInt32(network,128)==1,
                "the matching accepted direct lobby confirms without publishing a directory command");
            Console.WriteLine("PASS isolated host observation and queued directory join: identity/endpoint filtering, real-row callback contract and accepted-client confirmation. Original APIs are stubbed; no game peers joined.");
        }finally{VirtualFree(module,UIntPtr.Zero,0x8000);}
    }
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
    static void BotRaceCase(Type catalog)
    {
        IntPtr memory = Allocate(0xA9B000);
        try
        {
            IntPtr cave = Plus(memory, 0x1000), wrapper = Plus(memory, 0x2000);
            IntPtr screen = Plus(memory, 0x3000), room = Plus(memory, 0x4000);
            IntPtr race = Plus(memory, 0x5000), result = Plus(memory, 0x6000);
            object hack = catalog.GetField("NativeBotRaceLaunch", Any).GetValue(null);
            Put(cave, Builder(hack)(U(memory), U(cave)));
            Put(Plus(memory, 0xA99318), 0x6a, 0x01, 0x5a, 0xc3); // EDX = enabled
            Put(Plus(memory, 0xA99321), 0x31, 0xd2, 0xc3); // EDX = disabled
            int expectedRoomType = unchecked((int)(U(memory) + 0x1D6A510));

            Action<string, int, bool, bool, bool, int, int, bool> verify =
                (name, humans, host, bots, nativeRoom, slots, mode, allowed) =>
            {
                Marshal.WriteInt32(screen, 0x278, mode < 0 ? 0 : race.ToInt32());
                Marshal.WriteByte(screen, 0x284, host ? (byte)1 : (byte)0);
                Marshal.WriteByte(screen, 0x288, bots ? (byte)1 : (byte)0);
                Marshal.WriteInt32(room, nativeRoom ? expectedRoomType : 0);
                Marshal.WriteInt32(room, 0x5c, slots);
                Marshal.WriteInt32(race, 0x10, mode);
                var code = new List<byte> { 0x60 }; // PUSHAD
                Mov(code, 0xb8, 0x11223344);
                Mov(code, 0xb9, (uint)humans);
                Mov(code, 0xbb, U(screen));
                Mov(code, 0xbf, U(room));
                code.Add(0xe8);
                Imm(code, unchecked(U(cave) - (U(wrapper) + (uint)code.Count + 4)));
                Store(code, 0x05, U(result));
                Store(code, 0x15, U(result) + 4);
                code.AddRange(new byte[] { 0x61, 0xc3 }); // POPAD, RET
                Put(wrapper, code.ToArray());
                var run = (Run)Marshal.GetDelegateForFunctionPointer(wrapper, typeof(Run));
                run();
                Check(Marshal.ReadInt32(result) == 0x11223344, "bot race preserves EAX: " + name);
                Check(Marshal.ReadInt32(result, 4) == (allowed ? 1 : 0), "bot race eligibility: " + name);
            };
            verify("host with bots", 1, true, true, true, 2, 0, true);
            verify("native capacity limit", 1, true, true, true, 12, 0, true);
            verify("stock room with two players", 2, false, false, false, 0, -1, true);
            verify("empty room", 0, true, true, true, 2, 0, false);
            verify("non-host", 1, false, true, true, 2, 0, false);
            verify("bots disabled", 1, true, false, true, 2, 0, false);
            verify("foreign room", 1, true, true, false, 2, 0, false);
            verify("no bot slot", 1, true, true, true, 1, 0, false);
            verify("capacity above native limit", 1, true, true, true, 13, 0, false);
            verify("event unavailable", 1, true, true, true, 2, -1, false);
            verify("non-Classic event", 1, true, true, true, 2, 1, false);
            Console.WriteLine("PASS experimental bot race Launch eligibility and EAX preservation.");
        }
        finally
        {
            VirtualFree(memory, UIntPtr.Zero, 0x8000);
        }
    }

    static void BotRaceAloneCase(Type catalog, Type hook, IntPtr wallet)
    {
        IntPtr module = Allocate(0x1E60000);
        try
        {
            IntPtr manager = Plus(module, 0x1000), callback = Plus(module, 0x2000);
            IntPtr server = Plus(module, 0x3000), roster = Plus(module, 0x4000);
            IntPtr human = Plus(module, 0x5000), bot = Plus(module, 0x8000);
            IntPtr cave = Plus(module, 0xb000), result = Plus(module, 0xc000);
            Marshal.WriteInt32(Plus(module, 0x1e511bc), manager.ToInt32());
            Marshal.WriteInt32(manager, Plus(module, 0x1d3a030).ToInt32());
            Marshal.WriteInt32(manager, 0xe8, callback.ToInt32());
            Marshal.WriteInt32(callback, Plus(module, 0x1d6a4e4).ToInt32());
            Marshal.WriteInt32(manager, 0xec, server.ToInt32());
            Marshal.WriteInt32(server, Plus(module, 0x1d41dac).ToInt32());
            Marshal.WriteInt32(server, 0x104, 1);
            Marshal.WriteInt32(server, 0x108, 1);
            Marshal.WriteByte(server, 0x168, 1);
            Marshal.WriteInt32(server, 0xc, roster.ToInt32());
            Marshal.WriteInt32(server, 0x10, Plus(roster, 8).ToInt32());
            Marshal.WriteInt32(server, 0x14, Plus(roster, 8).ToInt32());
            Marshal.WriteInt32(roster, human.ToInt32());
            Marshal.WriteInt32(roster, 4, bot.ToInt32());
            foreach (IntPtr racer in new[] { human, bot })
            {
                Marshal.WriteInt32(racer, Plus(module, 0x1d19464).ToInt32());
                Marshal.WriteByte(racer, 0x2290, 1);
            }
            Marshal.WriteByte(bot, 0x22f8, 1);
            Marshal.WriteByte(bot, 0x22f9, 1);Marshal.WriteByte(human, 0x22f9, 1);

            IntPtr entry = Plus(wallet, 0x2000 + Export(hook, "NativeSoloBotRace"));
            var eligible = (SoloBotRace)Marshal.GetDelegateForFunctionPointer(entry, typeof(SoloBotRace));
            Check(eligible(U(module)) == 1, "solo bot race uses the complete object's server field");
            Marshal.WriteInt32(manager, 0xec, callback.ToInt32());
            Check(eligible(U(module)) == 0, "lobby callback cannot stand in for the server");
            Marshal.WriteInt32(manager, 0xec, server.ToInt32());
            Marshal.WriteInt32(server, 0x104, 2);
            Check(eligible(U(module)) == 0, "original alert retained after a human peer leaves");
            Marshal.WriteInt32(server, 0x104, 1);
            Marshal.WriteByte(bot, 0x2290, 0);
            Check(eligible(U(module)) == 0, "inactive bot cannot clear the alert");
            Marshal.WriteByte(bot, 0x2290, 1);
            Marshal.WriteInt32(server, 0x10, Plus(roster, 12).ToInt32());
            Check(eligible(U(module)) == 0, "out-of-bounds roster rejected");
            Marshal.WriteInt32(server, 0x10, Plus(roster, 8).ToInt32());

            IntPtr head=Plus(module,0xd000),node=Plus(module,0xe000),transport=Plus(module,0xf000),peer=Plus(module,0x12000);
            Marshal.WriteInt32(server,0x28,head.ToInt32());Marshal.WriteInt32(server,0x2c,1);
            Marshal.WriteInt32(head,node.ToInt32());Marshal.WriteInt32(node,0x10,transport.ToInt32());
            Marshal.WriteInt32(transport,Plus(module,0x1d19738).ToInt32());
            Marshal.WriteInt16(transport,0x21f8,2);Marshal.WriteInt32(transport,0x21fc,0x0100007f);
            Marshal.WriteInt32(transport,0x21b8,41);Marshal.WriteInt32(transport,0x23e0,peer.ToInt32());
            Marshal.WriteByte(peer,0x2b9,1);
            Marshal.WriteByte(human,0x2290,0);Marshal.WriteByte(bot,0x2290,0);
            Check(eligible(U(module))==1,"connected local client with provisional AI rows should clear the premature alert");
            Marshal.WriteInt32(transport,0x21fc,0x0200007f);
            Check(eligible(U(module))==0,"an unverified source address must preserve the alert");
            Marshal.WriteInt32(transport,0x21fc,0x0100007f);Marshal.WriteByte(peer,0x2b9,0);
            Check(eligible(U(module))==0,"a disconnected local race peer must preserve the alert");
            Marshal.WriteByte(peer,0x2b9,1);

            hook.GetField("StateAddress", Any).SetValue(null, (long)U(wallet) + 0x38000);
            object hack = catalog.GetField("NativeBotRaceAlone", Any).GetValue(null);
            Put(cave, Builder(hack)(U(module), U(cave)));
            var reset = new List<byte> { 0xff, 0x05 };
            Imm(reset, U(result));
            reset.Add(0xc3);
            Put(Plus(module, 0xD555EF), reset.ToArray());
            var pause = new List<byte> { 0xff, 0x05 };
            Imm(pause, U(result) + 4);
            pause.Add(0xc3);
            Put(Plus(module, 0x83CC20), pause.ToArray());
            Put(Plus(module, 0xD5559F), 0xc3);
            var run = (Run)Marshal.GetDelegateForFunctionPointer(cave, typeof(Run));
            run();
            Check(Marshal.ReadInt32(result) == 1 && Marshal.ReadInt32(result, 4) == 0,
                "solo wrapper takes only the timer reset branch");
            Marshal.WriteByte(server, 0x168, 0);
            run();
            Check(Marshal.ReadInt32(result) == 1 && Marshal.ReadInt32(result, 4) == 1,
                "wrapper replays original pause when AI fill is off");
            Console.WriteLine("PASS native solo bot race predicate and original alert fallback.");
        }
        finally
        {
            VirtualFree(module, UIntPtr.Zero, 0x8000);
        }
    }

    static void RosterCase(Type hook, IntPtr wallet)
    {
        var entry = Plus(wallet, 0x2000 + Export(hook, "NativeRosterPage"));
        var plan = (RosterPage)Marshal.GetDelegateForFunctionPointer(entry, typeof(RosterPage));
        uint page = 2;
        Check(plan(20, 1, ref page) == 20 && page == 2, "20 slots keep the page containing slots 17 to 20");
        page = 7;
        Check(plan(60, 1, ref page) == 60 && page == 7, "60 slots reach the final page");
        Check(plan(20, 1, ref page) == 20 && page == 2, "smaller custom grid clamps the active page");
        Check(plan(1, 1, ref page) == 1 && page == 0, "one slot shows one page");
        Check(plan(1, 2, ref page) == 2 && page == 0, "live humans are kept visible");
        Console.WriteLine("PASS native room roster page and occupied-slot bounds.");
    }

    static void LiveRosterCase(Type hook, IntPtr wallet, IntPtr memory)
    {
        IntPtr roster = memory, slots = Plus(memory, 0x1000), sources = Plus(memory, 0x2000);
        Clear(slots, 204);
        Marshal.WriteInt32(roster, 0, 8);
        Marshal.WriteInt32(roster, 12, 42);
        Marshal.WriteInt32(roster, 16, 7);
        var entry = Plus(wallet, 0x2000 + Export(hook, "NativeAssignRoster"));
        var assign = (AssignRoster)Marshal.GetDelegateForFunctionPointer(entry, typeof(AssignRoster));
        Action<int[]> users = identities =>
        {
            Marshal.WriteInt32(roster, 4, identities.Length);
            for (int i = 0; i < identities.Length; ++i)
                for (int field = 0; field < 4; ++field)
                    Marshal.WriteInt32(roster, 20 + 48 * i + field * 4, identities[i] + field);
        };
        Action<string, int[]> verify = (label, expected) =>
        {
            Check(assign(roster, slots, sources) == 1, label + " accepted");
            for (int i = 0; i < expected.Length; ++i)
                Check(Marshal.ReadInt32(sources, i * 4) == expected[i], label + " slot " + (i + 1));
        };
        users(new[] { 1, 10, 20, 30 });
        verify("initial roster", new[] { 1, 2, 3, 4, 0 });
        users(new[] { 1, 30, 20 });
        verify("a departure frees exactly its original slot", new[] { 1, 0, 3, 2, 0 });
        users(new[] { 1, 40, 20, 30 });
        verify("new player reuses the vacated slot", new[] { 1, 2, 3, 4, 0 });
        users(new[] { 1, 30, 30 });
        byte[] prior = Snapshot(slots, 204);
        Check(assign(roster, slots, sources) == 0 && Equal(prior, Snapshot(slots, 204)),
              "duplicate identity does not mutate the last valid assignment");
        users(new[] { 1, 30, 20 });
        Marshal.WriteInt32(roster, 16, 8);
        verify("new room resets the slot mapping", new[] { 1, 2, 3, 0, 0 });
        users(new[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        verify("full eight-place native roster", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 0 });
        Console.WriteLine("PASS stable native roster mapping on join, departure, reorder and room changes.");
    }

    static void RoomCapacityCase(Type catalog, Type hook, IntPtr memory)
    {
        IntPtr frame = Plus(memory, 0x1800), state = Plus(memory, 0x2000);
        IntPtr eventLimit = Plus(memory, 0x3000), wrapper = Plus(memory, 0x4000);
        hook.GetField("StateAddress", Any).SetValue(null, (long)U(state));
        object hack = catalog.GetField("NativeRoomCapacity", Any).GetValue(null);
        byte[] body = Builder(hack)(0x10000000, U(memory));
        Check(body[body.Length - 5] == 0xe9, "capacity hook rejoins the stock room-name path");
        body[body.Length - 5] = 0xc3;
        Put(memory, body);
        var code = new List<byte> { 0x60 };
        Mov(code, 0xb8, 0x11223344);
        Mov(code, 0xbd, U(frame));
        Mov(code, 0xb9, U(eventLimit));
        code.Add(0xe8);
        Imm(code, unchecked(U(memory) - (U(wrapper) + (uint)code.Count + 4)));
        code.AddRange(new byte[] { 0x61, 0xc3 });
        Put(wrapper, code.ToArray());
        var run = (Run)Marshal.GetDelegateForFunctionPointer(wrapper, typeof(Run));
        foreach (var item in new[] {
            new[] { 0, 8, 8 }, new[] { 1, 8, 2 }, new[] { 4, 8, 4 },
            new[] { 8, 8, 8 }, new[] { 20, 8, 8 }, new[] { 60, 6, 6 },
            new[] { -1, 8, 8 }, new[] { 61, 8, 8 } })
        {
            Marshal.WriteInt32(state, 36, item[0]);
            Marshal.WriteInt32(eventLimit, item[1]);
            run();
            Check(Marshal.ReadInt32(frame, -0x1c) == item[2], "native capacity for grid " + item[0] + " and event " + item[1]);
            Check(Marshal.ReadInt32(frame, -0x18) == 0x11223344, "capacity hook preserves displaced stock store");
        }
        Console.WriteLine("PASS native room capacity argument: default, custom, event cap and minimum.");
    }

    static void InternetBindCase(Type catalog, Type hook, IntPtr memory)
    {
        IntPtr state = Plus(memory, 0x2000);
        IntPtr gate = Plus(state, (int)hook.GetField("InternetHostGateOffset", Any).GetRawConstantValue());
        IntPtr frame = Plus(memory, 0x1800), continuation = Plus(memory, 0x400);
        IntPtr result = Plus(memory, 0x800), wrapper = Plus(memory, 0xc00);
        hook.GetField("StateAddress", Any).SetValue(null, (long)U(state));

        foreach (bool lobby in new[] { true, false })
        {
            string name = lobby ? "InternetLobbyBind" : "InternetRaceBind";
            int resumeRva = lobby ? 0x110D90A : 0x96C6DA;
            var code = new List<byte> { 0x60 }; // Save the fixture caller's registers.
            Mov(code, lobby ? (byte)0xb8 : (byte)0xbd, U(frame));
            Mov(code, lobby ? (byte)0xbb : (byte)0xb8, 0x11223344);
            code.Add(0xf9); // Carry must survive the gate comparison.
            code.Add(0xe8);
            Imm(code, unchecked(U(memory) - (U(wrapper) + (uint)code.Count + 4)));
            code.AddRange(new byte[] { 0x61, 0xc3 });
            Put(wrapper, code.ToArray());

            code.Clear();
            if (lobby) code.Add(0x58); // Consume the original port argument.
            code.Add(0xa3); Imm(code, U(result));
            code.AddRange(new byte[] { 0x9c, 0x58, 0xa3 }); Imm(code, U(result) + 4);
            code.Add(0xc3);
            Put(continuation, code.ToArray());
            object hack = catalog.GetField(name, Any).GetValue(null);
            Put(memory, Builder(hack)((long)U(continuation) - resumeRva, U(memory)));
            Marshal.WriteInt32(frame, -0xb0, 0x76543210);
            var run = (Run)Marshal.GetDelegateForFunctionPointer(wrapper, typeof(Run));
            foreach (int enabled in new[] { 0, 1, 2 })
            {
                Marshal.WriteInt32(gate, enabled);
                run();
                int port = enabled == 1 ? (lobby ? 6530 : 6540) : (lobby ? 0 : 0x3344);
                Check((Marshal.ReadInt32(result, 4) & 1) == 1, name + " preserves flags");
                Check(lobby ? Marshal.ReadInt32(result) == port : (ushort)Marshal.ReadInt16(frame, -0x24) == port,
                    name + " selects the UDP port only when explicitly enabled");
                Check(lobby ? Marshal.ReadInt32(frame, 0x148) == 0x11223344 : Marshal.ReadInt32(result) == 0x76543210,
                    name + " replays the original adjacent instruction");
            }
        }
        Console.WriteLine("PASS Internet host port stubs: fixed requests, disabled fallback, stack and flags.");
    }

    static void PasswordCase(Type catalog,Type hook,IntPtr wallet)
    {
        IntPtr module=Allocate(0x1900000);
        try
        {
            IntPtr state=Plus(wallet,0x38000),gate=Plus(state,0x1020),lobby=Plus(module,0x1000),
                room=Plus(module,0x2000),connection=Plus(module,0x3000),profile=Plus(module,0x4000),
                encoded=Plus(module,0x5000),clock=Plus(module,0x6000);
            const long now=0x1122334455667788;
            var code=new List<byte>(new byte[]{0x8b,0x44,0x24,4,0xc7,0x00});
            Imm(code,(uint)(now&uint.MaxValue));code.AddRange(new byte[]{0xc7,0x40,4});
            Imm(code,(uint)(now>>32));code.AddRange(new byte[]{0xc2,4,0});
            Put(clock,code.ToArray());Marshal.WriteInt32(module,0x1884108,unchecked((int)U(clock)));
            Marshal.WriteInt32(lobby,0xc,unchecked((int)U(room)));
            Marshal.WriteInt32(room,unchecked((int)(U(module)+0x1d6a510)));
            Marshal.WriteInt32(room,0x50,17);Marshal.WriteInt32(room,0x54,29);
            Marshal.WriteInt32(profile,0x30,41);
            var authorize=(Admission)Marshal.GetDelegateForFunctionPointer(Plus(wallet,0x2000+Export(hook,"NativeAuthorizeProfile")),typeof(Admission));
            Func<uint> admit=()=>authorize(U(module),state,lobby,connection,profile);
            Marshal.WriteInt32(state,32,32);Marshal.WriteInt32(gate,1);
            Check(admit()==0,"Direct admission without a password key must fail closed");
            byte[] key=new byte[32];for(int i=0;i<key.Length;i++)key[i]=(byte)(i+1);
            Put(Plus(gate,12),key);Marshal.WriteInt32(gate,4,1);
            Check(admit()==0,"Direct admission without proof must be rejected");
            Action<long,long,byte[]> prepare=(time,nonce,secret)=>
            {
                byte[] input;
                using(var data=new MemoryStream())
                using(var writer=new BinaryWriter(data))
                {
                    writer.Write(17);writer.Write(29);writer.Write(41);writer.Write(0);writer.Write(0);writer.Write(0);
                    writer.Write(time);writer.Write(nonce);input=data.ToArray();
                }
                byte[] auth=new byte[32];
                Buffer.BlockCopy(input,24,auth,0,16);
                using(var hmac=new HMACSHA256(secret))Buffer.BlockCopy(hmac.ComputeHash(input),0,auth,16,16);
                string value=Convert.ToBase64String(auth).TrimEnd('=').Replace('+','-').Replace('/','_');
                Put(encoded,Encoding.ASCII.GetBytes(value));
                Put(Plus(profile,0x44),Encoding.ASCII.GetBytes("A8OL-PASSWORD-1"));
                Marshal.WriteInt32(profile,0x54,15);Marshal.WriteInt32(profile,0x58,15);
                Marshal.WriteInt32(profile,0x5c,unchecked((int)U(encoded)));
                Marshal.WriteInt32(profile,0x6c,43);Marshal.WriteInt32(profile,0x70,47);
                Marshal.WriteByte(profile,0x40,1);
            };
            prepare(now,81,new byte[32]);Check(admit()==0,"Wrong password proof was accepted");
            prepare(now-310000000,82,key);Check(admit()==0,"Stale proof was accepted");
            prepare(now,83,key);Marshal.WriteInt32(profile,0x30,42);Check(admit()==0,"Proof for a different peer was accepted");
            Marshal.WriteInt32(profile,0x30,41);
            prepare(now,84,key);Marshal.WriteInt32(room,0x50,18);Check(admit()==0,"Proof for a different room was accepted");
            Marshal.WriteInt32(room,0x50,17);
            prepare(now,85,key);Check(admit()==1&&Marshal.ReadByte(profile,0x40)==0,"Valid password proof or metadata removal failed");
            prepare(now,85,key);Check(admit()==0,"Password proof replay was accepted");
            Marshal.WriteInt32(connection,0x134,1);
            Check(admit()==1,"An already admitted connection lost its acknowledgement");
            Marshal.WriteInt32(connection,0x134,0);
            IntPtr send=Plus(module,0x7000),driver=Plus(module,0x8000),authorizeCode=Plus(module,0x9000),received=Plus(module,0xa000),
                authorizeDriver=Plus(module,0xb000),decision=Plus(module,0xc000);
            Put(Plus(module,0x135ba0),0x8b,0x44,0x24,4,0x89,1,0x8b,0x44,0x24,8,0x89,0x41,0x10,
                0xc7,0x41,0x14,0x40,0,0,0,0x8b,0xc1,0xc2,8,0);
            code.Clear();code.AddRange(new byte[]{0x56,0x57,0x8b,0x54,0x24,0x0c,0x8b,0x72,0x5c,0xbf});
            Imm(code,U(received));code.AddRange(new byte[]{0xb9,43,0,0,0,0xf3,0xa4,0x5f,0x5e,0xb8,1,0,0,0,0xc2,4,0});
            Put(Plus(module,0x110bd90),code.ToArray());
            Put(Plus(module,0x110e07b),0x61,0xc3);
            code.Clear();code.Add(0x60);code.Add(0xbf);Imm(code,U(lobby));code.Add(0xb9);Imm(code,U(connection));
            code.Add(0x68);Imm(code,U(profile));code.Add(0xe9);Imm(code,unchecked(U(send)-(U(driver)+(uint)code.Count+4)));
            Put(driver,code.ToArray());
            Put(send,Builder(catalog.GetField("NativeSendProfile",Any).GetValue(null))(U(module),U(send)));
            Put(Plus(gate,44),key);Marshal.WriteInt32(gate,8,1);
            Marshal.WriteInt32(gate,76,91);Marshal.WriteInt32(gate,92,17);Marshal.WriteInt32(gate,96,29);
            var runSend=(Run)Marshal.GetDelegateForFunctionPointer(driver,typeof(Run));runSend();
            Check(Marshal.ReadByte(profile,0x40)==0&&Marshal.ReadInt32(gate,92)==0,"Guest wrapper failed to restore the profile or consume its join scope");
            Check(Marshal.ReadByte(received)!=0,"The native profile send did not receive encoded proof");
            Marshal.WriteInt32(profile,0x5c,unchecked((int)U(received)));
            Marshal.WriteByte(profile,0x40,1);
            foreach(var tail in new[]{Tuple.Create(0x110e95b,1),Tuple.Create(0x110e922,0)})
            {
                code.Clear();code.Add(0xb8);Imm(code,(uint)tail.Item2);code.Add(0xa3);Imm(code,U(decision));
                code.AddRange(new byte[]{0x61,0xc3});Put(Plus(module,tail.Item1),code.ToArray());
            }
            code.Clear();code.Add(0x60);code.Add(0xbf);Imm(code,U(lobby));code.Add(0xbb);Imm(code,U(connection));
            code.Add(0xbe);Imm(code,U(profile));code.Add(0xe9);Imm(code,unchecked(U(authorizeCode)-(U(authorizeDriver)+(uint)code.Count+4)));
            Put(authorizeDriver,code.ToArray());
            Put(authorizeCode,Builder(catalog.GetField("NativeAuthorizeProfile",Any).GetValue(null))(U(module),U(authorizeCode)));
            var runAuthorize=(Run)Marshal.GetDelegateForFunctionPointer(authorizeDriver,typeof(Run));
            runAuthorize();Check(Marshal.ReadInt32(decision)==1,"The host wrapper rejected the guest wrapper's encoded proof");
            runAuthorize();Check(Marshal.ReadInt32(decision)==0,"The host wrapper bypassed password admission");
            Marshal.WriteInt32(state,32,0);
            runAuthorize();Check(Marshal.ReadInt32(decision)==1,"Public admission incorrectly requires a password");
            Clear(gate,108);
        }
        finally{VirtualFree(module,UIntPtr.Zero,0x8000);}
        Console.WriteLine("PASS native password admission logic: standard HMAC interoperability, room and user binding, expiry and replay.");
    }

    static void RaceBoardCase(Type catalog,Type hook,IntPtr wallet)
    {
        IntPtr module=Allocate(0x1e60000);
        try
        {
            IntPtr state=Plus(wallet,0x38000),mode=Plus(module,0x1000),manager=Plus(module,0x2000),
                server=Plus(module,0x3000),callback=Plus(module,0x4000),ordered=Plus(module,0x5000),
                roster=Plus(module,0x6000),guest=Plus(module,0x7000),owner=Plus(module,0xa000),
                bot=Plus(module,0xd000),peer=Plus(module,0x10000),clock=Plus(module,0x11000),
                driver=Plus(module,0x12000),sort=Plus(module,0x13000),hudBranch=Plus(module,0x14000),
                hudDriver=Plus(module,0x15000),settings=Plus(module,0x16000),decision=Plus(module,0x17000),
                transport=Plus(module,0x18000);
            Action<IntPtr,int,IntPtr> pointer=(target,offset,address)=>Marshal.WriteInt32(target,offset,address.ToInt32());
            Action<IntPtr,int> type=(obj,va)=>pointer(obj,0,Plus(module,va-0x400000));
            Put(clock,0xb8,0x78,0x56,0x34,0x12,0x31,0xd2,0xc3);
            pointer(module,0x1884078,clock);pointer(module,0x1e511bc,manager);
            type(manager,0x213a030);type(server,0x2141dac);type(callback,0x216a4e4);type(mode,0x212a368);
            pointer(manager,0xe8,callback);pointer(manager,0xec,server);Marshal.WriteByte(server,0x168,1);
            pointer(mode,0x18,ordered);pointer(mode,0x1c,Plus(ordered,12));
            pointer(ordered,0,guest);pointer(ordered,4,owner);pointer(ordered,8,bot);
            type(guest,0x2119464);type(owner,0x2119464);type(bot,0x2119464);
            Marshal.WriteInt32(guest,0x2380,2);Marshal.WriteInt32(owner,0x2380,1);Marshal.WriteByte(bot,0x22f8,1);
            foreach(var racer in new[]{guest,owner,bot})Marshal.WriteByte(racer,0x22f9,1);
            Marshal.WriteInt32(guest,0x23e0,3);
            type(transport,0x2119738);pointer(transport,0x23e0,peer);
            Marshal.WriteInt32(peer,0x2a8,2);Marshal.WriteByte(peer,0x2b9,1);Marshal.WriteInt32(transport,0x21b8,57);
            var mapping=new List<byte>{0x81,0x7c,0x24,4};Imm(mapping,U(guest));
            mapping.AddRange(new byte[]{0x75,8,0xb8});Imm(mapping,U(transport));
            mapping.AddRange(new byte[]{0xc2,4,0,0x31,0xc0,0xc2,4,0});
            Put(Plus(module,0x974270),mapping.ToArray());
            Marshal.WriteInt32(roster,0,3);Marshal.WriteInt32(roster,4,2);Marshal.WriteInt32(roster,12,17);
            Marshal.WriteInt32(roster,16,29);Marshal.WriteInt32(roster,20,1);Marshal.WriteInt32(roster,68,2);
            Put(Plus(roster,36),Encoding.ASCII.GetBytes("Owner\0"));Put(Plus(roster,84),Encoding.ASCII.GetBytes("Guest\0"));
            var remember=(RememberRoster)Marshal.GetDelegateForFunctionPointer(Plus(wallet,0x2000+Export(hook,"NativeRememberRaceRoster")),typeof(RememberRoster));
            var capture=(Tick)Marshal.GetDelegateForFunctionPointer(Plus(wallet,0x2000+Export(hook,"NativeCaptureRaceBoard")),typeof(Tick));
            var hud=(RaceText)Marshal.GetDelegateForFunctionPointer(Plus(wallet,0x2000+Export(hook,"NativeRaceText")),typeof(RaceText));
            remember(roster);
            Put(Plus(module,0x5c2386),0xc3);
            Put(sort,Builder(catalog.GetField("NativeCaptureRaceBoard",Any).GetValue(null))(U(module),U(sort)));
            var code=new List<byte>(new byte[]{0x60,0xb9});Imm(code,U(mode));
            code.Add(0xe8);Imm(code,unchecked(U(sort)-(U(driver)+(uint)code.Count+4)));code.AddRange(new byte[]{0x61,0xc3});
            Put(driver,code.ToArray());
            ((Run)Marshal.GetDelegateForFunctionPointer(driver,typeof(Run)))();
            Check(hud(U(module),state)==IntPtr.Zero,"Race table was rendered with TAB released.");
            Put(hudBranch,Builder(catalog.GetField("NativeRaceHudAvailability",Any).GetValue(null))(U(module),U(hudBranch)));
            code.Clear();code.Add(0x60);code.Add(0xb8);Imm(code,U(settings));
            code.Add(0xe9);Imm(code,unchecked(U(hudBranch)-(U(hudDriver)+(uint)code.Count+4)));Put(hudDriver,code.ToArray());
            code.Clear();code.AddRange(new byte[]{0x74,0x0c,0xb8,1,0,0,0,0xa3});Imm(code,U(decision));
            code.AddRange(new byte[]{0x61,0xc3,0xb8,0,0,0,0,0xa3});Imm(code,U(decision));code.AddRange(new byte[]{0x61,0xc3});
            Put(Plus(module,0x9aa13a),code.ToArray());
            var runHud=(Run)Marshal.GetDelegateForFunctionPointer(hudDriver,typeof(Run));
            IntPtr position=Plus(module,0x1b000),positionDriver=Plus(module,0x1c000),frame=Plus(module,0x1e000);
            Put(position,Builder(catalog.GetField("NativeRaceHudPosition",Any).GetValue(null))(U(module),U(position)));
            Put(Plus(module,0x9ab563),0x5d,0xc3);
            code.Clear();code.AddRange(new byte[]{0x55,0xbd});Imm(code,U(frame));code.Add(0xb8);
            code.AddRange(BitConverter.GetBytes(-22.0f));
            code.AddRange(new byte[]{0x66,0x0f,0x6e,0xc0,0xe9});
            Imm(code,unchecked(U(position)-(U(positionDriver)+(uint)code.Count+4)));Put(positionDriver,code.ToArray());
            var runPosition=(Run)Marshal.GetDelegateForFunctionPointer(positionDriver,typeof(Run));
            Marshal.WriteInt32(decision,-1);runHud();
            Check(Marshal.ReadInt32(decision)==0,"HUD hook did not preserve the inactive stock branch.");
            runPosition();
            Check(BitConverter.ToSingle(Snapshot(Plus(frame,-0x108),4),0)==-22.0f,"Inactive HUD position changed.");
            Marshal.WriteInt32(state,0x10a0,1);
            runHud();Check(Marshal.ReadInt32(decision)==1,"HUD hook did not reach the original renderer while TAB was held.");
            runPosition();
            Check(BitConverter.ToSingle(Snapshot(Plus(frame,-0x108),4),0)==-176.0f,"The multiline standings baseline did not move.");
            string text=Marshal.PtrToStringAnsi(hud(U(module),state));
            Check(text!=null&&text.Contains("1.  Guest  |  57 ms")&&text.Contains("2.  Owner  |  HOST")&&text.Contains("3.  AI"),
                "HUD did not format the native ordered race and original RTT.");
            byte[] received=Snapshot(Plus(state,0x1100),700);
            Marshal.WriteInt32(peer,0x2a8,3);capture(U(module),state,mode);
            Check(Marshal.PtrToStringAnsi(hud(U(module),state)).Contains("Guest  |  -- ms"),"Another peer's RTT leaked into a row.");
            Marshal.WriteInt32(state,0x1100+20,0);
            Check(hud(U(module),state)==IntPtr.Zero,"Expired standings were displayed.");
            Put(Plus(module,0x7af5e0),0xb8,1,0,0,0,0xc3);
            Put(Plus(state,0x1400),received);
            Check(hud(U(module),state)==IntPtr.Zero,"Guest displayed standings from an unconfirmed room.");
            Marshal.WriteInt32(state,0x10a8,17);Marshal.WriteInt32(state,0x10ac,29);
            Check(Marshal.PtrToStringAnsi(hud(U(module),state))==text,"Guest HUD did not display the same authoritative standings.");
            Put(Plus(module,0x7af5e0),0x31,0xc0,0xc3);
            Check(hud(U(module),state)==IntPtr.Zero,"Guest standings carried over into a local race.");
            Put(Plus(state,0x1100),received);
            Marshal.WriteInt32(state,0x1100+24+56+16,3);
            Check(hud(U(module),state)==IntPtr.Zero,"An all-AI provisional race snapshot was displayed as joined standings.");
            Clear(Plus(state,0x10a0),0x900);
        }
        finally{VirtualFree(module,UIntPtr.Zero,0x8000);}
        Console.WriteLine("PASS original race-order/RTT snapshot and TAB formatting with native objects stubbed.");
    }

    public static int Main(string[] args) {
        IntPtr wallet=IntPtr.Zero,lap=IntPtr.Zero;FieldInfo stateField=null;long saved=0;
        try { Check(IntPtr.Size==4,"fixture must run x86");Check(args.Length==1&&File.Exists(args[0]),"explicit trainer assembly path required");Assembly a=Assembly.LoadFrom(Path.GetFullPath(args[0]));Type catalog=a.GetType("InjectionCatalog",true),hook=a.GetType("NativeLobbyHook",true);stateField=hook.GetField("StateAddress",Any);saved=(long)stateField.GetValue(null);
            wallet=Allocate(0x40000);Wallet(a,catalog,hook,wallet);DirectCase(catalog,hook,wallet);NativeNetworkCase(hook,wallet);PasswordCase(catalog,hook,wallet);RaceBoardCase(catalog,hook,wallet);lap=Allocate(0x6000);stateField.SetValue(null,(long)U(lap)+0x2000);object hack=catalog.GetField("NativeClassicLaps",Any).GetValue(null);Func<long,long,byte[]> build=Builder(hack);
            LapCase(build,lap,0,0,1,2,2);LapCase(build,lap,1,0,1,2,1);LapCase(build,lap,60,0,1,2,60);LapCase(build,lap,61,0,1,2,2);LapCase(build,lap,-1,0,1,2,2);LapCase(build,lap,3,1,1,2,2);LapCase(build,lap,3,0,0,2,2);LapCase(build,lap,0,0,1,7,7);
            Console.WriteLine("PASS isolated native lap execution: default, 1, 60, invalid range, mode/private gates, original register/tail effects.");
            BotRaceCase(catalog);
            BotRaceAloneCase(catalog, hook, wallet);
            RosterCase(hook, wallet);
            LiveRosterCase(hook, wallet, lap);
            RoomCapacityCase(catalog, hook, lap);
            InternetBindCase(catalog, hook, lap);
            Console.WriteLine("PASS "+checks+" assertions. This does not validate the game UI, network peers, or races.");return 0;
        } catch(Exception e){Console.Error.WriteLine("FAIL "+e);return 1;}
        finally{if(stateField!=null)stateField.SetValue(null,saved);if(lap!=IntPtr.Zero)VirtualFree(lap,UIntPtr.Zero,0x8000);if(wallet!=IntPtr.Zero)VirtualFree(wallet,UIntPtr.Zero,0x8000);}
    }
}
