using FocusLock.Core.Board;

namespace FocusLock.Core.Export;

/// <param name="Vertical">A vertical line at x = <paramref name="At"/>, otherwise a horizontal one at y.</param>
public readonly record struct Guide(bool Vertical, double At);

public readonly record struct SnapResult(Rect Box, IReadOnlyList<Guide> Guides);

/// <summary>
/// Pulls a section being moved or resized onto nearby lines: the page margins, the page centre and
/// the edges and centres of the other sections on the page. Each axis snaps on its own, to the
/// closest line within the threshold.
/// </summary>
public static class Snapping
{
    static List<double> LinesX(double pageW, IEnumerable<Rect> others)
    {
        List<double> lines = [PageLayout.Margin, pageW - PageLayout.Margin, pageW / 2];
        foreach (var o in others) lines.AddRange([o.X, o.Right, o.X + o.W / 2]);
        return lines;
    }

    static List<double> LinesY(double pageH, IEnumerable<Rect> others)
    {
        List<double> lines = [PageLayout.Margin, pageH - PageLayout.Margin, pageH / 2];
        foreach (var o in others) lines.AddRange([o.Y, o.Bottom, o.Y + o.H / 2]);
        return lines;
    }

    /// <summary>The smallest shift that puts one of <paramref name="edges"/> on a line, and that line.</summary>
    static (double Shift, double Line)? Nearest(IEnumerable<double> edges, List<double> lines, double threshold)
    {
        (double Shift, double Line)? best = null;
        foreach (var edge in edges)
            foreach (var line in lines)
            {
                var shift = line - edge;
                if (Math.Abs(shift) > threshold) continue;
                if (best is null || Math.Abs(shift) < Math.Abs(best.Value.Shift)) best = (shift, line);
            }
        return best;
    }

    public static SnapResult Move(Rect box, double pageW, double pageH, IEnumerable<Rect> others, double threshold)
    {
        var list = others as IReadOnlyCollection<Rect> ?? others.ToList();
        var guides = new List<Guide>();
        double x = box.X, y = box.Y;

        if (Nearest([box.X, box.X + box.W / 2, box.Right], LinesX(pageW, list), threshold) is { } sx)
        {
            x += sx.Shift;
            guides.Add(new Guide(true, sx.Line));
        }
        if (Nearest([box.Y, box.Y + box.H / 2, box.Bottom], LinesY(pageH, list), threshold) is { } sy)
        {
            y += sy.Shift;
            guides.Add(new Guide(false, sy.Line));
        }

        return new SnapResult(box with { X = x, Y = y }, guides);
    }

    /// <summary>
    /// Resizes from one corner with the opposite corner held still. The picture keeps its shape
    /// (<paramref name="aspect"/> is its height over width) and the caption band keeps its height, so
    /// only the width is free: it follows whichever axis the pointer has moved further along, then
    /// snaps the moving vertical or horizontal edge, whichever is closer to a line.
    /// </summary>
    /// <param name="corner">"nw", "ne", "sw" or "se": the corner being dragged.</param>
    public static SnapResult Resize(Pt anchor, Pt pointer, string corner, double aspect, double captionHeight,
        double pageW, double pageH, IEnumerable<Rect> others, double threshold, double minWidth)
    {
        var list = others as IReadOnlyCollection<Rect> ?? others.ToList();
        var right = corner.Contains('e');
        var down = corner.Contains('s');
        double HeightFor(double w) => w * aspect + captionHeight;

        var byX = right ? pointer.X - anchor.X : anchor.X - pointer.X;
        var byY = ((down ? pointer.Y - anchor.Y : anchor.Y - pointer.Y) - captionHeight) / aspect;
        var width = Math.Max(minWidth, Math.Max(byX, byY));

        var guides = new List<Guide>();
        var edgeX = right ? anchor.X + width : anchor.X - width;
        var edgeY = down ? anchor.Y + HeightFor(width) : anchor.Y - HeightFor(width);
        var sx = Nearest([edgeX], LinesX(pageW, list), threshold);
        var sy = Nearest([edgeY], LinesY(pageH, list), threshold);

        if (sx is { } x && (sy is null || Math.Abs(x.Shift) <= Math.Abs(sy.Value.Shift)))
        {
            var snapped = Math.Abs(x.Line - anchor.X);
            if (snapped >= minWidth)
            {
                width = snapped;
                guides.Add(new Guide(true, x.Line));
            }
        }
        else if (sy is { } y)
        {
            var snapped = (Math.Abs(y.Line - anchor.Y) - captionHeight) / aspect;
            if (snapped >= minWidth)
            {
                width = snapped;
                guides.Add(new Guide(false, y.Line));
            }
        }

        var height = HeightFor(width);
        var box = new Rect(right ? anchor.X : anchor.X - width, down ? anchor.Y : anchor.Y - height, width, height);
        return new SnapResult(box, guides);
    }
}
