# RynthAi Loot, Salvage, Vendor and Trade (RynthAi sub-project #5) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RynthAi's item automation works in OpenAC: corpse looting into the pack it names, VTank colour rules, closing corpses, UST salvage, AutoVendor buy/sell, AutoTrade and the give commands.

**Architecture:**
- Task 1 adds RynthCore-style trade event counters to the client's `RuntimeTradeState` (plus TradeComplete through a new `ServerReplyLog` event).
- Task 2 is the contract change (`0.1.25-fork.9`): `ITradeAutomation.Open` / `Events` and `ILootAutomation.Pickup(id, preferredContainer)`, implemented in the runtime and forwarded by `ScopedPluginHost`.
- Tasks 3–6 bind the slots in `AcDream.Rynth.Host`: `IRynthGameView` grows members, `RuntimeRynthGameView` implements them over `GameRuntime` / `IAutomationSurface`, small host-side classes keep RynthCore's own state (`RynthSalvagePanel`, `RynthVendorTrades`), and new `RynthApiTable.*.cs` partials only marshal and catch.
- Task 7 is the live check, run with the user.

**Tech Stack:** .NET 10 (`~/.dotnet/dotnet`), xunit v3 (VSTest runner: `--filter "FullyQualifiedName~X"`), RynthCore/RynthSuite submodules (read only).

**Spec:** `docs/superpowers/specs/2026-10-19-rynth-ai-loot-vendor-trade-design.md` (approved 2026-10-19). RynthAi split: #1 dashboard, #2 state + actions, #3 combat + buffs, #4 navigation + Nav3D (all done), **#5 this plan**, #6 dat raycast + pathfinder.

## Global Constraints

- Repo `~/code/OpenAC/OpenAC` (the fork, `davidc-smith/OpenAC`). Work on branch `feat/rynth-ai-loot-trade` (already created from `main`, holds the spec commits). Do not push.
- Use `~/.dotnet/dotnet` (the system `dotnet` lacks SDK 10.0.300).
- **Never edit, copy or move anything inside `external/RynthCore` or `external/RynthSuite`.** `git status --porcelain external` must stay empty.
- `AcDream.Core`, `AcDream.Runtime`, `AcDream.App`, `AcDream.ClientExtensions`, `AcDream.Plugin.Abstractions` and `AcDream.Rynth.Host` build with warnings as errors and analyzers on. New code must build clean.
- **Table rule:** every `[UnmanagedCallersOnly]` table function null-checks its pointers, catches every exception, logs through `Fail`, and returns RynthCore's "not available" value. An exception crossing that boundary ends the process. Follow `src/AcDream.Rynth.Host/RynthApiTable.Actions.cs` exactly (`RynthApiTable? table = _current; if (table is null) return 0; try { … } catch (Exception error) { table.Fail(nameof(X), error); return 0; }`).
- `RynthApiTable.ApiVersion` stays **86**.
- **Plugin contract:** only Task 2 changes `AcDream.Plugin.Abstractions` (additive default interface members). `ForkContractVersion` goes from `0.1.25-fork.8` to `0.1.25-fork.9` in that task, nowhere else.
- **Slots set by this plan:** `MoveItemExternalFn`, `GiveObjectToFn`, `CloseContainerFn`, `GetObjectPalettesFn`, `GetNumContainedItemsFn`, `GetNumContainedContainersFn`, `SalvagePanelOpenFn`, `SalvagePanelAddItemFn`, `SalvagePanelExecuteFn`, `SalvageDirectFn`, `GetVendorInfoFn`, `GetVendorItemsFn`, `VendorBuyFn`, `VendorSellFn`, `GetVendorTradeStatusFn`, `GetTradeStateFn`, `GetTradeItemsFn`, `TradeOpenFn`, `TradeAddFn`, `TradeAcceptFn`, `TradeDeclineFn`, `TradeResetFn`, `TradeCloseFn`.
- **Slots still unset after this plan:** `NativeAttackFn`, `StopMovementFn`, `DoMovementFn`, `SetAutonomyLevelFn`, `WorldToScreenFn`, `GetViewportSizeFn`, `GetRadarRectFn`, `SetRadarSuppressedFn`, `GetPluginInterfaceFn`, `IsPlayerReadyFn`, `GetScreenModeFn`.
- All table functions run on the game update thread (`GameWindow.OnUpdate` → `ClientExtensionHost.Tick` → `RynthHost.Tick`). No locking, no queues.
- Rynth host tests run without parallelism (assembly attribute in `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`). Only one `RynthApiTable` can be live at a time, so each test disposes its table or host.
- Known unrelated failures exist in `AcDream.App.Tests` (installed dats, offscreen Vulkan). Run App tests with each task's filter. `AcDream.Rynth.Host.Tests` must be fully green; `AcDream.Runtime.Tests` must show no new failures (compare with `main` if one fails).
- Commit messages end with the line `Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi`.
- Never launch a client without asking the user first. Only the local ACE server 10.10.20.20, never Dreamweave. Never print passwords (`~/OpenAC-dev/dev-client.env`).

**User decisions (already made):**
- Scope: "All in one" — loot + colour rules + corpse close, salvage, vendor, trade and give in one sub-project.
- Loot target: "Honour the named pack" — target 0 or the player falls back to the client's own choice.
- Salvage: "Host-side panel state" — the retail salvage panel is not driven.
- Trade counters: "Count in the client" — `RuntimeTradeState` counts as messages arrive; `ITradeAutomation.Open` added; contract fork.9.
- Design sections 1 and 2 approved ("Yes"); "Proceed and you can write the implementation plan when ready - I trust your judgement".
- Standing rule: "we cant make changes to their code, and we need to be able to pull in their latest daily changes".

## Reference: RynthCore facts used below

- Slot signatures (`external/RynthCore/src/RynthCore.PluginSdk/RynthCoreHost.cs`): `int MoveItemExternal(uint obj, uint target, int amount)`, `int GiveObjectTo(uint obj, uint target, int amount)`, `int CloseContainer(uint)`, `int GetObjectPalettes(uint, uint* subIds, uint* offsets, int max)`, `int GetNumContainedItems(uint)`, `int GetNumContainedContainers(uint)`, `int SalvagePanelOpen(uint tool)`, `int SalvagePanelAddItem(uint)`, `int SalvagePanelExecute()`, `int SalvageDirect(uint tool, uint* ids, int n)`, `int GetVendorInfo(VendorInfoNative*)`, `int GetVendorItems(VendorItemNative*, int max)`, `uint VendorBuy(uint vendor, VendorTradeEntryNative*, int n)`, `uint VendorSell(uint vendor, uint* ids, int n)`, `int GetVendorTradeStatus(VendorTradeStatusNative*)`, `int GetTradeState(TradeStateNative*)`, `int GetTradeItems(int side, uint*, int cap)`, `int TradeOpen(uint)`, `int TradeAdd(uint item, uint slot)`, `int TradeAccept()`, `int TradeDecline()`, `int TradeReset()`, `int TradeClose()`.
- SDK wrappers used by tests: `MoveItemExternal`, `GiveObjectTo`, `CloseContainer`, `GetObjectPalettes(uint, uint[], uint[], int)`, `GetNumContainedItems`, `GetNumContainedContainers`, `SalvagePanelOpen/AddItem/Execute`, `SalvageDirect(uint, ReadOnlySpan<uint>)`, `TryGetVendorInfo(out VendorInfo)`, `GetVendorItems()`, `VendorBuy(uint, IReadOnlyList<VendorTradeEntryNative>)`, `VendorSell(uint, IReadOnlyList<uint>)`, `TryGetVendorTradeStatus(out VendorTradeStatus)`, `TryGetTradeState(out TradeState)`, `GetTradeItems(TradeSide)`, `TradeOpen/Add/Accept/Decline/Reset/Close`, and `HasMoveItemExternal`, `HasGiveObjectTo`, `HasCloseContainer`, `HasGetObjectPalettes`, `HasGetNumContainedItems`, `HasGetNumContainedContainers`, `HasSalvagePanel`, `HasSalvageDirect`, `HasVendorTrade`, `HasTrade`.
- Return conventions (RynthCore engine): contained counts and palettes return **-1** for an unknown object; palettes return the **total** count (may exceed `max`); `GetVendorItems` returns the total, -1 with no vendor; `GetTradeItems` returns that side's total, -1 for a bad side; `GetTradeState` needs `Size >= 8`, writes `min(Size, sizeof)` bytes, sets `Size` and returns it; `SalvageDirect` returns -2 without the slot (SDK side), 0 for bad input; a vendor request refused before sending returns 0.
- Trade counters (`external/RynthCore/src/RynthCore.Engine/Compatibility/PlayerTrade.cs`): every trade GameEvent `0x01FD`–`0x0208` bumps Sequence and sets LastEventType. Register: Generation+1, Initiator = a, Partner = a unless a is you, else b. Close: reason. Accept: LastAcceptedBy; PartnerAcceptCount+1 when not you. Decline: LastDeclinedBy. Reset: LastResetBy. Failure: FailureCount+1, item, reason. WeenieError `0x0529` TradeComplete: CompletedCount+1, Sequence+1. Flags: 1 open, 2 you accepted, 4 partner accepted, 8 watching, 16 actions available. Counters never reset.
- Vendor (`Compatibility/VendorTrade.cs`): status states 1 Queued, 2 Sent, 3 Done, 4 Refused; results 1 Accepted, 2 AnsweredNoRefresh, 3 TimedOut. In-flight timeout 10 s. Max 100 lines, amount 1..0x7FFFFF. Item flags 1 unlimited, 2 stackable (MaxStackSize > 0), 4 needs container slot. `Amount` -1 for unlimited. Info flags 1 trading available, 2 request in flight. Vendor events: `QueueVendorOpen` on every OpenVendor (first open and every re-sent list); switching vendors queues the old one's close first; drain order puts VendorOpen, VendorClose after StopViewingObjectContents and before UpdateObjectInventory.
- RynthPlugin exports: `RynthPluginOnVendorOpen(uint)` / `RynthPluginOnVendorClose(uint)`.

---

### Task 1: Trade event counters in the client

**Goal:** `RuntimeTradeState` keeps RynthCore's trade counters as each trade message arrives, including the close reason, who reset, and TradeComplete, exposed on a new `RuntimeTradeCounters` snapshot.

**Files:**
- Modify: `src/AcDream.Runtime/Gameplay/RuntimeTradeState.cs` (add counters, `ApplyClose(uint reason = 0u)`, `ApplyReset(uint whoReset = 0u)`, `ApplyTradeComplete()`, `Counters`, `IRuntimeTradeView.Counters`)
- Modify: `src/AcDream.Runtime/Session/LiveSessionEventRouter.cs:340-363` (pass the close reason and reset id on)
- Modify: `src/AcDream.Core/Items/ServerReplyLog.cs` (raise `WeenieErrorRecorded` after recording)
- Modify: `src/AcDream.Runtime/GameRuntime.cs:267` (subscribe trade to `ServerReplies.WeenieErrorRecorded` for `WeenieError.TradeComplete`)
- Test: `tests/AcDream.Runtime.Tests/Gameplay/RuntimeTradeStateTests.cs`, `tests/AcDream.Core.Tests` (ServerReplyLog event; put next to existing ServerReplyLog tests — `grep -rln ServerReplyLog tests/AcDream.Core.Tests`)

**Acceptance Criteria:**
- [ ] Register bumps Generation and records Initiator; every Apply* bumps Sequence and sets LastEventType to its opcode (0x01FD Register, 0x01FF Close, 0x0200 Add, 0x0201 Remove, 0x0202 Accept, 0x0203 Decline, 0x0205 Reset, 0x0207 Failure, 0x0208 ClearAcceptance), also when no trade is open.
- [ ] Accept by the partner bumps PartnerAcceptCount and sets LastAcceptedBy; accept by self sets LastAcceptedBy only.
- [ ] Close records LastCloseReason; Reset records LastResetBy; Failure bumps FailureCount.
- [ ] `ApplyTradeComplete` bumps CompletedCount and Sequence; a WeenieError 0x0529 recorded on the runtime's `ServerReplies` reaches it.
- [ ] Two events applied back to back both count (Sequence +2).
- [ ] Existing `RuntimeTradeStateTests` and router tests still pass.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~Trade"` and `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ServerReply"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (add to `RuntimeTradeStateTests`, reusing its `Self`/`Partner`/`ItemA` constants)

```csharp
[Fact]
public void CountersFollowEveryTradeEventLikeRynthCore()
{
    using var trade = new RuntimeTradeState();

    trade.ApplyRegister(new GameEvents.RegisterTrade(Partner, Partner, 0uL), Self);
    trade.ApplyAdd(new GameEvents.AddToTrade(ItemA, (uint)RuntimeTradeSide.Self, 0u));
    trade.ApplyAccept(Partner, Self);
    trade.ApplyAccept(Self, Self);
    trade.ApplyFailure(new GameEvents.TradeFailure(ItemA, 0x0426u));
    trade.ApplyDecline(Partner, Self);
    trade.ApplyReset(Partner);
    trade.ApplyClearAcceptance();
    trade.ApplyTradeComplete();
    trade.ApplyClose(0x51u);

    RuntimeTradeCounters c = trade.View.Counters;
    Assert.Equal(1u, c.Generation);
    Assert.Equal(10u, c.Sequence);
    Assert.Equal(Partner, c.InitiatorId);
    Assert.Equal(0x01FFu, c.LastEventType);
    Assert.Equal(1u, c.PartnerAcceptCount);
    Assert.Equal(Self, c.LastAcceptedBy);
    Assert.Equal(Partner, c.LastDeclinedBy);
    Assert.Equal(Partner, c.LastResetBy);
    Assert.Equal(1u, c.FailureCount);
    Assert.Equal(ItemA, c.LastFailureItemId);
    Assert.Equal(0x0426u, c.LastFailureReason);
    Assert.Equal(1u, c.CompletedCount);
    Assert.Equal(0x51u, c.LastCloseReason);
}

[Fact]
public void CountersMoveEvenWithNoTradeOpen()
{
    using var trade = new RuntimeTradeState();

    trade.ApplyAccept(Partner, Self);
    trade.ApplyReset(Partner);

    RuntimeTradeCounters c = trade.View.Counters;
    Assert.Equal(2u, c.Sequence);
    Assert.Equal(0x0205u, c.LastEventType);
    Assert.Equal(0u, c.Generation);
    Assert.False(trade.View.Snapshot.IsOpen);
}

[Fact]
public void ARegisterAfterACloseStartsANewGeneration()
{
    using var trade = new RuntimeTradeState();

    trade.ApplyRegister(new GameEvents.RegisterTrade(Self, Partner, 0uL), Self);
    trade.ApplyClose(1u);
    trade.ApplyRegister(new GameEvents.RegisterTrade(Partner, Partner, 0uL), Self);

    Assert.Equal(2u, trade.View.Counters.Generation);
    Assert.Equal(Partner, trade.View.Counters.InitiatorId);
    Assert.Equal(1u, trade.View.Counters.LastCloseReason);
}
```

And in the Core tests next to the existing `ServerReplyLog` tests:

```csharp
[Fact]
public void WeenieErrorRecordedIsRaisedAfterTheSnapshotMoves()
{
    var log = new ServerReplyLog();
    var seen = new List<(uint Code, int Sequence)>();
    log.WeenieErrorRecorded += code => seen.Add((code, log.Snapshot.WeenieErrorSequence));

    log.RecordWeenieError(0x28Au, 0x0529u, 0u);

    Assert.Equal(new[] { (0x0529u, 1) }, seen);
}
```

- [ ] **Step 2: Run them to see them fail** (`RuntimeTradeCounters`, `ApplyTradeComplete`, the new `ApplyClose`/`ApplyReset` arguments and `WeenieErrorRecorded` do not exist)

Run: `~/.dotnet/dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~RuntimeTradeStateTests"`
Expected: build error.

- [ ] **Step 3: Implement**

In `RuntimeTradeState.cs`, next to `RuntimeTradeSnapshot`:

```csharp
/// <summary>
/// RynthCore's trade counters (its GetTradeState): they only ever grow, so a
/// caller compares two reads to see what happened between them.
/// </summary>
public readonly record struct RuntimeTradeCounters(
    uint Generation,
    uint Sequence,
    uint InitiatorId,
    uint LastEventType,
    uint FailureCount,
    uint LastFailureItemId,
    uint LastFailureReason,
    uint LastCloseReason,
    uint LastAcceptedBy,
    uint LastDeclinedBy,
    uint LastResetBy,
    uint CompletedCount,
    uint PartnerAcceptCount);
```

Add `RuntimeTradeCounters Counters { get; }` to `IRuntimeTradeView` and implement it in the nested `TradeView` (read under `_gate`). Add a private `RuntimeTradeCounters _counters;` and a helper, called inside the lock at the **top** of each `Apply*` (before the `if (!_isOpen) return;` early returns, after the disposed check):

```csharp
// Every trade event counts, open trade or not, as RynthCore's PlayerTrade does.
private void CountLocked(uint eventType, Func<RuntimeTradeCounters, RuntimeTradeCounters>? change = null)
{
    RuntimeTradeCounters next = _counters with
    {
        Sequence = unchecked(_counters.Sequence + 1u),
        LastEventType = eventType,
    };
    _counters = change is null ? next : change(next);
}
```

Calls per method (opcode, then the change):
- `ApplyRegister`: `CountLocked(0x01FDu, c => c with { Generation = unchecked(c.Generation + 1u), InitiatorId = update.Initiator })`
- `ApplyClose(uint reason = 0u)`: `CountLocked(0x01FFu, c => c with { LastCloseReason = reason })`
- `ApplyAdd`: `CountLocked(0x0200u)`
- `ApplyRemove`: `CountLocked(0x0201u)`
- `ApplyAccept`: `CountLocked(0x0202u, c => c with { LastAcceptedBy = whoAccepted, PartnerAcceptCount = whoAccepted != selfGuid ? unchecked(c.PartnerAcceptCount + 1u) : c.PartnerAcceptCount })`
- `ApplyDecline`: `CountLocked(0x0203u, c => c with { LastDeclinedBy = whoDeclined })`
- `ApplyReset(uint whoReset = 0u)`: `CountLocked(0x0205u, c => c with { LastResetBy = whoReset })`
- `ApplyFailure`: `CountLocked(0x0207u, c => c with { FailureCount = unchecked(c.FailureCount + 1u), LastFailureItemId = failure.ItemGuid, LastFailureReason = failure.Reason })`
- `ApplyClearAcceptance`: `CountLocked(0x0208u)`

`ApplyClose` currently has no early return; add the count inside its existing lock. New method:

```csharp
/// <summary>WeenieError 0x0529 TradeComplete: the server carried a trade out.</summary>
public void ApplyTradeComplete()
{
    lock (_gate)
    {
        if (_disposed) return;
        _counters = _counters with
        {
            CompletedCount = unchecked(_counters.CompletedCount + 1u),
            Sequence = unchecked(_counters.Sequence + 1u),
        };
    }
}
```

`Clear()` and `ClearLocked()` do **not** touch `_counters`.

In `LiveSessionEventRouter.cs`, change `onTradeClose: … ? _ => { tradeClose.ApplyClose(); …` to `? reason => { tradeClose.ApplyClose(reason); …` and `onTradeReset: … ? _ => tradeReset.ApplyReset()` to `? whoReset => tradeReset.ApplyReset(whoReset)` (check the delegate parameter types: the close callback carries the `ParseCloseTrade` reason `uint`; the reset callback carries the resetter's guid — if either is not a `uint`, convert to `uint` there).

In `ServerReplyLog.cs`:

```csharp
/// <summary>Raised with the refusal code after <see cref="RecordWeenieError"/> records it, outside the lock.</summary>
public event Action<uint>? WeenieErrorRecorded;
```

and at the end of `RecordWeenieError`, after the lock: `WeenieErrorRecorded?.Invoke(weenieError);`.

In `GameRuntime.cs`, right after `context.Trade = new RuntimeTradeState(...)` and `construction.Own(context.Trade);` (the inventory already exists; check `context.Inventory` is set before line 267 — if not, place the subscription after both are created):

```csharp
RuntimeTradeState trade = context.Trade;
context.Inventory!.ServerReplies.WeenieErrorRecorded += code =>
{
    if (code == (uint)AcDream.Core.Physics.WeenieError.TradeComplete)
        trade.ApplyTradeComplete();
};
```

(Both live exactly as long as the runtime, so no unsubscribe is needed.) Add a Runtime test that drives this through the runtime if a fixture that builds a `GameRuntime` is already used by `RuntimeTradeAutomationTests`; otherwise the two unit tests above are enough.

- [ ] **Step 4: Run the tests**

Run: the Verify commands. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Runtime/Gameplay/RuntimeTradeState.cs src/AcDream.Runtime/Session/LiveSessionEventRouter.cs src/AcDream.Core/Items/ServerReplyLog.cs src/AcDream.Runtime/GameRuntime.cs tests/
git commit -m "feat(trade): RynthCore-style trade counters, close reason and TradeComplete

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```

---

### Task 2: Contract fork.9 — trade Open/Events, pickup into a pack

**Goal:** Plugins can open a trade, read the trade counters, and loot into a preferred pack; contract `0.1.25-fork.9`.

**Files:**
- Modify: `src/AcDream.Plugin.Abstractions/TradeAutomation.cs` (add `PluginTradeEventCounters`, `ITradeAutomation.Open`, `ITradeAutomation.Events`)
- Modify: `src/AcDream.Plugin.Abstractions/LootAutomation.cs:246` (add the `Pickup(uint, uint)` overload)
- Modify: `src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj:39` (`0.1.25-fork.9`)
- Modify: `src/AcDream.Runtime/Gameplay/RuntimeTradeAutomation.cs` (implement `Open`, `Events`)
- Modify: `src/AcDream.Runtime/Gameplay/RuntimeItemInteraction.cs:1043` (`TryPlaceWorldItemInBackpack(uint itemGuid, bool mainPack = false, uint preferredContainer = 0u)`)
- Modify: `src/AcDream.Runtime/Plugins/RuntimeAutomationSurface.cs` (`BindItems` optional `pickupItemInto`, `Pickup(uint, uint)` overload next to `Pickup` at ~5358)
- Modify: `src/AcDream.Runtime/Plugins/RuntimeAutomationBindings.cs:337-349` (bind `pickupItemInto`)
- Modify: `src/AcDream.Core/Plugins/ScopedPluginHost.cs:538-560` (forward `Open`, `Events`; `Loot` is already a pass-through)
- Modify: `docs/plugin-api.md` (trade and loot sections; contract version note)
- Test: `tests/AcDream.Runtime.Tests/Gameplay/RuntimeTradeAutomationTests.cs`, the runtime test that covers `Pickup` (`grep -rln "\.Pickup(" tests/AcDream.Runtime.Tests tests/AcDream.Headless.Tests`), `tests/AcDream.Plugin.Tests` if it has a scoped-host forwarding test for trade (`grep -rn "Scoped.*Trade\|TradeAutomation" tests/AcDream.Plugin.Tests tests/AcDream.Core.Tests | head`)

**Acceptance Criteria:**
- [ ] `ITradeAutomation.Open(partner)` sends `SendOpenTradeNegotiations(partner)` and answers `Sent`; answers `Unavailable` out of world or with no session, `Refused` when a trade is already open, `InvalidItem` for partner 0 or the player.
- [ ] `ITradeAutomation.Events` returns the runtime's `RuntimeTradeCounters` as `PluginTradeEventCounters`; the interface default is all zero.
- [ ] `ILootAutomation.Pickup(id, pack)` with pack 0 or the player behaves exactly as `Pickup(id)`; with a pack the player owns it places into that pack when it has room and otherwise falls back as the placement search does; with anything else it answers `InvalidTarget` and sends nothing.
- [ ] `ScopedPluginHost`'s trade wrapper forwards `Open` and `Events`.
- [ ] `ForkContractVersion` is `0.1.25-fork.9`; `docs/plugin-api.md` documents the three members and that fork.9 added them.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~Trade|FullyQualifiedName~Pickup|FullyQualifiedName~Loot"` and `~/.dotnet/dotnet build src/AcDream.App` → pass, build clean

**Steps:**

- [ ] **Step 1: Write the failing tests.** In `RuntimeTradeAutomationTests` follow the existing fixture (it builds a runtime with a recording session; reuse whatever it uses to assert `SendAddToTrade`, and assert the open the same way):

```csharp
[Fact]
public void OpenSendsTheTradeRequestToThePartner()
{
    // arrange as the existing Add test does: in world, no trade open
    PluginTradeCommandResult result = automation.Open(PartnerGuid);
    Assert.Equal(PluginTradeCommandStatus.Sent, result.Status);
    // assert the recorded wire call: OpenTradeNegotiations(PartnerGuid)
}

[Fact]
public void OpenIsRefusedWhileATradeIsOpenAndForSelfOrZero()
{
    // arrange: in world; apply a RegisterTrade so a trade is open
    Assert.Equal(PluginTradeCommandStatus.Refused, automation.Open(PartnerGuid).Status);
    // close it again
    Assert.Equal(PluginTradeCommandStatus.InvalidItem, automation.Open(0u).Status);
    Assert.Equal(PluginTradeCommandStatus.InvalidItem, automation.Open(SelfGuid).Status);
}

[Fact]
public void EventsMirrorTheRuntimeCounters()
{
    // arrange: apply RegisterTrade then AcceptTrade(partner) to the runtime's TradeOwner
    PluginTradeEventCounters events = automation.Events;
    Assert.Equal(1u, events.Generation);
    Assert.Equal(2u, events.Sequence);
    Assert.Equal(1u, events.PartnerAcceptCount);
    Assert.Equal(0x0202u, events.LastEventType);
}
```

Fill the arrange/assert lines from the fixture the existing tests in that file use (it already drives `Add`/`Accept` against a recording session). For pickup, next to the existing `Pickup` surface test:

```csharp
[Fact]
public void PickupIntoAPackPrefersThatPack()
{
    // arrange as the existing Pickup test: a corpse open with ItemA inside; the player
    // owns SidePack (ItemsCapacity 24, empty) in the main pack
    PluginItemCommandResult result = surface.Loot.Pickup(ItemA, SidePack);
    Assert.Equal(PluginItemCommandStatus.Started, result.Status);
    // assert the recorded PutItemInContainer went to SidePack
}

[Fact]
public void PickupIntoAContainerThePlayerDoesNotOwnIsRefused()
{
    // arrange as above
    Assert.Equal(PluginItemCommandStatus.InvalidTarget, surface.Loot.Pickup(ItemA, CorpseId).Status);
    // assert nothing was sent
}
```

- [ ] **Step 2: Run them to see them fail** (members missing → build error).

- [ ] **Step 3: Implement the contract.** In `TradeAutomation.cs`:

```csharp
/// <summary>
/// Trade event counters (RynthCore's trade state): Generation counts trades opened,
/// Sequence every trade event. They only ever grow; compare two reads to see what
/// happened between them. Fork contract 0.1.25-fork.9.
/// </summary>
public readonly record struct PluginTradeEventCounters(
    uint Generation,
    uint Sequence,
    uint InitiatorId,
    uint LastEventType,
    uint FailureCount,
    uint LastFailureItemId,
    uint LastFailureReason,
    uint LastCloseReason,
    uint LastAcceptedBy,
    uint LastDeclinedBy,
    uint LastResetBy,
    uint CompletedCount,
    uint PartnerAcceptCount);
```

In `ITradeAutomation`, after `PartnerAccepted`:

```csharp
/// <summary>
/// The trade event counters. All zero when this host does not provide them
/// (before fork contract 0.1.25-fork.9).
/// </summary>
PluginTradeEventCounters Events => default;

/// <summary>
/// Asks the server to open a trade with another player, the same as the
/// client's trade request. <see cref="PluginTradeCommandStatus.Refused"/> while a
/// trade is already open; <see cref="PluginTradeCommandStatus.InvalidItem"/> for 0
/// or the player. Fork contract 0.1.25-fork.9.
/// </summary>
PluginTradeCommandResult Open(uint partnerObjectId) =>
    new(PluginTradeCommandStatus.Unavailable);
```

In `ILootAutomation`, after `Pickup(uint, bool)`:

```csharp
/// <summary>
/// <see cref="Pickup(uint, bool)"/> into a preferred pack: the player's own
/// container <paramref name="preferredContainerObjectId"/> when it has room, else
/// where the client's own placement would put it. 0 or the player's id is the
/// client's own choice. <see cref="PluginItemCommandStatus.InvalidTarget"/> for a
/// container the player does not own. Fork contract 0.1.25-fork.9.
/// </summary>
PluginItemCommandResult Pickup(uint objectId, uint preferredContainerObjectId) =>
    new(PluginItemCommandStatus.Unavailable);
```

(`Pickup(id)` keeps binding to the `bool` overload because its parameter is optional; check `Pickup(id, true)` still compiles — a `bool` literal never converts to `uint`.)

- [ ] **Step 4: Implement the runtime.** `RuntimeTradeAutomation`:

```csharp
public PluginTradeEventCounters Events
{
    get
    {
        RuntimeTradeCounters c = _runtime.Trade.Counters;
        return new(c.Generation, c.Sequence, c.InitiatorId, c.LastEventType, c.FailureCount,
            c.LastFailureItemId, c.LastFailureReason, c.LastCloseReason, c.LastAcceptedBy,
            c.LastDeclinedBy, c.LastResetBy, c.CompletedCount, c.PartnerAcceptCount);
    }
}

public PluginTradeCommandResult Open(uint partnerObjectId)
{
    if (!IsAvailable)
        return new(PluginTradeCommandStatus.Unavailable);
    if (partnerObjectId == 0u || partnerObjectId == _runtime.PlayerIdentity.ServerGuid)
        return new(PluginTradeCommandStatus.InvalidItem);
    if (Snapshot.IsOpen)
        return new(PluginTradeCommandStatus.Refused, "a trade is already open");
    if (Session is not { } session)
        return new(PluginTradeCommandStatus.Unavailable);
    session.SendOpenTradeNegotiations(partnerObjectId);
    return new(PluginTradeCommandStatus.Sent);
}
```

`RuntimeItemInteraction.TryPlaceWorldItemInBackpack(uint itemGuid, bool mainPack = false, uint preferredContainer = 0u)`: replace `uint target = PreferredBackpackContainer(mainPack);` with `uint target = preferredContainer != 0u ? preferredContainer : PreferredBackpackContainer(mainPack);`. Nothing else changes (the merge plan, room check and dispatch are the same). `PlaceWorldItemInBackpack` keeps calling it with two arguments. Because the method now has three parameters, the method group in `RuntimeAutomationBindings` no longer converts to `Func<uint, bool, RuntimeBackpackPlacementOutcome>`: change that argument to `(item, mainPack) => itemOwner.TryPlaceWorldItemInBackpack(item, mainPack)`, and check `RuntimeWorldObjectUse.cs:195` still compiles.

`RuntimeAutomationSurface.BindItems`: add a last optional parameter `Func<uint, uint, AcDream.Runtime.Gameplay.RuntimeBackpackPlacementOutcome>? pickupItemInto = null`, stored in a new `_pickupItemInto` field under `_gate` (cleared where `_pickupItem` is cleared, ~line 6370). Bind it in `RuntimeAutomationBindings`: `pickupItemInto: (item, pack) => itemOwner.TryPlaceWorldItemInBackpack(item, false, pack)`, and add `bound.Add("BindItems.pickupItemInto");` next to the other `BindItems.*` lines (check whether a test asserts the bound list and update it).

Refactor `Pickup(uint objectId, bool mainPack = false)` so its body after the checks is shared with the new overload:

```csharp
public PluginItemCommandResult Pickup(uint objectId, uint preferredContainerObjectId)
{
    GameRuntime? runtime;
    lock (_gate)
        runtime = _runtime;
    if (runtime is null || !IsAvailable)
        return new(PluginItemCommandStatus.Unavailable);
    uint player = runtime.PlayerIdentity.ServerGuid;
    if (preferredContainerObjectId == 0u || preferredContainerObjectId == player)
        return Pickup(objectId);
    ClientObjectTable objects = runtime.InventoryOwner.Objects;
    if (!TryGetOwned(objects, player, preferredContainerObjectId, out ClientObject? pack)
        || pack!.ItemsCapacity <= 0)
    {
        return new(PluginItemCommandStatus.InvalidTarget);
    }
    Func<uint, uint, AcDream.Runtime.Gameplay.RuntimeBackpackPlacementOutcome>? into;
    lock (_gate)
        into = _pickupItemInto;
    if (into is null)
        return new(PluginItemCommandStatus.Unavailable);
    return PickupChecked(runtime, objectId, () => into(objectId, preferredContainerObjectId));
}
```

where `PickupChecked(GameRuntime runtime, uint objectId, Func<RuntimeBackpackPlacementOutcome> place)` is the existing body of `Pickup(uint, bool)` from the `uint root = …` line to the end, with `pickup(objectId, mainPack)` replaced by `place()`; `Pickup(uint, bool)` calls it with `() => pickup(objectId, mainPack)`. (Use `ItemsCapacity > 0` as "is a container", the same test `CaptureContainerIds` makes.)

`ScopedPluginHost` trade wrapper: `public PluginTradeEventCounters Events => source().Events;` and `public PluginTradeCommandResult Open(uint partnerObjectId) => source().Open(partnerObjectId);`.

- [ ] **Step 5: Version and docs.** `<ForkContractVersion>0.1.25-fork.9</ForkContractVersion>`. In `docs/plugin-api.md`, in the trade section document `Events` (the field meanings from the record's doc) and `Open`; in the loot section document `Pickup(id, preferredContainer)`; note "Fork contract 0.1.25-fork.9" on each, matching how fork.8's `ColorB`/`ColorC` note is written (`grep -n "fork.8" docs/plugin-api.md`).

- [ ] **Step 6: Run the tests and build.** Run the Verify commands, plus `~/.dotnet/dotnet test tests/AcDream.Plugin.Tests` and `~/.dotnet/dotnet test tests/AcDream.Headless.Tests --filter "FullyQualifiedName~Trade|FullyQualifiedName~Loot"`. Expected: PASS, no new failures.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Plugin.Abstractions src/AcDream.Runtime src/AcDream.Core/Plugins/ScopedPluginHost.cs docs/plugin-api.md tests/
git commit -m "feat(plugins): trade Open and event counters, pickup into a pack (contract 0.1.25-fork.9)

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```

---

### Task 3: Loot, give, close, palettes and contained counts slots

**Goal:** `MoveItemExternal`, `GiveObjectTo`, `CloseContainer`, `GetObjectPalettes`, `GetNumContainedItems` and `GetNumContainedContainers` work through the RynthCore table.

**Files:**
- Modify: `src/AcDream.Rynth.Host/IRynthGameView.cs` (add five members)
- Modify: `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs` (implement them)
- Create: `src/AcDream.Rynth.Host/RynthApiTable.Loot.cs`
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.cs` (call `BindLootSlots()` after `BindNav3DSlots()`)
- Modify: `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthApiTableTests.cs`

**Acceptance Criteria:**
- [ ] `MoveItemExternal(item, 0, 0)` and `(item, player, 0)` call `PickupItem(item, 0)`; `(item, pack, 0)` calls `PickupItem(item, pack)`; a non-zero amount or item 0 answers false and calls nothing.
- [ ] `GiveObjectTo(item, target, n)` calls `GiveItem(item, target, n)` for n ≥ 0; negative n, item 0 or target 0 answer false.
- [ ] `CloseContainer(id)` calls `CloseContainer(id)`; 0 answers false.
- [ ] `GetObjectPalettes` fills up to `max` in offset order and returns the total; -1 for an unknown object.
- [ ] `GetNumContainedItems` / `GetNumContainedContainers` return the view's counts; -1 for an unknown container.
- [ ] A throwing view logs "X threw" and answers 0 / -1.
- [ ] `DeferredSlotsStayUnset` no longer lists `MoveItemExternalFn`, `GetObjectPalettesFn`, `GetNumContainedItemsFn`, `GetNumContainedContainersFn`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass

**Steps:**

- [ ] **Step 1: Add the view members** (`IRynthGameView.cs`, after `TryGetObjectMotionOn`):

```csharp
/// <summary>Loots an item from the open container into a preferred pack (0 = the client's own choice). True when sent.</summary>
bool PickupItem(uint objectId, uint preferredContainerId);

/// <summary>Gives all (0) or part of an owned stack to a world target. True when sent.</summary>
bool GiveItem(uint objectId, uint targetId, uint amount);

/// <summary>Closes the open corpse or chest. True when sent.</summary>
bool CloseContainer(uint containerId);

/// <summary>An object's sub-palettes (id, offset) in offset order; null when the object is unknown.</summary>
IReadOnlyList<(uint SubPaletteId, uint Offset)>? GetPalettes(uint objectId);

/// <summary>A container's direct contents split into items and containers (worn items are not contents); false when it is unknown.</summary>
bool TryCountContents(uint containerId, out int items, out int containers);
```

- [ ] **Step 2: Fake** (`FakeGameView.cs`, after `TryGetObjectMotionOn`):

```csharp
public Dictionary<uint, List<(uint SubPaletteId, uint Offset)>> Palettes { get; } = new();
public Dictionary<uint, (int Items, int Containers)> ContentCounts { get; } = new();

public bool PickupItem(uint objectId, uint preferredContainerId) => Act($"pickup {objectId:X8} {preferredContainerId:X8}");
public bool GiveItem(uint objectId, uint targetId, uint amount) => Act($"give {objectId:X8} {targetId:X8} {amount}");
public bool CloseContainer(uint containerId) => Act($"close {containerId:X8}");
public IReadOnlyList<(uint SubPaletteId, uint Offset)>? GetPalettes(uint objectId) =>
    Read(Palettes).TryGetValue(objectId, out var list) ? list : null;
public bool TryCountContents(uint containerId, out int items, out int containers)
{
    if (Throw) throw new InvalidOperationException("view failed");
    bool known = ContentCounts.TryGetValue(containerId, out var counts);
    (items, containers) = counts;
    return known;
}
```

(`Read<T>(T)` is the fake's existing helper that throws when `Throw` is set; check its name in the file and use it.)

- [ ] **Step 3: Write the failing tests** (`RynthApiTableTests.cs`):

```csharp
[Fact]
public void LootSlots_MoveGiveAndCloseReachTheView()
{
    const uint player = 0x50000001, item = 0x80000100, pack = 0x80000200, corpse = 0x80000300;
    var view = new FakeGameView { PlayerId = player };
    using var table = new RynthApiTable(view, _log.Add);
    var host = new RynthCoreHost(*table.Native);

    Assert.True(host.HasMoveItemExternal);
    Assert.True(host.HasGiveObjectTo);
    Assert.True(host.HasCloseContainer);
    Assert.True(host.MoveItemExternal(item, 0, 0));
    Assert.True(host.MoveItemExternal(item, player, 0));
    Assert.True(host.MoveItemExternal(item, pack, 0));
    Assert.False(host.MoveItemExternal(item, pack, 5));
    Assert.False(host.MoveItemExternal(0, pack, 0));
    Assert.True(host.GiveObjectTo(item, 0x50000002, 0));
    Assert.True(host.GiveObjectTo(item, 0x50000002, 3));
    Assert.False(host.GiveObjectTo(item, 0x50000002, -1));
    Assert.False(host.GiveObjectTo(item, 0, 0));
    Assert.True(host.CloseContainer(corpse));
    Assert.False(host.CloseContainer(0));
    Assert.Equal(
        new[]
        {
            "pickup 80000100 00000000", "pickup 80000100 00000000", "pickup 80000100 80000200",
            "give 80000100 50000002 0", "give 80000100 50000002 3", "close 80000300",
        },
        view.Actions);
}

[Fact]
public void LootSlots_PalettesAndCountsFollowRynthCoresReturns()
{
    var view = new FakeGameView { PlayerId = 0x50000001 };
    view.Palettes[0x80000100] = [(0x04001234, 0), (0x04005678, 8), (0x04009ABC, 16)];
    view.ContentCounts[0x50000001] = (40, 3);
    using var table = new RynthApiTable(view, _log.Add);
    var host = new RynthCoreHost(*table.Native);

    Assert.True(host.HasGetObjectPalettes);
    var ids = new uint[2];
    var offsets = new uint[2];
    Assert.Equal(3, host.GetObjectPalettes(0x80000100, ids, offsets, 2));
    Assert.Equal(new uint[] { 0x04001234, 0x04005678 }, ids);
    Assert.Equal(new uint[] { 0, 8 }, offsets);
    Assert.Equal(-1, host.GetObjectPalettes(0x80000999, ids, offsets, 2));

    Assert.Equal(40, host.GetNumContainedItems(0x50000001));
    Assert.Equal(3, host.GetNumContainedContainers(0x50000001));
    Assert.Equal(-1, host.GetNumContainedItems(0x80000999));
    Assert.Equal(-1, host.GetNumContainedContainers(0x80000999));

    view.Throw = true;
    Assert.Equal(-1, host.GetNumContainedItems(0x50000001));
    Assert.False(host.CloseContainer(0x80000300));
    Assert.Contains(_log, line => line.Contains("GetNumContainedItems threw", StringComparison.Ordinal));
    Assert.Contains(_log, line => line.Contains("CloseContainer threw", StringComparison.Ordinal));
}
```

Remove `MoveItemExternalFn`, `GetObjectPalettesFn`, `GetNumContainedItemsFn`, `GetNumContainedContainersFn` from `DeferredSlotsStayUnset`.

- [ ] **Step 4: Run them to see them fail** (`~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~LootSlots"`; expect the Has* asserts to fail).

- [ ] **Step 5: Implement the table partial** (`RynthApiTable.Loot.cs`):

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// The table's loot, give and container-count slots (RynthAi sub-project #5).
// Counts and palettes answer -1 for an unknown object, as RynthCore does.
public sealed unsafe partial class RynthApiTable
{
    private void BindLootSlots()
    {
        Native->MoveItemExternalFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int, int>)&MoveItemExternal;
        Native->GiveObjectToFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int, int>)&GiveObjectTo;
        Native->CloseContainerFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&CloseContainer;
        Native->GetObjectPalettesFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, uint*, int, int>)&GetObjectPalettes;
        Native->GetNumContainedItemsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&GetNumContainedItems;
        Native->GetNumContainedContainersFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&GetNumContainedContainers;
    }

    // RynthAi always loots whole stacks (amount 0); OpenAC's pickup has no partial amount.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int MoveItemExternal(uint objectId, uint targetContainerId, int amount)
    {
        RynthApiTable? table = _current;
        if (table is null || objectId == 0 || amount != 0)
            return 0;
        try
        {
            uint preferred = targetContainerId == table._view.PlayerId ? 0u : targetContainerId;
            return table._view.PickupItem(objectId, preferred) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(MoveItemExternal), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GiveObjectTo(uint objectId, uint targetId, int amount)
    {
        RynthApiTable? table = _current;
        if (table is null || objectId == 0 || targetId == 0 || amount < 0)
            return 0;
        try { return table._view.GiveItem(objectId, targetId, (uint)amount) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(GiveObjectTo), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int CloseContainer(uint containerId)
    {
        RynthApiTable? table = _current;
        if (table is null || containerId == 0)
            return 0;
        try { return table._view.CloseContainer(containerId) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(CloseContainer), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectPalettes(uint objectId, uint* subIds, uint* offsets, int maxCount)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return -1;
        try
        {
            if (table._view.GetPalettes(objectId) is not { } palettes)
                return -1;
            if (subIds is not null && offsets is not null)
            {
                int n = Math.Min(Math.Max(maxCount, 0), palettes.Count);
                for (int i = 0; i < n; i++)
                {
                    subIds[i] = palettes[i].SubPaletteId;
                    offsets[i] = palettes[i].Offset;
                }
            }
            return palettes.Count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectPalettes), error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetNumContainedItems(uint containerId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return -1;
        try { return table._view.TryCountContents(containerId, out int items, out _) ? items : -1; }
        catch (Exception error)
        {
            table.Fail(nameof(GetNumContainedItems), error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetNumContainedContainers(uint containerId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return -1;
        try { return table._view.TryCountContents(containerId, out _, out int containers) ? containers : -1; }
        catch (Exception error)
        {
            table.Fail(nameof(GetNumContainedContainers), error);
            return -1;
        }
    }
}
```

Add `BindLootSlots();` after `BindNav3DSlots();` in the constructor.

- [ ] **Step 6: Implement the runtime view** (`RuntimeRynthGameView.cs`, after the door members):

```csharp
public bool PickupItem(uint objectId, uint preferredContainerId) =>
    (preferredContainerId == 0u
        ? _automation.Loot.Pickup(objectId)
        : _automation.Loot.Pickup(objectId, preferredContainerId)).Accepted;

public bool GiveItem(uint objectId, uint targetId, uint amount) =>
    _automation.Items.Give(objectId, targetId, amount).Accepted;

public bool CloseContainer(uint containerId) => _automation.Loot.Close(containerId).Accepted;

// The raw ObjDesc swaps, as RynthCore captures them at CreateObject: id and offset only.
public IReadOnlyList<(uint SubPaletteId, uint Offset)>? GetPalettes(uint objectId)
{
    if (!_runtime.EntityObjects.Entities.TryGetActive(objectId, out RuntimeEntityRecord record))
        return _runtime.InventoryOwner.Objects.Get(objectId) is null ? null : [];
    return [.. record.Snapshot.SubPalettes
        .Select(p => ((uint)p.SubPaletteId, (uint)p.Offset))
        .OrderBy(p => p.Item2)];
}

public bool TryCountContents(uint containerId, out int items, out int containers)
{
    items = containers = 0;
    ClientObjectTable objects = _runtime.InventoryOwner.Objects;
    if (objects.Get(containerId) is null)
        return false;
    foreach (uint id in objects.GetContents(containerId))
    {
        if (objects.Get(id) is { ItemsCapacity: > 0 })
            containers++;
        else
            items++;
    }
    return true;
}
```

Check the names against the code: `record.Snapshot.SubPalettes` element type (its id property is `SubPaletteId` and `Offset` per `RuntimeAutomationSurface.ProjectPalettes`), and that `GetContents` leaves out worn items (it does for the player — `GetContainerContents` at `RuntimeRynthGameView.cs:122` adds `GetEquippedBy` separately). Add `using System.Linq;` only if the file's implicit usings lack it.

- [ ] **Step 7: Run the tests** (Verify). Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): loot into a pack, give, close, palettes and contained counts

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```

---

### Task 4: Salvage panel slots

**Goal:** RynthAi's open / add / execute salvage flow and `SalvageDirect` send one salvage request through `Items.Salvage`.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthSalvagePanel.cs`
- Create: `src/AcDream.Rynth.Host/RynthApiTable.Salvage.cs`
- Modify: `src/AcDream.Rynth.Host/IRynthGameView.cs`, `RuntimeRynthGameView.cs` (three members)
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.cs` (`internal RynthSalvagePanel Salvage { get; }`, create it, `BindSalvageSlots()`)
- Modify: `src/AcDream.Rynth.Host/RynthEventBridge.cs` or `RynthHost.cs` (reset the panel on logout — see Step 5)
- Modify: `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthSalvagePanelTests.cs`

**Acceptance Criteria:**
- [ ] Open refuses an id that is not a tinkering tool the player owns; accepts one and empties the list.
- [ ] AddItem refuses before Open, refuses item 0, the tool and items the player does not own; a repeat add answers true and is listed once; the 101st item is refused.
- [ ] Execute with an empty list answers false and sends nothing; otherwise sends `Salvage(tool, ids)` once in add order and empties the list (the tool stays, so another round can follow).
- [ ] Logout closes the panel.
- [ ] `SalvageDirect(tool, ids)` sends once for 1..100 ids with none 0 and none the tool; otherwise 0.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass

**Steps:**

- [ ] **Step 1: View members** (`IRynthGameView.cs`):

```csharp
/// <summary>True when the object is a tinkering tool (an Ust) the player owns.</summary>
bool IsOwnedTinkeringTool(uint objectId);

/// <summary>True when the player owns the item (in a pack or worn).</summary>
bool IsOwnedItem(uint objectId);

/// <summary>Salvages the items with the tool in one request. True when sent.</summary>
bool Salvage(uint toolId, IReadOnlyList<uint> itemIds);
```

Fake:

```csharp
public HashSet<uint> Tools { get; } = new();
public HashSet<uint> Owned { get; } = new();
public bool IsOwnedTinkeringTool(uint objectId) => Read(Tools).Contains(objectId);
public bool IsOwnedItem(uint objectId) => Read(Owned).Contains(objectId);
public bool Salvage(uint toolId, IReadOnlyList<uint> itemIds) =>
    Act($"salvage {toolId:X8} {string.Join(",", itemIds.Select(id => id.ToString("X8")))}");
```

Runtime (`RuntimeRynthGameView.cs`):

```csharp
public bool IsOwnedTinkeringTool(uint objectId) =>
    IsOwnedItem(objectId)
    && _runtime.InventoryOwner.Objects.Get(objectId) is { } tool
    && (tool.Type & ItemType.TinkeringTool) != 0;

public bool IsOwnedItem(uint objectId) =>
    objectId != 0
    && _runtime.InventoryOwner.Objects.Get(objectId) is { } item
    && RuntimeWorldObjectProjection.IsPlayerOwned(item, PlayerId, _runtime.InventoryOwner.Objects);

public bool Salvage(uint toolId, IReadOnlyList<uint> itemIds) =>
    _automation.Items.Salvage(toolId, itemIds).Accepted;
```

`RuntimeWorldObjectProjection.IsPlayerOwned` (`src/AcDream.Runtime/Gameplay/RuntimeWorldObjectProjection.cs:126`) is the rule the surface's own `TryGetOwned` uses, so the panel accepts exactly what `Items.Salvage` will. `ItemType` is `AcDream.Core.Items.ItemType` (as `Salvage` in the surface uses it).

- [ ] **Step 2: Write the failing tests** (`RynthSalvagePanelTests.cs`):

```csharp
using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthSalvagePanelTests
{
    private const uint Ust = 0x80000050, A = 0x80000101, B = 0x80000102, Foreign = 0x80000999;
    private readonly List<string> _log = new();

    private static FakeGameView View()
    {
        var view = new FakeGameView { PlayerId = 0x50000001 };
        view.Tools.Add(Ust);
        view.Owned.UnionWith(new[] { Ust, A, B });
        return view;
    }

    [Fact]
    public void OpenAddExecuteSendsOneSalvageAndKeepsTheTool()
    {
        FakeGameView view = View();
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.HasSalvagePanel);
        Assert.False(host.SalvagePanelAddItem(A));     // not open yet
        Assert.False(host.SalvagePanelOpen(A));        // not a tool
        Assert.True(host.SalvagePanelOpen(Ust));
        Assert.True(host.SalvagePanelAddItem(A));
        Assert.True(host.SalvagePanelAddItem(A));      // repeat: listed once
        Assert.False(host.SalvagePanelAddItem(Ust));   // the tool itself
        Assert.False(host.SalvagePanelAddItem(Foreign));
        Assert.False(host.SalvagePanelAddItem(0));
        Assert.True(host.SalvagePanelAddItem(B));
        Assert.True(host.SalvagePanelExecute());
        Assert.False(host.SalvagePanelExecute());      // list emptied
        Assert.True(host.SalvagePanelAddItem(A));      // tool kept: next round
        Assert.True(host.SalvagePanelExecute());

        Assert.Equal(new[] { "salvage 80000050 80000101,80000102", "salvage 80000050 80000101" }, view.Actions);
    }

    [Fact]
    public void TheListHoldsAtMostOneHundredItems()
    {
        FakeGameView view = View();
        for (uint i = 0; i < 101; i++)
            view.Owned.Add(0x80010000 + i);
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.SalvagePanelOpen(Ust));
        for (uint i = 0; i < 100; i++)
            Assert.True(host.SalvagePanelAddItem(0x80010000 + i));
        Assert.False(host.SalvagePanelAddItem(0x80010000 + 100));
    }

    [Fact]
    public void LogoutClosesThePanel()
    {
        FakeGameView view = View();
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.SalvagePanelOpen(Ust));
        Assert.True(host.SalvagePanelAddItem(A));
        table.Salvage.Close();
        Assert.False(host.SalvagePanelExecute());
        Assert.False(host.SalvagePanelAddItem(A));
    }

    [Fact]
    public void SalvageDirectFollowsRynthCoresRules()
    {
        FakeGameView view = View();
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.HasSalvageDirect);
        Assert.Equal(1, host.SalvageDirect(Ust, new[] { A, B }));
        Assert.Equal(0, host.SalvageDirect(Ust, new[] { A, 0u }));
        Assert.Equal(0, host.SalvageDirect(Ust, new[] { A, Ust }));
        Assert.Equal(0, host.SalvageDirect(0, new[] { A }));
        Assert.Equal(new[] { "salvage 80000050 80000101,80000102" }, view.Actions);
    }
}
```

- [ ] **Step 3: Run them to see them fail.**

- [ ] **Step 4: Implement** `RynthSalvagePanel.cs`:

```csharp
namespace AcDream.Rynth.Host;

/// <summary>
/// RynthCore's salvage panel, kept by the host: the tool it was opened with and the
/// items added since. OpenAC's own salvage window is not involved; Execute sends the
/// list in one salvage request, as the panel's Salvage button does.
/// </summary>
internal sealed class RynthSalvagePanel
{
    /// <summary>The most ids one salvage request takes (RynthCore's MaxSalvageDirectItems).</summary>
    internal const int MaxItems = 100;

    private readonly IRynthGameView _view;
    private readonly List<uint> _items = [];
    private uint _toolId;

    public RynthSalvagePanel(IRynthGameView view) => _view = view;

    public bool Open(uint toolId)
    {
        if (toolId == 0 || !_view.IsOwnedTinkeringTool(toolId))
            return false;
        _toolId = toolId;
        _items.Clear();
        return true;
    }

    public bool Add(uint itemId)
    {
        if (_toolId == 0 || itemId == 0 || itemId == _toolId)
            return false;
        if (_items.Contains(itemId))
            return true;
        if (_items.Count >= MaxItems || !_view.IsOwnedItem(itemId))
            return false;
        _items.Add(itemId);
        return true;
    }

    public bool Execute()
    {
        if (_toolId == 0 || _items.Count == 0 || !_view.Salvage(_toolId, _items.ToArray()))
            return false;
        _items.Clear();
        return true;
    }

    /// <summary>Closes the panel (logout).</summary>
    public void Close()
    {
        _toolId = 0;
        _items.Clear();
    }
}
```

`RynthApiTable.Salvage.cs` — bind `SalvagePanelOpenFn` (`<uint, int>`), `SalvagePanelAddItemFn` (`<uint, int>`), `SalvagePanelExecuteFn` (`<int>`), `SalvageDirectFn` (`<uint, uint*, int, int>`). The first three are `table.Salvage.Open(id) ? 1 : 0` etc. inside the usual try/catch. `SalvageDirect`:

```csharp
[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
private static int SalvageDirect(uint toolId, uint* itemIds, int count)
{
    RynthApiTable? table = _current;
    if (table is null || toolId == 0 || itemIds is null || count <= 0 || count > RynthSalvagePanel.MaxItems)
        return 0;
    try
    {
        var ids = new uint[count];
        for (int i = 0; i < count; i++)
        {
            if (itemIds[i] == 0 || itemIds[i] == toolId)
                return 0;
            ids[i] = itemIds[i];
        }
        return table._view.Salvage(toolId, ids) ? 1 : 0;
    }
    catch (Exception error)
    {
        table.Fail(nameof(SalvageDirect), error);
        return 0;
    }
}
```

In `RynthApiTable.cs` add `internal RynthSalvagePanel Salvage { get; }` next to `Nav3D`, `Salvage = new RynthSalvagePanel(view);` in the constructor, and `BindSalvageSlots();` after `BindLootSlots();`.

- [ ] **Step 5: Close on logout.** The bridge tells plugins of logout in `RynthEventBridge.OnLeavingWorld`, but the bridge does not know the table. Add to `RynthHost`'s constructor, after `Bridge = new RynthEventBridge(...)`: `Bridge.LoggedOut += _table.Salvage.Close;`, and add to the bridge `public event Action? LoggedOut;` raised at the end of `OnLeavingWorld` (after the plugins' `OnLogout`, inside its own try/catch that logs through `_log`, the same way other deliveries guard). Add a bridge test in `RynthEventQueueTests` (or wherever `OnLeavingWorld` is tested) that `LoggedOut` fires once per `OnLeavingWorld`.

- [ ] **Step 6: Run the tests** (Verify). Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): RynthCore's salvage panel and SalvageDirect over one salvage request

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```

---

### Task 5: Vendor slots and vendor events

**Goal:** RynthAi's AutoVendor reads the open vendor, buys and sells through RynthCore's request/status model, and hears `OnVendorOpen` / `OnVendorClose`.

**Files:**
- Modify: `src/AcDream.Rynth.Host/RynthStateTypes.cs` (vendor records)
- Modify: `src/AcDream.Rynth.Host/IRynthGameView.cs`, `RuntimeRynthGameView.cs` (vendor members)
- Create: `src/AcDream.Rynth.Host/RynthVendorTrades.cs`
- Create: `src/AcDream.Rynth.Host/RynthApiTable.Vendor.cs`
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.cs` (`internal RynthVendorTrades Vendor { get; }`, `BindVendorSlots()`, an ANSI writer if none exists — see Step 4)
- Modify: `src/AcDream.Rynth.Host/RynthPlugin.cs` (`OnVendorOpen`, `OnVendorClose`)
- Modify: `src/AcDream.Rynth.Host/RynthEventBridge.cs` (`EventKind.VendorOpen`, `EventKind.VendorClose` after `StopViewingContents`; `OnVendorChanged`)
- Modify: `src/AcDream.Rynth.Host/RynthHost.cs` (tick the vendor tracker; `OnVendorChanged` that bumps the generation and queues the events)
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs` (subscribe `InventoryOwner.Vendor.Changed`)
- Modify: `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthVendorTradesTests.cs`, `RynthEventQueueTests.cs`

**Acceptance Criteria:**
- [ ] `TryGetVendorInfo` reads the view's vendor (id, name, generation, item types, min/max, deals magic, buy/sell rate, alt currency wcid/name/server count/have, coins, item count) with Flags 1 always and 2 while a request is in flight; false with no vendor.
- [ ] `GetVendorItems` returns every listing with Amount -1 for unlimited stock, unit price, flags 1/2/4; returns empty with no vendor.
- [ ] `VendorBuy` refuses (returns 0, status Refused with the reason) for: no vendor, another vendor's id, 0 or >100 lines, a repeated id, an id not on the list, amount <1 or >0x7FFFFF, more than limited stock; otherwise calls the view's buy and returns a non-zero request id with status Sent.
- [ ] `VendorSell` refuses id 0, the vendor's own listings or the vendor, and repeats; otherwise sends.
- [ ] A view refusal (busy, surface says no) returns 0 and status Refused with the view's notice.
- [ ] Status after a transaction outcome: success → Done + Accepted; failure → Done + AnsweredNoRefresh with the notice; no outcome within 10 s → Done + TimedOut. A second request while one is in flight returns 0 and leaves the waiting request's status as it was.
- [ ] Vendor changes raise, in RynthCore's drain order (after StopViewingObjectContents, before UpdateObjectInventory): Opened → open (after a close of the previous vendor when it replaced one); Refreshed → open; Closed / Reset → close. Opened and Refreshed bump the info Generation. Reset clears an in-flight request.
- [ ] `HasVendorTrade` is true.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` and `~/.dotnet/dotnet build src/AcDream.App` → all pass, build clean

**Steps:**

- [ ] **Step 1: Host records** (`RynthStateTypes.cs`):

```csharp
/// <summary>The open vendor as RynthCore's GetVendorInfo reports it; -1 for a count the client cannot tell.</summary>
public sealed record RynthVendor(
    uint VendorId,
    string Name,
    uint ItemTypes,
    int MinValue,
    int MaxValue,
    bool DealsMagic,
    float BuyRate,
    float SellRate,
    uint AltCurrencyWcid,
    string AltCurrencyName,
    int AltCurrencyServerCount,
    int AltCurrencyHave,
    int PlayerCoins,
    IReadOnlyList<RynthVendorItem> Items);

/// <summary>One vendor listing as RynthCore's GetVendorItems reports it; Amount -1 = unlimited.</summary>
public readonly record struct RynthVendorItem(
    uint ObjectId, uint Wcid, string Name, uint ItemType, uint IconId, int Amount, int StackSize,
    int MaxStackSize, int Value, int UnitValue, int UnitPrice, int Burden, bool NeedsContainerSlot)
{
    public bool Unlimited => Amount < 0;
    public bool Stackable => MaxStackSize > 0;
}

/// <summary>The server's answer to the last buy or sell.</summary>
public readonly record struct RynthVendorOutcome(bool Success, string? Notice);
```

- [ ] **Step 2: View members** (`IRynthGameView.cs`):

```csharp
/// <summary>The open vendor; null when none is open.</summary>
RynthVendor? Vendor { get; }

/// <summary>Buys the given listings in one request. False (with the reason) when the client would not send it.</summary>
bool VendorBuy(IReadOnlyList<(uint ObjectId, int Amount)> items, out string? notice);

/// <summary>Sells the given owned items, whole stacks, in one request. False (with the reason) when the client would not send it.</summary>
bool VendorSell(IReadOnlyList<uint> itemIds, out string? notice);

/// <summary>Raised on the game thread with the server's answer to a buy or sell the view sent.</summary>
event Action<RynthVendorOutcome>? VendorTransactionCompleted;
```

Fake (`FakeGameView.cs`):

```csharp
public RynthVendor? OpenVendor { get; set; }
public string? VendorNotice { get; set; }
public RynthVendor? Vendor => Read(OpenVendor);
public bool VendorBuy(IReadOnlyList<(uint ObjectId, int Amount)> items, out string? notice)
{
    notice = VendorNotice;
    return Act($"buy {string.Join(",", items.Select(i => $"{i.ObjectId:X8}x{i.Amount}"))}");
}
public bool VendorSell(IReadOnlyList<uint> itemIds, out string? notice)
{
    notice = VendorNotice;
    return Act($"sell {string.Join(",", itemIds.Select(id => id.ToString("X8")))}");
}
public event Action<RynthVendorOutcome>? VendorTransactionCompleted;
public void CompleteVendorTransaction(bool success, string? notice = null) =>
    VendorTransactionCompleted?.Invoke(new RynthVendorOutcome(success, notice));
```

- [ ] **Step 3: Write the failing tests** (`RynthVendorTradesTests.cs`):

```csharp
using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthVendorTradesTests
{
    private const uint VendorId = 0x80000700, Sword = 0x80000701, Gems = 0x80000702, Mine = 0x80000101;
    private readonly List<string> _log = new();

    private static RynthVendor Shop() => new(
        VendorId, "Thorsten the Weaponsmith", 0x1u, 0, 10_000, true, 0.9f, 1.3f,
        0, "", 0, -1, 1_234,
        [
            new(Sword, 30, "Sword", 1, 0x06001234, Amount: 5, StackSize: 1, MaxStackSize: 0, Value: 100, UnitValue: 100,
                UnitPrice: 130, Burden: 200, NeedsContainerSlot: false),
            new(Gems, 31, "Gem", 0x800, 0x06005678, Amount: -1, StackSize: 1, MaxStackSize: 100, Value: 10, UnitValue: 10,
                UnitPrice: 13, Burden: 5, NeedsContainerSlot: false),
        ]);

    [Fact]
    public void InfoAndItemsReadTheOpenVendor()
    {
        var view = new FakeGameView { PlayerId = 0x50000001, OpenVendor = Shop() };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);
        table.Vendor.NoteOpened();

        Assert.True(host.HasVendorTrade);
        Assert.True(host.TryGetVendorInfo(out VendorInfo info));
        Assert.Equal(VendorId, info.VendorId);
        Assert.Equal("Thorsten the Weaponsmith", info.Name);
        Assert.Equal(1u, info.Generation);
        Assert.Equal(1.3f, info.SellRate);
        Assert.Equal(1_234, info.PlayerCoins);
        Assert.Equal(2, info.ItemCount);
        Assert.True(info.TradingAvailable);
        Assert.False(info.TradeInFlight);

        VendorItem[] items = host.GetVendorItems();
        Assert.Equal(2, items.Length);
        Assert.Equal((Sword, 5, 130, false, false), (items[0].ObjectId, items[0].Amount, items[0].UnitPrice, items[0].Unlimited, items[0].Stackable));
        Assert.Equal((Gems, -1, true, true), (items[1].ObjectId, items[1].Amount, items[1].Unlimited, items[1].Stackable));
        Assert.Equal("Gem", items[1].Name);

        view.OpenVendor = null;
        Assert.False(host.TryGetVendorInfo(out _));
        Assert.Empty(host.GetVendorItems());
    }

    [Fact]
    public void BuyFollowsRynthCoresChecksThenSends()
    {
        var view = new FakeGameView { PlayerId = 0x50000001, OpenVendor = Shop() };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.Equal(0u, host.VendorBuy(0x80000999, [new(Sword, 1)]));              // not the open vendor
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus refused));
        Assert.Equal(VendorTradeState.Refused, refused.State);
        Assert.Contains("not the open vendor", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0u, host.VendorBuy(0, [new(Sword, 6)]));                       // more than its stock
        Assert.Equal(0u, host.VendorBuy(0, [new(Sword, 1), new(Sword, 1)]));        // listed twice
        Assert.Equal(0u, host.VendorBuy(0, [new(0x80000999, 1)]));                  // not on the list
        Assert.Equal(0u, host.VendorBuy(0, [new(Gems, 0)]));                        // bad amount
        Assert.Empty(view.Actions);

        uint id = host.VendorBuy(0, [new(Sword, 2), new(Gems, 500)]);
        Assert.NotEqual(0u, id);
        Assert.Equal(new[] { "buy 80000701x2,80000702x500" }, view.Actions);
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus sent));
        Assert.Equal((id, VendorTradeState.Sent, true, 2), (sent.RequestId, sent.State, sent.IsBuy, sent.EntryCount));
        Assert.True(host.TryGetVendorInfo(out VendorInfo busy));
        Assert.True(busy.TradeInFlight);

        Assert.Equal(0u, host.VendorBuy(0, [new(Sword, 1)]));                       // one at a time
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus stillSent));
        Assert.Equal((id, VendorTradeState.Sent), (stillSent.RequestId, stillSent.State));
        view.CompleteVendorTransaction(success: true);
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus done));
        Assert.Equal((id, VendorTradeState.Done, VendorTradeResult.Accepted), (done.RequestId, done.State, done.Result));
        Assert.True(done.Succeeded);
    }

    [Fact]
    public void SellRefusesVendorItemsAndRepeatsAndReportsAFailedAnswer()
    {
        var view = new FakeGameView { PlayerId = 0x50000001, OpenVendor = Shop() };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.Equal(0u, host.VendorSell(0, [Sword]));
        Assert.Equal(0u, host.VendorSell(0, [VendorId]));
        Assert.Equal(0u, host.VendorSell(0, [Mine, Mine]));
        Assert.Equal(0u, host.VendorSell(0, [0u]));
        uint id = host.VendorSell(0, [Mine]);
        Assert.NotEqual(0u, id);
        view.CompleteVendorTransaction(success: false, notice: "Vendor transaction failed.");
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus status));
        Assert.Equal((VendorTradeState.Done, VendorTradeResult.AnsweredNoRefresh), (status.State, status.Result));
        Assert.Equal("Vendor transaction failed.", status.Message);
        Assert.Equal(new[] { "sell 80000101" }, view.Actions);
    }

    [Fact]
    public void AViewRefusalAndATimeoutAreReported()
    {
        var view = new FakeGameView { PlayerId = 0x50000001, OpenVendor = Shop(), ActionAnswer = false, VendorNotice = "busy" };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);
        long now = 1_000;
        table.Vendor.Clock = () => now;

        Assert.Equal(0u, host.VendorBuy(0, [new(Sword, 1)]));
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus refused));
        Assert.Equal((VendorTradeState.Refused, "busy"), (refused.State, refused.Message));

        view.ActionAnswer = true;
        uint id = host.VendorBuy(0, [new(Sword, 1)]);
        now += 9_999;
        table.Vendor.Tick();
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus waiting));
        Assert.Equal(VendorTradeState.Sent, waiting.State);
        now += 1;
        table.Vendor.Tick();
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus timedOut));
        Assert.Equal((id, VendorTradeState.Done, VendorTradeResult.TimedOut), (timedOut.RequestId, timedOut.State, timedOut.Result));
    }

    [Fact]
    public void NoStatusBeforeTheFirstRequest()
    {
        using var table = new RynthApiTable(new FakeGameView(), _log.Add);
        var host = new RynthCoreHost(*table.Native);
        Assert.False(host.TryGetVendorTradeStatus(out _));
    }
}
```

And in `RynthEventQueueTests` (follow how it builds a host with a recording plugin export type and checks delivery order; add `RynthPluginOnVendorOpen` / `RynthPluginOnVendorClose` exports to its recording plugin):

```csharp
[Fact]
public void VendorEventsArriveInRynthCoresDrainOrder()
{
    // arrange as the other drain-order tests
    host.Bridge.OnContainerContentsChanged(0x80000100);   // UpdateInventory
    host.OnVendorChanged(RynthVendorChange.Opened, previousVendorId: 0x80000600, vendorId: 0x80000700);
    host.Bridge.OnContainerClosed(0x80000100);              // StopViewingContents
    host.Tick();
    // expected order: stop-viewing 80000100, vendor-close 80000600, vendor-open 80000700, update-inventory 80000100
}

[Fact]
public void RefreshOpensAgainAndResetCloses()
{
    // Refreshed(0x80000700) -> vendor-open 80000700; Reset(prev 0x80000700) -> vendor-close 80000700;
    // Opened with previous 0 raises no close. Generation goes 1, 2 for Opened then Refreshed.
}
```

- [ ] **Step 4: Run them to see them fail.**

- [ ] **Step 5: Implement the tracker** (`RynthVendorTrades.cs`):

```csharp
namespace AcDream.Rynth.Host;

/// <summary>
/// RynthCore's vendor trade requests (VendorTrade.cs): checks a buy or sell against
/// the open vendor, sends it through the view, and keeps the status of the most recent
/// request: Sent, then Done with Accepted / AnsweredNoRefresh / TimedOut, or Refused
/// for one that never went out. One request is in flight at a time.
/// </summary>
internal sealed class RynthVendorTrades : IDisposable
{
    internal const int MaxLines = 100;
    internal const int MaxAmount = 0x7FFFFF;
    internal const long TimeoutMs = 10_000;

    internal const int StateSent = 2, StateDone = 3, StateRefused = 4;
    internal const int ResultAccepted = 1, ResultAnsweredNoRefresh = 2, ResultTimedOut = 3;

    private readonly IRynthGameView _view;
    private uint _sequence;
    private long _sentAt;
    private bool _inFlight;

    public RynthVendorTrades(IRynthGameView view)
    {
        _view = view;
        _view.VendorTransactionCompleted += OnCompleted;
    }

    /// <summary>Milliseconds; tests replace it.</summary>
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    /// <summary>+1 for every vendor list the server sent (open or refresh).</summary>
    public uint Generation { get; private set; }

    public bool InFlight => _inFlight;

    /// <summary>The latest request's status; RequestId 0 before the first.</summary>
    public (uint RequestId, int State, int Result, bool IsBuy, uint VendorId, int EntryCount, string Message) Status { get; private set; }

    public void NoteOpened() => Generation = unchecked(Generation + 1u);

    /// <summary>Session teardown: nothing is in flight any more.</summary>
    public void Reset() => _inFlight = false;

    public uint Buy(uint vendorId, IReadOnlyList<(uint ObjectId, int Amount)> lines)
    {
        // One at a time, as RynthCore's single in-flight slot: the waiting request keeps its status.
        if (_inFlight)
            return 0;
        uint seq = NextSequence();
        RynthVendor? vendor = _view.Vendor;
        if (Check(vendor, vendorId, lines.Count) is { } error)
            return Refuse(seq, true, vendorId, lines.Count, error);
        for (int i = 0; i < lines.Count; i++)
        {
            (uint id, int amount) = lines[i];
            for (int j = 0; j < i; j++)
                if (lines[j].ObjectId == id)
                    return Refuse(seq, true, vendor!.VendorId, lines.Count, $"0x{id:X8} listed twice");
            RynthVendorItem? item = Find(vendor!, id);
            if (item is null)
                return Refuse(seq, true, vendor.VendorId, lines.Count, $"0x{id:X8} is not on this vendor's list");
            if (amount < 1 || amount > MaxAmount)
                return Refuse(seq, true, vendor.VendorId, lines.Count, $"bad amount {amount} for 0x{id:X8}");
            if (!item.Value.Unlimited && amount > item.Value.Amount)
                return Refuse(seq, true, vendor.VendorId, lines.Count,
                    $"vendor only has {item.Value.Amount} of 0x{id:X8}, asked for {amount}");
        }
        return _view.VendorBuy(lines, out string? notice)
            ? Sent(seq, true, vendor!.VendorId, lines.Count)
            : Refuse(seq, true, vendor!.VendorId, lines.Count, notice ?? "the client would not send it");
    }

    public uint Sell(uint vendorId, IReadOnlyList<uint> ids)
    {
        if (_inFlight)
            return 0;
        uint seq = NextSequence();
        RynthVendor? vendor = _view.Vendor;
        if (Check(vendor, vendorId, ids.Count) is { } error)
            return Refuse(seq, false, vendorId, ids.Count, error);
        for (int i = 0; i < ids.Count; i++)
        {
            uint id = ids[i];
            if (id == 0)
                return Refuse(seq, false, vendor!.VendorId, ids.Count, "item id 0");
            if (id == vendor!.VendorId || Find(vendor, id) is not null)
                return Refuse(seq, false, vendor.VendorId, ids.Count, $"0x{id:X8} belongs to the vendor");
            for (int j = 0; j < i; j++)
                if (ids[j] == id)
                    return Refuse(seq, false, vendor.VendorId, ids.Count, $"0x{id:X8} listed twice");
        }
        return _view.VendorSell(ids, out string? notice)
            ? Sent(seq, false, vendor!.VendorId, ids.Count)
            : Refuse(seq, false, vendor!.VendorId, ids.Count, notice ?? "the client would not send it");
    }

    /// <summary>Times out a request the server never answered. Called once per host tick.</summary>
    public void Tick()
    {
        if (_inFlight && Clock() - _sentAt >= TimeoutMs)
            Finish(ResultTimedOut, "no answer from the server within 10 s");
    }

    public void Dispose() => _view.VendorTransactionCompleted -= OnCompleted;

    private void OnCompleted(RynthVendorOutcome outcome)
    {
        if (!_inFlight)
            return;
        if (outcome.Success)
            Finish(ResultAccepted, "server accepted");
        else
            Finish(ResultAnsweredNoRefresh, outcome.Notice ?? "server refused");
    }

    private static string? Check(RynthVendor? vendor, uint vendorId, int count)
    {
        if (vendor is null)
            return "no vendor open";
        if (vendorId != 0 && vendorId != vendor.VendorId)
            return $"vendor 0x{vendorId:X8} is not the open vendor (0x{vendor.VendorId:X8})";
        if (count < 1)
            return "nothing to trade";
        if (count > MaxLines)
            return $"too many lines ({count} > {MaxLines}); split into batches";
        return null;
    }

    private static RynthVendorItem? Find(RynthVendor vendor, uint id)
    {
        foreach (RynthVendorItem item in vendor.Items)
            if (item.ObjectId == id)
                return item;
        return null;
    }

    private uint NextSequence()
    {
        _sequence = unchecked(_sequence + 1u);
        if (_sequence == 0)
            _sequence = 1;
        return _sequence;
    }

    private uint Sent(uint seq, bool isBuy, uint vendorId, int count)
    {
        _inFlight = true;
        _sentAt = Clock();
        Status = (seq, StateSent, 0, isBuy, vendorId, count, "sent, waiting for the server");
        return seq;
    }

    private uint Refuse(uint seq, bool isBuy, uint vendorId, int count, string reason)
    {
        Status = (seq, StateRefused, 0, isBuy, vendorId, count, reason);
        return 0;
    }

    private void Finish(int result, string message)
    {
        _inFlight = false;
        Status = Status with { State = StateDone, Result = result, Message = message };
    }
}
```

- [ ] **Step 6: Table partial** (`RynthApiTable.Vendor.cs`): bind `GetVendorInfoFn` (`<VendorInfoNative*, int>`), `GetVendorItemsFn` (`<VendorItemNative*, int, int>`), `VendorBuyFn` (`<uint, VendorTradeEntryNative*, int, uint>`), `VendorSellFn` (`<uint, uint*, int, uint>`), `GetVendorTradeStatusFn` (`<VendorTradeStatusNative*, int>`). The native types are in `RynthCore.PluginSdk`.

```csharp
[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
private static int GetVendorInfo(VendorInfoNative* info)
{
    RynthApiTable? table = _current;
    if (table is null || info is null)
        return 0;
    try
    {
        *info = default;
        if (table._view.Vendor is not { } vendor)
            return 0;
        info->VendorId = vendor.VendorId;
        info->Generation = table.Vendor.Generation;
        info->ItemTypes = vendor.ItemTypes;
        info->MinValue = vendor.MinValue;
        info->MaxValue = vendor.MaxValue;
        info->DealsMagic = vendor.DealsMagic ? 1 : 0;
        info->BuyRate = vendor.BuyRate;
        info->SellRate = vendor.SellRate;
        info->AltCurrencyWcid = vendor.AltCurrencyWcid;
        info->AltCurrencyServerCount = vendor.AltCurrencyServerCount;
        info->AltCurrencyHave = vendor.AltCurrencyHave;
        info->PlayerCoins = vendor.PlayerCoins;
        info->ItemCount = vendor.Items.Count;
        info->Flags = 1u | (table.Vendor.InFlight ? 2u : 0u);
        WriteAnsi(info->Name, 64, vendor.Name);
        WriteAnsi(info->AltCurrencyName, 64, vendor.AltCurrencyName);
        return 1;
    }
    catch (Exception error)
    {
        table.Fail(nameof(GetVendorInfo), error);
        return 0;
    }
}
```

`GetVendorItems(VendorItemNative* items, int max)`: -1 with no vendor; fill `min(max, count)` entries (`*d = default;` then the fields, `Amount = item.Unlimited ? -1 : item.Amount`, `Flags = (Unlimited ? 1 : 0) | (Stackable ? 2 : 0) | (NeedsContainerSlot ? 4 : 0)`, `WriteAnsi(d->Name, 64, item.Name)`); return the total. `VendorBuy(uint vendorId, VendorTradeEntryNative* entries, int count)`: 0 for a null pointer with count > 0; copy into a `(uint, int)[]` (count clamped at 0; a count over 100 still goes to `Buy` so it records the refusal — copy at most 101 entries) and `return table.Vendor.Buy(vendorId, lines);`. `VendorSell` the same with `uint*`. `GetVendorTradeStatus(VendorTradeStatusNative* status)`: `*status = default;` return 0 when `Status.RequestId == 0`, else copy the tuple's fields (`IsBuy ? 1 : 0`, `Estimate` 0) and `WriteAnsi(status->Message, 128, Message)`, return 1.

`WriteAnsi`: check `RynthApiTable.cs` / `RynthTranslate.cs` for an existing fixed-buffer string writer; if none, add to `RynthApiTable.Vendor.cs`:

```csharp
// RynthCore's WriteAnsi: Latin-1 bytes, cut to fit, always zero-terminated.
private static void WriteAnsi(byte* buffer, int size, string? text)
{
    int n = 0;
    if (!string.IsNullOrEmpty(text))
        for (; n < text.Length && n < size - 1; n++)
            buffer[n] = text[n] <= 0xFF ? (byte)text[n] : (byte)'?';
    buffer[n] = 0;
}
```

In `RynthApiTable.cs`: `internal RynthVendorTrades Vendor { get; }`, created in the constructor (`Vendor = new RynthVendorTrades(view);`), disposed in `Dispose()` before `NativeMemory.Free`, and `BindVendorSlots();` after `BindSalvageSlots();`.

- [ ] **Step 7: Events.** `RynthPlugin.cs`: `public void OnVendorOpen(uint vendorId) => CallObject("RynthPluginOnVendorOpen", vendorId);` and the same for close. `RynthEventBridge.cs`: insert `VendorOpen, VendorClose,` after `StopViewingContents` in `EventKind`, and:

```csharp
/// <summary>A vendor's list arrived (RynthCore raises it for every OpenVendor, refreshes included).</summary>
public void OnVendorOpened(uint vendorId) => Enqueue(EventKind.VendorOpen, () =>
{
    foreach (RynthPlugin plugin in _plugins)
        plugin.OnVendorOpen(vendorId);
});

public void OnVendorClosed(uint vendorId) => Enqueue(EventKind.VendorClose, () =>
{
    foreach (RynthPlugin plugin in _plugins)
        plugin.OnVendorClose(vendorId);
});
```

(Check `KindCount` still uses the last enum member.) `RynthHost.cs`: add a host-side enum next to the class or in `RynthStateTypes.cs`, `public enum RynthVendorChange { Opened, Refreshed, Closed, Reset }`, and:

```csharp
/// <summary>The client's vendor state changed: RynthCore's vendor events, and the vendor list generation.</summary>
public void OnVendorChanged(RynthVendorChange change, uint previousVendorId, uint vendorId)
{
    switch (change)
    {
        case RynthVendorChange.Opened:
            if (previousVendorId != 0 && previousVendorId != vendorId)
                Bridge.OnVendorClosed(previousVendorId);
            _table.Vendor.NoteOpened();
            Bridge.OnVendorOpened(vendorId);
            break;
        case RynthVendorChange.Refreshed:
            _table.Vendor.NoteOpened();
            Bridge.OnVendorOpened(vendorId);
            break;
        case RynthVendorChange.Closed:
            if (previousVendorId != 0)
                Bridge.OnVendorClosed(previousVendorId);
            break;
        case RynthVendorChange.Reset:
            _table.Vendor.Reset();
            if (previousVendorId != 0)
                Bridge.OnVendorClosed(previousVendorId);
            break;
    }
}
```

In `Tick()`, after `Bridge.DeliverQueued();`: `try { _table.Vendor.Tick(); } catch (Exception error) { _log($"vendor tick failed: {error.Message}"); }`.

Check `VendorTransition`'s fields when the vendor closes: `Closed` may carry the closed vendor in `PreviousVendorId` or in `VendorId` — read `VendorState.Close()` / `Reset()` (`src/AcDream.Core/Items/VendorState.cs:106-150`) and pass whichever holds the vendor that closed as `previousVendorId`.

`RynthClientExtension.cs`: next to `ExternalContainers.Changed += OnExternalContainerChanged;` add `_runtime.InventoryOwner.Vendor.Changed += OnVendorChanged;` (and the matching `-=` in Dispose), with:

```csharp
private void OnVendorChanged(VendorTransition transition)
{
    if (_host is not { } host)
        return;
    RynthVendorChange change = transition.Kind switch
    {
        VendorStateTransitionKind.Opened => RynthVendorChange.Opened,
        VendorStateTransitionKind.Refreshed => RynthVendorChange.Refreshed,
        VendorStateTransitionKind.Closed => RynthVendorChange.Closed,
        _ => RynthVendorChange.Reset,
    };
    host.OnVendorChanged(change, transition.PreviousVendorId, transition.VendorId);
}
```

(Confirm the `Vendor` property name on `InventoryOwner` — `RuntimeVendorAutomation` uses `_runtime.InventoryOwner.Vendor.Changed`.)

- [ ] **Step 8: Runtime view** (`RuntimeRynthGameView.cs`). Subscribe in the constructor: `_automation.Vendor.TransactionCompleted += t => VendorTransactionCompleted?.Invoke(new RynthVendorOutcome(t.Success, t.Notice));` (the surface polls the vendor automation every tick, `RuntimeAutomationSurface.cs:1419`, which raises `TransactionCompleted` on the game thread).

```csharp
public event Action<RynthVendorOutcome>? VendorTransactionCompleted;

public RynthVendor? Vendor
{
    get
    {
        VendorState state = _runtime.InventoryOwner.Vendor;
        if (state.VendorId == 0u)
            return null;
        VendorShopProfile p = state.Profile;
        IReadOnlyList<PluginVendorItem> priced = _automation.Vendor.Items;
        var items = new RynthVendorItem[state.Items.Count];
        for (int i = 0; i < items.Length; i++)
        {
            VendorShopItem s = state.Items[i];
            int value = s.Value ?? 0;
            int stack = Math.Max(1, s.DescStackSize ?? 1);
            items[i] = new RynthVendorItem(
                s.ItemGuid, s.WeenieClassId, s.Name ?? string.Empty, s.ItemType ?? 0u, s.IconId,
                Amount: s.StackSize <= 0 ? -1 : s.StackSize,
                StackSize: stack, MaxStackSize: s.MaxStackSize ?? 0, Value: value,
                UnitValue: stack > 1 ? value / stack : value,
                UnitPrice: i < priced.Count && priced[i].TemplateObjectId == s.ItemGuid ? priced[i].UnitPrice : 0,
                Burden: s.Burden ?? 0,
                NeedsContainerSlot: (s.ItemsCapacity ?? 0) > 0 || (s.ContainersCapacity ?? 0) > 0);
        }
        uint player = PlayerId;
        int coins = TryGetIntProperty(player, 20u, out int c) ? c : -1;
        return new RynthVendor(
            state.VendorId, _runtime.InventoryOwner.Objects.Get(state.VendorId)?.Name ?? string.Empty,
            p.MerchandiseItemTypes, ClampToInt(p.MerchandiseMinValue), ClampToInt(p.MerchandiseMaxValue),
            p.DealMagicalItems, p.BuyPrice, p.SellPrice, p.AlternateCurrencyWcid,
            p.AlternateCurrencyPluralName ?? string.Empty, ClampToInt(p.AlternateCurrencyAmount),
            p.AlternateCurrencyWcid == 0u ? -1 : CountOwned(p.AlternateCurrencyWcid),
            coins, items);
    }
}

public bool VendorBuy(IReadOnlyList<(uint ObjectId, int Amount)> items, out string? notice)
{
    IVendorAutomation vendor = _automation.Vendor;
    vendor.ClearBuyList();
    foreach ((uint id, int amount) in items)
    {
        PluginVendorCommandResult added = vendor.AddToBuyList(id, amount);
        if (!added.Accepted)
        {
            vendor.ClearBuyList();
            notice = added.Notice ?? $"0x{id:X8} could not be bought ({added.Status})";
            return false;
        }
    }
    PluginVendorCommandResult sent = vendor.BuyAll();
    notice = sent.Notice ?? (sent.Accepted ? null : sent.Status.ToString());
    return sent.Accepted;
}
```

`VendorSell` is the same with `ClearSellList` / `AddToSellList` / `SellAll`. Write the two helpers: `ClampToInt(uint v) => v > int.MaxValue ? int.MaxValue : (int)v;` (min/max use `uint.MaxValue` as "no limit", which becomes `int.MaxValue`), and `CountOwned(uint wcid)` summing `StackSize` (at least 1) of player-owned objects with that wcid — follow the owned-items walk this file already does for the player's inventory (or `RynthCore`'s `CountAltCurrency`: the player's and their packs' direct contents, not worn). `TryGetIntProperty` — use the file's existing int-property read for the player (`grep -n "Ints\b\|TryGetValue(propertyId" src/AcDream.Rynth.Host/RuntimeRynthGameView.cs`). If `AlternateCurrencyAmount` is not "how many the vendor's listing says" (RynthCore's `AltCurrencyServerCount`), keep it anyway: it is the closest field. The `Amount` rule: ACE stock 0 = unlimited (see the vendor-unlimited-stock fix); confirm how `VendorShopItem.StackSize` carries the stock (`grep -rn "unlimited" src/AcDream.Core/Items src/AcDream.App/UI | head`) and use the same test the vendor window uses.

- [ ] **Step 9: Run the tests and build** (Verify). Expected: PASS, build clean.

- [ ] **Step 10: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests src/AcDream.Runtime
git commit -m "feat(rynth): vendor info, buy/sell requests with RynthCore's status, vendor events

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```

---

### Task 6: Trade slots

**Goal:** RynthAi's AutoTrade reads the trade state and items and drives open / add / accept / decline / reset / close.

**Files:**
- Modify: `src/AcDream.Rynth.Host/RynthStateTypes.cs` (`RynthTradeState`)
- Modify: `src/AcDream.Rynth.Host/IRynthGameView.cs`, `RuntimeRynthGameView.cs`
- Create: `src/AcDream.Rynth.Host/RynthApiTable.Trade.cs`
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.cs` (`BindTradeSlots()`)
- Modify: `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthApiTableTests.cs`

**Acceptance Criteria:**
- [ ] `HasTrade` is true.
- [ ] `TryGetTradeState` reports Flags (1 open, 2 you accepted, 4 partner accepted, 8 watching, 16 actions available), Generation, Sequence, partner, initiator, both item counts and every counter from the view.
- [ ] A `Size` below 8 answers 0; a `Size` smaller than the struct writes only that many bytes and reports that size.
- [ ] `GetTradeItems(You)` / `(Partner)` return the sides; a side other than 1 or 2 answers -1.
- [ ] `TradeOpen(id)`, `TradeAdd(item, slot)`, `TradeAccept`, `TradeDecline`, `TradeReset`, `TradeClose` reach the view; 0 ids answer false.
- [ ] No trade state from the view (out of world) answers 0 from `GetTradeState`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass

**Steps:**

- [ ] **Step 1: Host record** (`RynthStateTypes.cs`):

```csharp
/// <summary>The trade window and its event counters, as RynthCore's GetTradeState reports them.</summary>
public readonly record struct RynthTradeState(
    bool IsOpen, bool YouAccepted, bool PartnerAccepted, uint PartnerId,
    IReadOnlyList<uint> YourItems, IReadOnlyList<uint> PartnerItems,
    uint Generation, uint Sequence, uint InitiatorId, uint LastEventType,
    uint FailureCount, uint LastFailureItemId, uint LastFailureReason, uint LastCloseReason,
    uint LastAcceptedBy, uint LastDeclinedBy, uint LastResetBy, uint CompletedCount, uint PartnerAcceptCount);
```

- [ ] **Step 2: View members**:

```csharp
/// <summary>The trade window and counters; null when the client cannot trade (out of world).</summary>
RynthTradeState? Trade { get; }

/// <summary>Trade actions; true when sent. Open asks the partner, Add stages an owned item, Close ends the trade.</summary>
bool TradeOpen(uint partnerId);
bool TradeAdd(uint itemId);
bool TradeAccept();
bool TradeDecline();
bool TradeReset();
bool TradeClose();
```

Fake: `public RynthTradeState? TradeState { get; set; }`, `public RynthTradeState? Trade => Read(TradeState);` and `Act($"trade open {partnerId:X8}")`, `Act($"trade add {itemId:X8}")`, `Act("trade accept")`, `Act("trade decline")`, `Act("trade reset")`, `Act("trade close")`.

Runtime:

```csharp
public RynthTradeState? Trade
{
    get
    {
        ITradeAutomation trade = _automation.Trade;
        if (!trade.IsAvailable)
            return null;
        PluginTradeEventCounters c = trade.Events;
        return new RynthTradeState(
            trade.IsOpen, trade.MyAccepted, trade.PartnerAccepted, trade.PartnerObjectId,
            trade.MyItems, trade.PartnerItems,
            c.Generation, c.Sequence, c.InitiatorId, c.LastEventType, c.FailureCount, c.LastFailureItemId,
            c.LastFailureReason, c.LastCloseReason, c.LastAcceptedBy, c.LastDeclinedBy, c.LastResetBy,
            c.CompletedCount, c.PartnerAcceptCount);
    }
}

public bool TradeOpen(uint partnerId) => _automation.Trade.Open(partnerId).Accepted;
public bool TradeAdd(uint itemId) => _automation.Trade.Add(itemId).Accepted;
public bool TradeAccept() => _automation.Trade.Accept().Accepted;
public bool TradeDecline() => _automation.Trade.Decline().Accepted;
public bool TradeReset() => _automation.Trade.Reset().Accepted;
public bool TradeClose() => _automation.Trade.End().Accepted;
```

- [ ] **Step 3: Write the failing tests** (`RynthApiTableTests.cs`):

```csharp
[Fact]
public void TradeSlots_ReportTheWindowAndCounters()
{
    var view = new FakeGameView
    {
        PlayerId = 0x50000001,
        TradeState = new RynthTradeState(
            IsOpen: true, YouAccepted: false, PartnerAccepted: true, PartnerId: 0x50000002,
            YourItems: [0x80000101], PartnerItems: [0x80000201, 0x80000202],
            Generation: 3, Sequence: 17, InitiatorId: 0x50000002, LastEventType: 0x0202,
            FailureCount: 1, LastFailureItemId: 0x80000103, LastFailureReason: 0x0426, LastCloseReason: 1,
            LastAcceptedBy: 0x50000002, LastDeclinedBy: 0, LastResetBy: 0x50000001,
            CompletedCount: 2, PartnerAcceptCount: 4),
    };
    using var table = new RynthApiTable(view, _log.Add);
    var host = new RynthCoreHost(*table.Native);

    Assert.True(host.HasTrade);
    Assert.True(host.TryGetTradeState(out TradeState state));
    Assert.True(state.IsOpen);
    Assert.False(state.YouAccepted);
    Assert.True(state.PartnerAccepted);
    Assert.True(state.Watching);
    Assert.True(state.ActionsAvailable);
    Assert.Equal((3u, 17u, 0x50000002u, 0x50000002u), (state.Generation, state.Sequence, state.PartnerId, state.InitiatorId));
    Assert.Equal((1, 2), (state.YourItemCount, state.PartnerItemCount));
    Assert.Equal((0x0202u, 1u, 0x80000103u, 0x0426u, 1u), (state.LastEventType, state.FailureCount, state.LastFailureItemId, state.LastFailureReason, state.LastCloseReason));
    Assert.Equal((0x50000002u, 0u, 0x50000001u, 2u, 4u), (state.LastAcceptedBy, state.LastDeclinedBy, state.LastResetBy, state.CompletedCount, state.PartnerAcceptCount));
    Assert.Equal(new uint[] { 0x80000101 }, host.GetTradeItems(TradeSide.You));
    Assert.Equal(new uint[] { 0x80000201, 0x80000202 }, host.GetTradeItems(TradeSide.Partner));

    var raw = new TradeStateNative { Size = 8 };
    Assert.Equal(8, ((delegate* unmanaged[Cdecl]<TradeStateNative*, int>)table.Native->GetTradeStateFn)(&raw));
    Assert.Equal(8u, raw.Size);
    Assert.Equal(0u, raw.Generation);                    // past the 8 bytes asked for
    raw = new TradeStateNative { Size = 4 };
    Assert.Equal(0, ((delegate* unmanaged[Cdecl]<TradeStateNative*, int>)table.Native->GetTradeStateFn)(&raw));
    Assert.Equal(-1, ((delegate* unmanaged[Cdecl]<int, uint*, int, int>)table.Native->GetTradeItemsFn)(3, null, 0));

    view.TradeState = null;
    Assert.False(host.TryGetTradeState(out _));
}

[Fact]
public void TradeSlots_ActionsReachTheView()
{
    var view = new FakeGameView { PlayerId = 0x50000001 };
    using var table = new RynthApiTable(view, _log.Add);
    var host = new RynthCoreHost(*table.Native);

    Assert.True(host.TradeOpen(0x50000002));
    Assert.False(host.TradeOpen(0));
    Assert.True(host.TradeAdd(0x80000101, 4));
    Assert.False(host.TradeAdd(0));
    Assert.True(host.TradeAccept());
    Assert.True(host.TradeDecline());
    Assert.True(host.TradeReset());
    Assert.True(host.TradeClose());
    Assert.Equal(
        new[] { "trade open 50000002", "trade add 80000101", "trade accept", "trade decline", "trade reset", "trade close" },
        view.Actions);
}
```

- [ ] **Step 4: Run them to see them fail.**

- [ ] **Step 5: Implement** `RynthApiTable.Trade.cs`. Bind the eight slots. `GetTradeState`:

```csharp
[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
private static int GetTradeState(TradeStateNative* state)
{
    RynthApiTable? table = _current;
    if (table is null || state is null || state->Size < 8)
        return 0;
    try
    {
        if (table._view.Trade is not { } t)
            return 0;
        uint want = state->Size;
        TradeStateNative s = default;
        // Watching and actions available: OpenAC always sees the trade events and binds every action.
        s.Flags = (t.IsOpen ? 1u : 0u) | (t.YouAccepted ? 2u : 0u) | (t.PartnerAccepted ? 4u : 0u) | 8u | 16u;
        s.Generation = t.Generation;
        s.Sequence = t.Sequence;
        s.PartnerId = t.PartnerId;
        s.InitiatorId = t.InitiatorId;
        s.SelfItemCount = t.YourItems.Count;
        s.PartnerItemCount = t.PartnerItems.Count;
        s.LastEventType = t.LastEventType;
        s.FailureCount = t.FailureCount;
        s.LastFailureItemId = t.LastFailureItemId;
        s.LastFailureReason = t.LastFailureReason;
        s.LastCloseReason = t.LastCloseReason;
        s.LastAcceptedBy = t.LastAcceptedBy;
        s.LastDeclinedBy = t.LastDeclinedBy;
        s.LastResetBy = t.LastResetBy;
        s.CompletedCount = t.CompletedCount;
        s.PartnerAcceptCount = t.PartnerAcceptCount;
        uint n = Math.Min(want, (uint)sizeof(TradeStateNative));
        s.Size = n;
        Buffer.MemoryCopy(&s, state, want, n);
        return (int)n;
    }
    catch (Exception error)
    {
        table.Fail(nameof(GetTradeState), error);
        return 0;
    }
}
```

`GetTradeItems(int side, uint* buffer, int capacity)`: -1 unless side is 1 or 2; 0 when `Trade` is null; else copy `min(capacity, count)` ids when `buffer` is not null and `capacity > 0`, return the side's count. Actions: `TradeOpen(uint id)` → `id != 0 && _view.TradeOpen(id)`; `TradeAdd(uint item, uint slot)` → `item != 0 && _view.TradeAdd(item)` (slot ignored: the server places it); the four no-argument actions call the view. All inside the usual try/catch.

The test casts `table.Native->GetTradeStateFn` directly to check the size handling; `Native` is public, so this works from the test.

- [ ] **Step 6: Run the tests** (Verify). Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): trade state, items and actions for RynthAi's AutoTrade

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```

---

### Task 7: Live check on local ACE (with the user)

**Goal:** RynthAi's loot, salvage, vendor, trade and give work against the local ACE server, checked with the user present.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- None (record results in the plan's checklist and the Rynth status memory).

**Acceptance Criteria:**
- [ ] The user said they are available before any client is launched.
- [ ] Corpse loot: with a VTank loot profile that has a colour rule, RynthAi loots a matching item from a corpse into the pack it chose (the item shows in that side pack), and closes the corpse afterwards.
- [ ] Salvage: RynthAi (or `/ra` salvage) salvages two or three items with an Ust; salvage bags appear and the items are gone.
- [ ] Vendor: at a Holtburg vendor, AutoVendor sells the profile's items and buys its restock; coins change accordingly; no "threw" lines in the log.
- [ ] Trade: between two test characters, `/ra` (or AutoTrade) opens a trade, adds an item, both accept, the item changes hands.
- [ ] Give: `/mt givep <item> <character>` hands the item over.
- [ ] Any failure is recorded with the log lines and becomes a fix task.

**Verify:** the user confirms each item in game; log captured from `~/OpenAC-dev` run output shows no `threw` lines for the new slots (`grep -E "(MoveItemExternal|GiveObjectTo|CloseContainer|Salvage|Vendor|Trade)[A-Za-z]* threw"` → no matches)

```json:metadata
{"userGate": true, "tags": ["user-gate"], "verifyCommand": "grep -E \"(MoveItemExternal|GiveObjectTo|CloseContainer|Salvage|Vendor|Trade)[A-Za-z]* threw\" <client log>", "acceptanceCriteria": ["user available before launch", "corpse loot into chosen pack + close", "UST salvage", "AutoVendor sell and buy", "player trade completes", "/mt givep"]}
```

**Steps:**

- [ ] **Step 1:** Ask the user whether they are available for the live check. Wait.
- [ ] **Step 2:** Build and launch per `local-ace-test-server` / `live-testing-rules` memories (`~/OpenAC-dev/dev-client.env`, server 10.10.20.20). Never Dreamweave.
- [ ] **Step 3:** Walk the checklist with the user, capturing the log.
- [ ] **Step 4:** Record results; open fix tasks for failures.
