using System;
using System.Threading;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Stops the surface re-running a compose that has just proved it cannot finish in time.
///
/// A compose walks the whole panel set on the main thread. When that walk outruns the compose
/// timeout the request throws, the page renders the busy state, and the companion — doing exactly
/// what it should — asks again. Nothing between those two attempts is different, so the second walk
/// costs the same main thread as the first and fails the same way. Measured live on a 290-tie cup
/// round: ~11s of main thread per attempt, back to back, with the game frozen throughout and the
/// player's page stuck on "The accessibility mod is busy." forever. The compose was slow; the
/// livelock was this missing memory.
///
/// So: once a compose has failed on time, further composes are refused WITHOUT dispatching until
/// something changes. Two things reopen it —
/// <list type="bullet">
/// <item>a panel load (<see cref="Reset"/>), because the screen the walk choked on is gone; and</item>
/// <item><see cref="CooldownMs"/> elapsing, which allows exactly one more attempt.</item>
/// </list>
/// The cooldown is what keeps this from being a trap: a screen that was slow once (a heavy load, a
/// sim finishing under the walk) gets tried again rather than being written off, and a screen that
/// is genuinely too slow costs one attempt per cooldown instead of one per refresh.
///
/// The refusal is not silence. The player gets the same honest busy page the timeout produced, and
/// every open/refuse/reopen is logged for the dev driver.
/// </summary>
internal static class ComposeBreaker
{
    /// <summary>How long a refusal holds before one more attempt is allowed through.</summary>
    internal const long CooldownMs = 15000;

    // TickCount64 of the failure that opened the breaker, or 0 when it is closed.
    private static long _openedMs;
    // Suppresses the log line for every refusal after the first of an open period: a refused
    // compose is cheap and the companion retries freely, so logging each one buries the open.
    private static int _refusalsLogged;

    /// <summary>
    /// Whether a compose may run. Call once per compose attempt: a call that returns true on the
    /// cooldown edge consumes that edge, so concurrent requests cannot all slip through it.
    /// </summary>
    internal static bool ShouldAttempt()
    {
        long opened = Interlocked.Read(ref _openedMs);
        if (opened == 0) return true;

        if (Environment.TickCount64 - opened < CooldownMs)
        {
            if (Interlocked.Increment(ref _refusalsLogged) == 1)
                Degrade.Note($"compose refused: the last compose of this screen ran out of time; "
                           + $"retrying in {CooldownMs / 1000}s or on the next screen");
            return false;
        }

        // The cooldown is spent: let exactly one attempt through by closing the breaker. It reopens
        // on the next failure, so a permanently slow screen settles into one attempt per cooldown.
        if (Interlocked.CompareExchange(ref _openedMs, 0, opened) != opened) return false;
        Log.Info("[ComposeBreaker] cooldown elapsed, allowing one compose attempt");
        Volatile.Write(ref _refusalsLogged, 0);
        return true;
    }

    /// <summary>A compose ran out of time (or was abandoned mid-walk). Refuse the next ones.</summary>
    internal static void NoteTimedOut()
    {
        if (Interlocked.Exchange(ref _openedMs, Environment.TickCount64) == 0)
        {
            Volatile.Write(ref _refusalsLogged, 0);
            Log.Warn($"[ComposeBreaker] compose timed out; refusing further composes for {CooldownMs}ms "
                   + "so the retry does not hold the main thread again");
        }
    }

    /// <summary>A compose finished. Any memory of a slow screen is stale.</summary>
    internal static void NoteSucceeded() => Reset();

    /// <summary>
    /// The screen changed, so whatever the breaker was holding against no longer applies. Called
    /// from the panel-load patch.
    /// </summary>
    internal static void Reset()
    {
        if (Interlocked.Exchange(ref _openedMs, 0) != 0)
            Log.Info("[ComposeBreaker] screen changed, composes allowed again");
        Volatile.Write(ref _refusalsLogged, 0);
    }
}
