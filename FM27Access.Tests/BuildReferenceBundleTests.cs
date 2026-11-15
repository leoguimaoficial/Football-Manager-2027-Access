using System.Security.Cryptography;
using System.Text.Json;
using FM27Access.Build;
using FM27Access.Platform;

namespace FM27Access.Tests;

public sealed class BuildReferenceBundleTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fm27-reference-test-" + Guid.NewGuid().ToString("N"));
    private ReferenceManifest Create()
    {
        Directory.CreateDirectory(Path.Combine(root, "BepInEx/interop"));
        string target = Path.Combine(root, "BepInEx/interop/Fixture.dll");
        File.Copy(typeof(BuildReferenceBundleTests).Assembly.Location, target);
        var native = new InstallationReport("", "Xbox", "fixture", "0.9.0.0", "x64", "6000.3.18f1", 39,
            new string('A', 64), new string('B', 64), "6.0.0-be.793+fixture", 1, []);
        var manifest = new ReferenceManifest(1, native, [new("BepInEx/interop/Fixture.dll", Hash(target))]);
        Save(manifest);
        return manifest;
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private void Save(ReferenceManifest manifest) => File.WriteAllText(Path.Combine(root, "build-references.json"), JsonSerializer.Serialize(manifest));
    [Fact]
    public void AcceptsExactManagedInputAndMatchingPublicPin()
    {
        var manifest = Create();
        var loaded = BuildReferenceBundle.Validate(root);
        Assert.Equal(BuildReferenceBundle.Identity(manifest), BuildReferenceBundle.Identity(loaded));
        string pin = Path.Combine(root, "pin.json");
        File.WriteAllText(pin, JsonSerializer.Serialize(new { schema = 1, identity = BuildReferenceBundle.Identity(manifest) }));
        BuildReferenceBundle.CheckBaseline(loaded, pin);
        File.WriteAllText(pin, JsonSerializer.Serialize(new { schema = 1, identity = new string('0', 64) }));
        Assert.Throws<InvalidDataException>(() => BuildReferenceBundle.CheckBaseline(loaded, pin));
    }
    [Fact]
    public void DetectsChangedDllAndUnlistedFiles()
    {
        Create();
        File.AppendAllText(Path.Combine(root, "BepInEx/interop/Fixture.dll"), "changed");
        Assert.Throws<InvalidDataException>(() => BuildReferenceBundle.Validate(root));
        Directory.Delete(root, true);
        Create();
        File.WriteAllText(Path.Combine(root, "BepInEx/interop/unlisted.txt"), "extra");
        Assert.Throws<InvalidDataException>(() => BuildReferenceBundle.Validate(root));
    }
    [Theory]
    [InlineData("BepInEx/interop/../Fixture.dll")]
    [InlineData("GameAssembly.dll")]
    [InlineData("BepInEx/interop/sub/Fixture.dll")]
    [InlineData("BepInEx/interop/C:Fixture.dll")]
    public void RejectsEscapedOrNonReferencePaths(string relative)
    {
        var manifest = Create();
        Save(manifest with { Files = [manifest.Files[0] with { Relative = relative }] });
        Assert.Throws<InvalidDataException>(() => BuildReferenceBundle.Validate(root));
    }
    [Fact]
    public void RejectsNativeBytesEvenWithMatchingHash()
    {
        var manifest = Create();
        string path = Path.Combine(root, manifest.Files[0].Relative);
        // A complete PE image without CLR metadata is still not compiler input.
        byte[] bytes = File.ReadAllBytes(path);
        int pe = BitConverter.ToInt32(bytes, 60);
        int optional = pe + 24;
        int directories = optional + (BitConverter.ToUInt16(bytes, optional) == 0x20b ? 112 : 96);
        Array.Clear(bytes, directories + 14 * 8, 8);
        File.WriteAllBytes(path, bytes);
        Save(manifest with { Files = [manifest.Files[0] with { Sha256 = Hash(path) }] });
        Assert.Throws<InvalidDataException>(() => BuildReferenceBundle.Validate(root));
    }
    [Fact]
    public void NativeUpdateChangesPinEvenWhenDllNamesStayTheSame()
    {
        var manifest = Create();
        var changed = manifest with { Native = manifest.Native with { MetadataHash = new string('C', 64) } };
        Assert.NotEqual(BuildReferenceBundle.Identity(manifest), BuildReferenceBundle.Identity(changed));
        Assert.Equal(BuildReferenceBundle.Identity(manifest), BuildReferenceBundle.Identity(manifest with { Files = manifest.Files.Reverse().ToArray() }));
    }
    [Fact]
    public void RejectsLocalPathAndUnsupportedNativeFamily()
    {
        var manifest = Create();
        Save(manifest with { Native = manifest.Native with { Path = "C:/private-game" } });
        Assert.Throws<InvalidDataException>(() => BuildReferenceBundle.Validate(root));
        Save(manifest with { Native = manifest.Native with { MetadataVersion = 40 } });
        Assert.Throws<InvalidDataException>(() => BuildReferenceBundle.Validate(root));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
