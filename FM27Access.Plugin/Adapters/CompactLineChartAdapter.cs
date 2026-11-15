using System;
using System.Collections.Generic;
using System.Reflection;
using FM27Access.Speech;
using SI.Bindable.Charts;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

// Reuse the original readonly text stop and semantic table-cell presentation.
// Restricted to a cell's sole compact integer line chart with both axes hidden.
// Date/value-labelled finance, history and attribute charts retain their readers.
internal sealed class CompactLineChartAdapter : PluginAdapterBase
{
    private readonly SILineChart chart;
    private static readonly PropertyInfo AxisType = typeof(SILineChart).GetProperty(
        "Y_AxisLabelType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    private CompactLineChartAdapter(SILineChart chart) : base(chart) => this.chart = chart;

    internal static CompactLineChartAdapter TryCreate(VisualElement cell)
    {
        // The observed native cell is Cell -> BindingExpect -> FMLineChart.
        // Only traverse a single visible wrapper chain; do not consume another
        // widget, a caption, sibling content or a larger containing card.
        var current = cell;
        for (int depth = 0; current != null && depth < 5; depth++)
        {
            string kind = current.GetIl2CppType().Name;
            if (kind is "FMLineChart" or "SILineChart")
            {
                var line = current.TryCast<SILineChart>();
                if (line == null || !line.HideAxisLines || !line.DisableXAxisLabels
                    || !line.DisableYAxisLabels || line.MaxPointsOnLine <= 0) return null;
                if (AxisType == null) throw new MissingMemberException(typeof(SILineChart).FullName, "Y_AxisLabelType");
                if (AxisType.GetValue(line)?.ToString() != "Integer") return null;
                return new CompactLineChartAdapter(line);
            }
            VisualElement only = null;
            for (int n = 0; n < current.hierarchy.childCount; n++)
            {
                var child = current.hierarchy[n];
                if (!ElementBridge.IsVisible(child)) continue;
                if (only != null) return null;
                only = child;
            }
            current = only;
        }
        return null;
    }

    public override string Role => "text";
    public override bool CanActivate => false;
    public override string GetState() => "";
    public override string GetAnnouncement() => GetLabel();
    public override string GetDescription() =>
        "The direction of each drawn segment from left to right. The game does not label dates or numeric values on this compact chart.";
    public override void Activate() => throw new NotSupportedException("Chart trends are readonly.");

    // Use native binding/layout state in the existing across-frame row harvest.
    // A not-yet-created plotted window must not be cached as an empty graph.
    internal bool IsReadyToRead
    {
        get
        {
            if (!chart.IsBound || chart.Series == null || chart.m_chartPointIndices == null) return false;
            if (chart.Series.Count != 1) return true; // Empty or explicitly unsupported, not a pending single line.
            var points = chart.Series[0].DataPoints;
            if (points == null) return false;
            if (chart.m_chartPointIndices.Count > 0) return true;
            foreach (var point in points)
                if (point.HasValue) return false; // Bound values still await their native plotted mapping.
            return true; // The bound line genuinely has no drawable points.
        }
    }

    public override string GetLabel()
    {
        try
        {
            if (!IsReadyToRead)
                return Degrade.Read("[CompactLineChart] Native binding/plotted window is not ready.");
            var series = chart.Series;
            var displayed = chart.m_chartPointIndices;
            if (series == null || displayed == null)
                return Degrade.Read("[CompactLineChart] Native plotted data is not bound yet.");
            if (series.Count == 0 && displayed.Count == 0) return LineChartTrend.Format(Array.Empty<double?>());
            if (series.Count != 1)
                return Degrade.Read("[CompactLineChart] A cell's unlabelled lines cannot be associated unambiguously.");
            var points = series[0].DataPoints;
            if (displayed.Count == 0) return LineChartTrend.Format(Array.Empty<double?>());

            var values = new List<double?>(displayed.Count);
            int previous = -1;
            for (int i = 0; i < displayed.Count; i++)
            {
                // Use the actual rendered point mapping, not an assumed first
                // window of the source series or a guessed six-month interval.
                var index = displayed[i];
                if (index.x != 0 || index.y <= previous || index.y < 0 || index.y >= points.Count
                    || previous >= 0 && index.y != previous + 1)
                    return Degrade.Read("[CompactLineChart] Native point order/window is not coherent.");
                var point = points[index.y];
                if (!string.IsNullOrWhiteSpace(point.Label) || point.DataRef != null)
                    return Degrade.Read("[CompactLineChart] Labelled data requires its original date/value reader.");
                values.Add(point.HasValue ? point.Value : null);
                previous = index.y;
            }
            return LineChartTrend.Format(values);
        }
        catch (Exception ex)
        {
            return Degrade.Read($"[CompactLineChart] Native plotted trend read failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
