using FM27Access.Navigation;

namespace FM27Access.Tests;

public class AttributeChangeTests
{
    // The six glyphs the game draws, in ramp order. A wrong entry here would tell a player an
    // attribute is improving when it is declining, so each is pinned individually.
    [Theory]
    [InlineData("Union_2x", "Up 1")]
    [InlineData("Union-1_2x", "Progressing well")]
    [InlineData("Union-2_2x", "Slight progression")]
    [InlineData("Union-3_2x", "Slight regression")]
    [InlineData("Union-4_2x", "Regressing")]
    [InlineData("Union-5_2x", "Down 1")]
    public void KnownSprite_ReadsAsItsRungOfTheRamp(string sprite, string expected)
        => Assert.Equal(expected, AttributeChange.LabelFor(sprite));

    // Only the extremes claim the number actually moved; the middle four must not, because the
    // displayed value is unchanged for them and "Up 1" beside a static number would be a lie.
    [Theory]
    [InlineData("Union-1_2x")]
    [InlineData("Union-2_2x")]
    [InlineData("Union-3_2x")]
    [InlineData("Union-4_2x")]
    public void MidRampSprite_DoesNotClaimAWholePointMoved(string sprite)
    {
        var label = AttributeChange.LabelFor(sprite);
        Assert.DoesNotContain("1", label);
    }

    // Direction must survive any future rewording: the top half may never read as a decline, nor
    // the bottom half as an improvement.
    [Theory]
    [InlineData("Union_2x", "Up")]
    [InlineData("Union-1_2x", "Progress")]
    [InlineData("Union-2_2x", "progress")]
    [InlineData("Union-3_2x", "regress")]
    [InlineData("Union-4_2x", "Regress")]
    [InlineData("Union-5_2x", "Down")]
    public void SpriteKeepsItsDirection(string sprite, string directionWord)
        => Assert.Contains(directionWord, AttributeChange.LabelFor(sprite));

    // The sign would be dropped by a screen reader at default punctuation settings, collapsing a
    // gain and a loss into the same spoken "one".
    [Theory]
    [InlineData("Union_2x")]
    [InlineData("Union-5_2x")]
    public void WholePointWording_CarriesNoBareSign(string sprite)
    {
        var label = AttributeChange.LabelFor(sprite);
        Assert.DoesNotContain("+", label);
        Assert.DoesNotContain("-", label);
    }

    // Atlas density is a rendering detail, not part of the glyph's identity: the same arrow served
    // from a 1x or 4x atlas must still resolve rather than silently blanking every row.
    [Theory]
    [InlineData("Union-2")]
    [InlineData("Union-2_1x")]
    [InlineData("Union-2_4x")]
    [InlineData("Union-2_2x")]
    public void DensitySuffix_DoesNotChangeTheReading(string sprite)
        => Assert.Equal("Slight progression", AttributeChange.LabelFor(sprite));

    // A suffix that merely looks like a density marker must not be stripped, or an unrelated
    // sprite could be mistaken for an arrow.
    [Theory]
    [InlineData("Union-2_box")]
    [InlineData("Union-2_x")]
    [InlineData("Union-2_2y")]
    public void NonDensitySuffix_IsNotStripped(string sprite)
        => Assert.Null(AttributeChange.LabelFor(sprite));

    // An unknown sprite returns null so the caller emits a gap marker. Returning a label here
    // would invent a development the game never reported; returning "" would assert the attribute
    // held steady, which is equally wrong when an arrow is visibly on screen.
    [Theory]
    [InlineData("Union-6_2x")]
    [InlineData("Union-99_2x")]
    [InlineData("SomeOtherIcon")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownSprite_ReturnsNullSoTheCallerDegrades(string? sprite)
        => Assert.Null(AttributeChange.LabelFor(sprite));

    // "Union" is the top of the ramp, not a prefix match for anything beginning with it.
    [Fact]
    public void BareUnion_IsTheTopOfTheRamp()
        => Assert.Equal("Up 1", AttributeChange.LabelFor("Union"));

    // Every rung reads differently: two glyphs sharing wording would make distinct developments
    // indistinguishable to a player.
    [Fact]
    public void EveryRung_ReadsDistinctly()
    {
        string[] sprites =
        {
            "Union_2x", "Union-1_2x", "Union-2_2x", "Union-3_2x", "Union-4_2x", "Union-5_2x",
        };
        var labels = sprites.Select(AttributeChange.LabelFor).ToList();
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }
}
