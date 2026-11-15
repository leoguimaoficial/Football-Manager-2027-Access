> Upstream FM26 Access guide, release 0.2.16. Current FM27 commands and compatibility limits: [migration notes](fm27-migration.md).

# Hot-reload plan: host/core split with POST /reload

## Goal

Iterate on the mod without relaunching the game. Rebuild the mod's logic assembly,
POST `/reload`, and the new code is live in the running game. This serves the dev-driver
workflow: an agent driving the game over HTTP can reload the mod itself, with no human at
the keyboard and no F6 keypress.

## Why a split is unavoidable

Four things initialized in `FM26AccessPlugin.Load()` cannot be re-run in a live IL2CPP
process, so they cannot live in a reloadable assembly:

1. **IL2CPP type injection** — `ClassInjector.RegisterTypeInIl2Cpp<AccessibilityBehaviour>()`
   is one-way. There is no unregister, and re-injecting the same type throws. A Mono mod
   (e.g. soc-access, which this idea came from) never hits this; we do.
2. **The native Prism load** — `NativeLibrary.Load(prism.dll)` cannot be undone.
3. **The companion process** — relaunching on every reload would stack duplicate windows.
4. **(Indirectly) Speech init** — `SpeechEngine.Initialize()` and the `SpeechPipeline`
   filter registration are one-time; re-running would double-register filters.

Everything else — Harmony patches, tree walkers, adapters, contexts, HTML rendering, the
server route handlers, the dev endpoints, and the behaviour's diagnostics/input/rescan
logic — is reloadable.

## Architecture

Two BepInEx-visible pieces plus the existing permanent shared assemblies.

- **`FM26Access.Host`** (new, permanent). The BepInEx `BasePlugin`. Loaded once, never
  reloaded. Owns the four immovable resources above, plus Speech init and the `Log`
  ring-buffer wiring. Injects a thin `HostBehaviour : MonoBehaviour` whose `Update` just
  forwards to a swappable core tick. Loads the core assembly from disk via
  `Assembly.Load(bytes)`, finds `FM26Access.Plugin.CoreEntry`, and calls `Mount`. Owns the
  reload thread.
- **`FM26Access.Plugin`** (existing, becomes the reloadable core — keeps its name so
  `InternalsVisibleTo` and the deploy targets are unchanged). Loses its `[BepInPlugin]` /
  `BasePlugin`. Gains `CoreEntry.Mount(IHostBridge)` / `CoreEntry.Unmount()`. `Mount` is the
  current `Load()` body minus the four permanent ops; `Unmount` is the new teardown.
- **`FM26Access.Speech`, `FM26Access.Navigation`** (existing, permanent). Referenced by
  both host and core. Not reloaded. `NavigationManager` (Navigation) keeps its cursor state
  across reloads; the `NavigationHost` the core wired is dropped on `Unmount` and a fresh
  one is built and passed to `NavigationManager.Initialize` on `Mount`.

The **host does not reference core at compile time** (it loads it by reflection, which is
what makes reload possible). Core references the host assembly for the `IHostBridge`
contract. Compile dependency: Core to Host. Runtime load: Host to Core. No cycle.

### The contract (`IHostBridge`, in Host)

What core needs from the permanent host:

- `Tick` registration — host's injected `HostBehaviour.Update` calls the core tick each frame.
- `RequestReload()` — core's `/reload` handler asks the host to reload core off the server
  threads.
- Accessors for anything permanent core must reach (prism path is already resolvable from
  `BepInEx.Paths`, so this stays minimal).

`MainThreadDispatcher` stays in **core**: the TCP server lives in core, so its worker
threads and the dispatcher queue are recreated together on each reload. Host's
`HostBehaviour.Update` calls the core tick, and the core tick drains the dispatcher. No
in-flight dispatch survives a reload because `Unmount` stops the server first.

### The TCP socket

Stays in **core**. `Unmount` stops the accept loop and closes the listener (which unblocks
`AcceptTcpClient`); `Mount` rebinds. Set `ExclusiveAddressUse = false` so the rebind does
not trip "address already in use" during the brief close/reopen. This keeps `AccessibilityServer`
whole rather than splitting its SSE loop across the boundary.

## Reload sequence (POST /reload)

1. Core's `/reload` handler writes a `reloading` response and returns immediately (the
   socket is about to close).
2. It calls `IHostBridge.RequestReload()`, which posts to a **host-owned** reload thread
   (survives core teardown).
3. Reload thread: `CoreEntry.Unmount()` (stop server, `UnpatchSelf`, drop the
   `NavigationHost`, reset cursor, dispose input poller) then host loads fresh
   `core.dll` bytes and calls `CoreEntry.Mount(bridge)`.
4. Agent polls `/health` until the server is back, then re-inspects.

Between Unmount and Mount the `HostBehaviour` tick is a no-op (mod paused); that is fine.

**Gotcha — reload empties the panel history.** Unmount's `NavigationManager.ResetForReload()` calls
`ResetValidation()`, which sets `_activePanel = null` *and* `_panelStack.Clear()`. The new core's
`ReestablishActivePanel()` (reload-only) restores just the single currently-open panel via
`OnPanelReady`; because `_activePanel` was reset to null first, that call pushes nothing, so the stack
stays empty. Net effect: after any `/reload`, the navigation history is gone. The first time you close
or back out of the panel you reloaded on, `OnPanelClosed` finds the empty stack, logs "no panel to
restore," and leaves the cursor with **no active panel** until the next panel-load event (e.g. a
navigation) re-establishes one. This is reload-only — cold boot builds the stack from the game's real
`PanelLoaded` events — so it has zero production impact (players never `/reload`). The visible symptom
during dev is that `/gui` would go blank in that window; `/gui` now falls back to the topmost
interactive panel, so it does not. Not worth "fixing" by rebuilding `_panelStack`: the only source is
PanelManager's live layer stack, which is not the nav history (and the persistent ribbon would become a
bogus restore target), so a reconstruction would make post-reload back-navigation behave wrongly.

## Recovery from a failed Mount

`Mount` starts the server (and wires the dev-driver ring buffers) *before* the navigation
wiring and Harmony patches that can throw, and `/reload`, `/log`, `/speech`, and `/events/log`
are marked `Route.AlwaysReady` (they touch no live game state, so they run before the dispatcher
is Ready). Together these make a reload whose `Mount` throws recoverable without a relaunch: the
server stays up, `/log` shows why `Mount` failed, and `/reload` still triggers — so a fixed
rebuild plus another `/reload` brings the mod back. Only a failure in `Mount`'s first few lines,
before the server starts, needs a game restart.

## Reload boundary (what hot-reloads vs. what needs a full restart)

Hot-reloads (lives in core / loaded JSON): Harmony patches, `ElementBridge`/classification,
`TreeWalker`, adapters, contexts, `ServerHandles`, dev endpoints, HTML rendering, panel
profiles (JSON — already runtime-loaded, could even get a `/reload-profiles` fast path).

Needs a full game restart (lives in permanent assemblies): `NavigationManager` core,
`ProfileLoader`/`ProfileRegistry`, Speech engine/filters, the host itself. These change
rarely; widening the boundary (moving Navigation into core) is a later option once the
mechanism is proven.

## Memory note

First cut uses non-collectible `Assembly.Load(bytes)`: each reload leaks the previous core
assembly (hundreds of KB). Over a dev session of dozens of reloads this is irrelevant. A
collectible `AssemblyLoadContext` is a later optimization and carries IL2CPP-interop sharp
edges (GCHandles, generated delegates, Harmony dynamic methods) that the non-collectible
path sidesteps. `Unmount` must `UnpatchSelf` before any unload regardless, or dangling patch
trampolines crash.

## Build sequence (each stage compiles)

1. **Mount/Unmount seam inside the current assembly.** Split `Load()` into the permanent ops
   (kept in `Load`) and `Mount()` (the rest), and write `Unmount()` (teardown). Still one
   assembly, still a `BasePlugin`; mod runs identically. Gets the riskiest logic — clean
   teardown — written and reviewable first.
2. **Extract `FM26Access.Host`.** New BepInEx plugin project. Move the entry, the thin
   injected `HostBehaviour`, the `IHostBridge` contract, prism load, companion launch, and
   Speech init into it. Strip `BasePlugin`/`[BepInPlugin]` from `FM26Access.Plugin`; add
   `CoreEntry`. Behaviour state machine + input + rescan move from `AccessibilityBehaviour`
   to a core `CoreLoop` driven by the tick. Host loads core and calls `Mount`. Verify cold
   start is identical.
3. **Wire POST /reload.** Host reload thread + `RequestReload`; core `/reload` endpoint.
   End-to-end test: change an adapter string, rebuild core, POST /reload, confirm live.
4. **Polish.** Quiesce in-flight requests on reload, optional `/reload-profiles`, docs in
   `claude-in-game-verification.md`, optional collectible ALC.
</content>
</invoke>
