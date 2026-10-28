# Gear Deck design

Date: 2026-10-28
Status: brainstorm agreed; sections 2-7 written from recommended defaults after the user asked to go on to the plan
Scope: fork-only Modern HUD addition (no upstream PR)

## Summary

The Gear Deck is the Modern HUD replacement for the retail Inventory window and its paper doll. It is one column the width of the retail panel (300 points) that grows downwards. At the top is an Equipment section: the live 3D figure with slots round it, or a compact grid of every slot, and it folds to a single row of worn items. Below it are your packs, as a grid or a list, with the side packs in a column down the right edge, as in retail. A query bar filters and sorts across every pack: type "steel", pick Type: Salvage and sort by Workmanship, best first, and the matches from all packs come back as one flat result. Organising tools combine stacks, split by amount, tidy a pack on the server, send items to packs by rule, and lock items so they can't be sold, given, dropped or salvaged by mistake.

Salvaging and tinkering stay in the retail windows. The Gear Deck only gathers items for them: with the retail salvage panel open, "Add N to salvage" sends every item the filter shows into it.

Mockups: `.superpowers/brainstorm/gear-deck/layout.html` (local), also published at https://claude.ai/artifact/FsrgBsbcVgoDstcL2Ha2pj (option C).

## Decisions

| Topic | Decision |
|---|---|
| Shape | "C": one 300-point column; the Equipment section folds; packs fill the rest. Vertical like retail, never wide |
| Equipment views | Figure (3D model + slot ring, drag to turn, scroll to zoom) or Slots (labelled 6-column grid of every slot); folded = one row of worn items, still drop targets |
| Equipment interaction | Drop on the figure picks the slot (`PaperdollClickMap`); hovering a slot highlights pack items that fit it; double-click a pack item wears it, a worn item takes it off |
| Pack views | Grid (retail-style icons) or List (name + columns that follow the sort and filters); multi-select and drag a selection in both |
| Query | Chips + free text + sort with direction; saved filters per character |
| Results | A non-empty query shows flat results across all packs, each with a pack badge; an empty query shows the normal pack view |
| Data | Only what create messages carry (name, type, material, workmanship, value, burden, stack). No ID requests. ID-based filters later |
| Organising | Combine stacks (button + optional on arrival), split by amount, tidy a pack on the server, pack rules, item locks |
| Salvage / tinker | Retail windows unchanged; "Add N to salvage" feeds the open retail salvage panel |
| Outside containers | A separate Loot window, its own spec later; until then the retail container window is unchanged |
| Item detail | The retail examine window, unchanged |
| Look | Deck chrome (`DeckChrome`, `DeckFonts`); never follows the plugin theme |
| Retail UI | With Modern HUD on, the Gear Deck replaces `WindowNames.Inventory` (inventory + paper doll); with it off, retail is untouched |

## Architecture

The same hybrid as the Chat Deck. A native deck window draws the column; deck-look markup popups handle the occasional dialogs. The rules live in pure Core code so they can be tested without a client.

### Core (`src/AcDream.Core/Items/Gear/`): pure, no UI

- **`GearItemView`**: a read-only projection of one `ClientObject`: id, name, friendly type group, `ItemType`, material, workmanship, value, burden, stack size and max, pack id and slot, worn location, locked. Built from `ClientObjectTable` by `GearItemProjector`.
- **`GearTypeGroup`**: the friendly groups the Type chip offers: Salvage, Mana stones, Healing kits, Food, Gems, Scrolls, Keys, Armour, Weapons, Jewellery, Clothing, Containers, Other. Each maps to `ItemType` bits plus a name or weenie-class rule where the type bits are not enough (all salvage bags share one type).
- **`GearQuery`**: a list of `GearChip` plus a `GearSort` (field + direction). Chip fields are Name (contains), Type group (one of), Material (one of), Workmanship, Value, Burden and Stack (min/max), Pack (one of), Worn and Locked (yes or no). Free text that is not a chip is a name-contains match. Chips on the same field are OR'd; different fields are AND'd. Ties sort by name, then pack order.
- **`GearQueryEngine.Run(items, query)`**: returns the ordered matches. `query.IsEmpty` means no filter.
- **`GearChipSuggester`**: turns a typed word into suggested chips ("steel" → Material: Steel, "salv" → Type: Salvage, "work 7" → Workmanship ≥ 7). Materials come from the existing material names.
- **`SavedFilter`**: a name plus a `GearQuery`.
- **`GearPackRule`**: a saved filter, a target pack, and "apply on arrival" on or off.
- **`GearLockSet`**: the per-character set of locked object ids.
- **Organising planners** (pure, return a list of moves, send nothing):
  - `StackCombinePlanner`: merges for every partial stack of the same weenie class across all packs, fullest target first.
  - `PackTidyPlanner`: the moves that reorder one pack by type group, then name, then workmanship (descending).
  - `PackRulePlanner`: the moves that apply every rule (locked items are never moved by a rule).
- **`GearMove`**: `Merge(from, to, amount)`, `Split(stack, amount, container, slot)`, `Put(item, container, slot)`.

### Runtime and network

There are no new messages. Moves go through what exists: `SendPutItemInContainer`, `BuildStackableMerge`, `BuildStackableSplitToContainer`, `BuildGetAndWieldItem`, and the existing `InventoryTransactionState` / `InventoryPlacementSearch` for placement and pending state.

**`GearMoveQueue`** (App) sends a planner's moves one at a time, waits for the server to confirm each one (or for a 2 s timeout), and stops at the first failure with the server's own message in chat. The deck shows "Tidying… 12 / 40" with a cancel. Only one queue runs at a time, and a manual drag cancels it.

### App (`src/AcDream.App/UI/Hud/`)

- **`UiGearDeck`** (+ `.Draw`, `.Input`, `.Equipment`, `.Packs` partials): the native window, drawn with `DeckChrome`. The width is fixed at 300 points × deck scale. The height can be resized (the packs area takes the change), and the position is saved.
- **`GearDeckGeometry`**: pure layout of the sections at a given height and scale (title, equipment in each view and folded, burden, query bar, packs area, side column, footer). It is unit-tested like `ChatDeckGeometry`.
- **`GearDeckSession`**: holds the view state, query, saved filters, rules and locks for the logged-in character; it loads and saves through `SettingsStore` and coalesces saves over 0.5 s, like `ChatDeckSession`.
- **Equipment**: Slots view and the folded row are drawn natively. Figure view hosts `PaperdollViewportRenderer` through `IUiViewportRenderer` and uses `PaperdollClickMap` for drops on the body.
- **Popups** (markup in the deck look through `PluginUiThemeSettings.WithFixedLook` + `FrameOverride` + `DeckFonts`, as with the Chat Deck settings popup): the filter menu, saved filters, pack rules, and the split-amount picker.
- **Salvage hand-off**: while the retail salvage window is visible, the footer shows "Add N to salvage". It calls `SalvageUiController.CanAdd` / `AddItem` for each shown item that is not locked.
- **Modern HUD**: a `ModernHudReplacement("Gear Deck", …, [WindowNames.Inventory], HoldsRetailVisibility: true)`. Whatever opens the retail inventory (key binding, toolbar button) toggles the Gear Deck while Modern HUD is on.

## Window layout

From the top: the title bar (Gear, ✕). The Equipment section header (▾ / ▸ and the Figure | Slots studs). The equipment area: Figure about 220 points tall, Slots 4 rows of 38-point cells, folded one row of 24-point cells. The Packs header with the burden percentage, then a burden bar. The query bar: chips, text, then the sort button with ↑/↓. Then the packs area: a 6-column grid or the list on the left, and a 36-point side column on the right with the main pack, side packs, and "All" while results are shown. Last, the footer: a count ("3 of 96 match", "Main · 96 / 102"), the queue progress, and action buttons (Combine, Add N to salvage).

List columns: icon, name, then up to two value columns. The value columns are the sort field and, if a range chip is set, that chip's field (Work, Value, Burden, Stack). Last comes the pack badge.

## Interaction

- **Selecting**: click selects one item, Ctrl/Cmd-click toggles, Shift-click selects a range. The selection is the target for Split, Lock, Add to salvage and drag.
- **Drag**: drag one item or the selection to a pack icon (it goes into that pack), into the grid (that slot), onto an equipment slot or the figure (wear), or out of the window (the existing world-drop path). Ctrl/Shift-drag of a stack opens the split picker first.
- **Double-click**: wear, take off, or use (for usable items), as in retail.
- **Right-click menu**: Split…, Lock / Unlock, Combine like stacks, Add to salvage (only while salvage is open), Show in pack (clears the query and selects the item where it is).
- **Query bar**: typing shows suggested chips below it. Enter takes the first, Esc clears the text, and Backspace on an empty field removes the last chip. A ★ button saves the current query as a named filter. Saved filters show as a row of studs under the bar when there are any.
- **Locks**: a locked item shows a small padlock. Sell, give, drop, salvage and pack rules refuse it, in the Gear Deck and in the retail paths (`ItemInteractionPolicy`, `SalvageUiController.CanAdd`), with a chat line: "<item> is locked."

## Persistence

`SettingsStore.LoadGearDeck(toonKey)` / `SaveGearDeck(toonKey, json)`, in the same shape as the Chat Deck. Saved per character: folded, Figure or Slots, Grid or List, the selected pack, saved filters, pack rules, locked ids, and "combine on arrival". The window position and height go through the existing layout persistence. The live query is not saved; it is cleared at logout.

## Error handling

- A server refusal of any move stops the queue and shows the server's message (`InventoryFailureMessages`).
- Locked ids that no longer exist are dropped on load.
- Unknown fields or enum values in the saved JSON load as disabled and are kept on save, the same as the Chat Deck rules.
- If the figure renderer fails to start, the Figure stud is disabled and Slots view is used, with one log line.
- If the deck fails to mount, the retail inventory stays (the `ModernHudSwitch` rule).

## Testing

- Core unit tests: projector, type groups (with real salvage, mana stone and healing kit names), query engine (OR/AND, ranges, ties), chip suggester, the three planners (including locks, full packs and max stack sizes), and saved JSON round trip.
- App tests: `GearDeckGeometry` at several heights and scales, `GearMoveQueue` (confirm, timeout, failure, cancel), session load and save, and the salvage hand-off skipping locked items.
- Live check at the end of each plan, run by the user on the local ACE server (never Dreamweave).

## Delivery: one spec, four plans

1. **Gear core**: Core projector and type groups; `UiGearDeck` window and geometry; Modern HUD switch; packs grid and list, side column and burden; drag and drop moves; Equipment section Slots view and folded row; double-click wear and remove; persistence.
2. **Query**: query model, engine and suggester; query bar, chips and sort; flat results; saved filters; multi-select; right-click menu; "Add N to salvage".
3. **Figure view**: 3D model in the Equipment section, the slot ring, drops on the body, and hover-highlighting matching pack items.
4. **Organising**: `GearMoveQueue`; combine (button and on arrival); split picker; pack tidy; pack rules; locks (including the retail paths).

## Out of scope

- ID-based filters and sorts (armour level, damage, spells, sets, imbues). They come later and will need a background ID policy.
- The Loot window for outside containers (its own spec).
- Changes to the retail salvage, tinkering or examine windows.
- Plugin hooks into the Gear Deck.

## Defaults chosen without a section review

The user approved section 1 (data and query) and asked to go on. These were decided by recommendation and can still be changed:
- The height can be resized and the width is fixed.
- Moves are sent one at a time with a 2 s confirmation timeout, and a manual drag cancels the queue.
- Tidy sorts by type group, then name, then workmanship (descending).
- Combine merges into the fullest stack first.
- Locks are also enforced in retail sell, give, drop and salvage.
- The live query is not saved between sessions.
