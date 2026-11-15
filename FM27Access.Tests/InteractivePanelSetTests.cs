using System.Linq;
using FM27Access.Navigation;
using static FM27Access.Navigation.InteractivePanelSet;

namespace FM27Access.Tests;

/// <summary>
/// Pins the panel-selection model from docs/archive/plans/removing panel cycling plan.md. The four named fixtures
/// are the live F12 readouts captured during Phase 0; the remaining tests each lock one rule with a
/// failure mode the fixtures alone would not catch.
/// </summary>
public class InteractivePanelSetTests
{
    // A candidate carrying its name as payload, so assertions read as panel names. Bounds default to
    // empty (no geometry), which never participates in occlusion — so the pre-existing fixtures, which
    // predate the occlusion rule, keep asserting exactly what they did before.
    private static Candidate<string> P(string name, int layer, bool visible, bool interactive, PanelRect bounds = default, bool isTitleBar = false)
        => new Candidate<string>(layer, visible, interactive, bounds, name, isTitleBar);

    private static string[] Select(params Candidate<string>[] candidates)
        => InteractivePanelSet.Select(candidates).ToArray();

    // ---- The four Phase 0 fixtures (§12) ----

    [Fact]
    public void Fixture1_ModalDialog_RendersOnlyTheDialog()
    {
        var set = Select(
            P("GameWorldBackground", 0, true, false),
            P("FullScreenTeamSelection", LayerMenu, true, false),
            P("TeamSelectionTitleBar", LayerMenu, true, false),
            P("TeamSelectionInfoDialog", LayerModalDialog, true, true),
            P("Watermark", 10, true, true),
            P("GameVersion", 11, true, true));

        Assert.Equal(new[] { "TeamSelectionInfoDialog" }, set);
    }

    [Fact]
    public void Fixture2_PlainSquad_RendersTitleBarSquadAndReport()
    {
        // The positive case: three co-existing base panels become three sections. The title bar and
        // shell read non-interactive (focus is on the Report) yet must still render.
        var set = Select(
            P("GameWorldBackground", 0, true, false),
            P("OverviewTitleBar", LayerMenu, true, false),
            P("SquadScreen", LayerMenu, true, false),
            P("TeamSquadReport", LayerReport, true, true),
            P("Watermark", 10, true, true),
            P("GameVersion", 11, true, true));

        Assert.Equal(new[] { "OverviewTitleBar", "SquadScreen", "TeamSquadReport" }, set);
    }

    [Fact]
    public void Fixture3_FitnessCardOverReport_RendersOnlyTheCard()
    {
        var set = Select(
            P("OverviewTitleBar", LayerMenu, true, false),
            P("SquadScreen", LayerMenu, true, false),
            P("PlayerReport", LayerReport, true, false),
            P("PlayerReportFitnessCard", LayerCard, true, true),
            P("Watermark", 10, true, true));

        Assert.Equal(new[] { "PlayerReportFitnessCard" }, set);
    }

    [Fact]
    public void Fixture4_ConditionsCardOverSearch_RendersOnlyTheCard()
    {
        var set = Select(
            P("OverviewTitleBar", LayerMenu, true, false),
            P("RecruitmentScreen", LayerMenu, true, false),
            P("PlayerSearchReport", LayerReport, true, false),
            P("PlayerSearchConditionFiltersCard", LayerCard, true, true),
            P("Watermark", 10, true, true));

        Assert.Equal(new[] { "PlayerSearchConditionFiltersCard" }, set);
    }

    // ---- Rule-level tests ----

    [Fact]
    public void BasePanel_RendersEvenWhenNotInteractive()
    {
        // The squad-screen lesson in isolation: with no overlay, base panels render on visibility
        // alone. A regression that required Interactive on base would silently drop the title bar.
        var set = Select(P("LoneReport", LayerReport, true, false));
        Assert.Equal(new[] { "LoneReport" }, set);
    }

    [Fact]
    public void HigherOverlay_WinsOverLowerOverlay()
    {
        // A modal above a card: render only the modal, never the card beneath it.
        var set = Select(
            P("SomeReport", LayerReport, true, false),
            P("SomeCard", LayerCard, true, true),
            P("SomeModal", LayerModalDialog, true, true));
        Assert.Equal(new[] { "SomeModal" }, set);
    }

    [Fact]
    public void CardBehindModal_IsSkipped_WhenItReadsNonInteractive()
    {
        // The card is blocked (interactive=false) by the modal above; the modal is the active overlay.
        var set = Select(
            P("BlockedCard", LayerCard, true, false),
            P("TopModal", LayerModalDialog, true, true));
        Assert.Equal(new[] { "TopModal" }, set);
    }

    [Fact]
    public void NonCoveringOverlay_CoRendersWithBase()
    {
        // The match-HUD fixture (2026-06-27): the floating Match Controller (Overlay 4) opens over a
        // corner while the management bar and squad bar (base content) stay fully usable on screen.
        // The overlay must co-render with that base content, not suppress it — the player keeps the
        // play/pause + tactics controls AND gains the transport controller. Failure mode (the bug this
        // fixes): the overlay blanket-blocked the base, trapping the player with no way back.
        var managementBar = new PanelRect(0, 0, 1680, 84);
        var squadBar = new PanelRect(0, 84, 1680, 60);
        var controller = new PanelRect(1400, 90, 280, 120); // top-right corner, covers neither bar
        var set = Select(
            P("ManagementBar", LayerMenu, true, false, managementBar),
            P("SquadBar", LayerReport, true, false, squadBar),
            P("MatchController", LayerOverlay, true, true, controller));
        Assert.Equal(new[] { "ManagementBar", "SquadBar", "MatchController" }, set);
    }

    [Fact]
    public void CoveringOverlay_StillSuppressesBase()
    {
        // The other side of the same rule: an Overlay(4) that geometrically covers the base content
        // is a full-screen takeover and renders alone, exactly as a card/modal would. Geometric
        // occlusion demotes the covered base, so a blocked panel still never surfaces (§3).
        var fullScreen = new PanelRect(0, 0, 1680, 945);
        var set = Select(
            P("BaseScreen", LayerMenu, true, false, fullScreen),
            P("FullScreenOverlay", LayerOverlay, true, true, fullScreen));
        Assert.Equal(new[] { "FullScreenOverlay" }, set);
    }

    [Fact]
    public void CareerStartHostedOnLoadingScreen_RendersTheLoadingScreenPanel()
    {
        // FM26 hosts the interactive "Begin Career" screen on GameCreationLoadingScreen (LoadingScreen
        // layer 9), with a progress overlay below it (non-interactive) and no base panels open. The
        // loading screen is the active foreground and must render, or the whole screen blanks with no
        // F5 recovery (the bug this fixture pins).
        var set = Select(
            P("ApplicationProgressPanel", LayerOverlay, true, false),
            P("GameCreationLoadingScreen", LayerLoadingScreen, true, true),
            P("Watermark", 10, true, true),
            P("GameVersion", 11, true, true));
        Assert.Equal(new[] { "GameCreationLoadingScreen" }, set);
    }

    [Fact]
    public void DormantGhost_ExcludedByVisibility()
    {
        // The ProcessingCard ghost (LoadingScreen layer, visible=false) present on every screen must
        // never appear. Now that layer 9 is a renderable foreground layer, the visibility gate is the
        // only thing keeping this hidden interactive panel from hijacking the page — so this guards it.
        var set = Select(
            P("OverviewTitleBar", LayerMenu, true, false),
            P("SquadScreen", LayerReport, true, true),
            P("ProcessingCard", LayerLoadingScreen, false, true));
        Assert.Equal(new[] { "OverviewTitleBar", "SquadScreen" }, set);
    }

    [Fact]
    public void InvisibleOverlay_DoesNotSuppressBase()
    {
        // An overlay must be visible to count as the active blocker; a hidden one leaves base content showing.
        var set = Select(
            P("BaseScreen", LayerMenu, true, false),
            P("HiddenCard", LayerCard, false, true));
        Assert.Equal(new[] { "BaseScreen" }, set);
    }

    [Fact]
    public void BaseLayers_OrderedMenuBeforeReport_RegardlessOfInputOrder()
    {
        var set = Select(
            P("TheReport", LayerReport, true, true),
            P("TheTitleBar", LayerMenu, true, false));
        Assert.Equal(new[] { "TheTitleBar", "TheReport" }, set);
    }

    [Fact]
    public void TitleBar_LeadsItsLayer_EvenWhenEnumeratedAfterContent()
    {
        // Live Portal enumerates the content panel (PortalScreen, j=0) before the title bar
        // (OverviewTitleBar, j=1) within the Menu layer, so a layer-only stable order put the ribbon
        // BELOW the dashboard on the page. The title bar must lead its layer to render as the page's
        // top section. Failure mode: without the chrome tiebreak this returns content-then-titlebar.
        var ribbon = new PanelRect(0, 0, 1680, 84);
        var content = new PanelRect(0, 84, 1680, 861);
        var set = Select(
            P("PortalScreen", LayerMenu, true, false, content),
            P("OverviewTitleBar", LayerMenu, true, false, ribbon, isTitleBar: true));
        Assert.Equal(new[] { "OverviewTitleBar", "PortalScreen" }, set);
    }

    [Fact]
    public void NonContentLayers_NeverRenderOnTheirOwn()
    {
        // Watermark/Version read interactive=true but are neither base nor overlay: with only those
        // open the set is empty (they must never become the page).
        var set = Select(
            P("Watermark", 10, true, true),
            P("GameVersion", 11, true, true));
        Assert.Empty(set);
    }

    [Fact]
    public void NothingOpen_ProducesEmptySet()
    {
        Assert.Empty(Select());
    }

    [Fact]
    public void AllHidden_ProducesEmptySet()
    {
        var set = Select(
            P("HiddenMenu", LayerMenu, false, false),
            P("HiddenCard", LayerCard, false, true));
        Assert.Empty(set);
    }

    // ---- Occlusion: a base panel fully covered by a higher base panel is dropped ----

    [Fact]
    public void Portal_DashboardCoveredByReportTool_DropsDashboard()
    {
        // Live Portal with a message open (worldBound capture 2026-06-25): the Report-layer Messages
        // tool sits on the exact rect of the Menu-layer dashboard, fully covering it, while the ribbon
        // occupies a separate top strip. Both base panels read interactive=false (the frontier is the
        // tool, but Report is a base layer, so activeForeground stays -1). The covered dashboard must
        // not render — it duplicated the tool's message list, the bug this rule fixes.
        var ribbon = new PanelRect(0, 0, 1680, 84);
        var content = new PanelRect(0, 84, 1680, 861);
        var set = Select(
            P("OverviewTitleBar", LayerMenu, true, false, ribbon),
            P("PortalScreen", LayerMenu, true, false, content),
            P("PortalMessagesTool", LayerReport, true, false, content));
        Assert.Equal(new[] { "OverviewTitleBar", "PortalMessagesTool" }, set);
    }

    [Fact]
    public void BasePanels_InDistinctRegions_BothRender()
    {
        // The squad lesson with geometry (fixture 2's intent): a Menu shell and a Report panel that
        // occupy different regions — neither covers the other — both render. Occlusion only drops a
        // FULLY covered panel, so a co-rendered shell + report is never collapsed.
        var shell = new PanelRect(0, 84, 1680, 120);   // a tab/header strip
        var report = new PanelRect(0, 204, 1680, 741); // the table below it
        var set = Select(
            P("ShellWithTabs", LayerMenu, true, false, shell),
            P("MainReport", LayerReport, true, true, report));
        Assert.Equal(new[] { "ShellWithTabs", "MainReport" }, set);
    }

    [Fact]
    public void PartialOverlap_DoesNotCount_AsCovered()
    {
        // A higher panel overlapping but not fully containing a lower one does not drop it (the lower
        // panel still shows the uncovered remainder). Only full containment means "not visible".
        var lower = new PanelRect(0, 84, 1680, 861);
        var partial = new PanelRect(0, 400, 1680, 545); // covers the bottom, not the top
        var set = Select(
            P("LowerContent", LayerMenu, true, false, lower),
            P("PartialTool", LayerReport, true, false, partial));
        Assert.Equal(new[] { "LowerContent", "PartialTool" }, set);
    }

    [Fact]
    public void SameLayerIdenticalRects_BothRender_NoZOrderImplied()
    {
        // Coverage only drops across a strictly higher layer. Two panels on the same layer with the
        // same rect imply no z-order, so neither is dropped (we never guess which is on top).
        var r = new PanelRect(0, 84, 1680, 861);
        var set = Select(
            P("A", LayerMenu, true, false, r),
            P("B", LayerMenu, true, false, r));
        Assert.Equal(new[] { "A", "B" }, set);
    }

    [Fact]
    public void CoveredDashboard_StillRenders_WhenItIsTheOnlyContent()
    {
        // With no panel on top, the dashboard is the visible screen and renders normally — the rule
        // only suppresses it while a higher base panel actually covers it.
        var content = new PanelRect(0, 84, 1680, 861);
        var set = Select(
            P("OverviewTitleBar", LayerMenu, true, false, new PanelRect(0, 0, 1680, 84)),
            P("PortalScreen", LayerMenu, true, false, content));
        Assert.Equal(new[] { "OverviewTitleBar", "PortalScreen" }, set);
    }
}
