using FocusLock.App.Guard;
using FocusLock.App.Lock;
using FocusLock.Core;
using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.App;

/// <summary>
/// Removes every trace of a lock: startup entry, Task Manager policy, active-session pointer.
/// Run with <c>FocusLock.App.exe --cleanup</c> (works in Safe Mode, where Run entries do not start).
/// Session data is kept; an unfinished active session is marked as ended early.
/// </summary>
internal static class Cleanup
{
    public static List<string> Run()
    {
        var log = new List<string>();

        StartupRegistration.Unregister();
        log.Add("Startup entry removed");

        var active = new ActiveSessionStore(AppPaths.ActiveFile);
        var pointer = active.Get();

        if (pointer is { TaskMgrWasDisabled: true })
            log.Add("Task Manager policy was set before FocusLock, left unchanged");
        else
            log.Add(TaskManagerPolicy.TryRestore() ? "Task Manager policy removed" : "Could not remove Task Manager policy");

        if (pointer is not null)
        {
            var store = new SessionStore(AppPaths.SessionsDir);
            if (store.Load(pointer.SessionId) is { IsEnded: false } session)
            {
                session.EndedUtc = DateTime.UtcNow;
                session.EndReason = EndReason.Emergency;
                store.Save(session);
                log.Add($"Session \"{session.Name}\" marked as ended early");
            }
            GuardClient.EndById(pointer.SessionId);
            active.Clear();
            log.Add("Active session cleared");
        }

        // Without administrator rights the command left in the inbox above is the whole story: the
        // service picks it up on the next normal boot and lets go before anything locks again.
        if (GuardClient.IsGuarding())
            log.Add(GuardClient.TryClearState()
                ? "Guard service state cleared"
                : "Guard service asked to let go on its next start");

        return log;
    }
}
