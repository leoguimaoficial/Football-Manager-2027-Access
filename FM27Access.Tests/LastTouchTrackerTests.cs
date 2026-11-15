using FM27Access.Plugin.Server;
using Xunit;

namespace FM27Access.Tests;

/// <summary>
/// The last-touch possession judgement. The contract: winning the ball takes the challenger's SECOND
/// touch, unless the first is decisive — which flips at once but provisionally, so a block the losing
/// team recovers inside the window reverts; too-close-to-call touches hold the incumbent but a run of
/// them forfeits the claim (a scramble), while touches nobody is near are neutral; and authority events
/// (restarts, engine turnovers) override the touch stream instantly. These pin the transitions the
/// ticks depend on.
/// </summary>
public class LastTouchTrackerTests
{
    [Fact]
    public void FirstAttributedTouchClaimsFromNone()
    {
        var t = new LastTouchTracker();

        t.ObserveTouch(TouchAttribution.Reference, 100);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
        Assert.Equal(100, t.LastEvidenceMs);
    }

    [Fact]
    public void SingleOpposingTouchDoesNotFlip()
    {
        // A block, a deflection, a stretched toe-poke: one opposing touch is not a turnover, and the
        // first live version that flipped on it audibly called every failed clearance one.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);

        t.ObserveTouch(TouchAttribution.Opposition, 200);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
    }

    [Fact]
    public void SecondOpposingTouchConfirmsTheFlip()
    {
        // An interceptor's next touch (or a clearance reaching a teammate — touches are team-level)
        // is what a deflection cannot produce, and it confirms the turnover immediately.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);

        t.ObserveTouch(TouchAttribution.Opposition, 200);
        t.ObserveTouch(TouchAttribution.Opposition, 600);

        Assert.Equal(PossessionHolder.Opposition, t.Holder);
    }

    [Fact]
    public void HolderTouchCancelsTheChallenge()
    {
        // Deflection off a defender straight back to the attackers: the holder's next touch proves
        // they never lost it, so the defender's later isolated touch starts over rather than confirming.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);
        t.ObserveTouch(TouchAttribution.Opposition, 200);

        t.ObserveTouch(TouchAttribution.Reference, 400);
        t.ObserveTouch(TouchAttribution.Opposition, 600);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
    }

    [Fact]
    public void ChallengeSurvivesUnclearTouchesBetween()
    {
        // An interception often rebounds off nobody-in-particular before the winner's second touch;
        // the unclear touch in between must not void the challenge.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);
        t.ObserveTouch(TouchAttribution.Opposition, 200);

        t.ObserveTouch(TouchAttribution.Unknown, 400);
        t.ObserveTouch(TouchAttribution.Opposition, 600);

        Assert.Equal(PossessionHolder.Opposition, t.Holder);
    }

    [Fact]
    public void StaleChallengeRestartsInsteadOfConfirming()
    {
        // Two opposing touches far apart are unrelated events, not a won ball; the second reopens the
        // challenge rather than confirming off ancient evidence.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);
        t.ObserveTouch(TouchAttribution.Opposition, 200);

        t.ObserveTouch(TouchAttribution.Opposition, 5000, challengeLapseMs: 2500);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
        // ...but it did reopen: a further prompt touch now confirms.
        t.ObserveTouch(TouchAttribution.Opposition, 5400, challengeLapseMs: 2500);
        Assert.Equal(PossessionHolder.Opposition, t.Holder);
    }

    [Fact]
    public void GeometricControlConfirmsAPendingChallenge()
    {
        // A clean interception whose second touch goes undetected: the geometry seeing the winner on
        // the ball is the second witness, and the flip must not wait for a touch that never registers.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);
        t.ObserveTouch(TouchAttribution.Opposition, 200);

        t.ObserveGeometricControl(forRef: false, 500);

        Assert.Equal(PossessionHolder.Opposition, t.Holder);
    }

    [Fact]
    public void GeometricControlAloneNeverTakesTheBall()
    {
        // Without a pending challenge this is exactly the proximity guessing the touch system exists
        // to replace: no flip, and an unowned ball stays unowned.
        var t = new LastTouchTracker();
        t.ObserveGeometricControl(forRef: true, 100);
        Assert.Equal(PossessionHolder.None, t.Holder);

        t.ObserveTouch(TouchAttribution.Reference, 200);
        t.ObserveGeometricControl(forRef: false, 300);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
    }

    [Fact]
    public void HolderGeometricControlCancelsTheChallenge()
    {
        // The holder demonstrably back on the ball is proof it never lost it, same as its own touch.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);
        t.ObserveTouch(TouchAttribution.Opposition, 200);

        t.ObserveGeometricControl(forRef: true, 300);
        t.ObserveTouch(TouchAttribution.Opposition, 400);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
    }

    [Fact]
    public void GeometricControlCannotConfirmAStaleChallenge()
    {
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);
        t.ObserveTouch(TouchAttribution.Opposition, 200);

        t.ObserveGeometricControl(forRef: false, 5000, challengeLapseMs: 2500);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
    }

    [Fact]
    public void ScrambleExpiresTheClaim()
    {
        // Three too-close-to-call touches in a row is a scramble; ticking the pre-scramble team through
        // it is the stuck-holder defect heard live, so the claim is forfeited to silence instead.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);

        t.ObserveTouch(TouchAttribution.Ambiguous, 200);
        t.ObserveTouch(TouchAttribution.Ambiguous, 300);
        Assert.Equal(PossessionHolder.Reference, t.Holder);
        t.ObserveTouch(TouchAttribution.Ambiguous, 400);

        Assert.Equal(PossessionHolder.None, t.Holder);
        Assert.Equal(1, t.ScrambleExpiries);
        // The first clear touch out of the pile takes the unowned ball with no challenge needed.
        t.ObserveTouch(TouchAttribution.Opposition, 500);
        Assert.Equal(PossessionHolder.Opposition, t.Holder);
    }

    [Fact]
    public void UnknownTouchesNeverExpireTheClaim()
    {
        // A touch nobody is near is a bounce, not a pile-up: a long ball skipping across empty grass must
        // not forfeit the claim (that was the long through-ball silence heard live). The far-loose mute,
        // not the scramble rule, owns a ball nobody is near.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);

        for (int i = 1; i <= 6; i++) t.ObserveTouch(TouchAttribution.Unknown, 100 + i * 100);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
        Assert.Equal(0, t.ScrambleExpiries);
    }

    [Fact]
    public void UnknownTouchDoesNotBreakTheScrambleRun()
    {
        // Neutral both ways: a stray bounce inside a scramble neither counts towards it nor resets it.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);

        t.ObserveTouch(TouchAttribution.Ambiguous, 200);
        t.ObserveTouch(TouchAttribution.Ambiguous, 300);
        t.ObserveTouch(TouchAttribution.Unknown, 400);
        t.ObserveTouch(TouchAttribution.Ambiguous, 500);

        Assert.Equal(PossessionHolder.None, t.Holder);
        Assert.Equal(1, t.ScrambleExpiries);
    }

    [Fact]
    public void DecisiveOpposingTouchFlipsAtOnce()
    {
        // A clean win (toucher on the ball, the other team clearly further away) needs no second touch.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);

        t.ObserveTouch(TouchAttribution.Opposition, 200, decisive: true);

        Assert.Equal(PossessionHolder.Opposition, t.Holder);
        Assert.Equal(PossessionHolder.Reference, t.ProvisionalFrom);
        Assert.Equal(1, t.DecisiveFlips);
    }

    [Fact]
    public void LosingTeamTouchInsideTheWindowRevertsADecisiveFlip()
    {
        // A block looks decisive too; the attackers recovering it inside the window take it straight
        // back on one touch, so the false switch lasts under a second.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);
        t.ObserveTouch(TouchAttribution.Opposition, 200, decisive: true);

        t.ObserveTouch(TouchAttribution.Reference, 900);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
        Assert.Equal(PossessionHolder.None, t.ProvisionalFrom);
        Assert.Equal(1, t.DecisiveReverts);
    }

    [Fact]
    public void LosingTeamTouchAfterTheWindowOnlyChallenges()
    {
        // Once the window lapses the flip is an ordinary holding: winning it back needs the usual two.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);
        t.ObserveTouch(TouchAttribution.Opposition, 200, decisive: true);

        t.ObserveTouch(TouchAttribution.Reference, 1300);

        Assert.Equal(PossessionHolder.Opposition, t.Holder);
        Assert.Equal(PossessionHolder.Reference, t.Challenger);
        Assert.Equal(0, t.DecisiveReverts);
    }

    [Fact]
    public void NewHolderTouchMakesADecisiveFlipFinal()
    {
        // The winner's own next touch is the proof a block cannot give: the flip stops being reversible,
        // and a later single touch by the old team is only a challenge.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);
        t.ObserveTouch(TouchAttribution.Opposition, 200, decisive: true);

        t.ObserveTouch(TouchAttribution.Opposition, 400);
        Assert.Equal(PossessionHolder.None, t.ProvisionalFrom);
        t.ObserveTouch(TouchAttribution.Reference, 600);

        Assert.Equal(PossessionHolder.Opposition, t.Holder);
    }

    [Fact]
    public void LosingTeamGeometricControlInsideTheWindowReverts()
    {
        // The block-recovered case witnessed by the geometry rather than a detected touch.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);
        t.ObserveTouch(TouchAttribution.Opposition, 200, decisive: true);

        t.ObserveGeometricControl(forRef: true, 700);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
        Assert.Equal(1, t.DecisiveReverts);
    }

    [Fact]
    public void AuthorityClearsAProvisionalFlip()
    {
        // An engine turnover names the holder outright; a provisional window left open after it could
        // let the old team's touch undo the engine's own account.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);
        t.ObserveTouch(TouchAttribution.Opposition, 200, decisive: true);

        t.ObserveEngineTurnover(forRef: false, 300);
        t.ObserveTouch(TouchAttribution.Reference, 400);

        Assert.Equal(PossessionHolder.Opposition, t.Holder);
        Assert.Equal(PossessionHolder.None, t.ProvisionalFrom);
    }

    [Fact]
    public void DecisiveTouchByTheHolderChangesNothing()
    {
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);

        t.ObserveTouch(TouchAttribution.Reference, 200, decisive: true);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
        Assert.Equal(0, t.DecisiveFlips);
    }

    [Fact]
    public void AttributedTouchResetsTheUnclearRun()
    {
        // Unclear touches interleaved with the holder's own do not accumulate into an expiry.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);
        t.ObserveTouch(TouchAttribution.Ambiguous, 200);
        t.ObserveTouch(TouchAttribution.Ambiguous, 300);

        t.ObserveTouch(TouchAttribution.Reference, 400);
        t.ObserveTouch(TouchAttribution.Ambiguous, 500);
        t.ObserveTouch(TouchAttribution.Ambiguous, 600);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
    }

    [Fact]
    public void AmbiguousTouchHoldsTheIncumbent()
    {
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);

        t.ObserveTouch(TouchAttribution.Ambiguous, 200);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
        Assert.Equal(1, t.AmbiguousHeld);
    }

    [Fact]
    public void AmbiguousTouchFromNoneStaysNone()
    {
        // A touch we cannot name must not conjure a holder out of silence — the ticks would then assert
        // a claim nothing supports.
        var t = new LastTouchTracker();

        t.ObserveTouch(TouchAttribution.Ambiguous, 100);

        Assert.Equal(PossessionHolder.None, t.Holder);
    }

    [Fact]
    public void UnknownTouchHoldsTheIncumbent()
    {
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Opposition, 100);

        t.ObserveTouch(TouchAttribution.Unknown, 200);

        Assert.Equal(PossessionHolder.Opposition, t.Holder);
        Assert.Equal(1, t.UnattributedHeld);
    }

    [Fact]
    public void RestartSetsHolderAndRestarter()
    {
        // The restarting side owns a dead ball by rule; the pending restarter is what lets the cue tick
        // that team while the ball is out.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);

        t.ObserveRestart(forRef: false, 200);

        Assert.Equal(PossessionHolder.Opposition, t.Holder);
        Assert.Equal(PossessionHolder.Opposition, t.Restarter);
    }

    [Fact]
    public void AttributedTouchConsumesThePendingRestart()
    {
        // Any nameable touch proves the ball back in play, even an opponent's isolated one — but that
        // single touch is still not a turnover: the restarting side keeps the ball until a second.
        var t = new LastTouchTracker();
        t.ObserveRestart(forRef: true, 100);

        t.ObserveTouch(TouchAttribution.Opposition, 200);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
        Assert.Equal(PossessionHolder.None, t.Restarter);
    }

    [Fact]
    public void AmbiguousTouchDoesNotConsumeThePendingRestart()
    {
        // A touch we could not attribute is no proof the restart was taken; the dead-ball tick must
        // keep naming the restarting team.
        var t = new LastTouchTracker();
        t.ObserveRestart(forRef: true, 100);

        t.ObserveTouch(TouchAttribution.Ambiguous, 200);

        Assert.Equal(PossessionHolder.Reference, t.Restarter);
        Assert.Equal(PossessionHolder.Reference, t.Holder);
    }

    [Fact]
    public void EngineTurnoverOverridesTouchDerivedHolder()
    {
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);

        t.ObserveEngineTurnover(forRef: false, 200);

        Assert.Equal(PossessionHolder.Opposition, t.Holder);
    }

    [Fact]
    public void ResetClearsBothAndAmbiguityDoesNotResurrect()
    {
        // After a gap we cannot see into (a skip), the old claim is forfeit; the next unnameable touch
        // must leave the ticks silent rather than re-asserting it.
        var t = new LastTouchTracker();
        t.ObserveTouch(TouchAttribution.Reference, 100);
        t.ObserveRestart(forRef: true, 150);

        t.Reset();
        t.ObserveTouch(TouchAttribution.Ambiguous, 300);

        Assert.Equal(PossessionHolder.None, t.Holder);
        Assert.Equal(PossessionHolder.None, t.Restarter);
    }

    [Fact]
    public void RestartIsIdempotent()
    {
        // Multiple SSE streams each feed the same engine event; a duplicate must change nothing.
        var t = new LastTouchTracker();
        t.ObserveRestart(forRef: true, 100);

        t.ObserveRestart(forRef: true, 110);

        Assert.Equal(PossessionHolder.Reference, t.Holder);
        Assert.Equal(PossessionHolder.Reference, t.Restarter);
    }
}
