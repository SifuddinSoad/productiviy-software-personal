namespace FocusLock.Core.Sessions;

/// <summary>
/// Last-resort guards so a bug can never hold the screen indefinitely. These are deliberately
/// generous: they should only ever fire when something has gone wrong.
/// </summary>
public static class LockSafety
{
    /// <summary>How far past the planned length a lock may run before it is forced open.</summary>
    public const double GraceSeconds = 600;

    /// <summary>The longest session the app will accept at all.</summary>
    public const double MaxSessionSeconds = 4 * 60 * 60;

    public static bool ShouldForceUnlock(double plannedSeconds, double lockedForSeconds) =>
        lockedForSeconds > Math.Min(plannedSeconds, MaxSessionSeconds) + GraceSeconds;
}
