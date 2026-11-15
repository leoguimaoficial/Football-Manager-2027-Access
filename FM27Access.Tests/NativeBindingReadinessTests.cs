using FM27Access.Plugin.Contexts;
using FM27Access.Plugin.Server;

namespace FM27Access.Tests;

public sealed class NativeBindingReadinessTests
{
    [Fact]
    public void EmptyOptionalFooterDoesNotMakeACompletePagePermanentlyProvisional()
    {
        var readiness = new NativeBindingReadiness(0, 0);
        Assert.Equal(1, readiness.PendingRowCells); // Preserve the FM26 body-row contract.
        Assert.False(readiness.OptionalContentPending);
        Assert.True(new ComposedShape("page1-of2", 100, readiness.OptionalContentPending)
            .CanSuppress("page1-of2", 110, 1000));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(3, 3)]
    public void LateRealFooterBindingsRetainCompletenessRecovery(int observed, int pending)
    {
        var readiness = new NativeBindingReadiness(observed, pending);
        Assert.True(readiness.OptionalContentPending);
        Assert.Equal(pending, readiness.PendingRowCells);
        Assert.False(new ComposedShape("same-final-native-shape", 100, readiness.OptionalContentPending)
            .CanSuppress("same-final-native-shape", 110, 1000));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void BoundEmptyValuesAreNotMissingRowsOrPendingFooters(int observed)
    {
        var readiness = new NativeBindingReadiness(observed, 0);
        Assert.Equal(0, readiness.PendingRowCells);
        Assert.False(readiness.OptionalContentPending);
    }

    [Fact]
    public void StructuralBaselineAdoptionNeverCertifiesPartialDelivery()
    {
        var receipt = new ComposedShape("post-harvest-native-window", 100, true);
        Assert.True(receipt.MatchesNativeShape("post-harvest-native-window", 110, 1000));
        Assert.False(receipt.CanSuppress("post-harvest-native-window", 110, 1000));
        Assert.True(receipt.HasMarker); // The existing bounded marker retry still owns recovery.
    }

    [Fact]
    public void AChangedOrExpiredNativeShapeCannotBeAdopted()
    {
        var receipt = new ComposedShape("old-window", 100, true);
        Assert.False(receipt.MatchesNativeShape("new-window", 110, 1000));
        Assert.False(receipt.MatchesNativeShape("old-window", 1101, 1000));
    }
}
