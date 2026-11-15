using System;
using UnityEngine;
using FM27Access.Speech;

namespace FM27Access.Host;

/// <summary>
/// The one IL2CPP-injected MonoBehaviour. Injection cannot be undone or re-run in a live process,
/// so this type is permanent (host-owned) and stays trivial: every frame it drains the host pump
/// (where a reload runs) and forwards to the current core tick. All the real per-frame logic lives
/// in core's CoreLoop, which is swapped wholesale on reload. See docs/hot-reload-plan.md.
/// </summary>
public class HostBehaviour : MonoBehaviour
{
    // Required IL2CPP constructor — ClassInjector passes the native object pointer.
    public HostBehaviour(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        HostDispatcher.Drain();

        try { HostBridge.Instance?.Tick?.Invoke(); }
        catch (Exception ex)
        {
            // The tick is core code; a throw here must not kill the host (which owns reload).
            // Core's own boundaries already Kill() on speech-affecting failures; this is the last
            // net so the host keeps draining and a /reload can still recover.
            Log.Error($"[HostBehaviour] Core tick threw: {ex}");
        }
    }
}
