using FM27Access.Navigation;

namespace FM27Access.Tests;

public class SquadReadoutTests
{
    private static SquadReadout.Entry E(string name, double value) => new(name, value);

    [Fact]
    public void Compose_BestRating_SpeaksTopThreeDescending()
    {
        var entries = new[] { E("Gustave", 6.4), E("Hamilton", 7.4), E("Wilson", 6.9), E("Saputo", 7.1) };
        Assert.Equal("best rated: Hamilton 7.4, Saputo 7.1, Wilson 6.9",
            SquadReadout.Compose(entries, SquadMetric.Rating, SquadRank.Best));
    }

    [Fact]
    public void Compose_WorstRating_SpeaksBottomThreeAscending()
    {
        var entries = new[] { E("Gustave", 6.4), E("Hamilton", 7.4), E("Wilson", 6.9), E("Saputo", 7.1) };
        Assert.Equal("lowest rated: Gustave 6.4, Wilson 6.9, Saputo 7.1",
            SquadReadout.Compose(entries, SquadMetric.Rating, SquadRank.Worst));
    }

    [Fact]
    public void Compose_Condition_SpeaksWholePercentages()
    {
        var entries = new[] { E("Hamilton", 91.4), E("Wilson", 78.6), E("Saputo", 100.0) };
        Assert.Equal("best condition: Saputo 100%, Hamilton 91%, Wilson 79%",
            SquadReadout.Compose(entries, SquadMetric.Condition, SquadRank.Best));
    }

    [Fact]
    public void Compose_WorstCondition_LeadsWithLowest()
    {
        var entries = new[] { E("Hamilton", 91.0), E("Wilson", 78.0), E("Saputo", 100.0), E("Garcia", 85.0) };
        Assert.Equal("lowest condition: Wilson 78%, Garcia 85%, Hamilton 91%",
            SquadReadout.Compose(entries, SquadMetric.Condition, SquadRank.Worst));
    }

    [Fact]
    public void Compose_Ties_KeepListOrderWithNoSpecialWording()
    {
        // A fresh kickoff: the whole XI shares one rating. The first three in list order speak, plainly.
        var entries = new[] { E("Hamilton", 6.7), E("Wilson", 6.7), E("Vázquez", 6.7), E("Saputo", 6.7) };
        Assert.Equal("best rated: Hamilton 6.7, Wilson 6.7, Vázquez 6.7",
            SquadReadout.Compose(entries, SquadMetric.Rating, SquadRank.Best));
        Assert.Equal("lowest rated: Hamilton 6.7, Wilson 6.7, Vázquez 6.7",
            SquadReadout.Compose(entries, SquadMetric.Rating, SquadRank.Worst));
    }

    [Fact]
    public void Compose_TieAtTheCutoff_StillSpeaksExactlyThree()
    {
        var entries = new[] { E("Hamilton", 7.4), E("Wilson", 7.1), E("Vázquez", 7.1), E("Saputo", 7.1) };
        Assert.Equal("best rated: Hamilton 7.4, Wilson 7.1, Vázquez 7.1",
            SquadReadout.Compose(entries, SquadMetric.Rating, SquadRank.Best));
    }

    [Fact]
    public void Compose_FewerThanThree_SpeaksWhatExists()
    {
        var entries = new[] { E("Hamilton", 7.4), E("Wilson", 6.9) };
        Assert.Equal("best rated: Hamilton 7.4, Wilson 6.9",
            SquadReadout.Compose(entries, SquadMetric.Rating, SquadRank.Best));
    }

    [Fact]
    public void Compose_Empty_ReturnsNullSoTheCallerDegrades()
    {
        Assert.Null(SquadReadout.Compose(System.Array.Empty<SquadReadout.Entry>(), SquadMetric.Rating, SquadRank.Best));
        Assert.Null(SquadReadout.Compose(null, SquadMetric.Condition, SquadRank.Worst));
    }

    [Fact]
    public void Compose_UnreadablePlayers_SpokenAsATrailingGapClause()
    {
        // An unread player could outrank everyone listed, so the gap must be audible.
        var entries = new[] { E("Hamilton", 7.4), E("Wilson", 6.9) };
        Assert.Equal("best rated: Hamilton 7.4, Wilson 6.9, 1 player unreadable",
            SquadReadout.Compose(entries, SquadMetric.Rating, SquadRank.Best, unreadableCount: 1));
        Assert.Equal("lowest condition: Wilson 79%, Hamilton 91%, 2 players unreadable",
            SquadReadout.Compose(new[] { E("Hamilton", 91.0), E("Wilson", 78.6) },
                SquadMetric.Condition, SquadRank.Worst, unreadableCount: 2));
    }

    [Fact]
    public void Compose_RatingRoundsToOneDecimal()
    {
        // Live NumericTypedValue floats arrive as 6.69999980926514; the spoken value must be "6.7".
        var entries = new[] { E("Gustave", 6.69999980926514) };
        Assert.Equal("best rated: Gustave 6.7", SquadReadout.Compose(entries, SquadMetric.Rating, SquadRank.Best));
    }

    private static (string, int)[] Bands(params (string, int)[] bands) => bands;

    [Fact]
    public void ComposeBandCounts_SortsByCountAndOmitsZeroBands()
    {
        var counts = Bands(("very positive", 2), ("positive", 1), ("neutral", 6), ("negative", 2), ("very negative", 0));
        Assert.Equal("body language: 6 neutral, 2 very positive, 2 negative, 1 positive",
            SquadReadout.ComposeBandCounts(counts));
    }

    [Fact]
    public void ComposeBandCounts_CountTiesKeepCanonicalBandOrder()
    {
        // Canonical order is best-to-worst; a stable sort must not flip tied bands.
        var counts = Bands(("very positive", 3), ("positive", 3), ("neutral", 5), ("negative", 3), ("very negative", 0));
        Assert.Equal("body language: 5 neutral, 3 very positive, 3 positive, 3 negative",
            SquadReadout.ComposeBandCounts(counts));
    }

    [Fact]
    public void ComposeBandCounts_UnreadablePlayersSpokenAsTrailingGap()
    {
        var counts = Bands(("very positive", 0), ("positive", 0), ("neutral", 10), ("negative", 0), ("very negative", 0));
        Assert.Equal("body language: 10 neutral, 1 player unreadable",
            SquadReadout.ComposeBandCounts(counts, unreadableCount: 1));
    }

    [Fact]
    public void ComposeBandCounts_NoCountablePlayers_ReturnsNullSoTheCallerDegrades()
    {
        Assert.Null(SquadReadout.ComposeBandCounts(Bands(("neutral", 0)), unreadableCount: 11));
        Assert.Null(SquadReadout.ComposeBandCounts(null));
    }

    [Fact]
    public void ComposeNonNeutral_SpeaksEachPlayerWithTheDetailedWord()
    {
        var players = new[] { ("Vázquez", "Complacent"), ("Wilson", "Nervous") };
        Assert.Equal("Vázquez Complacent, Wilson Nervous",
            SquadReadout.ComposeNonNeutral(players, neutralCount: 9));
    }

    [Fact]
    public void ComposeNonNeutral_AllNeutral_SaysSoRatherThanSilence()
    {
        Assert.Equal("all players neutral body language",
            SquadReadout.ComposeNonNeutral(System.Array.Empty<(string, string)>(), neutralCount: 11));
    }

    [Fact]
    public void ComposeNonNeutral_UnreadablePlayersSpokenAsTrailingGap()
    {
        var players = new[] { ("Vázquez", "Complacent") };
        Assert.Equal("Vázquez Complacent, 2 players unreadable",
            SquadReadout.ComposeNonNeutral(players, neutralCount: 8, unreadableCount: 2));
        Assert.Equal("all players neutral body language, 1 player unreadable",
            SquadReadout.ComposeNonNeutral(System.Array.Empty<(string, string)>(), neutralCount: 10, unreadableCount: 1));
    }

    [Fact]
    public void ComposeNonNeutral_NothingCounted_ReturnsNullSoTheCallerDegrades()
    {
        Assert.Null(SquadReadout.ComposeNonNeutral(System.Array.Empty<(string, string)>(), neutralCount: 0, unreadableCount: 11));
        Assert.Null(SquadReadout.ComposeNonNeutral(null, neutralCount: 5));
    }
}
