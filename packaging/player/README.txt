FM27 Access 0.1.0 - production candidate (Windows x64)

This is a qualification package, not a declaration of complete career coverage.
Compatible Xbox/Steam build labels are accepted through the required native API
contract, rather than a version/store allowlist. The game must be x64, Unity
6000.3.x, metadata 39, with the pinned BepInEx loader and fresh locally generated
interop. Reference native hashes record provenance; loader hashes stay exact.
Required type/member/layout/constant changes, stale interop and known mixed pairs
are refused. API acceptance does not prove gameplay on an untested build.
Steam gameplay and external clean first-install qualification remain pending.
Never share or copy game binaries/generated interop between players.

Prerequisites: PowerShell 7.2 or later, WebView2 Evergreen and the separately installed
BepInEx 6.0.0-be.793 Unity.IL2CPP win-x64 loader. No .NET development SDK is needed.
The companion includes .NET 10.0.15. Launch through your game's normal launcher.

1. Extract the whole archive to a separate folder outside the game installation.
2. Close Football Manager. Keep installation/removal receipts in a safe folder.
3. From PowerShell 7:
   ./install.ps1 -GamePath '<game root>' -ReceiptPath '<safe folder>/fm27-mod.json'
4. Start the game normally. The accessible companion opens automatically.

Existing files/mods are preserved; collisions and incompatible builds are refused.
Remove a previous mod using its original receipt before installing this candidate.
Do not delete other mods, change Windows permissions or reset the Xbox application.
The installer disables the BepInEx console and retains useful disk logs, backing
up the original logging configuration in a separate .logging.json receipt.

Removal, with the game closed:
   ./uninstall.ps1 -ReceiptPath '<safe folder>/fm27-mod.json'
Only unchanged receipt-owned files are removed. Generated logs remain. The exact
original BepInEx configuration is restored only if nobody changed it afterwards.
If a configuration changed, it is preserved and the receipt contains its backup.

Diagnostics, collected locally without uploading anything:
   ./collect-diagnostics.ps1 -GamePath '<game root>' -OutputPath '<new folder>'
Review the output before sharing. Do not send saves, Player.log or login details.
Developer eval, inspection, screenshot and reload HTTP routes are absent in Release.

GPL-3.0 source must accompany redistribution; see source and third-party notices.
This mod does not include Football Manager, proprietary assets or generated interop.
