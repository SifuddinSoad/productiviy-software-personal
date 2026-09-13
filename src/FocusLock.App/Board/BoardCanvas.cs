using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusLock.Core.Board;
using FocusLock.Core.Models;
using CorePt = FocusLock.Core.Board.Pt;
using CoreRect = FocusLock.Core.Board.Rect;
using Rect = System.Windows.Rect;

namespace FocusLock.App.Board;

/// <summary>
/// Draws the whole board with a single <see cref="DrawingContext"/> pass and routes pointer input to
/// <see cref="BoardController"/>. Text editing uses one overlay <see cref="TextBox"/> placed over the
/// object being edited.
/// </summary>
public sealed class BoardCanvas : Canvas
{
    readonly TextBox _editor = new();
    BoardController? _controller;
    string? _editingId;
    int _editingCell = -1;

    public BoardCanvas()
    {
        Background = B.Canvas;
        ClipToBounds = true;
        Focusable = true;
        SnapsToDevicePixels = true;

        _editor.Visibility = Visibility.Collapsed;
        _editor.AcceptsReturn = true;
        _editor.TextWrapping = TextWrapping.Wrap;
        _editor.BorderThickness = new Thickness(0);
        _editor.Background = Brushes.Transparent;
        _editor.Padding = new Thickness(0);
        _editor.LostFocus += (_, _) => CommitEdit();
        _editor.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; CancelEdit(); }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; CommitEdit(); }
        };
        Children.Add(_editor);
    }

    /// <summary>True while a label is open in the overlay editor; keyboard shortcuts must stand down.</summary>
    public bool IsEditing => _editingId is not null;

    public BoardController? Controller
    {
        get => _controller;
        set
        {
            if (_controller is not null)
            {
                _controller.Changed -= OnChanged;
                _controller.EditRequested -= OnEditRequested;
            }
            _controller = value;
            if (_controller is not null)
            {
                _controller.Changed += OnChanged;
                _controller.EditRequested += OnEditRequested;
            }
            InvalidateVisual();
        }
    }

    void OnChanged() => InvalidateVisual();

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (_controller is not null)
        {
            _controller.ViewportWidth = ActualWidth;
            _controller.ViewportHeight = ActualHeight;
        }
    }

    double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    FormattedText Ft(string text, FontFamily family, double size, FontWeight weight, Brush brush, double? maxWidth = null,
        TextAlignment align = TextAlignment.Left)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Fonts.Face(family, weight), size, brush, Dpi)
        {
            TextAlignment = align,
        };
        if (maxWidth is { } w) ft.MaxTextWidth = Math.Max(1, w);
        return ft;
    }

    // ---------------------------------------------------------------- rendering

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(B.Canvas, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_controller is not { } c) return;

        var doc = c.Doc;
        dc.PushTransform(new MatrixTransform(doc.Cam.Z, 0, 0, doc.Cam.Z, doc.Cam.X, doc.Cam.Y));

        BoardPainter.Paint(dc, doc, new PaintOptions(_editingId, _editingCell, c.SelectedConnectorIds, Dpi));
        DrawOverlays(dc, c);

        dc.Pop();
        PositionEditor();
    }

    static Rect ToRect(CoreRect r) => new(r.X, r.Y, Math.Max(0, r.W), Math.Max(0, r.H));

    /// <summary>The line that follows the pointer while a connector is being drawn.</summary>
    void DrawConnectorRubberBand(DrawingContext dc, BoardController c)
    {
        var byId = c.Doc.Objs.ToDictionary(o => o.Id);
        CorePt? start = null;
        if (c.ConnectorFrom is { } fromId && byId.TryGetValue(fromId, out var from))
            start = ConnectorGeometry.Attach(from, c.MouseWorld.X, c.MouseWorld.Y, 2);
        else if (c.ConnectorFromPoint is { } p)
            start = p;

        if (start is not { } s) return;
        dc.DrawLine(B.Dashed(B.Connector, 1.5, 6, 5), new Point(s.X, s.Y), new Point(c.MouseWorld.X, c.MouseWorld.Y));
        dc.DrawEllipse(B.Canvas, B.Frozen(new Pen(B.Connector, 1.5)), new Point(s.X, s.Y), 3.5, 3.5);
    }

    void DrawOverlays(DrawingContext dc, BoardController c)
    {
        var z = c.Doc.Cam.Z;
        DrawConnectorRubberBand(dc, c);

        if (c.PolyPoints is { Count: > 0 } poly)
        {
            var pts = poly.Append(c.MouseWorld).ToList();
            var data = "M " + string.Join(" L ", pts.Select(p => $"{p.X:0.0} {p.Y:0.0}"));
            dc.DrawGeometry(B.PolyFill, B.Dashed(B.Connector, 1.5, 5, 4), Geometry.Parse(data));
            foreach (var p in poly)
                dc.DrawEllipse(B.Canvas, B.Frozen(new Pen(B.Light, 1.5)), new Point(p.X, p.Y), 4, 4);
        }

        // regions already picked for the PDF, so you can see what has been taken
        if (c.ShowExtractOutlines || c.CurrentTool == Tool.Extract)
        {
            var pen = B.Dashed(B.Green, 1.6 / z, 7 / z, 5 / z);
            for (var i = 0; i < c.ExtractOutlines.Count; i++)
            {
                var r = c.ExtractOutlines[i];
                dc.DrawRectangle(null, pen, ToRect(r));

                var ft = BoardPainter.Ft($"{i + 1}", Fonts.Mono, 11 / z, FontWeights.Bold, B.OnLight, Dpi);
                var badge = new Rect(r.X, r.Y - 20 / z, ft.Width + 12 / z, 17 / z);
                dc.DrawRoundedRectangle(B.Green, null, badge, 4 / z, 4 / z);
                dc.DrawText(ft, new Point(badge.X + 6 / z, badge.Y + 1 / z));
            }
        }

        if (c.Draft is { } d)
        {
            var kind = d.Kind is Tool.Frame or Tool.Extract ? "rect" : c.ShapeKind;
            var data = ShapeGeometry.Path(kind, null, Math.Max(1, d.W), Math.Max(1, d.H));
            dc.PushTransform(new TranslateTransform(d.X, d.Y));
            dc.DrawGeometry(B.DraftFill, B.Dashed(B.Light, 1.5, 5, 4), Geometry.Parse(data));
            dc.Pop();
            DrawSizeBadge(dc, d.X + d.W / 2, d.Y + d.H + 8 / z, $"{Math.Round(d.W)} × {Math.Round(d.H)}", z);
        }

        if (c.Marquee is { } m)
            dc.DrawRectangle(B.MarqueeFill, B.MarqueePen, ToRect(m));

        var selected = c.SelectedObjects.ToList();
        if (selected.Count > 1)
            foreach (var o in selected)
            {
                var b = Bounds.Of(o);
                dc.DrawRectangle(null, B.SelectionMemberPen, new Rect(b.X - 2, b.Y - 2, b.W + 4, b.H + 4));
            }

        if (c.SelectionBox() is { } box)
        {
            dc.DrawRectangle(null, B.SelectionPen, ToRect(box));
            if (selected.Count > 1)
            {
                var ft = Ft($"{selected.Count} selected", Fonts.Sans, 10.5 / z, FontWeights.SemiBold, B.OnLight);
                var rect = new Rect(box.X, box.Y - 24 / z, ft.Width + 14 / z, 19 / z);
                dc.DrawRoundedRectangle(B.Light, null, rect, 5 / z, 5 / z);
                dc.DrawText(ft, new Point(rect.X + 7 / z, rect.Y + (rect.Height - ft.Height) / 2));
            }
        }

        if (c.SingleSelection is { } single && !c.ReadOnly)
        {
            var b = Bounds.Of(single);
            var size = 9 / z;
            foreach (var (_, fx, fy) in BoardController.Handles)
            {
                var rect = new Rect(b.X + fx * b.W - size / 2, b.Y + fy * b.H - size / 2, size, size);
                dc.DrawRoundedRectangle(B.Canvas, B.Frozen(new Pen(B.Light, 1.5 / z)), rect, 1.5 / z, 1.5 / z);
            }
            if (c.IsResizing)
                DrawSizeBadge(dc, b.X + b.W / 2, b.Bottom + 8 / z, $"{Math.Round(single.W)} × {Math.Round(Bounds.Size(single).H)}", z);
        }

        // grab handles on the selected connector's two ends
        if (c.SelectedConnectorEnds() is { } ends && !c.ReadOnly)
        {
            var size = 9 / z;
            foreach (var p in new[] { ends.A, ends.B })
                dc.DrawEllipse(B.Canvas, B.Frozen(new Pen(B.Light, 1.8 / z)), new Point(p.X, p.Y), size / 2, size / 2);
        }

        if (c.CurrentTool == Tool.Eraser && !c.ReadOnly)
        {
            var r = c.EraserSize / 2;
            dc.DrawEllipse(B.EraserFill, B.Frozen(new Pen(B.Light, 1.5 / z)), new Point(c.MouseWorld.X, c.MouseWorld.Y), r, r);
        }
    }

    void DrawSizeBadge(DrawingContext dc, double cx, double top, string label, double z)
    {
        var ft = Ft(label, Fonts.Mono, 11 / z, FontWeights.Normal, B.OnLight);
        var rect = new Rect(cx - ft.Width / 2 - 6 / z, top, ft.Width + 12 / z, ft.Height + 4 / z);
        dc.DrawRoundedRectangle(B.Light, null, rect, 4 / z, 4 / z);
        dc.DrawText(ft, new Point(rect.X + 6 / z, rect.Y + 2 / z));
    }

    // ---------------------------------------------------------------- editing overlay

    void OnEditRequested(string id, int cell)
    {
        _editingId = id;
        _editingCell = cell;
        var o = _controller?.Editor.ById(id);
        if (o is null) return;

        _editor.Text = cell >= 0 ? o.Cells?.ElementAtOrDefault(cell) ?? "" : o.Text;
        _editor.Visibility = Visibility.Visible;
        PositionEditor();

        // The editor has only just been made visible; focusing it in the same pass silently fails
        // and every keystroke would then reach the canvas as a tool shortcut.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_editingId != id) return;
            Keyboard.Focus(_editor);
            _editor.SelectAll();
        });
    }

    void PositionEditor()
    {
        if (_editingId is null || _controller is not { } c) return;
        if (c.Editor.ById(_editingId) is not { } o)
        {
            HideEditor();
            return;
        }

        var z = c.Doc.Cam.Z;
        var b = Bounds.Of(o);
        double x, y, w;
        double fontSize;
        FontWeight weight = FontWeights.Medium;
        FontFamily family = Fonts.Sans;
        Brush fg = B.OnLight;

        if (_editingCell >= 0)
        {
            var cols = o.Cols ?? 1;
            var cellW = o.W / cols;
            x = o.X + _editingCell % cols * cellW + 9;
            y = o.Y + _editingCell / cols * 32 + 7;
            w = cellW - 18;
            fontSize = 12;
            fg = BoardPainter.TextBrush(o, BoardPainter.FillOf(o, "#1b1d20"), B.Light.ToString());
        }
        else
        {
            switch (o.Kind)
            {
                case ObjKind.Sticky:
                    x = o.X + 12; y = o.Y + 12; w = o.W - 24; fontSize = 13.5;
                    fg = BoardPainter.TextBrush(o, BoardPainter.FillOf(o, "#f2d06b"));
                    break;
                case ObjKind.Text:
                    x = o.X; y = o.Y; w = o.W; fontSize = o.Size ?? 20;
                    weight = FontWeight.FromOpenTypeWeight(Math.Clamp(o.Weight ?? 600, 100, 900));
                    fg = string.IsNullOrEmpty(o.TextColor) ? B.Light : Theme.HexBrush.FromHex(o.TextColor);
                    break;
                case ObjKind.Prompt:
                    x = o.X + 12; y = o.Y + 34; w = o.W - 24; fontSize = 11.5;
                    family = Fonts.Mono;
                    fg = BoardPainter.TextBrush(o, BoardPainter.FillOf(o, "#1c1e21"), B.PromptText.ToString());
                    break;
                case ObjKind.Frame:
                    x = o.X; y = o.Y - 19; w = Math.Max(120, o.W / 2); fontSize = 11;
                    weight = FontWeights.SemiBold;
                    fg = string.IsNullOrEmpty(o.TextColor) ? B.FrameLabel : Theme.HexBrush.FromHex(o.TextColor);
                    break;
                default:
                    var maxW = o.W * ShapeGeometry.TextWidthFactor(o);
                    x = o.X + (o.W - maxW) / 2; y = o.Y + b.H / 2 - fontSizeGuess(13) / 2; w = maxW; fontSize = 13;
                    fg = BoardPainter.TextBrush(o, BoardPainter.FillOf(o, "#ffffff"));
                    break;
            }
        }

        static double fontSizeGuess(double size) => size * 1.4;

        _editor.FontFamily = family;
        _editor.FontSize = fontSize * z;
        _editor.FontWeight = weight;
        _editor.Foreground = fg;
        _editor.CaretBrush = fg;
        _editor.Width = Math.Max(24, w * z);
        SetLeft(_editor, x * z + c.Doc.Cam.X);
        SetTop(_editor, y * z + c.Doc.Cam.Y);
    }

    void CommitEdit()
    {
        if (_editingId is null || _controller is null) return;
        var id = _editingId;
        var cell = _editingCell;
        var text = _editor.Text.Trim();
        HideEditor();
        if (cell >= 0) _controller.CommitCell(id, cell, text);
        else _controller.CommitText(id, text);
    }

    void CancelEdit()
    {
        HideEditor();
        _controller?.Notify();
        Focus();
    }

    void HideEditor()
    {
        _editingId = null;
        _editingCell = -1;
        _editor.Visibility = Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- input

    static CorePt P(MouseEventArgs e, IInputElement el)
    {
        var p = e.GetPosition(el);
        return new CorePt(p.X, p.Y);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (_controller is not { } c) return;
        Focus();
        if (_editingId is not null) CommitEdit();

        var pt = P(e, this);

        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            var world = c.ToWorld(pt);
            if ((Bounds.ObjectAt(c.Doc.Objs, world) ?? Bounds.FrameLabelAt(c.Doc.Objs, world)) is { } hit)
            {
                var cell = hit.Kind == ObjKind.Table ? CellIndexAt(hit, world) : -1;
                c.Select([hit.Id]);
                c.BeginEdit(hit.Id, cell);
                e.Handled = true;
                return;
            }
        }

        CaptureMouse();
        c.PointerDown(pt, e.ChangedButton == MouseButton.Middle,
            (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        e.Handled = true;
    }

    static int CellIndexAt(BoardObject table, CorePt world)
    {
        var cols = table.Cols ?? 1;
        var rows = table.Rows ?? 1;
        var col = (int)Math.Clamp((world.X - table.X) / (table.W / cols), 0, cols - 1);
        var row = (int)Math.Clamp((world.Y - table.Y) / 32, 0, rows - 1);
        return row * cols + col;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_controller is not { } c) return;
        c.PointerMove(P(e, this));
        UpdateCursor(P(e, this));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
        _controller?.PointerUp(P(e, this));
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_controller is not { } c) return;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        c.Wheel(0, -e.Delta * (ctrl ? 1 : 0.6), ctrl, P(e, this));
        e.Handled = true;
    }

    void UpdateCursor(CorePt screen)
    {
        if (_controller is not { } c) return;
        if (c.ReadOnly || c.CurrentTool == Tool.Hand || c.SpaceHeld) { Cursor = Cursors.Hand; return; }
        if (c.HandleAt(screen) is { } dir)
        {
            Cursor = dir switch
            {
                "n" or "s" => Cursors.SizeNS,
                "e" or "w" => Cursors.SizeWE,
                "ne" or "sw" => Cursors.SizeNESW,
                _ => Cursors.SizeNWSE,
            };
            return;
        }
        if (c.CurrentTool == Tool.Select && c.ConnectorEndAt(screen) is not null)
        {
            Cursor = Cursors.SizeAll;
            return;
        }
        Cursor = c.CurrentTool switch
        {
            Tool.Select => Cursors.Arrow,
            Tool.Eraser => Cursors.None,
            _ => Cursors.Cross,
        };
    }
}
