using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusLock.App.Document;
using FocusLock.App.ViewModels;
using FocusLock.Core.Document;
using FocusLock.Core.Models;

namespace FocusLock.App.Views;

public partial class PageLayoutView : UserControl
{
    readonly DocumentEditor _editor;
    PageLayoutViewModel? _vm;

    public PageLayoutView()
    {
        InitializeComponent();
        _editor = new DocumentEditor(Editor);
        _editor.ContextChanged += () => _vm?.Format.Follow(_editor);
        Editor.PreviewKeyDown += OnEditorKeyDown;
        Editor.PreviewMouseWheel += (_, e) => { Board.ScrollBy(e.Delta); e.Handled = true; };
        Board.ViewMoved += PlaceEditor;

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
        }
        _vm = vm;
        Board.Model = vm;
        if (vm is null) return;

        vm.EditStarted += OnEditStarted;
        vm.EditFinishing += OnEditFinishing;
        vm.EditEnded += OnEditEnded;
        Dispatcher.BeginInvoke(() => Board.Focus());
    }

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
        // while typing, Table, Callout and Line go into the text itself
        if (Vm.IsEditing)
        {
            switch (kind)
            {
                case PageLayoutViewModel.NewText.Table: _editor.InsertTable(3, 3); return;
                case PageLayoutViewModel.NewText.Callout: _editor.InsertCallout(CalloutTone.Note); return;
                case PageLayoutViewModel.NewText.Line: _editor.InsertDivider(); return;
            }
        }
        if (Board.CurrentPageId() is { } pageId) Vm.AddText(kind, pageId);
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
