using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

// The pure fire-once decision behind the "shot attempt" sound (a strike at goal, before its outcome is
// narrated): a silent baseline, the second pass a replay makes over the same strike, and a dedupe
// deliberately tight enough that a rebound sounds twice. Each test pins one behaviour the live scan cannot
// easily be driven through by hand.
public class ShotOneShotTests
{
    [Fact]
    public void FirstReadBaselinesSilently_AndReportsMinusOneBefore()
    {
        var s = new ShotOneShot();
        Assert.Equal(-1, s.HighWater);   // before any read: baseline, do not search

        // A reconnect just after an attempt entered the window: must NOT fire, and must adopt the high-water
        // so the already-shown strike is not re-sounded.
        Assert.Null(s.Observe(ok: true, lastEventIndex: 120, inReplay: false, occurred: true, slice: 500));
        Assert.Equal(120, s.HighWater);
    }

    [Fact]
    public void AnAttemptFiresOnce()
    {
        var s = new ShotOneShot();
        Assert.Null(s.Observe(true, 100, false, occurred: false, slice: 0));   // baseline

        Assert.Equal("shotattempt", s.Observe(true, 104, false, occurred: true, slice: 715));
        // Quiet ticks after (the outcome events are not attempts, so the scan reports nothing): no repeat.
        Assert.Null(s.Observe(true, 108, false, occurred: false, slice: 0));
    }

    [Fact]
    public void OneStrikeNarratedTwiceAtTheSameMomentIsDeduped()
    {
        var s = new ShotOneShot();
        Assert.Null(s.Observe(true, 100, false, false, 0));

        // Two attempt-family events for a single strike, at the same engine moment, split across two scan
        // spans: only the first sounds.
        Assert.Equal("shotattempt", s.Observe(true, 104, false, occurred: true, slice: 715));
        Assert.Null(s.Observe(true, 105, false, occurred: true, slice: 716));
    }

    [Fact]
    public void ARebound_ShotSaveShot_SoundsTwice()
    {
        var s = new ShotOneShot();
        Assert.Null(s.Observe(true, 100, false, false, 0));

        // The whole point of the tight window: a shot, a save, and an immediate second attempt a second or so
        // of engine time later must both be heard. Playback runs at roughly four slices per game second, so a
        // follow-up eight slices on is about two seconds later.
        Assert.Equal("shotattempt", s.Observe(true, 104, false, occurred: true, slice: 715));
        Assert.Equal("shotattempt", s.Observe(true, 110, false, occurred: true, slice: 723));
    }

    [Fact]
    public void AReplayReSoundsTheStrikeItReShows_ThenLiveResumeDoesNotEcho()
    {
        var s = new ShotOneShot();
        Assert.Null(s.Observe(true, 100, false, false, 0));

        // Live: the strike sounds as the highlight reaches it.
        Assert.Equal("shotattempt", s.Observe(true, 108, inReplay: false, occurred: true, slice: 600));

        // The replay rewinds and shows the same strike again. It must sound again — the same-moment dedupe
        // would otherwise read the replayed strike, at the engine time it already fired at, as one strike
        // narrated twice.
        Assert.Null(s.Observe(true, 102, inReplay: true, occurred: false, slice: 0));   // pass opens silently
        Assert.Equal("shotattempt", s.Observe(true, 108, inReplay: true, occurred: true, slice: 600));

        // Live resumes over ground the replay covered: silent, and the live bookmark is still 108.
        Assert.Null(s.Observe(true, 108, inReplay: false, occurred: true, slice: 600));
        Assert.Equal(108, s.HighWater);
        Assert.Null(s.Observe(true, 110, false, occurred: false, slice: 0));
    }

    [Fact]
    public void DedupeStillHoldsWithinOneReplayPass()
    {
        var s = new ShotOneShot();
        Assert.Null(s.Observe(true, 100, false, false, 0));
        Assert.Null(s.Observe(true, 90, inReplay: true, occurred: false, slice: 0));

        // Inside the replay the strike is narrated by two ids at the same engine moment, exactly as it was
        // live: still one sound. Clearing the latch at the pass boundary must not disarm the dedupe itself.
        Assert.Equal("shotattempt", s.Observe(true, 95, inReplay: true, occurred: true, slice: 600));
        Assert.Null(s.Observe(true, 96, inReplay: true, occurred: true, slice: 601));
    }

    [Fact]
    public void AHalfTimeTimelineResetDoesNotSuppressTheNextAttempt()
    {
        var s = new ShotOneShot();
        Assert.Null(s.Observe(true, 100, false, false, 0));

        // Late in a half, then the playback timeline rewinds for the next period. The dedupe compares
        // distance, not order, so the first attempt of the new period is far away and still sounds.
        Assert.Equal("shotattempt", s.Observe(true, 104, false, occurred: true, slice: 2700));
        Assert.Equal("shotattempt", s.Observe(true, 300, false, occurred: true, slice: 40));
    }

    [Fact]
    public void KickedAndHeadedArbitersDoNotSwallowEachOther()
    {
        // The plugin runs one arbiter per attempt kind, both fed from the same scan and the same played
        // high-water. A shot blocked and headed back in at almost the same engine moment must sound twice:
        // each kind dedupes only against itself, and the two clips are distinct, so the pair cannot be
        // mistaken for one strike double-triggering.
        var kick = new ShotOneShot();
        var header = new ShotOneShot();
        Assert.Null(kick.Observe(true, 100, false, false, 0));     // baseline both, in lockstep
        Assert.Null(header.Observe(true, 100, false, false, 0));

        Assert.Equal("shotattempt", kick.Observe(true, 104, false, occurred: true, slice: 715));
        Assert.Null(header.Observe(true, 104, false, occurred: false, slice: 0));

        // The headed rebound, one slice later — well inside the window that would swallow a second KICK.
        Assert.Null(kick.Observe(true, 106, false, occurred: false, slice: 0));
        Assert.Equal("shotattempt", header.Observe(true, 106, false, occurred: true, slice: 716));

        // Each still dedupes within its own kind: a second header at the same moment stays silent.
        Assert.Null(header.Observe(true, 107, false, occurred: true, slice: 717));
    }

    [Fact]
    public void BothArbitersEnterAndLeaveAReplayTogether()
    {
        // Sharing one scan means both see the same replay flag and the same high-water, so their passes
        // open and close in lockstep and neither can silently consume an attempt the other is holding.
        var kick = new ShotOneShot();
        var header = new ShotOneShot();
        Assert.Null(kick.Observe(true, 100, false, false, 0));
        Assert.Null(header.Observe(true, 100, false, false, 0));

        // The replay opens on both at the rewound index: silent, and both report it as the scan span.
        Assert.Null(kick.Observe(true, 90, inReplay: true, occurred: true, slice: 600));
        Assert.Null(header.Observe(true, 90, inReplay: true, occurred: true, slice: 601));
        Assert.Equal(90, kick.HighWater);
        Assert.Equal(90, header.HighWater);

        Assert.Equal("shotattempt", kick.Observe(true, 108, inReplay: true, occurred: true, slice: 600));
        Assert.Equal("shotattempt", header.Observe(true, 108, inReplay: true, occurred: true, slice: 601));

        // Live resumes on both at once, each back on its own untouched live bookmark.
        Assert.Null(kick.Observe(true, 108, false, occurred: true, slice: 600));
        Assert.Null(header.Observe(true, 108, false, occurred: true, slice: 601));
        Assert.Equal(100, kick.HighWater);
        Assert.Equal(100, header.HighWater);
    }

    [Fact]
    public void NotOkLeavesTheStateUntouched()
    {
        var s = new ShotOneShot();
        Assert.Null(s.Observe(true, 100, false, false, 0));   // baseline -> high-water 100

        // A gap (no match / unreadable stream): nothing fires, nothing moves.
        Assert.Null(s.Observe(ok: false, lastEventIndex: 0, inReplay: false, occurred: true, slice: 500));
        Assert.Equal(100, s.HighWater);
    }

    [Fact]
    public void NotOkOnFirstReadDoesNotBaseline()
    {
        var s = new ShotOneShot();
        // Connection opened before a match exists: gap ticks must not baseline.
        Assert.Null(s.Observe(false, 50, false, true, 500));
        Assert.Equal(-1, s.HighWater);

        Assert.Null(s.Observe(true, 5, false, false, 0));   // now baselines
        Assert.Equal(5, s.HighWater);
    }
}
