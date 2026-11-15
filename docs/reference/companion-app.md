> Upstream FM26 Access reference, release 0.2.16. Check [FM27 migration notes](../fm27-migration.md) before using game-specific APIs.

# The Companion App

The companion app is the blind player's primary window into FM26. The game's own UI is
inaccessible, so the mod renders the active screen as semantic ARIA HTML and serves it on a
loopback HTTP server; the companion is a small WebView2 window that displays that HTML, which the
player browses with their screen reader. This doc covers what it is, how it launches, what the
player does with it, and how it stays in sync with the game.

## What it is

`FM26Access.CompanionApp` is a standalone WinForms + WebView2 executable (`Program.cs`). It is a
thin shell: a single full-window WebView2 control pointed at `http://127.0.0.1:8787/`, the mod's
in-game `AccessibilityServer`. All of the actual UI — the page, its behaviour, the keyboard
chords — is HTML and JavaScript (`FM26Access.Plugin/Server/app.js`) served by the mod, not built
into the companion. The companion just hosts a browser engine that a screen reader can read in
browse mode.

It is a separate project and is **not** part of `FM26Access.sln`. It builds on its own and is
deployed, as a self-contained exe, into `…/BepInEx/plugins/FM26Access/companion/` alongside the
mod by the Plugin's MSBuild targets.

## Launching

The mod auto-launches the companion at startup (`CompanionLauncher.Launch`), passing
`--watch-pid <gamePid>` so the window closes itself when FM26 exits — quitting the game leaves no
orphaned companion behind. If a companion is already running, the launcher skips starting a second
one. A failed launch is a non-speech convenience failure: it warns and continues (the player can
open the companion, or any browser at `127.0.0.1:8787`, by hand), and never crashes the game.

Because the companion retries the connection until the server answers, launch ordering does not
matter — it can start before the game's server is up and will connect once it is.

## What the player does with it

The player reads and drives the game entirely through the companion's HTML:

- **Read** the screen by browsing the HTML with the screen reader (headings, lists, tables, form
  controls, buttons with their state). This is the player's view of the current screen.
- **Navigate** with custom Alt-chords handled client-side in `app.js`: Alt+1–6 for the ribbon
  categories, Alt+C calendar, Alt+P the FM/pause menu (during a match: toggle match playback),
  Alt+M the match preferences card during a match, Alt+Left/Right to cycle panels, Alt+[ /
  Alt+] for screen history, Alt+I to speak the focused control's hidden description, Alt+\ to
  speak the continue button's label plus the game date/time, Alt+Enter to press it. See
  `docs/reference/default-keybinds.md` (HTML Surface Chords).
- **Activate** controls with Enter; the page posts the action back to the server, which drives the
  live game, then refreshes in place.

## How it stays in sync (live updates)

The page holds a Server-Sent Events connection to `/events`. On each event the server pushes, the
companion updates without a full reload:

- `screen` / `restructure` — the screen changed or late content populated; the page re-fetches
  `/body` and morphs the changed DOM in place (focus preserved).
- `values` — specific element texts changed; patched in place by id, read on demand.
- `commentary` — match play-by-play; written into a polite `aria-live` region (`#fmlive`) so the
  screen reader announces it hands-free.
- `score` — the match score/clock line; updated silently.

Two distinct things the player hears, therefore, come from two channels: **Prism TTS** speaks
on-demand reads and action feedback (the `/speak` endpoint, via `FM.core.say` — Alt+I, search
counts, errors), while the **screen reader's own voice** reads the HTML they browse and announces
the `commentary` live region. The dev driver's `/speech` captures the former; `/events/log`
captures the latter (see below).

## Relationship to the in-game speech cursor

The mod also has an in-game arrow-key speech cursor (`NavigationManager` + Prism), the surface
that predates the companion. It is still live — arrow keys move it and it speaks through Prism —
but the companion HTML is the primary, richer surface (headings/tables/forms, custom chords,
in-place live updates). When the player activates a control via the in-game cursor, the mod stays
silent and lets the companion's screen reader re-announce the new state, to avoid double-speaking.

## Verifying / driving it as an agent

Everything the player perceives through the companion is observable over the same loopback server,
so an agent can verify the experience without the WebView2 window:

- `GET /read` — the whole screen linearized to a screen-reader-style text block (the companion's
  content in reading order). The fastest way to audit whether a menu reads logically.
- `GET /body` — the raw HTML the player browses (what `/read` is built from).
- `GET /speech?since=N` — what Prism spoke.
- `GET /events/log?since=N` — the SSE events pushed to the companion (commentary/values/score/…),
  i.e. the live announcements the player heard that `/speech` misses.
- `POST /activate`, `/nav`, etc. — drive the companion surface exactly as the player's chords do.

See `docs/claude-in-game-verification.md` for the full endpoint reference and workflow.

## Code map

- `FM26Access.CompanionApp/Program.cs` — the WebView2 host window, `--watch-pid` handling,
  anti-backgrounding browser flags (so the SSE stream is not throttled when unfocused).
- `FM26Access.Plugin/CompanionLauncher.cs` — auto-launch from the mod at startup.
- `FM26Access.Plugin/Server/app.js` — the page logic: `/body` fetch + morph, SSE handling,
  Alt-chords, action posts, focus restore.
- `FM26Access.Plugin/Server/AccessibilityServer.cs` — the server, the SSE `/events` stream, and
  routing.
