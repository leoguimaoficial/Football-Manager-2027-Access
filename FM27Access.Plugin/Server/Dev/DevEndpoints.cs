using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Registers the dev-driver routes on the existing accessibility server's route table, the same
/// way FeatureRegistry contributes the companion feature routes. The dev driver lets an agent
/// drive and inspect the live game over loopback HTTP:
///   POST /eval        body = C# source, run against the live game (REPL state persists);
///                     returns output + result/errors. The universal "run any part of the game"
///                     capability — strictly more powerful than any fixed verb set.
///   POST /input       body = verb (up|down|left|right|confirm|cancel|activate). Drives FM26's
///                     own focus system natively (see GameDriver).
///   POST /cursor      body = verb (up|down|left|right|first|last|read). Drives the mod's OWN
///                     accessibility cursor (the arrow-key speech surface), via CursorDriver.
///   GET  /speech?since=N      lines the mod has spoken since cursor N (we cannot hear the TTS).
///   GET  /log?since=N         the mod's Log.* output since cursor N (warnings/errors over HTTP).
///   GET  /events/log?since=N  the SSE events pushed to the companion (commentary/values/score/
///                     screen/restructure) - the live announcements the player hears that /speech
///                     misses. See EventLog.
///   GET  /read        the whole interactive screen (every interactive panel) linearized to a
///                     screen-reader-style text block - what the companion shows, in reading order,
///                     for auditing menus (see ReadingView).
///   GET  /rendered    the companion app's LIVE rendered DOM (#fmroot) — the served /body plus every
///                     client-side SSE morph, i.e. exactly what the player's screen reader sees, which
///                     can drift from a fresh /body. app.js reports it via POST /rendered/report.
///   GET  /gui         raw VisualElement tree of the active panel (see Introspect).
///   GET  /classify    the mod's classification of every interactive panel (stops + announcements).
///   GET  /audit       categorised accessibility-defect report for the active screen (the generated
///                     worklist: empty-name / degrade / image / orphan-label / dup-adjacent + leads).
///   GET  /panels      raw tree of every open panel across all layers.
///   GET  /unclassified  the stateful unclassified-element tracker; ?clear=1 resets it (F8).
///   GET  /screenshot  capture a PNG and return its path for the agent to read.
///   GET  /health      liveness for the bring-up poll.
///
/// Eval / input / cursor / gui / classify / panels / unclassified run on the Unity main thread via
/// MainThreadDispatcher.Invoke. /read runs on the request thread (its streamed-table load sleeps
/// between frames, like /body). /speech, /log and /events/log read their thread-safe ring buffers
/// directly off the request thread. Always on; binds loopback only, like the rest of the server.
/// </summary>
internal static class DevEndpoints
{
#if FM27_DEVELOPER_TOOLS
    private static SpeechLog _speech;
    private static SpeechLog _log;
#endif

    // The companion app's live rendered DOM (#fmroot innerHTML), posted by app.js after each render
    // (POST /rendered/report) and returned by GET /rendered. Lets an agent read EXACTLY what the
    // companion shows — the served /body plus every client-side SSE morph — which can drift from a
    // fresh /body fetch. A plain volatile ref: each report is one atomic reference assignment, so a
    // concurrent GET reads either the old or the new whole string, never a torn one. null until a
    // running companion reports (a headless run with no window never does).
#if FM27_DEVELOPER_TOOLS
    private static volatile string _renderedDom;
    private static volatile string _renderedFocus = "";

    // The evaluator is created lazily on the first /eval call, not eagerly, so that loading
    // Mono.CSharp (the plan's one real unknown under IL2CPP / CoreCLR) can only ever break /eval
    // — a load failure returns a diagnostic from that one request and never sinks the server, the
    // companion surface, or the other dev endpoints. Created and used only on the main thread.
    private static CSharpEvaluator _evaluator;
#endif

    // The plugin hands over the speech buffer (also wired as SpeechPipeline.Observer) and the log
    // buffer (chained into Log's backends) after the server starts. Read at request time, so
    // registration order does not matter.
#if FM27_DEVELOPER_TOOLS
    public static void Attach(SpeechLog speech, SpeechLog log)
    {
        _speech = speech;
        _log = log;
    }
#endif

    public static void RegisterRoutes(Dictionary<string, Route> routes)
    {
        routes["/health"] = new Route
        {
            ContentType = "application/json",
            LoadingBody = RuntimeSurface.Health,
            Handler = (q, b) => (RuntimeSurface.Health(), null),
        };
#if FM27_DEVELOPER_TOOLS
        routes["/log"] = new Route
        {
            ContentType = "text/plain",
            // AlwaysReady (ring-buffer read): lets an agent read the mod log during loading and during
            // a failed Mount — which is how you diagnose why a reload's Mount threw.
            AlwaysReady = true,
            LoadingBody = () => "cursor: 0\n",
            Handler = (q, b) =>
            {
                long.TryParse(Query.Str(q, "since"), out long since);
                if (_log == null) return ("cursor: 0\n", null);
                var lines = _log.Render(since, out var next);
                return ("cursor: " + next + "\n" + lines, null);
            },
        };
#endif
        routes["/navigator/go"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => (NavigatorIndex.Go(Query.Str(q, "uid"), Query.Str(q, "obj")), (string)null),
        };
#if FM27_DEVELOPER_TOOLS
        routes["/httptrace"]=new Route
        {
            ContentType="application/json",AlwaysReady=true,
            Handler=(q,b)=>(System.Text.Json.JsonSerializer.Serialize(AccessibilityServer.RequestTrace.Snapshot()),null)
        };
        // Health answers even before the game is interactive so the bring-up poll knows the
        // server is up; the loading body short-circuit returns "ok" too.


        routes["/speech"] = new Route
        {
            ContentType = "text/plain",
            // Reads a thread-safe ring buffer, no live game state — safe before Ready and during a
            // failed Mount, so an agent can read what was spoken regardless of mod state.
            AlwaysReady = true,
            LoadingBody = () => "cursor: 0\n",
            Handler = (q, b) =>
            {
                long since = 0;
                long.TryParse(Query.Str(q, "since"), out since);
                if (_speech == null) return ("cursor: 0\n", null);
                var lines = _speech.Render(since, out var next);
                return ("cursor: " + next + "\n" + lines, null);
            },
        };

        // The mod's own Log.* output (Info/Warn/Error/Fatal), same cursor protocol as /speech, so
        // an agent can read warnings/errors without opening Player.log.


        // The SSE events pushed to the companion (commentary, values, score, screen, restructure).
        // Captures the live announcements the player hears via the companion's aria-live region,
        // which never reach Prism and so are absent from /speech. Same cursor protocol.
        routes["/events/log"] = new Route
        {
            ContentType = "text/plain",
            // AlwaysReady (ring-buffer read): the pushed-events history needs no live game state.
            AlwaysReady = true,
            LoadingBody = () => "cursor: 0\n",
            Handler = (q, b) =>
            {
                long.TryParse(Query.Str(q, "since"), out long since);
                var lines = EventLog.Render(since, out var next);
                return ("cursor: " + next + "\n" + lines, null);
            },
        };

        routes["/eval"] = new Route
        {
            ContentType = "text/plain",
            Method = "POST",
            LoadingBody = () => "The game is still loading.\n",
            // Eval can be slower than the 2s default — game calls, reflection, compilation. The
            // first call constructs the evaluator (and so loads mcs) on the main thread inside the
            // guarded Invoke; a load failure surfaces in the response, not at startup.
            Handler = (q, b) => string.IsNullOrWhiteSpace(b)
                ? ("[empty] POST C# source as the request body\n", (string)null)
                : OnMainThread(() => { _evaluator ??= new CSharpEvaluator(); return _evaluator.Eval(b); }, 30000),
        };

        routes["/input"] = new Route
        {
            ContentType = "text/plain",
            Method = "POST",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => GameDriver.Drive(b)),
        };

        // Drives the mod's accessibility cursor (the arrow-key speech surface), not the game's
        // native focus that /input drives. The move speaks through Prism, so read it back via /speech.
        routes["/cursor"] = new Route
        {
            ContentType = "text/plain",
            Method = "POST",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => CursorDriver.Drive(b)),
        };

        // Probe the game's bookmark-mapping popup (open/state/dump/rows/click/close). Groundwork
        // for the bookmark-mapping feature; see BookmarkPopupProbe and docs/bookmark-mapping-plan.md.
        routes["/bookmarkpopup"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() =>
                BookmarkPopupProbe.Drive(Query.Str(q, "op"), Query.Str(q, "index")), 10000),
        };

        // Probe any SIFloatingPopupWidget (list/open/state/dump/rows/click/close) — the generic
        // sibling of /bookmarkpopup for scouting popups like table Filter widgets. See FloatingPopupProbe.
        routes["/popupprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() =>
                FloatingPopupProbe.Drive(Query.Str(q, "op"), Query.Str(q, "index"), Query.Str(q, "row")), 10000),
        };

        // Dev-only: the contract-decision split-button dropdown, whose selections the ordinary
        // TriggerSelectionEvent confirm path does not commit (see DecisionDropdownProbe).
        routes["/decisionprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() =>
                DecisionDropdownProbe.Drive(Query.Str(q, "op"), Query.Str(q, "index"), Query.Str(q, "row")), 10000),
        };

        // Dev-only: the Transfer Activity offer-detail block's bound property names, so a label for
        // one of its unlabelled values rests on the game's own naming rather than layout position
        // (see OfferDetailProbe).
        routes["/offerprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => OfferDetailProbe.Drive(Query.Str(q, "op")), 10000),
        };

        // Dev-only: the Position suitability visualiser's painted data model (see SuitabilityProbe).
        routes["/suitabilityprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(SuitabilityProbe.Run, 10000),
        };

        // Dev-only: the squad table's Inf column (SI.Bindable.ExpandingList "StatusFlags"), whose
        // collapsed form puts only its first badge in the tree. See StatusFlagsProbe.
        routes["/statusflagsprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => StatusFlagsProbe.Run(Query.Str(q, "mode")), 10000),
        };

        // Dev-only: the condition row's FMCombobox stepper (see ConditionComboProbe). Runs on the
        // request thread — it spans frames between its two main-thread hops.
        routes["/conditionprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => (ConditionComboProbe.Run(Query.Str(q, "row"), Query.Str(q, "op")), (string)null),
        };

        // Dev-only: line-chart series/data-point dump for the active panel (see ChartProbe).
        routes["/chartprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(ChartProbe.Run, 10000),
        };

        // Dev-only: the progress report's attribute rows and the bindings around its graph, hunting
        // a per-attribute identity the chart's unnamed series could be matched on (see AttributeRowProbe).
        routes["/attrprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(AttributeRowProbe.Run, 15000),
        };

        // Dev-only: the data every chart on the rendered panels is bound to (see ChartDataProbe).
        routes["/chartdata"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => ChartDataProbe.Run(Query.Int(q, "max"), Query.Str(q, "panel")), 15000),
        };

        // Dev-only: the binding system's own property request for a scatter point's person (see NameRequestProbe).
        routes["/namerequest"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => NameRequestProbe.Run(Query.Str(q, "op"), Query.Str(q, "path")), 15000),
        };

        // Dev-only: the open player scatter's points; ?click=N presses our Nth player (see PlayerScatterProbe).
        routes["/playerscatter"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => PlayerScatterProbe.Run(Query.Int(q, "click")), 15000),
        };

        // Dev-only: every visible image tallied by element and sprite name (see SpriteCensus).
        routes["/spritecensus"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => SpriteCensus.Run(Query.Str(q, "name")), 15000),
        };

        // Dev-only: the Data Hub radar's series sources (see RadarProbe).
        routes["/radarprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(RadarProbe.Run, 10000),
        };

        // Dev-only: the binding nodes and record fields above a drawn widget (see NodeProbe).
        routes["/nodeprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => NodeProbe.Run(Query.Str(q, "type"),
                Math.Max(0, Query.Int(q, "nth")), Query.Int(q, "depth") is var d && d >= 0 ? d : 3), 10000),
        };

        // Dev-only: the raw tree under named elements, past /gui's node cap (see NodeProbe.Subtree).
        routes["/subtree"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => NodeProbe.Subtree(Query.Str(q, "name"),
                Math.Max(0, Query.Int(q, "nth")), Math.Max(1, Query.Int(q, "count"))), 10000),
        };

        // Dev-only: the Form column sparkline's backing ratings (see FormChartProbe).
        routes["/formprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(FormChartProbe.Run, 10000),
        };

        // Dev-only: the standings table's team Form pips — model, binding nodes, drawn colours (see TeamFormProbe).
        routes["/teamformprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => TeamFormProbe.Run(Query.Int(q, "max") is var m && m > 0 ? m : 4), 10000),
        };

        // Dev-only: reverse lookup of runtime TranslationID GameIDs by exact text (see LocScanProbe).
        routes["/locscan"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => LocScanProbe.Run(Query.Str(q, "q"),
                Math.Max(0, Query.Int(q, "from")), Query.Int(q, "to") is var t && t > 0 ? t : 200000), 60000),
        };

        // Dev-only: every SIDropdown on the active panel with the game's own choice count, to tell a
        // genuinely short menu from one the surface under-reports (see DropdownCountProbe).
        // Dev-only: table multi-selection state + the game's own range-select entry, the
        // feasibility probe for a mod-authored "Select all" button (see SelectionProbe).
        routes["/selprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => (SelectionProbe.Run(Query.Str(q, "op"), Query.Str(q, "index")), (string)null),
        };

        // Dev-only: realized rows of the currently open dropdown popup — absolute index, layout
        // height/position, label read — for diagnosing virtualised-popup harvest stalls
        // (see DropdownSessionManager.DescribeOpenTop).
        routes["/ddwindow"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => (DropdownSessionManager.DescribeOpenTop(), (string)null),
        };

        routes["/ddprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(DropdownCountProbe.Run, 10000),
        };

        // Dev-only: the Mentoring card's relationship diagram — arrow style classes and bindings
        // (see MentoringProbe).
        routes["/mentoringprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(MentoringProbe.Run, 10000),
        };

        // Dev-only: the possession cue's decision chain sampled over live play, for diagnosing tick
        // dropouts (see PossessionTrace). ?on=1 / ?off=1 control recording, ?raw=N dumps samples.
        // No main-thread marshal: it reads an in-memory ring the SSE loop fills, nothing in the tree.
        routes["/postrace"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => (PossessionTrace.Run(
                Query.Str(q, "on") == "1", Query.Str(q, "off") == "1", Query.Int(q, "raw")), null),
        };

        // Dev-only: the message inbox rows' field reads; ?blank=1 clears the first row's headline
        // visual to exercise the unreadable-subject degrade path (see MessageProbe).
        routes["/msgprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => MessageProbe.Run(Query.Str(q, "blank") == "1",
                Query.Str(q, "settext"), Query.Str(q, "blank") == "all"), 10000),
        };

        // Dev-only: the Routine Frequency popup's rows — checkbox values vs binding model (see FreqProbe).
        routes["/freqprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(FreqProbe.Run, 10000),
        };

        // Dev-only: the analysis zone maps' binding model and grid nesting (see ZoneProbe).
        routes["/zoneprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(ZoneProbe.Run, 10000),
        };

        // Dev-only: the date selector widget's controller, month set and day grid (see DateProbe).
        // GET ?open=1 / ?close=1 drive the popup, ?year=YYYY lists that year's enabled days.
        routes["/dateprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => DateProbe.Run(
                Query.Str(q, "open") == "1", Query.Str(q, "close") == "1", Query.Int(q, "year"),
                new DateProbe.Steps
                {
                    // Query.Int reports an absent key as -1, which is a legitimate stepmonth value,
                    // so the presence check goes through Str (empty when the key is absent).
                    SetYear = Query.Int(q, "setyear"),
                    StepMonth = Query.Str(q, "stepmonth") == "" ? 0 : Query.Int(q, "stepmonth"),
                    Pick = Query.Str(q, "pick"),
                    Confirm = Query.Str(q, "confirm") == "1",
                }), 10000),
        };

        // Dev-only: raw engine values behind the Alt+4 match-conditions readout (see ConditionsProbe).
        routes["/conditionsprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(ConditionsProbe.Run, 10000),
        };

        // Dev-only: the last-touch possession system (the STANDARD tick driver) — report its state,
        // or flip back to the legacy geometric arbiter with ?touch=0 for an A/B. No main-thread
        // marshal: everything read is mod state.
        routes["/posmode"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) =>
            {
                var flip = Query.Str(q, "touch");
                if (flip == "1" || flip == "0")
                {
                    bool want = flip == "1";
                    bool changed = TouchPossession.Enabled != want;
                    TouchPossession.Enabled = want;
                    // Spoken confirmation on every flip, so which system is driving the ticks is never
                    // in doubt — the announcement traverses the real speech pipeline, proving the
                    // reloaded core is the one serving.
                    if (changed)
                        ServerHandles.Speak(want ? "possession ticks: standard touch system"
                                                 : "possession ticks: legacy geometric system");
                }
                var (holder, restarter, fresh) = TouchPossession.Snapshot(Environment.TickCount64);
                var (attributed, ambiguous, unattributed, expiries, lastEvidence, heartbeat) = TouchPossession.Stats();
                var (maxTick, touches, resets) = TouchSampler.Diagnostics();
                var (decisiveFlips, decisiveReverts) = TouchPossession.DecisiveStats();
                long now = Environment.TickCount64;
                return ($"mode: {(TouchPossession.Enabled ? "TOUCH (standard)" : "geometric arbiter (LEGACY fallback)")}\n"
                     + $"holder: {holder}  restarter: {restarter}  fresh: {fresh}\n"
                     + $"heartbeat: {(heartbeat == 0 ? "never" : now - heartbeat + "ms ago")}  "
                     + $"last evidence: {(lastEvidence == 0 ? "never" : now - lastEvidence + "ms ago")}\n"
                     + $"touches seen: {touches}  attributed: {attributed}  ambiguous-held: {ambiguous}  "
                     + $"unattributed-held: {unattributed}\n"
                     + $"decisive flips: {decisiveFlips}  reverted as blocks: {decisiveReverts}\n"
                     + $"scramble expiries: {expiries}  window resets: {resets}  "
                     + $"sampler max: {maxTick:0.0}ms/frame\n", (string)null);
            },
        };

        // Dev-only: the last-touch system's evidence journal (see
        // TouchJournal). Same cursor protocol as /speech; a ring read, so no main-thread marshal.
        routes["/touchlog"] = new Route
        {
            ContentType = "text/plain",
            AlwaysReady = true,
            LoadingBody = () => "cursor: 0\n",
            Handler = (q, b) =>
            {
                long.TryParse(Query.Str(q, "since"), out long since);
                var lines = TouchJournal.Render(since, out var next);
                return ("cursor: " + next + "\n" + lines, null);
            },
        };

        // Dev-only: the engine commentary event log behind the events readout (see MatchEventsProbe).
        routes["/eventsprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => MatchEventsProbe.Run(
                Query.Int(q, "slice"), Query.Int(q, "window"), Query.Str(q, "all") == "1"), 10000),
        };

        // Dev-only: the animation layer's ball-contact buffer (see BallContactsProbe).
        routes["/contactsprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => Query.Str(q, "scan") == "1"
                ? BallContactsProbe.Scan(
                    Query.Str(q, "from") == "" ? 0f : float.Parse(Query.Str(q, "from")),
                    Query.Str(q, "to") == "" ? 0f : float.Parse(Query.Str(q, "to")),
                    Query.Str(q, "step") == "" ? 0f : float.Parse(Query.Str(q, "step")),
                    Query.Str(q, "raw") == "1")
                : BallContactsProbe.Run(), 10000),
        };

        // Dev-only: the pitch shirts' binding model (see ShirtProbe).
        routes["/shirtprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(ShirtProbe.Run, 10000),
        };

        // Dev-only: hunt a free source for a player instruction's option-level locks - the tile's
        // own binding node versus the edit card's radios (see PiLockProbe). GET ?key=AttackingWidth
        routes["/pilockprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => Query.Str(q, "qc") != ""
                ? (PiLockProbe.RunQuickChange(Query.Str(q, "key"), Query.Str(q, "qc"), Query.Int(q, "times")), (string)null)
                : Query.Str(q, "arrow") != ""
                ? (PiLockProbe.RunArrow(Query.Str(q, "key"), Query.Str(q, "arrow"), Query.Int(q, "times")), (string)null)
                : Query.Str(q, "sweep") == "1"
                ? (PiLockProbe.RunSweep(), (string)null)
                : Query.Str(q, "open") == "1"
                ? (PiLockProbe.RunWithEditor(Query.Str(q, "key"), Query.Int(q, "depth"), Query.Str(q, "hold") == "1"), (string)null)
                : OnMainThread(() => PiLockProbe.Run(Query.Str(q, "key"), Query.Int(q, "depth")), 15000),
        };

        // Dev-only: where the squad-table PI button's real click payload lives (see PiButtonProbe).
        // GET ?row=N
        routes["/pibtnprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => Query.Str(q, "send") == "1"
                ? OnMainThread(() => PiButtonProbe.Send(Query.Int(q, "row")), 15000)
                : Query.Str(q, "list") == "1"
                ? OnMainThread(PiButtonProbe.ListRows, 15000)
                : Query.Str(q, "node") == "1"
                ? OnMainThread(() => PiButtonProbe.DumpNode(Query.Int(q, "row")), 15000)
                : OnMainThread(() => PiButtonProbe.Run(Query.Int(q, "row")), 15000),
        };

        // Dev-only: the position-selector pitch's selection model (see PositionSelectorProbe).
        // Dev-only: the per-position suitability behind a selector pitch's slots — what the game
        // paints each pitch dot with, and what an accessible position table would have to read.
        // See PositionSuitabilityProbe. GET ?pitch=N
        routes["/suitpositions"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => PositionSuitabilityProbe.Run(Query.Int(q, "pitch")), 10000),
        };

        routes["/positionprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => PositionSelectorProbe.Run(
                Query.Str(q, "click"), Query.Str(q, "any") == "1", Query.Str(q, "submit"),
                Query.Int(q, "pitch")), 10000),
        };

        // Dev-only: the Navigator's live enumeration of the game's navigation tree (categories,
        // subsections, dropdown children) with availability flags. See NavigatorIndex and
        // docs/navigator-plan.md.
        routes["/navigator/list"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(NavigatorIndex.DescribeText, 10000),
        };

        // Dev-only for phase 2 (the phase-3 surface will call the same Go): navigate to a
        // /navigator/list destination by uid (+obj for object-bound entries). Go marshals to the
        // main thread itself, so no OnMainThread wrapper here.


        routes["/gui"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            // ?textlen=N raises the per-node text cap when the truncated tail is what you came for.
            Handler = (q, b) => OnMainThread(() => Introspect.DumpTree(Query.Int(q, "textlen")), 10000),
        };

        // Diagnostic: what a virtualised table cell looks like at activation time — the realised
        // element's rect against the list viewport, whether it is clipped, and whether a name
        // link's markup has bound yet. The trail for "the click announced the right row and did
        // nothing / opened the neighbour". GET ?id=
        routes["/cellprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            // ?fire=N additionally activates the cell on frame N, so an agent can find how many
            // frames after the scroll the game actually honours the click.
            Handler = (q, b) => (ServerHandles.ProbeCell(Query.Str(q, "id"), Query.Int(q, "fire")), (string)null),
        };

        // Diagnostic: scroll a fixture-schedule row in by data index and dump its raw realized
        // subtree + item model type. GET ?id=<any FixtureRowHandle>&idx=<data index, optional>
        routes["/fixrowprobe"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => (ServerHandles.ProbeFixtureRow(Query.Str(q, "id"), Query.Int(q, "idx")), (string)null),
        };

        // Diagnostic: full ancestry of a rendered handle (the context-menu walk-up, unbounded). GET ?id=
        routes["/ctxancestry"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => (ServerHandles.DescribeAncestry(Query.Str(q, "id")), (string)null),
        };

        // Diagnostic: any ContextMenuDescriptionManipulator (the universal trigger) in a handle's
        // click-target ancestry, and whether it is armed (SET) vs present-but-null. GET ?id=
        routes["/ctxtrigger"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => (ServerHandles.DescribeMenuTrigger(Query.Str(q, "id")), (string)null),
        };

        // Diagnostic: every menu mark the active screen would render, with the holder evidence behind
        // it and ContextMenuScope's verdict on each pairing. Sweep it across the ribbon to get a
        // game-wide baseline, then diff after a scoping change. GET, no args.
        routes["/ctxcensus"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            // Not OnMainThread: like /read, the streamed-table phase sleeps between frames, so it must
            // run on the request thread and marshal its own main-thread phases.
            Handler = (q, b) =>
            {
                try { return (ContextMenuCensusReport.Run(), (string)null); }
                catch (Exception ex) { return ("Error: " + ex.Message + "\n", (string)null); }
            },
        };

        // Diagnostic: the context-menu system's live state (context bound?, popup show state, pending
        // and in-tree entry lists). Poll around an open/close sequence to catch partial fills and
        // unclean closes. GET, no args.
        routes["/ctxstate"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            // ?tree=N also dumps the description as a nested tree N levels deep — the only way to tell
            // a folder the GAME left empty from one the surface failed to reach.
            Handler = (q, b) => (ServerHandles.DescribeMenuState(Query.Int(q, "tree")), (string)null),
        };

        // /classify: the mod's classification of the active panel (stops + announcements, the F9
        // f10_classify view inline). /panels: the raw tree of every open panel across all layers,
        // which /gui and /body (active-panel-only) never show.
        routes["/classify"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(Introspect.DumpClassification, 10000),
        };

        routes["/panels"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() => Introspect.DumpAllPanels(Query.Int(q, "textlen")), 15000),
        };

        // The accessibility audit: walks the interactive set as the HTML surface renders it and
        // buckets every stop into the recurring defect flavours (empty-name, degrade-marker,
        // image-stop, orphan-label, dup-adjacent) plus open-set LEADS (id-shaped names, the
        // /unclassified count). The generated worklist that replaces eyeballing /classify vs /body;
        // re-run after a structural fix to confirm a category cleared across every screen. See Audit.
        routes["/audit"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(Audit.RunActive, 15000),
        };

        // The unclassified-element tracker: the stateful accumulation of element types the classifier
        // did not recognize across every screen visited (what /classify, a single-panel snapshot,
        // cannot show). ?clear=1 resets it (the F8 equivalent). Runs on the main thread because the
        // tracker is written there during classification.
        routes["/unclassified"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => OnMainThread(() =>
            {
                if (Query.Str(q, "clear") == "1")
                {
                    UnclassifiedTracker.Entries.Clear();
                    return "cleared\n";
                }
                return UnclassifiedTracker.RenderText();
            }),
        };

        // The whole active screen linearized to a screen-reader-style text block - what the companion
        // presents, in reading order, for auditing whether a menu reads logically. Built from the
        // same pipeline as /body (streamed tables materialized), so unlike the other dev views it runs
        // on the request thread (the table load sleeps between frames) rather than via OnMainThread.
        routes["/read"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) =>
            {
                try { return (ReadingView.Compose(), (string)null); }
                catch (Exception ex) { return ("[host error] " + ex + "\n", (string)null); }
            },
        };

        // The companion app's LIVE rendered DOM, as the player's screen reader actually sees it: the
        // served /body plus every client-side SSE morph (live values, dropdown popups, idiomorph
        // patches), which can diverge from a fresh /body fetch. app.js reports #fmroot after each
        // change via POST /rendered/report; this returns the latest. Empty until a companion window is
        // open and has rendered. Use it to diagnose "I see X in the app but /body shows Y" reports.
        routes["/rendered"] = new Route
        {
            ContentType = "text/html",
            // AlwaysReady: a stored string, no live game state — readable during loading or a failed Mount.
            AlwaysReady = true,
            LoadingBody = () => "",
            Handler = (q, b) =>
            {
                var dom = _renderedDom;
                if (string.IsNullOrEmpty(dom))
                    return ("[no companion DOM reported yet - open the companion window; it reports #fmroot after each render]\n", (string)null);
                // The focused element id, reported alongside the DOM, as a trailing comment — so an
                // agent can verify where the companion's browser focus actually sits (focus is not
                // part of innerHTML). Empty id = focus is on <body>/nowhere.
                return (dom + "\n<!--fm-focus:" + _renderedFocus + "-->", (string)null);
            },
        };

        // app.js POSTs #fmroot's innerHTML here (debounced) after each render, with the focused
        // element's id in ?focus=. Body is the DOM string.
        routes["/rendered/report"] = new Route
        {
            ContentType = "text/plain",
            Method = "POST",
            AlwaysReady = true,
            LoadingBody = () => "ok\n",
            Handler = (q, b) => { _renderedDom = b; _renderedFocus = Query.Str(q, "focus"); return ("ok\n", (string)null); },
        };

        routes["/screenshot"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => (Screenshot(), null),
        };

        // Hot-reload the core assembly in place. Rebuild FM27Access.Plugin.dll, POST /reload, then
        // poll /health until the server is back, and the new code is live without restarting the
        // game. The host runs the actual reload (Unmount -> load fresh bytes -> Mount) on the main
        // thread off this request thread, so the "reloading" response goes out before the server
        // socket is torn down. See docs/hot-reload-plan.md.
        routes["/reload"] = new Route
        {
            ContentType = "text/plain",
            Method = "POST",
            // AlwaysReady: the reload trigger must work even when the mod never reached Ready (e.g. a
            // prior reload's Mount threw). The handler only posts to the host pump; it touches no live
            // game state, so it is safe to run before the dispatcher is draining.
            AlwaysReady = true,
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) =>
            {
                CoreEntry.RequestReload();
                return ("reloading\n", (string)null);
            },
        };
#endif
    }

#if FM27_DEVELOPER_TOOLS
    // Run main-thread game work for a route and wrap it in the dev driver's uniform error
    // envelope, so /eval, /input and /gui share one guard instead of each repeating the
    // try/Invoke/catch. A dispatch timeout or thrown game call comes back as "[host error] ...".
    private static (string body, string header) OnMainThread(Func<string> work, int timeoutMs = 2000)
    {
        try { return (MainThreadDispatcher.Invoke(work, timeoutMs), null); }
        catch (Exception ex) { return ("[host error] " + ex + "\n", null); }
    }

    // Trigger a screenshot on the main thread, then wait (on this request thread) for the PNG,
    // which ScreenCapture writes asynchronously over the next frame(s). Returns the path, which
    // the agent then reads to view the frame. Works while the game is unfocused because
    // Application.runInBackground is forced true in HostPlugin.Load.
    private static string Screenshot()
    {
        string path = Path.Combine(Path.GetTempPath(), "fm26_shot.png");
        try
        {
            MainThreadDispatcher.Invoke(() =>
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (Exception ex) { Log.Warn($"[DevEndpoints] Could not delete stale screenshot, may return a previous frame: {ex.Message}"); }
                UnityEngine.ScreenCapture.CaptureScreenshot(path);
                return true;
            });
        }
        catch (Exception ex) { return "[host error] " + ex + "\n"; }

        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < 8)
        {
            try
            {
                if (File.Exists(path))
                {
                    long size = new FileInfo(path).Length;
                    if (size > 0)
                    {
                        Thread.Sleep(60); // let the write settle, then confirm size is stable
                        if (new FileInfo(path).Length == size) return path + "\n";
                    }
                }
            }
            // The file is mid-write for the first frames (locked / zero-length); that is the
            // expected state this loop polls through, so swallow and retry rather than log 50ms.
            catch { }
            Thread.Sleep(50);
        }
        return "[timeout] screenshot not written within 8s\n";
    }
#endif
}
