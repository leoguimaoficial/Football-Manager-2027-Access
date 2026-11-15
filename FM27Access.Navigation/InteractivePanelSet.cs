using System.Collections.Generic;
using System.Linq;

namespace FM27Access.Navigation;

/// <summary>
/// The pure selection logic behind the single-page HTML surface: given the open panels and the
/// game's own interactivity verdict for each, decide which panels to render and in what order.
/// No game or Unity dependency, so it is unit-tested without the game; the live PanelManager /
/// IsOpenAndInteractive reads are a thin seam in the Plugin (InteractivePanels) that feeds this.
///
/// The model (validated against four live fixtures, see docs/archive/plans/removing panel cycling plan.md §12):
/// the game's <c>IsOpenAndInteractive</c> answers "is this panel at or above the current focus
/// frontier", NOT "can the user use this panel". On a normal screen the main content (a Report)
/// takes focus, so the title bar and screen shell beneath it read non-interactive even though they
/// hold live, mod-clickable content. We therefore use the signal only to locate the active covering
/// foreground panel (a card or modal that covers what is beneath, or an interactive full-screen the
/// game hosts on a loading layer — e.g. the career-start screen); if one has focus we render it
/// alone. A non-covering Overlay(4) HUD panel (the match transport controller) is the exception: it
/// co-renders with the base content it leaves visible, demoting only the base it geometrically
/// covers. Otherwise we render every visible base-content panel.
///
/// Safety property (the cornerstone, §3): a panel blocked by a covering foreground can never reach
/// the page, because when one has focus we render only that one. A non-covering overlay relies on the
/// same geometric occlusion as base-vs-base — a base panel it fully covers is dropped — so a blocked
/// panel still never surfaces. Showing fewer panels than exist is acceptable; presenting a dead panel
/// as usable is the lie the mod must never tell.
/// </summary>
public static class InteractivePanelSet
{
    // PanelManager.PanelLayer indices, mirrored here so the selection stays game-free. Keep in
    // sync with SI.Bindable.PanelManager.PanelLayer.
    public const int LayerMenu = 1;
    public const int LayerReport = 2;
    public const int LayerCard = 3;
    public const int LayerOverlay = 4;
    public const int LayerModalDialog = 7;
    public const int LayerProfanityLoadingScreen = 8;
    public const int LayerLoadingScreen = 9;

    // Covering foreground layers: a visible interactive panel here is an opaque modal that takes over
    // the page, suppressing the base content beneath WITHOUT consulting geometry. Card and ModalDialog
    // are confirmed overlays (fixtures 1/3/4). The loading layers (8/9) are included because FM26 hosts
    // genuine interactive full-screens on them — the career-start "Begin Career" screen lives on
    // GameCreationLoadingScreen (LoadingScreen, layer 9); excluding them blanked that screen with no F5
    // recovery. A real loading spinner on these layers is harmless: it renders as a heading with no
    // content and refreshes once real content arrives. The dormant ProcessingCard (layer 9,
    // visible=false) is still excluded by the visibility gate, so it never hijacks the page.
    //
    // Overlay(4) is deliberately NOT here. It is a HUD overlay (the match transport controller) that
    // typically covers only a corner; it co-renders with the base content it leaves visible and is
    // demoted by geometric occlusion only over the base it actually covers — see Select.
    private static bool IsCoveringForegroundLayer(int layer) =>
        layer == LayerCard || layer == LayerModalDialog
        || layer == LayerProfanityLoadingScreen || layer == LayerLoadingScreen;

    // Base content layers: the chrome + main content of one screen, all co-rendered. Their
    // individual IsOpenAndInteractive is deliberately ignored (it lies about the chrome below the
    // main content); presence and visibility decide.
    private static bool IsBaseLayer(int layer) =>
        layer == LayerMenu || layer == LayerReport;

    /// <summary>
    /// A panel's screen rectangle, kept game-free (no UnityEngine.Rect) so the selection stays
    /// unit-testable. Filled from the live panel's <c>worldBound</c> in the Plugin seam. A zero-area
    /// rect (the default, and what an unlaid-out panel reports) is treated as "no geometry" and never
    /// participates in occlusion, so a missing bound can only ever render more, never hide a panel.
    /// </summary>
    public readonly struct PanelRect
    {
        public readonly float X, Y, W, H;

        public PanelRect(float x, float y, float w, float h)
        {
            X = x; Y = y; W = w; H = h;
        }

        public bool IsEmpty => W <= 0f || H <= 0f;

        /// <summary>
        /// True when this rectangle fully contains <paramref name="inner"/> (small epsilon for float
        /// slop). Empty rectangles never cover and are never covered. Assumes the covering panel is
        /// opaque — true of base-layer screen content, which is the only place this is used.
        /// </summary>
        public bool Covers(in PanelRect inner, float eps = 0.5f) =>
            !IsEmpty && !inner.IsEmpty
            && X <= inner.X + eps && Y <= inner.Y + eps
            && X + W >= inner.X + inner.W - eps && Y + H >= inner.Y + inner.H - eps;
    }

    /// <summary>A panel's selection-relevant facts; <typeparamref name="T"/> carries the live panel through untouched.</summary>
    public readonly struct Candidate<T>
    {
        public readonly int LayerIndex;
        public readonly bool Visible;
        public readonly bool Interactive;
        public readonly PanelRect Bounds;
        public readonly T Payload;
        /// <summary>
        /// The title bar — the top chrome strip carrying the global navigation ribbon (the panel
        /// parented in its layer's "Title" region). It leads its layer in the render order so the page
        /// opens on the navigation landmark; the Plugin seam sets it, the selection only orders by it.
        /// </summary>
        public readonly bool IsTitleBar;

        public Candidate(int layerIndex, bool visible, bool interactive, PanelRect bounds, T payload, bool isTitleBar = false)
        {
            LayerIndex = layerIndex;
            Visible = visible;
            Interactive = interactive;
            Bounds = bounds;
            Payload = payload;
            IsTitleBar = isTitleBar;
        }
    }

    /// <summary>
    /// Returns the panels to render, ordered by layer ascending (main content before things stacked
    /// on top); within a layer the title bar leads and everything else keeps input order. Empty when
    /// nothing qualifies.
    /// </summary>
    public static List<T> Select<T>(IEnumerable<Candidate<T>> candidates)
    {
        var visible = candidates.Where(c => c.Visible).ToList();

        // The active covering foreground is the highest covering-foreground layer the game reports as
        // interactive (so a panel itself blocked by a higher one is skipped, and the higher one wins).
        // The live seam (InteractivePanels.SelectLive) augments that interactivity verdict with
        // geometry before it gets here — a covering overlay the game marks non-interactive (e.g. the
        // InstantResultPanel confirmation) is reported interactive — so this stays a pure
        // interactive-only decision. Overlay(4) is excluded here: it is handled below, by geometry.
        int coveringForeground = -1;
        foreach (var c in visible)
            if (c.Interactive && IsCoveringForegroundLayer(c.LayerIndex) && c.LayerIndex > coveringForeground)
                coveringForeground = c.LayerIndex;

        if (coveringForeground >= 0)
            // An opaque modal covers the base content: render only that layer.
            return visible.Where(c => c.LayerIndex == coveringForeground)
                          .OrderBy(c => c.LayerIndex).Select(c => c.Payload).ToList();

        // No covering foreground: render the screen's base content plus any active (interactive)
        // Overlay(4) HUD panel, minus any panel sitting entirely behind a higher panel that covers it.
        // The same geometric occlusion used base-vs-base now also governs the overlay: a non-covering
        // HUD overlay (the match transport controller in a corner) leaves the management + squad bars
        // visible, so they stay and it co-renders above them; a full-screen overlay covers them, so
        // they drop and it renders alone. A covered panel is not visible (e.g. the Portal dashboard
        // beneath the Messages tool, identical rects) — rendering it would duplicate hidden content and
        // present a dead panel as usable (§3). The uncovered chrome (the ribbon is a separate top
        // strip) and genuinely distinct co-rendered panels (a squad shell and its report occupy
        // different regions) are not covered, so they stay.
        var renderSet = visible.Where(c =>
            IsBaseLayer(c.LayerIndex) || (c.LayerIndex == LayerOverlay && c.Interactive)).ToList();
        var kept = renderSet.Where(b =>
            !renderSet.Any(a => a.LayerIndex > b.LayerIndex && a.Bounds.Covers(b.Bounds)));

        // OrderBy is stable. Within a layer the title bar leads (the chrome strip is the page's
        // top landmark, even though the game enumerates it after the content it sits above); the
        // rest keep their enumeration order. Across layers, lower (main content) still precedes higher.
        return kept.OrderBy(c => c.LayerIndex).ThenBy(c => c.IsTitleBar ? 0 : 1).Select(c => c.Payload).ToList();
    }
}
