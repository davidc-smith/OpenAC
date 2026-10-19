# RynthCore's Inventory panel in OpenAC — design

Date: 2026-10-19. Branch `feat/rynth-inventory-panel`. Last unblocked dashboard panel
(after Dungeon Map and Mini Remote; World Map and Characters stay blocked).

## Goal

RynthCore's ImGui Inventory panel (both looks, Classic and Modern) runs in OpenAC from their
source, unchanged, with every item action working: Use, Move (to a pack slot), Split, Wield,
Drop, Give, several-item batches, and identify-on-hover tooltips.

## Decisions (user, 2026-10-19)

- **Model A**: compile their real `InventoryModel.cs` + `InventoryModel.Batch.cs` unchanged and
  delete our partial `InventoryModel` stand-in; answer the hooks it calls from OpenAC.
- **Identify on hover**: keep RynthCore's behaviour (0.3 s hover, at most one a second, never the
  same item within a minute, the tooltip's identify queue).
- Section 1 (scope, entry points) approved as presented; the user asked to go straight to the plan.

## Compiled unchanged (from `external/RynthCore/src/RynthCore.Engine`)

- `ImGui/Panels/InventoryFace.cs`, `InventoryFace.Classic.cs`, `InventoryFace.MultiSelect.cs`,
  `InventoryFace.AttrFilter.cs`, `InventorySelection.cs`
- `Compatibility/InventoryModel.cs`, `InventoryModel.Batch.cs`, `InventoryAttributeFilter.cs`,
  `InventoryFilterFacts.cs`, `VaultFeed.cs`
- `ImGui/RetailSprites.cs` (Classic's retail art from `client_portal.dat` through the
  `AcIconDecoder` / dat reader already compiled for Skills)
- `Net/AelrynthVaultWire.cs` (vault payload types; never fed: `ServerInfo.IsAelrynth` is false)

The `RynthTypeExtract` entry for `InventoryModel.cs` is removed (the whole file is compiled now).

## Removed

- Our `InventoryModel` stand-in: the view builder in `StandIns/DashboardStandIns.cs`
  (`Source`, `Subscribe`, `Current`, `SelectedItem`, `Reset`) and `StandIns/InventoryActionsStandIn.cs`
  (Use-only actions from the Mini Remote work), with their host hooks
  (`InventoryModel.Source/UseSender/Busy/Replies/ReadItem/PlayerId`) and
  `RynthChatHookup.BuildInventory`.
- The `InventoryFace` title stub in `DashboardStandIns.cs`.
- Their tests move to the real model (see Testing); the Mini Remote and Item Count keep working
  through the real model's `Request` / `Current`.

## Stand-ins and host work

All stand-ins live in `src/AcDream.Rynth.EngineUi/StandIns/` (or extend the existing classes
there); host delegates are set by `RynthChatHookup` and unset on Dispose, each call wrapped in its
existing `Guard`.

1. **`ClientObjectHooks`** (extend):
   - `TryReadItemFields(id, out ItemPwdFields)` from `RynthObjectInfo` (Wcid, IconId, ContainerId,
     WielderId, ValidLocations, EquipLocation → Location, ItemType, Value, Useability, StackSize,
     StackSizeMax, Bitfield, SpellId, ItemsCapacity, Burden, MaterialType, Workmanship). Fields
     `RynthObjectInfo` lacks today (ValidLocations, Useability: check the object's PWD fields in
     `ClientObject`) are added to `RynthObjectInfo` and filled by `RuntimeRynthGameView`.
   - `TryGetObjectIntProperty(id, stype, out int)` from the object's int table (player included:
     EncumbVal 5, CoinValue 20, AugCarry 230, Aetheria 322, ContainersCapacity 7, AugExtraPackSlot
     229, item mana 107/108).
   - `TryGetObjectAttribute(id, attr, kind, out uint)`: the player's current attribute (Strength)
     from the same source the Skills panel's `PlayerProgress` reads; false for other objects.
   - `CollectOwnedIdsFromSnapshot(container, Span<uint>)`: `IRynthGameView.GetContainerContents`
     (already includes the player's worn items) copied into the span; returns the count.
2. **`ClientHelperHooks`** (extend), all game thread, each `Has*` true:
   - `UseObject(id)` → the same host path as RynthAi's slot (wieldables through Equip).
   - `PutItemInContainerAt(item, container, slot)` → `IRynthGameView.MoveItem(item, container, slot, 0)`.
   - `MoveItemInternal(item, container, slot, amount)` → `MoveItem(item, container, slot, amount)`.
   - `SplitStackInternal(item, container, slot, amount)` → `MoveItem(item, container, slot, amount)`.
   - `IsOwnedContainerFull(container)` → `TryCountContents` items against the pack's `ItemsCapacity`.
   - `WieldItem(item, mask)` → `IRynthGameView.WieldItem`.
   - `DropItem(item)` → `MoveItemExternal(item, 0, 0)`.
   - `GiveObjectTo(item, target, amount)` → `GiveItem(item, target, amount)`.
3. **`ClientActionGates`** (new stand-in): OpenAC's `InventoryTransactionState` keeps the same
   one pending request acclient's slot does. `IsRequestGateAvailable` true while the host is bound;
   `TryReadPending(type, obj, age)` from the pending request: kind mapped to acclient's numbers
   (Merge 1, SplitToContainer/SplitToWorld 2, Move 3, Pickup 4, PutInContainer 5, DropToWorld 6,
   Wield 7, Give 9, Shop 10), object = its item, age from when it was reserved (−1 if unknown);
   `TryReadAttacking` from the client's attack-in-progress state (false when none is tracked);
   their `RequestTypeName` texts copied verbatim. New `IRynthGameView` member:
   `bool TryGetPendingItemRequest(out uint acType, out uint objectId, out double ageSeconds)`
   (and `bool IsAttacking` if the client tracks it; else the stand-in answers false).
4. **`SmartBoxHooks`** (new stand-in): `GetUseDoneSeq`, `TryGetLastUseDone(out seq, out err)`,
   `TryGetLastWeenieError(out seq, out err, out evt, out obj)` from `ServerReplySnapshot`
   (it already carries event type and object id).
5. **`AppraisalHooks`** / **`ClientActionHooks`** (new / extend): `HasAppraisalData` (Identified),
   `GetObjectSpellIds` (AppraisedSpellIds into the buffer), `TryGetCachedInt/Bool/DoubleProperty`
   (Ints / Bools / Floats); `ClientActionHooks.RequestId(id)` → `IRynthGameView.RequestIdentify`.
   `Net.EngineSendMarker.Enter(...)` returns a no-op disposable; `Net.IdRequester.Ui` exists.
6. **`Net.InventoryOrder`** (stand-in; theirs parses raw server messages and OpenAC has no raw
   tap): `TryGetOrder(container, slotKind, out ids)` from OpenAC's per-item `ContainerSlot`, items
   (`slotKind` false) and pack-slot things (side packs, foci; true) apart, as ACE orders them;
   `Version` bumps when a container's order changes. A container with no known slots → false
   (their fallback: first-empty-slot move). New `IRynthGameView` member returning a container's
   contents with slots (or `ContainerSlot` on `RynthObjectInfo`).
7. **Change feed**: the host calls `InventoryModel.MarkDirty()` when the client's object table
   reports an item moved, added, removed or restacked (RynthCore calls it from its net hooks).
8. **Classic art**: `IClientOverlayIcons` gains `IntPtr Upload(byte[] rgba, int w, int h, string name)`
   and `bool Release(IntPtr texture)` over `IPluginImageBackend.UploadOwned/ReleaseOwned` (issued
   ids recognised by `IsIssued`); `DX9Backend.CreateTextureRgba/ReleaseTexture` stand-in calls them
   (device argument ignored). The panel host calls `RetailSprites.BeginFrame(IntPtr.Zero)` before
   the faces draw and `RetailSprites.Shutdown()` when the overlay goes. `EngineThreads.Stopping`
   stand-in reads the host's stopping flag. Host-internal API: no plugin contract bump.
9. **Small stand-ins**: `IconSampling.Begin/End` no-op; `ItemDragBridge.NotePopOutDrag/EndPopOutDrag`
   no-op; `UiDropTargets.EngineDragObject` (settable, unused) and `TryClaim` false;
   `ScriptIcons.TryGetItem` false (only the hidden Vault uses it); `AcMainThreadQueue.EnqueueSetSelectedObject`
   → `IRynthGameView.SelectObject` on the game thread; `RynthLog.Compat` → the host log;
   `ChatCallbackHooks`, `ChatCommandDispatcher`, `LogoutLifecycleHooks`, `ChatOrigin` as far as
   `VaultFeed` / `AelrynthVaultWire` need them, inert.
10. **Ticks**: `RynthAiDashboard.Tick` keeps calling `InventoryModel.MainThreadTick()` (now theirs;
    `MainThreadGuard` stand-in already answers the game thread). `ResetActions` is gone: their
    model clears itself when the player id is 0 or changes.
11. **Entry points**: `InventoryFace.Register()` beside the other faces; `/rc inventory` added to
    `RynthCoreChatCommandsStandIn` with RynthCore's texts (toggle via `PanelRouter`); the dashboard
    menu item and the Mini Remote Inventory section use the real face.

## Behaviour notes

- Defaults are theirs: 302×426 chromeless, Classic look first, settings in
  `LocalApplicationData/RynthCore/inventory_settings.txt`; window place follows the per-display layout.
- No drags between the Rynth inventory and OpenAC's own windows or the 3D world; drags inside the
  face (onto a pack, a paperdoll slot, Drop, Give) work.
- Vault never shows (not Aelrynth).

## Testing

- Unit (`tests/AcDream.Rynth.Host.Tests`), against the fake game view:
  - item fields / int properties / attribute / owned ids answered as specified; burden and pyreals
    in the published view;
  - each action kind reaches the right `IRynthGameView` call with the right arguments; refusals
    and results use their texts (InventoryServerSaveFailed for the item → "Refused: …");
  - gate: a pending OpenAC request holds the action ("Still waiting for the game: …"), released
    when it clears, dropped after 10 s; move recorded in the slot and answered;
  - batch: three drops run one after another, Stop cancels;
  - identify: hover 0.3 s sends one `RequestIdentify`, throttled;
  - `InventoryOrder` stand-in: order from slots, items and packs apart, version bumps;
  - texture upload/release through the overlay icons; `RetailSprites` gets a texture for a known id
    with the test dats when present;
  - Mini Remote and Item Count tests moved to the real model still pass.
- Existing `Rynth.Host.Tests` (355) stay green.
- Live check (user, when available): `/rc inventory` both looks; burden/pyreals; move to a side pack
  and to a slot; split; wield via drag to a paperdoll slot; drop; give to an NPC; batch drop with
  Stop; hover identify tooltip with spells/mana; Mini Remote Inventory section; Item Count.
