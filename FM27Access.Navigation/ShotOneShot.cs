using System;

namespace FM27Access.Navigation;

/// <summary>
/// Pure fire-once decision for the "shot attempt" sound: one short cue the moment the engine's commentary
/// event stream shows an attempt on goal — a shot, a header at goal, a volley, a struck free kick, a penalty
/// being taken — regardless of where it ends up. The plugin maps the engine ids onto <c>occurred</c> (see
/// <c>MatchBallLocation.ScanShots</c>); this struct owns the subtle parts — baseline the first read silently,
/// sound again when a replay re-shows the strike, and collapse one strike narrated by more than one event —
/// so they are unit-testable without the game.
///
/// The point of the cue is the gap it opens. The engine narrates the strike first and its outcome a beat
/// later (live: <c>EventDefaultShot</c> at slice 715, <c>EventShotWide</c> at 721 — about a second and a half
/// of playback), so the player hears the attempt, then waits to learn from the goal, save, or restart sound
/// whether it went in. Both teams' attempts sound the same: the possession metronome already says who has
/// the ball, and not knowing whose chance it is until it resolves is the tension the cue exists to create.
///
/// The dedupe is deliberately much tighter than <see cref="DefenceOneShot"/>'s. A rebound — shot, save,
/// immediate second shot — is two genuine attempts a second or two apart and MUST sound twice; that is the
/// most dramatic moment in a match to be blind for. So the window only spans one strike narrated by two ids
/// at the same engine moment, not a whole passage of play.
/// </summary>
public struct ShotOneShot
{
    // Two attempt events closer than this (in engine time slices) are one strike narrated twice; playback
    // runs at roughly four slices per game second, so this is under a second and a genuine rebound off a
    // save — seconds of engine time later — still sounds. Distance, not order, so a playback timeline reset
    // (a new half) cannot suppress a real attempt.
    private const int DuplicateWindowSlices = 3;

    // Played high-water we have accounted for, live and inside a replay (see PlayedEventCursor).
    private PlayedEventCursor _cursor;
    // Engine time of the last fire, for the same-moment dedupe.
    private bool _fired;
    private int _lastSlice;

    /// <summary>
    /// The played event index already accounted for on the current pass, or -1 before the first read has
    /// baselined (so a reconnect mid-match adopts the already-shown events silently instead of firing for
    /// them).
    /// </summary>
    public readonly int HighWater => _cursor.HighWater;

    /// <summary>
    /// Fold one scan result in and return the sfx token to fire this tick ("shotattempt"), or null for
    /// nothing. <paramref name="ok"/> false (no match / unreadable stream) leaves the state untouched; the
    /// first readable tick baselines silently; a replay sounds the attempt again on its own bookmark as the
    /// strike is re-shown. <paramref name="occurred"/> says an attempt-on-goal event entered the
    /// played span since the last advance, and <paramref name="slice"/> is that event's engine time for the
    /// same-moment dedupe.
    /// </summary>
    public string Observe(bool ok, int lastEventIndex, bool inReplay, bool occurred, int slice)
    {
        bool mayFire = _cursor.Advance(ok, lastEventIndex, inReplay);
        // Crossing into or out of a replay starts a fresh pass over the same engine moments, so the
        // same-moment dedupe must not carry across: a replayed strike sits at the engine time it already
        // fired at, and would otherwise be read as one strike narrated twice.
        if (_cursor.PassChanged) _fired = false;
        if (!mayFire || !occurred) return null;

        if (_fired && Math.Abs(slice - _lastSlice) < DuplicateWindowSlices) return null;
        _fired = true;
        _lastSlice = slice;
        return "shotattempt";
    }
}
