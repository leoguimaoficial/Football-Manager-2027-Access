using System;
using System.Collections.Generic;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Plugin.Contexts;

namespace FM27Access.Plugin.Server;

/// <summary>
/// A linearized, screen-reader-style text rendering of the whole interactive screen as one block -
/// every interactive panel the gate (<see cref="InteractivePanels"/>) reports, each as its own
/// section, exactly the set /body renders. This is what the companion app presents, in reading
/// order, as plain text instead of HTML. The point is to audit menus: pull one block per screen and
/// judge whether it reads logically and completely, without scanning raw /body HTML or interacting
/// per control.
///
/// Built from the SAME pipeline as /body: ScreenWalker's RenderItems, streamed tables materialized
/// by StreamedTableLoader. So it shows exactly what the companion shows (a table showing the same
/// page of the same <see cref="StreamedTableLoader.PageRows"/> rows, named in the read; a dropdown's
/// current value, with its option list noted as fetched on demand). The per-control phrasing mirrors HtmlRenderer.RenderNode's role decisions.
///
/// Runs the three phases on the request thread like AccessibilityServer.ComposeBody (walk on the
/// main thread, load tables across frames, render on the main thread) - NOT inside a single
/// main-thread Invoke, because the table load sleeps between frames.
/// </summary>
internal static class ReadingView
{
    public static string Compose()
    {
        var sections = MainThreadDispatcher.Invoke(() =>
        {
            var panels = InteractivePanels.Current();
            // This diagnostic observes the existing page. BeginBody would close
            // its native dropdown, clear its command/value registrations and
            // reset editable-pitch state without replacing the companion DOM.
            var active = NavigationManager.GetActivePanel();

            var built = new List<(object Panel, string Title, List<ScreenWalker.RenderItem> Items)>();
            foreach (var pv in panels)
            {
                // Each panel walks under its own profile (hide rules differ per panel); restore the
                // speech panel's profile afterwards, exactly as AccessibilityServer.ComposeBody does.
                AccessibilityServer.ActivateProfileFor(pv.Panel);
                built.Add((pv.Panel, pv.Title, HtmlRenderer.WalkPanel(pv.Panel)));
            }
            AccessibilityServer.ActivateProfileFor(active);
            return built;
        }, 10000);

        // Like ComposeBody's streamed path: each section's loads run under that section's own
        // profile, because streamed rows bake their cell labels at read time and would otherwise
        // resolve renames/hides against the speech panel's profile.
        foreach (var (panel, _, items) in sections)
        {
            bool activated = false;
            foreach (var item in items)
            {
                bool streamed = item is ScreenWalker.TableItem { Context: not null }
                    or ScreenWalker.FixtureScheduleItem or ScreenWalker.ConditionTableItem
                    or ScreenWalker.ResponsibilitiesTableItem or ScreenWalker.StaffDutiesTableItem;
                if (!streamed) continue;
                if (!activated)
                {
                    MainThreadDispatcher.Invoke(() => { AccessibilityServer.ActivateProfileFor(panel); return true; }, 10000);
                    activated = true;
                }
                if (item is ScreenWalker.TableItem t && t.Context != null)
                    StreamedTableLoader.Load(t);
                else if (item is ScreenWalker.FixtureScheduleItem fs)
                    FixtureScheduleLoader.Load(fs);
                else if (item is ScreenWalker.ConditionTableItem ct)
                    ConditionTableLoader.Load(ct);
                else if (item is ScreenWalker.ResponsibilitiesTableItem rt)
                    ResponsibilitiesLoader.Load(rt);
                else if (item is ScreenWalker.StaffDutiesTableItem sd)
                    ResponsibilitiesLoader.LoadStaffDuties(sd);
            }
        }

        return MainThreadDispatcher.Invoke(() =>
        {
            // The same post-load consolidations /body runs. Without them this view disagreed with
            // the companion on everything that can only be joined once the streamed rows exist —
            // most visibly a named multi-line chart, which read here as an unreadable one.
            foreach (var (panel, _, items) in sections)
                ScreenWalker.RestructureLoadedTables(items, panel);
            return RenderSections(sections);
        }, 10000);
    }

    // Render each interactive panel as its own block. Mirrors ComposeBody's per-section profile
    // re-activation: the global profile is shared with speech and other frames ran between the walk
    // and this render, so re-activate each panel's profile before reading its items' text, then
    // restore the speech panel's profile.
    private static string RenderSections(List<(object Panel, string Title, List<ScreenWalker.RenderItem> Items)> sections)
    {
        var active = NavigationManager.GetActivePanel();
        var sb = new StringBuilder();
        if (sections.Count == 0)
        {
            sb.Append("(no navigable content on this screen)\n");
            return sb.ToString();
        }

        foreach (var (panel, title, items) in sections)
        {
            AccessibilityServer.ActivateProfileFor(panel);
            sb.Append("# ").Append(title).Append('\n');
            if (items.Count == 0)
                sb.Append("(no navigable content in this panel)\n");
            else
                RenderItems(sb, items);
        }
        AccessibilityServer.ActivateProfileFor(active);
        return sb.ToString();
    }

    private static void RenderItems(StringBuilder sb, List<ScreenWalker.RenderItem> items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case ScreenWalker.StopItem s: sb.Append(StopLine(s.Element)).Append('\n'); break;
                case ScreenWalker.TextListItem list:
                    if (list.Rows != null)
                    {
                        foreach (var row in list.Rows) { sb.Append("- "); RenderItems(sb, row); }
                        break;
                    }
                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        sb.Append("- ").Append(DisplayText.Of(list.Items[i]));
                        var trailing = list.TrailingAt(i);
                        if (trailing != null)
                            foreach (var extra in trailing) sb.Append(' ').Append(DisplayText.Of(extra));
                        sb.Append('\n');
                    }
                    break;
                case ScreenWalker.JoinedTextItem joined:
                    for (int i = 0; i < joined.Parts.Count; i++)
                    {
                        if (i > 0) sb.Append(' ');
                        sb.Append(StopLine(joined.Parts[i]));
                        if (joined.LeadingCaption && i == 0) sb.Append(':');
                    }
                    sb.Append('\n');
                    break;
                case ScreenWalker.TableItem t: RenderTable(sb, t); break;
                case ScreenWalker.FixtureScheduleItem fs: RenderFixtureSchedule(sb, fs); break;
                case ScreenWalker.ConditionTableItem ct: RenderConditionTable(sb, ct); break;
                case ScreenWalker.ContractTermsItem cterms: RenderContractTerms(sb, cterms); break;
                case ScreenWalker.ContractConditionsItem cconds: RenderContractConditions(sb, cconds); break;
                case ScreenWalker.ScoreboardItem sc:
                    if (!string.IsNullOrEmpty(sc.Line)) sb.Append("Score: ").Append(sc.Line).Append('\n');
                    break;
                case ScreenWalker.TabBarItem tb: RenderTabBar(sb, tb); break;
                case ScreenWalker.RadioGroupItem rg: RenderRadioGroup(sb, rg); break;
                case ScreenWalker.HeadingItem h: sb.Append("## ").Append(h.Text).Append('\n'); break;
                // StopLine, not bare text: a folded value can be a live button (the transfer offer's
                // Club row), and this view has to show that it is one.
                case ScreenWalker.LabelValueItem lv:
                    sb.Append(lv.Label).Append(": ").Append(StopLine(lv.Value)).Append('\n');
                    break;
                case ScreenWalker.NoteItem n: sb.Append(n.Text).Append('\n'); break;
                case ScreenWalker.SyntheticTableItem syn:
                    RenderStringGrid(sb, syn.Headers, syn.Rows);
                    break;
                // Mirrors HtmlRenderer.RenderFocusList: the focus master list as a table, with the
                // selection shown per row the way the radiogroup view shows it.
                case ScreenWalker.FocusListItem fl:
                    if (fl.Rows.Count == 0) { sb.Append(ScreenWalker.FocusListItem.EmptyNote).Append('\n'); break; }
                    RenderStringGrid(sb, ScreenWalker.FocusListItem.Headers,
                        fl.Rows.ConvertAll(r => new List<string>
                        {
                            ServerText.State(r.Radio) == "selected" ? "(x)" : "( )",
                            r.Name, r.Status, r.Matched,
                        }));
                    break;
                // Mirrors HtmlRenderer.RenderScoutFocusList, including the empty-state line —
                // an empty focus section is the case worth seeing in the dev view too.
                case ScreenWalker.ScoutFocusListItem sfl:
                    if (sfl.Rows.Count == 0) { sb.Append(ScreenWalker.ScoutFocusListItem.EmptyNote).Append('\n'); break; }
                    RenderStringGrid(sb, ScreenWalker.ScoutFocusListItem.Headers,
                        sfl.Rows.ConvertAll(r => new List<string>
                        {
                            r.Name, StopLine(r.Edit), StopLine(r.Delete),
                        }));
                    break;
                // Mirrors HtmlRenderer.RenderValueChart. Without this case the finance history
                // tables were dropped from /read entirely, so the dev view disagreed with what the
                // player's companion actually showed.
                case ScreenWalker.ValueChartItem vc:
                    if (!string.IsNullOrEmpty(vc.Title)) sb.Append("### ").Append(vc.Title).Append('\n');
                    RenderValueChart(sb, vc);
                    break;
                case ScreenWalker.TileActivatorItem ta:
                    sb.Append("### ").Append(ta.Title);
                    if (!ServerText.IsDisabled(new Adapters.ButtonAdapter(ta.TileButton))) sb.Append(", button");
                    sb.Append('\n');
                    break;
                case ScreenWalker.StatsToggleItem st:
                    sb.Append(DisplayText.Of(st.El)).Append(", button, ")
                      .Append(st.Expanded ? "expanded" : "collapsed").Append('\n');
                    break;
                // Mirrors HtmlRenderer.RenderCarouselTile: the tile title as a heading that opens
                // the tile, then whichever reading its content supported.
                case ScreenWalker.CarouselTileItem car:
                    sb.Append("### ").Append(car.Title);
                    if (car.TileButton != null && !ServerText.IsDisabled(new Adapters.ButtonAdapter(car.TileButton))) sb.Append(", button");
                    sb.Append('\n');
                    RenderCarouselTileBody(sb, car);
                    foreach (var page in car.OtherPages)
                        sb.Append("Show: ").Append(page.Title).Append(", button\n");
                    break;
                // The position picker reads back as its grid: each toggle prefixed with the slot
                // phrase the table's row and column headers carry ("Defence centre right: D (C)"),
                // so the dev view shows the same distinctions the player gets.
                case ScreenWalker.PositionGridItem pgd:
                    foreach (var slot in AllSlotsInReadingOrder())
                        if (pgd.BySlot.TryGetValue(slot, out var toggle))
                            sb.Append(PitchNavigation.SlotPhrase(slot) ?? slot).Append(": ")
                              .Append(StopLine(toggle)).Append('\n');
                    break;
                case ScreenWalker.PitchGridItem pg:
                    foreach (var p in pg.Formation.Players)
                    {
                        if (pg.Editable != null && p.Slot != null && pg.Editable.TryGetValue(p.Slot, out var tp))
                            sb.Append(StopLine(tp)).Append('\n');
                        else
                            sb.Append(PitchFormation.Describe(p)).Append('\n');
                    }
                    break;
            }
        }
    }

    // The 24 slots back to front, left to right — the same order the position grid's rows and cells
    // render in, so the two views cannot drift.
    private static IEnumerable<string> AllSlotsInReadingOrder()
    {
        foreach (var row in PitchNavigation.Rows)
            foreach (var slot in row)
                yield return slot;
    }

    // The screen reader's reading of one stop, mirroring HtmlRenderer.RenderNode's role/label/state
    // decisions in text. Uses DisplayText.Of (the companion's label, renames applied) so this matches
    // what the player hears browsing the companion, not the raw speech-surface announcement.
    private static string StopLine(IAccessibleElement el)
    {
        if (ServerText.IsHeading(el))
        {
            // Mirror HtmlRenderer's tag depth: ### for a level-3+ profile rule (an <h4>
            // per-item title), ## for everything else (<h3>). An interactive button heading
            // keeps its control (HtmlRenderer wraps the button in the heading tag).
            string marks = ServerText.HeadingLevel(el) >= 3 ? "###" : "##";
            string line = marks + " " + DisplayText.Of(el);
            if (ServerText.SafeRole(el) == "button" && !ServerText.IsDisabled(el))
                line += ", button";
            return line;
        }

        string label = DisplayText.Of(el);
        string state = ServerText.State(el);
        string dis = ServerText.IsDisabled(el) ? ", unavailable" : "";

        switch (ServerText.SafeRole(el))
        {
            case "button":
                return label + ", button" + dis;
            case "checkbox":
            case "toggle":
                return label + ", checkbox, " + (IsOn(state) ? "checked" : "unchecked") + dis;
            case "radio button":
                return label + ", radio button, " + (state == "selected" ? "selected" : "not selected") + dis;
            case "text field":
            {
                string val = el is Adapters.TextFieldAdapter tf ? tf.GetCurrentText() : state;
                return (string.IsNullOrEmpty(val) ? "(empty)" : val) + ", edit text" + dis;
            }
            case "slider":
                return label + ", slider, " + state + dis;
            case "combobox":
                return label + ", combobox (stepper)" + dis;
            case "dropdown":
                // Mirror HtmlRenderer's <select>: the aria-label is the clean name (RenameOr ?? Label,
                // NOT DisplayText.Of, which for a dropdown already bakes in role+count) and the option
                // is the state, so the screen reader reads "name, combobox, value". Clean is what makes
                // both match HtmlRenderer's EscapeText: RenameOr/Label and the state are raw, so a
                // person or competition name carrying <color>/<link>/<style> markup would otherwise
                // leak into this audit view.
                return Clean(ServerText.RenameOr(el, null) ?? ServerText.Label(el)) + ", combobox, " + Clean(state)
                     + " [options on demand: /dropdown/open]" + dis;
            case "searchable dropdown":
                return Clean(ServerText.RenameOr(el, null) ?? ServerText.Label(el)) + ", searchable combobox, " + Clean(state)
                     + " [options on demand: /searchdropdown]" + dis;
            case "progress bar":
                return label + ", progress, " + state;
            case "number field":
                // HtmlRenderer labels a number field with the raw backing name, not DisplayText.Of
                // (which would bake in role/state); match it.
                return ServerText.BackingNameOr(el, "Number field") + (string.IsNullOrEmpty(state) ? "" : ", " + state);
            default:
                return label;
        }
    }

    private static bool IsOn(string state) => state == "checked" || state == "on" || state == "selected";

    // Strip rich-text/TMP markup the same way /body does (EscapeText = Escape(CleanForDisplay)),
    // minus the HTML escaping, since this surface is plain text. Cell text and headers carry raw
    // <color>/<link>/<style> tags otherwise.
    private static string Clean(string s) => Speech.TextFilters.CleanForDisplay(s);

    private static void RenderTable(StringBuilder sb, ScreenWalker.TableItem t)
    {
        sb.Append("Table:\n");
        // The page the HTML surface is showing, so a text read and the player's surface never
        // describe different rows. Silent for a single-page table, like the pager itself.
        if (t.PageCount > 1)
            sb.Append("  ").Append(TablePaging.Label(t.Page, t.PageCount)).Append('\n');
        // Mirrors the HTML surface's column display order so an audit reads what the player reads.
        var order = t.DisplayOrder;
        if (t.Headers.Count > 0)
        {
            var headers = new List<string>(t.Headers.Count);
            for (int i = 0; i < t.Headers.Count; i++)
                headers.Add(Clean(t.Headers[order != null ? order[i] : i].Text));
            sb.Append("  Columns: ").Append(string.Join(" | ", headers)).Append('\n');
        }

        foreach (var row in t.Rows)
        {
            if (row == null) continue; // a row that recycled between settle and read
            int slots = order == null ? row.Cells.Count : Math.Max(order.Length, row.Cells.Count);
            var cells = new List<string>(slots);
            for (int i = 0; i < slots; i++)
            {
                int c = order != null && i < order.Length ? order[i] : i;
                cells.Add(c < row.Cells.Count ? CellText(row.Cells[c]) : "");
            }
            sb.Append("  Row: ").Append(string.Join(" | ", cells)).Append('\n');
        }
        if (t.Truncated)
            sb.Append("  ... (").Append(Speech.Degrade.Unreadable).Append(" all of this page, showing ")
              .Append(t.Rows.Count).Append(" rows)\n");
    }

    // A cell's elements joined into one plain-text field: a dropdown as label=value, a two-option
    // segmented control as its options with the live one marked, an actionable element tagged
    // [button], anything else as its text. Mirrors the HTML cell's multi-element read.
    private static string CellText(Contexts.TableCell cell)
    {
        // The status-flag cell reads from the widget's data, not its stops — the tree holds only the
        // first badge. Mirror the HTML cell so the two surfaces never disagree about a row.
        if (cell.StatusFlagsUnreadable) return Speech.Degrade.Unreadable;
        if (cell.StatusFlags != null) return StatusFlagsText(cell.StatusFlags);

        var parts = new List<string>(cell.Elements.Count);
        foreach (var elem in cell.Elements)
        {
            if (elem.IsSegmented) parts.Add(SegmentText(elem));
            else if (elem.IsDropdown) parts.Add(Clean(elem.DropdownLabel) + "=" + Clean(elem.DropdownState));
            else if (elem.IsToggle) parts.Add((elem.IsChecked ? "[x] " : "[ ] ") + Clean(elem.Text));
            else if (elem.CanActivate) parts.Add(Clean(elem.Text) + " [button]");
            else parts.Add(Clean(elem.Text));
        }
        return string.Join(" ", parts);
    }

    // Every status flag as "title, reason", semicolon-separated. A reason that only restates its
    // title, as the game's own "Youth Contract" does, is dropped rather than read twice — the same
    // rule StatusFlagsHtml applies.
    private static string StatusFlagsText(List<Navigation.StatusFlagsHtml.StatusFlag> flags)
    {
        var parts = new List<string>(flags.Count);
        foreach (var flag in flags)
        {
            string title = flag.Full ?? "";
            string reason = flag.Reason ?? "";
            parts.Add(reason.Length == 0 || reason == title ? title : title + ", " + reason);
        }
        return string.Join("; ", parts);
    }

    // A two-option segmented cell as both options with the live one marked ("(Position)/Player"),
    // so the text read shows the same thing the HTML select does: what the choices are and which
    // one the game currently holds. Reading only the active word could not distinguish a state
    // from a label.
    private static string SegmentText(Contexts.TableCellElement elem)
    {
        var parts = new List<string>(elem.SegmentOptions.Length);
        for (int i = 0; i < elem.SegmentOptions.Length; i++)
            parts.Add(i == elem.SegmentIndex ? "(" + elem.SegmentOptions[i] + ")" : elem.SegmentOptions[i]);
        return string.Join("/", parts);
    }

    // The fixture schedule in the same shape as RenderTable, so an agent audits it like any other
    // table. Mirrors HtmlRenderer.RenderFixtureSchedule: played/upcoming split, date+time and
    // "venue vs opponent" merges, W/D/L before the score, TV last.
    private static void RenderFixtureSchedule(StringBuilder sb, ScreenWalker.FixtureScheduleItem f)
    {
        int iSel = HeaderIndex(f, "Selected"), iTv = HeaderIndex(f, "TV"),
            iDate = HeaderIndex(f, "Date"), iTime = HeaderIndex(f, "Time"),
            iOpp = HeaderIndex(f, "Opposition"), iVen = HeaderIndex(f, "Venue"),
            iRes = HeaderIndex(f, "Result"), iComp = HeaderIndex(f, "Competition");

        var played = new List<Contexts.FixtureScheduleContext.Row>();
        var upcoming = new List<Contexts.FixtureScheduleContext.Row>();
        if (f.Rows != null)
        {
            foreach (var row in f.Rows)
            {
                if (row == null) continue;
                (iRes >= 0 && !string.IsNullOrEmpty(TextAt(row, iRes)) ? played : upcoming).Add(row);
            }
        }

        if (played.Count > 0)
        {
            sb.Append("Played fixtures:\n");
            RenderScheduleRows(sb, f, played, iSel, iTv, iDate, iTime, iOpp, iVen, iComp, iRes);
        }
        if (upcoming.Count > 0)
        {
            sb.Append(played.Count > 0 ? "Upcoming fixtures:\n" : "Fixtures:\n");
            RenderScheduleRows(sb, f, upcoming, iSel, iTv, iDate, iTime, iOpp, iVen, iComp, -1);
        }
        if (f.LoadFailed)
            sb.Append("  ").Append(Speech.Degrade.Unreadable).Append(" fixture list\n");
        if (f.Truncated)
            sb.Append("  ... (incomplete: only the first ")
              .Append(played.Count + upcoming.Count).Append(" fixtures could be read)\n");
    }

    private static void RenderScheduleRows(StringBuilder sb, ScreenWalker.FixtureScheduleItem f,
        List<Contexts.FixtureScheduleContext.Row> rows,
        int iSel, int iTv, int iDate, int iTime, int iOpp, int iVen, int iComp, int iRes)
    {
        foreach (var row in rows)
        {
            var cells = new List<string>(6);
            var sel = CellAt(row, iSel);
            if (sel != null && sel.Kind == Contexts.FixtureScheduleContext.CellKind.Checkbox)
                cells.Add(sel.On ? "(x)" : "( )"); // single-select row selector, rendered as a radio

            var dt = new List<string>(2);
            if (!string.IsNullOrEmpty(TextAt(row, iDate))) dt.Add(Clean(TextAt(row, iDate)));
            if (!string.IsNullOrEmpty(TextAt(row, iTime))) dt.Add(Clean(TextAt(row, iTime)));
            if (dt.Count > 0) cells.Add(string.Join(", ", dt));

            string venue = Clean(TextAt(row, iVen)), opp = Clean(TextAt(row, iOpp));
            if (!string.IsNullOrEmpty(venue) || !string.IsNullOrEmpty(opp))
                cells.Add(string.IsNullOrEmpty(venue) ? opp : venue + " vs " + opp);

            if (iRes >= 0)
            {
                var res = CellAt(row, iRes);
                string score = Clean(res?.Text ?? "");
                cells.Add((string.IsNullOrEmpty(res?.Outcome) ? "" : res.Outcome + " ") + score + " [button]");
            }

            if (!string.IsNullOrEmpty(TextAt(row, iComp))) cells.Add(Clean(TextAt(row, iComp)) + " [button]");

            var tv = CellAt(row, iTv);
            if (tv != null && tv.Kind == Contexts.FixtureScheduleContext.CellKind.TvFlag && tv.On && iTv < f.Headers.Count)
                cells.Add(Clean(f.Headers[iTv].Text));

            sb.Append("  Row: ").Append(string.Join(" | ", cells)).Append('\n');
        }
    }

    // The condition table in the same shape as RenderFixtureSchedule, so an agent audits what the
    // player browses: enabled state, If/And connector, name, comparator, value(s), per row.
    // The contract/transfer terms table in the same shape as HtmlRenderer.RenderContractTerms:
    // one line per term, its value control, its negotiability padlock and any Remove button. Without
    // this the whole terms block was silently absent from this view — an audit read of the contract
    // and transfer-offer screens showed their section headings with nothing under them.
    private static void RenderContractTerms(StringBuilder sb, ScreenWalker.ContractTermsItem t)
    {
        sb.Append("Terms:\n");
        foreach (var r in t.Rows)
            RenderTermRow(sb, r);
    }

    private static void RenderContractConditions(StringBuilder sb, ScreenWalker.ContractConditionsItem c)
    {
        sb.Append("Conditions:\n");
        foreach (var r in c.Rows)
            RenderTermRow(sb, r);
    }

    private static void RenderTermRow(StringBuilder sb, ScreenWalker.ContractTermRow r)
    {
        sb.Append("  ").Append(r.Term ?? Speech.Degrade.Unreadable);
        if (r.ParentTerm != null) sb.Append(" (").Append(r.ParentTerm).Append(')');
        sb.Append(": ").Append(r.Value != null ? StopLine(r.Value) : "");
        foreach (var ex in r.Extras) sb.Append(" | ").Append(StopLine(ex));
        if (r.Lock != null) sb.Append(" | negotiability: ").Append(StopLine(r.Lock));
        if (r.Delete != null) sb.Append(" | ").Append(StopLine(r.Delete));
        sb.Append('\n');
    }

    private static void RenderConditionTable(StringBuilder sb, ScreenWalker.ConditionTableItem t)
    {
        if (t.Rows == null || t.LoadFailed)
        {
            sb.Append(Speech.Degrade.Unreadable).Append(" condition list\n");
            return;
        }
        if (t.Rows.Count == 0) return;

        sb.Append("Conditions:\n");
        foreach (var r in t.Rows)
        {
            if (r == null) continue;
            var cells = new List<string>(6);
            cells.Add(r.Enabled is bool on ? (on ? "(x)" : "( )") : Speech.Degrade.Unavailable);
            if (!string.IsNullOrEmpty(r.Connector))
                cells.Add(Clean(r.Connector) + (r.ConnectorIsToggle ? " [toggle]" : ""));
            cells.Add(Clean(r.Name ?? ""));
            if (r.Comparator != null) cells.Add(Clean(r.Comparator) + " [dropdown]");

            var values = new List<string>(r.Values.Count);
            foreach (var v in r.Values)
            {
                string suffix = v.Kind switch
                {
                    "dropdown" or "combobox" => " [dropdown]",
                    "textfield" => " [edit]",
                    _ => "",
                };
                values.Add(Clean(v.Value ?? "") + suffix);
            }
            if (values.Count > 0) cells.Add(string.Join(" ", values));

            sb.Append("  ").Append(string.Join(" | ", cells)).Append('\n');
        }
        if (t.Truncated)
            sb.Append("  ... (truncated to the first ").Append(t.Rows.Count).Append(" rows)\n");
    }

    private static int HeaderIndex(ScreenWalker.FixtureScheduleItem f, string key)
    {
        for (int i = 0; i < f.Headers.Count; i++)
            if (f.Headers[i].Key == key) return i;
        return -1;
    }

    private static Contexts.FixtureScheduleContext.Cell CellAt(Contexts.FixtureScheduleContext.Row row, int col)
        => col >= 0 && col < row.Cells.Count ? row.Cells[col] : null;

    private static string TextAt(Contexts.FixtureScheduleContext.Row row, int col)
        => CellAt(row, col)?.Text ?? "";

    // Mirrors HtmlRenderer.RenderValueChart, including its refusal: several lines that could not be
    // matched to the rows that selected them render as the gap marker, never as anonymous columns.
    private static void RenderValueChart(StringBuilder sb, ScreenWalker.ValueChartItem item)
    {
        var headers = item.RenderedHeaders();
        var dates = item.DateColumn;
        if (headers == null || dates == null)
        {
            sb.Append(FM27Access.Speech.Degrade.Unreadable).Append(" chart\n");
            return;
        }

        var rows = new List<List<string>>(dates.Count);
        for (int i = 0; i < dates.Count; i++)
        {
            var cells = new List<string> { dates[i].Label };
            foreach (var line in item.Series) cells.Add(line[i].Value);
            rows.Add(cells);
        }
        RenderStringGrid(sb, headers, rows);

        if (item.OmittedColumns is { Count: > 0 })
            sb.Append(ScreenWalker.ValueChartItem.OmittedPrefix)
              .Append(string.Join(", ", item.OmittedColumns)).Append('\n');

        if (item.AmbiguousColumns is { Count: > 0 })
            sb.Append(ScreenWalker.ValueChartItem.AmbiguousPrefix)
              .Append(string.Join(", ", item.AmbiguousColumns)).Append('\n');
    }

    // A plain-string table in the same "header | header" / "cell | cell" shape the other table
    // readings use, so an agent can see the column alignment the player's screen reader gets.
    private static void RenderStringGrid(StringBuilder sb, string[] headers, List<List<string>> rows)
    {
        sb.Append("  ").Append(string.Join(" | ", headers)).Append('\n');
        foreach (var row in rows)
            sb.Append("  ").Append(string.Join(" | ", row)).Append('\n');
    }

    // The tile content, mirroring HtmlRenderer.RenderCarouselTile's three cases: live stat rows,
    // a report-tile grid, or the flat lines no reader recognised.
    private static void RenderCarouselTileBody(StringBuilder sb, ScreenWalker.CarouselTileItem car)
    {
        foreach (var chart in car.ChartTables)
        {
            if (chart.Note != null) sb.Append(chart.Note).Append('\n');
            RenderStringGrid(sb, chart.Grid.Headers, chart.Grid.Rows);
        }
        if (car.HasChart && car.ChartTables.Count == 0)
            sb.Append("chart, visual only\n");
        if (car.WindowNote != null)
            sb.Append(car.WindowNote).Append('\n');
        if (car.Rows.Count > 0)
        {
            sb.Append("  Stat | ").Append(car.HomeTeam ?? "Home").Append(" | ")
              .Append(car.AwayTeam ?? "Away").Append('\n');
            foreach (var row in car.Rows)
                sb.Append("  ").Append(row.Name).Append(" | ").Append(StatCellText(row.Home))
                  .Append(" | ").Append(StatCellText(row.Away)).Append('\n');
            return;
        }
        if (car.Grid != null)
        {
            RenderStringGrid(sb, car.Grid.Headers, car.Grid.Rows);
            return;
        }
        foreach (var line in car.Lines)
            sb.Append(line).Append('\n');
    }

    // Mirrors HtmlRenderer.AppendLiveStatCell's read, minus the live-value registration the text
    // view has no use for.
    private static string StatCellText(UnityEngine.UIElements.VisualElement valueEl)
        => valueEl == null
            ? Speech.Degrade.Value("carousel stat value element missing")
            : DisplayText.Of(new Adapters.TextAdapter(valueEl));

    private static void RenderTabBar(StringBuilder sb, ScreenWalker.TabBarItem t)
    {
        var tabs = new List<string>(t.Tabs.Count);
        foreach (var tab in t.Tabs)
            tabs.Add(DisplayText.Of(tab) + (tab.IsSelected ? " (selected)" : ""));
        sb.Append("Tabs: ").Append(string.Join(" | ", tabs)).Append('\n');
    }

    private static void RenderRadioGroup(StringBuilder sb, ScreenWalker.RadioGroupItem rg)
    {
        sb.Append(string.IsNullOrEmpty(rg.Label) ? "Radio group:\n" : "Radio group: " + rg.Label + "\n");
        for (int r = 0; r < rg.Radios.Count; r++)
        {
            var radio = rg.Radios[r];
            bool on = ServerText.State(radio) == "selected";
            sb.Append("  ").Append(on ? "(x) " : "( ) ").Append(DisplayText.Of(radio)).Append('\n');
            if (rg.SubRadios != null && r < rg.SubRadios.Count)
                foreach (var sub in rg.SubRadios[r])
                    sb.Append("    ").Append(ServerText.State(sub) == "selected" ? "(x) " : "( ) ")
                      .Append(DisplayText.Of(sub)).Append('\n');
            if (rg.RadioExtras != null && r < rg.RadioExtras.Count)
                foreach (var extra in rg.RadioExtras[r])
                    sb.Append("    - ").Append(DisplayText.Of(extra)).Append('\n');
        }
    }
}
