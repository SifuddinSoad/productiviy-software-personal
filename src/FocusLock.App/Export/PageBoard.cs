using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FocusLock.App.Board;
using FocusLock.App.Theme;
using FocusLock.App.ViewModels;
using FocusLock.Core.Export;
using FocusLock.Core.Models;
using Fonts = FocusLock.App.Board.Fonts;
using CorePt = FocusLock.Core.Board.Pt;
using CoreRect = FocusLock.Core.Board.Rect;
using Rect = System.Windows.Rect;

namespace FocusLock.App.Export;

/// <summary>
/// Draws every A4 page in a column, with the sections on them, and turns the pointer into layout
/// edits: drag a section (onto any page), drag a corner to resize it, click a page header's buttons.
/// Like <see cref="BoardCanvas"/> it paints in one pass and scrolls itself, so pages, headers and
/// hit testing all come from the one <see cref="Layout"/>.
/// </summary>
public sealed class PageBoard : FrameworkElement
{
    const double TopPad = 58;
    const double Between = 76;
    const double SidePad = 48;
    const double HandleSize = 9;
    const double SnapPixels = 6;

    static readonly Brush Ground = HexBrush.FromHex("#0e0f10");
    static readonly Brush DarkSheet = HexBrush.FromHex("#121315");
    static readonly Brush LightSheet = Brushes.White;
    static readonly Brush Muted = HexBrush.FromHex("#7f8489");
    static readonly Brush Secondary = HexBrush.FromHex("#a9adb2");
    static readonly Brush GuideBrush = HexBrush.FromHex("#ff8a6b");
    static readonly Brush Missing = HexBrush.FromHex("#26292d");
    static readonly Pen SheetPen = B.Frozen(new Pen(HexBrush.FromHex("#34383d"), 1));
    static readonly Pen ChipPen = B.Frozen(new Pen(HexBrush.FromHex("#2a2d31"), 1));
    static readonly Pen DarkMarginPen = B.Dashed(HexBrush.FromHex("#24272b"), 1, 4, 4);
    static readonly Pen LightMarginPen = B.Dashed(HexBrush.FromHex("#e3e3df"), 1, 4, 4);
    static readonly Pen GuidePen = B.Frozen(new Pen(GuideBrush, 1));
    static readonly Pen AddPen = B.Dashed(HexBrush.FromHex("#34383d"), 1, 5, 4);
    static readonly Pen ItemPenDark = B.Frozen(new Pen(HexBrush.FromHex("#2a2d31"), 1));
    static readonly Pen SelectionOnLight = B.Frozen(new Pen(B.OnLight, 1.6));

    PageLayoutViewModel? _model;
    double _scrollX, _scrollY;

    // drag state
    string? _dragItem;
    string? _resizeCorner;
    CorePt _grab;          // pointer offset from the box's top-left, in points
    CorePt _anchor;        // the fixed corner while resizing, in points
    Point _downAt;
    bool _snapshotTaken;
    Point? _panFrom;

    public PageBoard()
    {
        Focusable = true;
        ClipToBounds = true;
        FocusVisualStyle = null;
    }

    public PageLayoutViewModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null) _model.Changed -= InvalidateVisual;
            _model = value;
            _scrollX = _scrollY = 0;
            if (_model is not null) _model.Changed += InvalidateVisual;
            InvalidateVisual();
        }
    }

    double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    // ---------------------------------------------------------------- geometry

    sealed record Slot(PdfPage Page, int Number, Rect Sheet, Rect Label, Rect Turn, Rect Delete);

    sealed record Layout(List<Slot> Slots, Rect AddButton, double ContentWidth, double ContentHeight);

    Layout Arrange(PageLayoutViewModel m)
    {
        var z = m.Zoom;
        var pages = m.Session.Pages;
        var widest = pages.Count == 0 ? PageLayout.A4Short : pages.Max(p => PageLayout.SizeOf(p).W);
        var contentWidth = widest * z + 2 * SidePad;
        var centerX = contentWidth <= ActualWidth ? ActualWidth / 2 : SidePad + widest * z / 2 - _scrollX;

        var slots = new List<Slot>();
        var y = TopPad - _scrollY;
        for (var i = 0; i < pages.Count; i++)
        {
            var (pw, ph) = PageLayout.SizeOf(pages[i]);
            var sheet = new Rect(centerX - pw * z / 2, y, pw * z, ph * z);
            var headerY = sheet.Y - 32;
            slots.Add(new Slot(pages[i], i + 1, sheet,
                new Rect(sheet.X, headerY, 58, 24),
                new Rect(sheet.X + 62, headerY, 104, 24),
                pages.Count > 1 ? new Rect(sheet.Right - 26, headerY, 26, 24) : Rect.Empty));
            y = sheet.Bottom + Between;
        }

        var add = new Rect(centerX - 70, y - Between + 20, 140, 34);
        var contentHeight = add.Bottom + 40 + _scrollY;
        return new Layout(slots, add, contentWidth, contentHeight);
    }

    void ClampScroll(Layout l)
    {
        _scrollX = Math.Clamp(_scrollX, 0, Math.Max(0, l.ContentWidth - ActualWidth));
        _scrollY = Math.Clamp(_scrollY, 0, Math.Max(0, l.ContentHeight - ActualHeight));
    }

    static CorePt ToPage(Slot s, Point p, double z) => new((p.X - s.Sheet.X) / z, (p.Y - s.Sheet.Y) / z);

    static Rect ToScreen(Slot s, CoreRect r, double z) =>
        new(s.Sheet.X + r.X * z, s.Sheet.Y + r.Y * z, Math.Max(0, r.W * z), Math.Max(0, r.H * z));

    /// <summary>The page a dragged section belongs to: the one whose band (sheet plus half the gap) holds the pointer.</summary>
    static Slot NearestSlot(Layout l, Point p) =>
        l.Slots.MinBy(s => p.Y < s.Sheet.Top ? s.Sheet.Top - p.Y : p.Y > s.Sheet.Bottom ? p.Y - s.Sheet.Bottom : 0)!;

    (Slot Slot, ExtractItem Item)? ItemAt(Layout l, Point p, PageLayoutViewModel m)
    {
        foreach (var slot in l.Slots)
        {
            if (!slot.Sheet.Contains(p)) continue;
            var pt = ToPage(slot, p, m.Zoom);
            var hit = m.Deck.ItemsOn(slot.Page.Id).LastOrDefault(e => PageLayout.BoxOf(e, m.Titles).Contains(pt));
            if (hit is not null) return (slot, hit);
        }
        return null;
    }

    (Slot Slot, ExtractItem Item, string Corner)? HandleAt(Layout l, Point p, PageLayoutViewModel m)
    {
        if (m.SelectedId is not { } id || m.Deck.ItemById(id) is not { } item) return null;
        var slot = l.Slots.FirstOrDefault(s => s.Page.Id == item.PageId);
        if (slot is null) return null;

        var box = ToScreen(slot, PageLayout.BoxOf(item, m.Titles), m.Zoom);
        foreach (var (corner, c) in Corners(box))
            if (Math.Abs(p.X - c.X) <= HandleSize && Math.Abs(p.Y - c.Y) <= HandleSize)
                return (slot, item, corner);
        return null;
    }

    static IEnumerable<(string Corner, Point At)> Corners(Rect r) =>
    [
        ("nw", r.TopLeft), ("ne", r.TopRight), ("sw", r.BottomLeft), ("se", r.BottomRight),
    ];

    // ---------------------------------------------------------------- rendering

    FormattedText Text(string text, double size, Brush brush, FontFamily? family = null, FontWeight? weight = null) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Fonts.Face(family ?? Fonts.Sans, weight ?? FontWeights.Medium), size, brush, Dpi);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Ground, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_model is not { } m) return;

        var l = Arrange(m);
        ClampScroll(l);
        l = Arrange(m);
        var z = m.Zoom;

        foreach (var slot in l.Slots)
        {
            DrawHeader(dc, slot, l.Slots.Count);

            dc.DrawRectangle(m.Light ? LightSheet : DarkSheet, SheetPen, slot.Sheet);
            var inset = PageLayout.Margin * z;
            dc.DrawRectangle(null, m.Light ? LightMarginPen : DarkMarginPen,
                new Rect(slot.Sheet.X + inset, slot.Sheet.Y + inset,
                    Math.Max(0, slot.Sheet.Width - 2 * inset), Math.Max(0, slot.Sheet.Height - 2 * inset)));

            dc.PushClip(new RectangleGeometry(slot.Sheet));
            foreach (var item in m.Deck.ItemsOn(slot.Page.Id)) DrawItem(dc, m, slot, item);

            if (m.GuidePageId == slot.Page.Id && _dragItem is not null)
                foreach (var g in m.Guides)
                {
                    if (g.Vertical)
                        dc.DrawLine(GuidePen, new Point(slot.Sheet.X + g.At * z, slot.Sheet.Top), new Point(slot.Sheet.X + g.At * z, slot.Sheet.Bottom));
                    else
                        dc.DrawLine(GuidePen, new Point(slot.Sheet.Left, slot.Sheet.Y + g.At * z), new Point(slot.Sheet.Right, slot.Sheet.Y + g.At * z));
                }
            dc.Pop();
        }

        if (m.Session.Extracts.Count == 0 && l.Slots.Count > 0)
        {
            var hint = Text("Pick a region on the canvas and it lands here", 12, Muted);
            var s = l.Slots[0].Sheet;
            dc.DrawText(hint, new Point(s.X + (s.Width - hint.Width) / 2, s.Y + s.Height / 2 - hint.Height / 2));
        }

        dc.DrawRoundedRectangle(null, AddPen, l.AddButton, 9, 9);
        var plus = Text(Icons.Add, 16, Secondary, Fonts.Icons, FontWeights.Light);
        var add = Text("Add page", 12, Secondary, weight: FontWeights.SemiBold);
        var startX = l.AddButton.X + (l.AddButton.Width - plus.Width - 6 - add.Width) / 2;
        dc.DrawText(plus, new Point(startX, l.AddButton.Y + (l.AddButton.Height - plus.Height) / 2));
        dc.DrawText(add, new Point(startX + plus.Width + 6, l.AddButton.Y + (l.AddButton.Height - add.Height) / 2));

        DrawScrollThumb(dc, l);
    }

    void DrawHeader(DrawingContext dc, Slot slot, int pageCount)
    {
        var label = Text($"Page {slot.Number}", 12, Muted, weight: FontWeights.SemiBold);
        dc.DrawText(label, new Point(slot.Label.X, slot.Label.Y + (slot.Label.Height - label.Height) / 2));

        dc.DrawRoundedRectangle(null, ChipPen, slot.Turn, 7, 7);
        var icon = Text(slot.Page.Landscape ? "" : "", 15, Secondary, Fonts.Icons, FontWeights.Light);
        var word = Text(slot.Page.Landscape ? "Landscape" : "Portrait", 11, Secondary, weight: FontWeights.SemiBold);
        var x = slot.Turn.X + (slot.Turn.Width - icon.Width - 4 - word.Width) / 2;
        dc.DrawText(icon, new Point(x, slot.Turn.Y + (slot.Turn.Height - icon.Height) / 2));
        dc.DrawText(word, new Point(x + icon.Width + 4, slot.Turn.Y + (slot.Turn.Height - word.Height) / 2));

        if (pageCount > 1)
        {
            var trash = Text(Icons.Delete, 16, Muted, Fonts.Icons, FontWeights.Light);
            dc.DrawText(trash, new Point(slot.Delete.X + (slot.Delete.Width - trash.Width) / 2,
                slot.Delete.Y + (slot.Delete.Height - trash.Height) / 2));
        }
    }

    void DrawItem(DrawingContext dc, PageLayoutViewModel m, Slot slot, ExtractItem item)
    {
        var z = m.Zoom;
        var box = ToScreen(slot, PageLayout.BoxOf(item, m.Titles), z);
        var picture = box;

        if (m.Titles)
        {
            var band = PageLayout.CaptionHeight * z;
            if (!string.IsNullOrWhiteSpace(item.Name) && band > 6)
            {
                var caption = Text(item.Name, Math.Max(1, 10 * z), m.Light ? B.OnLight : B.Light, weight: FontWeights.Normal);
                caption.MaxTextWidth = Math.Max(1, box.Width);
                caption.MaxLineCount = 1;
                caption.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawText(caption, new Point(box.X, box.Y + 3 * z));
            }
            picture = new Rect(box.X, box.Y + band, box.Width, Math.Max(0, box.Height - band));
        }

        if (m.PictureFor(item) is { } image)
        {
            dc.DrawImage(image, picture);
            if (!m.Light) dc.DrawRectangle(null, ItemPenDark, picture);
        }
        else
        {
            dc.DrawRectangle(Missing, null, picture);
            var gone = Text("Plan removed", Math.Max(9, 11 * z), Muted);
            dc.DrawText(gone, new Point(picture.X + 8, picture.Y + 6));
        }

        if (m.SelectedId != item.Id) return;
        // the canvas's light selection colour vanishes on a white page, so it flips with the page
        var ink = m.Light ? SelectionOnLight : B.SelectionPen;
        dc.DrawRectangle(null, ink, new Rect(box.X - 1, box.Y - 1, box.Width + 2, box.Height + 2));
        foreach (var (_, c) in Corners(box))
            dc.DrawRectangle(m.Light ? Brushes.White : B.Canvas, ink,
                new Rect(c.X - HandleSize / 2, c.Y - HandleSize / 2, HandleSize, HandleSize));
    }

    void DrawScrollThumb(DrawingContext dc, Layout l)
    {
        if (l.ContentHeight <= ActualHeight + 1) return;
        var track = ActualHeight - 8;
        var length = Math.Max(30, track * ActualHeight / l.ContentHeight);
        var top = 4 + (track - length) * (_scrollY / Math.Max(1, l.ContentHeight - ActualHeight));
        dc.DrawRoundedRectangle(HexBrush.FromHex("#34383d"), null, new Rect(ActualWidth - 7, top, 4, length), 2, 2);
    }

    // ---------------------------------------------------------------- input

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_model is not { } m) return;
        var p = e.GetPosition(this);
        var l = Arrange(m);

        if (e.ChangedButton == MouseButton.Middle)
        {
            _panFrom = p;
            CaptureMouse();
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;
        e.Handled = true;

        if (l.AddButton.Contains(p)) { m.AddPage(); return; }

        foreach (var slot in l.Slots)
        {
            if (slot.Turn.Contains(p)) { m.ToggleLandscape(slot.Page); return; }
            if (!slot.Delete.IsEmpty && slot.Delete.Contains(p)) { m.DeletePage(slot.Page); return; }
        }

        if (HandleAt(l, p, m) is { } handle)
        {
            var box = PageLayout.BoxOf(handle.Item, m.Titles);
            _dragItem = handle.Item.Id;
            _resizeCorner = handle.Corner;
            _anchor = handle.Corner switch
            {
                "nw" => new CorePt(box.Right, box.Bottom),
                "ne" => new CorePt(box.X, box.Bottom),
                "sw" => new CorePt(box.Right, box.Y),
                _ => new CorePt(box.X, box.Y),
            };
            StartDrag(p);
            return;
        }

        if (ItemAt(l, p, m) is { } hit)
        {
            m.Select(hit.Item.Id);
            var pt = ToPage(hit.Slot, p, m.Zoom);
            _dragItem = hit.Item.Id;
            _resizeCorner = null;
            _grab = new CorePt(pt.X - hit.Item.PageX, pt.Y - hit.Item.PageY);
            StartDrag(p);
            return;
        }

        m.Select(null);
    }

    void StartDrag(Point p)
    {
        _downAt = p;
        _snapshotTaken = false;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_model is not { } m) return;
        var p = e.GetPosition(this);

        if (_panFrom is { } from)
        {
            _scrollX -= p.X - from.X;
            _scrollY -= p.Y - from.Y;
            _panFrom = p;
            InvalidateVisual();
            return;
        }

        var l = Arrange(m);
        if (_dragItem is null || m.Deck.ItemById(_dragItem) is not { } item)
        {
            UpdateCursor(l, p, m);
            return;
        }

        // a click is not an edit; only real movement earns an undo step
        if (!_snapshotTaken)
        {
            if (Math.Abs(p.X - _downAt.X) + Math.Abs(p.Y - _downAt.Y) < 3) return;
            m.Deck.Snapshot();
            _snapshotTaken = true;
        }

        var z = m.Zoom;
        var snap = (Keyboard.Modifiers & ModifierKeys.Alt) == 0;
        var threshold = snap ? SnapPixels / z : -1;

        if (_resizeCorner is { } corner)
        {
            var slot = l.Slots.First(s => s.Page.Id == item.PageId);
            var (pw, ph) = PageLayout.SizeOf(slot.Page);
            var others = m.Deck.ItemsOn(slot.Page.Id).Where(o => o != item).Select(o => PageLayout.BoxOf(o, m.Titles)).ToList();
            var r = Snapping.Resize(_anchor, ToPage(slot, p, z), corner, PageLayout.AspectOf(item),
                m.Titles ? PageLayout.CaptionHeight : 0, pw, ph, others, threshold, PageLayout.MinWidth);
            m.Deck.Place(item.Id, slot.Page.Id, r.Box.X, r.Box.Y, r.Box.W);
            m.ShowGuides(slot.Page.Id, r.Guides);
        }
        else
        {
            var slot = NearestSlot(l, p);
            var (pw, ph) = PageLayout.SizeOf(slot.Page);
            var pt = ToPage(slot, p, z);
            var box = PageLayout.BoxOf(item, m.Titles) with { X = pt.X - _grab.X, Y = pt.Y - _grab.Y };
            var others = m.Deck.ItemsOn(slot.Page.Id).Where(o => o != item).Select(o => PageLayout.BoxOf(o, m.Titles)).ToList();
            var r = Snapping.Move(box, pw, ph, others, threshold);
            m.Deck.Place(item.Id, slot.Page.Id, r.Box.X, r.Box.Y, item.PageW);
            m.ShowGuides(slot.Page.Id, r.Guides);
        }
        m.Redraw();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
        _panFrom = null;
        if (_model is not { } m || _dragItem is null) return;

        var changed = _snapshotTaken;
        _dragItem = null;
        _resizeCorner = null;
        m.ShowGuides(null, []);
        if (changed) m.Commit();
        else m.Redraw();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_model is not { } m) return;
        e.Handled = true;

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            // keep the point under the pointer where it is
            var p = e.GetPosition(this);
            var before = m.Zoom;
            m.ZoomBy(e.Delta > 0 ? 1.1 : 1 / 1.1);
            var ratio = m.Zoom / before;
            _scrollY = (_scrollY + p.Y - TopPad) * ratio - (p.Y - TopPad);
            _scrollX = (_scrollX + p.X) * ratio - p.X;
            InvalidateVisual();
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) _scrollX -= e.Delta * 0.6;
        else _scrollY -= e.Delta * 0.6;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_model is not { } m) return;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        switch (e.Key)
        {
            case Key.Delete or Key.Back: m.RemoveSelected(); break;
            case Key.Escape: m.Select(null); break;
            case Key.Z when ctrl && shift: m.Redo(); break;
            case Key.Z when ctrl: m.Undo(); break;
            case Key.Y when ctrl: m.Redo(); break;
            default: return;
        }
        e.Handled = true;
    }

    void UpdateCursor(Layout l, Point p, PageLayoutViewModel m)
    {
        if (HandleAt(l, p, m) is { } h)
        {
            Cursor = h.Corner is "nw" or "se" ? Cursors.SizeNWSE : Cursors.SizeNESW;
            return;
        }
        if (l.AddButton.Contains(p) || l.Slots.Any(s => s.Turn.Contains(p) || (!s.Delete.IsEmpty && s.Delete.Contains(p))))
        {
            Cursor = Cursors.Hand;
            return;
        }
        Cursor = ItemAt(l, p, m) is not null ? Cursors.SizeAll : Cursors.Arrow;
    }
}
