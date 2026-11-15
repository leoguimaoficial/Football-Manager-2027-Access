# FM27 Access

FM27 Access (Football Manager 2027 Access) is an unofficial accessibility mod
that aims to help blind players manage their football clubs using a screen
reader and keyboard navigation.

**In development — not yet released.** The project is working toward its first
version, **0.1.0**. There is no official downloadable release or supported
end-user installation available yet.

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

## Development status and current progress

This repository makes the source code and development progress available to
follow openly. The existing implementation includes the Companion Web interface,
speech support and adaptations for FM27's redesigned screens.

Targeted checks in the game have exercised messages and inbox navigation, squad
and player information, tactics, career setup, and parts of the match interface,
including team talks, statistics and preferences. Recent work improves focus
return, late-loading controls, table content and spoken screen titles.

These checks cover specific scenarios. Complete career and match accessibility,
all game variants and a first public installation still need further validation.
Known problems and testing limits remain recorded in the
[development notes](docs/README.md) and [known issues](docs/KNOWN_ISSUES.md).
The [changelog](CHANGELOG.md) describes implemented changes.

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
The `main` branch is maintained as the verified development baseline; it is not
a released product.

## License and disclaimer

FM27 Access is licensed under the [GNU General Public License v3.0](LICENSE).
Original authors' notices and [third-party licenses](packaging/THIRD-PARTY-LICENSES.txt)
are preserved. Proprietary game files are not included.

This is an unofficial community project, not affiliated with or endorsed by
Sports Interactive or SEGA. Football Manager and related trademarks belong to
their respective owners.
