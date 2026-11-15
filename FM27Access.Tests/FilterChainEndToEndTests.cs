using FM27Access.Speech;

namespace FM27Access.Tests;

public class FilterChainEndToEndTests : IDisposable
{
    public FilterChainEndToEndTests()
    {
        SpeechPipeline.ClearFilters();
        TextFilters.ClearSpriteNames();

        TextFilters.RegisterSpriteName("captain", "captain");
        TextFilters.RegisterSpriteName("injury", "injured");

        SpeechPipeline.RegisterFilter(TextFilters.StripControlCharacters);
        SpeechPipeline.RegisterFilter(TextFilters.ConvertSpriteTags);
        SpeechPipeline.RegisterFilter(TextFilters.ExtractLinkText);
        SpeechPipeline.RegisterFilter(TextFilters.StripKnownRichTextTags);
        SpeechPipeline.RegisterFilter(TextFilters.StripRemainingTags);
        SpeechPipeline.RegisterFilter(TextFilters.NormalizeWhitespace);
    }

    public void Dispose()
    {
        SpeechPipeline.ClearFilters();
        TextFilters.ClearSpriteNames();
    }

    [Fact]
    public void PlayerNameWithLinkAndCaptainSprite()
    {
        var input = "<link=\"player_123\"><b>John Smith</b></link> <sprite name=\"captain\">";
        Assert.Equal("John Smith captain", SpeechPipeline.ApplyFilters(input));
    }

    [Fact]
    public void ColoredStatText()
    {
        var input = "<color=#FF0000><b>Goals:</b></color> 18";
        Assert.Equal("Goals: 18", SpeechPipeline.ApplyFilters(input));
    }

    [Fact]
    public void InjuryWithSpriteAndFormatting()
    {
        var input = "<sprite name=\"injury\"> <color=#FF0000><i>Hamstring strain</i></color> (2-3 weeks)";
        Assert.Equal("injured Hamstring strain (2-3 weeks)", SpeechPipeline.ApplyFilters(input));
    }

    [Fact]
    public void ControlCharsAndExtraWhitespaceAfterTagStrip()
    {
        var input = "  <b>Player</b>\0  <color=#FFF>Name</color>  ";
        Assert.Equal("Player Name", SpeechPipeline.ApplyFilters(input));
    }

    [Fact]
    public void PlainText_PassesThroughUnchanged()
    {
        Assert.Equal("John Smith scored a goal",
            SpeechPipeline.ApplyFilters("John Smith scored a goal"));
    }

    [Fact]
    public void UnknownSpriteInComplexText()
    {
        var input = "<b><sprite name=\"unknown_icon\"></b> text";
        Assert.Equal("Unknown sprite text", SpeechPipeline.ApplyFilters(input));
    }

    [Fact]
    public void NestedFormattingAroundLink()
    {
        var input = "<color=#00FF00><link=\"team_456\"><b><i>Manchester United</i></b></link></color>";
        Assert.Equal("Manchester United", SpeechPipeline.ApplyFilters(input));
    }

    [Fact]
    public void KitchenSink()
    {
        var input = "  <color=#FFF><link=\"p1\"><b>John Smith</b></link></color> <sprite name=\"captain\"> "
                  + "<i>scored</i> vs <link=\"t1\">Liverpool</link>\0 "
                  + "<sprite name=\"injury\"> <size=12>(sub 70')</size>  ";
        Assert.Equal("John Smith captain scored vs Liverpool injured (sub 70')",
            SpeechPipeline.ApplyFilters(input));
    }

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", SpeechPipeline.ApplyFilters(""));
    }
}
