using FM27Access.Speech;

namespace FM27Access.Tests;

public class ExtractLinkTextTests
{
    [Fact]
    public void NoLinkTags_PassesThrough()
    {
        Assert.Equal("Player scored a goal", TextFilters.ExtractLinkText("Player scored a goal"));
    }

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", TextFilters.ExtractLinkText(""));
    }

    [Fact]
    public void LinkTag_ExtractsInnerText()
    {
        Assert.Equal("John Smith scored",
            TextFilters.ExtractLinkText("<link=\"player_123\">John Smith</link> scored"));
    }

    [Fact]
    public void MultipleLinks_AllExtracted()
    {
        Assert.Equal("John Smith assisted by Jane Doe",
            TextFilters.ExtractLinkText("<link=\"p1\">John Smith</link> assisted by <link=\"p2\">Jane Doe</link>"));
    }

    [Fact]
    public void CaseInsensitive_Matches()
    {
        Assert.Equal("John Smith",
            TextFilters.ExtractLinkText("<Link=\"player_123\">John Smith</Link>"));
    }

    [Fact]
    public void EmptyInnerText_BecomesEmpty()
    {
        Assert.Equal("before  after",
            TextFilters.ExtractLinkText("before <link=\"id\"></link> after"));
    }

    [Fact]
    public void AdjacentToOtherTags_OnlyLinksExtracted()
    {
        Assert.Equal("<b>John Smith</b>",
            TextFilters.ExtractLinkText("<b><link=\"p1\">John Smith</link></b>"));
    }
}
