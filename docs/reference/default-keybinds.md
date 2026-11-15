> Upstream FM26 Access reference, release 0.2.16. Check [FM27 migration notes](../fm27-migration.md) before using game-specific APIs.

# FM26 Default Keybinds

Known keyboard shortcuts in Football Manager 2026. This list is maintained so the mod can make deliberate, documented decisions when overriding game hotkeys.

> **Sources:** Community guides, SI bug tracker, decompiled source. Entries marked **(unconfirmed)** need in-game verification. FM26 shipped with many shortcuts broken vs FM24 — SI is restoring them over patches.

---

## Main Screen Navigation (Number Keys)

| Key | Action | Status |
|-----|--------|--------|
| `1` | Portal | confirmed |
| `2` | Squad | confirmed |
| `3` | Recruitment | confirmed |
| `4` | Match Day | confirmed |
| `5` | Club | confirmed |
| `6` | Career | confirmed |

## Bookmarks (Shift + Number)

| Key | Action | Status |
|-----|--------|--------|
| `Shift+1` | Bookmark 1 | confirmed |
| `Shift+2` | Bookmark 2 | confirmed |
| `Shift+3` through `Shift+6` | Bookmarks 3-6 | confirmed |
| `Shift+7` through `Shift+0`, etc. | Bookmarks 7+ (up to 24 bookmarks exist) | unconfirmed |

## Quick Access Letters

| Key | Action | Status |
|-----|--------|--------|
| `H` | FMPedia | confirmed |
| `C` | Calendar | confirmed |
| `U` | Messages (Inbox) | confirmed | Mod hooks: after game opens unread message, cursor jumps to MessageTitle |

## General / System

| Key | Action | Status |
|-----|--------|--------|
| `Space` | Continue (advance time) | confirmed |
| `Shift+Space` | Go on holiday | confirmed |
| `Escape` | Back / close current panel | confirmed |
| `Enter` | Confirm / submit / activate focused element | confirmed |
| `Arrow Keys` | Navigate between focusable elements | confirmed |
| `Ctrl+S` | Save game | confirmed |
| `Ctrl+Shift+R` | Load last save | unconfirmed |

## Screen Shortcuts (Ctrl/Shift/Alt Combos)

| Key | Action | Status |
|-----|--------|--------|
| `Shift+T` | Squad > Training | confirmed |
| `Shift+P` | Match Day > Penalties | confirmed |
| `Ctrl+R` | Squad > Registration | confirmed |
| `Ctrl+D` | Match Day > Data Hub | confirmed |
| `Ctrl+Shift+R` | Portal > Opposition Report | confirmed |
| `Ctrl+Shift+D` | Squad > Dynamics | confirmed |
| `Ctrl+Shift+F` | Recruitment > Recruitment Focuses | confirmed |
| `Ctrl+Shift+V` | Club > Club Vision | confirmed |
| `Ctrl+Shift+B` | Club > Board | confirmed |
| `Ctrl+Alt+O` | Match Day > Opposition Instructions | confirmed |
| `Ctrl+Alt+P` | Recruitment > Squad Planner | confirmed |
| `Ctrl+Alt+A` | Club > Affiliates | confirmed |
| `Ctrl+Alt+N` | Squad > Squad Numbers | confirmed |
| `Ctrl+Alt+M` | Squad > Medical Centre | confirmed |

## Function Keys (Currently Broken in FM26)

These worked in FM24 and earlier. SI bug tracker confirms they are missing/broken in FM26 as of early access. They may be restored in patches.

| Key | Expected Action | Status |
|-----|----------------|--------|
| `F1` | FMPedia | broken in FM26 |
| `F2` | Portal > Overview | broken in FM26 |
| `F3` | Squad > First Team | broken in FM26 |
| `F4` | Portal > Messages | broken in FM26 |

## Legacy Shortcuts (FM12-FM24, Status Unknown in FM26)

These existed in prior FM versions. FM26's Unity rewrite may have dropped or changed them. Need in-game verification.

| Key | Action (in prior FMs) | Status |
|-----|----------------------|--------|
| `Ctrl+Shift+S` | Save game as | unknown |
| `Ctrl+O` | Load game | unknown |
| `Ctrl+Shift+N` | Start new game | unknown |
| `Ctrl+T` | Tactics | unknown |
| `Ctrl+F` | Search | unknown |
| `Ctrl+L` | League table | unknown |
| `Ctrl+B` | Backroom advice | unknown |
| `Ctrl+U` | Training > Players | unknown |
| `Shift+C` | Calendar | unknown (may be just `C` now) |
| `Shift+O` | Finances > Summary | unknown |
| `Shift+P` | Preferences | unknown (conflicts with Penalties) |
| `Shift+L` | Match speed: Fast | unknown |
| `Shift+K` | Match speed: Medium | unknown |
| `Shift+J` | Match speed: Slow | unknown |
| `Alt+F9` | Save screenshot | unknown |
| `Ctrl+Q` / `Alt+F4` | Exit game | unknown |

---

## Mod Overrides

When the mod overrides a game hotkey, document it here.

| Key | Original Game Action | Mod Action | Rationale |
|-----|---------------------|------------|-----------|
| `F8` | (broken in FM26) | Clear unclassified element tracker | Debug tool — resets tracker for a fresh session |
| `F9` | (broken in FM26) | Dump full tree + classification report to files (`f9_tree.txt`, `f10_classify.txt`) | Debug tool — raw tree plus all nav stops/containers/unknowns with announcements |
| `F10` | (broken in FM26) | Dump cursor context to file (`f10_cursor.txt`) | Debug tool — current element's ancestors, siblings, children, nearby stops |
| `F11` | (broken in FM26) | Dump unclassified element tracker to file (`f11_unclassified.txt`) | Debug tool — element types seen during navigation that the classifier did not recognize |
| `F12` | (broken in FM26) | Ad-hoc diagnostic (currently disabled) | Placeholder hook for one-off diagnostics |

Handled in `Input/Handlers/DefaultScreenHandler.cs`; implemented in `DebugTools.cs`. The F9 tree
and classification walks, and the cursor dump, share `Diagnostics/TreeDump.cs` and
`Diagnostics/ClassificationDump.cs` with the dev driver's `/gui`, `/panels` and `/classify`
endpoints, so an agent can read the same views over HTTP without pressing a key (see
`docs/claude-in-game-verification.md`).

---

## HTML Surface Chords (Companion App)

Keys handled client-side by `app.js` in the companion app's WebView2, not by the game. They are
distinct from the in-game BepInEx hotkeys above. All use `e.code` so keyboard layout and Shift
never munge them, and are confirmed to pass through the screen reader's browse mode. Bare
printable keys are NOT viable here: NVDA's browse mode consumes them (quick-nav keys) before
the page sees them — bare `\` and `` ` `` were tried for the continue announce and each worked
in only one NVDA mode, hence the Alt chord.

| Key | Action | Module |
|-----|--------|--------|
| `Alt+1`…`Alt+6` | Fixed mod destinations (2026-07-24, replacing the ribbon-category chords, which reopened each section wherever it last was): `Alt+1` Messages, `Alt+2` First Team, `Alt+3` Tactics (Match Day), `Alt+4` Player Database, `Alt+5` Staff (Club), `Alt+6` Fixture Schedule. All six route through `/matchchord` (owned by `FM.matchchord`), which performs the navigation server-side whenever no match is live, via the Navigator's live-resolved subtab dispatch (`NavigatorIndex.GoToUid` — PanelID uids are stable across saves and languages; First Team's club object id is resolved live). An unavailable destination is spoken honestly ("First Team is not available right now"). The six ribbon SECTIONS (Portal…Career) stay reachable through the Menu browser (`Alt+M`) and the on-page ribbon buttons | `FM.matchchord` |
| `` Alt+` ``, `Alt+1`…`Alt+7` (during a match) | Context-sensitive override: while a match engine is live (decided server-side by `/matchchord` — the rendered scoreboard is per-competition and not a reliable match signal) these report live match info, relative to the player's team (else the home team). `` Alt+` `` = the scoreboard line the page's top heading shows — team names, score, clock ("Arsenal 1 - 0 Chelsea, 63:12"), read live from the engine; a quick **double tap** speaks the match events instead — goals ("Goals. Khous, USO, 34 minutes, assisted by Diako."), then cards (bookings only while their owner is still on the pitch, dismissals always), then injuries without minutes, each group in the order they happened, read from the engine's own classified event log and gated to the PLAYBACK slice so the simulation frontier can never spoil a goal. `Alt+1` = your on-pitch players' three best match ratings ("best rated: Hamilton 7.4, Saputo 7.1, Wilson 6.9"); a quick **double tap** (same chord within 450 ms) speaks the three lowest instead ("lowest rated: …"), its speech interrupting the single-tap readout. `Alt+2` = the same for condition, as percentages (best three; double tap = lowest three). `Alt+3` = the body-language overview — players per icon band, most populous first, zero bands omitted ("body language: 6 neutral, 2 very positive, 2 negative, 1 positive"); double tap = the detail: each non-neutral player with the game's detailed word ("Vázquez Complacent, Wilson Nervous", or "all players neutral body language") — the squad readouts all read live from the in-match Your Squad panel's binding values (red-carded and subbed-off players excluded; unreadable players spoken as a trailing "N players unreadable" gap). `Alt+4` = the match conditions — attendance with its share of capacity, the away following, then the weather ("Attendance: 3,189, 47% of capacity. 341 away fans. 8 degrees, drizzle."), read live from the engine's stadium setup and the graphics scene's lighting/weather setup (attendance and weather are fixed at kick-off; nothing is cached); an empty away end is stated ("No away fans") so it is distinguishable from a failed read, and each clause degrades on its own ("weather unavailable") rather than shortening the line silently. A quick **double tap** speaks both crowds' moods instead, home first, in the engine's own 26-state vocabulary ("home crowd idle happy, away crowd minor appreciation"). Wind and pitch condition are deliberately NOT spoken: both are readable but neither has an enum, threshold table, or any consumer in the game's managed code, so there is no scale to anchor a spoken band against — see `docs/reference/live-match-data.md`. `Alt+6` is unassigned during a match. `Alt+5` = possession then ball position in one line: possession by our own nearest-player criterion (the game exposes none) — "&lt;name&gt;, your team/opposition" when someone is on the ball, else "loose ball, nearest &lt;name&gt;", "contested, &lt;a&gt; and &lt;b&gt;" (both teams within 1.5 m), or "ball in the air"; only meaningful during a highlight/replay when player objects exist, degrades to "possession unavailable" otherwise — followed by the ball third plus flank (e.g. "attacking third left", "middle third central"), superseded by a penalty or six yard box when the ball is inside one ("in opposition box", "in own six yard box"). `Alt+7` = distance (metres from the attacked goal). When the ball has left the field the location readouts report it: `Alt+5` names the exit ("out of play, attacking third right", "goal, opposition net") and says "out of play, &lt;nearest player&gt;, &lt;team&gt;" for possession; `Alt+7` prefixes "out of play". Outside a match `/matchchord` performs the ribbon navigation instead for `Alt+1`…`Alt+6`; `` Alt+` `` and `Alt+7` answer "Not in a match." | `FM.matchchord` |
| `Alt+C` | Open the calendar (clicks the ribbon date/time button) | `FM.nav` |
| `Alt+H` | Open the Go on vacation panel (H for Holiday). Calls the very handler the game's own Shift+Space runs (`GameWorldShortcutsModule.OnGoOnHolidayShortcut` via `/holiday`), which fires the module's own open-panel event, so the flow is identical to the native shortcut. Success is silent — the vacation card rendering is the feedback; refused while a sim is processing (the direct call would bypass the game's action-map block), and a missing module is spoken honestly | `FM.nav` |
| `Alt+P` | During a match (same `/matchchord` engine gate as the digits): toggle match playback by pressing the game's own play/pause button, speaking the direction observed at press time ("pausing" / "resuming"; "play pause pressed" when the button's sprite state is unreadable, "play pause unavailable" when the button is missing). Each tap is its own press, so a quick double tap flips twice — natural toggle semantics. Outside a match: open the FM menu / pause menu (clicks the ribbon settings button), as it always has | `FM.matchchord` |
| `Alt+M` | During a match: open the match preferences card by pressing the settings button on the game's playback strip (`MatchPlaybackControls`); success is silent — the card's arrival announcement is the feedback. Outside a match (M for Menu), THE **Menu browser** key, three-way: an open modal Navigator closes (toggle); a page carrying the title bar's Menu browser focuses it, landing on the player's last tree position (roving tabindex) or the first category; a page without one (no title bar) opens the modal Navigator with focus landing on the tree — so the widget is reachable from anywhere outside a match, ready for type-to-filter + arrows + Enter. "Menu browser unavailable." where the navigation model itself is absent (the main menu). The in/out-of-match split is decided server-side (`/matchchord`), so match behaviour is untouched | `FM.matchchord` → `FM.navigator` |
| `Alt+Left` / `Alt+Right` | On the standalone Messages panel: `Alt+Right` focuses the message body, `Alt+Left` the currently open email's label in the list (spatially: right into the content, left back to the list). Elsewhere inert (swallowed): HTML panel cycling is retired — move between panels by heading (H) — but the chord is still caught so `Alt+Left` does not trigger the WebView's browser-Back. | `FM.messages` / `FM.nav` |
| `Alt+[` / `Alt+]` | Screen history back / forward (clicks the on-screen Back / Forward button) | `FM.nav` |
| `Alt+I` | Speak the **focused** button's secondary text (the description hidden to keep the label short) through the in-game Prism TTS; re-speaks the primary label when there is no secondary. Repeats on every press | `FM.describe` |
| `Ctrl+1`…`Ctrl+9`, `Ctrl+0` (slot 10), `Ctrl+-` (11), `Ctrl+=` (12) | Open MOD bookmark N (2026-07-24, replacing the game-strip remap): the slots are the mod's own store (`ModBookmarks`, persisted to `BepInEx/plugins/FM26Access/bookmarks.json` — the mod's first user-data file), where ANY destination from the Menu browser tree can be bookmarked, not just the game catalog's ~29 tools. Edited via the **Edit bookmarks** button (title bar), which opens the mod-owned editor: remove rows with their chord names, Move up/Move down for slot order, and every live tree destination grouped by category as add rows. Navigation goes through the Navigator's live-resolved dispatch (`NavigatorIndex.Go`), so a stale entry fails honestly. Success feedback is the screen change ("Opening X" is suppressed, matching `Alt+1`…`Alt+6`); an empty slot ("No bookmark 5, only 3 set") or a failed resolve is spoken honestly. The game's bookmark strip is hidden on the HTML page (clutter; the game's own system and Shift+N stay untouched in-game). `preventDefault` keeps Ctrl+- / Ctrl+= out of the WebView zoom accelerator — needs one NVDA confirmation pass | `FM.bookmarks` |
| `Alt+\` | Speak the continue button's current label (the context-sensitive flow action: "Continue", the team-talk / match-flow steps, ...) plus the game date and time shown beside it in the title bar (omitted where absent, e.g. the match HUD). Mirrors the speech surface's Tab. An Alt chord because NVDA's browse mode swallows bare printable keys — bare `\` and `` ` `` were tried and reached the page in only one mode | `FM.continuebtn` |
| `Alt+Enter` | Press the continue button, wherever it currently lives. Mirrors the speech surface's Shift+Tab jump plus Enter. Success is silent (the screen change and the mod's "Simulating" announcements are the feedback); a missing or disabled button is spoken honestly | `FM.continuebtn` |
| `Alt+S` | Quick save (S for Save; moved off `Ctrl+S` 2026-07-24 per user — Ctrl is the bookmark tier and headed for user-defined shortcuts, Alt is the mod tier). Calls the very handler the game's own in-game Ctrl+S runs (`SaveGameHandlerModule.OnSaveGameInput` via `/save`), so the flow is identical to saving in-game: overwrite the current career file (the game keeps its own overwrite backup), or the game's own no-file handling when there is none. Speaks "Saving" on trigger; "saving not available right now" when the game's CanSave gate is off, and refused while a sim is processing (the direct call would bypass the game's own action-map block) | `FM.save` |

`Alt+I` acts on the element with DOM focus (reached via **Tab**), not the NVDA browse cursor: a
screen reader's browse position (B / arrow navigation) is not exposed to JavaScript, so it cannot
be targeted. See `docs/archive/alt-i-browse-probe.html` for the experiment that established this.

**Companion announcements go through Prism, not an aria-live region.** On-demand reads (Alt+I, the
search result count, calendar month/year and date-pick feedback) and action errors are spoken
through the in-game Prism TTS via the server's `/speak` endpoint (`FM.core.say`), not a browser
live region. Routine action confirmations — a successful activation's "Activated X" — are not
announced at all, since the screen reader already conveys the control and its new state. The
companion page therefore has no status live region; only the `#fmlive` match play-by-play remains.

---

## Programmatic Extraction

The authoritative source for keybindings is the Unity Input System `InputActionAsset` loaded at runtime. To dump all bindings programmatically from within the mod:

```csharp
// In a BepInEx plugin or Harmony patch, after GameInputManager is initialized:
var inputManager = GameInputManager.Instance;

// Option 1: Iterate InputRebindingConfig (the rebindable subset)
// InputRebindingConfig is a ScriptableObject with a list of InputRebindingData.
// Each entry has an InputActionReference (the action) and binding IDs.

// Option 2: Dump the full InputActionAsset
// GameInputManager inherits from InputManager which holds InputActionAssets.
// Walk all action maps and actions:
foreach (var actionMap in inputManager.asset.actionMaps)
{
    foreach (var action in actionMap.actions)
    {
        foreach (var binding in action.bindings)
        {
            Log.Info($"{actionMap.name}/{action.name}: {binding.effectivePath}");
        }
    }
}
```

Key classes to look at in decompiled source:
- `GameInputManager` — the singleton that owns the Input System state
- `InputRebindingConfig` — ScriptableObject listing all rebindable actions
- `InputManager.asset` — the `InputActionAsset` with all action maps
- `SIInputActionReference` — wraps addressable asset ref + action ID

The dump should be done once at mod startup (after `GameInputManager` initializes) and logged to `BepInEx/LogOutput.log`. This gives us the ground truth for every binding without relying on community guides.

---

## Notes

- FM26 uses Unity's Input System with `InputRebindingConfig` / `InputRebindingData` for rebindable actions.
- Many shortcuts are context-sensitive (`ContextShortcutsElement` binds actions per-panel).
- The `ControllerShortcuts` panel layer (layer 11) renders shortcut icons for controller users.
- FM26 shipped with many legacy shortcuts broken. SI is restoring them across patches — this list will need periodic updates.
