# User guide editorial and evidence notes

The core guide keeps fourteen sections in all twenty installer languages.
The expanded beginner material is maintained in English (the editorial base),
Brazilian Portuguese and European Portuguese. The other seventeen translations
retain their shorter chapters, with corrected installation destinations,
current store-support limits, navigation and installer-update instructions.
Native-speaker review remains needed. Do not describe the editions as equally
detailed or claim manual screen-reader qualification from browser tests.

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
