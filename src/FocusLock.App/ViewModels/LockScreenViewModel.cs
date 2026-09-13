using CommunityToolkit.Mvvm.ComponentModel;
using FocusLock.App.Services;
using FocusLock.Core.Models;

namespace FocusLock.App.ViewModels;

/// <summary>Shown when a locked session has no plans yet: just the timer, plus a way to start a plan.</summary>
public sealed partial class LockScreenViewModel : ObservableObjectBase, IDisposable
{
    readonly SessionRuntime _runtime;

    [ObservableProperty] string _timerLabel;
    [ObservableProperty] bool _isFinished;

    public string SessionName { get; }
    public event Action? MakePlan;
    public event Action? Ended;

    public LockScreenViewModel(Session session, SessionRuntime runtime)
    {
        _runtime = runtime;
        SessionName = session.Name;
        _timerLabel = runtime.TimerLabel;
        _isFinished = runtime.IsFinished;
        runtime.PropertyChanged += OnRuntimeChanged;
    }

    void OnRuntimeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        TimerLabel = _runtime.TimerLabel;
        IsFinished = _runtime.IsFinished;
    }

    public void OnMakePlan() => MakePlan?.Invoke();
    public void OnEnd() => Ended?.Invoke();

    public void Dispose() => _runtime.PropertyChanged -= OnRuntimeChanged;
}
