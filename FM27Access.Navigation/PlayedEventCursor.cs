namespace FM27Access.Navigation;

/// <summary>
/// The played-event bookmark shared by every engine-event one-shot (goal, shot, good defence,
/// out-of-play, put-in-play). It owns the one piece of bookkeeping all of them need and none of them
/// should each get subtly different: which events have already been accounted for, live and inside a
/// replay, so a cue sounds once as the highlight reaches it AND once more when a replay shows it again.
///
/// Two bookmarks, because a replay is a second pass over ground already played. <b>Live</b> advances with
/// the playback high-water and only ever moves forward, so when a replay rewinds the event window and
/// re-crosses the same events, live resumes exactly where it was and nothing echoes. <b>Replay</b> is
/// opened fresh at every rewind — the replay starting, and each camera angle that rewinds inside it — and
/// advances across that span independently: the events it re-shows are new to it, so they sound again,
/// once per angle.
///
/// The replay bookmark is discarded when live resumes, never merged into the live one: the replay only
/// ever re-covers already-played ground, so live's bookmark is still correct and still ahead.
/// </summary>
public struct PlayedEventCursor
{
    private int _live;
    private bool _seen;
    private int _replay;
    private bool _inReplay;
    private bool _passChanged;

    /// <summary>
    /// The <c>afterIndex</c> to hand the next scan: the played event index already accounted for on the
    /// current pass (the replay's while a replay shows, otherwise live's), or -1 before the first read has
    /// baselined — which tells the scan to baseline only, not search, so a reconnect mid-match adopts the
    /// already-shown events silently instead of firing for them.
    /// </summary>
    public readonly int HighWater => !_seen ? -1 : (_inReplay ? _replay : _live);

    /// <summary>
    /// True when the last <see cref="Advance"/> crossed between live and a replay. The pass either side of
    /// that boundary re-shows the same engine moments, so a caller holding a same-moment dedupe must clear
    /// it here: without that, a shot replayed at the engine time it already fired at would be mistaken for
    /// the same strike narrated twice and swallowed.
    /// </summary>
    public readonly bool PassChanged => _passChanged;

    /// <summary>
    /// Fold one scan result in and return whether this tick's span may fire. <paramref name="ok"/> false
    /// (no match / unreadable stream) leaves everything untouched. The first readable tick baselines
    /// silently. The first tick of a replay opens the replay bookmark at the rewound position — also
    /// silent, since the span the caller just scanned was measured from the live bookmark and is empty
    /// once the window has rewound behind it. After that, both passes advance and fire the same way.
    /// </summary>
    public bool Advance(bool ok, int lastEventIndex, bool inReplay)
    {
        _passChanged = false;
        if (!ok) return false;
        if (!_seen)
        {
            _live = lastEventIndex;
            _seen = true;
            _inReplay = false;
            return false;
        }

        if (inReplay)
        {
            // Any rewind opens a pass: the replay starting, and equally a second camera angle rewinding
            // inside it. Each angle re-shows the same moment as football happening again, so each one
            // sounds again — the alternative leaves every angle after the first with the metronome and the
            // drone but no goal, no strike, no save, which is the dead air this whole channel exists to
            // remove. Opening the pass is silent (the span just scanned was measured from the previous
            // bookmark, and is empty once the window has rewound behind it) and marks PassChanged, so the
            // same-moment dedupes clear — a re-shown strike sits at the engine time it already fired at.
            if (!_inReplay || lastEventIndex < _replay)
            {
                _inReplay = true;
                _passChanged = true;
                _replay = lastEventIndex;
                return false;
            }
            _replay = lastEventIndex;
            return true;
        }

        if (_inReplay)
        {
            // Live resuming. Silent, and the live bookmark is left exactly where the replay found it:
            // the span this tick scanned was measured from the REPLAY's bookmark, so it covers ground
            // live already sounded and firing on it would echo. Not advancing live here also means
            // anything genuinely new that the resuming tick carried is still ahead of the bookmark, so
            // the next tick sounds it — one tick late rather than lost.
            _inReplay = false;
            _passChanged = true;
            return false;
        }
        _live = lastEventIndex;
        return true;
    }
}
