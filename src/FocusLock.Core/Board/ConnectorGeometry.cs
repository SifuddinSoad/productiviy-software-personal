using FocusLock.Core.Models;

namespace FocusLock.Core.Board;

public readonly record struct ConnectorPath(string Data, string Heads, double[]? Dash);

/// <summary>Port of the design's <c>attach()</c>, <c>head()</c> and <c>connGeom()</c>.</summary>
public static class ConnectorGeometry
{
    /// <summary>Where a line aimed at (tx,ty) leaves the object's outline, pushed out by <paramref name="pad"/>.</summary>
    public static Pt Attach(BoardObject o, double tx, double ty, double pad)
    {
        var b = Bounds.Of(o);
        var c = b.Center;
        var dx = tx - c.X;
        var dy = ty - c.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len == 0) len = 1;
        var hw = b.W / 2;
        var hh = b.H / 2;
        var kind = o.Kind == ObjKind.Shape ? o.Shape ?? "rect" : "rect";

        double t;
        if (kind is "ellipse" or "pill")
            t = 1 / Math.Sqrt(dx / hw * (dx / hw) + dy / hh * (dy / hh));
        else if (kind == "diamond")
            t = 1 / (Math.Abs(dx) / hw + Math.Abs(dy) / hh);
        else
            t = Math.Min(hw / (Math.Abs(dx) == 0 ? 1e-6 : Math.Abs(dx)), hh / (Math.Abs(dy) == 0 ? 1e-6 : Math.Abs(dy)));

        return new Pt(c.X + dx * t + dx / len * pad, c.Y + dy * t + dy / len * pad);
    }

    /// <summary>Filled arrow head triangle at <paramref name="tip"/> pointing along <paramref name="angle"/>.</summary>
    public static string Head(Pt tip, double angle)
    {
        const double hl = 11, hw = 4.6;
        var p1 = new Pt(
            tip.X - Math.Cos(angle) * hl - Math.Sin(angle) * hw,
            tip.Y - Math.Sin(angle) * hl + Math.Cos(angle) * hw);
        var p2 = new Pt(
            tip.X - Math.Cos(angle) * hl + Math.Sin(angle) * hw,
            tip.Y - Math.Sin(angle) * hl - Math.Cos(angle) * hw);
        return $"M {Num.F1(tip.X)} {Num.F1(tip.Y)} L {Num.F1(p1.X)} {Num.F1(p1.Y)} L {Num.F1(p2.X)} {Num.F1(p2.Y)} Z ";
    }

    static Pt Anchor(BoardObject? o, double[]? pt) =>
        o is not null ? Bounds.Of(o).Center : new Pt(pt?[0] ?? 0, pt?[1] ?? 0);

    /// <summary>Both ends of a connector, for hit testing and for drawing the line.</summary>
    public static (Pt From, Pt To) Endpoints(Connector c, BoardObject? from, BoardObject? to) =>
        (Anchor(from, c.FromPt), Anchor(to, c.ToPt));

    /// <summary>
    /// <paramref name="from"/> or <paramref name="to"/> may be null, meaning that end is a free
    /// point on the canvas rather than an object.
    /// </summary>
    /// <summary>Where the drawn line actually starts and stops — an object end sits on its outline.</summary>
    public static (Pt A, Pt B) Ends(Connector c, BoardObject? from, BoardObject? to)
    {
        var (fromAnchor, toAnchor) = Endpoints(c, from, to);
        // An object end leaves its outline aimed at the other end; a free end is the point itself.
        var a = from is not null ? Attach(from, toAnchor.X, toAnchor.Y, 2) : fromAnchor;
        var b = to is not null ? Attach(to, fromAnchor.X, fromAnchor.Y, 10) : toAnchor;
        return (a, b);
    }

    /// <summary>
    /// The route as a polyline, following the same curve/elbow/straight shape that is drawn.
    /// Used for hit testing, so clicking the visible line selects it.
    /// </summary>
    public static List<Pt> Polyline(Connector c, BoardObject? from, BoardObject? to)
    {
        var (a, b) = Ends(c, from, to);
        var style = string.IsNullOrEmpty(c.Style) ? "curve" : c.Style;

        if (style == "straight") return [a, b];

        if (style == "elbow")
        {
            if (Math.Abs(b.X - a.X) > Math.Abs(b.Y - a.Y))
            {
                var mx = (a.X + b.X) / 2;
                return [a, new Pt(mx, a.Y), new Pt(mx, b.Y), b];
            }
            var my = (a.Y + b.Y) / 2;
            return [a, new Pt(a.X, my), new Pt(b.X, my), b];
        }

        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var horiz = Math.Abs(dx) > Math.Abs(dy);
        const double k = 0.45;
        var c1 = horiz ? new Pt(a.X + dx * k, a.Y) : new Pt(a.X, a.Y + dy * k);
        var c2 = horiz ? new Pt(b.X - dx * k, b.Y) : new Pt(b.X, b.Y - dy * k);

        var pts = new List<Pt>(17);
        for (var i = 0; i <= 16; i++)
        {
            var t = i / 16.0;
            var u = 1 - t;
            pts.Add(new Pt(
                u * u * u * a.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * b.X,
                u * u * u * a.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * b.Y));
        }
        return pts;
    }

    public static ConnectorPath Compute(Connector c, BoardObject? from, BoardObject? to)
    {
        var (a, b) = Ends(c, from, to);

        string data;
        double angEnd, angStart;
        var style = string.IsNullOrEmpty(c.Style) ? "curve" : c.Style;

        if (style == "elbow")
        {
            var horiz = Math.Abs(b.X - a.X) > Math.Abs(b.Y - a.Y);
            if (horiz)
            {
                var mx = (a.X + b.X) / 2;
                data = $"M {Num.F1(a.X)} {Num.F1(a.Y)} H {Num.F1(mx)} V {Num.F1(b.Y)} H {Num.F1(b.X)}";
                angEnd = b.X > mx ? 0 : Math.PI;
                angStart = a.X > mx ? 0 : Math.PI;
            }
            else
            {
                var my = (a.Y + b.Y) / 2;
                data = $"M {Num.F1(a.X)} {Num.F1(a.Y)} V {Num.F1(my)} H {Num.F1(b.X)} V {Num.F1(b.Y)}";
                angEnd = b.Y > my ? Math.PI / 2 : -Math.PI / 2;
                angStart = a.Y > my ? Math.PI / 2 : -Math.PI / 2;
            }
        }
        else if (style == "straight")
        {
            data = $"M {Num.F1(a.X)} {Num.F1(a.Y)} L {Num.F1(b.X)} {Num.F1(b.Y)}";
            angEnd = Math.Atan2(b.Y - a.Y, b.X - a.X);
            angStart = angEnd + Math.PI;
        }
        else
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var horiz = Math.Abs(dx) > Math.Abs(dy);
            const double k = 0.45;
            var c1 = horiz ? new Pt(a.X + dx * k, a.Y) : new Pt(a.X, a.Y + dy * k);
            var c2 = horiz ? new Pt(b.X - dx * k, b.Y) : new Pt(b.X, b.Y - dy * k);
            data = $"M {Num.F1(a.X)} {Num.F1(a.Y)} C {Num.F1(c1.X)} {Num.F1(c1.Y)}, {Num.F1(c2.X)} {Num.F1(c2.Y)}, {Num.F1(b.X)} {Num.F1(b.Y)}";
            angEnd = Math.Atan2(b.Y - c2.Y, b.X - c2.X);
            angStart = Math.Atan2(a.Y - c1.Y, a.X - c1.X);
        }

        var mode = string.IsNullOrEmpty(c.Arrows) ? "end" : c.Arrows;
        var heads = "";
        var tipEnd = new Pt(b.X + Math.Cos(angEnd) * 9, b.Y + Math.Sin(angEnd) * 9);
        if (mode is "end" or "both") heads += Head(tipEnd, angEnd);
        if (mode == "both") heads += Head(new Pt(a.X + Math.Cos(angStart) * 2, a.Y + Math.Sin(angStart) * 2), angStart);

        return new ConnectorPath(data, heads.Length == 0 ? "M 0 0" : heads, c.Dash ? [7, 6] : null);
    }
}
