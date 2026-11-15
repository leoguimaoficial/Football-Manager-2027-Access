using System;
using FM27Access.Speech;

namespace FM27Access.Host;

/// <summary>
/// The seam between the permanent host (loaded once by BepInEx, owns the four resources that
/// cannot be re-initialized in a live IL2CPP process) and the reloadable core. Core receives an
/// <see cref="IHostBridge"/> in <c>CoreEntry.Mount</c> and talks to the host only through it, so
/// the host never holds a compile-time reference to core (it loads core by reflection, which is
/// what makes reload possible). See docs/hot-reload-plan.md.
/// </summary>
public interface IHostBridge
{
    /// <summary>
    /// Core's per-frame tick. The host's injected <c>HostBehaviour.Update</c> invokes this every
    /// frame. Core sets it in Mount and clears it in Unmount; between the two the mod is paused.
    /// </summary>
    Action Tick { get; set; }

    /// <summary>
    /// Core's <c>/reload</c> handler calls this to ask the host to reload core. The host runs the
    /// Unmount → load-fresh-bytes → Mount sequence on the Unity main thread (off the server
    /// threads that issued the request), so patching never races live game code.
    /// </summary>
    void RequestReload();

    /// <summary>Spoken-line ring buffer the dev driver reads back over <c>/speech</c>. Host-owned so it survives reloads.</summary>
    SpeechLog SpeechHistory { get; }

    /// <summary>Mod-log ring buffer the dev driver reads back over <c>/log</c>. Host-owned so it survives reloads.</summary>
    SpeechLog LogHistory { get; }

    /// <summary>
    /// False on a reload (the game is already loaded), true the first time core mounts this session.
    /// Lets the core loop skip the wait-for-load state machine and startup diagnostics on a reload.
    /// </summary>
    bool IsFirstMount { get; }

    /// <summary>Local HTTP session secret, owned by the permanent host and never logged.</summary>
    string CompanionSessionToken { get; }
}
