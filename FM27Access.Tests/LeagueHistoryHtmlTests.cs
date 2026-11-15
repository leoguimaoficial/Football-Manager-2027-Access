using System.Collections.Generic;
using FM27Access.Navigation;

namespace FM27Access.Tests;

public class LeagueHistoryHtmlTests
{
    private static List<LeagueHistoryRow> Rows(params (string season, string division, string position)[] rows)
    {
        var list = new List<LeagueHistoryRow>();
        foreach (var r in rows) list.Add(new LeagueHistoryRow(r.season, r.division, r.position));
        return list;
    }

    [Fact]
    public void Build_EmitsOneRowPerSeasonAsTable()
    {
        // One <tr> per season inside the body, plus the header row: the table is the whole point,
        // so a row dropped or the structure malformed must fail here.
        var html = LeagueHistoryHtml.Build(Rows(
            ("05/06", "Sky Bet Championship", "17"),
            ("16/17", "Premier League", "16")));
        Assert.Contains("<th scope=\"col\">Season</th>", html);
        Assert.Contains("<th scope=\"col\">Division</th>", html);
        Assert.Contains("<th scope=\"col\">Position</th>", html);
        Assert.Contains("<td>05/06</td><td>Sky Bet Championship</td><td>17th</td>", html);
        Assert.Contains("<td>16/17</td><td>Premier League</td><td>16th</td>", html);
    }

    [Fact]
    public void Build_FormatsPositionAsOrdinal()
    {
        var html = LeagueHistoryHtml.Build(Rows(("15/16", "Sky Bet Championship", "1")));
        Assert.Contains("<td>1st</td>", html);
    }

    [Theory]
    [InlineData("1", "1st")]
    [InlineData("2", "2nd")]
    [InlineData("3", "3rd")]
    [InlineData("4", "4th")]
    [InlineData("11", "11th")]
    [InlineData("12", "12th")]
    [InlineData("13", "13th")]
    [InlineData("21", "21st")]
    [InlineData("22", "22nd")]
    [InlineData("23", "23rd")]
    [InlineData("111", "111th")]
    public void Ordinal_AppliesEnglishSuffix(string position, string expected)
    {
        // The 11/12/13 exception and the per-units suffix are the parts most likely to be coded
        // wrong; each branch is exercised.
        Assert.Equal(expected, LeagueHistoryHtml.Ordinal(position));
    }

    [Fact]
    public void Ordinal_LeavesNonIntegerUnchanged()
    {
        // A degrade marker or an unexpected value passes through verbatim rather than being mangled
        // into a fake ordinal.
        Assert.Equal("couldn't read", LeagueHistoryHtml.Ordinal("couldn't read"));
    }

    [Fact]
    public void Build_EscapesSeasonText()
    {
        // The season cell is escaped (not run through the rich-text cleaner), so angle brackets,
        // ampersands and quotes in game text can't break out of the cell.
        var html = LeagueHistoryHtml.Build(Rows(("<b> & \"x\"", "Premier League", "5")));
        Assert.Contains("&lt;b&gt; &amp; &quot;x&quot;", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public void Build_EscapesDivisionAmpersand()
    {
        // A division name's ampersand survives the rich-text cleaner and must be HTML-escaped so it
        // can't corrupt the cell markup.
        var html = LeagueHistoryHtml.Build(Rows(("05/06", "A & B", "5")));
        Assert.Contains("<td>A &amp; B</td>", html);
    }

    [Fact]
    public void Build_StripsRichTextFromDivision()
    {
        // CleanForDisplay removes Unity rich-text markup so the screen reader never reads the tags.
        var html = LeagueHistoryHtml.Build(Rows(
            ("05/06", "<color=\"#FFFFFF\">Sky Bet Championship</color>", "17")));
        Assert.Contains("<td>Sky Bet Championship</td>", html);
        Assert.DoesNotContain("color=", html);
    }
}
