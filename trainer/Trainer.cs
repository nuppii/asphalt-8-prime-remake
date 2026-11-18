using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var english = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Threading.Thread.CurrentThread.CurrentCulture = english;
        System.Threading.Thread.CurrentThread.CurrentUICulture = english;
        bool firstInstance;
        using (var instance = new System.Threading.Mutex(false, @"Local\Asphalt8OfflineTrainer_0pp20fcewvvtj", out firstInstance))
        {
            if (!firstInstance)
            {
                TrainerContext.LaunchGame();
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool showMenu = Array.Exists(Environment.GetCommandLineArgs(), arg =>
                string.Equals(arg, "--show", StringComparison.OrdinalIgnoreCase));
            Application.Run(new TrainerContext(showMenu));
        }
    }
}

internal sealed class TrainerContext : ApplicationContext
{
    private const string AppId = "YoanRochat.Asphalt8Airborne480Offline_0pp20fcewvvtj!App";
    private readonly TrainerForm trainer;
    private readonly NotifyIcon tray;

    public TrainerContext(bool showMenu)
    {
        trainer = new TrainerForm();
        // Force creation of the native window handle so RegisterHotKey runs,
        // then keep the overlay hidden until Insert is pressed.
        trainer.Show();
        if (!showMenu)
            trainer.Hide();

        tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Asphalt 8 Offline Trainer",
            Visible = true,
            ContextMenu = new ContextMenu(new[]
            {
                new MenuItem("Show / hide (Insert)", (_, __) => trainer.ToggleVisible()),
                new MenuItem("Launch game", (_, __) => LaunchGame()),
                new MenuItem("Exit trainer", (_, __) => ExitThread())
            })
        };
        tray.DoubleClick += (_, __) => trainer.ToggleVisible();
        LaunchGame();
    }

    internal static void LaunchGame()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "shell:AppsFolder\\" + AppId,
            UseShellExecute = true
        });
    }

    protected override void ExitThreadCore()
    {
        tray.Visible = false;
        tray.Dispose();
        trainer.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class TrainerForm : Form
{
    private const int HotkeyId = 0xA848;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkInsert = 0x2D;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint PageExecuteReadWrite = 0x40;
    private const int VehicleVisibilityRva = 0x257360;
    private const int TryAllVehiclesRva = 0x32D756;
    private static readonly byte[] VehicleVisibilityOriginal = { 0x55, 0x8B, 0xEC, 0x6A, 0xFF, 0x68, 0x58, 0x84 };
    private static readonly byte[] VehicleVisibilityPatch = { 0xB0, 0x01, 0xC2, 0x04, 0x00, 0x90, 0x90, 0x90 };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtectEx(IntPtr process, IntPtr address, UIntPtr size, uint newProtection, out uint oldProtection);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private readonly Color background = Color.FromArgb(21, 25, 34);
    private readonly Color panel = Color.FromArgb(30, 36, 48);
    private readonly Color panelHover = Color.FromArgb(40, 48, 63);
    private readonly Color accent = Color.FromArgb(0, 203, 255);
    private readonly Color muted = Color.FromArgb(145, 158, 178);
    private readonly Label title;
    private readonly Label sectionTitle;
    private readonly Label subtitle;
    private readonly Label status;
    private readonly FlowLayoutPanel options;
    private readonly Dictionary<string, bool> importedStates = new Dictionary<string, bool>();
    private readonly HashSet<string> installedRaceSettings = new HashSet<string>();
    private readonly HashSet<string> savedTools = new HashSet<string>();
    private readonly Dictionary<string,int> savedValues = new Dictionary<string,int>();
    private static readonly string[] MutatorNames =
    {
        "Acceleration", "Top speed", "Nitro power", "Air rotation", "Drift radius", "Steering response", "Braking"
    };
    private readonly float[,] mutatorValues = new float[2, 7];
    private readonly bool[,] mutatorEnabled = new bool[2, 7];
    private readonly bool[,] mutatorApplied = new bool[2, 7];
    private readonly Timer stateMonitor = new Timer();
    private int observedGamePid;
    private bool walletEditPending;
    private static readonly string[] UnlimitedBoosterNames =
    {
        "Unlimited double credits", "Unlimited nitro starter", "Unlimited tuning kit", "Unlimited extra tank"
    };
    private readonly Dictionary<string, string> favorites = new Dictionary<string, string>();
    private string currentSection = "GARAGE & VEHICLES";
    private Point dragOrigin;
    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "_offline_patch_backup", "tools", "trainer.log");
    private static readonly string FavoritesPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "_offline_patch_backup", "tools", "favorites.txt");
    private static readonly string ToolStatesPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "_offline_patch_backup", "tools", "active-tools.txt");
    private static readonly string CarReceiptsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "_offline_patch_backup", "tools", "car-receipts.txt");
    private static readonly string MutatorSettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "_offline_patch_backup", "tools", "mutators.txt");

    public TrainerForm()
    {
        LoadToolStates();
        LoadMutatorSettings();
        Text = "Asphalt 8 Offline Trainer";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1050, 680);
        BackColor = background;
        ForeColor = Color.White;
        Opacity = 0.94;
        TopMost = true;
        // Expose the overlay as a real top-level window while visible. This is
        // required for reliable focus/activation over the UWP game window.
        ShowInTaskbar = true;
        KeyPreview = true;
        DoubleBuffered = true;
        Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 22, 22));

        var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Color.FromArgb(24, 29, 39) };
        header.MouseDown += BeginDrag;
        header.MouseMove += ContinueDrag;
        Controls.Add(header);

        title = new Label
        {
            AutoSize = true,
            Text = "A8  •  OFFLINE LAB",
            Font = new Font("Segoe UI Semibold", 17f),
            ForeColor = Color.White,
            Location = new Point(26, 19)
        };
        title.MouseDown += BeginDrag;
        title.MouseMove += ContinueDrag;
        header.Controls.Add(title);

        var build = new Label
        {
            AutoSize = true,
            Text = "4.8.0.7  /  SOLO",
            Font = new Font("Segoe UI", 9f),
            ForeColor = accent,
            Location = new Point(255, 26)
        };
        header.Controls.Add(build);

        var close = MakeHeaderButton("×", 1000);
        close.Click += (_, __) => Hide();
        header.Controls.Add(close);

        var sidebar = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            Width = 275,
            Padding = new Padding(15, 18, 15, 15),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.FromArgb(25, 30, 41)
        };
        Controls.Add(sidebar);

        AddSection(sidebar, "GARAGE & VEHICLES", true);
        AddSection(sidebar, "★ FAVORITES", false);
        AddSection(sidebar, "CURRENCY & INVENTORY", false);
        AddSection(sidebar, "UPGRADES", false);
        AddSection(sidebar, "RACING", false);
        AddSection(sidebar, "IN-GAME MUTATORS", false);
        AddSection(sidebar, "AI & OPPONENTS", false);
        AddSection(sidebar, "CUSTOM RACES", false);
        AddSection(sidebar, "PROGRESSION", false);
        AddSection(sidebar, "CAMERA & DISPLAY", false);
        AddSection(sidebar, "SYSTEM", false);
        LoadFavorites();

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30, 24, 30, 20), BackColor = background };
        Controls.Add(content);
        content.BringToFront();

        sectionTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 52,
            Text = "Garage & vehicles",
            Font = new Font("Segoe UI Semibold", 22f),
            ForeColor = Color.White
        };
        content.Controls.Add(sectionTitle);

        subtitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            Text = "Local garage controls. Unverified options remain locked for build 4.8.0.7.",
            Font = new Font("Segoe UI", 10f),
            ForeColor = muted
        };
        content.Controls.Add(subtitle);

        status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            Text = "INSERT  Show / hide    •    Local garage patch engine",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = accent
        };
        content.Controls.Add(status);

        options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 8, 8, 8),
            BackColor = background
        };
        content.Controls.Add(options);
        options.BringToFront();

        string[] garageOptions =
        {
            InjectionCatalog.CareerCarChoice,
            "Unlock all cars",
            "Unlock all motorcycles",
            "Add selected car to garage",
            "Remove selected car from garage",
            "Unlock all classes",
            "Unlock all blueprints",
            "Unlock all collections",
            "Unlock all mastery events",
            "Unlock all paint colors",
            "Unlock all decals",
            "Unlock all wheels",
            "Unlock all visual parts",
            "Choose primary color",
            "Choose secondary color",
            "Allow all colors",
            "Duplicate car configurations",
            "Switch vehicles instantly",
            "Ignore rank restrictions",
            "Show hidden or unused vehicles",
            "Restore a removed vehicle",
            "Export car configuration",
            "Import car configuration",
            "Copy settings to another car"
        };

        foreach (string option in garageOptions)
        {
            if (option == InjectionCatalog.CareerCarChoice)
                options.Controls.Add(MakeInjectionOption(option, "SOLO CAREER • REOPEN THE CAR SELECTOR"));
            else if (option == "Unlock all cars")
                options.Controls.Add(MakeTryAllVehiclesOption(option));
            else if (option == "Show hidden or unused vehicles")
                options.Controls.Add(MakeVehicleVisibilityOption(option));
            else
                options.Controls.Add(MakeOption(option));
        }

        stateMonitor.Interval=2000;
        stateMonitor.Tick += (_,__) => RestoreToolsForNewGame();
        stateMonitor.Start();

    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        bool registered = RegisterHotKey(Handle, HotkeyId, ModNoRepeat, VkInsert);
        Log("RegisterHotKey=" + registered + " Win32Error=" + Marshal.GetLastWin32Error());
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, HotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId)
        {
            Log("Insert received; visible=" + Visible);
            ToggleVisible();
            return;
        }
        base.WndProc(ref message);
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.AppendAllText(LogPath, DateTime.Now.ToString("O") + " " + message + Environment.NewLine);
        }
        catch { }
    }

    public void ToggleVisible()
    {
        if (Visible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
            BringToFront();
        }
    }

    private Button MakeHeaderButton(string text, int x)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            ForeColor = muted,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 18f),
            Size = new Size(42, 42),
            Location = new Point(x, 14),
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private void AddSection(Control sidebar, string name, bool active)
    {
        var button = new Button
        {
            Text = (active ? "▾  " : "›  ") + name,
            UseMnemonic = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Size = new Size(238, 48),
            Margin = new Padding(0, 0, 0, 7),
            Padding = new Padding(12, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = active ? Color.White : muted,
            BackColor = active ? Color.FromArgb(38, 48, 63) : Color.Transparent,
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, __) => ShowSection(name);
        sidebar.Controls.Add(button);
    }

    private void ShowSection(string name)
    {
        currentSection = name;
        options.SuspendLayout();
        options.Controls.Clear();
        sectionTitle.Text = ToTitle(name);
        sectionTitle.UseMnemonic = false;
        subtitle.Text = name == "IN-GAME MUTATORS"
            ? "Independent player and bot physics • 0–100× • changes apply during the race."
            : "Local garage controls. Unverified options remain locked for build 4.8.0.7.";

        if (name == "★ FAVORITES")
        {
            if(favorites.Count==0) options.Controls.Add(MakePendingOption("No favorites yet", "CLICK THE STAR NEXT TO A TOOL"));
            else foreach(var item in new List<KeyValuePair<string,string>>(favorites)) options.Controls.Add(MakeFavoriteShortcut(item.Key,item.Value));
        }
        else if (name == "GARAGE & VEHICLES")
        {
            options.Controls.Add(MakeInjectionOption(InjectionCatalog.CareerCarChoice, "SOLO CAREER • REOPEN THE CAR SELECTOR"));
            options.Controls.Add(MakeTryAllVehiclesOption("Unlock all cars"));
            options.Controls.Add(MakeVehicleVisibilityOption("Show hidden or unused vehicles"));
            options.Controls.Add(MakeImportedOption("Change Cars Textures"));
        }
        else if (name == "CURRENCY & INVENTORY")
        {
            options.Controls.Add(MakeWalletBalanceOption(false));
            options.Controls.Add(MakeWalletBalanceOption(true));
            options.Controls.Add(MakeWalletBalanceOption(false, true));
            options.Controls.Add(MakeCarPurchaseOption(false));
            options.Controls.Add(MakeCarPurchaseOption(true));
            options.Controls.Add(MakeCarSaleOption());
            options.Controls.Add(MakeInjectionOption("Free Multiplayer Tickets"));
            for (int booster=0; booster<UnlimitedBoosterNames.Length; booster++)
                options.Controls.Add(MakeUnlimitedBoosterOption(booster));
            options.Controls.Add(MakeValueInjectionOption("Booster Duration", 1, 86400));
            options.Controls.Add(MakeInjectionOption("Clear Booster Time"));
        }
        else if (name == "UPGRADES")
        {
            options.Controls.Add(MakeInjectionOption(InjectionCatalog.OfflineProUpgrade, "STANDARD FUSION COST • CARDS UNAVAILABLE OFFLINE"));
            options.Controls.Add(MakeInjectionOption("Max All Cars"));
            options.Controls.Add(MakeInjectionOption("Pro All Cars"));
            options.Controls.Add(MakeInjectionOption("Max R&D Car"));
            options.Controls.Add(MakeInjectionOption("Pro R&D Car"));
            options.Controls.Add(MakeInjectionOption("Try All Decals"));
        }
        else if (name == "RACING")
        {
            options.Controls.Add(MakeImportedOption("Perfect Nitro"));
            options.Controls.Add(MakeImportedOption("Nitro Refill"));
            options.Controls.Add(MakeImportedOption("Classic Finish"));
            options.Controls.Add(MakeImportedOption("Elimination Finish"));
            options.Controls.Add(MakeImportedOption("Infected Finish"));
            options.Controls.Add(MakeImportedOption("Drift Finish"));
            options.Controls.Add(MakeInjectionOption("Knockdown Finish"));
            options.Controls.Add(MakeInjectionOption("Triple Nitro"));
            options.Controls.Add(MakeInjectionOption("Triple Nitro v2"));
            options.Controls.Add(MakeImportedOption("Perfect Nitro 1 Click"));
            options.Controls.Add(MakeImportedOption("Safe Crash"));
            options.Controls.Add(MakeInjectionOption("No Crash"));
            options.Controls.Add(MakeInjectionOption("Stop Time"));
            options.Controls.Add(MakeInjectionOption("Unlimited Nitro"));
            options.Controls.Add(MakeInjectionOption("Unlimited Drift"));
            options.Controls.Add(MakeInjectionOption("Unlimited Speed"));
            options.Controls.Add(MakeDualValueInjectionOption("Nitro Ratio", "Nitro Ratio A", "Nitro Ratio B", 1, 20));
            options.Controls.Add(MakeInjectionOption("Boost Mode"));
            options.Controls.Add(MakeInjectionOption("Intangible Vehicles"));
        }
        else if (name == "IN-GAME MUTATORS")
        {
            AddMutatorGroup(0, "PLAYER");
            AddMutatorGroup(1, "BOTS");
        }
        else if (name == "AI & OPPONENTS")
        {
            options.Controls.Add(MakeImportedOption("Ultimate AI Finish"));
            options.Controls.Add(MakeInjectionOption("Automatic Driver"));
            options.Controls.Add(MakeInjectionOption("Stop Vehicles"));
            options.Controls.Add(MakeInjectionOption("No Traffic"));
            options.Controls.Add(MakeInjectionOption("Ghost Mode"));
        }
        else if (name == "CUSTOM RACES")
        {
            options.Controls.Add(MakeRaceSettingOption("Laps", "Lap count", 999999));
            options.Controls.Add(MakeRaceSettingOption("Racers", "Racer count", 61));
            options.Controls.Add(MakeQuickRaceMapsOption());
        }
        else if (name == "PROGRESSION")
        {
            options.Controls.Add(MakeImportedOption(CheatTableForm.UnlockCareer, "REOPEN PLAY OR THE SEASON LIST TO REFRESH"));
            options.Controls.Add(MakeDriverNameOption());
            options.Controls.Add(MakeImportedOption("Unlimited Mastery Rewards"));
            options.Controls.Add(MakeInjectionOption("Unlock Mastery"));
            options.Controls.Add(MakeInjectionOption("Unlock R&D Labs"));
            options.Controls.Add(MakeInjectionOption("Unlock Mastery Moto"));
            options.Controls.Add(MakeInjectionOption("Easy Events"));
            options.Controls.Add(MakeInjectionOption("5 Stars Hack"));
        }
        else if (name == "CAMERA & DISPLAY")
        {
            options.Controls.Add(MakeImportedOption("Christmas Tracks"));
            options.Controls.Add(MakeImportedOption("Bubbles Effects"));
            options.Controls.Add(MakeInjectionOption("Hide HUD"));
            options.Controls.Add(MakeInjectionOption("Invisible Vehicles"));
            options.Controls.Add(MakeInjectionOption("WallHack"));
        }
        else
        {
            options.Controls.Add(MakePendingOption("Patch engine 4.8.0.7", "ACTIVE"));
        }

        status.Text = ToTitle(name) + "  •  integrated trainer tools";
        options.ResumeLayout();
    }

    private void AddMutatorGroup(int target, string titleText)
    {
        var heading = new Panel { Width=690, Height=49, BackColor=background, Margin=new Padding(0,8,0,5) };
        heading.Controls.Add(new Label { Text=titleText, Font=new Font("Segoe UI Semibold",12f), ForeColor=accent,
            Location=new Point(7,1), AutoSize=true });
        heading.Controls.Add(new Label { Text="0.00–100.00×  •  1.00× = original physics", Font=new Font("Segoe UI",8.5f),
            ForeColor=muted, Location=new Point(8,26), AutoSize=true });
        options.Controls.Add(heading);
        for (int parameter=0; parameter<MutatorNames.Length; parameter++)
            options.Controls.Add(MakeMutatorOption(target,parameter));

        var footer = new Panel { Width=690, Height=39, BackColor=background, Margin=new Padding(0,0,0,14) };
        var reset = new Button { Text="Reset "+(target==0?"player":"bots"), FlatStyle=FlatStyle.Flat,
            Font=new Font("Segoe UI Semibold",8.5f), ForeColor=muted, BackColor=panel,
            Location=new Point(8,2), Size=new Size(126,30), TabStop=false };
        reset.FlatAppearance.BorderColor=Color.FromArgb(67,78,96);
        reset.Click += (_,__) =>
        {
            RestoreToolsForNewGame();
            string error;
            if (observedGamePid!=0 && !MutatorEngine.ResetTarget(target,out error))
            {
                status.Text="Could not reset "+(target==0?"player":"bots")+": "+error;
                return;
            }
            for (int i=0;i<MutatorNames.Length;i++)
            {
                mutatorEnabled[target,i]=false;
                mutatorApplied[target,i]=false;
                mutatorValues[target,i]=1f;
            }
            SaveMutatorSettings();
            ShowSection(currentSection);
            status.Text=(target==0?"Player":"Bot")+" mutators reset • original physics restored";
        };
        footer.Controls.Add(reset);
        footer.Controls.Add(new Label { Text="Disables this group's mutators and restores every multiplier to 1.00×.",
            Font=new Font("Segoe UI",8f), ForeColor=muted, AutoSize=true, Location=new Point(147,10) });
        options.Controls.Add(footer);
    }

    private Control MakeMutatorOption(int target, int parameter)
    {
        string targetName=target==0?"Player":"Bots";
        string label=MutatorNames[parameter];
        bool requested=mutatorEnabled[target,parameter];
        bool applied=mutatorApplied[target,parameter];
        var row=new Panel { Width=690, Height=66, BackColor=panel, Margin=new Padding(0,0,0,7) };
        row.Controls.Add(new Label { Text=label, Font=new Font("Segoe UI Semibold",10f), ForeColor=Color.White,
            Location=new Point(18,8), Size=new Size(268,24) });
        string detail=!requested?"DISABLED • ORIGINAL PHYSICS":applied?"ACTIVE • "+mutatorValues[target,parameter].ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+"×":"NOT APPLIED • WAITING FOR GAME";
        row.Controls.Add(new Label { Text=detail, Font=new Font("Segoe UI Semibold",7.5f),
            ForeColor=applied?Color.FromArgb(84,222,150):muted, Location=new Point(18,38), AutoSize=true });
        var value=new NumericUpDown { Minimum=0m, Maximum=100m, DecimalPlaces=2, Increment=0.10m,
            Value=(decimal)mutatorValues[target,parameter], Location=new Point(290,17), Size=new Size(90,30),
            Font=new Font("Segoe UI Semibold",10f), BackColor=Color.FromArgb(25,30,40), ForeColor=Color.White };
        var apply=new Button { Text="APPLY", FlatStyle=FlatStyle.Flat, Font=new Font("Segoe UI Semibold",8.5f),
            ForeColor=Color.White, BackColor=Color.FromArgb(0,122,160), Location=new Point(384,14), Size=new Size(98,36), TabStop=false };
        var toggle=new Button { Text=requested?"DISABLE":"ENABLE", FlatStyle=FlatStyle.Flat,
            Font=new Font("Segoe UI Semibold",8.5f), ForeColor=Color.White,
            BackColor=applied?Color.FromArgb(22,145,92):Color.FromArgb(0,122,160),
            Location=new Point(525,14), Size=new Size(145,36), TabStop=false };
        apply.FlatAppearance.BorderColor=accent;
        toggle.FlatAppearance.BorderColor=accent;
        Action<bool> commit = toggleRequested =>
        {
            float selected=(float)value.Value;
            RestoreToolsForNewGame();
            bool enable=toggleRequested?!mutatorEnabled[target,parameter]:mutatorEnabled[target,parameter];
            string error;
            if (observedGamePid==0 && enable)
            {
                status.Text="Start the game before enabling "+targetName.ToLowerInvariant()+" mutators.";
                return;
            }
            if (observedGamePid!=0 && (enable || mutatorApplied[target,parameter]) &&
                !MutatorEngine.Configure(target,parameter,enable,selected,out error))
            {
                status.Text=targetName+" • "+label+": "+error;
                return;
            }
            mutatorValues[target,parameter]=selected;
            mutatorEnabled[target,parameter]=enable;
            mutatorApplied[target,parameter]=enable;
            SaveMutatorSettings();
            ShowSection(currentSection);
            status.Text=targetName+" • "+label+(enable?" = "+selected.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+"×":toggleRequested?" disabled • original physics restored":" multiplier saved");
        };
        apply.Click += (_,__) => commit(false);
        toggle.Click += (_,__) => commit(true);
        row.Controls.Add(value);
        row.Controls.Add(apply);
        row.Controls.Add(toggle);
        AddFavoriteButton(row,targetName+" • "+label);
        return row;
    }

    private void LoadMutatorSettings()
    {
        for (int target=0;target<2;target++)
            for (int parameter=0;parameter<MutatorNames.Length;parameter++) mutatorValues[target,parameter]=1f;
        try
        {
            if (!File.Exists(MutatorSettingsPath)) return;
            foreach (string line in File.ReadAllLines(MutatorSettingsPath))
            {
                string[] parts=line.Split('|');
                int target,parameter;
                float value;
                if (parts.Length!=4 || !int.TryParse(parts[0],out target) || !int.TryParse(parts[1],out parameter) ||
                    target<0 || target>1 || parameter<0 || parameter>=MutatorNames.Length ||
                    !float.TryParse(parts[3],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value) ||
                    float.IsNaN(value) || float.IsInfinity(value) || value<0f || value>100f || (parts[2]!="0" && parts[2]!="1")) continue;
                mutatorEnabled[target,parameter]=parts[2]=="1";
                mutatorValues[target,parameter]=value;
            }
        }
        catch (IOException ex) { Log("Mutator settings could not be read: "+ex.Message); }
        catch (UnauthorizedAccessException ex) { Log("Mutator settings unavailable: "+ex.Message); }
    }

    private void SaveMutatorSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MutatorSettingsPath));
            var lines=new List<string>();
            for (int target=0;target<2;target++)
                for (int parameter=0;parameter<MutatorNames.Length;parameter++)
                    lines.Add(target+"|"+parameter+"|"+(mutatorEnabled[target,parameter]?"1":"0")+"|"+
                        mutatorValues[target,parameter].ToString("R",System.Globalization.CultureInfo.InvariantCulture));
            File.WriteAllLines(MutatorSettingsPath,lines.ToArray());
        }
        catch (IOException ex) { Log("Mutator settings could not be saved: "+ex.Message); }
        catch (UnauthorizedAccessException ex) { Log("Mutator settings could not be saved: "+ex.Message); }
    }

    private void RestoreMutatorsForNewGame()
    {
        Array.Clear(mutatorApplied,0,mutatorApplied.Length);
        bool requested=false;
        foreach (bool enabled in mutatorEnabled) if (enabled) { requested=true; break; }
        if (!requested) return;
        string error;
        if (!MutatorEngine.EnsureInstalled(out error)) { Log("Mutator engine: "+error); return; }
        for (int target=0;target<2;target++)
            for (int parameter=0;parameter<MutatorNames.Length;parameter++)
            {
                if (!mutatorEnabled[target,parameter]) continue;
                if (MutatorEngine.Configure(target,parameter,true,mutatorValues[target,parameter],out error))
                    mutatorApplied[target,parameter]=true;
                else Log((target==0?"Player":"Bots")+" mutator "+MutatorNames[parameter]+": "+error);
            }
    }

    private Control MakeDriverNameOption()
    {
        var row = new Panel { Width = 690, Height = 65, BackColor = panel, Margin = new Padding(0, 0, 0, 8) };
        row.Controls.Add(new Label { Text = "Driver name", ForeColor = Color.White, AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10f), Location = new Point(18, 9) });
        row.Controls.Add(new Label { Text = "Then open a car's tuning screen", ForeColor = muted,
            AutoSize = true, Location = new Point(18, 34) });
        var name = new TextBox { MaxLength = 24, Location = new Point(289, 19), Width = 220,
            Font = new Font("Segoe UI", 10f), ForeColor = Color.White, BackColor = Color.FromArgb(25, 30, 40) };
        var apply = new Button { Text = "APPLY", Location = new Point(525, 13), Size = new Size(145, 36),
            ForeColor = Color.White, BackColor = Color.FromArgb(0, 122, 160), FlatStyle = FlatStyle.Flat };
        apply.Click += (_, __) =>
        {
            string value = name.Text.Trim();
            if (value.Length == 0 || Array.Exists(value.ToCharArray(), char.IsControl))
            { status.Text = "Enter a valid driver name."; return; }
            string error;
            status.Text = NativeInjectionEngine.QueueDriverName(value, out error)
                ? "Name queued. Open a car's tuning screen to apply it."
                : error;
        };
        row.Controls.Add(name);
        row.Controls.Add(apply);
        return row;
    }

    private static string ToTitle(string name)
    {
        string lower = name.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
    }

    private Control MakeOption(string name)
    {
        var row = new Panel
        {
            Width = 690,
            Height = 58,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = panel
        };

        var label = new Label
        {
            Text = name,
            AutoEllipsis = true,
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Color.White,
            Location = new Point(18, 9),
            Size = new Size(475, 24)
        };
        row.Controls.Add(label);

        var verification = new Label
        {
            Text = "UNVERIFIED",
            Font = new Font("Segoe UI Semibold", 7.5f),
            ForeColor = Color.FromArgb(255, 185, 74),
            Location = new Point(18, 33),
            AutoSize = true
        };
        row.Controls.Add(verification);

        var action = new Button
        {
            Text = "LOCKED",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 8.5f),
            ForeColor = muted,
            BackColor = Color.FromArgb(25, 30, 40),
            Size = new Size(145, 36),
            Location = new Point(525, 11),
            Enabled = false,
            TabStop = false
        };
        action.FlatAppearance.BorderColor = Color.FromArgb(67, 78, 96);
        row.Controls.Add(action);
        AddFavoriteButton(row,name); return row;
    }

    private Control MakeVehicleVisibilityOption(string name)
    {
        var row = new Panel
        {
            Width = 690,
            Height = 58,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = panel
        };
        row.Controls.Add(new Label
        {
            Text = name,
            AutoEllipsis = true,
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Color.White,
            Location = new Point(18, 9),
            Size = new Size(475, 24)
        });
        row.Controls.Add(new Label
        {
            Text = "VERIFIED  •  catalogue 146 → 273",
            Font = new Font("Segoe UI Semibold", 7.5f),
            ForeColor = Color.FromArgb(84, 222, 150),
            Location = new Point(18, 33),
            AutoSize = true
        });
        bool persisted=importedStates.ContainsKey("Visible All Vehicles")&&importedStates["Visible All Vehicles"];
        var action = new Button
        {
            Text = persisted?"DISABLE":"ENABLE",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 8.5f),
            ForeColor = Color.White,
            BackColor = persisted?Color.FromArgb(22,145,92):Color.FromArgb(0, 122, 160),
            Size = new Size(145, 36),
            Location = new Point(525, 11),
            TabStop = false
        };
        action.FlatAppearance.BorderColor = accent;
        action.Click += (_, __) =>
        {
            bool enable = action.Text == "ENABLE";
            string error;
            DirectHack hack=CheatTableForm.Find("Visible All Vehicles");
            if (NativePatchEngine.Toggle(hack,enable,out error))
            {
                importedStates["Visible All Vehicles"]=enable;SetToolSaved("Visible All Vehicles",enable);
                action.Text = enable ? "DISABLE" : "ENABLE";
                action.BackColor = enable ? Color.FromArgb(22, 145, 92) : Color.FromArgb(0, 122, 160);
                status.Text = enable
                    ? "Full catalogue enabled • reopen the garage to refresh (273 vehicles)"
                    : "Standard catalogue restored • reopen the garage to refresh";
            }
            else
            {
                status.Text = "Catalogue patch failed: " + error;
                Log(status.Text);
            }
        };
        row.Controls.Add(action);
        AddFavoriteButton(row,name); return row;
    }

    private Control MakeTryAllVehiclesOption(string name)
    {
        var row = new Panel
        {
            Width = 690,
            Height = 58,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = panel
        };
        row.Controls.Add(new Label
        {
            Text = name,
            AutoEllipsis = true,
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Color.White,
            Location = new Point(18, 9),
            Size = new Size(475, 24)
        });
        var verification = new Label
        {
            Text = "SIGNATURE CHECKED  •  IN-GAME TEST REQUIRED",
            Font = new Font("Segoe UI Semibold", 7.5f),
            ForeColor = Color.FromArgb(255, 185, 74),
            Location = new Point(18, 33),
            AutoSize = true
        };
        row.Controls.Add(verification);
        bool persisted=importedStates.ContainsKey("Try All Vehicles")&&importedStates["Try All Vehicles"];
        var action = new Button
        {
            Text = persisted?"DISABLE":"ENABLE",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 8.5f),
            ForeColor = Color.White,
            BackColor = persisted?Color.FromArgb(22,145,92):Color.FromArgb(0, 122, 160),
            Size = new Size(145, 36),
            Location = new Point(525, 11),
            TabStop = false
        };
        action.FlatAppearance.BorderColor = accent;
        action.Click += (_, __) =>
        {
            bool enable = action.Text == "ENABLE";
            string error;
            DirectHack hack=CheatTableForm.Find("Try All Vehicles");
            if (NativePatchEngine.Toggle(hack,enable,out error))
            {
                importedStates["Try All Vehicles"]=enable;SetToolSaved("Try All Vehicles",enable);
                action.Text = enable ? "DISABLE" : "ENABLE";
                action.BackColor = enable ? Color.FromArgb(22, 145, 92) : Color.FromArgb(0, 122, 160);
                status.Text = enable
                    ? "All-vehicle preview enabled • select an unowned car"
                    : "All-vehicle preview disabled";
            }
            else
            {
                status.Text = "Vehicle preview patch failed: " + error;
                Log(status.Text);
            }
        };
        row.Controls.Add(action);
        AddFavoriteButton(row,name); return row;
    }

    private static bool PatchSingleByte(int rva, byte expected, byte replacement, out string error)
    {
        error = "";
        Process[] games = Process.GetProcessesByName("Asphalt8");
        if (games.Length == 0)
        {
            error = "game not found";
            return false;
        }
        Process game = games[0];
        IntPtr handle = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite, false, game.Id);
        if (handle == IntPtr.Zero)
        {
            error = "OpenProcess " + Marshal.GetLastWin32Error();
            return false;
        }
        try
        {
            IntPtr address = IntPtr.Add(game.MainModule.BaseAddress, rva);
            byte[] current = new byte[1];
            IntPtr count;
            if (!ReadProcessMemory(handle, address, current, 1, out count) || count.ToInt64() != 1)
            {
                error = "memory read failed: " + Marshal.GetLastWin32Error();
                return false;
            }
            if (current[0] == replacement) return true;
            if (current[0] != expected)
            {
                error = "unexpected 4.8.0.7 signature";
                return false;
            }
            uint oldProtection;
            if (!VirtualProtectEx(handle, address, (UIntPtr)1, PageExecuteReadWrite, out oldProtection))
            {
                error = "memory protection failed: " + Marshal.GetLastWin32Error();
                return false;
            }
            bool written = WriteProcessMemory(handle, address, new[] { replacement }, 1, out count)
                && count.ToInt64() == 1;
            uint ignored;
            VirtualProtectEx(handle, address, (UIntPtr)1, oldProtection, out ignored);
            if (!written)
            {
                error = "memory write failed: " + Marshal.GetLastWin32Error();
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static bool PatchVehicleVisibility(bool enable, out string error)
    {
        error = "";
        Process[] games = Process.GetProcessesByName("Asphalt8");
        if (games.Length == 0)
        {
            error = "game not found";
            return false;
        }
        Process game = games[0];
        IntPtr handle = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite, false, game.Id);
        if (handle == IntPtr.Zero)
        {
            error = "OpenProcess " + Marshal.GetLastWin32Error();
            return false;
        }
        try
        {
            IntPtr address = IntPtr.Add(game.MainModule.BaseAddress, VehicleVisibilityRva);
            byte[] current = new byte[8];
            IntPtr count;
            if (!ReadProcessMemory(handle, address, current, current.Length, out count) || count.ToInt64() != current.Length)
            {
                error = "memory read failed: " + Marshal.GetLastWin32Error();
                return false;
            }
            byte[] expected = enable ? VehicleVisibilityOriginal : VehicleVisibilityPatch;
            byte[] replacement = enable ? VehicleVisibilityPatch : VehicleVisibilityOriginal;
            if (!BytesEqual(current, expected))
            {
                if (BytesEqual(current, replacement))
                    return true;
                error = "unexpected 4.8.0.7 signature";
                return false;
            }
            uint oldProtection;
            if (!VirtualProtectEx(handle, address, (UIntPtr)replacement.Length, PageExecuteReadWrite, out oldProtection))
            {
                error = "memory protection failed: " + Marshal.GetLastWin32Error();
                return false;
            }
            bool written = WriteProcessMemory(handle, address, replacement, replacement.Length, out count)
                && count.ToInt64() == replacement.Length;
            uint ignored;
            VirtualProtectEx(handle, address, (UIntPtr)replacement.Length, oldProtection, out ignored);
            if (!written)
            {
                error = "memory write failed: " + Marshal.GetLastWin32Error();
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
            if (left[i] != right[i]) return false;
        return true;
    }

    private Control MakeImportedOption(string hackName, string detail = "PORTED  •  BUILD 4.8.0.7 SIGNATURE")
    {
        if (hackName == CheatTableForm.UnlockCareer) return MakeCareerAccessOption(hackName);
        DirectHack hack = CheatTableForm.Find(hackName);
        if (hack == null)
            return MakePendingOption(hackName, "DEFINITION NOT FOUND");

        var row = new Panel
        {
            Width = 690,
            Height = 58,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = panel
        };
        row.Controls.Add(new Label
        {
            Text = hackName,
            AutoEllipsis = true,
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Color.White,
            Location = new Point(18, 9),
            Size = new Size(475, 24)
        });
        row.Controls.Add(new Label
        {
            Text = detail,
            Font = new Font("Segoe UI Semibold", 7.5f),
            ForeColor = Color.FromArgb(84, 222, 150),
            Location = new Point(18, 33),
            AutoSize = true
        });
        bool enabled = importedStates.ContainsKey(hackName) && importedStates[hackName];
        var action = new Button
        {
            Text = enabled ? "DISABLE" : "ENABLE",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 8.5f),
            ForeColor = Color.White,
            BackColor = enabled ? Color.FromArgb(22, 145, 92) : Color.FromArgb(0, 122, 160),
            Size = new Size(145, 36),
            Location = new Point(525, 11),
            TabStop = false
        };
        action.FlatAppearance.BorderColor = accent;
        action.Click += (_, __) =>
        {
            bool turnOn = !importedStates.ContainsKey(hackName) || !importedStates[hackName];
            string error;
            if (NativePatchEngine.Toggle(hack, turnOn, out error))
            {
                importedStates[hackName] = turnOn;
                SetToolSaved(hackName,turnOn);
                action.Text = turnOn ? "DISABLE" : "ENABLE";
                action.BackColor = turnOn ? Color.FromArgb(22, 145, 92) : Color.FromArgb(0, 122, 160);
                status.Text = hackName + (turnOn ? " enabled" : " disabled");
            }
            else
                status.Text = hackName + " : " + error;
        };
        row.Controls.Add(action);
        AddFavoriteButton(row,hackName); return row;
    }

    private Control MakePendingOption(string name, string state)
    {
        var row = new Panel
        {
            Width = 690,
            Height = 58,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = panel
        };
        row.Controls.Add(new Label
        {
            Text = name,
            AutoEllipsis = true,
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Color.White,
            Location = new Point(18, 9),
            Size = new Size(475, 24)
        });
        row.Controls.Add(new Label
        {
            Text = state,
            Font = new Font("Segoe UI Semibold", 7.5f),
            ForeColor = state == "ACTIVE" ? Color.FromArgb(84, 222, 150) : Color.FromArgb(255, 185, 74),
            Location = new Point(18, 33),
            AutoSize = true
        });
        var action = new Button
        {
            Text = state == "ACTIVE" ? "ACTIVE" : "PORT IN PROGRESS",
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 8.5f),
            ForeColor = muted,
            BackColor = Color.FromArgb(25, 30, 40),
            Size = new Size(145, 36),
            Location = new Point(525, 11),
            Enabled = false,
            TabStop = false
        };
        action.FlatAppearance.BorderColor = Color.FromArgb(67, 78, 96);
        row.Controls.Add(action);
        return row;
    }

    private Control MakeInjectionOption(string hackName, string detail = "PORTED INJECTION  •  BUILD 4.8.0.7 SIGNATURE")
    {
        if (hackName == InjectionCatalog.CareerCarChoice) return MakeCareerAccessOption(hackName);
        InjectionHack hack = InjectionCatalog.Find(hackName);
        if (hack == null) return MakePendingOption(hackName, "DEFINITION NOT FOUND");
        var row = new Panel { Width=690, Height=58, Margin=new Padding(0,0,0,8), BackColor=panel };
        row.Controls.Add(new Label { Text=hackName, AutoEllipsis=true, Font=new Font("Segoe UI Semibold",10f), ForeColor=Color.White, Location=new Point(18,9), Size=new Size(475,24) });
        row.Controls.Add(new Label { Text=detail, Font=new Font("Segoe UI Semibold",7.5f), ForeColor=Color.FromArgb(84,222,150), Location=new Point(18,33), AutoSize=true });
        bool enabled=importedStates.ContainsKey(hackName)&&importedStates[hackName];
        var action=new Button { Text=enabled?"DISABLE":"ENABLE", FlatStyle=FlatStyle.Flat, Font=new Font("Segoe UI Semibold",8.5f), ForeColor=Color.White, BackColor=enabled?Color.FromArgb(22,145,92):Color.FromArgb(0,122,160), Size=new Size(145,36), Location=new Point(525,11), TabStop=false };
        action.FlatAppearance.BorderColor=accent;
        action.Click += (_,__) =>
        {
            bool turnOn=!importedStates.ContainsKey(hackName)||!importedStates[hackName]; string error;
            if(turnOn && (hackName=="Money Mod" || hackName=="Free Buy Mode"))
            {
                string otherName=hackName=="Money Mod"?"Free Buy Mode":"Money Mod";
                bool otherEnabled=importedStates.ContainsKey(otherName)&&importedStates[otherName];
                if(otherEnabled)
                {
                    string otherError;
                    InjectionHack other=InjectionCatalog.Find(otherName);
                    if(!NativeInjectionEngine.Toggle(other,false,out otherError))
                    {
                        status.Text=otherName+" : "+otherError; return;
                    }
                    importedStates[otherName]=false;
                    SetToolSaved(otherName,false);
                    if(otherName=="Money Mod")
                    {
                        InjectionHack currency=InjectionCatalog.Find("Money Mod Currency");
                        if(importedStates.ContainsKey("Money Mod Currency")&&importedStates["Money Mod Currency"])
                        {
                            if(!NativeInjectionEngine.Toggle(currency,false,out otherError)){status.Text="Money Mod Currency : "+otherError;return;}
                            importedStates["Money Mod Currency"]=false;SetToolSaved("Money Mod Currency",false);
                        }
                    }
                }
            }
            if(!turnOn && hackName=="Money Mod")
            {
                InjectionHack currency=InjectionCatalog.Find("Money Mod Currency"); string currencyError;
                if(importedStates.ContainsKey("Money Mod Currency")&&importedStates["Money Mod Currency"])
                {
                    if(!NativeInjectionEngine.Toggle(currency,false,out currencyError)){status.Text="Money Mod Currency : "+currencyError;return;}
                    importedStates["Money Mod Currency"]=false;SetToolSaved("Money Mod Currency",false);
                }
            }
            if(NativeInjectionEngine.Toggle(hack,turnOn,out error))
            {
                if(turnOn && hackName=="Money Mod")
                {
                    InjectionHack currency=InjectionCatalog.Find("Money Mod Currency"); string currencyError;
                    if(!NativeInjectionEngine.Toggle(currency,true,out currencyError))
                    {
                        string ignored;NativeInjectionEngine.Toggle(hack,false,out ignored);status.Text="Money Mod Currency : "+currencyError;return;
                    }
                    importedStates["Money Mod Currency"]=true;SetToolSaved("Money Mod Currency",true);
                }
                importedStates[hackName]=turnOn; action.Text=turnOn?"DISABLE":"ENABLE";
                SetToolSaved(hackName,turnOn);
                action.BackColor=turnOn?Color.FromArgb(22,145,92):Color.FromArgb(0,122,160);
                status.Text=hackName+(turnOn?" enabled  •  in-game behavior test required":" disabled");
            }
            else status.Text=hackName+" : "+error;
        };
        row.Controls.Add(action); AddFavoriteButton(row,hackName); return row;
    }

    private bool SetCareerAccess(bool enabled, out string error)
    {
        bool priorChoice = importedStates.ContainsKey(InjectionCatalog.CareerCarChoice)
            && importedStates[InjectionCatalog.CareerCarChoice];
        bool priorCareer = importedStates.ContainsKey(CheatTableForm.UnlockCareer)
            && importedStates[CheatTableForm.UnlockCareer];
        error = "";
        if (priorChoice != enabled && !NativeInjectionEngine.Toggle(
            InjectionCatalog.Find(InjectionCatalog.CareerCarChoice), enabled, out error)) return false;
        if (priorCareer != enabled && !NativePatchEngine.Toggle(
            CheatTableForm.Find(CheatTableForm.UnlockCareer), enabled, out error))
        {
            if (priorChoice != enabled)
            {
                string rollbackError;
                if (!NativeInjectionEngine.Toggle(InjectionCatalog.Find(InjectionCatalog.CareerCarChoice),
                    priorChoice, out rollbackError)) error += " • " + rollbackError;
            }
            return false;
        }
        importedStates[InjectionCatalog.CareerCarChoice] = enabled;
        importedStates[CheatTableForm.UnlockCareer] = enabled;
        if (!NativeInjectionEngine.SetCareerLiveMode(enabled,out error))
        {
            string rollbackError;
            if (priorChoice != enabled && NativeInjectionEngine.Toggle(
                InjectionCatalog.Find(InjectionCatalog.CareerCarChoice),priorChoice,out rollbackError))
                importedStates[InjectionCatalog.CareerCarChoice]=priorChoice;
            if (priorCareer != enabled && NativePatchEngine.Toggle(
                CheatTableForm.Find(CheatTableForm.UnlockCareer),priorCareer,out rollbackError))
                importedStates[CheatTableForm.UnlockCareer]=priorCareer;
            return false;
        }
        return true;
    }

    private Control MakeCareerAccessOption(string name)
    {
        bool enabled = importedStates.ContainsKey(CheatTableForm.UnlockCareer)
            && importedStates[CheatTableForm.UnlockCareer]
            && importedStates.ContainsKey(InjectionCatalog.CareerCarChoice)
            && importedStates[InjectionCatalog.CareerCarChoice];
        var row = new Panel { Width=690, Height=66, Margin=new Padding(0,0,0,8), BackColor=panel };
        row.Controls.Add(new Label { Text=name, AutoEllipsis=true, Font=new Font("Segoe UI Semibold",10f),
            ForeColor=Color.White, Location=new Point(18,8), Size=new Size(475,24) });
        row.Controls.Add(new Label { Text="SEASONS, CLASSES & REQUIRED CARS", AutoSize=true,
            Font=new Font("Segoe UI Semibold",7.5f), ForeColor=muted, Location=new Point(18,38) });
        var action = new Button { Text=enabled?"DISABLE":"ENABLE", FlatStyle=FlatStyle.Flat,
            Font=new Font("Segoe UI Semibold",8.5f), ForeColor=Color.White,
            BackColor=enabled?Color.FromArgb(22,145,92):Color.FromArgb(0,122,160),
            Location=new Point(525,14), Size=new Size(145,36), TabStop=false };
        action.FlatAppearance.BorderColor=accent;
        action.Click += (_,__) =>
        {
            RestoreToolsForNewGame();
            bool turnOn = !importedStates.ContainsKey(CheatTableForm.UnlockCareer)
                || !importedStates[CheatTableForm.UnlockCareer]
                || !importedStates.ContainsKey(InjectionCatalog.CareerCarChoice)
                || !importedStates[InjectionCatalog.CareerCarChoice];
            string error;
            if (!SetCareerAccess(turnOn, out error)) { status.Text=error; return; }
            SetToolSaved(CheatTableForm.UnlockCareer,turnOn);
            SetToolSaved(InjectionCatalog.CareerCarChoice,turnOn);
            ShowSection(currentSection);
            status.Text=turnOn?"Unrestricted career access enabled":"Normal restrictions restored";
        };
        row.Controls.Add(action);
        AddFavoriteButton(row,name);
        return row;
    }

    private Control MakeUnlimitedBoosterOption(int booster)
    {
        string label = UnlimitedBoosterNames[booster];
        var row = new Panel { Width=690, Height=58, Margin=new Padding(0,0,0,8), BackColor=panel };
        row.Controls.Add(new Label { Text=label, Font=new Font("Segoe UI Semibold",10f), ForeColor=Color.White, Location=new Point(18,16), Size=new Size(450,24) });
        bool enabled = importedStates.ContainsKey(label) && importedStates[label];
        var action = new Button { Text=enabled?"DISABLE":"ENABLE", FlatStyle=FlatStyle.Flat, Font=new Font("Segoe UI Semibold",8.5f),
            ForeColor=Color.White, BackColor=enabled?Color.FromArgb(22,145,92):Color.FromArgb(0,122,160), Size=new Size(145,36), Location=new Point(525,11) };
        action.FlatAppearance.BorderColor=accent;
        action.Click += (_,__) =>
        {
            bool next = !enabled;
            int mask = UnlimitedBoosterMask(savedTools);
            mask = next ? mask | (1 << booster) : mask & ~(1 << booster);
            string error;
            if (!NativeInjectionEngine.SetUnlimitedBoosterMask(mask, out error)) { status.Text=error; return; }
            enabled=next;
            importedStates[label]=enabled;
            SetToolSaved(label,enabled);
            action.Text=enabled?"DISABLE":"ENABLE";
            action.BackColor=enabled?Color.FromArgb(22,145,92):Color.FromArgb(0,122,160);
        };
        row.Controls.Add(action);
        AddFavoriteButton(row,label);
        return row;
    }

    private static int UnlimitedBoosterMask(HashSet<string> tools)
    {
        int mask=0;
        for (int booster=0; booster<UnlimitedBoosterNames.Length; booster++)
            if (tools.Contains(UnlimitedBoosterNames[booster])) mask |= 1 << booster;
        return mask;
    }

    private Control MakeCarPurchaseOption(bool catalogue)
    {
        string label = catalogue ? "Buy at catalogue price (offline)" : "Free Buy — 1 CR";
        var row = new Panel { Width=690, Height=66, Margin=new Padding(0,0,0,8), BackColor=panel };
        row.Controls.Add(new Label { Text=label, Font=new Font("Segoe UI Semibold",10f), ForeColor=Color.White, Location=new Point(18,8), Size=new Size(300,24) });
        row.Controls.Add(new Label { Text="CURRENTLY SELECTED GARAGE CAR", Font=new Font("Segoe UI Semibold",7.5f), ForeColor=muted, Location=new Point(18,38), AutoSize=true });
        var action = new Button { Text=catalogue?"CHECK PRICE":"BUY • 1 CR", FlatStyle=FlatStyle.Flat, Font=new Font("Segoe UI Semibold",8.5f), ForeColor=Color.White,
            BackColor=Color.FromArgb(0,122,160), Size=new Size(145,36), Location=new Point(525,14) };
        action.FlatAppearance.BorderColor=accent;
        action.Click += (_,__) =>
        {
            string error;
            if (walletEditPending) { status.Text="An operation is already pending."; return; }
            if (catalogue)
            {
                if (!NativeInjectionEngine.QuoteSelectedCar(out error)) { status.Text=error; return; }
                ObserveWalletCommand(action, "Checking price…", state => ConfirmCataloguePurchase(action,state));
                return;
            }
            if (!NativeInjectionEngine.BuySelectedCar(out error)) { status.Text=error; return; }
            ObserveWalletCommand(action, "Purchasing…", state => RecordCarPurchase(state,"1 CR"));
        };
        row.Controls.Add(action);
        AddFavoriteButton(row,label);
        return row;
    }

    private string ConfirmCataloguePurchase(Button action, int state)
    {
        if (state!=3) return CarPurchaseResult(state,null);
        int amount;
        bool gems;
        if (!NativeInjectionEngine.ReadQuotedCarPrice(out amount,out gems)) return "Cannot read the price.";
        string price=amount.ToString("N0")+(gems?" gems":" CR");
        var choice=MessageBox.Show(this,"Buy the selected car for "+price+" ?","Offline purchase",
            MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2);
        if (choice!=DialogResult.Yes) return "Purchase cancelled.";
        string error;
        if (!NativeInjectionEngine.BuyQuotedCar(out error)) return error;
        ObserveWalletCommand(action, "Purchasing…", result => RecordCarPurchase(result,price));
        return "Purchasing…";
    }

    private static string CarPurchaseResult(int state, string price)
    {
        switch (state)
        {
            case 3: return "Car purchased for "+price+".";
            case 5: return "Select a car in the main garage.";
            case 6: return "You already own this car.";
            case 7: return "Insufficient balance. Listed price: "+price+".";
            case 9: return "The loaded catalogue has no credit or gem price for this car.";
            case 10: return "The selected car changed. Check its price again.";
            case 11: return "You do not own this car.";
            case 12: return "The starter Dodge Dart GT stays in your garage.";
            case 13: return "The refund would exceed the game's maximum balance.";
            default: return "The game did not confirm the purchase.";
        }
    }

    private string RecordCarPurchase(int state, string price)
    {
        if(state!=3) return CarPurchaseResult(state,price);
        int id, amount;
        bool gems;
        if(!NativeInjectionEngine.ReadCarReceipt(out id,out amount,out gems) || amount<=0)
            return "Purchase confirmed. Cannot read the game's receipt.";
        try
        {
            WriteCarReceipt(id,amount,gems);
            return "Car purchased for "+price+". Receipt saved.";
        }
        catch (IOException ex) { return "Purchase confirmed; receipt unavailable. "+ex.Message; }
        catch (UnauthorizedAccessException ex) { return "Purchase confirmed; receipt unavailable. "+ex.Message; }
    }

    private static void WriteCarReceipt(int id, int amount, bool gems)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CarReceiptsPath));
        File.AppendAllText(CarReceiptsPath, id+"|"+amount+"|"+(gems?"1":"0")+Environment.NewLine);
    }

    private static int FindCarReceipt(int id, out bool gems)
    {
        gems=false;
        int amount=0;
        if(!File.Exists(CarReceiptsPath)) return 0;
        foreach(string line in File.ReadLines(CarReceiptsPath))
        {
            string[] fields=line.Split('|');
            int vehicle, paid;
            if(fields.Length==3 && int.TryParse(fields[0],out vehicle) && vehicle==id
                && int.TryParse(fields[1],out paid) && paid>=0 && (fields[2]=="0" || fields[2]=="1"))
            { amount=paid; gems=fields[2]=="1"; }
        }
        return amount;
    }

    private Control MakeCarSaleOption()
    {
        const string label="Sell selected car";
        var row = new Panel { Width=690, Height=66, Margin=new Padding(0,0,0,8), BackColor=panel };
        row.Controls.Add(new Label { Text=label, Font=new Font("Segoe UI Semibold",10f), ForeColor=Color.White, Location=new Point(18,8), Size=new Size(450,24) });
        row.Controls.Add(new Label { Text="REFUND THE RECORDED PURCHASE PRICE", Font=new Font("Segoe UI Semibold",7.5f), ForeColor=muted, Location=new Point(18,38), AutoSize=true });
        var action = new Button { Text="SELL", FlatStyle=FlatStyle.Flat, Font=new Font("Segoe UI Semibold",8.5f),
            ForeColor=Color.White, BackColor=Color.FromArgb(0,122,160), Size=new Size(145,36), Location=new Point(525,14) };
        action.FlatAppearance.BorderColor=accent;
        action.Click += (_,__) =>
        {
            if(walletEditPending) { status.Text="An operation is already pending."; return; }
            string error;
            if(!NativeInjectionEngine.ReadSaleSelection(out error)) { status.Text=error; return; }
            ObserveWalletCommand(action,"Reading receipt…",state=>ConfirmCarSale(action,state));
        };
        row.Controls.Add(action);
        AddFavoriteButton(row,label);
        return row;
    }

    private string ConfirmCarSale(Button action, int state)
    {
        if(state!=3) return CarPurchaseResult(state,null);
        int id, ignored;
        bool gems;
        if(!NativeInjectionEngine.ReadCarReceipt(out id,out ignored,out gems)) return "The game did not confirm the selected car.";
        int paid;
        try { paid=FindCarReceipt(id,out gems); }
        catch (IOException ex) { return "Cannot read receipt. "+ex.Message; }
        catch (UnauthorizedAccessException ex) { return "Cannot access receipt. "+ex.Message; }
        if(paid<=0) return "No receipt exists for this car. Its actual purchase price is unknown.";
        string refund=paid.ToString("N0")+(gems?" gems":" CR");
        var choice=MessageBox.Show(this,"Sell the selected car for "+refund+"? Its upgrades will be removed from the garage.",
            "Offline sale",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2);
        if(choice!=DialogResult.Yes) return "Sale cancelled.";
        string error;
        if(!NativeInjectionEngine.SellSelectedCar(paid,gems,out error)) return error;
        ObserveWalletCommand(action,"Selling…",result=>
        {
            if(result!=3) return result==8 ? "Could not remove the car." : CarPurchaseResult(result,null);
            try { WriteCarReceipt(id,0,gems); }
            catch (IOException) { return "Car sold for "+refund+". History unavailable."; }
            catch (UnauthorizedAccessException) { return "Car sold for "+refund+". History unavailable."; }
            return "Car sold. Refund: "+refund+".";
        });
        return "Selling…";
    }

    private void ObserveWalletCommand(Button action, string pending, Func<int,string> result)
    {
        walletEditPending=true;
        action.Enabled=false;
        status.Text=pending;
        var timer = new Timer { Interval=100 };
        int checks=0;
        timer.Tick += (sender,args) =>
        {
            int state=NativeInjectionEngine.WalletCommandState();
            if ((state==1 || state==2) && ++checks<100) return;
            timer.Dispose();
            walletEditPending=false;
            if (!action.IsDisposed) action.Enabled=true;
            status.Text=state==1 || state==2 ? "Open an in-game menu to apply the pending operation." : result(state);
        };
        timer.Start();
    }

    private Control MakeWalletBalanceOption(bool gems, bool fusion = false)
    {
        string label = fusion ? "Set fusion coins" : gems ? "Set gems" : "Set credits";
        string unit = fusion ? "fusion coins" : gems ? "gems" : "CR";
        var row = new Panel { Width=690, Height=66, Margin=new Padding(0,0,0,8), BackColor=panel };
        row.Controls.Add(new Label { Text=label, Font=new Font("Segoe UI Semibold",10f), ForeColor=Color.White, Location=new Point(18,8), Size=new Size(300,24) });
        row.Controls.Add(new Label { Text="NEW BALANCE", Font=new Font("Segoe UI Semibold",7.5f), ForeColor=muted, Location=new Point(18,38), AutoSize=true });
        var value = new NumericUpDown { Minimum=0, Maximum=999999999, ThousandsSeparator=true, Location=new Point(352,17), Size=new Size(138,30),
            Font=new Font("Segoe UI Semibold",10f), BackColor=Color.FromArgb(25,30,40), ForeColor=Color.White };
        var action = new Button { Text="APPLY", FlatStyle=FlatStyle.Flat, Font=new Font("Segoe UI Semibold",8.5f), ForeColor=Color.White,
            BackColor=Color.FromArgb(0,122,160), Size=new Size(145,36), Location=new Point(525,14) };
        action.FlatAppearance.BorderColor=accent;
        action.Click += (_,__) =>
        {
            int amount = (int)value.Value;
            string error;
            if (walletEditPending) { status.Text="A balance change is already pending."; return; }
            bool queued = fusion ? NativeInjectionEngine.SetFusionBalance(amount, out error)
                                 : NativeInjectionEngine.SetWalletBalance(gems, amount, out error);
            if (!queued) { status.Text=error; return; }
            ObserveWalletCommand(action, "Applying balance…", state => state==3
                ? "Balance set to "+amount.ToString("N0")+" "+unit
                : "The game did not confirm the new balance.");
        };
        row.Controls.Add(value);
        row.Controls.Add(action);
        AddFavoriteButton(row,label);
        return row;
    }

    private Control MakeRaceSettingOption(string name, string label, int maximum)
    {
        var row = new Panel { Width=690, Height=66, Margin=new Padding(0,0,0,8), BackColor=panel };
        var caption = new Label { Text=label, Font=new Font("Segoe UI Semibold",10f), ForeColor=Color.White, Location=new Point(18,8), Size=new Size(300,24) };
        var detail = new Label { Font=new Font("Segoe UI Semibold",7.5f), Location=new Point(18,38), AutoSize=true };
        var value = new NumericUpDown { Minimum=1, Maximum=maximum, Value=InjectionCatalog.GetValue(name), Location=new Point(290,17), Size=new Size(90,30), Font=new Font("Segoe UI Semibold",10f), BackColor=Color.FromArgb(25,30,40), ForeColor=Color.White };
        var apply = new Button { Text="APPLY", FlatStyle=FlatStyle.Flat, Font=new Font("Segoe UI Semibold",8.5f), ForeColor=Color.White, BackColor=Color.FromArgb(0,122,160), Location=new Point(384,14), Size=new Size(98,36), TabStop=false };
        var toggle = new Button { FlatStyle=FlatStyle.Flat, Font=new Font("Segoe UI Semibold",8.5f), ForeColor=Color.White, Location=new Point(525,14), Size=new Size(145,36), TabStop=false };
        apply.FlatAppearance.BorderColor = accent;
        toggle.FlatAppearance.BorderColor = accent;

        Action refresh = () =>
        {
            bool active = importedStates.ContainsKey(name) && importedStates[name];
            toggle.Text = active ? "DISABLE" : "ENABLE";
            toggle.BackColor = active ? Color.FromArgb(22,145,92) : Color.FromArgb(0,122,160);
            detail.Text = active ? "ACTIVE  •  " + InjectionCatalog.GetValue(name) + " AT NEXT RACE START" : "DEFAULT  •  ORIGINAL RACE VALUE";
            if (name == "Racers" && active) detail.Text += " (INCLUDING PLAYER)";
            detail.ForeColor = active ? Color.FromArgb(84,222,150) : muted;
        };
        Action<bool> commit = toggleRequested =>
        {
            RestoreToolsForNewGame();
            bool active = importedStates.ContainsKey(name) && importedStates[name];
            bool enable = toggleRequested ? !active : active;
            int selected = (int)value.Value;
            string error;
            if ((active || enable) && !ApplyRaceValue(name, enable ? selected : 0, out error))
            {
                status.Text = label + " : " + error;
                return;
            }
            InjectionCatalog.SetValue(name, selected);
            savedValues[name] = selected;
            importedStates[name] = enable;
            SetToolSaved(name, enable);
            refresh();
            status.Text = label + (enable ? " = " + selected + " at next race start"
                : toggleRequested ? ": original value at next race start" : ": custom value saved");
        };
        apply.Click += (_,__) => commit(false);
        toggle.Click += (_,__) => commit(true);
        refresh();
        row.Controls.AddRange(new Control[] { caption, detail, value, apply, toggle });
        AddFavoriteButton(row, name);
        return row;
    }

    private Control MakeValueInjectionOption(string hackName, int minimum, int maximum)
    {
        InjectionHack hack=InjectionCatalog.Find(hackName);
        if(hack==null) return MakePendingOption(hackName,"DEFINITION NOT FOUND");
        var row=new Panel { Width=690,Height=66,Margin=new Padding(0,0,0,8),BackColor=panel };
        row.Controls.Add(new Label { Text=hackName,Font=new Font("Segoe UI Semibold",10f),ForeColor=Color.White,Location=new Point(18,8),Size=new Size(300,24) });
        row.Controls.Add(new Label { Text="BOUNDED VALUE  •  CRASH GUARD",Font=new Font("Segoe UI Semibold",7.5f),ForeColor=Color.FromArgb(84,222,150),Location=new Point(18,38),AutoSize=true });
        int stored=InjectionCatalog.GetValue(hackName);
        if(hackName=="Nitro Ratio") stored=(int)BitConverter.ToSingle(BitConverter.GetBytes(stored),0);
        var value=new NumericUpDown { Minimum=minimum,Maximum=maximum,Value=Math.Max(minimum,Math.Min(maximum,stored)),Location=new Point(380,17),Size=new Size(110,30),Font=new Font("Segoe UI Semibold",10f),BackColor=Color.FromArgb(25,30,40),ForeColor=Color.White };
        bool valueActive=importedStates.ContainsKey(hackName)&&importedStates[hackName];
        var action=new Button { Text=valueActive?"ACTIVE: "+stored:"APPLY",FlatStyle=FlatStyle.Flat,Font=new Font("Segoe UI Semibold",8.5f),ForeColor=Color.White,BackColor=valueActive?Color.FromArgb(22,145,92):Color.FromArgb(0,122,160),Size=new Size(145,36),Location=new Point(525,14),TabStop=false };
        action.FlatAppearance.BorderColor=accent;
        action.Click += (_,__) =>
        {
            RestoreToolsForNewGame();
            int selected = (int)value.Value;
            string error;
            bool wasOn = importedStates.ContainsKey(hackName) && importedStates[hackName];
            if (wasOn && !NativeInjectionEngine.Toggle(hack, false, out error))
            {
                status.Text = hackName + " : " + error;
                return;
            }
            importedStates[hackName] = false;
            InjectionCatalog.SetValue(hackName, selected);
            if (!NativeInjectionEngine.Toggle(hack, true, out error))
            {
                status.Text = hackName + " : " + error;
                return;
            }
            importedStates[hackName] = true;
            savedValues[hackName] = selected;
            SetToolSaved(hackName, true);
            action.Text = "ACTIVE: " + selected;
            action.BackColor = Color.FromArgb(22,145,92);
            status.Text = hackName + " = " + selected;
        };
        row.Controls.Add(value); row.Controls.Add(action); AddFavoriteButton(row,hackName); return row;
    }

    private Control MakeDualValueInjectionOption(string label, string firstName, string secondName, int minimum, int maximum)
    {
        InjectionHack first=InjectionCatalog.Find(firstName), second=InjectionCatalog.Find(secondName);
        if(first==null||second==null) return MakePendingOption(label,"DEFINITION NOT FOUND");
        var row=new Panel { Width=690,Height=66,Margin=new Padding(0,0,0,8),BackColor=panel };
        row.Controls.Add(new Label { Text=label,Font=new Font("Segoe UI Semibold",10f),ForeColor=Color.White,Location=new Point(18,8),Size=new Size(300,24) });
        row.Controls.Add(new Label { Text="SYNCHRONIZED DUAL INJECTION",Font=new Font("Segoe UI Semibold",7.5f),ForeColor=Color.FromArgb(84,222,150),Location=new Point(18,38),AutoSize=true });
        float current=BitConverter.ToSingle(BitConverter.GetBytes(InjectionCatalog.GetValue(label)),0);
        var value=new NumericUpDown { Minimum=minimum,Maximum=maximum,Value=(decimal)Math.Max(minimum,Math.Min(maximum,current)),Location=new Point(380,17),Size=new Size(110,30),Font=new Font("Segoe UI Semibold",10f),BackColor=Color.FromArgb(25,30,40),ForeColor=Color.White };
        bool active=importedStates.ContainsKey(firstName)&&importedStates[firstName]&&importedStates.ContainsKey(secondName)&&importedStates[secondName];
        var action=new Button { Text=active?"DISABLE":"APPLY",FlatStyle=FlatStyle.Flat,Font=new Font("Segoe UI Semibold",8.5f),ForeColor=Color.White,BackColor=active?Color.FromArgb(22,145,92):Color.FromArgb(0,122,160),Size=new Size(145,36),Location=new Point(525,14),TabStop=false };
        action.FlatAppearance.BorderColor=accent;
        action.Click += (_,__) =>
        {
            string error;
            if(active)
            {
                bool a=NativeInjectionEngine.Toggle(first,false,out error); string error2; bool b=NativeInjectionEngine.Toggle(second,false,out error2);
                if(!a||!b){status.Text=label+" : "+(!a?error:error2);return;}
                active=false; importedStates[firstName]=false; importedStates[secondName]=false; SetToolSaved(firstName,false);SetToolSaved(secondName,false); action.Text="APPLY"; action.BackColor=Color.FromArgb(0,122,160); status.Text=label+" disabled"; return;
            }
            int bits=BitConverter.ToInt32(BitConverter.GetBytes((float)value.Value),0); InjectionCatalog.SetValue(label,bits);
            SetToolValue(label,bits);
            if(!NativeInjectionEngine.Toggle(first,true,out error)){status.Text=label+" A : "+error;return;}
            if(!NativeInjectionEngine.Toggle(second,true,out error))
            {
                string ignored; NativeInjectionEngine.Toggle(first,false,out ignored); status.Text=label+" B : "+error;return;
            }
            active=true; importedStates[firstName]=true; importedStates[secondName]=true; SetToolSaved(firstName,true);SetToolSaved(secondName,true); action.Text="DISABLE"; action.BackColor=Color.FromArgb(22,145,92);
            status.Text=label+" = "+value.Value+"  •  both paths active";
        };
        row.Controls.Add(value); row.Controls.Add(action); AddFavoriteButton(row,label); return row;
    }

    private Control MakeQuickRaceMapsOption()
    {
        var row=new Panel { Width=690,Height=66,Margin=new Padding(0,0,0,8),BackColor=panel };
        row.Controls.Add(new Label { Text="Unlock All Maps In QR",Font=new Font("Segoe UI Semibold",10f),ForeColor=Color.White,Location=new Point(18,8),Size=new Size(330,24) });
        row.Controls.Add(new Label { Text="OPEN THE QUICK RACE PAGE FIRST",Font=new Font("Segoe UI Semibold",7.5f),ForeColor=Color.FromArgb(84,222,150),Location=new Point(18,38),AutoSize=true });
        bool active=false;
        var action=new Button { Text="ENABLE",FlatStyle=FlatStyle.Flat,Font=new Font("Segoe UI Semibold",8.5f),ForeColor=Color.White,BackColor=Color.FromArgb(0,122,160),Size=new Size(145,36),Location=new Point(525,14),TabStop=false };
        action.FlatAppearance.BorderColor=accent;
        action.Click += (_,__) =>
        {
            string error; bool turnOn=!active;
            if(NativeQuickRaceMapEngine.Toggle(turnOn,out error))
            {
                active=turnOn; action.Text=active?"DISABLE":"ENABLE"; action.BackColor=active?Color.FromArgb(22,145,92):Color.FromArgb(0,122,160);
                status.Text=active?"Quick Race tracks enabled":"Quick Race tracks restored";
            }
            else status.Text="Unlock All Maps In QR : "+error;
        };
        row.Controls.Add(action); AddFavoriteButton(row,"Unlock All Maps In QR"); return row;
    }

    private void LoadFavorites()
    {
        try
        {
            if(!File.Exists(FavoritesPath)) return;
            foreach(string line in File.ReadAllLines(FavoritesPath))
            {
                int split=line.IndexOf('|'); if(split>0) favorites[NormalizeSavedName(line.Substring(0,split))]=NormalizeSavedName(line.Substring(split+1));
            }
        }
        catch { }
    }

    private static readonly Dictionary<string, string> LegacyEnglishNames = new Dictionary<string, string>
    {
        { "Afficher / masquer (Insert)", "Show / hide (Insert)" },
        { "Relancer le jeu", "Launch game" },
        { "Quitter le trainer", "Exit trainer" },
        { "Crédits doubles illimités", "Unlimited double credits" },
        { "Départ nitro illimité", "Unlimited nitro starter" },
        { "Kit de tuning illimité", "Unlimited tuning kit" },
        { "Réserve supplémentaire illimitée", "Unlimited extra tank" },
        { "GARAGE ET VÉHICULES", "GARAGE & VEHICLES" },
        { "★ FAVORIS", "★ FAVORITES" },
        { "MONNAIES ET INVENTAIRE", "CURRENCY & INVENTORY" },
        { "AMÉLIORATIONS", "UPGRADES" },
        { "COURSE", "RACING" },
        { "IA ET ADVERSAIRES", "AI & OPPONENTS" },
        { "COURSES PERSONNALISÉES", "CUSTOM RACES" },
        { "CAMÉRA ET AFFICHAGE", "CAMERA & DISPLAY" },
        { "SYSTÈME", "SYSTEM" },
        { "Garage et véhicules", "Garage & vehicles" },
        { "Commandes liées au garage local. Une option reste verrouillée jusqu'à validation sur la 4.8.0.7.", "Local garage controls. Unverified options remain locked for build 4.8.0.7." },
        { "INSERT  Afficher / masquer    •    État : moteur Garage en cours d'analyse", "INSERT  Show / hide    •    Local garage patch engine" },
        { "Déverrouiller toutes les voitures", "Unlock all cars" },
        { "Déverrouiller toutes les motos", "Unlock all motorcycles" },
        { "Ajouter la voiture sélectionnée au garage", "Add selected car to garage" },
        { "Retirer la voiture sélectionnée du garage", "Remove selected car from garage" },
        { "Déverrouiller toutes les classes", "Unlock all classes" },
        { "Déverrouiller tous les plans", "Unlock all blueprints" },
        { "Déverrouiller toutes les collections", "Unlock all collections" },
        { "Déverrouiller toutes les maîtrises", "Unlock all mastery events" },
        { "Déverrouiller toutes les peintures", "Unlock all paint colors" },
        { "Déverrouiller tous les autocollants", "Unlock all decals" },
        { "Déverrouiller toutes les jantes", "Unlock all wheels" },
        { "Déverrouiller toutes les pièces visuelles", "Unlock all visual parts" },
        { "Choisir la couleur primaire", "Choose primary color" },
        { "Choisir la couleur secondaire", "Choose secondary color" },
        { "Autoriser toutes les couleurs", "Allow all colors" },
        { "Dupliquer une voiture dans plusieurs configurations", "Duplicate car configurations" },
        { "Changer instantanément de véhicule", "Switch vehicles instantly" },
        { "Ignorer les restrictions de rang", "Ignore rank restrictions" },
        { "Afficher les véhicules cachés ou inutilisés", "Show hidden or unused vehicles" },
        { "Restaurer un véhicule supprimé du profil", "Restore a removed vehicle" },
        { "Exporter une configuration de voiture", "Export car configuration" },
        { "Importer une configuration de voiture", "Import car configuration" },
        { "Copier les réglages vers une autre voiture", "Copy settings to another car" },
        { "CARRIÈRE SOLO • ROUVRE LA SÉLECTION DE VOITURE", "SOLO CAREER • REOPEN THE CAR SELECTOR" },
        { "Aucun favori", "No favorites yet" },
        { "UTILISEZ L'ÉTOILE SUR UN OUTIL", "CLICK THE STAR NEXT TO A TOOL" },
        { "FUSION AU PRIX DU JEU • CARTES INDISPONIBLES HORS LIGNE", "STANDARD FUSION COST • CARDS UNAVAILABLE OFFLINE" },
        { "Nombre de tours", "Lap count" },
        { "Nombre de pilotes", "Racer count" },
        { "ROUVRE LE MENU JOUER OU LES SAISONS POUR ACTUALISER", "REOPEN PLAY OR THE SEASON LIST TO REFRESH" },
        { "Moteur de patch 4.8.0.7", "Patch engine 4.8.0.7" },
        { "ACTIF", "ACTIVE" },
        { "  •  outils de la table répartis dans cette section", "  •  integrated trainer tools" },
        { "Nom du pilote", "Driver name" },
        { "Ouvre ensuite le tuning d’une voiture", "Then open a car's tuning screen" },
        { "APPLIQUER", "APPLY" },
        { "Saisis un nom de pilote valide.", "Enter a valid driver name." },
        { "Nom en attente. Ouvre le tuning d’une voiture pour l’appliquer.", "Name queued. Open a car's tuning screen to apply it." },
        { "NON VÉRIFIÉ", "UNVERIFIED" },
        { "VERROUILLÉ", "LOCKED" },
        { "VÉRIFIÉ  •  catalogue 146 → 273", "VERIFIED  •  catalogue 146 → 273" },
        { "DÉSACTIVER", "DISABLE" },
        { "ACTIVER", "ENABLE" },
        { "Catalogue complet actif  •  quittez puis rouvrez le garage pour actualiser (273 véhicules)", "Full catalogue enabled • reopen the garage to refresh (273 vehicles)" },
        { "Catalogue standard restauré  •  quittez puis rouvrez le garage pour actualiser", "Standard catalogue restored • reopen the garage to refresh" },
        { "Échec du patch Catalogue : ", "Catalogue patch failed: " },
        { "SIGNATURE VALIDÉE  •  essai en jeu requis", "SIGNATURE CHECKED  •  IN-GAME TEST REQUIRED" },
        { "Essai de tous les véhicules actif  •  testez une voiture non possédée", "All-vehicle preview enabled • select an unowned car" },
        { "Essai de tous les véhicules désactivé", "All-vehicle preview disabled" },
        { "Échec du patch Essai véhicules : ", "Vehicle preview patch failed: " },
        { "jeu introuvable", "game not found" },
        { "lecture mémoire ", "memory read failed: " },
        { "signature 4.8.0.7 inattendue", "unexpected 4.8.0.7 signature" },
        { "protection mémoire ", "memory protection failed: " },
        { "écriture mémoire ", "memory write failed: " },
        { "IMPORTÉ  •  SIGNATURE 4.8.0.7", "PORTED  •  BUILD 4.8.0.7 SIGNATURE" },
        { "DÉFINITION INTROUVABLE", "DEFINITION NOT FOUND" },
        { " activé", " enabled" },
        { " désactivé", " disabled" },
        { "EN PORTAGE", "PORT IN PROGRESS" },
        { "INJECTION IMPORTÉE  •  SIGNATURE 4.8.0.7", "PORTED INJECTION  •  BUILD 4.8.0.7 SIGNATURE" },
        { " activé  •  essai comportemental requis", " enabled  •  in-game behavior test required" },
        { "SAISONS, CLASSES ET VOITURES IMPOSÉES", "SEASONS, CLASSES & REQUIRED CARS" },
        { "Accès libre aux courses activé", "Unrestricted career access enabled" },
        { "Restrictions normales rétablies", "Normal restrictions restored" },
        { "Achat hors ligne au prix du jeu", "Buy at catalogue price (offline)" },
        { "VOITURE SÉLECTIONNÉE DANS LE GARAGE", "CURRENTLY SELECTED GARAGE CAR" },
        { "VOIR LE PRIX", "CHECK PRICE" },
        { "ACHETER • 1 CR", "BUY • 1 CR" },
        { "Une opération est déjà en cours.", "An operation is already pending." },
        { "Lecture du prix…", "Checking price…" },
        { "Achat en cours…", "Purchasing…" },
        { "Le prix est illisible.", "Cannot read the price." },
        { " gemmes", " gems" },
        { "Acheter la voiture sélectionnée pour ", "Buy the selected car for " },
        { "Achat hors ligne", "Offline purchase" },
        { "Achat annulé.", "Purchase cancelled." },
        { "Voiture achetée pour ", "Car purchased for " },
        { "Sélectionne une voiture dans le garage principal.", "Select a car in the main garage." },
        { "Cette voiture est déjà dans ton garage.", "You already own this car." },
        { "Solde insuffisant. Prix affiché : ", "Insufficient balance. Listed price: " },
        { "Aucun prix en CR ou en gemmes pour cette voiture dans le catalogue chargé.", "The loaded catalogue has no credit or gem price for this car." },
        { "La voiture sélectionnée a changé. Consulte à nouveau le prix.", "The selected car changed. Check its price again." },
        { "Cette voiture ne fait pas partie de ton garage.", "You do not own this car." },
        { "La Dodge Dart GT de départ reste dans le garage.", "The starter Dodge Dart GT stays in your garage." },
        { "Le remboursement dépasserait le solde maximal du jeu.", "The refund would exceed the game's maximum balance." },
        { "Le jeu n’a pas confirmé l’achat.", "The game did not confirm the purchase." },
        { "Achat confirmé. Le reçu transmis par le jeu est illisible.", "Purchase confirmed. Cannot read the game's receipt." },
        { ". Reçu enregistré.", ". Receipt saved." },
        { "Achat confirmé, reçu indisponible. ", "Purchase confirmed; receipt unavailable. " },
        { "Revendre la voiture sélectionnée", "Sell selected car" },
        { "REMBOURSEMENT AU PRIX PAYÉ", "REFUND THE RECORDED PURCHASE PRICE" },
        { "REVENDRE", "SELL" },
        { "Lecture du reçu…", "Reading receipt…" },
        { "Le jeu n’a pas confirmé la voiture sélectionnée.", "The game did not confirm the selected car." },
        { "Reçu illisible. ", "Cannot read receipt. " },
        { "Reçu inaccessible. ", "Cannot access receipt. " },
        { "Aucun reçu pour cette voiture. Le prix réellement payé est inconnu.", "No receipt exists for this car. Its actual purchase price is unknown." },
        { "Revendre la voiture sélectionnée pour ", "Sell the selected car for " },
        { " ? Ses améliorations seront retirées du garage.", "? Its upgrades will be removed from the garage." },
        { "Revente hors ligne", "Offline sale" },
        { "Revente annulée.", "Sale cancelled." },
        { "Revente en cours…", "Selling…" },
        { "Le retrait de la voiture a échoué.", "Could not remove the car." },
        { "Voiture vendue pour ", "Car sold for " },
        { ". Historique indisponible.", ". History unavailable." },
        { "Voiture vendue. Remboursement de ", "Car sold. Refund: " },
        { "Ouvre un menu du jeu pour appliquer l’opération en attente.", "Open an in-game menu to apply the pending operation." },
        { "Définir les pièces de fusion", "Set fusion coins" },
        { "Définir les gemmes", "Set gems" },
        { "Définir les crédits", "Set credits" },
        { "pièces de fusion", "fusion coins" },
        { "gemmes", "gems" },
        { "NOUVEAU SOLDE", "NEW BALANCE" },
        { "Une modification du solde est déjà en cours.", "A balance change is already pending." },
        { "Application du solde en cours…", "Applying balance…" },
        { "Solde appliqué à ", "Balance set to " },
        { "Le jeu n’a pas confirmé le nouveau solde.", "The game did not confirm the new balance." },
        { "ACTIF  •  ", "ACTIVE  •  " },
        { " AU PROCHAIN DÉPART", " AT NEXT RACE START" },
        { "PAR DÉFAUT  •  VALEUR DE LA COURSE", "DEFAULT  •  ORIGINAL RACE VALUE" },
        { " (JOUEUR COMPRIS)", " (INCLUDING PLAYER)" },
        { " au prochain départ", " at next race start" },
        { " : valeur d’origine au prochain départ", ": original value at next race start" },
        { " : valeur personnalisée mémorisée", ": custom value saved" },
        { "VALEUR BORNÉE  •  ANTI-CRASH", "BOUNDED VALUE  •  CRASH GUARD" },
        { "ACTIF : ", "ACTIVE: " },
        { "DOUBLE INJECTION SYNCHRONISÉE", "SYNCHRONIZED DUAL INJECTION" },
        { "  •  deux chemins actifs", "  •  both paths active" },
        { "OUVRIR D'ABORD LA PAGE COURSE RAPIDE", "OPEN THE QUICK RACE PAGE FIRST" },
        { "Circuits Course rapide injectés", "Quick Race tracks enabled" },
        { "Circuits Course rapide restaurés", "Quick Race tracks restored" },
        { "Jeu introuvable.", "Game not found." },
        { " • Retour à l’état initial : ", " • Rollback: " },
        { "Grille de départ : ", "Starting grid: " },
        { "Boosters : ", "Boosters: " },
        { "définition introuvable", "definition not found" },
        { "Accès carrière : ", "Career access: " },
        { " outil(s) mémorisé(s) réappliqué(s)", " saved tool(s) restored" },
        { " ajouté aux favoris", " added to favorites" },
        { " retiré des favoris", " removed from favorites" },
        { "OUVRIR", "OPEN" },
        { "Débloquer toute la carrière et le Motoblitz", "Unlock all career seasons and Moto Blitz" },
        { "FIN DE COURSE", "RACE FINISH" },
        { "AFFICHAGE", "DISPLAY" },
        { "Table Cheat Engine 4.8.0h • import natif", "Cheat Engine 4.8.0h • native port" },
        { "TABLE CE 4.8.0H  •  PATCHS DIRECTS", "CE TABLE 4.8.0H  •  DIRECT PATCHES" },
        { "13 modules importés  •  signatures contrôlées avant chaque écriture", "13 ported modules • signatures checked before every write" },
        { "TABLE ORIGINALE  •  SIGNATURE 4.8.0.7", "ORIGINAL TABLE  •  BUILD 4.8.0.7 SIGNATURE" },
        { "lecture mémoire impossible", "cannot read memory" },
        { "signature inattendue à +", "unexpected signature at +" },
        { "écriture mémoire impossible", "cannot write memory" },
        { "Kits PRO hors ligne", "Offline PRO upgrades" },
        { "Ignorer la classe et la voiture imposée", "Ignore class and required car" },
        { "Code du solde trop grand.", "Wallet code exceeds allocation." },
        { "signature inattendue", "unexpected signature" },
        { "allocation distante ", "remote allocation failed: " },
        { "code trop grand", "code exceeds allocation" },
        { "écriture code cave", "cannot write code cave" },
        { "écriture branche", "cannot write branch" },
        { "restauration impossible", "cannot restore original bytes" },
        { "Ouvre une seule instance du jeu.", "Open exactly one game instance." },
        { "Actualisation de la carrière inaccessible.", "Career refresh is unavailable." },
        { "Impossible d’actualiser la sélection du véhicule.", "Cannot refresh the car selector." },
        { "Grille de départ indisponible.", "Starting grid is unavailable." },
        { "Jeu inaccessible.", "Cannot access the game." },
        { "Impossible de transmettre la valeur au jeu.", "Cannot send the value to the game." },
        { "Caractères invalides.", "Invalid characters." },
        { "Le nom doit occuper de 1 à 64 octets.", "The name must be between 1 and 64 bytes." },
        { "Un seul jeu doit être ouvert.", "Exactly one game instance must be running." },
        { "Le jeu est inaccessible.", "Cannot access the game." },
        { "État de la demande illisible.", "Cannot read request status." },
        { "Ouvre le tuning d’une voiture pour appliquer le nom en attente.", "Open a car's tuning screen to apply the queued name." },
        { "Impossible de transmettre le nom au jeu.", "Cannot send the name to the game." },
        { "Ouvre une seule instance d’Asphalt 8.", "Open exactly one Asphalt 8 instance." },
        { "Les boosters du jeu sont inaccessibles.", "Cannot access the game's boosters." },
        { "Impossible de mettre à jour les boosters.", "Cannot update boosters." },
        { "Le reçu doit indiquer un montant positif.", "The receipt must contain a positive amount." },
        { "Le solde du jeu est inaccessible.", "Cannot access the game's balance." },
        { "État du solde illisible.", "Cannot read balance status." },
        { "Impossible de transmettre la devise du reçu.", "Cannot send the receipt currency." },
        { "Impossible d’envoyer le nouveau solde.", "Cannot send the new balance." },
        { "module non actif", "module is inactive" },
        { "ouvrez la page Course rapide puis réessayez (", "open Quick Race and try again (" },
        { " absent)", " missing)" },
        { "allocation distante", "remote allocation failed" },
        { "écriture code", "cannot write code" },
        { "Grille native absente", "Native grid resource is missing" },
        { "Image native de grille trop grande", "Native grid image exceeds allocation" },
        { "Relocation de grille invalide", "Invalid grid relocation" },
        { "Relocation de grille non prise en charge", "Unsupported grid relocation" },
    };

    private static string NormalizeSavedName(string name)
    {
        string english;
        return LegacyEnglishNames.TryGetValue(name, out english) ? english : name;
    }

    private void LoadToolStates()
    {
        try { if(File.Exists(ToolStatesPath)) foreach(string raw in File.ReadAllLines(ToolStatesPath)) { string line=NormalizeSavedName(raw.Trim()); if(line.StartsWith("@")&&line.Contains("=")){int at=line.IndexOf('=');string key=line.Substring(1,at-1);int value;if(int.TryParse(line.Substring(at+1),out value)){if(key=="Racers")value=Math.Max(1,Math.Min(61,value));if(key=="Laps")value=Math.Max(1,Math.Min(999999,value));savedValues[key]=value;InjectionCatalog.SetValue(key,value);}} else if(!string.IsNullOrWhiteSpace(line)&&line!="Credits"&&line!="Racer Descriptors"&&line!="Racer Runtime Entries") savedTools.Add(line); } }
        catch { }
    }

    private void SetToolSaved(string name,bool enabled)
    {
        if(enabled)savedTools.Add(name);else savedTools.Remove(name);
        SaveToolStateFile();
    }

    private void SetToolValue(string name,int value){savedValues[name]=value;SaveToolStateFile();}
    private void SaveToolStateFile()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(ToolStatesPath));var lines=new List<string>(savedTools);foreach(var item in savedValues)lines.Add("@"+item.Key+"="+item.Value);File.WriteAllLines(ToolStatesPath,lines.ToArray()); } catch { }
    }

    private bool EnsureRaceGrid(out string error)
    {
        const string name = GridSpawnHook.Name;
        error = "";
        if(importedStates.ContainsKey(name)) return true;
        if(!NativeInjectionEngine.Toggle(InjectionCatalog.Find(name),true,out error)) return false;
        importedStates[name] = true;
        return true;
    }

    private bool ApplyRaceValue(string name, int value, out string error)
    {
        if (observedGamePid == 0)
        {
            error = "Game not found.";
            return false;
        }
        if (!EnsureRaceGrid(out error)) return false;
        bool racers = name == "Racers";
        int offset = racers ? GridSpawnHook.RacersOffset : GridSpawnHook.LapsOffset;
        if (installedRaceSettings.Contains(name))
            return NativeInjectionEngine.SetRaceValue(observedGamePid, offset, value, out error);

        InjectionHack descriptors = racers ? InjectionCatalog.Find("Racer Descriptors") : null;
        if (racers && !NativeInjectionEngine.Toggle(descriptors, true, out error))
            return false;
        if (NativeInjectionEngine.Toggle(InjectionCatalog.Find(name), true, out error))
        {
            installedRaceSettings.Add(name);
            return NativeInjectionEngine.SetRaceValue(observedGamePid, offset, value, out error);
        }
        if (racers)
        {
            string rollbackError;
            if (!NativeInjectionEngine.Toggle(descriptors, false, out rollbackError))
                error += " • Rollback: " + rollbackError;
        }
        return false;
    }

    private void RestoreToolsForNewGame()
    {
        Process[] games=Process.GetProcessesByName("Asphalt8");
        if(games.Length==0)
        {
            bool hadGame=observedGamePid!=0;
            observedGamePid=0;
            Array.Clear(mutatorApplied,0,mutatorApplied.Length);
            if(hadGame && currentSection=="IN-GAME MUTATORS") ShowSection(currentSection);
            return;
        }
        int pid=games[0].Id; if(pid==observedGamePid)return; observedGamePid=pid; importedStates.Clear();
        installedRaceSettings.Clear();
        string gridError;
        bool gridReady=EnsureRaceGrid(out gridError);
        if(!gridReady) Log("Starting grid: "+gridError);
        foreach (string name in new[] { "Offline Standard Upgrade Credits", "Offline Standard Upgrade Gems" })
        {
            string upgradeError;
            if (!NativeInjectionEngine.Toggle(InjectionCatalog.Find(name), true, out upgradeError))
                Log(name + ": " + upgradeError);
        }
        int boosterMask=UnlimitedBoosterMask(savedTools);
        if (boosterMask!=0)
        {
            string boosterError;
            if (NativeInjectionEngine.SetUnlimitedBoosterMask(boosterMask, out boosterError))
            {
                foreach (string booster in UnlimitedBoosterNames)
                    if (savedTools.Contains(booster)) importedStates[booster]=true;
            }
            else Log("Boosters: "+boosterError);
        }
        foreach(string name in new List<string>(savedTools))
        {
            if (name == CheatTableForm.UnlockCareer || name == InjectionCatalog.CareerCarChoice) continue;
            if (Array.IndexOf(UnlimitedBoosterNames,name)>=0) continue;
            string error="definition not found"; DirectHack direct=CheatTableForm.Find(name); InjectionHack injection=InjectionCatalog.Find(name);
            bool ok;
            if (name == "Laps" || name == "Racers")
                ok = ApplyRaceValue(name, InjectionCatalog.GetValue(name), out error);
            else ok=direct!=null?NativePatchEngine.Toggle(direct,true,out error):injection!=null&&NativeInjectionEngine.Toggle(injection,true,out error);
            if(ok) importedStates[name]=true; else Log("Restore "+name+" failed: "+error);
        }
        if (savedTools.Contains(CheatTableForm.UnlockCareer) || savedTools.Contains(InjectionCatalog.CareerCarChoice))
        {
            string careerError;
            if (!SetCareerAccess(true, out careerError)) Log("Career access: " + careerError);
        }
        RestoreMutatorsForNewGame();
        ShowSection(currentSection);
        status.Text=importedStates.Count+" saved tool(s) restored";
    }

    private void SaveFavorites()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FavoritesPath)); var lines=new List<string>();
            foreach(var item in favorites) lines.Add(item.Key+"|"+item.Value); File.WriteAllLines(FavoritesPath,lines.ToArray());
        }
        catch { }
    }

    private void AddFavoriteButton(Panel row,string name)
    {
        var star=new Button { Text=favorites.ContainsKey(name)?"★":"☆",FlatStyle=FlatStyle.Flat,ForeColor=Color.FromArgb(255,195,64),BackColor=Color.Transparent,Font=new Font("Segoe UI Symbol",14f),Size=new Size(34,34),Location=new Point(488,12),TabStop=false };
        star.FlatAppearance.BorderSize=0;
        star.Click += (_,__) => { if(favorites.ContainsKey(name)){favorites.Remove(name);star.Text="☆";}else{favorites[name]=currentSection;star.Text="★";}SaveFavorites();status.Text=name+(favorites.ContainsKey(name)?" added to favorites":" removed from favorites"); };
        row.Controls.Add(star); star.BringToFront();
    }

    private Control MakeFavoriteShortcut(string name,string section)
    {
        var row=new Panel { Width=690,Height=58,Margin=new Padding(0,0,0,8),BackColor=panel };
        row.Controls.Add(new Label { Text="★  "+name,Font=new Font("Segoe UI Semibold",10f),ForeColor=Color.White,Location=new Point(18,9),Size=new Size(475,24) });
        row.Controls.Add(new Label { Text=section,Font=new Font("Segoe UI Semibold",7.5f),ForeColor=muted,Location=new Point(18,33),AutoSize=true });
        var open=new Button { Text="OPEN",FlatStyle=FlatStyle.Flat,ForeColor=Color.White,BackColor=Color.FromArgb(0,122,160),Size=new Size(145,36),Location=new Point(525,11),TabStop=false };
        open.Click += (_,__) => ShowSection(section); row.Controls.Add(open); return row;
    }

    private void BeginDrag(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            dragOrigin = new Point(e.X, e.Y);
    }

    private void ContinueDrag(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        Point screen = ((Control)sender).PointToScreen(e.Location);
        Location = new Point(screen.X - dragOrigin.X, screen.Y - dragOrigin.Y);
    }
}
