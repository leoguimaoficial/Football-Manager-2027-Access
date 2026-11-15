#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FM27Access.Speech;

/// <summary>Mirrors the pinned loader's managed cache checksum, without invoking it.</summary>
public static class InteropGenerationState
{
    public static string BaseDirectory(string gameRoot)
    {
        string bepinex = Path.Combine(gameRoot, "BepInEx");
        string config = Path.Combine(bepinex, "config", "BepInEx.cfg");
        string path = "{BepInEx}";
        if (File.Exists(config))
        {
            string text = File.ReadAllText(config);
            var section = Regex.Match(text, @"(?ms)^\[IL2CPP\][^\r\n]*\r?\n.*?(?=^\[|\z)");
            var setting = Regex.Match(section.Value, @"(?m)^IL2CPPInteropAssembliesPath\s*=([^\r\n]*)");
            if (setting.Success) path = setting.Groups[1].Value.Trim();
        }
        return Path.GetFullPath(Path.Combine(gameRoot, path.Replace("{BepInEx}", bepinex).Replace("{ProcessName}", "fm")));
    }
    public static string AssemblyDirectory(string gameRoot) => Path.Combine(BaseDirectory(gameRoot), "interop");

    public static string ComputeChecksum(string gameRoot)
    {
        using var md5 = MD5.Create(); // BepInEx793 cache protocol, not a security fingerprint.
        void Bytes(byte[] bytes, int count) => md5.TransformBlock(bytes, 0, count, bytes, 0);
        void Text(string value) { var bytes = Encoding.UTF8.GetBytes(value); Bytes(bytes, bytes.Length); }
        void FileHash(string path)
        {
            using var stream = File.OpenRead(path);
            byte[] buffer = new byte[81920]; int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) Bytes(buffer, read);
        }
        FileHash(Path.Combine(gameRoot, "GameAssembly.dll"));
        string libraries = Path.Combine(BaseDirectory(gameRoot), "unity-libs");
        if (Directory.Exists(libraries))
            // Match the actual loader's enumeration order, not an invented sorted checksum.
            foreach (string file in Directory.EnumerateFiles(libraries, "*.dll", SearchOption.TopDirectoryOnly))
            { Text(Path.GetFileName(file)); FileHash(file); }
        string map = Path.Combine(gameRoot, "BepInEx", "DeobfuscationMap.csv.gz");
        if (File.Exists(map)) FileHash(map);
        string core = Path.Combine(gameRoot, "BepInEx", "core");
        Text((AssemblyName.GetAssemblyName(Path.Combine(core, "Il2CppInterop.Generator.dll")).Version ?? throw new InvalidDataException("Missing generator version.")).ToString());
        Text((AssemblyName.GetAssemblyName(Path.Combine(core, "Cpp2IL.Core.dll")).Version ?? throw new InvalidDataException("Missing Cpp2IL version.")).ToString());
        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(md5.Hash ?? throw new InvalidDataException("Missing generation checksum."));
    }
    public static bool IsCurrent(string gameRoot)
    {
        string path = Path.Combine(AssemblyDirectory(gameRoot), "assembly-hash.txt");
        return File.Exists(path) && new FileInfo(path).Length <= 128
            && string.Equals(File.ReadAllText(path).Trim(), ComputeChecksum(gameRoot), StringComparison.OrdinalIgnoreCase);
    }
}
