using FM27Access.Plugin.Adapters;

namespace FM27Access.Tests;

public sealed class LineChartTrendTests
{
    [Fact]
    public void NativeUnlabelledSequenceRetainsShapeWithoutExposingUnderlyingNumbers()
    {
        string text = LineChartTrend.Format(new double?[] { 25740, 25740, 25740, 25520, 25520, 25520 });
        Assert.Equal("6 points, left to right: level, level, down, level, level", text);
        Assert.DoesNotContain("25740", text);
        Assert.DoesNotContain("25520", text);
        Assert.DoesNotContain("%", text);
    }

    [Fact]
    public void OffsetAndPositiveScalingDoNotRevealTheHiddenUnitOrMagnitude()
    {
        Assert.Equal(LineChartTrend.Format(new double?[] { 1, 2, 2, 1 }),
            LineChartTrend.Format(new double?[] { 10400, 10800, 10800, 10400 }));
    }

    [Fact]
    public void DecimalSegmentsDoNotRoundAwayRealVisibleChanges() =>
        Assert.Equal("3 points, left to right: up, down",
            LineChartTrend.Format(new double?[] { .7, .71, .705 }));

    [Fact]
    public void AGapNeverConnectsUnrelatedPoints() =>
        Assert.Equal("4 points, left to right: gap, gap, down",
            LineChartTrend.Format(new double?[] { 1, null, 20, 19 }));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteValuesNeverBecomeAnInventedDirection(double invalid) =>
        Assert.Equal("2 points, left to right: gap", LineChartTrend.Format(new double?[] { invalid, 1 }));

    [Fact]
    public void NoPointsDoesNotInventAPlayerValue() =>
        Assert.Equal("No plotted points", LineChartTrend.Format(Array.Empty<double?>()));

    [Fact]
    public void OnePointHasNoFabricatedSegment() =>
        Assert.Equal("1 plotted point", LineChartTrend.Format(new double?[] { 25000 }));

    [Fact]
    public void MissingInputIsAMappingFailure() =>
        Assert.Throws<ArgumentNullException>(() => LineChartTrend.Format(null!));
}
