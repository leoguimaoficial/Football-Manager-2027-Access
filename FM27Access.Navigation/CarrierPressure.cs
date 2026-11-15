namespace FM27Access.Navigation;

/// <summary>
/// How heavily the player on the ball is being closed down, as a 0..1 level for the pressure bed.
///
/// Sighted players read this instantly — whether the carrier has time to pick a pass or is swarmed and
/// about to lose it — and nothing else the mod says carries it. The possession tick says WHO has the ball
/// and the pan says WHERE; this says how much trouble he is in.
///
/// Count outweighs distance, deliberately — but through the divisor rather than the per-man share. Because
/// <see cref="Normalise"/>'s divisor is the opponent count that means maximum pressure, it caps what any
/// smaller number of opponents can reach: with a divisor of 3, one opponent can never exceed the lowest
/// third however tightly he marks, and two can never exceed two thirds. Only bodies open the upper bands.
/// A lone marker and a three-man swarm are different in kind, not merely in degree, and any weighting that
/// let one man's closeness reach a band reserved for two blurred exactly that.
///
/// The result is continuous in both count and closeness, and — where the base share is zero — falls to
/// nothing at the radius rather than stepping off a cliff. That matters: the levels are banded into stems
/// for playback, and a value that jumped at the radius would make the stems chatter at the boundary, the
/// same failure PossessionArbiter exists to prevent for the ticks.
///
/// Split into a per-opponent contribution and a final normalisation so the caller can accumulate inside the
/// player walk it already runs, without building a collection per sample. Pure; every threshold is a
/// parameter so the tuning stays in the reloadable plugin. Unit-tested without the game.
/// </summary>
public static class CarrierPressure
{
    /// <summary>
    /// One opponent's share of the pressure on the carrier. Zero beyond <paramref name="radius"/>;
    /// <paramref name="baseShare"/> at exactly the radius, rising to 1 at the ball. Sum these over every
    /// opponent within the radius and pass the total to <see cref="Normalise"/>.
    /// </summary>
    /// <param name="distance">Metres from this opponent to the ball.</param>
    /// <param name="radius">How far out an opponent still counts as pressing.</param>
    /// <param name="baseShare">
    /// What an opponent contributes for merely being inside the radius, 0..1. This is the count-versus-
    /// distance dial: at 1 only the number of opponents matters, at 0 only their closeness does.
    /// </param>
    public static double Contribution(double distance, double radius, double baseShare)
    {
        if (double.IsNaN(distance) || distance > radius) return 0.0;
        if (distance < 0.0) distance = 0.0;
        return baseShare + (1.0 - baseShare) * (1.0 - distance / radius);
    }

    /// <summary>
    /// The summed contributions as a 0..1 bed level. <paramref name="divisor"/> is the total that counts as
    /// maximum pressure — near enough the number of opponents on top of the carrier before the bed is at
    /// full — and anything beyond it clamps rather than overshooting into a louder-than-maximum level.
    /// </summary>
    public static double Normalise(double sum, double divisor)
    {
        if (double.IsNaN(sum) || sum <= 0.0) return 0.0;
        double level = sum / divisor;
        return level > 1.0 ? 1.0 : level;
    }
}
