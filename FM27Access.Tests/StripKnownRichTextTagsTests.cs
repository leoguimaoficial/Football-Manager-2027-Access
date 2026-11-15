using FM27Access.Speech;

namespace FM27Access.Tests;

public class StripKnownRichTextTagsTests
{
    [Fact]
    public void NoTags_PassesThrough()
    {
        Assert.Equal("Player scored", TextFilters.StripKnownRichTextTags("Player scored"));
    }

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", TextFilters.StripKnownRichTextTags(""));
    }

    [Fact]
    public void BoldTags_Stripped()
    {
        Assert.Equal("text", TextFilters.StripKnownRichTextTags("<b>text</b>"));
    }

    [Fact]
    public void ColorTagWithValue_Stripped()
    {
        Assert.Equal("text", TextFilters.StripKnownRichTextTags("<color=#FF0000>text</color>"));
    }

    [Fact]
    public void SizeTagWithValue_Stripped()
    {
        Assert.Equal("text", TextFilters.StripKnownRichTextTags("<size=20>text</size>"));
    }

    [Fact]
    public void AlphaTag_Stripped()
    {
        Assert.Equal("text", TextFilters.StripKnownRichTextTags("<alpha=#CC>text"));
    }

    [Fact]
    public void ItalicStrikethroughUnderline_AllStripped()
    {
        Assert.Equal("one two three",
            TextFilters.StripKnownRichTextTags("<i>one</i> <strikethrough>two</strikethrough> <underline>three</underline>"));
    }

    [Fact]
    public void MixedFormatting_AllStripped()
    {
        Assert.Equal("text",
            TextFilters.StripKnownRichTextTags("<b><color=#FFF><i>text</i></color></b>"));
    }

    [Fact]
    public void UnknownTags_LeftAlone()
    {
        var input = "<div>text</div> <sprite name=\"x\">";
        Assert.Equal(input, TextFilters.StripKnownRichTextTags(input));
    }

    [Fact]
    public void CaseInsensitive_Stripped()
    {
        Assert.Equal("text", TextFilters.StripKnownRichTextTags("<Color=#FFF>text</COLOR>"));
    }
}
