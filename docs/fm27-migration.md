## FM26 0.2.17 Many-selection adaptation — local trial

Upstream commits7775d90,90b1ec6 and5ff0048 add stale-checkbox-body protection,
index-native Many-mode toggles, a cheaper classification predicate order and
page-only bulk selection. Reuse the classification and selection patterns;
keep FM27's existing actual record identities and command-owner/availability
checks. The new checkbox guard also respects incoming native owner identity and
the existing newer-value timestamps, including the hard-replacement path.

Actual Steam PlayerSearchReport/playertable has SelectionMode.Many, an
ExpandedToggle inside StreamedTable.SelectionCell and VirtualisedList, and a
separate FM.UI.PersonReference binding. Items are integers0,1,2...; they cannot
certify the player's identity. The FM27 handle therefore still realizes and
validates that exact reference before the native index toggle. Serialize this
background operation with the existing streamed-reader gate: another selector
or body harvest otherwise scrolls the record away mid-validation. No native
calls leave the Unity dispatcher and no game-state cache or identity bypass is
introduced. AtLeastOne pickers retain their original pointer/confirmation path.

An initial rapid-three-checkbox trial refused one row; the shared gate corrects
that demonstrated race. Subsequent native counts confirm3/0 through selection/
inverse, including an overlapping body read. Page-only selection confirms50
without retaining the previous page's50; original Select all confirms10,000.
Final Release repeats the relevant actions without developer tools. Scope and
timings are in the session/testing records; no complete-game qualification.

## Arrival titles omit internal fallback before native binding — F205

Native setup titles can bind after PanelOpened. Preserve GetTitle for document/
bookkeeping semantics and reuse its extraction through GetAnnouncementTitle
without PanelID.name/Screen fallback for open, return and reload announcements.
The existing nullable PanelEntry speech scope makes that early arrival silent;
do not defer a page/control read. Real titles, explicit reads and action feedback
remain. Actual AddNationsModal cycles and custom-database editor/cancel pass with
unchanged setup values; the separate database-file chooser is not newly reopened.

## Late ordinary button commands participate in shape settlement — F204

FM27's shared native confirmation buttons can bind their callbacks after visible
captions/children. CanSubmit=false without Events/custom action is initially
readonly, then becomes a command without a visual-tree change. Observe ordinary
SIButton command capability through the existing bounded shape/settle contract,
as already done for inbox actions. Preserve native readonly/disabled semantics,
ordinary FM26 button HTML, exact handles and streamed-row identity. Do not force
enabled, add translated-caption exceptions or change settle budgets. Six actual
open/cancel cycles across return-to-start and quit confirmations recover all three
buttons automatically without F5; normal Companion UIA confirms button semantics.

## Untitled match ribbons retain headerless presentation — F203

FM27 uses ContinuePanel at the interval as its native MenuTitle command ribbon.
Reuse FM26's existing OverviewHeader heading-level-zero contract for that actual
owner too. Preserve all native ribbon controls and report/score headings; the
synthetic Navigation caption is not a native content title. The actual half-time
DOM comparison retains all 20 controls at45:00. Full-time recurrence is not newly
live-qualified. Match audio assets/calibration remain inherited; successful
asset decoding and offline signal rendering do not certify device audibility.

## Match preferences reuse native form choices — F202

FM27's HighlightModes options add fixed radial artwork beside the real radio
captions. The unchanged FM26 radio-extra walk would expose that glyph as another
numeric field. Hide only that demonstrated component through the existing profile;
keep all native choices, commands, selected/disabled state and unrelated meters.
Adapt actual section-name/path/first-child anchors and use the recovered official
checkbox labels. The original fieldset/radio/checkbox/slider/dropdown HTML remains.

FM27 can also style a segmented option through an active SIStyleSetter class
while the button's own background stays transparent. The original derived radio
adapter must reuse the ordinary button active-style resolver as well as its FM26
solid-fill fallback; otherwise the native fullscreen choice appears unchecked.
Keep existing native actions, checked-value revisions and radio-group ownership.

## Covering overlays retain identity without a main landmark — F201

FM27's overlay metadata is required for foreground focus/return and background
suppression, but its added main tag is not. Reuse a neutral div and FM26's actual
title-only section presentation. Real answer dialogs keep role=dialog/aria-modal;
existing data attributes, native headings/actions and all client handling stay.
The original match-stat comparison reader/HTML work during the match too; native
Ver estatísticas reaches key and advanced AFS/FC Porto tables while paused.

## Native inline table identity is not a taker schema — F200

TeamTalkPanel's PreMatchTable rows are DynamicReference records with actual
MatchPlayer/Team fields, mutable reaction/selection data and a legitimate key
absent from PropertyIdentifierSet. Running every inline row through the taker
schema calls GetInfo on that key and prevents all rows loading. Only the actual
set-piece taker component uses the specialized type/side/player schema. Other
inline row identities retain their direct native InteropReference fields with
their numeric context keys, without interpreting every field name. Keep actual
reference revalidation, ordinary FM26 table/SSE/selection contracts and native
availability; no cache, guessed property name or enabled override is required.

## Pre-match readonly title chrome — F199

TeamSelectionTitleBar's SIText/MainTabText repeats the actual content h2 on the
demonstrated FullScreenTeamSelection. Reuse the existing scoped profile hide
pattern for that structural readonly caption; retain the native content heading,
navigation/actions and actual text. No global string deduplication is needed.

## Pre-match substitutes template ownership — F198

The actual FullScreenTeamSelection also instantiates the same anonymous
TacticalPlannerSubsBlock as TacticalPlannerTool. Extend the existing structurally
scoped omission to this demonstrated owner; preserve its native table controls
and the original FullScreenTeamSelection profile. Native table slot dropdowns
are separate from duplicate strip pickers and remain part of this screen's FM26
contract. No global template suppression or caption matching is introduced.

## Pre-match header FM menu naming — F197

The same native FMMenuDropdown template also lives under TeamSelectionTitleBar.
Without the ordinary-header rename, its empty self-caption lets the inherited
field-label resolver borrow Ir para o Jogo from the adjacent button. Reuse the
existing OverviewTitleBar SIDropdown/FMMenuDropdown profile rename in this actual
owner. Keep all native dropdown options, open/cancel/focus and the distinct match
button. No generalized field-label change or caption matching is required.

## Restore FM26 substitutes-strip parity — F195

Reuse the two exact original TacticalPlannerTool path hide rules for the strip
and NoPlayer controls. Preserve actual table dropdowns and intrinsic XIButton
commands. An empty Name field's unavailable candidate list is insufficient
evidence of missing table reserve selection: occupied rows offer actual players
and native player-picker navigation. The user's demonstrated path supersedes
F194's rationale for a second strip; the remaining native adapters are retained.
FM27's anonymous TacticalPlannerSubsBlock instance cannot match FM26's named
ancestor rules. Adapt that actual template source to InternalChild only within
TacticalPlannerTool, reusing both walkers' existing subtree omission. Native
table selection remains separate. No caption or player-name matching is used.

## Native tactics variants and simultaneous pitch ownership — F194

Reuse FM26 PlayerInstructionsButtonAdapter across the demonstrated native raster
density family. Preserve actual selection-model Index, native event and contextual
failure. Current planner XI/substitute pickers and split role buttons are real
commands distinct from Name/role dropdowns. A no-command visual SIButton can wrap
the actual linked SIText: walk the real link, preserving narrative/menu guards.
Group members and specialized adapters retain their intrinsic native commands.

Both's two editable pitches require separately scoped DOM IDs, menu/move state,
full player reference/tactic slot/possession ownership and same-pitch completion.
Use actual phase-caption Pointers and unique native branch ancestry to place
informative headings before their own field controls. Keep FM26 occupied-slot
geometry, roving keys and24-slot move interaction. Unscoped multiple-pitch moves
refuse ambiguity. Panoramic retains the original placeholder by user instruction.

Modern TeamInstructions roots differ from FM26. Reuse its semantic table only
after every current native list has complete count/unique-index coverage; retain
fallback paging. Resolve actual InstructionID/catalog IDs and full tactic+slot
context. Compound entries keep ordinary native menu/editor open actions rather
than parsing translated option order into fabricated fields. Preserve native
availability and stale-owner guards. Native gauges/footer without commands are
readonly; demonstrated captioned role/position/binary foot artwork keeps its own
official text. Details and cohort boundaries: [tactics audit](FM27_TACTICS_AUDIT.md).

## Native overlay close is not menu-history traversal — F193

FM27's Back delegate closes the actual covering foreground card before considering
the background title bar. GlobalSearchCard dismissal therefore need not change
the menu-history index. Extend the existing HistoryPlan/press deadline with a
separate completion outcome: capture the exact interactive overlay Pointer before
the single native Back dispatch and observe that owner's removal from Open and
Closing. Retain the actual native close policy, generation cancellation and real
history/message landing timeout errors; never silence every failed Back or replay
a close. Client focus/dialog handling stays unchanged. Five search closes and
ordinary page-history Back are qualified separately in F193 session progress.

## Native overview frame headers and fixture content — F192

Reuse ordinary FM26 ButtonAdapter/StopItem/TextAdapter headings: FM27's shared
title-link-button has an actual primary-text header that can provide a separate
readonly live h3 while its original command remains an ordinary button. Preserve
full ordinary tile labels and Alt+I; the user explicitly retained Dynamics there.

NavigableLink caption suppression must ask whether its actual native image
command remains after presentation policy. F186's valid same-record image
consolidation exposed an older independent caption-demotion rule: both could be
removed. LinkHasCommandButton now counts genuinely bound retained commands only.
The real named SIText link, menu, native reference and stale guards remain.

The full calendar also has readonly CalendarEventBindingExpect fixture bodies
without SIButtons. Walk their actual content in-place rather than dropping an
event after an empty button collection. Team/competition badges are redundant
only in the demonstrated sibling schema with matching Event/caption references.
Nonempty event-list shells with zero bindings remain provisional through the
existing bounded marker path, while genuinely empty days remain empty. No new
calendar framework, simulated action, forced native state or retry budget.
See F192 session progress for six main-overview captures and evidence boundaries.

## Youth Setup actions: preserve native availability — F191

The FM26 ordinary table contract already retains visible cell controls; FM27's
existing NativeButtonCommand names EventFmxActionDevelopmentCentreAdvice when
its icon-only SIButton has no native caption. Actual men's FC Porto Sub-19/B
captures show four such buttons in their own action cells. Overview's10 native
playeradvice records instead mark IsSuggestionActionable=false and hide their
actionbuttonvisible wrappers. Do not replace this availability with an invented
Promote action. Actual player context menus already expose Plantel → FC Porto
and the native youth destinations. F191 required no production adaptation.
Business effects and unseen states are outside this capture/menu-only evidence.

## Message prose versus useful entity rows — F190

The user wants FM26 readonly message prose/title/metadata and explicitly retains
player/staff links in Important People and useful native rows. Scope this by the
actual reading-pane ownership and native table/list/NavigableLink boundaries.
Reuse TextAdapter and the original paragraph/body-focus path; omit extracted
subject-entity actions there. Keep native person/table actions and their record,
context and menu guards. No caption lists, global link removal or unrelated
conversation/profile changes. See FM26_MESSAGE_PARITY.md for exact baseline
sources, clarified scope and current targeted-versus-full-regression boundaries.

## Native foreground-opening focus barrier — F189

FM27 can put the incoming tutorial card in PanelManager.Open with IsLoading=true
before it is visible/interactive, while the outgoing instance remains in Closing.
Selecting only currently interactive panels exposed the blocked Messages parent
to an optimistic /body read and restored its h2 focus. Observe current native
covering Card/ModalDialog loading entries and defer composition across Unity
dispatches within the existing10s bound. A late opening receives ordinary loading
content. Before finishing streamed rows, reject/recompose an older body when an
actual exclusive native foreground now has different panel Pointers. Dedicated
loading screens and non-covering HUDs keep their original behavior. Real closure
still returns to the original control. No caption/screen exceptions, native state
cache, action replay or client focus redesign. See session progress for actual
step/close/ordinary-dialog coverage and explicit runtime/client boundaries.

## Native introduction content grouping — F188

Squad, responsibilities and tactics introductions share native purpose_block,
topic_block and squad_header/squad_block sections. Their flat walk lost heading
and row membership. Extend the original live StopItem heading renderer and
TextListItem with ordinary rendered row content; keep each topic separate and
each person link with its own native description. The responsibilities variant
wraps purpose in one SIVisible. Native topic and person lists are independent;
never zip them by ordinal or infer player roles. Complete native list counts are
required for consolidation; unexpected/partial lists retain original paging.
See [message contracts](FM26_MESSAGE_PARITY.md) for source and live evidence limits.

## FM26 empty message-list contract — F187

The native men's Inbox reproduced Por Ler(0) with an empty four-column table.
FM26's TryBuildMessagesTable emits no table for zero rows. FM27 now applies that
rule after RefreshMessagesTable at render time, keeping the provisional marker
and late native binding recovery. See [the complete message contract review](FM26_MESSAGE_PARITY.md).
Final ef524091 passes7groups/11messages/3axe checks, plus the existing six-check
inbox/focus suite and three further clean axe audits. Six original message source
contracts remain exact; actual native extraction/identity/transport guards stay.

## Shared entity presentation audit — F186

The men's FC Porto career is the native evidence source. FM26's semantic controls,
context menus, informative glyphs and ordinary table/link contracts remain the
baseline. Equal captions alone are never a reason to remove an action.

The shared native adapter now suppresses a pure crest/flag/portrait action only
when its actual bound record matches a unique enabled named link in the same
bounded native component. Club/Team aliases require the canonical ownership
relationship. Conditional flag commands resolve their selected opening argument;
a nation and its national team can be different destinations. Copied remapper
values require bounded named-node resolution, not an unrestricted provider walk.

Readonly single-name copies beside an already named image action use the actual
inline-fragment semantics, preserving headings, help, menus and mixed paragraphs.
Known formatted native captions retain their original TranslationID/TranslationArg
composition. Native ClubButton variants in clause rows must name their OWN club,
not borrow the adjacent player name: both independent actions stay available.
A generic competition badge is decorative only in its demonstrated captioned
schema. Actual category, compliance and other informative glyphs remain readable.

The first targeted follow-up caught a second native shape: the competition button
acquires its own SIText after initial realization. The image-only test had left a
readonly copy beside that newly captioned button. The shared rule now also checks
the button's own unique bound record/caption and never folds its own child text.
The twelve-check final entity regression passes after this correction.
The complete146-destination sweep on dcac7fd0 left one further real duplicate:
the compact club tile's Portugal flag and named link both opened NationSite with
NationReference167772322. Its frame is not an entity ComponentElement. The shared
flag adapter now accepts the unique matching named link in that bounded local
frame, stopping at its binding scope. No screen/name allowlist is used. Final
fb2b5c60 passes13entity checks/six clean axe audits, including the compact tile's
retained menu/destination/exact focus and Ghana's two DISTINCT destinations.
During development, an inspection request crashed the game. The local dump's
managed stack identifies EntityLinkProbe.Describe → BindingBag.Request.get_Value
→ Il2CppObjectPool.Get → il2cpp_object_get_class. The probe had inspected a Direct
binding through its VisualFunction state. Both diagnostic branches now require
the actual BindingMethodKind before reading their native payloads. Actual Direct
caption probes were repeated successfully after a receipt-aware cold reinstall.
Production already excludes this entire developer probe; the failure remains
recorded, rather than attributed to user input or an unmodified native game.

Detailed current coverage, final runtime and conditional limits are recorded in
[the presentation audit](FM27_ENTITY_PRESENTATION_AUDIT.md). No manual NVDA,
all-conditional-state or production-stability claim follows from this audit.

## Native title announcements and inbox filters — F182

The reported `Close, button` and `BGImage, image` are present in the mod's own
SpeechPipeline history on Debug64fe, including while Unity Application.isFocused
is false. The companion's reported HTML correctly presents the Preferences and
What's New screens; this is not evidence of Unity UI Automation or an NVDA
configuration defect. The inherited FM26 PanelTracker/NavigationSession announces
the first native cursor stop on arrival; FM27's deferred opened observation and
late rescan also emit that automatic read outside the companion's reading flow.

The user's final policy preserves short destination titles, including Alt+number,
and excludes automatic first-control/image reads and whole-page speech. The initial
complete background mute in Debug0b130c44 is superseded. Scoped PanelEntry resolves
the shared actual ServerText title at automatic entry/return; late rescans/cursor
restoration stay quiet. Cursor bookkeeping, explicit navigation, Alt+I's original
describe/speak path, errors and commentary remain. Background mod-owned automatic
form tooltips are canceled. No Windows/NVDA setting or native control is hidden.

The Messages All/New/Tasks/Unread group reuses AppendButton and actual tab handles,
native disabled/selected values, exclusive aria-pressed and the existing Unread
return marker. Other native tabs retain unchanged FM26 TabBarHtml. Final Debug94cf
passes all four native filter selections and all six Alt-number command paths:
short title speech without control suffixes and actual sole-companion destination
commits. Earlier main-menu dialog observations locate the reported speech source;
their complete-silence result is not final-policy qualification. No manual NVDA
or complete-career claim follows. Developer reporting remains absent in Release.

## Inbox latency, one-shot focus and paired club links — F183–F185

The actual message command spent hundreds of milliseconds traversing unrelated
native descendants before its one-millisecond activation. Reuse Unity's typed
UQuery inside NativeTreeQuery, materialized only for the current dispatch, for
message search fields, history tab/fixture discovery and stepper signature text.
Keep native order, ancestor visibility, uniqueness, option values and all owner/
record checks. No cached game state, readiness budget reduction, lost table rows,
command replay or transport workaround is introduced. Readonly native comparison
matches the previous full walks, including an open search, a real tabbed calendar
and its fixture list. A changed General tab and its exact focus survive native Back.

On an actual linked-club history return, historyFocus remained armed after the
correct control was focused; a later neutral update refocused that old link after
blur. Consume only a successfully landed exact handoff, including a body commit
that already focused it. Retain pending missing/hidden targets and existing input,
owner and message guards. Four coordination cases and actual Benfica/Sporting
returns plus neutral updates pass; this is DOM focus evidence, not manual NVDA.

PairedEntityLink applies the existing shared hide policy to a demonstrated native
image-button/named-link component. Require its sole EmbeddedDataClicked action,
actual ClubReference and unique enabled full-text link in the immediate component.
For a TeamReference, require that exact bound Team and ClubForObjectLookupData;
never infer identity from equal captions. Keep the named link, its context menu,
all other fields, headings, breadcrumbs and biography links. All18transfer rows
retain one named club action; Benfica and Sporting profiles and exact returns pass.
Final Debug94cf message133 clicks reach body focus in719–1142ms and complete18rows
in1063–1745ms. A native nested menu return and neutral update pass too. This is
not an absolute FM26 performance benchmark. Three axe audits
are clean. Phase probes are developer-only and compiled out of production.

## Native wizard phase/value contract — F181

FM27 0.9.2435's selected IP branch uses an unnamed SIText inside BindingRemapper;
its empty branch and the OOP branch retain selected-tactic-name. The original
FoldTacticWizardPreview missed the selected IP pair and exposed separate value /
phase paragraphs. Accept only that unnamed SIText under the same remapper, direct
caption and TacticInstructionsIP/OOP block, through the existing LabelValueItem
and LiveValues renderer. Do not generalize to arbitrary unnamed widgets or pair
text from another block. No new HTML component, grid or caption allowlist exists.
Final 64fe native regression passes both phase changes/inverses, stable IDs and
Cancel on the men's career. The static Texture artwork has no Position/player
record: keep the inherited formation-name representation, without inferred roles.
Full keyboard/all-formation coverage remains separate.

F115's native menu/editor mismatch also reproduces for Trofim Melnichenko and
survives same-process native save/load. Raw native form PropertyValue 64 and its
visible unavailable SIText disagree with the fresh available menu. Do not treat
this as a stale HTML-only value or silently substitute the menu's value into the
form. Current callback/schema inspection found no demonstrated supported editor
refresh lifecycle that corrects this; no provider reset or business replay is added.

## Current action-tile and heading contract — F179

Ordinary flat action tiles use the original ButtonAdapter and unchanged FM26
DisplayText composition. A generic visible tile title is insufficient reason to
expand every descendant into a section. Actual rich tables/value groups/media
retain their existing readers. Independently bound child commands remain; a
nested SIButton with neither Events nor m_clickableAction is only a visual call
to action. Main-menu native evidence demonstrates this for the country/Draft
inner buttons; no caption or per-screen deduplication is used.

Headings are informational. Rich-card open actions follow their contents as
ordinary buttons; native links in rich titles follow the complete readonly title
as ordinary real controls. Preserve all original label, value, state, context
menu, native record and identity validation. Section arrival ids have a separate
readonly, screen-scoped description registry; they are not game-action aliases.
Its native sources are validated and cleared with the existing handle lifecycle.

Native Xbox process paths can use WindowsApps aliases rather than the configured
XboxGames root. Derive companion ownership from the actual running game's
ExecutablePath and watch-pid; never overlap a second UI by comparing only the
configured alias. Native direct routes plus actual reported DOM are separate
from frontend keyboard/focus evidence.

F181 remains an investigated native difference: FMTacticInstructionsWidget has
TacticData/PitchEntity artwork, not FMPitchIndicator position/person records.
The visible selected IP 433 widget has11 Texture entities; hidden sibling widgets
may be unloaded. No position names or player identities may be derived from these
coordinates. Actual native style/IP/OOP selection and confirmed active-player
PitchGrid work independently; this does not certify the pre-commit artwork.

Older runtime snapshots below are historical.

## Current source: F176, F177 and F178 native corrections

Debug Plugin 1912a71e-8cf3-4d38-bf2c-480f8372f541 / game PID 29712 is the current
qualification runtime. The sole shared native regression is running; consult
FM27_SESSION_PROGRESS.md and exact report identities before inheriting results.
F176 final guard passed all 26 actual roster selector values, six corrected names
and eleven recovered positions on preceding 226c3a88 (3 groups / 1 clean audit).
F177 restores the actual World dropdown origin after native entity navigation
and Back. F178 reuses the existing bound SiteHeaderDynamicItem title contract
for native Site panels, rejecting nested/foreign panel ancestors. Six native
World entity families pass 12 clean audits on current 1912a71e, with exact Back
focus. Portugal's 10,006 content characters / 361 controls are otherwise identical.
Evidence: career-world-site-title-passed.json,
career-world-portugal-title-comparison.json,
career-squad-position-label-final-selectors-passed.json and
career-squad-position-label-final-availability-passed.json.

Current pure checks pass 1,628 cases, zero failures/skips, plus loader/profile/
player fixtures; JavaScript passes 79 cases. F047 pre-HTTP Windows 10060 cause,
F115 native loan-status disagreement and broader career/production qualification
remain open. Candidate 090515 predates these changes and is superseded. No stable,
whole-career, Steam or manual NVDA assertion. Older entries retain historical scope.

## Current F175 native result

Cold Debugfe50e602-762b-4ad6-a4be-92e3adce44a0/game29712 passes3functional groups/1clean WCAG audit on the exact protected September original. All four native labels/enable flags match HTML; disabled row2 stays readable. Explicit disabled expand/select requests refuse with native selection/root unchanged and no native command dispatch log; both permitted folders open/cancel with exact parent/root focus. Evidence career-native-option-fixed-passed.json. Full1,628 pure cases plus loader/profile/player checks and74JS cases pass0skips. Broader fresh124-destination ordinary-menu coverage is running; source qualification is incomplete. Historical evidence below retains its own module and date scope.
## Ordinary native dropdown availability — F175, corrected source awaiting live checks

The career menu sweep reached30destinations/234completed controls before finding a genuine mapping defect in Arsenal U21 Set All. Native row2 is disabled (both enable flags false, opacity0.50), but ordinary HTML offered it enabled. Raw native tree/state and screenshot evidence are retained in career-native-option-u21-fresh-* and career-native-option-u21-menu-layout.png. This is legitimate game unavailability, not a reason to enable or remove the option.

The shared FM26 select/options contract now accepts native disabled state. Availability is harvested with the same absolute row index as its label across virtualized windows and continuations. The backend reads the realized native state again immediately before expand/select; offscreen preparation scrolls once and observes bounded binding. Current Debugfe50e602-762b-4ad6-a4be-92e3adce44a0/game29712 was cold installed with receiptmod488f42863fe640a6ab9c0a234fbfe45f after a native quit and observed exit.1,628 pure cases plus loader/profile/player fixtures pass0skips; build0errors/230warnings. Live corrected-state/focus/refusal qualification is pending. Broader dropdown/career coverage, F04710060 cause and F115 loan mismatch remain open. No stable/Steam/manual-NVDA claim.
## Optional native table footers and structural receipts — F174

The native load browser with53real saves exposed a permanent provisional footer and continuous production recomposition. FM26 TableContext.CountPendingCells deliberately treats a zero-binding virtualized BODY row as pending. That rule does not apply to an optional empty footer. NativeBindingReadiness now shares native binding observation while retaining separate PendingRowCells and OptionalContentPending policies. Real late footer bindings remain pending; native contents and50-row paging are unchanged.

SSE changed-shape handling may adopt a matching fresh post-harvest structural baseline without certifying completeness. Forced recovery still uses CanSuppress, which rejects provisional receipts. Existing marker retry count/delays are retained, with no automatic rearming. Release78c cold native53-file regression passes50+3pagination, completeness/quiescence, selected-file preservation, keyboard focus and cancellation (3 groups/4clean semantic audits).1620pure tests/loader/player/API fixtures pass0skips. Shared/current career qualification remains pending. Evidence artifacts/tests/web/save-browser-paging.json and career-load-footer-readiness-pure.log.

## Compact youth progress charts — F172, historical Debug b3bf7776

A real native screenshot and tree expose an empty companion Progress cell despite a visible line. The immutable post-advice January checkpoint has six drawn points; both axes and axis lines are hidden, point labels/DataRefs are absent and the native integer coordinates have no displayed unit. CompactLineChartAdapter reuses FM26's readonly text/table-cell contract on both native cursor and HTML. It follows the actual native displayed-point mapping, retaining only left-to-right segment directions. It does not publish coordinates as ability, invent dates/months/percentages or query hidden person attributes. Noncontiguous/ambiguous mappings remain explicit failures.

The first correction passes 3 functional groups / 3 clean WCAG audits on Debug8da: an independent native point/schema oracle, native readonly cell versus DOM agreement, every other original table/control/value/order preserved, real Katie profile destination and exact Back focus. The final integration adds native IsBound/plotted-window readiness to the existing across-frame row harvest, and keeps chart data out of name/position selection labels. Current Debugb3bf7776 independently confirms a second real September case with only 2 displayed points, one level segment, native ready=true, row pending=0 and selection label Katie Reid, D(C). Its six-point full preservation regression is still pending; do not inherit the earlier result as final-module qualification. One undefined-ready diagnostic used a cached old Node helper; after reloading that helper, the actual new native fields pass. The earlier index probe used fields rather than the actual x/y properties and stopped with a managed reflection error; corrected property reads prove indices0/0 through0/5. Neither observation was a game crash or user input.

The final pure formatter suite passes 1612 tests with zero skips, together with loader/deployment/player/API fixtures. Compilation passes. Current native shared regression is running on exact b3bf7776; incident/profile, menus and training have passed. Current player candidate is 063205, Release dd435038-6ce1-4da4-bf8e-527447bf1fc9, ZIP SHA25652785DFAF50278F21924B59E5BBC16955D04CA63357C4B4CB7071F3911A11EC3, API455types/24588members. It is built but not installed/cold-qualified. Candidates051433 and061403 are superseded. No changed-data SSE, all-chart, full career, Steam or manual NVDA claim.

The maintained advertisement command/save/reload workflows also pass on preceding Debug727: 6functional groups/12 clean WCAG audits, one publication and one selective removal on separate freshly reloaded branches, independent saved outcomes on owned29 Coverage20261114054455832 and30 Coverage20261114054846810. Immediate native SIText and companion both show0/43 after publication, while the saved/reloaded state is1/44; this observed native counter lag is retained without guessing its cause or inventing an early count. Native and companion read-only saved26/27 outcomes remain separate exact-checkpoint evidence. Thirty owned copies and all23originals are preserved.

Evidence: career-youth-progress-before.json, native-chartprobe.txt, native-chart-schema.txt, native-plotted-indices-corrected.txt, final-debug-passed.json (8da), september-native-readiness.json (b3), readiness-identity-pure.log; career-staff-maintained-advert-*-debug-passed.json. New live-route-dropdowns.cjs is syntax checked only and has NOT run; do not present its prepared all-destination open/cancel sweep as coverage. F047 and F115 remain unresolved.

## F115: saved outcome comparison on current Debug 727

The original default regression was repeated from the pristine owned January checkpoint on Plugin 7275079f-ccb3-421a-96a3-3a03b1d52983. Exactly one actual development-advice command changed the recommendation row to the native listed-for-loan acknowledgement, but the native form still read Sem estatuto definido (5 clean semantic audits, no unexpected JavaScript/request/network failure). This is a functional failure, not a successful action inferred from an acknowledgement.

A unique isolated post-command checkpoint, Coverage 20261114052208924 (owned copy 28), was saved once and natively reloaded. The read-only observe-f115-saved-loan.cjs diagnostic again finds Sem estatuto definido in both the companion and independently visible native SIText. The recalculated youth row now describes potential instead of the loan advice. Thus save/reload did not demonstrate the expected availability change. This narrows the investigation beyond an immediate DOM-only stale value; it does not establish the native engine cause or attribute fault to the original game. Evidence: career-staff-current-f115-default-result.json, career-f115-after-editor-working-created.json, career-f115-after-editor-working-reloaded.json and career-f115-saved-loan-observation.json. No advice was resent in the changed branch.

A separate fresh-pristine comparison is testing a single native ClickEvent for untouched Cancel, without SimulateButtonClick or a supplementary event. This is a Debug diagnostic only; production button activation is unchanged. A failure to close stops before advice. Earlier Submit/focus/pointer comparisons remain retained with their original results.

## Current staff career coverage — November 14, 2026

On Debug Plugin 7275079f-ccb3-421a-96a3-3a03b1d52983, the native advertisement lifecycle has both saved outcomes verified: one Arsenal scout advertisement (1 own / 44 total) on published checkpoint 26, and its selective removal (0 / 43) on withdrawn checkpoint 27. The maintained read-only outcome driver passes 2 functional groups and 3 clean WCAG audits for each checkpoint, compares the exact save SHA256 and all 43 other vacancy records, and verifies the actual card destination and exact return focus. Evidence: career-staff-published-advert-persistence-debug-passed.json and career-staff-withdrawn-advert-persistence-debug-passed.json. Immediate post-publication refresh remains unqualified; saved persistence alone does not explain its timing.

The updated native workload-legend regression now also passes on this exact module: 2 functional groups / 3 clean audits, independent visible native count/category pairs, exact immutable checkpoint/hash, original content/control preservation and actual coach-task destination/Back focus. Action-menu cursor/count metadata is excluded from historical comparisons only after independently verifying the native adapter kind. Evidence: career-staff-current-legend-debug-passed.json. This does not establish changes to live counts, all job types or whole-career coverage.

Candidate 20261114-051433 contains the current source (Release MVID a09e2189-5407-4827-8daa-2d2daa6d059b; ZIP SHA256 5684EAE5FF265F940191682466F1FB182193F07651DACB1032DDEE45E4BD18E5). Compilation and packaged production-profile checks pass; it is not installed or cold/public-qualified yet. Its API manifest contains 454 types / 24,521 members. Earlier candidates are superseded. The 23 original saves and all installed receipt files remain unchanged; 27 explicitly owned additional career copies are retained. F047 and F115 remain unresolved. No stable release, full career, Steam or manual NVDA claim.

## Staff control audit correction and separate recovery policy — F170

The earlier six-missing-controls interpretation was incorrect. Retained career-staff-workload-overview-before.json already contains3BoardRequest selectors,3PlaceAdvert selectors and2customization selectors. A button-role query and innerText missed their accessible names because these real native SIDropdowns are rendered as comboboxes. No restoration of those already-present controls is claimed. Exact correction evidence is career-staff-control-role-interpretation-correction.json.

Independently, the actual SSE suppression policy allowed a known partial body to suppress recovery merely because its final native shape matched. The existing immutable ComposedShape receipt was extracted into a pure file and now requires completeness before suppression; both existing callers reuse it, with no retry-cap/window extension or automatic budget rearming. Red policy tests2fail/4pass become6pass; full1602pure/loader/player/API checks pass0skips. Actual727 native receipt partial=true/suppresses=false is observed. Six menus and all5nested paths pass2functional groups/12 cleanWCAG, exact native widget/department mapping and parent/root focus,0businessleaf selections. No deterministic lost-control reproduction or whole-career/production stability inference.

## Retired-core cleanup and stop-related timeout — F168 / F169

A managed-only probe of actual game15136 shows retired Plugin assemblies retained native adapters: handles/LiveValues/cachecontexts54/166/1,63/180/1 and28/58/2. CoreEntry.Unmount now closes the popup it owns through the existing dropdown lifecycle before stopping the pump, then clears only its managed classification/command/value registries after server stop. It does not reset native providers, edit values or reopen native channels. Actual same-source Debug98c reload leaves corrected retired registries0/0/0 and0popup levels, while the replacement remains populated. Already-retired legacy assemblies retain their historical state until a cold process restart.

Separately, a regression against the actual linked MainThreadDispatcher reproduces a queued caller waiting after Stopped=true with no next Unity frame. The corrected stopped setter and enqueue share a managed gate; rejection callbacks signal pending callers and drop closures without executing any queued native action. Both single-caller and16producer tests pass with0callback executions/empty queue. Full1596pure/loader/player/API checks pass0skips;68unchanged JavaScript checks also pass. This does not establish the root of F047preHTTPWindows10060.

Maintained live-reload-registry-cleanup.cjs passes2functional groups/1cleanWCAG: an actual open FMMenu is closed on one native reload, a fresh sole client receives identical choices, native Preferências e Acessibilidade opens/closes toPortal, gamePID and Host session cookie remain unchanged. Health.instance is intentionally per-core and changes. Two earlier test assumptions (Host versus core instance, shortened native preference title) stopped the test and are retained as test observations, not product failures or physical input. No token is logged. Current Debug98c still needs normal-action/shared qualification and new cold/public production qualification. Candidate032608 predates these corrections and must not be installed as current.

Exact evidence: artifacts/career-reload-retained-core-registries-before.txt, career-dispatch-stop-queued-red.log, career-dispatch-stop-cleanup-pure.log and career-dispatch-stop-cleanup-native-passed.json.

## Actual staff recruitment acceptance and persistence — F167

On owned staffRecruitmentWorking22 (July7,2027), the production staff database opens actual free scout Kelly Adams. The real Actions/Approach command supplies native requested terms: full-time scout,700EUR/week, immediate start and3yearsJuly2027-Jun2030. Five actual duration choices open/cancel with unchanged value and exact focus. ONE FinalizeAgreement reaches native pending final decision; this alone is not hiring proof. Maintained live-staff-contract.cjs --propose independently repeats the pristine22 proposal branch and passes2groups/5clean WCAG checks without Save or simulation.

PendingStaffWorking23 is uniquely saved. Real native delegated calendar progression1+3days returnsJuly11; each progression passes5groups/7clean WCAG. News156852 and156899 confirm actual appointmentJuly9; recruitment increases10→11, scouts3/13→4/13 and vacancies26→25. Actual profile showsArsenal/Olheira/700EURweek/endJune30,2030. HiredStaffWorking24 Coverage20261114032818622 is uniquely saved and reloaded; real department row/profile retain those exact values and Back restores the same employee link. Appointment/profile audits are clean. This is real accepted business and saved persistence, not a mock or dispatch acknowledgment. Maintained outcome regression and new production qualification remain pending. No other job-type or all-career inference.

Evidence: artifacts/career-staff-kelly-signed-news.json, appointment-news.json, employed-profile.json, reopened-persistence.json, career-staff-maintained-proposal-passed.json and owned save/reload proofs.23originals preserved;24 explicit owned career copies now exist, autosaveFALSE. No manual NVDA or Steam claim.

## Native staff workload legend — F166

On Debug8ec75084/game15136, the native CoachAssignmentsTile_4x2 category legend contained count, fixed radial category artwork and localized severity. The previous output misleadingly combined count with the artwork value (for example6x:25of0to100). RadialGaugeAdapter recognizes only the demonstrated asset/visible leaf schema and ScreenWalker reuses the original LabelValueItem: Muito Forte:0x, Forte:0x, Mediana:0x, Leve:6x. Other meters/help/content remain intact. No invented count unit, percentage, business rule or game mutation.

Maintained live-staff-workload-legend.cjs passes2functional groups/3clean WCAG checks: independent visible native count/category tree, identical original content controls and tables, existing readonly LiveValues snapshot, actual card destination with six light training-area rows, Back/exact origin and reopening of all four pairs. No changing-count SSE event is claimed. The visible coach matrix0stars are independently corroborated actual0/0 widget values; no speculative star-helper change. Build passes0errors (one inherited Navigation warning) and1594pure/loader/player/API checks pass0skips. Exact artifacts/career-staff-workload-legend-debug-passed.json and native evidence; current production/shared qualification remains pending.

## Actual accepted renewal and persistence — F161

On owned July2 contractWorking19 Coverage20261113232046827, native recent-departure refusal for released Katie Reid was readable and Exit worked. Real Maddy Earl renewal proceeds through the original confirmation (Dont-show preference untouched), actual10playing-time options/open/cancel/exact focus, preliminary promises, agent financial demand and one Finalize Agreement. A retained completed-phase command is refused with the same screen token and unchanged state. Requested terms are800EUR/week, native loyalty3,3mEUR, agent800EUR and12monthsJuly2027–June2028 plus3native bonuses. The agent initially states final player decision is pending; Change/Withdraw Offer actions corroborate actual proposal creation.

PendingContractWorking20 Coverage20261113233949812 is saved once. Real native holiday processing2→3→4→7July (one,one,three days) passes5groups/7clean WCAG checks per explicit progression, never replaying a command. This is delegated progression, not manual matches. News154686 confirms Earl signed6July through30June2028 at800EUR/week; actual profile changes from900EUR/week/N.D. to800EUR/week/30June2028. AcceptedContractWorking21 Coverage20261113235458286 is saved once, natively reloaded and exact wage/enddate persist. News/profile semantic audits clean. Exact career-contract-maddy-signed-news.json, signed-profile.json, reopened-persistence.json and owned save proofs.21owned copies;23originals preserved.

A new defect F162 is exposed by the signed details: official caption0,7% but aria-valuetext0% and visible0,7%:0%, still after reload. Inherited ProgressBarAdapter truncates width percentage to int. Metadata-only native API confirms no public value getter; m_progressHandler and binding configuration exist and must be inspected on the Unity thread. Fix is pending; semantic axe success did not catch this value contradiction. Current221619 remains a candidate, not stable/full-career qualified.

## F115 current Release comparison: original versus quiet cancellation

Both fresh owned JanuaryArrival3 cases run on the SAME current Release5ff2bc9c/game19436. The ordinary ButtonAdapter case still reads Sem estatuto definido before and after ONE development advice command, with5clean WCAG checks and0JS/request/CDP failures. The quiet case explicitly closes its SSE connection, waits for the actual server worker-end log BEFORE each untouched-form Cancel, then reloads the real native player profile to restore normal handlers. Both closures are independently observed; one advice is sent, and the reopened field still reads Sem estatuto definido.5clean WCAG checks,0unexpected request/CDP failures; two intentional EventSource aborts are retained in each observer and classified by exact endpoint/error/time window, not discarded.

Artifacts career-f115-current-release-default-result.json and career-f115-quiet-current-release-result.json. The first quiet preflight failed before Cancel/advice because retained logs reused a connection ordinal across core lifetimes; the lifetime check now starts at the latest connect. That stopped diagnostic is separately retained at career-f115-quiet-stream-counter-preflight.json. Stopping SSE around cancellation plus a fresh profile document is therefore NOT a sufficient correction. No native provider reset, hidden property write, activation workaround, business-value override or command replay was introduced. F115 remains unresolved; neither failed business result is counted as success.

## Completed current player cold/public/destination cohort

Candidate221619 / Release5ff2bc9c on actual19436 passes cold5groups/2axe, public competition4/8, outcomes3/4, incidents+ratings7/10, finance5/13 and club history5/7, plus ALL124native destinations/8912control instances/124clean WCAG checks;0JavaScript/request/CDP failures in the passing cohorts. Actual native datasets retained from Debug4c9272e5 for the exact immutable September QA are used only as explicit Release conformance references; private inspection endpoints are absent in Release. The seven-fixture driver retains20scorer records/101rating rows/60actual player events, both exact profile Back paths and7actual body/action overlaps.

The first outcomes test stopped on its old global-toolbar comparison (Continuar versus contextual Próxima mensagem after Inbox). That failed observation is retained at career-ratings-release-outcomes-global-chrome-observation.json. The test now compares all content controls after the real page heading, exactly as the established competition preservation test does; it does not exempt captions or change product behavior. Competition passed in the initial suite and outcomes onward pass in the explicit resumed suite. Exact suite reports and per-driver passed evidence under career-ratings-release-*.json. Matrix defaults now point to this completed current native route cohort. These measurements do not establish every leaf business action or full career completion.

## Completed native match-summary regression

Current Debug13512/Plugin4c9272e5-eefa-4a85-9212-fd2e802428e8 passes7functional groups/10clean WCAG checks across7played fixtures:20scorer records,101native player/rating rows and60numeric player events. Independently observed player glyphs are sub off_small_3x, sub on_small_3x and yellow card_small_3x. Every visible rating matches the real numeric value rounded to its official displayed precision; native PersonReference identities and original row/event order are retained. Both Poppy scorer and Daphne rating-player links open real profiles and native Back restores exact focus/fixture. Seven actual read/action overlaps, the goalless case, penalty and grouped scorer minutes also pass. No Save, simulation or native business command. Full1579pure tests/loader/deployment/player/API fixtures pass,0skips. Exact artifacts/career-fixture-ratings-debug-passed.json; the earlier scoped-test failure remains separately retained.

The prior f285b01b shared36groups/53axe cohort is complete; the current4c9272e5 shared cohort is running. Candidate221619 SHA2567B0A33AFC25948C0485A1810222F7005CEA782E100809F10347929C2A1AE12A7 / Release5ff2bc9c-aee7-4735-aedf-1d20d19cfb58 is built but not installed/cold-qualified.220014 is superseded. No previous full-route or cold results are inherited. F047/F115 and complete career/Steam/manual NVDA qualification remain open.

## Native match-rating events — F160 qualification in progress

Actual native PlayerRatings binds each streamed row to Player, Rating and PlayerEvents. Its unnamed image glyphs have their own numeric Event binding and represent substitution/card information, not decoration. Current FM27FormTable reuses the existing live-cell pattern for Player/Event/Rating and retains complete player/context controls, official localized rating text and each original event list. ElementBridge/SpriteHelper only make these images informative inside the demonstrated PlayerEvents/PlayerRatings scope with a real numeric Event; shared MatchIncidentLabel supplies the existing labels. Portraits and unrelated unnamed icons remain unchanged.

First actual Leicester comparison confirms14PersonReference rows, native numeric ratings/official text and6event codes6/7/substitution assets; both scorer and rated-player real profile/Back paths pass exact selected-fixture/focus checks. The following Sheffield case exposes actual Steph Catley Event4/yellow-card information previously omitted. The first test stopped because its old global card assertion counted that rating event as a scorer incident; the failure is retained at career-fixture-ratings-test-scope-observation.json. Counts are now scoped to their respective tables and the complete run is in progress. No success is claimed for the stopped run.1579current pure tests pass with0skips. Candidate220014 is superseded by this new source, never installed.

## Selected fixture event semantics — current source qualification

Debug13512/Pluginf285b01b-727a-4e3b-af3a-33c8bd5a8528 reuses the FM26 MatchReportTiles Min/Team/Event/Player contract through existing FM27FormTable live cells. Actual KeyEvents has separate home/away lists; composition-owned native score headers supply team labels. Complete realized list counts, actual Event record schema and exact Name/Number fields gate consolidation; other shapes retain the original walker/paging. Player controls remain real native links, including exact profile Back focus. No event/model write, invented team, score inference or grouped-minute expansion is used in presentation.

Maintained live-fixture-incidents.cjs passes5functional groups/9clean WCAG checks for all7played fixtures,20actual scorer records, seven genuine concurrent body/selection overlaps, goalless case, penalty glyph, grouped scorer times, full36fixture data and selection/tab restoration. Independent native-fixture-incidents-probe.cjs records side/team/player/time/asset against the actual selected FixtureReference, without calling the production table/incident formatter. Evidence artifacts/career-fixture-event-table-debug-passed.json and career-fixture-event-table-native-first.json.1579pure tests pass,0failed/0skipped; loader/deployment/API/player fixtures pass. Unit compilation also reports one existing unused test-field warning; Plugin build has one inherited Navigation constant-branch warning,0errors.

Current shared history/league/competition/finance/club-history/squad cohort is running. New candidate220014 SHA2565A1CE9A87ACBC0310B2950EBADD7AC21843B86398C14C15EC2935AAA15BB4AA2 is built, not installed or cold-qualified. Candidate213647 is superseded, never installed. Earlier39/a29 results remain historical; full-career, Steam and manual NVDA are not inferred. F047/F115 remain unresolved.

# FM27 migration and reuse contracts

## Canonical fixture selection, history and incident qualification — F153–F157

Actual Debug13512/Plugin981edb7d completed live-fixture-incidents.cjs:4functional groups/9clean WCAG checks,7actual played fixtures,20native scorer/event records, grouped official minute strings and a real goalless case. All7consecutive/restoration selections deliberately overlap genuine native /body harvesting and explicitly return Selected; independent native selectedfixture references, player/time/assets and DOM agree. No command replay, match playback, Save or career simulation. Actual scorer profile opens correctly and Back restores the same selected past fixture and exact scorer focus. Original complete36-fixture table values remain intact; final selection restores the run's original Brighton fixture. Exact artifacts/career-fixture-incidents-history-overlap-981-passed.json.

Native controller-only UpdateSelection formerly acknowledged a temporary checkbox while the next body retained the previous fixture. The public VirtualisedList.SetSelection entry (already used by the original GridSelector adapter) updates the bound fixture. The command now observes canonical selectedfixturelistelement.Fixture/Index and its separate selectedfixture owner, not only a checkbox; native reference validation remains before dispatch and throughout observation. The existing fixture loader read lease is shared with cross-frame actions, so concurrent harvests cannot scroll their row away. The earlier FCF failure was an explicit BEFORE-command availability refusal, not a second false success; exact recovered replies/timing remain retained.

The native widget declares PersistSelection=False and history only opens Team/calendartab. FixtureHistorySelection extends the existing exact history-key snapshot with observed selection path/index/reference, restores through one native list operation under the same bounded press/read lease, and refuses changed references. It does not change history arguments, native persistence flags, providers or business values. A final scope guard admits only actual ExactlyOne lists with the native fixture-owner/record schema, preserving unrelated readonly lists and tab behavior. That defensive guard is now loaded as39befd67; its shared native-history regression is running and is not inherited from the981cohort.

The actual generic penalty glyph is misspelled penelities_small_3x. MatchIncidentLabel now reads it as Penalty kick without inventing scored/missed/shootout. Separate penalties→shootout mapping remains. The current actual FCF record is EventType2 Georgia Stanway at official pen90, independently corroborating that goal summary. A test initially assumed one scorer record meant one goal; Brighton instead groups Frida66,83 in one official string. That test assumption was corrected, no product row was missing or added.

1579pure tests and loader/deployment/player/native API fixtures pass, zero skips. Actual native table cursor Home/T/CtrlAlt arrows/inverse also passes on981 with no native business action. Preservation independently matches23original saves,33receipt files and exactly18owned extras. New source/player packaging, cold/current public/shared/full-route qualification and final normal companion restoration remain pending. Candidate201431 predates F153–F157 and is superseded, never installed. F047/F115 remain open; no whole-career/Steam/NVDA/stable claim.

## Standings direction and fixture outcomes — F151/F152

F151 reuses the original informative-image/table stop for the actual ArrowStateIcon/BindingExpect/SIStyleSetter. Native LeagueTableArrowState and actual chevron/line assets independently match every32senior/14full-stage/14youth position cell; no positions gained/lost are invented. Maintained competition regression on67fd passes4groups/8clean WCAG checks, retaining all original values, controls and exact heading-link Back focus.

F152 actual native schedule renamed its result style setter to IconStyleSetter and exposes m_addedClasses rather than the inherited single m_addedClass. All seven old score cells omitted native win/draw/defeat indicators. StyleSetterClasses reads legacy/current metadata through one adapter; FixtureScheduleContext keeps both native template names and reuses TeamFormReader official localized abbreviations. No home/away-score or colour inference. Baseline seven native identities/assets/classes are retained; corrected Debug13512/Plugin3dca94da driver passes3 groups/4clean WCAG checks, independently verifying all seven outcomes, exact original scores/buttons, full36fixture season/header/other-cell/control preservation, actual opposition club destination/Back focus and monthly/schedule inverse. No Save, simulation, match playback or fixture selection was submitted by this outcome driver.

Actual Add/Remove Leagues already names the current selection Jogável and exposes all three official native options. Thus its static legacy-property reference was NOT a reproduced user-facing missing-value defect. Its inherited specialized reader now shares the metadata adapter. Only popup open/cancel was used; no league change.

Evidence: artifacts/career-competition-context-direction-debug-passed.json, career-fixture-outcomes-native-before.json, career-fixture-outcomes-debug-passed.json and maintained native probes/drivers linked in matrix F151/F152.1578pure/loader/deployment/player/native API fixtures pass with no skipped tests. Plugin reloads have0errors/one inherited unreachable-constant Navigation warning. Shared native cursor/fixture/league/table checks and a new cold player package remain pending. No full-career/Steam/NVDA qualification is inferred.

## F115 fair native activation comparison — still unresolved

On fresh owned JanuaryArrival3, --native-cancel-pointer uses the original pre-activation lifecycle and sends one existing SyntheticPointer.LeftClick to the exact validated native Cancel. Both unedited forms actually close. Native OnlyAcceptsClickEvents=False, OnlyAcceptsSubmitEvents=False, CanSubmit=True. ONE advice then changes the native development row to loan-listed/removes its action, but the reopened form still reads Sem estatuto definido.5clean WCAG checks/no JS-request-CDP errors; failed business result is retained, not counted as success. artifacts/career-f115-fair-native-pointer-result.json. This excludes simple SimulateButtonClick-versus-pointer cancellation as a sufficient correction.

After a fresh explicit owned checkpoint reload, --native-cancel-event includes the same normal lifecycle and sends one exact-target pooled NavigationSubmitEvent. It does not close the untouched form; the driver stops BEFORE advice. An ordinary production ButtonAdapter Cancel subsequently closes that draft. artifacts/career-f115-fair-native-submit-result.json. No production event change, native focus bypass, provider reset, synthetic business value or uncertain command replay was introduced. F115 remains open; earlier advice-before-editor success remains a separately qualified context.

## Competition groups and native result series — F149/F150

The original FM26 profile/HeadingRule/rich-link renderer is reused to identify each actual CompName group and nest the StageSetter player-leader subsection. A fresh current native GUI and Unity framebuffer confirm the hierarchy. TeamFormReader preserves its original named-wrapper path, chronological ReadList and official translated result formatter. The FM27 adapter discovers the now-unnamed StreamedObjectList by its bound teamform schema, without caption/column-index/pip-colour guesses or game-model writes.

Debug13512/Plugin8d9d45ab maintained live-competition-context-form.cjs passes4groups/8clean WCAG checks. All four senior/youth heading links reach actual competition profiles and Back restores the exact opener. Every original control name and non-form cell/header/order is retained. An independent native-team-form-probe.cjs reads raw native item indices/values and team names, without calling the production reader/formatter:32senior rows(23populated/9bound empty),14full BWSL stage rows and14youth rows match;14rows per cohort independently agree with native J/V/E/D totals. The league overview mini-table has no Forma column and remains unchanged. No Save/simulation/business operation or manual NVDA. Evidence: artifacts/career-competition-context-form-debug-passed.json and its retained native datasets; before captures and fresh native tree/framebuffer are linked in matrix F149/F150.

Full1570pure/loader/deployment/player/native API tests and68JavaScript units pass, zero skips. New Release/shared-table qualification remains pending. Candidate182636/a29 completed its cold5/2, menus4/3, contract decisions4/3, strengthened alias5/6 and124 route/8907control/124axe qualification BEFORE these changes; its evidence is separate.

## Native alias arrival and native folded-stop identity

F147 now resolves the navigation proxy's declared OpenPanelEventContext target and uses the existing native parameter binding/tab adapter. The original same-panel-only correction was insufficient on first entry; its failed fresh-checkpoint F115 preflight remains retained. The completed5866 regression passes five functional groups/six clean WCAG axe checks, including Board Support→Squad Feedback, three local cycles, alternate/already-selected tabs, unrelated screen entry, inverse Global, exact tree origin/history and zero pending subscriptions. The subsequent stronger F115 run explicitly waited on both actual containing-panel UIDs and the actual selected Plantel tab, then reached the Cancel experiment. Thus the original guarded navigation preflight now passes. A further maintained alias fixture now explicitly waits on the first containing-panel UID too; that strengthened helper has not yet been rerun.

F148 reproduces SuppressStopFor retaining an already-folded stop across two managed wrappers over the SAME real native Pointer. The correction compares Pointer. Native before/after retain1→0; exact wrapper still0, distinct native element stays1 and outside-four-item-tail stays5. Only local managed lists over actual native elements were exercised, not synthetic native UI. Full1570 pure and loader/deployment/player/native API fixtures pass, zero skipped. Actual full competition-stage rendering remains a separate shared regression.

The exact-target pooled native Submit diagnostic (--native-cancel-event) now reaches the real unedited Cancel button and emits NATIVE_CANCEL_EVENT_SENT, but the form remains open. It stops BEFORE advice; ordinary original ButtonAdapter cancellation then closes it normally. This does not resolve F115 and does not justify changing production SIButton activation or forcing native focus. Evidence: artifacts/career-f115-arrival-fixed-exact-event-result.json. The earlier focus-based variant also stopped before Submit because CurrentFocus did not identify Cancel. No native advice/availability command was replayed.

Evidence: artifacts/career-feedback-alias-first-entry-5866-result.json, career-native-folded-stop-baseline.json, career-native-folded-stop-fixed.json and career-alias-arrival-folded-stop-pure.log. Current source includes original FM26 HTML; only the native target/parameter orchestration and duplicate-stop identity changed. New player packaging is running; candidate175506 predates both corrections and is superseded. Cold Release, shared calendar/history/menu and full-route qualification remain pending.


Native wrapper identity also applies to inherited subtree exclusions and context
boundaries. Actual readonly game probes reproduced tab membership False across
wrappers and fixture option duplication for a native button already represented
by another column. Corrected helpers compare Pointer; the same probe now keeps
1uncovered option and0covered duplicates. Tactic-preview caption pairing,
headerless tile first-child recognition and calendar traversal use the same
identity without changing FM26 HTML or native command semantics. See
artifacts/career-native-calendar-fixture-identity-passed.json and TESTING.md.

## Native wrapper identity — F141 and F142

F141 corrects deferred cursor restoration to reuse the existing pointer-aware PanelTracker identity helper. Its new three-case regression first reproduced a valid same-native-panel restore being canceled, while true replacement and failed native identity remained safely refused. Full1570pure tests pass/no skips. Cold Debug27944 loads Navigation236709c6 and the real Host comparison accepts a distinct managed wrapper over its actual active native panel. This is native seam evidence plus pure state-machine coverage, not manual NVDA.

F142 is independently reproduced against actual existing game UI, without a synthetic tree: distinct managed child/parent wrappers have equal native pointers, and native VisualElement.IndexOf returns0, yet original TreeWalker reported ancestryFalse and threw both index/direct-child errors. Pointer comparisons now preserve strict containment, occurrence/boundary checks and TableContext row/column ownership. Corrected Plugina0748b75 returns ancestryTrue/index0/directChild0/selfFalse, with actual Host comparisonTrue. Before/after: artifacts/career-native-tree-wrapper-actual-baseline-failure.json and career-native-tree-wrapper-a074-passed.json. Full1570C#/loader/deployment/player/native API contracts pass after the fix. Live selected-squad row/focus/stale-command regression is running.

Native conformance setup first had an ambiguous reflected overload and an MCS LINQ/ImageConversion compilation failure; these were tool preflights before conformance execution, not native crashes. The first row test also refused the empty-Squads Misa-first fixture before any row command; its stronger scenario requires the selected-position/Daphne-first checkpoint. Failed setup evidence is retained.

Previous Releasef747 candidate135538 passed124destinations/8908controls/124WCAG axe, cold5groups/2axe, offer5/9, earlier-slot history8/6, message-return8/3, search7/5, controlled response-loss2/2, completed-loan5/6, club3cycles/3axe, registration4/4, staff3/15 and empty-lineup6groups. Original23saves/32Release files/14owned extras match. These are pre-F141/F142 scopes. Candidate143419 includes F141 but predates F142 and is superseded for final qualification.

F115 remains unresolved in a fresh cold Release: one advice changes the native report to Colocado na lista de empréstimos and removes its action, while both form and public profile menu remain Sem estatuto definido. Exact reports: artifacts/career-current-tabs-release-f747-f115-cold-failure.json / f115-independent-menu.json. Do not infer a canonical value from dispatch or conflate this with the separate earlier direct-menu-after-form mismatch. No provider reset, debug/licensing event or native command replay has been introduced. Full-career, Steam and manual NVDA qualification remain unclaimed.


## November13 continuation — opponent formation and shared native selections

F140 extends the same native ancestry validation to consolidated tab bars. Actual first Recruitment-advice visit briefly included two outgoing technical tabs beside the current pair; no guessed names were introduced. Fresh-checkpoint Debug77f6 full staff driver passes3 groups/15clean WCAG axe across all five categories/ten modes/four repeated visits and exact Misa profile/Back focus. The combined opponent driver passes3 groups/8axe on the same module. Current source pure1567 and68JS pass/no skips. New player candidate135538 (SHA25622705A71C50DA8356ADA2A44E5AA4A905BFEB90E9140514BEC2A80CED0A09E2C) includes all four corrections; cold installation/qualification is now running. Preceding133552 candidate lacks F140 and is superseded.

Separate b351 broad native regression passes16groups/21axe, all nine responsibility categories9groups/9axe, and25native medical tab transitions. These retain their original module scope rather than being relabelled current Release. Preservation verifies23original saves/33Debug files/14owned extras. Current July6 FM27 staff advice uses direct named action buttons, not the older disclosure-radio layout; no such controls are fabricated for coverage.


F137 restores meaningful anonymous expected-formation diagrams using the original FM26 PitchGrid/PitchFormation/keyboard overview. A visible native any-player or goal-keeper marker must own a one-field Position record; unused indicators stay empty. Position-only entries do not fabricate player names, numbers or actions. Named/editable formation contracts remain intact.

F138 fixes selection-disclosure radios that previously shared a page-wide HTML name. Independent IP/OOP native tables could both be selected correctly while the browser silently unchecked the first one on every render. The renderer now reuses the existing per-table radio naming and explicit RadioGroup override. Native selection commands remain unchanged.

F139 was reproduced twice by the expanded regression: a tab changed during multi-frame table composition, leaving captured outgoing radios whose labels now read blank. The renderer revalidates group/radio native ancestry before rendering and requests the existing bounded provisional rewalk when obsolete. No timer, invented label, native replay or TabContext redesign was introduced.

Cold Debug4fc9d9e6 plus Pluginb351b9bb/game4312 passes the full opponent driver:3functional groups/8clean WCAG axe, both eleven-position native diagrams and all six native summaries at every sampled used/faced/row-selection state, independent checked properties/phase changes, isolated arrow focus and exact inverse/reopen. Evidence: artifacts/career-opponent-formation-b351-expanded-passed.json. Two earlier semantic failures and the independent-radio failure remain archived.1567C# and68JS pass/no skips. Shared native regression is running. New Release/cold qualification and source bundle are pending; candidate120356 predates these corrections.

The old whole-tab-empty description was inaccurate: the archived Release body already contained the two native tables/nested controls; current native summary values arrive asynchronously. This finding did not justify rewriting tab extraction. All manual NVDA and actual Steam execution claims remain excluded. F047/F115 and complete-career action coverage remain unresolved.


## June career / F135 full form-history graph

Actual115day February6→June1 and3day June1→June4 transitions each pass5functional groups/7clean axe, with native processing news and exact message return. Eligible Masaka application129118 is submitted once, acknowledged/processed and removed on reopening; actual refusal130442/120day wait is preserved, never relabelled approval. Owned June4 checkpoint Coverage20261113115113371 is new copy14; originals remain unchanged.

FM27 LoanReportCard uses StreamedObjectList_Large bar indicators bound to the same playerform/MatchRating100 data as the original FM26 pip reader. Source indices/axis labels alone omitted all ratings. FormRatingsReader.ReadOrderedSeries reuses its model/realised-root paths; the original roster formatter still reverses recent-first. FormHistoryChart consolidates only a complete verified native list/1..N sequence with its exact two native axis captions and no interactive stops. Existing SyntheticTableItem/AppendStringGrid renders it. No heights/pixels, guessed dates, new chart framework or ARIA grid. Current20point tables preserve raw zeroes and original0.0 formatting.

Debug962 independently verifies4players/80cells against each native NodeProbe binding, all report actions and exact Back/query/source-focus returns:3 groups/10axe, no client/request/CDP failures (career-loan-form-history-962-four-player-passed.json). Release must use this retained native cohort at the same immutable owned checkpoint/date and explicitly acknowledges absent Debug inspection.1565C# and68JS pass. Cold Release/shared124-route qualification and a source bundle including this change remain pending.


## November13 — native loan profile associations and full-Inbox search history

Debug aa5f089b/game2844 completes actual news81482 → Selina Cerci profile → native Back → Escape with correctly associated parent/loan club rows, native AgreedPlayingTime label, original Cerci query and exact message focus:4functional groups/4clean axe, no client/request/CDP failures. Evidence artifacts/career-loan-profile-search-return-aa5f-passed.json. Existing FieldLabelReader caption ownership and AppendJoinedTextParts extend the inherited banner table; native captions stay live. Full-Inbox native history rebuilds the search collapsed, so a verified explicit history continuation reuses the actual SearchButton/TextFieldAdapter once. Another nonempty query or changed native visit refuses restoration. Original history-message focus records retain matching-NewsItem return context through intermediate Inbox bodies.64JS units pass; new cold Release/shared qualification is pending. This is actual Xbox/native evidence, not NVDA/Steam/full-career validation. Happiness content is present in the current native/companion inspection; no unproven card patch was applied.


## Same-template native club navigation

Native ClubSite rebinds can reach the current page before its observable history
index advances. An intermediate refresh can therefore store a surviving header
button as the old slot's cursor. Extend the existing bounded nativeReturn intent
with source token/action and actual message identity; at confirmed forward arrival,
restore that existing source cursor before pruning the branch. New input cancels
the intent; expired/non-forward observations do not overwrite newer focus. No
native history writes, command replay or timing delay. Actual Arsenal→Manchester
City/Joie Stadium→Arsenal five-cycle Back/Alt+[ passes on bdfd2d77 with exact MCI
origin focus, three clean axe captures and three additional controlled-core tests.

OverviewTitleBar and ClubSite fixed Club crest renames masked actual entity labels.
Removing only these proven overrides reuses the original ButtonAdapter resolver
and preserves every native link. Actual Arsenal/ARS/MCI/Birmingham City identities
are confirmed. Other profiles need their own native evidence before changes.

Actual BWSL open-window registration reuses the original table/checkbox/button/
confirmation contracts. Changed24-player set and native inverse25-player set both
persist after leaving/reopening, restoring every original selection/availability.
The original game's green-check Inscrição pendente caption remains before/after
confirmation; use actual records, clean edit state and persistence as evidence,
not an invented success string or altered official caption. See matrix F130.

## November13 — native message replacement and offer field caches

A native priority transfer proposal can offer Aceitar/Deve Responder while its
list flags do not include the blocking response-needed flag. Preserve these
different states and actual Tasks membership; do not synthesize a blocking flag
from available optional actions. In the current Release career, the revised
London City/Cerci reply is80853 with priority, while the earlier Debug branch
produced80844. Discover the actual record and verify current message content/
enabled action rather than pinning the earlier ID or conflating those flags.

Native message search matches bodies as well as subjects. A queried Portal list
and the full Inbox have separate filter owners: opening an older Portal result
pushes Inbox, whose first unfiltered window may not contain that record. Extend
the inherited message-close contract only for this actual nonempty search.
Capture its official source history visit, manager, native text field/query and
NewsItem before activating the validated row. On explicit Close/Escape require
the exact adjacent source visit and unchanged query/identity, then use native
Back once. Do not copy queries, synthesize results or write history. Ordinary
Portal opens and same-Inbox searches keep their existing behavior.

The frontend waits through the intermediate Inbox before restoring the exact
result. New input cancels that focus handoff. Native Back restores the original
search widget; restore its existing revealedInput field/opener metadata as well,
without moving focus merely to record it. This preserves the later Close-search
return. Actual result/body/Close/Escape/caret/clear/opener paths are qualified in
F126, separately from the native business actions present inside that message.

An already mounted native offer can reuse an EARLIER history slot and renew that
destination's Identifier. Require unchanged exact preceding PanelID/Identifier
keys and the same destination PanelID, then retain its actual new Identifier.
Do not require the replaced destination's old Identifier to survive. Capture the
actual source Inbox snapshot/NewsItem only immediately before a validated content
command. The verified composite supplies cursor provenance alongside a coherent
body history header; the client retains both exact message-action and Inbox-row
positions across the native branch truncation. Require the recorded NewsItem
identity as well as its original canonical page key. Never use provenance to
execute an action, infer a different player, rewrite native history or replay a
business command. Explicit reselection captures a new origin; restoration retains
its existing origin. Native career loading clears old history cursor metadata.

F124 extends the FM26 contract table's existing live-value path. Value-named term
selects had been excluded from tracking because a bare state can be just an index
or empty. A native role Undo can finish after body composition with no shape
change, leaving a stale displayed role until reload. TrackContractValue now tags
the current managed adapter for the same ContractTermsText.ComposeValueOption
composition used by HTML. Keep the original source/description/detachment/action
identity, include revision stamps and use the existing SSE dropdown patcher.
Enabled and native-disabled values must retain their full caption. No new HTML,
business command, artificial delay or transaction/session recreation is needed.

Extend the inherited message/history pattern when a native offer replaces Inbox
at the same history index. Capture actual native history nodes and exact message
identity; reopen through native OpenPanel without modifying the history list or
replaying business commands. Asynchronous native transitions require an owned,
bounded rooted completion callback and restoration of the original history-event
guard. Native Forward can truncate the future while retaining the current index.
Observe actual interactive foreground and completion, not an index change alone.

Whole-offer negotiability has its own real caption without the per-term enum
collection. Passive reads must not open/hide native popups to obtain that caption.
Native loan preferred-position Undo restores canonical TransferOfferClauseReference
state but can leave SelectedPrimaryValue/SelectedIndex stale. Resolve unique
official captions against actual option records and their PropertyValue identity.
Only an unaliased, handler-free local UI binding may be synchronized when opening
the actual popup, omitting UpdateHandler. Read-only value polling must not mutate
the cache. Other binding types retain their existing contracts. This is not a
general business-value override or a fix for SetTransferStatusReference/F115.

## November12 — native development advice and style-only images

Reuse the FM26 ButtonAdapter/NativeButtonCommand and ImageAdapter/SpriteHelper
paths. The January youth table exposes an icon-only SIButton with actual command
EventFmxActionDevelopmentCentreAdvice, CanSubmit=true and a click callback; it
has no caption or tooltip. Name that command Apply development advice, without
treating every tick glyph as Confirm or inventing a new youth interface. Its
native transient acknowledgement is observable, but persistent loan-status
qualification remains unresolved: a baseline-form regression still reads the
old status, and the native menu itself marks that old status Selected. Preserve
this evidence rather than overriding a game state in HTML. See F113.

The support-change image is informative. Native SIImage.ImageKind=None and
resolvedStyle.backgroundImage has no asset, while m_imageStyleName identifies
ui-concepts-double_chevron_top_green. Prefer resolved asset names as before,
then use the actual SIImage style identifier. Describe this observed glyph as
Double upward arrow (green); keep47/100 and Satisfatório separately. Never
apply the attribute-row Up1 mapping or infer a numerical change. Existing image
announcement/paragraph semantics remain intact. Six pure mapping cases and two
native Debug support pages pass; Release coverage is recorded separately.

## November12 — message opening across an in-flight body

The production-readiness regression reproduced a visible unread subject refused
as no longer on screen, and a further Debug capture proved the old DOM handle
was already absent from ServerHandles. A new body replaces that native map before
its HTTP response commits. Private readiness polling had obscured this race;
Release tests now verify native-derived pane ID/subject without calling private
endpoints, and the same Debug driver can use --production-readiness.

Reuse FM26's FM.msgview HTML, message identity/focus/close contract and the existing
confirmed-body wait used by native history. When a body is already in flight,
retain an undispatched intent with the actual NewsItem ID and native owner; after
commit, require exactly one enabled current subject for that ID. New keyboard or
pointer input, another owner, ambiguity, missing record or expiry cancels it. No
command is retried. A definite native refusal clears modal intent and re-reads
the actual list. Existing native action/record guards remain authoritative.

The original shape observer also includes the native message button Pointer so
same-shaped replacement command owners trigger its established settle path.
RefreshMessagesTable revalidates full ancestry before re-reading a cached host;
hidden/detached outgoing lists use existing bounded provisional recovery and
never register stale rows. No new message component, grid, label-based lookup,
artificial delay or native command fallback was added. Pure coordination cases
pass; exact Debug/Release qualification is recorded in session/testing evidence.

## November12 — native processing message schema and interaction

Reuse MessageButtonAdapter and the original4column message table on both inbox
and processing surfaces. Actual ProcessingCard rows bind SIText Headline and
MessageSender, rather than MessageHeadline/PersonSenderName/PersonName. Native
subtree inspection proved populated fields behind unreadable HTML subjects and
empty senders. The shared adapter now accepts those exact schema aliases without
inventing previews, text translations or a separate table component.

The processing summary reuses the NewsItemButton template but its actual native
CanSubmit=false, Events=null and m_clickableAction=null establish an informative
row. Keep all data as text rather than offering an opening action that cannot run.
Retain real disabled commands when events/custom callbacks exist. Track native
message-action readiness through the existing shape boundary and preserve pending
subject healing through LiveValues. MessageViewSurface rejects read-only activation
before dispatch. No supplementary ClickEvent or speculative Submit fallback was
added; the native teardown-crash protection remains intact.

Native proof: career-processing-preview-native-before.txt and career-processing-
actual-native-flags.json. Earlier63c0 seven-day test established30readable rows,
not correct processing action semantics. The expanded baf8 qualification checks
informative subjects during simulation and a genuinely actionable inbox afterward.
Its first driver waited for a stable body after Confirm, which could consume the
whole simulation and miss in-progress observations. The corrected driver observes
the native phase immediately after one dispatch and requires its actual return.
Release qualification remains separate. No manual NVDA or complete-career claim.

The same career walkthrough exposed an informative compact-calendar glyph family
missing from the inherited aggregate mapping: training set pieces_small_3x.
Actual native Icon71/72/93 and their Event records prove the category; the aggregate
has numeric SessionType fields and no SessionName. Extend the existing category
mapping with Set-piece training, rather than hiding it as decoration. This uses
the established FM26 mod-base English fallback for icon-only aggregate categories;
native localized session names still take their original nonaggregate path. No
session, schedule, artwork or game localization asset is modified.

## November 12 — transport and virtualized dropdown readiness

Reuse the original raw loopback server, routes, SSE and main-thread actions.
Bounded HTTP1.1 persistence now preserves complete buffered request boundaries,
response byte lengths, HEAD behavior and per-request authorization. The original
requestText wrapper uses POST without changing component/query APIs or keyboard
UX; never replay uncertain actions. See TRANSPORT_STABILITY.md for measured
600→3connection comparison and current12,000query/47connection/native evidence.

The actual6208goalkeeper refusal exposed FM26's inherited dropdown realization
loop re-scrolling every attempt. TableContext.RealizeCellDropdown now accepts the
same optional scroll contract as RealizeCellElement; CellHandle/ServerHandles pass
that request-scoped choice. DropdownSession scopes its first scroll and uses the
existing CellHandle1000ms readiness budget. Native row/reference/context/disabled
guards are preserved. Current3cfa20full-harvest/open/cancel cycles and actual Misa
assignment/removal passed. Do not increase arbitrary waits or force enabled state
instead of allowing the native bind to settle. Other controls retain defaults.

## Production boundary and presentation-panel observation (November12)

Release excludes evaluator/probe/reload HTTP routes and DOM mirroring; normal FM26
HTML/actions/Alt+I/navigation remain. Host uses managed BepInEx logging and checks
receipt-owned native-build.json/required-api.json before a non-inlined Unity
initialization method. Compatible store/build labels are accepted by actual API,
architecture/family/metadata, pinned-loader and fresh-local-interop checks; reference
native fingerprints record qualification. See NATIVE_COMPATIBILITY.md. This covers later game updates without
changing platform activation. The existing companion displays a standard startup
error when initialization is refused. Its standalone runtime is .NET10.0.15; the
pinned loader process runtime is unchanged. See PRODUCTION_RELEASE.md.

Official regeneration of178interop DLLs changed every binary hash but preserved
every tested declaration/signature contract. Player installation uses exact game/
loader hashes and conservative MVID/timestamp-independent interop contracts, with
exact generated bytes as a fast path. No generated interop is redistributed. The
local Unity base-library ZIP was reused; external clean-machine coverage is separate.

FM27's GameWorldBackground could arrive below a loading screen and be restored by
the inherited FM26 tracker as speech cursor owner. The runtime warning/tree showed
only background_image. PanelObservationPolicy adapts the native observer using
actual semantic layers and the existing navigation interactivity registry. Covered
navigable/loading layers remain tracked; unknown native-interactive foregrounds
remain eligible. Noninteractive presentation documents cannot replace cursor/history
or activate another profile. Original PanelTracker, HTML, graphics and native
actions remain. Five pure cases pass; live qualification is recorded separately.

## Latest verified normal handoff — November12, ad65c959

The native game remains Live in PID3208 with productionad65c959 and one normal
owned companionPID22176. The diagnostic UI was replaced after exact owner/path
checks;9227 is closed and no UI automation/socket watcher remains. Fresh normal
Portal DOM(4tables) was observed after clearing only the dev inspection cache.
Original protected Dialog Regression career was reloaded natively without Save,
verified through Save As/Cancel.23protected saves remain byte/size/mtime exact,
no extra saves;32receipt hashes and original native assembly/metadata pair match.

Current build0warnings/errors;1451C#pure and17JS tests pass with0skips. Current
native qualification:13functional suites99groups/73clean axe, plus124destination
views and2explicit same-module registration follow-ups. All124final records are
coherent,8906control instances/7contexts; no duplicate IDs, multipleh1, unreadable
chart/table fallback, false-enabled-native leads, image-role noise or unexpected
JS/request/CDP failures. Previous1924/e6 results remain separately scoped.

The full card cycle executes25alternatives plusdefault=26actual content types,
27clean axe, native title/text/current-selection/reopen/exact-focus checks and
EXACT original Data Hub native snapshot inverse. Current empty-lineup6groups/1axe
again proves50rows, actual Misa16 assignment50→49→50 and bounded controlled
transport-failure recovery. Original native Quick/mod-sound settings and nine
established viewing preferences were verified without writes. One initial
supplementary assertion conflated MatchShowNames with MatchShowAllPlayerNames;
that failed diagnostic is retained, actual extra field recorded without claiming
an unavailable historical baseline. No setting was changed to satisfy a test.

Evidence: current-ad65-native-card-options-cycle-passed.json,
current-ad65-empty-lineup-passed.json, current-ad65-normal-owned-companion-restored.json,
current-ad65-post-normal-handoff-preservation.json and current-ad65-v10-complete-
route-review.json. Matrix102targetedflows still has0fully validated whole screens
and no complete business-action count. Full conditional-state/career and Steam
qualification remain unestablished; the mitigated Windows10060 root cause remains
unknown. No integral completion, zero-known-issue or manual NVDA claim. Graphical
appearance/clothing was excluded. The standing master audit request remains valid.

## Captioned native training arrows corrected —ad65c959

This is the current production qualification boundary. The1924 v10 destination
walk found two literal Arrow, image paragraphs in SquadScreen's training tile.
The agent cooperatively stopped after71captures/70coherent to investigate/fix
immediately. This was neither a physical user Escape nor a completed route.

The viewed native screenshot and tree show green up1 and red down2 beside the
complete localized improvement/regression captions. Extend the existing
ElementBridge captioned-image classification for this exact IndividualTraining
component only, while its own Title/Value are visible/bound and tooltip empty.
Other arrows, meaningful tooltips, missing captions and unknown values are kept.
No new HTML renderer, graphical edit or value/control removal was introduced.

Current ad65 build has0warnings/errors,1451pure passed/0skipped and the loader,
receipt and mixed-profile guards pass. Receipt-aware Plugin reload and runtime
reuse6exact/4adapted pass. New registered TrainingArtwork passes2groups/1clean axe:
actual game arrows remain painted, native1/2 and both official captions remain,
and a detached real-before comparison proves all other text and every control
name/order unchanged. No JS/request/CDP errors, Save or simulation. Evidence:
current-captioned-training-artwork-native-passed.json and live-training-artwork.cjs.
The first supplementary ancestry diagnostic hit an mcs metadata import error;
its corrected readonly query passed. That boundary is retained, not a game failure.

A related ten-case native/browser qualification and fresh full124destination
inventory-v10 walk are now running on ad65. The preceding1924 completed25cases/
126groups/98clean axe plus25medical transitions/110valid guards remain separately
scoped historical evidence; its interrupted route is not inherited. Original
native QA no-save restoration, final preferences/protected-save/receipt checks
and normal sole owned companion handoff remain due. No whole-screen, complete
career, Steam or manual NVDA qualification is claimed.

## Native pre-career corrections and current qualification —19242e9a

This is the latest qualification boundary; older dated sections below retain
their historical module/results and may contain superseded pending wording.
Production19242e9a builds with zero warnings/errors;1451 pure tests, zero skips,
and runtime reuse6exact/4adapted pass. Frontend source is unchanged since the
35f17-JavaScript-test pass. No permanent assembly or native package was changed.

Actual pre-career inspection found that the inherited Start Journey profile's
Database options rename erased the selected native Football Manager27 caption.
Remove that rename and retain the original native button, label and action.
DatabaseSelectionCard also adopted hidden FMPedia chrome as its page title.
ServerText now checks ancestor visibility and reuses its existing visibility-
pruned text traversal. The observed ApplicationProgressPanel uses the existing
mod-authored Loading title mapping, preserving native progress/caption content.

Advanced setup has the additional real structural gauge pair
[help icon, field caption, styled radial, own status]. Extend the existing
RadialGaugeAdapter pairing for that exact layout: retain the separate official
processing heading once and present its own Veloz:88of0to100 without detached
status or duplicate punctuation. No native value is hidden or invented.

Registered PrecareerSetup passes4groups/4clean axe on1924, zero JS/request/CDP:
native database caption/action/Close; correct Base de Dados title; actual3nation
rows; all5native continent radios and named country checkbox states; South Africa
selection/inverse; Cancel restores original rows and Start Journey. No database
load/Confirm, world/career creation or excluded appearance edit. The registered
initial advanced title was already ready; do not claim a separately sampled
transient loading title. Evidence: current-precareer-setup-registered-passed.json,
current-title-hidden-fmpedia-before.json and current-precareer* native captures.

Both reachable current GameplayCard and AdvancedSetupCard nation rows use
reference-bound generic CellHandle/RuleGroupReference, not legacy bespoke ordinal
LeagueRowHandle. Native hidden-old-table refusal and legitimate controlled-league
disabled reason were verified earlier. No demonstrated legacy retarget defect,
dead-code deletion or invented native choices resulted from that investigation.

Current1924 dedicated empty-lineup regression passes6groups/1clean axe: all50
native rows, controlled failed/stalled transports, actual Misa assignment with
native shirt16, reopen/cancel and native remove inverse back to50rows. No JS,
uncontrolled request or CDP failures. The complete log emits the report after
final assertions; unlike most suites, lineup.json has no success property.
Evidence: current-1924-empty-lineup.log and current-1924-empty-lineup-passed.json.
The original protected Dialog Regression checkpoint was then loaded natively,
declining one Save prompt, verified via Save As/Cancel, with no Save/simulation:
current-1924-empty-lineup-original-restored.json.

The preceding35f13-case targeted qualification completed63 groups/39clean axe
as an explicit Attribute prefix plus ConditionDate-and-later suffix. Its initial
date final-reopen assertion sampled provisional native binding; the corrected
driver waits for actual row readiness. Both failed diagnostic boundaries remain
retained. Separate DropdownFinalRefresh3/2 passes and the original uncontrolled
Attribute case now passes6/3. The ordinary date suite deliberately tests one lost
response; do not label that controlled failure an uncontrolled native error.

Current1924 functional qualification completed25suites126groups/98clean axe,
plus25medical native transitions/110valid guards(max5159ms), as an explicit12case
retained prefix and13case completed suffix. See current-visible-title-functional25-
before-v10-route.json. The obsolete club-gauge expectation failed and was corrected
from actual native Global evidence; that boundary remains retained. The initial
v9route was agent-stopped after24coherent captures to include native image-role
paragraphs; no user Escape or native failure is inferred. A fresh full inventory-v10
walk is running separately. Do not inherit35f/e6 results onto1924. Final original
native reload/preferences,23protected
save checks,32receipt hashes and sole normal owned companion handoff remain due.
No whole-screen, complete career, Steam or manual NVDA validation is claimed.

## Prior-commit focus race corrected —35f5a6ad

The current5a relevant cohort found an uncontrolled AttributeCondition failure
after native preset20: the next genuine options response arrived, but the select
never became ready. No JS/request/CDP failure occurred; the original query was
canceled/restored. It remains retained as a failed run, not qualification.

A separate exact native reproduction retained one genuine open response across
the naturally firing2s table-commit follow-up. That preceding callback blurred
the newly focused select while FM.dropdown.pending was true. The normal response
ownership gate then discarded valid options because focus had moved to BODY.
This is distinct from the older Windows10060 pre-HTTP transport observation.

The surgical35f fix keeps the inherited final refresh but prevents its blur when
a new pending/browsed dropdown owns focus. Registered DropdownFinalRefresh passed
3 groups/2 clean axe with genuine delayed-reply and already-loaded variants: focus
and owner survive, real options are adopted, no native command replay. Exact
native15 inverse and original query restoration pass; zero JS/request/CDP.
See current-dropdown-final-refresh-registered-passed.json and
current-dropdown-final-refresh-focus-native-reproduced.json. An earlier attempt
observed a different prior timer and did not reproduce; it is retained separately.

35f build0warnings/errors,1451pure0skip,17JS and runtime6exact/4adapted pass.
The13-case relevant native cohort is now running, starting with the uncontrolled
attribute case that failed, then date, identities, menus, fixtures, proposal/date/
contract workflows and gauge contexts. Do not inherit5a partial or e6 full-route
results onto this module. Native legacy pre-career league investigation, current
empty-lineup, original QA no-save reload/preferences and final protected-file/
receipt verification remain due. No NVDA, Steam or whole-screen completion claim.


## Native condition date and styled gauge corrections —5a1f6916

Actual native contract-date selection was missing from the specialized condition
HTML: the visible DateSelectorWidget was reduced to readonly19/9/2026. Reuse the
existing DateField disclosure/calendar/native commit session through a realized,
reference/context-bound ConditionRowHandle. Both date endpoints retain the original
DOM state capability; captured native condition guards protect intermediate steps
and a separate adjusted-value guard permits/validates the observed own commit.
Ordinary date fields retain their shared renderer/session.

Registered ConditionDate passed5groups/2 clean axe, zero JS/request/CDP: actual
menu Data de Final de Contrato produces row Contrato Expira;19→20→19September2026
is independently confirmed in native dateSelectedText and DOM, exact focus returns,
calendar reopen/Escape and logical E→Ou→E work. Old-value capabilities and retained
old date DOM after native reorder refuse both calendar and set operations. Original
sole Empréstimo/Listado query restored by native Cancel/reopen, no Apply/Save.
See current-condition-date-registered-passed.json and live-condition-date.cjs.

Native league processing also revealed a missed common gauge pair:
[SIStyleSetter[SIRadialGauge], SIText Veloz]. RadialGaugeAdapter now pairs the own
status and numeric value, retaining the separate official field caption once.
Registered ca LeagueGameplay passed5groups/2 clean axe, native pair/value100/0–100,
real choices/cancel, gender switch/old hidden command refusal and original rows.
England Delete is legitimately disabled with its official controlled-team reason.
Current GameplayCard uses reference-bound CellHandle, not legacy LeagueRowHandle;
the latter pre-career path remains separate. An immediate provisional empty-body
read and a persistent-Node assertion realm mismatch are retained as diagnostic
boundaries, not game failures. Actual readiness/native follow-ups passed.

5a build0warnings/errors,1451pure0skip,17JS and runtime6exact/4adapted pass.
Current12-case relevant shared regression is running independently from preceding
e6 full25functional126groups/98axe and124coherent schema8 route captures. Matrix
base remains scoped e6,124destinations/8896control instances/7contexts; these are
not complete business-action or whole-screen validation. Original native QA no-save
restoration/preferences/final protected files and current empty-lineup remain due.
No manual NVDA or Steam claim.

## Native rule identity, capabilities and exact attribute steps — e6a5be64

FilterCompareRuleReference Index changes on reorder, and same-type predicates can
also reuse the identical full reference with different native country values.
Keep complete native reference plus typed FilterValue/SelectedCompareFlag and
mapped predicate context. A row index or caption is not identity. Stable edit
IDs retain a separate DOM state capability; requestText captures it before any
wait. Server Run/dropdown/step compare it, and native realization revalidates the
actual predicate. Native popup selection captures the opening handle guard.
Capabilities describe UI state, not authentication.

Reference-based move IDs require the inherited action-focus handoff to follow
the observed native moved predicate, including duplicate same-type rows. Focus
uses corroborated current content; it never authorizes a command. Existing native
PositionSelectorAdapter now reads the own PascalCase PositionText first and the
FM26 position-text fallback, retaining internal slot IDs and native selection.

Attribute presets are not exhaustive: actual Cantos offers1/5/10/15/20 but +/-
selects16. Reuse ComboboxAdapter and the shared step renderer; snapshot native
availability, dispatch once, observe the changed value under unchanged other
predicate context. Preserve the context attribute in dropdown restore/syncValue.
Registered e6 AttributeCondition6groups/3clean axe confirms exact native16/inverse,
three real cancel/reopen cycles and original filter restoration. Native buttons
remain enabled at preset1/20; no hardcoded min/max business rule was introduced.
Wider current-build regression and other condition families remain separate.

## Native fixture/header identity and focused checked controls —857f73ae

Agenda fixture rows use own listelement.Fixture FM.UI.FixtureReference, not the
opaque integer Items model or display index. Shared reference serialization,
provisional harvest settlement and original FixtureRowHandle are retained. Every
command validates native visibility/owner, realizes and revalidates the actual
reference; native controller selection is issued once, then observed separately
through its bound SICheckBox. Full native ancestry refuses hidden outgoing lists
before replacement HTML. Native date/order/rules remain unchanged.

Actual eight header templates have no primary/menu callbacks; old Button-based
classification manufactured sort actions/state. ColumnHeaderAdapter reads actual
Clickable delegates and native sort controller state. Render ordinary th text
when no action exists; primary sorting and native menu gestures remain separate.
A menu-only header reuses the existing FM.contextmenu.open path.

The registered keyboard inverse exposed shared ignoreActiveValue behavior:
focused radios were incorrectly protected like unfinished text, leaving checked
property false despite correct incoming checked HTML. Exclude checkbox/radio from
that edit protection, retaining text/select/range protection and exact focus.
857f73ae actual repeat passed4groups/3clean axe, four native selection/inverse
pairs, actual Leicester club/Back and held-old-DOM hidden-list refusal. Separate
current real sorting9groups/2 clean axe and penalty3/2 passed.1451pure/17JS,
0fail/0skip; runtime6exact/4adapted and32receipt hashes match. Wider regression
remains ongoing. Evidence current-fixture-identity-checked-registered-passed.json
and current-fixture-checked-shared-{table-row-identity,penalty-options}-passed.json.
## Current diagnostic isolation and card availability —279274ed

An actual diagnostic request reproduced a destructive inherited side effect:
/read reset40live command registrations and41value sources to zero, cleared the
native dropdown owner and closed its popup while the companion DOM remained.
ReadingView no longer calls the document-replacement BeginBody operation.
Registered actual repeat passed4groups/2 clean axe:40/41 entries, native popup/
owner/depth/focus and every preference retained; Tactics independently preserved
198commands/67values, pitch count1/editabletrue, and native commands still work.
The failure and successful cleanup are retained separately. This is not a claim
that it caused every prior user timeout. Evidence current-read-isolation-before-
failure.json and current-read-isolation-live-passed.json/live-read-isolation.cjs.

The inherited tile-title renderer also unconditionally registered fixed native
SIButton wrappers. Actual Recruitment Contracts was disabled by its native
parent template while authoritative HTML exposed an enabled action. It now uses
the existing native SIText/TextAdapter heading; all real values remain in order.
Carousel headings reuse the same tile path and Open respects native availability.
Available Clauses still opens actual RecruitmentClausesCard and inherited Back
restores exact origin. Current279 registered TileAvailability5groups/4clean axe
passed, separately from earlier b503 manual2axe/registered5/4. Native screenshot
was visually inspected; native fields/actions remain the primary evidence.

1451pure tests pass0fail0skip, current build/reload0warnings/errors, named runtime
reuse6exact4adapted and32receipt hashes match279. Current native route audit
completed124destinations/205tables/9056form-control instances, all clean axe/pt,
no content-fallback notes.122registration captures were quiescent;2overlapped
SSE and still need named read-only rechecks before a qualified total.3557coherent
native availability comparisons showed no mismatch. These are instance counts,
not unique executed actions or whole-screen/business coverage. Broader exact-
module tests continue in current-diagnostic-read-shared-cohort-summary.json.
Earlier incomplete cohorts and driver caption-revision/report-schema boundaries
remain separate; do not inherit them as passes. No NVDA/Steam/full-career claim.

A working-tree-only extraction makes the existing initial static error document
independently testable. Its behavior is unchanged and not yet deployed/tested.
The planned component check serves one actual production-builder error document,
then requires real backend/SSE recovery; it injects no native exception/method
patch and adds no alternative server. This is separate from native crash cause.
## Current selected-language correction —2d802caf

The companion now declares the actual selected game language in its initial
HTML and each committed body update. Native resource names are converted to
standard tags through the shared DocumentLanguage helper. All21 installed
native preference records were inspected; actual US English and Latin American
Spanish aliases retain en-US/es-419. Unknown custom resources remain und.
Native defaultCulture stayed pt-BR during English selection, so Windows/culture
is deliberately not used. English mod-only loading/error pages remain English.
The inherited FM26 shell, refresh, controls and one SSE connection are preserved.

Manual evidence includes fresh en-US and actual Confirm to pt/en/es-419,3clean
axe, original Portuguese/allQuick preferences restored and all23 save hashes/
sizes/mtimeNs unchanged/no extras. First a7e01f0a registered repeat passed5groups/
4axe. Final2d802caf repeat also passed5/4; MessageReturnFocus repeated4/3.
The current exact-module shared/124destination route cohort is still running,
not inherited from earlier modules.1451 C#pure and17JS units pass0fail0skip;
final solution/reload0warnings/errors, runtime6exact4adapted/32receipt hashes pass.
An earlier build's227 inherited TEST warnings remain in its raw log.

Evidence: current-document-language-live-passed.json, current-document-language-
registered-first-passed.json, current-document-language-final-cohort-summary.json,
current-document-language-final-receipt.json, native-choice-fields.json and
DocumentLanguageTests/live-document-language.cjs. This is four exercised native
language variants and full installed identifier mapping, not all language/game
business branches, NVDA, Steam or whole-screen/career qualification.
## November 11 — current message-return correction and signed-contract evidence

Production Plugin da8731e4-b38f-4ff3-96d3-3f1cec5b35a6 corrects the reproduced
Portal-to-Messages focus loss using the existing native NewsItem identity in the
original FM26 message module. Original HTML ids/parents can change while the
record survives. Unique record matching, Unread next-row/empty-filter fallback,
new-input protection and stale HTML stash release retain the inherited UI.
Actual manual validation passed5groups/2 clean axe; registered MessageReturnFocus
passed4groups/3clean axe.17 production JavaScript units and1420 C# pure tests
passed, zero failures/skips; build/runtime and all32receipt hashes passed.
Evidence: current-message-native-return-live-passed.json,
current-message-native-return-registered-passed.json and production unit source.
The first registered readiness failure and intermediate-destination/startup test
boundaries remain retained. Shared current-module regression is still running;
previous a7 full destination/two-match cohorts are not inherited by this module.

Actual player decision completed independently: Lotte's September23 signed news
and reopened native profile agree on2,9m € p/s through June2029, replacing the
original2,2m/June2028. Pending offer had reopened before the final decision; the
withdrawal option disappeared after signing. Evidence current-contract-player-
decision-passed.json explicitly separates a7 signing from da873 message follow-up.
Original protected career/date/profile/autosave preferences are restored and all23
save hashes/sizes/mtimeNs unchanged/no extras. No save submitted, no pending inverse.
This validates one actual renewal decision, not all contract/transfer branches.

The document-language defect recorded below was subsequently fixed in2d802caf.
See the current selected-language section and evidence; this earlier da873
snapshot is not a pending-language statement or current-module coverage claim.
No NVDA, Steam or complete-career/whole-screen/zero-bug claim is made.

## Observed native history tabs and asynchronous return focus (November11)

FM27 history opening arguments do not track later local tab choices: opening
calendartab2 can coexist with actual General0 or Training1. PanelHistoryNode is
an IL2CPP value type; every history getter boxes a new copy with a different
Pointer. These copies must not be used as entry identity. The actual history
slot plus official PanelID/Identifier identify the retained logical entry.

Before leaving a validated native action/navigation context, NativeDestinationTab
captures the actual selectable group option for selection paths declared by that
entry's root integer argument names. It does not reuse their stale values. On
Back or Forward, it revalidates the official entry and foreground identity,
reuses GetIndexForOptionValue and the original TabContext activation, and observes
the selected result within the single existing five-second history deadline.
At most one command per retained field is dispatched. No history/model writes,
fixed caption/index lists, synthetic category reopen or native command replay.
Snapshots are pruned when native history branches replace their entry.

Actual General -> preview -> Back and Training -> unsaved editor -> Back on
4675 independently required and received one native correction, retained the
unchanged opening argument2, restored complete original data and exact origin
focus. Fixtures2 -> club -> Back required no redundant command. Forward initially
bypassed that restoration; 8029 extends the same landing observation to both
directions, without restoring the virtual message overlay on Forward.

A rapid opposite shortcut or native Forward button can follow a provisional body
whose native history header is deliberately absent. app.js retains cursor memory
but does not use that stale index as another command's source. It waits for a real
confirmed body under the existing25s handoff deadline, sends the native command
once, and cancels an undispatched request after newer actual input. Standalone
modifier presses are not cursor choices. A pending global Back/Forward control
cannot overwrite the remembered content origin while that body is arriving.
No fixed delay, guessed index, synthesized header or uncertain command replay.

Final a7a20378 actual NativeHistoryTabs PASSED seven groups/seven clean axe scans:
General/Training/Fixtures Back, latest local choice, ordinary inherited Forward,
and shortcut/native-button Forward while one genuine Portal body was held.
The held shortcut response really had no history header; a later confirmed body
resolved readiness. Each required native correction was exactlyone, unchanged
Fixtures requiredzero; actual option, complete rows and exact origin focus agreed.
Zero JS/request/CDP errors. Native groups absent during panel arrival are awaited,
not interpreted as successful restoration.1420pure tests/0fail/0skip and runtime/
hash checks passed. Seven production-core JavaScript units cover readiness,
new-input cancellation and the expired-resolution microtask race: deadline is
checked both when readiness resolves and before sending. Separate actual held-
body cancellation passed1group/1clean axe with zero Forward requests. Broader
current-module shared regression passed12suites62groups/46clean axe. Earlier6a5
counter0 passes do not prove this branch. MainMenu0/count1 and Preferences1/count2
both provided actual history readiness; inherited Back restored exact opener.
No speculative unavailable-history protocol or synthetic header was added.

## Shared native form notifications and empty add-on previews (November11)

The training-name form uses notifications-modal-dialog-default-default with a
frame/body/header caption and native action strip, but no dialog-description.
ServerText resolves that actual noninteractive SIText and routes it through the
existing native dialog title/renderer/focus contract. Do not adopt edit values,
passwords, action labels or an internal panel name as a localized heading.
Existing description-based confirmations retain their original path.

Add-on save/load browsers draw no-image-fallback under a native no-preview-switch
and no-preview-image under SIVisible/no-preview-image-visible when there is no
preview. These proven shared placeholders are decorative. ElementBridge keeps
their parents/metadata/real preview action and omits only those image stops.
This does not change graphical customization or hide unknown informative media.
Actual template create/save/reopen/local export/import and native QA cleanup
passed7groups/5clean axe across named modules; final43990 input/weekday regression
passed5/4, zero JS/request/CDP. Both placeholders are verified; shared suites run
separately. No actual-week application, restart or cloud-write inference. F069/F070.

## Undated weekday references are not pending dated records (November11)

DynamicReference derives SI.Core.Record. The native one-week training template
therefore exposes a Date field whose value is legitimately null, plus resolved
DayOfTheWeek and no day-of-month. The old date guard refused all its slot buttons
as calendar-date:pending. CalendarTree resolves that native schema/header relation;
CalendarCommandIdentity retains the actual reference Pointer, owner and binding
scope, and StopHandle strongly holds/revalidates the reference. Shared ReferenceID1
is not unique day identity; captions/indices never become command identity.
Ordinary dated GameDate behavior remains unchanged. Original Read API stays intact.
Final5418 actual4groups/3axe plus shared22/24 passed; retained actual handle and
current-token route refuse after Back, while fresh real slot selection works.

## Native one-week training headers are carried by the cells (November11)

CreateTrainingScheduleTool has a single/no-prefix DateGrid with seven native
DynamicReference day bindings and localized Header text within each DayCell,
instead of a separate DayNames row. The existing reader fell back to internal
Monday–Sunday keys despite native Portuguese labels. CalendarTree now reuses
those bound, visible cell captions at their actual native column indices through
CalendarScheduleHtml's existing native-label input. Date-record monthly grids
keep their original header path. Afterbfaf actual TrainingCalendar6groups/10axe
and four shared calendar suites passed22groups/24clean axe, original calendars
unchanged, zero JS/request/CDP. No guessed dates, translated list or new table.

## Native favorites are separate from companion Ctrl shortcuts (November 11)

The pristine FM26 ScreenWalker replaces BookmarksPopupWidget with the mod-owned
BookmarkEditorSurface and hides the native bar. Its older bookmark-mapping plan
does not describe that shipped implementation. FM27's actual native floating
popup contains Favoritos, bar/all tabs, a label switch, native category filters,
reorder/pin commands and 32 bound favorite records in the observed career.
Reusing only the Ctrl editor therefore omits real game functionality.

NativeBookmarkSurface adapts the existing FilterPopupSurface, TabItemAdapter /
TabBarHtml, NativeFormTableItem, ListWindowSurface and ButtonAdapter. Each row
retains its actual SI.Core.Record reference, ID/Value/PropertyValue destination,
list owner and captured pin state. Stable keys use destination identity, never
captions or display indices. Both StopHandle and direct activation revalidate
the bound record before the native command. Native pin changes participate in
the popup signature. The original mod Ctrl editor/store remains separate.

Shared popup extraction now folds only a checkbox's actual resolved text, so
unrelated titles/help remain readable; generic streamed popup lists expose the
inherited native window controls. Field-label searches stop at FloatingPopupContent
instead of borrowing an underlying page's caption. Programmatic native bookmark
navigation left its floating popup visible over the new destination: a queued
close now waits until the following Unity frame, retaining the originating widget
and session and cancelling on reload. It neither replays navigation nor closes
a newly opened popup session.

Actual held-body pin/reorder tests refused retained commands with current tokens,
restored exact membership/order, and retained the reordered record's action focus.
The label switch survived close/reopen in both directions before its original
state was restored. Native destination UID and actual popup closure agree.
The reset menu is listed/cancelled, never executed just for coverage. The first
repeatable suite reached all native assertions but incorrectly required a Ctrl
table when its genuine inherited state was empty; that harness failure is retained
and the test now accepts the actual empty-state contract. Final f77 repeat passed
eight groups/four clean axe, then nine shared suites passed49groups/47clean axe,
zero JS/request/CDP. Detailed evidence/limits are recorded in Testing; no complete
screen or business parity is inferred from these targeted results.

## First native readonly page title shares the section heading (November 11)

NewsAggregatorSite's current first header uses ordinary native SIText Title;
the FM26 profile's ForYouTitle rule does not identify it. The synthetic section
heading therefore immediately repeated the same native paragraph. RenderSection
now reuses only a FIRST readonly TextAdapter whose cleaned caption equals the
section title, through the existing native-title heading renderer/LiveValues.
Later same-name native content groups and all controls remain. Two actual news
destinations passed native/DOM text-count, source-ID, neutral-focus/table and axe
checks on aef952d8. No second component or screen/caption allowlist was introduced.

## Current native pair: complete-match continuity evidence (November 11)

Xbox PC0.9.2435/Plugin08d completed two full native fixtures on the same production
WebView2 page/EventSource, including the MATCH_NONE career boundary and separate
new match Pointer. The second match's goal619 below the previous1868-event
frontier was delivered correctly. Both kickoffs, four halves, fulltimes and
career returns passed with zero JS/request/CDP failures and seven clean axe scans.
The installed companion-local TCP mitigation was enabled; native server transport
and OS policies were not changed for this run. Underlying Windows cause and broader
career/Steam qualification remain unproven. Older failed-package evidence stays
failed. See Testing/F036 and current-new-build-two-matches-one-sse-passed.json.
Original preferences/checkpoint and all23 exact save hashes were restored.

## Native paging helpers validate their actual owner (November11)

ListWindowSurface.PageAdapter is synthetic HTML chrome over a real native
ScrollView. Its original always-true IsVisible and detached Activate early return
allowed StopHandle to report Activated although the native list was gone. Actual
retained production handle/current-token route reproduced this while real
replacement body rendering was withheld; native owner attachedFalse/visibleFalse,
adapter/handle validTrue. Source now uses actual native panel/ancestor visibility,
StopHandle validates that typed paging owner before dispatch, and direct activation
throws a contextual error. No new HTML, key, caption exception or command replay.
Current08d actual3 groups/3axe verifies refusal with old AND current tokens, unchanged
native destination and still-functional fresh paging/original newest return.


## Native message record identity and true paging boundary (November11)

Native Messages virtualization can include index0 in overscan while its actual
viewport is still scrolled; late row-height binding then evicts that first record.
Current game showed view1-22/offset555.9026 after the old newest helper stopped;
actual newer paging restored the same first NewsItem25560 at view0-16/offset0.
Overlapping25559 changed its old physical-path-based DOM ID across that scroll.
Use NativeMessageIdentity.ListOwner plus actual bound NewsItem id for the handle
key. Preserve the native screen/record checks, native windowed model and original
FM26 table. ListWindowSurface retains its existing newer paging until physical
scroll start even with first-index overscan. A final page-up may keep the same
records; actual native start/chrome disappearance is then the observed result.

Current3e real4groups/3axe verifies native0/799.96875/0,11overlapping stable IDs,
16stable returns, independent captured/current NewsItem values and refusal of
an evicted old command without changing the reading pane. Duplicate-message and
native club-history5groups/1axe also pass after restricting the category boundary
to an already open UID crossing BOTH entity and category. Ordinary Alt+1 retains
its direct history. One deliberate lost message reply executes once, no replay.
Other native list families/timing variants remain separately qualified.


## Native destination category, entity and local tab (November11)

FM27 can retain a local General view while its current navigator destination is
Training. The original OpenSubtabEvent intentionally short-circuits the current
PanelID/entity. NativeDestinationTab reads the live entry's opening bindings,
matches the actual visible TabbedGridLayoutElement/ButtonGroup.SelectionBinding,
resolves the native numeric property in m_openEventScope through a rooted
callback, maps it with GetIndexForOptionValue/IsSelectable/m_slots, then reuses the
FM26 TabContext/TabItemAdapter. ConstReference is not a DynamicReference/Record;
FindOrBringBranchIntoScope resolves its branch, not the final numeric property.
BindWithinScope supplies that actual property asynchronously. Never parse const
names into invented numbers or prepare/send events merely to read a value.

Only the HTTP caller waits; callbacks collect data and never activate controls.
Revalidate navigation generation, current UID/entity, interactive panel Pointer,
ButtonGroup selection/path and actual button identity immediately before the
single command. Unbind on every exit and Plugin shutdown. Same-UID changes arm
the inherited bounded SseStreamer action-shape settle window; otherwise native
session buttons can arrive after the first date-only body without a token change.

The native OpenSubtabEvent handler does not consume MainTabPanelID. Direct
cross-category same-UID senior/youth navigation reproduced the previous team's
actual calendar on return. For an already open UID crossing BOTH entity and
category, NavigatorIndex activates the actual owning ribbon category once, waits
on its native selected main tab, re-resolves and dispatches the original subtab.
Ordinary direct destinations/shortcuts retain FM26 history. The first broader
condition inserted Portal into message/club history and FAILED the real message
identity regression; current1a561f60 restricts it and is undergoing live repeat. No forced panel reopen, second UI,
caption allowlist, guessed index, binding setter or transport/command replay.
Read-only bounded native-code investigation is ignored evidence, never offsets
used by the production implementation.

Current98b real five-group/five-axe regression passes local General/Fixtures/
Training, no-change, inverse General, youth-specific sessions/fixtures and exact
senior five-week return, zero unexpected network/JS and zero pending bindings.
FM26 menu focus/history and original HTML remain. Additional shared regression
and timing-specific refusal conditions are tracked separately in Testing.


## Equal-caption floating menus retain their native owner (November11)

Two real weekly popups had the same title, native choices and category IDs. Their
screen tokens were also identical, so an old September21 category command selected
the category in September28. Native category navigation alone reproduced this;
no schedule was changed. The calendar command-date guard initially covered the
trigger/day controls, but floating option content is reparented outside DateGrid.

Resolve that content by its actual Pointer to the current native trigger, then
reuse the trigger's bound calendar date for option IDs and immediate validation.
The existing popup signature also includes the same trigger key, even during
content loading. The original single section, native controls, choices, Close,
keyboard and focus remain. No per-caption exception or second popup component.

Plugin945f129f passed three real groups/four clean axe: equal choices but distinct
IDs/tokens, old-command refusal with old AND current tokens, current category
selection/inverse and exact second-week Close/five-week restoration. Shared
regression continues separately; this is matrix F058, not whole-screen parity.


## Recycled native calendar command ownership (November11)

Actual DateGrid reuses the same physical prefix/day cells and paths across months.
An old September21 week handle activated October19 on051da34e. This was reproduced
without changing a schedule. Native APIs show m_boundPrefix records contain
WeekStartDate and m_boundDays contain Date, both real FM.GamePlugin.GameDate values.

CalendarCommandIdentity scopes IDs to the native packed full date and validates
it again immediately before commands. Locate the cell by actual Pointer in its
native collection; its index locates a bound value but is not identity. Preserve
ordinary labels/disabled/commands and original same-date focus. Unbound dates
return contextual errors; index-based date-picker grids without dated records
retain their original contract. Ordinary StopHandle and floating TriggerAdapter
share this path, including week Copy/Paste/Clear. No caption/date parsing, native
setter, guessed offset, permanent assembly change or retry was introduced.

Current14d28b5b real pre-render and post-render ownership regression passed4groups/
3clean axe, with fresh October controls and exact September restoration. Retained
before evidence and broader shared checks are in Testing/matrix F056.


## Native calendar captions and first-arrival focus (November11)

The current DateGrid calendar's localized weekday captions live in DayNames above
nested component wrappers. Preserve internal Monday..Sunday column keys, but pass
the seven actual visible native captions into the inherited CalendarScheduleHtml
builder. Its original signature remains valid; partial missing captions fall back
to the original key and mismatched counts fail instead of shifting data. This
Navigation assembly change was cold installed through the owned receipt.

SIFloatingPopupWidget already uses the original single native popup section and
CloseAdapter. Mark that existing section as a covering native overlay, recognizing
its real Close so no synthetic Back or background leaks into reading. On first
unremembered entry, the inherited dialog focus path reaches its actual title.

A picker can close one frame before a new full-screen native editor arrives.
Retain the existing explicit native-forward history intent across provisional
shells; when actual content arrives and focus is still BODY, focus its existing
content heading. User input cancels the intent; dialogs, focused controls and
backward/history restoration keep their own rules. No timeout, new framework or
screen-caption allowlist was added. Current051da34e real-game regression is in
Testing. Native same-destination Training routing remains a separate investigation.


## Native select values and delayed body responses (November11)

A real loan Undo restored native index0 and sent the localized original value
through SSE. The browser applied it, then a body response22ms later morphed the
old non-negotiable option back. The independently read native state was already
restored. This is a presentation ordering defect, not a failed native Undo.

Extend the existing NativeValueRevision/checked-input preservation contract to
closed native state-tracked selects. Stamp the actual state read, carry the
revision through the inherited fresh-node sync/cancel path and preserve a newer
option during morph or hard replacement. Older SSE also cannot replace a newer
body value. An unchanged SSE caption still advances its observation revision.
Do not touch a loaded option session, native disabled state, labels or handlers.
Swept/custom value compositions retain their own contracts; do not stamp their
render time as though it were their earlier native read. No command retry or
additional refresh is required. Real-game qualification is recorded in TESTING.

## Paused historical selection and played clock (November11)

Current0.9.2435 can retain the last played scorebug clock while a different
historical position is selected. Independent native reads confirmed that both
SISlider and its match.MatchCurrentTimeSlice binding changed; invoking the actual
native UpdateValue operation once also left the displayed clock unchanged while
paused. The earlier assumption that this clock always previewed the selection
was incorrect. Do not change the native seek mechanism to satisfy that assumption.

Keep the original range/step/setter and native TimelineSeek/resume contract.
Represent its actual selected position as a percentage of its own native range,
separately naming the game's displayed played clock. This does not convert raw
time slices to official minutes. The existing values stream updates visible
text/aria-valuetext together; Alt+I explains the distinction without DOM insertion.
The repeated native test selects Home, observes the real0–0 lead-in on resume,
then the first played goal, selects End and observes fulltime/Portal return.
Official incident minutes/scorers/assists still come from native event records.

## Fixture header artwork and renamed selector values (November11)

The full-screen CalendarTool fixture view reuses the licensed backdrop outside
the previously covered tile/FixtureBackground ancestors. Native inspection and
a screenshot identified MiddleLayer as competition background artwork between
SIStyleSetter BottomLayer and SIGradient TopLayer, under the Licensed branch of
backgroundsetter. Extend the existing shared image classifier with that actual
painted-layer schema. Keep the date, competition, weather, venue, entity links
and fixture tables; do not hide arbitrary MiddleLayer images or whole headers.
The unchanged FM26 LayoutWrapper traversal serves both HTML and speech.

The same view exposed a separate inherited dropdown inconsistency: the profile
names its selector Season, replacing the native value-based name, while state
remained only1 of1. The actual visible caption was Época2026/27 (with the game's
own spacing), and native enabledInHierarchy was false. Reuse the original FM26
ComboboxAdapter labelled-value rule in ordinary DropdownAdapter: an explicit
profile field name, like a native row label, preserves the selected native text
as state. Menu titles and specialized positional adapters retain their existing
contracts. No year/choice/value is invented and native-disabled remains disabled.
Current runtime evidence and exact information-preservation checks: TESTING.md.

## Asynchronous conditional text ownership (November11 UTC)

The medical severity panels can switch between same-shaped conditional branches
after the selected tab updates. The inherited name/child-count signature can
therefore retain an old TextAdapter: its text becomes empty while another native
SIText displays the actual result. Lifetime observation alone did not fix this;
a repeated real-game run failed at the nineteenth transition.

Extend the existing text-readiness shape record with the actual native Pointer
for non-streamed text. An identity replacement now invalidates the captured
sources; ordinary text changes still use LiveValues. StreamedTable descendants
retain their separate record/realization validation and do not treat harvesting
recycling as new text identity. No caption allowlist, translated zero-state,
forced refresh, new component or action replay is introduced. Keep the actual
medical foreground observed while its asynchronous results bind. See TESTING.md
for repeated empty/populated-state evidence and remaining limits.

## Speech transport failure remains readable (November11 UTC)

FM26's primary path remains Alt+I/describe → speak → native SpeechPipeline/Prism.
An actual current-WebView2 connection failure showed that logging alone leaves
lost speech feedback unavailable to a blind player. Extend the existing dynamic
paragraph/persistence-notice pattern with one error-only role=alert paragraph.
It contains only the mod's generic English-base uncertainty message, never the
requested description/value. There is no initial empty status region, repeated
alert mutation, new control, focus move, commentary-region replacement or replay.

Only the latest speech generation in the same native screen may show/clear its
notice. Successful acknowledgement removes it; actual screen changes invalidate
pending generations and remove the old notice, including instant native menu/
message stash returns. A lost response does not prove speech was unspoken.
The error says it could not be confirmed, not that a native operation failed.
Keyboard and normal reading contracts are unchanged. Tests and their explicit
controlled-fault/native-pipeline boundaries are in TESTING.md.

## Cold native UI readiness (November11 UTC)

The loading manager can close while only a splash/background panel is bound.
Keep input initialization separate from developer UI diagnostics. Defer those
readonly probes until the actual foreground contains a visible native SIButton
and nonempty TextElement; retain the bounded startup budget and report Not run
if readiness never arrives. Do not report absent transient UI as a failed native
type/text API. FM27 input uses the actual InputAction registry/bridge, rather than
the removed FM26 OnNavigationMove method. This registry probe does not claim key
interception; functional keyboard tests remain separate.

The cold LoadGameBrowserFullscreen shell can precede its Local/Cloud selector,
native space status and empty/list content by more than the navigation settle
window. Extend the inherited self-updating-panel shape watch to that actual
native panel. Reuse the existing text-readiness/visibility signatures and local
SSE restructure/value updates. No selector/caption/options are invented and no
forced refresh or fixed focus delay is added. Cold validation is recorded in
TESTING.md; warm reopening alone does not establish first-open coverage.

## Loader restoration excludes bundled development mods (November 10)

The local loader source had retained an old Smoke example; copying that source
wholesale silently reinstalled a second BepInEx plugin. Stage only the loader's
runtime files, excluding both BepInEx/plugins and BepInEx/patchers subtrees.
Those are separately owned mods, not loader dependencies. Preserve their source
files and existing game installations; receipts for the loader and production
Host/Plugin remain separate. Do not remove unknown user mods to make the runtime
look clean. Tests cover actual install/removal around an independent mod, source/
runtime byte preservation and recursive/collision refusal. The clean restored
native startup discovers exactly one production Host; no claim that the inert
Smoke example caused the previous native activation wait is made.

## Xbox0.9.2435 channel API and deployment qualification (November 10)

The new generated InteropDataHandler.m_channels return type is InteropChannels;
its m_open dictionary stores actual open keys and native Route tracks generation
for stale replies. The earlier profile exposed the dictionary directly. Read
membership through those actual managed properties without a compiled dependency
on the newer return type, arbitrary pointer casts, routing changes or provider
recreation. Native close/open for existing form-value subscriptions remains the
same. Missing collection APIs must be logged as contextual failures.

After an Xbox update, rediscover registered identity/executable and inspect the
new exact native/metadata pair and generated interop. Build against that interop;
unchanged Host source may still produce a different binary and requires a cold
receipt-aware installation. An investigated deployment profile may explicitly be
in live qualification; it must never imply inherited live coverage. New mixed
version/hash combinations remain refused. See TESTING.md for the current limits.

## Native promise records and retained readonly enum values (November 10)

Keep the original field/dropdown/negotiability HTML and handlers. The actual
promise dropdown serializes its enum choices inside single-field DynamicReference
records named RowData. Unwrap that exact demonstrated envelope in the existing
row-caption resolver; do not change global numeric conversion or derive an enum
from the ordinal. Its second list entry is enum2, not enum1. An ordinary labelled
icon field with empty realized text can use the same localized native row value;
bare action menus and the specialized FM26 negotiability ordinal remain unchanged.

Finalized ContractOfferTool lock selectors are disabled and clear choices/index,
but keep the actual bound SelectedValue enum. Resolve that value through the
same strict row asset/DataCollection/TranslationID contract only when disabled
and empty. Do not open disabled popups, retain a guessed previous caption, invent
selection for an active index-1 menu, or enable native unavailable controls.
Actual promise0/2 and seven finalized lock0 values were inspected on Xbox PC;
see TESTING.md for native evidence and the distinct manual/failed live coverage.

## Native full-screen Exit uses parent cursor memory (November 10)

Native Sair/Cancel can remove a full-screen tool's history entry without going
through the companion Back button. On FM27 the returned parent's BindingRoot
can differ, so per-token memory alone loses its cursor. Retain a bounded hint
for the current button action; when actual native history returns to a previously
observed parent slot, reuse the existing exact-parent pending handoff. Wait for
its actual panel/control if a provisional category shell arrives first. Clear the
hint on new input, forward navigation, native loading or expiry. Do not create a
new navigator or write/replay native history commands. Explicit Back/Forward and
their original cancellation remain authoritative. Actual contract refusal/Exit
and held-real-body Tab tests are recorded in TESTING.md.

## Initial negotiability values without popup realization (November 10)

The original NegotiabilityDropdownAdapter's synchronous Show/Read/Hide cannot
reliably read FM27's delayed popup rows. Reuse StaticButtonCaption's actual
SIDropdown row enum/DataCollection localization before that fallback. The inspected
collection-text function also serializes seven inputs, omitting its explicit
translation flag: accept only that same function identity, exact native row path,
empty formatting arguments and a TranslationID collection result. Preserve the
existing eight-input explicit-true flag check and all other schema guards.
This is distinct from the three/four-argument localized/gender functions; never
strip gender tokens or ignore formatting arguments to force a caption.

Read the actual chosen enum value from m_choices at the native SelectedIndex,
translate its actual collection TranslationID on each read and retain the FM26
caption/index presentation. Cache native assets, not localized strings. Disabled
controls retain their current native value without opening a popup. Verify initial
states, all choices, native index/write-back, Undo/Revert, exact return and absence
of native background popups; never substitute a hardcoded negotiability list.

## Shared recruitment budget summary rows (November 10)

The RecruitmentBudgets grid component has an additional FM27 summary variant:
three native rows each contain caption/availability in one branch and amount in
the other, with optional nested binding wrappers. It does not use the separate
AvailableText/SpendingText names of the existing detail tiles. Extend the existing
BudgetTileReadout and JoinedTextItem pattern for that demonstrated component,
checking actual Pointer ancestry, two visible branches and exactly2+1 text parts.
Keep native order as Caption: Availability Amount and all source IDs for SSE.
Never pair across rows/cards, borrow a neighbouring amount, parse monetary text
to infer business values, or flatten interactive/context-menu content. Retain
the heading's native button, customization and informative warnings. Existing
detail-tile and caption-below-total variants keep their contracts.

## Observed native combobox adjustments (November 10)

FM27 money/value commands update their caption on a following frame. Preserve
FM26 ComboboxHandler's direction availability and changed-value contract in the
companion: use actual SIEnabled/SIButton enabledInHierarchy, the common
ButtonAdapter command path and existing select/arrow HTML. Disable only the
native-unavailable direction, removing its handler; preserve the other direction
and preset menu. Never infer a financial bound from displayed currency text.

/step validates direction before sending, performs one native operation, then
reads the same validated native widget/owner until its displayed value changes.
Its reply must be that actual new value. An unchanged result after the bounded
observation is explicitly unconfirmed; never replay or report its old caption
as successful adjustment. A native-unavailable direction returns the original
FM26 minimum/maximum feedback without a command. Preserve native clamps, offer
draft lifecycle, Revert and original monetary units. See TESTING.md for actual
0→1€, exact inverse,349M€ ceiling and unsent proposal cancellation evidence.

## Complete loopback request before native dispatch (November 10)

The inherited raw server could execute a partial declared payload after EOF.
Reuse its one-request-per-connection transport and route table, but require a
complete header terminator and exact byte-length body before authorization or
route handling. HttpRequestReader is a pure framing helper compiled into tests;
it preserves actual UTF-8 values and rejects truncation, conflicting lengths,
invalid UTF-8 and unsupported transfer codings explicitly. Headers are bounded
at64KiB and bodies at4MiB; actual companion requests have known byte lengths.
No keepalive, command retries, dispatcher/native changes or alternate server.

This follows the incomplete-message requirements of
[RFC9112 sections6.3/8](https://www.rfc-editor.org/rfc/rfc9112.html#section-6.3).
It does not identify or fix the separately observed pre-HTTP TCP10060 condition.
Actual before/after diagnostic evidence and limits are recorded in TESTING.md.

## Provisional outgoing action focus (November 10)

Native profile navigation can first deliver another outgoing Squad body with
the old token/history slot but without the activating row. The original nearby
fallback must not cross into global navigation or become the remembered player
origin. Resolve the existing flat renderer's native section heading, scope
fallback candidates to that section and distinguish automatic fallback from
new user input. Keep its real origin in per-screen/history memory until an actual
body owner change or user keyboard/pointer input. An SSE identity observation
alone does not mean the outgoing DOM has already been replaced.

This extends the FM26 commit/restore and local action-focus contracts without
new UI, fabricated player values, delayed command replay or a navigation system.
Retain the separate Back handoff until its actual target arrives; user input
still cancels it. See the retained real failure and targeted tests in TESTING.md.

## Native table permission and scouting-state variants (November 10)

Honor the existing TableCellElement.IsDisabled for all native cell controls, as
already done for dropdowns. Keep current text/checked/selected values with native
disabled HTML, omit their action handles/handlers and retain useful context-menu
metadata. Recheck native availability at dispatch, since an older client can
retain a formerly enabled handle. Ordinary virtualized cells must first complete
their existing binding/layout settlement: a recycled widget can briefly retain
the previous occupant's disabled flag. Preserve index-based disclosure selection
and wait for native pending-cell/stable readiness before its permission check.
Merged native/virtual history keeps its own existing history path.

The database's virtualized-item can itself own the Cell/SelectionCell children.
Preserve that row before descending its first child; the latter is a selection
checkbox, not a row wrapper. Reuse TableContext's actual column/header mapping.

Projected efficacy uses projected-te-gauge-stepper templates with native Range
fill mode, currentValue=-1 and authoritative currentRangeMin/Max. Read that range
on the same0..100 scale, retaining native enabled/read-only behavior. The native
unscouted template selects its neutral first pip (SelectedOnly/current0/ranges-1)
with a tooltip explicitly requesting scouting. Present the game's localized
Unknown caption (runtime TranslationID1900 in the inspected0.9.2409.0 build),
not a fictitious measured zero. Never read hidden player ability to fill the gap.
Treat an invalid/nonfinite native range or missing translation as a mapping error.

The tooltip's four-argument localized text function includes gender formatting:
raw TranslationID43992 carries male#1, while native rendering correctly says esta
jogadora in this context. Do not parse/strip those tokens or broaden the shared
constant-caption reader to ignore formatting arguments. These fixes do not alter
StaticButtonCaption or inject tooltip descriptions into ordinary DOM labels.

## Native tactical effectiveness gauge semantics (November 10)

The FM27 training summary uses five enabled SIButtons containing SIStepper
te-gauge-stepper templates. They open the real detail card. In the detail card,
the same template's navigation buttons are natively disabled and represent
read-only values. Preserve that native distinction in StepperAdapter instead of
rendering every instance as an unavailable button. Read the actual current value
and 0..100 step limits; nonuniform visual thresholds are not a count of filled pips.

Reuse FieldLabelReader for actual localized captions. The overall detail gauge
places its caption below unary binding wrappers: accept only its first exact
visible two-child text/value pair, without broadening ordinary field labels to
borrow arbitrary following text. Retain real headings and explanatory paragraphs.
Radial gauges in the 17 factor rows and55 individual player/value pairs retain
their actual values/scales; an uncaptioned value cell need not repeat the technical
Gauge fallback. Meaningful paired captions elsewhere remain intact. Keep the
original semantic tables, native commands, keyboard and history focus contracts.

In Tactics, the same gauge also appears inside actual StreamedTable player cells
with no local text. TableContext.ReadColumnLabel reuses the native realized-row,
cell and header mapping as the last caption source. Verify matching column counts
and the actual containing cell; never hardcode an index or borrow adjacent player
text. Native empty bench slots retain no gauge or fabricated player value.

Native Back can recreate the card with its local display variable reset, removing
the previous player row. Keep that native state. If the remembered history page
matches but the old control is absent, focus its existing content heading instead
of global Back. Keep the pending exact target for later native binding, while user
input cancels the handoff. Do not replay arbitrary checkboxes or invent persistent
game preferences to restore a visual context.

## Readable mod preference save failures (November 10)

Retain the actual session mix when sound persistence fails, but never claim it
was saved. Store the real failure notice alongside current settings, clear it
only after a successful complete write, and show it through the original NoteItem
paragraph with an optional escaped id. Keep normal notes byte-for-byte unchanged
when no id is supplied. Update that one paragraph locally while preserving the
slider focus. It is conditional plain text, not an empty region or second live
announcement; the original SpeechPipeline request announces once. Reopening/body
refresh must recover the same backend notice. Preserve the original volume/help,
preview and native close/resume contracts. Actual filesystem regression uses a
bounded readonly sharing lock, never ACL/security changes or a fabricated failure.
Capture fresh speech/log cursors before the edit to prove NEW feedback.


## Native edit-history value subscriptions (November 10)

FM27 can complete FmxRevert/Undo/Redo without updating existing Person/value
channels. Resolve actual native form keys and renew only their active
InteropDataHandler subscriptions with the unchanged key, actual parent reference
and native property name/ID. Do not reopen session/list/provider channels: they
own edit history. No Node.Reset/NotifyDataChanged, global panel reload, invented
reference or business-value write. Guard the current foreground/binding scope,
expire stale requests, and perform native work on a following Unity frame.
Revert waits for its native clean-state footer; Undo/Redo cannot rely on button
edges when both remain enabled. Their value reads follow the command through the
same FMX request pipeline. Verify every actual result rather than treating the
request as success. Clear pending observations on shutdown/reload.

Native checkbox restoration can arrive after the original action refresh.
Extend the existing LiveValues/values channel with checked state and update the
existing HTML input.checked, without firing another action or moving focus.
Keep native controls, labels, radio contracts and original HTML structures.

A single-client native regression proved that HTTP delivery can lag a newer
checked-value event: eventTrue at1794336762093, older body morphFalse at
1794336762107. Stamp the actual native reads, not response arrival. Checked
input markup and the existing values event carry ordered, exactly representable
boot-clock revisions. Ignore older checked events and preserve a newer checked
value when morphing/replacing older markup. Strip reserved event metadata before
ID processing. Do not change labels, force focus, replay a command or add a live
region. This correction deliberately covers checked controls; no other widget
ordering coverage is inferred. Delayed-delivery tests use retained actual native
markup and restore current footer availability before further native commands.

## Native match editor ownership (November 10)

A plain OpenPanelEventContext can expose a tactical editor whose footer emits
MatchCloseTacticalPanelEventContext without entering FMMatchUIComponent's edit
session. Preserve native transaction semantics: after the original click, on a
later Unity frame, resolve the actual asset with PanelManager.TryGetPanelID and
verify the foreground's typed close command before OpenTacticalChangesPanel.
AssetReference.Asset can remain null despite the manager's resolved resource;
the later-frame handoff verifies the actual foreground resource. Use the original asset,
native IsTacticsPanel and m_inPosession. Never write pending/owner/session fields
or bare-close a panel with uncommitted edits. Clear stale requests on match change,
expiry and reload. Log a failed handoff without starving the dispatcher.

When a native answer closes both its dialog and parent editor, retain the normal
screen-focus restoration already made to the destination's remembered opener.
The missing immediate dialog origin alone must not override that valid return
with a heading. Existing title focus remains for genuine arrivals without it.

## Official feature discovery cross-check (November 10, 2026)

The official [navigation announcement](https://community.sports-interactive.com/forums/topic/611875-fm27-clearer-interface-smoother-navigation/)
and [UI FAQ](https://community.sports-interactive.com/forums/topic/611878-fm27-uiux-faq/)
describe matching-size tile replacement, configurable initial destinations,
editable shortcuts, reordered bookmarks and more full-screen navigation. Treat
these as discovery requirements; the installed native controls determine actual
availability/options. Do not implement a fixed invented tile menu from an article.

The [official training feature notes](https://www.footballmanager.com/fm27/features/fm27-training-and-tactical-effectiveness)
connect understanding, ability, player support and opponent preparation with
instruction complexity and the weekly plan. Read every native dimension and
available plan, retaining the FM26 accessible table/calendar contracts. Do not
restore the removed training-unit model merely because FM26 had it. These
articles provide scope, not proof that any companion action works.

MentoringDiagram retains the FM26 matrix/legend/influence contract. FM27 nests
its actual player-name link beyond the original shallow text walk. Reuse the
shared entity-name resolver, exclude that exact name control by Pointer from
personality text, and preserve actual SITextLink actions with existing RowActions.
Never use one ambiguous personality string as a person's name. A hierarchy badge
with its complete adjacent official caption is redundant; suppress its glyph
only within that actual visible two-child pair. Keep informative icon-only badges.

The shared switch-label wrapper can contain either a constant official caption
or conditional On/Off text. On the actual advice customization card the constant
runtime TranslationID binding must win over the enclosing group title. Reuse the
existing static localized-text recognizer, including its runtime binding when the
instantiated asset lacks serialized data. Conditional functions do not qualify;
retain their external field-label resolution. SICheckBox.Value is distinct from
inherited Toggle.value; diagnostics of checked state must use the latter.

Baseline: FM26 Access 0.2.16, commit 4d38a0256f29b97376845ef59edfcd64d7f165ed.
The pristine local comparison checkout is .upstream/fm26 (ignored by Git).
Run node scripts/compare-fm26-source.cjs to produce an English source comparison
in artifacts/analysis. Textual equality establishes reuse, not runtime coverage.

## Same-screen native input disclosure focus (November 10)

Reuse the original text-field HTML, settext endpoint and native filter. FM27's
message search reveals a BindableTextEditBox inside MessageFilterVisible without
native focus. Extend the shared action-focus record with existing editable IDs:
a sole new field while the same-screen button survives is the arrival target.
Observe actual refreshes, not a focus delay. Never steal a subsequent user Tab or
pointer choice. Retain the opener for actual Close even after cancelled autofocus.
Exclude native overlays so their title/initial-focus contracts remain authoritative.
Do not introduce per-caption/screen allowlists or a second client-side filter.

## High-speed match audio and native fixture artwork (November 10)

The original SSE/one-shot architecture is retained. FM27's actual active live/past
MatchSpeedDuringHighlights can be 10, above the inherited 4x rate ceiling.
Timestamp the native snapshot with Unity Time.unscaledTimeAsDouble, which is
fixed for the observed frame, rather than a socket-thread enqueue clock.
At speeds above four, a short batch cannot contain a complete unsampled gap
when it is shorter than actual GetLiveHighlightModeData().MinimumBetweenHighlightLengthSeconds.
That native value was four seconds (16 engine slices). Preserve the ordinary
4x floor, the independent native fast-forward gate and all event high-waters.
Never generate goals from a changed score or replay a skipped event later.

InHighlightTimes was 0..24660 for a goals-only historical replay; it did NOT
identify the displayed goal clip. m_keyHighlightTimes lagged a clip and still
muted the fifth goal. Both clip-bypass experiments were rejected and removed.
Two actual five-goal x10 replays passed after the minimum-gap adaptation, with
all five gap transitions suppressed and zero muted goals/JS/request failures.
An additional x1 five-goal replay passed; live/past speeds and original viewing
preferences were restored through native UI and verified after reopening. This
is not full match-mode parity.

The licensed FixtureBackground has decorative MiddleLayer artwork even in inbox
attachments outside calendar tiles. Extend the existing shared exclusion only
for that actual native component; retain fixture/club/competition text and actions.
No game assets are redistributed. The actual September 22 UEFA invitation
passed after-fix content/focus/axe checks; details are in Testing/session progress.

## Native covering cards, dates and editable searches

Advice table consolidation must recover the common native FoldedTexts field
caption before the column override replaces the dropdown's accessible name.
Otherwise the original suppression pass removes a caption from both its old
text position and its final row header; a remaining description can also become
the title. Preserve the original table and description column, without field-name
allowlists or additional translation. Four actual frequency fields were verified.

FM27 tab-row statistics can bind the first numeric text after its initial walk.
The original renderer's first-value-only emission then suppresses every walked
caption/other number, even when native counts are ready at render. Emit once per
native strip Pointer at the first available pair stop and reset that set in the
existing BeginBody. Track captions as well as values so late binding stays local;
retain the original single-line order and meaning.

Root dropdowns can disappear after becoming visible but before their choice rows
bind (observed active=0, choicesCount=10, popup=False with a live nonzero anchor).
The original bounded read loop now revalidates source screen and resolves the
same current handle before reopening its root. Never replay a selection or replace
a vanished child submenu with its root's options. The latter is an explicit error.

Popup starts must await an already-running companion body compose. FM27's native
harvesting scrolls virtualized anchors across frames, and the original BeginBody
tears down popup sessions at render time. Allowing an open to overlap that pass
caused both an unavailable responsibility and an opened-then-closed dropdown.
Core retains the body promise and awaits it for popup-start endpoints; existing
pending/dd state prevents subsequent same-screen refreshes. This is event-driven,
without an arbitrary delay or native command replay. Ordinary dropdown Open also
validates the captured screen, as searchable dropdowns already do.

FM27 sorted headers expose live state through StreamedTable.TryFindSortedColumn.
Match the native m_headerLookup button by Pointer and use its ColumnID. Native
SortedColumn retains the configured default during other sorts, and indicator
sprites can be hidden after reopening or use different directions from FM26.
Preserve the original header HTML/state contract and retain icon fallback only
when no actual table/controller match can be obtained. Real surname ascending,
descending and reopened selected-position sorting were checked independently.

The inherited history focus handoff also needs to survive a provisional Back
body that lacks the remembered element. Do not consume the destination until it
exists in the replacement DOM or overwrite its position with the transition's
surviving Back button. Keep the original bounded deadline and input cancellation;
later updates must respect a new user focus. Controlled ID absence on a real
native Back reproduced the failure without changing native data or commands.
If that provisional body also loses its transitional focus, a later neutral
refresh must complete the already-requested history restoration. The ordinary
neutral-refresh rule still initiates no unrelated focus. A controlled blur to
BODY without new input reproduced the previously consumed-but-unfocused handoff.

Reference-bound StreamedTable rows require record identity, not their current
display index. Actual surname sorting reused Misa's old command ID for Taylor
Hinds and opened Taylor's native report. The virtualized Items were integer
model keys; the real row BindingRoot's `binding` held the PersonReference.
TableContext now reads that actual InteropReference type/identifier pairs within
the table boundary; the renderer uses them in cell keys, so Misa's control moves
with Misa. Re-realization compares the reference again in the action's main-thread
dispatch, including dropdown/segment/context and selection observation. Deferred
cell actions also retain their rendered screen owner and revalidate ancestry.
Non-reference rows keep the inherited protocol; no caption identifies a record.
Four groups/two clean axe passed, including native unchanged-row selection and
lineup-menu cancellation, exact order/assignment restoration and held-body stale
refusals. Shared table regression continues; every row type is not inferred.

FM27 can replace a medical severity's populated table with native EmptyText inside
the same panel/token. The original provisional retry budget was shared across
successive actions, so three earlier gaps could strand a later provisional body
indefinitely. This was reproduced with matching native tree/screenshot and SSE
events. Each new explicit native action now refunds the existing bounded allowance;
the retry counts/delays and shape/value protocol stay unchanged. Automatic retries
do not rearm themselves. Twenty-five native transitions/fifteen menu cancellations
passed on the first fix. Ordinary native tables also revalidate their ancestry
before rendering, as ordinary stops/attribute lists already do; an outgoing hidden
table emits only the original provisional marker, retaining visible-failure feedback.

Responsibility actions retain the original swept semantic tables and native
virtualized row/dropdown protocol. The actual native Take Control/Undo/Redo/
Reset/Confirm flow can disable or remove the focused control without changing
the owning screen. Shared DOM focus repair records only the acted control,
same-row peers, available neighboring actions and last edited row. It applies
only after that exact action loses focus in the same owner, before component
arrival hooks; it does not move focus on neutral refresh or override later user
focus. Disabled/enabled responsibility slots retain the same HTML ID, with no
handler registered for disabled native controls. Five native regression groups
restored all assignments/totals; Confirm persistence is leave/reopen only.

The same responsibility list, indices and panel token are reused across native
Staff/Scouting categories. Index-only handles demonstrably opened a different
duty's choices after category replacement. Group/duty data now captures each
actual bound InteropReference type and identifier pairs. Slot keys include those
references; realization compares them again before opening/acting, including
before replacement HTML has rebuilt the map. No assignee, translated caption,
guessed offset or per-caption allowlist identifies a duty. With body delivery
held, old commands were refused and all assignments/totals stayed unchanged.
Six groups passed on 96973000. Other category actions and disk persistence remain
separate coverage.

The subsequent opt-in persistence regression saved/loaded two NEW owned QA
checkpoints. A deliberate in-memory inverse was discarded by Load without saving;
the confirmed disk assignment returned. The original person was then confirmed,
saved to the second NEW checkpoint and loaded back with all original totals and
duties. All twenty pre-existing saves and both new files retained exact hashes,
lengths and modification metadata. This verifies that native responsibility and
Save/Load integration, not every category or a game-process restart.

Message titles/senders/times are not identities: three actual medical updates
had identical metadata and distinct native UniqueId values. Read the property's
value from the actual NewsItemReference.m_id pairs, checking the official
PropertyIdentifierSet before resolving a name. Other pairs are context IDs and
need not exist in that property registry. The generated m_uniqueID field was
zero in this build; it must not be mistaken for the record identifier.

The original four-column message table and modal content remain. Native row/view
identity is metadata, never label noise. Handles reject rebound rows and opens
validate their rendered owner. Verification uses actual pane identity plus its
native subject, so an old same-title pane cannot confirm another report's open.
MessageViewHistory remains exactly upstream after namespace rename; its existing
opaque comparison string carries the native ID/title and survives Plugin reload.
Legacy subject-only restoration requires an unambiguous visible row. A compact
preview can restore by known native ID even without a row subject. Missing IDs
use the existing provisional refresh and contextual action failure, not an
unverified successful opening or a falsely disabled control.

An exclusive native covering card retains its original controls/order with one
main landmark/h1, initial title focus and opener return. It is not a modal answer
dialog and does not acquire a Tab trap. FM.dialog tracks actual native identities
as a stack, so closing an overwrite confirmation returns to the Save button in
its parent card. Native loading preserves that ancestry. Identity excludes async
caption resolution; a newly resolved name is not a new form arrival.

DateField retains the original CalendarGrid and DateSelectorSession. It adds the
native common field label, uses the existing dropdown interaction gate and checks
generation/owner before presenting a month. Escape/other commands/new screens
release that gate. Dates and pitch operations validate the rendered screen on
the Unity thread before native execution. Calendar targets derive from the real
controller year list and day availability, skipping empty intervening periods;
per-request repeated availability probes are shared only within that one render.
There is no persistent game-data cache or invented calendar range.

Editable searchable inputs retain the FM26 input/listbox/active-descendant model.
Client native requests are serialized, superseded query results cannot render,
and typing immediately removes obsolete selection indices. Enter flushes a
pending query before a subsequent choice. Opening validates its rendered owner;
SearchDropdownSession otherwise remains exactly upstream. Runtime reuse audit
therefore now reports **six exact/four adapted** central sources, checking the
precise opening-guard delta instead of mislabelling this file REUSE EXACT.

FM27's FMComboboxSearchInput keeps its typed query when the list closes; its
search binding is not the committed selection. Native Blur/EndInput do not
replace that text. Retain the original query-preserving cancel, rather than
inventing a rollback from an old HTML value. Actual native selection updates
both the input value and its value attribute. Failed/lost selection responses
close only their session and observe native state, never replay the selection.

Sound settings retain the authored immediate-session mix behavior. HTTP/status/
complete-body failures now explicitly report unconfirmed persistence; native
filesystem save failures return a session-only explanation instead of `ok`.
Coalesced writes are serialized and stale initial settings loads cannot overwrite
newer local changes. No game audio/visual preference is changed by that contract.

## Empty-lineup timeout and additional native contracts

A native answer dialog shown alone reuses its original section/title/content/
actions inside a labelled modal wrapper. Ordinary cards/HUDs are not modalized.
FM.dialog observes the actual body transition (including action/body before SSE),
focuses its existing heading, contains Tab and returns to the actual opener.
Native profanity validation can interpose a genuine loading-layer panel before
the answer prompt: retain the command's original control/context across that
transition, not the progress screen. Loading-layer identification uses the
existing safe native catalog. Native actions remain unchanged and are not retried.

The section headings already used as programmatic arrival targets now retain
tabindex=-1 in authoritative HTML. Previously a neutral morph removed the JS-only
attribute, blurring the focused heading; negative tabindex adds no Tab stop.
Do not introduce another title or a second live region to compensate.

Compact transfer totals put a native caption below two values. The existing
JoinedTextItem now joins only an exact read-only sibling structure inside a
native card, placing that caption first. The wage-percentage section similarly
keeps its actual current amount, budget, percentage and caption together.
Every constituent retains its own LiveValues identity. Empty text, rich links,
headings, activation and context-menu controls prevent this fold. Other layouts
retain the original stops; no values, seasons, units or financial rules are
inferred. AppendJoinedTextParts defaults to its original order elsewhere.

The inherited TabBarHtml remains unchanged: native tab buttons, aria-selected
and the ordinary activation route. FM.tabs adds focus-only arrows/Home/End
within the focused tab's own tablist, skipping native-disabled/hidden tabs.
Enter/Space retain native activation; game tab reads are not instant, so merely
browsing choices must not submit requests. Modifier chords, text editing,
tables and the inherited Tab/Shift+Tab sequence are unaffected. Reference:
[WAI-ARIA tabs keyboard pattern](https://www.w3.org/WAI/ARIA/apg/patterns/tabs/).

Native ManagersInfoStreamedList rows pair a linked manager identity branch with
the real start, separator and end/Present branch. ManagerHistoryRows extends
the original SyntheticTable/RowActions; SITextLink retains the actual profile
command and context, ListWindowSurface retains paging, and layout/extra-control/
unbound-link failures keep the original list. No new HTML table widget.

Native inline radial captions can follow the gauge as exactly one plain SIText
sibling. RadialGaugeAdapter names only that immediate pair, preserving native
current/min/max. Same-Pointer caption folding is shared by table model, inner
navigation, handle resolution and speech-cell reading; captions with rich links
or other peer controls are not swallowed. Ordinary form labels remain the
fallback, and unrelated rows cannot supply a name. Actual preferred-role table
and native selection/restoration passed in a real linked staff report.

The shared styled-gauge cell can instead pair a single-child gauge style with
the native [styled rating caption, explanation] frame. Only exact read-only
single-child wrappers are followed; the explanation remains a separate stop.
The actual medical table retains Alto 71, Elevado 42/51 and Fraco 22/23 with
their full native scales and reasons, without a generic Gauge label. Rich links,
other controls and unrelated siblings prevent caption adoption.

An ordinary native action can open a menu through its /activate-triggered body
before the SSE screen event. The original menu focus and stash contract must
observe the actual body/DOM transition as well as token events. Otherwise the
new token masks arrival and focus stays outside, disabling menu-key handling.
Reuse wantFirst/menuStash once, preserve subsequent neutral/browsing focus, and
verify arrows/Escape/origin with real screen delivery delayed, not fake menus.

Budget tiles draw AvailableText/AvailableValueText and SpendingText/SpendingValueText
as separate native rows beside SIProgressBar. The ordinary form-label resolver
could adopt SpendingText (or the wage amount), silently folding it away from its
monetary value. BudgetTileReadout recognizes the shared card structure, reuses
the original LabelValueItem and names only these bars from their own tile title.
The native wage tile's 50% fill is distinct from WageBudgetPercentage 86.212517;
preserve both actual values, never substitute a calculated financial ratio.
All other ordinary ProgressBar labels retain the FM26 form resolver.

Progress LiveValues now carry caption/state/display together; app.js updates
aria-label and aria-valuetext alongside textContent. The original span/role and
single values channel remain. Actual native save loading observed multiple
changing values with identical DOM accessible state, followed by Portal.

Native budget conversion recalculates after each commit: returning to an old
slider index did not restore exact amounts. Tests must inspect applied/reopened
money and use an owned checkpoint to restore it, never infer a reversible money
round trip from the index alone. Native commit and exact checkpoint restoration
were both observed; no financial rules are implemented in the companion.

Historical replay's SISlider is bound to a TimelineSeekEventContext whose
selected-time path matches the slider DataLink. It previews a passage and the
game applies that passage on resume. Do not issue an extra MatchSeekEventContext
or infer seconds from native time-slice integers. MatchTimelineInput adapts the
FM26 SliderAdapter: actual BindingExpect caption wins over unrelated commentary
found by ordinary form heuristics; scorebug playbacktime supplies the elapsed
played clock including stoppage. It is not necessarily a paused-selection preview;
the actual selected percentage is identified separately. Original HTML range/keyboard/setter remain,
with actual bounds/step/value/text in the existing LiveValues stream.

Timeline SIButton marker contexts identify their actual bound row. Read its
TimeString (official match minute), incident sprite, Player and Player2 through
the shared verified name requests and FM26 MatchIncidentLabel relationship.
Clip-start MatchTimeSlice is not the official goal minute. Missing cold report
sprites mark the existing Grid/SyntheticTable provisional and retry through the
existing bounded mechanism; do not bake unavailable forever or invent a goal.
Actual replay resume, goal score/markers, cold fulltime recovery and parent
return passed; other event families/modes need their own evidence.

The FM26 SpeakToRenderer still walks/renders ordinary children and response
buttons. FM27's SpeakToMeetingAnswerActionButton and SpeakToOptionalActionButton
reuse the physical button for another question. Only these native command
identities add the visible NPCBubbleFrame question and answer caption. Their
StopHandle validates that context live, even if an old handle object survives
a body rebuild. The frame search is bounded, shared during body registration
and refreshed for validation; IL2CPP identities use Pointer. Ordinary command
identity and value-edit focus remain unchanged.

Native question SIText uses the existing readonly paragraph/JoinedText body
focus pattern. Its normal source adapter serves Alt+I and preserves independently
actionable rich links. The frontend handoff follows observable body/value updates,
survives provisional plain-to-rich replacement and cancels on user navigation.
It adds no heading/landmark or whole-page live region. Actual three-question
postmatch press, stale handles/HTTP ids, menu origin and Alt+I passed; other
conversation controllers/states need independent evidence. See TESTING.md.

Populated Portal/Data Hub radars use FMSpiderChart, distinct from FMPolarChart.
Its SpiderChartSeries.DataPoints contain the official Label and unscaled
StatValue even when thumbnail labels are not drawn. Reuse the chart Grid with
one row per named axis, matching axes across series, native legend where needed
and finite values. Avoid treating NormalizedValue as a performance percentage.
The adjacent HasPerformanceValue component's visible Label already supplies
the complete feedback represented by the thumbs glyph; retain the assessment,
not the redundant image announcement. Axe did not detect the former readonly
unknown-control/ThumbsUp paragraphs; native/DOM inspection remains necessary.

FMScatterChart summary widgets bind their graph through a typed
BindingHandler<FMScatterChart> visual-function BindingBag, not an ancestor
BindingRoot. Read only actual bound requests and require one record with the
expected native graph fields. The existing DataHubScatter/ScatterSummary logic
then applies. Full plot templates also omit BindingRoot: their actual
ChartData.DataPoints[].ChartPoint.m_key and m_element provide the per-point
scope and real CellButton. Summary thumbnails genuinely have no such buttons.
Preserve this native interactivity distinction. Multiple scatter cards must
share the player-name request's native panel-set session, like entity requests;
per-card sessions reset asynchronous answers and leave permanent Player 1/2.
Names still require the returned entity link to match the exact requested
database reference and selected language. Irene/Leah names in simultaneous
cards and actual point→profile→Back focus were observed after these fixes.

The icon-only match-view selector has no SelectionField text. The generic
dropdown sibling fallback incorrectly adopted the whole MatchViewer body.
Its row template's SIText uses native visual function
e281793f1cc6505468b78ca3cc9222da: actual row value, DataCollection asset, column
name and TranslationID. StaticButtonCaption reads that guarded binding only
for a single-text row template with the matching row parameter and no format
arguments; the game's QuantiseAndMap supplies the lookup. This retains official
captions and language changes without a caption/field/screen allowlist. Native
assets are cached; translated strings are not. The three actual modes were
committed and the initial mode restored.

The central viewer's homestats/awaystats Data-Frame containers retain native
club links outside the original SyntheticTables label/value grid. Events mode
uses separate per-team incident lists rather than the report's paired row slots;
the existing Grid/row-action and MatchIncidentLabel patterns retain minute,
event-icon meaning, player and assist relationship. No event is inferred from a
caption or supplied by mock data.

The in-match report uses readonly `attributes_row` with the original two
name/value children and badge-text. AttributeRowAdapter recognizes that guarded
shape and retains the original attribute table, progression and summaries.
CareerStats separates lifetime label/value totals from SeasonYear/Club/
TransferInfo/Apps/Goals rows; the existing native-form table carries all actual
controls, with the final stat header read from the visible native totals (SG
versus Golos). The advice LiveTable likewise uses native TableHeader and
PositionBlock/TeamBlock/CellBlock rich cells.

StreamedTable's native column-footers live outside the virtualised row list.
Read them after row harvesting, reuse the ordinary cell walker, retain binder
readiness, render tfoot in the same display permutation, and keep unexpected
widths as unaligned content with a diagnostic rather than mislabel values.

FMMatchSimulationWidget.m_data supplies current map coordinates, distinct from
average positions. PlayerEntityData.EntityIndex matches the live Player's
EntityIndex; verify ShirtNumber and TeamID before naming rows. IsSubstitute
excludes bench entities. The native Ball point is retained separately. This
extends the original chart-table pattern without replacing game coordinates or
claiming inferred units/orientation. Real paused data yielded 22 players plus
ball; the extended MatchDugout regression checks that populated state.

At 52:19 the direct native xG widget record summed to 0.83–0.84 while its native
footer printed 1.00–0.84. Preserve the validated observation table and identify
both totals explicitly; no cause for the game's difference is assumed. Only
the chart's own binding can establish that ownership without footer agreement.
Ancestor/name-only fallback still requires matching totals, and shot sums must
always match their record total. The second-half regression covers this case.
Player-card roots now handle Escape on their own card/panel, after the inherited
loaded sub-select Escape handler, closing the actual native session and retaining
origin focus. This fixes the post-commit root remaining open indefinitely.

Opened messages retain the native MessageTitle as their one section heading,
identified by its backing Pointer rather than repeated caption matching. The
synthetic section anchor remains stable. This also preserves the native title's
independent SIText embedded links through the existing joined-text/button renderer;
the inherited profile-heading path had flattened them. Sender, time, body,
attachments and native profile rules remain intact. Other rich native headings
use that same inline renderer. No description text is injected into the DOM.

Native confirmations with dialog-description and actual notification action
slots reuse their exact dialog-title adapter too, and require their own native
answer rather than offering a synthetic history Back. Readonly SIText widgets
with EnableEmbeddedLinks=false expose styled entity names as text. Individually
disabled links in an enabled widget retain their native unavailable state;
captured link enablement changes invalidate the source for a fresh read.

The request screen parameter must be enforced. ActionScreen keeps every native
panel/binding/step/modal segment and ignores only numeric names revisions. Check
ownership on Unity's thread immediately before resolving/acting on a handle;
activation still serializes with popup opening through the original session gate.
Id-only developer operations remain supported. Current actions are not replayed.

FM27's readonly kit pitch uses an anonymous numeric leaf in the native kit
template. Its own view scope binds Person; reuse PitchGrid and the verified
native name reader. Never join opponent shirt numbers to the human squad.
Multiple readonly formations require distinct browse/neighbour IDs while the
single/editable FM26 keyboard contract remains. Team News headings recognize
joined rich-text sources as well as ordinary stops, keeping the first name inside
its actual lineup/sub group.

Compact league rows identify their native template with a BindingExpect whose
parent is the instantiated row. Verify all four actual cells and link identity,
then reuse MatchReportTiles.Grid and bounded ExpandForTile. A parent streamed
list must not absorb a nested list from another card/competition. The same native
rows occur in the dugout, press and messages. The xG dugout footer names home and
away values separately; retain the original native observation/printed-total check.

The match removes ContextMenuSubsystem global entity-menu contributors. A styled
SIText may retain LinkHasActions while neither native shortcut nor right-click
can bind a menu. Gate SIText menu promises on the actual provider collection;
component-owned menus remain independent and normal profile links remain active.
Check out-of-match menu restoration separately, not by inferring it from a count.

The collapsed squad strip binds its own playerinfo.Person without a visible name.
Use that DIRECT child, not a recursive first playerinfo, and the shared native name
subscription. Its OpenDescendantDropdownEvent points to a dropdown now wrapped in
one template. Reuse SquadPlayerButtonAdapter/PlayerCard after verifying that own
native target. Anchor the outer card before selecting an id-less inline leaf, so
native profile/instructions return restores an element that still exists.

### Saved management styles and recycled submenu ownership

FM27's saved style editor can retain CoachingStylesSelected/MentalTraitsSelected
while every option's selected binding is false. In the owned QA profile, masks
5 and 12288 corresponded to two coaching and two mental styles. Clicking an
already-selected card through the stale checkbox added a duplicate index to
the native list. Retain FM26 CheckBoxAdapter and its HTML/keyboard/focus contract.
ManagerStyleSelection recognizes actual ToggleCoachingAttributesEventContext or
ToggleMentalAttributesEventContext, resolves their own manager/index binding paths
in that checkbox's native view scope, and reads the typed persona mask. EventList
contains prototypes: its cached m_indexSelected is zero, so it is not a row index.
Read the bound DynamicReference's index using the existing BindingRecords numeric
reader; do not prepare or send events while reading. Before the ordinary native
click, synchronize SICheckBox.Value with that actual selection. The Value setter
was checked not to emit checked/unchecked business events; native count/mask changes
then occur only through SimulateCheckboxClick. Ordinary checkboxes retain their
original path. No caption, panel ID, style list or business-rule replica is added.

Long hierarchical dropdowns can keep several recycled children visible at once.
Resolve the requested absolute choice's realized row for both native opening and
child adoption, rather than taking the first visible pool entry. Close sibling
hierarchies through their existing native lifecycle. Pass the requested index in
both DropdownSession and the inherited speech handler. Original dropdown HTML,
harvesting, hierarchy, left/cancel and focus contracts remain unchanged. The
228-country regression committed Harare and restored Porto Alegre from the real
native full list without saving the profile.

The shared fixed-caption reader only accepts actual SIText and a non-null UXML
asset. A runtime TemplateContainer has no constant text asset; probing it as text
had broken the Load Game location label. Its actual Local/Cloud selector was
rechecked after the guard. Readonly breadcrumbs use the same ServerText.GetTitle
as the rendered page to avoid the biography's duplicate heading; PanelID.Title
alone was empty on that native edit panel. Appearance notes remain limited to
the inherited ManagerCreationScreen, not non-graphical persona forms.

FM27's report-header PluginContextMenuBindings can have unnamed icon children.
Use its actual LeftMouse binding and own visible caption, outside an SIButton,
as well as the inherited chevron pattern. The own-coach and player Actions
menus now retain real menu ownership, Right/Left navigation and Escape origin.
The compact country flag also takes the linked caption of its own two-part
row rather than a generic Nationality fallback. Native commands remain intact.

A native SIMenu is a sealed VisualElement, not SIDropdown. The actual appearance
BodyMenu exposed an unsafe pointer reinterpretation in ShowDropdown and the
retained native/managed crash stack identified that exact path. GetDropdown
now requires TryCast<SIDropdown>, throwing a contextual error before native
entry for any other type. Actual SIMenu child traversal is separate. An isolated
real SIMenu rejection passed after restart; this does not prove all SIMenu
actions, and graphical appearance work was subsequently excluded by the user.

Unnamed SelectionField SIText can still be a fixed native menu caption. Resolve
its own constant localized-text visual function/TranslationID through the same
reader as omitted button captions; do not treat an ordinary selected-value
binding as a label. The medical Actions menu had incorrectly borrowed Nome
from the table. Its true Acções caption now also supplies IsActionMenu, keeping
native menu commands separate from value edits. Native options and cancellation
were checked. Radial gauges retain full native values/ranges without repeating
their implementation role in plain-text announcements; no values were hidden.

The empty Arsenal QA tactics list contains 26 native selection slots followed
by 24 available players. Empty-slot morale binders can remain pending forever.
The inherited batch pre-settle applied only to the initial realized window;
after scrolling back from the players, the reader settled each empty row
separately and exhausted its eight-second budget at 22–23 rows. Extend that
same shared settle to every newly realized window, including the requested row.
Keep PageRows = 50, across-frame slices, native pending counts, context-menu
guards and schema validation. Never exclude empty positions or invent values
to make the table finish. Live repeated reads now contain all 50 rows; a real
Misa selection and targeted removal were verified in the companion and game.

Icon-only phase tabs can omit their text branch from the realized hierarchy.
StaticButtonCaption reads the button's own BindableSwitchElement UXML branch
and actual constant TranslationID argument of the native localized-text visual
function. It accepts only that typed native contract and one candidate, with
no caption/translation-ID table. Compact Panorâmico, Com Posse de Bola and Sem
Posse de Bola now use the same game captions as their expanded representation.
The SI.VisualScripting interop reference is compile-only, not redistributed.

Readonly current-snapshot sources now participate in /describe without being
registered as commands. A message body's original SIText source has a real
description handle; static link parts validate current identity even without
an activation command. Alt+I keeps the original /describe → /speak pipeline and
does not insert help into the DOM. Both BindTextEditBox.IsPassword and plain
TextField.isPasswordField protect descriptions and write confirmations.

HEAD / establishes the same Host-owned session cookie without composing a
Unity screen. Receipt-aware reload uses that handshake; older cores returning
405 retain a bounded GET compatibility fallback. It does not relax action or
body deadlines. Unmount stops the old dispatcher before dropping its tick,
preventing future requests from queuing against an abandoned Plugin instance.

## November 9 continuation: compact shirts, cards and response failures

- The live TacticalPlannerTool still exposed eleven number-only formation cells.
  FM26's PitchNameLookup expected a descendant BindingRoot and demoted name
  buttons in squad rows; FM27's compact shirts have neither. FMTacticsShirt's
  resolved m_playerIndexKey points to Position.PlayerIndex, whose same native
  parent also contains a typed Player PersonReference. Request that person's
  official name through the existing PlayerNameRequests verification/language/
  lifecycle path. Keep the inherited row join as a fallback and the original
  PitchFormation, spatial arrows and renderer. ReadOccupants shares that reader
  for move confirmation instead of maintaining a second name-only traversal.
  Eleven actual full names and shirt numbers, including the goalkeeper, were
  read on the QA career; the upward arrow reached the corresponding defender.
- Card-frame is an SIImage containing the shared Border/Header/GridSafeArea
  layout. HTML already traversed it but speech announced Card-frame, image on
  arrival. Classify that decorative native container as a layout wrapper in the
  common bridge, preserving every child and all informative images. A native
  classify dump and subsequent speech arrival confirmed the correction.
- BookmarksText (+2) accompanies three displayed native bookmarks, while the
  real selected list contains five. Retain the actual text with an explicit
  Additional native bookmarks label. Explain that the inherited Edit bookmarks
  helper changes separate companion Ctrl shortcuts; Alt+I uses the unchanged
  describe/speak pipeline. Do not treat the overflow as new Portal data.
- The actual swap-tile-dropdown supplies 26 alternatives for the Data Hub slot.
  Name it Customize card with its own native title and provide its purpose in
  GetDescription. Mark that native widget in the existing select renderer;
  after a swap use normal dropdown teardown and screen-position memory without
  the ordinary commit blur. Copy that marker when rebuilding a closed select.
  The medical tile replaced Data Hub in the game, retained its identity/focus,
  survived reopening and a Plugin reload, and was restored to its original
  native default. Preserve ordinary dropdown behavior elsewhere. The fixed
  agenda has no invented customization control.
- Generic action requests previously rejected without contextual feedback on
  lost transport. A shared complete-response deadline checks HTTP status and
  catches failure for the existing activation/value/history helpers, without
  replaying commands. Query values are excluded from failure diagnostics, and
  late failures do not announce over a different native screen. Re-read actual
  control state rather than treating a browser change as native success.
  Keep the body-refresh deadline active until r.text completes, not only until
  headers arrive. The diagnostic DOM mirror now permits one bounded request
  and coalesces changes while it is pending. Controlled blocked, executed-but-
  lost and HTTP-failure checks exercise recovery separately from native success.
  The underlying intermittent connection cause remains unconfirmed.
- Native loading layers can have no readable children before their progress
  binding arrives. Keep their existing Loading heading and omit only the false
  empty-panel error in that state, using the common catalog's actual layer
  membership. Ordinary empty panels retain their original feedback. Historical
  playback captured both its native warmup text and its empty exit-loading phase.
- Extend the existing 24-by-24-pixel button minimum to selects. The populated
  tactics table exposed sixteen 22-by-19-pixel player selectors; the same
  original controls then passed the WCAG-tagged axe target-size check.

These are demonstrated boundary adaptations, not a completion claim for all
career states. Current checks and remaining limits are in TESTING.md and
KNOWN_ISSUES.md; ignored artifacts contain the native evidence.

## Source of truth

The original FM27 game determines screens, order, grouping, labels, translations,
assets, state, commands and business rules. FM26 Access determines reusable code,
architecture, semantic HTML and accessible interaction contracts. Investigate the
FM26 implementation and docs before adding any adapter. Choose exact reuse first,
then a native compatibility adapter, then an extension of the existing pattern.
Create a new component only when the native difference requires it.

Do not invent game controls or hide existing information to simplify a screen.
Accessibility-only controls inherited from FM26 must explain their purpose and
must not replace native search or actions. Keep useful visual presentation for low
vision; screenshots document layout but never substitute for native data/actions.
Extract official strings/assets from FM27 where available. Mod-only text uses
English as its base, with existing localization preserved. Do not redistribute
proprietary assets. Name informative icons and ignore decorative ones.

## Shared HTML and actions

- Preserve messages, attachments, subjects, senders, dates, categories, filters,
  read state, navigation and actions. Preserve headings, lists, forms, groups,
  links, real tables, row/column headers and controls inside cells.
- General virtualized tables use StreamedTableLoader.PageRows = 50, harvesting
  across frames. A native realized window is not a complete HTML page. Reuse
  TablePaging, TablePages, TablePagerHtml and StreamedTableReader. Specialized
  readers retain their own contracts.
- Preferences modes and categories use the original TabBarItem → RenderTabBar →
  TabBarHtml.Build contract: tablist, button role=tab and native aria-selected.
  RadioGroupItem/input type=radio remains for actual form choices.
- Reuse form adapters and their common field-label resolver. Preserve the label,
  value, help, native availability and origin focus. Do not create per-caption or
  per-screen allowlists for ordinary buttons or fields.
- SIButton uses ButtonAdapter.Activate and the original ServerHandles transport.
  Native state determines disabled. Mapping failures must return contextual
  errors, not artificial unavailability, placeholders or successful no-ops.
- Alt+I uses the original /describe → /speak → SpeechPipeline path. It must not
  inject descriptive text into the top of the DOM or duplicate labels.
- Preserve Tab/Shift+Tab, Enter/Space, Escape, Home/End and component-specific
  arrows/shortcuts. Never capture arrows globally or break text cursor movement.
  Preserve selection, focus, scroll, history and origin on refresh and close.
  Modals suppress their background. Use semantic HTML and native controls first;
  add ARIA where needed. Do not replace ordinary tables with ARIA grids.
- Do not add generic development-coverage warnings to normal screen reading.
  Keep useful loading, action and error feedback. Coverage belongs in these docs.

## Demonstrated FM27 native differences

- FM27PanelCatalog reads per-document layer collections and native navigation
  ordering, maps them to the original logical navigation model, and selects the
  actual interactive foreground. Compare native elements by Pointer; managed
  wrappers are not stable identities. Do not expose passive or covered panels.
  FM27 splits MenuTitle/MenuBody, ReportBody and CardBody into separate layers.
  Merge collections belonging to the same original logical role rather than
  overwriting them. DatabaseSelectionCard uses CardBody; omitting it exposes the
  background and loses its controls. Title-bar detection uses the native layer.
- FieldLabelReader handles the FM27 wrapper hierarchy using the original common
  field resolver. SIDropdown and FMSearchableCombobox are distinct native types;
  use the real widget/controller, never a pointer cast to an unrelated type.
- FM27ShortcutBinding uses native rebinding completion/cancellation, with an
  extension of the original text-field contract and original shortcut table.
- Native colour grids reuse the original radio-group model. Form colour tables
  preserve original per-cell controls and real table headers. Native disabled
  preset-dependent controls must remain disabled until their prerequisite changes.
- Native external links derive from actual events/banner data. Ordinary button
  activation remains generic. Do not hardcode translated button labels as routing.
- The overview FM menu is now SIDropdown, not SIButton. Alt+P returns the existing
  rendered dropdown handle and uses the original dropdown session and keyboard
  handling. Never register a second handle for the same element.
- A realized dropdown row can contain OpenPanelEventContext. Its bound button
  must execute through ButtonAdapter, as in FM26; changing the selected index
  alone does not open the custom-database dialog. Ordinary value choices retain
  the original selection binding, rather than treating every row as a command.
- Both speech and HTML use the shared native descriptor adaptation, including
  FMSearchableCombobox and decorative TintGradient, mode and own-club images.
  Entity-image buttons derive names from their own NavigableLink/component text.
  The original icon-family names also apply to FM27's 3x sprite variants.
- Save dialogs use the native placeholder or shared BrowseFilesSearchAndTypeElement
  template to label their file-name field. Retain-player-rule removal derives its
  name from the actual EventFmxRemoveRetainPlayerRules command.
- Tooltip-only InfoIcon wrappers use the original FM26 suppression policy. Do
  not programmatically show the native tooltip: that path previously crashed.
  This does not establish access to all tooltip text or profile appearance edits.
- Screen title extraction recognizes native notification captions and setup
  panels. Profile rules remove duplicate title text, without hiding instructions,
  league summaries or custom-database player counts in nested frames.
- Keep the original SSE/LiveValues refresh path. Defer body morphing while a
  dropdown session opens or remains active, including responses already in flight.
  Otherwise an older body strips session attributes while preserving its options,
  making the dropdown appear populated but unable to commit. Commit/cancel flushes
  deferred refreshes and preserves origin focus. Save deletion refreshes the table
  and native selection/availability without a page reload.
- Date selectors, toggle-list selection, row hover, input APIs and match event
  access changed in FM27. Their adapters compile against actual native APIs;
  match runtime validation is still pending. The DateGrid calendar and tested creation
  paths and their limits are recorded in [TESTING](TESTING.md).

## Metadata 39 safety

Do not read PanelLayerSettings.Layer.Name: its generated string conversion caused
a native crash. Do not receive Layer in a Harmony Postfix, or call
TryGetLayerInfoForPanel(..., out LayerInfo): generated value-type transport crashed
at Layer.op_Explicit. Use GetDocumentHandles/GetLayersForDocument via the common
FM27PanelCatalog. Do not use offsets to disguise interop errors. Crash analysis
must identify the actual stack, not infer a cause from an isolated Class::Init log.

The complete OnPanelLoadedInternal interception was also removed. Crash dumps from
career navigation showed the original-call Harmony trampoline entering that native
method before the observer. Omitting Layer from a postfix still leaves Layer
marshalling in the trampoline. PanelOpenedObserver now subscribes to the native
OnPanelOpened event, whose only argument is Panel. Its delegate remains rooted and
is unsubscribed on core shutdown/reload. The original arrival/profile/appearance
handling is reused on the following Unity frame, after the game's remaining
opened callbacks finish. Native callbacks only enqueue Panel references; they do
not re-enter navigation or tutorial construction. The native Tutorial layer maps
to the existing overlay role. The FM26 manager-level tutorial auto-dismiss was
removed after CloseCurrentTutorial raced FM27's floating tutorial setup and threw
from FMTutorialHandler.OnTutorialDeinitialised (null child removal). Observe the
actual tutorial and use its bound controls instead of forcing that manager call.
Do not call RestartTutorial on an inactive/completed tutorial: FM27's whole-screen
restart path also assumes a live tutorial root. Use only the actual active UI control.
Cold startup, hot reload and repeated destination/Portal
round trips must be included when checking this boundary.

## Coach creation and the career shell

- Reuse the original coach form adapters and semantic controls. FM27's searchable
  combo has separate full and filtered popups: focus the native text input, update
  its real value and let OnNewValue drive the filter lifecycle. m_hasSearchText is
  not evidence that a popup is visible. Reopening a filtered control must preserve
  its committed native value. Hybrid checkbox cards own their title/help rather
  than borrowing the question above every card.
- Native stepper text uses global-text-primary for the current step; alpha and
  font weight do not distinguish it in this build. Compare sibling identity by
  Pointer. Include SIEnabled dependency boundaries in the original shape signature
  so an in-place role change enables its dependent fields without forcing every
  radio-state change into a full refresh.
- FM27 PortalAdaptiveContent exposes messages, next opponent, Data Hub, transfer
  and wage budgets, news, agenda, standings, player statistics and weekly training
  together. Preserve the native widget order and content. Reuse heading promotion
  and tile-title activation, reading inside titled tiles rather than flattening
  their contents into the title button. Weekday captions are subheadings. Actual
  money captions/values replace redundant, misleading budget progress-bar readings.
  The observed training-rest asset family supplies meaningful Rest icon labels.
- The inbox host can be an unnamed MessagesStreamedObjectList. Reuse the original
  four-column message table, modal view, status and focus contracts. Visible native
  category chips accompany status. Refresh row bindings at final render because
  other streamed dashboard tables can recycle the inbox during composition. Keep
  the table item across an initially empty filter bind; otherwise the final shape
  can describe rows that the served body never included.
- FM27's selected Unread tab can retain a just-read native row. Only while that
  real tab is selected, exclude rows whose actual unread indicator is off. The
  original client waits until the message view has been observed, refreshes on
  closure and restores focus to the next unread subject. Do not guess read state
  or activate an unrelated filter. Full messages still use native tables/actions.
- FM27 IList proxies can omit inherited ICollection transport. List windowing
  falls back to VirtualisedList's native focus-source ItemCount getter; never
  substitute the number of realized rows for the total. General tables retain the
  original 50-row harvest/page contract.
- Resolve profile policy from each element's actual owning panel, using the same
  original managed registry including external overrides. An ambient navigation
  profile must not rename streamed table cells or SSE values from another panel.
  Decorative entity textures are not content. Entity crest buttons use text from
  their own native entity wrapper, bounded before another cell/panel.
- The permanent menu has six actual primary categories. Exclude contextual/debug
  definitions from the native map. Tactics and Training have their own ribbon
  targets. Alt+1..6 retain FM26 meanings; native Staff and Schedule destinations
  changed to 1204137728 and 837367296. A bound navigation caption resolves an
  unexpanded gender token in a PanelID title.
- Native SearchBox reuses the original text-field HTML/value setter and speech
  search mode. Its result SIText links reuse the existing native link dispatcher;
  close/show-all commands remain separate controls. Empty TileSearchPanel wrappers
  are layout, not unknown actions. Close commands and card-close templates share
  the original Escape marker. Close the foreground card before considering a
  covered title-bar BackButton, preserving the originating screen.
- FMActionButton is a shared FM27 container: its caption/value live beside the
  icon-only action-button, inside that same component. Reuse the original primary/
  description and folded-label contracts against this bounded component, rather
  than announcing the action as unnamed or borrowing another card's caption.
  Entity captions can also be direct rich-text links without NavigableLink wrappers.
  Native tutorial text and its EndTutorialButton remain accessible; decorative
  popup backgrounds and the duplicate heading are suppressed by a normal profile.
- The original progressbar span/value presentation is retained, with an explicit
  accessible name from its existing native field caption. ARIA progressbar does
  not derive its name from text content; a live career-shell axe audit detected that
  missing contract. Native key-capture Escape cancels rebinding before any generic
  card-close action, retaining the existing cancellation/value confirmation.

## Verification and portability

For each change inspect the real FM27 screen and native model, inspect the FM26
equivalent, reuse its components, record demonstrated differences, then verify
DOM → event → transport → main-thread dispatcher → native command → observed result
→ refreshed DOM. A changed token, nonempty HTML or dispatch acknowledgment alone
does not prove success. Check selection/value, destination, apply/cancel, reopening,
persistence and focus when applicable. Stale actions must never affect another screen.

Protect user privacy decisions, authentication, saves, profiles, licensing, ACLs
and unrelated mods. Do not perform resets or destructive actions for coverage.
Xbox may run under WindowsApps; inspect package identity rather than assuming
the visible installation path. Keep Steam/Xbox inspection generic without assuming
binary compatibility. Distinguish static review, unit tests, live checks and NVDA.

## Squads, reports and asynchronous native content

- FM27 wraps the always-visible status badge in an extra template. Resolve its
  actual ExpandingListItem BindingRoot, then reuse StatusFlagsReader for every
  StatusTitleShort/Full/Reason and overflow item. Injury names and treatments are
  game data, not a fixed injury-type or event-caption allowlist. The condition
  slot also uses the native `level=injured` asset family.
- Form cells can have an unnamed HasMatches tile. Reuse FormRatingsReader and the
  original latest-first history. Attribute rows retain two columns but can omit
  the old value-wrapper name/button. Reuse AttributeRowAdapter and attribute HTML.
- Unity 6.3 link-tag events require an explicit target. SITextLink sets that
  target and disposes both pooled link/source event pairs. It still resolves the
  real link ID from the current text; no guessed IDs or entity commands.
- A player banner's single native ability button may contain both current and
  potential FMStarRatingTemp widgets. Read both with their own visible captions,
  preserving half stars and uncertain ranges through StarRatingHelper. Never
  substitute hidden CA/PA values. Updated banner anchors keep the original table.
- Native entity subtabs exist outside the human team's six primary categories.
  Keep SecondaryNavigationTabs when the native main-tab selection is null; suppress
  only the primary-map duplicates. Contextual navigation dropdown captions are
  menu names, not the ordinal of the last destination chosen. TeamSquadReport's
  bound header names the actual senior/youth team; ClubSite's header names the
  viewed club instead of its generic facilities asset title.
- Hybrid split controls gained a parent-anchor around their two branches. Read
  the sibling value inside that same component and reuse the original table
  dropdown's column-name/value split. Do not borrow text from another cell.
- Responsibilities retained their original models/HTML but renamed the duty list,
  assign dropdown and Take/Delegate visibility wrappers. Resolve those native
  anchors, skip hidden wrappers, and retain swept rows and real control handles.
- FM27 guided tutorials move from TutorialDialog into the handler's owned floating
  block. Resolve that ownership by native Pointer and visible root; use it for the
  walker, profile, title and step token. Keep the game's own Back/Continue/Finish/
  Ignore controls. Never show/reparent/close tutorial content from the reader.
- Tactical-effectiveness steppers use real 0–100 values and threshold lists,
  rather than stars or generic filled-pip counts. Reuse StepperAdapter and the
  actual button command; radial factor meters reuse the original gauge reader
  and SyntheticTables. Icon-only OpenPanel commands can resolve the official
  title through their small PanelID asset, as BookmarkBar already does.
- Medical current/prior-season lines have independent date ranges. Match the
  observed native solid/dotted keys and StyleColourIndex, then render each line
  with the original single-series Date/Value HTML. Shared-date charts must agree
  on both length and date labels before sharing columns; currency conversion is
  unchanged.
- Radio captions/descriptions and table headers can bind after their elements
  attach. Include these folded texts in the original settle-window signature,
  excluding checked state. Recheck column labels after across-frame table harvest;
  if they changed, harvest against the new schema before serving rows. Repeated
  schema changes remain provisional. Keep the original 50-row paging contract.
- A shallow, bounded native template-frontier signature detects a wizard changing
  into its editor after background generation, even beyond the action window.
  Hidden step trees do not contribute stale wizard identities. No screen timers
  or per-caption navigation commands are added.
- Same-type subject navigation can reuse both PanelID and the native Panel
  Identifier: senior and Sub-21 reports demonstrated this. Include the actual
  BindingRoot key in the server screen token so a rebind invalidates the previous
  subject's body/handles. Keep Navigation's original panel-type identity for its
  cursor/history contract. Production tests wait for the actual native destination
  and context before checking the settled DOM; dispatch acknowledgment alone can
  leave them inspecting the previous screen.

## Calendar and floating training selectors

- FM27 replaced the Weeks streamed list with DateGrid. Read native m_rows and,
  when HasPrefix, m_dayTracks. Require seven actual bound DayCell columns per week;
  carry PrefixCell controls and whole-week notes into the existing calendar row
  header. CalendarContext and CalendarDayAdapter share the native date lookup,
  including unnamed date text in training mode. The existing CalendarScheduleHtml
  table and keyboard contracts remain in use.
- Event visuals can duplicate a separate EventLinkSwitch action. Prefer that real
  action only when both complete captions agree within the same event binding.
  Native bound commands use ButtonAdapter; links use SITextLink; context-only
  events retain their native menu. Truly inert tiles are text.
- TrainingScheduleSelectorPopup uses the existing floating-popup surface. Native
  categories are NavigatableVisualElement entries in an ExactlyOne VirtualisedList;
  use its index selection, as GridSelectorSelection already does. An internal
  Clickable alone does not prove that a wrapper has a usable command. Do not
  promote every NavigatableVisualElement into a button.
- Popup triggers have stable native identities and report actual disabled state.
  Text changes join the popup signature so category rebinding refreshes its list.
  Popup forms reuse ordinary form adapters; projected impacts reuse the FM26
  negative/positive-bar table with actual widget ownership. Close uses the existing
  Escape marker, and reload dismisses the owned native popup before unloading.
- ExactlyOne list-entry buttons also read the actual VirtualisedList.Selection
  membership for the original selected-state announcement. Do not infer selection
  from the chosen caption or force it to persist when the game clears/rebinds
  the list's own selection model.

See [screen coverage](fm27-screen-coverage.md) and [testing](TESTING.md) for what
was actually read or exercised. These boundary adaptations do not establish
complete match, transfer, dialogue or career coverage.

## Gameplay walkthrough boundary adaptations

The separate QA Gameplay career was advanced from July 6 into the August 8
friendly. These are FM27 native boundary changes; the FM26 HTML renderers,
message-history model, table paging and keyboard contracts remain in use.

- FM27's Current history reference exposes its position in the registered
  PropertyValue field. Read the typed numeric value and validate it against the
  live history length. The old OnHistoryIndexChanged postfix intermittently
  received invalid positions on this build and was removed. Never read the
  ref-returning CurrentIndex or box ValueWithChangeNotify<int>. Opening a player
  from a message and Back restored the same full message twice, including after
  reload, through the original merged-history implementation.
- Full native Menu documents suppress split title/body backgrounds; a higher
  interactive layer suppresses a noninteractive full-menu document. This was
  exercised in board meetings and an entity report opened over a press conference.
  It uses the catalog's actual document/layer membership, never Layer.Name.
- Introductions use titled whole-tile buttons around real tables, stars and
  links. Extend the original mentoring/pitch traversal and title activator by
  component shape. Compact Body/ControlsArea/NameAndHoverControls value tiles
  retain the original button label, value, help and locked state. A commandless
  SpeakTo focus-transfer button is transparent;
  its actual answer buttons remain interactive. Progress bars without a caption
  retain the shared Progress label instead of a template ID.
- Entity names reuse the original asynchronous Name request and verified link
  record identity. FM27 requires a native subscription owner. Request returns a
  temporary source root, while the callback identifies its Name child. Associate
  that child key with the exact session, language and record; ignore old answers.
  Flag-only nations and crest-only single-record club links use this shared path.
- Parallel Headers/Values form columns resolve by matching native positions.
  Stop ordinary label ancestry at Cell boundaries so one field does not borrow a
  preceding column's caption. A state-only Frame around an On/Off switch is not
  the setting name; resolve outside it. Original checkbox/radio/dropdown HTML is
  retained. Modifier chords must bypass dropdown priming.
- Form-table consolidation also handles the native suggested-lineup List with
  position, player links (including replaced players), reasons and optional
  selection. NavigableLink text uses the existing native text-link adapter.
  Outside tables, promote text-only links; components already owning a real
  native button keep that original action, avoiding a duplicate competition link.
  Suppress position/body-language pictograms only beside their own visible text.
- SIVisible/BindableSwitchElement binders and FMStarRatingTemp binders participate
  in table readiness before hidden-branch pruning, following the FM26 fixture
  reader. This prevents half-rebound team selections and zero-star snapshots.
  Blank-to-readable SIText transitions join the existing bounded settle signature;
  ordinary nonempty value updates still use local live values.
- Preseason projected impacts reuse the negative/positive-bar table by native
  component scope. Contract and hierarchy glyphs are decoded from observed asset
  families; FM27's ui concepts-placeholder is informative for the four verified
  hierarchy assets despite its formerly decorative prefix.
- Match body-language icons moved under BodyLanguageMorphIcon and use vector
  assets such as body_language_neutral_normal/calm. Decode the observed neutral
  family; do not invent bands for other families. Detailed words still come from
  the native body-language reference. A legacy lighting instance may expose an
  all-default weather setup: reject it rather than announce zero-degree weather.
  Other named vector families retain their localized native word in the existing
  count/detail composers; do not assign an invented FM26 happiness band.
- Standalone match-report tiles now use BindableVariableSizedGridElement as well
  as BindingRemapper. Reuse the existing event, xG, momentum and latest-score
  readers. TeamsBlock names its two native entity links ClubLink. statsOnlyView
  arranges the same labelled home/away bars in three columns; feed their actual
  value texts to the original live-stat table, excluding the visual bar ratios.
  The news-result MatchStats component uses the same paired-bar reader.
- A menu drill's focus request is tied to its source native folder path. An
  older root-body response cannot consume it before the destination arrives.
  Reconnecting to an already-open menu also requests its initial focus. Five
  consecutive Right/Left cycles were checked before the expanded squad regression.
  Alt+Enter releases client popup deferral as well as the native popup session,
  so a same-panel Continue can refresh after an open dropdown.
- Ordinary single/double/triple substitution events interleave (incoming,
  outgoing) players. The observed halftime double and triple instead group their
  incoming players before outgoing players. Read outgoing slots accordingly;
  otherwise a newly-entered player's booking can disappear from the risk summary.

See the gameplay evidence in [screen coverage](fm27-screen-coverage.md). A passing
walkthrough does not establish every match event, dialogue or later career state.

## Populated report and dialogue boundary adaptations

- Last-match widgets expose their resolved dataset keys directly. Read those
  before legacy named-ancestor observations. Pass-map points use
  m_playerDataPoints/DataRef and nest Player/SquadNumber in PlayerMatchData.
  Preserve visibility and reuse the original named-player/link composers.
  Average positions retain native coordinates, without invented distance units;
  action-zone rows retain each team's defensive/middle/final-third values.
- FMHeatMap binds a column-major 65-by-100 texture in the observed report. Its
  screenshot/hotspot verify the bottom-left origin. A validated nine-area table
  explicitly describes shares of plotted intensity, not minutes or possession.
  Invalid/ragged/negative/nonfinite data decline. The 13-zone goal layout also
  draws shots: paired SubjectStatValue/XgValue elements distinguish that variant.
  Use the existing WidenColumns seam for separate Shots/xG columns. Goal-only
  maps retain Goals and the original long-range goal-binding fallback.
- Compact 4x4Tile calendar icons retain their localized SessionName in their own
  Event record. Match icons use that record's TeamOpponent/MatchVenue and the
  existing entity-name request. Never search another event in the same day.
  Full-calendar linked hit surfaces borrow only their own
  CalendarEventBindingExpect caption and use the existing deduplication rule.
- Home-side past-match incidents leave their primary SIImage unnamed while the
  away branch retains IncidentImage. Use the same leading image and existing
  event decoder. Scorer/assist attribution was verified for all three goals.
- Transfer response tick/time/cross icons are named from their actual native
  TransferCentreResponse Accept/Stall/CancelReject commands before generic icon
  labels. CancelReject also cancels/withdraws outgoing offers; it is not Close.
  Nonnegotiable optional dates can display a localized no-expiry caption while
  holding DateTime.MinValue. Native-disabled dates use the original readonly
  fallback and reject stale actions, instead of exposing 01/01/0001 as an editor.
- GenericModalDialog belongs to DialogManager's queue. PanelManager.Close alone
  left that queue waiting and prevented the same question from reopening. Resolve
  its visible native response whose DialogButtonData.EventID equals the native
  CloseDialogEventID, by slot, and activate the original button. IsCloseControl
  marks that response for the existing Escape path; history Back uses it too.
  Never infer cancellation from translated captions or button order. A generic
  dialog without a native close response requires an explicit answer.
- Conditional nationality links nest their boolean source below the entity
  component. Resolve its typed Nation within two binding ancestors, retaining
  native ownership and the original name-request verification.
- OverviewTitleBar Back/Forward handles use the original merged History action,
  like Alt+[ / Alt+]. A direct SIButton click skipped the virtual message entry.
  Capture direction on Unity's thread during registration and keep restoration
  waits on the request thread. HTML and pure MessageViewHistory are unchanged.
- DialogData overloads equality through native code which rejects a null operand.
  Use the managed `is null` wrapper test before resolving dialog cancellation.
  An ordinary `== null` crashed composition of the owned-save overwrite prompt.
- The shared club-header nation-frame and NavigableLink place a flag button beside
  their linked country caption. Read within that same wrapper before requesting
  an asynchronous entity name; never borrow a wider club header's first caption.
  Grade images paired with grade-text and captioned objective/entity badges are
  decorative; keep the written native meaning and underlying navigation.
- The compact wages tile exposes a seven-row BindingRoot list with TypeText,
  QuantityText and AwarageText cells. Reuse SyntheticTables' named-cell extraction
  and HTML. This list is distinct from the full 19-row wages screen.
- Compact competition fixtures use four aligned native cells and retain team/
  country buttons inside them. Reuse native form-table rendering and list-window
  chrome, with the home-team cell as row header. HeaderSmall is the shared
  in-match schedule header variant; the existing fixture loader harvests it.
- The national ranking chart's native plotted Y coordinate is inverted; its
  FifaRanking record field is the actual rank. Read validated Year/Months and
  FifaRanking through the existing value-chart table. Never infer rank from an
  axis constant or treat that coordinate as a date/currency history.
- AggregateSignature's original unit separator remains the screen identity
  contract. It is not legal in an HTTP field value: after an asynchronous name
  arrived, the in-flight dropdown regression's strict HTTP reader rejected the
  raw X-FM-Token. URL-encode that header at RuntimeSurface and decode it in the
  companion before comparing with the unchanged SSE token. Preserve all internal
  identity, menu/history segmentation and HTML contracts.
- The live OverviewPrototype mounts dugout advice and redraws chart data without
  navigation or a user action. Extend the original self-updating-panel gate used
  by penalty shootouts. The existing structural signature observes xG series
  counts and actual native momentum values, without including the advancing clock.
  Keep ordinary preferences' original bounded settle timing.
- Live momentum omits the report legend. Only on that overview, read the current
  scorebar's actual home/away captions, without its cached fallback. The observation
  preallocates nine periods, while FMMatchMomentumChart.m_matchMomentumData holds
  only the played/drawn periods. Validate its zero-based backing MatchTime fields
  before reading that prefix; future zero values must not be announced as Even.
  Reuse the original LastMatchTables composer and semantic table.
- A streamed-table load can outlive a captured answer/skip control. In the replay,
  the checkbox stayed attached and individually visible while its SIVisible
  ancestor had display:none, and RenderNode emitted an unnamed input from that
  stale item. Revalidate native ancestry at rendering and activation. A skipped
  stale item emits the existing provisional comment so the FM26 retry also picks
  up replacement content absent from the old walk. No alternate HTML or live-value
  protocol is needed; temporary diagnostics were removed.
- A final shortcut-capture regression after more than eight hours of gameplay
  and development reloads crashed in VisualElement.SendEvent from ButtonAdapter.
  The inherited SIButton path first called SimulateButtonClick and then sent a
  supplementary ClickEvent. Attachment alone does not establish that a native
  callback left the target safe for that second dispatch. SIButton now uses its
  native SimulateButtonClick once, as the existing match-playback path does;
  other button types retain NavigationSubmitEvent. The fresh-process core,
  widgets and dropdown suites passed, including opening/canceling key capture.
  This is not yet evidence of another equally long session. The dump did not
  establish a Layer-marshalling failure. Button-group selection also compares
  native Pointer identity rather than managed wrappers.
- Individual-training selection demonstrated a second cross-frame boundary:
  stars and the complete attribute block bound while the player table was being
  harvested. Captured rows lacked them, but the post-render shape already included
  them, suppressing the needed refresh. Retain the walk-time shape for streamed
  composes and compare it with the final native shape; a difference marks the body
  with the existing provisional comment. The original bounded marker retry then
  obtains the later content. Fast-path HTML, settle timing and final shape identity
  remain unchanged. Star readiness also participates in the structural signature.
- Attribute collections can likewise be replaced during a tab/player transition.
  Revalidate their native list and rows before rendering, using the same original
  retry marker instead of emitting unreadable rows from the old content.
- The training header nests Workload and TrainingRating together inside
  TrainingDataVisible. The existing named-row synthetic table separates all four
  real caption/value pairs, retaining its HTML and headers. PlayerHeader also owns
  the original suitability pitch/key and the new trainingoptions/positions groups;
  update the existing restructure anchors. The original PositionSuitability reader
  still reads valid native 1–20 familiarity values without new scale bands.
- FM27's dropdown-base-regular-leading-icon-label template writes its selected
  choice into the SIText named label. That leaf is a value, unlike a static menu
  caption. Adapt the common label resolver so the original table renderer supplies
  the column header and shows the real selected choice, retaining native value
  confirmation. No translated field names or screen-specific command routing.

## Competition, analysis and instruction component deltas

- MatchLinkButton is a hit surface beside its date/time/team texts inside the
  shared MatchLink component. Name it from those same visible texts and retain
  its ordinary native button command. A future fixture opened the corresponding
  match preview with both teams and date verified.
- Compact pitch/TOTW slots can leave fm-tactics-shirt-name blank and render their
  own player link below the shirt. Read that visible linked name before trying a
  human-squad number lookup. Reuse the original PitchFormation table; the BWSL
  dream XI's eleven actual player names were checked.
- The competition breadcrumb's EntityTextureComp is decorative beside the native
  competition caption, just like the existing club/nation entity textures. Keep
  it out of reading without removing the caption or navigation controls.
- Entity captions take precedence over a destination asset's generic title.
  Report headers separate LogoFrame and ContentFrame; resolve their own first
  visible name before reaching unrelated links elsewhere in the header. This
  preserves Arsenal/BWSL rather than announcing Facilities/Overview.
- BlockElement derives from BindableSection in FM27. Apply the existing section
  traversal contract, including legitimately empty analysis blocks. FM27's
  unnamed info-widget-small template is the same tooltip-only information widget
  already excluded by FM26's safety policy. Detect its actual template identity;
  never show its tooltip to obtain content.
- Data Hub goal-type rows use BindingRoot instead of AsyncBindingRoot. Keep the
  original synthetic Type/Goals table and native-list expansion chrome; expanding
  each list read all eight native goal types without F5. Normal streamed tables
  retain 50-row pages. Native goal/formation/set-piece section anchors were updated
  in the existing profile without caption-based action routing.
- Player-instruction cells gained binding/template wrappers, placing the same
  TileBindingExpect marker six levels below the cell. Extend the bounded original
  tile lookup so the player/position context precedes the Instructions landmark.
  Editors keep original radios/help and Switch Instruction links. Marking's two
  options were actually locked for the selected goalkeeper role, with the native
  reason retained; Back restored the out-of-possession tab without changing values.
  The tactics IJ icon is EventFmxTogglePositionPersonalisedRole; it is not the
  editor-opening command. The role dropdown's native navigation item opens the
  PlayerInstructionsCard.
- Coach profile and coach-assignment section titles also moved into named blocks.
  Update the existing profiles using their native block/header anchors. Keep the
  original attribute table and its redundant value-header exclusion; do not turn
  ordinary values or the second column label into new section headings.

## Recruitment and report continuation

- Contract terms retain their lock/text/value controls inside the shared unnamed
  Frame rather than the old frames-contract-terms name. Recognize that native
  structure and reuse the original Term/Value/Negotiability/Remove table. The
  expanded SpeakTo side panel also reuses the original attributed conversation
  and Actions section. New Other Information, Wage Budget Impact and Transfer
  Budget Impact blocks retain the existing headings and live label/value folding.
- A readonly value-named contract dropdown must not register its empty selection
  state as a live replacement for its real caption. Retain the original static
  value-named contract on both editable and disabled branches.
- Optional DateSelector widgets can be unset and leave their bounded calendar
  collection uninitialized until opened. Prepare that actual native controller,
  retain its allowed dates and hide only the popup opened for this preparation.
  Preserve dateNotSelectedText instead of rendering DateTime.MinValue. An actual
  hidden clear button is not an accessible clear command; the native offer Revert
  action restores the draft. DateProbe now inspects the interactive document set.
- GenericActionButtonDataWidget hides its own bound caption in icon-only mode.
  Use that single caption to name its command, excluding TooltipVisible; ordinary
  content visibility remains unchanged. The weekly-training Praise action was
  exercised through its real conversation and native reply.
- KeyFindingsRowExpect gives its DataPointButtonFrame the finding's own Title.
  This supplies an actual label for the heat-map command without a screen/text
  action allowlist. Today's fixture rows likewise moved competition and time into
  Competition beside HomeTeamRemapper/AwayTeamRemapper. Reuse the original fixture
  table, native entity commands and SIText link-tag operation.
- The observed full heat-map card's dimensions are AlongLength=100/AcrossWidth=65;
  its column-major dataset and texture are width=65/height=100. Validate that
  orientation rather than reversing it. Track the native texture's updateCount
  in the existing action settle/retry signature: equal-sized player/phase changes
  otherwise update the summary while leaving the captured table stale. Misa/Kim
  Little/Misa and all three possession phases changed the nine-area table without
  F5. The compact tile's different template fields are addressed in the later
  heat-map tile notes. The earlier pure aggregation tests alone did not establish
  this integration.
- Sound fetch/decode failures now reach the existing client diagnostic log with
  the cue name and HTTP/decode error, rather than failing silently. No coverage
  banner or alternative sound runtime was introduced.
- Entity-name requests can complete synchronously inside BindingUtils.Request,
  before its temporary key returns. Retain that in-flight request context and
  apply the same entity-reference/language/session verification as asynchronous
  answers. Otherwise cached names disappear despite successful native replies.
  The compact training fixture now names FCF 91/Fora instead of Icon.
- Idiomorph's ignoreActiveValue also suppresses the active node's children.
  Preserve that protection for input/textarea/select/contenteditable controls,
  while allowing a focused command's caption to update. Four native Play/Pause
  toggles matched their actual sprites and retained focus after this shared fix.
  No alternate HTML or widget-specific refresh protocol was introduced.
- FM27's shout cooldown wrapper is shouts-cooldown-disable around an unnamed
  SIFloatingPopupWidget. Adapt the original match-header availability token to
  that real wrapper so cooldown completion can refresh without another action.
- The fixture's weather is now MatchPlaybackController.Setup.
  MatchLightingAndWeatherSetup. Its actual 25-degree/Wet values matched the
  pre-match display; the initialized legacy lighting singleton held defaults.
  Alt+4 and ConditionsProbe use the current setup, preserving the original composer.
- One-shot scans must keep their bookmarks while there is no live audio slice.
  A period transition rewound the native event window and re-fired old first-half
  sounds, including a disallowed goal, when second-half play appeared. Gate all
  five event scans with the existing active-playback source. The later Brighton
  continuation reproduced a separate live-window regression and verified its
  shared guard at a normal-speed interval, as recorded below.
- Match-event rows retain SIButtonGoToIncident/SIButtonGoToPastMatch through the
  existing string-grid RowActions seam. The semantic table and its headers stay
  intact; activating the minute opens the actual native incident replay. Actual
  disabled row actions retain disabled rather than registering a working handle.
- A toggle can own a panel through single matched OpenPanel/ClosePanel events
  targeting the same asset. Closing that panel alone left its checkbox checked,
  requiring two activations to reopen the match controller. Resolve the native
  checked owner and close through it, without a panel-name allowlist or an
  unrelated menu-history wait. DialogManager cancellation retains precedence.
  Back, Escape and Alt+[ each cleared the flag and allowed one-activation reopening.
- Result attachments pair WeatherIcon with a visible WeatherDescription carrying
  the conditions and temperature. Classify only that duplicate image as a layout
  wrapper, retaining the actual written value. The neutral numeric happiness=3
  sprite uses the existing neutral-morale label instead of exposing grade-value.
- FM27's StyleTransitionLooper is a scheduled style-transition container. Traverse
  it as a layout wrapper at the native boundary, preserving its children instead
  of reporting an unknown interactive control during loading.
- Closing the final unread message leaves no origin or next row. The existing
  post-refresh focus restoration now lands on the actual selected Unread (0) tab
  in that empty case; populated unread lists retain their original row position.
  No replacement message list or additional HTML control was introduced.
- The cross-frame walk/render guard must exclude StreamedTable descendants:
  harvesting intentionally recycles their native row windows. Including them
  repeatedly marked complete tactics tables provisional and caused unnecessary
  full-page harvests. Preserve the full original SSE shape; exclude those rows
  only when deciding whether earlier non-table render items need another walk.
  The loader still validates changed headers and preserves 50-row harvesting.
  Checked the native possession and panorama views; the latter has both role
  columns, while the former legitimately has one. Tests establish the phase
  instead of assuming that loading a save restores this shared view preference.
- A table-cell NavigableLink can expose its crest button and written embedded
  link as two identical entity actions. Both Arsenal controls opened the same
  club screen. Keep the original text-link adapter (including its context menu)
  and omit the image hit target only when that same native component contains
  one button and a visible, bound primary text link. Outside tables the existing
  button/caption contract stays intact. The standings/fixture captions now read
  once; activation, Shift+F10, Escape and origin focus were checked natively.

### Recruitment focus component continuation

- FM27 places the shared position picker below RecruitmentFocusDialogMainBlockDescription,
  replacing the old named owning block. Detect its actual slot template
  (input-squad-planner-data-point-regular and PositionText) rather than the
  containing screen/block name. This reuses FM26's 24-position semantic table,
  flank/line headers and checkbox states for both possession phases.
- Those position buttons, tactics role buttons and shirt buttons now use the
  same single native SIButton operation as the corrected ordinary button path;
  no supplementary ClickEvent is sent after native dispatch. Position selection
  was exercised live; tactics role/shirt regressions must run on this source.
- A grouped pair of controls separated by a text connector can share one native
  caption. Preserve that caption and distinguish the first/second control instead
  of naming the second field only after the connector. Recognition uses the
  actual two-control group structure, not field names or translated captions.
  The focus age bounds were changed to 18/23 and retained on reopening.
- Staff assignment name cells can be unnamed SIText links. Their embedded link's
  UIRecord is a PersonReference. Reuse that typed identity for FM26 selection-row
  naming; nationality/club links cannot substitute for the person. Scout and
  analyst checkboxes now identify Jack Ellaby/Amrit Bains and their actual roles.
- The compact objective/statistic list reuses Frame-condition without the full
  condition editor's enable/name/comparator slots. Require the actual full
  template before using ConditionTableContext; otherwise retain the original
  generic controls and written statistic. This removes fabricated unavailable
  Enabled cells and restores the compact list's Delete actions. The native
  game also drew a blank compact row for the height condition; no missing title
  was fabricated. Full condition rows retain the FM26 checkbox state reader:
  SICheckBox derives from ExpandedToggle/Toggle, and Toggle.value is the displayed
  checked state. SICheckBox.Value did not reflect this row's current check state.
- Condition handles also reject lists hidden by an ancestor. After changing tabs,
  an old condition handle was rejected and the enabled height remained unchanged.
- The focus bin icon triggers RecruitmentFocusRemove, whose actual confirmation
  completes a focus and moves it to Completed. Its accessible action is Complete
  recruitment focus. EventFmxRecruitmentFocusRestart is labelled Restart
  recruitment focus. These labels come from native command identity, not captions.
  Complete/cancel/reopen and the actual Restart command were exercised. Editing
  a completed focus also starts an active copy; it is not the same as Restart.

### Floating match menus and recurring injuries

- FM27's individual touchline-shout popup contains a genuine StreamedTable,
  with SelectionCell/ExpandedToggle rows, inside SIFloatingPopupWidget. The
  inherited filter-only walk flattened the cells and omitted their selection.
  Delegate native tables and tab bars to the existing FM26 walker. Popups with
  tables now take the ordinary cross-frame StreamedTableLoader path; simple
  filters retain their inline path. Selection changes participate in the popup
  signature. No replacement table markup, click coordinates or new menu framework.
- Live validation selected Batlle by keyboard in the eleven-player table, read
  her checked state and the newly available shout choices, then sent No Pressure.
  Both tabs use the original tablist and aria-selected contract. WCAG-tagged axe
  found no violations. A specific native reaction to that individual shout still
  needs confirmation; a closed popup alone is not proof of that reaction.
- RecurringInjuryOverlapDrawer is the noninteractive painter over the front/back
  body diagram. Classify it as infrastructure in the common HTML/speech path,
  preserving the native recurring-injury legend, diagnosis, duration and treatment.
  Kelly's recurring knee-ligament injury message no longer has unknown-control
  markers; its WCAG-tagged axe check had no violations. StyleTransitionLooper
  uses the same shared classifier rather than a speech-only override.
- Quick tweaks use a separate native SIButton overlay beside their title and
  pending/active indicators. Extend ButtonAdapter's existing component-root label
  and folded-text path to tweak-header and player-header. Individual player
  expanders retain native aria-expanded from player-tweak-list visibility; real
  player-profile links remain independent commands. Active tweaks expose the
  native state-indicator through the original selected-state contract.
  Team and goalkeeper tweaks were submitted, then observed as active (including
  the actual exchanged winger positions). Reset > Reset all queued both removals.
  The opened collective and individual views had no unnamed buttons or
  WCAG-tagged axe violations. After Reset all, the active count disappeared and
  Smith/Cerci returned to their original right/left positions in native readback.
- A floating trigger can show an active count before its caption. Read all of
  its own visible text, excluding hidden state variants and tooltip content;
  the match menu now retains its native label alongside the count instead of
  becoming a button named only 2. No translated caption allowlist is involved.
- MatchFrame.StopTimeInfos records the final whistle as EndOfGame in FM27.
  Include that stop alongside EndOfPeriod in the existing MatchClock boundary
  input. The live PSG friendly ticked past 90 minutes normally, then the old
  companion ended at 92:41 +2 against the native frozen 90:00 (+2). Reloading
  the adapted reader on the same finished match produced 90:00 +2. The native
  period stops were StartOfPeriod 1, EndOfPeriod 11064, StartOfPeriod 11124 and
  EndOfGame 22570. Extra-time/penalty endings are not established by this check.
- Crest-only links can bind the same database record as Club and activeclub.
  Verified identical CombinedIndexAndType values in the result article. Treat
  these as aliases, while retaining ambiguity for genuinely different records.
  Prefer the bound entity name to a generic destination-panel title. The result
  article's unnamed home crest now reads Arsenal and opens the actual ClubSite.
- Native entity-name requests now share the composed panel-set context, including
  each panel's binding key. Per-panel request sessions reset each other when title
  bar and body both request names, producing continuous refreshes. The club site
  now settles with refresh/pending false; reference and language checks remain.
  A paired portrait/name component can also supply its visible embedded-link
  caption within the same two-child binding remapper, without climbing a whole card.
- Deferred inbox focus compares the native panel/binding/template identity while
  ignoring the trailing entity-name revision. A live trace of the final unread
  message showed names:18 becoming names:19 within the 100 ms focus-return window;
  the old exact-token comparison discarded the return, leaving focus on body.
  Actual navigation or an open message still cancels it. Empty Unread returns to
  the selected native filter; a remaining row retains the original table position.
- The runtime reuse audit now reports eight identity-only central files and the
  streamed loader separately as reuse with an adapter. It checks that the original
  50-row cross-frame harvest remains unchanged beneath the native column-rebind
  wrapper; it no longer incorrectly calls the whole loader source identical.

### Club facts and reconstructed history pages

- A captured TeamFactsCard screen shows nineteen aligned fact rows with separate
  club/division columns. Its StreamedObjectList is not a native StreamedTable.
  Extend the existing FM27FormTable consolidation, retaining FM26's table, column
  headers, row headers, ordinary cell walker and ListWindowSurface pagination.
  Headers use the actual TeamNameCell and DivisionNameCell captions. The exact
  three-cell template must match throughout; otherwise retain the original walk.
  Keep secondary division values, reputation headings/meters and native links.
- Fact-cell SIText can contain multiple independent embedded records. The first
  row contains Yuka Momiki at link 0 and Everton at link 1. SITextLink now exposes
  those parts through the existing JoinedTextItem and AppendButton path rather
  than flattening the paragraph or activating only its first record. A part's
  action validates the original text, native record Pointer, validity, enabled
  state and LinkHasActions. Unbound records keep the original text and request
  the existing bounded provisional retry. This is currently wired to fact cells,
  not a claim that every free-form paragraph has been converted.
- Native context-menu focus normally primes link zero. Pass an explicitly exposed
  part's index through the existing handle/menu path and set SIText.m_hoveredLinkID
  after the normal focus operation. This is the generated native property, not an
  offset. Everton now opens its club menu rather than Momiki's transfer actions;
  both menus have the correct owner, and Escape returns to their exact link.
- FM27 rebuilds a page's BindingRoot when navigating Back. Token-only position
  memory therefore misses the old page, and the surviving title-bar Back button
  kept focus. Successful body responses now include the actual native history
  index, only after confirming the composed token still matches the live page.
  Extend the original cursor memory for explicit Back/Forward moves, retaining
  native panel identities and separate modal keys. New branches discard reused
  slots; an intermediate category shell does not consume the pending return.
  Action validation still uses the complete original screen token. Native Back
  and Alt+[/Alt+] returned to the exact fact-cell link after club/player visits.
- Social Groups and Happiness transitions exposed another outgoing-screen race:
  detached tables were treated as cold lists, then the column-rebind wrapper
  repeated an already-provisional load. Several obsolete tables exhausted the
  client's existing twenty-second body deadline. The wrapper now rejects a
  detached/ancestor-hidden native scope and returns immediately when the unchanged
  FM26 harvest reports Rows null. Attached, genuinely cold lists keep their original
  realization wait. Four successive native transitions settled in 2.9–3.0 seconds
  each with no failed requests; no timeout or page-size increase was used.
- An empty lineup-slot face has neither a field label nor a displayed value.
  The original table-column fallback now also names this case from its real
  header without substituting the unknown-control marker as a value. The same
  slots appear in squad overview, roster, captains and penalties; their native
  choices and disabled state are unchanged. Staff activity reuses the existing
  FM26 Active offers only profile rename for the identical active-toggle widget;
  no inferred game translation or replacement control was added.

### Name revisions during dropdown interaction

- Entity-name revisions are content changes, not native screen departures. Use a
  shared screen-identity comparison that removes only the trailing `names:N`
  revision when deciding whether to defer an in-flight body or abandon an open
  dropdown. Keep the complete token for native action validation. Normal updates
  still refresh resolved names after the interaction finishes. The unread-focus
  comparison uses the same helper.
- The existing production-client dropdown regression holds a real body response,
  changes only its name-revision header and releases it over an actual open native
  dropdown. It also exercises a names-only screen event. Before the correction,
  the dropdown lost its loaded session; afterwards its real options, value and
  cancellation focus survived both cases. No replacement UI or fixture server
  was introduced.
- Error and busy body responses use the same encoded token/title header formatter
  as successful responses. The old error paths could emit raw unit separators in
  the composite token. A direct call to the actual busy-response formatter checks
  encoding independently of the dropdown race. Neither finding establishes the
  cause of a separately observed intermittent activation-request timeout.

### Segmented-button state in native analysis cards

- The post-PSG heat-map card uses three ordinary SIButton controls wrapped by
  SIStyleSetter, rather than ButtonGroup or the older active-tab style. Native
  `m_addedClasses` applies the `buttons-segmented-button-row-active-` family to
  the selected leading/middle/trailing button. Read those applied classes from
  the immediate wrapper in the shared ButtonAdapter selection resolver, verifying
  that the class is actually present on the button or wrapper. This preserves
  FM26's original button HTML, selected-state wording and live-value updates.
  It does not turn these buttons into radios or introduce a heat-map-only widget.
- All three phases retained focus and announced exactly one selected button.
  Their actual nine-area intensity tables differed after the native asynchronous
  texture update. The existing texture revision/settle observer already delivered
  those later values without F5; no additional refresh mechanism was necessary.
  The native image confirmed the highlighted phase and displayed heat map. The
  opened card had no WCAG-tagged axe findings or JavaScript errors.

### Playback crowd moods

- FM27 removed HomeCrowdMood/AwayCrowdMood from MatchState and exposes them on
  Match.m_matchFrame. Reuse the existing MatchConditions composer and Alt+4
  double-tap route with this native input; its enum-word formatting handles the
  expanded native vocabulary without a caption/state allowlist.
- Before kickoff the frame reported two IdleNeutral defaults while the native
  mood controller already reported IdleExcited for the away crowd. Do not expose
  that unplayed frame as a real mood reading. Require HasStarted and a positive
  playback slice; absent/unplayed data retains the original unavailable marker.
- In Brighton v Arsenal, at paused slice 805, both the actual frame and the
  controller's query for that same slice returned IdleNeutral/IdleExcited. The
  inherited readout was unavailable; the adapted endpoint and actual double
  Alt+4 returned those two moods. At played slice 820, following Batlle's goal,
  both sources returned GoalConcededMajorDisappointment/Celebration and the
  adapted readout changed accordingly. The diagnostic conditions probe now reads
  the same frame and includes its slice and HasStarted flag. Nothing reads mood
  history at the simulation frontier or caches previous game values.

### Match-report club rows and bounded list expansion

- The competitive Brighton interval screenshot showed club names in the live
  table, while the inherited report reader returned empty team cells. FM27 moved
  each caption from TeamName into that same row's TeamBlock/primary-text link.
  Read the native team slot as a fallback, retaining the original table and row
  headers. A single bound entity link uses the existing Grid.RowActions and
  SITextLink part adapter; no separate table/button renderer is needed. Spurs
  opened the actual club-profile modal, and closing it restored the exact origin.
- The actual VirtualisedList reported ActiveView 0–9, Count 9, fourteen source
  items, OverprovisionPercent 1400 and OverprovisionEnabled false. Its End is
  exclusive. Enabling its native overprovision switch and requesting SetView(0,
  total) realised all fourteen clubs. The shared ListWindowSurface now uses this
  exclusive-end contract for range notes, completeness and paging availability,
  and enables overprovision for the existing bounded expansion. Keep the current
  small-list limits and the separate unchanged fifty-row StreamedTable harvest.
- Native realisation/name resolution updated the production table without F5.
  All fourteen clubs and points were present, the incomplete-window note retired,
  and the opened table had no WCAG-tagged axe findings. The initial name-only
  reader correction did not itself solve disabled overprovision; both native
  differences were investigated and verified separately.
- TeamTalkPanel's comp-icon repeats the adjacent CompetitionName in BottomInfo.
  The shared image classifier omits this badge only while the written native
  caption is visible and populated, preserving the competition link and text.

### Commentary-window regressions at period boundaries

- A full-speed Brighton interval reproduced a spurious goal-for cue at second-half
  resumption, without a new goal or replay. The actual kickoff and halftime
  substitution were new events 799 and 798; the earlier scored goals remained in
  the same commentary log. The previous dead-period audio-slice gate was not
  sufficient for this transition.
- The five native event scans now reject an uninitialised index or a live index
  lower than their supplied bookmark, returning Ok false so FM26's original
  PlayedEventCursor stays untouched. Actual replay windows deliberately retain
  rewind behavior. A shared pure policy and tests exercise period rebind, the
  next genuine goal, replayed goals and connection baselining. The deployed
  readers were also called with their real native high-water and a controlled
  later bookmark: all five accepted baseline/current windows and rejected the
  regressed window. A second owned Brighton run then crossed the whistle, halftime
  and new kickoff at normal speed, continuing to 48:00 without a reload. The live
  index regressed from 865 to zero in the same Match object. The old goal was not
  replayed: only the new restarts, corner and defensive events sounded, agreeing
  with the played native event range and actual decoded buffer starts. No replay
  or fast-forward boundary occurred during this sample. The normal-speed interval
  regression is closed; extra-time and shootout boundaries remain separate checks.

### Empty overflow status records after a withdrawal

- Fox's withdrawn tactics row persistently read couldn't read, including after
  reopening, while the native image showed its ordinary substitution badge. The
  visible bound record correctly supplied Substituído and the game's own reason.
  An overflow record retained StatusFlag 131/StatusType 7 with explicitly empty
  StatusTitleShort, StatusTitleFull and StatusReason fields; it had no independent
  caption or reason to expose. The same case occurred after withdrawing Smith.
- Distinguish those three explicitly empty native strings from absent properties,
  null/unreadable values or an unnamed flag that still has a reason. Skip only
  the wholly empty overflow record; retain the original complete flag reader,
  error behavior and StatusFlagsHtml. The visible substitution stayed readable,
  Smith's on-pitch yellow remained intact, and both withdrawn rows subsequently
  read their native substitution status with no unreadable marker. No status-ID
  or player-name allowlist was introduced. Native zero-minute wording in those
  reasons was preserved rather than replaced with an invented corrected minute.

### Sound bookmarks across consecutive native matches

- Loading the owned pre-match checkpoint preserved the browser's SSE connection
  but created native MatchHandle 4 in a different Match object. The first played
  frame had slice 1 and commentary index 0, while the stream still supplied the
  preceding match's bookmark 1719. The native-window guard correctly refused that
  range, exposing a separate lifecycle bug: a connection's bookmarks were not
  scoped to the match they described.
- MatchEventWindow now owns the six unchanged FM26 one-shot state machines and
  clears them when the actual native Match Pointer changes, including leaving
  for the career. The main-thread snapshot returns that identity alongside its
  other values. Failed dispatches retain the last identity; a timeout must not
  masquerade as leaving the match. Ignore the first scans made with the previous
  owner's bookmarks and baseline the following read normally. Period breaks and
  replays keep their same native Match object and their original bookmarks.
- A pure regression carries goal index 1719 through a same-match halftime,
  leaves for no match and starts another match at index 0, where its first goal
  can sound. After the controlled Plugin reload, the new Brighton match's first
  goal was Tvedten at 25 minutes, assisted by Krezyman: the score, inherited
  readout, live goal-against SSE and decoded buffer start agreed. That reload
  itself reconnects SSE; distinguish this native new-goal check and the pure
  lifecycle regression from a complete second end-to-end connection-lifetime run.

### xG chart clock units

- Native xG shot records supply MatchCurrentTimeSlice as continuous elapsed
  minutes, including period stoppage time. Captured records placed Batlle's
  4-minute goal at 3.35 elapsed minutes and Maanum's 66-minute goal at 69.2458.
  These are the chart's timestamps, not an already formatted football clock.
- Preserve those native plotted values and FM26's table/rounding contract. The
  shared native reader identifies the first column as Elapsed minute and uses
  the existing chart-note mechanism to explain its continuous clock. Do not
  silently apply the currently playing match's period boundaries to an unrelated
  historical report. The live xG tab displayed the corrected header/note, actual
  chances/totals and native goal result, with no WCAG-tagged axe findings.

### Titles for shared in-match tools

- FM27's unbound MatchTactics and MatchOppositionInstructionsTool panels sit in
  ReportBody beneath the same visible TopBarTacticsButtonGroup. Their internal
  names are not user-facing titles. When the foreground tool has no bound title,
  read the unique selected native button through ButtonAdapter. Reuse that title
  for the HTML heading/document title and speech override. Bound titles retain
  priority; unrelated cards and overlays do not borrow this toolbar's caption.
- Both Tácticas and Instruções sobre oponentes followed their native selected
  button. Applying the actual coach advice changed Vanegas' closing-down value
  from a dash to Nunca, generated the pending-instructions state and retained
  Nunca after the next stoppage and halftime reopening. This is execution/value
  evidence for Aplicar Conselhos, separate from the earlier Dugout advice click.

### Heat-map tiles, card headings and fixture outcomes

- The populated Data Hub Last Match tile retained FMHeatMap's template grid
  fields as 100 by 65, but both its actual rendered texture and bound dataset
  were 65 by 100. The full card correctly set its grid fields to 65 by 100.
  Comparing the dataset against the template fields rejected only the compact
  drawing, leaving an unknown-control stop. Read the actual texture dimensions
  and validate every bound column/row against them; retain bounds, numeric and
  intensity validation and the existing nine-area table/formatter. Both native
  views then returned identical Maanum percentages without F5 or invented data.
- When an unbound BaseCardTemplateImpl chart card supplies its heading in the
  shared fixture-details-frame, read its unique visible caption excluding
  teams-and-dates-frame and action buttons. This titles the HeatMapsCard as the
  actual Mapa Térmico on both surfaces, without borrowing a team or summary.
  The original close action returned focus to its exact tile button. The card
  and compact report had no WCAG-tagged axe findings or JavaScript errors.
- Shared fixture tiles displayed FixtureResultIcon rather than their outcome.
  Actual win_small_3x and draw_small_3x assets matched the two away wins and home
  draw in the QA agenda. Scope the established win/draw/defeat asset families to
  that informative result slot and reuse the existing asset-word formatter.
  Do not remove the icon as decorative or infer the result from a score whose
  home/away perspective may differ. Win and Draw were verified in the live UI;
  a live defeat variant remains distinct from the inherited defeat-family mapping.

### Unconfirmed dropdown transport results

- A real squad regression lost the /dropdown/select response while opening Save
  As. The browser recorded ERR_CONNECTION_TIMED_OUT and an uncaught Failed to
  fetch from FM.dropdown.resolve. The native surface remained Live/idle. Reflection
  across retained Plugin assemblies found only the current AccessibilityServer
  running; thirty sequential browser /health reads all reached the same module.
  These checks do not establish the underlying intermittent connection failure's
  cause, or prove that every timed-out command did or did not execute.
- Preserve the original dropdown component, pending selection and Escape path.
  Catch selection/expansion/more/collapse transport failures, log them and report
  that the result could not be confirmed only while the owning session remains
  current. Do not clear a new screen's session, claim success or automatically
  replay a native command. The existing browser driver now records failed-request
  timing data to distinguish a future connection failure from a native dispatch.
- If the selection reply is lost, Escape must also re-read the native value.
  The game may already have committed it; the pre-request data-orig is then not
  a truthful value to restore. Track only that uncertain session and use the
  existing hard-refresh/focus path after close so ignoreActiveValue cannot pin
  its old value. Normal successful commits and ordinary cancellation retain their
  inherited behavior. A fresh successful open/commit clears the transport marker.
- The career regression had also assumed that Portal's compact inbox and the
  full Messages destination had identical realised row counts. Captured evidence
  showed nine compact rows becoming forty full rows while the unread count fell
  and the opened subject disappeared correctly. Assert native unread count,
  subject removal, read-state return in All and focus, rather than row-count
  equality across those different native contexts. No production inbox behavior
  was changed to satisfy that test. Failure snapshots retain context and traces.

### FMFC card title

- The FM27 FMFCJoinSquadCard leaves PanelID.Title unbound. Its TitleHeader contains
  only the image's close button; the actual written heading begins the content
  Frame below that image. Reuse the existing TitleFromScreen mechanism to read
  Junte-se à Equipa from that slot, preserving the reward text, explanation and
  both actual login actions. No login/authentication was submitted for this check.

### Modal return focus during entity-name updates

- Cancelling the actual holiday dialog returned to the same Portal panel,
  binding and template, but focus was empty. The stored origin was keyed by
  names:11 and the returned page used names:15; the name-resolution revision
  described content arriving in that same native screen, not a different origin.
  The same mechanism could affect other dialogs using the shared position map.
- Use the existing screenIdentity helper for all five reads/writes of FM.core.pos.
  Preserve the complete FMTOKEN for native commands and keep actual panel,
  binding, template and overlay changes distinct. This changes focus bookkeeping,
  not the action protocol or the HTML/component contract.
- A remembered native-menu leaf initially sits in a collapsed group in the
  returned HTML. Reuse the navigator's existing focus repair after restoring its
  filter/expansion; focusing the still-hidden leaf before that hook cannot work.
  No per-dialog origin map or alternate navigation renderer was introduced.
- The career regression opens the real holiday dialog from its filtered native
  navigator leaf and cancels it. Real return bodies have only their name revision
  varied to reproduce the content callback; the exact leaf must regain focus.
  The actual server token is restored before any subsequent native command.

### Processing-calendar structure and compact event content

- The visible processing screenshot shows weekday columns and native week rows,
  but the old walk emitted a flat run of dates/events. FM27 uses
  FM.UI.Widgets.Calendar with an authoritative m_rows collection, not DateGrid or
  the older streamed Weeks list. Each row still has WeekDays and seven named
  weekday remappers containing CalendarDayBindingExpect. Read that structure in
  the shared CalendarTree and CalendarContext, validating unique weekday slots.
  Feed the existing CalendarItem/RenderCalendar/CalendarScheduleHtml unchanged.
- Compact events separate icon-only content from their EventLinkSwitch hit
  button. Walk their complete event content inside each original day cell;
  collecting only SIButtons would discard training categories and badges.
  Retain the header's native excess-event count. Reuse the existing caption-
  equivalence principle to omit duplicate plain captions only when one bound
  button carries the complete same text and the caption has no separate state,
  menu or help. All independent icons, links, descriptions and commands remain.
- An 8x4 tile is not necessarily a training aggregate. Require its numeric
  SessionType1 for the category path; other events retain SessionName or their
  own TeamOpponent/MatchVenue. Actual physical/general/in-possession/
  out-of-possession/rest/miscellaneous/travel assets were read while processing.
  International flags retain their informative event meaning. No session names
  are invented from category codes or borrowed from another day.
- The registration event's CompetitionIcon names that event's Competition through
  the existing native entity-name requester. Its Licensed/MiddleLayer is the
  badge's background artwork, with a style-setter counterpart in Other; omit
  only that background inside the same compact competition-badge template.
  The actual UEFA W. Champions League caption arrived without F5, as did the
  three youth opponents and senior fixture names. Native current/past day states
  and the return to Portal followed the live clock.
- PersonImage normally remains decorative under the inherited FM26 rules, but
  an international compact event identifies its first involved player solely by
  that portrait. Its own Event.Player is the source of the missing identity.
  Promote only this compact-event portrait and use the same verified entity-name
  requester. The original unreadable marker remains while a name is pending.
  A menu is exposed only if the shared native context-menu scope accepts it;
  it uses the existing menu-only button interaction, without inventing a profile
  command or making other decorative portraits interactive.
- The live portrait check matched Event.Player and the portrait's own Person
  alias (16822687) before the requester resolved Cecily Wellesley-Smith. The
  existing SSE refresh then exposed that name in the calendar without F5. In
  this native processing context HasContextMenu was false: the portrait remained
  an informative image, not an invented player-profile button. The calendar's
  existing seven-column HTML and WCAG-tagged axe check were retained.
- Processing is asynchronous. The captured first frame still contained pending
  names and provisional buttons; the settled calendar resolved them. A newly
  received message subject was also briefly unreadable during binding. Retain
  that observation separately from the resolved calendar rather than treating
  provisional content as evidence of complete instantaneous screen coverage.

### Minimum action target size in message attachments

- The real tactical-advice attachment places two native action buttons beside
  each other in each table row. The browser's 21-pixel default height caused
  eighteen WCAG 2.2 target-size findings, despite all controls having labels and
  working keyboard semantics. The previous minimum-size rule covered only tabs.
- Apply that existing 24-by-24-pixel minimum to buttons generally. Preserve the
  original table, element types, native captions, commands, focus and keyboard
  contracts. The same attachment then measured nine next-match buttons at
  24 pixels high, with no WCAG-tagged axe findings; no advice was applied merely
  to test its sizing.

### Training-session headers and coach assignment summaries

- TrainingSessionSelectCard draws SessionIcon beside SessionName in SessionHeader.
  The physical-training sprite was exposed as the internal SessionIcon name.
  Apply the existing decorative-caption rule only while that same header has a
  visible, bound session name. Session choice, descriptions and impact tables
  retain the original form and calendar HTML.
- The original TryEmitColumnSummary/ColumnSummaryItem/RenderColumnSummary already
  provide a category-labelled table for coach workload and lead-coach ratings.
  FM27 removed the generated value-frame names and leaves the BindingExpect
  unnamed. Recognise its actual type and validate the replacement component:
  two summary labels, one value component and category columns each containing
  a workload gauge and star widget. Resolve the unique sibling StreamedTable by
  native type when its old name is absent. The renderer remains unchanged.
- The workload glyphs use the same fill for different written workload bands.
  Their generic field-label resolver also borrowed the preceding category's word.
  Omit only the styled glyph with its own visible coach-*-workload-text caption;
  preserve that official word and keep unrelated gauges unchanged.
- The consolidated table captures its cell strings, so its native late updates
  need the original folded-content/action-settle gate. Include only this validated
  summary's captions and star bounds in that signature, not ordinary control
  checked states. Actual assign/unassign changed native Forte/Mediana while the
  old browser cell stayed stale; the adapted gate delivered both transitions
  without navigation or F5 and retained checkbox focus.

### Inline native links in ordinary paragraphs

- The actual MessageBodyText for the Brighton result contains an enabled native
  person link for Frida Maanum, but the generic text renderer stripped its action.
  Reuse SITextLink.ReadParts and JoinedTextItem beyond fact cells for ordinary
  linked text. Preserve the original headings, inert pre-career previews, complete
  surrounding sentence, and existing buttons/context-menu dispatch.
- A linked MessageBodyText retains the same focusable paragraph id and
  data-fm-msg-body marker; its child parts use the existing button/span renderer.
  Opening a message still focuses its whole body, not its first link.
- Each part validates its captured native source and embedded record. Use the
  actual UIRecord DatabaseRecordReference identity for its stable target key:
  the SI.Core.Record wrapper address changes on native Back reconstruction.
  Non-database records retain a conservative record-address fallback. A changed
  source/record or newly bound pending text reuses LiveValues' existing stale-
  source recompose path, rather than allowing captured text/actions to persist.
- The body link opened the actual PlayerReport for Frida Maanum. Its official
  page title is Relatório de jogador, not her name; verify the native identity
  banner rather than guessing the document title. Back restored the same message
  and exact link id. A seven-link article paragraph independently opened Brighton;
  Shift+F10 selected Brighton's own club menu, and both Escape and Back returned
  to that exact second link. Both message checks had clean WCAG-tagged axe results.

### Full searchable lists and recycled submenu controllers

- FMSearchableCombobox exposes a real editable input and a separate chevron for
  its explicitly bound full list. Retain the original searchable input. Adapt
  that native chevron's m_fullDropdown to FM26's existing DropdownAdapter,
  StopItem, select renderer and hierarchical session; do not cast the composite
  widget to SIDropdown. Opening uses the composite's own focus/chevron lifecycle.
  The neighbouring Browse control reports the same current field value; Alt+I
  explains that typing searches while Browse opens the native full list.
- Native large menus recycle child SIDropdown controllers with their realized
  rows. The inherited m_childrenDropdowns[index] addressed the controller pool,
  not the absolute choice: Brazil incorrectly opened Panama after harvesting.
  Realize the requested absolute row across game frames and resolve its own
  child SIDropdown before showing the submenu. Preserve the existing HTML,
  absolute choice indices, lazy hierarchy, continuation and cancellation.
- Template containers inside ButtonGroup, SIDropdown, FMSearchableCombobox and
  SIFloatingPopupWidget are control internals rather than page identities.
  Exclude those leaves from the existing root-template frontier. Decorative tab
  templates disappearing on popup open otherwise generated a false screen
  departure, invalidated the original session and stranded a readable list.
- Real checks followed Brazil/P/Porto Alegre (RS) through native selection and
  read-back. Continuation exposed all 228 native countries; the final Zimbabwe/H
  submenu contained Harare and Hwange. Left returned one level; Escape preserved
  Porto Alegre and restored the Browse origin. The QA biography was cancelled,
  preserving its saved profile. Its WCAG-tagged axe check was clean.



## Latest shared corrections — native advice and initial recovery

FM27 native tactical-advice SIButton AcknowledgeSuggestionButton has empty
m_staticText, no BindTextFunction/m_textBindingHandler or tooltip, and an actual
EventToPluginContext carrying EventFmxAcknowledgeAdvice. Extend the EXISTING
NativeButtonCommand.ActionLabel command fallback; native captions retain
precedence. No component redesign or caption/button-name/screen allowlist.
Real commands independently change the same native suggestion row to Ciente,
remove exactly one pending acknowledgment and persist through message reopen.
Acknowledgment and Apply are separate commands; do not infer applied tactics.

The inherited initial DefaultPage catch returned static HTML without app.js,
so it could not recover through the original SSE even if native reads recovered.
Reuse HtmlRenderer.WrapShell with unknown token "?", escaped contextual error
and English mod-only initial language. Original first real SSE token triggers
the existing /body refresh, committing actual native title/language. Keep a
static last-resort error if shell resources themselves fail and log both
failure paths contextually. Controlled actual-builder/real-backend testing is
component evidence, not an induced native exception or an unprovoked crash.
See TESTING.md for exactba/d4ea boundaries and pending shared regression.

## Native set-piece inline records and formatted captions

FM27 SetPieceTakersBlockTemplateLeft/Right instances reference the same actual
templateSource asset SetPieceTakersBlockTemplate. Their native Header and4column
shape differ from FM26's old frame/7column layout. Extend the inherited renderer
with those demonstrated structural differences, retaining its semantic priority
table, contextual native commands, status/focus and optional former attributes.
Use Pointer for native title-element equality. Specialized commands must reuse
HtmlRenderer.RegisterCellHandle rather than rebuilding index-only keys.

The native binding is inline {SetPieceType,SetPieceSide,Index,Player}; it is not
itself an InteropReference. The exact-schema SetPieceTakers.RowIdentity adapter
retains actual PersonReference plus native type/side; mutable ordinal is excluded.
TableContext still tries its ordinary direct reference first. Do not generalize
arbitrary record fields into identity or use a displayed name/current index.
Native held-body stale refusal, actual reorder/removal/inverses and close/reopen
were observed on d21; see TESTING.md for the retained fixture-wording boundary.

Formatted native captions use VisualFunction f97abdf5a74842647ba921d183f4a3eb,
four inputs including constant GenericVisualArg<TranslationID> and
StructListVisualArg<TranslationArg>. Extend existing StaticButtonCaption for
that actual binding contract, returning live SIText.text for formatted captions.
Never reformat gender/player arguments, read an unrealized formatted template
as final text, introduce caption/screen allowlists or replace On/Off values with
captions. This restores3penalty option purposes and both default-taker captions;
actual keyboard native-value inverses are separately recorded.


## Native fractional percentage pairing (F162)

Current Debug game3244/Plugin40b2eedd-b0be-4aab-b3e5-309b3d318d77 now contains the final fractional-progress correction. Native signed-contract154686 shows field caption plus official0,7% consistently in visible text/aria-valuetext, with one colon and clean axe (career-fractional-final-native-contract-passed.json). Actual handler value0.7336319 and misleading drawn-width ratio establish why the official own percentage caption must be reused instead of deriving decimal precision from geometry. Common FieldLabelReader and strict sibling schema preserve FM26 presentation; budget amounts remain separate from native fill. Shared native loading test on predecessor570a observed real SSE0/3/25/95% with exact DOM synchronization; final-module finance/training/history/loading qualification is running sequentially. Pure1594/JS68 rerun in progress/completing; Release package build does not deploy. Previous player221619 is uninstalled and contains F162. F047/F115 still unresolved; no stable/full-career/Steam/NVDA claim.

The old FM26 geometry integer fallback remains only when no demonstrated own native percentage caption exists. Missing geometry now returns a contextual read failure rather than a fabricated zero. No SIProgressBar.SetProgressPercentage, handler Unbind/Release or native monetary write is used. Fifteen pure cases cover localized decimal/Unicode percentage notation and reject money, field captions and composed announcements.

## Self-labelled native actions inside player banners (F163)

A confirmed new defect F163 was found after the completed production route walk: Maddy's real Actions menu and compound ability/potential report button occupy two unnamed scalar-field rows. Both native actions and exact Back/focus work; axe alone did not flag the bad table association. Source now preserves all field order and original controls but uses full-width action cells in the same inherited table for known native menu/own-captioned compound rating actions. Known data anchors win; unknown scalar fields remain diagnostic. Build passes0errors and1594pure tests/loader/player/API fixtures pass0skips. First compile caught a placement error in RenderPitchSummary; fixed before deployment, stopped log retained separately. New live-profile-banner-actions.cjs is now intentionally checking the OLD installed13ae for the expected two-empty-header failure, before corrected cold Debug qualification.

Installed candidate004635 and sourcebundle010441 predate F163; neither is a stable/latest-source release. Their completed13ae scope remains real:29groups/44axe,124destinations/8912controls/124axe and maintained signed-contract fractional-caption regression passed. Current nativegame10704/accepted21, autosaveFALSE, normalcompanionclosed.23originals/32 playerfiles/exact21ownedextras preserved.


## Native goalkeeper-number banner variant (F164)

Actual Debug game15136 now runs Plugin ab704e81-df19-4602-9c97-9fcbef933d5a under updatedreceipt mod-8999b3daed8c422caf1db466cd1b9005.json. F163 self-labelled action rows passed on125684:3 groups/4axe, all15Maddy rows/values/controls/order preserved, native menu/report destinations and exact returnfocus. Its shared125684 menus and training suites passed; the extended fixture driver then passed Poppy banner/profile but correctly STOPPED on Daphne's previously unnamed scalar1. This distinct F164 is actual SIText under native SIStyleSetter GKKit; existing code recognized GoalkeeperKit/OutfieldKit but omitted GKKit. One alias added to the existing Squad Number mapping; no kit-graphics change or number mutation. Direct ab704e81 live check confirms Squad Number:1 and ALL other Daphne rows/values/controls/order unchanged, clean axe. Full shared regression now running; no new-source production qualification inherited.
