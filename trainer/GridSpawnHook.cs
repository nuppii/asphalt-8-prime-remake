using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

internal static class GridSpawnHook
{
    internal const int AllocationSize = 0x9000;
    internal const string Name = "Racer Runtime Entries";
    private const int ImageOffset = 0x1000;
    private const int StateOffset = 0x6400;
    // Keep settings separate from the GridState written by PrepareGrid.
    internal const int LapsOffset = 0x6600;
    internal const int RacersOffset = 0x6604;
    internal const int LapsRestoreOffset = 0x6610;
    internal const int RacersRestoreOffset = 0x6620;
    private const int GeneratedMarkersOffset = 0x7000;

    internal static byte[] Build(long module, long cave)
    {
        byte[] code = new byte[GeneratedMarkersOffset];
        int entry = CopyNativeImage(code, cave + ImageOffset);
        byte[] seed = new byte[4];
        using (var random = RandomNumberGenerator.Create())
            do { random.GetBytes(seed); } while (BitConverter.ToUInt32(seed, 0) == 0);
        Buffer.BlockCopy(seed, 0, code, StateOffset, seed.Length);

        var wrapper = new List<byte>();
        Action<long> integer = value => wrapper.AddRange(BitConverter.GetBytes(unchecked((int)value)));
        Action<long> push = value => { wrapper.Add(0x68); integer(value); };
        wrapper.AddRange(InjectionHack.Hex("9C 60")); // preserve the surrounding native frame
        push(module + 0x1D36938); // verified grid marker vtable
        push(cave + StateOffset);
        push(cave + GeneratedMarkersOffset);
        wrapper.AddRange(InjectionHack.Hex("FF 75 C0 FF 75 BC")); // vector end, begin
        wrapper.Add(0xE8);
        integer(entry - (wrapper.Count + 4));
        wrapper.AddRange(InjectionHack.Hex("83 C4 14"));
        RestoreRaceSetting(wrapper, module, cave + LapsRestoreOffset, 0x18);
        RestoreRaceSetting(wrapper, module, cave + RacersRestoreOffset, 0x2C);
        wrapper.AddRange(InjectionHack.Hex("61 9D 8B 55 C0 8B 75 BC"));
        wrapper.Add(0xE9);
        integer(module + 0x2BFAF8 - (cave + wrapper.Count + 4));
        wrapper.CopyTo(code);
        return code;
    }

    internal static void RestoreRaceSetting(List<byte> code, long module, long record, byte field)
    {
        var address = BitConverter.GetBytes(unchecked((int)record));
        code.Add(0xB9); // mov ecx, record
        code.AddRange(address);
        string displacement = field.ToString("X2");
        code.AddRange(InjectionHack.Hex(
            "8B 01 85 C0 74 14 " +                              // no event to restore
            "8B 51 08 39 50 " + displacement + " 75 06 " +       // preserve intervening game writes
            "8B 51 04 89 50 " + displacement + " " +
            "C7 01 00 00 00 00"));
    }

    private static int CopyNativeImage(byte[] code, long loadAddress)
    {
        byte[] file;
        using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("GridSpawn.x86"))
        {
            if (resource == null) throw new InvalidDataException("Native grid resource is missing");
            using (var data = new MemoryStream())
            {
                resource.CopyTo(data);
                file = data.ToArray();
            }
        }

        int pe = BitConverter.ToInt32(file, 0x3C);
        int header = pe + 24;
        int sections = BitConverter.ToUInt16(file, pe + 6);
        int table = header + BitConverter.ToUInt16(file, pe + 20);
        int entry = BitConverter.ToInt32(file, header + 16);
        int imageBase = BitConverter.ToInt32(file, header + 28);
        int relocations = BitConverter.ToInt32(file, header + 96 + 5 * 8);
        int relocationSize = BitConverter.ToInt32(file, header + 100 + 5 * 8);
        for (int i = 0; i < sections; i++)
        {
            int section = table + i * 40;
            int target = ImageOffset + BitConverter.ToInt32(file, section + 12);
            int length = BitConverter.ToInt32(file, section + 16);
            int source = BitConverter.ToInt32(file, section + 20);
            if (target < ImageOffset || target + length > StateOffset)
                throw new InvalidDataException("Native grid image exceeds allocation");
            Buffer.BlockCopy(file, source, code, target, length);
        }

        long delta = loadAddress - imageBase;
        int finish = ImageOffset + relocations + relocationSize;
        for (int block = ImageOffset + relocations; block < finish;)
        {
            int page = ImageOffset + BitConverter.ToInt32(code, block);
            int size = BitConverter.ToInt32(code, block + 4);
            if (size < 8 || size > finish - block) throw new InvalidDataException("Invalid grid relocation");
            for (int offset = block + 8; offset < block + size; offset += 2)
            {
                int address = BitConverter.ToUInt16(code, offset);
                int type = address >> 12;
                if (type == 0) continue;
                if (type != 3) throw new InvalidDataException("Unsupported grid relocation");
                int target = page + (address & 4095);
                int value = unchecked((int)(BitConverter.ToInt32(code, target) + delta));
                Buffer.BlockCopy(BitConverter.GetBytes(value), 0, code, target, 4);
            }
            block += size;
        }
        return ImageOffset + entry;
    }
}
