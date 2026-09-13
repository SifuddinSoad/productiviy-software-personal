using System.Globalization;
using FocusLock.Core.Models;

namespace FocusLock.Core.Board;

public readonly record struct Pt(double X, double Y)
{
    public static Pt operator -(Pt a, Pt b) => new(a.X - b.X, a.Y - b.Y);
    public static Pt operator +(Pt a, Pt b) => new(a.X + b.X, a.Y + b.Y);
    public double Length => Math.Sqrt(X * X + Y * Y);
}

public readonly record struct Rect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public Pt Center => new(X + W / 2, Y + H / 2);
    public bool Contains(Pt p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
    public bool Intersects(Rect o) => X < o.Right && Right > o.X && Y < o.Bottom && Bottom > o.Y;
}

/// <summary>Number formatting that mirrors the design's helpers, so ported paths stay identical.</summary>
public static class Num
{
    /// <summary>JS <c>Math.round(n * 10) / 10</c> rendered like a JS number ("3", "12.5").</summary>
    public static string R(double n)
    {
        var v = Math.Floor(n * 10 + 0.5) / 10;
        if (v == 0) v = 0;   // avoid "-0"
        return v.ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>JS <c>n.toFixed(1)</c>.</summary>
    public static string F1(double n) => n.ToString("0.0", CultureInfo.InvariantCulture);
}

public static class Bounds
{
    /// <summary>Port of the design's <c>size(o)</c>: some kinds derive their height from content.</summary>
    public static (double W, double H) Size(BoardObject o) => o.Kind switch
    {
        ObjKind.Text => (o.W, (o.Size ?? 16) * 1.5),
        ObjKind.Table => (o.W, (o.Rows ?? 0) * 32),
        ObjKind.Prompt => (o.W, 104),
        _ => (o.W, o.H),
    };

    public static Rect Of(BoardObject o)
    {
        var (w, h) = Size(o);
        return new Rect(o.X, o.Y, w, h);
    }

    /// <summary>Topmost non-frame object under the point, like the design's <c>objAt()</c>.</summary>
    public static BoardObject? ObjectAt(IReadOnlyList<BoardObject> objs, Pt p)
    {
        for (var i = objs.Count - 1; i >= 0; i--)
        {
            if (objs[i].Kind == ObjKind.Frame) continue;
            if (Of(objs[i]).Contains(p)) return objs[i];
        }
        return null;
    }
}
