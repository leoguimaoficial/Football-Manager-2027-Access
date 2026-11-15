# Universal installer and distribution

FM27 Access 0.1.0 is distributed through GitHub Releases. This document describes implementation
and build details; the [player guide](../docs/user/FM27Access-UserGuide.html)
provides the installation and gameplay instructions.
The user-facing program is always `FM27Access-Installer.exe`.
The mod version comes from `Directory.Build.props`; the independent installer
engine version comes from `InstallerVersion.props`. Neither project changes the
original five-project game solution.

The Reigns Access installer supplied by the maintainer informed the simple
native Windows interface and rolling release selector. Windows Forms is also
used by the existing Companion and exposes native controls through UI Automation.
There is no web frontend, Node runtime or administrator requirement in the installer.

## Build and tests

Use the SDK pinned in `global.json` and PowerShell 7.2 or later:

```powershell
./installer/build-installer.ps1
./.tools/dotnet/dotnet.exe test installer/FM27Access.Installer.Tests -c Release
node docs/user/build-guide.cjs
node --test tests/distribution/*.test.cjs
npm ci --prefix tests/companion
npx --prefix tests/companion playwright install chromium
node tests/distribution/guide-browser.cjs
```

The executable is written to ignored `artifacts/installer/`. It is x64,
self-contained and single-file, including .NET 10.0.15. WebView2 is needed by the
game's Companion, not the installer. The installer uses no GitHub tokens.
Its GPL source and dependency notices are publicly available. For independent
engine updates, `sourceCommit` in the installer metadata identifies the exact
repository source (also linked by the release). The mod source ZIP retains its
historical build identity and may contain an older installer engine.

## Runtime contracts

- `GitHubReleaseService` paginates public official releases. Drafts, invalid
  version tags and releases lacking both exact manifest/package assets are
  excluded. Prereleases are opt-in. Cache timestamps remain separate from
  successful online checks; offline data is never called current.
  A complete adjacent manifest/ZIP is also available as an explicitly marked
  local ZIP choice, including when no online release exists. It is not presented
  as an official GitHub release. Repair can use that same local version offline.
- `DistributionManifest` schema 1 binds mod version, minimum installer version,
  package size/SHA-256, loader version, platforms, compatibility and every file.
  `mod` files stay under `BepInEx/plugins/FM27Access/`; `loader` permits the
  pinned runtime/proxy paths only; `config` permits the production BepInEx
  defaults only. No generated game interop, game binaries, developer compiler,
  symbols, bookmarks or player configuration belong in that manifest.
- The pinned loader is the original verified
  `6.0.0-be.793+dbc82f0` Unity.IL2CPP win-x64 distribution, SHA-256 locked in
  `packaging/loader.lock.json`. It is a technically prerelease dependency, not
  the unpinned newest build. Production is enforced by configuration and the
  existing compiled mod checks, not by calling an incompatible loader stable.
- BepInEx console logging is disabled **before the first launch**. Disk logging
  retains Fatal/Error/Warning only. The same default is included in the manual
  ZIP. Existing config sections are merged, not replaced. A separate logging
  receipt restores unchanged settings; intervening user edits become the new
  baseline during later updates, with the old receipt retained as history.
- `GameDiscoveryService` reads Steam library manifests, Epic launcher manifests,
  Xbox GamingRoot/configuration and current-user package registration. It
  validates actual `fm.exe`, `app.info`, native x64, Unity family and metadata.
  Directory aliases are resolved before deduplication; inaccessible WindowsApps
  aliases are excluded. No ACL or package protection is changed.
- `InstallationService` revalidates the game and process guard; stages, hashes,
  locks and journals the operation; verifies final content; then commits its
  receipt. The original native API guard validates fresh existing interop.
  Fresh/stale interop requires the game's first normal loader run and the Host's
  fail-closed guard. No game is launched by installation.
- Every destination is relative, contained and checked for junctions/symlinks.
  ZIP links, traversal, duplicate paths, alternate streams, reserved names,
  unlisted files, corrupt files and excessive sizes are refused.
- `.fm27access/installation.json` records per-game ownership. A durable journal,
  backups and exclusive lock support rollback, safe cancellation and recovery
  after interruption. Recovery refuses conflicting user edits and preserves
  evidence. Uninstall deletes only unchanged receipt-owned mod files. The
  shared BepInEx loader, unknown files, settings, bookmarks and saves remain.
- An old source/player deployment is recognized by its assembly version but is
  not silently adopted. Its existing receipt must be used to remove that
  deployment with the game closed before installing through this manager.
- `DocumentationService` updates the canonical HTML from `main` independently
  of releases, with cache fallback and an embedded offline guide. The browser
  receives the selected language. No published binary asset is replaced.
- `InstallerUpdateService` discovers independent engine metadata in stable
  releases, verifies HTTPS/size/hash, stages a sibling executable and waits for
  the original process identity before replacing/restarting. The old executable
  is retained for recovery. Self-update is separate from mod installation.
  Every startup checks for an engine update, independently of the mod catalog's
  online/cache result. A newer engine opens a native Yes/No prompt after startup
  discovery finishes, with No selected by default. No update means no dialog.
  Declining keeps the available version on the update button; a refresh does
  not repeatedly prompt. The current engine version appears in the window title.

## Localization and accessibility

Twenty resource sets and matching HTML guides are included: English, Brazilian
and European Portuguese, Spanish and Latin American Spanish, French, German,
Italian, Dutch, Danish, Swedish, Norwegian Bokmål, Polish, Turkish, Russian,
Greek, Japanese, Korean, Simplified Chinese and Arabic. OS culture and saved
preference select the initial language; unsupported languages fall back to English.
Arabic uses RTL. Resource-key completeness and offline HTML behavior are tested.

The language inventory includes the native regional aliases already evidenced
by FM27 and the public FM26 language baseline, plus Brazilian Portuguese. A
complete official FM27 language inventory was not independently available in
the consulted public sources; equivalence to every FM27 distribution remains a
verification item. Translations need native-speaker review. This is not a claim
of publisher-provided translations or manual NVDA qualification.

The interface uses real combo boxes, buttons, read-only information, progress
bars, accessible names, native focus/disabled state, system colors and DPI
scaling. Progress announcements are limited to stage/10-percent changes.
Technical details appear only in an error dialog; there is no inert log button
on the main screen. See the testing record below for actual evidence limits.
The native read-only multiline Installer messages field is a Tab stop and keeps
recent progress, warnings and errors available for later reading. Error-dialog
explanations are Tab stops too. A standard Windows information dialog confirms
completed installation/update/repair and removal, then returns to the installer.
Startup discovery and version checks do not request automatic announcements or
restore focus a second time. An available engine update has its own native
confirmation dialog. User-initiated operations retain progress announcements.
The primary action becomes Repair for the installed mod version; the separate
Repair button is shown only when another version is selected. Release Markdown
is converted to plain text in the native read-only information field, retaining
paragraphs, bullets and link captions. Release authors must write player-facing
outcomes there; compiler, loader, hash and test details belong in developer docs.

## Distribution and publication

See [the release runbook](../docs/DISTRIBUTION.md) for exact artifacts, approval
setup, trusted build prerequisites and remaining qualification.
