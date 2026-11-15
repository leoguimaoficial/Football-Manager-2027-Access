> Upstream FM26 Access reference, release 0.2.16. Check [FM27 migration notes](../fm27-migration.md) before using game-specific APIs.

# Debug Tools Explainer

The FM26 accessibility mod includes built-in debug tools that dump the game's UI state to files. These tools help diagnose what a blind player would experience on any given screen by revealing the full UI element tree, how elements are classified for navigation, where the cursor is, and what element types the mod doesn't recognize.

**The dev driver is the primary path now; the F-keys are a manual fallback.** Every view below is available **over HTTP** without pressing a key or opening a file: `GET /gui` (raw active-panel tree), `GET /panels` (every open panel across all layers), `GET /classify` (the active panel's classification, the inline equivalent of F9's `f10_classify.txt`), `GET /unclassified` (the unclassified tracker; `?clear=1` resets it — the F8/F11 pair), and `GET /read` (the whole screen linearized to a screen-reader-style reading block). The F-key file dumps and the HTTP endpoints share the same walkers (`Diagnostics/TreeDump.cs` and `Diagnostics/ClassificationDump.cs`), so they show the same thing. See `docs/claude-in-game-verification.md`. The keys are mainly useful to a **sighted developer sitting at the game** who wants a snapshot to disk without an agent attached; an agent should drive over HTTP instead.

## Output Location

All dump files are written to:

```
<game install>/BepInEx/plugins/FM26Access/dumps/
```

Default path: `C:\Program Files (x86)\Steam\steamapps\common\Football Manager 26\BepInEx\plugins\FM26Access\dumps\`

Each keypress **overwrites** the previous file. The section headers and summary lines are also echoed to the Unity Player.log; the bulk per-element tree and classification bodies are written only to the files (logging thousands of element lines per F9 floods the log without adding anything the files do not already hold). The dedicated files are the primary way to read the dumps.

## Keybindings and Output Files

| Key | Action | Output File(s) |
|-----|--------|----------------|
| **F8** | Clear unclassified tracker | (none — resets the tracker in memory) |
| **F9** | Full tree dump + classification report (all layers) | `f9_tree.txt` + `f10_classify.txt` |
| **F10** | Cursor context dump | `f10_cursor.txt` |
| **F11** | Unclassified element tracker dump | `f11_unclassified.txt` |
| **F12** | Interactive-panel gate readout | `f12_interactive.txt` |

Note: F9 produces two files (`f9_tree.txt` for the raw element tree and `f10_classify.txt` for the classification report). All other keys write to a single file. F12 (`DumpAdHoc`) walks every open panel and reports the interactive-set gate (`IsOpenAndInteractive`) per panel plus the computed interactive set, writing `f12_interactive.txt` and speaking a one-line summary.

## F9: Full Tree Dump + Classification Report

F9 triggers two sequential operations that dump **all panel layers**, not just the active panel. This means the ribbon, tooltips, overlays, and every other open panel across all 12 PanelManager layers are included.

### f9_tree.txt — Raw Element Tree

Dumps every `VisualElement` across all open panels, recursively, organized by layer. For each element:

- **Type name** (e.g., `SIButton`, `SIText`, `VisualElement`)
- **Element name** (the `name` attribute, e.g., `"NameText"`, `"column-headers"`)
- **Visibility** (`visible=True/False`)
- **Enabled state** (shown as `DISABLED` if disabled)
- **Opacity** (shown only if less than 1.0)
- **Child count**
- **Text content** (for `TextElement` subclasses — shown as `text="..."`, truncated to 80 chars)
- **Interfaces** (for non-trivial types — skipped for plain `VisualElement` and `TemplateContainer`)
- **USS classes** (checked against a known set of class names)

The output is indented by depth to show parent-child nesting. Each panel ends with a stats summary (total/visible/hidden element counts, max depth, text element count, type distribution). The file ends with aggregate stats across all panels.

Structure:
```
========== ALL LAYERS TREE DUMP ==========
---------- LAYER: Background (0) ----------
========== PANEL: "panel title" ==========
  [depth] TypeName name="..." visible=... children=...
  ...
--- PANEL STATS ---
  ...
---------- LAYER: Menu (1) ----------
  ...
========== AGGREGATE STATS ==========
  ...
```

**When to use:** When you need to find a specific piece of data on screen — whether it exists in the UI tree at all, what type it is, how deep it's nested, which layer it's on, and what its parent structure looks like.

### f10_classify.txt — Classification Report

Runs the mod's actual classification logic (`ElementBridge.Describe` + `ElementClassifier.Classify`) on every visible element across all layers and sorts results into buckets per panel:

- **Navigation Stops** — Elements the cursor will land on. Each entry shows:
  - Classification type (Button, Text, Dropdown, Breadcrumb, Image, Checkbox, Slider, etc.)
  - The element's type name and name attribute
  - Raw text content
  - The actual announcement the player would hear (via `GetAnnouncement()`)
  - Ancestor path (4 levels up, for locating it in the tree)

- **Containers** — Structural groupings (Section, ButtonBar, RadioGroup, Table) that organize stops into logical regions.

- **Layout Wrappers** — The hundreds of binding/layout elements skipped during navigation. Shown as a type-name frequency table.

- **Unclassified/Unknown** — Elements the classifier returned `Unknown` for. Ideally this section says `(none)`.

- **Hidden Subtrees** — Top-level elements (depth < 10) marked `visible=False` that have children. Useful for spotting conditional content areas.

- **Summary** — Per-panel counts of stops, containers, wrappers, unknowns, internal children, and hidden subtree roots, plus a breakdown of classifications by type. Ends with an aggregate summary across all panels.

After writing the file, F9 also speaks a summary: total panels, layers, elements, stops, containers, and unknowns.

**When to use:** This is the most actionable dump. It directly answers: "What will the blind player hear on this screen? What can they navigate to? What are they missing?" Compare the navigation stops against what's visible on screen to identify gaps.

## F10: Cursor Context

Shows detailed information about where the accessibility cursor is currently positioned. Writes to `f10_cursor.txt`.

- **Current element** — Role and announcement
- **Backing element** — The underlying `VisualElement` type and name
- **Visibility and style** — `display`, `visibility`, `opacity` from resolved styles
- **Layout** — Position (x, y) and size (width, height) in the UI
- **Ancestor chain** — Every parent element from the current element up to the panel root, with type, name, visibility, and child count
- **Siblings** — All children of the current element's parent, with the current one marked `<<< CURRENT`. Shows sibling text content if any.
- **Children** — If the current element has children, shows 3 levels deep of its subtree
- **Nearby stops** — The previous and next navigation stops (what the player would land on by pressing up/down arrow). Shows their role and announcement.

If there is no active cursor, speaks "no cursor" and writes nothing.

**When to use:** When debugging navigation order ("why does pressing down from here go to X instead of Y?"), verifying cursor placement after panel entry, or understanding why a particular element announces what it does.

## F11: Unclassified Element Tracker

Dumps the runtime accumulator of element types the mod encountered during actual navigation but couldn't classify. Writes to `f11_unclassified.txt`.

For each unclassified type:
- **Type name** and **hit count**
- **Interfaces** implemented
- **USS classes** found on instances
- **Panel titles** where encountered (up to 5)
- **Example element name**

If no unclassified elements have been tracked, speaks "no unclassified elements tracked" and writes nothing.

**When to use:** After navigating through several screens, press F11 to see if there are element types the mod is silently skipping. High hit counts suggest a common element type that should be classified.

## F12: Interactive-Panel Gate Readout

Phase 0 validation for the "remove panel cycling" plan (`docs/archive/plans/removing panel cycling plan.md` §10). For every open panel across all layers, dumps its layer, root `visible`, `Panel.IsVisible`, `Panel.IsActive`, and the game's own `PanelManager.IsOpenAndInteractive(PanelID)` — then the **computed interactive set** (panels that are both visible and report interactive). Writes to `f12_interactive.txt` and speaks "N open panels, M interactive."

This is the signal the future single-page HTML surface will gate on: only panels in the interactive set should reach the page. Use it to confirm, on each scenario, that exactly the right panels qualify:

- **Modal dialog up** → only the dialog is interactive (the screen beneath is `visible=True` but inert).
- **Card over a report** → only the card is interactive (the report beneath is `visible=True` but blocked, with no scrim to detect).
- **Plain squad screen** → title bar + main screen + report all interactive; the dormant `ProcessingCard` (LoadingScreen layer, `visible=False`) is excluded.

If `IsOpenAndInteractive` reports `THREW: ...` for a panel, the gate signal is unreliable and the plan's fallback (§6.2) is needed. Diagnostic only — it never feeds speech, and reports a thrown gate call rather than crashing so the failure is visible.

## F8: Clear Unclassified Tracker

Resets the unclassified tracker to empty. No file output. Use this before testing a specific screen so the F11 results only reflect that screen's navigation.

## Typical Workflow

1. **Navigate to the screen** you want to diagnose.
2. **Press F9.** This produces both the tree dump and classification report across all layers.
3. **Read f10_classify.txt first.** This tells you what the blind player would experience. Count the navigation stops and compare against what's visible on screen.
4. **If stops are missing content,** read f9_tree.txt to find the missing elements and understand their type, depth, layer, and parent structure.
5. **Press F10** to check cursor position if debugging navigation order.
6. **Press F8** to clear the unclassified tracker, navigate through screens, then **press F11** to catch unclassified element types.

## Comparing Dumps to Screenshots

When diagnosing a screen, combine a screenshot with the F9 dump files:

- **The screenshot** shows spatial layout and visual grouping — which elements are in the same region, what the intended reading order might be, and what a sighted player would consider "one thing."
- **f10_classify.txt** shows what the blind player actually hears — the announcements, the navigation order, and what's missing.
- **f9_tree.txt** shows whether missing data exists in the tree, which layer it's on, and how to reach it.

The screenshot answers "what should the experience be?" The dumps answer "what is the experience?" The gap between them is the work to be done.

## Code Location

Front end (file dumps): `FM26Access.Plugin/DebugTools.cs`
Shared walkers: `FM26Access.Plugin/Diagnostics/TreeDump.cs` (raw tree) and `FM26Access.Plugin/Diagnostics/ClassificationDump.cs` (classification report) — also backing the dev driver's `/gui`, `/panels` and `/classify`.
Keybindings: `FM26Access.Plugin/Input/Handlers/DefaultScreenHandler.cs` (the F8–F12 cases in `HandleKeyDown`).
