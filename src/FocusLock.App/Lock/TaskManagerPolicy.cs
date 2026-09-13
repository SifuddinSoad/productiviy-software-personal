using System.IO;
using System.Security;
using Microsoft.Win32;

namespace FocusLock.App.Lock;

/// <summary>HKCU DisableTaskMgr policy: the one thing Ctrl+Alt+Del would otherwise give access to.</summary>
internal static class TaskManagerPolicy
{
    public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    public const string ValueName = "DisableTaskMgr";

    public static bool IsDisabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is int v && v != 0;
    }

    /// <returns>false when the current user may not write the policy key.</returns>
    public static bool TryDisable()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            key.SetValue(ValueName, 1, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            return false;
        }
    }

    public static bool TryRestore()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            return false;
        }
    }
}
