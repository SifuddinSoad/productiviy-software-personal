using System.IO;
using System.Security.Principal;
using System.ServiceProcess;
using FocusLock.Core.Guard;
using FocusLock.Core.Models;

namespace FocusLock.App.Guard;

/// <summary>
/// The app's side of the guard. Everything here is best-effort: the service is an extra layer, and
/// a session must run exactly as before on a machine where it was never installed.
/// </summary>
internal static class GuardClient
{
    static readonly GuardStore Store = new();

    /// <summary>Whether the guard service is installed and running, for the message on the start screen.</summary>
    public static bool IsRunning()
    {
        try
        {
            using var service = new ServiceController(GuardPaths.ServiceName);
            return service.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            return false;   // not installed
        }
    }

    public static void Begin(Session session) => Send(GuardCommandKind.Begin, session);

    public static void Checkpoint(Session session) => Send(GuardCommandKind.Checkpoint, session);

    public static void End(Session session) => Send(GuardCommandKind.End, session);

    /// <summary>
    /// Asks the guard to let go of a session by id, for <c>--cleanup</c>, which has a pointer but no
    /// loaded session. In Safe Mode the service is not running to read it — the command waits in the
    /// inbox and is picked up on the next normal boot, before anything is locked again.
    /// </summary>
    public static bool EndById(string sessionId) => Store.Send(new GuardCommand
    {
        Kind = GuardCommandKind.End,
        SessionId = sessionId,
        SentUtc = DateTime.UtcNow,
    });

    /// <summary>Whether the service is holding a session right now.</summary>
    public static bool IsGuarding()
    {
        try
        {
            return Store.Read() is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Clears the guard outright. Only possible with administrator rights, which is the point.</summary>
    public static bool TryClearState()
    {
        try
        {
            Store.Clear();
            return Store.Read() is null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static void Send(string kind, Session session) => Store.Send(new GuardCommand
    {
        Kind = kind,
        SessionId = session.Id,
        UserSid = CurrentSid(),
        AppPath = Environment.ProcessPath ?? "",
        PlannedSeconds = session.PlannedSeconds,
        Clock = session.Clock,
        SentUtc = DateTime.UtcNow,
    });

    static string CurrentSid()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? "";
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "";
        }
    }
}
