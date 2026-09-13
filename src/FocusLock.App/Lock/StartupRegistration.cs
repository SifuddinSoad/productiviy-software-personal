using Microsoft.Win32;

namespace FocusLock.App.Lock;

/// <summary>HKCU Run entry that relaunches the app at logon while a session is active.</summary>
internal static class StartupRegistration
{
    public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "FocusLock";

    public static void Register()
    {
        var exe = Environment.ProcessPath!;
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, $"\"{exe}\" --resume", RegistryValueKind.String);
    }

    public static void Unregister()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is not null;
    }
}
