using FocusLock.Core.Models;

namespace FocusLock.Core.Sessions;

/// <summary>
/// Tracks how much of a session has elapsed without trusting the wall clock while running.
/// Running time is measured with a monotonic tick source, so changing the system clock mid-session
/// has no effect. Time the machine spent off (between the last checkpoint and a resume) is credited
/// from the wall clock, but a negative gap counts as zero.
/// </summary>
public sealed class SessionClock
{
    readonly Func<long> _tickMs;
    readonly Func<DateTime> _utcNow;
    readonly SessionClockState _state;
    long _lastTick;

    public double PlannedSec { get; }

    SessionClock(SessionClockState state, double plannedSec, Func<long> tickMs, Func<DateTime> utcNow)
    {
        _state = state;
        PlannedSec = plannedSec;
        _tickMs = tickMs;
        _utcNow = utcNow;
        _lastTick = tickMs();
    }

    /// <summary>
    /// How much of a saved session would be left right now, without starting it. Used to decide
    /// whether a session found on disk is still worth resuming.
    /// </summary>
    public static double RemainingFor(SessionClockState saved, double plannedSec, DateTime nowUtc)
    {
        var gap = Math.Max(0, (nowUtc - saved.LastCheckpointUtc).TotalSeconds);
        return Math.Max(0, plannedSec - (saved.ConfirmedElapsedSec + gap));
    }

    public static SessionClock Start(double plannedSec, Func<long> tickMs, Func<DateTime> utcNow) =>
        new(new SessionClockState { ConfirmedElapsedSec = 0, LastCheckpointUtc = utcNow() }, plannedSec, tickMs, utcNow);

    /// <summary>Continue after a restart or app relaunch; credits the off time.</summary>
    public static SessionClock Resume(SessionClockState saved, double plannedSec, Func<long> tickMs, Func<DateTime> utcNow)
    {
        var now = utcNow();
        var gap = (now - saved.LastCheckpointUtc).TotalSeconds;
        var state = new SessionClockState
        {
            ConfirmedElapsedSec = saved.ConfirmedElapsedSec + Math.Max(0, gap),
            LastCheckpointUtc = now,
        };
        return new SessionClock(state, plannedSec, tickMs, utcNow);
    }

    /// <summary>Advance by monotonic time. The checkpoint moves by the same amount, not by the wall clock.</summary>
    public void Tick()
    {
        var tick = _tickMs();
        var delta = Math.Max(0, tick - _lastTick) / 1000.0;
        _lastTick = tick;
        _state.ConfirmedElapsedSec += delta;
        _state.LastCheckpointUtc = _state.LastCheckpointUtc.AddSeconds(delta);
    }

    public double ElapsedSec => _state.ConfirmedElapsedSec;

    public double RemainingSec => Math.Max(0, PlannedSec - _state.ConfirmedElapsedSec);

    public bool IsFinished => RemainingSec <= 0;

    public SessionClockState Snapshot() => new()
    {
        ConfirmedElapsedSec = _state.ConfirmedElapsedSec,
        LastCheckpointUtc = _state.LastCheckpointUtc,
    };
}
