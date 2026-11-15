using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FM27Access.Installer;

public sealed class DownloadService : IDisposable
{
    private readonly HttpClient client;
    public DownloadService(HttpMessageHandler? handler = null)
    {
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
        client.Timeout = TimeSpan.FromMinutes(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FM27Access-Installer/" + Distribution.EngineVersion);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }
    public static void ValidateOrigin(Uri uri)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("HTTPS origin required.");
        bool valid = uri.Host switch
        {
            "api.github.com" => uri.AbsolutePath.StartsWith("/repos/" + Distribution.Repository + "/", StringComparison.Ordinal),
            "github.com" => uri.AbsolutePath.StartsWith("/" + Distribution.Repository + "/releases/download/", StringComparison.Ordinal),
            "raw.githubusercontent.com" => uri.AbsolutePath.StartsWith("/" + Distribution.Repository + "/main/docs/user/", StringComparison.Ordinal),
            "release-assets.githubusercontent.com" => true,
            _ => false
        };
        if (!valid) throw new InvalidDataException("Untrusted download origin.");
    }
    public async Task DownloadAsync(Uri uri, string target, long limit, string? hash, long? expectedSize,
        IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        if (limit <= 0 || limit > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Invalid download limit.");
        HttpResponseMessage? response = null;
        try
        {
            for (int redirect = 0; redirect <= 5; redirect++)
            {
                ValidateOrigin(uri);
                response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    var next = response.Headers.Location ?? throw new HttpRequestException("Missing redirect.");
                    uri = next.IsAbsoluteUri ? next : new Uri(uri, next); response.Dispose(); response = null;
                    continue;
                }
                response.EnsureSuccessStatusCode(); break;
            }
            if (response is null) throw new HttpRequestException("Too many redirects.");
            long? total = response.Content.Headers.ContentLength;
            if (total > limit || (expectedSize is not null && total is not null && total != expectedSize))
                throw new InvalidDataException("Unexpected download size.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            await using (var output = new FileStream(target + ".part", FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            {
                var buffer = new byte[81920]; long received = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) != 0)
                {
                    received += count;
                    if (received > limit) throw new InvalidDataException("Download size limit exceeded.");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    progress?.Report(new("download", 0, 0, received, total ?? expectedSize));
                }
                if ((expectedSize is not null && received != expectedSize) || (total is not null && received != total))
                    throw new InvalidDataException("Truncated download.");
            }
            ct.ThrowIfCancellationRequested();
            if (hash is not null && (!Distribution.IsHash(hash) || !Distribution.Hash(target + ".part").Equals(hash, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Download checksum mismatch.");
            File.Move(target + ".part", target, true);
        }
        finally { response?.Dispose(); if (File.Exists(target + ".part")) File.Delete(target + ".part"); }
    }
    public async Task<T> JsonAsync<T>(Uri uri, string temp, CancellationToken ct)
    {
        await DownloadAsync(uri, temp, 8 * 1024 * 1024, null, null, null, ct);
        return Distribution.Read<T>(temp);
    }
    public void Dispose() => client.Dispose();
}

public sealed class GitHubReleaseService(DownloadService download, string cacheRoot)
{
    public async Task<ReleaseCatalog> GetAsync(CancellationToken ct)
    {
        string cache = Path.Combine(cacheRoot, "releases.json"), temp = Path.Combine(cacheRoot, Guid.NewGuid() + ".json");
        try
        {
            List<ModRelease> list = [];
            for (int page = 1; page <= 100; page++)
            {
                var entries = await download.JsonAsync<JsonElement[]>(new($"https://api.github.com/repos/{Distribution.Repository}/releases?per_page=100&page={page}"), temp, ct);
                foreach (var e in entries)
                {
                    if (e.GetProperty("draft").GetBoolean()) continue;
                    string tag = e.GetProperty("tag_name").GetString()!;
                    SemVersion version;
                    try { version = SemVersion.Parse(tag); } catch (Exception ex) when (ex is InvalidDataException or OverflowException) { continue; }
                    var assets = e.GetProperty("assets").EnumerateArray().Select(a => new ReleaseAsset(a.GetProperty("name").GetString()!,
                        new(a.GetProperty("browser_download_url").GetString()!), a.GetProperty("size").GetInt64(),
                        a.TryGetProperty("digest", out var digest) ? digest.GetString() : null)).ToArray();
                    var manifest = assets.SingleOrDefault(a => a.Name == "FM27Access-Manifest.json");
                    string value = tag.TrimStart('v');
                    var package = assets.SingleOrDefault(a => a.Name == $"FM27Access-v{value}.zip");
                    if (manifest is null || package is null) continue;
                    DownloadService.ValidateOrigin(manifest.Url); DownloadService.ValidateOrigin(package.Url);
                    list.Add(new(value, tag, e.GetProperty("published_at").GetDateTimeOffset(), e.GetProperty("body").GetString() ?? "",
                        e.GetProperty("prerelease").GetBoolean() || version.Pre.Length > 0, manifest, package));
                }
                if (entries.Length < 100) break;
                if (page == 100) throw new InvalidDataException("Release catalog pagination limit reached.");
            }
            var result = new ReleaseCatalog(list.OrderByDescending(r => SemVersion.Parse(r.Version)).ToArray(), true, DateTimeOffset.UtcNow);
            Distribution.Save(cache, result); return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            if (!File.Exists(cache)) throw;
            return Distribution.Read<ReleaseCatalog>(cache) with { Online = false };
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public async Task<DistributionManifest> ManifestAsync(ModRelease release, CancellationToken ct)
    {
        string path = Path.Combine(cacheRoot, Guid.NewGuid() + ".json");
        try
        {
            string? digest = release.Manifest.Digest?.StartsWith("sha256:", StringComparison.Ordinal) == true ? release.Manifest.Digest[7..] : null;
            await download.DownloadAsync(release.Manifest.Url, path, 8 * 1024 * 1024, digest, release.Manifest.Size, null, ct);
            var manifest = Distribution.Read<DistributionManifest>(path); Distribution.Validate(manifest, release.Version);
            if (manifest.PackageSize != release.Package.Size) throw new InvalidDataException("Package asset size mismatch.");
            return manifest;
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

public sealed record GuideOrigin(string Source, string Sha256);
public sealed class DocumentationService(DownloadService download, string cacheRoot)
{
    public async Task<(string Path, bool Online)> GetAsync(CancellationToken ct)
    {
        string file = Path.Combine(cacheRoot, "FM27Access-UserGuide.html");
        try
        {
            await download.DownloadAsync(new($"https://raw.githubusercontent.com/{Distribution.Repository}/main/docs/user/FM27Access-UserGuide.html"),
                file + ".download", 4 * 1024 * 1024, null, null, null, ct);
            string html = await File.ReadAllTextAsync(file + ".download", ct);
            if (!html.Contains("FM27 Access", StringComparison.Ordinal) || !html.Contains("<!doctype html>", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid user guide.");
            File.Move(file + ".download", file, true);
            Distribution.Save(Path.Combine(cacheRoot, "guide-origin.json"), new GuideOrigin("remote", Distribution.Hash(file)));
            return (file, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
        { if (!File.Exists(file)) throw; return (file, false); }
        finally { if (File.Exists(file + ".download")) File.Delete(file + ".download"); }
    }
}
