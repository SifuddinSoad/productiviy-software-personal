using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusLock.App.Board;
using FocusLock.App.Document;
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
/// Draws every A4 page in a column, with the sections and text boxes on them, and turns the pointer
/// into layout edits: drag a section or text box (onto any page), resize a section from a corner or a
/// text box from its side, double-click a text box to type in it, click a page header's buttons.
/// Like <see cref="BoardCanvas"/> it paints in one pass and scrolls itself, so pages, headers and hit
/// testing all come from the one <see cref="Layout"/>.
/// </summary>
public sealed class PageBoard : FrameworkElement
{
    const double TopPad = 28;
    const double Between = 52;
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
    static readonly Pen DarkMarginPen = B.Dashed(HexBrush.FromHex("#24272b"), 1, 4, 4);
    static readonly Pen LightMarginPen = B.Dashed(HexBrush.FromHex("#e3e3df"), 1, 4, 4);
    static readonly Pen GuidePen = B.Frozen(new Pen(GuideBrush, 1));
    static readonly Pen AddPen = B.Dashed(HexBrush.FromHex("#34383d"), 1, 5, 4);
    static readonly Pen ItemPenDark = B.Frozen(new Pen(HexBrush.FromHex("#2a2d31"), 1));
    static readonly Pen RunOnDark = B.Dashed(HexBrush.FromHex("#5a6067"), 1, 3, 3);
    static readonly Pen RunOnLight = B.Dashed(HexBrush.FromHex("#b4b8bd"), 1, 3, 3);

    // one accent for selection, hover and drop hints, readable on white and dark paper alike
    static readonly Brush Accent = HexBrush.FromHex("#378add");
    static readonly Pen AccentPen = B.Frozen(new Pen(Accent, 1.6));
    static readonly Pen HoverPen = B.Frozen(new Pen(HexBrush.FromHex("#80378add"), 1));
    static readonly Pen GhostPen = B.Dashed(Accent, 1.4, 5, 4);
    static readonly Pen DropPen = B.Frozen(new Pen(Accent, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    static readonly Brush GhostFill = HexBrush.FromHex("#1a378add");
    static readonly Brush ShadowNear = HexBrush.FromHex("#40000000");
    static readonly Brush ShadowFar = HexBrush.FromHex("#1c000000");

    /// <summary>Selection outlines and handles sit this far outside a box, so they never cover its edge.</summary>
    const double Outset = 4;

    /// <summary>How long pushed things take to glide to their new place.</summary>
    static readonly TimeSpan GlideTime = TimeSpan.FromMilliseconds(180);

    PageLayoutViewModel? _model;
    double _scrollX, _scrollY;

    // drag state
    string? _dragId;
    bool _dragText;
    string? _resizeCorner;
    CorePt _grab;          // pointer offset from the box's top-left, in points
    CorePt _anchor;        // the fixed corner while resizing, in points
    Point _downAt;
    bool _snapshotTaken;
    Point? _panFrom;

    /// <summary>
    /// A moving text box's first part as it was laid out when the drag began. It is drawn at the
    /// pointer instead of laying the text out again on every step, which made dragging stick.
    /// </summary>
    TextFragment? _dragFragment;

    /// <summary>Where the box being dragged would land if dropped now, on which page.</summary>
    (string PageId, CoreRect Box)? _landing;

    /// <summary>The box under the pointer, outlined lightly.</summary>
    string? _hoverId;

    /// <summary>Things gliding from where they were to where they are: the old spot and when the glide began.</summary>
    readonly Dictionary<string, (PageLayoutViewModel.Spot From, DateTime Start)> _glides = [];
    bool _animating;

    /// <summary>Pictures of text fragments at the zoom they were drawn for; a fragment is replaced whenever its text is laid out again.</summary>
    readonly ConditionalWeakTable<TextFragment, Tuple<double, BitmapSource>> _fragmentPictures = [];

    /// <summary>The pages scrolled or zoomed, or were laid out again; anything placed over them must follow.</summary>
    public event Action? ViewMoved;

    public PageBoard()
    {
        Focusable = true;
        ClipToBounds = true;
        FocusVisualStyle = null;
        _flashEnds.Tick += (_, _) =>
        {
            _flashEnds.Stop();
            _scrollToPage = -1;
            InvalidateVisual();
        };
    }

    public PageLayoutViewModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.Changed -= OnModelChanged;
                _model.Moved -= OnMoved;
            }
            _model = value;
            _scrollX = _scrollY = 0;
            _glides.Clear();
            if (_model is not null)
            {
                _model.Changed += OnModelChanged;
                _model.Moved += OnMoved;
            }
            InvalidateVisual();
        }
    }

    void OnModelChanged() => InvalidateVisual();

    // ---------------------------------------------------------------- gliding

    void OnMoved(IReadOnlyDictionary<string, PageLayoutViewModel.Spot> from)
    {
        var now = DateTime.UtcNow;
        foreach (var (id, spot) in from) _glides[id] = (spot, now);
        if (_animating) return;
        _animating = true;
        CompositionTarget.Rendering += OnFrame;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        foreach (var id in _glides.Where(g => now - g.Value.Start >= GlideTime).Select(g => g.Key).ToList()) _glides.Remove(id);
        if (_glides.Count == 0)
        {
            CompositionTarget.Rendering -= OnFrame;
            _animating = false;
        }
        InvalidateVisual();
    }

    /// <summary>How far a gliding thing is still drawn from its real place, in points; hit testing always uses the real place.</summary>
    Vector GlideOffset(string id, int pageIndex, double x, double y)
    {
        if (!_glides.TryGetValue(id, out var glide) || glide.From.Page != pageIndex) return default;
        var t = Math.Clamp((DateTime.UtcNow - glide.Start).TotalMilliseconds / GlideTime.TotalMilliseconds, 0, 1);
        var left = Math.Pow(1 - t, 3);   // ease out
        return new Vector((glide.From.X - x) * left, (glide.From.Y - y) * left);
    }

    double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    // ---------------------------------------------------------------- geometry

    sealed record Slot(PdfPage Page, int Index, Rect Sheet);

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
            slots.Add(new Slot(pages[i], i, sheet));
            y = sheet.Bottom + Between;   // the page's label sits in the gap below it
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

    static CoreRect RectOf(TextFragment f) => new(f.X, f.Y, f.W, f.H);

    /// <summary>The page a dragged box belongs to: the one whose band (sheet plus half the gap) holds the pointer.</summary>
    static Slot NearestSlot(Layout l, Point p) =>
        l.Slots.MinBy(s => p.Y < s.Sheet.Top ? s.Sheet.Top - p.Y : p.Y > s.Sheet.Bottom ? p.Y - s.Sheet.Bottom : 0)!;

    /// <summary>A text box's first part on its own page, which is what moves, resizes and snaps.</summary>
    static CoreRect FirstRect(PageLayoutViewModel m, TextItem text) =>
        m.FragmentsOf(text).FirstOrDefault() is { } f ? RectOf(f) : new CoreRect(text.PageX, text.PageY, text.PageW, TextFlow.EmptyHeightPt);

    /// <summary>Boxes of everything else on a page, for snapping to.</summary>
    static List<CoreRect> OthersOn(PageLayoutViewModel m, string pageId, string exceptId) =>
        m.Deck.ItemsOn(pageId).Where(e => e.Id != exceptId).Select(e => PageLayout.BoxOf(e, m.Titles))
            .Concat(m.Deck.TextsOn(pageId).Where(t => t.Id != exceptId).Select(t => FirstRect(m, t)))
            .ToList();

    /// <summary>What is under the pointer: text boxes first, since they are drawn over sections.</summary>
    (Slot Slot, string Id, bool IsText, bool IsFirstPart)? HitAt(Layout l, Point p, PageLayoutViewModel m)
    {
        foreach (var slot in l.Slots)
        {
            if (!slot.Sheet.Contains(p)) continue;
            var pt = ToPage(slot, p, m.Zoom);

            foreach (var text in m.Session.TextItems.AsEnumerable().Reverse())
            {
                var fragments = m.FragmentsOf(text);
                for (var i = 0; i < fragments.Count; i++)
                    if (fragments[i].PageIndex == slot.Index && RectOf(fragments[i]).Contains(pt))
                        return (slot, text.Id, true, i == 0);
            }

            var hit = m.Deck.ItemsOn(slot.Page.Id).LastOrDefault(e => PageLayout.BoxOf(e, m.Titles).Contains(pt));
            if (hit is not null) return (slot, hit.Id, false, true);
        }
        return null;
    }

    /// <summary>A resize handle of the selected box: a corner of a section, or the left or right side of a text box.</summary>
    (Slot Slot, string Corner)? HandleAt(Layout l, Point p, PageLayoutViewModel m)
    {
        if (m.SelectedId is not { } id || m.IsEditing) return null;

        Rect box;
        Slot? slot;
        IEnumerable<(string, Point)> handles;
        if (m.Deck.ItemById(id) is { } item)
        {
            slot = l.Slots.FirstOrDefault(s => s.Page.Id == item.PageId);
            if (slot is null) return null;
            box = Outside(ToScreen(slot, PageLayout.BoxOf(item, m.Titles), m.Zoom));
            handles = Corners(box);
        }
        else if (m.Deck.TextById(id) is { } text)
        {
            slot = l.Slots.FirstOrDefault(s => s.Page.Id == text.PageId);
            if (slot is null) return null;
            box = Outside(ToScreen(slot, FirstRect(m, text), m.Zoom));
            handles = Sides(box);
        }
        else return null;

        foreach (var (corner, c) in handles)
            if (Math.Abs(p.X - c.X) <= HandleSize && Math.Abs(p.Y - c.Y) <= HandleSize)
                return (slot, corner);
        return null;
    }

    static Rect Outside(Rect r) => new(r.X - Outset, r.Y - Outset, r.Width + 2 * Outset, r.Height + 2 * Outset);

    static void DrawHandle(DrawingContext dc, Point c) =>
        dc.DrawRoundedRectangle(Brushes.White, AccentPen, new Rect(c.X - HandleSize / 2, c.Y - HandleSize / 2, HandleSize, HandleSize), 2, 2);

    static IEnumerable<(string Corner, Point At)> Corners(Rect r) =>
    [
        ("nw", r.TopLeft), ("ne", r.TopRight), ("sw", r.BottomLeft), ("se", r.BottomRight),
    ];

    static IEnumerable<(string Side, Point At)> Sides(Rect r) =>
    [
        ("w", new Point(r.Left, r.Top + r.Height / 2)), ("e", new Point(r.Right, r.Top + r.Height / 2)),
    ];

    // ---------------------------------------------------------------- for the view

    /// <summary>Where a text box's top-left is on the board, for putting the editor over it.</summary>
    public Point? ScreenOrigin(TextItem text)
    {
        if (_model is not { } m) return null;
        var slot = Arrange(m).Slots.FirstOrDefault(s => s.Page.Id == text.PageId);
        return slot is null ? null : new Point(slot.Sheet.X + text.PageX * m.Zoom, slot.Sheet.Y + text.PageY * m.Zoom);
    }

    /// <summary>The page a new box should go on: the selected box's, or else the one nearest the middle of the view.</summary>
    public string? CurrentPageId()
    {
        if (_model is not { } m || m.Session.Pages.Count == 0) return null;
        if (m.SelectedId is { } id)
        {
            if (m.Deck.ItemById(id) is { } item) return item.PageId;
            if (m.Deck.TextById(id) is { } text) return text.PageId;
        }
        var middle = ActualHeight / 2;
        return Arrange(m).Slots.MinBy(s => Math.Abs((s.Sheet.Top + s.Sheet.Bottom) / 2 - middle))!.Page.Id;
    }

    /// <summary>Scrolls just enough to bring a text box's top into view.</summary>
    public void BringIntoView(TextItem text)
    {
        if (ScreenOrigin(text) is not { } origin) return;
        if (origin.Y < 20) _scrollY += origin.Y - 60;
        else if (origin.Y > ActualHeight - 80) _scrollY += origin.Y - ActualHeight + 160;
        InvalidateVisual();
    }

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
            DrawLabel(dc, slot);

            // a soft shadow lifts the paper off the ground
            var s0 = slot.Sheet;
            dc.DrawRoundedRectangle(ShadowFar, null, new Rect(s0.X - 3, s0.Y + 1, s0.Width + 6, s0.Height + 7), 5, 5);
            dc.DrawRoundedRectangle(ShadowNear, null, new Rect(s0.X - 1, s0.Y + 1, s0.Width + 2, s0.Height + 3), 3, 3);
            DrawSheet(dc, m, slot, z, l.Slots.Count, decorate: true);
            if (_scrollToPage == slot.Index) dc.DrawRectangle(null, AccentPen, Outside(slot.Sheet));

            dc.PushClip(new RectangleGeometry(slot.Sheet));
            DrawLanding(dc, m, slot);   // over everything, so it is seen through the box being dragged

            if (m.GuidePageId == slot.Page.Id && _dragId is not null)
                foreach (var g in m.Guides)
                {
                    if (g.Vertical)
                        dc.DrawLine(GuidePen, new Point(slot.Sheet.X + g.At * z, slot.Sheet.Top), new Point(slot.Sheet.X + g.At * z, slot.Sheet.Bottom));
                    else
                        dc.DrawLine(GuidePen, new Point(slot.Sheet.Left, slot.Sheet.Y + g.At * z), new Point(slot.Sheet.Right, slot.Sheet.Y + g.At * z));
                }
            dc.Pop();
        }

        // an empty page says how to fill it
        foreach (var slot in l.Slots)
        {
            var empty = !m.Deck.ItemsOn(slot.Page.Id).Any()
                        && !m.Session.TextItems.Any(t => m.FragmentsOf(t).Any(f => f.PageIndex == slot.Index))
                        && _landing?.PageId != slot.Page.Id;
            if (!empty || slot.Sheet.Bottom < 0 || slot.Sheet.Top > ActualHeight) continue;
            var s = slot.Sheet;
            var first = Text("This page is empty", Math.Max(10, 13 * z), Muted, weight: FontWeights.SemiBold);
            var second = Text("Drag a section here from the left, or use Insert", Math.Max(9, 11 * z), Muted, weight: FontWeights.Normal);
            var top = s.Y + s.Height / 2 - (first.Height + second.Height + 4) / 2;
            dc.DrawText(first, new Point(s.X + (s.Width - first.Width) / 2, top));
            dc.DrawText(second, new Point(s.X + (s.Width - second.Width) / 2, top + first.Height + 4));
        }

        dc.DrawRoundedRectangle(null, AddPen, l.AddButton, 9, 9);
        var plus = Text(Icons.Add, 16, Secondary, Fonts.Icons, FontWeights.Light);
        var add = Text("Add page", 12, Secondary, weight: FontWeights.SemiBold);
        var startX = l.AddButton.X + (l.AddButton.Width - plus.Width - 6 - add.Width) / 2;
        dc.DrawText(plus, new Point(startX, l.AddButton.Y + (l.AddButton.Height - plus.Height) / 2));
        dc.DrawText(add, new Point(startX + plus.Width + 6, l.AddButton.Y + (l.AddButton.Height - add.Height) / 2));

        DrawScrollThumb(dc, l);
        ViewMoved?.Invoke();
    }

    /// <summary>"Page 1 · Portrait" in small letters under the page.</summary>
    void DrawLabel(DrawingContext dc, Slot slot)
    {
        var label = Text($"Page {slot.Index + 1}  ·  {(slot.Page.Landscape ? "Landscape" : "Portrait")}", 11, Muted, weight: FontWeights.Medium);
        dc.DrawText(label, new Point(slot.Sheet.X + (slot.Sheet.Width - label.Width) / 2, slot.Sheet.Bottom + 12));
    }

    /// <summary>
    /// One page with everything on it. The board draws it with selection, hover and gliding; page
    /// thumbnails draw it plain, at their own scale, from the same code.
    /// </summary>
    void DrawSheet(DrawingContext dc, PageLayoutViewModel m, Slot slot, double z, int pageCount, bool decorate)
    {
        dc.DrawRectangle(m.Light ? LightSheet : DarkSheet, decorate ? SheetPen : null, slot.Sheet);
        if (decorate)
        {
            var inset = PageLayout.Margin * z;
            dc.DrawRectangle(null, m.Light ? LightMarginPen : DarkMarginPen,
                new Rect(slot.Sheet.X + inset, slot.Sheet.Y + inset,
                    Math.Max(0, slot.Sheet.Width - 2 * inset), Math.Max(0, slot.Sheet.Height - 2 * inset)));
        }

        dc.PushClip(new RectangleGeometry(slot.Sheet));
        DrawPageFurniture(dc, m, slot, z, pageCount);
        foreach (var item in m.Deck.ItemsOn(slot.Page.Id)) DrawItem(dc, m, slot, item, z, decorate);
        foreach (var text in m.Session.TextItems) DrawText(dc, m, slot, text, z, decorate);
        dc.Pop();
    }

    /// <summary>A small picture of one page, for the page list.</summary>
    public BitmapSource? RenderPageThumbnail(int index, double width)
    {
        if (_model is not { } m || index < 0 || index >= m.Session.Pages.Count) return null;
        var page = m.Session.Pages[index];
        var (pw, ph) = PageLayout.SizeOf(page);
        var z = width / pw;
        var dpi = Dpi;
        var slot = new Slot(page, index, new Rect(0, 0, pw * z, ph * z));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(dpi, dpi));
            DrawSheet(dc, m, slot, z, m.Session.Pages.Count, decorate: false);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(pw * z * dpi), (int)Math.Ceiling(ph * z * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The header line and page number printed on the page itself, as they will be in the PDF.</summary>
    void DrawPageFurniture(DrawingContext dc, PageLayoutViewModel m, Slot slot, double z, int pageCount)
    {
        var brush = m.Light ? Muted : HexBrush.FromHex("#9aa0a6");
        var size = Math.Max(1, 8.5 * z);   // the board shows z pixels per point
        if (m.Header)
        {
            var header = Text(PdfExporter.HeaderText(m.Session), size, brush, weight: FontWeights.Normal);
            header.MaxTextWidth = Math.Max(1, slot.Sheet.Width - 2 * PageLayout.Margin * z);
            header.MaxLineCount = 1;
            header.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(header, new Point(slot.Sheet.X + PageLayout.Margin * z, slot.Sheet.Y + PdfExporter.HeaderBaseline * z - header.Baseline));
        }
        if (m.PageNumbers)
        {
            var number = Text(PdfExporter.PageNumberText(slot.Index, pageCount), size, brush, weight: FontWeights.Normal);
            dc.DrawText(number, new Point(slot.Sheet.Right - PageLayout.Margin * z - number.Width,
                slot.Sheet.Bottom - PdfExporter.FooterBaseline * z - number.Baseline));
        }
    }

    /// <summary>While dragging: a dashed ghost where the box will land and a line marking the spot.</summary>
    void DrawLanding(DrawingContext dc, PageLayoutViewModel m, Slot slot)
    {
        if (_landing is not { } landing || landing.PageId != slot.Page.Id || _resizeCorner is not null) return;
        var ghost = ToScreen(slot, landing.Box, m.Zoom);
        dc.DrawRoundedRectangle(GhostFill, GhostPen, ghost, 3, 3);

        var y = ghost.Top - PageLayout.Gap / 2 * m.Zoom;
        dc.DrawLine(DropPen, new Point(ghost.Left, y), new Point(ghost.Right, y));
        dc.DrawEllipse(Accent, null, new Point(ghost.Left, y), 3.5, 3.5);
        dc.DrawEllipse(Accent, null, new Point(ghost.Right, y), 3.5, 3.5);
    }

    void DrawItem(DrawingContext dc, PageLayoutViewModel m, Slot slot, ExtractItem item, double z, bool decorate)
    {
        var glide = decorate ? GlideOffset(item.Id, slot.Index, item.PageX, item.PageY) : default;
        var dragging = decorate && _dragId == item.Id && _resizeCorner is null;
        var box = ToScreen(slot, PageLayout.BoxOf(item, m.Titles), z);
        box.Offset(glide.X * z, glide.Y * z);
        var picture = box;
        if (dragging) dc.PushOpacity(0.6);

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

        if (dragging) dc.Pop();
        if (!decorate) return;

        if (m.SelectedId == item.Id)
        {
            var frame = Outside(box);
            dc.DrawRoundedRectangle(null, AccentPen, frame, 2, 2);
            if (!dragging) foreach (var (_, c) in Corners(frame)) DrawHandle(dc, c);
        }
        else if (_hoverId == item.Id && _dragId is null)
        {
            dc.DrawRoundedRectangle(null, HoverPen, Outside(box), 2, 2);
        }
    }

    /// <summary>The parts of a text box that fall on this page, with a dashed line where it runs on to the next one.</summary>
    void DrawText(DrawingContext dc, PageLayoutViewModel m, Slot slot, TextItem text, double z, bool decorate)
    {
        if (decorate && m.EditingId == text.Id) return;   // the editor is showing it
        var selected = decorate && m.SelectedId == text.Id;

        if (decorate && _dragId == text.Id && _resizeCorner is null && _dragFragment is { } moving)
        {
            if (slot.Page.Id != text.PageId) return;
            var at = ToScreen(slot, new CoreRect(text.PageX, text.PageY, moving.W, moving.H), z);
            dc.PushOpacity(0.6);
            if (PictureOf(moving, z) is { } picture) dc.DrawImage(picture, at);
            dc.Pop();
            dc.DrawRoundedRectangle(null, AccentPen, Outside(at), 2, 2);
            return;
        }

        var fragments = m.FragmentsOf(text);
        var hovered = decorate && !selected && _hoverId == text.Id && _dragId is null;

        for (var i = 0; i < fragments.Count; i++)
        {
            var f = fragments[i];
            if (f.PageIndex != slot.Index) continue;
            var rect = ToScreen(slot, RectOf(f), z);
            if (i == 0 && decorate)
            {
                var glide = GlideOffset(text.Id, slot.Index, text.PageX, text.PageY);
                rect.Offset(glide.X * z, glide.Y * z);
            }

            if (PictureOf(f, z, cache: decorate) is { } picture) dc.DrawImage(picture, rect);
            if (i < fragments.Count - 1 && decorate)
                dc.DrawLine(m.Light ? RunOnLight : RunOnDark, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom));

            if (hovered) dc.DrawRoundedRectangle(null, HoverPen, Outside(rect), 2, 2);
            if (!selected) continue;
            var frame = Outside(rect);
            dc.DrawRoundedRectangle(null, AccentPen, frame, 2, 2);
            if (i != 0) continue;
            foreach (var (_, c) in Sides(frame)) DrawHandle(dc, c);
        }
    }

    /// <summary>A fragment drawn as a picture at this zoom and screen density, kept until the fragment is laid out again.</summary>
    BitmapSource? PictureOf(TextFragment f, double z, bool cache = true)
    {
        if (cache && _fragmentPictures.TryGetValue(f, out var cached) && cached.Item1 == z) return cached.Item2;

        // fragment visuals are in WPF units of the paper (4/3 per point); the board shows z pixels per point
        var scale = z * Dpi / DocLook.DipPerPoint;
        var width = (int)Math.Ceiling(DocLook.Dip(f.W) * scale);
        var height = (int)Math.Ceiling(DocLook.Dip(f.H) * scale);
        if (width <= 0 || height <= 0) return null;

        var host = new ContainerVisual();
        var transform = new Matrix();
        transform.Translate(0, -f.ShiftDip);
        transform.Scale(scale, scale);
        host.Transform = new MatrixTransform(transform);
        host.Children.Add(f.Page.Visual);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        host.Children.Clear();
        bitmap.Freeze();

        if (cache) _fragmentPictures.AddOrUpdate(f, Tuple.Create(z, (BitmapSource)bitmap));
        return bitmap;
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

        // a click anywhere on the board finishes typing in a text box; the editor itself is not part of the board
        if (m.IsEditing) m.FinishEditing();
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

        if (HandleAt(l, p, m) is { } handle)
        {
            _dragId = m.SelectedId;
            _resizeCorner = handle.Corner;
            _dragText = m.Deck.TextById(_dragId!) is not null;
            if (!_dragText && m.Deck.ItemById(_dragId!) is { } item)
            {
                var box = PageLayout.BoxOf(item, m.Titles);
                _anchor = handle.Corner switch
                {
                    "nw" => new CorePt(box.Right, box.Bottom),
                    "ne" => new CorePt(box.X, box.Bottom),
                    "sw" => new CorePt(box.Right, box.Y),
                    _ => new CorePt(box.X, box.Y),
                };
            }
            StartDrag(p);
            return;
        }

        if (HitAt(l, p, m) is { } hit)
        {
            m.Select(hit.Id);
            if (hit.IsText && e.ClickCount == 2)
            {
                m.BeginEdit(hit.Id);
                return;
            }
            // only a text box's first part drags; the parts after it follow wherever that goes
            if (hit.IsText && !hit.IsFirstPart) return;

            var pt = ToPage(hit.Slot, p, m.Zoom);
            var (x, y) = hit.IsText && m.Deck.TextById(hit.Id) is { } t ? (t.PageX, t.PageY)
                : m.Deck.ItemById(hit.Id) is { } it ? (it.PageX, it.PageY) : (0, 0);
            _dragId = hit.Id;
            _dragText = hit.IsText;
            _dragFragment = hit.IsText && m.Deck.TextById(hit.Id) is { } dragged ? m.FragmentsOf(dragged).FirstOrDefault() : null;
            _resizeCorner = null;
            _grab = new CorePt(pt.X - x, pt.Y - y);
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
        if (_dragId is null)
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
        var threshold = (Keyboard.Modifiers & ModifierKeys.Alt) == 0 ? SnapPixels / z : -1;

        if (_dragText && m.Deck.TextById(_dragId) is { } text)
        {
            if (_resizeCorner is { } side)
            {
                var slot = l.Slots.First(s => s.Page.Id == text.PageId);
                var (pw, _) = PageLayout.SizeOf(slot.Page);
                var r = Snapping.ResizeWidth(text.PageX, text.PageX + text.PageW, ToPage(slot, p, z).X, side == "e", pw,
                    OthersOn(m, slot.Page.Id, text.Id), threshold, PageLayout.MinTextWidth);
                m.Deck.PlaceText(text.Id, slot.Page.Id, r.X, text.PageY, r.W);
                m.ShowGuides(slot.Page.Id, r.Guides);
            }
            else
            {
                var slot = NearestSlot(l, p);
                var (pw, ph) = PageLayout.SizeOf(slot.Page);
                var pt = ToPage(slot, p, z);
                var height = _dragFragment?.H ?? TextFlow.EmptyHeightPt;
                var box = new CoreRect(pt.X - _grab.X, pt.Y - _grab.Y, text.PageW, height);
                var others = OthersOn(m, slot.Page.Id, text.Id);
                var r = Snapping.Move(box, pw, ph, others, threshold);
                m.Deck.PlaceText(text.Id, slot.Page.Id, r.Box.X, r.Box.Y, text.PageW);
                m.ShowGuides(slot.Page.Id, r.Guides);
                var placed = new CoreRect(text.PageX, text.PageY, text.PageW, height);
                _landing = (slot.Page.Id, placed with { Y = PageSettler.LandingTop(others, placed) });
            }
        }
        else if (m.Deck.ItemById(_dragId) is { } item)
        {
            if (_resizeCorner is { } corner)
            {
                var slot = l.Slots.First(s => s.Page.Id == item.PageId);
                var (pw, ph) = PageLayout.SizeOf(slot.Page);
                var r = Snapping.Resize(_anchor, ToPage(slot, p, z), corner, PageLayout.AspectOf(item),
                    m.Titles ? PageLayout.CaptionHeight : 0, pw, ph, OthersOn(m, slot.Page.Id, item.Id), threshold, PageLayout.MinWidth);
                m.Deck.Place(item.Id, slot.Page.Id, r.Box.X, r.Box.Y, r.Box.W);
                m.ShowGuides(slot.Page.Id, r.Guides);
            }
            else
            {
                var slot = NearestSlot(l, p);
                var (pw, ph) = PageLayout.SizeOf(slot.Page);
                var pt = ToPage(slot, p, z);
                var box = PageLayout.BoxOf(item, m.Titles) with { X = pt.X - _grab.X, Y = pt.Y - _grab.Y };
                var others = OthersOn(m, slot.Page.Id, item.Id);
                var r = Snapping.Move(box, pw, ph, others, threshold);
                m.Deck.Place(item.Id, slot.Page.Id, r.Box.X, r.Box.Y, item.PageW);
                m.ShowGuides(slot.Page.Id, r.Guides);
                var placed = PageLayout.BoxOf(item, m.Titles);
                _landing = (slot.Page.Id, placed with { Y = PageSettler.LandingTop(others, placed) });
            }
        }
        m.Redraw();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
        _panFrom = null;
        if (_model is not { } m || _dragId is null) return;

        var changed = _snapshotTaken;
        var moved = _dragId;
        _dragId = null;
        _dragFragment = null;
        _landing = null;
        _resizeCorner = null;
        m.ShowGuides(null, []);
        if (changed) m.Commit(moved);
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

    /// <summary>Wheel turns that reach the editor over a text box still scroll the pages.</summary>
    public void ScrollBy(double delta)
    {
        _scrollY -= delta * 0.6;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_model is not { } m) return;
        if (HandleKey(m, e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    /// <summary>The board's keys; public so the modifier keys can be given directly, not read from the keyboard.</summary>
    public bool HandleKey(PageLayoutViewModel m, Key key, ModifierKeys modifiers)
    {
        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        var step = shift ? 10 : 1;

        switch (key)
        {
            case Key.Delete or Key.Back: m.RemoveSelected(); break;
            case Key.Enter when m.SelectedId is { } id && m.Deck.TextById(id) is not null: m.BeginEdit(id); break;
            case Key.Escape: m.Select(null); break;
            case Key.Z when ctrl && shift: m.Redo(); break;
            case Key.Z when ctrl: m.Undo(); break;
            case Key.Y when ctrl: m.Redo(); break;
            case Key.D when ctrl: m.DuplicateSelected(); BringSelectedIntoView(); break;
            case Key.D0 or Key.NumPad0 when ctrl: FitWidth(); break;
            case Key.OemPlus or Key.Add when ctrl: m.ZoomIn(); break;
            case Key.OemMinus or Key.Subtract when ctrl: m.ZoomOut(); break;
            case Key.Left when m.SelectedId is not null: m.Nudge(-step, 0); break;
            case Key.Right when m.SelectedId is not null: m.Nudge(step, 0); break;
            case Key.Up when m.SelectedId is not null: m.Nudge(0, -step); BringSelectedIntoView(); break;
            case Key.Down when m.SelectedId is not null: m.Nudge(0, step); BringSelectedIntoView(); break;
            case Key.PageDown: ScrollToPage(CurrentPageIndex() + 1); break;
            case Key.PageUp: ScrollToPage(CurrentPageIndex() - 1); break;
            case Key.Tab: m.SelectNext(!shift); BringSelectedIntoView(); break;
            default: return false;
        }
        return true;
    }

    /// <summary>Zooms so the widest page fills the width of the view.</summary>
    public void FitWidth()
    {
        if (_model is not { } m || m.Session.Pages.Count == 0 || ActualWidth <= 0) return;
        var widest = m.Session.Pages.Max(p => PageLayout.SizeOf(p).W);
        m.Zoom = Math.Clamp(Math.Round((ActualWidth - 2 * SidePad) / widest, 2), PageLayoutViewModel.MinZoom, PageLayoutViewModel.MaxZoom);
        _scrollX = 0;
        InvalidateVisual();
    }

    /// <summary>The page whose middle is nearest the middle of the view.</summary>
    public int CurrentPageIndex()
    {
        if (_model is not { } m || m.Session.Pages.Count == 0) return 0;
        var middle = ActualHeight / 2;
        return Arrange(m).Slots.MinBy(s => Math.Abs((s.Sheet.Top + s.Sheet.Bottom) / 2 - middle))!.Index;
    }

    /// <summary>Scrolls so a page's top sits just under the top of the view.</summary>
    /// <param name="flash">Outline the page for a moment, so it is clear which one was jumped to.</param>
    public void ScrollToPage(int index, bool flash = false)
    {
        if (_model is not { } m || m.Session.Pages.Count == 0) return;
        var slots = Arrange(m).Slots;
        var slot = slots[Math.Clamp(index, 0, slots.Count - 1)];
        _scrollY += slot.Sheet.Top - TopPad;
        if (flash)
        {
            _scrollToPage = slot.Index;
            _flashEnds.Stop();
            _flashEnds.Start();
        }
        InvalidateVisual();
    }

    int _scrollToPage = -1;
    readonly System.Windows.Threading.DispatcherTimer _flashEnds = new() { Interval = TimeSpan.FromMilliseconds(700) };

    public bool IsDragging => _dragId is not null;

    /// <summary>The selected box on screen (its part on its own page), or null when nothing is selected or it is off screen.</summary>
    public Rect? SelectedScreenRect()
    {
        if (_model is not { } m || m.SelectedId is not { } id) return null;
        var slots = Arrange(m).Slots;
        if (m.Deck.ItemById(id) is { } item && slots.FirstOrDefault(s => s.Page.Id == item.PageId) is { } a)
            return ToScreen(a, PageLayout.BoxOf(item, m.Titles), m.Zoom);
        if (m.Deck.TextById(id) is { } text && slots.FirstOrDefault(s => s.Page.Id == text.PageId) is { } b)
            return ToScreen(b, FirstRect(m, text), m.Zoom);
        return null;
    }

    // ---------------------------------------------------------------- sections dragged in from the list

    /// <summary>Where a section dragged from the list would sit with its middle under the pointer.</summary>
    (Slot Slot, CoreRect Box)? DropSpot(PageLayoutViewModel m, ExtractItem item, Point p)
    {
        var l = Arrange(m);
        if (l.Slots.Count == 0) return null;
        var slot = NearestSlot(l, p);
        var (pw, ph) = PageLayout.SizeOf(slot.Page);
        var pt = ToPage(slot, p, m.Zoom);
        var width = Math.Min(item.PageW, pw - 2 * PageLayout.Margin);
        var height = PageLayout.BoxHeight(item, width, m.Titles);
        var x = Math.Clamp(pt.X - width / 2, 0, pw - width);
        var y = Math.Clamp(pt.Y - height / 2, 0, Math.Max(0, ph - height));
        return (slot, new CoreRect(x, y, width, height));
    }

    /// <summary>Shows where a section dragged from the list would land; <paramref name="p"/> is in board coordinates.</summary>
    public void PreviewSectionDrop(string itemId, Point p)
    {
        if (_model is not { } m || m.Deck.ItemById(itemId) is not { } item || DropSpot(m, item, p) is not { } spot) return;
        var others = OthersOn(m, spot.Slot.Page.Id, itemId);
        _landing = (spot.Slot.Page.Id, spot.Box with { Y = PageSettler.LandingTop(others, spot.Box) });
        InvalidateVisual();
    }

    public void ClearDropPreview()
    {
        if (_landing is null) return;
        _landing = null;
        InvalidateVisual();
    }

    /// <summary>Moves a section from the list to where it was dropped, as one undo step.</summary>
    public void DropSection(string itemId, Point p)
    {
        _landing = null;
        if (_model is not { } m || m.Deck.ItemById(itemId) is not { } item || DropSpot(m, item, p) is not { } spot) return;
        m.FinishEditing();
        m.Deck.Snapshot();
        m.Deck.Place(itemId, spot.Slot.Page.Id, spot.Box.X, spot.Box.Y, spot.Box.W);
        m.Select(itemId);
        m.Commit(itemId);
        BringSelectedIntoView();
    }

    /// <summary>Scrolls just enough to show the selected box.</summary>
    public void BringSelectedIntoView()
    {
        if (_model is not { } m || m.SelectedId is not { } id) return;
        var slots = Arrange(m).Slots;
        Rect box;
        if (m.Deck.ItemById(id) is { } item && slots.FirstOrDefault(s => s.Page.Id == item.PageId) is { } a)
            box = ToScreen(a, PageLayout.BoxOf(item, m.Titles), m.Zoom);
        else if (m.Deck.TextById(id) is { } text && slots.FirstOrDefault(s => s.Page.Id == text.PageId) is { } b)
            box = ToScreen(b, FirstRect(m, text), m.Zoom);
        else return;

        if (box.Top < 20) _scrollY += box.Top - 60;
        else if (box.Bottom > ActualHeight - 20) _scrollY += Math.Min(box.Top - 60, box.Bottom - ActualHeight + 60);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverId is null) return;
        _hoverId = null;
        InvalidateVisual();
    }

    void UpdateCursor(Layout l, Point p, PageLayoutViewModel m)
    {
        if (HandleAt(l, p, m) is { } h)
        {
            Cursor = h.Corner switch
            {
                "nw" or "se" => Cursors.SizeNWSE,
                "ne" or "sw" => Cursors.SizeNESW,
                _ => Cursors.SizeWE,
            };
            return;
        }
        if (l.AddButton.Contains(p))
        {
            Cursor = Cursors.Hand;
            return;
        }
        var hit = HitAt(l, p, m);
        Cursor = hit switch
        {
            { IsText: true, IsFirstPart: false } => Cursors.Arrow,
            { IsText: true } => Cursors.SizeAll,
            not null => Cursors.SizeAll,
            _ => Cursors.Arrow,
        };
        if (hit?.Id != _hoverId)
        {
            _hoverId = hit?.Id;
            InvalidateVisual();
        }
    }
}
