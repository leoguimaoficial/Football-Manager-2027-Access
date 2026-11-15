#nullable enable
using System;
using System.IO;

namespace FM27Access.Plugin.Server;

// Complete a settings file beside its destination before replacing the old
// file. A write/rename failure must not truncate the player's previous settings.
internal static class SettingsFileWriter
{
    internal static void Write(string path, string contents, Action<Exception>? cleanupFailure = null)
    {
        string target = Path.GetFullPath(path);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(temporary, contents);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            // The unique sibling belongs to this write only. Preserve any
            // original failure instead of disguising it as a cleanup failure.
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException ex) { cleanupFailure?.Invoke(ex); }
            catch (UnauthorizedAccessException ex) { cleanupFailure?.Invoke(ex); }
        }
    }
}
