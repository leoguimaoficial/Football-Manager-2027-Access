using FM27Access.Speech;

namespace FM27Access.Tests;

public class StripRemainingTagsTests
{
    [Fact]
    public void NoTags_PassesThrough()
    {
        Assert.Equal("Player scored", TextFilters.StripRemainingTags("Player scored"));
    }

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", TextFilters.StripRemainingTags(""));
    }

    [Fact]
    public void UnknownTag_Stripped()
    {
        Assert.Equal("text", TextFilters.StripRemainingTags("<div>text</div>"));
    }

    [Fact]
    public void SelfClosingTag_Stripped()
    {
        Assert.Equal("beforeafter", TextFilters.StripRemainingTags("before<br/>after"));
    }

    [Fact]
    public void TagWithAttributes_Stripped()
    {
        Assert.Equal("text", TextFilters.StripRemainingTags("<foo bar=\"baz\">text</foo>"));
    }

    [Fact]
    public void MultipleUnknownTags_AllStripped()
    {
        Assert.Equal("one two three",
            TextFilters.StripRemainingTags("<x>one</x> <y>two</y> <z>three</z>"));
    }

    [Fact]
    public void BareAngleBracket_NotMatched()
    {
        // A lone < without a closing > is not a tag
        Assert.Equal("5 < 10", TextFilters.StripRemainingTags("5 < 10"));
    }
}
