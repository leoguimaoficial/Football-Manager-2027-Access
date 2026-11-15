namespace FM27Access.Plugin.Patches;

/// <summary>
/// The mod's share of the time spent inside a game callback.
///
/// A patch body runs on the main thread, inside the game's own call, outside both the core's tick and
/// the dispatcher's drain. So a slow one stops frames in exactly the way a stalled game does, and the
/// surface then reports "the game is not responding" about time the mod itself spent. Timing the
/// patches that fire on a busy screen is what makes that claim checkable rather than assumed — the
/// same reason <see cref="PanelLoadedPatch"/> times its own postfix.
///
/// Quiet below the threshold: a normal callback should never say anything.
/// </summary>
internal static class PatchTiming
{
    private const long SlowMs = 250;

    public static void Run(string what, Action body)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try { body(); }
        finally
        {
            long ms = watch.ElapsedMilliseconds;
            if (ms >= SlowMs)
                Speech.Log.Info($"[{what}] SLOW: took {ms}ms in the mod");
        }
    }
}
