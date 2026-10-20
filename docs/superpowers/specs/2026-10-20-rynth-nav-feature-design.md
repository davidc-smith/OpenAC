# RynthNav as a feature (sub-project B)

Date: 2026-10-20. Branch: `feat/rynth-nav-feature` (from main 5cb2c1c8).

## Why

Sub-project A (`2026-10-19-rynth-nav-detours-design.md`) loads RynthNav as a whole plugin
but only builds and checks its navmesh path service, which RynthAi's outdoor detours use.
B makes RynthNav itself usable: its `/rnav` commands, RynthCore's RynthNav panel and arrow
overlay, and its goto / Atlas travel / portals / recalls / Town Network, with the host slots
those need and a live check.

## Decisions

- **Scope: the full feature** (user, 2026-10-20): command routing, both faces, a bar button,
  the three missing host slots, chat coordinate links, live check of every travel kind.
- **Their code, unchanged** (standing rule): RynthCore's faces and RynthNav compile from the
  submodules as they are; stand-ins only where they reach panels or engine parts OpenAC
  doesn't have.
- **Every typed line reaches the plugins** (user, 2026-10-20), as in RynthCore, not only the
  verbs we register. Done through the existing plugin chat input interceptor, so no new client
  API.
- **Their defaults**: portal routing off until `/rnav portals on`, recalls on, settings in
  their `rynthnav.json`, Town Network from the installed `townnet.json`.
- **No contract bump; `ApiVersion` stays 86.** Only null slots get filled.

## Design

### 1. Chat lines to plugins

Today a Rynth plugin's `OnChatBarEnter` only sees the verbs the extension registers (`/ro`,
`/ginv`, `/ra`, `/mt`, `/ub`, `/vt`, `/lua`), forwarded through `OfferChatBarText`. RynthNav
also learns a recall command from the line typed just before "X is recalling home."
(`_lastChatCommand`), and RynthLua's script-defined slash commands never arrive.

- `RynthClientExtension` registers one interceptor through
  `context.Automation.Chat.RegisterInputInterceptor`. For each line it calls
  `OfferChatBarText`; a plugin that eats it → `PluginChatInputDecision.Suppress`, else `Pass`.
- `ChatCommandRouter` consults interceptors for every line submitted through it: OpenAC's chat
  bar, the floating chat windows, and every `InvokeChatParser` call (Rynth chat's input box
  sends through RynthChat's `SendLine` → `InvokeChatParser` → `SubmitChatCommand`). The client's own commands (`RetailClientCommandCatalog`, `/help`) are
  matched first and never reach plugins; server commands such as `@lifestone` do.
- **Lines a plugin submits itself** (`InvokeChatParser`) are offered to the plugins too, as in
  RynthCore: its `InvokeParser` runs `ChatCommandDispatcher.Dispatch`, whose plugin
  pre-dispatch offers every line to each plugin's `OnChatBarEnter` (that is how `/rnav` typed
  in Rynth chat reaches RynthNav there). Every line is offered to every plugin, in load
  order, with one shared eat flag (an eaten line still reaches later plugins, as RynthLua
  expects). A line a plugin submits from inside its own `OnChatBarEnter` skips the plugin
  pre-dispatch, as RynthCore's `_inPluginPreDispatch` does.
- The forwarding registrations (`ro`, `ginv`, `ra`, `mt`, `ub`, `vt`, `lua`) are removed; the
  interceptor reaches every plugin before plugin verbs are consulted. `/rynth` and `/rc` stay
  registered (the host answers them, not a plugin).
- Without an automation surface (tests, headless) no interceptor is registered and nothing
  reaches the plugins' chat handler; that matches today for unregistered text.
- An interceptor that throws is reported once by `ChatInputInterceptors` and chat carries on.
  A plugin that throws inside `OnChatBarEnter` is already caught by `RynthPlugin`.

### 2. The panel and arrow

In `AcDream.Rynth.EngineUi`:

- Compile `ImGui/Panels/RynthNavFace.cs` and `ImGui/Panels/RynthNavArrowFace.cs` unchanged.
  `UI/Data/RynthNavData.cs` and `RynthNavAtlasData.cs` are already compiled (Dungeon Map).
- Delete our `RynthNavFace` stand-in (`StandIns/ChatEngineStandIns.cs`); the real face's
  `Title` is the same, so ChatFace's "Arrow to" / "Go to" menu appears once it is registered.
- New stand-ins, title constants only, never registered: `WorldMapFace`, `MiniMapFace` (the
  panel hides its map buttons when `HasFace` is false; the World Map stays blocked on RynthNav
  maps). Further stand-ins for what the arrow face reaches (`SenseFace`, `ResolveDockedFace`,
  members of `PlayerPhysicsHooks`, `RynthAiFace` uses) reuse existing stand-ins where they
  exist; the exact list is fixed in the plan.
- New `RynthNavPanel` (Start / Stop, like `RynthAiDashboard`): registers `RynthNavFace` (which
  registers the arrow face and subscribes `RynthNavArrowWatch`, so `/rnav arrow Holtburg` opens
  the arrow with the panel shut), and hands its sources to the hub (section 3). Started by the
  extension only when the RynthNav plugin is loaded in the host (its init succeeded), the way
  RynthCore checks `HasPluginDll("RynthCore.Plugin.RynthNav.dll")`. Register calls are guarded
  by `HasFace`, so a panel host reset re-registers.
- Bar: `"RynthNav"` joins `PanelButtons`; `RynthBar` already maps it to Phosphor
  `NavigationArrow`. The bar only draws registered titles, so no button without the plugin.
- Window rects follow the existing per-display layout store; the faces hide at the character
  list like the other Rynth windows.

### 3. One engine hub

The source pump (posted work + polling subscribed, due sources) lives in
`RynthAiDashboard.Tick` and only runs with RynthAi started, so RynthNav alone would leave the
panel dead.

- New `RynthEngineHub` in EngineUi: `Add(sources)` (idempotent per source), `Tick()` =
  `PromoteDeferredRefresh` on each source, `UiDataHub.Drain`, then the existing `PollAll`
  (per-source try/catch, a throwing source logged and marked polled), `Stop()` = clear the
  queue, `PluginsUnloaded`, reset sources.
- `RynthAiDashboard` keeps its RynthAi-specific per-frame steps (`PlayerProgressHooks.Prefetch`,
  `SectionRegistry.Tick`, `InventoryModel.MainThreadTick`) and adds its sources to the hub.
  `RynthNavPanel` adds `UiSources.RynthNav`, the Atlas source and `RynthNavArrowWatch`.
- The extension ticks the hub once per frame when either is started; RynthAi's behaviour is
  unchanged (the existing dashboard tests run against the hub).
- `NavAtlasInstall.Ensure` and the background `RynthNavAtlas.EnsureLoadedForNames` become a
  shared one-time step run by whichever of the two starts first.
- Threads: all of it runs on the game thread, as does RynthNav's tick and the export calls the
  panel makes (`RynthNavGetStatusJsonUtf8`, `RynthNavGoto`, `RynthNavCommand`, `RynthNavMove`,
  ...). Their code guards for a separate pump thread (`_gate`); one thread is the safe case.

### 4. Host slots

In `AcDream.Rynth.Host`, following RynthCore's semantics:

- **`GetLiveObjectIdsFn`** (v69): `int GetLiveObjectIds(uint* buffer, int capacity)` → total
  count of objects in the client's live object table, filling at most `capacity` ids (a null
  buffer or capacity 0 just counts). 0 out of world. RynthNav uses it to find the route's
  portal, things to avoid and a caster to wield for a recall; RynthLua's `GameWorld` uses it
  too.
- **`DoMovementFn`**: `int DoMovement(uint motion, float speed, int holdKey)` → a hold in
  `RynthMotionHolds` for the motions it holds (RynthNav only uses `WalkBackward` with
  `HoldKeyRun`, and re-sends it every 15 ticks while walking backward, which must stay a no-op
  for an already-held key). A motion it doesn't hold → 0. Speed and hold key are accepted and
  ignored; the run/walk choice follows the held run state as for `SetMotion`.
- **`StopMovementFn`**: `int StopMovement(uint motion, int holdKey)` → lets go of that hold
  (`RynthMotionHolds.SetMotion(motion, false)`); also empty in our host today, and RynthNav
  calls it whenever goto stops or stops walking backward.

Everything else RynthNav calls is already filled (UseFor, CastSpell, ReadKnownSpells,
WieldItem, IsPortaling, InvokeChatParser, ObjectOwnershipInfo, LastWeenieError, ...).

### 5. When things are missing

- RynthNav not loaded: no faces, no bar button, coordinate links hidden; `/rnav` falls through
  to "Unknown command".
- A RynthNav without travel exports: their `RynthNavData` reports `Travel = false` and the Go
  tab shows basic status.
- No navdata: RynthNav's status says so; `RynthNavDataHint` already logs the install step.
- Logout: RynthNav stops goto itself (`OnLogout`); faces stay registered, hidden at the
  character list.

## Testing

`tests/AcDream.Rynth.Host.Tests` unless noted:

- `GetLiveObjectIds`: total returned when the buffer is short, fills up to capacity, 0 out of
  world, null buffer counts.
- `DoMovement(WalkBackward, 1, HoldKeyRun)` holds, `StopMovement` releases; an unheld motion
  returns 0.
- Interceptor: `/rnav where` is eaten by RynthNav (Suppress); `/rnavx` and plain say pass;
  `@lifestone` passes and RynthNav recorded it; a line sent through `InvokeChatParser`
  (Rynth chat's path) is offered too; `/ra help` still reaches RynthAi with the verb registrations gone; a client
  command (`/help`) never reaches the interceptor (router test in the Runtime tests if not
  already covered).
- Panel: with RynthNav loaded, `RynthNav` and the arrow face are registered and the bar lists
  the button; without it, neither.
- Hub: RynthNav sources poll with RynthAi not started; existing dashboard tests pass unchanged;
  posted work runs once per frame with both started.
- Real plugin smoke (skipped without installed navdata, like A's real-tile tests): `/rnav
  where` against the installed tiles, and the status JSON parses through their `RynthNavData`
  (`HasPose`, `Travel`).

## Live check (local ACE, Holtburg test hub; ask before launching)

1. Bar button opens the panel; Go / Atlas / Arrow tabs draw; drag + relog keep the layout.
2. `/rnav help`, `/rnav where`.
3. Walking goto by coordinates and by an Atlas name; stop midway.
4. `/rnav arrow Holtburg` opens the arrow with the panel shut; it points right while turning.
5. Chat coordinate link: right-click → Go to / Arrow to (closes RynthChat's pending check).
6. `/rnav portals on`, a route through a Holtburg hub portal.
7. A recall leg (lifestone recall), wielding a caster if needed; a typed `@lifestone`
   (or ACE's equivalent) is learned.
8. Town Network route through the hub.
9. Walking backward if it comes up (rare; not required).
10. A RynthLua script slash command now reaches RynthLua.

Sub-project A's live check (outdoor snag detour, unroutable goal, RynthLua bridge) can run in
the same session.

## Out of scope

The World Map and minimap (blocked on RynthNav maps), the round Radar panel, the launcher
installing navdata (A2), mirroring the archive, RynthNav's Avalonia `RynthNavPanel`.
