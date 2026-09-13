using FocusLock.Core.Models;

namespace FocusLock.Core.Sessions;

public static class Progress
{
    /// <summary>Done plans / all plans, 0-100. Null when the session has no plans.</summary>
    public static int? Percent(Session s) =>
        s.Plans.Count == 0 ? null : (int)Math.Round(100.0 * s.Plans.Count(p => p.Done) / s.Plans.Count);

    public static bool IsComplete(Session s) => s.Plans.Count == 0 || s.Plans.All(p => p.Done);

    public static string StateLabel(Session s)
    {
        if (!s.IsEnded) return "active";
        if (s.EndReason == EndReason.Emergency) return "ended early";
        return IsComplete(s) ? "complete" : "ended";
    }

    /// <summary>An ended session with unfinished plans can seed a new session.</summary>
    public static bool CanContinue(Session s) => s.IsEnded && s.Plans.Any(p => !p.Done);
}
