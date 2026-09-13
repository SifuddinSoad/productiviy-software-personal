using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.App.ViewModels;

public sealed partial class HomeViewModel : ObservableObjectBase
{
    readonly SessionStore _store;

    [ObservableProperty] bool _confirmingClearAll;

    public string TodayLabel { get; } = DateTime.Now.ToString("ddd, MMM d");
    public ObservableCollection<SessionRow> Sessions { get; }

    public event Action? NewSession;
    public event Action<Session>? OpenSession;

    public HomeViewModel(SessionStore store)
    {
        _store = store;
        Sessions = [.. store.List().Select(Row)];
    }

    SessionRow Row(Session session) =>
        new(session, () => OpenSession?.Invoke(session), Delete);

    public string SessionCount => $"{Sessions.Count} saved";
    public bool HasSessions => Sessions.Count > 0;

    public string ClearAllPrompt => Sessions.Count == 1
        ? "Delete this session?"
        : $"Delete all {Sessions.Count} sessions?";

    public void StartNew() => NewSession?.Invoke();

    public void AskClearAll() => ConfirmingClearAll = true;
    public void CancelClearAll() => ConfirmingClearAll = false;

    public void ClearAll()
    {
        foreach (var row in Sessions) _store.Delete(row.Id);
        Sessions.Clear();
        ConfirmingClearAll = false;
        Changed();
    }

    void Delete(SessionRow row)
    {
        _store.Delete(row.Id);
        Sessions.Remove(row);
        Changed();
    }

    void Changed()
    {
        OnPropertyChanged(nameof(SessionCount));
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(ClearAllPrompt));
    }
}

public sealed partial class SessionRow(Session session, Action open, Action<SessionRow> delete) : ObservableObject
{
    /// <summary>Deleting loses the canvases with it, so the row asks first.</summary>
    [ObservableProperty] bool _confirmingDelete;

    public string Id => session.Id;
    public string Name => session.Name;
    public string State => Progress.StateLabel(session);
    public Action Open { get; } = open;

    public void AskDelete() => ConfirmingDelete = true;
    public void CancelDelete() => ConfirmingDelete = false;
    public void Delete() => delete(this);

    public string Range
    {
        get
        {
            var start = session.StartUtc.ToLocalTime();
            var end = (session.EndedUtc ?? session.StartUtc.AddSeconds(session.PlannedSeconds)).ToLocalTime();
            return $"{start:h:mm tt}  ›  {end:h:mm tt}";
        }
    }

    public string Hours
    {
        get
        {
            var h = session.PlannedSeconds / 3600.0;
            return h < 1 ? $"{session.PlannedSeconds / 60} min" : $"{h:0.##} h";
        }
    }

    public string PlanLine => session.Plans.Count == 1 ? "1 plan" : $"{session.Plans.Count} plans";

    int? Percent => Progress.Percent(session);
    public bool HasRing => Percent is not null;
    public string PctLabel => Percent is { } p ? $"{p}%" : "—";

    public string RingColor => Percent switch
    {
        null => "#7f8489",
        >= 100 => "#8fd18a",
        >= 60 => "#e9e9e7",
        _ => "#ff8a6b",
    };

    public bool IsComplete => Percent >= 100;

    /// <summary>SVG arc path for the progress ring: r=14 centred at (17,17), starting at 12 o'clock.</summary>
    public string ArcData
    {
        get
        {
            double pct = Math.Clamp(Percent ?? 0, 0, 100);
            if (pct <= 0) return "";
            const double cx = 17, cy = 17, r = 14;
            if (pct >= 100) pct = 99.999;
            var end = -90 + 360.0 * pct / 100.0;
            double Rad(double d) => d * Math.PI / 180.0;
            var x = cx + r * Math.Cos(Rad(end));
            var y = cy + r * Math.Sin(Rad(end));
            var large = pct > 50 ? 1 : 0;
            return $"M {cx:0.##},{cy - r:0.##} A {r},{r} 0 {large} 1 {x:0.###},{y:0.###}";
        }
    }
}
