using System.Collections.Generic;
using FM27Access.Navigation;

namespace FM27Access.Tests;

public class MatchStatsHtmlTests
{
    private static List<MatchStatRow> Rows(params (string label, string home, string away)[] rows)
    {
        var list = new List<MatchStatRow>();
        foreach (var r in rows) list.Add(new MatchStatRow(r.label, r.home, r.away));
        return list;
    }

    [Fact]
    public void FoldCell_SingleValue_ReturnedVerbatim()
    {
        Assert.Equal("27", MatchStatsHtml.FoldCell(new[] { "27" }));
    }

    [Fact]
    public void FoldCell_RatioThenPercent_FoldsToTallyWithPercent()
    {
        // The home side lists the tally before the percentage.
        Assert.Equal("21 / 43 (48%)", MatchStatsHtml.FoldCell(new[] { "21 / 43", "48%" }));
    }

    [Fact]
    public void FoldCell_PercentThenRatio_FoldsRegardlessOfOrder()
    {
        // The away side mirrors the layout and lists the percentage first; the fold must not depend on
        // source order, or one team's cell would render backwards.
        Assert.Equal("18 / 39 (46%)", MatchStatsHtml.FoldCell(new[] { "46%", "18 / 39" }));
    }

    [Fact]
    public void FoldCell_Empty_ReturnsEmpty()
    {
        Assert.Equal("", MatchStatsHtml.FoldCell(new string[0]));
        Assert.Equal("", MatchStatsHtml.FoldCell(null));
    }

    [Fact]
    public void FoldCell_DropsBlankLeaves()
    {
        // A radial-gauge cell can hand over an empty text leaf alongside the real ones; blanks must not
        // produce a stray space or an empty fragment.
        Assert.Equal("21 / 43 (48%)", MatchStatsHtml.FoldCell(new[] { "21 / 43", "", "48%" }));
    }

    [Fact]
    public void FoldCell_UnexpectedShape_JoinsRatherThanGuesses()
    {
        // Two values that are neither a tally nor a percentage are surfaced verbatim, joined, so an
        // unrecognised stat shape degrades to visible text instead of dropping a value.
        Assert.Equal("a b", MatchStatsHtml.FoldCell(new[] { "a", "b" }));
    }

    [Fact]
    public void Build_EmitsOneRowPerStatWithTeamColumns()
    {
        var html = MatchStatsHtml.Build("Burnley", "Panathinaikos", Rows(
            ("Shots", "27", "15"),
            ("Headers Won", "21 / 43 (48%)", "18 / 39 (46%)")));
        Assert.Contains("<th scope=\"col\">Burnley</th>", html);
        Assert.Contains("<th scope=\"col\">Panathinaikos</th>", html);
        Assert.Contains("<tr><th scope=\"row\">Shots</th><td>27</td><td>15</td></tr>", html);
        Assert.Contains("<tr><th scope=\"row\">Headers Won</th><td>21 / 43 (48%)</td><td>18 / 39 (46%)</td></tr>", html);
    }

    [Fact]
    public void Build_EscapesTeamAndValueText()
    {
        // Team names and folded values are game-sourced; angle brackets, ampersands and quotes must be
        // escaped so they cannot break out of the cell.
        var html = MatchStatsHtml.Build("A & B", "<x>", Rows(("Poss <\"%\">", "1 & 2", "3")));
        Assert.Contains("<th scope=\"col\">A &amp; B</th>", html);
        Assert.Contains("<th scope=\"col\">&lt;x&gt;</th>", html);
        Assert.Contains("<th scope=\"row\">Poss &lt;&quot;%&quot;&gt;</th>", html);
        Assert.Contains("<td>1 &amp; 2</td>", html);
        Assert.DoesNotContain("<x>", html);
    }
}
