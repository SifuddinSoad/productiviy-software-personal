using FocusLock.Core.Models;

namespace FocusLock.Core.Board;

/// <summary>Port of the design's <c>pathOf()</c>: quadratic smoothing through midpoints.</summary>
public static class StrokePath
{
    public static string Of(IReadOnlyList<double[]> pts)
    {
        if (pts.Count == 0) return "";

        if (pts.Count < 3)
            return "M " + string.Join(" L ", pts.Select(p => $"{Num.F1(p[0])} {Num.F1(p[1])}"));

        var d = $"M {Num.F1(pts[0][0])} {Num.F1(pts[0][1])}";
        for (var i = 1; i < pts.Count - 1; i++)
        {
            var mx = (pts[i][0] + pts[i + 1][0]) / 2;
            var my = (pts[i][1] + pts[i + 1][1]) / 2;
            d += $" Q {Num.F1(pts[i][0])} {Num.F1(pts[i][1])} {Num.F1(mx)} {Num.F1(my)}";
        }
        var last = pts[^1];
        return d + $" L {Num.F1(last[0])} {Num.F1(last[1])}";
    }
}

/// <summary>Port of the design's <c>eraseAt()</c>: rubs ink away and deletes whole objects.</summary>
public static class Eraser
{
    public sealed record Result(List<Stroke> Strokes, BoardObject? RemovedObject, bool Changed);

    public static Result Erase(BoardDoc doc, Pt p, double eraserSize, Func<string> newId)
    {
        var r = eraserSize / 2;
        bool Hit(double[] a) => Math.Sqrt((a[0] - p.X) * (a[0] - p.X) + (a[1] - p.Y) * (a[1] - p.Y)) <= r;

        var changed = false;
        var next = new List<Stroke>();
        foreach (var st in doc.Strokes)
        {
            if (!st.Pts.Any(Hit))
            {
                next.Add(st);
                continue;
            }
            changed = true;
            var run = new List<double[]>();
            foreach (var pt in st.Pts)
            {
                if (Hit(pt))
                {
                    if (run.Count > 1) next.Add(new Stroke { Id = newId(), Pts = run, Color = st.Color, W = st.W });
                    run = [];
                }
                else run.Add(pt);
            }
            if (run.Count > 1) next.Add(new Stroke { Id = newId(), Pts = run, Color = st.Color, W = st.W });
        }

        var target = Bounds.ObjectAt(doc.Objs, p);
        return new Result(changed ? next : doc.Strokes, target, changed || target is not null);
    }
}
