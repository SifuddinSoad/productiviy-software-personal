using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FocusLock.App.Board;
using FocusLock.App.Document;
using FocusLock.Core.Document;
using FocusLock.Core.Export;
using FocusLock.Core.Models;
using CoreRect = FocusLock.Core.Board.Rect;

namespace FocusLock.App.ViewModels;

/// <summary>
/// The Arrange pages screen: the session's A4 pages with sections and text boxes on them. Edits go
/// through <see cref="PageDeck"/>; <see cref="Export.PageBoard"/> draws the result and turns pointer
/// input into those edits; the view puts an editor over a text box while its words are being changed.
/// </summary>
public sealed partial class PageLayoutViewModel : ObservableObject
{
    public const double MinZoom = 0.5;
    public const double MaxZoom = 2.0;

    /// <summary>Longest side a picture is rendered at for the screen; enough for 200 % on an A4 page.</summary>
    const double PictureMaxPixels = 1700;

    readonly WhiteboardViewModel _owner;
    readonly Dictionary<string, BitmapSource?> _pictures = [];
    readonly Dictionary<string, (string Key, List<TextFragment> Fragments)> _flows = [];

    public PageDeck Deck { get; }
    public Session Session => Deck.Session;

    /// <summary>Anything that changes what the board shows.</summary>
    public event Action? Changed;

    [ObservableProperty] double _zoom = 0.8;

    public string? SelectedId { get; private set; }

    /// <summary>Snap guides for what is being dragged, on the page it is over.</summary>
    public IReadOnlyList<Guide> Guides { get; private set; } = [];
    public string? GuidePageId { get; private set; }

    public TextFormatState Format { get; } = new();

    public PageLayoutViewModel(WhiteboardViewModel owner)
    {
        _owner = owner;
        PageLayout.Complete(owner.Session);
        Deck = new PageDeck(owner.Session);
        Settle(null);   // layouts from before settling may overlap
        RebuildSections();
        _owner.PropertyChanged += OnOwnerChanged;
    }

    /// <summary>Stops listening to the whiteboard once the screen closes.</summary>
    public void Detach() => _owner.PropertyChanged -= OnOwnerChanged;

    void OnOwnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WhiteboardViewModel.LastExport)) OnPropertyChanged(nameof(LastExport));
    }

    public bool Light => Session.PdfLight;
    public bool Titles => Session.PdfTitles;
    public bool Header => Session.PdfHeader;
    public string HeaderText => Session.PdfHeaderText;
    public string HeaderPlaceholder => Session.Name;
    public bool PageNumbers => Session.PdfPageNumbers;
    public bool CanUndo => Deck.CanUndo;
    public bool CanRedo => Deck.CanRedo;
    public bool CanExport => _owner.CanExport;
    public string LastExport => _owner.LastExport;
    public string ZoomLabel => $"{Math.Round(Zoom * 100)}%";
    public string PageCountLabel => Session.Pages.Count == 1 ? "1 page" : $"{Session.Pages.Count} pages";

    partial void OnZoomChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomLabel));
        Changed?.Invoke();
    }

    public void ZoomBy(double factor) => Zoom = Math.Clamp(Math.Round(Zoom * factor, 2), MinZoom, MaxZoom);
    public void ZoomIn() => ZoomBy(1.25);
    public void ZoomOut() => ZoomBy(0.8);
    public void ZoomReset() => Zoom = 1;

    /// <summary>The region as it looks on its canvas now, or null when its plan is gone.</summary>
    public BitmapSource? PictureFor(ExtractItem item)
    {
        if (_pictures.TryGetValue(item.Id, out var cached)) return cached;

        var plan = Session.Plans.FirstOrDefault(p => p.Id == item.PlanId);
        BitmapSource? picture = null;
        if (plan is not null && item.W > 0 && item.H > 0)
        {
            var scale = Math.Min(2, PictureMaxPixels / Math.Max(item.W, item.H));
            picture = RegionRenderer.Render(plan.Doc, new CoreRect(item.X, item.Y, item.W, item.H), scale);
        }
        _pictures[item.Id] = picture;
        return picture;
    }

    /// <summary>
    /// A text box laid out down the pages. Laying out is kept until something it depends on changes:
    /// where the box is, its words (a new blocks list), the pages, or the header and page numbers.
    /// </summary>
    public List<TextFragment> FragmentsOf(TextItem text)
    {
        var key = string.Join("|",
            text.PageId, text.PageX, text.PageY, text.PageW, RuntimeHelpers.GetHashCode(text.Blocks),
            Session.PdfLight, Session.PdfHeader, Session.PdfPageNumbers,
            string.Join(",", Session.Pages.Select(p => p.Id + (p.Landscape ? "L" : "P"))));
        if (_flows.TryGetValue(text.Id, out var cached) && cached.Key == key) return cached.Fragments;

        var fragments = TextFlow.Layout(Session, text);
        _flows[text.Id] = (key, fragments);
        return fragments;
    }

    public void Select(string? id)
    {
        if (SelectedId == id) return;
        SelectedId = id;
        RaiseSelection();
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- what the side panel shows

    public bool HasSelection => SelectedId is { } id && (Deck.ItemById(id) is not null || Deck.TextById(id) is not null);
    public bool IsSectionSelected => SelectedId is { } id && Deck.ItemById(id) is not null;
    public bool IsTextSelected => SelectedId is { } id && Deck.TextById(id) is not null;

    /// <summary>Nothing is selected and nothing is being typed: the panel shows the page and document.</summary>
    public bool ShowsPageSettings => !HasSelection && !IsEditing;
    public bool ShowsSelection => HasSelection && !IsEditing;

    public string SelectionTitle => IsSectionSelected ? "SECTION" : "TEXT";

    public string SelectedName => SelectedId is { } id
        ? Deck.ItemById(id)?.Name is { Length: > 0 } name ? name
        : Deck.TextById(id) is { } text ? TextPreview(text) : ""
        : "";

    static string TextPreview(TextItem text)
    {
        var words = string.Concat(text.Blocks.OfType<ParagraphBlock>().SelectMany(p => p.Runs).Select(r => r.Text)).Trim();
        if (words.Length == 0) words = text.Blocks.FirstOrDefault() switch
        {
            TableBlock => "Table",
            CalloutBlock => "Callout",
            DividerBlock => "Line",
            _ => "Text",
        };
        return words.Length > 40 ? words[..40] + "…" : words;
    }

    void RaiseSelection()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSectionSelected));
        OnPropertyChanged(nameof(IsTextSelected));
        OnPropertyChanged(nameof(ShowsPageSettings));
        OnPropertyChanged(nameof(ShowsSelection));
        OnPropertyChanged(nameof(SelectionTitle));
        OnPropertyChanged(nameof(SelectedName));
    }

    /// <summary>The page in the middle of the view, which page settings apply to; the view keeps it up to date.</summary>
    public int CurrentPageIndex
    {
        get => Math.Clamp(_currentPage, 0, Math.Max(0, Session.Pages.Count - 1));
        set
        {
            if (_currentPage == value) return;
            _currentPage = value;
            RaisePageStatus();
        }
    }
    int _currentPage;

    public PdfPage? CurrentPage => Session.Pages.Count == 0 ? null : Session.Pages[CurrentPageIndex];
    public bool CurrentLandscape => CurrentPage?.Landscape == true;
    public bool CanDeletePage => Session.Pages.Count > 1;
    public string CurrentPageTitle => $"PAGE {CurrentPageIndex + 1}";

    public string StatusLabel
    {
        get
        {
            var items = Session.Extracts.Count + Session.TextItems.Count;
            return $"Page {CurrentPageIndex + 1} of {Session.Pages.Count}  ·  {items} item{(items == 1 ? "" : "s")}";
        }
    }

    void RaisePageStatus()
    {
        OnPropertyChanged(nameof(CurrentPageIndex));
        OnPropertyChanged(nameof(CurrentLandscape));
        OnPropertyChanged(nameof(CanDeletePage));
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(StatusLabel));
    }

    public void SetCurrentLandscape(bool landscape)
    {
        if (CurrentPage is not { } page || page.Landscape == landscape) return;
        FinishEditing();
        Deck.SetLandscape(page.Id, landscape);
        Commit();
    }

    public void DeleteCurrentPage()
    {
        if (CurrentPage is { } page) DeletePage(page);
    }

    public void SetWidthFraction(double fraction)
    {
        if (SelectedId is not { } id || IsEditing) return;
        Deck.SetWidthFraction(id, fraction);
        Commit(id);
    }

    public void AlignSelected(string align)
    {
        if (SelectedId is not { } id || IsEditing) return;
        Deck.AlignOnPage(id, align);
        Commit(id);
    }

    public void MovePage(int from, int to)
    {
        FinishEditing();
        if (!Deck.MovePage(from, to)) return;
        CurrentPageIndex = to;
        Commit();
    }

    public void EditSelected()
    {
        if (SelectedId is { } id && Deck.TextById(id) is not null) BeginEdit(id);
    }

    /// <summary>Back to the canvas with the Extract tool ready, to pick another region.</summary>
    public void PickRegion()
    {
        _owner.CloseArrange();
        _owner.Panel = "extract";
        _owner.StartExtractTool();
    }

    /// <summary>Every extract, for the list the sections are dragged in from.</summary>
    public System.Collections.ObjectModel.ObservableCollection<SectionCard> Sections { get; } = [];

    void RebuildSections()
    {
        var pages = Session.Pages.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var cards = Session.Extracts.Select(e => new SectionCard(e.Id, e.Name, PictureFor(e),
            pages.TryGetValue(e.PageId, out var i) ? $"Page {i + 1}" : "")).ToList();
        if (cards.SequenceEqual(Sections)) return;
        Sections.Clear();
        foreach (var card in cards) Sections.Add(card);
    }

    /// <summary>Lit for a moment after each save.</summary>
    [ObservableProperty] bool _justSaved;

    readonly System.Windows.Threading.DispatcherTimer _savedFades = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    void FlashSaved()
    {
        JustSaved = true;
        _savedFades.Stop();
        _savedFades.Tick -= FadeSaved;
        _savedFades.Tick += FadeSaved;
        _savedFades.Start();
    }

    void FadeSaved(object? sender, EventArgs e)
    {
        _savedFades.Stop();
        JustSaved = false;
    }

    public string SessionTitle => $"{Session.Name} · PDF";

    public void ShowGuides(string? pageId, IReadOnlyList<Guide> guides)
    {
        GuidePageId = pageId;
        Guides = guides;
    }

    public void SetLight(bool light) { FinishEditing(); Deck.SetLight(light); Commit(); }
    public void ToggleTitles() { Deck.SetTitles(!Session.PdfTitles); Commit(); }
    public void ToggleHeader() { Deck.SetHeader(!Session.PdfHeader); Commit(); }
    public void SetHeaderText(string text) { Deck.SetHeaderText(text); Commit(); }
    public void TogglePageNumbers() { Deck.SetPageNumbers(!Session.PdfPageNumbers); Commit(); }
    public void AddPage() { Deck.AddPage(); Commit(); }
    public void ToggleLandscape(PdfPage page) { Deck.SetLandscape(page.Id, !page.Landscape); Commit(); }

    public void DeletePage(PdfPage page)
    {
        FinishEditing();
        if (Deck.DeletePage(page.Id)) Commit();
    }

    public void RemoveSelected()
    {
        if (SelectedId is not { } id) return;
        if (Deck.TextById(id) is not null) Deck.RemoveText(id);
        else Deck.Remove(id);
        SelectedId = null;
        Commit();
    }

    public void Undo() { FinishEditing(); Deck.Undo(); DropStaleSelection(); Commit(); }
    public void Redo() { FinishEditing(); Deck.Redo(); DropStaleSelection(); Commit(); }

    void DropStaleSelection()
    {
        if (SelectedId is { } id && Deck.ItemById(id) is null && Deck.TextById(id) is null) SelectedId = null;
    }

    public void Export() => _owner.ExportPdf();
    public void Close() => _owner.CloseArrange();

    // ---------------------------------------------------------------- text boxes

    public static class NewText
    {
        public const string Text = "text";
        public const string Heading = "heading";
        public const string Table = "table";
        public const string Callout = "callout";
        public const string Line = "line";
    }

    /// <summary>The text box whose words are open in the editor.</summary>
    public string? EditingId { get; private set; }

    public bool IsEditing => EditingId is not null;

    /// <summary>Asks the view to open the editor over a text box.</summary>
    public event Action<TextItem, string>? EditStarted;

    /// <summary>Asks the view for the editor's words so the edit can be finished.</summary>
    public event Func<List<DocBlock>?>? EditFinishing;

    /// <summary>Tells the view to take the editor away.</summary>
    public event Action? EditEnded;

    /// <summary>
    /// Adds a box of the given kind and opens it for typing unless it is a line. It goes straight
    /// under the selected box, pushing what follows down; with nothing selected, under everything on
    /// the page.
    /// </summary>
    public void AddText(string kind, string pageId)
    {
        FinishEditing();

        double below;
        PdfPage? page;
        if (SelectedId is { } id && Deck.ItemById(id) is { } item)
        {
            page = Deck.PageById(item.PageId);
            below = PageLayout.BoxOf(item, Session.PdfTitles).Bottom + PageLayout.Gap;
        }
        else if (SelectedId is { } tid && Deck.TextById(tid) is { } selected)
        {
            page = Deck.PageById(selected.PageId);
            var first = FragmentsOf(selected).FirstOrDefault();
            below = (first is null ? selected.PageY + TextFlow.EmptyHeightPt : first.Y + first.H) + PageLayout.Gap;
        }
        else
        {
            page = Deck.PageById(pageId);
            if (page is null) return;
            var top = PageLayout.TextArea(Session, page).Top;
            below = Deck.ItemsOn(page.Id).Select(e => PageLayout.BoxOf(e, Session.PdfTitles).Bottom)
                .Concat(Deck.TextsOn(page.Id).Select(t => FragmentsOf(t).FirstOrDefault() is { } f ? f.Y + f.H : t.PageY))
                .DefaultIfEmpty(top - PageLayout.Gap).Max() + PageLayout.Gap;
        }
        if (page is null) return;
        var (pw, _) = PageLayout.SizeOf(page);

        List<DocBlock> blocks = kind switch
        {
            NewText.Table => [new TableBlock { Rows = [.. Enumerable.Range(0, 3).Select(_ => Enumerable.Range(0, 3).Select(_ => new TableCell()).ToList())] }],
            NewText.Callout => [new CalloutBlock { Tone = CalloutTone.Note, Paragraphs = [new ParagraphBlock()] }],
            NewText.Line => [new DividerBlock()],
            NewText.Heading => [new ParagraphBlock { Style = DocStyle.H1 }],
            _ => [new ParagraphBlock()],
        };
        var text = new TextItem { Id = Core.Ids.New("t"), PageId = page.Id, PageX = PageLayout.Margin, PageY = below, PageW = pw - 2 * PageLayout.Margin, Blocks = blocks };
        Deck.Snapshot();
        Session.TextItems.Add(text);   // not clamped: it may start past the bottom, and settling moves it to the next page
        SelectedId = text.Id;
        Commit(text.Id);
        if (kind != NewText.Line) BeginEdit(text.Id, kind);
    }

    public void BeginEdit(string textId, string startAt = NewText.Text)
    {
        if (EditingId == textId) return;
        FinishEditing();
        if (Deck.TextById(textId) is not { } text) return;
        Deck.Snapshot();   // the whole edit undoes in one step
        EditingId = textId;
        SelectedId = textId;
        OnPropertyChanged(nameof(IsEditing));
        RaiseSelection();
        EditStarted?.Invoke(text, startAt);
        Changed?.Invoke();
    }

    /// <summary>Takes the editor's words into the box and closes the editor. A box left with nothing in it goes.</summary>
    public void FinishEditing()
    {
        if (EditingId is not { } id) return;
        var blocks = EditFinishing?.Invoke();
        EditingId = null;
        OnPropertyChanged(nameof(IsEditing));
        RaiseSelection();
        EditEnded?.Invoke();

        if (Deck.TextById(id) is { } text && blocks is not null)
        {
            if (DocOps.IsBlank(blocks))
            {
                Session.TextItems.Remove(text);
                if (SelectedId == id) SelectedId = null;
            }
            else if (JsonSerializer.Serialize(blocks) == JsonSerializer.Serialize(text.Blocks))
            {
                Deck.DiscardSnapshot();   // opened and closed without a change: nothing to undo
            }
            else
            {
                Deck.SetText(id, blocks);
            }
        }
        Commit(id);
    }

    /// <summary>Where the text box being edited sits, for placing the editor over it.</summary>
    public TextItem? EditingText => EditingId is { } id ? Deck.TextById(id) : null;

    /// <summary>Redraw only; for every step of a drag.</summary>
    public void Redraw() => Changed?.Invoke();

    /// <summary>
    /// Moves things so nothing overlaps (see <see cref="PageSettler"/>), adding pages that text or
    /// pushed-down things now need. Part of whatever change came before it, so the same undo step.
    /// </summary>
    void Settle(string? movedId) =>
        PageSettler.Settle(Session, Measure, movedId);

    TextMeasure Measure(TextItem t) => TextFlow.MeasureOf(FragmentsOf(t));

    // ---------------------------------------------------------------- positions, for animating

    /// <summary>Where a section or text box sits: its page's index and its top-left, in points.</summary>
    public readonly record struct Spot(int Page, double X, double Y);

    public Dictionary<string, Spot> Spots()
    {
        var index = Session.Pages.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var spots = new Dictionary<string, Spot>();
        foreach (var e in Session.Extracts)
            if (index.TryGetValue(e.PageId, out var i)) spots[e.Id] = new Spot(i, e.PageX, e.PageY);
        foreach (var t in Session.TextItems)
            if (index.TryGetValue(t.PageId, out var i)) spots[t.Id] = new Spot(i, t.PageX, t.PageY);
        return spots;
    }

    /// <summary>
    /// Things a change moved along their page, with where each was before, so the board can glide
    /// them there instead of jumping. Things that changed page are left out: they simply appear.
    /// </summary>
    public event Action<IReadOnlyDictionary<string, Spot>>? Moved;

    // ---------------------------------------------------------------- tidy, nudge, duplicate, tab

    /// <summary>Closes the gaps on every page (see <see cref="PageSettler.Compact"/>), as one undo step.</summary>
    public void Tidy()
    {
        FinishEditing();
        var before = Spots();
        var pages = Session.Pages.Count;
        Deck.Snapshot();
        PageSettler.Compact(Session, Measure);
        Settle(null);
        if (Session.Pages.Count == pages && Spots().All(s => before.TryGetValue(s.Key, out var b) && b == s.Value))
        {
            Deck.DiscardSnapshot();   // nothing to close
            return;
        }
        Commit(null, before);
    }

    readonly System.Windows.Threading.DispatcherTimer _nudgeEnds = new() { Interval = TimeSpan.FromMilliseconds(500) };
    bool _nudging;

    /// <summary>Moves the selected box by a few points. Presses close together make one undo step.</summary>
    public void Nudge(double dx, double dy)
    {
        if (SelectedId is not { } id || IsEditing) return;
        if (!_nudging)
        {
            Deck.Snapshot();
            _nudging = true;
            _nudgeEnds.Tick -= EndNudge;
            _nudgeEnds.Tick += EndNudge;
        }
        _nudgeEnds.Stop();
        _nudgeEnds.Start();

        if (Deck.ItemById(id) is { } item) Deck.Place(id, item.PageId, item.PageX + dx, item.PageY + dy, item.PageW);
        else if (Deck.TextById(id) is { } text) Deck.PlaceText(id, text.PageId, text.PageX + dx, text.PageY + dy, text.PageW);
        Commit(id);
    }

    void EndNudge(object? sender, EventArgs e)
    {
        _nudgeEnds.Stop();
        _nudging = false;
    }

    public void DuplicateSelected()
    {
        if (SelectedId is not { } id || IsEditing) return;
        var height = Deck.TextById(id) is { } text ? TextFlow.MeasureOf(FragmentsOf(text)).FirstHeight : 0;
        if (Deck.Duplicate(id, height) is not { } copy) return;
        SelectedId = copy;
        Commit(copy);
    }

    /// <summary>Every section and text box, page by page, top to bottom.</summary>
    public List<string> ReadingOrder()
    {
        var order = new List<string>();
        foreach (var page in Session.Pages)
        {
            order.AddRange(Deck.ItemsOn(page.Id).Select(e => (e.Id, Y: PageLayout.BoxOf(e, Titles).Y, X: e.PageX))
                .Concat(Deck.TextsOn(page.Id).Select(t => (t.Id, Y: t.PageY, X: t.PageX)))
                .OrderBy(x => x.Y).ThenBy(x => x.X)
                .Select(x => x.Id));
        }
        return order;
    }

    /// <summary>Selects the next (or previous) box in reading order, wrapping round; returns it.</summary>
    public string? SelectNext(bool forward)
    {
        var order = ReadingOrder();
        if (order.Count == 0) return null;
        var at = SelectedId is { } id ? order.IndexOf(id) : -1;
        var next = at < 0 ? (forward ? 0 : order.Count - 1) : (at + (forward ? 1 : -1) + order.Count) % order.Count;
        Select(order[next]);
        return order[next];
    }

    /// <summary>A finished edit: settled, saved with the session, and every label brought up to date.</summary>
    /// <param name="movedId">What the user just moved, added or typed in; it keeps its place when it ties with something.</param>
    /// <param name="from">Where things were before the change, for gliding them; taken now when not given.</param>
    public void Commit(string? movedId = null, IReadOnlyDictionary<string, Spot>? from = null)
    {
        from ??= Spots();
        Settle(movedId);
        var moves = new Dictionary<string, Spot>();
        foreach (var (key, now) in Spots())
            if (from.TryGetValue(key, out var was) && was.Page == now.Page && (Math.Abs(was.X - now.X) > 0.5 || Math.Abs(was.Y - now.Y) > 0.5))
                moves[key] = was;
        if (moves.Count > 0) Moved?.Invoke(moves);

        _owner.PersistLayout();
        FlashSaved();
        _owner.RaiseCanExport();
        OnPropertyChanged(nameof(Light));
        OnPropertyChanged(nameof(Titles));
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(PageNumbers));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(PageCountLabel));
        DropStaleSelection();
        RaiseSelection();
        RaisePageStatus();
        RebuildSections();
        Changed?.Invoke();
    }
}

/// <summary>An extract in the list sections are dragged in from.</summary>
public sealed record SectionCard(string Id, string Name, BitmapSource? Picture, string PageLabel);

/// <summary>A page in the page list.</summary>
public sealed record PageThumb(int Index, string Label, BitmapSource? Picture, bool Current);

/// <summary>Where the caret is in the text being edited, so the formatting bar can show what is on.</summary>
public sealed partial class TextFormatState : ObservableObject
{
    public IReadOnlyList<string> TextColors { get; } = DocLook.TextColors;
    public IReadOnlyList<string> Highlights { get; } = DocLook.Highlights;

    [ObservableProperty] string _currentStyle = DocStyle.Normal;
    [ObservableProperty] string _currentList = DocList.None;
    [ObservableProperty] TextAlignment _currentAlignment = TextAlignment.Left;
    [ObservableProperty] bool _isBold;
    [ObservableProperty] bool _isItalic;
    [ObservableProperty] bool _isUnderline;
    [ObservableProperty] string _sizeLabel = "11";
    [ObservableProperty] bool _inTable;

    public void Follow(DocumentEditor editor)
    {
        CurrentStyle = editor.CurrentStyle;
        CurrentList = editor.CurrentList;
        CurrentAlignment = editor.CurrentAlignment;
        IsBold = editor.IsBold;
        IsItalic = editor.IsItalic;
        IsUnderline = editor.IsUnderline;
        SizeLabel = editor.CurrentSizePt is { } size ? size.ToString("0.#") : "–";
        InTable = editor.CurrentCell is not null;
    }
}
