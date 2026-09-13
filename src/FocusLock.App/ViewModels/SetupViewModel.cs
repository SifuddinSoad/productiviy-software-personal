using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FocusLock.App.Guard;
using FocusLock.App.Services;
using FocusLock.Core;
using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.App.ViewModels;

public sealed partial class SetupViewModel : ObservableObjectBase
{
    [ObservableProperty] string _name = "";
    [ObservableProperty] bool _isCountingDown;
    [ObservableProperty] int _countdownLeft;

    // The length is a clock: three fields the user types into directly.
    [ObservableProperty] string _hours = "01";
    [ObservableProperty] string _minutes = "00";
    [ObservableProperty] string _seconds = "00";

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
        foreach (var minutes in Durations.PresetMinutes)
            DurationChips.Add(new DurationChip(minutes, () => SetClock(minutes * 60)));
        foreach (var p in _seededPlans)
            Plans.Add(new PlanRow(p.Name, RemovePlan));
        RefreshClock();
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

    /// <summary>Fills the clock from a length in seconds (used by the preset chips).</summary>
    public void SetClock(int seconds)
    {
        seconds = Math.Clamp(seconds, 0, Durations.MaxSeconds);
        Hours = (seconds / 3600).ToString("00");
        Minutes = (seconds % 3600 / 60).ToString("00");
        Seconds = (seconds % 60).ToString("00");
    }

    /// <summary>Pads each field to two digits once the user leaves it.</summary>
    public void NormaliseClock()
    {
        Hours = Part(Hours, Durations.MaxHours).ToString("00");
        Minutes = Part(Minutes, 59).ToString("00");
        Seconds = Part(Seconds, 59).ToString("00");
    }

    /// <summary>Adds to one field and carries nothing — each field stands on its own.</summary>
    public void Nudge(string field, int delta)
    {
        switch (field)
        {
            case "h": Hours = Wrap(Part(Hours, Durations.MaxHours) + delta, Durations.MaxHours).ToString("00"); break;
            case "m": Minutes = Wrap(Part(Minutes, 59) + delta, 59).ToString("00"); break;
            default: Seconds = Wrap(Part(Seconds, 59) + delta, 59).ToString("00"); break;
        }
    }

    static int Wrap(int value, int max) => value < 0 ? max : value > max ? 0 : value;

    static int Part(string text, int max) =>
        int.TryParse(text.Trim(), out var v) ? Math.Clamp(v, 0, max) : 0;

    partial void OnHoursChanged(string value) => RefreshClock();
    partial void OnMinutesChanged(string value) => RefreshClock();
    partial void OnSecondsChanged(string value) => RefreshClock();

    void RefreshClock()
    {
        foreach (var c in DurationChips) c.Active = c.Minutes * 60 == SelectedSeconds;
        OnPropertyChanged(nameof(SelectedSeconds));
        OnPropertyChanged(nameof(TimeRange));
        OnPropertyChanged(nameof(DurationLabel));
        OnPropertyChanged(nameof(DurationIsValid));
        OnPropertyChanged(nameof(CanStart));
    }

    /// <summary>How long the session will run, straight from the clock.</summary>
    public int SelectedSeconds =>
        Part(Hours, Durations.MaxHours) * 3600 + Part(Minutes, 59) * 60 + Part(Seconds, 59);

    public bool DurationIsValid => SelectedSeconds is >= 1 && SelectedSeconds <= Durations.MaxSeconds;

    public string DurationHint => $"up to {Durations.MaxHours} hours";

    public string TimeRange
    {
        get
        {
            if (!DurationIsValid) return "—";
            var now = DateTime.Now;
            var end = now.AddSeconds(SelectedSeconds);
            return $"{now:h:mm tt}  ›  {end:h:mm tt}";
        }
    }

    /// <summary>
    /// Running from a shared or network folder means Windows cannot start the app at logon, so a
    /// restart would leave the machine unlocked. Worth saying plainly before a session begins.
    /// </summary>
    public bool WarnAboutLocation => !AppLocation.CanResumeAfterRestart;

    public string LocationWarning =>
        "Running from a network folder, so this session will not come back after a restart. " +
        "Install it on this PC first (scripts\\install.ps1).";

    /// <summary>
    /// Without the guard service the desktop is usable for a few seconds after sign-in, before the
    /// logon entry starts the app. Worth knowing which of the two you are running with.
    /// </summary>
    public bool GuardIsOn { get; } = GuardClient.IsRunning();

    public string GuardLabel => GuardIsOn
        ? "Guard service on — a session comes back the moment you sign in"
        : "Guard service off — after a restart the desktop is briefly usable before the lock returns";

    public string DurationLabel => DurationIsValid ? Durations.Describe(SelectedSeconds) : "—";
    public string PlanCountLabel => Plans.Count == 1 ? "1 in list" : $"{Plans.Count} in list";
    public bool CanStart => Name.Trim().Length > 0 && DurationIsValid;

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
            PlannedSeconds = Math.Min(SelectedSeconds, Durations.MaxSeconds),
            StartUtc = DateTime.UtcNow,
            Plans = plans,
        };
    }
}

public sealed partial class DurationChip(int minutes, Action pick) : ObservableObject
{
    public int Minutes { get; } = minutes;
    public string Label { get; } = Durations.ChipLabel(minutes);
    public Action Pick { get; } = pick;
    [ObservableProperty] bool _active;
}

public sealed partial class PlanRow(string title, Action<PlanRow> remove) : ObservableObject
{
    [ObservableProperty] string _title = title;
    public Action Remove => () => remove(this);
}
