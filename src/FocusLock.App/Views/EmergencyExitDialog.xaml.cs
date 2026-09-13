using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusLock.Core.Sessions;

namespace FocusLock.App.Views;

/// <summary>
/// The way out of a locked session when something real happens. The code is long on purpose and
/// must be typed by hand: pasting, dropping and the context menu are all closed off.
/// </summary>
public partial class EmergencyExitDialog : Window
{
    readonly string _code;

    public EmergencyExitDialog(string? code = null)
    {
        InitializeComponent();
        _code = code ?? EmergencyCode.Generate();
        CodeText.Text = _code;
        Input.ContextMenu = null;
        Input.TextChanged += (_, _) => Refresh();
        DataObject.AddPastingHandler(Input, (_, e) => e.CancelCommand());
        Loaded += (_, _) => Input.Focus();
        Refresh();
    }

    void Refresh()
    {
        var typed = Input.Text;
        var matches = EmergencyCode.Matches(_code, typed);
        UnlockButton.IsEnabled = matches;

        var correct = 0;
        while (correct < typed.Length && correct < _code.Length && typed[correct] == _code[correct]) correct++;

        Status.Text = matches
            ? "Matches — you can unlock."
            : typed.Length == 0
                ? $"{_code.Count(c => c != ' ')} characters to type"
                : correct == typed.Length
                    ? $"{correct} of {_code.Length} matched so far"
                    : $"Doesn't match from character {correct + 1}";
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        // no pasting by any of its usual shortcuts
        if ((ctrl && e.Key is Key.V or Key.Y) || (shift && e.Key == Key.Insert))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (!EmergencyCode.Matches(_code, Input.Text)) return;
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
