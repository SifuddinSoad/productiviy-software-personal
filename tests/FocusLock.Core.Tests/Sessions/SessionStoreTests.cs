using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.Core.Tests.Sessions;

public sealed class SessionStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "focuslock-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static Session Sample(string id, DateTime start) => new()
    {
        Id = id,
        Name = "Tutor system planning",
        PlannedSeconds = 3600,
        StartUtc = start,
        Plans =
        [
            new Plan
            {
                Id = "p1", Name = "Matching flow", Done = true,
                Doc = new BoardDoc
                {
                    Objs = [new BoardObject { Id = "n1", Kind = ObjKind.Sticky, X = 10, Y = 20, W = 150, H = 92, Text = "hello", Fill = "#f2d06b", Votes = 2 }],
                    Conns = [new Connector { Id = "c1", From = "n1", To = "n1", Style = "elbow", Dash = true }],
                    Strokes = [new Stroke { Id = "k1", Pts = [[1, 2], [3, 4]] }],
                    Cam = new Camera { X = 5, Y = 6, Z = 0.8 },
                },
            },
        ],
    };

    [Fact]
    public void Save_and_load_roundtrip()
    {
        var store = new SessionStore(_dir);
        var start = new DateTime(2026, 9, 13, 9, 30, 0, DateTimeKind.Utc);
        store.Save(Sample("s1", start));

        var loaded = store.Load("s1")!;

        Assert.Equal("Tutor system planning", loaded.Name);
        Assert.Equal(start, loaded.StartUtc);
        var doc = loaded.Plans.Single().Doc;
        Assert.Equal("hello", doc.Objs.Single().Text);
        Assert.Equal("elbow", doc.Conns.Single().Style);
        Assert.True(doc.Conns.Single().Dash);
        Assert.Equal([3.0, 4.0], doc.Strokes.Single().Pts[1]);
        Assert.Equal(0.8, doc.Cam.Z);
    }

    [Fact]
    public void Save_leaves_no_temp_file()
    {
        var store = new SessionStore(_dir);
        store.Save(Sample("s1", DateTime.UtcNow));
        store.Save(Sample("s1", DateTime.UtcNow));

        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void List_is_newest_first_and_skips_corrupt_files()
    {
        var store = new SessionStore(_dir);
        store.Save(Sample("old", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
        store.Save(Sample("new", new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc)));
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json");

        var ids = store.List().Select(s => s.Id).ToList();

        Assert.Equal(["new", "old"], ids);
    }

    [Fact]
    public void Enums_are_stored_as_text()
    {
        var store = new SessionStore(_dir);
        var s = Sample("s1", DateTime.UtcNow);
        s.EndedUtc = DateTime.UtcNow;
        s.EndReason = EndReason.Emergency;
        store.Save(s);

        Assert.Contains("\"emergency\"", File.ReadAllText(Path.Combine(_dir, "s1.json")));
        Assert.Equal(EndReason.Emergency, store.Load("s1")!.EndReason);
    }

    [Fact]
    public void Unfinished_sessions_are_closed_out_as_interrupted()
    {
        var store = new SessionStore(_dir);
        var started = new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc);
        var stale = Sample("stale", started);
        stale.Clock = new SessionClockState { ConfirmedElapsedSec = 600, LastCheckpointUtc = started.AddMinutes(10) };
        store.Save(stale);

        var closed = store.CloseUnfinished(exceptId: null);

        var loaded = store.Load("stale")!;
        Assert.Equal(1, closed);
        Assert.Equal(EndReason.Interrupted, loaded.EndReason);
        Assert.Equal(started.AddMinutes(10), loaded.EndedUtc);   // last moment it was known alive
        Assert.Equal("interrupted", Progress.StateLabel(loaded));
    }

    [Fact]
    public void The_session_that_is_still_running_is_left_alone()
    {
        var store = new SessionStore(_dir);
        store.Save(Sample("running", DateTime.UtcNow));

        store.CloseUnfinished(exceptId: "running");

        Assert.False(store.Load("running")!.IsEnded);
    }

    [Fact]
    public void Already_ended_sessions_are_not_touched()
    {
        var store = new SessionStore(_dir);
        var done = Sample("done", DateTime.UtcNow);
        done.EndedUtc = new DateTime(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);
        done.EndReason = EndReason.Completed;
        store.Save(done);

        Assert.Equal(0, store.CloseUnfinished(null));
        Assert.Equal(EndReason.Completed, store.Load("done")!.EndReason);
    }

    [Fact]
    public void Active_store_set_get_clear()
    {
        var active = new ActiveSessionStore(Path.Combine(_dir, "active.json"));
        Assert.Null(active.Get());

        active.Set(new ActiveSession { SessionId = "s1", TaskMgrWasDisabled = true });
        Assert.Equal("s1", active.Get()!.SessionId);
        Assert.True(active.Get()!.TaskMgrWasDisabled);

        active.Clear();
        Assert.Null(active.Get());
    }
}
