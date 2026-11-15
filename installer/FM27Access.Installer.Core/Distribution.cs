using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FM27Access.Installer;

public sealed record SemVersion(int Major, int Minor, int Patch, string Pre = "") : IComparable<SemVersion>
{
    public static SemVersion Parse(string value)
    {
        var m = Regex.Match(value, @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$");
        if (!m.Success || m.Groups[4].Value.Split('.').Any(p => p.Length > 1 && p.All(char.IsAsciiDigit) && p[0] == '0'))
            throw new InvalidDataException("Invalid semantic version.");
        return new(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), m.Groups[4].Value);
    }
    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;
        int c = Major.CompareTo(other.Major); if (c != 0) return c;
        c = Minor.CompareTo(other.Minor); if (c != 0) return c;
        c = Patch.CompareTo(other.Patch); if (c != 0) return c;
        if (Pre == other.Pre) return 0;
        if (Pre.Length == 0) return 1; if (other.Pre.Length == 0) return -1;
        var a = Pre.Split('.'); var b = other.Pre.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool an = a[i].All(char.IsAsciiDigit), bn = b[i].All(char.IsAsciiDigit);
            c = an && bn ? (a[i].Length != b[i].Length ? a[i].Length.CompareTo(b[i].Length) : string.CompareOrdinal(a[i], b[i]))
                : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return c;
        }
        return a.Length.CompareTo(b.Length);
    }
}

public sealed record PackageFile(string Path, string Sha256, long Size, string Kind);
public sealed record DistributionManifest(int Schema, string Version, string MinimumInstallerVersion, string Package,
    string PackageSha256, long PackageSize, string BepInExVersion, string[] Platforms, string Compatibility, PackageFile[] Files);
public sealed record ReleaseAsset(string Name, Uri Url, long Size, string? Digest);
public sealed record ModRelease(string Version, string Tag, DateTimeOffset Published, string Notes, bool Prerelease,
    ReleaseAsset Manifest, ReleaseAsset Package)
{
    public override string ToString() => Version;
}
public sealed record ReleaseCatalog(ModRelease[] Releases, bool Online, DateTimeOffset Checked);
public sealed record OperationProgress(string Stage, int Completed, int Total, long Bytes = 0, long? TotalBytes = null);

public static class Distribution
{
    public const string Repository = "leoguimaoficial/Football-Manager-2027-Access";
    public const string PluginRoot = "BepInEx/plugins/FM27Access/";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream));
    }
    public static bool IsHash(string? value) => value is not null && Regex.IsMatch(value, "^[0-9a-fA-F]{64}$");
    public static bool IsLoaderPath(string path) => path is "winhttp.dll" or "doorstop_config.ini" or ".doorstop_version"
        || path.StartsWith("BepInEx/core/", StringComparison.Ordinal) || path.StartsWith("dotnet/", StringComparison.Ordinal);
    public static bool IsModPath(string path) => path.StartsWith(PluginRoot, StringComparison.Ordinal)
        && !Regex.IsMatch(path, @"\.(cfg|pdb|mdb|fm|fmf|dmp|log|tmp|bak)$|/(bookmarks\.json|mcs\.dll|GameAssembly\.dll|UnityPlayer\.dll|Assembly-CSharp\.dll|global-metadata\.dat)$", RegexOptions.IgnoreCase);
    public static string Relative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') || path.Length > 220
            || path.Split('/').Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.Any(c => c < 32 || "<>\"|?*".Contains(c))
                || Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Unsafe package path.");
        return path;
    }
    public static string Target(string root, string relative)
    {
        Relative(relative);
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path escaped root.");
        // Never traverse junctions or symlinks in the destination, including the root itself.
        for (string? p = target; p is not null && p.Length >= prefix.Length - 1; p = Path.GetDirectoryName(p))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(p); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked installation paths are unsupported.");
        }
        return target;
    }
    public static void Validate(DistributionManifest m, string? expectedVersion = null)
    {
        if (m.Schema != 1 || SemVersion.Parse(m.MinimumInstallerVersion).CompareTo(SemVersion.Parse(EngineVersion)) > 0)
            throw new InvalidDataException("Installer update required for this manifest.");
        SemVersion.Parse(m.Version);
        if (expectedVersion is not null && m.Version != expectedVersion) throw new InvalidDataException("Release/manifest version mismatch.");
        if (m.Package != $"FM27Access-v{m.Version}.zip" || !IsHash(m.PackageSha256) || m.PackageSize <= 0 || m.PackageSize > 2L * 1024 * 1024 * 1024
            || m.BepInExVersion != "6.0.0-be.793+dbc82f0" || m.Platforms is not { Length: > 0 }
            || m.Platforms.Any(p => p is not ("Steam" or "Xbox" or "Epic" or "Manual")) || string.IsNullOrWhiteSpace(m.Compatibility)
            || m.Files is not { Length: > 0 and <= 10000 }) throw new InvalidDataException("Unsupported distribution manifest.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var f in m.Files)
        {
            Relative(f.Path);
            if (!paths.Add(f.Path) || !IsHash(f.Sha256) || f.Size < 0 || f.Size > 1024L * 1024 * 1024
                || (f.Kind switch { "mod" => !IsModPath(f.Path), "loader" => !IsLoaderPath(f.Path), "config" => f.Path != "BepInEx/config/BepInEx.cfg", _ => true })
                || f.Path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) || f.Path.EndsWith("/mcs.dll", StringComparison.OrdinalIgnoreCase)
                || (f.Kind != "config" && f.Path.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)) || f.Path.Equals(PluginRoot + "bookmarks.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid managed file entry.");
            total = checked(total + f.Size);
            if (total > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("Package expanded size exceeded.");
        }
        foreach (string required in new[] { "FM27Access.Host.dll", "FM27Access.Plugin.dll", "FM27Access.Speech.dll", "FM27Access.Navigation.dll",
            "native-build.json", "required-api.json", "LICENSE", "THIRD-PARTY-LICENSES.txt", "companion/FM27AccessCompanion.exe", "prism/native/win-x64/prism.dll" })
            if (!paths.Contains(PluginRoot + required)) throw new InvalidDataException("Incomplete mod package: " + required);
        foreach (string required in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "BepInEx/core/BepInEx.Unity.IL2CPP.dll", "BepInEx/config/BepInEx.cfg" })
            if (!paths.Contains(required)) throw new InvalidDataException("Incomplete loader package.");
        foreach (string path in paths)
            for (var parent = path.LastIndexOf('/'); parent > 0; parent = path.LastIndexOf('/', parent - 1))
                if (paths.Contains(path[..parent])) throw new InvalidDataException("File/directory collision.");
    }
    public static string EngineVersion => typeof(Distribution).Assembly.GetName().Version!.ToString(3);
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".new";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, value, Json); stream.Flush(true); }
        File.Move(temp, path, true);
    }
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException("Invalid state document.");
}
