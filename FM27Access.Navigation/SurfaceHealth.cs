namespace FM27Access.Navigation;

/// <summary>
/// Whether the companion surface can answer at all, and what to tell the player when it cannot.
///
/// The surface runs on background threads and hands every game read to the main thread. If nothing
/// is draining that hand-off, each read waits out its budget and fails — and the mod used to answer
/// with the raw timeout text ("Main-thread dispatch timed out (is the game updating?)"), spoken for
/// an action and rendered as the whole page for a screen read. That is jargon for a developer and
/// says nothing a player can act on; worse, it is identical whether the mod shut itself down, the
/// mod's per-frame loop is failing, or the game itself has stopped updating. Those need different
/// answers, and only the first is even about the game.
///
/// So the surface reports state instead of guessing: whether the core deliberately stopped (a Kill,
/// which is known, not inferred), whether its per-frame tick is throwing (so the tick runs but the
/// drain is being skipped), or whether nothing at all is happening (the game is not updating). Pure
/// so the rules are unit-testable without a game; the plugin feeds it live readings.
/// </summary>
public static class SurfaceHealth
{
    /// <summary>
    /// How long without a drain counts as stalled. Comfortably above the dispatch budget a caller
    /// would already have given up on, and above any legitimately long frame (a heavy load, a
    /// processing step), because calling a busy game "not responding" would be its own lie.
    /// </summary>
    public const long StallMs = 5000;

    /// <summary>
    /// How long a stall must last before the player is advised to restart. Telling someone to restart
    /// is advice with a cost — unsaved progress — so it waits until the evidence supports it. A tick
    /// that throws for a few seconds and recovers must not send anyone to the main menu; one still
    /// failing this long is not coming back on its own.
    /// </summary>
    public const long ProlongedMs = 30000;

    /// <summary>Never seen: pass this as the "ms since" for something that has not happened.</summary>
    public const long Never = long.MaxValue;

    /// <summary>
    /// Not holding the main thread at all. Deliberately zero rather than <see cref="Never"/>: the
    /// other readings here are "how long since X happened", where never means infinitely long ago, but
    /// this one is "how long has X been going on", where never means no time at all. Passing Never for
    /// it would clear the stall threshold and report every stall as the mod's fault.
    /// </summary>
    public const long NotHolding = 0;

    public enum State
    {
        /// <summary>The game is not interactive yet; nothing drains the hand-off by design.</summary>
        Loading,

        /// <summary>Draining normally. The only state in which the surface may read the game.</summary>
        Live,

        /// <summary>The core stopped itself (a foundation failure took it down). Known, not inferred.</summary>
        ModStopped,

        /// <summary>The core's per-frame tick is failing, so the hand-off is not being drained.</summary>
        ModFailing,

        /// <summary>
        /// The main thread is inside the mod's own work — a dispatched action or the per-frame tick —
        /// and has not come back. The frame is stopped, but the mod is what is stopping it.
        /// </summary>
        ModBusy,

        /// <summary>Nothing is running the frame at all — the game has stopped updating.</summary>
        GameNotResponding,
    }

    /// <summary>
    /// The live state. <paramref name="stopped"/> comes first because it is the one certainty here:
    /// the core recorded its own shutdown, so there is nothing to infer from timings.
    /// </summary>
    /// <param name="msHoldingMainThread">
    /// How long the mod has been inside its own main-thread work — a dispatched action or the
    /// per-frame tick — or <see cref="NotHolding"/> when it is inside neither.
    /// </param>
    public static State Evaluate(bool ready, bool stopped, long msSinceDrain, long msSinceTickFailure,
                                 long msHoldingMainThread = NotHolding)
    {
        if (stopped) return State.ModStopped;
        if (!ready) return State.Loading;
        if (msSinceDrain < StallMs) return State.Live;
        // Stalled — but a stalled drain alone cannot say which side stopped, because the drain stamps
        // its timestamp on entry and the tick runs ahead of it. Mod work that holds the main thread
        // therefore reads exactly like a game that stopped running frames. Asking directly whether the
        // mod is inside that work is the only reading that separates them, so it is asked first: a mod
        // that is demonstrably holding the thread is not evidence about the game at all.
        if (msHoldingMainThread >= StallMs) return State.ModBusy;
        // A tick that threw within the same window means the frame IS running and the mod is what is
        // broken; no tick failures and no mod work in flight means nothing is running the frame.
        return msSinceTickFailure < StallMs ? State.ModFailing : State.GameNotResponding;
    }

    /// <summary>Only a live surface may touch the game. Everything else answers instead of waiting.</summary>
    public static bool CanServe(State state) => state == State.Live;

    /// <summary>Whether a stall has lasted long enough to stop being a wait and start being a fault.</summary>
    public static bool IsProlonged(long msSinceDrain) => msSinceDrain >= ProlongedMs;

    /// <summary>
    /// What the player is told. Each names the side that stopped; only the states that have earned it
    /// prescribe a remedy, and a live surface has nothing to say because the screen speaks for itself.
    ///
    /// Two states deliberately withhold the restart advice:
    ///   - a mod whose tick is failing, or which is holding the main thread, may still recover, so it
    ///     is described, not prescribed for, until the stall is <see cref="IsProlonged"/>;
    ///   - a game that is not responding is NEVER told to restart, however long it takes. The game may
    ///     simply be busy — a 50s pause building a heavy screen was measured — and a restart at the
    ///     mod's suggestion could throw away the player's unsaved progress. Say what is true and let
    ///     them decide.
    /// A mod that has stopped is the one certainty: it does not come back on its own, and without it a
    /// blind player cannot use the game at all, so the restart is the honest next step.
    /// </summary>
    public static string Message(State state, bool prolonged = false) => state switch
    {
        State.Loading => "The game is still loading.",
        State.Live => "",
        State.ModStopped => "The accessibility mod has stopped. Restart the game.",
        State.ModFailing => prolonged
            ? "The accessibility mod is not responding. Restart the game."
            : "The accessibility mod is not responding.",
        State.ModBusy => prolonged
            ? "The accessibility mod is busy. Restart the game."
            : "The accessibility mod is busy.",
        State.GameNotResponding => "The game is not responding.",
        _ => "The accessibility mod is unavailable.",
    };

    /// <summary>
    /// What a read that timed out should say. A stalled surface names the side that stopped; a live
    /// one means this single read lost a race with a long frame, which is a wait, not a breakage.
    ///
    /// <paramref name="modHoldingMainThread"/> exists because a timeout is itself the earliest evidence
    /// of a hold — it arrives on the caller's budget, long before <see cref="StallMs"/> has elapsed, so
    /// the surface still reads Live. Without it a read that timed out because the mod was occupying the
    /// main thread reported "The game is busy.", which is the same false claim in a shorter window.
    /// </summary>
    public static string TimeoutMessage(State state, bool prolonged = false, bool modHoldingMainThread = false)
    {
        if (state != State.Live) return Message(state, prolonged);
        return modHoldingMainThread ? "The accessibility mod is busy." : "The game is busy.";
    }
}
