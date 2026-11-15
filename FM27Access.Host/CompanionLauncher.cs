using System.Diagnostics;
using System.IO;
using FM27Access.Speech;

namespace FM27Access.Host;

/// <summary>
/// Launches the standalone companion window at game startup so the player does not have to start
/// it by hand. Host-owned and run once: relaunching on every core reload would stack duplicate
/// windows. The companion connects to core's AccessibilityServer and retries on its own until the
/// server is up, so it survives core reloads (the socket goes away and comes back) without help.
///
/// This is a non-speech convenience: a failed launch warns and continues. The player can still open
/// the companion (or a browser) manually, so it must never crash the game.
/// </summary>
internal static class CompanionLauncher
{
    private const string ProcessName = "FM27AccessCompanion";

    public static void Launch(string startupError = null)
    {
        var exePath = Path.Combine(
            BepInEx.Paths.PluginPath, "FM27Access", "companion", "FM27AccessCompanion.exe");

        if (!File.Exists(exePath))
        {
            Log.Warn($"[CompanionLauncher] Companion exe not found at {exePath}; skipping auto-launch.");
            return;
        }

        // The player may already have a companion open (e.g. launched by hand). Launching a
        // second one would stack a redundant window, so skip if one is already running.
        if (startupError == null && Process.GetProcessesByName(ProcessName).Length > 0)
        {
            Log.Info("[CompanionLauncher] Companion already running; skipping auto-launch.");
            return;
        }

        try
        {
            var gamePid = Process.GetCurrentProcess().Id;
            var start = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("--watch-pid");
            start.ArgumentList.Add(gamePid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (startupError != null)
            {
                start.ArgumentList.Add("--startup-error");
                start.ArgumentList.Add(startupError);
            }
            Process.Start(start);
            Log.Info("[CompanionLauncher] Launched companion window.");
        }
        catch (System.Exception ex)
        {
            Log.Warn($"[CompanionLauncher] Failed to launch companion: {ex.Message}");
        }
    }
}
