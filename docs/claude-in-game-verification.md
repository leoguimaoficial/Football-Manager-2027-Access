> Upstream FM26 Access guide, release 0.2.16. Current FM27 commands and compatibility limits: [migration notes](fm27-migration.md).

# Verifying changes in the live game (Claude workflow)

## The idea

The mod already runs a localhost HTTP server (`AccessibilityServer`, port **8787**) that renders
the active game panel as semantic ARIA HTML and accepts interaction post-backs. It exists to feed
the browser-based NVDA surface, but it is equally a **machine-readable view of the live game**.

That means Claude can verify accessibility changes directly, without a human launching the game and
reading every screen back over NVDA. With FM26 running on the same machine, Claude hits the server
over `curl`:

- **read** the current screen as HTML to confirm a panel renders the way the change intended, and
- **drive** the game (activate buttons, navigate the ribbon, change values, open dropdowns) to
  exercise a flow end to end.

This is the project's biggest verification bottleneck — real correctness has required an in-game
NVDA pass every round-trip — partially closed. Claude reads the same surface NVDA reads, so most
structure/label/role bugs are catchable from the HTML alone.

## What this does NOT replace

The HTML is a **strong proxy, not NVDA**. It does not exercise:

- NVDA's browse-mode rendering quirks,
- live-region announcement timing (the `values` / `restructure` SSE path),
- focus movement and focus-jump bugs.

Those are exactly where past bugs have hidden, so a final in-game NVDA pass still matters for
live-region and focus behaviour. Treat this workflow as "catch most issues fast, before the human
NVDA pass," not "instead of it."

Two more constraints:

- **The game must be running.** The user launches FM26; Claude attaches via `curl`. Claude cannot
  meaningfully play the game itself beyond menu/UI navigation.
- **Driving mutates real game state.** Reading (`/version`, `/body`, `/describe`) is free and safe.
  Anything that activates or sets a value clicks through the live save — use deliberately, not
  blindly, and prefer read-only inspection when that answers the question.

## Endpoints

Base URL: `http://127.0.0.1:8787/`

### Read (safe)

| Endpoint | Returns |
| --- | --- |
| `GET /version` | Screen token (plain text). Changes on navigation or a step change — poll it to detect that the screen moved. Returns `loading` before the game is interactive. |
| `GET /body` | Active panel as ARIA HTML (the `#fmroot` fragment). Response header `X-FM-Token` carries the matching screen token. This is what the server *serves* — a fresh render. |
| `GET /rendered` | The companion window's **live** `#fmroot` DOM — `/body` plus every client-side SSE morph (live values, dropdown popups, idiomorph patches), i.e. exactly what the player's screen reader sees. Use it when a report doesn't match `/body`: the served HTML and the rendered DOM can drift. Returns a marker until a companion window is open and has reported (it reports via a debounced MutationObserver). An already-open companion must reload its page once after a `/reload` to start reporting, since app.js is inlined and loads once per session. |
| `GET /` | Full HTML page (shell + body). `/body` is usually what you want. |
| `GET /describe?id=<id>` | Description of a single element by its `id`. |

Element `id`s are the `id="..."` attributes in the `/body` HTML (e.g. `ecb50cb4d`). They are stable
only for the current screen — re-read `/body` after any navigation before using ids again.

### Drive (mutates game state)

| Endpoint | Effect |
| --- | --- |
| `GET /activate?id=<id>` | Click the element (button, tile, card). |
| `GET /settext?id=<id>&value=<text>` | Set text on a text field. |
| `GET /setvalue?id=<id>&value=<text>` | Set a control's value. |
| `GET /step?id=<id>&dir=<-1\|1>` | Step a stepper-style control. |
| `GET /nav?dir=<next\|prev>` | Cycle the open panel forward/back. |
| `GET /nav?target=<portal\|squad\|recruitment\|matchday\|club\|career\|calendar\|pausemenu>` | Jump to a ribbon category. |
| `GET /history?dir=<back\|forward>` | Screen-history back / forward (the on-screen Back/Forward button). |
| `GET /dropdown/open?id=<id>` · `/dropdown/expand?path=<p>&index=<i>` · `/dropdown/collapse?path=<p>` · `/dropdown/select?path=<p>&index=<i>` · `/dropdown/close` · `/dropdown/more` | Drive a standard dropdown session. |
| `GET /searchdropdown/open?id=<id>` · `/searchdropdown/filter?q=<text>` · `/searchdropdown/select?index=<i>` · `/searchdropdown/close` | Drive a searchable (combobox) dropdown. |

Action endpoints reply with a short plain-text result (`ok`, or a message like
`Unknown navigation target.`). Query values must be URL-encoded.

`GET /events` is the SSE stream (`screen` / `restructure` / `values` / `commentary` / `score`
events). It is a long-lived connection, awkward to consume from one-shot `curl`; for verification,
poll `/version` for change detection instead.

### Dev driver (arbitrary game control)

The endpoints above drive the mod's companion surface (its cursor and handles). The dev-driver
endpoints below drive the **raw game** and read back state the companion surface does not expose.
They are always on (loopback only). Requests run on their own thread, so a slow `/eval` never blocks
`/version` or the companion.

| Endpoint | Effect |
| --- | --- |
| `POST /eval` | Body = C# source, compiled and run on the main thread against the live game (`mcs` REPL — the mcs-unity build of the Mono C# compiler, CoreCLR-compatible; state persists across calls). Returns stdout + the last expression (printed as `=> value`) or compile/exception text. The universal fallback: it can click any element, call any `FMNavigationManager` / `PanelManager` method, send any UI event, and read any state. |
| `POST /input` | Body = a verb: `up` `down` `left` `right` `confirm` `cancel` `activate`. Drives FM26's own native focus via `FMNavigationManager` (`GetNextNavigatableElement` + `UpdateFocus` for moves; `NavigationSubmit`/`Cancel` events for confirm/cancel). When nothing is focused yet, the first directional verb establishes focus rather than moving. |
| `POST /cursor` | Body = a verb: `up` `down` `left` `right` `first` `last` `read`. Drives the mod's OWN accessibility cursor (the arrow-key speech surface the blind player moves), not the game's native focus — each verb maps to the same `NavigationManager` call the matching arrow key makes. The move speaks through Prism, so read the exact spoken line back via `/speech`; the response also returns the resulting cursor's role and announcement. `read` reports the current cursor without moving. |
| `GET /speech?since=N` | Lines the mod has spoken through Prism since cursor `N` (we can't hear the TTS). First line is `cursor: <next>`; pass that back as `since` to get only newer lines. Start at `since=0`. |
| `GET /log?since=N` | The mod's own `Log.*` output (Info/Warn/Error/Fatal) since cursor `N`, same cursor protocol — read warnings and errors without opening `Player.log`. |
| `GET /events/log?since=N` | The SSE events pushed to the companion (`commentary`, `values`, `score`, `screen`, `restructure`) since cursor `N`, same cursor protocol. Captures the live announcements the player hears via the companion's aria-live region (the match commentary), which never go through Prism and so are absent from `/speech`. |
| `GET /read` | The **whole active screen linearized to a screen-reader-style text block** — what the companion presents, in reading order, as plain text. Built from the same pipeline as `/body` (streamed tables materialized to the same 50-row page, named in the read as "Page 3 of 7"), so it shows exactly what the player would hear browsing the screen, minus the on-demand dropdown option lists (a dropdown shows its current value with a note to open it). This is the menu-audit view: pull one block per screen and judge whether it reads logically and completely. |
| `GET /gui` | Raw `VisualElement` tree of the active panel (path depth, IL2CPP type, name, visibility, text) — the unfiltered structure the cleaned `/body` view hides. |
| `GET /classify` | The mod's classification of the active panel: the navigation stops (each with the announcement the player would hear), containers, layout wrappers, and any unknowns. The most actionable structural view — "what can the player reach here, and what is missed" — the inline equivalent of F9's `f10_classify.txt`. |
| `GET /audit` | The **accessibility-defect audit** of the active screen: it walks the interactive set exactly as the HTML surface renders it and buckets every stop into the recurring defect flavours — `empty-name` (an interactive control with no accessible name), `degrade` (an honest gap marker the control fell back to), `image` (a leaf image read as a stop), `orphan-label` (a field label stranded before the unnamed control it should name), `dup-adjacent` (an informational title rendered twice) — plus open-set LEADS for a flavour the rules do not model yet (`id-shaped` names; the live `/unclassified` count). This is the generated worklist that replaces eyeballing `/classify` against `/body`; the rules live in `FM26Access.Navigation/AuditRules.cs` (unit-tested). Sweep the whole ribbon and aggregate findings by flavour across screens with `scripts/audit-sweep.ps1` — re-run after a structural fix and watch a category fall to zero on every screen to prove the fix generalised. |
| `GET /panels` | Raw `VisualElement` tree of every open panel across all PanelManager layers (the ribbon, tooltips, overlays, modals) — the cross-layer view that `/gui` and `/body`, both active-panel-only, never show. |
| `GET /unclassified` | The stateful unclassified-element tracker: element types the classifier did not recognize, accumulated across every screen visited (what `/classify`, a single-panel snapshot, cannot show). `?clear=1` resets it. This is the F8/F11 pair as an endpoint. |
| `GET /chartprobe` | Dumps every line chart on the active panel: series, data points, each point's label/value and DynamicReference property names (via PropertyIdentifierSet). The recon tool for turning a graphical chart into a semantic table (see `LeagueHistoryChartReader` / `FinanceChartReader`). |
| `GET /nodeprobe?type=<Il2CppType>[&nth=0][&depth=3]` | The binding nodes above the first element of that type (e.g. `FMXGMatchStoryChart`): for each BindingRoot ancestor, its node tree with every record-valued node's field names. The recon step for reading a drawing from the record it plots (see `LastMatchCharts`). |
| `GET /subtree?name=<element name>[&nth=0][&count=1]` | The raw tree under the nth (and following) elements with that name, past `/gui`'s node cap, plus every background-image sprite name and each SIText's resolved colour — the meaning of an icon is often only its sprite (a goal glyph's `goal-2_2x` count, a `sub off` arrow) and of a name only its colour (a booked player in yellow). |
| `GET /dateprobe` | The active panel's date selector, inside and out: controller type, the years and months it allows, the month dropdown's index, and every day-grid cell with its bound date and enabled flag. `?open=1` / `?close=1` drive the popup, `?year=YYYY` lists that year's selectable days, and `?setyear=` / `?stepmonth=` / `?pick=d/m/y` / `?confirm=1` rehearse `DateSelectorSession`'s apply one step per request (frames pass between calls). Use it when a picker refuses a date, or to read a controller's allowed set without opening the calendar. |
| `GET /screenshot` | Captures a PNG to a temp path and returns the path; `Read` that path to view the frame. Works while the game is unfocused. |
| `GET /health` | `ok` — liveness for the bring-up poll (answers even before the game is interactive). |
| `GET /liveness` | Whether the surface can read the game at all, and why not: `state=Loading\|Live\|ModStopped\|ModFailing\|GameNotResponding` with the readings behind it (`msSinceDrain`, `msSinceTickFailure`). Answers when nothing else can — after the core has stopped or stalled, every other route only reports the state, so this is where "the mod says everything is unavailable" gets triaged. |

`/eval`, `/input` and `/cursor` are POST; send the body with `curl --data` (or `--data-binary @file`
for larger snippets). `/eval` is arbitrary code execution — it mutates real game state as readily as
it reads it. To see everything the player just perceived after an action, combine `/body` (what they
can read), `/speech` (what Prism said) and `/events/log` (what the live regions announced).

```bash
# Run C# against the live game (the body is an expression; the result prints as "=> ...")
curl -s -X POST --data 'UnityEngine.Application.version' http://127.0.0.1:8787/eval

# Move the game's native focus down, then read back what was spoken
curl -s -X POST --data 'down' http://127.0.0.1:8787/input
curl -s 'http://127.0.0.1:8787/speech?since=0'
```

### Permissions

The loopback server URLs are on the Bash allowlist in `.claude/settings.local.json`, so **single**
curl calls to `127.0.0.1:8787` / `localhost:8787` auto-approve without a prompt. Auto-approval does
NOT survive command chaining — a pipe, `&&`, `;`, or a `while` loop drops back to prompting. So
issue one curl per call, and when waiting for a screen to load, poll by repeating single `/version`
calls rather than wrapping them in a shell loop.

## Running the game (dev loop)

The accessibility server and dev driver are baked into the mod and **on by default** — no env var
to enable them. Launching or killing the game is a real action; do it with the user's OK.

- **Launch:** FM26 is Steam app id **3551340**. Invoke `steam.exe` directly by its full path,
  backgrounded with `&`, e.g. `"/c/Program Files (x86)/Steam/steam.exe" -applaunch 3551340 &` from
  Git Bash. Do **not** wrap it in `cmd.exe /c start …` — `start` can swallow the `-applaunch` args and
  resets the shell's working directory, so the game never launches. `steam://rungameid/3551340` also
  did not work. Cold boot to an interactive main menu takes a while.
- **Wait for ready:** poll `GET /health` until it answers `ok` (the server is up), then `GET /version`
  until it returns a numeric token rather than `loading` (the game is interactive).
- **The cold-boot gauntlet:** a fresh launch does not land on a usable screen. Expect a Controller
  Introduction screen (advance it — its primary button is an `SIButton` named
  `buttons-button-primary-default`), then one or more modals (a graphics-driver warning, a
  controller-disconnected notice that can also reappear mid-session while driving input). Dismiss each
  via its Ok/Dismiss button — grab the id from `/body` and `GET /activate?id=…`, which is more reliable
  than `/input confirm` when native focus is on the wrong element. Only after these do you reach the
  Main Menu. **With no save loaded, the ribbon categories are unavailable** — `/nav?target=squad`
  returns "squad not available on this screen" until a career is loaded, so menu/Preferences screens
  are all you can reach from a cold boot without loading or starting a game.
- **Iterate on the mod (hot reload):** for changes to the reloadable core (`FM26Access.Plugin` —
  adapters, patches, walkers, HTML, dev endpoints, profiles, i.e. almost everything), you do **not**
  need to relaunch. The host loads the core DLL from bytes (no file lock), so just `dotnet build`
  (auto-deploys the new `FM26Access.Plugin.dll`), then `curl -X POST http://127.0.0.1:8787/reload`,
  then poll `/health` until it answers `ok` again (about a second). The new code is live. If a
  reload's Mount throws (a bad change), the mod pauses but the server stays up: `/log` shows the
  exception and another `/reload` after a fix recovers it, still without a relaunch. See
  `docs/hot-reload-plan.md`.
- **Changes that still need a full relaunch:** the Host project (`FM26Access.Host` — the entry point,
  injected behaviour, Prism/companion/Speech init) and the permanent shared assemblies
  (`FM26Access.Speech`, `FM26Access.Navigation`). For those, **kill the game first**
  (`taskkill /F /IM fm.exe`), `dotnet build`, then relaunch. While the game is running it holds those
  three DLLs locked, so their deploy-copy is skipped with a warning (the build still **succeeds** —
  `Plugin.dll` and the panel profiles deploy normally); your new Host/Speech/Navigation code therefore
  does not reach the game until you relaunch with it killed.
- The companion window auto-launches with the game and closes when the game exits. It survives core
  reloads on its own (the server socket goes away and comes back; the companion retries).

## The loop

1. **Confirm the game is up:** `curl -s -m 4 http://127.0.0.1:8787/version`
   - empty / connection refused → game not running (launch it, see Running the game, or ask the user).
   - `loading` → game still starting; wait and re-poll.
   - a numeric token → ready.
2. **Read the screen:** `curl -s -m 8 http://127.0.0.1:8787/body` and inspect the HTML — headings,
   `aria-label`s, button text, roles, disabled state. This is what the screen reader user perceives.
3. **Drive if needed:** activate an element or navigate, then re-read `/body` (its `X-FM-Token`, or a
   fresh `/version`, confirms the screen actually changed).
4. **Repeat** until the changed flow is exercised, then report what the HTML showed.

### Example

```bash
# Is the game ready?
curl -s -m 4 http://127.0.0.1:8787/version

# Read the current screen
curl -s -m 8 http://127.0.0.1:8787/body

# Jump to the Squad category, then re-read
curl -s -m 6 "http://127.0.0.1:8787/nav?target=squad"
curl -s -m 8 http://127.0.0.1:8787/body
```

## Background

The cross-project nudge that prompted this: a sibling accessibility mod
([`Destranis/tangledeep_access`](https://github.com/Destranis/tangledeep_access), BepInEx + Tolk
for the roguelike Tangledeep) — useful as a peer reference on screen-reader routing, but it has no
server. The "HTTP server worth reusing" is this project's own `AccessibilityServer`; the insight was
that the surface built for NVDA doubles as a verification channel for Claude.
