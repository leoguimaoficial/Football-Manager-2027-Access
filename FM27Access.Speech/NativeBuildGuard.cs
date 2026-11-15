using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace FM27Access.Speech;

/// <summary>Managed preflight before any mod-owned Unity injection or native hooks.</summary>
public static class NativeBuildGuard
{
    internal sealed class Profile
    {
        public string Version { get; set; }
        public string Assembly { get; set; }
        public string Metadata { get; set; }
    }

    private static readonly Profile[] Profiles = LoadProfiles();
    public readonly record struct Result(bool Supported, string Version, string Reason);

    private static Profile[] LoadProfiles()
    {
        using var stream = typeof(NativeBuildGuard).Assembly.GetManifestResourceStream("FM27Access.NativeProfiles.json")
            ?? throw new InvalidOperationException("Missing native compatibility profiles.");
        return JsonSerializer.Deserialize<Profile[]>(stream)
            ?? throw new InvalidOperationException("Invalid native compatibility profiles.");
    }

    internal static bool Matches(string identity, string version, string assembly, string metadata) =>
        identity == "SportsInteractive.FootballManager27" && Profiles.Any(p =>
            p.Version == version && string.Equals(p.Assembly, assembly, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Metadata, metadata, StringComparison.OrdinalIgnoreCase));

    public sealed class FileFingerprint
    {
        public string Relative { get; set; }
        public string Sha256 { get; set; }
    }
    public sealed class Deployment
    {
        public int Schema { get; set; }
        public string Version { get; set; }
        public string Assembly { get; set; }
        public string Metadata { get; set; }
        public string UnityFamily { get; set; }
        public int MetadataVersion { get; set; }
        public string ApiFile { get; set; }
        public FileFingerprint[] LoaderFiles { get; set; }
    }
    private static string SafeFile(string root, string relative)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Compatibility path escaped its installation.");
        return path;
    }
    public static Result Validate(string gameRoot, string deploymentProfile = null)
    {
        string version = "unknown";
        try
        {
            if (deploymentProfile == null) return new(false, version, "Missing deployment compatibility profile.");
            using var input = File.OpenRead(deploymentProfile);
            var expected = JsonSerializer.Deserialize<Deployment>(input);
            if (expected?.Schema != 2 || expected.ApiFile != "required-api.json" || expected.MetadataVersion != 39
                || expected.UnityFamily != "6000.3." || expected.LoaderFiles == null || expected.LoaderFiles.Length == 0
                || expected.LoaderFiles.Any(file => file == null || string.IsNullOrWhiteSpace(file.Relative) || string.IsNullOrWhiteSpace(file.Sha256)))
                return new(false, version, "Missing or unsupported native API compatibility profile.");
            var report = FM27Access.Platform.InstallationInspector.Inspect(gameRoot);
            version = report.GameVersion;
            if (report.Architecture != "x64" || report.MetadataVersion != expected.MetadataVersion || !report.UnityVersion.StartsWith(expected.UnityFamily, StringComparison.Ordinal))
                return new(false, version, "Game architecture, Unity family or IL2CPP metadata format is incompatible.");
            // Preserve known mixed-pair refusal, without treating a new build number as an error.
            if (Profiles.Any(p => p.Assembly == report.AssemblyHash) && Profiles.Any(p => p.Metadata == report.MetadataHash)
                && !Profiles.Any(p => p.Assembly == report.AssemblyHash && p.Metadata == report.MetadataHash))
                return new(false, version, "Native assembly and metadata belong to different known builds.");
            if (report.AssemblyHash == expected.Assembly && report.MetadataHash != expected.Metadata)
                return new(false, version, "Metadata changed without its bound native assembly; regenerate and inspect the native pair.");
            using var sha = SHA256.Create();
            foreach (var file in expected.LoaderFiles.Where(f => f.Relative is "version.dll" or "winhttp.dll"))
            {
                string other = file.Relative == "version.dll" ? "winhttp.dll" : "version.dll";
                if (File.Exists(Path.Combine(gameRoot, other)))
                    return new(false, version, "Conflicting Doorstop proxies; repair the owned installation before loading the mod.");
            }
            foreach (var file in expected.LoaderFiles)
            {
                string path = SafeFile(gameRoot, file.Relative);
                using var stream = File.OpenRead(path);
                if (!string.Equals(Convert.ToHexString(sha.ComputeHash(stream)), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    return new(false, version, "Loader prerequisite differs: " + file.Relative);
            }
            if (!InteropGenerationState.IsCurrent(gameRoot))
                return new(false, version, "Generated interop is stale or its loader cache is missing; regenerate it locally before loading the mod.");
            using var api = File.OpenRead(SafeFile(Path.GetDirectoryName(Path.GetFullPath(deploymentProfile)), expected.ApiFile));
            var requirements = JsonSerializer.Deserialize<ManagedApiContract.TypeContract[]>(api);
            string[] errors = ManagedApiContract.Validate(requirements, InteropGenerationState.AssemblyDirectory(gameRoot));
            if (errors.Length != 0) return new(false, version, errors[0] + (errors.Length > 1 ? " (" + errors.Length + " compatibility differences)" : ""));
            bool exact = report.AssemblyHash == expected.Assembly && report.MetadataHash == expected.Metadata;
            return new(true, version, exact ? "Native API verified against the reference binary pair."
                : "Native API compatible variant (" + report.Store + "); live gameplay on this build is not yet qualified.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException or JsonException or BadImageFormatException or InvalidOperationException)
        {
            return new(false, version, "Could not verify game compatibility (" + ex.GetType().Name + "): " + ex.Message);
        }
    }
}
