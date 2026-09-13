using System.Windows;
using System.Windows.Controls;
using FocusLock.App.ViewModels;

namespace FocusLock.App.Views;

public partial class PageLayoutView : UserControl
{
    public PageLayoutView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            Board.Model = Vm;
            if (Vm is not null) Dispatcher.BeginInvoke(() => Board.Focus());
        };
    }

    PageLayoutViewModel? Vm => DataContext as PageLayoutViewModel;

    // the toolbar hands keyboard focus straight back, so Delete and Ctrl+Z keep working after a click
    void Refocus() => Board.Focus();

    void Close_Click(object s, RoutedEventArgs e) => Vm?.Close();
    void FreeMode_Click(object s, RoutedEventArgs e) { Vm?.SetMode(FocusLock.Core.Document.PdfMode.Free); Refocus(); }
    void DocumentMode_Click(object s, RoutedEventArgs e) => Vm?.SetMode(FocusLock.Core.Document.PdfMode.Document);
    void Undo_Click(object s, RoutedEventArgs e) { Vm?.Undo(); Refocus(); }
    void Redo_Click(object s, RoutedEventArgs e) { Vm?.Redo(); Refocus(); }
    void Dark_Click(object s, RoutedEventArgs e) { Vm?.SetLight(false); Refocus(); }
    void White_Click(object s, RoutedEventArgs e) { Vm?.SetLight(true); Refocus(); }
    void Titles_Click(object s, RoutedEventArgs e) { Vm?.ToggleTitles(); Refocus(); }
    void ZoomIn_Click(object s, RoutedEventArgs e) { Vm?.ZoomIn(); Refocus(); }
    void ZoomOut_Click(object s, RoutedEventArgs e) { Vm?.ZoomOut(); Refocus(); }
    void ZoomReset_Click(object s, RoutedEventArgs e) { Vm?.ZoomReset(); Refocus(); }
    void Export_Click(object s, RoutedEventArgs e) { Vm?.Export(); Refocus(); }
}
