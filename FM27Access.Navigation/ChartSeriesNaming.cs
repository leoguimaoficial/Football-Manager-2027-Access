using System;
using System.Collections.Generic;
using System.Globalization;

namespace FM27Access.Navigation;

/// <summary>
/// Joins a multi-series line chart's series back to the rows that selected them — the progress
/// report's attribute graph, where every ticked attribute draws a line.
///
/// Nothing on a series names it. <c>SILineChart.SeriesData</c> exposes only a
/// <c>StyleColourIndex</c>, and that is not a key: seventeen live series came back with twelve
/// distinct indices, so the colours repeat. The data points carry no attribute id.
///
/// Position is not a key either. The chart's series order is neither the table's render order nor
/// ascending attribute id (both were tested live against a nineteen-line chart and both failed on
/// the Mental block). So the only thing tying a line to a row is the numbers it plots.
///
/// The rule is therefore: match by current rating, and accept the match when it cannot be wrong.
/// Rows and series are grouped by that rating. A group of one is settled outright. A group of
/// several is settled too when every line in it plots an identical sequence — attributes on the
/// same rating almost always have the same flat history, and when they do, every possible
/// assignment within the group produces exactly the same table, so picking one is not a guess.
/// Only a group whose lines actually differ is ambiguous, and those columns are dropped and named
/// rather than labelled on a coin flip.
///
/// This is what replaced an earlier rule that demanded every ticked attribute be on a different
/// rating. That was correct but hostile: on a real selection several attributes always share a
/// rating, so the chart almost never rendered.
///
/// Pure and game-free so the matching and refusal rules are unit-testable.
/// </summary>
public static class ChartSeriesNaming
{
    /// <summary>A row that ticked itself onto the chart: its name, and the rating it currently shows.</summary>
    public readonly struct Candidate
    {
        public readonly string Name;
        public readonly string Value;

        public Candidate(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>Why a match produced what it did, so the caller can say something useful.</summary>
    public enum Outcome
    {
        /// <summary>At least one line was named. Some may still be ambiguous.</summary>
        Named,
        /// <summary>No tick boxes: an ability graph or a finance history, never a selection chart.</summary>
        NoSelection,
        /// <summary>The ticked rows and the plotted lines do not describe the same set of ratings.</summary>
        CountMismatch,
        /// <summary>A row had no name, or a rating on either side would not read.</summary>
        Unreadable,
    }

    /// <summary>The outcome of a match: which lines got names, and which could not be told apart.</summary>
    public sealed class Match
    {
        /// <summary>One entry per series, in series order. Null where the line is ambiguous.</summary>
        public string[] Names { get; }

        /// <summary>Names of the attributes whose lines could not be separated from each other.</summary>
        public IReadOnlyList<string> Ambiguous { get; }

        public Outcome Outcome { get; }

        internal Match(string[] names, List<string> ambiguous, Outcome outcome)
        {
            Names = names;
            Ambiguous = ambiguous ?? new List<string>();
            Outcome = outcome;
        }
    }

    /// <summary>
    /// Matches each plotted line to the row that selected it.
    /// </summary>
    /// <param name="series">Each line's plotted values, oldest first, in the chart's series order.</param>
    /// <param name="ticked">The rows that put a line on the chart.</param>
    public static Match TryMatch(
        IReadOnlyList<IReadOnlyList<string>> series, IReadOnlyList<Candidate> ticked)
    {
        if (series == null || ticked == null) return Fail(Outcome.Unreadable);
        if (ticked.Count == 0) return Fail(Outcome.NoSelection);
        if (series.Count != ticked.Count) return Fail(Outcome.CountMismatch);

        // Rows by the rating they show.
        var rowsByValue = new Dictionary<double, List<string>>();
        foreach (var row in ticked)
        {
            if (string.IsNullOrWhiteSpace(row.Name) || !TryValue(row.Value, out double v))
                return Fail(Outcome.Unreadable);
            if (!rowsByValue.TryGetValue(v, out var list))
                rowsByValue[v] = list = new List<string>();
            list.Add(row.Name.Trim());
        }

        // Lines by the rating they end on.
        var seriesByValue = new Dictionary<double, List<int>>();
        for (int s = 0; s < series.Count; s++)
        {
            var pts = series[s];
            if (pts == null || pts.Count == 0) return Fail(Outcome.Unreadable);
            if (!TryValue(pts[pts.Count - 1], out double v)) return Fail(Outcome.Unreadable);
            if (!seriesByValue.TryGetValue(v, out var list))
                seriesByValue[v] = list = new List<int>();
            list.Add(s);
        }

        // The two sides must describe the same ratings in the same quantities, or the lines and the
        // rows are not the same set of attributes and nothing can be matched.
        if (rowsByValue.Count != seriesByValue.Count) return Fail(Outcome.CountMismatch);
        foreach (var pair in rowsByValue)
            if (!seriesByValue.TryGetValue(pair.Key, out var idx) || idx.Count != pair.Value.Count)
                return Fail(Outcome.CountMismatch);

        var names = new string[series.Count];
        var ambiguous = new List<string>();
        foreach (var pair in rowsByValue)
        {
            var rowNames = pair.Value;
            var indices = seriesByValue[pair.Key];

            // Several lines on one rating are interchangeable only if they plot the same story. If
            // they do, any assignment yields the same table; if they don't, no assignment is safe.
            if (rowNames.Count > 1 && !AllSequencesEqual(series, indices))
            {
                ambiguous.AddRange(rowNames);
                continue;
            }
            for (int i = 0; i < indices.Count; i++)
                names[indices[i]] = rowNames[i];
        }

        // Every group ambiguous means nothing was named; that is a refusal, not a partial result.
        bool anyNamed = false;
        for (int i = 0; i < names.Length; i++) if (names[i] != null) { anyNamed = true; break; }
        if (!anyNamed) return new Match(null, ambiguous, Outcome.CountMismatch);

        ambiguous.Sort(StringComparer.Ordinal);
        return new Match(names, ambiguous, Outcome.Named);
    }

    private static Match Fail(Outcome outcome) => new Match(null, null, outcome);

    private static bool AllSequencesEqual(
        IReadOnlyList<IReadOnlyList<string>> series, List<int> indices)
    {
        var first = series[indices[0]];
        for (int i = 1; i < indices.Count; i++)
        {
            var other = series[indices[i]];
            if (other.Count != first.Count) return false;
            for (int p = 0; p < first.Count; p++)
            {
                if (!TryValue(first[p], out double a) || !TryValue(other[p], out double b)) return false;
                if (a != b) return false;
            }
        }
        return true;
    }

    // A plotted value or a cell's text as a number. The two sides come from different formatters,
    // so "8", "8.0" and " 8 " are one rating and must not read as three.
    private static bool TryValue(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return double.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }
}
