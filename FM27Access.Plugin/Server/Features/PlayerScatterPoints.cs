using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The points of a Data Hub player scatter card (Crossing - Defenders, Shooting - Attackers…), read
/// from each point's own binding: its two values (its <c>data</c> record's ScatterChartData) and
/// whether the player belongs to the highlighted team (the player's <c>Person &gt; Team &gt;
/// UniqueId</c> against the point's <c>highlightteam &gt; UniqueId</c>). Each point is also the
/// game's own player button (<c>CellButton</c>), kept so a row can open that player.
///
/// Player names are not in any of this (person references only). Main thread only; read live.
/// </summary>
internal static class PlayerScatterPoints
{
    internal sealed class Point
    {
        public double X, Y;
        public bool Ours;
        public VisualElement Button;
        public SI.Bindable.IReadOnlyNode Node;   // the point's own binding node
        public SI.Core.TypedValue Person;        // the point's player (a person reference)
    }

    private const string ChartType = "FMScatterChart";
    private const string DataNode = "data";
    private const string HighlightNode = "highlightteam";
    private const string PersonNode = "Person";
    private const string TeamNode = "Team";
    private const string UniqueIdNode = "UniqueId";
    private const string PointButton = "CellButton";

    /// <summary>Every point under the scatter container, or null with a reason.</summary>
    internal static List<Point> Read(VisualElement container, out string reason)
    {
        reason = null;
        var chart = Find(container, el => el.GetIl2CppType().Name == ChartType, 6);
        if (chart == null) { reason = "no scatter chart"; return null; }

        var points = new List<Point>();
        var sources=new List<(VisualElement Element,SI.Bindable.IReadOnlyNode Node)>();
        var nativePoints=chart.TryCast<FM.UI.FMScatterChart>()?.m_chartData?.DataPoints;
        if(nativePoints!=null&&nativePoints.Count>0)
        {
            var bindings=SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
            foreach(var point in nativePoints)
            {
                var native=point?.ChartPoint;var key=native?.m_key??default;
                if(native?.m_element==null||key.m_key==0||!bindings.TryGetNode(ref key,out var node))
                {reason="a native chart point has no current binding scope";return null;}
                sources.Add((native.m_element,node));
            }
        }
        else
        {
            var roots=new List<VisualElement>();CollectRoots(chart,roots,0);
            foreach(var root in roots)sources.Add((root,BindingRecords.NodeOf(root)));
        }
        foreach (var source in sources)
        {
            var r=source.Element;var node=source.Node;
            if (node == null) continue;
            var data = BindingRecords.FindNode(node, DataNode, 0);
            if (data == null) continue;   // not a point (a tooltip's root)

            var rec = BindingRecords.RecordOf(BindingRecords.ValueOf(data));
            var fields = rec == null ? null : BindingRecords.Fields(rec);
            var values = fields == null ? null : BindingRecords.RecordOf(BindingRecords.Field(fields, "ScatterChartData"));
            if (values == null) { reason = $"point {points.Count}: no values"; return null; }
            var vf = BindingRecords.Fields(values);
            double? x = BindingRecords.NumberOf(BindingRecords.Field(vf, "GraphPointX"));
            double? y = BindingRecords.NumberOf(BindingRecords.Field(vf, "GraphPointY"));

            double? highlight = IdUnder(BindingRecords.FindNode(node, HighlightNode, 2));
            double? team = PlayerTeamId(node);
            if (x == null || y == null || highlight == null || team == null) { reason = $"point {points.Count}: unreadable value or team"; return null; }

            points.Add(new Point
            {
                X = x.Value, Y = y.Value, Ours = team == highlight,
                Button = Find(r, el => el.name == PointButton, 12),
                Node = node,
                Person = BindingRecords.Field(fields, "Player"),
            });
        }
        if (points.Count == 0) { reason = "no points bound"; return null; }
        return points;
    }

    private static double? IdUnder(SI.Bindable.IReadOnlyNode node)
    {
        var id = node == null ? null : BindingRecords.FindNode(node, UniqueIdNode, 0);
        return id == null ? null : BindingRecords.NumberOf(BindingRecords.ValueOf(id));
    }

    // The player's team id: the Person node that carries a Team child (another Person node under the
    // portrait binding carries the person's own id and appearance, not the team).
    private static double? PlayerTeamId(SI.Bindable.IReadOnlyNode node)
    {
        var stack = new Stack<(SI.Bindable.IReadOnlyNode, int)>();
        stack.Push((node, 0));
        while (stack.Count > 0)
        {
            var (n, depth) = stack.Pop();
            foreach (var child in n.Children)
            {
                if (child.Name == PersonNode)
                {
                    var team = BindingRecords.FindNode(child, TeamNode, 0);
                    var id = IdUnder(team);
                    if (id != null) return id;
                }
                if (depth < 4) stack.Push((child, depth + 1));
            }
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

    private static VisualElement Find(VisualElement el, Func<VisualElement, bool> match, int depth)
    {
        if (depth < 0) return null;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var c = el.hierarchy[i];
            if(!ElementBridge.IsVisible(c))continue;
            if (match(c)) return c;
            var found = Find(c, match, depth - 1);
            if (found != null) return found;
        }
        return null;
    }
}
