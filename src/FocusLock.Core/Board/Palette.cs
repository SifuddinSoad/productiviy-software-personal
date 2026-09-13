using System.Globalization;

namespace FocusLock.Core.Board;

/// <summary>Colour helpers so text stays readable on whatever fill an object is given.</summary>
public static class Palette
{
    public const string Dark = "#17181a";
    public const string Light = "#e9e9e7";

    public static (int R, int G, int B) Rgb(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        if (h.Length < 6) return (0, 0, 0);
        int P(int i) => int.Parse(h.AsSpan(i, 2), NumberStyles.HexNumber);
        return (P(0), P(2), P(4));
    }

    /// <summary>Perceived brightness, 0 (black) to 1 (white).</summary>
    public static double Brightness(string hex)
    {
        var (r, g, b) = Rgb(hex);
        return (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
    }

    public static bool IsLight(string hex) => Brightness(hex) > 0.55;

    /// <summary>Readable text colour for a given background.</summary>
    public static string TextOn(string fill) => IsLight(fill) ? Dark : Light;

    /// <summary>Same hue, scaled brightness: below 1 darkens, above 1 lightens.</summary>
    public static string Shade(string hex, double factor)
    {
        var (r, g, b) = Rgb(hex);
        int C(int v) => Math.Clamp((int)Math.Round(v * factor), 0, 255);
        return $"#{C(r):x2}{C(g):x2}{C(b):x2}";
    }
}
