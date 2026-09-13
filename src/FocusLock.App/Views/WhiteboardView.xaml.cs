using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusLock.App.Board;
using FocusLock.App.ViewModels;

namespace FocusLock.App.Views;

public partial class WhiteboardView : UserControl
{
    public WhiteboardView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            Canvas.Controller = Vm?.Controller;
            if (Vm is not null)
            {
                Canvas.Controller!.ViewportWidth = Canvas.ActualWidth;
                Canvas.Controller.ViewportHeight = Canvas.ActualHeight;
            }
        };
        Loaded += (_, _) => Canvas.Focus();
    }

    WhiteboardViewModel? Vm => DataContext as WhiteboardViewModel;
    BoardController? C => Vm?.Controller;

    static T? Item<T>(object sender) where T : class =>
        (sender as FrameworkElement)?.DataContext as T;

    // ---- top bar ----
    void Back_Click(object s, RoutedEventArgs e) => Vm?.OnBack();
    void Continue_Click(object s, RoutedEventArgs e) => Vm?.OnContinue();
    void End_Click(object s, RoutedEventArgs e) => Vm?.OnEnd();
    void Undo_Click(object s, RoutedEventArgs e) => C?.Undo();
    void Redo_Click(object s, RoutedEventArgs e) => C?.Redo();
    void TogglePlans_Click(object s, RoutedEventArgs e) => Vm?.TogglePlans();
    void TogglePrompt_Click(object s, RoutedEventArgs e) => Vm?.TogglePrompt();
    void ClosePanel_Click(object s, RoutedEventArgs e) => Vm?.ClosePanel();
    void Deselect_Click(object s, RoutedEventArgs e) => C?.Deselect();

    // ---- zoom ----
    void ZoomIn_Click(object s, RoutedEventArgs e) => C?.ZoomIn();
    void ZoomOut_Click(object s, RoutedEventArgs e) => C?.ZoomOut();
    void ZoomReset_Click(object s, RoutedEventArgs e) => C?.ZoomReset();
    void ZoomFit_Click(object s, RoutedEventArgs e) => C?.ZoomFit();

    // ---- dock and flyouts ----
    void Tool_Click(object s, RoutedEventArgs e) => Item<ToolButton>(s)?.Pick();
    void ShapeKind_Click(object s, RoutedEventArgs e) => Item<FlyoutItem>(s)?.Pick();
    void ConnStyle_Click(object s, RoutedEventArgs e) => Item<FlyoutItem>(s)?.Pick();
    void ConnArrows_Click(object s, RoutedEventArgs e) => Item<FlyoutItem>(s)?.Pick();
    void StickyColor_Click(object s, RoutedEventArgs e) => Item<Swatch>(s)?.Pick();
    void ToggleNewConnDash_Click(object s, RoutedEventArgs e) => C?.ToggleConnDash();

    // ---- props ----
    void Fill_Click(object s, RoutedEventArgs e) => Item<Swatch>(s)?.Pick();
    void VoteUp_Click(object s, RoutedEventArgs e) => C?.AddVotes(1);
    void VoteDown_Click(object s, RoutedEventArgs e) => C?.AddVotes(-1);
    void Duplicate_Click(object s, RoutedEventArgs e) => C?.DuplicateSelection();
    void Delete_Click(object s, RoutedEventArgs e) => C?.DeleteSelection();

    void Align_Click(object s, RoutedEventArgs e)
    {
        if (s is Button { Tag: string what }) C?.Align(what);
    }

    void EditText_Click(object s, RoutedEventArgs e)
    {
        if (C?.SingleSelection is { } o) C.BeginEdit(o.Id);
    }

    // ---- connector panel ----
    void ConnStyleSelected_Click(object s, RoutedEventArgs e)
    {
        if (Item<FlyoutItem>(s) is { } item) C?.SetConnectorStyle(item.Id);
    }

    void ConnArrowsSelected_Click(object s, RoutedEventArgs e)
    {
        if (Item<FlyoutItem>(s) is { } item) C?.SetConnectorArrows(item.Id);
    }

    void ToggleConnDash_Click(object s, RoutedEventArgs e) => C?.ToggleConnectorDash();

    // ---- plans and prompts ----
    void OpenPlan_Click(object s, RoutedEventArgs e) => Item<PlanCard>(s)?.Open();
    void ToggleDone_Click(object s, RoutedEventArgs e) => Item<PlanCard>(s)?.Toggle();
    void NewPlan_Click(object s, RoutedEventArgs e) => Vm?.NewPlan();
    void AddPrompt_Click(object s, RoutedEventArgs e) => Vm?.AddPrompt();
    void FocusPrompt_Click(object s, RoutedEventArgs e) => Item<PromptCard>(s)?.Focus();
    void DeletePrompt_Click(object s, RoutedEventArgs e) => Item<PromptCard>(s)?.Delete();

    void PlanName_LostFocus(object s, RoutedEventArgs e)
    {
        if (s is TextBox box && Item<PlanCard>(s) is { } card) card.Rename(box.Text);
    }

    void PlanName_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || s is not TextBox box) return;
        e.Handled = true;
        Item<PlanCard>(s)?.Rename(box.Text);
        Canvas.Focus();
    }

    // ---- keyboard ----
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (C is not { } c || c.ReadOnly) return;
        // A letter typed into a label must never also fire that letter's tool shortcut.
        if (Canvas.IsEditing || Keyboard.FocusedElement is TextBox) return;

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (e.Key == Key.Space) { c.SpaceHeld = true; e.Handled = true; return; }

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z: if (shift) c.Redo(); else c.Undo(); e.Handled = true; return;
                case Key.A: c.SelectAll(); e.Handled = true; return;
                case Key.D: c.DuplicateSelection(); e.Handled = true; return;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.OemOpenBrackets: c.SetEraserSize(c.EraserSize - 6); e.Handled = true; return;
            case Key.OemCloseBrackets: c.SetEraserSize(c.EraserSize + 6); e.Handled = true; return;
            case Key.Enter when c.PolyPoints is { Count: > 0 }: c.ClosePolygon(); e.Handled = true; return;
            case Key.Back or Key.Delete: c.DeleteSelection(); e.Handled = true; return;
            case Key.Escape: c.Deselect(); c.SetTool(Tool.Select); e.Handled = true; return;
            case Key.D1 when shift: c.ZoomFit(); e.Handled = true; return;
        }

        var tool = e.Key switch
        {
            Key.V => Tool.Select,
            Key.H => Tool.Hand,
            Key.P => Tool.Pen,
            Key.E => Tool.Eraser,
            Key.R => Tool.Shape,
            Key.X => Tool.Connector,
            Key.N => Tool.Sticky,
            Key.T => Tool.Text,
            Key.F => Tool.Frame,
            Key.B => Tool.Table,
            Key.G => Tool.Prompt,
            Key.D => Tool.Vote,
            _ => null,
        };
        if (tool is not null)
        {
            c.SetTool(tool);
            e.Handled = true;
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Key == Key.Space && C is { } c) c.SpaceHeld = false;
    }
}
