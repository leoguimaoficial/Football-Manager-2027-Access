using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FM27Access.Installer.Tests;

public sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "fm27-installer-test-" + Guid.NewGuid());
    public string Game => Path.Combine(Root, "game");
    public string Staged => Path.Combine(Root, "staged");
    public string Zip => Path.Combine(Root, "package.zip");
    public DistributionManifest Manifest { get; private set; }
    public InstallationService Service(Action<int>? fault = null) => new(_ => { }, _ => { }, fault);
    public Fixture()
    {
        Directory.CreateDirectory(Game); Directory.CreateDirectory(Staged);
        var paths = new[] { "FM27Access.Host.dll", "FM27Access.Plugin.dll", "FM27Access.Speech.dll", "FM27Access.Navigation.dll", "native-build.json", "required-api.json", "LICENSE",
            "THIRD-PARTY-LICENSES.txt", "companion/FM27AccessCompanion.exe", "prism/native/win-x64/prism.dll" }.Select(p => Distribution.PluginRoot + p)
            .Concat(["winhttp.dll", "doorstop_config.ini", ".doorstop_version", "BepInEx/core/BepInEx.Unity.IL2CPP.dll"]);
        foreach (var p in paths) Write(Staged, p, "synthetic file " + p);
        Write(Staged, "BepInEx/config/BepInEx.cfg", BepInExService.LoggingText(""));
        Manifest = Build("0.1.0");
        Write(Game, "saves/career.fm", "private save");
        Write(Game, "BepInEx/plugins/UserMod/mod.dll", "other mod");
    }
    public static void Write(string root, string relative, string text)
    { string target = Distribution.Target(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, text); }
    public DistributionManifest Build(string version)
    {
        if (File.Exists(Zip)) File.Delete(Zip);
        // The staging directory is the exact payload; logging staging is produced later by the service.
        var files = Directory.EnumerateFiles(Staged, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith("logging-production.cfg", StringComparison.Ordinal))
            .Select(p => { string relative = Path.GetRelativePath(Staged, p).Replace('\\', '/'); return new PackageFile(relative, Distribution.Hash(p), new FileInfo(p).Length,
                relative.StartsWith(Distribution.PluginRoot, StringComparison.Ordinal) ? "mod" : relative == "BepInEx/config/BepInEx.cfg" ? "config" : "loader"); }).ToArray();
        using (var zip = ZipFile.Open(Zip, ZipArchiveMode.Create)) foreach (var f in files) zip.CreateEntryFromFile(Distribution.Target(Staged, f.Path), f.Path);
        return Manifest = new(1, version, "1.0.0", $"FM27Access-v{version}.zip", Distribution.Hash(Zip), new FileInfo(Zip).Length,
            "6.0.0-be.793+dbc82f0", ["Xbox", "Steam", "Epic", "Manual"], "Synthetic compatibility fixture", files);
    }
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}

public sealed class InstallationTests
{
    [Fact] public async Task InstallUpdateRepairUninstallPreservePersonalFilesAndSharedLoader()
    {
        using var f = new Fixture(); var s = f.Service(); var state = new InstallationStateService();
        Fixture.Write(f.Game, "BepInEx/config/BepInEx.cfg", "[Other]\nValue = untouched\n");
        await s.InstallAsync(f.Game, f.Staged, f.Manifest, null, default);
        Assert.Equal("0.1.0", state.Read(f.Game)!.Version); Assert.Empty(state.Verify(f.Game, state.Read(f.Game)!));
        Fixture.Write(f.Game, Distribution.PluginRoot + "bookmarks.json", "my bookmarks");
        Fixture.Write(f.Staged, Distribution.PluginRoot + "FM27Access.Plugin.dll", "new mod");
        var next = f.Build("0.2.0"); await s.InstallAsync(f.Game, f.Staged, next, null, default);
        Fixture.Write(f.Game, Distribution.PluginRoot + "FM27Access.Plugin.dll", "damaged");
        Assert.Single(state.Verify(f.Game, state.Read(f.Game)!));
        await s.InstallAsync(f.Game, f.Staged, next, null, default);
        await s.UninstallAsync(f.Game, null, default);
        Assert.Null(state.Read(f.Game)); Assert.True(File.Exists(Path.Combine(f.Game, "winhttp.dll")));
        Assert.Equal("private save", File.ReadAllText(Path.Combine(f.Game, "saves/career.fm")));
        Assert.Equal("my bookmarks", File.ReadAllText(Path.Combine(f.Game, Distribution.PluginRoot + "bookmarks.json")));
        Assert.Equal("other mod", File.ReadAllText(Path.Combine(f.Game, "BepInEx/plugins/UserMod/mod.dll")));
        Assert.Equal("[Other]\nValue = untouched\n", File.ReadAllText(Path.Combine(f.Game, "BepInEx/config/BepInEx.cfg")));
        await s.InstallAsync(f.Game, f.Staged, next, null, default);
        Assert.Equal("0.2.0", state.Read(f.Game)!.Version);
    }
    [Fact] public async Task MidInstallFailureRollsBackEveryFileAndReceipts()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<IOException>(() => f.Service(i => { if (i == 4) throw new IOException("injected write failure"); }).InstallAsync(f.Game, f.Staged, f.Manifest, null, default));
        Assert.Null(new InstallationStateService().Read(f.Game));
        foreach (var file in f.Manifest.Files) Assert.False(File.Exists(Distribution.Target(f.Game, file.Path)));
        Assert.False(File.Exists(InstallationStateService.StatePath(f.Game, "journal.json")));
    }
    [Fact] public async Task UpdateFailureRestoresOldVersionAndBytes()
    {
        using var f = new Fixture(); await f.Service().InstallAsync(f.Game, f.Staged, f.Manifest, null, default);
        string path = Distribution.PluginRoot + "FM27Access.Plugin.dll", original = File.ReadAllText(Distribution.Target(f.Game, path));
        Fixture.Write(f.Staged, path, "new version"); var next = f.Build("0.2.0");
        await Assert.ThrowsAsync<IOException>(() => f.Service(_ => throw new IOException("fixture failure")).InstallAsync(f.Game, f.Staged, next, null, default));
        Assert.Equal("0.1.0", new InstallationStateService().Read(f.Game)!.Version); Assert.Equal(original, File.ReadAllText(Distribution.Target(f.Game, path)));
    }
    [Fact] public async Task CancellationDuringApplyRestoresInstallation()
    {
        using var f = new Fixture(); using var ct = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service(i => { if (i == 2) ct.Cancel(); }).InstallAsync(f.Game, f.Staged, f.Manifest, null, ct.Token));
        Assert.Null(new InstallationStateService().Read(f.Game));
        Assert.False(File.Exists(Distribution.Target(f.Game, "winhttp.dll")));
    }
    [Fact] public async Task UnmanagedCollisionFailsBeforeAnyWrites()
    {
        using var f = new Fixture(); Fixture.Write(f.Game, Distribution.PluginRoot + "FM27Access.Host.dll", "source receipt owned");
        await Assert.ThrowsAsync<IOException>(() => f.Service().InstallAsync(f.Game, f.Staged, f.Manifest, null, default));
        Assert.False(File.Exists(Distribution.Target(f.Game, "winhttp.dll")));
        Assert.Equal("source receipt owned", File.ReadAllText(Distribution.Target(f.Game, Distribution.PluginRoot + "FM27Access.Host.dll")));
    }
    [Fact] public async Task IdenticalExternalLoaderIsReusedWithoutOwnership()
    {
        using var f = new Fixture(); File.Copy(Distribution.Target(f.Staged, "winhttp.dll"), Distribution.Target(f.Game, "winhttp.dll"));
        await f.Service().InstallAsync(f.Game, f.Staged, f.Manifest, null, default);
        Assert.False(new InstallationStateService().Read(f.Game)!.Files.Single(p => p.Path == "winhttp.dll").Owned);
    }
    [Fact] public async Task MismatchedLoaderRefusedAndModifiedModPreservedOnUninstall()
    {
        using var f = new Fixture(); Fixture.Write(f.Game, "winhttp.dll", "foreign loader");
        await Assert.ThrowsAsync<IOException>(() => f.Service().InstallAsync(f.Game, f.Staged, f.Manifest, null, default));
        File.Delete(Distribution.Target(f.Game, "winhttp.dll")); await f.Service().InstallAsync(f.Game, f.Staged, f.Manifest, null, default);
        Fixture.Write(f.Game, Distribution.PluginRoot + "FM27Access.Plugin.dll", "modified");
        await Assert.ThrowsAsync<IOException>(() => f.Service().UninstallAsync(f.Game, null, default));
        Assert.NotNull(new InstallationStateService().Read(f.Game));
    }
    [Fact] public async Task ModifiedLoggingIsPreserved()
    {
        using var f = new Fixture(); await f.Service().InstallAsync(f.Game, f.Staged, f.Manifest, null, default);
        Fixture.Write(f.Game, "BepInEx/config/BepInEx.cfg", "player changed configuration");
        await f.Service().UninstallAsync(f.Game, null, default);
        Assert.Equal("player changed configuration", File.ReadAllText(Distribution.Target(f.Game, "BepInEx/config/BepInEx.cfg")));
    }
    [Fact] public async Task UpdateAfterPlayerConfigChangeRestoresPlayersLatestBytes()
    {
        using var f = new Fixture(); await f.Service().InstallAsync(f.Game, f.Staged, f.Manifest, null, default);
        const string changed = "[Other]\nUserSetting = new preference\n";
        Fixture.Write(f.Game, "BepInEx/config/BepInEx.cfg", changed);
        await f.Service().InstallAsync(f.Game, f.Staged, f.Build("0.2.0"), null, default);
        await f.Service().UninstallAsync(f.Game, null, default);
        Assert.Equal(changed, File.ReadAllText(Distribution.Target(f.Game, "BepInEx/config/BepInEx.cfg")));
    }
    [Fact] public async Task ProductionDefaultsDisableConsoleBeforeFirstLaunch()
    {
        using var f = new Fixture();
        var manifest = f.Manifest;
        await f.Service().InstallAsync(f.Game, f.Staged, manifest, null, default);
        string cfg = File.ReadAllText(Distribution.Target(f.Game, "BepInEx/config/BepInEx.cfg"));
        Assert.Equal(BepInExService.LoggingText(""), cfg);
        Assert.DoesNotContain(new InstallationStateService().Read(f.Game)!.Files, f => f.Kind == "config");
    }
    [Fact] public async Task NativeApiFailureRollsBackCompletePayload()
    {
        using var f = new Fixture();
        var service = new InstallationService(_ => { }, _ => { }, validateFinal: (_, _) => throw new InvalidDataException("Incompatible native API fixture"));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(f.Game, f.Staged, f.Manifest, null, default));
        Assert.Null(new InstallationStateService().Read(f.Game));
        Assert.False(File.Exists(Distribution.Target(f.Game, Distribution.PluginRoot + "FM27Access.Host.dll")));
    }
    [Fact] public async Task RecoveryRestoresInterruptedDurableJournal()
    {
        using var f = new Fixture(); string relative = Distribution.PluginRoot + "FM27Access.Plugin.dll";
        Fixture.Write(f.Game, relative, "partial write"); Fixture.Write(f.Game, ".fm27access/transaction/0.bin", "previous");
        var entry = new JournalEntry(relative, Distribution.Hash(InstallationStateService.StatePath(f.Game, "transaction/0.bin")), Distribution.Hash(Distribution.Target(f.Game, relative)), "transaction/0.bin");
        Distribution.Save(InstallationStateService.StatePath(f.Game, "journal.json"), new TransactionJournal(1, "applying", null, null, [entry]));
        await f.Service().RecoverAsync(f.Game, default);
        Assert.Equal("previous", File.ReadAllText(Distribution.Target(f.Game, relative)));
    }
    [Fact] public async Task RecoveryConflictPreservesUserChangesAndJournal()
    {
        using var f = new Fixture(); string relative = Distribution.PluginRoot + "FM27Access.Plugin.dll";
        Fixture.Write(f.Game, relative, "player changed after crash");
        Distribution.Save(InstallationStateService.StatePath(f.Game, "journal.json"), new TransactionJournal(1, "applying", null, null, [new(relative, null, new string('A', 64), "transaction/0.bin")]));
        await Assert.ThrowsAsync<IOException>(() => f.Service().RecoverAsync(f.Game, default));
        Assert.True(File.Exists(InstallationStateService.StatePath(f.Game, "journal.json")));
        Assert.Equal("player changed after crash", File.ReadAllText(Distribution.Target(f.Game, relative)));
    }
    [Fact] public async Task RecoveryBeforeFirstMutationCleansPartialBackupsOnly()
    {
        using var f = new Fixture(); Fixture.Write(f.Game, ".fm27access/transaction/0.bin", "partial backup");
        Distribution.Save(InstallationStateService.StatePath(f.Game, "journal.json"), new TransactionJournal(1, "preparing", null, null, []));
        await f.Service().RecoverAsync(f.Game, default);
        Assert.False(Directory.Exists(InstallationStateService.StatePath(f.Game, "transaction")));
        Assert.Equal("other mod", File.ReadAllText(Distribution.Target(f.Game, "BepInEx/plugins/UserMod/mod.dll")));
        await f.Service().InstallAsync(f.Game, f.Staged, f.Manifest, null, default);
        Assert.NotNull(new InstallationStateService().Read(f.Game));
    }
    [Fact] public async Task SimultaneousInstallBlocked()
    {
        using var f = new Fixture(); string path = InstallationStateService.StatePath(f.Game, "operation.lock"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var lease = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => f.Service().InstallAsync(f.Game, f.Staged, f.Manifest, null, default));
    }
    [Fact] public async Task ValidPackageExtractsAndDamagedPackageFails()
    {
        using var f = new Fixture(); string dest = Path.Combine(f.Root, "extracted");
        await InstallationService.ExtractAsync(f.Zip, dest, f.Manifest, null, default);
        Assert.Equal(f.Manifest.Files.Length, Directory.GetFiles(dest, "*", SearchOption.AllDirectories).Length);
        await File.AppendAllTextAsync(f.Zip, "corruption");
        await Assert.ThrowsAsync<InvalidDataException>(() => InstallationService.ExtractAsync(f.Zip, Path.Combine(f.Root, "bad"), f.Manifest, null, default));
    }
    [Theory]
    [InlineData("../outside.dll")][InlineData("/root.dll")][InlineData("C:/escape.dll")][InlineData("BepInEx\\plugins\\evil.dll")]
    [InlineData("BepInEx/plugins/FM27Access/file.dll:stream")][InlineData("BepInEx/plugins/FM27Access/CON.dll")][InlineData("BepInEx/plugins/FM27Access/name. ")]
    public void UnsafePathsRejected(string value) => Assert.Throws<InvalidDataException>(() => Distribution.Relative(value));
    [Fact] public async Task ZipTraversalRejectedEvenWithValidArchiveChecksum()
    {
        using var f = new Fixture(); using (var z = ZipFile.Open(f.Zip, ZipArchiveMode.Update)) { var e = z.CreateEntry("../escape"); using var w = new StreamWriter(e.Open()); w.Write("bad"); }
        var manifest = f.Manifest with { PackageSha256 = Distribution.Hash(f.Zip), PackageSize = new FileInfo(f.Zip).Length };
        await Assert.ThrowsAsync<InvalidDataException>(() => InstallationService.ExtractAsync(f.Zip, Path.Combine(f.Root, "bad"), manifest, null, default));
        Assert.False(File.Exists(Path.Combine(f.Root, "escape")));
    }
    [Fact] public void ManifestRejectsUnknownSchemaMissingFilesAndProtectedData()
    {
        using var f = new Fixture();
        Assert.Throws<InvalidDataException>(() => Distribution.Validate(f.Manifest with { Schema = 2 }));
        Assert.Throws<InvalidDataException>(() => Distribution.Validate(f.Manifest with { Files = [] }));
        Assert.Throws<InvalidDataException>(() => Distribution.Validate(f.Manifest with { Files = [.. f.Manifest.Files, new(Distribution.PluginRoot + "bookmarks.json", new string('0', 64), 0, "mod")] }));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task DuplicateAndLinkedEntriesAreRejectedWithValidArchiveHash(bool link)
    {
        using var f = new Fixture();
        using (var zip = ZipFile.Open(f.Zip, ZipArchiveMode.Update))
        {
            var entry = zip.CreateEntry(f.Manifest.Files[0].Path);
            if (link) entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var output = new StreamWriter(entry.Open()); output.Write("unsafe duplicate or link");
        }
        var manifest = f.Manifest with { PackageSha256 = Distribution.Hash(f.Zip), PackageSize = new FileInfo(f.Zip).Length };
        await Assert.ThrowsAsync<InvalidDataException>(() => InstallationService.ExtractAsync(f.Zip, Path.Combine(f.Root, "bad-entry"), manifest, null, default));
    }
    [Theory][InlineData("install", null, "0.1.0")][InlineData("update", "0.1.0", "0.2.0")][InlineData("repair", "0.2.0", "0.2.0")][InlineData("downgrade", "1.0.0", "0.2.0")]
    public void VersionActions(string action, string? installed, string selected) => Assert.Equal(action, InstallationStateService.Action(installed, selected));
    [Fact] public void SemanticOrderingAndPrereleases()
    {
        var versions = new[] { "1.0.0-alpha.2", "1.0.0", "1.0.0-alpha.10", "0.10.0", "0.2.0" };
        Assert.Equal(new[] { "0.2.0", "0.10.0", "1.0.0-alpha.2", "1.0.0-alpha.10", "1.0.0" }, versions.OrderBy(SemVersion.Parse));
        Assert.Throws<InvalidDataException>(() => SemVersion.Parse("1.0.0-01"));
    }
    [Fact] public void LoggingProfilePreservesOtherSettingsAndRefusesAmbiguity()
    {
        string value = BepInExService.LoggingText("[Other]\nSetting = true\n");
        Assert.Contains("Setting = true", value); Assert.Contains("Fatal, Error, Warning", value); Assert.Contains("Enabled = false", value);
        Assert.Throws<InvalidDataException>(() => BepInExService.LoggingText("[Logging.Console]\n[Logging.Console]\n"));
    }
}

public sealed class OnlineTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(send(r)); }
    [Theory][InlineData("http://github.com/file")][InlineData("https://evil.invalid/file")][InlineData("https://github.com/attacker/repo/releases/download/v1/file")]
    [InlineData("https://api.github.com/repos/attacker/repo/releases")]
    public void ForeignOriginsRejected(string uri) => Assert.Throws<InvalidDataException>(() => DownloadService.ValidateOrigin(new(uri)));
    [Fact] public async Task ChecksumFailureAndUnsafeRedirectLeaveNoDownload()
    {
        using var f = new Fixture(); string target = Path.Combine(f.Root, "download");
        using (var d = new DownloadService(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("corrupt") })))
            await Assert.ThrowsAsync<InvalidDataException>(() => d.DownloadAsync(new("https://api.github.com/repos/" + Distribution.Repository + "/releases"), target, 1000, new string('0', 64), null, null, default));
        using (var d = new DownloadService(new Handler(_ => { var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new("https://evil.invalid/file"); return r; })))
            await Assert.ThrowsAsync<InvalidDataException>(() => d.DownloadAsync(new("https://api.github.com/repos/" + Distribution.Repository + "/releases"), target, 1000, null, null, null, default));
        Assert.False(File.Exists(target)); Assert.False(File.Exists(target + ".part"));
    }
    [Fact] public async Task EmptyOfficialCatalogAndOfflineCacheAreDistinct()
    {
        using var f = new Fixture(); using var d = new DownloadService(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("[]") }));
        var catalog = await new GitHubReleaseService(d, f.Root).GetAsync(default);
        Assert.True(catalog.Online); Assert.Empty(catalog.Releases);
        using var offline = new DownloadService(new Handler(_ => throw new HttpRequestException("offline")));
        var cached = await new GitHubReleaseService(offline, f.Root).GetAsync(default);
        Assert.False(cached.Online); Assert.Equal(catalog.Checked, cached.Checked);
    }
    [Fact] public async Task OfficialVersionsSortSemanticallyAndDoNotTreatBuildsAsPublicReleases()
    {
        using var f = new Fixture();
        object Release(string version, bool draft = false) => new {
            tag_name = "v" + version, draft, prerelease = false, published_at = "2026-11-15T12:00:00Z", body = "Release notes",
            assets = new[] { "FM27Access-Manifest.json", $"FM27Access-v{version}.zip" }.Select(name => new {
                name, browser_download_url = $"https://github.com/{Distribution.Repository}/releases/download/v{version}/{name}", size = 100L, digest = (string?)null }) };
        string json = JsonSerializer.Serialize(new[] { Release("0.2.0"), Release("0.10.0"), Release("1.0.0-beta.1"), Release("2.0.0", true), Release("build-101") });
        using var download = new DownloadService(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(json) }));
        var result = await new GitHubReleaseService(download, f.Root).GetAsync(default);
        Assert.Equal(new[] { "1.0.0-beta.1", "0.10.0", "0.2.0" }, result.Releases.Select(r => r.Version));
        Assert.True(result.Releases[0].Prerelease);
        Assert.Equal("0.10.0", result.Releases.First(r => !r.Prerelease).Version);
    }
    [Fact] public void EpicManifestPathsAreReadFromLauncherMetadata()
    {
        using var f = new Fixture(); File.WriteAllText(Path.Combine(f.Root, "game.item"), JsonSerializer.Serialize(new { DisplayName = "Football Manager 27", InstallLocation = f.Game }));
        File.WriteAllText(Path.Combine(f.Root, "unrelated.item"), "{\"DisplayName\":\"Other game\",\"InstallLocation\":\"C:/other\"}");
        Assert.Equal([f.Game], GameDiscoveryService.EpicPaths(f.Root));
    }
    [Fact] public void XboxGamingRootFindsConfiguredDriveContent()
    {
        using var f = new Fixture();
        string root = Path.Combine(f.Root, "Games/FM/Content"); Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "MicrosoftGame.config"), "<Game><Identity Name=\"SportsInteractive.FootballManager27\" /></Game>");
        File.WriteAllBytes(Path.Combine(f.Root, ".GamingRoot"), [.. Encoding.ASCII.GetBytes("RGBX"), 1, 0, 0, 0, .. Encoding.Unicode.GetBytes("Games\0")]);
        Assert.Equal([Path.GetFullPath(root)], GameDiscoveryService.XboxDrivePaths(f.Root));
    }
}
