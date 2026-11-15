using System;
using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Speech;
using SI.Bindable;

namespace FM27Access.Plugin.Server;

/// <summary>
/// THE GATE (docs/archive/plans/removing panel cycling plan.md §6): the single source of truth for which panels
/// the HTML surface renders. The live, game-touching seam over the pure
/// <see cref="InteractivePanelSet"/> selection — it reads each open panel's layer, real visibility,
/// and the game's own <see cref="PanelManager.IsOpenAndInteractive"/> verdict, then lets the pure
/// logic decide the render set.
///
/// Cornerstone invariant: no panel reaches the page except through here, and a failed interactivity
/// query fails closed rather than over-rendering. We never fall back to "show everything" — that would
/// reintroduce the lie (a dead panel presented as usable). The safe default is the opposite of
/// permissive: when an overlay has focus the set is just that overlay. Failing closed is what upholds
/// the invariant; halting the mod is a separate decision, and now happens only when the failure
/// persists (see <see cref="Current"/>).
///
/// Main thread only (reads live game objects), like all PanelManager access.
/// </summary>
internal static class InteractivePanels
{
    internal readonly struct PanelView
    {
        public readonly Panel Panel;
        public readonly string Title;
        public readonly int LayerIndex;
        public readonly bool IsTitleBar;
        public readonly bool IsOverlay;

        public PanelView(Panel panel, string title, int layerIndex, bool isTitleBar, bool isOverlay)
        {
            Panel = panel;
            Title = title;
            LayerIndex = layerIndex;
            IsTitleBar = isTitleBar;
            IsOverlay = isOverlay;
        }
    }

    // A covering overlay (a card, a generic overlay, or a modal dialog) renders alone over the base
    // screen, so the title bar and its real Back button are suppressed. The HTML surface gives such a
    // section its own Back affordance — otherwise the player is trapped in it, with only the Alt+[
    // history-back chord as an exit. The loading-screen foreground layers are deliberately excluded:
    // they host non-dismissable flows (the career-start screen) and spinners, where a Back is wrong.
    // Mirrors the covering subset of InteractivePanelSet.IsForegroundLayer; the layer consts are shared.
    private static bool IsOverlayLayer(int layer) =>
        layer == InteractivePanelSet.LayerCard
        || layer == InteractivePanelSet.LayerOverlay
        || layer == InteractivePanelSet.LayerModalDialog;

    // PanelManager.PanelLayer.Tooltip. Normally passive hover chrome, but the game also hosts a few
    // genuine interactive modals here (the career-start "Are you sure you want to delete?" manager
    // dialog), so a VISIBLE + INTERACTIVE panel on this layer is promoted to a modal in SelectLive.
    // Not in InteractivePanelSet's mirrored consts: that pure logic never needed it, and keeping the
    // promotion in this seam avoids touching the permanent Navigation assembly (this file hot-reloads).
    private const int LayerTooltip = 5;

    // Foreground layers (mirrors InteractivePanelSet.IsForegroundLayer via the shared public layer
    // consts): a visible covering panel here takes over the page. Used only to scope the geometry-based
    // interactivity augmentation in SelectLive to panels that can legitimately be an active overlay.
    private static bool IsForegroundLayer(int layer) =>
        layer == InteractivePanelSet.LayerCard
        || layer == InteractivePanelSet.LayerOverlay
        || layer == InteractivePanelSet.LayerModalDialog
        || layer == InteractivePanelSet.LayerProfanityLoadingScreen
        || layer == InteractivePanelSet.LayerLoadingScreen;

    // PanelManager.Open contains the incoming native card before it is visible
    // or interactive. During a replacement the old card is already Closing.
    // That gap is not a usable parent screen. Do not infer it from a remembered
    // popup, a caption or a timer: read the current native loading collection.
    // Non-covering HUDs and dedicated progress screens keep their own contract.
    internal static bool IsCoveringForegroundLoading()
    {
        var layers = FM27PanelCatalog.GetLayers(PanelManager.Instance);
        foreach (var layer in layers)
        {
            if (layer.Layer is not (InteractivePanelSet.LayerCard or InteractivePanelSet.LayerModalDialog)
                || !layer.Open.Any(panel => panel.IsLoading)) continue;
            bool covered = layers.Any(above => above.Layer > layer.Layer
                && IsForegroundLayer(above.Layer)
                && above.Open.Any(panel => panel.IsVisible && panel.IsActive && !panel.IsLoading));
            if (!covered) return true;
        }
        return false;
    }

    /// <summary>
    /// How many render queries may fail in a row before the mod treats the contract as broken and
    /// halts. One failure is a panel being torn down while we looked at it — the game does that
    /// legitimately, and it says nothing about the next query. Five in a row is not a blip.
    ///
    /// The asymmetry is deliberate: halting too eagerly ends the session (the player must restart the
    /// game, and speech goes with it), while halting too late costs only a few renders that already
    /// say "unavailable" rather than showing anything. So this errs patient.
    /// </summary>
    private const int MaxConsecutiveFailures = 5;

    private static int _consecutiveFailures;
    private static volatile bool _recovered;

    /// <summary>
    /// The ordered set of panels to render right now. Empty only when the screen is honestly empty
    /// (nothing open). A failed query never returns an empty or "show everything" result — it throws,
    /// so the render fails closed and the page says so. This is the render path (<c>/body</c>), where
    /// the cornerstone applies in full.
    ///
    /// A failure no longer halts the mod on its own. The cornerstone is about never presenting a
    /// blocked panel as usable, and throwing satisfies that completely: nothing reaches the page. What
    /// halting additionally did was end the session over one bad instant — and since this query runs on
    /// every page build, a single transient failure while the game tore a panel down took the whole mod
    /// down with it, speech included. That is the wrong trade for a query that is asked several times a
    /// second. Persistent failure still halts (see <see cref="MaxConsecutiveFailures"/>), because then
    /// the contract really is broken and every render would be a gap.
    /// </summary>
    internal static List<PanelView> Current()
    {
        try
        {
            var set = SelectLive();
            NoteSuccess();
            return set;
        }
        catch (Exception ex)
        {
            if (++_consecutiveFailures >= MaxConsecutiveFailures)
            {
                Log.Fatal($"[InteractivePanels] Interactive-set query failed "
                        + $"{_consecutiveFailures} times in a row; the contract is broken: {ex}");
                AccessibilityMod.Kill();
                throw;
            }

            Degrade.Note($"[InteractivePanels] interactive-set query failed "
                       + $"({_consecutiveFailures}/{MaxConsecutiveFailures}), skipping this render: {ex.Message}");
            throw;   // fail closed: the caller renders the gap, never a guessed set
        }
    }

    // A query succeeding after failures means the run was transient. Clearing the streak is the point;
    // the recovery flag exists because the render that failed left the player looking at an
    // "unavailable" page, and the screen beneath it has very likely not changed — so without forcing a
    // re-read there would be no screen event to refresh them off it.
    private static void NoteSuccess()
    {
        if (_consecutiveFailures == 0) return;
        Log.Info($"[InteractivePanels] interactive-set query recovered after {_consecutiveFailures} failure(s)");
        _consecutiveFailures = 0;
        _recovered = true;
    }

    /// <summary>True once after a run of failed render queries ended, then cleared. The SSE poll uses
    /// it to force a re-read so the player is not left on the page a failed render produced.</summary>
    internal static bool ConsumeRecovered()
    {
        if (!_recovered) return false;
        _recovered = false;
        return true;
    }

    /// <summary>
    /// Best-effort variant for the background refresh watch (the SSE poll's aggregate token/shape and
    /// <c>ArmSettleAfterAction</c>'s baseline). These only *detect change*; they never render, so a
    /// blocked panel can never reach the page through here. The crashing <see cref="Current"/> still
    /// runs on the <c>/body</c> render that a detected change triggers, so a transient enumeration
    /// failure here skips the tick rather than killing the game (plan §Phase 3 decision). Returns null
    /// on failure.
    /// </summary>
    internal static List<PanelView> CurrentOrNull()
    {
        try
        {
            return SelectLive();
        }
        catch (Exception ex)
        {
            Log.Warn($"[InteractivePanels] Interactive-set watch read failed; skipping this tick: {ex.Message}");
            return null;
        }
    }

    // The shared live enumeration + selection, called by both the crashing render gate and the
    // best-effort watch. One computation, two failure policies — the selection logic never forks.
    private static List<PanelView> SelectLive()
    {
        // Pass 1: gather each open panel's live facts (deduped). Geometry for ALL panels must be known
        // before interactivity is finalised (pass 2), because a covering overlay's "active" status is
        // derived from whether it covers a panel beneath it.
        // The game can hold two live instances of the same dialog on one layer (observed: two
        // InstantResultPanel entries on the ModalDialog layer for one confirmation prompt). Rendering
        // both would double the dialog, so keep one per (layer, PanelID name) — and of the duplicates,
        // the one the game is actually presenting: a visible-and-interactive instance beats a visible
        // one beats a hidden one. Keeping strictly the first (the old rule) meant a stale first
        // instance — open in the list but no longer the one on screen — was the one the page walked,
        // and a stale panel's async binds never land, so its content read as permanent gaps that no
        // re-read could heal. Ties keep the first, preserving the old behavior for the identical-twin
        // dialog case. Distinct panels (a title bar and its content, differently named) are unaffected.
        var raw = new List<(SI.Bindable.Panel Panel, int Layer, bool Visible, bool Interactive, InteractivePanelSet.PanelRect Bounds, bool IsTitleBar)>();
        var seen = new Dictionary<string, int>();
        var pm = PanelManager.Instance;
        var layers = FM27PanelCatalog.GetLayers(pm);
        for (int i = 0; i < layers.Count; i++)
        {
            var open = layers[i].Open;
            if (open == null) continue;
            for (int j = 0; j < open.Count; j++)
            {
                var panel = open[j];
                if (panel == null) continue;

                // The game hides the ProcessingCard without a close event and lets it fade out
                // over a couple of seconds; keeping it in the set for that fade strands the page
                // on a frozen card well after "Simulation complete" was spoken. Once the continue
                // flow has left Processing the card is spent, so retire it immediately.
                if (panel.PanelID.name == "ProcessingCard" && !Patches.ContinueStatePatch.IsProcessing)
                    continue;

                bool visible = ElementBridge.IsVisible(panel);
                bool interactive = FM27PanelCatalog.IsOpenAndInteractive(pm,panel.PanelID);
                var wb = panel.worldBound;
                var bounds = new InteractivePanelSet.PanelRect(wb.x, wb.y, wb.width, wb.height);
                // The title bar is the panel parented in its layer's "Title" region (vs "Body" for
                // content) — a stable PanelManager layer-template name, so this holds on every screen
                // regardless of language or which title bar variant is up. A transient null parent
                // mid-transition reads as "not the title bar", which only ever costs lead position.
                bool isTitleBar = FM27PanelCatalog.IsTitleBar(panel);

                string key = i + "|" + panel.PanelID.name;
                if (seen.TryGetValue(key, out int keptIdx))
                {
                    var kept = raw[keptIdx];
                    int keptRank = (kept.Visible ? 2 : 0) + (kept.Interactive ? 1 : 0);
                    int rank = (visible ? 2 : 0) + (interactive ? 1 : 0);
                    if (rank > keptRank)
                    {
                        raw[keptIdx] = (panel, i, visible, interactive, bounds, isTitleBar);
                        LogOnce(ref _lastDuplicateLogged, key,
                            $"[InteractivePanels] Duplicate panel \"{panel.PanelID.name}\" on layer {i}: "
                            + $"preferring the later instance (visible={visible} interactive={interactive}) "
                            + $"over the first (visible={kept.Visible} interactive={kept.Interactive}).");
                    }
                    else
                    {
                        LogOnce(ref _lastDuplicateLogged, key,
                            $"[InteractivePanels] Skipping duplicate panel \"{panel.PanelID.name}\" on layer {i}.");
                    }
                    continue;
                }
                seen[key] = raw.Count;
                raw.Add((panel, i, visible, interactive, bounds, isTitleBar));
            }
        }

        // A full FM27 Menu document (meetings/negotiations among its users)
        // replaces MenuTitle/MenuBody, even though their mounted roots can have
        // no geometry and remain above the same navigation focus frontier.
        // Keep this native distinction out of FM26's permanent layer model.
        if (raw.Exists(r => r.Visible && r.Interactive && r.Layer == InteractivePanelSet.LayerMenu
            && FM27PanelCatalog.IsFullMenu(pm, r.Panel)))
            raw.RemoveAll(r => r.Layer == InteractivePanelSet.LayerMenu
                && !FM27PanelCatalog.IsFullMenu(pm, r.Panel));

        // Entity links can open a Report above a full Menu document. The latter
        // remains mounted (including its own title strip), but the native catalog
        // marks it noninteractive. Its full-screen bounds must not make its old
        // conversation/actions readable beside the new destination.
        bool foregroundAboveMenu = raw.Exists(r => r.Visible && r.Interactive
            && r.Layer > InteractivePanelSet.LayerMenu);
        if (foregroundAboveMenu)
            raw.RemoveAll(r => !r.Interactive && FM27PanelCatalog.IsFullMenu(pm, r.Panel));

        // The post-match tunnel interview opens as a SIBLING of the portal screen on the same Menu
        // layer — not on a card/modal layer — while the real screen is a full-screen takeover: the
        // 3D press-room scene plus the interview UI, with no portal content visible at all
        // (screenshot-verified 2026-07-30). Same-layer siblings never trigger the cross-layer
        // covering rule in pass 2, so without this the page co-rendered the whole inbox above the
        // interview. Present the takeover honestly: while the interview panel is up, drop the other
        // non-title-bar panels on its layer (the title bar stays — the game retitles it
        // "Post-Match Tunnel Interview"). Name-scoped: the PRE-match interview is a different
        // panel and, in-match, has no same-layer content sibling to drop in the first place.
        int takeoverLayer = -1;
        foreach (var r in raw)
            if (r.Visible && r.Panel.PanelID.name == "PostMatchTunnelInterviewPanel")
            {
                takeoverLayer = r.Layer;
                break;
            }
        if (takeoverLayer >= 0)
            raw.RemoveAll(r => r.Layer == takeoverLayer && !r.IsTitleBar
                && r.Panel.PanelID.name != "PostMatchTunnelInterviewPanel");

        // The in-match tactics planner (MatchTactics, Report layer) is the other takeover the
        // geometry cannot see: opened over the half-time team talk it leaves the talk's top match
        // strip (score, Second Half) visible, so its bounds never fully cover the Menu-layer talk
        // panel and the page co-rendered the entire pep talk above the planner. On the real screen
        // the planner takes over everything but that strip (screenshot-verified 2026-07-30), and
        // during live play the same panel already renders alone because the base it opens over IS
        // fully covered. While it is up, drop the base panels on lower layers: their content sits
        // behind the planner, the Second Half continue stays reachable through the continue chord,
        // and closing the planner re-renders the talk via the screen token.
        int tacticsLayer = -1;
        foreach (var r in raw)
            if (r.Visible && r.Panel.PanelID.name == "MatchTactics")
            {
                tacticsLayer = r.Layer;
                break;
            }
        if (tacticsLayer >= 0)
            raw.RemoveAll(r => r.Layer < tacticsLayer && !r.IsTitleBar);

        // Every Portal tool (Messages, Calendar, News Site, Stages, ...) opens on the Report layer
        // as a full-screen takeover of the Portal dashboard, which stays mounted beneath it with
        // its own live Messages widget — same container names and row templates as the real
        // Messages tool, indistinguishable once both are on the page. Geometry normally drops the
        // covered dashboard, but the occlusion check has half a pixel of slack; on a machine where
        // the two rects disagree the page co-rendered the covered dashboard's inbox first, and a
        // covered panel's composed strings (headline, sender) never finish their async bind — the
        // field-reported permanently unreadable subject rows, which no re-read of the same dead
        // widget could ever heal. The takeover is a fact of the Portal's design, not of geometry,
        // so encode it by name: while a visible Report-layer tool is up, the dashboard leaves the
        // set. The log fires exactly when this rule changes the outcome geometry would have
        // produced, so an affected user's Player.log confirms the diagnosis.
        bool portalToolUp = false;
        foreach (var r in raw)
            if (r.Visible && r.Layer == InteractivePanelSet.LayerReport && !r.IsTitleBar)
            {
                portalToolUp = true;
                break;
            }
        if (portalToolUp)
            for (int i = raw.Count - 1; i >= 0; i--)
            {
                var r = raw[i];
                if (r.IsTitleBar || r.Panel.PanelID.name != "PortalScreen") continue;
                bool covered = false;
                foreach (var t in raw)
                    if (t.Visible && t.Layer == InteractivePanelSet.LayerReport && !t.IsTitleBar
                        && t.Bounds.Covers(r.Bounds))
                    {
                        covered = true;
                        break;
                    }
                if (!covered)
                    LogOnce(ref _lastPortalTakeoverLogged, r.Panel.PanelID.name,
                        "[InteractivePanels] Report-layer tool does not geometrically cover the Portal dashboard; "
                        + "dropping the dashboard by the takeover rule (co-rendering it would surface its stale Messages widget).");
                raw.RemoveAt(i);
            }

        // Pass 2: build candidates, augmenting the game's interactivity verdict with geometry. The game
        // reports some covering overlays as non-interactive — the InstantResultPanel confirmation does,
        // because its focus frontier sits on the always-on watermark/version chrome — which would let
        // the page fall back to the base screen the overlay actually covers, presenting a blocked panel
        // as usable (the §3 lie). A VISIBLE foreground panel that fully covers a visible panel beneath
        // it IS the active overlay, so report it interactive. A dormant zero-size foreground (the empty
        // ProcessingCard, 0x0) covers nothing and is unaffected. This augmentation lives in the seam —
        // not the pure InteractivePanelSet.Select — because it reads live geometry, and because the
        // seam is hot-reloadable while Navigation is a permanent assembly.
        var candidates = new List<InteractivePanelSet.Candidate<PanelView>>();
        foreach (var r in raw)
        {
            bool interactive = r.Interactive;
            if (!interactive && r.Visible && IsForegroundLayer(r.Layer) && !r.Bounds.IsEmpty)
                foreach (var other in raw)
                    if (other.Visible && other.Layer < r.Layer && r.Bounds.Covers(other.Bounds))
                    {
                        interactive = true;
                        break;
                    }

            // A visible, interactive panel on the Tooltip layer is a real focus-taking modal (the
            // delete-manager confirmation), not passive hover chrome, so present it to the selection as
            // a modal dialog — otherwise the Tooltip layer is invisible to it and the modal never
            // renders. Passive tooltips report non-interactive and are left as their own layer, so they
            // never hijack the page. Only the layer the SELECTION sees changes; the PanelView keeps the
            // panel's true layer for any downstream read.
            int selectionLayer = r.Layer;
            if (interactive && r.Visible && r.Layer == LayerTooltip)
            {
                selectionLayer = InteractivePanelSet.LayerModalDialog;
                LogOnce(ref _lastPromotionLogged, r.Panel.PanelID.name,
                    $"[InteractivePanels] Promoting interactive Tooltip-layer panel \"{r.Panel.PanelID.name}\" to a modal dialog.");
            }

            // An Overlay(4) panel that fully covers a visible Card-layer panel beneath it is a modal
            // stacked on a card (the Recruitment Focus "Assigned Scout" dialog: its root spans the
            // whole screen, probe-verified 2026-08-05), not a HUD co-renderer. The pure selection
            // would drop it — a covering Card makes Select return that layer alone, and its Overlay
            // co-render path never runs — leaving the page on the card while an invisible modal
            // blocks it (the §3 lie, inverted: a blocked panel presented as usable). Promote it to a
            // modal dialog so it renders alone. The match transport controller HUD covers only a
            // corner and can never fully cover a card, so the validated co-render path is untouched.
            // A visible real ModalDialog(7) above means this overlay is itself blocked: no promotion,
            // the selection renders the true modal alone as before.
            if (interactive && r.Visible && r.Layer == InteractivePanelSet.LayerOverlay && !r.Bounds.IsEmpty)
            {
                bool blockedAbove = false;
                bool coversCard = false;
                foreach (var other in raw)
                {
                    if (!other.Visible) continue;
                    if (other.Layer == InteractivePanelSet.LayerModalDialog) blockedAbove = true;
                    if (other.Layer == InteractivePanelSet.LayerCard && r.Bounds.Covers(other.Bounds)) coversCard = true;
                }
                if (coversCard && !blockedAbove)
                {
                    selectionLayer = InteractivePanelSet.LayerModalDialog;
                    LogOnce(ref _lastPromotionLogged, r.Panel.PanelID.name,
                        $"[InteractivePanels] Promoting card-covering Overlay-layer panel \"{r.Panel.PanelID.name}\" to a modal dialog.");
                }
            }

            var view = new PanelView(r.Panel, ServerText.GetTitle(r.Panel), r.Layer, r.IsTitleBar, IsOverlayLayer(r.Layer));
            candidates.Add(new InteractivePanelSet.Candidate<PanelView>(selectionLayer, r.Visible, interactive, r.Bounds, view, r.IsTitleBar));
        }

        var selected = InteractivePanelSet.Select(candidates);
        SuppressSetupTitleBar(selected);

        // An Overlay(4) HUD panel (e.g. the match transport controller) co-renders with the base
        // content it does not cover — the validated §9.8 path. Log at debug for diagnosis without
        // shaping the page.
        string overlayTitle = null;
        foreach (var view in selected)
            if (view.LayerIndex == InteractivePanelSet.LayerOverlay)
            {
                overlayTitle = view.Title;
                LogOnce(ref _lastOverlayLogged, view.Title,
                    $"[InteractivePanels] Overlay-layer panel \"{view.Title}\" in render set (co-rendering with base content).");
                break;
            }
        if (overlayTitle == null) _lastOverlayLogged = null;

        return selected;
    }

    // This method runs on every SSE full tick (~500ms) as well as every compose, so an unconditional
    // Log.Debug here is a synchronous Player.log write twice a second for as long as the state holds
    // (the match transport controller sits on the Overlay layer for a whole match). Log state CHANGES,
    // not state: same key as last time stays silent. Main-thread only, like the rest of SelectLive.
    private static string _lastDuplicateLogged;
    private static string _lastPromotionLogged;
    private static string _lastOverlayLogged;
    private static string _lastPortalTakeoverLogged;

    private static void LogOnce(ref string last, string key, string message)
    {
        if (key == last) return;
        last = key;
        Log.Debug(message);
    }

    // Several advanced career-setup screens co-render the unemployed Portal ribbon (Portal / Career
    // tabs, Messages, the date) as their title bar, because the game treats the manager as an
    // unemployed portal user during setup. In a setup wizard that ribbon is a dead-end distraction —
    // each screen's flow is driven by its own Back / Next buttons — so drop the title bar from the
    // render set when one of these setup screens is the base content, leaving only the wizard step.
    // The Quick Start flow does not show the ribbon, so it needs no entry here. Scoped by panel name;
    // every other screen keeps its title bar.
    private static readonly HashSet<string> SetupScreensWithoutTitleBar = new()
    {
        "GameCreationAdvancedSetupChooseTeamScreen",
        "GameCreationManagerSelectionScreen",
    };

    private static void SuppressSetupTitleBar(List<PanelView> selected)
    {
        bool isSetupScreen = false;
        foreach (var v in selected)
            if (!v.IsTitleBar && SetupScreensWithoutTitleBar.Contains(v.Panel.PanelID.name))
            {
                isSetupScreen = true;
                break;
            }
        if (isSetupScreen)
            selected.RemoveAll(v => v.IsTitleBar);
    }
}
