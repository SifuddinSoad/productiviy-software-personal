using System.IO;

namespace FocusLock.App.Services;

/// <summary>
/// Where this copy of the app is running from. It matters: Windows runs the logon entry before
/// network and shared folders are ready, so a session cannot resume after a restart unless the
/// app sits on a local disk.
/// </summary>
public static class AppLocation
{
    public static string ExePath { get; } = Environment.ProcessPath ?? "";

    public static string InstallDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "FocusLock");

    public static string InstalledExePath => Path.Combine(InstallDirectory, "FocusLock.App.exe");

    /// <summary>True for a UNC path such as \\VBoxSvr\share, or a mapped network drive.</summary>
    public static bool IsOnNetworkPath
    {
        get
        {
            if (string.IsNullOrEmpty(ExePath)) return false;
            if (ExePath.StartsWith(@"\\", StringComparison.Ordinal)) return true;
            try
            {
                var root = Path.GetPathRoot(ExePath);
                return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    /// <summary>A session can only survive a restart when the app runs from a local disk.</summary>
    public static bool CanResumeAfterRestart => !IsOnNetworkPath;
}
