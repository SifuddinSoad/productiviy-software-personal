using System.Runtime.InteropServices;

namespace FocusLock.App.Lock;

/// <summary>The Win32 surface the lock needs. Kept in one place so the rest stays readable.</summary>
internal static class Win32
{
    // ---- windows ----
    public const int SwpNoSize = 0x0001;
    public const int SwpNoMove = 0x0002;
    public const int SwpNoActivate = 0x0010;
    public const int SwpShowWindow = 0x0040;
    public static readonly IntPtr HwndTopmost = new(-1);
    public static readonly IntPtr HwndNoTopmost = new(-2);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public const int SwRestore = 9;

    // ---- monitors ----
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    public const uint MonitorPrimary = 0x1;

    delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    /// <summary>Every monitor's pixel bounds, primary first.</summary>
    public static List<(Rect Bounds, bool IsPrimary)> Monitors()
    {
        var found = new List<(Rect, bool)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref Rect _, IntPtr _) =>
        {
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
                found.Add((info.rcMonitor, (info.dwFlags & MonitorPrimary) != 0));
            return true;
        }, IntPtr.Zero);

        return [.. found.OrderByDescending(m => m.Item2)];
    }

    // ---- low level keyboard hook ----
    public const int WhKeyboardLl = 13;
    public const int WmKeyDown = 0x0100;
    public const int WmSysKeyDown = 0x0104;
    public const uint LlkhfAltDown = 0x20;

    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    public const int VkShift = 0x10;
    public const int VkControl = 0x11;
    public const int VkTab = 0x09;
    public const int VkEscape = 0x1B;
    public const int VkF4 = 0x73;
    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;
    public const int VkSpace = 0x20;

    public static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    // ---- foreground events ----
    public const uint EventSystemForeground = 0x0003;
    public const uint WineventOutofcontext = 0x0000;
    public const uint WineventSkipownprocess = 0x0002;

    public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd,
        int objectId, int childId, uint threadId, uint time);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hook);
}
