using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Turns the active screen into the semantic HTML the companion app's WebView2 renders
/// and a screen reader browses. BuildHtml emits the persistent page shell (status live
/// region, client script, EventSource); BuildBody emits the swappable #fmroot content
/// patched in on every refresh. Interactive elements are registered with ServerHandles
/// during render so action requests resolve back to the live game element.
/// </summary>
internal static class HtmlRenderer
{
    // Client assets, authored/vendored under Server/ and embedded in the assembly at build
    // time. Loaded once: they are static assets, not game state, so caching them does not
    // violate the no-cache rule. A missing resource means every interaction is dead, so it
    // is a hard crash rather than a silently blank script. Idiomorph is the vendored morph
    // library app.js's refresh patches the page through; it must load before app.js runs.
    private static readonly string IdiomorphJs = LoadResource("FM27Access.Plugin.Server.idiomorph.min.js");
    private static readonly string AppJs = LoadResource("FM27Access.Plugin.Server.app.js");

    // Custom feature renderers, consulted before the generic role switch in RenderNode.
    private static readonly List<IElementRenderer> FeatureRenderers = BuildRenderers();

    private static List<IElementRenderer> BuildRenderers()
    {
        var renderers = new List<IElementRenderer>();
        FeatureRegistry.RegisterRenderers(renderers);
        return renderers;
    }

    private static string LoadResource(string resourceName)
    {
        using var stream = typeof(HtmlRenderer).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            // A build-time invariant for the HTML shell. It breaks the companion page only, not
            // speech, so it does not halt the mod: the throw surfaces to the server request handler
            // (and a failed Mount stays recoverable via /reload) while the speech cursor keeps working.
            Log.Error($"[AccessibilityServer] Embedded resource '{resourceName}' not found in assembly.");
            throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // The full page is loaded ONCE per session: a persistent shell (status live region,
    // keyboard layer, EventSource) around a swappable content container (#fmroot). All
    // subsequent updates fetch /body and patch the container in place, restoring focus
    // to the previously focused element by stable id — the DOM port of the mod's
    // cursor-restoration feature. No location.reload() anywhere.
    // Wrap a freshly built body in the persistent page shell. Pure string assembly — no game access
    // — so the request thread calls it directly after the (frame-spanning) body build, without a
    // main-thread Invoke. Title and token are captured during the body build.
    internal static string WrapShell(string title, string token, string body, string language)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"").Append(DocumentLanguage.FromNative(language))
            .Append("\" data-fm-development=\"").Append(FM27Access.Speech.BuildProfile.DeveloperTools ? "true" : "false")
            .Append("\"><head><meta charset=\"utf-8\"><title>");
        sb.Append(ServerText.EscapeText(title)).Append("</title>");
        // The page is otherwise unstyled (screen-reader first). This only makes the search
        // combobox's listbox legible to sighted QA: a bordered list, no bullets, with the
        // aria-activedescendant option visibly highlighted (the SR path is the attribute itself).
        sb.Append("<style>ul[role=listbox]{list-style:none;margin:0;padding:0;border:1px solid #888;max-height:20em;overflow:auto}"
                + "ul[role=listbox] li{padding:2px 6px}li.fm-active{background:#0a3d91;color:#fff}"
                // The Navigator's browse tree: indented groups, focused item highlighted. Sighted-QA
                // legibility only — the SR path is the ARIA tree itself.
                + "ul[role=tree],ul[role=tree] ul{list-style:none;margin:0;padding-left:1.2em}"
                + "ul[role=tree] li{padding:1px 4px}"
                + "li[role=treeitem]:focus{outline:2px solid #0a3d91;background:#dce7f7}"
                + "button,select{min-height:24px;min-width:24px}</style>");
        sb.Append("</head><body>");
        // Companion announcements (on-demand reads, errors) are spoken through the in-game Prism
        // TTS via the /speak endpoint, not an aria-live region, so there is no status line here.
        // Play-by-play live region (announces as it changes). The score/clock line is the
        // separate #fmscore stop, rendered in the body where the scorebar sits so the user
        // finds it in context; the SSE 'score' event keeps that element ticking by id.
        sb.Append("<p id=\"fmlive\" aria-live=\"polite\"></p>");
        sb.Append("<div id=\"fmroot\">").Append(body).Append("</div>");

        // Idiomorph first so the global exists before app.js's refresh patches the page with
        // it. Then FMTOKEN (the only dynamic value), then the static client script verbatim.
        // JsEscape guards the token against a "</script>" breakout; the scripts are authored or
        // vendored, not interpolated, so they need no escaping.
        sb.Append("<script>").Append(IdiomorphJs).Append("</script>");
        sb.Append("<script>var FMTOKEN='").Append(ServerText.JsEscape(token)).Append("';</script>");
        sb.Append("<script>").Append(AppJs).Append("</script>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    // Reset per-build state ONCE per render pass: a rebuild invalidates every handle the session could
    // act through, and an open game popup must not outlive the page that knows about it. Handles and
    // live-value ids are identity-keyed and collision-free across panels, so the whole set re-registers
    // into the shared pools after this single clear. The caller controls WHEN within the build: the
    // /body path (ComposeBody) clears at the start of the RENDER Invoke, not the walk, so the previous
    // build's handles survive the frame-spanning streamed-table load between walk and render — clearing
    // earlier left an empty map that failed clicks ("Unknown element") on churning screens.
    //
    // preservePageHandles: the context-menu view is a MODAL OVERLAY — the page beneath it survives
    // unchanged, and the player's very next action after closing the menu targets that page (open the
    // next player's menu). Clearing on the menu render evicted every page handle, so that follow-up
    // gesture raced the page re-render and failed as "Unknown element" (observed live on the squad).
    // The menu render therefore keeps the HANDLE map (menu stops overwrite in; the next page render
    // still starts with the full clear) — but the ID POOL must reset every render regardless: the
    // pool suffixes repeated keys (-2, -3, ...), so carrying it across menu re-renders gave the SAME
    // menu different ids each refresh, and the id-keyed morph then replaced every entry node under
    // the reader mid-menu (the "options don't load" churn, observed live). Value tracking clears
    // with the pool; the menu view tracks no values and the modal hides the page's anyway.
    internal static void BeginBody(bool preservePageHandles = false, bool hasEditablePitch = false)
    {
        DropdownSessionManager.CloseCurrentOnMainThread();
        LiveValues.Clear();
        TabRowStatsRenderer.BeginBody();
        if (!preservePageHandles) ServerHandles.Clear();
        _suppressWalkedScoreboard = false;
        _pitchCount = 0;
        _editablePitchCount = 0;
        _hasEditablePitch = hasEditablePitch;
    }

    // Set for the current build when RenderSectionsToBody emitted the page-top score heading
    // (in-match): the walked per-panel scoreboard stop would render a second #fmscore, so it
    // is skipped. Reset in BeginBody; when the heading is absent the walked stop renders as
    // before, so the score line never disappears just because the heading compose failed.
    private static bool _suppressWalkedScoreboard;
    private static int _pitchCount;
    private static int _editablePitchCount;
    private static bool _hasEditablePitch;

    internal static void SuppressWalkedScoreboard() => _suppressWalkedScoreboard = true;

    // Walk one panel into render items (main thread). Pools are NOT cleared here — BeginBody does
    // that once before the loop. Must run under the panel's own profile (the walk consults hide
    // rules). Streamed tables are captured with their TableContext but NOT read here —
    // StreamedTableLoader fills them across frames on the request thread, then RenderSection emits.
    internal static List<ScreenWalker.RenderItem> WalkPanel(object panelObj)
    {
        return ScreenWalker.WalkScreen(panelObj);
    }

    // Render one interactive panel as its heading followed by its walked content, flat in #fmroot —
    // no wrapping element. The title bar (the global navigation chrome) is the page's single <h1>;
    // every content panel is an <h2> (headingLevel selects the tag). We deliberately emit NO <section>
    // wrapper: a <section> named by its heading is a region landmark, so the screen reader announced
    // each panel's name as a landmark AND as its heading — the same text twice. The headings alone
    // delineate the panels, so dropping the landmark removes that double-speak. A covering overlay
    // (card/modal) shown alone has its title bar suppressed; showBack — set by the caller when that
    // overlay has no close button of its own — adds a synthetic Back right after the heading (the
    // player's discoverable exit, the same /history?dir=back as Alt+[). index keeps the heading id
    // unique across the page (a stable idiomorph anchor across refreshes). Runs on the main thread
    // under the panel's own profile: registers handles and reads non-table stops live as it emits.
    internal static void RenderSection(StringBuilder sb, string title, List<ScreenWalker.RenderItem> items, int index, int headingLevel, bool showBack, bool isLoading = false)
    {
        // During matches the page-top score already owns h1. The generic
        // navigation panel must not introduce a second primary heading (seen
        // on the native full-time report).
        if(_suppressWalkedScoreboard&&headingLevel==1)headingLevel=2;
        ScreenWalker.StopItem nativeTitle = null;
        if (headingLevel > 0)
            foreach (var item in items)
                if (item is ScreenWalker.StopItem stop && stop.IsSectionTitle)
                {
                    nativeTitle = stop;
                    break;
                }
        // A full-screen native header may be an ordinary readonly text stop
        // rather than a dialog title. Reuse that first, identical source in
        // the existing section heading instead of reading it twice. Later
        // same-name groups still identify their own content and remain intact.
        if (nativeTitle == null && headingLevel > 0 && items.Count > 0
            && items[0] is ScreenWalker.StopItem first
            && first.Element is Adapters.TextAdapter
            && string.Equals(TextFilters.CleanForDisplay(DisplayText.Of(first.Element) ?? "").Trim(),
                TextFilters.CleanForDisplay(title ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            nativeTitle = first;
        // headingLevel 0 renders the section's content with no heading at all — the in-match
        // score bug strip, whose handful of controls sit directly at the page top (the pending
        // tactical-changes banner provides its own heading when it is up). FM.sim's arrival
        // focus targets the LAST fmsec heading, so a missing first one costs nothing.
        if (headingLevel > 0)
        {
            string headingId = "fmsec" + index + "-h";
            string tag = "h" + headingLevel;
            ServerHandles.RegisterHeadingDescription(headingId, title, nativeTitle?.Element);
            if (nativeTitle != null)
                RenderNode(sb, nativeTitle.Element, headingTag: tag, headingId: headingId);
            else
                sb.Append("<").Append(tag).Append(" id=\"").Append(headingId).Append("\" tabindex=\"-1\">")
                  .Append(ServerText.EscapeText(title)).Append("</").Append(tag).Append(">");
        }

        // A cascading context menu renders as a WAI-ARIA menu (roving focus, arrow navigation) rather
        // than a flat button list, so the player drills into a submenu with Right and steps out with
        // Left — the hierarchy the game's own menus have but a flat list hides. Detected by its
        // entries being MenuDataEntryAdapter stops (the only thing WalkEntries produces).
        if (IsContextMenuSection(items))
        {
            RenderContextMenuItems(sb, title, items);
            return;
        }

        // A covering overlay suppresses the title bar (with its real Back button), so without this the
        // only exit is the Alt+[ history-back chord. fmback() posts the same /history?dir=back.
        if (showBack)
            sb.Append("<p><button type=\"button\" data-dialog-close onclick=\"fmback()\">Back</button></p>");

        // A native loading layer can legitimately precede its bound progress
        // message. Its heading already identifies the wait; an empty frame is
        // not a missing-accessibility error. Ordinary empty panels stay explicit.
        if (items.Count == 0 && !isLoading)
            sb.Append("<p>No navigable content in this panel.</p>");

        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], nativeTitle)) continue;
            // A native full-screen card can repeat the panel title as its
            // very first heading (Mensagens / MENSAGENS). The stable section
            // heading already identifies that same content. Keep distinct card
            // headings and all later groups, without caption/panel allowlists.
            if (i == 0 && headingLevel > 0 && items[i] is ScreenWalker.HeadingItem heading
                && string.Equals(TextFilters.CleanForDisplay(heading.Text ?? "").Trim(),
                    TextFilters.CleanForDisplay(title ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            RenderItem(sb, items[i]);
        }
    }

    // True when a section's items are a cascading context menu — every item a MenuDataEntryAdapter
    // stop. WalkEntries is the only producer, so one probe of the first item suffices.
    private static bool IsContextMenuSection(List<ScreenWalker.RenderItem> items) =>
        items.Count > 0 && items[0] is ScreenWalker.StopItem s && s.Element is Adapters.MenuDataEntryAdapter;

    // The context menu as a WAI-ARIA menu: a role="menu" list whose items carry roving focus (the
    // first item tabbable, the rest reachable by arrow). A folder entry gets aria-haspopup so the
    // screen reader announces it opens a submenu, and data-fm-folder so the client's Right-arrow
    // drills it; the "Back" entry carries data-fm-menu-back for Left-arrow; "Close menu" keeps
    // data-fm-menu-first (the open's landing spot) plus data-fm-menu-close (Escape's target). All
    // drilling and dismissal reuse the SAME act() handles a click uses — the arrows are a second way
    // to drive them, not a parallel path. See FM.contextmenu in app.js.
    private static void RenderContextMenuItems(StringBuilder sb, string title, List<ScreenWalker.RenderItem> items)
    {
        sb.Append("<ul role=\"menu\" data-fm-menu aria-label=\"").Append(ServerText.EscapeText(title)).Append("\">");
        bool firstTabbable = true;
        foreach (var item in items)
        {
            if (item is not ScreenWalker.StopItem stop || stop.Element is not Adapters.MenuDataEntryAdapter entry)
                continue;

            bool disabled = !entry.CanActivate;
            sb.Append("<li role=\"none\">");
            // A ticked entry is a menuitemcheckbox so the screen reader announces "checked". An
            // unticked one stays a plain menuitem: the game's Selected flag only ever says checked,
            // so aria-checked="false" would invent a state it never expressed (see IsChecked).
            sb.Append(entry.IsChecked
                ? "<button type=\"button\" role=\"menuitemcheckbox\" aria-checked=\"true\""
                : "<button type=\"button\" role=\"menuitem\"");

            // Roving tabindex: exactly one item is in the tab order at a time (the first enabled one),
            // the rest are focusable only via the arrow controller. A disabled item is never tabbable.
            bool tabbable = firstTabbable && !disabled;
            if (tabbable) firstTabbable = false;
            sb.Append(" tabindex=\"").Append(tabbable ? "0" : "-1").Append("\"");

            if (disabled)
            {
                // aria-disabled, NOT the disabled attribute: a disabled <button> cannot take focus,
                // so the arrow controller's focusItem silently no-opped on one and the player got
                // STUCK — every further Down re-targeted the same unfocusable entry (any menu with a
                // disabled entry: a header menu on a built-in view greys Rename/Delete/Export). The
                // ARIA menu pattern wants disabled items focusable so they can be read and skipped
                // past; without an onclick, Enter on one does nothing.
                sb.Append(" aria-disabled=\"true\"");
            }
            else
            {
                string id = ServerHandles.Register(new ServerHandles.StopHandle { El = entry }, entry);
                sb.Append(" id=\"").Append(id).Append("\" onclick=\"act('").Append(id).Append("')\"");
            }

            if (entry.IsSubmenu) sb.Append(" aria-haspopup=\"true\" data-fm-folder");
            if (entry.IsMenuBack) sb.Append(" data-fm-menu-back");
            if (entry.IsMenuDismiss) sb.Append(" data-fm-menu-first data-fm-menu-close");

            sb.Append(">").Append(ServerText.EscapeText(entry.GetPrimaryLabel())).Append("</button></li>");
        }
        sb.Append("</ul>");
    }

    // Render one walked item through the role/feature dispatch. Internal so a feature renderer that
    // expands a composite (SpeakToRenderer) can render the items it walked out of that composite.
    internal static void RenderItem(StringBuilder sb, ScreenWalker.RenderItem item)
    {
        if (item is ScreenWalker.StopItem s) RenderNode(sb, s.Element,
            headingTag: s.HeadingLevel > 0 ? "h" + s.HeadingLevel : null);
        else if (item is ScreenWalker.TextListItem list) RenderTextList(sb, list);
        else if (item is ScreenWalker.TableItem t) RenderTable(sb, t);
        else if (item is ScreenWalker.ColumnSummaryItem colSummary) RenderColumnSummary(sb, colSummary);
        else if (item is ScreenWalker.FixturesItem fx) RenderFixtures(sb, fx);
        else if (item is ScreenWalker.LeagueTableItem lt) RenderLeagueTable(sb, lt);
        else if (item is ScreenWalker.ConditionTableItem ct) RenderConditionTable(sb, ct);
        else if (item is ScreenWalker.FixtureScheduleItem fsc) RenderFixtureSchedule(sb, fsc);
        else if (item is ScreenWalker.ScoreboardItem sc) RenderScoreboard(sb, sc);
        else if (item is ScreenWalker.CarouselTileItem car) RenderCarouselTile(sb, car);
        else if (item is ScreenWalker.StatsToggleItem stog) RenderStatsToggle(sb, stog);
        else if (item is ScreenWalker.TileActivatorItem tact) RenderTileActivator(sb, tact);
        else if (item is ScreenWalker.TabBarItem tb) RenderTabBar(sb, tb);
        else if (item is ScreenWalker.RadioGroupItem rg) RenderRadioGroup(sb, rg);
        else if (item is ScreenWalker.HeadingItem h) RenderHeadingItem(sb, h);
        else if (item is ScreenWalker.NoteItem n) RenderNoteItem(sb, n);
        else if (item is ScreenWalker.JoinedTextItem joined) RenderJoinedText(sb, joined);
        else if (item is ScreenWalker.ContractTermsItem cterms) RenderContractTerms(sb, cterms);
        else if (item is ScreenWalker.ContractConditionsItem cconds) RenderContractConditions(sb, cconds);
        else if (item is ScreenWalker.ResponsibilitiesTableItem resp) RenderResponsibilities(sb, resp);
        else if (item is ScreenWalker.StaffDutiesTableItem duties) RenderStaffDuties(sb, duties);
        else if (item is ScreenWalker.AdviceTableItem advice) RenderAdviceTable(sb, advice);
        else if (item is ScreenWalker.LabelValueItem lv) RenderLabelValue(sb, lv);
        else if (item is ScreenWalker.PitchGridItem pgrid) RenderPitchGrid(sb, pgrid);
        else if (item is ScreenWalker.PositionGridItem posgrid) RenderPositionGrid(sb, posgrid);
        else if (item is ScreenWalker.LineChartItem lc) RenderLineChart(sb, lc);
        else if (item is ScreenWalker.SyntheticTableItem syn) RenderSyntheticTable(sb, syn);
        else if (item is ScreenWalker.NativeFormTableItem form) RenderNativeFormTable(sb, form);
        else if (item is ScreenWalker.TeamInstructionsItem ti) TeamInstructions.Render(sb, ti);
        else if (item is ScreenWalker.SetPieceChipTableItem spct) SetPieceInstructions.RenderChipTable(sb, spct);
        else if (item is ScreenWalker.ValueChartItem vc) RenderValueChart(sb, vc);
        else if (item is ScreenWalker.ScoutFocusListItem sfl) RenderScoutFocusList(sb, sfl);
        else if (item is ScreenWalker.FocusListItem fl) RenderFocusList(sb, fl);
        else if (item is ScreenWalker.ProjectionsTableItem pt) RenderProjectionsTable(sb, pt);
        else if (item is ScreenWalker.PitchSummaryItem ps) RenderPitchSummary(sb, ps);
        else if (item is ScreenWalker.CalendarItem cal) RenderCalendar(sb, cal);
        else if (item is ScreenWalker.ScheduleWeekItem swk) RenderScheduleWeek(sb, swk);
        else if (item is ScreenWalker.StatComparisonItem stat) RenderStatComparison(sb, stat);
        else if (item is ScreenWalker.AttributeTableItem at) RenderAttributeTable(sb, at);
        else if (item is ScreenWalker.PlayerBannerItem pb) RenderPlayerBanner(sb, pb);
        else if (item is ScreenWalker.TodaysFixturesItem tf) RenderTodaysFixtures(sb, tf);
        else if (item is ScreenWalker.AttributeSummaryItem asum) RenderAttributeSummary(sb, asum);
        else if (item is ScreenWalker.RegionStartItem rs) RenderRegionStart(sb, rs);
        else if (item is ScreenWalker.RegionEndItem) sb.Append("</div>");
        else if (item is ScreenWalker.MessagesTableItem mt) RenderMessagesTable(sb, mt);
        else if (item is ScreenWalker.BookmarksTableItem bt) RenderBookmarksTable(sb, bt);
    }

    private static void RenderTextList(StringBuilder sb, ScreenWalker.TextListItem list)
    {
        if (list.Rows != null)
        {
            if (list.Source.panel == null || !ElementBridge.IsVisibleInHierarchy(list.Source))
            { sb.Append(ProvisionalComment); return; }
            sb.Append("<ul>");
            foreach (var row in list.Rows)
            {
                sb.Append("<li>");
                foreach (var part in row) RenderItem(sb, part);
                sb.Append("</li>");
            }
            sb.Append("</ul>");
            return;
        }
        sb.Append("<ul>");
        for (int i = 0; i < list.Items.Count; i++)
        {
            var trailing = list.TrailingAt(i);
            string id = LiveValues.RegisterReadOnly(list.Items[i]);
            if (trailing == null)
            {
                sb.Append("<li id=\"").Append(id).Append("\">")
                  .Append(ServerText.Escape(DisplayText.Of(list.Items[i])))
                  .Append("</li>");
                continue;
            }
            sb.Append("<li><span id=\"").Append(id).Append("\">")
              .Append(ServerText.Escape(DisplayText.Of(list.Items[i]))).Append("</span>");
            foreach (var extra in trailing)
                sb.Append(" <span id=\"").Append(LiveValues.RegisterReadOnly(extra)).Append("\">")
                  .Append(ServerText.Escape(DisplayText.Of(extra))).Append("</span>");
            sb.Append("</li>");
        }
        sb.Append("</ul>");
    }

    // Open an ARIA region landmark (the Messages panel's "message list" / "message" panes). The
    // matching RegionEndItem closes the <div> in document order. The label is mod-authored chrome;
    // EscapeText keeps it handled like every other string reaching the page.
    private static void RenderRegionStart(StringBuilder sb, ScreenWalker.RegionStartItem item)
    {
        sb.Append("<div role=\"region\" aria-label=\"").Append(ServerText.EscapeText(item.Label)).Append("\"");
        // The list region is the Alt+Left focus fallback (when no email has been opened yet), so make
        // it programmatically focusable and findable by FM.messages.
        if (item.IsList) sb.Append(" tabindex=\"-1\" data-fm-msg-list");
        sb.Append(">");
    }

    // A league-history line chart as a semantic table (Season, Division, Position). The graphical
    // chart carries no text; LeagueHistoryChartReader pulled the data points and LeagueHistoryHtml
    // formats them, so the player browses the club's season-by-season record the chart depicts.
    private static void RenderLineChart(StringBuilder sb, ScreenWalker.LineChartItem item)
    {
        // <h3>, one level under the card's <h2>, matching the fixtures table's stage heading. The title
        // is already cleaned display text, so HTML-escape only.
        if (!string.IsNullOrEmpty(item.Title))
            sb.Append("<h3>").Append(ServerText.Escape(item.Title)).Append("</h3>");
        sb.Append(LeagueHistoryHtml.Build(item.Rows));
    }

    // A SyntheticTables consolidation as a semantic table: mod-chrome column headers, the first
    // cell of each row as the row header, cells already cleaned at walk time.
    private static void RenderNativeFormTable(StringBuilder sb,ScreenWalker.NativeFormTableItem item)
    {
        if(item.Provisional)sb.Append(ProvisionalComment);
        sb.Append("<table><thead><tr>");
        foreach(string heading in item.Headers)sb.Append("<th scope=\"col\">").Append(ServerText.EscapeText(heading)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach(var row in item.Rows)
        {
            sb.Append("<tr>");
            for(int column=0;column<row.Count;column++)
            {
                sb.Append(column==item.RowHeader?"<th scope=\"row\">":"<td>");
                foreach(var part in row[column])RenderItem(sb,part);
                sb.Append(column==item.RowHeader?"</th>":"</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }
    private static void RenderSyntheticTable(StringBuilder sb, ScreenWalker.SyntheticTableItem item)
    {
        if(item.Provisional)sb.Append(ProvisionalComment);
        AppendStringGrid(sb, item.Headers, item.Rows, item.RowHeader, item.RowActions);
    }

    // A table of already-cleaned strings: column headers, then the naming cell of each row as its
    // row header. Shared by every consolidation whose cells are plain text by walk time.
    // <paramref name="rowHeader"/> is the column that identifies the row — the first by default,
    // which is the label column in a label/value consolidation. A row with an action renders its
    // naming cell as a button driving that control, labelled by the cell's own text.
    private static void AppendStringGrid(StringBuilder sb, string[] headers, List<List<string>> rows,
        int rowHeader = 0, List<IAccessibleElement> rowActions = null)
    {
        sb.Append("<table><thead><tr>");
        foreach (var h in headers)
            sb.Append("<th scope=\"col\">").Append(ServerText.Escape(h)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var action = rowActions != null && r < rowActions.Count ? rowActions[r] : null;
            sb.Append("<tr>");
            for (int i = 0; i < row.Count; i++)
            {
                bool isHeader = i == rowHeader;
                sb.Append(isHeader ? "<th scope=\"row\">" : "<td>");
                if (isHeader && action != null)
                {
                    bool disabled = ServerText.IsDisabled(action);
                    string id = disabled ? LiveValues.AllocateId(ServerHandles.KeyFor(action))
                        : ServerHandles.Register(new ServerHandles.StopHandle { El = action }, action);
                    sb.Append("<button type=\"button\" id=\"").Append(id).Append('"');
                    if (disabled) sb.Append(" disabled");
                    else sb.Append(" onclick=\"act('").Append(id).Append("')\"");
                    AppendContextMenuAttr(sb, action.HasContextMenu);
                    sb.Append('>')
                      .Append(ServerText.Escape(row[i])).Append("</button>");
                }
                else sb.Append(ServerText.Escape(row[i]));
                sb.Append(isHeader ? "</th>" : "</td>");
            }
            // Rows shorter than the header count pad with empty cells so the grid stays rectangular
            // for table navigation; an empty cell is an honest "nothing here".
            for (int i = row.Count; i < headers.Length; i++)
                sb.Append("<td></td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    // A date-plotted line chart (a finance card's history) as a Date-Value table: the game's own
    // x-axis labels over the plotted values, in the chart's place. A money chart's values arrive
    // already carrying the game's currency notation for whichever currency the player selected
    // (FinanceChartReader translates them), so the unit is in the cells and needs no column of its
    // own. The caption names the metric — several of these tables stack on one finance screen.
    private static void RenderValueChart(StringBuilder sb, ScreenWalker.ValueChartItem item)
    {
        var headers = item.RenderedHeaders();
        var dates = item.DateColumn;
        // Several lines that could not be told apart. The post-load join normally replaces such a
        // chart with the marker outright; this is the same refusal for any path that renders
        // without it, so anonymous columns can never reach the page.
        if (headers == null || dates == null)
        {
            sb.Append("<p>").Append(ServerText.Escape(Degrade.Unreadable + " chart")).Append("</p>");
            return;
        }

        sb.Append("<table>");
        if (!string.IsNullOrEmpty(item.Title))
            sb.Append("<caption>").Append(ServerText.Escape(item.Title)).Append("</caption>");
        sb.Append("<thead><tr>");
        foreach (var h in headers)
            sb.Append("<th scope=\"col\">").Append(ServerText.EscapeText(h)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        for (int i = 0; i < dates.Count; i++)
        {
            sb.Append("<tr><td>").Append(ServerText.EscapeText(dates[i].Label)).Append("</td>");
            foreach (var line in item.Series)
                sb.Append("<td>").Append(ServerText.Escape(line[i].Value)).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");

        if (item.OmittedColumns is { Count: > 0 })
            sb.Append("<p>")
              .Append(ServerText.Escape(ScreenWalker.ValueChartItem.OmittedPrefix))
              .Append(ServerText.EscapeText(string.Join(", ", item.OmittedColumns)))
              .Append("</p>");

        if (item.AmbiguousColumns is { Count: > 0 })
            sb.Append("<p>")
              .Append(ServerText.Escape(ScreenWalker.ValueChartItem.AmbiguousPrefix))
              .Append(ServerText.EscapeText(string.Join(", ", item.AmbiguousColumns)))
              .Append("</p>");
    }

    // The selected scout's recruitment focuses as a table: the focus names down the row headers,
    // its own Edit and Delete buttons in the cells. Each button also carries the focus name in its
    // accessible name, so a player tabbing between buttons (rather than reading across the table)
    // still knows which focus a Delete would remove.
    private static void RenderScoutFocusList(StringBuilder sb, ScreenWalker.ScoutFocusListItem item)
    {
        if (item.Rows.Count == 0)
        {
            sb.Append("<p>").Append(ServerText.Escape(ScreenWalker.ScoutFocusListItem.EmptyNote)).Append("</p>");
            return;
        }

        sb.Append("<table><thead><tr>");
        foreach (var h in ScreenWalker.ScoutFocusListItem.Headers)
            sb.Append("<th scope=\"col\">").Append(ServerText.Escape(h)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var row in item.Rows)
        {
            sb.Append("<tr><th scope=\"row\">").Append(ServerText.EscapeText(row.Name)).Append("</th>");
            AppendScoutFocusAction(sb, row.Edit, row.Name);
            AppendScoutFocusAction(sb, row.Delete, row.Name);
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static void AppendScoutFocusAction(StringBuilder sb, IAccessibleElement el, string focusName)
    {
        string id = ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
        string action;
        try { action = DisplayText.Of(el); }
        catch (Exception ex) { action = Degrade.Control($"focus action label read failed: {ex.Message}"); }
        if (string.IsNullOrWhiteSpace(action)) action = Degrade.Control("a focus action had no label");

        sb.Append("<td><button type=\"button\" id=\"").Append(id)
          .Append("\" onclick=\"act('").Append(id).Append("')\" aria-label=\"")
          .Append(ServerText.Escape(focusName + ", " + action)).Append("\">")
          .Append(ServerText.EscapeText(action)).Append("</button></td>");
    }

    // The Recruitment Focus tool's focus list: the master list of the scouts-and-focuses pair, drawn
    // as the same table shape the Scouting Assignments card's staff list uses. The radios keep a
    // shared name, so this is still one single-select group — the cells only add the column context
    // the run-together radio label could not carry ("French National, In Progress, 14").
    private static void RenderFocusList(StringBuilder sb, ScreenWalker.FocusListItem item)
    {
        if (item.Rows.Count == 0)
        {
            sb.Append("<p>").Append(ServerText.Escape(ScreenWalker.FocusListItem.EmptyNote)).Append("</p>");
            return;
        }

        // Unique among groups on this page; focus restores by each radio's own identity-based id.
        string groupName = "fmrg" + (uint)ServerHandles.IdentityPath(item.Group).GetHashCode();
        sb.Append("<table><thead><tr>");
        foreach (var h in ScreenWalker.FocusListItem.Headers)
            sb.Append("<th scope=\"col\">").Append(ServerText.Escape(h)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var row in item.Rows)
        {
            string id = ServerHandles.Register(new ServerHandles.StopHandle { El = row.Radio }, row.Radio);
            bool on = ServerText.State(row.Radio) == "selected";
            sb.Append("<tr><td><input type=\"radio\" name=\"").Append(groupName)
              .Append("\" id=\"").Append(id).Append("\"")
              .Append(on ? " checked" : "")
              // The row header names the row, so the control itself only has to say what it does.
              .Append(" aria-label=\"").Append(ServerText.EscapeText(row.Name)).Append("\"")
              .Append(" onchange=\"actradio('").Append(id).Append("')\"></td>");
            sb.Append("<th scope=\"row\">").Append(ServerText.EscapeText(row.Name)).Append("</th>")
              .Append("<td>").Append(ServerText.EscapeText(row.Status)).Append("</td>")
              .Append("<td>").Append(ServerText.EscapeText(row.Matched)).Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    // A pitch of position slots as one table (the Squad Planner position strip, the Best XI
    // pitch): position as the row header, the slot's values in the remaining columns. Cells are
    // display text prepared at walk time; the headers mix mod chrome ("Position") and game text.
    // A row with a Select adapter draws its position cell as a pressed-state button — activating
    // it selects that position on the game's pitch and the plan section below follows.
    private static void RenderPitchSummary(StringBuilder sb, ScreenWalker.PitchSummaryItem item)
    {
        sb.Append("<table><thead><tr>");
        foreach (var h in item.Headers)
            sb.Append("<th scope=\"col\">").Append(ServerText.EscapeText(h)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var row in item.Rows)
        {
            sb.Append("<tr><th scope=\"row\">");
            if (row.Select != null)
            {
                string id = ServerHandles.Register(new ServerHandles.StopHandle { El = row.Select }, row.Select);
                sb.Append("<button type=\"button\" id=\"").Append(id)
                  .Append("\" onclick=\"act('").Append(id).Append("')\" aria-pressed=\"")
                  .Append(row.Selected ? "true" : "false").Append("\">")
                  .Append(ServerText.EscapeText(row.Cells[0])).Append("</button>");
            }
            else
            {
                sb.Append(ServerText.EscapeText(row.Cells[0]));
            }
            sb.Append("</th>");
            for (int c = 1; c < item.Headers.Count; c++)
            {
                string cell = c < row.Cells.Count ? row.Cells[c] : "";
                sb.Append("<td>").Append(ServerText.EscapeText(cell)).Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    // The Projections card's metric blocks as one grid: a row per metric (Balance, Turnover, ...),
    // a column per season, replacing six repeating label/value lists and their charts. Season
    // labels and every cell are game text read at walk time.
    private static void RenderProjectionsTable(StringBuilder sb, ScreenWalker.ProjectionsTableItem item)
    {
        sb.Append("<table><thead><tr><th scope=\"col\"></th>");
        foreach (var label in item.SeasonLabels)
            sb.Append("<th scope=\"col\">").Append(ServerText.EscapeText(label)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var block in item.Blocks)
        {
            sb.Append("<tr><th scope=\"row\">").Append(ServerText.EscapeText(block.Metric)).Append("</th>");
            for (int c = 0; c < item.SeasonLabels.Count; c++)
            {
                string value = c < block.Seasons.Count ? block.Seasons[c].Value : "";
                sb.Append("<td>").Append(ServerText.EscapeText(value)).Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    // The schedule calendar as a week-by-weekday table (CalendarScheduleHtml): weekday column headers,
    // one row per week, each populated cell carrying its day-of-month, state, and the day's events. Each
    // day's events are rendered here live (so buttons register handles and stay clickable) into an HTML
    // string the pure builder injects verbatim; the date/state scaffolding and escaping are the builder's.
    private static void RenderCalendar(StringBuilder sb, ScreenWalker.CalendarItem cal)
    {
        var weeks = new List<CalendarScheduleHtml.ScheduleWeek>(cal.Weeks.Count);
        foreach (var week in cal.Weeks)
        {
            var sw = new CalendarScheduleHtml.ScheduleWeek();
            for (int col = 0; col < 7; col++)
            {
                var day = week.Days[col];
                if (day == null) continue; // padding stays a null slot (empty cell)
                if (day.Provisional || (day.Events.Count == 0 && ScreenWalker.CalendarEventsPending(day.EventsList)))
                    sb.Append(ProvisionalComment);

                var events = new StringBuilder();
                foreach (var ev in day.Events)
                    RenderCalendarEvent(events, ev);

                sw.Days[col] = new CalendarScheduleHtml.ScheduleDay(day.DateText, day.State, events.ToString());
            }

            // The week's training schedule, rendered through the normal node path so it keeps its
            // button, its disabled state on a week already under way, and its description.
            if (week.Focus != null || week.FocusItems is { Count: > 0 })
            {
                var focus = new StringBuilder();
                if (week.FocusItems != null)
                    foreach (var item in week.FocusItems) RenderItem(focus, item);
                else RenderItem(focus, week.Focus);
                sw.FocusHtml = focus.ToString();
            }

            weeks.Add(sw);
        }
        sb.Append(CalendarScheduleHtml.Build(weeks, cal.WeekdayLabels));
    }

    // The create-schedule editor's week: one row per weekday, the day as the row header, then the
    // day's session tiles and intensity bars in the columns the game itself declares. Sessions render
    // through the normal node path so each keeps its context menu — that menu is how a session is
    // changed, and this table would be pointless if it stripped the editing it exists to expose.
    //
    // The columns are matched to the header row by position: the day's EventsList fills the leading
    // columns and its IntensitiesList the rest, in draw order, which is the order the header names
    // them. Where the counts disagree with the header the extra cells still render — an unlabelled
    // column is a gap the player can see, whereas dropping cells would hide real settings.
    // The game draws no title over this grid, so the table would otherwise arrive as an unannounced
    // slab between the description field and the impact table. Same treatment SyntheticTables gives
    // an untitled block through its Heading field.
    private const string ScheduleWeekHeading = "Training Week";

    private static void RenderScheduleWeek(StringBuilder sb, ScreenWalker.ScheduleWeekItem week)
    {
        sb.Append("<h3>").Append(ScheduleWeekHeading).Append("</h3>");
        sb.Append("<table><thead><tr><th scope=\"col\">Day</th>");
        foreach (var header in week.Headers)
            sb.Append("<th scope=\"col\">").Append(ServerText.Escape(header)).Append("</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var row in week.Rows)
        {
            sb.Append("<tr><th scope=\"row\">").Append(ServerText.Escape(row.DayName)).Append("</th>");
            foreach (var session in row.Sessions)
            {
                sb.Append("<td>");
                RenderItem(sb, session);
                sb.Append("</td>");
            }
            foreach (var intensity in row.Intensities)
                sb.Append("<td>").Append(ServerText.Escape(intensity)).Append("</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
    }

    // A schedule calendar event split into per-sub-element targets: each menu-bearing SIText leaf (the
    // competition, the opponent) becomes its own button carrying both its left-click link (navigates to
    // the report/fixture) and its own aria-haspopup menu, so Shift+F10 on the opponent opens the team
    // menu and on the competition opens the competition menu — the multiple-menus-per-tile case. Non-menu
    // link parts (the score) stay clickable; plain parts (time, venue) render as text. An event with no
    // menu leaf (a training session, an upcoming "Match" placeholder) falls back to the single-stop render.
    private static void RenderCalendarEvent(StringBuilder sb, ScreenWalker.RenderItem ev)
    {
        if (ev is ScreenWalker.StopItem s && s.Element is Adapters.CalendarEventAdapter cea)
        {
            var leaves = OrderedTextLeaves(cea.BackingElement);
            bool anyMenu = false;
            foreach (var leaf in leaves)
                if (Adapters.ContextMenuOpener.HasMenuManipulator(leaf)) { anyMenu = true; break; }

            if (anyMenu)
            {
                sb.Append("<p>");
                bool firstPart = true;
                foreach (var leaf in leaves)
                {
                    string raw = leaf.Cast<UnityEngine.UIElements.TextElement>().text;
                    bool isLink = Adapters.SITextLink.HasLink(raw);
                    bool isMenu = Adapters.ContextMenuOpener.HasMenuManipulator(leaf);
                    string text = isLink ? Speech.TextFilters.ExtractLinkText(raw) : Speech.TextFilters.CleanForDisplay(raw);
                    if (string.IsNullOrEmpty(text)) continue;
                    if (!firstPart) sb.Append(", ");
                    firstPart = false;

                    if (isMenu || isLink)
                    {
                        string id = ServerHandles.RegisterKey(
                            new ServerHandles.LeafHandle { Leaf = leaf, HasLink = isLink },
                            ServerHandles.IdentityPath(leaf));
                        sb.Append("<button type=\"button\" id=\"").Append(id)
                          .Append("\" onclick=\"act('").Append(id).Append("')\"");
                        AppendContextMenuAttr(sb, isMenu);
                        sb.Append('>').Append(ServerText.EscapeText(text)).Append("</button>");
                    }
                    else
                    {
                        sb.Append(ServerText.EscapeText(text));
                    }
                }
                sb.Append("</p>");
                return;
            }
        }
        RenderItem(sb, ev);
    }

    // Visible SIText leaves with text under a tile, in document order — the parts of a composite event.
    private static List<VisualElement> OrderedTextLeaves(VisualElement root)
    {
        var result = new List<VisualElement>();
        var stack = new Stack<VisualElement>();
        stack.Push(root);
        int seen = 0;
        while (stack.Count > 0 && seen < 600)
        {
            var e = stack.Pop();
            seen++;
            if (e.GetIl2CppType().Name == "SIText" && ElementBridge.IsVisible(e))
            {
                var t = e.Cast<UnityEngine.UIElements.TextElement>().text;
                if (!string.IsNullOrEmpty(t)) result.Add(e);
            }
            for (int i = e.hierarchy.childCount - 1; i >= 0; i--)
                stack.Push(e.hierarchy[i]);
        }
        return result;
    }

    // The match-stats comparison card as a semantic table: one row per stat, a column per team. The
    // game's per-row layout (home value, stat name, away value, comparison bar) loses team attribution
    // when flattened; MatchStatsTableReader read the values and MatchStatsHtml ties each to its team.
    private static void RenderStatComparison(StringBuilder sb, ScreenWalker.StatComparisonItem item)
    {
        sb.Append(FM27Access.Navigation.MatchStatsHtml.Build(item.HomeTeam, item.AwayTeam, item.Rows));
    }

    // The tactics pitch as a 2D arrow-navigable grid (role="grid", so the screen reader hands the
    // arrows to app.js). Three parts share one widget: the browse grid (occupied slots only — the
    // formation), a shared action menu (Change role / Edit instructions / Change pitch position), and
    // a hidden move grid (all 24 slots) used while placing a player. Each cell carries its precomputed
    // spatial neighbours (data-up/down/left/right -> target cell id) so the client just follows them;
    // the geometry lives in the unit-tested PitchNavigation. Rows run back to front (keeper first).
    private static void RenderPitchGrid(StringBuilder sb, ScreenWalker.PitchGridItem item)
    {
        var formation = item.Formation;
        bool editable = item.Editable != null && item.Editable.Count > 0;
        int pitchIndex = _pitchCount++;
        // Preserve the original single/editable pitch contract. Additional
        // read-only formations on a native report need their own neighbour IDs.
        bool originalIds = editable ? _editablePitchCount++ == 0 : pitchIndex == 0 && !_hasEditablePitch;
        string browseId = originalIds ? "fm-pitch-browse" : "fm-pitch-browse-" + pitchIndex;
        string browsePrefix = originalIds ? "bc-" : "bc" + pitchIndex + "-";
        string suffix = originalIds ? "" : "-" + pitchIndex;
        string movePrefix = originalIds ? "mc-" : "mc" + pitchIndex + "-";

        var bySlot = new Dictionary<string, PitchPlayer>();
        var names = new Dictionary<string, string>();
        foreach (var p in formation.Players)
        {
            if (string.IsNullOrEmpty(p.Slot)) continue;
            bySlot[p.Slot] = p;
            if (!string.IsNullOrEmpty(p.Name)) names[p.Slot] = FM27Access.Speech.TextFilters.CleanForDisplay(p.Name);
        }
        var occSet = new HashSet<string>(formation.OccupiedSlots());
        var allSet = new HashSet<string>();
        foreach (var row in PitchNavigation.Rows) foreach (var s in row) allSet.Add(s);

        sb.Append("<div class=\"fm-pitch\" id=\"fm-pitch-widget").Append(suffix).Append("\">");

        // The formation as prose above the grid: one linear read gives the whole shape (rows and
        // flanks) without arrowing through the cells. Built from the same model as the grid, so the
        // two cannot drift apart.
        sb.Append("<section aria-label=\"Formation overview\">");
        foreach (var line in formation.OverviewLines())
            sb.Append("<p>").Append(ServerText.Escape(line)).Append("</p>");
        sb.Append("</section>");

        // Browse grid: occupied slots only. On the editable pitch each cell carries its action handles
        // and Enter opens the menu; a read-only card omits those, so the cell is just the player.
        sb.Append("<table role=\"grid\" id=\"").Append(browseId).Append("\" data-pitch=\"browse\" aria-label=\"")
          .Append(editable
              ? "Formation. Arrow keys move between players; Enter for options."
              : formation.IsPositionOnly ? "Formation. Arrow keys move between positions."
              : "Formation. Arrow keys move between players.")
          .Append("\"><tbody>");
        bool first = true;
        for (int r = 0; r < PitchNavigation.Rows.Length; r++)
        {
            var present = System.Array.FindAll(PitchNavigation.Rows[r], occSet.Contains);
            if (present.Length == 0) continue;
            sb.Append("<tr role=\"row\">");
            foreach (var slot in present)
            {
                sb.Append("<td role=\"gridcell\" id=\"").Append(browsePrefix).Append(slot).Append("\" data-slot=\"").Append(slot)
                  .Append("\" tabindex=\"").Append(first ? "0" : "-1").Append('"');
                AppendNeighbors(sb, occSet, slot, browsePrefix);

                string label;
                if (editable && item.Editable.TryGetValue(slot, out var tp))
                {
                    string key = ServerHandles.IdentityPath(tp.BackingElement);
                    var moveSource = PitchMoveSourceHandle.Capture(item.Pitch, tp, slot);
                    if (moveSource != null) key += "|" + moveSource.Identity;
                    string roleId = ServerHandles.RegisterKey(new PitchActionHandle { Adapter = tp, Action = PitchAction.Role, Source = moveSource }, key + "#role");
                    string piId = ServerHandles.RegisterKey(new PitchActionHandle { Adapter = tp, Action = PitchAction.Instructions, Source = moveSource }, key + "#pi");
                    string moveId = moveSource == null ? "" : ServerHandles.RegisterKey(moveSource, key + "#move|" + moveSource.Identity);
                    sb.Append(" data-role=\"").Append(roleId).Append("\" data-pi=\"").Append(piId)
                      .Append("\" data-move=\"").Append(moveId).Append('"');
                    label = DisplayText.Of(tp);
                }
                else
                {
                    label = PitchFormation.Describe(bySlot[slot]);
                }
                sb.Append('>').Append(ServerText.Escape(label)).Append("</td>");
                first = false;
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");

        // The action menu and the 24-slot move grid only exist on the editable pitch; a read-only card
        // has no actions, so it ends at the browse grid.
        if (editable)
        {
            sb.Append("<div role=\"menu\" id=\"fm-pitch-actions").Append(suffix).Append("\" hidden>")
              .Append("<button type=\"button\" role=\"menuitem\" tabindex=\"-1\" data-act=\"role\">Change player role</button>")
              .Append("<button type=\"button\" role=\"menuitem\" tabindex=\"-1\" data-act=\"pi\">Edit player instructions</button>")
              .Append("<button type=\"button\" role=\"menuitem\" tabindex=\"-1\" data-act=\"move\">Change pitch position</button>")
              .Append("</div>");

            // Move grid: every slot, occupied or empty. Source/labels are finalised client-side per move.
            sb.Append("<table role=\"grid\" id=\"fm-pitch-move").Append(suffix).Append("\" data-pitch=\"move\" hidden ")
              .Append("aria-label=\"Choose a new position. Arrow keys move; Enter places; Escape cancels.\"><tbody>");
            for (int r = 0; r < PitchNavigation.Rows.Length; r++)
            {
                sb.Append("<tr role=\"row\">");
                foreach (var slot in PitchNavigation.Rows[r])
                {
                    names.TryGetValue(slot, out var occ);
                    string pos = PitchNavigation.SlotPhrase(slot);
                    string mlabel = string.IsNullOrEmpty(occ) ? pos + ", available" : pos + ", " + occ;
                    sb.Append("<td role=\"gridcell\" id=\"").Append(movePrefix).Append(slot).Append("\" data-slot=\"").Append(slot)
                      .Append("\" tabindex=\"-1\"");
                    // data-pos/data-name: the ingredients openMove composes its per-move
                    // announcements from ("selected" on the source, "swap X with Y" on occupied
                    // cells, "move X here" on empty ones). Only the client knows which cell is
                    // the move's source, so the phrases are assembled there.
                    sb.Append(" data-pos=\"").Append(ServerText.Escape(pos)).Append('"');
                    if (!string.IsNullOrEmpty(occ))
                        sb.Append(" data-occ=\"1\" data-name=\"").Append(ServerText.Escape(occ)).Append('"');
                    AppendNeighbors(sb, allSet, slot, movePrefix);
                    sb.Append('>').Append(ServerText.Escape(mlabel)).Append("</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
        }
        sb.Append("</div>");
    }

    // The position-picker pitch as a plain data table: one row per pitch band, one column per lane.
    // A cell holds that slot's filter toggle; the row and column headers say where the slot is, which
    // is the whole point — the game labels three different centre-back slots "D (C)", and only the
    // pitch tells them apart. Headers are mod chrome (PitchNavigation's row and flank labels); the
    // checkbox label stays the game's own position text, unaltered.
    //
    // A deliberately ordinary <table>, not the role="grid" widget the formation pitch uses: this is a
    // form to fill in, so the screen reader's own table-navigation and checkbox handling apply, with
    // no custom arrow-key layer to learn.
    private static void RenderPositionGrid(StringBuilder sb, ScreenWalker.PositionGridItem item)
    {
        var bySlot = item.BySlot;

        // Rows the current formation actually uses. An all-empty band is dropped rather than
        // rendered as a row of blanks, mirroring the formation grid.
        var rows = new List<int>();
        for (int r = 0; r < PitchNavigation.Rows.Length; r++)
            foreach (var slot in PitchNavigation.Rows[r])
                if (bySlot.ContainsKey(slot)) { rows.Add(r); break; }
        if (rows.Count == 0) return;

        // Lanes the formation uses, so a narrow shape does not carry three permanently empty columns.
        var cols = new List<int>();
        for (int c = 0; c < PitchNavigation.FlankLabels.Count; c++)
            if (UsesColumn(bySlot, c)) cols.Add(c);

        // Named after the phase tab that governs it, so the table announces which formation it shows
        // rather than leaving the player to infer it from the tab they last passed.
        sb.Append("<table aria-label=\"Positions to search for");
        if (!string.IsNullOrEmpty(item.Phase))
            sb.Append(", ").Append(ServerText.EscapeText(item.Phase));
        sb.Append("\"><thead><tr><td></td>");
        foreach (int c in cols)
            sb.Append("<th scope=\"col\">").Append(ServerText.Escape(Capitalise(PitchNavigation.FlankLabels[c]))).Append("</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (int r in rows)
        {
            sb.Append("<tr><th scope=\"row\">")
              .Append(ServerText.Escape(PitchNavigation.RowLabels[r])).Append("</th>");
            foreach (int c in cols)
            {
                sb.Append("<td>");
                var el = SlotAt(bySlot, r, c);
                if (el != null) AppendPositionToggle(sb, el);
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    // One slot's toggle as a labelled checkbox, matching the generic checkbox stop rendering so the
    // handle registration, checked state and activation path are identical — only the wrapper differs
    // (a table cell instead of a paragraph). The game disables the other slots once a position is
    // committed; a disabled toggle registers read-only and says so, rather than offering a click that
    // silently does nothing.
    private static void AppendPositionToggle(StringBuilder sb, IAccessibleElement el)
    {
        bool disabled = ServerText.IsDisabled(el);
        string id = disabled
            ? LiveValues.RegisterReadOnly(el)
            : ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
        // Exact part match, never a substring: the adapter's off state is the word "not selected".
        bool on = System.Array.IndexOf(
            (ServerText.State(el) ?? "").Split(new[] { ", " }, StringSplitOptions.None), "selected") >= 0;
        sb.Append("<label><input type=\"checkbox\" id=\"").Append(id).Append('"')
          .Append(on ? " checked" : "");
        if (disabled) sb.Append(" disabled");
        else sb.Append(" onclick=\"act('").Append(id).Append("')\"");
        sb.Append("> ").Append(ServerText.Escape(DisplayText.Of(el))).Append("</label>");
    }

    private static IAccessibleElement SlotAt(Dictionary<string, IAccessibleElement> bySlot, int row, int col)
    {
        foreach (var slot in PitchNavigation.Rows[row])
            if (PitchNavigation.TryGetCoord(slot, out _, out int c) && c == col
                && bySlot.TryGetValue(slot, out var el))
                return el;
        return null;
    }

    private static bool UsesColumn(Dictionary<string, IAccessibleElement> bySlot, int col)
    {
        foreach (var slot in bySlot.Keys)
            if (PitchNavigation.TryGetCoord(slot, out _, out int c) && c == col) return true;
        return false;
    }

    // "centre left" -> "Centre left". Column headers read better capitalised beside the row labels,
    // which PitchNavigation already supplies in title case.
    private static string Capitalise(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    private static void AppendNeighbors(StringBuilder sb, HashSet<string> present, string slot, string prefix)
    {
        AppendNeighbor(sb, "data-up", present, slot, PitchNavDir.Up, prefix);
        AppendNeighbor(sb, "data-down", present, slot, PitchNavDir.Down, prefix);
        AppendNeighbor(sb, "data-left", present, slot, PitchNavDir.Left, prefix);
        AppendNeighbor(sb, "data-right", present, slot, PitchNavDir.Right, prefix);
    }

    private static void AppendNeighbor(StringBuilder sb, string attr, HashSet<string> present, string slot, PitchNavDir dir, string prefix)
    {
        var n = PitchNavigation.Neighbor(present, slot, dir);
        if (n != null) sb.Append(' ').Append(attr).Append("=\"").Append(prefix).Append(n).Append('"');
    }

    // A mod-authored plain-text note as a <p>. Text is literal chrome (no game rich-text), so Escape
    // (HTML-escape only) suffices. No id: it carries no live value and is not a focus target.
    // Text the layout split across lines, joined into one <p>. Each part keeps its own id so the
    // values SSE patches it in place exactly as it would have on its own line.
    private static void RenderJoinedText(StringBuilder sb, ScreenWalker.JoinedTextItem item)
    {
        if (item.BodySource != null)
            sb.Append("<p id=\"").Append(ServerHandles.Register(new ServerHandles.StopHandle { El = item.BodySource }, item.BodySource))
              .Append(item.IsConversationQuestion
                  ? "\" tabindex=\"-1\" data-fm-conversation-question>"
                  : "\" tabindex=\"-1\" data-fm-msg-body>");
        else sb.Append("<p>");
        AppendJoinedTextParts(sb, item.Parts,item.LeadingCaption);
        sb.Append("</p>");
    }

    private static void AppendJoinedTextParts(StringBuilder sb, List<IAccessibleElement> parts,bool leadingCaption=false)
    {
        var inlineSource=(parts.FirstOrDefault() as Adapters.SITextLink.Part)?.BackingElement.Pointer;
        bool nativeInline=inlineSource!=null&&parts.All(part=>part is Adapters.SITextLink.Part fragment
            &&fragment.BackingElement.Pointer==inlineSource);
        for (int i = 0; i < parts.Count; i++)
        {
            if(nativeInline)sb.Append(ServerText.Escape(((Adapters.SITextLink.Part)parts[i]).SeparatorBefore));
            else if (i > 0) sb.Append(' ');
            var part = parts[i];
            if (part is Adapters.SITextLink.Part && ServerText.SafeRole(part) == "button")
            {
                AppendButton(sb, part, null);
                continue;
            }
            sb.Append("<span id=\"").Append(LiveValues.RegisterReadOnly(part)).Append("\">")
              .Append(ServerText.Escape(DisplayText.Of(part))).Append("</span>");
            if(leadingCaption&&i==0)sb.Append(':');
        }
    }

    private static void RenderNoteItem(StringBuilder sb, ScreenWalker.NoteItem item)
    {
        sb.Append("<p");
        if (!string.IsNullOrEmpty(item.Id)) sb.Append(" id=\"").Append(ServerText.EscapeText(item.Id)).Append('"');
        sb.Append('>').Append(ServerText.Escape(item.Text)).Append("</p>");
    }

    // A synthetic landmark heading the walk inserted at a structural boundary (e.g. the title bar's
    // section tabs). Text may be mod chrome ("Shortcuts") or game-derived (the selected ribbon
    // category, the "Messages" widget title), so EscapeText (clean Unity rich-text, then HTML-escape)
    // covers both at the walk-chosen level. No id: it carries no live value and is never a focus
    // target — idiomorph keeps stable text in place across refreshes on its own.
    private static void RenderHeadingItem(StringBuilder sb, ScreenWalker.HeadingItem item)
    {
        sb.Append("<h").Append(item.Level).Append('>')
          .Append(ServerText.EscapeText(item.Text)).Append("</h").Append(item.Level).Append('>');
    }

    // One attribute section as a semantic table: the combined stat cell ("Composure 15", plain
    // text, tracked so the number refreshes in place) and a "Change this month" cell carrying the
    // month's development, which the game otherwise shows only as a coloured arrow. A whole point
    // gained or lost reads as "Up 1"/"Down 1"; the sub-point drift between reads as progression or
    // regression under way ("Slight progression").
    //
    // The column appears whenever progression is knowable for the player, so a blank cell means
    // "this attribute held steady" and an absent column means "no development data for this
    // player" — two different facts that a movement-only gate rendered identically.
    // The Stat header is a client-side sort toggle
    // (FM.attrs, ordering by the value at the end of each stat cell) — these tables are
    // mod-built, with no game sort behind them. Cell id and handle id are keyed off the row's
    // identity with distinct suffixes so they never collide.
    // The news site's Today's Top Fixtures rail as one table (Competition | Home | Time |
    // Away). Cells keep the game's own clickable competition/club buttons — labels are the
    // game's displayed text, abbreviations included — and a slot the game leaves blank
    // renders an empty cell.
    private static void RenderTodaysFixtures(StringBuilder sb, ScreenWalker.TodaysFixturesItem item)
    {
        string tid = LiveValues.AllocateId(ServerHandles.IdentityPath(item.List) + "#todaysfixtures");
        sb.Append("<table id=\"").Append(tid).Append("\"><thead><tr>")
          .Append("<th scope=\"col\">Competition</th><th scope=\"col\">Home</th>")
          .Append("<th scope=\"col\">Time</th><th scope=\"col\">Away</th>")
          .Append("</tr></thead><tbody>");
        int r = 0;
        foreach (var row in item.Rows)
        {
            sb.Append("<tr>");
            RenderFixtureCell(sb, row.Competition, tid, r, "c");
            RenderFixtureCell(sb, row.Home, tid, r, "h");
            sb.Append("<td>").Append(ServerText.EscapeText(row.Time)).Append("</td>");
            RenderFixtureCell(sb, row.Away, tid, r, "a");
            sb.Append("</tr>");
            r++;
        }
        sb.Append("</tbody></table>");
    }

    private static void RenderFixtureCell(StringBuilder sb, ScreenWalker.FixtureCell cell, string tid, int row, string col)
    {
        sb.Append("<td>");
        if (cell?.Button != null)
        {
            string id = ServerHandles.RegisterKey(new ServerHandles.StopHandle { El = cell.Button }, tid + "#" + row + col);
            LiveValues.Track(id, cell.Button);
            sb.Append("<button type=\"button\" id=\"").Append(id)
              .Append("\" onclick=\"act('").Append(id).Append("')\"");
            AppendContextMenuAttr(sb, cell.Button.HasContextMenu);
            sb.Append(">").Append(ServerText.Escape(DisplayText.Of(cell.Button))).Append("</button>");
        }
        else if (cell != null && cell.Text.Length > 0)
        {
            sb.Append(ServerText.EscapeText(cell.Text));
        }
        sb.Append("</td>");
    }

    // The player report banner as one label/value table: a row header naming what the game draws
    // only as an icon, and the game's own control beside it. No column headers — every row is one
    // property of one player, so a row header alone is what pairs a value with its name ("Club:
    // US Orleans"), and a thead would add a line of chrome above every report.
    //
    // Values are the SAME controls the banner walked out as before the fold, registered the same
    // way: an activatable one keeps its handle, its live-value tracking and its aria-haspopup, so
    // Enter still opens the club, the nation, the transfer-value breakdown, the contract, and
    // still raises the context menus the club and nation buttons carry. A disabled one renders as
    // a disabled button rather than as text, so the player hears that it is a control he cannot
    // use. A row the walk could not name renders an empty row header — never a guessed one.
    private static void RenderPlayerBanner(StringBuilder sb, ScreenWalker.PlayerBannerItem item)
    {
        string tid = LiveValues.AllocateId(ServerHandles.IdentityPath(item.Banner) + "#playerbanner");
        sb.Append("<table id=\"").Append(tid).Append("\"><tbody>");
        foreach (var row in item.Rows)
        {
            if (row.IsAction)
            {
                // Keep the native position inside the same inherited table,
                // with the ordinary control renderer and no empty field header.
                sb.Append("<tr><td colspan=\"2\">");
                RenderNode(sb, row.Value);
                sb.Append("</td></tr>");
                continue;
            }
            sb.Append("<tr><th scope=\"row\">");
            if (row.LabelSource != null) RenderPlayerBannerValue(sb, row.LabelSource);
            else if (row.Label != null) sb.Append(ServerText.EscapeText(row.Label));
            sb.Append("</th><td>");
            if (row.Parts != null) AppendJoinedTextParts(sb, row.Parts);
            else RenderPlayerBannerValue(sb, row.Value);
            sb.Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static void RenderPlayerBannerValue(StringBuilder sb, IAccessibleElement el)
    {
        // Degrade per row: one unreadable value marks itself and leaves the rest of the banner
        // standing, rather than dropping the row (silence reads as "no such field") or the table.
        string text;
        bool disabled;
        string role;
        try
        {
            text = DisplayText.Of(el);
            disabled = ServerText.IsDisabled(el);
            role = ServerText.SafeRole(el);
        }
        catch (Exception ex)
        {
            sb.Append(ServerText.Escape(Degrade.Read($"Player banner value unreadable: {ex.Message}")));
            return;
        }

        if (role == "text")
        {
            string vid = LiveValues.RegisterReadOnly(el);
            sb.Append("<span id=\"").Append(vid).Append("\">")
              .Append(ServerText.EscapeText(text)).Append("</span>");
            return;
        }

        string id = disabled
            ? LiveValues.RegisterReadOnly(el)
            : ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
        if (!disabled) LiveValues.Track(id, el);
        sb.Append("<button type=\"button\" id=\"").Append(id).Append("\"");
        if (disabled) sb.Append(" disabled");
        else sb.Append(" onclick=\"act('").Append(id).Append("')\"");
        if (!disabled) AppendContextMenuAttr(sb, el.HasContextMenu);
        sb.Append(">").Append(ServerText.Escape(text)).Append("</button>");
    }

    private static void RenderAttributeTable(StringBuilder sb, ScreenWalker.AttributeTableItem item)
    {
        // A tab/player change can replace this list's rows during another
        // table's cross-frame load. Retry the captured collection rather than
        // announcing unreadable attributes from the previous native content.
        if (item.List.panel == null || !ElementBridge.IsVisibleInHierarchy(item.List))
        {
            sb.Append(ProvisionalComment);
            return;
        }
        foreach (var row in item.Rows)
            if (row.BackingElement.panel == null || !ElementBridge.IsVisibleInHierarchy(row.BackingElement))
            {
                sb.Append(ProvisionalComment);
                return;
            }
        // Read every row's progression arrow up front: it decides whether the column exists at all,
        // and reading it once here keeps the header and the cells from disagreeing.
        var changes = new string[item.Rows.Count];
        bool anyChange = false;
        bool anyTracked = false;
        for (int i = 0; i < item.Rows.Count; i++)
        {
            try
            {
                changes[i] = item.Rows[i].GetChange();
            }
            catch (Exception ex)
            {
                Log.Warn($"[AccessibilityServer] Attribute change unreadable, marker rendered: {ex.Message}");
                changes[i] = Degrade.Unreadable;
            }
            if (changes[i].Length > 0) anyChange = true;
            // The game reports progression only for players at our own club, and those are exactly
            // the rows it builds from the clickable template.
            if (item.Rows[i].IsClickable) anyTracked = true;
        }

        // Show the column whenever progression is KNOWABLE for this player, not merely when
        // something moved. Gating on movement alone made a quiet month look identical to a player
        // we get no development data on at all — both rendered a bare one-column table — so a
        // reader could not tell "this attribute held steady" from "we do not track this player".
        // Present-but-blank now means the former and an absent column means the latter. The
        // anyChange half stays as the data-driven safety net, so a row that somehow shows an arrow
        // outside the clickable template still gets a column to show it in.
        bool showChangeColumn = anyTracked || anyChange;

        string tid = LiveValues.AllocateId(ServerHandles.IdentityPath(item.List) + "#attrtable");
        sb.Append("<table id=\"").Append(tid).Append("\"><thead><tr>")
          .Append("<th scope=\"col\" aria-sort=\"none\"><button type=\"button\" onclick=\"fmattrs('")
          .Append(tid).Append("')\">Stat</button></th>");
        if (showChangeColumn) sb.Append("<th scope=\"col\">Change this month</th>");
        sb.Append("</tr></thead><tbody>");
        for (int i = 0; i < item.Rows.Count; i++)
        {
            var row = item.Rows[i];
            // Degrade per row: an unreadable name or value renders an honest marker row rather
            // than dropping the attribute or aborting the section.
            string stat;
            try
            {
                stat = row.GetLabel();
            }
            catch (Exception ex)
            {
                Log.Warn($"[AccessibilityServer] Attribute row unreadable, marker rendered: {ex.Message}");
                sb.Append("<tr><td");
                if (showChangeColumn) sb.Append(" colspan=\"2\"");
                sb.Append(">").Append(ServerText.Escape(Degrade.Unreadable)).Append("</td></tr>");
                continue;
            }

            string key = ServerHandles.KeyFor(row);
            string sid = LiveValues.AllocateId(key + "#stat");
            LiveValues.Track(sid, row);

            sb.Append("<tr><td id=\"").Append(sid).Append("\">")
              .Append(ServerText.EscapeText(stat)).Append("</td>");
            if (showChangeColumn)
            {
                // Tracked per row, including the rows showing nothing: an arrow that appears while
                // the report is open then patches in place instead of waiting for a full re-render,
                // and one that clears patches back to blank.
                string cid = LiveValues.AllocateId(key + "#change");
                LiveValues.Track(cid, new Adapters.AttributeChangeCell(row));
                sb.Append("<td><span id=\"").Append(cid).Append("\">")
                  .Append(ServerText.EscapeText(changes[i])).Append("</span></td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    // An attribute summary closing the attributes block (best, then worst): a small read-only table
    // under its own heading. Values are a live read from walk time; the section tables above carry
    // the tracked, in-place-updating copies, so these cells stay untracked snapshot text.
    private static void RenderAttributeSummary(StringBuilder sb, ScreenWalker.AttributeSummaryItem item)
    {
        sb.Append("<h3>").Append(ServerText.Escape(item.Heading)).Append("</h3>")
          .Append("<table><thead><tr><th scope=\"col\">Attribute</th><th scope=\"col\">Value</th></tr></thead><tbody>");
        foreach (var e in item.Entries)
            sb.Append("<tr><td>").Append(ServerText.EscapeText(e.Name)).Append("</td><td>")
              .Append(e.Value).Append("</td></tr>");
        sb.Append("</tbody></table>");
    }

    // The contract negotiation terms table (see ScreenWalker.RestructureContractNegotiation): one
    // row per term, the term name as the row header, the value control and the negotiability
    // padlock dropdown in labelled cells, and — when any row carries one — a Remove column for
    // the game's Delete buttons. Cell controls render through the normal role paths, relabelled
    // by their term so the padlock no longer shares the term's bare name ("Salary negotiability"
    // beside "Salary").
    private static void RenderContractTerms(StringBuilder sb, ScreenWalker.ContractTermsItem item)
    {
        bool anyDelete = false;
        foreach (var r in item.Rows)
            if (r.Delete != null) { anyDelete = true; break; }

        sb.Append("<table><thead><tr><th scope=\"col\">Term</th><th scope=\"col\">Value</th><th scope=\"col\">Negotiability</th>");
        if (anyDelete) sb.Append("<th scope=\"col\">Remove</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var r in item.Rows)
        {
            string term = r.Term ?? Degrade.Value("contract term name unreadable");
            sb.Append("<tr><th scope=\"row\">").Append(ServerText.Escape(term)).Append("</th><td>");
            if (r.Value != null) RenderContractTermValue(sb, r.Value, term);
            // Extras go through the same value path, not the raw one: a row with two value controls
            // (a per-appearance fee's threshold AND its money amount) puts the second in here, and
            // through the raw path that money control lost its preset value list — the blind stepper
            // this whole table exists to replace.
            foreach (var ex in r.Extras) RenderContractTermValue(sb, ex, term);
            sb.Append("</td><td>");
            if (r.Lock != null) RenderNode(sb, r.Lock, term + " negotiability");
            sb.Append("</td>");
            if (anyDelete)
            {
                sb.Append("<td>");
                if (r.Delete != null) RenderNode(sb, r.Delete);
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    // --- Responsibilities tables (see ScreenWalker.RestructureResponsibilities) ---

    // Column names are mod chrome; every cell value is the game's own text or its own live control.
    private const string ResponsibilityGroupColumn = "Group";
    private const string ResponsibilityColumn = "Responsibility";
    private const string ResponsibilityDescriptionColumn = "Description";
    private const string ResponsibilityAssignedColumn = "Assigned to";
    private const string ResponsibilityControlColumn = "Control";
    private const string AdviceSettingColumn = "Setting";
    private const string AdviceFrequencyColumn = "Frequency";
    // Shown only where the game offers no enabled control at all, so the player is never left
    // guessing whether a duty is theirs to move. Not a state word: it says what the row cannot do.
    private const string ResponsibilityLockedNote = "Cannot be changed";
    private const string GroupActionsHeading = "Group actions";
    private const string ResponsibilityTruncatedNote = "Some responsibility groups could not be read, list may be incomplete, ";
    private const string ResponsibilityUnreadableNote = "Responsibilities ";

    // One group's duties: the title heads each row, the description sits beside it, and the
    // assignee dropdown and the game's Take Control / Delegate pair keep their own handles so
    // every action stays exactly where it was. A duty with no assignable staff shows the game's
    // own explanation in place of the missing dropdown; a duty with both controls disabled is
    // marked as unchangeable.
    // The whole Delegation section as one table, swept across frames so every group is present
    // whether or not the game had it realised. A section with several groups gets a Group column
    // and gathers the per-group bulk buttons above the table, each prefixed with its group (four
    // identical "Delegate All to Recommended" buttons are indistinguishable once their headings
    // fold into a column). A single-group section keeps its heading and skips the column, which
    // would otherwise repeat one value down every row.
    private static void RenderResponsibilities(StringBuilder sb, ScreenWalker.ResponsibilitiesTableItem item)
    {
        if (item.LoadFailed)
        {
            sb.Append("<p>").Append(ServerText.Escape(ResponsibilityUnreadableNote))
              .Append(ServerText.Escape(Degrade.Unavailable)).Append("</p>");
            return;
        }
        var groups = item.Groups;
        if (groups == null || groups.Count == 0) return;

        bool multi = groups.Count > 1;
        RenderResponsibilityGroupActions(sb, item, groups, multi);

        sb.Append("<table><thead><tr>");
        if (multi) sb.Append("<th scope=\"col\">").Append(ResponsibilityGroupColumn).Append("</th>");
        sb.Append("<th scope=\"col\">").Append(ResponsibilityColumn)
          .Append("</th><th scope=\"col\">").Append(ResponsibilityDescriptionColumn)
          .Append("</th><th scope=\"col\">").Append(ResponsibilityAssignedColumn)
          .Append("</th><th scope=\"col\">").Append(ResponsibilityControlColumn)
          .Append("</th></tr></thead><tbody>");

        foreach (var g in groups)
            foreach (var d in g.Duties)
            {
                sb.Append("<tr>");
                if (multi)
                    sb.Append("<td>").Append(ServerText.Escape(g.Name ?? Degrade.Value("group name unreadable"))).Append("</td>");
                sb.Append("<th scope=\"row\">")
                  .Append(ServerText.Escape(d.Title ?? Degrade.Value("responsibility name unreadable")))
                  .Append("</th><td>").Append(ServerText.Escape(d.Description ?? "")).Append("</td><td>");

                if (d.HasAssign)
                    RenderResponsibilityControl(sb, item, g.Index, d.Index,
                        g.NativeIdentity, d.NativeIdentity,
                        Contexts.ResponsibilitiesContext.SlotAssign,
                        d.AssignText ?? Degrade.Value("responsibilities: assignee unreadable"),
                        d.AssignEnabled, asSelect: true);
                foreach (var line in d.NoStaff) sb.Append("<p>").Append(ServerText.Escape(line)).Append("</p>");

                sb.Append("</td><td>");
                if (d.HasTakeControl)
                    RenderResponsibilityControl(sb, item, g.Index, d.Index,
                        g.NativeIdentity, d.NativeIdentity,
                        Contexts.ResponsibilitiesContext.SlotTake, d.TakeControlLabel, d.TakeControlEnabled, asSelect: false);
                if (d.HasDelegate)
                    RenderResponsibilityControl(sb, item, g.Index, d.Index,
                        g.NativeIdentity, d.NativeIdentity,
                        Contexts.ResponsibilitiesContext.SlotDelegate, d.DelegateLabel, d.DelegateEnabled, asSelect: false);
                // Both controls present and refused is the game's own statement that neither action
                // is offered — a fact, not a deduction. A duty missing a control entirely is not
                // claimed as locked: absence is not refusal.
                if (g.ControlsStable && d.HasTakeControl && d.HasDelegate && !d.TakeControlEnabled && !d.DelegateEnabled)
                    sb.Append("<p>").Append(ServerText.Escape(ResponsibilityLockedNote)).Append("</p>");
                sb.Append("</td></tr>");
            }
        sb.Append("</tbody></table>");

        if (item.Truncated)
            sb.Append("<p>").Append(ServerText.Escape(ResponsibilityTruncatedNote))
              .Append(ServerText.Escape(Degrade.Unavailable)).Append("</p>");
    }

    private static void RenderResponsibilityGroupActions(
        StringBuilder sb, ScreenWalker.ResponsibilitiesTableItem item,
        System.Collections.Generic.List<Contexts.ResponsibilitiesContext.Group> groups, bool multi)
    {
        bool any = false;
        foreach (var g in groups) if (g.HasTakeAll || g.HasDelegateAll) { any = true; break; }
        if (!any) return;

        // With several groups the bulk actions are a table in their own right — group per row, the
        // two buttons in one Control cell, exactly the shape the duties table below uses. Read
        // sequentially the old flat list gave two same-looking buttons per group with nothing but
        // their own captions to separate one group's pair from the next.
        if (multi)
        {
            sb.Append("<h3>").Append(ServerText.Escape(GroupActionsHeading)).Append("</h3>");
            sb.Append("<table><thead><tr><th scope=\"col\">").Append(ResponsibilityGroupColumn)
              .Append("</th><th scope=\"col\">").Append(ResponsibilityControlColumn)
              .Append("</th></tr></thead><tbody>");
            foreach (var g in groups)
            {
                if (!g.HasTakeAll && !g.HasDelegateAll) continue;
                sb.Append("<tr><th scope=\"row\">")
                  .Append(ServerText.Escape(g.Name ?? Degrade.Value("group name unreadable")))
                  .Append("</th><td>");
                AppendGroupBulkButtons(sb, item, g);
                sb.Append("</td></tr>");
            }
            sb.Append("</tbody></table>");
            return;
        }

        // A single group needs no table: its own heading names it, and the two buttons follow.
        foreach (var g in groups)
        {
            if (g.Name != null) sb.Append("<h3>").Append(ServerText.Escape(g.Name)).Append("</h3>");
            AppendGroupBulkButtons(sb, item, g);
        }
    }

    // A group's Take-Control-All / Delegate-All pair. The captions are the game's own button text,
    // read live; a caption that could not be read degrades to a marker rather than rendering an
    // empty button the player cannot identify.
    private static void AppendGroupBulkButtons(
        StringBuilder sb, ScreenWalker.ResponsibilitiesTableItem item,
        Contexts.ResponsibilitiesContext.Group g)
    {
        if (g.HasTakeAll)
            RenderResponsibilityControl(sb, item, g.Index, -1,
                g.NativeIdentity, null,
                Contexts.ResponsibilitiesContext.SlotTakeAll,
                g.TakeAllLabel ?? Degrade.Value("responsibilities: bulk take-control label unreadable"),
                g.TakeAllEnabled, asSelect: false);
        if (g.HasDelegateAll)
            RenderResponsibilityControl(sb, item, g.Index, -1,
                g.NativeIdentity, null,
                Contexts.ResponsibilitiesContext.SlotDelegateAll,
                g.DelegateAllLabel ?? Degrade.Value("responsibilities: bulk delegate label unreadable"),
                g.DelegateAllEnabled, asSelect: false);
    }

    // One swept control. Disabled renders inert (no handle, announced unavailable); enabled
    // registers a handle keyed by group/duty/slot that re-realises the control at action time.
    private static void RenderResponsibilityControl(
        StringBuilder sb, ScreenWalker.ResponsibilitiesTableItem item,
        int group, int duty, string groupIdentity, string dutyIdentity,
        string slot, string text, bool enabled, bool asSelect)
    {
        string safe = ServerText.EscapeText(text ?? "");
        string key = "resp:" + group + ":" + duty + ":" + slot + "|" + groupIdentity + "|" + dutyIdentity;
        string id = enabled
            ? ServerHandles.RegisterKey(new ServerHandles.ResponsibilityRowHandle { List = item.List, Group = group, Duty = duty, Slot = slot,
                GroupIdentity = groupIdentity, DutyIdentity = dutyIdentity }, key)
            : LiveValues.AllocateId(key);
        if (!enabled)
        {
            if (asSelect)
                sb.Append("<select id=\"").Append(id).Append("\" disabled aria-label=\"").Append(ServerText.Escape(ResponsibilityAssignedColumn))
                  .Append("\"><option selected>").Append(safe).Append("</option></select>");
            else
                sb.Append("<p><button type=\"button\" id=\"").Append(id).Append("\" disabled>").Append(safe).Append("</button></p>");
            return;
        }

        if (asSelect)
            sb.Append("<select id=\"").Append(id).Append("\" data-dd=\"1\" aria-label=\"")
              .Append(ServerText.Escape(ResponsibilityAssignedColumn)).Append("\"><option selected>")
              .Append(safe).Append("</option></select>");
        else
            sb.Append("<p><button type=\"button\" id=\"").Append(id).Append("\" onclick=\"act('")
              .Append(id).Append("')\">").Append(safe).Append("</button></p>");
    }

    // The Overview tab's duty list for the selected staff member: read-only, so two columns.
    private static void RenderStaffDuties(StringBuilder sb, ScreenWalker.StaffDutiesTableItem item)
    {
        if (item.LoadFailed)
        {
            sb.Append("<p>").Append(ServerText.Escape(ResponsibilityUnreadableNote))
              .Append(ServerText.Escape(Degrade.Unavailable)).Append("</p>");
            return;
        }
        var duties = item.Duties;
        if (duties == null || duties.Count == 0) return;

        sb.Append("<table><thead><tr><th scope=\"col\">").Append(ResponsibilityColumn)
          .Append("</th><th scope=\"col\">").Append(ResponsibilityDescriptionColumn)
          .Append("</th></tr></thead><tbody>");
        foreach (var d in duties)
            sb.Append("<tr><th scope=\"row\">")
              .Append(ServerText.Escape(d.Title ?? Degrade.Value("responsibility name unreadable")))
              .Append("</th><td>").Append(ServerText.Escape(d.Description ?? "")).Append("</td></tr>");
        sb.Append("</tbody></table>");

        if (item.Truncated)
            sb.Append("<p>").Append(ServerText.Escape(ResponsibilityTruncatedNote))
              .Append(ServerText.Escape(Degrade.Unavailable)).Append("</p>");
    }

    // One Advice and Reports group: each setting's own dropdown, relabelled by the column it sits
    // in so the control does not repeat the row header it already has.
    private static void RenderAdviceTable(StringBuilder sb, ScreenWalker.AdviceTableItem item)
    {
        string valueColumn = item.ValueIsFrequency ? AdviceFrequencyColumn : ResponsibilityAssignedColumn;
        sb.Append("<table><thead><tr><th scope=\"col\">").Append(AdviceSettingColumn)
          .Append("</th><th scope=\"col\">").Append(ResponsibilityDescriptionColumn)
          .Append("</th><th scope=\"col\">").Append(valueColumn)
          .Append("</th></tr></thead><tbody>");
        foreach (var r in item.Rows)
        {
            sb.Append("<tr><th scope=\"row\">")
              .Append(ServerText.Escape(r.Title ?? Degrade.Value("setting name unreadable")))
              .Append("</th><td>").Append(ServerText.Escape(r.Description ?? "")).Append("</td><td>");
            if (r.Value != null) RenderNode(sb, r.Value, valueColumn);
            foreach (var ex in r.Extras) RenderNode(sb, ex);
            sb.Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    private const string ConditionStatisticLabel = "Statistic";
    private const string ConditionTargetLabel = "Target";

    // The conditions attached to promises, as their own table: which promise each condition
    // applies to (row header), the game's condition sentence, then the statistic and target
    // dropdowns under their short role names.
    private static void RenderContractConditions(StringBuilder sb, ScreenWalker.ContractConditionsItem item)
    {
        bool anyDelete = false;
        foreach (var r in item.Rows)
            if (r.Delete != null) { anyDelete = true; break; }

        sb.Append("<table><thead><tr><th scope=\"col\">Applies To</th><th scope=\"col\">Condition</th><th scope=\"col\">Statistic</th><th scope=\"col\">Target</th>");
        if (anyDelete) sb.Append("<th scope=\"col\">Remove</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var r in item.Rows)
        {
            string parent = r.ParentTerm ?? Degrade.Value("condition parent promise unreadable");
            // An unset condition (the game's "Set Target" state) has no sentence yet and no
            // target dropdown — an empty Condition cell is the truth, not a gap. A row that DOES
            // carry a target dropdown with no readable sentence is a real read failure.
            bool hasTarget = false;
            foreach (var ex in r.Extras)
                if (ServerText.SafeRole(ex) == "dropdown" && ex is Adapters.DropdownAdapter) { hasTarget = true; break; }
            string sentence = r.Term ?? (hasTarget ? Degrade.Value("condition text unreadable") : "");
            sb.Append("<tr><th scope=\"row\">").Append(ServerText.Escape(parent)).Append("</th><td>")
              .Append(ServerText.Escape(sentence)).Append("</td><td>");
            if (r.Value != null) RenderConditionDropdown(sb, r.Value, ConditionStatisticLabel);
            sb.Append("</td><td>");
            foreach (var ex in r.Extras)
            {
                if (ServerText.SafeRole(ex) == "dropdown" && ex is Adapters.DropdownAdapter)
                    RenderConditionDropdown(sb, ex, ConditionTargetLabel);
                else
                    RenderNode(sb, ex);
            }
            sb.Append("</td>");
            if (anyDelete)
            {
                sb.Append("<td>");
                if (r.Delete != null) RenderNode(sb, r.Delete);
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    // A condition row's dropdown, named by its role in the condition ("Statistic" picks the stat,
    // "Target" the required count) rather than the condition sentence both controls would
    // otherwise repeat as their folded field label. The option is the displayed selection read
    // live from the control's SelectionField (the generic state read doubles the value on this
    // template); a failed read falls back to the state. Not value-tracked: the tracked state
    // would overwrite the clean option text.
    private static void RenderConditionDropdown(StringBuilder sb, IAccessibleElement el, string label)
    {
        string option = DropdownDisplayedText(el) ?? ServerText.State(el);
        if (ServerText.IsDisabled(el))
        {
            string did = LiveValues.RegisterReadOnly(el);
            sb.Append("<select id=\"").Append(did).Append("\" disabled aria-label=\"")
              .Append(ServerText.EscapeText(label)).Append("\"><option selected>")
              .Append(ServerText.EscapeText(option)).Append("</option></select>");
            return;
        }
        string id = ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
        sb.Append("<select id=\"").Append(id).Append("\" data-dd=\"1\" aria-label=\"")
          .Append(ServerText.EscapeText(label)).Append("\"><option selected>")
          .Append(ServerText.EscapeText(option)).Append("</option></select>");
    }

    // The selection text a dropdown currently displays, read live from its SelectionField.
    private static string DropdownDisplayedText(IAccessibleElement el)
    {
        var backing = (el as Adapters.PluginAdapterBase)?.BackingElement;
        if (backing == null) return null;
        var field = ScreenWalker.FindDescendantNamed(backing, "SelectionField");
        var raw = field != null ? ScreenWalker.ReadFirstText(field) : null;
        if (string.IsNullOrEmpty(raw)) return null;
        var cleaned = TextFilters.CleanForDisplay(raw);
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    // The money-combobox setter, shared by the contract/transfer terms tables and any bare
    // labelled combobox row (the Set Transfer Status asking price): an openable select carrying
    // the game's own preset-value list (the internal dropdown, resolved by StopHandle — the
    // game's own commit path) plus the +/- step buttons for fine adjustment.
    private static void RenderComboboxSetter(StringBuilder sb, IAccessibleElement el,
        Adapters.ComboboxAdapter cb, string label)
    {
        string value;
        try { value = cb.GetCurrentValue(); }
        catch (Exception ex)
        {
            Log.Warn($"[AccessibilityServer] Combobox value read failed: {ex.Message}");
            value = null;
        }
        value ??= Degrade.Value("combobox value unreadable");

        if (ServerText.IsDisabled(el))
        {
            string did = LiveValues.RegisterReadOnly(el);
            sb.Append("<select id=\"").Append(did).Append("\" disabled aria-label=\"")
              .Append(ServerText.EscapeText(label)).Append("\"><option selected>")
              .Append(ServerText.EscapeText(value)).Append("</option></select> ")
              .Append("<button type=\"button\" disabled>decrease</button> ")
              .Append("<button type=\"button\" disabled>increase</button>");
            return;
        }
        string id = ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
        sb.Append("<select id=\"").Append(id).Append("\" data-dd=\"1\" aria-label=\"")
          .Append(ServerText.EscapeText(label)).Append("\"><option selected>")
          .Append(ServerText.EscapeText(value)).Append("</option></select> ");
        RenderComboboxStepButtons(sb, id, cb, true);
    }

    private static void RenderComboboxStepButtons(StringBuilder sb, string id, ComboboxAdapter combo, bool separateValue)
        => RenderComboboxStepButtons(sb, id, combo.IsDecrementEnabled(), combo.IsIncrementEnabled(), separateValue);

    private static void RenderComboboxStepButtons(StringBuilder sb, string id, bool canDecrement, bool canIncrement, bool separateValue)
    {
        for (int direction = -1; direction <= 1; direction += 2)
        {
            bool available = direction > 0 ? canIncrement : canDecrement;
            string suffix = direction > 0 ? "_up" : separateValue ? "_dn" : "";
            sb.Append("<button type=\"button\" id=\"").Append(id).Append(suffix).Append('"');
            if (available) sb.Append(" onclick=\"step('").Append(id).Append("',").Append(direction).Append(")\"");
            else sb.Append(" disabled");
            sb.Append('>').Append(direction > 0 ? "increase" : "decrease").Append("</button>");
            if (direction < 0) sb.Append(' ');
        }
    }

    // A term's value control. A choice dropdown is relabelled by its term — and because a
    // value-named dropdown (its accessible name IS its displayed value) can report an empty or
    // index-only state, the displayed value is folded into the option so it always reaches the
    // page. Track that SAME composition through live values: native Undo can
    // finish after the action's body read without changing this table's shape.
    // A state-only update would erase the value or retain a stale selection. A money
    // combobox (FMCombobox) is the game's own combobox — an internal preset-value dropdown plus
    // +/- steppers — so it renders as both: an openable select (the internal dropdown's choice
    // list, resolved by StopHandle, is the game's own commit path) and the step buttons for fine
    // adjustment. Any other control keeps its normal rendering.
    private static void RenderContractTermValue(StringBuilder sb, IAccessibleElement el, string term)
    {
        if (ServerText.SafeRole(el) == "combobox" && el is Adapters.ComboboxAdapter cb)
        {
            bool hasChoices = false;
            try { hasChoices = cb.HasDropdownChoices(); }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Combobox choice probe failed, stepper kept: {ex.Message}"); }
            if (hasChoices)
            {
                RenderComboboxSetter(sb, el, cb, term);
                return;
            }
        }

        if (ServerText.SafeRole(el) == "dropdown" && el is Adapters.DropdownAdapter dd)
        {
            string state = ServerText.State(el);
            long stateReadAt = NativeValueRevision.Next();
            string option = state;
            bool valueNamed = false;
            try
            {
                valueNamed = dd.IsValueNamed();
                if (valueNamed)
                    option = ContractTermsText.ComposeValueOption(ServerText.Label(el), state);
            }
            catch (Exception ex) { Log.Warn($"[AccessibilityServer] Term value composition failed, state kept: {ex.Message}"); }

            if (ServerText.IsDisabled(el))
            {
                string did = LiveValues.AllocateId(ServerHandles.KeyFor(el));
                LiveValues.TrackContractValue(did, el, valueNamed);
                sb.Append("<select id=\"").Append(did).Append("\" disabled aria-label=\"")
                  .Append(ServerText.EscapeText(term)).Append('"');
                sb.Append(" data-fm-value-at=\"").Append(stateReadAt).Append('"');
                sb.Append("><option selected>")
                  .Append(ServerText.EscapeText(option)).Append("</option></select>");
                return;
            }
            string id = ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
            LiveValues.TrackContractValue(id, el, valueNamed);
            sb.Append("<select id=\"").Append(id).Append("\" data-dd=\"1\" aria-label=\"")
              .Append(ServerText.EscapeText(term)).Append('"');
            sb.Append(" data-fm-value-at=\"").Append(stateReadAt).Append('"');
            sb.Append("><option selected>")
              .Append(ServerText.EscapeText(option)).Append("</option></select>");
            return;
        }
        RenderNode(sb, el);
    }

    // A folded "label: value" line (the contract wage-budget footer). The value keeps its own id
    // on the values stream, so the number updates in place as the offer changes.
    private static void RenderLabelValue(StringBuilder sb, ScreenWalker.LabelValueItem item)
    {
        // A value that does something (the transfer offer's Club / Nationality / Positions rows are
        // buttons opening those screens) keeps its control on the same line as its label, so folding
        // the pair never costs the player the action. Everything else — including a disabled button,
        // which has no action to lose — collapses into a live-tracked span.
        if (ServerText.SafeRole(item.Value) == "button" && !ServerText.IsDisabled(item.Value))
        {
            sb.Append("<p>").Append(ServerText.Escape(item.Label)).Append(": ");
            AppendButton(sb, item.Value, null);
            sb.Append("</p>");
            return;
        }
        string id = LiveValues.RegisterReadOnly(item.Value);
        sb.Append("<p>").Append(ServerText.Escape(item.Label)).Append(": <span id=\"").Append(id).Append("\">")
          .Append(ServerText.Escape(DisplayText.Of(item.Value))).Append("</span></p>");
    }

    // The button element itself, with no block wrapper, so a caller can place it inside a line it is
    // already building (a folded "label: value" row whose value is a button). RenderNode's own button
    // case wraps this in the paragraph it has always emitted.
    //
    // A disabled (greyed-out) button must not be presented as actionable: the native disabled
    // attribute blocks activation and the screen reader announces it as unavailable. It still gets a
    // read-only id (stable across the enabled<->disabled transition) so its label updates live and
    // focus survives a refresh. An enabled button registers an action handle and an onclick.
    private static void AppendButton(StringBuilder sb, IAccessibleElement el, string labelOverride, bool? pressed = null)
    {
        bool disabled = ServerText.IsDisabled(el);
        string id = disabled
            ? LiveValues.RegisterReadOnly(el)
            : ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
        if (!disabled) LiveValues.Track(id, el);
        // A news-event (email) button opens the reading pane: route it through FM.messages so
        // focus lands on the message body. Every other button keeps the plain act() path.
        bool isMessage = !disabled && ServerText.BackingNameOr(el, "") == "NewsItemButton";
        sb.Append("<button type=\"button\" id=\"").Append(id).Append("\"");
        if (pressed.HasValue) sb.Append(" aria-pressed=\"").Append(pressed.Value ? "true" : "false").Append('"');
        if (el is MessageViewSurface.CloseAdapter && MessageViewSurface.CurrentMessageId is int messageId)
            sb.Append(" data-fm-message-view-id=\"").Append(messageId).Append('"');
        if(el is Adapters.ButtonAdapter answer&&SpeakToRenderer.IsAnswer(answer.BackingElement))
            sb.Append(" data-fm-conversation-answer");
        if (el is Adapters.ButtonAdapter historyButton
            && ElementBridge.IsInPanel(historyButton.BackingElement, "OverviewTitleBar"))
        {
            var direction = historyButton.BackingElement.name switch
            { "BackButton" => "back", "ForwardButton" => "forward", _ => null };
            if (direction != null) sb.Append(" data-fm-history=\"").Append(direction).Append('"');
        }
        if (!disabled && (el is FilterPopupSurface.CloseAdapter
            || el is Adapters.ButtonAdapter close
            && Adapters.NativeButtonCommand.IsCloseControl(close.BackingElement)))
            sb.Append(" data-dialog-close");
        if (disabled) sb.Append(" disabled");
        else if (isMessage) sb.Append(" onclick=\"fmmsg('").Append(id).Append("')\"");
        else sb.Append(" onclick=\"act('").Append(id).Append("')\"");
        if (!disabled) AppendContextMenuAttr(sb, el.HasContextMenu);
        // A button that expands or collapses something (a popup checkbox group's header) says which
        // it will do next.
        if (HasStateToken(el, "expanded")) sb.Append(" aria-expanded=\"true\"");
        else if (HasStateToken(el, "collapsed")) sb.Append(" aria-expanded=\"false\"");
        // Mark only the Messages surface's native Unread filter. The client uses this structural,
        // untranslated identity to repeat that one filter action after a message is closed.
        if (!disabled && IsUnreadMessagesFilter(el))
        {
            sb.Append(" data-fm-msg-unread-filter");
            if (HasStateToken(el, "selected")) sb.Append(" data-fm-selected");
        }
        // The context menu's dismiss stop is every menu view's consistent first stop;
        // app.js (FM.contextmenu.focusFirst) lands screen-reader focus on it when a menu
        // opens, so reading always starts from the same place.
        if (el is Adapters.MenuDataEntryAdapter menuEntry && menuEntry.IsMenuDismiss)
            sb.Append(" data-fm-menu-first");
        sb.Append(">").Append(ServerText.Escape(labelOverride ?? ButtonDisplay(el))).Append("</button>");
    }

    internal static bool IsUnreadMessagesFilter(IAccessibleElement el)
        => IsMessagesFilter(el, "UnreadMessagesTutorial");

    private static bool IsMessagesFilter(IAccessibleElement el, string anchor)
    {
        var current = (el as Adapters.PluginAdapterBase)?.BackingElement;
        bool unreadOwner = false;
        bool inFilterGroup = false;
        // These are the game's stable template names; labels such as "Unread" are translated.
        for (int depth = 0; current != null && depth < 64; depth++, current = current.hierarchy.parent)
        {
            string name = current.name;
            string type;
            try { type = current.GetIl2CppType().Name; }
            catch { type = null; }
            if (name == anchor) unreadOwner = true;
            if (name is "TabButtonsGroup" or "TabButtons" || type == "TabButtonsGroup") inFilterGroup = true;
        }
        return unreadOwner && inFilterGroup;
    }

    // A button's display text WITH the "locked" state token (ButtonAdapter.GetState: a tile the
    // game padlocks), folded in the way DisplayText.Of folds "warning" and "selected". It lives
    // here rather than there because DisplayText is in the restart-only Navigation assembly; move
    // it in when that assembly next changes anyway. This is the single read for BOTH the rendered
    // label and the value channel's snapshot (LiveValues.ReadValue) - the two must see the same
    // text, or a padlock the game populates a beat after the body render never diffs and the page
    // shows the tile unlocked until a manual refresh.
    internal static string ButtonDisplay(IAccessibleElement el)
    {
        string label = DisplayText.Of(el);
        if (HasStateToken(el, "locked")) label += ", locked";
        // A popup checkbox group's header carries how many of its rows are chosen, which the
        // collapsed group otherwise hides (the expanded/collapsed half is aria-expanded).
        if (el is FilterPopupSurface.GroupAdapter group && group.ChosenStatus() is { } chosen) label += ", " + chosen;
        return label;
    }

    private static bool HasStateToken(IAccessibleElement el, string token)
    {
        string state = ServerText.State(el);
        if (string.IsNullOrEmpty(state)) return false;
        foreach (var part in state.Split(','))
            if (part.Trim() == token) return true;
        return false;
    }

    // labelOverride supplies an accessible name for a control the game renders icon-only (so the
    // adapter would otherwise fall back to an unrelated sibling text). Used by the fixtures Options
    // cells; null everywhere else, leaving the normal label path untouched.
    private static void RenderNode(StringBuilder sb, IAccessibleElement el, string labelOverride = null,
        string headingTag = null, string headingId = null)
    {
        // A streamed-table load runs across frames after the initial walk.
        // FM27 can hide an answer/skip control in that interval while leaving
        // the child attached and individually visible. Revalidate its native
        // ancestry before rendering, then use the existing provisional retry
        // to pick up any replacement content absent from the old walk.
        if (el is Adapters.PluginAdapterBase native
            && (native.BackingElement.panel == null
                || !ElementBridge.IsVisibleInHierarchy(native.BackingElement)))
        {
            sb.Append(ProvisionalComment);
            return;
        }
        if ((headingTag != null || ServerText.IsHeading(el)) && ServerText.SafeRole(el) != "button")
        {
            // One level below the content section's <h2>, so the outline stays well-formed
            // (section -> in-section heading). A profile heading rule nests one deeper per level
            // (3 -> h4, 4 -> h5, for a panel with three real tiers of grouping like the Data Hub's
            // set-piece insights); level 0-2 — including the generic title-text headings, which
            // carry no profile level — stays <h3>. Capped at h6, the deepest HTML has.
            string tag = headingTag ?? "h" + Math.Min(6, Math.Max(3, ServerText.HeadingLevel(el) + 1));
            // Rich native titles retain their complete informational heading.
            // Their actual record links remain ordinary controls after it.
            List<IAccessibleElement> headingParts = null;
            if (el is Adapters.PluginAdapterBase source
                && source.BackingElement.GetIl2CppType().Name == "SIText"
                && !MessageViewSurface.IsNarrativeText(source.BackingElement))
            {
                try { headingParts = Adapters.SITextLink.ReadParts(source.BackingElement); }
                catch (Exception ex)
                {
                    Log.Warn($"[AccessibilityServer] Heading links not ready; keeping complete title: {ex.Message}");
                }
            }
            string hid = LiveValues.RegisterReadOnly(el);
            sb.Append("<").Append(tag).Append(" id=\"").Append(headingId ?? hid).Append('"');
            // Section headings are existing programmatic arrival targets, not
            // Tab stops. Keep their focusability in the authoritative markup;
            // otherwise a neutral morph removes JS's tabindex and drops focus.
            if(headingId!=null)sb.Append(" tabindex=\"-1\"");
            sb.Append('>');
            if (headingId != null) sb.Append("<span id=\"").Append(hid).Append("\">");
            sb.Append(ServerText.Escape(DisplayText.Of(el)));
            if (headingId != null) sb.Append("</span>");
            sb.Append("</").Append(tag).Append('>');
            if (headingParts != null && headingParts.Exists(part => part.CanActivate))
            {
                sb.Append("<p>");
                foreach (var part in headingParts)
                    if (part.CanActivate) { AppendButton(sb, part, null); sb.Append(' '); }
                sb.Append("</p>");
            }
            return;
        }

        // Custom features own their own markup; see Server/Features/. First match wins, then
        // fall through to the generic role rendering below.
        foreach (var renderer in FeatureRenderers)
            if (renderer.CanRender(el)) { renderer.Render(sb, el); return; }

        string label = ServerText.Label(el);
        string state = ServerText.State(el);
        long stateReadAt = NativeValueRevision.Next();

        switch (ServerText.SafeRole(el))
        {
            case "button":
            {
                // The Data Hub is built almost entirely from analysis tiles whose whole content —
                // the stat line, the verdict, the written analysis — is a description the speech
                // surface holds back behind Alt+I so a readout is not flooded. On the page there is
                // nothing to flood: skipping a paragraph costs one keystroke, and without it the
                // analysis tabs render as a column of tile names with no readable content at all —
                // three of them named "Defending - Defenders", each about a different player, and
                // no way to tell which is which. Print it, and drop the Alt+I pointer with it.
                string held = HeldBackDescription(el);
                sb.Append("<p>");
                AppendButton(sb, el, held == null ? labelOverride : (labelOverride ?? PrimaryLabelOf(el)));
                sb.Append("</p>");
                if (held != null)
                    sb.Append("<p>").Append(ServerText.Escape(held)).Append("</p>");
                break;
            }

            case "checkbox":
            case "toggle":
            case "radio button":
            {
                // A toggle the game has greyed out ("Only sell transfer listed players" while
                // "Reject all offers" is picked) renders genuinely disabled, matching the disabled
                // treatment dropdowns already get. Rendered live it offered a click that silently
                // did nothing, which reads to the player as a setting they successfully changed.
                bool off = ServerText.IsDisabled(el);
                string id = off
                    ? LiveValues.RegisterReadOnly(el)
                    : ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
                LiveValues.Track(id, el);
                string inputType = ServerText.SafeRole(el) == "radio button" ? "radio" : "checkbox";
                string nameAttr = inputType == "radio" ? " name=\"fm-radio\"" : "";
                bool on = state == "checked" || state == "on" || state == "selected";
                sb.Append("<p><label><input type=\"").Append(inputType).Append("\"").Append(nameAttr)
                  .Append(" id=\"").Append(id).Append("\"")
                  .Append(" data-fm-value-at=\"").Append(stateReadAt).Append('"')
                  .Append(on ? " checked" : "");
                if (off) sb.Append(" disabled");
                else sb.Append(" onclick=\"act('").Append(id).Append("')\"");
                sb.Append("> ")
                  .Append(ServerText.Escape(labelOverride ?? DisplayText.Of(el))).Append("</label></p>");
                break;
            }

            case "search box":
            case "text field":
            {
                // The box's accessible name is its row's field label ("First Name") when it has
                // one, folded in as aria-label so the label no longer reads as a separate stray
                // stop above the input. A box with no row label gets no aria-label (rather than
                // echoing the game's "Box" element name or its own value). The value is read
                // straight from the field so an empty box renders an empty value.
                string id = ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
                var tf = el as Adapters.TextFieldAdapter;
                string value = tf != null ? tf.GetCurrentText() : "";
                // A profile rename names a box the game leaves unlabelled — the create-schedule
                // description field, whose only text is its own placeholder. Same seam dropdowns
                // use for their aria-label, so one declarative rule still fixes both surfaces.
                string fieldLabel = ServerText.RenameOr(el, tf?.FieldLabel());
                bool password = tf?.IsPassword == true;
                if (password && string.IsNullOrEmpty(fieldLabel)) fieldLabel = "Password";
                sb.Append("<p><input type=\"").Append(password ? "password" : "text").Append("\" id=\"").Append(id).Append("\"");
                if (tf != null && MessageSearchReturn.IsQueryField(tf.BackingElement))
                {
                    sb.Append(" data-fm-message-query");
                    var openerId = MessageSearchReturn.QueryOpenerId(tf.BackingElement);
                    if (openerId != null) sb.Append(" data-fm-message-query-opener=\"").Append(openerId).Append('"');
                }
                if (!string.IsNullOrEmpty(fieldLabel))
                    sb.Append(" aria-label=\"").Append(ServerText.EscapeText(fieldLabel)).Append("\"");
                sb.Append(" value=\"").Append(ServerText.EscapeText(value))
                  .Append("\" onchange=\"settext('").Append(id).Append("',this.value)\"></p>");
                break;
            }

            case "slider":
            {
                string id = ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
                LiveValues.Track(id,el);
                var (val, low, high, step) = ServerText.SliderRange(el);
                sb.Append("<p><label>").Append(ServerText.EscapeText(label))
                  .Append(" <input type=\"range\" id=\"").Append(id)
                  .Append("\" min=\"").Append(ServerText.Num(low))
                  .Append("\" max=\"").Append(ServerText.Num(high))
                  .Append("\" step=\"").Append(step > 0 ? ServerText.Num(step) : "any")
                  .Append("\" value=\"").Append(ServerText.Num(val)).Append("\"");
                // The row's value label ("0.75x") is the slider's real meaning; the raw position
                // is just an index into the presets, so the screen reader announces the label.
                var valueText = ServerText.SliderValueText(el);
                if (valueText != null)
                    sb.Append(" aria-valuetext=\"").Append(ServerText.EscapeText(valueText)).Append("\"");
                sb.Append(" onchange=\"setvalue('").Append(id).Append("',this.value)\"");
                // A slider the game has zeroed and disabled (Camera Height under a fixed camera)
                // renders as genuinely disabled, not as a usable 0-to-0 range.
                if (ServerText.IsDisabled(el)) sb.Append(" disabled");
                sb.Append("></label></p>");
                break;
            }

            case "combobox":
            {
                // A combobox whose internal dropdown carries preset choices gets the same
                // setter widget the contract/transfer terms tables use (an openable select
                // over the game's own value list plus the steppers) — the Set Transfer Status
                // asking price is this shape as a bare labelled row. Without choices it stays
                // the name + value + stepper reading.
                if (el is Adapters.ComboboxAdapter cbx)
                {
                    bool cbxChoices = false;
                    try { cbxChoices = cbx.HasDropdownChoices(); }
                    catch (Exception ex) { Log.Warn($"[AccessibilityServer] Combobox choice probe failed, stepper kept: {ex.Message}"); }
                    if (cbxChoices)
                    {
                        sb.Append("<p>");
                        RenderComboboxSetter(sb, el, cbx, DisplayText.Of(el));
                        sb.Append("</p>");
                        break;
                    }
                }
                bool cbDisabled = ServerText.IsDisabled(el);
                string id = cbDisabled
                    ? LiveValues.RegisterReadOnly(el)
                    : ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
                sb.Append("<p>").Append(ServerText.Escape(DisplayText.Of(el))).Append(" combobox ");
                // The live value follows the name and role, mirroring the speech announcement
                // ("Place Of Birth, combobox, London"); empty for a value-named combobox.
                string cbValue = ServerText.State(el);
                if (!string.IsNullOrEmpty(cbValue))
                    sb.Append(ServerText.EscapeText(cbValue)).Append(" ");
                if (cbDisabled)
                {
                    sb.Append("<button type=\"button\" disabled>decrease</button> ")
                      .Append("<button type=\"button\" disabled>increase</button></p>");
                    break;
                }
                if (el is Adapters.ComboboxAdapter adjustable)
                    RenderComboboxStepButtons(sb, id, adjustable, false);
                sb.Append("</p>");
                break;
            }

            case "dropdown" when el is Adapters.DropdownAdapter:
            {
                // A real select: C quick-nav finds it and the screen reader treats it
                // as a combo box. It holds only the current value until focused, when
                // the client populates the live options from /dropdown/open.
                //
                // Mirror the speech surface ("Right Back, dropdown, 1 of 7"). A native select
                // speaks name then role ("combo box") then value, in that order, so to read
                // value-role-count the value goes in the accessible name and the count in the
                // value slot. The name carries the rename when one applies (as the speech label
                // does), else the live value; it is set once here and never mutated by browsing,
                // so the announced value can never go stale mid-session.
                string ddName = labelOverride ?? ServerText.RenameOr(el, null) ?? label;
                // A disabled (greyed-out) dropdown must not be openable: drop data-dd and the action
                // handle and emit the native disabled attribute, so the screen reader announces it as
                // unavailable and the client never tries to open the game popup (which would NRE on a
                // disabled menu). A read-only id keeps its value live and focus stable across refresh.
                if (ServerText.IsDisabled(el))
                {
                    string did = LiveValues.RegisterReadOnly(el);
                    sb.Append("<p><select id=\"").Append(did)
                      .Append("\" data-fm-value-at=\"").Append(stateReadAt)
                      .Append("\" disabled aria-label=\"").Append(ServerText.EscapeText(ddName)).Append("\"><option selected>")
                      .Append(ServerText.EscapeText(state))
                      .Append("</option></select></p>");
                    break;
                }
                string id = ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
                // Track the select's value so a selection that changes on the same screen (a commit
                // whose value lands a beat after the post-commit refresh, or a change made in the
                // game window) is pushed over the value channel and patched into the closed select,
                // rather than only appearing on a full re-render. ReadValue reads GetState for it.
                LiveValues.Track(id, el);
                sb.Append("<p><select id=\"").Append(id)
                  .Append("\" data-fm-value-at=\"").Append(stateReadAt).Append("\" data-dd=\"1\"");
                if (((Adapters.DropdownAdapter)el).IsCardOptions) sb.Append(" data-card-options=\"1\"");
                sb.Append(" aria-label=\"").Append(ServerText.EscapeText(ddName)).Append("\"><option selected>")
                  .Append(ServerText.EscapeText(state))
                  .Append("</option></select></p>");
                break;
            }

            case "searchable dropdown" when el is Adapters.SearchableDropdownAdapter:
            {
                // An ARIA editable combobox: one search box that filters the game's huge list
                // (debounced as-you-type, round-tripped through /searchdropdown/filter) into the
                // listbox below it. Focus stays in the box; Down/Up move aria-activedescendant
                // through the results and Enter commits. The handle id is the box's id, so
                // /searchdropdown/select acts through the same registered adapter and focus restores
                // to the box after a commit. The listbox starts empty and hidden; see FM.search.
                string name = ServerText.RenameOr(el, null) ?? label;
                // Disabled searchable dropdown: a plain disabled box, no data-sdd/handlers, so it
                // announces as unavailable and the client never opens the game search popup.
                if (ServerText.IsDisabled(el))
                {
                    string sdid = LiveValues.RegisterReadOnly(el);
                    sb.Append("<p><input id=\"").Append(sdid).Append("\" disabled aria-label=\"")
                      .Append(ServerText.EscapeText(name)).Append("\" value=\"")
                      .Append(ServerText.EscapeText(state)).Append("\"></p>");
                    break;
                }
                string id = ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
                // Value-tracked so a committed choice that lands after this render (the game
                // writes the box text a beat behind the commit) is pushed over the values
                // stream and patched into the input in place — no F5.
                LiveValues.Track(id, el);
                sb.Append("<p><input role=\"combobox\" id=\"").Append(id)
                  .Append("\" data-sdd=\"1\" aria-expanded=\"false\" aria-autocomplete=\"list\" aria-controls=\"")
                  .Append(id).Append("-lb\" aria-label=\"").Append(ServerText.EscapeText(name)).Append('"');
                // The committed choice ("London") renders as the box's value so the player can
                // review what they picked; an unset box stays empty for a clean type-to-search.
                if (!string.IsNullOrEmpty(state))
                    sb.Append(" value=\"").Append(ServerText.EscapeText(state)).Append('"');
                sb.Append(" oninput=\"fmsearch(this)\">")
                  .Append("<ul role=\"listbox\" id=\"").Append(id).Append("-lb\" data-sdd-lb hidden></ul></p>");
                break;
            }

            case "number field":
            {
                // The adapter label, and so any profile rename, the same as every other role here.
                // The old backing-element name bypassed both and, on a template-named box, left the
                // generic "Number field" as the only name the player ever heard.
                string fieldLabel = PrimaryLabelOf(el);
                if (string.IsNullOrEmpty(fieldLabel)) fieldLabel = ServerText.BackingNameOr(el, "Number field");

                // An int box is writable, so the companion gets a real spin box rather than an
                // echo the player can read but never change. min/max carry the permitted range,
                // which the screen reader announces and the browser enforces before the round
                // trip.
                if (el is Adapters.IntFieldAdapter nf)
                {
                    // The vacation day count is greyed until its radio option is picked. Disabled,
                    // not read-only: "disabled" is what the screen reader must say, because the box
                    // is not permanently uneditable — the player can enable it, and needs to know
                    // that is what stands between them and typing in it.
                    bool off = ServerText.IsDisabled(el);
                    var (val, min, max) = nf.GetRange();
                    string nid = off
                        ? LiveValues.RegisterReadOnly(el)
                        : ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
                    sb.Append("<p><label>").Append(ServerText.Escape(fieldLabel))
                      .Append(" <input type=\"number\" id=\"").Append(nid).Append('"');
                    if (min.HasValue) sb.Append(" min=\"").Append(min.Value).Append('"');
                    if (max.HasValue) sb.Append(" max=\"").Append(max.Value).Append('"');
                    sb.Append(" value=\"").Append(val).Append('"');
                    if (off) sb.Append(" disabled");
                    // The value posts on change (blur/Enter), not per keystroke, so typing "12" is
                    // one write of 12 rather than a write of 1 the game would take on its own.
                    else sb.Append(" onchange=\"setvalue('").Append(nid).Append("',this.value)\"");
                    sb.Append("></label></p>");
                    break;
                }

                sb.Append("<p><label>").Append(ServerText.Escape(fieldLabel))
                  // A float box: no numeric accessor, so a read-only echo of the state. type="text",
                  // not type="number", because the state can carry the value AND its permitted range
                  // ("1, range 1 to 999") and a number input silently discards any value it cannot
                  // parse — the field then read as empty, a lie about a box plainly holding a number.
                  .Append(" <input type=\"text\" inputmode=\"numeric\" value=\"")
                  .Append(ServerText.EscapeText(state)).Append("\" readonly></label></p>");
                break;
            }

            case "progress bar":
            {
                string id = LiveValues.RegisterReadOnly(el);
                // ARIA progressbar does not take its accessible name from its
                // contents. Retain the original markup/value contract and give
                // it the same native field caption the adapter already reads.
                string caption = ServerText.Clean(ServerText.RenameOr(el, label));
                sb.Append("<p><span id=\"").Append(id).Append("\" role=\"progressbar\" aria-label=\"")
                  .Append(ServerText.EscapeText(string.IsNullOrWhiteSpace(caption) ? "Progress" : caption))
                  .Append("\" aria-valuetext=\"")
                  .Append(ServerText.EscapeText(state)).Append("\">")
                  .Append(ServerText.Escape(DisplayText.Of(el))).Append("</span></p>");
                break;
            }

            default:
            {
                // dropdowns, star ratings, plain text — read-only announcement until
                // option selection (modal popups) is solved.
                string id = LiveValues.RegisterReadOnly(el);
                sb.Append("<p id=\"").Append(id).Append("\"");
                // The opened email's body is where focus lands when a message is opened (the body,
                // not the subject, under the screen-reader cursor). Mark it with a stable attribute
                // and make it focusable; app.js (FM.messages) aims focus here by that attribute,
                // while the id stays identity-allocated from the shared pool.
                if (ServerText.BackingNameOr(el, "") == "MessageBodyText")
                    sb.Append(" tabindex=\"-1\" data-fm-msg-body");
                else if(el is Adapters.PluginAdapterBase question&&SpeakToRenderer.IsQuestion(question.BackingElement))
                    sb.Append(" tabindex=\"-1\" data-fm-conversation-question");
                sb.Append(">").Append(ServerText.Escape(DisplayText.Of(el))).Append("</p>");
                break;
            }
        }
    }

    // The single consolidated scoreboard stop, rendered in the scorebar's place. Built
    // fresh from the engine and kept live by the SSE 'score' event (which targets this
    // id). Silent: read on demand, not a live region. Empty when the engine has no score
    // (between matches), which the screen reader skips.
    private static void RenderScoreboard(StringBuilder sb, ScreenWalker.ScoreboardItem item)
    {
        if (_suppressWalkedScoreboard) return;
        sb.Append("<p id=\"fmscore\">");
        if (!string.IsNullOrEmpty(item.Line)) sb.Append(ServerText.EscapeText(item.Line));
        sb.Append("</p>");
    }

    // The open action of a rich tile. The walker keeps its readonly title and
    // content separately; this ordinary button never carries heading semantics.
    private static void RenderTileActivator(StringBuilder sb, ScreenWalker.TileActivatorItem item)
    {
        if (item.TileButton.panel == null || !ElementBridge.IsVisibleInHierarchy(item.TileButton))
        {
            sb.Append(ProvisionalComment);
            return;
        }
        var tile = new Adapters.ButtonAdapter(item.TileButton);
        if (ServerText.IsDisabled(tile))
        {
            // Its separate informational heading and native contents remain.
            // A fixed card wrapper does not supply an available open action.
            return;
        }
        string bid = ServerHandles.Register(new ServerHandles.StopHandle { El = tile }, tile);
        sb.Append("<p><button type=\"button\" id=\"").Append(bid)
          .Append("\" onclick=\"act('").Append(bid).Append("')\">")
          .Append(ServerText.EscapeText(item.Title)).Append("</button></p>");
    }

    // The match header's stats-tile carousel (see ScreenWalker.TryEmitCarouselTile). The tile
    // title heads the widget as readonly <h3>; the game's open action is a
    // separate ordinary button. Stat rows render as a table whose value cells are live-tracked, so the values
    // channel patches them in place as the match runs; a report tile whose content is a grid
    // (MatchReportTiles) renders as a plain-text table of the same shape, re-read per body build
    // rather than patched. Each hidden carousel page renders as a
    // button driving the game's own page dot; the swap re-renders through the post-action
    // settle window.
    // The score bug's stats-widget toggle, with its open/closed state exposed: the widget it
    // reveals renders directly beneath, so aria-expanded is the truthful reading of what the
    // button will do next.
    private static void RenderStatsToggle(StringBuilder sb, ScreenWalker.StatsToggleItem item)
    {
        string bid = ServerHandles.Register(new ServerHandles.StopHandle { El = item.El }, item.El);
        sb.Append("<p><button type=\"button\" id=\"").Append(bid)
          .Append("\" aria-expanded=\"").Append(item.Expanded ? "true" : "false")
          .Append("\" onclick=\"act('").Append(bid).Append("')\">")
          .Append(ServerText.Escape(ServerText.Label(item.El)))
          .Append("</button></p>");
    }

    private static void RenderCarouselTile(StringBuilder sb, ScreenWalker.CarouselTileItem item)
    {
        // A real carousel presents as a tab strip: plain title heading, one tab per page (the
        // current page selected) BEFORE the content, and the full-card open as its own labelled
        // button after it — three explicit controls in place of a heading-button with two hidden
        // jobs and a trailing run of "Show:" buttons with no selected state. A standalone report
        // tile also keeps its informational heading separate from its open button.
        bool isCarousel = item.CarouselRoot != null && item.AllPages.Count > 1;
        if (isCarousel)
        {
            // Tabs first, then the current page's title heading the content below it — so
            // reading order runs switcher -> "you are here" -> content.
            sb.Append("<div role=\"region\" aria-label=\"").Append(ContainerKind.TabGroup).Append("\">");
            var entries = new List<TabBarHtml.TabEntry>(item.AllPages.Count);
            foreach (var page in item.AllPages)
            {
                var handle = new CarouselPageHandle { Root = item.CarouselRoot, Index = page.Index, Title = page.Title };
                string pid = ServerHandles.RegisterKey(handle,
                    ServerHandles.IdentityPath(item.CarouselRoot) + "|page:" + page.Index);
                entries.Add(new TabBarHtml.TabEntry(pid, page.Title, page.Index == item.VisiblePageIndex));
            }
            sb.Append(TabBarHtml.Build(entries)).Append("</div>");
            sb.Append("<h3>").Append(ServerText.EscapeText(item.Title)).Append("</h3>");
        }
        else if (item.TileButton != null)
        {
            sb.Append("<h3>").Append(ServerText.EscapeText(item.Title)).Append("</h3>");
            RenderTileActivator(sb, new ScreenWalker.TileActivatorItem { TileButton = item.TileButton, Title = item.Title });
        }
        else
            sb.Append("<h3>").Append(ServerText.EscapeText(item.Title)).Append("</h3>");

        foreach (var chart in item.ChartTables)
        {
            if (chart.Note != null) sb.Append("<p>").Append(ServerText.EscapeText(chart.Note)).Append("</p>");
            if(chart.Grid.Provisional)sb.Append(ProvisionalComment);
            AppendStringGrid(sb, chart.Grid.Headers, chart.Grid.Rows, chart.Grid.RowHeader, chart.Grid.RowActions);
        }
        if (item.HasChart && item.ChartTables.Count == 0)
            sb.Append("<p>chart, visual only</p>");
        if (item.WindowNote != null)
            sb.Append("<p>").Append(ServerText.EscapeText(item.WindowNote)).Append("</p>");

        if (item.Rows.Count > 0)
        {
            // A missing team name falls back to the positional truth ("Home"/"Away") rather
            // than a degrade marker: the column side is certain even when the badge text is not.
            sb.Append("<table><thead><tr><th></th><th scope=\"col\">")
              .Append(ServerText.EscapeText(item.HomeTeam ?? "Home")).Append("</th><th scope=\"col\">")
              .Append(ServerText.EscapeText(item.AwayTeam ?? "Away")).Append("</th></tr></thead><tbody>");
            foreach (var row in item.Rows)
            {
                sb.Append("<tr><th scope=\"row\">").Append(ServerText.EscapeText(row.Name)).Append("</th>");
                AppendLiveStatCell(sb, row.Home);
                AppendLiveStatCell(sb, row.Away);
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
        }
        else if (item.Grid != null)
        {
            if(item.Grid.Provisional)sb.Append(ProvisionalComment);
            AppendStringGrid(sb, item.Grid.Headers, item.Grid.Rows, item.Grid.RowHeader, item.Grid.RowActions);
        }
        else if (item.Lines.Count > 0)
        {
            foreach (var line in item.Lines)
                sb.Append("<p>").Append(ServerText.EscapeText(line)).Append("</p>");
        }
        else if (!item.HasChart)
            sb.Append("<p>No readable text in this view.</p>");

        if (item.CarouselRoot != null && item.AllPages.Count > 1)
        {
            // The full-card open, moved off the title heading onto its own explicit control.
            if (item.TileButton != null)
            {
                var tile = new Adapters.ButtonAdapter(item.TileButton);
                if (item.TileButton.panel != null && ElementBridge.IsVisibleInHierarchy(item.TileButton)
                    && !ServerText.IsDisabled(tile))
                {
                    string bid = ServerHandles.Register(new ServerHandles.StopHandle { El = tile }, tile);
                    sb.Append("<p><button type=\"button\" id=\"").Append(bid)
                      .Append("\" onclick=\"act('").Append(bid).Append("')\">Open ")
                      .Append(ServerText.EscapeText(item.Title)).Append(" card</button></p>");
                }
            }
        }
        else
        {
            foreach (var page in item.OtherPages)
            {
                var handle = new CarouselPageHandle { Root = item.CarouselRoot, Index = page.Index, Title = page.Title };
                string pid = ServerHandles.RegisterKey(handle,
                    ServerHandles.IdentityPath(item.CarouselRoot) + "|page:" + page.Index);
                sb.Append("<p><button type=\"button\" id=\"").Append(pid)
                  .Append("\" onclick=\"act('").Append(pid).Append("')\">Show: ")
                  .Append(ServerText.EscapeText(page.Title)).Append("</button></p>");
            }
        }
    }

    // Switches the match header's stats carousel to one of its hidden pages through the game's
    // own SICarouselTileRoot.NavigateToPage — the same call its dot indicator makes — rather
    // than synthesizing clicks on the dots. animate:false swaps instantly, so the post-action
    // settle window re-renders the new page's content in one hop. Returns the "Activated"
    // prefix act() treats as silent success: the refreshed page IS the feedback.
    private sealed class CarouselPageHandle : ServerHandles.Handle
    {
        public UnityEngine.UIElements.VisualElement Root;
        public int Index;
        public string Title;

        public override bool TryValidate(out string error)
        {
            error = null;
            if (Root.panel == null || !ElementBridge.IsVisible(Root))
            {
                error = "That widget is no longer on screen. Refresh.";
                return false;
            }
            return true;
        }

        public override string Activate()
        {
            Root.Cast<SI.UI.SICarouselTileRoot>().NavigateToPage(Index, animate: false);
            return "Activated " + Title;
        }
    }

    // One live-tracked stat value cell. A missing value element renders an honest gap marker —
    // never an empty cell, which reads as "nothing there" — and gets no id, so the values
    // channel can never patch it into looking real.
    private static void AppendLiveStatCell(StringBuilder sb, UnityEngine.UIElements.VisualElement valueEl)
    {
        if (valueEl == null)
        {
            sb.Append("<td>").Append(ServerText.Escape(Speech.Degrade.Value("carousel stat value element missing")))
              .Append("</td>");
            return;
        }
        var text = new Adapters.TextAdapter(valueEl);
        string id = LiveValues.RegisterReadOnly(text);
        sb.Append("<td id=\"").Append(id).Append("\">")
          .Append(ServerText.EscapeText(DisplayText.Of(text))).Append("</td>");
    }

    // A radio group as a native <fieldset role="radiogroup">: the radios share one per-group name
    // so the screen reader treats them as a real group (group boundaries, "N of M") and browser
    // radio semantics auto-deselect the siblings client-side. onchange (not onclick) fires only on
    // the radio that becomes selected, and goes through actradio, which selects the game element
    // silently - the browser shows the new choice and the screen reader announces it, so no status
    // line and no forced refresh, which is what kept the old per-radio inputs feeling stuck.
    private static void RenderRadioGroup(StringBuilder sb, ScreenWalker.RadioGroupItem item)
    {
        // Streamed tables can yield across frames between walk and render. A tab
        // switch can hide the captured group in that interval: its adapters then
        // read blank captions, producing obsolete unnamed inputs. As with outgoing
        // tables, retry the native walk instead of registering the old controls.
        if (item.Group == null || item.Group.panel == null
            || !ElementBridge.IsVisibleInHierarchy(item.Group)
            || item.Radios.Any(r => r is PluginAdapterBase source
                && (source.BackingElement.panel == null
                    || !ElementBridge.IsVisibleInHierarchy(source.BackingElement))))
        {
            sb.Append(ProvisionalComment);
            Log.Debug("[HtmlRenderer] Captured radio group left the visible native tree; rewalking.");
            return;
        }
        // The name only needs to be unique among groups on this page, not stable across refreshes
        // (focus restores by each radio's identity-based id); the group's identity path gives that.
        string groupName = "fmrg" + (uint)ServerHandles.IdentityPath(item.Group).GetHashCode();
        // A segmented control's row label names the group; an SIRadioGroup carries none (Label null).
        // The player instructions editor is the case that matters: its radios ARE the instruction,
        // but the panel never writes the instruction's name anywhere, so without this the player
        // arrives in an unnamed group of options with no idea which setting they are changing.
        // Recovered live from the options themselves, so it stays right when the editor's own
        // instruction switcher moves to another instruction in place. The recovered name is also
        // the editor page's only title, so it doubles as a heading the player can jump to.
        string label = item.Label;
        bool piEditor = false;
        if (string.IsNullOrEmpty(label))
        {
            label = PlayerInstructions.NameForOptions(item.Radios);
            piEditor = !string.IsNullOrEmpty(label);
            if (piEditor)
                sb.Append("<h3>").Append(ServerText.EscapeText(label)).Append("</h3>");
        }
        // A whole-locked instruction's radios are a display, not a control: the game draws its
        // lock line but leaves the radios looking available and silently refuses the click -
        // which here would leave a checked radio the game rejected. Disabled radios say
        // "unavailable" honestly, and the game's own lock sentence follows with the why.
        bool groupLocked = piEditor && PlayerInstructions.EditorInstructionLocked();
        sb.Append("<fieldset role=\"radiogroup\"");
        if (!string.IsNullOrEmpty(label))
            sb.Append(" aria-label=\"").Append(ServerText.EscapeText(label)).Append("\"");
        sb.Append(">");
        for (int r = 0; r < item.Radios.Count; r++)
        {
            var radio = item.Radios[r];
            bool disabled = groupLocked || ServerText.IsDisabled(radio);
            string id = disabled
                ? LiveValues.RegisterReadOnly(radio)
                : ServerHandles.Register(new ServerHandles.StopHandle { El = radio }, radio);
            bool on = ServerText.State(radio) == "selected";
            sb.Append("<p><label><input type=\"radio\" name=\"").Append(groupName)
              .Append("\" id=\"").Append(id).Append("\"")
              .Append(on ? " checked" : "");
            if (disabled) sb.Append(" disabled");
            else sb.Append(" onchange=\"actradio('").Append(id).Append("')\"");
            sb.Append("> ");
            string text = DisplayText.Of(radio);
            sb.Append(ServerText.Escape(text));
            // A disabled radio already reads as unavailable, but the game usually writes WHY right
            // next to it ("...incompatible with this player's position") and the label text does not
            // pick that up. Say the game's reason rather than leave the player guessing whether the
            // option is blocked, broken, or something they can unlock.
            string reason = ServerText.ExtraTextUnder(radio, text);
            if (disabled && !string.IsNullOrEmpty(reason))
                sb.Append(' ').Append(ServerText.Escape(reason));
            sb.Append("</label></p>");
            // A nested sub-option group (the Player Interest dialog's Interested/Persuadable/
            // Doubtful list under "Transfer only") renders as its own radiogroup right after its
            // parent radio. A visible <legend> — "<parent> options" — names it: the reader
            // announces the legend on entering the group and scopes each radio to it, so the
            // boundary between the outer choice and its sub-options is explicit (a bare
            // aria-label repeating the parent's name read as an echo, not a new group). Its
            // radios share a distinct name so the browser treats it as a separate single-select.
            if (item.SubRadios != null && r < item.SubRadios.Count && item.SubRadios[r].Count > 0)
            {
                sb.Append("<fieldset role=\"radiogroup\"><legend>")
                  .Append(ServerText.Escape(DisplayText.Of(radio))).Append(" options</legend>");
                foreach (var sub in item.SubRadios[r])
                {
                    string subId = ServerHandles.Register(new ServerHandles.StopHandle { El = sub }, sub);
                    bool subOn = ServerText.State(sub) == "selected";
                    sb.Append("<p><label><input type=\"radio\" name=\"").Append(groupName).Append('s').Append(r)
                      .Append("\" id=\"").Append(subId).Append("\"")
                      .Append(subOn ? " checked" : "")
                      .Append(" onchange=\"actradio('").Append(subId).Append("')\"> ")
                      .Append(ServerText.Escape(DisplayText.Of(sub))).Append("</label></p>");
                }
                sb.Append("</fieldset>");
            }
            // A radio row's own action controls (the coach list's Edit/Delete buttons) render right
            // after their radio so a screen reader reads them in that option's context.
            if (item.RadioExtras != null && r < item.RadioExtras.Count)
                foreach (var extra in item.RadioExtras[r])
                    RenderNode(sb, extra);
        }
        sb.Append("</fieldset>");
    }

    // A tab group as an ARIA tablist. Each tab registers a handle so act() activates it
    // (the same Clickable.Invoke the speech surface uses), then the body re-renders with the
    // new selection. Ids come from element identity, so focus restores to the clicked tab.
    // The tablist sits inside a labelled region landmark: a tablist is not a landmark role,
    // so without the wrapper landmark navigation (D / Shift+D) could never jump to the tab row.
    private static void RenderTabBar(StringBuilder sb, ScreenWalker.TabBarItem item)
    {
        // As with radio groups, a native tab can hide this captured tab bar
        // while another table is harvested across frames. Its still-attached
        // button captions then read as technical fallbacks. Rewalk the current
        // visible bar instead of registering outgoing tabs beside the new set.
        if (item.Tabs.Any(tab => tab.BackingElement.panel == null
            || !ElementBridge.IsVisibleInHierarchy(tab.BackingElement)))
        {
            sb.Append(ProvisionalComment);
            Log.Debug("[HtmlRenderer] Captured tab bar left the visible native tree; rewalking.");
            return;
        }
        // The native inbox filter bar retains ordinary button interaction, as
        // requested for FM26 parity. Reuse its existing structural Unread anchor,
        // shared button renderer, native tab handles and selected state. Other
        // tab bars (including Preferences) keep the original tablist contract.
        if (item.Tabs.Any(IsUnreadMessagesFilter))
        {
            sb.Append("<div role=\"region\" aria-label=\"")
              .Append(ServerText.Escape(item.Label ?? ContainerKind.ButtonBar)).Append("\">");
            foreach (var tab in item.Tabs)
            {
                sb.Append("<p>");
                AppendButton(sb, tab, ServerText.Clean(tab.GetLabel()), tab.IsSelected);
                sb.Append("</p>");
            }
            sb.Append("</div>");
            return;
        }
        var entries = new List<TabBarHtml.TabEntry>(item.Tabs.Count);
        string unreadId = null;
        bool unreadSelected = false;
        foreach (var tab in item.Tabs)
        {
            string id = ServerHandles.Register(new ServerHandles.StopHandle { El = tab }, tab);
            entries.Add(new TabBarHtml.TabEntry(id, DisplayText.Of(tab), tab.IsSelected, ServerText.IsDisabled(tab)));
            if (IsUnreadMessagesFilter(tab)) { unreadId = id; unreadSelected = tab.IsSelected; }
        }
        // Reuse the original tab HTML unchanged. Carry the existing message-view
        // return marker on FM27's native Unread tab, rather than a plain button.
        string html = TabBarHtml.Build(entries);
        if (unreadId != null) html = html.Replace("id=\"" + unreadId + "\"",
            "id=\"" + unreadId + "\" data-fm-msg-unread-filter" + (unreadSelected ? " data-fm-selected" : ""));
        sb.Append("<div role=\"region\" aria-label=\"")
          .Append(ServerText.Escape(item.Label ?? ContainerKind.TabGroup)).Append("\">")
          .Append(html)
          .Append("</div>");
    }

    // Panels whose buttons print their held-back description on the page instead of pointing at
    // Alt+I. Scoped rather than universal: elsewhere the description is a genuine aside on a
    // control that already reads sensibly without it, and inlining every one of them would bury
    // the controls. Here the description IS the content.
    // The Youth Setup screens join the Data Hub for the same reason: their overview, performance
    // and candidates screens are grids of tiles whose whole content — the facility ratings, the
    // squad size and average age, the next five fixtures, the staff counts — is the description.
    // Without inlining they read as a column of bare tile names.
    private static readonly string[] DescriptionInlinePanels =
    {
        "Data Hub",
        "SquadYouthSetupReport",
        "SquadYouthPerformanceReport",
        "YouthReportSiteYouthCandidatesTool",
        "YouthReportSiteYouthEnvironmentCard",
    };

    // The description DisplayText held back for on-demand reading, or null when there is none or
    // this panel does not inline them. Never the short tail already folded into the label.
    private static string HeldBackDescription(IAccessibleElement el)
    {
        if (el is not Adapters.PluginAdapterBase a) return null;
        if (!InDescriptionInlinePanel(a.BackingElement)) return null;
        if (DisplayText.HasInlinedDescription(el)) return null;
        var desc = DisplayText.DescriptionOf(el);
        return string.IsNullOrEmpty(desc) ? null : desc;
    }

    private static bool InDescriptionInlinePanel(UnityEngine.UIElements.VisualElement el)
    {
        foreach (var panel in DescriptionInlinePanels)
            if (ElementBridge.IsInPanel(el, panel)) return true;
        return false;
    }

    // The button's own name with no inlined description and no Alt+I hint, for the callers that
    // print the description themselves. Profile rename first, matching DisplayText.Of.
    private static string PrimaryLabelOf(IAccessibleElement el)
    {
        var rename = ServerText.RenameOr(el, null);
        return ServerText.Clean(string.IsNullOrEmpty(rename) ? el.GetPrimaryLabel() : rename);
    }

    // Mod-authored accessible name for the single-select row-disclosure radio (the game's control is
    // a bare template toggle with no name of its own). Generic on purpose: the same control selects
    // an advice topic on staff meetings and a fixture on the schedule. The row's own cells carry the
    // identity, so the radio needs only its verb.
    private const string DisclosureRadioLabel = "Select";

    // Marks a table the streamed load could not read. BuildBody looks for it to flag the compose
    // provisional, which is what gets the page re-read instead of leaving the player on it.
    internal const string TableNotLoadedNote = " (table not loaded; refresh)";

    // Invisible marker for a compose that caught the game mid-build — content that rendered but is
    // visibly half-bound (a table with rows and no headers yet). NVDA never sees a comment; BuildBody
    // sniffs it and flags the compose provisional so the settle tick re-reads the page.
    internal const string ProvisionalComment = "<!--fm-provisional-->";

    private static void RenderTable(StringBuilder sb, ScreenWalker.TableItem t)
    {
        // Like ordinary stops and attribute lists, an outgoing native table
        // can remain attached while its ancestor is hidden during the load.
        // It is no longer content of this view: retry the walk for the real
        // replacement/empty state instead of announcing an unavailable table.
        if (t.Table != null && (t.Table.panel == null || !ElementBridge.IsVisibleInHierarchy(t.Table)))
        {
            sb.Append(ProvisionalComment);
            return;
        }
        if (SetPieceTakers.TryRenderTable(sb, t)) return;
        if (SetPieceFrequencies.TryRenderTable(sb, t)) return;
        // A streamed table reaches here with null Rows when its section was rendered without the
        // streamed load phase ever running (a fast-path surface — see ComposeBody). Its sibling
        // renderers all guard this; this one did not, and walking null rows took the process down
        // rather than throwing something catchable. Degrade: say the table is unreadable and keep
        // the rest of the page, which is the honest answer and never a fabricated empty table.
        if (t.Rows == null)
        {
            Speech.Degrade.Note("html: streamed table reached the renderer unloaded; rendering as unavailable");
            sb.Append("<p>").Append(ServerText.Escape(Speech.Degrade.Unavailable))
              .Append(TableNotLoadedNote).Append("</p>");
            return;
        }

        string tablePath = ServerHandles.IdentityPath(t.Table);
        // Above and below: a player who has read to the end of a hundred rows must not have to
        // travel back up to turn the page, and one arriving at the table must not have to travel
        // down to discover it has more than one. Each copy registers its own ids — a DOM id must be
        // unique — so the two are separate controls driving the same page change.
        sb.Append(BuildPager(t, tablePath, "t"));
        AppendSelectionButtons(sb, t, tablePath);
        sb.Append("<table>");
        var toggleCols = ToggleColumns(t.Rows);
        // Exactly one toggle column is a row-selection column ("Sel."); several are an assignment
        // grid (e.g. Coach Assignments), where every column is a real category whose header must
        // stay and whose toggles are named per cell below.
        bool isGrid = toggleCols.Count > 1;
        int selCol = toggleCols.Count == 1 ? toggleCols[0] : DisclosureColumnIndex(t.Rows);
        // A table whose every header is blank (the Debts And Loans totals strip — a visual
        // continuation of the table above it) renders with no thead at all: a row of empty
        // column headers tells the reader nothing.
        bool anyNamedHeader = selCol >= 0;
        foreach (var h in t.Headers)
            if (!IsBlankHeader(h.Text)) { anyNamedHeader = true; break; }
        // The walker's display permutation (see ScreenWalker.ApplyTableColumnMoves), or null for
        // the game's order. Only iteration order changes: every handle below still receives the
        // real column index.
        var order = ColumnOrder(t);
        // Rows without any header elements at all: the walk raced the table's own build (a tab
        // switch mid-transition — headers land a few frames after the rows realize). The rows are
        // real, so render them, but mark the compose provisional: the re-read serves the finished
        // table, headers and late-binding cell values both. Distinct from the deliberate headerless
        // render below, where headers EXIST but are all blank (the Debts And Loans totals strip).
        if (t.Headers.Count == 0 && t.Rows.Count > 0)
        {
            ServerText.LogBackground("[HtmlRenderer] table rendered rows with no headers; compose marked provisional");
            sb.Append(ProvisionalComment);
        }
        if (t.Headers.Count > 0 && anyNamedHeader)
        {
            sb.Append("<thead><tr>");
            // An order may be SHORTER than the header list (a gutter column left out), so it — not
            // the header count — decides how many columns render.
            int headerSlots = order != null ? order.Length : t.Headers.Count;
            for (int i = 0; i < headerSlots; i++)
            {
                int hc = order != null ? order[i] : i;
                var h = t.Headers[hc];
                // The row-selection column's game header is blank, so it renders as the bare "column
                // header" marker. Name it for what its checkboxes do, so the screen reader announces
                // a purpose instead of a placeholder. Only a blank header is replaced: a named toggle
                // column is a real data column, not the selection column.
                if (hc == selCol && IsBlankHeader(h.Text))
                    h.Text = "Selection";
                RenderHeader(sb, h, tablePath, hc);
            }
            sb.Append("</tr></thead>");
        }
        sb.Append("<tbody>");
        foreach (var row in t.Rows)
        {
            if (row == null) continue; // a row that recycled between settle and read
            string rowLabel = RowLabel(row);
            string rowIdentity = isGrid ? RowIdentity(row) : null;
            sb.Append("<tr>");
            // A reordered row iterates every header slot (padding a short, still-streaming row with
            // empty cells so the permuted grid stays aligned) plus any extra cells past the header
            // count in their natural place.
            // A full permutation still pads a short, still-streaming row and keeps any extra cells
            // past the header count. An order that deliberately omits a column renders exactly the
            // columns it lists, so the omitted one does not reappear as an "extra".
            int slots = order == null ? row.Cells.Count
                : order.Length < t.Headers.Count ? order.Length
                : Math.Max(order.Length, row.Cells.Count);
            for (int i = 0; i < slots; i++)
            {
                int c = order != null && i < order.Length ? order[i] : i;
                if (c >= row.Cells.Count)
                {
                    sb.Append("<td></td>");
                    continue;
                }
                // A grid toggle is named by its row and column ("Robert Caplin, Head Coach, Glk"),
                // never by the whole-row label — that would recite every column's value on every
                // checkbox with no way to tell which category the checkbox controls.
                string header = c < t.Headers.Count && !IsBlankHeader(t.Headers[c].Text)
                    ? t.Headers[c].Text : "";
                string toggleLabel = isGrid ? JoinNonEmpty(rowIdentity, header) : rowLabel;
                sb.Append("<td>");
                RenderCell(sb, t.Table, tablePath, row.DataIndex, c, row, row.Cells[c], toggleLabel, header);
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody>");
        if(t.FooterPending)sb.Append(ProvisionalComment);
        if(t.Footer!=null)
        {
            sb.Append("<tfoot><tr>");
            if(t.Footer.Count==t.Headers.Count)
            {
                int slots=order?.Length??t.Headers.Count;
                for(int i=0;i<slots;i++)
                {
                    int column=order==null?i:order[i];
                    sb.Append(column==0?"<th scope=\"row\">":"<td>");
                    foreach(var part in t.Footer[column])RenderItem(sb,part);
                    sb.Append(column==0?"</th>":"</td>");
                }
            }
            else
            {
                // Keep unexpected native footer content without assigning it
                // to headers whose column count no longer agrees.
                Speech.Degrade.Note("table footer column count differs from headers; content retained unaligned");
                sb.Append("<td colspan=\"").Append(Math.Max(1,order?.Length??t.Headers.Count)).Append("\">");
                foreach(var cell in t.Footer)foreach(var part in cell)RenderItem(sb,part);
                sb.Append("</td>");
            }
            sb.Append("</tr></tfoot>");
        }
        sb.Append("</table>");
        // Only a page the loader could not finish reaches here: "there are more rows" is the pager's
        // job, and saying "truncated" about an ordinary next page would report a defect where there
        // is none. An incomplete page IS a gap in what the player can read, so it still says so.
        if (t.Truncated)
            sb.Append("<p>").Append(ServerText.Escape(Speech.Degrade.Unreadable))
              .Append(" all of this page, showing ").Append(t.Rows.Count).Append(" rows.</p>");
        sb.Append(BuildPager(t, tablePath, "b"));
    }

    // A multi-select table (the game's Many selection mode - the databases' per-row checkbox
    // tables) gets mod-authored bulk-selection buttons above it, spanning the whole filtered
    // table, not just the rendered page. The press reply announces the count the game actually
    // selected, and the refresh that follows every activation re-reads the checkboxes live.
    private static void AppendSelectionButtons(StringBuilder sb, ScreenWalker.TableItem t, string tablePath)
    {
        if (!t.MultiSelect || tablePath == null) return;
        string allId = ServerHandles.RegisterKey(
            new ServerHandles.TableSelectionHandle { Table = t.Table, Clear = false }, tablePath + "|selall");
        string clearId = ServerHandles.RegisterKey(
            new ServerHandles.TableSelectionHandle { Table = t.Table, Clear = true }, tablePath + "|selclear");
        sb.Append("<p><button type=\"button\" id=\"").Append(allId)
          .Append("\" onclick=\"act('").Append(allId).Append("')\">Select all</button> ");
        if (t.PageCount > 1)
        {
            string pageId = ServerHandles.RegisterKey(
                new ServerHandles.TableSelectionHandle { Table = t.Table, PageTablePath = tablePath },
                tablePath + "|selpage");
            sb.Append("<button type=\"button\" id=\"").Append(pageId)
              .Append("\" onclick=\"act('").Append(pageId).Append("')\">Select all on page</button> ");
        }
        sb.Append("<button type=\"button\" id=\"").Append(clearId)
          .Append("\" onclick=\"act('").Append(clearId).Append("')\">Clear selection</button></p>");
    }

    // The page controls for a table that has more than one page, or nothing at all. slot ("t"/"b")
    // separates the copy above the table from the copy below in the id pool. The buttons carry a
    // step, not a destination, so pressing one twice in a row moves two pages instead of repeating
    // the first (see TablePageHandle); the load clamps again against the live row count.
    private static string BuildPager(ScreenWalker.TableItem t, string tablePath, string slot)
        => BuildPager(tablePath, t.Page, t.PageCount, slot);

    // Shared by every paged surface (streamed tables, the consolidated fixtures tables): the page
    // is stored and stepped by path alone, so nothing here needs the item it came from.
    private static string BuildPager(string tablePath, int page, int pageCount, string slot)
    {
        if (pageCount <= 1 || tablePath == null) return "";
        string prevId = ServerHandles.RegisterKey(
            new ServerHandles.TablePageHandle { TablePath = tablePath, Delta = -1, PageCount = pageCount },
            tablePath + "|pageprev" + slot);
        string nextId = ServerHandles.RegisterKey(
            new ServerHandles.TablePageHandle { TablePath = tablePath, Delta = 1, PageCount = pageCount },
            tablePath + "|pagenext" + slot);
        return TablePagerHtml.Build(prevId, nextId, page, pageCount);
    }

    // The column-summary block as a table transposed from the game's layout: the game draws two
    // long value rows under the assignments table, but here each CATEGORY is a row ("Att Tact,
    // Average, 2 and a half stars"), so a screen reader reads a category's workload and lead
    // rating together instead of nine context-free values in a row. Read-only — a body refresh
    // re-reads everything live.
    private static void RenderColumnSummary(StringBuilder sb, ScreenWalker.ColumnSummaryItem cs)
    {
        sb.Append("<table><thead><tr><th scope=\"col\"></th>");
        foreach (var label in cs.ValueLabels)
            sb.Append("<th scope=\"col\">").Append(ServerText.EscapeText(label)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        for (int i = 0; i < cs.Values.Count; i++)
        {
            sb.Append("<tr><th scope=\"row\">").Append(ServerText.EscapeText(cs.Categories[i])).Append("</th>");
            foreach (var v in cs.Values[i])
                sb.Append("<td>").Append(ServerText.EscapeText(v)).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    // The column indices whose cells hold a real checkbox/radio (the control RenderCell draws as a
    // native input) in any row. The single-select row disclosure presents as a button, not a toggle,
    // so it never counts here.
    private static List<int> ToggleColumns(List<Contexts.TableRow> rows)
    {
        var cols = new SortedSet<int>();
        foreach (var row in rows)
        {
            if (row == null) continue;
            for (int c = 0; c < row.Cells.Count; c++)
                foreach (var e in row.Cells[c].Elements)
                    if (e.IsToggle) { cols.Add(c); break; }
        }
        return new List<int>(cols);
    }

    // The column holding the single-select row disclosure (the per-row select control that presents
    // as a button), or -1. Like the checkbox selection column, its game header is blank.
    private static int DisclosureColumnIndex(List<Contexts.TableRow> rows)
    {
        foreach (var row in rows)
        {
            if (row == null) continue;
            for (int c = 0; c < row.Cells.Count; c++)
                foreach (var e in row.Cells[c].Elements)
                    if (e.IsSelectionDisclosure) return c;
        }
        return -1;
    }

    // A header the game left unnamed: empty, or the bare "column header" role marker DisplayText
    // yields for a label-less header stop.
    private static bool IsBlankHeader(string text)
        => string.IsNullOrWhiteSpace(text) || text == "column header";

    // Who a grid row is about, used to prefix each of its assignment toggles: the entity selection
    // label when the row carries one, otherwise the first plain or activatable cell text (the row
    // disclosure's "Robert Caplin, Head Coach", or a name button). Toggles and dropdowns never
    // identify a row. Empty when the row carries no such text.
    private static string RowIdentity(Contexts.TableRow row)
    {
        if (!string.IsNullOrEmpty(row.SelectionLabel))
            return row.SelectionLabel;
        foreach (var cell in row.Cells)
            foreach (var elem in cell.Elements)
                if (!elem.IsToggle && !elem.IsDropdown && !string.IsNullOrEmpty(elem.Text))
                    return elem.Text;
        return "";
    }

    private static string JoinNonEmpty(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b ?? "";
        if (string.IsNullOrEmpty(b)) return a;
        return a + ", " + b;
    }

    /// <summary>
    /// The column order to render: the walk's permutation, minus a declared gutter column when the
    /// loaded rows confirm it is empty. A gutter that holds anything at all is rendered like any
    /// other column — the walk names a candidate, the data decides.
    /// </summary>
    private static int[] ColumnOrder(ScreenWalker.TableItem t)
    {
        var order = t.DisplayOrder;
        int gutter = t.GutterColumn;
        if (gutter < 0 || gutter >= t.Headers.Count) return order;
        if (!ScreenWalker.IsColumnEmpty(t, gutter))
        {
            ServerText.LogBackground($"[HtmlRenderer] gutter column {gutter} holds content; rendering it");
            return order;
        }

        var source = order ?? BuildIdentityOrder(t.Headers.Count);
        var kept = new List<int>(source.Length);
        foreach (var c in source)
            if (c != gutter) kept.Add(c);
        return kept.ToArray();
    }

    private static int[] BuildIdentityOrder(int count)
    {
        var order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;
        return order;
    }

    // A column header. A header with a sort button becomes an activatable button so Enter dispatches
    // the game's own sort (universal: any column can be tried, the game decides whether it reorders);
    // aria-sort carries the real state and nothing more. A non-button header is plain text.
    internal static void RenderHeader(StringBuilder sb, Contexts.TableHeader h, string tablePath, int index)
    {
        sb.Append("<th scope=\"col\"");
        string aria = AriaSort(h.SortState);
        if (aria != null) sb.Append(" aria-sort=\"").Append(aria).Append("\"");
        sb.Append(">");
        var headerAdapter = h.HeaderButton == null ? null : new ColumnHeaderAdapter(h.HeaderButton);
        if (headerAdapter != null && (headerAdapter.HasPrimaryClick || h.HasContextMenu))
        {
            string id = ServerHandles.RegisterKey(
                new ServerHandles.HeaderHandle { Header = headerAdapter },
                tablePath + "#h" + index);
            sb.Append("<button type=\"button\" id=\"").Append(id)
              .Append("\" onclick=\"").Append(headerAdapter.HasPrimaryClick ? "act('" : "FM.contextmenu.open('")
              .Append(id).Append("')\"");
            if (ServerText.IsDisabled(headerAdapter)) sb.Append(" disabled");
            // The game's per-column options menu (insert / remove / replace column, view management),
            // opened with Shift+F10 or the Applications key like any other menu-bearing control.
            // Left-click still sorts; the two gestures are separate paths in the game too.
            AppendContextMenuAttr(sb, h.HasContextMenu);
            sb.Append(">").Append(ServerText.EscapeText(h.Text)).Append("</button>");
        }
        else
        {
            sb.Append(ServerText.EscapeText(h.Text));
        }
        sb.Append("</th>");
    }

    // ColumnHeaderAdapter.GetState() to an aria-sort token; null when the game reports no sort, so a
    // column is never announced as sorted (or sortable) beyond what the game actually shows.
    private static string AriaSort(string state) => state switch
    {
        "sorted ascending" => "ascending",
        "sorted descending" => "descending",
        "sorted" => "other",
        _ => null,
    };

    /// <summary>
    // An aria-label attribute, or nothing at all when there is no truthful name to give. An empty
    // aria-label would leave the control unnamed anyway, and omitting it lets the screen reader
    // name a table cell's control from its row and column headers instead.
    private static string AriaLabelAttr(string label)
        => string.IsNullOrEmpty(label) ? "" : " aria-label=\"" + ServerText.EscapeText(label) + "\"";

    /// <summary>
    /// True when a cell control's accessible name is just the text of a DIFFERENT cell in the same
    /// row — a name the field-label walk scavenged sideways rather than found. The contract-expiry
    /// message table is the reported case: every Recommended Action select came back named
    /// "$184K p/a", the value of the Current Wage column three cells to its left.
    ///
    /// Only plain text counts as a source. A neighbouring cell's own control (a name button, a
    /// second dropdown) is not a label either, but those cells legitimately share wording with
    /// labelled controls elsewhere, and matching against them would strip real names.
    ///
    /// The row's FIRST plain-text cell is exempt: a label-then-control row (Club →
    /// Responsibilities → Delegation, "Handles Team Selection (U19)" beside its dropdown) names its
    /// control from exactly there, and that name is right. Only a match further along the row — a
    /// value column the walk reached sideways into — is the defect.
    /// </summary>
    private static bool NamedBySiblingCell(Contexts.TableRow row, int col, string label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        bool seenText = false;
        for (int c = 0; c < row.Cells.Count; c++)
        {
            var cell = row.Cells[c];
            for (int e = 0; e < cell.Elements.Count; e++)
            {
                var sibling = cell.Elements[e];
                if (sibling.CanActivate || sibling.IsDropdown || sibling.IsToggle) continue;
                if (string.IsNullOrEmpty(sibling.Text)) continue;
                bool isRowLabel = !seenText;
                seenText = true;
                if (c == col || isRowLabel) continue;
                if (sibling.Text == label) return true;
            }
        }
        return false;
    }

    // Every interactive element a cell holds, rendered in document order inside the one <td>: a
    // dropdown as a <select>, an actionable element as a <button>, anything else as text. Each
    // interactive element carries its own handle keyed by its position in the cell, so the action
    // dispatches to the right control even when a cell holds several.
    internal static string RegisterCellHandle(VisualElement table, string tablePath,
        int dataIndex, int col, int element, string nativeIdentity, bool disclosure = false, bool manySelector = false)
    {
        var key = tablePath + (nativeIdentity == null ? "@" + dataIndex : "@record:" + nativeIdentity)
            + "," + col + "#" + element;
        return ServerHandles.RegisterKey(new ServerHandles.CellHandle
        {
            Table = table, Row = dataIndex, Col = col, ElementIndex = element,
            NativeRowIdentity = nativeIdentity, IsSelectionDisclosure = disclosure,
            IsManySelectRowSelector = manySelector,
        }, key);
    }

    internal static void RenderCell(StringBuilder sb, VisualElement table, string tablePath, int dataIndex, int col, Contexts.TableRow row, Contexts.TableCell cell, string rowLabel, string header, int firstElement = 0)
    {
        if (cell.StatusFlagsUnreadable || cell.StatusFlags != null)
        {
            RenderStatusFlags(sb, table, tablePath, dataIndex, col, cell, row.NativeIdentity);
            return;
        }

        for (int e = firstElement; e < cell.Elements.Count; e++)
        {
            var elem = cell.Elements[e];
            if (e > firstElement) sb.Append(' ');
            if (elem.IsDropdown)
            {
                // Mirror the standalone dropdown case: the value is the accessible name, the current
                // value the only option, so it reads value-role. A dropdown that borrowed its name
                // from its own displayed value (the opposition-instructions grid, where the value is
                // a bare "-" and the state a positional "1 of 3") instead takes its column header as
                // the name and shows the borrowed value as the value — "Tight Marking, -", the same
                // label-then-value shape every settings-row dropdown reads with.
                string ddLabel = elem.DropdownLabel;
                string ddValue = elem.DropdownState;
                if (ddLabel == Degrade.UnknownControl && !string.IsNullOrEmpty(header))
                {
                    // An unassigned native slot has a blank face, not an
                    // unknown value. Its actual column still names the field.
                    ddLabel = header;
                }
                else if (elem.IsValueNamedDropdown && !string.IsNullOrEmpty(header))
                {
                    ddValue = ddLabel;
                    ddLabel = header;
                }
                else if (NamedBySiblingCell(row, col, ddLabel))
                {
                    // The name was scavenged from a neighbouring column, so it describes the wrong
                    // thing entirely. The column header is the truthful name; with no header the
                    // select goes unnamed and the screen reader announces it from the table's own
                    // row and column headers, which beats reciting another cell's value.
                    ddLabel = header;
                }
                // Greyed out in-game: mirror the standalone dropdown's disabled case — no handle,
                // no data-dd, the native disabled attribute. The open would be refused anyway, so an
                // openable select would announce an operability the game does not offer.
                if (elem.IsDisabled)
                {
                    sb.Append("<select disabled").Append(AriaLabelAttr(ddLabel))
                      .Append("><option selected>").Append(ServerText.EscapeText(ddValue))
                      .Append("</option></select>");
                }
                else
                {
                    string id = RegisterCellHandle(table, tablePath, dataIndex, col, e, row.NativeIdentity);
                    sb.Append("<select id=\"").Append(id)
                      .Append("\" data-dd=\"1\"").Append(AriaLabelAttr(ddLabel)).Append("><option selected>")
                      .Append(ServerText.EscapeText(ddValue))
                      .Append("</option></select>");
                }
            }
            else if (elem.IsToggle)
            {
                // A native checkbox/radio carrying the cell's live on/off state, mirroring the
                // standalone RenderNode checkbox case: the input conveys role and checked state to
                // the screen reader, and onclick=act dispatches the same CellHandle the button path
                // used (verified to flip the game checkbox), then the body refresh re-reads state.
                string id = elem.IsDisabled ? null : RegisterCellHandle(table, tablePath, dataIndex, col, e, row.NativeIdentity, manySelector: elem.IsManySelectRowSelector);
                string inputType = elem.IsRadio ? "radio" : "checkbox";
                sb.Append("<input type=\"").Append(inputType).Append('"');
                if (id != null) sb.Append(" id=\"").Append(id).Append('"');
                // Native radios in a table share a per-table name so the browser deselects siblings
                // client-side; checkboxes stay independent. The game still owns the real selection.
                if (elem.IsRadio)
                    sb.Append(" name=\"")
                      .Append(string.IsNullOrEmpty(elem.RadioGroup)
                          ? "fmtr" + (uint)tablePath.GetHashCode()
                          : ServerText.Escape(elem.RadioGroup))
                      .Append('"');
                if (elem.IsChecked) sb.Append(" checked");
                // The control itself is unlabeled in-game (a template checkbox whose only name is a
                // leaked id like "CheckboxDefault"); name it after its row so the toggle announces
                // what it controls. With no row text, the column header ("Sel.") still applies, so
                // emit no name rather than leak the template id.
                if (!string.IsNullOrEmpty(rowLabel))
                    sb.Append(" aria-label=\"").Append(ServerText.EscapeText(rowLabel)).Append("\"");
                if (elem.IsDisabled) sb.Append(" disabled");
                else sb.Append(" onclick=\"act('").Append(id).Append("')\"");
                sb.Append('>');
            }
            else if (elem.IsSegmented)
            {
                // A two-option segmented control as a data-ti select, so it carries the SAME
                // keyboard contract as every other combo in the app: browsing is local, ENTER is
                // the only commit, and leaving any other way reverts to the game's value. It was
                // briefly a plain onchange select, which committed on every arrow — the one combo
                // in the app you could not browse without changing.
                //
                // Rendered as a button (what it was before that) its whole accessible name flipped
                // between "Player" and "Position" on every press, naming neither the control nor
                // the alternative — and an in-place text swap is exactly the change a screen
                // reader's browse buffer is least likely to re-announce, so the cell read stale.
                // The select keeps a stable name (the column header, the only place the game names
                // the pair) and carries the choice as a real value read natively.
                //
                // The commit is "select option N", not "flip whatever is there", so a repeated or
                // replayed commit lands on the option the player named rather than toggling past it.
                string segLabel = string.IsNullOrEmpty(header) ? elem.Text : header;
                string segCurrent = elem.SegmentIndex >= 0 && elem.SegmentIndex < elem.SegmentOptions.Length
                    ? elem.SegmentOptions[elem.SegmentIndex]
                    : Speech.Degrade.Value("segmented cell: no active option");
                if (elem.IsDisabled)
                {
                    sb.Append("<select disabled aria-label=\"").Append(ServerText.EscapeText(segLabel))
                      .Append("\"><option selected>").Append(ServerText.EscapeText(segCurrent)).Append("</option></select>");
                    continue;
                }
                string id = RegisterCellHandle(table, tablePath, dataIndex, col, e, row.NativeIdentity);
                sb.Append("<select id=\"").Append(id)
                  .Append("\" data-ti=\"1\" data-ti-set=\"/segment/set\" data-ti-cur=\"").Append(elem.SegmentIndex)
                  .Append("\" data-ti-names=\"").Append(TeamInstructions.JsonAttr(elem.SegmentOptions))
                  .Append("\" data-ti-longs=\"").Append(TeamInstructions.JsonAttr(elem.SegmentOptions))
                  .Append("\" aria-label=\"").Append(ServerText.EscapeText(segLabel))
                  .Append("\"><option selected>").Append(ServerText.EscapeText(segCurrent))
                  .Append("</option></select>");
            }
            else if (elem.IsSelectionDisclosure)
            {
                // The single-select row disclosure is the game's own radio: SelectionCell keeps
                // exactly one toggle on per list, and activation routes through the list's
                // Controller.UpdateSelection. A native radio input carries that live selected
                // state to the screen reader; the whole-row button label this cell used to render
                // only duplicated the Subject/Reason cells beside it, so the control takes the
                // short generic name instead and the row's own cells identify it. Use the same
                // per-table scope as native radio cells. A page-wide name makes the browser erase
                // an independently selected table's checked state on EVERY render, including
                // authoritative native refreshes (the report's IP/OOP formation tables).
                // An explicitly shared native set can retain the existing RadioGroup override.
                // Cross-list business selection is still enforced and re-read by the game.
                string id = elem.IsDisabled ? null : RegisterCellHandle(table, tablePath, dataIndex, col, e, row.NativeIdentity, disclosure: true);
                sb.Append("<input type=\"radio\" name=\"")
                  .Append(string.IsNullOrEmpty(elem.RadioGroup)
                      ? "fmtr" + (uint)tablePath.GetHashCode()
                      : ServerText.Escape(elem.RadioGroup)).Append('"');
                if (id != null) sb.Append(" id=\"").Append(id).Append('"');
                if (elem.IsChecked) sb.Append(" checked");
                sb.Append(" aria-label=\"").Append(DisclosureRadioLabel).Append('"');
                if (elem.IsDisabled) sb.Append(" disabled");
                else sb.Append(" onclick=\"act('").Append(id).Append("')\"");
                sb.Append('>');
            }
            else if (elem.CanActivate)
            {
                if (elem.IsDisabled)
                {
                    sb.Append("<button type=\"button\" disabled");
                    AppendContextMenuAttr(sb, elem.HasContextMenu);
                    sb.Append('>').Append(ServerText.EscapeText(elem.Text)).Append("</button>");
                    continue;
                }
                string id = RegisterCellHandle(table, tablePath, dataIndex, col, e, row.NativeIdentity);
                sb.Append("<button type=\"button\" id=\"").Append(id)
                  .Append("\" onclick=\"act('").Append(id).Append("')\"");
                AppendContextMenuAttr(sb, elem.HasContextMenu);
                sb.Append(">").Append(ServerText.EscapeText(elem.Text)).Append("</button>");
            }
            else
            {
                sb.Append(ServerText.EscapeText(elem.Text));
            }
        }
    }

    // The squad "Inf" cell. The walk can only ever find the one badge the game parents into the row
    // (the rest live in a hover popup that does not exist until it opens), so the flags come from
    // StatusFlagsReader and the shaping — flat for one, a list for several — from StatusFlagsHtml.
    //
    // The first flag keeps its live control: the game's badge button opens that flag's explanation
    // panel, and it is the only flag with an element to act on. It is labelled with the flag's full
    // title rather than the cell's cryptic abbreviation ("Injured", not "Inj") — both are the game's
    // own strings from the same record, and only one of them is any use read aloud. The remaining
    // flags render as text: fabricating buttons for them would offer actions that cannot fire.
    private static void RenderStatusFlags(StringBuilder sb, VisualElement table, string tablePath,
        int dataIndex, int col, Contexts.TableCell cell, string nativeIdentity)
    {
        string marker = cell.StatusFlagsUnreadable ? Degrade.Unreadable : null;

        string controlHtml = null;
        var flags = cell.StatusFlags;
        if (marker == null && flags != null && flags.Count > 0 && cell.Elements.Count > 0
            && cell.Elements[0].CanActivate)
        {
            string id = RegisterCellHandle(table, tablePath, dataIndex, col, 0, nativeIdentity);
            controlHtml = "<button type=\"button\" id=\"" + id + "\" onclick=\"act('" + id + "')\">"
                        + ServerText.EscapeText(flags[0].Full) + "</button>";
        }

        sb.Append(Navigation.StatusFlagsHtml.Build(flags, controlHtml, marker));
    }

    // The identifying text of a row, used to name a checkbox/radio cell the game leaves unlabeled
    // (the "Sel." column's control is a bare template checkbox). An entity row (a player/staff list)
    // carries a precomputed "<name>, <position>" label so the checkbox announces who it selects, not
    // the row's stat line. Other rows (a file table) fall back to joining the plain-text cells — the
    // file name, nation, etc. — so the toggle still announces which row it controls ("Canada
    // Database 5.0, Canada"). Empty when the row carries no plain text.
    private static string RowLabel(Contexts.TableRow row)
    {
        if (!string.IsNullOrEmpty(row.SelectionLabel))
            return row.SelectionLabel;
        var parts = new List<string>();
        foreach (var cell in row.Cells)
            foreach (var elem in cell.Elements)
                if (!elem.IsToggle && !elem.IsDropdown && !elem.CanActivate && !string.IsNullOrEmpty(elem.Text))
                    parts.Add(elem.Text);
        return string.Join(", ", parts);
    }

    // The bookmark editor's current bookmarks as one semantic table: Key (the Ctrl chord),
    // Action (the destination, with its trail), Move (the up/down stops in one cell), Delete.
    // The buttons are the surface's synthetic adapters — identity-keyed handles, so an edit
    // changes exactly its own nodes under the screen reader.
    private static void RenderBookmarksTable(StringBuilder sb, ScreenWalker.BookmarksTableItem item)
    {
        sb.Append("<table><thead><tr>")
          .Append("<th scope=\"col\">Key</th>")
          .Append("<th scope=\"col\">Action</th>")
          .Append("<th scope=\"col\">Move</th>")
          .Append("<th scope=\"col\">Delete</th>")
          .Append("</tr></thead><tbody>");
        foreach (var row in item.Rows)
        {
            sb.Append("<tr>");
            sb.Append("<th scope=\"row\">").Append(ServerText.EscapeText(row.Chord)).Append("</th>");
            sb.Append("<td>").Append(ServerText.EscapeText(row.Action)).Append("</td>");
            sb.Append("<td>");
            AppendCellButton(sb, row.MoveUp);
            AppendCellButton(sb, row.MoveDown);
            sb.Append("</td>");
            sb.Append("<td>");
            AppendCellButton(sb, row.Remove);
            sb.Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    // One synthetic-adapter button inside a table cell (no <p> wrapper). Null renders nothing —
    // an empty Move cell on an edge row is an honest "cannot move that way".
    private static void AppendCellButton(StringBuilder sb, IAccessibleElement el)
    {
        if (el == null) return;
        string id = ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
        sb.Append("<button type=\"button\" id=\"").Append(id)
          .Append("\" onclick=\"act('").Append(id).Append("')\">")
          .Append(ServerText.Escape(el.GetLabel())).Append("</button> ");
    }

    // The standalone Messages panel's inbox as one semantic table: Status, Subject, Sender, Sent
    // (date + time) — the parts MessageButtonAdapter composes, so the screen reader gets column
    // navigation over the inbox. Cells were baked (cleaned) at walk time. The Subject cell
    // is the activatable control: clicking it opens the message as the modal Message view
    // (FM.msgview -> /message/open), and it keeps the row's context menu. The handle id is the
    // same identity-derived id the row's plain-button render had, so focus survives the redesign's
    // refreshes and returns to the row after the modal closes. A disabled row renders its subject
    // as plain text — never a fake control.
    private static void RenderMessagesTable(StringBuilder sb, ScreenWalker.MessagesTableItem item)
    {
        ScreenWalker.RefreshMessagesTable(item);
        // A subject that had not bound at walk time rendered as a marker below; the provisional
        // comment makes the SSE marker retry re-read this page once the async bind lands.
        if (item.Provisional) sb.Append(ProvisionalComment);
        // FM26 does not emit an inbox table without messages. Keep the FM27
        // render-time re-read and provisional marker for late native bindings,
        // but do not expose an empty table after the final unread row leaves.
        if (item.Rows.Count == 0) return;
        // The compact row template (small screens) shows the message body's first line where the
        // full template shows the subject; the column header must say which one the cells hold.
        sb.Append("<table><thead><tr>")
          .Append("<th scope=\"col\">Status</th>")
          .Append("<th scope=\"col\">").Append(item.AnyPreview ? "Preview" : "Subject").Append("</th>")
          .Append("<th scope=\"col\">Sender</th>")
          .Append("<th scope=\"col\">Sent</th>")
          .Append("</tr></thead><tbody>");
        foreach (var row in item.Rows)
        {
            var messageId = row.Adapter.MessageId;
            if (!messageId.HasValue) sb.Append(ProvisionalComment);
            sb.Append("<tr");
            if (messageId.HasValue) sb.Append(" data-fm-message-id=\"").Append(messageId.Value).Append('"');
            sb.Append('>');
            sb.Append("<td>").Append(ServerText.Escape(row.Status)).Append("</td>");
            sb.Append("<td>");
            if (!row.Adapter.CanActivate || ServerText.IsDisabled(row.Adapter))
            {
                if (row.SubjectUnreadable)
                {
                    // Keep the inherited late-subject healing path even when
                    // the native summary has no message-opening operation.
                    string id = LiveValues.RegisterReadOnly(
                        new Adapters.MessageSubjectValue(row.Adapter.BackingElement));
                    sb.Append("<span id=\"").Append(id).Append("\">")
                      .Append(ServerText.Escape(row.Subject)).Append("</span>");
                }
                else sb.Append(ServerText.Escape(row.Subject));
            }
            else
            {
                string id = ServerHandles.Register(new ServerHandles.StopHandle { El = row.Adapter }, row.Adapter);
                // A subject that had not bound at compose time becomes a live-value source, so the
                // value channel pushes the real subject into this button when the bind lands —
                // however late. The marker retry's recompose covers only the first few seconds; a
                // slower bind (and every re-read of a row that stays mid-bind) otherwise left the
                // marker on the page until the player navigated away and back by hand.
                if (row.SubjectUnreadable)
                    LiveValues.Track(id, new Adapters.MessageSubjectValue(row.Adapter.BackingElement));
                sb.Append("<button type=\"button\" id=\"").Append(id)
                  .Append("\" onclick=\"FM.msgview.open('").Append(id).Append("')\"");
                AppendContextMenuAttr(sb, row.Adapter.HasContextMenu);
                sb.Append(">").Append(ServerText.Escape(row.Subject)).Append("</button>");
            }
            sb.Append("</td>");
            RenderSenderCell(sb, row);
            sb.Append("<td>").Append(ServerText.Escape(row.Sent)).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    // The Sender column. A person sender carries their own right-click menu (Contract, Report, ...);
    // the game arms it on the sender-name link, which the row's subject button used to inherit and
    // mislabel. Here it lives on the cell that names the sender: a button whose click and Shift+F10
    // both open the sender's menu. A news-outlet sender has no such menu, so the check leaves it
    // plain text — never a fake control. The menu resolution runs on the render thread (main), like
    // every other HasContextMenu read.
    private static void RenderSenderCell(StringBuilder sb, ScreenWalker.MessageRow row)
    {
        sb.Append("<td>");
        var leaf = row.SenderLeaf;
        bool hasMenu = leaf != null && Adapters.ContextMenuOpener.Resolve(leaf).Target != null;
        if (hasMenu && !string.IsNullOrEmpty(row.Sender))
        {
            // Key off the row's own stable identity plus a suffix, so the sender button keeps its id
            // across refreshes (NVDA focus survives) and never collides with the subject button's id.
            string key = ServerHandles.KeyFor(row.Adapter) + ":sender";
            string id = ServerHandles.RegisterKey(new ServerHandles.MenuLeafHandle { Leaf = leaf }, key);
            sb.Append("<button type=\"button\" id=\"").Append(id)
              .Append("\" onclick=\"FM.contextmenu.open('").Append(id).Append("')\" aria-haspopup=\"menu\">")
              .Append(ServerText.Escape(row.Sender)).Append("</button>");
        }
        else
        {
            sb.Append(ServerText.Escape(row.Sender));
        }
        sb.Append("</td>");
    }

    // One stage's match results as a heading plus a semantic table: Home (team + league position),
    // Score, Away, Scorers, then (when present) Options, and finally Status. Team names render as the
    // same act() buttons a normal stop does, so they stay clickable to the team's page; the
    // score/status/positions are live game text, cleaned and escaped at render. Read-only otherwise —
    // results don't change once final, and a body refresh re-reads everything live.
    private static void RenderFixtures(StringBuilder sb, ScreenWalker.FixturesItem f)
    {
        if (!string.IsNullOrEmpty(f.StageName))
            sb.Append("<h3>").Append(ServerText.EscapeText(f.StageName)).Append("</h3>");

        // A long round (a cup's 290 ties) is paged like the streamed tables, with the controls
        // either side of the table so the player reaches them without crossing the rows.
        sb.Append(BuildPager(f.Path, f.Page, f.PageCount, "t"));

        // Columns that no row in this table populates are dropped whole, so a table variant with
        // less data (a stage bracket: no scorers, no status, no per-match controls) doesn't drag
        // empty cells past the screen reader. A row missing a populated column's value still gets
        // an empty cell, keeping the grid rectangular.
        bool hasOptions = false, hasScorers = false, hasStatus = false;
        foreach (var m in f.Matches)
        {
            if (m.Options != null && m.Options.Count > 0) hasOptions = true;
            if ((m.HomeScorers != null && m.HomeScorers.Count > 0)
                || (m.AwayScorers != null && m.AwayScorers.Count > 0)) hasScorers = true;
            if (!string.IsNullOrEmpty(m.Status)) hasStatus = true;
        }

        sb.Append("<table><thead><tr>")
          .Append("<th scope=\"col\">Home</th>")
          .Append("<th scope=\"col\">Score</th>")
          .Append("<th scope=\"col\">Away</th>");
        if (hasScorers) sb.Append("<th scope=\"col\">Scorers</th>");
        if (hasOptions) sb.Append("<th scope=\"col\">Options</th>");
        // Status (FT / live minute / kickoff time) is the last column: it's supplementary to the
        // result for finished matches, so the player reads Home/Score/Away/Scorers first.
        if (hasStatus) sb.Append("<th scope=\"col\">Status</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var m in f.Matches)
        {
            sb.Append("<tr>");
            sb.Append("<td>"); RenderTeamCellWithExtras(sb, m.HomeTeam, m.HomeTeamExtra, m.HomePosition); sb.Append("</td>");
            sb.Append("<td>").Append(ServerText.EscapeText(m.Score ?? "")).Append("</td>");
            sb.Append("<td>"); RenderTeamCellWithExtras(sb, m.AwayTeam, m.AwayTeamExtra, m.AwayPosition); sb.Append("</td>");
            if (hasScorers) { sb.Append("<td>"); RenderScorers(sb, m); sb.Append("</td>"); }
            if (hasOptions)
            {
                sb.Append("<td>");
                if (m.Options != null)
                    foreach (var opt in m.Options)
                        RenderNode(sb, opt, FixtureOptionLabel(opt));
                sb.Append("</td>");
            }
            if (hasStatus)
                sb.Append("<td>").Append(ServerText.EscapeText(m.Status ?? "")).Append("</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        sb.Append(BuildPager(f.Path, f.Page, f.PageCount, "b"));
    }

    // The Add/Remove Leagues grid as one semantic table: a fixed column layout (Nation, Mode, the
    // men's and women's rule + setting pairs, Remove) covering EVERY nation, not just the ~6 the
    // virtualised list keeps realized — LeagueTableLoader streams them all in first. Rows are plain
    // data (recycled elements can't be held); the rule/setting dropdowns and remove button stay
    // interactive by re-realising their control by data index through LeagueRowHandle at action time.
    // The setting dropdowns are icon-only in-game, so they carry an explicit "Men's/Women's setting"
    // label the adjacent count would otherwise be mistaken for.
    private static void RenderLeagueTable(StringBuilder sb, ScreenWalker.LeagueTableItem t)
    {
        if (t.Rows == null || t.LoadFailed)
        {
            sb.Append("<p>").Append(ServerText.Escape(Speech.Degrade.Unavailable))
              .Append(" (league list could not be read; refresh)</p>");
            return;
        }

        string listPath = ServerHandles.IdentityPath(t.List);
        sb.Append("<table><thead><tr>")
          .Append("<th scope=\"col\">Nation</th>")
          .Append("<th scope=\"col\">Mode</th>")
          .Append("<th scope=\"col\">Men's Leagues</th>")
          .Append("<th scope=\"col\">Men's Setting</th>")
          .Append("<th scope=\"col\">Women's Leagues</th>")
          .Append("<th scope=\"col\">Women's Setting</th>")
          .Append("<th scope=\"col\">Remove</th>")
          .Append("</tr></thead><tbody>");
        foreach (var r in t.Rows)
        {
            if (r == null) continue;
            sb.Append("<tr>");
            sb.Append("<th scope=\"row\">").Append(ServerText.EscapeText(r.Nation ?? "")).Append("</th>");
            sb.Append("<td>").Append(ServerText.EscapeText(r.Mode ?? "")).Append("</td>");

            sb.Append("<td>");
            RenderLeagueDropdown(sb, t.List, listPath, r.DataIndex, "men-rule", r.MenRule ?? "", r.MenRuleEnabled);
            if (!string.IsNullOrEmpty(r.MenCount)) sb.Append(' ').Append(ServerText.EscapeText(r.MenCount));
            sb.Append("</td>");

            sb.Append("<td>");
            if (r.MenSetting) RenderLeagueDropdown(sb, t.List, listPath, r.DataIndex, "men-setting", r.MenSettingValue ?? "Men's setting", r.MenSettingEnabled);
            sb.Append("</td>");

            sb.Append("<td>");
            if (r.WomenRule != null)
            {
                RenderLeagueDropdown(sb, t.List, listPath, r.DataIndex, "women-rule", r.WomenRule, r.WomenRuleEnabled);
                if (!string.IsNullOrEmpty(r.WomenCount)) sb.Append(' ').Append(ServerText.EscapeText(r.WomenCount));
            }
            else
            {
                sb.Append(ServerText.EscapeText(r.WomenNone ?? "None"));
                if (!string.IsNullOrEmpty(r.WomenCount)) sb.Append(' ').Append(ServerText.EscapeText(r.WomenCount));
            }
            sb.Append("</td>");

            sb.Append("<td>");
            if (r.WomenSetting) RenderLeagueDropdown(sb, t.List, listPath, r.DataIndex, "women-setting", r.WomenSettingValue ?? "Women's setting", r.WomenSettingEnabled);
            sb.Append("</td>");

            sb.Append("<td>");
            if (r.HasDelete)
            {
                string id = ServerHandles.RegisterKey(
                    new ServerHandles.LeagueRowHandle { List = t.List, Row = r.DataIndex, ColumnKey = "remove" },
                    listPath + "@" + r.DataIndex + "#remove");
                sb.Append("<button type=\"button\" id=\"").Append(id)
                  .Append("\" onclick=\"act('").Append(id).Append("')\">Remove</button>");
            }
            sb.Append("</td>");

            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        if (t.Truncated)
            sb.Append("<p>Table truncated to the first ").Append(t.Rows.Count).Append(" nations.</p>");
    }

    // The Player Search condition list as one semantic table: Enabled | If/And | Condition |
    // Comparison | Value | Actions, one row per condition, the condition name as the row header.
    // Cell controls stay live by re-realising through ConditionRowHandle by data index + slot key
    // (rows recycle, so no element is held). The Value cell holds the type's visible controls —
    // one dropdown/stepper, or a min "and" max pair for range conditions. Rows without a reorder
    // cell (the Attributes view) render an empty Actions gap before Delete, keeping one column
    // layout for both views.
    private static void RenderConditionTable(StringBuilder sb, ScreenWalker.ConditionTableItem t)
    {
        if (t.Rows == null || t.LoadFailed)
        {
            sb.Append("<p>").Append(ServerText.Escape(Speech.Degrade.Unavailable))
              .Append(" (condition list could not be read; refresh)</p>");
            return;
        }
        if (t.Rows.Count == 0) return;

        string listPath = ServerHandles.IdentityPath(t.List);
        sb.Append("<table id=\"").Append(LiveValues.AllocateId(listPath + "#condition-table")).Append("\"><thead><tr>")
          .Append("<th scope=\"col\">Enabled</th>")
          .Append("<th scope=\"col\">If/And</th>")
          .Append("<th scope=\"col\">Condition</th>")
          .Append("<th scope=\"col\">Comparison</th>")
          .Append("<th scope=\"col\">Value</th>")
          .Append("<th scope=\"col\">Actions</th>")
          .Append("</tr></thead><tbody>");
        foreach (var r in t.Rows)
        {
            if (r == null) continue;
            string name = r.Name ?? "";

            sb.Append("<tr data-fm-row-context=\"").Append(LiveValues.AllocateId("condition-focus-context:" + r.FocusContext)).Append("\"><td>");
            if (r.Enabled is bool on)
            {
                string id = RegisterConditionHandle(t.List, listPath, r, "enabled");
                sb.Append("<input type=\"checkbox\" id=\"").Append(id)
                  .Append("\" data-fm-command-context=\"").Append(ConditionCommandKey(listPath, r, "enabled")).Append("\"")
                  .Append(on ? " checked" : "")
                  .Append(" aria-label=\"").Append(ServerText.EscapeText(name)).Append(" enabled\"")
                  .Append(" onclick=\"act('").Append(id).Append("')\">");
            }
            else
            {
                sb.Append(ServerText.Escape(Speech.Degrade.Unavailable));
            }
            sb.Append("</td>");

            sb.Append("<td>");
            if (r.ConnectorIsToggle)
            {
                string id = RegisterConditionHandle(t.List, listPath, r, "andor");
                sb.Append("<button type=\"button\" id=\"").Append(id)
                  .Append("\" data-fm-command-context=\"").Append(ConditionCommandKey(listPath, r, "andor"))
                  .Append("\" onclick=\"act('").Append(id).Append("')\">")
                  .Append(ServerText.EscapeText(r.Connector ?? "")).Append("</button>");
            }
            else if (!string.IsNullOrEmpty(r.Connector))
            {
                sb.Append(ServerText.EscapeText(r.Connector));
            }
            sb.Append("</td>");

            sb.Append("<th scope=\"row\">").Append(ServerText.EscapeText(name)).Append("</th>");

            sb.Append("<td>");
            if (r.Comparator != null && !r.ComparatorEnabled)
            {
                sb.Append("<select disabled aria-label=\"")
                  .Append(ServerText.EscapeText(name)).Append(" comparison\"><option selected>")
                  .Append(ServerText.EscapeText(r.Comparator)).Append("</option></select>");
            }
            else if (r.Comparator != null)
            {
                string id = RegisterConditionHandle(t.List, listPath, r, "compare");
                sb.Append("<select id=\"").Append(id).Append("\" data-fm-command-context=\"")
                  .Append(ConditionCommandKey(listPath, r, "compare")).Append("\" data-dd=\"1\" aria-label=\"")
                  .Append(ServerText.EscapeText(name)).Append(" comparison\"><option selected>")
                  .Append(ServerText.EscapeText(r.Comparator)).Append("</option></select>");
            }
            sb.Append("</td>");

            sb.Append("<td>");
            for (int i = 0; i < r.Values.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                RenderConditionValue(sb, t.List, listPath, r, r.Values[i]);
            }
            sb.Append("</td>");

            sb.Append("<td>");
            if (r.HasReorder)
            {
                RenderConditionActionButton(sb, t.List, listPath, r, "up", "Move up", r.CanMoveUp);
                sb.Append(' ');
                RenderConditionActionButton(sb, t.List, listPath, r, "down", "Move down", r.CanMoveDown);
                sb.Append(' ');
            }
            if (r.HasDelete)
                RenderConditionActionButton(sb, t.List, listPath, r, "delete", "Delete", enabled: true);
            sb.Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
        if (t.Truncated)
            sb.Append("<p>Table truncated to the first ").Append(t.Rows.Count).Append(" conditions.</p>");
    }

    // One element of a condition row's Value cell: a dropdown (and a combobox, which opens its
    // internal choice dropdown — its own +/- buttons ignore simulated clicks on this panel) opens
    // a popup through the handle; an edit box takes typed text; plain text (the "and" between a
    // range's bounds) renders as-is.
    private static void RenderConditionValue(StringBuilder sb, VisualElement list, string listPath,
        Contexts.ConditionTableContext.Row r, Contexts.ConditionTableContext.ValuePart part)
    {
        string name = r.Name ?? "";
        switch (part.Kind)
        {
            case "dropdown":
            case "combobox":
            {
                // Greyed out in-game: no handle, no data-dd, the native disabled attribute. An
                // openable select here would announce an operability the open would refuse.
                if (!part.Enabled)
                {
                    sb.Append("<select disabled aria-label=\"")
                      .Append(ServerText.EscapeText(name)).Append(" value\"><option selected>")
                      .Append(ServerText.EscapeText(part.Value ?? "")).Append("</option></select>");
                    break;
                }
                string id = RegisterConditionHandle(list, listPath, r, part.SlotKey);
                sb.Append("<select id=\"").Append(id).Append("\" data-fm-command-context=\"")
                  .Append(ConditionCommandKey(listPath, r, part.SlotKey)).Append("\" data-dd=\"1\" aria-label=\"")
                  .Append(ServerText.EscapeText(name)).Append(" value\"><option selected>")
                  .Append(ServerText.EscapeText(part.Value ?? "")).Append("</option></select>");
                if (part.Kind == "combobox")
                {
                    sb.Append(' ');
                    RenderComboboxStepButtons(sb, id, part.CanDecrement, part.CanIncrement, true);
                }
                break;
            }
            case "date":
            {
                if (!part.Enabled) { sb.Append(ServerText.EscapeText(part.Value ?? "")); break; }
                string id = RegisterConditionHandle(list, listPath, r, part.SlotKey);
                DateField.RenderControl(sb, id, part.Value ?? "", name, ConditionCommandKey(listPath, r, part.SlotKey), paragraph: false);
                break;
            }
            case "textfield":
            {
                if (!part.Enabled)
                {
                    sb.Append("<input type=\"text\" disabled aria-label=\"")
                      .Append(ServerText.EscapeText(name)).Append(" value\" value=\"")
                      .Append(ServerText.EscapeText(part.Value ?? "")).Append("\">");
                    break;
                }
                string id = RegisterConditionHandle(list, listPath, r, part.SlotKey);
                sb.Append("<input type=\"text\" id=\"").Append(id)
                  .Append("\" data-fm-command-context=\"").Append(ConditionCommandKey(listPath, r, part.SlotKey))
                  .Append("\" aria-label=\"").Append(ServerText.EscapeText(name)).Append(" value\"")
                  .Append(" value=\"").Append(ServerText.EscapeText(part.Value ?? ""))
                  .Append("\" onchange=\"settext('").Append(id).Append("',this.value)\">");
                break;
            }
            default:
                sb.Append(ServerText.EscapeText(part.Value ?? ""));
                break;
        }
    }

    private static void RenderConditionActionButton(StringBuilder sb, VisualElement list, string listPath,
        Contexts.ConditionTableContext.Row row, string key, string label, bool enabled)
    {
        if (!enabled)
        {
            sb.Append("<button type=\"button\" disabled>").Append(label).Append("</button>");
            return;
        }
        string id = RegisterConditionHandle(list, listPath, row, key);
        sb.Append("<button type=\"button\" id=\"").Append(id)
          .Append("\" data-fm-command-context=\"").Append(ConditionCommandKey(listPath, row, key))
          .Append("\" data-fm-row-action=\"").Append(key).Append("\" onclick=\"act('").Append(id).Append("')\">").Append(label).Append("</button>");
    }

    private static string RegisterConditionHandle(VisualElement list, string listPath, Contexts.ConditionTableContext.Row row, string key)
        => ServerHandles.RegisterKey(
            new ServerHandles.ConditionRowHandle { List = list, Row = row.DataIndex, ColumnKey = key, NativeIdentity = row.NativeIdentity, RuleContext = row.CommandContext, ClientContext = ConditionCommandKey(listPath, row, key) },
            listPath + "@condition:" + row.NativeIdentity + "#" + key);

    private static string ConditionCommandKey(string listPath, Contexts.ConditionTableContext.Row row, string key)
        => System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            listPath + "@condition-context:" + row.NativeIdentity + "|" + row.CommandContext + "#" + key)));

    // A league cell's dropdown as a native <select> whose open resolves back to the live control by
    // data index (LeagueRowHandle re-realises the recycled row). The current value is the accessible
    // name (the rule text, or the "…setting" label for the icon-only setting picker); options stream
    // in on open from /dropdown/open.
    private static void RenderLeagueDropdown(StringBuilder sb, VisualElement list, string listPath, int dataIndex, string key, string label, bool enabled)
    {
        // Greyed out in-game: no handle, no data-dd, the native disabled attribute, so the open is
        // never attempted on a control the game would refuse.
        if (!enabled)
        {
            sb.Append("<select disabled aria-label=\"")
              .Append(ServerText.EscapeText(label)).Append("\"><option selected></option></select>");
            return;
        }
        string id = ServerHandles.RegisterKey(
            new ServerHandles.LeagueRowHandle { List = list, Row = dataIndex, ColumnKey = key },
            listPath + "@" + dataIndex + "#" + key);
        sb.Append("<select id=\"").Append(id).Append("\" data-dd=\"1\" aria-label=\"")
          .Append(ServerText.EscapeText(label)).Append("\"><option selected></option></select>");
    }

    // The Calendar tool's Fixture Schedule: the season's streamed rows split into a "Played
    // fixtures" table (rows with a result) and an "Upcoming fixtures" table, each under its own
    // heading. Columns follow a fixed presentation layout rather than the game's raw order —
    // selection, date+time merged, "venue vs opponent" merged, the result (W/D/L from the game's
    // badge class, then the score button), competition, any column this layout doesn't know, and
    // TV last; the upcoming table drops the always-empty result column. Headers keep the game's own
    // sort buttons (per-table handle keys so the two tables' ids don't collide); plain cells are
    // live game text; interactive cells dispatch through FixtureRowHandle by data index. A failed
    // load renders an honest marker instead of a silently empty table.
    private static void RenderFixtureSchedule(StringBuilder sb, ScreenWalker.FixtureScheduleItem f)
    {
        string listPath = ServerHandles.IdentityPath(f.List);
        var cols = new ScheduleColumns(f);

        var played = new List<Contexts.FixtureScheduleContext.Row>();
        var upcoming = new List<Contexts.FixtureScheduleContext.Row>();
        if (f.Rows != null)
        {
            foreach (var row in f.Rows)
            {
                if (row == null) continue; // a row that recycled between settle and read
                var res = CellAt(row, cols.Result);
                (res != null && !string.IsNullOrEmpty(res.Text) ? played : upcoming).Add(row);
            }
        }

        if (cols.Result < 0)
        {
            // No result column to split on: one table, no played/upcoming claim.
            RenderScheduleTable(sb, f, cols, upcoming, listPath, "a", includeResult: false);
        }
        else if (played.Count == 0 && upcoming.Count == 0)
        {
            // Nothing loaded (an empty season, or a failed load — the marker below says which).
            RenderScheduleTable(sb, f, cols, played, listPath, "a", includeResult: true);
        }
        else
        {
            if (played.Count > 0)
            {
                sb.Append("<h4>Played fixtures</h4>");
                RenderScheduleTable(sb, f, cols, played, listPath, "p", includeResult: true);
            }
            if (upcoming.Count > 0)
            {
                sb.Append("<h4>Upcoming fixtures</h4>");
                RenderScheduleTable(sb, f, cols, upcoming, listPath, "u", includeResult: false);
            }
        }

        if (f.LoadFailed)
            sb.Append("<p>").Append(ServerText.Escape(Degrade.Unreadable)).Append(" fixture list</p>");
        // The schedule is read whole, so this is not a page boundary — it fires only when the read
        // ran out of budget, which means fixtures really are missing from both tables above.
        if (f.Truncated)
            sb.Append("<p>Fixture list incomplete: only the first ")
              .Append(played.Count + upcoming.Count).Append(" fixtures could be read.</p>");
    }

    // The fixed presentation layout's column indices into the walked headers/cells; -1 when the
    // game's header row lacks that column. Extras are columns the layout doesn't know, kept (before
    // TV) so a template change never silently drops data.
    private sealed class ScheduleColumns
    {
        public readonly int Sel, Tv, Date, Time, Opposition, Venue, Result, Competition;
        public readonly List<int> Extras = new();

        public ScheduleColumns(ScreenWalker.FixtureScheduleItem f)
        {
            Sel = IndexOf(f, "Selected"); Tv = IndexOf(f, "TV");
            Date = IndexOf(f, "Date"); Time = IndexOf(f, "Time");
            Opposition = IndexOf(f, "Opposition"); Venue = IndexOf(f, "Venue");
            Result = IndexOf(f, "Result"); Competition = IndexOf(f, "Competition");
            var known = new HashSet<int> { Sel, Tv, Date, Time, Opposition, Venue, Result, Competition };
            for (int i = 0; i < f.Headers.Count; i++)
                if (!known.Contains(i)) Extras.Add(i);
        }

        private static int IndexOf(ScreenWalker.FixtureScheduleItem f, string key)
        {
            for (int i = 0; i < f.Headers.Count; i++)
                if (f.Headers[i].Key == key) return i;
            return -1;
        }
    }

    private static void RenderScheduleTable(StringBuilder sb, ScreenWalker.FixtureScheduleItem f,
        ScheduleColumns cols, List<Contexts.FixtureScheduleContext.Row> rows, string listPath,
        string suffix, bool includeResult)
    {
        sb.Append("<table><thead><tr>");
        RenderScheduleTh(sb, f, listPath, suffix, cols.Sel);
        RenderScheduleTh(sb, f, listPath, suffix, cols.Date, cols.Time);
        RenderScheduleTh(sb, f, listPath, suffix, cols.Venue, cols.Opposition);
        if (includeResult) RenderScheduleTh(sb, f, listPath, suffix, cols.Result);
        RenderScheduleTh(sb, f, listPath, suffix, cols.Competition);
        foreach (var e in cols.Extras) RenderScheduleTh(sb, f, listPath, suffix, e);
        RenderScheduleTh(sb, f, listPath, suffix, cols.Tv);
        sb.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            sb.Append("<tr>");

            if (cols.Sel >= 0)
            {
                sb.Append("<td>");
                RenderScheduleSelector(sb, f, row, CellAt(row, cols.Sel), ScheduleRowLabel(row, cols), listPath);
                sb.Append("</td>");
            }

            if (cols.Date >= 0 || cols.Time >= 0)
            {
                var parts = new List<string>(2);
                string date = TextAt(row, cols.Date), time = TextAt(row, cols.Time);
                if (!string.IsNullOrEmpty(date)) parts.Add(date);
                if (!string.IsNullOrEmpty(time)) parts.Add(time);
                sb.Append("<td>").Append(ServerText.EscapeText(string.Join(", ", parts))).Append("</td>");
            }

            if (cols.Venue >= 0 || cols.Opposition >= 0)
            {
                sb.Append("<td>");
                string venue = TextAt(row, cols.Venue);
                bool hasOpp = !string.IsNullOrEmpty(TextAt(row, cols.Opposition));
                if (!string.IsNullOrEmpty(venue))
                {
                    sb.Append(ServerText.EscapeText(venue));
                    if (hasOpp) sb.Append(" vs ");
                }
                RenderScheduleContent(sb, f, row, cols.Opposition, listPath);
                sb.Append("</td>");
            }

            if (includeResult && cols.Result >= 0)
            {
                sb.Append("<td>");
                var res = CellAt(row, cols.Result);
                // The W/D/L letter carries the outcome a sighted player reads from the badge's
                // colour; absent (unread/unmapped class) the score still stands on its own.
                if (res != null && !string.IsNullOrEmpty(res.Outcome))
                    sb.Append(ServerText.Escape(res.Outcome)).Append(' ');
                RenderScheduleContent(sb, f, row, cols.Result, listPath);
                sb.Append("</td>");
            }

            if (cols.Competition >= 0)
            {
                sb.Append("<td>");
                RenderScheduleContent(sb, f, row, cols.Competition, listPath);
                sb.Append("</td>");
            }

            foreach (var e in cols.Extras)
            {
                sb.Append("<td>");
                RenderScheduleContent(sb, f, row, e, listPath);
                sb.Append("</td>");
            }

            if (cols.Tv >= 0)
            {
                // A televised fixture shows the column's own label as the cell text; an untelevised
                // one stays empty, like the game's blank cell.
                var tv = CellAt(row, cols.Tv);
                sb.Append("<td>");
                if (tv != null && tv.On) sb.Append(ServerText.EscapeText(f.Headers[cols.Tv].Text));
                sb.Append("</td>");
            }

            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    // A header cell, merging one or more of the game's columns into one <th> that keeps every
    // merged column's own sort button ("Date" and "Time" stay separately sortable inside the
    // combined column). Skipped entirely when none of the columns exist. aria-sort comes from the
    // first merged column reporting one. Handle keys carry the per-table suffix so the played and
    // upcoming tables' header ids never collide.
    private static void RenderScheduleTh(StringBuilder sb, ScreenWalker.FixtureScheduleItem f,
        string listPath, string suffix, params int[] colIndices)
    {
        var valid = new List<int>(colIndices.Length);
        foreach (var c in colIndices)
            if (c >= 0 && c < f.Headers.Count) valid.Add(c);
        if (valid.Count == 0) return;

        sb.Append("<th scope=\"col\"");
        foreach (var c in valid)
        {
            string aria = AriaSort(f.Headers[c].SortState);
            if (aria != null) { sb.Append(" aria-sort=\"").Append(aria).Append("\""); break; }
        }
        sb.Append(">");
        for (int i = 0; i < valid.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            var h = f.Headers[valid[i]];
            var headerAdapter = h.Button == null ? null : new ColumnHeaderAdapter(h.Button);
            if (headerAdapter != null && (headerAdapter.HasPrimaryClick || h.HasContextMenu))
            {
                string id = ServerHandles.RegisterKey(
                    new ServerHandles.HeaderHandle { Header = headerAdapter },
                    listPath + "#" + suffix + "h" + valid[i]);
                sb.Append("<button type=\"button\" id=\"").Append(id)
                  .Append("\" onclick=\"").Append(headerAdapter.HasPrimaryClick ? "act('" : "FM.contextmenu.open('")
                  .Append(id).Append("')\"");
                if (ServerText.IsDisabled(headerAdapter)) sb.Append(" disabled");
                AppendContextMenuAttr(sb, h.HasContextMenu);
                sb.Append(">").Append(ServerText.EscapeText(h.Text)).Append("</button>");
            }
            else
            {
                sb.Append(ServerText.EscapeText(h.Text));
            }
        }
        sb.Append("</th>");
    }

    // The row selector. The game's widget is an SICheckBox, but it presents and behaves as a
    // single-select radio (one filled circle per list, selection drives the fixture preview card),
    // and activation goes through the list's single-selection API — so a native radio with a
    // shared per-list name is the truthful control. Named after its row (the game's control is a
    // bare template whose own label resolution leaks whatever text sits nearby).
    private static void RenderScheduleSelector(StringBuilder sb, ScreenWalker.FixtureScheduleItem f,
        Contexts.FixtureScheduleContext.Row row, Contexts.FixtureScheduleContext.Cell cell,
        string rowLabel, string listPath)
    {
        if (cell == null || cell.Kind != Contexts.FixtureScheduleContext.CellKind.Checkbox) return;
        string id = ServerHandles.RegisterKey(
            new ServerHandles.FixtureRowHandle { List = f.List, Row = row.DataIndex, ColumnKey = "Selected", NativeIdentity = row.NativeIdentity },
            listPath + "@fixture:" + row.NativeIdentity + "#Selected");
        sb.Append("<input type=\"radio\" id=\"").Append(id).Append("\"");
        // One selection group spanning the played and upcoming tables — they are one game list.
        sb.Append(" name=\"fmfx").Append((uint)listPath.GetHashCode()).Append("\"");
        if (cell.On) sb.Append(" checked");
        if (!string.IsNullOrEmpty(rowLabel))
            sb.Append(" aria-label=\"").Append(ServerText.EscapeText(rowLabel)).Append("\"");
        sb.Append(" onclick=\"act('").Append(id).Append("')\">");
    }

    // A content cell body: the cell's button (with its context-menu mark) when it is clickable and
    // carries text, otherwise its plain text. Emits nothing for a missing column.
    private static void RenderScheduleContent(StringBuilder sb, ScreenWalker.FixtureScheduleItem f,
        Contexts.FixtureScheduleContext.Row row, int col, string listPath)
    {
        var cell = CellAt(row, col);
        if (cell == null) return;
        if (cell.Clickable && !string.IsNullOrEmpty(cell.Text))
        {
            string key = f.Headers[col].Key;
            string id = ServerHandles.RegisterKey(
                new ServerHandles.FixtureRowHandle { List = f.List, Row = row.DataIndex, ColumnKey = key, NativeIdentity = row.NativeIdentity },
                listPath + "@fixture:" + row.NativeIdentity + "#" + key);
            sb.Append("<button type=\"button\" id=\"").Append(id)
              .Append("\" onclick=\"act('").Append(id).Append("')\"");
            AppendContextMenuAttr(sb, cell.HasMenu);
            sb.Append(">").Append(ServerText.EscapeText(cell.Text)).Append("</button>");
        }
        else
        {
            sb.Append(ServerText.EscapeText(cell.Text));
        }
    }

    private static Contexts.FixtureScheduleContext.Cell CellAt(Contexts.FixtureScheduleContext.Row row, int col)
        => col >= 0 && col < row.Cells.Count ? row.Cells[col] : null;

    private static string TextAt(Contexts.FixtureScheduleContext.Row row, int col)
        => CellAt(row, col)?.Text ?? "";

    // The identifying text for a fixture row's otherwise-unlabeled selection checkbox: its date and
    // opposition ("Sat 8 Mar 2025, Sigma FC"). Raw game text; EscapeText cleans and escapes at emit.
    private static string ScheduleRowLabel(Contexts.FixtureScheduleContext.Row row, ScheduleColumns cols)
    {
        var parts = new List<string>(2);
        string date = TextAt(row, cols.Date), opp = TextAt(row, cols.Opposition);
        if (!string.IsNullOrEmpty(date)) parts.Add(date);
        if (!string.IsNullOrEmpty(opp)) parts.Add(opp);
        return string.Join(", ", parts);
    }

    // Marks an enabled control that carries a right-click context menu so the screen reader announces
    // "has popup menu" and the companion's contextmenu handler (Shift+F10 / Applications key) knows to
    // route Shift+F10 to /contextmenu instead of letting the browser show its own menu. The id is
    // already on the element, so the handler reads it from the focused node.
    private static void AppendContextMenuAttr(StringBuilder sb, bool hasContextMenu)
    {
        if (hasContextMenu) sb.Append(" aria-haspopup=\"menu\"");
    }

    // The match tile's per-match controls are icon-only, so their adapters fall back to the kickoff
    // time. Give them a clear accessible name on the companion: the action dropdown (its options are
    // No action / Scout match / Attend match) reads "Match action"; the CanAttend checkbox reads
    // "Attend match". Other controls (e.g. a view-match button) keep their own label. Companion-only
    // (the in-game speech cursor is unaffected); null means "use the control's normal label".
    private static string FixtureOptionLabel(IAccessibleElement opt)
    {
        if (opt is Adapters.DropdownAdapter) return "Match action";
        if (opt is Adapters.CheckBoxAdapter || opt is Adapters.ToggleAdapter) return "Attend match";
        return null;
    }

    // A team cell: the clickable team button (registered like any other stop so act() and live-value
    // focus restore work) followed by its league position in parentheses when present. A null adapter
    // (team unreadable) degrades to an honest marker rather than an empty or wrong cell.
    // A team cell that may hold several candidate teams (a stage tie's undecided side): the
    // candidates render "/"-separated, each still its own clickable button, with the optional
    // caption ("Winner of ...") after them all. An ordinary single-team row passes through.
    private static void RenderTeamCellWithExtras(StringBuilder sb, IAccessibleElement team,
        List<IAccessibleElement> extras, string position)
    {
        if (extras == null || extras.Count == 0)
        {
            RenderTeamCell(sb, team, position);
            return;
        }
        RenderTeamCell(sb, team, null);
        foreach (var extra in extras)
        {
            sb.Append(" / ");
            RenderTeamCell(sb, extra, null);
        }
        if (!string.IsNullOrEmpty(position))
            sb.Append(" (").Append(ServerText.EscapeText(position)).Append(")");
    }

    private static void RenderTeamCell(StringBuilder sb, IAccessibleElement team, string position)
    {
        if (team == null)
        {
            sb.Append(ServerText.EscapeText(Degrade.Unreadable));
        }
        else
        {
            bool disabled = ServerText.IsDisabled(team);
            string id = disabled
                ? LiveValues.RegisterReadOnly(team)
                : ServerHandles.Register(new ServerHandles.StopHandle { El = team }, team);
            if (!disabled) LiveValues.Track(id, team);
            sb.Append("<button type=\"button\" id=\"").Append(id).Append("\"");
            if (disabled) sb.Append(" disabled");
            else { sb.Append(" onclick=\"act('").Append(id).Append("')\""); AppendContextMenuAttr(sb, team.HasContextMenu); }
            sb.Append(">").Append(ServerText.Escape(DisplayText.Of(team))).Append("</button>");
        }
        if (!string.IsNullOrEmpty(position))
            sb.Append(" (").Append(ServerText.EscapeText(position)).Append(")");
    }

    // The combined goalscorers cell, each scoring side prefixed by its team name so the column reads
    // unambiguously when jumped to directly: "Scotland: Ryan Christie 28', John McGinn 48'". Empty
    // (the screen reader skips it) when neither side has a listed scorer.
    private static void RenderScorers(StringBuilder sb, ScreenWalker.MatchRow m)
    {
        var parts = new List<string>(2);
        AppendSideScorers(parts, m.HomeTeam, m.HomeScorers);
        AppendSideScorers(parts, m.AwayTeam, m.AwayScorers);
        sb.Append(string.Join("; ", parts));
    }

    private static void AppendSideScorers(List<string> parts, IAccessibleElement team, List<(string Name, string Minute)> scorers)
    {
        if (scorers == null || scorers.Count == 0) return;
        var goals = new List<string>(scorers.Count);
        foreach (var (name, minute) in scorers)
        {
            string g = ServerText.EscapeText(name);
            if (!string.IsNullOrEmpty(minute)) g += " " + ServerText.EscapeText(minute);
            goals.Add(g);
        }
        string prefix = team != null ? ServerText.Escape(DisplayText.Of(team)) : ServerText.EscapeText(Degrade.Unreadable);
        parts.Add(prefix + ": " + string.Join(", ", goals));
    }
}
