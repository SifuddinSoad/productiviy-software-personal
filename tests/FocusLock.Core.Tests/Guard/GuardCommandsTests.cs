using FocusLock.Core.Guard;
using FocusLock.Core.Models;

namespace FocusLock.Core.Tests.Guard;

public class GuardCommandsTests
{
    static GuardCommand Command(string kind, string sessionId, double planned = 3600, double elapsed = 0) => new()
    {
        Kind = kind,
        SessionId = sessionId,
        UserSid = "S-1-5-21-1",
        AppPath = @"C:\FocusLock\FocusLock.App.exe",
        PlannedSeconds = planned,
        Clock = new SessionClockState { ConfirmedElapsedSec = elapsed, LastCheckpointUtc = DateTime.UtcNow },
        SentUtc = DateTime.UtcNow,
    };

    [Fact]
    public void Begin_starts_guarding_a_session()
    {
        var state = GuardCommands.Apply(null, Command(GuardCommandKind.Begin, "s1"));

        Assert.NotNull(state);
        Assert.Equal("s1", state.SessionId);
        Assert.Equal(@"C:\FocusLock\FocusLock.App.exe", state.AppPath);
    }

    [Fact]
    public void Checkpoint_moves_the_clock_on()
    {
        var state = GuardCommands.Apply(null, Command(GuardCommandKind.Begin, "s1"));
        state = GuardCommands.Apply(state, Command(GuardCommandKind.Checkpoint, "s1", elapsed: 120));

        Assert.Equal(120, state!.Clock.ConfirmedElapsedSec);
    }

    [Fact]
    public void End_stops_guarding()
    {
        var state = GuardCommands.Apply(null, Command(GuardCommandKind.Begin, "s1"));

        Assert.Null(GuardCommands.Apply(state, Command(GuardCommandKind.End, "s1")));
    }

    [Fact]
    public void A_command_for_another_session_is_ignored()
    {
        var state = GuardCommands.Apply(null, Command(GuardCommandKind.Begin, "s1"));

        Assert.Same(state, GuardCommands.Apply(state, Command(GuardCommandKind.End, "other")));
        Assert.Same(state, GuardCommands.Apply(state, Command(GuardCommandKind.Checkpoint, "other", elapsed: 9999)));
    }

    [Fact]
    public void An_unknown_command_changes_nothing()
    {
        var state = GuardCommands.Apply(null, Command(GuardCommandKind.Begin, "s1"));

        Assert.Same(state, GuardCommands.Apply(state, Command("nonsense", "s1")));
    }

    [Fact]
    public void A_checkpoint_before_any_session_is_ignored()
    {
        Assert.Null(GuardCommands.Apply(null, Command(GuardCommandKind.Checkpoint, "s1")));
    }
}
