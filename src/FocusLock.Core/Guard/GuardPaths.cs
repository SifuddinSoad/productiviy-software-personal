namespace FocusLock.Core.Guard;

/// <summary>
/// Where the guard keeps its state. ProgramData rather than the user's own folder: the point of the
/// service is to hold something the locked user cannot quietly delete. <c>scripts\install-service.ps1</c>
/// sets the permissions — the root is read-only to normal users, <see cref="InboxDir"/> is not, since
/// that is how the app talks to the service.
/// </summary>
public static class GuardPaths
{
    public const string ServiceName = "FocusLockGuard";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FocusLock");

    public static string StateFile(string root) => Path.Combine(root, "guard.json");

    public static string InboxDir(string root) => Path.Combine(root, "inbox");
}
