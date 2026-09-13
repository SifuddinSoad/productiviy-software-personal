using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusLock.App.Board;
using FocusLock.App.Export;
using FocusLock.Core.Document;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FocusLock.App.Document;

/// <summary>
/// Draws laid-out WPF text into a PDF page by reading back what WPF drew — text runs, shapes and
/// pictures — and drawing the same things with PDFsharp at the same places. Text is written word by
/// word at WPF's own glyph positions, so line breaks, alignment and spacing match the screen exactly
/// and the words stay selectable. Anything that cannot be read that way is drawn as a picture instead.
/// </summary>
public static class PdfDocumentWriter
{
    const double PointPerDip = 72.0 / 96.0;
    const double FallbackDpi = 200;

    /// <summary>Why the last piece that had to be drawn as a picture could not be read; for diagnosing.</summary>
    public static string? LastPictureFallback { get; private set; }

    /// <summary>Draws one text fragment where it sits on the page.</summary>
    public static void DrawFragment(XGraphics gfx, TextFragment fragment)
    {
        // the fragment's page starts ShiftDip above its visible top; that part is empty spacer
        var origin = new Matrix();
        origin.Translate(DocLook.Dip(fragment.X), DocLook.Dip(fragment.Y) - fragment.ShiftDip);

        var ops = new List<Action<XGraphics>>();
        try
        {
            Walk(fragment.Page.Visual, origin, ops);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            LastPictureFallback = e.ToString();
            System.Diagnostics.Debug.WriteLine("FocusLock: text drawn into the PDF as a picture — " + e.Message);
            DrawAsPicture(gfx, fragment);
            return;
        }
        foreach (var op in ops) op(gfx);
    }

    static void DrawAsPicture(XGraphics gfx, TextFragment fragment)
    {
        var scale = FallbackDpi / 96;
        var width = DocLook.Dip(fragment.W);
        var height = DocLook.Dip(fragment.H);
        var host = new ContainerVisual { Transform = new TranslateTransform(0, -fragment.ShiftDip) };
        host.Children.Add(fragment.Page.Visual);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale),
            FallbackDpi, FallbackDpi, PixelFormats.Pbgra32);
        bitmap.Render(host);
        host.Children.Clear();
        using var stream = new MemoryStream(RegionRenderer.EncodePng(bitmap));
        using var image = XImage.FromStream(stream);
        gfx.DrawImage(image, fragment.X, fragment.Y, fragment.W, fragment.H);
    }

    // ---------------------------------------------------------------- reading the page

    static void Walk(Visual visual, Matrix parent, List<Action<XGraphics>> ops)
    {
        var local = VisualTreeHelper.GetTransform(visual)?.Value ?? Matrix.Identity;
        var offset = VisualTreeHelper.GetOffset(visual);
        local.Translate(offset.X, offset.Y);
        var m = local * parent;

        if (VisualTreeHelper.GetDrawing(visual) is { } drawing) Draw(drawing, m, ops);

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            if (VisualTreeHelper.GetChild(visual, i) is Visual child) Walk(child, m, ops);
    }

    static void Draw(Drawing drawing, Matrix m, List<Action<XGraphics>> ops)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                var gm = (group.Transform?.Value ?? Matrix.Identity) * m;
                foreach (var child in group.Children) Draw(child, gm, ops);
                break;
            case GlyphRunDrawing text:
                DrawText(text, m, ops);
                break;
            case GeometryDrawing shape:
                DrawShape(shape, m, ops);
                break;
            case ImageDrawing image:
                DrawImage(image, m, ops);
                break;
        }
    }

    static XPoint ToPdf(Matrix m, Point p)
    {
        var t = m.Transform(p);
        return new XPoint(t.X * PointPerDip, t.Y * PointPerDip);
    }

    static XColor? ColorOf(Brush? brush)
    {
        if (brush is not SolidColorBrush solid) return null;
        var c = solid.Color;
        var alpha = (byte)Math.Round(c.A * Math.Clamp(solid.Opacity, 0, 1));
        return alpha == 0 ? null : XColor.FromArgb(alpha, c.R, c.G, c.B);
    }

    static void DrawText(GlyphRunDrawing drawing, Matrix m, List<Action<XGraphics>> ops)
    {
        var run = drawing.GlyphRun;
        if (ColorOf(drawing.ForegroundBrush) is not { } color || run.Characters is not { Count: > 0 } chars) return;

        // Words go in as text only in the faces the app embeds. Icons (the checklist box) and
        // whatever Windows substituted (list bullets come from Wingdings) are drawn as their outlines:
        // they look identical and nobody needs to select them.
        var file = Path.GetFileNameWithoutExtension(run.GlyphTypeface.FontUri.AbsolutePath);
        var face = EmbeddedFontResolver.Faces.FirstOrDefault(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase));
        if (face is null || face.StartsWith("Material", StringComparison.Ordinal))
        {
            // the outline comes back frozen, so the page transform goes on a group around it
            var outline = new GeometryGroup { Children = { run.BuildGeometry() }, Transform = new MatrixTransform(m) };
            DrawShape(new GeometryDrawing(drawing.ForegroundBrush, null, outline), Matrix.Identity, ops);
            return;
        }

        var style = XFontStyleEx.Regular;
        if (run.GlyphTypeface.StyleSimulations.HasFlag(StyleSimulations.BoldSimulation)) style |= XFontStyleEx.Bold;
        if (run.GlyphTypeface.StyleSimulations.HasFlag(StyleSimulations.ItalicSimulation)) style |= XFontStyleEx.Italic;

        var scale = Math.Sqrt(Math.Abs(m.M11 * m.M22 - m.M12 * m.M21));
        var size = run.FontRenderingEmSize * scale * PointPerDip;
        if (size <= 0.1) return;
        var font = new XFont(face, size, style, new XPdfFontOptions(PdfFontEncoding.Unicode));
        var brush = new XSolidBrush(color);

        // x of each glyph from WPF's own advances; a character finds its glyph through the cluster map
        var advances = run.AdvanceWidths;
        var glyphX = new double[advances.Count + 1];
        for (var g = 0; g < advances.Count; g++) glyphX[g + 1] = glyphX[g] + advances[g];
        int GlyphOf(int ch) => run.ClusterMap is { Count: > 0 } map ? map[Math.Min(ch, map.Count - 1)] : Math.Min(ch, advances.Count - 1);
        var rtl = run.BidiLevel % 2 == 1;

        var text = new string(chars.ToArray());
        var start = 0;
        while (start < text.Length)
        {
            if (char.IsWhiteSpace(text[start])) { start++; continue; }
            var end = start;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;

            var word = text[start..end];
            var x = glyphX[GlyphOf(start)];
            var at = ToPdf(m, new Point(run.BaselineOrigin.X + (rtl ? -x : x), run.BaselineOrigin.Y));
            ops.Add(g => g.DrawString(word, font, brush, at, XStringFormats.BaseLineLeft));
            start = end;
        }
    }

    static void DrawShape(GeometryDrawing drawing, Matrix m, List<Action<XGraphics>> ops)
    {
        var fill = ColorOf(drawing.Brush);
        var penColor = ColorOf(drawing.Pen?.Brush);
        if (fill is null && penColor is null) return;
        if (drawing.Geometry is null || drawing.Geometry.IsEmpty()) return;

        var flat = drawing.Geometry.GetFlattenedPathGeometry(0.25, ToleranceType.Absolute);
        var path = new XGraphicsPath();
        foreach (var figure in flat.Figures)
        {
            var points = new List<XPoint> { ToPdf(m, figure.StartPoint) };
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line: points.Add(ToPdf(m, line.Point)); break;
                    case PolyLineSegment poly: points.AddRange(poly.Points.Select(p => ToPdf(m, p))); break;
                }
            }
            if (points.Count < 2) continue;
            path.StartFigure();
            path.AddLines(points.ToArray());
            if (figure.IsClosed) path.CloseFigure();
        }

        var scale = Math.Sqrt(Math.Abs(m.M11 * m.M22 - m.M12 * m.M21));
        var pen = penColor is { } pc ? new XPen(pc, Math.Max(0.1, drawing.Pen!.Thickness * scale * PointPerDip)) : null;
        var brush = fill is { } fc ? new XSolidBrush(fc) : null;
        ops.Add(g =>
        {
            if (pen is not null && brush is not null) g.DrawPath(pen, brush, path);
            else if (brush is not null) g.DrawPath(brush, path);
            else g.DrawPath(pen!, path);
        });
    }

    static void DrawImage(ImageDrawing drawing, Matrix m, List<Action<XGraphics>> ops)
    {
        if (drawing.ImageSource is not BitmapSource bitmap) return;
        var a = ToPdf(m, drawing.Rect.TopLeft);
        var b = ToPdf(m, drawing.Rect.BottomRight);
        var png = RegionRenderer.EncodePng(bitmap);
        ops.Add(g =>
        {
            using var stream = new MemoryStream(png);
            using var image = XImage.FromStream(stream);
            g.DrawImage(image, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        });
    }
}
