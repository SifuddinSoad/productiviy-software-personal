using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FocusLock.App.Guard;
using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.App.Services;

/// <summary>
/// The session that is currently running: owns its clock, ticks once per second, checkpoints to disk
/// and keeps the active-session pointer in sync. Lock enforcement subscribes to <see cref="Started"/>
/// and <see cref="Ended"/>.
/// </summary>
public sealed partial class SessionRuntime : ObservableObject
{
    const int CheckpointEverySec = 15;

    readonly SessionStore _store;
    readonly ActiveSessionStore _active;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    SessionClock? _clock;
    int _sinceCheckpoint;

    [ObservableProperty] Session? _session;
    [ObservableProperty] double _remainingSec;
    [ObservableProperty] bool _isFinished;

    public event Action? Started;
    public event Action<Session>? Ended;

    public SessionRuntime(SessionStore store, ActiveSessionStore active)
    {
        _store = store;
        _active = active;
        _timer.Tick += (_, _) => OnTick();
    }

    public bool IsRunning => Session is not null;

    public string TimerLabel => FormatHms(RemainingSec);

    public static string FormatHms(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, seconds)));
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    public void Start(Session session, bool taskMgrWasDisabled = false)
    {
        _clock = SessionClock.Start(session.PlannedSeconds, () => Environment.TickCount64, () => DateTime.UtcNow);
        session.StartUtc = DateTime.UtcNow;
        session.Clock = _clock.Snapshot();
        _store.Save(session);
        _active.Set(new ActiveSession { SessionId = session.Id, TaskMgrWasDisabled = taskMgrWasDisabled });
        GuardClient.Begin(session);
        Begin(session);
    }

    /// <summary>Picks up an unfinished active session after an app or machine restart.</summary>
    public bool TryResume()
    {
        var pointer = _active.Get();
        var session = pointer is null ? null : _store.Load(pointer.SessionId);

        // The pointer is small and written often, so a crash can cost it. The session files are the
        // real record: fall back to the newest one still unfinished and still holding time, so
        // losing the pointer is not a way out of a locked session.
        session ??= _store.List().FirstOrDefault(s =>
            !s.IsEnded && SessionClock.RemainingFor(s.Clock, s.PlannedSeconds, DateTime.UtcNow) > 0);

        if (session is null || session.IsEnded)
        {
            _active.Clear();
            return false;
        }

        _clock = SessionClock.Resume(session.Clock, session.PlannedSeconds, () => Environment.TickCount64, () => DateTime.UtcNow);
        session.Clock = _clock.Snapshot();
        _store.Save(session);

        if (pointer is null || pointer.SessionId != session.Id)
            _active.Set(new ActiveSession { SessionId = session.Id });

        // Tell the guard again: it may have been installed, restarted or cleared since the session began.
        GuardClient.Begin(session);
        Begin(session);
        return true;
    }

    void Begin(Session session)
    {
        Session = session;
        _sinceCheckpoint = 0;
        UpdateRemaining();
        _timer.Start();
        Started?.Invoke();
    }

    void OnTick()
    {
        if (_clock is null || Session is null) return;
        _clock.Tick();
        UpdateRemaining();
        if (++_sinceCheckpoint >= CheckpointEverySec)
        {
            _sinceCheckpoint = 0;
            SaveNow();
        }
    }

    void UpdateRemaining()
    {
        RemainingSec = _clock!.RemainingSec;
        OnPropertyChanged(nameof(TimerLabel));
        if (_clock.IsFinished && !IsFinished)
        {
            IsFinished = true;
            SaveNow();
        }
        else if (!_clock.IsFinished)
        {
            IsFinished = false;
        }
    }

    public void SaveNow()
    {
        if (Session is null || _clock is null) return;
        Session.Clock = _clock.Snapshot();
        _store.Save(Session);
        GuardClient.Checkpoint(Session);
    }

    /// <summary>
    /// Records whether Task Manager was already disabled before this session locked, so unlocking
    /// leaves a setting the user made themselves alone.
    /// </summary>
    public void SetTaskManagerWasDisabled(bool value)
    {
        if (Session is null) return;
        _active.Set(new ActiveSession { SessionId = Session.Id, TaskMgrWasDisabled = value });
    }

    public void End(EndReason reason)
    {
        if (Session is not { } session) return;
        _timer.Stop();
        session.Clock = _clock!.Snapshot();
        session.EndedUtc = DateTime.UtcNow;
        session.EndReason = reason;
        _store.Save(session);
        _active.Clear();
        GuardClient.End(session);
        Session = null;
        _clock = null;
        IsFinished = false;
        Ended?.Invoke(session);
    }
}
