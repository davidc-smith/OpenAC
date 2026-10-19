# RynthAi #5: loot, salvage, vendor and trade — design

Date: 2026-10-19. Status: approved.

## Goal

RynthAi's item automation works in OpenAC: corpse looting into the pack it chooses,
VTank colour rules, closing corpses, UST salvage, AutoVendor buy/sell, AutoTrade and
the `/mt` and `/ra` give commands.

This is sub-project #5 of the RynthAi split (#1 dashboard, #2 state and actions,
#3 combat and buffs, #4 navigation and Nav3D, #6 dat raycast and pathfinder).

## Decisions

- **Their code, unchanged.** As in #1–#4, RynthAi compiles from the submodule as-is;
  everything here is host slots in `AcDream.Rynth.Host`, plus surface members where
  the client lacks one.
- **One sub-project** covering loot, salvage, vendor, trade and give (user, 2026-10-19).
- **Loot honours the pack RynthAi names** when it has room, and falls back to the
  client's own choice when it is full (as a retail drag onto a full pack does). Target
  0 or the player uses the client's own choice.
- **Salvage panel state lives in the host.** OpenAC's retail salvage panel
  (`App/UI/Layout/SalvageUiController`) is not driven; Execute sends one salvage
  request through `Items.Salvage`.
- **Trade event counters are kept by the client** as each trade message arrives, so
  two events in one frame are both counted. The host never infers events by diffing.
- **Contract `0.1.25-fork.9`:** `ITradeAutomation.Open`, `ITradeAutomation.Events`,
  `ILootAutomation.Pickup(id, targetContainer)`. All default interface members.
- All gated slots pass their version checks already (`ApiVersion` 86).

## Slots

As in #2–#4, the table only marshals arguments and catches exceptions; the work is
on `IRynthGameView` / `RuntimeRynthGameView` (or a small host-side class where the
state is RynthCore's, not the client's). Every slot runs on the game thread and
answers 0 (or does nothing) when the player is not in the world or the surface
refuses. A throw becomes the usual "threw" log line and answers 0.

### Loot and give (`RynthApiTable.Loot.cs`)

| Slot | Implementation |
|---|---|
| `MoveItemExternalFn(uint obj, uint target, int amount)` | RynthAi's general move (loot at `LootPickup` / `OwnCorpse`, `/mt drop`, `/ra give` without GiveObjectTo, and the meta `give` actions with the stack size as the amount), so `RuntimeRynthGameView.MoveItemExternal` picks the move from the objects. An item the player does not own is looted, whole stack (the amount is ignored): target 0 or the player → `Loot.Pickup(obj)` (client picks the pack), otherwise the new `Loot.Pickup(obj, target)`: the same checks and placement as today's pickup, with `target` as the preferred pack. The client's placement search (`InventoryPlacementSearch.ChooseContainer`) uses it when it has room and otherwise falls back to the main pack, then the other packs, as a retail drag onto a full pack does; a stack the player already holds is still joined first. The pickup refuses an item that is not in the open container and a `target` that is not a container the player owns. An owned item: target 0 → `Items.Drop(obj, amount)`; the player or a pack the player owns → `Items.MoveToContainer(obj, target, amount)`; any other known object the player does not own → `Items.Give(obj, target, amount)`; anything else answers 0. Amount 0 is the whole stack; negative answers 0. Returns 1 when sent. |
| `GiveObjectToFn(uint obj, uint target, int amount)` | `Items.Give(obj, target, amount)`; 0 = whole stack, negative answers 0. |
| `CloseContainerFn(uint id)` | `Loot.Close(id)`. |
| `GetObjectPalettesFn(uint id, uint* subIds, uint* offsets, int max)` | From the object's CreateObject sub-palette swaps, in offset order: `subIds[i]` = sub-palette id, `offsets[i]` = its offset as a colour index (the wire's blocks of eight × 8), as RynthCore's `PaletteCache` reads the client's unpacked `Subpalette.offset`. An object the client knows but has no world entity for answers 0. Fills up to `max`, returns the total count (may exceed `max`), -1 when the object is unknown (RynthCore's "no data captured"). |
| `GetNumContainedItemsFn(uint id)` / `GetNumContainedContainersFn(uint id)` | The container's direct contents that take an item slot / a container slot, split as the client's placement search does (`InventoryContainerPlacementPolicy.IsContainer`: packs and spell foci take container slots); worn items are not contents. -1 when the container is unknown, as RynthCore. |

### Salvage (`RynthSalvagePanel` + `RynthApiTable.Salvage.cs`)

| Slot | Implementation |
|---|---|
| `SalvagePanelOpenFn(uint tool)` | Accepts when `tool` is a tinkering tool the player owns; records it and empties the list. Returns 1. |
| `SalvagePanelAddItemFn(uint item)` | Adds the id when the panel is open, the item is owned, not the tool and not already listed; at most 100 ids (`MaxSalvageDirectItems`). Returns 1 when added (or already listed). |
| `SalvagePanelExecuteFn()` | `Items.Salvage(tool, ids)`, then empties the list (the tool stays). 0 when the list is empty or the send is refused. |
| `SalvageDirectFn(uint tool, uint* ids, int n)` | `Items.Salvage(tool, ids)` directly; RynthCore's rules (1..100 ids, none zero, none the tool). Not used by RynthAi; filled because it is the same call. |

RynthAi never calls `SalvagePanelOpenFn`: its `SalvageManager` opens the panel by
using the Ust (`UseFor` → `UseObjectFn`), as a retail double-click does. So
`UseObjectFn` on a tinkering tool the player owns is the same panel open (recorded,
list emptied, 1) and sends nothing to the server, as using a Ust in retail only opens
the panel. RynthAi does not wait for a UseDone after it (`RecordUseForPacing` skips
Salvage uses). Any other object is an ordinary use.

The panel resets on logoff.

### Vendor (`RynthVendorTrades` + `RynthApiTable.Vendor.cs`)

| Slot | Implementation |
|---|---|
| `GetVendorInfoFn(VendorInfoNative*)` | From the client's `VendorState` (id, profile: item types, min/max value, deals magic, buy and sell rates, alt currency wcid / server count / name), the vendor's name, `Items.Count`, the player's coins (int property 20 on the player, -1 unknown) and the alt currency the player holds (-1 when the vendor takes none). `Generation` +1 per vendor open. Flags: 1 trading available (always), 2 a request in flight. 0 when no vendor is open. |
| `GetVendorItemsFn(VendorItemNative*, int max)` | `VendorState.Items` projected as RynthCore does: `Amount` -1 for unlimited stock (ACE stock 0), stack size, max stack, value and unit value, unit price (`IVendorAutomation.Items[i].UnitPrice`, the client's own vendor-window price), burden, icon; flags 1 unlimited, 2 stackable (max stack > 0), 4 needs a container slot (a container listing). Fills up to `max`; returns the total, -1 when no vendor is open. |
| `VendorBuyFn(uint vendor, VendorTradeEntryNative*, int n)` | RynthCore's checks (vendor 0 = the open one, any other id must match it; 1..100 lines; no id twice; each id on the list; amount 1..0x7FFFFF and within limited stock), then `ClearBuyList`, `AddToBuyList` per entry, `BuyAll`. Returns the new request id; a refusal (a check, or the surface saying no) returns 0 and sets the status to Refused with the reason. |
| `VendorSellFn(uint vendor, uint* ids, int n)` | Same checks (no id 0, none the vendor's own, none twice), then the sell list and `SellAll`. |
| `GetVendorTradeStatusFn(VendorTradeStatusNative*)` | The most recently updated request, RynthCore's states: Sent (2) once `BuyAll`/`SellAll` accepts; Done (3) + Accepted (1) when `IVendorAutomation.TransactionCompleted` reports success; Done + AnsweredNoRefresh (2) with the surface's notice when it reports failure; Done + TimedOut (3) after 10 s with no answer; Refused (4) for a request that never went out. 0 before the first request. |

Events: `OnVendorOpen(vendorId)` / `OnVendorClose(vendorId)` are raised from
`VendorState.Changed` through the per-type event queue, drained after
StopViewingContents and before UpdateInventory as RynthCore does. As RynthCore's
OpenVendor hook, an open is raised for Opened and for Refreshed (the server re-sent
the list, including after a buy or sell); an Opened that replaces another vendor
raises the previous vendor's close first; Closed and Reset raise a close. A request
already sent stays in flight when the vendor closes (the server still answers it),
and logoff clears it.

### Trade (`RynthApiTable.Trade.cs`)

| Slot | Implementation |
|---|---|
| `GetTradeStateFn(TradeStateNative*)` | Projects `ITradeAutomation` + `Events`: Flags (open, you accepted, partner accepted, watching = 1, actions available = 1), Generation, Sequence, Partner, Initiator, item counts, LastEventType, failure fields, close reason, last accepted / declined / reset by, completed count, partner accept count. Honours the caller's `Size` (under 8 answers 0); writes at most `sizeof(TradeStateNative)`, sets `Size` to that and returns it. |
| `GetTradeItemsFn(int side, uint* ids, int cap)` | Side 1 = `MyItems`, 2 = `PartnerItems`; fills up to `cap`, returns that side's total, -1 for any other side. |
| `TradeOpenFn(uint target)` | New `ITradeAutomation.Open(target)`. |
| `TradeAddFn(uint item, uint slot)` | `Add(item)`; `slot` is ignored (the server places it). |
| `TradeAcceptFn` / `TradeDeclineFn` / `TradeResetFn` / `TradeCloseFn` | `Accept` / `Decline` / `Reset` / `End`. |

## Client changes (contract `0.1.25-fork.9`)

- `ITradeAutomation.Open(uint partnerObjectId)` → `WorldSession.SendOpenTradeNegotiations`
  through a new runtime binding. Refused when not in the world, a trade is already
  open, or the partner is 0 or the player.
- `ITradeAutomation.Events` → `PluginTradeEventCounters`: Generation, Sequence,
  InitiatorId, LastEventType (the GameEvent opcode, `0x01FD`–`0x0208`), FailureCount,
  LastFailureItemId, LastFailureReason, LastCloseReason, LastAcceptedBy,
  LastDeclinedBy, LastResetBy, CompletedCount, PartnerAcceptCount.
  `RuntimeTradeState` updates them in its existing `Apply*` methods with RynthCore's
  rules (`Compatibility/PlayerTrade.cs`): Register (Generation, Initiator, close
  reason back to 0), Add, Remove, Accept (PartnerAcceptCount when it is not the
  player), Decline, Reset (who), Close (reason, from the CloseTrade payload the router
  now passes on; Initiator back to 0), Failure,
  ClearAcceptance; every one bumps Sequence and sets LastEventType, whether or not a
  trade is open. CompletedCount (+ Sequence) counts WeenieError `0x0529`
  TradeComplete, seen through a new `ServerReplyLog.WeenieErrorRecorded` event. Like
  RynthCore's, the counts only ever grow; plugins compare them between reads.
- `ILootAutomation.Pickup(uint objectId, uint preferredContainerObjectId)` → the
  existing pickup, with `RuntimeItemInteraction.TryPlaceWorldItemInBackpack` taking
  the preferred pack as the placement search's target.
- `ForkContractVersion` → `0.1.25-fork.9`; `docs/plugin-api.md` documents the new
  members.

## Testing

Unit tests, no live client:

- Loot: pack fallback (0 / player / own pack / foreign container); give; close;
  palettes copy and truncation; contained counts.
- Salvage: open with a non-tool / unowned tool refused; dedupe; tool excluded; 100
  cap; execute sends once and empties; reset on logoff; SalvageDirect rules.
- Vendor: native struct layouts and string copies; info/items projection; buy/sell
  checks with vendor 0 / matching / other id; each status transition (success,
  failure, timeout, refused); open/close events (Opened, Refreshed, switch, Closed,
  Reset) in drain order.
- Trade: state projection and `Size` handling; item sides; actions mapped.
- Runtime: trade counters per message, two events in one frame, TradeComplete;
  `Open` refusals; `Pickup(id, pack)` prefers the named pack and falls back when it
  is full.
- `RynthApiTableTests.DeferredSlotsStayUnset` updated: these slots are now set.

Live check on local ACE (with the user, later): loot a corpse into a named pack with a
VTank colour rule; UST salvage of a few items; AutoVendor sell and buy at a Holtburg
vendor; a trade between two test characters (open, add, accept, complete); `/mt givep`.
