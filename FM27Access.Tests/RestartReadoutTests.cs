using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

// The pure spoken-restart naming for the Alt+5 out-of-play readouts, cross-checked against the line
// geometry so a stale event cannot mislabel.
public class RestartReadoutTests
{
    [Theory]
    [InlineData(true, "your throw-in")]
    [InlineData(false, "opposition throw-in")]
    public void TouchlineThrowInGetsTheTeam(bool forRef, string expected)
        => Assert.Equal(expected, RestartReadout.Label(OutOfPlayKind.Touchline, RestartKind.ThrowIn, forRef));

    [Fact]
    public void TouchlineReadsThrowInEvenWithNoEvent()
    {
        // A side-line exit is a throw-in by geometry alone (no team without an event).
        Assert.Equal("throw-in", RestartReadout.Label(OutOfPlayKind.Touchline, RestartKind.None, false));
    }

    [Fact]
    public void TouchlineIgnoresAnInconsistentGoalLineEvent()
    {
        // A stale corner event from an earlier goal-line dead ball must not turn a touchline exit into a
        // corner: geometry wins and the team is dropped (the event disagrees about the line).
        Assert.Equal("throw-in", RestartReadout.Label(OutOfPlayKind.Touchline, RestartKind.Corner, true));
    }

    [Theory]
    [InlineData(true, "your corner")]
    [InlineData(false, "opposition corner")]
    public void GoalLineCornerGetsTheTeam(bool forRef, string expected)
        => Assert.Equal(expected, RestartReadout.Label(OutOfPlayKind.GoalLine, RestartKind.Corner, forRef));

    [Theory]
    [InlineData(true, "your goal kick")]
    [InlineData(false, "opposition goal kick")]
    public void GoalLineGoalKickGetsTheTeam(bool forRef, string expected)
        => Assert.Equal(expected, RestartReadout.Label(OutOfPlayKind.GoalLine, RestartKind.GoalKick, forRef));

    [Fact]
    public void GoalLineWithoutAMatchingEventIsNull_SoTheCallerKeepsGeometry()
    {
        // Geometry cannot tell a corner from a goal kick, so no event => no restart name (not a guess).
        Assert.Null(RestartReadout.Label(OutOfPlayKind.GoalLine, RestartKind.None, false));
        // A stale throw-in event from an earlier touchline dead ball disagrees about the line: also rejected.
        Assert.Null(RestartReadout.Label(OutOfPlayKind.GoalLine, RestartKind.ThrowIn, true));
    }

    [Theory]
    [InlineData(OutOfPlayKind.Net)]
    [InlineData(OutOfPlayKind.InPlay)]
    public void NetAndInPlayHaveNoRestartLabel(OutOfPlayKind geom)
        => Assert.Null(RestartReadout.Label(geom, RestartKind.GoalKick, true));
}
