using FM27Access.Plugin.Server;

namespace FM27Access.Tests;

public class DocumentLanguageTests
{
    [Theory]
    [InlineData("portuguese", "pt")]
    [InlineData("english", "en")]
    [InlineData(" Portuguese ", "pt")]
    [InlineData("French", "fr")]
    [InlineData("German", "de")]
    [InlineData("Spanish", "es")]
    [InlineData("Japanese", "ja")]
    [InlineData("indonesian", "id")]
    [InlineData("danish", "da")]
    [InlineData("english (us)", "en-US")]
    [InlineData("spanish (latam)", "es-419")]
    [InlineData("italian", "it")]
    [InlineData("dutch", "nl")]
    [InlineData("norwegian", "no")]
    [InlineData("polish", "pl")]
    [InlineData("swedish", "sv")]
    [InlineData("turkish", "tr")]
    [InlineData("greek", "el")]
    [InlineData("russian", "ru")]
    [InlineData("arabic", "ar")]
    [InlineData("korean", "ko")]
    [InlineData("chinese", "zh")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("en-GB", "en-GB")]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData(null, "und")]
    [InlineData("", "und")]
    [InlineData("unknown-custom-translation", "und")]
    [InlineData("en\r\nInjected: yes", "und")]
    [InlineData("en\" onload=\"bad", "und")]
    public void NativeResourceNamesBecomeStandardTagsWithoutGuessing(string? native, string tag)
        => Assert.Equal(tag, DocumentLanguage.FromNative(native));

    [Fact]
    public void BodyMetadataCarriesOnlyTheResolvedTag()
    {
        string header = RuntimeSurface.BodyHeader("token", "Title", language: "Portuguese");
        Assert.Contains("\r\nX-FM-Language: pt", header);
        Assert.EndsWith("X-FM-Language: und", RuntimeSurface.BodyHeader("token", "Title",
            language: "en\r\nInjected: yes"));
    }
}
