using System;
using System.Collections.Generic;
using System.Globalization;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The Data Hub's Last Match cards, whose substance is drawn: the xG match story (two step lines),
/// match momentum (a bar per period) and the pass maps (a passing network on a pitch). Each drawing
/// is bound to a record holding exactly the figures it plots; this reads that record and replaces
/// the drawing with the table <see cref="LastMatchTables"/> builds from it.
///
/// Verified live 2026-10-04 on home and away matches: the xG card's SubjectRecordList is the HOME
/// side's shots and ComparisionRecordList the away side's, each summing to the printed totals;
/// momentum is positive when the home team is on top; the legends list home then away.
///
/// HTML-only, like the other chart consolidations: the speech surface keeps its raw stops. Read live
/// per render. Declines in place: when any read or check fails the drawing walks exactly as before,
/// and the reason is logged once.
/// </summary>
internal static class LastMatchCharts
{
    // The drawings' containers (element names) and the cards they sit on (panel ids).
    private const string XgCardPanel = "OnThePitchxGGraphCard";
    private const string XgContainer = "data_display-graphs-xg_story-large";
    private const string MomentumCardPanel = "MatchMomentumDH";
    private const string MomentumContainer = "match_momentum_graph";
    private const string PassMapType = "FMPassMapPanel";
    private const string ComparisonHomeFrame = "player-team-pitch-frame";
    private const string ComparisonAwayFrame = "opponent-team-pitch-frame";

    // The observation records and their fields.
    private const string XgRecord = "xgstory";
    private const string MomentumRecord = "MatchMomentumObservationReference";
    private const string PlayerIconNode = "playerIcon";

    // The legends naming the two teams, home first.
    private const string MomentumLegend = "home-away-graph-legend";
    private const int LegendAscend = 4;
    private const int LegendDepth = 3;

    // How deep under each BindingRoot above a drawing to look for its observation: the Data Hub
    // cards bind theirs within four levels, a finished match's report tiles five, the half-time
    // carousel six and the post-match full-time carousel seven (verified 2026-10-05 via /nodeprobe).
    private const int RecordSearchDepth = 8;

    // How far up from a drawing its panel's BindingRoot may sit: a report tile's chart is 38 levels
    // below it, and inside the half-time report's carousel 41 (verified 2026-10-05 via /nodeprobe).
    private const int RecordAncestorBudget = 80;

    private static readonly HashSet<string> _misses = new(StringComparer.Ordinal);

    /// <summary>
    /// Replace <paramref name="el"/> with its table when it is one of the Last Match drawings and
    /// every read holds. True means items were added and the caller must not walk into el.
    /// </summary>
    internal static bool TryBuild(VisualElement el, List<ScreenWalker.RenderItem> items)
    {
        string name = el.name;
        try
        {
            if(el.GetIl2CppType().Name=="FMSpiderChart")
            {
                var grid=RadarSeriesReader.ReadSpider(el,out var reason);
                if(grid==null)
                {
                    Miss("spider chart: "+reason);
                    items.Add(new ScreenWalker.NoteItem{Text="Chart data "+Degrade.Unavailable});
                }
                else items.Add(new ScreenWalker.SyntheticTableItem{Headers=grid.Headers,Rows=grid.Rows,NamesLegend=true});
                return true;
            }
            if (name == XgContainer && ElementBridge.IsInPanel(el, XgCardPanel)) return BuildXg(el, items);
            if (name == MomentumContainer && ElementBridge.IsInPanel(el, MomentumCardPanel)) return BuildMomentum(el, items);
            if (el.GetIl2CppType().Name == "FMXGMatchStoryChart") return BuildXg(el, items);
            if (el.GetIl2CppType().Name == "FMMatchMomentumChart") return BuildMomentum(el, items);
            if (el.GetIl2CppType().Name == "FMAveragePositionsMap") return BuildAveragePositions(el, items);
            if (el.GetIl2CppType().Name == "FMMatchSimulationWidget") return BuildSimulationPositions(el, items);
            if (el.GetIl2CppType().Name == "FMActionZonesPanel") return BuildActionZones(el, items);
            if (el.GetIl2CppType().Name == "FMHeatMap") return BuildHeatMap(el, items);
            if (name is ComparisonHomeFrame or ComparisonAwayFrame) return BuildComparisonSide(el, items);
            // A comparison side's map is built with its team heading or not at all (see
            // BuildComparisonSide); only the single-team card's map is built bare.
            if (el.GetIl2CppType().Name == PassMapType && !InComparisonFrame(el)) return BuildPassMap(el, items, null);
        }
        catch (Exception ex)
        {
            if(el.GetIl2CppType().Name=="FMSpiderChart")
            {
                Miss("spider chart: "+ex.Message);
                items.Add(new ScreenWalker.NoteItem{Text="Chart data "+Degrade.Unavailable});
                return true;
            }
            return Miss($"{name}: read failed: {ex.GetType().Name}: {ex.Message}");
        }
        return false;
    }

    // ---------------------------------------------------------------- xG match story

    private const string XgClockNote = "Times use the chart's continuous elapsed clock, including stoppage time.";

    private static bool BuildXg(VisualElement el, List<ScreenWalker.RenderItem> items)
    {
        var drawing = el.GetIl2CppType().Name == "FMXGMatchStoryChart" ? el : FindTyped(el, "FMXGMatchStoryChart", 8);
        var names = LegendNear(el, ScreenWalker.RadarLegendContainer) ?? XgLegend(drawing);
        if (names == null || names.Count != 2) return Miss("xG story: legend does not name two teams");
        var table = ReadXg(drawing ?? el, XgRecord, names[0], names[1], null, out string bad, out _);
        if (table == null) return Miss("xG story: " + bad);
        items.Add(new ScreenWalker.NoteItem { Text = XgClockNote });
        items.Add(new ScreenWalker.SyntheticTableItem { Headers = table.Headers, Rows = table.Rows, NamesLegend = true });
        return true;
    }

    // Prefer the chart's own bound observation. A name-only ancestor fallback
    // must also match the footer; direct binding can retain an explicit native
    // dataset/footer discrepancy without discarding the chart's real shots.
    private static LastMatchTables.Table ReadXg(VisualElement el, string recordNode, string homeName, string awayName,
        string printedTotals, out string reason, out string snapshotNote)
    {
        snapshotNote=null;
        var boundRecord = BindingRecords.RecordOf(BindingRecords.WidgetValue(el));
        var rec = boundRecord ?? BindingRecords.RecordAbove(el, recordNode, RecordAncestorBudget, RecordSearchDepth);
        if (rec == null) { reason = "no observation record"; return null; }
        var f = BindingRecords.Fields(rec);

        var home = Shots(BindingRecords.ListOf(BindingRecords.Field(f, "SubjectRecordList")), out string bad);
        if (home == null) { reason = "home shots: " + bad; return null; }
        var away = Shots(BindingRecords.ListOf(BindingRecords.Field(f, "ComparisionRecordList")), out bad);
        if (away == null) { reason = "away shots: " + bad; return null; }

        string totals = BindingRecords.TextOf(BindingRecords.Field(f, "XgValue"));
        if (printedTotals != null && totals != printedTotals)
        {
            // FM27 can draw a different total in the footer from the chart's
            // own bound dataset (observed while paused in the second half).
            // Direct widget binding proves ownership; retain both values and
            // verify the shots against their OWN native dataset total below.
            // A record merely found by ancestor/name still requires agreement.
            if(boundRecord==null)
            {
                reason = $"record totals \"{totals}\" are not the printed \"{printedTotals}\"";
                return null;
            }
            snapshotNote=$"Native chart total: {totals}. Current displayed total: {printedTotals}. The game reports different values.";
        }
        var table = LastMatchTables.XgStory(home, away, homeName, awayName, totals, out reason);
        // MatchCurrentTimeSlice here is continuous elapsed minutes: the native
        // Brighton records put the 66-minute goal at 69.2458 after first-half
        // stoppage time. Preserve the plotted data and identify its actual unit.
        if (table != null) table.Headers[0] = "Elapsed minute";
        return table;
    }

    private static List<LastMatchTables.Shot> Shots(Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue> list, out string reason)
    {
        reason = null;
        if (list == null) { reason = "no shot list"; return null; }
        var shots = new List<LastMatchTables.Shot>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var rec = BindingRecords.RecordOf(list[i]);
            if (rec == null) { reason = $"shot {i} is not a record"; return null; }
            var f = BindingRecords.Fields(rec);
            double? minute = BindingRecords.NumberOf(BindingRecords.Field(f, "MatchCurrentTimeSlice"));
            double? xg = BindingRecords.NumberOf(BindingRecords.Field(f, "XgValue"));
            bool? goal = BindingRecords.FlagOf(BindingRecords.Field(f, "Goal"));
            bool? ownGoal = BindingRecords.FlagOf(BindingRecords.Field(f, "OwnGoal"));
            if (minute == null || xg == null || goal == null || ownGoal == null) { reason = $"shot {i} has an unreadable field"; return null; }
            shots.Add(new LastMatchTables.Shot(minute.Value, xg.Value, goal.Value, ownGoal.Value));
        }
        return shots;
    }

    // ---------------------------------------------------------------- match momentum

    private static bool BuildMomentum(VisualElement el, List<ScreenWalker.RenderItem> items)
    {
        var legend = SyntheticTables.FindDescendantNamed(el, MomentumLegend, 2);
        for (var up = el.parent; legend == null && up != null; up = up.parent)
        {
            legend = SyntheticTables.FindDescendantNamed(up, MomentumLegend, 8)
                ?? SyntheticTables.FindDescendantNamed(up, TileMomentumLegend, 8);
            if (up.name == "states-templates-tile-content" || up.GetIl2CppType().Name == "Panel") break;
        }
        var names = MomentumTeamNames(el, legend);
        if (names == null || names.Count != 2) return Miss("momentum: legend/scoreboard does not name two teams");
        var table = ReadMomentum(el, MomentumRecord, names[0], names[1], out string bad);
        if (table == null) return Miss("momentum: " + bad);
        items.Add(new ScreenWalker.SyntheticTableItem { Headers = table.Headers, Rows = table.Rows });
        return true;
    }

    private static List<string> MomentumTeamNames(VisualElement chart, VisualElement legend)
    {
        var names = legend == null ? null : RadarSeriesReader.ReadLegend(legend);
        if (names?.Count == 2) return names;
        // FM27's live overview omits the report legend. Its own scorebar names
        // the current fixture in home/away order. Require the actual live text,
        // and never use this fallback on another match's report/card.
        if (!ElementBridge.IsInPanel(chart, "OverviewPrototype")) return null;
        var live = MatchWatcher.TryTeamNames(requireLive: true);
        return live.HasValue ? new List<string> { live.Value.Home, live.Value.Away } : null;
    }

    private static LastMatchTables.Table ReadMomentum(VisualElement el, string recordNode, string homeName, string awayName,
        out string reason)
    {
        var rec = BindingRecords.RecordOf(BindingRecords.WidgetValue(el))
            ?? BindingRecords.RecordAbove(el, recordNode, RecordAncestorBudget, RecordSearchDepth);
        if (rec == null) { reason = "no observation record"; return null; }
        var f = BindingRecords.Fields(rec);

        var homeTeam = BindingRecords.TeamOf(BindingRecords.Field(f, "HomeTeam"));
        var awayTeam = BindingRecords.TeamOf(BindingRecords.Field(f, "AwayTeam"));
        if (homeTeam == null || awayTeam == null) { reason = "home or away team unreadable"; return null; }
        string valueHeader = SyntheticTables.CleanOrNull(BindingRecords.TextOf(BindingRecords.Field(f, "StatNameLabel")));
        if (valueHeader == null) { reason = "no stat name"; return null; }

        var list = BindingRecords.ListOf(BindingRecords.Field(f, "DataPointList"));
        if (list == null) { reason = "no period list"; return null; }
        int count = list.Count;
        if (ElementBridge.IsInPanel(el, "OverviewPrototype"))
        {
            // The live observation preallocates future periods as zero/Even.
            // The chart's actual drawn series contains only played periods.
            var drawn = el.TryCast<FM.UI.FMMatchMomentumChart>()?.m_matchMomentumData;
            if (drawn == null || drawn.Count > count) { reason = "drawn periods unavailable"; return null; }
            count = drawn.Count;
            for (int i = 0; i < count; i++)
                if (drawn[i]._MatchTime_k__BackingField != i)
                { reason = "drawn periods out of order"; return null; }
        }
        var periods = new List<LastMatchTables.Period>(count);
        var goals = new List<LastMatchTables.Goal>();
        for (int i = 0; i < count; i++)
        {
            var pr = BindingRecords.RecordOf(list[i]);
            if (pr == null) { reason = $"period {i} is not a record"; return null; }
            var pf = BindingRecords.Fields(pr);
            double? index = BindingRecords.NumberOf(BindingRecords.Field(pf, "TimePeriod"));
            double? value = BindingRecords.NumberOf(BindingRecords.Field(pf, "MomentumSegmentMax"));
            if (index != i || value == null) { reason = $"period {i} out of order or unreadable"; return null; }
            periods.Add(new LastMatchTables.Period(value.Value, SideOf(BindingRecords.TeamOf(BindingRecords.Field(pf, "Team")), homeTeam, awayTeam)));
            if (!AddGoals(BindingRecords.ListOf(BindingRecords.Field(pf, "HomeGoalList")), i, LastMatchTables.Side.Home, goals)
                || !AddGoals(BindingRecords.ListOf(BindingRecords.Field(pf, "AwayGoalList")), i, LastMatchTables.Side.Away, goals))
            {
                reason = $"period {i} goals unreadable";
                return null;
            }
        }
        return LastMatchTables.Momentum(periods, goals, homeName, awayName, valueHeader, out reason);
    }

    // ---------------------------------------------------------------- match report tiles

    // The full-time, half-time and past-match reports (and the in-match header carousel) draw the
    // same two charts as tiles, bound to the same observation shapes under different node names.
    // Verified live 2026-10-05 on PastMatchTeamReport via /nodeprobe.
    private const string TileXgRecord = "xgStoryReference";
    private const string TileMomentumRecord = "matchMomentumObservation";
    private const string TileMomentumLegend = "GraphLegend";
    private const string TileXgFooter = "Footer";
    private const string TileXgTotal = "ExpectedGoals";

    // The report tiles' pass maps name their players through PlayerNameRequests under this session:
    // a person reference's name never changes, so one session serves every report and match.
    private const string TilePassMapNames = "match-report-pass-map";

    /// <summary>A table read from a tile's drawing, with the line that must precede it (or null).</summary>
    internal sealed class TileTable
    {
        public string Note;
        public LastMatchTables.Table Table;
    }

    /// <summary>
    /// The tables a report tile's drawing holds — a chart's plotted figures, or a pass map's players
    /// and links — or null (logged once) when the tile holds no drawing this reads or any read or
    /// check fails; the tile then keeps its "visual only" reading or its plain lines.
    /// <paramref name="legendNames"/> are the two team names the xG tile's legend prints, which the
    /// table's columns now carry, so the caller can drop them as loose lines.
    /// </summary>
    internal static List<TileTable> TryReadReportTile(VisualElement content, out List<string> legendNames)
    {
        legendNames = null;
        try
        {
            var passMap = FindTyped(content, PassMapType, 8);
            if (passMap != null)
            {
                var map = ReadPassMap(passMap, TilePassMapNames, out string why);
                if (map == null) { Miss("pass map tile: " + why); return null; }
                return new List<TileTable>
                {
                    new() { Table = map.Value.Players },
                    new() { Note = LastMatchTables.LinkStrengthNote, Table = map.Value.Links },
                };
            }

            var xg = FindTyped(content, "FMXGMatchStoryChart", 8);
            if (xg != null)
            {
                // The legend is the row of two swatch-and-name items drawn just above the chart's frame.
                var frame = xg.hierarchy.parent;
                var row = frame.hierarchy.parent;
                int at = row.hierarchy.IndexOf(frame);
                if (at < 1) { Miss("xG tile: no legend above the chart"); return null; }
                var names = RadarSeriesReader.ReadLegend(row.hierarchy[at - 1]);
                if (names.Count != 2) { Miss($"xG tile: legend names {names.Count} teams, expected 2"); return null; }

                string printed = PrintedXgTotals(content);
                if (printed == null) { Miss("xG tile: footer totals unreadable"); return null; }
                var table = ReadXg(xg, TileXgRecord, names[0], names[1], printed, out string bad, out string snapshotNote);
                if (table == null) { Miss("xG tile: " + bad); return null; }
                legendNames = names;
                return new List<TileTable> { new() { Table = table, Note = snapshotNote==null?XgClockNote:XgClockNote+" "+snapshotNote } };
            }

            var momentum = FindTyped(content, "FMMatchMomentumChart", 8);
            if (momentum != null)
            {
                var legend = SyntheticTables.FindDescendantNamed(content, TileMomentumLegend, 8)
                    ?? SyntheticTables.FindDescendantNamed(content, MomentumLegend, 8);
                var names = MomentumTeamNames(momentum, legend);
                if (names == null || names.Count != 2) { Miss("momentum tile: legend/scoreboard does not name two teams"); return null; }
                var table = ReadMomentum(momentum, TileMomentumRecord, names[0], names[1], out string bad);
                if (table == null) { Miss("momentum tile: " + bad); return null; }
                return new List<TileTable> { new() { Table = table } };
            }
        }
        catch (Exception ex)
        {
            Miss($"report tile chart: read failed: {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }

    // "4.19 - 1.75": the two "ExpectedGoals" texts of the xG tile's footer, home first, joined the
    // way the observation record prints its own totals.
    private static string PrintedXgTotals(VisualElement content)
    {
        var footer = SyntheticTables.FindDescendantNamed(content, TileXgFooter, 8);
        if (footer == null) return null;
        var totals = new List<string>(2);
        CollectNamedTexts(footer, TileXgTotal, totals, 4);
        if (totals.Count == 0)
        {
            // FM27's dugout names the same two footer values explicitly;
            // keep native home/away attribution and the observation check.
            var home = SyntheticTables.FindDescendantNamed(footer, "Home-ExpectedGoals", 4);
            var away = SyntheticTables.FindDescendantNamed(footer, "Away-ExpectedGoals", 4);
            var homeText = home == null ? null : SyntheticTables.CleanOrNull(ScreenWalker.ReadFirstText(home));
            var awayText = away == null ? null : SyntheticTables.CleanOrNull(ScreenWalker.ReadFirstText(away));
            if (homeText != null && awayText != null) return homeText + " - " + awayText;
        }
        return totals.Count == 2 ? totals[0] + " - " + totals[1] : null;
    }

    private static void CollectNamedTexts(VisualElement el, string name, List<string> into, int depth)
    {
        if (depth < 0) return;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var c = el.hierarchy[i];
            if (c.name == name && c.GetIl2CppType().Name == "SIText")
            {
                var t = SyntheticTables.CleanOrNull(ScreenWalker.ReadFirstText(c));
                if (t != null) into.Add(t);
                continue;
            }
            CollectNamedTexts(c, name, into, depth - 1);
        }
    }

    private static LastMatchTables.Side SideOf(FM.UI.TeamReference team, FM.UI.TeamReference home, FM.UI.TeamReference away)
    {
        if (team == null) return LastMatchTables.Side.None;
        if (home.Equals((FM.UI.DatabaseRecordReference)team)) return LastMatchTables.Side.Home;
        if (away.Equals((FM.UI.DatabaseRecordReference)team)) return LastMatchTables.Side.Away;
        return LastMatchTables.Side.None;
    }

    private static bool AddGoals(Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue> list, int period, LastMatchTables.Side side, List<LastMatchTables.Goal> into)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++)
        {
            var g = BindingRecords.RecordOf(list[i]);
            if (g == null) return false;
            double? minute = BindingRecords.NumberOf(BindingRecords.Field(BindingRecords.Fields(g), "MatchTime"));
            if (minute == null) return false;
            into.Add(new LastMatchTables.Goal(period, side, (int)minute.Value));
        }
        return true;
    }

    // ---------------------------------------------------------------- pass maps

    // One side of the comparison card: its team name heads that side's tables, so the two sides can
    // be told apart. The name is game text in the caption frame beside the pitch, the one text that
    // frame holds directly (its "Direction of Play" caption sits a level deeper, with the arrow, and
    // goes with the drawing). Exactly one such text or the side declines: two unheaded tables would
    // leave the player unable to tell the teams apart.
    private static bool BuildComparisonSide(VisualElement frame, List<ScreenWalker.RenderItem> items)
    {
        var panel = FindTyped(frame, PassMapType, 4);
        if (panel == null) return Miss($"{frame.name}: no pass map");
        var names = new List<string>();
        int n = frame.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var caption = frame.hierarchy[i];
            if (FindTyped(caption, PassMapType, 4) != null) continue;
            int m = caption.hierarchy.childCount;
            for (int j = 0; j < m; j++)
            {
                var t = caption.hierarchy[j];
                if (t.GetIl2CppType().Name != "SIText" || !ElementBridge.IsVisible(t)) continue;
                var text = SyntheticTables.CleanOrNull(ScreenWalker.ReadFirstText(t));
                if (text != null) names.Add(text);
            }
        }
        if (names.Count != 1) return Miss($"{frame.name}: {names.Count} team names beside the pitch, expected 1");
        return BuildPassMap(panel, items, names[0]);
    }

    private static bool BuildPassMap(VisualElement panel, List<ScreenWalker.RenderItem> items, string heading)
    {
        var map = ReadPassMap(panel, null, out string reason);
        if (map == null) return Miss("pass map: " + reason);

        if (heading != null) items.Add(new ScreenWalker.HeadingItem { Text = heading, Level = 3 });
        items.Add(new ScreenWalker.SyntheticTableItem { Headers = map.Value.Players.Headers, Rows = map.Value.Players.Rows });
        items.Add(new ScreenWalker.NoteItem { Text = LastMatchTables.LinkStrengthNote });
        items.Add(new ScreenWalker.SyntheticTableItem { Headers = map.Value.Links.Headers, Rows = map.Value.Links.Rows });
        return true;
    }

    // The pass map's two tables: its players and its links. With a nameSession, each player is
    // named by asking the game for his own reference's name (PlayerNameRequests); names arrive a
    // render later, and until every player has one the tables keep shirt numbers only.
    private static (LastMatchTables.Table Players, LastMatchTables.Table Links)? ReadPassMap(VisualElement panel,
        string nameSession, out string reason)
    {
        var icons = new List<(SI.Core.Record Rec, SI.Bindable.IReadOnlyNode Node, bool Visible)>();
        var mapPanel = panel.TryCast<FM.UI.FMPassMapPanel>();
        var points = mapPanel?.m_playerDataPoints;
        var key = mapPanel?.m_bindingDataKey ?? default;
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (points != null && bindings.TryGetNode(ref key, out var context))
        {
            foreach (var point in points)
                if (point?.DataRef?.TryCast<SI.Core.Record>() is { } record)
                    icons.Add((record, context, point.Visible));
        }
        else CollectIcons(panel, icons, 0, true);
        if (icons.Count == 0) { reason = "no player icons bound"; return null; }

        // Every icon's player, so a link's receiver (a person reference) can be named by shirt number.
        var people = new List<(FM.UI.PersonReference Person, int Number)>();
        var fields = new List<Dictionary<string, SI.Core.TypedValue>>();
        foreach (var (rec, _, _) in icons)
        {
            var f = BindingRecords.Fields(rec);
            var matchData = BindingRecords.RecordOf(BindingRecords.Field(f, "PlayerMatchData"));
            if (matchData != null)
                foreach (var pair in BindingRecords.Fields(matchData))
                    if (!f.ContainsKey(pair.Key)) f.Add(pair.Key, pair.Value);
            var person = BindingRecords.PersonOf(BindingRecords.Field(f, "Player"));
            double? number = BindingRecords.NumberOf(BindingRecords.Field(f, "SquadNumber"));
            if (person == null || number == null) { reason = "an icon's player or number is unreadable"; return null; }
            people.Add((person, (int)number.Value));
            fields.Add(f);
        }

        var visibleNumbers = new HashSet<int>();
        for (int i = 0; i < icons.Count; i++) if (icons[i].Visible) visibleNumbers.Add(people[i].Number);

        var players = new List<LastMatchTables.MapPlayer>();
        for (int i = 0; i < icons.Count; i++)
        {
            if (!icons[i].Visible) continue;
            var f = fields[i];
            double? combos = BindingRecords.NumberOf(BindingRecords.Field(f, "TotalPassCombinations"));
            double? completion = BindingRecords.NumberOf(BindingRecords.Field(f, "PassCompletionPercentage"));
            var links = BindingRecords.ListOf(BindingRecords.Field(f, "PassCombinations"));
            if (combos == null || completion == null || links == null) { reason = $"player {people[i].Number} has an unreadable field"; return null; }

            var player = new LastMatchTables.MapPlayer
            {
                Number = people[i].Number,
                Name = PlayerNameRequests.NameFor(nameSession ?? TilePassMapNames, icons[i].Node.Key, BindingRecords.Field(f, "Player")),
                Combinations = (int)combos.Value,
                CompletionPercent = (int)Math.Round(completion.Value),
            };
            for (int l = 0; l < links.Count; l++)
            {
                var lf = BindingRecords.Fields(BindingRecords.RecordOf(links[l]));
                var receiver = BindingRecords.PersonOf(BindingRecords.Field(lf, "Player"));
                double? strength = BindingRecords.NumberOf(BindingRecords.Field(lf, "PassMapSize"));
                if (receiver == null || strength == null) { reason = $"a link of {player.Number} is unreadable"; return null; }
                int receiverNumber = NumberOf(receiver, people);
                if (receiverNumber < 0) { reason = $"{player.Number} passes to a player not on the map"; return null; }
                // A link to a player the selection hides is not drawn either.
                if (visibleNumbers.Contains(receiverNumber)) player.Links.Add((receiverNumber, strength.Value));
            }
            players.Add(player);
        }
        if (players.Count == 0) { reason = "no visible players"; return null; }

        var linkTable = LastMatchTables.PassMapLinks(players, out reason);
        if (linkTable == null) return null;
        return (LastMatchTables.PassMapPlayers(players), linkTable);
    }

    // The player icons under the pitch: each is a BindingRoot whose node carries the icon's record.
    private static void CollectIcons(VisualElement el, List<(SI.Core.Record, SI.Bindable.IReadOnlyNode, bool)> into, int depth, bool visible)
    {
        if (depth > 6) return;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var c = el.hierarchy[i];
            bool v = visible && ElementBridge.IsVisible(c);
            var node = BindingRecords.NodeOf(c);
            var iconNode = node == null ? null : BindingRecords.FindNode(node, PlayerIconNode, 0);
            if (iconNode != null)
            {
                var rec = BindingRecords.RecordOf(BindingRecords.ValueOf(iconNode));
                if (rec != null) { into.Add((rec, iconNode, v)); continue; }
            }
            CollectIcons(c, into, depth + 1, v);
        }
    }

    private static int NumberOf(FM.UI.PersonReference person, List<(FM.UI.PersonReference Person, int Number)> people)
    {
        foreach (var (p, n) in people)
            if (p.Equals((FM.UI.DatabaseRecordReference)person)) return n;
        return -1;
    }

    private static bool BuildSimulationPositions(VisualElement element,List<ScreenWalker.RenderItem> items)
    {
        var widget=element.TryCast<FM.UI.MatchSimulation.FMMatchSimulationWidget>();
        var data=widget?.m_data;var names=MatchWatcher.TryTeamNames(requireLive:true);
        if(data?.Players==null||!names.HasValue)return Miss("match map: native data/team names unavailable");
        // EntityIndex is the simulation's own identity, shared with the live
        // Player. Shirt numbers alone cannot distinguish the opposing teams.
        var entities=new Dictionary<int,FM.Match.Player>();
        foreach(var player in UnityEngine.Object.FindObjectsOfType<FM.Match.Player>())
        {
            if(player.EntityKind!=0)continue;
            if(!entities.TryAdd(player.EntityIndex,player))return Miss("match map: duplicate entity identity");
        }
        string Number(float value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
        var rows=new List<List<string>>();
        for(int i=0;i<data.Players.Count;i++)
        {
            var point=data.Players[i];if(point==null||point.IsSubstitute)continue;
            if(!entities.TryGetValue(point.EntityIndex,out var player)||player.TeamID is not(0 or 1)
                ||player.ShirtNumber!=point.ShirtNumber||string.IsNullOrWhiteSpace(player.BillboardName))
                return Miss("match map: player identity unavailable");
            var pos=point.Position;
            if(!float.IsFinite(pos.x)||!float.IsFinite(pos.y)||!float.IsFinite(point.Rotation))return Miss("match map: invalid coordinates");
            rows.Add(new(){player.TeamID==0?names.Value.Home:names.Value.Away,
                point.ShirtNumber.ToString(CultureInfo.InvariantCulture),player.BillboardName,Number(pos.x),Number(pos.y),Number(point.Rotation)});
        }
        if(rows.Count==0)return Miss("match map: no active players");
        var ball=data.Ball;
        if(ball!=null)
        {
            var pos=ball.Position;
            if(!float.IsFinite(pos.x)||!float.IsFinite(pos.y))return Miss("match map: invalid ball coordinates");
            rows.Add(new(){"","","Ball",Number(pos.x),Number(pos.y),""});
        }
        items.Add(new ScreenWalker.HeadingItem{Text="Current positions",Level=3});
        items.Add(new ScreenWalker.NoteItem{Text="Snapshot of the native match map. X, Y and rotation use the game's plot coordinates."});
        items.Add(new ScreenWalker.SyntheticTableItem{Headers=new[]{"Team","No.","Player","X","Y","Rotation"},Rows=rows,RowHeader=2});
        return true;
    }

    private static bool BuildAveragePositions(VisualElement element, List<ScreenWalker.RenderItem> items)
    {
        var map = element.Cast<FM.UI.FMAveragePositionsMap>();
        var points = map.m_playerDataPoints;
        var key = map.m_bindingDataKey;
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (points == null || !bindings.TryGetNode(ref key, out _)) return Miss("average positions: dataset unavailable");
        var rows = new List<List<string>>();
        bool named = true;
        foreach (var point in points)
        {
            if (point == null || !point.Visible) continue;
            var record = point.DataRef?.TryCast<SI.Core.Record>();
            if (record == null) return Miss("average positions: player record unavailable");
            var fields = BindingRecords.Fields(record);
            var position = BindingRecords.RecordOf(BindingRecords.Field(fields, "AveragePosition"));
            if (position == null) return Miss("average positions: position unavailable");
            var axes = BindingRecords.Fields(position);
            double? x = BindingRecords.NumberOf(BindingRecords.Field(axes, "XaxisData"));
            double? y = BindingRecords.NumberOf(BindingRecords.Field(axes, "YaxisData"));
            double? number = BindingRecords.NumberOf(BindingRecords.Field(fields, "SquadNumber"));
            if (x == null || y == null || number == null || !double.IsFinite(x.Value) || !double.IsFinite(y.Value)) return Miss("average positions: unreadable coordinates");
            var name = PlayerNameRequests.NameFor(TilePassMapNames, key, BindingRecords.Field(fields, "Player"));
            named &= !string.IsNullOrEmpty(name);
            rows.Add(new List<string> { number.Value.ToString("0", CultureInfo.InvariantCulture), name,
                x.Value.ToString("0.##", CultureInfo.InvariantCulture), y.Value.ToString("0.##", CultureInfo.InvariantCulture) });
        }
        if (rows.Count == 0) return Miss("average positions: no visible players");
        if (!named) foreach (var row in rows) row.RemoveAt(1);
        items.Add(new ScreenWalker.NoteItem { Text = "X and Y are the game's native plot coordinates." });
        items.Add(new ScreenWalker.SyntheticTableItem { Headers = named ? new[] { "No.", "Player", "X", "Y" } : new[] { "No.", "X", "Y" }, Rows = rows });
        return true;
    }

    private static bool BuildActionZones(VisualElement element, List<ScreenWalker.RenderItem> items)
    {
        var record = BindingRecords.RecordOf(BindingRecords.WidgetValue(element));
        if (record == null) return Miss("action zones: dataset unavailable");
        var fields = BindingRecords.Fields(record);
        VisualElement keys = null;
        for (var up = element.parent; up != null && keys == null; up = up.parent)
        {
            keys = SyntheticTables.FindDescendantNamed(up, "team-keys-frame", 6);
            if (up.name == "states-templates-tile-content" || up.GetIl2CppType().Name == "Panel") break;
        }
        if (keys == null) return Miss("action zones: team legend unavailable");
        var rows = new List<List<string>>();
        foreach (var side in new[] { ("HomeTeamActionZonesData", "home-team-key-frame"), ("AwayTeamActionZonesData", "away-team-key-frame") })
        {
            var data = BindingRecords.RecordOf(BindingRecords.Field(fields, side.Item1));
            var label = SyntheticTables.FindDescendantNamed(keys, side.Item2, 2);
            string name = label == null ? null : SyntheticTables.CleanOrNull(ScreenWalker.ReadFirstText(label));
            if (data == null || name == null) return Miss("action zones: team unavailable");
            var values = BindingRecords.Fields(data);
            var row = new List<string> { name };
            foreach (var field in new[] { "DefensiveThirdPossession", "MiddleThirdPossession", "FinalThirdPossession" })
            {
                var value = BindingRecords.NumberOf(BindingRecords.Field(values, field));
                if (value == null || !double.IsFinite(value.Value) || value < 0 || value > 100) return Miss("action zones: percentage unavailable");
                row.Add(value.Value.ToString("0.##", CultureInfo.InvariantCulture) + "%");
            }
            rows.Add(row);
        }
        items.Add(new ScreenWalker.SyntheticTableItem { Headers = new[] { "Team", "Defensive third", "Middle third", "Final third" }, Rows = rows });
        return true;
    }

    private static bool BuildHeatMap(VisualElement element, List<ScreenWalker.RenderItem> items)
    {
        var map = element.Cast<FM.UI.FMHeatMap>();
        var columns = BindingRecords.ListOf(BindingRecords.WidgetValue(element));
        // Native columns run across the pitch; rows run along its length.
        // The 100-by-65 pitch therefore draws a 65-by-100 texture/dataset.
        // The compact FM27 tile can retain the template's 100x65 grid fields,
        // while its resolved dataset and rendered texture are both 65x100.
        // Read the actual drawing dimensions, cross-checked with every bound
        // column below, rather than rejecting a populated tile's template defaults.
        int width = map.m_heatTexture?.width ?? 0, height = map.m_heatTexture?.height ?? 0;
        if (width < 3 || height < 3 || width > 256 || height > 256 || columns?.Count != width
            || map.m_heatTexture == null)
            return Miss("heat map: native grid dimensions unavailable");
        var values = new List<double[]>();
        for (int x = 0; x < width; x++)
        {
            var column = BindingRecords.ListOf(columns[x]);
            if (column?.Count != height) return Miss("heat map: incomplete native column");
            var cells = new double[height];
            for (int y = 0; y < height; y++)
            {
                var value = BindingRecords.NumberOf(column[y]);
                if (value == null) return Miss("heat map: unreadable cell");
                cells[y] = value.Value;
            }
            values.Add(cells);
        }
        var table = LastMatchTables.HeatMap(values);
        if (table == null) return Miss("heat map: invalid native intensities");
        items.Add(new ScreenWalker.NoteItem { Text = "Share of native heat-map intensity across nine areas. This measures plotted intensity, not minutes played." });
        items.Add(new ScreenWalker.SyntheticTableItem { Headers = table.Headers, Rows = table.Rows });
        return true;
    }

    // ---------------------------------------------------------------- shared

    private static List<string> LegendNear(VisualElement el, string legendName)
    {
        var up = el.hierarchy.parent;
        for (int i = 0; i < LegendAscend && up != null; i++, up = up.hierarchy.parent)
        {
            var legend = SyntheticTables.FindDescendantNamed(up, legendName, LegendDepth);
            if (legend != null) return RadarSeriesReader.ReadLegend(legend);
        }
        return null;
    }

    private static List<string> XgLegend(VisualElement drawing)
    {
        var frame = drawing?.parent;
        var row = frame?.parent;
        if (row == null) return null;
        int at = row.hierarchy.IndexOf(frame);
        return at < 1 ? null : RadarSeriesReader.ReadLegend(row.hierarchy[at - 1]);
    }

    private static bool InComparisonFrame(VisualElement el)
    {
        var up = el.hierarchy.parent;
        for (int i = 0; i < 4 && up != null; i++, up = up.hierarchy.parent)
            if (up.name is ComparisonHomeFrame or ComparisonAwayFrame) return true;
        return false;
    }

    private static VisualElement FindTyped(VisualElement el, string typeName, int depth)
    {
        if (depth < 0) return null;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var c = el.hierarchy[i];
            if (c.GetIl2CppType().Name == typeName) return c;
            var found = FindTyped(c, typeName, depth - 1);
            if (found != null) return found;
        }
        return null;
    }

    private static bool Miss(string reason)
    {
        if (_misses.Add(reason)) Degrade.Note("[LastMatchCharts] drawing kept as walked: " + reason);
        return false;
    }
}
