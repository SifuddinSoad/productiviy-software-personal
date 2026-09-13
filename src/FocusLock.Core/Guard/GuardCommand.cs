using FocusLock.Core.Models;

namespace FocusLock.Core.Guard;

public static class GuardCommandKind
{
    public const string Begin = "begin";
    public const string Checkpoint = "checkpoint";
    public const string End = "end";
}

/// <summary>
/// What the app tells the service: a session started, time has moved on, or it is over. Dropped in
/// the inbox as a file rather than sent down a pipe, so a command survives the service restarting.
/// </summary>
public sealed class GuardCommand
{
    public string Kind { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string UserSid { get; set; } = "";
    public string AppPath { get; set; } = "";
    public double PlannedSeconds { get; set; }
    public SessionClockState Clock { get; set; } = new();
    public DateTime SentUtc { get; set; }
}

public static class GuardCommands
{
    /// <summary>
    /// Folds one command into what the service knows. A command naming a different session than the
    /// one being guarded is ignored: a second app cannot take over or end someone else's lock.
    /// </summary>
    public static GuardState? Apply(GuardState? current, GuardCommand command) => command.Kind switch
    {
        GuardCommandKind.Begin => new GuardState
        {
            SessionId = command.SessionId,
            UserSid = command.UserSid,
            AppPath = command.AppPath,
            PlannedSeconds = command.PlannedSeconds,
            Clock = command.Clock,
        },

        GuardCommandKind.Checkpoint when Matches(current, command) => new GuardState
        {
            SessionId = current!.SessionId,
            UserSid = current.UserSid,
            AppPath = string.IsNullOrEmpty(command.AppPath) ? current.AppPath : command.AppPath,
            PlannedSeconds = command.PlannedSeconds,
            Clock = command.Clock,
        },

        GuardCommandKind.End when Matches(current, command) => null,

        _ => current,
    };

    static bool Matches(GuardState? state, GuardCommand command) =>
        state is not null && state.SessionId == command.SessionId;
}
