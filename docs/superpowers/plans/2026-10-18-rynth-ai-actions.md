# RynthAi State and Action Slots (RynthAi sub-project #2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The RynthCore API table that every Rynth plugin gets fills the state slots RynthAi reads (pose, heading, portaling, busy, combat mode, vitals, enchantments, skills, attributes, spells, bool/double/string properties, attackable, open container, server time, UseDone and WeenieError counters) and the basic action slots (select, use, identify, query health, change combat mode, move/split/merge items, reset busy, wield). The client serves them from OpenAC's `IAutomationSurface`, falling back to `GameRuntime` where the surface lacks the data.

**Architecture:** Approach A, agreed in conversation:
- `RynthApiTable` stays a thin layer: it marshals arguments and catches everything. Its 600-line file is split into partial files.
- `IRynthGameView` grows members in RynthCore's own terms: degrees, 1/2/4/8 combat modes, server-time expiry.
- `RuntimeRynthGameView` translates from `IAutomationSurface` and `GameRuntime`. The pure translations live in `RynthTranslate`.
- RynthAi's ActionPacer needs a global UseDone/WeenieError counter, and OpenAC has none. A small Core `ServerReplyLog` on `RuntimeInventoryState` is fed by `GameEventWiring`. The game-event dispatcher keeps one handler per event, so the counter can't be a tap in the Rynth host.
- The surface reaches the extension through a new `ClientExtensionContext.Automation`.

**Tech Stack:** .NET 10 (`~/.dotnet/dotnet`), xunit v3 (VSTest runner: `--filter "FullyQualifiedName~X"`), RynthCore/RynthSuite submodules (read only).

**Spec:** No separate spec file. The user asked to go straight to the plan; the design was agreed in conversation (2026-10-18) and is summarised under "User decisions" and "Slot map" below. RynthAi sub-project split: #1 compile + dashboard (done), **#2 this plan**, #3 combat and buffs live, #4 navigation + Nav3D, #5 loot/salvage/vendor/trade, #6 dat raycasting and dungeon pathfinder.

## Global Constraints

- Repo `~/code/OpenAC/OpenAC-Rynth`. Create branch `feat/rynth-ai-actions` from `main` before Task 1. Do not push.
- Use `~/.dotnet/dotnet` (the system `dotnet` lacks SDK 10.0.300).
- **Never edit, copy or move anything inside `external/RynthCore` or `external/RynthSuite`.** `git status --porcelain external` must stay empty.
- `AcDream.Core`, `AcDream.Core.Net`, `AcDream.Runtime`, `AcDream.App`, `AcDream.ClientExtensions` and `AcDream.Rynth.Host` build with warnings as errors and analyzers on: new code there must build clean.
- **Table rule:** every `[UnmanagedCallersOnly]` table function catches every exception, logs through `Fail`, and returns RynthCore's "not available" value. An exception crossing that boundary ends the process.
- **Plugin API is untouched:** no change to `AcDream.Plugin.Abstractions`. `IAutomationSurface` is only consumed.
- `RynthApiTable.ApiVersion` stays **86**. It already passes the SDK's v50–v74 `Has*` version gates.
- **Slots deliberately left unset** (zero, so RynthAi keeps its own fallbacks; each belongs to a later sub-project or is unused):
  - `GetCastBusyStateFn`, `GetTargetVitalsFn`, `ReadObjectEnchantmentsFn`: #3.
  - `StopMovementFn`, `GetObjectStateFn`, `GetObjectMotionOnFn`, `GetViewportSizeFn`, `WorldToScreenFn`, `GetRadarRectFn`: #4.
  - `MoveItemExternalFn`, `GetObjectPalettesFn`, `GetNumContainedItemsFn`, `GetNumContainedContainersFn`: #5.
  - Unused by RynthAi: `IsPlayerReadyFn`, `SetSelectedObjectIdFn`, `GetPreviousSelectedItemIdFn`, `GetEngineStatusJsonFn`, `GetVTankStateFn`, `GetLastIdTimeFn`.
- All table functions run on the game update thread: `RynthClientExtension.Tick` is driven right after the plugin tick in `GameWindow.OnUpdate`. That is the thread `IAutomationSurface` requires.
- Rynth host tests run without parallelism (assembly attribute in `FakeGameView.cs`). App tests that touch Rynth use `[Collection("Rynth")]`.
- Known unrelated failures exist in `AcDream.App.Tests` (dat files, live server). Run App tests with the filters each task's Verify shows. `AcDream.Rynth.Host.Tests` must be fully green.
- Never launch a client without asking the user first. Only the local ACE server 10.10.20.20, never Dreamweave. Never print passwords (`~/OpenAC-dev/dev-client.env`).

**User decisions (already made):**
- #2 scope: "Plumbing + state + basic acts". Pass `IAutomationSurface` to the extension, fill the read-only state slots RynthAi uses, and fill the generic actions. Combat, nav, loot and vendor actions stay unset for #3–#5.
- Approach A: grow `IRynthGameView`; `RuntimeRynthGameView` translates over the surface plus `GameRuntime`; the table stays marshalling only.
- Section 1 approved: the UseDone/WeenieError counter lives as runtime state (`ServerReplyLog` on `RuntimeInventoryState`), not in the plugin API.
- Run their code unchanged: "we cant make changes to their code, and we need to be able to pull in their latest daily changes".
- User asked to go straight from the agreed design to this plan (no spec file).

**Corrections to the in-chat design, made while planning:**
- `StopMovementFn` moves to #4. RynthAi calls it only from NavigationEngine and MoveAudit, paired with `DoMovement`/`SetMotion`, which #4 fills; alone it would only stop the player's own key movement.
- `MoveItemExternalFn` moves to #5. Its callers are corpse looting (`LootPickup.cs`), and OpenAC's `Items.MoveToContainer` covers owned items only.
- `ObjectIsAttackable` reads `SelectedObjectHealthPolicy.ObjectIsAttackable` (retail `ClientCombatSystem::ObjectIsAttackable`). It does not use `RuntimeHostileTargetQuery.IsAttackableSelection`, which adds visibility and liveness checks RynthCore doesn't make.

## Slot map (what each filled slot reads)

| Slot | View member | Source |
|---|---|---|
| GetPlayerPoseFn | `TryGetPlayerPose` | `runtime.EntityObjects.Entities.TryGetActive(player).PhysicsBody.CellPosition` (cell, origin, orientation) |
| GetPlayerHeadingFn | `TryGetPlayerHeading` | `Navigation.Snapshot.Position.HeadingDegrees` (when `IsAvailable`) |
| GetObjectHeadingFn | `TryGetObjectHeading` | `Objects.TryGet(id)` → `HasPosition` → `Position.HeadingDegrees` |
| IsPortalingFn | `IsPortaling` | `Navigation.Snapshot.IsPortalSpace` |
| GetBusyStateFn | `BusyCount` | `Recovery.CaptureBusyState().BusyCount` |
| GetCurrentCombatModeFn | `CombatMode` | `Combat.Snapshot.Mode` → 1/2/4/8 |
| GetPlayerVitalsFn | `TryGetPlayerVitals` | `Character.Current*/Max*` |
| GetPlayerBaseVitalsFn | `TryGetPlayerBaseVitals` | `Character.BaseHealth/BaseStamina/BaseMana` |
| GetVitaeFn | `VitaeMultiplier` | `Character.VitaeMultiplier` |
| ReadPlayerEnchantmentsFn | `PlayerEnchantments` | `Character.ActiveEnchantments`, expiry = server now + seconds remaining, permanent = `double.MaxValue` |
| GetServerTimeFn | `ServerTime` | `runtime.EnvironmentOwner.WorldTime.NowTicks`; 0 before login |
| GetObjectBool/Double/StringPropertyFn | `TryGetObject` → `Bools/Floats/Strings` | `ClientObject.Properties`, merged with the player description for the player |
| GetObjectSkillFn / GetObjectSkillBuffedFn | `TryGetSkill` | `Character.TryGetSkill` (player only) |
| GetObjectAttributeFn | `TryGetAttribute` | `Character.Attributes` by `StatId` (player only) |
| GetObjectAttribute2ndBaseLevelFn | `TryGetVitalBase` | `Character.TryGetVital(kind).Base`, stat 1/3/5 (player only) |
| IsSpellKnownFn | `IsSpellKnown` | `Spells.IsKnown` (player only; -1 otherwise) |
| ReadKnownSpellsFn | `KnownSpells` | `runtime.CharacterOwner.Spellbook.LearnedSpells` |
| ObjectIsAttackableFn | `IsAttackable` | `SelectedObjectHealthPolicy.ObjectIsAttackable` |
| GetGroundContainerIdFn | `OpenContainerId` | `Objects.OpenContainerObjectId` |
| GetUseDoneSeqFn / GetLastUseDoneFn / GetLastWeenieErrorFn | `ServerReplies` | `runtime.InventoryOwner.ServerReplies.Snapshot` |
| SelectItemFn | `SelectObject` | `runtime.ActionOwner.Selection.Select(id, SelectionChangeSource.Plugin)`; true when `id` is then selected |
| UseObjectFn | `UseObject` | `Items.Use(id).Accepted` |
| RequestIdFn | `RequestIdentify` | `Objects.Identify(id).Accepted` |
| QueryHealthFn | `QueryHealth` | `runtime.Session.CurrentSession?.SendQueryHealth(id)` |
| ChangeCombatModeFn | `ChangeCombatMode` | `Combat.EnterMode(mapped).Accepted` |
| MoveItemInternalFn / SplitStackInternalFn | `MoveItem` | `Items.MoveToContainer(id, container, amount, slot).Accepted`; amount ≤ 0 refused |
| MergeStackInternalFn | `MergeStack` | `Items.Merge(source, target).Accepted` |
| ForceResetBusyCountFn | `ResetBusyCount` | `Recovery.ClearOneBusyReference()` until the count is 0 (at most 64 times) |
| WieldItemFn | `WieldItem` | `Equipment.Equip(id, mask).Accepted` |

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AcDream.Core/Items/ServerReplyLog.cs` | create | counts UseDone and server refusals; `ServerReplySnapshot` |
| `src/AcDream.Core.Net/GameEventWiring.cs` | modify | optional `serverReplies`; UseDone/WeenieError/WeenieErrorWithString/InventoryServerSaveFailed record into it |
| `src/AcDream.Runtime/Gameplay/RuntimeInventoryState.cs` | modify | owns `ServerReplies` |
| `src/AcDream.Runtime/Session/LiveSessionEventRouter.cs` | modify | `LiveInventorySessionBindings.ServerReplies`, passed to `WireAll` |
| `src/AcDream.App/Net/LiveSessionRuntimeFactory.cs` | modify | binds `_domain.Inventory.ServerReplies` |
| `src/AcDream.ClientExtensions/ClientExtensionContext.cs` | modify | `IAutomationSurface? Automation` |
| `src/AcDream.App/Extensions/ClientExtensionHost.cs` | modify | `Attach(..., automation)` |
| `src/AcDream.App/Rendering/GameWindow.cs` | modify | passes `_automation` |
| `src/AcDream.Rynth.Host/RynthClientExtension.cs` | modify | hands the surface to the view |
| `src/AcDream.Rynth.Host/RynthApiTable.cs` | split | setup, lifetime, buffers, log/chat/UI functions |
| `src/AcDream.Rynth.Host/RynthApiTable.Reads.cs` | create (moved code) | the existing object reads |
| `src/AcDream.Rynth.Host/RynthApiTable.State.cs` | create | the state slots |
| `src/AcDream.Rynth.Host/RynthApiTable.Actions.cs` | create | the action slots |
| `src/AcDream.Rynth.Host/RynthStateTypes.cs` | create | `RynthVitals`, `RynthEnchantment` |
| `src/AcDream.Rynth.Host/RynthTranslate.cs` | create | combat mode, expiry, training translations |
| `src/AcDream.Rynth.Host/IRynthGameView.cs` | modify | state and action members |
| `src/AcDream.Rynth.Host/RynthObjectInfo.cs` | modify | `Bools`, `Floats`, `Strings` |
| `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs` | modify | implements the new members |
| `tests/AcDream.Core.Tests/Items/ServerReplyLogTests.cs` | create | counter semantics |
| `tests/AcDream.Core.Net.Tests/GameEventWiringTests.cs` | modify | the four events record |
| `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs` | modify | new members |
| `tests/AcDream.Rynth.Host.Tests/RynthApiTableTests.cs` | modify | slot coverage, state and action round trips, throw paths |
| `tests/AcDream.Rynth.Host.Tests/RynthTranslateTests.cs` | create | translations |
| `tests/AcDream.App.Tests/Extensions/FakeRynthAutomation.cs` | create | recording `IAutomationSurface` for view tests |
| `tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs` | modify | state and action translations over a real runtime |

---

### Task 1: ServerReplyLog counts UseDone and server refusals

**Goal:** Every inbound UseDone (0x1C7), WeenieError (0x28A), WeenieErrorWithString (0x28B) and InventoryServerSaveFailed (0xA0) is counted on `RuntimeInventoryState.ServerReplies`. Silent and unmapped codes count too, as in RynthCore's `SmartBoxHooks`. No other behaviour changes.

**Files:**
- Create: `src/AcDream.Core/Items/ServerReplyLog.cs`
- Modify: `src/AcDream.Core.Net/GameEventWiring.cs` (parameter list ends at `onLocalPlayerDeath`, line 72; handlers at ~429, ~445, ~764, ~777)
- Modify: `src/AcDream.Runtime/Gameplay/RuntimeInventoryState.cs` (property list ~60)
- Modify: `src/AcDream.Runtime/Session/LiveSessionEventRouter.cs` (`LiveInventorySessionBindings` ~49; `WireAll` call ~239)
- Modify: `src/AcDream.App/Net/LiveSessionRuntimeFactory.cs` (`CreateInventoryBindings` ~338)
- Test: `tests/AcDream.Core.Tests/Items/ServerReplyLogTests.cs`, `tests/AcDream.Core.Net.Tests/GameEventWiringTests.cs`

**Acceptance Criteria:**
- [ ] `ServerReplyLog.Snapshot` starts all-zero; each `RecordUseDone` adds 1 to `UseDoneSequence` and stores the error; each `RecordWeenieError` adds 1 to `WeenieErrorSequence` and stores error, event type and object id.
- [ ] With `serverReplies` passed to `WireAll`, dispatching each of the four events records once, including a silent code (UseDone 0) and an unmapped WeenieError code (0x9C). The 0xA0 event carries its item guid as the object id.
- [ ] `WireAll` without `serverReplies` behaves exactly as before (existing `GameEventWiringTests` green).
- [ ] The App's live session binds `RuntimeInventoryState.ServerReplies`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ServerReplyLogTests" && ~/.dotnet/dotnet test tests/AcDream.Core.Net.Tests --filter "FullyQualifiedName~GameEventWiringTests"` → all pass

**Steps:**

- [ ] **Step 1: Branch**

```bash
git switch -c feat/rynth-ai-actions main
```

- [ ] **Step 2: Write the failing log test** (`tests/AcDream.Core.Tests/Items/ServerReplyLogTests.cs`)

```csharp
using AcDream.Core.Items;

namespace AcDream.Core.Tests.Items;

public sealed class ServerReplyLogTests
{
    [Fact]
    public void StartsEmptyAndCountsEachReply()
    {
        var log = new ServerReplyLog();
        Assert.Equal(default, log.Snapshot);

        log.RecordUseDone(0);
        log.RecordUseDone(0x0036);
        log.RecordWeenieError(0x028A, 0x04A, 0);
        log.RecordWeenieError(0x00A0, 0x0029, 0x80000010);

        ServerReplySnapshot snapshot = log.Snapshot;
        Assert.Equal(2, snapshot.UseDoneSequence);
        Assert.Equal(0x0036u, snapshot.LastUseDoneError);
        Assert.Equal(2, snapshot.WeenieErrorSequence);
        Assert.Equal(0x0029u, snapshot.LastWeenieError);
        Assert.Equal(0x00A0u, snapshot.LastWeenieErrorEventType);
        Assert.Equal(0x80000010u, snapshot.LastWeenieErrorObjectId);
    }
}
```

Run: `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ServerReplyLogTests"`
Expected: build FAIL, `ServerReplyLog` not found.

- [ ] **Step 3: Write the log** (`src/AcDream.Core/Items/ServerReplyLog.cs`)

```csharp
namespace AcDream.Core.Items;

/// <summary>
/// What <see cref="ServerReplyLog"/> has seen: how many UseDone events and
/// server refusals arrived, and the latest of each. All zero before the first.
/// </summary>
public readonly record struct ServerReplySnapshot(
    int UseDoneSequence,
    uint LastUseDoneError,
    int WeenieErrorSequence,
    uint LastWeenieError,
    uint LastWeenieErrorEventType,
    uint LastWeenieErrorObjectId);

/// <summary>
/// Counts the server's UseDone events and its refusals (WeenieError,
/// WeenieErrorWithString, InventoryServerSaveFailed), silent codes included,
/// so a caller can tell "the server answered" from "nothing yet" by comparing
/// sequence numbers. Retail-hosted RynthCore keeps the same counters.
/// </summary>
public sealed class ServerReplyLog
{
    private readonly object _gate = new();
    private ServerReplySnapshot _snapshot;

    public ServerReplySnapshot Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    public void RecordUseDone(uint weenieError)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                UseDoneSequence = _snapshot.UseDoneSequence + 1,
                LastUseDoneError = weenieError,
            };
        }
    }

    /// <param name="eventType">The GameEvent that carried it (0x28A, 0x28B or 0xA0).</param>
    /// <param name="weenieError">The refusal code.</param>
    /// <param name="objectId">The item for 0xA0, else 0.</param>
    public void RecordWeenieError(uint eventType, uint weenieError, uint objectId)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                WeenieErrorSequence = _snapshot.WeenieErrorSequence + 1,
                LastWeenieError = weenieError,
                LastWeenieErrorEventType = eventType,
                LastWeenieErrorObjectId = objectId,
            };
        }
    }
}
```

Run the test again. Expected: PASS.

- [ ] **Step 4: Write the failing wiring tests** (append to `GameEventWiringTests`, using its `WrapEnvelope` and `MakeString16L` helpers)

```csharp
    private static (GameEventDispatcher, ServerReplyLog) MakeWithReplies()
    {
        var dispatcher = new GameEventDispatcher();
        var replies = new ServerReplyLog();
        GameEventWiring.WireAll(dispatcher, new ClientObjectTable(), new CombatState(),
            new Spellbook(), new ChatLog(), serverReplies: replies);
        return (dispatcher, replies);
    }

    private static void DispatchU32s(GameEventDispatcher dispatcher, GameEventType type, params uint[] values)
    {
        byte[] payload = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(i * 4), values[i]);
        dispatcher.Dispatch(GameEventEnvelope.TryParse(WrapEnvelope(type, payload))!.Value);
    }

    [Fact]
    public void WireAll_ServerReplies_CountsUseDoneIncludingSuccess()
    {
        var (d, replies) = MakeWithReplies();

        DispatchU32s(d, GameEventType.UseDone, 0u);
        DispatchU32s(d, GameEventType.UseDone, 0x0036u);

        Assert.Equal(2, replies.Snapshot.UseDoneSequence);
        Assert.Equal(0x0036u, replies.Snapshot.LastUseDoneError);
    }

    [Fact]
    public void WireAll_ServerReplies_CountsEveryRefusalKindWithItsEventType()
    {
        var (d, replies) = MakeWithReplies();

        DispatchU32s(d, GameEventType.WeenieError, 0x9Cu);   // unmapped: still counted
        Assert.Equal((1, 0x9Cu, 0x028Au, 0u), Tuple(replies.Snapshot));

        byte[] interp = MakeString16L("drudge");
        byte[] withString = new byte[4 + interp.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(withString, 0x04Au);
        Array.Copy(interp, 0, withString, 4, interp.Length);
        d.Dispatch(GameEventEnvelope.TryParse(
            WrapEnvelope(GameEventType.WeenieErrorWithString, withString))!.Value);
        Assert.Equal((2, 0x04Au, 0x028Bu, 0u), Tuple(replies.Snapshot));

        DispatchU32s(d, GameEventType.InventoryServerSaveFailed, 0x80000010u, 0x0029u);
        Assert.Equal((3, 0x0029u, 0x00A0u, 0x80000010u), Tuple(replies.Snapshot));

        static (int, uint, uint, uint) Tuple(ServerReplySnapshot s) =>
            (s.WeenieErrorSequence, s.LastWeenieError, s.LastWeenieErrorEventType, s.LastWeenieErrorObjectId);
    }
```

Add `using AcDream.Core.Items;` if the file lacks it. Run: `~/.dotnet/dotnet test tests/AcDream.Core.Net.Tests --filter "FullyQualifiedName~GameEventWiringTests"`. Expected: build FAIL, no `serverReplies` parameter.

- [ ] **Step 5: Record in `GameEventWiring`**

Add a last parameter after `onLocalPlayerDeath`:

```csharp
        Action<string /*deathMessage*/>? onLocalPlayerDeath = null,
        ServerReplyLog? serverReplies = null)
```

In the four handlers, record right after the parse succeeds and **before** any silent-code or unmapped-code return:

```csharp
        registrar.Register(GameEventType.WeenieError, e =>
        {
            var code = GameEvents.ParseWeenieError(e.Payload.Span);
            if (code is null) return;
            serverReplies?.RecordWeenieError((uint)GameEventType.WeenieError, code.Value, 0);
            if (WeenieErrorMessages.IsSilentClientControlStatus(code.Value)) return;
```

```csharp
        registrar.Register(GameEventType.WeenieErrorWithString, e =>
        {
            var p = GameEvents.ParseWeenieErrorWithString(e.Payload.Span);
            if (p is null) return;
            serverReplies?.RecordWeenieError((uint)GameEventType.WeenieErrorWithString, p.Value.ErrorCode, 0);
            if (WeenieErrorMessages.IsSilentClientControlStatus(p.Value.ErrorCode)) return;
```

```csharp
            var p = GameEvents.ParseInventoryServerSaveFailed(e.Payload.Span);
            if (p is null) return;
            serverReplies?.RecordWeenieError(
                (uint)GameEventType.InventoryServerSaveFailed, p.Value.WeenieError, p.Value.ItemGuid);
```

```csharp
            if (err is null) return;
            serverReplies?.RecordUseDone(err.Value);
            Console.WriteLine($"[use-done] err=0x{err.Value:X4}");
```

Add `using AcDream.Core.Items;` (already present). Run the Core.Net tests. Expected: PASS, including the existing ones.

- [ ] **Step 6: Own and bind it in the runtime and App**

`RuntimeInventoryState` (next to `Transactions`):

```csharp
    /// <summary>The server's UseDone events and refusals, counted (see <see cref="ServerReplyLog"/>).</summary>
    public ServerReplyLog ServerReplies { get; } = new();
```

`LiveInventorySessionBindings` gets a last optional member:

```csharp
    Func<string>? PlayerName = null,
    ServerReplyLog? ServerReplies = null);
```

The router's `WireAll` call gets, after `onLocalPlayerDeath: social.OnLocalPlayerDeath,` (keep the named-argument style):

```csharp
                serverReplies: inventory.ServerReplies,
```

`LiveSessionRuntimeFactory.CreateInventoryBindings` gets, after `PlayerName: ...`:

```csharp
            ?? string.Empty,
        ServerReplies: _domain.Inventory.ServerReplies);
```

(The `PlayerName` lambda's closing `);` becomes `,`.) Add `using AcDream.Core.Items;` where needed.

Run: `~/.dotnet/dotnet build AcDream.slnx`. Expected: 0 errors, 0 warnings in the touched projects.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Core/Items/ServerReplyLog.cs src/AcDream.Core.Net/GameEventWiring.cs src/AcDream.Runtime/Gameplay/RuntimeInventoryState.cs src/AcDream.Runtime/Session/LiveSessionEventRouter.cs src/AcDream.App/Net/LiveSessionRuntimeFactory.cs tests/AcDream.Core.Tests/Items/ServerReplyLogTests.cs tests/AcDream.Core.Net.Tests/GameEventWiringTests.cs
git commit -m "feat(runtime): count the server's UseDone events and refusals"
```

---

### Task 2: The client hands extensions its automation surface

**Goal:** `ClientExtensionContext.Automation` carries the App's bound `IAutomationSurface`. `RuntimeRynthGameView` takes it as an optional constructor argument, storing `NoOpAutomationSurface.Instance` when it is null. Nothing reads it yet.

**Files:**
- Modify: `src/AcDream.ClientExtensions/ClientExtensionContext.cs`
- Modify: `src/AcDream.App/Extensions/ClientExtensionHost.cs:20-39`
- Modify: `src/AcDream.App/Rendering/GameWindow.cs:539`
- Modify: `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs:11-19`
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs:70`

**Acceptance Criteria:**
- [ ] `ClientExtensionContext` has `public IAutomationSurface? Automation { get; init; }`, documented as null when the client has no surface.
- [ ] `GameWindow` passes `_automation` to `_extensions.Attach` as `automation:`.
- [ ] `new RuntimeRynthGameView(runtime)` and `new RuntimeRynthGameView(runtime, submit)` still compile (existing tests untouched).
- [ ] Solution builds clean; `RuntimeRynthGameViewTests` and `RynthOverlayExtensionTests` green.

**Verify:** `~/.dotnet/dotnet build AcDream.slnx && ~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RuntimeRynthGameViewTests|FullyQualifiedName~RynthOverlayExtensionTests"` → build 0 errors, tests pass

**Steps:**

- [ ] **Step 1: Context member** (`ClientExtensionContext.cs`, add `using AcDream.Plugin.Abstractions;`)

```csharp
    /// <summary>
    /// The client's plugin automation surface, bound to <see cref="Runtime"/>.
    /// Null when the client has none (tests); it answers unavailable before login.
    /// </summary>
    public IAutomationSurface? Automation { get; init; }
```

- [ ] **Step 2: Host and window**

`ClientExtensionHost.Attach` gets a last parameter `IAutomationSurface? automation = null` and sets `Automation = automation,` in the context initializer. In `GameWindow.cs:539`, append `, automation: _automation` to the `_extensions.Attach(...)` call.

- [ ] **Step 3: The view keeps the surface**

```csharp
    private readonly GameRuntime _runtime;
    private readonly IAutomationSurface _automation;

    private readonly Func<string, bool>? _submitChatCommand;

    public RuntimeRynthGameView(
        GameRuntime runtime,
        Func<string, bool>? submitChatCommand = null,
        IAutomationSurface? automation = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _submitChatCommand = submitChatCommand;
        _automation = automation ?? NoOpAutomationSurface.Instance;
    }
```

Add `using AcDream.Plugin.Abstractions;`. If the analyzer flags `_automation` as assigned but never read (IDE0052/CA1823), don't suppress it with a `#pragma`. Add this one-line member instead; Task 4 removes it once real reads exist:

```csharp
    /// <summary>Whether the automation surface has a live in-world session.</summary>
    public bool AutomationAvailable => _automation.IsAvailable;
```

In `RynthClientExtension.cs:70`:

```csharp
        var view = new RuntimeRynthGameView(context.Runtime, context.SubmitChatCommand, context.Automation);
```

- [ ] **Step 4: Build and test** (Verify command). Expected: green.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.ClientExtensions/ClientExtensionContext.cs src/AcDream.App/Extensions/ClientExtensionHost.cs src/AcDream.App/Rendering/GameWindow.cs src/AcDream.Rynth.Host/RuntimeRynthGameView.cs src/AcDream.Rynth.Host/RynthClientExtension.cs
git commit -m "feat(rynth): the client hands extensions its automation surface"
```

---

### Task 3: Split RynthApiTable into partial files

**Goal:** `RynthApiTable` becomes a `partial` class over two files, with no behaviour change. This is a pure move, so Tasks 4 and 5 can add `RynthApiTable.State.cs` and `RynthApiTable.Actions.cs`.

**Files:**
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.cs`
- Create: `src/AcDream.Rynth.Host/RynthApiTable.Reads.cs`

**Acceptance Criteria:**
- [ ] `RynthApiTable.cs` keeps: class doc, `ApiVersion`, fields, constructor, `Native`, `Dispose`, `Fail`, `Log`, `WriteToChat`, `Replace`, `InvokeChatParser`, `UiSubmit`, `UiPollEvents`, `UiGetInfo`.
- [ ] `RynthApiTable.Reads.cs` holds, unchanged: `GetPlayerId`, `GetObjectName`, `GetObjectQuadProperty`, `GetSelectedItemId`, `TryObject`, `GetObjectIntProperty`, `GetObjectDataIdProperty`, `GetObjectInstanceIdProperty`, `GetContainerContents`, `GetObjectOwnershipInfo`, `GetObjectWielderInfo`, `GetItemType`, `GetObjectWcid`, `GetObjectBitfield`, `HasAppraisalData`, `GetObjectSpellIds`, `GetObjectPosition`, `GetCurCoords`, `GetAccountName`, `GetWorldName`.
- [ ] `git diff --stat` shows only moved lines plus the `partial` keyword and the new file's header; `AcDream.Rynth.Host.Tests` fully green.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass

**Steps:**

- [ ] **Step 1:** Change the declaration to `public sealed unsafe partial class RynthApiTable : IDisposable`.

- [ ] **Step 2:** Create `RynthApiTable.Reads.cs` with this header, then cut and paste the listed methods (with their comments) from `RynthApiTable.cs`, keeping their order:

```csharp
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// The table's object and player reads (see RynthApiTable for the rules every function follows).
public sealed unsafe partial class RynthApiTable
{
    // ...moved methods...
}
```

Remove `using System.Numerics;` from `RynthApiTable.cs` if nothing there uses it any more (the analyzer will say).

- [ ] **Step 3:** Run the Verify command. Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add src/AcDream.Rynth.Host/RynthApiTable.cs src/AcDream.Rynth.Host/RynthApiTable.Reads.cs
git commit -m "refactor(rynth): split the API table's reads into their own file"
```

---

### Task 4: State slots

**Goal:** The 25 state slots in the slot map are set and read the game view. `RuntimeRynthGameView` serves them from the surface and the runtime, with RynthCore's units.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthStateTypes.cs`, `src/AcDream.Rynth.Host/RynthTranslate.cs`, `src/AcDream.Rynth.Host/RynthApiTable.State.cs`
- Modify: `src/AcDream.Rynth.Host/IRynthGameView.cs`, `src/AcDream.Rynth.Host/RynthObjectInfo.cs`, `src/AcDream.Rynth.Host/RynthApiTable.cs` (constructor, string buffer), `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`, `tests/AcDream.Rynth.Host.Tests/RynthApiTableTests.cs`, `tests/AcDream.Rynth.Host.Tests/RynthTranslateTests.cs`, `tests/AcDream.App.Tests/Extensions/FakeRynthAutomation.cs`, `tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs`

**Acceptance Criteria:**
- [ ] Through `RynthCoreHost`, every state slot in the slot map reports `Has*` true and returns the fake view's values: pose with quaternion, both headings, portaling, busy, combat mode, vitals, base vitals, vitae, enchantments (count may exceed max), server time, bool/double/string properties, skill buffed and base and training, attribute, vital base, spell known (-1/0/1), known spells, attackable, ground container, UseDone seq, last UseDone, last WeenieError.
- [ ] When the view throws, each state slot returns RynthCore's "not available" value: 0 / false, `GetCurrentCombatMode` 1, `GetVitae` 1.0, `IsSpellKnown` -1, `ReadPlayerEnchantments`/`ReadKnownSpells` -1. The log names the function.
- [ ] `RynthTranslate`: Peace/Unknown→1, Melee→2, Missile→4, Magic→8, and back (1/2/4/8 only); expiry = now + remaining, infinite or NaN → `double.MaxValue`.
- [ ] `RuntimeRynthGameView` over a test runtime and `FakeRynthAutomation`:
  - pose comes from the player's physics body;
  - combat mode comes from `Combat.Snapshot.Mode`;
  - enchantment expiry is server-now plus the remaining seconds;
  - skills answer for the player only;
  - the player's bools, floats and strings come through `TryGetObject`;
  - server replies come from `InventoryOwner.ServerReplies`;
  - before login, `ServerTime` is 0 and `KnownSpells` is null.
- [ ] Deferred slots stay zero in the native table: `GetCastBusyStateFn`, `GetTargetVitalsFn`, `ReadObjectEnchantmentsFn`, `StopMovementFn`, `GetObjectStateFn`, `GetObjectMotionOnFn`, `WorldToScreenFn`, `MoveItemExternalFn`, `GetObjectPalettesFn`, `GetNumContainedItemsFn`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests && ~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RuntimeRynthGameViewTests"` → all pass

**Steps:**

- [ ] **Step 1: Types and translations** (`RynthStateTypes.cs`, `RynthTranslate.cs`)

```csharp
namespace AcDream.Rynth.Host;

/// <summary>The player's vitals as RynthCore's GetPlayerVitals reports them.</summary>
public readonly record struct RynthVitals(
    uint Health, uint MaxHealth, uint Stamina, uint MaxStamina, uint Mana, uint MaxMana);

/// <summary>One enchantment on the player; expiry in server time, <see cref="double.MaxValue"/> = permanent.</summary>
public readonly record struct RynthEnchantment(uint SpellId, double ExpiryServerTime);
```

```csharp
using AcDream.Plugin.Abstractions;

namespace AcDream.Rynth.Host;

/// <summary>OpenAC's plugin values in RynthCore's units.</summary>
public static class RynthTranslate
{
    /// <summary>RynthCore's combat modes (retail CombatMode flags).</summary>
    public const int NonCombat = 1, Melee = 2, Missile = 4, Magic = 8;

    public static int ToRynthCombatMode(PluginCombatMode mode) => mode switch
    {
        PluginCombatMode.Melee => Melee,
        PluginCombatMode.Missile => Missile,
        PluginCombatMode.Magic => Magic,
        _ => NonCombat,
    };

    public static PluginCombatMode? FromRynthCombatMode(int mode) => mode switch
    {
        NonCombat => PluginCombatMode.Peace,
        Melee => PluginCombatMode.Melee,
        Missile => PluginCombatMode.Missile,
        Magic => PluginCombatMode.Magic,
        _ => null,
    };

    // RynthCore stores start + duration in server time; OpenAC reports seconds left.
    public static double ToServerExpiry(double serverNow, double secondsRemaining) =>
        double.IsFinite(secondsRemaining) ? serverNow + secondsRemaining : double.MaxValue;
}
```

`RynthTranslateTests.cs`:

```csharp
using AcDream.Plugin.Abstractions;
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthTranslateTests
{
    [Theory]
    [InlineData(PluginCombatMode.Unknown, 1)]
    [InlineData(PluginCombatMode.Peace, 1)]
    [InlineData(PluginCombatMode.Melee, 2)]
    [InlineData(PluginCombatMode.Missile, 4)]
    [InlineData(PluginCombatMode.Magic, 8)]
    public void CombatModesBecomeRetailFlags(PluginCombatMode mode, int rynth) =>
        Assert.Equal(rynth, RynthTranslate.ToRynthCombatMode(mode));

    [Fact]
    public void RetailFlagsBecomeCombatModes()
    {
        Assert.Equal(PluginCombatMode.Peace, RynthTranslate.FromRynthCombatMode(1));
        Assert.Equal(PluginCombatMode.Melee, RynthTranslate.FromRynthCombatMode(2));
        Assert.Equal(PluginCombatMode.Missile, RynthTranslate.FromRynthCombatMode(4));
        Assert.Equal(PluginCombatMode.Magic, RynthTranslate.FromRynthCombatMode(8));
        Assert.Null(RynthTranslate.FromRynthCombatMode(3));
        Assert.Null(RynthTranslate.FromRynthCombatMode(0));
    }

    [Fact]
    public void ExpiryIsServerNowPlusRemainingAndPermanentIsMax()
    {
        Assert.Equal(1_030.5, RynthTranslate.ToServerExpiry(1_000.0, 30.5));
        Assert.Equal(double.MaxValue, RynthTranslate.ToServerExpiry(1_000.0, double.PositiveInfinity));
        Assert.Equal(double.MaxValue, RynthTranslate.ToServerExpiry(1_000.0, double.NaN));
    }
}
```

- [ ] **Step 2: View members** (append to `IRynthGameView`, add `using AcDream.Core.Items;`)

```csharp
    /// <summary>The player's cell, origin in that cell, and orientation.</summary>
    bool TryGetPlayerPose(out uint cellId, out Vector3 origin, out Quaternion orientation);

    /// <summary>The player's heading: degrees clockwise from north.</summary>
    bool TryGetPlayerHeading(out float headingDegrees);

    /// <summary>A world object's heading: degrees clockwise from north.</summary>
    bool TryGetObjectHeading(uint objectId, out float headingDegrees);

    /// <summary>True while the player is in portal space.</summary>
    bool IsPortaling { get; }

    /// <summary>The client's busy count: 0 idle, more while actions are outstanding.</summary>
    int BusyCount { get; }

    /// <summary>RynthCore's combat mode: 1 NonCombat, 2 Melee, 4 Missile, 8 Magic.</summary>
    int CombatMode { get; }

    bool TryGetPlayerVitals(out RynthVitals vitals);

    /// <summary>The unbuffed maximum health, stamina and mana.</summary>
    bool TryGetPlayerBaseVitals(out uint health, out uint stamina, out uint mana);

    /// <summary>1 with no vitae penalty; 0.95 for five percent.</summary>
    float VitaeMultiplier { get; }

    /// <summary>Server time in seconds; 0 before login.</summary>
    double ServerTime { get; }

    /// <summary>The player's enchantments in force; null when not known (before login).</summary>
    IReadOnlyList<RynthEnchantment>? PlayerEnchantments { get; }

    /// <summary>A skill of the player: buffed and base level, training (0 unusable, 1 untrained, 2 trained, 3 specialized).</summary>
    bool TryGetSkill(uint objectId, uint skillId, out int buffed, out int baseLevel, out int training);

    /// <summary>A primary attribute of the player (1 Strength … 6 Self).</summary>
    bool TryGetAttribute(uint objectId, uint attributeId, out uint buffed, out uint baseLevel);

    /// <summary>The player's unbuffed maximum of a vital, by stat 1 health, 3 stamina, 5 mana.</summary>
    bool TryGetVitalBase(uint objectId, uint vitalStatId, out uint baseMaximum);

    /// <summary>1 known, 0 not known, -1 cannot tell (not the player, before login).</summary>
    int IsSpellKnown(uint objectId, uint spellId);

    /// <summary>Every spell the player knows, ascending; null before login.</summary>
    IReadOnlyList<uint>? KnownSpells { get; }

    /// <summary>Retail's ObjectIsAttackable for the player against this object.</summary>
    bool IsAttackable(uint objectId);

    /// <summary>The open external container (corpse, chest); 0 when none.</summary>
    uint OpenContainerId { get; }

    /// <summary>The server's UseDone and refusal counters.</summary>
    ServerReplySnapshot ServerReplies { get; }
```

`RynthObjectInfo` gets three optional tables (not `required`, so existing constructions still compile):

```csharp
    public IReadOnlyDictionary<uint, bool> Bools { get; init; } = new Dictionary<uint, bool>();
    public IReadOnlyDictionary<uint, double> Floats { get; init; } = new Dictionary<uint, double>();
    public IReadOnlyDictionary<uint, string> Strings { get; init; } = new Dictionary<uint, string>();
```

- [ ] **Step 3: Fake view** (append to `FakeGameView`; the `Read` helper honours `Throw`)

```csharp
    private T Read<T>(T value) => Throw ? throw new InvalidOperationException("view failed") : value;

    public (uint Cell, System.Numerics.Vector3 Origin, System.Numerics.Quaternion Orientation)? Pose { get; set; }
    public float? PlayerHeading { get; set; }
    public Dictionary<uint, float> Headings { get; } = new();
    public bool Portaling { get; set; }
    public int Busy { get; set; }
    public int Mode { get; set; } = 1;
    public RynthVitals? Vitals { get; set; }
    public (uint Health, uint Stamina, uint Mana)? BaseVitals { get; set; }
    public float Vitae { get; set; } = 1f;
    public double Time { get; set; }
    public List<RynthEnchantment>? Enchantments { get; set; }
    public Dictionary<(uint Object, uint Skill), (int Buffed, int Base, int Training)> Skills { get; } = new();
    public Dictionary<(uint Object, uint Attribute), (uint Buffed, uint Base)> Attributes { get; } = new();
    public Dictionary<(uint Object, uint Vital), uint> VitalBases { get; } = new();
    public List<uint>? Known { get; set; }
    public HashSet<uint> Attackable { get; } = new();
    public uint OpenContainer { get; set; }
    public AcDream.Core.Items.ServerReplySnapshot Replies { get; set; }

    public bool TryGetPlayerPose(out uint cellId, out System.Numerics.Vector3 origin, out System.Numerics.Quaternion orientation)
    {
        var pose = Read(Pose);
        (cellId, origin, orientation) = pose ?? default;
        return pose is not null;
    }

    public bool TryGetPlayerHeading(out float headingDegrees)
    {
        float? heading = Read(PlayerHeading);
        headingDegrees = heading ?? 0;
        return heading is not null;
    }

    public bool TryGetObjectHeading(uint objectId, out float headingDegrees) =>
        Read(Headings).TryGetValue(objectId, out headingDegrees);

    public bool IsPortaling => Read(Portaling);
    public int BusyCount => Read(Busy);
    public int CombatMode => Read(Mode);

    public bool TryGetPlayerVitals(out RynthVitals vitals)
    {
        RynthVitals? found = Read(Vitals);
        vitals = found ?? default;
        return found is not null;
    }

    public bool TryGetPlayerBaseVitals(out uint health, out uint stamina, out uint mana)
    {
        var found = Read(BaseVitals);
        (health, stamina, mana) = found ?? default;
        return found is not null;
    }

    public float VitaeMultiplier => Read(Vitae);
    public double ServerTime => Read(Time);
    public IReadOnlyList<RynthEnchantment>? PlayerEnchantments => Read(Enchantments);

    public bool TryGetSkill(uint objectId, uint skillId, out int buffed, out int baseLevel, out int training)
    {
        bool found = Read(Skills).TryGetValue((objectId, skillId), out var skill);
        (buffed, baseLevel, training) = skill;
        return found;
    }

    public bool TryGetAttribute(uint objectId, uint attributeId, out uint buffed, out uint baseLevel)
    {
        bool found = Read(Attributes).TryGetValue((objectId, attributeId), out var attribute);
        (buffed, baseLevel) = attribute;
        return found;
    }

    public bool TryGetVitalBase(uint objectId, uint vitalStatId, out uint baseMaximum) =>
        Read(VitalBases).TryGetValue((objectId, vitalStatId), out baseMaximum);

    public int IsSpellKnown(uint objectId, uint spellId) =>
        Read(Known) is { } known && objectId == _playerId ? (known.Contains(spellId) ? 1 : 0) : -1;

    public IReadOnlyList<uint>? KnownSpells => Read(Known);
    public bool IsAttackable(uint objectId) => Read(Attackable).Contains(objectId);
    public uint OpenContainerId => Read(OpenContainer);
    public AcDream.Core.Items.ServerReplySnapshot ServerReplies => Read(Replies);
```

(The settable values have different names from the interface members, e.g. `Portaling` against `IsPortaling`, so the class has no clashes.)

- [ ] **Step 4: Write the failing table tests** (append to `RynthApiTableTests`; add `using System.Numerics;`)

```csharp
    [Fact]
    public void StateSlots_ReadTheGameView()
    {
        const uint player = 0x50000001;
        var view = new FakeGameView
        {
            PlayerId = player,
            Pose = (0xA9B40021, new Vector3(10, 20, 30), new Quaternion(0, 0, 0.5f, 0.8660254f)),
            PlayerHeading = 90f,
            Portaling = true,
            Busy = 2,
            Mode = 8,
            Vitals = new RynthVitals(50, 100, 60, 110, 70, 120),
            BaseVitals = (90, 100, 110),
            Vitae = 0.95f,
            Time = 1_234.5,
            Enchantments = [new RynthEnchantment(2, 1_300.0), new RynthEnchantment(3, double.MaxValue)],
            Known = [1, 2, 3],
            OpenContainer = 0x80000044,
            Replies = new AcDream.Core.Items.ServerReplySnapshot(7, 0x36, 4, 0x29, 0xA0, 0x80000010),
        };
        view.Headings[0x80000010] = 180f;
        view.Skills[(player, 6)] = (250, 200, 3);
        view.Attributes[(player, 1)] = (150, 100);
        view.VitalBases[(player, 1)] = 180;
        view.Attackable.Add(0x80000010);
        view.Objects[0x80000010] = ObjectWith(bools: new() { [1] = true }, floats: new() { [5] = 2.5 },
            strings: new() { [1] = "Drudge" });
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.TryGetPlayerPose(out uint cell, out float x, out float y, out float z,
            out float qw, out float qx, out float qy, out float qz));
        Assert.Equal((0xA9B40021u, 10f, 20f, 30f), (cell, x, y, z));
        Assert.Equal((0.8660254f, 0f, 0f, 0.5f), (qw, qx, qy, qz));
        Assert.True(host.TryGetPlayerHeading(out float heading));
        Assert.Equal(90f, heading);
        Assert.True(host.TryGetObjectHeading(0x80000010, out float objectHeading));
        Assert.Equal(180f, objectHeading);
        Assert.True(host.IsPortaling());
        Assert.Equal(2, host.GetBusyState());
        Assert.Equal(8, host.GetCurrentCombatMode());
        Assert.True(host.TryGetPlayerVitals(out uint hp, out uint maxHp, out uint st, out uint maxSt, out uint mp, out uint maxMp));
        Assert.Equal((50u, 100u, 60u, 110u, 70u, 120u), (hp, maxHp, st, maxSt, mp, maxMp));
        Assert.True(host.TryGetPlayerBaseVitals(out uint baseHp, out uint baseSt, out uint baseMp));
        Assert.Equal((90u, 100u, 110u), (baseHp, baseSt, baseMp));
        Assert.Equal(0.95f, host.GetVitae(player));
        Assert.Equal(1_234.5, host.GetServerTime());

        var spellIds = new uint[1];
        var expiries = new double[1];
        Assert.Equal(2, host.ReadPlayerEnchantments(spellIds, expiries, 1));   // count exceeds max
        Assert.Equal((2u, 1_300.0), (spellIds[0], expiries[0]));

        Assert.True(host.TryGetObjectBoolProperty(0x80000010, 1, out bool flag));
        Assert.True(flag);
        Assert.True(host.TryGetObjectDoubleProperty(0x80000010, 5, out double number));
        Assert.Equal(2.5, number);
        Assert.True(host.TryGetObjectStringProperty(0x80000010, 1, out string text));
        Assert.Equal("Drudge", text);
        Assert.False(host.TryGetObjectStringProperty(0x80000010, 2, out _));

        Assert.True(host.TryGetObjectSkill(player, 6, out int buffed, out int training));
        Assert.Equal((250, 3), (buffed, training));
        Assert.True(host.TryGetObjectSkillLevel(player, 6, 1, out int baseLevel));
        Assert.Equal(200, baseLevel);
        Assert.True(host.TryGetObjectSkillLevel(player, 6, 0, out int buffedLevel));
        Assert.Equal(250, buffedLevel);
        Assert.True(host.TryGetObjectAttribute(player, 1, 1, out uint strengthBase));
        Assert.Equal(100u, strengthBase);
        Assert.True(host.TryGetObjectAttribute2ndBaseLevel(player, 1, out uint healthBase));
        Assert.Equal(180u, healthBase);

        Assert.True(host.IsSpellKnown(player, 2, out bool known));
        Assert.True(known);
        Assert.True(host.IsSpellKnown(player, 9, out bool unknown));
        Assert.False(unknown);
        var knownIds = new uint[8];
        Assert.Equal(3, host.ReadKnownSpells(knownIds, 8));
        Assert.Equal(new uint[] { 1, 2, 3 }, knownIds[..3]);

        Assert.True(host.ObjectIsAttackable(0x80000010));
        Assert.False(host.ObjectIsAttackable(0x80000011));
        Assert.Equal(0x80000044u, host.GetGroundContainerId());
        Assert.Equal(7, host.GetUseDoneSeq());
        Assert.True(host.TryGetLastUseDone(out int useSeq, out uint useError));
        Assert.Equal((7, 0x36u), (useSeq, useError));
        Assert.True(host.TryGetLastWeenieError(out int errSeq, out uint err, out uint evt, out uint obj));
        Assert.Equal((4, 0x29u, 0xA0u, 0x80000010u), (errSeq, err, evt, obj));
    }

    [Fact]
    public void StateSlots_ReturnNotAvailableWhenTheViewThrows()
    {
        var view = new FakeGameView { Throw = true };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.False(host.TryGetPlayerPose(out _, out _, out _, out _, out _, out _, out _, out _));
        Assert.False(host.IsPortaling());
        Assert.Equal(0, host.GetBusyState());
        Assert.Equal(1, host.GetCurrentCombatMode());
        Assert.False(host.TryGetPlayerVitals(out _, out _, out _, out _, out _, out _));
        Assert.Equal(1f, host.GetVitae(1));
        Assert.Equal(0, host.GetServerTime());
        Assert.Equal(-1, host.ReadPlayerEnchantments(new uint[4], new double[4], 4));
        Assert.False(host.TryGetObjectBoolProperty(1, 1, out _));
        Assert.False(host.IsSpellKnown(1, 1, out _));
        Assert.Equal(-1, host.ReadKnownSpells(new uint[4], 4));
        Assert.False(host.ObjectIsAttackable(1));
        Assert.Equal(0, host.GetUseDoneSeq());
        Assert.False(host.TryGetLastWeenieError(out _, out _, out _, out _));
        Assert.Contains(_log, line => line.Contains("GetCurrentCombatMode threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("ReadPlayerEnchantments threw", StringComparison.Ordinal));
    }

    [Fact]
    public void DeferredSlotsStayUnset()
    {
        using var table = new RynthApiTable(new FakeGameView(), _log.Add);
        Assert.Equal(IntPtr.Zero, table.Native->GetCastBusyStateFn);
        Assert.Equal(IntPtr.Zero, table.Native->GetTargetVitalsFn);
        Assert.Equal(IntPtr.Zero, table.Native->ReadObjectEnchantmentsFn);
        Assert.Equal(IntPtr.Zero, table.Native->StopMovementFn);
        Assert.Equal(IntPtr.Zero, table.Native->GetObjectStateFn);
        Assert.Equal(IntPtr.Zero, table.Native->GetObjectMotionOnFn);
        Assert.Equal(IntPtr.Zero, table.Native->WorldToScreenFn);
        Assert.Equal(IntPtr.Zero, table.Native->MoveItemExternalFn);
        Assert.Equal(IntPtr.Zero, table.Native->GetObjectPalettesFn);
        Assert.Equal(IntPtr.Zero, table.Native->GetNumContainedItemsFn);
    }

    private static RynthObjectInfo ObjectWith(
        Dictionary<uint, bool> bools, Dictionary<uint, double> floats, Dictionary<uint, string> strings) => new()
    {
        ContainerId = 0, WielderId = 0, EquipLocation = 0, ItemType = 0, Wcid = 0, Bitfield = 0,
        IconId = 0, IconUnderlayId = 0, IconOverlayId = 0, SpellId = null, HouseOwnerId = null,
        StackSize = 0, StackSizeMax = 0, Value = 0, Burden = 0, ItemsCapacity = 0, Structure = 0,
        MaxStructure = 0, Workmanship = 0, MaterialType = null, Identified = false,
        AppraisedSpellIds = [], Ints = new Dictionary<uint, int>(), DataIds = new Dictionary<uint, uint>(),
        InstanceIds = new Dictionary<uint, uint>(),
        Bools = bools, Floats = floats, Strings = strings,
    };
```

Note: `GetVitae` with the view throwing returns 1.0 (no penalty), as the wrapper's own fallback does; `GetUseDoneSeq` returns 0. Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthApiTableTests"`. Expected: build FAIL (view members missing on `RuntimeRynthGameView`, slots unset).

- [ ] **Step 5: The table's state functions** (`RynthApiTable.State.cs`)

In `RynthApiTable.cs`: add the field `private IntPtr _stringBuffer;`. Free it in `Dispose` like `_nameBuffer`. Call `BindStateSlots();` at the end of the constructor, before the `if (scriptWindows)` block.

```csharp
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AcDream.Core.Items;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host;

// The table's state slots (RynthAi sub-project #2). Same rules as every table function:
// catch everything, log, return RynthCore's "not available" value.
public sealed unsafe partial class RynthApiTable
{
    private void BindStateSlots()
    {
        Native->GetPlayerPoseFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint*, float*, float*, float*, float*, float*, float*, float*, int>)&GetPlayerPose;
        Native->GetPlayerHeadingFn = (IntPtr)(delegate* unmanaged[Cdecl]<float*, int>)&GetPlayerHeading;
        Native->GetObjectHeadingFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, float*, int>)&GetObjectHeading;
        Native->IsPortalingFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&IsPortaling;
        Native->GetBusyStateFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&GetBusyState;
        Native->GetCurrentCombatModeFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&GetCurrentCombatMode;
        Native->GetPlayerVitalsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint*, uint*, uint*, uint*, uint*, uint*, int>)&GetPlayerVitals;
        Native->GetPlayerBaseVitalsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint*, uint*, uint*, int>)&GetPlayerBaseVitals;
        Native->GetVitaeFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, float>)&GetVitae;
        Native->ReadPlayerEnchantmentsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint*, double*, int, int>)&ReadPlayerEnchantments;
        Native->GetServerTimeFn = (IntPtr)(delegate* unmanaged[Cdecl]<double>)&GetServerTime;
        Native->GetObjectBoolPropertyFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int*, int>)&GetObjectBoolProperty;
        Native->GetObjectDoublePropertyFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, double*, int>)&GetObjectDoubleProperty;
        Native->GetObjectStringPropertyFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, IntPtr>)&GetObjectStringProperty;
        Native->GetObjectSkillFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int*, int*, int>)&GetObjectSkill;
        Native->GetObjectSkillBuffedFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int, int*, int>)&GetObjectSkillBuffed;
        Native->GetObjectAttributeFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int, uint*, int>)&GetObjectAttribute;
        Native->GetObjectAttribute2ndBaseLevelFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, uint*, int>)&GetObjectAttribute2ndBaseLevel;
        Native->IsSpellKnownFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int>)&IsSpellKnown;
        Native->ReadKnownSpellsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint*, int, int>)&ReadKnownSpells;
        Native->ObjectIsAttackableFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&ObjectIsAttackable;
        Native->GetGroundContainerIdFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint>)&GetGroundContainerId;
        Native->GetUseDoneSeqFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&GetUseDoneSeq;
        Native->GetLastUseDoneFn = (IntPtr)(delegate* unmanaged[Cdecl]<int*, uint*, int>)&GetLastUseDone;
        Native->GetLastWeenieErrorFn = (IntPtr)(delegate* unmanaged[Cdecl]<int*, uint*, uint*, uint*, int>)&GetLastWeenieError;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetPlayerPose(uint* cellId, float* x, float* y, float* z, float* qw, float* qx, float* qy, float* qz)
    {
        RynthApiTable? table = _current;
        if (table is null || cellId is null || x is null || y is null || z is null
            || qw is null || qx is null || qy is null || qz is null)
            return 0;
        try
        {
            if (!table._view.TryGetPlayerPose(out uint cell, out Vector3 origin, out Quaternion orientation))
                return 0;
            *cellId = cell;
            *x = origin.X;
            *y = origin.Y;
            *z = origin.Z;
            *qw = orientation.W;
            *qx = orientation.X;
            *qy = orientation.Y;
            *qz = orientation.Z;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetPlayerPose), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetPlayerHeading(float* headingDegrees)
    {
        RynthApiTable? table = _current;
        if (table is null || headingDegrees is null)
            return 0;
        try
        {
            if (!table._view.TryGetPlayerHeading(out float heading))
                return 0;
            *headingDegrees = heading;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetPlayerHeading), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectHeading(uint objectId, float* headingDegrees)
    {
        RynthApiTable? table = _current;
        if (table is null || headingDegrees is null)
            return 0;
        try
        {
            if (!table._view.TryGetObjectHeading(objectId, out float heading))
                return 0;
            *headingDegrees = heading;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectHeading), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int IsPortaling()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.IsPortaling ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(IsPortaling), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetBusyState()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return Math.Max(table._view.BusyCount, 0); }
        catch (Exception error)
        {
            table.Fail(nameof(GetBusyState), error);
            return 0;
        }
    }

    // RynthCore: 1 NonCombat (also its "not available"), 2 Melee, 4 Missile, 8 Magic.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetCurrentCombatMode()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return RynthTranslate.NonCombat;
        try { return table._view.CombatMode; }
        catch (Exception error)
        {
            table.Fail(nameof(GetCurrentCombatMode), error);
            return RynthTranslate.NonCombat;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetPlayerVitals(uint* health, uint* maxHealth, uint* stamina, uint* maxStamina, uint* mana, uint* maxMana)
    {
        RynthApiTable? table = _current;
        if (table is null || health is null || maxHealth is null || stamina is null
            || maxStamina is null || mana is null || maxMana is null)
            return 0;
        try
        {
            if (!table._view.TryGetPlayerVitals(out RynthVitals vitals))
                return 0;
            *health = vitals.Health;
            *maxHealth = vitals.MaxHealth;
            *stamina = vitals.Stamina;
            *maxStamina = vitals.MaxStamina;
            *mana = vitals.Mana;
            *maxMana = vitals.MaxMana;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetPlayerVitals), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetPlayerBaseVitals(uint* health, uint* stamina, uint* mana)
    {
        RynthApiTable? table = _current;
        if (table is null || health is null || stamina is null || mana is null)
            return 0;
        try
        {
            if (!table._view.TryGetPlayerBaseVitals(out uint h, out uint s, out uint m))
                return 0;
            *health = h;
            *stamina = s;
            *mana = m;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetPlayerBaseVitals), error);
            return 0;
        }
    }

    // RynthCore: a multiplier, 1.0 = no vitae (also its "not available").
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static float GetVitae(uint playerId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 1f;
        try { return playerId != 0 && playerId == table._view.PlayerId ? table._view.VitaeMultiplier : 1f; }
        catch (Exception error)
        {
            table.Fail(nameof(GetVitae), error);
            return 1f;
        }
    }

    // RynthCore: copies at most max, returns how many there are; -1 when not available.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ReadPlayerEnchantments(uint* spellIds, double* expiryTimes, int max)
    {
        RynthApiTable? table = _current;
        if (table is null || spellIds is null || expiryTimes is null || max <= 0)
            return -1;
        try
        {
            if (table._view.PlayerEnchantments is not { } enchantments)
                return -1;
            int count = Math.Min(enchantments.Count, max);
            for (int i = 0; i < count; i++)
            {
                spellIds[i] = enchantments[i].SpellId;
                expiryTimes[i] = enchantments[i].ExpiryServerTime;
            }

            return enchantments.Count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(ReadPlayerEnchantments), error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static double GetServerTime()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.ServerTime; }
        catch (Exception error)
        {
            table.Fail(nameof(GetServerTime), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectBoolProperty(uint objectId, uint stat, int* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info) || !info.Bools.TryGetValue(stat, out bool found))
                return 0;
            *value = found ? 1 : 0;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectBoolProperty), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectDoubleProperty(uint objectId, uint stat, double* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info) || !info.Floats.TryGetValue(stat, out double found))
                return 0;
            *value = found;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectDoubleProperty), error);
            return 0;
        }
    }

    // RynthCore: an ANSI pointer valid until the next call; 0 = not set.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr GetObjectStringProperty(uint objectId, uint stat)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return IntPtr.Zero;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info) || !info.Strings.TryGetValue(stat, out string? found))
                return IntPtr.Zero;
            return Replace(ref table._stringBuffer, found);
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectStringProperty), error);
            return IntPtr.Zero;
        }
    }

    // RynthCore's GetObjectSkill reports the buffed level (InqSkill raw=0) and the training.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectSkill(uint objectId, uint skill, int* level, int* training)
    {
        RynthApiTable? table = _current;
        if (table is null || level is null || training is null)
            return 0;
        try
        {
            if (!table._view.TryGetSkill(objectId, skill, out int buffed, out _, out int trained))
                return 0;
            *level = buffed;
            *training = trained;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectSkill), error);
            return 0;
        }
    }

    // raw 0 = buffed, 1 = base.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectSkillBuffed(uint objectId, uint skill, int raw, int* level)
    {
        RynthApiTable? table = _current;
        if (table is null || level is null)
            return 0;
        try
        {
            if (!table._view.TryGetSkill(objectId, skill, out int buffed, out int baseLevel, out _))
                return 0;
            *level = raw != 0 ? baseLevel : buffed;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectSkillBuffed), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectAttribute(uint objectId, uint attribute, int raw, uint* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table._view.TryGetAttribute(objectId, attribute, out uint buffed, out uint baseLevel))
                return 0;
            *value = raw != 0 ? baseLevel : buffed;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectAttribute), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectAttribute2ndBaseLevel(uint objectId, uint vital, uint* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table._view.TryGetVitalBase(objectId, vital, out uint baseMaximum))
                return 0;
            *value = baseMaximum;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectAttribute2ndBaseLevel), error);
            return 0;
        }
    }

    // RynthCore: 1 known, 0 not known, -1 not available.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int IsSpellKnown(uint objectId, uint spellId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return -1;
        try { return table._view.IsSpellKnown(objectId, spellId); }
        catch (Exception error)
        {
            table.Fail(nameof(IsSpellKnown), error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ReadKnownSpells(uint* spellIds, int max)
    {
        RynthApiTable? table = _current;
        if (table is null || spellIds is null || max <= 0)
            return -1;
        try
        {
            if (table._view.KnownSpells is not { } known)
                return -1;
            int count = Math.Min(known.Count, max);
            for (int i = 0; i < count; i++)
                spellIds[i] = known[i];
            return known.Count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(ReadKnownSpells), error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ObjectIsAttackable(uint objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.IsAttackable(objectId) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(ObjectIsAttackable), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint GetGroundContainerId()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.OpenContainerId; }
        catch (Exception error)
        {
            table.Fail(nameof(GetGroundContainerId), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetUseDoneSeq()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.ServerReplies.UseDoneSequence; }
        catch (Exception error)
        {
            table.Fail(nameof(GetUseDoneSeq), error);
            return 0;
        }
    }

    // RynthCore answers 1 whenever it watches game events (seq 0 = none yet); so does this host.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetLastUseDone(int* seq, uint* error)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            ServerReplySnapshot replies = table._view.ServerReplies;
            if (seq != null) *seq = replies.UseDoneSequence;
            if (error != null) *error = replies.LastUseDoneError;
            return 1;
        }
        catch (Exception error2)
        {
            table.Fail(nameof(GetLastUseDone), error2);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetLastWeenieError(int* seq, uint* error, uint* eventType, uint* objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            ServerReplySnapshot replies = table._view.ServerReplies;
            if (seq != null) *seq = replies.WeenieErrorSequence;
            if (error != null) *error = replies.LastWeenieError;
            if (eventType != null) *eventType = replies.LastWeenieErrorEventType;
            if (objectId != null) *objectId = replies.LastWeenieErrorObjectId;
            return 1;
        }
        catch (Exception failure)
        {
            table.Fail(nameof(GetLastWeenieError), failure);
            return 0;
        }
    }
}
```

(In `GetLastUseDone` and `GetLastWeenieError` the exception variable is not named `error`, because a parameter already has that name.)

- [ ] **Step 6: The runtime view serves them** (`RuntimeRynthGameView.cs`; remove the Task 2 `AutomationAvailable` member; add `using AcDream.Core.Combat;`, `using AcDream.Core.Items;`)

```csharp
    public bool TryGetPlayerPose(out uint cellId, out Vector3 origin, out Quaternion orientation)
    {
        uint player = PlayerId;
        if (player != 0
            && _runtime.EntityObjects.Entities.TryGetActive(player, out RuntimeEntityRecord record)
            && record.PhysicsBody is { } body
            && body.CellPosition.ObjCellId != 0)
        {
            cellId = body.CellPosition.ObjCellId;
            origin = body.CellPosition.Frame.Origin;
            orientation = body.CellPosition.Frame.Orientation;
            return true;
        }

        cellId = 0;
        origin = default;
        orientation = Quaternion.Identity;
        return false;
    }

    public bool TryGetPlayerHeading(out float headingDegrees)
    {
        PluginNavigationSnapshot snapshot = _automation.Navigation.Snapshot;
        headingDegrees = snapshot.IsAvailable ? snapshot.Position.HeadingDegrees : 0;
        return snapshot.IsAvailable;
    }

    public bool TryGetObjectHeading(uint objectId, out float headingDegrees)
    {
        if (_automation.Objects.TryGet(objectId, out PluginWorldObject found) && found.HasPosition)
        {
            headingDegrees = found.Position.HeadingDegrees;
            return true;
        }

        headingDegrees = 0;
        return false;
    }

    public bool IsPortaling => _automation.Navigation.Snapshot.IsPortalSpace;

    public int BusyCount => _automation.Recovery.CaptureBusyState().BusyCount;

    public int CombatMode => _automation.IsAvailable
        ? RynthTranslate.ToRynthCombatMode(_automation.Combat.Snapshot.Mode)
        : RynthTranslate.NonCombat;

    public bool TryGetPlayerVitals(out RynthVitals vitals)
    {
        ICharacterInfo character = _automation.Character;
        vitals = new RynthVitals(
            character.CurrentHealth, character.MaxHealth,
            character.CurrentStamina, character.MaxStamina,
            character.CurrentMana, character.MaxMana);
        return _automation.IsAvailable && character.MaxHealth > 0;
    }

    public bool TryGetPlayerBaseVitals(out uint health, out uint stamina, out uint mana)
    {
        ICharacterInfo character = _automation.Character;
        health = character.BaseHealth;
        stamina = character.BaseStamina;
        mana = character.BaseMana;
        return _automation.IsAvailable && health > 0;
    }

    public float VitaeMultiplier => _automation.IsAvailable ? _automation.Character.VitaeMultiplier : 1f;

    // The server clock as the client last synced it; 0 before login, as RynthCore reports it.
    public double ServerTime => PlayerId == 0 ? 0 : _runtime.EnvironmentOwner.WorldTime.NowTicks;

    public IReadOnlyList<RynthEnchantment>? PlayerEnchantments
    {
        get
        {
            if (!_automation.IsAvailable)
                return null;
            double now = ServerTime;
            return
            [
                .. _automation.Character.ActiveEnchantments.Select(enchantment => new RynthEnchantment(
                    enchantment.SpellId, RynthTranslate.ToServerExpiry(now, enchantment.SecondsRemaining))),
            ];
        }
    }

    public bool TryGetSkill(uint objectId, uint skillId, out int buffed, out int baseLevel, out int training)
    {
        if (IsPlayer(objectId) && _automation.Character.TryGetSkill(skillId, out PluginSkillInfo skill))
        {
            buffed = (int)skill.Current;
            baseLevel = (int)skill.Base;
            training = (int)skill.Training;
            return true;
        }

        buffed = baseLevel = training = 0;
        return false;
    }

    public bool TryGetAttribute(uint objectId, uint attributeId, out uint buffed, out uint baseLevel)
    {
        if (IsPlayer(objectId))
        {
            foreach (PluginAttributeInfo attribute in _automation.Character.Attributes)
            {
                if (attribute.StatId != attributeId)
                    continue;
                buffed = attribute.Current;
                baseLevel = attribute.Base;
                return true;
            }
        }

        buffed = baseLevel = 0;
        return false;
    }

    // RynthCore's secondary-attribute ids: 1 max health, 3 max stamina, 5 max mana.
    public bool TryGetVitalBase(uint objectId, uint vitalStatId, out uint baseMaximum)
    {
        if (IsPlayer(objectId) && (vitalStatId is 1 or 3 or 5)
            && _automation.Character.TryGetVital((int)(vitalStatId - 1) / 2, out PluginVitalInfo vital))
        {
            baseMaximum = vital.Base;
            return true;
        }

        baseMaximum = 0;
        return false;
    }

    public int IsSpellKnown(uint objectId, uint spellId) =>
        !IsPlayer(objectId) || !_automation.IsAvailable ? -1 : _automation.Spells.IsKnown(spellId) ? 1 : 0;

    public IReadOnlyList<uint>? KnownSpells =>
        PlayerId == 0 ? null : [.. _runtime.CharacterOwner.Spellbook.LearnedSpells.Order()];

    public bool IsAttackable(uint objectId)
    {
        uint player = PlayerId;
        if (player == 0)
            return false;
        ClientObjectTable objects = _runtime.InventoryOwner.Objects;
        return SelectedObjectHealthPolicy.ObjectIsAttackable(player, objects.Get(player), objectId, objects.Get(objectId));
    }

    public uint OpenContainerId => _automation.Objects.OpenContainerObjectId;

    public ServerReplySnapshot ServerReplies => _runtime.InventoryOwner.ServerReplies.Snapshot;

    private bool IsPlayer(uint objectId) => objectId != 0 && objectId == PlayerId;
```

In `TryGetObject`, add to the initializer:

```csharp
            Bools = properties.Bools,
            Floats = properties.Floats,
            Strings = properties.Strings,
```

and extend `Merge` with the same three tables:

```csharp
        foreach (KeyValuePair<uint, bool> pair in description.Bools)
            merged.Bools[pair.Key] = pair.Value;
        foreach (KeyValuePair<uint, double> pair in description.Floats)
            merged.Floats[pair.Key] = pair.Value;
        foreach (KeyValuePair<uint, string> pair in description.Strings)
            merged.Strings[pair.Key] = pair.Value;
```

Run the Host tests. Expected: PASS.

- [ ] **Step 7: Recording fake surface for view tests** (`tests/AcDream.App.Tests/Extensions/FakeRynthAutomation.cs`)

The probe build (2026-10-18) showed the members each interface leaves abstract; the fakes implement exactly those, plus the default members this plan reads or calls.

```csharp
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Extensions;

/// <summary>An automation surface whose areas tests set and whose commands it records.</summary>
internal sealed class FakeRynthAutomation : IAutomationSurface
{
    public bool Available { get; set; } = true;
    public FakeCharacter CharacterArea { get; } = new();
    public FakeSpells SpellArea { get; } = new();
    public FakeCombat CombatArea { get; } = new();
    public FakeItems ItemArea { get; } = new();
    public FakeEquipment EquipmentArea { get; } = new();
    public FakeObjects ObjectArea { get; } = new();
    public FakeRecovery RecoveryArea { get; } = new();
    public FakeNavigation NavigationArea { get; } = new();

    public bool IsAvailable => Available;
    public ICharacterInfo Character => CharacterArea;
    public ISpellCatalog Spells => SpellArea;
    public IMagicCommands Magic => NoOpAutomationSurface.Instance;
    public IPluginChat Chat => NoOpAutomationSurface.Instance;
    public ICombatAutomation Combat => CombatArea;
    public IItemAutomation Items => ItemArea;
    public IEquipmentAutomation Equipment => EquipmentArea;
    public IWorldObjectAutomation Objects => ObjectArea;
    public IRecoveryAutomation Recovery => RecoveryArea;
    public INavigationAutomation Navigation => NavigationArea;
}

internal sealed class FakeCharacter : ICharacterInfo
{
    public bool IsInWorld { get; set; } = true;
    public uint ObjectId { get; set; }
    public uint CurrentHealth { get; set; }
    public uint MaxHealth { get; set; }
    public uint CurrentStamina { get; set; }
    public uint MaxStamina { get; set; }
    public uint CurrentMana { get; set; }
    public uint MaxMana { get; set; }
    public uint BaseHealth { get; set; }
    public uint BaseStamina { get; set; }
    public uint BaseMana { get; set; }
    public float VitaeMultiplier { get; set; } = 1f;
    public List<PluginSkillInfo> SkillList { get; } = new();
    public List<PluginAttributeInfo> AttributeList { get; } = new();
    public List<PluginVitalInfo> VitalList { get; } = new();
    public List<PluginActiveEnchantment> EnchantmentList { get; } = new();
    public IReadOnlyList<PluginSkillInfo> Skills => SkillList;
    public IReadOnlyList<PluginAttributeInfo> Attributes => AttributeList;
    public IReadOnlyList<PluginActiveEnchantment> ActiveEnchantments => EnchantmentList;

    public bool TryGetSkill(uint skillId, out PluginSkillInfo skill)
    {
        skill = SkillList.FirstOrDefault(s => s.SkillId == skillId);
        return skill.SkillId == skillId && skillId != 0;
    }

    public bool TryGetVital(int kind, out PluginVitalInfo vital)
    {
        vital = VitalList.FirstOrDefault(v => v.Kind == kind);
        return VitalList.Any(v => v.Kind == kind);
    }
}

internal sealed class FakeSpells : ISpellCatalog
{
    public HashSet<uint> Known { get; } = new();
    public IReadOnlyList<PluginSpellInfo> KnownSelfBuffs => Array.Empty<PluginSpellInfo>();
    public bool TryGet(uint spellId, out PluginSpellInfo info)
    {
        info = default;
        return false;
    }

    public bool IsKnown(uint spellId) => Known.Contains(spellId);
}

internal sealed class FakeCombat : ICombatAutomation
{
    public PluginCombatSnapshot Snapshot { get; set; }
    public List<PluginCombatMode> ModesRequested { get; } = new();
    public PluginCombatCommandResult EnterDefaultMode() => new(PluginCombatCommandStatus.Unavailable);
    public PluginCombatCommandResult BeginPhysicalAttack(uint targetObjectId, PluginAttackHeight height, float power) =>
        new(PluginCombatCommandStatus.Unavailable);
    public PluginCombatCommandResult ReleasePhysicalAttack() => new(PluginCombatCommandStatus.Unavailable);
    public PluginCombatCommandResult AbortPhysicalAttack() => new(PluginCombatCommandStatus.Unavailable);
    public IReadOnlyList<PluginCombatTarget> CaptureHostileTargets(float maximumDistance) =>
        Array.Empty<PluginCombatTarget>();

    public PluginCombatCommandResult EnterMode(PluginCombatMode mode)
    {
        ModesRequested.Add(mode);
        return new(PluginCombatCommandStatus.ModeChangeSent);
    }
}

internal sealed class FakeItems : IItemAutomation
{
    public List<string> Calls { get; } = new();
    public PluginItemCommandStatus Answer { get; set; } = PluginItemCommandStatus.Started;

    public PluginItemCommandResult Use(uint objectId)
    {
        Calls.Add($"use {objectId:X8}");
        return new(Answer);
    }

    public PluginItemCommandResult MoveToContainer(uint objectId, uint containerObjectId, uint amount = 0u, int placement = 0)
    {
        Calls.Add($"move {objectId:X8} {containerObjectId:X8} {amount} {placement}");
        return new(Answer);
    }

    public PluginItemCommandResult Merge(uint sourceObjectId, uint targetObjectId, uint amount = 0u)
    {
        Calls.Add($"merge {sourceObjectId:X8} {targetObjectId:X8} {amount}");
        return new(Answer);
    }
}

internal sealed class FakeEquipment : IEquipmentAutomation
{
    public List<(uint ObjectId, uint Location)> Equipped { get; } = new();

    public PluginEquipmentCommandResult Equip(uint objectId, uint requestedLocation = 0u)
    {
        Equipped.Add((objectId, requestedLocation));
        return new(PluginEquipmentCommandStatus.Started);
    }
}

internal sealed class FakeObjects : IWorldObjectAutomation
{
    public Dictionary<uint, PluginWorldObject> Known { get; } = new();
    public List<uint> Identified { get; } = new();
    public uint OpenContainer { get; set; }
    public uint OpenContainerObjectId => OpenContainer;

    public bool TryGet(uint objectId, out PluginWorldObject value) => Known.TryGetValue(objectId, out value);

    public PluginItemCommandResult Identify(uint objectId)
    {
        Identified.Add(objectId);
        return new(PluginItemCommandStatus.Started);
    }
}

internal sealed class FakeRecovery : IRecoveryAutomation
{
    public int Busy { get; set; }
    public PluginBusyState CaptureBusyState() => new(Busy, false, 0, 0, 0, false);

    public PluginRecoveryResult ClearOneBusyReference()
    {
        if (Busy == 0)
            return new(false);
        int previous = Busy--;
        return new(true, previous, Busy);
    }
}

internal sealed class FakeNavigation : INavigationAutomation
{
    public PluginNavigationSnapshot Snapshot { get; set; }
    public PluginNavigationCommandStatus SetMovementIntent(in PluginMovementIntent intent) =>
        PluginNavigationCommandStatus.Unavailable;
    public PluginNavigationCommandStatus ClearMovementIntent() => PluginNavigationCommandStatus.Unavailable;
    public bool TryGetObject(uint objectId, out PluginNavigationObject value)
    {
        value = default;
        return false;
    }
}
```

If the compiler reports a further abstract member (the abstractions may have grown since the probe), implement it with the same "Unavailable / empty / false" body that `NoOpAutomationSurface` uses for it.

- [ ] **Step 8: View tests** (append to `RuntimeRynthGameViewTests`; add `using System.Numerics;`, `using AcDream.Plugin.Abstractions;`)

```csharp
    [Fact]
    public void StateComesFromTheSurfaceInRynthCoresUnits()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var automation = new FakeRynthAutomation();
        automation.CombatArea.Snapshot = default(PluginCombatSnapshot) with { Mode = PluginCombatMode.Missile };
        automation.CharacterArea.MaxHealth = 100;
        automation.CharacterArea.CurrentHealth = 40;
        automation.CharacterArea.EnchantmentList.Add(new PluginActiveEnchantment(2, 1, 1, 60));
        automation.CharacterArea.EnchantmentList.Add(new PluginActiveEnchantment(3, 1, 1, double.PositiveInfinity));
        automation.CharacterArea.SkillList.Add(new PluginSkillInfo(6, "Melee Defense", PluginSkillTraining.Specialized, 250) { Base = 200 });
        automation.SpellArea.Known.Add(2);
        automation.RecoveryArea.Busy = 3;
        automation.ObjectArea.OpenContainer = 0x80000044;
        runtime.InventoryOwner.ServerReplies.RecordUseDone(0x36);
        var view = new RuntimeRynthGameView(runtime, automation: automation);

        Assert.Equal(4, view.CombatMode);
        Assert.True(view.TryGetPlayerVitals(out RynthVitals vitals));
        Assert.Equal((40u, 100u), (vitals.Health, vitals.MaxHealth));
        Assert.Equal(3, view.BusyCount);
        Assert.Equal(0x80000044u, view.OpenContainerId);
        Assert.Equal(1, view.IsSpellKnown(0x50000001, 2));
        Assert.Equal(0, view.IsSpellKnown(0x50000001, 9));
        Assert.Equal(-1, view.IsSpellKnown(0x80000010, 2));
        Assert.True(view.TryGetSkill(0x50000001, 6, out int buffed, out int baseLevel, out int training));
        Assert.Equal((250, 200, 3), (buffed, baseLevel, training));
        Assert.False(view.TryGetSkill(0x80000010, 6, out _, out _, out _));
        Assert.Equal((1, 0x36u), (view.ServerReplies.UseDoneSequence, view.ServerReplies.LastUseDoneError));

        IReadOnlyList<RynthEnchantment> enchantments = view.PlayerEnchantments!;
        double now = view.ServerTime;
        Assert.InRange(enchantments[0].ExpiryServerTime - now, 59.0, 60.0);
        Assert.Equal(double.MaxValue, enchantments[1].ExpiryServerTime);
    }

    [Fact]
    public void BeforeLoginTheStateReadsAsNotAvailable()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var view = new RuntimeRynthGameView(runtime);   // no surface: the no-op one

        Assert.Equal(0, view.ServerTime);
        Assert.Null(view.KnownSpells);
        Assert.Null(view.PlayerEnchantments);
        Assert.Equal(1, view.CombatMode);
        Assert.False(view.TryGetPlayerVitals(out _));
        Assert.False(view.TryGetPlayerPose(out _, out _, out _));
        Assert.Equal(-1, view.IsSpellKnown(0, 1));
    }

    [Fact]
    public void ObjectBoolsFloatsAndStringsComeThroughTryGetObject()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var item = new ClientObject { ObjectId = 0x80000010, Name = "Wand" };
        item.Properties.Bools[22] = true;
        item.Properties.Floats[5] = 0.25;
        item.Properties.Strings[16] = "A wand";
        runtime.InventoryOwner.Objects.AddOrUpdate(item);
        var view = new RuntimeRynthGameView(runtime);

        Assert.True(view.TryGetObject(0x80000010, out RynthObjectInfo info));
        Assert.True(info.Bools[22]);
        Assert.Equal(0.25, info.Floats[5]);
        Assert.Equal("A wand", info.Strings[16]);
    }
```

The pose read uses the same physics-body path as the existing `TryGetObjectPosition` test. If that test file already builds a player entity with a body, add one assertion there that `TryGetPlayerPose` returns that entity's cell, origin and `Frame.Orientation`. Otherwise the Host table test covers the marshalling, and Task 6's live check covers the runtime path.

Run the Verify command. Expected: all pass.

- [ ] **Step 9: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests tests/AcDream.App.Tests/Extensions
git commit -m "feat(rynth): the API table serves RynthAi's state slots"
```

---

### Task 5: Action slots

**Goal:** The 10 action slots in the slot map are set and call the game view. `RuntimeRynthGameView` sends them through the surface, or through the runtime for select and query health.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthApiTable.Actions.cs`
- Modify: `src/AcDream.Rynth.Host/IRynthGameView.cs`, `src/AcDream.Rynth.Host/RynthApiTable.cs` (constructor), `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`, `tests/AcDream.Rynth.Host.Tests/RynthApiTableTests.cs`, `tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs`

**Acceptance Criteria:**
- [ ] Through `RynthCoreHost`: `SelectItem`, `UseObject`, `RequestId`, `QueryHealth`, `ChangeCombatMode`, `MoveItemInternal`, `SplitStackInternal`, `MergeStackInternal`, `ForceResetBusyCount` and `WieldItem` reach the fake view with their arguments. The bool results mirror the view's answer.
- [ ] `ChangeCombatMode(3)` and `MoveItemInternal(..., amount: 0)` return false without calling the view.
- [ ] A throwing view makes each action return false (`ForceResetBusyCount` returns nothing) and logs `<Name> threw`.
- [ ] `NewTable_HasApiVersionAndOnlyTheImplementedSlots` now asserts `HasUseObject` true and `HasCastSpell` false.
- [ ] `RuntimeRynthGameView` over `FakeRynthAutomation`:
  - use, identify, move, merge, wield and combat mode reach the right surface calls (combat mode 8 → `Magic`);
  - `ResetBusyCount` clears every reference;
  - `SelectObject` selects through `ActionOwner.Selection` and is true when the object is already selected;
  - `QueryHealth` is false with no session.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests && ~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RuntimeRynthGameViewTests|FullyQualifiedName~RynthOverlayExtensionTests"` → all pass

**Steps:**

- [ ] **Step 1: View members** (append to `IRynthGameView`)

```csharp
    /// <summary>Selects an object (0 clears). True when it is selected afterwards.</summary>
    bool SelectObject(uint objectId);

    /// <summary>Uses an object, approaching it first when out of reach. True when sent.</summary>
    bool UseObject(uint objectId);

    /// <summary>Asks the server to identify (appraise) an object. True when sent.</summary>
    bool RequestIdentify(uint objectId);

    /// <summary>Asks the server for a creature's health. True when sent.</summary>
    bool QueryHealth(uint objectId);

    /// <summary>Changes combat mode, in RynthCore's 1/2/4/8. True when sent.</summary>
    bool ChangeCombatMode(int rynthMode);

    /// <summary>Moves (or, with an amount below the stack, splits) an owned item into a container slot.</summary>
    bool MoveItem(uint objectId, uint containerId, int slot, int amount);

    /// <summary>Merges one stack into another.</summary>
    bool MergeStack(uint sourceObjectId, uint targetObjectId);

    /// <summary>Clears every busy reference the client holds.</summary>
    void ResetBusyCount();

    /// <summary>Wields an item into the given equip slot mask.</summary>
    bool WieldItem(uint objectId, uint equipMask);
```

- [ ] **Step 2: Fake view** (append to `FakeGameView`)

```csharp
    public List<string> Actions { get; } = new();
    public bool ActionAnswer { get; set; } = true;

    private bool Act(string call)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        Actions.Add(call);
        return ActionAnswer;
    }

    public bool SelectObject(uint objectId) => Act($"select {objectId:X8}");
    public bool UseObject(uint objectId) => Act($"use {objectId:X8}");
    public bool RequestIdentify(uint objectId) => Act($"id {objectId:X8}");
    public bool QueryHealth(uint objectId) => Act($"health {objectId:X8}");
    public bool ChangeCombatMode(int rynthMode) => Act($"mode {rynthMode}");
    public bool MoveItem(uint objectId, uint containerId, int slot, int amount) =>
        Act($"move {objectId:X8} {containerId:X8} {slot} {amount}");
    public bool MergeStack(uint sourceObjectId, uint targetObjectId) => Act($"merge {sourceObjectId:X8} {targetObjectId:X8}");
    public void ResetBusyCount() => Act("reset busy");
    public bool WieldItem(uint objectId, uint equipMask) => Act($"wield {objectId:X8} {equipMask:X8}");
```

- [ ] **Step 3: Write the failing table tests** (in `RynthApiTableTests`: change `Assert.False(host.HasUseObject);` to `Assert.True(host.HasUseObject);` in the first test, then append)

```csharp
    [Fact]
    public void ActionSlots_CallTheGameView()
    {
        var view = new FakeGameView();
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.SelectItem(0x80000010));
        Assert.True(host.UseObject(0x80000010));
        Assert.True(host.RequestId(0x80000011));
        Assert.True(host.QueryHealth(0x80000012));
        Assert.True(host.ChangeCombatMode(8));
        Assert.True(host.MoveItemInternal(0x80000013, 0x50000001, 2, 5));
        Assert.True(host.SplitStackInternal(0x80000014, 0x80000020, 0, 3));
        Assert.True(host.MergeStackInternal(0x80000015, 0x80000016));
        host.ForceResetBusyCount();
        Assert.True(host.WieldItem(0x80000017, 0x00200000));

        Assert.Equal(
            new[]
            {
                "select 80000010", "use 80000010", "id 80000011", "health 80000012", "mode 8",
                "move 80000013 50000001 2 5", "move 80000014 80000020 0 3", "merge 80000015 80000016",
                "reset busy", "wield 80000017 00200000",
            },
            view.Actions);

        view.ActionAnswer = false;
        Assert.False(host.UseObject(0x80000010));
    }

    [Fact]
    public void ActionSlots_RefuseBadArgumentsWithoutCallingTheView()
    {
        var view = new FakeGameView();
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.False(host.ChangeCombatMode(3));
        Assert.False(host.MoveItemInternal(0x80000013, 0x50000001, 0, 0));
        Assert.False(host.SplitStackInternal(0x80000013, 0x50000001, 0, -1));
        Assert.Empty(view.Actions);
    }

    [Fact]
    public void ActionSlots_ReturnFalseWhenTheViewThrows()
    {
        var view = new FakeGameView { Throw = true };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.False(host.UseObject(1));
        Assert.False(host.SelectItem(1));
        Assert.False(host.WieldItem(1, 1));
        host.ForceResetBusyCount();
        Assert.Contains(_log, line => line.Contains("UseObject threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("ForceResetBusyCount threw", StringComparison.Ordinal));
    }
```

Run the Host tests. Expected: build FAIL (view members missing on `RuntimeRynthGameView`).

- [ ] **Step 4: The table's action functions** (`RynthApiTable.Actions.cs`; call `BindActionSlots();` in the constructor right after `BindStateSlots();`)

```csharp
using System.Runtime.CompilerServices;

namespace AcDream.Rynth.Host;

// The table's action slots (RynthAi sub-project #2): 1 = sent, 0 = refused or failed.
public sealed unsafe partial class RynthApiTable
{
    private void BindActionSlots()
    {
        Native->SelectItemFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&SelectItem;
        Native->UseObjectFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&UseObject;
        Native->RequestIdFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&RequestId;
        Native->QueryHealthFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&QueryHealth;
        Native->ChangeCombatModeFn = (IntPtr)(delegate* unmanaged[Cdecl]<int, int>)&ChangeCombatMode;
        Native->MoveItemInternalFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int, int, int>)&MoveItemInternal;
        Native->SplitStackInternalFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int, int, int>)&SplitStackInternal;
        Native->MergeStackInternalFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int>)&MergeStackInternal;
        Native->ForceResetBusyCountFn = (IntPtr)(delegate* unmanaged[Cdecl]<void>)&ForceResetBusyCount;
        Native->WieldItemFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int>)&WieldItem;
    }

    // One body for every action: run it, map true to 1, catch and log everything.
    private static int Send(string function, Func<IRynthGameView, bool> action)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return action(table._view) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(function, error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SelectItem(uint objectId) =>
        Send(nameof(SelectItem), view => view.SelectObject(objectId));

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int UseObject(uint objectId) =>
        Send(nameof(UseObject), view => objectId != 0 && view.UseObject(objectId));

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int RequestId(uint objectId) =>
        Send(nameof(RequestId), view => objectId != 0 && view.RequestIdentify(objectId));

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int QueryHealth(uint objectId) =>
        Send(nameof(QueryHealth), view => objectId != 0 && view.QueryHealth(objectId));

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ChangeCombatMode(int mode) =>
        Send(nameof(ChangeCombatMode),
            view => RynthTranslate.FromRynthCombatMode(mode) is not null && view.ChangeCombatMode(mode));

    // RynthCore requires amount > 0 (its whole stack is the stack size, not 0).
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int MoveItemInternal(uint objectId, uint containerId, int slot, int amount) =>
        Send(nameof(MoveItemInternal),
            view => objectId != 0 && containerId != 0 && amount > 0 && view.MoveItem(objectId, containerId, slot, amount));

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SplitStackInternal(uint objectId, uint containerId, int slot, int amount) =>
        Send(nameof(SplitStackInternal),
            view => objectId != 0 && containerId != 0 && amount > 0 && view.MoveItem(objectId, containerId, slot, amount));

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int MergeStackInternal(uint sourceObjectId, uint targetObjectId) =>
        Send(nameof(MergeStackInternal),
            view => sourceObjectId != 0 && targetObjectId != 0 && view.MergeStack(sourceObjectId, targetObjectId));

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ForceResetBusyCount() =>
        Send(nameof(ForceResetBusyCount), view =>
        {
            view.ResetBusyCount();
            return true;
        });

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int WieldItem(uint objectId, uint equipMask) =>
        Send(nameof(WieldItem), view => objectId != 0 && view.WieldItem(objectId, equipMask));
}
```

The lambdas capture arguments, so each call allocates. That is fine at RynthAi's action rate: actions are paced at 100 ms or more.

- [ ] **Step 5: The runtime view sends them** (`RuntimeRynthGameView.cs`; add `using AcDream.Core.Selection;`)

```csharp
    public bool SelectObject(uint objectId)
    {
        SelectionState selection = _runtime.ActionOwner.Selection;
        selection.Select(objectId, SelectionChangeSource.Plugin);
        // RynthCore answers 1 for a repeat select too; OpenAC's Select is false when nothing changed.
        return (selection.SelectedObjectId ?? 0u) == objectId;
    }

    public bool UseObject(uint objectId) => _automation.Items.Use(objectId).Accepted;

    public bool RequestIdentify(uint objectId) => _automation.Objects.Identify(objectId).Accepted;

    public bool QueryHealth(uint objectId)
    {
        if (_runtime.Session.CurrentSession is not { } session)
            return false;
        session.SendQueryHealth(objectId);
        return true;
    }

    public bool ChangeCombatMode(int rynthMode) =>
        RynthTranslate.FromRynthCombatMode(rynthMode) is { } mode && _automation.Combat.EnterMode(mode).Accepted;

    public bool MoveItem(uint objectId, uint containerId, int slot, int amount) =>
        amount > 0 && _automation.Items.MoveToContainer(objectId, containerId, (uint)amount, slot).Accepted;

    public bool MergeStack(uint sourceObjectId, uint targetObjectId) =>
        _automation.Items.Merge(sourceObjectId, targetObjectId).Accepted;

    // RynthCore zeroes the busy count at once; OpenAC releases one reference per call.
    public void ResetBusyCount()
    {
        for (int i = 0; i < 64; i++)
        {
            PluginRecoveryResult result = _automation.Recovery.ClearOneBusyReference();
            if (!result.Accepted || result.CurrentCount <= 0)
                return;
        }
    }

    public bool WieldItem(uint objectId, uint equipMask) => _automation.Equipment.Equip(objectId, equipMask).Accepted;
```

- [ ] **Step 6: View tests** (append to `RuntimeRynthGameViewTests`)

```csharp
    [Fact]
    public void ActionsGoThroughTheSurface()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var automation = new FakeRynthAutomation();
        automation.RecoveryArea.Busy = 3;
        var view = new RuntimeRynthGameView(runtime, automation: automation);

        Assert.True(view.UseObject(0x80000010));
        Assert.True(view.MoveItem(0x80000011, 0x50000001, 2, 5));
        Assert.False(view.MoveItem(0x80000011, 0x50000001, 2, 0));
        Assert.True(view.MergeStack(0x80000012, 0x80000013));
        Assert.Equal(
            new[] { "use 80000010", "move 80000011 50000001 5 2", "merge 80000012 80000013 0" },
            automation.ItemArea.Calls);
        Assert.True(view.RequestIdentify(0x80000014));
        Assert.Equal(new[] { 0x80000014u }, automation.ObjectArea.Identified);
        Assert.True(view.WieldItem(0x80000015, 0x00200000));
        Assert.Equal(new[] { (0x80000015u, 0x00200000u) }, automation.EquipmentArea.Equipped);
        Assert.True(view.ChangeCombatMode(8));
        Assert.False(view.ChangeCombatMode(3));
        Assert.Equal(new[] { PluginCombatMode.Magic }, automation.CombatArea.ModesRequested);

        view.ResetBusyCount();
        Assert.Equal(0, automation.RecoveryArea.Busy);

        automation.ItemArea.Answer = PluginItemCommandStatus.Busy;
        Assert.False(view.UseObject(0x80000010));
    }

    [Fact]
    public void SelectIsTrueWhenTheObjectEndsUpSelectedAndQueryHealthNeedsASession()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var view = new RuntimeRynthGameView(runtime);

        Assert.True(view.SelectObject(0x80000010));
        Assert.True(view.SelectObject(0x80000010));   // already selected: still true, as in RynthCore
        Assert.Equal(0x80000010u, view.SelectedObjectId);
        Assert.False(view.QueryHealth(0x80000010));   // the test runtime has no session
    }
```

Run the Verify command. Expected: all pass. If `Select` refuses an object the client doesn't know, add `runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x80000010, Name = "Drudge" })` before selecting. That keeps the test about the repeat-select answer.

- [ ] **Step 7: Full check and commit**

```bash
~/.dotnet/dotnet build AcDream.slnx
~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests
git status --porcelain external   # must print nothing
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests tests/AcDream.App.Tests/Extensions
git commit -m "feat(rynth): the API table serves RynthAi's basic actions"
```

---

### Task 6: Live check on local ACE (deferred allowed)

**Goal:** On the local ACE server (GolemTest), RynthAi reads live state and its basic actions reach the server. **Ask the user before launching.** The user has deferred live checks before; if they defer again, leave this task pending and record that in the memory status note.

**Files:** none (no code change expected; any fix found becomes its own commit with a test first).

**Acceptance Criteria:**
- [ ] The client launches per `live-testing-rules` (local ACE 10.10.20.20 only; the env file is mapped inside a script, and passwords are never printed).
- [ ] RynthAi's dashboard shows live vitals and combat mode: switching to melee in the client shows Melee.
- [ ] `/ra` commands that use basic actions work. Check the exact verbs in `RynthAiCommands.cs` before the run (e.g. a clearbusy command, a select or use command). The client log shows no `threw` lines from `RynthApiTable`.
- [ ] Heading check: RynthAi's own pose-derived heading (`-2·atan2(qz,qw)`) agrees with the client's heading when facing north and east. This verifies OpenAC's quaternion convention matches retail's.
- [ ] Macro left off: RynthAi takes no game action on its own beyond its login health query.

**Verify:** user-observed on the live client, with the client log captured: `grep -c "threw" <client log>` → 0.

**Steps:**

- [ ] **Step 1:** Ask the user whether to run the live check now.
- [ ] **Step 2:** If yes, Release build, then launch per the memory note `live-testing-rules`, and walk the criteria with the user.
- [ ] **Step 3:** Record the result (pass, deferred, or the issues found) in the Rynth status memory.

---

## Self-review notes (planning, 2026-10-18)

- Every state and action slot in the user-approved scope has a task, except `StopMovement` and `MoveItemExternal`. Both moved to #4 and #5, with the reasons listed under "Corrections" above; the user hears about them in the hand-off.
- The member names used in Tasks 4 and 5 match the interface blocks in Step 2 / Step 1: `IsPortaling`, `BusyCount`, `CombatMode`, `ServerReplies`, `SelectObject`, `MoveItem`, `ResetBusyCount`. In `FakeGameView` the settable values are `Portaling`, `Busy`, `Mode`, `Replies`.
- The `RynthCoreHost` wrapper names used in the tests come from the SDK source: `TryGetObjectSkillLevel` wraps `GetObjectSkillBuffedFn`, and `IsSpellKnown(objectId, spellId, out bool)` returns false on -1.
