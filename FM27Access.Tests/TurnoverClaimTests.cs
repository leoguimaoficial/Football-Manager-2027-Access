using FM27Access.Plugin.Server;
using Xunit;

namespace FM27Access.Tests;

/// <summary>
/// The authoritative possession override. The geometric dwell measured a median 2.75s behind the engine's
/// own account of the same turnovers, missing one in three, so an engine event now concedes the ball
/// outright. These pin the three ways that authority must be held back — before baselining, during a replay,
/// and after a break — because each of them, if wrong, moves possession for a turnover the player is not
/// watching.
/// </summary>
public class TurnoverClaimTests
{
    [Fact]
    public void FirstScanBaselinesSilently()
    {
        // Connecting mid-match must adopt the already-played events, not concede possession for an
        // interception the player watched several minutes ago.
        var c = new TurnoverClaim();

        var claimed = c.Observe(ok: true, lastEventIndex: 400, inReplay: false, occurred: true, forRef: true);

        Assert.Equal(PossessionHolder.None, claimed);
        Assert.Equal(400, c.HighWater);
    }

    [Fact]
    public void ConfirmedEventConcedesToTheNamedTeam()
    {
        var c = new TurnoverClaim();
        c.Observe(ok: true, lastEventIndex: 10, inReplay: false, occurred: false, forRef: false);

        Assert.Equal(PossessionHolder.Opposition,
            c.Observe(ok: true, lastEventIndex: 11, inReplay: false, occurred: true, forRef: false));
        Assert.Equal(PossessionHolder.Reference,
            c.Observe(ok: true, lastEventIndex: 12, inReplay: false, occurred: true, forRef: true));
    }

    [Fact]
    public void ReplayNeitherClaimsNorAdvances()
    {
        // A replayed interception must not move the LIVE claim, and must still land once live play resumes —
        // which is why the high-water is frozen rather than merely the claim suppressed.
        var c = new TurnoverClaim();
        c.Observe(ok: true, lastEventIndex: 10, inReplay: false, occurred: false, forRef: false);

        Assert.Equal(PossessionHolder.None,
            c.Observe(ok: true, lastEventIndex: 15, inReplay: true, occurred: true, forRef: true));
        Assert.Equal(10, c.HighWater);
    }

    [Fact]
    public void UnreadableScanLeavesTheHighWaterAlone()
    {
        // Advancing past events that were never examined would silently swallow every turnover in the gap.
        var c = new TurnoverClaim();
        c.Observe(ok: true, lastEventIndex: 10, inReplay: false, occurred: false, forRef: false);

        Assert.Equal(PossessionHolder.None,
            c.Observe(ok: false, lastEventIndex: 99, inReplay: false, occurred: true, forRef: true));
        Assert.Equal(10, c.HighWater);
    }

    [Fact]
    public void ResetRebaselinesSoANewMatchIsNotSuppressed()
    {
        // Event indices restart with a new match. A high-water carried over from the last one sits above
        // every index of the next, which would suppress all of its turnovers rather than a few.
        var c = new TurnoverClaim();
        c.Observe(ok: true, lastEventIndex: 5000, inReplay: false, occurred: false, forRef: false);

        c.Reset();
        Assert.Equal(-1, c.HighWater);

        // The first scan of the new match baselines low instead of acting on its backlog...
        Assert.Equal(PossessionHolder.None,
            c.Observe(ok: true, lastEventIndex: 3, inReplay: false, occurred: true, forRef: true));
        Assert.Equal(3, c.HighWater);

        // ...and it is live again from there.
        Assert.Equal(PossessionHolder.Reference,
            c.Observe(ok: true, lastEventIndex: 4, inReplay: false, occurred: true, forRef: true));
    }
}
