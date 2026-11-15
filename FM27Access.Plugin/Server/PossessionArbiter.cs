namespace FM27Access.Plugin.Server;

/// <summary>
/// Why one team's tick cue is or is not sounding this instant, so the SSE loop can apply a short grace
/// that rides out a brief soft loss (a pass in flight) without letting a real turnover keep ticking.
/// Evaluated once per team (the reference team and the opposition run identical, independent metronomes).
/// <see cref="InControl"/> is this team clearly on the ball (tick the zone). <see cref="SoftLoss"/>
/// is a loose ball, an aerial (uncontested) ball, or the ball out of play — grace-eligible, because a
/// pass by this team reads as one of these while the ball travels. <see cref="HardLoss"/> is a genuine
/// loss of control — the other team on the ball — which cuts the cue at once. <see cref="Contested"/> is a
/// 50-50: not a loss, and not control either, so the side that went into the duel with the ball keeps
/// ticking under the contested clip for as long as the challenge lasts.
/// <see cref="None"/> is no judgement available (no match, or between highlights with no player objects).
///
/// Lives here rather than beside the geometry that produces it because <see cref="PossessionArbiter"/>
/// reasons over it and must stay free of any game dependency to be unit-testable.
/// </summary>
public enum CueControl { None, InControl, SoftLoss, HardLoss, Contested }

/// <summary>Which team the mod currently claims is holding the ball.</summary>
public enum PossessionHolder { None, Reference, Opposition }

/// <summary>
/// Confirms a change of possession before it is allowed to move the tick cues.
///
/// The geometric read (<c>MatchBallLocation.CueForTeam</c>) judges each sample independently, so a ball
/// that merely rolls near an opponent makes him the nearer man for a sample or two and hands him the cue.
/// At the streamer's 125 ms cadence that produced bursts of alternating team ticks — the two metronomes
/// trading the ball several times a second — which reads as the mod being unable to tell who has it.
///
/// The fix is a dwell: a challenger must hold the ball for <c>dwellMs</c> before the claim transfers. A pass
/// going past a defender occupies one sample and is rejected outright; a genuine interception persists and
/// confirms. This costs a real turnover the dwell in latency and nothing else, which is the trade — the
/// alternative (deciding instantly) is what thrashes.
///
/// The dwell counts EVIDENCE, not consecutive samples, and that distinction is load-bearing. Requiring an
/// unbroken run of in-control samples looked equivalent and was not: a defender who wins the ball and knocks
/// it a metre ahead as he takes it produces a loose sample, which reset the clock, so he had to start over —
/// and at a contact-sized on-ball radius, where someone is in control on a small minority of samples, that run of
/// consecutive touches essentially never happened. A live trace measured 22 challenges where the team
/// WITHOUT the claim was demonstrably on the ball and only 59% of them ever moved possession; the rest kept
/// ticking for the side that had just been dispossessed, which is the one thing this class exists to prevent.
///
/// So a challenge now survives loose samples and ends only when the INCUMBENT touches the ball again (he
/// never really lost it) or when the challenger goes <c>lapseMs</c> without a touch (he never really won it).
/// Confirmation needs <c>dwellMs</c> elapsed AND at least two touches, so a single deflection — one touch and
/// no follow-up — still cannot take the ball however long the ball stays loose afterwards.
///
/// <see cref="Observe"/> does not gate the cues itself. It REWRITES the two controls so that an unconfirmed
/// turnover is invisible to everything downstream, which is what lets the existing grace machinery in the
/// SSE loop do the sustaining without any new state there:
///   - the incumbent's <see cref="CueControl.HardLoss"/> becomes a <see cref="CueControl.SoftLoss"/>, so it
///     falls into the grace window already applied to a pass in flight and keeps ticking unbroken. The grace
///     also BOUNDS the claim: a challenge that never confirms lets the incumbent lapse to silence rather
///     than asserting a holder it no longer has.
///   - the challenger's <see cref="CueControl.InControl"/> becomes a <see cref="CueControl.HardLoss"/>, which
///     clears its own grace and guarantees it cannot tick during the window. Without this the challenger
///     would tick alongside the incumbent, because the two metronomes are computed independently and only
///     the strict-nearer test kept them mutually exclusive.
///
/// A duel (<see cref="CueControl.Contested"/>) is deliberately NOT a challenge: it is not a claim to the
/// ball by either side, the incumbent already keeps ticking through it under its own longer grace, and
/// treating it as a challenge would start the dwell on every 50-50.
///
/// Every threshold is a parameter rather than a constant here so the tuning stays with the caller. Pure and
/// stateful across samples, so one instance belongs to one stream.
///
/// This lives in the reloadable Plugin rather than in Navigation deliberately: the rules here are tuned
/// against live football and got three revisions in one session, each of which cost a full game restart
/// while the file sat in Navigation. It has no dependencies of any kind, so FM27Access.Tests compiles this
/// source file directly (see the Compile Include in its csproj) instead of referencing the Plugin assembly,
/// which cannot be loaded without the game. Keep it dependency-free — a using directive for anything Unity
/// or IL2CPP here breaks the test build, which is the intended alarm rather than an inconvenience.
/// </summary>
public sealed class PossessionArbiter
{
    private PossessionHolder _holder = PossessionHolder.None;
    private PossessionHolder _challenger = PossessionHolder.None;
    private long _challengeStartedMs;
    // Touches by the current challenger, and when the last one was. Two touches is what separates a man
    // taking the ball from a man it bounced off; the timestamp is what lets a challenge that has gone dead
    // expire instead of lingering to confirm off a much later, unrelated touch.
    private int _challengeTouches;
    private long _challengeLastTouchMs;

    /// <summary>The team the mod currently claims holds the ball, after confirmation.</summary>
    public PossessionHolder Holder => _holder;

    /// <summary>
    /// Forget the claim and any challenge in progress. For leaving a match, or any break (between
    /// highlights, a stalled feed) after which the previous holder says nothing about the next sample.
    /// </summary>
    public void Reset()
    {
        _holder = PossessionHolder.None;
        DropChallenge();
    }

    /// <summary>
    /// Hand the ball to <paramref name="team"/> on authority rather than on evidence, for a source that
    /// KNOWS who won it — the engine's own turnover events (see <see cref="TurnoverClaim"/>). No dwell, no
    /// touch count: those exist to weigh a geometric guess, and there is nothing to weigh here.
    ///
    /// Any challenge in progress is dropped, including one BY the conceded team: its partial evidence is
    /// superseded, and leaving it standing would let a later stray touch re-confirm a transfer that has
    /// already happened. Conceding to the team that already holds the ball is a no-op beyond that, so a
    /// duplicate event costs nothing.
    /// </summary>
    public void Concede(PossessionHolder team)
    {
        if (team == PossessionHolder.None) return;
        _holder = team;
        DropChallenge();
    }

    private void DropChallenge()
    {
        _challenger = PossessionHolder.None;
        _challengeStartedMs = 0;
        _challengeTouches = 0;
        _challengeLastTouchMs = 0;
    }

    /// <summary>
    /// Consume one sample and return the controls the cue channels should act on.
    /// </summary>
    /// <param name="reference">The reference team's geometric control this sample.</param>
    /// <param name="opposition">The opposition's geometric control this sample.</param>
    /// <param name="nowMs">Monotonic wall-clock milliseconds; only differences are used.</param>
    /// <param name="dwellMs">How long a challenger must hold the ball before the claim transfers.</param>
    /// <param name="lapseMs">
    /// How long a challenger may go without touching the ball before his challenge is abandoned. Generous by
    /// design: it exists to stop a dead challenge confirming off an unrelated later touch, not to police the
    /// gaps between an interceptor's touches, which is what the consecutive-sample rule got wrong.
    /// </param>
    public (CueControl Reference, CueControl Opposition) Observe(
        CueControl reference, CueControl opposition, long nowMs, long dwellMs, long lapseMs = 1500)
    {
        // No judgement available at all (between highlights): drop the claim rather than carrying it across
        // a gap we cannot see into. The next visible sample starts a fresh dwell.
        if (reference == CueControl.None && opposition == CueControl.None)
        {
            Reset();
            return (reference, opposition);
        }

        PossessionHolder claiming = reference == CueControl.InControl ? PossessionHolder.Reference
            : opposition == CueControl.InControl ? PossessionHolder.Opposition
            : PossessionHolder.None;

        // Nobody claims the ball: a loose ball, a duel, or out of play. The incumbent stands, and a challenge
        // in progress SURVIVES — the ball being loose is the normal state between an interceptor's touches,
        // not evidence he let it go. It only expires once he has gone lapseMs without touching it at all.
        if (claiming == PossessionHolder.None)
        {
            if (_challenger != PossessionHolder.None && nowMs - _challengeLastTouchMs >= lapseMs)
                DropChallenge();
            return (reference, opposition);
        }

        // Nobody holds the ball: the first clear touch takes it, with no dwell. The dwell exists to stop a
        // deflection STEALING possession from a team that has it — with no incumbent there is nothing to
        // steal and nothing to protect, so charging the same price to pick up an unowned ball only buys
        // silence.
        //
        // Measured before this existed: a between-highlights blip resets the claim (see the None/None gate
        // above), and re-earning it then needed the dwell's worth of CONSECUTIVE contact samples by one
        // team. At a contact-sized on-ball radius someone is in control on only ~14% of samples, so that
        // run of three took 21.75 SECONDS to occur in one live window — the cue silent throughout, with the
        // ball at a player's feet and the game in flow. Adopting on the first touch costs nothing in
        // honesty: a claim nobody else holds cannot be a wrong transfer.
        if (_holder == PossessionHolder.None)
        {
            _holder = claiming;
            DropChallenge();
            return (reference, opposition);
        }

        // The incumbent is on the ball: he never lost it, so any challenge against it is over. This is now
        // the ONLY way a challenge is cancelled by play (the other is the lapse above) — which is what makes
        // the test "did the man who had it touch it again?" rather than "was the ball ever loose?".
        if (claiming == _holder)
        {
            DropChallenge();
            return (reference, opposition);
        }

        // A new challenger, or the same one continuing. Restarting the clock on a new challenger is what
        // stops two players alternating as nearest from accumulating a transfer between them.
        if (claiming != _challenger)
        {
            _challenger = claiming;
            _challengeStartedMs = nowMs;
            _challengeTouches = 0;
        }
        _challengeTouches++;
        _challengeLastTouchMs = nowMs;

        // Held long enough AND touched more than once: the turnover is real. Adopt it and let this sample
        // through untouched, so the cue moves on the confirming sample rather than a further one later. The
        // second touch is what a deflection cannot produce, and is the whole reason the dwell can afford to
        // tolerate the loose samples in between.
        if (nowMs - _challengeStartedMs >= dwellMs && _challengeTouches >= 2)
        {
            _holder = claiming;
            DropChallenge();
            return (reference, opposition);
        }

        // Unconfirmed: sustain the incumbent and silence the challenger.
        return claiming == PossessionHolder.Reference
            ? (CueControl.HardLoss, Sustain(opposition))
            : (Sustain(reference), CueControl.HardLoss);
    }

    /// <summary>
    /// The incumbent's control during an unconfirmed challenge. A hard loss is downgraded to a soft one so
    /// the streamer's grace keeps the tick alive; anything else already describes a ball this team has not
    /// lost outright and is left alone.
    /// </summary>
    private static CueControl Sustain(CueControl incumbent)
        => incumbent == CueControl.HardLoss ? CueControl.SoftLoss : incumbent;
}
