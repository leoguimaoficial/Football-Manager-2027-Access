using FM.UI;
using FM27Access.Navigation;
using FM27Access.Navigation.Profiles;
using HarmonyLib;
using SI.Bindable;
using SI.Bindable.Events;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Patches;

public static class PanelOpenedObserver
{
    private static PanelManager _manager;
    private static readonly Queue<(SI.Bindable.Panel Panel, int Frame)> Pending = new();
    private static readonly PanelManager.PanelOpenedDelegate Opened =
        (PanelManager.PanelOpenedDelegate)(Action<SI.Bindable.Panel>)OnOpened;

    internal static void EnsureSubscribed()
    {
        var manager = PanelManager.Instance;
        if (manager == null || _manager?.Pointer == manager.Pointer) return;
        Unsubscribe();
        manager.add_OnPanelOpened(Opened);
        _manager = manager;
    }

    internal static void Unsubscribe()
    {
        Pending.Clear();
        Server.MatchTacticalPanelOwnership.Clear();
        Server.NativeEditSessionRefresh.Clear();
        if (_manager == null) return;
        _manager.remove_OnPanelOpened(Opened);
        _manager = null;
    }

    // A slow panel load looks identical from outside the process whether the mod or the game owns the
    // time: the main thread simply stops running frames, the surface reports "not responding", and the
    // player waits. This observer runs inside the native opened event, so it is the mod's share of
    // that time — timing it is the only way to answer the question rather than argue about it. Logged
    // only past the threshold, so a normal load stays quiet.
    private const long SlowLoadMs = 250;

    public static void OnOpened(SI.Bindable.Panel panel)
    {
        // OnPanelOpened is raised inside the native load method, before its
        // remaining callbacks finish. Never re-enter UI/navigation from it.
        Pending.Enqueue((panel, UnityEngine.Time.frameCount));
    }

    internal static void DrainPending()
    {
        int frame = UnityEngine.Time.frameCount;
        while (Pending.Count > 0 && Pending.Peek().Frame < frame)
        {
            var pending = Pending.Dequeue();
            if (pending.Panel.panel != null) AnnounceOpened(pending.Panel);
        }
        Server.MatchTacticalPanelOwnership.DrainPending();
        Server.NativeEditSessionRefresh.DrainPending();
    }

    private static void AnnounceOpened(SI.Bindable.Panel panel)
    {
        if (!AccessibilityMod.IsAlive) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        long announceMs = -1;
        string timedPanel = "?";
        try
        {
            if (panel.panel == null) return;
            var panelID = panel.PanelID;
            var title = panelID.Title;
            var translatedTitle = title.TranslatedString;

            // This event carries only Panel. Resolve its layer through the
            // common catalog, without a Layer-bearing Harmony trampoline.
            int? layerIndex=FM27PanelCatalog.GetNativeLayerForPanel(PanelManager.Instance,panel)?.Layer;
            Speech.Log.Info($"[PanelLoaded] name={panelID.name} ID={panelID.ID} title=\"{translatedTitle}\" layer={layerIndex} ptr=0x{panel.Pointer.ToInt64():X}");

            if (layerIndex is int nativeLayer)
            {
                int role = FM27PanelCatalog.SemanticRoleForNativeLayer(nativeLayer);
                if (!PanelObservationPolicy.IsKnownCursorLayer(role)
                    && !PanelObservationPolicy.CanOwnCursor(role,
                        FM27PanelCatalog.IsOpenAndInteractive(PanelManager.Instance, panelID)))
                {
                    Speech.Log.Info($"[PanelLoaded] Presentation panel \"{panelID.name}\" is not native-interactive; cursor/history unchanged.");
                    return;
                }
            }

            if (panelID.name == "TutorialDialog")
            {
                // The FM26 manager-level auto-dismiss races FM27's floating
                // tutorial construction. Keep its actual panel observable;
                // dismissal must use a bound native control once ready.
                Speech.Log.Info("[PanelLoaded] Native tutorial opened; manager-level auto-dismiss is not used.");
            }

            // Use the same actual native heading as the companion, including
            // dialogs whose PanelID title is unbound. Keep one title resolver.
            string panelTitle = Server.ServerText.GetTitle(panel);

            NavigationEventBlocker.Attach(panel);

            // A new panel is a new screen to compose, so a refusal held against the previous one no
            // longer applies — the player must never be left on a busy page for a screen that has
            // already gone (see ComposeBreaker).
            Server.ComposeBreaker.Reset();

            bool isLoadingScreen = layerIndex == (int)SI.UI.PanelLayerSettings.GetLayer("LoadingScreen");
            ProfileRegistry.ActivateForPanel(panelID.name ?? "");
            // Panels opening while a sim runs or an arrival report is due announce nothing —
            // the report names the landing and where the cursor sits. Modal dialogs always
            // punch through: a question the game is asking must never arrive silently.
            bool arrivalQuiet = layerIndex != (int)SI.UI.PanelLayerSettings.GetLayer("ModalDialog")
                                && (ContinueStatePatch.IsProcessing || Server.ArrivalReport.QuietWindowActive);
            timedPanel = panelID.name ?? "";
            long beforeAnnounce = watch.ElapsedMilliseconds;
            if (arrivalQuiet)
                NavigationSpeechMute.Run(() => NavigationManager.OnPanelReady(panel, panelTitle, isLoadingScreen));
            else
                NavigationSpeechMute.PanelEntry(() => Server.ServerText.GetAnnouncementTitle(panel),
                    () => NavigationManager.OnPanelReady(panel, panelTitle, isLoadingScreen));
            announceMs = watch.ElapsedMilliseconds - beforeAnnounce;
            AppearanceAutoSkip.OnPanelLoaded(panelID.name ?? "", panel);
            InterestDialogSync.OnPanelLoaded(panelID.name ?? "", panel);
        }
        catch (Exception ex)
        {
            // Announcing one panel failed. Degrade: log and continue so the mod stays alive for the
            // next panel/navigation instead of tearing down here. The missing announcement is the
            // visible signal, and a fixed core can be hot-swapped via /reload.
            Speech.Log.Error($"[PanelOpenedObserver] panel announcement failed, continuing: {ex}");
        }
        finally
        {
            long total = watch.ElapsedMilliseconds;
            if (total >= SlowLoadMs)
                Speech.Log.Info($"[PanelLoaded] SLOW: \"{timedPanel}\" took {total}ms in the mod"
                              + (announceMs >= 0 ? $" ({announceMs}ms announcing)" : " (aborted before announcing)"));
        }
    }

}

internal static class NavigationEventBlocker
{
    // The mod owns navigation through its own cursor, so it swallows the game's native
    // NavigationMove/Cancel events at every panel root. The dev driver's GameDriver, which
    // deliberately drives the game's native focus, sets this for the duration of one synthetic
    // SendEvent so that event passes through. Main-thread only (set and cleared inside an
    // Invoke), so there is no race with real input.
    public static bool AllowNext = false;

    // One instance each, rooted for the process lifetime, registered on every panel root.
    //
    // These are handed to the game, which keeps a raw pointer and calls back through an interop
    // trampoline. Two garbage collectors govern that pointer and nothing coordinates them: .NET owns
    // when the managed target dies, IL2CPP owns when the game's reference dies. A per-panel delegate
    // therefore has a window where the game still holds a pointer to a target .NET has reclaimed, and
    // the next navigation key on that panel lands in freed memory. That failure is an
    // ExecutionEngineException on the reverse-P/Invoke stub: below the level any catch, log or
    // degrade marker can reach, so it takes the process with it and leaves nothing in the log.
    // Confirmed from a crash dump — see the fault stack in docs/reference/failure-handling.md.
    //
    // Static fields close both sides at once: the field roots the managed target, and holding the
    // Il2CppObjectBase wrapper holds its IL2CPP GC handle, so the game's pointer stays valid too.
    // The lambdas capture nothing, so a single shared instance is equivalent to a fresh one, and
    // re-attaching to the same root now dedups instead of stacking a second swallower.
    private static readonly EventCallback<NavigationMoveEvent> SwallowMove =
        (EventCallback<NavigationMoveEvent>)(evt =>
        {
            if (AllowNext) return;
            evt.StopImmediatePropagation();
            evt.PreventDefault();
        });

    private static readonly EventCallback<NavigationCancelEvent> SwallowCancel =
        (EventCallback<NavigationCancelEvent>)(evt =>
        {
            if (AllowNext) return;
            evt.StopImmediatePropagation();
            evt.PreventDefault();
        });

    public static void Attach(VisualElement root)
    {
        root.RegisterCallback<NavigationMoveEvent>(SwallowMove, TrickleDown.TrickleDown);
        root.RegisterCallback<NavigationCancelEvent>(SwallowCancel, TrickleDown.TrickleDown);
    }
}

[HarmonyPatch(typeof(PanelHistoryManager), "OnBackEvent")]
public static class PanelHistoryBackPatch
{
    public static bool AllowNextBackEvent;

    // Some in-game buttons close their screen by raising the history back event internally
    // (contract negotiation "Exit Talks" and "Cancel Negotiations", quick chat "End Chat"). A
    // mod-triggered click on any button is a deliberate player action, so ButtonAdapter opens
    // this short window around the click. The game defers the resulting back event — measured
    // 2 frames after the click for Cancel Negotiations — so the window is 3 frames (~50ms),
    // still far below the speed at which a player could click and then press the Left Arrow the
    // suppression exists to block.
    private const int AllowBackWindowFrames = 3;
    private static int _allowUntilFrame = -1;
    private static int _armedAtFrame = -1;

    public static void AllowBackThroughNextFrame()
    {
        _armedAtFrame = UnityEngine.Time.frameCount;
        _allowUntilFrame = _armedAtFrame + AllowBackWindowFrames;
    }

    [HarmonyPrefix]
    public static bool Prefix()
    {
        if (!AccessibilityMod.IsAlive) return true;
        try
        {
            if (AllowNextBackEvent)
            {
                AllowNextBackEvent = false;
                Speech.Log.Info("[PanelHistoryBackPatch] Allowed mod-triggered back navigation");
                return true;
            }

            if (UnityEngine.Time.frameCount <= _allowUntilFrame)
            {
                Speech.Log.Info("[PanelHistoryBackPatch] Allowed back navigation raised by an activated button");
                return true;
            }

            Speech.Log.Info($"[PanelHistoryBackPatch] Suppressed back navigation (frame {UnityEngine.Time.frameCount}, last click armed at {_armedAtFrame})");
            return false;
        }
        catch (Exception ex)
        {
            // Could not decide whether to suppress this back event. Degrade: log and keep the mod's
            // default of suppressing native back (return false) rather than killing the mod or
            // letting an unexpected panel change happen under the player on a transient failure.
            Speech.Log.Error($"[PanelHistoryBackPatch] back-event handling failed, suppressing native: {ex}");
            return false;
        }
    }
}

[HarmonyPatch(typeof(PanelManager), "PanelClosed")]
public static class PanelClosedPatch
{
    [HarmonyPostfix]
    public static void Postfix(SI.Bindable.Panel panel)
    {
        if (!AccessibilityMod.IsAlive) return;
        PatchTiming.Run("PanelClosed", () =>
        {
            try
            {
                var panelID = panel.PanelID;
                Speech.Log.Info($"[PanelClosed] name={panelID.name} ID={panelID.ID} ptr=0x{panel.Pointer.ToInt64():X}");

                // A close during the arrival window re-announces the panel beneath — same chatter
                // as a load, muted the same way (the stack bookkeeping still runs).
                bool arrivalQuiet = ContinueStatePatch.IsProcessing || Server.ArrivalReport.QuietWindowActive;
                var wasActivePanel = arrivalQuiet
                    ? NavigationSpeechMute.Run(() => NavigationManager.OnPanelClosed(panel))
                    : NavigationSpeechMute.PanelEntry(() => Server.ServerText.GetAnnouncementTitle(NavigationManager.GetActivePanel()),
                        () => NavigationManager.OnPanelClosed(panel));
                if (wasActivePanel)
                    ProfileRegistry.ClearActiveProfile();
            }
            catch (Exception ex)
            {
                // A panel-closed bookkeeping failure must not tear the mod down. Log and continue.
                Speech.Log.Error($"[PanelClosedPatch] panel-closed handling failed, continuing: {ex}");
            }
        });
    }
}
