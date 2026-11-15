using System.Collections.Generic;
using FM27Access.Navigation;

namespace FM27Access.Tests;

public class ChartSeriesNamingTests
{
    private static List<ChartSeriesNaming.Candidate> Ticked(params (string name, string value)[] rows)
    {
        var list = new List<ChartSeriesNaming.Candidate>();
        foreach (var r in rows) list.Add(new ChartSeriesNaming.Candidate(r.name, r.value));
        return list;
    }

    // A line's plotted history, oldest first.
    private static List<IReadOnlyList<string>> Series(params string[][] lines)
    {
        var list = new List<IReadOnlyList<string>>();
        foreach (var l in lines) list.Add(l);
        return list;
    }

    private static string[] Flat(string value, int points = 12)
    {
        var a = new string[points];
        for (int i = 0; i < points; i++) a[i] = value;
        return a;
    }

    [Fact]
    public void TryMatch_NamesEachLineByItsRating()
    {
        var m = ChartSeriesNaming.TryMatch(
            Series(Flat("5"), Flat("13"), Flat("8")),
            Ticked(("Dribbling", "5"), ("Balance", "13"), ("Pace", "8")));

        Assert.Equal(ChartSeriesNaming.Outcome.Named, m.Outcome);
        Assert.Equal(new[] { "Dribbling", "Balance", "Pace" }, m.Names);
        Assert.Empty(m.Ambiguous);
    }

    [Fact]
    public void TryMatch_IsIndependentOfOrder()
    {
        // The chart's series order is neither render order nor attribute id, so names must follow
        // the SERIES: line 0 here ends on 8 and is therefore Pace, whatever position Pace occupies.
        var m = ChartSeriesNaming.TryMatch(
            Series(Flat("8"), Flat("5"), Flat("13")),
            Ticked(("Dribbling", "5"), ("Balance", "13"), ("Pace", "8")));

        Assert.Equal(new[] { "Pace", "Dribbling", "Balance" }, m.Names);
    }

    [Fact]
    public void TryMatch_NamesSharedRatingsWhenTheirHistoriesAreIdentical()
    {
        // The case that makes this usable. Marking and Tackling are both on 13 and both flat, so
        // the two lines are interchangeable: either assignment produces exactly the same table.
        var m = ChartSeriesNaming.TryMatch(
            Series(Flat("13"), Flat("13"), Flat("8")),
            Ticked(("Marking", "13"), ("Tackling", "13"), ("Pace", "8")));

        Assert.Equal(ChartSeriesNaming.Outcome.Named, m.Outcome);
        Assert.Empty(m.Ambiguous);
        Assert.Equal("Pace", m.Names[2]);
        // Both 13-lines are named, and between them they carry both 13-rated attributes.
        Assert.Equal(new[] { "Marking", "Tackling" }, new[] { m.Names[0], m.Names[1] });
    }

    [Fact]
    public void TryMatch_DropsOnlyTheGroupWhoseHistoriesActuallyDiffer()
    {
        // Live shape: two attributes on 13, but one has been flat and the other fell 14 -> 13.
        // Those two genuinely cannot be separated; everything else still gets named.
        var m = ChartSeriesNaming.TryMatch(
            Series(Flat("13"), new[] { "14", "14", "13" }, Flat("8")),
            Ticked(("Decisions", "13"), ("Acceleration", "13"), ("Pace", "8")));

        Assert.Equal(ChartSeriesNaming.Outcome.Named, m.Outcome);
        Assert.Equal(new[] { "Acceleration", "Decisions" }, m.Ambiguous);
        Assert.Null(m.Names[0]);
        Assert.Null(m.Names[1]);
        Assert.Equal("Pace", m.Names[2]);
    }

    [Fact]
    public void TryMatch_RefusesWhenEveryGroupIsAmbiguous()
    {
        // Nothing survived, so this is a refusal rather than an empty-but-successful table.
        var m = ChartSeriesNaming.TryMatch(
            Series(Flat("13"), new[] { "14", "14", "13" }),
            Ticked(("Decisions", "13"), ("Acceleration", "13")));

        Assert.Null(m.Names);
        Assert.NotEqual(ChartSeriesNaming.Outcome.Named, m.Outcome);
    }

    [Fact]
    public void TryMatch_NamesASingleSeries()
    {
        var m = ChartSeriesNaming.TryMatch(Series(Flat("8")), Ticked(("Pace", "8")));
        Assert.Equal(ChartSeriesNaming.Outcome.Named, m.Outcome);
        Assert.Equal(new[] { "Pace" }, m.Names);
    }

    [Fact]
    public void TryMatch_ReportsNoSelectionWhenNothingIsTicked()
    {
        // The ability graph and the finance histories: lines, but no tick boxes anywhere. The
        // caller keeps its plain Date/Value table.
        var m = ChartSeriesNaming.TryMatch(Series(Flat("17")), Ticked());
        Assert.Null(m.Names);
        Assert.Equal(ChartSeriesNaming.Outcome.NoSelection, m.Outcome);
    }

    [Fact]
    public void TryMatch_DeclinesWhenCountsDisagree()
    {
        var a = ChartSeriesNaming.TryMatch(Series(Flat("5"), Flat("7")), Ticked(("Dribbling", "5")));
        Assert.Equal(ChartSeriesNaming.Outcome.CountMismatch, a.Outcome);

        var b = ChartSeriesNaming.TryMatch(Series(Flat("5")), Ticked(("Dribbling", "5"), ("Corners", "7")));
        Assert.Equal(ChartSeriesNaming.Outcome.CountMismatch, b.Outcome);
    }

    [Fact]
    public void TryMatch_DeclinesWhenALineEndsOnNoTickedRating()
    {
        // Same count, but a line ending on 9 when nothing ticked is on 9: the lines and the rows
        // are not the same set of attributes.
        var m = ChartSeriesNaming.TryMatch(
            Series(Flat("5"), Flat("9")), Ticked(("Dribbling", "5"), ("Corners", "7")));
        Assert.Equal(ChartSeriesNaming.Outcome.CountMismatch, m.Outcome);
    }

    [Fact]
    public void TryMatch_DeclinesWhenARatingIsOverSubscribed()
    {
        // Two lines end on 5 but only one ticked attribute is on 5. Counts agree overall, so only
        // the per-rating check catches this.
        var m = ChartSeriesNaming.TryMatch(
            Series(Flat("5"), Flat("5")), Ticked(("Dribbling", "5"), ("Corners", "7")));
        Assert.Equal(ChartSeriesNaming.Outcome.CountMismatch, m.Outcome);
    }

    [Fact]
    public void TryMatch_MatchesRatingsNumericallyNotTextually()
    {
        // Plotted values and cell text come from different formatters.
        var m = ChartSeriesNaming.TryMatch(
            Series(Flat("8.0"), Flat(" 13 ")), Ticked(("Pace", "8"), ("Balance", "13")));
        Assert.Equal(new[] { "Pace", "Balance" }, m.Names);
    }

    [Fact]
    public void TryMatch_TreatsDifferingHistoryLengthsAsAmbiguous()
    {
        // Same rating, same final value, but one line has fewer points. They cannot be shown to be
        // interchangeable, so neither is named.
        var m = ChartSeriesNaming.TryMatch(
            Series(new[] { "13", "13" }, new[] { "13", "13", "13" }, Flat("8")),
            Ticked(("Marking", "13"), ("Tackling", "13"), ("Pace", "8")));

        Assert.Equal(new[] { "Marking", "Tackling" }, m.Ambiguous);
        Assert.Equal("Pace", m.Names[2]);
    }

    [Fact]
    public void TryMatch_DeclinesWhenARowHasNoName()
    {
        var m = ChartSeriesNaming.TryMatch(
            Series(Flat("5"), Flat("7")), Ticked(("Dribbling", "5"), ("  ", "7")));
        Assert.Equal(ChartSeriesNaming.Outcome.Unreadable, m.Outcome);
    }

    [Fact]
    public void TryMatch_DeclinesOnUnreadableValues()
    {
        // A gap marker in a rating cell, or an unplotted point, cannot be matched to anything.
        var a = ChartSeriesNaming.TryMatch(
            Series(Flat("5"), Flat(null)), Ticked(("Dribbling", "5"), ("Corners", "7")));
        Assert.Equal(ChartSeriesNaming.Outcome.Unreadable, a.Outcome);

        var b = ChartSeriesNaming.TryMatch(
            Series(Flat("5"), Flat("7")), Ticked(("Dribbling", "5"), ("Corners", "couldn't read")));
        Assert.Equal(ChartSeriesNaming.Outcome.Unreadable, b.Outcome);
    }

    [Fact]
    public void TryMatch_TrimsTheNamesItReturns()
    {
        var m = ChartSeriesNaming.TryMatch(Series(Flat("8")), Ticked((" Pace ", "8")));
        Assert.Equal(new[] { "Pace" }, m.Names);
    }
}
