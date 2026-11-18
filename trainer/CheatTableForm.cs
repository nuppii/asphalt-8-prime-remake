using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class CheatTableForm : Form
{
    internal const string UnlockCareer = "Unlock all career seasons and Moto Blitz";
    private readonly FlowLayoutPanel list;
    private readonly Label status;
    private readonly Dictionary<string, bool> states = new Dictionary<string, bool>();

    internal static readonly DirectHack[] DirectHacks =
    {
        new DirectHack("Try All Vehicles", "GARAGE", new PatchFragment(0x32D756, "74", "75")),
        new DirectHack("Visible All Vehicles", "GARAGE",
            new PatchFragment(0x2573B9, "80 78", "C6 40"),
            new PatchFragment(0x2573BC, "00", "01"),
            new PatchFragment(0x2573C3, "80 78", "C6 40"),
            new PatchFragment(0x2573C6, "00", "01"),
            new PatchFragment(0x2573DA, "03", "04")),
        new DirectHack("Classic Finish", "RACE FINISH", new PatchFragment(0xB44C6B, "76 10", "90 90")),
        new DirectHack("Elimination Finish", "RACE FINISH",
            new PatchFragment(0x902256, "74 0E", "90 90"),
            new PatchFragment(0x902277, "74 0E", "90 90")),
        new DirectHack("Infected Finish", "RACE FINISH", new PatchFragment(0xA81D4E, "76 10", "90 90")),
        new DirectHack("Drift Finish", "RACE FINISH", new PatchFragment(0x887637, "0F 85 5A 04 00 00", "90 90 90 90 90 90")),
        new DirectHack("Ultimate AI Finish", "RACE FINISH", new PatchFragment(0xD29501, "76 3F", "90 90")),
        new DirectHack(UnlockCareer, "PROGRESSION",
            // Substitute the season threshold and bypass the per-race star comparison.
            new PatchFragment(0x82A63E, "8B 44 C8 04", "31 C0 90 90"),
            new PatchFragment(0xA16309, "73 08", "EB 08"),
            // Accept a populated McLaren Legends season without the season-wide ownership gate.
            new PatchFragment(0xA168FE, "8B FF", "EB 3D"),
            // Open the race details so the garage can apply its own vehicle rules.
            new PatchFragment(0x891C4F, "0F 95 C1", "B1 01 90"),
            // The Motoblitz menu, its callbacks and related race screens compare the same threshold.
            MotoStarGate(0x4B6F35), MotoStarGate(0x596A58), MotoStarGate(0x596D3B),
            MotoStarGate(0x6058F9), MotoStarGate(0x84CB05), MotoStarGate(0x9BFE4C),
            MotoStarGate(0x9C3EBA), MotoStarGate(0x9D6B16), MotoStarGate(0xAB04F8),
            MotoStarGate(0xB0F378), MotoStarGate(0xB71B33), MotoStarGate(0xB72544),
            // Season and race progression gates use motorcycle points rather than car stars.
            new PatchFragment(0xB3AD04, "0F 83 9A 00 00 00", "90 E9 9A 00 00 00"),
            new PatchFragment(0xB3ADE1, "0F 83 86 00 00 00", "90 E9 86 00 00 00"),
            new PatchFragment(0xB3B7B3, "0F 82 82 00 00 00", "90 90 90 90 90 90")),
        new DirectHack("Unlimited Mastery Rewards", "PROGRESSION",
            new PatchFragment(0xCD60DD, "75 12", "90 90"),
            new PatchFragment(0xCD60E6, "66 C7 46 04 00 00", "66 C7 46 04 01 00")),
        new DirectHack("Perfect Nitro", "RACING", new PatchFragment(0x490C88, "76 15", "90 90")),
        new DirectHack("Nitro Refill", "RACING", new PatchFragment(0x49176B, "C7 45 FC 00 00 00 00", "C7 45 FC 00 00 48 43")),
        new DirectHack("Christmas Tracks", "DISPLAY", new PatchFragment(0x1D9812, "74 43", "90 90")),
        new DirectHack("Bubbles Effects", "DISPLAY", new PatchFragment(0xF327A3, "7D", "EB")),
        new DirectHack("Change Cars Textures", "DISPLAY",
            new PatchFragment(0xED26AD, "8B 46 20", "90 90 90"),
            new PatchFragment(0xED26B4, "8B 46 24", "90 90 90"),
            new PatchFragment(0xED26BA, "8B 46 28", "90 90 90")),
        new DirectHack("Perfect Nitro 1 Click", "RACING",
            new PatchFragment(0x491394, "FF 46 04 8B 4E 04", "83 46 04 03 90 90"),
            new PatchFragment(0x4913DF, "80 7E 39 00 74 0A", "C6 46 39 01 90 90")),
        new DirectHack("Safe Crash", "RACING", new PatchFragment(0x554069, "74", "75"))
    };

    private static PatchFragment MotoStarGate(int rva)
    {
        return new PatchFragment(rva, "3B 90 E4 01 00 00", "83 FA 00 90 90 90");
    }

    internal static DirectHack Find(string name)
    {
        foreach (DirectHack hack in DirectHacks)
            if (hack.Name == name) return hack;
        return null;
    }

    public CheatTableForm()
    {
        Text = "Cheat Engine 4.8.0h — native port";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(860, 650);
        BackColor = Color.FromArgb(21, 25, 34);
        ForeColor = Color.White;
        TopMost = true;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 64,
            Padding = new Padding(22, 15, 0, 0),
            Text = "CE TABLE 4.8.0H  •  DIRECT PATCHES",
            Font = new Font("Segoe UI Semibold", 17f),
            ForeColor = Color.White
        };
        Controls.Add(title);

        status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(20, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "13 ported modules • signatures checked before every write",
            ForeColor = Color.FromArgb(0, 203, 255),
            Font = new Font("Segoe UI Semibold", 9.5f)
        };
        Controls.Add(status);

        list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(18, 10, 18, 10),
            BackColor = Color.FromArgb(21, 25, 34)
        };
        Controls.Add(list);
        list.BringToFront();

        string lastCategory = null;
        foreach (DirectHack hack in DirectHacks)
        {
            if (lastCategory != hack.Category)
            {
                lastCategory = hack.Category;
                list.Controls.Add(new Label
                {
                    Text = lastCategory,
                    Width = 790,
                    Height = 30,
                    ForeColor = Color.FromArgb(145, 158, 178),
                    Font = new Font("Segoe UI Semibold", 9f),
                    Padding = new Padding(4, 7, 0, 0)
                });
            }
            list.Controls.Add(MakeRow(hack));
        }
    }

    private Control MakeRow(DirectHack hack)
    {
        var row = new Panel
        {
            Width = 790,
            Height = 58,
            Margin = new Padding(0, 0, 0, 7),
            BackColor = Color.FromArgb(30, 36, 48)
        };
        row.Controls.Add(new Label
        {
            Text = hack.Name,
            Location = new Point(16, 8),
            Size = new Size(510, 24),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 10f)
        });
        row.Controls.Add(new Label
        {
            Text = "ORIGINAL TABLE  •  BUILD 4.8.0.7 SIGNATURE",
            Location = new Point(16, 33),
            AutoSize = true,
            ForeColor = Color.FromArgb(84, 222, 150),
            Font = new Font("Segoe UI Semibold", 7.5f)
        });
        var button = new Button
        {
            Text = "ENABLE",
            Location = new Point(624, 11),
            Size = new Size(145, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 160),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 8.5f),
            TabStop = false
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(0, 203, 255);
        button.Click += (_, __) =>
        {
            bool enable = !states.ContainsKey(hack.Name) || !states[hack.Name];
            string error;
            if (NativePatchEngine.Toggle(hack, enable, out error))
            {
                states[hack.Name] = enable;
                button.Text = enable ? "DISABLE" : "ENABLE";
                button.BackColor = enable ? Color.FromArgb(22, 145, 92) : Color.FromArgb(0, 122, 160);
                status.Text = hack.Name + (enable ? " enabled" : " disabled");
            }
            else
            {
                status.Text = hack.Name + " : " + error;
            }
        };
        row.Controls.Add(button);
        return row;
    }
}

internal sealed class DirectHack
{
    public readonly string Name;
    public readonly string Category;
    public readonly PatchFragment[] Fragments;

    public DirectHack(string name, string category, params PatchFragment[] fragments)
    {
        Name = name;
        Category = category;
        Fragments = fragments;
    }
}

internal sealed class PatchFragment
{
    public readonly int Rva;
    public readonly byte[] Original;
    public readonly byte[] Patched;

    public PatchFragment(int rva, string original, string patched)
    {
        Rva = rva;
        Original = Parse(original);
        Patched = Parse(patched);
    }

    private static byte[] Parse(string value)
    {
        string[] parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        byte[] bytes = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            bytes[i] = Convert.ToByte(parts[i], 16);
        return bytes;
    }
}

internal static class NativePatchEngine
{
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint PageExecuteReadWrite = 0x40;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtectEx(IntPtr process, IntPtr address, UIntPtr size, uint protection, out uint oldProtection);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static bool Toggle(DirectHack hack, bool enable, out string error)
    {
        error = "";
        Process[] games = Process.GetProcessesByName("Asphalt8");
        if (games.Length == 0) { error = "game not found"; return false; }
        Process game = games[0];
        IntPtr process = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite, false, game.Id);
        if (process == IntPtr.Zero) { error = "OpenProcess " + Marshal.GetLastWin32Error(); return false; }
        try
        {
            foreach (PatchFragment fragment in hack.Fragments)
            {
                IntPtr address = IntPtr.Add(game.MainModule.BaseAddress, fragment.Rva);
                byte[] expected = enable ? fragment.Original : fragment.Patched;
                byte[] replacement = enable ? fragment.Patched : fragment.Original;
                byte[] current = new byte[expected.Length];
                IntPtr count;
                if (!ReadProcessMemory(process, address, current, current.Length, out count) || count.ToInt64() != current.Length)
                { error = "cannot read memory"; return false; }
                if (Equal(current, replacement)) continue;
                if (!Equal(current, expected)) { error = "unexpected signature at +" + fragment.Rva.ToString("X"); return false; }
            }
            foreach (PatchFragment fragment in hack.Fragments)
            {
                IntPtr address = IntPtr.Add(game.MainModule.BaseAddress, fragment.Rva);
                byte[] replacement = enable ? fragment.Patched : fragment.Original;
                uint oldProtection;
                if (!VirtualProtectEx(process, address, (UIntPtr)replacement.Length, PageExecuteReadWrite, out oldProtection))
                { error = "VirtualProtectEx " + Marshal.GetLastWin32Error(); return false; }
                IntPtr written;
                bool ok = WriteProcessMemory(process, address, replacement, replacement.Length, out written)
                    && written.ToInt64() == replacement.Length;
                uint ignored;
                VirtualProtectEx(process, address, (UIntPtr)replacement.Length, oldProtection, out ignored);
                if (!ok) { error = "cannot write memory"; return false; }
            }
            return true;
        }
        finally { CloseHandle(process); }
    }

    private static bool Equal(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }
}

internal sealed class InjectionHack
{
    public readonly string Name;
    public readonly int Rva;
    public readonly byte[] Original;
    public readonly bool[] OriginalMask;
    public readonly Func<long, long, byte[]> BuildCode;
    public readonly int AllocationSize;

    public InjectionHack(string name, int rva, string original, Func<long, long, byte[]> buildCode, int allocationSize = 4096)
    {
        Name = name;
        Rva = rva;
        string[] parts = original.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        Original = new byte[parts.Length]; OriginalMask = new bool[parts.Length];
        for (int i=0;i<parts.Length;i++)
        {
            OriginalMask[i]=parts[i]!="??";
            Original[i]=OriginalMask[i]?Convert.ToByte(parts[i],16):(byte)0;
        }
        BuildCode = buildCode;
        AllocationSize = allocationSize;
    }

    internal static byte[] Hex(string text)
    {
        string[] parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        byte[] bytes = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++) bytes[i] = Convert.ToByte(parts[i], 16);
        return bytes;
    }
}

internal static class InjectionCatalog
{
    internal const int DriverNameStateOffset = 0x200;
    internal const int DriverNameTextOffset = 0x204;
    internal const int WalletCommandOffset = 0x900;
    internal const int SetFusionBalanceCommand = 7;
    internal const string OfflineProUpgrade = "Offline PRO upgrades";
    internal const string CareerCarChoice = "Ignore class and required car";
    internal const string CareerLiveRefresh = "Career Live Refresh";
    internal const int CareerLiveStateOffset = 0x800;
    internal const int UnlimitedBoosterMaskOffset = 0x300;
    internal const string MultiplayerEntryName = "Offline Multiplayer Entry";
    internal static readonly InjectionHack MultiplayerEntry = new InjectionHack(MultiplayerEntryName,
        0x84C7F0, "55 8B EC 83 E4 F8", (module,cave) =>
            Raw("C7 05 " + I32(unchecked((int)(cave + 0x100))) + " 01 00 00 00 C3"));
    // Status 4 selects the native information card and enables its real button.
    // The click callback above owns navigation; no legacy server is contacted.
    internal static readonly InjectionHack MultiplayerTile = new InjectionHack("Offline Multiplayer Tile",
        0x42F490, "55 8B EC 6A FF", (module,cave) => Raw("8B 44 24 04 C7 00 04 00 00 00 C2 04 00"));
    private static readonly Dictionary<string, int> Values = new Dictionary<string, int>
    {
        { "Credits", 10000000 }, { "Nitro Ratio", BitConverter.ToInt32(BitConverter.GetBytes(8.0f), 0) },
        { "Racers", 8 }, { "Laps", 3 }, { "Booster Duration", 60 }
    };

    internal static int GetValue(string name) { return Values[name]; }
    internal static void SetValue(string name, int value) { Values[name] = value; }

    internal static readonly InjectionHack[] Hacks =
    {
        new InjectionHack("Driver Name", 0xCD2FF0, "55 8B EC 83 E4 F8", BuildDriverName),
        new InjectionHack("Wallet Editor", 0xBD68E0, "51 56 8B F1 57", BuildWalletEditor),
        new InjectionHack("Offline Standard Upgrade Credits", 0x625970, "55 8B EC 83 E4 F8", (m,c) => BuildPaidUpgrade(m,c,UpgradeCurrency.Credits)),
        new InjectionHack("Offline Standard Upgrade Gems", 0x625AA0, "55 8B EC 83 E4 F8", (m,c) => BuildPaidUpgrade(m,c,UpgradeCurrency.Gems)),
        new InjectionHack(OfflineProUpgrade, 0xE683B0, "55 8B EC 83 E4 F8", (m,c) => BuildPaidUpgrade(m,c,UpgradeCurrency.Fusion)),
        new InjectionHack(CareerCarChoice, 0xA2B3AE, "E8 0D 39 03 00", BuildCareerCarChoice),
        new InjectionHack(CareerLiveRefresh, 0xA2C904, "83 BE 48 09 00 00 00", BuildCareerLiveRefresh),
        new InjectionHack("Unlimited Booster Timers", 0x235F70, "55 8B EC 83 EC 20", BuildUnlimitedBoosterTimers),
        new InjectionHack("Unlimited Nitro", 0x4913D6, "83 3E 00 0F 84 61 05 00 00", (m,c) => Code(c,
            Raw("83 3E 03"), Jcc(0x84, c + 3, m + 0x491940), Jmp(c + 9, m + 0x4913DF))),
        new InjectionHack("Unlimited Drift", 0xC70638, "0F 84 3B 01 00 00", (m,c) => Code(c,
            Jcc(0x85, c, m + 0xC70779), Jmp(c + 6, m + 0xC7063E))),
        new InjectionHack("Unlimited Speed", 0xB93FB9, "F3 0F 11 45 EC", (m,c) => Code(c,
            Raw("C7 45 EC 00 00 80 7F"), Jmp(c + 7, m + 0xB93FBE))),
        new InjectionHack("Automatic Driver", 0x835BC2, "75 0E C7 47 14 00 00 00 00", (m,c) => Code(c,
            Jcc(0x84, c, m + 0x835BD2), Raw("C7 47 14 00 00 00 00"), Jmp(c + 13, m + 0x835BCB))),
        new InjectionHack("Hide HUD", 0x9A5807, "80 BE 38 75 00 00 00", (m,c) => Code(c,
            Raw("80 BE 38 75 00 00 01"), Jmp(c + 7, m + 0x9A580E))),
        new InjectionHack("No Traffic", 0x1CBB2A, "0F B6 46 33 88 42 33", (m,c) => Code(c,
            Raw("C6 46 33 00 0F B6 46 33 88 42 33"), Jmp(c + 11, m + 0x1CBB31))),
        new InjectionHack("Unlock R&D Labs", 0xE1B9B5, "74 10 8B 4C 24 14", (m,c) => Code(c,
            Jcc(0x85, c, m + 0xE1B9C7), Raw("8B 4C 24 14"), Jmp(c + 10, m + 0xE1B9BB))),
        new InjectionHack("Unlock Mastery", 0x9BFC37, "3B 90 E8 01 00 00", (m,c) => Code(c,
            Raw("C7 80 E8 01 00 00 00 00 00 00 3B 90 E8 01 00 00"), Jmp(c + 16, m + 0x9BFC3D))),
        // These two display/runtime overrides only fill the temporary tuning result
        // at EDI.  They deliberately never alter the persistent vehicle record at ESI.
        new InjectionHack("Max All Cars", 0xCD30EF, "8B 46 24 89 47 04", (m,c) => BuildTuningOverride(c, m + 0xCD3101, 0, 5)),
        new InjectionHack("Pro All Cars", 0xCD3101, "8B 4E 30 89 4F 10", BuildProCarOverride),
        new InjectionHack("Money Mod", 0x32D690, "2B 75 08 8B CF", (m,c) => Code(c,
            Raw("81 EE 88 13 00 00 8B CF"), Jmp(c + 8, m + 0x32D695))),
        new InjectionHack("Money Mod Currency", 0xCC8C53, "8B 30 39 56 30", (m,c) => Code(c,
            Raw("8B 30 39 D2"), Jmp(c + 4, m + 0xCC8C58))),
        new InjectionHack("Free Buy Mode", 0x32D690, "2B 75 08 8B CF", (m,c) => Code(c,
            Raw("8B CF"), Jmp(c + 2, m + 0x32D695))),
        // Gem getter in the player-economy object (offset 0x4A4).  Credits use the
        // distinct getter at 0xCDA590 and therefore continue through the normal game.
        new InjectionHack("Gems 10M", 0xCD7490, "8B 81 A8 04 00 00", (m,c) => Code(c,
            Raw("B8 80 96 98 00 C3"))),
        // Unowned catalogue entries can have no economy descriptor.  The original
        // routine dereferences ECX unconditionally; return a zero price instead.
        new InjectionHack("Safe Economy Lookup", 0xCC8C40, "8B 41 1C 56 3B 41 20", (m,c) => Code(c,
            Raw("85 C9"), Jcc(0x85, c + 2, c + 12), Raw("0F 57 C0 C3 8B 41 1C 56 3B 41 20"),
            Jmp(c + 19, m + 0xCC8C47))),
        new InjectionHack("Free Boosters", 0xCD9ACD, "83 46 04 F0 83 C4 08", (m,c) => Code(c,
            Raw("83 46 04 00 83 C4 08"), Jmp(c + 7, m + 0xCD9AD4))),
        new InjectionHack("Try All Decals", 0x7C1B9B, "74 18 8D 46 10", (m,c) => Code(c,
            Raw("C7 46 10 00 00 00 00"), Jcc(0x85, c + 7, m + 0x7C1BB5), Raw("8D 46 10"), Jmp(c + 16, m + 0x7C1BA0))),
        new InjectionHack("Max R&D Car", 0xCD3148, "8B 48 18 89 4F 04", (m,c) => BuildTuningOverride(c, m + 0xCD315A, 0, 5)),
        new InjectionHack("Pro R&D Car", 0xCD315A, "8B 48 24 89 4F 10", (m,c) => BuildTuningOverride(c, m + 0xCD3172, 0x10, 5)),
        new InjectionHack("Ghost Mode", 0x553B6F, "88 8F C0 00 00 00", (m,c) => Code(c,
            Raw("C6 87 C0 00 00 00 01"), Jmp(c + 7, m + 0x553B75))),
        new InjectionHack("Intangible Vehicles", 0xF87B37, "F3 0F 11 74 24 1C", (m,c) => Code(c,
            Jmp(c, m + 0xF87B3D))),
        new InjectionHack("Stop Vehicles", 0x554CC0, "8A 81 7C 01 00 00", (m,c) => Code(c,
            Jmp(c, m + 0x554CC6))),
        new InjectionHack("Boost Mode", 0x4913D6, "83 3E 00 0F 84 61 05 00 00", (m,c) => Code(c,
            Raw("C7 06 03 00 00 00 83 3E 00"), Jcc(0x84, c + 9, m + 0x491940), Jmp(c + 15, m + 0x4913DF))),
        new InjectionHack("WallHack", 0xF9A988, "F3 0F 10 60 FC", (m,c) => Code(c,
            Jmp(c, m + 0xF9A98D))),
        new InjectionHack("Invisible Vehicles", 0xB43352, "80 BF 0C 02 00 00 00", (m,c) => Code(c,
            Raw("80 BF 0C 02 00 00 01"), Jmp(c + 7, m + 0xB43359))),
        new InjectionHack("Credits", 0x32D4E9, "8B CE F7 D1 89 15 ?? ?? ?? ??", (m,c) => Code(c,
            Raw("BE " + I32(GetValue("Credits")) + " 8B CE F7 D1 89 15 " + I32((int)(m + 0x1CFCC54))), Jmp(c + 15, m + 0x32D4F3))),
        new InjectionHack("Racer Descriptors", 0x2BE001, "83 C4 04 C7 45 FC 03 00 00 00", (m,c) => BuildRacerDescriptors(m,c)),
        new InjectionHack(GridSpawnHook.Name, 0x2BFAF2, "8B 55 C0 8B 75 BC", GridSpawnHook.Build, GridSpawnHook.AllocationSize),
        new InjectionHack("Racers", 0x1CBB0F, "8B 46 2C 89 42 2C", (m,c) =>
            BuildRaceValue(m, c, m + 0x1CBB15, GridSpawnHook.RacersOffset, GridSpawnHook.RacersRestoreOffset, 0x2C)),
        new InjectionHack("Laps", 0x1CBAF1, "8B 46 18 89 42 18", (m,c) =>
            BuildRaceValue(m, c, m + 0x1CBAF7, GridSpawnHook.LapsOffset, GridSpawnHook.LapsRestoreOffset, 0x18)),
        new InjectionHack("Knockdown Finish", 0xC26D07, "8B 40 10 89 44 24 1C", (m,c) => Code(c,
            Raw("C7 40 10 15 00 00 00 8B 40 10 89 44 24 1C"), Jmp(c + 14, m + 0xC26D0E))),
        new InjectionHack("Free Multiplayer Tickets", 0x63E4D8, "75 15 8B 0D ?? ?? ?? ??", (m,c) => Code(c,
            Jmp(c, m + 0x63E4EF))),
        new InjectionHack("Stop Time", 0x63D837, "8B 0D ?? ?? ?? ??", (m,c) => Code(c,
            Jmp(c, m + 0x63D83D))),
        new InjectionHack("Triple Nitro", 0x491394, "FF 46 04 8B 4E 04", (m,c) => Code(c,
            Raw("83 46 04 03 8B 4E 04"), Jmp(c + 7, m + 0x49139A))),
        new InjectionHack("Triple Nitro v2", 0x490C9F, "83 79 24 00 5F", (m,c) => Code(c,
            Raw("83 79 24 FF 5F"), Jmp(c + 5, m + 0x490CA4))),
        new InjectionHack("No Crash", 0x553F91, "38 86 E7 01 00 00", (m,c) => Code(c,
            Raw("83 BE 34 01 00 00 01"), Jcc(0x85, c + 7, c + 20), Raw("B0 00"), Jmp(c + 15, m + 0x553F97),
            Raw("38 86 E7 01 00 00"), Jmp(c + 26, m + 0x553F97))),
        new InjectionHack("Unlock Mastery Moto", 0xB3AE59, "8B 46 04 89 84 24 A8 00 00 00", (m,c) => Code(c,
            Raw("C7 46 04 00 00 00 00 8B 46 04 89 84 24 A8 00 00 00"), Jmp(c + 17, m + 0xB3AE63))),
        new InjectionHack("Nitro Ratio A", 0x4914D4, "0F 28 CC F3 0F 11 65 F4 F3 0F 59 4D EC", (m,c) => Code(c,
            Raw("50 B8 "+I32(GetValue("Nitro Ratio"))+" 66 0F 6E E0 B8 00 00 40 40 66 0F 6E F8 F3 0F 5E CF 0F 28 CC F3 0F 11 65 F4 F3 0F 59 4D EC 58"), Jmp(c+35,m+0x4914E1))),
        new InjectionHack("Nitro Ratio B", 0x491730, "0F 28 CC F3 0F 11 65 E4 F3 0F 59 4D EC", (m,c) => Code(c,
            Raw("50 B8 "+I32(GetValue("Nitro Ratio"))+" 66 0F 6E E0 B8 00 00 40 40 66 0F 6E F8 F3 0F 5E CF 0F 28 CC F3 0F 11 65 E4 F3 0F 59 4D EC 58"), Jmp(c+35,m+0x49173D))),
        new InjectionHack("Booster Duration", 0x235FBF, "89 45 E0 8D 45 E0", (m,c) => Code(c,
            Raw("B8 "+I32(GetValue("Booster Duration"))+" 89 45 E0 8D 45 E0"), Jmp(c+11,m+0x235FC5))),
        new InjectionHack("Clear Booster Time", 0x2A170F, "89 04 31 8B 4D EC", (m,c) => Code(c,
            Raw("53 BB 00 00 00 00 D3 C3 33 5D F4 33 5D 18 8B C3 5B 89 04 31 8B 4D EC"), Jmp(c+23,m+0x2A1715))),
        new InjectionHack("Easy Events", 0xB6694E, "83 7D E8 10 8B 7F 04", (m,c) => Code(c,
            Raw("83 7D E8 10 C7 07 0E 00 00 00 C7 47 04 00 00 00 00 8B 7F 04 0F 43 45 D4"), Jmp(c+24,m+0xB66955))),
        new InjectionHack("5 Stars Hack", 0xCD5DEA, "F3 0F 7E 01 66 0F D6 02", (m,c) => Code(c,
            Raw("C7 41 04 03 00 00 00 66 C7 41 08 01 01 F3 0F 7E 01 66 0F D6 02 8B 41 08"), Jmp(c+24,m+0xCD5DF2)))
    };

    internal static InjectionHack Find(string name)
    {
        foreach (InjectionHack hack in Hacks) if (hack.Name == name) return hack;
        return null;
    }

    private static byte[] Raw(string text) { return InjectionHack.Hex(text); }

    private static string I32(int value)
    {
        byte[] b = BitConverter.GetBytes(value);
        return b[0].ToString("X2")+" "+b[1].ToString("X2")+" "+b[2].ToString("X2")+" "+b[3].ToString("X2");
    }

    private static byte[] Jmp(long instruction, long target)
    {
        byte[] result = new byte[5]; result[0] = 0xE9;
        Array.Copy(BitConverter.GetBytes((int)(target - (instruction + 5))), 0, result, 1, 4);
        return result;
    }

    private static byte[] Jcc(byte condition, long instruction, long target)
    {
        byte[] result = new byte[6]; result[0] = 0x0F; result[1] = condition;
        Array.Copy(BitConverter.GetBytes((int)(target - (instruction + 6))), 0, result, 2, 4);
        return result;
    }

    private static byte[] Code(long cave, params byte[][] parts)
    {
        int length = 0; foreach (byte[] part in parts) length += part.Length;
        byte[] result = new byte[length]; int offset = 0;
        foreach (byte[] part in parts) { Array.Copy(part, 0, result, offset, part.Length); offset += part.Length; }
        return result;
    }

    private static byte[] BuildDriverName(long module, long cave)
    {
        var code = new List<byte>();
        Action<string> emit = bytes => code.AddRange(Raw(bytes));
        Action<long> call = target =>
        {
            code.Add(0xE8);
            code.AddRange(BitConverter.GetBytes((int)(target - (cave + code.Count + 4))));
        };
        string state = I32((int)(cave + DriverNameStateOffset));
        emit("9C 60 A1 " + I32((int)(module + 0x1E4AA3C)) + " 85 C0");
        int noProfile = code.Count;
        code.AddRange(Jcc(0x84, cave + code.Count, cave));
        emit("3B 48 28"); // this must be the active local profile
        int otherProfile = code.Count;
        code.AddRange(Jcc(0x85, cave + code.Count, cave));
        emit("B8 01 00 00 00 BA 02 00 00 00 F0 0F B1 15 " + state);
        int noRequest = code.Count;
        code.AddRange(Jcc(0x85, cave + code.Count, cave));
        emit("8B F1 6A 00 8B CC 68 " + I32((int)(cave + DriverNameTextOffset)));
        call(module + 0xEA4F90); // create the game's reference-counted string
        emit("8B CC 6A 00 51 8B CE");
        call(module + 0xCDCFC0); // change the nickname and notify profile observers
        emit("8B CC");
        call(module + 0xBCD00); // release the temporary string reference
        emit("83 C4 04 C7 05 " + state + " 00 00 00 00");
        int resume = code.Count;
        emit("61 9D 55 8B EC 83 E4 F8");
        code.AddRange(Jmp(cave + code.Count, module + 0xCD2FF6));
        foreach (int offset in new[] { noProfile, otherProfile, noRequest })
            PatchRel32(code, offset + 2, cave + offset + 6, cave + resume);
        return code.ToArray();
    }

    private static byte[] BuildUnlimitedBoosterTimers(long module, long cave)
    {
        var code = new List<byte>();
        var fallback = new List<int>();
        var enabled = new List<int>();
        string mask = I32((int)(cave + UnlimitedBoosterMaskOffset));
        Action<string> emit = bytes => code.AddRange(Raw(bytes));
        Action<List<int>, byte> branch = (jumps, condition) =>
        {
            jumps.Add(code.Count);
            code.AddRange(Jcc(condition, cave + code.Count, cave));
        };
        emit("9C 51 83 3D " + mask + " 00");
        branch(fallback, 0x84);
        emit("8B 44 24 08 3D " + I32((int)(module + 0xD59000)));
        int outsideTimerStore = code.Count;
        code.AddRange(Jcc(0x82, cave + code.Count, cave));
        emit("3D " + I32((int)(module + 0xD5C000)));
        branch(fallback, 0x82); // the timer store must read real time for saving and expiry
        PatchRel32(code, outsideTimerStore + 2, cave + outsideTimerStore + 6, cave + code.Count);
        emit("A1 " + I32((int)(module + 0x1E4AA3C)) + " 85 C0");
        branch(fallback, 0x84);
        emit("8B 40 28 85 C0");
        branch(fallback, 0x84);
        emit("8B 80 A4 0B 00 00 85 C0");
        branch(fallback, 0x84);
        emit("83 C0 1C 8B 4C 24 0C 2B C8");
        for (int booster = 0; booster < 4; booster++)
        {
            emit("81 F9 " + I32(booster * 0x30));
            int next = code.Count;
            code.AddRange(Jcc(0x85, cave + next, cave));
            emit("F7 05 " + mask + " " + I32(1 << booster));
            branch(enabled, 0x85);
            int finish = code.Count;
            fallback.Add(finish);
            code.AddRange(Jcc(0x84, cave + finish, cave));
            PatchRel32(code, next + 2, cave + next + 6, cave + code.Count);
        }
        int native = code.Count;
        emit("59 9D 55 8B EC 83 EC 20");
        code.AddRange(Jmp(cave + code.Count, module + 0x235F76));
        int infinite = code.Count;
        const long visibleDuration = 3600000000L; // 1 hour in the native timer's microseconds
        emit("59 9D B8 " + I32(unchecked((int)visibleDuration)) + " BA " + I32((int)(visibleDuration >> 32)) + " C2 0C 00");
        foreach (int offset in fallback) PatchRel32(code, offset + 2, cave + offset + 6, cave + native);
        foreach (int offset in enabled) PatchRel32(code, offset + 2, cave + offset + 6, cave + infinite);
        return code.ToArray();
    }

    private static byte[] BuildCareerCarChoice(long module, long cave)
    {
        var code = new List<byte>();
        var unchanged = new List<int>();
        Action<string> emit = bytes => code.AddRange(Raw(bytes));
        Action<int> call = rva =>
        {
            code.Add(0xE8);
            code.AddRange(BitConverter.GetBytes(unchecked((int)(module + rva - cave - code.Count - 4))));
        };
        Action<byte> keepOriginal = condition =>
        {
            unchanged.Add(code.Count);
            code.AddRange(Jcc(condition, cave + code.Count, cave));
        };

        // Run the stock selector and keep its output for races outside the career garage.
        emit("53 56 57 8B 7C 24 0C 8B 74 24 10 56 57");
        call(0xA5ECC0);
        emit("81 7D 04 " + I32((int)(module + 0x86EB17)));
        // McLaren Legends reaches the same garage selector through its own race details.
        emit("74 0D 81 7D 04 " + I32((int)(module + 0x22B637)));
        keepOriginal(0x85);

        // Native filter mode 0, vehicle type 0 gives the full list of available cars.
        // Preserve ownership, rank checks, opponents and the original race settings.
        emit("83 EC 08 31 C0 89 04 24 89 44 24 04 8B DC 83 EC 0C 8B 0D " + I32((int)(module + 0x1E4AA0C)));
        emit("8B C4 53 56 50");
        call(0x802140);
        emit("50 8B CF");
        call(0x0F0AC0);
        emit("83 C4 14");

        foreach (int offset in unchanged)
            PatchRel32(code, offset + 2, cave + offset + 6, cave + code.Count);
        emit("8B C7 5F 5E 5B 83 C4 08");
        code.AddRange(Jmp(cave + code.Count, module + 0xA2B3B3));
        return code.ToArray();
    }

    private static byte[] BuildCareerLiveRefresh(long module, long cave)
    {
        var code = new List<byte>();
        Action<string> emit = bytes => code.AddRange(Raw(bytes));
        Action<int> call = rva =>
        {
            code.Add(0xE8);
            code.AddRange(BitConverter.GetBytes(unchecked((int)(module + rva - cave - code.Count - 4))));
        };
        Func<byte,int> branch = condition =>
        {
            int at=code.Count;
            code.AddRange(Jcc(condition,cave+at,cave));
            return at;
        };
        string state=I32((int)(cave+CareerLiveStateOffset));
        string screen=I32((int)(cave+CareerLiveStateOffset+4));
        string applied=I32((int)(cave+CareerLiveStateOffset+8));
        emit("9C 60 A1 "+state+" 3B 05 "+applied);
        int changed=branch(0x85);
        emit("3B 35 "+screen);
        int unchanged=branch(0x84);
        int refresh=code.Count;
        emit("A3 "+applied+" 89 35 "+screen);
        // This runs on the game's UI thread, preserving floating-point state across callbacks.
        emit("8B EC 81 EC 10 02 00 00 83 E4 F0 0F AE 04 24");
        emit("83 EC 30 8B FC 31 C0 B9 0C 00 00 00 F3 AB 8B FC 8B DE");
        emit("57 8B 0D "+I32((int)(module+0x1E4AA0C)));
        call(0x802010);
        emit("83 3D "+state+" 00");
        int normal=branch(0x84);
        emit("8D 47 24 50 57 8D 47 18 50 8B 0D "+I32((int)(module+0x1E4AA0C)));
        call(0x802140);
        int activeDone=code.Count;
        code.AddRange(Jmp(cave+activeDone,cave));
        int stock=code.Count;
        emit("8D 83 10 09 00 00 50 8D 4F 0C");
        call(0xA5EAE0);
        emit("57 8D 47 18 50 8D 4F 0C");
        call(0xA5ECC0);
        int rebuild=code.Count;
        emit("8D 47 18 50 8B CB");
        call(0xA2DD20);
        emit("8B 03 8B CB FF 90 DC 00 00 00 8B CB");
        call(0x9C1300);
        emit("8B CB");
        call(0x9C1480);
        foreach(int offset in new[] {0,12,24})
        {
            emit("FF 77 "+offset.ToString("X2")+" FF 15 "+I32((int)(module+0x1884A5C))+" 83 C4 04");
        }
        emit("83 C4 30 0F AE 0C 24 8B E5");
        int done=code.Count;
        emit("61 9D 83 BE 48 09 00 00 00");
        code.AddRange(Jmp(cave+code.Count,module+0xA2C90B));
        PatchRel32(code,changed+2,cave+changed+6,cave+refresh);
        PatchRel32(code,unchanged+2,cave+unchanged+6,cave+done);
        PatchRel32(code,normal+2,cave+normal+6,cave+stock);
        PatchRel32(code,activeDone+1,cave+activeDone+5,cave+rebuild);
        var image=new byte[CareerLiveStateOffset+12];
        code.CopyTo(image);
        return image;
    }

    private enum UpgradeCurrency { Credits, Gems, Fusion }

    private static byte[] BuildPaidUpgrade(long module, long cave, UpgradeCurrency currency)
    {
        bool pro = currency == UpgradeCurrency.Fusion;
        bool gems = currency == UpgradeCurrency.Gems;
        int levelBase = pro ? 0x214 : 0x204;
        var code = new List<byte>();
        var done = new List<int>();
        Action<string> emit = bytes => code.AddRange(Raw(bytes));
        Action<long> call = rva =>
        {
            code.Add(0xE8);
            code.AddRange(BitConverter.GetBytes(unchecked((int)(module + rva - cave - code.Count - 4))));
        };
        Action<byte> failIf = condition =>
        {
            done.Add(code.Count);
            code.AddRange(Jcc(condition, cave + code.Count, cave));
        };

        // Each native button receives its own upgrade widget in ECX.
        // The game derives the displayed prices and maximum level from the local catalog.
        emit("9C 60 8B EC 81 EC 40 02 00 00 83 E4 F0 0F AE 04 24 8B F9");
        emit("81 3F " + I32((int)(module + (pro ? 0x1D64364 : 0x1D2D5DC))));
        failIf(0x85);
        if (pro)
        {
            // The ordinary purchase still handles a loaded card catalog and inventory.
            emit("A1 " + I32((int)(module + 0x1E4AC2C)) + " 85 C0");
            failIf(0x84);
            emit("83 78 48 00");
            failIf(0x85);
            emit("83 BF D8 02 00 00 00 8B 87 A0 02 00 00"); // local offer and apply button
            failIf(0x84);
            emit("85 C0");
            failIf(0x84);
            emit("80 B8 19 02 00 00 00");
            failIf(0x85);
        }
        // Never write the synthetic tuning returned by either ALL CARS override.
        foreach (int rva in new[] { 0xCD30EF, 0xCD3101 })
        {
            emit("80 3D " + I32((int)(module + rva)) + " 8B");
            failIf(0x85);
        }
        emit("A1 " + I32((int)(module + 0x1E4AA3C)) + " 85 C0");
        failIf(0x84);
        emit("8B 70 28 85 F6");
        failIf(0x84);
        emit("80 7E 11 00");
        failIf(0x85);
        emit("8B 9F " + I32(pro ? 0x218 : 0x228) + " 83 FB 03");
        failIf(0x87);
        emit("8B 87 " + I32(pro ? 0x210 : 0x22C) + " 89 84 24 00 02 00 00 8D 8C 24 00 02 00 00 51 8B CE");
        call(0xCD54E0);
        emit("84 C0");
        failIf(0x84);
        if (pro)
            emit("8D 97 B0 02 00 00 8B 02 33 C2 33 05 " + I32((int)(module + 0x1E519B4)));
        else
            emit("8B 87 " + I32(gems ? 0x23C : 0x238));
        emit("85 C0");
        failIf(0x8E);
        emit("89 84 24 24 02 00 00 8B CE");
        call(pro ? 0xCD7A80 : gems ? 0xCD7490 : 0xCDA590);
        emit("2B 84 24 24 02 00 00");
        failIf(0x8C);
        emit("89 84 24 28 02 00 00");

        // Copy all levels, update only the selected part, and use the normal save observer.
        emit("8D 84 24 00 02 00 00 8D 90 04 00 00 00 50 52 8B CE");
        call(0xCD2FF0);
        emit("8B 84 9C " + I32(levelBase));
        emit(pro ? "3B 84 9F 90 02 00 00" : "3B 87 30 02 00 00");
        failIf(0x85);
        emit("3B 87 " + I32(pro ? 0x2BC : 0x234));
        failIf(0x8D);
        emit("40 89 84 9C " + I32(levelBase) + " 8D 84 24 00 02 00 00 8D 50 04 52 50 8B CE");
        call(0xCD31D0);
        emit("8B CE FF B4 24 " + I32(pro || gems ? 0x228 : 0x224));
        call(pro ? 0xCD7950 : gems ? 0xCD7360 : 0xCD8390);

        if (pro) emit("0F AE 0C 24 8B E5 61 9D C3");
        foreach (int offset in done) PatchRel32(code, offset + 2, cave + offset + 6, cave + code.Count);
        emit("0F AE 0C 24 8B E5 61 9D");
        if (pro)
        {
            emit("55 8B EC 83 E4 F8");
            code.AddRange(Jmp(cave + code.Count, module + 0xE683B6));
        }
        else emit("C2 04 00");
        return code.ToArray();
    }

    private static byte[] BuildWalletEditor(long module, long cave)
    {
        const int creditSetterOffset = 0x600;
        var code = new List<byte>();
        string state = I32((int)(cave + WalletCommandOffset));
        string currency = I32((int)(cave + WalletCommandOffset + 4));
        string amount = I32((int)(cave + WalletCommandOffset + 8));
        string quoteVehicle = I32((int)(cave + WalletCommandOffset + 12));
        string quoteAmount = I32((int)(cave + WalletCommandOffset + 16));
        string quoteCurrency = I32((int)(cave + WalletCommandOffset + 20));
        Action<string> emit = bytes => code.AddRange(Raw(bytes));
        Action<long> call = target =>
        {
            code.Add(0xE8);
            code.AddRange(BitConverter.GetBytes(unchecked((int)(target - cave - code.Count - 4))));
        };
        Func<byte, int> branch = condition =>
        {
            int offset = code.Count;
            code.AddRange(Jcc(condition, cave + offset, cave));
            return offset;
        };
        Action<int, int> targetAt = (offset, target) =>
            PatchRel32(code, offset + 2, cave + offset + 6, cave + target);

        emit("9C 83 3D " + state + " 01");
        int idle = branch(0x85);
        emit("60 A1 " + I32((int)(module + 0x1E4AA3C)) + " 85 C0");
        int noManager = branch(0x84);
        emit("8B 70 28 85 F6");
        int noProfile = branch(0x84);
        emit("80 7E 11 00");
        int notifying = branch(0x85);
        emit("C7 05 " + state + " 02 00 00 00");
        // Preserve any live SSE/x87 state across profile observer notifications.
        emit("8B DC 81 EC 10 02 00 00 83 E4 F0 0F AE 04 24");
        emit("83 3D " + currency + " " + SetFusionBalanceCommand.ToString("X2"));
        int notFusion = branch(0x85);
        emit("FF 35 " + amount + " 8B CE");
        call(module + 0xCD7950);
        emit("8B CE");
        call(module + 0xCD7A80);
        int fusionDone = code.Count;
        code.AddRange(Jmp(cave + fusionDone, cave));
        targetAt(notFusion, code.Count);
        emit("8B 7B 18 83 3D " + currency + " 02");
        int purchase = branch(0x83);
        emit("FF 35 " + amount + " 8B CE 83 3D " + currency + " 00");
        int gems = branch(0x85);
        call(cave + creditSetterOffset);
        emit("8B CE");
        call(module + 0xCDA590);
        int creditsDone = code.Count;
        code.AddRange(Jmp(cave + creditsDone, cave));
        targetAt(gems, code.Count);
        call(module + 0xCD7360);
        emit("8B CE");
        call(module + 0xCD7490);
        PatchRel32(code, creditsDone + 1, cave + creditsDone + 5, cave + code.Count);
        PatchRel32(code, fusionDone + 1, cave + fusionDone + 5, cave + code.Count);
        emit("3B 05 " + amount + " 0F 95 C0 0F B6 C0 83 C0 03");
        int balanceDone = code.Count;
        code.AddRange(Jmp(cave + balanceDone, cave));
        targetAt(purchase, code.Count);
        emit("B8 05 00 00 00 81 3F " + I32((int)(module + 0x1D33B78)));
        var purchaseDone = new List<int> { branch(0x85) };
        // GS_Garage -> selection -> vehicle definition -> numeric vehicle ID.
        emit("8B 97 D4 02 00 00 85 D2");
        purchaseDone.Add(branch(0x84));
        emit("8B 12 85 D2");
        purchaseDone.Add(branch(0x84));
        emit("8B 7A 24 83 FF FF");
        purchaseDone.Add(branch(0x84));
        emit("57 8B CC 51 8B CE");
        call(module + 0xCD54E0);
        emit("83 C4 04 83 3D " + currency + " 05");
        int sale = branch(0x83);
        emit("84 C0 B8 06 00 00 00");
        purchaseDone.Add(branch(0x85));
        emit("83 3D " + currency + " 03");
        int readPrice = branch(0x84);
        emit("C7 84 24 00 02 00 00 01 00 00 00 C7 84 24 04 02 00 00 00 00 00 00");
        emit("83 3D " + currency + " 04");
        int freeBuy = branch(0x85);
        emit("39 3D " + quoteVehicle + " B8 0A 00 00 00");
        purchaseDone.Add(branch(0x85));
        emit("A1 " + quoteAmount + " 89 84 24 00 02 00 00 A1 " + quoteCurrency + " 89 84 24 04 02 00 00");
        targetAt(freeBuy, code.Count);
        emit("8B CE 83 BC 24 04 02 00 00 00");
        int useGemBalance = branch(0x85);
        call(module + 0xCDA590);
        int gotCredits = code.Count;
        code.AddRange(Jmp(cave + gotCredits, cave));
        targetAt(useGemBalance, code.Count);
        call(module + 0xCD7490);
        PatchRel32(code, gotCredits + 1, cave + gotCredits + 5, cave + code.Count);
        emit("2B 84 24 00 02 00 00 89 84 24 08 02 00 00 B8 07 00 00 00");
        purchaseDone.Add(branch(0x8C));
        emit("6A 00 57 8B CE");
        call(module + 0xCD8560);
        emit("57 8B CC 51 8B CE");
        call(module + 0xCD54E0);
        emit("83 C4 04 84 C0 B8 08 00 00 00");
        purchaseDone.Add(branch(0x84));
        emit("8B CE 83 BC 24 04 02 00 00 00");
        int spendGems = branch(0x85);
        emit("FF B4 24 00 02 00 00");
        call(module + 0xCD8390);
        int spentCredits = code.Count;
        code.AddRange(Jmp(cave + spentCredits, cave));
        targetAt(spendGems, code.Count);
        emit("FF B4 24 08 02 00 00");
        call(module + 0xCD7360);
        PatchRel32(code, spentCredits + 1, cave + spentCredits + 5, cave + code.Count);
        emit("8B 4B 18"); // refresh the selected vehicle's garage controls
        call(module + 0xA8A160);
        emit("89 3D " + quoteVehicle + " 8B 84 24 00 02 00 00 A3 " + quoteAmount);
        emit("8B 84 24 04 02 00 00 A3 " + quoteCurrency);
        emit("B8 03 00 00 00");
        int bought = code.Count;
        code.AddRange(Jmp(cave + bought, cave));
        targetAt(readPrice, code.Count);
        emit("8B 4B 18 8B 89 B0 08 00 00 85 C9 B8 09 00 00 00");
        purchaseDone.Add(branch(0x84));
        emit("89 8C 24 08 02 00 00 8B 41 4C 83 F8 01");
        int standardCurrency = branch(0x86);
        emit("B8 01 00 00 00");
        targetAt(standardCurrency, code.Count);
        emit("89 84 24 04 02 00 00 50");
        call(module + 0xCC8C00);
        emit("F2 0F 2C C0 85 C0");
        int gotPrice = branch(0x8F);
        emit("83 B4 24 04 02 00 00 01 FF B4 24 04 02 00 00 8B 8C 24 08 02 00 00");
        call(module + 0xCC8C00);
        emit("F2 0F 2C C0 85 C0");
        int positive = branch(0x8F);
        emit("B8 09 00 00 00");
        int noPrice = code.Count;
        code.AddRange(Jmp(cave + noPrice, cave));
        targetAt(gotPrice, code.Count);
        targetAt(positive, code.Count);
        emit("A3 " + quoteAmount + " 89 3D " + quoteVehicle + " 8B 84 24 04 02 00 00 A3 " + quoteCurrency);
        emit("B8 03 00 00 00");
        int quoted = code.Count;
        code.AddRange(Jmp(cave + quoted, cave));

        targetAt(sale, code.Count);
        emit("84 C0 B8 0B 00 00 00");
        purchaseDone.Add(branch(0x84));
        emit("83 FF 01 B8 0C 00 00 00"); // keep the starter Dodge Dart available
        purchaseDone.Add(branch(0x84));
        emit("83 3D " + currency + " 05");
        int confirmSale = branch(0x85);
        emit("89 3D " + quoteVehicle + " B8 03 00 00 00");
        int selectedForSale = code.Count;
        code.AddRange(Jmp(cave + selectedForSale, cave));

        targetAt(confirmSale, code.Count);
        emit("39 3D " + quoteVehicle + " B8 0A 00 00 00");
        purchaseDone.Add(branch(0x85));
        emit("8B CE 83 3D " + quoteCurrency + " 01");
        int refundGems = branch(0x84);
        call(module + 0xCDA590);
        int refundBalance = code.Count;
        code.AddRange(Jmp(cave + refundBalance, cave));
        targetAt(refundGems, code.Count);
        call(module + 0xCD7490);
        PatchRel32(code, refundBalance + 1, cave + refundBalance + 5, cave + code.Count);
        emit("03 05 " + amount + " 89 84 24 08 02 00 00 B8 0D 00 00 00");
        purchaseDone.Add(branch(0x80)); // refuse a refund that would overflow the game balance
        emit("6A 01 6A 00 57 8B CE");
        call(module + 0xCD8850); // erase the ownership record and save the active profile
        emit("57 8B CC 51 8B CE");
        call(module + 0xCD54E0);
        emit("83 C4 04 84 C0 B8 08 00 00 00");
        purchaseDone.Add(branch(0x85));
        emit("8B CE FF B4 24 08 02 00 00 83 3D " + quoteCurrency + " 01");
        int setGemRefund = branch(0x84);
        call(cave + creditSetterOffset);
        int refunded = code.Count;
        code.AddRange(Jmp(cave + refunded, cave));
        targetAt(setGemRefund, code.Count);
        call(module + 0xCD7360);
        PatchRel32(code, refunded + 1, cave + refunded + 5, cave + code.Count);
        // The garage maintains two cached arrays of 0x70-byte vehicle entries.
        // Its purchase observer marks both copies owned. Clear both after removal.
        foreach (int begin in new[] { 0x2BC, 0x2C8 })
        {
            emit("8B 4B 18 8B 81 " + I32(begin) + " 8B 89 " + I32(begin+4));
            int nextEntry=code.Count;
            emit("39 C8");
            int endEntries=branch(0x83);
            emit("8B 10 39 7A 24");
            int otherCar=branch(0x85);
            emit("C6 40 2E 00");
            targetAt(otherCar,code.Count);
            emit("83 C0 70");
            code.AddRange(Jmp(cave+code.Count,cave+nextEntry));
            targetAt(endEntries,code.Count);
        }
        emit("8B 4B 18");
        call(module + 0xA8A160);
        emit("B8 03 00 00 00");
        foreach (int offset in new[] { bought, noPrice, quoted, selectedForSale })
            PatchRel32(code, offset + 1, cave + offset + 5, cave + code.Count);
        foreach (int offset in purchaseDone) targetAt(offset, code.Count);
        PatchRel32(code, balanceDone + 1, cave + balanceDone + 5, cave + code.Count);
        emit("A3 " + state);
        emit("0F AE 0C 24 8B E3");
        int restore = code.Count;
        emit("61");
        targetAt(idle, code.Count);
        emit("9D 51 56 8B F1 57");
        code.AddRange(Jmp(cave + code.Count, module + 0xBD68E5));
        foreach (int offset in new[] { noManager, noProfile, notifying }) targetAt(offset, restore);

        // The 4.8.0.7 absolute gem setter, adapted to the credit field and
        // its matching observer slot. It keeps the stock save and refresh behavior.
        byte[] setCredits = Raw(
            "55 8B EC 83 EC 08 53 56 57 8B F9 8B 87 88 04 00 00 8D 9F 84 04 00 00 " +
            "8B 13 8B 08 33 0A F7 D1 8B C1 89 4D F8 C1 E8 10 33 C1 0F AF 43 0C 8B C8 C1 E9 10 " +
            "33 C8 0F AF 4B 0C 8B C1 C1 E8 10 33 C1 3B 43 08 74 10 6A 00 8B CB E8 00 00 00 00 " +
            "C7 45 F8 00 00 00 00 FF 75 08 8B CB E8 00 00 00 00 8B 43 04 8B 0B 8B 10 33 11 " +
            "F7 D2 8B C2 89 55 FC C1 E8 10 33 C2 0F AF 43 0C 8B D0 C1 EA 10 33 D0 0F AF 53 0C " +
            "8B C2 C1 E8 10 33 C2 3B 43 08 74 10 6A 00 8B CB E8 00 00 00 00 33 D2 89 55 FC " +
            "EB 03 8B 55 FC C6 47 11 01 33 F6 8B 5F 08 2B 5F 04 C1 FB 02 85 DB 74 1B 8B 47 04 " +
            "8B 0C B0 85 C9 74 09 8B 01 52 FF 75 F8 FF 50 0C 8B 55 FC 46 3B F3 72 E5 80 7F 10 " +
            "00 C6 47 11 00 74 39 8B 77 04 3B 77 08 74 2D 8D 5E 04 83 3E 00 75 1A 8B 47 08 " +
            "2B C3 83 E0 FC 50 53 56 FF 15 00 00 00 00 83 C4 0C 83 47 08 FC EB 06 83 C6 04 " +
            "83 C3 04 3B 77 08 75 D6 C6 47 10 00 8B 45 FC 39 45 F8 74 09 8B 47 30 8D 4F 30 " +
            "FF 50 04 5F 5E 5B 8B E5 5D C2 04 00");
        foreach (int offset in new[] { 0x48, 0x59, 0x92 })
            Array.Copy(BitConverter.GetBytes(unchecked((int)(module + 0x32D480 - cave - creditSetterOffset - offset - 5))),
                0, setCredits, offset + 1, 4);
        Array.Copy(BitConverter.GetBytes(unchecked((int)(module + 0x1884A7C))), 0, setCredits, 0xF6, 4);

        if (code.Count > creditSetterOffset) throw new InvalidOperationException("Wallet code exceeds allocation.");
        var installed = new byte[WalletCommandOffset + 24];
        code.CopyTo(installed);
        setCredits.CopyTo(installed, creditSetterOffset);
        return installed;
    }

    private static byte[] BuildProCarOverride(long module, long cave)
    {
        // Match the vehicle ID used by the original tuning lookup. The garage
        // reads its four PRO caps from the vehicle's tuning data at +0x6E0.
        var code = new List<byte>();
        var fallback = new List<int>();
        Action<string> emit = bytes => code.AddRange(Raw(bytes));
        Action<byte> branch = condition =>
        {
            fallback.Add(code.Count);
            code.AddRange(Jcc(condition, cave + code.Count, cave));
        };
        emit("9C 60 A1 " + I32((int)(module + 0x1E4AA0C)) + " 85 C0"); // pushfd; pushad; registry
        branch(0x84);
        emit("8B 48 04 8B 50 08 8B 45 0C 8B 00"); // registry begin/end and vehicle ID
        int next = code.Count;
        emit("39 D1");
        branch(0x83); // end of registry
        emit("8B 19 83 C1 04 85 DB");
        code.AddRange(Jcc(0x84, cave + code.Count, cave + next));
        emit("39 43 24");
        code.AddRange(Jcc(0x85, cave + code.Count, cave + next));
        emit("8B CB 8B 03 FF 50 7C 85 C0"); // native vehicle tuning getter
        branch(0x84);
        emit("8B 88 E0 06 00 00 8B 90 E4 06 00 00 8B 98 E8 06 00 00 8B 80 EC 06 00 00");
        foreach (string compare in new[] { "83 F9 0A", "83 FA 0A", "83 FB 0A", "83 F8 0A" })
        {
            emit(compare);
            branch(0x87);
        }
        emit("89 4F 10 89 57 14 89 5F 18 89 47 1C 61 9D");
        code.AddRange(Jmp(cave + code.Count, module + 0xCD3119));

        int original = code.Count;
        emit("61 9D 8B 4E 30 89 4F 10");
        code.AddRange(Jmp(cave + code.Count, module + 0xCD3107));
        foreach (int offset in fallback)
            PatchRel32(code, offset + 2, cave + offset + 6, cave + original);
        return code.ToArray();
    }

    private static byte[] BuildTuningOverride(long cave, long resume, byte firstSlot, int level)
    {
        var instructions = new List<byte>();
        for (int part = 0; part < 4; part++)
        {
            byte slot = (byte)(firstSlot + 4 * part);
            // mov dword ptr [edi + disp8], imm32, also for displacement zero.
            instructions.AddRange(new byte[] { 0xC7, 0x47, slot });
            instructions.AddRange(BitConverter.GetBytes(level));
        }
        instructions.AddRange(Jmp(cave + instructions.Count, resume));
        return instructions.ToArray();
    }

    private static byte[] BuildRaceValue(long module, long cave, long resume, int settingOffset, int restoreOffset, byte field)
    {
        // The solo race loader reads the selected event again while building the race.
        // Change that source only for this caller and restore it after the grid is built.
        string fieldByte = field.ToString("X2");
        string slot = I32((int)NativeInjectionEngine.RaceValueAddress(settingOffset).ToInt64());
        long record = NativeInjectionEngine.RaceValueAddress(restoreOffset).ToInt64();
        var restore = new List<byte> { 0x60 };
        GridSpawnHook.RestoreRaceSetting(restore, module, record, field);
        restore.Add(0x61);
        byte[] update = Raw("51 B9 " + I32((int)record) + " " +
            "53 8B 5E " + fieldByte + " 89 59 04 89 31 " +
            "5B " +
            "89 41 08 89 46 " + fieldByte + " 59");
        byte[] copy = Code(cave,
            Raw("9C 81 7D 04 " + I32((int)(module + 0xA349FC)) + " 75 " +
                (restore.Count + update.Length + 9).ToString("X2")),
            restore.ToArray(),
            Raw("A1 " + slot + " 85 C0 74 " + update.Length.ToString("X2")),
            update,
            Raw("9D 8B 46 " + fieldByte + " 89 42 " + fieldByte));
        return Code(cave, copy, Jmp(cave + copy.Length, resume));
    }

    private static byte[] BuildRacerDescriptors(long module, long cave)
    {
        var code = new List<byte>();
        Action<string> add = text => code.AddRange(Raw(text));
        Action<int> add32 = value => code.AddRange(BitConverter.GetBytes(value));
        Action<long> call = target => { long at=cave+code.Count; code.Add(0xE8); add32((int)(target-(at+5))); };

        add("9C 60");                                      // pushfd / pushad
        add("8B 75 E4 2B 75 E0 C1 FE 03");                // esi = original descriptor count
        add("85 F6");
        int noDescriptorsJump=code.Count; add("0F 8E 00 00 00 00");
        add("8B 3D "); add32((int)NativeInjectionEngine.RaceValueAddress(GridSpawnHook.RacersOffset).ToInt64());
        add("3B F7");
        int alreadyLargeJump=code.Count; add("0F 8D 00 00 00 00");
        add("57");                                          // vector.resize(requested)
        add("8D 4D E0"); call(module+0x298590);
        add("8B 5D E0 8D 34 F3");                         // original source range
        add("8B C6 2B C3 83 F8 08 7E 03 83 C3 08");       // cycle AI entries, not the player
        add("8B FE");                                      // first appended destination
        long loop=cave+code.Count;
        add("3B 7D E4");
        int doneJump=code.Count; add("0F 8D 00 00 00 00");
        add("57 8B CB 8D 53 08"); call(module+0x2A2C60);   // reference-counted copy
        add("83 C7 08 83 C3 08 3B DE");
        int keepSourceJump=code.Count; add("72 00");      // jb source_ready
        add("8B 5D E0 8B C6 2B C3 83 F8 08 7E 03 83 C3 08");
        long sourceReady=cave+code.Count;
        add("EB "); code.Add((byte)(loop-(cave+code.Count+1)));
        long done=cave+code.Count;
        add("61 9D 83 C4 04 C7 45 FC 03 00 00 00");
        byte[] tail=Jmp(cave+code.Count,module+0x2BE00B); code.AddRange(tail);

        code[keepSourceJump+1]=(byte)(sourceReady-(cave+keepSourceJump+2));
        PatchRel32(code,noDescriptorsJump+2,cave+noDescriptorsJump+6,done);
        PatchRel32(code,alreadyLargeJump+2,cave+alreadyLargeJump+6,done);
        PatchRel32(code,doneJump+2,cave+doneJump+6,done);
        return code.ToArray();
    }

    private static void PatchRel32(List<byte> code, int offset, long nextInstruction, long target)
    {
        byte[] displacement=BitConverter.GetBytes((int)(target-nextInstruction));
        for(int i=0;i<4;i++) code[offset+i]=displacement[i];
    }

}

internal static class NativeInjectionEngine
{
    private static int allocationProcessId;
    private static int driverNameProcessId;
    private static int walletProcessId;
    private static int boosterProcessId;
    private const uint ProcessVmOperation = 0x0008, ProcessVmRead = 0x0010, ProcessVmWrite = 0x0020;
    private const uint MemCommit = 0x1000, MemReserve = 0x2000, MemRelease = 0x8000, PageExecuteReadWrite = 0x40;
    private static readonly Dictionary<string, IntPtr> Allocations = new Dictionary<string, IntPtr>();
    private static readonly Dictionary<string, byte[]> SavedOriginals = new Dictionary<string, byte[]>();
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint a,bool i,int p);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool ReadProcessMemory(IntPtr p,IntPtr a,byte[] b,int s,out IntPtr r);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool WriteProcessMemory(IntPtr p,IntPtr a,byte[] b,int s,out IntPtr w);
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr VirtualAllocEx(IntPtr p,IntPtr a,UIntPtr s,uint t,uint pr);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool VirtualFreeEx(IntPtr p,IntPtr a,UIntPtr s,uint t);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool VirtualProtectEx(IntPtr p,IntPtr a,UIntPtr s,uint n,out uint o);
    [DllImport("kernel32.dll")] static extern bool FlushInstructionCache(IntPtr p,IntPtr a,UIntPtr s);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    internal static bool Toggle(InjectionHack hack, bool enable, out string error)
    {
        error=""; Process[] games=Process.GetProcessesByName("Asphalt8"); if(games.Length==0){error="game not found";return false;}
        Process game=games[0]; IntPtr process=OpenProcess(ProcessVmOperation|ProcessVmRead|ProcessVmWrite,false,game.Id);
        if(process==IntPtr.Zero){error="OpenProcess "+Marshal.GetLastWin32Error();return false;}
        try
        {
            if (allocationProcessId != game.Id)
            {
                Allocations.Clear();
                SavedOriginals.Clear();
                allocationProcessId = game.Id;
            }
            long module=game.MainModule.BaseAddress.ToInt64(); IntPtr site=new IntPtr(module+hack.Rva); IntPtr count;
            if(enable)
            {
                byte[] current=new byte[hack.Original.Length];
                if(!ReadProcessMemory(process,site,current,current.Length,out count)||!EqualMasked(current,hack.Original,hack.OriginalMask)){error="unexpected signature";return false;}
                SavedOriginals[hack.Name]=(byte[])current.Clone();
                IntPtr cave=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)hack.AllocationSize,MemCommit|MemReserve,PageExecuteReadWrite);
                if(cave==IntPtr.Zero){error="remote allocation failed: "+Marshal.GetLastWin32Error();return false;}
                byte[] code;
                try { code=hack.BuildCode(module,cave.ToInt64()); }
                catch(Exception ex){VirtualFreeEx(process,cave,UIntPtr.Zero,MemRelease);error=ex.Message;return false;}
                if(code.Length>hack.AllocationSize){VirtualFreeEx(process,cave,UIntPtr.Zero,MemRelease);error="code exceeds allocation";return false;}
                if(!WriteProcessMemory(process,cave,code,code.Length,out count)){VirtualFreeEx(process,cave,UIntPtr.Zero,MemRelease);error="cannot write code cave";return false;}
                byte[] patch=new byte[hack.Original.Length]; for(int i=0;i<patch.Length;i++)patch[i]=0x90; patch[0]=0xE9;
                Array.Copy(BitConverter.GetBytes((int)(cave.ToInt64()-(site.ToInt64()+5))),0,patch,1,4);
                uint old; VirtualProtectEx(process,site,(UIntPtr)patch.Length,PageExecuteReadWrite,out old);
                bool ok=WriteProcessMemory(process,site,patch,patch.Length,out count); uint ignored; VirtualProtectEx(process,site,(UIntPtr)patch.Length,old,out ignored);
                FlushInstructionCache(process,site,(UIntPtr)patch.Length); FlushInstructionCache(process,cave,(UIntPtr)code.Length);
                if(!ok){VirtualFreeEx(process,cave,UIntPtr.Zero,MemRelease);error="cannot write branch";return false;}
                Allocations[hack.Name]=cave; return true;
            }
            else
            {
                uint old; VirtualProtectEx(process,site,(UIntPtr)hack.Original.Length,PageExecuteReadWrite,out old);
                byte[] restore; if(!SavedOriginals.TryGetValue(hack.Name,out restore)) restore=hack.Original;
                bool ok=WriteProcessMemory(process,site,restore,restore.Length,out count); uint ignored; VirtualProtectEx(process,site,(UIntPtr)hack.Original.Length,old,out ignored);
                FlushInstructionCache(process,site,(UIntPtr)hack.Original.Length);
                IntPtr cave; if(Allocations.TryGetValue(hack.Name,out cave)){VirtualFreeEx(process,cave,UIntPtr.Zero,MemRelease);Allocations.Remove(hack.Name);}
                SavedOriginals.Remove(hack.Name);
                if(!ok){error="cannot restore original bytes";return false;} return true;
            }
        }
        finally{CloseHandle(process);}
    }

    internal static IntPtr RaceValueAddress(int offset)
    {
        return IntPtr.Add(Allocations[GridSpawnHook.Name], offset);
    }

    internal static bool ConsumeMultiplayerRequest()
    {
        IntPtr cave;
        if (allocationProcessId == 0 || !Allocations.TryGetValue(InjectionCatalog.MultiplayerEntryName,out cave)) return false;
        IntPtr process = OpenProcess(ProcessVmRead|ProcessVmWrite|ProcessVmOperation,false,allocationProcessId);
        if (process == IntPtr.Zero) return false;
        try
        {
            byte[] flag = new byte[4]; IntPtr count;
            IntPtr address = IntPtr.Add(cave,0x100);
            if (!ReadProcessMemory(process,address,flag,4,out count) || count.ToInt32()!=4 || BitConverter.ToInt32(flag,0)==0) return false;
            return WriteProcessMemory(process,address,new byte[4],4,out count) && count.ToInt32()==4;
        }
        finally { CloseHandle(process); }
    }

    internal static bool SetCareerLiveMode(bool enabled, out string error)
    {
        var games=Process.GetProcessesByName("Asphalt8");
        if(games.Length!=1){error="Open exactly one game instance.";return false;}
        int pid=games[0].Id;
        if (allocationProcessId!=pid || !Allocations.ContainsKey(InjectionCatalog.CareerLiveRefresh))
            if (!Toggle(InjectionCatalog.Find(InjectionCatalog.CareerLiveRefresh),true,out error)) return false;
        IntPtr process=OpenProcess(ProcessVmOperation|ProcessVmWrite,false,pid);
        if(process==IntPtr.Zero){error="Career refresh is unavailable.";return false;}
        try
        {
            IntPtr written;
            byte[] value=BitConverter.GetBytes(enabled?1:0);
            bool ok=WriteProcessMemory(process,IntPtr.Add(Allocations[InjectionCatalog.CareerLiveRefresh],
                InjectionCatalog.CareerLiveStateOffset),value,4,out written) && written.ToInt32()==4;
            error=ok?"":"Cannot refresh the car selector.";
            return ok;
        }
        finally { CloseHandle(process); }
    }

    internal static bool SetRaceValue(int processId, int offset, int value, out string error)
    {
        if (allocationProcessId != processId || !Allocations.ContainsKey(GridSpawnHook.Name))
        {
            error = "Starting grid is unavailable.";
            return false;
        }
        IntPtr process = OpenProcess(ProcessVmOperation | ProcessVmWrite, false, processId);
        if (process == IntPtr.Zero)
        {
            error = "Cannot access the game.";
            return false;
        }
        try
        {
            IntPtr written;
            bool ok = WriteProcessMemory(process, RaceValueAddress(offset), BitConverter.GetBytes(value), 4, out written)
                && written.ToInt32() == 4;
            error = ok ? "" : "Cannot send the value to the game.";
            return ok;
        }
        finally { CloseHandle(process); }
    }

    internal static bool QueueDriverName(string name, out string error)
    {
        byte[] encoded;
        try { encoded = new System.Text.UTF8Encoding(false, true).GetBytes(name + "\0"); }
        catch (System.Text.EncoderFallbackException) { error = "Invalid characters."; return false; }
        if (encoded.Length < 2 || encoded.Length > 65)
        {
            error = "The name must be between 1 and 64 bytes.";
            return false;
        }

        var games = Process.GetProcessesByName("Asphalt8");
        if (games.Length != 1) { error = "Exactly one game instance must be running."; return false; }
        int pid = games[0].Id;
        if (driverNameProcessId != pid)
        {
            if (!Toggle(InjectionCatalog.Find("Driver Name"), true, out error)) return false;
            driverNameProcessId = pid;
        }
        IntPtr process = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite, false, pid);
        if (process == IntPtr.Zero) { error = "Cannot access the game."; return false; }
        try
        {
            IntPtr cave = Allocations["Driver Name"];
            IntPtr state = IntPtr.Add(cave, InjectionCatalog.DriverNameStateOffset);
            var pending = new byte[4];
            IntPtr count;
            if (!ReadProcessMemory(process, state, pending, pending.Length, out count) || count.ToInt32() != pending.Length)
            { error = "Cannot read request status."; return false; }
            if (BitConverter.ToInt32(pending, 0) != 0)
            { error = "Open a car's tuning screen to apply the queued name."; return false; }
            if (!WriteProcessMemory(process, IntPtr.Add(cave, InjectionCatalog.DriverNameTextOffset), encoded, encoded.Length, out count)
                || count.ToInt32() != encoded.Length
                || !WriteProcessMemory(process, state, BitConverter.GetBytes(1), 4, out count) || count.ToInt32() != 4)
            { error = "Cannot send the name to the game."; return false; }
            error = "";
            return true;
        }
        finally { CloseHandle(process); }
    }

    internal static bool SetUnlimitedBoosterMask(int mask, out string error)
    {
        error = "";
        var games = Process.GetProcessesByName("Asphalt8");
        if (games.Length != 1) { error = "Open exactly one Asphalt 8 instance."; return false; }
        int pid = games[0].Id;
        if (boosterProcessId != pid)
        {
            if (!Toggle(InjectionCatalog.Find("Unlimited Booster Timers"), true, out error)) return false;
            boosterProcessId = pid;
        }
        IntPtr process = OpenProcess(ProcessVmOperation | ProcessVmWrite, false, pid);
        if (process == IntPtr.Zero) { error = "Cannot access the game's boosters."; return false; }
        try
        {
            IntPtr address = IntPtr.Add(Allocations["Unlimited Booster Timers"], InjectionCatalog.UnlimitedBoosterMaskOffset);
            IntPtr written;
            if (WriteProcessMemory(process, address, BitConverter.GetBytes(mask), 4, out written) && written.ToInt32() == 4)
                return true;
            error = "Cannot update boosters.";
            return false;
        }
        finally { CloseHandle(process); }
    }

    internal static bool SetWalletBalance(bool gems, int amount, out string error)
    {
        return QueueWalletCommand(gems ? 1 : 0, amount, out error);
    }

    internal static bool SetFusionBalance(int amount, out string error)
    {
        return QueueWalletCommand(InjectionCatalog.SetFusionBalanceCommand, amount, out error);
    }

    internal static bool BuySelectedCar(out string error)
    {
        return QueueWalletCommand(2, 1, out error);
    }

    internal static bool QuoteSelectedCar(out string error)
    {
        return QueueWalletCommand(3, 0, out error);
    }

    internal static bool BuyQuotedCar(out string error)
    {
        return QueueWalletCommand(4, 0, out error);
    }

    internal static bool ReadSaleSelection(out string error)
    {
        return QueueWalletCommand(5, 0, out error);
    }

    internal static bool SellSelectedCar(int amount, bool gems, out string error)
    {
        if(amount<=0) { error="The receipt must contain a positive amount."; return false; }
        return QueueWalletCommand(6, amount, out error, gems?1:0);
    }

    internal static bool ReadQuotedCarPrice(out int amount, out bool gems)
    {
        int id;
        return ReadCarReceipt(out id,out amount,out gems) && amount>0;
    }

    internal static bool ReadCarReceipt(out int id, out int amount, out bool gems)
    {
        id=0; amount=0; gems=false;
        IntPtr process = OpenProcess(ProcessVmRead, false, walletProcessId);
        if (process == IntPtr.Zero) return false;
        try
        {
            var quote = new byte[12];
            IntPtr address = IntPtr.Add(Allocations["Wallet Editor"], InjectionCatalog.WalletCommandOffset+12);
            IntPtr read;
            if(!ReadProcessMemory(process,address,quote,quote.Length,out read) || read.ToInt32()!=quote.Length) return false;
            id=BitConverter.ToInt32(quote,0);
            amount=BitConverter.ToInt32(quote,4);
            int currency=BitConverter.ToInt32(quote,8);
            gems=currency==1;
            return currency==0 || currency==1;
        }
        finally { CloseHandle(process); }
    }

    private static bool QueueWalletCommand(int command, int amount, out string error, int? refundCurrency = null)
    {
        error = "";
        var games = Process.GetProcessesByName("Asphalt8");
        if (games.Length != 1) { error = "Open exactly one Asphalt 8 instance."; return false; }
        int pid = games[0].Id;
        if (walletProcessId != pid)
        {
            if (!Toggle(InjectionCatalog.Find("Wallet Editor"), true, out error)) return false;
            walletProcessId = pid;
        }
        IntPtr process = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite, false, pid);
        if (process == IntPtr.Zero) { error = "Cannot access the game's balance."; return false; }
        try
        {
            IntPtr address = IntPtr.Add(Allocations["Wallet Editor"], InjectionCatalog.WalletCommandOffset);
            int state;
            if (!ReadWalletCommand(process, address, out state)) { error = "Cannot read balance status."; return false; }
            if (state == 1 || state == 2) { error = "A balance change is already pending."; return false; }
            var data = new byte[8];
            Array.Copy(BitConverter.GetBytes(command), data, 4);
            Array.Copy(BitConverter.GetBytes(amount), 0, data, 4, 4);
            IntPtr written;
            if (refundCurrency.HasValue &&
                (!WriteProcessMemory(process,IntPtr.Add(address,20),BitConverter.GetBytes(refundCurrency.Value),4,out written) || written.ToInt32()!=4))
            { error="Cannot send the receipt currency."; return false; }
            if (!WriteProcessMemory(process, IntPtr.Add(address, 4), data, data.Length, out written)
                || written.ToInt32() != data.Length
                || !WriteProcessMemory(process, address, BitConverter.GetBytes(1), 4, out written)
                || written.ToInt32() != 4)
            { error = "Cannot send the new balance."; return false; }
            return true;
        }
        finally { CloseHandle(process); }
    }

    internal static int WalletCommandState()
    {
        var games = Process.GetProcessesByName("Asphalt8");
        if (games.Length != 1 || games[0].Id != walletProcessId) return -1;
        IntPtr process = OpenProcess(ProcessVmRead, false, walletProcessId);
        if (process == IntPtr.Zero) return -1;
        try
        {
            IntPtr address = IntPtr.Add(Allocations["Wallet Editor"], InjectionCatalog.WalletCommandOffset);
            int state;
            return ReadWalletCommand(process, address, out state) ? state : -1;
        }
        finally { CloseHandle(process); }
    }

    private static bool ReadWalletCommand(IntPtr process, IntPtr address, out int state)
    {
        var bytes = new byte[4];
        IntPtr read;
        bool ok = ReadProcessMemory(process, address, bytes, bytes.Length, out read) && read.ToInt32() == bytes.Length;
        state = ok ? BitConverter.ToInt32(bytes, 0) : -1;
        return ok;
    }

    private static bool Equal(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    private static bool EqualMasked(byte[] a,byte[] b,bool[] mask){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(mask[i]&&a[i]!=b[i])return false;return true;}
}

internal static class NativeQuickRaceMapEngine
{
    private const uint Access=0x0008|0x0010|0x0020, Commit=0x1000, Reserve=0x2000, Release=0x8000, Rwx=0x40;
    private static IntPtr cave=IntPtr.Zero; private static byte[] saved;
    [StructLayout(LayoutKind.Sequential)] private struct Mbi { public IntPtr BaseAddress,AllocationBase; public uint AllocationProtect; public UIntPtr RegionSize; public uint State,Protect,Type; }
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint a,bool i,int p);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool ReadProcessMemory(IntPtr p,IntPtr a,byte[] b,int s,out IntPtr r);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteProcessMemory(IntPtr p,IntPtr a,byte[] b,int s,out IntPtr w);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr VirtualAllocEx(IntPtr p,IntPtr a,UIntPtr s,uint t,uint pr);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool VirtualFreeEx(IntPtr p,IntPtr a,UIntPtr s,uint t);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool VirtualProtectEx(IntPtr p,IntPtr a,UIntPtr s,uint n,out uint o);
    [DllImport("kernel32.dll")] static extern int VirtualQueryEx(IntPtr p,IntPtr a,out Mbi i,int s);
    [DllImport("kernel32.dll")] static extern bool FlushInstructionCache(IntPtr p,IntPtr a,UIntPtr s);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    internal static bool Toggle(bool enable,out string error)
    {
        error=""; Process[] games=Process.GetProcessesByName("Asphalt8"); if(games.Length==0){error="game not found";return false;}
        Process game=games[0]; IntPtr h=OpenProcess(Access,false,game.Id); if(h==IntPtr.Zero){error="OpenProcess "+Marshal.GetLastWin32Error();return false;}
        try
        {
            long m=game.MainModule.BaseAddress.ToInt64(), siteAddress=m+0xB5A1A5; IntPtr site=new IntPtr(siteAddress), count;
            if(!enable)
            {
                if(saved==null){error="module is inactive";return false;} uint old; VirtualProtectEx(h,site,(UIntPtr)saved.Length,Rwx,out old);
                bool ok=WriteProcessMemory(h,site,saved,saved.Length,out count); uint ignored; VirtualProtectEx(h,site,(UIntPtr)saved.Length,old,out ignored);
                if(cave!=IntPtr.Zero)VirtualFreeEx(h,cave,UIntPtr.Zero,Release); cave=IntPtr.Zero;saved=null;FlushInstructionCache(h,site,(UIntPtr)7);return ok;
            }
            byte[] original=new byte[7]; if(!ReadProcessMemory(h,site,original,7,out count)||!Eq(original,new byte[]{0x8D,0x4D,0x08,0xC6,0x45,0xFC,0x01})){error="unexpected signature";return false;}
            string[] names={"EventDef_Spc_01","EventDef_Spc_01_Reverse","EventDef_Spc_02","EventDef_Spc_02_Reverse"}; long[] strings=new long[4];
            for(int i=0;i<4;i++){strings[i]=FindAscii(h,names[i]);if(strings[i]==0){error="open Quick Race and try again ("+names[i]+" missing)";return false;}}
            cave=VirtualAllocEx(h,IntPtr.Zero,(UIntPtr)0x1000,Commit|Reserve,Rwx); if(cave==IntPtr.Zero){error="remote allocation failed";return false;}
            long c=cave.ToInt64(); var code=new List<byte>();
            for(int i=0;i<4;i++)
            {
                Add(code,"C7 45 08 00 00 00 00 68"); Add32(code,(int)strings[i]); Add(code,"8D 4D 08 C6 45 FC ");code.Add((byte)(0x74+2*i)); Call(code,c+code.Count,m+0xEA4F90);
                Add(code,"8D 45 08 C6 45 FC ");code.Add((byte)(0x75+2*i));Add(code,"50 8B CB");Call(code,c+code.Count,m+0x133CF0);Add(code,"8D 4D 08");if(i<3)Call(code,c+code.Count,m+0xBCD00);
            }
            Add(code,"8D 4D 08 C6 45 FC 01");Jump(code,c+code.Count,siteAddress+7);
            if(!WriteProcessMemory(h,cave,code.ToArray(),code.Count,out count)){VirtualFreeEx(h,cave,UIntPtr.Zero,Release);cave=IntPtr.Zero;error="cannot write code";return false;}
            byte[] patch={0xE9,0,0,0,0,0x90,0x90};Array.Copy(BitConverter.GetBytes((int)(c-(siteAddress+5))),0,patch,1,4);
            uint prot;VirtualProtectEx(h,site,(UIntPtr)7,Rwx,out prot);bool wrote=WriteProcessMemory(h,site,patch,7,out count);uint back;VirtualProtectEx(h,site,(UIntPtr)7,prot,out back);
            if(!wrote){VirtualFreeEx(h,cave,UIntPtr.Zero,Release);cave=IntPtr.Zero;error="cannot write branch";return false;} saved=original;FlushInstructionCache(h,site,(UIntPtr)7);return true;
        }
        finally{CloseHandle(h);}
    }
    private static long FindAscii(IntPtr h,string text)
    {
        byte[] needle=System.Text.Encoding.ASCII.GetBytes(text+"\0"); long a=0x10000;
        while(a<0x7FFF0000){Mbi mbi;if(VirtualQueryEx(h,new IntPtr(a),out mbi,Marshal.SizeOf(typeof(Mbi)))==0)break;long size=(long)mbi.RegionSize.ToUInt64();
            if(mbi.State==0x1000&&(mbi.Protect&0x100)==0&&(mbi.Protect&1)==0&&size>0&&size<=0x4000000){byte[] data=new byte[(int)size];IntPtr read;if(ReadProcessMemory(h,mbi.BaseAddress,data,data.Length,out read)){int at=Index(data,needle);if(at>=0)return mbi.BaseAddress.ToInt64()+at;}}
            long next=mbi.BaseAddress.ToInt64()+Math.Max(size,0x1000);if(next<=a)break;a=next;}return 0;
    }
    private static int Index(byte[] h,byte[] n){for(int i=0;i<=h.Length-n.Length;i++){int j=0;for(;j<n.Length&&h[i+j]==n[j];j++);if(j==n.Length)return i;}return -1;}
    private static void Add(List<byte>b,string x){b.AddRange(InjectionHack.Hex(x));} private static void Add32(List<byte>b,int v){b.AddRange(BitConverter.GetBytes(v));}
    private static void Call(List<byte>b,long at,long target){b.Add(0xE8);Add32(b,(int)(target-(at+5)));} private static void Jump(List<byte>b,long at,long target){b.Add(0xE9);Add32(b,(int)(target-(at+5)));}
    private static bool Eq(byte[]a,byte[]b){for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
}
