using System;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The thread boundary around the last-touch possession judgement: the per-frame
/// <see cref="TouchSampler"/> (main thread) writes evidence in, the SSE loop (its own thread) reads
/// the holder out, and this class owns the lock between them. <see cref="Enabled"/> selects which
/// system drives the tick cues — this one (the standard, on by default) or the legacy geometric
/// arbiter; both keep recording to the trace either way.
///
/// The heartbeat is the honesty gate: the sampler stamps it every frame it actually ran, and
/// <see cref="Snapshot"/> reports stale once it stops (a stalled main thread, a dead hook). The SSE
/// loop then degrades the touch-mode cues to silence rather than ticking a claim nobody is
/// maintaining. Never hold this lock together with the SSE loop's PossessionGate — the SSE side
/// feeds this class only after releasing its own lock.
/// </summary>
internal static class TouchPossession
{
    /// <summary>
    /// Whether the last-touch system drives the tick cues. ON by default — it graded and ear-tested
    /// better than the geometric arbiter (2026-09-01) — with GET /posmode?touch=0 as the live escape
    /// hatch back to the arbiter. Static, so a restart or /reload returns to this default.
    /// </summary>
    internal static volatile bool Enabled = true;

    /// <summary>
    /// How long the sampler may go silent before the holder is untrustworthy: four SSE sub-ticks
    /// without a heartbeat means the main thread is stalled or the hook is gone, and a claim nobody
    /// is updating must not keep ticking.
    /// </summary>
    private const long StaleAfterMs = 500;

    private static readonly object Gate = new();
    private static readonly LastTouchTracker Tracker = new();
    // The replay pass's own claim. A replay re-shows a passage already played, so its possession is a
    // separate question from the live one: the live claim describes the field the match has reached,
    // the replay's describes what is on screen. Keeping them apart is what lets the ticks follow a
    // replay without a replayed touch ever moving the live claim — the reason the sampler used to skip
    // replays outright. Opened empty at every replay (and every camera angle within one), so it holds
    // no claim until the replay itself produces evidence, and the ticks stay honestly silent until then.
    private static readonly LastTouchTracker ReplayTracker = new();
    private static bool _inReplayPass;
    private static long _heartbeatMs;

    // The claim every feed writes to and Snapshot reads from: the replay's while one is showing.
    private static LastTouchTracker Active => _inReplayPass ? ReplayTracker : Tracker;

    /// <summary>Sampler liveness stamp; called once per frame the sampler actually ran.</summary>
    internal static void Heartbeat(long nowMs)
    {
        lock (Gate) _heartbeatMs = nowMs;
    }

    /// <summary>
    /// Open or close the replay pass, from the sampler's per-frame read of the playback mode.
    /// <paramref name="fresh"/> starts a new pass over the same replay — a camera angle rewinding to
    /// show the move again — and clears the replay claim with it, because the evidence that built it
    /// belongs to ground the new angle is about to re-cover. The live claim is never touched here: it
    /// is still true of the field the match has actually reached, and resumes intact.
    /// </summary>
    internal static void SetReplayPass(bool inReplay, bool fresh = false)
    {
        lock (Gate)
        {
            if (inReplay && (!_inReplayPass || fresh)) ReplayTracker.Reset();
#if FM27_DEVELOPER_TOOLS
            if (inReplay != _inReplayPass || (inReplay && fresh))
                Dev.TouchJournal.Record(inReplay ? (fresh ? "replay pass (rewound)" : "replay pass") : "live pass");
#endif
            _inReplayPass = inReplay;
        }
    }

    /// <summary>One touch, journalled with its attribution geometry and the judgement it produced.</summary>
    internal static void NoteTouch(TouchAttribution who, bool decisive, string detail, long nowMs)
    {
        lock (Gate)
        {
#if FM27_DEVELOPER_TOOLS
            var (holder, challenger) = (Active.Holder, Active.Challenger);
#endif
            Active.ObserveTouch(who, nowMs, decisive);
#if FM27_DEVELOPER_TOOLS
            Dev.TouchJournal.Record($"touch {who}{(decisive ? " decisive" : "")} {detail} | {Judgement(holder, challenger)}");
#endif
        }
    }

    internal static void NoteRestart(bool forRef, long nowMs)
    {
        lock (Gate)
        {
#if FM27_DEVELOPER_TOOLS
            var (holder, restarter) = (Active.Holder, Active.Restarter);
#endif
            Active.ObserveRestart(forRef, nowMs);
#if FM27_DEVELOPER_TOOLS
            if (Active.Restarter != restarter || Active.Holder != holder)
                Dev.TouchJournal.Record($"restart {(forRef ? "ref" : "opp")} | {Judgement(holder, Active.Challenger)}");
#endif
        }
    }

    internal static void NoteEngineTurnover(bool forRef, long nowMs)
    {
        lock (Gate)
        {
#if FM27_DEVELOPER_TOOLS
            var holder = Active.Holder;
#endif
            Active.ObserveEngineTurnover(forRef, nowMs);
#if FM27_DEVELOPER_TOOLS
            if (Active.Holder != holder)
                Dev.TouchJournal.Record($"engine turnover {(forRef ? "ref" : "opp")} | {Judgement(holder, Active.Challenger)}");
#endif
        }
    }

    internal static void NoteGeometricControl(bool forRef, long nowMs)
    {
        lock (Gate)
        {
#if FM27_DEVELOPER_TOOLS
            var (holder, challenger) = (Active.Holder, Active.Challenger);
#endif
            Active.ObserveGeometricControl(forRef, nowMs);
#if FM27_DEVELOPER_TOOLS
            // Called every SSE sample, so only a sample that moved the judgement is journalled.
            if (Active.Holder != holder || Active.Challenger != challenger)
                Dev.TouchJournal.Record($"geometric {(forRef ? "ref" : "opp")} | {Judgement(holder, challenger)}");
#endif
        }
    }

    // "holder ref->opp, challenge none" — the judgement before and after one piece of evidence.
    // Called under Gate, after the evidence has been applied.
#if FM27_DEVELOPER_TOOLS
    private static string Judgement(PossessionHolder holderBefore, PossessionHolder challengerBefore)
    {
        var t = Active;
        string h = holderBefore == t.Holder
            ? Dev.TouchJournal.Name(t.Holder)
            : Dev.TouchJournal.Name(holderBefore) + "->" + Dev.TouchJournal.Name(t.Holder);
        string c = challengerBefore == t.Challenger
            ? Dev.TouchJournal.Name(t.Challenger)
            : Dev.TouchJournal.Name(challengerBefore) + "->" + Dev.TouchJournal.Name(t.Challenger);
        string p = t.ProvisionalFrom == PossessionHolder.None ? "" : ", provisional";
        return $"holder {h}, challenge {c}{p}{(_inReplayPass ? " (replay)" : "")}";
    }
#endif

    /// <summary>
    /// Forfeit the claim. Both, deliberately: the callers are the gaps nothing can be inferred across —
    /// a fast-forward, a match ending, a goal — after which neither pass's evidence describes anything
    /// on screen. Which pass is open is left alone: that belongs to the sampler, which owns it from the
    /// frame loop, and a caller on another thread must not be able to redirect the feeds mid-replay.
    /// </summary>
    internal static void Reset(string reason)
    {
        lock (Gate)
        {
#if FM27_DEVELOPER_TOOLS
            if (Tracker.Holder != PossessionHolder.None || ReplayTracker.Holder != PossessionHolder.None)
                Dev.TouchJournal.Record("reset: " + reason);
#endif
            Tracker.Reset();
            ReplayTracker.Reset();
        }
    }

    /// <summary>
    /// The current judgement for the SSE loop: the holder, any pending restarter (the team about to
    /// take a dead-ball restart), and whether the sampler is alive enough for either to describe now.
    /// </summary>
    internal static (PossessionHolder Holder, PossessionHolder Restarter, bool Fresh) Snapshot(long nowMs)
    {
        lock (Gate)
        {
            var t = Active;
            return (t.Holder, t.Restarter, nowMs - _heartbeatMs <= StaleAfterMs);
        }
    }

    /// <summary>Decisive-flip counters for /posmode: single-touch turnovers, and those reversed as blocks.</summary>
    internal static (int Flips, int Reverts) DecisiveStats()
    {
        lock (Gate) return (Tracker.DecisiveFlips, Tracker.DecisiveReverts);
    }

    /// <summary>Diagnostic counters for /posmode and the trace report.</summary>
    internal static (int Attributed, int Ambiguous, int Unattributed, int ScrambleExpiries,
                     long LastEvidenceMs, long HeartbeatMs) Stats()
    {
        lock (Gate)
            return (Tracker.AttributedTouches, Tracker.AmbiguousHeld, Tracker.UnattributedHeld,
                    Tracker.ScrambleExpiries, Tracker.LastEvidenceMs, _heartbeatMs);
    }
}
