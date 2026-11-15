using System.Collections.Generic;
using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

public class MarkerHoldTests
{
    private const string Marker = "unlabeled icon button";
    private const int Grace = 4;

    private static MarkerHold NewHold() => new(Marker, Grace);

    private static Dictionary<string, string> Snap(params (string Id, string Text)[] entries)
    {
        var d = new Dictionary<string, string>();
        foreach (var e in entries) d[e.Id] = e.Text;
        return d;
    }

    [Fact]
    public void CleanSnapshot_PassesThroughUntouched()
    {
        // The ordinary sample must not allocate a copy, so the caller's baseline swap stays cheap.
        var hold = NewHold();
        var snap = Snap(("a", "Positive"), ("b", "Neutral"));
        Assert.Same(snap, hold.Apply(snap, new Dictionary<string, string>()));
    }

    [Fact]
    public void ProvisionalMarker_RewrittenToBaseline_SoNoDiffIsProduced()
    {
        // The bug this exists for: a mid-rebind sample replaced correct answer text with the
        // marker, and the client patched the marker onto the page.
        var hold = NewHold();
        var baseline = Snap(("a", "It's always important to play well"));
        var result = hold.Apply(Snap(("a", Marker)), baseline);
        Assert.Equal("It's always important to play well", result["a"]);
        Assert.Empty(ValueDiff.Changed(baseline, result));
    }

    [Fact]
    public void RealTextAfterAHeldMarker_StillDiffsThrough()
    {
        // Holding must not cost the repair: once the label binds it has to reach the page, which
        // only happens if the held sample left the OLD value in the baseline, not the marker.
        var hold = NewHold();
        var baseline = Snap(("a", "Old answer"));
        var held = hold.Apply(Snap(("a", Marker)), baseline);
        var bound = hold.Apply(Snap(("a", "New answer")), held);
        Assert.Equal("New answer", Assert.Contains("a", ValueDiff.Changed(held, bound)));
    }

    [Fact]
    public void MarkerPersistingPastTheGrace_IsReportedHonestly()
    {
        // A genuinely unlabeled control must still degrade in place on the page; the hold defers
        // an honest marker, it never suppresses one.
        var hold = NewHold();
        var baseline = Snap(("a", "Was labelled"));
        var current = baseline;
        for (int i = 0; i < Grace; i++)
        {
            current = hold.Apply(Snap(("a", Marker)), current);
            Assert.Equal("Was labelled", current["a"]);
        }
        var released = hold.Apply(Snap(("a", Marker)), current);
        Assert.Equal(Marker, released["a"]);
    }

    [Fact]
    public void GraceRestartsAfterTheMarkerClears()
    {
        // Consecutive samples, not a lifetime budget: one interview question's rebind must not
        // spend the grace the next question's rebind needs.
        var hold = NewHold();
        var baseline = Snap(("a", "First"));
        for (int i = 0; i < Grace; i++) baseline = hold.Apply(Snap(("a", Marker)), baseline);
        baseline = hold.Apply(Snap(("a", "Second")), baseline);

        var held = hold.Apply(Snap(("a", Marker)), baseline);
        Assert.Equal("Second", held["a"]);
    }

    [Fact]
    public void MarkerWithNoBaseline_IsDroppedRatherThanReportedAsNew()
    {
        // ValueDiff treats an absent-then-present id as changed, so leaving a baseline-less marker
        // in the snapshot would push it exactly as before.
        var hold = NewHold();
        var previous = new Dictionary<string, string>();
        var result = hold.Apply(Snap(("a", Marker), ("b", "Neutral")), previous);
        var changed = ValueDiff.Changed(previous, result);
        Assert.False(changed.ContainsKey("a"));
        Assert.Equal("Neutral", Assert.Contains("b", changed));
    }

    [Fact]
    public void Reset_ForgetsGraceSoALeftPageCannotShortenTheNextOne()
    {
        var hold = NewHold();
        var baseline = Snap(("a", "First"));
        for (int i = 0; i < Grace; i++) baseline = hold.Apply(Snap(("a", Marker)), baseline);

        hold.Reset();
        var held = hold.Apply(Snap(("a", Marker)), baseline);
        Assert.Equal("First", held["a"]);
    }

    [Fact]
    public void RetiredIdsArePrunedOnceTheMapOutgrowsTheLiveSet()
    {
        // A panel rebuilding in place keeps its screen token, so Reset never runs. Without the
        // prune a returning id inherits a stale count and loses its grace.
        var hold = NewHold();
        var baseline = Snap(("a", "A"), ("b", "B"), ("c", "C"));
        for (int i = 0; i < Grace; i++)
            baseline = hold.Apply(Snap(("a", Marker), ("b", Marker), ("c", Marker)), baseline);

        // The rebuild retires b and c; the single-entry snapshot triggers the prune.
        hold.Apply(Snap(("a", "A again")), baseline);

        var held = hold.Apply(Snap(("b", Marker)), Snap(("b", "B")));
        Assert.Equal("B", held["b"]);
    }
}
