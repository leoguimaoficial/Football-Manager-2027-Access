using FM27Access.Navigation;
using FM27Access.Plugin;

namespace FM27Access.Tests;

public sealed class PanelObservationPolicyTests
{
    [Fact]
    public void CoveredNavigablePanelsRemainAvailableForLoadingScreenReturn()
    {
        foreach (int role in PanelObservationPolicy.NavigableLayers)
            Assert.True(PanelObservationPolicy.CanOwnCursor(role, false));
    }

    [Theory]
    [InlineData(InteractivePanelSet.LayerLoadingScreen)]
    [InlineData(InteractivePanelSet.LayerProfanityLoadingScreen)]
    public void LoadingPanelsRetainDeferralEvenWithoutAStopYet(int role) =>
        Assert.True(PanelObservationPolicy.CanOwnCursor(role, false));

    [Theory]
    [InlineData(33)]
    [InlineData(100)]
    public void PresentationLayersCannotReplaceTheCursorButNewNativeForegroundsCan(int role)
    {
        Assert.False(PanelObservationPolicy.CanOwnCursor(role, false));
        Assert.True(PanelObservationPolicy.CanOwnCursor(role, true));
    }
}
