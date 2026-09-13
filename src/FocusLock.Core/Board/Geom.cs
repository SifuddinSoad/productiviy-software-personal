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

    /// <summary>True when any part of the polyline falls inside this rectangle.</summary>
    public bool IntersectsPolyline(IReadOnlyList<Pt> pts)
    {
        if (pts.Count == 1) return Contains(pts[0]);
        for (var i = 0; i < pts.Count - 1; i++)
            if (IntersectsSegment(pts[i], pts[i + 1]))
                return true;
        return false;
    }

    /// <summary>Liang-Barsky clip: true when any part of the segment lies in the rectangle, edges included.</summary>
    public bool IntersectsSegment(Pt a, Pt b)
    {
        double t0 = 0, t1 = 1;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;

        Span<(double P, double Q)> edges =
        [
            (-dx, a.X - X), (dx, Right - a.X),
            (-dy, a.Y - Y), (dy, Bottom - a.Y),
        ];

        foreach (var (p, q) in edges)
        {
            if (p == 0)
            {
                if (q < 0) return false;   // parallel to this edge and outside it
                continue;
            }
            var r = q / p;
            if (p < 0)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
        }
        return true;
    }
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

    /// <summary>Height of the band above a frame where its label is drawn.</summary>
    public const double FrameLabelHeight = 20;

    /// <summary>
    /// Topmost frame whose label band holds the point. <see cref="ObjectAt"/> skips frames so their
    /// insides stay clickable, and the label sits outside the frame anyway, so the label needs its own
    /// target. The band spans the frame's width, so an empty label can still be given a name.
    /// </summary>
    public static BoardObject? FrameLabelAt(IReadOnlyList<BoardObject> objs, Pt p)
    {
        for (var i = objs.Count - 1; i >= 0; i--)
        {
            var o = objs[i];
            if (o.Kind != ObjKind.Frame) continue;
            if (new Rect(o.X, o.Y - FrameLabelHeight, o.W, FrameLabelHeight).Contains(p)) return o;
        }
        return null;
    }
}
