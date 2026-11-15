using System;
using System.Collections.Generic;
using System.Globalization;

namespace FM27Access.Plugin.Server;

/// <summary>
/// What a Data Hub scatter card says about the team it highlights, as a table: for each of the two
/// measures it plots, the team's value against the league's average, highest and lowest, and its
/// rank, plus the quadrant it falls in (in the game's own words). The other points are teams the
/// card names nowhere (references only), so they appear only through the average, range and rank.
/// Pure, so the tests compile it in; the live read is <c>Features/DataHubScatter.cs</c>.
///
/// All-or-nothing: the points must reproduce the card's own average, highest and lowest for both
/// measures (proving the list is the whole league the chart draws), and exactly one point must be
/// the highlighted team.
/// </summary>
internal static class ScatterSummary
{
    internal readonly struct Axis
    {
        public readonly string Name;
        public readonly double Mean, Min, Max;
        public Axis(string name, double mean, double min, double max) { Name = name; Mean = mean; Min = min; Max = max; }
    }

    internal readonly struct Point
    {
        public readonly double X, Y;
        public readonly bool Highlighted;
        public Point(double x, double y, bool highlighted) { X = x; Y = y; Highlighted = highlighted; }
    }

    internal sealed class Result
    {
        public string[] Headers;
        public List<List<string>> Rows = new();
        public string Quadrant;     // null when the team sits exactly on an average line
    }

    // Mod-authored chrome.
    private const string MeasureHeader = "Measure";
    private const string AverageHeader = "League average";
    private const string HighestHeader = "Highest";
    private const string LowestHeader = "Lowest";
    private const string RankHeader = "Rank, highest first";
    internal const string QuadrantPrefix = "Quadrant: ";

    // The card's own figures are floats; a recomputed mean of 20 of them differs in the last places.
    private const double Tolerance = 1e-3;

    /// <summary>
    /// <paramref name="quadrants"/> is indexed by the game's QuadrantZone: bit 0 set above the
    /// vertical measure's average, bit 1 above the horizontal measure's (verified against the drawn
    /// quadrant labels on all three team scatter cards).
    /// </summary>
    internal static Result Build(IReadOnlyList<Point> points, Axis y, Axis x, IReadOnlyList<string> quadrants,
        string teamHeader, out string reason)
    {
        reason = null;
        if (points.Count < 2) { reason = $"{points.Count} points"; return null; }
        int highlighted = 0;
        Point team = default;
        foreach (var p in points) if (p.Highlighted) { highlighted++; team = p; }
        if (highlighted != 1) { reason = $"{highlighted} highlighted points"; return null; }
        if (!Matches(points, y, p => p.Y, out reason) || !Matches(points, x, p => p.X, out reason)) return null;

        var result = new Result { Headers = new[] { MeasureHeader, teamHeader, AverageHeader, HighestHeader, LowestHeader, RankHeader } };
        result.Rows.Add(Row(y, team.Y, points, p => p.Y));
        result.Rows.Add(Row(x, team.X, points, p => p.X));

        if (team.Y != y.Mean && team.X != x.Mean && quadrants.Count == 4)
        {
            int zone = (team.Y > y.Mean ? 1 : 0) + (team.X > x.Mean ? 2 : 0);
            if (!string.IsNullOrEmpty(quadrants[zone])) result.Quadrant = QuadrantPrefix + quadrants[zone];
        }
        return result;
    }

    private static bool Matches(IReadOnlyList<Point> points, Axis axis, Func<Point, double> value, out string reason)
    {
        reason = null;
        double sum = 0, min = double.MaxValue, max = double.MinValue;
        foreach (var p in points)
        {
            double v = value(p);
            sum += v;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        double mean = sum / points.Count;
        if (Math.Abs(mean - axis.Mean) > Tolerance || Math.Abs(min - axis.Min) > Tolerance || Math.Abs(max - axis.Max) > Tolerance)
        {
            reason = $"{axis.Name}: points give mean {mean:0.###} range {min:0.###}-{max:0.###}, card has {axis.Mean:0.###} {axis.Min:0.###}-{axis.Max:0.###}";
            return false;
        }
        return true;
    }

    private static List<string> Row(Axis axis, double team, IReadOnlyList<Point> points, Func<Point, double> value)
        => new() { axis.Name, F(team), F(axis.Mean), F(axis.Max), F(axis.Min), Rank(team, points, value) };

    // ---------------------------------------------------------------- player scatters

    internal sealed class PlayersResult
    {
        public string[] ContextHeaders;
        public List<List<string>> ContextRows = new();
        public string[] PlayerHeaders;
        public List<List<string>> PlayerRows = new();
        public List<int> PlayerPoints = new();   // the point index behind each player row
    }

    // Mod-authored chrome for player scatters. The points are every player of one position group in
    // the division, so "Average", not "League average".
    private const string GroupAverageHeader = "Average";
    private const string PlayerHeader = "Player";
    private const string PlayerLabel = "Player ";
    private const string QuadrantHeader = "Quadrant";

    /// <summary>
    /// A player scatter: the position group's average, highest and lowest on each measure, then a
    /// row per highlighted (our) player: both values, each with its rank in the group, and the
    /// quadrant, best first on the vertical measure. The game names none of the players, so rows are
    /// numbered in that order; each row's point is returned so the caller can let the row open that
    /// player. Same completeness check as <see cref="Build"/>; zero of our players is allowed (the
    /// group context still reads).
    /// </summary>
    internal static PlayersResult BuildPlayers(IReadOnlyList<Point> points, Axis y, Axis x, IReadOnlyList<string> quadrants, out string reason)
    {
        reason = null;
        if (points.Count < 2) { reason = $"{points.Count} points"; return null; }
        if (!Matches(points, y, p => p.Y, out reason) || !Matches(points, x, p => p.X, out reason)) return null;

        var result = new PlayersResult
        {
            ContextHeaders = new[] { MeasureHeader, GroupAverageHeader, HighestHeader, LowestHeader },
            PlayerHeaders = new[] { PlayerHeader, y.Name, RankHeader, x.Name, RankHeader, QuadrantHeader },
        };
        result.ContextRows.Add(new List<string> { y.Name, F(y.Mean), F(y.Max), F(y.Min) });
        result.ContextRows.Add(new List<string> { x.Name, F(x.Mean), F(x.Max), F(x.Min) });

        var ours = new List<int>();
        for (int i = 0; i < points.Count; i++) if (points[i].Highlighted) ours.Add(i);
        ours.Sort((a, b) =>
        {
            int c = points[b].Y.CompareTo(points[a].Y);
            return c != 0 ? c : a.CompareTo(b);
        });

        for (int n = 0; n < ours.Count; n++)
        {
            var p = points[ours[n]];
            string quadrant = "";
            if (p.Y != y.Mean && p.X != x.Mean && quadrants.Count == 4)
                quadrant = quadrants[(p.Y > y.Mean ? 1 : 0) + (p.X > x.Mean ? 2 : 0)] ?? "";
            result.PlayerRows.Add(new List<string>
            {
                PlayerLabel + (n + 1).ToString(CultureInfo.InvariantCulture),
                F(p.Y), Rank(p.Y, points, q => q.Y),
                F(p.X), Rank(p.X, points, q => q.X),
                quadrant,
            });
            result.PlayerPoints.Add(ours[n]);
        }
        return result;
    }

    private static string Rank(double value, IReadOnlyList<Point> points, Func<Point, double> of)
    {
        int above = 0;
        foreach (var p in points) if (of(p) > value) above++;
        return Ordinal(above + 1) + " of " + points.Count.ToString(CultureInfo.InvariantCulture);
    }

    // The card's axes print two decimals ("90.00").
    private static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    internal static string Ordinal(int n)
    {
        string suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString(CultureInfo.InvariantCulture) + suffix;
    }
}
