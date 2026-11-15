using System;
using System.Collections.Generic;
using System.Globalization;

namespace FM27Access.Plugin.Adapters;

// An unlabelled sparkline exposes its shape, not the numeric attribute/unit
// behind it. Only segment directions and the actual displayed point count leave
// this formatter. Missing/nonfinite points never interpolate a fabricated line.
internal static class LineChartTrend
{
    internal static string Format(IReadOnlyList<double?> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0) return "No plotted points";
        if (points.Count == 1) return "1 plotted point";

        var segments = new string[points.Count - 1];
        for (int i = 1; i < points.Count; i++)
        {
            var before = points[i - 1];
            var after = points[i];
            segments[i - 1] = before is null || after is null
                || !double.IsFinite(before.Value) || !double.IsFinite(after.Value)
                ? "gap" : after > before ? "up" : after < before ? "down" : "level";
        }
        return points.Count.ToString(CultureInfo.InvariantCulture)
            + " points, left to right: " + string.Join(", ", segments);
    }
}
