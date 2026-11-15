# FM26 accessible component reference

Baseline: FM26 Access 0.2.16, commit
`4d38a0256f29b97376845ef59edfcd64d7f165ed`, retained in `.upstream/fm26`.
Original authorship, GPL and dependency notices remain in the source. This
catalog identifies implementation contracts, not FM27 functional qualification.
FM27 native observations and test boundaries belong in the migration notes,
Testing and implementation matrix.

Paths below are relative to that baseline. Read the named implementation before
changing its FM27 counterpart. Prefer exact reuse, an adapter, then an extension
of the established pattern. A new component requires a demonstrated native gap.

| Required components | Original implementation | Accessible and integration contract |
| --- | --- | --- |
| Main navigation and submenus | `FM26Access.Plugin/Server/NavigatorIndex.cs`, `NavigatorSurface.cs`; `app.js` `FM.navigator`/`FM.nav` | Native destinations feed the searchable tree. Enter opens the actual destination; tree-specific arrows do not capture ordinary page arrows. Back/Forward use native history. |
| Messages, message rows and news | `ScreenWalker.MessagesTableItem`, `HtmlRenderer.RenderMessagesTable`, `MessageViewSurface.cs`; `app.js` `FM.messages`/`FM.msgview` | The original component is a semantic table: Status, Subject or Preview, Sender, Sent. Subject opens the message; sender actions belong to the sender. Retain read state, native identity, attachments and actions. Escape closes the message and returns to its row. |
| Filters and search | `FilterPopupSurface.cs`, `SearchDropdownSession.cs`; `app.js` `FM.search` and the message modules | Native categories/options and search bindings supply results. Preserve query, selection and focus; a searchable dropdown is distinct from a simple dropdown. |
| General tables, interactive rows and pagination | `StreamedTableLoader.cs`, `TablePages.cs`; `FM26Access.Navigation/StreamedTableReader.cs`, `TablePaging.cs`, `TablePagerHtml.cs`; `app.js` `FM.tablepager` | Ordinary HTML tables retain headers, column order, cells and actual row controls. General pages contain50 rows, harvested across native frames. Native virtualization is not an HTML page. |
| Dialogs, warnings and confirmations | `HtmlRenderer.RenderSection`/`RenderNode`, `ServerText.cs`, `ServerHandles.cs`; applicable `app.js` overlay modules | Retain native title, content, controls and legitimate disabled states. Modal content suppresses background interaction; opening/closing and origin focus belong to the shared surface, not a duplicate per-screen dialog. FM27 differences are documented separately. |
| Comboboxes and menus | `DropdownSession.cs`, `SearchDropdownSession.cs`, `ContextMenuSurface.cs`; `app.js` `FM.dropdown`, `FM.search`, `FM.contextmenu` | Native options, submenu ownership, selection and cancel drive the original controls. Arrow/Enter/Escape handling is local. Native menu actions are not value edits. |
| Action buttons and controls in cells | `HtmlRenderer.RenderNode`, `ServerHandles.StopHandle`, button adapters | `act(id)` dispatches through the registered native adapter. A click acknowledgement is not result evidence. Informational text and indicators must not become disabled buttons. |
| Informational, navigable and customizable cards | `ScreenWalker` render items; `HtmlRenderer.RenderCarouselTile`, `RenderNode`, `RenderLabelValue`; feature renderers | Preserve actual grouping, native actions and label/value relations. A customization selector uses real native options, not a fixed invented list. Native FM27 tile swapping requires an adapter; it is not an upstream qualification claim. |
| Gauges and indicators | Gauge/progress adapters and `HtmlRenderer.RenderNode` role rendering | Keep the actual name, value, range and readonly state. An indicator is not an action unless its native control really opens details. Synchronize live values without announcing the whole page. |
| Additional information | `ServerText.Label`/description resolution; `app.js` `FM.describe` | Alt+I queries the focused handle through `/describe`, then `/speak` and the speech pipeline. It reads that item's current description; it does not inject a long tooltip into the DOM or every button name. |
| Player and club profiles | `ScreenWalker` specialized readers; `HtmlRenderer.RenderAttributeTable`, `RenderPlayerBanner`, `RenderLabelValue` and table renderers | Retain original data order, header/value association, profile links and contextual actions. A profile is composed from these existing readers, not a second generic card framework. |
| Position selectors and lineup | `HtmlRenderer.RenderPositionGrid`/`RenderPitchGrid`; `FM26Access.Navigation/PitchFormation.cs`, `PitchNavigation.cs`; `app.js` `FM.pitch`/`FM.player` | Formation geometry and component-specific arrows remain local. Occupied positions announce the actual player; selection and substitutions dispatch to the game. Retain native occupants/record identity rather than a display index. |
| Tactical selectors and instructions | `Server/Features/TacticSlots.cs`, `TacticsPhaseSelect.cs`, `PlayerInstructions.cs`, `TeamInstructions.cs`; `app.js` `FM.ti` | Native tactic/phase/role options and instruction state feed the established selectors and tables. Preserve apply/cancel and context; new FM27 dimensions require actual native data. |
| Set pieces and taker priorities | `Server/Features/SetPieceInstructions.cs`; `app.js` `FM.takers`/`FM.spmove` | Reuse native assignments, list order, chip tables and movement controls. Do not infer ordering success from HTML movement alone. |
| Tabs and actual radio choices | `HtmlRenderer.RenderTabBar`/`RenderRadioGroup`; `FM26Access.Navigation/TabBarHtml.cs` | Navigation is a tablist of native buttons with `aria-selected`; actual form choices remain radios. The original tab renderer supplies a labelled region and activates through the normal handle path. |
| Reports and charts | Specialized feature readers and `HtmlRenderer` line/value/stat-comparison/synthetic-table renderers | Preserve native plotted/table values and units, meaningful labels and real detail actions. Provide the established textual/table representation; a generic “Chart” name is insufficient. |
| Calendar and schedules | `HtmlRenderer.RenderCalendar`/`RenderScheduleWeek`; `FM26Access.Navigation/CalendarScheduleHtml.cs`, `CalendarGrid.cs`; `app.js` `FM.date` | Preserve native day/week order, localized headings, schedules and owner context. FM27 dated and undated records need distinct identity adapters, not invented dates. |

The runtime audit currently checks namespace-only exact reuse for the central
streamed reader, table pages/paging/pager, tab HTML and speech pipeline. Its
report records the running module and installed hashes. Other files are adapted;
do not infer source equality or live coverage from a similar filename.

Current FM27 departures, including rebuilt native binding roots, copied native
history values, dropdown type changes, record recycling and form notification
headers, are in [migration notes](fm27-migration.md). The applicable verification
is recorded in [Testing](TESTING.md) and the
[coverage matrix](FM27_IMPLEMENTATION_MATRIX.md).

## Selected-fixture event adaptation

The upstream MatchReportTiles.TryReadMatchEvents builds Min, Team, Event and Player from actual two-sided native slots. Current FM27 KeyEvents splits those slots into two streamed lists and retains live scorer links. FM27FormTable therefore reuses the same headers/group order with the existing native live-cell table, preserving each original player action rather than flattening links into the readonly report Grid. Native score-header ownership, full realization/counts and the actual Event schema gate this adapter. Exact sources, seven-fixture evidence and limits are tracked in matrix F159 and Testing.

## Required rows versus optional footer content

Upstream `FM26Access.Plugin/Contexts/TableContext.cs`, `CountPendingCells` and
`CountReadiness`, deliberately keep a virtualized body row pending when it has
zero observed native bindings. FM27 preserves that contract. Its optional footer
extraction must distinguish an empty cell from a missing row shell; the same
native binding observation now feeds separate readiness policies. No new HTML
table or pager replaces the original fifty-row component. Actual 53-file native
paging, selection and focus evidence is tracked in F174.
