> Upstream FM26 Access reference, release 0.2.16. Check [FM27 migration notes](../fm27-migration.md) before using game-specific APIs.

# Live match data — what the mod can read in real time

Research inventory for mod-authored "report this info" keyboard shortcuts during a live match.
Confidence is graded by how it was established: **verified live** (read back from a running match this
session), **high** (concrete engine structure reachable via a path the mod already uses), or
**needs work** (exists, but extraction/design is non-trivial). Design intent so far: **on-demand**
shortcuts (a key pulls the info), not an interrupting auto-announcer.

## Proven access path

The mod already reaches the live engine in `Server/MatchWatcher.cs`:
`UnityEngine.Object.FindObjectOfType<FM.Match.Match>()` → read the `m_matchState` **property** (IL2CPP
exposes the private field as a property — use `AccessTools.Property`, not `GetField`). Anything on the
`Match` MonoBehaviour or the `MatchState` struct is confidently reachable this way.

**Operational caveat:** `/eval` and any read dispatch onto the game's main thread. While a
highlight / replay / cutscene renders the 3D engine, that thread is busy and reads **time out**. A live
reader must be cheap and tolerate "engine busy right now" (as `MatchWatcher` already does).

## Tier 1 — verified live this session

Read back from a running match (`MatchState` struct, `SI.Match\MatchState.cs`):

- **Score** — `MatchState.HomeGoals` / `AwayGoals` (sbyte)
- **Clock** — `MatchState.Time` (float seconds) + `Match.GetCurrentMinute()` (int)
- **Injury time** — `MatchState.InjuryTime` (int; −1 = none)
- **Period** — `MatchState.Period` (enum: KickOff, FirstHalf, HalfTime, SecondHalf, ExtraTime*, Penalties, FullTime)
- **Sim state** — `MatchState.Flags` (Simulating / Finished / HighlightThreadAheadEnough)
- **Crowd mood** — `HomeCrowdMood` / `AwayCrowdMood` (enum, 26 states: Celebrating, Angry, Nervous, MissedChance, ConcedeVitalGoal, Singing…)
- **User's team** — `Match.UserTeamId` (sbyte); **HasStarted** — `Match.HasStarted`

Verified live via the UI (mod already renders these):

- **Play-by-play commentary** — CommentaryBar UI, already streamed over SSE (e.g. "Vázquez knocks it out to the left for Grant")
- **Per-player match rating** — squad panel (e.g. "Gustave, 6.7")
- **Per-player body language** — "Positive / Neutral body language"
- **Per-player condition** — "Excellent condition"
- **Subs / stoppages remaining** — "5/5 Subs", "3/3 Stoppages"
- **Aggregate match stats** (possession, shots, shots on target, corners, fouls…) — the match **Stats** card, already parsed per-team by `Server/Features/MatchStatsTableReader.cs`

Note: player rating/condition/body-language and aggregate stats exist **only in the UI**, not the
engine model — read the rendered panel, not `Player`/`Team`. For rating and condition the reliable
read (verified live 2026-07-11, and shipped as the Alt+1/Alt+2 squad readouts) is the in-match
Your Squad panel (`OverviewPrototype` → `PlayersList` → `BindingRoot_SquadOverviewPlayerBlock`
rows, all 11 realized): each row's binding node materialises `MatchRating` (float), `Condition`
(0–100), `IsSub`, `RedCardedInMatch`, `MatchInjuryType`, `Goals`, `SquadNumber` and more under its
`playerinfo` (`FM.UI.MatchPlayerReference`) child — see `Server/MatchSquadReadout.cs`.

## Tier 2 — high confidence, engine model via the `Match` handle

- **Structured event stream** — `Match.m_commentaryEvents` : `List<MatchCommentaryEvent>`. Each event
  (`FM.Match\MatchCommentaryEvent.cs`) carries: `TextPresent[]`/`TextPast[]` (the strings),
  `CommentaryEventID` (large enum classifying goal / own-goal / penalty / card / substitution / injury /
  foul / ~50 shot variants / save / corner / throw-in / free-kick / offside / VAR / whistles),
  `PlayerUniqueIDs[]` + `PlayerTeams` (who was involved), `TimeSlice`/`Duration`,
  `CommentaryImportance` (VeryLow→MustShow) and `IncidentImportance` (Skippable→Goals) for filtering,
  and `Team` (home/away). Classifier helpers in `CommentaryEventDefinitionsExtensions.cs`
  (`ScoredGoal`, `IsSentOffEvent`, `IsSubstituteEvent`, `TakeCornerKick`…).
- **Push subscription** — `MatchCommentaryComponent.SubscribeCommentary(onAdd, onRemove)` fires per line
  as it appears (hook for an event-driven announcer, if ever wanted).
- **What's coming (set pieces / stops)** — `StopTimeInfo` (`SI.Match\StopTimeInfo.cs`):
  `NextStopReason` (SetPiece, EndOfPeriod, PenaltyAwarded, EndOfCelebration…) and `SetPieceType`
  (Corner, Penalty, GoalKick, ThrowIn, free-kick flavours by pitch position).
- **Per-player live status flags** — `IPluginMatchPlaybackInterface.GetEntitiesBillboardInfo(...)` →
  `BillboardInfo` with `IsShowing()` (on pitch), `IsSub()`, `HasYellowCard()`, `HasRedCard()`,
  `HasSlightInjury()`, `HasSeriousInjury()`.

## Tier 3 — reachable, needs work

- **Spatial data** — ball + all 22 player positions/rotations (`GetBallPosition`,
  `GetEntityWorldTransform`, `PlayerEntityData.Position` Vector2). Coordinates→speech is its own design.
- **Lineups / formation / shirt numbers** — `Match → MatchScene.HomeTeam/AwayTeam → Players`
  (`ShirtNumber`, `ShirtName`, `IsGoalkeeper`, `HasArmband`=captain). Path sound, not live-verified; the
  rendered squad panel is the reliable near-term source.
- **xG / momentum** — UI-bound chart data (`XGPoint`: minute, xG, running goals; momentum `DataPoint`);
  underlying numbers come from the native plugin.

## Spatial / on-pitch data (deep dive) — verified live 2026-07-02

Goal: translate ball + player positions into "what's happening on the pitch" speech. **Verified live**
against a running match (playback mode `Highlight`): ball object with a live transform, 45 `Player`
objects each with position + identity, and a working possession-by-proximity read.

### Two access routes (both metres, Unity world space)

1. **Direct engine objects (verified working live):**
   - Ball — `FindObjectOfType<FM.Match.Ball>().transform.position` → Vector3. Read `(-16.02, 9.01, 16.43)`
     (Y = height; ball was 9 m up). Ground plane is (X, Z); Y is height.
   - Players — `FindObjectsOfType<FM.Match.Player>()`; each exposes (as IL2CPP **properties**) `Position`
     (Vector3, Y≈0 on ground), `Rotation`, `ShirtNumber`, `ShirtName`, `BillboardName` ("K. Kambere"),
     `TeamID`, `IsGoalkeeper`, `HasArmband` (captain), `IsActive`. Live-verified all of these.
   - Subs sit lined up on the touchline (a tight column at constant X); on-pitch vs bench is better told
     from `BillboardInfo` flags (`IsShowing`/`IsSub`) than from coordinates.
2. **Game's own normalised 2D data:** `Match2DSimulation` (a `MatchComponent`) holds
   `Match2DEngineData` = `ValueWithChangeNotify<MatchSimulationData>`, refreshed each `MatchTick`.
   `MatchSimulationData` = `Ball` (`MatchEntityData`: Vector2 Position, Rotation, Height) + `Players`
   (`List<PlayerEntityData>`: Position Vector2, Rotation, Height, `EntityIndex`, `ShirtNumber`,
   `IsSubstitute`, colours). Identity maps `EntityIndex → MatchSetup.Entities[]` (`MatchEntitySetup`:
   shirtName, billboardName, shirtNumber, teamIndex, isCaptain, uniqueID…). Cleaner coords, but tied to
   the 2D sim component ticking (the pitch-view widget `FMMatchSimulationWidget` is NOT loaded in the
   default 3D match-overview panel — confirmed absent live).

The direct-object route worked regardless of view, so it's the more robust source; the 2D-sim route
gives pre-normalised coordinates when available.

### Coordinate system

- Unity world Vector3, metres, pitch centred near origin. Ground plane = (X, Z); **Y = height above
  ground** (ball aerial vs on the floor).
- Pitch dimensions: `PitchSetupInfo.pitchLength` / `pitchWidth` (metres) via
  `MatchSetup.Stadium.pitchSetupInfo`. Standard ≈105×68.
- The game's own mappers: `Match2DSimulation.NormalisePosition(Vector3)` and
  `PitchDrawing.GetMatchEngineCoordPercentage(x,y)` (raw → 0..1 pitch %).
- Which axis is length vs width, and each team's attacking direction: derive from
  `MatchPlaybackController.TeamKickingUpfield` / `IPluginMatchInterface.IsTeamKickingUpfield(handle,
  timeSlice, teamId)` (native). **TODO: calibrate the length/width axis empirically** (e.g. read a
  goalkeeper's coordinate — he sits near his goal-line on the length axis).

### Ball detail

- Position + height (above). Rotation. `IPluginMatchPlaybackInterface.GetBallPosition(timeSlice)` /
  `GetBallWorldTransform(scale)` are the interface-level accessors.
- Velocity — `SI.Match.Anim.BallVelocity` (`float3 incoming` / `outgoing`) via `IMatches.GetBallVelocity`.
- Contact events — `SI.Match.Anim.BallContact` (`time`, `float3 position`, `BallContactID`): Kick,
  Header, Save, Deflection, Ground, Post, Crossbar, Net… — good for "header", "hit the post".

### Possession / phase — NO direct field; use proxies

The managed layer exposes no "team in possession" flag (it's native C++). Proxies:
- **Nearest active player to the ball = man on the ball** — verified live: ball aerial at (−16,9,16),
  nearest was #34 P. Saputo at 2.1 m. Simple, works, gives player + team.
- **`IsTeamKickingUpfield`** — which team is attacking (phase/direction).
- **Last on-ball commentary event's `PlayerTeams`** (pass/tackle/shot names a team).
- **Ball-contact type**, momentum chart (`FMMatchMomentumChart` DataPoint), crowd mood — softer signals.

### Derivable readouts (what we can translate)

From positions + pitch dims + attacking direction: which **third** (defensive/middle/attacking) the ball
is in, which **flank** (left/centre/right), **distance from goal**, in/around the **box**, the **man on
the ball** and his team, **ball aerial vs on the floor**, rough **team shape** (how deep/high the line
sits, how compact), and nearest defender to the ball-carrier.

### Ball-third readout — POC feasibility VERIFIED in-game (2026-07-02)

Goal: on-demand shortcut announces which third the ball is in (defending / middle / attacking),
relative to the player's team (else home). **Feasible in-game, not just replays** — proven live.

**Key: read the ball from the engine INTERFACE, not the rendered object.** The rendered
`FM.Match.Ball` only exists while a highlight renders, but the playback interface returns the ball
position for ANY time-slice, independent of what's on screen. Verified: `GetBallPosition(slice)`
returned coherent, distinct pitch coords for slices 0 (=(0,0,0) kickoff centre), 10, 20…80 (=z 51.86,
the +Z goal-line), 150 — including slices ahead of the current playback position. So at trigger time,
`GetBallPosition(CurrentTimeSlice)` always gives the current ball position.

**Verified access path (all via reflection, all live):**
- `ctrl = FindObjectOfType<FM.Match.MatchPlaybackController>()` — persists whole match.
- `m = FindObjectOfType<FM.Match.Match>()`; `refTeam = (int)m.UserTeamId` (valid in the user's own
  matches; −1 when neutral → fall back to home team). `CurrentManagerFixtureTeam` came back **Invalid**,
  so do NOT use it — `UserTeamId` is authoritative.
- `slice = (int)m.CurrentTimeSlice` (playback position; not LiveTimeSlice/sim frontier).
- `ball = (Vector3)ctrl.PlaybackInterface.GetBallPosition(slice)` — Z is the length axis.
- `upfield = (bool)ctrl.MatchInterface.IsTeamKickingUpfield(ctrl.MatchHandle, slice, (byte)refTeam)` —
  true ⇒ that team attacks **+Z**. Handles half-time end-swaps.

**Calibrated coordinate facts (from GK positions live):** ground plane (X, Z), **Z = length**, X =
width, Y = height. Starting GKs sat at Z≈−46 and Z≈+48 (opposite goal-lines); half-length ≈ 52.5.

**Algorithm (ran live, returned "middle third" for z=12.94):**
```
L = 52.5                         // half pitch length; ideally PitchSetupInfo.pitchLength/2
s = upfield ? ball.z : -ball.z   // signed toward the ref team's attacking goal
p = (s + L) / (2*L)              // 0 = own goal-line, 1 = opponent goal-line
p < 1/3 -> "defending end" ; p < 2/3 -> "middle third" ; else "attacking third"
```

**Wiring (mirror the Alt+I "describe" feature):** JS key handler →
`fetch('/matchthird').then(r=>r.text()).then(FM.core.say)`; new GET route → `MainThreadDispatcher`
→ gate with `MatchWatcher.TryRead` (CommentaryBar present = in a match) → compute → return the string
(or "ball position unavailable" pre-kickoff/half-time/full-time, "not in a match" otherwise).
Announce path: `FM.core.say` → `/speak` → `SpeechPipeline` (app.js:137).

## Match conditions (Alt+4) — calibrated live 2026-08-06

Shipped as `MatchConditionsReadout` (Plugin) + `MatchConditions` (Navigation), routes `/matchconditions`
and `/matchcrowdmood`. Raw values captured from a running match via `/conditionsprobe`
(`Server/Dev/ConditionsProbe.cs`), which exists to keep this calibration repeatable:

```
attendance 3189   capacity 6833   home fans 2848   away fans 341   stadiumAge 14
temperature 8     precipitation Drizzle   wind 0.75   time 19.0    fogDensity 0
pitchConditionRating 170 (Int16)   wetnessStrength 0.76   hasUnderSoilHeating 0
HomeCrowdMood IdleHappy   AwayCrowdMood MinorAppreciation
m_matchImportanceLevel FairlyImportant   m_enabled True
```

What that settled:

- **Attendance is populated and internally consistent** — home 2,848 + away 341 = 3,189 = `attendance`.
  Path is `MatchPlaybackController.Setup.Stadium.stadiumSetupInfo`, the sibling field of the
  `pitchSetupInfo` `MatchBallLocation` already reads.
- **Attendance is NOT strictly fixed at kick-off.** A later read of the same fixture returned 3,194
  against the same 6,833 capacity and the same 341 away fans. It is near-static, not constant, so it
  must be re-read per press and never cached. (Verified live 2026-08-06 against the shipped Alt+4.)
- **`m_temperature` is Celsius.** 8 at a 19:00 kick-off with drizzle is only coherent on that reading.
- **Weather is NOT on `MatchSetup`** (its `Environment` holds only kits). The source is
  `FM.Graphics.Lighting.PresetLightingSystem.MatchLightingAndWeatherSetup`, whose static `Instance` was
  present and initialised during a live 3D match. Needs the `FM.Graphics` interop reference.
- **Match importance is real, not a default.** It read `FairlyImportant`, not the zero value
  (`ExtremelyImportant`) an unpopulated field would give, with `m_enabled` true. Any future readout must
  still gate on `m_enabled`, because the default is a lie rather than an `Invalid` marker.
- **Wind and pitch condition are NOT shippable.** `m_windStrength` (0.75) and `pitchConditionRating`
  (170, an `Int16`) each have no enum, no threshold table, and **no consumer anywhere in the game's
  managed code** — only the native pitch shader reads the latter. One sample cannot establish a range,
  so any spoken band ("strong wind", "pitch good") would be invented. Left out until calibrated.
  The game does have localised wind wording — the condition text beside `WeatherIcon` on the Team News
  and Half Time Report panels reads e.g. "Strong Wind" — so pairing that string with the raw float
  across a few matches is the way to calibrate, and would let the readout use the game's own words.

**Keybind conflict:** resolved — shipped as the context-sensitive `/matchchord` route
(`FM.matchchord` owns Alt+` and Alt+1..7: match readouts while the engine is live, ribbon nav
otherwise). Current in-match map: Alt+` score+clock, Alt+1 ratings (double tap = lowest three),
Alt+2 condition (double tap = lowest three), Alt+3 body language (double tap = non-neutral detail),
Alt+4 match conditions (double tap = both crowds' moods), Alt+5 possession then third+flank,
Alt+7 distance. Alt+6 remains free.

### Hard limitations

- **Rendered 3D entities** (`FM.Match.Ball`/`Player` objects, world transforms) exist only during
  Highlight/Replay playback. The **interface reads** (`GetBallPosition`, `IsTeamKickingUpfield`) do NOT
  have this limit — they work between highlights too. Use the interface for anything on-demand.
- **Main-thread reads stall while a highlight frame is actively rendering** (repeated `/eval` timeouts,
  then freed). A handler must be cheap and tolerate "engine busy".
- Positions are only meaningful once the match is underway (not pre-kickoff / half-time / full-time) —
  gate on `HasStarted` and period, degrade to "ball position unavailable" otherwise.

## Gaps

- No live **engine** fields for player rating / condition / stamina / morale — UI only.
- No aggregate-stat struct in the engine — UI Stats card, or tally from the event stream.
- Main-thread reads stall during highlights/replays — never block; degrade in place.

## Candidate on-demand shortcuts (by value/effort)

1. Score + clock + period (most of this is already composed in `MatchWatcher`). (Shipped as Alt+`,
   minus the period.)
2. Repeat last commentary line.
3. Match situation — period, added time, subs (5/5) & stoppages (3/3) remaining, next stop/set-piece.
4. Team stats snapshot (from the Stats card) without navigating to the screen.
5. Your XI status — booked / injured / subbed, lowest-condition players. (Partially shipped: the
   Alt+1/Alt+2 squad readouts cover best/lowest ratings and condition; cards/injuries remain.)
6. Crowd/atmosphere ("home crowd nervous / celebrating"). (Shipped as the Alt+4 double tap, both ends.)
7. Wind and pitch condition, once their scales are calibrated (see the Alt+4 section above).
