# RynthCore Inventory Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The RynthAi dashboard's Inventory menu item, `/rc inventory` and the Mini Remote's Inventory section open RynthCore's own Inventory panel (Classic and Modern looks), with Use, Move, Split, Wield, Drop, Give, batches and identify-on-hover working through OpenAC.

**Architecture:** RynthCore's `InventoryFace` (all partials), their real `InventoryModel` (+ Batch, filters), `RetailSprites` and `VaultFeed` compile unchanged in `AcDream.Rynth.EngineUi`, replacing our partial model stand-in. The acclient hooks they call (`ClientObjectHooks`, `ClientHelperHooks`, `ClientActionGates`, `SmartBoxHooks`, `AppraisalHooks`, `ClientActionHooks`, `Net.InventoryOrder`, …) become stand-ins answered by a new host hookup (`RynthInventoryHookup`) over `IRynthGameView`. `IRynthGameView` gains the pending item request, attack state, an inventory revision and three item fields; the overlay icon service gains texture upload so `RetailSprites` (Classic art) works through a `DX9Backend` stand-in.

**Tech Stack:** .NET 10, C#, ImGui.NET, xunit.

**Spec:** `docs/superpowers/specs/2026-10-19-rynth-inventory-panel-design.md`

## Global Constraints

- Never edit anything under `external/RynthCore` or `external/RynthSuite`; their files are compiled as-is (memory: rynth-no-rewrite). A compile error in one of their files is fixed by adding the missing member to a stand-in, never by editing theirs.
- Build with the SDK in `~/.dotnet`: `export PATH=~/.dotnet:$PATH DOTNET_ROOT=~/.dotnet`.
- Stand-in style: same namespace and member names/signatures as RynthCore's type, a short `<summary>` saying what RynthCore does and what OpenAC does instead (see `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs`). Host delegates follow `RynthChatHookup`: created in the constructor, assigned to the stand-in, unassigned on Dispose only if still ours, each call wrapped in `Guard(name, read, fallback)`.
- RynthCore signatures to match exactly (from their sources):
  - `ClientObjectHooks.TryGetObjectIntProperty(uint objectId, uint stype, out int value)`, `TryReadItemFields(uint objectId, out ItemPwdFields f)`, `TryGetObjectAttribute(uint objectId, uint stype, int raw, out uint value)`, `CollectOwnedIdsFromSnapshot(uint containerId, Span<uint> dest)` → `int`
  - `ClientHelperHooks.UseObject(uint)`, `PutItemInContainerAt(uint objectId, uint containerId, int slot)`, `MoveItemInternal(uint objectId, uint targetContainerId, int slot, int amount)`, `SplitStackInternal(uint objectId, uint targetContainerId, int slot, int amount)`, `IsOwnedContainerFull(uint target)`, `WieldItem(uint objectId, uint equipMask)`, `DropItem(uint objectId)`, `GiveObjectTo(uint objectId, uint targetId, int amount = 0)` — all `bool`; `HasDropItem`, `HasPutItemInContainer`, `HasGiveObjectTo`, `HasWieldItem` — `bool` properties
  - `ClientActionGates.IsRequestGateAvailable` (bool), `TryReadPending(out uint type, out uint objectId, out double ageSeconds)`, `TryReadAttacking(out bool attacking)`, `RequestTypeName(uint type)`
  - `SmartBoxHooks.GetUseDoneSeq()` → int, `TryGetLastUseDone(out int seq, out uint error)`, `TryGetLastWeenieError(out int seq, out uint error, out uint eventType, out uint objectId)`
  - `AppraisalHooks.HasAppraisalData(uint guid)`, `GetObjectSpellIds(uint guid, uint[] output, int maxCount)` → int, `TryGetCachedIntProperty(uint guid, uint stype, out int)`, `TryGetCachedBoolProperty(…, out bool)`, `TryGetCachedDoubleProperty(…, out double)`
  - `ClientActionHooks.RequestId(uint objectId)` → bool
  - `AcMainThreadQueue.EnqueueSetSelectedObject(uint objectId)` → bool
  - `DX9Backend.CreateTextureRgba(IntPtr pDevice, int width, int height, ReadOnlySpan<byte> rgba)` → IntPtr, `DX9Backend.ReleaseTexture(IntPtr tex)`
  - `EngineThreads.Stopping` (bool), `ItemDragBridge.NotePopOutDrag(uint)`, `ItemDragBridge.EndPopOutDrag()`, `UiDropTargets.EngineDragObject` (uint get/set), `UiDropTargets.TryClaim(uint itemId, uint spellId, out string key)`, `IconSampling.Begin(ImDrawListPtr)`, `IconSampling.End(ImDrawListPtr)`, `ScriptIcons.TryGetItem(uint icon, uint underlay, uint overlay, uint effects, uint itemType, out IntPtr tex)` (check the exact parameter types at `InventoryFace.Classic.cs:709` against `VaultItem`'s fields), `RynthLog.Compat(string)`
- Compiled unchanged where they are pure code: `Net/IdRequester.cs` (gives `EngineSendMarker`, `IdRequester`) and `Net/ChatOrigin.cs`, as long as they compile without engine state; otherwise a stand-in with the same members.
- Their model reads `Stopwatch.GetTimestamp()` directly (no clock seam) and throttles: watch 10 Hz, capture ≥150 ms after a change / 1 s idle, target 4 Hz, hover 0.3 s, identify 1/s. Tests sleep real time between ticks (`Thread.Sleep(120)`), never fake it, and never test the 6 s / 10 s / 15 s timeouts.
- Their model keeps static state with no reset beyond a player change. Tests reset it through reflection (`InventoryModelReset.Run()`, Task 3), the way the Skills tests reset `PortalSpellTable`.
- No plugin contract bump (nothing in `AcDream.Plugin.Abstractions` changes). `IRynthGameView` and `IClientOverlayIcons` are host-internal.
- Don't run `tools/update-package-locks.ps1`.
- Test commands: `dotnet test tests/AcDream.Rynth.Host.Tests` (baseline 355/355), `dotnet test tests/AcDream.App.Tests --filter OverlayIcons` for Task 2.
- Commit message trailer: `Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz`.

**User decisions (already made):**
- Model A: compile their real `InventoryModel.cs` + `InventoryModel.Batch.cs` unchanged and delete our partial stand-in (2026-10-19: "Yes A").
- Identify on hover kept as RynthCore has it (2026-10-19: "Option A").
- Scope/entry points per spec section 1 (2026-10-19: "yes, continue to implementation plan").
- Live checks happen later, when the user says so (memory: ask-before-live-client).

---

### Task 1: Host reads for the inventory

**Goal:** `IRynthGameView` answers what RynthCore's inventory hooks read and OpenAC already knows: the pending item request (acclient's request slot), whether an attack is in progress, an inventory change counter, and each object's valid locations, useability and container slot.

**Files:**
- Modify: `src/AcDream.Core/Items/InventoryTransactionState.cs` (expose the pending request)
- Modify: `src/AcDream.Core/Items/ClientObjectTable.cs:111` (`MutationRevision` internal → public)
- Modify: `src/AcDream.Rynth.Host/RynthObjectInfo.cs`
- Modify: `src/AcDream.Rynth.Host/IRynthGameView.cs`
- Modify: `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs`
- Modify: `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthInventoryViewTests.cs` (create); Core test beside the existing `InventoryTransactionState` tests (`grep -rl "class InventoryTransactionStateTests" tests`)

**Acceptance Criteria:**
- [ ] `InventoryTransactionState.PendingRequest` returns the reserved request (kind, item) and null after it completes or fails.
- [ ] `IRynthGameView.TryGetPendingItemRequest(out uint acType, out uint objectId)` maps OpenAC's kinds to acclient's numbers: Merge 1, SplitToContainer 2, SplitToWorld 2, Move 3, Pickup 4, PutInContainer 5, DropToWorld 6, Wield 7, Give 9, Shop 10; false with zeros when none is pending.
- [ ] `IRynthGameView.IsAttackInProgress` is true while `RuntimeCombatAttackState` has `AttackRequestInProgress`, `AttackServerResponsePending` or `RepeatAttackInProgress`.
- [ ] `IRynthGameView.InventoryRevision` (ulong) is the object table's `MutationRevision`.
- [ ] `RynthObjectInfo` has `ValidLocations` (uint, from `ClientObject.ValidLocations`), `Useability` (uint, `Useability ?? 0`) and `ContainerSlot` (int, `ClientObject.ContainerSlot`, −1 unknown), filled by `RuntimeRynthGameView.TryGetObject`; existing initialisers in tests compile (the new members are `init` with defaults, not `required`).
- [ ] `FakeGameView` gains settable `PendingItemRequest` (`(uint Type, uint Object)?`), `AttackInProgress` (bool) and `Revision` (ulong) backing the new members.

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthInventoryViewTests` and the Core test → pass; full `dotnet build AcDream.slnx` has no new errors.

**Steps:**

- [ ] **Step 1: Write the failing Core test** (in the existing `InventoryTransactionState` test class; reuse its table/setup helpers)

```csharp
    [Fact]
    public void PendingRequestIsTheReservedOneUntilItEnds()
    {
        // arrange as the class's other TryReserve tests do (an item in the table)
        Assert.Null(state.PendingRequest);
        Assert.True(state.TryReserve(InventoryRequestKind.Move, ItemId, out PendingInventoryRequest pending));
        Assert.Equal((InventoryRequestKind.Move, ItemId), (state.PendingRequest!.Value.Kind, state.PendingRequest.Value.ItemId));
        // complete it the way the class's completion test does, then:
        Assert.Null(state.PendingRequest);
    }
```

- [ ] **Step 2: Implement** — in `InventoryTransactionState`, beside `HasPendingRequest`:

```csharp
    /// <summary>The one item request reserved or in flight (acclient's request slot); null when none.</summary>
    public PendingInventoryRequest? PendingRequest => _pendingRequest;
```

and make `ClientObjectTable.MutationRevision` `public`.

- [ ] **Step 3: Write the failing host tests** (`RynthInventoryViewTests.cs`): the kind mapping is a pure static so it is tested directly.

```csharp
using AcDream.Core.Items;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthInventoryViewTests
{
    [Theory]
    [InlineData(InventoryRequestKind.Merge, 1u)]
    [InlineData(InventoryRequestKind.SplitToContainer, 2u)]
    [InlineData(InventoryRequestKind.SplitToWorld, 2u)]
    [InlineData(InventoryRequestKind.Move, 3u)]
    [InlineData(InventoryRequestKind.Pickup, 4u)]
    [InlineData(InventoryRequestKind.PutInContainer, 5u)]
    [InlineData(InventoryRequestKind.DropToWorld, 6u)]
    [InlineData(InventoryRequestKind.Wield, 7u)]
    [InlineData(InventoryRequestKind.Give, 9u)]
    [InlineData(InventoryRequestKind.Shop, 10u)]
    public void OpenAcRequestKindsAreAcclientRequestTypes(InventoryRequestKind kind, uint acType) =>
        Assert.Equal(acType, RuntimeRynthGameView.AcRequestType(kind));

    [Fact]
    public void ObjectInfoCarriesSlotLocationsAndUseability()
    {
        var info = new RynthObjectInfo
        {
            ContainerId = 1, WielderId = 0, EquipLocation = 0, ItemType = 0, Wcid = 0, Bitfield = 0,
            IconId = 0, IconUnderlayId = 0, IconOverlayId = 0, SpellId = null, HouseOwnerId = null,
            StackSize = 1, StackSizeMax = 1, Value = 0, Burden = 0, ItemsCapacity = 0, Structure = 0,
            MaxStructure = 0, Workmanship = 0, MaterialType = null, Identified = false,
            AppraisedSpellIds = [], Ints = new Dictionary<uint, int>(), DataIds = new Dictionary<uint, uint>(),
            InstanceIds = new Dictionary<uint, uint>(),
        };
        Assert.Equal((0u, 0u, -1), (info.ValidLocations, info.Useability, info.ContainerSlot));
    }
}
```

- [ ] **Step 4: Implement the host side**

`RynthObjectInfo.cs` (after `MaterialType`):

```csharp
    /// <summary>The slots it can be worn in (PublicWeenieDesc ValidLocations); 0 when not wearable.</summary>
    public uint ValidLocations { get; init; }

    /// <summary>ITEM_USEABLE from PublicWeenieDesc; 0 when the client has none.</summary>
    public uint Useability { get; init; }

    /// <summary>Its place in its container, in the server's order; -1 when unknown.</summary>
    public int ContainerSlot { get; init; } = -1;
```

`IRynthGameView.cs` (after `ServerReplies`):

```csharp
    /// <summary>The one item request the client has outstanding (acclient's request slot), as acclient's request type and object; false when none.</summary>
    bool TryGetPendingItemRequest(out uint acType, out uint objectId);

    /// <summary>True while an attack request, its server answer, or a repeat attack is in progress.</summary>
    bool IsAttackInProgress { get; }

    /// <summary>Changes whenever an object is added, moved, removed or updated in the client's table.</summary>
    ulong InventoryRevision { get; }
```

`RuntimeRynthGameView.cs`:

```csharp
    public bool TryGetPendingItemRequest(out uint acType, out uint objectId)
    {
        if (_runtime.InventoryOwner.Transactions.PendingRequest is { } pending)
        {
            (acType, objectId) = (AcRequestType(pending.Kind), pending.ItemId);
            return true;
        }
        (acType, objectId) = (0, 0);
        return false;
    }

    /// <summary>acclient's request-slot numbers (RynthCore's ClientActionGates.RequestTypeName).</summary>
    internal static uint AcRequestType(InventoryRequestKind kind) => kind switch
    {
        InventoryRequestKind.Merge => 1,
        InventoryRequestKind.SplitToContainer or InventoryRequestKind.SplitToWorld => 2,
        InventoryRequestKind.Move => 3,
        InventoryRequestKind.Pickup => 4,
        InventoryRequestKind.PutInContainer => 5,
        InventoryRequestKind.DropToWorld => 6,
        InventoryRequestKind.Wield => 7,
        InventoryRequestKind.Give => 9,
        InventoryRequestKind.Shop => 10,
        _ => 0,
    };

    public bool IsAttackInProgress
    {
        get
        {
            RuntimeCombatAttackState attack = _runtime.ActionOwner.CombatAttack;
            return attack.AttackRequestInProgress || attack.AttackServerResponsePending || attack.RepeatAttackInProgress;
        }
    }

    public ulong InventoryRevision => _runtime.InventoryOwner.Objects.MutationRevision;
```

and in `TryGetObject`'s initialiser: `ValidLocations = (uint)item.ValidLocations, Useability = item.Useability ?? 0, ContainerSlot = item.ContainerSlot,`. Confirm `_runtime.InventoryOwner.Transactions` is the right path (see `RuntimeAutomationSurface.cs:457`).

`FakeGameView.cs`:

```csharp
    public (uint Type, uint Object)? PendingItemRequest { get; set; }
    public bool AttackInProgress { get; set; }
    public ulong Revision { get; set; }

    public bool TryGetPendingItemRequest(out uint acType, out uint objectId)
    {
        (acType, objectId) = Read(PendingItemRequest) ?? (0, 0);
        return PendingItemRequest is not null;
    }

    public bool IsAttackInProgress => Read(AttackInProgress);
    public ulong InventoryRevision => Read(Revision);
```

- [ ] **Step 5: Run** `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthInventoryViewTests` and the Core test → PASS; `dotnet build AcDream.slnx` → no new errors.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Core/Items src/AcDream.Rynth.Host tests
git commit -m "feat(rynth): host reads for the inventory: pending request, attack, revision, item slots"
```

---

### Task 2: Overlay texture upload

**Goal:** Plugins on the overlay can upload their own RGBA pictures as ImGui textures and release them, so RynthCore's `RetailSprites` (Classic look) can make textures.

**Files:**
- Modify: `src/AcDream.ClientExtensions/IClientOverlayIcons.cs`
- Modify: `src/AcDream.ClientExtensions/IClientOverlay.cs` (`NoOverlayIcons`)
- Modify: `src/AcDream.App/Extensions/Overlay/OverlayIcons.cs`
- Test: `tests/AcDream.App.Tests/Extensions/OverlayIconsTests.cs` (its `FakeBackend` gains working `UploadOwned`/`ReleaseOwned`)

**Acceptance Criteria:**
- [ ] `IClientOverlayIcons.Upload(byte[] rgba, int width, int height, string name)` returns a texture id from `IPluginImageBackend.UploadOwned`, which `IsIssued` then recognises; zero when unbound, on a bad size (`width`/`height` ≤ 0 or `rgba.Length < width*height*4`) or when the backend throws (logged once per name).
- [ ] `IClientOverlayIcons.Release(IntPtr texture)` calls `ReleaseOwned` only for ids this service uploaded, removes it from the issued set, returns its answer; false otherwise.
- [ ] Both have default interface implementations (zero / false) so other implementers keep compiling; `NoOverlayIcons` answers zero / false.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter OverlayIconsTests` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** (in `OverlayIconsTests`; change `FakeBackend.UploadOwned` to `{ Asked.Add($"upload:{width}x{height}:{debugName}"); return Answer; }` and `ReleaseOwned` to `{ Asked.Add($"release:{texture}"); return true; }`, honouring `Throw` like its other members)

```csharp
    [Fact]
    public void UploadedPicturesAreIssuedAndReleasedOnce()
    {
        _icons.Bind(_backend);
        _backend.Answer = 21;

        IntPtr tex = _icons.Upload(new byte[2 * 3 * 4], 2, 3, "retail 06004D0A");
        Assert.Equal((IntPtr)21, tex);
        Assert.True(_icons.IsIssued(tex));
        Assert.True(_icons.Release(tex));
        Assert.False(_icons.IsIssued(tex));
        Assert.False(_icons.Release(tex));
        Assert.False(_icons.Release((IntPtr)99));
        Assert.Equal(new[] { "upload:2x3:retail 06004D0A", "release:21" }, _backend.Asked);
    }

    [Fact]
    public void BadPicturesAndAnUnboundServiceUploadNothing()
    {
        Assert.Equal(IntPtr.Zero, _icons.Upload(new byte[16], 2, 2, "unbound"));
        _icons.Bind(_backend);
        _backend.Answer = 22;
        Assert.Equal(IntPtr.Zero, _icons.Upload(new byte[15], 2, 2, "short"));
        Assert.Equal(IntPtr.Zero, _icons.Upload(new byte[16], 0, 2, "empty"));
        _backend.Throw = true;
        Assert.Equal(IntPtr.Zero, _icons.Upload(new byte[16], 2, 2, "boom"));
        Assert.Single(_log);
    }
```

- [ ] **Step 2: Run** → FAIL (no `Upload`).

- [ ] **Step 3: Implement**

`IClientOverlayIcons.cs`:

```csharp
    /// <summary>
    /// Uploads a picture of the caller's own (RGBA, 4 bytes a pixel) as a texture id the
    /// overlay draws; zero when it can't. Give it back with <see cref="Release"/>.
    /// </summary>
    IntPtr Upload(byte[] rgba, int width, int height, string name) => IntPtr.Zero;

    /// <summary>Gives back a texture from <see cref="Upload"/>; false for any other id.</summary>
    bool Release(IntPtr texture) => false;
```

`NoOverlayIcons`: `public IntPtr Upload(byte[] rgba, int width, int height, string name) => IntPtr.Zero;` and `public bool Release(IntPtr texture) => false;`.

`OverlayIcons.cs`:

```csharp
    private readonly HashSet<uint> _owned = new();
    private readonly HashSet<string> _reportedUploads = new();

    public IntPtr Upload(byte[] rgba, int width, int height, string name)
    {
        if (_backend is not { } backend || rgba is null || width <= 0 || height <= 0
            || rgba.Length < (long)width * height * 4)
            return IntPtr.Zero;
        try
        {
            uint texture = backend.UploadOwned(rgba, width, height, name);
            if (texture == 0)
                return IntPtr.Zero;
            _owned.Add(texture);
            _issued.Add(texture);
            return (IntPtr)texture;
        }
        catch (Exception error)
        {
            if (_reportedUploads.Add(name))
                _log($"[ImGui] picture upload failed ({name}): {error.GetType().Name}: {error.Message}");
            return IntPtr.Zero;
        }
    }

    public bool Release(IntPtr texture)
    {
        long value = texture.ToInt64();
        if (_backend is not { } backend || value <= 0 || value > uint.MaxValue || !_owned.Remove((uint)value))
            return false;
        _issued.Remove((uint)value);
        try { return backend.ReleaseOwned((uint)value); }
        catch (Exception error)
        {
            _log($"[ImGui] picture release failed: {error.GetType().Name}: {error.Message}");
            return false;
        }
    }
```

- [ ] **Step 4: Run** → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.ClientExtensions src/AcDream.App/Extensions/Overlay tests/AcDream.App.Tests/Extensions/OverlayIconsTests.cs
git commit -m "feat(overlay): plugins upload and release their own pictures as textures"
```

---

### Task 3: Compile RynthCore's inventory, with the hooks answered from OpenAC

**Goal:** Their Inventory face, model, filters, `RetailSprites` and `VaultFeed` compile unchanged; our partial `InventoryModel` stand-in is gone; every hook they call is a stand-in answered by a new `RynthInventoryHookup` from `IRynthGameView`; the published view (packs, items, burden, pyreals) matches the fake game's inventory.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` (Compile items; drop the `InventoryModel.cs` `RynthPanelTypes` entry and update the comment above it)
- Delete: `src/AcDream.Rynth.EngineUi/StandIns/InventoryActionsStandIn.cs`
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs` (remove the `InventoryModel` stand-in and the `InventoryFace` title stub; extend `AcMainThreadQueue`, `UiDropTargets`)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/ChatEngineStandIns.cs` (extend `ClientHelperHooks`, `ClientObjectHooks`)
- Create: `src/AcDream.Rynth.EngineUi/StandIns/InventoryStandIns.cs` (`ClientActionGates`, `SmartBoxHooks`, `AppraisalHooks`, `ClientActionHooks`, `Net.InventoryOrder`, `IconSampling`, `ItemDragBridge`, `DX9Backend`, `EngineThreads`, `ChatCallbackHooks`, `ChatCommandDispatcher`, `LogoutLifecycleHooks`, and `Net.EngineSendMarker`/`IdRequester`/`ChatOrigin` only if their files can't compile as-is)
- Modify: `src/AcDream.Rynth.ScriptWindows/StandIns/EngineStandIns.cs` (`RynthLog.Compat`), `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs` (`ScriptIcons.TryGetItem`)
- Create: `src/AcDream.Rynth.Host/RynthInventoryHookup.cs`
- Modify: `src/AcDream.Rynth.Host/RynthChatHookup.cs` (remove `BuildInventory` and the `InventoryModel.*` hooks), `src/AcDream.Rynth.Host/RynthClientExtension.cs` (create/dispose the new hookup; tick it)
- Modify: `src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs` (`ResetActions` call removed)
- Delete/rewrite: `tests/AcDream.Rynth.Host.Tests/RynthInventoryUseTests.cs` (its cases move to Task 4's tests on the real model)
- Create: `tests/AcDream.Rynth.Host.Tests/InventoryModelReset.cs`, `tests/AcDream.Rynth.Host.Tests/RynthInventoryHookupTests.cs`

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx` succeeds with nothing under `external/` changed (`git status external` clean).
- [ ] No `InventoryModel` member is defined in our code (`grep -rn "partial class InventoryModel" src` → nothing).
- [ ] Stand-ins answer from the host: item fields (`ItemPwdFields` from `RynthObjectInfo`), int properties (`Ints`), Strength (the player's buffed attribute 1 from `PlayerProgress`; false for anyone else), owned ids (`GetContainerContents` copied, clipped to the span), appraisal data, UseDone/WeenieError counters (`ServerReplies`), gate (`IsRequestGateAvailable` true while hooked; `TryReadPending` from `TryGetPendingItemRequest`, age −1; `TryReadAttacking` from `IsAttackInProgress`), `InventoryOrder` from `ContainerSlot`.
- [ ] `InventoryOrder.TryGetOrder(container, slotKind, out ids)` lists the container's items of that kind (`slotKind` true = `Bitfield & RequiresPackSlot (0x00002000)` set; verify the value against their `ObjectDescriptionFlag.RequiresPackSlot`) ordered by `ContainerSlot`; false when any of them has slot −1 or the container is unknown; `Version` changes when the host's `InventoryRevision` does.
- [ ] The hookup calls `InventoryModel.MarkDirty()` on a tick where `InventoryRevision` changed.
- [ ] With the fake game holding a player, a main-pack item, a side pack with one item and one worn item, three `MainThreadTick`s 200 ms apart while subscribed publish a view with those packs and items, the worn item in `Worn`, burden and pyreals from the player's int properties 5 and 20.
- [ ] Rynth.Host.Tests stay green apart from the moved Use tests (count recorded in the commit message).

**Verify:** `dotnet build AcDream.slnx` and `dotnet test tests/AcDream.Rynth.Host.Tests` → green.

**Steps:**

- [ ] **Step 1: Switch the compile.** In the csproj, beside the other face items:

```xml
    <Compile Include="$(RynthEngine)ImGui\Panels\InventoryFace*.cs" Link="RynthCore\%(Filename)%(Extension)" />
    <Compile Include="$(RynthEngine)ImGui\Panels\InventorySelection.cs" Link="RynthCore\InventorySelection.cs" />
    <Compile Include="$(RynthEngine)Compatibility\InventoryModel*.cs" Link="RynthCore\%(Filename)%(Extension)" />
    <Compile Include="$(RynthEngine)Compatibility\InventoryAttributeFilter.cs" Link="RynthCore\InventoryAttributeFilter.cs" />
    <Compile Include="$(RynthEngine)Compatibility\InventoryFilterFacts.cs" Link="RynthCore\InventoryFilterFacts.cs" />
    <Compile Include="$(RynthEngine)Compatibility\VaultFeed.cs" Link="RynthCore\VaultFeed.cs" />
    <Compile Include="$(RynthEngine)Net\AelrynthVaultWire.cs" Link="RynthCore\AelrynthVaultWire.cs" />
    <Compile Include="$(RynthEngine)Net\IdRequester.cs" Link="RynthCore\IdRequester.cs" />
    <Compile Include="$(RynthEngine)Net\ChatOrigin.cs" Link="RynthCore\ChatOrigin.cs" />
    <Compile Include="$(RynthEngine)ImGui\RetailSprites.cs" Link="RynthCore\RetailSprites.cs" />
```

Remove the `RynthPanelTypes` line for `Compatibility\InventoryModel.cs` and its mention in the comment. Delete `InventoryActionsStandIn.cs`, the `InventoryModel` class and the `InventoryFace` stub in `DashboardStandIns.cs`. A probe on 2026-10-19 listed what's then missing (each error is counted twice):
`ClientObjectHooks.{TryGetObjectIntProperty, TryReadItemFields, TryGetObjectAttribute, CollectOwnedIdsFromSnapshot}`, `AppraisalHooks`, `ClientActionGates`, `RynthLog.Compat`, `SmartBoxHooks`, `ChatOrigin`, `IconSampling`, `ItemDragBridge`, `ChatCommandDispatcher`, `DX9Backend`, `ClientActionHooks`, `UiDropTargets.{EngineDragObject, TryClaim}`, `Net.{IdRequester, EngineSendMarker}`, `LogoutLifecycleHooks`, `EngineThreads`, `ChatCallbackHooks`, `ScriptIcons.TryGetItem`, `ClientHelperHooks.{WieldItem, UseObject, SplitStackInternal, PutItemInContainerAt, MoveItemInternal, IsOwnedContainerFull, HasWieldItem, HasPutItemInContainer, HasGiveObjectTo, HasDropItem, GiveObjectTo, DropItem}`, `AcMainThreadQueue.EnqueueSetSelectedObject`, `InventoryModel.ResetActions` (our caller), and a definite-assignment error at their `TryReadAttacking(out bool attacking)` call that goes away once the stand-in has that exact signature. If `IdRequester.cs` or `ChatOrigin.cs` need engine state, drop them from the csproj and stand in the members used (`EngineSendMarker.Enter(IdRequester) → Scope : IDisposable`, `IdRequester.Ui`; `ChatOrigin.StartsWithMarker`, `ChatOrigin.ContentStart`).

- [ ] **Step 2: Data stand-ins** — extend `ClientObjectHooks` in `ChatEngineStandIns.cs` (the `ItemPwdFields` struct is already there):

```csharp
        internal delegate bool ItemFieldsReader(uint objectId, out ItemPwdFields fields);
        internal delegate bool IntPropertyReader(uint objectId, uint stype, out int value);
        internal delegate bool AttributeReader(uint objectId, uint stype, int raw, out uint value);

        /// <summary>The host's item fields, int properties, attributes and container contents.</summary>
        internal static ItemFieldsReader? ItemFields { get; set; }
        internal static IntPropertyReader? IntProperty { get; set; }
        internal static AttributeReader? Attribute { get; set; }
        internal static Func<uint, IReadOnlyList<uint>>? Contents { get; set; }

        // RynthCore reads these PublicWeenieDesc fields out of acclient's object in one go.
        internal static bool TryReadItemFields(uint objectId, out ItemPwdFields f)
        {
            f = default;
            return ItemFields is { } read && read(objectId, out f);
        }

        public static bool TryGetObjectIntProperty(uint objectId, uint stype, out int value)
        {
            value = 0;
            return IntProperty is { } read && read(objectId, stype, out value);
        }

        public static bool TryGetObjectAttribute(uint objectId, uint stype, int raw, out uint value)
        {
            value = 0;
            return Attribute is { } read && read(objectId, stype, raw, out value);
        }

        // RynthCore walks its ~100 ms copy of acclient's objects; here, the client's table now.
        internal static int CollectOwnedIdsFromSnapshot(uint containerId, Span<uint> dest)
        {
            if (Contents?.Invoke(containerId) is not { } ids)
                return 0;
            int n = Math.Min(ids.Count, dest.Length);
            for (int i = 0; i < n; i++)
                dest[i] = ids[i];
            return n;
        }
```

New `InventoryStandIns.cs` (namespace `RynthCore.Engine.Compatibility` unless noted; one `<summary>` each):

```csharp
    /// <summary>
    /// RynthCore reads acclient's item request slot and attack flag from its .data. OpenAC's
    /// InventoryTransactionState keeps the same one pending request; the host answers from it.
    /// </summary>
    internal static class ClientActionGates
    {
        internal delegate bool PendingReader(out uint type, out uint objectId);

        internal static PendingReader? Pending { get; set; }
        internal static Func<bool>? Attacking { get; set; }

        public static bool IsRequestGateAvailable => Pending is not null;
        public static bool IsAttackGateAvailable => Attacking is not null;

        public static bool TryReadPending(out uint type, out uint objectId, out double ageSeconds)
        {
            (type, objectId, ageSeconds) = (0, 0, -1);
            if (Pending is not { } read)
                return false;
            if (!read(out type, out objectId))
                (type, objectId) = (0, 0);
            return true;
        }

        public static bool TryReadAttacking(out bool attacking)
        {
            attacking = false;
            if (Attacking is not { } read)
                return false;
            attacking = read();
            return true;
        }

        // Their names, verbatim (ClientActionGates.RequestTypeName).
        public static string RequestTypeName(uint type) => type switch
        {
            0 => "none",
            1 => "merge",
            2 => "split",
            3 => "move",
            4 => "pick up",
            5 => "put in container",
            6 => "drop",
            7 => "wield",
            9 => "give",
            10 => "vendor buy/sell",
            _ => $"request {type}",
        };
    }

    /// <summary>RynthCore counts acclient's UseDone and WeenieError events; the host answers from ServerReplies.</summary>
    internal static class SmartBoxHooks
    {
        internal static Func<AcDream.Core.Items.ServerReplySnapshot>? Replies { get; set; }

        public static int GetUseDoneSeq() => Replies?.Invoke().UseDoneSequence ?? 0;

        public static bool TryGetLastUseDone(out int seq, out uint error)
        {
            (seq, error) = (0, 0);
            if (Replies is not { } read) return false;
            var r = read();
            (seq, error) = (r.UseDoneSequence, r.LastUseDoneError);
            return seq != 0;
        }

        public static bool TryGetLastWeenieError(out int seq, out uint error, out uint eventType, out uint objectId)
        {
            (seq, error, eventType, objectId) = (0, 0, 0, 0);
            if (Replies is not { } read) return false;
            var r = read();
            (seq, error, eventType, objectId) =
                (r.WeenieErrorSequence, r.LastWeenieError, r.LastWeenieErrorEventType, r.LastWeenieErrorObjectId);
            return seq != 0;
        }
    }
```

(If `AcDream.Core` isn't referenced by EngineUi, use a tuple `Func<(int UseDoneSeq, uint UseDoneError, int ErrorSeq, uint Error, uint Event, uint Object)>` instead, as the old `InventoryModel.Replies` did. Check the old `SmartBoxHooks`-shaped reads RynthAi's host already does — `grep -rn "SmartBoxHooks" src` — and keep one stand-in.)

```csharp
    /// <summary>RynthCore keeps each object's last appraisal; the host answers from the client's object table.</summary>
    internal static class AppraisalHooks
    {
        internal delegate bool AppraisalReader(uint guid, out RynthAppraisal appraisal);

        /// <summary>What one appraisal left: identified, spells, int/bool/float properties.</summary>
        internal sealed record RynthAppraisal(bool Identified, IReadOnlyList<uint> SpellIds,
            IReadOnlyDictionary<uint, int> Ints, IReadOnlyDictionary<uint, bool> Bools, IReadOnlyDictionary<uint, double> Floats);

        internal static AppraisalReader? Reader { get; set; }

        private static RynthAppraisal? Read(uint guid) => Reader is { } r && r(guid, out RynthAppraisal a) ? a : null;

        public static bool HasAppraisalData(uint guid) => Read(guid)?.Identified ?? false;

        public static int GetObjectSpellIds(uint guid, uint[] output, int maxCount)
        {
            if (Read(guid) is not { Identified: true } a) return 0;
            int n = Math.Min(Math.Min(maxCount, output.Length), a.SpellIds.Count);
            for (int i = 0; i < n; i++) output[i] = a.SpellIds[i];
            return n;
        }

        public static bool TryGetCachedIntProperty(uint guid, uint stype, out int value)
        {
            value = 0;
            return Read(guid) is { Identified: true } a && a.Ints.TryGetValue(stype, out value);
        }

        public static bool TryGetCachedBoolProperty(uint guid, uint stype, out bool value)
        {
            value = false;
            return Read(guid) is { Identified: true } a && a.Bools.TryGetValue(stype, out value);
        }

        public static bool TryGetCachedDoubleProperty(uint guid, uint stype, out double value)
        {
            value = 0;
            return Read(guid) is { Identified: true } a && a.Floats.TryGetValue(stype, out value);
        }
    }

    /// <summary>RynthCore calls acclient's RequestId (0xC8); the host sends the client's identify.</summary>
    internal static class ClientActionHooks
    {
        internal static Func<uint, bool>? Identify { get; set; }

        public static bool RequestId(uint objectId) => objectId != 0 && (Identify?.Invoke(objectId) ?? false);
    }
```

`Net.InventoryOrder` (namespace `RynthCore.Engine.Net`):

```csharp
    /// <summary>
    /// RynthCore reads each container's order from the server's messages. OpenAC keeps each
    /// item's ContainerSlot; the host lists a container's contents with their slots and pack-slot flag.
    /// </summary>
    internal static class InventoryOrder
    {
        internal readonly record struct Placed(uint Id, int Slot, bool PackSlot);

        internal static Func<uint, IReadOnlyList<Placed>?>? Source { get; set; }
        internal static Func<ulong>? Revision { get; set; }

        public static int Version => unchecked((int)(Revision?.Invoke() ?? 0));

        public static bool TryGetOrder(uint container, bool slotKind, out uint[] ids)
        {
            ids = Array.Empty<uint>();
            if (Source?.Invoke(container) is not { } placed)
                return false;
            var kind = new List<Placed>();
            foreach (Placed p in placed)
            {
                if (p.PackSlot != slotKind) continue;
                if (p.Slot < 0) return false;   // not every place known: their first-empty-slot fallback
                kind.Add(p);
            }
            kind.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            ids = kind.ConvertAll(p => p.Id).ToArray();
            return true;
        }
    }
```

Small ones (namespaces as RynthCore's: `ImGuiBackend` for `IconSampling`, `ItemDragBridge`, `DX9Backend`; `RynthCore.Engine` for `EngineThreads`):

```csharp
    /// <summary>RynthCore switches its D3D9 sampler to point for crisp icons; the overlay's own sampling is kept.</summary>
    internal static class IconSampling
    {
        public static void Begin(ImDrawListPtr dl) { }
        public static void End(ImDrawListPtr dl) { }
    }

    /// <summary>RynthCore hands an item dragged out of its panel to acclient's drag; OpenAC has no such hand-off.</summary>
    internal static class ItemDragBridge
    {
        public static void NotePopOutDrag(uint itemId) { }
        public static void EndPopOutDrag() { }
    }

    /// <summary>RynthCore makes D3D9 textures; here the overlay's own upload (device unused).</summary>
    internal static class DX9Backend
    {
        internal static Func<byte[], int, int, IntPtr>? Upload { get; set; }
        internal static Func<IntPtr, bool>? Release { get; set; }

        public static IntPtr CreateTextureRgba(IntPtr pDevice, int width, int height, ReadOnlySpan<byte> rgba) =>
            Upload?.Invoke(rgba.ToArray(), width, height) ?? IntPtr.Zero;

        public static void ReleaseTexture(IntPtr tex) => Release?.Invoke(tex);
    }

    /// <summary>RynthCore's engine stop signal; the host sets it while shutting down.</summary>
    internal static class EngineThreads
    {
        internal static volatile bool StoppingFlag;
        public static bool Stopping => StoppingFlag;
    }
```

`VaultFeed` needs only: `ChatCallbackHooks.IncomingInstalled` (false), `ChatCommandDispatcher.SendServerCommand(string)` (false), `LogoutLifecycleHooks.HasObservedLogout` (false) — check each member's exact use in `VaultFeed.cs` (it never runs: `ServerInfo.IsAelrynth` is false) and add only what the compiler asks for, inert, with a summary saying so. Also: `UiDropTargets` gains `public static uint EngineDragObject { get; set; }` and `public static bool TryClaim(uint itemId, uint spellId, out string key) { key = string.Empty; return false; }`; `AcMainThreadQueue` gains `internal static Func<uint, bool>? SelectObject { get; set; }` and `public static bool EnqueueSetSelectedObject(uint objectId) => SelectObject?.Invoke(objectId) ?? false;` (OpenAC draws and ticks on one thread, so "queue" = now, as `MainThreadGuard` says); `RynthLog` gains `internal static void Compat(string msg) => Sink?.Invoke("[Compat] " + msg);`; `ScriptIcons` gains `TryGetItem(...)` returning false ("only RynthCore's Aelrynth vault draws icons from their parts").

- [ ] **Step 3: Action stand-ins** — extend `ClientHelperHooks` in `ChatEngineStandIns.cs`:

```csharp
        /// <summary>The host's item actions, as RynthCore sends them through acclient's own calls. Game thread.</summary>
        internal static Func<uint, bool>? Use { get; set; }
        internal static Func<uint, uint, int, int, bool>? Move { get; set; }      // item, container, slot, amount (0 = whole)
        internal static Func<uint, uint, bool>? Wield { get; set; }
        internal static Func<uint, bool>? Drop { get; set; }
        internal static Func<uint, uint, int, bool>? Give { get; set; }
        internal static Func<uint, bool>? ContainerFull { get; set; }

        public static bool HasDropItem => Drop is not null;
        public static bool HasPutItemInContainer => Move is not null;
        public static bool HasGiveObjectTo => Give is not null;
        public static bool HasWieldItem => Wield is not null;

        public static bool UseObject(uint objectId) => Use?.Invoke(objectId) ?? false;
        public static bool PutItemInContainerAt(uint objectId, uint containerId, int slot) =>
            Move?.Invoke(objectId, containerId, slot, 0) ?? false;
        // acclient's MoveItemInternal / SplitStackInternal walk the pack's slot table for the first
        // empty slot (RynthCore always passes slot 0) and MoveItemInternal refuses a full pack of the
        // player's own. ACE keeps places contiguous, so the first empty one is the item count.
        internal static Func<uint, uint, int>? FirstEmptySlot { get; set; }   // container, moving item → slot

        public static bool MoveItemInternal(uint objectId, uint targetContainerId, int slot, int amount) =>
            !IsOwnedContainerFull(targetContainerId)
            && (Move?.Invoke(objectId, targetContainerId, FirstEmptySlot?.Invoke(targetContainerId, objectId) ?? 0, amount) ?? false);
        public static bool SplitStackInternal(uint objectId, uint targetContainerId, int slot, int amount) =>
            Move?.Invoke(objectId, targetContainerId, FirstEmptySlot?.Invoke(targetContainerId, objectId) ?? 0, amount) ?? false;
        internal static bool IsOwnedContainerFull(uint target) => ContainerFull?.Invoke(target) ?? false;
        public static bool WieldItem(uint objectId, uint equipMask) => Wield?.Invoke(objectId, equipMask) ?? false;
        public static bool DropItem(uint objectId) => Drop?.Invoke(objectId) ?? false;
        public static bool GiveObjectTo(uint objectId, uint targetId, int amount = 0) =>
            Give?.Invoke(objectId, targetId, amount) ?? false;
```

- [ ] **Step 4: The hookup** — `src/AcDream.Rynth.Host/RynthInventoryHookup.cs`, same shape as `RynthChatHookup` (copy its `Guard`). Constructor takes `(IRynthGameView view, IClientOverlayIcons? icons, Action<string> log)` and sets:
  - `ClientObjectHooks.ItemFields` → `TryGetObject` mapped:

```csharp
    private bool ReadItemFields(uint id, out ClientObjectHooks.ItemPwdFields f)
    {
        f = default;
        RynthObjectInfo? o = Guard(nameof(ClientObjectHooks.TryReadItemFields), () => _view.TryGetObject(id, out RynthObjectInfo i) ? i : null, null);
        if (o is null) return false;
        f = new ClientObjectHooks.ItemPwdFields
        {
            Wcid = o.Wcid, Icon = o.IconId, Container = o.ContainerId, Wielder = o.WielderId,
            ValidLocations = o.ValidLocations, Location = o.EquipLocation, Type = o.ItemType, Value = (uint)o.Value,
            Useability = o.Useability, StackSize = (uint)o.StackSize, MaxStackSize = (uint)o.StackSizeMax,
            Bitfield = o.Bitfield, SpellId = o.SpellId ?? 0, ItemsCapacity = o.ItemsCapacity, Burden = o.Burden,
            MaterialType = (int)(o.MaterialType ?? 0), Workmanship = o.Workmanship,
        };
        return true;
    }
```

  - `ClientObjectHooks.IntProperty` → `TryGetObject(id).Ints.TryGetValue(stype)`; `Attribute` → only when `id == _view.PlayerId`: `_view.PlayerProgress?.Attributes` entry with `Id == stype`, value `Buffed` (raw 0) or `Base` (raw ≠ 0); `Contents` → `_view.GetContainerContents`.
  - `AppraisalHooks.Reader` → from `TryGetObject`: `new(o.Identified, o.AppraisedSpellIds, o.Ints, o.Bools, o.Floats)`.
  - `ClientActionHooks.Identify` → `_view.RequestIdentify`; `AcMainThreadQueue.SelectObject` → `_view.SelectObject`.
  - `ClientActionGates.Pending` → `_view.TryGetPendingItemRequest`; `Attacking` → `_view.IsAttackInProgress`.
  - `SmartBoxHooks.Replies` → `_view.ServerReplies`.
  - `ClientHelperHooks.Use` → the same use path the old `InventoryModel.UseSender` had (`_view.UseObject`); `Move` → `_view.MoveItem(item, container, slot, amount)`; `Wield` → `_view.WieldItem`; `Drop` → `_view.MoveItemExternal(item, 0, 0)`; `Give` → `_view.GiveItem(item, target, (uint)Math.Max(0, amount))`; `ContainerFull` → `_view.TryCountContents(target, out int items, out _) && TryGetObject(target) is { ItemsCapacity: > 0 } c && items >= c.ItemsCapacity`; `FirstEmptySlot(container, moving)` → the number of `GetContainerContents(container)` entries that are not worn (`WielderId == 0`), not pack-slot things (`Bitfield & 0x00002000` clear) and not `moving` itself.
  - `Net.InventoryOrder.Source` → `GetContainerContents(container)` (skip ids whose `WielderId != 0`) mapped to `Placed(id, o.ContainerSlot, (o.Bitfield & 0x00002000) != 0)`; null when the container is unknown; `Revision` → `_view.InventoryRevision`.
  - `DX9Backend.Upload/Release` → `icons.Upload(rgba, w, h, "rynth retail")` / `icons.Release` when `icons` is not null.
  - `Tick()`: read `_view.InventoryRevision` (guarded); when it differs from the last one, `InventoryModel.MarkDirty()`.
  - `Dispose()`: unset every delegate still ours; `EngineThreads.StoppingFlag = true` then `RetailSprites.Shutdown()` (guarded), then `StoppingFlag = false`.

  Remove from `RynthChatHookup` the `_inventory`/`BuildInventory`, `_useSender`, `_busy`, `_replies`, `_readItem`, `_usePlayerId` fields and their set/unset lines. In `RynthClientExtension`: create `_inventoryHookup = new RynthInventoryHookup(view, context.Overlay?.Icons, context.Log)` beside `_chatHookup` (check the exact name of the overlay's icons member in `IClientOverlay.cs:19`), tick it in `Tick` before `RynthAiDashboard.Tick()`, dispose it beside `_chatHookup` (inside the same try). Remove `InventoryModel.ResetActions()` from `RynthAiDashboard.Stop` (their model empties itself when the player id goes to 0).

- [ ] **Step 5: The model reset helper** — `tests/AcDream.Rynth.Host.Tests/InventoryModelReset.cs`:

```csharp
using System.Collections;
using System.Reflection;
using RynthCore.Engine.Compatibility;

namespace AcDream.Rynth.Host.Tests;

/// <summary>Their InventoryModel keeps static state with no reset; tests clear it between cases.</summary>
internal static class InventoryModelReset
{
    public static void Run()
    {
        Type t = typeof(InventoryModel);
        foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (f.IsInitOnly)
            {
                if (f.GetValue(null) is IDictionary d) d.Clear();
                else if (f.GetValue(null) is IList l && !f.FieldType.IsArray) l.Clear();
                else if (f.GetValue(null) is { } o && o.GetType().GetMethod("Clear", Type.EmptyTypes) is { } clear) clear.Invoke(o, null);
                continue;
            }
            if (f.FieldType == typeof(InventoryView)) f.SetValue(null, InventoryView.Empty);
            else if (f.Name == "_dirty") f.SetValue(null, 1);
            else f.SetValue(null, f.FieldType.IsValueType ? Activator.CreateInstance(f.FieldType) : null);
        }
    }
}
```

Read their field list first (`grep -n "private static" external/RynthCore/src/RynthCore.Engine/Compatibility/InventoryModel*.cs`) and adjust for any field whose default isn't zero/null (e.g. `_playerName` is `string.Empty`: set strings to `string.Empty`).

- [ ] **Step 6: Write the hookup tests** — `RynthInventoryHookupTests.cs`. Fixture: a `FakeGameView` with player `0x50000001`, main pack items, a side pack `0x80000010` (`ItemsCapacity = 24`, `Bitfield = 0x00002000`), an item in it, a worn item (`WielderId = Player`), player ints `{5: 1200, 20: 5000}`, `Contents` filled; `new RynthInventoryHookup(view, null, _ => {})`; `InventoryModelReset.Run()` in the constructor and `Dispose`. Helper `RynthObjectInfo Item(uint container, uint wielder = 0, int slot = -1, uint bitfield = 0, int capacity = 0, string name)` with every required member set (also set `view.Names`). Cases:

```csharp
    [Fact]
    public void TheModelPublishesThePlayersPacksItemsAndWornThings()
    {
        InventoryModel.Subscribe();
        try
        {
            for (int i = 0; i < 3; i++) { InventoryModel.MainThreadTick(); Thread.Sleep(200); }
            InventoryView v = InventoryModel.Current;
            Assert.Contains(v.Packs, p => p.Id == Pack && p.Items.Any(it => it.Id == InPack));
            Assert.Contains(v.Worn, it => it.Id == Worn);
            // and the main pack holds MainItem; burden/pyreals from ints 5 and 20 —
            // read InventoryView's member names in their InventoryModel.cs (lines ~100-130) for these asserts.
        }
        finally { InventoryModel.Unsubscribe(); }
    }

    [Fact]
    public void ItemFieldsComeFromTheObjectTable() { /* TryReadItemFields(InPack) → Container == Pack, StackSize, Bitfield; unknown id → false */ }

    [Fact]
    public void StrengthIsThePlayersBuffedAttributeOnly() { /* view.PlayerProgress with attribute 1 Buffed 200 Base 100 → (200 raw 0), (100 raw 1); another id → false */ }

    [Fact]
    public void TheGateIsTheClientsPendingRequestAndAttack()
    {
        Assert.True(ClientActionGates.IsRequestGateAvailable);
        _view.PendingItemRequest = (3, InPack);
        Assert.True(ClientActionGates.TryReadPending(out uint type, out uint obj, out double age));
        Assert.Equal((3u, InPack, -1d), (type, obj, age));
        _view.PendingItemRequest = null;
        Assert.True(ClientActionGates.TryReadPending(out type, out _, out _));
        Assert.Equal(0u, type);
        _view.AttackInProgress = true;
        Assert.True(ClientActionGates.TryReadAttacking(out bool attacking) && attacking);
    }

    [Fact]
    public void OrderComesFromSlotsItemsAndPacksApart() { /* two items slots 1,0 + a pack slot 0 in the player → TryGetOrder(player,false) = [slot0, slot1], (player,true) = [pack]; one item slot -1 → false */ }

    [Fact]
    public void ARevisionChangeMarksTheModelDirty() { /* reflection-read _dirty after Reset sets 0; Tick with same revision → 0; Revision++ then Tick → 1 */ }

    [Fact]
    public void DisposeUnhooksOnlyWhatIsStillOurs() { /* after Dispose: ClientHelperHooks.HasDropItem false, ClientActionGates.IsRequestGateAvailable false */ }
```

Fill each `/* … */` with the asserts it names (no other behaviour), using the fixture's ids.

- [ ] **Step 7: Run** `dotnet build AcDream.slnx` then `dotnet test tests/AcDream.Rynth.Host.Tests`. Fix stand-ins (never their files) until green. Delete `RynthInventoryUseTests.cs` (Task 4 re-covers Use on the real model); Mini Remote / Item Count tests that set `InventoryModel.Source`/`UseSender` must now go through the hookup + `FakeGameView` instead — rewrite their setup only, keep their asserts.

- [ ] **Step 8: Commit**

```bash
git add -A src tests
git commit -m "feat(rynth): RynthCore's InventoryModel and Inventory face compiled unchanged, hooks answered from OpenAC"
```

---

### Task 4: Item actions on the real model

**Goal:** Every action RynthCore's model sends reaches the right OpenAC call, the gate holds and releases, results and refusals carry their texts, a batch runs one item after another, and hovering an unknown item asks the server to identify it once.

**Files:**
- Create: `tests/AcDream.Rynth.Host.Tests/RynthInventoryActionTests.cs`
- Modify (only if a test shows a stand-in/hookup bug): files from Task 3

**Acceptance Criteria:**
- [ ] Use → `use <id>`; then `Replies` UseDone seq +1 with error 0 → `LastResult` "Used <name>." ok.
- [ ] Move with `Slot = 2` → `move <id> <pack> 2 0` (their `PutItemInContainerAt`); item's container becomes the pack → "Moved <name> to <pack name>." Move with `Slot = -1` into a pack holding 2 items → `move <id> <pack> 2 <stack>` (their `MoveItemInternal(id, target, 0, stack)`, first empty slot = 2); into a full pack → no call, "<pack name> is full, or the move isn't possible."
- [ ] Split `Amount = 3` of a stack of 10 into a pack holding 2 items → `move <id> <pack> 2 3`; stack drops to 7 → "Split 3 <name> into <pack name>."
- [ ] Wield (TargetId = slot mask) → `wield <id> <mask>`; Drop → `moveext <id> 00000000 0`; Give to a creature (ItemType has 0x10) → `give <id> <target> 0`.
- [ ] A WeenieError with event 0x00A0 for the item → "Refused: …" not ok.
- [ ] With `PendingItemRequest = (3, other)` a Move is held: `Pending.WaitingForGame` true and `BusyWith` "move of <other name>"; clearing it sends on the next tick.
- [ ] A batch of three Drops sends them one at a time (the second only after the first item is gone); `CancelBatch` stops the rest.
- [ ] `Hover(unknown id)` then ticks ≥ 0.3 s apart send exactly one `id <id>`; an identified item sends none.

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthInventoryActionTests` → pass; full Rynth.Host.Tests green.

**Steps:**

- [ ] **Step 1: Write the tests** — same fixture as Task 3's hookup tests (copy it into a shared `InventoryFixture` class in `InventoryModelReset.cs` if both need it). Helpers:

```csharp
    private static void Ticks(int n = 3)
    {
        for (int i = 0; i < n; i++) { InventoryModel.MainThreadTick(); Thread.Sleep(120); }
    }

    private static InventoryRequest Req(InventoryActionKind kind, uint item, string name, uint target = 0, string targetName = "", int amount = 0, int slot = -1) =>
        new() { Kind = kind, ItemId = item, ItemName = name, TargetId = target, TargetName = targetName, Amount = amount, Slot = slot };
```

Each test: `InventoryModel.Subscribe()` (so captures run), `Assert.True(InventoryModel.Request(req, out string why), why)`, `Ticks(1)`, assert `_view.Actions` contains the call, then change the fake (move the item's `ContainerId`, remove it from `Objects`, bump `Replies`) and `Ticks()`, assert `InventoryModel.LastResult!.Text` and `.Ok`, then `Unsubscribe()`. Read their `Send`, `Watch`, `ActionLabel`, `PendingLabel` and `Finish` in `InventoryModel.cs` (lines ~800–1010) for the exact strings and argument values; assert those strings verbatim. For the gate: set `_view.PendingItemRequest` before `Request`, `Ticks(1)`, assert `InventoryModel.Pending!.WaitingForGame` and `BusyWith`, set it to null, `Ticks(1)`, assert the action was sent. For the batch: `InventoryModel.RequestBatch([...three drops...], out _)`, tick, assert one `moveext`, remove that item from `Objects`, tick, assert the second; `CancelBatch()`, remove it, tick, assert no third. For identify: `InventoryModel.Hover(id)`, `Ticks(4)` → one `id <id>`; an object with `Identified = true` → none.

- [ ] **Step 2: Run** → fix any stand-in/hookup mismatch the tests expose (never their files). Expected end state: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests src
git commit -m "test(rynth): Inventory item actions, gate, batch and identify through OpenAC"
```

---

### Task 5: Open the panel: registration, Classic art frames, `/rc inventory`

**Goal:** The Inventory face is registered with the panel host, draws headless in both looks without errors, Classic's retail art is fed by `RetailSprites.BeginFrame` each frame, and `/rc inventory` toggles it with RynthCore's texts.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs` (register `InventoryFace`; a `BeginFrame()` that calls `RetailSprites.BeginFrame(IntPtr.Zero)`)
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs` (`DrawFrame` calls the dashboard's `BeginFrame` before `ImGuiPanelHost.DrawAll()`, guarded)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/RynthCoreChatCommandsStandIn.cs` (the `inventory` verb; usage text lists both verbs)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthInventoryPanelTests.cs` (create), `tests/AcDream.Rynth.Host.Tests/RynthMiniRemoteTests.cs` (usage/unknown-verb text asserts updated)

**Acceptance Criteria:**
- [ ] After `RynthAiDashboard.Start()`, `ImGuiPanelHost.HasFace("Inventory")` is true.
- [ ] Opening it and drawing 3 headless frames (`HeadlessImGui`, see `RynthMiniRemoteTests.TheDashboardRegistersTheMiniRemoteAndItDrawsTheGrid`) logs no "stopped drawing" / exception lines, in the Classic look and again after `InventorySettingsStore.SetClassic(false)`.
- [ ] `RetailSprites.BeginFrame` runs once per drawn frame; with `DX9Backend.Upload` set to a counting fake and the test dats present (`/Users/davidsmith/AsheronsCall/client_portal.dat`; skip when missing, as the Skills dat tests do), a Classic draw leads to at least one upload within 50 frames (`Thread.Sleep(20)` between frames; their decode runs on the thread pool).
- [ ] `/rc inventory` toggles the panel (`ImGuiPanelHost.IsOpen("Inventory")` flips — use the host's real query name); with no face registered it replies "The Inventory panel isn't available (the ImGui layer is off?)."; bare `/rc` replies `Usage: /rc miniremote [show|hide|toggle|tab <tab>|section <id>] | inventory`; an unknown verb replies `/rc <verb> isn't in OpenAC (only /rc miniremote and /rc inventory).`
- [ ] The Mini Remote's Inventory section embeds the real face (`CanEmbed("Inventory")` true).

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests` → all green; `dotnet build AcDream.slnx -c Release` succeeds.

**Steps:**

- [ ] **Step 1: Write the failing tests** — `RynthInventoryPanelTests.cs`, modelled on `RynthMiniRemoteTests` (same sandbox/HeadlessImGui setup; copy its constructor/Dispose):

```csharp
    [Fact]
    public void TheDashboardRegistersTheInventoryAndItDrawsInBothLooks()
    {
        RynthAiDashboard.Start();
        Assert.True(ImGuiPanelHost.HasFace(InventoryFace.Title));
        ImGuiPanelHost.Open(InventoryFace.Title);
        DrawFrames(3);
        InventorySettingsStore.SetClassic(false);
        DrawFrames(3);
        Assert.DoesNotContain(_log, l => l.Contains("stopped drawing") || l.Contains("Exception"));
    }

    [Fact]
    public void RcInventoryTogglesThePanel()
    {
        RynthAiDashboard.Start();
        var replies = new List<string>();
        RynthCoreChatCommands.TryHandle("inventory", replies.Add);
        Assert.True(ImGuiPanelHost.IsOpen(InventoryFace.Title));
        RynthCoreChatCommands.TryHandle("inventory", replies.Add);
        Assert.False(ImGuiPanelHost.IsOpen(InventoryFace.Title));
        Assert.Empty(replies);
    }

    [Fact]
    public void ClassicArtIsUploadedThroughTheOverlay() { /* skip without the dats; DX9Backend.Upload = counting fake returning (IntPtr)(++n); open Classic; up to 50 frames with Sleep(20) until n > 0 */ }
```

`DrawFrames(n)` = per frame: `RynthAiDashboard.BeginFrame(); ImGuiPanelHost.DrawAll();` inside the headless frame helper. Check `InventorySettingsStore.SetClassic`'s exact signature in `InventoryFace.Classic.cs:972+` and point its file at the test sandbox if it writes to LocalApplicationData (the sandbox already redirects it if `RynthConfigSandbox` covers `LocalApplicationData`; otherwise restore the setting in `Dispose`).

- [ ] **Step 2: Implement**
  - `RynthAiDashboard.Start`: `InventoryFace.Register();` beside `ItemCountFace.Register();`.
  - `RynthAiDashboard`:

```csharp
    /// <summary>RynthCore's EngineFrameController: the retail pictures decoded since the last frame become textures before the faces draw.</summary>
    internal static void BeginFrame()
    {
        if (_started)
            RetailSprites.BeginFrame(IntPtr.Zero);
    }
```

  - `RynthClientExtension.DrawFrame`: `Guard("RynthAi", RynthAiDashboard.BeginFrame);` just before `ImGuiPanelHost.DrawAll();`.
  - `/rc inventory` — in `RynthCoreChatCommands.TryHandle`, before the miniremote check, RynthCore's branch (`RynthCoreChatCommands.cs:486-491` in their source):

```csharp
            if (IsVerb(sub, "inventory", out _))
            {
                // The ImGui Inventory panel (open/close), like its bar button.
                if (!ImGuiPanelHost.HasFace(InventoryFace.Title))
                    reply("The Inventory panel isn't available (the ImGui layer is off?).");
                else
                    PanelRouter.Toggle(InventoryFace.Title);
                return true;
            }
```

  Update the usage and unknown-verb texts as in the criteria and the class summary.

- [ ] **Step 3: Run** → PASS; `dotnet build AcDream.slnx -c Release`.

- [ ] **Step 4: Commit**

```bash
git add src tests
git commit -m "feat(rynth): RynthCore's Inventory panel opens from the dashboard, /rc inventory and the Mini Remote"
```

---

### Task 6: Live check (user)

**Goal:** The user confirms the Inventory panel works in their own client on the local ACE server.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:** none (fixes found here get their own commits).

**Acceptance Criteria:**
- [ ] User reports, in their client (Release build, local ACE only — never Dreamweave), each of: `/rc inventory` opens and closes the panel; Classic look shows retail art, paperdoll slots and packs; toggle to Modern works and is remembered after relog; burden and pyreals match the game's; drag an item to a side pack and onto a chosen slot; split a stack; drag a weapon onto a paperdoll slot (wields); drop an item; give an item to an NPC; select three items and drop them as a batch, Stop part-way; hover an unidentified item → tooltip fills with spells/mana; Mini Remote Inventory section shows the panel; Item Count still counts.
- [ ] Any failure is fixed on the branch and rechecked.

**Verify:** the user's report in the conversation; the client log (`~/Library/Logs/ACELauncher/aethrixtest.log`) has no `InventoryModel: tick failed` or `threw` lines from the session.

**Steps:**

- [ ] **Step 1:** Ask the user whether they are available (memory: ask-before-live-client); don't launch anything before they say so.
- [ ] **Step 2:** Walk the checklist with them; record PASS/FAIL per item in `.superpowers/sdd/2026-10-19-rynth-inventory-panel/progress.md`.
- [ ] **Step 3:** Fix failures (systematic-debugging), rebuild Release, recheck.
