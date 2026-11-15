> Upstream FM26 Access guide, release 0.2.16. Current FM27 commands and compatibility limits: [migration notes](fm27-migration.md).

# UI Modification Guide

The single protocol for changing what a blind player perceives of any panel — hiding clutter,
relabelling a control, marking a landmark, adding an interactive widget, a live value, an
announcement, or a command. Every contributor follows this one document.

This supersedes the two former, parallel docs (*Panel Layout Profile Guidelines* and *HTML UI
Feature Addition Protocol*), which split the same activity across two mechanisms and left the
overlap between them undocumented. They are now sections of this guide (§3 and §4).

This is a **protocol, not a tutorial**. It states the mental model, the shared rules, and the
decision path, then points you at the canonical files to copy. Read those files; do not reinvent
their shape. Background on how the HTML surface got its structure: `docs/archive/plans/HTML surface refactor
plan.md`.

---

## 0. The mental model: two surfaces, two mechanisms

The player perceives the game through **two screen-reader surfaces**, both fed from the same live
`VisualElement` tree:

- **Speech surface** — sequential TTS navigation. The player arrows Up/Down through a flat list
  of navigation *stops* and hears each one. This is their only source of truth, so it must never
  lie (see §2).
- **HTML surface** — a semantic HTML page served to the companion app's WebView2 and browsed
  with the screen reader's own navigation (headings, tables, forms). Richer presentation and
  interaction live here.

There are **two mechanisms** for modifying either surface:

- **Panel profiles (declarative)** — per-panel JSON rules that reshape the *stop list* abstractly:
  **Hide**, **Rename**, **Heading**. They describe the panel, not a surface, so they apply to
  **both** surfaces (§3).
- **HTML features (code)** — C# `Server/Features/*.cs` + `FM.*` JS modules that add behaviour and
  richer presentation to the **HTML surface only**: interactive widgets, live values, live
  streams, consolidated stops, commands (§4).

**Coverage at a glance** — every profile rule now shapes both surfaces (this used to be true of
headings only; hide and rename were speech-only until the surfaces were unified):

| Mechanism / rule | Speech surface | HTML surface |
| --- | --- | --- |
| Profile **Hide** | `TreeWalker.FindStop` | `ScreenWalker.WalkRec` |
| Profile **Rename** | `Announcer.GetDisplayAnnouncement` (Navigation/Core) | `DisplayText.Of` (+ dropdown `aria-label` via `ServerText.RenameOr`) |
| Profile **Heading** | `DisplayText.Of` + `DefaultScreenHandler` (H/Shift+H) | `HtmlRenderer.RenderNode` (`<h3>`, or `<h4>` when the rule's `level` is 3+) + `DisplayText.Of` |
| HTML feature (widget / live value / stream / command) | — | `Server/Features/*` + seams (§4) |

All three profile rules reach both surfaces through the `NavigationHost.ProfilePolicy`
capabilities (`IsHidden`, `RenameLabel`, `IsHeading`) wired once in `CoreEntry.cs` from the
active profile and read through the `NavigationManager.IsHidden` / `GetRenameLabel` /
`IsHeading` queries. `DisplayText.Of` (in the Navigation project) is the shared
text resolver both surfaces lean on, which is why heading and rename live there.

---

## 1. Decision path: which mechanism does my change need?

| If you want to… | Mechanism | Where | Surfaces affected |
| --- | --- | --- | --- |
| Remove a redundant / decorative stop | Profile **Hide** | `PanelProfiles/<Panel>.json` | Both |
| Fix a confusing or missing label on a stop | Profile **Rename** | `PanelProfiles/<Panel>.json` | Both |
| Mark a section landmark for H/Shift+H | Profile **Heading** | `PanelProfiles/<Panel>.json` | Both |
| Add a clickable/disclosure widget tied to a game element | HTML feature: Renderer + Handle (+ Route + JS) | `Server/Features/<Feature>.cs` | HTML |
| Add a value that updates in place as the game changes | HTML feature: `LiveValues.Track` → `values` SSE | `Server/Features/` / core | HTML |
| Add a *new* live data stream with its own push channel | HTML feature: new SSE source | extend `StreamEvents` (§4.5) | HTML |
| Add a play-by-play / status announcement | HTML feature: new live region + SSE source | extend shell + `StreamEvents` | HTML |
| Consolidate many stale stops into one live stop | HTML feature: new `ScreenWalker` item | extend `WalkRec` (§4.5) | HTML |
| Add a keyboard command / navigation action | HTML feature: Route + JS (and/or a speech input handler) | `Server/Features/` / Input | HTML (+ speech) |

Most real changes are a **combination** — e.g. a profile that hides three clutter stops *and* an
HTML feature that renders the remaining one as a rich widget. Walk each mechanism the change
needs. Reach for a profile rule first when the change is "which stops exist / what they're called
/ what's a landmark," because one declarative rule fixes both surfaces at once.

---

## 2. Cross-cutting rules (every change, both mechanisms)

These come straight from `CLAUDE.md`'s golden rules. A change that violates any of them is wrong
even if it "works."

1. **Degrade in place, never lie.** Never announce wrong or stale *gameplay* info as if it were
   real — but "I can't read this" is not a lie. If one element/value can't be read, degrade to an
   honest marker (`Degrade.Control`/`Value`/`Read`) and keep going; never fabricate or guess, and
   never silently swallow (always log via `Degrade.Note`). Reserve `AccessibilityMod.Kill()` for
   foundation failures where nothing the mod says can be trusted. See `MatchWatcher` for the
   canonical pattern (an unreadable commentary or score degrades to "unavailable", never a fabricated
   line). Full doctrine: [docs/reference/failure-handling.md](reference/failure-handling.md).
2. **Never cache game state.** Re-read live from the element/engine at render time and at every
   SSE snapshot. The only "cache" allowed is holding a reference to a live Unity object.
3. **Structural / identity matching only.** Never match a stop — or allocate an id — by text
   content, announcement, or panel numeric id. These change with every player, language, and save
   game. Match by element `name`, `type`, ancestor `path`, `classification`, or USS class; key
   ids off element identity (§3.3, §4.4).
4. **Tolerate missing elements.** A rule (or renderer condition) whose target is absent in the
   current view is a silent skip, not an error. Panels vary by data (goalkeeper vs outfield, on
   loan, foreign) — never assume a conditional element is present.
5. **Preserve all gameplay information.** The goal is to reorganise and consolidate, not to
   remove. Before hiding a stop, verify its information is reachable through another stop. Concise
   does not mean incomplete.
6. **Reuse game text; minimise mod-authored strings.** Prefer `DisplayText.Of(el)` and the
   `STRINGS`/`LocString` sources. Any mod-authored string must be concise per `CLAUDE.md`'s
   announcement rules: no positional counts, no obvious type suffixes, distinguishing word first,
   no emdash. Never alter, truncate, or reword *game* text.

HTML features carry four further cross-cutting rules because they run on background threads and
write to a live page; they are stated in full in §4.2.

---

## 3. Mechanism A — Panel profiles (declarative, both surfaces)

A panel layout profile customises navigation through a panel without changing the game's visual
UI. Each profile is a set of rules that **hide**, **rename**, or mark **heading** landmarks on the
navigation stops discovered from the element tree. Because the rules describe the panel
abstractly, they now apply to both the speech and HTML surfaces (see the §0 coverage table).

### How navigation works with and without a profile

When a panel loads, the mod walks its `VisualElement` tree top-to-bottom, depth-first. Every
element that qualifies as a navigation stop (button, text, checkbox, dropdown, …) joins a flat
list in tree order. That order follows the *visual* layout — designed for sighted players who scan
the whole screen at once — so read sequentially it is often unintuitive: related information
scattered, labels split from values, decorative elements cluttering the list.

A profile sits between the tree walk and the player and transforms that raw stop list into
something that makes sense read sequentially.

### 3.1 Panel identification

Every profile targets a panel by its **panel name** — the internal name from the panel's
`PanelID`, visible in:

- The `[PanelLoaded]` log line: `[PanelLoaded] name=PlayerReport ID=...`
- The F10 classify dump header: `========== PANEL: "Player Report" ==========`
- The F9 tree dump header: `Panel type: Panel, name: "PlayerReport", layer: Report (2)`
- The F10 cursor dump ancestor chain: `type=Panel name="TeamSquadReport"`

Use the `name` from the PanelID (e.g. `PlayerReport`, `SquadScreen`, `TeamSquadReport`), never the
translated title. The name is a code identifier; it never changes between languages, players, or
saves.

### 3.2 Stop identification

Match each stop by **structural properties** that stay stable across data. The F10 classify dump
shows every available field per stop:

```
[Button] type=SIButton name="ClubButton" text="" announcement="ClubButton, button"
  path=BindingSetter("ActiveClubIconSetter") > SIEnabled("ActiveClubEnabled") > ...
```

| Field | What it is | Stable? | Example |
|-------|-----------|---------|---------|
| `type` | C# class name | Yes | `SIButton`, `SIText`, `SICheckBox` |
| `name` | `name` attribute from the UI template | Yes | `ClubButton`, `ip-tab-button` |
| `path` | Ancestor chain (4 levels up) | Yes | `ButtonGroup("TabButtons") > SIVisible(...) > ...` |
| `classification` | How the mod classified it | Yes | `Button`, `Text`, `Checkbox`, `Toggle` |
| `text` | Current text content | **No** — per player/context | `"Georges Gustave"`, `"11"` |
| `announcement` | What the player hears | **No** — per player/context | `"HAI, button"` |

**Matching rules:**

1. **Match by `name` when available** — names like `ClubButton`, `player-pos-button` come from the
   template UXML and never change. Most reliable.
2. **Use `path` fragments when `name` is empty** — many stops have `name=""`. Match by a
   distinctive ancestor name (e.g. stops under `SIStyleSetter("TeamOrSubStyle")` are formation
   position buttons).
3. **Use `classification` to narrow broad path matches** — add `"Button"`/`"Text"` when a path
   fragment catches more than one type.
4. **Use `type` + position as a last-resort tiebreaker** for nameless stops sharing a path.
5. **Never match by `text` or `announcement`** — they change per player, language, and save.

**Conditional elements** (a "Fgn" flag only for foreign players, international caps only with
international data, loan details only on loan, goalkeeper vs outfield attribute categories) mean
every rule must tolerate a missing target — a skip, not an error (§2 rule 4).

### 3.3 Rule types

#### Hide — remove a stop from navigation

The element still exists and renders visually; the player simply can't land on it, and it is
absent from the HTML page. Use for redundant stops (same info/action reachable elsewhere),
purposeless labels, decorative elements misclassified as stops, or whole irrelevant groups.

**Before hiding, verify the information is reachable through another stop** (§2 rule 5). Hide
removes clutter, not information.

Find a stable identifier from the F10/F9 dumps and add a rule:

```json
"hides": [
  { "match": { "name": "ClubButton" } },
  { "match": { "parentName": "AddNewManager", "classification": "Text" } },
  { "match": { "pathContains": "TeamOrSubStyle", "classification": "Button" } }
]
```

Real examples: the redundant "Create new manager profile" text next to its button
(`GameCreationManagerSelectionScreen.json`, the `parentName` rule); the 26 formation position
buttons whose position is already in the table (`TeamSquadReport.json`, the `pathContains` rule).
Test that adjacent stops stay reachable (no dead zones) and that the matcher is stable across
different data.

#### Rename — replace a stop's label

Replaces the **label** portion of a stop's announcement; the role (e.g. "dropdown") and live state
(e.g. "0 of 4") still come from the adapter. Use for icon-only controls whose adapter picks up
unrelated sibling text, or auto-generated labels that are technically correct but confusing.

**Rename labels are static strings** describing what the stop *is*, not its current data. Good:
`"Advice actions"`. Bad: `"All, New, Tasks (0), Unread (7)"`. Rename does not affect cursor
restore — the original announcement is stored for position matching.

```json
"renames": [
  { "match": { "name": "dropdown-icon-base", "pathContains": "AdviceDropdown" },
    "label": "Advice actions" }
]
```

The player then hears `"Advice actions, dropdown, 0 of 4"`; the HTML surface uses the rename as the
control's label / `aria-label`. **Rename vs. fixing the adapter:** if a whole *class* of elements
is mislabelled, fix the adapter's `GetLabel()`; use Rename for a specific stop on a specific panel.

#### Heading — mark a stop as a landmark

Marks an existing stop as a structural landmark so the player can jump between sections with
H/Shift+H (speech) and the screen reader's heading navigation (HTML). The stop keeps its label,
role, activation, and position; **it is not announced any differently** during normal navigation —
heading status only affects jump behaviour.

The rule's `level` shapes the **HTML outline only**: level 0–2 renders `<h3>` (one below the
panel section's `<h2>`), level 3+ nests one deeper as `<h4>` — use it for per-item titles under
a level-2 section heading (a responsibility under its group). The speech surface's H-jump treats
every heading alike regardless of level.

```json
"headings": [
  { "match": { "pathContains": "MentalsFrame", "classification": "Text" }, "level": 2 }
]
```

Choose the matcher carefully: `pathContains` when the ancestor name is unique to the header;
`pathStartsWith` when the ancestor name appears in both the header and its children (matches a
specific depth, so children don't inherit heading status); `parentName` when the immediate parent
is distinctive. **Common pitfall:** `pathContains` on a container name matches every text element
inside it — use `pathStartsWith` for section titles that share an ancestor with their content.

### 3.4 Working with the F10 classify dump

`f10_classify.txt` is the primary authoring reference:

1. **Identify the panel** from the `========== PANEL: "..." ==========` header; take the profile
   name from the F9 tree dump's `name=` field (or the F10 cursor dump ancestor chain).
2. **List the current stop order** from the `NAVIGATION STOPS` section; note hide candidates
   (redundant/useless), rename candidates (bad/missing labels), and heading candidates (section
   landmarks).
3. **Find stable matchers** per target — `name` if present, else a distinctive `path` ancestor.
   Cross-reference `f9_tree.txt` for the full ancestor chain and `f10_cursor.txt` for the deepest
   chain up to the panel root.
4. **Test with different data** — re-dump on a different player/squad/formation and confirm
   `name`/`path` matches still hit, conditional absences cause no problems, and broad matchers
   don't catch stops that should remain.

### 3.5 File format

Profiles are JSON in `FM26Access.Plugin/PanelProfiles/`, named after the panel's PanelID name
(`PlayerReport.json`, `TeamSquadReport.json`), deployed alongside the plugin DLLs. **To load an
edit: `dotnet build` (deploys the JSON), then `POST /reload`** — a full game restart is not
required (verified live 2026-07-05). Speech-side renames may lag until the next companion render
re-activates the panel's profile.

```json
{
  "panelName": "TeamSquadReport",
  "headings": [
    { "match": { "pathContains": "MentalsFrame", "classification": "Text" }, "level": 2 }
  ],
  "hides": [
    { "match": { "pathContains": "TeamOrSubStyle", "classification": "Button" } }
  ],
  "renames": [
    { "match": { "name": "dropdown-icon-base", "pathContains": "AdviceDropdown" },
      "label": "Advice actions" }
  ]
}
```

The `match` object supports all `StopMatcher` fields; all specified fields must match (AND),
omitted fields are ignored:

| Field | Match type | Description |
|-------|-----------|-------------|
| `name` | Exact | Element's `name` attribute |
| `typeName` | Exact | C# class name (`"SIButton"`, `"SIText"`) |
| `classification` | Exact (case-insensitive) | Mod classification (`"Button"`, `"Text"`, `"Toggle"`) |
| `pathContains` | Substring | Ancestor path contains this string anywhere |
| `pathStartsWith` | Prefix | Ancestor path starts with this string |
| `parentName` | Exact | `name` of the element's immediate parent |
| `childIndex` | Exact | The element's index within its parent — the last-resort discriminator for template siblings identical in every other field (a row title and its description are often both `SIText("text")` under the same frame, differing only by position). Always pair it with a narrowing field. |

### 3.6 Authoring principles

1. **Structural matching only** (§2 rule 3) — the same profile must work for every player, team,
   and language.
2. **Tolerate missing elements** (§2 rule 4) — a missing target is a skip.
3. **Preserve all gameplay information** (§2 rule 5) — reorganise, don't remove.
4. **One profile per panel template** — a single file covers all of a panel's tab states and data
   variations via conditional rules; don't split "goalkeeper" vs "outfield" reports.
5. **Name profiles after panel names** — filename = PanelID name + `.json`, so the mapping is
   unambiguous.

### 3.7 Future rule types (not yet implemented)

Documented for when the time comes: **Merge** (combine adjacent stops, e.g. label + value into one
announcement), **Reorder** (change stop position), **Group / Pair Merge** (repeating merge for
uniform structures like attribute label+value pairs), **Regions** (scoped rule areas partitioning
a panel with independent ordering), **Tab-Conditional Regions**, **Synthetic Headings** (inserted
heading stops with no backing element).

---

## 4. Mechanism B — HTML features (code, HTML surface)

### 4.1 The architecture in one paragraph

A background thread (`AccessibilityServer`) serves HTTP on `127.0.0.1:8787` to the companion app's
WebView2. Every request is looked up in a **route table** and dispatched; any handler that touches
the game hops to the Unity main thread via `MainThreadDispatcher.Invoke` and blocks for the
result. The page loads **once**: a persistent shell (`HtmlRenderer.BuildHtml`) around a swappable
`#fmroot` body (`BuildBody`). `BuildBody` walks the live panel (`ScreenWalker`) into render items
— applying the active profile's hide rules as it goes — then `HtmlRenderer` turns each into HTML,
consulting registered feature renderers before its generic role switch and applying the profile's
heading/rename rules through `DisplayText.Of`. A long-lived **SSE stream** (`StreamEvents`) pushes
live updates (screen-change token, in-place value diffs, match commentary/score) which `app.js`
applies in place. Interactive elements register a **handle** (`ServerHandles`) so action requests
resolve back to the live element. `app.js` is organised into
`FM.core`/`dropdown`/`player`/`nav`/`pitch` modules with a small set of global shims the server
HTML calls inline.

### 4.2 HTML-feature cross-cutting rules (in addition to §2)

1. **Threading.** Touch Unity/IL2CPP objects *only* inside `MainThreadDispatcher.Invoke`. The
   accept loop and SSE loop run on background threads — calling `Log.*` (UnityEngine.Debug)
   off-thread hard-crashes IL2CPP. Use `ServerText.LogBackground` for background diagnostics. Never
   `Invoke` before `MainThreadDispatcher.Ready`.
2. **Escape everything that reaches the page.** Game text into HTML → `ServerText.EscapeText`
   (cleans Unity rich-text *then* HTML-escapes). Already-clean mod text → `ServerText.Escape`. A
   value in an inline `<script>` → `ServerText.JsEscape`. Anything on an SSE `data:` line →
   `ServerText.SseEscape`. Getting this wrong is a correctness/security bug.
3. **Ids from the shared pool, by identity.** Allocate via `LiveValues.AllocateId` or
   `ServerHandles.Register`/`RegisterKey` (keyed off `IdentityPath`). Stable across refreshes (so
   focus restoration survives) and collision-free between interactive and read-only ids. Never
   invent your own id scheme.
4. **Nothing persists across a body refresh.** `BuildBody` clears `ServerHandles` and `LiveValues`
   and closes any open dropdown session every render — re-register every render. Client-side, an
   in-flight interaction defers the refresh (`FM.core.dd`/`ddpend`) so an open widget is never
   wiped mid-use; respect that flag if your feature holds an open session.

### 4.3 Prefer the seams; extend carefully where there are none

The surface has three clean plug-in points where a feature registers without touching core
dispatch: **routes**, **element renderers**, and **handles** (via `FeatureRegistry` and the
`Handle` base). Use one when your feature fits — don't duplicate a core switch.

Some features have **no plug-in point yet** — a new live-update stream, a new consolidated stop, a
new live region. **That is fine, and you do not need to build new infrastructure.** Extend the
relevant core file directly, following the shape already there (the live scoreboard and play-by-play
work exactly this way and are perfectly acceptable).

> **Rule of three.** Hardcode the first one. A second is fine too. By the time you are adding the
> third of the same kind — and the core file is sprouting repeated feature-specific branches —
> extract a seam (§4.6). Don't pre-build seams you don't need yet.

So: **use a seam if one exists; otherwise add your case cleanly next to the existing ones** and
obey §2 + §4.2. Leave a `// TODO: extract a seam if a third lands` note if you can feel one coming.

### 4.4 Registering through existing seams

One feature = one `Server/Features/<Feature>.cs` + one `FM.<feature>` module in `app.js` (if it has
client logic).

- **Routes — `FeatureRegistry.RegisterRoutes`.** Add a `Route` to the dict. `ContentType`
  `text/plain` (a plain reply) or `text/html` (fragment); a fragment handler must return markup
  starting with `<` (`app.js` treats a leading `<` as a fragment, anything else as a plain-text
  reply). There is no status live region: the caller decides what a plain reply does — speak it
  through the in-game Prism TTS via `FM.core.say` (the `/speak` endpoint) for an on-demand read
  or an error, or consume it silently for a redundant confirmation.
  `LoadingBody` is served before `Ready`. Parse the query with `Query.Str`/`Query.Int`. Template:
  **`Features/PlayerCard.cs`** (`/player/panel`), backed by `DropdownSessionManager`.
- **Element renderers — `FeatureRegistry.RegisterRenderers` + `IElementRenderer`.** `CanRender(el)`
  must mirror the *full* condition (role *and* type), since renderers run before the generic role
  switch and first match wins. `Render(sb, el)` emits markup; allocate ids via
  `ServerHandles.Register`/`LiveValues`. Templates: **`PlayerCardRenderer`**,
  **`TacticsPitchPlayerRenderer`**.
- **Handles — `ServerHandles.Register`/`RegisterKey`.** Subclass `ServerHandles.Handle`.
  `TryValidate` gates every action (stale element → return a refresh message, never act). Override
  only the verbs your widget supports (`Activate`, `SetText`, `SetValue`, `Step`). If the widget
  opens a dropdown popup, override `TryGetDropdownAdapter` (keeps `ResolveDropdownHandle`
  feature-agnostic); reuse `DropdownSessionManager` for any native-select popup. Template:
  **`PlayerMenuHandle`** in `PlayerCard.cs`.
- **Live in-place value (leaf text) — `LiveValues`.** Render with an id and call
  `LiveValues.Track(id, el)` (or `RegisterReadOnly(el)` for headings/progress/plain text with no
  handle). The `values` SSE stream already diffs `LiveValues.Snapshot()` and `app.js` patches
  `textContent` by id — no new route, no new JS — *if* the element is leaf text.

### 4.5 Extending the parts with no seam yet

Add your case directly to the core file, next to the existing one, obeying §2 + §4.2. There is a
working example in each to copy.

- **New live stream (SSE) — extend `AccessibilityServer.StreamEvents`.** Read your value inside the
  loop's `Invoke` block, keep a `last…` local, emit `event: <name>\ndata: <SseEscape(value)>\n\n`
  when it differs. Client side: add `FMES.addEventListener('<name>', …)` in the `app.js` wiring
  section. Copy the `commentary`/`score` blocks (data from `MatchWatcher`).
- **New consolidated stop — extend `ScreenWalker.WalkRec`.** Add recognition near the top of
  `WalkRec`, emit a new `RenderItem` subclass, and add the matching branch in `HtmlRenderer`. Copy
  the scoreboard block (the `"scorebar"` / ScoreBug `DynamicItem` check) + `ScoreboardItem` + `HtmlRenderer.RenderScoreboard`.
- **New ARIA live region — extend the shell in `HtmlRenderer.BuildHtml`.** Add a
  `<p id="…" aria-live="polite">` (or `assertive`) and feed it from a live stream that targets that
  id. Copy the `#fmlive` region + its `commentary` SSE handler.

### 4.6 Promoting a repeated pattern to a seam (only at the third)

When a third case of the same kind lands, extract a seam — exactly how `RegisterRenderers` was
added in Step 3 of `docs/archive/plans/HTML surface refactor plan.md`:

1. Define a small contribution type (interface, or a delegate-holding class like `Route`).
2. Add `FeatureRegistry.Register<Thing>(List<…>)`.
3. Have the core file build the list once and **iterate it** where the hardcoded branches were.
4. Move the existing hardcoded cases into registered contributions in the same change.
5. Keep the crash/threading/escaping rules intact inside each contribution.

Optional and demand-driven. Two hardcoded live streams are fine; a seam earns its keep at the
third.

### 4.7 Client-side (JS) protocol

`app.js` ships as one embedded `<script>`; there is no build step or module loader.

- **One module per feature:** `FM.<feature> = { … }`, named to match the C# `Features/` file.
- **Call siblings as `FM.x.y(...)`, never `this.y(...)`** — methods are passed around as bare
  references (event handlers, `onKey`), so `this` is unreliable.
- **No new top-level globals.** Shared interaction state lives on `FM.core` (`dd`, `ddpend`,
  `player`, …) so there is one owner of "a session is open, defer refreshes." Reuse it.
- **Keyboard:** register via `FM.core.onKey(fn)`; return `true` when you handled the event (after
  `preventDefault`). First handler to return `true` wins, so mind order (dropdown is registered
  before nav for this reason).
- **Inline-called functions:** if the server HTML calls your function in an `onclick`/`onchange`,
  expose a thin **global shim** delegating into your module (see the shim block at the bottom of
  `app.js`: `refresh`, `act`, `fmplayer`, …). That global list is the surface's public API
  contract — keep it small and documented.
- **`FMTOKEN`** stays a server-set global; read/write it through `FM.core`, don't redeclare it.
- **New SSE streams:** add the `FMES.addEventListener` in the wiring section; politeness comes from
  the live region's `aria-live` in the shell.

### 4.8 Canonical templates (read these, copy their shape)

| Mechanism | Read |
| --- | --- |
| Interactive disclosure (renderer + handle + route + JS) | `Features/PlayerCard.cs` + `FM.player` in `app.js` |
| Disclosure with no route (client-only toggle) | `Features/TacticsPitchPlayer.cs` + `FM.pitch` |
| Route + HTML fragment + game session | `PlayerCard` `/player/panel` + `DropdownSession.cs` |
| Handle + dropdown resolution | `PlayerMenuHandle` (`PlayerCard.cs`) + `Handle.TryGetDropdownAdapter` |
| Live in-place value (leaf text) | `LiveValues.cs` + the `values` SSE path in `StreamEvents`/`app.js` |
| Disclosure with an ARIA grid + multi-frame server-validated apply session | `Features/DateField.cs` + `DateSelectorSession.cs` + `FM.date` |
| Live consolidated stop + custom stream | `ScreenWalker.ScoreboardItem` + `MatchWatcher` + `score`/`commentary` |
| Promoting a repeated pattern to a seam | Step 3 of `docs/archive/plans/HTML surface refactor plan.md` (`RegisterRenderers`) |

When you add a seam or a new canonical example, update this table and `docs/archive/plans/HTML surface refactor
plan.md`.

---

## 5. Testing & verification

- **Make it testable: push pure logic into the Navigation project.** Profile matching
  (`StopMatcher`, `PanelProfile`, `ProfileRegistry`) and any HTML/text building with no game
  dependency (`DisplayText`, `DropdownHtml`) live in Navigation, covered by `FM26Access.Tests`,
  which runs without the game. Add tests there; every test must have a real failure mode not
  already covered by another.
- **Plugin-side and JS paths are not unit-testable** (they need the game) — they require the
  in-game pass.
- **JS has no compiler or tests** — run `node --check app.js` for syntax, then a wide manual pass.
  Treat any JS change as the highest-risk part of a feature.
- `dotnet build` and `dotnet test FM26Access.Tests` must be green before the in-game pass.
- **In-game pass:** exercise your change *and* a regression sweep of neighbours. For a profile:
  navigate the panel on both surfaces with different data and confirm hides/renames/headings land
  and stability holds. For an HTML feature: a standalone dropdown, a table-cell dropdown, ribbon
  nav, a screen change while a session is open, the live score/commentary — the surface's parts
  share state, so confirm you didn't disturb them. **After editing a profile: `dotnet build` +
  `POST /reload` re-reads it** (§3.5) — no game restart needed.

---

## 6. Pre-flight checklist

Run this before opening a change. Every box is a rule from above.

**Both mechanisms:**

- [ ] Degrade-in-place applied: an unreadable element/value degrades to an honest marker (logged via
      `Degrade.Note`), never a fabricated value and never a silent swallow; `Kill` only for
      foundation failures; supplementary ones `Warn` + honest omission.
- [ ] No cached game state; re-read live at render and at each SSE snapshot.
- [ ] Structural / identity matching only — no `text`/`announcement`/numeric-id matching; ids keyed
      off identity.
- [ ] Rules tolerate missing elements (silent skip).
- [ ] No information removed — every hidden stop's info is reachable elsewhere.
- [ ] Game text reused where possible; any mod-authored string is concise per `CLAUDE.md`.
- [ ] Pure builders (profile matching / text building) live in Navigation with tests; `build` +
      `dotnet test` green; in-game pass (change + regression sweep) done on the affected surface(s).

**Profile changes (Mechanism A):**

- [ ] One file per panel template, named after the PanelID; matchers verified stable across
      different data via fresh F10 dumps.
- [ ] Game restarted to load the edited profile before the in-game pass.

**HTML feature changes (Mechanism B):**

- [ ] One `Server/Features/<Feature>.cs` (+ one `FM.<feature>` in `app.js` if it has client logic);
      no feature logic leaked into core files.
- [ ] Used an existing seam where one fits; did not duplicate a core switch a seam already covers.
      Where no seam exists, extended the core file cleanly next to the existing case (§4.5) — seam
      extraction considered only at the third of its kind (§4.3).
- [ ] All Unity reads inside `MainThreadDispatcher.Invoke`; no `Invoke` before `Ready`; background
      diagnostics via `ServerText.LogBackground`.
- [ ] Every game string reaching the page escaped
      (`EscapeText`/`Escape`/`JsEscape`/`SseEscape` as appropriate).
- [ ] JS: `FM.<feature>` module, `FM.x.y` calls only, `onKey` for keys, global shim for
      inline-called functions, no new top-level globals; `node --check` clean.
