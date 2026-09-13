using System.Windows;
using System.Windows.Controls;
using FocusLock.App.ViewModels;

namespace FocusLock.App.Views;

public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

    void NewSession_Click(object sender, RoutedEventArgs e) =>
        (DataContext as HomeViewModel)?.StartNew();

    void Session_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SessionRow row } && !row.ConfirmingDelete)
            row.Open();
    }

    static SessionRow? Row(object sender) => (sender as FrameworkElement)?.DataContext as SessionRow;

    void AskDelete_Click(object sender, RoutedEventArgs e) => Row(sender)?.AskDelete();
    void CancelDelete_Click(object sender, RoutedEventArgs e) => Row(sender)?.CancelDelete();
    void ConfirmDelete_Click(object sender, RoutedEventArgs e) => Row(sender)?.Delete();

    void ClearAll_Click(object sender, RoutedEventArgs e) => (DataContext as HomeViewModel)?.AskClearAll();
    void CancelClearAll_Click(object sender, RoutedEventArgs e) => (DataContext as HomeViewModel)?.CancelClearAll();
    void ConfirmClearAll_Click(object sender, RoutedEventArgs e) => (DataContext as HomeViewModel)?.ClearAll();
}
