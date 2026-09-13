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
