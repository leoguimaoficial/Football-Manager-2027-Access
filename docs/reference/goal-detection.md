> Upstream FM26 Access reference, release 0.2.16. Check [FM27 migration notes](../fm27-migration.md) before using game-specific APIs.

# Goal detection — reliable, position-independent signals

Research for replacing the goal ("you score" / "they score") sound trigger. Sibling of
[live-match-data.md](live-match-data.md); focuses on one question: **how does the mod know a goal was
scored, reliably and at the right moment, without guessing from the ball's position?**

## The problem with the current trigger

Today the goal sound fires from **ball geometry** — `MatchBallLocation.ComputeMatchAudio` classifies the
ball as `OutOfPlayKind.Net` when it reads the ball crossing the goal-line between the posts and under the
bar (`BallLocation.ClassifyOutOfPlay` + `DowngradeDeepNet`), and `SseStreamer` fires `goalfor`/`goalagainst`
on the `InPlay -> Net` edge. This is sampled at ~2 Hz from a re-created live ball reading.

It is unreliable in both directions:

- **False positives** — a ball that is momentarily inside the post/bar box but not a goal, sampling noise,
  or an angle/height the geometry misjudges, reads as `Net`.
- **False negatives** — at 2 Hz the actual crossing frame is often missed; the ball is only seen once it
  has landed deep, so `DowngradeDeepNet` (rightly) suppresses it and the goal is silent.

The root cause is structural: **the ball position is a low-rate geometric proxy for an event the engine
already knows about exactly.** The engine does not make us guess.

## The engine knows. Three position-free signals.

### 1. Definitive score — `MatchState.HomeGoals` / `AwayGoals` (ground truth)

`MatchWatcher` already reads `FM.Match.Match.m_matchState` (via `AccessTools.Property`) every SSE snapshot.
`HomeGoals` / `AwayGoals` (sbyte) are the **authoritative running score**. A goal has been scored **iff**
one of them increments — this is true by construction, so it has **zero false positives and zero false
negatives**, and it inherently excludes disallowed / VAR-overturned goals (the count only moves on a goal
that stands). Team attribution is free: home vs away, mapped to the reference ("your") team the same way
`MatchBallLocation` already computes `goalForRef`.

The one open question is **timing** (see the empirical section). The existing code comment claims the score
"lags the crossing by many seconds during highlight playback." A modest lag is acceptable — a goal sound a
couple of seconds into the celebration is honest and correct; it is not a spoiler and is far better than
firing wrongly or not at all. The guard already used for the geometric path (freeze while
`MatchIsInReplay`, fire only on an increase) covers the replay/seek edge where a rewind past a goal could
re-increment.

### 2. The exact goal event — `EventGoalScored` in the commentary stream (in-sync, attributed)

`Match.m_commentaryEvents` : `Il2CppSystem.Collections.Generic.List<MatchCommentaryEvent>` is the engine's
structured event stream (verified live: 200+ events read back mid-match). Each `MatchCommentaryEvent` carries
`CommentaryEventID` (`SI.MatchTypes.CommentaryEventDefinitions`), a `TimeSlice`, `PlayerTeams`, and a `.Team`
(`FixtureTeam`, home/away).

The game ships its own canonical classifiers in `FM.Match.CommentaryEventDefinitionsExtensions`. Called live
against the whole enum, they resolve to (this session):

- **`IsAnyTypeOfGoal`** → exactly **2** ids: `EventGoalScored` (legal) and `EventIllegalGoalScored`
  (ball in net but disallowed).
- **`IsIllegalGoal`** → `EventIllegalGoalScored` only.
- Therefore `IsAnyTypeOfGoal(id) && !IsIllegalGoal(id)` == **`EventGoalScored`** — a single, canonical,
  **exactly-once-per-goal** event. This is the clean trigger.
- **`ScoredGoal`** → 22 ids, but these are commentary *flavours* that accompany a goal (`EventGoalOne`,
  `EventGreatStrike`, `EventOwnGoal`, `EventScoredPenalty*`, `EventHatTrick`…). Multiple can fire for one
  goal, so `ScoredGoal` is good for *describing* a goal, **not** for counting one. Do not trigger on it
  without dedup.
- `IncidentImportance.Goals` / `GoalAction` are also on each event as a coarse semantic marker (a 0-0 match
  showed zero `Goals`-importance events across 212 events — a clean discriminator), but the extension
  methods are more precise.

**Sync boundary (verified live):** the *full* `m_commentaryEvents` list is populated at the **simulation
frontier** — events exist with `TimeSlice` well ahead of the current playback position (e.g. list out to
`ts=1061` while `MatchState.TimeSlice=1003`). Reading the whole list for a goal would therefore fire
**ahead** of the highlight — a spoiler. But the playback window
`[Match.CurrentFirstEventIndex .. m_currentLastEventIndex]` **advances with playback** (e.g. indices
102–104 at TimeSlice 1003) and is what drives the on-screen commentary. Firing when a goal event enters
**that window** is in-sync with what the player watches. This is the key insight the ball-geometry design
was implicitly reaching for.

### 3. Push subscription (event-driven, no polling) — `MatchCommentaryComponent.SubscribeCommentary`

`MatchCommentaryComponent.SubscribeCommentary(onAdd, onRemove)` fires a callback per commentary line as it
appears. If it fires at *display* time (playback-synced, likely, as it drives the CommentaryBar), it is the
cleanest possible trigger: subscribe, and on an added line whose `CommentaryEventID` is `EventGoalScored`,
fire the sound with `.Team` attribution — no 2 Hz polling, no geometry. Needs a live check of *when* it
fires (frontier vs display) before relying on it.

### Supporting corroborators

- `MatchStopReason.EndOfCelebration` in `StopTimeInfo` — a goal-specific stoppage.
- `Match.HighlightTimes.LastGoalTimeSlice` — the highlight system records the last goal's time slice.
- `Match.m_jumpToKickOffAfterGoal` (bool) — flips around a goal restart.

## Recommendation

Trigger on a **real engine goal signal**, not ball geometry. In order of preference:

1. **`EventGoalScored` entering the playback event window** (`IsAnyTypeOfGoal && !IsIllegalGoal`), team from
   `.Team`, gated by `!MatchIsInReplay`. In-sync, exactly-once, reliable, team-attributed, excludes
   disallowed goals. Or the equivalent via `SubscribeCommentary` if it proves display-timed.
2. **Score delta** (`HomeGoals`/`AwayGoals`) as trigger or as a confirmation cross-check — ground truth,
   trivially simple, possibly lags to the celebration.

Both are strictly better than geometry: zero false positives, zero false negatives. Keep the existing replay
freeze and fire-on-edge discipline from `SseStreamer`.

## Empirical verification (confirmed at a live goal)

A background monitor (`scratchpad/monitor.sh` + `goalstate.cs`) sampled `HomeGoals+AwayGoals`, the playback
window contents, and a full-list `Goals`-importance scan across a live match and **captured a real goal**
(away team; scorer `VisitorTeamPlayer1`, assist `VisitorTeamPlayer2`; `EventGoalScored` stamped
`TimeSlice=2544`). Findings:

- **Score is playback-synced, not the frontier, and not lagged.** `AwayGoals` ticked 0→1 at playback
  `TimeSlice≈2550`, and `EventGoalScored@2544` was **already in the playback window** `[F255..L259]` at that
  same instant. The two are **coincident** (within a second). The old "lags the crossing by many seconds"
  belief is wrong — there is **no spoiler risk** and no material celebration lag for either signal.
- **Both signals rewind and re-fire on the goal replay.** Seconds later the game seeked back to re-show the
  goal: `TimeSlice` jumped back to 2502, `AwayGoals` reverted to **0**, and `EventGoalScored` **left** the
  window; then playback ran the goal again and both returned to 1 / in-window. So a naive trigger on either
  signal **double-fires**. The `!MatchIsInReplay` gate plus the fire-on-edge latch already in `SseStreamer`
  is **mandatory**, not optional — this is the real trap, now proven.
- **Team attribution is consistent.** The event's `PlayerTeams` (`VisitorTeam*`) matched the side whose
  score incremented (`AwayGoals`); `.Team` yields home/away directly.

Net: the chosen trigger — **`EventGoalScored` entering the playback window, `.Team` for attribution, gated
by `!MatchIsInReplay` and latched to fire once per goal** — is confirmed spoiler-free, exactly-once (with
the latch), and team-correct. `HomeGoals`/`AwayGoals` agree instant-for-instant and make a free confirmation
cross-check.

## Access notes

- Reach the match: `UnityEngine.Object.FindObjectOfType<FM.Match.Match>()`.
- IL2CPP exposes private fields as **properties** — use `GetProperty(name, Public|NonPublic|Instance)`, not
  `GetField`. Verified for `m_matchState`, `m_commentaryEvents`, `m_currentLastEventIndex`,
  `CurrentFirstEventIndex`, `LiveTimeSlice`, `CurrentTimeSlice`.
- `List<MatchCommentaryEvent>` is read via its `Count` / `Item` properties; each element is a
  `MatchCommentaryEvent` struct.
- `CommentaryEventDefinitionsExtensions.*` are public static and **callable live** despite the decompiled
  bodies showing `return false` (IL2CPP stub artifact) — invoke via reflection or directly.
- Reads run on the Unity main thread and **time out while a highlight/replay/cutscene renders** — any
  trigger must be cheap and tolerate "engine busy", exactly as `MatchWatcher` already does.
