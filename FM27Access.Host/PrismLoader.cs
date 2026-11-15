using System.IO;
using System.Runtime.InteropServices;
using FM27Access.Speech;

namespace FM27Access.Host;

/// <summary>
/// Pre-loads prism.dll via NativeLibrary so the [DllImport("prism")] calls in the Speech assembly
/// resolve (IL2CPP won't search subdirectories). The native library cannot be unloaded, so this is
/// a host (permanent) one-time op — never re-run on reload.
/// </summary>
internal static class PrismLoader
{
    public static void Load()
    {
        // TODO: platform detection per architecture Section 9f (Linux/macOS)
        var prismPath = Path.Combine(
            BepInEx.Paths.PluginPath,
            "FM27Access", "prism", "native", "win-x64", "prism.dll");

        if (!NativeLibrary.TryLoad(prismPath, out _))
        {
            Log.Error($"FATAL: Failed to load Prism native library at: {prismPath}");
            throw new DllNotFoundException(
                $"FM27Access cannot start: Prism native library not found or failed to load at {prismPath}");
        }

        Log.Info($"Prism native library loaded from: {prismPath}");
    }
}
