using System.Collections.Generic;
using FM27Access.Plugin.Server;
using Xunit;

namespace FM27Access.Tests;

/// <summary>
/// Widening a radar stat table with the series the chart only draws. The contract: every row gains a
/// column per extra series, formatted like the figure the game printed, and the whole widen is refused
/// (rows untouched) unless the first series' raw number reproduces every printed figure exactly. A
/// number the player cannot check must never appear on a table whose rows the binding does not match.
/// </summary>
public class RadarColumnsTests
{
    private static List<List<string>> Rows(params (string Label, string Printed)[] rows)
    {
        var list = new List<List<string>>();
        foreach (var (label, printed) in rows) list.Add(new List<string> { label, printed });
        return list;
    }

    private static readonly string[] Legend = { "Cagliari", "Serie A Average" };

    [Fact]
    public void AddsComparisonColumnFormattedLikeThePrintedFigure()
    {
        var rows = Rows(("Goals per Game", "1.9"), ("Conceded per Game", "1.0"));
        var axes = new[]
        {
            new RadarColumns.Axis("Goals per Game", new double?[] { 1.8888888, 1.2137148 }),
            new RadarColumns.Axis("Conceded per Game", new double?[] { 1.0, 1.2137161 }),
        };

        var headers = RadarColumns.TryWiden(rows, axes, Legend, "Metric", out _);

        Assert.Equal(new[] { "Metric", "Cagliari", "Serie A Average" }, headers);
        Assert.Equal(new[] { "Goals per Game", "1.9", "1.2" }, rows[0]);
        Assert.Equal(new[] { "Conceded per Game", "1.0", "1.2" }, rows[1]);
    }

    [Fact]
    public void JoinsRowsToAxesByLabelNotPosition()
    {
        // The drawn rows and the binding's axis order are independent reads; a row must take the
        // comparison figure of ITS axis even when the two orders differ.
        var rows = Rows(("Shots per Game", "16.4"), ("Goals per Game", "1.9"));
        var axes = new[]
        {
            new RadarColumns.Axis("Goals per Game", new double?[] { 1.8888888, 1.2137148 }),
            new RadarColumns.Axis("Shots per Game", new double?[] { 16.444444, 11.321218 }),
        };

        Assert.NotNull(RadarColumns.TryWiden(rows, axes, Legend, "Metric", out _));
        Assert.Equal("11.3", rows[0][2]);
        Assert.Equal("1.2", rows[1][2]);
    }

    [Fact]
    public void RefusesWhenFirstSeriesDisagreesWithAPrintedFigure()
    {
        // A stale or mismatched binding: one row's printed figure is not what series 0 holds.
        var rows = Rows(("Goals per Game", "1.9"), ("Shots per Game", "16.4"));
        var axes = new[]
        {
            new RadarColumns.Axis("Goals per Game", new double?[] { 1.8888888, 1.2137148 }),
            new RadarColumns.Axis("Shots per Game", new double?[] { 15.2, 11.321218 }),
        };

        Assert.Null(RadarColumns.TryWiden(rows, axes, Legend, "Metric", out var reason));
        Assert.Contains("Shots per Game", reason);
        Assert.All(rows, r => Assert.Equal(2, r.Count));
    }

    [Fact]
    public void RefusesWhenAComparisonFigureIsMissing()
    {
        var rows = Rows(("Goals per Game", "1.9"), ("Shots per Game", "16.4"));
        var axes = new[]
        {
            new RadarColumns.Axis("Goals per Game", new double?[] { 1.8888888, 1.2137148 }),
            new RadarColumns.Axis("Shots per Game", new double?[] { 16.444444, null }),
        };

        Assert.Null(RadarColumns.TryWiden(rows, axes, Legend, "Metric", out _));
        Assert.All(rows, r => Assert.Equal(2, r.Count));
    }

    [Fact]
    public void RefusesWhenLegendAndSeriesCountsDiffer()
    {
        var rows = Rows(("Goals per Game", "1.9"));
        var axes = new[] { new RadarColumns.Axis("Goals per Game", new double?[] { 1.8888888, 1.2, 1.4 }) };

        Assert.Null(RadarColumns.TryWiden(rows, axes, Legend, "Metric", out _));
    }

    [Fact]
    public void RefusesARowWithNoMatchingAxis()
    {
        var rows = Rows(("Goals per Game", "1.9"), ("Tackles Won Ratio (%)", "78.6"));
        var axes = new[]
        {
            new RadarColumns.Axis("Goals per Game", new double?[] { 1.8888888, 1.2137148 }),
            new RadarColumns.Axis("Pass Completion Ratio (%)", new double?[] { 88.29, 90.47 }),
        };

        Assert.Null(RadarColumns.TryWiden(rows, axes, Legend, "Metric", out _));
    }

    [Fact]
    public void RefusesDuplicateAxisLabels()
    {
        // Two axes sharing a label cannot be told apart, so neither figure can be trusted to a row.
        var rows = Rows(("Goals per Game", "1.9"), ("Goals per Game", "1.9"));
        var axes = new[]
        {
            new RadarColumns.Axis("Goals per Game", new double?[] { 1.8888888, 1.2 }),
            new RadarColumns.Axis("Goals per Game", new double?[] { 1.8888888, 1.6 }),
        };

        Assert.Null(RadarColumns.TryWiden(rows, axes, Legend, "Metric", out _));
    }

    [Theory]
    [InlineData("1.9", 1.2137148, "1.2")]
    [InlineData("3", 2.6, "3")]
    [InlineData("88,3", 90.47052, "90,5")]   // a comma decimal mark carries over
    [InlineData("-0.4", -0.41, "-0.4")]
    [InlineData("0.08", 0.085135, "0.09")]
    public void FormatLikeCopiesDecimalsAndMark(string printed, double value, string expected)
        => Assert.Equal(expected, RadarColumns.FormatLike(printed, value));

    [Theory]
    [InlineData("1,234.5")]   // grouping separator: not a plain figure
    [InlineData("45%")]
    [InlineData("")]
    [InlineData("1.")]
    public void FormatLikeRejectsFiguresItCannotMirror(string printed)
        => Assert.Null(RadarColumns.FormatLike(printed, 1.0));

    // A radar that prints no figures (the Coach Report's): the table comes from the binding alone.

    [Fact]
    public void ValueTableListsEveryAxisWithEachSeriesToOneDecimal()
    {
        var axes = new[]
        {
            new RadarColumns.Axis("Pas %", new double?[] { 92.65306, 92.63099 }),
            new RadarColumns.Axis("Tck %", new double?[] { 100, 74.97382 }),
        };

        var rows = RadarColumns.ValueTable(axes, 2, out _);

        Assert.Equal(new[] { "Pas %", "92.7", "92.6" }, rows[0]);
        Assert.Equal(new[] { "Tck %", "100.0", "75.0" }, rows[1]);
    }

    [Fact]
    public void ValueTableRefusesAMissingValue()
    {
        // With nothing printed to check against, a hole cannot be filled or skipped honestly.
        var axes = new[] { new RadarColumns.Axis("Pas %", new double?[] { 92.6, null }) };

        Assert.Null(RadarColumns.ValueTable(axes, 2, out var reason));
        Assert.Contains("Pas %", reason);
    }

    [Fact]
    public void ValueTableRefusesTheWrongNumberOfSeries()
    {
        var axes = new[] { new RadarColumns.Axis("Pas %", new double?[] { 92.6 }) };

        Assert.Null(RadarColumns.ValueTable(axes, 2, out _));
    }

    // The radar caption's competition, read from the game's own markup (the Coach Report's caption,
    // captured 2026-10-04, then the same shape for other leagues).
    [Theory]
    [InlineData("<color=\"#FFFFFF\"><link=\"0\"><style=\"Norml\">Serie A</style></link></color>, This Season", "Serie A")]
    [InlineData("<color=\"#FFFFFF\"><link=\"0\"><style=\"Norml\">Premier League</style></link></color>, This Season", "Premier League")]
    [InlineData("<link=\"3\">Liga Portugal Betclic</link>, Last Season", "Liga Portugal Betclic")]
    [InlineData("<link=\"0\"><b>Sky Bet League One</b></link>, This Season", "Sky Bet League One")]
    public void LinkedNameIsTheCompetitionWhateverItIs(string raw, string expected)
        => Assert.Equal(expected, RadarColumns.LinkedName(raw));

    [Theory]
    [InlineData("Serie A, This Season")]                                      // no link: nothing to trust
    [InlineData("<link=\"0\">Serie A</link> v <link=\"1\">Serie B</link>")]  // two: ambiguous
    [InlineData("<link=\"0\"> </link>, This Season")]                        // empty name
    [InlineData("")]
    public void LinkedNameRefusesAnyOtherShape(string raw)
        => Assert.Null(RadarColumns.LinkedName(raw));

    [Fact]
    public void ValueTableRefusesAnUnnamedAxis()
        => Assert.Null(RadarColumns.ValueTable(new[] { new RadarColumns.Axis(" ", new double?[] { 1, 2 }) }, 2, out _));
}
