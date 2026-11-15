namespace FM27Access.Navigation;

/// <summary>
/// Which third of the pitch the ball is in, named relative to the reference team.
/// </summary>
public enum BallThird
{
    DefendingEnd,
    MiddleThird,
    AttackingThird,
}

/// <summary>Which flank the ball is on, named relative to the reference team's attacking direction.</summary>
public enum Flank
{
    Left,
    Centre,
    Right,
}

/// <summary>
/// Whether the ball is inside a penalty or goal (six-yard) area, named relative to the reference team.
/// </summary>
public enum BoxZone
{
    None,
    OppositionPenaltyArea,
    OppositionSixYardBox,
    OwnPenaltyArea,
    OwnSixYardBox,
}

/// <summary>
/// Where a ball that has left the field of play crossed a boundary. <see cref="Touchline"/> is a
/// throw-in exit over a side line; <see cref="GoalLine"/> is a dead-ball exit over an end line (a goal
/// kick or corner, including a shot that clears the crossbar); <see cref="Net"/> is a goal — the ball
/// crossed the goal-line between the posts and under the bar. <see cref="Net"/> is kept distinct from
/// <see cref="GoalLine"/> so the goal-line audio cue never fires for a scored goal.
/// </summary>
public enum OutOfPlayKind
{
    InPlay,
    Touchline,
    GoalLine,
    Net,
}

/// <summary>
/// Classifies the ball's position on the pitch into speakable zones. Pure and game-independent: the
/// plugin reads the ball's engine coordinate, the reference team's attacking direction, and the pitch
/// dimensions from the live match and passes the primitives here, so every split stays unit-testable.
/// The engine's Z axis runs goal-to-goal with 0 at the centre spot; X is the width axis with 0 on the
/// halfway line down the middle of the pitch.
/// </summary>
public static class BallLocation
{
    // Penalty and goal (six-yard) area dimensions are fixed by the Laws of the Game, independent of the
    // pitch size, so they are absolute metres rather than fractions of the pitch. Depth is measured from
    // the goal-line toward the centre; half-width is measured from the centre of the goal outward.
    private const double PenaltyAreaDepth = 16.5;
    private const double PenaltyAreaHalfWidth = 20.16;   // 40.32 m box, half of it either side of centre
    private const double SixYardDepth = 5.5;
    private const double SixYardHalfWidth = 9.16;         // 18.32 m box

    // Calibration of the width axis handedness: when the reference team attacks toward +Z, is its RIGHT
    // wing at +X? Unity world space is left-handed (+X right, +Y up, +Z forward), so a player facing +Z
    // has their right hand toward +X. Confirmed live in-game (2026-07-02) — the spoken flank matched the
    // action on the first check. Flip this one flag if a future engine build proves the ball coordinate
    // uses the opposite handedness.
    private const bool RightWingIsPositiveXWhenAttackingUpfield = true;

    // The touchline drones fall silent across a narrower central lane than the spoken flank's central
    // third: half its depth, so the lane is the middle SIXTH of the half-width either side of centre.
    // The drone is a continuous signal and the spoken flank a coarse verbal label, so the drone starts
    // moving while the readout still says "central". Narrower is the only safe direction — see
    // ComputeTouchlineDrone for the containment rule that keeps the two from contradicting each other.
    private const double DroneCentralLaneFraction = 6.0;

    /// <summary>
    /// Maps the ball's goal-to-goal coordinate to a third relative to the reference team's
    /// attacking direction. <paramref name="lengthCoord"/> is the ball's position on the length
    /// axis; <paramref name="attackingPositive"/> is true when that team attacks toward larger
    /// coordinates; <paramref name="halfPitchLength"/> is half the goal-to-goal length in the same
    /// units. A ball past a goal-line clamps to the nearer end's third.
    /// </summary>
    public static BallThird Classify(double lengthCoord, bool attackingPositive, double halfPitchLength)
    {
        // Signed distance toward the reference team's attacking goal, normalised to
        // 0 (own goal-line) .. 1 (opponent goal-line), then split into equal thirds.
        double towardAttack = attackingPositive ? lengthCoord : -lengthCoord;
        double progress = (towardAttack + halfPitchLength) / (2.0 * halfPitchLength);
        if (progress < 1.0 / 3.0) return BallThird.DefendingEnd;
        if (progress < 2.0 / 3.0) return BallThird.MiddleThird;
        return BallThird.AttackingThird;
    }

    /// <summary>
    /// Maps the ball's width coordinate to a flank relative to the reference team's attacking direction.
    /// <paramref name="widthCoord"/> is the ball's position on the width axis (0 at the middle of the
    /// pitch); the pitch width is split into equal left / centre / right thirds, so the central band is
    /// the middle third of the width. Left and right flip with the attacking direction so they always
    /// name the wing as that team would see it looking at the goal it attacks.
    ///
    /// This split is the SPOKEN one and is coarser than the touchline drones' — see
    /// <see cref="ComputeTouchlineDrone"/>, which falls silent over a narrower lane, so a ball this calls
    /// central can already be faintly droning. Keep this band the wider of the two.
    /// </summary>
    public static Flank ClassifyFlank(double widthCoord, bool attackingPositive, double halfPitchWidth)
    {
        // Positive = toward the reference team's right touchline.
        double rightward = attackingPositive ? widthCoord : -widthCoord;
        if (!RightWingIsPositiveXWhenAttackingUpfield) rightward = -rightward;
        double band = halfPitchWidth / 3.0; // central third of the full width straddles the halfway line
        if (rightward > band) return Flank.Right;
        if (rightward < -band) return Flank.Left;
        return Flank.Centre;
    }

    /// <summary>
    /// Maps the ball's goal-to-goal coordinate to a stereo pan position for the match audio cues:
    /// -1 is full left, 0 the centre spot, +1 full right. The stereo field is the televised view of the
    /// pitch — the HOME team attacks left to right in the first half, the way a scoreboard puts the home
    /// side first, and the sides swap afterwards while the camera stays put.
    ///
    /// <paramref name="screenRightIsPositiveZ"/> is that camera placement: which world direction appears
    /// to the right. It is fixed for the whole match (see the caller, which reads the home team's
    /// direction at kick-off), so the same spot on the pitch keeps its pan through half time and extra
    /// time while the play audibly changes direction. A ball past a goal-line clamps to that edge.
    /// </summary>
    public static double ComputeLengthPan(double lengthCoord, bool screenRightIsPositiveZ,
                                          double halfPitchLength)
    {
        double rightward = screenRightIsPositiveZ ? lengthCoord : -lengthCoord;
        double pan = rightward / halfPitchLength;
        if (pan < -1.0) return -1.0;
        if (pan > 1.0) return 1.0;
        return pan;
    }

    /// <summary>
    /// How near the ball is to each touchline, as one signed value for the touchline drones: the sign picks
    /// which physical touchline (negative = the near one, positive = the far one) and the magnitude is how
    /// close it is to that touchline, 0 to 1. Exactly 0 anywhere in the middle lane, rising from 0 at the lane
    /// boundary to 1 at the touchline, and clamped at 1 for a ball that has gone out.
    ///
    /// The silent lane is <see cref="DroneCentralLaneFraction"/> — half the depth of the central third
    /// <see cref="ClassifyFlank"/> speaks — which makes the drone react to the ball sooner and spreads the
    /// ramp over more of the pitch, so a given step sideways is easier to hear. The two are therefore
    /// deliberately NOT the same split, and the invariant between them is containment, not equality:
    /// anything the readout calls a wing is always sounding, while the outer part of what it calls "central"
    /// may already be faintly sounding. That direction is the safe one — the drone can lead the readout but
    /// must never lag it, or the player would hear silence for a ball being called wide. Widening this lane
    /// past <see cref="ClassifyFlank"/>'s band would invert that and is what the containment test forbids.
    ///
    /// Because the ramp starts at 0 exactly where the silent lane ends, the drone fades in and out purely as
    /// a consequence of the ball moving — there is no separate fade to trigger and no step to smooth at the
    /// boundary. Passing through the lane sends the value cleanly through 0, so a ball crossing the pitch
    /// fades one drone out and the other in with silence between rather than crossfading two drones over
    /// each other.
    ///
    /// Unlike <see cref="ClassifyFlank"/>, this takes NO attacking direction: a touchline is a fixed piece of
    /// ground and does not swap ends at half time. That is deliberate, and it is what keeps this axis
    /// coherent with <see cref="ComputeLengthPan"/>, which pins the length axis to the world for the same
    /// reason. If this flipped at half time the pitch would mirror sideways while the stereo field stayed
    /// put, and the two axes would describe different pitches.
    ///
    /// <paramref name="nearIsPositiveX"/> must be the SAME camera placement the pan uses
    /// (<c>screenRightIsPositiveZ</c>), and the two are equal rather than merely related. The camera sits at
    /// the near touchline, so a player facing screen-right has his right hand pointing toward it: a team
    /// attacking left to right has its right wing on the NEAR side. That team attacking +Z has its right wing
    /// at +X (see <see cref="RightWingIsPositiveXWhenAttackingUpfield"/>), so near is +X exactly when
    /// screen-right is +Z. Passing anything else — in particular hardcoding a side, as this once did — pairs a
    /// fixed width axis with a length axis that varies per fixture, and the two then agree only by luck.
    /// </summary>
    public static double ComputeTouchlineDrone(double widthCoord, double halfPitchWidth, bool nearIsPositiveX)
    {
        double band = halfPitchWidth / DroneCentralLaneFraction;
        double toward = nearIsPositiveX ? -widthCoord : widthCoord;
        double distance = System.Math.Abs(toward);
        if (distance <= band) return 0.0;

        double intensity = (distance - band) / (halfPitchWidth - band);
        if (intensity > 1.0) intensity = 1.0;
        return toward < 0 ? -intensity : intensity;
    }

    /// <summary>
    /// Height at which the ball counts as being in the air rather than at someone's feet, in metres.
    /// This is one number serving two jobs — the threshold that turns the in-the-air cue on, and the
    /// bottom of that cue's loudness ramp — and they must be the same number or the sound would either
    /// step in at an audible level or start before it can be heard. The plugin's cue threshold is
    /// derived from this constant rather than repeating it.
    /// </summary>
    public const double AirFloorMetres = 2.5;

    /// <summary>
    /// Height at which the in-the-air cue reaches full loudness, in metres. Anchored to measurement
    /// rather than intuition: sampling live play (47 flights) put the median peak at 5 m, p90 at 8.8 m
    /// and p95 at 10.4 m, with a single 16 m outlier. A ceiling near the top of what actually happens
    /// keeps the loud end reachable — set far above it, almost every ball would sit squashed at the
    /// quiet end of the ramp and the cue would sound broken rather than subtle. Balls above this clamp
    /// at full, which is the right trade: a handful of monster clearances sounding alike costs less
    /// than the common 3-to-8 metre ball having nowhere to move.
    /// </summary>
    public const double AirCeilingMetres = 10.0;

    /// <summary>
    /// How high the ball is, as the 0-to-1 position of its loudness on the in-the-air cue's ramp:
    /// 0 at <see cref="AirFloorMetres"/>, 1 at <see cref="AirCeilingMetres"/> and above, rising
    /// linearly between. The caller maps this to decibels, keeping the levels beside every other level
    /// in the companion and the geometry here — the same split the touchline drones use.
    ///
    /// Returning 0 exactly at the floor is what makes the cue fade in from silence: the trigger fires
    /// just above that height, so the first thing heard is the quietest the ramp goes, with no step.
    /// The value is a pure function of current height, so it falls again as the ball drops — loudness
    /// always means how high the ball is now, never how high it got.
    /// </summary>
    public static double ComputeAirLoudness(double heightMetres)
    {
        double span = AirCeilingMetres - AirFloorMetres;
        double fraction = (heightMetres - AirFloorMetres) / span;
        if (fraction < 0.0) return 0.0;
        if (fraction > 1.0) return 1.0;
        return fraction;
    }

    /// <summary>
    /// Which penalty or goal area the ball is inside, if any, relative to the reference team. Needs both
    /// axes: a box is a rectangle near a goal-line, not a length band. Box dimensions are fixed metres, so
    /// pass coordinates in metres. The six-yard test wins over the enclosing penalty area.
    /// </summary>
    public static BoxZone ClassifyBox(double widthCoord, double lengthCoord, bool attackingPositive, double halfPitchLength)
    {
        double towardAttack = attackingPositive ? lengthCoord : -lengthCoord;
        double toOppositionLine = halfPitchLength - towardAttack; // 0 at the goal the team attacks
        double toOwnLine = halfPitchLength + towardAttack;         // 0 at the team's own goal
        double fromCentre = System.Math.Abs(widthCoord);

        if (toOppositionLine <= SixYardDepth && fromCentre <= SixYardHalfWidth) return BoxZone.OppositionSixYardBox;
        if (toOppositionLine <= PenaltyAreaDepth && fromCentre <= PenaltyAreaHalfWidth) return BoxZone.OppositionPenaltyArea;
        if (toOwnLine <= SixYardDepth && fromCentre <= SixYardHalfWidth) return BoxZone.OwnSixYardBox;
        if (toOwnLine <= PenaltyAreaDepth && fromCentre <= PenaltyAreaHalfWidth) return BoxZone.OwnPenaltyArea;
        return BoxZone.None;
    }

    /// <summary>
    /// Straight-line metres from the ball to the centre of the goal the reference team is attacking (the
    /// middle of that goal-line, where the net sits), so a ball out wide reads farther than one square in
    /// front of goal. Combines both axes: the along-pitch component toward the goal-line and the sideways
    /// component off centre. The along-pitch part is clamped at 0 once the ball is level with or behind the
    /// goal-line, leaving just the sideways distance.
    /// </summary>
    public static double MetresToAttackingGoal(double widthCoord, double lengthCoord, bool attackingPositive, double halfPitchLength)
    {
        double towardAttack = attackingPositive ? lengthCoord : -lengthCoord;
        double alongPitch = halfPitchLength - towardAttack;
        if (alongPitch < 0.0) alongPitch = 0.0;
        return System.Math.Sqrt(alongPitch * alongPitch + widthCoord * widthCoord);
    }

    /// <summary>
    /// Classifies a ball that may have left the field into which boundary it crossed, so the audio cues
    /// can distinguish a throw-in from a goal kick / corner from an actual goal. The goal-line test wins
    /// over the touchline test (a ball past the corner flag is a goal-line exit, not a throw-in). A
    /// goal-line exit is a <see cref="OutOfPlayKind.Net"/> only when the ball is between the posts
    /// (within <paramref name="goalHalfWidth"/> of centre) and at or below the crossbar
    /// (<paramref name="crossbarHeight"/>); a high central ball has cleared the bar, so it is a plain
    /// <see cref="OutOfPlayKind.GoalLine"/> exit. Coordinates and dimensions are in the same metres.
    /// </summary>
    public static OutOfPlayKind ClassifyOutOfPlay(
        double widthCoord, double lengthCoord, double ballHeight, bool attackingPositive,
        double halfPitchLength, double halfPitchWidth,
        double outMargin, double goalHalfWidth, double crossbarHeight)
    {
        if (System.Math.Abs(lengthCoord) > halfPitchLength + outMargin)
        {
            // Reuse the flank split with a virtual width whose central third is exactly the goal mouth
            // (+/- goalHalfWidth): Centre => between the posts.
            var side = ClassifyFlank(widthCoord, attackingPositive, 3.0 * goalHalfWidth);
            if (side == Flank.Centre && ballHeight <= crossbarHeight) return OutOfPlayKind.Net;
            return OutOfPlayKind.GoalLine;
        }
        if (System.Math.Abs(widthCoord) > halfPitchWidth + outMargin) return OutOfPlayKind.Touchline;
        return OutOfPlayKind.InPlay;
    }

    /// <summary>The spoken phrase for a third.</summary>
    public static string Speak(BallThird third)
    {
        switch (third)
        {
            case BallThird.DefendingEnd: return "defensive third";
            case BallThird.MiddleThird: return "middle third";
            default: return "attacking third";
        }
    }

    /// <summary>The spoken phrase for a flank.</summary>
    public static string Speak(Flank flank)
    {
        switch (flank)
        {
            case Flank.Left: return "left";
            case Flank.Right: return "right";
            default: return "central";
        }
    }

    /// <summary>The spoken phrase for a box zone (never called for <see cref="BoxZone.None"/>).</summary>
    public static string Speak(BoxZone box)
    {
        switch (box)
        {
            case BoxZone.OppositionPenaltyArea: return "opposition box";
            case BoxZone.OppositionSixYardBox: return "opposition six yard box";
            case BoxZone.OwnPenaltyArea: return "own box";
            case BoxZone.OwnSixYardBox: return "own six yard box";
            default: return "";
        }
    }
}
