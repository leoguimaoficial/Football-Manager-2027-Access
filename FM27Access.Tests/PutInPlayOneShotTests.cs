using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

// The pure fire-once decision behind the "put in play" ping (kick-off, or a throw-in / goal kick / corner /
// free kick being taken; a penalty sounds as a shot attempt instead): a silent baseline, a replay freeze,
// and a same-take dedupe over engine
// time slices. Each test pins one behaviour the live scan cannot easily be driven through by hand.
public class PutInPlayOneShotTests
{
    [Fact]
    public void FirstReadBaselinesSilently_AndReportsMinusOneBefore()
    {
        var p = new PutInPlayOneShot();
        Assert.Equal(-1, p.HighWater);   // before any read: baseline, do not search

        // A reconnect just after a take entered the window: must NOT ping, and must adopt the high-water.
        Assert.False(p.Observe(ok: true, lastIndex: 120, inReplay: false, occurred: true, slice: 500));
        Assert.Equal(120, p.HighWater);
    }

    [Fact]
    public void ATakeEnteringTheWindowPingsOnce()
    {
        var p = new PutInPlayOneShot();
        Assert.False(p.Observe(true, 100, false, occurred: false, slice: 0));   // baseline

        Assert.True(p.Observe(true, 104, false, occurred: true, slice: 500));
        // Quiet ticks after: nothing repeats.
        Assert.False(p.Observe(true, 104, false, occurred: false, slice: 0));
    }

    [Fact]
    public void FlavourDoublesOfOneTakeAreDeduped_AcrossSpanBoundaries()
    {
        var p = new PutInPlayOneShot();
        Assert.False(p.Observe(true, 100, false, false, 0));

        // The engine narrates one free kick with two commentary events at (nearly) the same engine time,
        // split across two scan spans: only the first pings.
        Assert.True(p.Observe(true, 104, false, occurred: true, slice: 500));
        Assert.False(p.Observe(true, 105, false, occurred: true, slice: 503));
    }

    [Fact]
    public void ADistinctLaterTakePingsAgain()
    {
        var p = new PutInPlayOneShot();
        Assert.False(p.Observe(true, 100, false, false, 0));

        Assert.True(p.Observe(true, 104, false, occurred: true, slice: 500));
        // The next dead ball's take is a full pause later in engine time: it pings.
        Assert.True(p.Observe(true, 130, false, occurred: true, slice: 700));
    }

    [Fact]
    public void ATimelineResetCannotSuppressAKickOff()
    {
        var p = new PutInPlayOneShot();
        Assert.False(p.Observe(true, 100, false, false, 0));

        // Late first half take at a high slice...
        Assert.True(p.Observe(true, 104, false, occurred: true, slice: 27000));
        // ...then the second-half kick-off on a reset playback timeline (small slice): distance, not order,
        // so it still pings.
        Assert.True(p.Observe(true, 140, false, occurred: true, slice: 10));
    }

    [Fact]
    public void AReplayRePingsTheTakeItReShows_ThenLiveResumeDoesNotEcho()
    {
        var p = new PutInPlayOneShot();
        Assert.False(p.Observe(true, 100, false, false, 0));

        // Live: the take pings as the highlight reaches it.
        Assert.True(p.Observe(true, 108, inReplay: false, occurred: true, slice: 600));

        // The replay rewinds and re-shows the take: it pings again, on the replay's own bookmark.
        Assert.False(p.Observe(true, 95, inReplay: true, occurred: false, slice: 0));
        Assert.True(p.Observe(true, 108, inReplay: true, occurred: true, slice: 600));

        // Live resumes over ground the replay covered: silent, live bookmark untouched.
        Assert.False(p.Observe(true, 108, inReplay: false, occurred: true, slice: 600));
        Assert.Equal(108, p.HighWater);
    }

    [Fact]
    public void NotOkLeavesTheStateUntouched()
    {
        var p = new PutInPlayOneShot();
        Assert.False(p.Observe(true, 100, false, false, 0));   // baseline -> high-water 100

        // A gap (no match / unreadable stream): nothing fires, nothing moves.
        Assert.False(p.Observe(ok: false, lastIndex: 0, inReplay: false, occurred: true, slice: 500));
        Assert.Equal(100, p.HighWater);
    }

    [Fact]
    public void NotOkOnFirstReadDoesNotBaseline()
    {
        var p = new PutInPlayOneShot();
        // Connection opened before a match exists: gap ticks must not baseline.
        Assert.False(p.Observe(false, 50, false, true, 500));
        Assert.Equal(-1, p.HighWater);

        Assert.False(p.Observe(true, 5, false, false, 0));   // now baselines
        Assert.Equal(5, p.HighWater);
    }
}
