using System;

namespace FM27Access.Navigation;

/// <summary>
/// Pure fire-once decision for the "good defence" sound: one short cue the moment the engine's commentary
/// event stream shows a successful or impactful defensive action — an interception, a tackle won, a save, a
/// block, a ball won back. The plugin maps the engine ids onto <c>occurred</c> and attributes the acting
/// team via the event's <c>Team</c> (see <c>MatchBallLocation.ScanDefence</c>); this struct owns the subtle
/// parts — baseline the first read silently, freeze during a replay, and collapse the same defensive moment
/// narrated by more than one event — so they are unit-testable without the game.
///
/// The engine's <c>Team</c> on a defensive event is the side that MADE the play (confirmed live: interceptions
/// and clearances attribute to the defending team, not the side that lost the ball). So a play by the
/// reference team sounds "gooddefence" and one by the opposition sounds "oppgooddefence". The tick metronome
/// still conveys who holds the ball; this replaces the old geometry possession-flip sound, which fired on
/// non-events.
///
/// Direction is tracked per side so a same-moment dedupe on one team cannot swallow the other team's play in
/// the same tick. A fire is suppressed while its engine <c>TimeSlice</c> is within
/// <see cref="DuplicateWindowSlices"/> of that side's previous fire (one defensive phase narrated by two
/// events — e.g. a block then a smother — sounds once); distinct defensive moments are seconds of engine time
/// apart, far wider than the window.
/// </summary>
public struct DefenceOneShot
{
    // Two defensive events for the same side closer than this (in engine time slices) are one phase narrated
    // twice; distinct turnovers/stops are a full passage of play apart. Mirrors PutInPlayOneShot's same-take
    // window, which is calibrated to the engine's flavour-double spacing.
    private const int DuplicateWindowSlices = 25;

    // Played high-water we have accounted for, live and inside a replay (see PlayedEventCursor).
    private PlayedEventCursor _cursor;
    // Engine time of the last fire, per side, for the same-moment dedupe. Compared by distance, not order, so
    // a playback timeline reset (a new half) cannot suppress a real play.
    private bool _firedRef;
    private int _lastRefSlice;
    private bool _firedOpp;
    private int _lastOppSlice;

    /// <summary>
    /// The played event index already accounted for, or -1 before the first read has baselined (so a
    /// reconnect mid-match adopts the already-shown events silently instead of firing for them).
    /// </summary>
    public readonly int HighWater => _cursor.HighWater;

    /// <summary>
    /// Fold one scan result in and return the sfx token to fire this tick ("gooddefence" when the reference
    /// team made the play, "oppgooddefence" when the opposition did), or null for nothing.
    /// <paramref name="ok"/> false (no match / unreadable stream) leaves the state untouched; the first
    /// readable tick baselines silently; a replay sounds the play again on its own bookmark as it is
    /// re-shown. <paramref name="occurred"/> says a good-defence event entered the played span since
    /// the last advance, <paramref name="forRef"/> whether the reference team made it, and
    /// <paramref name="slice"/> is that event's engine time for the same-moment dedupe.
    /// </summary>
    public string Observe(bool ok, int lastEventIndex, bool inReplay, bool occurred, bool forRef, int slice)
    {
        bool mayFire = _cursor.Advance(ok, lastEventIndex, inReplay);
        // A pass boundary (live into a replay, or back) re-shows the same engine moments, so the
        // same-moment dedupe cannot carry across it — see ShotOneShot for the same reasoning.
        if (_cursor.PassChanged) { _firedRef = false; _firedOpp = false; }
        if (!mayFire || !occurred) return null;

        if (forRef)
        {
            if (_firedRef && Math.Abs(slice - _lastRefSlice) < DuplicateWindowSlices) return null;
            _firedRef = true;
            _lastRefSlice = slice;
            return "gooddefence";
        }

        if (_firedOpp && Math.Abs(slice - _lastOppSlice) < DuplicateWindowSlices) return null;
        _firedOpp = true;
        _lastOppSlice = slice;
        return "oppgooddefence";
    }
}
