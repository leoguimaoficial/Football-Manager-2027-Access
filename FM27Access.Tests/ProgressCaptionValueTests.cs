using FM27Access.Plugin.Adapters;

namespace FM27Access.Tests;

public sealed class ProgressCaptionValueTests
{
    [Theory]
    [InlineData("0,7%")]
    [InlineData("0.7%")]
    [InlineData("100 %")]
    [InlineData("0\u00a0%")]
    [InlineData("125%")]
    [InlineData("−2,5%")]
    [InlineData("٠٫٧٪")]
    public void NativeCompletePercentageRetainsItsOwnNotation(string value) =>
        Assert.True(ProgressCaptionValue.IsPercentage(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("800 € p/s")]
    [InlineData("3,3m €")]
    [InlineData("Percentagem do orçamento salarial total:")]
    [InlineData("Budget 0,7%")]
    [InlineData("0,7%: 0%")]
    [InlineData("0,7% of total")]
    public void MoneyLabelsAndComposedAnnouncementsAreNotPercentageValues(string? value) =>
        Assert.False(ProgressCaptionValue.IsPercentage(value!));
}
