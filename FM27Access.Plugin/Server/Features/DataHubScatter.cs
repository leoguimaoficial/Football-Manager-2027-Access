using System;
using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The Data Hub's team scatter cards (Possession, Tackling, Defensive Efficiency…), read into the
/// highlighted team's standing on both measures (<see cref="ScatterSummary"/>). Each point is a team
/// crest placed by its two values; the card's <c>graph</c> record holds every point, both axes (name,
/// league average, highest, lowest) and the four quadrant labels.
///
/// The highlighted team is the one every point's binding names as <c>highlightteam</c>. It is named
/// by the header of the screen the card belongs to (the Data Hub, or an opposition's Team Report)
/// only when that screen's team has the same id, so a screen whose id cannot be read, or that is
/// about another team, falls back to "Highlighted team" rather than a name that might be wrong.
///
/// A scatter of players (Player Analytics: one position group across the division) reads as the
/// group's range on each measure, then a row per player of the highlighted team with both values,
/// their ranks and quadrant. The game names none of them (person references only), so the rows are
/// numbered, and each row opens that player's profile through the game's own point button: verified
/// 2026-10-04, pressing a point opens the Player Report, and Back returns to the card.
///
/// HTML-only, read live per render; any failed read keeps the "visual only" note it replaces.
/// </summary>
internal static class DataHubScatter
{
    private const string GraphRecord = "graph";
    private const string ChartType = "FMScatterChart";
    private const string HighlightNode = "highlightteam";
    private const string UniqueIdNode = "UniqueId";
    // Screens whose header names the team their analysis cards are about: our Data Hub, and an
    // opposition's Team Report (verified 2026-10-04: same root Team.UniqueId and MainFrame header).
    private static readonly string[] HubPanels = { "Data Hub", "TeamReportSite" };
    private const string HubTeamNode = "Team";
    private const string HubNameFrame = "MainFrame";

    // Mod-authored fallback header for the highlighted team's column.
    private const string HighlightedTeam = "Highlighted team";

    private static readonly HashSet<string> _misses = new(StringComparer.Ordinal);

    internal static bool TryBuild(VisualElement container, List<ScreenWalker.RenderItem> items)
    {
        try { return Build(container, items); }
        catch (Exception ex) { return Miss($"read failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static bool Build(VisualElement container, List<ScreenWalker.RenderItem> items)
    {
        var chart=FindTyped(container,ChartType,6);
        var rec = chart==null?null:BindingRecords.RecordOf(BindingRecords.WidgetValue(chart));
        rec ??= BindingRecords.RecordAbove(container, GraphRecord);
        if (rec == null) return Miss("no graph record");
        var f = BindingRecords.Fields(rec);

        if (!ReadAxis(BindingRecords.Field(f, "YaxisData"), out var y) || !ReadAxis(BindingRecords.Field(f, "XaxisData"), out var x))
            return Miss("an axis is unreadable");

        var list = BindingRecords.ListOf(BindingRecords.Field(f, "CombinedRecordList"));
        if (list == null || list.Count == 0) return Miss("no point list");
        var quadrants = Quadrants(f);

        // A scatter of players (Player Analytics) plots persons; one of teams plots teams.
        var first = BindingRecords.Fields(BindingRecords.RecordOf(list[0]));
        if (BindingRecords.PersonOf(BindingRecords.Field(first, "Player")) != null)
        {
            // One card on screen is one naming session (see PlayerNameRequests).
            string session = container.Pointer.ToString() + "|" + BindingRecords.TextOf(BindingRecords.Field(f, "GraphType"));
            return BuildPlayers(container, list.Count, y, x, quadrants, items, session);
        }

        var highlight = Highlight(container, out double? highlightId);
        if (highlight == null) return Miss("no single highlighted team on the points");

        var points = new List<ScatterSummary.Point>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var pf = BindingRecords.Fields(BindingRecords.RecordOf(list[i]));
            var team = BindingRecords.TeamOf(BindingRecords.Field(pf, "Team"));
            var data = BindingRecords.RecordOf(BindingRecords.Field(pf, "ScatterChartData"));
            if (team == null || data == null) return Miss($"point {i} has no team or values");
            var df = BindingRecords.Fields(data);
            double? px = BindingRecords.NumberOf(BindingRecords.Field(df, "GraphPointX"));
            double? py = BindingRecords.NumberOf(BindingRecords.Field(df, "GraphPointY"));
            if (px == null || py == null) return Miss($"point {i} values unreadable");
            points.Add(new ScatterSummary.Point(px.Value, py.Value, team.Equals((FM.UI.DatabaseRecordReference)highlight)));
        }

        var result = ScatterSummary.Build(points, y, x, quadrants, HubTeamName(container, highlightId) ?? HighlightedTeam, out string reason);
        if (result == null) return Miss(reason);
        items.Add(new ScreenWalker.SyntheticTableItem { Headers = result.Headers, Rows = result.Rows });
        if (result.Quadrant != null) items.Add(new ScreenWalker.NoteItem { Text = result.Quadrant });
        return true;
    }

    // A player scatter: the position group's range, then our players, each row opening that player
    // through the game's own point button. The points are read from their own bindings
    // (PlayerScatterPoints) and must number exactly the record's list, so none is missed.
    private static bool BuildPlayers(VisualElement container, int listCount, ScatterSummary.Axis y, ScatterSummary.Axis x,
        string[] quadrants, List<ScreenWalker.RenderItem> items, string session)
    {
        var read = PlayerScatterPoints.Read(container, out string reason);
        if (read == null) return Miss("players: " + reason);
        if (read.Count != listCount) return Miss($"players: {read.Count} points drawn, record lists {listCount}");

        var points = new List<ScatterSummary.Point>(read.Count);
        foreach (var p in read) points.Add(new ScatterSummary.Point(p.X, p.Y, p.Ours));
        var result = ScatterSummary.BuildPlayers(points, y, x, quadrants, out reason);
        if (result == null) return Miss("players: " + reason);

        items.Add(new ScreenWalker.SyntheticTableItem { Headers = result.ContextHeaders, Rows = result.ContextRows });
        if (result.PlayerRows.Count == 0) return true;
        var actions = new List<IAccessibleElement>(result.PlayerPoints.Count);
        for (int row = 0; row < result.PlayerPoints.Count; row++)
        {
            var point = read[result.PlayerPoints[row]];
            actions.Add(point.Button == null ? null : ElementBridge.CreateAdapter(point.Button, ElementClassification.Button));
            // The player's own name, asked of the game for this point's person and kept only under
            // that person's reference; until it arrives (or if it never does) the row keeps its number.
            string name = PlayerNameRequests.NameFor(session, point.Node.Key, point.Person);
            if (name != null) result.PlayerRows[row][0] = name;
        }
        items.Add(new ScreenWalker.SyntheticTableItem { Headers = result.PlayerHeaders, Rows = result.PlayerRows, RowActions = actions });
        return true;
    }

    // The four quadrant labels by the game's QuadrantZone ("first, second"); a missing one stays null.
    private static string[] Quadrants(Dictionary<string, SI.Core.TypedValue> f)
    {
        var quadrants = new string[4];
        var qlist = BindingRecords.ListOf(BindingRecords.Field(f, "ScatterGraphQuadrantList"));
        if (qlist == null) return quadrants;
        for (int i = 0; i < qlist.Count; i++)
        {
            var qf = BindingRecords.Fields(BindingRecords.RecordOf(qlist[i]));
            double? zone = BindingRecords.NumberOf(BindingRecords.Field(qf, "QuadrantZone"));
            string a = SyntheticTables.CleanOrNull(BindingRecords.TextOf(BindingRecords.Field(qf, "QuadrantLabelFirst")));
            string b = SyntheticTables.CleanOrNull(BindingRecords.TextOf(BindingRecords.Field(qf, "QuadrantLabelSecond")));
            if (zone is >= 0 and <= 3 && a != null && b != null) quadrants[(int)zone.Value] = a + ", " + b;
        }
        return quadrants;
    }

    private static bool ReadAxis(SI.Core.TypedValue tv, out ScatterSummary.Axis axis)
    {
        axis = default;
        var rec = BindingRecords.RecordOf(tv);
        if (rec == null) return false;
        var f = BindingRecords.Fields(rec);
        string name = SyntheticTables.CleanOrNull(BindingRecords.TextOf(BindingRecords.Field(f, "StatNameLabel")));
        double? mean = BindingRecords.NumberOf(BindingRecords.Field(f, "MeanStatValue"));
        double? min = BindingRecords.NumberOf(BindingRecords.Field(f, "MinStatValue"));
        double? max = BindingRecords.NumberOf(BindingRecords.Field(f, "MaxStatValue"));
        if (name == null || mean == null || min == null || max == null) return false;
        axis = new ScatterSummary.Axis(name, mean.Value, min.Value, max.Value);
        return true;
    }

    // The team every point's binding highlights; null unless all points agree on one. Its id, when
    // the binding carries it, names it against the hub.
    private static FM.UI.TeamReference Highlight(VisualElement container, out double? id)
    {
        id = null;
        var chart = FindTyped(container, ChartType, 6);
        if (chart == null) return null;
        FM.UI.TeamReference found = null;
        var roots = new List<VisualElement>();
        CollectRoots(chart, roots, 0);
        foreach (var r in roots)
        {
            var node = BindingRecords.NodeOf(r);
            var h = node == null ? null : BindingRecords.FindNode(node, HighlightNode, 2);
            if (h == null) continue;
            var team = BindingRecords.TeamOf(BindingRecords.ValueOf(h));
            if (team == null) return null;
            if (found == null)
            {
                found = team;
                var idNode = BindingRecords.FindNode(h, UniqueIdNode, 0);
                id = idNode == null ? null : BindingRecords.NumberOf(BindingRecords.ValueOf(idNode));
            }
            else if (!found.Equals((FM.UI.DatabaseRecordReference)team)) return null;
        }
        return found;
    }

    // The club name from the header of whichever analysis screen belongs to the highlighted team: the
    // Data Hub (our club) or an opposition Team Report, the same header template. Only a panel whose
    // own team has the highlighted id names it; any other leaves the column "Highlighted team".
    private static string HubTeamName(VisualElement from, double? highlightId)
    {
        if (highlightId == null) return null;
        foreach (var panel in HubPanels)
        {
            var hub = FindPanel(from, panel);
            if (hub == null) continue;
            var name = PanelTeamName(hub, highlightId.Value);
            if (name != null) return name;
        }
        return null;
    }

    private static string PanelTeamName(VisualElement hub, double highlightId)
    {
        var roots = new List<VisualElement>();
        CollectRoots(hub, roots, 0);
        foreach (var r in roots)
        {
            var node = BindingRecords.NodeOf(r);
            var team = node == null ? null : BindingRecords.FindNode(node, HubTeamNode, 0);
            var idNode = team == null ? null : BindingRecords.FindNode(team, UniqueIdNode, 0);
            if (idNode == null) continue;
            if (BindingRecords.NumberOf(BindingRecords.ValueOf(idNode)) != highlightId) return null;
            var frame = ScreenWalker.FindDescendantNamed(hub, HubNameFrame);
            return frame == null ? null : SyntheticTables.CleanOrNull(ScreenWalker.ReadFirstText(frame));
        }
        return null;
    }

    // A panel elsewhere in the panel stack: up to the root, then down to the panel of that name.
    private static VisualElement FindPanel(VisualElement from, string name)
    {
        var top = from;
        while (top.hierarchy.parent != null) top = top.hierarchy.parent;
        return FindPanelBelow(top, name, 4);
    }

    private static VisualElement FindPanelBelow(VisualElement el, string name, int depth)
    {
        if (depth < 0) return null;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = el.hierarchy[i];
            if (c.name == name && c.GetIl2CppType().Name == "Panel") return c;
            var found = FindPanelBelow(c, name, depth - 1);
            if (found != null) return found;
        }
        return null;
    }

    private static void CollectRoots(VisualElement el, List<VisualElement> into, int depth)
    {
        if (depth > 6) return;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var c = el.hierarchy[i];
            if (c.TryCast<SI.Bindable.BindingRoot>() != null) { into.Add(c); continue; }
            CollectRoots(c, into, depth + 1);
        }
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
        if (_misses.Add(reason)) Degrade.Note("[DataHubScatter] visual-only note kept: " + reason);
        return false;
    }
}
