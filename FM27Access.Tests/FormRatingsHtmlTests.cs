using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Speech;

namespace FM27Access.Tests;

public class FormRatingsHtmlTests
{
    [Fact]
    public void Format_ReversesToMostRecentFirst()
    {
        // Oldest-first as read from the tile; the newest match (86) must lead the display.
        string text = FormRatingsHtml.Format(new List<int?> { 87, 96, 62, 65, 86 });
        Assert.Equal("8.6, 6.5, 6.2, 9.6, 8.7", text);
    }

    [Fact]
    public void Format_ShortDataShowsOnlyWhatExists()
    {
        string text = FormRatingsHtml.Format(new List<int?> { 64, 72 });
        Assert.Equal("7.2, 6.4", text);
    }

    [Fact]
    public void Format_WholeNumberRatingKeepsOneDecimal()
    {
        string text = FormRatingsHtml.Format(new List<int?> { 70 });
        Assert.Equal("7.0", text);
    }

    [Fact]
    public void Format_UnreadablePipHoldsItsChronologicalSlot()
    {
        string text = FormRatingsHtml.Format(new List<int?> { 70, null, 80 });
        Assert.Equal("8.0, " + Degrade.Unavailable + ", 7.0", text);
    }

    [Fact]
    public void Format_EmptyListRendersEmpty()
    {
        Assert.Equal("", FormRatingsHtml.Format(new List<int?>()));
    }
}
