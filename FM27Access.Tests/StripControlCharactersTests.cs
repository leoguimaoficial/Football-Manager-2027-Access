using FM27Access.Speech;

namespace FM27Access.Tests;

public class StripControlCharactersTests
{
    [Fact]
    public void PlainAscii_PassesThrough()
    {
        Assert.Equal("Hello world", TextFilters.StripControlCharacters("Hello world"));
    }

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", TextFilters.StripControlCharacters(""));
    }

    [Fact]
    public void NullByte_MidString_IsRemoved()
    {
        Assert.Equal("GoalsScored", TextFilters.StripControlCharacters("Goals\0Scored"));
    }

    [Fact]
    public void C0Controls_AreStripped()
    {
        // BEL (0x07), ESC (0x1B), form feed (0x0C)
        Assert.Equal("abc", TextFilters.StripControlCharacters("a\u0007b\u001Bc\u000C"));
    }

    [Fact]
    public void Tab_Newline_CR_ArePreserved()
    {
        var input = "Line1\tCol2\nLine2\r\nLine3";
        Assert.Equal(input, TextFilters.StripControlCharacters(input));
    }

    [Fact]
    public void DEL_IsStripped()
    {
        Assert.Equal("ab", TextFilters.StripControlCharacters("a\u007Fb"));
    }

    [Fact]
    public void C1Controls_AreStripped()
    {
        // U+0080, U+008F, U+009F
        Assert.Equal("abc", TextFilters.StripControlCharacters("a\u0080b\u009Fc"));
    }

    [Fact]
    public void AllControlChars_ReturnsEmpty()
    {
        Assert.Equal("", TextFilters.StripControlCharacters("\0\x01\x02\x07\x1B"));
    }

    [Fact]
    public void SurrogatePairs_ArePreserved()
    {
        var input = "Goal \U0001F3C9 scored";
        Assert.Equal(input, TextFilters.StripControlCharacters(input));
    }

    [Fact]
    public void MixedContent_OnlyControlsRemoved()
    {
        Assert.Equal("Player: 10 goals", TextFilters.StripControlCharacters("Player:\x00 10\x07 goals\x1B"));
    }
}
