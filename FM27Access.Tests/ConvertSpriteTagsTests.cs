using FM27Access.Speech;

namespace FM27Access.Tests;

public class ConvertSpriteTagsTests : IDisposable
{
    public ConvertSpriteTagsTests()
    {
        TextFilters.ClearSpriteNames();
        TextFilters.RegisterSpriteName("captain", "captain");
        TextFilters.RegisterSpriteName("injury", "injured");
    }

    public void Dispose()
    {
        TextFilters.ClearSpriteNames();
    }

    [Fact]
    public void NoSpriteTags_PassesThrough()
    {
        Assert.Equal("Player scored a goal", TextFilters.ConvertSpriteTags("Player scored a goal"));
    }

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", TextFilters.ConvertSpriteTags(""));
    }

    [Fact]
    public void KnownSprite_IsReplaced()
    {
        Assert.Equal("John Smith captain is ready",
            TextFilters.ConvertSpriteTags("John Smith <sprite name=\"captain\"> is ready"));
    }

    [Fact]
    public void MultipleSprites_AllReplaced()
    {
        Assert.Equal("captain John Smith injured",
            TextFilters.ConvertSpriteTags("<sprite name=\"captain\"> John Smith <sprite name=\"injury\">"));
    }

    [Fact]
    public void CaseInsensitive_Matches()
    {
        Assert.Equal("John Smith captain",
            TextFilters.ConvertSpriteTags("John Smith <Sprite Name=\"Captain\">"));
    }

    [Fact]
    public void UnknownSprite_ReplacedWithUnknownSprite()
    {
        Assert.Equal("John Smith Unknown sprite",
            TextFilters.ConvertSpriteTags("John Smith <sprite name=\"never_seen\">"));
    }

    [Fact]
    public void SelfClosingTag_IsMatched()
    {
        Assert.Equal("John Smith captain",
            TextFilters.ConvertSpriteTags("John Smith <sprite name=\"captain\"/>"));
    }

    [Fact]
    public void ExtraWhitespace_IsMatched()
    {
        Assert.Equal("John Smith captain",
            TextFilters.ConvertSpriteTags("John Smith <sprite  name=\"captain\"  />"));
    }

    [Fact]
    public void AdjacentToOtherTags_OnlySpriteConverted()
    {
        Assert.Equal("<color=#FF0000>captain</color>",
            TextFilters.ConvertSpriteTags("<color=#FF0000><sprite name=\"captain\"></color>"));
    }
}
