using System.Diagnostics;

namespace FM27Access.Installer;

internal sealed class InstallerForm : Form
{
    private readonly string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FM27Access/Installer");
    private readonly DownloadService download = new();
    private readonly GameDiscoveryService discovery = new();
    private readonly InstallationStateService state = new();
    private readonly InstallationService installation = new();
    private readonly LocalizationService text;
    private readonly LoggingService log;
    private readonly GitHubReleaseService releases;
    private readonly ComboBox language = new(), game = new(), version = new();
    private readonly CheckBox previews = new();
    private readonly Label languageLabel = new(), gameLabel = new(), versionLabel = new(), progressLabel = new();
    private readonly LiveStatus status = new();
    private readonly TextBox information = new();
    private readonly ProgressBar overall = new(), current = new();
    private readonly Button browse = new(), refresh = new(), primary = new(), localPackage = new(), repair = new(), remove = new(), guide = new(), selfUpdate = new(), cancel = new(), close = new(), recover = new();
    private ReleaseCatalog? catalog;
    private InstallerUpdate? installerUpdate;
    private sealed record VersionChoice(ModRelease? Release, DistributionManifest? Local, string? Zip)
    {
        public string Version => Release?.Version ?? Local!.Version;
        public override string ToString() => Version + (Local is null ? "" : " (ZIP)");
    }
    private DistributionManifest? selectedManifest;
    private CancellationTokenSource? operation;
    private bool busy, applyingLanguage;
    private int announcedBucket = -1;
    private string lastStage = "";
    private readonly CancellationTokenSource lifetime = new();

    public InstallerForm()
    {
        text = new(cache); log = new(cache); releases = new(download, cache);
        string localGuide = Path.Combine(cache, "FM27Access-UserGuide.html");
        string originPath = Path.Combine(cache, "guide-origin.json");
        bool seedGuide = !File.Exists(localGuide) || !File.Exists(originPath);
        if (!seedGuide)
            try { var origin = Distribution.Read<GuideOrigin>(originPath); seedGuide = origin.Source == "bundled" && origin.Sha256 == Distribution.Hash(localGuide); }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { seedGuide = true; }
        if (seedGuide)
        {
            Directory.CreateDirectory(cache);
            using var input = typeof(InstallerForm).Assembly.GetManifestResourceStream("FM27Access.Installer.UserGuide.html")!;
            using (var output = File.Create(localGuide)) input.CopyTo(output);
            Distribution.Save(originPath, new GuideOrigin("bundled", Distribution.Hash(localGuide)));
        }
        MinimumSize = new(780, 670); Size = new(920, 760);
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
        Build(); ApplyLanguage();
        language.DataSource = text.Languages.ToArray(); language.SelectedItem = text.Languages.First(l => l.Code == text.Code);
        language.SelectedIndexChanged += (_, _) => { if (!applyingLanguage && language.SelectedItem is Language l) { text.Set(l.Code); ApplyLanguage(); } };
        game.SelectedIndexChanged += (_, _) => UpdateState();
        version.SelectedIndexChanged += (_, _) => { selectedManifest = null; UpdateState(); };
        previews.CheckedChanged += (_, _) => BindVersions();
        Shown += async (_, _) => await Run(async ct =>
        {
            SetStatus(text["checking"]);
            var games = await Task.Run(() => discovery.Discover(log.Write), ct);
            foreach (var found in games) game.Items.Add(found);
            if (game.Items.Count > 0) game.SelectedIndex = 0;
            await Refresh(ct);
        });
        FormClosing += (_, e) =>
        { if (busy) { e.Cancel = true; operation?.Cancel(); SetStatus(text["cancelling"]); } else lifetime.Cancel(); };
    }
    private void Build()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new(16), ColumnCount = 1, RowCount = 13 };
        for (int i = 0; i < 13; i++) layout.RowStyles.Add(new(i == 8 ? SizeType.Percent : SizeType.AutoSize, i == 8 ? 100 : 0));
        Controls.Add(layout);
        foreach (var combo in new[] { language, game, version }) { combo.DropDownStyle = ComboBoxStyle.DropDownList; combo.Dock = DockStyle.Fill; }
        foreach (var label in new[] { languageLabel, gameLabel, versionLabel, status, progressLabel }) { label.AutoSize = true; label.MaximumSize = new(850, 0); }
        layout.Controls.Add(languageLabel); layout.Controls.Add(language);
        layout.Controls.Add(gameLabel); layout.Controls.Add(Row(game, browse));
        layout.Controls.Add(versionLabel); layout.Controls.Add(Row(version, refresh));
        previews.AutoSize = true; layout.Controls.Add(previews);
        layout.Controls.Add(status);
        information.Multiline = true; information.ReadOnly = true; information.ScrollBars = ScrollBars.Vertical; information.Dock = DockStyle.Fill;
        layout.Controls.Add(information);
        overall.Dock = current.Dock = DockStyle.Top; layout.Controls.Add(progressLabel); layout.Controls.Add(overall); layout.Controls.Add(current);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        foreach (var button in new[] { primary, localPackage, repair, remove, refresh, guide, selfUpdate, recover, cancel, close })
        {
            if (button == refresh) continue;
            button.AutoSize = true; button.MinimumSize = new(90, 32); buttons.Controls.Add(button);
        }
        layout.Controls.Add(buttons);
        browse.AutoSize = refresh.AutoSize = true;
        browse.Click += async (_, _) => await Run(async ct =>
        {
            using var dialog = new FolderBrowserDialog { Description = text["game"], UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                var found = await Task.Run(() => discovery.Validate(dialog.SelectedPath), ct);
                if (!game.Items.Cast<GameInstallation>().Any(g => g.Path.Equals(found.Path, StringComparison.OrdinalIgnoreCase))) game.Items.Add(found);
                game.SelectedItem = game.Items.Cast<GameInstallation>().First(g => g.Path.Equals(found.Path, StringComparison.OrdinalIgnoreCase));
            }
        });
        refresh.Click += async (_, _) => await Run(Refresh);
        primary.Click += async (_, _) => await Run(ct => Install(false, ct));
        localPackage.Click += async (_, _) => await Run(async ct =>
        {
            if (game.SelectedItem is not GameInstallation selected) return;
            using var dialog = new OpenFileDialog { Filter = "FM27Access-Manifest.json|FM27Access-Manifest.json", CheckFileExists = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var manifest = Distribution.Read<DistributionManifest>(dialog.FileName); Distribution.Validate(manifest);
            var old = state.Read(selected.Path);
            if (InstallationStateService.Action(old?.Version, manifest.Version) == "downgrade" && !Confirm("confirmDowngrade")) return;
            await InstallPackage(selected, manifest, Path.Combine(Path.GetDirectoryName(dialog.FileName)!, manifest.Package), null, ct);
        });
        repair.Click += async (_, _) => await Run(ct => Install(true, ct));
        remove.Click += async (_, _) =>
        {
            if (game.SelectedItem is not GameInstallation selected || !Confirm("confirmRemove")) return;
            await Run(async ct => { await installation.UninstallAsync(selected.Path, Progress(), ct); SetStatus(text["success"]); UpdateState(); });
        };
        guide.Click += async (_, _) => await Run(async ct =>
        {
            var result = await new DocumentationService(download, cache).GetAsync(ct);
            Process.Start(new ProcessStartInfo(new Uri(result.Path).AbsoluteUri + "?lang=" + Uri.EscapeDataString(text.Code)) { UseShellExecute = true });
            if (!result.Online) SetStatus(text["offline"]);
        });
        selfUpdate.Click += async (_, _) => await Run(async ct =>
        {
            var updater = new InstallerUpdateService(download, cache);
            var update = installerUpdate ?? await updater.FindAsync(ct);
            if (update is null) { SetStatus(text["noInstallerUpdate"]); return; }
            if (!Confirm("confirmUpdate")) return;
            await updater.StageAsync(update, Progress(), ct);
            BeginInvoke(() => { busy = false; Close(); });
        });
        recover.Click += async (_, _) => await Run(async ct =>
        { if (game.SelectedItem is GameInstallation selected) { await installation.RecoverAsync(selected.Path, ct); SetStatus(text["success"]); UpdateState(); } });
        cancel.Click += (_, _) => { operation?.Cancel(); cancel.Enabled = false; SetStatus(text["cancelling"]); };
        close.Click += (_, _) => Close();
        CancelButton = close;
    }
    private static TableLayoutPanel Row(Control control, Button button)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        row.ColumnStyles.Add(new(SizeType.Percent, 100)); row.ColumnStyles.Add(new(SizeType.AutoSize));
        row.Controls.Add(control); row.Controls.Add(button); return row;
    }
    private void ApplyLanguage()
    {
        applyingLanguage = true;
        Text = AccessibleName = text["title"];
        RightToLeft = text.Code == "ar" ? RightToLeft.Yes : RightToLeft.No; RightToLeftLayout = text.Code == "ar";
        languageLabel.Text = language.AccessibleName = text["language"];
        gameLabel.Text = game.AccessibleName = text["game"];
        versionLabel.Text = version.AccessibleName = text["version"];
        previews.Text = text["previews"]; information.AccessibleName = text["summary"];
        localPackage.Text = localPackage.AccessibleName = text["install"] + " ZIP…";
        overall.AccessibleName = text["overall"]; current.AccessibleName = text["current"];
        foreach (var (button, key) in new[] { (browse, "browse"), (refresh, "refresh"), (repair, "repair"), (remove, "uninstall"), (guide, "guide"),
            (selfUpdate, "selfUpdate"), (cancel, "cancel"), (close, "close"), (recover, "recover") }) button.Text = button.AccessibleName = text[key];
        applyingLanguage = false; UpdateState();
    }
    private void BindVersions()
    {
        string? previous = (version.SelectedItem as VersionChoice)?.Version;
        version.Items.Clear();
        foreach (var r in catalog?.Releases.Where(r => previews.Checked || !r.Prerelease) ?? []) version.Items.Add(new VersionChoice(r, null, null));
        string folder = Path.GetDirectoryName(Environment.ProcessPath!)!;
        string manifestFile = Path.Combine(folder, "FM27Access-Manifest.json");
        if (File.Exists(manifestFile))
            try
            {
                var local = Distribution.Read<DistributionManifest>(manifestFile); Distribution.Validate(local);
                string zip = Path.Combine(folder, local.Package);
                if (File.Exists(zip)) version.Items.Add(new VersionChoice(null, local, zip));
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { log.Write("Local package: " + ex.Message); }
        if (version.Items.Count > 0)
            version.SelectedIndex = Math.Max(0, version.Items.Cast<VersionChoice>().ToList().FindIndex(r => r.Version == previous));
        UpdateState();
    }
    private async Task Refresh(CancellationToken ct)
    {
        SetStatus(text["checking"]);
        try { catalog = await releases.GetAsync(ct); }
        catch (HttpRequestException ex) { log.Write(ex.Message); catalog = new([], false, DateTimeOffset.UtcNow); }
        BindVersions();
        SetStatus(!catalog.Online ? text["offline"] : version.Items.Count == 0 ? text["noReleases"] : text["ready"]);
        if (catalog.Online)
            try
            {
                installerUpdate = await new InstallerUpdateService(download, cache).FindAsync(ct);
                selfUpdate.Text = text["selfUpdate"] + (installerUpdate is null ? "" : " (" + installerUpdate.Version + ")");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException) { log.Write("Installer update check: " + ex.Message); }
    }
    private void UpdateState()
    {
        try
        {
            var selected = game.SelectedItem as GameInstallation;
            var installed = selected is null ? null : state.Read(selected.Path);
            var choice = version.SelectedItem as VersionChoice;
            var release = choice?.Release;
            string action = choice is null ? "install" : InstallationStateService.Action(installed?.Version, choice.Version);
            primary.Text = primary.AccessibleName = text[action];
            primary.Enabled = !busy && selected is not null && choice is not null;
            localPackage.Enabled = !busy && selected is not null;
            repair.Enabled = remove.Enabled = !busy && installed is not null;
            repair.Enabled &= version.Items.Cast<VersionChoice>().Any(r => r.Version == installed?.Version);
            recover.Enabled = !busy && selected is not null && File.Exists(InstallationStateService.StatePath(selected.Path, "journal.json"));
            if (recover.Enabled) primary.Enabled = localPackage.Enabled = repair.Enabled = remove.Enabled = false;
            string? latest = catalog?.Releases.FirstOrDefault(r => !r.Prerelease)?.Version;
            information.Text = string.Join(Environment.NewLine, new[] {
                selected?.ToString() ?? text["notFound"],
                text["installed"] + ": " + (installed?.Version ?? (selected is null ? null : InstallationStateService.ExistingVersion(selected.Path)) ?? text["none"]),
                installed is null && selected is not null && InstallationStateService.ExistingVersion(selected.Path) is not null ? text["unmanaged"] : "",
                (catalog?.Online == false ? text["cached"] : text["latest"]) + ": " + (latest ?? text["none"]),
                release is null ? "" : release.Version + " — " + release.Published.ToLocalTime().ToString("d"), release?.Notes ?? "",
                "" });
        }
        catch (Exception ex) { log.Write(ex.ToString()); primary.Enabled = repair.Enabled = remove.Enabled = false; SetStatus(text["failed"]); }
    }
    private async Task Install(bool installedVersion, CancellationToken ct)
    {
        if (game.SelectedItem is not GameInstallation selected) return;
        var installed = state.Read(selected.Path);
        var choice = installedVersion ? version.Items.Cast<VersionChoice>().First(r => r.Version == installed!.Version) : version.SelectedItem as VersionChoice;
        if (choice is null) return;
        string action = InstallationStateService.Action(installed?.Version, choice.Version);
        if (action == "downgrade" && !Confirm("confirmDowngrade")) return;
        BepInExService.AssertGameClosed(selected.Path);
        if (choice.Local is not null) { await InstallPackage(selected, choice.Local, choice.Zip, null, ct); return; }
        var release = choice.Release!;
        selectedManifest = await releases.ManifestAsync(release, ct);
        await InstallPackage(selected, selectedManifest, null, release, ct);
    }
    private async Task InstallPackage(GameInstallation selected, DistributionManifest manifest, string? localZip, ModRelease? release, CancellationToken ct)
    {
        if (!manifest.Platforms.Contains(selected.Platform)) throw new InvalidDataException("Unsupported package platform: " + selected.Platform);
        string work = Path.Combine(cache, "work-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        try
        {
            SetStatus(text["prepare"]);
            string zip = localZip ?? Path.Combine(work, manifest.Package);
            if (release is not null) await download.DownloadAsync(release.Package.Url, zip, 2L * 1024 * 1024 * 1024, manifest.PackageSha256, manifest.PackageSize, Progress(), ct);
            string staged = Path.Combine(work, "staged");
            await Task.Run(() => InstallationService.ExtractAsync(zip, staged, manifest, Progress(), ct), ct);
            await installation.InstallAsync(selected.Path, staged, manifest, Progress(), ct);
            overall.Value = 5; current.Value = 100;
            SetStatus(text[BepInExService.NeedsPreparation(selected.Path) ? "pending" : "success"]);
            UpdateState();
        }
        finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
    }
    private IProgress<OperationProgress> Progress() => new Progress<OperationProgress>(p =>
    {
        int stage = p.Stage switch { "download" => 1, "extract" => 2, "loader" or "mod" => 3, "verify" => 4, _ => 0 };
        overall.Maximum = 5; overall.Value = stage;
        overall.AccessibleDescription = $"{stage}/5";
        long total = p.TotalBytes ?? p.Total, done = p.TotalBytes is not null ? p.Bytes : p.Completed;
        current.Style = total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
        int percent = total > 0 ? (int)Math.Clamp(done * 100d / total, 0, 100) : 0;
        current.Value = percent; progressLabel.Text = text[p.Stage] + (total > 0 ? $" {percent}%" : "");
        int bucket = percent / 10;
        if (lastStage != p.Stage || bucket != announcedBucket) { lastStage = p.Stage; announcedBucket = bucket; SetStatus(progressLabel.Text); }
    });
    private bool Confirm(string key) => MessageBox.Show(this, text[key], text["title"], MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    private void SetStatus(string value) { status.Text = value; status.Announce(); }
    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (busy) return;
        var focus = ActiveControl; busy = true;
        overall.Value = current.Value = 0; announcedBucket = -1; lastStage = "";
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); operation = source;
        foreach (var control in new Control[] { language, game, version, previews, browse, refresh, primary, localPackage, repair, remove, guide, selfUpdate, recover }) control.Enabled = false;
        cancel.Enabled = true;
        try { await action(source.Token); }
        catch (OperationCanceledException) { SetStatus(text["cancelled"]); }
        catch (Exception ex)
        {
            log.Write(ex.ToString());
            string key = ex.Message == "gameRunning" ? "gameRunning" : ex is UnauthorizedAccessException ? "permission" : ex is HttpRequestException ? "network" : "failed";
            SetStatus(text[key]); ShowError(text[key], ex);
        }
        finally
        {
            operation = null; busy = false; cancel.Enabled = false;
            foreach (var control in new Control[] { language, game, version, previews, browse, refresh, guide, selfUpdate }) control.Enabled = true;
            current.Style = ProgressBarStyle.Continuous; UpdateState();
            if (focus?.Enabled == true) focus.Focus();
        }
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { lifetime.Cancel(); lifetime.Dispose(); download.Dispose(); } base.Dispose(disposing); }
    private void ShowError(string message, Exception exception)
    {
        using var dialog = new Form { Text = text["error"], StartPosition = FormStartPosition.CenterParent, MinimumSize = new(600, 260), Size = new(720, 300), AutoScaleMode = AutoScaleMode.Dpi };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new(16), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize));
        var explanation = new Label { Text = message, AutoSize = true, MaximumSize = new(650, 0) };
        var diagnostic = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, AccessibleName = text["details"], Text = exception.ToString(), Visible = false };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var expand = new Button { Text = text["details"], AutoSize = true };
        var dismiss = new Button { Text = text["close"], AutoSize = true, DialogResult = DialogResult.Cancel };
        expand.Click += (_, _) => { diagnostic.Visible = !diagnostic.Visible; dialog.Height = diagnostic.Visible ? 520 : 300; if (diagnostic.Visible) diagnostic.Focus(); };
        buttons.Controls.Add(expand); buttons.Controls.Add(dismiss);
        layout.Controls.Add(explanation); layout.Controls.Add(diagnostic); layout.Controls.Add(buttons); dialog.Controls.Add(layout);
        dialog.CancelButton = dismiss; dialog.AcceptButton = dismiss; dialog.Shown += (_, _) => dismiss.Focus(); dialog.ShowDialog(this);
    }
    private sealed class LiveStatus : Label
    {
        public LiveStatus() { AccessibleRole = AccessibleRole.StaticText; LiveSetting = System.Windows.Forms.Automation.AutomationLiveSetting.Polite; }
        public void Announce() => AccessibilityObject.RaiseLiveRegionChanged();
    }
}
