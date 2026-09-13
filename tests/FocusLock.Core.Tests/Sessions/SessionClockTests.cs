using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.Core.Tests.Sessions;

public class SessionClockTests
{
    sealed class FakeTime
    {
        public long TickMs;
        public DateTime Now = new(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);
        public void Advance(double seconds, bool wallClockToo = true)
        {
            TickMs += (long)(seconds * 1000);
            if (wallClockToo) Now = Now.AddSeconds(seconds);
        }
    }

    [Fact]
    public void Counts_monotonic_time_while_running()
    {
        var t = new FakeTime();
        var clock = SessionClock.Start(600, () => t.TickMs, () => t.Now);

        t.Advance(90);
        clock.Tick();

        Assert.Equal(90, clock.ElapsedSec, 3);
        Assert.Equal(510, clock.RemainingSec, 3);
        Assert.False(clock.IsFinished);
    }

    [Fact]
    public void Changing_wall_clock_while_running_has_no_effect()
    {
        var t = new FakeTime();
        var clock = SessionClock.Start(600, () => t.TickMs, () => t.Now);

        t.Now = t.Now.AddHours(5);   // user jumps the clock forward
        t.Advance(10);
        clock.Tick();

        Assert.Equal(10, clock.ElapsedSec, 3);
    }

    [Fact]
    public void Resume_credits_time_the_machine_was_off()
    {
        var t = new FakeTime();
        var clock = SessionClock.Start(600, () => t.TickMs, () => t.Now);
        t.Advance(100);
        clock.Tick();
        var saved = clock.Snapshot();

        t.Now = t.Now.AddSeconds(200);   // powered off for 200 s
        t.TickMs = 5;                    // tick counter restarts after reboot
        var resumed = SessionClock.Resume(saved, 600, () => t.TickMs, () => t.Now);

        Assert.Equal(300, resumed.ElapsedSec, 3);
    }

    [Fact]
    public void Clock_moved_back_before_reboot_does_not_add_time()
    {
        var t = new FakeTime();
        var clock = SessionClock.Start(600, () => t.TickMs, () => t.Now);
        t.Advance(100);
        clock.Tick();
        var saved = clock.Snapshot();

        t.Now = t.Now.AddHours(-3);
        var resumed = SessionClock.Resume(saved, 600, () => 0, () => t.Now);

        Assert.Equal(100, resumed.ElapsedSec, 3);
    }

    [Fact]
    public void Clock_moved_back_while_running_does_not_leak_into_next_resume()
    {
        var t = new FakeTime();
        var clock = SessionClock.Start(600, () => t.TickMs, () => t.Now);

        t.Now = t.Now.AddHours(-1);   // moved back while running
        t.Advance(60);
        clock.Tick();
        var saved = clock.Snapshot();

        t.Now = t.Now.AddHours(1);    // clock resynced on the next boot, no real off time
        var resumed = SessionClock.Resume(saved, 600, () => 0, () => t.Now);

        Assert.Equal(60, resumed.ElapsedSec, 3);
    }

    [Fact]
    public void Remaining_never_goes_negative()
    {
        var t = new FakeTime();
        var clock = SessionClock.Start(30, () => t.TickMs, () => t.Now);
        t.Advance(45);
        clock.Tick();

        Assert.Equal(0, clock.RemainingSec);
        Assert.True(clock.IsFinished);
    }

    [Fact]
    public void Resume_of_saved_state_with_future_checkpoint_counts_zero_gap()
    {
        var now = new DateTime(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);
        var saved = new SessionClockState { ConfirmedElapsedSec = 40, LastCheckpointUtc = now.AddMinutes(10) };

        var resumed = SessionClock.Resume(saved, 600, () => 0, () => now);

        Assert.Equal(40, resumed.ElapsedSec, 3);
    }
}
