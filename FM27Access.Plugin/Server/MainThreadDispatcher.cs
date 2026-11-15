using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using FM27Access.Navigation;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Marshals work from background threads (the accessibility server) onto the Unity
/// main thread. Unity/IL2CPP objects must only be touched on the main thread, so the
/// server thread hands snapshot-building and activation work here and blocks for the
/// result. <see cref="Drain"/> is called every frame from the core's tick.
///
/// The hand-off only works while something is draining it, and three things stop that: the core
/// shutting itself down (Kill pauses the tick on purpose), the tick throwing before it reaches the
/// drain, and the game no longer running frames at all. This class records enough to tell those
/// apart — see <see cref="Health"/> and <see cref="SurfaceHealth"/> — so callers can answer honestly
/// instead of waiting out a budget and reporting a timeout the player cannot act on.
/// </summary>
internal static class MainThreadDispatcher
{
    private readonly struct Work
    {
        public Work(string label, Action action, Ticket ticket, Action reject) { Label = label; Action = action; Ticket = ticket; Reject = reject; }
        public string Label { get; }
        public Action Action { get; }
        public Action Reject { get; }
        /// <summary>The caller's ticket, so a long action can see that the caller gave up. Null for none.</summary>
        public Ticket Ticket { get; }
    }

    private static readonly ConcurrentQueue<Work> _queue = new();
    private static readonly object QueueGate = new();

    /// <summary>An action holding the main thread this long is a fault worth naming, not a slow frame.</summary>
    private const long SlowActionMs = 250;

    /// <summary>
    /// How much of a frame the drain may spend before handing the rest of the queue to the next one.
    ///
    /// Without this the drain empties the whole queue every frame, so a caller that splits its work
    /// into small units to be kind to the frame gains nothing: it unblocks the instant its unit
    /// finishes and can queue the next one while the drain is still looping, and they run
    /// back-to-back inside the same frame. That is what turned the streamed table harvest into a
    /// half-second stall (see HarvestSlicer).
    ///
    /// The budget cannot preempt a unit already running, so the real bound is this plus the largest
    /// single unit. Deliberately kept ABOVE the slice budget of the batch callers rather than below
    /// it: below, a slice loop would monopolise a frame each and starve every cheap read queued
    /// behind it, and the queue is FIFO with no priority.
    /// </summary>
    private const long DrainBudgetMs = 45;

    /// <summary>
    /// True once the game is interactive and the tick is draining the queue. Background request
    /// threads check <see cref="Health"/> before calling Invoke — calling it earlier would block
    /// until timeout because nothing drains the queue during loading.
    /// </summary>
    public static volatile bool Ready;

    /// <summary>
    /// Set when the core stops on purpose (a Kill pausing the tick, which leaves the server up so
    /// /reload can recover). From then on nothing drains, so this is the difference between a mod
    /// that shut down and a game that stopped — a certainty rather than an inference.
    /// </summary>
    private static volatile bool _stopped;
    public static bool Stopped
    {
        get => _stopped;
        set
        {
            lock (QueueGate)
            {
                _stopped = value;
                if (!value) return;
                // No further Unity frame will drain this core. Release waiting
                // callers and their captured native references immediately,
                // without ever running an undispatched game command. The same
                // gate prevents a racing producer from enqueueing after Stop.
                while (_queue.TryDequeue(out var work)) work.Reject();
                Volatile.Write(ref _deferredDepth, 0);
            }
        }
    }

    private static long _lastDrainMs;
    private static long _lastTickFailureMs;

    // Attribution. A stalled hand-off says only that the frame stopped coming back; these say where
    // it stopped. The drain stamps entry and exit separately because a queued action that runs for
    // 25s and a game that stopped running frames are, from the entry stamp alone, the same reading.
    private static long _lastDrainEndMs;
    private static long _actionStartedMs;
    private static volatile string _actionLabel;
    private static long _slowestActionMs;
    private static volatile string _slowestActionLabel;
    private static long _tickStartedMs;
    // Work the drain left for the next frame because its budget ran out. Without this a caller that
    // times out waiting behind the budget reads as a healthy surface — the drain ran, nothing is
    // holding the main thread — and the surface would tell the player something that did not happen.
    private static long _deferredDepth;

    /// <summary>The core's per-frame tick has entered. Cleared by <see cref="NoteTickExit"/>.</summary>
    public static void NoteTickEnter() => Volatile.Write(ref _tickStartedMs, Environment.TickCount64);

    /// <summary>The core's per-frame tick has returned, however it returned.</summary>
    public static void NoteTickExit() => Volatile.Write(ref _tickStartedMs, 0);

    /// <summary>
    /// How long the mod has been holding the Unity main thread inside its own work, or
    /// <see cref="SurfaceHealth.NotHolding"/> when it is inside none.
    ///
    /// This is the reading that separates "the mod is stalling the game" from "the game stopped": both
    /// leave the drain stale, and only this one says the main thread is in the mod's hands. It covers
    /// the tick as well as dispatched actions because the tick runs ahead of the drain, so a slow tick
    /// starves the hand-off just as a slow action does.
    /// </summary>
    public static long MsHoldingMainThread()
    {
        long now = Environment.TickCount64;
        long action = Volatile.Read(ref _actionStartedMs);
        long tick = Volatile.Read(ref _tickStartedMs);
        // The earliest of the two that is actually in flight — whichever entered first is the one that
        // has been holding the thread, and the other is nested inside it.
        long started = action == 0 ? tick : tick == 0 ? action : Math.Min(action, tick);
        return started == 0 ? SurfaceHealth.NotHolding : now - started;
    }

    // The Unity main thread's id, stamped by the drain (which only ever runs there). Lets Invoke
    // recognise a caller that is ALREADY on the main thread and run it inline: queued, such a call
    // can never complete — the drain it waits for is the very frame it is blocking — so before this
    // it deadlocked until timeout (a measured 2s freeze per call). 0 until the first drain, which
    // keeps the check inert during boot.
    private static int _mainThreadId;

    // The ticket of the action currently running inside Drain, so work already under way can ask
    // whether its caller is still there. Main-thread only (written and read inside the drain loop),
    // so no synchronisation beyond the ticket's own volatile flag.
    private static Ticket _runningTicket;

    /// <summary>
    /// Thrown by <see cref="ThrowIfAbandoned"/> when the caller of the running action has already
    /// timed out. Distinct from <see cref="TimeoutException"/>: nobody is waiting for this, so it is
    /// a discarded result rather than a state to report to the player.
    /// </summary>
    internal sealed class AbandonedException : Exception
    {
        public AbandonedException() : base("The caller of this main-thread action timed out.") { }
    }

    /// <summary>
    /// Stop if the caller that queued the running action has given up on it. Long main-thread work
    /// (the panel walk, a large consolidated table) calls this at its own natural boundaries.
    ///
    /// Without it, an action only ever checks for cancellation BEFORE it starts: an 11s screen walk
    /// whose caller timed out at 10s still ran its remaining second — and every retry after it ran
    /// the full 11s again, holding the main thread continuously while the game appeared frozen.
    /// Cancelling between units bounds that to the caller's own timeout plus one unit.
    ///
    /// Inert off the main thread and inert for callers that never time out, so it is safe anywhere.
    /// </summary>
    public static void ThrowIfAbandoned()
    {
        if (_runningTicket is not { Cancelled: true }) return;
        // The caller's own timeout is already reported to it; this says the work it gave up on has
        // now actually stopped, which is the difference between a bounded stall and the frame the
        // game never got back. Logged at the abandonment, not by the drain: the exception is caught
        // by the dispatch wrapper and discarded (nobody is waiting for it), so this is its only trace.
        Log.Warn($"[MainThreadDispatcher] Action '{_actionLabel ?? "?"}' abandoned after "
               + $"{Environment.TickCount64 - Volatile.Read(ref _actionStartedMs)}ms: its caller timed out");
        throw new AbandonedException();
    }

    public static void Drain()
    {
        _mainThreadId = Environment.CurrentManagedThreadId;
        long drainStart = Environment.TickCount64;
        Volatile.Write(ref _lastDrainMs, drainStart);
        while (_queue.TryDequeue(out var work))
        {
            _actionLabel = work.Label;
            _runningTicket = work.Ticket;
            Volatile.Write(ref _actionStartedMs, Environment.TickCount64);
            try { work.Action(); }
            catch (Exception ex) { Log.Warn($"[MainThreadDispatcher] Action '{work.Label}' failed: {ex.Message}"); }
            finally
            {
                long elapsed = Environment.TickCount64 - Volatile.Read(ref _actionStartedMs);
                Volatile.Write(ref _actionStartedMs, 0);
                _actionLabel = null;
                _runningTicket = null;
                if (elapsed >= SlowActionMs)
                {
                    if (elapsed > Volatile.Read(ref _slowestActionMs))
                    {
                        Volatile.Write(ref _slowestActionMs, elapsed);
                        _slowestActionLabel = work.Label;
                    }
                    Log.Warn($"[MainThreadDispatcher] Action '{work.Label}' held the main thread for {elapsed}ms");
                }
            }
            // Checked after the item, never before, so a unit larger than the whole budget still
            // runs rather than never running. That is also the honest bound: the mod can hold a
            // frame for this budget plus one unit, not for this budget.
            if (Environment.TickCount64 - drainStart >= DrainBudgetMs)
            {
                Volatile.Write(ref _deferredDepth, _queue.Count);
                break;
            }
        }
        if (_queue.IsEmpty) Volatile.Write(ref _deferredDepth, 0);
        Volatile.Write(ref _lastDrainEndMs, Environment.TickCount64);
    }

    /// <summary>
    /// Which side is holding the main thread, for the diagnostic surface. The distinction the health
    /// state cannot make: an action still running inside the drain means the mod is the one blocking,
    /// a drain that completed and never came back means the frame did.
    /// </summary>
    public static string StallAttribution()
    {
        long now = Environment.TickCount64;
        long started = Volatile.Read(ref _actionStartedMs);
        long drainStart = Volatile.Read(ref _lastDrainMs);
        long drainEnd = Volatile.Read(ref _lastDrainEndMs);

        string running = started != 0
            ? $"IN-ACTION label={_actionLabel ?? "?"} forMs={now - started}"
            : drainStart > drainEnd
                ? $"IN-DRAIN forMs={now - drainStart}"
                : $"IDLE msSinceDrainEnd={(drainEnd == 0 ? -1 : now - drainEnd)}";

        long slowest = Volatile.Read(ref _slowestActionMs);
        return $"{running} slowestActionMs={slowest} slowestActionLabel={_slowestActionLabel ?? "-"} "
             + $"deferred={Volatile.Read(ref _deferredDepth)}";
    }

    /// <summary>Clears the high-water mark so a repro measures only itself.</summary>
    public static void ResetAttribution()
    {
        Volatile.Write(ref _slowestActionMs, 0);
        _slowestActionLabel = null;
    }

    /// <summary>
    /// The core's per-frame tick threw. Recorded because a stalled hand-off means something very
    /// different depending on whether frames are still running: a recent tick failure says the mod is
    /// what broke, silence says the game stopped.
    /// </summary>
    public static void NoteTickFailure() => Volatile.Write(ref _lastTickFailureMs, Environment.TickCount64);

    /// <summary>Whether the surface can read the game right now, and what to say if it cannot.</summary>
    public static SurfaceHealth.State Health()
    {
        long now = Environment.TickCount64;
        long drain = Volatile.Read(ref _lastDrainMs);
        long failure = Volatile.Read(ref _lastTickFailureMs);
        return SurfaceHealth.Evaluate(
            Ready,
            Stopped,
            drain == 0 ? SurfaceHealth.Never : now - drain,
            failure == 0 ? SurfaceHealth.Never : now - failure,
            MsHoldingMainThread());
    }

    /// <summary>
    /// What to tell the player about the surface right now, or "" when it is healthy. Computed here
    /// rather than at each caller so the duration tier (a stall long enough to justify advising a
    /// restart) is applied once and consistently.
    /// </summary>
    public static string HealthMessage()
        => SurfaceHealth.Message(Health(), SurfaceHealth.IsProlonged(MsSinceDrain()));

    /// <summary>Milliseconds since the last drain, or -1 if it has never run. Diagnostics only.</summary>
    public static long MsSinceDrain()
    {
        long drain = Volatile.Read(ref _lastDrainMs);
        return drain == 0 ? -1 : Environment.TickCount64 - drain;
    }

    /// <summary>Milliseconds since the tick last threw, or -1 if it never has. Diagnostics only.</summary>
    public static long MsSinceTickFailure()
    {
        long failure = Volatile.Read(ref _lastTickFailureMs);
        return failure == 0 ? -1 : Environment.TickCount64 - failure;
    }

    /// <summary>Run <paramref name="fn"/> on the main thread and return its result. Blocks the caller.</summary>
    public static T Invoke<T>(Func<T> fn, int timeoutMs = 2000,
                              [CallerMemberName] string caller = null, [CallerLineNumber] int line = 0)
    {
        if (Stopped) throw new InvalidOperationException("The accessibility mod has stopped.");
        // Already on the main thread (a dispatched action or the tick calling back in): run inline.
        // Attribution is left to the outer action's stamps — nesting them would zero the outer
        // action's start on this call's exit.
        if (_mainThreadId != 0 && Environment.CurrentManagedThreadId == _mainThreadId)
            return fn();

        var done = new ManualResetEventSlim(false);
        var ticket = new Ticket();
        T result = default;
        Exception error = null;

        var work = new Work($"{caller}:{line}", () =>
        {
            // If the caller already timed out, don't run — the action could otherwise
            // activate an element late or mutate shared render state after the fact.
            if (ticket.Cancelled) return;
            try
            {
                if (Stopped) throw new InvalidOperationException("The accessibility mod has stopped.");
                result = fn();
            }
            catch (Exception e) { error = e; }
            finally { done.Set(); }
        }, ticket, () =>
        {
            ticket.Cancelled = true;
            error = new InvalidOperationException("The accessibility mod has stopped.");
            done.Set();
        });
        lock (QueueGate)
        {
            if (Stopped) throw new InvalidOperationException("The accessibility mod has stopped.");
            _queue.Enqueue(work);
        }

        if (!done.Wait(timeoutMs))
        {
            ticket.Cancelled = true;
            // The message names the state the surface is actually in, so a caller that surfaces it
            // says something the player can act on rather than describing the dispatch mechanism.
            throw new TimeoutException(
                SurfaceHealth.TimeoutMessage(Health(), SurfaceHealth.IsProlonged(MsSinceDrain()),
                                             MsHoldingMainThread() != SurfaceHealth.NotHolding));
        }
        if (error != null)
            throw error;
        return result;
    }

    private sealed class Ticket { public volatile bool Cancelled; }
}
