> Upstream FM26 Access reference, release 0.2.16. Check [FM27 migration notes](../fm27-migration.md) before using game-specific APIs.

# Tactical Familiarity & Role Proficiency — the Tactics Planner model

Investigation write-up, 2026-07-01. Sources: the decompiled IL2CPP dump (`Decompiled/`), a live probe
session against the running game (2026-06-30), and web research into FM26's design-level mechanics.
Every claim below is tagged with its provenance, because the three sources have very different
reliability and the history of this widget is a cautionary tale about conflating them.

**Provenance tags used throughout:**
- **[decompile]** — verified directly in the decompiled source (file paths given). Field layouts,
  enum values, and signatures are real; method bodies are stubbed (IL2CPP — logic is native).
- **[live]** — observed in the running game via the dev driver (2026-06-30 session).
- **[web]** — community/official sources, with confidence noted. FM26 launched with acknowledged
  familiarity bugs (SI bugtracker r27905, r33149, r36480), so live readings of *sub-components*
  may reflect bugs, not design.
- **[inference]** — our best model; explicitly not evidence.

---

## 1. History: how the hardcode happened

Commit `992ce5b` (Jacob Shymanski, 2026-05-19) added stepper reading for the Tactics Planner
toolbar's Intensity and Familiarity indicators. Intensity reads "filled steps" by checking each
step element's background alpha. Familiarity was observed **not to change when switching tactics**,
which was diagnosed as "the game's stepper binding doesn't reliably update its internal state" —
so it was hardcoded to `4 of 7` (`ButtonAdapter.cs:104`).

**That diagnosis was wrong.** The value not changing across tactic slots is *correct game
behaviour*: Familiarity does not belong to the tactic slot you are editing (§3). The observation
was accurate; the model built from it wasn't. The result is the current state: the companion
surfaces Familiarity as a clickable button (activation is a no-op) announcing a constant fake
value — both a lie under the Golden Rule.

## 2. The two mechanics — do not conflate them

FM26 has **two distinct tactical-readiness axes**, and community confusion between them is
widespread (dedicated SI-forum thread "FM26: Tactical Familiarity vs. Tactical Proficiency",
2025-10-25):

| | **Tactical Familiarity** | **Role Proficiency / Suitability** |
|---|---|---|
| Scope | Team/squad, one value | Per player, per assigned role |
| Applies to | The **primary trained tactic** (not the slot being edited) | The player's current **IP role** and **OOP role** |
| Changes | Slowly — accrues over weeks of training/matches, degrades on big tactical changes | Instantly — recomputed when you change the tactic's settings or the player's role |
| Driven by | Repetition: match prep, friendlies, playing the system | Attributes + positional familiarity (SI: "their positional familiarity and their key attributes for the role in question") |
| UI | The 7-step **Familiarity** bar on the Tactics Planner toolbar | Five-star ratings ("classic five-star system", per SI's feature page), player reports, tactics pitch |
| Decompile anchors | `PlayerTacticalFamiliarityReference`, `ActionID.TeamMaximiseExistingTacticalFamiliarity`, training references | `RoleAbilityRowReference`, `TacticsRoleReference`, `EventFmxRecruitmentFocus{Ip,Oop}RoleProficiencyUpdated` |

The distinction in one line: **familiarity = the squad knows the plan; proficiency = each player is
good at his job within it.** High familiarity with low proficiency (and vice versa) is normal.

## 3. Why the Familiarity bar doesn't move when you switch tactics

This is the crux the original investigation got wrong, and the decompile settles it structurally.

FM26's tactic selector holds multiple slots — one primary and two secondary **[web: Passion4FM,
single-sourced but FM26-specific]** — and two *separate* interop events with *separate* dispatch
channels control them **[decompile: `Decompiled/FM.GamePlugin/FM.GamePlugin/InteropEventIDs.cs`]**:

- `EventFmxSetTacticSlot = 78` — switch which slot you are viewing/editing. Dispatched through
  `ModifyTacticEvent` / `ModifyTacticEventContext` (the tactic-editor channel).
- `EventFmxSetPrimaryTrainingTacticSlot = 88` — designate which slot the squad **trains**.
  Dispatched through `TeamTrainingChangeEventContext`
  (`Decompiled/FM.UI/FM.UI/TeamTrainingChangeEventContext.cs`), whose fields
  (`m_teamTrainingID`, `m_teamTrainingTarget` → a team-training reference) show it is a
  **training-system** operation, not a tactic-editor one.

Familiarity is bound to the trained tactic. Flipping editor slots fires event 78, which never
touches the training designation, so the bar correctly stays put — **[live]** confirmed: the
stepper's value read 8811 across three tactics from different style families. It moves only when
the *trained* tactic changes (event 88, via "Set as Primary Trained Tactic") or as training
accrues/decays it over game time. Passion4FM corroborates the design: the primary trained tactic
is selectable from the Training Overview, "handy if you're considering changing to a different
system" **[web, medium confidence]**.

The **"Primary Trained Tactic"** entry players see in the tactic selector's modify menu is
`ModifyTacticOption.PRIMARY_TRAINED_TACTIC` — one value of the menu enum
(`INVALID, CREATE_NEW, DUPLICATE_SLOT, PRIMARY_TRAINED_TACTIC, EDIT_TACTIC, PRESETS, SAVE, LOAD,
MANAGE, CLEAR` **[decompile: `Decompiled/FM.UI/FM.UI/ModifyTacticOption.cs`]**). It is a
command, not a selectable tactic entry: it designates the tactic you are currently viewing as
the squad's trained tactic (event 88). With a single tactic slot the command is trivially
satisfied — your only tactic is already the trained one — which made it look like an inert
marker in early probing; with two or more slots it acts **[live: user selected it on a
newly-added second tactic, 2026-07-01]**.

## 4. The widget: SIStepper anatomy

The Familiarity indicator is an `SIStepper` (`Decompiled/SI.Bindable/SI.Bindable/SIStepper.cs`)
inside a button-shaped toolbar container with an `SIText` label "Familiarity". Key internals
**[decompile]**:

- `private float m_currentValue` (line 85) — **the real bound model value.** Fed by
  `BindStepper(TypedValue typedCurrentValue)` (line 200), which the visual-scripting node
  `SIStepper_BindStepper` invokes with the bound data value. This is not a visual artifact;
  it is the number the game itself renders from.
- `private List<float> m_stepLimits` (line 61) — thresholds deciding which steps fill.
- `private List<VisualElement> m_steps` (line 59) — the step visuals.
- `FillMode` enum: `UpToSelected`, `SelectedOnly`, `UpToSelectedExclusive`.
- Steppers can be built either with fixed ranges (`CreateStepper`) or data-driven from a
  `DataCollection` column (`CreateStepperFromDataCollection`) — which one Familiarity uses is
  runtime-only.

**[live]** For the Familiarity stepper: `m_stepLimits = [0, 1500, 3000, 4500, 6000, 9000, 10000]`
and `m_currentValue = 8811`. So the scale is **basis points** (0–10000 = 0–100%), the seven steps
sit at 0/15/30/45/60/90/100%, and 8811 means **88.11% familiarity**. No public source documents
these thresholds; this is ahead of anything on the web.

**Why the alpha-counting heuristic (and the hardcode) read the wrong layer:** the mod's generic
stepper reading counts "filled" step elements by background alpha (`ButtonAdapter.CountFilledSteps`).
That reads the *rendering* of a threshold display, loses all precision between thresholds (88.11%
and 60.01% both show 5 filled steps at best), and depends on styling that evidently differs for
this stepper. `m_currentValue` is the actual value; read it.

**Familiarity is passive; Intensity is interactive.** The neighbouring Intensity indicator opens a
card when clicked (`TacticalPlannerTacticalIntensityCard` — observed **[live]**; the type does not
appear in the decompile, so it is asset/graph-defined). Familiarity has no click command — it is a
read-only computed output. Input vs output: you *set* intensity; you *earn* familiarity.

## 5. Design-level semantics (web research, with confidence)

- **Accrual** *(medium)*: weeks of playing/training the system — tactics-focused match preparation,
  pre-season friendlies ("several weeks for the familiarity bar to become fully green", recharge.com;
  6–8 weeks of pre-season guidance, Passion4FM). Whether specific training sessions (Match
  Practice/Match Tactics/Match Review) raise it in FM26 is **contradicted between sources** and
  muddied by launch bugs — unresolved.
- **Update cadence** *(live-verified 2026-07-01)*: the reported value refreshes on a simulation
  tick (daily processing), not instantly. Redesignating the primary trained tactic to a brand-new
  system left the bar at its old value until game time advanced. The stepper is push-bound — it
  shows whatever the core last published — so the mod's read and the sighted bar lag identically;
  expect the same cadence for any training-derived value the mod surfaces.
- **Decay** *(low-medium)*: large sudden tactical changes (formation, style, roles, instructions)
  degrade it; new signings arrive unfamiliar. Unexplained decay with an unchanged tactic is a
  *reported beta bug* (bugtracker r36480), not a mechanic.
- **Sub-components** *(medium, bugtracker snippets)*: FM26 retains an FM24-style detail view —
  overall status word (e.g. "Excellent") plus category "power bars" (passing style, tempo,
  pressing, marking, …) using the old level ladder (e.g. "Awkward"). The full FM26 category list
  and ladder are not publicly documented. Launch-window bugtracker threads report the category
  bars misbehaving while the overall bar stays green — so the overall trained-tactic percentage
  (what our stepper read targets) is the value the game itself treats as authoritative.
- **Coach directive** *(decompile)*: `ActionID.TeamMaximiseExistingTacticalFamiliarity`
  (`Decompiled/FM.GamePlugin/FM.GamePlugin/ActionID.cs`) — training can be pointed at maximising
  familiarity with the existing setup.
- **Training intensity → familiarity gain rate**: no source answers this; the decompile has a
  stubbed `TrainingIntensityConditionReference` but nothing linking it to familiarity.

## 6. Role Proficiency (the second axis) — what we know so far

- SI's own feature page (footballmanager.com, "Possession & Out of Possession: FM26's new tactical
  evolution) states role suitability is rated with the **classic five-star system**, built into
  player reports, driven by **positional familiarity + key attributes** **[web, high]**.
- FM26 individual training has three per-player slots — IP role, OOP role, attribute focus — so
  proficiency inputs are trainable **[web, medium-high]**.
- Decompile anchors: `RoleAbilityRowReference`, `TacticsRoleReference` (both property-stubbed);
  `EventFmxRecruitmentFocusIpRoleProficiencyUpdated = 960968525` /
  `...OopRoleProficiencyUpdated = 1449946478` (recruitment-focus update notifications — the
  mechanic itself is not recruitment-only); the dual role enums `InPossessionRole` (~60 values) /
  `OutOfPossessionRole` (~40 values); per-position pitch tokens are `FMPitchIndicator` with role
  menus (`OpenRoleCard`/`ChooseRole`/`OpenPlayerInstructions`) and an `m_inPossessionKey` phase
  flag; `TacticsPositionCombinedReference` is the likely binding target of the "Both" view.
- **Unknown**: how proficiency renders on the pitch tokens (stars? colour?), whether the FM26 UI
  ever literally says "Proficiency", and the exact bound property names — all need a live DataRef
  dump. The mod currently surfaces none of this.

## 7. What only a live probe can answer, and how to probe safely

The decompile's property registry (`PropertyIdentifierSet.CreateInfo` returns null — stubbed) and
all `IDataReference` property tables are runtime-only, and there is no STRINGS export in the dump.
Offline research is exhausted; the remaining unknowns and their probes:

1. **Exact bound property behind the Familiarity stepper** (team-aggregate vs
   `PlayerTacticalFamiliarityReference` roll-up): dump the widget's DataRef property names via the
   `LeagueHistoryChartReader` pattern (`DataRef → PropertyIdentifierSet.GetInfo(key).Name`).
   This refines the *label* only; `m_currentValue` already gives the right number.
2. **Proficiency fields**: DataRef dump on a specific `FMPitchIndicator`/role-card cell.
3. **Familiarity sub-category bars**: find whether the detail view exists in our UI tree and reads.

**Probe safety (hard-won):** never BFS from the Tactics Planner panel root — the deep pitch tree
hard-crashes `/eval` (error bursts audible to the player; see `docs/` memory: eval-deep-tree-crash).
Resolve the toolbar element by its companion handle id via reflection into `ServerHandles._handles`
(`StopHandle.El → PluginAdapterBase.BackingElement`), then a tiny bounded `FindDescendant` for the
`SIStepper` under just that element.

**Probe hazard (2026-07-01):** reading `BindingRemapper.Mappings` (an Il2Cpp `List<RemapInfo>` of
serialized structs) through the `/eval` evaluator threw a managed NRE **and raised two native
application-error popups** on the player's machine, without landing anything in `/log`. The
evaluator cannot safely marshal that generic struct list. Do not repeat via `/eval`; if the exact
bound property name is ever needed, read the mappings from a compiled helper inside the Plugin
(hot-reloadable) instead. Note this is label-precision only — the fix does not depend on it.

## 8. The mod's surfacing (fix implemented 2026-07-01)

The old state (both wrong): the companion rendered Familiarity as an activatable **button**
(activation was a no-op), and `ButtonAdapter.TryGetStepperValue` hardcoded `"4 of 7"` for the
label "Familiarity".

Implemented in `FM26Access.Plugin/Adapters/FamiliarityAdapter.cs` (dispatched from
`ElementBridge.CreateButtonAdapter`), replacing the deleted hardcode:
1. Reads `SIStepper.m_currentValue ÷ 100` → announces/renders **"Familiarity, 88%"** — a
   read-only value with no activation affordance (role `text`, `CanActivate` false; the
   companion's default read-only branch renders and live-tracks it).
2. Guards the percentage claim: the basis-point scale is verified against the live
   `m_stepLimits` (max must be 10000) on every read; a rebased stepper degrades to the honest
   `unavailable` marker instead of a possibly-wrong number. Missing stepper/label likewise
   degrade in place.
3. Uses the game's own label (the child `SIText`) + the percentage. It is deliberately **not**
   labelled "familiarity of this tactic" — it is the trained tactic's value (§3).
4. Known limitation: the widget is recognised by its "Familiarity" label text — the only
   discriminator recoverable offline. Other locales fall through to the generic button
   presentation (honest visual step count). Replace with the element/template name once a live
   toolbar probe records it.
5. Intensity and other steppers keep the visual filled-step read; migrating them to
   `m_currentValue`-derived precision is optional future work.

Verification pending a live session: confirm the adapter attaches on the Tactics Planner
toolbar, the announced % matches the bar, and the companion shows plain text, not a button.

Role proficiency surfacing is a separate, later deliverable — blocked on the live DataRef dump (§7).

## 9. The toolbar's two tactic menus (live-verified 2026-07-01)

The Tactics Planner toolbar carries two adjacent dropdowns that read similarly but split the
`ModifyTacticOption` enum between them:

- **Under `tactics-picker-focusser`** (mod label **"Tactic options"**, formerly the misleading
  "Tactic selector"): the *current slot's* command menu — `Primary Trained Tactic / Edit Tactic /
  Presets… / Save / Load… / Manage Tactics… / Clear Slot` (enum values
  `PRIMARY_TRAINED_TACTIC, EDIT_TACTIC, PRESETS, SAVE, LOAD, MANAGE, CLEAR`). Seven entries —
  the source of the old misleading "N of 7" readout (menu position, not tactic count).
- **Under `tactics-picker`** (mod label **"Add tactic"**, formerly "Manage tactics"): the
  *add-a-tactic* menu — `Create New Tactic / Load Tactic / Copy Tactic` (`CREATE_NEW, LOAD,
  DUPLICATE_SLOT`; the `ModifyTacticEvent.IsAddingSlot` path).

The old profile labels were guesses and actively harmful: the box labelled "Manage tactics" did
not contain the "Manage Tactics…" option — that lives in the other box.

**"Manage Tactics…" opens nothing from the Tactics Planner.** Triple-verified live: (a) the
mod's `TriggerSelectionEvent(5)` fires cleanly, no dialog; (b) the game's own keyboard
navigation cannot reach the entry at all (`FMNavigationManager`'s focus chain stops at "Load…" —
the two rows below are not navigable); (c) a synthetic pointer click at the row's real screen
rect also opens nothing. Do not re-litigate the mod's selection path — Load/Save/Presets/Copy
all work through the identical mechanism.

**What MANAGE maps to is an open question.** An earlier hypothesis tied it to
`ModifyTacticHandlerModule.HandleManageTeamSelectionsFromPlugin` /
`EventFmxManageTeamSelection` — that attribution is WRONG: in `InteropEventIDs` that event sits
in the saved-**lineup** group (`SaveTeamSelection`, `Select/Delete/RenameTeamSelection`,
`QuickPick*`), i.e. it is the squad screen's "manage saved team selections" dialog, unrelated to
`ModifyTacticOption.MANAGE`. For MANAGE itself the managed layer shows **no dialog asset and no
handler at all** (`ModifyTacticHandlerModule` carries dialogs for Save/Load/Create New only);
its dispatch lives in the stubbed `ModifyTacticEvent` callback / native side. Live candidates,
untested: the entry is context-gated (works only from the full-screen team-selection surface,
which hosts the same menu pair), or it is unimplemented/vestigial at launch (consistent with the
game's own navigation not reaching it). Byte-level ISIL of the ModifyTacticEvent callback would
settle it (Cpp2IL not currently on disk; see docs/guides/decompilation-guide.md).

## File manifest (decompile anchors)

| What | Where |
|---|---|
| Stepper widget (value, limits, fill) | `Decompiled/SI.Bindable/SI.Bindable/SIStepper.cs` |
| Stepper binding node | `Decompiled/SI.Bindable/SI.Bindable/SIStepper_BindStepper.cs` |
| Modify-tactic menu enum | `Decompiled/FM.UI/FM.UI/ModifyTacticOption.cs` |
| Interop events (78, 88, proficiency) | `Decompiled/FM.GamePlugin/FM.GamePlugin/InteropEventIDs.cs` |
| Slot-switch event payload | `Decompiled/FM.UI/FM.UI/ModifyTacticEvent.cs`, `ModifyTacticEventContext.cs` |
| Trained-tactic (training channel) | `Decompiled/FM.UI/FM.UI/TeamTrainingChangeEventContext.cs` |
| Familiarity references | `Decompiled/FM.UI/FM.UI/PlayerTacticalFamiliarityReference.cs`, `TacticReference.cs` (+ Team/Competition/Match subclasses) |
| Familiarity coach directive | `Decompiled/FM.GamePlugin/FM.GamePlugin/ActionID.cs` (`TeamMaximiseExistingTacticalFamiliarity`) |
| Proficiency references | `Decompiled/FM.UI/FM.UI/RoleAbilityRowReference.cs`, `TacticsRoleReference.cs` |
| Dual-role enums | `Decompiled/FM.UI/FM.UI/InPossessionRole.cs`, `OutOfPossessionRole.cs` |
| Pitch position token | `Decompiled/FM.UI/FM.UI/FMPitchIndicator.cs`, `FMTacticsPanel.cs` |
| "Both" view reference | `Decompiled/FM.UI/FM.UI/TacticsPositionCombinedReference.cs` |
| Team instructions model | `Decompiled/FM.UI/FM.UI/TacticTeamInstructions.cs`, `TacticTeamInstructionOptions.cs` |
| Property registry (stubbed) | `Decompiled/SI.Bindable.Reference.Core/SI.Bindable.Reference.Core/PropertyIdentifierSet.cs` |
| The hardcode to remove | `FM26Access.Plugin/Adapters/ButtonAdapter.cs:104` (commit `992ce5b`) |
