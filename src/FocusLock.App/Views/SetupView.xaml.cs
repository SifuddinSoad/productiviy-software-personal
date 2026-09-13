using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    /// <summary>Digits only, so the clock can never hold nonsense.</summary>
    void Clock_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsAsciiDigit);

    /// <summary>Selecting the whole field means typing replaces it, the way a clock behaves.</summary>
    void Clock_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box) box.Dispatcher.BeginInvoke(box.SelectAll);
    }

    void Clock_LostFocus(object sender, RoutedEventArgs e) => Vm?.NormaliseClock();

    /// <summary>Two digits fills a field, so move on to the next one.</summary>
    void Clock_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box || box.Text.Length < 2 || !box.IsKeyboardFocusWithin) return;

        var next = box == HoursBox ? MinutesBox : box == MinutesBox ? SecondsBox : null;
        if (next is null) return;
        next.Focus();
        next.SelectAll();
    }

    /// <summary>Up and down step a field, the way a spinner would.</summary>
    void Clock_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: string field }) return;
        if (e.Key is not (Key.Up or Key.Down)) return;

        Vm?.Nudge(field, e.Key == Key.Up ? 1 : -1);
        ((TextBox)sender).SelectAll();
        e.Handled = true;
    }

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
