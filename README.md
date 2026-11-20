# FM27 Access

FM27 Access (Football Manager 2027 Access) is an unofficial accessibility mod
that aims to help blind players manage their football clubs using a screen
reader and keyboard navigation.

**FM27 Access 0.1.1** improves large-screen loading, table selection and Xbox/Microsoft Store compatibility.

The unreleased source also fixes startup on Steam FM27 **27.1.0.2505117**
(build **26620374**) and reduces unnecessary rejections after game updates.
It also restores native Quick Start choices and organizes the game-setup summary
and editable league table. The published 0.1.1 downloads below predate these
source-only corrections.

## Download

- [Download the universal Windows installer](https://github.com/leoguimaoficial/Football-Manager-2027-Access/releases/latest/download/FM27Access-Installer.exe) — recommended.
- [Download the complete 0.1.1 ZIP](https://github.com/leoguimaoficial/Football-Manager-2027-Access/releases/download/v0.1.1/FM27Access-v0.1.1.zip) — offline installation through **Install ZIP**.
- [Download the multilingual user guide](https://github.com/leoguimaoficial/Football-Manager-2027-Access/releases/latest/download/FM27Access-UserGuide.html).
- [Release notes and all downloads](https://github.com/leoguimaoficial/Football-Manager-2027-Access/releases/tag/v0.1.1).

You need Windows 10 or 11 x64, Football Manager 2027, a screen reader such as
NVDA, and Microsoft Edge WebView2, which normally comes with Microsoft Edge.

## Install and start playing

Open `FM27Access-Installer.exe`, choose your language and Football Manager 2027
installation, select the mod version and choose **Install**. If you have the ZIP
package, keep it beside `FM27Access-Manifest.json` and choose **Install ZIP**.
Close the game and Companion before installing, updating or removing the mod.

Start Football Manager normally after installation. The first launch can take a
few minutes without speech; wait for the Companion window, then use it with your
screen reader. You do not need to install .NET or BepInEx separately. The BepInEx
console stays closed.

Steam is the primary development target. For Steam startup freezes or an Xbox/
Microsoft Store compatibility error with 0.1.0, close the game and Companion,
update the installer to **1.0.4** or newer, then update the mod to **0.1.1**.
The same installer selects the appropriate files for each store; updating the
installer alone does not update the installed mod. Saves and settings are kept.
See the [compatibility record](docs/NATIVE_COMPATIBILITY.md) for actual evidence
and the remaining fresh Xbox/Epic live-test limits.

Use the same installer to update, repair or uninstall. Your saves, bookmarks and
settings are kept. See [installation help](INSTALL.md) and the
[player guide](docs/user/FM27Access-UserGuide.html) for keyboard shortcuts,
messages, squad management, tactics and matches.

## About the project

Football Manager brings together a great deal of information, from messages and
player reports to tactics and match events. FM27 Access is adapting that
experience so blind players can read information, navigate screens and use the
game's controls independently, without relying on its visual interface.

## How it works

The Companion Web interface presents the current game's information and controls
as an accessible page. Players can explore headings, lists and tables with their
screen reader, navigate with the keyboard and interact with the game through
the companion. Spoken feedback supports navigation and actions.

This approach builds on the established FM26 Access experience. FM27's own text,
choices and rules remain the source of the information presented to players.

## What you can do

- Read the current screen with familiar headings, lists, tables and form controls.
- Navigate messages, player information, squad selection, tactics and training.
- Use a searchable menu, keyboard shortcuts and your own saved bookmarks.
- Read match information, statistics, team talks and preferences through the Companion.
- Install, update, repair and remove the mod with a native Windows installer.
- Choose from 20 installer and user-guide languages, including Brazilian Portuguese.

The installer detects Steam, Epic and Xbox PC installations and checks whether
the selected package can be installed. Current evidence includes targeted Steam
screens and historical Xbox PC scenarios. The Xbox 0.1.1 correction is checked
against preserved real Xbox compiler/API inputs; a fresh Xbox launch remains
pending. See [known issues](docs/KNOWN_ISSUES.md) for specific
limitations and [the changelog](CHANGELOG.md) for the changes in this release.

The [universal installer](installer/README.md) uses the same
`FM27Access-Installer.exe` to discover official mod versions and manage each game
installation, with safe update, repair and removal. English and nineteen other
language resources, including Brazilian Portuguese, are included with an
[offline HTML user guide](docs/user/FM27Access-UserGuide.html).
The installer opens quietly and keeps status and errors in a read-only messages
field you can reach with Tab. Windows dialogs confirm completed operations.

## Accessibility goals

- Screen reader compatibility and keyboard-first interaction.
- Clearly named menus and controls, with consistent navigation and focus.
- Useful headings, lists and tables that preserve the game's information.
- Reuse of proven FM26 Access interaction patterns, adapted to FM27's interface.
- Independent gameplay for blind players.

These are continuing project goals; FM27 Access does not yet provide complete
accessibility across the game.

## Project origins

FM27 Access builds upon the original
[Football Manager 2026 Access project created by DnDeezNuts](https://github.com/DnDeezNuts/Football-Manager-2026-Access).
With the original developer's permission, it carries forward FM26 Access's
accessibility foundations while adapting them to Football Manager 2027 and its
redesigned interface. See [credits and dependency acknowledgments](CREDITS.md).

## Following development and contributing

Follow this repository and its [changelog](CHANGELOG.md) for development updates.
Use [GitHub Issues](https://github.com/leoguimaoficial/Football-Manager-2027-Access/issues)
to report problems, share accessibility feedback or suggest improvements. Please
avoid including private information or game saves in public reports.

Contributions to testing, documentation and code are welcome. Start with the
[contribution guide](CONTRIBUTING.md). Developers can find the existing source in
the `FM27Access.*` directories and prerequisites in [Building FM27 Access](BUILDING.md).
The `main` branch contains current development. To build the published version,
use its matching release tag and source archive. The
[distribution guide](docs/DISTRIBUTION.md) explains builds and release approval.

## License and disclaimer

FM27 Access is licensed under the [GNU General Public License v3.0](LICENSE).
Original authors' notices and [third-party licenses](packaging/THIRD-PARTY-LICENSES.txt)
are preserved. Proprietary game files are not included.

This is an unofficial community project, not affiliated with or endorsed by
Sports Interactive or SEGA. Football Manager and related trademarks belong to
their respective owners.
