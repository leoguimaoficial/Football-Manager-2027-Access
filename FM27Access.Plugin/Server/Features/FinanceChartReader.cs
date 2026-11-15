using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Reads the live data out of a date-plotted line chart (the finance cards' balance, income,
/// expenditure and budget histories). These FMLineCharts are graphical lines with no text nodes,
/// so a screen reader finds nothing where they sit; the plotted data lives in the chart's series —
/// each point carries the game's own axis label ("1 Feb 25") and the plotted value. Gated to
/// charts whose points are date-plotted (a GraphPointX GameDate property), so some other line
/// chart with non-date labels never renders as a wrongly-headed "Date" table; those keep the
/// honest degrade marker. Lives in Plugin (not Navigation) so it stays hot-reloadable.
///
/// A money chart's plotted value is NOT the number the rest of the card shows. GraphPointY holds
/// the game's base currency (pounds); every figure the game draws — the tiles, the breakdown
/// tables, the chart's own y-axis labels — is converted to the currency the player picked in
/// settings, native-side, during translation. Rendering the raw plot therefore contradicted the
/// card around it: on a save set to dollars the income chart read "99,162" beside a "Last Month
/// $133,950" tile for the same month (verified 2026-08-02: the two differ by exactly the save's
/// 1.350825 rate, on the Income and Expenditure screens independently). So a currency chart's
/// values go back through the game's own translation, which applies whichever currency is
/// selected — the symbol, the grouping and the accounting parentheses are all the game's, never
/// composed here, so a player on pounds or euros gets their own notation with no code change.
/// </summary>
internal static class FinanceChartReader
{
    // One plotted point: the game's own x-axis label and the value formatted for display.
    internal readonly struct Point
    {
        public readonly string Label;
        public readonly string Value;

        public Point(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }

    // The DataRef property that marks a date-plotted point. Read by name (not hashed id), matching
    // LeagueHistoryChartReader.
    private const string XProp = "GraphPointX";
    private const string GameDateMarker = "GameDate";

    // Which unit the y-axis plots. The chart declares it in SILineChart's private Y_AxisLabelType
    // (a YAxisLabelType); the interop wrapper surfaces private fields as properties, so it is read
    // by name with all flags. Only the Currency case gets translated — an Integer axis plotting a
    // count would be corrupted by a currency conversion.
    private const string YAxisLabelTypeProp = "Y_AxisLabelType";
    private const string CurrencyAxis = "Currency";

    private static readonly PropertyInfo AxisTypeProperty =
        typeof(SI.Bindable.Charts.SILineChart).GetProperty(
            YAxisLabelTypeProp,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    // Reads every plotted line out of a date-plotted line chart, one point list per series in the
    // chart's own series order — the order the caller joins against the rows that selected them
    // (see ChartSeriesNaming). Returns null when the element is not such a chart — no series, no
    // points, an unlabeled point, an x-axis that is not a game date, or series of differing lengths
    // — so the caller degrades in place rather than emitting a mislabeled table. Throws are left to
    // the caller's boundary catch.
    public static List<List<Point>> TryReadAll(VisualElement chartEl)
        => ReadSeries(chartEl, sharedDates: true);

    private static List<List<Point>> ReadSeries(VisualElement chartEl, bool sharedDates)
    {
        var chart = chartEl.TryCast<SI.Bindable.Charts.SILineChart>();
        if (chart == null) return null;

        var series = chart.Series;
        if (series == null || series.Count == 0) return null;

        var first = series[0].DataPoints;
        if (first == null || first.Count == 0) return null;

        if (!IsDatePlotted(first[0])) return null;

        // Establish the unit before reading a single value. "I could not tell whether these are
        // pounds, dollars or goals" has to decline the whole chart: a plotted money value shown
        // unconverted is not a gap the player can see, it is a wrong number that reads as real.
        bool? currency = IsCurrencyPlotted(chart);
        if (currency == null)
        {
            Degrade.Note("Date-plotted chart: y-axis unit unreadable, declining the read.");
            return null;
        }

        var all = new List<List<Point>>(series.Count);
        for (int s = 0; s < series.Count; s++)
        {
            var points = series[s].DataPoints;
            if (points == null || points.Count == 0) return null;

            // Every line shares one date column, so lines of different lengths cannot be laid out
            // as rows without inventing which dates the short one is missing.
            if (sharedDates && points.Count != first.Count)
            {
                Degrade.Note($"Date-plotted chart series {s} has {points.Count} points against "
                           + $"{first.Count} in the first, declining the read.");
                return null;
            }

            var rows = new List<Point>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (!IsDatePlotted(p)) return null;
                string label = p.Label;
                if (string.IsNullOrWhiteSpace(label)) return null;
                if (sharedDates && s > 0 && label != all[0][i].Label) return null;

                string value = currency.Value
                    ? Translate(p.Value)
                    : p.Value.ToString("N0", CultureInfo.InvariantCulture);
                rows.Add(new Point(label, value));
            }
            all.Add(rows);
        }
        return all;
    }

    // The medical comparison draws the current and previous seasons on separate
    // date ranges. The native solid/dotted key names series 0/1 respectively;
    // their StyleColourIndex and this exact key shape must agree. Reuse the
    // original single-series Date/Value tables, each with its own actual dates.
    internal static bool TryBuildSeasonComparison(VisualElement element, List<ScreenWalker.RenderItem> items)
    {
        if (!ElementBridge.IsInPanel(element, "MedicalCentreSeasonSummaryCard")) return false;
        var chart = element.TryCast<SI.Bindable.Charts.SILineChart>();
        if (chart?.Series?.Count != 2 || chart.Series[0].StyleColourIndex != 0
            || chart.Series[1].StyleColourIndex != 1 || element.parent?.name != "axis_container") return false;
        var content = element.parent.parent;
        if (content == null) return false;
        var keys = new string[2];
        int keyCount = 0;
        ReadSeasonKeys(content, keys, ref keyCount, 0);
        if (keyCount != 2 || string.IsNullOrWhiteSpace(keys[0]) || string.IsNullOrWhiteSpace(keys[1])
            || keys[0] == keys[1]) return false;
        var series = ReadSeries(element, sharedDates: false);
        if (series == null || series.Count != 2) return false;
        var axis = ScreenWalker.FindDescendantNamed(element.parent, "axis_label");
        var label = axis == null ? null : ScreenWalker.ReadFirstText(axis);
        for (int i = 0; i < series.Count; i++)
            items.Add(new ScreenWalker.ValueChartItem
            {
                Title = string.IsNullOrWhiteSpace(label) ? keys[i] : label + " — " + keys[i],
                Series = new List<List<Point>> { series[i] },
            });
        return true;
    }

    private static void ReadSeasonKeys(VisualElement element, string[] keys, ref int count, int depth)
    {
        if (depth > 5 || !ElementBridge.IsVisible(element)) return;
        var sprite = element.resolvedStyle.backgroundImage.sprite;
        var name = sprite == null ? null : sprite.name;
        int index = name == "game-concepts-data-vis-key-solid_small_3x" ? 0
            : name == "game-concepts-data-vis-key-dotted_small_3x" ? 1 : -1;
        if (index >= 0)
        {
            count++;
            if (keys[index] != null) { count += 2; return; }
            keys[index] = ScreenWalker.ReadFirstText(element.parent);
            return;
        }
        for (int i = 0; i < element.hierarchy.childCount; i++)
            ReadSeasonKeys(element.hierarchy[i], keys, ref count, depth + 1);
    }

    // The plotted base-currency amount as the game's own display text, in the player's selected
    // currency. A null or blank return degrades that one cell — never the raw number, which would
    // be the wrong currency stated as fact.
    private static string Translate(double baseCurrencyValue)
    {
        string text;
        try { text = FM.UI.ChartCurrencyVisualFunctionLibrary.TranslateCurrencyValue(baseCurrencyValue); }
        catch (Exception ex) { return Degrade.Read($"Chart currency translation threw: {ex.Message}"); }

        return string.IsNullOrWhiteSpace(text)
            ? Degrade.Read("Chart currency translation returned nothing.")
            : text;
    }

    // True when the chart plots money, false when it plots something else, null when the chart
    // does not say — the caller declines rather than guess a unit.
    private static bool? IsCurrencyPlotted(SI.Bindable.Charts.SILineChart chart)
    {
        if (AxisTypeProperty == null) return null;

        object axis;
        try { axis = AxisTypeProperty.GetValue(chart); }
        catch (Exception ex)
        {
            Degrade.Note($"Chart y-axis type read threw: {ex.Message}");
            return null;
        }

        if (axis == null) return null;
        return axis.ToString() == CurrencyAxis;
    }

    private static bool IsDatePlotted(SI.Bindable.Charts.SILineChart.DataPoint point)
    {
        var dr = point.DataRef;
        if (dr == null) return false;

        var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
        var en = dr.Keys.GetEnumerator();
        while (en.MoveNext())
        {
            var key = en.Current;
            string name;
            try { name = reg.GetInfo((uint)key).Name; }
            catch { continue; }
            if (name != XProp) continue;
            var val = dr[key];
            return val != null && val.ToString().Contains(GameDateMarker);
        }
        return false;
    }
}
