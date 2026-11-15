using System;

namespace FM27Access.Navigation;

/// <summary>
/// Pure fire-once decision for the "put in play" ping: one short sound the moment the engine's commentary
/// event stream shows the ball being put back into play — a kick-off (each half, and after a goal), a
/// throw-in being taken (not awarded), a goal kick, a corner, or a free kick. A penalty is excluded: it is
/// also an attempt on goal, and the shot-attempt cue sounds on the same engine event at the same moment. The plugin
/// maps the engine's <c>EventKickOff</c> / <c>Take*</c> ids onto <c>occurred</c> (see
/// <c>MatchBallLocation.ScanRestarts</c>, which detects these in the same played-window pass as the
/// restart awards); this struct owns the subtle parts — baseline the first read silently, freeze during a
/// replay, and never ping twice for one take — so they are unit-testable without the game.
///
/// The engine can narrate one take with more than one commentary event at (nearly) the same engine time
/// (flavour variants of the same kick), and a scan-span boundary can split them across ticks, so a fire is
/// suppressed while its engine <c>TimeSlice</c> is within <see cref="DuplicateWindowSlices"/> of the
/// previous fire: one take pings once. Distinct takes are separated by a full dead-ball pause, far wider
/// than the window.
/// </summary>
public struct PutInPlayOneShot
{
    // Two commentary events closer together than this (in engine time slices) are the same take narrated
    // twice; two real takes have a whole dead-ball pause (~100+ slices, confirmed live for the Restart* ->
    // Take* gap alone) between them.
    private const int DuplicateWindowSlices = 25;

    // Played high-water we have accounted for, live and inside a replay (see PlayedEventCursor).
    private PlayedEventCursor _cursor;
    // Engine time of the last ping, for the same-take dedupe. Compared by distance, not order, so a
    // playback timeline reset (e.g. a new half) cannot suppress a real kick-off.
    private bool _fired;
    private int _lastFiredSlice;

    /// <summary>
    /// The played event index already accounted for, or -1 before the first read has baselined (so a
    /// reconnect mid-match adopts the already-shown takes silently instead of pinging for them).
    /// </summary>
    public readonly int HighWater => _cursor.HighWater;

    /// <summary>
    /// Fold one tick in and return whether the put-in-play ping should sound. The arguments mirror the
    /// restart scan: <paramref name="ok"/> false leaves the state untouched; the first readable tick
    /// baselines silently; a replay pings the take again on its own bookmark as it is re-shown.
    /// <paramref name="occurred"/> says a put-in-play event entered the played span since the
    /// last advance, and <paramref name="slice"/> is that event's engine time for the same-take dedupe.
    /// </summary>
    public bool Observe(bool ok, int lastIndex, bool inReplay, bool occurred, int slice)
    {
        bool mayFire = _cursor.Advance(ok, lastIndex, inReplay);
        // A pass boundary (live into a replay, or back) re-shows the same engine moments, so the
        // same-take dedupe cannot carry across it — see ShotOneShot for the same reasoning.
        if (_cursor.PassChanged) _fired = false;
        if (!mayFire || !occurred) return false;
        if (_fired && Math.Abs(slice - _lastFiredSlice) < DuplicateWindowSlices) return false;

        _fired = true;
        _lastFiredSlice = slice;
        return true;
    }
}
