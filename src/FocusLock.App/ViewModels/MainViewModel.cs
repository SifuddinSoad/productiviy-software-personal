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

    /// <summary>
    /// An unfinished session always resumes, however the app was started — otherwise closing and
    /// reopening would be a way out of a locked session. Anything left unfinished by an earlier
    /// crash is closed out so it stops reading as active.
    /// </summary>
    public void Startup()
    {
        if (Runtime.TryResume())
        {
            _store.CloseUnfinished(Runtime.Session!.Id);
            OpenActiveBoard();
            return;
        }
        _store.CloseUnfinished(null);
        GoHome();
    }

    public void GoHome()
    {
        var vm = new HomeViewModel(_store);
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
        var vm = new WhiteboardViewModel(session, Runtime, readOnly: true)
        {
            // the canvas stays untouched, but the extract list is worth keeping
            SaveSession = () => _store.Save(session),
        };
        vm.Back += GoHome;
        vm.Continue += s => GoSetup(SetupViewModel.ForContinue(s));
        Current = vm;
    }

    void StartSession(Session session)
    {
        Runtime.Start(session);
        OpenActiveBoard();
    }

    /// <summary>Shows the emergency dialog and reports whether the code was typed correctly.</summary>
    public Func<bool>? EmergencyExitRequested { get; set; }

    void OpenActiveBoard()
    {
        var session = Runtime.Session!;
        if (session.Plans.Count == 0)
        {
            var lockVm = new LockScreenViewModel(session, Runtime);
            lockVm.MakePlan += () => { AddFirstPlan(session); OpenActiveBoard(); };
            lockVm.Ended += () => Runtime.End(EndReason.Completed);
            lockVm.EmergencyExit += TryEmergencyExit;
            Current = lockVm;
            return;
        }

        var vm = new WhiteboardViewModel(session, Runtime, readOnly: false);
        vm.EmergencyExit += TryEmergencyExit;
        Current = vm;
    }

    void TryEmergencyExit()
    {
        if (EmergencyExitRequested?.Invoke() == true)
            Runtime.End(EndReason.Emergency);
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
