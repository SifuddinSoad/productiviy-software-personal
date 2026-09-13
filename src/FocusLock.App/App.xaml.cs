using System.Windows;
using FocusLock.App.Lock;
using FocusLock.App.Services;
using FocusLock.App.ViewModels;
using FocusLock.App.Views;
using FocusLock.Core;
using FocusLock.Core.Sessions;

namespace FocusLock.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Cleanup deliberately skips the single-instance check: it is the way out when a copy is stuck.
        if (e.Args.Contains("--cleanup", StringComparer.OrdinalIgnoreCase))
        {
            var log = Cleanup.Run();
            MessageBox.Show(string.Join(Environment.NewLine, log), "Focus Mood cleanup",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        if (!SingleInstance.TryAcquire())
        {
            SingleInstance.FocusRunningInstance();
            Shutdown();
            return;
        }
        Exit += (_, _) => SingleInstance.Release();

        var store = new SessionStore(AppPaths.SessionsDir);
        var active = new ActiveSessionStore(AppPaths.ActiveFile);

        // Nothing is locked: clear any policy left behind by an earlier crash.
        if (active.Get() is null)
            TaskManagerPolicy.TryRestore();

        var main = new MainViewModel(store, active);
        var window = new MainWindow { DataContext = main };
        MainWindow = window;

        var kiosk = new KioskController(window, main.Runtime);
        main.EmergencyExitRequested = () =>
            new EmergencyExitDialog { Owner = window }.ShowDialog() == true;
        Exit += (_, _) => kiosk.Dispose();

        window.Show();
        main.Startup();
    }
}
