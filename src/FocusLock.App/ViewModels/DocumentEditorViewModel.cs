using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FocusLock.App.Document;
using FocusLock.Core.Document;
using FocusLock.Core.Models;

namespace FocusLock.App.ViewModels;

/// <summary>
/// State for the document editor's toolbar and side panel. The editing itself happens in
/// <see cref="DocumentEditor"/>, which the view owns; this mirrors where the caret is so buttons can
/// light up, and holds page setup, preview pages and saving.
/// </summary>
public sealed partial class DocumentEditorViewModel : ObservableObject
{
    readonly WhiteboardViewModel _owner;

    public Session Session => _owner.Session;
    public DocModel Model => Session.Document;

    /// <summary>The view writes the editor's content into the model when asked.</summary>
    public event Action? FlushRequested;

    /// <summary>Page setup changed in a way that needs the editor rebuilt.</summary>
    public event Action? LayoutChanged;

    public DocumentEditorViewModel(WhiteboardViewModel owner)
    {
        _owner = owner;
        DocOps.Seed(owner.Session);
        foreach (var p in Papers) p.Active = p.Hex == Model.Paper;
    }

    public SectionSource? SectionFor(string extractId) => _owner.SectionSourceFor(extractId, print: false);

    public string SessionName => Session.Name;

    public ObservableCollection<ExtractCard> Extracts => _owner.Extracts;

    public IReadOnlyList<string> TextColors { get; } = DocLook.TextColors;
    public IReadOnlyList<string> Highlights { get; } = DocLook.Highlights;
    public IReadOnlyList<PaperChoice> Papers { get; } = DocLook.Papers.Select(p => new PaperChoice(p.Name, p.Hex)).ToList();

    // ---------------------------------------------------------------- caret state

    [ObservableProperty] string _currentStyle = DocStyle.Normal;
    [ObservableProperty] string _currentList = DocList.None;
    [ObservableProperty] TextAlignment _currentAlignment = TextAlignment.Left;
    [ObservableProperty] bool _isBold;
    [ObservableProperty] bool _isItalic;
    [ObservableProperty] bool _isUnderline;
    [ObservableProperty] string _sizeLabel = "11";
    [ObservableProperty] bool _inTable;
    [ObservableProperty] bool _hasSection;
    [ObservableProperty] string _sectionSize = Core.Document.SectionSize.Full;
    [ObservableProperty] string _sectionAlign = DocAlign.Center;
    [ObservableProperty] bool _sectionCaption = true;

    public void Follow(DocumentEditor editor)
    {
        CurrentStyle = editor.CurrentStyle;
        CurrentList = editor.CurrentList;
        CurrentAlignment = editor.CurrentAlignment;
        IsBold = editor.IsBold;
        IsItalic = editor.IsItalic;
        IsUnderline = editor.IsUnderline;
        SizeLabel = editor.CurrentSizePt is { } size ? size.ToString("0.#") : "–";
        InTable = editor.CurrentCell is not null && editor.SelectedSection is null;
        var section = editor.SelectedSectionSettings;
        HasSection = section is not null;
        if (section is not null)
        {
            SectionSize = section.Size;
            SectionAlign = section.Align;
            SectionCaption = section.Caption;
        }
    }

    // ---------------------------------------------------------------- insert table

    [ObservableProperty] int _tableRows = 3;
    [ObservableProperty] int _tableColumns = 3;

    public void StepRows(int by) => TableRows = Math.Clamp(TableRows + by, 1, 30);
    public void StepColumns(int by) => TableColumns = Math.Clamp(TableColumns + by, 1, 8);

    // ---------------------------------------------------------------- page setup

    public bool Landscape => Model.Landscape;
    public string Paper => Model.Paper;
    public bool ShowHeader => Model.ShowHeader;
    public bool ShowPageNumbers => Model.ShowPageNumbers;
    public string HeaderText => Model.HeaderText;

    public void SetLandscape(bool landscape)
    {
        if (Model.Landscape == landscape) return;
        Flush();
        Model.Landscape = landscape;
        OnPropertyChanged(nameof(Landscape));
        LayoutChanged?.Invoke();
        Save();
    }

    public void SetPaper(string hex)
    {
        if (Model.Paper == hex) return;
        Flush();
        Model.Paper = hex;
        foreach (var p in Papers) p.Active = p.Hex == hex;
        OnPropertyChanged(nameof(Paper));
        LayoutChanged?.Invoke();
        Save();
    }

    public void SetShowHeader(bool on) { Model.ShowHeader = on; OnPropertyChanged(nameof(ShowHeader)); Save(); }
    public void SetShowPageNumbers(bool on) { Model.ShowPageNumbers = on; OnPropertyChanged(nameof(ShowPageNumbers)); Save(); }
    public void SetHeaderText(string text) { Model.HeaderText = text; Save(); }

    // ---------------------------------------------------------------- preview

    [ObservableProperty] bool _isPreview;
    public ObservableCollection<ImageSource> PreviewPages { get; } = [];
    [ObservableProperty] string _pageCountLabel = "";

    public void ShowPreview(bool on)
    {
        if (on)
        {
            Flush();
            PreviewPages.Clear();
            var pager = DocumentPager.Paginate(Model, SectionFor, Session.Name);
            for (var i = 0; i < pager.PageCount; i++) PreviewPages.Add(PagePicture(pager.GetPage(i)));
            PageCountLabel = pager.PageCount == 1 ? "1 page" : $"{pager.PageCount} pages";
        }
        IsPreview = on;
    }

    static ImageSource PagePicture(System.Windows.Documents.DocumentPage page)
    {
        const double scale = 1.25;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(page.Size.Width * scale), (int)(page.Size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(page.Visual);
        bitmap.Freeze();
        return bitmap;
    }

    // ---------------------------------------------------------------- saving

    /// <summary>Asks the view to write what is in the editor into the model.</summary>
    public void Flush() => FlushRequested?.Invoke();

    public void Store(List<DocBlock> blocks)
    {
        Model.Blocks = blocks;
    }

    public void Save() => _owner.PersistLayout();
}

public sealed partial class PaperChoice(string name, string hex) : ObservableObject
{
    public string Name { get; } = name;
    public string Hex { get; } = hex;
    [ObservableProperty] bool _active;
}
