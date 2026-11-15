namespace FM27Access.Plugin.Server;

/// <summary>
/// Who a detected ball touch belonged to, as judged by the nearest player to the touch point.
/// <see cref="Ambiguous"/> is both teams close enough that naming one would invent a distinction the
/// readings cannot support; <see cref="Unknown"/> is no player near enough to call it anyone's touch at
/// all (or no player objects to ask, between highlights). Both hold the incumbent, but only an
/// ambiguous touch counts towards a scramble: an unknown one is almost always a bounce with nobody near
/// (measured live, 83 of 84 had no player within 2.5 m), which says nothing about a pile-up.
/// </summary>
public enum TouchAttribution { Reference, Opposition, Ambiguous, Unknown }

/// <summary>
/// The last-touch possession judgement: the team holding the ball is the team that last took it with
/// intent. This is the STANDARD driver of the possession tick cues (default since 2026-09-01, after
/// grading and ear-testing better than the geometric route). <see cref="PossessionArbiter"/> is the
/// legacy fallback, kept running for the trace and reachable live via /posmode?touch=0 — where the
/// arbiter weighs a geometric guess (nearest man within reach, sampled blind between touches) with a
/// dwell, this tracker consumes actual touch events from the engine's animation layer (velocity
/// discontinuities, see TouchSampler).
///
/// A SINGLE opposing touch is deliberately not a turnover. In football a block, a deflection, or a
/// stretched toe-poke that lands straight back with the attackers leaves possession where it was — and
/// the first live version, which flipped on every attributed touch, audibly called each failed
/// clearance a turnover. So an opposing touch opens a pending CHALLENGE, and the flip needs that
/// team's next touch (any player on it) to confirm: an interceptor's dribble or a clearance reaching a
/// teammate confirms within a touch, while a deflection — one touch and no follow-up — never can. A
/// touch by the holding team cancels the challenge (they never lost it); a challenge whose touches are
/// further apart than the lapse restarts rather than confirming off ancient evidence. This is the same
/// two-touch insight the arbiter earned on geometric data, applied to real touches where it confirms
/// in well under a second.
///
/// The exception is a DECISIVE touch (the toucher right on the ball, the other team clearly further
/// away — judged by TouchSampler): it flips at once, because waiting for a second touch costs ~0.9 s on
/// a turnover nobody could doubt. A block or deflection looks just as decisive, so the flip is
/// PROVISIONAL for a short window: the team that lost the ball touching it again inside the window
/// takes it straight back on that one touch. The new holder's own next touch (or the window lapsing)
/// makes the flip final.
///
/// Restarts and engine turnover events are authority, not evidence: the engine names the team
/// outright, so they override whatever the touch stream last said. A restart also matters because the
/// touch window empties across between-highlight skips while the event scans cover the skipped span —
/// the restart is often the only signal that survives the gap. While the ball is dead the pending
/// <see cref="Restarter"/> is exposed so the cue can tick the team about to take the restart.
///
/// <see cref="Reset"/> forfeits everything: a gap we cannot see into (a skip, a stalled feed, leaving
/// the match) says nothing about who has the ball on the far side, so the ticks fall honestly silent
/// until the first fresh evidence — the same doctrine as the arbiter's None/None gate.
///
/// Lives in the reloadable Plugin, dependency-free, for the same reason as <see cref="PossessionArbiter"/>:
/// the rules get tuned against live football over /reload, and FM27Access.Tests compiles this source
/// file directly (see the Compile Include in its csproj). Keep it free of Unity/IL2CPP usings — the
/// test build breaking is the intended alarm. Not thread-safe by itself: TouchPossession owns the lock.
/// </summary>
public sealed class LastTouchTracker
{
    private PossessionHolder _holder = PossessionHolder.None;
    private PossessionHolder _restarter = PossessionHolder.None;
    // The pending challenge: the non-holding team's isolated touch, waiting for its confirming second.
    private PossessionHolder _challenger = PossessionHolder.None;
    private long _challengerTouchMs;
    // Consecutive touches nobody could be named for. A scramble is exactly this run growing, and past
    // the expiry the incumbent's claim is speculation — the pre-scramble team ticking through a pile-up
    // it may well have lost the ball in, which is the stuck-holder defect heard live.
    private int _unclearRun;
    // A decisive flip still open to reversal: the team it was taken from, and until when.
    private PossessionHolder _provisionalFrom = PossessionHolder.None;
    private long _provisionalUntilMs;
    private int _decisiveFlips;
    private int _decisiveReverts;
    private long _lastEvidenceMs;
    private int _ambiguousHeld;
    private int _unattributedHeld;
    private int _attributedTouches;
    private int _scrambleExpiries;

    /// <summary>The team that last unambiguously touched the ball (or was named by authority).</summary>
    public PossessionHolder Holder => _holder;

    /// <summary>
    /// The team about to take a restart while the ball is dead; <see cref="PossessionHolder.None"/> once
    /// any touch confirms the ball back in play.
    /// </summary>
    public PossessionHolder Restarter => _restarter;

    /// <summary>The team with a pending, unconfirmed challenge; None when there is none.</summary>
    public PossessionHolder Challenger => _challenger;

    /// <summary>When the last piece of evidence (touch or authority event) arrived. Monotonic ms.</summary>
    public long LastEvidenceMs => _lastEvidenceMs;

    /// <summary>Touches held because both teams were too close to call. Trace diagnostics.</summary>
    public int AmbiguousHeld => _ambiguousHeld;

    /// <summary>Touches held because nobody was near enough to name (or no players). Trace diagnostics.</summary>
    public int UnattributedHeld => _unattributedHeld;

    /// <summary>Touches that named a team. Trace diagnostics.</summary>
    public int AttributedTouches => _attributedTouches;

    /// <summary>Claims forfeited to a scramble (the unclear-touch run reaching expiry). Diagnostics.</summary>
    public int ScrambleExpiries => _scrambleExpiries;

    /// <summary>Turnovers taken on a single decisive touch. Diagnostics.</summary>
    public int DecisiveFlips => _decisiveFlips;

    /// <summary>Decisive flips reversed by the losing team inside the window (blocks). Diagnostics.</summary>
    public int DecisiveReverts => _decisiveReverts;

    /// <summary>The team a still-reversible decisive flip was taken from; None when there is none.</summary>
    public PossessionHolder ProvisionalFrom => _provisionalFrom;

    /// <summary>
    /// One detected ball touch. A touch by the holding team keeps the ball, kills any challenge and makes
    /// a provisional flip final; a touch by the other team opens a challenge that its NEXT touch (within
    /// <paramref name="challengeLapseMs"/>) confirms into a flip — one isolated touch, a deflection or
    /// a block, can never take the ball — unless it is <paramref name="decisive"/>, which flips at once
    /// but provisionally: the losing team touching again within <paramref name="provisionalMs"/> takes
    /// the ball straight back. Any attributed touch consumes a pending restart, because a nameable touch
    /// means the ball is back in play. An ambiguous touch holds the incumbent silently — but
    /// <paramref name="scrambleExpireTouches"/> of them in a row forfeit the claim entirely: a run that
    /// long is a scramble, and ticking the pre-scramble team through it is the one way this system was
    /// heard lying. An unknown touch is neutral: it holds the incumbent and neither builds nor breaks the
    /// run (a ball loose with nobody near is the far-loose mute's case, not a scramble).
    /// </summary>
    public void ObserveTouch(TouchAttribution who, long nowMs, bool decisive = false,
                             long challengeLapseMs = 2500, int scrambleExpireTouches = 3,
                             long provisionalMs = 1000)
    {
        _lastEvidenceMs = nowMs;
        if (who == TouchAttribution.Unknown)
        {
            _unattributedHeld++;
            return;
        }
        if (who == TouchAttribution.Ambiguous)
        {
            _ambiguousHeld++;
            // The challenge deliberately survives unclear touches (an interceptor's win is often
            // followed by a rebound off nobody-in-particular); only the holder touching, a flip, or
            // the lapse ends it.
            if (++_unclearRun >= scrambleExpireTouches && _holder != PossessionHolder.None)
            {
                _scrambleExpiries++;
                Reset();
            }
            return;
        }

        _attributedTouches++;
        _unclearRun = 0;
        _restarter = PossessionHolder.None;
        var team = who == TouchAttribution.Reference ? PossessionHolder.Reference : PossessionHolder.Opposition;

        // An unowned ball is taken on the first touch: there is nothing to steal, so the two-touch
        // guard would only buy silence (the arbiter learned this the expensive way).
        if (_holder == PossessionHolder.None)
        {
            _holder = team;
            ClearChallenge();
            ClearProvisional();
            return;
        }
        if (team == _holder)
        {
            ClearChallenge();     // the holder never lost it
            ClearProvisional();   // the new holder's own touch makes a decisive flip final
            return;
        }
        // The team a decisive flip was just taken from, touching again inside the window: it was a
        // block or a deflection, and the ball goes straight back on this one touch.
        if (_provisionalFrom == team && nowMs <= _provisionalUntilMs)
        {
            _holder = team;
            _decisiveReverts++;
            ClearChallenge();
            ClearProvisional();
            return;
        }
        // The other team on the ball: their second touch inside the lapse confirms the turnover; a
        // first (or stale) touch only opens/restarts the challenge and the incumbent keeps ticking.
        if (_challenger == team && nowMs - _challengerTouchMs <= challengeLapseMs)
        {
            _holder = team;
            ClearChallenge();
            ClearProvisional();
            return;
        }
        if (decisive)
        {
            _provisionalFrom = _holder;
            _provisionalUntilMs = nowMs + provisionalMs;
            _holder = team;
            _decisiveFlips++;
            ClearChallenge();
            return;
        }
        _challenger = team;
        _challengerTouchMs = nowMs;
    }

    /// <summary>
    /// A geometric on-the-ball read (the cue pipeline's per-sample InControl: a player of
    /// <paramref name="forRef"/>'s team within reach of the ball, on the ground, uncontested). Not a
    /// touch — it neither opens challenges nor takes an unowned ball, because that is exactly the
    /// proximity guessing this system exists to replace. It does two narrow jobs the touch stream can
    /// miss: as the CONFIRMING evidence for a pending challenge (a clean interception flips even when
    /// the winner's second touch goes undetected or unnameable — the geometry seeing him on the ball
    /// is the second witness), and as the holder's proof it never lost the ball (cancelling a
    /// challenge, mirroring the holder-touch cancel).
    /// </summary>
    public void ObserveGeometricControl(bool forRef, long nowMs, long challengeLapseMs = 2500)
    {
        var team = forRef ? PossessionHolder.Reference : PossessionHolder.Opposition;
        if (team == _holder)
        {
            ClearChallenge();
            ClearProvisional();   // the new holder seen on the ball: the decisive flip stands
            return;
        }
        // The team a decisive flip was taken from, seen back on the ball inside the window: the same
        // block-recovered case as their touch, witnessed geometrically.
        if (_provisionalFrom == team && nowMs <= _provisionalUntilMs)
        {
            _lastEvidenceMs = nowMs;
            _holder = team;
            _decisiveReverts++;
            ClearChallenge();
            ClearProvisional();
            return;
        }
        if (_challenger != team) return;
        if (nowMs - _challengerTouchMs > challengeLapseMs)
        {
            ClearChallenge();   // stale challenge: ancient touch evidence must not combine with now
            return;
        }
        _lastEvidenceMs = nowMs;
        _holder = team;
        _restarter = PossessionHolder.None;
        _unclearRun = 0;
        ClearChallenge();
    }

    private void ClearChallenge()
    {
        _challenger = PossessionHolder.None;
        _challengerTouchMs = 0;
    }

    private void ClearProvisional()
    {
        _provisionalFrom = PossessionHolder.None;
        _provisionalUntilMs = 0;
    }

    /// <summary>
    /// The engine named a restarting team (throw-in, corner, goal kick — see ScanRestarts). The
    /// restarting side owns the ball by rule, so it takes the holder too; the pending restarter stays
    /// exposed until a touch puts the ball back in play. Idempotent, so the multi-stream SSE loops all
    /// feeding the same event costs nothing.
    /// </summary>
    public void ObserveRestart(bool forRef, long nowMs)
    {
        _lastEvidenceMs = nowMs;
        var team = forRef ? PossessionHolder.Reference : PossessionHolder.Opposition;
        _holder = team;
        _restarter = team;
        ClearChallenge();
        ClearProvisional();
        _unclearRun = 0;
    }

    /// <summary>
    /// The engine narrated a turnover (see TurnoverClaim / ScanTurnovers). Authority outranks the touch
    /// stream, mirroring <see cref="PossessionArbiter.Concede"/>: the engine KNOWS who won the ball,
    /// so there is nothing to weigh. Idempotent for the same reason as restarts.
    /// </summary>
    public void ObserveEngineTurnover(bool forRef, long nowMs)
    {
        _lastEvidenceMs = nowMs;
        _holder = forRef ? PossessionHolder.Reference : PossessionHolder.Opposition;
        ClearChallenge();
        ClearProvisional();
        _unclearRun = 0;
    }

    /// <summary>
    /// Forfeit the claim: a window reset (skip/rewind), a stalled feed, or leaving the match. Counters
    /// survive — they describe the whole trace session, not one claim.
    /// </summary>
    public void Reset()
    {
        _holder = PossessionHolder.None;
        _restarter = PossessionHolder.None;
        ClearChallenge();
        ClearProvisional();
        _unclearRun = 0;
    }
}
