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
            active.Clear();
            log.Add("Active session cleared");
        }

        return log;
    }
}
