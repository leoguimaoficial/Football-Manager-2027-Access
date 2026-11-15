> Upstream FM26 Access reference, release 0.2.16. Check [FM27 migration notes](../fm27-migration.md) before using game-specific APIs.

# FM26 UI Elements and Containers Catalog

A comprehensive catalog of every UI element and container type in Football Manager 2026, based on the decompiled source code in `SI.Bindable`, `FM.UI`, and Unity UI Toolkit foundations.

**Purpose:** Serves as a reference for the accessibility mod — identifies what each type does, whether it needs speech handling, and whether the mod currently classifies it.

> **Reconciled against `ElementClassifier.KnownTypes` (FM26Access.Navigation) on 2026-06-24.** The
> mod now maps **30** known type names (Rule 2), not the 22 this catalog was first written against.
> Classified since the first draft (so the "would crash on discovery" / "currently skipped" notes
> for these below are out of date): **FMCombobox** → Combobox, **SIMenu** → Dropdown,
> **StreamedTable** → Table, **SIMultiBox** → Button, **DateSelectorWidget** → DateSelector,
> **ExpandedToggle** / **Toggle** → Toggle, **FMStarRatingTemp** → StarRating,
> **DiggableVisualElement** → Diggable, **SearchBox** → SearchBox, **SILongField** → LayoutWrapper.
> Treat `ElementClassifier.cs` as the source of truth; this catalog is a guide, not the source.

---

## How the Mod Classifies Elements

The mod's `ElementClassifier` uses a 9-rule cascade:

1. **Internal child** of a widget that owns children → `InternalChild` (skipped)
2. **Known type name** → direct mapping (30 entries)
3. **TextElement subclass** with text content → `Text`
4. **Known infrastructure type** → `LayoutWrapper` (skipped)
5. **Unknown IBindableElement** → `Unknown` (crashes the mod — forces us to add explicit handling)
6. **INotifyValueChanged\<bool\>** → `RadioButton`
7. **IUISection** → `Section`
8. **USS class fallback** → lookup by CSS class name
9. **Default** → `LayoutWrapper` (skipped)

---

## Interactive Widgets

These are elements a player can activate, toggle, or change. All require speech handling.

### Buttons

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **SIButton** | Button | IBindableElement, ITextElement, INavigatable, IShortcutElement | `Button` | Standard clickable button. Supports text binding, click/right-click events, keyboard shortcuts, hover bindings. |
| **Button** | (Unity built-in) | — | `Button` | Rare plain Unity button, used in some framework contexts. |
| **SIStepButton\<TValue, TRange\>** | SIButton | (inherits SIButton interfaces) | `Button` (via SIButton inheritance) | Abstract generic step button for increment/decrement. Not instantiated directly. |
| **SIStepIntButton** | SIStepButton\<int, IntRange\> | (inherits) | `Button` | Integer step button (e.g. +/- controls). |
| **SIStepFloatButton** | SIStepButton\<float, FloatRange\> | (inherits) | `Button` | Float step button. |
| **ButtonGroup** | VisualElement | — | `ButtonBar` | Container that groups multiple buttons into a bar. Children are individual buttons. |

### Checkboxes

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **SICheckBox** | Toggle | IBindableElement, INavigatable, IShortcutElement | `Checkbox` | Standard checkbox. Fires checked/unchecked events, supports selection binding. |
| **SICheckBoxGroup** | VisualElement | IBindableElement, INavigatable | In infrastructure list (skipped) | Group of checkboxes with parent/child state tracking (Unchecked/Checked/Partial). Collapsible. **Currently skipped — may need handling.** |

### Sliders

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **SISlider** | Slider | IBindableElement, INavigatable, IShortcutElement | `Slider` | Slider with min/max, step size, step visualization, inversion modes. |

### Dropdowns, Menus & Popup Selection Controls

FM26 has a family of dropdown-like controls beyond just `SIDropdown`. There are 6 distinct controls a player could encounter, plus internal plumbing types.

#### Primary Dropdown Controls

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **SIDropdown** | NavigatableVisualElement | IBindableElement, IShortcutElement | `Dropdown` | The primary dropdown widget. Choice binding, selection events, search, popup control (click/hover), row templates. |
| **FMCombobox** | VisualElement | IBindableElement, IBindingInfo, IShortcutElement | `Combobox` | Alternative dropdown with increment/decrement step buttons. Found in FM.UI.Widgets. Classified as `Combobox` (stepper-style); rendered with decrease/increase controls on the HTML surface. |

#### Menu Controls (Dropdown-like)

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **SIMenu** | VisualElement | IBindableElement, IStreamedListElement | `Dropdown` | Menu container with row templates, render modes (static/floating). Classified as `Dropdown`. |
| **SIRadialMenu** | VisualElement | IBindableElement, IShortcutElement | Not classified (falls to rule 5 → crash) | Radial menu with item templates, configurable radius. **Not yet encountered or would crash on discovery.** |

#### Popup Selection Controls

| Type Name | Namespace | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **SIFloatingPopupWidget** | SI.Bindable | IShortcutElement | In infrastructure list (skipped) | Floating popup with content templates, anchor styles, return-to-focus management. **Currently skipped — may need speech for popup content.** |
| **ListPopup** | SI.UI | — | Not in classifier | Popup list for selection. Lives outside SI.Bindable so not encountered by the element tree walker. |
| **CascadingMenuPopup** | SI.UI | — | Not in classifier | Cascading/nested menu popup for hierarchical selection. Lives outside SI.Bindable. |

#### Dropdown Internal Infrastructure

These are plumbing types inside dropdown controls — not navigated independently.

| Type Name | Parent Class | Purpose |
|---|---|---|
| **SIDropdownElement\<T\>** | VisualElement | Individual row item inside a dropdown popup list. |
| **StreamedDropdownList** | StreamedObjectList | Virtualized list powering dropdown content. |
| **StreamedMenuList** | StreamedObjectList | Virtualized list powering menu content. |
| **DropdownRowNavigatable** | TableRowNavigatable | Focus management for dropdown rows. |

#### Picker Controls (Selection Analogues)

These aren't dropdowns but provide similar "choose from options" functionality.

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **ColourPicker** | VisualElement | IBindableElement | Not classified (falls to rule 5 → crash) | Color selection control. **Not yet encountered or would crash on discovery.** |
| **GridSelector** | GroupBox | IBindableElement | In infrastructure list (skipped) | Grid-based selection control. |
| **DateSelectorWidget** | NavigatableVisualElement | IBindableElement | `DateSelector` | Date picker (see Date Selection section below). |

### Radio Buttons

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **SIRadioButton** | VisualElement | INotifyValueChanged\<bool\> | `RadioButton` | Single radio button with value change notification. |
| **SIRadioGroup** | GroupBox | IBindableElement, IView | `RadioGroup` | Container of radio buttons. Supports selection/index/items binding, horizontal and 2-column layouts. |

### Toggles

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **ExpandedToggle** | VisualElement | — | `Toggle` | Expandable toggle element (expand/collapse sections). |

### Text Input Fields

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **BindableTextEditBox** | TextInputBaseField\<string\> | IBindableElement | `TextField` | Free-text input with placeholder text binding. |
| **BindableIntEditBox** | IntegerField | IBindableElement | `NumberField` | Integer input field. |
| **BindableFloatEditBox** | FloatField | IBindableElement | `NumberField` | Float input field. |

### Date Selection

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **DateSelectorWidget** | NavigatableVisualElement | IBindableElement | `DateSelector` | Date picker with confirm/clear/cancel events. Uses a main component template and item template. |

---

## Display Widgets

These show information but are not directly interactive. Most need speech for their content.

### Text

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **SIText** | TextElement | IBindableElement, IStyleOverridden, ITextElement | `Text` | Rich text with case transforms, embedded data styling, embedded links, elision tooltips. Primary text display widget. |

### Images

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **SIImage** | SI.UI.Image | IBindableElement, IStyleOverridden | `Image` | Image display from asset reference, bytes, or style. Supports recoloring and icon styling. |
| **SIRenderTextureImage** | VisualElement | — | In infrastructure list (skipped) | Renders a texture or render texture (e.g. 3D player model previews). Not useful for speech. |

### Progress & Gauges

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **SIProgressBar** | VisualElement | IBindableElement | `ProgressBar` | Standard progress bar with percentage binding. Supports dotted-line mode. |
| **SIDualProgressBar** | VisualElement | — | Not classified (no IBindableElement → falls to default `LayoutWrapper`) | Shows current vs. previous values with positive/negative color coding. **Currently skipped — should likely be `ProgressBar`.** |
| **SIRadialGauge** | VisualElement | IBindableElement | `Gauge` | Circular gauge with segments, min/max/current/previous values, arc direction. |
| **SISegmentedGraph** | VisualElement | IBindableElement | Not classified (falls to rule 5 → crash) | Segmented bar graph with data binding and color styling. **Not yet encountered or would crash.** |

### Steppers (Display)

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **SIStepper** | VisualElement | IBindableElement | `Stepper` | Step progression indicator with fill modes (UpToSelected, SelectedOnly, etc.). Display-only despite similar name to step buttons. |

### Specialized Displays

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **SIPositionalAbilitiesMap** | VisualElement | IBindableElement | Not classified (falls to rule 5 → crash) | Positional abilities visualization (likely a pitch/field diagram). **Not yet encountered or would crash.** |

---

## Containers & Layout Elements

These organize child elements. Most are infrastructure that the mod skips, but some have semantic meaning worth announcing.

### Sections & Tabs

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **BindableSection** | UISection | IBindableElement | `Section` | Base section container with binding lifecycle. Semantic grouping of related content. |
| **UISection** | VisualElement | IUISection | `Section` | Framework section base. Caught by Rule 7 (IUISection interface). |
| **TabGroup** | VisualElement | — | `TabGroup` | Container of tabs for switching between views. |
| **SITabView** | VisualElement | IBindableElement | In infrastructure list (skipped) | Tab switcher that loads UXML templates based on events. **Currently skipped — may overlap with TabGroup handling.** |

### Virtualized Lists

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **StreamedListView** | DiggableVisualElement | IView, IScrollable, IBindableElement | In infrastructure list (skipped) | Abstract base for all virtualized lists. Handles sorting, pagination, truncation, row selection, scroll targeting. |
| **StreamedObjectList** | StreamedListView | — | In infrastructure list (skipped) | Concrete list view with row templates, binding, USS class assignment. The workhorse list container. |
| **StreamedObjectListWithChildren\<T\>** | StreamedObjectList | — | In infrastructure list (skipped) | Hierarchical list with parent-child relationships and child rendering modes. |
| **StreamedObjectList_PlaceTimelineEvents** | StreamedObjectList | — | In infrastructure list (skipped) | Specialized list for timeline event display. |

### Tables

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **StreamedTable** | StreamedListView | IBindingInfo | `Table` | Table with columns, headers, sorting, context menus, column resizing. Classified as `Table`; walked into rows/cells and rendered as a `<table>` (headers + streamed rows) on the HTML surface. |
| **TableRowNavigatable** | VisualElement | — | In infrastructure list (skipped) | Focus management wrapper for table rows. |

### Dropdown Lists (Internal)

See the "Dropdown Internal Infrastructure" table in the Interactive Widgets section for StreamedDropdownList, StreamedMenuList, and DropdownRowNavigatable.

### Grid Layouts

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **BindableGridLayoutElement** | BindableSection | IBindableVisibleMany, IBindableElement | In infrastructure list (skipped) | Abstract grid layout with data-driven pages. Base for the tiles-and-cards pattern. |
| **ScalableBindableGridLayoutElement** | BindableGridLayoutElement | — | In infrastructure list (skipped) | Grid layout that scales with responsive breakpoints. |
| **TabbedGridLayoutElement** | BindableGridLayoutElement | — | In infrastructure list (skipped) | Grid layout with tab-based page switching. |
| **MultiGridLayoutElementVisible** | VisualElement | — | In infrastructure list (skipped) | Visibility controller for multi-grid layouts. |
| **GridElement** | VisualElement | — | In infrastructure list (skipped) | Individual tile slot in a grid layout. |
| **GridLayoutElement** | VisualElement | — | In infrastructure list (skipped) | Layout calculation for grid items. |
| **VariableSizedGridElement** | VisualElement | — | In infrastructure list (skipped) | Grid element with variable sizing. |
| **GridSelector** | VisualElement | — | In infrastructure list (skipped) | Grid-based selection indicator. |

### Carousel

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **SICarousel** | StreamedObjectList | — | In infrastructure list (skipped) | Auto-rotating carousel with progress bars, transition timing, keyboard/gamepad navigation. **Currently skipped — likely needs handling for carousel item announcements.** |
| **BindableCarouselTileRoot** | VisualElement | — | In infrastructure list (skipped) | Tile root container for carousel items. |

### Scroll Containers

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **ScrollView** | (Unity built-in) | — | In infrastructure list (skipped) | Standard Unity scroll container. |
| **SIScrollView** | ScrollView | — | In infrastructure list (skipped) | SI-enhanced scroll view. |
| **SIAutoScrollView** | ScrollView | — | In infrastructure list (skipped) | Auto-scrolling scroll view. |
| **Scroller** | (Unity built-in) | — | In infrastructure list (skipped) | Scroll bar control. |
| **AutoScroller** | VisualElement | — | In infrastructure list (skipped) | Auto-scroll controller. |
| **ScrollerSlider** | VisualElement | — | In infrastructure list (skipped) | Slider inside a scroller. |

### Navigation Infrastructure

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **NavigatableVisualElement** | VisualElement | INavigatable | In infrastructure list (skipped) | Base class for elements that participate in gamepad/keyboard navigation. |
| **NavigationFocusser** | VisualElement | — | In infrastructure list (skipped) | Manages focus assignment for navigation. |
| **DiggableVisualElement** | VisualElement | — | In infrastructure list (skipped) | Base for elements that can be "dug into" (expanded navigation). |

### Selectors & Indicators

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **SISelector** | Image | IBindableElement | In infrastructure list (skipped) | Visual selection indicator positioned at/before/after a list index. |
| **SIMaskSelector** | VisualElement | IBindableElement | In infrastructure list (skipped) | Bitmask-based element visibility selector. |

### Panes & Resizing

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **ResizablePanes** | VisualElement | — | In infrastructure list (skipped) | Resizable split-pane layout. |

### Cards & Templates

| Type Name | Parent Class | Interfaces | Mod Classification | Notes |
|---|---|---|---|---|
| **BaseCardTemplateImpl** | VisualElement | — | In infrastructure list (skipped) | Base implementation for card templates (the "card" in tiles-and-cards). |

---

## Where section titles live at runtime (tiles-and-cards)

The tables above catalog types. This section is a runtime-structure note: on a large screen, the
visual "sections" a sighted player sees are the **tiles** of the tiles-and-cards layout, and the
section heading text is found by element **name**, not by type. The grouping types in the tables
(`BindableSection`, `UISection`, `GridElement`) are the data/layout scaffolding; the visible title
is a plain `SIText` sitting in a conventionally-named slot inside each tile.

Verified live on the **Preferences & Accessibility** screen (2026-06-24). One tile looks like:

```
BindingRemapper name="RegionPreferences"          ← section container, named for its content
  TemplateContainer name="tile-wide-4x2"          ← tile size template (tile-*)
    VisualElement name="layout-block-base-container"
      VisualElement name="title area frame"        ← the header slot
        … AutoScroller …
          SIText name="title-text" text="Region"   ← the SECTION TITLE (the heading text)
      TemplateContainer name="states-templates-tile-content"   ← the fields/content
        … SIText name="text" text="Language" + SIDropdown …    ← field labels are name="text"
```

Practical rules for anything that wants to surface section headings (companion HTML `<h3>`, speech
landmarks):

- **The reliable signal is the element name `title-text`** (an `SIText` inside a `title area frame`).
  The classifier already surfaces it as a `Text` stop with the right announcement. You cannot key off
  the IL2CPP type — it is just `SIText`, the same type as every other label.
- **Field labels inside a tile are `SIText name="text"`** (and occasionally `name="ttile"` — a real
  typo in the game's UXML, seen on the Sounds toggles). These are one level below the section title
  and should stay plain text, not headings.
- **The convention is not universal.** Some tiles hide their `title area frame`, leave `title-text`
  empty, and instead place the visible heading as a loose `SIText name="text"` in the content (the
  Sounds/Graphics tile on Preferences does this). A detector keyed only on `name=="title-text"` heads
  the well-behaved tiles and silently misses these. The failure mode is a *missing* heading, never a
  wrong one: empty `title-text` elements are not navigation stops, so they never reach a heading check
  and never produce an empty heading. A complete solution needs a fallback that finds the tile's
  leading text when `title-text` is empty.
- **To verify on any screen:** `GET /gui` or `GET /classify` and search for `title-text` and
  `title area frame`. If the section titles show up as named `title-text` stops, the simple rule
  covers them; if a section's heading is a loose `name="text"`, it needs the fallback.

---

## Binding & Data Infrastructure

These handle data flow and are never directly visible to the player. All are correctly in the infrastructure list.

| Type Name | Purpose |
|---|---|
| **BindingRoot** | Root container for data binding context. |
| **BindingRemapper** | Transforms binding paths between contexts. |
| **BindingVariables** | Stores binding variable values. |
| **BindingExpect** | Asserts expected binding state. |
| **BindingSetter** | Sets binding values programmatically. |
| **DataReferenceElement** | Wrapper for data-bound element references. |
| **DebugBindingElement** | Debug visualization for binding state. |
| **AsyncBindableVisualElement** | Async binding container. |
| **AsyncBindingRoot** | Async binding context root. |
| **BindableElement** | Base bindable element. |
| **BindableUxmlEntry** | Entry for UXML template binding. |
| **BindableUxmlList** | List of UXML template entries. |
| **DynamicItem** | Dynamically created bound item. |

## Styling & Visual Infrastructure

These control appearance only. Correctly in the infrastructure list.

| Type Name | Purpose |
|---|---|
| **SIStyleSetter** | Applies USS classes, styles, colors, and sizing. |
| **SIVisible** | Controls visibility of child elements. |
| **SIEnabled** | Controls enabled/disabled state. |
| **SIGradient** | Applies gradient colors. |
| **SIAnimation** | Animation controller. |
| **SIScrollAnimation** | Scroll-triggered animation. |
| **SIAspectRatioFitter** | Maintains aspect ratio for children. |
| **SITaggedElement** | Element with tag identifier for masking/selection. |
| **SITile** | Tile display with background image and filter color. |

## Other Infrastructure

| Type Name | Purpose |
|---|---|
| **TemplateContainer** | UXML template instantiation container. |
| **RepeatButton** | Button that fires repeatedly while held. |
| **EventTrigger** | Event handler element. |
| **BindableSwitchElement** | Conditionally shows children by index or bitmask. |
| **SwitchElement** | Non-bindable switch element. |
| **BindableDragDropObject** | Drag-and-drop handler. |
| **DragHandler** | Drag operation handler. |
| **VirtualisedList** | Low-level virtualization engine (used by StreamedListView). |

---

## Potential Coverage Gaps

These types are in the infrastructure list (skipped) or unclassified but may need proper handling for accessibility:

| Type Name | Why It Might Need Handling |
|---|---|
| **SIRadialMenu** | Interactive menu needing full navigation support. |
| **SIFloatingPopupWidget** | Popups appearing over content need announcement. |
| **ListPopup** | Popup selection list (SI.UI) — may appear outside normal element tree. |
| **CascadingMenuPopup** | Nested menu popup (SI.UI) — may appear outside normal element tree. |
| **SICarousel** | Players need to know what's in the carousel and navigate between items. |
| **SICheckBoxGroup** | Group state (partial selection) is meaningful. |
| **SITabView** | Tab switching needs announcement (may be handled via TabGroup already). |
| **SIDualProgressBar** | Shows meaningful comparison data that SIProgressBar handler won't catch. |
| **SISegmentedGraph** | Data visualization that needs speech equivalent. |
| **SIPositionalAbilitiesMap** | Spatial data that needs a speech-friendly representation. |
| **ColourPicker** | Color selection control needing speech. |

---

## Summary Counts

| Category | Count |
|---|---|
| Interactive widgets (mod-classified) | 30 type names in KnownTypes |
| Dropdown-like controls (total family) | 6 distinct controls (SIDropdown, FMCombobox, SIMenu, SIRadialMenu, SIFloatingPopupWidget, ListPopup/CascadingMenuPopup) + 4 internal plumbing types + 3 picker analogues |
| Interactive widgets (unhandled, potentially need classification) | 6 (SIRadialMenu, SIFloatingPopupWidget, SICarousel, SICheckBoxGroup, ColourPicker, ListPopup/CascadingMenuPopup) |
| Display widgets (mod-classified) | 6 (SIText, SIImage, SIProgressBar, SIRadialGauge, SIStepper + TextElement subclasses) |
| Display widgets (unhandled) | 3 (SIDualProgressBar, SISegmentedGraph, SIPositionalAbilitiesMap) |
| Container/layout types | 31+ |
| Binding/data infrastructure | 13 |
| Styling/visual infrastructure | 9 |
| Other infrastructure | 8 |
| **Total unique types cataloged** | **~90** |
