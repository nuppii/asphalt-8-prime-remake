using System;

// Asphalt 8 Windows 4.8.0.7 only. Reuses GS_Play's native navigation frame,
// context ownership and cleanup, selecting the existing Local Wi-Fi screen.
internal static class NativeMultiplayer
{
    internal static byte[] BuildEntry(long module, long cave)
    {
        const string template = "558bec83e4f86aff6848c3a70164a1000000005083ec2ca160010f0233c4894424245657a160010f0233c4508d44243864a3000000008bf18b0d58aa24028d4424106a005068dccf2402c744241c00000000c744242000000000c744242400000000e8c9266f0083c67cc744240c25000000568d4c2420e8043293ffc7442440000000008d4c241c8b460c894424288d44240c50c744243001000000e84f579dff8d54241cc7442440010000008d4c2410e8ca2d02008bf0c64424400283ec088b0e8bd489642414890ac74204000000008b4e048b4204894604894a04c70600000000c644244803c644244802e85e7d86ff83c408c6442440018b74241485f6742b83cfff8d4e048bc7f00fc10183f801751a8b068bceff50048d4608f00fc13883ff0175078b068bceff5008c7442440ffffffff8b74241c85f67432803da3ae24020074078bcee8d398650056ff155c4ac80183c404c744241c00000000c744242000000000c7442424000000008b4c243864890d00000000595f5e8b4c242433cce888bfdb008be55dc3";
        var code = new byte[template.Length / 2];
        for (int i = 0; i < code.Length; i++) code[i] = Convert.ToByte(template.Substring(i * 2, 2), 16);
        // HIGHLOW entries from the original image relocation directory.
        foreach (int offset in new[] { 9, 24, 37, 58, 70, 319, 336 })
            Write(code, offset, unchecked(BitConverter.ToInt32(code, offset) + (int)(module - 0x400000)));
        // External relative calls must target the loaded image, not the clone.
        Write(code, 99, unchecked((int)(module + 0xF3ED90 - (cave + 103))));
        Write(code, 120, unchecked((int)(module + 0x17F8E0 - (cave + 124))));
        Write(code, 157, unchecked((int)(module + 0x221E50 - (cave + 161))));
        Write(code, 178, unchecked((int)(module + 0x8DB7D0 - (cave + 182))));
        Write(code, 238, unchecked((int)(module + 0xB44B0 - (cave + 242))));
        Write(code, 329, unchecked((int)(module + 0xEA6080 - (cave + 333))));
        Write(code, 388, unchecked((int)(module + 0x1608770 - (cave + 392))));
        return code;
    }

    private static void Write(byte[] code, int offset, int value)
    {
        Array.Copy(BitConverter.GetBytes(value), 0, code, offset, 4);
    }
}
