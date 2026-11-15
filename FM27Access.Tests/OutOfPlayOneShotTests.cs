using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

// The pure fire-once decision behind the out-of-play sounds: the engine restart event (throw-in / corner /
// goal kick, the corner split by team) is the only trigger — geometry never sounds, it just arms the
// once-per-dead-ball latch, as does the engine's put-in-play take event — with a silent baseline and a
// replay freeze. Each test pins one behaviour the live scan cannot easily be driven through by hand.
public class OutOfPlayOneShotTests
{
    // A readable restart scan that reports no restart, advancing the played high-water to `last`. The
    // geometry defaults to in play; individual tests override it.
    private static OutOfPlaySound Quiet(ref OutOfPlayOneShot o, int last, OutOfPlayKind geom = OutOfPlayKind.InPlay)
        => o.Observe(evOk: true, evLastIndex: last, inReplay: false,
                     evOccurred: false, evKind: RestartKind.None, evForRef: false, evPutOccurred: false,
                     geomKind: geom);

    [Fact]
    public void FirstReadBaselinesSilently_AndReportsMinusOneBefore()
    {
        var o = new OutOfPlayOneShot();
        Assert.Equal(-1, o.EventHighWater);   // before any read: baseline, do not search

        // A reconnect mid-throw-in: the restart already entered the window and the ball is already out — must
        // NOT fire, and must adopt both the event high-water and the geometry state.
        var s = o.Observe(true, 120, false, evOccurred: true, evKind: RestartKind.ThrowIn, evForRef: true,
                          evPutOccurred: false, geomKind: OutOfPlayKind.Touchline);
        Assert.Equal(OutOfPlaySound.None, s);
        Assert.Equal(120, o.EventHighWater);
    }

    [Fact]
    public void GeometryAloneNeverSounds_TheEventIsTheOnlyTrigger()
    {
        var o = new OutOfPlayOneShot();
        Quiet(ref o, 100);   // baseline, in play

        // The ball crosses the side line but no restart event has landed yet: silence (the event is the
        // only trigger)...
        Assert.Equal(OutOfPlaySound.None, Quiet(ref o, 100, OutOfPlayKind.Touchline));
        // ...then the throw-in event lands and sounds exactly once.
        Assert.Equal(OutOfPlaySound.ThrowIn,
            o.Observe(true, 104, false, evOccurred: true, evKind: RestartKind.ThrowIn, evForRef: true,
                      evPutOccurred: false, geomKind: OutOfPlayKind.Touchline));
        // Still the same dead ball: no repeat.
        Assert.Equal(OutOfPlaySound.None, Quiet(ref o, 104, OutOfPlayKind.Touchline));
    }

    [Fact]
    public void ThrowInFiresFromTheEvent_EvenWhenGeometryStillReadsInPlay()
    {
        var o = new OutOfPlayOneShot();
        Quiet(ref o, 100);
        // A fast crossing the 2 Hz geometry never sampled (still reads in play): the event alone fires.
        Assert.Equal(OutOfPlaySound.ThrowIn,
            o.Observe(true, 104, false, evOccurred: true, evKind: RestartKind.ThrowIn, evForRef: false,
                      evPutOccurred: false, geomKind: OutOfPlayKind.InPlay));
    }

    [Fact]
    public void GoalKickSoundsOnlyFromTheEvent_NotFromGoalLineGeometry()
    {
        var o = new OutOfPlayOneShot();
        Quiet(ref o, 100);
        // The ball is out over a goal-line: geometry cannot tell a goal kick from a corner, so it stays silent.
        Assert.Equal(OutOfPlaySound.None, Quiet(ref o, 100, OutOfPlayKind.GoalLine));
        // The engine's goal-kick event classifies it: now it sounds.
        Assert.Equal(OutOfPlaySound.GoalKick,
            o.Observe(true, 106, false, evOccurred: true, evKind: RestartKind.GoalKick, evForRef: false,
                      evPutOccurred: false, geomKind: OutOfPlayKind.GoalLine));
    }

    [Fact]
    public void CornerIsSplitByTeam()
    {
        var forRef = new OutOfPlayOneShot();
        Quiet(ref forRef, 100);
        Assert.Equal(OutOfPlaySound.CornerFor,
            forRef.Observe(true, 106, false, true, RestartKind.Corner, evForRef: true,
                           evPutOccurred: false, geomKind: OutOfPlayKind.GoalLine));

        var against = new OutOfPlayOneShot();
        Quiet(ref against, 100);
        Assert.Equal(OutOfPlaySound.CornerAgainst,
            against.Observe(true, 106, false, true, RestartKind.Corner, evForRef: false,
                            evPutOccurred: false, geomKind: OutOfPlayKind.GoalLine));
    }

    [Fact]
    public void NetIsSilent_LeftToTheGoalDetector()
    {
        var o = new OutOfPlayOneShot();
        Quiet(ref o, 100);
        Assert.Equal(OutOfPlaySound.None, Quiet(ref o, 100, OutOfPlayKind.Net));
    }

    [Fact]
    public void EachDeadBallSoundsOnce_ThenTheNextEpisodeArmsAfterReturningToPlay()
    {
        var o = new OutOfPlayOneShot();
        Quiet(ref o, 100);

        // First throw-in event.
        Assert.Equal(OutOfPlaySound.ThrowIn,
            o.Observe(true, 102, false, evOccurred: true, evKind: RestartKind.ThrowIn, evForRef: true,
                      evPutOccurred: false, geomKind: OutOfPlayKind.Touchline));
        // A duplicate restart event while the same ball is still dead: latched, no repeat.
        Assert.Equal(OutOfPlaySound.None,
            o.Observe(true, 103, false, evOccurred: true, evKind: RestartKind.ThrowIn, evForRef: true,
                      evPutOccurred: false, geomKind: OutOfPlayKind.Touchline));
        // Back in play: episode resets.
        Assert.Equal(OutOfPlaySound.None, Quiet(ref o, 104, OutOfPlayKind.InPlay));
        // The next dead ball's event sounds again.
        Assert.Equal(OutOfPlaySound.ThrowIn,
            o.Observe(true, 106, false, evOccurred: true, evKind: RestartKind.ThrowIn, evForRef: true,
                      evPutOccurred: false, geomKind: OutOfPlayKind.Touchline));
    }

    [Fact]
    public void PutInPlayEventArmsTheNextEpisode_WhenGeometryNeverSamplesInPlay()
    {
        var o = new OutOfPlayOneShot();
        Quiet(ref o, 100);

        // Ball out for a throw-in: sounds.
        Assert.Equal(OutOfPlaySound.ThrowIn,
            o.Observe(true, 102, false, evOccurred: true, evKind: RestartKind.ThrowIn, evForRef: true,
                      evPutOccurred: false, geomKind: OutOfPlayKind.Touchline));
        // Taken quickly and out again before any 2 Hz geometry sample caught the ball in play: the take
        // event alone re-arms the episode, so the second restart is NOT swallowed. Both land in one span
        // here (the take precedes the new award in engine time, so arming precedes the fire).
        Assert.Equal(OutOfPlaySound.ThrowIn,
            o.Observe(true, 105, false, evOccurred: true, evKind: RestartKind.ThrowIn, evForRef: false,
                      evPutOccurred: true, geomKind: OutOfPlayKind.Touchline));
    }

    [Fact]
    public void PutInPlayEventAloneDoesNotSound_AndAWholeQuietSpanStaysQuiet()
    {
        var o = new OutOfPlayOneShot();
        Quiet(ref o, 100);
        Assert.Equal(OutOfPlaySound.ThrowIn,
            o.Observe(true, 102, false, evOccurred: true, evKind: RestartKind.ThrowIn, evForRef: true,
                      evPutOccurred: false, geomKind: OutOfPlayKind.Touchline));
        // The take with no new restart: re-arms silently, never sounds anything itself.
        Assert.Equal(OutOfPlaySound.None,
            o.Observe(true, 104, false, evOccurred: false, evKind: RestartKind.None, evForRef: false,
                      evPutOccurred: true, geomKind: OutOfPlayKind.Touchline));
        // The next dead ball's event sounds (armed by the take, though geometry stayed dead throughout).
        Assert.Equal(OutOfPlaySound.ThrowIn,
            o.Observe(true, 107, false, evOccurred: true, evKind: RestartKind.ThrowIn, evForRef: true,
                      evPutOccurred: false, geomKind: OutOfPlayKind.Touchline));
    }

    [Fact]
    public void ARestartInsideAReplayArmsOnlyThatPass()
    {
        var o = new OutOfPlayOneShot();
        Quiet(ref o, 100);
        Assert.Equal(OutOfPlaySound.ThrowIn,
            o.Observe(true, 102, false, evOccurred: true, evKind: RestartKind.ThrowIn, evForRef: true,
                      evPutOccurred: false, geomKind: OutOfPlayKind.Touchline));

        // A replay opens at the rewound index (silent) and re-shows the take, which arms the replay pass.
        Assert.Equal(OutOfPlaySound.None,
            o.Observe(true, 98, inReplay: true, evOccurred: false, evKind: RestartKind.None, evForRef: false,
                      evPutOccurred: false, geomKind: OutOfPlayKind.Touchline));
        Assert.Equal(OutOfPlaySound.None,
            o.Observe(true, 104, inReplay: true, evOccurred: false, evKind: RestartKind.None, evForRef: false,
                      evPutOccurred: true, geomKind: OutOfPlayKind.Touchline));

        // Live resumes on its own untouched bookmark, and the next genuinely new restart still sounds.
        Assert.Equal(OutOfPlaySound.None,
            o.Observe(true, 102, inReplay: false, evOccurred: false, evKind: RestartKind.None, evForRef: false,
                      evPutOccurred: false, geomKind: OutOfPlayKind.Touchline));
        Assert.Equal(102, o.EventHighWater);
        Assert.Equal(OutOfPlaySound.ThrowIn,
            o.Observe(true, 107, false, evOccurred: true, evKind: RestartKind.ThrowIn, evForRef: false,
                      evPutOccurred: true, geomKind: OutOfPlayKind.Touchline));
    }

    [Fact]
    public void RestartSoundsAgainWithItsReplay_ThenDoesNotEchoWhenLiveResumes()
    {
        var o = new OutOfPlayOneShot();
        Quiet(ref o, 100);
        Assert.Equal(OutOfPlaySound.GoalKick,
            o.Observe(true, 106, false, true, RestartKind.GoalKick, false, false, OutOfPlayKind.GoalLine));

        // The restart's replay rewinds the window and re-shows the ball going out: it sounds again. The
        // once-per-episode latch is per pass, or the still-dead ball would swallow the replayed restart.
        Assert.Equal(OutOfPlaySound.None,
            o.Observe(true, 101, inReplay: true, evOccurred: false, evKind: RestartKind.None, evForRef: false,
                      evPutOccurred: false, geomKind: OutOfPlayKind.GoalLine));
        Assert.Equal(OutOfPlaySound.GoalKick,
            o.Observe(true, 106, inReplay: true, evOccurred: true, evKind: RestartKind.GoalKick, evForRef: false,
                      evPutOccurred: false, geomKind: OutOfPlayKind.GoalLine));

        // Live resumes over the same still-dead ball: the live bookmark is past the restart, so the scan
        // reports nothing and nothing repeats.
        Assert.Equal(OutOfPlaySound.None, Quiet(ref o, 106, OutOfPlayKind.GoalLine));
        Assert.Equal(106, o.EventHighWater);
    }

    [Fact]
    public void NotOkLeavesTheEventHighWaterUntouched()
    {
        var o = new OutOfPlayOneShot();
        Quiet(ref o, 100);   // baseline -> high-water 100

        // A gap (no match / unreadable stream): the event state must not move. Geometry still in play, so no
        // sound either.
        Assert.Equal(OutOfPlaySound.None,
            o.Observe(evOk: false, evLastIndex: 0, inReplay: false, evOccurred: true, evKind: RestartKind.Corner,
                      evForRef: true, evPutOccurred: false, geomKind: OutOfPlayKind.InPlay));
        Assert.Equal(100, o.EventHighWater);
    }

    [Fact]
    public void NotOkOnFirstReadDoesNotBaselineTheEventHighWater()
    {
        var o = new OutOfPlayOneShot();
        // Connection opened before a match exists: the first event ticks are gaps. They must not baseline the
        // event high-water, so it stays -1 until the first readable tick.
        Assert.Equal(OutOfPlaySound.None,
            o.Observe(false, 50, false, true, RestartKind.ThrowIn, true, false, OutOfPlayKind.InPlay));
        Assert.Equal(-1, o.EventHighWater);

        Assert.Equal(OutOfPlaySound.None, Quiet(ref o, 5));   // now baselines
        Assert.Equal(5, o.EventHighWater);
    }
}
