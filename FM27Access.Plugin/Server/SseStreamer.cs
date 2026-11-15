using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Owns the Server-Sent Events connection (/events) and the post-navigation settle window.
/// One dedicated background thread per connection (<see cref="Stream"/>) pushes screen /
/// restructure / values / commentary / score events to the companion as the live game changes.
/// <para>
/// Split out of <see cref="AccessibilityServer"/>: that class owns the socket lifecycle, HTTP
/// parsing/routing, and page composition; this class owns everything that feeds the SSE stream.
/// The two share no state across the boundary except the server's running flag, which is passed
/// into <see cref="Stream"/> as a live probe so a Stop()/reload ends in-flight streamers on their
/// next tick. The settle-window fields below are read by the SSE thread and written by the request
/// thread (via <see cref="ArmSettleAfterAction"/>); both now live here, so the cross-thread sharing
/// is local to this class.
/// </para>
/// </summary>
internal static class SseStreamer
{
    // EventSource does not dispatch an SSE event whose accumulated data buffer is empty. Send an
    // impossible commentary value when the match stream ends so the client can clear the final
    // play-by-play sentence without announcing the protocol marker itself.
    private const string CommentaryClearToken = "__fm_commentary_clear__";

    // Settle window: how long after a navigation to watch for late-arriving panel content, and how
    // many consecutive unchanged ticks (at the ~500ms poll) end the window early once the screen
    // has settled. See docs/archive/plans/settle-window-plan.md.
    private const int SettleWindowMs = 3000;
    private const int SettleStableTicks = 2;

    // Marker retry: how long after a marker-bearing companion compose to re-read the screen,
    // and how many consecutive marker-bearing composes may retry before the screen is accepted
    // as permanently carrying the marker (an unlabeled control, an unloaded table — see the emit
    // site in the event loop). The delay grows with each attempt (700ms, 1.4s, 2.1s): a busy main
    // thread (world creation, screen build) can keep the game mid-build past the first retry, and
    // three fixed quick retries would all land on the same half-built state.
    private const int MarkerRetryDelayMs = 700;
    private const int MarkerRetryMax = 3;

    // Step 4 (in-panel action follow-up): an action that mutates the current panel without
    // navigating (toggle, tab switch, radio/checkbox, dropdown commit, ...) does not change the
    // screen token, so nothing re-reads the panel as the game applies the change over later
    // frames. After such an action we arm the settle window so the change is detected and morphed
    // in. The baseline shape is captured at action time (pre-mutation) so the window reliably sees
    // the async follow-up mutation rather than racing it. See docs/archive/plans/refresh-step-4-plan.md.
    // Who holds the ball, and the engine-claim gate that can override it. Shared by every SSE stream rather
    // than built per connection: see the comment at the use site in the loop.
    private static readonly PossessionArbiter SharedPossession = new();
    private static readonly object PossessionGate = new();
    private static TurnoverClaim _turnoverClaim;

    /// <summary>The shared claim gate's played high-water, read under the lock that guards it.</summary>
    private static int TurnoverHighWater()
    {
        lock (PossessionGate) return _turnoverClaim.HighWater;
    }

    private static volatile int _actionEpoch;
    private static volatile string _actionBaselineShape;

    // Sim arrival: the arrival report signals a screen-changing simulation so the companion
    // lands focus on the new page's content heading. The token names the arrival page — the
    // client only focuses when its rendered token matches, so a late event can never yank
    // focus on an unrelated screen. Written on the main thread, read by the SSE threads.
    private static volatile int _simArriveEpoch;
    private static volatile string _simArriveToken;

    public static void NotifySimArrival(string token)
    {
        _simArriveToken = token;
        _simArriveEpoch++;
    }

    // The structural shape the last companion-served /body reflected (noted by BuildBody for
    // /body?c=1 — the companion's own refresh, never a dev-driver read). A settle-window tick whose
    // current shape equals it has nothing to deliver: the client's page already renders exactly this
    // structure, so the tick adopts the baseline silently instead of emitting a redundant restructure
    // (the post-action double refresh). One immutable pair behind a single volatile reference so a
    // reader never sees a fresh timestamp with a stale shape; the TTL bounds how long an old note can
    // suppress, so a stale note can never mask a genuine change indefinitely.
    private const long ComposedShapeTtlMs = 15000;
    private static volatile ComposedShape _composed;

    public static void NoteComposedShape(string shape, bool hasMarker)
    {
        if (shape != null) _composed = new ComposedShape(shape, Environment.TickCount64, hasMarker);
    }

    // Count of companion /body composes currently running (BuildBody brackets them). The settle tick
    // races the client's own refresh: a tick can see the changed shape a few hundred ms before the
    // compose finishes and notes it, and would emit a restructure that only re-delivers what the
    // in-flight fetch is already carrying. While one is in flight the tick defers the decision one
    // tick (bounded — see the emit site), by which time the note has landed and a genuinely stale
    // fetch still gets its restructure on the next tick.
    private static int _companionComposesInFlight;
    public static void CompanionComposeBegin() => Interlocked.Increment(ref _companionComposesInFlight);
    public static void CompanionComposeEnd() => Interlocked.Decrement(ref _companionComposesInFlight);
    private static bool CompanionComposeInFlight => Volatile.Read(ref _companionComposesInFlight) > 0;

    // A per-SSE-connection id so server.log shows each connection's lifetime and why it ended:
    // healthy is one long-lived connection; a run of short-lived ones is churn worth chasing.
    private static int _sseConnCounter;

    // True when the render set holds a panel on a loading-screen layer (FM26 hosts genuine interactive
    // full-screens there — the career-start "Begin Career"/news screen). Used to keep the settle window
    // open for such a panel's whole lifetime, since its content populates well past the 3s window.
    private static bool SetHasLoadingScreen(IReadOnlyList<InteractivePanels.PanelView> set)
    {
        for (int i = 0; i < set.Count; i++)
            if (set[i].LayerIndex == InteractivePanelSet.LayerLoadingScreen
                || set[i].LayerIndex == InteractivePanelSet.LayerProfanityLoadingScreen)
                return true;
        return false;
    }

    // True when the render set holds a panel that rewrites its own content with no navigation and no
    // user action behind it, so nothing would ever arm the settle window: the live penalty shootout
    // card grows a result icon per kick while the player sits still and watches. FM27's live
    // overview also mounts dugout advice and changes its native chart series during play.
    // The native save browser also enumerates local/cloud data after its shell opens;
    // cold bindings can arrive after the normal three-second navigation window.
    // Its existing text-readiness/visibility shape must remain observed so the
    // source selector, available space and empty/list state do not need F5.
    // Medical risk tabs likewise bind their empty/results branch after the
    // selected-tab state settles. Keep observing the actual foreground so an
    // official zero-player explanation cannot remain absent until F5.
    // These are treated like a loading screen: shape stays watched for the panel's
    // whole lifetime, so each asynchronous native change lands as
    // a restructure instead of waiting for the player to press F5.
    private static bool SetHasSelfUpdatingPanel(IReadOnlyList<InteractivePanels.PanelView> set)
    {
        for (int i = 0; i < set.Count; i++)
            if (set[i].Panel is VisualElement ve
                && ve.name is PenaltyShootoutReader.PanelName or "OverviewPrototype" or "LoadGameBrowserFullscreen" or "MedicalCentreRiskAssessmentReport")
                return true;
        return false;
    }

    // Read the current aggregate panel shape for use as a settle baseline. Called BEFORE the
    // mutating action handler runs, so the baseline is guaranteed pre-mutation — a synchronous
    // mutation (the Reset button's revert applies its enabled flips before the response is even
    // written) must still diff against it, or the window sees "no change" and the companion goes
    // stale until F5. Best-effort: a shape-read failure returns null (the loop falls back to
    // capture-on-first-tick) and never crashes the action.
    public static string CaptureShapeBaseline()
    {
        try
        {
            return MainThreadDispatcher.Invoke(() =>
            {
                var set = InteractivePanels.CurrentOrNull();
                return set != null ? ScreenWalker.ShapeSignature(set) : null;
            });
        }
        // Runs on the background request thread: Log.* routes to UnityEngine.Debug, which hard-crashes
        // off the main thread in IL2CPP. Route to the background-safe file log instead.
        catch (Exception ex)
        {
            ServerText.LogBackground($"Arm-settle shape read failed: {ex.Message}");
            return null;
        }
    }

    // How many consecutive full ticks (~500ms each) a tracked control may read as the unlabeled
    // degrade marker before the values channel pushes it as that control's text. ~2s of grace,
    // enough to cover a panel rebuilding its controls in place. See Navigation.MarkerHold.
    private const int MarkerHoldTicks = 4;

    // Arm a settle window against the pre-mutation baseline captured by CaptureShapeBaseline,
    // so the mutation's follow-up (sync or async) is detected and morphed in.
    public static void ArmSettleAfterAction(string baselineShape)
    {
        _actionBaselineShape = baselineShape;
        _actionEpoch++;
    }

    // --- SSE stream (dedicated thread per connection) ---

    // Per ~0.5s tick, pushes whichever SSE events apply: "screen" on a token change (navigation
    // or step change), "restructure" when the panel's shape changes during the post-navigation
    // settle window (late content), "values" for changed live text, plus "commentary"/"score" for
    // a live match. The client morphs the page in place on screen/restructure. A comment heartbeat
    // every otherwise-idle tick lets a dead connection be detected by the write failing.
    // isRunning is the server's live running probe: when Stop()/reload clears it, the loop ends on
    // its next tick (matching the old in-class `while (_running)`).
    public static void Stream(TcpClient client, Func<bool> isRunning)
    {
        int connId = Interlocked.Increment(ref _sseConnCounter);
        long connStart = Environment.TickCount64;
        ServerText.LogBackground($"SSE connect #{connId}");
        try
        {
            var stream = client.GetStream();
            var header = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\n"
                       + "Cache-Control: no-cache\r\nConnection: keep-alive\r\n\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            WriteSse(stream, "retry: 1000\n\n");

            string lastToken = null;
            // Whether the surface could be read on the previous tick, so its return can force a
            // re-read (see the check in the loop).
            bool lastServable = true;
            string lastCommentary = null;
            string lastScore = null;
            // Match-header availability fingerprint (pending-changes toast up/down, shouts
            // dropdown enabled/disabled). These flips change no live text and happen minutes
            // after the last user action, so neither the values channel nor the settle window
            // sees them — the page stayed stale until a manual refresh. A change emits a
            // restructure; the first in-match observation only baselines (no refresh on entry).
            string lastHeaderState = null;
            string lastCue = null;
            string lastOppCue = null;
            // Contested ball: a sustained looping sound while both teams contest the ball, pushed on-change on
            // its own channel like the cue metronomes. "off" so a quiet start (or a reconnect out of a contest)
            // does not push a spurious stop; a reconnect mid-contest pushes "on" and the client starts the loop.
            string lastContested = "off";
            // In-the-air: one long clip (not a loop) while the ball is high and uncontested, pushed
            // on-change on its own channel like contested. The client starts the clip from the beginning
            // on "on" and stops it on "off".
            string lastAir = "off";
            // Fast-forwarding between highlights: the game runs the same playback stream at ~30x
            // (MatchPlaybackMode BetweenHighlightsFast/Slow), so the ticks would gabble and every
            // skipped event would fire its one-shot. While active, all cue channels are forced off,
            // the one-shot emissions are suppressed (their arbiters keep observing so the high-waters
            // advance past the skipped events — nothing retro-fires when the highlight starts), and
            // the 'ff' channel loops the stereo fast-forwarding sound client-side. Null start so
            // the FIRST tick always pushes the current state — the client's master cue gate
            // follows this channel, and a reconnect that missed an "off" would otherwise leave the
            // page silenced for good. lastFfActive is the keep-last for a dispatch timeout, like
            // the other channels.
            string lastFf = null;
            bool lastFfActive = false;
            // Touch-mode overlay mutes, persistent across ticks. goalQuiet latches at the goal
            // one-shot (ball dead in the net, celebration — the flickering in/out-of-play reads
            // there strobed the box tick) and releases on the engine's put-in-play event, the
            // kick-off. A replay suspends it: the sampler scans a replay into its own claim, so the
            // ticks there describe the move being re-shown rather than the celebration the latch is
            // about. lastNearMs feeds the far-loose mute: refreshed whenever anyone is within
            // reach of the ball (or the distance is unmeasurable, which is not knowledge that the
            // ball is alone).
            bool goalQuiet = false;
            // The engine time of the latching goal, and how the latch survives fast-forward: a
            // simulated span can gulp the goal AND its kick-off into one tick, so release is decided
            // by comparing slices (a put-in-play at or after the goal's slice), never by which branch
            // ran first. goalQuietLiveSinceMs is the missed-event backstop clock: play running
            // continuously (in play, readable, a fresh holder claimed) for a sustained stretch proves
            // the kick-off happened even if its event never crossed — while the net-line flicker that
            // motivated the latch can only produce isolated in-play samples, never a sustained run.
            int goalQuietSlice = 0;
            long goalQuietLiveSinceMs = 0;
            long lastNearMs = Environment.TickCount64;
            // Replay state for the start/end marker cues: null until the first live reading
            // (baseline — connecting mid-replay plays nothing), reset to null through dead
            // periods (a replay never spans a break, so a stale "replay over" can never fire
            // after half time).
            bool? lastInReplay = null;
            // Ball stereo pan along the pitch length (-1 full left .. +1 full right), pushed on-change on
            // its own channel; the client applies it to every match sound through one shared panner. NaN =
            // nothing sent yet, so the first readable pan always reaches a fresh (or reconnected) client.
            float lastPan = float.NaN;
            // Metres from the ball to the goal the possessing team attacks (the Alt+7 straight-line
            // measure), pushed on-change while a cue is active; the client accelerates the running tick
            // as this closes on goal. NaN = nothing sent yet.
            float lastDist = float.NaN;
            float lastDrone = float.NaN;
            float lastAirLevel = float.NaN;
            // Carrier pressure: how heavily the confirmed holder is being closed down, 0..1, pushed on
            // change like the drone. One channel serves both teams — it always describes whoever has the
            // ball, and the metronome already says which team that is, so "you are penned in" and "your
            // press is working" need no second sound to tell apart. NaN = nothing sent yet.
            float lastPressure = float.NaN;
            // Pause mute: the playback time slice of the previous audio read. An unmoved slice while in
            // a play period means playback is halted (the pause button, a stopped replay) with the action
            // frozen on screen — every cue channel is forced off until the slice moves again. Dead
            // periods (pre-kickoff, half time, full time to extra time, penalties) are already silenced
            // upstream by ReadState's in-play gate.
            int lastAudioSlice = int.MinValue;
            // How long an opponent must hold the ball before the cue changes hands. Two samples at the
            // SubTickMs cadence below. A pass rolling past a defender occupies one, so it is rejected
            // outright; a genuine interception persists and confirms, paying only this in latency.
            //
            // Cut 375 -> 250 with the radius drop: three consecutive samples inside 2 m was easy, but inside
            // a contact-sized one it demanded the taker keep the ball at his feet for the whole window, which a player
            // knocking it ahead as he wins it does not do — so real turnovers risked never confirming and the
            // opposition's metronome staying silent. Two samples still rejects a ball merely rolling past.
            const long TurnoverDwellMs = 250;
            // The far-loose mute (touch mode only): nobody of either team within this of the ball for
            // this long silences the tick without moving the holder. Distance chosen well past every
            // judgement radius in the chain (on-ball 0.6, contested 1.2, attribution 2.5) so it can
            // only fire on a ball genuinely in no-man's-land; duration by ear (user-set 1.8s) so a
            // switch of play does not blink the tick out mid-flight.
            const float FarLooseDistance = 5f;
            const long FarLooseMuteMs = 1800;
            // Possession is a property of the MATCH, not of a client connection, so the arbiter and the
            // engine-claim gate are shared and survive a stream reconnect. Per-connection instances lost the
            // holder outright whenever the companion's SSE stream dropped and re-opened: a trace caught one
            // mid-play, the claim vanishing with live controls either side of it and the ticks going quiet
            // until somebody touched the ball again. Nothing about a socket closing changes who has the ball.
            //
            // Guarded because a second viewer would run a second loop over the same state; the work inside is
            // a handful of field writes, so the lock is never contended for meaningfully. Sharing is also the
            // more correct behaviour with two clients attached: they hear one agreed possession rather than
            // two independently-drifting ones.
            var possession = SharedPossession;
            // FM27 also offers 10x highlights. Read the actual preference in
            // the same native snapshot instead of mistaking that rate for a skip.
            long lastAudioSliceAtMs = 0;
            // Two cadences in one loop: the AUDIO half (cue zones, one-shot scans, the
            // fast-forward flag) runs every sub-tick so the fast-forward mute reaches the client
            // before a short skip is over — the game's minimum skip is ~4 game seconds, ~0.13s of
            // wall time at 30x, and at the old 500ms cadence the ticks sailed straight through it.
            // The SCREEN half (token/shape walk, values, commentary, score, settle window) stays
            // on every fourth sub-tick: it is the expensive part and none of it needs sub-second
            // latency.
            const int SubTickMs = 125;
            const int SubTicksPerFullTick = 4;
            // Pause detection is time-based, not consecutive-equal-reads: at the sub-tick cadence
            // a slow playback speed legitimately leaves the slice unchanged across neighbouring
            // reads (~3 slices/s at 0.75x), so "frozen" now means no slice movement for this long.
            const long FrozenAfterMs = 600;
            long lastSliceChangeMs = 0;
            int subTick = 0;
            // One-shot out-of-play match sounds (the 'sfx' channel). The pure arbiter fires solely on the
            // engine's restart event stream (authoritative throw-in / corner / goal-kick, read by
            // ScanRestarts), baselining/latching so each dead ball sounds exactly once per pass — live, and again
            // when a replay re-shows it;
            // geometry only arms the per-episode latch when the ball returns to play. lastGeomKind holds the
            // last geometry classification fed to the arbiter so a main-thread timeout tick reuses it (no
            // spurious edge), matching the goal scan's keep-last-good.
            var eventWindow = new MatchEventWindow();
            var lastGeomKind = OutOfPlayKind.InPlay;
            // "Put in play" ping: fires when the engine shows the ball put back into play (kick-off, or a
            // throw-in / goal kick / corner / free kick being taken; a penalty sounds as a shot attempt
            // instead, so one kick is not two sounds). It shares the restart scan,
            // whose span is driven by eventWindow.Out.EventHighWater; this arbiter's own high-water advances in
            // lockstep (same inputs, same call site) and it owns the baseline, the replay pass, and
            // same-take dedupe. See Navigation.PutInPlayOneShot.

            // Goal one-shots from the engine event stream (position-free). The pure state machine holds the
            // played high-water event index and baselines/latches so a goal (EventGoalScored or
            // EventIllegalGoalScored) reaching playback fires exactly once, and once more with its replay.
            // See Navigation.GoalOneShot.

            // Good-defence one-shots from the engine event stream: a successful / impactful defensive action
            // (interception, tackle won, save, block, ball won back) sounds once as the highlight reaches it,
            // attributed by the event's Team so the player hears whether their team ("gooddefence") or the
            // opposition ("oppgooddefence") made the play. Baselined, replay-tracked, and same-moment-deduped by
            // Navigation.DefenceOneShot; it replaces the old geometry possession-flip sound, which fired on
            // non-events. See MatchBallLocation.ScanDefence.

            // Shot-attempt one-shots from the engine event stream: an attempt on goal (shot, header at goal,
            // volley, struck free kick, penalty taken) sounds once as the highlight reaches the STRIKE, before
            // its outcome is narrated, so the player waits on the goal / save / restart sound to learn whether
            // it went in. Both teams sound alike (the metronome already says who has the ball). Baselined,
            // replay-tracked, and tightly deduped by Navigation.ShotOneShot so a rebound sounds twice. See
            // MatchBallLocation.ScanShots.
            // Kicked and headed attempts arbitrate independently so a shot blocked and headed back in moments
            // later sounds twice; the two clips are distinct, so hearing both cannot be mistaken for one
            // strike double-triggering. Both are fed from the one ScanShots call and the same played
            // high-water, so their baselines and replay passes advance in lockstep (the same arrangement
            // eventWindow.Out and eventWindow.PutInPlay share).


            // Commentary gap filler: the possession line (minus its team clause) inserted into the
            // live region while the game's play-by-play is quiet. The pure gate owns the quiet clock,
            // spacing, and no-repeat rules; the polite live region guarantees an insertion can never cut
            // off speech in progress. See Navigation.CommentaryFillerGate.
            var fillerGate = new Navigation.CommentaryFillerGate();
            var lastSnapshot = new Dictionary<string, string>();
            // Settle window: for a short time after each navigation, watch the panel's structural
            // shape and re-read when it changes, so content that populates after the panel became
            // "ready" appears without a manual refresh. Bounded in time (and ended early once the
            // shape is stable) so a settled screen is never watched and ambient churn cannot
            // trigger it. settleDeadline == 0 means not currently watching.
            long settleDeadline = 0;
            string settleSig = null;
            // Marker retry state: which marker-bearing compose was already retried, and how many
            // consecutive marker-bearing composes have retried (refunded by any clean compose).
            long lastMarkerComposeAtMs = 0;
            int markerRetries = 0;
            // Marker hold state: per connection, so no cross-thread sharing.
            var markerHold = new MarkerHold(Adapters.ButtonAdapter.UnlabeledIconButton, MarkerHoldTicks);
            int stableTicks = 0;
            // Step 4: when an in-panel action arms the window, the first in-window tick re-reads
            // unconditionally (catching even a pure attribute/disabled change that the structural
            // shape hash would miss), then the window shape-gates as usual.
            int seenEpoch = _actionEpoch;
            int seenSimArrive = _simArriveEpoch;
            bool settleForced = false;
            // The next sub-tick's scheduled instant; see the sleep at the foot of the loop.
            long nextTickAt = Environment.TickCount64;
            while (isRunning())
            {
                long now = Environment.TickCount64;
                bool fullTick = subTick == 0;
                subTick = (subTick + 1) % SubTicksPerFullTick;
                bool inWindow = settleDeadline != 0 && now < settleDeadline;
                // Whether the filler clocks would allow an insertion this tick, checked up front so the
                // lambda only pays for composing a candidate (a full player scan) when one could emit.
                bool wantFiller = fullTick && fillerGate.WantsCandidate(now);

                // All live channels (token, values, shape, commentary, score) are read in a single
                // main-thread dispatch instead of two back-to-back Invokes, saving a full frame of
                // serialized wait and one event allocation per tick (and halving the worst-case stall
                // on a hung main thread, since each Invoke has its own 2s timeout). Fault isolation is
                // preserved by wrapping each sub-read in its own try/catch INSIDE the lambda: the token
                // must survive a value/shape failure (discarding it silently blinds screen-change
                // detection until a manual refresh), and the match read must survive a screen failure
                // and vice-versa. The sub-reads are internally guarded, so the outer catch is now
                // catastrophic-only (a dispatch timeout) and keeps last-good for every channel.
                string token, commentary = lastCommentary, score = lastScore, cue = lastCue, oppCue = lastOppCue;
                string headerState = lastHeaderState;
                IntPtr nativeEventMatch = eventWindow.Match;
                Dictionary<string, string> snapshot = lastSnapshot;
                long valuesReadAt = 0;
                string shape = null;
                var outKind = lastGeomKind;
                bool inReplay = false;
                var cueControl = CueControl.None;
                var oppControl = CueControl.None;
                bool contested = false;
                bool air = false;
                float pan = float.NaN;
                float distRef = float.NaN;
                float distOpp = float.NaN;
                float drone = 0f;
                float airLevel = float.NaN;
                float pressRef = float.NaN;
                float pressOpp = float.NaN;
                float nearestMan = float.NaN;
                int audioSlice = -1;
                long audioSampleAtMs = now;
                bool ff = lastFfActive;
                float highlightSpeed = 4;
                int minimumSkippedSlices = 0;
                string filler = null;
                MatchBallLocation.GoalScan goal = default;
                MatchBallLocation.RestartScan restart = default;
                MatchBallLocation.DefenceScan defence = default;
                MatchBallLocation.ShotScan shot = default;
                MatchBallLocation.TurnoverScan turnover = default;
                // True while the render set holds a loading-screen-layer panel. Such a panel fills its
                // real content asynchronously over a long, variable window (world generation to the
                // career-start news article) that far outlasts the 3s settle window and never moves the
                // screen token (same panel, no stepper), so the settle logic below keeps watching its
                // shape for the panel's whole lifetime rather than closing after 3s. Also set for a
                // self-updating panel (SetHasSelfUpdatingPanel), which needs the same open-ended watch
                // for the same reason: its content changes with no navigation and no action behind it.
                bool loadingActive = false;
                // Skipped entirely unless the hand-off is being drained: while the core is stopped or
                // stalled, every Invoke here would just wait out its budget once per tick. Coming back
                // forces a re-read — the screen beneath is very likely unchanged, so without clearing
                // lastToken there would be no 'screen' event, and the companion would sit on the
                // "not responding" page indefinitely after the surface had recovered.
                bool servable = SurfaceHealth.CanServe(MainThreadDispatcher.Health());
                if (servable && !lastServable)
                {
                    lastToken = null;
                    ServerText.LogBackground("[SSE] surface live again, forcing a re-read");
                }
                lastServable = servable;
                // Same reasoning for a render that failed closed on a transient panel-set read: the
                // player was left on an "unavailable" page while the screen beneath it very likely did
                // not change, so nothing would move the token to refresh them off it.
                if (InteractivePanels.ConsumeRecovered())
                {
                    lastToken = null;
                    ServerText.LogBackground("[SSE] panel-set query recovered, forcing a re-read");
                }
                if (servable)
                {
                    try
                    {
                        (token, snapshot, shape, commentary, score, cue, cueControl, oppCue, oppControl, outKind, inReplay, contested, air, pan, distRef, distOpp, drone, airLevel, pressRef, pressOpp, nearestMan, audioSlice, ff, filler, goal, restart, defence, shot, turnover, loadingActive, headerState, nativeEventMatch, highlightSpeed, audioSampleAtMs, minimumSkippedSlices) = MainThreadDispatcher.Invoke(() =>
                        {
                            // Playback advances on Unity frames. Their unscaled clock is
                            // fixed at frame start; callback wall time also includes queued
                            // work and does not measure the observed native frame interval.
                            long sampleAtMs = (long)(UnityEngine.Time.unscaledTimeAsDouble * 1000);
                            // --- screen / values / shape (best-effort, isolated). Full ticks only:
                            // this is the expensive walk, and the sub-ticks exist for audio latency.
                            // A sub-tick keeps every screen channel at last-good, so the emission
                            // half below naturally has nothing to send for them. ---
                            string t = lastToken;
                            Dictionary<string, string> snap = lastSnapshot;
                            string shp = null;
                            bool loading = false;
                            if (fullTick)
                            {
                                try
                                {
                                    // Watch the aggregate over the whole interactive set, not the single
                                    // speech active panel (Phase 3). CurrentOrNull is best-effort: a transient
                                    // set-read failure returns a null token (treated as "no change" by the
                                    // guard below) rather than killing the game — the crashing gate still runs
                                    // on the /body render a real change triggers.
                                    var set = InteractivePanels.CurrentOrNull();
                                    snap = LiveValues.Snapshot();
                                    valuesReadAt = NativeValueRevision.Next();
                                    if (set == null) t = null;
                                    else
                                    {
                                        t = ServerText.ScreenToken(set);
                                        loading = SetHasLoadingScreen(set) || SetHasSelfUpdatingPanel(set);
                                        // Pay for the shape walk while watching for settle, or for the whole
                                        // time a loading screen is up (its content builds long after the 3s
                                        // window would have closed).
                                        if (inWindow || loading)
                                        {
                                            try { shp = ScreenWalker.ShapeSignature(set); }
                                            catch (Exception ex) { ServerText.LogBackground($"ShapeSignature failed, skipping restructure this tick: {ex.Message}"); }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    t = lastToken; snap = lastSnapshot;
                                    ServerText.LogBackground($"Screen/value read failed; skipping this tick: {ex.Message}");
                                }
                            }

                            // --- match commentary/score (isolated from the screen read; full ticks only,
                            // like the screen). MatchWatcher degrades an unreadable live commentary/score
                            // to an explicit "unavailable" marker (a muted stream would read as "nothing
                            // is happening"), so TryRead does not throw on that path; this catch only
                            // handles unexpected failures, keeping the last good values rather than
                            // tearing down. ---
                            string c = lastCommentary, s = lastScore;
                            string hdr = lastHeaderState;
                            if (fullTick)
                            {
                                try { MatchWatcher.TryRead(out c, out s); }
                                catch (Exception ex)
                                {
                                    c = lastCommentary; s = lastScore;
                                    ServerText.LogBackground($"Match read failed: {ex.Message}");
                                }

                                // --- match header availability (isolated; gated on a live score line so
                                // screens outside a match never pay the walk). Keep last-good on failure:
                                // no spurious refresh, no lost baseline. ---
                                try { hdr = s != null ? MatchWatcher.HeaderStateToken() : null; }
                                catch (Exception ex)
                                {
                                    hdr = lastHeaderState;
                                    ServerText.LogBackground($"Header state read failed: {ex.Message}");
                                }
                            }

                            // --- match audio: possession cue zone and out-of-play kind from ONE ball read,
                            // isolated from the screen and commentary reads. ComputeMatchAudio returns
                            // (None, Off, InPlay, false, Unclear) for every gap (no match, between highlights,
                            // no clear possession), so a failure here only means an unexpected read error;
                            // keep last-good rather than silencing the metronome or firing a wrong one-shot. ---
                            string cu = lastCue, ocu = lastOppCue;
                            var ctl = CueControl.None;
                            var octl = CueControl.None;
                            var ok = lastGeomKind;
                            bool replay = false;
                            bool ct = false;
                            bool ar = false;
                            float pn = float.NaN;
                            float dRef = float.NaN;
                            float dOpp = float.NaN;
                            float drn = 0f;
                            float alv = float.NaN;
                            int slc = -1;
                            float pRef = float.NaN;
                            float pOpp = float.NaN;
                            float nm = float.NaN;
                            try
                            {
                                var (control, zone, oppCtl, oppZone, outNow, inRep, contestedNow, airNow, panNow, distRefNow, distOppNow, droneNow, airLevelNow, sliceNow, pressRefNow, pressOppNow, nearestNow) = MatchBallLocation.ComputeMatchAudio();
                                cu = MatchBallLocation.CueToken(zone);
                                ctl = control;
                                ocu = MatchBallLocation.CueToken(oppZone);
                                octl = oppCtl;
                                ok = outNow;
                                replay = inRep;
                                ct = contestedNow;
                                ar = airNow;
                                pn = panNow;
                                dRef = distRefNow;
                                dOpp = distOppNow;
                                drn = droneNow;
                                alv = airLevelNow;
                                slc = sliceNow;
                                pRef = pressRefNow;
                                pOpp = pressOppNow;
                                nm = nearestNow;
                            }
                            catch (Exception ex)
                            {
                                cu = lastCue; ctl = CueControl.None; ocu = lastOppCue; octl = CueControl.None;
                                ok = lastGeomKind; replay = false;
                                ct = false;
                                ar = false;
                                pn = float.NaN;
                                dRef = float.NaN;
                                dOpp = float.NaN;
                                // NaN is "no update" (the client holds its last drone level), matching pan and
                                // dist. A failed read is not knowledge that the ball is central, so it must not
                                // be reported as silence; an actual 0 is reserved for knowing there is nothing
                                // to play. A persistent failure shows up in the liveness surface.
                                drn = float.NaN;
                                alv = float.NaN;
                                slc = -1;
                                pRef = float.NaN;   // same rule: a failed read is not "nobody is pressing"
                                pOpp = float.NaN;
                                nm = float.NaN;     // and not "nobody is near the ball"
                                ServerText.LogBackground($"Cue read failed: {ex.Message}");
                            }

                            // --- goal and out-of-play one-shots from the engine event stream (position-free,
                            // isolated). The caller holds each high-water; the scans read the played event
                            // range up to it. A gap returns Ok=false so the caller keeps its high-water. ---
                            // FM27 temporarily rewinds/rebinds the commentary window at period breaks.
                            // A dead-period index must neither baseline a newly connected stream nor
                            // move an existing bookmark backwards and replay the first half's cues.
                            // The shared audio read already identifies a real playback slice; keep
                            // all event cursors untouched while that live source is absent.
                            MatchBallLocation.GoalScan gs = default;
                            try { if (slc >= 0) gs = MatchBallLocation.ScanGoals(eventWindow.Goal.HighWater); }
                            catch (Exception ex) { gs = default; ServerText.LogBackground($"Goal scan failed: {ex.Message}"); }

                            MatchBallLocation.RestartScan rs = default;
                            try { if (slc >= 0) rs = MatchBallLocation.ScanRestarts(eventWindow.Out.EventHighWater); }
                            catch (Exception ex) { rs = default; ServerText.LogBackground($"Restart scan failed: {ex.Message}"); }

                            MatchBallLocation.DefenceScan ds = default;
                            try { if (slc >= 0) ds = MatchBallLocation.ScanDefence(eventWindow.Defence.HighWater); }
                            catch (Exception ex) { ds = default; ServerText.LogBackground($"Defence scan failed: {ex.Message}"); }

                            MatchBallLocation.ShotScan shs = default;
                            try { if (slc >= 0) shs = MatchBallLocation.ScanShots(eventWindow.Shot.HighWater); }
                            catch (Exception ex) { shs = default; ServerText.LogBackground($"Shot scan failed: {ex.Message}"); }

                            // The engine's own account of who won the ball, which overrides the geometric
                            // dwell below. Read here with its siblings so all four event scans share one
                            // main-thread dispatch and one played high-water discipline.
                            MatchBallLocation.TurnoverScan tos = default;
                            try { if (slc >= 0) tos = MatchBallLocation.ScanTurnovers(TurnoverHighWater()); }
                            catch (Exception ex) { tos = default; ServerText.LogBackground($"Turnover scan failed: {ex.Message}"); }

                            // --- fast-forward state (isolated). Only meaningful with a live playback
                            // reading this tick (slc >= 0): in a dead period the mode can linger on
                            // BetweenHighlights* while nothing is actually advancing, and the loop must
                            // not play through half time. The reader itself degrades to false (audible,
                            // the status quo) on no match / unreadable mode; keep-last on unexpected
                            // failure like the cues. ---
                            bool ffNow = lastFfActive;
                            float viewingSpeed = 4;
                            int minimumGapSlices = 0;
                            try
                            {
                                ffNow = slc >= 0 && MatchBallLocation.ReadFastForward();
                                if (slc >= 0) viewingSpeed = MatchBallLocation.ReadHighlightSpeed();
                                if (slc >= 0) minimumGapSlices = MatchBallLocation.ReadMinimumSkippedSlices();
                            }
                            catch (Exception ex) { ServerText.LogBackground($"Fast-forward read failed: {ex.Message}"); }

                            // --- commentary gap filler candidate (isolated). Composed only when the gate's
                            // clocks would allow an insertion AND a match is live (a score line exists) AND
                            // playback is not fast-forwarding (30x possession is noise), so normal ticks
                            // never pay the player scan. Null (unjudgeable / read error) just means no
                            // filler this tick — enrichment, never a marker. ---
                            string fl = null;
                            if (wantFiller && s != null && !ffNow)
                            {
                                try { fl = MatchBallLocation.ComposeCommentaryFiller(); }
                                catch (Exception ex) { ServerText.LogBackground($"Filler read failed: {ex.Message}"); }
                            }

                            return (t, snap, shp, c, s, cu, ctl, ocu, octl, ok, replay, ct, ar, pn, dRef, dOpp, drn, alv, pRef, pOpp, nm, slc, ffNow, fl, gs, rs, ds, shs, tos, loading, hdr, MatchBallLocation.CurrentMatch()?.Pointer ?? IntPtr.Zero, viewingSpeed, sampleAtMs, minimumGapSlices);
                        });
                    }
                    catch (Exception ex)
                    {
                        // Only a dispatch timeout reaches here (all sub-reads are guarded inside). Keep
                        // last-good for every channel so a stalled main thread neither blinds screen
                        // detection nor mutes the match.
                        token = lastToken; snapshot = lastSnapshot;
                        commentary = lastCommentary; score = lastScore; cue = lastCue; oppCue = lastOppCue;
                        cueControl = CueControl.None; oppControl = CueControl.None; outKind = lastGeomKind; inReplay = false;
                        contested = false; air = false; pan = float.NaN; distRef = float.NaN; distOpp = float.NaN;
                        drone = float.NaN;   // a stalled tick is not knowledge; hold the last level
                        airLevel = float.NaN;
                        pressRef = float.NaN; pressOpp = float.NaN; nearestMan = float.NaN;
                        audioSlice = -1; ff = lastFfActive; filler = null; goal = default; restart = default; defence = default; shot = default; turnover = default;
                        loadingActive = false; headerState = lastHeaderState;
                        ServerText.LogBackground($"Main-thread read timed out; skipping this tick: {ex.Message}");
                    }
                }
                else
                {
                    token = "loading"; // snapshot stays lastSnapshot; shape null; commentary/score stay last
                }

                // Scope the inherited sound bookmarks to the real native match.
                // Loading another career can retain this EventSource connection;
                // its new commentary indices must not inherit the previous game.
                if (servable && eventWindow.Bind(nativeEventMatch))
                {
                    goal = default; restart = default; defence = default;
                    shot = default; turnover = default;
                    goalQuiet = false; goalQuietSlice = 0; goalQuietLiveSinceMs = 0;
                    lastAudioSlice = int.MinValue;
                    EventLog.Record("eventwindow", "native match changed; sound bookmarks reset");
                }
                // --- fast-forward / pause state, resolved BEFORE any emission so both halves can
                // gate on it (the commentary live region suppresses on it too, not just the audio
                // channels). ---
                // Pause mute: a playback slice that has not moved for FrozenAfterMs while in a play
                // period means playback is halted (the pause button, a stopped replay) — the action
                // is frozen on screen, so a repeating tick would just re-announce a still frame.
                // Force every cue channel off until the slice moves again; unpausing recomputes
                // everything within one sub-tick. Dead periods are already silenced upstream.
                // Skip detection by slice RATE, the backstop behind the sampled fast-forward flag.
                // The flag is read at one instant per tick, but the game's minimum between-highlight
                // gap is only 4 GAME seconds (HighlightModeData.MinimumBetweenHighlightLengthSeconds,
                // read live) — ~0.13s of wall time at 30x — so a skip can start and finish entirely
                // between two reads and its events would fire as live one-shots. And even a detected
                // window's final stretch lands on the first flag-off tick's scan span. Both cases
                // share one signature: more game time passed since the last reading than real-time
                // playback can produce in that much wall time. Slices advance ~4 per game second, so
                // playback yields ~4 x the actual native highlight speed slices per wall second.
                // Keep the inherited 4x floor at ordinary speeds and one slice of quantization
                // allowance above it. A 10x highlight is legitimate; a 30x between-highlight
                // span still exceeds that rate, and the native flag independently mutes it.
                // Use the native frame-clock interval rather than a fixed delta so a slow dispatch does not cause
                // a false skip. A rewind (replay) is a negative delta, never a skip.
                bool skipped = false;
                if (audioSlice >= 0)
                {
                    if (lastAudioSlice != int.MinValue && audioSampleAtMs > lastAudioSliceAtMs)
                    {
                        double wallSeconds = (audioSampleAtMs - lastAudioSliceAtMs) / 1000.0;
                        skipped = MatchEventWindow.CrossedSkippedGround(lastAudioSlice, audioSlice, wallSeconds, highlightSpeed, minimumSkippedSlices);
                        if (skipped && !ff && (goal.Crossed || shot.KickOccurred || shot.HeaderOccurred))
                            EventLog.Record("playbackskip", $"slice {lastAudioSlice}->{audioSlice}; sampleMs={audioSampleAtMs - lastAudioSliceAtMs}; highlightSpeed={highlightSpeed}; minimumGapSlices={minimumSkippedSlices}; goal={goal.Crossed}");
                    }
                    // Frozen = no slice movement for FrozenAfterMs, measured on the wall clock: at
                    // the sub-tick cadence, equal neighbouring reads are normal at slow playback
                    // speeds, so consecutive-equal is no longer evidence of a halt on its own.
                    if (audioSlice != lastAudioSlice || lastAudioSlice == int.MinValue)
                        lastSliceChangeMs = now;
                    bool frozen = now - lastSliceChangeMs >= FrozenAfterMs;
                    lastAudioSlice = audioSlice;
                    lastAudioSliceAtMs = audioSampleAtMs;
                    if (frozen)
                    {
                        cue = "off"; oppCue = "off";
                        cueControl = CueControl.None; oppControl = CueControl.None;
                        contested = false; air = false;
                        pan = float.NaN; distRef = float.NaN; distOpp = float.NaN;
                        drone = 0f;   // a paused match: the drones fade out with everything else
                        // The air cue itself is forced off on a pause, so its level is simply held:
                        // re-sending a level for a sound that is not playing would only risk it being
                        // wrong when it restarts.
                        airLevel = float.NaN;
                        // A paused match: the bed fades out with every other cue rather than holding a
                        // level for action that is frozen on screen.
                        pressRef = 0f; pressOpp = 0f;
                        // Pausing mid-fast-forward keeps the mode on BetweenHighlights*, but nothing
                        // is advancing: the fast-forwarding loop tails out like every other cue.
                        ff = false;
                    }
                }
                else
                {
                    // No live reading (dead period / timeout): baseline both the skip delta and the
                    // freeze clock, so the first live tick after half time never mistakes the
                    // break's slice gap for a skip (which would eat the second-half kick-off ping)
                    // and never reads as instantly frozen.
                    lastAudioSlice = int.MinValue;
                    lastSliceChangeMs = now;
                }
                // Fast-forward mute: force the same set the pause mute forces (the grace below cannot
                // resurrect a held cue because the controls are None). The 'ff' channel further down
                // loops the fast-forwarding sound in its place, and the one-shot emissions are
                // suppressed while their arbiters keep advancing their high-waters past the skipped
                // events, so nothing retro-fires when the highlight starts.
                lastFfActive = ff;
                // The mute applies on the flag AND on any tick that carried a slice jump: the jump
                // means this tick crossed skipped ground (a sub-tick skip the sampled flag never
                // saw, or a detected window's final stretch), and everything in it — the one-shots
                // it scanned AND the continuous cues — is between-highlights noise. A skipped tick
                // without the flag silences the cues for one sub-tick only (~125ms); they recompute
                // from the already-live ball state on the next read.
                if (ff || skipped)
                {
                    cue = "off"; oppCue = "off";
                    cueControl = CueControl.None; oppControl = CueControl.None;
                    contested = false; air = false;
                    pan = float.NaN; distRef = float.NaN; distOpp = float.NaN;
                    // Hold the drone level rather than zeroing it. Real fast-forward is silenced client-side
                    // by the cue gate along with every other cue, so zeroing here would only add a dip on the
                    // single skipped sub-tick — and on a continuous drone that reads as a wobble, where a
                    // metronome merely misses a beat.
                    drone = float.NaN;
                    airLevel = float.NaN;
                    // Held for the same reason as the drone: a one-sub-tick dip in a continuous bed reads
                    // as a wobble, where a metronome merely misses a beat.
                    pressRef = float.NaN; pressOpp = float.NaN;
                }
                bool suppressSfx = ff || skipped;

                // Consume any in-panel action arm. Navigation (token change) supersedes it below;
                // otherwise it arms a settle window with the pre-mutation baseline and forces the
                // first in-window tick to re-read unconditionally. Full ticks only — consuming the
                // epoch on a sub-tick (which skips the settle logic) would silently lose the arm.
                bool actionArmed = false;
                if (fullTick)
                {
                    int actionEpoch = _actionEpoch;
                    actionArmed = actionEpoch != seenEpoch;
                    if (actionArmed) seenEpoch = actionEpoch;
                }

                // Hold a provisional unlabeled marker out of the values push (see MarkerHoldTicks).
                // Full ticks only: a sub-tick carries lastSnapshot by reference and must never be
                // rewritten under the baseline it IS.
                if (fullTick && !ReferenceEquals(snapshot, lastSnapshot))
                    snapshot = markerHold.Apply(snapshot, lastSnapshot);

                // Consumed here (not inside the dispatch) so the count this tick's Snapshot recorded
                // is read exactly once; accumulates across sub-ticks, which take no snapshot.
                int staleDrops = fullTick ? LiveValues.TakeStaleDrops() : 0;

                bool sent = false;
                if (token != null && token != lastToken)
                {
                    WriteSse(stream, $"event: screen\ndata: {ServerText.SseEscape(token)}\n\n");
                    EventLog.Record("screen", token);
                    lastToken = token;
                    // The client's /body refresh delivers fresh text for the new screen, so
                    // reset the values baseline instead of pushing the whole screen as a diff.
                    lastSnapshot = snapshot;
                    sent = true;
                    // Start (or restart) the settle window. settleForced makes the first in-window
                    // tick re-read unconditionally and capture the shape baseline there, then the
                    // window shape-gates the rest. That forced read covers content that populates in
                    // the first ~500ms (before a lazily-captured baseline would notice it); without
                    // it a fast step load leaves the surface on the half-built optimistic read.
                    settleDeadline = now + SettleWindowMs;
                    settleSig = null;
                    stableTicks = 0;
                    settleForced = true;
                    markerRetries = 0;
                    // The ids belong to the page being left; their grace counters mean nothing on
                    // the new one, and clearing here is what bounds this map over a session.
                    markerHold.Reset();
                }
                else
                {
                    if (actionArmed)
                    {
                        // Arm with the pre-mutation baseline and let the window's shape-gated path
                        // catch a genuine structural follow-up. We do NOT force an unconditional
                        // re-read here (that rebuilt /body even when nothing changed — the form-
                        // filling no-op storm). The action's own effect is already shown by the
                        // client's optimistic refresh; late text is caught by the values channel;
                        // late structure is caught by the shape gate below. See
                        // docs/archive/plans/refresh-step-4-perf-plan.md.
                        settleDeadline = now + SettleWindowMs;
                        settleSig = _actionBaselineShape;
                        stableTicks = 0;
                        // A new native action can replace content inside the
                        // same panel (medical severity tabs do so). Its initial
                        // body may be provisional even after earlier actions
                        // spent this panel's retry allowance. Give this explicit
                        // action its own unchanged, bounded retry budget; do not
                        // let unrelated earlier gaps strand the new result.
                        markerRetries = 0;
                        lastMarkerComposeAtMs = 0;
                    }

                    if ((inWindow || loadingActive) && shape != null)
                    {
                        if (settleForced)
                        {
                            var forcedComposed = _composed;
                            if (forcedComposed != null && forcedComposed.CanSuppress(shape,now,ComposedShapeTtlMs))
                            {
                                // The companion's own navigation /body already rendered exactly this
                                // structure, so the guaranteed re-read would only re-deliver it (the
                                // navigation double-compose; the same soundness argument as the
                                // suppression in the shape-gated branch below). Adopt the baseline;
                                // content that populates later still restructures via the shape gate,
                                // and text drift still flows through the values diff (lastSnapshot is
                                // deliberately not reset).
                                ServerText.LogBackground("forced restructure suppressed: shape matches last composed body");
                                settleSig = shape;
                                stableTicks = 0;
                                settleForced = false;
                            }
                            else
                            {
                                // Guaranteed re-read after a navigation/step change, in case its content
                                // populated within the first ~500ms (before the shape baseline below is
                                // captured) and so would not register as a later shape change.
                                WriteSse(stream, "event: restructure\ndata: 1\n\n");
                                EventLog.Record("restructure", null);
                                settleSig = shape;
                                lastSnapshot = snapshot;
                                stableTicks = 0;
                                settleForced = false;
                                sent = true;
                            }
                        }
                        else if (settleSig == null)
                        {
                            settleSig = shape;
                        }
                        else if (shape != settleSig)
                        {
                            var composed = _composed;
                            if (composed != null && composed.MatchesNativeShape(shape,now,ComposedShapeTtlMs))
                            {
                                // The compose captured this native structure, including any window
                                // its own table harvest realised. Adopt that structural baseline;
                                // this does NOT certify a partial body's completeness. Its marker
                                // and unchanged bounded retry budget still recover late content.
                                // Keep the values baseline so real text updates remain observable.
                                ServerText.LogBackground(composed.HasMarker
                                    ? "provisional structural baseline adopted; bounded marker recovery retained"
                                    : "restructure suppressed: shape matches last composed body");
                                settleSig = shape;
                                stableTicks = 0;
                            }
                            else if (CompanionComposeInFlight && settleDeadline - now > 600)
                            {
                                // The client is fetching this content RIGHT NOW; a restructure would
                                // only make it fetch again. Hold the decision one tick (no emit, no
                                // baseline update, so the comparison simply repeats): by the next
                                // tick the compose has noted its shape — a fetch that captured this
                                // state suppresses above, one that raced ahead of further changes
                                // still gets its restructure. Bounded so a deferral can never cross
                                // the settle deadline and lose the restructure outright.
                                ServerText.LogBackground("restructure deferred: companion compose in flight");
                            }
                            else
                            {
                                // Content changed during the window: re-read the screen (the client
                                // morphs it in gently). Reset the values baseline like the screen
                                // branch, since the morph delivers fresh text too.
                                WriteSse(stream, "event: restructure\ndata: 1\n\n");
                                EventLog.Record("restructure", null);
                                settleSig = shape;
                                lastSnapshot = snapshot;
                                stableTicks = 0;
                                sent = true;
                            }
                        }
                        else if (++stableTicks >= SettleStableTicks)
                        {
                            // A settled ordinary screen stops being watched until the next navigation.
                            // A loading screen is never closed here: its content can still arrive long
                            // after a stable stretch (world-gen pause, then the news article), so it
                            // stays watched (via the loadingActive gate above) for its whole lifetime.
                            if (!loadingActive) settleDeadline = 0;
                        }
                    }

                    // Marker retry: a companion-served body that rendered a control as the unlabeled
                    // degrade marker may have raced text that was still binding — the tunnel interview
                    // recreates its answer buttons per question, and a compose landing between the
                    // elements appearing and their SIText binding serves markers; the values patch that
                    // followed was then undone by the in-flight morph, and text arrival never changes
                    // the structural shape, so nothing re-triggered (unlabeled until F5). Re-read once
                    // per marker-bearing compose, shortly after it, so the refetch lands with labels
                    // bound. The retry budget stops a screen whose markers are permanent (a genuinely
                    // unlabeled icon button) from looping; any marker-free compose refunds it, so a flow
                    // of alternating provisional pages (one interview question after another) keeps
                    // retrying.
                    var markerComposed = _composed;
                    if (markerComposed != null && !markerComposed.HasMarker)
                        markerRetries = 0;
                    if (fullTick && !sent && markerComposed != null && markerComposed.HasMarker
                        && markerComposed.AtMs != lastMarkerComposeAtMs
                        && now - markerComposed.AtMs >= MarkerRetryDelayMs * (markerRetries + 1)
                        && markerRetries < MarkerRetryMax)
                    {
                        lastMarkerComposeAtMs = markerComposed.AtMs;
                        markerRetries++;
                        WriteSse(stream, "event: restructure\ndata: 1\n\n");
                        EventLog.Record("restructure", "marker retry");
                        lastSnapshot = snapshot;
                        sent = true;
                    }

                    // Stale tracked elements (a panel that rebuilt its controls in place, leaving the
                    // value map pointing at the replaced ones — see LiveValues.DropStaleMarkerSources).
                    // The map cannot serve honest values for this screen until a render re-registers
                    // it against the live elements, and the structural shape does not change when a
                    // control is swapped for its replacement, so the settle window never fires.
                    // Refetch: the same thing F5 did by hand.
                    if (!sent && staleDrops > 0)
                    {
                        WriteSse(stream, "event: restructure\ndata: 1\n\n");
                        EventLog.Record("restructure", "stale value sources");
                        lastSnapshot = snapshot;
                        sent = true;
                    }

                    // Match-header availability flip (pending-changes toast swapped back, shout
                    // cooldown expired): emit a restructure so the page re-renders without a manual
                    // refresh. Only a genuine in-match flip fires — the first in-match observation
                    // and match entry/exit only move the baseline (the screen token covers those),
                    // so this can never add churn to a screen where nothing changed.
                    if (!sent && headerState != null && lastHeaderState != null && headerState != lastHeaderState)
                    {
                        WriteSse(stream, "event: restructure\ndata: 1\n\n");
                        EventLog.Record("restructure", "match header state");
                        lastSnapshot = snapshot;
                        sent = true;
                    }

                    if (!sent)
                    {
                        var changed = ValueDiff.Changed(lastSnapshot, snapshot);
                        if (changed.Count > 0)
                        {
                            changed["__fm_value_at"] = valuesReadAt.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            var valuesJson = JsonMap.Encode(changed);
                            WriteSse(stream, $"event: values\ndata: {ServerText.SseEscape(valuesJson)}\n\n");
                            EventLog.Record("values", valuesJson);
                            lastSnapshot = snapshot;
                            sent = true;
                        }
                    }
                }
                lastHeaderState = headerState;
                // The commentary live region is muted with the audio cues while fast-forwarding
                // (and on a skipped tick): at 30x the game's play-by-play lines flick past faster
                // than speech can follow, so pushing them would just gabble over the fast-forwarding
                // sound. lastCommentary deliberately does NOT advance on a suppressed line, so the
                // line showing when the highlight starts still differs from the last SPOKEN one and
                // is announced then — the player rejoins the commentary exactly where the game is.
                // Suppressions are recorded so /events/log shows the drop, never silent.
                bool commentaryCleared = string.IsNullOrEmpty(commentary)
                                         && !string.IsNullOrEmpty(lastCommentary);
                bool commentaryChanged = !string.IsNullOrEmpty(commentary) && commentary != lastCommentary;
                bool commentarySpoke = commentaryChanged && !ff && !skipped;
                if (commentaryCleared)
                {
                    WriteSse(stream, $"event: commentary\ndata: {CommentaryClearToken}\n\n");
                    EventLog.Record("commentary", "");
                    lastCommentary = null;
                    sent = true;
                }
                else if (commentarySpoke)
                {
                    WriteSse(stream, $"event: commentary\ndata: {ServerText.SseEscape(commentary)}\n\n");
                    EventLog.Record("commentary", commentary);
                    lastCommentary = commentary;
                    sent = true;
                }
                else if (commentaryChanged)
                {
                    EventLog.Record("commentarymuted", commentary);
                }
                // Commentary gap filler: the possession line (team clause dropped) inserted into the
                // same live region while the game's play-by-play is quiet, so the gaps carry live info
                // without disturbing the real stream. Emitted on the commentary SSE channel (the client is
                // untouched) but recorded as "filler" so /events/log tells insertions from real lines.
                // lastCommentary keeps tracking the GAME's line only: a filler never makes the unchanged
                // game line re-emit, and the next real change still speaks.
                if (fullTick && fillerGate.Observe(now, commentarySpoke, commentary, filler))
                {
                    WriteSse(stream, $"event: commentary\ndata: {ServerText.SseEscape(filler)}\n\n");
                    EventLog.Record("filler", filler);
                    sent = true;
                }
                // Score changes update silently; an empty event clears the page's
                // score line when the match ends.
                if (score != lastScore)
                {
                    WriteSse(stream, $"event: score\ndata: {ServerText.SseEscape(score ?? "")}\n\n");
                    EventLog.Record("score", score ?? "");
                    lastScore = score;
                    sent = true;
                }
                // A screen-changing simulation just settled: tell the companion which page to
                // land focus on (the token is the arrival page's identity).
                int simEpoch = _simArriveEpoch;
                if (simEpoch != seenSimArrive)
                {
                    seenSimArrive = simEpoch;
                    var simToken = _simArriveToken;
                    if (simToken != null)
                    {
                        WriteSse(stream, $"event: simarrive\ndata: {ServerText.SseEscape(simToken)}\n\n");
                        EventLog.Record("simarrive", simToken);
                        sent = true;
                    }
                }
                // Turnover confirmation, BEFORE the grace: the geometric read has no memory, so it hands the
                // cue over the instant an opponent is nearest. The arbiter makes a candidate turnover serve a
                // dwell first, rewriting the two controls so an unconfirmed one never reaches the grace below
                // — the incumbent arrives as a soft loss and is sustained by it, the challenger as a hard loss
                // and stays silent. It rewrites CONTROLS only; the zone tokens still describe the geometry, so
                // the grace below is what has to make a suppressed challenger actually silent.
                //
                // The whole possession update is taken under one lock because the arbiter and the claim gate
                // are shared across streams; it is a few field writes, so a second viewer never waits on it
                // meaningfully.
                PossessionHolder holderNow;
                var claimedForTouch = PossessionHolder.None;
                // The RAW geometric controls, before the arbiter rewrites them in Observe below: the
                // touch tracker uses "a team's man is on the ball" as a challenge's confirming second
                // witness, and it must see the geometry's own judgement, not the arbiter's sustain.
                var rawRefControl = cueControl;
                var rawOppControl = oppControl;
                lock (PossessionGate)
                {
                    (cueControl, oppControl) = possession.Observe(cueControl, oppControl, now, TurnoverDwellMs);

                    // The engine's own account of a turnover OVERRIDES the geometric dwell, and is applied
                    // after it so it wins outright. The dwell weighs an uncertain read; an event naming the
                    // team that won the ball is not a read to be weighed. Applying it here rather than
                    // inside Observe keeps the arbiter a pure function of the geometry it was written for,
                    // with authority arriving as a separate, explicit act.
                    var claimed = _turnoverClaim.Observe(
                        turnover.Ok, turnover.LastEventIndex, turnover.InReplay, turnover.Occurred, turnover.ForRef);
                    if (claimed != PossessionHolder.None)
                    {
                        possession.Concede(claimed);
#if FM27_DEVELOPER_TOOLS
                        Dev.PossessionTrace.NoteEngineTurnover(turnover.ForRef, turnover.Slice);
#endif
                        claimedForTouch = claimed;
                    }

                    // A break that clears the claim clears the event baseline with it. Event indices restart
                    // with a new match, so a high-water carried over from the last one would sit above every
                    // index of the next and silently suppress all of its turnovers.
                    if (possession.Holder == PossessionHolder.None
                        && cueControl == CueControl.None && oppControl == CueControl.None)
                        _turnoverClaim.Reset();

                    holderNow = possession.Holder;
                }
                MatchBallLocation.NoteConfirmedHolder(holderNow, now);

                // The last-touch judgement (the standard tick driver; /posmode?touch=0 falls back
                // to the legacy geometric arbiter for an A/B). Its touch evidence
                // arrives from the per-frame TouchSampler; the engine's own events are authority in
                // both systems, so the same turnover the arbiter just conceded to — and any live
                // restart — feed it here, AFTER PossessionGate is released (the two locks are never
                // held together). Both calls are idempotent, so every connected stream feeding the
                // same event costs nothing.
                if (claimedForTouch != PossessionHolder.None)
                    TouchPossession.NoteEngineTurnover(
                        claimedForTouch == PossessionHolder.Reference, now);
                if (restart.Ok && restart.Occurred && !restart.InReplay)
                    TouchPossession.NoteRestart(restart.ForRef, now);
                // InControl is mutually exclusive between the teams by CueForTeam's complement
                // invariant, so at most one of these fires per sample.
                if (rawRefControl == CueControl.InControl)
                    TouchPossession.NoteGeometricControl(forRef: true, now);
                else if (rawOppControl == CueControl.InControl)
                    TouchPossession.NoteGeometricControl(forRef: false, now);
                var (touchHolder, touchRestarter, touchFresh) = TouchPossession.Snapshot(now);
                bool touchMode = TouchPossession.Enabled;
                // Which claim drives the audible ticks: the toggle picks the system, and a stale touch
                // feed (sampler not running) degrades to no holder — honest silence, never a claim
                // nobody is maintaining.
                var tickHolder = touchMode
                    ? (touchFresh ? touchHolder : PossessionHolder.None)
                    : holderNow;

                // Carrier pressure follows the confirmed holder, so it is selected here rather than in the
                // geometry: both teams' figures come back from the read and the arbiter decides which one
                // describes the man actually on the ball. With no holder there is nobody to press — a loose
                // ball, a dead ball, a gap between highlights — and the bed goes silent rather than holding
                // a level that belongs to a passage of play already over. NaN survives as NaN, because
                // failing to read the pitch is not the same as knowing nobody is pressing.
                float pressure =
                    tickHolder == PossessionHolder.Reference ? pressRef
                    : tickHolder == PossessionHolder.Opposition ? pressOpp
                    : (float.IsNaN(pressRef) && float.IsNaN(pressOpp)) ? float.NaN
                    : 0f;

                // Possession follows the arbiter's CONFIRMED HOLDER, not a grace timer. The holder is sticky
                // by construction: PossessionArbiter only moves it when the other team is in control for the
                // whole dwell, so a deflection off a defender or a shot that nicks someone cannot take the
                // ball, and it survives every loose-ball sample in between untouched.
                //
                // This replaced a pair of grace windows that held the last on-ball zone for a fixed time after
                // the last touch. /postrace measured why they had to go: at a contact-sized on-ball radius a
                // team is only geometrically in control on a small minority of samples, so most of every tick was already a
                // sustained claim, and EVERY silence run recorded began from a soft loss rather than a
                // turnover — long balls, at a mean 8.1 m/s against 6.4 m/s while ticking, simply outlived the
                // window. Lengthening it could not fix that without over-claiming short passes too. The holder
                // has no such tension: it already knows whose ball it is for as long as nobody takes it.
                //
                // The zones are live every sample now (see CueForTeam), so the tick follows the ball around
                // the pitch during a pass instead of repeating the zone it was struck from.
                //
                // Silence is now reserved for the cases where there is genuinely nothing to claim:
                //   - a dead ball. Possession off the pitch is not a thing the ticks should assert, and the
                //     restart one-shots cover the moment instead.
                //   - no reading at all (both controls None): between highlights, a pause, or a fast-forward,
                //     all of which force None upstream. Observe() resets the holder on that pair, so the mutes
                //     stay muted and the next passage has to earn a fresh claim rather than resuming an old one.
                //   - nobody has established possession yet (kick-off, or the first touches after a reset).
                // At most one metronome can run because a holder names one team; the other is silenced here
                // explicitly rather than relying on its zone to be Off.
                bool ballDead = outKind != Navigation.OutOfPlayKind.InPlay;
                bool noReading = cueControl == CueControl.None && oppControl == CueControl.None;
                // The far-loose clock: a ball more than FarLooseDistance from every player for
                // FarLooseMuteMs is in no-man's-land (a clearance, a long punt rolling out of reach),
                // and asserting a holder's tick through that is the one loose-ball silence the
                // last-touch semantics lost. Holder unchanged — this mutes, never judges.
                if (float.IsNaN(nearestMan) || nearestMan <= FarLooseDistance) lastNearMs = now;
                bool farLoose = now - lastNearMs >= FarLooseMuteMs;
                // Backstop for a kick-off whose event never crosses (it should always cross, even in
                // fast-forward — the event scans read the played window): play running CONTINUOUSLY
                // for two seconds (in play, readable, a fresh claim established since the goal reset)
                // proves the restart happened. The net-line flicker the latch exists for produces only
                // isolated in-play samples, so it cannot accumulate this run.
                if (goalQuiet)
                {
                    // A replay is not proof of a kick-off — it is the goal being shown again — so its
                    // live-looking play must not accumulate this run. Without the guard a replay long
                    // enough to reach two seconds would drop the latch for good, and the net-line
                    // flicker it exists for would strobe through the gap after the replay ends.
                    bool liveNow = !inReplay && !ballDead && !noReading && tickHolder != PossessionHolder.None;
                    if (!liveNow) goalQuietLiveSinceMs = 0;
                    else if (goalQuietLiveSinceMs == 0) goalQuietLiveSinceMs = now;
                    else if (now - goalQuietLiveSinceMs >= 2000) goalQuiet = false;
                }
                if (touchMode && goalQuiet && !inReplay)
                {
                    // Between a goal and the kick-off the ball is dead in the net however the
                    // geometry reads it; the engine's own events bound the quiet exactly. A replay
                    // suspends the quiet rather than ending it: the replay scans into its own claim,
                    // so the ticks there describe the move being re-shown, not the celebration. The
                    // latch stands, so the gap after the replay goes quiet again until the kick-off.
                    cue = "off";
                    oppCue = "off";
                }
                else if (touchMode && ballDead && !noReading && touchFresh
                    && touchRestarter != PossessionHolder.None)
                {
                    // Last-touch mode ticks the RESTARTING team through a dead ball: the engine named
                    // it, and the pre-announcement is the point. The zones stay live out of play (see
                    // ComputeCue), so the tick names where the restart is. noReading still silences —
                    // a pause or fast-forward outranks the restart.
                    if (touchRestarter == PossessionHolder.Reference) oppCue = "off";
                    else cue = "off";
                }
                else if (ballDead || noReading || tickHolder == PossessionHolder.None
                         || (touchMode && farLoose))
                {
                    cue = "off";
                    oppCue = "off";
                }
                else if (tickHolder == PossessionHolder.Reference)
                {
                    oppCue = "off";
                }
                else
                {
                    cue = "off";
                }
                // Pairs with the geometry recorded inside ComputeCue to give the dev trace one row per
                // sample across the whole chain. Off unless /postrace?on=1 has been called.
#if FM27_DEVELOPER_TOOLS
                Dev.PossessionTrace.NoteOutcome(now, cueControl, oppControl, holderNow, cue, oppCue, audioSlice,
                    touchHolder, touchMode);
#endif
                // Distance to goal for the tick acceleration: while a cue is active (including its grace),
                // push the possessing team's straight-line metres to the goal it attacks, on-change (>0.5m).
                // Emitted BEFORE the cue channels so a metronome that starts this tick reads a fresh cadence.
                // Both cues off = no ticking = nothing to pace, so nothing is emitted and the client keeps
                // its last value harmlessly.
                float dist = cue != null && cue != "off" ? distRef
                           : oppCue != null && oppCue != "off" ? distOpp
                           : float.NaN;
                if (!float.IsNaN(dist) && (float.IsNaN(lastDist) || Math.Abs(dist - lastDist) > 0.5f))
                {
                    string distTok = dist.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                    WriteSse(stream, $"event: dist\ndata: {distTok}\n\n");
                    EventLog.Record("dist", distTok);
                    lastDist = dist;
                    sent = true;
                }
                // Touchline drones: one signed level, sign choosing which touchline and magnitude how near
                // the ball is to it, pushed on change. This is the width axis, which the stereo field cannot
                // carry because pan is spent on the length. NaN means "no reading this tick" and is not sent,
                // so the client holds its level instead of dipping. The threshold keeps a ball drifting inside
                // a lane from pushing on every sub-tick while staying far finer than the client's smoothing,
                // so the swell still sounds continuous.
                if (!float.IsNaN(drone) && (float.IsNaN(lastDrone) || Math.Abs(drone - lastDrone) > 0.02f
                                            || (drone == 0f && lastDrone != 0f)))
                {
                    string droneTok = drone.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                    WriteSse(stream, $"event: drone\ndata: {droneTok}\n\n");
                    EventLog.Record("drone", droneTok);
                    lastDrone = drone;
                    sent = true;
                }
                // Carrier pressure, on the drone's rules: NaN is not sent so the client holds its level, and
                // the threshold keeps a carrier drifting within one band from pushing every sub-tick while
                // staying far finer than the client's glide, so the swell still sounds continuous. Zero is
                // always pushed once, because the bed falling silent is a real state change.
                if (!float.IsNaN(pressure) && (float.IsNaN(lastPressure) || Math.Abs(pressure - lastPressure) > 0.02f
                                               || (pressure == 0f && lastPressure != 0f)))
                {
                    string pressureTok = pressure.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                    WriteSse(stream, $"event: pressure\ndata: {pressureTok}\n\n");
                    EventLog.Record("pressure", pressureTok);
                    lastPressure = pressure;
                    sent = true;
                }
                // Audio cue zones: each pushed only when it changes (like score), on its own channel. The
                // client runs one metronome per channel off the current zone, so "off" (out of play / no clear
                // possession / between highlights) stops that team's ticking until its next non-off zone
                // arrives. At most one team is on the ball at a time, so at most one metronome ticks.
                if (cue != lastCue)
                {
                    WriteSse(stream, $"event: cue\ndata: {ServerText.SseEscape(cue ?? "off")}\n\n");
                    EventLog.Record("cue", cue ?? "off");
                    lastCue = cue;
                    sent = true;
                }
                if (oppCue != lastOppCue)
                {
                    WriteSse(stream, $"event: oppcue\ndata: {ServerText.SseEscape(oppCue ?? "off")}\n\n");
                    EventLog.Record("oppcue", oppCue ?? "off");
                    lastOppCue = oppCue;
                    sent = true;
                }
                // Contested ball: a sustained looping sound while both teams contest the ball, pushed on-change
                // (like the cue channels) as "on"/"off". The client loops the sound while "on" and stops it on
                // "off". Not replay-gated, matching the metronomes (a replayed contest reflects what is on screen).
                string contestedTok = contested ? "on" : "off";
                if (contestedTok != lastContested)
                {
                    WriteSse(stream, $"event: contested\ndata: {contestedTok}\n\n");
                    EventLog.Record("contested", contestedTok);
                    lastContested = contestedTok;
                    sent = true;
                }
                // How high the ball is, as a 0..1 ramp position for the in-the-air cue's loudness, pushed
                // on change like the drone. Sent BEFORE the on/off below so a flight starting this tick
                // begins at the right loudness rather than at the previous flight's. It is computed and
                // sent whatever the cue is doing — below the aerial height it is simply 0 — so the level
                // is always already correct at the instant the cue turns on. NaN is "no reading", which
                // holds the client's level rather than dropping it to silence.
                if (!float.IsNaN(airLevel) && (float.IsNaN(lastAirLevel)
                                               || Math.Abs(airLevel - lastAirLevel) > 0.02f
                                               || (airLevel == 0f && lastAirLevel != 0f)))
                {
                    string airLevelTok = airLevel.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                    WriteSse(stream, $"event: airlevel\ndata: {airLevelTok}\n\n");
                    EventLog.Record("airlevel", airLevelTok);
                    lastAirLevel = airLevel;
                    sent = true;
                }
                // In the air: one long clip while the ball is high and uncontested, pushed on-change like
                // contested (and like it, not replay-gated: a replayed flight reflects what is on screen).
                // The client starts the clip from the beginning on "on" and stops it on "off".
                string airTok = air ? "on" : "off";
                if (airTok != lastAir)
                {
                    WriteSse(stream, $"event: air\ndata: {airTok}\n\n");
                    EventLog.Record("air", airTok);
                    lastAir = airTok;
                    sent = true;
                }
                // Fast-forwarding: pushed on-change like contested/air. The client loops the stereo
                // fast-forwarding sound on its own un-panned channel while "on"; "off" lets the
                // current pass of the loop finish rather than cutting it, so the sound ends cleanly
                // as the highlight begins.
                string ffTok = ff ? "on" : "off";
                if (ffTok != lastFf)
                {
                    WriteSse(stream, $"event: ff\ndata: {ffTok}\n\n");
                    EventLog.Record("ff", ffTok);
                    lastFf = ffTok;
                    sent = true;
                }
                // Replay start/end markers: one-shots on the replay state's edges, on their own
                // channel (NOT sfx — these are centred viewing-state markers, exempt from the
                // ball pan, the tick duck, and the fast-forward mute, so a replay ending straight
                // into a fast-forward still says "exiting replay" alongside the loop). Only ticks
                // with a live reading move the state, so a timeout can never fabricate a
                // transition. Pausing inside a replay keeps the mode on ReplayStopped — still
                // in-replay, no edge.
                if (audioSlice >= 0)
                {
                    if (lastInReplay != null && inReplay != lastInReplay)
                    {
                        string replayTok = inReplay ? "replaystart" : "replayend";
                        WriteSse(stream, $"event: replay\ndata: {replayTok}\n\n");
                        EventLog.Record("replay", replayTok);
                        sent = true;
                    }
                    lastInReplay = inReplay;
                }
                else
                {
                    lastInReplay = null;
                }
                // Ball stereo pan: pushed when the ball has moved audibly along the pitch length (> 0.02
                // of the field). The client slews one shared StereoPannerNode off it, so the metronome
                // ticks, the one-shots, and the contested loop all localize to the ball. An unreadable
                // pan (NaN: no match / no reading this tick) emits nothing and the client keeps its last
                // value — supplementary positioning, never spoken, so keep-last cannot mislead speech.
                if (!float.IsNaN(pan) && (float.IsNaN(lastPan) || Math.Abs(pan - lastPan) > 0.02f))
                {
                    string panTok = pan.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    WriteSse(stream, $"event: pan\ndata: {panTok}\n\n");
                    EventLog.Record("pan", panTok);
                    lastPan = pan;
                    sent = true;
                }
                // One-shot out-of-play sounds from the pure arbiter (Navigation.OutOfPlayOneShot): the engine
                // restart event (authoritative throw-in / corner / goal kick, the corner split by team) is
                // the only trigger; geometry just arms the once-per-dead-ball latch when the ball returns to
                // play. It baselines the first read and tracks a replay on its own bookmark, so it is called every tick
                // (like the goal one-shot) with both the restart scan and the current geometry. A ball in the
                // net (a goal) sounds nothing here — the goal detector below owns it. lastGeomKind is kept so
                // a timeout tick reuses the last geometry (no spurious edge).
                var oopSound = eventWindow.Out.Observe(
                    restart.Ok, restart.LastEventIndex, inReplay,
                    restart.Occurred, restart.Kind, restart.ForRef, restart.PutOccurred, outKind);
                lastGeomKind = outKind;
                string oopSfx = oopSound switch
                {
                    Navigation.OutOfPlaySound.ThrowIn => "touchline",
                    Navigation.OutOfPlaySound.GoalKick => "goalkick",
                    Navigation.OutOfPlaySound.CornerFor => "cornerfor",
                    Navigation.OutOfPlaySound.CornerAgainst => "corneragainst",
                    _ => null,
                };
                // While fast-forwarding, a one-shot that fires is recorded as "sfxmuted" instead of
                // emitted (here and at the three sites below): the skipped events ARE the
                // between-highlights noise being muted, and the record keeps /events/log honest
                // about what was deliberately dropped.
                if (oopSfx != null)
                {
                    if (suppressSfx) { EventLog.Record("sfxmuted", oopSfx); }
                    else
                    {
                        WriteSse(stream, $"event: sfx\ndata: {oopSfx}\n\n");
                        EventLog.Record("sfx", oopSfx);
                        sent = true;
                    }
                }
                // "Put in play" ping: the same restart scan reports the engine's kick-off / Take* events; the
                // pure arbiter (Navigation.PutInPlayOneShot) baselines, replay-freezes, and dedupes flavour
                // doubles of one take, so the ping sounds exactly once as the ball is played back in.
                bool putPing = eventWindow.PutInPlay.Observe(
                    restart.Ok, restart.LastEventIndex, inReplay, restart.PutOccurred, restart.PutSlice);
                if (putPing)
                {
                    if (suppressSfx) { EventLog.Record("sfxmuted", "putinplay"); }
                    else
                    {
                        WriteSse(stream, "event: sfx\ndata: putinplay\n\n");
                        EventLog.Record("sfx", "putinplay");
                        sent = true;
                    }
                }

                // Shot-attempt one-shots from the engine event stream (position-free). An attempt on goal fires
                // once as the highlight reaches the strike, with a headed attempt sounding its own clip.
                // Emitted BEFORE the defence and goal blocks so that when a strike and its outcome land in the
                // same tick the player still hears them in the order they happened: the attempt, then the save
                // or the goal. Each arbiter baselines, replay-freezes, and dedupes only its own kind's engine
                // moment, so a rebound sounds again. When a kick and a header both land in one tick they are
                // emitted in engine-time order, for the same reason.
                bool kickFired = eventWindow.Shot.Observe(
                    shot.Ok, shot.LastEventIndex, shot.InReplay, shot.KickOccurred, shot.KickSlice) != null;
                bool headerFired = eventWindow.Header.Observe(
                    shot.Ok, shot.LastEventIndex, shot.InReplay, shot.HeaderOccurred, shot.HeaderSlice) != null;
                if (kickFired || headerFired)
                {
                    bool headerFirst = kickFired && headerFired && shot.HeaderSlice < shot.KickSlice;
                    if (headerFirst)
                    {
                        EmitSfx(stream, "headerattempt", suppressSfx, ref sent);
                        EmitSfx(stream, "shotattempt", suppressSfx, ref sent);
                    }
                    else
                    {
                        if (kickFired) EmitSfx(stream, "shotattempt", suppressSfx, ref sent);
                        if (headerFired) EmitSfx(stream, "headerattempt", suppressSfx, ref sent);
                    }
                }

                // Good-defence one-shots from the engine event stream (position-free). A successful / impactful
                // defensive event (interception, tackle won, save, block, ball won back) fires once as the
                // highlight reaches it: "gooddefence" when the reference team made the play, "oppgooddefence"
                // when the opposition did. The pure state machine baselines, replay-freezes, and same-moment-
                // dedupes; the caller holds the played high-water via eventWindow.Defence.HighWater.
                string defenceSfx = eventWindow.Defence.Observe(
                    defence.Ok, defence.LastEventIndex, defence.InReplay, defence.Occurred, defence.ForRef, defence.Slice);
                if (defenceSfx != null)
                {
                    if (suppressSfx) { EventLog.Record("sfxmuted", defenceSfx); }
                    else
                    {
                        WriteSse(stream, $"event: sfx\ndata: {defenceSfx}\n\n");
                        EventLog.Record("sfx", defenceSfx);
                        sent = true;
                    }
                }

                // Goal one-shots from the engine event stream (position-free). The pure state machine baselines
                // and latches, and carries a second bookmark across the goal's replay so the replayed goal
                // sounds too — see Navigation.PlayedEventCursor.
                string goalSfx = eventWindow.Goal.Observe(goal.Ok, goal.LastEventIndex, goal.InReplay, goal.Crossed, goal.ForRef);
                if (goalSfx != null)
                {
                    // A real goal: the ball is dead through the celebration however the net-line
                    // geometry flickers, so the possession ticks latch quiet until the kick-off
                    // releases them, and the touch claim starts over — possession after a
                    // goal belongs to whoever the kick-off says, not to the scorer.
                    // The replayed goal sounds but changes none of this: the replay is a second pass
                    // over ground already played, so re-latching on it would re-stamp the quiet's
                    // engine time with the replay's and reset a possession claim that belongs to the
                    // live celebration.
                    if (!inReplay)
                    {
                        goalQuiet = true;
                        goalQuietSlice = goal.Slice;
                        goalQuietLiveSinceMs = 0;
                        TouchPossession.Reset("goal");
                    }
                    // A goal's event reaches playback inside its own highlight (highlights lead up to
                    // the moment), not during fast-forward, so this mute never swallows a real goal
                    // sound; the record would show it if that assumption ever breaks.
                    if (suppressSfx) { EventLog.Record("sfxmuted", goalSfx); }
                    else
                    {
                        WriteSse(stream, $"event: sfx\ndata: {goalSfx}\n\n");
                        EventLog.Record("sfx", goalSfx);
                        sent = true;
                    }
                }
                // Post-goal quiet release, decided by ENGINE TIME rather than arrival order: a
                // put-in-play at or after the goal's slice is the kick-off (or later), even when
                // fast-forward delivered the goal and the kick-off in the same tick; one from before
                // the goal's slice is an older restart the same span happened to carry, and must not
                // release. Evaluated after the goal branch so a same-tick pair is compared, not raced.
                // A replayed take is not the kick-off — it is an earlier restart being shown again — so
                // only a live ping releases; the replay suspends the quiet on its own while it runs.
                if (goalQuiet && putPing && !inReplay && restart.PutSlice >= goalQuietSlice)
                    goalQuiet = false;
                if (fullTick && !sent)
                    WriteSse(stream, ": heartbeat\n\n");
                // Sleep to the next DEADLINE, not for a flat interval. Thread.Sleep(SubTickMs) made the
                // period "work + SubTickMs + however far the scheduler overshoots", and Windows' ~15.6ms
                // timer granularity rounds every sleep up to the next tick: /postrace measured a median
                // period of 141ms (125 + 15.6) with a p90 of 157ms (125 + 31.2), so the loop essentially
                // never ran at its nominal rate. Nothing broke, because every threshold in the cue chain is
                // compared against timestamps rather than counted in samples — but a 250ms dwell really
                // confirmed at ~282ms, so the constants tuned by ear did not mean what they said.
                //
                // Sleeping until the next multiple of SubTickMs absorbs the work into the interval and lets
                // a long frame be followed by a short sleep instead of pushing the whole schedule back. A
                // deadline already missed sleeps zero rather than going negative.
                nextTickAt += SubTickMs;
                long wait = nextTickAt - Environment.TickCount64;
                // Far behind (a long stall, a resumed pause): give up on the missed deadlines rather than
                // spinning through them with no sleep at all, and restart the schedule from now.
                if (wait < -SubTickMs) { nextTickAt = Environment.TickCount64 + SubTickMs; wait = SubTickMs; }
                if (wait > 0) Thread.Sleep((int)wait);
            }
        }
        catch (Exception ex)
        {
            // Normal lifecycle: the browser closing the page kills the socket. The lifetime tells
            // churn (sub-second, repeated) apart from an occasional drop or a clean session close.
            long lifeMs = Environment.TickCount64 - connStart;
            ServerText.LogBackground($"SSE end #{connId} after {lifeMs}ms: {ex.Message}");
        }
        finally { client.Close(); }
    }

    private static void WriteSse(NetworkStream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Push one one-shot match sound on the 'sfx' channel, or record that it was swallowed while the game is
    /// fast-forwarding. <paramref name="sent"/> is raised so the caller's tick counts as having written.
    /// </summary>
    private static void EmitSfx(NetworkStream stream, string token, bool suppressed, ref bool sent)
    {
        if (suppressed) { EventLog.Record("sfxmuted", token); return; }
        WriteSse(stream, $"event: sfx\ndata: {token}\n\n");
        EventLog.Record("sfx", token);
        sent = true;
    }
}
