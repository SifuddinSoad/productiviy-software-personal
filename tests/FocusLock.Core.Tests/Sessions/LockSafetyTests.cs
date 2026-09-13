using FocusLock.Core.Sessions;

namespace FocusLock.Core.Tests.Sessions;

public class LockSafetyTests
{
    [Fact]
    public void A_lock_within_its_planned_length_stays_on()
    {
        Assert.False(LockSafety.ShouldForceUnlock(3600, 3599));
    }

    [Fact]
    public void The_grace_window_keeps_it_on_a_little_longer()
    {
        Assert.False(LockSafety.ShouldForceUnlock(3600, 3600 + 599));
    }

    [Fact]
    public void Past_the_grace_window_it_is_forced_open()
    {
        Assert.True(LockSafety.ShouldForceUnlock(3600, 3600 + 601));
    }

    [Fact]
    public void An_absurd_planned_length_cannot_extend_the_lock()
    {
        // a corrupt file asking for 100 hours must not hold the screen for 100 hours
        Assert.True(LockSafety.ShouldForceUnlock(360000, LockSafety.MaxSessionSeconds + 601));
    }
}
