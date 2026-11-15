using System;
using System.IO;
using System.Reflection;
using FM27Access.Speech;

namespace FM27Access.Host;

/// <summary>
/// Loads and reloads the core assembly (FM27Access.Plugin.dll). The host holds no compile-time
/// reference to core: it loads the DLL bytes, finds <c>FM27Access.Plugin.CoreEntry</c> by
/// reflection, and calls its static <c>Mount(IHostBridge)</c> / <c>Unmount()</c>. Loading from a
/// byte[] (not LoadFrom) means the file is never locked, so a rebuild can overwrite it between
/// reloads. See docs/hot-reload-plan.md.
///
/// The previous core assembly is not unloaded (non-collectible load): each reload leaks it, which
/// is irrelevant over a dev session. <c>Unmount</c> must fully unpatch Harmony before the next
/// Mount regardless, or dangling patch trampolines into the old assembly would crash.
/// </summary>
internal static class CoreHost
{
    private const string CoreDll = "FM27Access.Plugin.dll";
    private const string CoreEntryTypeName = "FM27Access.Plugin.CoreEntry";

    private static IHostBridge _bridge;
    private static MethodInfo _unmount;
    private static int _mountCount;

    /// <summary>True while the mount currently running is the first of this game session.</summary>
    public static bool IsFirstMount => _mountCount <= 1;

    public static void MountInitial(IHostBridge bridge)
    {
        _bridge = bridge;
        LoadAndMount();
    }

    /// <summary>
    /// Reload the core in place. Runs on the Unity main thread (posted via <see cref="HostDispatcher"/>
    /// from <c>IHostBridge.RequestReload</c>), so it never races live game code.
    /// </summary>
    public static void Reload()
    {
        Log.Info("[CoreHost] Reloading core...");
        try { _unmount?.Invoke(null, null); }
        catch (Exception ex) { Log.Error($"[CoreHost] Core Unmount failed (continuing into fresh mount): {ex}"); }
        _unmount = null;
        _bridge.Tick = null;

        try
        {
            LoadAndMount();
            Log.Info("[CoreHost] Core reloaded.");
        }
        catch (Exception ex)
        {
            // A failed reload leaves the mod paused (no tick) rather than half-mounted. Mount starts
            // the server early (before the wiring/patches that can throw), so a typical Mount failure
            // still leaves the server up and another /reload (AlwaysReady) can load a fix without a
            // game restart; /log (also AlwaysReady) shows why it failed. Only a failure in Mount's
            // first few lines, before the server starts, needs a relaunch. The host stays loaded either way.
            Log.Fatal($"[CoreHost] Core Mount failed; mod paused. Fix and reload again: {ex}");
        }
    }

    private static void LoadAndMount()
    {
        var path = Path.Combine(BepInEx.Paths.PluginPath, "FM27Access", CoreDll);
        var bytes = File.ReadAllBytes(path);
        var asm = Assembly.Load(bytes);
        var type = asm.GetType(CoreEntryTypeName, throwOnError: true);
        var mount = type.GetMethod("Mount", BindingFlags.Public | BindingFlags.Static)
                    ?? throw new MissingMethodException(CoreEntryTypeName, "Mount");
        _unmount = type.GetMethod("Unmount", BindingFlags.Public | BindingFlags.Static)
                   ?? throw new MissingMethodException(CoreEntryTypeName, "Unmount");
        _mountCount++;
        mount.Invoke(null, new object[] { _bridge });
    }
}

