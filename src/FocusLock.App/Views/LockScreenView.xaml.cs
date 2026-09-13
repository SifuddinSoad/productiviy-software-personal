using System.Windows;
using System.Windows.Controls;
using FocusLock.App.ViewModels;

namespace FocusLock.App.Views;

public partial class LockScreenView : UserControl
{
    public LockScreenView() => InitializeComponent();

    LockScreenViewModel? Vm => DataContext as LockScreenViewModel;

    void MakePlan_Click(object sender, RoutedEventArgs e) => Vm?.OnMakePlan();
    void End_Click(object sender, RoutedEventArgs e) => Vm?.OnEnd();
}
