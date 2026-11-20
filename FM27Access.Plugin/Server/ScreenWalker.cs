using System;
using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Walks the active panel's live VisualElement tree into an ordered list of render items
/// (navigation stops, tables, the consolidated scoreboard) using the same classification
/// core as the speech surface (ElementBridge.Describe + ElementClassifier.Classify +
/// TreeWalker.NavigationStops). The renderer turns these items into HTML; the walker
/// makes no markup itself.
/// </summary>
internal static class ScreenWalker
{
    // A cheap structural "shape" signature of the active panel, used only inside the
    // post-navigation settle window to notice content that populates after the panel became
    // "ready". Walks every visible element contributing (name, childCount) — no classification,
    // no adapters, no table-row scrolling — so it stays light enough to run a few times right
    // after each navigation. Read live, never cached.
    internal static string ShapeSignature(object panelObj, bool excludeStreamedRows = false)
    {
        var nodes = new List<(string name, int childCount)>();
        if (TutorialContent.RootFor(panelObj) is VisualElement panel)
            CollectShape(panel, nodes, excludeStreamedRows: excludeStreamedRows);
        return ScreenShape.Compose(nodes);
    }

    // The aggregate shape across the interactive set (Phase 3, plan §7.4): each panel's own shape,
    // combined in set order. Watched only inside the settle window, exactly like the single-panel
    // shape it replaces — so late content populating in any rendered panel is caught.
    internal static string ShapeSignature(IReadOnlyList<InteractivePanels.PanelView> set, bool excludeStreamedRows = false)
    {
        var parts = new List<string>(set.Count);
        foreach (var pv in set)
            parts.Add(ShapeSignature(pv.Panel, excludeStreamedRows));
        return AggregateSignature.Combine(parts);
    }

    private static void CollectShape(VisualElement el, List<(string name, int childCount)> nodes,
        bool foldedContent = false, bool excludeStreamedRows = false, bool columnSummary = false,
        bool streamedContent = false)
    {
        if (!ElementBridge.IsVisible(el)) return;
        int childCount = el.hierarchy.childCount;
        string typeName = el.GetIl2CppType().Name;
        if (!columnSummary && typeName == "BindingExpect" && FindChildNamed(el, "frame0") is { } summaryLabels)
            columnSummary = HasChildNamed(el, "Frame 1000002441")
                || CompactSummaryColumns(el, summaryLabels).Count > 0;
        foldedContent |= typeName == "SIRadioButton" || el.name == "column-headers";
        // Consolidated summaries have no standalone leaf ids in the unchanged
        // FM26 table HTML. Observe their captured captions/ratings through the
        // existing action settle gate so late native updates are not stranded.
        foldedContent |= columnSummary;
        // Ordinary controls keep the original shape-only contract: their own
        // checked/pressed changes must not cause redundant radio refreshes.
        nodes.Add((el.name, childCount));
        // A message row can acquire its native command after its text binds.
        // Observe that structural capability without treating a disabled real
        // command as information or forcing refreshes for ordinary selections.
        if (typeName == "SIButton" && el.name == "NewsItemButton")
            // Filters can replace the command owner while retaining identical
            // labels/structure. The old native button must not remain served.
            nodes.Add(("message-action-ready:" + el.Pointer, Adapters.MessageButtonAdapter.HasNativeAction(
                el.TryCast<SI.Bindable.SIButton>()) ? 1 : 0));
        // A native button can finish binding its callback after its caption and
        // children arrive (shared confirmation dialogs do this). That changes
        // readonly content into a command without changing the visual tree.
        // Observe capability through the existing bounded settle gate, keeping
        // ordinary enabled/pressed/checked values on their local update path.
        // Streamed rows retain their separate realization/record identity.
        if (typeName == "SIButton")
        {
            var button = el.TryCast<SI.Bindable.SIButton>();
            nodes.Add((streamedContent ? "button-command-ready" : "button-command-ready:" + el.Pointer,
                button != null && (button.CanSubmit || Adapters.NativeButtonCommand.HasBoundCommand(el)) ? 1 : 0));
        }
        // The loader deliberately realizes/recycles these native rows while
        // harvesting the page. They are read after the walk, so their changed
        // window does not invalidate content captured by that earlier walk.
        // Full SSE shape observation still includes them; only the cross-frame
        // walk/render boundary excludes them. The loader validates its schema.
        if (excludeStreamedRows && typeName == "StreamedTable") return;
        streamedContent |= typeName == "StreamedTable";
        // Live match charts draw their series as geometry, without adding a
        // text leaf per sample. Reuse the structural update gate for actual
        // new chances/changed momentum, not a continuously advancing clock.
        if (typeName == "FMXGMatchStoryChart")
        {
            var chart = el.Cast<FM.UI.FMXGMatchStoryChart>();
            nodes.Add(("xg-home", chart._HomeChartData_k__BackingField?.Count ?? 0));
            nodes.Add(("xg-away", chart._AwayChartData_k__BackingField?.Count ?? 0));
        }
        if (typeName == "FMMatchMomentumChart")
        {
            var points = el.Cast<FM.UI.FMMatchMomentumChart>().m_matchMomentumData;
            nodes.Add(("momentum", points?.Count ?? 0));
            if (points != null)
                for (int i = 0; i < points.Count; i++)
                    nodes.Add(("momentum:" + i, BitConverter.SingleToInt32Bits(points[i]._MomentumValue_k__BackingField)));
        }
        if (typeName == "FMHeatMap")
        {
            // Selecting a player/phase updates the same-sized native texture
            // after its summary text. Observe that draw revision during the
            // existing settle/retry window so the table cannot keep old data.
            var texture = el.Cast<FM.UI.FMHeatMap>().m_heatTexture;
            nodes.Add((texture == null ? "heat-texture:none" : "heat-texture:" + texture.Pointer,
                texture == null ? 0 : unchecked((int)texture.updateCount)));
        }
        // FM27 can mount an empty text leaf before binding its caption. A blank
        // leaf has no stop to track through LiveValues yet; watch readiness in
        // the existing settle window so its arrival cannot be lost. Ordinary
        // nonempty value changes retain the original local values channel.
        // Conditional native branches can have identical names/counts while
        // owning different text sources. Retain their native identity: otherwise
        // a late switch can strand the old branch's LiveValues source in HTML.
        // Streamed rows have their own record/realization validation and may be
        // recycled by harvesting, so do not treat that recycling as a new shape.
        if (typeName == "SIText")
            nodes.Add((streamedContent ? "text-ready" : "text-ready:" + el.Pointer,
                string.IsNullOrWhiteSpace(el.TryCast<TextElement>()?.text) ? 0 : 1));
        // A star widget keeps the same five children while its native values
        // bind. Synthetic tables capture the rating during the walk, so its
        // readiness must participate in the same cross-frame retry boundary.
        if (typeName == "FMStarRatingTemp")
        {
            var stars = el.Cast<FM.UI.FMStarRatingTemp>();
            nodes.Add(("stars-ready", stars.m_minValueReady && stars.m_maxValueReady ? 1 : 0));
            if (columnSummary)
            {
                nodes.Add(("summary-stars-min", (int)Adapters.StarRatingHelper.MinValueProperty.GetValue(stars)));
                nodes.Add(("summary-stars-max", (int)Adapters.StarRatingHelper.MaxValueProperty.GetValue(stars)));
            }
        }
        // FM27's SIEnabled binding wrappers change dependent fields without
        // adding/removing children (coach-role and Next-button dependencies).
        // Include that native dependency boundary, not every control's state,
        // so the existing action settle window delivers newly usable fields.
        if (typeName == "SIEnabled")
            nodes.Add(("SIEnabled", new SI.Bindable.SIEnabled(el.Pointer).Value ? 1 : 0));
        // FM27 can attach a radio row before binding its caption/description.
        // Those labels are folded into the original HTML input, not tracked leaf
        // text. Observe content identity during the existing settle window so a
        // late bind cannot leave a blank or previous-step option indefinitely.
        // Deliberately exclude the checked state: ordinary selection still uses
        // the original radio update/focus contract without extra body refreshes.
        // Table headers are also captured rather than live text nodes. A tab
        // can rebind its columns in place while retaining the same row count.
        if (foldedContent && typeName == "SIText")
            nodes.Add(("folded-label:" + (el.TryCast<TextElement>()?.text ?? ""), 0));
        for (int i = 0; i < childCount; i++)
            CollectShape(el.hierarchy[i], nodes, foldedContent, excludeStreamedRows, columnSummary, streamedContent);
    }

    // Walk the active panel into ordered render items: navigation stops and tables.
    // Tables are not recursed into — their cells render as a <table>, not flat stops.
    internal static List<RenderItem> WalkScreen(object panelObj)
    {
        var items = new List<RenderItem>();
        if (TutorialContent.RootFor(panelObj) is VisualElement panel)
            WalkRec(panel, null, items);
        InsertShortcutsHeading(items, panelObj);
        ReorderTitleBarControls(items);
        ReorderMatchHeaderControls(items, panelObj);
        InsertSoundCuesTrigger(items, panelObj);
        ReorderCardHeaderLinks(items);
        PromotePitchPhaseCaptions(items);
        RemovePitchCollapseButton(items);
        RemoveAllInstructionsButton(items);
        RestructureTeamSelectionLayout(items, panelObj);
        InsertFullTeamSelectionHeadings(items, panelObj);
        InsertTeamNewsHeadings(items, panelObj);
        RestructureTunnelInterview(items, panelObj);
        InsertMatchSquadHeading(items, panelObj);
        InsertSelectedManagerHeading(items, panelObj);
        InsertAppearanceUnavailableNote(items, panelObj);
        InsertPostMatchResultLines(items, panelObj);
        ConsolidateProjectionBlocks(items);
        ConsolidatePitchIndicators(items, panelObj);
        PromoteFirstTextHeadings(items, panelObj);
        ApplyTableColumnMoves(items, panelObj);
        RestructureBudgetSpeakTo(items, panelObj);
        FoldBudgetTilePairs(items);
        FoldCoachWorkloadCounts(items);
        JoinCaptionedTotals(items);
        GameCreationSummary.Restructure(items, panelObj);
        RestructureAdvancedSetup(items, panelObj);
        RestructureContractNegotiation(items, panelObj);
        RestructureTransferOffer(items, panelObj);
        RestructureTransferActivity(items, panelObj);
        RestructureResponsibilities(items, panelObj);
        RestructureSquadHappiness(items, panelObj);
        FoldTacticWizardPreview(items, panelObj);
        GroupPlayerRecommendationProsAndCons(items, panelObj);
        RestructureComparisonTables(items, panelObj);
        MergeComparisonSeasonStats(items, panelObj);
        MergeComparisonAttributes(items, panelObj);
        AddComparisonSuitability(items, panelObj);
        RestructureIndividualTraining(items, panelObj);
        StripComparisonReportPitch(items, panelObj);
        NameComparisonPlayers(items, panelObj);
        // Full boundary: the fold reads names and ancestry across the whole banner, and a throw
        // here must cost the banner its table, not the page its body.
        try { ConsolidatePlayerBanner(items); }
        catch (Exception ex) { Degrade.Note($"Player banner fold failed, raw stops kept: {ex.Message}"); }
        try { FormHistoryChart.Consolidate(items); }
        catch (Exception ex) { Degrade.Note($"Form history fold failed, native content kept: {ex.Message}"); }
        // Shared native confirmations repeat their own title in the synthetic
        // section chrome. Promote that exact source, preserving inline actions
        // and descriptions through the existing section-heading renderer.
        var dialogTitle = ServerText.DialogTitleElement(panelObj as VisualElement);
        if (dialogTitle != null)
            foreach (var item in items)
                if (item is StopItem stop && stop.Element is Adapters.PluginAdapterBase native
                    && native.BackingElement.Pointer == dialogTitle.Pointer
                    && string.Equals(ServerText.Clean(DisplayText.Of(stop.Element)).Trim(),
                        ServerText.Clean(ServerText.GetTitle(panelObj)).Trim(), StringComparison.OrdinalIgnoreCase))
                { stop.IsSectionTitle = true; break; }
        return items;
    }

    internal static void WalkFragment(VisualElement element, List<RenderItem> items)
        => WalkRec(element, null, items);

    // The recommendations card lays each pro/con sentence out as a repeated visual row, but the
    // flat walk loses that relationship and renders a run of unrelated paragraphs. Preserve each
    // consecutive run as one semantic list. The row-template name is shared by pros and cons while
    // their localized title stops separate the runs, so this stays language-independent and never
    // merges the two sections. The decorative image in each row is profile-hidden separately.
    // The Scouting Report's Scout and Analyst tabs use the same row; an analyst row also prints the
    // stat's league rank as a second text ("High Intensity Sprints - 3", "(349th in Ligue 1)"),
    // which stays on its row's item instead of reading as a stray paragraph.
    private static readonly HashSet<string> ProConListPanels = new() { "RecruitmentPlayerRecommendationsCard", "ScoutingReportCard" };
    private const string ProConListItemTemplate = "data_display-string-list-item-pro-con";

    private static void GroupPlayerRecommendationProsAndCons(List<RenderItem> items, object panelObj)
    {
        if (!ProConListPanels.Contains(NavigationManager.GetPanelName(panelObj) ?? "")) return;

        for (int i = 0; i < items.Count; i++)
        {
            if (ProConRowOf(items[i]) == null) continue;

            var list = new TextListItem();
            int start = i;
            IntPtr lastRow = IntPtr.Zero;
            VisualElement row;
            while (i < items.Count && (row = ProConRowOf(items[i])) != null)
            {
                var element = ((StopItem)items[i]).Element;
                if (row.Pointer == lastRow) list.TrailingOf(list.Items.Count - 1).Add(element);
                else { list.Items.Add(element); lastRow = row.Pointer; }
                i++;
            }

            items.RemoveRange(start, i - start);
            items.Insert(start, list);
            i = start;
        }
    }

    // The pro/con row a text stop sits in, or null when it is not a pro/con text.
    private static VisualElement ProConRowOf(RenderItem item)
    {
        if (item is not StopItem stop || ServerText.SafeRole(stop.Element) != "text") return null;
        var backing = (stop.Element as Adapters.PluginAdapterBase)?.BackingElement;
        for (var p = backing?.hierarchy.parent; p != null; p = p.hierarchy.parent)
            if (p.name == ProConListItemTemplate) return p;
        return null;
    }

    // The create-a-tactic wizard's formation steps preview the running choice as two blocks, each
    // laying the chosen formation's name above a pitch graphic with the phase caption beneath it.
    // Read linearly that put every value before its label — "4-2-3-1 DM Wide", "In Possession",
    // "No formation", "Out of Possession" — so a formation reads next to the phase it does not
    // belong to, the one wrong conclusion this pane can produce. Fold each name/caption pair into one
    // "In Possession: 4-2-3-1 DM Wide" line; the name element keeps its own id on the values
    // stream, so it still updates in place as the player picks. Gated on the pane's exact shape (a
    // selected-tactic-name whose block also holds the phase's pitch widget, with the caption a
    // direct child of that block), so a pair that does not hold is left raw rather than captioned
    // with a label that might not be its own. Speech keeps the raw walk order — profiles cannot
    // reorder — but the caption still follows its value there, so nothing is lost, only unmerged.
    private const string TacticWizardPanelName = "TacticalPlannerTool";
    private const string SelectedTacticNameElement = "selected-tactic-name";
    private static readonly string[] TacticPreviewPitchWidgets = { "TacticInstructionsIP", "TacticInstructionsOOP" };

    private static void FoldTacticWizardPreview(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != TacticWizardPanelName) return;

        for (int i = items.Count - 2; i >= 0; i--)
        {
            if (items[i] is not StopItem value || items[i + 1] is not StopItem caption) continue;
            if (ServerText.SafeRole(value.Element) != "text" || ServerText.SafeRole(caption.Element) != "text") continue;

            var valueEl = (value.Element as Adapters.PluginAdapterBase)?.BackingElement;
            var captionEl = (caption.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (valueEl == null || captionEl == null) continue;
            // FM27's selected IP branch has an unnamed SIText; its empty
            // branch and the OOP branch retain the original FM26 name. Both
            // use the same remapper/value, direct caption and phase-widget
            // contract. Do not borrow another nearby text as the value.
            if (valueEl.name != SelectedTacticNameElement
                && (!string.IsNullOrEmpty(valueEl.name) || valueEl.GetIl2CppType().Name != "SIText")) continue;
            if (valueEl.hierarchy.parent?.GetIl2CppType().Name != "BindingRemapper") continue;

            var block = valueEl.hierarchy.parent?.hierarchy.parent;
            if (block == null || !IsTacticPreviewBlock(block)) continue;
            if (captionEl.hierarchy.parent?.Pointer != block.Pointer) continue;

            string label;
            try { label = CleanOrNull(DisplayText.Of(caption.Element)); }
            catch (Exception ex)
            {
                Log.Warn($"[AccessibilityServer] Tactic preview caption read failed, pair left raw: {ex.Message}");
                continue;
            }
            if (label == null) continue;

            items[i] = new LabelValueItem { Label = label, Value = value.Element };
            items.RemoveAt(i + 1);
        }
    }

    private static bool IsTacticPreviewBlock(VisualElement block)
    {
        for (int i = 0; i < TacticPreviewPitchWidgets.Length; i++)
            if (HasChildNamed(block, TacticPreviewPitchWidgets[i])) return true;
        return false;
    }

    // The post-match Breaking News page states the simulated match's result plainly only inside
    // its Matches tile, below the story tiles. Surface each fixture that tile shows as its own
    // line at the top of the section, read live from the tile's HomeTeam/hasResult/AwayTeam
    // frames (the same structures the Fixtures schedule reads), so the line can never disagree
    // with what the game displays. A missing tile inserts nothing; an unreadable team name skips
    // its line with a note (the tile itself still renders below, so the result stays reachable);
    // an unreadable score degrades to a marker, matching the fixtures policy.
    private const string ResultLinePrefix = "Result: ";

    private static void InsertPostMatchResultLines(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != "PostMatchNewsPanel") return;
        if (panelObj is not VisualElement panel) return;

        try
        {
            var tiles = new List<VisualElement>();
            CollectMatchTiles(panel, tiles);
            for (int i = tiles.Count - 1; i >= 0; i--)
            {
                var tile = tiles[i];
                var home = FindChildNamed(tile, "HomeTeam");
                var away = FindChildNamed(tile, "AwayTeam");
                string homeName = CleanOrNull(home != null ? ReadFirstText(home) : null);
                string awayName = CleanOrNull(away != null ? ReadFirstText(away) : null);
                if (homeName == null || awayName == null)
                {
                    Degrade.Note("post-match news: result line team name unreadable, line omitted");
                    continue;
                }
                var scoreSwitch = FindDescendantNamed(tile, "hasResult");
                string score = CleanOrNull(scoreSwitch != null ? ReadFirstText(scoreSwitch) : null)
                               ?? Degrade.Value("post-match news: result score unreadable");
                items.Insert(0, new NoteItem { Text = ResultLinePrefix + homeName + " " + score + " " + awayName });
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Post-match result line build failed, lines omitted: {ex.Message}");
        }
    }

    private static string CleanOrNull(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var cleaned = TextFilters.CleanForDisplay(raw);
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    // The full-screen Team Selection (the pre-match "review squad selection" screen) shares the
    // tactics-planner layout, but its subs strip renders as collapsed disclosures, so the
    // structure-gated RestructureTeamSelectionLayout pass never fires there. Give its regions the
    // same landmarks by panel name instead: the tactic picker strip, the style/mentality settings,
    // the match-preparation actions (Set Pieces through Set For and the fixture chip), the squad
    // actions from Quick Pick on, the phase tablist, and the selection table's view dropdown.
    // Every anchor is structural; a missing anchor skips its own heading.
    private const string MatchPreparationHeadingLabel = "Match preparation";
    private const string YourSquadHeadingLabel = "Your squad";

    private static void InsertFullTeamSelectionHeadings(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != "FullScreenTeamSelection") return;

        RenderItem tabBar = null;
        StopItem tacticPicker = null, styleButton = null, matchPrep = null, quickPick = null, viewDropdown = null;
        foreach (var item in items)
        {
            if (tabBar == null && item is TabBarItem) { tabBar = item; continue; }
            if (item is not StopItem s) continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null) continue;
            if (tacticPicker == null && AncestorNameContains(backing, "tactics-picker")) tacticPicker = s;
            else if (styleButton == null && AncestorNameContains(backing, "style-split-button")) styleButton = s;
            else if (matchPrep == null && AncestorNameContains(backing, "MatchNavigationVisible")) matchPrep = s;
            else if (quickPick == null && AncestorNameContains(backing, "QuickPick")) quickPick = s;
            else if (viewDropdown == null && backing.name == "ViewDropdown") viewDropdown = s;
        }

        // Level 3 (<h3>) nests each region under the panel's own <h2>, matching the tactics pass.
        // RestructureTeamSelectionLayout (the shared tactics-layout pass) inserts four of these
        // same landmarks when the subs strip renders alongside the table, so each insert is
        // guarded: a heading that is already in items is never doubled.
        if (tacticPicker != null)
            InsertHeadingOnce(items, tacticPicker, TacticHeadingLabel);
        if (styleButton != null)
            InsertHeadingOnce(items, styleButton, TacticSettingsHeadingLabel);
        if (matchPrep != null)
            InsertHeadingOnce(items, matchPrep, MatchPreparationHeadingLabel);
        if (quickPick != null)
            InsertHeadingOnce(items, quickPick, YourSquadHeadingLabel);
        if (tabBar != null)
            InsertHeadingOnce(items, tabBar, PitchViewHeadingLabel);
        if (viewDropdown != null)
            InsertHeadingOnce(items, viewDropdown, TeamSelectionHeadingLabel);
    }

    // The pre-match Team News panel runs the match header, both starting lineups, both subs lists,
    // the referee and the staff advice into one flat run — and nothing marks where the home team's
    // players end and the opposition's begin (home rows read name-then-number, away rows
    // number-then-name). Give the regions landmarks: "Match details" over the competition/clubs/
    // date/venue block, one heading per starting lineup carrying the team's own name (read live
    // from the match header's HomeTeamName/AwayTeamName), and a team heading over each subs list
    // nested under the game's own "Substitutes" divider (profile-promoted to a heading). Anchors
    // are the lists' stable container names; a missing anchor skips its own heading; an unreadable
    // team name degrades to the positional "Home"/"Away" label (true regardless of data) with a note.
    private const string MatchDetailsHeadingLabel = "Match details";
    private const string StartingLineupHeadingSuffix = " starting lineup";

    private static void InsertTeamNewsHeadings(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != "TeamNewsPanel") return;
        if (panelObj is not VisualElement panel) return;

        RenderItem matchInfo = null, homeStarters = null, awayStarters = null, homeSubs = null, awaySubs = null;
        foreach (var item in items)
        {
            // A linked first name now uses JoinedTextItem. Its own source is
            // still the first item of the native group, ahead of the number.
            var source = item is StopItem s ? s.Element
                : item is JoinedTextItem joined && joined.Parts.Count > 0 ? joined.Parts[0] : null;
            var backing = (source as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null) continue;
            if (matchInfo == null && AncestorNameContains(backing, "CompInfo")) matchInfo = item;
            else if (homeStarters == null && AncestorNameContains(backing, "HomeStarters")) homeStarters = item;
            else if (awayStarters == null && AncestorNameContains(backing, "AwayStarters")) awayStarters = item;
            else if (homeSubs == null && AncestorNameContains(backing, "HomeSubs")) homeSubs = item;
            else if (awaySubs == null && AncestorNameContains(backing, "AwaySubs")) awaySubs = item;
        }

        string homeName = TeamNewsTeamName(panel, "HomeTeamName", "Home");
        string awayName = TeamNewsTeamName(panel, "AwayTeamName", "Away");

        if (matchInfo != null)
            items.Insert(items.IndexOf(matchInfo), new HeadingItem { Text = MatchDetailsHeadingLabel, Level = 3 });
        if (homeStarters != null)
            items.Insert(items.IndexOf(homeStarters), new HeadingItem { Text = homeName + StartingLineupHeadingSuffix, Level = 3 });
        if (awayStarters != null)
            items.Insert(items.IndexOf(awayStarters), new HeadingItem { Text = awayName + StartingLineupHeadingSuffix, Level = 3 });
        if (homeSubs != null)
            items.Insert(items.IndexOf(homeSubs), new HeadingItem { Text = homeName, Level = 4 });
        if (awaySubs != null)
            items.Insert(items.IndexOf(awaySubs), new HeadingItem { Text = awayName, Level = 4 });
    }

    // The tunnel-interview layout walks the answer sidebar before the journalist's question, so
    // the page read "here are your answers" before what was asked. Restructure the HTML order:
    // drop the floating speaker card (a NonPlayerInfo hover-duplicate of the header strip — the
    // journalist's name, publication and body language all render there already, and the card's
    // body-language text is structurally indistinguishable from the strip's within the profile
    // matcher's 8-level path, which is why this drop lives here), then move the SpeakTo sidebar
    // (the manager header and tone-grouped answers) behind the interview content so the question
    // reads before its answers. Speech keeps the raw walk order (profiles cannot reorder); the
    // card's name/publication are profile-hidden there, leaving one stray body-language text.
    private static void RestructureTunnelInterview(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != "TunnelInterviewPanel") return;

        var sidebar = new List<RenderItem>();
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is not StopItem s) continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null) continue;
            if (AncestorNameContains(backing, "NonPlayerInfo")) { items.RemoveAt(i); continue; }
            if (AncestorNameContains(backing, "SpeakToSidePanel")) { sidebar.Insert(0, items[i]); items.RemoveAt(i); }
        }
        items.AddRange(sidebar);
    }

    private static string TeamNewsTeamName(VisualElement panel, string elementName, string positionalFallback)
    {
        var el = FindDescendantNamed(panel, elementName);
        var name = el != null ? CleanOrNull(ReadFirstText(el)) : null;
        if (name == null)
            Degrade.Note($"team news: {elementName} unreadable, positional heading used");
        return name ?? positionalFallback;
    }

    // One Projections-card metric block, read structurally: the block's leading SIText is the
    // metric name, each SeasonNFrame holds the season label ("End of 2026 season") and its value
    // inside a ValueFrame. Null (caller falls through to the raw walk) when the shape doesn't
    // hold — a missing title, no season frames, or a frame missing its label or value.
    private static ProjectionBlockItem TryReadProjectionBlock(VisualElement block)
    {
        string metric = CleanOrNull(ReadFirstText(block));
        if (metric == null) return null;

        var seasons = new List<(string Label, string Value)>();
        for (int n = 1; n <= 3; n++)
        {
            var frame = FindDescendantNamed(block, "Season" + n + "Frame");
            if (frame == null) continue;
            string label = CleanOrNull(ReadFirstText(frame));
            var valueFrame = FindDescendantNamed(frame, "ValueFrame");
            string value = valueFrame != null ? CleanOrNull(ReadFirstText(valueFrame)) : null;
            if (label == null || value == null) return null;
            seasons.Add((label, value));
        }

        if (seasons.Count == 0) return null;
        return new ProjectionBlockItem { Metric = metric, Seasons = seasons };
    }

    // The Scouting Assignments card, and the one element name inside it that anchors the focus
    // list. Three other StreamedObjectLists sit on this panel (inside the World Knowledge tile);
    // only the focus list carries a name, so the name is the whole gate.
    private const string ScoutAssignmentsPanel = "RecruitmentScoutAssignmentsCard";
    private const string ScoutFocusListName = "StreamedObjectList";

    // The Mentoring card, whose summary tiles are descended into so their content grids table.
    private const string MentoringPanel = "MentoringCard";

    // Consolidates the focus list into a table item. Each row is expected to walk out as exactly
    // three stops — the focus name, then its Edit and Delete buttons — and anything else declines
    // the WHOLE list (returns false, caller walks it raw). Per-row tolerance would be worse than
    // useless here: a row that lost its name would put a Delete button under the previous focus's
    // heading, which is precisely the confusion this exists to remove.
    //
    // An empty list is a success, not a decline: a scout with no focuses assigned is the case that
    // most needs saying out loud (the section simply went silent before, and read as "the club has
    // no recruitment focuses" when it means "this scout has none").
    private static bool TryEmitScoutFocusList(VisualElement list, ElementClassification? cls, List<RenderItem> items)
    {
        var rowEls = new List<VisualElement>();
        CollectDescendantsOfType(list, "AsyncBindingRoot", rowEls, 8);
        if (rowEls.Count == 0)
        {
            var view = FindDescendantNamed(list, "View");
            if (view != null)
                for (int i = 0; i < view.hierarchy.childCount; i++)
                {
                    var child = view.hierarchy[i];
                    if (child.GetIl2CppType().Name == "BindingRoot") rowEls.Add(child);
                }
            // An unnamed empty FM27 list has no structural signature yet.
            // Other lists on the same card are world-knowledge lists, not focuses.
            if (rowEls.Count == 0 && list.name != ScoutFocusListName) return false;
        }

        var rows = new List<ScoutFocusRow>(rowEls.Count);
        foreach (var rowEl in rowEls)
        {
            var stops = new List<RenderItem>();
            int kids = rowEl.hierarchy.childCount;
            for (int i = 0; i < kids; i++)
                WalkRec(rowEl.hierarchy[i], cls, stops);

            if (stops.Count != 3) return false;
            if (stops[0] is not StopItem nameStop) return false;
            if (stops[1] is not StopItem editStop) return false;
            if (stops[2] is not StopItem deleteStop) return false;
            if (ServerText.SafeRole(nameStop.Element) != "text") return false;
            if (ServerText.SafeRole(editStop.Element) != "button") return false;
            if (ServerText.SafeRole(deleteStop.Element) != "button") return false;

            string name;
            try { name = DisplayText.Of(nameStop.Element); }
            catch (Exception ex) { return Declined($"focus name read failed: {ex.Message}"); }
            if (string.IsNullOrWhiteSpace(name)) return Declined("a focus row had no name");

            rows.Add(new ScoutFocusRow { Name = name, Edit = editStop.Element, Delete = deleteStop.Element });
        }

        items.Add(new ScoutFocusListItem { Rows = rows });
        return true;
    }

    // Note the shape break and hand the list back to the raw walk. Every stop still renders; only
    // the table is given up, which is the honest trade when the row shape can no longer be trusted.
    private static bool Declined(string reason)
    {
        Degrade.Note($"Scouting Assignments focus list: {reason}, raw stops kept.");
        return false;
    }

    // The TransferRoom "Requirements" form, and the one radio group on it whose label the game
    // draws as a separate text above the group. An SIRadioGroup carries no label of its own, so
    // that group reached the page unnamed: a reader focusing an option heard "Transfer, radio
    // button, 1 of 3" with nothing saying what was being chosen. Folding the label into the
    // fieldset name is what the segmented-control groups already do.
    //
    // Scoped by group name rather than applied to every SIRadioGroup: the stop before a group is
    // only sometimes its label, and stealing an unrelated text would lose it from the page.
    private const string TransferRoomAdvertPanel = "TransferRoomAdvertCard";
    private const string TransferTypeGroupName = "TransferTypeRadioGroup";

    private static string FoldGroupLabel(VisualElement group, string groupName, List<RenderItem> items)
    {
        if (groupName != TransferTypeGroupName) return null;
        if (!ElementBridge.IsInPanel(group, TransferRoomAdvertPanel)) return null;
        return FoldPrecedingLabel(items);
    }

    // The Recruitment Focus tool, the other half of the scouts-and-focuses pair: where the Scouting
    // Assignments card lists staff and shows the focuses assigned to the selected one, this lists
    // focuses and shows the detail of the selected one. The two master lists are rendered the same
    // way on purpose.
    private const string RecruitmentFocusPanel = "RecruitmentFocusTool";

    // Consolidates the focus radio group into a table. Each option holds exactly three texts in tree
    // order — name, status, matched count — and anything else declines the whole list back to the
    // plain radiogroup, which still reads every value; only the column names are given up.
    //
    // The radios keep their own adapters and a shared HTML name, so the table is still one
    // single-select group: putting them in cells adds column context without costing the radio
    // semantics. Same shape as the staff table on the Scouting Assignments card.
    private static bool TryEmitFocusList(VisualElement group, List<IAccessibleElement> radios,
        List<List<IAccessibleElement>> extras, List<List<IAccessibleElement>> subGroups, List<RenderItem> items)
    {
        // Per-option action controls or nested sub-options have nowhere to go in these three
        // columns; the radiogroup renderer handles both, so hand it back rather than drop them.
        foreach (var e in extras) if (e.Count > 0) return false;
        foreach (var s in subGroups) if (s.Count > 0) return false;

        var rows = new List<FocusRow>(radios.Count);
        foreach (var radio in radios)
        {
            var backing = (radio as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null) return FocusListDeclined("a focus option had no backing element");

            var texts = new List<VisualElement>();
            CollectDescendantsOfType(backing, "SIText", texts, 12);
            if (texts.Count != 3) return FocusListDeclined($"a focus option held {texts.Count} texts, expected 3");

            string name = ReadTextOrNull(texts[0]);
            string status = ReadTextOrNull(texts[1]);
            string matched = ReadTextOrNull(texts[2]);
            if (name == null || status == null || matched == null)
                return FocusListDeclined("a focus option had an unreadable value");

            rows.Add(new FocusRow { Radio = radio, Name = name, Status = status, Matched = matched });
        }

        items.Add(new FocusListItem { Group = group, Rows = rows });
        return true;
    }

    private static bool FocusListDeclined(string reason)
    {
        Degrade.Note($"Recruitment Focus list: {reason}, plain radiogroup kept.");
        return false;
    }

    // An SIText's display text, or null when it cannot be read or is blank — the caller declines
    // rather than put an empty cell under a named column. Cleaned like every other cell the walk
    // produces, so rich-text markup never reaches the page.
    private static string ReadTextOrNull(VisualElement el)
    {
        try { return CleanOrNull(ReadSITextValue(el)); }
        catch (Exception ex) { Degrade.Note($"focus option text read failed: {ex.Message}"); return null; }
    }

    // Merge each run of consecutive ProjectionBlockItems (the Projections card's six metric
    // blocks) into one metric-by-season table, so the player reads one grid instead of six
    // repeating label/value lists. Season labels come from the first block of the run; the
    // blocks all bind the same three seasons, and a block with fewer rows just leaves its
    // trailing cells empty.
    private static void ConsolidateProjectionBlocks(List<RenderItem> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not ProjectionBlockItem) continue;

            int end = i;
            while (end + 1 < items.Count && items[end + 1] is ProjectionBlockItem) end++;

            var blocks = new List<ProjectionBlockItem>(end - i + 1);
            for (int j = i; j <= end; j++)
                blocks.Add((ProjectionBlockItem)items[j]);

            items.RemoveRange(i, end - i + 1);
            items.Insert(i, new ProjectionsTableItem
            {
                SeasonLabels = blocks[0].Seasons.ConvertAll(s => s.Label),
                Blocks = blocks,
            });
        }
    }

    // Promote a section's leading text stop to an <h3> landmark, keyed by the section container's
    // name — for section titles too deep for a profile matcher's 8-level path window (the budget
    // cards' graph blocks, the Transfer History in/out halves, the Recruitment Focus detail pane).
    // Each fragment names one section container; its first text-role stop is the section's own
    // on-screen title, promoted in place with its game text. HTML-only, like the date-separator
    // promotion — the speech surface keeps the plain text. A missing section promotes nothing.
    private static readonly Dictionary<string, string[]> FirstTextHeadingAnchors = new()
    {
        ["RecruitmentTransferBudgetsCard"] = new[] { "RecruitmentTransferBudgetAvailableGraph" },
        ["RecruitmentWageBudgetCard"] = new[]
        {
            "RecruitmentWageBudgetCardAvailableWageBudget",
            "RecruitmentWageBudgetCardExpenditureGraph",
            "RecruitmentWageBudgetCardCommittedWageTotal",
        },
        ["RecruitmentScoutingBudgetCard"] = new[] { "RecruitmentRemainingBudget", "RecruitmentExpenditure" },
        // The transfers-in / transfers-out halves, and the selected focus's detail pane title.
        ["RecruitmentTransferHistoryReport"] = new[] { "Frame 1000002472", "Frame 1000002473" },
        ["RecruitmentFocusTool"] = new[] { "Frame 1000002580" },
    };

    private static void PromoteFirstTextHeadings(List<RenderItem> items, object panelObj)
    {
        if (!FirstTextHeadingAnchors.TryGetValue(NavigationManager.GetPanelName(panelObj) ?? "", out var fragments)) return;

        foreach (var fragment in fragments)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is not StopItem s) continue;
                if (ServerText.SafeRole(s.Element) != "text") continue;
                var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
                if (backing == null || !AncestorNameContains(backing, fragment)) continue;

                string text;
                try { text = DisplayText.Of(s.Element); }
                catch (Exception ex)
                {
                    Log.Warn($"[AccessibilityServer] Section-title promotion read failed, kept as text: {ex.Message}");
                    break;
                }
                if (!string.IsNullOrEmpty(text))
                    items[i] = new HeadingItem { Text = text, Level = 3 };
                break;
            }
        }
    }

    // Per-panel column moves for the table surfaces: place a named column directly before/after a
    // target column, so a control and the column it acts on read adjacently (the First Team table
    // separates the "Picked" position dropdown from "Player" with the Inf column; the Tactics
    // Planner and full-screen Team Selection put four role columns between "Pkd" and the "Player"
    // selection dropdown). Columns are matched by the game's own header text: a table or view
    // lacking either header keeps the game's raw order, so the move only applies where it is valid.
    private sealed class ColumnMove
    {
        public string Column;  // header text of the column to move
        public string Target;  // header text of the column to place it next to
        public bool After;     // false: directly before Target; true: directly after
    }

    private static readonly Dictionary<string, ColumnMove[]> TableColumnMoves = new(StringComparer.Ordinal)
    {
        // Selection, Picked, Inf, Player, ... -> Selection, Inf, Picked, Player (same prefix in all 14 views)
        ["TeamSquadReport"] = new[] { new ColumnMove { Column = "Picked", Target = "Player" } },
        // Pkd, IP Role, OOP Role, Tactic Ability, PI, Player, ... -> Pkd, Player, IP Role, ...
        ["FullScreenTeamSelection"] = new[] { new ColumnMove { Column = "Player", Target = "Pkd", After = true } },
        ["TacticalPlannerTool"] = new[] { new ColumnMove { Column = "Player", Target = "Pkd", After = true } },
        // The in-match tactics panel carries the same table with its own column set:
        // Pkd, Role, Role Ability, PI, Player, ... -> Pkd, Player, Role, ...
        ["MatchTactics"] = new[] { new ColumnMove { Column = "Player", Target = "Pkd", After = true } },
    };

    // One note per panel+column when a configured move finds no matching headers (a changed game
    // template, or a non-English game language), so a quietly kept game order stays diagnosable
    // without flooding the log on every render.
    private static readonly HashSet<string> _columnMoveMisses = new(StringComparer.Ordinal);

    private static void ApplyTableColumnMoves(List<RenderItem> items, object panelObj)
    {
        var panelName = NavigationManager.GetPanelName(panelObj) ?? "";
        if (!TableColumnMoves.TryGetValue(panelName, out var moves)) return;
        foreach (var item in items)
        {
            if (item is not TableItem t || t.Headers == null || t.Headers.Count == 0) continue;
            t.DisplayOrder = ComputeColumnOrder(t.Headers, moves, panelName);
        }
    }

    // The display permutation the moves produce, or null when they change nothing (headers missing,
    // or the columns are already adjacent). Later moves see the order earlier moves produced.
    private static int[] ComputeColumnOrder(List<Contexts.TableHeader> headers, ColumnMove[] moves, string panelName)
    {
        var order = new List<int>(headers.Count);
        for (int i = 0; i < headers.Count; i++) order.Add(i);
        foreach (var m in moves)
        {
            int col = IndexInOrder(headers, order, m.Column);
            int tgt = IndexInOrder(headers, order, m.Target);
            if (col < 0 || tgt < 0)
            {
                if (_columnMoveMisses.Add(panelName + "|" + m.Column))
                    Log.Debug($"[AccessibilityServer] Column move \"{m.Column}\" next to \"{m.Target}\" on {panelName}: header not found, game order kept");
                continue;
            }
            int value = order[col];
            order.RemoveAt(col);
            if (col < tgt) tgt--;
            order.Insert(m.After ? tgt + 1 : tgt, value);
        }
        for (int i = 0; i < order.Count; i++)
            if (order[i] != i) return order.ToArray();
        return null;
    }

    // The display position of the column whose cleaned header text matches, or -1. Compares the
    // display-cleaned form because raw header text can carry TMP markup.
    private static int IndexInOrder(List<Contexts.TableHeader> headers, List<int> order, string headerText)
    {
        for (int i = 0; i < order.Count; i++)
            if (string.Equals(Speech.TextFilters.CleanForDisplay(headers[order[i]].Text)?.Trim(), headerText, StringComparison.Ordinal))
                return i;
        return -1;
    }

    // The Squad Planner position strip and the Best XI pitch draw one FMPitchIndicator per
    // position, each holding the position code and its values (an ability star rating; on Best XI
    // a best-player and best-prospect name each with a rating). Walked flat they read as dozens of
    // context-free lines; consolidate each pitch into one semantic table. HTML-only — the speech
    // surface keeps the raw stop order. A shape that doesn't hold (an unreadable text, a group
    // with no position) leaves the raw stops untouched rather than a half-built table.
    private static void ConsolidatePitchIndicators(List<RenderItem> items, object panelObj)
    {
        var panelName = NavigationManager.GetPanelName(panelObj) ?? "";
        try
        {
            if (panelName == "RecruitmentSquadPlannerReport")
            {
                ConsolidatePlannerPositionStrip(items);
                InsertPlannerPlanHeading(items, panelObj);
            }
            else if (panelName == "BestXICard") ConsolidateBestXIPitch(items);
            else if (panelName == ComparisonPanelName) ConsolidateComparisonPositionPicker(items);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Pitch-indicator consolidation failed, raw stops kept: {ex.Message}");
        }
    }

    // The planner's per-position plan section (the view dropdown, Add, and the plan table) is
    // headed on screen by the selected position's name, but that title folds into the view
    // dropdown's label, leaving the section without a landmark. Re-read the title from its frame
    // and insert it as an <h3> before the dropdown, so jumping to the selected position's plan is
    // one heading press after picking a position in the strip table. Missing pieces skip.
    private static void InsertPlannerPlanHeading(List<RenderItem> items, object panelObj)
    {
        if (panelObj is not VisualElement panel) return;

        var titleFrame = FindDescendantNamed(panel, "Frame-1000002559");
        string title = titleFrame != null ? CleanOrNull(ReadFirstText(titleFrame)) : null;
        if (title == null) return;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not StopItem s) continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null || backing.name != "ViewDropdown") continue;
            items.Insert(i, new HeadingItem { Text = title, Level = 3 });
            return;
        }
    }

    // The Squad Depth chart as one table: a row per position (back to front, as the pitch draws
    // them), the game's own position and role codes, its squad-depth count, and the realised depth
    // list in rank order. The rank columns are the game's own "1 / 2 / 3" headers; the count column
    // is what keeps the read honest when a position has more players than the three the chart ever
    // shows. False leaves the raw stops alone.
    private static bool TryEmitSquadDepth(VisualElement pitch, List<RenderItem> items)
    {
        List<Tactics.SquadDepthPitch.DepthSlot> slots;
        try { slots = Tactics.SquadDepthPitch.Read(pitch); }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Squad depth read failed, raw stops kept: {ex.Message}");
            return false;
        }
        if (slots.Count == 0) return false;

        int ranks = 0;
        foreach (var s in slots) if (s.Players.Count > ranks) ranks = s.Players.Count;
        if (ranks == 0) return false;

        var headers = new List<string> { "Position", "Role", "Depth" };
        for (int r = 1; r <= ranks; r++) headers.Add(r.ToString());

        var rows = new List<List<string>>();
        foreach (var s in slots)
        {
            var row = new List<string>
            {
                PitchNavigation.SlotPhrase(s.Slot) ?? s.Slot,
                s.Role ?? "",
                s.Count ?? Degrade.Unreadable,
            };
            for (int r = 0; r < ranks; r++) row.Add(r < s.Players.Count ? s.Players[r] : "");
            rows.Add(row);
        }

        // The chart's only title on screen is the tab that governs it, which the walk has already
        // emitted as a tab bar; head the table with that same game text so it is one heading press
        // away. No tab above it (a context this chart has not been seen in) leaves it untitled
        // rather than named by a guess.
        var title = SelectedTabLabel(items);
        if (title != null) items.Add(new HeadingItem { Text = title, Level = 3 });
        items.Add(new SyntheticTableItem { Headers = headers.ToArray(), Rows = rows });
        return true;
    }

    private sealed class IndicatorGroup
    {
        public System.IntPtr Ptr;
        public VisualElement Indicator;
        public string Position;
        public List<string> PlayerParts = new();
        public List<string> ProspectParts = new();
    }

    // The FMPitchIndicator ancestor of a walked stop's backing element, or null when the stop is
    // not inside a pitch slot. A collected wrapper throws on Pointer reads; treated as "not a slot".
    private static VisualElement FindPitchIndicatorAncestor(VisualElement el)
    {
        for (var p = el; p != null; p = p.hierarchy.parent)
        {
            string type;
            try { type = p.GetIl2CppType().Name; }
            catch { return null; }
            if (type == "FMPitchIndicator") return p;
        }
        return null;
    }

    // Collect the consecutive run of stops sitting inside FMPitchIndicator slots under the named
    // container, grouped by slot (native-pointer identity — wrappers differ per stop). Returns the
    // groups plus the run's item range; null when there is no such run. Group content is sorted
    // into position (the "TopFrame" / "PositionText" text), prospect parts (under the
    // "HasProspectsVisible" branch) and player parts (everything else).
    private static List<IndicatorGroup> CollectIndicatorRun(
        List<RenderItem> items, string containerName, out int first, out int last)
    {
        first = -1; last = -1;
        var groups = new List<IndicatorGroup>();
        IndicatorGroup current = null;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not StopItem s)
            {
                if (first >= 0) break;
                continue;
            }
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            var indicator = backing != null ? FindPitchIndicatorAncestor(backing) : null;
            if (indicator == null || !AncestorNameContains(indicator, containerName))
            {
                if (first >= 0) break;
                continue;
            }

            System.IntPtr ptr;
            try { ptr = indicator.Pointer; }
            catch { if (first >= 0) break; continue; }

            if (first < 0) first = i;
            last = i;

            if (current == null || current.Ptr != ptr)
            {
                current = new IndicatorGroup { Ptr = ptr, Indicator = indicator };
                groups.Add(current);
            }

            string text = DisplayText.Of(s.Element);
            if (string.IsNullOrEmpty(text)) continue;
            bool isPosition = backing.name == "PositionText" || backing.hierarchy.parent?.name == "TopFrame";
            if (isPosition && current.Position == null) current.Position = text;
            else if (AncestorNameContains(backing, "HasProspectsVisible")) current.ProspectParts.Add(text);
            else current.PlayerParts.Add(text);
        }

        return groups.Count >= 2 ? groups : null;
    }

    // The Squad Planner's position strip: one row per position, valued by the active analysis tab
    // (Ability / Depth / Role), whose game label heads the value column. Each position cell is a
    // select control — activating it runs the pitch's own click path (HandleClick + the selection
    // publish, see PositionSelectorAdapter), which switches the plan section below to that
    // position. The team-average line after the strip sits outside the indicators and stays
    // plain text.
    private static void ConsolidatePlannerPositionStrip(List<RenderItem> items)
    {
        var groups = CollectIndicatorRun(items, "RecruitmentSquadPlannerPitch", out int first, out int last);
        if (groups == null) return;

        var rows = new List<PitchSummaryRow>(groups.Count);
        foreach (var g in groups)
        {
            if (g.Position == null) return; // shape broke; keep the raw stops
            var parts = new List<string>(g.PlayerParts);
            parts.AddRange(g.ProspectParts);
            var adapter = new Adapters.PositionSelectorAdapter(g.Indicator);
            bool selected = false;
            try { selected = adapter.GetState() == "selected"; }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Planner slot state read failed: {ex.Message}"); }
            rows.Add(new PitchSummaryRow
            {
                Cells = new List<string> { g.Position, string.Join(", ", parts) },
                Select = adapter,
                Selected = selected,
            });
        }

        // The value column is whatever the selected analysis tab shows; reuse its game label.
        string valueHeader = "";
        for (int j = first - 1; j >= 0; j--)
        {
            if (items[j] is not TabBarItem tb) continue;
            foreach (var tab in tb.Tabs)
                if (tab.IsSelected) { try { valueHeader = tab.GetLabel() ?? ""; } catch { } break; }
            break;
        }

        items.RemoveRange(first, last - first + 1);
        items.Insert(first, new PitchSummaryItem
        {
            Headers = new List<string> { PositionHeaderLabel, valueHeader },
            Rows = rows,
        });
    }

    // The Best XI pitch: one row per position with the best player and best prospect (each "name,
    // rating"). The game's own "Best Player" / "Best Prospect" legend texts precede the pitch as
    // two sibling text stops; they become the column headers and their stray stops fold away.
    private static void ConsolidateBestXIPitch(List<RenderItem> items)
    {
        var groups = CollectIndicatorRun(items, "BestXICardBody", out int first, out int last);
        if (groups == null) return;

        var rows = new List<PitchSummaryRow>(groups.Count);
        foreach (var g in groups)
        {
            if (g.Position == null) return; // shape broke; keep the raw stops
            rows.Add(new PitchSummaryRow
            {
                Cells = new List<string>
                {
                    g.Position,
                    string.Join(", ", g.PlayerParts),
                    string.Join(", ", g.ProspectParts),
                },
            });
        }

        string playerHeader = BestPlayerHeaderFallback, prospectHeader = BestProspectHeaderFallback;
        int consumed = 0;
        if (first >= 2 && TryReadSiblingTextPair(items, first - 2, out var h1, out var h2))
        {
            playerHeader = h1;
            prospectHeader = h2;
            consumed = 2;
        }

        items.RemoveRange(first - consumed, last - (first - consumed) + 1);
        items.Insert(first - consumed, new PitchSummaryItem
        {
            Headers = new List<string> { PositionHeaderLabel, playerHeader, prospectHeader },
            Rows = rows,
        });
    }

    // Everything above builds the card's sections and columns around "Player 1" and "Player 2",
    // because the halves are identified by nothing except which picker they belong to. Those are
    // placeholders: the pickers themselves hold the names, so once the page is built, swap them in.
    // "Esteban Mouton | Clément Bassin" over a column of numbers says what "Player 1 | Player 2"
    // needs a second lookup to answer.
    //
    // Done as a final substitution rather than threaded through every builder because the table
    // columns come from static spec declarations that exist before any player is chosen. If a name
    // cannot be read the placeholders simply stay — they are already correct, just less useful, so
    // an unreadable name costs a nicety rather than the section's identity.
    private static void NameComparisonPlayers(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != ComparisonPanelName) return;
        if (panelObj is not VisualElement panel) return;
        try
        {
            if (!TryReadComparisonPlayerNames(panel, out string one, out string two)) return;

            foreach (var item in items)
            {
                switch (item)
                {
                    case HeadingItem h:
                        h.Text = NamedOrSame(h.Text, one, two);
                        break;
                    case SyntheticTableItem s when s.Headers != null:
                        s.Headers = NamedHeaders(s.Headers, one, two);
                        break;
                    case TableItem t when t.Headers != null:
                        for (int i = 0; i < t.Headers.Count; i++)
                        {
                            var header = t.Headers[i];
                            string named = NamedOrSame(header.Text, one, two);
                            if (ReferenceEquals(named, header.Text)) continue;
                            header.Text = named;
                            t.Headers[i] = header;
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Comparison player naming failed, positional labels kept: {ex.Message}");
        }
    }

    // A fresh array whenever a name lands: a spec's Headers array is the STATIC declaration shared
    // by every render, so writing a player's name into it in place would brand that spec with this
    // save's players for the rest of the session.
    private static string[] NamedHeaders(string[] headers, string one, string two)
    {
        string[] named = null;
        for (int i = 0; i < headers.Length; i++)
        {
            string value = NamedOrSame(headers[i], one, two);
            if (ReferenceEquals(value, headers[i])) continue;
            named ??= (string[])headers.Clone();
            named[i] = value;
        }
        return named ?? headers;
    }

    // Returns the SAME reference when nothing changes, so callers can tell a substitution happened
    // without comparing strings twice.
    private static string NamedOrSame(string text, string one, string two)
    {
        if (string.Equals(text, ComparisonPlayerOneHeading, StringComparison.Ordinal)) return one;
        if (string.Equals(text, ComparisonPlayerTwoHeading, StringComparison.Ordinal)) return two;
        return text;
    }

    // Both players' names, read from the pickers that name them. False unless BOTH read: naming one
    // column and numbering the other would be worse than numbering both.
    private static bool TryReadComparisonPlayerNames(VisualElement panel, out string one, out string two)
    {
        one = null;
        two = null;
        var header = FindDescendantNamed(panel, ComparisonHeaderFrame);
        if (header == null) return false;
        var left = FindDescendantNamed(header, ComparisonHeaderLeft);
        var right = FindDescendantNamed(header, ComparisonHeaderRight);
        if (left == null || right == null) return false;
        one = FindPickerName(left);
        two = FindPickerName(right);
        return one != null && two != null;
    }

    /// <summary>
    /// A half's player name. The game draws it two different ways: as a picker's label when that
    /// slot can be swapped for another player, and as plain text when it cannot — which is the case
    /// for anyone outside your club, so opening the comparison from a scouted player's report gave
    /// the left half no picker at all. Both shapes put the name first inside the half's "frame"
    /// block, ahead of the club, nation and age, so the name is taken from there rather than from
    /// the picker. Empty text is skipped: the template keeps an unfilled "no player chosen" picker
    /// beside the real one.
    /// </summary>
    private static string FindPickerName(VisualElement half)
    {
        var frame = FindVisibleDescendantNamed(half, ComparisonNameFrame, 0);
        return FirstVisibleText(frame ?? half, 0);
    }

    private static string FirstVisibleText(VisualElement el, int depth)
    {
        if (el == null || depth > 14 || !ElementBridge.IsVisible(el)) return null;
        string type;
        try { type = el.GetIl2CppType().Name; } catch { return null; }
        if (type == "SIText")
        {
            var raw = ReadFirstText(el);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var cleaned = TextFilters.CleanForDisplay(ServerText.DecodeInlineEntityLinks(raw));
                if (!string.IsNullOrWhiteSpace(cleaned)) return cleaned;
            }
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = FirstVisibleText(el.hierarchy[i], depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private const string ComparisonHeaderFrame = "Header frame";
    private const string ComparisonHeaderLeft = "Left";
    private const string ComparisonHeaderRight = "Right";
    // The block each half puts its name, club, nation and age in, name first.
    private const string ComparisonNameFrame = "frame";

    // The Overview tab draws each player his own Season Stats block, so the card's central question
    // — how do these two compare — took two tables and a memory to answer: eight stats read for one
    // player, eight more read for the other, and the reader holding the first set in their head
    // while hearing the second. Fold them into the one table the layout is really making, stats down
    // the side and a column per player, matching every other comparison block on the card.
    //
    // The two halves come from the same template, so their stat rows are the same list in the same
    // order; a pair that does NOT line up is not the shape this merges and both tables are left
    // exactly as they were, rather than pairing one player's figure with another stat's name. The
    // merged table takes the game's own "Season Stats" title from the first block, and sits at the
    // second block's place — after both halves, before the other shared sections — headed a level up
    // so it belongs to neither player.
    private static void MergeComparisonSeasonStats(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != ComparisonPanelName) return;
        try
        {
            var found = new List<int>();
            for (int i = 0; i < items.Count; i++)
                if (items[i] is SyntheticTableItem t && HasHeaders(t, SyntheticTables.ComparisonSeasonStatsHeaders))
                    found.Add(i);
            if (found.Count != 2) return;

            int i1 = found[0], i2 = found[1];
            var rows = PairSeasonStatRows((SyntheticTableItem)items[i1], (SyntheticTableItem)items[i2]);
            if (rows == null) return;

            // Each block is its own game-titled heading plus the table; take the title from the
            // first, and swallow both headings with their tables.
            int start1 = i1 > 0 && items[i1 - 1] is HeadingItem ? i1 - 1 : i1;
            int start2 = i2 > 0 && items[i2 - 1] is HeadingItem ? i2 - 1 : i2;
            string title = items[start1] is HeadingItem h ? h.Text : null;
            if (title == null) return;

            int count1 = i1 - start1 + 1, count2 = i2 - start2 + 1;
            items.RemoveRange(start2, count2);
            items.RemoveRange(start1, count1);

            int at = start2 - count1;
            items.Insert(at, new SyntheticTableItem
            {
                Headers = new[] { SyntheticTables.ComparisonSeasonStatsHeaders[0], ComparisonPlayerOneHeading, ComparisonPlayerTwoHeading },
                Rows = rows,
            });
            items.Insert(at, new HeadingItem { Text = title, Level = 3 });
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Season stats merge failed, per-player tables kept: {ex.Message}");
        }
    }

    // The Overview's Attributes section arrives as two tables because the game draws its two halves
    // differently: the bio strip (preferred foot, height) puts a player per ROW, the radar's stats
    // put a player per COLUMN. Under one heading that reads as two unrelated grids the player has to
    // reconcile. Transpose the bio strip and fold it in, so the section is the single table it
    // always was — one row per attribute, one column per player.
    //
    // Both shapes must be present and the bio strip must hold exactly the two players, otherwise
    // nothing is touched: a transpose over the wrong number of rows would file one player's foot
    // under the other's name.
    private static void MergeComparisonAttributes(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != ComparisonPanelName) return;
        try
        {
            int bioAt = -1, statsAt = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is not SyntheticTableItem t) continue;
                if (HasHeaders(t, SyntheticTables.ComparisonOverviewBioHeaders))
                {
                    if (bioAt >= 0) return;   // more than one of either shape is not this section
                    bioAt = i;
                }
                else if (HasHeaders(t, SyntheticTables.ComparisonAttributeHeaders))
                {
                    if (statsAt >= 0) return;
                    statsAt = i;
                }
            }
            if (bioAt < 0 || statsAt < 0) return;

            var bio = (SyntheticTableItem)items[bioAt];
            var stats = (SyntheticTableItem)items[statsAt];
            var rows = TransposePlayerRows(bio);
            if (rows == null) return;

            // Bio first: the game draws it above the radar, and foot and height read as the
            // introduction to the numbers rather than a footnote after them.
            rows.AddRange(stats.Rows);
            stats.Rows = rows;
            items.RemoveAt(bioAt);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Attribute table merge failed, both tables kept: {ex.Message}");
        }
    }

    // A player-per-row table turned into attribute-per-row: each of its value COLUMNS becomes a row
    // headed by that column's name. Null unless it holds exactly the card's two players.
    private static List<List<string>> TransposePlayerRows(SyntheticTableItem table)
    {
        if (table.Rows == null || table.Rows.Count != 2 || table.Headers == null) return null;
        var first = table.Rows[0];
        var second = table.Rows[1];
        var rows = new List<List<string>>(table.Headers.Length - 1);
        for (int c = 1; c < table.Headers.Length; c++)
        {
            if (c >= first.Count || c >= second.Count) return null;
            rows.Add(new List<string> { table.Headers[c], first[c], second[c] });
        }
        return rows.Count > 0 ? rows : null;
    }

    // The two blocks' rows as stat/player-one/player-two, or null when they are not the same stats
    // in the same order.
    private static List<List<string>> PairSeasonStatRows(SyntheticTableItem left, SyntheticTableItem right)
    {
        if (left.Rows == null || right.Rows == null || left.Rows.Count == 0) return null;
        if (left.Rows.Count != right.Rows.Count) return null;

        var rows = new List<List<string>>(left.Rows.Count);
        for (int i = 0; i < left.Rows.Count; i++)
        {
            var l = left.Rows[i];
            var r = right.Rows[i];
            if (l.Count < 2 || r.Count < 2) return null;
            if (!string.Equals(l[0], r[0], StringComparison.Ordinal)) return null;
            rows.Add(new List<string> { l[0], l[1], r[1] });
        }
        return rows;
    }

    private static bool HasHeaders(SyntheticTableItem t, string[] headers)
    {
        if (t.Headers == null || t.Headers.Length != headers.Length) return false;
        for (int i = 0; i < headers.Length; i++)
            if (!string.Equals(t.Headers[i], headers[i], StringComparison.Ordinal)) return false;
        return true;
    }

    // The Player Comparison card's Overview tab draws each player a pitch whose dots are coloured by
    // how well he plays each position — the whole point of the tab, and worth nothing on this
    // surface, where the slots carry no text at all and the two pitches render as empty space.
    //
    // Give the comparison the table the layout is really making: one row per position either player
    // can actually play, both players' familiarity beside it. Positions neither can play are left
    // out — eleven rows of "1, 1" would bury the four that matter. The table is a SHARED section
    // like Attributes, appended after both halves rather than inside one of them, because it is
    // about both players.
    //
    // The pitch is also a control, but deliberately NOT exposed as one here: selecting a slot on the
    // Overview pitch moves the highlight and updates that half's "Sel:" line and nothing else — the
    // "Best Role As" line beside it reports the player's best position and does not follow the
    // selection (checked live against D (C), D (L), DM and WB (L)). A picker whose only effect is to
    // read your own choice back to you is noise, so position picking is left to the Positions &
    // Roles tab, where it really does drive the familiarity, the ranked roles and the footedness.
    private static void AddComparisonSuitability(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != ComparisonPanelName) return;
        if (panelObj is not VisualElement panel) return;
        try
        {
            var pitches = PositionSuitability.FindPitches(panel);
            if (pitches.Count != 2) return;   // not the two-player Overview

            var left = PositionSuitability.Read(pitches[0]);
            var right = PositionSuitability.Read(pitches[1]);
            if (left.Count == 0 || right.Count != left.Count) return;

            AppendSuitabilityTable(items, left, right);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Position suitability build failed, section omitted: {ex.Message}");
        }
    }

    private static void AppendSuitabilityTable(
        List<RenderItem> items, List<PositionSuitability.Slot> left, List<PositionSuitability.Slot> right)
    {
        var rows = new List<List<string>>();
        for (int i = 0; i < left.Count; i++)
        {
            // Same pitch template both sides, so slot i is the same position on both. A mismatch
            // means the two halves are not the shape this pairs up, and pairing them anyway would
            // put one player's figure against another position's name.
            if (!string.Equals(left[i].SlotName, right[i].SlotName, StringComparison.Ordinal)) return;
            if (left[i].Familiarity <= SuitabilityFloor && right[i].Familiarity <= SuitabilityFloor) continue;
            rows.Add(new List<string>
            {
                left[i].Position,
                left[i].Familiarity.ToString(),
                right[i].Familiarity.ToString(),
            });
        }
        if (rows.Count == 0) return;

        items.Add(new HeadingItem { Text = SuitabilityHeading, Level = 3 });
        items.Add(new SyntheticTableItem
        {
            Headers = new[] { PositionHeaderLabel, ComparisonPlayerOneHeading, ComparisonPlayerTwoHeading },
            Rows = rows,
        });
    }

    private const string SuitabilityHeading = "Position suitability";
    // A player cannot play a position he reads the bottom of the scale at.
    private const int SuitabilityFloor = 1;

    // Individual Training: the screen where a player's IP role, OOP role, training focus and
    // training intensity are set. The game lays its card out around a pitch graphic, and two of
    // that layout's decisions do not survive being linearised:
    //
    //  - The pitch paints a dot per position, coloured by how well the player plays there, with a
    //    six-word colour key beside it. The dots carry no text, so this surface got the KEY and
    //    none of the data — six bare adjectives ("Natural", "Accomplished", ...) sitting in the
    //    reading order exactly where a description of the player would, which is how they read.
    //    Replace the key in place with the familiarity the dots are painted from.
    //  - The four controls the screen exists for are drawn as a plain column with no title of
    //    their own, roughly a hundred and forty stops into the panel. Give them a landmark.
    //
    // Each step is gated on the shape it was written for and skips whole otherwise: the key is
    // removed ONLY when the table that replaces it is emitted, so a pitch this cannot read leaves
    // the panel exactly as it is today rather than stripping the one clue it had.
    private const string IndividualTrainingPanel = "TeamIndividualTrainingTool";
    private const string IndividualTrainingOptionsBlock = "PlayerIndividualTrainingOptions";
    private const string IndividualTrainingDropdownsFrame = "Dropdowns";
    private const string IndividualTrainingControlsHeading = "Roles and training";
    private const string FamiliarityHeaderLabel = "Familiarity";

    // The suitability key's per-band frames, in the order the game stacks them. Names, not text:
    // the words are localized, the frame names are not.
    private static readonly string[] SuitabilityKeyBands =
    {
        "Natural", "Accomplished", "Competent", "Unconvincing", "Awkward", "Makeshift",
    };

    private static void RestructureIndividualTraining(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != IndividualTrainingPanel) return;
        if (panelObj is not VisualElement panel) return;
        try
        {
            ReplaceSuitabilityKeyWithTable(items, panel);
            InsertTrainingControlsHeading(items);
            DropRadarLegendNote(items);
            GatherFilterFlyout(items);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Individual Training restructure failed, raw stops kept: {ex.Message}");
        }
    }

    private static void ReplaceSuitabilityKeyWithTable(List<RenderItem> items, VisualElement panel)
    {
        var pitches = PositionSuitability.FindPitches(panel);
        if (pitches.Count != 1) return;   // not the single-player card this reads

        var slots = PositionSuitability.Read(pitches[0]);
        if (slots.Count == 0) return;     // Read already noted why; keep today's stops

        var rows = new List<List<string>>();
        foreach (var slot in slots)
        {
            if (slot.Familiarity <= SuitabilityFloor) continue;
            rows.Add(new List<string> { slot.Position, slot.Familiarity.ToString() });
        }
        if (rows.Count == 0) return;      // a player who can play nowhere is not a shape to assert

        int first = -1, last = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (!IsSuitabilityKeyStop(ItemElement(items[i])))
            {
                if (first >= 0) break;    // the key is one contiguous run
                continue;
            }
            if (first < 0) first = i;
            last = i;
        }
        if (first < 0) return;            // no key drawn: nothing to replace, nothing to add

        items.RemoveRange(first, last - first + 1);
        items.Insert(first, new SyntheticTableItem
        {
            Headers = new[] { PositionHeaderLabel, FamiliarityHeaderLabel },
            Rows = rows,
        });
        items.Insert(first, new HeadingItem { Text = SuitabilityHeading, Level = 3 });
    }

    // A colour-key band label on the training card's pitch. Scoped to the options block so a band
    // name reused elsewhere on the panel cannot pull an unrelated stop into the run.
    private static bool IsSuitabilityKeyStop(VisualElement backing)
    {
        if (backing == null) return false;
        // FM27 moves the same pitch/key into the shared PlayerHeader.
        if (FindAncestorNamed(backing, IndividualTrainingOptionsBlock) == null
            && FindAncestorNamed(backing, "PlayerHeader") == null) return false;
        foreach (var band in SuitabilityKeyBands)
            if (FindAncestorNamed(backing, band) != null) return true;
        return false;
    }

    // The squad table's Filter control is a flyout, not a modal: the game floats its panel over the
    // table, so in tree order the twenty-six checkboxes land AFTER the whole table — twenty rows
    // past the button that opened them, with nothing marking where they start and nothing saying
    // the button is what closes them again. Opening the filter therefore looked like nothing
    // happening, and the way back out was invisible.
    //
    // Put the flyout where its button is: the Filter button carries its open state, and its content
    // follows immediately under a landmark, so the reader hears "Filter, button, expanded", steps
    // into the groups, and presses the same button to collapse it. The group titles the game draws
    // become headings so the four sections are reachable directly.
    //
    // Anchored on the flyout list's own name and the two named branches of the button's visibility
    // switch; a panel drawing no filter (or a filter that is closed) simply finds nothing to move.
    private const string FilterFlyoutList = "filterobjectlist";
    private const string FilterButtonFrame = "Filter";
    private const string FilterOpenBranch = "HideFilters";
    private const string FilterHeadingLabel = "Filter";

    private static void GatherFilterFlyout(List<RenderItem> items)
    {
        int button = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not StopItem s) continue;
            var backing = ItemElement(items[i]);
            if (backing == null || FindAncestorNamed(backing, FilterButtonFrame) == null) continue;
            if (ServerText.SafeRole(s.Element) != "button") continue;
            button = i;
            break;
        }
        if (button < 0) return;

        bool expanded = FindAncestorNamed(ItemElement(items[button]), FilterOpenBranch) != null;

        var flyout = new List<RenderItem>();
        for (int i = items.Count - 1; i > button; i--)
        {
            var backing = ItemElement(items[i]);
            if (backing == null || FindAncestorNamed(backing, FilterFlyoutList) == null) continue;
            if (items[i] is StopItem s && IsFilterGroupTitle(backing))
                items[i] = new HeadingItem { Text = DisplayText.Of(s.Element), Level = 4 };
            flyout.Insert(0, items[i]);
            items.RemoveAt(i);
        }

        // Expose the toggle whether or not it is open: "collapsed" is what tells a reader who found
        // no filter content that there is some behind this button.
        items[button] = new StatsToggleItem { El = ((StopItem)items[button]).Element, Expanded = expanded };
        if (flyout.Count == 0) return;

        items.InsertRange(button + 1, flyout);
        items.Insert(button + 1, new HeadingItem { Text = FilterHeadingLabel, Level = 3 });
    }

    // A section title inside the filter flyout rather than one of its checkbox labels. Both are
    // plain text under the same template path; what separates them is nesting — the flyout is a
    // streamed list of groups, and each group's OWN items live in a second streamed list inside it,
    // so a title has exactly one such ancestor and a label has two.
    private static bool IsFilterGroupTitle(VisualElement backing)
    {
        int lists = 0;
        for (var p = backing; p != null; p = p.hierarchy.parent)
        {
            string type;
            try { type = p.GetIl2CppType().Name; }
            catch { return false; }
            if (type == "StreamedObjectList") lists++;
            if (p.name == FilterFlyoutList) break;
        }
        return lists == 1;
    }

    // This card reuses the attributes template, so it draws the "attributes key" container that the
    // radar note is keyed on — but it plots no radar chart, and its attributes reach the page as
    // full tables. The note there would tell the player that values below are missing when they are
    // all present, and would name a legend whose words this panel's profile hides (Preferable and
    // Focus mark attributes by colour alone, so the key names two sets nothing can reveal).
    private static void DropRadarLegendNote(List<RenderItem> items)
    {
        for (int i = items.Count - 1; i >= 0; i--)
            if (items[i] is NoteItem n && n.Text == RadarLegendNote) items.RemoveAt(i);
    }

    private static void InsertTrainingControlsHeading(List<RenderItem> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var backing = ItemElement(items[i]);
            if (backing == null) continue;
            bool legacy = FindAncestorNamed(backing, IndividualTrainingOptionsBlock) != null
                && FindAncestorNamed(backing, IndividualTrainingDropdownsFrame) != null;
            bool header = FindAncestorNamed(backing, "PlayerHeader") != null
                && (FindAncestorNamed(backing, "trainingoptions") != null
                    || FindAncestorNamed(backing, "positions") != null);
            if (!legacy && !header) continue;
            items.Insert(i, new HeadingItem { Text = IndividualTrainingControlsHeading, Level = 3 });
            return;
        }
    }

    // The Player Comparison card's two kinds of game table, both of which the game lays out for a
    // side-by-side read that does not survive being linearised.
    //
    // The Attributes tab draws one table per attribute group with FOUR columns: the group name
    // over the attribute names, then player one's figures, a spacer the layout uses to part the
    // two halves, and player two's. The game writes no header over any of the three value columns
    // — each is a bare "-" — so the table arrived as "Technical | - | - | -" with an empty column
    // down the middle and no way to tell whose number was whose. Name the value columns for the
    // players they hold, lift the group name out into the heading the tab otherwise has none of,
    // and drop the spacer from the display order.
    //
    // The Positions & Roles tab's best-role table draws the star rating BEFORE the role it belongs
    // to, so every row reads "3 stars, Centre-Back". Flip the display order so the role leads, and
    // promote the game's own "When Playing As An D (C)" line above it into that table's heading.
    //
    // Every step is gated on the exact shape it was written for and skips otherwise: a value header
    // the game DID name is never overwritten, and the spacer is dropped only when it is empty in
    // every row of the page.
    private static void RestructureComparisonTables(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != ComparisonPanelName) return;
        try
        {
            // The tab draws each attribute group TWICE: once for the group's attributes, then again
            // further down as a one-row grid holding that group's average. So the second run of four
            // is really one four-row averages table the layout has split apart, and it arrived as
            // four separate one-row tables each repeating the same three column headers. Head that
            // run once and fold it back into a single table (the fold itself has to wait for the
            // streamed rows — see RestructureLoadedTables). The repeat's own row already says
            // "Technical Average", so it needs no per-group heading of its own.
            var headed = new HashSet<string>(StringComparer.Ordinal);
            bool averagesStarted = false;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is not TableItem t || t.Headers == null) continue;
                if (t.Headers.Count == ComparisonAttributeColumns)
                    i += RestructureAttributeTable(items, i, t, headed, ref averagesStarted);
                else if (t.Headers.Count == 2) i += RestructureBestRoleTable(items, i, t);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Comparison table restructure failed, game layout kept: {ex.Message}");
        }
    }

    // The attribute group's four columns. Returns how many items were inserted before the table.
    private static int RestructureAttributeTable(
        List<RenderItem> items, int index, TableItem t, HashSet<string> headed, ref bool averagesStarted)
    {
        // Only the unnamed-value shape: if the game ever names these columns, its words win.
        for (int c = 1; c < ComparisonAttributeColumns; c++)
            if (!IsUnnamedHeader(t.Headers[c].Text)) return 0;

        string group = (t.Headers[0].Text ?? "").Trim();
        if (group.Length == 0) return 0;

        SetHeaderText(t, 0, AttributeColumnLabel);
        SetHeaderText(t, ComparisonPlayerOneColumn, ComparisonPlayerOneHeading);
        SetHeaderText(t, ComparisonPlayerTwoColumn, ComparisonPlayerTwoHeading);
        t.GutterColumn = ComparisonSpacerColumn;

        if (headed.Add(group))
        {
            items.Insert(index, new HeadingItem { Text = group, Level = 3 });
            return 1;
        }

        // A group seen twice: this is one of the averages. The first opens the section and takes the
        // heading the game never draws for it; the rest fold into it.
        if (averagesStarted)
        {
            t.FoldsIntoPreceding = true;
            return 0;
        }
        averagesStarted = true;
        items.Insert(index, new HeadingItem { Text = AveragesHeading, Level = 3 });
        return 1;
    }

    /// <summary>
    /// Consolidations that cannot run during the walk because they need the rows themselves, which
    /// a streamed table only has after <see cref="StreamedTableLoader"/> has read them. Main thread,
    /// after the load, before the render.
    /// </summary>
    internal static void RestructureLoadedTables(List<RenderItem> items, object panelObj)
    {
        try
        {
            MergeComparisonPositionRoles(items, panelObj);
            // Back to front, so each folded table lands in its neighbour before that neighbour is
            // itself folded onwards — a run of them collapses into the first.
            for (int i = items.Count - 1; i > 0; i--)
            {
                if (items[i] is not TableItem t || !t.FoldsIntoPreceding) continue;
                if (items[i - 1] is not TableItem into) continue;
                if (t.Rows == null || into.Rows == null) continue;
                if (!SameColumns(into, t)) continue;
                into.Rows.AddRange(t.Rows);
                items.RemoveAt(i);
            }

            MarkComparisonAverages(items, panelObj);

            // Anything built here was built after the walk's naming pass, so its columns still carry
            // the placeholders. Naming is idempotent — an already-named column matches nothing.
            NameComparisonPlayers(items, panelObj);

            // Needs the loaded rows for the same reason: the chart is walked before the tables that
            // say which of its lines is which, and those rows are virtualised until now.
            NameChartSeriesFromTickedRows(items);

            // On the progress report the selection has already been reduced to one attribute, so
            // its tick boxes are single-choice and render as radios.
            if (Server.Features.SingleAttributeChart.AppliesTo(panelObj))
                Server.Features.SingleAttributeChart.MarkRadios(items);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Loaded-table fold failed, split tables kept: {ex.Message}");
        }
    }

    // Names the columns of a chart whose lines were put there by tick boxes — the progress report's
    // attribute graph, where every ticked attribute draws a line. The lines carry no names of their
    // own (see ChartSeriesNaming), so they are matched to the ticked rows by position and that match
    // is only accepted when every line's final value agrees with its row's current value.
    //
    // Runs after the streamed load because the rows are virtualised: the ticked set does not exist
    // in the tree at walk time, and the chart is walked before the tables anyway.
    //
    // The three outcomes are all honest. Matched: the columns take the attribute names. Unmatched
    // with one line: it stays a plain Date/Value table, which is what the ability graph and the
    // finance histories are and must remain. Unmatched with several: nothing can be labelled, so
    // the chart becomes a gap marker rather than a table of anonymous columns.
    private static void NameChartSeriesFromTickedRows(List<RenderItem> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not ValueChartItem chart || chart.Series == null || chart.Series.Count == 0)
                continue;
            if (chart.ValueHeaders != null) continue;

            // Only the tables below this chart: on a tabbed card an earlier chart's rows are not
            // this one's, and a table above it never selected its lines.
            var ticked = CollectTickedRows(items, i + 1);

            var plotted = new List<IReadOnlyList<string>>(chart.Series.Count);
            for (int s = 0; s < chart.Series.Count; s++)
            {
                var values = new List<string>(chart.Series[s].Count);
                foreach (var p in chart.Series[s]) values.Add(p.Value);
                plotted.Add(values);
            }

            var match = Navigation.ChartSeriesNaming.TryMatch(plotted, ticked);
            if (match.Names != null)
            {
                ApplyChartColumns(chart, match, ticked);
                continue;
            }

            // One unnamed line still reads honestly as a plain Date/Value table — which is exactly
            // what the ability graph and the finance histories are. Several cannot.
            if (chart.Series.Count <= 1) continue;

            Degrade.Note($"Chart plots {chart.Series.Count} lines against {ticked.Count} ticked rows: "
                       + $"{match.Outcome}. Showing a marker rather than anonymous columns.");
            items[i] = new NoteItem { Text = Degrade.Unreadable + " chart" };
        }
    }

    // The ticked selection rows, in render order, as name/value pairs. A row contributes only when
    // it holds a checked toggle AND a later cell that reads as its value — the shape of an attribute
    // row. Rows of any other shape are skipped, so an unrelated table with tick boxes contributes
    // nothing and the count check below declines rather than mislabelling.
    private static List<Navigation.ChartSeriesNaming.Candidate> CollectTickedRows(List<RenderItem> items, int from)
    {
        var ticked = new List<Navigation.ChartSeriesNaming.Candidate>();
        for (int i = from; i < items.Count; i++)
        {
            if (items[i] is not TableItem t || t.Rows == null) continue;
            foreach (var row in t.Rows)
            {
                if (row.Cells == null) continue;

                bool isTicked = false;
                string name = null, value = null;
                foreach (var cell in row.Cells)
                {
                    if (cell.Elements == null) continue;
                    foreach (var e in cell.Elements)
                    {
                        if (e.IsToggle)
                        {
                            if (!e.IsChecked) { isTicked = false; goto nextRow; }
                            isTicked = true;
                        }
                        else if (isTicked && !string.IsNullOrWhiteSpace(e.Text))
                        {
                            // First text after the tick is the attribute, the next is its rating.
                            if (name == null) name = e.Text;
                            else if (value == null) value = e.Text;
                        }
                    }
                }
                if (isTicked && name != null && value != null)
                    ticked.Add(new Navigation.ChartSeriesNaming.Candidate(name, value));
                nextRow: ;
            }
        }
        return ticked;
    }

    // Lays the named lines out as columns: table order, capped at MaxColumns, with anything left
    // over named rather than dropped in silence — a table that quietly stops short would read as
    // "these are the attributes you ticked" when it is only the first ten of them.
    //
    // Columns follow the table the player just read, not the chart's own series order, which is the
    // game's internal attribute enumeration and matches nothing the player has seen.
    private static void ApplyChartColumns(
        ValueChartItem chart, Navigation.ChartSeriesNaming.Match match,
        List<Navigation.ChartSeriesNaming.Candidate> ticked)
    {
        var names = match.Names;

        // Ambiguous lines carry no name, so they cannot become columns — a column headed by a
        // coin flip is exactly the lie this whole join exists to avoid. They are named in the
        // note instead, which tells the player which tick to clear to resolve them.
        var order = new List<int>(names.Length);
        for (int s = 0; s < names.Length; s++) if (names[s] != null) order.Add(s);
        order.Sort((a, b) => TickedIndex(ticked, names[a]).CompareTo(TickedIndex(ticked, names[b])));

        var series = new List<List<FinanceChartReader.Point>>(order.Count);
        var headers = new List<string>(order.Count);
        for (int i = 0; i < order.Count; i++)
        {
            if (i >= ValueChartItem.MaxColumns)
            {
                (chart.OmittedColumns ??= new List<string>()).Add(names[order[i]]);
                continue;
            }
            series.Add(chart.Series[order[i]]);
            headers.Add(names[order[i]]);
        }

        chart.Series = series;
        chart.ValueHeaders = headers.ToArray();
        if (match.Ambiguous.Count > 0)
            chart.AmbiguousColumns = new List<string>(match.Ambiguous);
    }

    private static int TickedIndex(List<Navigation.ChartSeriesNaming.Candidate> ticked, string name)
    {
        for (int i = 0; i < ticked.Count; i++)
            if (string.Equals(ticked[i].Name?.Trim(), name, System.StringComparison.Ordinal)) return i;
        return int.MaxValue;
    }

    // Positions & Roles asks one question — at this position, what are these two players worth? —
    // and answered it in two halves you had to hold side by side in your head: each player's
    // suitability word, then his own ranked role table, each sorted by his own ratings so the two
    // lists did not even run in the same order. Fold the pair into one table: suitability first,
    // then every role at that position with both players' star ratings beside it.
    //
    // Rows follow the FIRST player's ranking, which is the game's own ordering of the roles that
    // suit him; the second player's table is matched by role NAME, never by position, and any role
    // only his list carries is appended rather than dropped. The suitability row is included only
    // when BOTH words read — one player's alone would be a comparison with a hole in it.
    //
    // Needs the loaded rows, so it runs here rather than in the walk. Anything unexpected — one
    // table instead of two, a half that is not on this tab, a row without cells — leaves the tab
    // exactly as the game laid it out.
    private static void MergeComparisonPositionRoles(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != ComparisonPanelName) return;
        if (panelObj is not VisualElement panel) return;

        var left = FindVisibleHalf(panel, ComparisonLeftHalfPositions, ComparisonPositionsTab);
        var right = FindVisibleHalf(panel, ComparisonRightHalfPositions, ComparisonPositionsTab);
        if (left == null || right == null) return;

        int leftFrom = FindHalfRange(items, left, out int leftTo);
        int rightFrom = FindHalfRange(items, right, out int rightTo);
        if (leftFrom < 0 || rightFrom < 0 || rightFrom <= leftTo) return;

        // The half's own first line is the position it is reporting on ("Defender (Center)"), which
        // is the game's name for what the whole section now answers.
        string title = FirstTextIn(items, leftFrom, leftTo);
        if (title == null) return;

        var rows = new List<List<string>>();
        string leftFit = FindSuitabilityWord(left);
        string rightFit = FindSuitabilityWord(right);
        // Both or neither: one player's suitability against a blank is a comparison with a hole.
        if (leftFit != null && rightFit != null)
            rows.Add(new List<string> { PositionSuitabilityRowLabel, leftFit, rightFit });

        var leftTable = FindRoleTable(items, leftFrom, leftTo);
        var rightTable = FindRoleTable(items, rightFrom, rightTo);
        var roleRows = PairRoleRows(leftTable, rightTable);
        if (roleRows == null) return;
        rows.AddRange(roleRows);
        if (rows.Count == 0) return;

        // Neither player has a role here: keep the game's own words for that rather than leaving a
        // table that simply stops after the suitability row.
        string noRoles = roleRows.Count == 0 ? FindTextUnder(left, ComparisonNoRolesContainer, 0) : null;

        string roleHeader = leftTable != null ? leftTable.Headers[BestRoleNameColumn].Text
            : rightTable != null ? rightTable.Headers[BestRoleNameColumn].Text : "";
        var headers = new[] { roleHeader ?? "", ComparisonPlayerOneHeading, ComparisonPlayerTwoHeading };

        items.RemoveRange(rightFrom, rightTo - rightFrom + 1);
        items.RemoveRange(leftFrom, leftTo - leftFrom + 1);

        // After the picker, which is all that is left between the two removed halves.
        int at = items.FindIndex(i => i is RadioGroupItem);
        at = at < 0 ? items.Count : at + 1;
        items.Insert(at++, new HeadingItem { Text = title, Level = 3 });
        items.Insert(at++, new SyntheticTableItem { Headers = headers, Rows = rows });
        if (noRoles != null) items.Insert(at, new NoteItem { Text = noRoles });
    }

    // The half is found by its own name and then checked to be on this tab, rather than by
    // descending from the tab: "PositionsRoles" names the tab's BUTTON as well as its content, and
    // the button comes first in the tree.
    // The Report tab is the two scouts' write-ups, and between them the game puts a third column: a
    // pitch marking each position with a dot per player, a "Player / Best Role / Rating" table for
    // whichever position is picked, and a note when neither player suits it.
    //
    // The table and the note are duplicates here. Positions & Roles already lists EVERY role at the
    // chosen position with both players' ratings, of which "the best one" is the top of; and the
    // pitch's dots are the per-position comparison the Overview now states outright as a table.
    // Both go.
    //
    // The PICKER stays, however much it looks like a second copy of the one on Positions & Roles.
    // This tab has its own pitch, and the game hides BOTH scout reports — dates, abilities, pros,
    // cons, the lot — until something is selected on it (verified live: clearing that pitch empties
    // the tab, and selecting on the Positions & Roles pitch does not refill it). Removing the picker
    // makes the reports unreachable from this surface the first time the selection clears.
    //
    // The centre column is, by definition, whatever sits between the two halves — no need to name a
    // container the template names generically.
    private static void StripComparisonReportPitch(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != ComparisonPanelName) return;
        if (panelObj is not VisualElement panel) return;
        try
        {
            var left = FindVisibleHalf(panel, ComparisonLeftHalfPositions, ComparisonReportTab);
            var right = FindVisibleHalf(panel, ComparisonRightHalfPositions, ComparisonReportTab);
            if (left == null || right == null) return;

            // Anchored on the centre column's own elements rather than on "whatever items sit
            // between the halves": this tab has its own pitch, so until a position is picked on IT
            // both halves are empty and contribute no items to sit between.
            var centre = SiblingsBetween(left, right);
            if (centre.Count == 0) return;

            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i] is RadioGroupItem) continue;   // the picker the reports depend on
                var el = ItemElement(items[i]);
                if (el != null && IsUnderAny(el, centre)) items.RemoveAt(i);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Report-tab pitch strip failed, column kept: {ex.Message}");
        }
    }

    // The siblings the template puts between two elements — the layout's middle column, without
    // needing a name for a container the template names generically. Empty unless both share a
    // parent and something really does sit between them.
    private static List<VisualElement> SiblingsBetween(VisualElement a, VisualElement b)
    {
        var result = new List<VisualElement>();
        var parent = a.hierarchy.parent;
        var other = b.hierarchy.parent;
        if (parent == null || other == null || parent.Pointer != other.Pointer) return result;

        int ia = -1, ib = -1;
        int n = parent.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var ptr = parent.hierarchy[i].Pointer;
            if (ptr == a.Pointer) ia = i;
            else if (ptr == b.Pointer) ib = i;
        }
        if (ia < 0 || ib < 0 || ib <= ia + 1) return result;
        for (int i = ia + 1; i < ib; i++) result.Add(parent.hierarchy[i]);
        return result;
    }

    private static bool IsUnderAny(VisualElement el, List<VisualElement> roots)
    {
        foreach (var root in roots)
            if (IsUnder(el, root)) return true;
        return false;
    }

    // ---------- Player report banner ----------

    // The block above the player report's tab strip carrying the player's identity. The game
    // draws nearly every value in it with an icon and no words — a kit graphic behind the squad
    // number, a flag beside the nationality code, a crest beside the club, a price tag before the
    // transfer value, a contract glyph before the wage — so walked raw it reads as a run of bare
    // values ("FRA", "US Orleans", "Impact Sub", "$20K - $140K", "$63K p/a, 6/30/2028") with
    // nothing saying what any of them is.
    private const string PlayerBannerContainer = "report-player-banner-max";

    // Each banner value's anchor. Labels are assigned from the structure a value sits in, never
    // from its position in the run: the banner's cells are conditional (a free agent has no club,
    // an unscouted player no ability stars, most players no squad number, a player with no status
    // pills no status row), and a positional map would silently shift every label down one the
    // moment a cell went missing. A value whose anchor is not in this set gets no label at all.
    private const string PlayerBannerSquadNumber = "data_display-player-report-header-squad number";
    private const string PlayerBannerNameFrame = "Frame 1000003381";
    private const string PlayerBannerPositionCell = "data_display-cell-standard-regular-1";
    private const string PlayerBannerAgeCell = "data_display-cell-standard-regular-2";
    private const string PlayerBannerNationCell = "NationCell_14px--unpacked";
    private const string PlayerBannerBioFrame = "Frame 1000003902";
    private const string PlayerBannerInternationalFrame = "Frame 1000003766";
    private const string PlayerBannerStatusFlags = "StatusFlags";
    private const string PlayerBannerPlayingTeam = "PlayingTeam";
    private const string PlayerBannerPlayingTime = "PlayingTime";
    // The club slot's other branch: a player with no club has neither PlayingTeam nor PlayingTime
    // content, and the game writes "Free Agent" into this container in their place. Reached only
    // after PlayingTeam and PlayingTime fail to match, both of which sit lower on the same path.
    private const string PlayerBannerFreeAgent = "FreeAgentCheck";
    private const string PlayerBannerTransferValueIcon = "game-concepts-transfer-value_small";
    private const string PlayerBannerContract = "ContractDetails";
    // The contract slot's other branch: a player with no contract to show gets a dash here.
    private const string PlayerBannerNoContract = "DashedString";
    // The two states of the banner's last section. CoachReport is the ability read the game shows
    // for a player you know; ScoutReport is the "scout this player" prompt it shows for one you do
    // not. Both draw their own label text, so their rows take the game's words, not the mod's.
    private const string PlayerBannerCoachReport = "CoachReport";
    private const string PlayerBannerScoutReport = "ScoutReport";

    private const string PlayerBannerSquadNumberLabel = "Squad Number";
    private const string PlayerBannerPositionLabel = "Position";
    private const string PlayerBannerNationalityLabel = "Nationality";
    private const string PlayerBannerAgeLabel = "Age";
    private const string PlayerBannerStatusLabel = "Status";
    private const string PlayerBannerClubLabel = "Club";
    private const string PlayerBannerInternationalLabel = "International Team";
    private const string PlayerBannerCapsLabel = "Caps";
    private const string PlayerBannerTransferValueLabel = "Transfer Value";
    private const string PlayerBannerContractLabel = "Contract";
    // The slot beneath the club is the one banner cell whose MEANING changes with what the game
    // knows: for a player you have scouted it is his expected playing time ("Impact Sub"), for one
    // you have not it is why you cannot see that ("Scouting Required"). Naming it "Playing Time"
    // unconditionally would be a lie on every unscouted player, so the label is chosen from which
    // section the banner ends in — the same panel's own structural statement of that knowledge.
    // Neither section present means the mod cannot tell, and the row goes out unlabelled.
    private const string PlayerBannerPlayingTimeLabel = "Playing Time";
    private const string PlayerBannerScoutingLabel = "Scouting";

    // Fewer labelled rows than this and the banner template is not the one these anchors describe.
    // Decline wholesale rather than emit a two-row table beside a run of raw stops.
    private const int PlayerBannerMinLabelledRows = 3;

    // One note per unrecognised anchor name, so a changed banner template stays diagnosable
    // without a warning per value per /body.
    private static readonly HashSet<string> _playerBannerUnknownAnchors = new(StringComparer.Ordinal);

    private enum PlayerBannerKind
    {
        Unknown, Name, SquadNumber, Position, Nationality, Age, Status, Club, PlayingTime,
        International, Caps, TransferValue, Contract, CoachSection, ScoutSection, Competition,
        AgreedPlayingTime, Action,
    }

    /// <summary>
    /// Fold the player report banner's run of unlabelled stops into one label/value table, with
    /// the player's name left ahead of it as the heading it already was. Structural, not panel
    /// gated: the same banner renders under PlayerReport, PlayerSearchReport and every other
    /// panel that hosts a full player report, and the anchor set is specific enough to identify
    /// it on its own. Declines whole (leaving today's raw stops untouched) when the banner cannot
    /// be found, when its stops are not one contiguous run, or when too few anchors resolve.
    /// HTML surface only — the speech cursor still reads the bare values.
    /// </summary>
    private static void ConsolidatePlayerBanner(List<RenderItem> items)
    {
        // The banner's items are one contiguous run, so the scan stops at its end rather than
        // ancestor-walking the rest of the page — on a player report that is the whole attributes
        // section below, several hundred stops.
        VisualElement banner = null;
        int first = -1, last = -1;
        for (int i = 0; i < items.Count; i++)
        {
            var backing = PlayerBannerElement(items[i]);
            var found = backing == null ? null : FindAncestorNamed(backing, PlayerBannerContainer);
            if (found == null)
            {
                if (banner == null) continue;
                break;
            }
            if (banner == null) { banner = found; first = i; }
            last = i;
        }
        if (banner == null) return;

        // A non-stop item inside the run (a table, a synthesised heading) means something else
        // already claimed part of the banner; folding around it would reorder the page.
        for (int i = first; i <= last; i++)
            if (items[i] is not StopItem && items[i] is not JoinedTextItem)
            {
                Degrade.Note($"Player banner holds a {items[i].GetType().Name}, raw stops kept.");
                return;
            }

        var kinds = new PlayerBannerKind[last - first + 1];
        bool coach = false, scout = false;
        for (int i = first; i <= last; i++)
        {
            var source = (items[i] as StopItem)?.Element;
            var kind = PlayerBannerKindOf(PlayerBannerElement(items[i]), banner,
                source);
            if (kind == PlayerBannerKind.CoachSection) coach = true;
            else if (kind == PlayerBannerKind.ScoutSection) scout = true;
            // Section ancestry supplies knowledge context for other fields;
            // it does not make a self-captioned compound report opener into
            // an unnamed scalar value. Retain that context before classifying
            // this control's own row.
            kinds[i - first] = (kind is PlayerBannerKind.CoachSection or PlayerBannerKind.ScoutSection)
                && IsPlayerBannerAction(source) ? PlayerBannerKind.Action : kind;
        }

        // Both sections at once is a template we do not understand; picking either label for the
        // playing-time slot would be a guess, so that row goes out unlabelled instead.
        string playingTimeLabel =
            coach == scout ? null
            : coach ? PlayerBannerPlayingTimeLabel
            : PlayerBannerScoutingLabel;
        if (playingTimeLabel == null && (coach || scout))
            Degrade.Note("Player banner shows both a coach and a scout section; playing-time row left unlabelled.");

        var lead = new List<RenderItem>();
        var item = new PlayerBannerItem { Banner = banner };
        // Inside the coach/scout section the game draws its own label text before each control;
        // carry it across as that control's row label rather than inventing one. The stop it came
        // from is held, not just its text, so a label with no control after it can be put back.
        StopItem pendingLabelStop = null;
        int labelled = 0;
        for (int i = first; i <= last; i++)
        {
            var stop = items[i] as StopItem;
            var joined = items[i] as JoinedTextItem;
            var kind = kinds[i - first];
            if (kind == PlayerBannerKind.Name)
            {
                lead.Add(items[i]);
                continue;
            }

            if (kind == PlayerBannerKind.CoachSection || kind == PlayerBannerKind.ScoutSection)
            {
                if (pendingLabelStop == null && stop != null && ServerText.SafeRole(stop.Element) == "text")
                {
                    pendingLabelStop = stop;
                    continue;
                }
                string drawn = PlayerBannerLabelText(pendingLabelStop);
                item.Rows.Add(new PlayerBannerRow { Label = drawn, Value = stop?.Element, Parts = joined?.Parts });
                if (drawn != null) labelled++;
                pendingLabelStop = null;
                continue;
            }

            string label = kind switch
            {
                PlayerBannerKind.SquadNumber => PlayerBannerSquadNumberLabel,
                PlayerBannerKind.Position => PlayerBannerPositionLabel,
                PlayerBannerKind.Nationality => PlayerBannerNationalityLabel,
                PlayerBannerKind.Age => PlayerBannerAgeLabel,
                PlayerBannerKind.Status => PlayerBannerStatusLabel,
                PlayerBannerKind.Club => PlayerBannerClubLabel,
                PlayerBannerKind.PlayingTime => playingTimeLabel,
                PlayerBannerKind.AgreedPlayingTime => PlayerBannerPlayingTimeLabel,
                PlayerBannerKind.International => PlayerBannerInternationalLabel,
                PlayerBannerKind.Caps => PlayerBannerCapsLabel,
                PlayerBannerKind.TransferValue => PlayerBannerTransferValueLabel,
                PlayerBannerKind.Contract => PlayerBannerContractLabel,
                PlayerBannerKind.Competition => "Competition",
                _ => null,
            };
            IAccessibleElement labelSource = null;
            if (kind != PlayerBannerKind.Action
                && TryTakePlayerBannerCaption(item.Rows, PlayerBannerElement(items[i]), out var caption))
            {
                if (caption.Label != null) labelled--;
                labelSource = caption.Value;
                label = null;
            }
            item.Rows.Add(new PlayerBannerRow { Label = label, LabelSource = labelSource,
                Value = stop?.Element, Parts = joined?.Parts, IsAction = kind == PlayerBannerKind.Action });
            if (labelSource != null) labelled++;
            if (label != null) labelled++;
        }

        // A label the game drew with no control after it: it was a value in its own right, so put
        // it back as its own unlabelled row rather than dropping it.
        if (pendingLabelStop != null)
        {
            Degrade.Note("Player banner section label had no control after it; kept as an unlabelled row.");
            item.Rows.Add(new PlayerBannerRow { Label = null, Value = pendingLabelStop.Element });
        }

        if (labelled < PlayerBannerMinLabelledRows)
        {
            Degrade.Note($"Player banner named only {labelled} of {item.Rows.Count} values, raw stops kept.");
            return;
        }

        items.RemoveRange(first, last - first + 1);
        lead.Add(item);
        items.InsertRange(first, lead);
    }

    // A native linked text is represented by its original independently guarded
    // fragments. It is still one banner source, not the end of the banner run.
    // Multi-source summary joins retain their separate consolidation contract.
    private static VisualElement PlayerBannerElement(RenderItem item)
    {
        if (item is not JoinedTextItem joined) return ItemElement(item);
        if (joined.Parts.Count == 0 || joined.BodySource != null || joined.IsConversationQuestion) return null;
        var source = (joined.Parts[0] as Adapters.PluginAdapterBase)?.BackingElement;
        if (source == null) return null;
        return joined.Parts.All(part => part is Adapters.PluginAdapterBase adapter
            && adapter.BackingElement.Pointer == source.Pointer) ? source : null;
    }

    // FM27 draws some banner fields as a caption followed by linked text. Reuse
    // the shared native label resolver and require its exact caption container
    // to own the preceding readonly source. Matching display text alone could
    // otherwise consume a neighbouring field with the same value.
    private static bool TryTakePlayerBannerCaption(List<PlayerBannerRow> rows,
        VisualElement value, out PlayerBannerRow caption)
    {
        caption = null;
        if (value == null || rows.Count == 0) return false;
        var previous = rows[^1];
        if (previous.Parts != null || previous.LabelSource != null || previous.Value == null
            || ServerText.SafeRole(previous.Value) != "text"
            || previous.Value is not Adapters.PluginAdapterBase adapter) return false;
        var reader = new Adapters.FieldLabelReader(value);
        var label = reader.GetLabel();
        if (string.IsNullOrWhiteSpace(label) || label != DisplayText.Of(previous.Value)
            || reader.FieldLabelContainer == null) return false;
        // A label borrowed from an outer neighbouring field is insufficient.
        // The inspected native caption is a direct child of the same local
        // field that contains this value (possibly beneath binding wrappers).
        var captionElement = adapter.BackingElement;
        if (captionElement == null || reader.FieldLabelContainer.Pointer != captionElement.Pointer
            || captionElement.parent == null) return false;
        bool sameField = false;
        for (var p = value; p != null; p = p.parent)
            if (p.Pointer == captionElement.parent.Pointer) { sameField = true; break; }
        if (!sameField) return false;
        caption = previous;
        rows.RemoveAt(rows.Count - 1);
        return true;
    }

    // The text the game drew as a section label, or null when it cannot be read — an unlabelled
    // row rather than a wrong one.
    private static string PlayerBannerLabelText(StopItem stop)
    {
        if (stop == null) return null;
        try
        {
            string text = DisplayText.Of(stop.Element);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            Degrade.Note($"Player banner section label unreadable, row left unlabelled: {ex.Message}");
            return null;
        }
    }

    // Which banner value a stop is, decided by the nearest recognised anchor above it. Ascends
    // only as far as the banner itself, so a name that also occurs elsewhere on the screen cannot
    // reach in.
    private static PlayerBannerKind PlayerBannerKindOf(VisualElement backing, VisualElement banner,
        IAccessibleElement source)
    {
        // This is the actual FM27 contract property, even when its localized
        // display value says that the player is loaned elsewhere. A scout-report
        // section does not turn this known value into a scouting requirement.
        if (PlayerBannerIsAgreedPlayingTime(backing)) return PlayerBannerKind.AgreedPlayingTime;
        // The transfer value is the one cell with no named wrapper of its own: it is the button
        // holding the price-tag icon.
        if (HasDirectChildNamed(backing, PlayerBannerTransferValueIcon))
            return PlayerBannerKind.TransferValue;

        bool nation = false;
        string outermost = null;
        for (var p = backing; p != null && p.Pointer != banner.Pointer; p = p.hierarchy.parent)
        {
            string name = p.name;
            if (string.IsNullOrEmpty(name)) continue;
            outermost = name;
            switch (name)
            {
                case PlayerBannerSquadNumber: return PlayerBannerKind.SquadNumber;
                case PlayerBannerNameFrame: return PlayerBannerKind.Name;
                case PlayerBannerPositionCell: return PlayerBannerKind.Position;
                case PlayerBannerAgeCell: return PlayerBannerKind.Age;
                case PlayerBannerStatusFlags: return PlayerBannerKind.Status;
                case PlayerBannerPlayingTeam: return PlayerBannerKind.Club;
                case PlayerBannerPlayingTime: return PlayerBannerKind.PlayingTime;
                case PlayerBannerFreeAgent:
                    return p.parent?.name == "PlayingTimeVisible"
                        ? PlayerBannerKind.Competition : PlayerBannerKind.Club;
                case PlayerBannerContract: return PlayerBannerKind.Contract;
                case PlayerBannerNoContract: return PlayerBannerKind.Contract;
                case PlayerBannerCoachReport: return PlayerBannerKind.CoachSection;
                case PlayerBannerScoutReport: return PlayerBannerKind.ScoutSection;
                // FM27's shared report header keeps the same values but moves
                // them into renamed native components. These are structural
                // anchors, shared by full player reports in every context.
                case "OutfieldKit":
                case "GoalkeeperKit":
                case "GKKit": return PlayerBannerKind.SquadNumber;
                case "AbilityStarRange": return PlayerBannerKind.CoachSection;
                case "PlayingTimeVisible": return PlayerBannerKind.Competition;
                case "NationLink":
                    for (var owner = p.parent; owner != null && owner.Pointer != banner.Pointer; owner = owner.parent)
                        if (FindDescendantNamed(owner, PlayerBannerAgeCell) != null)
                            return PlayerBannerKind.Nationality;
                    return PlayerBannerKind.International;
                case "CellButton":
                    if (PlayerBannerIsAgreedPlayingTime(FindDescendantNamed(p, "primary-text")))
                        return PlayerBannerKind.AgreedPlayingTime;
                    if (FindDescendantNamed(p, "secondary-text") != null) return PlayerBannerKind.Contract;
                    var contractOwner = p.parent;
                    for (int depth = 0; depth < 3 && contractOwner != null && contractOwner.Pointer != banner.Pointer;
                         depth++, contractOwner = contractOwner.parent)
                        if (FindDescendantNamed(contractOwner, "secondary-text") != null)
                            return PlayerBannerKind.PlayingTime;
                    return PlayerBannerKind.Caps;
                // Two nation cells share this template name — the player's nationality and, further
                // down, the national team he plays for — so which one this is only becomes clear at
                // the frame above.
                case PlayerBannerNationCell: nation = true; break;
                case PlayerBannerBioFrame:
                    return nation ? PlayerBannerKind.Nationality : PlayerBannerKind.Unknown;
                case PlayerBannerInternationalFrame:
                    return nation ? PlayerBannerKind.International : PlayerBannerKind.Caps;
            }
            // The full name is now the first text in the identity block,
            // alongside the position component; its old numbered Frame is gone.
            if (FindDescendantNamed(p, PlayerBannerPositionCell) != null
                && FindVisibleDescendantNamed(p, "text", 0)?.Pointer == backing.Pointer)
                return PlayerBannerKind.Name;
        }

        // FM27 embeds standalone menu and compound rating/report actions in
        // this same banner. Known data-field anchors above retain precedence;
        // a self-labelled action needs no fabricated scalar-field heading.
        if (IsPlayerBannerAction(source))
            return PlayerBannerKind.Action;

        if (outermost != null && _playerBannerUnknownAnchors.Add(outermost))
            Degrade.Note($"Player banner value under \"{outermost}\" has no known label; row left unlabelled.");
        return PlayerBannerKind.Unknown;
    }

    private static bool IsPlayerBannerAction(IAccessibleElement source) =>
        source is Adapters.ContextMenuButtonAdapter
            or Adapters.StarRatingAbilityAdapter { HasCaptionedRatings: true };

    private static bool PlayerBannerIsAgreedPlayingTime(VisualElement source)
    {
        var binding = source?.TryCast<SI.Bindable.SIText>()?.m_textBinding;
        return binding?.Kind == SI.Bindable.BindingMethodKind.Direct
            && binding.Direct?.Path?.Path?.EndsWith(".FullContract.AgreedPlayingTime.DisplayValue",
                StringComparison.Ordinal) == true;
    }

    private static bool HasDirectChildNamed(VisualElement el, string name)
    {
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            if (string.Equals(el.hierarchy[i].name, name, StringComparison.Ordinal)) return true;
        return false;
    }

    // The live element a render item was built from, or null for one the walk synthesised.
    private static VisualElement ItemElement(RenderItem item) => item switch
    {
        StopItem s => (s.Element as Adapters.PluginAdapterBase)?.BackingElement,
        TableItem t => t.Table,
        RadioGroupItem r => r.Group,
        _ => null,
    };

    private static VisualElement FindVisibleHalf(VisualElement panel, string name, string tab)
    {
        var found = new List<VisualElement>();
        CollectVisibleNamed(panel, name, found, 0);
        foreach (var el in found)
            if (FindAncestorNamed(el, tab) != null) return el;
        return null;
    }

    private static void CollectVisibleNamed(VisualElement el, string name, List<VisualElement> into, int depth)
    {
        if (depth > 60 || !ElementBridge.IsVisible(el)) return;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = el.hierarchy[i];
            if (!ElementBridge.IsVisible(c)) continue;
            if (string.Equals(c.name, name, StringComparison.Ordinal)) { into.Add(c); continue; }
            CollectVisibleNamed(c, name, into, depth + 1);
        }
    }

    private static VisualElement FindVisibleDescendantNamed(VisualElement el, string name, int depth)
    {
        if (depth > 60 || !ElementBridge.IsVisible(el)) return null;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = el.hierarchy[i];
            if (!ElementBridge.IsVisible(c)) continue;
            if (string.Equals(c.name, name, StringComparison.Ordinal)) return c;
            var found = FindVisibleDescendantNamed(c, name, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static TableItem FindRoleTable(List<RenderItem> items, int from, int to)
    {
        for (int i = from; i <= to && i < items.Count; i++)
            if (items[i] is TableItem t && t.Headers != null && t.Headers.Count == 2) return t;
        return null;
    }

    private static string FirstTextIn(List<RenderItem> items, int from, int to)
    {
        for (int i = from; i <= to && i < items.Count; i++)
        {
            if (items[i] is not StopItem s || ServerText.SafeRole(s.Element) != "text") continue;
            var text = CleanOrNull(DisplayText.Of(s.Element));
            if (text != null) return text;
        }
        return null;
    }

    // The first text under a named descendant, at any depth (the game's no-suitable-roles message
    // sits in a branch named for exactly that state).
    private static string FindTextUnder(VisualElement el, string name, int depth)
    {
        var holder = FindVisibleDescendantNamed(el, name, depth);
        return holder == null ? null : CleanOrNull(ReadFirstText(holder));
    }

    // Every role at the position, first player's ranking first, matched to the second player's by
    // name. Null when a table's rows do not hold the two cells the pairing needs.
    private static List<List<string>> PairRoleRows(TableItem left, TableItem right)
    {
        var rows = new List<List<string>>();
        var byRole = new Dictionary<string, string>(StringComparer.Ordinal);
        if (right?.Rows != null)
            foreach (var row in right.Rows)
            {
                if (!TryReadRolePair(row, out string role, out string rating)) return null;
                byRole[role] = rating;
            }

        if (left?.Rows != null)
            foreach (var row in left.Rows)
            {
                if (!TryReadRolePair(row, out string role, out string rating)) return null;
                rows.Add(new List<string> { role, rating, byRole.TryGetValue(role, out var other) ? other : "" });
                byRole.Remove(role);
            }
        // A role only the second player's list carries still belongs in the comparison.
        foreach (var pair in byRole)
            rows.Add(new List<string> { pair.Key, "", pair.Value });
        return rows;
    }

    // The game draws the rating BEFORE the role it belongs to; RestructureBestRoleTable only flips
    // how that renders, so the cells themselves stay in the game's order and must be read by their
    // real column indices.
    private const int BestRoleRatingColumn = 0;
    private const int BestRoleNameColumn = 1;

    private static bool TryReadRolePair(Contexts.TableRow row, out string role, out string rating)
    {
        role = null;
        rating = null;
        if (row?.Cells == null || row.Cells.Count <= BestRoleNameColumn) return false;
        role = CellText(row.Cells[BestRoleNameColumn]);
        rating = CellText(row.Cells[BestRoleRatingColumn]);
        return !string.IsNullOrWhiteSpace(role);
    }

    private static string CellText(Contexts.TableCell cell)
    {
        if (cell?.Elements == null) return "";
        var parts = new List<string>();
        foreach (var e in cell.Elements)
            if (!string.IsNullOrWhiteSpace(e.Text)) parts.Add(e.Text);
        return string.Join(" ", parts);
    }

    // The half's suitability word. The template puts it alone under a style setter beside its icon,
    // which is what separates it from the position name and the tally drawn in the same block.
    private static string FindSuitabilityWord(VisualElement half)
    {
        var found = FindStyledText(half, 0);
        return string.IsNullOrWhiteSpace(found) ? null : found;
    }

    private static string FindStyledText(VisualElement el, int depth)
    {
        if (depth > 12 || !ElementBridge.IsVisible(el)) return null;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var child = el.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            string type;
            try { type = child.GetIl2CppType().Name; } catch { continue; }
            if (type == "SIStyleSetter")
            {
                var text = CleanOrNull(ReadFirstText(child));
                if (text != null) return text;
            }
            var deeper = FindStyledText(child, depth + 1);
            if (deeper != null) return deeper;
        }
        return null;
    }

    // The contiguous run of items belonging to one half, including the heading immediately above it.
    private static int FindHalfRange(List<RenderItem> items, VisualElement half, out int last)
    {
        int first = -1;
        last = -1;
        for (int i = 0; i < items.Count; i++)
        {
            var el = ItemElement(items[i]);
            if (el == null || !IsUnder(el, half)) continue;
            if (first < 0) first = i;
            last = i;
        }
        return first;
    }

    private static string FindHeadingIn(List<RenderItem> items, int from, int to)
    {
        for (int i = from; i <= to && i < items.Count; i++)
            if (items[i] is HeadingItem h && !string.IsNullOrWhiteSpace(h.Text)) return h.Text;
        return null;
    }

    private static bool IsUnder(VisualElement el, VisualElement ancestor)
    {
        for (var p = el; p != null; p = p.hierarchy.parent)
            if (ReferenceEquals(p, ancestor) || p.Pointer == ancestor.Pointer) return true;
        return false;
    }

    private static VisualElement FindAncestorNamed(VisualElement el, string name)
    {
        for (var p = el; p != null; p = p.hierarchy.parent)
            if (string.Equals(p.name, name, StringComparison.Ordinal)) return p;
        return null;
    }

    private const string ComparisonPositionsTab = "PositionsRoles";
    private const string ComparisonReportTab = "Report";
    // The branch the game switches to when a player has no suitable role at the chosen position.
    private const string ComparisonNoRolesContainer = "NoData";
    // The row naming the familiarity the game prints above each half's role table. Mod chrome: the
    // game draws the word with an icon and no label of any kind.
    private const string PositionSuitabilityRowLabel = "Position suitability";

    /// <summary>
    /// Withhold a category average that does not describe the column it sits under.
    ///
    /// The game computes these over its own MASKED display: an attribute you have no reading for
    /// counts as -1, and one you only have a range for counts as the bottom of that range. For a
    /// player you know fully that collapses to a plain mean and is right. For a partly scouted one
    /// it is arithmetic on placeholders — a striker whose technicals really average 8 is reported
    /// as 3, and a group you know nothing about at all comes out as -1, which no average of 1-20
    /// attributes can ever be.
    ///
    /// The test needs no knowledge of scouting: does the figure describe the numbers above it? A
    /// column holding anything but plain numbers has no average to state, and a column of plain
    /// numbers whose mean is not what the game printed is not that column's average. Either way the
    /// cell reads N/A. This is the one place on this card the mod declines to repeat what the game
    /// draws, because a wrong average is not something a reader can catch by glancing across.
    /// </summary>
    private static void MarkComparisonAverages(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != ComparisonPanelName) return;
        try
        {
            // Each attribute table is titled with its group; the averages table with our own title.
            var groups = new List<(string Name, TableItem Table)>();
            TableItem averages = null;
            for (int i = 1; i < items.Count; i++)
            {
                if (items[i] is not TableItem t || t.Headers == null) continue;
                if (t.Headers.Count != ComparisonAttributeColumns) continue;
                if (items[i - 1] is not HeadingItem h || string.IsNullOrEmpty(h.Text)) continue;
                if (h.Text == AveragesHeading) averages = t;
                else groups.Add((h.Text, t));
            }
            if (averages?.Rows == null || groups.Count == 0) return;

            foreach (var row in averages.Rows)
            {
                if (row?.Cells == null || row.Cells.Count <= ComparisonPlayerTwoColumn) continue;
                // "Technical Average" belongs to the "Technical" table. Both strings are the game's
                // own, in the game's language, so this stays a match between two live labels.
                string label = CellText(row.Cells[0]) ?? "";
                var group = groups.Find(g => label.StartsWith(g.Name, StringComparison.Ordinal));
                if (group.Table?.Rows == null) continue;

                if (!AverageDescribesColumn(group.Table, ComparisonPlayerOneColumn, row.Cells[ComparisonPlayerOneColumn]))
                    SetCellText(row.Cells[ComparisonPlayerOneColumn], UnavailableAverage);
                if (!AverageDescribesColumn(group.Table, ComparisonPlayerTwoColumn, row.Cells[ComparisonPlayerTwoColumn]))
                    SetCellText(row.Cells[ComparisonPlayerTwoColumn], UnavailableAverage);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Average check failed, game values kept: {ex.Message}");
        }
    }

    // True when the shown figure is the mean of that column's attributes. False when the column
    // holds anything that is not a plain number (a range, or no reading at all), since then there is
    // no mean to have stated.
    private static bool AverageDescribesColumn(TableItem group, int column, Contexts.TableCell shownCell)
    {
        if (!int.TryParse(CellText(shownCell), out int shown)) return false;

        int sum = 0, count = 0;
        foreach (var row in group.Rows)
        {
            if (row?.Cells == null || column >= row.Cells.Count) return false;
            if (!int.TryParse(CellText(row.Cells[column]), out int value)) return false;
            sum += value;
            count++;
        }
        if (count == 0) return false;

        // The game truncates rather than rounds (a 7.5 group reads 7); a point either way is still
        // the same figure, so only a real disagreement withholds it.
        int expected = (int)Math.Floor(sum / (double)count);
        return Math.Abs(expected - shown) <= 1;
    }

    private static void SetCellText(Contexts.TableCell cell, string text)
    {
        if (cell == null) return;
        if (cell.Elements.Count == 0)
        {
            cell.Elements.Add(new Contexts.TableCellElement { Text = text });
            return;
        }
        var first = cell.Elements[0];
        first.Text = text;
        cell.Elements[0] = first;
        for (int i = cell.Elements.Count - 1; i > 0; i--) cell.Elements.RemoveAt(i);
    }

    // What a category average reads when the game's figure does not describe its column. The game's
    // own word for a value it cannot state, used here for the same reason.
    private const string UnavailableAverage = "N/A";

    // Two tables the same shape, so folding one into the other cannot file a value under the wrong
    // column heading.
    private static bool SameColumns(TableItem a, TableItem b)
    {
        if (a.Headers == null || b.Headers == null) return false;
        if (a.Headers.Count != b.Headers.Count || a.GutterColumn != b.GutterColumn) return false;
        for (int i = 0; i < a.Headers.Count; i++)
            if (!string.Equals(a.Headers[i].Text, b.Headers[i].Text, StringComparison.Ordinal)) return false;
        return true;
    }

    // The best-role table: role first, and its game-drawn title above it as a heading.
    private static int RestructureBestRoleTable(List<RenderItem> items, int index, TableItem t)
    {
        t.DisplayOrder = new[] { 1, 0 };

        // The line the game draws directly above the table ("When Playing As An D (C)") is its
        // title. Structural: the item before it, and only when that is a plain text stop.
        if (index == 0 || items[index - 1] is not StopItem s || ServerText.SafeRole(s.Element) != "text")
            return 0;
        string title = CleanOrNull(DisplayText.Of(s.Element));
        if (title == null) return 0;

        items[index - 1] = new HeadingItem { Text = title, Level = 4 };
        return 0;
    }

    // A column the game left unnamed: empty, or the bare dash its header template draws in place
    // of a name.
    private static bool IsUnnamedHeader(string text)
    {
        var t = (text ?? "").Trim();
        return t.Length == 0 || t == "-";
    }

    /// <summary>
    /// True when every rendered row leaves <paramref name="column"/> blank — no text, no control,
    /// no status flags. The test a declared gutter column must pass before it is left out.
    /// </summary>
    internal static bool IsColumnEmpty(TableItem t, int column)
    {
        if (t.Rows == null) return false;
        // A totals label/value can occupy a column whose ordinary rows are
        // empty. It is still content and must not disappear as a gutter.
        if(t.Footer!=null&&column<t.Footer.Count&&t.Footer[column].Count>0)return false;
        foreach (var row in t.Rows)
        {
            if (row?.Cells == null || column >= row.Cells.Count) continue;
            var cell = row.Cells[column];
            if (cell == null) continue;
            if (cell.StatusFlags != null || cell.StatusFlagsUnreadable) return false;
            foreach (var e in cell.Elements)
                if (e.CanActivate || e.IsDropdown || e.IsToggle || !string.IsNullOrWhiteSpace(e.Text))
                    return false;
        }
        return true;
    }

    private static void SetHeaderText(TableItem t, int index, string text)
    {
        var h = t.Headers[index];
        h.Text = text;
        t.Headers[index] = h;
    }

    private const int ComparisonAttributeColumns = 4;
    private const int ComparisonPlayerOneColumn = 1;
    private const int ComparisonSpacerColumn = 2;
    private const int ComparisonPlayerTwoColumn = 3;
    private const string AttributeColumnLabel = "Attribute";
    // The averages run's title. Mod-authored: the game draws these four grids with no title at all,
    // so without it they read as four unannounced one-row slabs after the attribute sections.
    private const string AveragesHeading = "Averages";

    // The Player Comparison card's Positions & Roles pitch. It is the tab's primary control:
    // picking a slot re-reads BOTH players' familiarity, best-role table and footedness for that
    // position, and with nothing picked the tab collapses to the bare slot names and two empty
    // player sections. The pitch carries no SIButton and no "positions-selector-root", so neither
    // the picker-grid consolidation nor the selector-indicator promotion recognises it, and the
    // slots walked out as fourteen inert words stranded between the two players — reading as
    // player one's, and doing nothing when activated.
    //
    // Selection runs through the pitch's own FMTacticsPanel.HandleClick path, which
    // PositionSelectorAdapter already drives (verified live on this pitch via /positionprobe).
    // Exactly one slot is ever selected, so the slots become one radiogroup, labelled and headed
    // in its own right — which also stops it reading as part of player one's section.
    //
    // Anything unexpected leaves the raw stops: no indicator run, a slot whose position text the
    // walk did not collect, or a group the adapter cannot place in the pitch's own position list.
    private static void ConsolidateComparisonPositionPicker(List<RenderItem> items)
    {
        var groups = CollectIndicatorRun(items, ComparisonPitchContainer, out int first, out int last);
        if (groups == null) return;

        var pitch = ElementBridge.FindAncestorByTypeName(groups[0].Indicator, "FMTacticsPanel");
        if (pitch == null) return;

        var radios = new List<IAccessibleElement>(groups.Count);
        foreach (var g in groups)
        {
            // The slot's own position text ("D (C)"). CollectIndicatorRun files a text it cannot
            // recognise as a position under the player parts, so accept either — this template
            // names neither frame the way the planner's does.
            string label = g.Position ?? (g.PlayerParts.Count > 0 ? g.PlayerParts[0] : null);
            if (label == null) return;
            radios.Add(new Adapters.PositionSelectorAdapter(g.Indicator, label));
        }

        items.RemoveRange(first, last - first + 1);
        items.Insert(first, new RadioGroupItem
        {
            Group = pitch,
            Label = PositionHeaderLabel,
            Radios = radios,
        });
        items.Insert(first, new HeadingItem { Text = PositionHeaderLabel, Level = 3 });
    }

    // Table chrome for the pitch tables; the fallbacks mirror the game's own legend wording and
    // are used only when the legend stops are not where the template puts them.
    private const string PositionHeaderLabel = "Position";
    private const string BestPlayerHeaderFallback = "Best Player";
    private const string BestProspectHeaderFallback = "Best Prospect";

    // Two consecutive text stops in sibling frames under one shared ancestor (the Best XI legend
    // pair — each label sits in its own "Frame" wrapper, so sameness lives at the grandparent).
    private static bool TryReadSiblingTextPair(List<RenderItem> items, int index, out string first, out string second)
    {
        first = null; second = null;
        if (items[index] is not StopItem a || items[index + 1] is not StopItem b) return false;
        if (ServerText.SafeRole(a.Element) != "text" || ServerText.SafeRole(b.Element) != "text") return false;
        var grandA = ((a.Element as Adapters.PluginAdapterBase)?.BackingElement)?.hierarchy.parent?.hierarchy.parent;
        var grandB = ((b.Element as Adapters.PluginAdapterBase)?.BackingElement)?.hierarchy.parent?.hierarchy.parent;
        if (grandA == null || grandB == null) return false;
        try
        {
            if (grandA.Pointer != grandB.Pointer) return false;
        }
        catch { return false; }
        first = DisplayText.Of(a.Element);
        second = DisplayText.Of(b.Element);
        return !string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(second);
    }

    // The recruitment budget cards (the Finances "Recruitment Budgets" hub and its Transfer /
    // Payroll / Scouting detail cards), the Recruitment Focus tool, and the Scout Priorities card
    // walk their "speak to" advisor sidebar — the advisor names and their action buttons/dropdown —
    // before the panel's own content. Move the sidebar behind the content under an "Advisors"
    // landmark so the data reads first and the actions stay reachable by heading. Speech keeps
    // the raw walk order (profiles cannot reorder). Mirrors RestructureTunnelInterview.
    private const string AdvisorsHeadingLabel = "Advisors";

    private static void FoldCoachWorkloadCounts(List<RenderItem> items)
    {
        for (int i = items.Count - 2; i >= 0; i--)
        {
            if (items[i] is not StopItem value || items[i + 1] is not StopItem caption
                || ServerText.SafeRole(value.Element) != "text"
                || ServerText.SafeRole(caption.Element) != "text"
                || value.Element.CanActivate || caption.Element.CanActivate
                || value.Element.HasContextMenu || caption.Element.HasContextMenu) continue;
            var count = (value.Element as Adapters.PluginAdapterBase)?.BackingElement;
            var label = (caption.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (count?.name != "count" || label?.name != "text"
                || !Adapters.RadialGaugeAdapter.TryCountedWorkloadParts(count.parent,
                    out var ownCount, out var ownCaption, out _)
                || ownCount.Pointer != count.Pointer || ownCaption.Pointer != label.Pointer) continue;
            string name = CleanOrNull(DisplayText.Of(caption.Element));
            if (name == null || CleanOrNull(DisplayText.Of(value.Element)) == null) continue;
            items[i] = new LabelValueItem { Label = name, Value = value.Element };
            items.RemoveAt(i + 1);
        }
    }

    private static void FoldBudgetTilePairs(List<RenderItem> items)
    {
        for(int i=items.Count-2;i>=0;i--)
        {
            if(items[i] is not StopItem label||items[i+1] is not StopItem value
                ||ServerText.SafeRole(label.Element)!="text"||ServerText.SafeRole(value.Element)!="text")continue;
            if(!BudgetTileReadout.IsMonetaryPair((label.Element as Adapters.PluginAdapterBase)?.BackingElement,
                (value.Element as Adapters.PluginAdapterBase)?.BackingElement))continue;
            var caption=CleanOrNull(DisplayText.Of(label.Element));
            if(caption==null)continue;
            items[i]=new LabelValueItem{Label=caption,Value=value.Element};
            items.RemoveAt(i+1);
        }
    }

    // Native compact totals place the caption below two values. Keep the
    // original live-tracked JoinedText paragraph and move only that exact
    // sibling caption first, so amount/count/season remain one readable unit.
    // Other controls, headings, rich links and neighbouring groups stay raw.
    private static void JoinCaptionedTotals(List<RenderItem> items)
    {
        Adapters.PluginAdapterBase Plain(RenderItem item)
        {
            if(item is not StopItem stop||stop.Element.CanActivate||ServerText.SafeRole(stop.Element)!="text"
                ||ServerText.IsHeading(stop.Element)||stop.Element is not Adapters.PluginAdapterBase native)return null;
            var text=native.BackingElement.TryCast<TextElement>();
            return text!=null&&!string.IsNullOrWhiteSpace(text.text)&&!Adapters.SITextLink.HasLink(text.text)?native:null;
        }
        int VisibleChildren(VisualElement parent)
        {
            int count=0;for(int c=0;c<parent.hierarchy.childCount;c++)if(ElementBridge.IsVisible(parent.hierarchy[c]))count++;
            return count;
        }
        for(int i=items.Count-3;i>=0;i--)
        {
            if(i+2>=items.Count)continue;
            var first=Plain(items[i]);var second=Plain(items[i+1]);var caption=Plain(items[i+2]);
            if(first==null||second==null||caption==null)continue;
            if(BudgetTileReadout.IsCaptionStatusValueRow(first.BackingElement,second.BackingElement,caption.BackingElement))
            {
                var summary=new JoinedTextItem{LeadingCaption=true};
                summary.Parts.Add(first);summary.Parts.Add(second);summary.Parts.Add(caption);
                if(summary.Parts.Any(part=>part.HasContextMenu))continue;
                items.RemoveRange(i,3);items.Insert(i,summary);
                continue;
            }
            int length=3;
            var fourth=i+3<items.Count?Plain(items[i+3]):null;
            var wages=BudgetTileReadout.WageSummaryOf(first.BackingElement);
            if(wages!=null&&fourth!=null
                &&BudgetTileReadout.WageSummaryOf(second.BackingElement)?.Pointer==wages.Pointer
                &&BudgetTileReadout.WageSummaryOf(caption.BackingElement)?.Pointer==wages.Pointer
                &&BudgetTileReadout.WageSummaryOf(fourth.BackingElement)?.Pointer==wages.Pointer)
            {
                caption=fourth;length=4;
            }
            else
            {
                var values=first.BackingElement.parent;var row=values?.parent;
                if(values==null||row==null||second.BackingElement.parent?.Pointer!=values.Pointer
                    ||caption.BackingElement.parent?.Pointer!=row.Pointer
                    ||VisibleChildren(values)!=2||VisibleChildren(row)!=2)continue;
                if(!BudgetTileReadout.IsInCard(first.BackingElement))continue;
            }
            var joined=new JoinedTextItem{LeadingCaption=true};
            joined.Parts.Add(caption);
            for(int v=0;v<length-1;v++)joined.Parts.Add(((StopItem)items[i+v]).Element);
            if(joined.Parts.Any(part=>part.HasContextMenu))continue;
            items.RemoveRange(i,length);items.Insert(i,joined);
        }
    }

    private static readonly HashSet<string> SpeakToRelocationPanels = new()
    {
        "RecruitmentBudgetsCard",
        "RecruitmentTransferBudgetsCard",
        "RecruitmentWageBudgetCard",
        "RecruitmentScoutingBudgetCard",
        "RecruitmentFocusTool",
        "RecruitmentScoutPrioritiesCard",
        "RecruitmentSquadPlannerReport",
    };

    private static void RestructureBudgetSpeakTo(List<RenderItem> items, object panelObj)
    {
        if (!SpeakToRelocationPanels.Contains(NavigationManager.GetPanelName(panelObj) ?? "")) return;

        var sidebar = new List<RenderItem>();
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is not StopItem s) continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null) continue;
            if (AncestorNameContains(backing, "SpeakToSidePanel")) { sidebar.Insert(0, items[i]); items.RemoveAt(i); }
        }
        if (sidebar.Count == 0) return;
        items.Add(new HeadingItem { Text = AdvisorsHeadingLabel, Level = 3 });
        items.AddRange(sidebar);
    }

    private static void InsertHeadingOnce(List<RenderItem> items, RenderItem anchor, string text)
    {
        foreach (var it in items)
            if (it is HeadingItem h && h.Text == text) return;
        items.Insert(items.IndexOf(anchor), new HeadingItem { Text = text, Level = 3 });
    }

    // The Match Squad screen's action row (Quick Pick, Actions, Filter, the possession toggles)
    // runs straight from the next-fixture chip into the selection table with no boundary. Drop a
    // "Squad" heading before the Quick Pick controls so a screen reader can jump from the fixture
    // landmark to the squad-picking region by heading. Anchored on the QuickPick binding ancestor
    // (structural), gated to this panel by name; no Quick Pick stop inserts nothing.
    private const string SquadHeadingLabel = "Squad";

    private static void InsertMatchSquadHeading(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != "TeamSquadReport") return;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not StopItem s) continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null) continue;
            if (AncestorNameContains(backing, "QuickPick"))
            {
                // Level 3 sits level with this panel's profile-promoted h3 landmarks under its <h2>.
                items.Insert(i, new HeadingItem { Text = SquadHeadingLabel, Level = 3 });
                return;
            }
        }
    }

    // The advanced career-setup screen needs three tweaks a profile cannot express: a landmark
    // over the leagues region, the Advanced Settings tile pulled up next to Preferences (the game
    // parks it dead last, after the whole nations table), and the Estimated Game Speed label folded
    // together with its gauge reading so they read as one stop instead of two. All structural and
    // gated to this panel; each step skips silently if its anchor is absent. HTML-only — the speech
    // surface keeps the raw walk order (profiles cannot reorder or merge).
    private const string NationsAndLeaguesHeadingLabel = "Nations and Leagues";

    // The two attribute-summary tables closing a player report's attributes block.
    private const string BestAttributesHeading = "Best Attributes";
    private const string WorstAttributesHeading = "Worst Attributes";

    private static void RestructureAdvancedSetup(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != "GameCreationAdvancedSetupScreen") return;
        try
        {
            MergeEstimatedGameSpeed(items);
            MoveAdvancedSettingsBelowPreferences(items);
            InsertNationsAndLeaguesHeading(items);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Advanced Setup restructure failed, raw stops kept: {ex.Message}");
        }
    }

    // The Estimated Game Speed widget draws its label and its gauge reading ("Balanced") as two
    // sibling text stops under the same frame, separated only by the radial gauge (which is not a
    // stop). Fold the consecutive run of text stops in that frame into one "label: value" line.
    // Fewer than two readable parts leaves the raw stops untouched.
    private static void MergeEstimatedGameSpeed(List<RenderItem> items)
    {
        int first = -1, last = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not StopItem s || ServerText.SafeRole(s.Element) != "text")
            {
                if (first >= 0) break;
                continue;
            }
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null || !AncestorNameContains(backing, "Frame 1000002686"))
            {
                if (first >= 0) break;
                continue;
            }
            if (first < 0) first = i;
            last = i;
        }
        if (first < 0 || last <= first) return;

        var parts = new List<string>(last - first + 1);
        for (int i = first; i <= last; i++)
        {
            string t = DisplayText.Of(((StopItem)items[i]).Element);
            if (!string.IsNullOrEmpty(t)) parts.Add(t);
        }
        if (parts.Count < 2) return;

        items.RemoveRange(first, last - first + 1);
        items.Insert(first, new NoteItem { Text = string.Join(": ", parts) });
    }

    // Both tiles carry the same "layout-tile-interaction-button" name, so distinguish them by their
    // tile-container ancestor. Move the Advanced Settings tile to directly after Preferences; either
    // tile missing leaves the order untouched.
    private static void MoveAdvancedSettingsBelowPreferences(List<RenderItem> items)
    {
        StopItem prefs = null, advSettings = null;
        foreach (var item in items)
        {
            if (item is not StopItem s) continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null || backing.name != "layout-tile-interaction-button") continue;
            if (prefs == null && AncestorNameContains(backing, "PreferencesFMMenu")) prefs = s;
            else if (advSettings == null && AncestorNameContains(backing, "AdvancedSettingsSummary")) advSettings = s;
        }
        if (prefs == null || advSettings == null) return;
        items.Remove(advSettings);
        items.Insert(items.IndexOf(prefs) + 1, advSettings);
    }

    // Drop a "Nations and Leagues" landmark before the Add Nations button, which fronts the leagues
    // region (the men's/women's toggle and the nations table). Anchored on the button's stable name
    // plus its add-remove-league frame ancestor; no such button inserts nothing.
    private static void InsertNationsAndLeaguesHeading(List<RenderItem> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not StopItem s) continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null) continue;
            if (backing.name == "button-primary-trailing-icon-label"
                && AncestorNameContains(backing, "add-remove-league"))
            {
                items.Insert(i, new HeadingItem { Text = NationsAndLeaguesHeadingLabel, Level = 3 });
                return;
            }
        }
    }

    // ============================ Contract negotiation ============================
    //
    // The ContractNegotiationTool (player contract talks, every phase: the preliminary promises,
    // the full offer, the read-only submitted view) walks as a flat run of per-term fragments:
    // each term's negotiability padlock dropdown, its name text and its value control arrive as
    // three unrelated stops (the padlock even reads BEFORE the name it belongs to), the
    // wage-budget footer reads as eight loose label/value texts, and the agent's response buttons
    // sit above the offer they act on. Restructure the HTML surface: expand the SpeakTo
    // conversation at walk time and move its response buttons behind the content under an
    // "Actions" landmark; give the negotiation block one heading composed from its live
    // title/club/player; consolidate each run of term rows into a table (Term | Value |
    // Negotiability | Remove); promote the in-block section titles ("Bonuses & Clauses",
    // "Additional Promises") and the side blocks' titles to landmarks; and fold the budget-impact
    // label/value pairs into "label: value" lines with the value live-tracked. Every anchor is
    // structural and every step skips silently when its target is absent. Speech keeps the raw
    // walk order (profiles cannot reorder or merge).
    private const string ContractActionsHeadingLabel = "Actions";
    private const string ContractBlockName = "frames-contract negotiations-contract terms";
    private const string ContractTermRowName = "frames-contract terms";
    private const string ContractOtherInfoBlockName = "frames-contract negotiations-other info";
    private const string ContractBudgetBlockName = "frames-contract negotiations-wage budget impact";

    // The transfer offer's Base Details rows are the SAME term-row template the contract
    // negotiation uses (a negotiability padlock, the term's name, its value control), differing
    // only in the frame's spelling. Read raw they linearize into "Fee, dropdown, Negotiable" /
    // "Fee" / "$3, combobox" — three stops, two of them called Fee, with the money value orphaned
    // from its name. Consolidate them into the same table: Term | Value | Negotiability, which
    // names the padlock after its term and gives the money combobox its preset value list beside
    // its steppers (see RenderContractTermValue). Only the term rows are touched; the rest of the
    // panel keeps its walk order, and speech keeps the raw order as always.
    // The offer header names its recipient across three separate texts ("To", the club, the player
    // in brackets), and both the cost block and the player-info block draw a label and its value as
    // two loose stops. Read linearly a value arrives with nothing attached to it — "$3" a line after
    // the cost it belongs to. Fold each into one line.
    private const string OfferSubtitleBlockName = "club-name-visible";
    private const string OfferCostBlockName = "Frame_Cost";
    private const string OfferPlayerInfoBlockName = "Frame_Info";

    // Transfer Activity's offer-detail card draws eight values with no labels beside them — the
    // club, the division, the positions, the role, then two game-labelled money blocks and a bare
    // date. Read sequentially that is a run of nouns whose meanings are carried only by where they
    // sit on screen, which a screen reader does not convey. Fold the read-only ones into one
    // Detail/Value table and leave the card's three buttons (nationality, status flag, club) where
    // they are, so nothing actionable is swallowed.
    //
    // Every row is matched by the ancestor the game itself names, never by position in the run: the
    // division is conditional (SIVisible "HasDivision"), so a positional pairing would shift every
    // label onto the wrong value for a player whose club has none. A value whose anchor is absent
    // simply contributes no row.
    private static void RestructureTransferActivity(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != "RecruitmentTransferActivityTool") return;
        try
        {
            var detail = FoldOfferDetailCard(items);
            ReorderOfferScreen(items, detail);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Transfer activity restructure failed, raw stops kept: {ex.Message}");
        }
    }

    // Put the screen in dependency order and name its three sections.
    //
    // The game lays this out as two columns, which flattens into: offer list, budget block, the
    // controls that filter the list, then the selected offer's detail. Read sequentially the
    // budget table splits the list from its own detail, and the two controls that decide what the
    // list contains arrive after it. Two moves fix both — the budget block to the end, the
    // controls to just above the list — and three headings say what each section is.
    //
    // Total by construction: the output is built by walking the original list and re-emitting
    // every item, so anything this does not recognise keeps its place rather than being dropped.
    // The count is asserted before the result is committed; a mismatch means a bug in the splice
    // and the original order is kept, because losing a control off the page is far worse than
    // reading it in an awkward order.
    private static void ReorderOfferScreen(List<RenderItem> items, SyntheticTableItem detailTable)
    {
        var directionTabs = FindDirectionTabs(items);
        var listAnchor = FindOffersListAnchor(items);
        var playerName = FindPlayerNameStop(items);
        var controls = FindListControls(items);
        var budget = FindBudgetBlock(items, detailTable);

        // Nothing recognised (the General Manager Offers tab, or an empty direction tab): leave it.
        bool movesControls = listAnchor != null && controls.Count > 0;
        if (!movesControls && budget.Count == 0 && directionTabs == null && playerName == null) return;

        var relocated = new HashSet<RenderItem>();
        if (movesControls) foreach (var c in controls) relocated.Add(c);
        foreach (var b in budget) relocated.Add(b);

        int headingsAdded = 0;
        var output = new List<RenderItem>(items.Count + 3);
        foreach (var item in items)
        {
            if (ReferenceEquals(item, directionTabs))
            {
                output.Add(new HeadingItem { Text = OffersHeading, Level = 3 });
                headingsAdded++;
            }
            if (ReferenceEquals(item, playerName))
            {
                output.Add(new HeadingItem { Text = SelectedOfferHeading, Level = 3 });
                headingsAdded++;
            }
            if (movesControls && ReferenceEquals(item, listAnchor))
                output.AddRange(controls);
            if (relocated.Contains(item)) continue;
            output.Add(item);
        }
        output.AddRange(budget);

        if (output.Count != items.Count + headingsAdded)
        {
            Log.Warn($"[AccessibilityServer] Transfer activity reorder dropped items " +
                     $"({items.Count} + {headingsAdded} headings -> {output.Count}); original order kept.");
            return;
        }
        items.Clear();
        items.AddRange(output);
    }

    // Mod chrome: the game names none of these three sections, and unnamed they are three
    // indistinguishable runs. "Budget" is emitted by the synthetic table's own spec.
    private const string OffersHeading = "Offers";
    private const string SelectedOfferHeading = "Selected offer";

    private static TabBarItem FindDirectionTabs(List<RenderItem> items)
    {
        foreach (var item in items)
            if (item is TabBarItem t && t.Label == "Direction") return t;
        return null;
    }

    // The list's own title, which the controls must sit above.
    private static RenderItem FindOffersListAnchor(List<RenderItem> items)
    {
        foreach (var item in items)
            if (item is StopItem s && BackingNameIs(s, "most-recent-offers-text")) return s;
        return null;
    }

    private static RenderItem FindPlayerNameStop(List<RenderItem> items)
    {
        foreach (var item in items)
        {
            if (item is not StopItem s) continue;
            var el = StopElement(s);
            if (el != null && AncestorNameContains(el, "PlayerNameFrame")) return s;
        }
        return null;
    }

    // The Active-offers toggle and the Filter button, in the order the game draws them.
    private static List<RenderItem> FindListControls(List<RenderItem> items)
    {
        var found = new List<RenderItem>();
        foreach (var item in items)
        {
            if (item is not StopItem s) continue;
            var el = StopElement(s);
            if (el == null) continue;
            if (AncestorNameContains(el, "active-toggle") || AncestorNameContains(el, "filter-button-frame"))
                found.Add(s);
        }
        return found;
    }

    // The budget table and the heading its spec emitted directly above it. Identified by NOT being
    // the detail table this same pass built — an identity check on a managed object we created, so
    // no reliance on the table's own header text.
    private static List<RenderItem> FindBudgetBlock(List<RenderItem> items, SyntheticTableItem detailTable)
    {
        var block = new List<RenderItem>();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not SyntheticTableItem t || ReferenceEquals(t, detailTable)) continue;
            if (i > 0 && items[i - 1] is HeadingItem h) block.Add(h);
            block.Add(t);
            return block;
        }
        return block;
    }

    private static bool BackingNameIs(StopItem s, string name)
        => StopElement(s)?.name == name;

    // The game element a stop stands for. Most stops are PluginAdapterBase; the filter trigger is a
    // FilterPopupSurface.TriggerAdapter wrapping the popup widget, and without this case it would
    // look like a stop with no element and silently miss every ancestry match.
    private static VisualElement StopElement(StopItem s) => s.Element switch
    {
        Adapters.PluginAdapterBase a => a.BackingElement,
        FilterPopupSurface.TriggerAdapter t => t.Widget,
        _ => null,
    };

    // The card's read-only values, each with the ancestor that proves what it is. Order here is the
    // table's row order.
    //
    // "Role" and "Contract expires" are the only mod-authored labels; the money rows reuse the
    // game's own label text. The date was proven by cross-reading the Transfer Status panel, which
    // renders the same player's salary and contract end under the game's own labels ("Contract,
    // Duration, 14 Jul 25, 30 Jun 26, Salary, $88.5K - $105K p/a") — matching this card's wage range
    // and date exactly, on two different players.
    private const string ClubAnchor = "SiVisible-ValidateClub";
    private const string DivisionAnchor = "HasDivision";
    private const string PositionsAnchor = "ShowPerson";
    private const string PosFrameAnchor = "SiVisible-ValidatePerson-pos-frame";
    private const string ValueFrameAnchor = "SiVisible-ValidatePerson-contract-frame";
    private const string WagesFrameAnchor = "Frame_Wages";

    // Returns the table it built, so the reorder can tell it apart from the budget table by
    // identity rather than by matching header text. Null when the card declines to fold.
    private static SyntheticTableItem FoldOfferDetailCard(List<RenderItem> items)
    {
        // Collected in walk order per anchor, so a block that draws a label before its value keeps
        // that pairing without relying on the run's overall position.
        var club = new List<string>();
        var division = new List<string>();
        var positions = new List<string>();
        var role = new List<string>();
        var money = new List<string>();
        var wages = new List<string>();
        var indices = new List<int>();

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not StopItem s || ServerText.SafeRole(s.Element) != "text") continue;
            var el = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (el == null) continue;
            var text = CleanOrNull(DisplayText.Of(s.Element));
            if (text == null) continue;

            List<string> bucket = null;
            if (AncestorNameContains(el, DivisionAnchor)) bucket = division;
            else if (AncestorNameContains(el, ClubAnchor)) bucket = club;
            else if (AncestorNameContains(el, PositionsAnchor)) bucket = positions;
            else if (AncestorNameContains(el, PosFrameAnchor)) bucket = role;
            else if (AncestorNameContains(el, ValueFrameAnchor)) bucket = money;
            else if (AncestorNameContains(el, WagesFrameAnchor)) bucket = wages;
            if (bucket == null) continue;

            bucket.Add(text);
            indices.Add(i);
        }

        var rows = new List<List<string>>();
        AddRow(rows, "Club", club, 0);
        AddRow(rows, "Division", division, 0);
        AddRow(rows, "Positions", positions, 0);
        AddRow(rows, "Role", role, 0);
        // Money blocks draw their own label first, then the value: reuse the game's word.
        if (money.Count >= 2) rows.Add(new List<string> { money[0], money[1] });
        if (wages.Count >= 2) rows.Add(new List<string> { wages[0], wages[1] });
        if (wages.Count >= 3) rows.Add(new List<string> { "Contract expires", wages[2] });

        // Below two rows this is not a table worth the chrome — leave the card alone.
        if (rows.Count < 2 || indices.Count == 0) return null;

        int at = indices[0];
        for (int i = indices.Count - 1; i >= 0; i--) items.RemoveAt(indices[i]);
        var table = new SyntheticTableItem { Headers = new[] { "Detail", "Value" }, Rows = rows };
        items.Insert(at, table);
        return table;
    }

    private static void AddRow(List<List<string>> rows, string label, List<string> values, int index)
    {
        if (index >= values.Count) return;
        rows.Add(new List<string> { label, values[index] });
    }

    private static void RestructureTransferOffer(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != "TransferOfferTool") return;
        try
        {
            ConsolidateContractTermRows(items);
            FoldOfferSubtitle(items);
            // Both blocks lay one label and one value per row frame; the player-info values are live
            // buttons (the club, the nation, the player's contract), which the fold keeps actionable.
            FoldFieldRows(items, OfferCostBlockName);
            FoldFieldRows(items, OfferPlayerInfoBlockName);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Transfer offer restructure failed, raw stops kept: {ex.Message}");
        }
    }

    // "To" + club + "(player)" as one line. Joins whatever of the three is present, so a header
    // variant with no player still reads; fewer than two texts is not a run worth folding.
    private static void FoldOfferSubtitle(List<RenderItem> items)
    {
        int start = -1, count = 0;
        var parts = new List<string>();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not StopItem s || ServerText.SafeRole(s.Element) != "text") continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null || !AncestorNameContains(backing, OfferSubtitleBlockName)) continue;
            if (start < 0) start = i;
            else if (i != start + count) break; // not a contiguous run; leave it alone
            count++;
            var text = CleanOrNull(DisplayText.Of(s.Element));
            if (text != null) parts.Add(text);
        }
        if (start < 0 || count < 2 || parts.Count < 2) return;
        items.RemoveRange(start, count);
        items.Insert(start, new NoteItem { Text = string.Join(" ", parts) });
    }

    // Fold each "label then value" row inside a block into one label/value line. A row is a frame
    // whose parent is the named block, so the two stops are paired by the layout's own grouping
    // rather than by adjacency alone — a row the game leaves half-drawn simply does not fold. The
    // value keeps its element, so an actionable value stays actionable on the page.
    private static void FoldFieldRows(List<RenderItem> items, string blockName)
    {
        for (int i = 0; i + 1 < items.Count; i++)
        {
            if (items[i] is not StopItem label || items[i + 1] is not StopItem value) continue;
            if (ServerText.SafeRole(label.Element) != "text") continue;
            var lb = (label.Element as Adapters.PluginAdapterBase)?.BackingElement;
            var vb = (value.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (lb == null || vb == null) continue;
            var row = RowFrameUnder(lb, blockName);
            if (row == null) continue;
            var valueRow = RowFrameUnder(vb, blockName);
            if (valueRow == null || valueRow.Pointer != row.Pointer) continue;
            var text = CleanOrNull(DisplayText.Of(label.Element));
            if (text == null) continue;
            items[i] = new LabelValueItem { Label = TrimTrailingColon(text), Value = value.Element };
            items.RemoveAt(i + 1);
        }
    }

    // The row frame an element sits in: the ancestor whose own parent is the named block. Wrapper
    // levels between the block and its rows (an SIVisible per cost row) become the row identity,
    // which is exactly right — they are one per row.
    private static VisualElement RowFrameUnder(VisualElement el, string blockName)
    {
        for (var p = el; p != null; p = p.hierarchy.parent)
        {
            var parent = p.hierarchy.parent;
            if (parent != null && parent.name == blockName) return p;
        }
        return null;
    }

    // The renderer joins label and value with ": ", so a label the game already punctuates
    // ("Current Transfer Budget Cost:") must not contribute a second colon.
    private static string TrimTrailingColon(string label)
    {
        var trimmed = label.TrimEnd();
        return trimmed.EndsWith(":") ? trimmed.Substring(0, trimmed.Length - 1).TrimEnd() : label;
    }

    private static void RestructureContractNegotiation(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != "ContractNegotiationTool") return;
        try
        {
            ExpandContractSpeakTo(items);
            ConsolidateContractHeader(items);
            ConsolidateContractTermRows(items);
            PromoteContractSectionTitles(items);
            RestructureContractSideBlocks(items);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Contract negotiation restructure failed, raw stops kept: {ex.Message}");
        }
    }

    // Replace the collapsed SpeakTo container stop with its walked children. SpeakToRenderer does
    // this same expansion at render time, but only a walk-time expansion lets the response buttons
    // (Finalize Deal, Cancel Negotiations, Exit Talks) move behind the offer they act on, so the
    // player reviews the terms before reaching the buttons that commit them. The conversation
    // (speaker, dialogue bubbles) stays at the container's position.
    private static void ExpandContractSpeakTo(List<RenderItem> items)
    {
        // FM27 can expose the conversation as ordinary stops already. Collect
        // that contiguous native side-panel span into the same expansion path.
        // The original collapsed-button variant remains supported below.
        for (int start = 0; start < items.Count; start++)
        {
            var first = ItemElement(items[start]);
            if (first == null) continue;
            var context = FindAncestorNamed(first, "BindingExpect_SpeakToSidePanel");
            if (context == null) continue;
            if (items[start] is StopItem collapsed && ServerText.SafeRole(collapsed.Element) == "button"
                && IsSpeakToContainerElement(first)) break;
            int end = start;
            while (end < items.Count && ItemElement(items[end]) is { } element
                && FindAncestorNamed(element, "BindingExpect_SpeakToSidePanel")?.Pointer == context.Pointer) end++;
            var expanded = items.GetRange(start, end - start);
            var actions = RestructureContractConversation(expanded, context);
            items.RemoveRange(start, end - start);
            items.InsertRange(start, expanded);
            AppendContractActions(items, actions);
            return;
        }
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not StopItem s) continue;
            if (ServerText.SafeRole(s.Element) != "button") continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null || !IsSpeakToContainerElement(backing)) continue;

            var expanded = WalkChildren(backing);
            if (expanded.Count == 0) return; // nothing inside yet; keep the collapsed stop
            var actions = RestructureContractConversation(expanded, backing);
            items.RemoveAt(i);
            items.InsertRange(i, expanded);
            AppendContractActions(items, actions);
            return;
        }
    }

    private static List<RenderItem> RestructureContractConversation(List<RenderItem> expanded, VisualElement backing)
    {
        // Dialogue bubbles are distinguished by side alone (the player's on the right, the
        // NPC's on the left) — invisible as bare text, so who says what was unreadable.
        // Carry the attribution the layout conveys: the player's lines as "You: ...", the
        // NPC's under their own name, read live from the conversation header. An unreadable
        // name leaves the NPC lines bare (status quo) with a note, never a guessed speaker.
        string speaker = CleanOrNull(ReadFirstTextUnder(backing, "Name"));
        if (speaker == null)
            Degrade.Note("contract talks: speaker name unreadable, NPC dialogue left unattributed");

        var actions = new List<RenderItem>();
        for (int j = expanded.Count - 1; j >= 0; j--)
        {
            if (expanded[j] is not StopItem es) continue;
            var eb = (es.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (eb == null) continue;
            if (AncestorNameContains(eb, "OptionalButtonsFrame"))
            {
                actions.Insert(0, expanded[j]);
                expanded.RemoveAt(j);
                continue;
            }
            if (ServerText.SafeRole(es.Element) != "text") continue;
            if (AncestorNameContains(eb, "HumanBubbleFrame"))
            {
                var line = CleanOrNull(DisplayText.Of(es.Element));
                if (line != null)
                    expanded[j] = new NoteItem { Text = "You: " + line };
            }
            else if (speaker != null && AncestorNameContains(eb, "NPCBubbleFrame"))
            {
                var line = CleanOrNull(DisplayText.Of(es.Element));
                if (line != null)
                    expanded[j] = new NoteItem { Text = speaker + ": " + line };
            }
        }

        return actions;
    }

    private static void AppendContractActions(List<RenderItem> items, List<RenderItem> actions)
    {
        if (actions.Count == 0) return;
        items.Add(new HeadingItem { Text = ContractActionsHeadingLabel, Level = 3 });
        items.AddRange(actions);
    }

    // Mirrors SpeakToRenderer's container detection: a button within a few ancestor levels of the
    // SpeakTo side-panel marker.
    private static bool IsSpeakToContainerElement(VisualElement el)
    {
        var ancestor = el.hierarchy.parent;
        for (int depth = 0; ancestor != null && depth < 6; depth++)
        {
            var name = ancestor.name;
            if (name == "BindingExpect_SpeakToSidePanel" || name == "SpeakToSidePanel") return true;
            ancestor = ancestor.hierarchy.parent;
        }
        return false;
    }

    // One heading for the negotiation block: its title when the phase shows one, then the club
    // and the game's parenthesised player name ("Promises: Chelsea (Teddy Sharman-Lowe)"). The
    // offer phase has no title text — that slot holds the offer-view dropdown (Current Offer /
    // Previous Offer / Existing Contract), which stays a stop right after the heading — so its
    // heading is club and player alone. The consumed texts are removed; nothing readable inserts
    // nothing (and the title keeps its profile-promoted heading as the fallback).
    private static void ConsolidateContractHeader(List<RenderItem> items)
    {
        StopItem title = null, club = null, player = null;
        StopItem viewDropdown = null;
        foreach (var item in items)
        {
            if (item is not StopItem s) continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null) continue;
            string role = ServerText.SafeRole(s.Element);
            if (role == "dropdown" && viewDropdown == null && AncestorNameContains(backing, "club-icon-remapper"))
                viewDropdown = s;
            if (role != "text") continue;
            if (title == null && backing.hierarchy.parent?.name == "Title"
                && AncestorNameContains(backing, "navigation-browser_tab-label-default")) title = s;
            else if (club == null && backing.name == "club-subtitle-text") club = s;
            else if (player == null && backing.name == "club-player-text") player = s;
        }

        string heading = ContractTermsText.ComposeBlockHeading(
            title != null ? CleanOrNull(DisplayText.Of(title.Element)) : null,
            club != null ? CleanOrNull(DisplayText.Of(club.Element)) : null,
            player != null ? CleanOrNull(DisplayText.Of(player.Element)) : null);
        if (heading == null) return;

        // The offer phase leads its header with the offer-view dropdown; the heading belongs
        // before it so the section starts at the landmark.
        RenderItem anchor = (RenderItem)viewDropdown ?? title ?? club ?? player;
        items.Insert(items.IndexOf(anchor), new HeadingItem { Text = heading, Level = 3 });
        if (title != null) items.Remove(title);
        if (club != null) items.Remove(club);
        if (player != null) items.Remove(player);
    }

    // Each contract term draws as one "frames-contract terms" row: the negotiability padlock
    // dropdown, the term's name text, its value control (a stepper combobox or a choice dropdown)
    // and, for removable bonuses/clauses, a Delete button. Consolidate each consecutive run of
    // row fragments into one ContractTermsItem the renderer draws as a table. Fragments a row
    // variant adds that this walk does not model ride along as extras so nothing drops.
    private const string ContractConditionsHeadingLabel = "Conditions";

    private static void ConsolidateContractTermRows(List<RenderItem> items)
    {
        var runs = new List<(int start, int count, List<RenderItem> replacements)>();
        int i = 0;
        while (i < items.Count)
        {
            if (TermRowRootOf(items[i]) == null) { i++; continue; }

            int start = i;
            var rows = new List<ContractTermRow>();
            ContractTermRow row = null;
            VisualElement currentRoot = null;
            bool currentTarget = false;
            while (i < items.Count)
            {
                var root = TermRowRootOf(items[i]);
                if (root == null) break;
                // A conditional target block ("If the player during 2025/26 season provides:
                // [statistic] [count]") extends its promise row but is its own term: give it its
                // own row, headed by the condition sentence read live from the block and tied to
                // its promise by the promise's own name.
                bool target = IsUnderTargetBlock(items[i]);
                // Pointer identity: IL2CPP wrapper objects are re-created per walk, so wrapper
                // reference comparison is meaningless (see the context-menu zombie-panel fix).
                if (currentRoot == null || root.Pointer != currentRoot.Pointer || target != currentTarget)
                {
                    currentRoot = root;
                    currentTarget = target;
                    row = new ContractTermRow();
                    if (target)
                    {
                        row.IsCondition = true;
                        row.Term = ReadConditionLabel(root);
                        row.ParentTerm = ReadConditionParentTerm(root);
                    }
                    rows.Add(row);
                }
                AssignTermRowPart(row, (StopItem)items[i]);
                i++;
            }

            // Terms and conditions render as two separate tables: the conditions follow their
            // promises under their own landmark, each naming the promise it applies to.
            var terms = new ContractTermsItem();
            var conditions = new ContractConditionsItem();
            foreach (var r in rows)
            {
                if (r.IsCondition) conditions.Rows.Add(r);
                else terms.Rows.Add(r);
            }
            var replacements = new List<RenderItem>();
            if (terms.Rows.Count > 0) replacements.Add(terms);
            if (conditions.Rows.Count > 0)
            {
                replacements.Add(new HeadingItem { Text = ContractConditionsHeadingLabel, Level = 4 });
                replacements.Add(conditions);
            }
            runs.Add((start, i - start, replacements));
        }

        for (int k = runs.Count - 1; k >= 0; k--)
        {
            items.RemoveRange(runs[k].start, runs[k].count);
            items.InsertRange(runs[k].start, runs[k].replacements);
        }
    }

    // The name of the promise a condition block extends — its row wrapper's own term text.
    private static string ReadConditionParentTerm(VisualElement rowRoot)
    {
        var frames = FindTermRowFrame(rowRoot);
        if (frames == null) return null;
        var label = FindDescendantNamed(frames, "primary-text");
        return label != null ? CleanOrNull(ReadFirstText(label)) : null;
    }

    // The transfer offer ships the same row template under a hyphenated name. Both spellings are
    // the row's controls frame; nothing else in either panel is named either way.
    private const string TransferTermRowName = "frames-contract-terms";

    private static bool IsTermRowFrameName(string name)
        => name == ContractTermRowName || name == TransferTermRowName;

    private static VisualElement FindTermRowFrame(VisualElement root)
        => FindDescendantNamed(root, ContractTermRowName) ?? FindDescendantNamed(root, TransferTermRowName)
            ?? FindNativeTermFrame(root, 0);

    // FM27 removes the controls-frame name but retains the common term template:
    // padlock/name inside Frame, and the value branch beside it. Match that
    // structure within the row wrapper, never a caption or contract phase.
    private static VisualElement FindNativeTermFrame(VisualElement root, int depth)
    {
        if (root == null || depth > 4) return null;
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var child = root.hierarchy[i];
            if (child.name == "Frame" && HasChildNamed(child, "lock-button-enabled")
                && HasChildNamed(child, "text-frame") && HasChildNamed(root, "button-1-visible"))
                return root;
        }
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var found = FindNativeTermFrame(root.hierarchy[i], depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static VisualElement TermRowRootOf(RenderItem item)
    {
        if (item is not StopItem s) return null;
        var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
        return backing == null ? null : FindTermRowRoot(backing);
    }

    // The term row's root. Every row wraps its content in a "background-colour-style-setter"
    // whose "frames-contract terms" child holds the main term controls; a conditional target
    // extension ("If the player during 2025/26 season provides: ..." on a future playing-time
    // promise) sits under the same wrapper but OUTSIDE the frames element, so the wrapper — not
    // the frames element — is the row's identity. A style-setter with no frames descendant is
    // not a term row; exact name matches so the block container ("frames-contract
    // negotiations-contract terms") never matches.
    private const string ContractRowWrapperName = "background-colour-style-setter";

    private static VisualElement FindTermRowRoot(VisualElement el)
    {
        for (var p = el; p != null; p = p.hierarchy.parent)
        {
            if (p.name == ContractRowWrapperName)
                return FindTermRowFrame(p) != null ? p : null;
            if (IsTermRowFrameName(p.name))
            {
                var wrapper = p.hierarchy.parent;
                return wrapper != null && wrapper.name == ContractRowWrapperName ? wrapper : p;
            }
        }
        return null;
    }

    private static bool IsUnderTargetBlock(RenderItem item)
    {
        if (item is not StopItem s) return false;
        var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
        return backing != null && AncestorNameContains(backing, "target-visible");
    }

    // The condition sentence heading a target block ("If the player during 2025/26 season
    // provides:"). It is not a walked stop — the block's dropdowns fold it as their field label —
    // so read it live from the named label element. An unset condition (the block shows only the
    // game's "Set Target" dropdown) has no label element and reads null deliberately: a broader
    // first-text read would pick up the dropdown's own value as the sentence. The renderer tells
    // the unset state from an unreadable one by the target dropdown's presence.
    private static string ReadConditionLabel(VisualElement rowRoot)
    {
        var block = FindDescendantNamed(rowRoot, "target-visible");
        if (block == null) return null;
        var label = FindDescendantNamed(block, "playing-time-promise-text");
        return label != null ? CleanOrNull(ReadFirstText(label)) : null;
    }

    private static void AssignTermRowPart(ContractTermRow row, StopItem s)
    {
        var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
        string role = ServerText.SafeRole(s.Element);

        if (role == "text" && backing?.name == "primary-text" && row.Term == null)
        {
            var t = CleanOrNull(DisplayText.Of(s.Element));
            if (t != null) { row.Term = t; return; }
        }
        else if (backing != null && AncestorNameContains(backing, "lock-button-enabled") && row.Lock == null)
        {
            row.Lock = s.Element;
            return;
        }
        // The Delete button's wrapper differs per row template ("remove-button-enabled" on bonus
        // rows, "remove-button-visible" on promise rows), so match its own template name too.
        else if (backing != null && row.Delete == null
                 && (backing.name == "buttons-controls-pc-default" || AncestorNameContains(backing, "remove-button")))
        {
            row.Delete = s.Element;
            return;
        }
        else if ((role == "dropdown" || role == "combobox") && row.Value == null)
        {
            row.Value = s.Element;
            return;
        }
        row.Extras.Add(s.Element);
    }

    // The in-block section titles ("Bonuses & Clauses", "Additional Promises") are the only plain
    // texts left inside the negotiation block after the header and the term rows are consumed;
    // promote them to landmarks so each terms table sits under its own heading.
    private static void PromoteContractSectionTitles(List<RenderItem> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not StopItem s) continue;
            if (ServerText.SafeRole(s.Element) != "text") continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null) continue;
            if (!AncestorNameContains(backing, ContractBlockName)
                && FindAncestorNamed(backing, "NegotiationBindingExpect") == null) continue;
            if (AncestorNameContains(backing, "navigation-browser_tab-label-default")) continue;
            if (FindTermRowRoot(backing) != null) continue;
            var t = CleanOrNull(DisplayText.Of(s.Element));
            if (t == null) continue;
            items[i] = new HeadingItem { Text = t, Level = 3 };
        }
    }

    // The side blocks: "Other Information" (its title fronts the Promises/Availability buttons)
    // and "Payroll Budget Impact" (its title, then label/value text pairs — Cost, Current, Avg.,
    // Remaining). Promote each block's title to a landmark and fold the budget pairs into
    // "label: value" lines whose value spans stay live-tracked, so the numbers keep updating as
    // the offer changes. A pair run that is odd or non-adjacent is left raw — misaligned folding
    // would caption a number with the wrong label.
    private static void RestructureContractSideBlocks(List<RenderItem> items)
    {
        PromoteFirstBlockText(items, ContractOtherInfoBlockName);
        PromoteFirstBlockText(items, "Contract Negotiation Other Info Block");
        FoldContractBudgetBlock(items, ContractBudgetBlockName);
        FoldContractBudgetBlock(items, "Wage Budget Impact Block");
        FoldContractBudgetBlock(items, "Transfer Budget Impact Block");
    }

    private static void FoldContractBudgetBlock(List<RenderItem> items, string budgetBlock)
    {
        if (PromoteFirstBlockText(items, budgetBlock) < 0) return;

        var textIdx = new List<int>();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not StopItem s) continue;
            if (ServerText.SafeRole(s.Element) != "text") continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null || !AncestorNameContains(backing, budgetBlock)) continue;
            textIdx.Add(i);
        }
        if (textIdx.Count == 0 || textIdx.Count % 2 != 0) return;
        for (int k = 0; k < textIdx.Count; k += 2)
            if (textIdx[k + 1] != textIdx[k] + 1) return;

        for (int k = textIdx.Count - 2; k >= 0; k -= 2)
        {
            var label = (StopItem)items[textIdx[k]];
            var value = (StopItem)items[textIdx[k + 1]];
            string labelText = CleanOrNull(DisplayText.Of(label.Element));
            if (labelText == null) continue;
            items[textIdx[k]] = new LabelValueItem { Label = labelText, Value = value.Element };
            items.RemoveAt(textIdx[k + 1]);
        }
    }

    // Promote the first readable text stop under the named block to a level-3 landmark.
    // Returns its index, or -1 when the block has no readable text.
    private static int PromoteFirstBlockText(List<RenderItem> items, string blockName)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not StopItem s) continue;
            if (ServerText.SafeRole(s.Element) != "text") continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null || !AncestorNameContains(backing, blockName)) continue;
            var t = CleanOrNull(DisplayText.Of(s.Element));
            if (t == null) return -1;
            items[i] = new HeadingItem { Text = t, Level = 3 };
            return i;
        }
        return -1;
    }

    // Head Coach Creation's Appearance step is a grid of portrait thumbnails with nothing the mod
    // can read, so AppearanceAutoSkip picks a preset and moves past it. This note at the top of
    // appearance screen explains that inherited gap. Biography/background/style
    // screens have their own functional controls and must not repeat this warning.
    private static void InsertAppearanceUnavailableNote(List<RenderItem> items, object panelObj)
    {
        var name = NavigationManager.GetPanelName(panelObj) ?? "";
        if (name != "ManagerCreationScreen") return;
        items.Insert(0, new NoteItem { Text = "Appearance editing not available in FM27 Access." });
    }

    // The head-coach selection screen lists the profiles as radios, then shows the selected coach's
    // biography and attributes beside them. Nothing names that detail block, so on the linear HTML
    // surface it ran straight from the last radio into "Full Name" with no boundary. Drop a heading
    // carrying the selected coach's name between the radio group and the biography, so a screen
    // reader can jump to the selected coach's details by heading. The name is read live from the
    // biography's own Forename/Surname (which always reflect the current selection). Gated to this
    // panel by name and to its exact shape (a radio group followed by the biography name pair), so
    // no other screen is touched; a missing selection (no name) inserts nothing.
    private static void InsertSelectedManagerHeading(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != "GameCreationManagerSelectionScreen") return;

        int radioIdx = -1;
        for (int i = 0; i < items.Count; i++)
            if (items[i] is RadioGroupItem) { radioIdx = i; break; }
        if (radioIdx < 0) return;

        string forename = null, surname = null;
        for (int i = radioIdx + 1; i < items.Count; i++)
        {
            if (items[i] is not StopItem s) continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null) continue;
            if (backing.name == "Forename") forename = SafeStopLabel(s.Element);
            else if (backing.name == "Surname") surname = SafeStopLabel(s.Element);
        }

        string name = JoinName(forename, surname);
        if (string.IsNullOrEmpty(name)) return;

        // The biography ("Full Name" …) directly follows the radio group, so inserting right after it
        // places the heading below the radios and above the selected coach's information.
        items.Insert(radioIdx + 1, new HeadingItem { Text = name, Level = 3 });
    }

    private static string SafeStopLabel(IAccessibleElement el)
    {
        try { return el.GetLabel(); }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] Selected-manager name read failed: {ex.Message}"); return null; }
    }

    private static string JoinName(string forename, string surname)
    {
        forename = forename?.Trim();
        surname = surname?.Trim();
        if (string.IsNullOrEmpty(forename)) return surname ?? "";
        if (string.IsNullOrEmpty(surname)) return forename;
        return forename + " " + surname;
    }

    // The Portal Overview dashboard runs the Messages widget straight into a row of action tiles with
    // no boundary. When the walk promoted a widget title to a heading (the dashboard signal) AND there
    // are tile stops, drop one "Shortcuts" heading before the first tile so the messages stop bleeding
    // into the tiles. One heading, not one per tile; anchored on the tile container (not names/order/
    // count), so it holds across save, team and club-vs-nation, and never fires on a tile-less screen.
    private const string ShortcutsHeadingLabel = "Shortcuts";

    // The Opposition Report ends most of its tabs with the same tile row, but they are analysis
    // cards, not navigation shortcuts, and the Team Analysis tab is nothing BUT tiles — no earlier
    // heading, so the dashboard gate below never fires and the tiles arrive with no landmark at
    // all. Name them for what they are there, and let the heading stand on its own.
    private const string AnalysisHeadingLabel = "Analysis";
    private const string AnalysisTilePanel = "TeamReportSite";

    private static void InsertShortcutsHeading(List<RenderItem> items, object panelObj)
    {
        if (panelObj is VisualElement portal && NavigationManager.GetPanelName(panelObj) == "PortalScreen"
            && FindDescendantNamed(portal, "PortalAdaptiveContent") != null) return;
        bool analysis = NavigationManager.GetPanelName(panelObj) == AnalysisTilePanel;
        string label = analysis ? AnalysisHeadingLabel : ShortcutsHeadingLabel;
        bool dashboard = analysis;
        foreach (var it in items) if (it is HeadingItem) { dashboard = true; break; }
        if (!dashboard) return;
        for (int i = 0; i < items.Count; i++)
            if (items[i] is StopItem s && IsDashboardTile(s.Element))
            {
                // A heading already sitting on the tiles (the Player Instructions grids label
                // their own) IS the boundary this exists to create; a second would misname it.
                if (i > 0 && items[i - 1] is HeadingItem) return;
                items.Insert(i, new HeadingItem { Text = label, Level = 2 });
                return;
            }
    }

    private static bool IsDashboardTile(IAccessibleElement el)
    {
        var backing = (el as Adapters.PluginAdapterBase)?.BackingElement;
        if (backing == null) return false;
        for (var p = backing; p != null; p = p.hierarchy.parent)
            if (p.name == "layout-tile-base-container") return true;
        return false;
    }

    // On the title bar, bring the controls a player reaches for most - the Continue prompt (whose
    // label changes: "Continue", "Response Required", …) and the current date - to the very front,
    // ahead of Back/Forward and the ribbon that otherwise bury them at the end. Match by structure,
    // never label: the Continue button by its stable "continue-button" name, the date by its
    // "CurrentDayWidget" container (its own name is the shared ribbon-button name). Gated on the date
    // widget, which only the title bar carries, so a "continue-button" inside some content flow is
    // never reordered. Order out: Continue, then date, then the existing Back/Forward/ribbon run.
    private static void ReorderTitleBarControls(List<RenderItem> items)
    {
        StopItem continueItem = null, dateItem = null;
        foreach (var item in items)
            if (item is StopItem s)
            {
                if (IsContinueButton(s.Element)) continueItem = s;
                else if (IsDateWidget(s.Element)) dateItem = s;
            }
        if (dateItem == null) return;
        items.Remove(dateItem);
        if (continueItem != null) items.Remove(continueItem);
        items.Insert(0, dateItem);
        if (continueItem != null) items.Insert(0, continueItem);
    }

    // The staff responsibilities card's header shows "Staff Responsibilities" and "Advice and
    // Reports" as one stacked pair of page links in its top-right corner, but the tree parks
    // the second one inside the tab bar's extra slot, so it read several stops later (after the
    // tabs and the stats). Reunite the pair: the advice link moves to directly after the
    // responsibilities link. Both anchors are structural (the stable "responsibilities-button"
    // template name; a button under the tab bar's ExtraTabTemplate slot) and both must be
    // present, so no other screen is touched.
    private static void ReorderCardHeaderLinks(List<RenderItem> items)
    {
        StopItem respLink = null, adviceLink = null;
        foreach (var item in items)
            if (item is StopItem s)
            {
                if (IsResponsibilitiesLink(s.Element)) respLink = s;
                else if (IsTabSlotButton(s.Element)) adviceLink = s;
            }
        if (respLink == null || adviceLink == null) return;
        items.Remove(adviceLink);
        items.Insert(items.IndexOf(respLink) + 1, adviceLink);
    }

    // The Responsibilities screens read as long ladders of loose stops: on the Delegation tab each
    // responsibility spends four to five stops (title, description, a Take Control / Delegate
    // button pair, the assignee dropdown) with nothing tying them together, so the player must
    // hold a row in their head to know which dropdown belongs to which duty; the Overview tab
    // spends two per duty; and the Advice and Reports tool spends two or three. Consolidate every
    // run into a table — one per group, keeping the group's own heading and its "…All" buttons
    // above it and any trailing group preferences below it.
    //
    // Delegation rows are found by container, which the template names well: each group's duties
    // live in a StreamedObjectList("IndividualResponsibilities") and each duty in its own
    // AsyncBindingRoot inside it, so a run breaks exactly at a group boundary (the group name and
    // AssignAll buttons are siblings of that list, never inside it). Within a row every part is
    // named too: the buttons by their SIEnabled wrappers ("TakeControlEnabled" /
    // "DelegateControlEnabled"), the assignee by its SIDropdown("AssignableStaff"), and the
    // game's own "no staff available" copy by its NoAssignableStaff block. Anything a row variant
    // adds that this model does not know rides along in the control cell, so no stop is dropped.
    //
    // The control pair is rendered as the game's two buttons, not as a derived state word: the
    // disabled one is the state the game is already in (verified live — clicking the enabled one
    // flips which is disabled, and the count of rows whose Take Control is disabled matches the
    // header's "under control" tally exactly). Re-presenting that as prose would be an inference
    // the mod would have to keep true across patches, so the buttons speak for themselves and the
    // renderer only adds a note where BOTH are disabled — the one case the player cannot read off
    // an enabled control, and a fact rather than a deduction.
    //
    // HTML surface only; the speech surface keeps the raw stop ladder (profiles cannot merge).
    private const string ResponsibilitiesPanelName = "StaffResponsibilitiesCard";
    // ============================ Squad happiness ============================
    //
    // The Happiness tool's selected-player block walks as a flat run of loose texts: the player's
    // name, his age/position line, then "Happiness" and its value, then "Morale" and its value, as
    // six unrelated paragraphs. Fold the identity and the happiness reading into one landmark line
    // heading the block, and the morale label/value pair into one plain line. The rest of the block
    // (hierarchy, positives, negatives) keeps the landmarks its panel profile marks. Every anchor is
    // a template name; any part missing (no player selected, a section the player has no data for)
    // leaves the raw stops untouched. Speech keeps the raw walk order (profiles cannot merge).
    private const string SquadHappinessPanelName = "SquadHappinessTool";
    private const string HappinessSelectedBlockName = "SquadHappinessSelectedPlayerBlock";
    private const string HappinessIdentityFrameName = "Frame 1000003090";
    private const string HappinessSectionName = "CanShowHappiness";
    private const string MoraleSectionName = "Frame 1000003044";
    private const string HappinessValueCellName = "data_display-cell-standard-regular";

    private static void RestructureSquadHappiness(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != SquadHappinessPanelName) return;
        try
        {
            StopItem name = null, subtitle = null, happinessLabel = null, happinessValue = null;
            StopItem moraleLabel = null, moraleValue = null;
            foreach (var item in items)
            {
                if (item is not StopItem s || ServerText.SafeRole(s.Element) != "text") continue;
                var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
                if (backing == null || !AncestorNameContains(backing, HappinessSelectedBlockName)) continue;
                string parentName = backing.hierarchy.parent?.name;
                bool isValue = parentName == HappinessValueCellName;

                if (AncestorNameContains(backing, HappinessIdentityFrameName))
                {
                    if (name == null) name = s;
                    else if (subtitle == null) subtitle = s;
                }
                else if (AncestorNameContains(backing, HappinessSectionName))
                {
                    if (isValue) happinessValue ??= s;
                    else happinessLabel ??= s;
                }
                else if (AncestorNameContains(backing, MoraleSectionName))
                {
                    if (isValue) moraleValue ??= s;
                    else moraleLabel ??= s;
                }
            }

            FoldHappinessHeading(items, name, subtitle, happinessLabel, happinessValue);
            FoldMoraleLine(items, moraleLabel, moraleValue);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Squad happiness restructure failed, raw stops kept: {ex.Message}");
        }
    }

    // "Joseph Atangana, 25 year-old centre-back, Slightly Happy" as the block's landmark. The name
    // alone is not worth a heading, so a block with no readable subtitle and no readable happiness
    // keeps its raw stops.
    private static void FoldHappinessHeading(
        List<RenderItem> items, StopItem name, StopItem subtitle, StopItem happinessLabel, StopItem happinessValue)
    {
        if (name == null) return;

        var parts = new List<string>(3);
        foreach (var stop in new[] { name, subtitle, happinessValue })
        {
            if (stop == null) continue;
            string text = CleanOrNull(DisplayText.Of(stop.Element));
            if (text != null) parts.Add(text);
        }
        if (parts.Count < 2) return;

        items[items.IndexOf(name)] = new HeadingItem { Text = string.Join(", ", parts), Level = 3 };
        foreach (var stop in new[] { subtitle, happinessLabel, happinessValue })
            if (stop != null) items.Remove(stop);
    }

    private static void FoldMoraleLine(List<RenderItem> items, StopItem label, StopItem value)
    {
        if (label == null || value == null) return;
        string labelText = CleanOrNull(DisplayText.Of(label.Element));
        string valueText = CleanOrNull(DisplayText.Of(value.Element));
        if (labelText == null || valueText == null) return;

        items[items.IndexOf(label)] = new NoteItem { Text = $"{labelText}: {valueText}" };
        items.Remove(value);
    }

    private const string AdviceAndReportsPanelName = "StaffAdviceAndReportsTool";
    private const string ResponsibilityRowListName = Contexts.ResponsibilitiesContext.DutyListName;
    private const string ResponsibilityGroupNameElement = "ResponsibilityGroupName";
    private const string ResponsibilityAssignAllBlockName = "AssignAll";
    private const string ResponsibilityDelegationRemapperName = "StaffResponsibilitiesDelegation";
    private const string ResponsibilityAssignDropdownName = "AssignableStaff";
    private const string ResponsibilityNoStaffBlockName = "NoAssignableStaff";
    private const string ResponsibilityTakeControlWrapperName = "TakeControlEnabled";
    private const string ResponsibilityDelegateWrapperName = "DelegateControlEnabled";
    private const string ResponsibilityOverviewRemapperName = "StaffResponsibilitiesOverview";
    private const string ResponsibilityRadioGroupName = "StaffResponsibilitiesRadioGroup";
    private const string ResponsibilityRowFrameName = "Frame 12";
    private const string ResponsibilityCountFrameName = "Frame 1000003158";
    private const string AdviceFrequencyDropdownName = "dropdown-base-regular-row-template-trailing-icon";

    private static void RestructureResponsibilities(List<RenderItem> items, object panelObj)
    {
        string panel = NavigationManager.GetPanelName(panelObj);
        if (panel != ResponsibilitiesPanelName && panel != AdviceAndReportsPanelName) return;
        try
        {
            if (panel == ResponsibilitiesPanelName)
            {
                FoldResponsibilityCount(items);
            }
            else
            {
                ConsolidateAdviceRows(items);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Responsibilities restructure failed, raw stops kept: {ex.Message}");
        }
    }

    // The Overview tab heads each staff member's duty list with a bare count above a bare
    // "Responsibilities" caption, which read as two unrelated stops ("7", "Responsibilities").
    // Fold them into one live line. Gated on the template's own count frame holding exactly the
    // value and its caption, so the pair is never guessed from position alone.
    private static void FoldResponsibilityCount(List<RenderItem> items)
    {
        for (int i = 0; i < items.Count - 1; i++)
        {
            if (items[i] is not StopItem value || items[i + 1] is not StopItem caption) continue;
            var vb = (value.Element as Adapters.PluginAdapterBase)?.BackingElement;
            var cb = (caption.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (vb == null || cb == null) continue;
            if (vb.hierarchy.parent == null || cb.hierarchy.parent == null) continue;
            if (vb.hierarchy.parent.name != ResponsibilityCountFrameName) continue;
            if (vb.hierarchy.parent.Pointer != cb.hierarchy.parent.Pointer) continue;

            string label = CleanOrNull(DisplayText.Of(caption.Element));
            if (label == null) continue;
            items.RemoveRange(i, 2);
            items.Insert(i, new LabelValueItem { Label = label, Value = value.Element });
            return;
        }
    }

    // The Advice and Reports tool's rows carry no per-row container name this walk can trust (the
    // frequency rows each have their own generated frame name), so rows are closed on their own
    // control instead: within a run of the tool's setting stops, every text is a title or its
    // description and the dropdown that follows ends the row. Runs break at the group headings and
    // "Customize Advice" buttons between them, so each group still gets its own table.
    private static void ConsolidateAdviceRows(List<RenderItem> items)
    {
        int i = 0;
        var runs = new List<(int start, int count, List<RenderItem> replacements)>();
        while (i < items.Count)
        {
            if (!IsAdviceSettingStop(items[i])) { i++; continue; }

            int start = i;
            var table = new AdviceTableItem();
            var row = new AdviceRow();
            bool frequency = false;
            // Index just past the dropdown that closed the last complete row; everything after it
            // is a fragment with no control of its own.
            int afterLastRow = start;
            while (i < items.Count && IsAdviceSettingStop(items[i]))
            {
                var s = (StopItem)items[i];
                var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
                if (ServerText.SafeRole(s.Element) == "dropdown")
                {
                    row.Value = s.Element;
                    // The common field-label pass already folded the native
                    // caption into this dropdown and removed its duplicate text
                    // stop. Restore that same caption as the table row header,
                    // before the renderer names the value by its column. A sole
                    // remaining paragraph is its description, not the title.
                    if (s.Element is Adapters.PluginAdapterBase native)
                    {
                        var folded = native.FoldedTexts();
                        var caption = folded.Count == 1 ? CleanOrNull(folded[0]) : null;
                        if (caption != null && row.Title == null) row.Title = caption;
                        else if (caption != null && caption != row.Title && row.Description == null)
                        {
                            row.Description = row.Title;
                            row.Title = caption;
                        }
                    }
                    if (backing != null && AncestorOrSelfNamed(backing, AdviceFrequencyDropdownName)) frequency = true;
                    table.Rows.Add(row);
                    row = new AdviceRow();
                    afterLastRow = i + 1;
                }
                else
                {
                    var t = CleanOrNull(DisplayText.Of(s.Element));
                    if (t == null) row.Extras.Add(s.Element);
                    else if (row.Title == null) row.Title = t;
                    else if (row.Description == null) row.Description = t;
                    else row.Extras.Add(s.Element);
                }
                i++;
            }

            if (table.Rows.Count == 0) continue;   // nothing closed a row; leave the run raw
            table.ValueIsFrequency = frequency;

            // A trailing fragment never became a row (nothing closed it): keep those stops raw
            // after the table rather than inventing a valueless row or dropping them.
            var replacements = new List<RenderItem> { table };
            for (int k = afterLastRow; k < i; k++) replacements.Add(items[k]);
            runs.Add((start, i - start, replacements));
        }

        for (int k = runs.Count - 1; k >= 0; k--)
        {
            items.RemoveRange(runs[k].start, runs[k].count);
            items.InsertRange(runs[k].start, runs[k].replacements);
        }
    }

    // A stop inside one of the tool's setting rows: the group blocks lay each setting out as a
    // title, an optional description and a dropdown, all under the group's content frame. Gated on
    // the row frame so the tool's headings, "Customize Advice" buttons and the Undo/Confirm footer
    // stay outside every run.
    private static bool IsAdviceSettingStop(RenderItem item)
    {
        if (item is not StopItem s) return false;
        var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
        if (backing == null) return false;
        string role = ServerText.SafeRole(s.Element);
        if (role != "text" && role != "dropdown") return false;
        return HasAncestorNamed(backing, ResponsibilityRowFrameName)
            || AncestorOrSelfNamed(backing, AdviceFrequencyDropdownName)
            || IsFrequencyRowText(backing);
    }

    // A frequency row's title or description: a text inside a row container, which the two
    // frequency layouts both mark by holding the dropdown as a DIRECT child — either the
    // dropdown's own template ("Player Targets" and its siblings) or the DropdownContainer the
    // round-up row wraps it in. Matching on a direct child rather than any descendant is what
    // keeps the group's heading out: the heading's ancestors reach the rows only through further
    // frames, never as their own children.
    private const string AdviceDropdownContainerName = "DropdownContainer";
    private const int AdviceRowSearchDepth = 5;

    private static bool IsFrequencyRowText(VisualElement el)
    {
        var frame = el.hierarchy.parent;
        for (int depth = 0; frame != null && depth < AdviceRowSearchDepth; depth++, frame = frame.hierarchy.parent)
        {
            int count = frame.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                string name = frame.hierarchy[i].name;
                if (name == null) continue;
                if (name == AdviceDropdownContainerName || name.StartsWith(AdviceFrequencyDropdownName, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    private static bool AncestorOrSelfNamed(VisualElement el, string name)
        => el.name == name || HasAncestorNamed(el, name);

    // The Delegation tab's group list: the streamed list inside the delegation content that holds
    // the groups themselves, not one group's duty list (which sits inside it under its own name).
    // The Overview tab's duty list. Its staff picker is a streamed list of radios inside the same
    // content, so that one is excluded by its radio group.
    private static bool IsStaffDutyList(VisualElement el)
        => HasAncestorNamed(el, ResponsibilityOverviewRemapperName)
           && !HasAncestorNamed(el, ResponsibilityRadioGroupName);

    private static bool IsDelegationGroupList(VisualElement el)
        => el.name != ResponsibilityRowListName
           && HasAncestorNamed(el, ResponsibilityDelegationRemapperName)
           && !HasAncestorNamed(el, ResponsibilityRowListName);

    // The team-selection layout (the Tactics Planner and the full-screen selection share it) runs
    // pitch, subs strip and selection table into one flat run under the panel heading. Give the two
    // unlabelled regions their landmarks — "Pitch View" before the phase tablist, "Team Selection"
    // before the table's view dropdown — and, where a profile leaves the subs strip standing as
    // just its header (its chips hidden), move that header and count below the table so the bench
    // summary reads as a footnote instead of interrupting the path to it. Gated on the table plus
    // the subs section existing in the tree — the layout's signature, read from the panel rather
    // than from surviving stops, because a profile that hides the strip outright (the planner's
    // does: every slot is already a table row) would otherwise take these landmarks with it. Every
    // anchor is structural and any missing piece skips its own step. Labels are the game's own
    // terms for these regions.
    private const string PitchViewHeadingLabel = "Pitch View";
    private const string TeamSelectionHeadingLabel = "Team Selection";
    private const string TacticHeadingLabel = "Tactic picker";
    private const string TacticSettingsHeadingLabel = "Tactic settings";
    private const string SubsSectionNameFragment = "sub section";

    private static void RestructureTeamSelectionLayout(List<RenderItem> items, object panelObj)
    {
        RenderItem table = null, tabBar = null;
        StopItem viewDropdown = null, tacticPicker = null, styleButton = null;
        var stripItems = new List<RenderItem>();
        bool chipsVisible = false;
        foreach (var item in items)
        {
            if (table == null && item is TableItem) { table = item; continue; }
            if (tabBar == null && item is TabBarItem) { tabBar = item; continue; }
            if (item is not StopItem s) continue;
            var backing = (s.Element as Adapters.PluginAdapterBase)?.BackingElement;
            if (backing == null) continue;
            if (AncestorNameContains(backing, "sub section"))
            {
                stripItems.Add(s);
                if (AncestorNameContains(backing, "FMDraggableObject")) chipsVisible = true;
            }
            else if (viewDropdown == null && backing.name == "ViewDropdown")
            {
                viewDropdown = s;
            }
            else if (tacticPicker == null && AncestorNameContains(backing, "tactics-picker"))
            {
                tacticPicker = s;
            }
            else if (styleButton == null && AncestorNameContains(backing, "style-split-button"))
            {
                styleButton = s;
            }
        }
        if (table == null) return;
        if (panelObj is not VisualElement panel || !HasDescendantNameContaining(panel, SubsSectionNameFragment)) return;

        if (stripItems.Count > 0 && !chipsVisible)
        {
            foreach (var it in stripItems) items.Remove(it);
            int after = items.IndexOf(table) + 1;
            for (int i = 0; i < stripItems.Count; i++) items.Insert(after + i, stripItems[i]);
        }

        // Literal tag depth (RenderHeadingItem emits <h{Level}>): 3 nests these regions under the
        // panel's own <h2>, level with the profile-promoted Substitutes heading.
        if (tacticPicker != null)
            items.Insert(items.IndexOf(tacticPicker), new HeadingItem { Text = TacticHeadingLabel, Level = 3 });

        // Style, Mentality, the gauges and the match-prep actions run from the style split button
        // to the pitch tablist; the style button is the region's stable front edge.
        if (styleButton != null)
            items.Insert(items.IndexOf(styleButton), new HeadingItem { Text = TacticSettingsHeadingLabel, Level = 3 });

        if (tabBar != null)
            items.Insert(items.IndexOf(tabBar), new HeadingItem { Text = PitchViewHeadingLabel, Level = 3 });

        RenderItem tableAnchor = viewDropdown ?? table;
        items.Insert(items.IndexOf(tableAnchor), new HeadingItem { Text = TeamSelectionHeadingLabel, Level = 3 });
    }

    private static bool HasDescendantNameContaining(VisualElement root, string fragment)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (child.name != null && child.name.Contains(fragment)) return true;
            if (HasDescendantNameContaining(child, fragment)) return true;
        }
        return false;
    }

    private static bool AncestorNameContains(VisualElement el, string fragment)
    {
        for (var p = el; p != null; p = p.hierarchy.parent)
            if (p.name != null && p.name.Contains(fragment)) return true;
        return false;
    }

    private static bool IsResponsibilitiesLink(IAccessibleElement el)
        => (el as Adapters.PluginAdapterBase)?.BackingElement?.name == "responsibilities-button";

    private static bool IsTabSlotButton(IAccessibleElement el)
    {
        if (ServerText.SafeRole(el) != "button") return false;
        var backing = (el as Adapters.PluginAdapterBase)?.BackingElement;
        if (backing == null) return false;
        for (var p = backing.hierarchy.parent; p != null; p = p.hierarchy.parent)
            if (p.name == "ExtraTabTemplate") return true;
        return false;
    }

    private static bool IsContinueButton(IAccessibleElement el)
        => (el as Adapters.PluginAdapterBase)?.BackingElement?.name == "continue-button";

    private static bool HasAncestorNamed(VisualElement el, string name)
    {
        for (var p = el.hierarchy.parent; p != null; p = p.hierarchy.parent)
            if (p.name == name) return true;
        return false;
    }

    private static bool IsDateWidget(IAccessibleElement el)
    {
        var backing = (el as Adapters.PluginAdapterBase)?.BackingElement;
        if (backing == null) return false;
        for (var p = backing; p != null; p = p.hierarchy.parent)
            if (p.name == "CurrentDayWidget") return true;
        return false;
    }

    // Walk an element's CHILDREN as if they were top-level content (fresh parent classification),
    // skipping the element itself. Used to expand a composite that the classifier would otherwise
    // collapse into a single stop — the SpeakTo speech-bubble button, whose children hold the
    // dialogue and response options (see SpeakToRenderer). Starting each child at a null parent is
    // what keeps them from being treated as the composite button's internal children and dropped.
    internal static List<RenderItem> WalkChildren(VisualElement root)
    {
        var items = new List<RenderItem>();
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            WalkRec(root.hierarchy[i], null, items);
        return items;
    }

    private static void WalkRec(VisualElement el, ElementClassification? parentClassification, List<RenderItem> items)
    {
        if (!ElementBridge.IsVisible(el)) return;

        // Read the UXML name once: every el.name is an IL2CPP marshal plus a fresh managed string,
        // and the name checks below evaluate ~20 of them for a typical fall-through node — ~50k
        // marshals per /body on a large screen. Same precedent as the il2cppType hoist further down.
        var elName = el.name;
        if (elName == "short-summary-table" || el.hierarchy.childCount == 2
            && el.hierarchy[1].GetIl2CppType().Name == "StreamedObjectList")
        {
            var setupTable = GameCreationSummary.TryTable(el, child =>
            { var parts = new List<RenderItem>(); WalkRec(child, null, parts); return parts; });
            if (setupTable != null) { items.Add(setupTable); return; }
        }
        if (elName is "purpose_block" or "topic_block" or "squad_header" or "squad_block"
            && MessageContentGroups.TryEmit(el, items, child =>
            { var parts = new List<RenderItem>(); WalkRec(child, null, parts); return parts; })) return;
        // The native capture SIText is intentionally empty while waiting. Intercept
        // before the ordinary blank-text pruning so the keyboard field stays present.
        if(elName=="action-shorcut-test"&&Adapters.FM27ShortcutCaptureAdapter.IsCapture(el))
        {items.Add(new StopItem{Element=new Adapters.FM27ShortcutCaptureAdapter(el)});return;}
        if(elName is "Attributes" or "Positions" or "Other" or "LineupList" or "StreamedObjectList_Fixtures" or "Frame" or "LiveTable" or "KeyEvents" or "PlayerRatings"
            || el.GetIl2CppType().Name=="StreamedObjectList")
        {
            var formTable=FM27FormTable.TryBuild(el,cell=>{var result=new List<RenderItem>();WalkRec(cell,null,result);return result;});
            if(formTable!=null)
            {
                if(elName=="StreamedObjectList_Fixtures"||el.GetIl2CppType().Name=="StreamedObjectList")
                    ListWindowSurface.AppendChrome(items,el,ListWindowSurface.Labels.Generic);
                if(elName=="LiveTable")
                {
                    var list=FindDescendantOfType(el,"StreamedObjectList");
                    if(list!=null)ListWindowSurface.AppendChrome(items,list,ListWindowSurface.Labels.Generic);
                }
                items.Add(formTable);return;
            }
        }
        if(el.GetIl2CppType().Name=="GridSelector"&&FM27ColourSurface.TryPalette(el,items))return;
        if(el.GetIl2CppType().Name=="ColourPicker")items.Add(new StopItem{Element=new FM27ColourSurface.Current(el.Cast<SI.Bindable.ColourPicker>())});

        // The schedule calendar's weekday header row (DayNames: seven SIText "Monday".."Sunday", a
        // sibling of the "Weeks" grid) is redundant on the HTML surface, where the schedule table
        // (EmitCalendar) emits its own weekday column headers. Skipping it here drops the flat list of
        // weekday names that otherwise renders just above the table. HTML-only and gated on the stable
        // container name; the speech surface keeps the labels (it has no column-header association).
        if (elName == "DayNames")
            return;

        if (elName == "CalendarEventBindingExpect" && TryEmitCompactCalendarEvent(el, items)) return;

        // The title bar's ribbon category buttons (Portal..Career) are retired on the HTML surface:
        // the Menu Treeview below covers every category and subsection in one browsable widget, so
        // the six-button run is pure clutter before it. Both job-state variants of the group. The
        // speech surface keeps the ribbon (its cursor has no tree widget), and the Alt+1..6 chords
        // and /nav still work: they resolve the ribbon buttons from the live element tree, not
        // from the rendered page.
        if (elName == "ManagingClubButtons" || elName == "ManagingNationButtons")
            return;

        // The Data Hub analysis cards' scatter plot. Every plotted point is a bare club or player
        // crest button positioned by its two axis values — no text anywhere, the identity and the
        // numbers live only in the binding behind an invisible hover tooltip. Walked raw it emits
        // one "unlabeled icon button" per point (seventeen on the Crossing card) that lead nowhere
        // and say nothing. A team scatter reads as the highlighted team's standing on both measures
        // (DataHubScatter, from the card's record); otherwise announce the chart honestly and drop
        // the points. The axis labels are siblings of this container, so they still read. HTML-only.
        if (elName == ScatterChartContainer)
        {
            if (!DataHubScatter.TryBuild(el, items))
                items.Add(new NoteItem { Text = ScatterChartNote });
            return;
        }

        // The recent-form strip (the Opposition Report's Upcoming Match tile): one coloured bar per
        // recent result, with the result itself carried only by the bar's fill. Walked raw it emits
        // nothing at all, so its "Form" heading reads as an empty section indistinguishable from a
        // team with no form. State the gap instead.
        if (elName == RecentFormContainer)
        {
            items.Add(new NoteItem { Text = RecentFormNote });
            return;
        }

        // A chart legend ("attributes key"). The table built from the chart just before it normally
        // names every series it lists (a radar's columns, RadarSeriesReader; the xG story's teams,
        // LastMatchCharts), and then the legend has nothing left to tell: skip it. When a radar's
        // read failed its table holds only the printed series, so state that the rest are drawn
        // alone; the note is a radar's only, since other charts share this legend template. Fall
        // through so the legend's names still read.
        if (elName == RadarLegendContainer)
        {
            if (items.Count > 0 && items[^1] is SyntheticTableItem chartTable
                && (chartTable.NamesLegend || RadarSeriesReader.HeadersNameLegend(chartTable.Headers, el)))
                return;
            if (RadarSeriesReader.HasRadarNearby(el))
                items.Add(new NoteItem { Text = RadarLegendNote });
        }

        // The Player Comparison card draws its two players side by side, and every tab splits into
        // a half per player: the Overview's LHS/RHS frames, the Positions & Roles tab's
        // Left/RightPlayer frames. Nothing inside either half names the player it belongs to — the
        // names live only in the two picker dropdowns at the top of the card — so read
        // sequentially the halves are two identical runs of "Coach Report / Season Stats /
        // positions" with no owner. Worse, the card mirrors the right half, so its blocks come in
        // the opposite order and the reader cannot even infer ownership from position. Head each
        // half by which picker it belongs to, matching the "Player 1" / "Player 2" columns of the
        // attribute and statistics tables. Positional, not a claim about identity: player one is
        // whoever the first picker holds. Panel-scoped, and falls through so the half's own
        // content still reads.
        if ((elName is ComparisonLeftHalfFrame or ComparisonLeftHalfPositions
             or ComparisonRightHalfFrame or ComparisonRightHalfPositions)
            && ElementBridge.IsInPanel(el, ComparisonPanelName)
            // Except on Positions & Roles, whose two halves are folded into one table naming both
            // players in its columns — a per-player heading there would head nothing.
            && !AncestorNameContains(el, ComparisonPositionsTab))
        {
            bool left = elName is ComparisonLeftHalfFrame or ComparisonLeftHalfPositions;
            items.Add(new HeadingItem { Text = left ? ComparisonPlayerOneHeading : ComparisonPlayerTwoHeading, Level = 3 });
        }

        // FM27's World dropdown browses clubs/competitions/continents/nations;
        // the separate global Search button retains its native search action.

        // The title bar's secondary nav (the selected category's section tabs, once headed
        // "<category> sections") is replaced in place by the Menu Treeview: the same browse tree
        // as the Navigator modal (F1), covering every category, subsection, and dropdown child —
        // not just the selected category's row. Fall through (no return): the bookmarks strip is
        // a child of this same bar and must keep walking out below the tree, unchanged; only the
        // tabs subtree (next check) is dropped. HTML-only; the speech surface keeps the real tabs.
        if (elName == "SecondaryNavigationBar")
        {
            items.Add(new StopItem { Element = new NavigatorSurface.MenuTreeAdapter() });
        }

        // Main-category sections are already in the Menu Treeview. Entity
        // reports use a different, contextual subtab list with a null main tab
        // (players, clubs, competitions, staff). Keep those real buttons and
        // dropdowns: the category map does not contain their destinations or
        // their native menu actions. The original generic adapters own them.
        if (elName == "SecondaryNavigationTabs" && Adapters.BookmarkBar.Module()?.m_currentMainTab.Value != null)
            return;

        // The game's bookmark strip is retired on the HTML surface: the Ctrl chords open MOD
        // bookmarks (ModBookmarks, edited below), so the strip's buttons are clutter the page
        // no longer needs. HTML-only — the speech cursor keeps the real strip and the game's
        // own Shift+N shortcuts still work in-game.
        if (elName == "BookmarksList")
            return;

        // The native favorites popup floats outside every panel. Use the existing
        // floating-popup surface for the actual game bar and retain the inherited
        // companion Ctrl-shortcut editor as its separate, explained operation.
        if (elName == "BookmarksPopupWidget")
        {
            items.Add(new StopItem { Element = new FilterPopupSurface.TriggerAdapter(el.Cast<SI.Bindable.SIFloatingPopupWidget>()) });
            items.Add(new StopItem { Element = new BookmarkEditorSurface.BookmarkEditorTriggerAdapter() });
            return;
        }

        // The Coach Assignments footer: two per-category summary rows (Coach Workload, Lead Coach
        // Rating) drawn as loose frames below the assignments table, one value column per training
        // category. The generic walk linearizes them into a run of "Average" / "N stars" text
        // stops with no category attached. Consolidate into one semantic table whose row names
        // are the assignment table's own category headers. A failed read falls through to the
        // generic recursion so the raw values still render. See HtmlRenderer.RenderColumnSummary.
        if ((elName == "BindingExpect" || el.GetIl2CppType().Name == "BindingExpect") && HasChildNamed(el, "frame0"))
        {
            try
            {
                if (TryEmitColumnSummary(el, items)) return;
            }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Column summary consolidation failed, recursing as plain frames: {ex.Message}"); }
        }

        // The tactics pitch has two stacked widgets that swap by the shape toggle: the editable
        // single-shape pitch ("interactable-pitch-expanded", shown for In/Out of Possession) and a
        // read-only Combined pitch ("static-pitch-expanded", shown for Combined). The Combined pitch
        // fuses both phases' role badges per player rather than the single-shape indicators our player
        // blocks read, so we expose no reader for it. The walk skips hidden elements (above), so
        // reaching a visible static-pitch-expanded IS the "Combined selected" signal: drop a pointer to
        // the editable shapes in its place. HTML-only — the speech surface is unaffected. See
        // docs/position-changing.md "Pitch View Modes".
        if (elName == "static-pitch-expanded")
        {
            items.Add(new NoteItem { Text = "Select in or out of possession to see pitch view here" });
            return;
        }

        // The tactics Team Instructions tile grid (header + phase dropdown + per-phase tiles).
        // Consolidated into one editable table (phase, instruction, a select per row) read from the
        // sibling phase-summary lists, which cover every phase of the active tab at once — see
        // Features/TeamInstructions.cs. A failed read falls through to the generic tile rendering,
        // and the summary skip below then keeps the summary cells too, so nothing is lost.
        if (elName == "team-instruction-tiles" || elName == "TeamInstructionsBlock"
            && el.GetIl2CppType().Name == "BindableVariableSizedGridElement")
        {
            try { if (TeamInstructions.TryEmit(el, items)) return; }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Team instructions consolidation failed, recursing as tiles: {ex.Message}"); }
        }

        // The phase-summary lists the table above was read from: every row is already in the
        // table, so rendering them too would put each instruction on the page twice. Skipped only
        // when the table actually emitted (tree order guarantees the tiles anchor walks first).
        if (elName is "PhasesTIsVisible" or "PhasesAndTeamInstructions")
        {
            for (int i = items.Count - 1; i >= 0; i--)
                if (items[i] is TeamInstructionsItem) return;
        }

        // Resolve the IL2CPP type once: the pitch-container, radio-group, and line-chart checks below
        // plus Describe all key off it, so caching it here avoids ~4 GetIl2CppType() marshals per node.
        var il2cppType = el.GetIl2CppType();
        var elTypeName = il2cppType.Name;

        // The search screens' result count, which the layout draws as a big number above its caption
        // ("10" / "Players Found (Filtered)") and the walk therefore emits as two stops on two lines,
        // the bare number arriving with nothing to say what it counts. One line instead.
        //
        // Both halves stay their own live-tracked span rather than being flattened into one string:
        // the caption is as dynamic as the number ("Players Found" gains its "(Filtered)" from the
        // filter state), so baking either into text would leave it stale between body refreshes.
        //
        // Gated on the shape, not the panel: this frame name is specific enough that any screen
        // drawing it wants the same treatment, and an unexpected shape falls through to the raw
        // stops rather than joining something that is not a count and its caption.
        if (elName == PlayersFoundFrame)
        {
            var parts = new List<RenderItem>();
            int partCount = el.hierarchy.childCount;
            for (int i = 0; i < partCount; i++) WalkRec(el.hierarchy[i], null, parts);

            var joined = new JoinedTextItem();
            foreach (var part in parts)
                if (part is StopItem stop && ServerText.SafeRole(stop.Element) == "text")
                    joined.Parts.Add(stop.Element);

            if (joined.Parts.Count == parts.Count && joined.Parts.Count > 1) items.Add(joined);
            else items.AddRange(parts);
            return;
        }

        // The Coach Report's right-hand column: the position picker, then that position's statistical
        // assessment, then the squad comparison (which titles itself). The game titles the column
        // nothing, so read sequentially the picker and the assessment land under whatever heading
        // came last — the Cons list — and appear to belong to it. One heading over the whole column
        // so the picker sits with the analysis it drives. Emitted before the walk descends, so it
        // opens the section rather than interrupting it.
        if (elName == RadarSectionColumn && ElementBridge.IsInPanel(el, CoachReportPanel))
            items.Add(new HeadingItem { Text = RadarSectionHeading, Level = RadarSectionHeadingLevel });

        // The Coach Report's statistics block opens with the selected position as a caption ("Defender
        // (Center)"), the value the Position dropdown just above already announces. It is the first of
        // two sibling frames under the chart section, the second holding the competition line and the
        // summary, and both texts share one path, so the caption is told apart by its frame's place.
        if (elName == RadarCaptionFrame && ElementBridge.IsInPanel(el, CoachReportPanel) && IsCoachRadarCaption(el))
            return;

        // The Coach Report's "Season Stats" block (in the radar's place for a player without league
        // figures): a headline stat, a ring gauge and label/value pairs, read as one Statistic | Value
        // table (CoachSeasonStats). Any other shape walks as before.
        if (elName == SeasonStatsBlock && ElementBridge.IsInPanel(el, CoachReportPanel) && TryAppendSeasonStats(el, items))
            return;

        // The Coach Report's statistics radar. The Data Hub's radar binds a "Value" beside every
        // axis label and reads as a stat table (SyntheticTables, FMPolarChart); this one prints no
        // figure at all, but its binding holds both series the polygons draw: the player and the
        // average for the role (RadarSeriesReader; the same chart type, verified 2026-10-04 against the
        // Data Hub's figures for the same player). They read as a table, then the chart's own summary
        // prose (AppendRadarSummary), which the card binds but never shows. A radar whose binding does
        // not read keeps the summary alone.
        //
        // Scoped to the panel this was established on: elsewhere a radar's figures may reach the
        // page by another route.
        if (elTypeName == "FMPolarChart"
            && FindDescendantNamed(el, RadarAxisValueElement) == null
            && ElementBridge.IsInPanel(el, CoachReportPanel))
        {
            AppendCoachRadarTable(el, items);
            AppendRadarSummary(el, items);
            return;
        }

        // A Player Instructions tile grid - the card's active tab, or the editor's single content
        // grid, whose cells are the option radios, the details block, and every instruction's tile
        // as an in-place switcher. Generic rendering with a fixed reading order and a landmark
        // over the tiles (see Features/PlayerInstructions.cs). The card's grid is matched by its
        // tab-specific name ("PlayerInstructionGroups<Tab>TileCompositionSet" - shared with the
        // tab BUTTON's template wrapper, so the type gates the match to the grid itself); the
        // editor's grid is unnamed, so there the panel identifies it. A failed read falls through
        // to the plain recursive walk.
        if (elTypeName == "ScalableBindableGridLayoutElement"
            && ((elName != null && elName.StartsWith("PlayerInstructionGroups", StringComparison.Ordinal))
                || ElementBridge.IsInPanel(el, PlayerInstructions.EditPanelName)))
        {
            try { if (PlayerInstructions.WalkGrid(el, items, child => WalkRec(child, null, items))) return; }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Player instructions grid walk failed, recursing generically: {ex.Message}"); }
        }

        // The phase-selector pitch of the Team Instructions section (a diggable wrapping
        // FMTacticsWithPhases): its only job was re-pointing the tile grid at a phase, which the
        // consolidated table made moot (every phase is on the page at once), so its expandable
        // stop - a label packing all eleven player names - is pure clutter. Dropped even when the
        // table fell back to tiles: the phases dropdown and the summary's own Select buttons still
        // cover phase switching there. HTML-only - the speech surface keeps the pitch.
        if (elTypeName == "DiggableVisualElement" && HasChildNamed(el, "FMTacticsWithPhases"))
            return;

        // Any OTHER floating-popup widget (the named BookmarksPopupWidget returned above) — a table
        // Filter menu and its siblings. Same treatment as the bookmark trigger, but generic: a
        // synthetic button labelled with the widget's own caption toggles the game's popup, and
        // while open ComposeBody renders only the popup section, modal (FilterPopupSurface).
        // Without this the widget's caption walks out as inert text and the popup is unreachable.
        // Returning drops only that caption — the trigger stop carries it as its label.
        if (elTypeName == "SIFloatingPopupWidget")
        {
            items.Add(new StopItem
            {
                Element = new FilterPopupSurface.TriggerAdapter(el.Cast<SI.Bindable.SIFloatingPopupWidget>()),
            });
            return;
        }

        // The match scoreboard holds only display text (clock, score, team names) and no controls.
        // Collapse its many stale SIText stops into one consolidated stop read live from the engine,
        // in place. It carries the #fmscore id so the SSE 'score' event keeps it ticking between
        // body refreshes. The scoreboard is a PER-COMPETITION template (the game's ScoreboardFactory
        // loads a different UXML per competition into the ScoreBug slot's DynamicItem, with different
        // internal names — "scorebar" is only the UCL family's), so anchor on the stable outer
        // structure: the DynamicItem under ScoreBug whose subtree holds a recognised team-name pair.
        // The legacy "scorebar" name is kept as a direct match. Match-control buttons (tactics,
        // playback) live outside this block and stay navigable.
        // The parts of a responsibility group the swept table owns: the group's name (its Group
        // column), its bulk buttons (rendered from the table with re-realising handles) and its
        // duty list (the table's rows). Skipped here so they are not also walked out as loose
        // stops beside the table. Everything else in the group still walks.
        if ((elName == ResponsibilityGroupNameElement || elName == ResponsibilityAssignAllBlockName
             || elName == ResponsibilityRowListName)
            && HasAncestorNamed(el, ResponsibilityDelegationRemapperName))
            return;

        // The half-time/full-time ribbon (ContinuePanel) loads the same per-competition scoreboard
        // template outside any ScoreBug, wrapped in a "BindingExpect_Scoreboard_<competition>";
        // walked plainly it read as five loose texts (clock, names, scores) under the page's own
        // score heading. Same team-name gate, so a non-scoreboard never matches.
        if (elName == "scorebar"
            || (elTypeName == "DynamicItem" && HasAncestorNamed(el, "ScoreBug") && MatchWatcher.HasTeamNames(el))
            || (elTypeName == "BindingExpect" && elName != null && elName.StartsWith("BindingExpect_Scoreboard_", StringComparison.Ordinal)
                && MatchWatcher.HasTeamNames(el)))
        {
            items.Add(new ScoreboardItem { Line = MatchWatcher.ComposeScoreLine(el) });
            return;
        }

        // The match header's stats widget (revealed by the score bug's Stats toggle) is a tile
        // CAROUSEL: several pages (Match Stats, Match Momentum, xG graph story, Latest Scores,
        // Live Table, ...), each wrapped whole in one tile-interaction SIButton, with a
        // dot-per-page indicator strip. The generic walk collapsed the visible page into one
        // opaque button — every stat crammed into its description — and dropped the dots, so the
        // sighted glance (five live key stats) and the other pages were unreachable. Consolidate:
        // the tile title as a heading that still opens the full card, the stat rows as a table
        // whose value cells are live-tracked, the hidden pages as buttons driving the game's own
        // dots. A failed or unrecognised read falls through to the generic recursion, so the
        // opaque-button behavior is the worst case. HTML-only — the speech surface keeps the
        // single tile-button stop. See HtmlRenderer.RenderCarouselTile.
        if (elTypeName == "BindableCarouselTileRoot")
        {
            try { if (TryEmitCarouselTile(el, items)) return; }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Carousel tile consolidation failed, recursing as a plain tile: {ex.Message}"); }
        }

        // The match report screens (half-time, full-time) place the same tiles STANDALONE, not in
        // a carousel — the match events list, a stats tile, latest scores, the live table — each
        // still one whole-tile SIButton the generic walk would collapse into a packed opaque
        // label. Recognised by their stable wrapper names, they get the same reading as a
        // carousel page: title as a heading that still opens the tile's card, stat rows as a
        // live table, other content as plain lines. Unrecognised shape falls through.
        if (elTypeName is "BindingRemapper" or "BindableVariableSizedGridElement"
            && elName is "MatchEvents" or "MatchStatsStandAlone" or "LatestScores" or "LiveTable" or "LiveLeagueTable"
                or "MatchMomentum" or "xGStory")
        {
            try { if (TryEmitStandaloneTile(el, items)) return; }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Standalone tile consolidation failed, recursing as a plain tile: {ex.Message}"); }
        }

        // FM27's shared match-statistics block replaces the old Data-Frame with
        // three columns of the same labelled home/away bars. Feed the original
        // live-stat table renderer; a bar's display ratio is not another statistic.
        if (elName == "statsBox" && el.parent?.name == "statsOnlyView"
            && TryEmitMatchStatsBox(el, items)) return;
        if (elName == "MatchStats")
        {
            var stats = FindDescendantNamed(el, "Stats");
            var title = CleanOrNull(ReadFirstTextUnder(el, "MatchStatsTitle"));
            if (stats != null && title != null && TryEmitMatchStatsBox(stats, items, title)) return;
        }

        // The same reports' result banner: every fact as "label: value" (see MatchResultHeader).
        if (elTypeName == "BindingRemapper" && elName == MatchResultHeader.WrapperName)
        {
            try { if (MatchResultHeader.TryEmit(el, items)) return; }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Match result header read failed, walking it plainly: {ex.Message}"); }
        }

        // Any FMTacticsPanel-family formation pitch — the editable In/Out-of-Possession pitch and the
        // read-only advice/report cards alike — is read forward into one PitchFormation both surfaces
        // share, instead of one player stop per shirt button. This is what carries the formation in
        // contexts where the whole pitch is wrapped in a single card-button (the suggested-lineup
        // message), where the old shirt-button consolidation found nothing and dropped it entirely.
        // Editable actions ride along only on the interactable pitch; every other context is read-only.
        // The Combined static pitch is handled by the placeholder above (it fuses two roles per player,
        // which this single-role reader would render incompletely), so it never reaches here.
        // A position-PICKER pitch (Recruitment Focus step 1, the transfer advert) carries filter
        // toggles, not players, so ReadFormation below finds nothing and the slots would walk out as
        // a run of bare, mutually indistinguishable checkboxes. Consolidate it into the same 6x5
        // geometry as a table whose headers say which slot each toggle is. Nothing readable declines,
        // leaving the raw checkboxes — a picker with no reachable positions is worse than an ugly one.
        if (Tactics.PitchGrid.IsPitchContainer(el, elTypeName)
            && Adapters.PitchPositionAdapter.IsPositionPickerPitch(el))
        {
            var toggles = PositionPicker.ReadToggles(el);
            if (toggles.Count > 0)
            {
                items.Add(new PositionGridItem { BySlot = toggles, Phase = SelectedTabLabel(items) });
                return;
            }
        }

        // The Opposition Report's Squad Depth tab lays a DEPTH CHART on the same pitch widgets a
        // formation uses: each slot holds the players who can fill that position, in depth order,
        // under a position/role/count header. Read as a formation it becomes an eleven the game
        // never named (the first name of each depth list, so one player "starts" at three
        // positions at once) — so it is recognised first, by the depth header, and read as what it
        // is. An unreadable shape falls through to the raw stops rather than a half-built chart.
        if (Tactics.PitchGrid.IsPitchContainer(el, elTypeName)
            && Tactics.SquadDepthPitch.IsDepthPitch(el))
        {
            if (TryEmitSquadDepth(el, items)) return;
        }

        if (Tactics.PitchGrid.IsPitchContainer(el, elTypeName))
        {
            var formation = Tactics.PitchGrid.ReadFormation(el);
            if (!formation.IsEmpty)
            {
                var phase = TacticsPitchPhaseCaption.Read(el, out var phaseScope);
                items.Add(new PitchGridItem
                {
                    Pitch = el,
                    PhaseCaption = phase,
                    PhaseScope = phaseScope,
                    Formation = formation,
                    Editable = Tactics.PitchGrid.IsEditablePitch(el) ? BuildEditableMap(el) : null,
                });
                return;
            }
            // An empty FMTacticsPanel (no occupied slot read) is not a formation to render; fall
            // through so any real content inside it still walks out rather than vanishing.
        }

        // The Calendar tool's Fixture Schedule tab: a column-header row of sort buttons over a
        // streamed list of month-grouped fixture rows. Left alone, the headers walk out as loose
        // buttons and the virtualised "FixturesList" underneath falls into the competition-fixtures
        // consolidation below, which finds no match tiles and swallows the season into an empty
        // table. Consolidate the wrapper into one semantic table instead: the game's own sortable
        // column headers, one row per fixture, cells read via the streamed protocol so the whole
        // season renders, not just the realized window. See HtmlRenderer.RenderFixtureSchedule.
        if (elName == "Fixtures" && Contexts.FixtureScheduleContext.IsScheduleWrapper(el))
        {
            EmitFixtureSchedule(el, items);
            return;
        }

        // The competition fixtures screen renders each stage's matches as a deep tree of nested
        // streamed lists, scattering status, teams, score and scorers across dozens of stray text
        // stops. Consolidate one stage's match list into a single results table, read live in place,
        // so the player browses it as a table (Status / Home / Score / Away / Scorers) with the team
        // names still clickable. Anchored on the "FixturesList" streamed list; the stage name is its
        // sibling, folded into the table heading. See HtmlRenderer.RenderFixtures.
        if (elName == "FixturesList")
        {
            EmitFixturesTable(el, items);
            return;
        }

        // The Stages panel's cup-round view: a streamed list of bracket ties whose generic walk
        // scatters each tie into loose stops — home team button, "V" button, away team button,
        // per-row competition badges, and the stacked candidate teams of an unresolved previous
        // tie. Consolidate the round into the same table the competition Results screen uses
        // (Home | Score | Away | Status), one row per tie, teams still clickable. See
        // EmitStageRoundTable.
        if (elName == "cup-round")
        {
            EmitStageRoundTable(el, items);
            return;
        }

        // The Add/Remove Leagues card's league grid: a header row (Nation | Men's Leagues | Setting
        // | Women's Leagues | Setting) over a StreamedObjectList of "frame league-row" rows, each
        // packing a nation and mode, its men's/women's rule dropdowns, active-division counts,
        // playable-setting dropdowns and a remove button. Left to the generic walk it linearises into
        // ~90 flat stops with the setting dropdowns mislabelled by the adjacent count. Consolidate
        // into one semantic table, read live in place. Anchored on the container holding the
        // "frame header-row"; a build failure or empty grid degrades to the recursion so no row is
        // ever swallowed. HTML-only. See HtmlRenderer.RenderLeagueTable.
        if (HasChildNamed(el, "frame header-row"))
        {
            EmitLeagueTable(el, items);
            return;
        }

        // An attributes list — a streamed list whose realized rows are clickable attribute rows
        // (the player report's Technical / Set Pieces / Mental / Physical columns) — consolidates
        // into one two-column semantic table (attribute button | value) instead of a flat run of
        // "Crossing 8" buttons. The section-title SIText is a sibling walked just before the list,
        // so the profile's heading rule still lands above the table. An empty or failed collection
        // falls through to the generic recursion so no row ever vanishes. HTML-only — the speech
        // surface keeps its per-row stops.
        if (elTypeName is "StreamedObjectList" or "StreamedObjectListWithChildren"
            or "StreamedObjectList_PlaceTimelineEvents")
        {
            // The same native compact league rows appear in message and press
            // attachments as in the dugout. Keep their original grid semantics
            // and bounded realization, without a screen/caption allowlist.
            MatchReportTiles.Grid compactLeague = null;
            try { compactLeague = MatchReportTiles.TryReadCompactLiveTable(el); }
            catch (Exception ex)
            {
                Log.Warn($"[AccessibilityServer] Compact league rows not ready; keeping ordinary list: {ex.Message}");
            }
            if (compactLeague != null)
            {
                var window = ListWindowSurface.ExpandForTile(el);
                if (window != null) items.Add(new NoteItem { Text = window });
                items.Add(new SyntheticTableItem
                {
                    Headers = compactLeague.Headers, Rows = compactLeague.Rows,
                    RowHeader = compactLeague.RowHeader, RowActions = compactLeague.RowActions,
                });
                return;
            }
            var viewerEvents=MatchReportTiles.TryReadTeamIncidentList(el);
            if(viewerEvents!=null)
            {
                ListWindowSurface.AppendChrome(items,el,ListWindowSurface.Labels.Generic);
                items.Add(new SyntheticTableItem{Headers=viewerEvents.Headers,Rows=viewerEvents.Rows,
                    RowHeader=viewerEvents.RowHeader,RowActions=viewerEvents.RowActions,Provisional=viewerEvents.Provisional});
                return;
            }
            var managerHistory=ManagerHistoryRows.TryRead(el);
            if(managerHistory!=null)
            {
                ListWindowSurface.AppendChrome(items,el,ListWindowSurface.Labels.Generic);
                items.Add(managerHistory);
                return;
            }
            // The news site's "Today's Top Fixtures" rail: each row scatters a competition
            // button, two club buttons and a kick-off time into four flat stops with nothing
            // tying them together. Consolidate the realised rows into one semantic table
            // (Competition | Home | Time | Away) with the buttons still clickable. Only when
            // the list is fully realised — an under-realised window falls through to the
            // paging chrome, the honest rendering of a partial list — and a collection
            // failure falls through to the generic recursion so no row is swallowed. HTML-only.
            if (elName == "TodaysFixturesList" && !ListWindowSurface.IsUnderRealised(el))
            {
                var fixtureRows = TryCollectTodaysFixtures(el);
                if (fixtureRows != null && fixtureRows.Count > 0)
                {
                    items.Add(new TodaysFixturesItem { List = el, Rows = fixtureRows });
                    return;
                }
            }

            // The Player Search condition/attribute list: every realised row is a "Frame-condition"
            // template (enable checkbox, If-or-And/Or connector, name, comparator dropdown, value
            // controls, reorder, delete). Left to the generic walk it linearises into ~7 stops per
            // condition plus paging chrome; consolidate into one semantic table read across frames
            // (all rows, not just the realised window). Anchored on the row template name, so the
            // Conditions and the Attributes views both consolidate. An empty list has no rows to
            // anchor on and falls through (nothing to tabulate). HTML-only. See
            // HtmlRenderer.RenderConditionTable.
            if (FindDescendantNamed(el, "Frame-condition") != null
                && FindDescendantNamed(el, "enabled-checkbox") != null
                && FindDescendantNamed(el, "condition-name-visible") != null)
            {
                items.Add(new ConditionTableItem { List = el, Context = new Contexts.ConditionTableContext(el) });
                return;
            }

            var attrRows = TryCollectAttributeRows(el);
            if (attrRows != null && attrRows.Count > 0)
            {
                items.Add(new AttributeTableItem { List = el, Rows = attrRows });
                return;
            }
            // The Delegation tab's responsibility groups. Emit the whole section as one table read
            // across frames (every group, not just the realised window) and fall THROUGH to the
            // generic recursion, so each group's own preference controls — the training-emails
            // checkbox, the scouting shortlist selects — still walk as themselves. The group name,
            // its bulk buttons and its duty list are skipped further down: the table owns those.
            // No paging chrome, because the sweep reads past the window rather than marking it.
            // Note: a group left unrealised by the walk contributes its duties through the sweep
            // but not its preference controls; in practice only single-group sections carry any.
            if (IsDelegationGroupList(el))
            {
                items.Add(new ResponsibilitiesTableItem
                {
                    List = el,
                    Context = new Contexts.ResponsibilitiesContext(el),
                });
            }
            // The Overview tab's duty list for the selected staff member — same sweep, read-only.
            else if (IsStaffDutyList(el))
            {
                items.Add(new StaffDutiesTableItem
                {
                    List = el,
                    Context = new Contexts.ResponsibilitiesContext(el),
                });
                return;
            }
            // Any other streamed list is virtualised: only the window of items around the scroll
            // position exists in the tree, and the generic recursion below would silently render
            // that window as the whole list (the Injury Table card showed 3 of 18 clubs). Mark the
            // window and offer paging so the absent items are an honest, navigable gap; a fully
            // realised list emits nothing. The schedule calendar ("Weeks", classified Calendar) is
            // exempt: it consolidates into its own month table further down this method.
            else if (elName != "Weeks")
                ListWindowSurface.AppendChrome(items, el, ListWindowSurface.Labels.Generic);
        }

        // The game groups radio buttons under an SIRadioGroup container. Consolidate that group
        // into one RadioGroupItem so the HTML surface renders a real radiogroup (shared name, "N
        // of M", group boundaries) instead of a screen full of disconnected radios. Only when the
        // group holds nothing but radios — anything else falls through to the normal recursion so
        // no content is dropped.
        if (elTypeName == "SIRadioGroup")
        {
            var radioExtras = new List<List<IAccessibleElement>>();
            var radioSubGroups = new List<List<IAccessibleElement>>();
            var radios = TryCollectRadioGroup(el, parentClassification, radioExtras, radioSubGroups);
            if (radios != null)
            {
                // The Recruitment Focus tool's focus list is this same single-select group, but each
                // option carries three values the radio's label runs together — "French National, In
                // Progress, 14" — leaving the count with nothing to say what it counts. Render it as
                // the same shape the Scouting Assignments card's staff list uses: a table with a
                // selection column, so every value sits under a named column. Declines to the plain
                // radiogroup when the row shape does not hold. Taken before the empty check, because
                // an empty focus list is exactly the case that must say something rather than leave
                // the section silent.
                if (ElementBridge.IsInPanel(el, RecruitmentFocusPanel)
                    && TryEmitFocusList(el, radios, radioExtras, radioSubGroups, items))
                    return;

                if (radios.Count > 0)
                {
                    items.Add(new RadioGroupItem
                    {
                        Group = el,
                        Radios = radios,
                        RadioExtras = radioExtras,
                        SubRadios = radioSubGroups,
                        // Recommendation delivery rows derive their subject from a sibling in the
                        // streamed row. Every other radio group keeps master's generic label-folding
                        // path, including its duplicate-stop suppression.
                        Label = Adapters.RecommendationsDeliveryRadioButtonAdapter.GetGroupLabel(el)
                                ?? FoldGroupLabel(el, elName, items),
                    });
                    return;
                }
            }
        }

        // A segmented single-select control: the game builds the Preferences "Mode"
        // (Fullscreen/Windowed) and "Text Scaling" (Small/Normal/Large/Larger) rows from plain
        // SIButtons in a "Mode-Buttons" container with a separate "ttile" label, not a radio group —
        // so they read as a stray label above a row of unlabelled buttons with no indicated
        // selection. Consolidate them into a RadioGroupItem so the HTML surface renders a real
        // radiogroup: the label folds into the fieldset name, each segment becomes a radio, and the
        // selected one (solid background) reports checked.
        if (elName == "Mode-Buttons")
        {
            var segments = CollectSegmentedOptions(el);
            if (segments.Count >= 2)
            {
                string label = FindSegmentedGroupLabel(el);
                // The label was walked as a stray Text stop just before these buttons; relocate it
                // into the group name by dropping that duplicate.
                if (!string.IsNullOrEmpty(label))
                    SuppressFoldedLabelStops(items, new[] { label });
                items.Add(new RadioGroupItem { Group = el, Radios = segments, Label = label });
                return;
            }
        }

        // The Team Selection "Set For" scope toggle (All Matches / Next Match) is the same
        // segmented-single-select shape as the Preferences Mode rows — plain SIButtons whose
        // selected side the game paints with a solid background — but its row label is the
        // container's own leading SIText, not a sibling "ttile". Consolidate it into a radiogroup
        // so the surface reports which scope is selected. Scoped to the "setforContainer" wrapper:
        // other segmented rows (the responsibilities Take Control/Delegate) signal state by
        // disabling a side and keep their plain-button presentation (see the 2026-07-01 revert).
        if (elName == "buttons-segmented-button-row-regular-default"
            && el.hierarchy.parent != null && el.hierarchy.parent.name == "setforContainer")
        {
            var segments = CollectSegmentedOptions(el);
            if (segments.Count >= 2)
            {
                // The leading SIText is inside this container, so it never walked out as a
                // separate stop; folding it into the group name loses nothing.
                items.Add(new RadioGroupItem { Group = el, Radios = segments, Label = ReadLeadingSITextLabel(el) });
                return;
            }
        }

        // The set-piece screens' exclusive pairs — Scenario (Defending/Attacking), Delivery Type
        // (Inswinger/Outswinger) and the instruction popup's category tabs — are this same
        // segmented template, but their live state is a tick child on the selected side (the
        // template's active/inactive names are static, verified live). Consolidate into a
        // radiogroup so the surface reports which option is on. Scoped to the set-piece panels:
        // elsewhere the template signals state differently (the responsibilities rows disable a
        // side; see the 2026-07-01 revert) and keeps its plain-button presentation.
        if (elName == "buttons-segmented-button-row-regular-default" && InSetPiecePanel(el))
        {
            var segments = CollectTickSegmentedOptions(el);
            if (segments.Count >= 2)
            {
                // The row's label ("Scenario", "Delivery Type") is the unpacked wrapper's
                // preceding SIText sibling, already walked as a stray stop; fold it into the
                // group name. The popup's tab row has none and renders unnamed.
                string label = FindSegmentedRowSiblingLabel(el);
                if (!string.IsNullOrEmpty(label))
                    SuppressFoldedLabelStops(items, new[] { label });
                items.Add(new RadioGroupItem { Group = el, Radios = segments, Label = label });
                return;
            }
        }

        // The set-piece pitch preview's Left/Right pair (a Tab-Buttons template, tick child on
        // the selected side) flips which side of the pitch the preview draws. The game names
        // neither button set nor purpose, so a concise mod label says what the pair changes.
        if (elName == "Tab-Buttons" && InSetPiecePanel(el))
        {
            var segments = CollectTickSegmentedOptions(el);
            if (segments.Count >= 2)
            {
                items.Add(new RadioGroupItem { Group = el, Radios = segments, Label = "Preview side" });
                return;
            }
        }

        // The set-piece routine strip (see TryEmitRoutineStrip). A shape mismatch falls through
        // to the generic walk, so the strip never disappears.
        if (elName == "Frame 1000003511" && InSetPiecePanel(el)
            && TryEmitRoutineStrip(el, parentClassification, items))
            return;

        // A set-piece instruction chip: consolidate its whole category's chips into one editable
        // table — slot, instruction select, optional fallback select, move select — instead of
        // the loose run of stops the flat walk gives them (see
        // SetPieceInstructions.TryEmitChipTable). The first chip walked emits the category's
        // table; the rest resolve to it and are consumed. A shape mismatch falls through to the
        // generic walk, so the chips never disappear.
        if (elTypeName == "FMDraggableObject"
            && ElementBridge.IsInPanel(el, SetPieceInstructions.PanelName)
            && FindDescendantNamed(el, "tk-txt") != null)
        {
            try
            {
                if (SetPieceInstructions.TryEmitChipTable(el, items,
                        (child, list) => WalkRec(child, null, list)))
                    return;
            }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Set-piece chip table consolidation failed, walking raw: {ex.Message}"); }
        }

        // A finance projection block (the Projections card's Balance / Expenditure / Profit tiles):
        // a metric title, a graphical projection chart, and three season-end label/value rows in
        // named frames. Consolidate the block into a data item; ConsolidateProjectionBlocks merges
        // the run of them into one metric-by-season table, which also subsumes each block's chart
        // (a monthly interpolation of the same projection the season values summarise). HTML-only.
        // An unreadable block falls through to the generic walk, so its texts and chart marker
        // still render individually rather than vanishing.
        if (elTypeName == "BindingRemapper" && (elName ?? "").EndsWith("Projection"))
        {
            ProjectionBlockItem block = null;
            try { block = TryReadProjectionBlock(el); }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Projection block read failed, walking raw: {ex.Message}"); }
            if (block != null)
            {
                items.Add(block);
                return;
            }
        }

        // The league-history line chart is a graphical plot with no text nodes, so a screen reader
        // finds nothing where it sits (the screen appears to end at the "League History" heading).
        // Extract its season-by-season data points into a semantic table in place. HTML-only — the
        // speech surface still treats the chart as transparent. A chart we can't turn into a table
        // (a read failure, or some other line chart) degrades to an honest marker rather than the
        // silent gap the bare graphic leaves. See LeagueHistoryChartReader / LeagueHistoryHtml.
        // Declarative synthesized tables (SyntheticTables): label/value runs, stat rows, and visual
        // grids configured per panel consolidate into one semantic table in the container's place.
        // HTML-only; a spec whose shape doesn't hold declines and the raw stops walk out below.
        if (SyntheticTables.TryBuild(el, items))
            return;

        // A mentoring group's relationship diagram, whose arrows carry their influence strength in
        // the line style alone, read into an influence matrix. Its graph key is walked first and is
        // dropped only once the diagram beside it proves readable, so an unreadable diagram keeps
        // both its key and its raw stops. See Features/MentoringDiagram.cs.
        if (MentoringDiagram.IsRedundantGraphKey(el))
            return;
        if (MentoringDiagram.TryBuild(el, items))
            return;

        // The Data Hub's Last Match drawings (xG match story, match momentum, pass maps), read into
        // tables from the records they plot. An unreadable drawing walks as before. See
        // Features/LastMatchCharts.cs.
        if (LastMatchCharts.TryBuild(el, items))
            return;

        if (LeagueHistoryChartReader.IsLineChart(el, elTypeName))
        {
            try { if (FinanceChartReader.TryBuildSeasonComparison(el, items)) return; }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Season comparison chart read failed: {ex.Message}"); }
            try
            {
                var ranking = LeagueHistoryChartReader.TryReadWorldRanking(el);
                if (ranking != null)
                {
                    items.Add(new ValueChartItem
                    {
                        Title = FoldPrecedingLabel(items),
                        Series = new List<List<FinanceChartReader.Point>> { ranking },
                        ValueHeaders = new[] { "World ranking" },
                    });
                    return;
                }
            }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] World ranking chart read failed: {ex.Message}"); }
            List<LeagueHistoryRow> rows = null;
            try { rows = LeagueHistoryChartReader.TryRead(el); }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] League history chart read failed: {ex.Message}"); }
            if (rows != null && rows.Count > 0)
            {
                // Fold the chart's section label (the text stop walked just before it) into the table's
                // own heading, mirroring how the fixtures table promotes its stage-name label. Only on
                // success: a degraded chart keeps its label as plain text so the marker stays in context.
                items.Add(new LineChartItem { Title = FoldPrecedingLabel(items), Rows = rows });
                return;
            }

            // Not a league-history chart: try the date-plotted read (the finance cards' balance /
            // income / expenditure / budget histories), rendered as a Date-Value table in the
            // chart's place. No preceding-label fold here — on the finance cards the stop before
            // the chart is a value, not a section label.
            // Every line, not just the first: the progress report's attribute graph plots one per
            // ticked attribute, and which line is which is settled after the streamed tables load
            // (NameChartSeriesFromTickedRows). Until then the item carries them all, unnamed.
            List<List<FinanceChartReader.Point>> chartSeries = null;
            try { chartSeries = FinanceChartReader.TryReadAll(el); }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Date-plotted chart read failed: {ex.Message}"); }
            if (chartSeries != null && chartSeries.Count > 0)
                items.Add(new ValueChartItem { Title = EnclosingHeading(items), Series = chartSeries, Chart = el });
            else
                items.Add(new NoteItem { Text = Degrade.Unreadable + " chart" });
            return;
        }

        // The match-stats comparison card: a "Title" of team names beside a "Content" of stat rows,
        // each row a home value, the stat name, an away value, and a graphical comparison bar. Read in
        // tree order the values lose their team attribution; consolidate the whole block into one
        // semantic table (stat name per row, a column per team) and stop recursing so the raw texts and
        // the team-name header don't also leak as stray stops. A read failure declines here and falls
        // through to the generic walk, which still renders every value as plain text.
        if (MatchStatsTableReader.IsStatsTableFrame(el))
        {
            MatchStatsTable table = null;
            try { table = MatchStatsTableReader.TryRead(el); }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Match stats table read failed: {ex.Message}"); }
            if (table != null && table.Rows.Count > 0)
            {
                items.Add(new StatComparisonItem { HomeTeam = table.HomeTeam, AwayTeam = table.AwayTeam, Rows = table.Rows });
                return;
            }
        }

        // The penalty shootout card. Its scoreboard splits "US Orléans 0 - 1 SC Toulon" across five
        // text leaves either side of two kit-colour swatches, and its result strips carry every
        // outcome as a bare sprite over a background row of round numbers — so walked raw the card
        // reads as loose digits and says nothing about who scored. Fold the scoreboard into one line
        // and the strips into a round-per-row table, stopping the recursion so the raw pieces do not
        // also leak. A read failure declines to the generic walk below.
        if (PenaltyShootoutReader.IsScoreboard(el))
        {
            PenaltyShootoutReader.Scoreboard board = null;
            try { board = PenaltyShootoutReader.TryReadScoreboard(el); }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Shootout scoreboard read failed: {ex.Message}"); }
            if (board != null)
            {
                items.Add(new NoteItem { Text = board.Line });
                return;
            }
        }

        if (PenaltyShootoutReader.IsIndicators(el))
        {
            PenaltyShootoutReader.Rounds rounds = null;
            try { rounds = PenaltyShootoutReader.TryReadRounds(el); }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Shootout indicators read failed: {ex.Message}"); }
            if (rounds != null)
            {
                var teams = PenaltyShootoutReader.TryReadTeams(el);
                items.Add(new StatComparisonItem
                {
                    HomeTeam = teams?.HomeTeam,
                    AwayTeam = teams?.AwayTeam,
                    Rows = PenaltyShootoutReader.BuildRows(rounds),
                });
                return;
            }
        }

        ElementClassification cls;
        try
        {
            var descriptor = ElementBridge.Describe(el, parentClassification, il2cppType);
            cls = ElementBridge.ClassifyDescriptor(descriptor, el);
        }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] Classify failed, skipping subtree: {ex.Message}"); return; }

        // InternalChild is an expected, silent skip.
        if (cls == ElementClassification.InternalChild)
            return;

        // Scouting Assignments, the recruitment focus list: one row per focus assigned to the
        // SELECTED scout, each an icon-only Edit and Delete button beside the focus name. Walked
        // raw the buttons lose their subject — "Edit, Delete, Edit, Delete" with nothing saying
        // which focus a Delete would remove, on a destructive action. Consolidated into a table
        // whose row header names the focus and whose cells hold that focus's own live buttons.
        // Sits after classification so the rows walk under this element's own classification,
        // exactly as they would have on the generic path below.
        if ((elName == ScoutFocusListName || elTypeName == "StreamedObjectList")
            && ElementBridge.IsInPanel(el, ScoutAssignmentsPanel)
            && TryEmitScoutFocusList(el, cls, items))
            return;

        // Info buttons ("<setting> info, click for tooltip") are skipped entirely, mirroring the
        // demotion in ElementBridge.ClassifyElement (this walk classifies via the raw
        // ElementClassifier, so it must repeat the rule). Their tooltip text is only obtainable by
        // showing the game's tooltip, which crashes, so they are dead stops on both surfaces.
        if (cls == ElementClassification.Button && Adapters.InfoButtonAdapter.IsInfoButton(el))
            return;

        // A bare count sibling that ElementBridge folds into its header's TextRunAdapter stop
        // ("Active Men's Nations (15)") would render twice here — mirror the demotion and skip
        // the stray count text.
        if (cls == ElementClassification.Text && elName == "text"
            && ElementBridge.HasSiblingNamed(el, "ActiveNations_Header"))
            return;

        // Unknown mirrors the speech surface's leaf-vs-container rule (TreeWalker): recurse into the
        // unknown's subtree so any real controls inside it still render; only a leaf unknown (nothing
        // rendered from inside) shows as an "unknown control", so the player's HTML surface shows the
        // same honest gap the cursor announces without hiding a container's children.
        if (cls == ElementClassification.Unknown)
        {
            int before = items.Count;
            int kids = el.hierarchy.childCount;
            for (int i = 0; i < kids; i++)
                WalkRec(el.hierarchy[i], cls, items);
            if (items.Count == before)
            {
                try { items.Add(new StopItem { Element = ElementBridge.CreateAdapter(el, cls) }); }
                catch (Exception ex) { Log.Warn($"[AccessibilityServer] Unknown leaf adapter failed, omitted: {ex.Message}"); }
            }
            return;
        }

        // A control that visually encloses a formation pitch — the suggested-lineup card is one big
        // SIButton wrapping the whole pitch — must not collapse into a single opaque stop, or the
        // formation is lost (the blob "IP, OOP, 13, GK, ..." button). Descend instead so the pitch
        // container below is reached and rendered as a grid by the interceptor above. Gated on Button
        // (the only thing that wraps a pitch) and bounded, so ordinary buttons pay almost nothing.
        // Descending drops the wrapper's own click target, though, which on a portal tile IS the
        // action (the tutorial introductions anchor on exactly this button and cannot be advanced
        // without it), so PromoteTileTitleToActivator gives it back as the tile's title heading.
        if (cls == ElementClassification.Button && Tactics.PitchGrid.SubtreeHasPitchContainer(el))
        {
            // Walk the children with a fresh (null) parent classification, not Button: a button's
            // descendants would otherwise classify as InternalChild and be pruned, defeating the
            // descent. This exposes the pitch grid below plus the card's own toggle/info controls.
            int before = items.Count;
            int kids = el.hierarchy.childCount;
            for (int i = 0; i < kids; i++)
                WalkRec(el.hierarchy[i], null, items);
            PromoteTileTitleToActivator(el, items, before);
            return;
        }

        // FM27 reuses the titled whole-tile button in introductions as well as dashboards
        // and mentoring. Its body can contain real tables, player links and ratings; folding
        // those into a button description loses their structure and interactive children.
        // Extend the FM26 mentoring/pitch pattern only for actual rich bodies;
        // ordinary action tiles retain the original button/description contract.
        if (cls == ElementClassification.Button && elName == "layout-tile-interaction-button"
            && !IsCompactValueTile(el)
            && (ElementBridge.IsInPanel(el, MentoringPanel)
                || ElementBridge.FindVisibleTileTitle(el) != null))
        {
            int before = items.Count;
            int kids = el.hierarchy.childCount;
            for (int i = 0; i < kids; i++)
                WalkRec(el.hierarchy[i], null, items);
            if (TryRestorePlainTileButton(el, items, before)) return;
            PromoteTileTitleToActivator(el, items, before);
            return;
        }

        // A ButtonGroup inside a TabGroup is the speech surface's tab concept (ElementBridge
        // hands it a TabContext). Consolidate it into one tablist item rather than letting the
        // walk recurse into individual tab buttons, so the HTML surface presents real tabs with
        // selection. Any other button bar (the ribbon categories, hybrid split buttons) is not a
        // tab group and keeps its plain-button presentation, but is still the game's button-bar
        // concept: its children walk inside a labelled region landmark so a screen reader's
        // landmark navigation (D / Shift+D) jumps to the bar's first button.
        if (cls == ElementClassification.ButtonBar)
        {
            List<Adapters.TabItemAdapter> tabs = null;
            Contexts.TabContext tabCtx = null;
            try
            {
                var ctx = ElementBridge.CreateContainerContext(el, cls, new ClassificationCache());
                tabCtx = ctx as Contexts.TabContext;
                if (tabCtx != null)
                    tabs = tabCtx.GetAllTabs();
            }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Tab bar detection failed, recursing as plain bar: {ex.Message}"); }

            if (tabs != null && tabs.Count > 0)
            {
                items.Add(new TabBarItem { Tabs = tabs, Label = TabBarLabel(el) });

                // Some tab ButtonGroups also hold non-tab content (the staff responsibilities
                // card keeps its under-control/delegated counts and an Advice and Reports button
                // in the tab row). Consolidating the tabs must not swallow it: a direct child
                // whose subtree holds no tab button is ordinary content — keep walking it.
                try
                {
                    var tabEls = tabCtx.GetTabBackingElements();
                    int barKids = el.hierarchy.childCount;
                    for (int i = 0; i < barKids; i++)
                    {
                        var kid = el.hierarchy[i];
                        if (!SubtreeContainsAny(kid, tabEls))
                            WalkRec(kid, null, items);
                    }
                }
                catch (Exception ex) { Log.Warn($"[AccessibilityServer] Non-tab tab-bar content walk failed, tabs kept: {ex.Message}"); }
                return;
            }

            WalkChildrenAsRegion(el, cls, items, ContainerKind.ButtonBar);
            return;
        }

        if (cls == ElementClassification.Table)
        {
            try
            {
                // Headers bind synchronously and are cheap; the row data is streamed and bound
                // asynchronously, so it is read off this main-thread walk by StreamedTableLoader
                // (which spans frames). Carry the context so the loader can drive it.
                var ctx = new Contexts.TableContext(el, new ClassificationCache());
                items.Add(new TableItem
                {
                    Table = el,
                    Context = ctx,
                    Headers = ctx.GetHeaders(),
                    // Walked here (on the main thread) because the loader that needs it to look up
                    // this table's page runs on the request thread.
                    Path = ServerHandles.IdentityPath(el),
                    MultiSelect = ctx.IsMultiSelect(),
                });
            }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Table walk failed: {ex.Message}"); }
            return;
        }

        // The schedule calendar is the game's StreamedObjectList("Weeks"), classified Calendar. Left to
        // the generic recursion it linearizes the whole month into a flat run of day cells and events,
        // losing the week/weekday structure. Consolidate it into a semantic table read live in place:
        // weekday column headers, one row per week, one cell per weekday, each day carrying its date,
        // state and its events (still clickable). HTML-only — the speech surface keeps its own
        // CalendarContext cursor. See HtmlRenderer.RenderCalendar.
        if (cls == ElementClassification.Calendar)
        {
            EmitCalendar(el, items);
            return;
        }

        // The create-schedule editor's week grid. The game declares its own column header row
        // (S1/S2/S3 sessions, then GK/Def/Att unit intensities) and gives each day exactly those
        // six cells, but the flat walk emitted 42 loose paragraphs — and the intensity bars carry
        // no text of their own, so every one of them took the nearest preceding session name and
        // read "Rest: 1%". Consolidated into the table the game already describes.
        if (IsScheduleWeekGrid(el))
        {
            EmitScheduleWeekGrid(el, items);
            return;
        }

        // Image mirrors the Unknown leaf-vs-container rule: a named, non-decorative SIImage is
        // sometimes used by the game as a content container — the WhatsNewDialog's BGImage wraps
        // the headline text and the feature tile buttons, the controller intro's Background wraps
        // the welcome copy and Next button. Recurse first so nested controls and text still render;
        // only a leaf image (nothing rendered from inside) shows as an "image" stop, the same honest
        // marker the cursor announces. Decorative images are already routed to LayoutWrapper by the
        // classifier and recurse via the normal path, so they never reach here.
        if (cls == ElementClassification.Image)
        {
            int before = items.Count;
            int kids = el.hierarchy.childCount;
            for (int i = 0; i < kids; i++)
                WalkRec(el.hierarchy[i], cls, items);
            if (items.Count == before)
            {
                try
                {
                    var adapter = ElementBridge.CreateAdapter(el, cls);
                    if (NavigationManager.IsHidden(adapter))
                        return;
                    items.Add(new StopItem { Element = adapter });
                }
                catch (Exception ex) { Log.Warn($"[AccessibilityServer] Image leaf adapter failed, omitted: {ex.Message}"); }
            }
            return;
        }

        // An attribute row is two side-by-side cells in the game — the attribute name and its
        // value — clickable (own-club players, opening the attribute detail) or plain text
        // (scouted players). The speech surface already reads either as one stop via
        // ElementBridge.ClassifyElement (LayoutWrapper + IsAttributeRow -> Button +
        // AttributeRowAdapter); mirror that here so the HTML surface reads one "Aerial Reach 13"
        // stop rather than two separate "Aerial Reach" / "13" pieces. On failure, fall through to
        // the normal recursion so the row's cells still render individually rather than vanishing.
        if (cls == ElementClassification.LayoutWrapper && Adapters.AttributeRowAdapter.IsAttributeRow(el))
        {
            try
            {
                var adapter = new Adapters.AttributeRowAdapter(el);
                if (NavigationManager.IsHidden(adapter))
                    return;
                items.Add(new StopItem { Element = adapter });
                return;
            }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] AttributeRow consolidation failed, recursing into row: {ex.Message}"); }
        }

        // A standalone "Actions ▾" context-menu trigger (a chevron-bearing PluginContextMenuBindings)
        // is LayoutWrapper by default; promote it to a Button stop so the HTML surface renders it as an
        // activatable control, mirroring ElementBridge.ClassifyElement for the speech surface. The
        // invisible right-click bindings of the same type lack the chevron and recurse normally.
        if (cls == ElementClassification.LayoutWrapper
            && elTypeName == "PluginContextMenuBindings"
            && Adapters.ContextMenuButtonAdapter.IsStandaloneMenuButton(el))
        {
            cls = ElementClassification.Button;
        }

        // A player Fitness detail card's bare level step bar is the only carrier of that card's
        // level (the Injury Risk card shows no risk word at all while the player is injured);
        // promote it to a Stepper stop so its filled-of-total count renders, mirroring
        // ElementBridge.ClassifyElement for the speech surface.
        if (cls == ElementClassification.LayoutWrapper
            && elTypeName == "SIStepper"
            && ElementBridge.IsFitnessLevelBar(el))
        {
            cls = ElementClassification.Stepper;
        }

        // A selector-mode pitch slot (the Player Search Conditions card's Position filter) is the
        // clickable, stateful control; promote it to a Toggle stop, mirroring
        // ElementBridge.ClassifyElement for the speech surface. Its position-text child then stays
        // inside the toggle (checkbox label) instead of leaking as a separate text stop.
        if (cls == ElementClassification.LayoutWrapper
            && elTypeName == "FMPitchIndicator"
            && Adapters.PositionSelectorAdapter.IsSelectorIndicator(el))
        {
            cls = ElementClassification.Toggle;
        }

        // The standalone Messages panel separates emails by date ("Fri Apr 11th"). The profile marks
        // these as headings; on the HTML surface demote them to <h4> so they sit one level under the
        // message subject (<h3>) and stop colliding with it in heading navigation. Structural anchor
        // (the "IsDateSeparator" parent the profile also matches), reusing the game's own date text.
        // The speech surface is unaffected (it keeps the profile heading). On failure, fall through
        // and render it as the plain text it was, never dropping it.
        if (cls == ElementClassification.Text && el.hierarchy.parent?.name == "IsDateSeparator")
        {
            try
            {
                var dateAdapter = ElementBridge.CreateAdapter(el, cls);
                items.Add(new HeadingItem { Text = DisplayText.Of(dateAdapter), Level = 4 });
                return;
            }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Date-separator heading failed, rendering as text: {ex.Message}"); }
        }

        // A dashboard widget's section title - a text directly inside a "Heading" container, e.g. the
        // Portal Overview's "Messages" block - becomes a Level-2 heading, so the widget reads as a
        // navigable landmark instead of a plain label its content blends into. Structural anchor (the
        // "Heading" container), reusing the game's own title text, so it holds across save and team.
        // Profile hide rules apply before the promotion, exactly as for ordinary stops — the Portal
        // profile hides this title because it already heads the panel's section (GetTitle). On
        // failure, fall through and render it as the plain text it was, never dropping it.
        if (cls == ElementClassification.Text && el.hierarchy.parent?.name == "Heading")
        {
            try
            {
                var headingAdapter = ElementBridge.CreateAdapter(el, cls);
                if (NavigationManager.IsHidden(headingAdapter))
                    return;
                int level = ServerText.HeadingLevel(headingAdapter);
                items.Add(new HeadingItem { Text = DisplayText.Of(headingAdapter), Level = level > 0 ? Math.Min(6, level + 1) : 2 });
                return;
            }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Heading-title promotion failed, rendering as text: {ex.Message}"); }
        }

        if (TreeWalker.NavigationStops.Contains(cls))
        {
            // A swallowed failure here drops the element from the page silently — a lie by
            // omission on the screen-reader surface. Log so it is at least diagnosable.
            try
            {
                var adapter = ElementBridge.CreateAdapter(el, cls);
                // Honor the active panel profile's hide rules, exactly as TreeWalker does
                // for the speech surface, so a stop hidden from navigation is also absent
                // from the HTML page rather than only half-removed.
                if (NavigationManager.IsHidden(adapter))
                    return;
                // Native frame headers can be commands (for example the
                // overview's First Team or Training Schedule). Preserve their
                // actual title as an informational landmark, with the ordinary
                // command separate. Never wrap an interactive control in h1-h6.
                if (cls == ElementClassification.Button && elName == "title-link-button")
                {
                    var title = FindDescendantNamed(el, "primary-text");
                    if (title != null && CleanOrNull(ReadSITextValue(title)) != null)
                        items.Add(new StopItem { Element = new Adapters.TextAdapter(title), HeadingLevel = 3 });
                }
                SuppressFoldedLabelStops(items, adapter);
                if (IsTextFoldedByPrecedingControl(items, adapter))
                    return;
                // Message prose retains FM26's readonly paragraph; useful
                // entity rows and other contexts retain their native links.
                if (cls == ElementClassification.Text && elTypeName == "SIText"
                    && !MessageViewSurface.IsNarrativeText(el)
                    && adapter is not Adapters.InertLinkTextAdapter
                    && ServerText.HeadingLevel(adapter) == 0
                    && Adapters.SITextLink.HasLink(el.TryCast<TextElement>()?.text))
                {
                    try
                    {
                        var parts = Adapters.SITextLink.ReadParts(el);
                        if (parts == null) adapter = new Adapters.SITextLink.PendingText(el);
                        else if (parts.Any(part => part is Adapters.SITextLink.Part))
                        {
                            bool question=SpeakToRenderer.IsQuestion(el);
                            items.Add(new JoinedTextItem { Parts = parts,
                                BodySource = elName == "MessageBodyText" || question ? adapter : null,
                                IsConversationQuestion=question });
                            return;
                        }
                    }
                    catch (Exception ex) { Log.Warn($"[AccessibilityServer] Inline links not ready; keeping complete text: {ex.Message}"); }
                }
                items.Add(new StopItem { Element = adapter });
                if (adapter is Adapters.SearchableDropdownAdapter search && search.FullListChooser() is { } fullList)
                    items.Add(new StopItem { Element = fullList });
                if (adapter is Adapters.SearchBoxAdapter)
                {
                    // Its text input belongs to the widget, but the native
                    // close/show-all commands alongside it remain independent.
                    var buttons = FindDescendantNamed(el, "Buttons");
                    if (buttons != null) WalkRec(buttons, null, items);
                }
            }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] CreateAdapter failed ({cls}), element omitted: {ex.Message}"); }
            return;
        }

        // Every message inbox — the standalone Messages panel AND the Portal dashboard's Messages
        // widget share these container names — renders its reading pane never (an opened message
        // is the modal Message view, MessageViewSurface) and its email list as one semantic table:
        // Status, Subject, Sender, Sent, the same parts MessageButtonAdapter already composes per
        // row, so the screen reader gets column navigation over the inbox instead of a run of
        // single-blob buttons. The list's siblings (tab bar, action dropdowns, Mark All Read)
        // keep walking normally. HTML-only; the speech cursor keeps the raw structure. A table
        // build failure degrades to the region-wrapped button list, so the inbox never disappears.
        if (elName == "MessagesToolBlockMessageContent") return;
        if (elName == "MessagesStreamedObjectList" || elTypeName == "MessagesStreamedObjectList")
        {
            // The list is virtualised: the paging chrome (ListWindowSurface) stays ahead of the
            // table so the unrealised rows remain an honest, navigable gap.
            ListWindowSurface.AppendChrome(items, el, ListWindowSurface.Labels.Messages);
            if (TryBuildMessagesTable(el, items)) return;
            items.Add(new RegionStartItem { Label = "message list", IsList = true });
            int mc = el.hierarchy.childCount;
            for (int mi = 0; mi < mc; mi++)
                WalkRec(el.hierarchy[mi], cls, items);
            items.Add(new RegionEndItem());
            return;
        }

        // The player report's whole attributes block (the IsGoalkeeper switch holding every
        // attribute column) closes with mod-authored summaries — the ten best and the ten worst
        // attributes across all sections — emitted after the block's children so they sit at the
        // bottom of the stats section, before the Info column that follows as a sibling.
        bool attributesBlock = elTypeName == "BindableSwitchElement" && elName == "IsGoalkeeper";

        // A segmented button row that reached this point kept its plain-button presentation (the
        // setfor / Mode-Buttons rows consolidated into radiogroups above; see also the 2026-07-01
        // selected-state revert) — but it is still the game's button-bar concept, so its children
        // walk inside a labelled region landmark for landmark navigation (D / Shift+D).
        if (elName == "buttons-segmented-button-row-regular-default")
        {
            WalkChildrenAsRegion(el, cls, items, ContainerKind.ButtonBar);
        }
        else
        {
            int count = el.hierarchy.childCount;
            for (int i = 0; i < count; i++)
                WalkRec(el.hierarchy[i], cls, items);
        }

        if (attributesBlock) EmitAttributeSummaries(el, items);
    }

    // Walks an element's children inside a labelled region landmark (RegionStart/RegionEnd), so
    // screen reader landmark navigation can jump to the group's first item. A group whose walk
    // emits nothing gets no landmark at all — an empty region is pure noise.
    private static void WalkChildrenAsRegion(VisualElement el, ElementClassification? cls, List<RenderItem> items, string label)
    {
        int mark = items.Count;
        items.Add(new RegionStartItem { Label = label });
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            WalkRec(el.hierarchy[i], cls, items);
        if (items.Count == mark + 1)
        {
            items.RemoveAt(mark);
            return;
        }
        items.Add(new RegionEndItem());
    }

    // The standalone Messages panel's inbox as one semantic table. Collects the realised rows in
    // document order, carrying each date separator's text down into the rows that follow it, and
    // bakes every cell (cleaned) at walk time — the rows are realised live elements, so no
    // streaming is needed. False (build declined) when the walk found no rows or failed; the
    // caller keeps the region-wrapped button list, so a build problem never hides the inbox.
    private static bool TryBuildMessagesTable(VisualElement listHost, List<RenderItem> items)
    {
        try
        {
            var rows = new List<MessageRow>();
            string date = null;
            int unreadableSubjects = 0;
            CollectMessageRowsRec(listHost, ref date, rows, ref unreadableSubjects);
            if (IsNativeUnreadTab(listHost)) rows.RemoveAll(row => !row.Adapter.IsUnread());
            // Keep the inbox item even while a new native filter is binding.
            // Other dashboard tables load over several frames; by final render
            // the native inbox can already contain rows although this walk did
            // not. Falling back here baked an empty list into a fresh shape.
            if (unreadableSubjects > 0)
            {
                Degrade.Note($"messages table: {unreadableSubjects} of {rows.Count} row subjects not readable at compose (async bind race?); marked provisional for re-read (host={HostChainOf(listHost)})");
                if (unreadableSubjects == rows.Count)
                    DumpUnreadableMessageRowOnce(listHost, rows);
            }
            items.Add(new MessagesTableItem
            {
                Host = listHost,
                Rows = rows,
                Provisional = unreadableSubjects > 0,
                AnyPreview = rows.Exists(r => r.UsedPreview),
            });
            return true;
        }
        catch (Exception ex)
        {
            Degrade.Note($"messages table build failed, keeping list rendering: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    // The named ancestor chain of the walked inbox list, outermost first ("PortalScreen/.../
    // MessagesStreamedObjectList"). A field log of the unreadable-subject bug must prove WHICH
    // inbox the page walked — the Portal dashboard's twin widget and the real Messages tool
    // share every container name below their panel roots, and the two shipped fixes each
    // assumed a different answer. Cheap (a parent climb), so it rides the recurring degrade note.
    private static string HostChainOf(VisualElement listHost)
    {
        try
        {
            var chain = new List<string>();
            for (var a = listHost; a != null; a = a.hierarchy.parent)
                if (!string.IsNullOrEmpty(a.name))
                    chain.Add(a.name);
            chain.Reverse();
            if (chain.Count == 0) return "(unnamed)";
            // The OUTERMOST names carry the answer (which panel's widget this is), so a deep
            // chain elides the middle, never the top.
            if (chain.Count > 12)
            {
                var elided = chain.GetRange(0, 8);
                elided.Add("…");
                elided.AddRange(chain.GetRange(chain.Count - 3, 3));
                chain = elided;
            }
            return string.Join("/", chain);
        }
        catch (Exception ex)
        {
            return "(host chain failed: " + ex.Message + ")";
        }
    }

    private static bool _unreadableRowDumped;

    // One structural snapshot per session of a fully-unreadable inbox row. The field bug this
    // diagnoses (every subject "couldn't read", never healing) has not reproduced locally, so a
    // shared Player.log must answer on its own: does MessageHeadline exist in the row at all
    // (absent = template mismatch), and if it does, is its text pending (SIText.m_finalTextSet
    // false = the game's composed-string bind never landed)? Bounded to one row, depth 5.
    private static void DumpUnreadableMessageRowOnce(VisualElement listHost, List<MessageRow> rows)
    {
        if (_unreadableRowDumped) return;
        _unreadableRowDumped = true;
        try
        {
            var row = rows.Find(r => r.SubjectUnreadable);
            if (row == null) return;
            var el = row.Adapter.BackingElement;
            var sb = new System.Text.StringBuilder();
            sb.Append("[degrade] unreadable-subject row snapshot (one per session): host=")
              .Append(HostChainOf(listHost));
            var headline = FindDescendantByName(el, "MessageHeadline", 12);
            if (headline == null)
            {
                sb.Append("\n MessageHeadline: NOT FOUND in row");
            }
            else
            {
                var text = headline.TryCast<TextElement>()?.text;
                sb.Append("\n MessageHeadline: type=").Append(headline.GetIl2CppType().Name)
                  .Append(" textlen=").Append(text?.Length.ToString() ?? "null(not a TextElement)")
                  .Append(" enabled=").Append(headline.enabledInHierarchy)
                  .Append(" visible=").Append(ElementBridge.IsVisible(headline));
                var siText = headline.TryCast<SI.Bindable.SIText>();
                if (siText != null)
                {
                    var finalSet = typeof(SI.Bindable.SIText).GetProperty("m_finalTextSet",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance);
                    sb.Append(" finalTextSet=")
                      .Append(finalSet == null ? "(property missing)" : finalSet.GetValue(siText)?.ToString());
                }
            }
            sb.Append("\n row tree:");
            AppendRowTree(el, sb, 0);
            Log.Warn(sb.ToString());
        }
        catch (Exception ex)
        {
            Log.Warn($"[degrade] unreadable-subject row snapshot failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static VisualElement FindDescendantByName(VisualElement root, string name, int depthCap)
    {
        if (depthCap < 0) return null;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var child = root.hierarchy[i];
            if (child.name == name) return child;
            var deeper = FindDescendantByName(child, name, depthCap - 1);
            if (deeper != null) return deeper;
        }
        return null;
    }

    // name:type[:textlen][:hidden] per node, indented, depth-capped — enough to see the row's
    // template shape and which leaves carry text, without logging message content.
    private static void AppendRowTree(VisualElement el, System.Text.StringBuilder sb, int depth)
    {
        if (depth > 5) return;
        sb.Append('\n').Append(' ', 2 + depth * 2)
          .Append(string.IsNullOrEmpty(el.name) ? "-" : el.name)
          .Append(':').Append(el.GetIl2CppType().Name);
        var text = el.TryCast<TextElement>()?.text;
        if (text != null) sb.Append(":len").Append(text.Length);
        if (!ElementBridge.IsVisible(el)) sb.Append(":hidden");
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            AppendRowTree(el.hierarchy[i], sb, depth + 1);
    }

    private static void CollectMessageRowsRec(VisualElement el, ref string date, List<MessageRow> rows,
        ref int unreadableSubjects)
    {
        if (!ElementBridge.IsVisible(el)) return;

        if ((el.hierarchy.parent?.name == "IsDateSeparator" || el.hierarchy.parent?.hierarchy.parent?.name == "IsDateSeparator")
            && el.GetIl2CppType().Name == "SIText")
        {
            var t = CleanOrNull(ReadSITextValue(el));
            if (t != null) date = t;
            return;
        }

        if (el.name == "NewsItemButton")
        {
            var adapter = new Adapters.MessageButtonAdapter(el);
            if (NavigationManager.IsHidden(adapter)) return;
            // Cells bake here (raw game text -> cleaned) because the adapter deliberately returns
            // raw markup and relies on each surface's boundary cleaner.
            // Sent is one column: the group's date (from the separator above) plus the row's own
            // time, either part alone when the other is missing.
            var time = CleanOrNull(adapter.GetTime());
            var sent = date != null && time != null ? date + ", " + time : date ?? time ?? "";
            // A row whose subject has not bound yet gets an honest marker, and the caller marks
            // the compose provisional so it is re-read once the text lands. The old fallback (the
            // full row label) baked "unread, 16:16" INTO the subject button — which read as the
            // whole message title on slow machines that lost the bind race, hiding the failure.
            // The COMPACT row template (small screens) has no subject element at all — the game
            // shows the message body's first line instead — so the row falls back to that
            // preview, and the table's column header says "Preview" rather than "Subject".
            var subject = CleanOrNull(adapter.GetSubject());
            bool usedPreview = false;
            if (subject == null)
            {
                var preview = CleanOrNull(adapter.GetPreview());
                if (preview != null)
                {
                    subject = preview;
                    usedPreview = true;
                }
            }
            bool subjectUnreadable = subject == null;
            if (subjectUnreadable)
            {
                unreadableSubjects++;
                subject = Degrade.Unreadable;
            }
            rows.Add(new MessageRow
            {
                Adapter = adapter,
                Status = CleanOrNull(adapter.GetStatusText()) ?? "",
                Subject = subject,
                SubjectUnreadable = subjectUnreadable,
                UsedPreview = usedPreview,
                Sender = CleanOrNull(adapter.GetSender()) ?? "",
                SenderLeaf = adapter.GetSenderElement(),
                Sent = sent,
            });
            return;
        }

        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            CollectMessageRowsRec(el.hierarchy[i], ref date, rows, ref unreadableSubjects);
    }

    /// <summary>
    /// The inbox row whose subject matches, or null when no realised row does. The restore path's
    /// fallback (MessageViewSurface.Restore) uses it to re-select a message the reading pane no
    /// longer holds. Rows are identified the same way the inbox table identifies them
    /// (NewsItemButton), so the two cannot disagree about what a row is; the subject is cleaned with
    /// the same filter the pane read uses, so the comparison is like-for-like. The list is
    /// virtualised — an unrealised row is simply not found, which the caller reports as an honest
    /// gap rather than guessing. Main thread.
    /// </summary>
    internal static Adapters.MessageButtonAdapter FindMessageRowBySubject(VisualElement root, string subject, int? messageId = null)
    {
        if (!messageId.HasValue && string.IsNullOrEmpty(subject)) return null;
        var matches = new List<Adapters.MessageButtonAdapter>();
        FindMessageRowRec(root, subject, messageId, matches);
        return matches.Count == 1 ? matches[0] : null;
    }

    private static void FindMessageRowRec(VisualElement el, string subject, int? messageId, List<Adapters.MessageButtonAdapter> matches)
    {
        if (matches.Count > 1 || !ElementBridge.IsVisible(el)) return;

        if (el.name == "NewsItemButton")
        {
            var adapter = new Adapters.MessageButtonAdapter(el);
            var rowSubject = adapter.GetSubject();
            if (messageId.HasValue ? adapter.MessageId == messageId
                : rowSubject != null && TextFilters.CleanForDisplay(rowSubject).Trim() == subject) matches.Add(adapter);
            return;
        }

        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            FindMessageRowRec(el.hierarchy[i], subject, messageId, matches);
        }
    }

    // The modal Message view (MessageViewSurface) walks the reading pane's subtree with this; the
    // base-page walk skips that container. Starts at the container's children so the walk never
    // re-enters the skip keyed on the container's own name. Main thread, under the Messages
    // panel's profile (the caller activates it).
    internal static List<RenderItem> WalkMessageContent(VisualElement contentRoot)
    {
        var items = new List<RenderItem>();
        int n = contentRoot.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            WalkRec(contentRoot.hierarchy[i], null, items);
        return items;
    }

    // All visible clickable attribute rows in a subtree, wrapped in their adapter — or null when
    // the subtree holds none (any other streamed list) or the collection failed (which logs and
    // falls back to the generic recursion, so the rows still render individually).
    private static List<Adapters.AttributeRowAdapter> TryCollectAttributeRows(VisualElement root)
    {
        try
        {
            List<Adapters.AttributeRowAdapter> rows = null;
            CollectAttributeRowsRec(root, ref rows);
            return rows;
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Attribute-row collection failed, recursing generically: {ex.Message}");
            return null;
        }
    }

    private static void CollectAttributeRowsRec(VisualElement el, ref List<Adapters.AttributeRowAdapter> rows)
    {
        if (!ElementBridge.IsVisible(el)) return;
        if (Adapters.AttributeRowAdapter.IsAttributeRow(el))
        {
            (rows ??= new List<Adapters.AttributeRowAdapter>()).Add(new Adapters.AttributeRowAdapter(el));
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            CollectAttributeRowsRec(el.hierarchy[i], ref rows);
    }

    // The best- and worst-attributes summaries across every attribute in the block. Emitted only
    // when every row's value read back as a number: a partially-masked report (scouted players
    // show "-") must not present a ranking built from partial data as "best attributes". The full
    // section tables above always show everything, so skipping the summary loses no information.
    //
    // Both tables come out of this one collection under that one gate: either the block ranked
    // cleanly and the player gets both ends of it, or neither table appears. The goalkeeper and
    // outfield reports need no distinction here — the block is the IsGoalkeeper switch itself, and
    // only the visible column's rows are collected, so each report ranks its own attribute set.
    private static void EmitAttributeSummaries(VisualElement block, List<RenderItem> items)
    {
        var rows = TryCollectAttributeRows(block);
        if (rows == null || rows.Count < 2) return;

        var entries = new List<AttributeEntry>(rows.Count);
        foreach (var row in rows)
        {
            string name, value;
            try
            {
                name = row.GetAttributeName();
                value = row.GetAttributeValue();
            }
            catch (Exception ex)
            {
                Log.Warn($"[AccessibilityServer] Attribute summary skipped, unreadable row: {ex.Message}");
                return;
            }
            if (!int.TryParse(value, out int v)) return;
            entries.Add(new AttributeEntry { Name = name, Value = v });
        }

        int take = Math.Min(10, entries.Count);

        // GetRange copies, so the second sort cannot disturb the list already handed to the first
        // item.
        entries.Sort((a, b) => b.Value.CompareTo(a.Value));
        items.Add(new AttributeSummaryItem
        {
            Heading = BestAttributesHeading,
            Entries = entries.GetRange(0, take),
        });

        entries.Sort((a, b) => a.Value.CompareTo(b.Value));
        items.Add(new AttributeSummaryItem
        {
            Heading = WorstAttributesHeading,
            Entries = entries.GetRange(0, take),
        });
    }

    // A value-bearing control adopts its row's field label (and, for a toggle, its on/off status)
    // as its own accessible name and state. That same text is also walked as a standalone Text stop
    // sitting just before the control, so without this it reads as a stray line above an
    // already-labelled control. Drop those duplicates: only Text stops at the tail whose content
    // exactly matches what the control folded, so real content is never removed and the label is
    // relocated into the control rather than lost.
    private static void SuppressFoldedLabelStops(List<RenderItem> items, IAccessibleElement adapter)
    {
        if (adapter is not Adapters.PluginAdapterBase pab) return;

        IReadOnlyList<string> folded;
        try { folded = pab.FoldedTexts(); }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] FoldedTexts threw, leaving stops: {ex.Message}"); return; }
        SuppressFoldedLabelStops(items, folded);
    }

    // Drop preceding stray Text stops whose content a following control (or group) has adopted as
    // its own name, given the folded text(s) directly. Shared by the adapter-driven path above and
    // the segmented-control group emission, which folds its row label into the fieldset name.
    private static void SuppressFoldedLabelStops(List<RenderItem> items, IReadOnlyList<string> folded)
    {
        if (folded == null || folded.Count == 0) return;

        // Scan back over this row's preceding Text stops, dropping those whose text the control
        // folded into its own name/value. A non-matching text stop (a row's help description) is
        // stepped over but kept, so a label sitting above a description above the control is still
        // relocated. The scan stops at the first non-text stop (the previous row's control) or a
        // section heading, keeping it within the current row so unrelated content is never removed.
        int i = items.Count - 1;
        int guard = 0;
        while (i >= 0 && guard++ < 8 && items[i] is StopItem si)
        {
            string role;
            try { role = si.Element.Role; }
            catch { break; }
            if (role != "text") break;
            if (ServerText.IsHeading(si.Element)) break;

            string text;
            try { text = si.Element.GetLabel(); }
            catch { break; }

            if (!string.IsNullOrEmpty(text) && FoldedContains(folded, text))
            {
                items.RemoveAt(i);
            }
            // Step to the preceding stop either way: a removal leaves earlier indices unchanged,
            // and a kept description is stepped over so a label behind it can still be dropped.
            i--;
        }
    }

    // The mirror of SuppressFoldedLabelStops for a label that FOLLOWS its control in tree order:
    // a checkbox row template can place the label text after the checkbox (the Pick Attributes
    // list), so the control folds the text into its own name before the text is walked. Drop the
    // text stop when the stop emitted immediately before it is a non-text control that folded this
    // exact content — the same never-remove-real-content bar as the backward scan, but only one
    // stop deep, so a text following an unrelated control is never touched.
    private static bool IsTextFoldedByPrecedingControl(List<RenderItem> items, IAccessibleElement adapter)
    {
        try
        {
            if (adapter.Role != "text") return false;
            if (items.Count == 0 || items[items.Count - 1] is not StopItem prev) return false;
            if (prev.Element.Role == "text") return false;
            if (prev.Element is not Adapters.PluginAdapterBase pab) return false;

            string text = adapter.GetLabel();
            if (string.IsNullOrEmpty(text)) return false;
            return FoldedContains(pab.FoldedTexts(), text);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Trailing folded-label check failed, keeping stop: {ex.Message}");
            return false;
        }
    }

    // The display text of the plain-text stop walked immediately before the current element, removed
    // from the list so it can be re-emitted in another form (the league-history chart promotes its
    // label into the table's heading). Returns null when the preceding item is not a plain text stop
    // (already a heading, a control, or nothing) — then the caller renders without a folded label.
    private static string FoldPrecedingLabel(List<RenderItem> items)
    {
        if (items.Count == 0 || items[items.Count - 1] is not StopItem s) return null;

        string role;
        try { role = s.Element.Role; }
        catch { return null; }
        if (role != "text" || ServerText.IsHeading(s.Element)) return null;

        string text;
        try { text = DisplayText.Of(s.Element); }
        catch { return null; }
        if (string.IsNullOrEmpty(text)) return null;

        items.RemoveAt(items.Count - 1);
        return text;
    }

    // The nearest heading already emitted above the current element, or null when nothing has been
    // emitted under one yet. Unlike FoldPrecedingLabel this consumes nothing — the heading stays in
    // the reading order and is only borrowed, so a chart table can name the metric it plots
    // ("Overall Balance") without stealing the section title from everything else in the section.
    // Both kinds count: a mod-authored HeadingItem, and a game text stop a profile rule promoted to
    // a heading (which is what the finance cards' own titles are).
    private static string EnclosingHeading(List<RenderItem> items)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is HeadingItem h)
                return string.IsNullOrWhiteSpace(h.Text) ? null : h.Text;

            if (items[i] is not StopItem s) continue;
            string text;
            try
            {
                if (!ServerText.IsHeading(s.Element)) continue;
                text = DisplayText.Of(s.Element);
            }
            catch { return null; }
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        return null;
    }

    private static bool FoldedContains(IReadOnlyList<string> folded, string text)
    {
        for (int i = 0; i < folded.Count; i++)
            if (string.Equals(folded[i], text, StringComparison.Ordinal)) return true;
        return false;
    }

    // The radio adapters inside an SIRadioGroup, or null if the group also holds a stray non-radio
    // stop (then the caller recurses normally rather than hiding that content in a group). Per-radio
    // action controls found beneath each radio are gathered into extras, aligned by index with the
    // returned radios; a radio with no such controls contributes an empty list. subGroups (optional,
    // same alignment) receives each radio's nested sub-option radios — empty for an ordinary radio.
    private static List<IAccessibleElement> TryCollectRadioGroup(VisualElement group, ElementClassification? parentClassification,
        List<List<IAccessibleElement>> extras, List<List<IAccessibleElement>> subGroups = null)
    {
        var radios = new List<IAccessibleElement>();
        bool hasNonRadioStop = false;
        subGroups ??= new List<List<IAccessibleElement>>();
        CollectRadioStops(group, parentClassification, radios, extras, subGroups, ref hasNonRadioStop);
        return hasNonRadioStop ? null : radios;
    }

    // The first visible SIRadioGroup inside a radio button's subtree: the radio's nested
    // sub-option list. Depth-first; null when the radio has none (the common case).
    private static VisualElement FindNestedRadioGroup(VisualElement radio)
    {
        int count = radio.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = radio.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            if (child.GetIl2CppType().Name == "SIRadioGroup") return child;
            var deeper = FindNestedRadioGroup(child);
            if (deeper != null) return deeper;
        }
        return null;
    }

    // Walk a radio group's subtree gathering its radio-button stops. A classification or adapter
    // failure, or a stray non-radio stop sitting directly in the group, sets hasNonRadioStop so the
    // caller abandons consolidation (a swallowed failure here would silently drop a control from the
    // page). A radio row's OWN action controls (the coach list's Edit/Delete buttons) are not stray:
    // they live beneath the radio, so they are captured as that radio's extras rather than aborting.
    private static void CollectRadioStops(VisualElement el, ElementClassification? parentClassification,
        List<IAccessibleElement> radios, List<List<IAccessibleElement>> extras,
        List<List<IAccessibleElement>> subGroups, ref bool hasNonRadioStop)
    {
        if (!ElementBridge.IsVisible(el)) return;

        ElementClassification cls;
        try
        {
            var descriptor = ElementBridge.Describe(el, parentClassification);
            cls = ElementBridge.ClassifyDescriptor(descriptor, el);
        }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] Radio group classify failed: {ex.Message}"); hasNonRadioStop = true; return; }

        if (cls == ElementClassification.InternalChild)
            return;

        // An unknown inside a candidate radio group: abandon radio consolidation so the normal walk
        // renders it as an unknown control rather than silently dropping it from the group.
        if (cls == ElementClassification.Unknown)
        {
            hasNonRadioStop = true;
            return;
        }

        if (TreeWalker.NavigationStops.Contains(cls))
        {
            IAccessibleElement adapter;
            try { adapter = ElementBridge.CreateAdapter(el, cls); }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Radio group CreateAdapter failed ({cls}): {ex.Message}"); hasNonRadioStop = true; return; }

            if (NavigationManager.IsHidden(adapter))
                return;
            if (adapter.Role == "radio button")
            {
                radios.Add(adapter);
                // A radio row can carry its own action controls beneath it (the coach list's per-row
                // Edit/Delete buttons). Capture those as this radio's extras — mirroring TreeWalker's
                // descent into a radio button — so they render beside the radio instead of vanishing.
                // The row's name/DOB text is already folded into the radio's own label, so only
                // interactive controls are kept, never text.
                var rowExtras = new List<IAccessibleElement>();
                int kids = el.hierarchy.childCount;
                for (int i = 0; i < kids; i++)
                    CollectRadioRowExtras(el.hierarchy[i], cls, rowExtras);
                extras.Add(rowExtras);
                // A nested sub-option group inside this radio's subtree (the Player Interest
                // dialog's Interested/Persuadable/Doubtful under "Transfer only") is its own
                // radio group, shown by the game only under the selected parent. Collect its
                // radios so the renderer nests a sub-radiogroup after this radio; a collection
                // failure aborts the whole consolidation (hasNonRadioStop) so the normal walk
                // still renders every control rather than silently dropping the sub-options.
                var subRadios = new List<IAccessibleElement>();
                var nested = FindNestedRadioGroup(el);
                if (nested != null)
                {
                    var collected = TryCollectRadioGroup(nested, cls, new List<List<IAccessibleElement>>());
                    if (collected == null) { hasNonRadioStop = true; return; }
                    subRadios = collected;
                }
                subGroups.Add(subRadios);
            }
            else hasNonRadioStop = true;
            return;
        }

        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectRadioStops(el.hierarchy[i], cls, radios, extras, subGroups, ref hasNonRadioStop);
    }

    // The interactive controls beneath a radio button (its Edit/Delete row buttons). Skips the row's
    // label text — already folded into the radio's own name — and any nested radio, keeping only
    // actionable stops. A read failure drops that one control with a log, never the whole group.
    private static void CollectRadioRowExtras(VisualElement el, ElementClassification? parentClassification,
        List<IAccessibleElement> extras)
    {
        if (!ElementBridge.IsVisible(el)) return;

        ElementClassification cls;
        try
        {
            var descriptor = ElementBridge.Describe(el, parentClassification);
            cls = ElementBridge.ClassifyDescriptor(descriptor, el);
        }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] Radio row extra classify failed: {ex.Message}"); return; }

        if (cls == ElementClassification.InternalChild)
            return;

        // A nested sub-option group is collected separately (CollectRadioStops subGroups); its
        // contents must not double up as this row's extras.
        if (cls == ElementClassification.RadioGroup)
            return;

        if (TreeWalker.NavigationStops.Contains(cls))
        {
            // Text is the radio's own folded label; a nested radio belongs to the group, not this row.
            if (cls == ElementClassification.Text || cls == ElementClassification.RadioButton)
                return;
            try
            {
                var adapter = ElementBridge.CreateAdapter(el, cls);
                if (!NavigationManager.IsHidden(adapter)) extras.Add(adapter);
            }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Radio row extra adapter failed, omitted: {ex.Message}"); }
            return;
        }

        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectRadioRowExtras(el.hierarchy[i], cls, extras);
    }

    // A segmented row's own leading label (the "Set For" SIText that is the container's first text
    // child). Null when absent or blank, so the group renders unnamed rather than mis-named.
    private static string ReadLeadingSITextLabel(VisualElement row)
    {
        int count = row.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var textEl = row.hierarchy[i].TryCast<TextElement>();
            if (textEl == null) continue;
            return string.IsNullOrEmpty(textEl.text) ? null : Speech.TextFilters.CleanForDisplay(textEl.text);
        }
        return null;
    }

    // The segment buttons of a "Mode-Buttons" container, each wrapped to read as a radio. They are
    // plain SIButtons nested under per-segment SIStyleSetter wrappers; collected depth-first so they
    // keep visual order.
    private static List<IAccessibleElement> CollectSegmentedOptions(VisualElement modeButtons)
    {
        var segments = new List<IAccessibleElement>();
        CollectSegmentButtons(modeButtons, segments);
        return segments;
    }

    private static void CollectSegmentButtons(VisualElement el, List<IAccessibleElement> segments)
    {
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = el.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            if (child.GetIl2CppType().Name == "SIButton")
                segments.Add(new Adapters.SegmentedOptionAdapter(child));
            else
                CollectSegmentButtons(child, segments);
        }
    }

    // The same collection for a control whose selected side is marked by a tick child rather
    // than a solid fill (the set-piece rows) — each button wraps as a tick-telling radio.
    private static List<IAccessibleElement> CollectTickSegmentedOptions(VisualElement row)
    {
        var segments = new List<IAccessibleElement>();
        CollectTickSegmentButtons(row, segments);
        return segments;
    }

    private static void CollectTickSegmentButtons(VisualElement el, List<IAccessibleElement> segments)
    {
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = el.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            if (child.GetIl2CppType().Name == "SIButton")
                segments.Add(new Adapters.SegmentedTickOptionAdapter(child));
            else
                CollectTickSegmentButtons(child, segments);
        }
    }

    // The set-piece panels: the main screen and its two popup cards. The tick-telling segmented
    // consolidations are scoped to these so the same templates elsewhere keep their existing
    // presentation.
    private static readonly string[] SetPiecePanelNames =
    {
        "OnThePitchSetPiecesTool", "SetPieceInstructionPopupCard", SetPieceTakers.PanelName,
    };

    private static bool InSetPiecePanel(VisualElement el)
    {
        for (var p = el; p != null; p = p.hierarchy.parent)
            for (int i = 0; i < SetPiecePanelNames.Length; i++)
                if (p.name == SetPiecePanelNames[i]) return true;
        return false;
    }

    // The routine strip: one always-present active hero toggle (its number display flipped per
    // routine, the routine name, a management dropdown) plus per-slot inactive number chips
    // beside textless switch SIButtons, and an Add block holding its own dropdown. Raw, that
    // walks out as a stray "Routine" label, loose number texts and an unlabeled icon button
    // (the audit's degrade finding on this screen). Consolidate the toggles into a radiogroup —
    // the active routine checked with its number and name, each inactive one labelled
    // "<strip label> <number>" — then walk the strip's dropdowns (routine management, Add) out
    // normally behind it.
    private static bool TryEmitRoutineStrip(VisualElement frame, ElementClassification? cls, List<RenderItem> items)
    {
        VisualElement stripLabel = null;
        int count = frame.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = frame.hierarchy[i];
            if (child.TryCast<TextElement>() == null || !ElementBridge.IsVisible(child)) continue;
            stripLabel = child;
            break;
        }

        var radios = new List<IAccessibleElement>();
        var dropdowns = new List<VisualElement>();
        int activeCount = 0;
        CollectRoutineStripParts(frame, stripLabel, radios, dropdowns, ref activeCount, insideActive: false);
        if (activeCount != 1 || radios.Count < 1) return false;

        string label = null;
        var labelText = stripLabel?.TryCast<TextElement>();
        if (labelText != null && !string.IsNullOrEmpty(labelText.text))
            label = Speech.TextFilters.CleanForDisplay(labelText.text);

        items.Add(new RadioGroupItem { Group = frame, Radios = radios, Label = label });
        for (int i = 0; i < dropdowns.Count; i++)
            WalkRec(dropdowns[i], cls, items);
        return true;
    }

    private static void CollectRoutineStripParts(VisualElement el, VisualElement stripLabel,
        List<IAccessibleElement> radios, List<VisualElement> dropdowns, ref int activeCount, bool insideActive)
    {
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = el.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            string typeName = child.GetIl2CppType().Name;
            if (typeName == "SIDropdown")
            {
                dropdowns.Add(child);
                continue;
            }
            if (child.name == "buttons-hero-toggle-buttons-active" && !insideActive)
            {
                activeCount++;
                radios.Add(new Adapters.ActiveRoutineOptionAdapter(child));
                // Its management dropdown lives inside; keep descending for dropdowns only.
                CollectRoutineStripParts(child, stripLabel, radios, dropdowns, ref activeCount, insideActive: true);
                continue;
            }
            if (typeName == "SIButton" && !insideActive)
            {
                radios.Add(new Adapters.InactiveRoutineOptionAdapter(child, stripLabel));
                continue;
            }
            CollectRoutineStripParts(child, stripLabel, radios, dropdowns, ref activeCount, insideActive);
        }
    }

    // A segmented row's field label ("Scenario", "Delivery Type"): the SIText that precedes the
    // row's "-unpacked" wrapper among the wrapper's siblings. Null when the shape doesn't hold
    // (the popup's tab row sits under a bare SIVisible with no label), so the group renders
    // unnamed rather than mis-named.
    private static string FindSegmentedRowSiblingLabel(VisualElement row)
    {
        var wrapper = row.hierarchy.parent;
        if (wrapper == null || wrapper.name == null || !wrapper.name.EndsWith("-unpacked")) return null;
        var frame = wrapper.hierarchy.parent;
        if (frame == null) return null;
        string label = null;
        int count = frame.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = frame.hierarchy[i];
            if (child.Pointer == wrapper.Pointer) return label;
            var textEl = child.TryCast<TextElement>();
            if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                label = Speech.TextFilters.CleanForDisplay(textEl.text);
        }
        return null;
    }

    // The "ttile" field label naming a segmented control (e.g. "Mode"), found by walking up from the
    // Mode-Buttons container and taking the nearest enclosing SIText named "ttile". The label sits
    // one to three levels out (a sibling of the buttons frame), so the shallow walk reaches it
    // without straying into a neighbouring row's label. Null when none is found (the group then
    // renders without a name rather than a wrong one).
    private static string FindSegmentedGroupLabel(VisualElement modeButtons)
    {
        var current = modeButtons.parent;
        for (int depth = 0; depth < 5 && current != null; depth++)
        {
            var ttile = FindDescendantNamed(current, "ttile");
            if (ttile != null)
            {
                string text = ReadSITextValue(ttile);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            current = current.parent;
        }
        return null;
    }

    internal static VisualElement FindDescendantNamed(VisualElement root, string name)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (child.name == name) return child;
            var deeper = FindDescendantNamed(child, name);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private static string ReadSITextValue(VisualElement el)
    {
        try
        {
            var t = new SI.Bindable.SIText(el.Pointer).text;
            return string.IsNullOrEmpty(t) ? null : t;
        }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] segmented label read failed: {ex.Message}"); return null; }
    }

    // --- Today's Top Fixtures table (news site) ---

    // All visible fixture rows of the news site's Today's Top Fixtures rail — or null when the
    // subtree holds none or the collection failed (which logs and falls back to the generic
    // recursion, so the rows still render individually).
    private static List<TodaysFixtureRowData> TryCollectTodaysFixtures(VisualElement list)
    {
        try
        {
            List<TodaysFixtureRowData> rows = null;
            CollectTodaysFixtureRows(list, ref rows);
            return rows;
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Today's fixtures collection failed, rows render individually: {ex.Message}");
            return null;
        }
    }

    // A row is anchored on its "FixtureDetails" frame (home | time | away); the competition
    // button is that frame's sibling under the shared row root. Hidden subtrees are recycled
    // rows — skipped so stale content never renders.
    private static void CollectTodaysFixtureRows(VisualElement el, ref List<TodaysFixtureRowData> rows)
    {
        if (!ElementBridge.IsVisible(el)) return;
        var competition = FindChildNamed(el, "Competition");
        var home = FindChildNamed(el, "HomeTeamRemapper");
        var away = FindChildNamed(el, "AwayTeamRemapper");
        if (competition != null && home != null && away != null)
        {
            // FM27 places competition and kick-off in one frame beside both
            // team remappers, without the FM26 FixtureDetails wrapper.
            var row = new TodaysFixtureRowData
            {
                Competition = ReadFixtureCell(competition),
                Home = ReadFixtureCell(home),
                Away = ReadFixtureCell(away),
                Time = ReadCompactFixtureTime(competition),
            };
            (rows ??= new List<TodaysFixtureRowData>()).Add(row);
            return;
        }
        if (el.name == "FixtureDetails")
        {
            var rowRoot = el.hierarchy.parent ?? el;
            var row = new TodaysFixtureRowData
            {
                Competition = ReadFixtureCell(FindChildNamed(rowRoot, "CompetitionRemapper")),
                Home = ReadFixtureCell(FindChildNamed(el, "HomeTeamRemapper")),
                Away = ReadFixtureCell(FindChildNamed(el, "AwayTeamRemapper")),
                Time = ReadFixtureTime(el),
            };
            (rows ??= new List<TodaysFixtureRowData>()).Add(row);
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            CollectTodaysFixtureRows(el.hierarchy[i], ref rows);
    }

    // A cell slot holds the game's clickable button (competition or club) when one is live,
    // else whatever text the slot displays; a slot the game leaves blank stays a blank cell.
    private static FixtureCell ReadFixtureCell(VisualElement slot)
    {
        var cell = new FixtureCell();
        if (slot == null || !ElementBridge.IsVisible(slot)) return cell;

        var btn = FirstVisibleOfType(slot, "SIButton");
        if (btn != null)
        {
            var cls = ElementBridge.ClassifyElement(btn, null);
            if (TreeWalker.NavigationStops.Contains(cls))
            {
                cell.Button = ElementBridge.CreateAdapter(btn, cls);
                return cell;
            }
        }
        var textElement = FirstVisibleOfType(slot, "SIText");
        var text = textElement == null ? null : ReadSITextValue(textElement);
        if (Adapters.SITextLink.HasLink(text))
        {
            cell.Button = new Adapters.TableRowTextAdapter(textElement);
            return cell;
        }
        if (text != null) cell.Text = TextFilters.CleanForDisplay(text);
        return cell;
    }

    private static string ReadCompactFixtureTime(VisualElement competition)
    {
        // The unnamed, direct SIText is the native kick-off caption; the
        // nested named text is the competition link, never a guessed time.
        for (int i = 0; i < competition.hierarchy.childCount; i++)
        {
            var child = competition.hierarchy[i];
            if (child.GetIl2CppType().Name != "SIText" || !ElementBridge.IsVisible(child)) continue;
            var text = ReadSITextValue(child);
            return text != null ? TextFilters.CleanForDisplay(text) : Degrade.Unavailable;
        }
        return "";
    }

    // The kick-off time SIText sits in the row's "data_display-cell-standard-regular" cell. An
    // absent cell is a blank the game displays; a present but unreadable one degrades to an
    // honest marker, never a blank.
    private static string ReadFixtureTime(VisualElement fixtureDetails)
    {
        var timeCell = FindDescendantNamed(fixtureDetails, "data_display-cell-standard-regular");
        if (timeCell == null || !ElementBridge.IsVisible(timeCell)) return "";
        var text = FirstVisibleSITextValue(timeCell);
        return text != null ? TextFilters.CleanForDisplay(text) : Degrade.Unavailable;
    }

    private static VisualElement FirstVisibleOfType(VisualElement root, string typeName)
    {
        if (!ElementBridge.IsVisible(root)) return null;
        if (root.GetIl2CppType().Name == typeName) return root;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var hit = FirstVisibleOfType(root.hierarchy[i], typeName);
            if (hit != null) return hit;
        }
        return null;
    }

    private static string FirstVisibleSITextValue(VisualElement root)
    {
        var textEl = FirstVisibleOfType(root, "SIText");
        return textEl != null ? ReadSITextValue(textEl) : null;
    }

    // --- Fixture schedule table ---

    // Consolidate the Fixture Schedule wrapper (header row + streamed fixture list) into a
    // FixtureScheduleItem: the live column headers read now (they are not virtualised), the rows
    // left for FixtureScheduleLoader to stream in on the request thread (cell values bind across
    // frames, unreadable from a single main-thread walk). A build failure degrades to the normal
    // recursion so the headers and any realized rows still render as flat stops rather than
    // vanishing.
    private static void EmitFixtureSchedule(VisualElement wrapper, List<RenderItem> items)
    {
        try
        {
            var headers = Contexts.FixtureScheduleContext.ReadHeaders(wrapper);
            var list = FindDescendantNamed(wrapper, "FixturesList");
            if (headers.Count == 0 || list == null)
                throw new InvalidOperationException("schedule shape changed under the wrapper gate");

            var keys = new List<string>(headers.Count);
            foreach (var h in headers) keys.Add(h.Key);

            items.Add(new FixtureScheduleItem
            {
                List = list,
                Headers = headers,
                Context = new Contexts.FixtureScheduleContext(list, keys),
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Fixture schedule build failed, recursing as plain stops: {ex.Message}");
            int count = wrapper.hierarchy.childCount;
            for (int i = 0; i < count; i++)
                WalkRec(wrapper.hierarchy[i], null, items);
        }
    }

    // --- Competition fixtures table ---

    // Consolidate the Add/Remove Leagues grid into a LeagueTableItem. The rows virtualise (only the
    // on-screen window is realized), so the item carries the streamed list and a context; the loader
    // scrolls every nation in and reads it across frames (see LeagueTableLoader). A missing list
    // degrades to the generic recursion so nothing is swallowed.
    private static void EmitLeagueTable(VisualElement container, List<RenderItem> items)
    {
        try
        {
            var list = FindDescendantOfType(container, "StreamedObjectList");
            if (list == null)
            {
                int kids = container.hierarchy.childCount;
                for (int i = 0; i < kids; i++)
                    WalkRec(container.hierarchy[i], null, items);
                return;
            }
            items.Add(new LeagueTableItem { List = list, Context = new Contexts.LeagueTableContext(list) });
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] League table build failed, recursing as plain stops: {ex.Message}");
            int kids = container.hierarchy.childCount;
            for (int i = 0; i < kids; i++)
                WalkRec(container.hierarchy[i], null, items);
        }
    }

    private static VisualElement FindDescendantOfType(VisualElement root, string typeName)
    {
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var child = root.hierarchy[i];
            if (child.GetIl2CppType().Name == typeName) return child;
            var deeper = FindDescendantOfType(child, typeName);
            if (deeper != null) return deeper;
        }
        return null;
    }

    // Consolidate one stage's "FixturesList" into a FixturesItem: the stage name (folded out of the
    // stray Text stop it was walked as) plus one MatchRow per match tile. A build failure degrades to
    // the normal recursion so the matches still render as flat stops rather than vanishing.
    private static void EmitFixturesTable(VisualElement fixturesList, List<RenderItem> items)
    {
        try
        {
            var tiles = new List<VisualElement>();
            CollectMatchTiles(fixturesList, tiles);

            // No match tiles means this list is not (or not yet) the competition-fixtures shape;
            // an empty consolidated table here would swallow whatever the list really holds, so
            // degrade to the recursion and render its actual content. Checked before the stage-name
            // fold so this path never eats the name's Text stop.
            if (tiles.Count == 0)
            {
                int n = fixturesList.hierarchy.childCount;
                for (int i = 0; i < n; i++)
                    WalkRec(fixturesList.hierarchy[i], null, items);
                return;
            }

            string stageName = null;
            var stageItem = fixturesList.hierarchy.parent;
            if (stageItem != null)
            {
                var nameVisible = FindDescendantNamed(stageItem, "StageNameVisible");
                var nameText = nameVisible != null ? FindDescendantNamed(nameVisible, "NameText") : null;
                if (nameText != null)
                {
                    stageName = ReadSITextValue(nameText);
                    // The stage name was already walked as a Text stop just before this list; drop it
                    // so it is not read twice (it becomes the table heading instead).
                    SuppressStopFor(items, nameText);
                }
            }

            var paged = PageMatchTiles(fixturesList, tiles, out string path, out int page, out int pageCount);
            var rows = new List<MatchRow>(paged.Count);
            foreach (var tile in paged)
            {
                var row = BuildMatchRow(tile);
                if (row != null) rows.Add(row);
            }

            items.Add(new FixturesItem
            {
                StageName = stageName, Matches = rows, Path = path, Page = page, PageCount = pageCount
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Fixtures table build failed, recursing as plain stops: {ex.Message}");
            int count = fixturesList.hierarchy.childCount;
            for (int i = 0; i < count; i++)
                WalkRec(fixturesList.hierarchy[i], null, items);
        }
    }

    // The Stages cup-round bracket consolidated into a FixturesItem (the Results screen's table).
    // Each visible "MainPart" is one tie; the per-row competition badges are deliberately not
    // carried over — the whole card is scoped to the competition named in its own header dropdown.
    // No ties (an unexpected shape) or a build failure degrades to the recursion, so nothing the
    // round really holds is ever swallowed.
    private static void EmitStageRoundTable(VisualElement round, List<RenderItem> items)
    {
        try
        {
            var mains = new List<VisualElement>();
            CollectVisibleDescendantsNamed(round, "MainPart", mains);
            var paged = PageMatchTiles(round, mains, out string path, out int page, out int pageCount);
            var rows = new List<MatchRow>(paged.Count);
            foreach (var main in paged)
            {
                var row = BuildStageRow(main);
                if (row != null) rows.Add(row);
            }
            if (rows.Count == 0)
            {
                int n = round.hierarchy.childCount;
                for (int i = 0; i < n; i++)
                    WalkRec(round.hierarchy[i], null, items);
                return;
            }
            items.Add(new FixturesItem { Matches = rows, Path = path, Page = page, PageCount = pageCount });
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Stage round table build failed, recursing as plain stops: {ex.Message}");
            int n = round.hierarchy.childCount;
            for (int i = 0; i < n; i++)
                WalkRec(round.hierarchy[i], null, items);
        }
    }

    // One bracket tie. A side is a settled team, or the candidate teams of an unresolved previous
    // tie (the game stacks them: the first becomes the cell's main entry, the rest ride as extras
    // in the same cell), optionally captioned by the prev-stage text, which rides as the cell's
    // position suffix. The centre cell is the Versus/result marker read live ("V", or the game's
    // result letters once played) — the one field that degrades to a marker when unreadable.
    private static MatchRow BuildStageRow(VisualElement main)
    {
        var left = FindDescendantNamed(main, "LeftSide");
        var right = FindDescendantNamed(main, "RightSide");
        if (left == null || right == null) return null;

        var row = new MatchRow();
        var home = CollectStageTeamButtons(left);
        var away = CollectStageTeamButtons(right);
        row.HomeTeam = home.Count > 0 ? home[0] : null;
        if (home.Count > 1) row.HomeTeamExtra = home.GetRange(1, home.Count - 1);
        row.AwayTeam = away.Count > 0 ? away[0] : null;
        if (away.Count > 1) row.AwayTeamExtra = away.GetRange(1, away.Count - 1);
        row.HomePosition = StagePrevText(left);
        row.AwayPosition = StagePrevText(right);

        var versus = FindDescendantNamed(main, "Versus");
        var texts = new List<string>(4);
        if (versus != null) CollectTexts(versus, texts, 4);
        row.Score = texts.Count > 0 ? string.Join(" ", texts)
                                    : Degrade.Value("stages: versus marker unreadable");

        row.HomeScorers = new List<(string, string)>();
        row.AwayScorers = new List<(string, string)>();
        return row;
    }

    // Team-name buttons on one side, document order, shallow-first: an SIButton carrying a
    // readable name text is one team entry, and the walk does not descend into it — the crest
    // button nested inside each entry has no text of its own and is never collected.
    private static List<IAccessibleElement> CollectStageTeamButtons(VisualElement side)
    {
        var teams = new List<IAccessibleElement>();
        CollectStageTeamButtonsRec(side, teams);
        return teams;
    }

    private static void CollectStageTeamButtonsRec(VisualElement el, List<IAccessibleElement> teams)
    {
        if (!ElementBridge.IsVisible(el)) return;
        if (el.GetIl2CppType().Name == "SIButton"
            && !string.IsNullOrEmpty(CleanOrNull(ReadFirstTextUnder(el, "text"))))
        {
            try { teams.Add(ElementBridge.CreateAdapter(el, ElementClassification.Button)); }
            catch (Exception ex) { Degrade.Note($"stages: team adapter failed: {ex.Message}"); }
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            CollectStageTeamButtonsRec(el.hierarchy[i], teams);
    }

    // The side's prev-stage caption ("Winner of ..."), when the game shows one. The container
    // name carries the game's own typo.
    private static string StagePrevText(VisualElement side)
    {
        var prev = FindDescendantNamed(side, "PrevStageTextContair");
        if (prev == null || !ElementBridge.IsVisible(prev)) return null;
        return CleanOrNull(ReadFirstText(prev));
    }

    // Unlike CollectDescendantsNamed below, this skips invisible subtrees: a virtualised list
    // keeps recycled, hidden row templates that must not become table rows.
    private static void CollectVisibleDescendantsNamed(VisualElement el, string name, List<VisualElement> found)
    {
        if (!ElementBridge.IsVisible(el)) return;
        if (el.name == name) { found.Add(el); return; }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            CollectVisibleDescendantsNamed(el.hierarchy[i], name, found);
    }

    // A match tile is the element holding both a "HomeTeam" and an "AwayTeam" frame. Collected
    // shallow-first: once a tile is found the walk does not descend into it (the scorer lists inside
    // are read by BuildMatchRow, not as nested tiles).
    private static void CollectMatchTiles(VisualElement el, List<VisualElement> tiles)
    {
        if (!ElementBridge.IsVisible(el)) return;
        if (HasChildNamed(el, "HomeTeam") && HasChildNamed(el, "AwayTeam")) { tiles.Add(el); return; }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            CollectMatchTiles(el.hierarchy[i], tiles);
    }

    // Rows per page of a consolidated fixtures table. Matches the streamed tables' page size so the
    // player's two paged surfaces step at the same rate.
    private const int FixturePageRows = StreamedTableLoader.PageRows;

    // The window of match tiles belonging to the page the player is on, and the paging state that
    // describes it. Every tile is still COLLECTED (the count is what makes the page count honest);
    // only the window is read into rows, and reading a row — team adapters, scorers, per-match
    // controls — is the expensive part: at ~38ms a tile, a 290-tie cup round cost 11s of main
    // thread per compose, longer than the compose's own timeout, so the surface never rendered it
    // at all and every retry froze the game again.
    //
    // Keyed on the list root's identity path, the same key the streamed tables page on, so the
    // stored page survives a refresh and is cleared with the screen (TablePages.SyncScreen).
    private static List<VisualElement> PageMatchTiles(VisualElement listRoot, List<VisualElement> tiles,
                                                      out string path, out int page, out int pageCount)
    {
        path = ServerHandles.IdentityPath(listRoot);
        pageCount = TablePaging.PageCount(tiles.Count, FixturePageRows);
        page = TablePaging.Clamp(TablePages.Get(path), pageCount);
        if (pageCount <= 1) return tiles;

        var (start, count) = TablePaging.Window(tiles.Count, FixturePageRows, page);
        return tiles.GetRange(start, count);
    }

    // Read one match tile into a MatchRow. Each field is matched structurally (frame/switch names that
    // are stable across data), tolerating absent optionals (no league position on a knockout tie, no
    // scorers in a goalless or unplayed match). The score is the one field that degrades to a marker
    // when unreadable rather than rendering blank, since a blank score reads as "no data" not "gap".
    private static MatchRow BuildMatchRow(VisualElement tile)
    {
        var home = FindChildNamed(tile, "HomeTeam");
        var away = FindChildNamed(tile, "AwayTeam");
        if (home == null || away == null) return null;

        var row = new MatchRow();

        // Status: "FT", a live clock, or the kickoff time, in the "Time" frame.
        var timeEl = FindDescendantNamed(tile, "Time");
        row.Status = timeEl != null ? ReadFirstText(timeEl) : null;

        // Score: the "hasResult" switch shows the result, or the kickoff time when unplayed.
        var scoreSwitch = FindDescendantNamed(tile, "hasResult");
        row.Score = scoreSwitch != null ? ReadFirstText(scoreSwitch) : null;
        if (string.IsNullOrEmpty(row.Score))
            row.Score = Degrade.Value("fixtures: match score unreadable");

        row.HomeTeam = BuildTeamAdapter(home);
        var homePos = FindDescendantNamed(home, "HomeLeaguePositionAndNation");
        row.HomePosition = homePos != null ? ReadFirstText(homePos) : null;
        row.HomeScorers = CollectScorers(home);

        row.AwayTeam = BuildTeamAdapter(away);
        var awayPos = FindDescendantNamed(away, "AwayLeaguePositionAndNation");
        row.AwayPosition = awayPos != null ? ReadFirstText(awayPos) : null;
        row.AwayScorers = CollectScorers(away);

        // Skip the subtrees already represented by a dedicated column (teams, status, score). The
        // score cell in particular is a switch that toggles between the score/"vs" and a hidden
        // "CanAttend" checkbox branch; mining it would surface that inactive checkbox as a phantom
        // control. The real per-match controls (the match-action dropdown, view button) live outside
        // these, in the tile's trailing frame.
        row.Options = CollectMatchOptions(tile, new[] { home, away, timeEl, scoreSwitch });

        return row;
    }

    // Every interactive control in the match tile that the Status/Home/Score/Away/Scorers columns
    // don't already render: the match-options dropdown, the view button, etc. The column-covered
    // subtrees (passed in) are skipped; plain text and images are skipped (covered by a column, or
    // decorative); an unreadable leaf still surfaces as an honest "unknown control" rather than
    // being dropped.
    private static List<IAccessibleElement> CollectMatchOptions(VisualElement tile, VisualElement[] covered)
    {
        var options = new List<IAccessibleElement>();
        CollectOptionsRec(tile, covered, options);
        return options;
    }

    private static void CollectOptionsRec(VisualElement el, VisualElement[] covered, List<IAccessibleElement> options)
    {
        if (!ElementBridge.IsVisible(el)) return;
        foreach (var c in covered) if (c?.Pointer == el.Pointer) return;

        ElementClassification cls;
        try
        {
            var descriptor = ElementBridge.Describe(el, null);
            cls = ElementBridge.ClassifyDescriptor(descriptor, el);
        }
        catch (Exception ex) { Log.Warn($"[AccessibilityServer] Fixtures option classify failed, skipping subtree: {ex.Message}"); return; }

        if (cls == ElementClassification.InternalChild) return;

        // Mirror WalkRec's Unknown rule: recurse so real controls inside still surface; only a leaf
        // unknown becomes an "unknown control" stop, the same honest gap the rest of the surface shows.
        if (cls == ElementClassification.Unknown)
        {
            int before = options.Count;
            int kids = el.hierarchy.childCount;
            for (int i = 0; i < kids; i++) CollectOptionsRec(el.hierarchy[i], covered, options);
            if (options.Count == before)
            {
                try { options.Add(ElementBridge.CreateAdapter(el, cls)); }
                catch (Exception ex) { Log.Warn($"[AccessibilityServer] Fixtures unknown option adapter failed: {ex.Message}"); }
            }
            return;
        }

        // An interactive control (not plain text, not a decorative/leaf image): surface it, then stop
        // descending (its own label/value render from the adapter).
        if (TreeWalker.NavigationStops.Contains(cls)
            && cls != ElementClassification.Text
            && cls != ElementClassification.Image)
        {
            try
            {
                var adapter = ElementBridge.CreateAdapter(el, cls);
                if (!NavigationManager.IsHidden(adapter))
                    options.Add(adapter);
            }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Fixtures option adapter failed: {ex.Message}"); }
            return;
        }

        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            CollectOptionsRec(el.hierarchy[i], covered, options);
    }

    // The clickable team control inside a HomeTeam/AwayTeam frame. National-team fixtures name it
    // "NationalTeamButton"; club fixtures name it "ClubButton" (the team-name button is the outer one,
    // found first by document order; the badge button nested inside it shares the name). Only the
    // active side's switch (NationVisible vs ClubVisible) is populated, so trying nation first then
    // club never crosses them. A missing button degrades the cell to a marker rather than guessing,
    // so the player sees an honest gap instead of a wrong team.
    private static IAccessibleElement BuildTeamAdapter(VisualElement teamFrame)
    {
        var btn = FindDescendantNamed(teamFrame, "NationalTeamButton")
                  ?? FindDescendantNamed(teamFrame, "ClubButton");
        if (btn == null) { Degrade.Note("fixtures: team button missing in match tile"); return null; }
        try { return ElementBridge.CreateAdapter(btn, ElementClassification.Button); }
        catch (Exception ex) { Degrade.Note($"fixtures: team adapter failed: {ex.Message}"); return null; }
    }

    // The goalscorers on one side: each entry is a small frame carrying an "IncidentIcon" plus the
    // player name and the minute (e.g. "John McGinn", "48'"; penalties read "pen 86'"). The two
    // texts come out in document order (name then minute) on both the home and away layouts.
    private static List<(string Name, string Minute)> CollectScorers(VisualElement teamFrame)
    {
        var list = new List<(string, string)>();
        CollectScorersRec(teamFrame, list);
        return list;
    }

    private static void CollectScorersRec(VisualElement el, List<(string, string)> list)
    {
        if (!ElementBridge.IsVisible(el)) return;
        if (HasChildNamed(el, "IncidentIcon"))
        {
            var texts = new List<string>(2);
            CollectTexts(el, texts, 2);
            if (texts.Count > 0)
                list.Add((texts[0], texts.Count > 1 ? texts[1] : ""));
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            CollectScorersRec(el.hierarchy[i], list);
    }

    // --- Tree read helpers (fixtures) ---

    private static VisualElement FindChildNamed(VisualElement parent, string name)
    {
        int n = parent.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            if (parent.hierarchy[i].name == name) return parent.hierarchy[i];
        return null;
    }

    private static bool HasChildNamed(VisualElement parent, string name) => FindChildNamed(parent, name) != null;

    // Fresh IL2CPP wrappers can represent the same native element. Match native identity,
    // as TabContext does, rather than the managed wrapper returned by a hierarchy read.
    private static bool SubtreeContainsAny(VisualElement root, List<VisualElement> targets)
    {
        for (int i = 0; i < targets.Count; i++)
            if (targets[i]?.Pointer == root.Pointer) return true;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            if (SubtreeContainsAny(root.hierarchy[i], targets)) return true;
        return false;
    }

    // The first non-empty SIText value anywhere under root (raw, with rich-text markup the renderer
    // cleans). Null when none is found.
    /// <summary>
    /// The Coach Report radar's own summary prose, which the card binds but does not display.
    ///
    /// The chart's figures reach the tree nowhere (see the caller), but the game writes a sentence
    /// naming where the player is above and below average for the selected position, into a text
    /// node beside the chart's subtitle that the layout leaves hidden. That paragraph is the only
    /// reading of this chart that exists in words, so a sighted player gets the polygon and a blind
    /// one would otherwise get nothing at all.
    ///
    /// It is live, not a leftover: verified 2026-08-22 to re-bind while hidden, both across players
    /// and across positions (switching a five-position player from Attacking Midfielder to
    /// Midfielder (Left) rewrote it from "attacking role / progressive passes / goals" to "creative
    /// role / crosses / dribbles" in the same frame the axis names changed).
    ///
    /// All-or-nothing, and deliberately narrow: exactly one hidden text under the chart's section,
    /// or nothing. Two would mean this is no longer the node identified here, and guessing which is
    /// the summary is how a panel starts speaking some other state's leftovers.
    /// </summary>
    private static void AppendCoachRadarTable(VisualElement chart, List<RenderItem> items)
    {
        string reason = "chart binding unreadable";
        List<List<string>> rows = null;
        try
        {
            var axes = RadarSeriesReader.TryRead(chart);
            if (axes != null) rows = RadarColumns.ValueTable(axes, CoachRadarHeaders.Length - 1, out reason);
        }
        catch (Exception ex) { reason = $"{ex.GetType().Name}: {ex.Message}"; }
        if (rows != null) { items.Add(new SyntheticTableItem { Headers = CoachRadarHeadersFor(chart), Rows = rows }); return; }
        if (_coachRadarMisses.Add(reason)) Degrade.Note("[CoachReport] radar figures left out, summary kept: " + reason);
    }

    // The average column named for the competition the radar's caption links ("Serie A, This Season"
    // links "Serie A"), so it holds for any league. Exactly one linked caption in the chart's section,
    // or the plain header.
    private static string[] CoachRadarHeadersFor(VisualElement chart)
    {
        var section = chart;
        for (int i = 0; i < RadarSectionAscend && section != null && section.name != RadarSectionElement; i++)
            section = section.hierarchy.parent;
        if (section == null || section.name != RadarSectionElement) return CoachRadarHeaders;

        var linked = new List<string>();
        CollectLinkedNames(section, linked, 4);
        if (linked.Count != 1) return CoachRadarHeaders;
        return new[] { CoachRadarHeaders[0], CoachRadarHeaders[1], linked[0] + CompetitionAverageSuffix };
    }

    private static void CollectLinkedNames(VisualElement el, List<string> into, int depth)
    {
        if (depth < 0 || into.Count > 1) return;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = el.hierarchy[i];
            if (!ElementBridge.IsVisible(c)) continue;
            if (c.GetIl2CppType().Name == "SIText")
            {
                var name = RadarColumns.LinkedName(c.Cast<TextElement>().text);
                if (name != null) into.Add(name);
                continue;
            }
            CollectLinkedNames(c, into, depth - 1);
        }
    }

    private const string SeasonStatsBlock = "HasStats";

    private static bool TryAppendSeasonStats(VisualElement block, List<RenderItem> items)
    {
        string reason;
        List<List<string>> rows = null;
        try
        {
            var texts = new List<string>();
            var rings = new List<string>();
            CollectStatsTexts(block, texts, rings, false, 0);
            if (rings.Count != 1) reason = $"{rings.Count} ring figures";
            else rows = CoachSeasonStats.Build(texts, rings[0], out reason);
        }
        catch (Exception ex) { reason = $"{ex.GetType().Name}: {ex.Message}"; }
        if (rows != null) { items.Add(new SyntheticTableItem { Headers = CoachSeasonStats.Headers, Rows = rows }); return true; }
        if (_coachRadarMisses.Add("season stats: " + reason)) Degrade.Note("[CoachReport] season stats walked as text: " + reason);
        return false;
    }

    // The block's visible texts in order; a text inside the ring gauge goes to rings instead.
    private static void CollectStatsTexts(VisualElement el, List<string> texts, List<string> rings, bool inRing, int depth)
    {
        if (depth > 12) return;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = el.hierarchy[i];
            if (!ElementBridge.IsVisible(c)) continue;
            string type = c.GetIl2CppType().Name;
            if (type == "SIText")
            {
                var t = SyntheticTables.CleanOrNull(ReadFirstText(c));
                if (t != null) (inRing ? rings : texts).Add(t);
                continue;
            }
            CollectStatsTexts(c, texts, rings, inRing || type == "SIRadialGauge", depth + 1);
        }
    }

    // The position caption's frame: the first child of the chart section's inner wrapper.
    private const string RadarCaptionFrame = "Frame";

    private static bool IsCoachRadarCaption(VisualElement frame)
    {
        var wrapper = frame.hierarchy.parent;
        var section = wrapper?.hierarchy.parent;
        return section != null && section.name == RadarSectionElement
            && wrapper.hierarchy.childCount > 1 && wrapper.hierarchy[0].Pointer == frame.Pointer;
    }

    private static void AppendRadarSummary(VisualElement chart, List<RenderItem> items)
    {
        var section = chart;
        for (int i = 0; i < RadarSectionAscend && section != null; i++)
        {
            if (section.name == RadarSectionElement) break;
            section = section.hierarchy.parent;
        }
        if (section == null || section.name != RadarSectionElement) return;

        var hidden = new List<string>();
        CollectHiddenTexts(section, hidden, 6);
        if (hidden.Count != 1) return;

        // Split before cleaning: the game writes the summary as blank-line-separated sentences, and
        // CleanForDisplay collapses whitespace, so cleaning first fuses all three into one run-on
        // paragraph. One <p> per sentence gives a screen reader the breaks the prose was written
        // with. Cleaned per paragraph rather than at render because NoteItem is chrome-escaped and
        // this is game text arriving with the panel's rich-text markup on it.
        foreach (var para in hidden[0].Split(RadarSummaryBreaks, StringSplitOptions.RemoveEmptyEntries))
        {
            var text = TextFilters.CleanForDisplay(ServerText.DecodeInlineEntityLinks(para));
            if (!string.IsNullOrWhiteSpace(text)) items.Add(new NoteItem { Text = text.Trim() });
        }
    }

    private static void CollectHiddenTexts(VisualElement root, List<string> into, int depth)
    {
        if (depth < 0 || into.Count > 1) return;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = root.hierarchy[i];
            if (c.GetIl2CppType().Name == "SIText")
            {
                if (ElementBridge.IsVisible(c)) continue;
                var t = ReadSITextValue(c);
                if (!string.IsNullOrWhiteSpace(t)) into.Add(t);
                continue;
            }
            CollectHiddenTexts(c, into, depth - 1);
        }
    }

    internal static string ReadFirstText(VisualElement root)
    {
        if (root.GetIl2CppType().Name == "SIText")
        {
            var t = ReadSITextValue(root);
            if (!string.IsNullOrEmpty(t)) return t;
        }
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var r = ReadFirstText(root.hierarchy[i]);
            if (r != null) return r;
        }
        return null;
    }

    // The first `max` non-empty SIText values under root, in document order.
    private static void CollectTexts(VisualElement root, List<string> texts, int max)
    {
        if (texts.Count >= max) return;
        if (root.GetIl2CppType().Name == "SIText")
        {
            var t = ReadSITextValue(root);
            if (!string.IsNullOrEmpty(t)) texts.Add(t);
        }
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n && texts.Count < max; i++)
            CollectTexts(root.hierarchy[i], texts, max);
    }

    // --- Column summary (the Coach Assignments footer) ---

    // The footer pairs a label frame ("frame0": the game's summary-row names — Coach Workload,
    // Lead Coach Rating) with a value frame ("Frame 1000002441") holding one "Frame 1000002425"
    // column per training category, each stacking one value per summary row (a workload gauge,
    // a star rating) in the same document order as the labels. Reads everything live at walk
    // time. False when the expected frames or texts are absent, so the caller falls back to the
    // generic walk and nothing vanishes.
    private static bool TryEmitColumnSummary(VisualElement el, List<RenderItem> items)
    {
        var labelFrame = FindChildNamed(el, "frame0");
        var valueFrame = FindChildNamed(el, "Frame 1000002441");
        if (labelFrame == null) return false;

        var labels = new List<string>();
        CollectTexts(labelFrame, labels, 4);
        if (labels.Count == 0) return false;

        var columns = new List<VisualElement>();
        if (valueFrame != null)
            CollectDescendantsNamed(valueFrame, "Frame 1000002425", columns);
        else if (labels.Count == 2)
            columns = CompactSummaryColumns(el, labelFrame);
        if (columns.Count == 0) return false;

        var cache = new ClassificationCache();
        var values = new List<List<string>>(columns.Count);
        foreach (var column in columns)
        {
            var cells = new List<VisualElement>();
            int n = column.hierarchy.childCount;
            for (int i = 0; i < n; i++)
                if (ElementBridge.IsVisible(column.hierarchy[i]))
                    cells.Add(column.hierarchy[i]);

            var colValues = new List<string>(labels.Count);
            for (int j = 0; j < labels.Count; j++)
                colValues.Add(j < cells.Count
                    ? ReadStopTexts(cells[j], cache)
                    : Degrade.Value($"column summary cell missing under '{labels[j]}'"));
            values.Add(colValues);
        }

        items.Add(new ColumnSummaryItem
        {
            ValueLabels = labels,
            Categories = SummaryCategories(el, columns.Count, cache),
            Values = values,
        });
        return true;
    }

    // FM27 removed the generated frame names from this shared footer. Its
    // native value component still owns one column per category, each with a
    // workload gauge followed by a star rating. Validate the entire structure
    // before feeding the unchanged FM26 column-summary item and renderer.
    private static List<VisualElement> CompactSummaryColumns(VisualElement footer, VisualElement labels)
    {
        var columns = new List<VisualElement>();
        VisualElement component = null;
        for (int i = 0; i < footer.hierarchy.childCount; i++)
        {
            var child = footer.hierarchy[i];
            if (child.Pointer == labels.Pointer || !ElementBridge.IsVisible(child)) continue;
            if (component != null || child.GetIl2CppType().Name != "ComponentElement") return columns;
            component = child;
        }
        if (component == null || component.hierarchy.childCount != 1) return columns;
        var values = component.hierarchy[0];
        for (int i = 0; i < values.hierarchy.childCount; i++)
        {
            var column = values.hierarchy[i];
            if (!ElementBridge.IsVisible(column)) continue;
            if (column.hierarchy.childCount != 2
                || FindDescendantOfType(column.hierarchy[0], "SIRadialGauge") == null
                || FindDescendantOfType(column.hierarchy[1], "FMStarRatingTemp") == null)
                return new List<VisualElement>();
            columns.Add(column);
        }
        return columns;
    }

    // The names of the summary's value columns: the assignment table's own column headers,
    // aligned from the right — the table's leading columns (row selection, coach) have no
    // summary beneath them. The table is the footer's sibling StreamedTable, reached from their
    // shared grandparent. When the headers cannot be paired up, every name degrades to a marker
    // rather than guessing an alignment.
    private static List<string> SummaryCategories(VisualElement footer, int count, ClassificationCache cache)
    {
        var result = new List<string>(count);
        var host = footer.hierarchy.parent?.hierarchy.parent;
        var table = host == null ? null : FindDescendantNamed(host, "StreamedTable");
        if (table == null && host != null)
        {
            var tables = new List<VisualElement>();
            CollectDescendantsOfType(host, "StreamedTable", tables, 5);
            if (tables.Count == 1) table = tables[0];
        }
        if (table != null)
        {
            var headers = new Contexts.TableContext(table, cache).GetHeaders();
            if (headers.Count >= count)
            {
                for (int i = headers.Count - count; i < headers.Count; i++)
                    result.Add(headers[i].Text);
                return result;
            }
        }
        Degrade.Note($"column summary categories unresolved ({(table == null ? "no sibling table" : "fewer headers than summary columns")})");
        for (int i = 0; i < count; i++)
            result.Add(Degrade.Unavailable);
        return result;
    }

    // Every readable stop under the frame joined in document order: the workload gauge's text,
    // the star-rating widget's value. A frame with nothing readable degrades to a marker — an
    // empty cell would be indistinguishable from "no value in the game".
    private static string ReadStopTexts(VisualElement frame, ClassificationCache cache)
    {
        var stops = TreeWalker.FindAllStopsInContainer(frame, cache);
        var parts = new List<string>(stops.Count);
        foreach (var stop in stops)
        {
            var t = DisplayText.Of(stop);
            if (!string.IsNullOrEmpty(t)) parts.Add(t);
        }
        if (parts.Count == 0) return Degrade.Value("column summary value had no readable stop");
        return string.Join(", ", parts);
    }

    // All descendants with the given name, document order. Does not descend into a match (the
    // summary's category columns never nest).
    private static void CollectDescendantsNamed(VisualElement root, string name, List<VisualElement> found)
    {
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var child = root.hierarchy[i];
            if (child.name == name) { found.Add(child); continue; }
            CollectDescendantsNamed(child, name, found);
        }
    }

    // Visible descendants of a given IL2CPP type, not descending past a match (a matched element's
    // own subtree is that element's content, not a sibling row). Depth-bounded so a deep panel
    // cannot turn a shallow structural lookup into a full-tree walk.
    private static void CollectDescendantsOfType(VisualElement root, string typeName, List<VisualElement> found, int depth)
    {
        if (depth < 0) return;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var child = root.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            if (string.Equals(child.GetIl2CppType().Name, typeName, StringComparison.Ordinal))
            {
                found.Add(child);
                continue;
            }
            CollectDescendantsOfType(child, typeName, found, depth - 1);
        }
    }

    // Drop a just-emitted Text stop that has been folded elsewhere (the stage name into the table
    // heading). Scans only the tail, matching by element identity, so unrelated stops are untouched.
    private static void SuppressStopFor(List<RenderItem> items, VisualElement backing)
    {
        for (int i = items.Count - 1; i >= 0 && i >= items.Count - 4; i--)
            if (items[i] is StopItem s && s.Element is Adapters.PluginAdapterBase a
                && a.BackingElement.Pointer == backing.Pointer)
            {
                items.RemoveAt(i);
                return;
            }
    }

    // --- Schedule calendar ---

    // Consolidate the schedule calendar (StreamedObjectList "Weeks") into a CalendarItem: the month as
    // week rows, each a Monday..Sunday array of populated day cells (date, state, events) or padding,
    // plus the week's training focus card as the row's header where the tab draws one. A read failure,
    // or a month that reads as empty, degrades to the normal recursion so any real content still
    // renders rather than vanishing.
    private static void EmitCalendar(VisualElement weeksContainer, List<RenderItem> items)
    {
        List<Contexts.CalendarTree.Week> weeks;
        try { weeks = Contexts.CalendarTree.ReadWeeks(weeksContainer); }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Calendar read failed, recursing as plain stops: {ex.Message}");
            weeks = null;
        }

        if (weeks == null || weeks.Count == 0)
        {
            int count = weeksContainer.hierarchy.childCount;
            for (int i = 0; i < count; i++)
                WalkRec(weeksContainer.hierarchy[i], null, items);
            return;
        }

        bool training = IsTrainingCalendar(weeksContainer);

        var rows = new List<CalendarWeekRow>(weeks.Count);
        foreach (var week in weeks)
        {
            var row = new CalendarWeekRow();
            for (int col = 0; col < 7; col++)
            {
                var dayCell = week.Days[col];
                if (dayCell == null) continue;
                try { row.Days[col] = BuildCalendarDay(dayCell, col, training,
                    weeksContainer.GetIl2CppType().Name == "Calendar"); }
                catch (Exception ex) { Log.Warn($"[AccessibilityServer] Calendar day build failed, skipping cell: {ex.Message}"); }
            }
            if (week.FocusCard != null)
            {
                try { row.Focus = new StopItem { Element = new Adapters.CalendarWeekFocusAdapter(week.FocusCard) }; }
                catch (Exception ex) { Log.Warn($"[AccessibilityServer] Calendar week focus build failed, row loses its header: {ex.Message}"); }
            }
            if (week.FocusContainer != null || week.SharedContent != null)
            {
                row.FocusItems = new List<RenderItem>();
                if (week.FocusContainer != null) WalkRec(week.FocusContainer, null, row.FocusItems);
                if (week.SharedContent != null)
                    foreach (var content in week.SharedContent) WalkRec(content, null, row.FocusItems);
            }
            rows.Add(row);
        }
        items.Add(new CalendarItem { Weeks = rows,
            WeekdayLabels = Contexts.CalendarTree.ReadWeekdayLabels(weeksContainer) });
    }

    // The calendar consolidation knows its container, so it tests the Training tab once per calendar
    // rather than per slot; the per-slot "is there anything behind this" test is the same one the
    // speech surface applies through ElementBridge (CalendarSlotTextAdapter.HasAction), so the two
    // surfaces can never disagree about which slots are inert.
    private static bool IsTrainingCalendar(VisualElement weeksContainer)
    {
        for (var cur = weeksContainer; cur != null; cur = cur.hierarchy.parent)
            if (cur.name == Adapters.CalendarSlotTextAdapter.TrainingGridName) return true;
        return false;
    }

    // One day cell into a CalendarDayCell: its date text and state (read through CalendarDayAdapter, the
    // same source the speech surface uses) and its events. col is the Monday..Sunday column, naming the
    // weekday for the adapter. An unreadable date degrades to a marker rather than a blank cell. Each
    // event tile is wrapped in a CalendarEventAdapter so activating it opens the event's context menu
    // (its real interaction), rather than the inert generic-button click WalkChildren would have given.
    // On the Training tab a tile with no action behind it at all is demoted to a text stop instead
    // (CalendarSlotTextAdapter) — it keeps its stop and its label, it just stops claiming to be a control.
    private static CalendarDayCell BuildCalendarDay(VisualElement dayCell, int col, bool training, bool compact = false)
    {
        string weekday = Navigation.CalendarScheduleHtml.Weekdays[col];
        var adapter = new Adapters.CalendarDayAdapter(dayCell, weekday);
        string date = Contexts.CalendarTree.ReadDayDate(dayCell) ?? Degrade.Value("calendar: day date unreadable");
        string state = adapter.GetState();

        var events = new List<RenderItem>();
        var eventsList = FindDescendantNamed(dayCell, "EventsList");
        if (compact)
        {
            // The compact native calendar separates icon-only event content
            // from its linked hit button. Preserve both through the ordinary
            // walker inside the existing day-cell HTML, rather than collecting
            // only SIButtons and losing the three training categories/badges.
            var excess = FindDescendantNamed(dayCell, "ExcessEvents");
            if (excess != null && !string.IsNullOrWhiteSpace(ReadFirstText(excess)))
                WalkRec(excess, null, events);
            if (eventsList != null) WalkRec(eventsList, null, events);
        }
        else if (eventsList != null)
        {
            CollectCalendarDayEvents(eventsList, events);
        }

        return new CalendarDayCell { DateText = date, State = state, Events = events,
            EventsList = eventsList, Provisional = CalendarEventsPending(eventsList) };
    }

    internal static bool CalendarEventsPending(VisualElement list)
    {
        if (list == null || !ElementBridge.IsVisibleInHierarchy(list)) return false;
        var readiness = Contexts.TableContext.ReadBindingReadiness(list);
        if (readiness.OptionalContentPending) return true;
        if (readiness.ObservedBindings != 0) return false;
        var windows = NativeTreeQuery.All<SI.UI.VirtualisedList>(list);
        // A genuinely empty day remains empty. A native nonempty event list
        // whose row shells have no bindings is provisional, exactly like a
        // required streamed-table body row. Reuse the bounded recovery budget.
        return windows.Count == 1 && ListWindowSurface.ReadItemCount(windows[0]) > 0;
    }

    private static void CollectCalendarDayEvents(VisualElement element, List<RenderItem> events)
    {
        if (!ElementBridge.IsVisible(element)) return;
        if (element.name == "CalendarEventBindingExpect")
        {
            var tiles = new List<VisualElement>();
            CollectEventTiles(element, tiles);
            // FM27 also binds readonly fixture events with native opponent,
            // competition and venue content but no SIButton. An empty command
            // collection does not mean an empty event. Keep its ordinary native
            // text/links/images in this same day cell and original event order.
            if (tiles.Count == 0) WalkRec(element, null, events);
            else foreach (var tile in tiles) AddCalendarEvent(tile, events);
            return;
        }
        if (element.GetIl2CppType().Name == "SIButton")
        {
            AddCalendarEvent(element, events);
            return;
        }
        for (int i = 0; i < element.hierarchy.childCount; i++)
            CollectCalendarDayEvents(element.hierarchy[i], events);
    }

    private static void AddCalendarEvent(VisualElement tile, List<RenderItem> events)
        => events.Add(new StopItem
        {
            Element = !Adapters.CalendarSlotTextAdapter.HasAction(tile)
                ? new Adapters.CalendarSlotTextAdapter(tile)
                : new Adapters.CalendarEventAdapter(tile),
        });

    // The compact Calendar separates the visible caption/icons from a linked
    // hit button. Apply the same caption-equivalence gate as CollectEventTiles,
    // retaining icons and any independent text/help instead of reading the same
    // caption twice. Ordinary DateGrid/legacy event rendering stays unchanged.
    private static bool TryEmitCompactCalendarEvent(VisualElement element, List<RenderItem> items)
    {
        bool compact = false;
        for (var up = element.parent; up != null; up = up.parent)
        {
            if (up.GetIl2CppType().Name == "Calendar" && up.TryCast<FM.UI.Widgets.Calendar>() != null)
            { compact = true; break; }
            if (up.GetIl2CppType().Name == "Panel") break;
        }
        if (!compact) return false;
        var link = FindDirectChildNamed(element, "EventLinkSwitch");
        if (link == null || !ElementBridge.IsVisible(link)) return false;
        var actions = new List<VisualElement>();
        CollectEventTiles(link, actions);
        if (actions.Count != 1 || !Adapters.CalendarEventAdapter.HasBoundCommand(actions[0])) return false;
        var visual = new List<RenderItem>();
        for (int i = 0; i < element.hierarchy.childCount; i++)
            if (element.hierarchy[i].Pointer != link.Pointer) WalkRec(element.hierarchy[i], null, visual);
        var textItems = new HashSet<RenderItem>();
        var captions = new List<string>();
        foreach (var item in visual)
            if (item is StopItem stop && stop.Element.Role == "text" && !stop.Element.CanActivate)
            {
                if (stop.Element.HasContextMenu || !string.IsNullOrEmpty(stop.Element.GetDescription())
                    || !string.IsNullOrEmpty(stop.Element.GetState())) return false;
                captions.Add(ServerText.Clean(stop.Element.GetLabel()));
                textItems.Add(item);
            }
        if (captions.Count == 0 || ServerText.Clean(string.Join(", ", captions))
            != ServerText.Clean(new Adapters.ButtonAdapter(actions[0]).GetLabel())) return false;
        foreach (var item in visual) if (!textItems.Contains(item)) items.Add(item);
        WalkRec(link, null, items);
        return true;
    }

    // The clickable event tiles inside a day's EventsList: each event is an SIButton ("FMVisual")
    // holding the event's texts and icons. Collected pre-order so they keep visual order (Training,
    // then Match, then the fixture result); the walk stops at each SIButton (the tile is the unit, its
    // inner texts are the label) and skips the list's own scroller RepeatButtons (not SIButtons).
    private static void CollectEventTiles(VisualElement el, List<VisualElement> tiles)
    {
        if (!ElementBridge.IsVisible(el)) return;
        if (el.name == "CalendarEventBindingExpect")
        {
            var link = FindDirectChildNamed(el, "EventLinkSwitch");
            if (link != null && ElementBridge.IsVisible(link))
            {
                var actions = new List<VisualElement>();
                var visuals = new List<VisualElement>();
                CollectEventTiles(link, actions);
                for (int i = 0; i < el.hierarchy.childCount; i++)
                    if (el.hierarchy[i].Pointer != link.Pointer) CollectEventTiles(el.hierarchy[i], visuals);
                // FM27 draws a legacy event visual and a separate linked hit
                // area for the same event. Keep the real action once only when
                // the complete captions agree; differing information stays.
                if (actions.Count == 1 && visuals.Count == 1
                    && Adapters.CalendarEventAdapter.HasBoundCommand(actions[0])
                    && ServerText.Clean(new Adapters.ButtonAdapter(actions[0]).GetLabel())
                        == ServerText.Clean(new Adapters.ButtonAdapter(visuals[0]).GetLabel()))
                { tiles.Add(actions[0]); return; }
            }
        }
        if (el.GetIl2CppType().Name == "SIButton") { tiles.Add(el); return; }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectEventTiles(el.hierarchy[i], tiles);
    }

    // --- Create-schedule week grid ---

    // The header row the editor's week grid declares for itself: the six per-day columns, in the
    // order the day's own lists fill them.
    private const string ScheduleWeekHeaderName = "WeekDays";

    // The grid is the element holding that header row. Matched on the header's name rather than on
    // the container's ("Calendar", which the schedule calendar also uses), so the two never collide.
    private static bool IsScheduleWeekGrid(VisualElement el)
        => FindDirectChildNamed(el, ScheduleWeekHeaderName) != null
           && FindDescendantNamed(el, "IntensitiesList") != null;

    // The grid into a ScheduleWeekItem: the game's own column headers, then one row per weekday
    // holding that day's session tiles (kept as live stops, so their menus still edit the session)
    // and its intensity bars (read as percentages). A read that finds no day degrades to the normal
    // recursion, so the editor still renders its raw stops rather than losing the week entirely.
    private static void EmitScheduleWeekGrid(VisualElement grid, List<RenderItem> items)
    {
        var item = new ScheduleWeekItem();

        // The header element holds both the column labels and the seven weekday rows. The labels sit
        // in its one non-weekday frame, so the collection is scoped to that frame — a depth bound
        // alone would also sweep up each day's own name text and turn the seven days into columns.
        var header = FindDirectChildNamed(grid, ScheduleWeekHeaderName);
        var labelFrame = FindHeaderLabelFrame(header);
        if (labelFrame != null)
        {
            var headerTexts = new List<VisualElement>();
            CollectDescendantsOfType(labelFrame, "SIText", headerTexts, 4);
            foreach (var headerText in headerTexts)
            {
                var text = headerText.TryCast<UnityEngine.UIElements.TextElement>();
                if (text != null && !string.IsNullOrEmpty(text.text)) item.Headers.Add(text.text);
            }
        }
        else
        {
            Log.Warn("[AccessibilityServer] Schedule week grid has no header frame; columns render unlabelled.");
        }

        foreach (var weekday in Navigation.CalendarScheduleHtml.Weekdays)
        {
            var dayRemapper = FindDescendantNamed(grid, weekday);
            if (dayRemapper == null) continue;

            var row = new ScheduleWeekRow { DayName = weekday };

            var eventsList = FindDescendantNamed(dayRemapper, "EventsList");
            if (eventsList != null)
            {
                var tiles = new List<VisualElement>();
                CollectEventTiles(eventsList, tiles);
                // ScheduleSessionAdapter, NOT CalendarEventAdapter: these tiles share the calendar
                // tile's template but invert its behaviour — they click and have no menu. See the
                // adapter for the evidence.
                foreach (var tile in tiles)
                    row.Sessions.Add(new StopItem { Element = new Adapters.ScheduleSessionAdapter(tile) });
            }

            var intensitiesList = FindDescendantNamed(dayRemapper, "IntensitiesList");
            if (intensitiesList != null)
            {
                var bars = new List<VisualElement>();
                // The bars sit deep inside the streamed list's scroll/virtualisation frames.
                CollectDescendantsOfType(intensitiesList, "SIProgressBar", bars, 12);
                foreach (var bar in bars)
                {
                    // Read through the same adapter the speech surface uses, so a cell and a spoken
                    // bar can never disagree about the fill.
                    try { row.Intensities.Add(new Adapters.ProgressBarAdapter(bar).GetState()); }
                    catch (Exception ex)
                    {
                        row.Intensities.Add(Degrade.Value($"schedule editor: intensity bar unreadable: {ex.Message}"));
                    }
                }
            }

            item.Rows.Add(row);
        }

        if (item.Rows.Count == 0)
        {
            Log.Warn("[AccessibilityServer] Schedule week grid found no days, recursing as plain stops.");
            int kids = grid.hierarchy.childCount;
            for (int i = 0; i < kids; i++) WalkRec(grid.hierarchy[i], null, items);
            return;
        }

        items.Add(item);

        // The grid element holds more than the week: the editor's Impacts block is its sibling under
        // the same parent. Consolidating the week and returning swallowed that block whole — the
        // projected-impact table vanished from the editor with no warning, because a consolidation
        // that returns early silently owns everything beneath it. Only the header element (the week
        // itself, now in the table) is consumed; every other child still walks.
        int count = grid.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = grid.hierarchy[i];
            if (child.name == ScheduleWeekHeaderName) continue;
            WalkRec(child, null, items);
        }
    }

    private static VisualElement FindDirectChildNamed(VisualElement parent, string name)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            if (parent.hierarchy[i].name == name) return parent.hierarchy[i];
        return null;
    }

    // The column-label frame among the header element's children: the one child that is not one of
    // the seven weekday rows sharing that parent.
    private static VisualElement FindHeaderLabelFrame(VisualElement header)
    {
        int count = header.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = header.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            if (Navigation.CalendarScheduleHtml.WeekdayIndex(child.name) >= 0) continue;
            return child;
        }
        return null;
    }

    // --- Render items ---

    // The editable enrichment for the interactable pitch: slot name -> the player's TacticsPlayerAdapter,
    // built from each occupied indicator's shirt-player-instruction-button (the control the role /
    // instructions / move actions drive). Read-only pitches get no map (null), so the grid renders
    // without actions. Only ever called on FMTacticsWithAnalysis, where the shirt button is live.
    private static Dictionary<string, Adapters.TacticsPlayerAdapter> BuildEditableMap(VisualElement pitch)
    {
        var map = new Dictionary<string, Adapters.TacticsPlayerAdapter>();
        var st = new Stack<VisualElement>();
        st.Push(pitch);
        while (st.Count > 0)
        {
            var e = st.Pop();
            if (e.GetIl2CppType().Name == "FMPitchIndicator")
            {
                var shirt = Tactics.PitchGrid.FindByName(e, "shirt-player-instruction-button");
                if (shirt != null && ElementBridge.IsVisible(shirt) && !string.IsNullOrEmpty(e.name))
                {
                    try
                    {
                        if (ElementBridge.CreateAdapter(shirt, ElementClassification.Button) is Adapters.TacticsPlayerAdapter tp)
                            map[e.name] = tp;
                    }
                    catch (Exception ex) { Log.Warn($"[AccessibilityServer] pitch editable adapter failed for {e.name}: {ex.Message}"); }
                }
                continue; // indicators don't nest
            }
            int c = e.hierarchy.childCount;
            for (int i = 0; i < c; i++) st.Push(e.hierarchy[i]);
        }
        return map;
    }

    // The label ButtonAdapter derives from the pitch's expand/collapse icon (sprite "expand_small_2x").
    private const string PitchCollapseLabel = "Collapse/Expand Pitch View";

    private static void PromotePitchPhaseCaptions(List<RenderItem> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not PitchGridItem { PhaseCaption: { } caption } pitch) continue;
            items.RemoveAll(item => item is StopItem { Element: Adapters.PluginAdapterBase source }
                && source.BackingElement.Pointer == caption.Pointer);
            i = items.IndexOf(pitch);
            int start = i;
            // A phase's ordinary formation/action controls precede its pitch
            // widget. Put the same native heading above those controls too;
            // the shared view-mode TabBarItem remains outside this grouping.
            if (pitch.PhaseScope != null)
                for (int j = 0; j < i; j++)
                {
                    if (items[j] is not StopItem { Element: Adapters.PluginAdapterBase source }) continue;
                    for (var owner = source.BackingElement; owner != null; owner = owner.parent)
                        if (owner.Pointer == pitch.PhaseScope.Pointer) { start = j; break; }
                    if (start != i) break;
                }
            items.Insert(start, new StopItem { Element = new Adapters.TextAdapter(caption), HeadingLevel = 3 });
            i = items.IndexOf(pitch);
        }
    }

    // HTML-only: the pitch is expanded by default and the grid reads the expanded pitch, so the
    // collapse/expand toggle is pure clutter here (and collapsing would hide the grid). Drop it from
    // the HTML surface, but only on the tactics pitch (gated on the PitchGridItem) so a stray
    // same-icon button elsewhere is never affected. The speech surface keeps the button.
    // The "All Instructions" button beside the In/Out of Possession tabs opened the game's flat
    // instructions card - fully superseded by the consolidated table (same list, same values,
    // richer editing, and the Feedback column now carries the detail modal's content too).
    // Removed only when the table actually rendered, so the tile-grid fallback keeps its card
    // doorway. Post-walk pass because the button walks out of the phases header BEFORE the
    // tiles anchor emits the table. Identified by its wrapper template name, not its label.
    private static void RemoveAllInstructionsButton(List<RenderItem> items)
    {
        bool hasTable = false;
        foreach (var it in items) if (it is TeamInstructionsItem) { hasTable = true; break; }
        if (!hasTable) return;
        items.RemoveAll(it => it is StopItem s
            && s.Element is Adapters.PluginAdapterBase a
            && a.BackingElement.hierarchy.parent?.name == "AllInstructionsButton");
    }

    private static void RemovePitchCollapseButton(List<RenderItem> items)
    {
        bool onPitch = false;
        foreach (var it in items) if (it is PitchGridItem { Editable.Count: > 0 }) { onPitch = true; break; }
        if (!onPitch) return;
        items.RemoveAll(it => it is StopItem s && DisplayText.Of(s.Element) == PitchCollapseLabel);
    }

    // The match header's stats-tile carousel, consolidated for the HTML surface (see the
    // BindableCarouselTileRoot branch in WalkRec). Returns false — so the caller recurses
    // generically — when the structure is not the recognised carousel shape (no ViewRoot, no
    // visible page, no readable title); the reader never guesses at a shape it does not know.
    private static bool TryEmitCarouselTile(VisualElement root, List<RenderItem> items)
    {
        var viewRoot = FindChildNamed(root, "ViewRoot");
        if (viewRoot == null) return false;

        // One child per carousel page; exactly one is visible at a time. Track the visible
        // page by INDEX — IL2CPP wrapper identity is not reference-comparable.
        var pages = new List<VisualElement>();
        int visibleIndex = -1;
        int pageCount = viewRoot.hierarchy.childCount;
        for (int i = 0; i < pageCount; i++)
        {
            var page = viewRoot.hierarchy[i];
            pages.Add(page);
            if (visibleIndex < 0 && ElementBridge.IsVisible(page)) visibleIndex = i;
        }
        if (visibleIndex < 0) return false;
        var visible = pages[visibleIndex];

        var item = BuildTileItem(visible);
        if (item == null) return false;

        // Every page becomes a switch target, driven by the carousel's own NavigateToPage(index)
        // (see HtmlRenderer.CarouselPageHandle) — pages are indexed in ViewRoot child order, the
        // same order the game adds them. AllPages (visible page included) feeds the tablist;
        // OtherPages keeps the hidden-only list for the reading view.
        item.CarouselRoot = root;
        item.VisiblePageIndex = visibleIndex;
        for (int i = 0; i < pages.Count; i++)
        {
            string title = i == visibleIndex
                ? item.Title
                : CleanOrNull(ReadFirstTextUnder(pages[i], "title-text")) ?? $"page {i + 1}";
            item.AllPages.Add(new CarouselPage { Title = title, Index = i });
            if (i != visibleIndex)
                item.OtherPages.Add(new CarouselPage { Title = title, Index = i });
        }

        items.Add(item);
        return true;
    }

    // The in-match score bug walks out in visual order (tactics strip, stats/settings toggles,
    // playback), which put the transport controls far from the page top and the Stats toggle far
    // from the widget it reveals. Reorder for the reading flow the player asked for: Play/Pause
    // then Settings first (they sit directly under the page-top score heading), the tactics
    // strip next, and the Stats toggle last so the stats widget it opens renders directly
    // beneath it, just above the Your Squad section. HTML-only — the speech cursor keeps the
    // walk order. Any control not found (template change) simply stays where it walked.
    // The "Sound cues settings" entry point: a synthetic trigger stop at the top of the in-match
    // settings card, opening the mod-authored sound-cue explainer/preview page (SoundCuesSurface,
    // modal while open like the bookmark editor). HTML-only — the page's previews play in the
    // companion's Web Audio, so the speech surface gets no dead trigger.
    private static void InsertSoundCuesTrigger(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != "MatchPreferencesCard") return;
        items.Insert(0, new StopItem { Element = new SoundCuesSurface.TriggerAdapter() });
    }

    private static void ReorderMatchHeaderControls(List<RenderItem> items, object panelObj)
    {
        if (NavigationManager.GetPanelName(panelObj) != "OverviewHeader") return;
        try
        {
            RenderItem playPause = null, settings = null, stats = null, carousel = null;
            foreach (var item in items)
            {
                if (item is CarouselTileItem) { carousel = item; continue; }
                if (item is not StopItem s || s.Element is not Adapters.PluginAdapterBase pab) continue;
                var el = pab.BackingElement;
                var name = el.name ?? "";
                if (name == "playpause") playPause = item;
                else if (name == "stats-button") stats = item;
                else if (name.Length == 0 && el.hierarchy.parent?.name == "settings") settings = item;
            }
            if (playPause == null && settings == null && stats == null && carousel == null) return;

            var reordered = new List<RenderItem>(items.Count);
            if (playPause != null) reordered.Add(playPause);
            if (settings != null) reordered.Add(settings);
            foreach (var item in items)
                if (!ReferenceEquals(item, playPause) && !ReferenceEquals(item, settings)
                    && !ReferenceEquals(item, stats) && !ReferenceEquals(item, carousel))
                    reordered.Add(item);
            // The toggle is rendered with an explicit expanded/collapsed state: the widget it
            // reveals renders directly beneath it, and this build knows whether it is open
            // (the carousel item exists exactly when it is).
            if (stats != null)
                reordered.Add(new StatsToggleItem { El = ((StopItem)stats).Element, Expanded = carousel != null });
            if (carousel != null) reordered.Add(carousel);

            items.Clear();
            items.AddRange(reordered);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Match header reorder failed, keeping walk order: {ex.Message}");
        }
    }

    // A standalone match-report tile (events, stats, latest scores, live table), read with the
    // same tile reader as a carousel page. See the wrapper-name branch in WalkRec.
    private static bool TryEmitStandaloneTile(VisualElement root, List<RenderItem> items)
    {
        // The same wrapper names also host a carousel (the half-time report's Match Momentum
        // carries the xG story as its second page): that is the carousel branch's to read, with
        // its page switcher, so decline and let the walk reach the BindableCarouselTileRoot.
        if (root.hierarchy.childCount > 0 && root.hierarchy[0].GetIl2CppType().Name == "BindableCarouselTileRoot")
            return false;
        var item = BuildTileItem(root);
        if (item == null) return false;
        items.Add(item);
        return true;
    }

    private static bool TryRestorePlainTileButton(VisualElement root, List<RenderItem> items, int from)
    {
        // FM26 leaves ordinary menu tiles as ButtonAdapter stops: their own
        // text is the label/description and Alt+I reads it on demand. The FM27
        // rich-body extension must not turn every titled button into a section.
        // Keep actual tables, value groups, informative media and other rich
        // items on that extension. Independently actionable child buttons stay
        // reachable as ordinary controls in their original native order.
        var controls = new List<RenderItem>();
        for (int i = from; i < items.Count; i++)
        {
            if (items[i] is not StopItem stop) return false;
            string role = ServerText.SafeRole(stop.Element);
            if (role == "button")
            {
                // Some native action tiles draw a second button-shaped call
                // to action inside their actual command wrapper. Without its
                // own Events/custom action it is presentation, not a second
                // command. Keep independently bound child actions unchanged.
                if (stop.Element is Adapters.ButtonAdapter child
                    && child.BackingElement.GetIl2CppType().Name == "SIButton"
                    && !Adapters.NativeButtonCommand.HasBoundCommand(child.BackingElement))
                    continue;
                controls.Add(stop);
            }
            else if (role != "text") return false;
        }
        var button = new Adapters.ButtonAdapter(root);
        // A fixed card can disable its outer hit surface. Leave its native
        // readonly title/content on the rich path rather than inventing an
        // unavailable menu action for that presentation wrapper.
        if (ServerText.IsDisabled(button) && !Adapters.NativeButtonCommand.HasBoundCommand(root)) return false;
        if (string.IsNullOrWhiteSpace(button.GetPrimaryLabel())) return false;
        items.RemoveRange(from, items.Count - from);
        items.Add(new StopItem { Element = button });
        items.AddRange(controls);
        return true;
    }

    private static bool IsCompactValueTile(VisualElement root)
    {
        var content = FindChildNamed(root, "states-templates-tile-content");
        var body = content == null ? null : FindChildNamed(content, "Body");
        var controls = body == null ? null : FindChildNamed(body, "ControlsArea");
        // These tiles are a single editable value, including native padlock
        // state. Keep the original ButtonAdapter label/value/help contract;
        // descending would split off the value and discard its locked state.
        return controls != null && FindChildNamed(controls, "NameAndHoverControls") != null;
    }

    private static bool TryEmitMatchStatsBox(VisualElement root, List<RenderItem> items, string title = null)
    {
        var item = new CarouselTileItem
        {
            Title = title ?? "Match statistics",
            HomeTeam = CleanOrNull(ReadFirstTextUnder(root.parent, "HomeTeam")),
            AwayTeam = CleanOrNull(ReadFirstTextUnder(root.parent, "AwayTeam")),
        };
        var rows = new List<VisualElement>();
        for (int c = 0; c < root.hierarchy.childCount; c++)
        {
            var column = root.hierarchy[c];
            if (!ElementBridge.IsVisible(column)) continue;
            if (FindChildNamed(column, "bar") != null) rows.Add(column);
            else for (int r = 0; r < column.hierarchy.childCount; r++)
            {
                var row = column.hierarchy[r];
                if (ElementBridge.IsVisible(row)) rows.Add(row);
            }
        }
        foreach (var row in rows)
        {
                var bar = FindChildNamed(row, "bar");
                var name = CleanOrNull(ReadFirstText(row));
                var home = bar == null ? null : FirstTextElement(FindChildNamed(bar, "Left-frame"));
                var away = bar == null ? null : FirstTextElement(FindChildNamed(bar, "Right-frame"));
                if (name == null || home == null || away == null) return false;
                item.Rows.Add(new CarouselStatRow { Name = name, Home = home, Away = away });
        }
        if (item.Rows.Count == 0 || item.Rows.Count > StreamedTableLoader.PageRows) return false;
        items.Add(item);
        return true;
    }

    // Retains the readonly title at its native position and restores a rich
    // wrapper's open action after its content. Ordinary flat action tiles use
    // the original ButtonAdapter path instead. Untitled pitch wrappers retain
    // their existing descent without inventing an action name.
    private static void PromoteTileTitleToActivator(VisualElement wrapper, List<RenderItem> items, int from)
    {
        var titleEl = ElementBridge.FindVisibleTileTitle(wrapper);
        if (titleEl == null) return;
        string title = CleanOrNull(ReadSITextValue(titleEl));
        if (title == null) return;

        // The title element itself is the only certain match, so it is searched for first: a stop
        // that merely READS like the title (a tile that repeats its name in its content) must not
        // be eaten, and is only accepted once the element itself has been ruled out.
        int at = IndexOfStopBackedBy(items, from, titleEl);
        if (at < 0) at = IndexOfStopReading(items, from, title);
        if (at < 0)
            items.Insert(from, new StopItem { Element = new Adapters.TextAdapter(titleEl) });
        // Retain the native readonly section title. Its separate open action
        // follows the contents instead of becoming an interactive heading.
        items.Add(new TileActivatorItem { Title = title, TileButton = wrapper });
    }

    private static int IndexOfStopBackedBy(List<RenderItem> items, int from, VisualElement element)
    {
        for (int i = from; i < items.Count; i++)
            if (items[i] is StopItem s && (s.Element as Adapters.PluginAdapterBase)?.BackingElement?.Pointer == element.Pointer)
                return i;
        return -1;
    }

    private static int IndexOfStopReading(List<RenderItem> items, int from, string text)
    {
        for (int i = from; i < items.Count; i++)
            if (items[i] is StopItem s && DisplayText.Of(s.Element) == text)
                return i;
        return -1;
    }

    // Reads one tile's content into a render item: its title and whole-tile button, then the
    // best structured reading its content supports — stat rows (a Data-Frame of name over
    // home/away value pairs), a formation pitch (a row per FMPitchIndicator player), a chart's
    // plotted figures, a report grid, or the visible texts as plain lines. Null when there is no readable title: the caller falls back
    // to the generic walk rather than emitting an unnamed widget.
    private static CarouselTileItem BuildTileItem(VisualElement root)
    {
        var item = new CarouselTileItem
        {
            Title = CleanOrNull(ReadFirstTextUnder(root, "title-text")),
            TileButton = FindDescendantNamed(root, "layout-tile-interaction-button"),
        };
        if (string.IsNullOrEmpty(item.Title)) return null;

        var dataFrame = FindDescendantNamed(root, "Data-Frame");
        if (dataFrame != null)
        {
            item.HomeTeam = CleanOrNull(ReadFirstTextUnder(root, "HomeTeamBadge"));
            item.AwayTeam = CleanOrNull(ReadFirstTextUnder(root, "AwayTeamBadge"));
            int rowCount = dataFrame.hierarchy.childCount;
            for (int i = 0; i < rowCount; i++)
            {
                var row = dataFrame.hierarchy[i];
                if (!ElementBridge.IsVisible(row)) continue;
                string name = CleanOrNull(ReadFirstText(row));
                if (name == null) continue;
                var bar = FindChildNamed(row, "bar");
                item.Rows.Add(new CarouselStatRow
                {
                    Name = name,
                    Home = bar != null ? FirstTextElement(FindChildNamed(bar, "Left-frame")) : null,
                    Away = bar != null ? FirstTextElement(FindChildNamed(bar, "Right-frame")) : null,
                });
            }
        }
        else
        {
            var content = FindDescendantNamed(root, "states-templates-tile-content") ?? root;
            try { item.Grid = MatchReportTiles.TryReadFormation(content); }
            catch (Exception ex)
            {
                Log.Warn($"[AccessibilityServer] Formation tile read failed, plain lines kept: {ex.Message}");
            }
            if (item.Grid == null)
            {
                // A chart page (Match Momentum's FMMatchMomentumChart, the xG story's
                // FMXGMatchStoryChart) draws its data as bars. The figures it plots are read from
                // its bound observation into a table where that read holds; otherwise the renderer
                // says "visual only". Either way its axis/legend labels are skipped (orphan words
                // without the chart); real text outside the legends (the momentum observation
                // chip, the xG totals footer) is still read.
                item.HasChart = FindDescendantTypeEndsWith(content, "Chart") != null
                    || FindDescendantTypeEndsWith(content, "PassMapPanel") != null;
                List<string> legendNames = null;
                if (item.HasChart)
                {
                    var tables = LastMatchCharts.TryReadReportTile(content, out legendNames);
                    if (tables != null)
                        foreach (var t in tables)
                            item.ChartTables.Add(new ChartTable
                            {
                                Note = t.Note,
                                Grid = new MatchReportTiles.Grid { Headers = t.Table.Headers, Rows = t.Table.Rows },
                            });
                }
                else
                {
                    // The report tiles that draw a real grid (events, latest scores, live table, a
                    // team's player stats) read as a table; anything else falls back to plain lines.
                    try { item.Grid = MatchReportTiles.TryRead(content); }
                    catch (Exception ex)
                    {
                        Log.Warn($"[AccessibilityServer] Match-report tile read failed, plain lines kept: {ex.Message}");
                    }
                }
                if (item.Grid == null) CollectChartTileLines(content, item.Lines, item.HasChart, legendNames);
            }
            // A report grid built from a scrolling list (the round's other scores, the live table)
            // holds only the rows the game has realised: ask for the rest, and say what is shown
            // until it arrives rather than passing a part of the list off as all of it.
            if (item.Grid != null)
                item.WindowNote = ListWindowSurface.ExpandForTile(content);
        }
        return item;
    }

    // The tile's loose text. On a chart tile the legends are skipped, and so is the xG tile's
    // unnamed two-team legend once the chart's table has named its columns after those teams (the
    // first adjacent pair of lines reading exactly those names). A "Footer" (the xG tile's
    // "Goals: 6 - 3 | xG Total: 4.19 - 1.75") reads as the one line it draws instead of seven
    // fragments.
    private static void CollectChartTileLines(VisualElement content, List<string> lines, bool hasChart, List<string> legendNames)
    {
        CollectVisibleTileLines(content, lines, skipLegends: hasChart, joinFooter: hasChart);
        if (legendNames == null) return;
        for (int i = 0; i + 1 < lines.Count; i++)
            if (lines[i] == legendNames[0] && lines[i + 1] == legendNames[1])
            {
                lines.RemoveRange(i, 2);
                return;
            }
    }

    // First descendant whose IL2CPP type name ends with the suffix (the chart widgets share no
    // base name beyond the "...Chart" convention). Depth-first, visible or not — presence is the
    // signal, not visibility.
    private static VisualElement FindDescendantTypeEndsWith(VisualElement root, string suffix)
    {
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = root.hierarchy[i];
            if (c.GetIl2CppType().Name.EndsWith(suffix, StringComparison.Ordinal)) return c;
            var found = FindDescendantTypeEndsWith(c, suffix);
            if (found != null) return found;
        }
        return null;
    }

    private static string ReadFirstTextUnder(VisualElement root, string name)
    {
        var el = FindDescendantNamed(root, name);
        return el != null ? ReadFirstText(el) : null;
    }

    // The first non-empty SIText ELEMENT under root (the element itself, so the renderer can
    // live-track it). Null-tolerant on root for the missing-frame case.
    private static VisualElement FirstTextElement(VisualElement root)
    {
        if (root == null) return null;
        if (root.GetIl2CppType().Name == "SIText" && !string.IsNullOrEmpty(ReadSITextValue(root)))
            return root;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var r = FirstTextElement(root.hierarchy[i]);
            if (r != null) return r;
        }
        return null;
    }

    // Every visible non-empty SIText under root, cleaned, in document order — the generic
    // fallback read for a carousel page with no recognised stat structure. Capped defensively;
    // a tile is a compact widget, so 40 lines only ever truncates a pathological case.
    private static void CollectVisibleTileLines(VisualElement root, List<string> lines, bool skipLegends = false,
        bool joinFooter = false)
    {
        if (lines.Count >= 40) return;
        if (!ElementBridge.IsVisible(root)) return;
        // A chart tile's legend containers (Half_legend, GraphLegend) hold the axis and colour-key
        // labels — orphan words once the chart itself is replaced by a table or a "visual only" note.
        if (skipLegends && (root.name ?? "").EndsWith("legend", StringComparison.OrdinalIgnoreCase))
            return;
        // A pass map's player icons print bare shirt numbers: without the drawing they say nothing.
        if (skipLegends && root.GetIl2CppType().Name == "FMPassMapPanel")
            return;
        if (joinFooter && root.name == "Footer")
        {
            var parts = new List<string>();
            CollectFooterTexts(root, parts);
            if (parts.Count > 0) lines.Add(string.Join(" ", parts));
            return;
        }
        if (root.GetIl2CppType().Name == "SIText")
        {
            var t = CleanOrNull(ReadSITextValue(root));
            if (t != null) lines.Add(t);
        }
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            CollectVisibleTileLines(root.hierarchy[i], lines, skipLegends, joinFooter);
    }

    // Every visible text a footer draws, in order — its SITexts and the plain Label delimiters
    // ("-") between the paired figures, which carry meaning once the parts share one line.
    private static void CollectFooterTexts(VisualElement root, List<string> parts)
    {
        if (!ElementBridge.IsVisible(root)) return;
        string type = root.GetIl2CppType().Name;
        if (type == "SIText")
        {
            var t = CleanOrNull(ReadSITextValue(root));
            if (t != null) parts.Add(t);
        }
        else if (type == "Label")
        {
            var t = CleanOrNull(root.Cast<Label>().text);
            if (t != null) parts.Add(t);
        }
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            CollectFooterTexts(root.hierarchy[i], parts);
    }

    internal abstract class RenderItem { }

    // A formation pitch consolidated into one widget. Formation is the game-free model both surfaces
    // share (occupied slots, ordered, with name/role/number); the renderer derives empty slots and
    // spatial neighbours from the fixed 24-slot layout. Editable is the interactable pitch's per-slot
    // action adapters (role/instructions/move), or null on a read-only card — when null the grid
    // renders the formation without actions.
    internal sealed class PitchGridItem : RenderItem
    {
        public VisualElement Pitch;
        public VisualElement PhaseCaption;
        public VisualElement PhaseScope;
        public PitchFormation Formation;
        public Dictionary<string, Adapters.TacticsPlayerAdapter> Editable;
    }

    // A position-picker pitch consolidated into one table: slot code -> that slot's filter toggle.
    // The renderer lays these out over the fixed 24-slot geometry, so an absent slot is simply an
    // empty cell and no layout is carried in the item itself.
    internal sealed class PositionGridItem : RenderItem
    {
        public Dictionary<string, IAccessibleElement> BySlot;
        // The phase tab this pitch belongs to ("In Possession"), read live from the tab bar the walk
        // just emitted, so the table says which formation it is showing. Null when there is no tab
        // bar above it — the table then carries its plain name rather than a guessed one.
        public string Phase;
    }

    // The selected tab of the most recently emitted tab bar, or null when the walk has not passed one
    // (or none is selected). Used to name a consolidated widget after the tab that governs it, using
    // the game's own tab text.
    private static string SelectedTabLabel(List<RenderItem> items)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is not TabBarItem tb) continue;
            foreach (var tab in tb.Tabs)
                if (tab.IsSelected) return DisplayText.Of(tab);
            return null;
        }
        return null;
    }

    // Text stops the layout split across lines, read as one line. Each part keeps its own live-tracked
    // span, so joining them costs no freshness — this is a presentation join, not a flattening.
    internal sealed class JoinedTextItem : RenderItem
    {
        public bool LeadingCaption;
        public bool IsConversationQuestion;
        public List<IAccessibleElement> Parts = new();
        public IAccessibleElement BodySource;
    }

    // The result-count frame of the search screens: a big number above its caption.
    private const string PlayersFoundFrame = "players-found-text-frame";

    // A mod-authored plain-text note rendered as a <p> — for synthetic "degrade in place" placeholders
    // (e.g. the Combined-view pitch pointer). No backing element; Text is chrome, escaped at render.
    internal sealed class NoteItem : RenderItem
    {
        public string Text;
        public string Id;
    }

    // The scatter plot on the Data Hub's analysis cards, and what the page says in its place.
    // Mod-authored chrome: the game gives the plot no accessible name of any kind.
    private const string ScatterChartContainer = "scatter_chart";
    private const string ScatterChartNote = "scatter chart, visual only";

    // The recent-form bar strip, and what the page says in its place: the results are drawn as
    // coloured fills with no text of any kind behind them.
    private const string RecentFormContainer = "data_display-cell-recent-form-fixtures-regular";
    private const string RecentFormNote = "recent form bars, visual only";

    // The Player Comparison card's per-player halves — the Overview/Attributes tabs name them
    // LHS/RHS, the Positions & Roles tab Left/Right — and the headings the walk puts above them.
    // Mod-authored chrome: the card names each half nothing at all, and the player it belongs to
    // is stated only by the picker dropdown at the top of that column.
    private const string ComparisonPanelName = "OnThePitchComparisonCard";
    private const string ComparisonLeftHalfFrame = "LHS Player frame";
    private const string ComparisonRightHalfFrame = "RHS Player frame";
    private const string ComparisonPitchContainer = "PitchWithPlayerPositions";
    private const string ComparisonLeftHalfPositions = "LeftPlayer";
    private const string ComparisonRightHalfPositions = "RightPlayer";
    private const string ComparisonPlayerOneHeading = "Player 1";
    private const string ComparisonPlayerTwoHeading = "Player 2";

    // The element a radar that prints its figures binds each one to (the Data Hub's), and the card
    // whose radar prints none (the Coach Report's), read from the chart's binding instead.
    private const string RadarAxisValueElement = "Value";
    private const string CoachReportPanel = "CoachReportCard";
    // The Coach Report radar table's columns: the axis, then its two series (the player, then the
    // average for the position across the competition the card's caption names, "Serie A average").
    // Mod-authored chrome; the competition is the game's, read from the caption, and without it the
    // column is plain "Average".
    private static readonly string[] CoachRadarHeaders = { "Statistic", "Player", "Average" };
    private const string CompetitionAverageSuffix = " average";
    private static readonly HashSet<string> _coachRadarMisses = new(StringComparer.Ordinal);

    // The radar's section wrapper, where its unshown summary prose sits beside the subtitle, and
    // how far above the chart to look for it.
    private const string RadarSectionElement = "Chart";
    private const int RadarSectionAscend = 4;
    private static readonly string[] RadarSummaryBreaks = { "\r\n\r\n", "\n\n" };
    // The column that holds the position picker and its statistical assessment, and the title the
    // page gives it. Mod-authored chrome: the card titles the column nothing at all.
    private const string RadarSectionColumn = "right-column";
    private const string RadarSectionHeading = "Statistics";
    // HeadingItem.Level is the literal HTML level, unlike a profile rule's (which maps 0-2 to h3),
    // so this is 3 to sit alongside the profile's own section headings on this card.
    private const int RadarSectionHeadingLevel = 3;

    // The radar card's legend, and the caveat the page states above it.
    internal const string RadarLegendContainer = "attributes key";
    private const string RadarLegendNote = "radar chart legend; only the first series below is in the table above, the rest are drawn on the chart and have no readable values";

    // An ARIA region boundary the walk inserts around a master-detail subtree (the Messages panel's
    // list and reading pane). RegionStartItem opens a role="region" landmark named Label; the matching
    // RegionEndItem closes it. HTML-only; Label is mod-authored chrome, escaped at render.
    internal sealed class RegionStartItem : RenderItem
    {
        public string Label;
        // The list region doubles as the Alt+Left focus fallback (when no email has been opened yet),
        // so the renderer makes it focusable and findable by FM.messages.
        public bool IsList;
    }

    internal sealed class RegionEndItem : RenderItem { }

    // The bookmark editor's current-bookmarks table (BookmarkEditorSurface): one row per slot,
    // columns Key / Action / Move / Delete. The action cells are baked text; the move and delete
    // cells hold the surface's synthetic adapters, registered as handles at render.
    internal sealed class BookmarksTableItem : RenderItem
    {
        public List<BookmarkTableRow> Rows = new();
    }

    internal sealed class BookmarkTableRow
    {
        public string Chord;
        public string Action;
        public IAccessibleElement Remove;
        public IAccessibleElement MoveUp;    // null on the first row
        public IAccessibleElement MoveDown;  // null on the last row
    }

    // The standalone Messages panel's inbox as one table (see TryBuildMessagesTable). Cells are
    // baked (cleaned) at walk time; Adapter is the live row button, held only for handle
    // registration and activation at render — never for re-reading baked text.
    internal sealed class MessagesTableItem : RenderItem
    {
        public VisualElement Host;
        public List<MessageRow> Rows = new();
        // A row's subject was not readable at walk time. The inbox rows bind asynchronously
        // (AsyncBindingRoot) and the composed headline/sender strings land last, so on a slow
        // machine the compose can race them; the renderer then marks the compose provisional so
        // the marker retry re-reads once the text has bound, instead of leaving the player on a
        // subject-less inbox until they refresh by hand.
        public bool Provisional;
        // Any row's text came from the compact template's body preview (no subject element on
        // small screens); the renderer then titles the column "Preview" instead of "Subject".
        public bool AnyPreview;
    }

    internal static void RefreshMessagesTable(MessagesTableItem item)
    {
        if (item.Host == null || item.Host.panel == null || !ElementBridge.IsVisibleInHierarchy(item.Host))
        {
            // An outgoing filter's cached list can stay attached under a hidden
            // ancestor while another table is harvested. Rewalk through the
            // existing bounded provisional recovery; never register its rows.
            item.Rows.Clear();
            item.Provisional = true;
            item.AnyPreview = false;
            return;
        }
        var rows = new List<MessageRow>();
        string date = null;
        int unreadable = 0;
        // Other streamed tables can reflow/recycle the inbox while composing
        // FM27's dashboard. Bind actions to the rows alive at render time.
        CollectMessageRowsRec(item.Host, ref date, rows, ref unreadable);
        if (IsNativeUnreadTab(item.Host)) rows.RemoveAll(row => !row.Adapter.IsUnread());
        item.Rows = rows;
        var list = Adapters.ButtonAdapter.FindDescendant(item.Host, "VirtualisedList")?.TryCast<SI.UI.VirtualisedList>();
        item.Provisional = unreadable > 0 || (rows.Count == 0 && !IsNativeUnreadTab(item.Host)
            && ListWindowSurface.ReadItemCount(list) != 0);
        item.AnyPreview = rows.Exists(r => r.UsedPreview);
    }

    private static bool IsNativeUnreadTab(VisualElement listHost)
    {
        for (var parent = listHost.parent; parent != null; parent = parent.parent)
        {
            if (parent.GetIl2CppType().Name != "TabGroup") continue;
            var buttons = FindDescendantNamed(parent, "TabButtons");
            if (buttons == null) return false;
            var context = new Contexts.TabContext(buttons, new ClassificationCache());
            return context.GetAllTabs().Any(tab => tab.IsSelected && HtmlRenderer.IsUnreadMessagesFilter(tab));
        }
        return false;
    }

    internal sealed class MessageRow
    {
        public Adapters.MessageButtonAdapter Adapter;
        public string Status;
        public string Subject;
        public string Sender;
        // The sender-name link, when the row has one. Rendered as the button carrying the sender's
        // own context menu — but only when it actually resolves a menu (a person sender, not a news
        // outlet); the renderer checks. Held live from walk to render like the row adapter itself.
        public VisualElement SenderLeaf;
        public string Sent;   // date + time in one column ("Tue Apr 29th, 16:16")
        // Subject read as the unreadable marker at walk time (async bind not landed). The renderer
        // tracks such a cell on the value channel so the real subject is pushed in place when the
        // bind lands — the bounded marker retry alone gave up after ~4s, leaving the marker
        // permanent on machines whose binds finish later.
        public bool SubjectUnreadable;
        // Subject cell holds the compact template's body preview, not a headline.
        public bool UsedPreview;
    }

    internal sealed class StopItem : RenderItem
    {
        public IAccessibleElement Element;
        // A structurally identified native section caption. Render through the
        // ordinary heading path, retaining its live source and Alt+I contract.
        public int HeadingLevel;
        // A native title reused as the section heading, retaining its own
        // description and embedded actions instead of repeating it in the body.
        public bool IsSectionTitle;
    }

    // A run of game-provided text rows rendered as one native HTML list. Elements stay live so
    // refreshes read the current recommendation rather than retaining copied report text.
    internal sealed class TextListItem : RenderItem
    {
        public List<IAccessibleElement> Items = new();
        // Native rows can contain an entity link followed by its own description.
        // Keep the ordinary renderers/handles inside each semantic list item.
        public List<List<RenderItem>> Rows;
        public VisualElement Source;
        // Further texts of an item's own row, read after it on the same list item (index-aligned
        // with Items; null when no item has any).
        public List<List<IAccessibleElement>> Trailing;

        public List<IAccessibleElement> TrailingOf(int index)
        {
            Trailing ??= new List<List<IAccessibleElement>>();
            while (Trailing.Count <= index) Trailing.Add(null);
            return Trailing[index] ??= new List<IAccessibleElement>();
        }

        public IReadOnlyList<IAccessibleElement> TrailingAt(int index)
            => Trailing != null && index < Trailing.Count ? Trailing[index] : null;
    }

    // A synthetic landmark heading inserted by the walk at a structural boundary (e.g. the title
    // bar's section-tabs group). No backing game element: Text is mod-authored chrome, Level is the
    // heading depth to emit (relative to the panel's own section heading).
    internal sealed class HeadingItem : RenderItem
    {
        public string Text;
        public int Level;
    }

    internal sealed class ScoreboardItem : RenderItem
    {
        public string Line;
    }

    // The contract negotiation terms table: one row per term (see ConsolidateContractTermRows).
    internal sealed class ContractTermsItem : RenderItem
    {
        public List<ContractTermRow> Rows = new();
    }

    // One term row: the term name (its row header), the live value control, the negotiability
    // padlock dropdown, the optional Delete button, and any unmodelled fragments the row template
    // adds (rendered in the value cell so nothing drops). A condition row (a promise's target
    // block) instead lands in the ContractConditionsItem table: Term is the condition sentence,
    // ParentTerm the promise it applies to, Value the statistic dropdown and the first extra
    // dropdown the target count.
    internal sealed class ContractTermRow
    {
        public string Term;
        public string ParentTerm;
        public bool IsCondition;
        public IAccessibleElement Value;
        public IAccessibleElement Lock;
        public IAccessibleElement Delete;
        public List<IAccessibleElement> Extras = new();
    }

    // The conditions attached to promises, as their own table after the terms table.
    internal sealed class ContractConditionsItem : RenderItem
    {
        public List<ContractTermRow> Rows = new();
    }

    // One responsibility group's duties as an editable table (see ConsolidateDelegationRows):
    // Responsibility | Description | Assigned to | Control.
    // The Delegation tab's whole section as one table: every group of the selected category with
    // every duty under it. Groups stay null until ResponsibilitiesLoader sweeps them in on the
    // request thread; each is plain strings + flags (no element references — virtualised groups
    // recycle), and every control re-realises by group/duty/slot through ResponsibilityRowHandle
    // at action time.
    internal sealed class ResponsibilitiesTableItem : RenderItem
    {
        public VisualElement List; // the StreamedObjectList of groups; handles key off it
        public Contexts.ResponsibilitiesContext Context;
        public List<Contexts.ResponsibilitiesContext.Group> Groups;
        public bool Truncated;
        public bool LoadFailed;
    }

    // The Overview tab's read-only duty list for the selected staff member, swept across frames
    // like the delegation table: the list virtualises hard (the manager's 39 duties realise a
    // dozen at a time), so a walk of the live tree would show a fraction as the whole.
    internal sealed class StaffDutiesTableItem : RenderItem
    {
        public VisualElement List;
        public Contexts.ResponsibilitiesContext Context;
        public List<Contexts.ResponsibilitiesContext.StaffDuty> Duties;
        public bool Truncated;
        public bool LoadFailed;
    }

    // One Advice and Reports group as a table: Setting | Description | Frequency-or-Assigned to.
    internal sealed class AdviceTableItem : RenderItem
    {
        public List<AdviceRow> Rows = new();
        // The frequency blocks set a schedule, the advice blocks pick a staff member; the value
        // column is named for whichever this group's dropdowns are.
        public bool ValueIsFrequency;
    }

    internal sealed class AdviceRow
    {
        public string Title;
        public string Description;
        public IAccessibleElement Value;
        public List<IAccessibleElement> Extras = new();
    }

    // A "label: value" line folded from an adjacent label/value text pair; the value element keeps
    // its own id on the values stream so the number updates in place.
    internal sealed class LabelValueItem : RenderItem
    {
        public string Label;
        public IAccessibleElement Value;
    }

    // The match header's stats-tile carousel: the visible page's title (heads the widget and,
    // via TileButton, opens the full card), its stat rows (value elements live-tracked at
    // render), plain text lines for a non-stats page, and the hidden pages' titles paired with
    // the game's own dot buttons. See TryEmitCarouselTile / HtmlRenderer.RenderCarouselTile.
    internal sealed class CarouselTileItem : RenderItem
    {
        public string Title;
        public VisualElement TileButton;
        public VisualElement CarouselRoot;
        public string HomeTeam;
        public string AwayTeam;
        public List<CarouselStatRow> Rows = new();
        // A report tile whose content is a real grid (MatchReportTiles); mutually exclusive with
        // Lines, which is the flat fallback for content no reader recognised.
        public MatchReportTiles.Grid Grid;
        // A drawn tile's figures as tables (LastMatchCharts.TryReadReportTile: a chart's plotted
        // values, a pass map's players and links), rendered in place of the "visual only" note;
        // Lines still carries the tile's loose text beside them.
        public List<ChartTable> ChartTables = new();
        // "Showing rows X to Y of Z" when Grid was read from a list the game has only partly
        // realised (ListWindowSurface.ExpandForTile); null when every row is present.
        public string WindowNote;
        public List<string> Lines = new();
        public List<CarouselPage> OtherPages = new();
        // Every page in carousel order (the visible one included) plus which is visible, so the
        // renderer can present the switcher as a real tablist with a selected tab. OtherPages is
        // kept alongside for the reading view. Empty for a standalone (non-carousel) tile.
        public List<CarouselPage> AllPages = new();
        public int VisiblePageIndex = -1;
        // The tile's content is a graphical chart (FMMatchMomentumChart / FMXGMatchStoryChart):
        // its data is not readable, so the renderers say so honestly instead of leaking the
        // chart's axis/legend labels as orphan lines. Lines still carries any real text outside
        // the legends (the momentum observation chip).
        public bool HasChart;
    }

    internal sealed class ChartTable
    {
        public string Note;
        public MatchReportTiles.Grid Grid;
    }

    // The score bug's stats-widget toggle, carrying whether the widget it reveals is currently
    // open so the button renders with an explicit expanded/collapsed state. Built by
    // ReorderMatchHeaderControls, which knows both the toggle stop and whether the carousel
    // rendered this build.
    internal sealed class StatsToggleItem : RenderItem
    {
        public IAccessibleElement El;
        public bool Expanded;
    }

    // The click target of a wrapper the walk descended through, presented as the tile's title
    // heading. See PromoteTileTitleToActivator / HtmlRenderer.RenderTileActivator.
    internal sealed class TileActivatorItem : RenderItem
    {
        public string Title;
        public VisualElement TileButton;
    }

    internal sealed class CarouselStatRow
    {
        public string Name;
        public VisualElement Home;
        public VisualElement Away;
    }

    internal sealed class CarouselPage
    {
        public string Title;
        public int Index;
    }

    // One cell of a Today's Top Fixtures row: the game's clickable element (competition or
    // club button), or the plain text the slot displays when nothing there is actionable.
    internal sealed class FixtureCell
    {
        public IAccessibleElement Button;
        public string Text = "";
    }

    internal sealed class TodaysFixtureRowData
    {
        public FixtureCell Competition;
        public FixtureCell Home;
        public FixtureCell Away;
        public string Time = "";
    }

    // The news site's Today's Top Fixtures rail consolidated into one table. List is the
    // streamed-list element, kept for stable table-id keying.
    internal sealed class TodaysFixturesItem : RenderItem
    {
        public VisualElement List;
        public List<TodaysFixtureRowData> Rows;
    }

    // One attribute section's rows (Technical, Set Pieces, ...) consolidated into a two-column
    // table: the combined "stat value" cell and a button opening the row's progress report,
    // with a client-side sortable stat column. List is the streamed-list element, kept for
    // stable table-id keying.
    internal sealed class AttributeTableItem : RenderItem
    {
        public VisualElement List;
        public List<Adapters.AttributeRowAdapter> Rows;
    }

    // One labelled row of the player report banner. Value stays live (never a copied string), so
    // activation, context menus and in-place value updates behave exactly as they did when the
    // same control walked out as a standalone stop. Label is mod chrome naming what the game
    // draws only as an icon — null where the mod cannot name the value, which renders an empty
    // row header rather than a guess.
    internal sealed class PlayerBannerRow
    {
        public bool IsAction;
        public string Label;
        public IAccessibleElement LabelSource;
        public IAccessibleElement Value;
        public List<IAccessibleElement> Parts;
    }

    // The player report banner consolidated into one label/value table. Banner is the banner
    // container, kept for stable table-id keying.
    internal sealed class PlayerBannerItem : RenderItem
    {
        public VisualElement Banner;
        public List<PlayerBannerRow> Rows = new();
    }

    // A name/value pair snapshot for the best-attributes summary, read live at walk time.
    internal struct AttributeEntry
    {
        public string Name;
        public int Value;
    }

    // A mod-authored summary closing the attributes block: the ten best attributes best first, and
    // the ten worst worst first. Both are built from one collection of the same rows under one
    // all-or-nothing gate, so the pair can never disagree about what the report showed.
    internal sealed class AttributeSummaryItem : RenderItem
    {
        public string Heading;
        public List<AttributeEntry> Entries;
    }

    // A league-history line chart consolidated into a semantic table: one row per season (season,
    // division, finishing position), extracted by LeagueHistoryChartReader from the graphical chart.
    // Title is the chart's section label, folded out of its own text stop to head the table; null when
    // there was no preceding label to promote.
    internal sealed class LineChartItem : RenderItem
    {
        public string Title;
        public List<LeagueHistoryRow> Rows;
    }

    // A SyntheticTables consolidation: one configured container's rows as a semantic table. Cells
    // are display text prepared live at walk time; Headers are mod chrome from the spec.
    // The tactics Team Instructions section as one editable table; rows read live from the
    // phase-summary cells, selects populated from the static instruction catalog. Built and
    // rendered by Features/TeamInstructions.cs.
    internal sealed class TeamInstructionsItem : RenderItem
    {
        public string Title;                              // live section header text, null = none
        public List<TeamInstructions.Row> Rows;
    }

    internal sealed class NativeFormTableItem : RenderItem
    {
        public string[] Headers;
        public List<List<List<RenderItem>>> Rows;
        public int RowHeader;
        public bool Provisional;
    }
    internal sealed class SyntheticTableItem : RenderItem
    {
        public bool Provisional;
        public int RowHeader;
        public string[] Headers;
        public List<List<string>> Rows;
        // The table names every series of the chart legend that follows it, so the walk skips that
        // legend (see the "attributes key" branch).
        public bool NamesLegend;
        // Per row, a game control the row's naming cell activates (a player scatter row opening
        // that player), rendered as a button carrying the cell's text; null for a plain text table
        // or a row with no action.
        public List<IAccessibleElement> RowActions;
    }

    // A set-piece category's instruction chips as one editable table (slot, instruction select,
    // optional fallback select, move select). Built by SetPieceInstructions.TryEmitChipTable from
    // the first chip walked in the category; rendered by SetPieceInstructions.RenderChipTable.
    // DropTarget identifies the category so the category's later chips resolve to their table.
    internal sealed class SetPieceChipTableItem : RenderItem
    {
        public string Category;
        public VisualElement DropTarget;
        public bool HasFallback;
        public List<SetPieceInstructions.ChipRow> Rows;
    }

    // A date-plotted line chart (a finance card's balance/income/expenditure/budget history)
    // consolidated into a Date-Value table: the game's own x-axis labels over the plotted values,
    // extracted by FinanceChartReader from the graphical chart. Title is the enclosing section
    // heading, borrowed as the table's caption — the finance screens stack several of these tables
    // and, entered from a table-navigation key, an untitled one gave no clue which metric it plots.
    internal sealed class ValueChartItem : RenderItem
    {
        // Mod chrome for the unnamed case, shared by the HTML and reading-view renderers so the two
        // cannot drift. A chart whose lines were matched to their rows uses those names instead.
        internal static readonly string[] Headers = { "Date", "Value" };

        // At most this many lines become columns. A progress report can plot every attribute at
        // once; past ten the table stops being something a screen reader can hold. The lines left
        // out are named in OmittedColumns rather than silently dropped.
        internal const int MaxColumns = 10;

        public string Title;

        // The live chart, kept so the series can be re-read when something changes what is plotted
        // between the walk and the render (SingleAttributeChart reducing the selection).
        public VisualElement Chart;

        // One point list per plotted line, in the chart's series order, all the same length. The
        // first line's labels are the shared date column.
        public List<List<FinanceChartReader.Point>> Series;

        // One column name per line, set by the post-load join (NameChartSeriesFromTickedRows) when
        // the lines could be matched to the rows that selected them. Null means unmatched: a single
        // line still renders under the plain "Value" header, several cannot render at all.
        public string[] ValueHeaders;

        // Lines dropped by MaxColumns, named so the player knows what is missing rather than
        // reading a table that quietly stops short of what they ticked.
        public List<string> OmittedColumns;

        // Mod chrome for that note, shared by both renderers so they cannot drift.
        internal const string OmittedPrefix = "Not charted: ";

        // Attributes whose lines could not be separated from each other: same current rating AND
        // different histories, so no column can carry their name honestly. Left out of the table
        // and named below it.
        public List<string> AmbiguousColumns;

        // Mod chrome for that note. Says what to do about it — unticking all but one of the named
        // attributes makes the remaining line unambiguous.
        internal const string AmbiguousPrefix = "Same rating, different history, so not charted: ";

        // The dates every line shares, or null when the chart holds no points.
        public List<FinanceChartReader.Point> DateColumn
            => Series != null && Series.Count > 0 ? Series[0] : null;

        // Column headers as rendered: the matched attribute names, or the plain Date/Value pair.
        // Null when several lines went unmatched — nothing can be labelled, so nothing renders.
        public string[] RenderedHeaders()
        {
            if (ValueHeaders == null)
                return Series != null && Series.Count == 1 ? Headers : null;

            var headers = new string[ValueHeaders.Length + 1];
            headers[0] = Headers[0];
            System.Array.Copy(ValueHeaders, 0, headers, 1, ValueHeaders.Length);
            return headers;
        }
    }

    // One recruitment focus assigned to the selected scout: its name, and the live Edit and Delete
    // buttons belonging to that row. The buttons are the game's own stops, carried through so
    // activating one from a table cell does exactly what landing on it would.
    internal sealed class ScoutFocusRow
    {
        public string Name;
        public IAccessibleElement Edit;
        public IAccessibleElement Delete;
    }

    // The Scouting Assignments card's focus list as a table. An empty Rows list is meaningful and
    // renders as the "none assigned" note — see TryEmitScoutFocusList.
    internal sealed class ScoutFocusListItem : RenderItem
    {
        // Mod chrome, shared by the HTML and reading-view renderers so the two cannot drift.
        internal static readonly string[] Headers = { "Focus", "Edit", "Delete" };

        // Names the scoping rather than the scout: the section belongs to whichever row is
        // selected in the assignments table above, and an empty section previously read as "the
        // club has no recruitment focuses" when it means "this one has none".
        internal const string EmptyNote = "No recruitment focuses assigned to the selected scout";

        public List<ScoutFocusRow> Rows;
    }

    // One recruitment focus in the Recruitment Focus tool's master list: its own radio, plus the
    // three values the option carries. "Matched" is the game's own word for the count — it is the
    // number its Matched tab reports for that focus (verified live: the focus showing 14 has
    // "Matched (14)" and 0 players being scouted, so the count is matches, not scouting activity).
    internal sealed class FocusRow
    {
        public IAccessibleElement Radio;
        public string Name;
        public string Status;
        public string Matched;
    }

    // The Recruitment Focus tool's focus list as a table, deliberately the same shape as the staff
    // table on the Scouting Assignments card: a selection column, then a named column per value.
    internal sealed class FocusListItem : RenderItem
    {
        // Mod chrome except "Matched", which is the game's own tab word. Shared by the HTML and
        // reading-view renderers so the two cannot drift.
        internal static readonly string[] Headers = { "Selection", "Focus", "Status", "Matched" };

        // No focus survives the filter (or the club has none). Named for the filter, because the
        // list is scoped by the status dropdown above it and an empty list otherwise reads as
        // "this club has no recruitment focuses".
        internal const string EmptyNote = "No recruitment focuses match the current filter";

        public VisualElement Group;
        public List<FocusRow> Rows;
    }

    // One Projections-card metric block (Balance, Turnover, ...): the metric name and its
    // season-end label/value pairs. An intermediate — ConsolidateProjectionBlocks merges each
    // run of these into a single ProjectionsTableItem before rendering.
    internal sealed class ProjectionBlockItem : RenderItem
    {
        public string Metric;
        public List<(string Label, string Value)> Seasons;
    }

    // The Projections card's metric blocks as one table: a row per metric, a column per season.
    internal sealed class ProjectionsTableItem : RenderItem
    {
        public List<string> SeasonLabels;
        public List<ProjectionBlockItem> Blocks;
    }

    // A pitch of FMPitchIndicator slots consolidated into one table (the Squad Planner position
    // strip, the Best XI pitch): a row per position, first cell the position code, the rest the
    // slot's values already formatted as display text. Headers align with the row cells. A row
    // with a Select adapter renders its position cell as a select control (the planner strip,
    // where picking a position switches the plan section); Selected mirrors the pitch's own
    // single selection. A null Select renders the row read-only (the Best XI pitch).
    internal sealed class PitchSummaryRow
    {
        public List<string> Cells;
        public Adapters.PositionSelectorAdapter Select;
        public bool Selected;
    }

    internal sealed class PitchSummaryItem : RenderItem
    {
        public List<string> Headers;
        public List<PitchSummaryRow> Rows;
    }

    // The match-stats comparison card consolidated into a semantic table: one row per stat (name, home
    // value, away value), with the team names as column headers. Built by MatchStatsTableReader and
    // rendered by MatchStatsHtml. Replaces the team-attribution-less stream of raw value stops.
    internal sealed class StatComparisonItem : RenderItem
    {
        public string HomeTeam;
        public string AwayTeam;
        public List<Navigation.MatchStatRow> Rows;
    }

    // Transfer Activity stacks two tab rows with nothing to tell them apart: an outer view switch
    // (Transfer Activity / General Manager Offers) and an inner direction filter (All / In / Out).
    // Both landed as "tab group", so landmark navigation offered two identical destinations.
    // Matched on the ancestor the game names, never on tab text. Panel-gated first: both ancestor
    // names are generic ("main-container" especially) and AncestorNameContains is a substring match
    // that walks to the root, so an unscoped rule would label other panels' tab rows "View".
    // Null for every other bar, which keeps the generic ContainerKind.TabGroup label.
    private static string TabBarLabel(VisualElement bar)
    {
        if (!ElementBridge.IsInPanel(bar, "RecruitmentTransferActivityTool")) return null;
        if (AncestorNameContains(bar, "active-transfers-tabs")) return "Direction";
        if (AncestorNameContains(bar, "main-container")) return "View";
        return null;
    }

    internal sealed class TabBarItem : RenderItem
    {
        public List<Adapters.TabItemAdapter> Tabs;
        // Region landmark name, where this panel's bar has a meaning worth naming. Null everywhere
        // else, which leaves the generic ContainerKind.TabGroup label untouched — a panel with one
        // tab row needs no name, and two unnamed rows on one panel are the problem this fixes.
        public string Label;
    }

    internal sealed class RadioGroupItem : RenderItem
    {
        public VisualElement Group;
        public List<IAccessibleElement> Radios;
        // Per-radio action controls, aligned by index with Radios: RadioExtras[i] holds the
        // interactive controls that live beneath Radios[i] (the coach list's per-row Edit/Delete
        // buttons), rendered right after that radio so they read in context. Empty for an ordinary
        // radio whose row carries no extra controls. Null only on the segmented-control path, which
        // sets Radios directly and never populates extras.
        public List<List<IAccessibleElement>> RadioExtras;
        // Per-radio nested sub-option radios, aligned by index with Radios: SubRadios[i] holds
        // the radios of a sub-option group living inside Radios[i]'s subtree (the Player Interest
        // dialog's Interested/Persuadable/Doubtful under "Transfer only"), rendered as a nested
        // radiogroup named after that radio. Empty for an ordinary radio; null on the
        // segmented-control path, like RadioExtras.
        public List<List<IAccessibleElement>> SubRadios;
        // The group's field label, used as the fieldset's accessible name. Set for segmented
        // controls (the "Mode"/"Text Scaling" rows); null for an SIRadioGroup, which carries no
        // separate row label.
        public string Label;
    }

    // The native column footer is outside the virtualised row list. Read it
    // after harvesting, using the same cell walk so labels/links stay native.
    internal static void ReadTableFooter(TableItem item)
    {
        item.Footer=null;item.FooterPending=false;
        var root=FindDescendantNamed(item.Table,"column-footers");
        if(root==null||!ElementBridge.IsVisibleInHierarchy(root))return;
        var cells=new List<List<RenderItem>>();
        for(int i=0;i<root.hierarchy.childCount;i++)
        {
            var cell=root.hierarchy[i];if(!ElementBridge.IsVisible(cell))continue;
            item.FooterPending|=Contexts.TableContext.ReadBindingReadiness(cell).OptionalContentPending;
            var parts=new List<RenderItem>();WalkRec(cell,null,parts);cells.Add(parts);
        }
        if(cells.Exists(c=>c.Count>0))item.Footer=cells;
    }

    internal sealed class TableItem : RenderItem
    {
        public VisualElement Table;
        public Contexts.TableContext Context;
        public List<Contexts.TableHeader> Headers;
        public List<Contexts.TableRow> Rows;
        public List<List<RenderItem>> Footer;
        public bool FooterPending;
        // This page could not be read in full (a load budget ran out mid-page). NOT "there are more
        // rows" — that is what the pager says.
        public bool Truncated;
        // The game declares this table's rows multi-selectable (its list is in Many selection
        // mode); the renderer then adds Select all / Clear selection buttons above the table.
        public bool MultiSelect;
        // The table's identity path, captured on the walk because the streamed load runs on the
        // request thread and cannot walk the hierarchy itself. Keys this table's stored page.
        public string Path;
        // Which page of the table Rows holds, and how many pages there are. PageCount 1 means the
        // whole table fits on one page (or its row count could not be read) and no pager renders.
        public int Page;
        public int PageCount;
        // Column display order (indices into Headers, and into each row's Cells), or null for the
        // game's own order. Set by ApplyTableColumnMoves. Presentation only: renderers iterate this
        // order but pass the real column index into every handle, so sorting and cell actions still
        // target the game's actual columns.
        public int[] DisplayOrder;
        // A column the layout uses purely as a gutter, to be left out of the rendered table — but
        // only if it really is empty in every row. -1 (the default) means render every column.
        // Decided by the renderer rather than the walk because the streamed rows do not exist yet
        // when the walk runs, so the walk can only say WHICH column it believes is the gutter; a
        // gutter that turns out to hold anything is rendered rather than hiding it.
        public int GutterColumn = -1;
        // This table holds further rows of the table before it: the game split one logical table
        // into several, each drawn as its own grid. Folded together after the streamed load, which
        // is the first moment the rows exist at all. False for every ordinary table.
        public bool FoldsIntoPreceding;
    }

    // A per-column summary block beneath a table (the Coach Assignments footer): one value
    // column per table category, one value per summary row (Coach Workload, Lead Coach Rating).
    // Rendered transposed — one row per category — so the screen reader pairs every value with
    // its category and summary-row name. All strings are read live at walk time.
    internal sealed class ColumnSummaryItem : RenderItem
    {
        public List<string> ValueLabels;  // the game's summary-row names, document order
        public List<string> Categories;   // one per value column, from the table's headers
        public List<List<string>> Values; // [column][value-label index]
    }

    // One stage's match results, consolidated from a "FixturesList" into a single table. StageName is
    // the section heading ("Group C", "Semi-Finals"), null when the list is not under a named stage.
    internal sealed class FixturesItem : RenderItem
    {
        public string StageName;
        // The rows of the CURRENT page only — a cup round can hold hundreds of ties (the French Cup
        // fifth round is 290), and reading every one of them per compose is what pinned the main
        // thread for 11s and left the surface stuck on "The accessibility mod is busy."
        public List<MatchRow> Matches;
        // Paging state, the same contract as TableItem: Path keys the stored page, PageCount 1
        // means the pager renders nothing.
        public string Path;
        public int Page;
        public int PageCount;
    }

    // The Player Search condition list consolidated into one table (Enabled | If/And | Condition |
    // Comparison | Value | actions). Rows stay null until ConditionTableLoader streams them in on
    // the request thread; each row is plain strings + flags (no element references — virtualised
    // rows recycle), and interactive cells re-realise their control by data index and slot key
    // through ConditionRowHandle at action time.
    internal sealed class ConditionTableItem : RenderItem
    {
        public VisualElement List; // the StreamedObjectList of condition rows; handles key off it
        public Contexts.ConditionTableContext Context;
        public List<Contexts.ConditionTableContext.Row> Rows;
        public bool Truncated;
        public bool LoadFailed;
    }

    // The Add/Remove Leagues grid consolidated into one table. Rows stay null until LeagueTableLoader
    // streams every nation in on the request thread; each row is plain strings + flags (no element
    // references — virtualised rows recycle), and interactive cells re-realise their control by data
    // index through LeagueRowHandle at action time.
    internal sealed class LeagueTableItem : RenderItem
    {
        public VisualElement List; // the StreamedObjectList of league rows; handles key off it
        public Contexts.LeagueTableContext Context;
        public List<Contexts.LeagueTableContext.Row> Rows;
        public bool Truncated;
        public bool LoadFailed;
    }

    // The Calendar tool's Fixture Schedule consolidated into a single table: the game's own sortable
    // column headers (read at walk time — they are not virtualised) over the season's fixture rows.
    // Rows stay null until FixtureScheduleLoader streams them in on the request thread; each row is
    // plain strings plus flags (no element references — virtualised rows recycle), and interactive
    // cells re-realise their control by data index through FixtureRowHandle at action time.
    internal sealed class FixtureScheduleItem : RenderItem
    {
        public VisualElement List; // the StreamedObjectList("FixturesList"); handles key off it
        public List<Contexts.FixtureScheduleContext.HeaderCol> Headers;
        public Contexts.FixtureScheduleContext Context;
        public List<Contexts.FixtureScheduleContext.Row> Rows;
        public bool Truncated;
        public bool LoadFailed;
    }

    // One match row. Status/Score/positions are live text (raw, cleaned at render); the team controls
    // are adapters so the names stay clickable; scorers are (player, minute) pairs per side.
    internal sealed class MatchRow
    {
        public string Status;
        public IAccessibleElement HomeTeam;
        public string HomePosition;
        public string Score;
        public IAccessibleElement AwayTeam;
        public string AwayPosition;
        // A stage tie's undecided side lists every candidate team in one cell: the first is the
        // cell's main entry above, the rest render "/"-separated after it. Null on ordinary rows.
        public List<IAccessibleElement> HomeTeamExtra;
        public List<IAccessibleElement> AwayTeamExtra;
        public List<(string Name, string Minute)> HomeScorers;
        public List<(string Name, string Minute)> AwayScorers;
        // Per-match interactive controls the dedicated columns don't cover (attend checkbox, options
        // dropdown, view-match button, ...), so consolidating the tile never drops an action.
        public List<IAccessibleElement> Options;
    }

    // The schedule calendar consolidated into a week-by-weekday table. Weeks render top to bottom; each
    // populated CalendarDayCell is a day (date, state, its events as walked render items), a null slot
    // is calendar padding. See HtmlRenderer.RenderCalendar / CalendarScheduleHtml.
    internal sealed class CalendarItem : RenderItem
    {
        public List<CalendarWeekRow> Weeks;
        public string[] WeekdayLabels;
    }

    internal sealed class CalendarWeekRow
    {
        // Monday..Sunday; a null slot is padding (no day in that weekday column this week).
        public readonly CalendarDayCell[] Days = new CalendarDayCell[7];

        // The week's training focus card (its schedule) as a walked stop, or null on the tabs that
        // draw none. Renders as the row's header cell.
        public RenderItem Focus;
        public List<RenderItem> FocusItems;
    }

    // The create-schedule editor's week as a table: the game's own column headers (S1/S2/S3 session
    // slots, then the GK/Def/Att unit intensities) and one row per weekday. See
    // HtmlRenderer.RenderScheduleWeek.
    internal sealed class ScheduleWeekItem : RenderItem
    {
        public readonly List<string> Headers = new();
        public readonly List<ScheduleWeekRow> Rows = new();
    }

    // One day of the editor's week: its sessions as walked stops (still clickable, so their context
    // menus keep editing the session) and its intensity bars as percentages.
    internal sealed class ScheduleWeekRow
    {
        public string DayName;
        public readonly List<RenderItem> Sessions = new();
        public readonly List<string> Intensities = new();
    }

    // One populated calendar day: the day-of-month text, its state ("current date"/"already
    // simulated"/""), and its events as walked render items (rendered into the cell, kept clickable).
    internal sealed class CalendarDayCell
    {
        public string DateText;
        public string State;
        public List<RenderItem> Events;
        public VisualElement EventsList;
        public bool Provisional;
    }
}
