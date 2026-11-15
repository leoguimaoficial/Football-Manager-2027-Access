# Installing and managing FM27 Access

Use FM27Access-Installer.exe to install, update, repair or remove the mod. You
need Windows 10 or 11 x64, Football Manager 2027, a screen reader and Microsoft
Edge WebView2. The package includes .NET and BepInEx.

[Download the universal installer](https://github.com/leoguimaoficial/Football-Manager-2027-Access/releases/latest/download/FM27Access-Installer.exe).
The [release page](https://github.com/leoguimaoficial/Football-Manager-2027-Access/releases/tag/v0.1.0)
also provides the complete ZIP, its manifest and the standalone HTML manual.

## Install with the universal installer

Open the installer, select a
language, a detected installation (or browse to its `fm.exe` folder), and an
official stable mod version. The primary action becomes Install, Update, Repair
or Downgrade according to the installed version. Older versions require an
explicit confirmation. Repair uses the installed version's own official package.
Use Check for updates to refresh the catalog; offline results are marked as cached.
Use Tab to reach Installer messages and reread any progress, warning or error.
A standard Windows dialog confirms completion; press OK to return to the installer.
Opening the installer does not trigger an automatic status announcement. Use
the standard Windows controls to explore the installation and version information.

For a downloaded package, keep the versioned ZIP and `FM27Access-Manifest.json`
together and choose **Install ZIP**, then select the manifest. This offline path
uses the same integrity, compatibility, collision and receipt checks. For direct
manual copying, extract the ZIP outside the game, inspect its manifest and
compare every existing destination before merging; never overwrite unknown
loader or mod files. Direct copies are unmanaged and cannot be removed by the
manager without an installation receipt. Prefer the receipt-managed offline path.

Updates preserve player settings, bookmarks and saves. Uninstall removes only
unchanged receipt-owned mod files and keeps the shared BepInEx loader, other mods
and personal data. Interrupted operations expose Recover; modified files block
unsafe removal. Logging has its own restoration receipt. The production loader
configuration disables its console before launch and keeps only
Fatal/Error/Warning disk logs; no log window opens with the game or Companion.
The manual ZIP includes the same console-off defaults for a fresh installation.

The first normal game launch may take time and have no speech while BepInEx
generates interop locally. The Host still refuses unsupported native APIs.
Never copy interop from another installation or disable the guard. User Guide
opens the selected language, updates from the canonical source when reachable,
and falls back to the bundled/cached guide offline. Update installer updates
the manager separately from the mod. Error details are available in the actual
error dialog rather than as an inert main-screen button.

Existing developer deployments keep their original receipts. Their files are
recognized but never silently adopted or overwritten by the new manager. Remove
the old deployment with its existing receipt and the game closed before using
the universal installer. Do not remove the currently installed QA runtime just
to test this candidate.


Developers building from source can use the [separate deployment reference](docs/DEVELOPER_INSTALLATION.md).
