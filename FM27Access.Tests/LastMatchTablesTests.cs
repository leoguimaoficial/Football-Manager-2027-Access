using System.Collections.Generic;
using FM27Access.Plugin.Server;
using Xunit;
using Side = FM27Access.Plugin.Server.LastMatchTables.Side;

namespace FM27Access.Tests;

/// <summary>
/// The tables that replace the Data Hub's Last Match drawings. The contract: the xG story lists
/// every shot in match order with each side's running total and refuses unless the shots sum to the
/// card's printed totals; momentum names the team on top from the bar's sign and refuses when the
/// record's own team contradicts it; pass map links are ranked strongest first on the game's 0 to 1
/// scale and refuse a link to a player who is not on the map.
/// </summary>
public class LastMatchTablesTests
{
    private static LastMatchTables.Shot S(double minute, double xg, bool goal = false, bool ownGoal = false)
        => new(minute, xg, goal, ownGoal);

    [Fact]
    public void XgStoryOrdersShotsAndRunsEachSidesTotal()
    {
        var home = new[] { S(9.6, 0.5), S(58.4, 0.3, goal: true) };
        var away = new[] { S(23.0, 0.19) };

        var t = LastMatchTables.XgStory(home, away, "Cagliari", "Lille", "0.80 - 0.19", out _);

        Assert.Equal(new[] { "Minute", "Team", "Chance xG", "Result", "Cagliari xG", "Lille xG" }, t.Headers);
        Assert.Equal(new[] { "10", "Cagliari", "0.50", "", "0.50", "0.00" }, t.Rows[0]);
        Assert.Equal(new[] { "23", "Lille", "0.19", "", "0.50", "0.19" }, t.Rows[1]);
        Assert.Equal(new[] { "58", "Cagliari", "0.30", "Goal", "0.80", "0.19" }, t.Rows[2]);
    }

    [Fact]
    public void XgStoryRefusesShotsThatDoNotSumToThePrintedTotals()
    {
        // The lists swapped (or one incomplete): the printed home total no longer matches.
        var home = new[] { S(10, 0.19) };
        var away = new[] { S(20, 0.80) };

        Assert.Null(LastMatchTables.XgStory(home, away, "Cagliari", "Lille", "0.80 - 0.19", out var reason));
        Assert.Contains("0.80 - 0.19", reason);
    }

    [Fact]
    public void XgStoryRefusesUnreadablePrintedTotals()
        => Assert.Null(LastMatchTables.XgStory(new[] { S(1, 0.1) }, new LastMatchTables.Shot[0], "A", "B", "0.10", out _));

    [Fact]
    public void XgStoryKeepsHomeFirstAtTheSameMoment()
    {
        var t = LastMatchTables.XgStory(new[] { S(30, 0.1) }, new[] { S(30, 0.2) }, "A", "B", "0.10 - 0.20", out _);

        Assert.Equal("A", t.Rows[0][1]);
        Assert.Equal("B", t.Rows[1][1]);
    }

    [Fact]
    public void XgStoryMarksOwnGoalsAsSuch()
    {
        var t = LastMatchTables.XgStory(new[] { S(30, 0.1, goal: true, ownGoal: true) }, new LastMatchTables.Shot[0], "A", "B", "0.10 - 0.00", out _);

        Assert.Equal("Own goal", t.Rows[0][3]);
    }

    [Theory]
    [InlineData(58.42, 58)]
    [InlineData(9.5, 10)]
    [InlineData(0.3, 0)]
    public void WholeMinuteIsTheNearestMinuteOnTheAxis(double minute, int expected)
        => Assert.Equal(expected, LastMatchTables.WholeMinute(minute));

    private static LastMatchTables.Period P(double value, Side team) => new(value, team);

    [Fact]
    public void MomentumNamesTheTeamOnTopFromTheSign()
    {
        var periods = new[] { P(0.48, Side.Home), P(-0.33, Side.Away), P(0, Side.None) };

        var t = LastMatchTables.Momentum(periods, new LastMatchTables.Goal[0], "Cagliari", "Lille", "Danger of Possession", out _);

        Assert.Equal(new[] { "Period", "On top", "Danger of Possession", "Goals" }, t.Headers);
        Assert.Equal(new[] { "1st half 1", "Cagliari", "0.48", "" }, t.Rows[0]);
        Assert.Equal(new[] { "1st half 2", "Lille", "0.33", "" }, t.Rows[1]);
        Assert.Equal(new[] { "1st half 3", "Even", "", "" }, t.Rows[2]);
    }

    [Fact]
    public void MomentumRefusesARecordWhoseTeamContradictsTheSign()
    {
        // The sign convention (positive = home) is what was verified; a record naming the away team
        // on a positive bar means it does not hold here, so no team may be named.
        var periods = new[] { P(0.48, Side.Away) };

        Assert.Null(LastMatchTables.Momentum(periods, new LastMatchTables.Goal[0], "A", "B", "X", out var reason));
        Assert.Contains("period 1", reason);
    }

    [Fact]
    public void MomentumPutsEachGoalInItsPeriodWithItsTeam()
    {
        var periods = new[] { P(0.1, Side.Home), P(0.2, Side.Home) };
        var goals = new[] { new LastMatchTables.Goal(1, Side.Away, 56), new LastMatchTables.Goal(1, Side.Home, 58) };

        var t = LastMatchTables.Momentum(periods, goals, "Cagliari", "Lille", "X", out _);

        Assert.Equal("", t.Rows[0][3]);
        Assert.Equal("Lille goal 56; Cagliari goal 58", t.Rows[1][3]);
    }

    [Theory]
    [InlineData(0, "1st half 1")]
    [InlineData(8, "1st half 9")]
    [InlineData(9, "2nd half 1")]
    [InlineData(17, "2nd half 9")]
    public void EighteenPeriodsSplitIntoHalves(int index, string expected)
        => Assert.Equal(expected, LastMatchTables.PeriodLabel(index, 18));

    [Theory]
    [InlineData(0, 9, "1st half 1")]
    [InlineData(8, 9, "1st half 9")]
    [InlineData(10, 12, "2nd half 2")]
    public void APartlyDrawnMatchStillSplitsIntoHalves(int index, int count, string expected)
        => Assert.Equal(expected, LastMatchTables.PeriodLabel(index, count));

    [Fact]
    public void ExtraTimePeriodCountsAreNumberedStraightThrough()
        => Assert.Equal("13", LastMatchTables.PeriodLabel(12, 24));

    [Fact]
    public void MomentumListsAPeriodsGoalsInMatchOrder()
    {
        var periods = new[] { P(0.1, Side.Home) };
        // The record's order: the period's home goals, then its away goals.
        var goals = new[] { new LastMatchTables.Goal(0, Side.Home, 35), new LastMatchTables.Goal(0, Side.Away, 34) };

        var t = LastMatchTables.Momentum(periods, goals, "Cagliari", "Qarabag", "X", out _);

        Assert.Equal("Qarabag goal 34; Cagliari goal 35", t.Rows[0][3]);
    }

    private static LastMatchTables.MapPlayer M(int number, params (int To, double Strength)[] links)
    {
        var p = new LastMatchTables.MapPlayer { Number = number, Combinations = 10, CompletionPercent = 90 };
        p.Links.AddRange(links);
        return p;
    }

    [Fact]
    public void PassLinksRankStrongestFirstOnTheGamesScale()
    {
        var players = new[] { M(4, (19, 0.0), (21, 0.35)), M(19, (4, 1.0)), M(21) };

        var t = LastMatchTables.PassMapLinks(players, out _);

        Assert.Equal(new[] { "From", "To", "Strength" }, t.Headers);
        Assert.Equal(new[] { "19", "4", "100" }, t.Rows[0]);
        Assert.Equal(new[] { "4", "21", "35" }, t.Rows[1]);
        // The weakest drawn link is a real link (it was counted in the card's summary), so it stays.
        Assert.Equal(new[] { "4", "19", "0" }, t.Rows[2]);
    }

    [Fact]
    public void PassLinksRefuseAReceiverNotOnTheMap()
        => Assert.Null(LastMatchTables.PassMapLinks(new[] { M(4, (99, 0.5)) }, out _));

    [Fact]
    public void PassLinksRefuseAStrengthOutsideTheScale()
        => Assert.Null(LastMatchTables.PassMapLinks(new[] { M(4, (19, 1.5)), M(19) }, out _));

    [Fact]
    public void PassPlayersNameEveryoneOrNoOne()
    {
        var named = new[] { M(4), M(19) };
        named[0].Name = "Yılmaz";
        named[1].Name = "Darwich";
        var partly = new[] { M(4), M(19) };
        partly[0].Name = "Yılmaz";

        Assert.Equal(new[] { "No.", "Player", "Pass combinations", "Pass completion" }, LastMatchTables.PassMapPlayers(named).Headers);
        Assert.Equal(new[] { "No.", "Pass combinations", "Pass completion" }, LastMatchTables.PassMapPlayers(partly).Headers);
        Assert.Equal(new[] { "4", "10", "90%" }, LastMatchTables.PassMapPlayers(partly).Rows[0]);
    }

    [Fact]
    public void HeatMapPreservesTextureOrientationAndWeights()
    {
        var map = new[] { new[] { 1d, 0, 2 }, new[] { 0d, 3, 0 }, new[] { 4d, 0, 0 } };
        var table = LastMatchTables.HeatMap(map);
        Assert.Equal(new[] { "Area", "Left", "Centre", "Right" }, table.Headers);
        Assert.Equal(new[] { "Top", "20%", "0%", "0%" }, table.Rows[0]);
        Assert.Equal(new[] { "Middle", "0%", "30%", "0%" }, table.Rows[1]);
        Assert.Equal(new[] { "Bottom", "10%", "0%", "40%" }, table.Rows[2]);
    }

    [Fact]
    public void HeatMapIncludesUnevenTextureEdges()
    {
        var map = new double[4][];
        for (int x = 0; x < map.Length; x++) map[x] = new double[5];
        map[0][0] = 1;
        map[3][4] = 3;
        var table = LastMatchTables.HeatMap(map);
        Assert.Equal(new[] { "Top", "0%", "0%", "75%" }, table.Rows[0]);
        Assert.Equal(new[] { "Bottom", "25%", "0%", "0%" }, table.Rows[2]);
    }

    [Fact]
    public void HeatMapEmptyIntensityDoesNotInventActivity()
    {
        var table = LastMatchTables.HeatMap(new[] { new double[3], new double[3], new double[3] });
        foreach (var row in table.Rows) Assert.Equal(new[] { "0%", "0%", "0%" }, row.GetRange(1, 3));
    }

    [Fact]
    public void HeatMapLargeFiniteIntensityStillProducesFiniteShares()
    {
        var table = LastMatchTables.HeatMap(new[]
        {
            new[] { double.MaxValue / 4, 0, 0 }, new double[3], new double[3]
        });
        Assert.Equal(new[] { "Bottom", "100%", "0%", "0%" }, table.Rows[2]);
    }

    [Fact]
    public void HeatMapRejectsIncompleteOrRaggedTextures()
    {
        Assert.Null(LastMatchTables.HeatMap(null));
        Assert.Null(LastMatchTables.HeatMap(new[] { new double[3], new double[3] }));
        Assert.Null(LastMatchTables.HeatMap(new[] { new double[2], new double[2], new double[2] }));
        Assert.Null(LastMatchTables.HeatMap(new[] { new double[3], null, new double[3] }));
        Assert.Null(LastMatchTables.HeatMap(new[] { new double[3], new double[4], new double[3] }));
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void HeatMapRejectsInvalidIntensity(double value)
        => Assert.Null(LastMatchTables.HeatMap(new[] { new[] { value, 0, 0 }, new double[3], new double[3] }));

    [Fact]
    public void HeatMapRejectsOverflowingTotals()
        => Assert.Null(LastMatchTables.HeatMap(new[]
        {
            new[] { double.MaxValue, double.MaxValue, 0 }, new double[3], new double[3]
        }));
}
