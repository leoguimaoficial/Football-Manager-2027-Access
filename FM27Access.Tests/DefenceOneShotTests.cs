using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

// The pure fire-once decision behind the "good defence" sound (interception, tackle won, save, block, ball
// won back): a silent baseline, a replay freeze, per-side same-moment dedupe over engine time slices, and the
// direction split (reference team = "gooddefence", opposition = "oppgooddefence"). Each test pins one
// behaviour the live scan cannot easily be driven through by hand.
public class DefenceOneShotTests
{
    [Fact]
    public void FirstReadBaselinesSilently_AndReportsMinusOneBefore()
    {
        var d = new DefenceOneShot();
        Assert.Equal(-1, d.HighWater);   // before any read: baseline, do not search

        // A reconnect just after a defensive event entered the window: must NOT fire, and must adopt the
        // high-water so the already-shown play is not re-sounded.
        Assert.Null(d.Observe(ok: true, lastEventIndex: 120, inReplay: false, occurred: true, forRef: true, slice: 500));
        Assert.Equal(120, d.HighWater);
    }

    [Fact]
    public void ReferenceTeamPlayFiresGoodDefence()
    {
        var d = new DefenceOneShot();
        Assert.Null(d.Observe(true, 100, false, occurred: false, forRef: false, slice: 0));   // baseline

        Assert.Equal("gooddefence", d.Observe(true, 104, false, occurred: true, forRef: true, slice: 500));
        // Quiet ticks after: nothing repeats.
        Assert.Null(d.Observe(true, 104, false, occurred: false, forRef: false, slice: 0));
    }

    [Fact]
    public void OppositionPlayFiresOppGoodDefence()
    {
        var d = new DefenceOneShot();
        Assert.Null(d.Observe(true, 100, false, false, false, 0));

        Assert.Equal("oppgooddefence", d.Observe(true, 104, false, occurred: true, forRef: false, slice: 500));
    }

    [Fact]
    public void SameMomentDoublesForOneSideAreDeduped_AcrossSpanBoundaries()
    {
        var d = new DefenceOneShot();
        Assert.Null(d.Observe(true, 100, false, false, false, 0));

        // One defensive phase narrated by two events at (nearly) the same engine time (e.g. a block then a
        // smother), split across two scan spans: only the first sounds.
        Assert.Equal("gooddefence", d.Observe(true, 104, false, occurred: true, forRef: true, slice: 500));
        Assert.Null(d.Observe(true, 105, false, occurred: true, forRef: true, slice: 503));
    }

    [Fact]
    public void ADistinctLaterPlayFiresAgain()
    {
        var d = new DefenceOneShot();
        Assert.Null(d.Observe(true, 100, false, false, false, 0));

        Assert.Equal("gooddefence", d.Observe(true, 104, false, occurred: true, forRef: true, slice: 500));
        // The next defensive moment is a full passage of play later in engine time: it sounds.
        Assert.Equal("gooddefence", d.Observe(true, 130, false, occurred: true, forRef: true, slice: 700));
    }

    [Fact]
    public void PerSideDedupeIsIndependent_OppositionNotSwallowedByReference()
    {
        var d = new DefenceOneShot();
        Assert.Null(d.Observe(true, 100, false, false, false, 0));

        // The reference team wins the ball, then the opposition wins it straight back within the dedupe window:
        // the opposition play is a different side, so it still sounds (the window is per side, not global).
        Assert.Equal("gooddefence", d.Observe(true, 104, false, occurred: true, forRef: true, slice: 500));
        Assert.Equal("oppgooddefence", d.Observe(true, 106, false, occurred: true, forRef: false, slice: 505));
    }

    [Fact]
    public void AReplayReSoundsTheStopItReShows_ThenLiveResumeDoesNotEcho()
    {
        var d = new DefenceOneShot();
        Assert.Null(d.Observe(true, 100, false, false, false, 0));

        // Live: the save sounds as the highlight reaches it.
        Assert.Equal("gooddefence", d.Observe(true, 108, inReplay: false, occurred: true, forRef: true, slice: 600));

        // The replay rewinds and re-shows the same save: it sounds again. The same-moment dedupe, which
        // spans a whole passage of play here, would otherwise swallow it outright.
        Assert.Null(d.Observe(true, 95, inReplay: true, occurred: false, forRef: false, slice: 0));
        Assert.Equal("gooddefence", d.Observe(true, 108, inReplay: true, occurred: true, forRef: true, slice: 600));

        // Live resumes over ground the replay covered: silent, live bookmark untouched.
        Assert.Null(d.Observe(true, 108, inReplay: false, occurred: true, forRef: true, slice: 600));
        Assert.Equal(108, d.HighWater);
    }

    [Fact]
    public void NotOkLeavesTheStateUntouched()
    {
        var d = new DefenceOneShot();
        Assert.Null(d.Observe(true, 100, false, false, false, 0));   // baseline -> high-water 100

        // A gap (no match / unreadable stream): nothing fires, nothing moves.
        Assert.Null(d.Observe(ok: false, lastEventIndex: 0, inReplay: false, occurred: true, forRef: true, slice: 500));
        Assert.Equal(100, d.HighWater);
    }

    [Fact]
    public void NotOkOnFirstReadDoesNotBaseline()
    {
        var d = new DefenceOneShot();
        // Connection opened before a match exists: gap ticks must not baseline.
        Assert.Null(d.Observe(false, 50, false, true, true, 500));
        Assert.Equal(-1, d.HighWater);

        Assert.Null(d.Observe(true, 5, false, false, false, 0));   // now baselines
        Assert.Equal(5, d.HighWater);
    }
}
