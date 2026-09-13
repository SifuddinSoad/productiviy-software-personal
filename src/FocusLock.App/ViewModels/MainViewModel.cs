using CommunityToolkit.Mvvm.ComponentModel;
using FocusLock.App.Services;
using FocusLock.Core;
using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.App.ViewModels;

/// <summary>Owns navigation between screens and the shared session runtime.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    readonly SessionStore _store;

    [ObservableProperty] ObservableObjectBase _current = null!;

    public SessionRuntime Runtime { get; }

    public MainViewModel(SessionStore store, ActiveSessionStore active)
    {
        _store = store;
        Runtime = new SessionRuntime(store, active);
        Runtime.Ended += _ => GoHome();
    }

    public void Startup(bool resume)
    {
        if (resume && Runtime.TryResume())
        {
            OpenActiveBoard();
            return;
        }
        GoHome();
    }

    public void GoHome()
    {
        var vm = new HomeViewModel(_store.List());
        vm.NewSession += () => GoSetup(SetupViewModel.ForNew());
        vm.OpenSession += OpenPrevious;
        Current = vm;
    }

    public void GoSetup(SetupViewModel vm)
    {
        vm.Cancelled += GoHome;
        vm.Confirmed += StartSession;
        Current = vm;
    }

    void OpenPrevious(Session session)
    {
        var vm = new WhiteboardViewModel(session, Runtime, readOnly: true);
        vm.Back += GoHome;
        vm.Continue += s => GoSetup(SetupViewModel.ForContinue(s));
        Current = vm;
    }

    void StartSession(Session session)
    {
        Runtime.Start(session);
        OpenActiveBoard();
    }

    void OpenActiveBoard()
    {
        var session = Runtime.Session!;
        if (session.Plans.Count == 0)
        {
            var lockVm = new LockScreenViewModel(session, Runtime);
            lockVm.MakePlan += () => { AddFirstPlan(session); OpenActiveBoard(); };
            lockVm.Ended += () => Runtime.End(EndReason.Completed);
            Current = lockVm;
            return;
        }

        var vm = new WhiteboardViewModel(session, Runtime, readOnly: false);
        Current = vm;
    }

    void AddFirstPlan(Session session)
    {
        session.Plans.Add(new Plan
        {
            Id = Ids.New("pl"),
            Name = "Untitled plan",
            Desc = "",
            Status = "draft",
            Dot = "#eae7e1",
            UpdatedUtc = DateTime.UtcNow,
            Doc = new BoardDoc(),
        });
        Runtime.SaveNow();
    }
}

/// <summary>Marker base so the navigation host can bind any screen VM.</summary>
public abstract class ObservableObjectBase : ObservableObject;
