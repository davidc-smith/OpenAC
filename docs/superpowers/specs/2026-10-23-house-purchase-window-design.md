# House Purchase and Maintenance Window — Design

Date: 2026-10-23. Upstream issue eriknihlen/OpenAC #93 (Housing), gap 2.
Gap 1 (owner entry) shipped as upstream PR 242.

## Problem

Using a slumlord (a house's sale or maintenance crystal) does nothing in
OpenAC. The server answers the use with 0x021D HouseProfile, but OpenAC only
has the event ID and nothing parses it. The client also cannot buy a house
(0x021C BuyHouse) or pay maintenance (0x0221 RentHouse).

## Decision

Build the retail window from LayoutDesc 0x2100000E, the same way Salvage
(0x2100000C) and Secure Trade (0x2100000D) are built. The player pays by
dragging items onto the window's item strip, as in retail. The Buy or
Maintenance button sends only the staged items. Approach A: a separate
`RuntimeHousePurchaseState` in AcDream.Runtime and a
`HousePurchaseUiController` in AcDream.App.

Rejected:

- Folding this into `RuntimeHouseState`. That type holds the player's own
  house (0x0225-0x0228), but a profile can belong to any house the player
  clicks.
- Keeping the staged items in the controller only. Headless and plugins could
  not see the profile, and it would break the runtime/UI split.

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
list buy                  u32 count, then count x HousePayment
list rent                 u32 count, then count x HousePayment

HousePayment (the existing GameEvents.HousePayment record):
i32  num                  quantity required
i32  paid                 quantity already paid
u32  wcid                 273 = pyreals (trade notes count towards it)
str16L name
str16L pluralName
```

`GameEvents.ParseHouseProfile` returns a `HouseProfile` record or null. It
reuses the existing `ReadHousePaymentList`. It returns null for truncated
input, including a truncated payment list, so a partial window is never
shown. `GameEventWiring` gains an `onHouseProfile` handler.

### 0x021C BuyHouse / 0x0221 RentHouse (outgoing)

`ClientCommandRequests.BuildBuyHouse(sequence, slumlordGuid, itemGuids)` and
`BuildRentHouse(...)` build `u32 slumlordGuid, u32 count, count x u32 guid`
after the usual game-action header. `WorldSession` gains `SendBuyHouse` and
`SendRentHouse`. ACE takes only what is still owed from the items sent,
smallest stacks first.

The UI publishes `BuyHouseRuntimeCmd` / `RentHouseRuntimeCmd` on the command
bus. `LiveSessionCommandRouter` routes them to two new optional
`LiveSessionCommandBindings` members, `BuyHouse` and `RentHouse`, which
`LiveSessionCommandBindingFactory` binds to the `WorldSession` sends. This is
the same path the trade window's commands take.

### Out of scope on the wire

0x0259 HouseTransaction: ACE never sends it, and nothing shows what retail
did with it.

## Runtime: `RuntimeHousePurchaseState` (AcDream.Runtime.Gameplay)

- It holds the current profile, a Buy staged list and a Rent staged list
  (`HousePurchaseTab.Buy` / `.Rent`). Each list holds an item guid at most
  once. A `Revision` number goes up on every change. A read-only view,
  `IRuntimeHousePurchaseView`, gives a snapshot (revision, `IsOpen`, the
  profile), the staged guids per tab, and the staged amount per payment line.
- `ApplyProfile(profile)`:
  - From a different slumlord, it replaces the profile and clears both lists.
  - From the same slumlord (the refresh ACE sends after a rent payment), it
    keeps the staged guids that are still in the player's inventory.
  - Either way it opens the state.
- `Stage(tab, guid)` returns `HouseStageResult`. It refuses:
  - when no profile is open (`NotOpen`),
  - the Buy tab when the house has an owner (`AlreadyOwned`),
  - the Rent tab when maintenance is free (`MaintenanceFree`),
  - an item that is not in the player's inventory (main pack or side packs;
    `NotInInventory`),
  - an item already staged on that tab (`AlreadyStaged`),
  - an item whose wcid matches no line on that tab (`NotRequired`; trade
    notes match a wcid 273 line),
  - an item whose line is already covered by `paid` plus the items already
    staged on that tab (`AlreadyCovered`). Amounts are stack sizes; a trade
    note counts as its value.
- `Unstage(tab, guid)` and `Close()`. `Close()` clears
  everything.
- A staged item that is removed from the object table, or that moves out of
  the player's inventory, is unstaged.
- `GameRuntime` creates it next to `RuntimeHouseState` and exposes
  `HousePurchaseOwner` and `HousePurchase` (the view).
  `RuntimeGenerationReset` closes it in the House reset stage.
  `LiveSocialSessionBindings` gains `HousePurchase`, so the live and headless
  routes feed it HouseProfile events.
- Range: the vendor range logic moves into a shared
  `RuntimeUseRange.ShouldClose(runtime, targetGuid)`. `RuntimeVendorRangeQuery`
  calls it with no change in behaviour. `RuntimeHousePurchaseRangeQuery` calls
  it for the slumlord, and runs in the same frame callback. The state closes
  on teleport, when the slumlord entity is gone, or when the player is out of
  the slumlord's use radius.
- Payment names: `RuntimeHouseState`'s private plural-name rule moves into a
  shared `HousePaymentNames.For(payment)`, so both windows name items the
  same way.

## UI: `HousePurchaseUiController` (AcDream.App.UI.Layout)

Element IDs come from LayoutDesc 0x2100000E. The retail element names are
from the enum names in client_portal.dat:

| Retail name (Buy / Maintenance) | Buy | Maintenance |
|---|---|---|
| HouseBuyTab / HouseMaintenanceTab | 0x1000008E | 0x1000008F |
| HouseBuyPage / HouseMaintenancePage | 0x10000090 | 0x10000097 |
| …RequirementsText, 245x87 | 0x10000091 | 0x10000098 |
| …ItemListFrame, 555x87 | 0x10000092 | 0x10000099 |
| …_HouseOwner, 150x16 | 0x10000093 | 0x1000009A |
| …_Button, 94x22 | 0x10000094 | 0x1000009B |
| …ItemsList, 555x32 (trade cell template) | 0x10000095 | 0x1000009C |
| …_ItemListScroll, 555x16 | 0x10000096 | 0x1000009D |

The root (RootSlumlord_Field) is 0x1000008C, the title bar is 0x1000008D,
and CloseSlumlordButton is 0x1000009E.

Captions that come from the dat (string table 0x23000001) and need no code:
tab 0x1000008E "Buy", tab 0x1000008F "Maintenance", button 0x10000094 "Buy",
button 0x1000009B "Maintenance". `LayoutImporter.Import` already resolves
them.

- Mounting: `RetailUiRuntime.MountHousePurchase()` follows
  `MountSecureTrade()`, with a new `WindowNames.HousePurchase`. Its
  visibility is state-managed (it joins `stateManagedVisibilityWindows`). It
  opens when the state opens and closes when the state closes. The close
  button calls `Close()`.
- Tabs use `RetailTabBinding`. A new slumlord, or a change of owner, selects
  the Maintenance tab when `ownerGuid != 0`, otherwise Buy. Only the selected
  page is visible.
- Owner line: `Owner: <name>`, or `For Sale` when unowned.
- Requirements text, one line each:
  - The house type (Cottage, Villa, Mansion, Apartment).
  - The level limit and/or allegiance-rank limit, when not -1.
  - One line per payment on that tab, `Name: paid / num`. It uses
    `HousePaymentNames.For`, groups digits, and appends `(+N staged)` when
    items are staged on that line.
  - On Maintenance, `maintenanceFree` replaces the payment lines with a
    single line saying maintenance is free.
- The button is greyed out when nothing is staged on that tab, or when the
  house is owned (Buy), or when maintenance is free (Maintenance). A press
  publishes the command with the slumlord guid and the staged guids. Staged
  items stay until the server's next profile or an inventory change removes
  them.
- Item strip: drops from the inventory call `Stage`. A refusal is reported in
  chat. Dragging a cell off the strip, or right-clicking it, calls `Unstage`.
  Cells show icon and tooltip the same way the salvage strip does, with the
  trade overlay. The scrollbar is bound to the strip like Salvage's.

### Captions to check against retail

These are not in the dats or ACE. Until the user checks a retail client
(planned 2026-10-23/24 on their Windows PC), they are placeholders, kept in
one class, `HousePurchaseText`:

| Where | Placeholder |
|---|---|
| Owner line | "Owner: {name}", "For Sale" |
| House type line | "Cottage", "Villa", "Mansion", "Apartment" |
| Limits | "Requires level {min}+", "Requires level {min} to {max}", "Requires allegiance rank {min}+" |
| Payment line | "{name}: {paid} / {num}" and " (+{n} staged)" |
| Maintenance free | "Maintenance is free this period." |
| Refusals in chat | one sentence per `HouseStageResult` |

## Errors

- A profile that fails to parse is ignored.
- Server refusals (level, already own a house, already paid, must wait) come
  as WeenieError or chat. OpenAC already shows both. The window does not
  predict results; it waits for the next profile.
- If nothing is staged, the button does nothing.

## Testing

TDD. Tests run through `~/code/OpenAC/test-baseline/run-mac-tests.sh`.

- Parser (`HouseEventsTests`, using `AceWireWriter`): unowned with empty
  lists, owned with a name and both lists, limits set, truncated in the
  header, truncated mid-payment.
- Builders: exact bytes for 0x021C and 0x0221, including an empty guid list.
- Router: the two commands reach the bindings.
- State: each staging refusal, trade notes towards pyreals, line coverage by
  staged stacks, a profile from a different slumlord clearing the lists, a
  same-slumlord refresh keeping them, an item leaving inventory, closing, and
  the revision going up on every change.
- Range: in range, out of range, slumlord gone, teleport. The existing
  vendor range tests keep passing unchanged.
- Text: `HousePurchaseText` lines for each case above.
- Controller, with hand-built layouts as in `SalvageUiControllerTests`:
  choosing the tab, the texts, the button's greyed-out states, drop to stage,
  drag off to unstage, and the button press publishing the staged guids.
- Installed dats (`[InstalledDatFact]`): importing 0x2100000E gives the
  element types the controller binds to, and the dat captions "Buy" and
  "Maintenance".

## Live check

This needs the user to say they are available. Local ACE only (10.10.20.20),
never Dreamweave.

1. Appraise a cottage crystal (`@tele 20.3s 78.1e`) and use the slumlord.
   The Buy tab opens with the requirements.
2. Stage pyreals or trade notes and the other buy items, then press Buy. The
   house is owned and the deed arrives.
3. Use the slumlord again. It opens on Maintenance.
4. Stage part of the rent and press Maintenance. The profile refreshes and
   shows the new paid amounts.
5. Walk out of range. The window closes.

## Delivery

- This spec lives on the fork-only `docs/house-purchase-spec` branch.
- The code goes on `feat/house-purchase-window`, cut from `upstream-main`,
  merged into fork `main` (and `rynth`, per the gateway split), and pushed.
- One upstream PR, marked "Part of #93".

## Out of scope

0x0259 HouseTransaction, hook items not rendering (gap 3),
`@house guest list` / 0x0257 (gap 4), the house panel text wrapping (gap 5),
and the outdoor barrier (gap 6).
