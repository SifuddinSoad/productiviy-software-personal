using FocusLock.Core.Guard;
using FocusLock.Core.Models;

namespace FocusLock.Core.Tests.Guard;

public class GuardStoreTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "FocusLockGuardTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    GuardStore Store() => new(_root);

    /// <summary>The service installer makes the inbox; a store without one accepts no commands.</summary>
    GuardStore InstalledStore()
    {
        Directory.CreateDirectory(GuardPaths.InboxDir(_root));
        return Store();
    }

    [Fact]
    public void With_no_service_installed_commands_go_nowhere()
    {
        Assert.False(Store().Send(new GuardCommand { Kind = GuardCommandKind.Begin, SessionId = "s1" }));
        Assert.False(Directory.Exists(GuardPaths.InboxDir(_root)));
    }

    [Fact]
    public void Nothing_is_guarded_to_start_with()
    {
        Assert.Null(Store().Read());
    }

    [Fact]
    public void State_survives_a_write_and_read()
    {
        var store = Store();
        store.Write(new GuardState
        {
            SessionId = "s1",
            UserSid = "S-1-5-21-1",
            AppPath = @"C:\FocusLock\FocusLock.App.exe",
            PlannedSeconds = 1800,
            Clock = new SessionClockState { ConfirmedElapsedSec = 42, LastCheckpointUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
        });

        var read = store.Read();

        Assert.Equal("s1", read!.SessionId);
        Assert.Equal(1800, read.PlannedSeconds);
        Assert.Equal(42, read.Clock.ConfirmedElapsedSec);
    }

    [Fact]
    public void Clearing_leaves_nothing_behind_to_read()
    {
        var store = Store();
        store.Write(new GuardState { SessionId = "s1" });
        store.Write(new GuardState { SessionId = "s2" });   // makes a .bak as well
        store.Clear();

        Assert.Null(store.Read());
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public void Commands_come_back_in_the_order_they_were_sent()
    {
        var store = InstalledStore();
        store.Send(new GuardCommand { Kind = GuardCommandKind.Begin, SessionId = "s1" });
        store.Send(new GuardCommand { Kind = GuardCommandKind.Checkpoint, SessionId = "s1" });
        store.Send(new GuardCommand { Kind = GuardCommandKind.End, SessionId = "s1" });

        var drained = store.Drain();

        Assert.Equal(
            [GuardCommandKind.Begin, GuardCommandKind.Checkpoint, GuardCommandKind.End],
            drained.Select(c => c.Kind));
    }

    [Fact]
    public void Draining_empties_the_inbox()
    {
        var store = InstalledStore();
        store.Send(new GuardCommand { Kind = GuardCommandKind.Begin, SessionId = "s1" });
        store.Drain();

        Assert.Empty(store.Drain());
        Assert.Empty(Directory.GetFiles(GuardPaths.InboxDir(_root)));
    }

    [Fact]
    public void A_damaged_state_file_reads_as_nothing_guarded()
    {
        var store = Store();
        Directory.CreateDirectory(_root);
        File.WriteAllText(GuardPaths.StateFile(_root), "{ not json");

        Assert.Null(store.Read());
    }

    [Fact]
    public void A_damaged_command_is_dropped_and_the_rest_still_arrive()
    {
        var store = InstalledStore();
        Directory.CreateDirectory(GuardPaths.InboxDir(_root));
        File.WriteAllText(Path.Combine(GuardPaths.InboxDir(_root), "aaa.json"), "{ not json");
        store.Send(new GuardCommand { Kind = GuardCommandKind.Begin, SessionId = "s1" });

        var drained = store.Drain();

        Assert.Equal(GuardCommandKind.Begin, Assert.Single(drained).Kind);
        Assert.Empty(Directory.GetFiles(GuardPaths.InboxDir(_root)));
    }
}
