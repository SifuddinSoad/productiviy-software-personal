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
        Changed?.Invoke();
    }

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
        PageSettler.Settle(Session, t => TextFlow.MeasureOf(FragmentsOf(t)), movedId);

    /// <summary>A finished edit: settled, saved with the session, and every label brought up to date.</summary>
    /// <param name="movedId">What the user just moved, added or typed in; it keeps its place when it ties with something.</param>
    public void Commit(string? movedId = null)
    {
        Settle(movedId);
        _owner.PersistLayout();
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
        Changed?.Invoke();
    }
}

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
