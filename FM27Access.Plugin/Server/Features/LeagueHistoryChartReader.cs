using System.Collections.Generic;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Reads the live data out of a club's league-history line chart. The game plots the season-by-season
/// finishing positions as an <c>FMLineChart</c> (a graphical line, no text nodes), so a screen reader
/// finds nothing where the chart sits. The data is in the chart's bound model: each point carries a
/// <see cref="SI.Bindable.DynamicReference"/> whose properties (resolved to names via the game's
/// PropertyIdentifierSet) include the season, division, and finishing position. The pure-HTML table is
/// built by <see cref="LeagueHistoryHtml"/>; this is the live, strongly-typed read.
/// </summary>
internal static class LeagueHistoryChartReader
{
    // National-team history uses an inverted plot value (209 for rank 2 in
    // the observed chart). Read its actual FifaRanking, never that coordinate.
    // The same native points carry calendar year/month and their displayed label.
    internal static List<FinanceChartReader.Point> TryReadWorldRanking(VisualElement element)
    {
        var chart = element.TryCast<SI.Bindable.Charts.SILineChart>();
        if (chart?.Series?.Count != 1) return null;
        var points = chart.Series[0].DataPoints;
        if (points == null || points.Count == 0) return null;
        var result = new List<FinanceChartReader.Point>();
        foreach (var point in points)
        {
            if (!point.HasValue || point.DataRef == null || string.IsNullOrWhiteSpace(point.Label)) return null;
            var fields = BindingRecords.Fields(point.DataRef);
            var rank = BindingRecords.NumberOf(BindingRecords.Field(fields, "FifaRanking"));
            var year = BindingRecords.NumberOf(BindingRecords.Field(fields, "Year"));
            var month = BindingRecords.NumberOf(BindingRecords.Field(fields, "Months"));
            if (rank == null || !double.IsFinite(rank.Value) || rank < 1 || rank != System.Math.Truncate(rank.Value)
                || year == null || year < 1 || year > 9999 || year != System.Math.Truncate(year.Value)
                || month == null || month < 1 || month > 12 || month != System.Math.Truncate(month.Value)) return null;
            result.Add(new FinanceChartReader.Point(point.Label,
                rank.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)));
        }
        return result;
    }

    // The DynamicReference property names carried by each league-history data point. Read by name
    // (not by hashed id) so the read is stable across builds.
    private const string SeasonProp = "YearSeasonString";
    private const string PositionProp = "LeaguePosition";
    private const string DivisionProp = "CompetitionLongName";

    // True for the line-chart widget family we extract. The concrete league-history chart is an
    // FMLineChart; SILineChart is its base. Other chart shapes (pie, scatter, momentum) are distinct
    // types handled elsewhere or not yet read.
    public static bool IsLineChart(VisualElement el)
        => IsLineChart(el, el.GetIl2CppType().Name);

    // Overload for callers that have already resolved the IL2CPP type name, to avoid a second marshal.
    public static bool IsLineChart(VisualElement el, string typeName)
        => typeName == "FMLineChart" || typeName == "SILineChart";

    // Reads the season-by-season rows out of a league-history line chart. Returns null when the
    // element is not a readable league-history chart — no series, no points, or points that don't
    // carry the league-history properties (some other line chart) — so the caller degrades in place
    // rather than emitting a half-empty table. Throws are left to the caller's boundary catch.
    public static List<LeagueHistoryRow> TryRead(VisualElement chartEl)
    {
        var chart = chartEl.TryCast<SI.Bindable.Charts.SILineChart>();
        if (chart == null) return null;

        var series = chart.Series;
        if (series == null || series.Count == 0) return null;

        var points = series[0].DataPoints;
        if (points == null || points.Count == 0) return null;

        var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
        var rows = new List<LeagueHistoryRow>(points.Count);

        for (int i = 0; i < points.Count; i++)
        {
            var dr = points[i].DataRef;
            if (dr == null) return null;

            string season = null, position = null, division = null;
            var en = dr.Keys.GetEnumerator();
            while (en.MoveNext())
            {
                var key = en.Current;
                string name = reg.GetInfo((uint)key).Name;
                if (name == SeasonProp) season = ReadValue(dr[key]);
                else if (name == PositionProp) position = ReadValue(dr[key]);
                else if (name == DivisionProp) division = ReadValue(dr[key]);
            }

            // The signature of a league-history chart: every point names a season and a position.
            // A point missing them means this is a line chart of something else we don't read yet.
            if (string.IsNullOrEmpty(season) || string.IsNullOrEmpty(position)) return null;
            rows.Add(new LeagueHistoryRow(season, division, position));
        }

        return rows.Count > 0 ? rows : null;
    }

    // A TypedValue stringifies as "SI.Core.NumericTypedValue, 17" / "SI.Core.ReferenceTypedValue,
    // Sky Bet Championship". Take everything after the first comma — the type prefix never contains
    // one, and a value (a division name) might — matching the established PresetButtonAdapter read.
    // The game exposes no non-generic typed accessor and the generic Get<T>() is ambiguous under IL2CPP.
    private static string ReadValue(Il2CppSystem.Object typedValue)
    {
        if (typedValue == null) return null;
        string raw = typedValue.ToString();
        int comma = raw.IndexOf(',');
        return comma >= 0 ? raw.Substring(comma + 1).Trim() : raw;
    }
}
