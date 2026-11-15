using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The tables that replace the Data Hub's Last Match drawings (the xG match story, the match
/// momentum bars, the pass maps), built from the figures those drawings are bound to. Pure, so the
/// tests compile it in; the live reads are in <c>Features/LastMatchCharts.cs</c>.
///
/// Every builder is all-or-nothing and returns a reason when it declines: a table the reads cannot
/// fully support is never shown, so the card keeps reading exactly as it did.
/// </summary>
internal static class LastMatchTables
{
    internal sealed class Table
    {
        public string[] Headers;
        public List<List<string>> Rows = new();
    }

    internal enum Side { Home, Away, None }

    // ---------------------------------------------------------------- xG match story

    internal readonly struct Shot
    {
        public readonly double Minute;
        public readonly double Xg;
        public readonly bool Goal;
        public readonly bool OwnGoal;
        public Shot(double minute, double xg, bool goal, bool ownGoal) { Minute = minute; Xg = xg; Goal = goal; OwnGoal = ownGoal; }
    }

    // Mod-authored chrome for the shot table.
    private const string MinuteHeader = "Minute";
    private const string TeamHeader = "Team";
    private const string ChanceXgHeader = "Chance xG";
    private const string ResultHeader = "Result";
    private const string RunningXgSuffix = " xG";
    private const string GoalResult = "Goal";
    private const string OwnGoalResult = "Own goal";

    // "2.19 - 0.19": the card's printed xG totals, home first.
    private static readonly Regex PrintedPair = new(@"^\s*(\S+)\s+-\s+(\S+)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// One row per shot in match order: the minute the chart plots it at (rounded), the team, the
    /// chance's xG, a goal or own goal, and each side's running total (what the two step lines
    /// draw). Declines unless each side's shots sum to the card's printed total for that side, which
    /// proves the shot lists, their order (home first) and the rounding all match what is printed.
    /// </summary>
    internal static Table XgStory(IReadOnlyList<Shot> home, IReadOnlyList<Shot> away,
        string homeName, string awayName, string printedTotals, out string reason)
    {
        reason = null;
        var m = PrintedPair.Match(printedTotals ?? "");
        if (!m.Success) { reason = $"printed totals \"{printedTotals}\" are not a home - away pair"; return null; }
        double homeTotal = Sum(home), awayTotal = Sum(away);
        string printedHome = m.Groups[1].Value, printedAway = m.Groups[2].Value;
        if (RadarColumns.FormatLike(printedHome, homeTotal) != printedHome
            || RadarColumns.FormatLike(printedAway, awayTotal) != printedAway)
        {
            reason = $"shots sum to {homeTotal:0.###} - {awayTotal:0.###}, card prints {printedTotals}";
            return null;
        }

        var all = new List<(Shot Shot, bool Home)>();
        foreach (var s in home) all.Add((s, true));
        foreach (var s in away) all.Add((s, false));
        var ordered = new List<(Shot Shot, bool Home)>();
        StableByMinute(ordered, all);

        var table = new Table { Headers = new[] { MinuteHeader, TeamHeader, ChanceXgHeader, ResultHeader, homeName + RunningXgSuffix, awayName + RunningXgSuffix } };
        double runHome = 0, runAway = 0;
        foreach (var (shot, isHome) in ordered)
        {
            if (isHome) runHome += shot.Xg; else runAway += shot.Xg;
            table.Rows.Add(new List<string>
            {
                WholeMinute(shot.Minute).ToString(CultureInfo.InvariantCulture),
                isHome ? homeName : awayName,
                FormatLike(printedHome, shot.Xg),
                shot.OwnGoal ? OwnGoalResult : shot.Goal ? GoalResult : "",
                FormatLike(printedHome, runHome),
                FormatLike(printedAway, runAway),
            });
        }
        return table;
    }

    /// <summary>The chart's minute as a sighted reader takes it off the axis: rounded to the nearest whole minute.</summary>
    internal static int WholeMinute(double minute) => (int)Math.Round(minute, MidpointRounding.AwayFromZero);

    // Match order. List.Sort is unstable, so ties keep source order: shots at the same moment read
    // home before away, each side in its own order.
    private static void StableByMinute(List<(Shot Shot, bool Home)> into, List<(Shot Shot, bool Home)> source)
    {
        var indexed = new List<(int Index, (Shot Shot, bool Home) Item)>();
        for (int i = 0; i < source.Count; i++) indexed.Add((i, source[i]));
        indexed.Sort((a, b) =>
        {
            int c = a.Item.Shot.Minute.CompareTo(b.Item.Shot.Minute);
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        });
        into.Clear();
        foreach (var x in indexed) into.Add(x.Item);
    }

    private static double Sum(IReadOnlyList<Shot> shots)
    {
        double t = 0;
        foreach (var s in shots) t += s.Xg;
        return t;
    }

    private static string FormatLike(string printed, double value) => RadarColumns.FormatLike(printed, value);

    // ---------------------------------------------------------------- match momentum

    internal readonly struct Period
    {
        public readonly double Value;   // positive: the home team on top; negative: the away team
        public readonly Side Team;      // the record's own team, cross-checked against the sign
        public Period(double value, Side team) { Value = value; Team = team; }
    }

    internal readonly struct Goal
    {
        public readonly int Period;
        public readonly Side Team;
        public readonly int Minute;
        public Goal(int period, Side team, int minute) { Period = period; Team = team; Minute = minute; }
    }

    // Mod-authored chrome for the momentum table.
    private const string PeriodHeader = "Period";
    private const string OnTopHeader = "On top";
    private const string GoalsHeader = "Goals";
    private const string EvenPeriod = "Even";
    private const string GoalWord = " goal ";
    private const string FirstHalf = "1st half ";
    private const string SecondHalf = "2nd half ";

    // The chart draws 18 periods, the first nine left of its half-time line and the rest right of it
    // (verified on screen, home and away). A match still in play has drawn only the periods so far,
    // filling from the left: the half-time report's chart holds exactly the nine before the line
    // (verified 2026-10-05). More than 18 (extra time) is numbered straight through.
    internal const int PeriodsPerMatch = 18;

    /// <summary>
    /// One row per period: which team was on top, by how much (the bar's height, the chart's
    /// "Danger of Possession"), and any goals in it with their minute. Declines when a period's own
    /// team contradicts the side its sign puts on top: then the sign convention is not what was
    /// verified, and naming a team could be wrong.
    /// </summary>
    internal static Table Momentum(IReadOnlyList<Period> periods, IReadOnlyList<Goal> goals,
        string homeName, string awayName, string valueHeader, out string reason)
    {
        reason = null;
        if (periods.Count == 0) { reason = "no periods"; return null; }

        var table = new Table { Headers = new[] { PeriodHeader, OnTopHeader, valueHeader, GoalsHeader } };
        for (int i = 0; i < periods.Count; i++)
        {
            var p = periods[i];
            Side top = p.Value > 0 ? Side.Home : p.Value < 0 ? Side.Away : Side.None;
            if (top != Side.None && p.Team != top)
            {
                reason = $"period {i + 1}: value {p.Value:0.###} puts {top} on top, record names {p.Team}";
                return null;
            }

            // In match order: the record lists a period's home goals before its away goals.
            var inPeriod = new List<Goal>();
            foreach (var g in goals)
                if (g.Period == i) inPeriod.Add(g);
            var ordered = new List<Goal>(inPeriod.Count);
            for (int k = 0; k < inPeriod.Count; k++)
            {
                int at = ordered.Count;
                while (at > 0 && ordered[at - 1].Minute > inPeriod[k].Minute) at--;
                ordered.Insert(at, inPeriod[k]);
            }
            var goalTexts = new List<string>();
            foreach (var g in ordered)
                goalTexts.Add((g.Team == Side.Home ? homeName : awayName) + GoalWord + g.Minute.ToString(CultureInfo.InvariantCulture));

            table.Rows.Add(new List<string>
            {
                PeriodLabel(i, periods.Count),
                top == Side.Home ? homeName : top == Side.Away ? awayName : EvenPeriod,
                top == Side.None ? "" : Math.Abs(p.Value).ToString("0.00", CultureInfo.InvariantCulture),
                string.Join("; ", goalTexts),
            });
        }
        return table;
    }

    internal static string PeriodLabel(int index, int count)
    {
        if (count > PeriodsPerMatch) return (index + 1).ToString(CultureInfo.InvariantCulture);
        int half = PeriodsPerMatch / 2;
        return index < half
            ? FirstHalf + (index + 1).ToString(CultureInfo.InvariantCulture)
            : SecondHalf + (index - half + 1).ToString(CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- pass maps

    internal sealed class MapPlayer
    {
        public int Number;
        public string Name;              // null where the card names nobody (the opposition)
        public int Combinations;
        public int CompletionPercent;
        public List<(int ReceiverNumber, double Strength)> Links = new();
    }

    // Mod-authored chrome for the pass map tables.
    private const string NumberHeader = "No.";
    private const string PlayerHeader = "Player";
    private const string CombinationsHeader = "Pass combinations";
    private const string CompletionHeader = "Pass completion";
    private const string PasserHeader = "From";
    private const string ReceiverHeader = "To";
    private const string StrengthHeader = "Strength";

    /// <summary>
    /// The pass map's players: shirt number, name (where the card names them), pass combinations and
    /// pass completion. A name column only when every player has a name, so a half-named table never
    /// reads as if the unnamed rows were blank names.
    /// </summary>
    internal static Table PassMapPlayers(IReadOnlyList<MapPlayer> players)
    {
        bool named = players.Count > 0;
        foreach (var p in players) if (string.IsNullOrEmpty(p.Name)) named = false;

        var table = new Table
        {
            Headers = named
                ? new[] { NumberHeader, PlayerHeader, CombinationsHeader, CompletionHeader }
                : new[] { NumberHeader, CombinationsHeader, CompletionHeader },
        };
        foreach (var p in players)
        {
            var row = new List<string> { p.Number.ToString(CultureInfo.InvariantCulture) };
            if (named) row.Add(p.Name);
            row.Add(p.Combinations.ToString(CultureInfo.InvariantCulture));
            row.Add(p.CompletionPercent.ToString(CultureInfo.InvariantCulture) + "%");
            table.Rows.Add(row);
        }
        return table;
    }

    /// <summary>
    /// What the link table's strength figures mean, stated above it. The game scales each drawn link
    /// between the map's weakest (0) and strongest (1) and gives no pass count per link: verified
    /// against a card summary, where a link the summary puts at 3 passes carried strength 0 and was
    /// still counted among the player's links. So the figure is a position on that range, never a
    /// share of the strongest link. Mod-authored.
    /// </summary>
    internal const string LinkStrengthNote = "Link strength: 0 is this map's weakest link, 100 its strongest.";

    /// <summary>
    /// Every passing link the map draws, strongest first: who passed to whom, and the link's
    /// strength on a 0 to 100 scale between the map's weakest and strongest link (see
    /// <see cref="LinkStrengthNote"/>). Players are named where the card names them, otherwise by
    /// shirt number. Declines when a link's receiver is not a player on this map.
    /// </summary>
    internal static Table PassMapLinks(IReadOnlyList<MapPlayer> players, out string reason)
    {
        reason = null;
        var byNumber = new Dictionary<int, MapPlayer>();
        foreach (var p in players) byNumber[p.Number] = p;
        bool named = players.Count > 0;
        foreach (var p in players) if (string.IsNullOrEmpty(p.Name)) named = false;

        var links = new List<(MapPlayer From, MapPlayer To, double Strength, int Order)>();
        int order = 0;
        foreach (var p in players)
            foreach (var l in p.Links)
            {
                if (!byNumber.ContainsKey(l.ReceiverNumber)) { reason = $"{p.Number} passes to {l.ReceiverNumber}, who is not on the map"; return null; }
                if (l.Strength < 0 || l.Strength > 1) { reason = $"{p.Number} to {l.ReceiverNumber} has strength {l.Strength}, outside 0 to 1"; return null; }
                links.Add((p, byNumber[l.ReceiverNumber], l.Strength, order++));
            }
        if (links.Count == 0) { reason = "no links"; return null; }
        links.Sort((a, b) =>
        {
            int c = b.Strength.CompareTo(a.Strength);
            return c != 0 ? c : a.Order.CompareTo(b.Order);
        });

        var table = new Table { Headers = new[] { PasserHeader, ReceiverHeader, StrengthHeader } };
        foreach (var l in links)
            table.Rows.Add(new List<string>
            {
                Who(l.From, named),
                Who(l.To, named),
                Math.Round(l.Strength * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture),
            });
        return table;
    }

    private static string Who(MapPlayer p, bool named)
        => named ? p.Name : p.Number.ToString(CultureInfo.InvariantCulture);

    // A compact accessible reading of a native bottom-left-origin texture. Values
    // are shares of plotted intensity, not possession, touches or minutes played.
    internal static Table HeatMap(IReadOnlyList<double[]> columns)
    {
        if (columns == null || columns.Count < 3 || columns[0] == null || columns[0].Length < 3) return null;
        int height = columns[0].Length;
        var areas = new double[3, 3];
        double total = 0;
        for (int x = 0; x < columns.Count; x++)
        {
            var column = columns[x];
            if (column == null || column.Length != height) return null;
            for (int y = 0; y < height; y++)
            {
                double value = column[y];
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) return null;
                areas[2 - y * 3 / height, x * 3 / columns.Count] += value;
                total += value;
            }
        }
        if (double.IsInfinity(total)) return null;
        var table = new Table { Headers = new[] { "Area", "Left", "Centre", "Right" } };
        var names = new[] { "Top", "Middle", "Bottom" };
        for (int y = 0; y < 3; y++)
        {
            var row = new List<string> { names[y] };
            for (int x = 0; x < 3; x++)
                row.Add((total == 0 ? 0 : areas[y, x] / total * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%");
            table.Rows.Add(row);
        }
        return table;
    }
}
