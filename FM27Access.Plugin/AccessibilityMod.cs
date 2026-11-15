using HarmonyLib;

namespace FM27Access.Plugin;

/// <summary>
/// The foundation-halt path of the "degrade in place, never lie" doctrine. Most failures degrade in
/// place (see <see cref="Speech.Degrade"/>) — they speak an honest gap marker and keep the mod
/// running. <see cref="Kill"/> is reserved for foundation failures where nothing the mod says can be
/// trusted: Prism/Speech init, Harmony patch application, the input system. It removes the Harmony
/// patches and pauses the core, but deliberately leaves the dev server up, so a halt stays
/// diagnosable over HTTP (/log, /health) and a fixed rebuild plus /reload recovers without
/// restarting the game. The injected behaviour is host-owned and is never destroyed here — only
/// core's per-frame tick is stopped. See docs/reference/failure-handling.md.
/// </summary>
public static class AccessibilityMod
{
    public static bool IsAlive { get; private set; }

    private static Harmony _harmony;
    private static Action _onKill;

    public static void Init(Harmony harmony, Action onKill)
    {
        _harmony = harmony;
        _onKill = onKill;
        IsAlive = true;
    }

    public static void Kill()
    {
        if (!IsAlive) return;
        IsAlive = false;

        Speech.Log.Fatal("Mod shutting down due to critical error.");

        try { Speech.SpeechPipeline.SpeakInterrupt("Accessibility mod error, shutting down"); }
        catch (Exception e) { Speech.Log.Warn($"Failed to speak shutdown message: {e}"); }

        try { _harmony?.UnpatchSelf(); }
        catch (Exception e) { Speech.Log.Error($"Failed to unpatch Harmony: {e}"); }

        // Pause the core (clear the tick, stop the server). Set by CoreEntry.Mount.
        try { _onKill?.Invoke(); }
        catch (Exception e) { Speech.Log.Error($"Failed to run kill teardown: {e}"); }

        _harmony = null;
        _onKill = null;
    }
}
