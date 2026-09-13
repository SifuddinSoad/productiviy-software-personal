using System.Windows;
using System.Windows.Controls;
using FocusLock.App.ViewModels;

namespace FocusLock.App.Views;

public partial class SetupView : UserControl
{
    public SetupView() => InitializeComponent();

    SetupViewModel? Vm => DataContext as SetupViewModel;

    void Back_Click(object sender, RoutedEventArgs e) => Vm?.Cancel();
    void AddPlan_Click(object sender, RoutedEventArgs e) => Vm?.AddPlan();
    void Start_Click(object sender, RoutedEventArgs e) => Vm?.Start();
    void CancelCountdown_Click(object sender, RoutedEventArgs e) => Vm?.CancelCountdown();

    void Duration_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DurationChip chip })
            chip.Pick();
    }

    void RemovePlan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PlanRow row })
            row.Remove();
    }
}
