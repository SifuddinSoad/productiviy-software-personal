using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using FocusLock.App.Services;
using FocusLock.Core;
using FocusLock.Core.Models;
using FocusLock.Core.Sessions;
using Microsoft.Win32;

namespace FocusLock.App.Lock;

/// <summary>
/// Holds the screen for the length of a session: the window goes fullscreen and topmost, other
/// monitors are covered, escape shortcuts are swallowed, the window refuses to close, Task Manager
/// is switched off and a startup entry brings the app back after a restart.
///
/// Everything it does is undone in <see cref="Release"/>, which also runs from
/// <see cref="Cleanup"/> if the app never got the chance.
/// </summary>
internal sealed class KioskController : IDisposable
{
    readonly Window _window;
    readonly SessionRuntime _runtime;
    readonly KeyboardHook _hook = new();
    readonly ForegroundWatchdog _watchdog = new();
    readonly List<MonitorBlocker> _blockers = [];
    readonly DispatcherTimer _guard = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Stopwatch _lockedFor = new();

    WindowState _savedState;
    WindowStyle _savedStyle;
    ResizeMode _savedResize;
    double _savedLeft, _savedTop, _savedWidth, _savedHeight;

    public bool IsLocked { get; private set; }

    public KioskController(Window window, SessionRuntime runtime)
    {
        _window = window;
        _runtime = runtime;
        _runtime.Started += Engage;
        _runtime.Ended += _ => Release();
        _guard.Tick += (_, _) => Guard();
    }

    void Engage()
    {
        if (IsLocked || _runtime.Session is not { } session) return;
        IsLocked = true;
        _lockedFor.Restart();

        _savedState = _window.WindowState;
        _savedStyle = _window.WindowStyle;
        _savedResize = _window.ResizeMode;
        _savedLeft = _window.Left;
        _savedTop = _window.Top;
        _savedWidth = _window.Width;
        _savedHeight = _window.Height;

        _window.WindowState = WindowState.Normal;   // maximised keeps the taskbar visible
        _window.WindowStyle = WindowStyle.None;
        _window.ResizeMode = ResizeMode.NoResize;
        _window.Topmost = true;
        _window.Closing += RefuseClose;

        Arrange();
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;

        _hook.Install();
        _watchdog.Start(() => new WindowInteropHelper(_window).Handle);

        // Remember whether the policy was already on, so unlocking leaves the user's own setting alone.
        var alreadyDisabled = TaskManagerPolicy.IsDisabled();
        _runtime.SetTaskManagerWasDisabled(alreadyDisabled);
        if (!alreadyDisabled) TaskManagerPolicy.TryDisable();

        StartupRegistration.Register();
        _guard.Start();
    }

    public void Release()
    {
        if (!IsLocked) return;
        IsLocked = false;
        _guard.Stop();
        _lockedFor.Stop();

        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        _watchdog.Stop();
        _hook.Remove();

        foreach (var blocker in _blockers) blocker.Close();
        _blockers.Clear();

        _window.Closing -= RefuseClose;
        _window.Topmost = false;
        _window.WindowStyle = _savedStyle;
        _window.ResizeMode = _savedResize;
        _window.Left = _savedLeft;
        _window.Top = _savedTop;
        _window.Width = _savedWidth;
        _window.Height = _savedHeight;
        _window.WindowState = _savedState;

        var pointer = new ActiveSessionStore(AppPaths.ActiveFile).Get();
        if (pointer is not { TaskMgrWasDisabled: true }) TaskManagerPolicy.TryRestore();
        StartupRegistration.Unregister();
    }

    /// <summary>Covers the primary monitor with the app and every other monitor with a blank window.</summary>
    void Arrange()
    {
        var monitors = Win32.Monitors();
        if (monitors.Count == 0) return;

        var hwnd = new WindowInteropHelper(_window).Handle;
        var primary = monitors[0].Bounds;
        Win32.SetWindowPos(hwnd, Win32.HwndTopmost,
            primary.Left, primary.Top, primary.Width, primary.Height, Win32.SwpShowWindow);

        foreach (var blocker in _blockers) blocker.Close();
        _blockers.Clear();

        foreach (var (bounds, _) in monitors.Skip(1))
        {
            var blocker = new MonitorBlocker();
            blocker.CoverMonitor(bounds);
            _blockers.Add(blocker);
        }
    }

    void OnDisplaysChanged(object? sender, EventArgs e)
    {
        if (!IsLocked) return;
        _window.Dispatcher.BeginInvoke(Arrange);
    }

    void RefuseClose(object? sender, CancelEventArgs e) => e.Cancel = true;

    /// <summary>
    /// Runs once a second: keeps the window on top, and forces the lock open if the session has
    /// gone away or has outstayed its planned length by a wide margin.
    /// </summary>
    void Guard()
    {
        if (!IsLocked) return;

        if (_runtime.Session is not { } session)
        {
            Release();
            return;
        }

        if (LockSafety.ShouldForceUnlock(session.PlannedSeconds, _lockedFor.Elapsed.TotalSeconds))
        {
            Debug.WriteLine("FocusLock: safety cap reached, releasing the lock");
            _runtime.End(EndReason.Completed);   // Ended releases the lock
            return;
        }

        if (!_window.Topmost) _window.Topmost = true;
    }

    public void Dispose()
    {
        Release();
        _hook.Dispose();
        _watchdog.Dispose();
        _guard.Stop();
    }
}
