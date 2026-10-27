# Spell Deck v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers-extended-cc:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Apply the spec's "v2" section: one tab for the whole deck chosen from bookmark tabs on top (plus local tabs 9–16), rows continuing that tab, a seamless join to the Command Deck, dragging the attached Spell Deck moves both, and a labelled CAST button.

**Architecture:** Same units as v1. The pure model and outline change shape (rows become slices of one tab). Settings gain tab, row count and local tabs. A `SpellDeckTabs` source merges server tabs (from `Spellbook`) and local tabs (from settings) behind one interface, so the controller treats them alike and routes edits to the server or local store. Geometry gains a tab strip and loses row chips. The dock places the Spell Deck on the deck's visible frame and forwards attached drags to the deck.

**Tech Stack:** C#/.NET, xUnit, the retained UI.

**Spec:** `docs/superpowers/specs/2026-10-26-spell-deck-design.md`, section **"v2 (2026-10-27, after the first live look)"** and **"CAST button label"**. These win over the earlier sections where they conflict.

## Global Constraints

- Worktree `~/code/OpenAC/OpenAC-spell-deck`, branch `feat/spell-deck` (head 40221653 or later). Build `~/.dotnet/dotnet build ~/code/OpenAC/OpenAC-spell-deck/AcDream.slnx -v q`. Tests only via `MAC_TEST_FILTER='<f>' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck`.
- Every commit message ends with `Claude-Session: https://claude.ai/code/session_014LbLR6dmw4pYiKRAFdC9jf`.
- Keep every v1 guarantee not changed by v2: drag transaction (nothing changes until a drop is accepted), Modern HUD off = retail unchanged (incl. the Ctrl/Shift re-dispatch), detached placement never overwritten while attached, restore order, no per-frame settings IO, `DeckFonts` for all text.
- Geometry v2 constants exactly as the spec: `Pad 6, Gutter 40, TabStrip 18, SlotsLeft 46, SlotSize 26, SlotPitch 28, RowPitch 28, TopPad 7, BottomPad 7`; base width `388 + 420·(stretch−1)`; height `12 + 18 + 28·rows`; slots per row 12 at stretch 1, 27 at stretch 2.
- Server tabs 0–7, local tabs 8–15 (at most 8 local). Local edits never call `SetFavorite`/`RemoveFavorite` on the server actions.
- Messages: `That spell tab is full.`

**User decisions (already made):** one tab for the whole deck, bookmark tabs on top (option B); rows continue the tab; dragging the attached Spell Deck moves both decks; extra tabs 9–16 saved locally per character; CAST button shows the word CAST; join flush on the deck frame from the orb's left edge to the deck's right edge.

---

### Task 15: Settings v2 and local tabs storage

**Goal:** `SpellDeckSettings` becomes `(int Tab, int RowCount, bool Attached, int? DetachedRows, IReadOnlyList<IReadOnlyList<uint>> LocalTabs)` with validation and v1 migration; the store round-trips it.

**Files:** Modify `src/AcDream.App/UI/Hud/SpellDeckSettings.cs`, `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs` (`StoredSpellDeck`, `LoadSpellDeck`, `SaveSpellDeck`); tests `SpellDeckSettingsTests.cs`, `SettingsStoreSpellDeckTests.cs`; update callers so the solution builds (callers that used `Rows` read `RowCount`/`Tab`; behaviour changes come in Task 17).

**Acceptance Criteria:**
- [ ] Default: Tab 0, RowCount 3, Attached true, DetachedRows null, no local tabs
- [ ] Normalize: RowCount clamped 1–10; LocalTabs truncated to 8 (each list de-duplicated, zero ids dropped); Tab clamped to 0..(7 + LocalTabs.Count), else 0
- [ ] JSON `spellDeck[toon] = { tab, rowCount, attached, detachedRows?, localTabs: [[ids]] }`; other sections kept
- [ ] A v1 entry with `rows: [{id, tab}, …]` loads as `RowCount = rows.length` (clamped), `Tab = rows[0].tab`; `detachedRows` carried
- [ ] A malformed local tab entry is skipped, not the whole deck

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SpellDeckSettings|FullyQualifiedName~SettingsStore'` → pass; solution builds.

### Task 16: Pure model and outline v2

**Goal:** Slices, capacity, drop planning (slot and bookmark), minimum slots over all tabs, Remove-row rule, and the simplified outline.

**Files:** Modify `SpellDeckModel.cs`, `SpellDeckOutline.cs` (and their tests). Remove v1-only members (`TabForNewRow`, row-id contexts) once unused.

**Acceptance Criteria:**
- [ ] `Slice(tab, row, perRow)` = spells `row·perRow … +perRow−1`; `Visible(tab, rows, perRow)` = first `rows·perRow`
- [ ] `MinSlotsPerRow(rows, allTabCounts)` = `ceil(max count / rows)` capped at `SpellDeckGeometry.MaxCapacity`, at least 1
- [ ] `CanRemoveRow(rows, allTabCounts)` = rows > 1 and `(rows−1)·MaxCapacity >= max count`
- [ ] `PlanDropOnSlot(source, tab, position, favorites, capacity)`: reorder if present (position clamped to count−1), insert if `count+1 <= capacity` (clamped to count), else `RefusedFull`; a slot source from the same tab is always a reorder
- [ ] `PlanDropOnTab(source, destTab, favorites, capacity)`: slot source from another tab → `Move` = `[Set(dest, count, id), Remove(src, id)]`; spellbook source → `Insert` at end; dest already holds it or source tab == dest → `None` (no ops, not refused); dest full → `RefusedFull`
- [ ] Outline `None | Item | Spell(tab, spellId)` with `Repair`, `Validate`, `Next/Prev/First/Last` over the visible spells of the current tab with the item before the first (spec "Outline" v2); showing another tab → first spell of that tab
- [ ] Tests for every bullet

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SpellDeckModel|FullyQualifiedName~SpellDeckOutline'`.

### Task 17: Tabs source and controller v2

**Goal:** `SpellDeckTabs` (server tabs from `Spellbook`, local tabs from settings, edits routed accordingly) and `SpellDeckController` reworked to one current tab, row count, slices, local tab add/remove, bookmark drops.

**Files:** Create `src/AcDream.App/UI/Hud/SpellDeckTabs.cs`; modify `SpellDeckController.cs`, `ISpellDeckActions.cs` (unchanged signature for server edits), tests.

**Acceptance Criteria:**
- [ ] `SpellDeckTabs`: `Count` (8 + local), `Get(tab)`, `Set(tab, pos, id)` / `Remove(tab, id)` → server actions for 0–7, local list mutation + save callback for 8–15; `AddLocal()` (≤ 8 local), `RemoveLastLocal()`
- [ ] Controller: `Tab`, `RowCount`, `PerRow` (from capacity), `ShowTab(t)`, `NextTab/PrevTab/FirstTab/LastTab` (Insert/PgUp keys) wrapping over `Count`
- [ ] Hotkey row r slot n casts `Visible[r·N+n]` and outlines it; hidden row / empty slot handled, no-op
- [ ] `Click/DoubleClick(row, slot)` and click modes as v1 (counts unchanged)
- [ ] `DropOnSlot(source, row, slot)`, `DropOnTab(source, tab)`, `DropOutside(source)`; refused → `That spell tab is full.`; an outlined spell moved to another tab is not followed (outline repairs)
- [ ] `AddRow()` (≤ 10 and `canAddRow`), `RemoveRow()` (model rule), `AddLocalTab()` shows the new tab, `RemoveLastLocalTab()` (if current, show previous)
- [ ] `MinSlotsPerRow` over all tabs; `AddFavoriteFromSpellbook` → current tab end; Command Deck members (`OutlinedSpellId`, `CanCastOutlined`, `CastOutlined`) unchanged in meaning
- [ ] Every change saves settings v2 (tab, rows, local tabs) once; local tab edits never call server actions
- [ ] Tests for every bullet

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SpellDeckController|FullyQualifiedName~SpellDeckTabs'`.

### Task 18: Geometry v2 and element v2 (tab strip, menu, drops)

**Goal:** Geometry with the tab strip and no chips; the element draws bookmarks and `+`, handles bookmark clicks and drops, and the v2 menu.

**Files:** Modify `SpellDeckGeometry.cs`, `UiSpellDeck.cs`, `UiSpellDeck.Draw.cs`, tests.

**Acceptance Criteria:**
- [ ] Geometry v2 constants (Global Constraints); `Width = (388 + 420·(stretch−1))·scale`; `Capacity(1)=12`, `Capacity(2)=27`; `StretchFor(n)` inverse; `Bookmark(i, count)` rects (24 wide, 2 gap, shrinking evenly to ≥16 to fit), `Plus` rect after the last; `HitAt` returns `Bookmark(i)`, `Plus`, `Item`, `Slot(row, n)`, `Frame`
- [ ] Attached flag on geometry or element: square bottom corners when attached, rounded otherwise
- [ ] Bookmarks drawn with numbers (current raised/lit), `+` hidden at 16 tabs; no row chips
- [ ] Click bookmark → `ShowTab`; click `+` → `AddLocalTab`; drag-over a bookmark highlights accept/reject from the plan; drop on bookmark → `DropOnTab`
- [ ] Menu items exactly as spec v2 (slot: Examine spell / Remove from tab; deck: Add row / Remove row / Click casts: on|off / Detach|Attach; last local bookmark: Remove tab only)
- [ ] Tooltips: bookmark `Tab n` with spell count ("Local tab" note for 9–16), `+` "Add a tab"
- [ ] Draw and hit tests at scale 0.75/1/1.5 and stretch 1/2

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SpellDeckGeometry|FullyQualifiedName~UiSpellDeck'`.

### Task 19: Seamless join, attached block drag, CAST label

**Goal:** The dock places the attached Spell Deck on the deck's visible frame with square meeting corners; dragging it moves the deck; the CAST button shows its label.

**Files:** Modify `SpellDeckDock.cs`, `SpellDeckWindowDockHost.cs`, `UiCommandDeck.cs`/`UiCommandDeck.Draw.cs`/`DeckGeometry.cs` (square top corners flag; CAST stud 54 wide with label), `UiSpellDeck.cs` (attached drag forwarding), tests (`SpellDeckDockTests`, `UiCommandDeck*Tests`, `DeckGeometryTests`).

**Acceptance Criteria:**
- [ ] Attached placement: left = deck.Left + 32·scale, width = deck width − 32·scale, bottom = deck.Top + 4·scale (spec "Looking attached"); deck `TopLimit` and corner-scale limit use the overlap (`spell height − 4·scale`)
- [ ] While attached the deck draws square top corners and the Spell Deck square bottom corners; detached both rounded
- [ ] Dragging the attached Spell Deck moves the deck by the same delta, block clamped on screen; the deck's move is saved; the Spell Deck's stored placement is unchanged; the move cursor is shown
- [ ] CAST stud is 54 base wide with the cast icon and the word `CAST` (small SemiBold); casting text area shrinks and still ends in `...` before it; hit zone covers the whole stud
- [ ] Tests for each bullet; existing Command Deck tests updated only where geometry intentionally changed

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SpellDeckDock|FullyQualifiedName~CommandDeck|FullyQualifiedName~DeckGeometry'`.

### Task 20: Runtime wiring v2

**Goal:** Wire `SpellDeckTabs`, settings v2 and the new controller/element into `RetailUiRuntime`; the Command Deck CASTING row and the spellbook add keep working; full suite green.

**Files:** Modify `src/AcDream.App/UI/RetailUiRuntime.cs`, `SpellDeckCharacterSync.cs`, tests (`ModernHudWiringTests`, persistence rig).

**Acceptance Criteria:**
- [ ] `SpellDeckTabs` built from `Spellbook` + settings local tabs; local edits save through the sync's save path (never under "default")
- [ ] Character load/switch restores tab, rows, local tabs; restore order guarantees from v1 still hold (rig tests pass, add one for local tabs surviving relog)
- [ ] Insert/PgUp etc. reach the controller only while the Spell Deck is active; Modern HUD off unchanged
- [ ] Full suite: 0 failures

**Verify:** `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck` → 0 failures.

### Task 21: Live check (user gate)

> **USER-ORDERED GATE — NON-SKIPPABLE.** Close only after every item has been confirmed by the user in the real client, with notes captured.

**Goal:** The user confirms v1's Task 14 checklist plus v2 in the real client on 10.10.20.20, only when they say they are available.

**Files:** none (fixes are their own commits).

**Acceptance Criteria:**
- [ ] User available before launch; local ACE only
- [ ] Tab strip: switch tabs by click and Insert/PgUp; `+` adds tab 9; remove last local tab; local tab survives relog
- [ ] Rows continue the tab; hotkeys cast what the slot shows; Remove row disabled when it would hide spells
- [ ] Join looks seamless (no gap, aligned edges); dragging the attached Spell Deck moves both; CAST shows its label
- [ ] Remaining Task 14 items (click modes, item spell, drag cases, resize, relog, Retina, glyphs)

**Verify:** user confirmation per item.
