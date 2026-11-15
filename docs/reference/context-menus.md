> Upstream FM26 Access reference, release 0.2.16. Check [FM27 migration notes](../fm27-migration.md) before using game-specific APIs.

# Right-Click Context Menus

How FM26's right-click context menus work, how the mod reads them, and the universal recipe for
opening one programmatically. Empirically verified live (2026-06-29) by capturing real right-clicks
with `InputProbe` and watching the companion surface the result.

## The two-part architecture: manipulator vs. contributor

A context menu is assembled on the fly by two cooperating pieces. Keep them separate in your head —
conflating them is what sends opening attempts down dead ends.

- **`ContextMenuDescriptionManipulator`** — the **trigger and orchestrator**. A UI Toolkit
  `Manipulator` attached to an element. It listens for the gestures that should open a menu, and when
  one fires it does the mechanical work: builds an empty `MenuContext` (with `Target` = the element),
  dispatches a `MenuDescriptionPopulateEvent`, then hands the filled result to
  `ContextMenuSubsystem.OpenContextMenu`. It knows *when* and *how* to open — but **nothing about what
  goes in the menu**.
- **Contributors (`IMenuContributor`)** — the **content**. As the populate event propagates, every
  contributor in the path gets `BindToMenu(ref context, parent)` and adds its entries.
  `PluginContextMenuContributor` is the big one: it reads an entity's `ActionGroupings` and adds the
  real items (Squad, Transfer, Contract, Report, …). In the element tree these contributors appear as
  **`PluginContextMenuBindings`** nodes (the invisible per-element ones are named
  `RightClickMenuBinding`).

So: **manipulator fires an empty shell → contributors fill it → subsystem shows it.** A manipulator
with no contributor in its populate path opens an *empty* menu, which surfaces as nothing.

## The trigger is a `button=1` pointer, not an InputAction

Captured ancestry of a real right-click on a squad player name:

```
PointerDown button=1 pos=(324.6, 835.6)  target: SIText'NameText'
PointerUp   button=1 pos=(324.6, 835.6)  target: SIText'NameText' < VisualElement
            < PluginContextMenuBindings < SIButton'NameButton' < …
```

A real right-click runs `OnPointerUp → PopulateEvent → OpenContextMenu` on the leaf's manipulator
(Harmony-traced — see "The opener" below). The earlier "down-only might be the whole fix" theory is
**disproven**: a synthetic `PointerDown` alone does not open it. But the gesture was never the real
lever — the production opener uses the pointer-free `OpenOnShortcutEvent`, and what actually gated the
calendar was the menu's *content* (the hover-link id), not the trigger. See "The opener" below.

The visible **"Actions ▾" menu button** is the same manipulator family with the **left** button
(`button=0`); it is already handled by `ContextMenuButtonAdapter` via `SyntheticPointer.LeftClick`. That
left-click path working is direct proof that a synthetic pointer opens these menus fine **when it carries
the element's real screen position** — a default-position event is rejected. (A *dispatched* synthetic
pointer re-picks its target by screen position, landing on whatever is under that point — e.g. the ribbon
over a scrolled row — which is why synthetic right-click was unreliable on tables and the opener moved off
it onto `OpenOnShortcutEvent`.)

## Detection: a non-null `ContextMenuDescriptionManipulator` on the leaf (THE universal signal)

> **Corrected 2026-06-29.** The earlier rule below ("`PluginContextMenuBindings` in the ancestry") was
> **wrong** — it missed the calendar entirely. The verified universal marker is the manipulator.

> **Corrected again 2026-07-02.** The manipulator is the universal *trigger* signal, but it is NOT a
> content signal on `SIText`: the game arms it on **every** hyperlink-styled text, including links whose
> entity has no actions — a fixture score's match link carries an armed manipulator, yet its populate
> resolves zero contributors and no menu ever shows (verified live on the Fixture Schedule). The game's
> own per-link content test is **`SIText.LinkHasActions(linkID)`** (private; the IL2CPP proxy exposes
> it — link ids are the `EmbeddedData` record indices, `<link="0">` is record 0). `ContextMenuOpener`
> therefore gates SIText leaves with `LinkHasActions` before treating them as menu-bearing
> (`IsMenuBearing`); non-SIText holders (`PluginContextMenuBindings`, `FMCalendarEvent`,
> `FMPitchIndicator`) carry their own content path and stay ungated. Two more live findings from the
> same session:
>
> - **`MenuTarget` prefers an actions-bearing SIText leaf over a `PluginContextMenuBindings` node**
>   earlier in document order. On the fixture table's opposition cells the bindings node binds a
>   context that never fills, while the team leaf (hover-link path) opens the real team menu.

> **Extended 2026-07-24.** Detection (does a leaf have a menu) is necessary but not sufficient: a
> resolved holder must also *belong to* the control the player is on, or the mark lies. See
> [Scope](#scope-a-mark-only-where-the-menu-matches-what-the-control-is-named-after-2026-07-24) below —
> `ContextMenuScope` gates every mark, and `MenuTarget` is now `ContextMenuOpener.Resolve(...).Target`.
> - **Entries fill asynchronously well past the popup surfacing** — often several hundred ms, and the
>   open confirm must require `ContextMenuSurface.HasEntries` (not mere menu presence), else it
>   announces an unusable empty shell as "opened". On timeout the opener closes the half-open context
>   (`ContextMenuSurface.Close`) so the player is never stranded on an entryless modal. Some entities
>   pass `LinkHasActions` yet never fill at all (a non-league friendly opponent, observed live) — those
>   surface as the honest "Context menu did not open."

A menu-bearing sub-element is an **`SIText` leaf whose `m_contextMenuDescriptionManipulator` field
(type `SI.UI.ContextMenuDescriptionManipulator`) is non-null.** Detect by reflecting the element's
IL2CPP type for a field of that type with a non-null value (cache the `FieldInfo` per type name; it can
sit on a base type). Verified live, and it is *precise* — SET only where a menu exists:

| Surface | leaf | `m_contextMenuDescriptionManipulator` | menu |
|---|---|:-:|---|
| Squad | `SIText'NameText'` | **SET** | player actions |
| Calendar event | `SIText'Name'` (competition) | **SET** | competition |
| Calendar event | `SIText'TeamOpponent'` | **SET** | opponent team |
| Calendar event | `SIText'Time'` / `'Venue'` / `'leaguePosition'` | null | none |
| any plain label | `SIText'text'` | null | none |

All menu leaves are also `<link="0">` rich-text hyperlinks (they carry a left-click navigation *and* a
right-click menu).

### Three attachment shapes — why the old `PluginContextMenuBindings` rule failed

The trigger (`ContextMenuDescriptionManipulator`) and the content (`IMenuContributor`) attach in two
different shapes; the old rule only knew the first:

1. **Standalone `PluginContextMenuBindings` element** (squad, next-match, clubsite). It holds a
   manipulator and uses a separate `PluginContextMenuContributor`. The leaf under it *also* carries a
   manipulator, so the leaf marker still catches it.
2. **A component that bakes the manipulator + contributor into itself** — `FMCalendarEvent : DynamicItem,
   IMenuContributor` (calendar tiles), `FMPitchIndicator`. **No `PluginContextMenuBindings` node exists.**
   The old doc's "calendar = yes (async)" was wrong; **nothing is async** — the calendar simply uses this
   second shape, which the ancestry scan can't see.

3. **A manipulator the element never stores** (2026-08-11) — `SI.Bindable.StreamedTable.AddContextMenu`
   (`StreamedTable.cs:1436`) builds a fresh manipulator per **column header** from a local closure
   (`<>c__DisplayClass262_0`, `:601`) and hands it to `AddManipulator`. UI Toolkit keeps no
   back-reference on the element, and `StreamedTable` stores none either. So the header carries
   **no field to reflect and no instance to drive** — detection AND opening both fail, silently.
   `Adapters/ManipulatorRegistry.cs` is the missing back-reference: `Patches/ContextMenuManipulatorPatches.cs`
   records `target → manipulator` in the manipulator's own `RegisterCallbacksOnTarget` /
   `UnregisterCallbacksFromTarget`, and `ContextMenuOpener.Manipulator`/`HasMenuManipulator` fall back
   to it after the field lookup. Only **non-field-backed** manipulators are recorded — the four field
   types are already reachable, and registering them would pin every hyperlink text in the game.

`ContextMenuDescriptionManipulator` **field** holders (the trigger, shapes 1–2): `PluginContextMenuBindings`,
`FMCalendarEvent`, `FMPitchIndicator`, `SIText` — verified by grep across the whole decompile, these four
and no others. Shape 3 has no field holder at all and is reached only through the registry.
`IMenuContributor` impls (the content): `PluginContextMenuContributor`, `FMCalendarEvent`,
`FMPitchIndicator`. (`PluginContextMenuBindings` is **not** itself a contributor — so
"contributor in ancestry" would miss the squad; the manipulator-on-leaf marker is the one that's
universal.)

**Dev gotcha:** manipulators bind at element-creation time, so a `/reload` resets the registry and
already-built tables stay unregistered — their headers report "No context menu here." until the table
is rebuilt (navigate away and back). A game restart is unaffected, and a player never reloads.

### Per-sub-element targeting is real, not optional

A composite tile has **several** menu leaves, each with its own menu — a calendar event yields a
competition menu (`Name`) *and* an opponent-team menu (`TeamOpponent`); time/venue have none. Enumerate
every `SIText` leaf with a non-null manipulator → each is its own target. The companion must expose each
as its own `aria-haspopup` control (the in-game per-leaf right-click reaches them spatially).

## Scope: a mark only where the menu matches what the control is named after (2026-07-24)

`LinkHasActions` proves a leaf *has* a menu, but not that the menu belongs to the control the player is
on. The old resolver accepted **any** menu-bearing element in a stop's subtree, so a control inherited a
buried, unrelated entity's menu and the label and the menu described different things:

| Control (what the player hears) | Menu that actually opened |
|---|---|
| Alt+5 "Staff" tile | the **manager's** own menu (a `RightClickMenuBinding` deep in the tile) |
| Squad "Medical Center" tile | the first **injured player's** menu |
| Message subject "Fitness tests" | the **sender's** (Mohamed Wright) menu |

The rule, in one line: **a control advertises a menu only when the menu belongs to the entity the
control is named after.** It lives in `FM26Access.Navigation/ContextMenuScope.cs` — pure and game-free
(five plain inputs), so every shape is unit-tested without a running game (`ContextMenuScopeTests`):

- **Text holder (`SIText`)** — judged by NAME: accept when its cleaned text matches the control's cleaned
  label (either-contains-other, so "Dan Abraham" matches a decorated cell "12, Dan Abraham, 7.4";
  case/punctuation-insensitive; diacritics kept — both sides are the same game text). A holder naming a
  *different* entity is rejected (`RejectedLabelMismatch`, or `RejectedComposite` when several rival
  holders exist and none matches).
- **Textless holder** (`PluginContextMenuBindings`, `FMCalendarEvent`, `FMPitchIndicator`) — no text to
  compare, so judged by DEPTH: accept within `MaxWrapperDepth = 3` (a control's own binding sits a level
  or two inside it), reject deeper (a summary tile's buried sub-block binding).
- **Self / unstreamed / punctuation-only** — the control itself is menu-bearing → always accept; a text
  holder with no readable text yet falls back to the depth rule rather than being rejected for a value
  that has not streamed in.

`MaxWrapperDepth = 3` is calibrated from a live census (~2,400 resolutions), **not guessed**: every
legitimate textless binding resolves at depth 1–2 (a name cell's binding, a nation flag), and the
nearest *mislabeled* one is at depth 22 — nothing exists between. Name-matched text holders are kept
regardless of depth, so a deep-but-correct cell (the squad "Ottawa" club link at depth 6) survives.

### The single mark funnel + enforcement

Every surface's mark — HTML plain stops, table cells, the speech cursor — funnels through
`PluginAdapterBase.HasContextMenu`. That one property is the gate:

```
var r = ContextMenuOpener.Resolve(Element);          // candidate holder + evidence (kind, depth, text, rival count)
if (Census.Recording) Census.Record(this, r);        // dev census observes the RAW decision
if (r.Target == null) return false;
return ContextMenuScope.Accepts(
    ContextMenuOpener.Verdict(r, ContextMenuOpener.OwnerLabel(this)));   // scoped against the control's own name
```

`ContextMenuOpener.Resolve` returns a `MenuResolution` (target + `Kind`/`HolderType`/`HolderText`/`Depth`/
`HolderCount`); `Verdict` and `OwnerLabel` are the shared label-aware helpers the census reuses, so the
mark and the census can never disagree.

### Promotion: don't drop a real menu, move it to the right control

Enforcement drops the *mislabeled* marks, but some of those covered a genuine entity the player still
wants (a message's sender). The fix is to surface that entity as its **own** marked control rather than
leave the menu on the wrong one. The Messages table does this: the **Sender** column renders a person
sender as a button (`onclick="FM.contextmenu.open(id)"` + `aria-haspopup`) backed by a
`ServerHandles.MenuLeafHandle` over the `PersonSenderName` leaf; a news-outlet sender (no actions) stays
plain text. So "Fitness tests" is just a subject, and "Mohamed Wright" is the control whose menu opens.

Summary **tiles** (Medical Center, Staff) are deliberately *not* promoted — they are navigation shortcuts
over an indeterminate entity, and promoting a buried player's menu onto them would be noise. Dropping the
mislabel is the whole fix there.

### Titling the open menu

The modal menu view is titled by the entity whose menu it is — `"Actions for Mohamed Wright"`, not a bare
`"Actions"` — so the player hears whose menu opened. The entity is the resolution's `HolderText`, captured
into `ContextMenuSurface.OpenedOwnerLabel` when the open is dispatched (`ServerHandles`) and cleared on
`Close`; a textless-holder menu (a nation flag) falls back to plain "Actions".

### Tooling: `/ctxcensus` and the audit lead

- **`/ctxcensus`** walks the active screen like `/body` (streamed tables materialized) with the census
  recorder armed, and prints every mark with its evidence, a verdict tally, and a depth histogram per
  holder kind. `scripts/ctxcensus-sweep.ps1` aggregates it across the ribbon + named destinations — the
  before/after diff that makes a scoping change reviewable game-wide instead of screen by screen.
  **Caveat:** the streamed-table phase hard-crashed the game (native, uncatchable) on the Club screen —
  collect fragile screens individually rather than via the full sweep.
- **`/ctxtrigger?id=`** now prints per-holder depth (`dN`) and a `resolved=<kind> <type> depth= holders=`
  header — which candidate the resolver actually picked.
- **`/audit`** carries a `menu-suppressed` LEAD: a control whose armed menu the scope rule suppresses,
  listed with the holder it would have opened (`"Medical Center -> menu of Alessandro Grant"`). Correct to
  drop today; the standing alarm is for a NEW mislabel shape a future game update introduces.

## Reading the open menu (already works)

`Server/ContextMenuSurface.cs` reads an **open** menu's data model and feeds it to the companion:

- The open menu is a floating element — `PanelManager-container` → `floating-element-root` →
  `CascadingMenu` — parented as a **sibling** of the PanelManager panels.
- `TryGetMenu()` finds it; the menu's entire entry tree (folders, items, each leaf's
  `HandleInteraction` callback) is read from `CascadingMenu.Description.m_root` and rendered as
  companion buttons. `WalkEntries` handles drill-in/Back/Close.
- **Entry flags** (`MenuDescription.EntryFlags`): `Visible=1`, `Enabled=2`, `Selected=4`,
  `ShowSpacerBefore=8`. `Selected` is the game's tick and renders as
  `role="menuitemcheckbox" aria-checked="true"`. An *unticked* entry stays a plain `menuitem`: the flag
  only ever says checked, so an unticked checkbox is indistinguishable from a plain command and
  `aria-checked="false"` would invent a state the game never expressed. (Real example: the player menu's
  Misc → "Keep History After Retirement".) `ShowSpacerBefore` is deliberately not surfaced.
- **An empty folder is the game's, not a failure.** The game leaves a folder in the menu even when
  every child has been filtered out — the Player Database's `Insert Column > Attributes > Mental` is
  empty because all fourteen mental attributes are already columns, and the game's own description has
  `Mental kind=2 f=3 kids=0`. Confirm with **`/ctxstate?tree=N`**, which dumps the description as a
  nested tree with each entry's kind, flags and child count; `/ctxstate` alone only reports root
  children and cannot answer this. Such a level renders an inert "No options" entry, and an
  unreadable description renders the `Degrade.Read` marker — the player must be able to tell "nothing
  here" from "the mod could not read this", and a bare Back + Close reads as neither.
- **Disabled entries use `aria-disabled`, never the `disabled` attribute.** A disabled `<button>`
  cannot take focus, so the arrow controller's `focusItem` silently no-opped and the player got STUCK:
  every further Down re-targeted the same unfocusable entry. Any menu with a greyed entry hit this —
  a column header menu on a built-in view greys Rename/Delete/Export.
- **Titling.** The view is titled from the holder's own text. A **textless** holder — a column header's
  `TemplateContainer`, a nation flag — has none, so `ServerHandles.Handle.MenuOwnerLabel()` names it
  instead (`HeaderHandle` answers with the column name, giving "Actions for Pas %"). Unimplemented on
  other handles, which keep the bare "Actions" fallback.

**Inspection gotcha:** `/panels` (DumpAllPanels) enumerates *registered panels per layer* and does
**not** traverse the `floating-element-root` sibling, so an open menu is invisible there. To see the
live menu, use **`/rendered`** (the companion DOM) — never conclude "no menu" from `/panels`.

## The opener — how it works (SOLVED 2026-06-30, ISIL-verified)

`ContextMenuOpener.Send` opens a menu in two steps: **ensure the target leaf's hover-link is valid, then
drive the manipulator's pointer-free entry** `OpenOnShortcutEvent(NavigationSubmitEvent.GetPooled())`. No
synthetic pointer, no OS input, no fabricated events.

### Why the hover-link matters (the real gate)

A link-leaf menu's *content* is the **target entity's** actions, gathered like this (read from the ISIL of
`ContextMenuSubsystem.OpenContextMenu`, `PluginContextMenuContributor`, and `SIText`):

1. `OpenContextMenu` gathers contributors from the populate context **plus the subsystem's global
   contributors**, calls `BindToMenu` on each, and shows the menu only if at least one contributor survives
   (else it clears the context and returns — nothing visible). The show is
   `CascadingMenuPopup.ShowOrWaitForEntries` — "show OR wait" — so the items fill **asynchronously** via
   binding callbacks; empty-at-open is normal, poll ~1.5 s.
2. For a link-leaf, nothing in the propagation path adds a contributor: `SIText.OnContextMenuPopulated` only
   **restyles** the hyperlink (it reads field `0x60C`, the *captured* link id, and never adds a contributor),
   and `FMCalendarEvent` is **off-tree** (not in the leaf's ancestry). So the content comes solely from the
   global `PluginContextMenuContributor`.
3. That contributor's `BindToMenu` calls `GetContextMenuObjects(context.Target)` →
   `SIText.GetEmbeddedDataContextValues(...)`, which reads field **`0x608` (the hover-link id)** to pick which
   link's entity to expose. **If `0x608` is `-1`, it returns no entity → the contributor removes itself →
   zero contributors → the menu doesn't show.**
4. `0x608` is set by **focus**: `SIText.SetHoverLinkOnTableRowFocusedStateChanged(bool)` writes it to the
   first link index when focused, `-1` when not (`HasValidHoverLinkID()` = `0x608 >= 0`).

So a focusable **table row** (a squad player name) works automatically — navigating onto it focuses it and
sets the hover-link. A non-row link leaf (a **calendar event tile**, the **next-opponent** label) is never
focused that way, so its hover-link stays `-1` and the menu opens empty.

### The fix

Before `OpenOnShortcutEvent`, `Send` sets the hover-link the game's own way when the target is an `SIText`:

```csharp
if (target.GetIl2CppType().Name == "SIText")
    target.Cast<SI.Bindable.SIText>().SetHoverLinkOnTableRowFocusedStateChanged(true);
```

It's the game's public method (sets `0x608` to the first link when the leaf has links, else clears it), so
re-calling it on an already-focused squad row is benign, and it is **skipped** for squad player names (whose
`MenuTarget` is a `PluginContextMenuBindings` node, not an `SIText`) — they keep opening through their
ancestor contributor unchanged. Verified live: calendar opponent + competition menus (correct per-leaf
entity, multiple events per day) and the squad next-opponent menu all open; squad player menus unaffected.

### `elementAsTarget` is a red herring (don't chase it)

`PopulateEvent` reads no focus/input/pointer state; `elementAsTarget` only selects whether `MenuContext.Target`
is the passed element or `evt.target` — it does **not** gate contributor gathering. The opener path is fully
input-free; the only state that matters is the hover-link id above.

## Menus die when their anchor row scrolls — and the game can crash on it (2026-07-02)

An open menu's context lives on its anchor row's link leaf. **Scrolling the virtualised table
recycles that row and the game silently drops the menu context** — no `CloseContextMenu`, the popup
left visible as an empty shell (verified live: menu bound on the top squad row, one forced scroll to
the bottom row, context unbound). The practical trigger was the mod itself: closing a menu re-renders
the page, whose streamed-table loaders sweep-scroll the whole list for seconds, so the next menu
opened during the sweep was gutted ("menu opens but its options never load").

Worse, the game has **no error handling for items that arrive after the teardown**: the late fill
null-refs (in `PluginContextMenuContributor.RegisterItem`, or in the entry-added show path through
`CascadingMenuPopup.ShowInternal` → `CascadingMenu.SetCurrentFolder`), the exception escapes through
the binding-notify chain into the game plugin's native data transaction, and the game hard-crashes or
wedges its main loop. Reproduced under synthetic rapid cycling and hit twice in one real session.

Mod-side handling:

- **`Server/ContextMenuGuard.cs`** — the streamed-table loaders abort their sweep while a menu is
  open or an open is in flight (the page they read is behind the modal anyway). The in-flight flag is
  set at BIND time, not during the realize/lookup phase — guarding realize livelocked, because an
  open waiting for its handle id aborted the very page render that would re-register it.
- **Dead-shell self-heal** — a visible menu whose description is gone (`ContextMenuSurface
  .IsDeadShell`) is dismissed by the next body compose instead of rendering as a trapping empty
  "Actions" modal.
- **`Patches/CascadingMenuGuardPatches.cs`** — Harmony finalizers on `OnItemsReceived`,
  `ShowInternal`, and `SetCurrentFolder` log (`Degrade.Note`) and swallow the late-fill exception,
  so the worst case is a menu that never appears (the open confirm then reports the honest "Context
  menu did not open") instead of a dead game. All three fired/needed in live sessions.
- **Fill-before-show** — `Send` only binds; `ServerHandles.OpenContextMenu` polls the bound (hidden)
  menu via `ContextMenuOpener.PendingMenu()` and surfaces it (`EnsureShown`) only once
  `ContextMenuSurface.HasEntries` is true, so a menu can never appear empty. Confirm budget 3s;
  timeout closes the half-open context.
- **`/ctxstate`** — dev endpoint reporting context-bound state, popup show state, and the pending and
  in-tree entry lists; the tool that cracked this whole investigation.

### How we got ground truth

The stub decompile in `decompiled/` is body-stripped (Cpp2IL `dummydll`), so every "why" was inference — and
an earlier model (an "active-element input gate" on field `0x60C` requiring real OS input) was a misread that
sent the investigation toward synthetic input for months. Re-running Cpp2IL with `--output-as isil` (see
`docs/guides/decompilation-guide.md`) emits the real instructions; reading `OpenContextMenu`,
`PluginContextMenuContributor.BindToMenu`/`GetContextMenuObjects`, and `SIText.GetEmbeddedDataContextValues`
settled it. **Lesson: read the ISIL before building on inferred game behaviour.**
