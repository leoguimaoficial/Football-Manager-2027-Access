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
    ./scripts/build.ps1 -GamePath 'C:/XboxGames/Football Manager 27/Content'

The game must already have a compatible BepInEx installation and generated FM27
interop assemblies. You can also set FM27_GAME_DIR or pass -p:FM27GameDir to dotnet.
The five-project solution requires these native references. Builds never deploy.

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
BepInEx, generated interop and game files are local prerequisites, never source
repository contents. Distribution artifacts belong in future releases.

Game-independent checks:

    ./scripts/test.ps1
    ./.tools/dotnet/dotnet.exe build tools/game-inspect

The companion builds separately:

    ./.tools/dotnet/dotnet.exe publish FM27Access.CompanionApp -c Release

Run ./scripts/diagnose.ps1 -GamePath '<game root>' to inspect identity, architecture,
Unity, metadata and binary fingerprints. Inspection does not modify the game.
See [INSTALL](INSTALL.md) for receipt-managed deployment and core reload.
