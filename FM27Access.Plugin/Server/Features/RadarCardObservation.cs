using System;
using System.Collections.Generic;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// What a Data Hub radar card (<c>OnThePitchPolygonGraphCard</c>) is about, read from the
/// observation record the card is bound to rather than from its drawn text, which misleads: the card
/// titles itself "General Performance" whatever it shows, and on a player's card the legend names
/// the club for the player's own series (the record's display subject is empty, so the game falls
/// back to the team).
///
/// The record (<c>polygonobservation</c>, a binding node above the chart) carries the card's real
/// category as <c>GraphType</c> ("Team Defending", "Attacking - Wingers") and its subject as
/// <c>SubjectRecordId</c>: a TeamReference for a team card, a PersonReference for a player's.
/// The player's name is NOT in the record (only a database id the game resolves natively), so a
/// player's series is labelled as a player, never by name. Main thread only.
/// </summary>
internal static class RadarCardObservation
{
    internal const string PanelName = "OnThePitchPolygonGraphCard";

    private const string ObservationNode = "polygonobservation";
    private const string GraphTypeProperty = "GraphType";
    private const string SubjectProperty = "SubjectRecordId";

    // How far up from the chart to look for the observation's binding, and how deep inside each
    // BindingRoot's node. Measured live: the node sits two levels under the card's ElementBindingRoot.
    private const int AncestorBudget = 40;
    private const int NodeDepth = 4;
    private const int ChartSearchBudget = 4000;

    internal enum Subject { Unknown, Team, Player }

    // One log line per distinct failure, so a broken read stays diagnosable without flooding.
    private static readonly HashSet<string> _misses = new(StringComparer.Ordinal);

    /// <summary>
    /// The card's category and subject kind, from the observation above <paramref name="chartEl"/>.
    /// False when no observation is bound there; <paramref name="graphType"/> may still be null and
    /// <paramref name="subject"/> Unknown when the record lacks either.
    /// </summary>
    internal static bool TryRead(VisualElement chartEl, out string graphType, out Subject subject)
    {
        graphType = null;
        subject = Subject.Unknown;
        var record = FindRecord(chartEl);
        if (record == null) return false;

        var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
        var en = record.GetEnumerator();
        while (en.MoveNext())
        {
            var kv = en.Current;
            string name = reg.GetInfo(kv.Key).Name;
            if (name == GraphTypeProperty)
                graphType = SyntheticTables.CleanOrNull(kv.Value?.AsString());
            else if (name == SubjectProperty)
                subject = SubjectOf(kv.Value);
        }
        return true;
    }

    /// <summary>
    /// The card's title for both surfaces (the HTML section heading and the panel-ready
    /// announcement): its real category instead of the "General Performance" every radar card is
    /// bound to. Null (caller keeps the bound title) for any other panel, or when the category is
    /// not yet bound or unreadable.
    /// </summary>
    internal static string CardTitle(SI.Bindable.Panel p)
    {
        try
        {
            if (p.PanelID.name != PanelName) return null;
            var chart = FindChart(p);
            if (chart == null) return Miss("no radar chart on the card");
            if (!TryRead(chart, out var graphType, out _)) return Miss("no observation bound above the chart");
            return graphType ?? Miss("observation carries no GraphType");
        }
        catch (Exception ex)
        {
            Degrade.Note($"radar card title read failed, keeping bound title: {ex.Message}");
            return null;
        }
    }

    private static Subject SubjectOf(SI.Core.TypedValue value)
    {
        var inner = value?.Get();
        if (inner == null) return Subject.Unknown;
        if (inner.TryCast<FM.UI.PersonReference>() != null) return Subject.Player;
        if (inner.TryCast<FM.UI.TeamReference>() != null) return Subject.Team;
        return Subject.Unknown;
    }

    private static SI.Core.Record FindRecord(VisualElement chartEl)
    {
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        var up = chartEl.hierarchy.parent;
        for (int i = 0; i < AncestorBudget && up != null; i++, up = up.hierarchy.parent)
        {
            var root = up.TryCast<SI.Bindable.BindingRoot>();
            if (root == null) continue;
            var key = root.Key;
            if (!bindings.TryGetNode(ref key, out var node)) continue;
            var found = FindNode(node, NodeDepth);
            if (found == null) continue;
            var foundKey = found.Key;
            return bindings.Get(ref foundKey)?.Get()?.TryCast<SI.Core.Record>();
        }
        return null;
    }

    private static SI.Bindable.IReadOnlyNode FindNode(SI.Bindable.IReadOnlyNode node, int depth)
    {
        if (depth < 0) return null;
        foreach (var child in node.Children)
        {
            if (child.Name == ObservationNode) return child;
            var found = FindNode(child, depth - 1);
            if (found != null) return found;
        }
        return null;
    }

    private static VisualElement FindChart(VisualElement root)
    {
        var stack = new Stack<VisualElement>();
        stack.Push(root);
        int budget = 0;
        while (stack.Count > 0 && budget++ < ChartSearchBudget)
        {
            var el = stack.Pop();
            if (el.GetIl2CppType().Name == "FMPolarChart") return el;
            for (int i = el.hierarchy.childCount - 1; i >= 0; i--)
                stack.Push(el.hierarchy[i]);
        }
        return null;
    }

    private static string Miss(string reason)
    {
        if (_misses.Add(reason)) Degrade.Note($"radar card title kept as bound: {reason}");
        return null;
    }
}
