using System.Net;
using System.Text.Json;
using FM27Access.Plugin.Server;
namespace FM27Access.Tests;

public class OriginalRuntimeTests
{
    private const string Session="0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static string[] Headers(params string[] extra)=>new[]{"GET / HTTP/1.1","Host: 127.0.0.1:8787"}.Concat(extra).ToArray();
    [Theory]
    [InlineData("/")]
    [InlineData("/health")]
    public void EntryAndHealthAcceptLocalRequests(string path)
    {
        RuntimeSurface.Configure(Session);
        Assert.True(RuntimeSurface.Authorize(Headers(),path));
    }
    [Theory]
    [InlineData("/activate")]
    [InlineData("/dropdown/open")]
    [InlineData("/speak")]
    [InlineData("/events")]
    [InlineData("/eval")]
    public void AllLiveOperationsRequireTheHostSession(string path)
    {
        RuntimeSurface.Configure(Session);
        Assert.False(RuntimeSurface.Authorize(Headers(),path));
        Assert.True(RuntimeSurface.Authorize(Headers("Cookie: other=1; fm27session="+Session),path));
    }
    [Theory]
    [InlineData("Origin: https://example.com")]
    [InlineData("Sec-Fetch-Site: cross-site")]
    [InlineData("Host: attacker.example")]
    [InlineData("Host: 127.0.0.1:8787")]
    public void SessionDoesNotBypassOriginAndHeaderChecks(string header)
    {
        RuntimeSurface.Configure(Session);
        Assert.False(RuntimeSurface.Authorize(Headers("Cookie: fm27session="+Session,header),"/activate"));
    }
    [Fact]
    public void BodyTitleTravelsWithItsOwnSnapshotWithoutRequiringAHeading()
    {
        const string title="Preferências e Acessibilidade + áudio";
        var lines=RuntimeSurface.BodyHeader("native-token",title).Split("\r\n");
        Assert.Equal("X-FM-Token: native-token",lines[0]);
        Assert.Equal(title,WebUtility.UrlDecode(lines[1]["X-FM-Title: ".Length..]));
    }
    [Fact]
    public void VerifiedHistoryOriginRoundTripsWithoutAddingHttpFields()
    {
        const string origin = "{\"index\":2,\"token\":\"panel\\u001fmsgview:open\",\"message\":\"80448\"}\r\nInjected: 1";
        var lines = RuntimeSurface.BodyHeader("token", "Title", 1, historyOrigin: origin).Split("\r\n");
        Assert.Equal(5, lines.Length);
        var field = Assert.Single(lines, line => line.StartsWith("X-FM-History-Origin: "));
        Assert.Equal(origin, WebUtility.UrlDecode(field["X-FM-History-Origin: ".Length..]));
        Assert.DoesNotContain("\r\nInjected:", string.Join("\r\n", lines));
    }
    [Fact]
    public void TitlesCannotInjectExtraHttpHeaders()
    {
        var header=RuntimeSurface.BodyHeader("token\r\nInjected: 1","Título\r\nInjected: 2");
        Assert.Equal(3,header.Split("\r\n").Length);
        Assert.DoesNotContain("\r\nInjected:",header);
        Assert.EndsWith("X-FM-Language: und",header);
    }
    [Theory]
    [InlineData("panel-a\u001fpanel-b")]
    [InlineData("panel#binding:123\u001fnames:1")]
    [InlineData("Preferências + %1F")]
    [InlineData("panel\tcontext")]
    public void SnapshotTokensRoundTripThroughLegalHttpFieldValues(string token)
    {
        var field=RuntimeSurface.BodyHeader(token,"Title").Split("\r\n")[0]["X-FM-Token: ".Length..];
        Assert.All(field,c=>Assert.InRange((int)c,32,126));
        Assert.Equal(token,WebUtility.UrlDecode(field));
    }
    [Fact]
    public void HealthIdentifiesTheExecutingCoreWithoutExposingTheSession()
    {
        RuntimeSurface.Configure(Session);
        string text=RuntimeSurface.Health();using var value=JsonDocument.Parse(text);
        Assert.Equal("fm26-original-core",value.RootElement.GetProperty("adapter").GetString());
        Assert.False(text.Contains(Session,StringComparison.Ordinal));
    }
}
