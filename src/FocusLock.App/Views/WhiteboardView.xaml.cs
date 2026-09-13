using System.Windows;
using System.Windows.Controls;
using FocusLock.App.ViewModels;

namespace FocusLock.App.Views;

public partial class WhiteboardView : UserControl
{
    public WhiteboardView() => InitializeComponent();

    WhiteboardViewModel? Vm => DataContext as WhiteboardViewModel;

    void Back_Click(object sender, RoutedEventArgs e) => Vm?.OnBack();
    void Continue_Click(object sender, RoutedEventArgs e) => Vm?.OnContinue();
    void End_Click(object sender, RoutedEventArgs e) => Vm?.OnEnd();
}
