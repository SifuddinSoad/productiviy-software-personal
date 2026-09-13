namespace FocusLock.Core;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusLock");

    public static string SessionsDir => Path.Combine(Root, "data", "sessions");

    public static string ActiveFile => Path.Combine(Root, "active.json");
}
