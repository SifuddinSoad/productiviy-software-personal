using FocusLock.Core.Sessions;

namespace FocusLock.App.Services;

/// <summary>
/// Session lengths. The clock in Setup is the real thing — hours, minutes and seconds — so there
/// is no difference between builds: type 00:00:45 when testing and 01:30:00 when working.
/// </summary>
public static class Durations
{
    /// <summary>Quick fills, in minutes.</summary>
    public static readonly int[] PresetMinutes = [25, 45, 60, 90, 120];

    public const int DefaultMinutes = 60;

    /// <summary>Past the lock's own ceiling the safety guard would force the screen open anyway.</summary>
    public static int MaxSeconds => (int)LockSafety.MaxSessionSeconds;

    public static int MaxHours => MaxSeconds / 3600;

    /// <summary>"25 min", "1 hour", "1.5 hours" — the design's chip labels.</summary>
    public static string ChipLabel(int minutes)
    {
        if (minutes < 60) return minutes + " min";
        var h = minutes / 60.0;
        return h == 1 ? "1 hour" : $"{h:0.#} hours";
    }

    /// <summary>"2 h 30 min", "45 min", "30 s" — a plain reading of a length.</summary>
    public static string Describe(int seconds)
    {
        if (seconds < 60) return seconds + " s";

        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        var rest = seconds % 60;

        var parts = new List<string>(3);
        if (hours > 0) parts.Add($"{hours} h");
        if (minutes > 0) parts.Add($"{minutes} min");
        if (rest > 0) parts.Add($"{rest} s");
        return string.Join(" ", parts);
    }
}
