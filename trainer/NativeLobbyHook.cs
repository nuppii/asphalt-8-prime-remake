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
    internal static long StateAddress;
    private static readonly byte[] ImageFile = LoadImage();

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
