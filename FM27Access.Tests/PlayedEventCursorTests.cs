using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

// The played-event bookmark every engine-event one-shot sits on. Each test pins one behaviour the live
// event scan cannot be driven through by hand: the silent baseline, the separate replay pass, the pass
// boundary the dedupes key off, and the guarantee that a replay never disturbs the live bookmark.
public class PlayedEventCursorTests
{
    [Fact]
    public void BeforeAnyReadTellsTheScanToBaseline()
    {
        var c = new PlayedEventCursor();
        Assert.Equal(-1, c.HighWater);
    }

    [Fact]
    public void FirstReadableTickBaselinesSilently()
    {
        var c = new PlayedEventCursor();
        Assert.False(c.Advance(ok: true, lastEventIndex: 250, inReplay: false));
        Assert.Equal(250, c.HighWater);
        Assert.True(c.Advance(ok: true, lastEventIndex: 251, inReplay: false));
    }

    [Fact]
    public void NotOkLeavesEverythingUntouched()
    {
        var c = new PlayedEventCursor();
        Assert.False(c.Advance(ok: false, lastEventIndex: 100, inReplay: false));
        Assert.Equal(-1, c.HighWater);   // a gap must not baseline

        c.Advance(true, 250, false);     // baseline
        Assert.False(c.Advance(ok: false, lastEventIndex: 0, inReplay: false));
        Assert.Equal(250, c.HighWater);
    }

    [Fact]
    public void ReplayScansItsOwnRewoundSpan_AndLeavesTheLiveBookmarkAlone()
    {
        var c = new PlayedEventCursor();
        c.Advance(true, 250, false);                 // baseline
        Assert.True(c.Advance(true, 259, false));    // live reaches the goal
        Assert.Equal(259, c.HighWater);

        // The replay rewinds the event window. The first in-replay tick opens the replay bookmark there
        // and stays silent (the span just scanned was measured from the live bookmark, so it is empty).
        Assert.False(c.Advance(true, 248, inReplay: true));
        Assert.Equal(248, c.HighWater);              // the scan now searches forward from the rewound point

        // Playing forward through the replay fires as the events are re-shown.
        Assert.True(c.Advance(true, 255, inReplay: true));
        Assert.True(c.Advance(true, 259, inReplay: true));

        // Live resumes from where it was: the goal cannot echo a third time.
        Assert.False(c.Advance(true, 259, inReplay: false));
        Assert.Equal(259, c.HighWater);
        Assert.True(c.Advance(true, 260, inReplay: false));
    }

    [Fact]
    public void EachCameraAngleInsideOneReplayOpensItsOwnPass()
    {
        var c = new PlayedEventCursor();
        c.Advance(true, 250, false);
        c.Advance(true, 259, false);
        c.Advance(true, 248, inReplay: true);        // replay opens
        Assert.True(c.Advance(true, 257, inReplay: true));

        // A second angle rewinds inside the same replay: the rewind opens a fresh pass silently, and the
        // moment sounds again as that angle plays through it.
        Assert.False(c.Advance(true, 249, inReplay: true));
        Assert.True(c.PassChanged);                  // so the same-moment dedupes clear with it
        Assert.True(c.Advance(true, 257, inReplay: true));
        Assert.True(c.Advance(true, 259, inReplay: true));
    }

    [Fact]
    public void AStalledTickInsideAReplayIsNotANewAngle()
    {
        var c = new PlayedEventCursor();
        c.Advance(true, 250, false);
        c.Advance(true, 240, inReplay: true);
        Assert.True(c.Advance(true, 245, inReplay: true));

        // Slow-motion playback repeats the same index across ticks. That is not a rewind, so it must not
        // open a pass — doing so would clear the dedupes every tick and let one moment sound repeatedly.
        Assert.True(c.Advance(true, 245, inReplay: true));
        Assert.False(c.PassChanged);
        Assert.Equal(245, c.HighWater);
    }

    [Fact]
    public void EachReplayOpensAFreshPass()
    {
        var c = new PlayedEventCursor();
        c.Advance(true, 250, false);
        c.Advance(true, 259, false);

        c.Advance(true, 248, inReplay: true);
        Assert.True(c.Advance(true, 259, inReplay: true));
        c.Advance(true, 259, inReplay: false);       // live again

        // A second replay of the same goal rewinds again and must sound it again, not treat 248..259 as
        // ground its predecessor already covered.
        Assert.False(c.Advance(true, 248, inReplay: true));
        Assert.True(c.Advance(true, 259, inReplay: true));
    }

    [Fact]
    public void PassChangedMarksBothEdgesOfAReplay()
    {
        var c = new PlayedEventCursor();
        c.Advance(true, 250, false);
        Assert.False(c.PassChanged);

        c.Advance(true, 248, inReplay: true);
        Assert.True(c.PassChanged);                  // live -> replay
        c.Advance(true, 250, inReplay: true);
        Assert.False(c.PassChanged);                 // still the same pass

        c.Advance(true, 251, inReplay: false);
        Assert.True(c.PassChanged);                  // replay -> live
        c.Advance(true, 252, inReplay: false);
        Assert.False(c.PassChanged);
    }

    [Fact]
    public void AGapInsideAReplayDoesNotEndThePass()
    {
        var c = new PlayedEventCursor();
        c.Advance(true, 250, false);
        c.Advance(true, 248, inReplay: true);
        Assert.True(c.Advance(true, 252, inReplay: true));

        // An unreadable tick mid-replay must not be mistaken for live resuming, which would strand the
        // replay bookmark and re-fire the rest of the replay against the live one.
        Assert.False(c.Advance(ok: false, lastEventIndex: 0, inReplay: false));
        Assert.False(c.PassChanged);
        Assert.Equal(252, c.HighWater);
        Assert.True(c.Advance(true, 255, inReplay: true));
    }
}
