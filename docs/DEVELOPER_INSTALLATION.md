# Developer deployment and removal

The primary development profile is FM27 Steam build 26577676 with Unity
6000.3.18f1, metadata 39 and BepInEx 6.0.0-be.793. Compatible build/store variants are accepted by the
required API contract, x64/Unity6000.3.x/metadata39 checks, exact loader hashes and
fresh local interop. Reference game hashes record qualification rather than impose
a version allowlist. Actual Steam startup/main-menu actions pass through Doorstop's
supported `version.dll` proxy; Xbox retains its historical scoped evidence.
See [compatibility details](NATIVE_COMPATIBILITY.md) for current candidate limits.

Close the game before installing the loader, Host, Navigation, Speech, Prism or
the companion. The scripts recognize the Xbox package's WindowsApps process path.
Loader staging renames the pinned archive's `winhttp.dll` to `version.dll`
without changing its bytes. Never install both proxies. The universal installer
can repair its own old receipt; source scripts require removal through the
separate source-loader receipt before changing its loader payload. Do not pass
the universal installer's `.fm27access/installation.json` to `-ReplaceReceipt`.

    ./scripts/setup.ps1 -Loader
    ./scripts/install-loader.ps1 -GamePath '<game root>'
    ./scripts/install.ps1 -GamePath '<game root>'

Do not reinstall an existing loader. Receipts in artifacts/install record owned
paths and SHA-256 hashes. Existing files are never silently overwritten. When
upgrading, pass the previous mod receipt to install.ps1 -ReplaceReceipt; the
script validates it before removal. Keep the loader receipt separate.

The installed mod contains Host, Plugin, Navigation, Speech, the original
Prism binary, the companion and license notices. Panel profiles and client assets
are embedded in Plugin. Start the game normally; Host opens the companion.
The default is Release without developer diagnostics. Debug also keeps those
tools disabled unless `-DeveloperTools` is explicitly passed with
`-Configuration Debug`; only that opt-in payload includes the local mcs compiler.

## Player candidate package

Developers build the SDK-free player archive with:

    ./scripts/package-player.ps1 -GamePath '<qualified game root>'

Extract that archive outside the game. Its own README.txt and install.ps1 require
PowerShell 7.2+, an already installed qualified BepInEx loader/local generated
interop, WebView2 and a closed game. They require no development SDK. Loader
hashes stay exact; required interop type/member/signature/layout/constant contracts
exclude generated MVID/tokens/timestamps and allow additive API changes. Nothing proprietary is
bundled. The companion includes .NET 10.0.15. The package remains a candidate until
[release qualification](PRODUCTION_RELEASE.md) is satisfied.

The player installer saves a separate logging receipt before disabling the console
and retaining only Fatal/Error/Warning disk logs. Keep both receipts. Its uninstall.ps1 restores the original
configuration bytes only when the installed configuration remains unchanged.
Changed configurations are preserved for manual review. Source install.ps1 uses
the same production profile and separate receipt, except for an explicitly enabled
developer installation. scripts/uninstall-loader.ps1 restores that configuration
under the same hash guard. No installed configuration is changed by a build.

## Development reload

    ./scripts/reload.ps1 -Configuration Debug -DeveloperTools -GamePath '<game root>'

Only Plugin.dll is replaced. The script verifies the active receipt and permanent
assemblies, preserves a backup, updates the receipt, requests POST /reload through
the session-protected original server and checks the new module identity and
dispatcher. Host, Navigation and Speech changes require a cold install.
Use -GameClosed to stage a core update for the next game start.
Live reload requires a Debug installation. Release does not expose /reload.

For ongoing local screen refinements, preserve the current game and unsaved
setup. Inspect its actual localhost document and use this reload for embedded
profiles, adapters, walker/renderer and client assets. Keep the explicitly enabled
development runtime during that UI session instead of alternating Debug/Release
installations after each edit. If Release is already running, enabling reload
requires one receipt-aware closed-game installation first; restore the original
setup before continuing. Restart again only when permanent dependencies change
or another technical necessity is demonstrated. Keep one owned Companion/client,
restore it for the user's in-game test, and record the exact runtime/profile.
Shipped builds retain the normal Release/no-diagnostics defaults.

A Git commit can change AssemblyInformationalVersion and SourceLink/PDB metadata
even when permanent source is unchanged. Reproducing an existing development
runtime can require its recorded SourceRevisionId and SourceLink compiler inputs;
record the actual edited source separately. Keep the full permanent-file hash
comparison: metadata reproduction must not mask a real permanent code change.

## Removal

    ./scripts/uninstall-loader.ps1 -ReceiptPath '<mod receipt>'

With the game closed, removal validates every recorded hash before deleting owned
files. Modified files, generated data, saves, profiles and unrelated mods remain
untouched. Remove the loader separately using its own receipt if desired.
