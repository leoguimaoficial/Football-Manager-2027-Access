# Contributing

FM27 Access is in early development. Help with accessibility reports, bug reports,
suggestions, screen reader testing, documentation and code is welcome.

## Reporting a problem

Open a [GitHub Issue](https://github.com/leoguimaoficial/Football-Manager-2027-Access/issues)
with the game version, relevant screen, steps to reproduce, expected result and
what happened. For accessibility problems, include your screen reader and input
method when relevant. Remove private information from reports; do not upload
game saves, credentials or proprietary files.

Start with the current [public release](https://github.com/leoguimaoficial/Football-Manager-2027-Access/releases/latest)
when reporting a player-facing problem. Include the installed mod version.
Checks and outstanding limits are described in the [development documentation](docs/README.md).

## Changing the project

Keep changes focused and preserve accessibility first. Maintain the Companion's
established headings, tables, keyboard interaction and focus behavior. Reuse
proven FM26 Access components where appropriate, adapting to the actual FM27
interface and its native text, state and commands. Avoid introducing inaccessible
controls or redesigning working components unnecessarily.

Before changing behavior, read [AGENTS.md](AGENTS.md), the
[UI modification guide](docs/UI%20modification%20guide.md) and
[FM27 migration notes](docs/fm27-migration.md). Source is in the `FM27Access.*`
directories. [Building FM27 Access](BUILDING.md) explains the Windows build and
local game dependencies; a full build needs compatible, locally generated game
interop that cannot be included in the repository.

Run the checks relevant to your change. Interface changes also need actual game
and Companion checks, including the action's result and focus return. State
exactly what was tested; automated HTML checks are not manual screen reader
validation. Update [CHANGELOG.md](CHANGELOG.md) for notable user-visible changes
and update other documentation when its information changes.

Write maintained documentation in English while preserving game localization and
verbatim legal notices. Retain original authors' attribution and dependency
licenses. Keep generated files, local diagnostics, credentials, saves and
proprietary game content out of commits.

Verified improvements can be committed and pushed through the normal Git
workflow. The public `main` branch should reflect the latest verified development
baseline; unfinished experiments should stay on development branches or locally.
Release tags and GitHub Releases require explicit authorization.
