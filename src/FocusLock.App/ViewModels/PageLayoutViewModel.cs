using System.ComponentModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FocusLock.App.Board;
using FocusLock.Core.Export;
using FocusLock.Core.Models;
using CoreRect = FocusLock.Core.Board.Rect;

namespace FocusLock.App.ViewModels;

/// <summary>
/// The Arrange pages screen: the session's A4 pages and where each extract sits on them. Edits go
/// through <see cref="PageDeck"/>; <see cref="Export.PageBoard"/> draws the result and turns pointer
/// input into those edits.
/// </summary>
public sealed partial class PageLayoutViewModel : ObservableObject
{
    public const double MinZoom = 0.5;
    public const double MaxZoom = 2.0;

    /// <summary>Longest side a picture is rendered at for the screen; enough for 200 % on an A4 page.</summary>
    const double PictureMaxPixels = 1700;

    readonly WhiteboardViewModel _owner;
    readonly Dictionary<string, BitmapSource?> _pictures = [];

    public PageDeck Deck { get; }
    public Session Session => Deck.Session;

    /// <summary>Anything that changes what the board shows.</summary>
    public event Action? Changed;

    [ObservableProperty] double _zoom = 0.8;

    public string? SelectedId { get; private set; }

    /// <summary>Snap guides for the section being dragged, on the page it is over.</summary>
    public IReadOnlyList<Guide> Guides { get; private set; } = [];
    public string? GuidePageId { get; private set; }

    public PageLayoutViewModel(WhiteboardViewModel owner)
    {
        _owner = owner;
        PageLayout.Complete(owner.Session);
        Deck = new PageDeck(owner.Session);
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
    public bool CanUndo => Deck.CanUndo;
    public bool CanRedo => Deck.CanRedo;
    public bool HasExtracts => Session.Extracts.Count > 0;
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

    public void SetLight(bool light) { Deck.SetLight(light); Commit(); }
    public void ToggleTitles() { Deck.SetTitles(!Session.PdfTitles); Commit(); }
    public void AddPage() { Deck.AddPage(); Commit(); }
    public void ToggleLandscape(PdfPage page) { Deck.SetLandscape(page.Id, !page.Landscape); Commit(); }

    public void DeletePage(PdfPage page)
    {
        if (Deck.DeletePage(page.Id)) Commit();
    }

    public void RemoveSelected()
    {
        if (SelectedId is not { } id) return;
        Deck.Remove(id);
        SelectedId = null;
        Commit();
    }

    public void Undo() { Deck.Undo(); DropStaleSelection(); Commit(); }
    public void Redo() { Deck.Redo(); DropStaleSelection(); Commit(); }

    void DropStaleSelection()
    {
        if (SelectedId is { } id && Deck.ItemById(id) is null) SelectedId = null;
    }

    public void Export() => _owner.ExportPdf();
    public void Close() => _owner.CloseArrange();

    /// <summary>Redraw only; for every step of a drag.</summary>
    public void Redraw() => Changed?.Invoke();

    /// <summary>A finished edit: saved with the session, and every label brought up to date.</summary>
    public void Commit()
    {
        _owner.PersistLayout();
        OnPropertyChanged(nameof(Light));
        OnPropertyChanged(nameof(Titles));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(HasExtracts));
        OnPropertyChanged(nameof(PageCountLabel));
        Changed?.Invoke();
    }
}
