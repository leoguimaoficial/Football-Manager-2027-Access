using FM27Access.Navigation;

namespace FM27Access.Tests;

public class BallThirdTests
{
    private const double HalfPitch = 52.5; // standard 105 m pitch, centre spot at 0

    [Theory]
    // Attacking toward +Z: own goal-line, centre, opponent goal-line.
    [InlineData(-52.5, true, BallThird.DefendingEnd)]
    [InlineData(0.0, true, BallThird.MiddleThird)]
    [InlineData(52.5, true, BallThird.AttackingThird)]
    // Mirrored when the team attacks toward -Z: the same +Z coordinate is now their own end.
    [InlineData(40.0, true, BallThird.AttackingThird)]
    [InlineData(40.0, false, BallThird.DefendingEnd)]
    public void Classify_PlacesBallRelativeToAttackingDirection(double z, bool attackingPositive, BallThird expected)
    {
        Assert.Equal(expected, BallLocation.Classify(z, attackingPositive, HalfPitch));
    }

    [Fact]
    public void Classify_MatchesTheVerifiedLiveReading()
    {
        // The live in-game check: ball at z=12.94 with the reference team attacking +Z read as the
        // middle third (progress 0.623). Guards against a sign or normalisation regression.
        Assert.Equal(BallThird.MiddleThird, BallLocation.Classify(12.94, true, HalfPitch));
    }

    [Theory]
    // The lower boundary sits at progress 1/3 -> z = -HalfPitch/3 (-17.5) when attacking +Z. Just
    // inside is still the defending end; the boundary itself belongs to the middle third.
    [InlineData(-17.6, true, BallThird.DefendingEnd)]
    [InlineData(-17.4, true, BallThird.MiddleThird)]
    // Upper boundary at progress 2/3 -> z = +HalfPitch/3 (+17.5).
    [InlineData(17.4, true, BallThird.MiddleThird)]
    [InlineData(17.6, true, BallThird.AttackingThird)]
    public void Classify_SplitsThirdsAtTheBoundaries(double z, bool attackingPositive, BallThird expected)
    {
        Assert.Equal(expected, BallLocation.Classify(z, attackingPositive, HalfPitch));
    }

    [Fact]
    public void Classify_ClampsBallPastTheGoalLine()
    {
        // A ball driven out beyond the opponent goal-line (past +HalfPitch) is still the attacking third,
        // not something off the scale.
        Assert.Equal(BallThird.AttackingThird, BallLocation.Classify(60.0, true, HalfPitch));
    }

    [Theory]
    [InlineData(BallThird.DefendingEnd, "defensive third")]
    [InlineData(BallThird.MiddleThird, "middle third")]
    [InlineData(BallThird.AttackingThird, "attacking third")]
    public void Speak_UsesTheAgreedPhrasing(BallThird third, string expected)
    {
        Assert.Equal(expected, BallLocation.Speak(third));
    }

    private const double HalfWidth = 34.0; // standard 68 m pitch; central flank band is +/- 34/3

    [Theory]
    // Attacking toward +Z: +X is the reference team's right, -X its left, centre straddles the halfway line.
    [InlineData(20.0, true, Flank.Right)]
    [InlineData(0.0, true, Flank.Centre)]
    [InlineData(-20.0, true, Flank.Left)]
    // Attacking toward -Z mirrors: the same +X coordinate is now that team's left.
    [InlineData(20.0, false, Flank.Left)]
    [InlineData(-20.0, false, Flank.Right)]
    public void ClassifyFlank_NamesTheWingRelativeToAttackingDirection(double x, bool attackingPositive, Flank expected)
    {
        Assert.Equal(expected, BallLocation.ClassifyFlank(x, attackingPositive, HalfWidth));
    }

    [Theory]
    // The central band edge sits at halfWidth/3 = 11.33 m. Just outside is a wing; just inside is centre.
    [InlineData(11.4, true, Flank.Right)]
    [InlineData(11.2, true, Flank.Centre)]
    [InlineData(-11.4, true, Flank.Left)]
    [InlineData(-11.2, true, Flank.Centre)]
    public void ClassifyFlank_SplitsWidthAtTheBandEdges(double x, bool attackingPositive, Flank expected)
    {
        Assert.Equal(expected, BallLocation.ClassifyFlank(x, attackingPositive, HalfWidth));
    }

    [Theory]
    // Deep and central at the attacked end: inside the opposition penalty area, then the six-yard box.
    [InlineData(0.0, 40.0, true, BoxZone.OppositionPenaltyArea)]
    [InlineData(0.0, 48.0, true, BoxZone.OppositionSixYardBox)]
    // The mirror end is the team's own box.
    [InlineData(0.0, -40.0, true, BoxZone.OwnPenaltyArea)]
    [InlineData(0.0, -48.0, true, BoxZone.OwnSixYardBox)]
    // Deep but too wide to be in the box: outside the penalty-area width, so no box.
    [InlineData(25.0, 40.0, true, BoxZone.None)]
    // Midfield is never a box.
    [InlineData(0.0, 0.0, true, BoxZone.None)]
    // Attacking direction flips which end is "own": +Z is the team's own end when it attacks -Z.
    [InlineData(0.0, 40.0, false, BoxZone.OwnPenaltyArea)]
    public void ClassifyBox_PlacesTheBallInPenaltyAndGoalAreas(double x, double z, bool attackingPositive, BoxZone expected)
    {
        Assert.Equal(expected, BallLocation.ClassifyBox(x, z, attackingPositive, HalfPitch));
    }

    [Fact]
    public void ClassifyBox_PrefersTheSixYardBoxOverTheEnclosingPenaltyArea()
    {
        // z=49 is within 5.5 m of the +Z goal-line, so it is the six-yard box even though it also lies
        // inside the penalty area.
        Assert.Equal(BoxZone.OppositionSixYardBox, BallLocation.ClassifyBox(0.0, 49.0, true, HalfPitch));
    }

    [Theory]
    // Down the middle (x=0): 0 on the attacked goal-line, half a pitch at the centre, a full pitch at the
    // far line.
    [InlineData(0.0, 52.5, true, 0.0)]
    [InlineData(0.0, 0.0, true, 52.5)]
    [InlineData(0.0, -52.5, true, 105.0)]
    // Mirrored when attacking -Z.
    [InlineData(0.0, -52.5, false, 0.0)]
    public void MetresToAttackingGoal_MeasuresToTheGoalDownTheMiddle(double x, double z, bool attackingPositive, double expected)
    {
        Assert.Equal(expected, BallLocation.MetresToAttackingGoal(x, z, attackingPositive, HalfPitch), 3);
    }

    [Fact]
    public void MetresToAttackingGoal_CountsTheWidthOffCentre()
    {
        // A ball only 7.7 m from the goal-line but 15.2 m off centre is ~17 m from the goal itself, not
        // 7.7 m. Straight-line distance to the net, not distance to the line.
        Assert.Equal(17.04, BallLocation.MetresToAttackingGoal(15.2, 44.8, true, HalfPitch), 2);
    }

    [Fact]
    public void MetresToAttackingGoal_ClampsTheAlongPitchComponentBeyondTheGoalLine()
    {
        // Past the goal-line and off centre: the along-pitch part clamps to 0, leaving the 10 m of width.
        Assert.Equal(10.0, BallLocation.MetresToAttackingGoal(10.0, 60.0, true, HalfPitch), 3);
    }

    [Theory]
    [InlineData(Flank.Left, "left")]
    [InlineData(Flank.Centre, "central")]
    [InlineData(Flank.Right, "right")]
    public void Speak_UsesTheAgreedFlankPhrasing(Flank flank, string expected)
    {
        Assert.Equal(expected, BallLocation.Speak(flank));
    }

    [Theory]
    [InlineData(BoxZone.OppositionPenaltyArea, "opposition box")]
    [InlineData(BoxZone.OppositionSixYardBox, "opposition six yard box")]
    [InlineData(BoxZone.OwnPenaltyArea, "own box")]
    [InlineData(BoxZone.OwnSixYardBox, "own six yard box")]
    public void Speak_UsesTheAgreedBoxPhrasing(BoxZone box, string expected)
    {
        Assert.Equal(expected, BallLocation.Speak(box));
    }

    // Out-of-play thresholds, matching MatchBallLocation's constants (metres).
    private const double OutMargin = 0.15;
    private const double GoalHalfWidth = 3.66;   // half of the 7.32 m goal mouth
    private const double CrossbarHeight = 2.44;

    private static OutOfPlayKind ClassifyOut(double x, double z, double y, bool attackingPositive)
        => BallLocation.ClassifyOutOfPlay(x, z, y, attackingPositive, HalfPitch, HalfWidth,
            OutMargin, GoalHalfWidth, CrossbarHeight);

    [Theory]
    // Well inside both lines is in play; so is a ball riding a line within the out margin.
    [InlineData(0.0, 0.0, 0.1, InPlayCase)]
    [InlineData(34.1, 0.0, 0.1, InPlayCase)]      // 34.1 < 34.15 threshold: still in
    [InlineData(0.0, 52.6, 0.1, InPlayCase)]      // 52.6 < 52.65 threshold: still in
    // Over a touchline (and clear of both goal-lines) is a throw-in exit.
    [InlineData(34.2, 0.0, 0.1, TouchlineCase)]
    [InlineData(-34.2, -10.0, 0.1, TouchlineCase)]
    public void ClassifyOutOfPlay_InPlayAndTouchline(double x, double z, double y, int expected)
    {
        Assert.Equal((OutOfPlayKind)expected, ClassifyOut(x, z, y, true));
    }

    [Theory]
    // Between the posts and under the bar: a goal.
    [InlineData(0.0, 52.7, 1.0, true, NetCase)]
    [InlineData(3.6, 52.8, 1.0, true, NetCase)]     // 3.6 < 3.66: just inside the post
    // The mirrored end is still a goal when that team attacks the other way (Centre is symmetric).
    [InlineData(0.0, -55.0, 1.0, false, NetCase)]
    // Central but above the crossbar has cleared the goal: a goal-line exit, not a goal.
    [InlineData(0.0, 55.0, 3.0, true, GoalLineCase)]
    // Wide of the post is a goal-line exit (corner / goal kick), never a goal.
    [InlineData(3.7, 52.8, 1.0, true, GoalLineCase)] // 3.7 > 3.66: just outside the post
    [InlineData(5.0, 55.0, 1.0, true, GoalLineCase)]
    // Past the corner flag (over both the goal-line and the touchline): the goal-line wins.
    [InlineData(35.0, 55.0, 1.0, true, GoalLineCase)]
    public void ClassifyOutOfPlay_GoalLineDistinguishesGoalFromDeadBall(double x, double z, double y, bool attackingPositive, int expected)
    {
        Assert.Equal((OutOfPlayKind)expected, ClassifyOut(x, z, y, attackingPositive));
    }

    // InlineData needs compile-time constants, so the enum values are spelled out here.
    private const int InPlayCase = (int)OutOfPlayKind.InPlay;
    private const int TouchlineCase = (int)OutOfPlayKind.Touchline;
    private const int GoalLineCase = (int)OutOfPlayKind.GoalLine;
    private const int NetCase = (int)OutOfPlayKind.Net;

    [Theory]
    // Camera with +Z to the right: the +Z goal-line is full right, its opposite full left.
    [InlineData(-52.5, true, -1.0)]
    [InlineData(0.0, true, 0.0)]
    [InlineData(52.5, true, 1.0)]
    [InlineData(26.25, true, 0.5)]
    // Camera the other way round (the home side kicks off toward -Z): the whole field mirrors.
    [InlineData(52.5, false, -1.0)]
    [InlineData(-26.25, false, 0.5)]
    public void ComputeLengthPan_PutsTheCameraRightHandGoalOnTheRight(double z, bool screenRightIsPositiveZ, double expected)
    {
        Assert.Equal(expected, BallLocation.ComputeLengthPan(z, screenRightIsPositiveZ, HalfPitch), 3);
    }

    [Fact]
    public void ComputeLengthPan_IsFixedForTheWholeMatch()
    {
        // The camera does not move: the sides swap ends, the stereo field does not. Because the pan no
        // longer takes a period or an attacking direction at all, a spot on the pitch cannot change its
        // pan at half time, at extra time, or after any other end-swap — it is fixed by construction.
        // This is what lets the drones share the same bearing without the two axes drifting apart.
        double spot = BallLocation.ComputeLengthPan(30.0, true, HalfPitch);
        Assert.Equal(spot, BallLocation.ComputeLengthPan(30.0, true, HalfPitch), 3);
        // Mirroring the camera mirrors the field, and nothing else can.
        Assert.Equal(-spot, BallLocation.ComputeLengthPan(30.0, false, HalfPitch), 3);
    }

    [Theory]
    // A ball past a goal-line (out of play / in the net) clamps to that edge of the stereo field.
    [InlineData(60.0, true, 1.0)]
    [InlineData(-60.0, true, -1.0)]
    public void ComputeLengthPan_ClampsBeyondTheGoalLines(double z, bool screenRightIsPositiveZ, double expected)
    {
        Assert.Equal(expected, BallLocation.ComputeLengthPan(z, screenRightIsPositiveZ, HalfPitch), 3);
    }

    // The touchline drones. Reusing HalfWidth above (34, the standard 68 m pitch): the drones' silent lane
    // is therefore |x| <= 5.667 and each ramp runs from there out to the touchline at 34. That lane is half
    // the depth of the SPOKEN central third (|x| <= 11.333, FlankCentreEdge below); the two splits are
    // deliberately different and the containment test at the end of this file is what governs the gap.
    private const double LaneEdge = HalfWidth / 6.0;
    private const double FlankCentreEdge = HalfWidth / 3.0;

    [Theory]
    [InlineData(0.0)]            // centre spot
    [InlineData(4.0)]
    [InlineData(-4.0)]
    [InlineData(LaneEdge)]       // exactly on the lane boundary: still silent, so entry is from zero
    [InlineData(-LaneEdge)]
    public void ComputeTouchlineDrone_IsSilentAcrossTheMiddleLane(double x)
    {
        Assert.Equal(0.0, BallLocation.ComputeTouchlineDrone(x, HalfWidth, true), 6);
    }

    [Fact]
    public void ComputeTouchlineDrone_RisesFromTheLaneEdgeToFullAtTheTouchline()
    {
        // The ramp spans the wing band only, so the drone swells as the ball runs at the touchline.
        Assert.Equal(1.0, System.Math.Abs(BallLocation.ComputeTouchlineDrone(HalfWidth, HalfWidth, true)), 6);
        double halfway = System.Math.Abs(
            BallLocation.ComputeTouchlineDrone(LaneEdge + (HalfWidth - LaneEdge) / 2.0, HalfWidth, true));
        Assert.Equal(0.5, halfway, 6);
    }

    [Fact]
    public void ComputeTouchlineDrone_ClampsBeyondTheTouchline()
    {
        // A ball that has gone out stays at full rather than overshooting into a louder-than-max gain.
        Assert.Equal(1.0, System.Math.Abs(BallLocation.ComputeTouchlineDrone(50.0, HalfWidth, true)), 6);
        Assert.Equal(1.0, System.Math.Abs(BallLocation.ComputeTouchlineDrone(-50.0, HalfWidth, true)), 6);
    }

    [Fact]
    public void ComputeTouchlineDrone_GivesTheTwoTouchlinesOppositeSigns()
    {
        // Sign is what selects the drone, so the two wings must never share one.
        double a = BallLocation.ComputeTouchlineDrone(25.0, HalfWidth, true);
        double b = BallLocation.ComputeTouchlineDrone(-25.0, HalfWidth, true);
        Assert.Equal(a, -b, 6);
        Assert.True(a * b < 0);
        Assert.Equal(System.Math.Abs(a), System.Math.Abs(b), 6);   // symmetric: same distance, same loudness
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ComputeTouchlineDrone_PutsTheAttackingRightWingOnTheNearTouchline(bool screenRightIsPositiveZ)
    {
        // The regression that started this: near/far was a hardcoded side while the pan's orientation was
        // computed per fixture, so the two agreed only by luck and the drones sounded inverted in half of
        // all matches. Both now take the same camera bearing, and this pins the relationship between them.
        //
        // The camera stands at the near touchline, so a player facing screen-right has his right hand
        // pointing toward it: the team attacking LEFT TO RIGHT has its right wing on the NEAR side. Work out
        // which team that is from the camera, then find its right wing via the same handedness ClassifyFlank
        // uses, and the drone must call that wing near (negative).
        bool teamAttacksPositiveZ = screenRightIsPositiveZ;          // this team is heard attacking rightward
        double rightWingX = teamAttacksPositiveZ ? 25.0 : -25.0;     // right wing is +X when attacking +Z

        Assert.Equal(Flank.Right, BallLocation.ClassifyFlank(rightWingX, teamAttacksPositiveZ, HalfWidth));
        Assert.True(BallLocation.ComputeTouchlineDrone(rightWingX, HalfWidth, screenRightIsPositiveZ) < 0,
            "the rightward-attacking team's right wing must sound on the NEAR touchline");
        // And its left wing is the far side.
        Assert.True(BallLocation.ComputeTouchlineDrone(-rightWingX, HalfWidth, screenRightIsPositiveZ) > 0,
            "the rightward-attacking team's left wing must sound on the FAR touchline");
    }

    [Fact]
    public void ComputeTouchlineDrone_MirrorsWithTheCameraJustAsThePanDoes()
    {
        // The two axes are one camera placement seen twice. Flipping it must mirror both together; if only
        // one flipped, the pitch would be described sideways-reversed relative to its own stereo field.
        double droneA = BallLocation.ComputeTouchlineDrone(25.0, HalfWidth, true);
        double droneB = BallLocation.ComputeTouchlineDrone(25.0, HalfWidth, false);
        Assert.Equal(droneA, -droneB, 6);

        double panA = BallLocation.ComputeLengthPan(30.0, true, HalfPitch);
        double panB = BallLocation.ComputeLengthPan(30.0, false, HalfPitch);
        Assert.Equal(panA, -panB, 3);
    }

    [Fact]
    public void ComputeTouchlineDrone_DoesNotSwapEndsAtHalfTime()
    {
        // A touchline is fixed ground. The signature takes no attacking direction at all, so the same spot
        // must return the identical value all match — this is what keeps the drone axis coherent with
        // ComputeLengthPan's world-pinned stereo field instead of mirroring the pitch at the break.
        double firstHalf = BallLocation.ComputeTouchlineDrone(25.0, HalfWidth, true);
        double secondHalf = BallLocation.ComputeTouchlineDrone(25.0, HalfWidth, true);
        Assert.Equal(firstHalf, secondHalf, 6);
    }

    [Fact]
    public void ComputeTouchlineDrone_SoundsWhereverTheSpokenFlankNamesAWing()
    {
        // The two splits differ, so the invariant is containment rather than equality: every ball the
        // readout puts on a wing must be sounding. The drone leading the readout is fine — it is the finer
        // signal — but lagging it is not, because the player would then hear silence for a ball being
        // called wide, which is the readout and the audio contradicting each other.
        foreach (var x in new[] { 12.0, -12.0, 30.0, -30.0, HalfWidth, -HalfWidth })
        {
            Assert.NotEqual(Flank.Centre, BallLocation.ClassifyFlank(x, true, HalfWidth));
            Assert.True(System.Math.Abs(BallLocation.ComputeTouchlineDrone(x, HalfWidth, true)) > 0.0,
                $"a ball the readout calls a wing must be sounding, but x={x} was silent");
        }

        // The permitted overlap, in the one direction it is allowed: inside the spoken central third but
        // outside the drones' narrower lane, the readout says central while the drone is already up. This
        // is the audible consequence of the split and is asserted so it cannot be "fixed" by accident.
        foreach (var x in new[] { 8.0, -8.0, 11.0, -11.0 })
        {
            Assert.Equal(Flank.Centre, BallLocation.ClassifyFlank(x, true, HalfWidth));
            Assert.True(System.Math.Abs(BallLocation.ComputeTouchlineDrone(x, HalfWidth, true)) > 0.0);
        }

        // Deep in the middle both agree on silence.
        foreach (var x in new[] { 0.0, 4.0, -4.0 })
        {
            Assert.Equal(Flank.Centre, BallLocation.ClassifyFlank(x, true, HalfWidth));
            Assert.Equal(0.0, BallLocation.ComputeTouchlineDrone(x, HalfWidth, true), 6);
        }
    }

    [Fact]
    public void ComputeAirLoudness_StartsAtNothingExactlyWhereTheCueTurnsOn()
    {
        // The aerial trigger and the bottom of the ramp are one height, so the cue always fades in from
        // its quietest. If the ramp floor drifted above the trigger the sound would appear part-way up
        // the ramp with an audible step; if it drifted below, the first part of every flight would be
        // inaudible. The plugin derives its trigger from AirFloorMetres, so pinning the ramp here pins
        // both ends of that relationship.
        Assert.Equal(0.0, BallLocation.ComputeAirLoudness(BallLocation.AirFloorMetres), 6);
        Assert.Equal(0.0, BallLocation.ComputeAirLoudness(BallLocation.AirFloorMetres + 0.0001), 3);
    }

    [Fact]
    public void ComputeAirLoudness_ReachesFullAtTheCeilingAndClampsAbove()
    {
        // Above the ceiling the value must clamp, not keep climbing: past 1 the caller's decibel map
        // would run louder than the cue is ever meant to be.
        Assert.Equal(1.0, BallLocation.ComputeAirLoudness(BallLocation.AirCeilingMetres), 6);
        Assert.Equal(1.0, BallLocation.ComputeAirLoudness(BallLocation.AirCeilingMetres + 10.0), 6);
        Assert.Equal(1.0, BallLocation.ComputeAirLoudness(100.0), 6);
    }

    [Fact]
    public void ComputeAirLoudness_IsSilentForABallOnTheGround()
    {
        // Below the threshold there is no flight. Clamping to 0 rather than going negative is what lets
        // the level be computed and sent whatever the cue is doing, so it is already correct at the
        // instant a flight starts.
        Assert.Equal(0.0, BallLocation.ComputeAirLoudness(0.0), 6);
        Assert.Equal(0.0, BallLocation.ComputeAirLoudness(1.0), 6);
        Assert.Equal(0.0, BallLocation.ComputeAirLoudness(-5.0), 6);
    }

    [Fact]
    public void ComputeAirLoudness_RisesLinearlyBetweenTheEnds()
    {
        // Halfway up in metres is halfway up the ramp. The caller maps this to decibels, so linear here
        // is what makes the swell sound even to the ear.
        double mid = (BallLocation.AirFloorMetres + BallLocation.AirCeilingMetres) / 2.0;
        Assert.Equal(0.5, BallLocation.ComputeAirLoudness(mid), 6);

        double quarter = BallLocation.AirFloorMetres
            + (BallLocation.AirCeilingMetres - BallLocation.AirFloorMetres) * 0.25;
        Assert.Equal(0.25, BallLocation.ComputeAirLoudness(quarter), 6);
    }

    [Fact]
    public void ComputeAirLoudness_TracksTheBallBackDown()
    {
        // A pure function of current height: the same height gives the same level whether the ball is on
        // the way up or on the way down. Holding a peak would need state, and loudness would stop meaning
        // "how high is it now".
        Assert.Equal(BallLocation.ComputeAirLoudness(6.0), BallLocation.ComputeAirLoudness(6.0), 6);
        Assert.True(BallLocation.ComputeAirLoudness(8.0) > BallLocation.ComputeAirLoudness(5.0));
        Assert.True(BallLocation.ComputeAirLoudness(3.0) < BallLocation.ComputeAirLoudness(5.0));
    }

    [Fact]
    public void ComputeAirLoudness_CeilingIsAboveTheFloor()
    {
        // A ceiling at or below the floor would divide by zero or invert the ramp, making a high ball
        // quieter than a low one. Cheap to assert, and these are the two numbers most likely to be
        // retuned by hand after listening.
        Assert.True(BallLocation.AirCeilingMetres > BallLocation.AirFloorMetres);
    }

    [Fact]
    public void ComputeTouchlineDrone_KeepsItsSilentLaneNarrowerThanTheSpokenCentralThird()
    {
        // The containment rule above only holds while the drone's lane is the narrower of the two. Widening
        // it back toward the spoken third — the obvious "fix" for the deliberate disagreement — would open a
        // band the readout calls a wing while the drone is silent. Pin the ordering directly, so that change
        // fails here with the reason rather than as a puzzling containment failure.
        Assert.True(LaneEdge < FlankCentreEdge);

        // Just inside the spoken boundary the drone must already be sounding, which is the same statement
        // measured at the tightest point it can be measured.
        double justInsideCentre = FlankCentreEdge - 0.001;
        Assert.Equal(Flank.Centre, BallLocation.ClassifyFlank(justInsideCentre, true, HalfWidth));
        Assert.True(System.Math.Abs(BallLocation.ComputeTouchlineDrone(justInsideCentre, HalfWidth, true)) > 0.0);
    }
}
