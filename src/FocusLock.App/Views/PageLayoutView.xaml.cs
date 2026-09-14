using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusLock.App.Document;
using FocusLock.App.ViewModels;
using FocusLock.Core.Document;
using FocusLock.Core.Models;

namespace FocusLock.App.Views;

public partial class PageLayoutView : UserControl
{
    const double ThumbWidth = 104;

    readonly DocumentEditor _editor;
    PageLayoutViewModel? _vm;

    readonly ObservableCollection<PageThumb> _thumbs = [];
    readonly List<BitmapSource?> _thumbPictures = [];
    readonly DispatcherTimer _thumbsDue = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public PageLayoutView()
    {
        InitializeComponent();
        _editor = new DocumentEditor(Editor);
        _editor.ContextChanged += () => _vm?.Format.Follow(_editor);
        Editor.PreviewKeyDown += OnEditorKeyDown;
        Editor.PreviewMouseWheel += (_, e) => { Board.ScrollBy(e.Delta); e.Handled = true; };
        Board.ViewMoved += OnViewMoved;

        PageList.ItemsSource = _thumbs;
        _thumbsDue.Tick += (_, _) => { _thumbsDue.Stop(); RenderThumbs(); };

        DataContextChanged += (_, _) => Attach(DataContext as PageLayoutViewModel);
    }

    void Attach(PageLayoutViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.FinishEditing();
            _vm.EditStarted -= OnEditStarted;
            _vm.EditFinishing -= OnEditFinishing;
            _vm.EditEnded -= OnEditEnded;
            _vm.Changed -= OnModelChanged;
        }
        _vm = vm;
        Board.Model = vm;
        _thumbs.Clear();
        _thumbPictures.Clear();
        if (vm is null) return;

        vm.EditStarted += OnEditStarted;
        vm.EditFinishing += OnEditFinishing;
        vm.EditEnded += OnEditEnded;
        vm.Changed += OnModelChanged;
        Dispatcher.BeginInvoke(() => { Board.Focus(); RenderThumbs(); });
    }

    void OnViewMoved()
    {
        PlaceEditor();
        PlaceMiniBar();
        if (_vm is null) return;
        var current = Board.CurrentPageIndex();
        if (current == _vm.CurrentPageIndex) return;
        _vm.CurrentPageIndex = current;
        MarkCurrentThumb();
    }

    // drags redraw on every step; the page pictures only follow once things settle
    void OnModelChanged()
    {
        if (Board.IsDragging) return;
        _thumbsDue.Stop();
        _thumbsDue.Start();
    }

    // ---------------------------------------------------------------- page list

    void RenderThumbs()
    {
        if (_vm is null) return;
        _thumbPictures.Clear();
        for (var i = 0; i < _vm.Session.Pages.Count; i++) _thumbPictures.Add(Board.RenderPageThumbnail(i, ThumbWidth));
        MarkCurrentThumb();
    }

    void MarkCurrentThumb()
    {
        if (_vm is null) return;
        var count = Math.Min(_vm.Session.Pages.Count, _thumbPictures.Count);
        var fresh = Enumerable.Range(0, count)
            .Select(i => new PageThumb(i, (i + 1).ToString(), _thumbPictures[i], i == _vm.CurrentPageIndex)).ToList();
        if (fresh.SequenceEqual(_thumbs)) return;
        _thumbs.Clear();
        foreach (var thumb in fresh) _thumbs.Add(thumb);
    }

    int? _pageDrag;
    Point _pageDown;
    bool _pageMoving;

    void PageThumb_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (s is not FrameworkElement { DataContext: PageThumb thumb } element) return;
        _pageDrag = thumb.Index;
        _pageDown = e.GetPosition(this);
        _pageMoving = false;
        element.CaptureMouse();
        e.Handled = true;
    }

    void PageThumb_MouseMove(object s, MouseEventArgs e)
    {
        if (_pageDrag is null || s is not FrameworkElement { IsMouseCaptured: true }) return;
        var p = e.GetPosition(this);
        if (!_pageMoving && (p - _pageDown).Length < 5) return;
        _pageMoving = true;

        var (target, lineY) = PageDropTarget(e.GetPosition(PageList));
        var origin = PageList.TranslatePoint(new Point(26, lineY), DragLayer);
        Canvas.SetLeft(PageDropLine, origin.X);
        Canvas.SetTop(PageDropLine, origin.Y - 5);
        PageDropLine.Visibility = Visibility.Visible;
        _pageTarget = target;
    }

    int _pageTarget;

    void PageThumb_MouseUp(object s, MouseButtonEventArgs e)
    {
        if (_pageDrag is not { } from) return;
        (s as FrameworkElement)?.ReleaseMouseCapture();
        PageDropLine.Visibility = Visibility.Collapsed;
        _pageDrag = null;
        e.Handled = true;

        if (!_pageMoving)
        {
            Board.ScrollToPage(from, flash: true);
            Board.Focus();
            return;
        }
        // the target counts gaps between pages; taking the page out first shifts the ones after it up
        var to = _pageTarget > from ? _pageTarget - 1 : _pageTarget;
        _vm?.MovePage(from, to);
        Board.ScrollToPage(to, flash: true);
        Board.Focus();
    }

    /// <summary>Which gap between page pictures the pointer is nearest, and where that gap is, in the list's coordinates.</summary>
    (int Target, double LineY) PageDropTarget(Point p)
    {
        var count = _thumbs.Count;
        for (var i = 0; i < count; i++)
        {
            if (PageList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container) continue;
            var top = container.TranslatePoint(new Point(0, 0), PageList).Y;
            if (p.Y < top + container.ActualHeight / 2) return (i, top - 4);
        }
        if (count > 0 && PageList.ItemContainerGenerator.ContainerFromIndex(count - 1) is FrameworkElement last)
            return (count, last.TranslatePoint(new Point(0, last.ActualHeight), PageList).Y - 4);
        return (count, 0);
    }

    void AddPage_Click(object s, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.AddPage();
        Board.ScrollToPage(_vm.Session.Pages.Count - 1, flash: true);
        Board.Focus();
    }

    // ---------------------------------------------------------------- sections dragged in

    string? _sectionDrag;
    Point _sectionDown;
    bool _sectionMoving;

    void SectionCard_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (s is not FrameworkElement { DataContext: SectionCard card } element) return;
        _vm?.FinishEditing();
        _sectionDrag = card.Id;
        _sectionDown = e.GetPosition(this);
        _sectionMoving = false;
        DragGhostImage.Source = card.Picture;
        DragGhostName.Text = card.Name;
        element.CaptureMouse();
        e.Handled = true;
    }

    void SectionCard_MouseMove(object s, MouseEventArgs e)
    {
        if (_sectionDrag is not { } id || s is not FrameworkElement { IsMouseCaptured: true }) return;
        var p = e.GetPosition(this);
        if (!_sectionMoving && (p - _sectionDown).Length < 5) return;
        _sectionMoving = true;

        Canvas.SetLeft(DragGhost, p.X + 12);
        Canvas.SetTop(DragGhost, p.Y + 12);
        DragGhost.Visibility = Visibility.Visible;

        var onBoard = e.GetPosition(Board);
        if (OverBoard(onBoard)) Board.PreviewSectionDrop(id, onBoard);
        else Board.ClearDropPreview();
    }

    void SectionCard_MouseUp(object s, MouseButtonEventArgs e)
    {
        if (_sectionDrag is not { } id) return;
        (s as FrameworkElement)?.ReleaseMouseCapture();
        DragGhost.Visibility = Visibility.Collapsed;
        _sectionDrag = null;
        e.Handled = true;

        var onBoard = e.GetPosition(Board);
        if (_sectionMoving && OverBoard(onBoard)) Board.DropSection(id, onBoard);
        else if (!_sectionMoving && _vm is not null)
        {
            _vm.Select(id);   // a click finds it
            Board.BringSelectedIntoView();
        }
        Board.ClearDropPreview();
        Board.Focus();
    }

    bool OverBoard(Point p) => p.X >= 0 && p.Y >= 0 && p.X <= Board.ActualWidth && p.Y <= Board.ActualHeight;

    void PickRegion_Click(object s, RoutedEventArgs e) => _vm?.PickRegion();

    // ---------------------------------------------------------------- floating toolbar

    void PlaceMiniBar()
    {
        var show = _vm is { HasSelection: true, IsEditing: false } && !Board.IsDragging && Board.SelectedScreenRect() is not null;
        if (!show)
        {
            MiniBar.Visibility = Visibility.Collapsed;
            return;
        }

        var box = Board.SelectedScreenRect()!.Value;
        MiniBar.Visibility = Visibility.Visible;
        MiniBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = MiniBar.DesiredSize;

        var x = Math.Clamp(box.X + box.Width / 2 - size.Width / 2, 8, Math.Max(8, Overlay.ActualWidth - size.Width - 8));
        var y = box.Top - size.Height - 12;
        if (y < 6) y = box.Bottom + 12;   // no room above: underneath
        if (y > Overlay.ActualHeight - size.Height - 6 || box.Bottom < 0 || box.Top > Overlay.ActualHeight)
        {
            MiniBar.Visibility = Visibility.Collapsed;   // off screen
            return;
        }
        Canvas.SetLeft(MiniBar, x);
        Canvas.SetTop(MiniBar, y);
    }

    static double Fraction(object sender) =>
        double.TryParse(Param(sender), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 1;

    void Width_Click(object s, RoutedEventArgs e) { _vm?.SetWidthFraction(Fraction(s)); Board.Focus(); }
    void AlignBox_Click(object s, RoutedEventArgs e) { _vm?.AlignSelected(Param(s)); Board.Focus(); }
    void Duplicate_Click(object s, RoutedEventArgs e) { _vm?.DuplicateSelected(); Board.BringSelectedIntoView(); Board.Focus(); }
    void Delete_Click(object s, RoutedEventArgs e) { _vm?.RemoveSelected(); Board.Focus(); }
    void Edit_Click(object s, RoutedEventArgs e) => _vm?.EditSelected();

    void Portrait_Click(object s, RoutedEventArgs e) { _vm?.SetCurrentLandscape(false); Board.Focus(); }
    void Landscape_Click(object s, RoutedEventArgs e) { _vm?.SetCurrentLandscape(true); Board.Focus(); }
    void DeletePage_Click(object s, RoutedEventArgs e) { _vm?.DeleteCurrentPage(); Board.Focus(); }

    // ---------------------------------------------------------------- the editor over a text box

    void OnEditStarted(TextItem text, string startAt)
    {
        if (_vm is null) return;
        Board.BringIntoView(text);
        _editor.Load(text.Blocks, TextFlow.PaperOf(_vm.Session), DocLook.Dip(text.PageW));
        EditorFrame.Background = HexBrushOrTransparent(_vm.Light);
        if (startAt == PageLayoutViewModel.NewText.Table) _editor.CaretToFirstCell();
        else if (startAt == PageLayoutViewModel.NewText.Callout) _editor.CaretToStart();

        EditorFrame.Visibility = Visibility.Visible;
        PlaceEditor();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Editor.Focus());
    }

    static Brush HexBrushOrTransparent(bool light) => Theme.HexBrush.FromHex(light ? "#ffffff" : "#121315");

    List<DocBlock>? OnEditFinishing() => EditorFrame.Visibility == Visibility.Visible ? _editor.Read() : null;

    void OnEditEnded()
    {
        EditorFrame.Visibility = Visibility.Collapsed;
        Board.Focus();
    }

    /// <summary>Keeps the editor exactly over its text box as the pages scroll and zoom.</summary>
    void PlaceEditor()
    {
        if (_vm?.EditingText is not { } text || EditorFrame.Visibility != Visibility.Visible) return;
        if (Board.ScreenOrigin(text) is not { } origin) return;

        // the editor lays text out in paper units (4/3 per point); the board shows z pixels per point
        var scale = _vm.Zoom / DocLook.DipPerPoint;
        Editor.LayoutTransform = new ScaleTransform(scale, scale);
        Canvas.SetLeft(EditorFrame, origin.X);
        Canvas.SetTop(EditorFrame, origin.Y);
    }

    void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _vm?.FinishEditing();
        }
    }

    // ---------------------------------------------------------------- top bar

    void Refocus()
    {
        if (_vm?.IsEditing == true) Editor.Focus();
        else Board.Focus();
    }

    PageLayoutViewModel? Vm => _vm;

    void Close_Click(object s, RoutedEventArgs e) => Vm?.Close();
    void Undo_Click(object s, RoutedEventArgs e) { Vm?.Undo(); Refocus(); }
    void Redo_Click(object s, RoutedEventArgs e) { Vm?.Redo(); Refocus(); }
    void Export_Click(object s, RoutedEventArgs e) { Vm?.Export(); Refocus(); }

    void AddText_Click(object s, RoutedEventArgs e)
    {
        if (Vm is null || (s as Button)?.CommandParameter is not string kind) return;
        // while typing, Table, Callout and Line go into the text itself; Text and Heading start a new box
        if (Vm.IsEditing)
        {
            switch (kind)
            {
                case PageLayoutViewModel.NewText.Table: _editor.InsertTable(3, 3); return;
                case PageLayoutViewModel.NewText.Callout: _editor.InsertCallout(CalloutTone.Note); return;
                case PageLayoutViewModel.NewText.Line: _editor.InsertDivider(); return;
            }
        }        if (Board.CurrentPageId() is { } pageId) Vm.AddText(kind, pageId);
        if (!Vm.IsEditing) Board.Focus();
    }

    // ---------------------------------------------------------------- page settings

    void Dark_Click(object s, RoutedEventArgs e) { Vm?.SetLight(false); Refocus(); }
    void White_Click(object s, RoutedEventArgs e) { Vm?.SetLight(true); Refocus(); }
    void Titles_Click(object s, RoutedEventArgs e) { Vm?.ToggleTitles(); Refocus(); }
    void Header_Click(object s, RoutedEventArgs e) { Vm?.ToggleHeader(); Refocus(); }
    void PageNumbers_Click(object s, RoutedEventArgs e) { Vm?.TogglePageNumbers(); Refocus(); }
    void ZoomIn_Click(object s, RoutedEventArgs e) { Vm?.ZoomIn(); Refocus(); }
    void ZoomOut_Click(object s, RoutedEventArgs e) { Vm?.ZoomOut(); Refocus(); }
    void ZoomReset_Click(object s, RoutedEventArgs e) { Vm?.ZoomReset(); Refocus(); }
    void Fit_Click(object s, RoutedEventArgs e) { Board.FitWidth(); Refocus(); }
    void Tidy_Click(object s, RoutedEventArgs e) { Vm?.Tidy(); Refocus(); }

    void HeaderText_LostFocus(object s, RoutedEventArgs e) => Vm?.SetHeaderText(HeaderBox.Text);

    void HeaderText_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Vm?.SetHeaderText(HeaderBox.Text);
        Board.Focus();
    }

    // ---------------------------------------------------------------- formatting

    static string Param(object sender) => (sender as Button)?.CommandParameter as string ?? "";
    static string? Swatch(object sender) => (sender as FrameworkElement)?.DataContext as string;

    void Style_Click(object s, RoutedEventArgs e) => _editor.SetStyle(Param(s));
    void Smaller_Click(object s, RoutedEventArgs e) => _editor.StepSize(-1);
    void Bigger_Click(object s, RoutedEventArgs e) => _editor.StepSize(1);
    void Bold_Click(object s, RoutedEventArgs e) => _editor.ToggleBold();
    void Italic_Click(object s, RoutedEventArgs e) => _editor.ToggleItalic();
    void Underline_Click(object s, RoutedEventArgs e) => _editor.ToggleUnderline();
    void Color_Click(object s, RoutedEventArgs e) => _editor.SetColor(Swatch(s));
    void ColorNone_Click(object s, RoutedEventArgs e) => _editor.SetColor(null);
    void Highlight_Click(object s, RoutedEventArgs e) => _editor.SetHighlight(Swatch(s));
    void HighlightNone_Click(object s, RoutedEventArgs e) => _editor.SetHighlight(null);
    void Align_Click(object s, RoutedEventArgs e) => _editor.Align(Param(s));
    void List_Click(object s, RoutedEventArgs e) => _editor.ToggleList(Param(s));
    void InsertTable_Click(object s, RoutedEventArgs e) => _editor.InsertTable(3, 3);
    void Callout_Click(object s, RoutedEventArgs e) => _editor.InsertCallout(Param(s));
    void Divider_Click(object s, RoutedEventArgs e) => _editor.InsertDivider();
    void PageBreak_Click(object s, RoutedEventArgs e) => _editor.InsertPageBreak();
    void RowAbove_Click(object s, RoutedEventArgs e) => _editor.AddRow(below: false);
    void RowBelow_Click(object s, RoutedEventArgs e) => _editor.AddRow(below: true);
    void ColumnLeft_Click(object s, RoutedEventArgs e) => _editor.AddColumn(right: false);
    void ColumnRight_Click(object s, RoutedEventArgs e) => _editor.AddColumn(right: true);
    void DeleteRow_Click(object s, RoutedEventArgs e) => _editor.DeleteRow();
    void DeleteColumn_Click(object s, RoutedEventArgs e) => _editor.DeleteColumn();
    void Done_Click(object s, RoutedEventArgs e) => Vm?.FinishEditing();
}
