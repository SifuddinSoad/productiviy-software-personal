using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FocusLock.App.Document;
using FocusLock.App.Theme;
using FocusLock.App.ViewModels;
using FocusLock.Core.Document;

namespace FocusLock.App.Views;

public partial class DocumentEditorView : UserControl
{
    readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(900) };
    readonly DispatcherTimer _repaginate = new() { Interval = TimeSpan.FromMilliseconds(700) };
    DocumentEditor? _editor;
    DocumentEditorViewModel? _vm;

    /// <summary>Where pages 2, 3, … begin in the editor, kept as live positions so scrolling only has to move the lines.</summary>
    readonly List<(int Page, TextPointer At)> _pageStarts = [];

    public DocumentEditorView()
    {
        InitializeComponent();
        _save.Tick += (_, _) => { _save.Stop(); Flush(); _vm?.Save(); };
        _repaginate.Tick += (_, _) => { _repaginate.Stop(); Repaginate(); };
        Editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => DrawMarkers()));
        SizeChanged += (_, _) => DrawMarkers();
        DataContextChanged += (_, _) => Attach(DataContext as DocumentEditorViewModel);
        IsVisibleChanged += (_, _) => { if (IsVisible) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Editor.Focus()); };
    }

    void Attach(DocumentEditorViewModel? vm)
    {
        if (_vm is not null)
        {
            Flush();
            _vm.FlushRequested -= Flush;
            _vm.LayoutChanged -= Reload;
        }
        _vm = vm;
        _editor = null;
        if (vm is null) return;

        _editor = new DocumentEditor(Editor, vm.Model, vm.SectionFor);
        _editor.ContentChanged += () => { _save.Stop(); _save.Start(); _repaginate.Stop(); _repaginate.Start(); };
        _editor.ContextChanged += () => vm.Follow(_editor);
        vm.FlushRequested += Flush;
        vm.LayoutChanged += Reload;
        Reload();
    }

    void Reload()
    {
        if (_editor is null) return;
        // the scrollbar sits inside the box, so the box is that much wider than the paper
        _editor.Load();
        Editor.Width = DocLook.PageDip(_editor.Model.Landscape).W + SystemParameters.VerticalScrollBarWidth;
        _repaginate.Stop();
        _repaginate.Start();
    }

    void Flush()
    {
        if (_editor is null || _vm is null) return;
        _vm.Store(_editor.Read());
    }

    // ---------------------------------------------------------------- page lines

    void Repaginate()
    {
        if (_editor is null || _vm is null || !IsVisible) return;
        _pageStarts.Clear();

        var model = new DocModel
        {
            Blocks = _editor.Read(), Landscape = _vm.Model.Landscape, Paper = _vm.Model.Paper,
            ShowHeader = _vm.Model.ShowHeader, HeaderText = _vm.Model.HeaderText, ShowPageNumbers = _vm.Model.ShowPageNumbers,
        };
        var pager = DocumentPager.Paginate(model, _vm.SectionFor, _vm.SessionName);
        var units = DocumentMapper.Units(Editor.Document);

        foreach (var start in pager.PageStarts())
        {
            if (start.Unit >= units.Count) continue;
            var at = units[start.Unit] is Paragraph p ? DocumentPager.PointerAt(p, start.TextOffset) : units[start.Unit].ContentStart;
            _pageStarts.Add((start.Page, at));
        }
        DrawMarkers();
    }

    void DrawMarkers()
    {
        Markers.Children.Clear();
        if (_editor is null || !Editor.IsVisible) return;

        var paperLeft = Editor.TranslatePoint(new Point(0, 0), Markers).X;
        var paperWidth = DocLook.PageDip(_editor.Model.Landscape).W;
        var muted = DocLook.Muted(_editor.Model.Paper);

        foreach (var (page, at) in _pageStarts)
        {
            Rect rect;
            try { rect = at.GetCharacterRect(LogicalDirection.Forward); }
            catch (InvalidOperationException) { continue; }   // the position was edited away; the next pass replaces it
            if (rect.IsEmpty) continue;

            var y = Editor.TranslatePoint(new Point(0, rect.Top), Markers).Y - 5;
            if (y < 18 || y > Markers.ActualHeight) continue;

            Markers.Children.Add(new Line
            {
                X1 = paperLeft, X2 = paperLeft + paperWidth, Y1 = y, Y2 = y,
                Stroke = muted, StrokeThickness = 1, StrokeDashArray = [5, 4], SnapsToDevicePixels = true,
            });
            var label = new Border
            {
                Background = HexBrush.FromHex("#26292d"), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 1),
                Child = new TextBlock { Text = $"Page {page}", FontSize = 10.5, Foreground = HexBrush.FromHex("#c6cad0") },
            };
            Canvas.SetLeft(label, paperLeft + paperWidth + 8);
            Canvas.SetTop(label, y - 9);
            Markers.Children.Add(label);
        }
    }

    // ---------------------------------------------------------------- toolbar

    static string Param(object sender) => (sender as Button)?.CommandParameter as string ?? "";

    void Style_Click(object s, RoutedEventArgs e) => _editor?.SetStyle(Param(s));
    void Smaller_Click(object s, RoutedEventArgs e) => _editor?.StepSize(-1);
    void Bigger_Click(object s, RoutedEventArgs e) => _editor?.StepSize(1);
    void Bold_Click(object s, RoutedEventArgs e) => _editor?.ToggleBold();
    void Italic_Click(object s, RoutedEventArgs e) => _editor?.ToggleItalic();
    void Underline_Click(object s, RoutedEventArgs e) => _editor?.ToggleUnderline();
    void Align_Click(object s, RoutedEventArgs e) => _editor?.Align(Param(s));
    void List_Click(object s, RoutedEventArgs e) => _editor?.ToggleList(Param(s));
    void PageBreak_Click(object s, RoutedEventArgs e) => _editor?.InsertPageBreak();

    void Preview_Click(object s, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.ShowPreview(!_vm.IsPreview);
        if (!_vm.IsPreview) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { Editor.Focus(); Repaginate(); });
    }

    // ---------------------------------------------------------------- side panel

    static T? Item<T>(object sender) where T : class => (sender as FrameworkElement)?.DataContext as T;

    void Color_Click(object s, RoutedEventArgs e) => _editor?.SetColor(Item<string>(s));
    void ColorNone_Click(object s, RoutedEventArgs e) => _editor?.SetColor(null);
    void Highlight_Click(object s, RoutedEventArgs e) => _editor?.SetHighlight(Item<string>(s));
    void HighlightNone_Click(object s, RoutedEventArgs e) => _editor?.SetHighlight(null);

    void Callout_Click(object s, RoutedEventArgs e) => _editor?.InsertCallout(Param(s));
    void Divider_Click(object s, RoutedEventArgs e) => _editor?.InsertDivider();
    void RowsLess_Click(object s, RoutedEventArgs e) => _vm?.StepRows(-1);
    void RowsMore_Click(object s, RoutedEventArgs e) => _vm?.StepRows(1);
    void ColumnsLess_Click(object s, RoutedEventArgs e) => _vm?.StepColumns(-1);
    void ColumnsMore_Click(object s, RoutedEventArgs e) => _vm?.StepColumns(1);
    void Table_Click(object s, RoutedEventArgs e) { if (_vm is not null) _editor?.InsertTable(_vm.TableRows, _vm.TableColumns); }

    void RowAbove_Click(object s, RoutedEventArgs e) => _editor?.AddRow(below: false);
    void RowBelow_Click(object s, RoutedEventArgs e) => _editor?.AddRow(below: true);
    void ColumnLeft_Click(object s, RoutedEventArgs e) => _editor?.AddColumn(right: false);
    void ColumnRight_Click(object s, RoutedEventArgs e) => _editor?.AddColumn(right: true);
    void DeleteRow_Click(object s, RoutedEventArgs e) => _editor?.DeleteRow();
    void DeleteColumn_Click(object s, RoutedEventArgs e) => _editor?.DeleteColumn();

    void SectionSize_Click(object s, RoutedEventArgs e) { var size = Param(s); _editor?.UpdateSection(x => x.Size = size); }
    void SectionAlign_Click(object s, RoutedEventArgs e) { var align = Param(s); _editor?.UpdateSection(x => x.Align = align); }
    void SectionCaption_Click(object s, RoutedEventArgs e) => _editor?.UpdateSection(x => x.Caption = !x.Caption);
    void SectionRemove_Click(object s, RoutedEventArgs e) => _editor?.RemoveSelectedSection();

    void InsertSection_Click(object s, RoutedEventArgs e)
    {
        if (Item<ExtractCard>(s) is { } card) _editor?.InsertSection(card.Item.Id);
    }

    void Portrait_Click(object s, RoutedEventArgs e) => _vm?.SetLandscape(false);
    void Landscape_Click(object s, RoutedEventArgs e) => _vm?.SetLandscape(true);
    void Paper_Click(object s, RoutedEventArgs e) { if (Item<PaperChoice>(s) is { } paper) _vm?.SetPaper(paper.Hex); }

    void Header_Click(object s, RoutedEventArgs e) { _vm?.SetShowHeader(!(_vm?.ShowHeader ?? true)); Repaginate(); }
    void PageNumbers_Click(object s, RoutedEventArgs e) { _vm?.SetShowPageNumbers(!(_vm?.ShowPageNumbers ?? true)); }

    void HeaderText_LostFocus(object s, RoutedEventArgs e) => _vm?.SetHeaderText(HeaderBox.Text);

    void HeaderText_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _vm?.SetHeaderText(HeaderBox.Text);
        Editor.Focus();
    }
}
