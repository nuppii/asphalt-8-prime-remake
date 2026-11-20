using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// Temporary keyboard capture for the foreground game's active native text field.
// The hook only queues input. The UI timer sends it through the native editor mailbox.
internal sealed class NativeKeyboardInput : IDisposable
{
    private const int Command = 0x110000;
    private readonly Timer timer = new Timer { Interval = 15 };
    private readonly Queue<int> pending = new Queue<int>();
    private readonly KeyboardHook callback;
    private readonly byte[] keys = new byte[256];
    private readonly bool[] handled = new bool[256];
    private readonly StringBuilder translated = new StringBuilder(16);
    private IntPtr hook, gameWindow;
    private int pid, editToken;
    private int tabPid;
    private bool tabHeld;
    private uint gameThread;
    private bool sending;
    private string accent;

    private delegate IntPtr KeyboardHook(int code, IntPtr message, IntPtr data);
    private delegate bool WindowVisitor(IntPtr window, IntPtr state);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, KeyboardHook filter, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr handle, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int process);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, WindowVisitor visitor, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint key, uint scan, byte[] state, StringBuilder buffer, int length, uint flags, IntPtr layout);

    internal NativeKeyboardInput()
    {
        callback = OnKey;
        timer.Tick += (_, __) => Poll();
        timer.Start();
    }

    private static bool Down(int key) { return GetAsyncKeyState(key) < 0; }

    private void Poll()
    {
        IntPtr foreground = GetForegroundWindow();
        int process;
        byte[] state;
        if (!NativeInjectionEngine.ReadNativeLobbyState(out process, out state))
        {
            tabPid=0;tabHeld=false;Reset();return;
        }
        bool tab=foreground!=IntPtr.Zero&&Down(9)&&BitConverter.ToInt32(state,NativeNetworkState.EditToken)==0&&IsGameWindow(foreground,process);
        if((process!=tabPid||tabHeld!=tab)&&
           NativeInjectionEngine.WriteNativeLobbyState(process,NativeNetworkState.RaceTabHeld,BitConverter.GetBytes(tab?1:0)))
        {tabPid=process;tabHeld=tab;}
        if (foreground == IntPtr.Zero || !NativeNetworkState.Valid(state) || BitConverter.ToInt32(state, 28) != 4)
        {
            Reset();
            return;
        }
        int token = BitConverter.ToInt32(state, NativeNetworkState.EditToken);
        if (token < 1 || token > 2047)
        {
            Reset();
            return;
        }

        if (process != pid || token != editToken || foreground != gameWindow)
        {
            Reset();
            int owner;
            uint thread = GetWindowThreadProcessId(foreground, out owner);
            if (owner != process)
            {
                bool containsGame = false;
                EnumChildWindows(foreground, (child, _) =>
                {
                    int childOwner;
                    uint childThread = GetWindowThreadProcessId(child, out childOwner);
                    containsGame = childOwner == process && IsWindowVisible(child);
                    if (containsGame) thread = childThread;
                    return !containsGame;
                }, IntPtr.Zero);
                if (!containsGame) return;
            }
            pid = process;
            editToken = token;
            gameWindow = foreground;
            gameThread = thread;
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) Reset();
        }

        int sequence = BitConverter.ToInt32(state, NativeNetworkState.KeySequence);
        if (pending.Count == 0 || sequence != BitConverter.ToInt32(state, NativeNetworkState.KeyAck))
            return;
        int code = pending.Peek();
        if (!NativeInjectionEngine.WriteNativeLobbyState(pid, NativeNetworkState.KeyWord, BitConverter.GetBytes((editToken << 21) | code))
            || !NativeInjectionEngine.WriteNativeLobbyState(pid, NativeNetworkState.KeySequence, BitConverter.GetBytes(unchecked(sequence + 1))))
            return;
        pending.Dequeue();
        sending = code == Command || code == Command + 1;
    }

    private static bool IsGameWindow(IntPtr window,int process)
    {
        int owner;GetWindowThreadProcessId(window,out owner);
        if(owner==process)return true;
        bool game=false;
        EnumChildWindows(window,(child,_)=>
        {
            int childOwner;GetWindowThreadProcessId(child,out childOwner);
            game=childOwner==process&&IsWindowVisible(child);
            return !game;
        },IntPtr.Zero);
        return game;
    }

    private IntPtr OnKey(int status, IntPtr message, IntPtr data)
    {
        if (status < 0 || gameWindow != GetForegroundWindow())
            return CallNextHookEx(hook, status, message, data);
        int key = Marshal.ReadInt32(data);
        if (key < 0 || key >= handled.Length) return CallNextHookEx(hook, status, message, data);
        int kind = message.ToInt32();
        if ((kind == 0x101 || kind == 0x105) && handled[key])
        {
            handled[key] = false;
            return new IntPtr(1);
        }
        if (kind != 0x100 && kind != 0x104) return CallNextHookEx(hook, status, message, data);
        try
        {
            int command = EditCommand(key);
            if (command >= 0)
            {
                if (!sending && pending.Count < 128) pending.Enqueue(Command + command);
                accent = null;
                handled[key] = true;
                return new IntPtr(1);
            }
            bool altGr = Down(0xa5);
            if ((!altGr && (Down(0x11) || Down(0x12))) || Down(0x5b) || Down(0x5c) || key < 0x20
                || (key >= 0x70 && key <= 0x87))
                return CallNextHookEx(hook, status, message, data);
            Array.Clear(keys, 0, keys.Length);
            keys[0x10] = Down(0x10) ? (byte)128 : (byte)0;
            keys[0x11] = altGr ? (byte)128 : (byte)0;
            keys[0x12] = keys[0xa5] = keys[0x11];
            keys[0x14] = (byte)(GetKeyState(0x14) & 1);
            keys[0x90] = (byte)(GetKeyState(0x90) & 1);
            translated.Length = 0;
            // Flag 4 leaves the OS dead-key buffer untouched.
            uint scan = unchecked((uint)Marshal.ReadInt32(data, 4));
            int count = ToUnicodeEx((uint)key, scan, keys, translated, translated.Capacity, 4,
                GetKeyboardLayout(gameThread));
            if (count == 0) return CallNextHookEx(hook, status, message, data);
            string value = translated.ToString();
            if (count < 0)
            {
                accent = value;
                handled[key] = true;
                return new IntPtr(1);
            }
            if (accent != null)
            {
                value = ComposeAccent(accent, value);
                accent = null;
            }
            for (int i = 0; !sending && i < value.Length && pending.Count < 128; i++)
            {
                if (char.IsLowSurrogate(value[i])) continue;
                int scalar = char.ConvertToUtf32(value, i);
                if (scalar >= 32 && (scalar < 127 || scalar >= 160)) pending.Enqueue(scalar);
            }
            handled[key] = true;
            return new IntPtr(1);
        }
        catch (ArgumentException)
        {
            return CallNextHookEx(hook, status, message, data);
        }
    }

    private static int EditCommand(int key)
    {
        if (Down(0x12) || Down(0x5b) || Down(0x5c)) return -1;
        if (Down(0x11)) return key == 'V' ? 3 : key == 'A' ? 4 : -1;
        switch (key)
        {
            case 0x0d: return 0;
            case 0x1b: return 1;
            case 0x08: return 2;
            case 0x25: return 5;
            case 0x27: return 6;
            case 0x24: return 7;
            case 0x23: return 8;
            case 0x2e: return 9;
            default: return -1;
        }
    }

    private static string ComposeAccent(string mark, string value)
    {
        if (value == " ") return mark;
        string combining;
        switch (mark)
        {
            case "`": combining = "\u0300"; break;
            case "´": case "'": combining = "\u0301"; break;
            case "^": combining = "\u0302"; break;
            case "~": combining = "\u0303"; break;
            case "¨": case "\"": combining = "\u0308"; break;
            case "¸": combining = "\u0327"; break;
            default: return mark + value;
        }
        if (value.Length != 1 || !char.IsLetter(value[0])) return mark + value;
        string combined = (value + combining).Normalize();
        return combined.Length == 1 ? combined : mark + value;
    }

    private void Reset()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = gameWindow = IntPtr.Zero;
        pid = editToken = 0;
        pending.Clear();
        Array.Clear(handled, 0, handled.Length);
        accent = null;
        sending = false;
    }

    public void Dispose()
    {
        timer.Dispose();
        Reset();
    }
}
