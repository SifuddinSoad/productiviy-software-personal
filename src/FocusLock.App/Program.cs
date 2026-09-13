using System.ServiceProcess;
using FocusLock.App.Guard;

namespace FocusLock.App;

/// <summary>
/// One binary, two jobs. Launched normally it is the WPF app; launched by the Service Control
/// Manager with <c>--service</c> it is the guard. Sharing the exe keeps a single self-contained
/// publish, so the service can never be a build behind the app it restarts.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--service", StringComparer.OrdinalIgnoreCase))
        {
            ServiceBase.Run(new GuardService());
            return 0;
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
