using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using FM27Access.Navigation.Input;

namespace FM27Access.Plugin;

/// <summary>
/// The mod's per-frame logic, driven by the host's injected HostBehaviour via the bridge tick.
/// Was AccessibilityBehaviour (an injected MonoBehaviour); now a plain reloadable class, because
/// the IL2CPP injection cannot be re-run on reload. Owns the load-state machine, the input system,
/// the rescan/cursor-restore loops, and draining the main-thread dispatcher.
///
/// On a reload (the game is already loaded) the wait-for-load states and startup diagnostics are
/// skipped: the loop goes straight to rebuilding the input system and marking the dispatcher ready.
/// </summary>
internal sealed class CoreLoop
{
    private enum LoadState { WaitingForManager, WaitingForLoadingDone, WaitingForPanelContent, RunningDiagnostics, Done }
    private LoadState _state;
    private readonly bool _isReload;
    private int _panelWaitFrames;
    private int _pendingRescanFrames;

    private int _emptyPanelRescanFrames;
    private const int EmptyPanelRescanInterval = 5;
    private const int EmptyPanelRescanMaxAttempts = 40;

    private int _cursorRestoreFrames;
    private const int CursorRestoreInterval = 5;
    private const int CursorRestoreMaxAttempts = 40;

    // Startup quiet window. Booting walks the player through panels they never chose to visit —
    // the splash, the watermark, the version stamp, two loading screens — each announced by its
    // internal panel name ("GameVersion", "PhotosensitivityWarning_SplashScreen"), and the mod
    // used to talk over them twice more with its own progress and self-test results. None of it
    // tells the player anything they can act on. Hold the navigation mute from mount until the
    // main menu is up, then say that once. Released on a budget (and on shutdown) so a boot that
    // never reaches the main menu cannot leave the mute wedged shut.
    private const string MainMenuPanelName = "MainMenu";
    private const string MainMenuArrival = "Main menu loaded";
    // Budgets are wall-clock, not frame counts: the splash and loading screens render uncapped
    // (a frame-based cap sized for 60fps burned five minutes' worth of frames in seconds and let
    // the splash chatter through).
    private const long SlowTickMs = 250;            // a tick this long is holding the frame, not filling it
    private const long MainMenuWaitMs = 10000;      // after loading finishes, before giving up on the menu
    private const long StartupQuietMaxMs = 300000;  // backstop on a boot that never finishes at all
    private const long MainMenuSettleMs = 200;      // past the menu appearing, to swallow its own announcement
    private bool _startupQuietHeld;
    private bool _panelReadFailureLogged;
    private long _startupQuietStartMs;
    private long _loadDoneMs;
    private long _mainMenuSeenMs;
#if FM27_DEVELOPER_TOOLS
    private bool _diagnosticsPending;
    private long _nextDiagnosticsCheckMs;
#endif

    // Handler stack input system
    private Input.BindingRegistry _bindingRegistry;
    private Input.InputActionBridgeImpl _bridge;
    private InputBlockingCoordinator _coordinator;
    private HandlerStack _handlerStack;
    private Input.KeyboardPoller _poller;
    private Navigation.IAccessibleElement _lastCursorElement;

    public CoreLoop(bool isReload)
    {
        _isReload = isReload;
#if FM27_DEVELOPER_TOOLS
        _diagnosticsPending = !isReload;
#endif
        // On reload the game is already loaded; jump past the wait-for-load states straight to
        // input init (diagnostics are skipped in the RunningDiagnostics case below).
        _state = isReload ? LoadState.RunningDiagnostics : LoadState.WaitingForManager;

        // Only a real boot walks the pre-menu panels; a reload lands on whatever the player was
        // already looking at and says "Mod reloaded" for itself.
        if (!isReload)
        {
            NavigationSpeechMute.Push();
            _startupQuietHeld = true;
            _startupQuietStartMs = Environment.TickCount64;
        }
    }

    /// <summary>
    /// Restore the game to a clean input state on Unmount. The input system disables game
    /// InputActions for every claimed key; re-enable them all so the game is fully usable while the
    /// mod is paused (the next Mount rebuilds the registry and re-disables as needed).
    /// </summary>
    public void Shutdown()
    {
        Server.FilterPopupSurface.CancelPendingClose();
        Server.ReplacedMessageHistory.Reset();
        try { Server.NativeDestinationTab.Shutdown(); }
        catch (Exception ex) { Speech.Log.Warn($"[CoreLoop] Failed to release native destination bindings: {ex.Message}"); }
        try { Patches.PanelOpenedObserver.Unsubscribe(); }
        catch (Exception ex) { Speech.Log.Warn($"[CoreLoop] Panel observer unsubscribe failed: {ex.Message}"); }
        // A discarded core must not leave the navigation mute it opened held shut: the depth counter
        // is static and survives into the next core.
        ReleaseStartupQuiet();

        try { _bridge?.EnableAllKeyboardActions(); }
        catch (Exception ex) { Speech.Log.Warn($"[CoreLoop] Failed to re-enable game input on shutdown: {ex.Message}"); }

        // A discarded core must never leave the game's audio muted from a fast-forward.
        try { Server.GameAudioMute.Restore(); }
        catch (Exception ex) { Speech.Log.Warn($"[CoreLoop] Failed to restore game audio on shutdown: {ex.Message}"); }

        // Drop the touch sampler's live handles; a stale holder must not survive into the next core.
        try { Server.TouchSampler.Shutdown(); }
        catch (Exception ex) { Speech.Log.Warn($"[CoreLoop] Failed to shut down the touch sampler: {ex.Message}"); }
    }

    public void Tick()
    {
        if (!AccessibilityMod.IsAlive) return;
        Patches.PanelOpenedObserver.EnsureSubscribed();
        Patches.PanelOpenedObserver.DrainPending();
        Server.FilterPopupSurface.DrainPendingClose();

        // Before anything else, so the window closes on its own budget even if the states below
        // stall: the player must never be left in a silence the mod is holding open.
        if (_startupQuietHeld) TickStartupQuiet();

        if (_state == LoadState.Done)
        {
            long tickStart = Environment.TickCount64;
            // Recorded for the surface, not just the log: the tick runs ahead of the drain, so a tick
            // that holds the main thread starves the hand-off exactly as a stopped game does. Without
            // this the surface would report the game as unresponsive for the mod's own time.
            Server.MainThreadDispatcher.NoteTickEnter();
            try
            {
#if FM27_DEVELOPER_TOOLS
                TryRunStartupDiagnostics();
#endif
                TickDone();
            }
            catch (Exception ex)
            {
                // A per-frame interactive failure (announce, activation, rescan, a context or
                // adapter read) must not tear the mod down. Log it and continue next frame: the mod
                // stays alive and diagnosable over HTTP, and a fixed core can be hot-swapped via
                // /reload. This guard covers only the live interactive loop — foundation failures
                // during init (the switch states below) still halt via Kill, because nothing the
                // mod says can be trusted after those.
                Speech.Log.Error($"[CoreLoop] Per-frame tick failed, continuing: {ex}");
                Server.MainThreadDispatcher.NoteTickFailure();
            }
            finally
            {
                // Always, even on a throw. The server's every read is handed to this queue, so a tick
                // that fails before reaching the drain would silently take the whole companion
                // surface down with it — one throwing frame per frame is indistinguishable, from the
                // outside, from a game that has stopped running.
                Server.MainThreadDispatcher.NoteTickExit();
                long tickMs = Environment.TickCount64 - tickStart;
                if (tickMs >= SlowTickMs)
                    Speech.Log.Warn($"[CoreLoop] TickDone held the main thread for {tickMs}ms");
                Server.MainThreadDispatcher.Drain();
            }
            return;
        }

        switch (_state)
        {
            case LoadState.WaitingForManager:
                if (SI.Core.LoadingScreenManager.Exists())
                {
                    Speech.Log.Info("LoadingScreenManager detected.");
                    _state = LoadState.WaitingForLoadingDone;
                }
                break;

            case LoadState.WaitingForLoadingDone:
                try
                {
                    var instance = SI.Core.LoadingScreenManager.Instance;
                    if (instance != null && !instance.IsScreenOpen())
                    {
                        Speech.Log.Info("Loading screen closed. Waiting for panel content...");
                        _panelWaitFrames = 0;
                        _state = LoadState.WaitingForPanelContent;
                    }
                }
                catch (Il2CppInterop.Runtime.Il2CppException)
                {
                    // LoadingScreenManager exists but has uninitialized internal state — wait
                }
                break;

            case LoadState.WaitingForPanelContent:
                _panelWaitFrames++;
                if (PanelHasContent())
                {
                    Speech.Log.Info($"Panel content ready after {_panelWaitFrames} frames.");
                    _state = LoadState.RunningDiagnostics;
                }
                else if (_panelWaitFrames > 600)
                {
                    Speech.Log.Warn("Timed out waiting for panel content. Initializing input; UI diagnostics still require an interactive foreground.");
                    _state = LoadState.RunningDiagnostics;
                }
                break;

            case LoadState.RunningDiagnostics:
                if (_isReload)
                    Speech.Log.Info("[CoreLoop] Reload: skipping diagnostics, reinitializing input system.");
                // Cold UI diagnostics wait for a real interactive foreground.
                // A loading manager can close while only its splash/background
                // is available; that absence is not a failed button/text API.
                InitializeInputSystem();
                // InitializeInputSystem calls Kill() and returns (without throwing) when the game's
                // input asset can't be read. If it did, the mod is dead: do not mark Ready, re-establish
                // the panel, or speak — announcing "Mod reloaded" after a Kill would be a lie. The next
                // Tick short-circuits on the !IsAlive guard at the top of this method.
                if (!AccessibilityMod.IsAlive) return;
                _state = LoadState.Done;
                Server.MainThreadDispatcher.Ready = true;
                if (_isReload)
                {
                    ReestablishActivePanel();
                    Speech.SpeechPipeline.SpeakInterrupt("Mod reloaded.");
                }
                break;
        }
    }

    /// <summary>
    /// Close the startup quiet window once the main menu is the active panel, announcing it as the
    /// one line the boot speaks. Matched on the panel's own name (a code identifier, stable across
    /// languages and saves), never its title. A boot that lands somewhere else — a straight-to-save
    /// launch, a menu that never opens — falls out on the frame budget: the window reopens speech
    /// and logs why, rather than announcing a main menu that is not there.
    /// </summary>
    private void TickStartupQuiet()
    {
        long now = Environment.TickCount64;

        // The absolute cap. Loading is legitimately long and the states below have no deadline of
        // their own before Done, so the menu-wait budget cannot start at mount — but nothing may
        // hold the player's speech shut indefinitely either.
        if (now - _startupQuietStartMs > StartupQuietMaxMs)
        {
            ReleaseStartupQuiet();
            Speech.Log.Warn("[CoreLoop] Startup quiet window hit its absolute cap before the game "
                            + "finished loading; speech reopened without an arrival line.");
            return;
        }

        // Still loading: stay quiet without spending the menu-wait budget.
        if (_state != LoadState.Done) return;
        if (_loadDoneMs == 0) _loadDoneMs = now;

        if (IsMainMenuActive())
        {
            // Hold briefly past the menu appearing before reopening speech. The panel's own
            // announcement is raised from the game's UI update, whose order against this tick is not
            // guaranteed, so releasing the moment the panel is seen can let "MainMenu. 26.3.2"
            // through right behind the arrival line. Waiting keeps the boot to one utterance.
            if (_mainMenuSeenMs == 0) _mainMenuSeenMs = now;
            if (now - _mainMenuSeenMs < MainMenuSettleMs) return;
            ReleaseStartupQuiet();
            Speech.SpeechPipeline.SpeakInterrupt(MainMenuArrival);
            return;
        }

        // Loaded, but somewhere other than the main menu (a straight-to-save launch, a boot that
        // opens elsewhere): reopen speech and say nothing rather than name a screen that is not up.
        if (now - _loadDoneMs > MainMenuWaitMs)
        {
            ReleaseStartupQuiet();
            Speech.Log.Warn($"[CoreLoop] No main menu within {MainMenuWaitMs}ms of loading; "
                            + "speech reopened without an arrival line.");
        }
    }

    private bool IsMainMenuActive()
    {
        try
        {
            var panel = Navigation.NavigationManager.GetActivePanel();
            return panel != null && Navigation.NavigationManager.GetPanelName(panel) == MainMenuPanelName;
        }
        catch (Exception ex)
        {
            // An unreadable panel must not strand the mute: treat it as "not the menu yet" and let
            // the budget close the window. Logged once — this runs every frame.
            if (!_panelReadFailureLogged)
            {
                _panelReadFailureLogged = true;
                Speech.Log.Warn($"[CoreLoop] Active panel read failed while waiting for the main menu: {ex.Message}");
            }
            return false;
        }
    }

    private void ReleaseStartupQuiet()
    {
        if (!_startupQuietHeld) return;
        _startupQuietHeld = false;
        NavigationSpeechMute.Pop();
    }

    /// <summary>
    /// The per-frame interactive work once the game is loaded: pending rescans, empty-panel rescan,
    /// cursor restore, cursor-change side effects, and draining input/dispatcher. Wrapped by Tick in
    /// a degrade catch so a localized failure here logs and continues rather than halting the mod.
    /// </summary>
    private void TickDone()
    {
        // A reload can begin inside a card whose background opened before this core
        // existed. Closing that card leaves no speech-stack entry to restore; recover
        // from FM27's actual open-panel navigation registry, as during mount.
        if(Navigation.NavigationManager.GetActivePanel()==null&&GetCurrentPanel()!=null)ReestablishActivePanel();
        // The arrival report's settle fallback: a continue flow parked on a transient state
        // (Waiting/Saving) still reports after its timeout.
        Server.ArrivalReport.Tick();

        // Game-audio mute while match playback fast-forwards between highlights (self-throttled).
        Server.GameAudioMute.Tick();

        // Ball-touch detection for the last-touch possession judgement (cheap per frame; the player
        // scan inside runs only when a touch fired).
        Server.TouchSampler.Tick();

        if (_pendingRescanFrames > 0)
        {
            _pendingRescanFrames--;
            if (_pendingRescanFrames == 0)
            {
                // Falls into the announcing rescan when the cursor went invisible — same
                // arrival-window mute as the empty-panel rescan below.
                NavigationSpeechMute.Run(Navigation.NavigationManager.RescanPanelKeepCursor);
            }
        }
        if (Navigation.NavigationManager.NeedsRescan)
        {
            _emptyPanelRescanFrames++;
            if (_emptyPanelRescanFrames % EmptyPanelRescanInterval == 0)
            {
                // An empty panel gaining stops announces its first stop. Inside the arrival
                // window — or when the report spoke while this rescan was still pending —
                // that announcement is muted so it cannot talk over the report; the report
                // named the landing, and an arrow key reveals the cursor.
                NavigationSpeechMute.Run(() => Navigation.NavigationManager.RescanPanel());
                if (_emptyPanelRescanFrames >= EmptyPanelRescanInterval * EmptyPanelRescanMaxAttempts)
                {
                    Speech.Log.Warn("[CoreLoop] Panel still empty after max rescan attempts, giving up.");
#if FM27_DEVELOPER_TOOLS
                    var activePanel = Navigation.NavigationManager.GetActivePanel();
                    if (activePanel is UnityEngine.UIElements.VisualElement ve)
                        ElementBridge.DumpTree(ve);
#endif
                    Navigation.NavigationManager.NeedsRescan = false;
                    _emptyPanelRescanFrames = 0;
                }
            }
        }
        else
        {
            _emptyPanelRescanFrames = 0;
            Server.ArrivalReport.MuteActiveRescan = false;
        }
        if (Navigation.NavigationManager.NeedsCursorRestore)
        {
            _cursorRestoreFrames++;
            if (_cursorRestoreFrames % CursorRestoreInterval == 0)
            {
                // A restore succeeding inside the arrival window — or one that was already
                // pending when the report spoke — announces silently: the report reads the
                // cursor live at compose time, and a restore landing after it must not cut
                // the report off.
                NavigationSpeechMute.Run(Navigation.NavigationManager.TryRestoreCursor);
                if (_cursorRestoreFrames >= CursorRestoreInterval * CursorRestoreMaxAttempts)
                {
                    Speech.Log.Warn("[CoreLoop] Cursor restore timed out, giving up.");
                    Navigation.NavigationManager.NeedsCursorRestore = false;
                    _cursorRestoreFrames = 0;
                }
            }
        }
        else
        {
            _cursorRestoreFrames = 0;
            Server.ArrivalReport.MuteActiveRestore = false;
        }
        var currentElement = Navigation.NavigationManager.GetCurrentElement();
        if (currentElement != _lastCursorElement)
        {
            _lastCursorElement = currentElement;
            Adapters.FormButtonAdapter.CancelPending();
            if (UnityEngine.Application.isFocused && currentElement is Adapters.FormButtonAdapter formButton)
                formButton.TriggerAsyncTooltipRead();
        }
        if (!UnityEngine.Application.isFocused) Adapters.FormButtonAdapter.CancelPending();
        Adapters.FormButtonAdapter.Tick();
        _poller.Poll();
    }

    /// <summary>
    /// After a reload the game fired no panel-load event, so NavigationManager has no active panel.
    /// Re-establish it from the currently open panel so the speech cursor works immediately.
    /// </summary>
    private void ReestablishActivePanel()
    {
        var panel = GetCurrentPanel();
        if (panel == null)
        {
            Speech.Log.Warn("[CoreLoop] Reload: no open panel to re-establish.");
            return;
        }

        var title = Server.ServerText.GetTitle(panel);
        if (string.IsNullOrEmpty(title))
            title = panel.PanelID.name ?? "unknown panel";

        Navigation.Profiles.ProfileRegistry.ActivateForPanel(panel.PanelID.name ?? "");
        NavigationSpeechMute.PanelEntry(() => Server.ServerText.GetAnnouncementTitle(panel),
            () => Navigation.NavigationManager.OnPanelReady(panel, title, false));
    }

    private void InitializeInputSystem()
    {
        try
        {
            var navManager = UnityEngine.Object.FindObjectOfType<FM.UI.FMNavigationManager>();
            if (navManager == null)
            {
                Speech.Log.Fatal("[InputSystem] FMNavigationManager not found. Cannot build binding registry.");
                AccessibilityMod.Kill();
                return;
            }

            var runtimeType = navManager.GetType();
            var flags = System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance;

            var assetProp = runtimeType.GetProperty("m_inputActionAsset", flags);
            var asset = assetProp?.GetValue(navManager) as UnityEngine.InputSystem.InputActionAsset;
            if (asset == null)
            {
                Speech.Log.Fatal("[InputSystem] Could not read InputActionAsset from FMNavigationManager.");
                AccessibilityMod.Kill();
                return;
            }

            _bindingRegistry = new Input.BindingRegistry();
            _bindingRegistry.Build(asset);

            _bridge = new Input.InputActionBridgeImpl(_bindingRegistry);
            _handlerStack = new HandlerStack();
            _coordinator = new InputBlockingCoordinator(_bridge, _handlerStack.GetAllHandlers);
            _handlerStack.SetCoordinator(_coordinator);
            _poller = new Input.KeyboardPoller(_handlerStack, _coordinator);

            _handlerStack.Push(new Input.Handlers.DefaultScreenHandler(_handlerStack, RequestRescan));
            _handlerStack.Push(new Input.Handlers.TeamSelectionHandler());
            Speech.Log.Info("[InputSystem] Handler stack initialized with DefaultScreenHandler + TeamSelectionHandler.");
        }
        catch (Exception ex)
        {
            Speech.Log.Fatal($"[InputSystem] Initialization failed: {ex}");
            AccessibilityMod.Kill();
            throw;
        }
    }

    public void RequestRescan(int frames)
    {
        _pendingRescanFrames = frames;
    }

#if FM27_DEVELOPER_TOOLS
    private void TryRunStartupDiagnostics()
    {
        if (!_diagnosticsPending || Environment.TickCount64 < _nextDiagnosticsCheckMs) return;
        _nextDiagnosticsCheckMs = Environment.TickCount64 + 500;
        if (Environment.TickCount64 - _startupQuietStartMs > StartupQuietMaxMs)
        {
            _diagnosticsPending = false;
            Speech.Log.Warn("[DIAG] Not run: no interactive foreground with native button/text became available during startup.");
            return;
        }
        try
        {
            var panel = GetCurrentPanel();
            if (panel == null) return;
            bool button = false, text = false;
            WalkTree(panel, element =>
            {
                if ((button && text) || !ElementBridge.IsVisible(element)) return;
                if (element.GetIl2CppType().Name == "SIButton") button = true;
                if (element.TryCast<TextElement>() is { } label && !string.IsNullOrWhiteSpace(label.text)) text = true;
            });
            if (!button || !text) return;
            _diagnosticsPending = false;
            RunDiagnostics();
        }
        catch (Exception ex)
        {
            _diagnosticsPending = false;
            Speech.Log.Error($"[DIAG] Foreground readiness inspection failed: {ex}");
        }
    }

    private void RunDiagnostics()
    {
        int passed = 0;
        int total = 5;

        if (Diag02_InputRegistry()) passed++;
        if (Diag03_TypeChecking()) passed++;
        if (Diag04_ReadText()) passed++;
        if (Diag05_SimulateButtonClick()) passed++;
        if (Diag06_PanelTitle()) passed++;

        // Log only: a boot self-test is developer output, not something the player can act on.
        // A failure that matters to them halts the mod and says so through Kill.
        Speech.Log.Info($"[DIAG] === Results: {passed}/{total} passed ===");
    }

    private bool Diag02_InputRegistry()
    {
        try
        {
            var actions = _bindingRegistry?.AllKeyboardActions;
            if (_bridge == null || _coordinator == null || actions == null || actions.Count == 0)
            {
                Speech.Log.Error("[DIAG 0.2] FAIL: Native keyboard action registry/input bridge is not initialized.");
                return false;
            }
            Speech.Log.Info($"[DIAG 0.2] PASS: Native InputAction registry exposes {actions.Count} keyboard actions; input bridge initialized. Key interception is checked separately by functional tests.");
            return true;
        }
        catch (Exception e)
        {
            Speech.Log.Error($"[DIAG 0.2] FAIL: Exception — {e}");
            return false;
        }
    }

    private bool Diag03_TypeChecking()
    {
        try
        {
            var panel = GetCurrentPanel();
            if (panel == null)
            {
                Speech.Log.Error("[DIAG 0.3] FAIL: Could not get current panel.");
                return false;
            }

            VisualElement foundButton = null;
            WalkTree(panel, el =>
            {
                if (foundButton != null) return;
                try
                {
                    if (el is SI.Bindable.SIButton)
                    {
                        foundButton = el;
                    }
                }
                catch { }
            });

            if (foundButton != null)
            {
                Speech.Log.Info($"[DIAG 0.3] PASS: 'element is SIButton' works. Found: {foundButton.name}");

                try
                {
                    var typeName = foundButton.GetIl2CppType().Name;
                    Speech.Log.Info($"[DIAG 0.3] INFO: GetIl2CppType().Name = \"{typeName}\"");
                }
                catch (Exception e)
                {
                    Speech.Log.Warn($"[DIAG 0.3] INFO: GetIl2CppType() failed — {e.Message}");
                }

                return true;
            }

            Speech.Log.Warn("[DIAG 0.3] INFO: 'element is SIButton' found nothing. Trying GetIl2CppType fallback...");
            WalkTree(panel, el =>
            {
                if (foundButton != null) return;
                try
                {
                    if (el.GetIl2CppType().Name == "SIButton")
                    {
                        foundButton = el;
                    }
                }
                catch { }
            });

            if (foundButton != null)
            {
                Speech.Log.Info($"[DIAG 0.3] PASS (fallback): GetIl2CppType().Name works. Found: {foundButton.name}");
                return true;
            }

            Speech.Log.Warn("[DIAG 0.3] INFO: GetIl2CppType fallback found nothing. Trying USS class check...");
            WalkTree(panel, el =>
            {
                if (foundButton != null) return;
                try
                {
                    if (el.ClassListContains("sibutton"))
                    {
                        foundButton = el;
                    }
                }
                catch { }
            });

            if (foundButton != null)
            {
                Speech.Log.Info($"[DIAG 0.3] PASS (USS): USS class check works. Found: {foundButton.name}");
                return true;
            }

            var typeNames = new HashSet<string>();
            WalkTree(panel, el =>
            {
                try { typeNames.Add(el.GetIl2CppType().Name); }
                catch { }
            });
            Speech.Log.Error($"[DIAG 0.3] FAIL: No SIButton found by any method. Types found: {string.Join(", ", typeNames.OrderBy(t => t))}");
            return false;
        }
        catch (Exception e)
        {
            Speech.Log.Error($"[DIAG 0.3] FAIL: Exception — {e}");
            return false;
        }
    }

    private bool Diag04_ReadText()
    {
        try
        {
            var panel = GetCurrentPanel();
            if (panel == null)
            {
                Speech.Log.Error("[DIAG 0.4] FAIL: Could not get current panel.");
                return false;
            }

            bool foundAny = false;

            WalkTree(panel, el =>
            {
                if (foundAny) return;
                try
                {
                    var typeName = el.GetIl2CppType().Name;
                    if (typeName == "SIText" || typeName == "TextElement" || typeName == "Label")
                    {
                        var textEl = el.Cast<TextElement>();
                        var text = textEl.text;
                        if (!string.IsNullOrEmpty(text))
                        {
                            Speech.Log.Info($"[DIAG 0.4] PASS: text readable via Cast<TextElement>(). Type: {typeName}, Content: \"{Truncate(text, 80)}\"");
                            foundAny = true;
                        }
                    }
                }
                catch { }
            });

            if (foundAny) return true;

            WalkTree(panel, el =>
            {
                if (foundAny) return;
                try
                {
                    var typeName = el.GetIl2CppType().Name;
                    if (typeName.Contains("Text"))
                    {
                        var textEl = el.Cast<TextElement>();
                        var text = textEl.text;
                        if (!string.IsNullOrEmpty(text))
                        {
                            Speech.Log.Info($"[DIAG 0.4] PASS: text readable (broad search). Type: {typeName}, Content: \"{Truncate(text, 80)}\"");
                            foundAny = true;
                        }
                    }
                }
                catch { }
            });

            if (foundAny) return true;

            Speech.Log.Error("[DIAG 0.4] FAIL: No readable text found in panel tree.");
            return false;
        }
        catch (Exception e)
        {
            Speech.Log.Error($"[DIAG 0.4] FAIL: Exception — {e}");
            return false;
        }
    }

    private bool Diag05_SimulateButtonClick()
    {
        try
        {
            var panel = GetCurrentPanel();
            if (panel == null)
            {
                Speech.Log.Error("[DIAG 0.5] FAIL: Could not get current panel.");
                return false;
            }

            SI.Bindable.SIButton button = null;
            WalkTree(panel, el =>
            {
                if (button != null) return;
                try
                {
                    if (el.GetIl2CppType().Name == "SIButton")
                        button = el.Cast<SI.Bindable.SIButton>();
                }
                catch { }
            });

            if (button == null)
            {
                Speech.Log.Error("[DIAG 0.5] FAIL: No SIButton found to test.");
                return false;
            }

            Speech.Log.Info($"[DIAG 0.5] INFO: SIButton found via IL2CPP cast. Name: \"{button.name}\"");

            var method = AccessTools.Method(typeof(SI.Bindable.SIButton), "SimulateButtonClick");
            if (method != null)
            {
                Speech.Log.Info($"[DIAG 0.5] PASS: SimulateButtonClick method found on SIButton.");
                return true;
            }

            Speech.Log.Error("[DIAG 0.5] FAIL: SimulateButtonClick method not found on SIButton.");
            return false;
        }
        catch (Exception e)
        {
            Speech.Log.Error($"[DIAG 0.5] FAIL: Exception — {e}");
            return false;
        }
    }

    private bool Diag06_PanelTitle()
    {
        try
        {
            var panel = GetCurrentPanel();
            if (panel == null)
            {
                Speech.Log.Error("[DIAG 0.6] FAIL: No open panel found.");
                return false;
            }

            var panelId = panel.PanelID;
            Speech.Log.Info($"[DIAG 0.6] INFO: PanelID obtained. GameID: {panelId.Title.GameID}");

            var title = panelId.Title.TranslatedString;
            if (!string.IsNullOrEmpty(title))
            {
                Speech.Log.Info($"[DIAG 0.6] PASS: Panel title = \"{title}\"");
                return true;
            }

            var text = panelId.Title.Text;
            if (!string.IsNullOrEmpty(text))
            {
                Speech.Log.Info($"[DIAG 0.6] PASS (via Text): Panel title = \"{text}\"");
                return true;
            }

            Speech.Log.Info($"[DIAG 0.6] PASS: PanelID accessible. Title is empty (expected for some panels).");
            return true;
        }
        catch (Exception e)
        {
            Speech.Log.Error($"[DIAG 0.6] FAIL: Exception — {e}");
            return false;
        }
    }

#endif

    // --- Helpers ---

    private bool PanelHasContent()
    {
        try
        {
            var panel = GetCurrentPanel();
            if (panel == null) return false;

            bool found = false;
            WalkTree(panel, el =>
            {
                if (found) return;
                try
                {
                    var typeName = el.GetIl2CppType().Name;
                    if (typeName != "VisualElement" && typeName != "Panel" && typeName != "BindingRoot")
                        found = true;
                }
                catch { }
            });
            return found;
        }
        catch { return false; }
    }

    private SI.Bindable.Panel GetCurrentPanel()
    {
        if (!SI.Bindable.PanelManager.Exists())
        {
            Speech.Log.Debug("[DIAG] PanelManager does not exist.");
            return null;
        }

        var pm = SI.Bindable.PanelManager.Instance;
        return FM27PanelCatalog.GetForegroundPanel();
    }

    /// <summary>
    /// Diagnostic-only tree walker. Intentionally separate from TreeWalker:
    /// uses bare catches for IL2CPP resilience in startup diagnostics.
    /// </summary>
    private static void WalkTree(VisualElement root, Action<VisualElement> visitor)
    {
        visitor(root);
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            WalkTree(root.hierarchy[i], visitor);
    }

#if FM27_DEVELOPER_TOOLS
    private static string Truncate(string s, int max)
    {
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "...";
    }
#endif
}
