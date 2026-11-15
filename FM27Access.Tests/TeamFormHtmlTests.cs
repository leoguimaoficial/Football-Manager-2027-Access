using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Speech;

namespace FM27Access.Tests;

public class TeamFormHtmlTests
{
    private static readonly TeamFormHtml.Letters English = new("W", "D", "L");

    // Results are passed oldest-first, as the game stores them: 3 win, 1/2 draw, 0 loss.
    private static string Format(params int?[] oldestFirst) =>
        TeamFormHtml.Format(new List<int?>(oldestFirst), English);

    [Fact]
    public void Format_CollapsesRunsMostRecentFirst()
    {
        // Most recent first: W W L D D.
        Assert.Equal("2W, L, 2D", Format(2, 2, 0, 3, 3));
    }

    [Fact]
    public void Format_AlternatingResultsStaySingle()
    {
        // Most recent first: W L W L W.
        Assert.Equal("W, L, W, L, W", Format(3, 0, 3, 0, 3));
    }

    [Fact]
    public void Format_RunAtTheStart()
    {
        // Most recent first: L L L W L.
        Assert.Equal("3L, W, L", Format(0, 3, 0, 0, 0));
    }

    [Fact]
    public void Format_AllSameIsOneRun()
    {
        Assert.Equal("5L", Format(0, 0, 0, 0, 0));
    }

    [Fact]
    public void Format_BothDrawValuesJoinOneRun()
    {
        // The game styles 1 and 2 identically as a draw, so a 1 next to a 2 is "2D", not "D, D".
        Assert.Equal("W, 2D, L", Format(0, 1, 2, 3));
    }

    [Fact]
    public void Format_ShortHistoryShowsOnlyWhatExists()
    {
        Assert.Equal("D, W", Format(3, 2));
    }

    [Fact]
    public void Format_UnreadableSlotHoldsItsPlaceAndBreaksTheRun()
    {
        // A gap between two wins must not merge them into "2W" — that would count a result the
        // mod never read.
        Assert.Equal($"W, {Degrade.Unavailable}, W", Format(3, null, 3));
    }

    [Fact]
    public void Format_UnknownValueIsAGapNotAResult()
    {
        Assert.Equal($"L, {Degrade.Unavailable}", Format(7, 0));
    }

    [Fact]
    public void Format_UsesTheSuppliedLetters()
    {
        var german = new TeamFormHtml.Letters("S", "U", "N");
        Assert.Equal("2S, N", TeamFormHtml.Format(new List<int?> { 0, 3, 3 }, german));
    }
}
