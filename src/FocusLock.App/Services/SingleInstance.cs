using System.Diagnostics;
using System.Threading;
using FocusLock.App.Lock;

namespace FocusLock.App.Services;

/// <summary>
/// Only one copy may run: a second one would install its own hooks and fight the first over the
/// lock. Opening the app again just brings the running window forward.
/// </summary>
internal static class SingleInstance
{
    // Local\ scopes the name to this logon session, which is exactly the scope of a lock.
    const string Name = @"Local\FocusLock.SingleInstance";

    static Mutex? _held;

    public static bool TryAcquire()
    {
        _held = new Mutex(initiallyOwned: true, Name, out var isOnlyInstance);
        if (isOnlyInstance) return true;

        _held.Dispose();
        _held = null;
        return false;
    }

    public static void Release()
    {
        _held?.Dispose();
        _held = null;
    }

    /// <summary>Brings the copy that is already running to the front, so the click is not ignored.</summary>
    public static void FocusRunningInstance()
    {
        using var self = Process.GetCurrentProcess();
        foreach (var other in Process.GetProcessesByName(self.ProcessName))
        {
            using (other)
            {
                if (other.Id == self.Id || other.MainWindowHandle == IntPtr.Zero) continue;
                ForegroundWatchdog.BringToFront(other.MainWindowHandle);
                return;
            }
        }
    }
}
