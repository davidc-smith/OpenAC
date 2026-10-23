# House Purchase and Maintenance Window — Design

Date: 2026-10-23. Upstream issue eriknihlen/OpenAC #93 (Housing), gap 2.
Gap 1 (owner entry) shipped as upstream PR 242.

## Problem

Using a slumlord (a house's sale or maintenance crystal) does nothing in
OpenAC. The server answers the use with 0x021D HouseProfile, but OpenAC only
has the event ID. Nothing parses it, and a dispatcher test asserts that it is
unhandled. The client cannot buy a house (0x021C BuyHouse) or pay maintenance
(0x0221 RentHouse), and 0x0259 HouseTransaction is ignored as well.

## Decision

Build the retail window from LayoutDesc 0x2100000E, the same way Salvage
(0x2100000C) and Secure Trade (0x2100000D) are built. The player pays by
dragging items onto the window's item strip, as in retail. Buy or Pay sends
only the staged items. Approach A: a separate `RuntimeHousePurchaseState` in
AcDream.Runtime and a `HousePurchaseUiController` in AcDream.App.

Rejected:

- Folding this into `RuntimeHouseState`. That type holds the player's own
  house (0x0225-0x0228), but a profile can belong to any house the player
  clicks.
- Keeping the staged items in the controller only. Headless and plugins could
  not see the profile, and it would break the runtime/UI split that Trade and
  Vendor follow.

## Wire (AcDream.Core.Net)

The formats come from ACE (`GameEventHouseProfile`, `HouseProfile.Write`,
`HousePayment.Write`, `GameActionHouseBuyHouse`, `GameActionHouseRentHouse`).

### 0x021D HouseProfile (incoming)

```
u32  slumlordGuid
u32  dwellingId
u32  ownerGuid            0 = unowned
u32  bitmask              HouseBitfield
i32  minLevel             -1 = none
i32  maxLevel             -1 = none
i32  minAllegianceRank    -1 = none
i32  maxAllegianceRank    -1 = none
u32  maintenanceFree      0/1
u32  houseType            1 cottage, 2 villa, 3 mansion, 4 apartment
str16L ownerName
list buy                  i32 count, then count x HousePayment
list rent                 i32 count, then count x HousePayment

HousePayment:
i32  num                  quantity required
i32  paid                 quantity already paid
u32  wcid                 273 = pyreals (trade notes count towards it)
str16L name
str16L pluralName
```

`GameEvents.ParseHouseProfile` returns a `HouseProfile` record (with a
`HousePayment` record list) or null. It returns null for truncated input,
including a truncated payment list, so a partial window is never shown.

### 0x0259 HouseTransaction (incoming)

`u32 noticeType`. ACE always sends 2. It is parsed and shown as a chat notice
only. Nothing is stored.

Both events get handlers in `GameEventWiring`. The test in
`GameEventDispatcherTests` that counts 0x021D as unhandled moves to another
event that is still unhandled.

### 0x021C BuyHouse / 0x0221 RentHouse (outgoing)

`ClientCommandRequests.BuildBuyHouse(sequence, slumlordGuid, itemGuids)` and
`BuildRentHouse(...)` build `u32 slumlordGuid, u32 count, count x u32 guid`.
ACE takes only what is still owed from the items sent, smallest stacks first.

`GameRuntimeCommands` gains `BuyHouse(slumlord, guids)` and
`PayHouseMaintenance(slumlord, guids)`.

## Runtime: `RuntimeHousePurchaseState` (AcDream.Runtime.Gameplay)

- It holds the current profile, a Buy staged list and a Rent staged list. Each
  list holds an item guid at most once. A `Revision` number goes up on every
  change. A read-only view, `IRuntimeHousePurchaseView`, gives a snapshot (the
  profile, `IsOpen`) and the staged guids per tab, in the same shape as
  `IRuntimeTradeView`.
- `ApplyProfile(profile)`:
  - From a different slumlord, it replaces the profile and clears both lists.
  - From the same slumlord (the refresh ACE sends after a rent payment), it
    keeps the staged guids that are still in the player's inventory.
  - Either way it opens the state.
- `Stage(tab, guid)` returns a result with a reason when it refuses. It
  refuses:
  - an item that is not in the player's inventory (main pack or side packs),
  - an item already staged on that tab,
  - an item whose wcid matches no line on that tab with `paid < num`
    (trade notes match a wcid 273 line),
  - an item whose line is already fully covered by `paid` plus the items
    already staged on that tab. Amounts are stack sizes; a trade note counts
    as its value.
- `Unstage(tab, guid)`, `ClearStaged(tab)`, and `Close()`. `Close()` clears
  everything.
- A staged item that leaves the inventory (ClientObjectTable removal or
  container change) is unstaged.
- Range: `RuntimeHousePurchaseRangeQuery.EnforceRange(runtime)` follows
  `RuntimeVendorRangeQuery`. It closes the state on teleport, or when the
  player is out of the slumlord's use radius. It runs in the same
  `GameRuntime` tick as the vendor check.
- `GameRuntime` owns the state, and `RuntimeGenerationReset` resets it.

## UI: `HousePurchaseUiController` (AcDream.App.UI.Layout)

Element IDs come from LayoutDesc 0x2100000E:

| Element | Purchase | Maintenance |
|---|---|---|
| Tab button | 0x1000008E | 0x1000008F |
| Page | 0x10000090 | 0x10000097 |
| Left text, 245x87 | 0x10000091 | 0x10000098 |
| Title, 150x16 | 0x10000093 | 0x1000009A |
| Button, 94x22 | 0x10000094 | 0x1000009B |
| Item strip, 555x32 (trade cell template 0x2100003D/0x10000339) | 0x10000095 | 0x1000009C |
| Bottom line, 555x16 | 0x10000096 | 0x1000009D |

The root is 0x1000008C, the title bar is 0x1000008D, and the close button is
0x1000009E.

- Mounting: `RetailUiRuntime.MountHousePurchase()` follows
  `MountSecureTrade()`, with a new `WindowNames.HousePurchase`. Its
  visibility is state-managed (it joins `stateManagedVisibilityWindows`). It
  opens when the state opens and closes when the state closes. The close
  button calls `Close()`. It shares the bottom slot under the rule Vendor and
  Trade already follow there; no new rule.
- Tabs use `RetailTabBinding`. A profile opens on the Maintenance tab when
  `ownerGuid != 0`, otherwise on Purchase.
- Left text, one line each:
  - The house type and the owner name, or "For sale".
  - The level limit and/or allegiance-rank limit, when not -1.
  - One line per payment on that tab, `Name: paid / num`. It uses
    `pluralName` when `num != 1`, groups digits, and appends
    `(+N staged)` when items are staged.
  - On Maintenance, `maintenanceFree` replaces the payment lines with a
    single line saying maintenance is free.
- Title: "Purchase" / "Maintenance". Button captions: "Buy" / "Pay".
- The button is greyed out when nothing is staged on that tab, or when the
  house is owned (Purchase), or when every rent line is paid or maintenance is
  free (Maintenance). A press sends the command with the staged guids, minus
  any no longer in inventory. Staged items stay until the server's next
  profile or inventory change removes them.
- Item strip: it registers a drop handler like
  `SecureTradeUiController.SelfGridDropHandler`. A drop from inventory calls
  `Stage`. A refusal prints its reason in chat. Dragging a cell off the strip,
  or right-clicking it, calls `Unstage`. Cells show icon, stack and tooltip
  the same way the trade strip does.
- Bottom line: "Drag items here to pay", "Fully paid", or "Maintenance is
  free this period", whichever applies.

### Captions to check against retail

The dats and ACE do not give retail's wording. Until the user checks a
retail client (planned 2026-10-23/24 on their Windows PC), these are
placeholders. Keep them in one place in the controller so they are easy to
change:

| Where | Placeholder |
|---|---|
| Tab titles / page titles | "Purchase", "Maintenance" |
| Buttons | "Buy", "Pay" |
| Unowned house line | "For sale" |
| Bottom line | "Drag items here to pay", "Fully paid", "Maintenance is free this period" |
| Staged suffix | "(+N staged)" |
| Payment line | "Name: paid / num" |

## Errors

- A profile that fails to parse is logged and ignored.
- Server refusals (level, already own a house, already paid, must wait) come
  as WeenieError or chat. OpenAC already shows both. The window does not
  predict results; it waits for the next profile.
- If nothing valid is left to send, nothing is sent.

## Testing

TDD. Tests run through `~/code/OpenAC/test-baseline/run-mac-tests.sh`.

- Parser: byte samples written in ACE's order. Cases: unowned with empty
  lists, owned with a name, both lists filled, limits set, truncated in the
  header, truncated mid-payment. Also the HouseTransaction notice.
- Builders: exact bytes for 0x021C and 0x0221, including an empty guid list.
- State: each staging refusal, trade notes towards pyreals, line coverage by
  staged stacks, a profile from a different slumlord clearing the lists, a
  same-slumlord refresh keeping them, an item leaving inventory, closing on
  range and teleport, and the revision going up on every change.
- Controller, using the same layout-backed fixtures as
  `SecureTradeUiControllerTooltipTests` / `VendorUiControllerTests`:
  choosing the tab, the left text, the button's greyed-out states, drop to
  stage, and the button press sending the staged guids.

## Live check

This needs the user to say they are available. Local ACE only (10.10.20.20),
never Dreamweave.

1. Appraise a cottage crystal (`@tele 20.3s 78.1e`) and use the slumlord.
   The Purchase tab opens with the requirements.
2. Stage pyreals or trade notes and the other buy items, then press Buy. The
   house is owned and the deed arrives.
3. Use the slumlord again. It opens on Maintenance.
4. Stage part of the rent and press Pay. The profile refreshes and shows the
   new paid amounts.
5. Walk out of range. The window closes.

## Delivery

- This spec lives on the fork-only `docs/house-purchase-spec` branch.
- The code goes on `feat/house-purchase-window`, cut from `upstream-main`,
  merged into fork `main` (and `rynth`, per the gateway split), and pushed.
- One upstream PR, marked "Part of #93".

## Out of scope

Hook items not rendering (gap 3), `@house guest list` / 0x0257 (gap 4), the
house panel text wrapping (gap 5), and the outdoor barrier (gap 6).
