using FocusLock.Core.Models;
using FocusLock.Core.Sessions;

namespace FocusLock.Core.Tests.Sessions;

public class ProgressTests
{
    static Session WithPlans(params bool[] done) => new()
    {
        Plans = done.Select((d, i) => new Plan { Id = "p" + i, Done = d }).ToList(),
    };

    [Fact]
    public void No_plans_has_no_percent()
    {
        Assert.Null(Progress.Percent(WithPlans()));
    }

    [Theory]
    [InlineData(new[] { true, false, false }, 33)]
    [InlineData(new[] { true, true, false }, 67)]
    [InlineData(new[] { true, true }, 100)]
    [InlineData(new[] { false }, 0)]
    public void Percent_is_done_over_total(bool[] done, int expected)
    {
        Assert.Equal(expected, Progress.Percent(WithPlans(done)));
    }

    [Fact]
    public void State_labels()
    {
        var active = WithPlans(false);
        Assert.Equal("active", Progress.StateLabel(active));

        var ended = WithPlans(true, false);
        ended.EndedUtc = DateTime.UtcNow;
        ended.EndReason = EndReason.Completed;
        Assert.Equal("ended", Progress.StateLabel(ended));
        Assert.True(Progress.CanContinue(ended));

        var complete = WithPlans(true, true);
        complete.EndedUtc = DateTime.UtcNow;
        complete.EndReason = EndReason.Completed;
        Assert.Equal("complete", Progress.StateLabel(complete));
        Assert.False(Progress.CanContinue(complete));

        var noPlans = WithPlans();
        noPlans.EndedUtc = DateTime.UtcNow;
        noPlans.EndReason = EndReason.Completed;
        Assert.Equal("complete", Progress.StateLabel(noPlans));

        var early = WithPlans(true, true);
        early.EndedUtc = DateTime.UtcNow;
        early.EndReason = EndReason.Emergency;
        Assert.Equal("ended early", Progress.StateLabel(early));
    }

    [Fact]
    public void Active_session_cannot_be_continued()
    {
        Assert.False(Progress.CanContinue(WithPlans(false)));
    }
}

public class EmergencyCodeTests
{
    [Fact]
    public void Has_requested_number_of_characters_in_groups_of_five()
    {
        var code = EmergencyCode.Generate(200);

        Assert.Equal(200, code.Count(c => c != ' '));
        Assert.All(code.Split(' '), g => Assert.Equal(5, g.Length));
    }

    [Fact]
    public void Contains_no_lookalike_characters()
    {
        var code = EmergencyCode.Generate(2000);
        Assert.DoesNotContain(code, c => "0O1lI".Contains(c));
    }

    [Fact]
    public void Codes_differ()
    {
        Assert.NotEqual(EmergencyCode.Generate(), EmergencyCode.Generate());
    }

    [Fact]
    public void Matching_is_exact_and_case_sensitive()
    {
        const string code = "abcde FGHJK";
        Assert.True(EmergencyCode.Matches(code, "abcde FGHJK"));
        Assert.True(EmergencyCode.Matches(code, "  abcde FGHJK\r\n"));
        Assert.False(EmergencyCode.Matches(code, "abcde fghjk"));
        Assert.False(EmergencyCode.Matches(code, "abcdeFGHJK"));
        Assert.False(EmergencyCode.Matches(code, "abcde FGHJ"));
    }
}
