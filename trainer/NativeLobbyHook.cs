using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

// Shares the existing game-thread wallet dispatcher instead of patching the
// same native update entry twice. The native UI uses the game's GIN renderer.
internal static class NativeLobbyHook
{
    internal const int AllocationSize = 0x40000;
    internal const int ImageOffset = 0x2000;
    internal const int StateOffset = 0x38000;
    // Separate from the shared UI/network ABI. Both host bind hooks read
    // this word, so a partial installation uses the stock port arguments.
    internal const int InternetHostGateOffset = 0x1000;
    internal static long StateAddress;
    private static readonly byte[] ImageFile = LoadImage();

    internal static int ExportRva(string name)
    {
        int pe=BitConverter.ToInt32(ImageFile,0x3c),h=pe+24,size=BitConverter.ToInt32(ImageFile,h+56);
        if(size<=0||size>StateOffset-ImageOffset)throw new InvalidDataException("Invalid native export image size.");
        byte[] mapped=new byte[size];int table=h+BitConverter.ToUInt16(ImageFile,pe+20);
        for(int i=0;i<BitConverter.ToUInt16(ImageFile,pe+6);i++){
            int section=table+i*40,rva=BitConverter.ToInt32(ImageFile,section+12),n=BitConverter.ToInt32(ImageFile,section+16),raw=BitConverter.ToInt32(ImageFile,section+20);
            if(rva<0||n<0||rva>size-n||raw<0||raw>ImageFile.Length-n)throw new InvalidDataException("Invalid export image section.");
            if(n!=0)Buffer.BlockCopy(ImageFile,raw,mapped,rva,n);
        }
        int directory=BitConverter.ToInt32(ImageFile,h+96),directorySize=BitConverter.ToInt32(ImageFile,h+100);
        if(directory<=0||directorySize<40||directory>size-directorySize)throw new InvalidDataException("Missing native exports.");
        int functions=BitConverter.ToInt32(mapped,directory+20),names=BitConverter.ToInt32(mapped,directory+24);
        int addresses=BitConverter.ToInt32(mapped,directory+28),strings=BitConverter.ToInt32(mapped,directory+32),ordinals=BitConverter.ToInt32(mapped,directory+36);
        if(functions<1||names<1||functions>1024||names>1024||addresses<0||addresses>size-functions*4||strings<0||strings>size-names*4||ordinals<0||ordinals>size-names*2)throw new InvalidDataException("Invalid native export tables.");
        for(int i=0;i<names;i++){
            int p=BitConverter.ToInt32(mapped,strings+i*4),end=p;
            if(p<0||p>=size)throw new InvalidDataException("Invalid native export name.");
            while(end<size&&end-p<128&&mapped[end]!=0)end++;
            if(end>=size||end-p==128)throw new InvalidDataException("Unterminated native export name.");
            string found=System.Text.Encoding.ASCII.GetString(mapped,p,end-p);
            if(found!=name&&found!="_"+name)continue;
            int ordinal=BitConverter.ToUInt16(mapped,ordinals+i*2);if(ordinal>=functions)throw new InvalidDataException("Invalid native export ordinal.");
            int target=BitConverter.ToInt32(mapped,addresses+ordinal*4);
            if(target<=0||target>=size||(target>=directory&&target<directory+directorySize))throw new InvalidDataException("Invalid or forwarded native export.");
            return target;
        }
        throw new InvalidDataException("Native export not found: "+name);
    }

    internal static byte[] BuildDiscovery(long module,long cave)
    {
        if(StateAddress==0)throw new InvalidOperationException("Native lobby state is not installed.");
        long target=StateAddress-StateOffset+ImageOffset+ExportRva("NativeLanDispatch");
        var code=new List<byte>();
        code.AddRange(InjectionHack.Hex("9C 60 8B DC 81 EC 10 02 00 00 83 E4 F0 0F AE 04 24"));
        code.Add(0x68);code.AddRange(BitConverter.GetBytes(unchecked((int)StateAddress)));
        code.AddRange(InjectionHack.Hex("FF 73 04")); // saved ESI: the real LocalLobbyFinder
        code.Add(0x68);code.AddRange(BitConverter.GetBytes(unchecked((int)module)));
        code.Add(0xe8);code.AddRange(BitConverter.GetBytes(unchecked((int)(target-(cave+code.Count+4)))));
        code.AddRange(InjectionHack.Hex("83 C4 0C 0F AE 0C 24 8B E3 61 9D"));
        code.Add(0xe9);code.AddRange(BitConverter.GetBytes(unchecked((int)(module+0x110CE7D-(cave+code.Count+4)))));
        return code.ToArray();
    }

    internal static byte[] BuildSendProtectedProfile(long module,long cave)
    {
        // Original CALL has the profile argument at [esp], the connection in
        // ECX and the LocalLobby in EDI. The original call pops one argument.
        var code=new List<byte>(InjectionHack.Hex("FF 34 24 51 57"));
        EmitPasswordCall(code,module,cave,"NativeSendProtectedProfile");
        code.AddRange(InjectionHack.Hex("83 C4 18"));
        return EndInternetBind(code,module+0x110E07B,cave);
    }

    internal static byte[] BuildAuthorizeProfile(long module,long cave)
    {
        // State-3 admission path before either accepting a retransmission or
        // comparing the incoming identity to potentially replace a real user.
        var code=new List<byte>(InjectionHack.Hex("9C 60 56 53 57"));
        EmitPasswordCall(code,module,cave,"NativeAuthorizeProfile");
        code.AddRange(InjectionHack.Hex("83 C4 14 85 C0 75 07 61 9D"));
        EndInternetBind(code,module+0x110E922,cave);
        code.AddRange(InjectionHack.Hex("61 9D 83 BB 34 01 00 00 00"));
        return EndInternetBind(code,module+0x110E95B,cave);
    }

    private static void EmitPasswordCall(List<byte> code,long module,long cave,string name)
    {
        if(StateAddress==0)throw new InvalidOperationException("Native lobby state is not installed.");
        long target=StateAddress-StateOffset+ImageOffset+ExportRva(name);
        code.Add(0x68);code.AddRange(BitConverter.GetBytes(unchecked((int)StateAddress)));
        code.Add(0x68);code.AddRange(BitConverter.GetBytes(unchecked((int)module)));
        code.Add(0xe8);code.AddRange(BitConverter.GetBytes(unchecked((int)(target-(cave+code.Count+4)))));
    }

    internal static byte[] BuildCaptureRaceBoard(long module,long cave)
    {
        if(StateAddress==0)throw new InvalidOperationException("Native lobby state is not installed.");
        long capture=StateAddress-StateOffset+ImageOffset+ExportRva("NativeCaptureRaceBoard");
        var code=new List<byte>(InjectionHack.Hex("51 E8"));
        int originalCall=code.Count;
        code.AddRange(new byte[4]);
        // Sort returns with the saved this pointer still on the stack.
        // FXSAVE keeps the original caller's x87/SSE state across observation.
        code.AddRange(InjectionHack.Hex("9C 60 8B DC 81 EC 10 02 00 00 83 E4 F0 0F AE 04 24 FF 73 24 68"));
        code.AddRange(BitConverter.GetBytes(unchecked((int)StateAddress)));
        code.Add(0x68);code.AddRange(BitConverter.GetBytes(unchecked((int)module)));
        code.Add(0xe8);code.AddRange(BitConverter.GetBytes(unchecked((int)(capture-(cave+code.Count+4)))));
        code.AddRange(InjectionHack.Hex("83 C4 0C 0F AE 0C 24 8B E3 61 9D 8D 64 24 04 C3"));
        byte[] rel=BitConverter.GetBytes(code.Count-originalCall-4);
        for(int i=0;i<rel.Length;i++)code[originalCall+i]=rel[i];
        code.AddRange(InjectionHack.Hex("8B 51 1C 8B 49 18"));
        return EndInternetBind(code,module+0x5C2386,cave);
    }

    private static List<byte> BeginRaceHud(long module,long cave)
    {
        if(StateAddress==0)throw new InvalidOperationException("Native race HUD state is not installed.");
        var code=new List<byte>(InjectionHack.Hex("9C 60 8B DC 81 EC 10 02 00 00 83 E4 F0 0F AE 04 24 68"));
        code.AddRange(BitConverter.GetBytes(unchecked((int)StateAddress)));
        code.Add(0x68);code.AddRange(BitConverter.GetBytes(unchecked((int)module)));
        long text=StateAddress-StateOffset+ImageOffset+ExportRva("NativeRaceText");
        code.Add(0xe8);code.AddRange(BitConverter.GetBytes(unchecked((int)(text-(cave+code.Count+4)))));
        code.AddRange(InjectionHack.Hex("83 C4 08 85 C0"));
        return code;
    }

    internal static byte[] BuildRaceHudAvailability(long module,long cave)
    {
        var code=BeginRaceHud(module,cave);
        code.AddRange(InjectionHack.Hex("74 10 0F AE 0C 24 8B E3 61 9D 8A 40 0C"));
        EndInternetBind(code,module+0x9AA13C,cave);
        code.AddRange(InjectionHack.Hex("0F AE 0C 24 8B E3 61 9D 8A 40 0C 84 C0"));
        return EndInternetBind(code,module+0x9AA13A,cave);
    }

    internal static byte[] BuildRaceHudText(long module,long cave)
    {
        var code=BeginRaceHud(module,cave);
        code.AddRange(InjectionHack.Hex("74 0A 89 43 2C C7 43 28"));
        int formatPointer=code.Count;
        code.AddRange(new byte[4]);
        code.AddRange(InjectionHack.Hex("0F AE 0C 24 8B E3 61 9D"));
        code.Add(0xe8);code.AddRange(BitConverter.GetBytes(unchecked((int)(module+0xEA4DE0-(cave+code.Count+4)))));
        EndInternetBind(code,module+0x9AB2B9,cave);
        byte[] pointer=BitConverter.GetBytes(unchecked((int)(cave+code.Count)));
        for(int i=0;i<pointer.Length;i++)code[formatPointer+i]=pointer[i];
        code.AddRange(new byte[]{(byte)'%',(byte)'s',0});
        return code.ToArray();
    }

    internal static byte[] BuildRaceHudPosition(long module,long cave)
    {
        var code=BeginRaceHud(module,cave);
        // The native text is anchored above its baseline. Lower that
        // baseline by a font-relative distance for the multiline table.
        code.AddRange(InjectionHack.Hex("74 1D 0F AE 0C 24 8B E3 61 9D F3 0F 59 05"));
        int relativeScale=code.Count;
        code.AddRange(new byte[4]);
        code.AddRange(InjectionHack.Hex("F3 0F 11 85 F8 FE FF FF"));
        EndInternetBind(code,module+0x9AB563,cave);
        code.AddRange(InjectionHack.Hex("0F AE 0C 24 8B E3 61 9D F3 0F 11 85 F8 FE FF FF"));
        EndInternetBind(code,module+0x9AB563,cave);
        byte[] pointer=BitConverter.GetBytes(unchecked((int)(cave+code.Count)));
        for(int i=0;i<pointer.Length;i++)code[relativeScale+i]=pointer[i];
        code.AddRange(BitConverter.GetBytes(8.0f));
        return code.ToArray();
    }

    internal static byte[] BuildInternetLobbyBind(long module, long cave)
    {
        var code = InternetBindPrefix();
        code.AddRange(InjectionHack.Hex("75 08 9D 68"));
        code.AddRange(BitConverter.GetBytes(NativeNetworkState.InternetLobbyPort));
        code.AddRange(InjectionHack.Hex("EB 03 9D 6A 00 89 98 48 01 00 00"));
        return EndInternetBind(code, module + 0x110D90A, cave);
    }

    internal static byte[] BuildInternetRaceBind(long module, long cave)
    {
        var code = InternetBindPrefix();
        code.AddRange(InjectionHack.Hex("74 07 9D 66 89 45 DC EB 07 9D 66 C7 45 DC"));
        code.AddRange(BitConverter.GetBytes((ushort)NativeNetworkState.InternetRacePort));
        code.AddRange(InjectionHack.Hex("8B 85 50 FF FF FF"));
        return EndInternetBind(code, module + 0x96C6DA, cave);
    }

    private static List<byte> InternetBindPrefix()
    {
        if (StateAddress == 0) throw new InvalidOperationException("Native lobby state is not installed.");
        var code = new List<byte>(InjectionHack.Hex("9C 83 3D"));
        code.AddRange(BitConverter.GetBytes(unchecked((int)(StateAddress + InternetHostGateOffset))));
        code.Add(1);
        return code;
    }

    private static byte[] EndInternetBind(List<byte> code, long continuation, long cave)
    {
        code.Add(0xe9);
        code.AddRange(BitConverter.GetBytes(unchecked((int)(continuation - (cave + code.Count + 4)))));
        return code.ToArray();
    }

    internal static byte[] BuildBotRaceAlone(long module, long cave)
    {
        if (StateAddress == 0) throw new InvalidOperationException("Native lobby state is not installed.");
        long predicate = StateAddress - StateOffset + ImageOffset + ExportRva("NativeSoloBotRace");
        var code = new List<byte>(InjectionHack.Hex("9C 60 68"));
        code.AddRange(BitConverter.GetBytes(unchecked((int)module)));
        Action<byte, long> branch = (opcode, target) =>
        {
            code.Add(opcode);
            code.AddRange(BitConverter.GetBytes(unchecked((int)(target - (cave + code.Count + 4)))));
        };
        branch(0xe8, predicate);
        code.AddRange(InjectionHack.Hex("83 C4 04 85 C0 74 07 61 9D"));
        branch(0xe9, module + 0xD555EF); // Reset the original alone timer.
        code.AddRange(InjectionHack.Hex("61 9D 8B CF")); // Restore state and replay displaced code.
        branch(0xe8, module + 0x83CC20);
        branch(0xe9, module + 0xD5559F);
        return code.ToArray();
    }

    private static byte[] LoadImage()
    {
        using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("NativeLobbyUi.x86"))
        {
            if (resource == null) throw new InvalidDataException("Native lobby UI resource is missing.");
            using (var data = new MemoryStream()) { resource.CopyTo(data); return data.ToArray(); }
        }
    }

    internal static void EmitTick(List<byte> code, long module, long cave)
    {
        int header = BitConverter.ToInt32(ImageFile, 0x3c) + 24;
        int entry = BitConverter.ToInt32(ImageFile, header + 16);
        code.AddRange(InjectionHack.Hex("9C 60 8B DC 81 EC 10 02 00 00 83 E4 F0 0F AE 04 24"));
        code.Add(0x68); code.AddRange(BitConverter.GetBytes(unchecked((int)(cave + StateOffset))));
        code.AddRange(InjectionHack.Hex("FF 73 18")); // ECX saved by PUSHAD: native screen
        code.Add(0x68); code.AddRange(BitConverter.GetBytes(unchecked((int)module)));
        code.Add(0xe8);
        code.AddRange(BitConverter.GetBytes(unchecked((int)(ImageOffset + entry - code.Count - 4))));
        code.AddRange(InjectionHack.Hex("83 C4 0C 0F AE 0C 24 8B E3 61 9D"));
    }

    internal static byte[] InstallImage(byte[] wallet, long cave)
    {
        StateAddress = cave + StateOffset;
        var result = new byte[AllocationSize];
        Buffer.BlockCopy(wallet, 0, result, 0, wallet.Length);
        int pe = BitConverter.ToInt32(ImageFile, 0x3c), header = pe + 24;
        int sectionCount = BitConverter.ToUInt16(ImageFile, pe + 6);
        int table = header + BitConverter.ToUInt16(ImageFile, pe + 20);
        int size = BitConverter.ToInt32(ImageFile, header + 56);
        if (size <= 0 || size > StateOffset - ImageOffset) throw new InvalidDataException("Native UI image exceeds its allocation.");
        foreach (int index in Sequence(sectionCount))
        {
            int section = table + index * 40;
            int rva = BitConverter.ToInt32(ImageFile, section + 12);
            int bytes = BitConverter.ToInt32(ImageFile, section + 16);
            int raw = BitConverter.ToInt32(ImageFile, section + 20);
            if (bytes == 0) continue;
            if (rva < 0 || rva > size - bytes || raw < 0 || raw > ImageFile.Length - bytes)
                throw new InvalidDataException("Invalid native UI section.");
            Buffer.BlockCopy(ImageFile, raw, result, ImageOffset + rva, bytes);
        }
        int relocation = BitConverter.ToInt32(ImageFile, header + 96 + 5 * 8);
        int remaining = BitConverter.ToInt32(ImageFile, header + 100 + 5 * 8);
        long delta = cave + ImageOffset - BitConverter.ToUInt32(ImageFile, header + 28);
        while (remaining > 0)
        {
            if (remaining < 8 || relocation < 0 || relocation > size - 8) throw new InvalidDataException("Invalid UI relocation block.");
            int page = BitConverter.ToInt32(result, ImageOffset + relocation);
            int blockSize = BitConverter.ToInt32(result, ImageOffset + relocation + 4);
            if (blockSize < 8 || blockSize > remaining || (blockSize & 1) != 0 || relocation > size - blockSize)
                throw new InvalidDataException("Invalid UI relocation size.");
            for (int offset = 8; offset < blockSize; offset += 2)
            {
                int item = BitConverter.ToUInt16(result, ImageOffset + relocation + offset);
                if ((item >> 12) == 0) continue;
                int target = page + (item & 4095);
                if ((item >> 12) != 3 || target < 0 || target > size - 4) throw new InvalidDataException("Unsupported UI relocation.");
                int value = unchecked((int)(BitConverter.ToInt32(result, ImageOffset + target) + delta));
                Buffer.BlockCopy(BitConverter.GetBytes(value), 0, result, ImageOffset + target, 4);
            }
            relocation += blockSize; remaining -= blockSize;
        }
        return result;
    }

    private static IEnumerable<int> Sequence(int count) { for (int index = 0; index < count; index++) yield return index; }
}
