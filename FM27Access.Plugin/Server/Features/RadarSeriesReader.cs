using System;
using System.Collections.Generic;
using System.Globalization;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Reads every series of an <c>FM.UI.FMPolarChart</c> radar from the chart's binding rather than its
/// drawn labels. The chart prints only the first series' figures (each axis's Value/Label pair); a
/// comparison series (the Data Hub's league average) is drawn as a polygon and stated nowhere in text.
///
/// The figures are all in the chart's own binding node: one child per axis, named by axis index
/// ("0".."N-1"), holding the axis <c>Label</c> and a <c>Value</c> node with one child per series, named
/// by series index, whose value is the raw figure (with a <c>Percentage</c> child, the drawn radius,
/// ignored here). Series order is the legend's order. Verified live 2026-10-04 on the Data Hub's team
/// and player-role radar cards: series 0 matched every printed axis figure, series 1 is the league
/// average. A series the chart does not draw (the hub tiles' unused slots) holds no number and reads
/// as null; a chart with no axis labels (the player tile thumbnails) is declined.
///
/// Main thread only.
/// </summary>
internal static class RadarSeriesReader
{
    // FM27's compact Portal radar uses FMSpiderChart, not FMPolarChart.
    // Its typed points carry the official axis label and unscaled stat value
    // even when the thumbnail omits all labels. Keep the original chart Grid.
    internal static MatchReportTiles.Grid ReadSpider(VisualElement element,out string reason)
    {
        reason=null;var chart=element.TryCast<FM.UI.FMSpiderChart>();var series=chart?.m_dataSeriesList;
        if(series==null||series.Count==0){reason="no bound series";return null;}
        var first=series[0]?.DataPoints;
        if(first==null||first.Count==0){reason="no bound axes";return null;}
        var headers=new List<string>{"Stat"};
        if(series.Count==1)headers.Add("Value");
        else
        {
            var legend=FindLegend(element);var names=legend==null?null:ReadLegend(legend);
            if(names?.Count!=series.Count){reason="series legend unavailable";return null;}
            headers.AddRange(names);
        }
        var rows=new List<List<string>>();var labels=new HashSet<string>(StringComparer.Ordinal);
        for(int axis=0;axis<first.Count;axis++)
        {
            var label=ServerText.Clean(first[axis].Label);
            if(string.IsNullOrWhiteSpace(label)||!labels.Add(label)){reason="axis label unavailable or repeated";return null;}
            var row=new List<string>{label};
            for(int s=0;s<series.Count;s++)
            {
                var points=series[s]?.DataPoints;
                if(points?.Count!=first.Count||ServerText.Clean(points[axis].Label)!=label)
                {reason="series axes differ";return null;}
                var value=points[axis].StatValue;
                if(!float.IsFinite(value)){reason="stat value is not finite";return null;}
                row.Add(value.ToString("0.##",CultureInfo.CurrentCulture));
            }
            rows.Add(row);
        }
        return new(){Headers=headers.ToArray(),Rows=rows};
    }
    private const string LabelChild = "Label";
    private const string ValueChild = "Value";

    // How far above the chart to look for the card's legend (chart -> SIVisible -> graph frame ->
    // data frame, whose sibling is the legend), and how deep below each ancestor.
    private const int LegendAscend = 4;
    private const int LegendDepth = 2;

    // One log line per distinct reason the comparison columns were left out, so a broken read stays
    // diagnosable without a line on every render. The page's legend note is the player's marker.
    private static readonly HashSet<string> _widenMisses = new(StringComparer.Ordinal);

    /// <summary>
    /// Every axis, in axis order, with its raw figure per series (null where a series holds no
    /// number). Null when the chart carries no readable binding of that shape.
    /// </summary>
    internal static List<RadarColumns.Axis> TryRead(VisualElement chartEl)
    {
        if (chartEl.hierarchy.childCount == 0) return null;
        var root = chartEl.hierarchy[0].TryCast<SI.Bindable.BindingRoot>();
        if (root == null) return null;

        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        var key = root.Key;
        if (!bindings.TryGetNode(ref key, out var node)) return null;

        var axes = new SortedDictionary<int, (string Label, Dictionary<int, double?> Values)>();
        int seriesCount = 0;
        foreach (var axisNode in node.Children)
        {
            if (!int.TryParse(axisNode.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int axisIndex)) continue;

            string label = null;
            var values = new Dictionary<int, double?>();
            foreach (var child in axisNode.Children)
            {
                if (child.Name == LabelChild)
                {
                    var labelKey = child.Key;
                    label = bindings.Get(ref labelKey)?.AsString();
                }
                else if (child.Name == ValueChild)
                {
                    foreach (var seriesNode in child.Children)
                    {
                        if (!int.TryParse(seriesNode.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int series)) continue;
                        var seriesKey = seriesNode.Key;
                        values[series] = ReadNumber(bindings.Get(ref seriesKey));
                        if (series + 1 > seriesCount) seriesCount = series + 1;
                    }
                }
            }
            if (string.IsNullOrEmpty(label)) return null;
            axes[axisIndex] = (label, values);
        }
        if (axes.Count == 0 || seriesCount == 0) return null;

        var result = new List<RadarColumns.Axis>(axes.Count);
        foreach (var (label, values) in axes.Values)
        {
            var series = new double?[seriesCount];
            foreach (var kv in values) series[kv.Key] = kv.Value;
            result.Add(new RadarColumns.Axis(label, series));
        }
        return result;
    }

    /// <summary>
    /// <see cref="SyntheticTables.Spec.WidenColumns"/> for the radar card: one column per series the
    /// chart plots, headed by the card's legend. Null (table unchanged, legend note kept) when any
    /// part of the read fails.
    /// </summary>
    internal static string[] WidenTable(VisualElement chartEl, List<List<string>> rows, string[] headers)
    {
        var axes = TryRead(chartEl);
        if (axes == null) return Miss("chart binding unreadable");
        var legendEl = FindLegend(chartEl);
        if (legendEl == null) return Miss("no legend beside the chart");
        var widened = RadarColumns.TryWiden(rows, axes, ReadLegend(legendEl), headers[0], out var reason);
        if (widened == null) return Miss(reason);

        // On a player's card the legend names the club for the player's own series (see
        // RadarCardObservation); head that column for what it holds. The name is not readable.
        if (RadarCardObservation.TryRead(chartEl, out _, out var subject)
            && subject == RadarCardObservation.Subject.Player)
            widened[1] = PlayerSeriesHeader;
        return widened;
    }

    // Mod-authored chrome: the column of a player's own figures on a player's radar card.
    private const string PlayerSeriesHeader = "Player";

    /// <summary>
    /// True when <paramref name="headers"/> are the table <see cref="WidenTable"/> built from this
    /// legend: a column per series, the comparison series headed by their legend names. The first
    /// series' column is not compared, since a player's card heads it for the player rather than by
    /// its legend name. Then the legend has nothing left to tell.
    /// </summary>
    internal static bool HeadersNameLegend(string[] headers, VisualElement legendEl)
    {
        var names = ReadLegend(legendEl);
        if (names.Count < 2 || headers.Length != names.Count + 1) return false;
        for (int i = 1; i < names.Count; i++)
            if (headers[i + 1] != names[i]) return false;
        return true;
    }

    // The legend's series names in order, cleaned as the walk would read them.
    internal static List<string> ReadLegend(VisualElement legendEl)
    {
        var names = new List<string>();
        int n = legendEl.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var item = legendEl.hierarchy[i];
            if (!ElementBridge.IsVisible(item)) continue;
            var name = SyntheticTables.CleanOrNull(ScreenWalker.ReadFirstText(item));
            if (name != null) names.Add(name);
        }
        return names;
    }

    /// <summary>
    /// True when a radar chart sits beside this legend (within the few levels the radar card puts
    /// between them). Other charts reuse the legend template, and the radar's caveat must not
    /// appear under them.
    /// </summary>
    internal static bool HasRadarNearby(VisualElement legendEl)
    {
        var up = legendEl.hierarchy.parent;
        for (int i = 0; i < LegendAscend && up != null; i++, up = up.hierarchy.parent)
            if (HasDescendantOfType(up, RadarChartType, 8)) return true;
        return false;
    }

    private const string RadarChartType = "FMPolarChart";

    private static bool HasDescendantOfType(VisualElement el, string typeName, int depth)
    {
        if (depth < 0) return false;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = el.hierarchy[i];
            if (c.GetIl2CppType().Name == typeName || HasDescendantOfType(c, typeName, depth - 1)) return true;
        }
        return false;
    }

    private static VisualElement FindLegend(VisualElement chartEl)
    {
        var up = chartEl.hierarchy.parent;
        for (int i = 0; i < LegendAscend && up != null; i++, up = up.hierarchy.parent)
        {
            var legend = SyntheticTables.FindDescendantNamed(up, ScreenWalker.RadarLegendContainer, LegendDepth);
            if (legend != null) return legend;
        }
        return null;
    }

    private static string[] Miss(string reason)
    {
        if (_widenMisses.Add(reason)) Degrade.Note($"radar comparison series left out: {reason}");
        return null;
    }

    // A numeric binding holds an SI.Core.DynamicNumber; read it typed rather than parsing AsString,
    // whose decimal separator follows the game's culture. Null for anything that is not one.
    private static double? ReadNumber(SI.Core.TypedValue tv)
    {
        var inner = tv?.Get();
        if (inner == null || !inner.GetIl2CppType().Equals(Il2CppInterop.Runtime.Il2CppType.Of<SI.Core.DynamicNumber>()))
            return null;
        return inner.Unbox<SI.Core.DynamicNumber>().GetDouble();
    }
}
