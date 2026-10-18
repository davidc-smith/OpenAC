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
- **Loot honours the pack RynthAi names** (as RynthCore does). Target 0 or the player
  falls back to the client's own pack choice.
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
| `MoveItemExternalFn(uint obj, uint target, int amount)` | Target 0 or the player id: `Loot.Pickup(obj)` (client picks the pack). Otherwise the new `Loot.Pickup(obj, target)`, which sends PutItemInContainer to that pack with the existing busy / no-room / not-ours checks; refused when `target` is not the player's own container. `amount` is passed through as the stack amount (0 = whole stack). Returns 1 when sent. |
| `GiveObjectToFn(uint obj, uint target, int amount)` | `Items.Give(obj, target, amount)`; 0 = whole stack. |
| `CloseContainerFn(uint id)` | `Loot.Close(id)`. |
| `GetObjectPalettesFn(uint id, uint* subIds, uint* offsets, int max)` | Copies `PluginInventoryItem.Palettes` (sub-palette id, offset) up to `max`; returns the count, 0 for an unknown object or none. |
| `GetNumContainedItemsFn(uint id)` / `GetNumContainedContainersFn(uint id)` | Count the known contents of the container (items that are not containers / containers). Unknown container: 0. |

### Salvage (`RynthSalvagePanel` + `RynthApiTable.Salvage.cs`)

| Slot | Implementation |
|---|---|
| `SalvagePanelOpenFn(uint tool)` | Accepts when `tool` is a tinkering tool the player owns; records it and empties the list. Returns 1. |
| `SalvagePanelAddItemFn(uint item)` | Adds the id when the panel is open, the item is owned, not the tool and not already listed; at most 100 ids (`MaxSalvageDirectItems`). Returns 1 when added (or already listed). |
| `SalvagePanelExecuteFn()` | `Items.Salvage(tool, ids)`, then empties the list (the tool stays). 0 when the list is empty or the send is refused. |
| `SalvageDirectFn(uint tool, uint* ids, int n)` | `Items.Salvage(tool, ids)` directly; RynthCore's rules (1..100 ids, none zero, none the tool). Not used by RynthAi; filled because it is the same call. |

The panel resets on logoff.

### Vendor (`RynthVendorTrades` + `RynthApiTable.Vendor.cs`)

| Slot | Implementation |
|---|---|
| `GetVendorInfoFn(VendorInfoNative*)` | From `IVendorAutomation` (`IsOpen`, `VendorObjectId`, `VendorName`, `Profile`, `Items.Count`) plus the player's coins and alt-currency count from inventory. `Generation` +1 per vendor open. Flags: trading available, trade in flight. 0 when no vendor is open. |
| `GetVendorItemsFn(VendorItemNative*, int max)` | `IVendorAutomation.Items` projected (unit price from the profile's sell rate, unlimited stock flag for ACE stock 0, stackable, needs-container-slot); returns the count. |
| `VendorBuyFn(uint vendor, VendorTradeEntryNative*, int n)` | Vendor 0 = the open vendor; any other id must match it. `ClearBuyList`, `AddToBuyList` per entry, `BuyAll`. Returns a new request id (non-zero) when sent, 0 otherwise. |
| `VendorSellFn(uint vendor, uint* ids, int n)` | Same with the sell list and `SellAll`. |
| `GetVendorTradeStatusFn(VendorTradeStatusNative*)` | The latest request: Sent once `BuyAll`/`SellAll` accepts; Done + Accepted on the next vendor refresh (`VendorState.Changed` Refreshed); Refused when a WeenieError arrives in the reply log while it is out; Done + TimedOut after 5 s. Message carries the surface notice or the error text. |

Events: `OnVendorOpen(vendorId)` / `OnVendorClose(vendorId)` are raised from
`VendorState.Changed` (Opened / Closed) through the per-type event queue, in
RynthCore's drain order. A vendor close or logoff finishes any outstanding request as
Refused.

### Trade (`RynthApiTable.Trade.cs`)

| Slot | Implementation |
|---|---|
| `GetTradeStateFn(TradeStateNative*)` | Projects `ITradeAutomation` + `Events`: Flags (open, you accepted, partner accepted, watching = 1, actions available = 1), Generation, Sequence, Partner, Initiator, item counts, LastEventType, failure fields, close reason, last accepted / declined / reset by, completed count, partner accept count. Honours the caller's `Size`; returns the number of bytes written (≥ 8). |
| `GetTradeItemsFn(int side, uint* ids, int cap)` | Side 1 = `MyItems`, 2 = `PartnerItems`; returns the count. |
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
  `RuntimeTradeState` updates them in its existing `Apply*` methods: Register
  (Generation, Initiator), Add, Remove, Accept, Decline, Reset, Close (reason),
  Failure, ClearAcceptance; every one bumps Sequence. CompletedCount counts the
  server's "Trade Complete!" notice (the plan pins the exact message from ACE).
  Counters survive a trade closing; they reset on logoff.
- `ILootAutomation.Pickup(uint objectId, uint targetContainerObjectId)` → existing
  pickup checks, then `SendPutItemInContainer` to the named container.
- `ForkContractVersion` → `0.1.25-fork.9`; `docs/plugin-api.md` documents the new
  members.

## Testing

Unit tests, no live client:

- Loot: pack fallback (0 / player / own pack / foreign container); give; close;
  palettes copy and truncation; contained counts.
- Salvage: open with a non-tool / unowned tool refused; dedupe; tool excluded; 100
  cap; execute sends once and empties; reset on logoff; SalvageDirect rules.
- Vendor: native struct layouts and string copies; info/items projection; buy/sell
  with vendor 0 / matching / other id; each status transition (refresh, WeenieError,
  timeout, vendor close); open/close events through the queue.
- Trade: state projection and `Size` handling; item sides; actions mapped.
- Runtime: trade counters per message, two events in one frame, reset on logoff;
  `Open` refusals; `Pickup(id, target)` sends to the named pack.
- `RynthApiTableTests.DeferredSlotsStayUnset` updated: these slots are now set.

Live check on local ACE (with the user, later): loot a corpse into a named pack with a
VTank colour rule; UST salvage of a few items; AutoVendor sell and buy at a Holtburg
vendor; a trade between two test characters (open, add, accept, complete); `/mt givep`.
