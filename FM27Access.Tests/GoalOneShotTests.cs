using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

// The pure fire-once decision behind the match goal sound. Each test pins one behaviour the live event
// scan cannot easily be driven through by hand: silent baseline, the fire-once latch across a goal's
// replay, attribution, and the no-match / reconnect gaps.
public class GoalOneShotTests
{
    // Convenience: a readable scan with no goal crossed, advancing the played high-water to `last`.
    private static (bool ok, int last, bool replay, bool crossed, bool forRef) Tick(int last, bool replay = false)
        => (true, last, replay, false, false);

    [Fact]
    public void FirstReadBaselinesSilently_AndReportsMinusOneBefore()
    {
        var g = new GoalOneShot();
        Assert.Equal(-1, g.HighWater);   // before any read, tells the scan to baseline (not search)

        // First readable tick with a goal already "crossed" (a reconnect mid-match, the goal already shown):
        // must NOT fire, and must adopt the played high-water so later scans start after it.
        var sfx = g.Observe(ok: true, lastEventIndex: 259, inReplay: false, crossed: true, forRef: true);
        Assert.Null(sfx);
        Assert.Equal(259, g.HighWater);
    }

    [Fact]
    public void GoalForReferenceTeamFiresGoalfor()
    {
        var g = new GoalOneShot();
        g.Observe(ok: true, lastEventIndex: 250, inReplay: false, crossed: false, forRef: false); // baseline
        var sfx = g.Observe(ok: true, lastEventIndex: 259, inReplay: false, crossed: true, forRef: true);
        Assert.Equal("goalfor", sfx);
        Assert.Equal(259, g.HighWater);
    }

    [Fact]
    public void GoalAgainstReferenceTeamFiresGoalagainst()
    {
        var g = new GoalOneShot();
        g.Observe(ok: true, lastEventIndex: 250, inReplay: false, crossed: false, forRef: false); // baseline
        var sfx = g.Observe(ok: true, lastEventIndex: 259, inReplay: false, crossed: true, forRef: false);
        Assert.Equal("goalagainst", sfx);
    }

    [Fact]
    public void GoalFiresLive_AgainWithItsReplay_ThenDoesNotEchoWhenLiveResumes()
    {
        var g = new GoalOneShot();
        g.Observe(ok: true, lastEventIndex: 250, inReplay: false, crossed: false, forRef: false); // baseline

        // Live pass reaches the goal: fires once.
        Assert.Equal("goalfor", g.Observe(true, 259, inReplay: false, crossed: true, forRef: true));

        // The goal's replay rewinds the event window (last drops) and re-shows the goal. The rewind opens
        // the replay's own bookmark silently, then the re-shown goal sounds again with it — the replay is
        // the goal happening on screen a second time, and the sound is how the player follows it.
        Assert.Null(g.Observe(true, 248, inReplay: true, crossed: false, forRef: true));
        Assert.Equal("goalfor", g.Observe(true, 259, inReplay: true, crossed: true, forRef: true));

        // Live resumes over ground the replay covered: silent, and the live high-water was never pulled
        // back, so the goal cannot echo a third time.
        Assert.Null(g.Observe(true, 259, inReplay: false, crossed: true, forRef: true));
        Assert.Equal(259, g.HighWater);
        Assert.Null(g.Observe(true, 260, inReplay: false, crossed: false, forRef: true));
    }

    [Fact]
    public void SecondDistinctGoalFiresAfterTheFirst()
    {
        var g = new GoalOneShot();
        g.Observe(true, 250, false, false, false); // baseline
        Assert.Equal("goalfor", g.Observe(true, 259, false, crossed: true, forRef: true));
        // Play continues; later a second, different goal crosses at a higher index.
        Assert.Null(g.Observe(true, 300, false, crossed: false, forRef: false));
        Assert.Equal("goalagainst", g.Observe(true, 340, false, crossed: true, forRef: false));
    }

    [Fact]
    public void NotOkLeavesStateUntouched()
    {
        var g = new GoalOneShot();
        g.Observe(true, 250, false, false, false); // baseline -> high-water 250

        // A gap (no match / unreadable stream) must neither fire nor disturb the high-water, so the next
        // good read still starts from where we were.
        Assert.Null(g.Observe(ok: false, lastEventIndex: 0, inReplay: false, crossed: true, forRef: true));
        Assert.Equal(250, g.HighWater);

        Assert.Equal("goalfor", g.Observe(true, 260, false, crossed: true, forRef: true));
    }

    [Fact]
    public void NotOkOnFirstReadDoesNotBaseline()
    {
        var g = new GoalOneShot();
        // Connection opened before a match exists: the first ticks are gaps. They must not baseline, so
        // HighWater stays -1 and the real baseline happens on the first readable tick.
        Assert.Null(g.Observe(ok: false, lastEventIndex: 100, inReplay: false, crossed: true, forRef: true));
        Assert.Equal(-1, g.HighWater);

        Assert.Null(g.Observe(ok: true, lastEventIndex: 5, inReplay: false, crossed: true, forRef: true)); // now baselines
        Assert.Equal(5, g.HighWater);
    }

    [Fact]
    public void AReplayNeverDisturbsTheLiveHighWater()
    {
        var g = new GoalOneShot();
        g.Observe(true, 100, false, false, false); // baseline at 100
        // A replay of some earlier non-goal moment runs its own pass over 80..95 and ends.
        Assert.Null(g.Observe(true, 80, inReplay: true, crossed: false, forRef: false));
        Assert.Null(g.Observe(true, 95, inReplay: true, crossed: false, forRef: false));
        // Live resumes exactly where it was, so the events between 95 and 100 — already sounded live —
        // are not scanned again.
        Assert.Null(g.Observe(true, 100, inReplay: false, crossed: true, forRef: false));
        Assert.Equal(100, g.HighWater);
    }
}
