using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

/// <summary>
/// The carrier pressure level. The shape that matters is count outweighing distance: each additional
/// opponent must move the value more than any amount of closing by one man, or the bed stops distinguishing
/// a lone marker from a swarm - which is the whole reason it exists.
/// </summary>
public class CarrierPressureTests
{
    // The shipped tuning, so these document what the bed actually does rather than an abstract shape.
    private const double R = 4.0;        // radius, metres: beyond this an opponent is silent
    private const double Base = 0.0;     // count-versus-distance dial; zero = a distant man counts nothing
    private const double Div = 3.0;      // sum that counts as maximum pressure: three men on the ball

    private static double Level(params double[] distances)
    {
        double sum = 0;
        foreach (var d in distances) sum += CarrierPressure.Contribution(d, R, Base);
        return CarrierPressure.Normalise(sum, Div);
    }

    [Fact]
    public void AnOpponentAtTheRadiusEdgeIsSilent()
    {
        // What stops the bed running constantly. The first tuning gave a man at the edge most of his share,
        // and since there is nearly always someone near the ball the low stem never rested.
        Assert.Equal(0.0, CarrierPressure.Contribution(R, R, Base), 6);
    }

    [Fact]
    public void OneOpponentCanNeverExceedTheLowBand()
    {
        // The count-outweighs-distance guarantee, and the reason the divisor is the opponent count that
        // means maximum pressure. However tightly a single man marks - even standing on the ball - he must
        // not reach a level that is supposed to mean two men are on the carrier.
        Assert.True(Level(0.0) <= 1.0 / 3.0 + 1e-9,
            $"one opponent on the ball reached {Level(0.0):F3}, past the low band");
    }

    [Fact]
    public void TwoOpponentsCanNeverExceedTheMediumBand()
    {
        Assert.True(Level(0.0, 0.0) <= 2.0 / 3.0 + 1e-9,
            $"two opponents on the ball reached {Level(0.0, 0.0):F3}, past the medium band");
    }

    [Fact]
    public void AnOpponentOnTheBallContributesEverything()
    {
        Assert.Equal(1.0, CarrierPressure.Contribution(0.0, R, Base), 6);
    }

    [Fact]
    public void OpponentsBeyondTheRadiusContributeNothing()
    {
        Assert.Equal(0.0, CarrierPressure.Contribution(R + 0.01, R, Base), 6);
        Assert.Equal(0.0, CarrierPressure.Contribution(40.0, R, Base), 6);
    }

    [Fact]
    public void ASecondOpponentOutweighsAnyAmountOfClosingByTheFirst()
    {
        // The decisive case for the weighting, stated the way the shipped tuning can honestly support: a
        // second man at the same range beats the first man closing all the way to the ball.
        double oneOnTheBall = Level(0.0);
        double twoAtMidRange = Level(R / 2.0, R / 2.0);
        Assert.True(twoAtMidRange >= oneOnTheBall,
            $"two at mid-range ({twoAtMidRange:F3}) must not sit below one on the ball ({oneOnTheBall:F3})");
    }

    [Fact]
    public void ClosingRaisesPressureWithoutChangingTheCount()
    {
        // Distance still matters within a count; it just cannot outweigh a body.
        Assert.True(Level(0.5) > Level(2.0));
        Assert.True(Level(1.0, 1.0) > Level(2.5, 2.5));
    }

    [Fact]
    public void PressureRisesMonotonicallyWithEachExtraOpponent()
    {
        double one = Level(1.5);
        double two = Level(1.5, 1.5);
        double three = Level(1.5, 1.5, 1.5);
        Assert.True(one < two && two < three);
    }

    [Fact]
    public void OneOpponentIsLowTwoIsMediumThreeIsHigh()
    {
        // The banding contract the three stems are authored against: with stems at thirds, a single marker
        // must not reach medium and a pair must not reach high, or the levels stop meaning anything.
        // Measured at a third of the radius - close enough that every one of them is genuinely engaged.
        const double engaged = R / 3.0;
        Assert.InRange(Level(engaged), 0.0, 1.0 / 3.0);
        Assert.InRange(Level(engaged, engaged), 1.0 / 3.0, 2.0 / 3.0);
        Assert.InRange(Level(engaged, engaged, engaged), 2.0 / 3.0, 1.0);
    }

    [Fact]
    public void ACrowdClampsRatherThanOvershooting()
    {
        // A goalmouth scramble puts far more than the divisor's worth of bodies on the ball. Overshooting
        // would drive the top stem past full.
        Assert.Equal(1.0, Level(0.1, 0.2, 0.3, 0.4, 0.5, 0.6), 6);
    }

    [Fact]
    public void NoOpponentsIsSilence()
    {
        Assert.Equal(0.0, CarrierPressure.Normalise(0.0, Div), 6);
    }

    [Fact]
    public void ContributionIsContinuousAcrossTheRadius()
    {
        // Just inside the radius must be within a hair of the base share - which at the shipped tuning is
        // zero, so an opponent fades in from silence rather than snapping on. A discontinuity here would
        // make the stems chatter as a player drifted across the boundary.
        double justInside = CarrierPressure.Contribution(R - 0.001, R, Base);
        Assert.Equal(Base, justInside, 3);
        Assert.True(justInside >= CarrierPressure.Contribution(R + 0.001, R, Base));
    }

    [Fact]
    public void AnUnreadableDistanceContributesNothingRatherThanPoisoningTheSum()
    {
        // One unreadable player must not take the whole bed with it - NaN would propagate through the sum
        // and silence a level that the other opponents legitimately earned.
        Assert.Equal(0.0, CarrierPressure.Contribution(double.NaN, R, Base), 6);
        Assert.Equal(Level(0.5), Level(0.5, double.NaN), 6);
    }

    [Fact]
    public void TheBaseShareIsWhatTradesCountAgainstDistance()
    {
        // Pins the dial itself: at baseShare 0 the contribution is purely distance-driven (an opponent at
        // the edge counts for nothing), at 1 purely count-driven (every opponent inside counts the same).
        Assert.Equal(0.0, CarrierPressure.Contribution(R, R, 0.0), 6);
        Assert.Equal(1.0, CarrierPressure.Contribution(R, R, 1.0), 6);
        Assert.Equal(1.0, CarrierPressure.Contribution(0.0, R, 0.0), 6);
    }
}
