using System;
using System.Collections.Generic;
using System.Globalization;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Widens a radar chart's stat table (one row per axis: label, printed figure) into one column per
/// plotted series, headed by the legend's names. The chart prints only the first series; the rest
/// come from the chart's binding (<see cref="RadarSeriesReader"/>) as raw numbers, so this formats
/// them the way the game formats the printed one.
///
/// All-or-nothing, and self-checking: the first series' raw number must format to exactly the
/// figure the game printed on every row, or nothing is added. That proves the rows and the binding
/// describe the same chart, in the same order, under the same rounding, before any number the
/// player cannot verify is shown. Pure, so the tests compile it in.
/// </summary>
internal static class RadarColumns
{
    internal readonly struct Axis
    {
        public readonly string Label;
        public readonly double?[] Series;
        public Axis(string label, double?[] series) { Label = label; Series = series; }
    }

    /// <summary>
    /// Appends series 1..N to each row of <paramref name="rows"/> and returns the widened headers
    /// (<paramref name="labelHeader"/> then the legend names), or null with <paramref name="reason"/>
    /// set, leaving <paramref name="rows"/> untouched.
    /// </summary>
    internal static string[] TryWiden(List<List<string>> rows, IReadOnlyList<Axis> axes,
        IReadOnlyList<string> legend, string labelHeader, out string reason)
    {
        reason = null;
        if (legend.Count < 2) { reason = $"legend names {legend.Count} series"; return null; }
        if (rows.Count != axes.Count) { reason = $"{rows.Count} rows against {axes.Count} axes"; return null; }

        var added = new List<string[]>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Count != 2) { reason = $"row \"{string.Join(" | ", row)}\" has {row.Count} cells"; return null; }

            int match = -1;
            for (int a = 0; a < axes.Count; a++)
            {
                if (axes[a].Label != row[0]) continue;
                if (match >= 0) { reason = $"axis label \"{row[0]}\" appears twice"; return null; }
                match = a;
            }
            if (match < 0) { reason = $"no axis labelled \"{row[0]}\""; return null; }

            var series = axes[match].Series;
            if (series.Length != legend.Count) { reason = $"\"{row[0]}\" plots {series.Length} series, legend names {legend.Count}"; return null; }
            if (series[0] is not double printed || FormatLike(row[1], printed) != row[1])
            {
                reason = $"\"{row[0]}\" printed {row[1]}, binding holds {series[0]?.ToString("R", CultureInfo.InvariantCulture) ?? "nothing"}";
                return null;
            }

            var cells = new string[series.Length - 1];
            for (int s = 1; s < series.Length; s++)
            {
                if (series[s] is not double v) { reason = $"\"{row[0]}\" has no figure for {legend[s]}"; return null; }
                cells[s - 1] = FormatLike(row[1], v);
            }
            added.Add(cells);
        }

        for (int r = 0; r < rows.Count; r++)
            rows[r].AddRange(added[r]);
        var headers = new string[legend.Count + 1];
        headers[0] = labelHeader;
        for (int i = 0; i < legend.Count; i++) headers[i + 1] = legend[i];
        return headers;
    }

    /// <summary>
    /// A radar that prints no figures at all (the Coach Report's), as a table built from its binding
    /// alone: one row per axis, the axis name and one column per series, to one decimal (how the
    /// Data Hub's radar prints the same statistics). There is no printed figure to check against, so
    /// the shape is the check: every axis named, exactly <paramref name="seriesCount"/>
    /// series, every value present — or null with <paramref name="reason"/>.
    /// </summary>
    internal static List<List<string>> ValueTable(IReadOnlyList<Axis> axes, int seriesCount, out string reason)
    {
        reason = null;
        if (axes.Count == 0) { reason = "no axes"; return null; }
        var rows = new List<List<string>>(axes.Count);
        foreach (var axis in axes)
        {
            if (string.IsNullOrWhiteSpace(axis.Label)) { reason = "an axis has no name"; return null; }
            if (axis.Series.Length != seriesCount) { reason = $"\"{axis.Label}\" plots {axis.Series.Length} series, expected {seriesCount}"; return null; }
            var row = new List<string> { axis.Label };
            foreach (var v in axis.Series)
            {
                if (v is not double d) { reason = $"\"{axis.Label}\" is missing a value"; return null; }
                row.Add(d.ToString("0.0", CultureInfo.InvariantCulture));
            }
            rows.Add(row);
        }
        return rows;
    }

    // The game's rich-text link element, and any tag inside its text.
    private static readonly System.Text.RegularExpressions.Regex LinkElement =
        new(@"<link=[^>]*>(.*?)</link>", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);
    private static readonly System.Text.RegularExpressions.Regex AnyTag =
        new(@"<[^>]*>", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// The competition a radar's caption names, read from the caption's raw game text: the text of its
    /// one link element (the Coach Report writes <c>&lt;link="0"&gt;Serie A&lt;/link&gt;, This Season</c>,
    /// the competition linked whatever league it is). Null unless there is exactly one link with a
    /// non-empty name, so a caption of another shape never names a column.
    /// </summary>
    internal static string LinkedName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var links = LinkElement.Matches(raw);
        if (links.Count != 1) return null;
        string name = AnyTag.Replace(links[0].Groups[1].Value, "").Trim();
        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// <paramref name="value"/> written with the decimal places and decimal mark of
    /// <paramref name="printed"/>, or null when <paramref name="printed"/> is not a plain figure
    /// (a grouping separator, a unit, a sign other than a leading minus).
    /// </summary>
    internal static string FormatLike(string printed, double value)
    {
        if (string.IsNullOrEmpty(printed)) return null;
        int mark = printed.LastIndexOfAny(new[] { '.', ',' });
        int start = printed[0] == '-' ? 1 : 0;
        for (int i = start; i < printed.Length; i++)
            if (i != mark && !char.IsDigit(printed[i])) return null;
        if (mark == start || mark == printed.Length - 1) return null;

        int decimals = mark < 0 ? 0 : printed.Length - mark - 1;
        string s = value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        return mark >= 0 && printed[mark] == ',' ? s.Replace('.', ',') : s;
    }
}
