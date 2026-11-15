using System;
using System.Reflection;
using FM27Access.Navigation;
using UnityEngine;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The audio-cue zone for one team's match tick sounds, named relative to THAT team's attacking direction
/// (the reference team and the opposition each get their own). Anything that is not a clear possession by
/// that team in a live third is <see cref="Off"/> (silence). <see cref="Box"/> is the penalty/goal area the
/// team is attacking, which supersedes the third to give a faster cue near the goal.
/// </summary>
internal enum CueZone { Off, Defensive, Middle, Attacking, Box }

/// <summary>
/// On-demand ball-location readouts for the HTML surface during a match: Alt+5 speaks possession then
/// the third plus flank (a penalty/goal box supersedes both), Alt+7 the distance from the attacked goal.
///
/// The ball position is read from the match ENGINE INTERFACE, not the rendered 3D ball object. The
/// rendered <c>FM.Match.Ball</c> only exists while a highlight is on screen, but
/// <c>IPluginMatchPlaybackInterface.GetBallPosition(timeSlice)</c> returns a position for any playback
/// slice, so the reads work between highlights and while paused, not only during a rendered highlight.
///
/// Everything is relative to the reference team's attacking direction: the player's team
/// (<c>Match.UserTeamId</c>), or the home team when the player controls neither. Direction comes from
/// <c>IPluginMatchInterface.IsTeamKickingUpfield</c>, which the engine keeps correct across the
/// half-time end-swap, so "attacking third" / "opposition box" / "left" always mean the reference team's
/// frame. Pitch dimensions come from the live <c>PitchSetupInfo</c> so the box maths and flank split are
/// correct on non-standard pitches.
///
/// Main thread only (touches Unity objects) — every entry point marshals via
/// <see cref="MainThreadDispatcher"/>. Stateless: every call re-finds the live match objects and
/// re-reads current state, never caching game values (only reflection member handles).
/// </summary>
internal static class MatchBallLocation
{
    private const BindingFlags AllInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    // Fallback pitch dimensions (standard 105 x 68 m) if the live PitchSetupInfo read ever fails. The
    // engine's Z axis runs goal-to-goal, X is width, both with 0 at the centre spot.
    private const float DefaultPitchLength = 105f;
    private const float DefaultPitchWidth = 68f;

    // Possession is not in the managed/plugin layer at all (it lives in the native engine), so we define
    // it ourselves from geometry, relative to the ball. All metres, all deliberate and tunable criteria,
    // not engine truths:
    //   - within OnBallRadius on the ground: the nearest player is "on the ball".
    //   - both teams have a player within ContestedRadius: a 50-50 (tackle or aerial duel).
    //   - farther than that: the ball is loose (e.g. a pass in flight).
    // ContestedRadius is tuned by ear, and what it costs changed once a 50-50 stopped cutting the tick.
    // While contested silenced both metronomes, a wide radius was expensive — every near-challenge blanked
    // the possession cue — so it was pulled in to 0.75 m, which then caught only foot-to-foot tackles and
    // missed challenges that were plainly duels. Now that a duel layers the contested clip over a held
    // tick instead of replacing it, a wider radius only adds the clip, so it can sit near where it began.
    // It also drives the spoken "contested" possession readout, so both surfaces move together.
    // Briefly pinned to OnBallRadius so the states were an exact partition of one reach; that made duels so
    // rare the clip stopped firing, and the two were decoupled again. See ContestedRadius below for the
    // consequences of it being the wider of the two.
    // The home side in the engine's Home=0/Away=1 ordering — the same order the game's scoreboard uses,
    // home first. The stereo field is anchored to this team, not to the player's, so a fixture sounds the
    // same way round whether the player is at home or away.
    private const byte HomeTeam = 0;

    // One radius for both keeping and winning the ball, deliberately. An earlier attempt at hysteresis
    // used a tighter "take" radius than this "keep" one; see the CueForTeam hard-loss branch for why that
    // cannot work — the two tests must stay exact complements or both metronomes run at once.
    //
    // Tuned live 2026-08-29, 2.0 -> 0.5 -> 0.3 -> 0.6 m. At 2 m the cue called possession far too widely: in
    // open play there is usually someone within 2 m of the ball, so a team read InControl through phases it
    // did not have and the tick stopped meaning "we have it". The trigger has to be CONTACT with the ball
    // rather than proximity to it.
    //
    // 0.3 m was contact taken too literally. It is not that touches were MISSED between samples — the trace
    // ruled that out, showing consecutive in-control samples whenever a team really had the ball — but that
    // the radius was satisfied on only ~9-13% of samples at all, because in football the ball spends most of
    // its life in transit. The whole possession model then ran on a tenth of a duty cycle of real evidence.
    // That is survivable for CONTINUITY, which the confirmed holder provides regardless, and bad for
    // DETECTING CHANGE: a turnover happens while the ball is travelling, so an interceptor had to produce
    // two separate sub-0.3 m samples before the cue would move. Graded against the engine's own turnover
    // events, the cue then agreed a median of 5.0s late, at worst 14.5s, and once never.
    //
    // 0.6 m is still a player's own reach rather than the space around him — the distinction that made 2 m
    // indefensible — but it is satisfied on roughly 25% of samples instead of 10%, so the evidence the
    // arbiter reasons over is about 2.4x denser.
    private const float OnBallRadius = 0.6f;
    // Deliberately DECOUPLED from OnBallRadius and much wider than it. Tying the two together made a duel
    // require both men within contact distance of the ball simultaneously, which almost never happens, so
    // the contested clip effectively stopped firing. A duel is two players converging on a ball, not two
    // players already touching it.
    //
    // The cost is understood and accepted: contested is tested FIRST and beats everything, and the arbiter
    // deliberately does not treat it as a challenge, so while two opposing men are inside this radius the
    // holder CANNOT change. A turnover in a crowded phase therefore sustains the old holder's zone until
    // ContestedGraceMs runs out. That is bounded and self-limiting, and the alternative — a duel the player
    // never hears — was judged worse.
    private const float ContestedRadius = 1.2f;
    // The carrier-pressure bed's tuning, kept here in the reloadable plugin rather than beside the pure
    // maths in Navigation so it can be dialled in by ear over /reload. See CarrierPressure for the shape.
    //
    // Tuned live 2026-08-21, from a first pass at 6 m with a 0.7 base share that had the bed running almost
    // continuously. That combination made a single opponent ANYWHERE within 6 m drive the low stem to most
    // of its band, and in football there is nearly always someone within 6 m of the ball, so the bed never
    // rested and stopped meaning anything.
    //
    // A zero base share is the fix: an opponent's contribution now falls to nothing at the radius, so a man
    // who is merely nearby is silent and only a man genuinely closing in registers at all. Count still
    // dominates, but through the divisor rather than through a flat share — at 3.0 it is exactly the number
    // of opponents that means maximum pressure, which caps what any smaller number of them can reach: ONE
    // opponent can never exceed the low band however tightly he marks, and two can never exceed medium.
    // Bodies alone open the upper stems, which is what "count outweighs distance" has to mean to be true.
    //
    // The radius went 3 m -> 4 m in a second live pass, because at 3 m the top stem was effectively dead:
    // reaching it needed three opponents inside 0.88 m or four inside 1.41 m, which is a goalmouth scramble
    // rather than a press, so in open play it never fired. Widening the radius adds BODIES to the count
    // without making any single one louder — the opposite of lowering the divisor, which would have made
    // every level easier and undone the work that stopped the bed sounding constantly.
    private const float PressureRadius = 4.0f;
    private const float PressureBaseShare = 0.0f;
    private const float PressureDivisor = 3.0f;
    // How much nearer one team's man must be for the ball to be called his rather than in dispute. Two
    // players both within reach whose distances differ by less than this are too close to call, and the
    // difference between them is below the resolution the readings can honestly support anyway.
    //
    // Sized as a fraction of OnBallRadius, so it moved with it: 0.35 m was a sixth of the old 2 m reach,
    // but would have been most of the way across the current one.
    //
    // NOTE that it is currently INERT. Its clause needs the nearer man within OnBallRadius (0.6 m) and the
    // two within this margin, which puts the far man inside 0.75 m — well within ContestedRadius (1.2 m), so
    // the genuine-duel clause has already fired. It only does work again if ContestedRadius drops below
    // OnBallRadius + HandoverMargin. Kept rather than deleted because the two radii are tuned by ear and
    // that relationship has already inverted once.
    private const float HandoverMargin = 0.15f;
    // Ball height above which it is treated as in the air rather than at anyone's feet. Metres.
    // Derived from the shared constant, never repeated: the same height is the bottom of the cue's
    // loudness ramp, and two copies that drifted apart would make the sound step in part-way up the
    // ramp instead of fading in from its quietest.
    private const float AerialHeight = (float)Navigation.BallLocation.AirFloorMetres;
    // How far past the pitch edge still counts as on-pitch. On-pitch players reach ~34 m wide / 52 m long;
    // the bench sits at |x| ~= 39 m, so this margin keeps throw-in/corner takers while dropping the bench.
    private const float OnPitchMargin = 2.0f;

    // Out-of-play detection. The ball is out once its centre crosses a boundary line by this margin (kept
    // small so a ball riding the line in play is not falsely called out; tune from live behaviour). The
    // goal mouth is the standard 7.32 m wide (half = 3.66 m) and 2.44 m high (the crossbar); a ball inside
    // that box beyond the goal-line is in the net.
    private const float OutMargin = 0.15f;
    private const float GoalHalfWidth = 3.66f;
    private const float CrossbarHeight = 2.44f;
    // How far behind the goal-line the net catches a real goal. At 2 Hz the crossing itself is missed, so
    // a ball hit over the bar is only sampled AFTER it has cleared the frame and landed on the ground
    // several metres behind the goal — where it would read as a low, central "Net". A ball sampled deeper
    // than this has gone behind the goal, not into it. Calibrated live: over-the-bar shots were first
    // sampled ~4 m deep; a real goal is stopped by the net well inside this.
    private const float GoalDepth = 2.0f;

    // Resolved from the live interface instances on first use; the interop proxy types are stable for
    // the process, so the handles are safe to cache. The read values themselves are never cached.
    private static MethodInfo _getBallPosition;   // IPluginMatchPlaybackInterface.GetBallPosition(int)
    private static MethodInfo _isKickingUpfield;  // IPluginMatchInterface.IsTeamKickingUpfield(MatchHandle,int,byte)
    private static PropertyInfo _playerPosition;  // FM.Match.Player.Position (Vector3)
    private static PropertyInfo _playerTeamId;    // FM.Match.Player.TeamID (0 home, 1 away)
    private static PropertyInfo _playerName;      // FM.Match.Player.BillboardName ("K. Kambere")
    private static PropertyInfo _playerKind;      // FM.Match.Player.EntityKind (Player=0 vs referee/staff)
    private static PropertyInfo _playbackMode;    // FM.Match.Match.CurrentMatchPlaybackMode (MatchPlaybackMode)
    private static PropertyInfo _commentaryEvents; // FM.Match.Match.m_commentaryEvents (List<MatchCommentaryEvent>)
    private static PropertyInfo _lastEventIndex;   // FM.Match.Match.m_currentLastEventIndex (playback high-water)
    private static PropertyInfo _userTeamId;       // FM.Match.Match.UserTeamId (0 home, 1 away, -1 neutral)
    private static PropertyInfo _matchPeriod;     // FM.Match.Match.MatchPeriod
    private static PropertyInfo _hasStarted;      // FM.Match.Match.HasStarted
    private static PropertyInfo _timeSlice;       // FM.Match.Match.CurrentTimeSlice
    private static PropertyInfo _playbackIface;   // MatchPlaybackController.PlaybackInterface
    private static PropertyInfo _matchIface;      // MatchPlaybackController.MatchInterface
    private static PropertyInfo _matchHandle;     // MatchPlaybackController.MatchHandle

    // Frame-scoped scene-scan memo. The SSE loop's sub-ticks call into this class up to five times
    // per dispatch, and MatchWatcher probes once more per full tick — each a full FindObjectOfType
    // scene scan, on every screen in the game, match or not. All of those run inside main-thread
    // dispatches on the same frame, so one scan per frame serves them all. Frame-scoped only, never
    // carried across frames — and FindObjectOfType itself still returns objects Destroy()ed earlier
    // in the same frame (destruction is deferred to frame end), so the memo adds no staleness a
    // direct scan would not have.
    private static FM.Match.Match _frameMatch;
    private static FM.Match.MatchPlaybackController _frameCtrl;
    private static int _matchScanFrame = -1;
    private static int _ctrlScanFrame = -1;

    internal static FM.Match.Match CurrentMatch()
    {
        int frame = UnityEngine.Time.frameCount;
        if (frame != _matchScanFrame)
        {
            _matchScanFrame = frame;
            _frameMatch = UnityEngine.Object.FindObjectOfType<FM.Match.Match>();
        }
        return _frameMatch;
    }

    private static FM.Match.MatchPlaybackController CurrentController()
    {
        int frame = UnityEngine.Time.frameCount;
        if (frame != _ctrlScanFrame)
        {
            _ctrlScanFrame = frame;
            _frameCtrl = UnityEngine.Object.FindObjectOfType<FM.Match.MatchPlaybackController>();
        }
        return _frameCtrl;
    }

    // A broken engine field would otherwise fire the same degrade on every keypress; log a given
    // reason once until a clean read clears it. The returned marker is still emitted every call so the
    // player always hears an honest gap.
    private static string _lastDegradeReason;

    private static void NoteDegradeOnce(string reason)
    {
        if (reason == _lastDegradeReason) return;
        _lastDegradeReason = reason;
        Speech.Degrade.Note(reason);
    }

    /// <summary>The live ball position, attacking direction, and pitch dimensions for one read.</summary>
    private readonly struct Reading
    {
        public readonly Vector3 Ball;
        public readonly bool AttackingUpfield;
        public readonly float PitchLength;
        public readonly float PitchWidth;
        // The reference team's id in the engine's Home=0/Away=1 ordering (the player's team, or home
        // when the player controls neither). Used by the cue to decide who is "your team".
        public readonly int RefTeam;
        // True while the viewer is showing a replay/rewind rather than the live first pass. The ball
        // re-traces the play during a replay, so the one-shot sounds must not re-fire on it.
        public readonly bool InReplay;
        // Where the televised camera stands: which world direction appears to the RIGHT of the stereo field.
        // Read from the HOME team's kick-off direction, so the home side is heard attacking left to right in
        // the first half (the order a scoreboard puts them in) and the sides swap afterwards while the camera
        // holds still. Fixed for the whole match. Both the pan and the touchline drones take their bearing
        // from this one value: the camera is at the near touchline, so near is +X exactly when screen-right
        // is +Z, and deriving both from it is what stops the two axes describing different pitches.
        public readonly bool ScreenRightIsPositiveZ;
        // The playback time slice this read was taken at. A slice that has not moved since the previous
        // read means playback is halted (paused, a stopped replay) — the SSE loop mutes the cues on it.
        public readonly int Slice;

        public Reading(Vector3 ball, bool attackingUpfield, float pitchLength, float pitchWidth, int refTeam,
                       bool inReplay, bool screenRightIsPositiveZ, int slice)
        {
            Ball = ball;
            AttackingUpfield = attackingUpfield;
            PitchLength = pitchLength;
            PitchWidth = pitchWidth;
            RefTeam = refTeam;
            InReplay = inReplay;
            ScreenRightIsPositiveZ = screenRightIsPositiveZ;
            Slice = slice;
        }
    }

    /// <summary>
    /// The ball-position half of Alt+5 (spoken second): where the ball is, as third plus flank
    /// (e.g. "attacking third left", "middle third
    /// central"). A penalty or goal area supersedes both and drops the flank ("in opposition box"). When
    /// the ball has left the field this reports the exit instead ("out of play, attacking third right",
    /// "out of play, left of opposition goal", "goal, opposition net").
    /// </summary>
    public static string DescribeThird() => Describe(r =>
    {
        var outOfPlay = OutOfPlayLocation(r.Ball, r.AttackingUpfield, r.PitchLength, r.PitchWidth);
        if (outOfPlay != null) return outOfPlay;
        var box = Navigation.BallLocation.ClassifyBox(r.Ball.x, r.Ball.z, r.AttackingUpfield, r.PitchLength / 2f);
        if (box != Navigation.BoxZone.None) return "in " + Navigation.BallLocation.Speak(box);
        var third = Navigation.BallLocation.Classify(r.Ball.z, r.AttackingUpfield, r.PitchLength / 2f);
        var flank = Navigation.BallLocation.ClassifyFlank(r.Ball.x, r.AttackingUpfield, r.PitchWidth / 2f);
        return Navigation.BallLocation.Speak(third) + " " + Navigation.BallLocation.Speak(flank);
    });

    /// <summary>
    /// Alt+7: metres from the ball to the goal the reference team is attacking, prefixed "out of play"
    /// when the ball has left the field.
    /// </summary>
    public static string DescribeDistance() => Describe(r =>
    {
        int metres = (int)Math.Round(
            Navigation.BallLocation.MetresToAttackingGoal(r.Ball.x, r.Ball.z, r.AttackingUpfield, r.PitchLength / 2f));
        string phrase = metres + (metres == 1 ? " metre from goal" : " metres from goal");
        return IsBallOutOfBounds(r.Ball, r.PitchLength, r.PitchWidth) ? "out of play, " + phrase : phrase;
    });

    /// <summary>
    /// Whether a match engine currently exists — the gate for the companion match chords
    /// (/matchchord). The ENGINE is the match signal, not any rendered scoreboard element: the
    /// scoreboard template varies per competition, so the page is not a reliable match indicator.
    /// True covers the whole match day the engine is loaded for, including paused and between
    /// highlights. If the check itself fails, report true: the readout path then degrades to an
    /// honest spoken marker, where a false would silently navigate the player away mid-match.
    /// </summary>
    internal static bool IsMatchLive()
    {
        try
        {
            return MainThreadDispatcher.Invoke(
                () => UnityEngine.Object.FindObjectOfType<FM.Match.Match>() != null);
        }
        catch (Exception ex)
        {
            NoteDegradeOnce("[MatchBallLocation] match-live check failed: " + ex.Message);
            return true;
        }
    }

    /// <summary>
    /// The possession half of Alt+5 (spoken first): who is on the ball, by our own geometric
    /// criterion. The engine exposes no possession, so we
    /// find the nearest on-pitch player of each team to the ball (excluding referees, staff, and the bench)
    /// and report, relative to the user's team: "contested, &lt;a&gt; and &lt;b&gt;" when both teams have a
    /// player within <see cref="ContestedRadius"/>, "ball in the air, nearest &lt;name&gt;" above
    /// <see cref="AerialHeight"/>, "&lt;name&gt;, your team/opposition" when the nearest is within
    /// <see cref="OnBallRadius"/>, else "loose ball, nearest &lt;name&gt;". Player objects only exist while
    /// a highlight/replay renders, so between highlights this degrades to an honest "possession unavailable".
    /// </summary>
    public static string DescribePossession()
    {
        try
        {
            return MainThreadDispatcher.Invoke(ComputePossession);
        }
        catch (Exception ex)
        {
            NoteDegradeOnce("[MatchBallLocation] possession read failed: " + ex.Message);
            return "possession " + Speech.Degrade.Unavailable;
        }
    }

    private static string ComputePossession()
    {
        var s = ReadPossessionSnapshot();
        if (s.Gap != null) return s.Gap;
        return s.TeamWord != null ? s.Body + ", " + s.TeamWord : s.Body;
    }

    /// <summary>
    /// The possession line for the commentary gap filler: the same judgement, minus the trailing
    /// team indicator (the tick sounds already carry the team). Null on every gap case (no match, dead
    /// period, between highlights, unreadable) — the filler is optional enrichment, so silence is honest
    /// where the on-demand Alt+5 readout must answer with a degrade marker. Main thread only, no
    /// self-dispatch: called from inside the SSE tick's existing dispatch.
    /// </summary>
    internal static string ComposeCommentaryFiller()
    {
        var s = ReadPossessionSnapshot();
        return s.Gap != null ? null : s.Body;
    }

    /// <summary>
    /// One possession judgement, split so the possession readout and the commentary filler share it:
    /// <see cref="Gap"/> non-null is a can't-judge outcome (the readout's phrase for it; the filler
    /// skips); otherwise <see cref="Body"/> is the phrase without any trailing team clause and
    /// <see cref="TeamWord"/> the clause the readout appends after a comma (null where it has none).
    /// </summary>
    private readonly struct PossessionSnapshot
    {
        public readonly string Gap;
        public readonly string Body;
        public readonly string TeamWord;

        private PossessionSnapshot(string gap, string body, string teamWord)
        {
            Gap = gap;
            Body = body;
            TeamWord = teamWord;
        }

        public static PossessionSnapshot OfGap(string gap) => new(gap, null, null);
        public static PossessionSnapshot OfInfo(string body, string teamWord = null) => new(null, body, teamWord);
    }

    // The tick cues' confirmed holder, stamped by the SSE loop each sample so the spoken Alt+5 readout can
    // answer with the same judgement the player is hearing. Without this the two disagree exactly where it
    // matters: through a sustained pass or an unconfirmed turnover the ticks say one team while a fresh
    // geometric read says "loose ball".
    //
    // Stamped rather than computed here because the arbiter needs the unbroken 125 ms sample train to
    // confirm anything, and an on-demand readout sees one isolated instant. The timestamp is what keeps that
    // honest: with no companion connected nothing feeds the arbiter, so a stale holder must not be spoken.
    private static PossessionHolder _confirmedHolder = PossessionHolder.None;
    private static long _confirmedAtMs;

    /// <summary>How old a holder stamp may be and still describe now. Three sample periods.</summary>
    private const long ConfirmedHolderMaxAgeMs = 400;

    /// <summary>Record the arbiter's confirmed holder. Called from the SSE loop once per sample.</summary>
    internal static void NoteConfirmedHolder(PossessionHolder holder, long nowMs)
    {
        _confirmedHolder = holder;
        _confirmedAtMs = nowMs;
    }

    /// <summary>
    /// The confirmed holder as an engine team id (Home=0/Away=1), or false when there is no live claim to
    /// report — nothing has been stamped recently (no SSE consumer), or the arbiter holds no one. The caller
    /// then falls back to judging this instant on its own, which is what the readout did before the cues
    /// gained any memory.
    /// </summary>
    private static bool TryConfirmedTeam(int userTeam, out int teamId)
    {
        teamId = -1;
        if (_confirmedHolder == PossessionHolder.None) return false;
        if (Environment.TickCount64 - _confirmedAtMs > ConfirmedHolderMaxAgeMs) return false;
        int refTeam = userTeam >= 0 ? userTeam : HomeTeam;   // same reference the cue uses
        teamId = _confirmedHolder == PossessionHolder.Reference ? refTeam : 1 - refTeam;
        return true;
    }

    private static PossessionSnapshot ReadPossessionSnapshot()
    {
        var match = CurrentMatch();
        var ctrl = CurrentController();
        if (match == null || ctrl == null) return PossessionSnapshot.OfGap("not in a match");

        var mType = match.GetType();
        _matchPeriod ??= mType.GetProperty("MatchPeriod", AllInstance);
        _hasStarted ??= mType.GetProperty("HasStarted", AllInstance);
        _timeSlice ??= mType.GetProperty("CurrentTimeSlice", AllInstance);
        _userTeamId ??= mType.GetProperty("UserTeamId", AllInstance);
        string period = _matchPeriod.GetValue(match).ToString();
        bool hasStarted = (bool)_hasStarted.GetValue(match);
        if (!hasStarted || !IsBallInPlayPeriod(period))
            return PossessionSnapshot.OfGap("ball position " + Speech.Degrade.Unavailable);

        int slice = Convert.ToInt32(_timeSlice.GetValue(match));
        int userTeam = Convert.ToInt32(_userTeamId.GetValue(match));

        var ctrlType = ctrl.GetType();
        _playbackIface ??= ctrlType.GetProperty("PlaybackInterface", AllInstance);
        var playback = _playbackIface.GetValue(ctrl);
        _getBallPosition ??= playback.GetType().GetMethod("GetBallPosition", AllInstance);
        if (_getBallPosition == null)
        {
            NoteDegradeOnce("[MatchBallLocation] GetBallPosition not found for possession read.");
            return PossessionSnapshot.OfGap("possession " + Speech.Degrade.Unavailable);
        }
        var ball = (Vector3)_getBallPosition.Invoke(playback, new object[] { slice });
        ReadPitchDimensions(ctrlType, ctrl, out float pitchLength, out float pitchWidth);
        float maxX = pitchWidth / 2f + OnPitchMargin;
        float maxZ = pitchLength / 2f + OnPitchMargin;

        var players = UnityEngine.Object.FindObjectsOfType<FM.Match.Player>();
        if (players == null || players.Length == 0)
        {
            // The rendered Player objects exist only during a highlight/replay. Between highlights the
            // per-player positions simply are not there, so we cannot judge possession honestly.
            NoteDegradeOnce("[MatchBallLocation] no on-pitch player objects (between highlights?).");
            return PossessionSnapshot.OfGap("possession " + Speech.Degrade.Unavailable);
        }

        var pType = players[0].GetType();
        _playerPosition ??= pType.GetProperty("Position", AllInstance);
        _playerTeamId ??= pType.GetProperty("TeamID", AllInstance);
        _playerName ??= pType.GetProperty("BillboardName", AllInstance);
        _playerKind ??= pType.GetProperty("EntityKind", AllInstance);
        if (_playerPosition == null || _playerTeamId == null || _playerName == null || _playerKind == null)
        {
            NoteDegradeOnce("[MatchBallLocation] player property missing "
                + $"(Position={_playerPosition != null} TeamID={_playerTeamId != null} BillboardName={_playerName != null} EntityKind={_playerKind != null}).");
            return PossessionSnapshot.OfGap("possession " + Speech.Degrade.Unavailable);
        }

        // Nearest on-pitch player of each team to the ball. On-pitch = a real Player entity (EntityKind 0,
        // not referee/assistant/manager/medical) within the pitch bounds (the bench sits ~5 m off the touchline).
        float dHome = float.MaxValue, dAway = float.MaxValue;
        string nameHome = null, nameAway = null;
        for (int i = 0; i < players.Length; i++)
        {
            var p = players[i];
            if (Convert.ToInt32(_playerKind.GetValue(p)) != 0) continue;               // not a Player entity
            var pos = (Vector3)_playerPosition.GetValue(p);
            if (Math.Abs(pos.x) > maxX || Math.Abs(pos.z) > maxZ) continue;            // off the pitch (bench)
            float dx = pos.x - ball.x, dz = pos.z - ball.z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            int team = Convert.ToInt32(_playerTeamId.GetValue(p));
            if (team == 0) { if (d < dHome) { dHome = d; nameHome = _playerName.GetValue(p)?.ToString(); } }
            else if (team == 1) { if (d < dAway) { dAway = d; nameAway = _playerName.GetValue(p)?.ToString(); } }
        }
        if (dHome == float.MaxValue && dAway == float.MaxValue)
            return PossessionSnapshot.OfGap("possession " + Speech.Degrade.Unavailable);

        bool homeNearer = dHome <= dAway;
        float nearestDist = homeNearer ? dHome : dAway;
        string nearestName = (homeNearer ? nameHome : nameAway) ?? "unknown player";
        int nearestTeam = homeNearer ? 0 : 1;
        string teamWord = userTeam < 0
            ? (nearestTeam == 0 ? "home" : "away")
            : (nearestTeam == userTeam ? "your team" : "opposition");

        _lastDegradeReason = null;
        // Out of play: possession is just who is nearest the dead ball (the likely taker). No loose /
        // contested / aerial states apply while the ball is off the field. Name the restart from the live
        // event stream where geometry cannot, cross-checked against the line (a stale event stays "out of
        // play"). Direction is not needed to tell a touchline from a goal-line, so a fixed direction is fine.
        if (IsBallOutOfBounds(ball, pitchLength, pitchWidth))
        {
            var lineKind = DowngradeDeepNet(
                Navigation.BallLocation.ClassifyOutOfPlay(
                    ball.x, ball.z, ball.y, true, pitchLength / 2f, pitchWidth / 2f, OutMargin, GoalHalfWidth, CrossbarHeight),
                ball.z, pitchLength / 2f);
            string lead = "out of play";
            if (TryLatestRestart(out var rKind, out bool rForRef))
                lead = Navigation.RestartReadout.Label(lineKind, rKind, rForRef) ?? "out of play";
            return PossessionSnapshot.OfInfo(lead + ", " + nearestName, teamWord);
        }
        // The cues' confirmed holder outranks this instant's geometry, so the spoken answer names whoever the
        // metronome the player can hear is ticking for. The ball-state phrases below still describe the BALL
        // (in the air, in dispute) — those match the air and contested clips, which play over the tick rather
        // than instead of it. Only the man named changes: the holder, not whoever happens to be nearest a
        // ball in flight. With no live claim to consult this falls through to the instantaneous read.
        bool confirmed = TryConfirmedTeam(userTeam, out int holdTeam);
        if (confirmed)
        {
            nearestName = (holdTeam == 0 ? nameHome : nameAway) ?? nearestName;
            teamWord = userTeam < 0
                ? (holdTeam == 0 ? "home" : "away")
                : (holdTeam == userTeam ? "your team" : "opposition");
        }

        // Both teams are disputing the ball: a genuine 50-50, or a challenge too close to call. The holder is
        // named first when there is one, so the order matches which metronome is still ticking underneath.
        if (IsContested(dHome, dAway))
        {
            bool holderFirst = confirmed ? holdTeam == 0 : homeNearer;
            string first = holderFirst ? nameHome : nameAway;
            string second = holderFirst ? nameAway : nameHome;
            return PossessionSnapshot.OfInfo("contested, " + (first ?? "unknown player") + " and " + (second ?? "unknown player"));
        }
        if (ball.y > AerialHeight)
            return confirmed
                ? PossessionSnapshot.OfInfo("ball in the air, " + nearestName, teamWord)
                : PossessionSnapshot.OfInfo("ball in the air, nearest " + nearestName);
        if (confirmed || nearestDist <= OnBallRadius) return PossessionSnapshot.OfInfo(nearestName, teamWord);
        return PossessionSnapshot.OfInfo("loose ball, nearest " + nearestName);
    }

    /// <summary>The stable token the companion keys its cue sound off, for a given zone.</summary>
    public static string CueToken(CueZone zone)
    {
        switch (zone)
        {
            case CueZone.Defensive: return "defensive";
            case CueZone.Middle: return "middle";
            case CueZone.Attacking: return "attacking";
            case CueZone.Box: return "box";
            default: return "off";
        }
    }

    /// <summary>
    /// The one ball read the SSE tick needs for the match audio: the possession-gated cue zone for each team
    /// (the reference team's <c>Zone</c> and the opposition's <c>OppZone</c>, each named relative to its own
    /// attacking direction so the two metronomes are exactly parallel) and where the ball is relative to the
    /// field (in play, or which line it has crossed). The SSE loop runs one metronome off each zone and
    /// edge-detects the out-of-play one-shot sounds off the kind.
    ///
    /// Goals are NOT detected here — a real goal in the net is reported as <see cref="Navigation.OutOfPlayKind.Net"/>
    /// only so the SSE loop can suppress the goal-line sound for it; the goal sound itself comes from the
    /// engine event stream via <see cref="ScanGoals"/>, which fires on the real
    /// <c>EventGoalScored</c> reaching playback rather than guessing from ball geometry. <c>InReplay</c> is
    /// true while the viewer is re-showing a replay, where the ball re-traces the play; the SSE loop uses it
    /// to suppress the out-of-play one-shots so a replayed throw-in does not echo.
    ///
    /// <c>Contested</c> is true while both teams have a man within the contest radius of the ball (a 50-50),
    /// which the SSE loop turns into a sustained looping sound.
    ///
    /// <c>Air</c> is true while the ball is in the air (above the aerial height) and NOT contested — the
    /// same judgement the possession readout makes, where a contested aerial is a duel, not a flight. The SSE
    /// loop turns it into one long clip that starts when the flight starts and stops when it is interrupted.
    ///
    /// <c>Pan</c> is the ball's stereo position along the pitch length (-1 full left .. +1 full right,
    /// reference team attacking rightward in a first half), which the companion applies to every match
    /// sound. <see cref="float.NaN"/> when no reading is available (the SSE loop then emits nothing, so
    /// the client keeps its last pan).
    ///
    /// <c>DistRef</c>/<c>DistOpp</c> are the straight-line metres from the ball to the goal each team is
    /// attacking (the Alt+7 measure — distance to the goal itself, not to the goal-line), which the SSE
    /// loop pushes for whichever team's cue is active so the client can accelerate that team's tick as
    /// it closes on goal. <see cref="float.NaN"/> when no reading is available.
    ///
    /// <c>Slice</c> is the playback time slice of this read (-1 when no reading). The SSE loop compares
    /// consecutive slices: an unmoved slice means playback is halted (paused, a stopped replay) with the
    /// action frozen on screen, so it mutes every cue until the slice moves again.
    ///
    /// Main thread only: reads live Unity match objects directly, so it is meant to be called from inside
    /// an existing <see cref="MainThreadDispatcher"/> dispatch (the SSE tick), never re-dispatched. Never
    /// caches game state; degrades to everything-off (no cue, in play, not contested) on any read gap so the
    /// player hears honest silence, not a wrong tick.
    /// </summary>
    public static (CueControl Control, CueZone Zone, CueControl OppControl, CueZone OppZone,
                   Navigation.OutOfPlayKind OutKind, bool InReplay, bool Contested, bool Air, float Pan,
                   float DistRef, float DistOpp, float Drone, float AirLevel, int Slice,
                   float PressureOnRef, float PressureOnOpp, float NearestMan) ComputeMatchAudio()
    {
        var reading = ReadState(out _);   // no match / not in play -> everything off, pan unavailable
        if (reading == null)
            return (CueControl.None, CueZone.Off, CueControl.None, CueZone.Off,
                    Navigation.OutOfPlayKind.InPlay, false, false, false, float.NaN, float.NaN, float.NaN, 0f,
                    float.NaN, -1, 0f, 0f, float.NaN);
        var r = reading.Value;
        var outKind = ClassifyOut(r);
        var (control, zone, oppControl, oppZone, contested, air, pressureOnRef, pressureOnOpp, nearestMan) =
            ComputeCue(r, outKind);
        // Dev measurement only (GET /airprobe), for anchoring the in-the-air cue's loudness ramp to
        // heights the engine actually produces. Raw aerial, not the Air cue: the ramp should cover every
        // flight, including the contested ones the cue itself stays out of.
        AirHeightProbe.Observe(r.Ball.y, r.Ball.y > AerialHeight, r.InReplay, r.Slice);
        // Stereo pan of every match sound: the ball's goal-to-goal position across the stereo field, as a
        // televised view — the home team attacking rightward in a first half, the camera never moving.
        float pan = (float)Navigation.BallLocation.ComputeLengthPan(
            r.Ball.z, r.ScreenRightIsPositiveZ, r.PitchLength / 2f);
        // Straight-line metres to the goal each team attacks (the Alt+7 measure), for the tick acceleration.
        float halfLen = r.PitchLength / 2f;
        float distRef = (float)Navigation.BallLocation.MetresToAttackingGoal(r.Ball.x, r.Ball.z, r.AttackingUpfield, halfLen);
        float distOpp = (float)Navigation.BallLocation.MetresToAttackingGoal(r.Ball.x, r.Ball.z, !r.AttackingUpfield, halfLen);
        // Touchline drones: the width axis the stereo field cannot carry, since pan is spent on the length.
        // Signed intensity — which touchline, and how near. Deliberately read from the raw world coordinate
        // with no attacking direction, so a touchline stays the same touchline all match. Unlike the cues,
        // this is NOT gated on possession or on the ball being in play: it is a statement about where the
        // ball is, which stays true and useful while it is dead at a throw-in.
        // Near is +X exactly when screen-right is +Z, so the drones take the same bearing as the pan.
        float drone = (float)Navigation.BallLocation.ComputeTouchlineDrone(
            r.Ball.x, r.PitchWidth / 2f, r.ScreenRightIsPositiveZ);
        // How high the ball is, as a 0..1 ramp position for the in-the-air cue's loudness. Computed
        // whatever the cue is doing: the level must already be right at the instant Air turns on, or the
        // sound would start at the previous flight's loudness and slide to the correct one.
        float airLevel = (float)Navigation.BallLocation.ComputeAirLoudness(r.Ball.y);
        return (control, zone, oppControl, oppZone, outKind, r.InReplay, contested, air, pan, distRef, distOpp,
                drone, airLevel, r.Slice, pressureOnRef, pressureOnOpp, nearestMan);
    }

    /// <summary>
    /// The result of one goal scan: whether the engine event stream was readable, the current playback
    /// high-water event index, whether a replay is showing, and — if a new goal entered the played
    /// range since <c>afterIndex</c> — that it <see cref="Crossed"/> and whether it was <see cref="ForRef"/>
    /// (the reference team scored). <see cref="Ok"/> is false when there is no match or the event stream
    /// cannot be read, in which case the caller keeps its high-water untouched.
    /// </summary>
    internal readonly struct GoalScan
    {
        public readonly bool Ok;
        public readonly int LastEventIndex;
        public readonly bool InReplay;
        public readonly bool Crossed;
        public readonly bool ForRef;
        public readonly int Slice;   // engine time of the crossed goal, for ordering against restarts

        public GoalScan(bool ok, int lastEventIndex, bool inReplay, bool crossed, bool forRef, int slice)
        {
            Ok = ok;
            LastEventIndex = lastEventIndex;
            InReplay = inReplay;
            Crossed = crossed;
            ForRef = forRef;
            Slice = slice;
        }
    }

    /// <summary>
    /// Position-free goal detection from the engine's commentary event stream. Scans the events the playback
    /// has reached since <paramref name="afterIndex"/> for a goal — the game's own classifier
    /// <c>IsAnyTypeOfGoal</c>, i.e. <c>EventGoalScored</c> or <c>EventIllegalGoalScored</c>, exactly one per
    /// ball-in-the-net moment — and attributes it via the event's <c>Team</c>. A disallowed goal (offside,
    /// whistle already gone) counts here on purpose: the cue marks the drama at the goalmouth, not the
    /// scoreline, and staying silent through a chalked-off goal drops the player out of the match while the
    /// commentary carries on about it. The engine's own correction ("But it won't count!", "offside though")
    /// arrives on the commentary channel within seconds, and the score readout never moves. The full
    /// <c>m_commentaryEvents</c> list is the SIMULATION frontier (events ahead of what is on screen), but
    /// <c>m_currentLastEventIndex</c> is the PLAYBACK high-water, so scanning up to it fires in sync with the
    /// highlight the player watches — not ahead (a spoiler) and not from ball geometry (unreliable). The
    /// caller (the SSE loop) holds the high-water and only advances it while live, so the goal's own replay
    /// (which rewinds the event window and re-crosses the goal) never re-fires.
    ///
    /// <paramref name="afterIndex"/> &lt; 0 means "baseline only": report the current high-water without
    /// scanning, so a reconnect mid-match adopts the played state silently instead of firing for goals
    /// already shown. Main thread only (reads live Unity objects); returns <c>Ok = false</c> on any gap.
    /// </summary>
    public static GoalScan ScanGoals(int afterIndex)
    {
        var match = CurrentMatch();
        if (match == null) return default;   // no match: caller keeps its high-water

        var mType = match.GetType();
        _commentaryEvents ??= mType.GetProperty("m_commentaryEvents", AllInstance);
        _lastEventIndex ??= mType.GetProperty("m_currentLastEventIndex", AllInstance);
        _userTeamId ??= mType.GetProperty("UserTeamId", AllInstance);
        if (_commentaryEvents == null || _lastEventIndex == null || _userTeamId == null)
        {
            NoteDegradeOnce("[MatchBallLocation] goal event stream member missing "
                + $"(m_commentaryEvents={_commentaryEvents != null} m_currentLastEventIndex={_lastEventIndex != null} UserTeamId={_userTeamId != null}).");
            return default;
        }

        var list = _commentaryEvents.GetValue(match)
            as Il2CppSystem.Collections.Generic.List<FM.Match.MatchCommentaryEvent>;
        if (list == null) return default;   // not populated yet (pre-kickoff): baseline next tick

        int last = Convert.ToInt32(_lastEventIndex.GetValue(match));
        int userTeam = Convert.ToInt32(_userTeamId.GetValue(match));
        int refTeam = userTeam >= 0 ? userTeam : 0;   // reference is the user's team, else home (id 0)
        bool inReplay = ReadInReplay(mType, match);
        if (!CanReadEventWindow(afterIndex, last, inReplay)) return default;

        bool crossed = false, forRef = false;
        int goalSlice = 0;
        if (afterIndex >= 0)
        {
            int hi = Math.Min(last, list.Count - 1);
            for (int i = afterIndex + 1; i <= hi; i++)
            {
                var ev = list[i];
                var id = ev.CommentaryEventID;
                if (FM.Match.CommentaryEventDefinitionsExtensions.IsAnyTypeOfGoal(id))
                {
                    crossed = true;
                    forRef = AttributeGoal(ev, refTeam);   // keep the most recent goal in this span
                    goalSlice = ev.TimeSlice;
                }
            }
        }

        _lastDegradeReason = null;   // a clean read clears the dedup latch
        return new GoalScan(true, last, inReplay, crossed, forRef, goalSlice);
    }

    // Which side scored, relative to the reference team. The event's Team (Home=0/Away=1) is authoritative and
    // matches the engine's Home/Away ordering. Invalid should never occur for a real goal; fall back to the
    // player-teams encoding (a visitor bit means the away side) and log once rather than lie or go silent.
    private static bool AttributeGoal(FM.Match.MatchCommentaryEvent ev, int refTeam)
        => AttributeToRef(ev, refTeam, "goal");
    private static bool CanReadEventWindow(int afterIndex, int lastIndex, bool inReplay)
    {
        if (MatchEventWindow.CanRead(afterIndex, lastIndex, inReplay)) return true;
        EventLog.Record("eventwindow", $"live commentary rewind ignored: {afterIndex} -> {lastIndex}");
        return false;
    }

    /// <summary>
    /// The result of one restart scan, mirroring <see cref="GoalScan"/>: whether the event stream was
    /// readable, the played high-water event index, whether a replay is showing, and — if a throw-in,
    /// corner, or goal kick restart entered the played range since <c>afterIndex</c> — that it
    /// <see cref="Occurred"/>, its <see cref="Kind"/>, and whether it is <see cref="ForRef"/> (the reference
    /// team is the restarting side). The most recent such restart in the span wins. The same pass also
    /// reports whether the ball was put back into play (<see cref="PutOccurred"/>: a kick-off or a taken
    /// throw-in / goal kick / corner / free kick; a penalty sounds as a shot attempt instead) and that event's engine time
    /// (<see cref="PutSlice"/>) for the put-in-play ping's same-take dedupe. <see cref="Ok"/> is false
    /// when there is no match or the stream cannot be read, in which case the caller keeps its high-water.
    /// </summary>
    internal readonly struct RestartScan
    {
        public readonly bool Ok;
        public readonly int LastEventIndex;
        public readonly bool InReplay;
        public readonly bool Occurred;
        public readonly Navigation.RestartKind Kind;
        public readonly bool ForRef;
        public readonly bool PutOccurred;
        public readonly int PutSlice;

        public RestartScan(bool ok, int lastEventIndex, bool inReplay, bool occurred, Navigation.RestartKind kind, bool forRef,
            bool putOccurred, int putSlice)
        {
            Ok = ok;
            LastEventIndex = lastEventIndex;
            InReplay = inReplay;
            Occurred = occurred;
            Kind = kind;
            ForRef = forRef;
            PutOccurred = putOccurred;
            PutSlice = putSlice;
        }
    }

    /// <summary>
    /// Position-free out-of-play detection from the engine's commentary event stream, the sibling of
    /// <see cref="ScanGoals"/>. Scans the played event range since <paramref name="afterIndex"/> for a
    /// restart the ball went out for — the engine's own <c>EventRestartThrowIn</c> / <c>EventRestartCorner</c>
    /// / <c>EventRestartGoalKick</c> (confirmed live to enter the played window at the moment the ball goes
    /// out; the <c>EventSeeBallOutOfPlayFor*</c> family never fires) — and attributes it via the event's
    /// <c>Team</c> (the restarting side). The same pass also detects the ball being put back into play (a
    /// kick-off or a <c>Take*</c> event) for the put-in-play ping. The played window
    /// <c>[.. m_currentLastEventIndex]</c> is used, not the simulation frontier, so it fires in sync with
    /// the highlight; the caller holds the high-water and only advances it while live so a restart's replay
    /// never re-fires.
    ///
    /// <paramref name="afterIndex"/> &lt; 0 means "baseline only". Main thread only; returns <c>Ok = false</c>
    /// on any gap.
    /// </summary>
    public static RestartScan ScanRestarts(int afterIndex)
    {
        var match = CurrentMatch();
        if (match == null) return default;

        var mType = match.GetType();
        _commentaryEvents ??= mType.GetProperty("m_commentaryEvents", AllInstance);
        _lastEventIndex ??= mType.GetProperty("m_currentLastEventIndex", AllInstance);
        _userTeamId ??= mType.GetProperty("UserTeamId", AllInstance);
        if (_commentaryEvents == null || _lastEventIndex == null || _userTeamId == null)
        {
            NoteDegradeOnce("[MatchBallLocation] restart event stream member missing "
                + $"(m_commentaryEvents={_commentaryEvents != null} m_currentLastEventIndex={_lastEventIndex != null} UserTeamId={_userTeamId != null}).");
            return default;
        }

        var list = _commentaryEvents.GetValue(match)
            as Il2CppSystem.Collections.Generic.List<FM.Match.MatchCommentaryEvent>;
        if (list == null) return default;

        int last = Convert.ToInt32(_lastEventIndex.GetValue(match));
        int userTeam = Convert.ToInt32(_userTeamId.GetValue(match));
        int refTeam = userTeam >= 0 ? userTeam : 0;
        bool inReplay = ReadInReplay(mType, match);
        if (!CanReadEventWindow(afterIndex, last, inReplay)) return default;

        bool occurred = false, forRef = false;
        var kind = Navigation.RestartKind.None;
        bool putOccurred = false;
        int putSlice = 0;
        if (afterIndex >= 0)
        {
            int hi = Math.Min(last, list.Count - 1);
            for (int i = afterIndex + 1; i <= hi; i++)
            {
                var ev = list[i];
                var id = ev.CommentaryEventID;
                var k = ClassifyRestart(id);
                if (k != Navigation.RestartKind.None)
                {
                    occurred = true;
                    kind = k;
                    forRef = AttributeToRef(ev, refTeam, "restart");   // keep the most recent restart in the span
                    continue;
                }
                if (ClassifyPutInPlay(id))
                {
                    putOccurred = true;
                    putSlice = ev.TimeSlice;   // keep the most recent take in the span
                }
            }
        }

        _lastDegradeReason = null;
        return new RestartScan(true, last, inReplay, occurred, kind, forRef, putOccurred, putSlice);
    }

    /// <summary>
    /// The result of one good-defence scan, mirroring <see cref="RestartScan"/>: whether the event stream was
    /// readable, the played high-water event index, whether a replay is showing, and — if a successful /
    /// impactful defensive event (interception, tackle won, save, block, ball won back) entered the played
    /// range since <c>afterIndex</c> — that it <see cref="Occurred"/>, whether it is <see cref="ForRef"/> (the
    /// reference team made the play), and its engine time (<see cref="Slice"/>) for the one-shot's
    /// same-moment dedupe. The most recent such event in the span wins. <see cref="Ok"/> is false when there
    /// is no match or the stream cannot be read, in which case the caller keeps its high-water.
    /// </summary>
    internal readonly struct DefenceScan
    {
        public readonly bool Ok;
        public readonly int LastEventIndex;
        public readonly bool InReplay;
        public readonly bool Occurred;
        public readonly bool ForRef;
        public readonly int Slice;

        public DefenceScan(bool ok, int lastEventIndex, bool inReplay, bool occurred, bool forRef, int slice)
        {
            Ok = ok;
            LastEventIndex = lastEventIndex;
            InReplay = inReplay;
            Occurred = occurred;
            ForRef = forRef;
            Slice = slice;
        }
    }

    /// <summary>
    /// Position-free good-defence detection from the engine's commentary event stream, the sibling of
    /// <see cref="ScanRestarts"/> and <see cref="ScanGoals"/>. Scans the played event range since
    /// <paramref name="afterIndex"/> for a successful or impactful defensive action — the curated set in
    /// <see cref="IsGoodDefence"/> (interceptions, tackles won, saves, blocks, ball won back) — and attributes
    /// the acting team via the event's <c>Team</c> (confirmed live to be the side that MADE the play, not the
    /// side that lost the ball). The played window <c>[.. m_currentLastEventIndex]</c> is used, not the
    /// simulation frontier, so it fires in sync with the highlight; the caller holds the high-water and only
    /// advances it while live so a replay never re-fires.
    ///
    /// <paramref name="afterIndex"/> &lt; 0 means "baseline only". Main thread only; returns <c>Ok = false</c>
    /// on any gap.
    /// </summary>
    public static DefenceScan ScanDefence(int afterIndex)
    {
        var match = CurrentMatch();
        if (match == null) return default;

        var mType = match.GetType();
        _commentaryEvents ??= mType.GetProperty("m_commentaryEvents", AllInstance);
        _lastEventIndex ??= mType.GetProperty("m_currentLastEventIndex", AllInstance);
        _userTeamId ??= mType.GetProperty("UserTeamId", AllInstance);
        if (_commentaryEvents == null || _lastEventIndex == null || _userTeamId == null)
        {
            NoteDegradeOnce("[MatchBallLocation] defence event stream member missing "
                + $"(m_commentaryEvents={_commentaryEvents != null} m_currentLastEventIndex={_lastEventIndex != null} UserTeamId={_userTeamId != null}).");
            return default;
        }

        var list = _commentaryEvents.GetValue(match)
            as Il2CppSystem.Collections.Generic.List<FM.Match.MatchCommentaryEvent>;
        if (list == null) return default;

        int last = Convert.ToInt32(_lastEventIndex.GetValue(match));
        int userTeam = Convert.ToInt32(_userTeamId.GetValue(match));
        int refTeam = userTeam >= 0 ? userTeam : 0;
        bool inReplay = ReadInReplay(mType, match);
        if (!CanReadEventWindow(afterIndex, last, inReplay)) return default;

        bool occurred = false, forRef = false;
        int slice = 0;
        if (afterIndex >= 0)
        {
            int hi = Math.Min(last, list.Count - 1);
            for (int i = afterIndex + 1; i <= hi; i++)
            {
                var ev = list[i];
                if (!IsGoodDefence(ev.CommentaryEventID)) continue;
                occurred = true;
                forRef = AttributeToRef(ev, refTeam, "defence");   // keep the most recent in the span
                slice = ev.TimeSlice;
            }
        }

        _lastDegradeReason = null;
        return new DefenceScan(true, last, inReplay, occurred, forRef, slice);
    }

    /// <summary>
    /// The result of one shot-attempt scan, mirroring <see cref="DefenceScan"/> without the team: whether the
    /// event stream was readable, the played high-water event index, whether a replay is showing, and — if an
    /// attempt on goal entered the played range since <c>afterIndex</c> — that it <see cref="Occurred"/> and
    /// its engine time (<see cref="Slice"/>) for the one-shot's same-moment dedupe. The most recent such event
    /// in the span wins. Both teams' attempts sound alike, so the event's <c>Team</c> is not read.
    /// <see cref="Ok"/> is false when there is no match or the stream cannot be read, in which case the caller
    /// keeps its high-water.
    /// </summary>
    internal readonly struct ShotScan
    {
        public readonly bool Ok;
        public readonly int LastEventIndex;
        public readonly bool InReplay;
        public readonly bool KickOccurred;
        public readonly int KickSlice;
        public readonly bool HeaderOccurred;
        public readonly int HeaderSlice;

        public ShotScan(bool ok, int lastEventIndex, bool inReplay,
            bool kickOccurred, int kickSlice, bool headerOccurred, int headerSlice)
        {
            Ok = ok;
            LastEventIndex = lastEventIndex;
            InReplay = inReplay;
            KickOccurred = kickOccurred;
            KickSlice = kickSlice;
            HeaderOccurred = headerOccurred;
            HeaderSlice = headerSlice;
        }
    }

    /// <summary>
    /// Position-free shot-attempt detection from the engine's commentary event stream, the sibling of
    /// <see cref="ScanDefence"/>, <see cref="ScanRestarts"/> and <see cref="ScanGoals"/>. Scans the played
    /// event range since <paramref name="afterIndex"/> for an attempt on goal — the curated set in
    /// <see cref="IsShotAttempt"/>, which is the STRIKE, not its outcome, so the sound lands before the player
    /// knows whether it went in. The played window <c>[.. m_currentLastEventIndex]</c> is used, not the
    /// simulation frontier, so it fires in sync with the highlight; the caller holds the high-water and only
    /// advances it while live so a replay never re-fires.
    ///
    /// <paramref name="afterIndex"/> &lt; 0 means "baseline only". Main thread only; returns <c>Ok = false</c>
    /// on any gap.
    /// </summary>
    public static ShotScan ScanShots(int afterIndex)
    {
        var match = CurrentMatch();
        if (match == null) return default;

        var mType = match.GetType();
        _commentaryEvents ??= mType.GetProperty("m_commentaryEvents", AllInstance);
        _lastEventIndex ??= mType.GetProperty("m_currentLastEventIndex", AllInstance);
        if (_commentaryEvents == null || _lastEventIndex == null)
        {
            NoteDegradeOnce("[MatchBallLocation] shot event stream member missing "
                + $"(m_commentaryEvents={_commentaryEvents != null} m_currentLastEventIndex={_lastEventIndex != null}).");
            return default;
        }

        var list = _commentaryEvents.GetValue(match)
            as Il2CppSystem.Collections.Generic.List<FM.Match.MatchCommentaryEvent>;
        if (list == null) return default;

        int last = Convert.ToInt32(_lastEventIndex.GetValue(match));
        bool inReplay = ReadInReplay(mType, match);
        if (!CanReadEventWindow(afterIndex, last, inReplay)) return default;

        bool kickOccurred = false, headerOccurred = false;
        int kickSlice = 0, headerSlice = 0;
        if (afterIndex >= 0)
        {
            int hi = Math.Min(last, list.Count - 1);
            for (int i = afterIndex + 1; i <= hi; i++)
            {
                var ev = list[i];
                var id = ev.CommentaryEventID;
                if (!IsShotAttempt(id)) continue;
                // Headed and kicked attempts are reported separately so they can arbitrate independently:
                // a shot blocked and headed back in moments later is two attempts, and with two distinct
                // clips hearing both cannot be mistaken for one strike double-triggering. Most recent of
                // each kind in the span wins.
                if (IsHeaderAttempt(id)) { headerOccurred = true; headerSlice = ev.TimeSlice; }
                else { kickOccurred = true; kickSlice = ev.TimeSlice; }
            }
        }

        _lastDegradeReason = null;
        return new ShotScan(true, last, inReplay, kickOccurred, kickSlice, headerOccurred, headerSlice);
    }

    // How far back through the played events the on-demand readout looks for the current dead ball's restart.
    // The restart that put the ball out is the most recent one, a handful of events back; a small bound keeps
    // the keypress cheap and avoids walking the whole list.
    private const int LatestRestartScanBack = 40;

    /// <summary>
    /// The most recent restart in the played events (the one the current dead ball went out for), for the
    /// on-demand Alt+5 location and possession readouts. Scans back from the playback high-water for the nearest
    /// throw-in / corner / goal-kick and returns its <paramref name="kind"/> and whether it is the reference
    /// team's (<paramref name="forRef"/>). False when there is no match, the stream is unreadable, or no
    /// restart is found in range. The caller cross-checks the kind against the ball's line geometry (via
    /// <see cref="Navigation.RestartReadout.Label"/>) so a stale restart cannot mislabel. Main thread only;
    /// re-read live on every keypress, never cached.
    /// </summary>
    public static bool TryLatestRestart(out Navigation.RestartKind kind, out bool forRef)
    {
        kind = Navigation.RestartKind.None;
        forRef = false;

        var match = CurrentMatch();
        if (match == null) return false;

        var mType = match.GetType();
        _commentaryEvents ??= mType.GetProperty("m_commentaryEvents", AllInstance);
        _lastEventIndex ??= mType.GetProperty("m_currentLastEventIndex", AllInstance);
        _userTeamId ??= mType.GetProperty("UserTeamId", AllInstance);
        if (_commentaryEvents == null || _lastEventIndex == null || _userTeamId == null) return false;

        var list = _commentaryEvents.GetValue(match)
            as Il2CppSystem.Collections.Generic.List<FM.Match.MatchCommentaryEvent>;
        if (list == null) return false;

        int last = Convert.ToInt32(_lastEventIndex.GetValue(match));
        int refTeam = Convert.ToInt32(_userTeamId.GetValue(match));
        refTeam = refTeam >= 0 ? refTeam : 0;

        int hi = Math.Min(last, list.Count - 1);
        int lo = Math.Max(0, hi - LatestRestartScanBack);
        for (int i = hi; i >= lo; i--)
        {
            var k = ClassifyRestart(list[i].CommentaryEventID);
            if (k == Navigation.RestartKind.None) continue;
            kind = k;
            forRef = AttributeToRef(list[i], refTeam, "restart");
            return true;
        }
        return false;
    }

    // Map an engine commentary event id onto the restart the ball went out for, or None for anything else.
    // These three restart ids were confirmed live to fire (the See*/Take* siblings do not drive the sound).
    private static Navigation.RestartKind ClassifyRestart(SI.MatchTypes.CommentaryEventDefinitions id)
    {
        switch (id)
        {
            case SI.MatchTypes.CommentaryEventDefinitions.EventRestartThrowIn: return Navigation.RestartKind.ThrowIn;
            case SI.MatchTypes.CommentaryEventDefinitions.EventRestartCorner: return Navigation.RestartKind.Corner;
            case SI.MatchTypes.CommentaryEventDefinitions.EventRestartGoalKick: return Navigation.RestartKind.GoalKick;
            default: return Navigation.RestartKind.None;
        }
    }

    // Whether an engine commentary event id is the ball being put (back) into play: a kick-off (each half,
    // and after a goal) or a set piece being taken — throw-in, goal kick, corner, free kick (including the
    // short variants). These drive the put-in-play ping. Take* events were confirmed live to fire
    // when the taker plays the ball, ~100+ slices after the Restart* award.
    private static bool ClassifyPutInPlay(SI.MatchTypes.CommentaryEventDefinitions id)
    {
        switch (id)
        {
            case SI.MatchTypes.CommentaryEventDefinitions.EventKickOff:
            case SI.MatchTypes.CommentaryEventDefinitions.EventTakeThrowIn:
            case SI.MatchTypes.CommentaryEventDefinitions.EventTakeGoalKick:
            case SI.MatchTypes.CommentaryEventDefinitions.EventTakeCorner:
            case SI.MatchTypes.CommentaryEventDefinitions.EventTakeShortCorner:
            case SI.MatchTypes.CommentaryEventDefinitions.EventTakeCornerToEdgeOfBox:
            case SI.MatchTypes.CommentaryEventDefinitions.EventTakeFreeKick:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShortFreeKick:
                return true;
            // A penalty (EventTakePenalty / EventTakePenaltyForHattrick) does put the ball in play, but it is
            // also an attempt on goal, and the shot-attempt cue fires on the same engine event at the same
            // moment. Two sounds on one kick is noise, and the attempt is the one that carries meaning — it
            // opens the wait for the goal or save sound. So the ping stays quiet here and IsShotAttempt owns
            // the penalty.
            default:
                return false;
        }
    }

    // The curated set of engine commentary events that count as a successful or impactful defensive play, from
    // docs/reference/possession-defensive-triggers.md (the confident list, live-frequency reviewed). The
    // event's Team is the side that MADE the play, so the one-shot sounds "gooddefence" for the reference team
    // and "oppgooddefence" for the opposition. Deliberately EXCLUDES the noise families (aerial 50-50s,
    // routine clearances, long-ball contests) and the same-team keeper distribution; see the doc for why each
    // is in or out. Weather variants (Wet/Windy/...) are included alongside their base where they exist.
    /// <summary>
    /// The most recent played turnover event since <paramref name="afterIndex"/> — the engine stating that a
    /// named team took the ball off the other — mirroring <see cref="ScanDefence"/> in shape, gating and
    /// replay handling so all four event scanners behave identically.
    ///
    /// This is the only signal in the possession chain that does not come from our own geometry, and that is
    /// the point of it. The cue infers turnovers from a contact radius, which measured a median 2.75s behind
    /// the engine's own account of the same turnovers (and missed one in three entirely), because an
    /// interception happens while the ball is travelling and the geometry only sees it once the taker has
    /// settled it. An `EventGoodIntercept` carrying a team is not an inference, so it does not need the
    /// dwell that exists to filter uncertain evidence.
    ///
    /// The played window <c>[.. m_currentLastEventIndex]</c> is used, never the simulation frontier, so a
    /// turnover can never be announced before the player has watched it happen.
    ///
    /// Main thread only. <c>Ok = false</c> on any gap, in which case the caller keeps its high-water.
    /// </summary>
    public static TurnoverScan ScanTurnovers(int afterIndex)
    {
        var match = CurrentMatch();
        if (match == null) return default;

        var mType = match.GetType();
        _commentaryEvents ??= mType.GetProperty("m_commentaryEvents", AllInstance);
        _lastEventIndex ??= mType.GetProperty("m_currentLastEventIndex", AllInstance);
        _userTeamId ??= mType.GetProperty("UserTeamId", AllInstance);
        if (_commentaryEvents == null || _lastEventIndex == null || _userTeamId == null)
        {
            NoteDegradeOnce("[MatchBallLocation] turnover event stream member missing "
                + $"(m_commentaryEvents={_commentaryEvents != null} m_currentLastEventIndex={_lastEventIndex != null} UserTeamId={_userTeamId != null}).");
            return default;
        }

        var list = _commentaryEvents.GetValue(match)
            as Il2CppSystem.Collections.Generic.List<FM.Match.MatchCommentaryEvent>;
        if (list == null) return default;

        int last = Convert.ToInt32(_lastEventIndex.GetValue(match));
        int userTeam = Convert.ToInt32(_userTeamId.GetValue(match));
        int refTeam = userTeam >= 0 ? userTeam : 0;
        bool inReplay = ReadInReplay(mType, match);
        if (!CanReadEventWindow(afterIndex, last, inReplay)) return default;

        bool occurred = false, forRef = false;
        int slice = 0;
        if (afterIndex >= 0)
        {
            int hi = Math.Min(last, list.Count - 1);
            for (int i = afterIndex + 1; i <= hi; i++)
            {
                var ev = list[i];
                if (!IsTurnover(ev.CommentaryEventID)) continue;
                occurred = true;
                forRef = AttributeToRef(ev, refTeam, "turnover");   // keep the most recent in the span
                slice = ev.TimeSlice;
            }
        }

        return new TurnoverScan(true, last, inReplay, occurred, forRef, slice);
    }

    /// <summary>
    /// One turnover scan: whether the stream was readable, the played high-water, whether a replay is
    /// showing, and — if the engine narrated a team winning the ball in the span — that it
    /// <see cref="Occurred"/>, whether it was the reference team (<see cref="ForRef"/>), and the engine time
    /// it happened at. Mirrors <see cref="DefenceScan"/>; the most recent such event in the span wins,
    /// because possession is a latest-wins fact rather than a queue of moments to replay.
    /// </summary>
    public readonly struct TurnoverScan
    {
        public readonly bool Ok;
        public readonly int LastEventIndex;
        public readonly bool InReplay;
        public readonly bool Occurred;
        public readonly bool ForRef;
        public readonly int Slice;

        public TurnoverScan(bool ok, int lastEventIndex, bool inReplay, bool occurred, bool forRef, int slice)
        {
            Ok = ok;
            LastEventIndex = lastEventIndex;
            InReplay = inReplay;
            Occurred = occurred;
            ForRef = forRef;
            Slice = slice;
        }
    }

    /// <summary>
    /// Events where the engine states one team took the ball off the other. A strict subset of
    /// <see cref="IsGoodDefence"/>: saves, blocks and clearances are excluded because they stop an attack
    /// without necessarily transferring possession, and grading the cue against those would count a blocked
    /// shot that rebounds to the attacker as a turnover the cue "missed".
    /// </summary>
    private static bool IsTurnover(SI.MatchTypes.CommentaryEventDefinitions id)
    {
        switch (id)
        {
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoodIntercept:
            case SI.MatchTypes.CommentaryEventDefinitions.EventScatterIntercept:
            case SI.MatchTypes.CommentaryEventDefinitions.EventSemiIntercept:
            case SI.MatchTypes.CommentaryEventDefinitions.EventInterceptThroughBall:
            case SI.MatchTypes.CommentaryEventDefinitions.EventWinsBlockTackle:
            case SI.MatchTypes.CommentaryEventDefinitions.EventWinsBlockTackleWet:
            case SI.MatchTypes.CommentaryEventDefinitions.EventPerfectSlideTackle:
            case SI.MatchTypes.CommentaryEventDefinitions.EventPerfectBlockTackle:
            case SI.MatchTypes.CommentaryEventDefinitions.EventSavingTackle:
            case SI.MatchTypes.CommentaryEventDefinitions.EventPickUpFreeBall:
            case SI.MatchTypes.CommentaryEventDefinitions.EventRobsGoalkeeper:
            case SI.MatchTypes.CommentaryEventDefinitions.EventRobsDefenderInOppArea:
            case SI.MatchTypes.CommentaryEventDefinitions.EventRobsUnderPressureDefender:
            case SI.MatchTypes.CommentaryEventDefinitions.EventRobsDefenderNearOppGoal:
                return true;
            default:
                return false;
        }
    }

    private static bool IsGoodDefence(SI.MatchTypes.CommentaryEventDefinitions id)
    {
        switch (id)
        {
            // Interceptions
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoodIntercept:
            case SI.MatchTypes.CommentaryEventDefinitions.EventScatterIntercept:
            case SI.MatchTypes.CommentaryEventDefinitions.EventInterceptThroughBall:
            // Tackles won
            case SI.MatchTypes.CommentaryEventDefinitions.EventWinsBlockTackle:
            case SI.MatchTypes.CommentaryEventDefinitions.EventWinsBlockTackleWet:
            case SI.MatchTypes.CommentaryEventDefinitions.EventPerfectSlideTackle:
            case SI.MatchTypes.CommentaryEventDefinitions.EventPerfectBlockTackle:
            case SI.MatchTypes.CommentaryEventDefinitions.EventSavingTackle:
            // Ball won back / recoveries
            case SI.MatchTypes.CommentaryEventDefinitions.EventPickUpFreeBall:
            case SI.MatchTypes.CommentaryEventDefinitions.EventRobsGoalkeeper:
            case SI.MatchTypes.CommentaryEventDefinitions.EventRobsDefenderInOppArea:
            case SI.MatchTypes.CommentaryEventDefinitions.EventRobsUnderPressureDefender:
            case SI.MatchTypes.CommentaryEventDefinitions.EventRobsDefenderNearOppGoal:
            case SI.MatchTypes.CommentaryEventDefinitions.EventWinsBallInAirAndClears:
            // Blocks / goal-line stops
            case SI.MatchTypes.CommentaryEventDefinitions.EventBlockShot:
            case SI.MatchTypes.CommentaryEventDefinitions.EventBlockShotHeader:
            case SI.MatchTypes.CommentaryEventDefinitions.EventBlockedOnLine:
            case SI.MatchTypes.CommentaryEventDefinitions.EventClearedOffLine:
            case SI.MatchTypes.CommentaryEventDefinitions.EventKeyClearance:
            case SI.MatchTypes.CommentaryEventDefinitions.EventWallBlocksShot:
            // Goalkeeper claims the ball
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoalieCatchBall:
            case SI.MatchTypes.CommentaryEventDefinitions.EventPerfectGoalieSmotherAtFeet:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoalkeeperHeld:
            // Goalkeeper saves (held and not-held / parried / tipped / punched)
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoodSaveHeld:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGreatSaveHeld:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGreatSaveHeldWet:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGreatSaveHeldWindy:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoodSaveNotHeld:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGreatSaveNotHeld:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoalkeeperParry:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoalkeeperParryWet:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoalkeeperParryUsingHands:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoalkeeperParryUsingFeet:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoalkeeperPunch:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoalkeeperTippedOver:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoalkeeperTippedWide:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoodSaveTippedOver:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoodSaveTippedWide:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGreatSaveTippedOver:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGreatSaveTippedWide:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoalieChipSave:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoalieChipSaveEasy:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoalieChipFailedSave:
            case SI.MatchTypes.CommentaryEventDefinitions.EventSaveSecondAttempt:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGoodSaveSecondAttempt:
            case SI.MatchTypes.CommentaryEventDefinitions.EventGreatSaveSecondAttempt:
            case SI.MatchTypes.CommentaryEventDefinitions.EventSavesPenalty:
            case SI.MatchTypes.CommentaryEventDefinitions.EventSavedPenaltyChipped:
            case SI.MatchTypes.CommentaryEventDefinitions.EventSavedPenaltyPlaced:
            case SI.MatchTypes.CommentaryEventDefinitions.EventSavedPenaltyBlasted:
            // Marking / pressure successes
            case SI.MatchTypes.CommentaryEventDefinitions.EventKeyChallenge:
            case SI.MatchTypes.CommentaryEventDefinitions.EventDefenderSolidAsARock:
            case SI.MatchTypes.CommentaryEventDefinitions.EventDefenderSolidInAir:
            case SI.MatchTypes.CommentaryEventDefinitions.EventUnableToShakeMarker:
            case SI.MatchTypes.CommentaryEventDefinitions.EventForcedBack:
            case SI.MatchTypes.CommentaryEventDefinitions.EventForcedToPass:
            case SI.MatchTypes.CommentaryEventDefinitions.EventPlayerForcedWide:
                return true;
            default:
                return false;
        }
    }

    // The curated set of engine commentary events that are an ATTEMPT ON GOAL — the strike itself, before its
    // outcome is known. The engine narrates a shot in two beats: the attempt, then the outcome a second or so
    // of playback later (live: EventDefaultShot at slice 715, EventShotWide at 721). Only the first beat
    // belongs here; the outcome families (EventShot/HeaderWide|Over|JustWide|ClipsPost|HitsBar, the deflection
    // and "made sure"/distance-goal flavours, saves, blocks) resolve the cue through the existing goal,
    // good-defence, and out-of-play sounds instead. Putting an outcome id in here would collapse the gap the
    // cue exists to create.
    //
    // Covers every way of striking at goal: the generic shot, all foot techniques (low/high drive, chip, lob,
    // banana, hope, volley, half-volley, bicycle kick) with their left/right-foot and weather variants, headers
    // at goal (including diving headers), the same families struck from distance and from a tight angle,
    // direct free kicks, and a penalty being taken. See docs/reference/shot-detection.md.
    private static bool IsShotAttempt(SI.MatchTypes.CommentaryEventDefinitions id)
    {
        switch (id)
        {
            // Generic attempt
            case SI.MatchTypes.CommentaryEventDefinitions.EventDefaultShot:
            // Shots from open play
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotLowDrive:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotLowDriveLeftFoot:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotLowDriveRightFoot:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotLowDriveWet:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotChip:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotChipGoalie:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotLob:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotLobGoalie:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotLobWind:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotBanana:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotHope:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotHopeLeftFoot:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotHopeRightFoot:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotBicycleKick:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotVolley:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotVolleyLeftFoot:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotVolleyRightFoot:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotHalfVolley:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotHalfVolleyLeftFoot:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotHalfVolleyRightFoot:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotHighDrive:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotHighDriveLeftFoot:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotHighDriveRightFoot:
            // Headers at goal
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotHeader:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotDivingHeader:
            // Struck from distance
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceLowDrive:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceLowDriveWet:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceChip:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceChipGoalie:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceLob:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceLobGoalie:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceLobWind:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceBanana:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceHope:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceBicycleKick:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceHeader:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceVolley:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceHalfVolley:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceHighDrive:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceDivingHeader:
            // Struck from a tight angle
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleLowDrive:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleChip:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleChipGoalie:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleLob:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleLobGoalie:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleBanana:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleHope:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleBicycleKick:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleHeader:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleVolley:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleHalfVolley:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleHighDrive:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleDivingHeader:
            // Set pieces struck at goal. The free-kick ids are the SHOT ones, so a free kick played short or
            // crossed in (EventTakeFreeKick) stays silent; only one hit at goal sounds.
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFreeKick:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceFreeKick:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleFreeKick:
            case SI.MatchTypes.CommentaryEventDefinitions.EventTakePenalty:
            case SI.MatchTypes.CommentaryEventDefinitions.EventTakePenaltyForHattrick:
                return true;
            default:
                return false;
        }
    }

    // Which attempts in IsShotAttempt were headed, so a header gets its own clip. Classification is purely by
    // event id, and the engine's naming was verified live to support it: across 38 attempts in one match the
    // header ids resolved to a header outcome 4 times and to a kicked outcome NEVER, while the generic
    // EventDefaultShot resolved to a kicked outcome 7 times and to a header NEVER. (Corroborating an attempt
    // by its outcome is only an offline audit — the outcome arrives after the strike, and waiting for it would
    // close the very gap the cue exists to open. EventBlockShot is useless for that audit either way: it was
    // observed following a header, so it names no technique.)
    //
    // The failure mode is deliberately one-sided. These six ids say "header" unambiguously, so a false header
    // is impossible; a header the engine ever narrated generically would simply fall through to the plain
    // attempt clip, which is still true — a header IS an attempt on goal, just described less precisely. Only
    // EventShotHeader has been seen live; the diving and from-distance/from-angle variants are included on
    // the strength of their names, and misclassifying one costs only that specificity.
    private static bool IsHeaderAttempt(SI.MatchTypes.CommentaryEventDefinitions id)
    {
        switch (id)
        {
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotHeader:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotDivingHeader:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceHeader:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromDistanceDivingHeader:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleHeader:
            case SI.MatchTypes.CommentaryEventDefinitions.EventShotFromAngleDivingHeader:
                return true;
            default:
                return false;
        }
    }

    // Whether the event's team is the reference team, by the engine's authoritative Team (Home=0/Away=1). For
    // a goal that is "the reference team scored"; for a restart it is "the reference team is restarting" (its
    // throw / corner / goal kick); for a defensive play it is "the reference team made the play". Invalid
    // should not occur; fall back to the player-teams encoding (a visitor bit means the away side) and log
    // once rather than lie or go silent.
    private static bool AttributeToRef(FM.Match.MatchCommentaryEvent ev, int refTeam, string what)
    {
        var team = ev.Team;
        if (team == FM.Match.FixtureTeam.Home) return refTeam == 0;
        if (team == FM.Match.FixtureTeam.Away) return refTeam == 1;

        NoteDegradeOnce($"[MatchBallLocation] {what} event Team was Invalid; attributing via PlayerTeams.");
        bool visitorTeam = ev.PlayerTeams != SI.Match.MatchEventPlayerTeams.None;
        return visitorTeam ? refTeam == 1 : refTeam == 0;
    }

    private static Navigation.OutOfPlayKind ClassifyOut(in Reading r)
    {
        float halfLen = r.PitchLength / 2f;
        var kind = Navigation.BallLocation.ClassifyOutOfPlay(
            r.Ball.x, r.Ball.z, r.Ball.y, r.AttackingUpfield,
            halfLen, r.PitchWidth / 2f, OutMargin, GoalHalfWidth, CrossbarHeight);
        return DowngradeDeepNet(kind, r.Ball.z, halfLen);
    }

    // A ball classified Net but sampled more than GoalDepth behind the goal-line has gone behind the goal
    // (e.g. over the bar and down), not into the net. At 2 Hz the crossing itself is missed, so we only see
    // the ball once it has landed deep; downgrade it to an honest GoalLine exit so no false goal fires.
    private static Navigation.OutOfPlayKind DowngradeDeepNet(Navigation.OutOfPlayKind kind, float ballZ, float halfLen)
        => kind == Navigation.OutOfPlayKind.Net && Math.Abs(ballZ) - halfLen > GoalDepth
            ? Navigation.OutOfPlayKind.GoalLine
            : kind;

    /// <summary>
    /// The tick-cue control category and zone for BOTH teams on one live read. The reference team and the
    /// opposition run identical, independent metronomes: each team's zone is which third the ball is in
    /// relative to THAT team's attacking direction — or the box it is attacking, which supersedes the third
    /// for a faster cue. Because a team is "in control" only when its nearest man is strictly nearer than the
    /// other's, at most one team's zone is non-off at any instant. A team not clearly on the ball has a
    /// <see cref="CueZone.Off"/> zone and a control that says WHY, so the SSE loop can grace a soft loss (a
    /// pass in flight) but cut a hard one at once:
    ///   - out of play, an aerial (uncontested) ball, or a loose ball (no player of that team within reach) is
    ///     a <see cref="CueControl.SoftLoss"/> — a pass by that team reads as one of these while it travels;
    ///   - the OTHER team on the ball is a <see cref="CueControl.HardLoss"/>;
    ///   - a genuine 50-50 is <see cref="CueControl.Contested"/> for BOTH teams — neither may claim a ball in
    ///     dispute, and the caller sustains whichever side went in holding it;
    ///   - no rendered player objects (between highlights) is <see cref="CueControl.None"/>.
    /// The <see cref="CueControl.InControl"/> branch is nearest, within OnBallRadius, not contested, on the
    /// ground; the other controls partition Off. Because being in control requires being STRICTLY nearer
    /// than the other team's man, at most one team can hold it — which is what keeps the two metronomes
    /// from ever running together.
    ///
    /// This is a per-sample geometric judgement only. It has no memory, so a hard loss here is a candidate
    /// turnover rather than a decided one — <see cref="Navigation.PossessionArbiter"/> is what confirms it
    /// over several samples before the cues move.
    /// </summary>
    private static (CueControl Control, CueZone Zone, CueControl OppControl, CueZone OppZone, bool Contested,
                    bool Air, float PressureOnRef, float PressureOnOpp, float NearestMan)
        ComputeCue(in Reading r, Navigation.OutOfPlayKind outKind)
    {
        // Out of play: not contested, and a soft loss for both teams so either grace can ride out a ball that
        // briefly leaves. Nobody is being pressed while the ball is dead, so the bed is silent (0, not NaN —
        // this is knowing there is no pressure, not failing to read it).
        if (outKind != Navigation.OutOfPlayKind.InPlay)
        {
#if FM27_DEVELOPER_TOOLS
            Dev.PossessionTrace.NoteGeometry(float.NaN, float.NaN, r.Ball.x, r.Ball.y, r.Ball.z,
                false, false, CueControl.SoftLoss, CueControl.SoftLoss, outOfPlay: true, noPlayers: false);
#endif
            // The zones stay live for a dead ball: the SSE layer is what silences the cues while the
            // ball is out, and the last-touch mode ticks the RESTARTING team's zone through the stop,
            // which needs to name where the restart is happening rather than Off.
            return (CueControl.SoftLoss, ZoneFor(r, r.AttackingUpfield),
                    CueControl.SoftLoss, ZoneFor(r, !r.AttackingUpfield), false, false, 0f, 0f, float.NaN);
        }

        // Player objects only exist while a highlight/replay renders; between highlights we cannot judge.
        // Pressure is genuinely unreadable there rather than absent, hence NaN: the client holds its last
        // level instead of dropping the bed out on every gap.
        if (!TryNearestPerTeam(r.Ball, r.PitchLength, r.PitchWidth, out float dHome, out float dAway,
                               out float pressHome, out float pressAway))
        {
#if FM27_DEVELOPER_TOOLS
            Dev.PossessionTrace.NoteGeometry(float.NaN, float.NaN, r.Ball.x, r.Ball.y, r.Ball.z,
                false, false, CueControl.None, CueControl.None, outOfPlay: false, noPlayers: true);
#endif
            return (CueControl.None, CueZone.Off, CueControl.None, CueZone.Off, false, false,
                    float.NaN, float.NaN, float.NaN);
        }
        float dRef = r.RefTeam == 0 ? dHome : dAway;
        float dOpp = r.RefTeam == 0 ? dAway : dHome;
        float pressureOnRef = (float)Navigation.CarrierPressure.Normalise(
            r.RefTeam == 0 ? pressHome : pressAway, PressureDivisor);
        float pressureOnOpp = (float)Navigation.CarrierPressure.Normalise(
            r.RefTeam == 0 ? pressAway : pressHome, PressureDivisor);

        // Contested / aerial are symmetric (they describe the ball, not a team), so both metronomes share them.
        bool contested = IsContested(dRef, dOpp);
        bool aerial = r.Ball.y > AerialHeight;

        // One metronome per team, each named in its own attacking frame. The opposition attacks the other way,
        // so its zone uses the negated direction; ClassifyBox/Classify then place the ball in the opposition's
        // own thirds and the box it is attacking (the reference team's goal).
        var (refControl, refZone) = CueForTeam(r, dRef, dOpp, r.AttackingUpfield, contested, aerial);
        var (oppControl, oppZone) = CueForTeam(r, dOpp, dRef, !r.AttackingUpfield, contested, aerial);
#if FM27_DEVELOPER_TOOLS
        Dev.PossessionTrace.NoteGeometry(dRef, dOpp, r.Ball.x, r.Ball.y, r.Ball.z, contested, aerial,
            refControl, oppControl, outOfPlay: false, noPlayers: false);
#endif
        // "In the air" mirrors the possession readout: a contested aerial is a 50-50 duel (the contested sound
        // owns it), so only an uncontested high ball reports Air.
        return (refControl, refZone, oppControl, oppZone, contested, aerial && !contested,
                pressureOnRef, pressureOnOpp, Math.Min(dRef, dOpp));
    }

    /// <summary>
    /// Whether the ball is in dispute between the two teams, given each side's nearest man's distance to it.
    /// Shared by the tick cue and the spoken possession readout so the sound and the speech can never
    /// disagree about what a 50-50 is.
    ///
    /// Two ways to qualify. A GENUINE DUEL is both men inside <see cref="ContestedRadius"/> — both physically
    /// on top of the ball, a tackle or an aerial challenge, however the distance splits between them. TOO
    /// CLOSE TO CALL is the NEARER man within reach (<see cref="OnBallRadius"/>) while the two are separated
    /// by less than <see cref="HandoverMargin"/>: someone is close enough to plausibly own the ball, but
    /// nobody is meaningfully nearer, so naming one of them the owner would invent a distinction the readings
    /// cannot support. The two tests are complementary — the first is about proximity to the ball, the second
    /// about the absence of a winner — and either alone leaves real gaps.
    ///
    /// The second case exists because deciding a near-tie by strict comparison made the cue thrash. Two
    /// players jockeying around a rolling ball trade the nearer position several times a second; each swap
    /// handed control over and, because a metronome starting from silence ticks immediately, produced a burst
    /// of alternating team ticks at the sampling rate rather than at either team's cadence. Every such
    /// crossover passes through a near-tie by definition, so treating that as disputed makes the whole episode
    /// one stable state: the side that went in holding the ball keeps ticking (the caller sustains it) and the
    /// contested clip explains why. A team that genuinely wins the ball emerges more than the margin nearer
    /// and takes the cue with no delay — unlike a dwell timer, this costs a real turnover nothing.
    ///
    /// It is the NEARER man that must be in reach, not both, and that is load-bearing rather than incidental:
    /// requiring both left the thrash alive at the edge of reach, where a crossover at ~2 m puts one man
    /// inside and one outside, failing the test at exactly the moment it is needed. Taking the minimum closes
    /// the gap, and a ball nobody is within reach of still falls through to loose rather than disputed.
    /// </summary>
    private static bool IsContested(float dA, float dB)
        => (dA <= ContestedRadius && dB <= ContestedRadius)
           || (Math.Min(dA, dB) <= OnBallRadius && Math.Abs(dA - dB) <= HandoverMargin);

    /// <summary>
    /// One team's tick-cue control and zone for the in-play part of a read (the out-of-play and no-player
    /// gates are handled by <see cref="ComputeCue"/>). <paramref name="dSelf"/> is this team's nearest man to
    /// the ball, <paramref name="dOther"/> the opponent's, and <paramref name="attackingUpfield"/> the
    /// direction this team attacks. Byte-identical to the old single-team gate, just parameterised so the
    /// reference and the opposition are computed the same way.
    /// </summary>
    private static (CueControl Control, CueZone Zone) CueForTeam(
        in Reading r, float dSelf, float dOther, bool attackingUpfield, bool contested, bool aerial)
    {
        // A genuine 50-50 is its own control, NOT a loss: a duel is not a turnover, and killing both
        // metronomes for it left the player with no sense of where the ball was for the whole challenge. The
        // caller holds the last clear holder's zone through the duel and the contested clip layers over it,
        // so the ticks say where the ball is while the clip says it is up for grabs. Checked first, and
        // symmetric for both teams, so neither side can claim control of a ball genuinely in dispute.
        // The zone is where the BALL is, in this team's attacking frame — a fact about the pitch, not about
        // who holds it — so it is computed for every control, not just for a team in possession. The cue is
        // driven by the arbiter's confirmed holder now, which has to tick a live zone through the loose-ball
        // spans between touches; returning Off for those made the holder's zone unreadable exactly when it
        // was needed. Nothing may infer possession from a non-Off zone any more: the SSE loop silences the
        // non-holder explicitly.
        var zone = ZoneFor(r, attackingUpfield);

        if (contested) return (CueControl.Contested, zone);

        // Hard loss: the other team nearer and within reach. This test is the EXACT COMPLEMENT of the
        // InControl gate below, and that is load-bearing rather than tidy: it is what guarantees at most one
        // team is ever InControl, which is in turn what guarantees at most one metronome runs. A team that
        // is InControl leaves its opponent HardLoss, which clears the opponent's grace and stops it
        // sustaining an old zone underneath.
        //
        // Do NOT make this threshold tighter than OnBallRadius. Trying that — a 1 m "take" radius against a
        // 2 m "keep" radius, meant as hysteresis against turnovers — broke the complement: in the band
        // between the two, the team NOT on the ball read SoftLoss instead of HardLoss, kept its grace, and
        // ticked underneath the team that had the ball. Both metronomes then ran, and as the two distances
        // traded places it sounded like possession alternating several times a second. The protection that
        // radius was reaching for belongs to PossessionArbiter's dwell, which is time-based and cannot
        // break this invariant; a hard loss here is still only a CANDIDATE turnover until the dwell
        // confirms it.
        bool otherInControl = dOther < dSelf && dOther <= OnBallRadius;
        if (otherInControl) return (CueControl.HardLoss, zone);

        // Soft loss: the ball in the air (uncontested) or loose (no player of this team within reach) — this
        // is how a pass by this team reads mid-flight, so the grace bridges it.
        if (aerial || dSelf > OnBallRadius) return (CueControl.SoftLoss, zone);

        // Otherwise this team is clearly on the ball, on the ground, uncontested.
        return (CueControl.InControl, zone);
    }

    /// <summary>
    /// Where the ball is in one team's attacking frame: the box it is attacking if it is in one (which
    /// supersedes the third, for a faster cue), otherwise the third. Independent of possession.
    /// </summary>
    private static CueZone ZoneFor(in Reading r, bool attackingUpfield)
    {
        var box = Navigation.BallLocation.ClassifyBox(r.Ball.x, r.Ball.z, attackingUpfield, r.PitchLength / 2f);
        if (box == Navigation.BoxZone.OppositionPenaltyArea || box == Navigation.BoxZone.OppositionSixYardBox)
            return CueZone.Box;

        return Navigation.BallLocation.Classify(r.Ball.z, attackingUpfield, r.PitchLength / 2f) switch
        {
            Navigation.BallThird.DefendingEnd => CueZone.Defensive,
            Navigation.BallThird.MiddleThird => CueZone.Middle,
            Navigation.BallThird.AttackingThird => CueZone.Attacking,
            _ => CueZone.Off,
        };
    }

    /// <summary>
    /// Nearest on-pitch player of each team (home=0, away=1) to the ball, in metres, or false when no
    /// rendered player objects exist at all (between highlights / fast-sim), where possession cannot be
    /// judged. On-pitch = a real Player entity (EntityKind 0, not referee/staff) within the pitch bounds
    /// (the bench sits off the touchline). A missing team leaves its distance at <see cref="float.MaxValue"/>.
    ///
    /// Also totals the carrier-pressure contributions in the same pass, since this walk already has every
    /// player's distance and team in hand and a second walk would double the per-sample cost for nothing.
    /// <paramref name="pressureOnHome"/> is the pressure the HOME team is under, so it accumulates the AWAY
    /// players' contributions, and vice versa — a team is pressed by its opponents.
    /// </summary>
    private static bool TryNearestPerTeam(Vector3 ball, float pitchLength, float pitchWidth,
                                          out float dHome, out float dAway,
                                          out float pressureOnHome, out float pressureOnAway)
    {
        dHome = float.MaxValue;
        dAway = float.MaxValue;
        pressureOnHome = 0f;
        pressureOnAway = 0f;

        var players = UnityEngine.Object.FindObjectsOfType<FM.Match.Player>();
        if (players == null || players.Length == 0) return false;

        var pType = players[0].GetType();
        _playerPosition ??= pType.GetProperty("Position", AllInstance);
        _playerTeamId ??= pType.GetProperty("TeamID", AllInstance);
        _playerKind ??= pType.GetProperty("EntityKind", AllInstance);
        if (_playerPosition == null || _playerTeamId == null || _playerKind == null)
        {
            NoteDegradeOnce("[MatchBallLocation] player property missing for cue "
                + $"(Position={_playerPosition != null} TeamID={_playerTeamId != null} EntityKind={_playerKind != null}).");
            return false;
        }

        float maxX = pitchWidth / 2f + OnPitchMargin;
        float maxZ = pitchLength / 2f + OnPitchMargin;
        for (int i = 0; i < players.Length; i++)
        {
            var p = players[i];
            if (Convert.ToInt32(_playerKind.GetValue(p)) != 0) continue;         // referee / staff
            var pos = (Vector3)_playerPosition.GetValue(p);
            if (Math.Abs(pos.x) > maxX || Math.Abs(pos.z) > maxZ) continue;      // bench / off the pitch
            float dx = pos.x - ball.x, dz = pos.z - ball.z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            int team = Convert.ToInt32(_playerTeamId.GetValue(p));
            float share = (float)Navigation.CarrierPressure.Contribution(d, PressureRadius, PressureBaseShare);
            if (team == 0) { if (d < dHome) dHome = d; pressureOnAway += share; }
            else if (team == 1) { if (d < dAway) dAway = d; pressureOnHome += share; }
        }
        return true;
    }

    /// <summary>
    /// Shared entry point: marshal to the main thread, read the live state through the gate, then let the
    /// caller project it to a spoken phrase. Returns an honest gap marker ("not in a match",
    /// "ball position &lt;unavailable&gt;") when the state cannot be read.
    /// </summary>
    private static string Describe(Func<Reading, string> project)
    {
        try
        {
            return MainThreadDispatcher.Invoke(() =>
            {
                var reading = ReadState(out string gap);
                return reading.HasValue ? project(reading.Value) : gap;
            });
        }
        catch (Exception ex)
        {
            // A timeout here means the main thread was busy rendering a highlight when the key was
            // pressed. Degrade rather than lie about where the ball is; the player can press again.
            NoteDegradeOnce("[MatchBallLocation] main-thread read failed: " + ex.Message);
            return "ball position " + Speech.Degrade.Unavailable;
        }
    }

    private static Reading? ReadState(out string gap)
    {
        gap = null;
        var match = CurrentMatch();
        var ctrl = CurrentController();
        if (match == null || ctrl == null)
        {
            // The companion only routes these keys here while the scoreboard is on the page, so this is
            // the narrow window where the match objects have gone. Honest: there is no match to read.
            gap = "not in a match";
            return null;
        }

        var mType = match.GetType();
        _matchPeriod ??= mType.GetProperty("MatchPeriod", AllInstance);
        _hasStarted ??= mType.GetProperty("HasStarted", AllInstance);
        _timeSlice ??= mType.GetProperty("CurrentTimeSlice", AllInstance);
        _userTeamId ??= mType.GetProperty("UserTeamId", AllInstance);
        string period = _matchPeriod.GetValue(match).ToString();
        bool hasStarted = (bool)_hasStarted.GetValue(match);
        if (!hasStarted || !IsBallInPlayPeriod(period))
        {
            // Pre-kickoff, a break (half time / extra-time breaks), penalties, or full time: no open-play
            // ball position to place.
            gap = "ball position " + Speech.Degrade.Unavailable;
            return null;
        }

        int slice = Convert.ToInt32(_timeSlice.GetValue(match));
        int userTeam = Convert.ToInt32(_userTeamId.GetValue(match));
        // Relative to the player's team, else the home team (id 0 in the game's Home/Away ordering).
        byte refTeam = (byte)(userTeam >= 0 ? userTeam : 0);

        var ctrlType = ctrl.GetType();
        _playbackIface ??= ctrlType.GetProperty("PlaybackInterface", AllInstance);
        _matchIface ??= ctrlType.GetProperty("MatchInterface", AllInstance);
        _matchHandle ??= ctrlType.GetProperty("MatchHandle", AllInstance);
        var playback = _playbackIface.GetValue(ctrl);
        var matchIface = _matchIface.GetValue(ctrl);
        var handle = _matchHandle.GetValue(ctrl);

        _getBallPosition ??= playback.GetType().GetMethod("GetBallPosition", AllInstance);
        _isKickingUpfield ??= matchIface.GetType().GetMethod("IsTeamKickingUpfield", AllInstance);
        if (_getBallPosition == null || _isKickingUpfield == null)
        {
            NoteDegradeOnce("[MatchBallLocation] engine interface method(s) not found "
                + $"(GetBallPosition={_getBallPosition != null} IsTeamKickingUpfield={_isKickingUpfield != null}).");
            gap = "ball position " + Speech.Degrade.Unavailable;
            return null;
        }

        var ball = (Vector3)_getBallPosition.Invoke(playback, new object[] { slice });
        bool attackingUpfield = (bool)_isKickingUpfield.Invoke(matchIface, new object[] { handle, slice, refTeam });
        ReadPitchDimensions(ctrlType, ctrl, out float pitchLength, out float pitchWidth);
        bool inReplay = ReadInReplay(mType, match);
        // The camera placement, taken from the HOME team (id 0) at KICK-OFF rather than from the current
        // period. Reading slice 0 asks the engine what it already knows instead of inferring the end-swap
        // from how many periods have elapsed: that inference assumed extra time reuses the first half's ends,
        // which the laws do not grant — a fresh toss decides them — and getting it wrong would mirror the
        // whole soundscape for the rest of the match. This asks once and cannot drift.
        bool screenRightIsPositiveZ = (bool)_isKickingUpfield.Invoke(matchIface, new object[] { handle, 0, (byte)HomeTeam });

        _lastDegradeReason = null;  // a clean read clears the dedup latch
        return new Reading(ball, attackingUpfield, pitchLength, pitchWidth, refTeam, inReplay, screenRightIsPositiveZ, slice);
    }

    // True while the viewer is showing a replay/rewind (MatchPlaybackMode Replay/ReplaySlowMo/
    // ReplayStopped/ReplaySuperSlowMo = 1/2/5/6), where the ball re-traces the play. The live first
    // pass (Highlight, BetweenHighlights, CommentaryHighlight) is not a replay, so real events still
    // fire. Degrades to "not a replay" if the property ever vanishes: at worst a replay echoes, which is
    // an annoyance, never a false event — so log once and carry on rather than muting real goals.
    private static bool ReadInReplay(Type mType, object match)
    {
        _playbackMode ??= mType.GetProperty("CurrentMatchPlaybackMode", AllInstance);
        if (_playbackMode == null)
        {
            NoteDegradeOnce("[MatchBallLocation] CurrentMatchPlaybackMode not found; replay echoes not suppressed.");
            return false;
        }
        int mode = Convert.ToInt32(_playbackMode.GetValue(match));
        return mode == 1 || mode == 2 || mode == 5 || mode == 6;
    }

    /// <summary>
    /// True while the viewer is fast-forwarding between highlights (MatchPlaybackMode
    /// BetweenHighlightsFast/BetweenHighlightsSlow = 0/7), where the game runs the SAME playback
    /// stream at high speed (~30x observed): the ball races end to end, the rendered players keep
    /// moving, and every skipped event still crosses the played window — so every cue would fire
    /// in an unfollowable jumble. The SSE loop mutes the cues on it and loops the fast-forwarding
    /// sound instead. False when there is no match or the mode is unreadable: a wrong false only
    /// leaves the cues audible (the status quo), where a wrong true would silence real play — so
    /// degrade toward not muting, logged once. Main thread only, called from inside the SSE
    /// tick's existing dispatch.
    /// </summary>
    internal static bool ReadFastForward()
    {
        var match = CurrentMatch();
        if (match == null) return false;
        _playbackMode ??= match.GetType().GetProperty("CurrentMatchPlaybackMode", AllInstance);
        if (_playbackMode == null)
        {
            NoteDegradeOnce("[MatchBallLocation] CurrentMatchPlaybackMode not found; fast-forward mute unavailable.");
            return false;
        }
        int mode = Convert.ToInt32(_playbackMode.GetValue(match));
        return mode == 0 || mode == 7;
    }

    /// <summary>
    /// The native active live/past highlight speed, read in the main-thread audio snapshot.
    /// </summary>
    internal static float ReadHighlightSpeed()
    {
        // Read on the Unity thread, in the same snapshot as playback mode.
        // This selects the real live/past preference, not a slider-index guess.
        var preferences = FM.Match.MatchPreferences.Instance;
        if (preferences == null)
        {
            NoteDegradeOnce("[MatchBallLocation] MatchPreferences unavailable; using inherited 4x skip threshold.");
            return 4;
        }
        return preferences.MatchSpeedDuringHighlights;
    }

    internal static int ReadMinimumSkippedSlices()
    {
        var match = CurrentMatch();
        if (match == null) return 0;
        var mode = match.GetLiveHighlightModeData();
        return mode == null ? 0 : checked(4 * mode.MinimumBetweenHighlightLengthSeconds);
    }

    /// <summary>
    /// The native fast-forward flag and playback slice for GameAudioMute's motion check.
    /// A paused between-highlight mode must not keep the crowd muted. Main thread only.
    /// </summary>
    internal static (bool FastForward, int Slice) ReadPlaybackMotion()
    {
        var match = CurrentMatch();
        if (match == null) return (false, -1);
        int slice;
        try
        {
            _timeSlice ??= match.GetType().GetProperty("CurrentTimeSlice", AllInstance);
            slice = Convert.ToInt32(_timeSlice.GetValue(match));
        }
        catch (Exception ex)
        {
            NoteDegradeOnce("[MatchBallLocation] CurrentTimeSlice read failed for the game-audio mute: " + ex.Message);
            return (false, -1);
        }
        return (ReadFastForward(), slice);
    }

    // Live per-stadium pitch size from ctrl.Setup.Stadium.pitchSetupInfo. Degrades to the standard
    // dimensions (not a wrong split) if the struct path ever breaks; the thirds/flank stay sensible.
    private static void ReadPitchDimensions(Type ctrlType, object ctrl, out float length, out float width)
    {
        try
        {
            var setup = ctrlType.GetProperty("Setup", AllInstance).GetValue(ctrl);
            var stadium = setup.GetType().GetProperty("Stadium", AllInstance).GetValue(setup);
            var psi = stadium.GetType().GetField("pitchSetupInfo", AllInstance).GetValue(stadium);
            length = (float)psi.GetType().GetField("pitchLength", AllInstance).GetValue(psi);
            width = (float)psi.GetType().GetField("pitchWidth", AllInstance).GetValue(psi);
            if (length > 0f && width > 0f) return;
        }
        catch (Exception ex)
        {
            NoteDegradeOnce("[MatchBallLocation] pitch dimension read failed, using standard 105x68: " + ex.Message);
        }
        length = DefaultPitchLength;
        width = DefaultPitchWidth;
    }

    // True once the ball's centre has crossed a boundary line: out of play (or in the net). Needs no
    // attacking direction, so possession can use it without reading the interface.
    private static bool IsBallOutOfBounds(Vector3 ball, float pitchLength, float pitchWidth)
        => Math.Abs(ball.x) > pitchWidth / 2f + OutMargin || Math.Abs(ball.z) > pitchLength / 2f + OutMargin;

    /// <summary>
    /// The spoken location for a ball that has left the field, or null if it is still in play. The engine
    /// event stream names the restart the ball went out for where geometry cannot: a touchline exit is a
    /// "&lt;your/opposition&gt; throw-in, &lt;third&gt; &lt;side&gt;" (plain "throw-in" if the taking team is
    /// not yet known); a goal-line exit is a "&lt;your/opposition&gt; corner" or "&lt;your/opposition&gt; goal
    /// kick" with the side across the goal, falling back to the geometry-only "out of play, &lt;side&gt; of
    /// &lt;opposition/your&gt; goal" when no matching restart event is available; a ball in the net is "goal,
    /// &lt;opposition/your&gt; net". The restart is re-read live and only used when it agrees with the line
    /// geometry (via <see cref="Navigation.RestartReadout.Label"/>), so a stale event cannot mislabel.
    /// </summary>
    private static string OutOfPlayLocation(Vector3 ball, bool attackingUpfield, float pitchLength, float pitchWidth)
    {
        float halfLen = pitchLength / 2f, halfWid = pitchWidth / 2f;
        // The audio cues and this spoken readout share one out-of-play classifier so they can never
        // disagree about which line the ball crossed (or whether it is a goal).
        var kind = DowngradeDeepNet(
            Navigation.BallLocation.ClassifyOutOfPlay(
                ball.x, ball.z, ball.y, attackingUpfield, halfLen, halfWid, OutMargin, GoalHalfWidth, CrossbarHeight),
            ball.z, halfLen);

        // Live restart name (throw-in / corner / goal kick, by team), cross-checked against the line above.
        string restart = TryLatestRestart(out var rKind, out bool rForRef)
            ? Navigation.RestartReadout.Label(kind, rKind, rForRef)
            : Navigation.RestartReadout.Label(kind, Navigation.RestartKind.None, false);

        switch (kind)
        {
            case Navigation.OutOfPlayKind.Touchline:
            {
                var third = Navigation.BallLocation.Classify(ball.z, attackingUpfield, halfLen);
                var side = Navigation.BallLocation.ClassifyFlank(ball.x, attackingUpfield, halfWid);
                // Label always names a touchline exit (at least "throw-in").
                return restart + ", " + Navigation.BallLocation.Speak(third) + " " + Navigation.BallLocation.Speak(side);
            }
            case Navigation.OutOfPlayKind.GoalLine:
            case Navigation.OutOfPlayKind.Net:
            {
                bool oppositionEnd = (attackingUpfield ? ball.z : -ball.z) > 0f;
                string goal = oppositionEnd ? "opposition" : "your";
                if (kind == Navigation.OutOfPlayKind.Net) return "goal, " + goal + " net";
                // Reuse the flank classifier with a virtual width so its centre band is exactly the goal
                // mouth (+/- the goal half-width): Centre => between the posts, Left/Right => wide of it.
                var side = Navigation.BallLocation.ClassifyFlank(ball.x, attackingUpfield, 3f * GoalHalfWidth);
                if (restart != null)
                {
                    // The event named the restart (corner / goal kick + team); lead with it plus the side.
                    return side == Navigation.Flank.Centre ? restart : restart + ", " + Navigation.BallLocation.Speak(side);
                }
                if (side == Navigation.Flank.Centre) return "out of play, over the " + goal + " goal";
                return "out of play, " + Navigation.BallLocation.Speak(side) + " of " + goal + " goal";
            }
            default:
                return null;
        }
    }

    // The periods where the ball is live on the pitch. Kick-off waits, breaks, the penalty shoot-out,
    // and full time have no open-play position worth placing.
    private static bool IsBallInPlayPeriod(string period)
        => period == "FirstHalf" || period == "SecondHalf"
        || period == "ExtraTimeFirstHalf" || period == "ExtraTimeSecondHalf";
}
