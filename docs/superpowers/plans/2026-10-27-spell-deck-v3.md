# Spell Deck v3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers-extended-cc:subagent-driven-development.

**Goal:** Apply spec section "v3": frame-width default (10 slots), attached resize from the Spell Deck's grips, and a client-side slot layout (any slot, gaps, duplicates) kept in sync with ACE's dense favourite lists.

**Spec:** `docs/superpowers/specs/2026-10-26-spell-deck-design.md`, section **v3** (wins over v2/v1 where they conflict).

## Global Constraints

- Worktree `~/code/OpenAC/OpenAC-spell-deck`, branch `feat/spell-deck`. Build `~/.dotnet/dotnet build ~/code/OpenAC/OpenAC-spell-deck/AcDream.slnx -v q` (warnings are errors). Tests only via `MAC_TEST_FILTER='<f>' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck`.
- Commits end with `Claude-Session: https://claude.ai/code/session_014LbLR6dmw4pYiKRAFdC9jf`.
- v3 geometry: base width `350 + 420·(stretch−1)`; slots per row 10 at stretch 1, 25 at stretch 2; attached left `deck.Left + 70·s`, bottom `deck.Top + 4·s`.
- ACE semantics: a server tab is an ordered list, each spell once; `SetFavorite(tab, pos, id)` inserts (moving an existing spell); `RemoveFavorite(tab, id)` removes. The client keeps the server list equal to the layout's distinct spells in reading order.
- Keep every earlier guarantee: nothing changes until a drop is accepted; Modern HUD off = retail unchanged; detached placement never overwritten while attached; restore order; saves never under "default"; coalesced tab-switch saves; `DeckFonts` for text.

**User decisions (already made):** default width = deck frame without the orb; attached Spell Deck grips resize both; any slot with gaps and duplicates, layout saved locally per character, server keeps each spell once in order.

---

### Task 22: Slot layout model (pure)

**Goal:** `SpellDeckLayout` per tab with placement, move/swap/copy/replace/clear, reading-order distinct list, reconcile against a server list, server-sync op planning, and min rows/columns.

**Files:** Create `src/AcDream.App/UI/Hud/SpellDeckLayout.cs`; tests `tests/AcDream.App.Tests/UI/Hud/SpellDeckLayoutTests.cs`.

**Acceptance Criteria:**
- [ ] Immutable value with `Get(row, col)`, `Place/Clear/Move/Swap/Copy/Replace` returning new layouts; `Filled` in reading order; `Distinct` = first copies in reading order
- [ ] `Reconcile(serverList, rows, cols)`: clears spells absent from the server list; places server spells missing from the layout into first empty slots in reading order within rows × cols; reports spells that did not fit
- [ ] `SyncOps(serverList)` → minimal ordered `Set`/`Remove` ops making the server list equal `Distinct` (removals first, then sets in order)
- [ ] `MaxRow` / `MaxCol` (occupied extents), `FirstEmpty(rows, cols)`, `FromList(list, cols)` (v2 migration / no saved layout)
- [ ] Tests for every bullet, including duplicates (removing one copy keeps the spell in `Distinct`), swap across rows, and reconcile with a reordered server list (layout positions win)

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SpellDeckLayout'`.

### Task 23: Settings v3, tabs source and controller on layouts

**Goal:** Layouts saved per character per tab; `SpellDeckTabs` serves layouts, reconciles server tabs and sends `SyncOps`; the controller uses layouts for slices, hotkeys, drops (any slot, swap, Ctrl-copy, replace, bookmark → first empty), remove, outline `Slot(tab,row,col)`, min rows/columns across tabs.

**Files:** Modify `SpellDeckSettings.cs`, `SettingsStore.cs` (`layouts`), `SpellDeckTabs.cs`, `SpellDeckController.cs`, `SpellDeckModel.cs`/`SpellDeckOutline.cs` (retire what layouts replace), tests; adapt `UiSpellDeck` minimally so it builds (Task 24 finishes it).

**Acceptance Criteria:**
- [ ] Settings v3 round-trip with `layouts`; v2 data migrates via `FromList` at the current slots per row
- [ ] Server-tab edits send exactly the layout's `SyncOps`; extra copies never reach the server; local tabs never call the server
- [ ] Reconcile runs on load, character change and whenever a server tab's list changes from elsewhere (spellbook favourites changed event), saving the layout once
- [ ] Drops: spellbook→empty place, spellbook→filled replace, slot→empty move, slot→filled swap, Ctrl copy, bookmark → first empty or `That spell tab is full.`; drag-off / Remove clears the slot
- [ ] `MinColumns`/`MinRows` across all tabs feed min stretch and Remove row
- [ ] Outline `Slot(tab,row,col)` with v3 navigation; hotkeys cast slot `(r, n)`
- [ ] Tests for each bullet

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SpellDeck'` then full suite.

### Task 24: Geometry v3, element drops and attached resize grips

**Goal:** Frame-width geometry (10 slots), element drop targets for every slot including empties (with Ctrl copy), attached Spell Deck right edge / top-right corner resizing both decks, dock placement at `deck.Left + 70·s`.

**Files:** Modify `SpellDeckGeometry.cs`, `UiSpellDeck.cs`/`.Draw.cs`, `SpellDeckDock.cs`, `SpellDeckWindowDockHost.cs`, `UiCommandDeck.cs` (accept forwarded resize), tests.

**Acceptance Criteria:**
- [ ] Base width `350 + 420·(stretch−1)`, `Capacity(1)=10`, `Capacity(2)=25`, `StretchFor` inverse
- [ ] Attached spot `deck.Left + 70·s`, width = deck frame width; detached same base width
- [ ] Attached: Spell Deck right edge stretches both, top-right corner scales both (deck geometry drives; Spell Deck follows); block stays on screen; detached grips independent
- [ ] Drop highlight and drop on any slot, empty or filled; Ctrl held at release → copy
- [ ] Tests (geometry at 3 scales × 2 stretches, forwarded resize through a real root, drop variants)

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SpellDeck|FullyQualifiedName~CommandDeck'` then full suite.

### Task 25: Runtime wiring v3

**Goal:** Wire layouts, reconcile triggers and the forwarded resize into `RetailUiRuntime`/`SpellDeckCharacterSync`; rig tests for layouts surviving relog, per character, and reconcile after an outside server change.

**Files:** `RetailUiRuntime.cs`, `SpellDeckCharacterSync.cs`, rig tests.

**Acceptance Criteria:**
- [ ] Layouts load/save per character with the existing guards; reconcile on load and on spellbook favourites change
- [ ] Rig: gap + duplicate layout survives relog; another character unaffected; a server-side add from elsewhere lands in the first empty slot
- [ ] Full suite 0 failures

**Verify:** full suite.

### Task 26: Live check (user gate)

> **USER-ORDERED GATE — NON-SKIPPABLE.** Close only after the user confirms each item in the real client.

**Goal:** The user confirms v3 plus the open v1/v2 items live on 10.10.20.20.

**Files:** none.

**Acceptance Criteria:**
- [ ] Default width matches the deck frame; Spell Deck grips resize both when attached
- [ ] Place a spell in any slot (row 2 slot 1 with gaps), duplicate with Ctrl, swap, replace, bookmark move, remove; layout survives relog
- [ ] Remaining v1/v2 items (tabs, +, click modes, item spell, drag cases, detach/attach, Retina, glyphs, two-digit bookmarks, seam look)

**Verify:** user confirmation per item.
