# Changelog

All notable changes to FM27 Access will be documented in this file.

The project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
and the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.
Version 0.1.0 is the initial development milestone and has not been released.

## [Unreleased]

The entries below summarize implemented work supported by the source and
development records. In-game checks cover specific scenarios; they do not
establish complete career, match or screen reader compatibility. See the
[testing record](docs/TESTING.md) and [known issues](docs/KNOWN_ISSUES.md).

### Added

- Universal native Windows installer with independent engine versioning, public
  GitHub version discovery, per-game receipts, package integrity checks, safe
  cancellation/rollback, repair, removal and a separate installer update path.
- Complete distribution packaging with the pinned IL2CPP x64 BepInEx runtime,
  production console-off defaults, manifests, checksums and corresponding source.
- Twenty installer language resource sets and a self-contained multilingual HTML
  guide with offline fallback and independent documentation updates.
- Player-oriented setup and gameplay instructions adapted from the public FM26
  manual: screen-reader setup, Enter-to-confirm lists, searchable navigation,
  table columns, bookmarks, career setup and separate match shortcut tables.
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
  with separate loader and logging ownership. No public installer is provided.
- Consolidated maintained documentation in English while preserving official
  game localization, original legal notices and attributed FM26 references.

### Fixed

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
