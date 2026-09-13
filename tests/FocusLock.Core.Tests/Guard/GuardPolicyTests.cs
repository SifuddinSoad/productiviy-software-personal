using FocusLock.Core.Guard;
using FocusLock.Core.Models;

namespace FocusLock.Core.Tests.Guard;

public class GuardPolicyTests
{
    static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    static GuardState State(double planned, double elapsed, double sinceCheckpointSec = 0) => new()
    {
        SessionId = "s1",
        UserSid = "S-1-5-21-1",
        AppPath = @"C:\FocusLock\FocusLock.App.exe",
        PlannedSeconds = planned,
        Clock = new SessionClockState
        {
            ConfirmedElapsedSec = elapsed,
            LastCheckpointUtc = Now.AddSeconds(-sinceCheckpointSec),
        },
    };

    [Fact]
    public void No_session_means_nothing_to_guard()
    {
        Assert.Equal(GuardAction.Idle, GuardPolicy.Decide(null, appRunning: false, Now, recentLaunches: 0));
    }

    [Fact]
    public void A_running_app_is_left_alone()
    {
        Assert.Equal(GuardAction.Idle, GuardPolicy.Decide(State(3600, 60), appRunning: true, Now, 0));
    }

    [Fact]
    public void A_missing_app_is_brought_back()
    {
        Assert.Equal(GuardAction.Relaunch, GuardPolicy.Decide(State(3600, 60), appRunning: false, Now, 0));
    }

    [Fact]
    public void Time_the_machine_spent_off_still_counts_down()
    {
        // 10 minutes planned, 1 minute confirmed, and the checkpoint is 20 minutes old
        var state = State(600, 60, sinceCheckpointSec: 1200);
        Assert.Equal(GuardAction.Expire, GuardPolicy.Decide(state, appRunning: false, Now, 0));
    }

    [Fact]
    public void The_planned_time_running_out_ends_the_guard()
    {
        Assert.Equal(GuardAction.Expire, GuardPolicy.Decide(State(600, 600), appRunning: false, Now, 0));
    }

    [Fact]
    public void An_expired_session_ends_the_guard_even_while_the_app_runs()
    {
        // the app decides when to unlock; the guard only has to stop bringing it back
        Assert.Equal(GuardAction.Expire, GuardPolicy.Decide(State(600, 900), appRunning: true, Now, 0));
    }

    [Fact]
    public void An_app_that_will_not_stay_up_is_given_up_on()
    {
        var state = State(3600, 60);
        Assert.Equal(GuardAction.Relaunch, GuardPolicy.Decide(state, false, Now, GuardPolicy.MaxLaunches - 1));
        Assert.Equal(GuardAction.Expire, GuardPolicy.Decide(state, false, Now, GuardPolicy.MaxLaunches));
    }
}
