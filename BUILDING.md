<!-- Current source packaging and retention -->

For a source-only workspace, run `./scripts/clean-workspace.ps1` after consolidating
current local verification into `artifacts/current-verification.json`. `-WhatIf`
previews the recognized generated targets. It removes build outputs, packages,
temporary evidence and retired diagnostic/SDK8 caches, while retaining source,
Git, FM26 baseline, the pinned SDK10, required Prism runtime/legal notices and
installation receipts. It never selects game saves or installed game files.
Safety fixtures are included in `scripts/test.ps1`. After dependency cleanup,
restore UI-test dependencies with `npm ci --prefix tests/companion`.

Generated outputs remain local. scripts/package-source.ps1 exports only the
publication source roots and respects Git exclusions, including .gitignore itself
and the supplied Prism runtime/notice exception. scripts/package-player.ps1 builds
a Release candidate. Each successful packaging command retains only its latest
folder/ZIP pair. scripts/clean-local-artifacts.ps1 removes inactive staging and
keeps the active receipt/payload and one latest recovery backup; source install
and confirmed reload invoke it automatically. Saves and personal data are excluded.
See docs/PRODUCTION_RELEASE.md for current qualification; a ZIP is not a release.

# Building FM27 Access

Use PowerShell 7 on Windows. The local SDK version is pinned by global.json.

    ./scripts/setup.ps1
    ./scripts/restore.ps1
    ./scripts/build.ps1 -GamePath 'C:/Program Files (x86)/Steam/steamapps/common/Football Manager 27'

Use the actual Steam library path if the game is installed elsewhere. Explicit
`-GamePath` and `FM27_GAME_DIR` also retain support for compatible Xbox installations;
the scripts do not require an Xbox path. The current compiler baseline is Steam
build 26577676, inspected locally with Unity 6000.3.18f1 and metadata 39.

The game must already have a compatible BepInEx installation and generated FM27
interop assemblies. You can also set FM27_GAME_DIR or pass -p:FM27GameDir to dotnet.
The five-project solution requires these native references. Builds never deploy.

GitHub-hosted builds use an exact private reference bundle pinned in
`packaging/build-references.json`; the build does not need your computer or a
game installation on the runner. The normal developer build detects changed
game/reference hashes. After a game update, let BepInEx regenerate interop, then
run `./scripts/update-build-references.ps1 -GamePath '<updated game root>'`.
It updates the private bundle and public pin together. Test and commit the
adaptation/pin before a release. See [distribution](docs/DISTRIBUTION.md).
For maintainer builds, `git config --local fm27.autoUpdateBuildReferences true`
enables that refresh automatically when references change. This operates only
while the normal build command runs and installs no background executor.

Projects, the solution and build/install scripts default to Release with
`FM27DeveloperTools=false`. Debug alone also leaves developer tools disabled.
Release rejects an attempt to enable them. Production omits evaluator/probe/reload
routes, DOM reports, startup self-tests, trace recorders and debug symbols; normal
accessibility commands, health checks and actionable error reporting remain.
The companion disables WebView developer tools. Source and player installers
apply the shared production BepInEx profile: console off, disk Fatal/Error/Warning,
with a separate hash-guarded restoration receipt.

For explicit local investigation only:

    ./scripts/build.ps1 -Configuration Debug -DeveloperTools -GamePath '<game root>'

That opt-in build requires a locally supplied compatible `lib/mcs.dll`, excluded
from Git and source/player archives. See lib/mcs-LICENSE.txt and the third-party notices.
BepInEx, generated interop and game files are build prerequisites, never public
source repository contents. Player downloads belong in GitHub Releases.

Game-independent checks:

    ./scripts/test.ps1
    ./.tools/dotnet/dotnet.exe build tools/game-inspect

The companion builds separately:

    ./.tools/dotnet/dotnet.exe publish FM27Access.CompanionApp -c Release

Run ./scripts/diagnose.ps1 -GamePath '<game root>' to inspect identity, architecture,
Unity, metadata and binary fingerprints. Inspection does not modify the game.
See [developer deployment](docs/DEVELOPER_INSTALLATION.md) for source receipts
and core reload, and [distribution](docs/DISTRIBUTION.md) for complete packages.
