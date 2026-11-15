using FM27Access.Navigation;

namespace FM27Access.Tests;

public sealed class SurfaceStartupHtmlTests
{
    [Fact]
    public void TemporaryUnavailableStartupRemainsARecoverableDocument()
    {
        string html=SurfaceStartupHtml.Build("The game is not responding.");
        Assert.StartsWith("<!doctype html>",html);
        Assert.Contains("<main><h1>FM27 Access</h1>",html);
        Assert.Contains("role=\"status\"",html);
        Assert.Contains("The game is not responding.",html);
        Assert.Contains("fetch('/health'",html);
        Assert.Contains("h.screen==='Live'",html);
        Assert.DoesNotContain("/version",html);
    }

    [Fact]
    public void StatusCannotInjectMarkupOrScript()
    {
        string html=SurfaceStartupHtml.Build("<script>bad()</script> & stopped");
        Assert.Contains("&lt;script&gt;bad()&lt;/script&gt; &amp; stopped",html);
        Assert.DoesNotContain("<script>bad()",html);
    }

    [Fact]
    public void LoadingRetainsTheInheritedCaptionAndRecovery()
    {
        string html=SurfaceStartupHtml.Build();
        Assert.Contains("Waiting for the game to load</h1>",html);
        Assert.Contains("Connected. Waiting for the game to be ready.",html);
        Assert.Contains("controller.abort()",html);
        Assert.Contains("if(!ready)setTimeout(waitForGame,1500)",html);
    }
}
