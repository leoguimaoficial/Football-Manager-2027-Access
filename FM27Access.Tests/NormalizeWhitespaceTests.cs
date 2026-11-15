using FM27Access.Speech;

namespace FM27Access.Tests;

public class NormalizeWhitespaceTests
{
    [Fact]
    public void NormalText_PassesThrough()
    {
        Assert.Equal("Player scored", TextFilters.NormalizeWhitespace("Player scored"));
    }

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", TextFilters.NormalizeWhitespace(""));
    }

    [Fact]
    public void MultipleSpaces_CollapsedToOne()
    {
        Assert.Equal("a b c", TextFilters.NormalizeWhitespace("a   b    c"));
    }

    [Fact]
    public void Tabs_CollapsedToSpace()
    {
        Assert.Equal("a b", TextFilters.NormalizeWhitespace("a\t\tb"));
    }

    [Fact]
    public void Newlines_CollapsedToSpace()
    {
        Assert.Equal("a b c", TextFilters.NormalizeWhitespace("a\nb\r\nc"));
    }

    [Fact]
    public void LeadingTrailing_Trimmed()
    {
        Assert.Equal("text", TextFilters.NormalizeWhitespace("  text  "));
    }

    [Fact]
    public void MixedWhitespace_NormalizedAndTrimmed()
    {
        Assert.Equal("a b c", TextFilters.NormalizeWhitespace(" \t a \n\n b   c \r\n "));
    }

    [Fact]
    public void OnlyWhitespace_ReturnsEmpty()
    {
        Assert.Equal("", TextFilters.NormalizeWhitespace("   \t \n  "));
    }
}
