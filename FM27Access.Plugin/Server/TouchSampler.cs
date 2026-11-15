using System;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Per-frame detector for ball touches, feeding <see cref="TouchPossession"/>. The engine's animation
/// layer keeps a rolling few-second history of ball velocity queryable by time
/// (<c>SI.Match.Anim.IMatches.GetBallVelocity</c>, timebase t = playback slice / 4); where the incoming
/// and outgoing velocities at a time differ, the ball was touched at that instant. Each frame this
/// scans the small span of game time since the last frame for such discontinuities, and attributes
/// each merged touch to the team whose player is nearest the touch POINT — asked within a frame or two
/// of the touch itself, which is what keeps nearest-player honest: at contact the toucher is at the
/// ball while everyone else is usually metres away, unlike the between-touches sampling the geometric
/// cue reasons over.
///
/// Replays are scanned too, on a second cursor into a second claim (see
/// <see cref="TouchPossession.SetReplayPass"/>). The velocity history is a rolling window around the
/// CURRENT playback position, and during a replay that position is the replayed time — measured live:
/// real, varying speeds and clean touch discontinuities throughout a replay, where those same times
/// queried from a live position return a frozen sentinel. Keeping the replay's claim separate is what
/// lets the ticks follow a replay while a replayed touch still never moves the live claim; the live
/// cursor is pinned across the replay and resumes exactly where it left off.
///
/// Fast-forward (0/7) is the one span that stays unscannable: it crosses ground whose touch history is
/// not retained, so the cursor is pinned forward and the claim forfeited
/// (<see cref="TouchPossession.Reset"/>) — the far side of a skip is a gap we cannot see into, and the
/// restart/turnover event feeds from the SSE loop are what rebuild the claim there.
///
/// Main thread only, hooked once per frame from CoreLoop.TickDone. Cost: a slice read plus a handful
/// of native velocity calls per frame; the player scan runs only when a touch actually fired (about
/// twice a second in open play). Every failure path logs once via a latch and leaves the tracker
/// untouched — the heartbeat then goes stale and the SSE loop silences the touch-mode cues honestly.
/// </summary>
internal static class TouchSampler
{
    // Touch detection, on the HORIZONTAL velocity change only. Physics drift between keyframes is
    // almost entirely vertical — gravity across a high ball's keyframe measured up to ~1.1 m/s of
    // in/out delta, which sat dangerously under the original 1.5 m/s full-vector threshold — while a
    // real touch redirects the ball across the ground. Judging only the ground plane removes the
    // gravity false-positive class outright, which is what lets this threshold sit LOW enough to catch
    // soft touches (ground friction drift measured ~0.2-0.6 m/s; this sits above that with margin).
    private const float HorizontalTouchDelta = 1.0f;
    // The one real touch a horizontal test misses: a clean trap, where the ball simply STOPS (measured
    // live: in 8.3 m/s, out exactly zero). A moving ball whose outgoing velocity is ~zero was touched,
    // whatever the direction of what it was doing before.
    private const float TrapOutSpeedSq = 0.01f;
    private const float TrapInSpeedSq = 1f;
    // Scan granularity: two samples per playback slice, so no touch falls between steps.
    private const float ScanStep = 0.05f;
    // How far behind "now" the scan frontier stays: one slice, so the slice-granular position read and
    // the velocity history are settled while player positions (readable only at the current frame) are
    // still at most a few decimetres stale.
    private const float ScanLag = 0.25f;
    // One physical touch shows as a cluster of discontinuous samples spanning about a slice; candidates
    // within this window of the previous accepted touch are the same touch. Tightened from 0.3 s: a
    // pass received and immediately won by an opponent is two touches ~a quarter-second apart, and the
    // wider window merged them into one — a first-time tackle vanished from the evidence.
    private const float TouchMergeWindow = 0.2f;
    // Attribution. Beyond this the "toucher" is a guess: emit Unknown, never a name. Also what keeps a
    // bench player (~5 m off the touchline) out of contention for touches near the line.
    private const float AttributionMaxDist = 2.5f;
    // Two teams' nearest men separated by less than this are too close to split honestly, so the touch
    // holds the incumbent (Ambiguous). With positions aligned to the touch's own moment (see
    // HistoryFrames) this only has to cover body size and foot reach, not staleness.
    private const float AmbiguityMargin = 0.5f;
    // A DECISIVE touch flips possession at once rather than opening a challenge (see LastTouchTracker):
    // the toucher right on the ball and the other team's nearest man clearly further away.
    private const float DecisiveMaxDist = 0.6f;
    private const float DecisiveMargin = 1.5f;
    // Player positions are only readable at the rendered moment, which runs ~0.3-0.6 s ahead of the
    // touch being attributed (ScanLag plus the slice the velocity history settles over). Measured live
    // (2026-10-03, 378 touches): against the CURRENT positions the nearest man to a touch sat a median
    // 1.55 m away and a quarter of touches had nobody within 2.5 m — a sprinting toucher has moved on,
    // and a defender running through the spot reads as the toucher. So every frame's positions are
    // recorded against the rendered match time, and a touch is judged against the frame recorded at
    // its own moment. HistoryFrames covers well over a second at any plausible frame rate.
    private const int HistoryFrames = 256;
    private const int HistoryMaxPlayers = 32;
    // A recorded frame this far from the touch's moment does not describe it (the history was just
    // cleared, or the touch predates it): the touch is unattributable rather than judged on stale men.
    private const float HistoryMatchTolerance = 0.1f;
    // A rendered-time step backwards or larger than this is a cut (a replay, a skip): the recorded
    // frames belong to other ground and are dropped.
    private const float HistoryMaxStep = 1f;
    // How often the on-pitch player objects are re-found; their positions are read every frame.
    private const int PlayerRefreshFrames = 15;
    // More game time than this arriving in one frame is a skip, not playback: the rolling touch history
    // does not cover it, so the claim is forfeited rather than scanned across.
    private const float MaxCatchupSeconds = 3f;
    // Where the cursor resyncs after a reset: slightly behind now, so the first live touch still lands.
    private const float BackfillSeconds = 0.5f;
    // Hard bound on the per-frame scan cost; exceeding it is treated as a skip.
    private const int MaxStepsPerFrame = 40;
    // On-pitch bounds for the attribution scan (standard pitch + margin): players beyond these are on
    // the bench or warming up, not touching the ball. Approximate on purpose — AttributionMaxDist does
    // the fine filtering; this only has to exclude the touchline bench column.
    private const float OnPitchMaxX = 36f;
    private const float OnPitchMaxZ = 54.5f;
    // While no match exists, only look for one every this many frames — out-of-match frames cost a
    // counter check and nothing else.
    private const int ResolveBackoffFrames = 30;

    // Live component handles (allowed: references to live Unity objects, re-read every use, never
    // cached game STATE). Re-resolved only when null/destroyed.
    private static FM.Match.MatchPlaybackController _ctrl;
    private static FM.Match.Match _match;
    private static SI.Match.Anim.IMatches _matches;
    private static int _resolveBackoff;

    // Scan cursor in engine seconds (playback slice / 4). NaN = not established.
    private static float _lastScannedT = float.NaN;
    private static float _lastTouchT = float.NegativeInfinity;
    // The replay pass's cursor and merge clock. Separate from the live pair so a replay can be scanned
    // in full without the live scan losing its place: the live cursor is pinned across the replay and
    // resumes exactly where the replay found it.
    private static float _replayScannedT = float.NaN;
    private static float _replayTouchT = float.NegativeInfinity;
    private static bool _replayPass;
    // Whether the PREVIOUS frame was a fast-forward, so its claim forfeit fires once on entry rather
    // than every frame of the skip. Only fast-forward sets it: a replay is scanned, not skipped, and a
    // replay running straight into a fast-forward must still forfeit.
    private static bool _wasFastForward;
    private static bool _hadMatch;

    // Player position history (see HistoryFrames): a ring of frames, each the rendered match time and
    // the on-pitch players' ground positions and teams at it. Positions are re-read live every frame;
    // the ring is a record of the recent past, never a stand-in for the present.
    private static readonly float[] _histT = new float[HistoryFrames];
    private static readonly int[] _histCount = new int[HistoryFrames];
    private static readonly float[,] _histX = new float[HistoryFrames, HistoryMaxPlayers];
    private static readonly float[,] _histZ = new float[HistoryFrames, HistoryMaxPlayers];
    private static readonly byte[,] _histTeam = new byte[HistoryFrames, HistoryMaxPlayers];
    private static int _histHead = -1;
    private static int _histLen;
    private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<FM.Match.Player> _players;
    private static int _playersAge = int.MaxValue;

    // Diagnostics for /posmode.
    private static float _maxTickMs;
    private static long _lastSlowWarningMs;
    private static int _touchesSeen;
    private static int _windowResets;

    private static string _lastDegradeReason;

    private static void NoteDegradeOnce(string reason)
    {
        if (reason == _lastDegradeReason) return;
        _lastDegradeReason = reason;
        Speech.Degrade.Note(reason);
    }

    /// <summary>Watermark and counters for /posmode.</summary>
    internal static (float MaxTickMs, int Touches, int WindowResets) Diagnostics()
        => (_maxTickMs, _touchesSeen, _windowResets);

    /// <summary>Per-frame entry point (main thread, from CoreLoop.TickDone).</summary>
    public static void Tick()
    {
        long started = Environment.TickCount64;
        long measured = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            TickBody(started);
        }
        catch (Exception ex)
        {
            NoteDegradeOnce("[TouchSampler] frame scan failed: " + ex.Message);
        }
        // Windows' coarse uptime clock can jump ~15.6ms during a sub-ms
        // read. Keep that clock for possession timestamps, not cost warnings.
        float elapsed = (float)((System.Diagnostics.Stopwatch.GetTimestamp()-measured)
            *1000.0/System.Diagnostics.Stopwatch.Frequency);
        if (elapsed > _maxTickMs) _maxTickMs = elapsed;
        if (elapsed > 5f && started-_lastSlowWarningMs>=5000)
        {
            _lastSlowWarningMs=started;
            Speech.Log.Warn($"[TouchSampler] slow frame: {elapsed:F2}ms; maximum {_maxTickMs:F2}ms");
        }
    }

    private static void TickBody(long now)
    {
        if (!MainThreadDispatcher.Ready) return;
        if (!TryResolve())
        {
            if (_hadMatch)
            {
                _hadMatch = false;
                _lastScannedT = float.NaN;
                _replayScannedT = float.NaN;
                _replayPass = false;
                _players = null;
                _histLen = 0;
                TouchPossession.Reset("match gone");
            }
            return;
        }
        _hadMatch = true;

        var matchID = _ctrl.CurrentMatchID;
        if (matchID == SI.Match.Anim.MatchID.None) return;

        TouchPossession.Heartbeat(now);

        float nowT = _match.CurrentTimeSlice * 0.25f;
        int mode = (int)_match.CurrentMatchPlaybackMode;
        // Fast-forward (0/7) crosses a span whose touch history is gone: not scannable, so the cursor is
        // pinned forward and the claim forfeited (silence until the far side produces fresh evidence).
        // Replays (1/2/5/6) retrace ground the live scan already covered — scannable, but into a claim
        // of their own, so a replayed touch can never move the live one.
        bool fastForward = mode == 0 || mode == 7;
        bool replay = mode == 1 || mode == 2 || mode == 5 || mode == 6;
        if (!fastForward) RecordPlayers(_match.CurrentTime);

        if (replay)
        {
            _wasFastForward = false;
            ScanReplay(matchID, nowT, now);
            return;
        }
        if (_replayPass)
        {
            // Live resuming: close the pass and pin the live cursor to now. The replay covered ground
            // the live scan already passed, so there is nothing to catch up on — only a gap to skip.
            _replayPass = false;
            TouchPossession.SetReplayPass(false);
            _lastScannedT = nowT;
        }
        if (fastForward)
        {
            if (!_wasFastForward)
            {
                _windowResets++;
                TouchPossession.Reset("fast-forward");
            }
            _wasFastForward = true;
            _lastScannedT = nowT;
            return;
        }
        _wasFastForward = false;

        if (float.IsNaN(_lastScannedT) || nowT < _lastScannedT
            || nowT - _lastScannedT > MaxCatchupSeconds
            || (nowT - _lastScannedT) / ScanStep > MaxStepsPerFrame)
        {
            // A gap we cannot see into (first frame, a rewind, or a skip): forfeit and resync.
            if (!float.IsNaN(_lastScannedT))
            {
                _windowResets++;
                TouchPossession.Reset("playback gap");
            }
            _lastScannedT = Math.Max(0f, nowT - BackfillSeconds);
            return;
        }

        Scan(matchID, ref _lastScannedT, ref _lastTouchT, nowT, now);
        _lastDegradeReason = null;
    }

    /// <summary>
    /// One frame of the replay pass. Mirrors the live path on the replay's own cursor and merge clock,
    /// so the same detection and attribution answer the same question about a different span. Anything
    /// the cursor cannot bridge — the pass opening, a camera angle rewinding, a jump too wide for the
    /// rolling history — restarts the pass with an empty claim rather than carrying one across ground
    /// it cannot account for. That claim starts silent, and the ticks stay quiet until the replay itself
    /// produces a touch: the honest state for a passage of play nothing has yet observed.
    /// </summary>
    private static void ScanReplay(SI.Match.Anim.MatchID matchID, float nowT, long now)
    {
        bool opening = !_replayPass;
        // Opening the pass always establishes the cursor, so while one is open the cursor is valid and
        // only a jump it cannot bridge starts a new pass: a camera angle rewinding, or a gap wider than
        // the rolling history covers.
        bool rewound = !opening
            && (nowT < _replayScannedT
                || nowT - _replayScannedT > MaxCatchupSeconds
                || (nowT - _replayScannedT) / ScanStep > MaxStepsPerFrame);
        if (opening || rewound)
        {
            if (rewound) _windowResets++;
            _replayPass = true;
            TouchPossession.SetReplayPass(true, fresh: rewound);
            _replayScannedT = Math.Max(0f, nowT - BackfillSeconds);
            _replayTouchT = float.NegativeInfinity;
            return;
        }

        Scan(matchID, ref _replayScannedT, ref _replayTouchT, nowT, now);
        _lastDegradeReason = null;
    }

    /// <summary>
    /// Walk the velocity history from <paramref name="cursor"/> up to the scan frontier, reporting each
    /// merged discontinuity as a touch. Shared by the live and replay passes: each owns its own cursor
    /// and merge clock, and <see cref="TouchPossession"/> routes the touch to whichever claim the
    /// current pass owns.
    /// </summary>
    private static void Scan(SI.Match.Anim.MatchID matchID, ref float cursor, ref float lastTouchT,
                             float nowT, long now)
    {
        float frontier = nowT - ScanLag;
        while (cursor + ScanStep <= frontier)
        {
            cursor += ScanStep;
            var v = _matches.GetBallVelocity(matchID, cursor);
            float dx = v.incoming.x - v.outgoing.x;
            float dz = v.incoming.z - v.outgoing.z;
            float outSq = v.outgoing.x * v.outgoing.x + v.outgoing.y * v.outgoing.y + v.outgoing.z * v.outgoing.z;
            float inSq = v.incoming.x * v.incoming.x + v.incoming.y * v.incoming.y + v.incoming.z * v.incoming.z;
            bool trapped = outSq < TrapOutSpeedSq && inSq > TrapInSpeedSq;
            if (dx * dx + dz * dz < HorizontalTouchDelta * HorizontalTouchDelta && !trapped) continue;
            if (cursor - lastTouchT < TouchMergeWindow) { lastTouchT = cursor; continue; }
            lastTouchT = cursor;
            _touchesSeen++;
            var who = Attribute(cursor, out bool decisive, out string detail);
            TouchPossession.NoteTouch(who, decisive, detail, now);
        }
    }

    /// <summary>
    /// Name the touch at engine time <paramref name="t"/>: nearest on-pitch player of each team to the
    /// touch point, judged against the player positions recorded at that same moment (see
    /// HistoryFrames). Unknown when nobody is close enough to call it anyone's touch, or no recorded
    /// frame covers the moment; Ambiguous when the two teams are too close to split honestly.
    /// <paramref name="decisive"/> marks a named touch clean enough to flip possession on its own.
    /// </summary>
    private static TouchAttribution Attribute(float t, out bool decisive, out string detail)
    {
        decisive = false;
        detail = $"t={t:0.00}";
        // Interpolate the ball position between the two adjacent playback slices. Flooring to one
        // slice quantized the touch point to 0.25 s of ball travel — up to ~4 m on a driven ball — and
        // that error alone pushed real touchers past AttributionMaxDist, which read as half of all
        // touches being unattributable. (Verified live that GetBallPosition answers for slices at and
        // slightly ahead of the playback position.)
        float sliceF = t * 4f;
        int slice = (int)sliceF;
        float frac = sliceF - slice;
        var b0 = _ctrl.PlaybackInterface.GetBallPosition(slice);
        var b1 = _ctrl.PlaybackInterface.GetBallPosition(slice + 1);
        var ball = b0 + (b1 - b0) * frac;

        int frame = FindFrame(t);
        if (frame < 0) { detail += " no player history at t"; return TouchAttribution.Unknown; }

        float dHome = float.MaxValue, dAway = float.MaxValue;
        for (int i = 0; i < _histCount[frame]; i++)
        {
            float dx = _histX[frame, i] - ball.x, dz = _histZ[frame, i] - ball.z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (_histTeam[frame, i] == 0) { if (d < dHome) dHome = d; }
            else if (d < dAway) dAway = d;
        }
        detail += $" frameDt={_histT[frame] - t:0.000}";

        int userTeam = _match.UserTeamId;
        int refTeam = userTeam >= 0 ? userTeam : 0;    // reference is the user's team, else home
        float dRef = refTeam == 0 ? dHome : dAway, dOpp = refTeam == 0 ? dAway : dHome;
        detail += $" ball=({ball.x:0.0},{ball.y:0.0},{ball.z:0.0}) dRef={dRef:0.0} dOpp={dOpp:0.0}"
                + $" lag={_match.CurrentTime - t:0.00}";

        float nearest = Math.Min(dHome, dAway);
        if (nearest > AttributionMaxDist) return TouchAttribution.Unknown;
        if (Math.Abs(dHome - dAway) < AmbiguityMargin) return TouchAttribution.Ambiguous;

        decisive = nearest <= DecisiveMaxDist && Math.Abs(dHome - dAway) >= DecisiveMargin;
        int toucher = dHome < dAway ? 0 : 1;
        return toucher == refTeam ? TouchAttribution.Reference : TouchAttribution.Opposition;
    }

    /// <summary>
    /// Append this frame's on-pitch player positions to the history at rendered match time
    /// <paramref name="renderT"/>. A paused frame (time unchanged) adds nothing; a cut (time going
    /// backwards, or jumping past HistoryMaxStep) drops the history first, since those frames describe
    /// other ground.
    /// </summary>
    private static void RecordPlayers(float renderT)
    {
        if (_histLen > 0)
        {
            float last = _histT[_histHead];
            if (renderT == last) return;
            if (renderT < last || renderT - last > HistoryMaxStep) _histLen = 0;
        }

        if (_players == null || ++_playersAge >= PlayerRefreshFrames)
        {
            _players = UnityEngine.Object.FindObjectsOfType<FM.Match.Player>();
            _playersAge = 0;
        }

        _histHead = (_histHead + 1) % HistoryFrames;
        _histT[_histHead] = renderT;
        int n = 0;
        if (_players != null)
        {
            for (int i = 0; i < _players.Length && n < HistoryMaxPlayers; i++)
            {
                var p = _players[i];
                if (p == null) { _playersAge = int.MaxValue; continue; }       // destroyed: re-find next frame
                if ((int)p.EntityKind != 0) continue;                            // referee/staff
                var pos = p.Position;
                if (Math.Abs(pos.x) > OnPitchMaxX || Math.Abs(pos.z) > OnPitchMaxZ) continue;   // bench
                int team = (int)p.TeamID;
                if (team != 0 && team != 1) continue;
                _histX[_histHead, n] = pos.x;
                _histZ[_histHead, n] = pos.z;
                _histTeam[_histHead, n] = (byte)team;
                n++;
            }
        }
        _histCount[_histHead] = n;
        if (_histLen < HistoryFrames) _histLen++;
    }

    /// <summary>
    /// The recorded frame nearest match time <paramref name="t"/>, or -1 when none lies within
    /// HistoryMatchTolerance or the nearest recorded no players.
    /// </summary>
    private static int FindFrame(float t)
    {
        int best = -1;
        float bestDt = float.MaxValue;
        for (int k = 0; k < _histLen; k++)
        {
            int i = (_histHead - k + HistoryFrames) % HistoryFrames;
            float dt = Math.Abs(_histT[i] - t);
            if (dt < bestDt) { bestDt = dt; best = i; }
            if (_histT[i] < t - HistoryMatchTolerance) break;   // older frames only get further away
        }
        if (best < 0 || bestDt > HistoryMatchTolerance || _histCount[best] == 0) return -1;
        return best;
    }

    /// <summary>
    /// (Re-)acquire the live match objects. Cheap when everything is alive; while there is no match,
    /// scene scans back off to every <see cref="ResolveBackoffFrames"/> frames.
    /// </summary>
    private static bool TryResolve()
    {
        if (_ctrl != null && _match != null && _matches != null) return true;
        if (_resolveBackoff-- > 0) return false;
        _resolveBackoff = ResolveBackoffFrames;

        _ctrl = UnityEngine.Object.FindObjectOfType<FM.Match.MatchPlaybackController>();
        // No playback controller means sampling cannot run. Avoid a second
        // whole-scene search on every idle retry (notably at the main menu).
        if (_ctrl == null)
        {
            _match = null;
            _matches = null;
            return false;
        }
        _match = UnityEngine.Object.FindObjectOfType<FM.Match.Match>();
        if (_ctrl == null || _match == null)
        {
            _ctrl = null;
            _match = null;
            _matches = null;
            return false;
        }
        var anim = UnityEngine.Object.FindObjectOfType<FM.Match.Animation.Match3DAnimation>();
        _matches = anim != null ? anim.m_sianimMatches : null;
        if (_matches == null)
        {
            var instance = FM.GamePlugin.Bindings.MarshallingWrappers
                .SI_Match_Anim_IMatches_NativeWrapper.GetInstance();
            _matches = instance == null ? null
                : ((Il2CppSystem.Object)instance).TryCast<SI.Match.Anim.IMatches>();
        }
        if (_matches == null)
        {
            NoteDegradeOnce("[TouchSampler] IMatches unavailable; touch detection off.");
            return false;
        }
        return true;
    }

    /// <summary>Drop handles across a hot reload or match exit; next Tick re-resolves.</summary>
    internal static void Shutdown()
    {
        _ctrl = null;
        _match = null;
        _matches = null;
        _players = null;
        _histLen = 0;
        _lastScannedT = float.NaN;
        _replayScannedT = float.NaN;
        _replayPass = false;
        TouchPossession.Reset("shutdown");
    }
}
