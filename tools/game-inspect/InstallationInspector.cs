#nullable enable
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FM27Access.Platform;

public sealed record InstallationReport(string Path, string Store, string Identity, string GameVersion,
    string Architecture, string UnityVersion, int MetadataVersion, string AssemblyHash, string MetadataHash,
    string LoaderVersion, int InteropAssemblyCount, string[] Findings);

public static class InstallationInspector
{
    public static InstallationReport Inspect(string directory)
    {
        string root = System.IO.Path.GetFullPath(directory);
        var findings = new List<string>();
        if (!File.Exists(System.IO.Path.Combine(root, "fm.exe"))) throw new InvalidDataException("fm.exe not found.");
        string appInfoPath = System.IO.Path.Combine(root, "fm_Data", "app.info");
        var appInfo = File.ReadAllLines(appInfoPath);
        if (appInfo.Length < 2 || appInfo[0].Trim() != "Sports Interactive" || appInfo[1].Trim() != "Football Manager 27")
            throw new InvalidDataException("Installation is not identified as Football Manager 27 by app.info.");
        string store = "Unknown", identity = "", version = "Unknown";
        string xbox = System.IO.Path.Combine(root, "MicrosoftGame.config");
        if (File.Exists(xbox))
        {
            var config = XDocument.Load(xbox);
            var id = config.Root?.Element("Identity");
            if ((string?)id?.Attribute("Name") != "SportsInteractive.FootballManager27") throw new InvalidDataException("Unexpected Xbox package identity.");
            store = "Xbox"; identity = (string)id!.Attribute("Name")!; version = (string?)id.Attribute("Version") ?? "Unknown";
        }
        else
        {
            // Steam installations are authenticated by their appmanifest, not a directory name.
            string? steamapps = Directory.GetParent(root)?.Parent?.FullName;
            if (steamapps != null && Directory.Exists(steamapps))
                foreach (string file in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    var values = ReadVdfPairs(File.ReadAllText(file));
                    if (values.TryGetValue("installdir", out var installDir) && installDir == System.IO.Path.GetFileName(root)
                        && values.TryGetValue("name", out var name) && name == "Football Manager 27"
                        && values.TryGetValue("appid", out var appId))
                    {
                        if (store != "Unknown") throw new InvalidDataException("Multiple matching Steam manifests.");
                        store="Steam"; identity=appId; version=values.GetValueOrDefault("buildid", "Unknown");
                    }
                }
        }
        if (store == "Unknown") findings.Add("No verified store identity; native-format and required-API checks are still needed.");
        string assemblyPath = System.IO.Path.Combine(root, "GameAssembly.dll");
        using var assembly = File.OpenRead(assemblyPath);
        byte[] header = new byte[64]; ReadBytes(assembly, header);
        if(header[0] != 'M' || header[1] != 'Z') throw new InvalidDataException("Invalid DOS header.");
        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(60));
        if(peOffset < 64 || peOffset > assembly.Length - 6) throw new InvalidDataException("Invalid PE header.");
        assembly.Position = peOffset;
        byte[] pe = new byte[6]; ReadBytes(assembly, pe);
        if(BinaryPrimitives.ReadUInt32LittleEndian(pe) != 0x4550) throw new InvalidDataException("Invalid PE signature.");
        string architecture = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(4)) switch { 0x8664 => "x64", 0x014c => "x86", 0xAA64 => "arm64", _ => "Unknown" };
        using var metadataStream = File.OpenRead(System.IO.Path.Combine(root, "fm_Data", "il2cpp_data", "Metadata", "global-metadata.dat"));
        byte[] metadata = new byte[8]; ReadBytes(metadataStream, metadata);
        if (metadata.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(metadata) != 0xFAB11BAF)
            throw new InvalidDataException("Invalid IL2CPP metadata signature.");
        int metadataVersion = BinaryPrimitives.ReadInt32LittleEndian(metadata.AsSpan(4));
        var globalManagers = File.ReadAllBytes(System.IO.Path.Combine(root, "fm_Data", "globalgamemanagers"));
        var unity = Regex.Match(Encoding.Latin1.GetString(globalManagers), @"\b\d{4}\.\d+\.\d+[abfp]\d+\b");
        string unityVersion = unity.Success ? unity.Value : "Unknown";
        string loader = System.IO.Path.Combine(root, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll");
        string loaderVersion = File.Exists(loader) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(loader).ProductVersion ?? "Unknown" : "Absent";
        string interop = System.IO.Path.Combine(root, "BepInEx", "interop");
        int count = Directory.Exists(interop) ? Directory.EnumerateFiles(interop, "*.dll").Count() : 0;
        findings.Add("Loader presence and generated assemblies do not prove plugin, screen, command or NVDA compatibility.");
        using var sha = SHA256.Create();
        assembly.Position = 0; metadataStream.Position = 0;
        return new(root, store, identity, version, architecture, unityVersion, metadataVersion,
            Convert.ToHexString(sha.ComputeHash(assembly)), Convert.ToHexString(sha.ComputeHash(metadataStream)), loaderVersion, count, findings.ToArray());
    }
    private static void ReadBytes(Stream stream, byte[] bytes)
    {
        int count = 0;
        while(count < bytes.Length) { int read = stream.Read(bytes, count, bytes.Length-count); if(read == 0) throw new InvalidDataException("Truncated native file header."); count += read; }
    }
    public static string ReadPeArchitecture(ReadOnlySpan<byte> image)
    {
        if (image.Length < 64 || image[0] != 'M' || image[1] != 'Z') throw new InvalidDataException("Invalid DOS header.");
        int offset = BinaryPrimitives.ReadInt32LittleEndian(image.Slice(60, 4));
        if (offset < 64 || offset > image.Length - 6 || BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(offset, 4)) != 0x00004550)
            throw new InvalidDataException("Invalid PE header.");
        return BinaryPrimitives.ReadUInt16LittleEndian(image.Slice(offset + 4, 2)) switch {
            0x8664 => "x64", 0x014c => "x86", 0xAA64 => "arm64", _ => "Unknown" };
    }
    public static Dictionary<string, string> ReadVdfPairs(string vdf)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match pair in Regex.Matches(vdf, "\"(?<key>[^\"]+)\"\\s*\"(?<value>[^\"]*)\""))
            values[pair.Groups["key"].Value] = pair.Groups["value"].Value.Replace("\\\\", "\\");
        return values;
    }
    public static IEnumerable<string> SteamLibraries(string root)
    {
        yield return System.IO.Path.GetFullPath(root);
        string vdf = System.IO.Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"(?<path>[^\"]+)\""))
            yield return match.Groups["path"].Value.Replace("\\\\", "\\");
    }
    public static IEnumerable<string> DiscoverSteam(string steamRoot)
    {
        foreach (string library in SteamLibraries(steamRoot).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string apps = System.IO.Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (string manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                var values = ReadVdfPairs(File.ReadAllText(manifest));
                if (!values.TryGetValue("name", out var name) || name != "Football Manager 27" || !values.TryGetValue("installdir", out var folder)) continue;
                string common = System.IO.Path.GetFullPath(System.IO.Path.Combine(apps, "common")) + System.IO.Path.DirectorySeparatorChar;
                string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(common, folder));
                if (!candidate.StartsWith(common, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid Steam install directory.");
                yield return candidate;
            }
        }
    }
}
