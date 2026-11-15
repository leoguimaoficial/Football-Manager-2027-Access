using FM27Access.Speech;

namespace FM27Access.Tests;

public class CleanForDisplayTests : IDisposable
{
    public CleanForDisplayTests()
    {
        TextFilters.ClearSpriteNames();
        TextFilters.RegisterSpriteName("captain", "captain");
        TextFilters.RegisterSpriteName("injury", "injured");
    }

    public void Dispose() => TextFilters.ClearSpriteNames();

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", TextFilters.CleanForDisplay(""));
    }

    [Fact]
    public void NullString_ReturnsEmpty()
    {
        Assert.Equal("", TextFilters.CleanForDisplay(null));
    }

    [Fact]
    public void PlainText_PassesThroughUnchanged()
    {
        Assert.Equal("Goals 18", TextFilters.CleanForDisplay("Goals 18"));
    }

    [Fact]
    public void StripsRichTextAndExtractsLinks()
    {
        var input = "<color=#FF0000><b>Goals:</b></color> 18";
        Assert.Equal("Goals: 18", TextFilters.CleanForDisplay(input));
    }

    [Fact]
    public void ConvertsSpritesToSpokenNames()
    {
        var input = "<link=\"player_123\"><b>John Smith</b></link> <sprite name=\"captain\">";
        Assert.Equal("John Smith captain", TextFilters.CleanForDisplay(input));
    }

    [Fact]
    public void UnknownSpriteAnnouncedAsUnknown()
    {
        Assert.Equal("Unknown sprite text",
            TextFilters.CleanForDisplay("<b><sprite name=\"unknown_icon\"></b> text"));
    }

    // The HTML surface and the speech surface must produce identical cleaned text;
    // CleanForDisplay is the single chain both sides rely on.
    [Fact]
    public void MatchesTheRegisteredSpeechChain()
    {
        var input = "  <color=#FFF><link=\"p1\"><b>John Smith</b></link></color> <sprite name=\"captain\"> "
                  + "<i>scored</i> vs <link=\"t1\">Liverpool</link>\0 "
                  + "<sprite name=\"injury\"> <size=12>(sub 70')</size>  ";
        Assert.Equal("John Smith captain scored vs Liverpool injured (sub 70')",
            TextFilters.CleanForDisplay(input));
    }
}
