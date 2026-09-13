using System.IO;
using System.ServiceProcess;
using System.Threading;
using FocusLock.Core.Guard;

namespace FocusLock.App.Guard;

/// <summary>
/// Runs as LocalSystem and does one thing: while a session is being guarded, make sure the app is up
/// on the user's desktop. It never blocks anything itself — all the locking still belongs to the app,
/// so a guard that misbehaves can only start a program, never hold the screen on its own.
///
/// It lets go readily: when the planned time is spent, when the app will not stay up, and when the
/// state file is unreadable. See <see cref="GuardPolicy"/>.
/// </summary>
internal sealed class GuardService : ServiceBase
{
    public const string Name = GuardPaths.ServiceName;

    static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    readonly GuardStore _store = new();
    readonly Queue<DateTime> _launches = new();
    Timer? _timer;

    public GuardService()
    {
        ServiceName = Name;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        Log("started");
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, Interval);
    }

    protected override void OnStop()
    {
        _timer?.Dispose();
        _timer = null;
        Log("stopped");
    }

    void Tick()
    {
        // A service that throws here is restarted by Windows and loses nothing, but a quiet log
        // entry is far easier to follow than a restart loop.
        try
        {
            Step(DateTime.UtcNow);
        }
        catch (Exception e)
        {
            Log($"tick failed: {e.Message}");
        }
    }

    internal void Step(DateTime nowUtc)
    {
        var state = _store.Read();

        var commands = _store.Drain();
        if (commands.Count > 0)
        {
            foreach (var command in commands) state = GuardCommands.Apply(state, command);
            Persist(state);
            _launches.Clear();
        }

        Forget(nowUtc);
        var running = state is not null && SessionLauncher.IsRunning(state.AppPath);

        switch (GuardPolicy.Decide(state, running, nowUtc, _launches.Count))
        {
            case GuardAction.Expire:
                Log($"letting go of {state?.SessionId}");
                _store.Clear();
                _launches.Clear();
                break;

            case GuardAction.Relaunch:
                _launches.Enqueue(nowUtc);
                var started = SessionLauncher.Launch(state!.AppPath, "--resume", state.UserSid);
                if (started) Log($"brought the app back for {state.SessionId}");
                break;
        }
    }

    void Persist(GuardState? state)
    {
        if (state is null) _store.Clear();
        else _store.Write(state);
    }

    /// <summary>Drops launches that have aged out of the crash-loop window.</summary>
    void Forget(DateTime nowUtc)
    {
        while (_launches.Count > 0 && nowUtc - _launches.Peek() > GuardPolicy.LaunchWindow)
            _launches.Dequeue();
    }

    // ---- logging ----
    const long MaxLogBytes = 64 * 1024;

    void Log(string message)
    {
        try
        {
            var path = Path.Combine(_store.Root, "guard.log");
            Directory.CreateDirectory(_store.Root);
            if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes) File.Delete(path);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // logging is never worth failing over
        }
    }
}
