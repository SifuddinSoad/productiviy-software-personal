using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.Core.Tests.Sessions;

/// <summary>
/// What a hard reset does to session files: the newest write can be lost, so the previous copy
/// has to be good enough to fall back to.
/// </summary>
public sealed class DurabilityTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "focuslock-crash-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static Session Sample(string id, string name) => new()
    {
        Id = id, Name = name, PlannedSeconds = 3600, StartUtc = DateTime.UtcNow,
    };

    [Fact]
    public void The_previous_copy_is_kept_beside_the_newest_one()
    {
        var store = new SessionStore(_dir);
        store.Save(Sample("s1", "first"));
        store.Save(Sample("s1", "second"));

        Assert.True(File.Exists(Path.Combine(_dir, "s1.json.bak")));
    }

    [Fact]
    public void A_session_zeroed_by_a_reset_comes_back_from_the_backup()
    {
        var store = new SessionStore(_dir);
        store.Save(Sample("s1", "first"));
        store.Save(Sample("s1", "second"));

        // exactly what NTFS leaves behind when the rename survives but the contents did not
        File.WriteAllBytes(Path.Combine(_dir, "s1.json"), new byte[512]);

        var loaded = store.Load("s1");

        Assert.NotNull(loaded);
        Assert.Equal("first", loaded.Name);
    }

    [Fact]
    public void A_zeroed_session_still_appears_in_the_list()
    {
        var store = new SessionStore(_dir);
        store.Save(Sample("s1", "first"));
        store.Save(Sample("s1", "second"));
        File.WriteAllBytes(Path.Combine(_dir, "s1.json"), []);

        Assert.Single(store.List());
    }

    [Fact]
    public void Backup_files_are_not_listed_as_sessions_of_their_own()
    {
        var store = new SessionStore(_dir);
        store.Save(Sample("s1", "first"));
        store.Save(Sample("s1", "second"));
        store.Save(Sample("s2", "other"));

        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void An_active_pointer_zeroed_by_a_reset_falls_back_too()
    {
        var path = Path.Combine(_dir, "active.json");
        var active = new ActiveSessionStore(path);
        active.Set(new ActiveSession { SessionId = "s1" });
        active.Set(new ActiveSession { SessionId = "s1", TaskMgrWasDisabled = true });

        File.WriteAllBytes(path, new byte[64]);

        Assert.Equal("s1", active.Get()!.SessionId);
    }
}

public class RemainingForTests
{
    [Fact]
    public void A_session_checkpointed_moments_ago_still_has_its_time()
    {
        var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        var saved = new SessionClockState { ConfirmedElapsedSec = 300, LastCheckpointUtc = now.AddSeconds(-5) };

        Assert.Equal(3295, SessionClock.RemainingFor(saved, 3600, now), 1);
    }

    [Fact]
    public void A_session_from_days_ago_has_nothing_left()
    {
        var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        var saved = new SessionClockState { ConfirmedElapsedSec = 60, LastCheckpointUtc = now.AddDays(-3) };

        Assert.Equal(0, SessionClock.RemainingFor(saved, 3600, now));
    }

    [Fact]
    public void A_checkpoint_in_the_future_does_not_add_time()
    {
        var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        var saved = new SessionClockState { ConfirmedElapsedSec = 100, LastCheckpointUtc = now.AddHours(2) };

        Assert.Equal(3500, SessionClock.RemainingFor(saved, 3600, now), 1);
    }
}
