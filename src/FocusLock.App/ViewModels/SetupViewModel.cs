using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FocusLock.App.Services;
using FocusLock.Core;
using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.App.ViewModels;

public sealed partial class SetupViewModel : ObservableObjectBase
{
    [ObservableProperty] string _name = "";
    [ObservableProperty] int _durationOption = Durations.DefaultOption;
    [ObservableProperty] bool _isCustomDuration;
    [ObservableProperty] string _customDuration = "";
    [ObservableProperty] bool _isCountingDown;
    [ObservableProperty] int _countdownLeft;

    DispatcherTimer? _countdown;

    public ObservableCollection<DurationChip> DurationChips { get; } = [];
    public ObservableCollection<PlanRow> Plans { get; } = [];

    /// <summary>Plans copied from a previous session; kept so the new session reuses their canvases.</summary>
    readonly List<Plan> _seededPlans;

    public event Action? Cancelled;
    public event Action<Session>? Confirmed;

    SetupViewModel(IEnumerable<Plan> seededPlans)
    {
        _seededPlans = [.. seededPlans];
        foreach (var opt in Durations.Options)
            DurationChips.Add(new DurationChip(opt, () => PickPreset(opt)));
        foreach (var p in _seededPlans)
            Plans.Add(new PlanRow(p.Name, RemovePlan));
        RefreshChips();
    }

    public static SetupViewModel ForNew() => new([]);

    public static SetupViewModel ForContinue(Session previous)
    {
        var remaining = previous.Plans.Where(p => !p.Done).Select(PlanCopy.ForNewSession).ToList();
        var vm = new SetupViewModel(remaining) { Name = ContinueName(previous.Name) };
        return vm;
    }

    static string ContinueName(string previous) =>
        previous.StartsWith("Continue: ", StringComparison.Ordinal) ? previous : $"Continue: {previous}";

    void PickPreset(int option)
    {
        IsCustomDuration = false;
        DurationOption = option;
        RefreshChips();
    }

    public void UseCustomDuration()
    {
        IsCustomDuration = true;
        if (CustomDuration.Length == 0) CustomDuration = Durations.DefaultOption.ToString();
        RefreshChips();
    }

    partial void OnDurationOptionChanged(int value) => RefreshChips();
    partial void OnCustomDurationChanged(string value) => RefreshChips();
    partial void OnIsCustomDurationChanged(bool value) => RefreshChips();

    void RefreshChips()
    {
        foreach (var c in DurationChips) c.Active = !IsCustomDuration && c.Option == DurationOption;
        OnPropertyChanged(nameof(TimeRange));
        OnPropertyChanged(nameof(DurationLabel));
        OnPropertyChanged(nameof(CustomIsValid));
        OnPropertyChanged(nameof(CanStart));
    }

    /// <summary>The typed number, or 0 when it is not a number at all.</summary>
    int CustomUnits => int.TryParse(CustomDuration.Trim(), out var v) ? v : 0;

    public bool CustomIsValid => !IsCustomDuration || Durations.IsValidCustom(CustomUnits);

    public string CustomUnitLabel => Durations.UnitLabel;
    public string CustomHint => $"1 – {Durations.MaxUnits} {Durations.UnitLabel}";

    /// <summary>How long the session will run, from whichever way the length was chosen.</summary>
    public int SelectedSeconds =>
        Durations.ToSeconds(IsCustomDuration ? CustomUnits : DurationOption);

    public string TimeRange
    {
        get
        {
            if (!CustomIsValid) return "—";
            var now = DateTime.Now;
            var end = now.AddSeconds(SelectedSeconds);
            return $"{now:h:mm tt}  ›  {end:h:mm tt}";
        }
    }

    public string DurationLabel => CustomIsValid ? Durations.Describe(SelectedSeconds) : "—";
    public string PlanCountLabel => Plans.Count == 1 ? "1 in list" : $"{Plans.Count} in list";
    public bool CanStart => Name.Trim().Length > 0 && CustomIsValid && SelectedSeconds > 0;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(CanStart));

    public void AddPlan()
    {
        Plans.Add(new PlanRow("", RemovePlan));
        OnPropertyChanged(nameof(PlanCountLabel));
    }

    void RemovePlan(PlanRow row)
    {
        Plans.Remove(row);
        OnPropertyChanged(nameof(PlanCountLabel));
    }

    public void Cancel()
    {
        StopCountdown();
        Cancelled?.Invoke();
    }

    /// <summary>A 5-second "save your work" grace window before the lock starts. Cancellable.</summary>
    public void Start()
    {
        if (!CanStart || IsCountingDown) return;
        CountdownLeft = 5;
        IsCountingDown = true;
        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) =>
        {
            CountdownLeft--;
            if (CountdownLeft <= 0)
            {
                StopCountdown();
                Confirmed?.Invoke(BuildSession());
            }
        };
        _countdown.Start();
    }

    public void CancelCountdown() => StopCountdown();

    void StopCountdown()
    {
        _countdown?.Stop();
        _countdown = null;
        IsCountingDown = false;
    }

    Session BuildSession()
    {
        var seededByName = _seededPlans.ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);
        var plans = new List<Plan>();
        foreach (var row in Plans)
        {
            var title = row.Title.Trim();
            if (title.Length == 0) continue;
            if (seededByName.Remove(title, out var seeded))
            {
                seeded.Name = title;
                plans.Add(seeded);
            }
            else
            {
                plans.Add(new Plan
                {
                    Id = Ids.New("pl"),
                    Name = title,
                    Desc = "",
                    Status = "draft",
                    Dot = "#eae7e1",
                    UpdatedUtc = DateTime.UtcNow,
                    Doc = new BoardDoc(),
                });
            }
        }

        return new Session
        {
            Id = Ids.New("s"),
            Name = Name.Trim(),
            PlannedSeconds = (int)Math.Min(SelectedSeconds, LockSafety.MaxSessionSeconds),
            StartUtc = DateTime.UtcNow,
            Plans = plans,
        };
    }
}

public sealed partial class DurationChip(int option, Action pick) : ObservableObject
{
    public int Option { get; } = option;
    public string Label { get; } = Durations.ChipLabel(option);
    public Action Pick { get; } = pick;
    [ObservableProperty] bool _active;
}

public sealed partial class PlanRow(string title, Action<PlanRow> remove) : ObservableObject
{
    [ObservableProperty] string _title = title;
    public Action Remove => () => remove(this);
}
