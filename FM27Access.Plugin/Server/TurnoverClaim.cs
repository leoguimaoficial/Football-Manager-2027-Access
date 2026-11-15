namespace FM27Access.Plugin.Server;

/// <summary>
/// Pure decision for the authoritative possession override: given the engine's own turnover events, which
/// team (if any) should be handed the ball outright this tick, bypassing PossessionArbiter's dwell.
///
/// The dwell exists to filter UNCERTAIN evidence — a geometric read that cannot tell a deflection from a
/// tackle without watching it persist. An engine event is not uncertain: it names the team that won the
/// ball. Making it serve the dwell anyway measured a median 2.75s of latency behind the engine's account of
/// the same turnovers, with one in three never registering at all, because the cue was re-deriving a fact
/// it had already been told. So a confirmed event concedes possession immediately and the dwell continues to
/// govern everything the engine does not narrate.
///
/// This owns the three subtleties the sibling one-shots own (see <c>Navigation.DefenceOneShot</c>, whose
/// shape this mirrors), so they are unit-testable without the game:
///   - BASELINE the first readable scan silently, so connecting mid-match adopts the already-played events
///     instead of conceding possession for a turnover the player watched five minutes ago.
///   - FREEZE during a replay: no claim and no advance, so a replayed interception cannot move the live
///     claim, and it still lands when live play resumes.
///   - FORGET on a break, so a stale high-water from a finished match cannot suppress every event of the
///     next one — event indices restart, and an index from the old match would sit above all of them.
///
/// No same-moment dedupe, unlike the sound one-shots: conceding the ball twice to the team that already has
/// it is a no-op, so collapsing duplicate narration of one phase would add state for no behavioural gain.
/// </summary>
public struct TurnoverClaim
{
    private int _highWater;
    private bool _seen;

    /// <summary>
    /// The played event index already accounted for, or -1 before the first scan has baselined — which is
    /// what makes a fresh start adopt the played events silently rather than acting on them.
    /// </summary>
    public readonly int HighWater => _seen ? _highWater : -1;

    /// <summary>
    /// Forget the baseline. For leaving a match or any break after which the event indices no longer relate
    /// to the ones already seen; the next readable scan re-baselines silently.
    /// </summary>
    public void Reset()
    {
        _highWater = 0;
        _seen = false;
    }

    /// <summary>
    /// Fold one scan in and return the team the engine says now has the ball, or
    /// <see cref="PossessionHolder.None"/> for "nothing to act on this tick".
    /// </summary>
    public PossessionHolder Observe(bool ok, int lastEventIndex, bool inReplay, bool occurred, bool forRef)
    {
        // Unreadable stream: leave the state untouched so the high-water is not advanced past events that
        // were never examined.
        if (!ok) return PossessionHolder.None;

        if (!_seen)
        {
            _highWater = lastEventIndex;
            _seen = true;
            return PossessionHolder.None;
        }

        if (inReplay) return PossessionHolder.None;

        _highWater = lastEventIndex;
        if (!occurred) return PossessionHolder.None;

        return forRef ? PossessionHolder.Reference : PossessionHolder.Opposition;
    }
}
