using FocusLock.Core.Models;

namespace FocusLock.Core.Board;

/// <summary>
/// Port of the design's <c>shapePath()</c>. Emits SVG path data, which WPF's
/// <c>Geometry.Parse</c> understands as-is.
/// </summary>
public static class ShapeGeometry
{
    static readonly double[][] Diamond = [[0.5, 0], [1, 0.5], [0.5, 1], [0, 0.5]];
    static readonly double[][] Triangle = [[0.5, 0], [1, 1], [0, 1]];
    static readonly double[][] Hexagon = [[0.25, 0], [0.75, 0], [1, 0.5], [0.75, 1], [0.25, 1], [0, 0.5]];
    static readonly double[][] Square = [[0, 0], [1, 0], [1, 1], [0, 1]];

    public static string Path(BoardObject o, double w, double h) =>
        Path(o.Kind == ObjKind.Frame ? "rect" : o.Shape ?? "rect", o.Pts, w, h);

    public static string Path(string kind, IReadOnlyList<double[]>? customPts, double w, double h)
    {
        string R(double n) => Num.R(n);

        if (kind == "ellipse")
        {
            double rx = w / 2, ry = h / 2;
            return $"M 0 {R(ry)} A {R(rx)} {R(ry)} 0 1 1 {R(w)} {R(ry)} A {R(rx)} {R(ry)} 0 1 1 0 {R(ry)} Z";
        }

        if (kind is "rect" or "round" or "pill")
        {
            var rad = kind switch
            {
                "rect" => 3,
                "pill" => Math.Min(w, h) / 2,
                _ => Math.Min(18, Math.Min(w / 2, h / 2)),
            };
            return $"M {R(rad)} 0 H {R(w - rad)} A {R(rad)} {R(rad)} 0 0 1 {R(w)} {R(rad)}" +
                   $" V {R(h - rad)} A {R(rad)} {R(rad)} 0 0 1 {R(w - rad)} {R(h)}" +
                   $" H {R(rad)} A {R(rad)} {R(rad)} 0 0 1 0 {R(h - rad)}" +
                   $" V {R(rad)} A {R(rad)} {R(rad)} 0 0 1 {R(rad)} 0 Z";
        }

        var pts = kind switch
        {
            "diamond" => Diamond,
            "triangle" => Triangle,
            "hexagon" => Hexagon,
            "custom" when customPts is { Count: > 0 } => [.. customPts],
            _ => Square,
        };

        return "M " + string.Join(" L ", pts.Select(p => $"{R(p[0] * w)} {R(p[1] * h)}")) + " Z";
    }

    /// <summary>How wide the label inside a shape may be, as a fraction of the shape width.</summary>
    public static double TextWidthFactor(BoardObject o) => (o.Shape ?? "rect") switch
    {
        "ellipse" or "diamond" or "hexagon" => 0.72,
        "triangle" => 0.60,
        "custom" => 0.68,
        _ => 0.92,
    };
}
