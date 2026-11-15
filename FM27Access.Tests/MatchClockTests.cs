using FM27Access.Navigation;

namespace FM27Access.Tests;

public class MatchClockTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5, "0:05")]
    [InlineData(65, "1:05")]
    [InlineData(335, "5:35")]
    [InlineData(982, "16:22")]
    public void Format_RendersMinutesAndZeroPaddedSeconds(double seconds, string expected)
    {
        Assert.Equal(expected, MatchClock.Format(seconds, 0, MatchClockState.Playing));
    }

    [Fact]
    public void Format_TruncatesFractionalSeconds()
    {
        // The engine Time is a float; a partial second must not round the clock forward.
        Assert.Equal("5:35", MatchClock.Format(335.9, 0, MatchClockState.Playing));
    }

    [Fact]
    public void Format_ClampsNegativeTimeToZero()
    {
        Assert.Equal("0:00", MatchClock.Format(-3, 0, MatchClockState.Playing));
    }

    [Fact]
    public void Format_AppendsInjuryTime()
    {
        Assert.Equal("45:00 +2", MatchClock.Format(2700, 2, MatchClockState.Playing));
    }

    [Fact]
    public void Format_OmitsInjuryTimeWhenZeroOrNegative()
    {
        Assert.Equal("45:00", MatchClock.Format(2700, 0, MatchClockState.Playing));
    }

    [Theory]
    [InlineData(MatchClockState.HalfTime, "Half Time")]
    [InlineData(MatchClockState.FullTime, "Full Time")]
    [InlineData(MatchClockState.Penalties, "Penalties")]
    public void Format_NonPlayPeriodsShowStateInsteadOfClock(MatchClockState state, string expected)
    {
        // Even with elapsed time on the clock, a break must not read as a ticking number.
        Assert.Equal(expected, MatchClock.Format(2700, 1, state));
    }

    // Boundaries from the live match this logic was verified against (times in engine
    // seconds): first half kicked off at 0.25, its final whistle at 2784 (46:24 — a 1:24
    // overrun), and the second half kicked off at 2799. The boundaries include entries the
    // playback has not reached yet, because the sim frontier records them ahead of time.
    private static readonly double[] Starts = { 0.25, 2799.0 };
    private static readonly double[] Ends = { 2784.0 };

    [Fact]
    public void DisplaySeconds_FirstHalfShowsEngineTimeIncludingStoppage()
    {
        // Observed live at 45:22 in first-half stoppage: the display keeps ticking past
        // 45:00, and the second-half boundaries ahead of playback must not apply yet. The
        // quarter-second is the kick-off slice offset, below what the MM:SS render shows.
        Assert.Equal(2722.45, MatchClock.DisplaySeconds(2722.7, Starts, Ends));
    }

    [Fact]
    public void DisplaySeconds_HalfTimeBreakFreezesAtNextPeriodBase()
    {
        // Observed live: the moment playback reaches the half-time whistle the scorebar
        // snaps to a frozen 45:00 baseline, not 46:24.
        Assert.Equal(2700, MatchClock.DisplaySeconds(2784.0, Starts, Ends));
    }

    [Fact]
    public void DisplaySeconds_SecondHalfTicksFromFortyFive()
    {
        // 11 seconds after the second-half kick-off the clock reads 45:11, not the raw
        // engine 46:50 that carries the first-half overrun.
        double? display = MatchClock.DisplaySeconds(2810.0, Starts, Ends);
        Assert.Equal("45:11", MatchClock.Format(display.Value, 0, MatchClockState.Playing));
    }

    [Fact]
    public void DisplaySeconds_SecondHalfStoppageRunsPastNinety()
    {
        // 2 minutes into second-half stoppage: 45:00 + 45:00 played + 2:00 = 92:00.
        double? display = MatchClock.DisplaySeconds(2799 + 2700 + 120, Starts, Ends);
        Assert.Equal("92:00 +4", MatchClock.Format(display.Value, 4, MatchClockState.Playing));
    }

    [Fact]
    public void DisplaySeconds_PreKickoffClampsToZero()
    {
        // Playback can sit a fraction before the recorded kick-off slice; the clock must
        // read 0:00, never negative.
        Assert.Equal(0, MatchClock.DisplaySeconds(0, new[] { 0.25 }, new double[0]));
    }

    [Fact]
    public void DisplaySeconds_ExtraTimeHalvesRestartAtTheirBases()
    {
        // Full boundary set: halves end at 46:24 / 93:00 / 108:30, ET halves kick off at
        // 5595 and 6525.
        var starts = new[] { 0.25, 2799.0, 5595.0, 6525.0 };
        var ends = new[] { 2784.0, 5580.0, 6510.0 };
        Assert.Equal(5400 + 10, MatchClock.DisplaySeconds(5605.0, starts, ends));
        Assert.Equal(6300 + 10, MatchClock.DisplaySeconds(6535.0, starts, ends));
        // Between the second-half whistle and the ET kick-off: frozen 90:00 baseline.
        Assert.Equal(5400, MatchClock.DisplaySeconds(5585.0, starts, ends));
    }

    [Fact]
    public void DisplaySeconds_NoBoundariesReturnsNull()
    {
        // Without kick-off boundaries the period is unknowable — the caller must get null
        // (and degrade), never the raw value, which reads ahead of the screen.
        Assert.Null(MatchClock.DisplaySeconds(2810, new double[0], new double[0]));
        Assert.Null(MatchClock.DisplaySeconds(2810, null, null));
    }

    [Fact]
    public void DisplaySeconds_MorePeriodsThanFootballHasReturnsNull()
    {
        // A fifth kick-off (a penalty shootout recorded as a period start) has no clock
        // baseline; degrade rather than invent one.
        var starts = new[] { 0.25, 2799.0, 5595.0, 6525.0, 7500.0 };
        var ends = new[] { 2784.0, 5580.0, 6510.0, 7480.0 };
        Assert.Null(MatchClock.DisplaySeconds(7510.0, starts, ends));
    }
}
