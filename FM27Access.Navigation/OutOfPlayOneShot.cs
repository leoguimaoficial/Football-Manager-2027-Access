namespace FM27Access.Navigation;

/// <summary>
/// Which restart the engine's commentary event stream says the ball went out for. <see cref="None"/>
/// means no restart event this scan. The plugin maps the engine's
/// <c>EventRestart{ThrowIn,Corner,GoalKick}</c> ids onto these; only these three fire out-of-play sounds
/// (the <c>EventSeeBallOutOfPlayFor*</c> family was confirmed live to never fire; the later <c>Take*</c>
/// family drives the put-in-play ping instead, see <see cref="PutInPlayOneShot"/>).
/// </summary>
public enum RestartKind { None, ThrowIn, Corner, GoalKick }

/// <summary>
/// The out-of-play sound to play this tick, decided by <see cref="OutOfPlayOneShot"/>. Corner is split by
/// team because the two corners have their own sounds; a throw-in and a goal kick are team-agnostic.
/// <see cref="OutOfPlaySound.None"/> is the honest result when the ball is dead but no restart event has
/// classified it yet.
/// </summary>
public enum OutOfPlaySound { None, ThrowIn, GoalKick, CornerFor, CornerAgainst }

/// <summary>
/// Pure fire-once decision for the out-of-play sounds, driven solely by the engine's commentary event
/// stream (authoritative restart type + team, read by the plugin's <c>MatchBallLocation.ScanRestarts</c>).
/// Ball geometry never fires a sound here — the event is more accurate (confirmed live to enter the played
/// window at the moment the ball goes out) and is the only source that can name the restart and the team;
/// geometry's sole role is arming the once-per-episode latch when the ball returns to play (the engine's
/// put-in-play take event arms it too, covering restarts too fast for the 2 Hz geometry sampling). The
/// game-coupled reads live in the plugin; this holds the played high-water event index and the latch, so
/// the subtle parts — baseline the first read, sound again when a replay re-shows the restart, and never
/// sound twice for one dead ball — are unit-testable without the game. A ball in the net (a goal) is owned
/// by the goal detector and never sounds here.
/// </summary>
public struct OutOfPlayOneShot
{
    // Event-stream bookmark (mirrors GoalOneShot): the played high-water we have scanned for restarts,
    // live and inside a replay. See PlayedEventCursor.
    private PlayedEventCursor _cursor;
    // Whether the current dead-ball episode has already sounded (armed again when geometry shows the ball
    // back in play), so a duplicate restart event for the same dead ball cannot sound twice.
    private bool _episodeSounded;

    /// <summary>
    /// The <c>afterIndex</c> to hand the next restart scan: the played event index already accounted for, or
    /// -1 before the first read has baselined (so a reconnect mid-match adopts the already-shown restarts
    /// silently instead of sounding for them).
    /// </summary>
    public readonly int EventHighWater => _cursor.HighWater;

    /// <summary>
    /// Fold one tick in and return the out-of-play sound to play, or <see cref="OutOfPlaySound.None"/>. The
    /// event arguments mirror the goal scan (<paramref name="evOk"/> false leaves the event state untouched;
    /// the first readable tick baselines silently; a replay re-sounds the restart it re-shows, on its own
    /// bookmark). <paramref name="geomKind"/>
    /// is the current ball-geometry classification, used only for episode tracking (back in play arms the
    /// next dead ball's latch) — it never fires a sound. <paramref name="evPutOccurred"/> is the engine's
    /// ball-put-back-into-play event from the same scan span: it also arms the next episode, because a fast
    /// out-taken-out-again sequence can pass between two geometry samples (~2 Hz) without ever SAMPLING the
    /// ball in play, which used to swallow the second restart's sound.
    /// </summary>
    public OutOfPlaySound Observe(
        bool evOk, int evLastIndex, bool inReplay,
        bool evOccurred, RestartKind evKind, bool evForRef, bool evPutOccurred,
        OutOfPlayKind geomKind)
    {
        // --- Event stream latch (mirror GoalOneShot): baseline first, then advance the bookmark for this
        //     pass — live, or the replay's own — and capture a fire. ---
        RestartKind firedKind = RestartKind.None;
        bool firedForRef = false;
        bool putFired = false;
        if (_cursor.Advance(evOk, evLastIndex, inReplay))
        {
            if (evOccurred) { firedKind = evKind; firedForRef = evForRef; }
            putFired = evPutOccurred;
        }
        // Crossing into or out of a replay starts a fresh pass over ground already played, so the
        // once-per-episode latch must not carry across: the dead ball the replay re-shows has already
        // sounded on the live pass, and would otherwise be swallowed as a duplicate.
        if (_cursor.PassChanged) _episodeSounded = false;

        // Back in play arms the next episode: either sampled by geometry, or reported by the engine's take
        // event. The take precedes any new restart award in engine time, so arming before the fire check is
        // correct even when both land in the same scan span.
        if (geomKind == OutOfPlayKind.InPlay || putFired) _episodeSounded = false;

        // The engine event is the only trigger: authoritative restart type and team, in sync with playback.
        if (firedKind != RestartKind.None && !_episodeSounded)
        {
            _episodeSounded = true;
            return Map(firedKind, firedForRef);
        }

        return OutOfPlaySound.None;
    }

    private static OutOfPlaySound Map(RestartKind kind, bool forRef) => kind switch
    {
        RestartKind.ThrowIn => OutOfPlaySound.ThrowIn,
        RestartKind.GoalKick => OutOfPlaySound.GoalKick,
        RestartKind.Corner => forRef ? OutOfPlaySound.CornerFor : OutOfPlaySound.CornerAgainst,
        _ => OutOfPlaySound.None,
    };
}
