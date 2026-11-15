using System;
using System.Collections.Concurrent;
using FM27Access.Speech;

namespace FM27Access.Host;

/// <summary>
/// A host-owned main-thread work queue, drained every frame by <see cref="HostBehaviour"/>. It is
/// separate from core's <c>MainThreadDispatcher</c> on purpose: a reload tears core's dispatcher
/// down, so the reload itself (Unmount → load → Mount, which unpatches and re-patches Harmony)
/// must run on a pump that outlives core. Posting from a server thread and running here means
/// patching never races live game code on the main thread.
/// </summary>
internal static class HostDispatcher
{
    private static readonly ConcurrentQueue<Action> _queue = new();

    public static void Post(Action action) => _queue.Enqueue(action);

    public static void Drain()
    {
        while (_queue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { Log.Error($"[HostDispatcher] Action failed: {ex}"); }
        }
    }
}
