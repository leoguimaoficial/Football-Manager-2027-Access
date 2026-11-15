using FM27Access.Plugin.Server;

namespace FM27Access.Tests;

public class ActionScreenTests
{
    [Theory]
    [InlineData("panel#binding:1", "panel#binding:2")]
    [InlineData("panel\u001fmsgview:open", "panel")]
    [InlineData("panel\u001fmenu:first", "panel\u001fmenu:second")]
    [InlineData("panel#step:1", "panel#step:2")]
    [InlineData("panel", "?")]
    [InlineData("panel", "")]
    public void RefusesChangedNativeOwner(string previous, string current)
        => Assert.False(ActionScreen.Matches(previous, current));

    [Theory]
    [InlineData("panel", "panel")]
    [InlineData("panel\u001fnames:1", "panel\u001fnames:9")]
    [InlineData("panel\u001fnames:1\u001fmenu:same", "panel\u001fnames:9\u001fmenu:same")]
    [InlineData("panel", "panel\u001fnames:1")]
    public void AcceptsSameOwnerDespiteAsyncNameArrival(string previous, string current)
        => Assert.True(ActionScreen.Matches(previous, current));

    [Fact]
    public void DoesNotIgnoreOtherNameLikeSegments()
        => Assert.False(ActionScreen.Matches("panel\u001fnames:invalid", "panel"));

    [Fact]
    public void RetainsIdOnlyDeveloperDriver()
        => Assert.True(ActionScreen.Matches(null!, "panel"));
}
