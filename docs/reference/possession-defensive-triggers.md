> Upstream FM26 Access reference, release 0.2.16. Check [FM27 migration notes](../fm27-migration.md) before using game-specific APIs.

# Possession / Defensive Event Triggers — Review Worklist

**Status:** DRAFT for review, now annotated with live frequency data. Jacob to edit — tick,
cross, move between sections, add notes.

## Goal

Replace the current gain/lose‑possession sound trigger. Today it fires off `PossessionHolder`
geometry flips (nearest‑player proximity), which goes off too often and at insignificant
moments. We want it to fire on **successful or impactful defensive events — which includes
changes of possession**, so the sound means "someone did something good defensively" rather
than "the ball drifted closer to a different shirt."

Each trigger maps to **gain** or **loss** by the event's `Team` (actioning team):

- Actioning team is **your** team → **possession gained** sound.
- Actioning team is the **opponent** → **possession lost** sound.

## Direction — CONFIRMED (live, Match 1)

`Team` = the team that **performs the action** (i.e. wins the ball), not the team that lost it.

Proof from the sample: Home dominated possession (`EventAttemptPassFloor` 84H/58A,
`EventPassShortForward` 41H/19A). If `Team` on an interception meant the team that *lost* the
ball, interceptions would skew Home. They skew **Away** (`EventGoodIntercept` 36H/48A), matching
the side doing the defending — as do `EventHeadClear` (12H/25A) and `EventGoalkeeperClearBall`
(1H/6A). So for a defensive‑win event, **`Team` is the winning side → their "possession
gained."** This is the correctness blocker cleared; no need to re‑verify per family unless a
family's counts contradict the pattern.

## Live frequency findings (Match 1)

- **Source:** engine `Match.m_commentaryEvents` (cumulative full match stream), read via the
  dev driver. Element fields: `CommentaryEventID`, `Team` (Home/Away), `TimeSlice`. Reusable
  tally logic lives in the scratchpad `tally.cs`; formalise into `scripts/` if we keep using it.
- **Coverage:** 1,380 events, 139 distinct types, ~1.4 halves of one **home‑dominant** match
  (a besieged/underdog match still needed to populate the save/last‑ditch families — see gaps).
- **Counts below are observed totals from that sample**, not projections. Treat as relative
  ranking, not absolutes.

**Two structural conclusions:**

1. **Turnovers are signalled by the WIN event, not a LOSS event.** The whole "loses the ball"
   family was **absent** (`EventLoseControlOfBall` 0, `EventLosesBallUnderPressure` 0,
   `EventBallGoesLoose` 0; only 1× `EventBadPassInOwnArea`). → Fire on win‑side events, derive
   loss from opponent attribution. Do **not** add loss‑side events (they'd rarely fire anyway).
2. **`EventGoodIntercept` needs a cooldown.** At 84 in ~1.4 halves it's the second most common
   event of *any* kind — the backbone signal, but on its own it's a sound every ~30–40s. A
   per‑team cooldown is mandatory, not optional.

---

## A. Confident — meets the criteria (ordered by observed frequency)

### A1. Possession wins (successful defensive action that takes the ball)

**Core signal — frequent, needs cooldown**
- [ ] `EventGoodIntercept` — **84** (36H/48A). The workhorse "won the ball" event.

**Good cadence — impactful, land at a meaningful rate**
- [ ] `EventWinsBlockTackle` — **12** (7H/5A)  (+ `EventWinsBlockTackleWet`, unobserved)
- [ ] `EventPerfectSlideTackle` — **4** (2H/2A)
- [ ] `EventInterceptThroughBall` — **3** (1H/2A). More meaningful than a generic intercept.
- [ ] `EventPickUpFreeBall` — **7** (4H/3A)
- [ ] `EventGoalieCatchBall` — **6** (5H/1A)
- [ ] `EventGoalkeeperHeld` — **7** (3H/4A)

**Not yet observed — keep as confident, but unconfirmed live** (likely need the defensive match)
- [ ] `EventScatterIntercept` — 0
- [ ] `EventPerfectBlockTackle` — 0
- [ ] `EventPerfectGoalieSmotherAtFeet` — 0
- [ ] `EventGoodSaveHeld` / `EventGreatSaveHeld` (+ Wet/Windy) — 0 (no big saves in a game this one‑sided)
- [ ] `EventRobsGoalkeeper` / `EventRobsDefenderInOppArea` / `EventRobsUnderPressureDefender` / `EventRobsDefenderNearOppGoal` — 0

### A2. Impactful defensive stops (deny a chance; possession may not transfer)
- [ ] `EventBlockShot` — **4** (2H/2A)
- [ ] `EventBlockShotHeader` — 0 (unobserved)
- [ ] `EventBlockedOnLine` — 0 (unobserved)
- [ ] `EventClearedOffLine` — 0 (unobserved)
- [ ] `EventKeyClearance` — 0 (unobserved; distinct from routine clears in B8)

---

## B. Not confident — your judgment needed

### B1. Partial / failed defensive attempts
- [ ] `EventSemiIntercept` — 0. Partial; may deflect without control.
- [ ] `EventMissedInterception` — **8** (6H/2A). A *failed* attempt; attacker keeps the ball.
      Fires reasonably often — include only if you want "nearly won it" as a *loss*. I'd exclude.
- [ ] `EventLegalOffTheBallBlockOrTackle` — 0.

### B2. Last‑ditch / silent tackles
- [ ] `EventSavingTackle` — 0 (unobserved; expect it in the defensive match). Very impactful; Add this to the confident list 
      promote to A1 if you like, though the ball often spills.
- [ ] `EventSilentSlideTackle` (+ Wet) — 0. "Silent" = no commentary; unclear if separate
      turnover or a silent duplicate of a tackle already in A1. Verify before including.

### B3. Aerial duels — CONFIRMED NOISE
Highest‑frequency defensive family by far; winning a header is usually just a flick‑on.
Strong recommend: exclude, or gate behind a "verbose" toggle.
- [ ] `EventWinsJumpforball` — **46** (19H/27A)
- [ ] `EventWinsJumpforballWithHeight` — **2**
- [ ] `EventWinsJumpforballNotWithHeight` — 0
- [ ] `EventWinsJumpforballAgainstKeeper` — 0
- [ ] `EventWinsBallInAirAndClears` — **3** (wins it, but clears — doesn't retain) add this to confident list 

### B4. Keeper stops that don't secure the ball: Add all these to the confident list  (rebound stays live)
- [ ] `EventGoalkeeperPunch` — **3** (1H1/1... 3 total)
- [ ] `EventGoalkeeperTippedOver` — **1**
- [ ] `EventGoodSaveNotHeld` / `EventGreatSaveNotHeld` — 0
- [ ] `EventGoalkeeperParry` (+ Wet/Hands/Feet) — 0
- [ ] `EventKeeperPunchesBallAway` — 0
- [ ] `EventGoalkeeperTippedWide` — 0
- [ ] `EventGoodSaveTippedOver` / `EventGoodSaveTippedWide` / `EventGreatSaveTippedOver` / `EventGreatSaveTippedWide` — 0
- [ ] `EventGoalieChipSave` (+ Easy/Failed) — 0
- [ ] `EventSaveSecondAttempt` (+ Good/Great) — 0
- [ ] `EventKeeperDeniesOppositionAgain` — 0
- [ ] `EventSavesPenalty` (+ `EventSavedPenalty*`) — 0. Hugely impactful but a set‑piece moment;
      may deserve its own cue.
- [ ] `EventWallBlocksShot` — 0

### B5. Keeper errors / loses control (loss side)
- [ ] `EventGoalieSpillsBall` (+ Wet) — 0
- [ ] `EventGoalieDropsBall` — 0
- [ ] `EventGoalkeeperGivesBallAway` — 0
- [ ] `EventGoalkeeperStrugglesToMakeSave` — 0
- [ ] `EventGoalkeeperConcedesAtNearPost` — 0

### B6. Loose ball (no owner yet — direction ambiguous)
- [ ] `EventLooseBallInArea` — **1** (0H/1A)
- [ ] `EventBallGoesLoose` (+ Wet/Waterlogged) — 0

### B7. Outfield loses the ball (loss side) — DE‑PRIORITISED
Live data shows these barely fire (the win event carries the turnover). Include only if a
specific turnover you care about produces *no* win‑side event.
- [ ] `EventBadPassInOwnArea` — **1** (1H/0A)
- [ ] `EventLoseControlOfBall` (+ Snow) — 0
- [ ] `EventLosesBallUnderPressure` — 0
- [ ] `EventPlayerMakesBadBackpass` — 0
- [ ] `EventPlayerOverhitsPass` (+ Wind) — 0
- [ ] `EventBadPassBackToKeeper` / `EventBadPassLongBackToKeeper` — 0

### B8. Clearances — high volume, recommend exclude (except `EventKeyClearance` in A2)
- [ ] `EventHeadClear` — **37** (12H/25A)
- [ ] `EventClearBall` — **18** (6H/12A)
- [ ] `EventClearForThrow` — **4**
- [ ] `EventClearBallDownRightChannel` — **1**
- [ ] `EventHeadsBallBehind` — **1**
- [ ] `EventOverheadKickClearance` (+ Behind) / `EventSlicedClearance` / `EventPoorHeadClear` /
      `EventClearedBall` / `EventClearBallIntoTouch` / `EventClearBallDownLeftChannel` /
      `EventClearsBallBehind` / `EventGoesToHeadBallBehind` / `EventGoesToClearBallBehind` — 0
- [ ] `EventGoalkeeperClearBall` — **7** · `EventGoalkeeperThrowPass` — **6** ·
      `EventGoalkeeperClearanceFromHeld` — **2**. Keeper *distributing* after securing the ball —
      **same team keeps possession, not a change.** Exclude (listed so you see they were seen).

### B9. Marking / pressure / positioning
- Successes: add the successes to the confident list 
  - [ ] `EventKeyChallenge` — **1** (1H/0A)
  - [ ] `EventDefenderSolidAsARock` / `EventDefenderSolidInAir` / `EventStrikerKeptQuietByDefender` /
        `EventTargetmanShutDown` / `EventUnableToShakeMarker` / `EventForcedBack` /
        `EventForcedToPass` / `EventPlayerForcedWide` / `EventPlayerStopsQuickFreeKick` — 0
- Failures (loss side):
  - [ ] `EventLostMarker` / `EventLostMultipleMarkers` / `EventMarkerBeatenInAir` /
        `EventMarkerTooSlow` — 0

### B10. Fouls & conceding set pieces (illegal defensive act → gives ball away)
Maps to a *loss*, but overlaps the free‑kick / corner cues you already ship. Note the
observed `FoulTrip` events below arrive as both live and `PastTense` (commentary) forms.
- [ ] `EventFoulTrip` — **3** (0H/3A)  ·  `EventFoulTripPastTense` — **5** (commentary echo — exclude)
- [ ] `EventPlayerConcedesCorner` — 0
- [ ] `EventFoulSlideTackle` (+ Slippery) / `EventFoulTwoFootedTackle` (+ LeadsToPenalty) /
      `EventFoulBlockTackle` / `EventFoulJumpforball` / `EventFoulGoalieSmotherAtFeet` /
      `EventFoulGoalieCatchBall` — 0

---

## C. Considered and excluded (with reason)

Move any back up if you disagree.

- **NEW from live data — high‑frequency contest events (not resolved possession):**
  - `EventPlayersRaceForLongBall` — **63** (29H/34A) — a 50/50 race; no winner *at* this event.
  - `EventPlayerLooksToLatchOntoLongBall` — **62** (28H/34A) — anticipation, not a turnover.
  - `EventHeadIt` — **36** — generic "heads the ball," a touch not a possession change.
  - `EventRunPastOpponent` (+ `WithPace`) — **26** / **7** — attacker beats a man (offensive).
  These weren't in the enum‑derived draft and are among the most frequent events in the match —
  exactly the "slips through the cracks" risk. Excluded as contests/touches, not turnovers.
- **All same‑team ball progression** — `EventPass*`, `EventLongBall*`, `EventCross*`,
  `EventOneTwoPass` (58!), `EventRunWithBall*`, `EventTurn*`, `EventLaysBallOff`,
  `EventPassBackToKeeper` / `EventHeaderBackToKeeper` (back to own keeper): possession stays.
- **Keeper distribution** — see B8 note (`EventGoalkeeperClearBall` / `ThrowPass` /
  `ClearanceFromHeld`): same team keeps the ball.
- **Timing / filler** — `EventWait` (64!), `EventHighlightMarker` (26): engine/UI bookkeeping.
- **Past‑tense commentary twins** — every `*PastTense` (e.g. `EventFoulTripPastTense`): narrative
  re‑reference, not a live instant.
- **Half/full‑time reactions, mistake/error narration, disciplinary, VAR/broadcast, offside
  mechanics, aggregate‑tie narration** — as before; no live possession meaning.

---

## Open decisions (now data‑informed)

1. **Win‑side only, or win + loss?** → **Win‑side only, strongly.** The loss‑side family was
   effectively absent live; firing on win events and deriving direction from `Team` gives one
   clean sound per turnover.
2. **Open play only, or also set pieces?** → Recommend **open play only.** Corner / throw‑in /
   goal‑kick / goal cues already exist; the restart events fire plenty (`EventRestartThrowIn`
   16, `EventRestartFreekick` 11, `EventRestartGoalKick` 11, `EventRestartCorner` 6) and would
   double up. Keep this cue out of B10 and set pieces.
3. **How wide is "impactful"?** A2 (shot blocks, off‑the‑line) and B4 saves are defensively huge
   but aren't possession changes, and the big‑save family didn't fire in a one‑sided game.
   Decide whether a great save shares this cue or earns its own — revisit after the defensive
   match populates it.
4. **Cooldown is required, not optional.** `EventGoodIntercept` alone would fire ~every 30–40s.
   Plan a per‑team cooldown (collapse rapid repeats to one) and/or a verbose toggle for the
   noisy families (B3 aerials, B8 clearances).

## Data still needed

One **defensive / underdog match** (you defending a lead or the smaller side). It will populate
the families that stayed at zero in this home‑dominant game: `EventGoodSaveHeld`/`GreatSaveHeld`,
`EventSavingTackle`, `EventBlockedOnLine`/`ClearedOffLine`/`EventKeyClearance`, `EventRobs*`,
`EventPerfectBlockTackle`, `EventScatterIntercept`. Until then, treat their inclusion as
provisional.

---

## Appendix — Match 1 raw sample (defensive / possession subset)

Home‑dominant match, ~1.4 halves, 1,380 total events. Full ranked dump captured during the
session; the possession/defensive‑relevant rows, `count  (H/A)`:

```
84  36H/48A  EventGoodIntercept
46  19H/27A  EventWinsJumpforball
37  12H/25A  EventHeadClear
26   9H/17A  EventRunPastOpponent      (offensive)
18   6H/12A  EventClearBall
12   7H/5A   EventWinsBlockTackle
 8   6H/2A   EventMissedInterception
 7   4H/3A   EventPickUpFreeBall
 7   3H/4A   EventGoalkeeperHeld
 7   1H/6A   EventGoalkeeperClearBall  (distribution — same team)
 6   6H/0A   EventGoalkeeperThrowPass  (distribution — same team)
 6   5H/1A   EventGoalieCatchBall
 4   2H/2A   EventPerfectSlideTackle
 4   2H/2A   EventBlockShot
 3   2H/1A   EventWinsBallInAirAndClears
 3   1H/2A   EventInterceptThroughBall
 3   1H/2A   EventGoalkeeperPunch
 3   0H/3A   EventFoulTrip
 2   0H/2A   EventWinsJumpforballWithHeight
 2   0H/2A   EventGoalkeeperClearanceFromHeld
 1   1H/0A   EventKeyChallenge
 1   1H/0A   EventGoalkeeperTippedOver
 1   1H/0A   EventBadPassInOwnArea
 1   0H/1A   EventLooseBallInArea
 1   0H/1A   EventHeadsBallBehind
```

Contest / filler for reference (excluded): `EventPlayersRaceForLongBall` 63,
`EventPlayerLooksToLatchOntoLongBall` 62, `EventHeadIt` 36, `EventWait` 64,
`EventHighlightMarker` 26, `EventOneTwoPass` 58.
