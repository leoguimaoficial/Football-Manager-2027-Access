> Upstream FM26 Access reference, release 0.2.16. Check [FM27 migration notes](../fm27-migration.md) before using game-specific APIs.

# Out-of-play detection — event-driven throw-in / corner / goal-kick signals

Research for replacing the geometry-based out-of-play sound triggers (the touchline "double tick" and
goal-line "triple tick") with the engine's own event stream. Sibling of
[goal-detection.md](goal-detection.md), which made the same migration for the goal sound — the machinery
and the sync discipline carry straight over. Focuses on one question: **how does the mod know the ball has
gone out for a throw-in / corner / goal kick, reliably and at the right moment, and can that also tell
the on-demand location/possession readouts (the Alt+5 chord) which restart it is?**

## The problem with the current trigger

Today the out-of-play sounds fire from **ball geometry**. `MatchBallLocation.ComputeMatchAudio` classifies
the live ball against the pitch boundary (`BallLocation.ClassifyOutOfPlay` → `OutOfPlayKind`
`{InPlay, Touchline, GoalLine, Net}`), and `SseStreamer` (`SseStreamer.cs`, the `!inReplay` block near line
443) fires `touchline` on the `InPlay → Touchline` edge and `goalline` on the `InPlay → GoalLine` edge,
sampled at ~2 Hz. The on-demand readouts (`MatchBallLocation.OutOfPlayLocation` / `DescribePossession`)
read the same geometry.

Two structural limits:

- **It cannot tell a corner from a goal kick.** Both cross the *same* goal line; the only difference is who
  touched the ball last, which geometry does not know. So the goal-line cue and the location readout collapse
  both into one vague "out of play, ... of the goal" — the single biggest information loss in the current
  design.
- **2 Hz sampling is lossy**, exactly as it was for goals: a fast ball can cross and be back in a taker's
  hands between samples, and `OutMargin` / boundary calibration is a proxy for a line the engine draws
  exactly.

The engine does not make us guess. It emits a structured event the moment the ball goes out, tagged with
*why* (throw / corner / goal kick) and *which team*.

## The engine knows. The event families.

`Match.m_commentaryEvents` : `Il2CppSystem.Collections.Generic.List<MatchCommentaryEvent>` is the same
structured stream the goal detector already reads in `MatchBallLocation.ScanGoals`. Each element is a
`MatchCommentaryEvent` carrying a `CommentaryEventID` (`SI.MatchTypes.CommentaryEventDefinitions`), a
`TimeSlice`, `Team`, `PlayerTeams`, `PlayerUniqueIDs`, and a `Data1`. Three families of ids name an
out-of-play restart directly (values are the `short`-based enum in
`decompiled/SI.MatchTypes/SI.MatchTypes/CommentaryEventDefinitions.cs`):

### A. "Ball just went out", with the reason — the best timing match

The closest analog to today's geometry edge: these mark the moment the ball leaves the field and say why.

- `EventSeeBallOutOfPlay` — generic
- `EventSeeBallOutOfPlayForGoalKick`
- `EventSeeBallOutOfPlayForThrow`
- `EventSeeBallOutOfPlayForCorner`
- `EventPlayBallOffOpponentForGoalKick` / `...ForThrow` / `...ForCorner` — the ball went out off an
  opponent's touch (relevant to team attribution — see below)
- `EventClearBallIntoTouch` — a deliberate clearance out for a throw

### B. Restart events — fire at the restart (slightly later)

- `EventRestartThrowIn`, `EventRestartCorner`, `EventRestartGoalKick`
- (siblings for the non-out-of-play restarts: `EventRestartFreekick`, `EventRestartPenalty`,
  `EventRestartOffside`)

### C. Take events — fire when the taker actually plays it (later still)

- `EventTakeThrowIn`, `EventTakeThrowInNoDest`, `EventTakeGoalKick`, `EventTakeCorner`,
  `EventTakeShortCorner`, `EventTakeCornerToEdgeOfBox`

For a sound that fires the instant the ball leaves the field, **family A is the right trigger**; families B
and C are useful corroborators (and the `Take*` events carry the taker in `PlayerUniqueIDs`).

There is no `IsOutOfPlay` classifier extension (unlike `IsAnyTypeOfGoal`), so match the specific enum ids
directly. That is fine and more transparent — the ids are canonical.

## What the event carries (and what it does not)

`MatchCommentaryEvent` (`decompiled/FM.Match/FM.Match/MatchCommentaryEvent.cs`):

| Field | Type | Use for us |
| --- | --- | --- |
| `CommentaryEventID` | `CommentaryEventDefinitions` | the restart type → **implicitly which line** (throw = touchline, corner/goal-kick = goal line) |
| `Team` | `FixtureTeam` (`Invalid=-1, Home=0, Away=1`) | which team the event pertains to — **semantics need a live probe** (team awarded the restart vs team that conceded) |
| `PlayerTeams` | `MatchEventPlayerTeams` (`None`, `VisitorTeamPlayerN` bitflags) | home/visitor discriminator for the players involved (the goal detector already uses this as a `.Team`-Invalid fallback) |
| `PlayerUniqueIDs` | `int[]` | the players involved — e.g. the taker on a `Take*` event |
| `Data1` | `int` | opaque payload — **probe it**; may encode a side/zone and save a geometry read |
| `TimeSlice` | `int` | for playback-window sync |

**There is no raw pitch coordinate (no X/Z) on the event.** So the event tells us *what* restart and *who*,
never the exact spot. That is the crux of the design below.

## The design: a hybrid, not a replacement

The event and the geometry are complementary, so use each for what it is authoritative about:

- **Event** → the *trigger*, the *restart type* (throw / corner / goal kick — the thing geometry can't
  resolve), and the *team*.
- **Ball geometry** (`GetBallPosition`, already read) → the *precise location* — the third, flank, and
  distance that drive the location/possession phrasing.

This mirrors goal detection, which kept `SseStreamer`'s replay-freeze and fire-once latch while swapping the
*trigger* from geometry to `EventGoalScored`. Keep ball geometry as the **fallback** for coverage (see
caveats): degrade in place to the geometry cue if the event stream is silent, never lose the sound.

### Where it plugs in

1. **The SSE one-shot sound** (`SseStreamer.cs`, the `OutKind` edge → `touchline` / `goalline` sfx). Switch
   the trigger from the geometry `InPlay → Touchline/GoalLine` edge to "an out-of-play event entered the
   playback window", and **split the single `goalline` token into `corner` and `goalkick`** — a strict
   accuracy gain the player hears. Extend `ScanGoals` into a sibling `ScanRestarts` (or one combined scan)
   returning the most recent in-window out-of-play event `{Kind, Team}`; all the window/replay/latch
   machinery already exists.

2. **The on-demand readouts** (`MatchBallLocation.OutOfPlayLocation` / `DescribePossession`, the Alt+5
   chord). Cache the last out-of-play event and let it name the true restart while geometry supplies the
   location:
   - location: **"corner to opposition, attacking third right"** (event → `corner` + team; geometry →
     `attacking third right`) instead of today's geometry-only "out of play, right of the goal", which
     cannot say corner vs goal kick.
   - the taker: corroborate or replace the current "nearest player" heuristic with
     `PlayerUniqueIDs` off the `Take*` event.

## Sync discipline (inherited from goal detection — mandatory)

The full `m_commentaryEvents` list is the **simulation frontier** (events exist ahead of playback — a
spoiler if read whole). The goal work proved the safe pattern, and it applies identically here:

- Scan only up to `m_currentLastEventIndex` (the **playback high-water**), not `list.Count`.
- Fire when a target event **enters** the played window `[CurrentFirstEventIndex .. m_currentLastEventIndex]`,
  not when it merely exists.
- Gate on `!inReplay` (`CurrentMatchPlaybackMode`): a replay rewinds the window and re-crosses events, so an
  ungated trigger double-fires. `SseStreamer` already freezes the one-shot detector during replay.
- **Latch once** per event (the caller holds the high-water and only advances it while live). A reconnect
  mid-match must baseline the high-water silently (the `afterIndex < 0` path in `ScanGoals`).

## Open questions the probe must answer

Before committing, `scripts/out-of-play-probe.ps1` (a background monitor, same shape as the goal monitor)
must confirm against a live match:

1. **Timing / sync.** Do `EventSeeBallOutOfPlayFor*` actually *enter the playback window* at the visible
   moment the ball goes out (as goals did), or are some of them silent pre-events that appear at the
   frontier only? This decides whether family A is the trigger or we fall back to B/C.
2. **`Team` semantics.** Is `Team` the side *awarded* the restart (takes the throw/corner, or the goal kick)
   or the side that *conceded* it? Probe at a throw-in whose direction you can see. This sets the
   attribution mapping the same way `.Team` did for goals.
3. **`Data1`.** Dump it across several restarts — it may encode a side/zone (and could reduce or replace the
   geometry read).
4. **Coverage between highlights.** Geometry works whenever `GetBallPosition` returns; the event stream is
   tied to playback. If a throw-in happens in an un-highlighted passage, does the event still land in the
   window? If not, geometry stays as the fallback — which is the recommended posture regardless.

## Empirical verification (live, 2026-07-05)

`scripts/out-of-play-probe.ps1` ran for 15 minutes against a live Ottawa (Away, `UserTeamId=1`) vs York
(Home) match and captured **10 distinct out-of-play events**, each correlated against the mod's own geometry
readouts. Log archived at `docs/archive/out-of-play-probe.log`. Event ids confirmed against the enum:
`EventRestartThrowIn`=8054, `EventRestartGoalKick`=8055, `EventTakeThrowIn`=8059, `EventTakeGoalKick`=8062
(the uncaptured corner is `EventRestartCorner`=8053 / `EventTakeCorner`=8064).

| Event | Count | `team` | Geometry cross-check |
| --- | --- | --- | --- |
| `RestartThrowIn` / `TakeThrowIn` | 1 / 3 | `1` your throws, `0` opposition throw | "attacking third" (your) / "defensive third" (opp), possession "your team" / "opposition" — matched every time |
| `RestartGoalKick` / `TakeGoalKick` | 4 / 2 | `0` opposition, `1` yours | opposition goal kicks in "opposition six yard box"; yours in "own six yard box" — matched |

**Q1 Timing/sync — CONFIRMED, clean.** Every event's own `TimeSlice` equalled the current playback
`CurrentTimeSlice` within 1 slice (e.g. `RestartThrowIn` ev ts 8469 / playback 8470), and the played window
`[CurrentFirstEventIndex..m_currentLastEventIndex]` contained that event's index while the frontier `count`
ran well ahead (716–927). So events **enter the played window exactly when playback reaches them** — no
spoiler, no lag — identical to the goal-detection model. `mode` was `3` (Highlight/live) throughout.

**Q2 Team — CONFIRMED both directions.** `team` (`FixtureTeam` Home=0/Away=1) is the **restarting side**:
your throw-ins and goal kick were `team=1` (Ottawa=Away), the opposition throw-in and goal kicks were
`team=0` (York=Home). Cross-checked against the mod's possession readout ("your team" / "opposition") on
every event — matched. This is the same field and encoding the goal detector already consumes.

**Q3 Data1 — undecoded, and NOT `SetPieceType` (hypothesis rejected).** `data1` is `0` on `Restart*` and
populated on `Take*`: throws `5/5/5/10`, goal kicks `50/60`. Out of `SetPieceType` range and not
type-constant; the long-vs-short split hints at a delivery length, but it is **not needed** — the event id
already gives the restart type. Left undecoded.

**Q4 Coverage / trigger choice — decided.** All 10 events fired during highlights. Crucially,
**`EventSeeBallOutOfPlayFor*` never appeared at all** — only `Restart*` and `Take*` did. So the practical
trigger is **`EventRestart{ThrowIn,Corner,GoalKick}`**, not the `See*` family. `Restart*` fires ~100–150
slices before the matching `Take*` (e.g. `RestartThrowIn`@8469 → `TakeThrowIn`@8574), i.e. closer to the
moment the ball goes out, so it is the right edge for the out-of-play sound; `Take*` (which carries the taker
in `PlayerUniqueIDs`) is the better source for a "who takes it" readout.

**Side-finding:** `pteams` (`MatchEventPlayerTeams`) only encodes *visitor* players — a home-team restart
gives `pteams=None`. Do **not** derive team from it; use `team`.

**Not yet observed:** a corner (id known, behaviour expected identical), and any replay of a restart (so the
`!inReplay` freeze is carried over from goal detection on principle, not yet exercised here).

## Recommendation

Extend the existing `ScanGoals` machinery into a general out-of-play scan that returns the most recent
in-window `{Kind: Throw/Corner/GoalKick, Team, TakerIds}`, gated by `!inReplay` and latched once. Match on
the confirmed **`EventRestart{ThrowIn,Corner,GoalKick}`** ids (8054 / 8053 / 8055) — the `See*` family never
fires, so `Restart*` is the trigger, firing at the going-out moment; the sibling `EventTake*` ids
(8059 / 8064 / 8062) carry the taker in `PlayerUniqueIDs` for a taker readout. Attribute with the `team`
field (`FixtureTeam`, restarting side) — not `pteams`. Feed it to (a) the SSE sfx trigger — splitting
`goalline` into `corner` / `goalkick` — and (b) `OutOfPlayLocation`, so the readouts name the true restart while
geometry keeps supplying the third / flank / distance. Keep the ball-geometry path as the coverage fallback.
Strictly better than geometry alone: it adds the corner/goal-kick distinction geometry can never make, and
drops the 2 Hz sampling failure modes. All three headline questions (sync, team, trigger) are confirmed
live; only a corner and a restart-replay remain unobserved (behaviour expected identical).

## Shipped changes since the probe

- **Geometry fallback removed (2026-07-05).** The throw-in geometry fallback originally kept in
  `OutOfPlayOneShot` is gone: all out-of-play sounds now fire **only** from the `EventRestart*` events, which
  are confirmed in sync with playback and are the only source that can name the restart and the team.
  Geometry's sole remaining role in the arbiter is arming the once-per-dead-ball latch when the ball returns
  to play (and the goal detector still owns the net).
- **Put-in-play ping (2026-07-05).** A `putinplay` one-shot (`Navigation.PutInPlayOneShot`, fed by the same
  `ScanRestarts` pass) sounds when the ball is put back into play: `EventKickOff` (each half, and after a
  goal) and the takes — `EventTakeThrowIn`/`NoDest`, `EventTakeGoalKick`, `EventTakeCorner`/`ShortCorner`/
  `CornerToEdgeOfBox`, `EventTakeFreeKick`, `EventShortFreeKick`, `EventTakePenalty`. A same-take dedupe
  (engine `TimeSlice` distance < 25 slices) guards against one take being narrated by two flavour events;
  which of these ids actually fire live (beyond throw-in/goal-kick takes, confirmed) still needs a match pass.

## Access notes

- Reach the match: `UnityEngine.Object.FindObjectOfType<FM.Match.Match>()`.
- IL2CPP exposes private fields as **properties** — use `GetProperty(name, Public|NonPublic|Instance)` for
  `m_commentaryEvents`, `m_currentLastEventIndex`, `CurrentFirstEventIndex`, `CurrentTimeSlice`,
  `CurrentMatchPlaybackMode`, `UserTeamId` (all already used by `MatchBallLocation`).
- Read the event fields by their enum value, not by `ToString()` — compare `(int)ev.CommentaryEventID`
  against the target ids to stay independent of how IL2CPP renders the enum name.
- `CommentaryEventDefinitionsExtensions.*` classifiers are callable live despite decompiled bodies showing
  `return false` (an IL2CPP stub artifact) — but there is no out-of-play classifier, so match ids directly.
- Reads run on the Unity main thread and **time out while a highlight/replay/cutscene renders** — any
  trigger must be cheap and tolerate "engine busy", exactly as `MatchWatcher` / `ScanGoals` already do.
- `/eval` REPL hazards apply (see the eval-repl-hazards memory): prefer the typed pattern `ScanGoals`
  already proves over AppDomain-reflection wrappers, which have hard-crashed the game.
</content>
</invoke>
