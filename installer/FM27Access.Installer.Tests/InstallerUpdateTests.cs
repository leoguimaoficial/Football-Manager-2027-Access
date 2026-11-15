using System.Net;
using System.Text.Json;

namespace FM27Access.Installer.Tests;

public sealed class InstallerUpdateTests
{
    private sealed class Handler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request)) });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentEngineUpdateAcceptsBothMetadataGenerations(bool provenance)
    {
        using var f = new Fixture();
        string prefix = $"https://github.com/{Distribution.Repository}/releases/download/v0.1.0/";
        string current = Distribution.EngineVersion;
        var parsed = SemVersion.Parse(current);
        string next = $"{parsed.Major}.{parsed.Minor}.{parsed.Patch + 1}";
        object Release(bool draft = false, bool prerelease = false) => new {
            draft, prerelease, assets = new[] {
                new { name = "FM27Access-Installer.json", browser_download_url = prefix + "FM27Access-Installer.json", size = 300L },
                new { name = "FM27Access-Installer.exe", browser_download_url = prefix + "FM27Access-Installer.exe", size = 100L } }
        };
        var metadata = new Dictionary<string, object> { ["schema"] = 1, ["version"] = next,
            ["sha256"] = new string('A', 64), ["size"] = 100L, ["url"] = prefix + "FM27Access-Installer.exe" };
        if (provenance) { metadata["sourceCommit"] = new string('a', 40); metadata["buildRunId"] = 123L; }
        using var download = new DownloadService(new Handler(request => request.RequestUri!.Host == "api.github.com"
            ? JsonSerializer.Serialize(new[] { Release(true), Release(prerelease: true), Release() })
            : JsonSerializer.Serialize(metadata)));
        var update = await new InstallerUpdateService(download, f.Root).FindAsync(default);
        Assert.NotNull(update); Assert.Equal(next, update.Version);
        Assert.Equal(provenance ? new string('a', 40) : null, update.SourceCommit);
        Assert.Empty(Directory.GetFiles(f.Root, "installer-update-*"));
        metadata["version"] = current;
        Assert.Null(await new InstallerUpdateService(download, f.Root).FindAsync(default));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("url")]
    public async Task EngineOfferRefusesMetadataThatDoesNotMatchTheOfficialExecutable(string mismatch)
    {
        using var f = new Fixture();
        string prefix = $"https://github.com/{Distribution.Repository}/releases/download/v0.1.0/";
        string metadata = JsonSerializer.Serialize(new { schema = 1, version = "99.0.0",
            sha256 = mismatch == "hash" ? "invalid" : new string('A', 64), size = mismatch == "size" ? 101L : 100L,
            url = prefix + (mismatch == "url" ? "other.exe" : "FM27Access-Installer.exe") });
        string releases = JsonSerializer.Serialize(new[] { new { draft = false, prerelease = false, assets = new[] {
            new { name = "FM27Access-Installer.json", browser_download_url = prefix + "FM27Access-Installer.json", size = 300L },
            new { name = "FM27Access-Installer.exe", browser_download_url = prefix + "FM27Access-Installer.exe", size = 100L } } } });
        using var download = new DownloadService(new Handler(request => request.RequestUri!.Host == "api.github.com" ? releases : metadata));
        await Assert.ThrowsAsync<InvalidDataException>(() => new InstallerUpdateService(download, f.Root).FindAsync(default));
        Assert.Empty(Directory.GetFiles(f.Root, "installer-update-*"));
    }
}
