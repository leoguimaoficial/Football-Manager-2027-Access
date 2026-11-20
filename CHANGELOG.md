# Changelog

All notable changes to FM27 Access will be documented in this file.

The project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
and the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.
Version 0.1.0 is the first public release.

## [Unreleased]

### Fixed

- The pre-career club profile separates board/supporter culture goals, kits,
  club facts, cup competitions and rivals into readable groups. Native headings,
  descriptions and available actions are preserved.

- Biography's additional favourite-club button uses its native localized caption
  instead of the decorative "Plus icon" name, without repeating the caption below.

- Game setup groups the chosen club, starting date and database totals more
  clearly. The summary and Add/Remove Leagues window use native table headings,
  retain division counts and name selectors by nation and men's/women's leagues.
  Playable/view-only labels come from the game's own legend.

- Quick Start uses the game's localized unemployment caption and omits the
  decorative `LeagueBackground` image. Club choices are actionable again through
  their native grid selection, retaining names, predicted finishes and selection.

- Restore startup on Steam FM27 27.1.0.2505117 (build 26620374). Compatibility
  checks now require the native members actually used by the mod, including
  reflected controls and patches, instead of unrelated generated game internals.
  Compatible updates can load without a mod rebuild; genuine required API or
  engine changes can still require an adaptation.

## [0.1.1] - 2026-11-15

### Faster screens and steadier table selection

- Adapt DnDeezNuts's FM26 Access 0.2.17 improvements. In the measured FC Porto
  men's checkpoint, repeated screen loads were about 9% faster for Squad, 15%
  for Tactics and 12% for Player Database; results depend on the screen/machine.
- Rapid player-checkbox presses retain the intended selections, including when
  a table refresh is in progress. A stale response no longer unticks a choice.
- Multi-page tables offer **Select all on page**, alongside the existing full
  list selection and clear actions.

### Xbox/Microsoft Store compatibility and installer 1.0.4

- Correct the false incompatible-game rejection on Xbox/Microsoft Store while
  retaining checks for the game functions the mod needs.
- One installer chooses the appropriate loader name for Xbox/Microsoft Store
  and other stores, keeps one proxy only and preserves owned-file migration and
  rollback. Update both the installer and the mod to receive the complete fix.
- The full manual retains all content in all twenty languages, including the
  same new update instructions. Saves, bookmarks and settings remain in place.

Steam has scoped live evidence; the Xbox correction is checked against the
preserved real 0.9.2435.0 compiler/API inputs and installer cases. A fresh Xbox
launch is still pending. See [testing](docs/TESTING.md) for exact evidence.

## [0.1.0] - 2026-11-15

The entries below summarize implemented work supported by the source and
development records. In-game checks cover specific scenarios; they do not
establish complete career, match or screen reader compatibility. See the
[testing record](docs/TESTING.md) and [known issues](docs/KNOWN_ISSUES.md).

The packages were refreshed for Steam on the same date, at the maintainer's
explicit request, without changing the mod version. Exact source/build and
download verification are recorded in [distribution](docs/DISTRIBUTION.md).

### Complete guide in all twenty languages

The full manual and installer 1.0.3 were published on the same date, preserving
the existing mod package and version.

- All twenty languages now contain the complete beginner manual, with the same
  paragraphs, steps, shortcuts and references. Previously shorter translations
  have been expanded to match the English and Portuguese editions.
- The guide build refuses missing content or translations based on an older
  source revision. Installer 1.0.3 includes the complete offline manual.

### Installer and manual refresh

Installer engine 1.0.2 and the revised manual were published independently on
the same date. The mod package, its source archive and its 0.1.0 tag are unchanged.

- The installer checks for its own updates when it opens and asks before
  downloading and restarting. The prompt shows the current and available versions.
- An installed mod now has one Repair action when its version is selected.
- Version notes are displayed as readable text with paragraphs and bullets,
  without Markdown punctuation. Public notes describe changes for players.
- Expanded beginner guidance in English and Portuguese covers the first day,
  delegation, players, tactics, training, deals, finances and the first match.
  Official FM27 feature references explain what is new without promising
  accessibility coverage for untested modes.
- All twenty editions clarify that Xbox game files belong inside Content,
  distinguish currently tested stores and explain installer startup updates.
- The offline manual has a new light/dark layout, contents, skip link, visible
  keyboard focus, mobile reflow, high-contrast support and print styling.

### Added

- Universal native Windows installer with independent engine versioning, public
  GitHub version discovery, per-game receipts, package integrity checks, safe
  cancellation/rollback, repair, removal and a separate installer update path.
- Complete distribution packaging with the pinned IL2CPP x64 BepInEx runtime,
  production console-off defaults, manifests, checksums and corresponding source.
- Full GitHub-hosted compilation using private, pinned compiler references;
  developer builds detect game/reference updates, and maintainer builds can
  refresh the private input and public pin automatically. Releases promote approved artifacts
  without depending on the developer's computer.
- Twenty installer language resource sets and a self-contained multilingual HTML
  guide with offline fallback and independent documentation updates.
- Player-oriented setup and gameplay instructions adapted from the public FM26
  manual: screen-reader setup, Enter-to-confirm lists, searchable navigation,
  table columns, bookmarks, career setup and separate match shortcut tables.
- Installer messages and error explanations are available as read-only multiline
  fields in the Tab sequence. Standard Windows dialogs confirm completed installs,
  updates, repairs and removal, with the result retained for later reading.
- GitHub validation and selected-build publication workflows, with run/commit
  verification and a required-reviewer environment gate before release publication.

- Initial FM27 adaptation of FM26 Access 0.2.16, retaining its accessible Companion
  Web interface, speech pipeline, keyboard navigation and table paging.
- Accessible presentation of FM27's Portal, including messages, agenda,
  standings, native categories, read state and unread updates.
- FM27 readers for squad status, injury reasons and histories, player attributes,
  form, ability and potential, with contextual player and club actions.
- Native tactical-effectiveness factors, role information and modern team
  instruction tables, retaining the game's own action and possession context.
- Adaptations for career setup, coach biography, guided tutorials, database
  selection and custom database controls using the existing form patterns.
- Weekly training calendar tables, native plan categories, projected impacts
  and contextual popup controls.
- Public project overview, contribution guidance, credits and ongoing repository
  and changelog maintenance rules.

### Changed

- Adapted native FM27 layers, labels, searchable choices, shortcut capture,
  colour controls and form tables while retaining FM26 interaction patterns.
- Preserved separate possession-phase tactic pitches, native player and position
  pickers, player instructions and role details. Panoramic keeps FM26's existing
  presentation.
- Restored message purpose, topics and important people as separate native-backed
  sections and lists. Message prose stays readonly while useful person links
  remain in the appropriate lists and tables.
- Consolidated redundant entity artwork and captions only where the same native
  identity and action are demonstrated, preserving distinct destinations.
- Kept informative headings separate from action buttons and retained complete
  labels and on-demand descriptions.
- Restored the original five-project solution and permanent Host/reloadable
  Plugin architecture, with the Companion application built separately.
- Made Release the default, with developer tools requiring explicit Debug opt-in.
  Production builds omit developer endpoints, recordings, startup self-tests and
  symbols; the Companion disables WebView developer tools.
- Preserved existing build and receipt-aware deployment tooling for development,
  with separate loader and logging ownership, alongside the universal player installer.
- Consolidated maintained documentation in English while preserving official
  game localization, original legal notices and attributed FM26 references.

### Fixed

- Steam startup uses Doorstop's supported `version.dll` proxy, preserving the
  exact pinned loader bytes while leaving native WinHTTP to Windows. The installer
  repairs the existing 0.1.0 installation by retiring only its unchanged, owned
  old proxy; shared or modified files are preserved and conflicting proxies refused.
- Complete packages require installer engine 1.0.1 for that receipt-aware repair.
  The mod remains 0.1.0; compiler references and build examples follow Steam.
- Release packaging correctly reads GitHub release arrays and full catalog
  pages, reusing the validated installer engine when its version is unchanged.
- Opening the installer no longer announces automatic discovery/status updates
  or restores focus a second time. Messages remain available in the Tab sequence.

- Automatic arrivals no longer speak internal panel identifiers when a native
  title has not bound yet. Actual short titles and explicit reading remain.
- Late native confirmation callbacks now update ordinary buttons automatically
  through the existing bounded screen-settling mechanism.
- Restored team-talk player and reaction rows by retaining their actual native
  references instead of interpreting them as set-piece taker records.
- Corrected match-preference labels and fullscreen selection, removed redundant
  decorative glyphs, and preserved the native controls and saved settings.
- Removed a synthetic heading from the half-time command ribbon and an extra
  landmark from match-statistics overlays, preserving native content and controls.
- Corrected the pre-match FM Menu name and duplicated team-selection headings
  and substitutes strips while retaining table player-selection commands.
- Repaired message opening, body replacement and history races, retaining actual
  message identity, stale-command refusal and contextual focus return.
- Restored the empty-inbox presentation while allowing late-arriving rows to
  recover automatically; message filters retain native selection state.
- Prevented covered background screens from being presented or focused while
  native overlays are opening or replacing one another.
- Corrected Global Search dismissal and native Back handling, preserving genuine
  history errors and exact origin focus.
- Preserved World-menu origin through hierarchical navigation and native Back,
  and identified Site pages from their actual bound entity headers.
- Retained native-disabled dropdown options through virtualized continuations
  and rechecked realized option identity and availability before commands.
- Restored squad position values and contextual column labels, native fractional
  percentages, late summary values and selected-state updates.
- Preserved full calendar events, native fixture and player associations, and
  late-loading overview content without creating duplicate commands.
- Recovered initial Companion loading errors through the existing live-update
  shell, with contextual error feedback if recovery is unavailable.
- Hardened local HTTP framing, request authorization and shutdown handling while
  avoiding automatic replay of game commands. The historical connection timeout
  remains unresolved.

[Unreleased]: https://github.com/leoguimaoficial/Football-Manager-2027-Access/commits/main
