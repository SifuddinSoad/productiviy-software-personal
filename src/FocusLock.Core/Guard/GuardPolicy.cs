namespace FocusLock.Core.Guard;

public enum GuardAction
{
    /// <summary>Nothing to do: no session, or the app is already up.</summary>
    Idle,

    /// <summary>Start the app in the user's session.</summary>
    Relaunch,

    /// <summary>Stop guarding and forget the session.</summary>
    Expire,
}

/// <summary>
/// The one decision the service makes, kept pure so it can be tested without a service.
///
/// Expiring is deliberately easy to reach. A guard that cannot let go is far worse than one that
/// lets go early, so the planned time running out, a crash loop, or a missing app all end it.
/// </summary>
public static class GuardPolicy
{
    /// <summary>
    /// Relaunches allowed inside <see cref="LaunchWindow"/> before the guard gives up. A build that
    /// dies on startup would otherwise make the machine unusable.
    /// </summary>
    public const int MaxLaunches = 6;

    public static readonly TimeSpan LaunchWindow = TimeSpan.FromMinutes(1);

    /// <param name="recentLaunches">Launches attempted within <see cref="LaunchWindow"/>.</param>
    public static GuardAction Decide(GuardState? state, bool appRunning, DateTime nowUtc, int recentLaunches)
    {
        if (state is null) return GuardAction.Idle;
        if (state.IsFinished(nowUtc)) return GuardAction.Expire;
        if (appRunning) return GuardAction.Idle;
        if (recentLaunches >= MaxLaunches) return GuardAction.Expire;
        return GuardAction.Relaunch;
    }
}
