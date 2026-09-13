using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FocusLock.Core.Board;
using FocusLock.Core.Models;
using Rect = System.Windows.Rect;

namespace FocusLock.App.Board;

/// <param name="EditingId">Its label is live in the overlay editor, so it must not be painted twice.</param>
/// <param name="EditingCell">Which table cell is being edited, or -1.</param>
/// <param name="SelectedConnectorIds">Drawn thicker. Null when nothing is selected, as in an export.</param>
public sealed record PaintOptions(
    string? EditingId = null,
    int EditingCell = -1,
    IReadOnlyCollection<string>? SelectedConnectorIds = null,
    double PixelsPerDip = 1.0);

/// <summary>
/// Paints a board document and nothing else — no selection, handles or other editing furniture.
/// The canvas draws its overlays on top of this; the PDF export uses it on its own, so both show
/// exactly the same thing.
/// </summary>
public static class BoardPainter
{
    public static void Paint(DrawingContext dc, BoardDoc doc, PaintOptions o)
    {
        foreach (var obj in doc.Objs.Where(x => x.Kind == ObjKind.Frame)) Frame(dc, obj, o);
        Connectors(dc, doc, o);
        foreach (var obj in doc.Objs.Where(x => x.Kind == ObjKind.Table)) Table(dc, obj, o);
        foreach (var obj in doc.Objs.Where(x => x.Kind == ObjKind.Shape)) Shape(dc, obj, o);
        foreach (var obj in doc.Objs.Where(x => x.Kind == ObjKind.Sticky)) Sticky(dc, obj, o);
        foreach (var obj in doc.Objs.Where(x => x.Kind == ObjKind.Text)) Text(dc, obj, o);
        foreach (var obj in doc.Objs.Where(x => x.Kind == ObjKind.Prompt)) Prompt(dc, obj, o);
        Strokes(dc, doc);
    }

    // ---------------------------------------------------------------- helpers

    public static FormattedText Ft(string text, FontFamily family, double size, FontWeight weight, Brush brush,
        double pixelsPerDip, double? maxWidth = null, TextAlignment align = TextAlignment.Left)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Fonts.Face(family, weight), size, brush, pixelsPerDip)
        {
            TextAlignment = align,
        };
        if (maxWidth is { } w) ft.MaxTextWidth = Math.Max(1, w);
        return ft;
    }

    static bool IsEditing(BoardObject obj, PaintOptions o, int cell = -1) =>
        o.EditingId == obj.Id && o.EditingCell == cell;

    /// <summary>The object's own fill, or the default for its kind.</summary>
    public static string FillOf(BoardObject o, string fallback) =>
        string.IsNullOrEmpty(o.Fill) ? fallback : o.Fill;

    /// <summary>The chosen text colour, else one that reads against the fill.</summary>
    public static Brush TextBrush(BoardObject o, string background, string? untinted = null)
    {
        if (!string.IsNullOrEmpty(o.TextColor)) return Theme.HexBrush.FromHex(o.TextColor);
        if (untinted is not null && string.IsNullOrEmpty(o.Fill)) return Theme.HexBrush.FromHex(untinted);
        return Theme.HexBrush.FromHex(Palette.TextOn(background));
    }

    // ---------------------------------------------------------------- object kinds

    static void Frame(DrawingContext dc, BoardObject obj, PaintOptions o)
    {
        var fill = FillOf(obj, "#16181b");
        dc.DrawRoundedRectangle(Theme.HexBrush.FromHex(fill), B.FramePen, new Rect(obj.X, obj.Y, obj.W, obj.H), 3, 3);
        if (string.IsNullOrEmpty(obj.Text) || IsEditing(obj, o)) return;

        // the label sits above the frame on the canvas, so it keeps the muted colour by default
        var brush = string.IsNullOrEmpty(obj.TextColor) ? B.FrameLabel : Theme.HexBrush.FromHex(obj.TextColor);
        dc.DrawText(Ft(obj.Text, Fonts.Sans, 11, FontWeights.SemiBold, brush, o.PixelsPerDip), new Point(obj.X, obj.Y - 19));
    }

    static void Connectors(DrawingContext dc, BoardDoc doc, PaintOptions o)
    {
        var byId = doc.Objs.ToDictionary(x => x.Id);
        foreach (var conn in doc.Conns)
        {
            if (!BoardController.TryResolve(conn, byId, out var a, out var b)) continue;
            var path = ConnectorGeometry.Compute(conn, a, b);
            var selected = o.SelectedConnectorIds?.Contains(conn.Id) == true;
            var thickness = selected ? 3.2 : 1.7;
            var pen = path.Dash is { } dash
                ? B.Dashed(B.Connector, thickness, dash[0], dash[1])
                : B.Frozen(new Pen(B.Connector, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
            dc.DrawGeometry(null, pen, Geometry.Parse(path.Data));
            if (path.Heads != "M 0 0") dc.DrawGeometry(B.Connector, null, Geometry.Parse(path.Heads));
        }
    }

    static void Table(DrawingContext dc, BoardObject obj, PaintOptions o)
    {
        var cols = obj.Cols ?? 1;
        var rows = obj.Rows ?? 1;
        var cellW = obj.W / cols;
        const double cellH = 32;
        var rect = new Rect(obj.X, obj.Y, obj.W, rows * cellH);

        var fill = FillOf(obj, "#1b1d20");
        var header = Palette.Shade(fill, Palette.IsLight(fill) ? 0.92 : 1.18);
        var lines = Theme.HexBrush.FromHex(Palette.Shade(fill, Palette.IsLight(fill) ? 0.85 : 1.45));
        var linePen = B.Frozen(new Pen(lines, 1));
        var textBrush = TextBrush(obj, fill, B.Light.ToString());

        dc.DrawRoundedRectangle(Theme.HexBrush.FromHex(fill), B.TablePen, rect, 4, 4);
        for (var i = 0; i < rows * cols; i++)
        {
            var r = i / cols;
            var col = i % cols;
            var cell = new Rect(obj.X + col * cellW, obj.Y + r * cellH, cellW, cellH);
            if (r == 0) dc.DrawRectangle(Theme.HexBrush.FromHex(header), null, cell);
            dc.DrawLine(linePen, new Point(cell.Right, cell.Top), new Point(cell.Right, cell.Bottom));
            dc.DrawLine(linePen, new Point(cell.Left, cell.Bottom), new Point(cell.Right, cell.Bottom));

            var text = obj.Cells is { } cells && i < cells.Count ? cells[i] : "";
            if (text.Length == 0 || IsEditing(obj, o, i)) continue;
            var ft = Ft(text, Fonts.Sans, 12, r == 0 ? FontWeights.SemiBold : FontWeights.Normal, textBrush,
                o.PixelsPerDip, cellW - 18);
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(ft, new Point(cell.Left + 9, cell.Top + 7));
        }
    }

    static void Shape(DrawingContext dc, BoardObject obj, PaintOptions o)
    {
        var fill = FillOf(obj, "#ffffff");
        var geometry = Geometry.Parse(ShapeGeometry.Path(obj, obj.W, obj.H));
        dc.PushTransform(new TranslateTransform(obj.X, obj.Y));
        dc.DrawGeometry(Theme.HexBrush.FromHex(fill), B.ShapePen, geometry);

        if (!string.IsNullOrEmpty(obj.Text) && !IsEditing(obj, o))
        {
            var maxW = obj.W * ShapeGeometry.TextWidthFactor(obj);
            var ft = Ft(obj.Text, Fonts.Sans, 13, FontWeights.Medium, TextBrush(obj, fill), o.PixelsPerDip,
                maxW, TextAlignment.Center);
            dc.DrawText(ft, new Point((obj.W - maxW) / 2, (obj.H - ft.Height) / 2));
        }
        dc.Pop();

        if (obj.Votes > 0) VoteBadge(dc, obj.X + obj.W + 9, obj.Y - 11, obj.Votes, o);
    }

    static void VoteBadge(DrawingContext dc, double right, double top, int votes, PaintOptions o)
    {
        var ft = Ft(votes.ToString(), Fonts.Sans, 11, FontWeights.Bold, B.White, o.PixelsPerDip);
        var w = ft.Width + 26;
        var rect = new Rect(right - w, top, w, 22);
        dc.DrawRoundedRectangle(B.Badge, null, rect, 11, 11);
        dc.DrawEllipse(B.Yellow, null, new Point(rect.Left + 12, rect.Top + 11), 3.5, 3.5);
        dc.DrawText(ft, new Point(rect.Left + 19, rect.Top + (22 - ft.Height) / 2));
    }

    static void Sticky(DrawingContext dc, BoardObject obj, PaintOptions o)
    {
        var pushed = false;
        if (obj.Rot != 0)
        {
            dc.PushTransform(new RotateTransform(obj.Rot, obj.X + obj.W / 2, obj.Y + obj.H / 2));
            pushed = true;
        }

        var fill = FillOf(obj, "#f2d06b");
        dc.DrawRectangle(B.Shadow, null, new Rect(obj.X + 2, obj.Y + 6, obj.W, obj.H));
        dc.DrawRoundedRectangle(Theme.HexBrush.FromHex(fill), null, new Rect(obj.X, obj.Y, obj.W, obj.H), 2, 2);

        if (!string.IsNullOrEmpty(obj.Text) && !IsEditing(obj, o))
            dc.DrawText(Ft(obj.Text, Fonts.Sans, 13.5, FontWeights.Medium, TextBrush(obj, fill), o.PixelsPerDip, obj.W - 24),
                new Point(obj.X + 12, obj.Y + 12));

        if (obj.Votes > 0)
        {
            var ft = Ft(obj.Votes.ToString(), Fonts.Sans, 10, FontWeights.Bold, B.White, o.PixelsPerDip);
            var rect = new Rect(obj.X + 12, obj.Y + obj.H - 26, ft.Width + 22, 17);
            dc.DrawRoundedRectangle(B.Badge, null, rect, 9, 9);
            dc.DrawEllipse(B.White, null, new Point(rect.Left + 9, rect.Top + 8.5), 3, 3);
            dc.DrawText(ft, new Point(rect.Left + 15, rect.Top + (17 - ft.Height) / 2));
        }

        if (pushed) dc.Pop();
    }

    static void Text(DrawingContext dc, BoardObject obj, PaintOptions o)
    {
        if (string.IsNullOrEmpty(obj.Text) || IsEditing(obj, o)) return;
        var weight = FontWeight.FromOpenTypeWeight(Math.Clamp(obj.Weight ?? 600, 100, 900));
        // free text sits on the canvas, so its own colour wins and the default stays light
        var brush = string.IsNullOrEmpty(obj.TextColor) ? B.Light : Theme.HexBrush.FromHex(obj.TextColor);
        dc.DrawText(Ft(obj.Text, Fonts.Sans, obj.Size ?? 20, weight, brush, o.PixelsPerDip, obj.W), new Point(obj.X, obj.Y));
    }

    static void Prompt(DrawingContext dc, BoardObject obj, PaintOptions o)
    {
        var rect = new Rect(obj.X, obj.Y, obj.W, 104);
        var fill = FillOf(obj, "#1c1e21");
        dc.DrawRectangle(B.Shadow, null, new Rect(rect.X + 2, rect.Y + 6, rect.Width, rect.Height));
        dc.DrawRoundedRectangle(Theme.HexBrush.FromHex(fill), B.PromptPen, rect, 8, 8);

        var label = Theme.HexBrush.FromHex(Palette.IsLight(fill) ? Palette.Shade(fill, 0.45) : "#9aa0a6");
        dc.DrawText(Ft(Theme.Icons.EditNote, Fonts.Icons, 14, FontWeights.Light, B.Yellow, o.PixelsPerDip),
            new Point(rect.X + 12, rect.Y + 11));
        dc.DrawText(Ft("PROMPT", Fonts.Mono, 9.5, FontWeights.Normal, label, o.PixelsPerDip),
            new Point(rect.X + 32, rect.Y + 13));

        if (string.IsNullOrEmpty(obj.Text) || IsEditing(obj, o)) return;
        var body = Ft(obj.Text, Fonts.Mono, 11.5, FontWeights.Normal, TextBrush(obj, fill, B.PromptText.ToString()),
            o.PixelsPerDip, obj.W - 24);
        body.MaxTextHeight = 64;
        body.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(body, new Point(rect.X + 12, rect.Y + 34));
    }

    static void Strokes(DrawingContext dc, BoardDoc doc)
    {
        foreach (var s in doc.Strokes)
        {
            if (s.Pts.Count < 2) continue;
            var pen = new Pen(Theme.HexBrush.FromHex(s.Color), s.W)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            dc.DrawGeometry(null, pen, Geometry.Parse(StrokePath.Of(s.Pts)));
        }
    }
}
