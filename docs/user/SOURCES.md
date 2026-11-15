# User guide editorial and evidence notes

The complete guide keeps the same fourteen sections and 257 content units in
all twenty installer languages. English is the canonical editorial source;
every paragraph, heading, list item, table cell, shortcut and reference must
have its counterpart in every translation. Never publish abridged editions or
add a chapter only to selected languages. Regional wording can differ while
preserving the complete meaning.

The seventeen previously shortened editions were fully translated with machine
translation assistance, protected technical terms and targeted corrections.
The already complete Portuguese editions were retained. Structural checks do
not establish linguistic perfection: native-speaker review remains useful, and
browser tests do not establish manual screen-reader qualification.

`translations.json` pins each locale to the canonical source and its own text
hash. The generator refuses stale revisions, missing content units, changed
links or altered keyboard notation. Update all twenty locales and their revision
records together whenever the canonical text changes. Do not regenerate stamps
to conceal an untranslated source edit. The existing regression suite covers
both complete editions and deliberate omission/stale-source failures.

## FM26 reference

The practical reading model, keyboard contracts, message loop, delegation and
match guidance follow DnDeezNuts and the FM26 Access contributors' licensed
[FM26 Access 0.2.16 manual](https://github.com/DnDeezNuts/Football-Manager-2026-Access/releases/download/v0.2.16/FM26.Access.Readme.html).
The pristine source baseline remains the separately recorded FM26 commit.
FM26's old setup/uninstall procedure and its broad coverage statements were
not carried over: this project's installer receipts and actual FM27 evidence
govern those instructions. In particular, never teach removal of shared loader
folders or installation beside Xbox's Content directory.

## Publisher references checked on 2026-11-15

These are game-feature references, not evidence that each feature is accessible:

- [FM27 navigation](https://www.footballmanager.com/fm27/features/fm27-clearer-interface-smoother-navigation): main areas, informational tiles, fuller screens and messages.
- [FM27 training and Tactical Effectiveness](https://www.footballmanager.com/fm27/features/fm27-training-and-tactical-effectiveness): the four factors, next-match preparation and individual training.
- [FM27 tactical changes](https://www.footballmanager.com/fm27/features/fm27-tactical-tweaks-and-match-engine-upgrades): grouped touchline instruction changes.
- [FM27 football world](https://www.footballmanager.com/fm27/features/deeper-more-immersive-world): cup draws and transfer deadline day.
- [FM27 feature overview](https://www.footballmanager.com/fm27/features): 2D/editor changes and Shuffle Draft; no mod-coverage claim.
- [Delegation advice for FM26](https://www.footballmanager.com/the-dugout/delegating-success-football-manager-26): staff responsibilities as a learning aid, rather than automatic approval of every decision.
- [SI player reference for FM24](https://community.sports-interactive.com/sigames-manual/football-manager-2024/players-r4958/): background terminology only, not current FM27 screen paths.

The maintained compatibility/known-issues/testing records supply mod-specific
limits. Current Steam evidence covers startup and selected native menus;
historical men's-career checks do not establish a full Steam career or every
match, store, editor or multiplayer variant. Device audibility and manual NVDA
remain outside these HTML checks. Do not advertise unproved total accessibility.

## Editing and rendering

Edit the locale JSON, `chrome.json` (manual navigation labels), and
`build-guide.cjs`; regenerate the self-contained HTML. Keep existing section
anchors, language switching and offline operation. The safe inline subset is
keyboard notation and HTTPS links; arbitrary HTML is escaped. Native headings,
ordered steps, lists and real tables retain their structure. Test all languages,
keyboard links/focus, narrow layouts, light/dark/forced colors and print before
publishing. No external fonts, scripts, images, analytics or game assets are used.
