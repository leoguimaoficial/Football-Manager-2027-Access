namespace FM27Access.Navigation;

/// <summary>
/// Pure fire-once decision for the match goal sound, driven by the engine event-stream scan (the
/// game-coupled read lives in the Plugin's <c>MatchBallLocation.ScanGoals</c>). Holds the playback
/// high-water event index we have already scanned for goals and whether we have baselined yet, so the
/// subtle parts — baseline the first read, freeze during the goal's own replay, and never fire twice for
/// one goal — are unit-testable without the game.
///
/// A goal is one of the engine's goal events (a scored goal or one chalked off, both of which earn the
/// cue) reaching the PLAYBACK high-water event index
/// (<c>m_currentLastEventIndex</c>), which is in sync with the highlight the player watches — not the
/// simulation frontier (ahead, a spoiler) and not ball geometry (unreliable). A replay rewinds the event
/// window and re-crosses the goal; <see cref="PlayedEventCursor"/> tracks that second pass on its own
/// bookmark, so the goal sounds again with the replay and still cannot echo when live resumes.
/// </summary>
public struct GoalOneShot
{
    private PlayedEventCursor _cursor;

    /// <summary>
    /// The <c>afterIndex</c> to hand the next scan: the played event index already accounted for on the
    /// current pass, or -1 before the first read has baselined (which tells the scan to baseline only, not
    /// search — so a reconnect mid-match adopts the already-shown events silently instead of firing for
    /// them). See <see cref="PlayedEventCursor"/>.
    /// </summary>
    public readonly int HighWater => _cursor.HighWater;

    /// <summary>
    /// Fold one scan result in and return the sfx token to fire this tick ("goalfor" / "goalagainst"), or
    /// null for nothing. <paramref name="ok"/> false (no match / unreadable stream) leaves the state
    /// untouched. The first readable tick baselines silently. A newly-crossed goal fires, attributed by
    /// <paramref name="forRef"/> — live as the highlight reaches it, and again when a replay re-shows it,
    /// which is the same goal happening on screen a second time and the loudest thing the player has to
    /// follow it by. The live bookmark never moves during the replay, so the goal cannot echo a third time
    /// when live resumes.
    /// </summary>
    public string Observe(bool ok, int lastEventIndex, bool inReplay, bool crossed, bool forRef)
    {
        if (!_cursor.Advance(ok, lastEventIndex, inReplay)) return null;
        return crossed ? (forRef ? "goalfor" : "goalagainst") : null;
    }
}
