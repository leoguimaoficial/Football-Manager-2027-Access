using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using FM27Access.Platform;
using FM27Access.Speech;

namespace FM27Access.Installer;

public sealed record InstalledFile(string Path, string Sha256, string Kind, bool Owned);
public sealed record InstallationReceipt(int Schema, string Version, InstalledFile[] Files, DateTimeOffset Installed);
public sealed record LoggingReceipt(bool Existed, byte[] Original, string AppliedHash);
public sealed record JournalEntry(string Path, string? BeforeHash, string? AfterHash, string Backup);
public sealed record TransactionJournal(int Schema, string Phase, byte[]? Receipt, byte[]? Logging, JournalEntry[] Entries);

public sealed class InstallationStateService
{
    public static string? ExistingVersion(string root)
    {
        string file = Distribution.Target(root, Distribution.PluginRoot + "FM27Access.Host.dll");
        return File.Exists(file) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(file).ProductVersion ?? "?" : null;
    }
    public const string StateDirectory = ".fm27access";
    public static string StatePath(string root, string name) => Distribution.Target(root, StateDirectory + "/" + name);
    public InstallationReceipt? Read(string root)
    {
        string path = StatePath(root, "installation.json");
        if (!File.Exists(path)) return null;
        var receipt = Distribution.Read<InstallationReceipt>(path);
        if (receipt.Schema != 1 || receipt.Files.Length == 0) throw new InvalidDataException("Invalid installation receipt.");
        SemVersion.Parse(receipt.Version);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in receipt.Files)
            if (!seen.Add(Distribution.Relative(f.Path)) || !Distribution.IsHash(f.Sha256) || (f.Kind == "mod" ? !Distribution.IsModPath(f.Path)
                : f.Kind != "loader" || !Distribution.IsLoaderPath(f.Path))) throw new InvalidDataException("Unsafe installation receipt.");
        return receipt;
    }
    public static string Action(string? installed, string selected) => installed is null ? "install"
        : SemVersion.Parse(selected).CompareTo(SemVersion.Parse(installed)) switch { > 0 => "update", < 0 => "downgrade", _ => "repair" };
    public string[] Verify(string root, InstallationReceipt receipt) => receipt.Files
        .Where(f => !File.Exists(Distribution.Target(root, f.Path)) || !Distribution.Hash(Distribution.Target(root, f.Path)).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
        .Select(f => f.Path).ToArray();
}

public sealed class BepInExService
{
    public static void ValidateGame(string root)
    {
        var report = InstallationInspector.Inspect(root);
        if (report.Architecture != "x64" || !report.UnityVersion.StartsWith("6000.3.", StringComparison.Ordinal) || report.MetadataVersion != 39)
            throw new InvalidDataException("Incompatible game architecture, Unity family or metadata.");
    }
    public static void AssertGameClosed(string root)
    {
        // Xbox can run through its WindowsApps alias. Conservatively block every fm process,
        // including an inaccessible process path, instead of assuming the configured root.
        foreach (string name in new[] { "fm", "gamelaunchhelper" })
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(name))
                using (process) { if (!process.HasExited) throw new InvalidOperationException("gameRunning"); }
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("FM27AccessCompanion"))
            using (process)
            {
                if (process.HasExited) continue;
                string? path;
                try { path = process.MainModule?.FileName; } catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("gameRunning"); }
                if (path is null || path.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("gameRunning");
            }
    }
    public static bool NeedsPreparation(string root) => !InteropGenerationState.IsCurrent(root);
    public static string LoggingText(string text)
    {
        foreach (var (section, key, value) in new[] { ("Logging.Console", "Enabled", "false"), ("Logging.Disk", "Enabled", "true"), ("Logging.Disk", "LogLevels", "Fatal, Error, Warning") })
        {
            var sections = Regex.Matches(text, @"(?ms)^\[" + Regex.Escape(section) + @"\][^\r\n]*(?:\r?\n|$).*?(?=^\[|\z)");
            if (sections.Count > 1) throw new InvalidDataException("Duplicate logging section.");
            if (sections.Count == 0) { text += $"\r\n[{section}]\r\n{key} = {value}\r\n"; continue; }
            var block = sections[0];
            var keys = Regex.Matches(block.Value, "(?m)^" + Regex.Escape(key) + @"[^\S\r\n]*=[^\r\n]*");
            if (keys.Count > 1) throw new InvalidDataException("Duplicate logging setting.");
            string updated = keys.Count == 0 ? block.Value.TrimEnd() + $"\r\n{key} = {value}\r\n"
                : block.Value.Remove(keys[0].Index, keys[0].Length).Insert(keys[0].Index, $"{key} = {value}");
            text = text.Remove(block.Index, block.Length).Insert(block.Index, updated);
        }
        return text;
    }
}

public sealed class InstallationService
{
    private readonly InstallationStateService state = new();
    private readonly Action<string> validateGame;
    private readonly Action<string> assertClosed;
    private readonly Action<int>? fault;
    private readonly Action<string, string>? validateFinal;
    public InstallationService() : this(BepInExService.ValidateGame, BepInExService.AssertGameClosed, null, (root, staged) =>
    {
        if (BepInExService.NeedsPreparation(root)) return;
        var result = NativeBuildGuard.Validate(root, Distribution.Target(staged, Distribution.PluginRoot + "native-build.json"));
        if (!result.Supported) throw new InvalidDataException(result.Reason);
    }) { }
    // Injectable boundaries let destructive/failure behavior be tested exclusively in temporary fixtures.
    public InstallationService(Action<string> validateGame, Action<string> assertClosed, Action<int>? fault = null, Action<string, string>? validateFinal = null)
    { this.validateGame = validateGame; this.assertClosed = assertClosed; this.fault = fault; this.validateFinal = validateFinal; }

    public static async Task ExtractAsync(string zip, string destination, DistributionManifest manifest, IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        Distribution.Validate(manifest);
        if (!Distribution.Hash(zip).Equals(manifest.PackageSha256, StringComparison.OrdinalIgnoreCase) || new FileInfo(zip).Length != manifest.PackageSize)
            throw new InvalidDataException("Package checksum/size mismatch.");
        using var archive = ZipFile.OpenRead(zip);
        var expected = manifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0; int count = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (++count > 20000) throw new InvalidDataException("ZIP entry limit exceeded.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                throw new InvalidDataException("Linked ZIP entry rejected.");
            if (entry.FullName.EndsWith('/')) { Distribution.Relative(entry.FullName.TrimEnd('/')); continue; }
            Distribution.Relative(entry.FullName);
            if (!seen.Add(entry.FullName) || !expected.TryGetValue(entry.FullName, out var file) || entry.Length != file.Size)
                throw new InvalidDataException("Unlisted, duplicate or malformed ZIP entry.");
            string target = Distribution.Target(destination, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await using (var input = entry.Open())
            {
                byte[] buffer = new byte[81920]; long written = 0; int n;
                while ((n = await input.ReadAsync(buffer, ct)) != 0)
                { written += n; if (written > file.Size) throw new InvalidDataException("ZIP expanded size mismatch."); await output.WriteAsync(buffer.AsMemory(0, n), ct); }
                if (written != file.Size) throw new InvalidDataException("Truncated ZIP entry.");
            }
            if (!Distribution.Hash(target).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Extracted file checksum mismatch.");
            total += file.Size;
            progress?.Report(new("extract", seen.Count, expected.Count, total, manifest.Files.Sum(f => f.Size)));
        }
        if (seen.Count != expected.Count) throw new InvalidDataException("Incomplete ZIP.");
    }

    public Task InstallAsync(string root, string staged, DistributionManifest manifest, IProgress<OperationProgress>? progress, CancellationToken ct) => Task.Run(() =>
    {
        Distribution.Validate(manifest); validateGame(root); assertClosed(root);
        using var lease = Lock(root);
        Recover(root);
        var old = state.Read(root);
        var oldFiles = old?.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, InstalledFile>(StringComparer.OrdinalIgnoreCase);
        var operations = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        List<InstalledFile> receiptFiles = [];
        var incoming = manifest.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            string source = Distribution.Target(staged, file.Path), target = Distribution.Target(root, file.Path);
            if (!File.Exists(source) || new FileInfo(source).Length != file.Size || !Distribution.Hash(source).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Staging integrity check failed.");
            if (file.Kind == "config")
            {
                string defaults = File.ReadAllText(source);
                if (BepInExService.LoggingText(defaults) != defaults) throw new InvalidDataException("Nonproduction default logging configuration.");
                continue;
            }
            bool owned = oldFiles.TryGetValue(file.Path, out var prior) && prior.Owned;
            if (File.Exists(target) && !owned)
            {
                if (file.Kind != "loader" || !Distribution.Hash(target).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Unmanaged file collision: " + file.Path);
                receiptFiles.Add(new(file.Path, file.Sha256, file.Kind, false)); continue;
            }
            // A loader shared with other mods must never be replaced by another build.
            if (File.Exists(target) && file.Kind == "loader" && !Distribution.Hash(target).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Incompatible existing loader preserved: " + file.Path);
            receiptFiles.Add(new(file.Path, file.Sha256, file.Kind, true));
            operations.Add(file.Path, source);
        }
        foreach (var file in oldFiles.Values.Where(f => f.Owned && f.Kind == "mod" && !incoming.Contains(f.Path)))
        {
            string target = Distribution.Target(root, file.Path);
            if (File.Exists(target) && !Distribution.Hash(target).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Modified obsolete file preserved: " + file.Path);
            operations.Add(file.Path, null);
        }
        // Keep ownership of loader files retired from a future manifest. They remain shared.
        receiptFiles.AddRange(oldFiles.Values.Where(f => f.Kind == "loader" && !incoming.Contains(f.Path)));
        string config = Distribution.Target(root, "BepInEx/config/BepInEx.cfg");
        byte[] original = File.Exists(config) ? File.ReadAllBytes(config) : [];
        string loggingState = InstallationStateService.StatePath(root, "logging.json");
        var logReceipt = File.Exists(loggingState) ? Distribution.Read<LoggingReceipt>(loggingState)
            : new LoggingReceipt(File.Exists(config), original, "");
        if (File.Exists(loggingState) && (!File.Exists(config) || !Distribution.Hash(config).Equals(logReceipt.AppliedHash, StringComparison.OrdinalIgnoreCase)))
        {
            File.Copy(loggingState, InstallationStateService.StatePath(root, "logging-history-" + Guid.NewGuid().ToString("N") + ".json"), false);
            logReceipt = new(File.Exists(config), original, "");
        }
        string configStage = Path.Combine(staged, "logging-production.cfg");
        File.WriteAllText(configStage, BepInExService.LoggingText(Encoding.UTF8.GetString(original)), new UTF8Encoding(false));
        operations.Add("BepInEx/config/BepInEx.cfg", configStage);
        logReceipt = logReceipt with { AppliedHash = Distribution.Hash(configStage) };
        ct.ThrowIfCancellationRequested(); assertClosed(root);
        Transact(root, operations, () =>
        {
            foreach (var file in manifest.Files.Where(f => f.Kind != "config"))
                if (!Distribution.Hash(Distribution.Target(root, file.Path)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Final integrity check failed.");
            validateFinal?.Invoke(root, staged);
            Distribution.Save(InstallationStateService.StatePath(root, "installation.json"), new InstallationReceipt(1, manifest.Version, receiptFiles.ToArray(), DateTimeOffset.UtcNow));
            Distribution.Save(loggingState, logReceipt);
        }, progress, ct);
    }, ct);

    public Task UninstallAsync(string root, IProgress<OperationProgress>? progress, CancellationToken ct) => Task.Run(() =>
    {
        validateGame(root); assertClosed(root);
        using var lease = Lock(root); Recover(root);
        var receipt = state.Read(root) ?? throw new InvalidOperationException("No managed installation.");
        Dictionary<string, string?> changes = new(StringComparer.OrdinalIgnoreCase);
        foreach (var file in receipt.Files.Where(f => f.Owned && f.Kind == "mod"))
        {
            string target = Distribution.Target(root, file.Path);
            if (File.Exists(target) && !Distribution.Hash(target).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Modified file preserved; repair before removal: " + file.Path);
            changes.Add(file.Path, null);
        }
        string logging = InstallationStateService.StatePath(root, "logging.json");
        string config = Distribution.Target(root, "BepInEx/config/BepInEx.cfg");
        string? configStage = null;
        if (File.Exists(logging))
        {
            var log = Distribution.Read<LoggingReceipt>(logging);
            if (File.Exists(config) && Distribution.Hash(config).Equals(log.AppliedHash, StringComparison.OrdinalIgnoreCase))
            {
                if (log.Existed) { configStage = InstallationStateService.StatePath(root, "logging-restore.cfg"); File.WriteAllBytes(configStage, log.Original); }
                changes.Add("BepInEx/config/BepInEx.cfg", configStage);
            }
        }
        try
        {
            ct.ThrowIfCancellationRequested(); assertClosed(root);
            Transact(root, changes, () =>
            {
                File.Delete(InstallationStateService.StatePath(root, "installation.json"));
                File.Delete(logging);
            }, progress, ct);
        }
        finally { if (configStage is not null && File.Exists(configStage)) File.Delete(configStage); }
        // Deliberately retain the shared loader and generated files/configuration, even when installed by us.
    }, ct);

    private static FileStream Lock(string root)
    {
        string path = InstallationStateService.StatePath(root, "operation.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private void Transact(string root, Dictionary<string, string?> changes, Action commit, IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        string journalPath = InstallationStateService.StatePath(root, "journal.json");
        string backupRoot = InstallationStateService.StatePath(root, "transaction");
        if (Directory.Exists(backupRoot)) throw new IOException("Unrecovered transaction files preserved.");
        long need = changes.Sum(pair => (File.Exists(Distribution.Target(root, pair.Key)) ? new FileInfo(Distribution.Target(root, pair.Key)).Length : 0)
            + (pair.Value is null ? 0 : new FileInfo(pair.Value).Length));
        if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace < need + 16 * 1024 * 1024) throw new IOException("Insufficient disk space.");
        byte[]? ReadState(string name) { string path = InstallationStateService.StatePath(root, name); return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        var preparing = new TransactionJournal(1, "preparing", ReadState("installation.json"), ReadState("logging.json"), []);
        Distribution.Save(journalPath, preparing);
        List<JournalEntry> entries = [];
        try
        {
            foreach (var pair in changes)
            {
                ct.ThrowIfCancellationRequested();
                string target = Distribution.Target(root, pair.Key);
                string backup = "transaction/" + entries.Count + ".bin";
                string? before = File.Exists(target) ? Distribution.Hash(target) : null;
                if (before is not null)
                {
                    string path = InstallationStateService.StatePath(root, backup);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Copy(target, path, false);
                }
                entries.Add(new(pair.Key, before, pair.Value is null ? null : Distribution.Hash(pair.Value), backup));
            }
        }
        catch { CleanupTransaction(root); throw; }
        var journal = preparing with { Phase = "applying", Entries = entries.ToArray() };
        Distribution.Save(journalPath, journal);
        try
        {
            int i = 0;
            foreach (var pair in changes)
            {
                ct.ThrowIfCancellationRequested(); assertClosed(root);
                string target = Distribution.Target(root, pair.Key);
                var entry = entries[i];
                string? current = File.Exists(target) ? Distribution.Hash(target) : null;
                if (current != entry.BeforeHash) throw new IOException("File changed during installation.");
                if (pair.Value is null) { if (File.Exists(target)) File.Delete(target); }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    // Copy to a sibling and atomically replace; the journal is durable before any destination changes.
                    string temporary = Distribution.Target(root, pair.Key + ".fm27access-new");
                    if (File.Exists(temporary)) throw new IOException("Pending destination file preserved.");
                    try { File.Copy(pair.Value, temporary, false); File.Move(temporary, target, true); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                i++; fault?.Invoke(i);
                progress?.Report(new(pair.Key.StartsWith(Distribution.PluginRoot, StringComparison.Ordinal) ? "mod" : "loader", i, changes.Count));
            }
            ct.ThrowIfCancellationRequested(); commit();
            progress?.Report(new("verify", 1, 1));
            Distribution.Save(journalPath, journal with { Phase = "committed" });
        }
        catch { Recover(root); throw; }
        CleanupTransaction(root);
    }
    public Task RecoverAsync(string root, CancellationToken ct) => Task.Run(() =>
    { ct.ThrowIfCancellationRequested(); validateGame(root); assertClosed(root); using var lease = Lock(root); Recover(root); }, ct);
    private static void Recover(string root)
    {
        string path = InstallationStateService.StatePath(root, "journal.json");
        if (!File.Exists(path)) return;
        var journal = Distribution.Read<TransactionJournal>(path);
        if (journal.Schema != 1 || journal.Phase is not ("preparing" or "applying" or "committed")) throw new InvalidDataException("Unknown recovery journal.");
        if (journal.Phase is "committed" or "preparing") { CleanupTransaction(root); return; }
        foreach (var entry in journal.Entries)
        {
            if (entry.Path != "BepInEx/config/BepInEx.cfg" && !Distribution.IsModPath(entry.Path) && !Distribution.IsLoaderPath(entry.Path))
                throw new InvalidDataException("Unsafe recovery target.");
            if (!Regex.IsMatch(entry.Backup, @"^transaction/\d+\.bin$") || (entry.BeforeHash is not null && !Distribution.IsHash(entry.BeforeHash))
                || (entry.AfterHash is not null && !Distribution.IsHash(entry.AfterHash))) throw new InvalidDataException("Invalid recovery entry.");
            string target = Distribution.Target(root, entry.Path);
            string? current = File.Exists(target) ? Distribution.Hash(target) : null;
            if (current != entry.BeforeHash && current != entry.AfterHash) throw new IOException("Recovery conflict; changed file preserved: " + entry.Path);
            if (entry.BeforeHash is not null && !Distribution.Hash(InstallationStateService.StatePath(root, entry.Backup)).Equals(entry.BeforeHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Recovery backup damaged; transaction preserved.");
        }
        foreach (var entry in journal.Entries.Reverse())
        {
            string target = Distribution.Target(root, entry.Path);
            if (entry.BeforeHash is null) { if (File.Exists(target)) File.Delete(target); }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string restored = InstallationStateService.StatePath(root, "recovery-tmp.bin");
                File.Copy(InstallationStateService.StatePath(root, entry.Backup), restored, true);
                File.Move(restored, target, true);
            }
        }
        Restore("installation.json", journal.Receipt); Restore("logging.json", journal.Logging);
        void Restore(string name, byte[]? bytes)
        { string target = InstallationStateService.StatePath(root, name); if (bytes is null) File.Delete(target); else File.WriteAllBytes(target, bytes); }
        CleanupTransaction(root);
    }
    private static void CleanupTransaction(string root)
    {
        string backup = InstallationStateService.StatePath(root, "transaction");
        if (Directory.Exists(backup))
        {
            // Validate every child before recursive cleanup, including malicious reparse points.
            foreach (var entry in Directory.EnumerateFileSystemEntries(backup, "*", SearchOption.AllDirectories))
                Distribution.Target(root, Path.GetRelativePath(root, entry).Replace('\\', '/'));
            Directory.Delete(backup, true);
        }
        File.Delete(InstallationStateService.StatePath(root, "journal.json"));
    }
}
