using FM27Access.Plugin.Server;

namespace FM27Access.Tests;

public class MessageVerificationKeyTests
{
    [Fact]
    public void IdenticalTitlesRequireTheSameNativeIdentifier()
    {
        var key = MessageVerificationKey.Create(25557, "Same title");
        Assert.True(MessageVerificationKey.Matches(key, 25557, "Same title"));
        Assert.False(MessageVerificationKey.Matches(key, 25556, "Same title"));
        Assert.False(MessageVerificationKey.Matches(key, null, "Same title"));
        Assert.False(MessageVerificationKey.Matches(key, 25557, "Previous title still binding"));
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("Title\u001eextra")]
    [InlineData("")]
    public void RoundTripPreservesNativeIdAndLiteralTitle(string subject)
    {
        var key = MessageVerificationKey.Create(23086, subject);
        Assert.Equal(23086, MessageVerificationKey.Id(key));
        Assert.Equal(subject, MessageVerificationKey.Subject(key));
    }

    [Fact]
    public void APreviewWithoutSubjectCanBeVerifiedByItsRealNativeId()
    {
        var key = MessageVerificationKey.Create(25557, null);
        Assert.True(MessageVerificationKey.Matches(key, 25557, "Native pane title"));
        Assert.False(MessageVerificationKey.Matches(key, 25556, "Native pane title"));
    }

    [Fact]
    public void ExistingFm26HistoryComparisonStringsRemainCompatible()
    {
        Assert.Equal("Legacy title", MessageVerificationKey.Create(null, "Legacy title"));
        Assert.True(MessageVerificationKey.Matches("Legacy title", null, "Legacy title"));
        Assert.False(MessageVerificationKey.Matches("Legacy title", null, "Other title"));
        Assert.False(MessageVerificationKey.Matches(null, null, null));
    }

    [Theory]
    [InlineData("\u001eFM27-news:0\u001eTitle")]
    [InlineData("\u001eFM27-news:-1\u001eTitle")]
    [InlineData("\u001eFM27-news:x\u001eTitle")]
    [InlineData("\u001eFM27-news:123")]
    public void MalformedNativeKeysAreNotMistakenForIdentifiers(string key)
    {
        Assert.Null(MessageVerificationKey.Id(key));
        Assert.Equal(key, MessageVerificationKey.Subject(key));
    }
}
