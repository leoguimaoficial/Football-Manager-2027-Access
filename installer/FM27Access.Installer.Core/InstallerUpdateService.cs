using System.Diagnostics;
using System.Text.Json;

namespace FM27Access.Installer;

public sealed record InstallerUpdate(int Schema, string Version, string Sha256, long Size, Uri Url);
public sealed record InstallerUpdateReceipt(string PreviousHash, string CurrentHash, string HelperName, string HelperHash);
public sealed class InstallerUpdateService(DownloadService download, string cache)
{
    public async Task<InstallerUpdate?> FindAsync(CancellationToken ct)
    {
        string temp = Path.Combine(cache, "installer-update-" + Guid.NewGuid() + ".json");
        try
        {
            InstallerUpdate? newest = null;
            for (int page = 1; page <= 100; page++)
            {
                var releases = await download.JsonAsync<JsonElement[]>(new($"https://api.github.com/repos/{Distribution.Repository}/releases?per_page=100&page={page}"), temp, ct);
                foreach (var release in releases)
                {
                    if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
                    var assets = release.GetProperty("assets").EnumerateArray().ToArray();
                    var asset = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == "FM27Access-Installer.json");
                    if (asset.ValueKind == JsonValueKind.Undefined) continue;
                    var manifest = await download.JsonAsync<InstallerUpdate>(new(asset.GetProperty("browser_download_url").GetString()!), temp, ct);
                    var executable = assets.SingleOrDefault(a => a.GetProperty("name").GetString() == "FM27Access-Installer.exe");
                    if (manifest.Schema != 1 || !Distribution.IsHash(manifest.Sha256) || manifest.Size <= 0 || manifest.Size > 256 * 1024 * 1024
                        || executable.ValueKind == JsonValueKind.Undefined || executable.GetProperty("size").GetInt64() != manifest.Size
                        || executable.GetProperty("browser_download_url").GetString() != manifest.Url.AbsoluteUri)
                        throw new InvalidDataException("Invalid installer update metadata.");
                    DownloadService.ValidateOrigin(manifest.Url);
                    if (SemVersion.Parse(manifest.Version).CompareTo(SemVersion.Parse(Distribution.EngineVersion)) > 0
                        && (newest is null || SemVersion.Parse(manifest.Version).CompareTo(SemVersion.Parse(newest.Version)) > 0)) newest = manifest;
                }
                if (releases.Length < 100) return newest;
            }
            throw new InvalidDataException("Installer catalog pagination limit reached.");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public async Task StageAsync(InstallerUpdate update, IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        string target = Environment.ProcessPath ?? throw new IOException("Missing installer executable path.");
        if (!Path.GetFileName(target).Equals("FM27Access-Installer.exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Run the published FM27Access-Installer.exe to update the installer.");
        string directory = Path.GetDirectoryName(target)!;
        // Same-volume staging permits an atomic replacement without elevating or running scripts.
        string staged = Path.Combine(directory, "FM27Access-Installer.update-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            await download.DownloadAsync(update.Url, staged, 256 * 1024 * 1024, update.Sha256, update.Size, progress, ct);
            using var parent = Process.GetCurrentProcess();
            var start = new ProcessStartInfo(staged) { UseShellExecute = false, WorkingDirectory = directory };
            foreach (string arg in new[] { "--apply-update", target, update.Sha256, parent.Id.ToString(), parent.StartTime.ToUniversalTime().Ticks.ToString(), Distribution.Hash(target) })
                start.ArgumentList.Add(arg);
            Process.Start(start)?.Dispose();
        }
        catch { if (File.Exists(staged)) File.Delete(staged); throw; }
    }
    public static void Apply(string[] args)
    {
        if (args.Length != 6 || !Distribution.IsHash(args[2]) || !Distribution.IsHash(args[5])) throw new InvalidDataException("Invalid update arguments.");
        string self = Environment.ProcessPath!, target = Path.GetFullPath(args[1]);
        if (!Path.GetDirectoryName(target)!.Equals(Path.GetDirectoryName(self), StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(target) != "FM27Access-Installer.exe" || !Path.GetFileName(self).StartsWith("FM27Access-Installer.update-", StringComparison.Ordinal)
            || (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0 || !Distribution.Hash(self).Equals(args[2], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update location or checksum mismatch.");
        try
        {
            using var process = Process.GetProcessById(int.Parse(args[3]));
            if (process.StartTime.ToUniversalTime().Ticks != long.Parse(args[4])) throw new InvalidDataException("Update parent identity changed.");
            if (!process.WaitForExit(60000)) throw new IOException("Close the installer before applying the update.");
        }
        catch (ArgumentException) { /* The specific parent has already exited. */ }
        if (!Distribution.Hash(target).Equals(args[5], StringComparison.OrdinalIgnoreCase)) throw new IOException("Installer changed; update preserved.");
        string backup = target + ".previous";
        string replacement = target + ".replacement";
        string receiptPath = target + ".update.json";
        if (File.Exists(backup))
        {
            var previous = Distribution.Read<InstallerUpdateReceipt>(receiptPath);
            if (!Distribution.Hash(backup).Equals(previous.PreviousHash, StringComparison.OrdinalIgnoreCase)
                || !Distribution.Hash(target).Equals(previous.CurrentHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Previous installer backup changed; preserved for inspection.");
        }
        if (File.Exists(replacement)) throw new IOException("Pending installer replacement preserved.");
        File.Copy(self, replacement, false);
        byte[]? oldReceipt = File.Exists(receiptPath) ? File.ReadAllBytes(receiptPath) : null;
        bool replaced = false;
        try
        {
            File.Replace(replacement, target, backup);
            replaced = true;
            Distribution.Save(receiptPath, new InstallerUpdateReceipt(args[5], args[2], Path.GetFileName(self), args[2]));
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch
        {
            if (replaced && Distribution.Hash(target).Equals(args[2], StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(backup, target, true);
                if (oldReceipt is null) { if (File.Exists(receiptPath)) File.Delete(receiptPath); }
                else File.WriteAllBytes(receiptPath, oldReceipt);
            }
            throw;
        }
        finally { if (File.Exists(replacement)) File.Delete(replacement); }
        // Keep the previous binary for recovery. A running helper cannot delete its own executable.
    }
    public static void CleanupCompletedHelper()
    {
        string? self = Environment.ProcessPath;
        if (self is null || Path.GetFileName(self) != "FM27Access-Installer.exe" || !File.Exists(self + ".update.json")) return;
        try
        {
            var receipt = Distribution.Read<InstallerUpdateReceipt>(self + ".update.json");
            if (!System.Text.RegularExpressions.Regex.IsMatch(receipt.HelperName, @"^FM27Access-Installer\.update-[a-f0-9]{32}\.exe$")
                || !Distribution.Hash(self).Equals(receipt.CurrentHash, StringComparison.OrdinalIgnoreCase)) return;
            string helper = Distribution.Target(Path.GetDirectoryName(self)!, receipt.HelperName);
            if (File.Exists(helper) && Distribution.Hash(helper).Equals(receipt.HelperHash, StringComparison.OrdinalIgnoreCase)) File.Delete(helper);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { /* A still-running helper can be cleaned on the next launch. */ }
    }
}
