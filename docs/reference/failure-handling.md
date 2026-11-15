> Upstream FM26 Access reference, release 0.2.16. Check [FM27 migration notes](../fm27-migration.md) before using game-specific APIs.

# Failure handling: Degrade in place, never lie

This is the canonical statement of how the mod handles failure. The root `CLAUDE.md` carries the
short version; this is the full rationale, the mechanism, and the rules.

## The invariant

A blind player cannot glance at the screen to check what they heard. Speech (and the companion HTML
page) is their only source of truth. So the one rule that never bends is:

> **Never present data that could be wrong or stale as if it were correct.**

That is the real meaning of "don't lie". Note what it does *not* say: it does not say "never fail".
Telling the player **"unknown control"** or **"score unavailable"** is not a lie — it is an honest,
actionable admission of a gap, and the player understands it as one. The failure mode we are
guarding against is a confident falsehood: a stale score read as if live, a guessed label, a
fabricated value.

## Why this replaced "crash, don't lie"

The original doctrine was "it is always better to crash the game than to speak wrong information."
That collapsed three different situations into one response:

- We have wrong/stale data and would speak it as truth. (Must never happen — the real lie.)
- We cannot read or classify one thing, but we can say so honestly. (Just needs a marker.)
- The mod's foundation is broken so nothing it says can be trusted. (Genuinely needs to stop.)

Treating all three as "crash the whole game" had two costs. First, a single unreadable widget on one
screen unpatched Harmony and paused the *entire* mod — navigation and speech died everywhere until a
reload. Second, the crash tore down the mod at the exact moment we most need it alive: we now develop
and test against a live game over the dev driver (`/eval`, `/cursor`, `/speech`, `/log`, `/reload`),
and a mod-wide halt makes a localized problem far harder to diagnose. The crash used to be the only
forcing function that surfaced a problem. It no longer is: loud logging, a visible marker, and the
always-on dev endpoints do that job, and a fixed core hot-swaps in via `/reload` without a relaunch.

## The three tiers

**1. Truth.** Read live (never cached) and speak/show the real value. The default.

**2. Degrade in place.** When one element, value, or widget can't be read, classified, or acted on,
emit an unmistakable gap marker, log the reason, record it for the dev driver, and keep the rest of
the mod running. This is the tier that used to be a crash. The player hears the gap; an agent sees it
in `/log`, `/speech`, and `/unclassified`; a fixed core hot-swaps in via `/reload`.

**3. Halt.** `AccessibilityMod.Kill()`. Reserved for *foundation* failures where nothing the mod says
can be trusted: Prism/Speech init, Harmony patch application, input-system init, missing required
navigation delegates. Halt removes the Harmony patches and pauses the core, but deliberately leaves
the dev server up — so a halt stays diagnosable over HTTP and `/reload` recovers without restarting
the game. Prefer the narrowest halt; never halt for a single element.

## Guardrails (so this never becomes "swallow errors")

- **Every degrade logs and is recorded.** Use `Degrade.Note` (logs at Warn into the `/log` ring
  buffer) or route an unknown widget *type* through `UnclassifiedTracker` (it dedupes by type, so a
  per-frame reclassification does not spam the log). A catch that returns a safe default with no log
  and no marker is forbidden — that is the one thing worse than a crash.
- **Degrade only when the marker is self-evidently a gap.** "unknown control" and "score unavailable"
  are safe because the player cannot mistake them for real game data. If emitting anything risks a
  believable falsehood, halt instead.
- **Actions never guess.** A control the mod cannot drive correctly reports `CanActivate == false`,
  so the cursor never fires it — it never performs a guessed action. (The `UnknownAdapter` is the
  canonical case: it describes the widget but does not activate it.)

## The mechanism

**`FM26Access.Speech/Degrade.cs`** — the central helper and the marker words:
`UnknownControl` ("unknown control"), `Unavailable` ("unavailable"), `Unreadable` ("couldn't read").
`Degrade.Note(reason)` logs without returning a marker;
`Degrade.Control/Value/Read(reason)` log and return the matching marker. The markers are centralized
so every surface degrades with the same words. (A full LocString/translation pass for them is
deferred — the mod already speaks mod-authored chrome like "cancelled" and "Mod reloaded" as plain
strings; aligning all mod-authored text to a LocString system is separate work.)

**`UnknownAdapter`** (`FM26Access.Plugin/Adapters/UnknownAdapter.cs`) — the landing adapter for
`ElementClassification.Unknown`. The cursor lands on an unclassifiable widget and announces a
best-effort label plus "unknown control" (rather than the old crash or a silent skip). It reads only
safe Unity properties, so describing a widget the mod does not understand can never itself throw, and
it does not activate (we do not know how to drive it). `Unknown` is a navigation stop
(`TreeWalker.NavigationStops`) and routed to this adapter in `ElementBridge.CreateAdapter`; the HTML
walker (`ScreenWalker`) renders it the same way instead of dropping it.

**Boundary catches** — the deep read/activate sites still `throw` on a genuine "I can't do this", but
that throw is now a *localized signal*, not a mod-wide death. It is caught at the boundaries that
invoke adapter/context code:
- `CoreLoop.TickDone` wraps the per-frame interactive loop: a failure logs and continues next frame.
- `Announcer.GetDisplayAnnouncement` (Navigation/Core) degrades the spoken announcement to a marker,
  so the player hears "couldn't read" rather than silence.
- The Harmony patch handlers (`PanelManagerPatches`, `NotificationPatches`, `ContinueStatePatches`)
  log and continue, so one bad panel/notification event does not tear the mod down.
- The HTML surface (`ScreenWalker`, `HtmlRenderer`) already catches per element and degrades by
  rendering a marker or omitting with a log.

## Where each tier applies

Degrade in place: unknown `IBindableElement` type; an adapter whose reflection contract broke (a
property/method it needs is gone); a per-element child that's missing; an unreadable panel title; a
score or commentary read that failed; a destroyed VisualElement mid-announcement.

Halt: Prism init failure; missing Harmony patch target; input-system init failure; missing required
navigation delegates. (A missing embedded HTML resource breaks only the companion page, not speech,
so it throws to the server request handler rather than halting the mod.)

Fails closed, then halts only if it persists: the interactive-set query (`InteractivePanels.Current`).
The invariant it protects is "never present a blocked panel as usable", and *throwing* satisfies that
in full — nothing reaches the page. Halting was doing something extra: ending the session over one bad
instant, on a query that runs on every page build, so a panel torn down mid-read took speech down with
it. It now throws (the render says "unavailable"), and halts only after five consecutive failures,
which means the contract really is broken rather than momentarily busy. Erring patient is deliberate:
a premature halt costs the player their session, a late one costs a few renders that were already gaps.

Warn and continue: sound-effect failure, haptic feedback, any non-speech supplementary feedback.

## After a halt: what the surface says

Halting is not the end of the story for the companion. `Kill()` pauses the core's tick on purpose and
leaves the server running so a fixed build can be hot-swapped in — but the tick is also what drains
the main-thread hand-off every server read depends on, so from that moment every route would wait out
its budget and fail. It used to fail with the raw dispatch text ("Main-thread dispatch timed out (is
the game updating?)"), spoken for an action and rendered as the player's *entire page* for a screen
read. That names the mechanism, not the situation, and is identical whether the mod stopped, the mod's
tick is failing, or the game itself stopped running frames.

So the surface reports state, via `SurfaceHealth` (pure, in Navigation) fed by `MainThreadDispatcher`:

| State | Means | The player hears |
| --- | --- | --- |
| `Loading` | not interactive yet | "The game is still loading." |
| `Live` | draining normally | nothing — the screen speaks |
| `ModStopped` | `Kill()` paused the tick (recorded, not inferred) | "The accessibility mod has stopped. Restart the game." |
| `ModFailing` | the tick is throwing, so the drain is skipped | "The accessibility mod is not responding." — plus "Restart the game." once the stall passes `ProlongedMs` |
| `GameNotResponding` | nothing is running the frame at all | "The game is not responding." — never a restart prescription |

**Advice is a claim too.** "Restart the game" costs the player their unsaved progress, so a state only
prescribes it once the evidence supports it. A failing tick may recover, so it is described for the
first 30 seconds and only then advised on. A game that is not responding is never told to restart at
all, however long it takes — it may simply be busy (a 50s pause building a heavy screen was measured
live), and a restart on the mod's suggestion would destroy work the player could otherwise have saved.
`ModStopped` is the one state that prescribes immediately, because it does not recover on its own and a
blind player cannot use the game without the mod.

Three rules follow, and they apply to any failure the player can perceive:

- **Never speak the mechanism.** A dispatch, a thread, a timeout are not things a player can act on.
  Say which side stopped and what to do. `ServerText.Failure` is the shared path: the exception detail
  goes to the log, a short truth goes to the player.
- **Never render an exception as the page.** A screen read that fails says what happened as a heading,
  not as a stack trace.
- **Never wait for a queue nothing is draining.** The routes check health *before* enqueuing, so a
  stopped mod answers instantly instead of costing a two-second silence per keypress.

`GET /liveness` reports all of it, and is `AlwaysReady` so it answers in exactly the situations the
other routes cannot.

## Below the tiers: failures no tier can reach

The three tiers all assume a failure arrives as a managed exception, somewhere a `catch` can see it.
Some failures do not. Under IL2CPP the runtime can **fail fast**: the process dies instantly, with no
exception, no log line and no marker, and from outside it is indistinguishable from the game itself
crashing. A `catch` around the offending call does nothing, because nothing unwinds.

The signature, confirmed from WER full dumps (`0x80131506` in `coreclr.dll`, a dozen occurrences at an
identical fault offset):

```
IL_STUB_ReversePInvoke      ← the game calling INTO managed code
  Invoker_VoidThis          ← Il2CppInterop invoking a game method from managed code
    IL_STUB_PInvoke         ← dies here
```

**Do not read the absence of mod frames as evidence about what ran.** That mistake cost this project
five days. The stack walker truncates at the reverse-P/Invoke stub, so "no `FM26Access` frame on the
faulting stack" was taken to *rule out* the screen walker — which turned out to be exactly where the
crash was. Frames you cannot see are not frames that did not run. Likewise the last line in
`Player.log` is not the crash site: a fail-fast does not flush Unity's log buffer, so whatever was
buffered is lost, and the surviving last line is an innocent bystander.

### What actually causes them

**Driving a game widget behind its own back.** The mod opens a dropdown by calling `ShowPopup` on the
widget directly, which the widget's own open/closed state never learns about. Activating that widget
while its popup is up therefore does not toggle it shut the way a mouse click would: the game opens a
second popup over the orphaned first, and tearing that down kills the process. Reproduced by clicking
the Tactics "Quick Pick" button while its own dropdown was opening — and *only* when the click landed
between two of the open sequence's main-thread steps. Fully sequential was always safe, which is what
made it look intermittent and untraceable to a player.

The general rule: **any mod-driven action that leaves the game in a state its own controls did not
author must be stood down before those controls are used.** `DropdownSessionManager.CloseBeforeActivation`
is the worked example — `/activate` closes an open popup first, and takes `SessionGate` so a click can
never interleave with an open still in flight.

The other confirmed case (July 2026) was **walking null**: `HtmlRenderer.RenderTable` iterated a streamed
table's `Rows` when they were still `null`, because that section had been rendered without the
streamed load phase ever running. Its sibling renderers all guarded the same field; that one did not.
The lesson generalises past this bug: **an item whose data is filled in by a later phase must never be
rendered off the walk**, and a renderer must not assume its phase ran. `AccessibilityServer.IsStreamedItem`
is the single definition of "needs the load phase"; use it rather than restating the type list.

Three other IL2CPP hazards the mod has paid for, all of the same invisible-until-fatal shape:

- **A delegate handed to the game and then collected.** The game keeps a raw pointer; .NET and IL2CPP
  each govern one side of its lifetime and nothing coordinates them. Any delegate handed to the game
  must be rooted for as long as the game could invoke it — a `static readonly` field roots the managed
  target *and* holds the IL2CPP GC handle. Never per panel, per element, or per render.
  `NavigationEventBlocker` is the worked example. (This was the first diagnosis of the crash above and
  it was **wrong** — the rule is sound on its own merits, but it did not explain that bug.)
- **Reflecting a generic-over-struct member**, which boxes an IL2CPP object per read.
- **Calling into the game from a thread never attached to its domain.**

The defence for all of them is the convention, not a `catch`.

### Diagnosing one

For reading the WER dump itself, use `tools/crash-dumpread` (developer-only; see `tools/README.md` —
nothing there ships, and it must stay that way). But a dump alone rarely names the culprit: its stack
truncates at the interop boundary.

A fail-fast leaves nothing behind, so the only reliable instrument is a probe that writes each step to
disk and flushes before returning — the file's last line is then genuinely the step that died. Pair it
with a switch that turns a suspect feature off: since the failure cannot be caught, "does this code
cause it?" can only be answered by not running it. Both were built and thrown away for the crash
above; rebuild them if another appears rather than reasoning from a stack that truncates.
