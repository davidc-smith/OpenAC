# Rynth plugin pump thread

Date: 2026-10-21. Branch: `feat/rynth-pump-thread` (from main ff28e715).

## Why

Live test 2026-10-21 (RynthNav goto to Samurai Hausu) was very choppy. A dotnet-trace of the
running client showed RynthNav's `Replan()` → DotRecast `FindPath` blocking the frame thread
for 450-520 ms every ~0.7 s (11 of 11 samples), because the goal was unreachable on the
navmesh and the search flooded every loaded tile. Between re-plans the client drew a frame in
~6 ms (~100 fps).

RynthCore runs plugin ticks on its own pump thread (`EntryPoint.StartNormalPluginPump`,
`NormalPluginPumpInterval` = 16 ms), so there a slow tick slows only the plugin. Our host
calls every plugin's `OnTick` inline from `RynthClientExtension.Tick` on the frame thread,
so a slow tick freezes the game. Our tick rate is also the frame rate (~100 Hz), not
RynthCore's 16 ms pump, and RynthNav's tick-counted timers assume RynthCore's cadence.

(The unreachable goal itself is a navdata/RynthNav matter, reported separately: the goal
probe at Samurai Hausu lands on small unlinked islands on the portal's own geometry in tile
0x4CE2, regions 120337-120339, which have no route-graph portals.)

## Decisions

- **All Rynth plugins move to one pump thread** (user, 2026-10-21), as in RynthCore.
- **Snapshots + action queue** (user, 2026-10-21): RynthCore's model. Off the frame thread,
  reads are answered from an immutable snapshot the frame thread publishes; actions are
  queued and run on the frame thread. Chosen over a lock-step "world gate" (deadlock risk
  between plugin locks and the gate) and over pacing alone (doesn't stop the freeze).
- **Their code, unchanged** (standing rule). Only our host, our stand-ins and our client
  extension API change.
- **RynthCore semantics for queued actions**: an action returns true when queued; the queue
  is a bounded ring; starts are refused while portalling or dead; stops and cancels always
  get a reserved slot; a full ring drops and counts, never runs off-thread.
- **No plugin contract change; `ApiVersion` stays 86.** The client extension API gains one
  member (screen projection capture, §2.6).
- **Kill switch**: `ACDREAM_RYNTH_PUMP=0` or session-config `"rynthPump": false` keeps today's
  inline frame-thread ticking.

## Design

### 1. Threads and routing

**Pump thread (new).** `RynthPump` owns a dedicated background thread. Each pass, at most
every 16 ms and only once a snapshot newer than the last pass's has been published, runs in
order:

1. `RynthEventBridge.DeliverQueued` (events).
2. Nav3D `BeginTick`, every plugin's `Tick()`, Nav3D `CommitTick` (as `RynthHost.Tick` today).
3. The pump half of `RynthEngineHub.Tick`: `PromoteDeferredRefresh`, the `BeforeDrain`
   hooks, `UiDataHub.Drain`, `PollAll` (RynthCore runs `UiDataHub.Step` on its pump).
4. `RynthVendorTrades.Tick` (its 10 s timeout).

A slow pass delays only the next pass.

**Frame thread (today's).** Keeps everything RynthCore runs on AC's own threads: ImGui
`DrawAll`, the bar, chat interceptors and the chat keyboard, hotkeys, drop claims, the
`RynthChatWindow`/`TickChat` work, `RynthInventoryHookup.Tick`, `InventoryModel.MainThreadTick`
(its `AfterDrain` part moves to a frame-side hook), and `RynthMotionHolds.Sync` (held keys
re-sent each frame). At today's Rynth tick point it:

1. drains the action queue (§3), running each action on the live view in order;
2. if the pump is waiting for its next pass, builds and publishes a snapshot (§2) and wakes
   the pump. While the pump is busy no snapshot is built, so a long pass costs no frame time.

**Routing by thread.** Today's `RuntimeRynthGameView` becomes the *live* view.
`SnapshotRynthGameView` answers every read from the latest published snapshot and sends every
action to the queue. `ThreadRoutedGameView : IRynthGameView` forwards to the live view on the
frame thread and to the snapshot view on any other thread (the pump, and plugins' own
`Task.Run` workers, which already call slots off-thread today). `RynthApiTable` and every
hook delegate (RynthChatHookup, RynthInventoryHookup, RynthMapHookup, RynthAiHookup) take the
routed view, so the ~115 slots need no per-slot change. The frame thread's identity is
latched when the extension starts (`RynthFrameThread.IsCurrent`).

Our stand-ins follow the same rule: `MainThreadGuard.IsOnMainThread` (SkillsStandIns) returns
`RynthFrameThread.IsCurrent` instead of always true; the `AcMainThreadQueue` stand-in
(DashboardStandIns) runs immediately on the frame thread and enqueues (§3) elsewhere.

**Lifecycle.** Login, logout (`OnLeavingWorld`), `Dispose` and the `rynth` launch switch
*quiesce* the pump: the frame thread stops scheduling passes and waits for the running pass
to end (up to 5 s; on overrun it logs and proceeds). It then runs `OnLogout`, teardown or
login replay itself while the pump is idle, and restarts the pump. `OnLeavingWorld`'s
synchronous `plugin.OnLogout()` keeps its ordering this way.

**Kill switch.** With the pump off, `RynthClientExtension.Tick` runs today's sequence inline
and the routed view always takes the live path (no snapshots built).

### 2. The snapshot

`RynthWorldSnapshot` is immutable and published with `Volatile.Write`; the pump reads one
snapshot per pass and keeps it for the whole pass. Building it must stay cheap: copy what is
small every time, copy what is big only when its revision moved, and share unchanged parts
with the previous snapshot by reference.

#### 2.1 Every publish (small)

Player id, selected object, account and world names, `IsPortaling`, dead (current health 0,
for §3's admission rule), player heading, busy
count, pending item request, combat mode, attack in progress, cast gesture active, autorun,
current movement intent, open container id, server replies (`ServerReplyLog.Snapshot`), the
server-time sync pair (§2.5), the screen projection (§2.6).

**Entities**: for every active entity in `EntityObjects.Entities`, one struct: cell id,
origin, orientation (the body's, as `TryGetPlayerPose` reads it today), raw physics state,
motion-on, sub-palettes (from `record.Snapshot`, an immutable record struct). A flat array,
rebuilt each publish (~40 B per entity). Serves `TryGetPlayerPose`, `TryGetObjectPosition`,
`TryGetObjectHeading` (now from the entity orientation, not `ProjectWorldObject`),
`TryGetObjectState`, `TryGetObjectMotionOn`, `GetPalettes`.

#### 2.2 Objects (when changed)

`ClientObjectTable` has one global `MutationRevision` and no per-object revision, but its
`ObjectAdded/Updated/Moved/Removed/Cleared` events carry the object. `RynthObjectMirror`
subscribes on the frame thread and keeps a dirty set. At publish it deep-copies only dirty
objects (all scalars and the seven `PropertyBundle` dictionaries; today's `TryGetObject`
hands out the live dictionaries, which must stop), drops removed ones, and shares the rest
with the previous snapshot. When `MutationRevision` moved it also rebuilds the
container-contents and equipped-by indexes and the `LiveObjectIds` array. The player object
uses today's `Merge` clone.

Serves `TryGetObject`, `TryGetObjectName`, `TryGetInt64Property` (non-player),
`GetContainerContents`, `TryCountContents`, `LiveObjectIds`, `InventoryRevision`. Derived
predicates (`IsOwnedItem`, `IsOwnedTinkeringTool`, `IsAttackable`) and each object's
`GetObjectEnchantments` are filled at publish by calling the live view for every object,
when `MutationRevision` (or the player id) moved, else shared. Filling the snapshot from
the live view's own answers keeps the two in agreement by construction.

#### 2.3 Character (when changed)

Vitals, base vitals, vital bases, skills, attributes, `PlayerProgress`, the player's
`Int64` properties: rebuilt when `RuntimeCharacterState`'s character revision or
`LocalPlayerState.DescriptionRevision` moved, otherwise shared.

#### 2.4 Spellbook (when changed)

`PlayerEnchantments`, `PlayerStatMods`, `VitaeMultiplier`, `KnownSpells` (plus a
`FrozenSet<uint>` for `IsSpellKnown`): filled from the live view when the spellbook revision
moved, and at least once a second otherwise, so expiries show without a revision bump.

#### 2.5 Clocks

`ServerTime` is computed live today (last sync + wall-clock elapsed). The snapshot carries
the sync pair (sync ticks, sync wall-clock) and the pump computes "now" itself, so it stays
current through a long pass. The effect clock is published the same way.

#### 2.6 Screen

`TryWorldToScreen` needs the live camera. `IClientScreen` gains
`bool TryCaptureProjection(out ClientScreenProjection projection)`: an immutable record of the
view-projection matrix, viewport size and the live world origin, with a pure
`TryProject(PluginNavigationPosition, out Vector2)` that gives exactly what `TryProject`
gives for the same frame. The snapshot carries the captured projection, viewport size and
radar disc.

#### 2.7 Read directly (already thread-safe)

`TryGetTargetVitals` and `TryGetHealthRatio` read `ConcurrentDictionary`s: the snapshot view
reads them live.

#### 2.8 Vendor and trade (when changed)

`Vendor` is rebuilt when `RynthVendorTrades.Generation` or `MutationRevision` moved (owned
counts and coins depend on inventory); `Trade` when the trade counters' generation or
sequence moved.

### 3. The action queue

`RynthActionQueue`: a bounded ring (256), multi-producer (a short lock), single consumer (the
frame thread). Each entry is the action, its arguments as values, and a label for logs.

- **Admission** (RynthCore's `AcMainThreadQueue`): stops and cancels (`StopCompletely`,
  `StopMovement`, `SetAutoRun(false)`, `ClearMovementIntent`, `CancelAttack`, `TradeDecline`,
  `TradeClose`, `CloseContainer`, releasing a held motion) may use a 32-slot reserve. Starts
  are refused while the snapshot says portalling or dead. A full ring drops the entry and
  counts it; the count is logged at most once per 10 s.
- **Return to the plugin**: true when queued, false when refused or dropped.
- **Drain**: in order, at the frame tick point, before the snapshot is built, so the next
  pass sees its own effects. Each action calls today's live view method. A false result or a
  throw is logged (rate-limited per action kind) and never retried.
- **Covered**: every action member of `IRynthGameView` (select, use, use-on, identify, health
  query, combat mode, move, merge, wield, give, external move, close container, salvage,
  cast, melee, missile, cancel attack, reset busy, request advancement, autorun, heading,
  movement intent, stop, jump, vendor buy/sell, trade ops), `SubmitChatCommand` and
  `WriteChatLine`, and `RynthMotionHolds` changes (`SetMotion`, `DoMovement`,
  `StopMovement`, `Refresh`), which now live on the frame side.

**Actions that answer more than "queued".**

- `VendorBuy` / `VendorSell` (`out notice`): `RynthVendorTrades` validates against the
  snapshot's vendor on the pump and gives its notice as today; the request then goes through
  the queue. A failure found at drain time is reported through `RynthVendorTrades`' existing
  status / completion path. Its state gets a lock, since completion arrives on the frame
  thread.
- `UseObject`'s salvage-panel check (`IsOwnedTinkeringTool`) runs on the pump against the
  snapshot; anything else is queued.
- `CastSpell` is refused on the pump when the snapshot says portalling, else queued.
- `MoveItemExternal` chooses loot, drop, move or give at drain time from live state, as today.
- `SelectObject` returns true when queued; the selection shows in the next snapshot.

**Read-your-writes.** A read in the same pass as an action sees the state from before the
action. That is RynthCore's behaviour too; nothing compensates for it.

### 4. Shared host state, errors, testing

**State both threads touch**, made safe:

- `RynthEventBridge` queues: `Enqueue` (frame) and `DeliverQueued` (pump) under a lock;
  `DeliverQueued` swaps the lists under it and runs them outside it. `RynthCombatEvents.OnLoggedIn`
  reads its combat mode and selection from the snapshot view, not the live owners.
- `RynthApiTable`'s `_nameBuffer` / `_stringBuffer`: per thread (`[ThreadStatic]`);
  `_interfaceFailures`: a lock.
- `RynthDispatch.Current` (`[ThreadStatic]`): set on the pump thread around plugin calls,
  as on the frame thread today.
- `RynthNav3D`: filled and committed on the pump; its anchor cell and pose from the snapshot;
  `PluginWorldTriangleStore.SetTriangles` already copies and swaps under a lock.
- `RynthSalvagePanel`: opened from the pump (via the UseObject check), drawn on the frame
  thread: its item list behind a lock.

**Errors.**

- A plugin's throw in a pass is caught and logged per plugin, as today. 50 failing passes in
  a row stop the pump with a chat notice (RynthCore's rule); `/rc` or a relog restarts it.
- A snapshot build that throws is logged; the previous snapshot stays and the pump isn't woken.
- A quiesce that overruns 5 s is logged with what the pump was running; the frame thread
  proceeds.

**Testing** (Rynth.Host.Tests, plus the new pieces):

- The pump takes an injectable scheduler: a real thread in the client, a manual one in tests
  that runs one pass on a separate thread and joins it, so tests stay deterministic and the
  routing is real. The existing suite keeps running inline (pump off, the routed view always
  live); pump mode gets its own tests, including one that loads the real plugins and runs
  passes through the manual scheduler.
- Routing: frame thread → live view; other threads → snapshot view; stand-ins follow it.
- Snapshot parity: on a populated runtime fixture, every read member answers the same through
  the snapshot view as through the live view right after a publish (table-driven over the
  read members).
- Dirty tracking: an updated object is recopied, unchanged ones are shared by reference; a
  publish with nothing changed allocates only the entity array.
- Action queue: order, capacity, reserve, refusal while portalling, drop counting, drain-time
  failure logging, vendor notices.
- Pump: a plugin whose tick blocks 500 ms leaves the frame tick under 2 ms; quiesce on
  logout waits for the pass; `OnLogout` runs with the pump idle; 50-failure stop.
- Projection: `ClientScreenProjection.TryProject` matches `IClientScreen.TryProject`.

**Live check** (user): the Samurai Hausu goto with no stutter (trace frame p99 unchanged
while it re-plans); a RynthAi patrol with combat and looting; vendor buy/sell; dashboard
panels, nav panel and arrow, world/dungeon map; chat commands and `/rnav`; logout and login;
the kill switch.

## Out of scope

- RynthNav's navdata island at Samurai Hausu, and its trip-path line disagreeing with the
  walker's straight fallback: upstream report to the RynthNav author.
- Making DotRecast's search cheaper (their code).
- A read-your-writes overlay for queued actions.
