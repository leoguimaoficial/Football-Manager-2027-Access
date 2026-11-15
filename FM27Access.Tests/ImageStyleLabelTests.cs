using FM27Access.Plugin.Adapters;

namespace FM27Access.Tests;

public class ImageStyleLabelTests
{
    [Theory]
    [InlineData("chevron top_small_3x", "Upward arrow")]
    [InlineData("chevron down_small_3x", "Downward arrow")]
    [InlineData("line_small_3x", "Horizontal line")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("chevron left_small_3x", null)]
    [InlineData("lineup", null)]
    [InlineData("ui-concepts-double_chevron_top_green", null)]
    public void RankingGlyphsDoNotInventMovementAmountsOrBorrowOtherIcons(string? sprite, string? expected)
        => Assert.Equal(expected, ImageStyleLabel.RankingArrow(sprite));

    [Fact]
    public void SupportGlyphPreservesDirectionWithoutInventingNumericalChange()
        => Assert.Equal("Double upward arrow (green)",
            ImageStyleLabel.For("ui-concepts-double_chevron_top_green"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Union")]
    [InlineData("SupportChangeIcon")]
    [InlineData("ui-concepts-unknown")]
    public void UnrecognizedImagesRetainTheirExistingResolution(string? name)
        => Assert.Null(ImageStyleLabel.For(name));
}
