using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace FocusLock.App.Guard;

/// <summary>
/// Starts the app on the desktop of whoever is signed in at the machine, from a service that has no
/// desktop of its own. The only reason the guard exists: after a restart this runs as the user's
/// session comes up, instead of waiting for the logon Run entry a few seconds later.
/// </summary>
internal static class SessionLauncher
{
    /// <summary>Is the app already up on the console desktop? The service shares the same binary, so
    /// its own process is ruled out by only counting processes in the console session.</summary>
    public static bool IsRunning(string exePath)
    {
        var console = ServiceWin32.WTSGetActiveConsoleSessionId();
        if (console is ServiceWin32.InvalidSessionId or 0) return false;

        var name = Path.GetFileNameWithoutExtension(exePath);
        if (string.IsNullOrEmpty(name)) return false;

        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                if (ServiceWin32.ProcessIdToSessionId((uint)process.Id, out var session) && session == console)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Starts <paramref name="exePath"/> as the signed-in user. Returns false whenever that is not
    /// possible right now — nobody signed in, a different user, a missing file — and the guard simply
    /// tries again on its next tick.
    /// </summary>
    public static bool Launch(string exePath, string arguments, string expectedSid)
    {
        if (!File.Exists(exePath)) return false;

        var console = ServiceWin32.WTSGetActiveConsoleSessionId();
        if (console is ServiceWin32.InvalidSessionId or 0) return false;

        // Fails while the sign-in screen is up, which is exactly when there is nothing to lock.
        if (!ServiceWin32.WTSQueryUserToken(console, out var token)) return false;

        var primary = IntPtr.Zero;
        var environment = IntPtr.Zero;
        try
        {
            if (!IsExpectedUser(token, expectedSid)) return false;

            if (!ServiceWin32.DuplicateTokenEx(token, ServiceWin32.MaximumAllowed, IntPtr.Zero,
                    ServiceWin32.SecurityImpersonation, ServiceWin32.TokenPrimary, out primary))
                return false;

            ServiceWin32.CreateEnvironmentBlock(out environment, primary, false);

            var startup = new ServiceWin32.StartupInfo
            {
                cb = Marshal.SizeOf<ServiceWin32.StartupInfo>(),
                lpDesktop = @"winsta0\default",
                dwFlags = ServiceWin32.StartfUseShowWindow,
                wShowWindow = ServiceWin32.SwShow,
            };

            var started = ServiceWin32.CreateProcessAsUser(
                primary, exePath, $"\"{exePath}\" {arguments}", IntPtr.Zero, IntPtr.Zero, false,
                ServiceWin32.CreateUnicodeEnvironment | ServiceWin32.NormalPriorityClass,
                environment, Path.GetDirectoryName(exePath), ref startup, out var info);

            if (started)
            {
                ServiceWin32.CloseHandle(info.hThread);
                ServiceWin32.CloseHandle(info.hProcess);
            }
            return started;
        }
        finally
        {
            if (environment != IntPtr.Zero) ServiceWin32.DestroyEnvironmentBlock(environment);
            if (primary != IntPtr.Zero) ServiceWin32.CloseHandle(primary);
            ServiceWin32.CloseHandle(token);
        }
    }

    /// <summary>A session belongs to the user who started it; another account signing in is not locked.</summary>
    static bool IsExpectedUser(IntPtr token, string expectedSid)
    {
        if (string.IsNullOrEmpty(expectedSid)) return true;
        try
        {
            using var identity = new WindowsIdentity(token);
            return identity.User?.Value == expectedSid;
        }
        catch (Exception e) when (e is ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
