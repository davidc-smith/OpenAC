# Spell Deck: modern spell bar for the Command Deck

Status: design approved in brainstorming, 2026-10-26. Fork-only; not for upstream.
Baseline: fork `main` at `d996784a` (Command Deck + resize merged).
Mockups: `.superpowers/brainstorm/44007-1793044336/content/` (`spell-deck-y3.html`,
`spell-deck-cast.html` are final).

## Goal

The Command Deck replaced the retail `combat` window, and with it the spell
bar (`SpellcastingPanel` in layout `0x21000073`). Since then, Modern HUD on
means no visible spell bar. The hidden retail controller still takes the
spell hotkeys, so they cast favourites the player cannot see.

The **Spell Deck** is the modern replacement: a separate window that attaches
on top of the Command Deck in magic mode. It has 1–10 rows of favourite-spell
slots, each row showing one of the 8 server favourite tabs. A slot for the
wielded item's spell sits on the left, and hotkeys cover several rows.

With Modern HUD off, nothing changes.

## Decisions

- **Separate window, attached by default.** It sits flush on top of the
  Command Deck and uses the same frame, fonts and look. It can be detached
  and moved on its own. It is not part of the deck, so the deck keeps its
  fixed height.
- **Visible only in magic combat**, and only with Modern HUD on.
- **Each row is its own bar** (MMO action-bar style). Every row picks one of
  the 8 favourite tabs, shown as a plain number `1`–`8` (no roman numerals,
  no retail tab names). Several rows may show the same tab.
- **No overflow.** A row shows only the spells that fit. Spells past the
  last visible slot are not shown and cannot be cast from the Spell Deck.
  There is no `+N` chip.
- **Cast button lives on the Command Deck.** The deck's CASTING row shows
  the outlined spell, and its button becomes **CAST** (RECAST goes away).
- **Two click modes.** Default: a click outlines and a double-click casts.
  Option: "Click casts".
- **Client-only.** No plugin contract change, no `-fork.n` bump. Favourites
  are changed with the existing server messages.

## Layout

```
 ┌────────────────────────────────────────────────────┐
 │      │ [1] ▣¹ ▣² ▣³ ▣⁴ ▣⁵ ▣⁶ ▣⁷ ▣⁸ □⁹ □  □  □      │   row 1: tab 1, keys 1–9
 │  ◉   │ [4] ▣  ▣  ▣  ▣  ▣  □  □  □  □  □  □  □      │   row 2: tab 4, Shift+1–9
 │      │ [6] ▣  ▣  ▣  □  □  □  □  □  □  □  □  □      │   row 3: tab 6, Ctrl+1–9
 └────────────────────────────────────────────────────┘
 ┌ Command Deck ───────────────────────────────────────┐
 │ … CASTING  ❄ Frost Bolt VII                [➤ CAST] │
```

- **Left gutter:** the item-spell slot (`◉`), round with a gold rim,
  centred top to bottom across all rows. The gutter is always there, so
  the width never jumps. With no item spell it shows a dim, empty socket.
- **Rows:** a tab chip (`1`–`8`), then the slots. A slot shows the spell
  icon and, in its top-left corner, the hotkey label of its current binding.
- **Outline:** the outlined "current spell" has a gold outline. The slot
  being cast glows blue.

## Size and attachment

`SpellDeckGeometry` follows the `DeckGeometry` pattern: `Scale` (0.75–1.5,
5% steps, 100% lock) and `Stretch` (width only, 1–2×).

- **Attached (default):** fully tied to the Command Deck. It uses the
  deck's scale and stretch, its left and right edges line up with the
  deck's, and its bottom sits on the deck's top. Dragging the deck's right
  edge or corners resizes both. The Spell Deck has no resize edges of its
  own while attached. Adding rows grows it upwards, so the deck never moves.
- **Detached:** a normal movable, lockable window. It resizes like the
  deck: the right edge stretches the width, the right corners scale it,
  and the left, top and bottom edges do not resize.
- **Slots per row** = how many fit in the width at the current stretch.
  Scale changes size, not count. At stretch 1 a row always holds at least
  12 slots, so hotkeys 1–= always land on a visible slot.
- **Narrowest width:** the Spell Deck cannot be made narrower than its
  fullest visible row. Rows hold favourites in order with no gaps, so a
  row's last used slot is `min(tab count, slots at max stretch)`.
  - While attached, this minimum becomes the **Command Deck's** minimum
    width too. That holds even while the Spell Deck is hidden (peace,
    melee, missile), so it never comes back too narrow.
  - A tab larger than the widest stretch makes the minimum the maximum
    stretch. The extra spells are not shown (see "Oversized tabs").
- **Oversized tabs:** retail characters may have tabs holding more spells
  than the widest bar shows. Those spells stay in the server list
  untouched, are not shown and cannot be cast from the Spell Deck. They
  are visible again in the retail bar with Modern HUD off.

## Rows

- **Count:** 1–10, default 3. Add and remove from the right-click menu.
- **New row:** added below the clicked row. It shows the lowest tab number
  no other row shows, or tab 1 if every tab is already on screen.
- **Remove row:** hides the row. The tab's favourites are never touched.
  The last row cannot be removed (the menu item is disabled).
- **Show tab ▸:** a submenu `1`–`8` that changes the clicked row's tab.
- **Default rows for a new character:** row 1 = tab 1, row 2 = tab 2,
  row 3 = tab 3.

## Casting and the outline

- **Outline:** the last slot the player clicked, cast or moved the outline
  to. It starts on row 1's first slot. If that slot empties or its row is
  removed, the outline moves to row 1's first filled slot; if there is
  none, nothing is outlined.
- **Click modes**, from the right-click menu (one setting for all
  characters):
  - **Default: "Double-click casts".** A click outlines the slot. A
    double-click, CAST, or End casts it.
  - **"Click casts".** A click outlines the slot and casts it at once.
- **What casting does:** the same call the retail bar makes when it casts
  a favourite (`RuntimeSpellCastState` with the current selection as
  target). The existing cast checks give the errors for missing
  components, no target, too far away and so on. There is no new error
  path.
- **Blue glow:** the slot whose spell matches `PendingSpellId` while
  `HasPendingCast` is set. When casts overlap (`PendingSpellId` null),
  nothing glows.
- **Tooltip** on hover: spell name, school and the key bound to the slot,
  in the existing `HudTooltip` style.

### Command Deck CASTING row (changed)

Text, in order of precedence:

1. A cast in progress with a known `PendingSpellId`: that spell, lit.
2. A cast in progress with no single known spell: `Casting...`, lit.
3. The Spell Deck's outlined spell, normal.
4. `No spell selected`, dimmed.

The button is now **CAST** (icon-only stud, like RECAST was). It casts the
outlined spell, or the item spell when that is outlined. It is disabled
while `HasPendingCast` is set or nothing is outlined. RECAST and its
`Recast` action are removed. `ICommandDeckActions.Recast` becomes
`CastOutlined`.

## Hotkeys

With Modern HUD on, `RetailUiRuntime.HandleInputAction` offers the action
to the Spell Deck first. The hidden retail `SpellcastingUiController` is not
asked. With Modern HUD off, routing is unchanged.

| Action | Spell Deck behaviour | Default key (MagicCombat scope) |
|---|---|---|
| `UseSpellSlot_1…12` (existing) | Cast row 1, slot n | 1–9 (unchanged); 10–12 unbound |
| `UseSpellRow2Slot1…12` (new) | Cast row 2, slot n | Shift+1–9 |
| `UseSpellRow3Slot1…12` (new) | Cast row 3, slot n | Ctrl+1–9 |
| `UseSpellRow4…10Slot1…12` (new) | Cast row r, slot n | unbound |
| `CastItemSpell` (new) | Cast the item spell | unbound |
| `CombatPrevSpellTab` / `CombatNextSpellTab` | Row 1's tab −1 / +1, wrapping 1–8 | Insert / PgUp (unchanged) |
| `CombatFirstSpellTab` / `CombatLastSpellTab` | Row 1's tab = 1 / 8 | Ctrl+Insert / Ctrl+PgUp |
| `CombatPrevSpell` / `CombatNextSpell` | Move the outline left / right in its row, wrapping. The item slot counts as the slot before row 1's first slot. | Delete / PgDn |
| `CombatFirstSpell` / `CombatLastSpell` | Outline the first / last filled slot of its row | Ctrl+Delete / Ctrl+PgDn |
| `CombatCastCurrentSpell` | Cast the outlined spell | End |

- Hotkeys cast at once, whatever the click mode.
- A hotkey for a row that is not shown (row count below r) or an empty slot
  does nothing, but is still handled (it does not fall through to other
  bindings).
- **Ctrl+0 is not a default:** it is already `CreateShortcut`. Row 3
  slot 10 is unbound by default, like slots 10–12 of every row.
- **Slot labels** come from the current `KeyBindings` (first chord bound
  to the slot's action): `4`, `⇧4`, `^4`, `⌥4`, or nothing. Rebinding
  updates the labels.
- All new actions appear in the keybinding UI with readable names
  ("Spell bar row 2, slot 4", "Cast item spell").

## Editing favourites

All edits use the existing favourite messages, the same `addFavorite(tab,
position, spell)` / `removeFavorite(tab, spell)` callbacks the retail bar
uses. The local `Spellbook` updates as it does today.

- **From the spellbook:** drag a spell (`SpellbookShortcutDragPayload`)
  onto a slot to insert it into that row's tab at that position. If the
  spell is already in the tab, it moves there.
- **Spellbook "add to favourites"** (the existing callback that today goes
  to `SpellcastingUiController.AddFavorite`) goes, with Modern HUD on, to
  the row holding the outline, or row 1 if nothing is outlined. It is
  added at the end.
- **Reorder / move:** drag a slot to another slot. In the same tab it is a
  reorder. In another tab it is removed from the old tab and inserted into
  the new one. Dropping on a row that shows the same tab is a reorder.
- **Remove:** drag a slot off the Spell Deck, or right-click → "Remove from
  row". This removes the spell from that tab.
- **Full row:** a drop that would put a spell past the row's last visible
  slot is refused. The drag shows "not allowed", and on release a chat line
  says `That spell bar row is full.` A move within the same tab is never
  refused.
- **Locked window:** while the Spell Deck is locked (the existing window
  lock), slots cannot be dragged. Clicks and hotkeys still work.

## Item spell

- **Source:** the wielded item's spell, found by the same logic the retail
  bar uses today for its endowment slot. That logic moves out of
  `SpellcastingUiController` into a shared `ItemSpellSource`, and both use
  it, so the retail bar's behaviour does not change.
- **Cast:** through the existing activate-item path the retail bar uses.
  Follows the click mode. `CastItemSpell`, End (when outlined) and CAST
  also cast it.
- **Tooltip:** item name and spell name.
- **No item spell:** an empty, dim socket. Clicks do nothing. It is skipped
  by outline navigation.

## Right-click menu

On a slot: **Examine spell**, **Remove from row**, separator. Then on any
row (slot or tab chip): **Add row below**, **Remove this row**, **Show tab
▸** (`1`–`8`), separator, **Click casts** (check item), separator,
**Detach from deck** / **Attach to deck**. Uses the existing retail
context menu.

## Saved state

| What | Where | Scope |
|---|---|---|
| Position, width, height (detached) | Existing window-layout persistence (`RetailWindowLayoutPersistence`), window name `spell-deck` | Per character, per resolution (as other windows) |
| Row count, each row's tab, attached flag | New `SpellDeckSettings` section in the settings store, under the same character key window layouts use | Per character |
| Click mode | `MiscSettings.SpellDeckClickCasts` (bool, default `false`) | All characters |

While attached, the saved detached position is kept, so detaching puts the
window back where it was last detached. The first detach puts it just
above the deck's top-left corner.

## Components

All new files go in `src/AcDream.App/UI/Hud/`.

| Unit | Responsibility |
|---|---|
| `SpellDeckGeometry` | Pure. Scale, stretch, row count → window size, gutter, tab chip and slot rectangles, slots per row, minimum stretch for a set of row fills, hit-testing (`SpellDeckZone`: item, tab chip, slot (row, index), empty). |
| `SpellDeckModel` | Pure. Input: rows (tab each), each tab's favourites, item spell, outline, cast state, key labels, geometry. Output: a `SpellDeckView` (per slot: spell id, icon, label, outlined, casting; item slot). Rules: drop allowed?, minimum stretch, tab for a new row, outline navigation, outline repair when slots empty. |
| `SpellDeckSettings` | Row count + row tabs + attached flag; load/save per character; validates (1–10 rows, tabs 0–7). |
| `UiSpellDeck` | `UiElement`. Draws the view in deck style (frame, `DeckFonts`, spell icons through the same icon resolver as the retail bar). Input: click / double-click / drag / right-click → `ISpellDeckActions`. Detached resize uses `SpellDeckGeometry`. |
| `ISpellDeckActions` | `CastSpell(spellId)`, `CastItemSpell()`, `SetFavorite(tab, position, spellId)`, `RemoveFavorite(tab, spellId)`, `ExamineSpell(spellId)`, `ShowMessage(text)`. Implemented in `RetailUiRuntime` over the same bindings `SpellcastingUiController` uses. |
| `SpellDeckInput` | Hotkey handling: maps the actions in the Hotkeys table to model operations and `ISpellDeckActions` calls. |
| `ItemSpellSource` | Wielded-item spell lookup (item id, spell id, or none), shared with `SpellcastingUiController`. |
| `SpellDeckDock` | Attachment. While attached, each layout pass places the Spell Deck on the deck and copies the deck's scale and stretch. Gives the deck's resize its minimum stretch. Handles detach / attach. |

## Changes to existing code

| File | Change |
|---|---|
| `DeckGeometry` / `UiCommandDeck` resize | Accept an external minimum stretch (`Func<float>`), applied while resizing and when restoring a saved size. |
| `CommandDeckModel` / `CommandDeckInput` | CASTING text order above; `OutlinedSpellName` input; `RecastEnabled` → `CastEnabled`. |
| `ICommandDeckActions` | `Recast()` → `CastOutlined()`. |
| `ModernHudSwitch` | Shows `spell-deck` only while on and in magic combat (and in the world); hides it otherwise. |
| `RetailUiRuntime` | Mounts the Spell Deck and dock; routes spell actions to it first with Modern HUD on; routes the spellbook's add-to-favourites to it with Modern HUD on. |
| `SpellcastingUiController` | Uses `ItemSpellSource`; no behaviour change. |
| `InputAction`, `KeyBindings`, `RetailActionIdentityTable`, keybinding UI names | New actions and defaults (Hotkeys table). New enum values go at the end, so saved bindings keep their numbers. |
| `MiscSettings`, `SettingsStore` | `SpellDeckClickCasts`; `SpellDeckSettings` section. |
| `WindowNames` | `SpellDeck = "spell-deck"`. |

## Testing

- **`SpellDeckGeometry`:** slots per row at stretch 1 and 2 (≥ 12 at
  stretch 1); scale changes size, not count; rectangles for 1 and 10 rows;
  item slot centred for every row count; hit-testing at scale 0.75 and 1.5;
  minimum stretch from row fills, including clamping to max stretch.
- **`SpellDeckModel`:** slots show the first N of each tab; oversized tab
  shows only N; key labels from bindings (plain, Shift, Ctrl, none); outline
  navigation (wrap, item slot before row 1, first / last, skip empty item
  socket); outline repair; drop rules (insert, move in tab, move across
  tabs, same tab on two rows, full row refused, reorder in a full row
  allowed); tab for a new row; blue glow only for a known pending spell.
- **Click modes:** click outlines / double-click casts (default); click
  casts (option); hotkeys always cast.
- **Hotkey routing:** with Modern HUD on, spell actions reach the Spell
  Deck and not `SpellcastingUiController`; with it off, the reverse; a key
  for a hidden row or empty slot is handled and does nothing.
- **Dock:** attached follows the deck's scale and stretch; the deck cannot
  shrink below the Spell Deck's minimum, also while the Spell Deck is
  hidden; detach restores the last detached position; attach snaps back.
- **`SpellDeckSettings`:** round-trip; invalid row counts and tabs are
  clamped; defaults for a new character.
- **Command Deck:** CASTING text order; CAST disabled while casting or with
  nothing outlined; CAST casts the item spell when that is outlined.
- **`ItemSpellSource`:** the retail bar's existing endowment tests still
  pass.
- **`ModernHudSwitch`:** Spell Deck shown only on + magic; hidden on mode
  change, Modern HUD off, leaving the world.
- Tests run via `~/code/OpenAC/test-baseline/run-mac-tests.sh`.
- **Live check** on the local ACE server (10.10.20.20), only when the user
  says they are available: rows add / remove / tab change; hotkeys on rows
  1–3 and after rebinding; both click modes; CAST and End; item spell with
  a caster that has one and one without; drag from the spellbook, reorder,
  move between rows, remove, full-row refusal; lock blocks dragging;
  attached resize (deck minimum width) and detached resize; relog keeps
  rows, tabs, attachment and position; Retina sharpness.

## Out of scope

- Spell component counts, cooldown or windup display.
- Showing or casting spells past the visible slots.
- Renaming tabs.
- Slots for anything other than spells (items, emotes).
- Upstream PR.
