using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FocusLock.App.Board;
using FocusLock.App.Services;
using FocusLock.App.Theme;
using FocusLock.Core;
using FocusLock.Core.Board;
using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.App.ViewModels;

public sealed partial class WhiteboardViewModel : ObservableObjectBase, IDisposable
{
    readonly SessionRuntime? _runtime;
    readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(1) };

    public Session Session { get; }
    public bool ReadOnly { get; }
    public bool CanContinue { get; }
    public BoardController Controller { get; }

    public ObservableCollection<ToolButton> Tools { get; } = [];
    public ObservableCollection<FlyoutItem> ShapeKinds { get; } = [];
    /// <summary>Route and end styles applied to the next connector you draw.</summary>
    public ObservableCollection<FlyoutItem> ConnectorStyles { get; } = [];
    public ObservableCollection<FlyoutItem> ArrowModes { get; } = [];

    /// <summary>The same choices for the connector that is currently selected.</summary>
    public ObservableCollection<FlyoutItem> SelectedStyles { get; } = [];
    public ObservableCollection<FlyoutItem> SelectedArrows { get; } = [];
    public ObservableCollection<Swatch> StickySwatches { get; } = [];
    public ObservableCollection<Swatch> FillSwatches { get; } = [];
    public ObservableCollection<Swatch> TextSwatches { get; } = [];
    public ObservableCollection<PlanCard> Plans { get; } = [];
    public ObservableCollection<PromptCard> Prompts { get; } = [];

    [ObservableProperty] string _timerLabel = "";
    [ObservableProperty] bool _isFinished;
    [ObservableProperty] string _panel = "plans";
    [ObservableProperty] PlanCard? _currentPlan;

    public event Action? Back;
    public event Action<Session>? Continue;
    public event Action? EmergencyExit;

    /// <summary>Only offered while a session is actually holding the screen.</summary>
    public bool CanEmergencyExit => !ReadOnly;

    public void RequestEmergencyExit() => EmergencyExit?.Invoke();

    static readonly string[] StickyColors = ["#f2d06b", "#a8d5c2", "#aec8e8", "#f0b8ae", "#c9bce4", "#eae7e1"];
    static readonly string[] Fills = ["#ffffff", "#eae7e1", "#f2d06b", "#a8d5c2", "#aec8e8", "#f0b8ae", "#c9bce4"];
    static readonly string[] TextColors =
        ["#17181a", "#e9e9e7", "#ffffff", "#7f8489", "#f2d06b", "#a8d5c2", "#aec8e8", "#f0b8ae", "#c9bce4"];

    public WhiteboardViewModel(Session session, SessionRuntime? runtime, bool readOnly)
    {
        Session = session;
        ReadOnly = readOnly;
        _runtime = readOnly ? null : runtime;
        CanContinue = readOnly && Progress.CanContinue(session);

        if (session.Plans.Count == 0)
            session.Plans.Add(new Plan { Id = Ids.New("pl"), Name = "Untitled plan", UpdatedUtc = DateTime.UtcNow });

        Controller = new BoardController(session.Plans[0].Doc, readOnly);
        Controller.Changed += OnBoardChanged;

        BuildTools();
        BuildPalettes();
        RebuildPlans();
        CurrentPlan = Plans[0];
        RefreshPrompts();

        _timerLabel = readOnly ? Progress.StateLabel(session) : runtime!.TimerLabel;
        _isFinished = !readOnly && runtime!.IsFinished;
        if (_runtime is not null) _runtime.PropertyChanged += OnRuntimeChanged;

        _autosave.Tick += (_, _) => { _autosave.Stop(); Save(); };
    }

    // ---------------------------------------------------------------- top bar

    public string PlanName => CurrentPlan?.Name ?? Session.Name;
    public string PlanMeta => ReadOnly
        ? $"{Session.Name} · read only"
        : $"{Session.Plans.Count} plans · {Session.Name}";
    public bool TimerRunning => !ReadOnly && !IsFinished;
    public string ObjectCountLabel => $"{Controller.Doc.Objs.Count} objects";
    public string PromptCountLabel => Prompts.Count.ToString();
    public bool CanUndo => Controller.Editor.CanUndo;
    public bool CanRedo => Controller.Editor.CanRedo;
    public string ZoomLabel => $"{Math.Round(Controller.Doc.Cam.Z * 100)}%";

    public string Hint => Tools.FirstOrDefault(t => t.Active)?.HintText ?? "";

    void OnRuntimeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        TimerLabel = _runtime!.TimerLabel;
        IsFinished = _runtime.IsFinished;
        OnPropertyChanged(nameof(TimerRunning));
    }

    void OnBoardChanged()
    {
        foreach (var t in Tools) t.Active = t.Id == Controller.CurrentTool;
        foreach (var s in ShapeKinds) s.Active = s.Id == Controller.ShapeKind;
        foreach (var s in ConnectorStyles) s.Active = s.Id == Controller.ConnStyle;
        foreach (var a in ArrowModes) a.Active = a.Id == Controller.ConnArrows;
        foreach (var s in StickySwatches) s.Active = s.Color == Controller.StickyColor;

        // With several lines selected the controls follow the first one.
        var selected = Controller.SelectedConnectors.FirstOrDefault();
        foreach (var s in SelectedStyles) s.Active = selected is not null && s.Id == (selected.Style is "" ? "curve" : selected.Style);
        foreach (var a in SelectedArrows) a.Active = selected is not null && a.Id == (selected.Arrows is "" ? "end" : selected.Arrows);

        var single = Controller.SingleSelection;
        foreach (var s in FillSwatches) s.Active = single?.Fill == s.Color;
        foreach (var s in TextSwatches) s.Active = single?.TextColor == s.Color;

        if (Controller.SelectedIds.Count > 0) Panel = "props";
        else if (Controller.SelectedConnectorIds.Count > 0) Panel = "conn";
        else if (Panel == "props" || Panel == "conn") Panel = "plans";

        RefreshPrompts();
        OnPropertyChanged(nameof(ObjectCountLabel));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(ZoomLabel));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(ShowShapeFlyout));
        OnPropertyChanged(nameof(ShowConnFlyout));
        OnPropertyChanged(nameof(ShowStickyFlyout));
        OnPropertyChanged(nameof(ShowEraserFlyout));
        OnPropertyChanged(nameof(EraserLabel));
        OnPropertyChanged(nameof(EraserSize));
        OnPropertyChanged(nameof(SelectionTitle));
        OnPropertyChanged(nameof(TextColorIsAuto));
        OnPropertyChanged(nameof(SelectionVotes));
        OnPropertyChanged(nameof(IsMultiSelection));
        OnPropertyChanged(nameof(SelectedConnectorDashLabel));
        OnPropertyChanged(nameof(SelectedConnectorDash));
        OnPropertyChanged(nameof(NewConnectorDash));
        OnPropertyChanged(nameof(ConnectorTitle));
        OnPropertyChanged(nameof(DeleteConnectorLabel));
        OnPropertyChanged(nameof(IsPlansPanel));
        OnPropertyChanged(nameof(IsPromptPanel));
        OnPropertyChanged(nameof(IsPropsPanel));
        OnPropertyChanged(nameof(IsConnPanel));

        if (!ReadOnly) ScheduleSave();
    }

    partial void OnPanelChanged(string value)
    {
        OnPropertyChanged(nameof(IsPlansPanel));
        OnPropertyChanged(nameof(IsPromptPanel));
        OnPropertyChanged(nameof(IsPropsPanel));
        OnPropertyChanged(nameof(IsConnPanel));
        OnPropertyChanged(nameof(ShowPanel));
    }

    public bool ShowPanel => Panel != "none";
    public bool IsPlansPanel => Panel == "plans";
    public bool IsPromptPanel => Panel == "prompt";
    public bool IsPropsPanel => Panel == "props" && Controller.SelectedIds.Count > 0;
    public bool IsConnPanel => Panel == "conn" && Controller.SelectedConnectorIds.Count > 0;

    public string ConnectorTitle => Controller.SelectedConnectorIds.Count is var n && n > 1
        ? $"{n} connectors"
        : "Connector";

    public string DeleteConnectorLabel => Controller.SelectedConnectorIds.Count > 1
        ? "Delete connectors"
        : "Delete connector";

    public void TogglePlans() => Panel = Panel == "plans" ? "none" : "plans";
    public void TogglePrompt() => Panel = Panel == "prompt" ? "none" : "prompt";
    public void ClosePanel() => Panel = "none";

    // ---------------------------------------------------------------- tools

    public bool ShowShapeFlyout => Controller.CurrentTool == Tool.Shape && !ReadOnly;
    public bool ShowConnFlyout => Controller.CurrentTool == Tool.Connector && !ReadOnly;
    public bool ShowStickyFlyout => Controller.CurrentTool == Tool.Sticky && !ReadOnly;
    public bool ShowEraserFlyout => Controller.CurrentTool == Tool.Eraser && !ReadOnly;
    public string EraserLabel => $"{Controller.EraserSize:0} px";

    public double EraserSize
    {
        get => Controller.EraserSize;
        set => Controller.SetEraserSize(value);
    }

    void BuildTools()
    {
        (string Id, string Icon, string Title, string Hint, bool Gap)[] defs =
        [
            (Tool.Select, Icons.NearMe, "Select (V)", "Drag empty canvas to box-select · Shift-click to add · double-click to edit", false),
            (Tool.Hand, Icons.BackHand, "Pan (H)", "Drag to pan · hold Space with any tool", false),
            (Tool.Pen, Icons.Draw, "Pen (P)", "Drag to draw freehand", true),
            (Tool.Eraser, Icons.InkEraser, "Eraser (E)", "Rub over ink to erase part of a stroke · [ and ] resize the tip", false),
            (Tool.Shape, Icons.Square, "Shape (R)", "Drag to draw · pick a form above", false),
            (Tool.Connector, Icons.ArrowRightAlt, "Connector (X)", "Drag shape to shape, or anywhere on empty canvas · route, ends and dash set above", false),
            (Tool.Sticky, Icons.StickyNote, "Sticky note (N)", "Click to drop a note · pick a colour above", false),
            (Tool.Text, Icons.Title, "Text (T)", "Click to place a text block", false),
            (Tool.Frame, Icons.CropFree, "Frame (F)", "Drag to enclose a section", false),
            (Tool.Table, Icons.TableChart, "Table (B)", "Click to insert a 3×3 table", false),
            (Tool.Prompt, Icons.EditNote, "Prompt card (G)", "Click anywhere to place a prompt card", true),
            (Tool.Vote, Icons.RadioChecked, "Vote dots (D)", "Click objects to add dots", false),
        ];

        foreach (var (id, icon, title, hint, gap) in defs)
            Tools.Add(new ToolButton(id, icon, title, hint, gap, () => Controller.SetTool(id))
            {
                Active = id == Controller.CurrentTool,
            });
    }

    void BuildPalettes()
    {
        (string Id, string Label)[] shapes =
        [
            ("rect", "Rectangle"), ("round", "Rounded"), ("pill", "Pill"), ("ellipse", "Ellipse / circle"),
            ("diamond", "Diamond"), ("triangle", "Triangle"), ("hexagon", "Hexagon"),
            ("custom", "Custom polygon — click points, Enter to close"),
        ];
        foreach (var (id, label) in shapes)
            ShapeKinds.Add(new FlyoutItem(id, label, ShapeGeometry.Path(id, [[0, 0], [1, 0.4], [0.6, 1]], 18, 18),
                () => Controller.SetShapeKind(id)) { Active = id == Controller.ShapeKind });

        (string Id, string Label, string Icon)[] styles =
        [
            ("curve", "Curved", "M 2 14 C 7 14 6 4 18 4"),
            ("elbow", "Elbow", "M 2 14 H 10 V 4 H 18"),
            ("straight", "Straight", "M 2 14 L 18 4"),
        ];
        foreach (var (id, label, icon) in styles)
        {
            ConnectorStyles.Add(new FlyoutItem(id, label, icon, () => Controller.SetConnStyle(id))
            { Active = id == Controller.ConnStyle });
            SelectedStyles.Add(new FlyoutItem(id, label, icon, () => Controller.SetConnectorStyle(id)));
        }

        (string Id, string Short, string Label)[] arrows =
        [
            ("end", "→", "Arrow at end"), ("both", "↔", "Arrows both ends"), ("none", "—", "No arrows"),
        ];
        foreach (var (id, shortLabel, label) in arrows)
        {
            ArrowModes.Add(new FlyoutItem(id, label, "", () => Controller.SetConnArrows(id))
            { Active = id == Controller.ConnArrows, Text = shortLabel });
            SelectedArrows.Add(new FlyoutItem(id, label, "", () => Controller.SetConnectorArrows(id))
            { Text = shortLabel });
        }

        foreach (var c in StickyColors)
            StickySwatches.Add(new Swatch(c, () => Controller.SetStickyColor(c)) { Active = c == Controller.StickyColor });

        foreach (var c in Fills)
            FillSwatches.Add(new Swatch(c, () => Controller.SetFill(c)));

        foreach (var c in TextColors)
            TextSwatches.Add(new Swatch(c, () => Controller.SetTextColor(c)));
    }

    // ---------------------------------------------------------------- props panel

    public bool IsMultiSelection => Controller.SelectedIds.Count > 1;

    public string SelectionTitle
    {
        get
        {
            if (Controller.SingleSelection is { } o)
                return o.Kind switch
                {
                    ObjKind.Sticky => "Sticky note",
                    ObjKind.Shape => "Shape",
                    ObjKind.Text => "Text",
                    ObjKind.Frame => "Frame",
                    ObjKind.Table => "Table",
                    ObjKind.Prompt => "Prompt card",
                    _ => "Object",
                };
            return $"{Controller.SelectedIds.Count} objects";
        }
    }

    public int SelectionVotes => Controller.SingleSelection?.Votes
        ?? Controller.SelectedObjects.Sum(o => o.Votes);

    /// <summary>No explicit text colour: the label follows the fill on its own.</summary>
    public bool TextColorIsAuto => Controller.SelectedObjects.All(o => string.IsNullOrEmpty(o.TextColor));

    public void ResetTextColor() => Controller.SetTextColor(null);

    public bool SelectedConnectorDash => Controller.SelectedConnectors.FirstOrDefault()?.Dash == true;

    public string SelectedConnectorDashLabel => SelectedConnectorDash ? "Dashed" : "Solid";

    /// <summary>Whether the next connector you draw will be dashed.</summary>
    public bool NewConnectorDash => Controller.ConnDash;

    // ---------------------------------------------------------------- plans

    void RebuildPlans()
    {
        Plans.Clear();
        foreach (var plan in Session.Plans)
            Plans.Add(new PlanCard(plan, this));
    }

    public void OpenPlan(PlanCard card)
    {
        if (CurrentPlan == card)
        {
            Panel = "prompt";
            return;
        }
        CurrentPlan = card;
        Controller.LoadDoc(card.Plan.Doc);
        OnPropertyChanged(nameof(PlanName));
        OnPropertyChanged(nameof(PlanMeta));
        RefreshPrompts();
        foreach (var p in Plans) p.Refresh();
    }

    public void NewPlan()
    {
        if (ReadOnly) return;
        var plan = new Plan
        {
            Id = Ids.New("pl"),
            Name = "Untitled plan",
            Desc = "Empty canvas — drop a frame or a prompt to start.",
            Status = "draft",
            Dot = "#eae7e1",
            UpdatedUtc = DateTime.UtcNow,
        };
        Session.Plans.Insert(0, plan);
        RebuildPlans();
        OpenPlan(Plans[0]);
        Save();
    }

    public void ToggleDone(PlanCard card)
    {
        if (ReadOnly) return;
        card.Plan.Done = !card.Plan.Done;
        card.Refresh();
        Save();
    }

    public void RenamePlan(PlanCard card, string name)
    {
        if (ReadOnly || string.IsNullOrWhiteSpace(name)) return;
        card.Plan.Name = name.Trim();
        card.Refresh();
        OnPropertyChanged(nameof(PlanName));
        Save();
    }

    void RefreshPrompts()
    {
        Prompts.Clear();
        foreach (var o in Controller.Doc.Objs.Where(o => o.Kind == ObjKind.Prompt))
            Prompts.Add(new PromptCard(o, this));
        OnPropertyChanged(nameof(PromptCountLabel));
        OnPropertyChanged(nameof(HasPrompts));
    }

    public bool HasPrompts => Prompts.Count > 0;

    public void FocusPrompt(PromptCard card)
    {
        var cam = Controller.Doc.Cam;
        cam.X = Controller.ViewportWidth / 2 - (card.Object.X + card.Object.W / 2) * cam.Z;
        cam.Y = Controller.ViewportHeight / 2 - (card.Object.Y + 52) * cam.Z;
        Controller.Select([card.Object.Id]);
    }

    public void DeletePrompt(PromptCard card)
    {
        if (ReadOnly) return;
        Controller.Select([card.Object.Id]);
        Controller.DeleteSelection();
    }

    public void AddPrompt() => Controller.SetTool(Tool.Prompt);

    // ---------------------------------------------------------------- saving

    void ScheduleSave()
    {
        _autosave.Stop();
        _autosave.Start();
    }

    public void Save()
    {
        if (ReadOnly) return;
        if (CurrentPlan is { } card) card.Plan.UpdatedUtc = DateTime.UtcNow;
        _runtime?.SaveNow();
    }

    public void OnBack() => Back?.Invoke();
    public void OnContinue() => Continue?.Invoke(Session);
    public void OnEnd() { Save(); _runtime?.End(EndReason.Completed); }

    public void Dispose()
    {
        _autosave.Stop();
        Controller.Changed -= OnBoardChanged;
        if (_runtime is not null) _runtime.PropertyChanged -= OnRuntimeChanged;
    }
}

public sealed partial class ToolButton(string id, string icon, string title, string hint, bool gap, Action pick)
    : ObservableObject
{
    public string Id { get; } = id;
    public string Icon { get; } = icon;
    public string Title { get; } = title;
    public string HintText { get; } = hint;
    public double GapLeft { get; } = gap ? 9 : 0;
    public Action Pick { get; } = pick;
    [ObservableProperty] bool _active;
}

public sealed partial class FlyoutItem(string id, string label, string icon, Action pick) : ObservableObject
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public string Icon { get; } = icon;
    public string Text { get; set; } = "";
    public Action Pick { get; } = pick;
    [ObservableProperty] bool _active;
}

public sealed partial class Swatch(string color, Action pick) : ObservableObject
{
    public string Color { get; } = color;
    public Action Pick { get; } = pick;
    [ObservableProperty] bool _active;
}

public sealed partial class PlanCard(Plan plan, WhiteboardViewModel owner) : ObservableObject
{
    public Plan Plan { get; } = plan;

    public string Name => Plan.Name;
    public string Desc => Plan.Desc;
    public string Dot => Plan.Dot;
    public bool Done => Plan.Done;
    public string Status => Plan.Done ? "done" : Plan.Status;
    public string Items => $"{Plan.Doc.Objs.Count}";
    public string PromptCount => $"{Plan.Doc.Objs.Count(o => o.Kind == ObjKind.Prompt)}";
    public string Updated => Ago(Plan.UpdatedUtc);
    public bool IsCurrent => owner.CurrentPlan == this;

    public void Open() => owner.OpenPlan(this);
    public void Toggle() => owner.ToggleDone(this);
    public void Rename(string name) => owner.RenamePlan(this, name);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Done));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(PromptCount));
        OnPropertyChanged(nameof(Updated));
        OnPropertyChanged(nameof(IsCurrent));
    }

    static string Ago(DateTime utc)
    {
        if (utc == default) return "—";
        var span = DateTime.UtcNow - utc;
        if (span.TotalMinutes < 1) return "now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h";
        return $"{(int)span.TotalDays}d";
    }
}

public sealed class PromptCard(BoardObject obj, WhiteboardViewModel owner)
{
    public BoardObject Object { get; } = obj;
    public string Text => Object.Text;
    public string Time => Object.T ?? "now";

    public void Focus() => owner.FocusPrompt(this);
    public void Delete() => owner.DeletePrompt(this);
}
