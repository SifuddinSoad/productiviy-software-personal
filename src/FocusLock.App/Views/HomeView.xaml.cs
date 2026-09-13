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
        if (sender is FrameworkElement { DataContext: SessionRow row })
            row.Open();
    }
}
