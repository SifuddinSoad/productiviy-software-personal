namespace FocusLock.App.Lock;

/// <summary>
/// Swallows the shortcuts that would otherwise leave the locked window: the Windows keys,
/// Alt+Tab, Alt+Esc, Ctrl+Esc, Alt+F4, Alt+Space and Ctrl+Shift+Esc.
/// Ctrl+Alt+Del cannot be hooked — the Task Manager policy covers what it would reach.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    // The delegate must stay referenced for as long as the hook lives, or it is collected
    // and the callback crashes the process.
    readonly Win32.HookProc _callback;
    IntPtr _hook;

    public KeyboardHook() => _callback = OnKey;

    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Install()
    {
        if (IsInstalled) return;
        _hook = Win32.SetWindowsHookEx(Win32.WhKeyboardLl, _callback, Win32.GetModuleHandle(null), 0);
    }

    public void Remove()
    {
        if (!IsInstalled) return;
        Win32.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);

        var message = (int)wParam;
        if (message is Win32.WmKeyDown or Win32.WmSysKeyDown)
        {
            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32.KbdLlHookStruct>(lParam);
            if (ShouldBlock((int)info.vkCode, (info.flags & Win32.LlkhfAltDown) != 0))
                return 1;   // handled: the key never reaches anyone
        }

        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    internal static bool ShouldBlock(int vkCode, bool altDown)
    {
        var ctrl = Win32.IsDown(Win32.VkControl);
        var shift = Win32.IsDown(Win32.VkShift);

        return vkCode switch
        {
            Win32.VkLWin or Win32.VkRWin => true,
            Win32.VkTab when altDown => true,
            Win32.VkEscape when altDown || ctrl => true,
            Win32.VkF4 when altDown => true,
            Win32.VkSpace when altDown => true,
            _ => false,
        } || (ctrl && shift && vkCode == Win32.VkEscape);
    }

    public void Dispose() => Remove();
}
