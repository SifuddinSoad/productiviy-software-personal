using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FocusLock.App.Lock;

/// <summary>A bare black window that covers one non-primary monitor for the length of a session.</summary>
internal sealed class MonitorBlocker : Window
{
    public MonitorBlocker()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = false;
        Background = new SolidColorBrush(Color.FromRgb(0x0e, 0x0f, 0x10));
        Title = "FocusLock";
        Left = -10000;
        Top = -10000;
        Width = 1;
        Height = 1;
    }

    public void CoverMonitor(Win32.Rect bounds)
    {
        Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetWindowPos(hwnd, Win32.HwndTopmost,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, Win32.SwpShowWindow | Win32.SwpNoActivate);
    }
}
