using FM27Access.Plugin.Server;

namespace FM27Access.Tests;

public class ChoiceCaptionMatchTests
{
    [Fact]
    public void RestoredNativeCaptionIdentifiesItsActualOptionRatherThanTheOldCachedOrdinal()
        => Assert.Equal(0, ChoiceCaptionMatch.Unique("Nenhuma Seleccionada", new[]{"Nenhuma Seleccionada","Médio ofensivo (Direito)","Ponta-de-lança (Centro)"}));

    [Fact]
    public void DuplicateOfficialNamesAreAmbiguousEvenIfOneOccupiedTheCachedPosition()
        => Assert.Equal(-1, ChoiceCaptionMatch.Unique("Same", new[]{"Same","Other","Same"}));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("missing")]
    [InlineData("none")]
    [InlineData(" None ")]
    public void MissingOrDifferentCaptionsNeverGuessACurrentOption(string? caption)
        => Assert.Equal(-1, ChoiceCaptionMatch.Unique(caption, new[]{"None","One"}));

    [Fact]
    public void EmptyUnboundRowsDoNotOffsetTheNativeOptionIndex()
        => Assert.Equal(2, ChoiceCaptionMatch.Unique("Official", new string?[]{null,"", "Official"}));
}
