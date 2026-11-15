using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

// The pure timing gate behind the commentary gap filler: possession lines are inserted into the live
// region only after the game's play-by-play has been quiet for 2s, at most every 2s, and never repeating
// the text already in the region. Each test pins one rule that keeps the filler out of the real stream's way.
public class CommentaryFillerGateTests
{
    [Fact]
    public void QuietGapMustElapseBeforeTheFirstFiller()
    {
        var g = new CommentaryFillerGate();
        // A real commentary line lands at t=10000: the quiet clock starts.
        Assert.False(g.Observe(10000, commentaryChanged: true, "kick-off", null));
        // 1.5s later: still inside the quiet window, no filler.
        Assert.False(g.Observe(11500, false, "kick-off", "loose ball, nearest Kambere"));
        // 2s later: the gap is real, the filler speaks.
        Assert.True(g.Observe(12000, false, "kick-off", "loose ball, nearest Kambere"));
    }

    [Fact]
    public void ACommentaryChangeResetsTheQuietClock()
    {
        var g = new CommentaryFillerGate();
        Assert.False(g.Observe(10000, true, "kick-off", null));
        // A new real line at t=11800 resets the clock (and never co-emits with a filler).
        Assert.False(g.Observe(11800, true, "a long ball forward", "loose ball, nearest Kambere"));
        // 2s from the FIRST line but only 0.4s from the second: still quiet-gated.
        Assert.False(g.Observe(12200, false, "a long ball forward", "loose ball, nearest Kambere"));
        // 2s from the second line: now it speaks.
        Assert.True(g.Observe(13800, false, "a long ball forward", "loose ball, nearest Kambere"));
    }

    [Fact]
    public void FillersAreSpacedAndNeverRepeatTheSameText()
    {
        var g = new CommentaryFillerGate();
        Assert.False(g.Observe(10000, true, "kick-off", null));
        Assert.True(g.Observe(12000, false, "kick-off", "Kambere"));
        // Same fact 2s later: suppressed (no repeats), even though the clocks would allow it.
        Assert.False(g.Observe(14000, false, "kick-off", "Kambere"));
        // The picture changes, but inside the 2s spacing from the last filler at 12000... at 13500: gated.
        Assert.False(g.Observe(13500, false, "kick-off", "loose ball, nearest Diaby"));
        // Changed fact, spacing elapsed: speaks.
        Assert.True(g.Observe(14500, false, "kick-off", "loose ball, nearest Diaby"));
    }

    [Fact]
    public void FillerMatchingTheRealLineInTheRegionIsSuppressed()
    {
        var g = new CommentaryFillerGate();
        Assert.False(g.Observe(10000, true, "contested, Kambere and Diaby", null));
        // The candidate says exactly what the region already says: silence, not an echo.
        Assert.False(g.Observe(12000, false, "contested, Kambere and Diaby", "contested, Kambere and Diaby"));
        // A different fact still speaks.
        Assert.True(g.Observe(12500, false, "contested, Kambere and Diaby", "Kambere"));
    }

    [Fact]
    public void NullCandidateNeverEmitsAndDoesNotDisturbTheClocks()
    {
        var g = new CommentaryFillerGate();
        Assert.False(g.Observe(10000, true, "kick-off", null));
        // Between highlights: no candidate, tick after tick, nothing emits.
        Assert.False(g.Observe(12000, false, "kick-off", null));
        Assert.False(g.Observe(14000, false, "kick-off", null));
        // The moment a candidate exists the gap is already open: it speaks at once.
        Assert.True(g.Observe(14500, false, "kick-off", "loose ball, nearest Kambere"));
    }

    [Fact]
    public void WantsCandidateMirrorsTheClocksSoQuietTicksSkipTheCompose()
    {
        var g = new CommentaryFillerGate();
        g.Observe(10000, true, "kick-off", null);
        Assert.False(g.WantsCandidate(11000));   // quiet window still open
        Assert.True(g.WantsCandidate(12000));    // gap reached: worth composing
        g.Observe(12000, false, "kick-off", "Kambere");   // filler emitted
        Assert.False(g.WantsCandidate(13000));   // inside filler spacing
        Assert.True(g.WantsCandidate(14000));
    }
}
