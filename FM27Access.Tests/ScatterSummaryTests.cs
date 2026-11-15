using FM27Access.Plugin.Server;
using Xunit;
using P = FM27Access.Plugin.Server.ScatterSummary.Point;

namespace FM27Access.Tests;

/// <summary>
/// A team scatter card read as the highlighted team's standing. The contract: both measures report
/// the team's value with the league's average, highest, lowest and the team's rank; the quadrant
/// follows the game's zone numbering (bit 0 above the vertical average, bit 1 above the horizontal);
/// and nothing is built unless the points reproduce the card's own average and range and exactly
/// one point is highlighted.
/// </summary>
public class ScatterSummaryTests
{
    // Vertical: 10, 20, 30 (mean 20). Horizontal: 1, 2, 3 (mean 2).
    private static readonly ScatterSummary.Axis Y = new("Won", 20, 10, 30);
    private static readonly ScatterSummary.Axis X = new("Lost", 2, 1, 3);
    private static readonly string[] Quadrants = { "low won, low lost", "high won, low lost", "low won, high lost", "high won, high lost" };

    [Fact]
    public void ReportsTheHighlightedTeamAgainstTheLeague()
    {
        var points = new[] { new P(1, 10, false), new P(3, 30, true), new P(2, 20, false) };

        var r = ScatterSummary.Build(points, Y, X, Quadrants, "Cagliari Calcio", out _);

        Assert.Equal(new[] { "Measure", "Cagliari Calcio", "League average", "Highest", "Lowest", "Rank, highest first" }, r.Headers);
        Assert.Equal(new[] { "Won", "30.00", "20.00", "30.00", "10.00", "1st of 3" }, r.Rows[0]);
        Assert.Equal(new[] { "Lost", "3.00", "2.00", "3.00", "1.00", "1st of 3" }, r.Rows[1]);
    }

    [Theory]
    [InlineData(1, 10, "low won, low lost")]
    [InlineData(1, 30, "high won, low lost")]
    [InlineData(3, 10, "low won, high lost")]
    [InlineData(3, 30, "high won, high lost")]
    public void QuadrantFollowsTheGamesZoneNumbering(double x, double y, string expected)
    {
        // The other points keep each axis's mean and range at the card's values whatever the team.
        var points = new[] { new P(x, y, true), new P(4 - x, 40 - y, false), new P(2, 20, false) };

        var r = ScatterSummary.Build(points, Y, X, Quadrants, "T", out _);

        Assert.Equal(ScatterSummary.QuadrantPrefix + expected, r.Quadrant);
    }

    [Fact]
    public void NoQuadrantOnAnAverageLine()
    {
        var points = new[] { new P(1, 10, false), new P(2, 20, true), new P(3, 30, false) };

        Assert.Null(ScatterSummary.Build(points, Y, X, Quadrants, "T", out _).Quadrant);
    }

    [Fact]
    public void RefusesPointsThatDoNotReproduceTheCardsAverage()
    {
        // A point missing from the list: the remaining ones cannot give the card's average.
        var points = new[] { new P(1, 10, false), new P(3, 30, true) };
        var y = new ScatterSummary.Axis("Won", 25, 10, 30);

        Assert.Null(ScatterSummary.Build(points, y, X, Quadrants, "T", out var reason));
        Assert.Contains("Won", reason);
    }

    [Fact]
    public void RefusesWithoutExactlyOneHighlightedTeam()
    {
        var none = new[] { new P(1, 10, false), new P(2, 20, false), new P(3, 30, false) };
        var two = new[] { new P(1, 10, true), new P(2, 20, true), new P(3, 30, false) };

        Assert.Null(ScatterSummary.Build(none, Y, X, Quadrants, "T", out _));
        Assert.Null(ScatterSummary.Build(two, Y, X, Quadrants, "T", out _));
    }

    [Fact]
    public void TiedValuesShareARank()
    {
        var points = new[] { new P(1, 30, false), new P(3, 30, true), new P(2, 0, false) };
        var y = new ScatterSummary.Axis("Won", 20, 0, 30);

        Assert.Equal("1st of 3", ScatterSummary.Build(points, y, X, Quadrants, "T", out _).Rows[0][5]);
    }

    [Fact]
    public void PlayerScatterListsOurPlayersBestFirstWithRanks()
    {
        // Ours: (x 3, y 10) and (x 1, y 30); the third point is someone else's.
        var points = new[] { new P(3, 10, true), new P(2, 20, false), new P(1, 30, true) };

        var r = ScatterSummary.BuildPlayers(points, Y, X, Quadrants, out _);

        Assert.Equal(new[] { "Measure", "Average", "Highest", "Lowest" }, r.ContextHeaders);
        Assert.Equal(new[] { "Won", "20.00", "30.00", "10.00" }, r.ContextRows[0]);
        Assert.Equal(new[] { "Player", "Won", "Rank, highest first", "Lost", "Rank, highest first", "Quadrant" }, r.PlayerHeaders);
        Assert.Equal(new[] { "Player 1", "30.00", "1st of 3", "1.00", "3rd of 3", "high won, low lost" }, r.PlayerRows[0]);
        Assert.Equal(new[] { "Player 2", "10.00", "3rd of 3", "3.00", "1st of 3", "low won, high lost" }, r.PlayerRows[1]);
        // Each row keeps the point behind it, so the row can open that player.
        Assert.Equal(new[] { 2, 0 }, r.PlayerPoints);
    }

    [Fact]
    public void PlayerScatterWithNoneOfOursStillGivesTheGroupContext()
    {
        var points = new[] { new P(1, 10, false), new P(2, 20, false), new P(3, 30, false) };

        var r = ScatterSummary.BuildPlayers(points, Y, X, Quadrants, out _);

        Assert.Equal(2, r.ContextRows.Count);
        Assert.Empty(r.PlayerRows);
    }

    [Fact]
    public void PlayerScatterRefusesAnIncompletePointList()
    {
        var points = new[] { new P(1, 10, true), new P(3, 30, false) };
        var y = new ScatterSummary.Axis("Won", 25, 10, 30);

        Assert.Null(ScatterSummary.BuildPlayers(points, y, X, Quadrants, out _));
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(18, "18th")]
    [InlineData(21, "21st")]
    public void OrdinalsReadNaturally(int n, string expected) => Assert.Equal(expected, ScatterSummary.Ordinal(n));
}
