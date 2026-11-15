using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;
#if FM27_DEVELOPER_TOOLS
using RequestTraceEntry = FM27Access.Plugin.Server.HttpRequestTrace.Entry;
#else
using RequestTraceEntry = System.Object;
#endif

namespace FM27Access.Plugin.Server;

/// <summary>
/// SPIKE: a localhost HTTP server that exposes the current game screen as semantic
/// HTML so an external browser (and the future companion app's WebView2) renders it
/// and a screen reader navigates it in browse mode. Interactions post back and run
/// against the live game element on the main thread.
///
/// Raw TcpListener on 127.0.0.1 — no HttpListener (URL-ACL/admin friction) and no new
/// assemblies. The page is rebuilt live per request, never cached. This class owns only
/// the socket lifecycle and request routing; rendering lives in <see cref="HtmlRenderer"/>,
/// the tree walk in <see cref="ScreenWalker"/>, and interactive handles in
/// <see cref="ServerHandles"/>.
/// </summary>
internal static class AccessibilityServer
{
    private const int Port = 8787;

    private static volatile TcpListener _listener;
    private static Thread _thread;
    private static volatile bool _running;
    private static readonly object ClientsGate = new();
    private static readonly HashSet<TcpClient> Clients = new();
#if FM27_DEVELOPER_TOOLS
    internal static readonly HttpRequestTrace RequestTrace=new();
#endif

    private static RequestTraceEntry BeginRequestTrace(int peerPort)
    {
#if FM27_DEVELOPER_TOOLS
        return RequestTrace.Begin(peerPort);
#else
        return null;
#endif
    }

    [System.Diagnostics.Conditional("FM27_DEVELOPER_TOOLS")]
    private static void TraceRequest(RequestTraceEntry entry, string phase, string route = null, string errorType = null)
    {
#if FM27_DEVELOPER_TOOLS
        RequestTrace.Mark(entry, phase, route, errorType);
#endif
    }

    // Endpoints that mutate the live panel in place. Navigation (/nav, /history) is excluded — it
    // already re-reads via the screen token. Session browsing (/dropdown/open|expand|..., search
    // open|filter) is excluded — the client defers refreshes during a session; only the commits
    // below change the panel.
    private static readonly HashSet<string> MutatingPaths = new HashSet<string>
    {
        "/activate", "/contextmenu", "/settext", "/setvalue", "/step",
        "/dropdown/select", "/searchdropdown/select", "/date/set", "/continue/press",
        "/tactics/move", "/save",
    };

    // Activate the panel profile for a panel during a multi-panel walk (§7.5). The active profile is
    // a single global shared with the speech surface, so any multi-panel build activates each panel's
    // profile before walking/rendering it and restores the speech panel's profile after. Passing null
    // (or a panel with no profile) clears it, since ActivateForPanel on an unknown name sets no
    // profile. Routed through the existing NavigationManager delegates — no new API. Shared with the
    // dev surfaces that walk the same interactive set (ReadingView's /read, Introspect's /classify).
    internal static void ActivateProfileFor(object panel)
    {
        string name = panel != null ? NavigationManager.GetPanelName(panel) : null;
        NavigationManager.RaiseActivePanelChanged(name ?? "");
    }

    public static void Start()
    {
        if (_running) return;
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            // A reload closes the old listener and rebinds immediately; don't claim exclusive use of
            // the port so the rebind can't trip "address already in use".
            _listener.ExclusiveAddressUse = false;
            _listener.Start();
        }
        catch (Exception ex)
        {
            Log.Error($"[AccessibilityServer] Failed to bind 127.0.0.1:{Port}: {ex.Message}");
            return;
        }

        _running = true;
        _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "FM27AccessServer" };
        _thread.Start();
        Log.Info($"[AccessibilityServer] Listening on http://127.0.0.1:{Port}/");
    }

    /// <summary>
    /// Stop the server and free the socket so the next Mount (after a reload) can rebind. Closing
    /// the listener unblocks the AcceptTcpClient call in the accept loop, which then exits because
    /// _running is false. SSE streamer threads see _running == false and end on their next tick.
    /// </summary>
    public static void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _listener?.Stop(); }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] Listener stop failed: {ex.Message}"); }
        _listener = null;
        TcpClient[] clients;
        lock (ClientsGate) { clients = new List<TcpClient>(Clients).ToArray(); Clients.Clear(); }
        foreach (var client in clients) client.Close();
        Log.Info("[AccessibilityServer] Stopped.");
    }

    private static void AcceptLoop()
    {
        long lastAcceptFailLogMs = 0;
        while (_running)
        {
            TcpClient client;
            try { client = _listener.AcceptTcpClient(); }
            catch (Exception ex)
            {
                if (!_running) break;
                // A persistent accept failure would otherwise spin this loop silently at full
                // speed: name it (rate-limited — the same fault repeats fast) and back off a beat.
                long now = Environment.TickCount64;
                if (now - lastAcceptFailLogMs >= 5000)
                {
                    lastAcceptFailLogMs = now;
                    ServerText.LogBackground($"accept failed: {ex.GetType().Name}: {ex.Message}");
                }
                Thread.Sleep(50);
                continue;
            }

            // Serve each bounded persistent connection on its own worker (as /events already does)
            // so a slow or long-running request — a multi-second /eval, a streamed /body load —
            // never blocks the others. Safe because MainThreadDispatcher serialises all real game
            // access: shared server state (ServerHandles, dropdown sessions) is only ever touched
            // inside Invoke (on the main thread), and the settle-window fields are volatile.
            // Record acceptance BEFORE starting its worker. A missing HTTP
            // trace must distinguish a failed handshake/accept from a worker
            // which has not started yet; no headers, values or secrets here.
            int peerPort = 0;
            try { peerPort = (client.Client.RemoteEndPoint as IPEndPoint)?.Port ?? 0; }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            var trace = BeginRequestTrace(peerPort);
            TraceRequest(trace, "accepted");
            lock (ClientsGate) Clients.Add(client);
            var worker = new Thread(() => ServeClient(client, trace)) { IsBackground = true, Name = "FM27AccessReq" };
            worker.Start();
        }
    }

    private static void ServeClient(TcpClient client, RequestTraceEntry trace)
    {
        TraceRequest(trace, "reading");
        bool keepOpen = false;
        string requestLabel = "before headers";
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            client.NoDelay = true;
            client.ReceiveTimeout = 2000;
            client.SendTimeout = 10000;
            var reader = new HttpRequestReader.ConnectionReader(client.GetStream());
            for (int index = 0; _running && index < 256; index++)
            {
                if (index != 0)
                {
                    // Idle pooling is bounded; active headers/body retain the
                    // existing2s read timeout. Buffered pipelining is sequential.
                    if (!reader.HasBufferedData && !client.Client.Poll(15_000_000, SelectMode.SelectRead)) break;
                    int peer = (client.Client.RemoteEndPoint as IPEndPoint)?.Port ?? 0;
                    trace = BeginRequestTrace(peer);
                    TraceRequest(trace, "reading");
                }
                requestLabel = "before headers";
                elapsed.Restart();
                var request = reader.Read();
                if (request == null) { TraceRequest(trace, "connection closed"); break; }
                string path = request.Target.Split('?')[0];
                bool persistent = request.Persistent && index < 255 && path is not "/events" and not "/reload";
                var response = new HttpResponseWriter(client.GetStream(), persistent, request.Method == "HEAD");
                keepOpen = HandleRequest(client, request, response, ref requestLabel, trace);
                TraceRequest(trace,keepOpen?"stream handed off":"response written",requestLabel);
                if (elapsed.ElapsedMilliseconds >= 5000)
                    ServerText.LogBackground($"Slow request ({requestLabel}): {elapsed.ElapsedMilliseconds}ms; streaming={keepOpen}.");
                if (keepOpen || !persistent) break;
            }
        }
        catch (HttpRequestReader.ProtocolException ex)
        {
            TraceRequest(trace, "invalid request", requestLabel, ex.GetType().Name);
            // No route/dispatcher was entered. Never execute an available
            // prefix of a command whose declared bytes have not all arrived.
            try { new HttpResponseWriter(client.GetStream(), false).Write("text/plain", ex.Message, null, ex.Status); }
            catch (Exception writeFailure)
            {
                TraceRequest(trace, "invalid request response failed", requestLabel, writeFailure.GetType().Name);
            }
        }
        catch (Exception ex)
        {
            TraceRequest(trace,"failed",requestLabel,ex.GetType().Name);
            // A client that opens a socket then sends nothing within ReceiveTimeout (2s) trips
            // the read here. That is a benign idle/aborted keep-alive probe, not a handler
            // failure; label it so it stops masquerading as a real error in the log.
            bool idle = ex.Message.IndexOf("period of time", StringComparison.OrdinalIgnoreCase) >= 0
                     || ex.Message.IndexOf("aborted", StringComparison.OrdinalIgnoreCase) >= 0;
            if (_running) ServerText.LogBackground(idle
                ? $"Idle connection closed ({requestLabel}, {elapsed.ElapsedMilliseconds}ms): {ex.Message}"
                : $"Request failed ({requestLabel}, {elapsed.ElapsedMilliseconds}ms): {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (!keepOpen) { client.Close(); ForgetClient(client); }
        }
    }

    private static void ForgetClient(TcpClient client) { lock (ClientsGate) Clients.Remove(client); }

    // Returns true if the connection has been handed off to a streaming thread and
    // must NOT be closed by the accept loop.
    private static bool HandleRequest(TcpClient client, HttpRequestReader.Request request,
        HttpResponseWriter response, ref string requestLabel,RequestTraceEntry trace)
    {
        var stream = client.GetStream();
        var method = request.Method;
        var rawUrl = request.Target;
        var lines = request.Lines;
        string requestBody = request.Body;

        string path = rawUrl, query = "";
        int qi = rawUrl.IndexOf('?');
        if (qi >= 0) { path = rawUrl.Substring(0, qi); query = rawUrl.Substring(qi + 1); }
        // Diagnostic identity only: never record a query, body, cookie or an
        // unregistered user-supplied path. Slow/error logs must distinguish a
        // real handler from an idle preconnection or a failed socket write.
        requestLabel = (method is "GET" or "POST" or "HEAD" ? method : "other method") + " "
            + (path is "/" or "/events" or "/sound" || Routes.ContainsKey(path) ? path : "unregistered route");
        TraceRequest(trace,"handling",requestLabel);
        if(!RuntimeSurface.Authorize(lines,path))
        {
            response.Write("text/plain","Local companion session required",null,403);
            return false;
        }

        // A standard HEAD on the entry page establishes the same local session
        // without composing a native screen. Deployment diagnostics must not
        // depend on a slow cross-frame table read merely to obtain its cookie.
        if (method == "HEAD" && path == "/")
        {
            response.Write("text/html", "", RuntimeSurface.Cookie);
            return false;
        }

        // /events is the one endpoint that keeps its socket open: it hands off to a
        // dedicated streamer thread and so cannot be a produce-a-body-and-close route.
        if (path == "/events")
        {
            if (method != "GET")
            {
                response.Write("text/plain", "The event stream requires GET.", null, 405);
                return false;
            }
            // Before Ready (notably the window after a /reload, when this assembly's Ready static has
            // reset to false but the socket is already bound), answer with a VALID but immediately
            // closed event-stream so the browser's EventSource treats it as a dropped stream and
            // auto-reconnects after the retry interval. We must NOT send text/plain here: a 200 with a
            // Content-Type other than text/event-stream is a FATAL error for EventSource (readyState
            // CLOSED, no reconnect) per the SSE spec, which silently freezes the companion on stale
            // content until a manual refresh. Once Ready, hand off to a dedicated streamer thread that
            // owns the connection for its lifetime.
            if (!MainThreadDispatcher.Ready)
            {
                var notReady = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\n"
                    + "Cache-Control: no-cache\r\nConnection: close\r\n\r\nretry: 1000\n\n");
                try { stream.Write(notReady, 0, notReady.Length); } catch { /* client already gone */ }
                return false; // socket closes -> EventSource sees the stream drop -> reconnects in ~1s
            }
            var t = new Thread(() => { try { SseStreamer.Stream(client, () => _running); } finally { ForgetClient(client); } }) { IsBackground = true, Name = "FM27AccessSSE" };
            t.Start();
            return true;
        }

        // /sound serves a static wav (a match audio cue) as raw bytes. Handled here, outside the
        // string-body route table, because the response is binary; and before the Ready gate because
        // these are static assets, so the companion can preload them while the game is still loading.
        if (path == "/sound")
        {
            if (SoundAssets.TryGet(Query.Str(query, "name"), out var wav))
                response.WriteBinary("audio/wav", wav);
            else
                response.Write("text/plain", "unknown sound", null, 404);
            return false;
        }

        Route route;
        if (path == "/") route = DefaultRoute;
        else if (!Routes.TryGetValue(path, out route))
        {
            response.Write("text/plain", "Unknown endpoint.\n", null, 404);
            return false;
        }

        // Nothing may reach Invoke unless the hand-off is actually being drained, or the call would
        // block until timeout and report a failure of the dispatch mechanism rather than of the thing
        // the player asked for. Loading is the everyday case (the game is not interactive yet); the
        // rest mean the core stopped, its tick is failing, or the game stopped running frames, and
        // each says so in words the player can act on. AlwaysReady routes (/reload, /log, /liveness)
        // run regardless: they don't touch live game state, and they are how a stopped mod is
        // diagnosed and recovered without restarting the game.
        var health = MainThreadDispatcher.Health();
        if (!SurfaceHealth.CanServe(health) && !route.AlwaysReady)
        {
            response.Write(route.ContentType,
                          health == SurfaceHealth.State.Loading
                              ? route.LoadingBody()
                              : route.UnavailableBody(MainThreadDispatcher.HealthMessage()),path=="/"?RuntimeSurface.Cookie:null);
            return false;
        }

        // Existing query-parameter endpoints retain legacy GET compatibility.
        // The owned production client uses POST for commands so Chromium must
        // not automatically replay an uncertain action as an idempotent GET.
        if (!string.Equals(route.Method, method, StringComparison.OrdinalIgnoreCase)
            && !(method == "POST" && route.Method == "GET"))
        {
            response.Write("text/plain",
                $"Method {method} not allowed for {path}; expected {route.Method}.", null, 405);
            return false;
        }

        // The settle baseline must be read BEFORE the mutating handler runs: an action that
        // applies synchronously (Reset's revert) would otherwise already be in the baseline,
        // the window would see "no change", and the companion would stay stale until F5.
        bool mutating = MutatingPaths.Contains(path);
        string settleBaseline = mutating ? SseStreamer.CaptureShapeBaseline() : null;

        var (body, header) = route.Handler(query, requestBody);
        if(path=="/")header=(string.IsNullOrEmpty(header)?"":header+"\r\n")+RuntimeSurface.Cookie;
        response.Write(route.ContentType, body, header);
        // Arm a settle window after an in-panel mutation so its follow-up is re-read.
        if (mutating) SseStreamer.ArmSettleAfterAction(settleBaseline);
        return false;
    }

    // --- Route table ---
    // Path -> Route. Only / serves the full-page builder; unknown paths return 404. Feature
    // routes are contributed by FeatureRegistry. /events is not here (it owns its socket).

    private const string LoadingMessage = "The game is still loading.";

    private static readonly Dictionary<string, Route> Routes = BuildRoutes();

    private static readonly Route DefaultRoute = new Route
    {
        ContentType = "text/html",
        LoadingBody = LoadingPage,
        UnavailableBody = message => SurfaceStartupHtml.Build(message),
        Handler = DefaultPage,
    };

    private static Dictionary<string, Route> BuildRoutes()
    {
        var routes = new Dictionary<string, Route>
        {
            // An open dropdown popup stands down before anything else is activated: the mod opened
            // it behind the widget's back, so a click on that widget does not toggle it shut the way
            // a mouse click would — it orphans it, and the teardown is fatal. See
            // DropdownSession.CloseBeforeActivation. Dropdown options are chosen through
            // /dropdown/select, not here, so this never closes the popup the player is using.
            ["/activate"] = Action(q =>
            {
                // Serialize with an opening popup, then validate ownership and
                // close it atomically with the native action on Unity's thread.
                lock (DropdownSessionManager.SessionGate)
                    return ServerHandles.Run(Query.Str(q, "id"), h => h.Activate(), Query.Str(q, "screen"), Query.Str(q, "context"));
            }),
            // Open the focused element's right-click context menu (the player's Shift+F10 / Applications
            // key in the companion). Opening it in-game makes the next body render surface the menu.
            ["/contextmenu"] = Action(q => ServerHandles.OpenContextMenu(Query.Str(q, "id"), Query.Str(q, "screen"))),
            ["/describe"] = Action(q => ServerHandles.Describe(Query.Str(q, "id"))),
            ["/settext"] = Action(q => ServerHandles.Run(Query.Str(q, "id"), h => h.SetText(Query.Str(q, "value")), Query.Str(q, "screen"), Query.Str(q, "context"))),
            ["/setvalue"] = Action(q => ServerHandles.Run(Query.Str(q, "id"), h => h.SetValue(Query.Str(q, "value")), Query.Str(q, "screen"), Query.Str(q, "context"))),
            ["/step"] = Action(q => ServerHandles.Step(Query.Str(q, "id"), Query.Int(q, "dir"), Query.Str(q, "screen"), Query.Str(q, "context"))),
            // Commit for a two-option segmented cell browsed through the shared data-ti widget
            // (FM.ti, via data-ti-set). Same shape as the team-instruction commit: "ok" or a
            // message to speak.
            ["/segment/set"] = Action(q => ServerHandles.Run(Query.Str(q, "id"), h => h.SetSegment(Query.Int(q, "opt")), Query.Str(q, "screen"))),
            ["/nav"] = Action(q => ServerHandles.Nav(Query.Str(q, "target"))),
            // The sound-cue settings page. Reads are the companion applying saved levels to its audio
            // graph at startup; writes are one slider or the wing-drone switch. Deliberately NOT
            // marshalled to the main thread — this touches a JSON file and nothing in the game — so a
            // slider drag never queues behind a frame.
            // Dev measurement of how high the engine sends the ball, for tuning the in-the-air cue's
            // loudness ramp. reset=1 starts a fresh sample.
            ["/airprobe"] = Action(q => Query.Str(q, "reset") == "1"
                ? AirHeightProbe.Reset()
                : AirHeightProbe.Report()),
            ["/soundsettings"] = Action(_ => SoundSettingsStore.ToJson()),
            ["/soundsetting"] = Action(q => SoundSettingsStore.Set(Query.Str(q, "key"), Query.Str(q, "value"))),
            // Ctrl+1..9 in the companion: open the title-bar bookmark in that slot.
            ["/bookmark"] = Action(q => ServerHandles.Bookmark(Query.Str(q, "slot"))),
            // A Messages-table row click: activate the row, verify the reading pane shows that
            // message, then open the modal Message view. Escape (FM.msgview) closes it.
            ["/message/open"] = Action(q => MessageViewSurface.OpenForRow(Query.Str(q, "id"), Query.Str(q, "screen"))),
            // Marshalled: the dismiss reads the game's history to retire the view's back/forward entry.
            ["/message/close"] = Action(_ => MainThreadDispatcher.Invoke(() => { MessageViewSurface.Close(); return ""; })),
            // dir=back|forward walks the game's screen history; no dir at all returns the read-only
            // state dump (stack position, shape, whether the modal Message view is open over it).
            ["/history"] = Action(q => ServerHandles.History(Query.Str(q, "dir"))),
            ["/speak"] = Action(q => ServerHandles.Speak(Query.Str(q, "text"))),
            // Alt+`, Alt+1..7, Alt+P and Alt+M in the companion: one round-trip that decides
            // server-side. During a match (gated on the ENGINE — the scoreboard template varies
            // per competition, so a page element is not a reliable match signal) the reply is
            // the match readout / playback action to speak; otherwise the navigation those
            // chords normally mean runs and the reply is empty on success, or the failure
            // message to speak.
            ["/matchchord"] = Action(q => MatchChord(Query.Str(q, "digit"), Query.Str(q, "taps"))),
            // The same readouts individually, for direct dev-driver use. rank=worst flips the
            // ranking readouts to their double-tap (lowest three) variant.
            ["/matchscore"] = Action(_ => ScoreLineReadout()),
            ["/matchthird"] = Action(_ => MatchBallLocation.DescribeThird()),
            ["/matchdistance"] = Action(_ => MatchBallLocation.DescribeDistance()),
            ["/matchpossession"] = Action(_ => MatchBallLocation.DescribePossession()),
            ["/matchratings"] = Action(q => MatchSquadReadout.DescribeRatings(RankArg(q))),
            ["/matchcondition"] = Action(q => MatchSquadReadout.DescribeCondition(RankArg(q))),
            ["/matchbodylanguage"] = Action(_ => MatchSquadReadout.DescribeBodyLanguage()),
            ["/matchbodylanguagedetail"] = Action(_ => MatchSquadReadout.DescribeBodyLanguageDetail()),
            ["/matchconditions"] = Action(_ => MatchConditionsReadout.Describe()),
            ["/matchcrowdmood"] = Action(_ => MatchConditionsReadout.DescribeCrowdMood()),
            // "\" and Alt+Enter in the companion: announce / press the continue button.
            ["/continue"] = Action(_ => ContinueSurface.Announce()),
            ["/continue/press"] = Action(_ => ContinueSurface.Press()),
            // Ctrl+S in the companion: the game's own quick-save shortcut, same handler.
            ["/save"] = Action(_ => SaveSurface.Trigger()),
            // Alt+H in the companion: the game's own Shift+Space go-on-vacation shortcut, same handler.
            ["/holiday"] = Action(_ => HolidaySurface.Trigger()),
            // The id on the non-open session actions is the owner check: the companion sends the
            // id its fragment was built for, so an action can never hit a session that replaced
            // it. Id-less calls (the dev driver's) act on whatever session is open.
            ["/dropdown/open"] = Html(q => DropdownSessionManager.Open(Query.Str(q, "id"), Query.Str(q, "screen"), Query.Str(q, "context"))),
            ["/dropdown/expand"] = Html(q => DropdownSessionManager.Expand(Query.Str(q, "path"), Query.Int(q, "index"), Query.Str(q, "id"))),
            ["/dropdown/collapse"] = Action(q => DropdownSessionManager.Collapse(Query.Str(q, "path"), Query.Str(q, "id"))),
            ["/dropdown/select"] = Action(q => DropdownSessionManager.Select(Query.Str(q, "path"), Query.Int(q, "index"), Query.Str(q, "id"))),
            ["/dropdown/close"] = Action(q => DropdownSessionManager.Close(Query.Str(q, "id"))),
            ["/dropdown/more"] = Html(q => DropdownSessionManager.More(Query.Str(q, "id"))),
            ["/searchdropdown/open"] = Action(q => SearchDropdownSession.Open(Query.Str(q, "id"), Query.Str(q, "screen"))),
            ["/searchdropdown/filter"] = Html(q => SearchDropdownSession.Filter(Query.Str(q, "q"), Query.Str(q, "id"))),
            ["/searchdropdown/select"] = Action(q => SearchDropdownSession.Select(Query.Int(q, "index"), Query.Str(q, "id"))),
            ["/searchdropdown/close"] = Action(q => SearchDropdownSession.Close(Query.Str(q, "id"))),
            ["/tactics/move"] = Action(q => Tactics.PitchMoveEngine.MoveAndSettle(Query.Str(q, "source"), Query.Str(q, "dest"), Query.Str(q, "screen"), Query.Str(q, "id"))),
            // Client-side failure reports (app.js FM.core.clog): the companion is the only place
            // its own JS errors surface, and a swallowed one leaves the player on a stale page
            // with no trace. POST body = the message; logged to server.log.
            ["/clientlog"] = new Route
            {
                ContentType = "text/plain",
                Method = "POST",
                LoadingBody = () => "ok",
                // AlwaysReady: pure logging, no game state - a client failure during loading
                // must be recordable too.
                AlwaysReady = true,
                Handler = (q, body) => { ServerText.LogBackground($"[client] {body}"); return ("ok", null); },
            },
            ["/body"] = new Route
            {
                ContentType = "text/html",
                LoadingBody = LoadingPage,
                UnavailableBody = UnavailablePage,
                Handler = BuildBody,
            },
            ["/version"] = new Route { ContentType = "text/plain", LoadingBody = () => "loading", Handler = Version },
            // How a stopped or stalled surface is diagnosed. AlwaysReady, and it touches no game
            // state, so it answers in exactly the situations every other route cannot.
            ["/liveness"] = new Route
            {
                ContentType = "text/plain",
                LoadingBody = () => "loading",
                AlwaysReady = true,
                Handler = (q, b) => (Liveness(), null),
            },
        };
        FeatureRegistry.RegisterRoutes(routes);
        Dev.DevEndpoints.RegisterRoutes(routes);
        return routes;
    }

    // A plain-text action route: result text, no extra header, generic loading message. The
    // body argument is ignored (companion action routes carry their parameters in the query
    // string); only dev POST routes read the body, and they declare their own Route directly.
    private static Route Action(Func<string, string> handler) => new Route
    {
        ContentType = "text/plain",
        LoadingBody = () => LoadingMessage,
        Handler = (q, b) => (handler(q), null),
    };

    // An html fragment route (dropdown popups etc.): same as Action but text/html.
    private static Route Html(Func<string, string> handler) => new Route
    {
        ContentType = "text/html",
        LoadingBody = () => LoadingMessage,
        Handler = (q, b) => (handler(q), null),
    };

    // Alt+`, Alt+1..7, Alt+P and Alt+M from the companion (see the /matchchord route; the
    // backquote arrives as digit=tick). During a match the chords are the match readouts and
    // playback controls; outside one, Alt+1..6 are fixed mod destinations (Messages, First Team,
    // Tactics, Player Database, Staff, Fixture Schedule — see the uid map below) and Alt+P is
    // the FM/pause menu it has always opened; Alt+M answers the "menubrowser" token, telling the
    // client to focus the title bar's Menu browser tree (Alt+` and Alt+7 have no out-of-match
    // meaning, so they answer honestly). A quick second tap of the same chord (taps=2, the
    // client decides within its window) picks the chord's detail variant where one exists — the
    // single-tap reply already spoke on the first press, so the detail interrupts it, the
    // standard screen-reader double-press idiom. A chord without a detail variant just repeats;
    // for the play/pause toggle each tap is its own press, so a double tap flips twice, natural
    // toggle semantics.
    // Nav success ("Opening X") is suppressed — the screen change speaks for itself, matching
    // /nav and /bookmark — while a nav failure is returned for the client to speak.
    private static string MatchChord(string digit, string taps)
    {
        bool detail = taps == "2";
        if (MatchBallLocation.IsMatchLive())
            return digit switch
            {
                "tick" => detail ? MatchEventsReadout.Describe() : ScoreLineReadout(),
                "1" => MatchSquadReadout.DescribeRatings(detail ? SquadRank.Worst : SquadRank.Best),
                "2" => MatchSquadReadout.DescribeCondition(detail ? SquadRank.Worst : SquadRank.Best),
                "3" => detail ? MatchSquadReadout.DescribeBodyLanguageDetail()
                              : MatchSquadReadout.DescribeBodyLanguage(),
                "4" => detail ? MatchConditionsReadout.DescribeCrowdMood()
                              : MatchConditionsReadout.Describe(),
                "5" => MatchBallLocation.DescribePossession() + ". " + MatchBallLocation.DescribeThird(),
                "7" => MatchBallLocation.DescribeDistance(),
                "p" => MatchPlayback.Toggle(),
                "m" => MatchPlayback.OpenSettings(),
                _ => "Unknown match chord.",
            };

        // Outside a match, Alt+1..6 are FIXED destinations (2026-07-24, replacing the ribbon
        // category chords, which reopened each section wherever it last was — unpredictable for
        // a player who cannot glance at the result). Each opens one specific screen through the
        // Navigator's live-resolved subtab dispatch, addressed by PanelID uid: the game's asset
        // ids, stable across saves and languages; a game patch changing one fails loudly
        // ("Destination not found"), never navigates somewhere wrong. First Team's club object
        // id is save-specific, so GoToUid takes whatever object the live entry carries. The six
        // section screens stay reachable through the Menu browser (Alt+M).
        uint navUid = digit switch
        {
            "1" => 3199626752u, // Portal > Messages
            "2" => 1381513472u, // Squad > First Team (club object id resolved live)
            "3" => 195478016u,  // Match Day > Tactics
            "4" => 3173313024u, // Recruitment > Player Database
            "5" => 1204137728u, // Club > Staff (FM27 native destination)
            "6" => 837367296u,  // Portal > Calendar > Schedule (FM27)
            _ => 0u,
        };
        if (navUid != 0)
        {
            var navResult = NavigatorIndex.GoToUid(navUid);
            return navResult != null && navResult.StartsWith("Opening ") ? "" : navResult;
        }

        var target = digit switch
        {
            "p" => "pausemenu",
            _ => null,
        };
        if (target == null)
            return digit switch
            {
                // Alt+M outside a match: the client focuses the title bar's Menu browser tree.
                // A protocol token, not speech — the match gate lives here (the ENGINE), so the
                // client cannot decide this locally; see FM.matchchord's reply handling.
                "m" => "menubrowser",
                "7" or "tick" => "Not in a match.",
                _ => "Unknown match chord.",
            };
        var result = ServerHandles.Nav(target);
        return result != null && result.StartsWith("Opening ") ? "" : result;
    }

    private static SquadRank RankArg(string q) =>
        Query.Str(q, "rank") == "worst" ? SquadRank.Worst : SquadRank.Best;

    // Alt+`: the scoreboard line the page's top heading shows — team names, score, clock — read
    // live from the engine at the moment of the press. ComposeCurrentScoreLine is null while the
    // team names have not been readable yet this match (kick-off before the bindings populate);
    // the player asked for a score, so that must be an audible marker, never silence.
    private static string ScoreLineReadout()
    {
        try
        {
            return MainThreadDispatcher.Invoke(MatchWatcher.ComposeCurrentScoreLine)
                ?? "score " + Speech.Degrade.Unavailable;
        }
        catch (Exception ex)
        {
            Speech.Degrade.Note($"[AccessibilityServer] score line read failed: {ex.Message}");
            return "score " + Speech.Degrade.Unavailable;
        }
    }

    private const int ComposeTimeoutMs = 10000;

    // Builds the #fmroot body on the request (background) thread. Two paths:
    //  - Fast path (no streamed table — the common case): walk AND render in a SINGLE main-thread
    //    Invoke, saving the second round-trip (~one frame) on every refresh. Safe because WalkPanel
    //    only produces render items (it registers no handles); BeginBody then RenderSection register
    //    them, so the walk -> BeginBody -> render order inside one atomic Invoke matches the two-phase
    //    order exactly, with no cross-frame gap to leave handles stale.
    //  - Streamed path: when any walked item is a streamed table, keep the three-phase split — (1)
    //    walk, (2) load each table across frames via StreamedTableLoader (Invoke + Sleep), (3) render
    //    — because those reads must span frames. The walked items hold the stable table container, so
    //    they survive the loader's scrolling.
    //
    // The page renders every panel the gate (InteractivePanels) reports, each as one heading + content
    // (no <section> wrapper). The token is the aggregate over the rendered set (Phase 3): the rendered
    // set IS the token, so the SSE poll refreshes whenever the set changes.
    // noteShape: also capture the panel set's structural ShapeSignature inside the SAME dispatch that
    // renders the served HTML (the companion's refresh asks for this via /body?c=1). SseStreamer uses
    // it to recognise a settle-window tick whose shape the client has already rendered and skip the
    // redundant restructure. Captured at render time, never earlier: the shape must describe exactly
    // the structure the served body reflects. Best-effort — a capture failure returns null (no
    // suppression, today's behavior) and never fails the compose.
    // An item whose rows are filled by a loader on the request thread, AFTER the walk. Anything
    // matching this must never be rendered straight off the walk: its Rows are still null, and the
    // renderers read them. One definition, used by both the "does this section need the streamed
    // path?" test and the load loop itself, so the two can never disagree about what streamed means.
    private static bool IsStreamedItem(ScreenWalker.RenderItem item)
        => item is ScreenWalker.TableItem { Context: not null }
            or ScreenWalker.FixtureScheduleItem or ScreenWalker.LeagueTableItem
            or ScreenWalker.ConditionTableItem or ScreenWalker.ResponsibilitiesTableItem
            or ScreenWalker.StaffDutiesTableItem;

    private static bool HasStreamedItem(List<ScreenWalker.RenderItem> items)
    {
        foreach (var item in items)
            if (IsStreamedItem(item)) return true;
        return false;
    }

    private sealed class ForegroundOpeningException : Exception { }

    private static (string body, string title, string token, string shape, string language) ComposeBody(bool noteShape = false)
    {
        long deadline = Environment.TickCount64 + ComposeTimeoutMs;
        do
        {
            try { return ComposeCurrentBody(noteShape); }
            catch (ForegroundOpeningException)
            {
                // Only observe on later Unity dispatches. Never block Unity,
                // replay the command or publish its blocked parent in the gap.
                Thread.Sleep(16);
            }
        } while (Environment.TickCount64 < deadline);
        // A genuinely slow native opening retains ordinary loading feedback,
        // not the background title/controls or a fabricated successful command.
        string loading = ServerText.Escape(LoadingMessage);
        return ($"<div data-fm-native-loading><h1>{loading}</h1></div>", LoadingMessage, "?", null, "en");
    }

    private static (string body, string title, string token, string shape, string language) ComposeCurrentBody(bool noteShape)
    {
#if FM27_DEVELOPER_TOOLS
        var composeTiming = System.Diagnostics.Stopwatch.StartNew();
#endif
        string language = "und";
        // fastBody is non-null only on the fast path (RenderSectionsToBody never returns null), so it
        // doubles as the "already rendered, skip the streamed phase" signal.
        var (sections, title, token, fastBody, fastShape) = MainThreadDispatcher.Invoke(() =>
        {
            if (InteractivePanels.IsCoveringForegroundLoading()) throw new ForegroundOpeningException();
            language = DocumentLanguage.FromNative(SI.Translation.TranslationManager.CurrentLanguage);
            // An open cascading context menu (the squad Actions menu) is a floating element outside the
            // panel set; when one is up it is modal, so render only its entries. The token still folds
            // in the base panels (via ScreenToken's menu suffix) so closing the menu refreshes back to
            // the screen beneath.
            var openMenu = ContextMenuSurface.TryGetMenu();
            // A dead shell (visible popup whose description the game dropped — a scroll recycled the
            // menu's anchor row) must never render as the modal: it would trap the player on an empty
            // "Actions" page. Dismiss it and render the page instead. Skipped while an open is in
            // flight, when the popup can be legitimately description-less for a moment.
            if (openMenu != null && !ContextMenuGuard.OpenInFlight && ContextMenuSurface.IsDeadShell(openMenu))
            {
                ContextMenuSurface.Close();
                openMenu = null;
            }
            if (openMenu != null)
            {
                var activePanel = NavigationManager.GetActivePanel();
                // Title the modal by the entity whose menu this is, captured when the open was
                // dispatched: "Actions for Mohamed Wright" rather than a bare "Actions", so the
                // player hears whose menu opened. Falls back to "Actions" for a textless-holder menu.
                string menuTitle = ContextMenuMenuTitle();
                var menuBuilt = new List<(object Panel, string Title, bool IsTitleBar, bool IsOverlay, List<ScreenWalker.RenderItem> Items)>
                {
                    (activePanel, menuTitle, false, false, ContextMenuSurface.WalkEntries(openMenu))
                };
                // A menu has no streamed table, so it always takes the fast path: render inline here.
                // preservePageHandles: the menu is a modal overlay — the page beneath stays live, and
                // the player's next gesture after closing it (the next row's menu) needs those ids.
                // No shape note: the restructure behavior under a modal overlay stays as today.
                return (menuBuilt, menuTitle,
                        ServerText.ScreenToken(InteractivePanels.Current()),
                        RenderSectionsToBody(menuBuilt, preservePageHandles: true),
                        (string)null);
            }

            // An open bookmark editor (the title bar's mapping popup) is modal on the HTML surface:
            // while it is up the player is editing their bookmark slots and nothing else, so render
            // only its section — the strip it edits and the rest of the page return when they Save
            // and close. Like the context menu above, the token still folds the base panels in (via
            // ScreenToken's editor suffix) so closing refreshes back to the screen beneath. A build
            // failure falls through to the normal full-panel render (the popup stays dismissible
            // in-game), so a broken editor never traps the player on a blank page.
            try
            {
                if (BookmarkEditorSurface.TryBuildSection() is { } editor)
                {
                    var editorActive = NavigationManager.GetActivePanel();
                    var editorBuilt = new List<(object Panel, string Title, bool IsTitleBar, bool IsOverlay, List<ScreenWalker.RenderItem> Items)>
                    {
                        (editorActive, editor.Title, false, false, editor.Items)
                    };
                    // No streamed table in the editor, so render inline on the fast path. Default
                    // handle handling (not preservePageHandles): the editor's rows want fresh ids
                    // each render, since a toggled row moves between the two lists.
                    return (editorBuilt, editor.Title,
                            ServerText.ScreenToken(InteractivePanels.Current()),
                            RenderSectionsToBody(editorBuilt),
                            (string)null);
                }
            }
            catch (Exception ex)
            {
                Speech.Degrade.Note($"bookmark editor section build failed: {ex.GetType().Name}: {ex.Message}");
            }

            // The mod's Sound Cues Settings page is modal the same way: while it is open the
            // player is learning the sound system (the match is paused) and nothing else, so
            // render only its section. A build failure falls through to the normal render, so a
            // broken page never traps the player — the trigger stop toggles it closed.
            try
            {
                if (SoundCuesSurface.TryBuildSection() is { } soundCues)
                {
                    var soundActive = NavigationManager.GetActivePanel();
                    var soundBuilt = new List<(object Panel, string Title, bool IsTitleBar, bool IsOverlay, List<ScreenWalker.RenderItem> Items)>
                    {
                        (soundActive, soundCues.Title, false, false, soundCues.Items)
                    };
                    return (soundBuilt, soundCues.Title,
                            ServerText.ScreenToken(InteractivePanels.Current()),
                            RenderSectionsToBody(soundBuilt),
                            (string)null);
                }
            }
            catch (Exception ex)
            {
                Speech.Degrade.Note($"sound cues section build failed: {ex.GetType().Name}: {ex.Message}");
            }

            // The Navigator search page is modal the same way: while it is open the player is
            // choosing where to go and nothing else. A build failure falls through to the normal
            // render, so a broken page never traps the player.
            try
            {
                if (NavigatorSurface.TryBuildSection() is { } navigator)
                {
                    var navActive = NavigationManager.GetActivePanel();
                    var navBuilt = new List<(object Panel, string Title, bool IsTitleBar, bool IsOverlay, List<ScreenWalker.RenderItem> Items)>
                    {
                        (navActive, navigator.Title, false, false, navigator.Items)
                    };
                    return (navBuilt, navigator.Title,
                            ServerText.ScreenToken(InteractivePanels.Current()),
                            RenderSectionsToBody(navBuilt),
                            (string)null);
                }
            }
            catch (Exception ex)
            {
                Speech.Degrade.Note($"navigator section build failed: {ex.GetType().Name}: {ex.Message}");
            }

            // An open filter popup (a table's SIFloatingPopupWidget menu) is modal the same way:
            // while it is up the player is choosing filters and nothing else. A build failure
            // falls through to the full-panel render, so a broken popup never traps the player.
            // TODO: extract a seam if a fourth modal floating surface lands (menu, bookmarks, this).
            try
            {
                if (FilterPopupSurface.TryBuildSection() is { } popup)
                {
                    var popupActive = NavigationManager.GetActivePanel();
                    var popupBuilt = new List<(object Panel, string Title, bool IsTitleBar, bool IsOverlay, List<ScreenWalker.RenderItem> Items)>
                    {
                        // Native floating content already replaces its whole
                        // background here. Reuse the covering-overlay focus
                        // contract even on its first, unremembered opening.
                        (popupActive, popup.Title, false, true, popup.Items)
                    };
                    // FM27 floating menus can contain a regular streamed table
                    // (including native row selection). Reuse the common
                    // cross-frame loader below; simple filters stay inline.
                    bool popupStreamed = HasStreamedTable(popupBuilt);
                    return (popupBuilt, popup.Title,
                            ServerText.ScreenToken(InteractivePanels.Current()),
                            popupStreamed ? null : RenderSectionsToBody(popupBuilt),
                            (string)null);
                }
            }
            catch (Exception ex)
            {
                Speech.Degrade.Note($"filter popup section build failed: {ex.GetType().Name}: {ex.Message}");
            }

            // An open Message view (an email opened from the Messages table) is modal the same
            // way: while it is up the player is reading that message and nothing else — the base
            // Messages page never renders the reading pane inline. Built under the Messages
            // panel's profile so its heading and hide rules apply to the walked pane, with the
            // speech panel's profile restored after. preservePageHandles like the context menu:
            // the list page beneath survives (the companion restores its stash on close) and the
            // player's next gesture targets it. A build failure falls through to the full-panel
            // render, so a broken view never traps the player.
            try
            {
                if (MessageViewSurface.IsOpen())
                {
                    ActivateProfileFor(MessageViewSurface.PanelForProfile());
                    var message = MessageViewSurface.TryBuildSection();
                    ActivateProfileFor(NavigationManager.GetActivePanel());
                    if (message is { } msg)
                    {
                        var msgActive = NavigationManager.GetActivePanel();
                        var msgBuilt = new List<(object Panel, string Title, bool IsTitleBar, bool IsOverlay, List<ScreenWalker.RenderItem> Items)>
                        {
                            (msgActive, msg.Title, false, false, msg.Items)
                        };
                        var msgToken = ServerText.ScreenToken(InteractivePanels.Current());
                        // An email can embed a streamed table (a squad list in a registration
                        // message). Rendering inline here would hand the renderer a table whose rows
                        // were never loaded — the fast path returns before the streamed phase runs —
                        // so this section takes the streamed path instead, exactly as a normal screen
                        // with a streamed table does. A message with no such table still renders
                        // inline, which keeps the common case one Invoke.
                        if (HasStreamedItem(msg.Items))
                            return (msgBuilt, msg.Title, msgToken, (string)null, (string)null);
                        return (msgBuilt, msg.Title, msgToken,
                                RenderSectionsToBody(msgBuilt, preservePageHandles: true), (string)null);
                    }
                }
            }
            catch (Exception ex)
            {
                Speech.Degrade.Note($"message view section build failed: {ex.GetType().Name}: {ex.Message}");
            }

            var panels = InteractivePanels.Current();
            var active = NavigationManager.GetActivePanel();

            string tok = ServerText.ScreenToken(panels);
            // A screen change resets every table to page 1: the pages the player chose belong to the
            // screen they chose them on. Done before ANY page is read — the streamed load further
            // down, and the walk below, which pages the consolidated fixtures tables as it builds
            // them. Synced after the walk, that walk read the previous screen's stored pages.
            TablePages.SyncScreen(tok);

            var built = new List<(object Panel, string Title, bool IsTitleBar, bool IsOverlay, List<ScreenWalker.RenderItem> Items)>();
            foreach (var pv in panels)
            {
                // Nobody is waiting for this compose any more (its caller timed out and already
                // served the busy page). Stop between panels rather than walking the rest into a
                // result that will be discarded — every further panel is main thread the game does
                // not get back. See MainThreadDispatcher.ThrowIfAbandoned.
                MainThreadDispatcher.ThrowIfAbandoned();
                // Each panel walks under its own profile (hide rules differ per panel); restore the
                // speech panel's profile afterwards so the shared global is left as speech expects.
                ActivateProfileFor(pv.Panel);
                built.Add((pv.Panel, pv.Title, pv.IsTitleBar, pv.IsOverlay, HtmlRenderer.WalkPanel(pv.Panel)));
            }
            ActivateProfileFor(active);
            MoveMatchControllerToggleToPageEnd(built);

            // The browser tab title is the foremost (highest-layer) panel's name; no page <h1>.
            string ttl = panels.Count > 0 ? panels[panels.Count - 1].Title : "Screen";
            // Fast path: render in this same Invoke when nothing needs the cross-frame streamed load.
            // The post-load restructure still runs — a screen with no streamed table reaches the
            // renderer from here and would otherwise skip it entirely.
            bool streamed = HasStreamedTable(built);
            if (!streamed)
                foreach (var (panel, _, _, _, sectionItems) in built)
                    ScreenWalker.RestructureLoadedTables(sectionItems, panel);
            string body = streamed ? null : RenderSectionsToBody(built, withScoreHeading: true);
            // Preserve the walked structure across a streamed load. Native
            // children can finish binding on intervening frames, after their
            // render items were captured but before the final shape is noted.
            string shp = noteShape ? CaptureComposedShape(panels, excludeStreamedRows: streamed) : null;
            return (built, ttl, tok, body, shp);
        }, ComposeTimeoutMs);

#if FM27_DEVELOPER_TOOLS
        Log.Debug($"[ComposeTiming] capture={composeTiming.ElapsedMilliseconds}ms streamed={fastBody == null} sections={sections.Count}");
#endif
        if (fastBody != null)
            return (fastBody, title, token, fastShape, language);

        // Streamed path: load each streamed table across frames, then render in a second Invoke.
        // Each section's loads run under that section's own profile: streamed rows bake their cell
        // labels at read time (TableCell.Text = DisplayText.Of), so a load running under the walk
        // loop's restored speech-panel profile (often the title bar's) would resolve renames/hides
        // against the wrong panel and bake raw labels into the cells. The render Invoke below
        // re-activates per section and restores the speech panel's profile when it finishes.
        foreach (var (panel, _, _, _, items) in sections)
        {
            bool activated = false;
            foreach (var item in items)
            {
                if (!IsStreamedItem(item)) continue;
                if (!activated)
                {
                    MainThreadDispatcher.Invoke(() => { ActivateProfileFor(panel); return true; }, ComposeTimeoutMs);
                    activated = true;
                }
                if (item is ScreenWalker.TableItem t && t.Context != null)
                {
#if FM27_DEVELOPER_TOOLS
                    var tableTiming = System.Diagnostics.Stopwatch.StartNew();
#endif
                    StreamedTableLoader.Load(t);
#if FM27_DEVELOPER_TOOLS
                    Log.Debug($"[ComposeTiming] table={tableTiming.ElapsedMilliseconds}ms rows={t.Rows?.Count} columns={t.Headers.Count}");
#endif
                }
                else if (item is ScreenWalker.FixtureScheduleItem fs)
                    FixtureScheduleLoader.Load(fs);
                else if (item is ScreenWalker.LeagueTableItem lt)
                    LeagueTableLoader.Load(lt);
                else if (item is ScreenWalker.ConditionTableItem ct)
                    ConditionTableLoader.Load(ct);
                else if (item is ScreenWalker.ResponsibilitiesTableItem rt)
                    ResponsibilitiesLoader.Load(rt);
                else if (item is ScreenWalker.StaffDutiesTableItem sd)
                    ResponsibilitiesLoader.LoadStaffDuties(sd);
            }
        }

        // The progress report's graph is only readable one attribute at a time (see
        // SingleAttributeChart), and the game re-seeds the selection on every visit to that tab.
        // Reduce it here — request thread, between frames, exactly like the loads above — so the
        // page below is built from the reduced selection and the player never sees the wider one.
        foreach (var (panel, _, _, _, items) in sections)
        {
            if (!Features.SingleAttributeChart.AppliesTo(panel)) continue;
            if (!Features.SingleAttributeChart.Reduce(items)) continue;

            // The rows and the chart were read before the unticking, so both are now stale.
            foreach (var item in items)
                if (item is ScreenWalker.TableItem t && t.Context != null)
                    StreamedTableLoader.Load(t);
            MainThreadDispatcher.Invoke(() =>
            {
                ActivateProfileFor(panel);
                Features.SingleAttributeChart.RereadCharts(items);
                return true;
            }, ComposeTimeoutMs);
        }

        var (streamedBody, streamedShape) = MainThreadDispatcher.Invoke(() =>
        {
            if (InteractivePanels.IsCoveringForegroundLoading()) throw new ForegroundOpeningException();
            // A foreground can finish opening while an older parent's table
            // is harvested. Validate actual panel Pointers before registering
            // or returning that older body's controls/focus targets.
            var foreground = InteractivePanels.Current();
            if (foreground.Count == 1 && foreground[0].IsOverlay
                && (foreground.Count != sections.Count
                    || foreground.Where((view, i) => sections[i].Panel is not SI.Bindable.Panel prior
                        || prior.Pointer != view.Panel.Pointer).Any())) throw new ForegroundOpeningException();
            if (language != DocumentLanguage.FromNative(SI.Translation.TranslationManager.CurrentLanguage))
                throw new InvalidOperationException("The game language changed while this screen was loading.");
            // Consolidations that need the rows a streamed table only has once the load above has
            // run: the walk can mark what belongs together, but not join it.
            foreach (var (panel, _, _, _, items) in sections)
                ScreenWalker.RestructureLoadedTables(items, panel);
            // The modal Message view keeps the inbox page's handles alive beneath it (the companion
            // restores focus to the row the player opened), so a message that comes through the
            // streamed path must preserve them exactly as its inline render did.
            string b = RenderSectionsToBody(sections, preservePageHandles: MessageViewSurface.IsOpen(),
                                            withScoreHeading: true);
            var currentPanels = noteShape ? InteractivePanels.CurrentOrNull() : null;
            string s = noteShape ? CaptureComposedShape(currentPanels) : null;
            // The final native shape does not prove that the earlier walk
            // contained its new content. Reuse the bounded FM26 marker retry;
            // otherwise the settle gate suppresses the needed re-read because
            // it sees this final shape as already served to the companion.
            string walkedShape = noteShape ? CaptureComposedShape(currentPanels, excludeStreamedRows: true) : null;
            if (fastShape != null && walkedShape != null && fastShape != walkedShape)
                b += HtmlRenderer.ProvisionalComment;
            return (b, s);
        }, ComposeTimeoutMs);
#if FM27_DEVELOPER_TOOLS
        Log.Debug($"[ComposeTiming] complete={composeTiming.ElapsedMilliseconds}ms");
#endif
        return (streamedBody, title, token, streamedShape, language);
    }

    // The Match Controller toggle lives in the score bug at the top of the page, but the transport
    // strip it opens renders as the page's LAST section (overlay sections follow base content) —
    // the player checked a box at the top and the controls appeared somewhere far below. Move the
    // toggle's stop to the end of the last base (non-overlay) section so the controller section
    // sits directly below it when checked, and the toggle is the page's last control when not.
    // HTML-only: the speech cursor keeps the header's natural order. The stop's rename must exist
    // in OverviewPrototype.json as well as OverviewHeader.json — renames resolve against the
    // profile active at render time, and after the move that is the overview section's. A failure
    // leaves the toggle where it walked (still reachable), logged per the no-silent-failures rule.
    private const string MatchControllerFocusserName = "ShowMatchControllerButtonFocusser";

    private static void MoveMatchControllerToggleToPageEnd(
        List<(object Panel, string Title, bool IsTitleBar, bool IsOverlay, List<ScreenWalker.RenderItem> Items)> sections)
    {
        try
        {
            foreach (var section in sections)
            {
                var items = section.Items;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] is not ScreenWalker.StopItem stop) continue;
                    if (stop.Element is not Adapters.PluginAdapterBase pab) continue;
                    if (!IsInsideMatchControllerFocusser(pab.BackingElement)) continue;

                    int target = -1;
                    for (int t = sections.Count - 1; t >= 0; t--)
                        if (!sections[t].IsOverlay) { target = t; break; }
                    if (target < 0) return;

                    items.RemoveAt(i);
                    sections[target].Items.Add(stop);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Match-controller toggle move failed, leaving in place: {ex.Message}");
        }
    }

    private static bool IsInsideMatchControllerFocusser(VisualElement el)
    {
        var parent = el.hierarchy.parent;
        for (int depth = 0; depth < 4 && parent != null; depth++, parent = parent.hierarchy.parent)
            if (parent.name == MatchControllerFocusserName) return true;
        return false;
    }

    // The modal context-menu view's heading. "Actions for {entity}" when the open captured the
    // entity's name; a bare "Actions" otherwise (a textless-holder menu, or a menu opened before the
    // label was captured). The connector matches the surrounding mod-authored menu chrome ("Actions",
    // "Close menu", "Back"), which are established literals on this surface.
    private const string MenuActionsTitle = "Actions";

    private static string ContextMenuMenuTitle()
    {
        var entity = ContextMenuSurface.OpenedOwnerLabel;
        return string.IsNullOrEmpty(entity) ? MenuActionsTitle : MenuActionsTitle + " for " + entity;
    }

    // Main thread only. Best-effort: null on any failure (the caller then skips the note and the
    // settle window behaves exactly as without this feature), logged per the no-silent-failures rule.
    private static string CaptureComposedShape(IReadOnlyList<InteractivePanels.PanelView> set,
        bool excludeStreamedRows = false)
    {
        if (set == null) return null;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string shape = ScreenWalker.ShapeSignature(set, excludeStreamedRows);
            Log.Debug($"[Compose] shape capture took {sw.ElapsedMilliseconds}ms");
            return shape;
        }
        catch (Exception ex)
        {
            Log.Warn($"[Compose] shape capture failed, skipping restructure suppression: {ex.Message}");
            return null;
        }
    }

    private static bool HasStreamedTable(List<(object Panel, string Title, bool IsTitleBar, bool IsOverlay, List<ScreenWalker.RenderItem> Items)> sections)
    {
        foreach (var (_, _, _, _, items) in sections)
            foreach (var item in items)
            {
                if (item is ScreenWalker.TableItem t && t.Context != null)
                    return true;
                if (item is ScreenWalker.FixtureScheduleItem)
                    return true;
                if (item is ScreenWalker.LeagueTableItem)
                    return true;
                if (item is ScreenWalker.ConditionTableItem)
                    return true;
                if (item is ScreenWalker.ResponsibilitiesTableItem)
                    return true;
                if (item is ScreenWalker.StaffDutiesTableItem)
                    return true;
            }
        return false;
    }

    // Render the walked sections into the #fmroot body. Main thread only. Resets the per-build
    // handle/value pools (BeginBody) then re-registers them as it emits, so the previous build's
    // handles stay valid until this atomic re-registration — a companion click landing mid-build
    // never hits an empty handle map. In the streamed path the StreamedTableLoader runs BEFORE this
    // (between the walk and this render Invoke); in the fast path the walk happens in the same Invoke
    // just above, so there is no gap at all.
    private static string RenderSectionsToBody(List<(object Panel, string Title, bool IsTitleBar, bool IsOverlay, List<ScreenWalker.RenderItem> Items)> sections, bool preservePageHandles = false, bool withScoreHeading = false)
    {
        bool hasEditablePitch = false;
        foreach (var section in sections)
            foreach (var item in section.Items)
                if (item is ScreenWalker.PitchGridItem pitch && pitch.Editable?.Count > 0)
                    hasEditablePitch = true;
        HtmlRenderer.BeginBody(preservePageHandles, hasEditablePitch);
        var active = NavigationManager.GetActivePanel();
        var sb = new StringBuilder();
        bool nativeDialogOnly = sections.Count == 1 && sections[0].Panel is SI.Bindable.Panel soleDialog
            && (soleDialog.PanelID?.name == "GenericModalDialog" || ServerText.HasNativeDialogActions(soleDialog));

        // While a match is live, the score line is the page's identity: an <h1> at the very
        // top, composed at page level (engine + remembered team names) so it exists no matter
        // which panels walked out or whether the scoreboard UI is transiently hidden. The SSE
        // score channel keeps it ticking by id. The walked per-panel scoreboard stop is then
        // suppressed (one #fmscore per page); if this compose fails the walked stop still
        // renders, so a heading failure can never lose the score. Modal builds (context menu,
        // bookmark editor) opt out — they replace the page with a focused editing surface.
        if (withScoreHeading && !nativeDialogOnly)
        {
            try
            {
                string line = MatchWatcher.ComposeCurrentScoreLine();
                if (!string.IsNullOrEmpty(line))
                {
                    sb.Append("<h1 id=\"fmscore\">").Append(ServerText.EscapeText(line)).Append("</h1>");
                    HtmlRenderer.SuppressWalkedScoreboard();
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[AccessibilityServer] Score heading compose failed, keeping the walked score line: {ex.Message}");
            }
        }

        if (sections.Count == 0)
            sb.Append("<p>No navigable content on this screen.</p>");

        // A synthetic Back rescues the player only from a covering overlay shown ALONE (its title
        // bar and real Back suppressed). When any base content co-renders — a non-covering HUD
        // overlay like the match transport controller renders beside the management bar — the
        // player can reach that navigation directly, so a Back on the overlay would be a redundant
        // dead control. Base content is any non-overlay section in the set.
        bool coRendersWithBase = false;
        foreach (var s in sections)
            if (!s.IsOverlay) { coRendersWithBase = true; break; }

        int index = 0;
        foreach (var (panel, secTitle, isTitleBar, isOverlay, items) in sections)
        {
            // Re-activate per section: the global profile is shared with speech and (in the streamed
            // path) the main thread ran other frames between the walk Invoke and this one.
            ActivateProfileFor(panel);
            // The title bar is the page's <h1> landmark; content panels are <h2>. A covering overlay
            // shown alone gets a synthetic Back so the player is never trapped — but only when it
            // co-renders with no base content (otherwise the screen's own navigation is present) and
            // the card hasn't instantiated its own close button (some, like My Profile, leave that
            // slot empty), so we never present Back AND Close, nor a dead Back.
            bool requiresDialogAnswer = panel is SI.Bindable.Panel dialog
                && (dialog.PanelID?.name == "GenericModalDialog" || ServerText.HasNativeDialogActions(dialog));
            bool showBack = isOverlay && !coRendersWithBase && !requiresDialogAnswer && !HasNativeCloseButton(items);
            // Match chrome renders headerless (level 0). ContinuePanel owns the
            // same untitled command/score ribbon at half/full time; its synthetic
            // "Navigation" title adds nothing above the actual report heading.
            // Keep all controls and the pending-changes banner's own heading.
            string panelName = NavigationManager.GetPanelName(panel);
            int level = panelName is "OverviewHeader" or "ContinuePanel" ? 0 : (isTitleBar ? 1 : 2);
            bool isLoading = false;
            if (panel is SI.Bindable.Panel loadingPanel)
            {
                var layer = FM27PanelCatalog.GetNativeLayerForPanel(SI.Bindable.PanelManager.Instance, loadingPanel);
                isLoading = layer != null && (layer.Layer == (int)SI.UI.PanelLayerSettings.GetLayer("LoadingScreen")
                    || layer.Layer == (int)SI.UI.PanelLayerSettings.GetLayer("ProfanityLoadingScreen"));
            }
            // Reuse the original section/title/content/action HTML. Only a real
            // native answer dialog shown alone is modal; ordinary cards, HUDs
            // and non-covering panels must not trap focus or hide their peers.
            bool covering = sections.Count == 1 && isOverlay && !isLoading;
            if (covering && !nativeDialogOnly) level = 1;
            if (nativeDialogOnly || covering)
            {
                // Keep the native overlay's focus/return identity without an
                // extra main landmark. FM26 uses the actual section heading
                // alone; a landmark repeats that stop in screen-reader reading.
                sb.Append("<div ");
                if (nativeDialogOnly) sb.Append("role=\"dialog\" aria-modal=\"true\" aria-labelledby=\"fmsec").Append(index).Append("-h\" data-fm-native-dialog=\"");
                else sb.Append("data-fm-overlay-title=\"fmsec").Append(index).Append("-h\" data-fm-native-overlay=\"");
                sb.Append(ServerText.Escape(ServerText.ScreenToken(panel))).Append("\">");
            }
            if(isLoading)sb.Append("<div data-fm-native-loading>");
            HtmlRenderer.RenderSection(sb, secTitle, items, index, level, showBack, isLoading);
            if(isLoading)sb.Append("</div>");
            if (nativeDialogOnly || covering) sb.Append("</div>");
            index++;
        }
        ActivateProfileFor(active);
        return sb.ToString();
    }

    // FM's card header puts its close affordance in a TemplateContainer named "close-button" (a
    // "buttons-controls-pc-default" SIButton with an x-icon). Most cards instantiate it and the mod
    // surfaces it as a reachable "Close" stop — but some (My Profile) leave the slot empty, which is
    // why the synthetic Back exists. When a real close already exists we skip the synthetic Back
    // rather than render two exits. Scanning the walked stops (not the raw tree) means a close hidden
    // by a profile still yields a Back. The shared native close-command/template resolver
    // also recognizes FM27's search and help cards, without translated-caption routing.
    // Main thread only: reads live VisualElement hierarchy, called from the phase-3 render Invoke.
    private static bool HasNativeCloseButton(List<ScreenWalker.RenderItem> items)
    {
        foreach (var item in items)
        {
            if (item is ScreenWalker.StopItem stop)
            {
                if (stop.Element is FilterPopupSurface.CloseAdapter) return true;
                var backing = (stop.Element as Adapters.PluginAdapterBase)?.BackingElement;
                if (backing != null && stop.Element is Adapters.ButtonAdapter
                    && Adapters.NativeButtonCommand.IsCloseControl(backing)) return true;
            }
        }
        return false;
    }

    private static (string body, string header) BuildBody(string query, string requestBody)
    {
        string token = "?";
        try
        {
            // c=1 marks the companion's own refresh (app.js). Only then is the composed shape noted:
            // a dev-driver /body read does not mean the PLAYER's page is up to date, so it must never
            // suppress a restructure. Noted only on success — a failed compose notes nothing. The
            // Begin/End bracket lets the settle tick defer a restructure it would otherwise race
            // against this compose (see SseStreamer).
            bool companion = Query.Str(query, "c") == "1";
            // The previous compose of this screen ran out of time. Re-running it would cost the same
            // main thread and fail the same way, so answer with the state it already reported rather
            // than freezing the game to rediscover it (see ComposeBreaker).
            if (!ComposeBreaker.ShouldAttempt()) return BusyPage(token, null);
            if (companion) SseStreamer.CompanionComposeBegin();
            try
            {
                (string body, string title, token, string shape, string language) = ComposeBody(noteShape: companion);
                if (shape != null)
                {
                    // Provisional: the page rendered a gap the game may simply not have filled yet —
                    // a control whose label was still binding, or a streamed table whose list was not
                    // realized at compose time. Neither changes the game's structural shape, so the
                    // shape gate alone would leave the player on the gap until they refresh.
                    bool provisional = body.Contains(Adapters.ButtonAdapter.UnlabeledIconButton)
                        || body.Contains(Speech.Degrade.UnknownControl)
                        || body.Contains(HtmlRenderer.TableNotLoadedNote)
                        || body.Contains(HtmlRenderer.ProvisionalComment);
                    SseStreamer.NoteComposedShape(shape, provisional);
                }
                ComposeBreaker.NoteSucceeded();
                // FM27 recreates a history page's BindingRoot on return. Keep
                // cursor memory attached to its actual native history slot,
                // only when this body still describes the interactive page.
                var history = MainThreadDispatcher.Invoke(() =>
                    ServerText.ScreenToken(InteractivePanels.Current()) == token
                        ? (GameHistory.TryRead()?.Index, ReplacedMessageHistory.CursorOrigin())
                        : ((int?)null, (string)null));
                return (body, RuntimeSurface.BodyHeader(token,title,history.Item1,language,history.Item2));
            }
            finally
            {
                if (companion) SseStreamer.CompanionComposeEnd();
            }
        }
        catch (Exception ex)
        {
            // This becomes the player's whole screen, so it must read as a state, not as a stack
            // trace: the detail goes to the log, and the page says what happened. A dispatch timeout
            // already carries the state's own sentence (see MainThreadDispatcher.Invoke).
            Speech.Degrade.Note($"screen render failed: {ex.GetType().Name}: {ex.Message}");
            // Out of time, either waiting for the walk or abandoned inside it. Both mean this screen
            // cannot be composed within the budget, so the retry is refused instead of run.
            if (ex is TimeoutException or MainThreadDispatcher.AbandonedException)
            {
                ComposeBreaker.NoteTimedOut();
                // A TimeoutException already carries the surface's own sentence; an abandoned walk
                // does not, so BusyPage composes one.
                return BusyPage(token, ex is TimeoutException ? ex.Message : null);
            }
            var message = ServerText.Escape("Screen unavailable.");
            return ($"<h1>{message}</h1><p role=\"status\" aria-live=\"assertive\">{message}</p>",
                    RuntimeSurface.BodyHeader(token,"Screen unavailable.",language:"en"));
        }
    }

    // The out-of-time page: the surface's own health sentence, naming the state the player is in and
    // whether it has lasted long enough to be worth a restart. Identical whether the compose was just
    // attempted or refused by the breaker, so a refusal never reads as a different problem.
    //
    // modHoldingMainThread is asserted rather than sampled: this page exists because the MOD's own
    // walk overran its budget, and at the instant a refusal is served that walk is over, so the live
    // reading would say "The game is busy." — blaming the game for the mod's cost.
    private static (string body, string header) BusyPage(string token, string timeoutMessage)
    {
        var status = timeoutMessage ?? SurfaceHealth.TimeoutMessage(
            MainThreadDispatcher.Health(),
            SurfaceHealth.IsProlonged(MainThreadDispatcher.MsSinceDrain()),
            modHoldingMainThread: true);
        var message = ServerText.Escape(status);
        return ($"<h1>{message}</h1><p role=\"status\" aria-live=\"assertive\">{message}</p>",
                RuntimeSurface.BodyHeader(token,status,language:"en"));
    }

    private static (string body, string header) Version(string query, string requestBody)
    {
        // The aggregate token over the interactive set, matching /body's X-FM-Token and the SSE
        // screen token (Phase 4) — so all three agree. Best-effort: a failed set read returns "?"
        // (the loading-page poll treats any change as "reload"), never crashes the poll.
        string token;
        try
        {
            token = MainThreadDispatcher.Invoke(() =>
            {
                var set = InteractivePanels.CurrentOrNull();
                return set != null ? ServerText.ScreenToken(set) : "?";
            });
        }
        catch { token = "?"; }
        return (token, null);
    }

    private static (string body, string header) DefaultPage(string query, string requestBody)
    {
        // Churn diagnosis: a served index right before an "SSE connect" means the companion PAGE
        // reloaded (fresh EventSource); an SSE connect without one is a transport-level reconnect.
        ServerText.LogBackground("index page served");
        string html;
        try
        {
            var (body, title, token, _, language) = ComposeBody();
            html = HtmlRenderer.WrapShell(title, token, body, language);
        }
        catch (Exception ex)
        {
            ServerText.LogBackground($"initial screen build failed: {ex}");
            html = InitialFailureDocument(ex.Message);
        }
        return (html, null);
    }

    // Keep the actual initial-failure document testable independently of the
    // native reader. This builder has no game state or command side effects.
    private static string InitialFailureDocument(string error)
    {
        string body = $"<p>Error building screen: {ServerText.Escape(error)}</p>";
        try
        {
            // Retain the inherited persistent client after a transient initial
            // read failure. The first real SSE token differs from "?", so the
            // existing refresh path reads the native body without a page reload
            // or replaying any native command.
            return HtmlRenderer.WrapShell("FM27 Access", "?", body, "en");
        }
        catch (Exception ex)
        {
            ServerText.LogBackground($"initial recovery shell failed: {ex}");
            // If the client assets themselves cannot load, still expose the
            // original contextual error instead of throwing out the response.
            return $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>FM27 Access</title></head><body>{body}</body></html>";
        }
    }

    // What /body serves when the surface cannot read the game. A fragment, not a document: the
    // companion morphs this into the page, and the heading is what its reader lands on. It replaces
    // what used to be here — the raw exception text, rendered as the player's entire screen.
    private static string UnavailablePage(string message)
    {
        var text = ServerText.Escape(message);
        return $"<h1>{text}</h1><p role=\"status\" aria-live=\"assertive\">{text}</p>";
    }

    // The liveness dump: which state the surface is in and the readings behind it. The one endpoint
    // that answers when the core has stopped or stalled, so it is where a "the mod says everything is
    // unavailable" report gets triaged.
    private static string Liveness()
    {
        var state = MainThreadDispatcher.Health();
        return $"state={state} ready={MainThreadDispatcher.Ready} stopped={MainThreadDispatcher.Stopped} "
             + $"msSinceDrain={MainThreadDispatcher.MsSinceDrain()} "
             + $"msSinceTickFailure={MainThreadDispatcher.MsSinceTickFailure()} "
             + $"msHoldingMainThread={MainThreadDispatcher.MsHoldingMainThread()}\n"
             + $"attribution={MainThreadDispatcher.StallAttribution()}\n"
             + $"{MainThreadDispatcher.HealthMessage()}\n";
    }

    private static string LoadingPage()
    {
        return SurfaceStartupHtml.Build();
    }
}
