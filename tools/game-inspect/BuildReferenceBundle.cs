using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using FM27Access.Platform;
using FM27Access.Speech;

namespace FM27Access.Build;

public sealed record ReferenceFile(string Relative, string Sha256);
public sealed record ReferenceManifest(int Schema, InstallationReport Native, ReferenceFile[] Files);

/// <summary>Private compiler input only. Never a player payload or source-archive input.</summary>
public static class BuildReferenceBundle
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static string Hash(string file) { using var input = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(input)); }
    public static string Identity(ReferenceManifest manifest)
    {
        var text = manifest.Native.AssemblyHash + ":" + manifest.Native.MetadataHash + ":" + manifest.Native.LoaderVersion + "\n"
            + string.Join("\n", manifest.Files.OrderBy(f => f.Relative, StringComparer.Ordinal).Select(f => f.Relative + ":" + f.Sha256));
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }
    public static ReferenceManifest Describe(string game)
    {
        var native = InstallationInspector.Inspect(game);
        CheckNative(native);
        if (!InteropGenerationState.IsCurrent(game)) throw new InvalidDataException("Regenerate interop locally with the current game before syncing build references.");
        string interop = InteropGenerationState.AssemblyDirectory(game);
        var files = Directory.GetFiles(interop, "*.dll").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        if (files.Length == 0) throw new InvalidDataException("Missing compiler reference assemblies.");
        List<ReferenceFile> entries = [];
        foreach (string file in files)
        {
            Managed(file);
            string relative = "BepInEx/interop/" + Path.GetFileName(file);
            entries.Add(new(relative, Hash(file)));
        }
        // No local installation paths, diagnostics or game files are exported.
        return new ReferenceManifest(1, native with { Path = "", Findings = [] }, entries.ToArray());
    }
    public static ReferenceManifest Export(string game, string destination)
    {
        var manifest = Describe(game);
        if (Directory.Exists(destination)) throw new IOException("Reference export destination already exists.");
        foreach (var file in manifest.Files)
        {
            string target = Path.Combine(destination, file.Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(InteropGenerationState.AssemblyDirectory(game), Path.GetFileName(file.Relative)), target, false);
        }
        File.WriteAllText(Path.Combine(destination, "build-references.json"), JsonSerializer.Serialize(manifest, Json));
        Validate(destination);
        return manifest;
    }
    public static void CheckBaseline(ReferenceManifest manifest, string baselinePath)
    {
        using var baseline = JsonDocument.Parse(File.ReadAllText(baselinePath));
        if (baseline.RootElement.GetProperty("schema").GetInt32() != 1 ||
            !string.Equals(baseline.RootElement.GetProperty("identity").GetString(), Identity(manifest), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("FM27 compiler references changed. Run scripts/update-build-references.ps1 -GamePath <game-root>, then test and commit packaging/build-references.json with the game adaptation; see docs/DISTRIBUTION.md.");
    }
    public static ReferenceManifest Validate(string root)
    {
        var manifest = JsonSerializer.Deserialize<ReferenceManifest>(File.ReadAllText(Path.Combine(root, "build-references.json")), Json)
            ?? throw new InvalidDataException("Invalid reference manifest.");
        if (manifest.Schema != 1 || manifest.Files is not { Length: > 0 and <= 512 } || manifest.Native.Path != "") throw new InvalidDataException("Unsupported private reference schema.");
        CheckNative(manifest.Native);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            const string prefix = "BepInEx/interop/";
            if (!file.Relative.StartsWith(prefix, StringComparison.Ordinal) || !file.Relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || file.Relative[prefix.Length..].IndexOfAny(['/', '\\', ':']) >= 0 || file.Relative.Contains("..", StringComparison.Ordinal)
                || !seen.Add(file.Relative) || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Unsafe private reference path.");
            string path = Path.Combine(root, file.Relative);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || Hash(path) != file.Sha256) throw new InvalidDataException("Private compiler reference changed.");
            Managed(path);
        }
        var actual = Directory.GetFiles(Path.Combine(root, "BepInEx/interop"), "*", SearchOption.AllDirectories);
        if (actual.Length != seen.Count) throw new InvalidDataException("Unlisted private compiler reference.");
        return manifest;
    }
    private static void CheckNative(InstallationReport native)
    {
        if (native.Architecture != "x64" || !native.UnityVersion.StartsWith("6000.3.", StringComparison.Ordinal) || native.MetadataVersion != 39
            || !native.LoaderVersion.StartsWith("6.0.0-be.793", StringComparison.Ordinal) || native.AssemblyHash.Length != 64 || native.MetadataHash.Length != 64)
            throw new InvalidDataException("Compiler references do not match the supported native family/loader.");
    }
    private static void Managed(string file)
    {
        using var stream = File.OpenRead(file); using var pe = new PEReader(stream);
        if (!pe.HasMetadata) throw new InvalidDataException("Only generated managed compiler references may enter the private bundle.");
    }
}
