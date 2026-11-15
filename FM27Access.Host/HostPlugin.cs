using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using FM27Access.Speech;
// BasePlugin exposes its own Log (a BepInEx ManualLogSource), which shadows the mod's static
// Speech.Log inside this class. Alias the mod logger so the backend wiring below is unambiguous.
using ModLog = FM27Access.Speech.Log;

namespace FM27Access.Host;

/// <summary>
/// The permanent BepInEx entry point. Loaded once and never reloaded, it owns the four things that
/// cannot be re-initialized in a live IL2CPP process — the injected behaviour, the native Prism
/// load, the companion process, and Speech init — then loads the reloadable core through
/// <see cref="CoreHost"/>. Everything you iterate on (patches, walkers, adapters, HTML, dev
/// endpoints) lives in core and reloads via POST /reload. See docs/hot-reload-plan.md.
/// </summary>
[BepInPlugin("com.fm27access.host", "FM27 Accessibility Host", BuildProfile.Version)]
public class HostPlugin : BasePlugin
{
    public override void Load()
    {
        var result = NativeBuildGuard.Validate(BepInEx.Paths.GameRootPath,
            System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FM27Access", "native-build.json"));
        if (!result.Supported)
        {
            base.Log.LogError("FM27 Access was not initialized. " + result.Reason + " Build: " + result.Version);
            CompanionLauncher.Launch("FM27 Access could not verify a compatible game version. The mod has been disabled for this launch. Check for a compatible FM27 Access release or remove the mod using its installation receipt. Details: BepInEx/LogOutput.log.");
            return;
        }
        base.Log.LogInfo("FM27 Access compatibility verified: " + result.Version + ". " + result.Reason);
        InitializeValidatedGame();
    }

    // Keep IL2CPP resolution/injection out of the preflight method's JIT body.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void InitializeValidatedGame()
    {
        var bridge = new HostBridge();
        HostBridge.Instance = bridge;

        // Managed BepInEx logging is safe on request threads and reaches LogOutput.log
        // even when Unity log forwarding is disabled. Keep the host-owned diagnostic ring.
        void Emit(Action<object> write, string msg)
        {
#if FM27_DEVELOPER_TOOLS
            bridge.LogHistory.Add(msg);
#endif
            write(msg);
        }
        ModLog.LogFn = msg => Emit(base.Log.LogInfo, msg);
        ModLog.WarnFn = msg => Emit(base.Log.LogWarning, msg);
        ModLog.ErrorFn = msg => Emit(base.Log.LogError, msg);
        // Debug lines go to the ring buffer ONLY (thread-safe managed state, no marshalling
        // needed): they fire on hot paths — every utterance, every per-compose profile
        // activation — and the Unity write each would otherwise pay is a synchronous Player.log
        // file write on the main thread. The dev driver's /log still shows them all.
#if FM27_DEVELOPER_TOOLS
        ModLog.DebugFn = msg => bridge.LogHistory.Add(msg);
#else
        ModLog.DebugFn = _ => { };
#endif

        ModLog.Info("[Host] Entry point reached.");

        // One-time, non-reloadable initialization (see class summary).
        PrismLoader.Load();
        SpeechEngine.Initialize();
        RegisterSpeechFilters();

        // IL2CPP type injection is one-way: register and create the permanent behaviour once.
        ClassInjector.RegisterTypeInIl2Cpp<HostBehaviour>();
        AddComponent<HostBehaviour>();

        // Keep the game updating (and the main-thread pumps draining) while focus is on the
        // browser/companion app rather than the game window.
        UnityEngine.Application.runInBackground = true;

        CompanionLauncher.Launch();

        // Load and mount the reloadable core. Everything below the host lives here.
        CoreHost.MountInitial(bridge);

        ModLog.Info("[Host] Initialized.");
    }

    // SpeechPipeline filters (architecture Section 7a step 4, registered in order). Lives in the
    // host because the filter list is permanent state on SpeechPipeline; re-registering on every
    // reload would stack duplicates.
    private static void RegisterSpeechFilters()
    {
        SpeechPipeline.RegisterFilter(TextFilters.StripControlCharacters);
        SpeechPipeline.RegisterFilter(TextFilters.ConvertSpriteTags);
        SpeechPipeline.RegisterFilter(TextFilters.ExtractLinkText);
        SpeechPipeline.RegisterFilter(TextFilters.StripKnownRichTextTags);
        SpeechPipeline.RegisterFilter(TextFilters.StripRemainingTags);
        SpeechPipeline.RegisterFilter(TextFilters.NormalizeWhitespace);
    }
}
