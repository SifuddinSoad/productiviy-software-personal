using CommunityToolkit.Mvvm.ComponentModel;
using FocusLock.App.Services;
using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.App.ViewModels;

/// <summary>
/// The canvas screen. Phase 1b fills in the full board (tools, objects, panels); for now it hosts
/// the top bar, timer and plan switching so the navigation and read-only paths are exercisable.
/// </summary>
public sealed partial class WhiteboardViewModel : ObservableObjectBase, IDisposable
{
    readonly SessionRuntime? _runtime;

    public Session Session { get; }
    public bool ReadOnly { get; }
    public bool CanContinue { get; }

    [ObservableProperty] string _timerLabel;
    [ObservableProperty] bool _isFinished;
    [ObservableProperty] Plan? _currentPlan;

    public event Action? Back;
    public event Action<Session>? Continue;

    public WhiteboardViewModel(Session session, SessionRuntime? runtime, bool readOnly)
    {
        Session = session;
        ReadOnly = readOnly;
        _runtime = readOnly ? null : runtime;
        CanContinue = readOnly && Progress.CanContinue(session);
        CurrentPlan = session.Plans.FirstOrDefault();

        _timerLabel = readOnly ? Progress.StateLabel(session) : runtime!.TimerLabel;
        _isFinished = !readOnly && runtime!.IsFinished;
        if (_runtime is not null) _runtime.PropertyChanged += OnRuntimeChanged;
    }

    public string PlanName => CurrentPlan?.Name ?? Session.Name;
    public bool TimerRunning => !ReadOnly && !IsFinished;

    void OnRuntimeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        TimerLabel = _runtime!.TimerLabel;
        IsFinished = _runtime.IsFinished;
        OnPropertyChanged(nameof(TimerRunning));
    }

    public void OnBack() => Back?.Invoke();
    public void OnContinue() => Continue?.Invoke(Session);
    public void OnEnd() => _runtime?.End(EndReason.Completed);

    public void Dispose()
    {
        if (_runtime is not null) _runtime.PropertyChanged -= OnRuntimeChanged;
    }
}
