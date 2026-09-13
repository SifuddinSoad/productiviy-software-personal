using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.Core.Guard;

/// <summary>
/// What the guard service knows about the session it is holding. Kept in ProgramData, where a
/// standard user can read it but not change it — unlike the app's own pointer in LocalAppData,
/// which the locked user can simply delete.
/// </summary>
public sealed class GuardState
{
    public string SessionId { get; set; } = "";

    /// <summary>SID of the user whose session is locked; nobody else is relaunched into.</summary>
    public string UserSid { get; set; } = "";

    /// <summary>Full path of the app to bring back. Recorded when the session starts.</summary>
    public string AppPath { get; set; } = "";

    public double PlannedSeconds { get; set; }

    public SessionClockState Clock { get; set; } = new();

    public double RemainingSec(DateTime nowUtc) =>
        SessionClock.RemainingFor(Clock, PlannedSeconds, nowUtc);

    public bool IsFinished(DateTime nowUtc) => RemainingSec(nowUtc) <= 0;
}
