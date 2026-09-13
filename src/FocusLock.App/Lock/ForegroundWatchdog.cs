using System.Diagnostics;
using System.Windows.Threading;

namespace FocusLock.App.Lock;

/// <summary>
/// Puts the locked window back in front whenever anything else takes the foreground.
/// Windows raises an event for that, and a slow timer covers the cases it misses.
/// </summary>
internal sealed class ForegroundWatchdog : IDisposable
{
    readonly Win32.WinEventProc _callback;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly uint _ownProcessId = (uint)Environment.ProcessId;
    IntPtr _hook;
    Func<IntPtr>? _target;

    public ForegroundWatchdog() => _callback = OnForegroundChanged;

    /// <summary>Starts guarding; <paramref name="target"/> returns the window that should stay in front.</summary>
    public void Start(Func<IntPtr> target)
    {
        _target = target;
        _hook = Win32.SetWinEventHook(Win32.EventSystemForeground, Win32.EventSystemForeground,
            IntPtr.Zero, _callback, 0, 0, Win32.WineventOutofcontext);
        _timer.Tick += (_, _) => Enforce();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        if (_hook != IntPtr.Zero)
        {
            Win32.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
        _target = null;
    }

    void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd,
        int objectId, int childId, uint threadId, uint time) => Enforce();

    void Enforce()
    {
        if (_target is null) return;

        var foreground = Win32.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return;

        // Anything of ours counts as fine: the emergency dialog, tooltips, the monitor blockers.
        Win32.GetWindowThreadProcessId(foreground, out var pid);
        if (pid == _ownProcessId) return;

        BringToFront(_target());
    }

    /// <summary>
    /// Windows only lets the foreground process hand focus over, so attach to its input queue
    /// first and the call is allowed.
    /// </summary>
    public static void BringToFront(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        var foreground = Win32.GetForegroundWindow();
        var foreignThread = Win32.GetWindowThreadProcessId(foreground, out _);
        var ownThread = Win32.GetCurrentThreadId();

        var attached = foreignThread != 0 && foreignThread != ownThread
            && Win32.AttachThreadInput(ownThread, foreignThread, true);
        try
        {
            Win32.ShowWindow(hwnd, Win32.SwRestore);
            Win32.SetWindowPos(hwnd, Win32.HwndTopmost, 0, 0, 0, 0,
                Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpShowWindow);
            Win32.BringWindowToTop(hwnd);
            Win32.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) Win32.AttachThreadInput(ownThread, foreignThread, false);
        }

        Debug.WriteLineIf(Win32.GetForegroundWindow() != hwnd, "FocusLock: could not take the foreground");
    }

    public void Dispose() => Stop();
}
