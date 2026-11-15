> Upstream FM26 Access reference, release 0.2.16. Check [FM27 migration notes](../fm27-migration.md) before using game-specific APIs.

# Shot-attempt detection

How the "shot attempt" match sound decides that a player has had a go at goal, and why the id set is
drawn where it is. Sibling of [goal-detection.md](goal-detection.md),
[out-of-play-detection.md](out-of-play-detection.md) and
[possession-defensive-triggers.md](possession-defensive-triggers.md); all four read the same engine
commentary stream through the same machinery.

## What the cue is for

The sound fires on the **strike**, not on where the ball ends up. A blind player hears the attempt and
then waits — a second or so later the goal, save, or restart sound tells them whether it went in. That
gap is the whole feature; anything that collapses it (firing on an outcome id instead) defeats it.

Both teams' attempts play the same clip. The possession metronome already says who has the ball, and
not knowing whose chance is about to resolve is part of the tension.

## The signal

`Match.m_commentaryEvents` (`List<MatchCommentaryEvent>`; `CommentaryEventID`, `TimeSlice`, `Team`),
read only up to the **playback** high-water `m_currentLastEventIndex` — never the full list, which is
the simulation frontier and would spoil. Same window, same replay freeze, same baseline-on-connect as
every other event-driven sound.

`CommentaryEventDefinitionsExtensions` has no shot classifier to borrow (unlike `IsAnyTypeOfGoal`), so
the set is curated by hand in `MatchBallLocation.IsShotAttempt` and validated live.

## The engine narrates a shot in two beats

Confirmed live (2026-08-09, Ottawa match, `UserTeamId=0`):

```
72   EventDefaultShot              ts=715    team=0
73   EventShotWide                 ts=721    team=0     <- outcome, 6 slices later
74   EventRestartGoalKick          ts=721    team=1

138  EventShotFromDistanceBanana   ts=1615   team=1
139  EventBlockShot                ts=1616   team=0     <- outcome (also a gooddefence trigger)
```

A header at goal has its own attempt beat too — the case most likely to have been narrated only by the
`EventHeader*` outcome family, and it is not:

```
165  EventWinsJumpforball   ts=2033   team=0     (excluded from IsGoodDefence as aerial noise)
166  EventShotHeader        ts=2033   team=0     <- the attempt
167  EventSilentHalfChance  ts=2033   team=0
     ... the outcome follows ~2 slices later
```

An attempt-family event **precedes** the outcome in every case seen, including a blocked shot and a
header. Playback runs
at roughly four slices per game second, so the wide shot's 6-slice gap is about a second and a half of
real tension. The SSE audio half samples every 125 ms, far finer than that, so the two beats land in
separate ticks.

The 1-slice gap on the blocked shot is tighter than one tick, so `SseStreamer` emits the shot block
**before** the defence and goal blocks: when a strike and its outcome fall in the same span, the player
still hears them in the order they happened.

## The cue lands on the strike, not the outcome (measured)

The whole feature rests on the played window advancing **with** playback rather than in bulk, so the
attempt beat is reachable before the outcome beat. Measured live (2026-08-09) by sampling
`(CurrentTimeSlice, m_currentLastEventIndex)` about three times a second and correlating each event's
arrival in the window against its own `TimeSlice`:

```
idx  event                     evTS   entered window at   lag
258  EventHeaderPass           3088   3088                0
259  EventAttemptPassFloor     3096   3096                0
260  EventAttemptPassFloor     3105   3105                0
261  EventWait                 3111   3111                0
262  EventAttemptPassFloor     3122   3122                0
263  EventPassMediumForward    3130   3130                0
264  EventPassShortForward     3142   3142                0
265  EventDefaultShot          3150   3150                0   <- the strike
266  EventHighlightMarker      3154   3154                0   <- the outcome, 4 slices later
```

**Lag is zero on every event**: an event enters the played window exactly as playback reaches its slice.
So the attempt is detectable a full 4 slices (about a second) before its outcome, and the detector cannot
be reading ahead of what the player is watching.

The remaining latency is the sampling rate. `SseStreamer` runs its audio half — the cue zones and all
four one-shot scans, including `ScanShots` — on every **125 ms** sub-tick, outside the `fullTick` gate
that throttles the expensive screen walk to every fourth. Playback advances about four slices a second,
i.e. one slice per 250 ms, so the scan samples faster than the engine produces events and the sound lands
within a sub-tick of the strike.

## In the set (attempt = the strike)

- `EventDefaultShot` — the generic attempt.
- Foot techniques: `EventShotLowDrive`, `Chip`, `ChipGoalie`, `Lob`, `LobGoalie`, `Banana`, `Hope`,
  `BicycleKick`, `Volley`, `HalfVolley`, `HighDrive` — with their `LeftFoot`/`RightFoot` variants and the
  weather variants that exist (`EventShotLowDriveWet`, `EventShotLobWind`).
- Headers at goal: `EventShotHeader`, `EventShotDivingHeader`.
- The same families struck from range (`EventShotFromDistance*`) and from a tight angle
  (`EventShotFromAngle*`), including their header and diving-header members and
  `EventShotFromDistanceLowDriveWet` / `EventShotFromDistanceLobWind`.
- Set pieces struck at goal: `EventShotFreeKick`, `EventShotFromDistanceFreeKick`,
  `EventShotFromAngleFreeKick`, `EventTakePenalty`, `EventTakePenaltyForHattrick`.

The free-kick ids are the **shot** ones, not `EventTakeFreeKick` — so a free kick played short or crossed
into the box stays silent and only one hit at goal sounds.

## Out of the set (outcome — these resolve the cue, they don't open it)

- Off target: `EventShotWellWide`, `WellOver`, `WellOverHeader`, `WellOverWide`, `Wide`, `Over`,
  `OverWide`, `JustWide`, `JustOver`, `EventShotWellWideWind`, and the whole `EventHeaderWide` /
  `Over` / `JustWide` / `JustOver` / `WellWide` / `WellOverWide` / `OverWide` family.
- Woodwork: `EventShotClipsPost`, `ClipsBar`, `HitsPost`, `HitsBar`, `EventShotClipsBottomOfPost`,
  `EventShotHitPostIn`, `EventShotHitBarIn`, `EventShotHitBarInDelayed`, and the `EventHeaderClips*` /
  `EventHeaderHits*` members.
- Deflections and goal flavours: `EventShotDeflected(Wide)`, `EventHeaderDeflected(Wide)`,
  `EventShotMadeSure`, `EventHeaderMadeSure`, `EventShotDistanceUnlucky`, `EventShotDistanceGoal(Long|
  VeryLong)`, `EventHeaderDistanceGoal`.
- Saves and blocks: the keeper-save families, `EventBlockShot`, `EventBlockShotHeader`,
  `EventWallBlocksShot`, `EventWallDeflectsShot`, `EventWallBlocksFreeKickAttempt` — all already
  `gooddefence` triggers.
- Penalty outcomes: `EventScoredPenalty*`, `EventMissedPenalty*`, `EventSavedPenalty*`,
  `EventSavesPenalty`, `EventPenaltyHitsPost`/`Bar`/`Over`/`Wide`.
- Not attempts at all: `EventMissedHeader`, `EventKnockdownHeader`, `EventHeaderBackToKeeper`,
  `EventHeaderPass`, `EventLongShotsRubbish` (commentary opinion), the `EventGreatStrike` /
  `EventGoodMoveGoodStrike` praise family, `EventFreeKickChance*` (a free kick has been awarded, not
  taken), `EventPenaltyAppeal*` / `EventDissent*Penalty*` / `EventVar*Penalty*`.

## Not yet observed live

These are in or out on naming and on the two-beat structure above, not on a sighting. If a shot ever
sounds late, twice, or not at all, check `/events/log` for which id was involved before changing the set:

- A **penalty**. `EventTakePenalty` was removed from `ClassifyPutInPlay` so the kick sounds once, as an
  attempt, rather than as a ping and an attempt at the same instant. The ball genuinely does go into play
  at that moment, so the put-in-play signal is deliberately traded away — the attempt is the more
  meaningful of the two, because it opens the wait for the goal or save sound.
- `EventShotGoalieLuresAttackerShotLeft` / `ShotRight` (9044/9047) — goalkeeper-context narration around
  a one-on-one. Left out: it is unclear whether they are the attempt or commentary about it, and the
  underlying strike should carry its own attempt event.

## Fire-once rules

`Navigation.ShotOneShot` (pure, unit-tested in `ShotOneShotTests`): silent baseline on the first
readable tick, replay freeze (no fire and no high-water advance, so the strike sounds when live
resumes), and a same-moment dedupe of **3 slices**.

That window is much tighter than `DefenceOneShot`'s 25. It has to be: a rebound — shot, save, immediate
second shot — is two genuine attempts a couple of seconds apart and both must be heard. Three slices is
under a second, which collapses one strike narrated by two ids without swallowing a follow-up.

## Deploying a change

`ShotOneShot` lives in Navigation, which is **permanent** — a change to it needs a full game restart, not
`/reload`, and the game must be closed before `dotnet build` or the locked DLL silently fails to copy.
See [../hot-reload-plan.md](../hot-reload-plan.md) and the deploy-lock note in
[goal-detection.md](goal-detection.md). The companion needs F5 to pick up the `app.js` preload change.
