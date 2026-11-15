using FM27Access.Navigation;
using FM27Access.Plugin.Server;
using Xunit;

namespace FM27Access.Tests;

public class MatchEventWindowTests
{
    [Theory]
    [InlineData(4770, 4779, .125, 10, 16, false)]
    [InlineData(10426, 10434, .125, 10, 16, false)]
    [InlineData(14726, 14736, .121, 10, 16, false)]
    [InlineData(16155, 16164, .078, 10, 16, false)]
    [InlineData(100, 115, .125, 10, 16, false)]
    [InlineData(100, 116, .125, 10, 16, true)]
    [InlineData(100, 160, .5, 10, 16, true)]
    [InlineData(100, 103, .125, 1, 16, true)]
    [InlineData(100, 109, .125, 10, 0, true)]
    [InlineData(569, 4738, .11, 10, 16, true)]
    public void NativeMinimumGapRejectsFalseShortBatchSkipsAtHighViewingSpeed(
        int previous, int current, double seconds, float speed, int gap, bool skipped)
        => Assert.Equal(skipped, MatchEventWindow.CrossedSkippedGround(previous, current, seconds, speed, gap));

    [Theory]
    [InlineData(100, 102, 0.125, 1, false)]
    [InlineData(100, 103, 0.125, 1, true)]
    [InlineData(100, 108, 0.5, 4, false)]
    [InlineData(100, 109, 0.5, 4, true)]
    [InlineData(100, 105, 0.125, 10, false)]
    [InlineData(100, 106, 0.125, 10, false)]
    [InlineData(100, 108, 0.2, 10, false)]
    [InlineData(100, 108, 0.1, 10, true)]
    [InlineData(100, 107, 0.125, 10, true)]
    [InlineData(100, 115, 0.125, 10, true)]
    [InlineData(100, 160, 0.5, 10, true)]
    [InlineData(100, 101, 0, 10, false)]
    [InlineData(100, 99, 0.125, 10, false)]
    [InlineData(-1, 100, 0.125, 10, false)]
    [InlineData(100, 103, 0.125, float.NaN, true)]
    [InlineData(100, 103, 0.125, float.PositiveInfinity, true)]
    public void NativeHighlightSpeedDoesNotTurnLegitimatePlaybackIntoSkippedGround(
        int previous, int current, double seconds, float speed, bool skipped)
        => Assert.Equal(skipped, MatchEventWindow.CrossedSkippedGround(previous, current, seconds, speed));

    [Fact]
    public void BookmarksBelongToTheMatchRatherThanTheBrowserConnection()
    {
        var window = new MatchEventWindow();
        Assert.True(window.Bind(new IntPtr(1)));
        Assert.Null(window.Goal.Observe(true, 10, false, false, true));
        Assert.Equal("goalfor", window.Goal.Observe(true, 20, false, true, true));
        Assert.Null(window.Goal.Observe(true, 1719, false, false, true));
        Assert.False(window.Bind(new IntPtr(1))); // halftime keeps the same match
        Assert.Equal(1719, window.Goal.HighWater);
        Assert.True(window.Bind(IntPtr.Zero)); // return to the career
        Assert.Equal(-1, window.Goal.HighWater);
        Assert.Equal(-1, window.Out.EventHighWater);
        Assert.Equal(-1, window.PutInPlay.HighWater);
        Assert.Equal(-1, window.Defence.HighWater);
        Assert.Equal(-1, window.Shot.HighWater);
        Assert.Equal(-1, window.Header.HighWater);
        Assert.True(window.Bind(new IntPtr(2)));
        Assert.Null(window.Goal.Observe(true, 0, false, false, true));
        Assert.Equal("goalfor", window.Goal.Observe(true, 64, false, true, true));
    }

    [Fact]
    public void PeriodRebindDoesNotReplayAnOldGoalAndKeepsTheNextGoal()
    {
        var cue = new GoalOneShot();
        string? Observe(int last, bool crossed, bool replay = false)
            => cue.Observe(MatchEventWindow.CanRead(cue.HighWater, last, replay), last, replay, crossed, true);

        Assert.Null(Observe(10, false));
        Assert.Equal("goalfor", Observe(20, true));
        Assert.Null(Observe(797, false));
        Assert.Null(Observe(0, false)); // temporary native period-rebind window
        Assert.Equal(797, cue.HighWater);
        Assert.Null(Observe(798, false)); // substitution, not a scan over the first half
        Assert.Null(Observe(799, false)); // second-half kickoff
        Assert.Equal("goalfor", Observe(900, true));
        Assert.Null(Observe(901, false));
    }

    [Fact]
    public void ActualReplayStillSoundsTheReplayedGoalAndResumesSilently()
    {
        var cue = new GoalOneShot();
        string? Observe(int last, bool crossed, bool replay = false)
            => cue.Observe(MatchEventWindow.CanRead(cue.HighWater, last, replay), last, replay, crossed, true);

        Assert.Null(Observe(10, false));
        Assert.Equal("goalfor", Observe(20, true));
        Assert.Null(Observe(25, false));
        Assert.Null(Observe(15, false, true));
        Assert.Equal("goalfor", Observe(20, true, true));
        Assert.Null(Observe(25, false));
        Assert.Null(Observe(26, false));
        Assert.Equal("goalfor", Observe(30, true));
    }

    [Fact]
    public void UninitializedWindowCannotBaselineAConnection()
    {
        var cue = new GoalOneShot();
        Assert.False(MatchEventWindow.CanRead(cue.HighWater, -1, false));
        Assert.Null(cue.Observe(false, -1, false, false, false));
        Assert.Equal(-1, cue.HighWater);
        Assert.Null(cue.Observe(MatchEventWindow.CanRead(cue.HighWater, 797, false), 797, false, true, true));
        Assert.Equal(797, cue.HighWater);
    }
}
