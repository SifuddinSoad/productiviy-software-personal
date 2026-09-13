using FocusLock.Core.Sessions;

namespace FocusLock.App.Services;

public static class Durations
{
#if DEBUG
    /// <summary>DEBUG builds count the design's options in seconds so a whole session can be tested quickly.</summary>
    public const bool InSeconds = true;
    public static readonly int[] Options = [30, 45, 60, 90, 120];
    public static int ToSeconds(int option) => option;
#else
    public const bool InSeconds = false;
    public static readonly int[] Options = [30, 45, 60, 90, 120];
    public static int ToSeconds(int option) => option * 60;
#endif

    public const int DefaultOption = 60;

    /// <summary>What a typed number means here: seconds in DEBUG builds, minutes otherwise.</summary>
    public static string UnitLabel => InSeconds ? "seconds" : "minutes";

    /// <summary>
    /// The largest number that may be typed. Past the lock's own ceiling the safety guard would
    /// force the screen open anyway, so the input stops there.
    /// </summary>
    public static int MaxUnits => (int)(LockSafety.MaxSessionSeconds / (InSeconds ? 1 : 60));

    public static bool IsValidCustom(int units) => units >= 1 && units <= MaxUnits;

    /// <summary>"2 h 30 min", "45 min", "90 s" — a plain reading of a length in seconds.</summary>
    public static string Describe(int seconds)
    {
#pragma warning disable CS0162 // one branch is unreachable per build configuration
        if (InSeconds) return seconds + " s";
        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        if (hours == 0) return $"{minutes} min";
        return minutes == 0 ? $"{hours} h" : $"{hours} h {minutes} min";
#pragma warning restore CS0162
    }

    /// <summary>"30 min", "1 hour", "1.5 hours" — the design's chip labels.</summary>
    public static string ChipLabel(int option)
    {
#pragma warning disable CS0162 // one branch is unreachable per build configuration
        if (InSeconds) return option + " sec";
        if (option < 60) return option + " min";
        var h = option / 60.0;
        return h == 1 ? "1 hour" : $"{h:0.#} hours";
#pragma warning restore CS0162
    }

    /// <summary>"30 min" / "1 h" / "1.5 h" — the right side of the time range row.</summary>
    public static string ShortLabel(int option)
    {
#pragma warning disable CS0162
        if (InSeconds) return option + " s";
        return option < 60 ? option + " min" : $"{option / 60.0:0.#} h";
#pragma warning restore CS0162
    }
}
