using FM27Access.Plugin.Server;
using Xunit;

namespace FM27Access.Tests;

/// <summary>
/// The Coach Report's Season Stats block as a Statistic | Value table. The contract: the headline,
/// the gauge (its caption with its percentage) and every label/value pair become rows in the order
/// drawn; the ring's own figure may be dropped only when it is the percentage again; any other shape
/// is refused so the block keeps its plain reading.
/// </summary>
public class CoachSeasonStatsTests
{
    // The goalkeeper block as drawn for Emanuele Toscano (2026-10-04), the ring's "0" held apart.
    private static readonly string[] Keeper =
    {
        "Saves", "0", "0%", "Held",
        "Expected Goals Prevented/90 Mins", "0.00", "Saves/90", "0.00", "Conceded/90 Mins", "0.00",
    };

    [Fact]
    public void ReadsTheBlockAsStatisticValueRows()
    {
        var rows = CoachSeasonStats.Build(Keeper, "0", out _);

        Assert.Equal(new[] { "Saves", "0" }, rows[0]);
        Assert.Equal(new[] { "Held", "0%" }, rows[1]);
        Assert.Equal(new[] { "Expected Goals Prevented/90 Mins", "0.00" }, rows[2]);
        Assert.Equal(new[] { "Conceded/90 Mins", "0.00" }, rows[4]);
        Assert.Equal(5, rows.Count);
    }

    [Fact]
    public void RefusesWhenTheRingFigureIsNotThePercentage()
    {
        // A ring figure that says something the caption does not would be lost from the table.
        Assert.Null(CoachSeasonStats.Build(Keeper, "3", out var reason));
        Assert.Contains("ring", reason);
    }

    [Fact]
    public void RefusesAnUnevenRun()
        => Assert.Null(CoachSeasonStats.Build(new[] { "Saves", "0", "0%", "Held", "Saves/90" }, "0", out _));

    [Fact]
    public void RefusesAGaugeWithoutAPercentage()
        => Assert.Null(CoachSeasonStats.Build(new[] { "Saves", "0", "0", "Held" }, "0", out _));

    [Fact]
    public void RefusesAPairWhoseValueIsNotAFigure()
        => Assert.Null(CoachSeasonStats.Build(new[] { "Saves", "0", "0%", "Held", "Saves/90", "none" }, "0", out _));
}
