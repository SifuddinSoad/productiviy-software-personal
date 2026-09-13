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

        foreach (var o in doc.Objs.Where(o => o.Kind == ObjKind.Frame)) DrawFrame(dc, o);
        DrawConnectors(dc, c);
        foreach (var o in doc.Objs.Where(o => o.Kind == ObjKind.Table)) DrawTable(dc, o);
        foreach (var o in doc.Objs.Where(o => o.Kind == ObjKind.Shape)) DrawShape(dc, o);
        foreach (var o in doc.Objs.Where(o => o.Kind == ObjKind.Sticky)) DrawSticky(dc, o);
        foreach (var o in doc.Objs.Where(o => o.Kind == ObjKind.Text)) DrawText(dc, o);
        foreach (var o in doc.Objs.Where(o => o.Kind == ObjKind.Prompt)) DrawPrompt(dc, o);
        DrawStrokes(dc, c);
        DrawOverlays(dc, c);

        dc.Pop();
        PositionEditor();
    }

    static Rect ToRect(CoreRect r) => new(r.X, r.Y, Math.Max(0, r.W), Math.Max(0, r.H));

    /// <summary>True while this text is live in the overlay editor, so the canvas must not draw it too.</summary>
    bool IsEditingText(BoardObject o, int cell = -1) => _editingId == o.Id && _editingCell == cell;

    void DrawFrame(DrawingContext dc, BoardObject o)
    {
        dc.DrawRoundedRectangle(B.FrameFill, B.FramePen, new Rect(o.X, o.Y, o.W, o.H), 3, 3);
        if (!string.IsNullOrEmpty(o.Text) && !IsEditingText(o))
            dc.DrawText(Ft(o.Text, Fonts.Sans, 11, FontWeights.SemiBold, B.FrameLabel), new Point(o.X, o.Y - 19));
    }

    void DrawConnectors(DrawingContext dc, BoardController c)
    {
        var byId = c.Doc.Objs.ToDictionary(o => o.Id);
        foreach (var conn in c.Doc.Conns)
        {
            if (!BoardController.TryResolve(conn, byId, out var a, out var b)) continue;
            var path = ConnectorGeometry.Compute(conn, a, b);
            var selected = c.SelectedConnector == conn.Id;
            var thickness = selected ? 3.2 : 1.7;
            var pen = path.Dash is { } dash
                ? B.Dashed(B.Connector, thickness, dash[0], dash[1])
                : B.Frozen(new Pen(B.Connector, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
            dc.DrawGeometry(null, pen, Geometry.Parse(path.Data));
            if (path.Heads != "M 0 0") dc.DrawGeometry(B.Connector, null, Geometry.Parse(path.Heads));
        }

        // rubber band while a connector is being drawn, from an object or from a bare point
        CorePt? start = null;
        if (c.ConnectorFrom is { } fromId && byId.TryGetValue(fromId, out var from))
            start = ConnectorGeometry.Attach(from, c.MouseWorld.X, c.MouseWorld.Y, 2);
        else if (c.ConnectorFromPoint is { } p)
            start = p;

        if (start is { } s)
        {
            dc.DrawLine(B.Dashed(B.Connector, 1.5, 6, 5), new Point(s.X, s.Y), new Point(c.MouseWorld.X, c.MouseWorld.Y));
            dc.DrawEllipse(B.Canvas, B.Frozen(new Pen(B.Connector, 1.5)), new Point(s.X, s.Y), 3.5, 3.5);
        }
    }

    void DrawTable(DrawingContext dc, BoardObject o)
    {
        var cols = o.Cols ?? 1;
        var rows = o.Rows ?? 1;
        var cellW = o.W / cols;
        const double cellH = 32;
        var rect = new Rect(o.X, o.Y, o.W, rows * cellH);

        dc.DrawRoundedRectangle(B.TableFill, B.TablePen, rect, 4, 4);
        for (var i = 0; i < rows * cols; i++)
        {
            var r = i / cols;
            var col = i % cols;
            var cell = new Rect(o.X + col * cellW, o.Y + r * cellH, cellW, cellH);
            if (r == 0) dc.DrawRectangle(B.TableHeader, null, cell);
            dc.DrawLine(B.CellPen, new Point(cell.Right, cell.Top), new Point(cell.Right, cell.Bottom));
            dc.DrawLine(B.CellPen, new Point(cell.Left, cell.Bottom), new Point(cell.Right, cell.Bottom));

            var text = o.Cells is { } cells && i < cells.Count ? cells[i] : "";
            if (text.Length == 0 || IsEditingText(o, i)) continue;
            var ft = Ft(text, Fonts.Sans, 12, r == 0 ? FontWeights.SemiBold : FontWeights.Normal, B.Light, cellW - 18);
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(ft, new Point(cell.Left + 9, cell.Top + 7));
        }
    }

    void DrawShape(DrawingContext dc, BoardObject o)
    {
        var geometry = Geometry.Parse(ShapeGeometry.Path(o, o.W, o.H));
        dc.PushTransform(new TranslateTransform(o.X, o.Y));
        dc.DrawGeometry(Theme.HexBrush.FromHex(o.Fill ?? "#ffffff"), B.ShapePen, geometry);

        if (!string.IsNullOrEmpty(o.Text) && !IsEditingText(o))
        {
            var maxW = o.W * ShapeGeometry.TextWidthFactor(o);
            var ft = Ft(o.Text, Fonts.Sans, 13, FontWeights.Medium, B.OnLight, maxW, TextAlignment.Center);
            dc.DrawText(ft, new Point((o.W - maxW) / 2, (o.H - ft.Height) / 2));
        }
        dc.Pop();

        if (o.Votes > 0) DrawVoteBadge(dc, o.X + o.W + 9, o.Y - 11, o.Votes);
    }

    void DrawVoteBadge(DrawingContext dc, double right, double top, int votes)
    {
        var ft = Ft(votes.ToString(), Fonts.Sans, 11, FontWeights.Bold, B.White);
        var w = ft.Width + 26;
        var rect = new Rect(right - w, top, w, 22);
        dc.DrawRoundedRectangle(B.Badge, null, rect, 11, 11);
        dc.DrawEllipse(B.Yellow, null, new Point(rect.Left + 12, rect.Top + 11), 3.5, 3.5);
        dc.DrawText(ft, new Point(rect.Left + 19, rect.Top + (22 - ft.Height) / 2));
    }

    void DrawSticky(DrawingContext dc, BoardObject o)
    {
        var pushed = false;
        if (o.Rot != 0)
        {
            dc.PushTransform(new RotateTransform(o.Rot, o.X + o.W / 2, o.Y + o.H / 2));
            pushed = true;
        }

        dc.DrawRectangle(B.Shadow, null, new Rect(o.X + 2, o.Y + 6, o.W, o.H));
        dc.DrawRoundedRectangle(Theme.HexBrush.FromHex(o.Fill ?? "#f2d06b"), null, new Rect(o.X, o.Y, o.W, o.H), 2, 2);

        if (!string.IsNullOrEmpty(o.Text) && !IsEditingText(o))
            dc.DrawText(Ft(o.Text, Fonts.Sans, 13.5, FontWeights.Medium, B.OnLight, o.W - 24), new Point(o.X + 12, o.Y + 12));

        if (o.Votes > 0)
        {
            var ft = Ft(o.Votes.ToString(), Fonts.Sans, 10, FontWeights.Bold, B.White);
            var rect = new Rect(o.X + 12, o.Y + o.H - 26, ft.Width + 22, 17);
            dc.DrawRoundedRectangle(B.Badge, null, rect, 9, 9);
            dc.DrawEllipse(B.White, null, new Point(rect.Left + 9, rect.Top + 8.5), 3, 3);
            dc.DrawText(ft, new Point(rect.Left + 15, rect.Top + (17 - ft.Height) / 2));
        }

        if (pushed) dc.Pop();
    }

    void DrawText(DrawingContext dc, BoardObject o)
    {
        if (string.IsNullOrEmpty(o.Text) || IsEditingText(o)) return;
        var weight = FontWeight.FromOpenTypeWeight(Math.Clamp(o.Weight ?? 600, 100, 900));
        dc.DrawText(Ft(o.Text, Fonts.Sans, o.Size ?? 20, weight, B.Light, o.W), new Point(o.X, o.Y));
    }

    void DrawPrompt(DrawingContext dc, BoardObject o)
    {
        var rect = new Rect(o.X, o.Y, o.W, 104);
        dc.DrawRectangle(B.Shadow, null, new Rect(rect.X + 2, rect.Y + 6, rect.Width, rect.Height));
        dc.DrawRoundedRectangle(B.PromptFill, B.PromptPen, rect, 8, 8);

        dc.DrawText(Ft(Theme.Icons.EditNote, Fonts.Icons, 14, FontWeights.Light, B.Yellow), new Point(rect.X + 12, rect.Y + 11));
        dc.DrawText(Ft("PROMPT", Fonts.Mono, 9.5, FontWeights.Normal, B.PromptLabel), new Point(rect.X + 32, rect.Y + 13));

        if (!string.IsNullOrEmpty(o.Text) && !IsEditingText(o))
        {
            var ft = Ft(o.Text, Fonts.Mono, 11.5, FontWeights.Normal, B.PromptText, o.W - 24);
            ft.MaxTextHeight = 64;
            ft.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(ft, new Point(rect.X + 12, rect.Y + 34));
        }
    }

    void DrawStrokes(DrawingContext dc, BoardController c)
    {
        foreach (var s in c.Doc.Strokes)
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

    void DrawOverlays(DrawingContext dc, BoardController c)
    {
        var z = c.Doc.Cam.Z;

        if (c.PolyPoints is { Count: > 0 } poly)
        {
            var pts = poly.Append(c.MouseWorld).ToList();
            var data = "M " + string.Join(" L ", pts.Select(p => $"{p.X:0.0} {p.Y:0.0}"));
            dc.DrawGeometry(B.PolyFill, B.Dashed(B.Connector, 1.5, 5, 4), Geometry.Parse(data));
            foreach (var p in poly)
                dc.DrawEllipse(B.Canvas, B.Frozen(new Pen(B.Light, 1.5)), new Point(p.X, p.Y), 4, 4);
        }

        if (c.Draft is { } d)
        {
            var kind = d.Kind == Tool.Frame ? "rect" : c.ShapeKind;
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
            fg = B.Light;
        }
        else
        {
            switch (o.Kind)
            {
                case ObjKind.Sticky:
                    x = o.X + 12; y = o.Y + 12; w = o.W - 24; fontSize = 13.5;
                    break;
                case ObjKind.Text:
                    x = o.X; y = o.Y; w = o.W; fontSize = o.Size ?? 20;
                    weight = FontWeight.FromOpenTypeWeight(Math.Clamp(o.Weight ?? 600, 100, 900));
                    fg = B.Light;
                    break;
                case ObjKind.Prompt:
                    x = o.X + 12; y = o.Y + 34; w = o.W - 24; fontSize = 11.5;
                    family = Fonts.Mono; fg = B.PromptText;
                    break;
                case ObjKind.Frame:
                    x = o.X; y = o.Y - 19; w = Math.Max(120, o.W / 2); fontSize = 11;
                    weight = FontWeights.SemiBold; fg = B.FrameLabel;
                    break;
                default:
                    var maxW = o.W * ShapeGeometry.TextWidthFactor(o);
                    x = o.X + (o.W - maxW) / 2; y = o.Y + b.H / 2 - fontSizeGuess(13) / 2; w = maxW; fontSize = 13;
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
            if (Bounds.ObjectAt(c.Doc.Objs, world) is { } hit)
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
