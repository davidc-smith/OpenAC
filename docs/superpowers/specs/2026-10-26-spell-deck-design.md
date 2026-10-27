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
- **Staying on screen while attached:** the Spell Deck and the deck are
  treated as one block, which must stay fully on screen.
  - Moving the deck is limited so the Spell Deck above it stays on screen.
  - "Add row below" is disabled when the extra row would push the block
    off the top of the screen. Scaling up from a corner stops at the
    largest scale that still fits.
  - On restore or after a resolution change, if the block does not fit,
    the deck moves down just enough to fit. If it still does not fit with
    the deck at the bottom of the screen, the scale is lowered in steps
    until it does.
  - A detached Spell Deck stays on screen like any other window.
- **Slots per row** = how many fit in the width at the current stretch.
  Scale changes size, not count. At stretch 1 a row always holds at least
  12 slots, so hotkeys 1–= always land on a visible slot.
- **Narrowest width:** the Spell Deck cannot be made narrower than its
  fullest visible row. Rows hold favourites in order with no gaps, so a
  row's last used slot is `min(tab count, slots at max stretch)`.
  - While attached, this minimum becomes the **Command Deck's** minimum
    width too. That holds even while the Spell Deck is hidden (peace,
    melee, missile), so it never comes back too narrow.
  - The minimum is enforced whenever it changes, not only during a resize.
    Adding a favourite (from here, the retail bar or the server), changing
    a row's tab or adding a row can raise it. When it rises above the
    current stretch, the window (the deck when attached) widens at once,
    keeping its left edge, and moves left if needed to stay on screen. The
    new size is saved.
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

### Outline

The outline is one of:

```text
None | Item | Slot(rowId, tab, spellId)
```

`rowId` is a stable id given to each row when it is created and saved with
the rows, so it survives other rows being added or removed. Because the
spell id is part of the outline, two rows showing the same tab are told
apart by `rowId`, and the outline follows its spell when the tab is
reordered.

**Repair** gives a new outline: row 1's first filled slot; otherwise the
item spell, if there is one; otherwise `None`. Repair runs whenever the
outline is `None` and something becomes available (for example, favourites
arriving after login), and whenever the rules below say so.

| Event | Outline becomes |
|---|---|
| Click, double-click or hotkey cast on a slot | That `Slot` |
| Click on the item slot, `CastItemSpell` | `Item` |
| The outlined spell is reordered in its tab | Same `Slot` (it follows the spell) |
| The outlined spell is dragged to another row | `Slot` in the destination row |
| The outlined spell leaves its tab, or no longer fits in the row's visible slots (any source, including the server) | Repair |
| The outlined row's tab is changed | That row's first filled slot, or repair if it has none |
| The outlined row is removed | Repair |
| The item spell disappears (item unwielded) while `Item` is outlined | Repair |
| Next / Prev from a `Slot` | The next / previous filled slot in that row, wrapping. In row 1 the item slot (if present) sits before slot 1 in the cycle. |
| Next / Prev from `Item` | Row 1's first / last filled slot (stays `Item` if row 1 is empty) |
| Next / First from `None` | Row 1's first filled slot, else `Item`, else `None` |
| Prev / Last from `None` | Row 1's last filled slot, else `Item`, else `None` |
| First / Last from `Item` or a `Slot` | The first / last filled slot of that row (row 1 for `Item`) |
| Row 1's tab changed by Insert / PgUp | As "the outlined row's tab is changed" when row 1 holds the outline; otherwise unchanged |

### Click modes

Chosen from the right-click menu; one setting for all characters. The UI
raises `Click` on both clicks of a double-click and then `DoubleClick`, so
the two modes handle them separately:

- **Default, "Double-click casts":** `Click` only outlines. `DoubleClick`
  casts once. CAST and End also cast.
- **"Click casts":** `Click` outlines and casts. `DoubleClick` does
  nothing, so a double-click casts twice (one per click), never three
  times.
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

**Fallback:** if the Spell Deck failed to mount, Modern HUD still turns on
(the deck works without it), but spell actions keep going to the hidden
retail `SpellcastingUiController`, as today, and one diagnostic line is
logged. The new row actions and `CastItemSpell` then do nothing.

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
- **Slot labels** come from the first chord bound to the slot's action:
  `4`, `⇧4`, `^4`, `⌥4`, or nothing. `InputDispatcher.SetBindings`
  replaces the bindings without an event, so the Spell Deck reads the
  dispatcher's current `Bindings` every time it builds its view, never a
  stored copy. Rebinding a key or loading another keymap updates the labels
  on the next frame.
- **Keybinding page:** that page builds its rows only from the retail DAT
  action map, so new enum values alone do not show up there. A new
  `ClientActionCatalog` lists the client-only actions (every
  `UseSpellRow{r}Slot{n}` and `CastItemSpell`), each with a stable
  identity in a client-only range that cannot collide with retail ids, its
  scope (MagicCombat), display name ("Spell bar row 2, slot 4", "Cast item
  spell") and group ("Spell bar", after the retail combat rows).
  `KeyboardConfigController` merges these rows into the combat page. They
  rebind, save, load and take part in conflict detection the same way as
  retail rows.

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
  reorder. In another tab it is a move. Dropping on a row that shows the
  same tab is a reorder.
- **Remove:** drag a slot off the Spell Deck, or right-click → "Remove from
  row". This removes the spell from that tab.
- **Drag transaction.** Nothing changes until a drop is accepted. (The
  retail bar removes the favourite when the drag *starts*. The Spell Deck
  must not copy that.)

  | Drag ends | Server calls |
  |---|---|
  | Accepted drop, same tab | One `SetFavorite(tab, newPosition, spell)` |
  | Accepted drop, other tab | `SetFavorite(destTab, position, spell)` first, then `RemoveFavorite(sourceTab, spell)` |
  | Drop on empty space outside every window | `RemoveFavorite(sourceTab, spell)` |
  | Refused drop, drop on another window, cancelled drag (Esc, focus lost), drag from a locked window | None |

  A drag remembers whether a drop accepted it; ending a drag never removes
  anything by itself.
- **Capacity:** checked against the tab's count *after* the operation. A
  spell already in the destination tab is a reorder and is always allowed,
  even in a full row. A spell new to the tab is refused when the tab
  already fills the row's visible slots. The drag shows "not allowed", and
  on release a chat line says `That spell bar row is full.`
- **Locked window:** while the Spell Deck is locked (the existing window
  lock), slots cannot be dragged. Clicks and hotkeys still work.

## Item spell

- **Source:** the wielded item's spell, found by the same logic the retail
  bar uses today for its endowment slot. That logic moves out of
  `SpellcastingUiController` into a shared `ItemSpellSource`
  (`src/AcDream.App/Spells/`, not the HUD folder, since both use it), so
  the retail bar's behaviour does not change.
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
| Rows (stable id + tab each, in order), attached flag | New `SpellDeckSettings` section in the settings store, under the same character key window layouts use | Per character |
| Click mode | `MiscSettings.SpellDeckClickCasts` (bool, default `false`) | All characters |

- **Detached placement is not overwritten while attached.**
  `RetailWindowLayoutPersistence` gains a per-window capture gate
  (`Func<bool>`). For `spell-deck` the gate is closed while attached, so
  moves and resizes made by the dock are neither saved nor applied from
  the saved placement. Detaching puts the window back where it was last
  detached, after any number of attaches, deck moves and relogs. The first
  detach (no saved placement) puts it just above the deck's top-left
  corner.
- **Restore order:** `SpellDeckSettings` (rows) first, then the
  placement. The width and height stored for a detached Spell Deck are
  derived values. On restore, scale = saved height ÷ the base height for
  the restored row count, snapped to a 5% step and clamped. Stretch =
  saved width ÷ (base width × scale), clamped to [minimum, 2]. The
  normalised size is applied and saved back. Changing the row count
  changes the height at the current scale. It never changes the scale.
- While attached, size comes from the deck and nothing is saved for
  `spell-deck`.

## Components

New files go in `src/AcDream.App/UI/Hud/` unless a different folder is given.

| Unit | Responsibility |
|---|---|
| `SpellDeckGeometry` | Pure. Scale, stretch, row count → window size, gutter, tab chip and slot rectangles, slots per row, minimum stretch for a set of row fills, hit-testing (`SpellDeckZone`: item, tab chip, slot (row, index), empty). |
| `SpellDeckModel` | Pure. Input: rows (tab each), each tab's favourites, item spell, outline, cast state, key labels, geometry. Output: a `SpellDeckView` (per slot: spell id, icon, label, outlined, casting; item slot). Rules: drop allowed?, minimum stretch, tab for a new row, outline navigation, outline repair when slots empty. |
| `SpellDeckSettings` | Rows (stable id + tab each, in order) + attached flag; load/save per character; validates (1–10 rows, tabs 0–7, unique ids). |
| `UiSpellDeck` | `UiElement`. Draws the view in deck style (frame, `DeckFonts`, spell icons through the same icon resolver as the retail bar). Input: click / double-click / drag / right-click → `ISpellDeckActions`. Detached resize uses `SpellDeckGeometry`. |
| `ISpellDeckActions` | `CastSpell(spellId)`, `CastItemSpell()`, `SetFavorite(tab, position, spellId)`, `RemoveFavorite(tab, spellId)`, `ExamineSpell(spellId)`, `ShowMessage(text)`. Implemented in `RetailUiRuntime` over the same bindings `SpellcastingUiController` uses. |
| `SpellDeckInput` | Hotkey handling: maps the actions in the Hotkeys table to model operations and `ISpellDeckActions` calls. |
| `ItemSpellSource` (`src/AcDream.App/Spells/`) | Wielded-item spell lookup (item id, spell id, or none), shared with `SpellcastingUiController`. |
| `SpellDeckDock` | Attachment. While attached, each layout pass places the Spell Deck on the deck and copies the deck's scale and stretch. Gives the deck its minimum stretch and widens it at once when the minimum rises. Keeps the combined block on screen (deck move limits, Add-row availability, corner-scale limit, restore fitting). Holds the persistence capture gate closed while attached. Handles detach / attach. |
| `ClientActionCatalog` (`src/AcDream.UI.Abstractions/Input/`) | Client-only actions for the keybinding page: identity, scope, display name, group. |

## Changes to existing code

| File | Change |
|---|---|
| `DeckGeometry` / `UiCommandDeck` resize | Accept an external minimum stretch (`Func<float>`), applied while resizing and when restoring a saved size. |
| `CommandDeckModel` / `CommandDeckInput` | CASTING text order above; `OutlinedSpellName` input; `RecastEnabled` → `CastEnabled`. |
| `ICommandDeckActions` | `Recast()` → `CastOutlined()`. |
| `ModernHudSwitch` | Shows `spell-deck` only while on and in magic combat (and in the world); hides it otherwise. Knows whether the Spell Deck mounted, for the hotkey fallback. |
| `RetailUiRuntime` | Mounts the Spell Deck and dock; routes spell actions to it first with Modern HUD on (falling back to the retail controller if it did not mount); routes the spellbook's add-to-favourites to it with Modern HUD on. |
| `RetailWindowLayoutPersistence` | Per-window capture gate (`Func<bool>`), default open. |
| `KeyboardConfigController` | Merges `ClientActionCatalog` rows into the combat page. |
| `UiCommandDeck` move | Accepts a move limit from the dock (keep the attached Spell Deck on screen). |
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
  shows only N; key labels from the live bindings (plain, Shift, Ctrl,
  none; changed after `SetBindings`); every row of the outline table,
  including duplicated tabs on two rows and `None` / `Item` starts; drop
  rules (insert, move in tab, move across tabs, same tab on two rows, full
  row refused for a new spell, reorder of an existing spell in a full row
  allowed); tab for a new row; blue glow only for a known pending spell.
- **Drag transaction:** accepted same-tab drop = one `SetFavorite`;
  accepted cross-tab drop = `SetFavorite` then `RemoveFavorite`; no calls
  for a refused full-row drop, a drop on another window, a cancelled drag,
  or a locked window; drop outside every window = one `RemoveFavorite`.
- **Click modes (exact call counts):** default: one physical click casts 0
  times, a double-click casts once; "Click casts": one click casts once, a
  double-click casts twice; hotkeys always cast once.
- **Hotkey routing:** with Modern HUD on, spell actions reach the Spell
  Deck and not `SpellcastingUiController`; with it off, the reverse; with
  the Spell Deck not mounted, they go to `SpellcastingUiController`; a key
  for a hidden row or empty slot is handled and does nothing.
- **Keybinding page:** every `ClientActionCatalog` action is listed, can be
  rebound, is saved and restored, and takes part in conflict detection.
- **Dock:** attached follows the deck's scale and stretch; the deck cannot
  shrink below the Spell Deck's minimum, also while the Spell Deck is
  hidden; a rising minimum (new favourite, tab change, added row) widens
  the deck at once, also while hidden; detach restores the last detached
  position after repeated attach / deck move / relog cycles; attach snaps
  back; Add row disabled when the block would leave the screen; deck move
  and corner scale limited by the block; restore on a smaller resolution
  moves the deck down, then lowers the scale.
- **`SpellDeckSettings`:** round-trip including row ids; invalid row counts
  and tabs are clamped; defaults for a new character; restore order and
  size normalisation (scale from height ÷ row base height, stretch from
  width) with the normalised size saved back.
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

## v2 (2026-10-27, after the first live look)

The user's first live look found three problems: tabs were invisible (a number chip per row, changed only by clicking it or from the menu), the attached Spell Deck did not look attached (a gap, and it overhung the deck's left edge), and an attached Spell Deck showed a move cursor but could not be moved. They chose: **one tab for the whole deck, picked from bookmark tabs on top**; **dragging the attached Spell Deck moves both decks**; and **more than 8 tabs, the extra ones saved locally**. Where this section conflicts with the sections above, this section wins.

### Tabs on top

- A **tab strip** runs across the top of the Spell Deck: one bookmark per tab, labelled with its number (`1`, `2`, …), the current tab raised and lit. After the last tab, a **`+`** bookmark adds a tab.
- **The deck shows one tab.** Every row shows more of that tab (below).
- Click a bookmark to show that tab. Insert / PgUp go to the previous / next tab (wrapping over the tabs that exist); Ctrl+Insert / Ctrl+PgUp go to the first / last.
- Tabs 1–8 are the server's favourite lists (ACE accepts tab numbers 0–7 only). **Tabs 9–16 are local**: added with `+`, saved in this PC's settings for the character, not sent to the server. They behave exactly like server tabs (drag, hotkeys, casting). They do not follow the character to another PC or the retail client.
- Right-click on the **last** local tab: **Remove tab** (its spells are discarded with it). Server tabs cannot be removed. `+` is hidden at 16 tabs.
- Bookmarks are 24 wide with a 2 gap; when they do not fit the strip they shrink evenly, down to 16 wide.

### Rows continue the tab

- With `N` slots per row (`SlotsPerRow`), row `r` (0-based) shows spells `r·N` … `r·N + N − 1` of the current tab. The deck shows up to `rows · N` spells.
- **No overflow (unchanged rule, new scope):** the narrowest stretch is the smallest one where `rows · N` holds the fullest tab of **all** tabs (server and local), capped at the widest stretch. A tab that does not fit even then shows its first `rows · Nmax` spells; the rest are not shown or castable (as before).
- **Remove row** is disabled when the remaining rows, at the widest stretch, cannot hold the fullest tab. When allowed, the deck widens as needed (the minimum-width rule does it).
- Add row: unchanged (disabled when it would leave the screen).

### Hotkeys

- A slot key casts what that slot shows: row `r` slot `n` casts spell `r·N + n` of the current tab. Defaults are unchanged (row 1 plain, row 2 Shift, row 3 Ctrl, slots 1–9). With a wide deck, slots past the 12th of a row have no key.

### Outline

- The outline is `None | Item | Spell(tab, spellId)`.
- Repair: the current tab's first visible spell, else the item spell, else `None`.
- Showing another tab moves the outline to that tab's first visible spell (else item, else `None`).
- Next / Prev walk the current tab's visible spells in order, the item spell (if any) before the first; wrapping. First / Last: the first / last visible spell.
- An outlined spell that is reordered keeps the outline; one that leaves the tab or the visible range is repaired.

### Editing favourites

- **Drop on a slot** at row `r`, slot `n`: position `r·N + n` in the current tab. Same rules as before: a spell already in the tab is a reorder (always allowed); a new one is refused when the tab already fills `rows · N` (chat: `That spell tab is full.`).
- **Drop on a tab bookmark:** a slot's spell **moves** to the end of that tab (add there first, then remove from the current tab); a spellbook spell is added to the end. Refused when that tab already fills `rows · N`, or already holds the spell (nothing to do).
- Drop outside every window: removes the spell from the current tab (unchanged).
- The spellbook's "add to favourites": the end of the current tab.
- Local tab edits change the local list and save it; server tab edits send the existing messages (unchanged).

### Right-click menu

- On a filled slot: **Examine spell**, **Remove from tab**, separator.
- Anywhere on the deck: **Add row**, **Remove row**, separator, **Click casts: on/off**, separator, **Detach from deck** / **Attach to deck**.
- On the last local tab's bookmark: **Remove tab** (only).
- The "Show tab n" items are gone.

### Looking attached

- **Attached:** the Spell Deck's left edge is the orb's left edge (`deck.Left + 32·scale`), its right edge the deck's right edge, its bottom the deck frame's top (`deck.Top + 4·scale`). Its width is `(388 + 420·(stretch − 1))·scale`. It draws square bottom corners and the Command Deck draws square top corners, so the two frames meet on one seam with no gap.
- **Detached:** rounded corners all round, same base width (388 at stretch 1).
- **Dragging the attached Spell Deck moves the whole block:** the Command Deck moves by the same amount (the block stays on screen) and its position is saved. The Spell Deck's own (detached) placement is untouched.

### Geometry v2

`Pad 6`, `Gutter 40` (item slot centred), `TabStrip 18`, `SlotsLeft 46`, `SlotSize 26`, `SlotPitch 28`, `RowPitch 28`, `TopPad 7`, `BottomPad 7`. Base width `388 + 420·(stretch − 1)`. Height `12 + 18 + 28·rows`. Slots per row `floor((width/scale − 46 − 6 + 2) / 28)`: 12 at stretch 1, 27 at stretch 2.

### Saved state v2

Per character: `tab` (0–15), `rowCount` (1–10), `attached`, `detachedRows`, `localTabs` (array of spell-id arrays, at most 8). Old saves with a `rows` array load as `rowCount = rows.length`, `tab = rows[0].tab`.

### Testing v2

Slices and capacity across stretches; hotkey `r·N + n` mapping; outline rules above; drops on slots (reorder / insert / full) and on bookmarks (move / add / full / already there); min stretch over all tabs including local; Remove row disabled rule; local tab add / remove-last / persist / no server call; tab strip hit-testing and bookmark shrink; attached placement (left, width, bottom) and square-corner flags; attached drag moves the deck and keeps the block on screen; settings migration from the v1 `rows` array.
