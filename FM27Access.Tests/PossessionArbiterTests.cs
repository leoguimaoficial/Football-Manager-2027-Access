using FM27Access.Plugin.Server;
using Xunit;

namespace FM27Access.Tests;

/// <summary>
/// The turnover dwell. The geometric cue read has no memory, so a ball rolling past an opponent used to hand
/// him the metronome for a sample or two and then hand it back, which the player heard as the two team ticks
/// alternating several times a second. These pin the confirmation that stops it, and equally that a real
/// turnover still lands.
/// </summary>
public class PossessionArbiterTests
{
    private const long Dwell = 375;
    private const long Sample = 125;   // the SSE loop's sub-tick cadence

    /// <summary>Give the reference team an established, confirmed claim to hold.</summary>
    private static PossessionArbiter WithReferenceHolding()
    {
        var a = new PossessionArbiter();
        // An unowned ball is taken on the first touch; the samples after it only settle the claim.
        for (long t = 0; t <= Dwell; t += Sample)
            a.Observe(CueControl.InControl, CueControl.HardLoss, t, Dwell);
        Assert.Equal(PossessionHolder.Reference, a.Holder);
        return a;
    }

    [Fact]
    public void UnownedBallIsTakenOnTheFirstTouch()
    {
        // With no incumbent there is no turnover to guard against, so the dwell must not apply. Charging it
        // anyway left the cue silent for whole passages: every between-highlights blip resets the claim, and
        // re-earning it then needed the dwell's worth of consecutive contact samples, which a live trace
        // measured taking 21.75s with the ball at a player's feet the whole time.
        var a = new PossessionArbiter();

        a.Observe(CueControl.InControl, CueControl.HardLoss, 0, Dwell);

        Assert.Equal(PossessionHolder.Reference, a.Holder);
    }

    [Fact]
    public void ReclaimAfterAResetIsImmediateButATransferStillDwells()
    {
        // The two halves of the rule in one run, because it is their ASYMMETRY that matters: picking up a
        // ball nobody holds is free, taking one off an opponent is not. A test that only pinned the first
        // would pass just as well if the dwell had been dropped altogether.
        var a = WithReferenceHolding();
        a.Reset();

        // Free: the opposition's first touch after the reset is enough.
        a.Observe(CueControl.HardLoss, CueControl.InControl, 1000, Dwell);
        Assert.Equal(PossessionHolder.Opposition, a.Holder);

        // Not free: the reference team now has to serve the full dwell to take it back.
        a.Observe(CueControl.InControl, CueControl.HardLoss, 1000 + Sample, Dwell);
        Assert.Equal(PossessionHolder.Opposition, a.Holder);
        a.Observe(CueControl.InControl, CueControl.HardLoss, 1000 + Sample * 2, Dwell);
        Assert.Equal(PossessionHolder.Opposition, a.Holder);

        a.Observe(CueControl.InControl, CueControl.HardLoss, 1000 + Sample + Dwell, Dwell);
        Assert.Equal(PossessionHolder.Reference, a.Holder);
    }

    [Fact]
    public void ADeflectionOffAnUnownedBallDoesNotSurviveAsAClaim()
    {
        // The immediate adoption must not become a back door for the very thing the dwell rejects: a
        // deflection by the side that does NOT go on to hold the ball. Adoption is honest only because the
        // next team to touch it has to earn the transfer normally, so a one-sample nick is overwritten
        // rather than defended.
        var a = new PossessionArbiter();

        // A nick off the opposition takes the unowned ball...
        a.Observe(CueControl.HardLoss, CueControl.InControl, 0, Dwell);
        Assert.Equal(PossessionHolder.Opposition, a.Holder);

        // ...and the reference team, genuinely on it from here, takes it back after the dwell and keeps it.
        for (long t = Sample; t <= Sample + Dwell; t += Sample)
            a.Observe(CueControl.InControl, CueControl.HardLoss, t, Dwell);

        Assert.Equal(PossessionHolder.Reference, a.Holder);
    }

    [Fact]
    public void InterceptionConfirmsThroughTheLooseSamplesBetweenTouches()
    {
        // The defect this rule exists for. A defender wins the ball and knocks it ahead as he takes it, so
        // his touches are separated by loose samples. Under the old consecutive-sample dwell the loose one
        // reset his clock and the turnover never confirmed — a live trace measured only 59% of challenges
        // converting, and the dispossessed team kept ticking.
        var a = WithReferenceHolding();
        long t = Dwell;

        a.Observe(CueControl.HardLoss, CueControl.InControl, t + Sample, Dwell);        // wins it
        a.Observe(CueControl.SoftLoss, CueControl.SoftLoss, t + Sample * 2, Dwell);     // knocked ahead
        a.Observe(CueControl.SoftLoss, CueControl.SoftLoss, t + Sample * 3, Dwell);     // still travelling
        Assert.Equal(PossessionHolder.Reference, a.Holder);                             // not yet: one touch

        a.Observe(CueControl.HardLoss, CueControl.InControl, t + Sample * 4, Dwell);    // catches it again

        Assert.Equal(PossessionHolder.Opposition, a.Holder);
    }

    [Fact]
    public void OneTouchNeverConfirmsHoweverLongTheBallStaysLoose()
    {
        // The other half: tolerating loose samples must not let a DEFLECTION confirm simply by waiting. The
        // ball bounces off an opponent once and then runs loose well past the dwell; without the two-touch
        // requirement the elapsed time alone would hand him possession.
        var a = WithReferenceHolding();
        long t = Dwell;

        a.Observe(CueControl.HardLoss, CueControl.InControl, t + Sample, Dwell);
        for (int i = 2; i < 10; i++)
            a.Observe(CueControl.SoftLoss, CueControl.SoftLoss, t + Sample * i, Dwell);

        Assert.Equal(PossessionHolder.Reference, a.Holder);
    }

    [Fact]
    public void IncumbentTouchingTheBallAgainEndsTheChallenge()
    {
        // What now cancels a challenge is the man who had the ball getting it back — not the ball merely
        // being loose. After that the challenger starts from nothing, so his earlier touch cannot combine
        // with a later one to reach two.
        var a = WithReferenceHolding();
        long t = Dwell;

        a.Observe(CueControl.HardLoss, CueControl.InControl, t + Sample, Dwell);       // challenger touch 1
        a.Observe(CueControl.InControl, CueControl.HardLoss, t + Sample * 2, Dwell);   // incumbent reclaims
        a.Observe(CueControl.HardLoss, CueControl.InControl, t + Sample * 3, Dwell);   // fresh touch 1
        a.Observe(CueControl.SoftLoss, CueControl.SoftLoss, t + Sample * 4, Dwell);

        Assert.Equal(PossessionHolder.Reference, a.Holder);
    }

    [Fact]
    public void ChallengeExpiresAfterTheLapseSoAnOldTouchCannotConfirmLater()
    {
        // Without an expiry a challenge would live forever across loose samples, and a touch minutes later
        // would confirm a turnover off a nick nobody remembers. Two touches straddling the lapse must count
        // as two separate first touches rather than one completed challenge.
        var a = WithReferenceHolding();
        long t = Dwell;
        const long Lapse = 1500;

        a.Observe(CueControl.HardLoss, CueControl.InControl, t + Sample, Dwell, Lapse);
        for (long u = t + Sample * 2; u <= t + Sample + Lapse + Sample; u += Sample)
            a.Observe(CueControl.SoftLoss, CueControl.SoftLoss, u, Dwell, Lapse);

        // The challenge has lapsed, so this reads as a first touch, not a confirming second one.
        a.Observe(CueControl.HardLoss, CueControl.InControl, t + Sample * 3 + Lapse * 2, Dwell, Lapse);

        Assert.Equal(PossessionHolder.Reference, a.Holder);
    }

    [Fact]
    public void BriefChallengeDoesNotTakeTheCue()
    {
        // The bug itself: a pass rolling past an opponent puts him on the ball for two samples.
        var a = WithReferenceHolding();
        long t = Dwell;

        for (int i = 0; i < 2; i++)
        {
            t += Sample;
            var (r, o) = a.Observe(CueControl.HardLoss, CueControl.InControl, t, Dwell);
            // The incumbent must never surface as a hard loss, or the streamer's grace would clear and the
            // tick would cut - which is exactly the break we are removing.
            Assert.Equal(CueControl.SoftLoss, r);
            Assert.Equal(CueControl.HardLoss, o);
            Assert.Equal(PossessionHolder.Reference, a.Holder);
        }
    }

    [Fact]
    public void ChallengerNeverTicksDuringAnUnconfirmedWindow()
    {
        // Distinct from the above: the two metronomes are computed independently, so without suppression the
        // challenger would tick ALONGSIDE the sustained incumbent rather than instead of it.
        var a = WithReferenceHolding();
        var (_, o) = a.Observe(CueControl.HardLoss, CueControl.InControl, Dwell + Sample, Dwell);
        Assert.NotEqual(CueControl.InControl, o);
    }

    [Fact]
    public void ChallengeHeldForTheDwellTakesTheCue()
    {
        var a = WithReferenceHolding();
        long t = Dwell;

        a.Observe(CueControl.HardLoss, CueControl.InControl, t + Sample, Dwell);       // 0 ms held
        a.Observe(CueControl.HardLoss, CueControl.InControl, t + Sample * 2, Dwell);   // 125 ms
        a.Observe(CueControl.HardLoss, CueControl.InControl, t + Sample * 3, Dwell);   // 250 ms
        Assert.Equal(PossessionHolder.Reference, a.Holder);

        var (r, o) = a.Observe(CueControl.HardLoss, CueControl.InControl, t + Sample * 4, Dwell);   // 375 ms
        Assert.Equal(PossessionHolder.Opposition, a.Holder);
        // Confirmed: the sample passes through untouched so the cue moves now, not a sample later.
        Assert.Equal(CueControl.HardLoss, r);
        Assert.Equal(CueControl.InControl, o);
    }

    [Fact]
    public void ConcedeTakesTheBallWithoutTheDwell()
    {
        // Authority, not evidence: the engine names the team that won the ball, so there is nothing for the
        // dwell to weigh. This is the whole point of the override — the dwell measured seconds of latency
        // behind the engine's account of the same turnovers.
        var a = WithReferenceHolding();

        a.Concede(PossessionHolder.Opposition);

        Assert.Equal(PossessionHolder.Opposition, a.Holder);
    }

    [Fact]
    public void ConcedeDropsAChallengeSoItCannotReconfirmLater()
    {
        // A challenge already under way is superseded by the concession. Left standing, its accumulated
        // touch would let a single later stray touch complete a transfer that has already happened — handing
        // the ball back to a team the engine says lost it.
        var a = WithReferenceHolding();
        long t = Dwell;

        a.Observe(CueControl.HardLoss, CueControl.InControl, t + Sample, Dwell);   // opposition touch 1
        a.Concede(PossessionHolder.Opposition);                                    // engine agrees, outright
        a.Concede(PossessionHolder.Reference);                                     // and then it turns over again

        // The opposition's old challenge must not still be one touch from confirming.
        a.Observe(CueControl.HardLoss, CueControl.InControl, t + Sample * 2, Dwell);
        Assert.Equal(PossessionHolder.Reference, a.Holder);
    }

    [Fact]
    public void ConcedingNothingIsANoOp()
    {
        // "No event this tick" is the common case and must not disturb a settled claim.
        var a = WithReferenceHolding();

        a.Concede(PossessionHolder.None);

        Assert.Equal(PossessionHolder.Reference, a.Holder);
    }

    [Fact]
    public void DuelIsNotAChallenge()
    {
        // A 50-50 is not a claim by either side. Treating it as one would start the dwell on every duel, and
        // the incumbent already keeps ticking through a contest under its own longer grace.
        var a = WithReferenceHolding();
        long t = Dwell;

        for (int i = 1; i <= 8; i++)
        {
            var (r, o) = a.Observe(CueControl.Contested, CueControl.Contested, t + Sample * i, Dwell);
            Assert.Equal(CueControl.Contested, r);
            Assert.Equal(CueControl.Contested, o);
        }
        Assert.Equal(PossessionHolder.Reference, a.Holder);
    }

    [Fact]
    public void TheDwellIsSymmetric()
    {
        // The reference team is the user's, so an asymmetry here would make the mod quicker to believe a
        // turnover in one direction than the other.
        var a = new PossessionArbiter();
        for (long t = 0; t <= Dwell; t += Sample)
            a.Observe(CueControl.HardLoss, CueControl.InControl, t, Dwell);
        Assert.Equal(PossessionHolder.Opposition, a.Holder);

        long u = Dwell;
        a.Observe(CueControl.InControl, CueControl.HardLoss, u + Sample, Dwell);
        a.Observe(CueControl.InControl, CueControl.HardLoss, u + Sample * 2, Dwell);
        Assert.Equal(PossessionHolder.Opposition, a.Holder);      // still unconfirmed
        a.Observe(CueControl.InControl, CueControl.HardLoss, u + Sample * 3, Dwell);
        a.Observe(CueControl.InControl, CueControl.HardLoss, u + Sample * 4, Dwell);
        Assert.Equal(PossessionHolder.Reference, a.Holder);
    }

    [Fact]
    public void NoJudgementDropsTheClaim()
    {
        // Between highlights there are no player objects. Carrying a holder across that gap would let the
        // readout answer for a passage of play nobody watched.
        var a = WithReferenceHolding();
        a.Observe(CueControl.None, CueControl.None, Dwell + Sample, Dwell);
        Assert.Equal(PossessionHolder.None, a.Holder);
    }

    [Fact]
    public void IncumbentKeepsTickingWhileTheBallRunsLoose()
    {
        // A pass in flight is a soft loss for both teams. The arbiter must leave it entirely alone so the
        // streamer's grace decides how long to sustain it - the arbiter governs turnovers, not passes.
        var a = WithReferenceHolding();
        var (r, o) = a.Observe(CueControl.SoftLoss, CueControl.SoftLoss, Dwell + Sample, Dwell);
        Assert.Equal(CueControl.SoftLoss, r);
        Assert.Equal(CueControl.SoftLoss, o);
        Assert.Equal(PossessionHolder.Reference, a.Holder);
    }
}
