using System.Text.Json;
using System.Xml.Linq;
using FM27Access.Platform;
using Microsoft.Win32;

namespace FM27Access.Installer;

public sealed record GameInstallation(string Path, string Platform, string GameVersion)
{
    public override string ToString() => $"{Platform} — {Path}";
}
public sealed class GameDiscoveryService
{
    public static string ResolveGameRoot(string path)
    {
        var directory = new DirectoryInfo(System.IO.Path.GetFullPath(path));
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            directory = directory.ResolveLinkTarget(true) as DirectoryInfo ?? throw new InvalidDataException("Unresolved game directory alias.");
        string resolved = directory.FullName;
        if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal) && !resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) resolved = resolved[4..];
        return System.IO.Path.GetFullPath(resolved);
    }
    public GameInstallation Validate(string path, string? platform = null)
    {
        path = System.IO.Path.GetFullPath(path);
        if (!File.Exists(System.IO.Path.Combine(path, "fm.exe")) && Directory.Exists(System.IO.Path.Combine(path, "Content"))) path = System.IO.Path.Combine(path, "Content");
        path = ResolveGameRoot(path);
        var report = InstallationInspector.Inspect(path);
        BepInExService.ValidateGame(path);
        return new(path, report.Store == "Unknown" ? platform ?? "Manual" : report.Store, report.GameVersion);
    }
    public static IEnumerable<string> EpicPaths(string manifestDirectory)
    {
        if (!Directory.Exists(manifestDirectory)) yield break;
        foreach (string file in Directory.EnumerateFiles(manifestDirectory, "*.item"))
        {
            string? path = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var e = doc.RootElement;
                if (e.TryGetProperty("DisplayName", out var name) && name.GetString() is "Football Manager 27" or "Football Manager 2027"
                    && e.TryGetProperty("InstallLocation", out var location)) path = location.GetString();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
            if (path is not null) yield return path;
        }
    }
    public static IEnumerable<string> XboxDrivePaths(string drive)
    {
        // Xbox's .GamingRoot v1 is an 8-byte header followed by a UTF-16 relative root.
        string marker = System.IO.Path.Combine(drive, ".GamingRoot");
        string gaming = System.IO.Path.Combine(drive, "XboxGames");
        if (File.Exists(marker))
        {
            var bytes = File.ReadAllBytes(marker);
            if (bytes.Length >= 10 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RGBX")
            {
                string relative = System.Text.Encoding.Unicode.GetString(bytes, 8, bytes.Length - 8).TrimEnd('\0').TrimStart('\\');
                try { gaming = Distribution.Target(drive, relative.Replace('\\', '/')); } catch (InvalidDataException) { yield break; }
            }
        }
        if (!Directory.Exists(gaming)) yield break;
        foreach (var folder in Directory.EnumerateDirectories(gaming))
        {
            string content = System.IO.Path.Combine(folder, "Content");
            string config = System.IO.Path.Combine(content, "MicrosoftGame.config");
            if (!File.Exists(config)) continue;
            bool match = false;
            try { match = (string?)XDocument.Load(config).Root?.Element("Identity")?.Attribute("Name") == "SportsInteractive.FootballManager27"; }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException) { }
            if (match) yield return content;
        }
    }
    public IReadOnlyList<GameInstallation> Discover(Action<string>? report = null)
    {
        List<GameInstallation> result = [];
        void Add(string path, string? platform = null)
        {
            try { var game = Validate(path, platform); if (!result.Any(g => g.Path.Equals(game.Path, StringComparison.OrdinalIgnoreCase))) result.Add(game); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Xml.XmlException)
            { report?.Invoke(ex.GetType().Name + ": " + ex.Message); }
        }
        void DiscoverPaths(Func<IEnumerable<string>> find, string platform)
        { try { foreach (string path in find()) Add(path, platform); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { report?.Invoke(ex.Message); } }
        if (OperatingSystem.IsWindows())
        {
            using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string steam = steamKey?.GetValue("SteamPath") as string ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            DiscoverPaths(() => InstallationInspector.DiscoverSteam(steam), "Steam");
            DiscoverPaths(() => EpicPaths(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic/EpicGamesLauncher/Data/Manifests")), "Epic");
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed)) DiscoverPaths(() => XboxDrivePaths(drive.RootDirectory.FullName), "Xbox");
            // Registered Store packages cover installations outside Xbox's mutable Content roots.
            using var packages = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if (packages is not null)
                foreach (string name in packages.GetSubKeyNames().Where(n => n.StartsWith("SportsInteractive.FootballManager27_", StringComparison.OrdinalIgnoreCase)))
                { using var key = packages.OpenSubKey(name); if (key?.GetValue("PackageRootFolder") is string path) Add(path, "Xbox"); }
        }
        return result;
    }
}
