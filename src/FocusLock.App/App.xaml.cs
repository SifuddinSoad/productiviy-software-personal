using System.Windows;

namespace FocusLock.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--cleanup", StringComparer.OrdinalIgnoreCase))
        {
            var log = Cleanup.Run();
            MessageBox.Show(string.Join(Environment.NewLine, log), "FocusLock cleanup",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
