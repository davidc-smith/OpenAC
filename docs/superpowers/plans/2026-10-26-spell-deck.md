# Spell Deck Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Modern HUD spell bar ("Spell Deck") that attaches on top of the Command Deck in magic mode, with 1–10 rows of favourite-spell slots, an item-spell slot, multi-row hotkeys and a CAST button on the deck.

**Architecture:** Pure units (geometry, model, outline rules, settings) under `src/AcDream.App/UI/Hud/`, each unit-tested. They feed a `SpellDeckController` that holds the per-character state and talks to the game through `ISpellDeckActions`. A `UiSpellDeck` element draws it and turns pointer input into controller calls, and a `SpellDeckDock` ties it to the Command Deck. `RetailUiRuntime` mounts everything and routes hotkeys. It is client-only, with no plugin contract change.

**Tech Stack:** C# / .NET (`~/.dotnet` SDK), xUnit, the retained UI (`UiElement`, `UiRoot`, `RetailWindowFrame`), `SettingsStore` JSON.

**Spec:** `docs/superpowers/specs/2026-10-26-spell-deck-design.md` (branch `docs/spell-deck-spec`, 1a0f91b2). Read it before every task.

## Global Constraints

- **Where to work:** worktree `~/code/OpenAC/OpenAC-spell-deck` on branch `feat/spell-deck`, created from fork `main` (`d996784a` or later). Merge `docs/spell-deck-spec` in first, so the spec and plan travel with the branch: `git merge --no-ff docs/spell-deck-spec`.
- **Tests:** `MAC_TEST_FILTER='<filter>' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck`. Never pass a second `--filter`. Never run tests the script skips. Build first: `~/.dotnet/dotnet build ~/code/OpenAC/OpenAC-spell-deck/AcDream.sln -v q`.
- **Fork-only, client-only:** no plugin contract change and no `-fork.n` bump.
- **Keep the retail behaviour:** with Modern HUD off, everything behaves exactly as today. The existing tests for `SpellcastingUiController`, the keyboard page, `ModernHudSwitch` and the Command Deck must still pass.
- **New `InputAction` values go at the end of the enum.** Bindings are saved by name, but code depends on the ordering.
- **Geometry constants** (unscaled points; every rectangle is multiplied by `Scale`):
  `BaseWidth = 420` (shared with `DeckGeometry`), `Pad = 6`, `Gutter = 40`,
  `ChipWidth = 20`, `ChipGap = 4`, `SlotSize = 26`, `SlotPitch = 28`,
  `RowPitch = 28`, `TopPad = 7`, `BottomPad = 7`, `SlotsLeft = 70`,
  `ItemSize = 30`. Height = `12 + 28 × rows`. Slots per row =
  `floor((420 × stretch − 74) / 28)`, which is 12 at stretch 1 and 27 at
  stretch 2.
- **Fonts:** HUD text uses `DeckFonts` (bundled sans), never `ctx.DefaultFont` directly. The user asked for this on the Command Deck.
- **Every commit message ends with:**
  `Claude-Session: https://claude.ai/code/session_014LbLR6dmw4pYiKRAFdC9jf`
- **Live tests:** local ACE only (10.10.20.20 via `~/OpenAC-dev/dev-client.env`), never Dreamweave, and only after the user says they are available.

**User decisions (already made):**
- Separate window that attaches on top of the Command Deck. It shows only in magic combat with Modern HUD on.
- Each row is its own bar with its own tab (1–8), shown as a plain number with no retail tab names. There are 1–10 rows, added and removed by right-click.
- The item-spell slot is centred top to bottom on the left gutter.
- While attached it is fully tied to the deck's scale and stretch. Detached, it resizes with the same pattern as the deck.
- No overflow chip. The bar can't be made narrower than the fullest row, and spells past what fits are not shown and can't be cast.
- The CAST button lives on the deck's CASTING row and replaces RECAST.
- Two click modes. The default is "click selects, double-click casts"; the option is "Click casts".
- Hotkeys: row 1 keeps `UseSpellSlot_n`, row 2 defaults to Shift+1–9, row 3 to Ctrl+1–9, rows 4–10 start unbound, plus a new `CastItemSpell` (unbound).
- Codex review fixes are all accepted. For the screen edge: Add row is disabled when it would not fit, and the deck moves down on restore.
- **Plan-level adaptation:** `UiMarkupContextMenu` has no submenus, so "Show tab ▸" becomes flat items "Show tab 1" … "Show tab 8". The current tab reads "Show tab 4 (shown)", and the click mode reads "Click casts: on/off". These are ASCII only, because the menu font may lack ✓ or •.

---

## File map

| File | Status | Responsibility |
|---|---|---|
| `src/AcDream.App/Spells/ItemSpellSource.cs` | new | Wielded caster's spell lookup |
| `src/AcDream.App/UI/Layout/SpellcastingUiController.cs` | modify | Use `ItemSpellSource` |
| `src/AcDream.UI.Abstractions/Input/InputAction.cs` | modify | 108 row actions + `CastItemSpell` |
| `src/AcDream.UI.Abstractions/Input/SpellBarActions.cs` | new | Row/slot ↔ action mapping |
| `src/AcDream.UI.Abstractions/Input/ClientActionCatalog.cs` | new | Client-only actions for the keyboard page |
| `src/AcDream.UI.Abstractions/Input/KeyBindings.cs` | modify | Defaults for rows 2–3 |
| `src/AcDream.App/UI/Layout/KeyboardConfigController.cs` | modify | Merge catalog rows into the combat page |
| `src/AcDream.App/UI/Hud/SpellDeckKeyLabel.cs` | new | Short slot labels from bindings |
| `src/AcDream.App/UI/Hud/SpellDeckGeometry.cs` | new | Sizes, rectangles, hit-testing |
| `src/AcDream.App/UI/Hud/SpellDeckModel.cs` | new | Visible slots, drop plans, min slots, new-row tab |
| `src/AcDream.App/UI/Hud/SpellDeckOutline.cs` | new | Outline value + transition rules |
| `src/AcDream.App/UI/Hud/SpellDeckSettings.cs` | new | Rows + attached; validation |
| `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs`, `MiscSettings.cs` | modify | `spellDeck` section; `SpellDeckClickCasts` |
| `src/AcDream.App/UI/Hud/DeckGeometry.cs`, `UiCommandDeck*.cs`, `CommandDeckModel.cs`, `ICommandDeckActions.cs` | modify | Min stretch, move limit, CAST |
| `src/AcDream.App/UI/Hud/ISpellDeckActions.cs` | new | Game calls |
| `src/AcDream.App/UI/Hud/SpellDeckController.cs` | new | State, hotkeys, outline upkeep, drops |
| `src/AcDream.App/UI/Hud/UiSpellDeck.cs`, `UiSpellDeck.Draw.cs` | new | Element: draw, click modes, drag, menu, tooltips |
| `src/AcDream.App/UI/RetailWindowLayoutPersistence.cs` | modify | Per-window capture gate |
| `src/AcDream.App/UI/Hud/SpellDeckDock.cs` | new | Attach / detach / size coupling / on-screen block |
| `src/AcDream.App/UI/Hud/ModernHudSwitch.cs` | modify | Show the Spell Deck in magic |
| `src/AcDream.App/UI/RetailUiRuntime.cs`, `WindowNames.cs` | modify | Mount, routing, wiring |
| `assets/hud/cast.svg` | new | CAST icon |

Tests mirror each file under `tests/AcDream.App.Tests/UI/Hud/` (App), `tests/AcDream.UI.Abstractions.Tests/Input/` (actions and catalog), `tests/AcDream.UI.Abstractions.Tests/Panels/Settings/` (store) and `tests/AcDream.App.Tests/Spells/` (item spell).

---

### Task 1: ItemSpellSource (shared item-spell lookup)

**Goal:** Move the "wielded caster with a spell" lookup out of `SpellcastingUiController` into `ItemSpellSource`, with no behaviour change.

**Files:**
- Create: `src/AcDream.App/Spells/ItemSpellSource.cs`
- Modify: `src/AcDream.App/UI/Layout/SpellcastingUiController.cs:643-652` (`UpdateEndowment`)
- Test: `tests/AcDream.App.Tests/Spells/ItemSpellSourceTests.cs`

**Acceptance Criteria:**
- [ ] `ItemSpellSource.Find(objects, playerGuid)` returns the held caster with a non-zero `SpellId`, else null
- [ ] Items that are not held, not casters, or have no spell are ignored
- [ ] `SpellcastingUiController.UpdateEndowment` calls it; all existing `SpellcastingUiController` tests pass

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ItemSpellSource|FullyQualifiedName~Spellcasting' run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** Look at how the existing `SpellcastingUiController` tests build a `ClientObjectTable` with equipped items (`grep -rn "GetEquippedBy\|CurrentlyEquippedLocation" tests/AcDream.App.Tests | head`) and build the objects the same way.

```csharp
using AcDream.App.Spells;
using AcDream.Core.Items;

namespace AcDream.App.Tests.Spells;

public sealed class ItemSpellSourceTests
{
    private const uint Player = 0x50000001u;

    [Fact]
    public void Find_HeldCasterWithSpell_ReturnsItAndItsSpell()
    {
        var objects = new ClientObjectTable();
        AddEquipped(objects, 0x70000001u, ItemType.Caster, EquipMask.Held, spellId: 1234u);

        ItemSpell? found = ItemSpellSource.Find(objects, Player);

        Assert.Equal(new ItemSpell(0x70000001u, 1234u), found);
    }

    [Theory]
    [InlineData(ItemType.Caster, (uint)EquipMask.Held, 0u)]          // no spell
    [InlineData(ItemType.MeleeWeapon, (uint)EquipMask.Held, 99u)]     // not a caster
    [InlineData(ItemType.Caster, (uint)EquipMask.HeadWear, 99u)]      // not held
    public void Find_IgnoresItemsThatDoNotQualify(ItemType type, uint location, uint spellId)
    {
        var objects = new ClientObjectTable();
        AddEquipped(objects, 0x70000002u, type, (EquipMask)location, spellId);

        Assert.Null(ItemSpellSource.Find(objects, Player));
    }

    // Same construction as SpellcastingUiControllerTests (an Orb wielded by the player).
    private static void AddEquipped(ClientObjectTable objects, uint id, ItemType type, EquipMask where, uint spellId)
    {
        objects.AddOrUpdate(new ClientObject { ObjectId = Player, Name = "Player" });
        objects.AddOrUpdate(new ClientObject
        {
            ObjectId = id,
            Name = "Orb",
            Type = type,
            WielderId = Player,
            CurrentlyEquippedLocation = where,
            SpellId = spellId == 0u ? null : spellId,
        });
    }
}
```

If `SpellId` is a non-nullable `uint` on `ClientObject`, pass `spellId` directly. `ItemSpellSource` treats 0 as no spell either way.

- [ ] **Step 2: Run, expect a compile failure** (`ItemSpellSource` doesn't exist yet).

- [ ] **Step 3: Implement.**

```csharp
using AcDream.Core.Items;

namespace AcDream.App.Spells;

/// <summary>A wielded item that carries a castable spell.</summary>
public readonly record struct ItemSpell(uint ItemId, uint SpellId);

/// <summary>
/// Finds the spell on the player's wielded caster: the retail spell bar's
/// endowment slot and the Spell Deck's item slot both read it from here.
/// </summary>
public static class ItemSpellSource
{
    public static ItemSpell? Find(ClientObjectTable objects, uint playerGuid)
    {
        ClientObject? item = objects.GetEquippedBy(playerGuid)
            .FirstOrDefault(o =>
                (o.CurrentlyEquippedLocation & EquipMask.Held) != 0
                && (o.Type & ItemType.Caster) != 0
                && o.SpellId.GetValueOrDefault() != 0u);
        return item is null ? null : new ItemSpell(item.ObjectId, item.SpellId!.Value);
    }
}
```

In `SpellcastingUiController.UpdateEndowment`, replace the `FirstOrDefault` query with:

```csharp
ItemSpell? found = ItemSpellSource.Find(_objects, _playerGuid());
ClientObject? endowment = found is { } f ? _objects.Get(f.ItemId) : null;
```

Leave the rest of the method unchanged and add `using AcDream.App.Spells;` (already present).

- [ ] **Step 4: Run the Verify command and expect it to pass.**
- [ ] **Step 5: Commit** `Spell Deck: share the wielded item-spell lookup`.

---

### Task 2: Spell bar input actions, defaults and catalog

**Goal:** Add the row hotkey actions and `CastItemSpell`, their default keys, the row/slot mapping, and the client-action catalog for the keyboard page.

**Files:**
- Modify: `src/AcDream.UI.Abstractions/Input/InputAction.cs` (append at the end)
- Create: `src/AcDream.UI.Abstractions/Input/SpellBarActions.cs`
- Create: `src/AcDream.UI.Abstractions/Input/ClientActionCatalog.cs`
- Modify: `src/AcDream.UI.Abstractions/Input/KeyBindings.cs:213-218` (after the `UseSpellSlot` loop in `RetailDefaults`)
- Test: `tests/AcDream.UI.Abstractions.Tests/Input/SpellBarActionsTests.cs`

**Acceptance Criteria:**
- [ ] `SpellBarActions.ForSlot(row, slot)` gives `UseSpellSlot_{slot+1}` for row 0 and `UseSpellRow{row+1}Slot{slot+1}` for rows 1–9; `TryParse` inverts it; `CastItemSpell` exists
- [ ] `RetailDefaults()` binds Shift+1–9 → row 2 slots 1–9 and Ctrl+1–9 → row 3 slots 1–9, in `InputScope.MagicCombat`; nothing else new is bound; Ctrl+0 stays `CreateShortcut`
- [ ] `ClientActionCatalog.Entries` lists all 108 row actions and `CastItemSpell`, each with input map `0x10000005`, a unique action id ≥ `0xC0000000`, a readable label and group `"Spell bar"`
- [ ] A keymap saved with `SaveToFile` and loaded with `LoadOrDefault` keeps a rebinding of a new action

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SpellBarActions|FullyQualifiedName~KeyBindings' run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.**

```csharp
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.UI.Abstractions.Tests.Input;

public sealed class SpellBarActionsTests
{
    [Fact]
    public void Row0_IsTheRetailSpellSlots()
    {
        Assert.Equal(InputAction.UseSpellSlot_1, SpellBarActions.ForSlot(0, 0));
        Assert.Equal(InputAction.UseSpellSlot_10, SpellBarActions.ForSlot(0, 9));
        Assert.Equal(InputAction.UseSpellSlot_12, SpellBarActions.ForSlot(0, 11));
    }

    [Fact]
    public void LaterRows_AreTheNewActions_AndParseBack()
    {
        Assert.Equal(InputAction.UseSpellRow2Slot1, SpellBarActions.ForSlot(1, 0));
        Assert.Equal(InputAction.UseSpellRow10Slot12, SpellBarActions.ForSlot(9, 11));
        for (int row = 0; row < SpellBarActions.Rows; row++)
            for (int slot = 0; slot < SpellBarActions.SlotsPerRow; slot++)
            {
                Assert.True(SpellBarActions.TryParse(SpellBarActions.ForSlot(row, slot), out int r, out int s));
                Assert.Equal((row, slot), (r, s));
            }
        Assert.False(SpellBarActions.TryParse(InputAction.CastItemSpell, out _, out _));
    }

    [Fact]
    public void Defaults_Row2Shift_Row3Ctrl_InMagicScope()
    {
        KeyBindings b = KeyBindings.RetailDefaults();
        Binding row2 = Assert.Single(b.ForAction(InputAction.UseSpellRow2Slot4));
        Assert.Equal(new KeyChord(Key.Number4, ModifierMask.Shift), row2.Chord);
        Assert.Equal(InputScope.MagicCombat, row2.Scope);
        Binding row3 = Assert.Single(b.ForAction(InputAction.UseSpellRow3Slot9));
        Assert.Equal(new KeyChord(Key.Number9, ModifierMask.Ctrl), row3.Chord);
        Assert.Empty(b.ForAction(InputAction.UseSpellRow3Slot10));
        Assert.Empty(b.ForAction(InputAction.UseSpellRow4Slot1));
        Assert.Empty(b.ForAction(InputAction.CastItemSpell));
        Assert.Contains(b.ForAction(InputAction.CreateShortcut),
            x => x.Chord == new KeyChord(Key.Number0, ModifierMask.Ctrl));
    }

    [Fact]
    public void Catalog_ListsEveryClientAction_WithUniqueIds()
    {
        IReadOnlyList<ClientActionEntry> e = ClientActionCatalog.Entries;
        Assert.Equal(108 + 1, e.Count);
        Assert.All(e, x => Assert.Equal(0x10000005u, x.InputMapId));
        Assert.All(e, x => Assert.True(x.ActionId >= 0xC0000000u));
        Assert.Equal(e.Count, e.Select(x => x.ActionId).Distinct().Count());
        Assert.Contains(e, x => x.Action == InputAction.UseSpellRow2Slot4 && x.Label == "Spell bar row 2, slot 4");
        Assert.Contains(e, x => x.Action == InputAction.CastItemSpell && x.Label == "Cast item spell");
        Assert.All(e, x => Assert.Equal("Spell bar", x.Group));
    }

    [Fact]
    public void Rebinding_ANewAction_SurvivesSaveAndLoad()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kb-{Guid.NewGuid():N}.json");
        try
        {
            KeyBindings b = KeyBindings.RetailDefaults();
            b.Add(new Binding(new KeyChord(Key.F7, ModifierMask.None), InputAction.CastItemSpell,
                Scope: InputScope.MagicCombat));
            b.SaveToFile(path);
            KeyBindings loaded = KeyBindings.LoadOrDefault(path);
            Binding cast = Assert.Single(loaded.ForAction(InputAction.CastItemSpell));
            Assert.Equal(Key.F7, cast.Chord.Key);
            Assert.Equal(InputScope.MagicCombat, cast.Scope);
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: Implement.** Append to the **end** of `InputAction` (after `ToggleCharacterOptionSideBySideVitals`). Generate the 108 names with a short script rather than typing them, so none are missed:

```bash
python3 - <<'EOF'
print("\n    // Spell Deck rows 2-10 (row 1 is UseSpellSlot_1..12). Client-only.")
for r in range(2, 11):
    print("    " + " ".join(f"UseSpellRow{r}Slot{s}," for s in range(1, 13)))
print("    CastItemSpell,")
EOF
```

Paste the output before the closing `}`.

`SpellBarActions.cs`:

```csharp
namespace AcDream.UI.Abstractions.Input;

/// <summary>The Spell Deck's hotkey actions: row 0 is the retail
/// <c>UseSpellSlot_1..12</c>, rows 1-9 the client-only <c>UseSpellRow{n}Slot{m}</c>.
/// Rows and slots are zero-based here.</summary>
public static class SpellBarActions
{
    public const int Rows = 10;
    public const int SlotsPerRow = 12;

    private static readonly InputAction[] Row0 =
    [
        InputAction.UseSpellSlot_1, InputAction.UseSpellSlot_2, InputAction.UseSpellSlot_3,
        InputAction.UseSpellSlot_4, InputAction.UseSpellSlot_5, InputAction.UseSpellSlot_6,
        InputAction.UseSpellSlot_7, InputAction.UseSpellSlot_8, InputAction.UseSpellSlot_9,
        InputAction.UseSpellSlot_10, InputAction.UseSpellSlot_11, InputAction.UseSpellSlot_12,
    ];

    public static InputAction ForSlot(int row, int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, SlotsPerRow);
        return row == 0
            ? Row0[slot]
            : InputAction.UseSpellRow2Slot1 + (row - 1) * SlotsPerRow + slot;
    }

    public static bool TryParse(InputAction action, out int row, out int slot)
    {
        int index = Array.IndexOf(Row0, action);
        if (index >= 0) { row = 0; slot = index; return true; }
        int offset = action - InputAction.UseSpellRow2Slot1;
        if (offset >= 0 && offset < (Rows - 1) * SlotsPerRow)
        {
            row = 1 + offset / SlotsPerRow;
            slot = offset % SlotsPerRow;
            return true;
        }
        row = slot = -1;
        return false;
    }
}
```

`ClientActionCatalog.cs`:

```csharp
namespace AcDream.UI.Abstractions.Input;

/// <summary>A client-only action shown on the keyboard page. The retail
/// action map has no row for it, so it carries its own identity: the magic
/// combat input map (so conflicts are checked against the spell keys) and an
/// action id in a range retail never uses.</summary>
public sealed record ClientActionEntry(
    InputAction Action, uint InputMapId, uint ActionId, string Label, string Group);

public static class ClientActionCatalog
{
    public const uint MagicCombatInputMap = 0x10000005u;
    public const uint FirstClientActionId = 0xC0000000u;
    public const string SpellBarGroup = "Spell bar";

    public static IReadOnlyList<ClientActionEntry> Entries { get; } = Build();

    private static List<ClientActionEntry> Build()
    {
        var list = new List<ClientActionEntry>();
        uint id = FirstClientActionId;
        for (int row = 1; row < SpellBarActions.Rows; row++)
            for (int slot = 0; slot < SpellBarActions.SlotsPerRow; slot++)
                list.Add(new(SpellBarActions.ForSlot(row, slot), MagicCombatInputMap, id++,
                    $"Spell bar row {row + 1}, slot {slot + 1}", SpellBarGroup));
        list.Add(new(InputAction.CastItemSpell, MagicCombatInputMap, id, "Cast item spell", SpellBarGroup));
        return list;
    }
}
```

In `KeyBindings.RetailDefaults()`, directly after the `UseSpellSlot` 1–9 loop:

```csharp
        // Spell Deck rows 2 and 3: Shift / Ctrl + 1-9. Rows 4-10 and slots 10-12 start
        // unbound (Ctrl+0 is CreateShortcut).
        for (int i = 1; i <= 9; i++)
        {
            var k = (Key)((int)Key.Number0 + i);
            b.Add(new(new KeyChord(k, ModifierMask.Shift), SpellBarActions.ForSlot(1, i - 1), Scope: InputScope.MagicCombat));
            b.Add(new(new KeyChord(k, ModifierMask.Ctrl), SpellBarActions.ForSlot(2, i - 1), Scope: InputScope.MagicCombat));
        }
```

If an existing test pins the full default count or a "no unknown actions" list (`grep -rn "RetailDefaults()" tests/AcDream.UI.Abstractions.Tests | head`), update its expected numbers and say so in the commit message.

- [ ] **Step 4: Run the Verify command and expect it to pass.** Also run `MAC_TEST_FILTER='FullyQualifiedName~Input' …` to catch exhaustiveness tests over `InputAction` (for example identity-table or label coverage). Where such a test requires every action to have a retail identity, exclude `ClientActionCatalog` actions in the test, with a comment.
- [ ] **Step 5: Commit** `Spell Deck: row hotkey actions, defaults and client action catalog`.

---

### Task 3: Keyboard page lists the client actions

**Goal:** The Combat page of Configure Keyboard shows a "Spell bar" group with every `ClientActionCatalog` entry, which you can rebind like a retail row, with conflict detection.

**Files:**
- Modify: `src/AcDream.App/UI/Layout/KeyboardConfigController.cs` (`Bind` and `BuildActionRow`)
- Test: `tests/AcDream.App.Tests/UI/Layout/KeyboardConfigControllerClientActionTests.cs`

**Acceptance Criteria:**
- [ ] The Combat page ends with a header row reading `Spell bar` followed by one row per catalog entry, in catalog order
- [ ] Each client row's `RowView.MappedAction` is the entry's action, and `Label` is the entry's label
- [ ] Setting a chord on a client row calls `Bindings.SetForAction(action, …)` with scope `MagicCombat`, activation `Press`
- [ ] Capturing a chord already used by a magic-combat retail row (for example `1` on `UseSpellSlot_1`) goes through the existing conflict flow

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~KeyboardConfigController' run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck` → all pass (old and new)

**Steps:**

- [ ] **Step 1: Read the existing tests** (`grep -rln "KeyboardConfigController" tests/AcDream.App.Tests`) to see how they build a layout, snapshot and `Bindings` record. Write the new tests using the same fixture. They should check:
  1. `controller.Rows.Where(r => r.MappedAction is { } a && ClientActionCatalog.Entries.Any(e => e.Action == a))` has count 109, in catalog order, with labels matching.
  2. Driving a row's `Model.Apply` (as the existing rebinding tests do) with `KeyChord(Key.F7, None)` on the `CastItemSpell` row records `SetForAction(CastItemSpell, [Binding(F7, CastItemSpell, Press, MagicCombat)])`.
  3. A conflict test copied from the existing retail conflict test, but capturing `Number1` on the row-2-slot-1 client row while `UseSpellSlot_1` holds `Number1`, gives the same conflict outcome the retail test expects.

- [ ] **Step 2: Run, expect failures** (no client rows yet).

- [ ] **Step 3: Implement.**
  - Turn the per-row inputs into a spec so retail and client rows share one builder:

```csharp
    private sealed record RowSpec(
        uint InputMapId,
        uint ActionId,
        InputAction? Action,
        string? Label,
        string? Tooltip,
        IReadOnlyList<KeyChord> Defaults);
```

  - Rename `BuildActionRow(listBox, RetailActionMapRow row, resolveString, bindings)` to a thin wrapper that builds a `RowSpec` from the DAT row (exactly what it computes today: `TryResolve`, label and tooltip from `resolveString`, `DatDefaultsToChords(row.DefaultBindings)`) and calls a new `BuildRow(listBox, RowSpec spec, Bindings bindings)` holding the rest of today's body. Replace `row.InputMapId` / `row.ActionId` / `mapped` / `action` with the spec fields. For a mapped row the template is still `(liveBindings[0].Activation, liveBindings[0].Scope)` when bound, else `(RetailActionIdentityTable.ActivationFor(...), RetailActionIdentityTable.ScopeForInputMap(spec.InputMapId))`. `ScopeForInputMap(0x10000005)` already gives `MagicCombat`, so client rows need no special case. `ActivationFor` returns `Press` for unknown ids; check this in the source, and if it doesn't, add a `spec.ActionId >= ClientActionCatalog.FirstClientActionId ? ActivationType.Press :` guard.
  - Add `BuildClientHeaderRow(UiTemplateListBox listBox, string label)`. It works like `BuildHeaderRow`, but sets `LinesProvider` from the literal label with no string-table lookup.
  - In `Bind`, after the `foreach (var inputMapGroup in byInputMap)` loop, when `cls == RetailActionClass.Combat`:

```csharp
            if (cls == RetailActionClass.Combat)
            {
                BuildClientHeaderRow(listBox, ClientActionCatalog.SpellBarGroup);
                foreach (ClientActionEntry entry in ClientActionCatalog.Entries)
                    controller.BuildRow(listBox, new RowSpec(
                        entry.InputMapId, entry.ActionId, entry.Action, entry.Label,
                        Tooltip: null, Defaults: KeyBindings.RetailDefaults()
                            .ForAction(entry.Action).Select(b => b.Chord).ToArray()), bindings);
            }
```

  Compute `KeyBindings.RetailDefaults()` once before the loop, not once per entry.
  - Conflict detection: `FindConflicts` works on `_rows` and their `InputMapId`, so client rows take part automatically through `snapshot.InputMapsConflict(0x10000005, …)`. Check that it doesn't skip rows whose `ActionId` is missing from the snapshot. If it looks up the snapshot by `(InputMapId, ActionId)`, treat client rows as "user-bindable", the same as mapped retail rows.

- [ ] **Step 4: Run the Verify command and expect everything to pass.**
- [ ] **Step 5: Commit** `Spell Deck: list the spell bar actions on the keyboard page`.

---

### Task 4: SpellDeckGeometry

**Goal:** Pure geometry for the Spell Deck: size from scale, stretch and rows; slot, chip and item rectangles; capacity; minimum stretch; and hit-testing.

**Files:**
- Create: `src/AcDream.App/UI/Hud/SpellDeckGeometry.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/SpellDeckGeometryTests.cs`

**Acceptance Criteria:**
- [ ] `Capacity(1f) == 12`, `Capacity(2f) == 27`, `MaxCapacity == 27`
- [ ] `StretchFor(12) == 1f`; `Capacity(StretchFor(n)) >= n` for n = 1…27; `StretchFor(40) == 2f`
- [ ] Height is `(12 + 28·rows)·scale`; width `420·stretch·scale`
- [ ] The item slot is vertically centred for 1, 3 and 10 rows
- [ ] `HitAt` finds item, chip (row) and slot (row, index) at scales 0.75 and 1.5; a point past the last slot returns `Frame`
- [ ] `FromSize(width, height, rows)` round-trips `Create(scale, stretch, rows)` for scale ∈ {0.75, 1, 1.5}, stretch ∈ {1, 1.5, 2}

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SpellDeckGeometry' run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.**

```csharp
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckGeometryTests
{
    [Fact]
    public void Capacity_AtTheStretchEnds()
    {
        Assert.Equal(12, SpellDeckGeometry.Capacity(1f));
        Assert.Equal(27, SpellDeckGeometry.Capacity(2f));
        Assert.Equal(27, SpellDeckGeometry.MaxCapacity);
    }

    [Fact]
    public void StretchFor_IsTheSmallestStretchThatFits()
    {
        Assert.Equal(1f, SpellDeckGeometry.StretchFor(12));
        for (int n = 1; n <= 27; n++)
            Assert.True(SpellDeckGeometry.Capacity(SpellDeckGeometry.StretchFor(n)) >= n, $"n={n}");
        Assert.True(SpellDeckGeometry.Capacity(SpellDeckGeometry.StretchFor(20) - 0.01f) < 20);
        Assert.Equal(2f, SpellDeckGeometry.StretchFor(40));
    }

    [Theory]
    [InlineData(1, 1f, 40f)]
    [InlineData(3, 1f, 96f)]
    [InlineData(10, 1.5f, 438f)]
    public void Height_FollowsRowsAndScale(int rows, float scale, float height) =>
        Assert.Equal(height, SpellDeckGeometry.Create(scale, 1f, rows).Height, 3);

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(10)]
    public void ItemSlot_IsCentredVertically(int rows)
    {
        var g = SpellDeckGeometry.Create(1f, 1f, rows);
        DeckRect item = g.ItemSlot;
        Assert.Equal(g.Height / 2f, item.Y + item.H / 2f, 3);
    }

    [Theory]
    [InlineData(0.75f)] [InlineData(1.5f)]
    public void HitAt_FindsEachPart(float scale)
    {
        var g = SpellDeckGeometry.Create(scale, 1f, 3);
        DeckRect s = g.Slot(2, 5), c = g.TabChip(1), i = g.ItemSlot;
        Assert.Equal(new SpellDeckHit(SpellDeckZone.Slot, 2, 5), g.HitAt(s.X + 1, s.Y + 1));
        Assert.Equal(new SpellDeckHit(SpellDeckZone.TabChip, 1, -1), g.HitAt(c.X + 1, c.Y + 1));
        Assert.Equal(new SpellDeckHit(SpellDeckZone.Item, -1, -1), g.HitAt(i.X + i.W / 2, i.Y + i.H / 2));
        Assert.Equal(SpellDeckZone.Frame, g.HitAt(g.Width - 1, g.Height / 2).Zone);
        Assert.Equal(SpellDeckZone.None, g.HitAt(-1, -1).Zone);
    }

    [Theory]
    [InlineData(0.75f, 1f)] [InlineData(1f, 1.5f)] [InlineData(1.5f, 2f)]
    public void FromSize_RoundTrips(float scale, float stretch)
    {
        var g = SpellDeckGeometry.Create(scale, stretch, 4);
        var back = SpellDeckGeometry.FromSize(g.Width, g.Height, 4);
        Assert.Equal(g.Scale, back.Scale);
        Assert.Equal(g.Stretch, back.Stretch, 3);
    }
}
```

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: Implement.**

```csharp
namespace AcDream.App.UI.Hud;

public enum SpellDeckZone { None, Frame, Item, TabChip, Slot }

/// <summary>What a point on the Spell Deck is over. Row and Slot are -1 when not used.</summary>
public readonly record struct SpellDeckHit(SpellDeckZone Zone, int Row, int Slot);

/// <summary>
/// The Spell Deck's geometry in points for one size. Like <see cref="DeckGeometry"/>:
/// <see cref="Scale"/> multiplies everything and <see cref="Stretch"/> widens it
/// (more slots per row, never bigger slots). The height follows the row count.
/// Scale changes the size, never the slot count.
/// </summary>
public sealed record SpellDeckGeometry
{
    public const float BaseWidth = DeckGeometry.BaseWidth;
    public const int MinRows = 1;
    public const int MaxRows = 10;
    private const float Pad = 6f, Gutter = 40f, ChipWidth = 20f, ChipGap = 4f;
    private const float SlotSize = 26f, SlotPitch = 28f, RowPitch = 28f, TopPad = 7f, BottomPad = 7f;
    private const float SlotsLeft = Pad + Gutter + ChipWidth + ChipGap;   // 70
    private const float ItemSize = 30f;

    public float Scale { get; }
    public float Stretch { get; }
    public int Rows { get; }

    private SpellDeckGeometry(float scale, float stretch, int rows) { Scale = scale; Stretch = stretch; Rows = rows; }

    public static SpellDeckGeometry Create(float scale, float stretch, int rows) => new(
        DeckGeometry.SnapScale(scale),
        Math.Clamp(float.IsFinite(stretch) ? stretch : 1f, DeckGeometry.MinStretch, DeckGeometry.MaxStretch),
        Math.Clamp(rows, MinRows, MaxRows));

    public static float BaseHeight(int rows) => TopPad + rows * RowPitch - (RowPitch - SlotSize) + BottomPad;

    public float Width => BaseWidth * Stretch * Scale;
    public float Height => BaseHeight(Rows) * Scale;
    public int SlotsPerRow => Capacity(Stretch);

    /// <summary>Slots that fit in one row at a stretch.</summary>
    public static int Capacity(float stretch) =>
        (int)MathF.Floor((BaseWidth * stretch - SlotsLeft - Pad + (SlotPitch - SlotSize)) / SlotPitch + 1e-4f);

    public static int MaxCapacity => Capacity(DeckGeometry.MaxStretch);

    /// <summary>The smallest stretch whose rows hold <paramref name="slots"/> slots (clamped to the stretch range).</summary>
    public static float StretchFor(int slots)
    {
        float width = SlotsLeft + Pad + slots * SlotPitch - (SlotPitch - SlotSize);
        return Math.Clamp(width / BaseWidth, DeckGeometry.MinStretch, DeckGeometry.MaxStretch);
    }

    /// <summary>The geometry for a window size and row count: scale from the height, stretch from the width.</summary>
    public static SpellDeckGeometry FromSize(float width, float height, int rows)
    {
        rows = Math.Clamp(rows, MinRows, MaxRows);
        if (!float.IsFinite(width) || !float.IsFinite(height)) return Create(1f, 1f, rows);
        float scale = DeckGeometry.SnapScale(height / BaseHeight(rows));
        return Create(scale, width / (BaseWidth * scale), rows);
    }

    public SpellDeckGeometry WithRows(int rows) => Create(Scale, Stretch, rows);
    public SpellDeckGeometry WithStretch(float stretch) => Create(Scale, stretch, Rows);
    public SpellDeckGeometry WithWidth(float width) => Create(Scale, width / (BaseWidth * Scale), Rows);

    /// <summary>A corner resize measured from this geometry (the drag start), as <see cref="DeckGeometry.WithCornerSize"/>.</summary>
    public SpellDeckGeometry WithCornerSize(float width, float height)
    {
        float fromWidth = width / (BaseWidth * Stretch);
        float fromHeight = height / BaseHeight(Rows);
        float raw = MathF.Abs(fromWidth - Scale) > MathF.Abs(fromHeight - Scale) ? fromWidth : fromHeight;
        return Create(raw, Stretch, Rows);
    }

    private DeckRect S(float x, float y, float w, float h) => new(x * Scale, y * Scale, w * Scale, h * Scale);

    public DeckRect Frame => new(0f, 0f, Width, Height);
    public DeckRect ItemSlot => S(Pad + (Gutter - ItemSize) / 2f, (BaseHeight(Rows) - ItemSize) / 2f, ItemSize, ItemSize);
    public DeckRect Divider => S(Pad + Gutter - 1f, TopPad, 1f, BaseHeight(Rows) - TopPad - BottomPad);
    public DeckRect TabChip(int row) => S(Pad + Gutter, TopPad + row * RowPitch, ChipWidth, SlotSize);
    public DeckRect Slot(int row, int index) => S(SlotsLeft + index * SlotPitch, TopPad + row * RowPitch, SlotSize, SlotSize);

    public SpellDeckHit HitAt(float x, float y)
    {
        if (!Frame.Contains(x, y)) return new(SpellDeckZone.None, -1, -1);
        if (ItemSlot.Contains(x, y)) return new(SpellDeckZone.Item, -1, -1);
        for (int row = 0; row < Rows; row++)
        {
            if (TabChip(row).Contains(x, y)) return new(SpellDeckZone.TabChip, row, -1);
            DeckRect first = Slot(row, 0);
            if (y < first.Y || y >= first.Y + first.H || x < first.X) continue;
            int index = (int)((x - first.X) / (SlotPitch * Scale));
            if (index < SlotsPerRow && Slot(row, index).Contains(x, y))
                return new(SpellDeckZone.Slot, row, index);
        }
        return new(SpellDeckZone.Frame, -1, -1);
    }
}
```

- [ ] **Step 4: Run the Verify command and expect it to pass.**
- [ ] **Step 5: Commit** `Spell Deck: geometry`.

---

### Task 5: SpellDeckModel (visible slots, drops, min slots, new rows)

**Goal:** The pure rules for what each row shows, the narrowest width, which tab a new row gets, and how a drop turns into favourite operations (the drag transaction).

**Files:**
- Create: `src/AcDream.App/UI/Hud/SpellDeckModel.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/SpellDeckModelTests.cs`

**Acceptance Criteria:**
- [ ] `Visible(tab, capacity)` gives the first `capacity` spells
- [ ] `MinSlots(rows, favorites)` is the largest tab count among the shown rows, capped at `MaxCapacity`, and at least 1
- [ ] `TabForNewRow(rows)` is the lowest tab 0–7 no row shows, else 0
- [ ] `PlanDrop`:
  - same-tab move (including a spellbook spell already in the tab, and a full row) gives one `Set` at `min(index, count−1)`
  - insert of a new spell gives one `Set` at `min(index, count)`
  - a cross-tab move gives `Set(dest)` then `Remove(source)`
  - a new spell into a full row gives `RefusedFull` with no ops
  - dropping on a row showing the same tab as the source is a reorder
- [ ] `PlanRemove(source)` gives a single `Remove`

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SpellDeckModel' run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.**

```csharp
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckModelTests
{
    private static Func<int, IReadOnlyList<uint>> Tabs(params uint[][] tabs) =>
        tab => tab < tabs.Length ? tabs[tab] : [];

    [Fact]
    public void Visible_IsTheFirstSlotsThatFit() =>
        Assert.Equal([1u, 2u], SpellDeckModel.Visible([1u, 2u, 3u], 2));

    [Fact]
    public void MinSlots_IsTheFullestShownRow_Capped()
    {
        var rows = new[] { new SpellDeckRow(1, 0), new SpellDeckRow(2, 2) };
        uint[] big = Enumerable.Range(1, 40).Select(i => (uint)i).ToArray();
        Assert.Equal(3, SpellDeckModel.MinSlots(rows, Tabs([1, 2], [], [5, 6, 7])));
        Assert.Equal(SpellDeckGeometry.MaxCapacity, SpellDeckModel.MinSlots(rows, Tabs(big)));
        Assert.Equal(1, SpellDeckModel.MinSlots(rows, Tabs()));
    }

    [Fact]
    public void TabForNewRow_LowestUnshown_ElseFirst()
    {
        Assert.Equal(1, SpellDeckModel.TabForNewRow([new(1, 0), new(2, 2)]));
        var all = Enumerable.Range(0, 8).Select(t => new SpellDeckRow(t + 1, t)).ToArray();
        Assert.Equal(0, SpellDeckModel.TabForNewRow(all));
    }

    [Fact]
    public void Drop_ReorderInSameTab_OneSet_EvenWhenFull()
    {
        var plan = SpellDeckModel.PlanDrop(
            new SpellDeckDragSource(Tab: 0, SpellId: 1u), destTab: 0, index: 5,
            favorites: Tabs([1, 2, 3]), capacity: 3);
        Assert.Equal(SpellDeckDropKind.Reorder, plan.Kind);
        Assert.Equal([SpellDeckFavoriteOp.Set(0, 2, 1u)], plan.Ops);
    }

    [Fact]
    public void Drop_SpellbookSpellAlreadyInTab_IsAReorder()
    {
        var plan = SpellDeckModel.PlanDrop(
            SpellDeckDragSource.FromSpellbook(3u), destTab: 0, index: 0,
            favorites: Tabs([1, 2, 3]), capacity: 3);
        Assert.Equal(SpellDeckDropKind.Reorder, plan.Kind);
        Assert.Equal([SpellDeckFavoriteOp.Set(0, 0, 3u)], plan.Ops);
    }

    [Fact]
    public void Drop_NewSpell_Inserts_ClampedToTheEnd()
    {
        var plan = SpellDeckModel.PlanDrop(
            SpellDeckDragSource.FromSpellbook(9u), destTab: 1, index: 7,
            favorites: Tabs([], [4, 5]), capacity: 12);
        Assert.Equal(SpellDeckDropKind.Insert, plan.Kind);
        Assert.Equal([SpellDeckFavoriteOp.Set(1, 2, 9u)], plan.Ops);
    }

    [Fact]
    public void Drop_AcrossTabs_AddsThenRemoves()
    {
        var plan = SpellDeckModel.PlanDrop(
            new SpellDeckDragSource(Tab: 0, SpellId: 1u), destTab: 2, index: 0,
            favorites: Tabs([1, 2], [], [7]), capacity: 12);
        Assert.Equal(SpellDeckDropKind.Move, plan.Kind);
        Assert.Equal([SpellDeckFavoriteOp.Set(2, 0, 1u), SpellDeckFavoriteOp.Remove(0, 1u)], plan.Ops);
    }

    [Fact]
    public void Drop_NewSpellIntoFullRow_IsRefused_WithNoOps()
    {
        var plan = SpellDeckModel.PlanDrop(
            SpellDeckDragSource.FromSpellbook(9u), destTab: 0, index: 0,
            favorites: Tabs([1, 2, 3]), capacity: 3);
        Assert.Equal(SpellDeckDropKind.RefusedFull, plan.Kind);
        Assert.Empty(plan.Ops);
    }

    [Fact]
    public void Remove_IsOneRemove() =>
        Assert.Equal([SpellDeckFavoriteOp.Remove(4, 8u)],
            SpellDeckModel.PlanRemove(new SpellDeckDragSource(4, 8u)).Ops);
}
```

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: Implement.**

```csharp
namespace AcDream.App.UI.Hud;

/// <summary>One Spell Deck row: a stable id (survives other rows being added
/// or removed) and the favourite tab it shows (0-7).</summary>
public readonly record struct SpellDeckRow(int Id, int Tab);

/// <summary>What is being dragged: a slot's spell (Tab 0-7), or a spellbook spell (Tab -1).</summary>
public readonly record struct SpellDeckDragSource(int Tab, uint SpellId)
{
    public static SpellDeckDragSource FromSpellbook(uint spellId) => new(-1, spellId);
    public bool IsFromSpellbook => Tab < 0;
}

/// <summary>A favourite change sent to the server (and applied locally by the runtime).</summary>
public readonly record struct SpellDeckFavoriteOp(bool IsRemove, int Tab, int Position, uint SpellId)
{
    public static SpellDeckFavoriteOp Set(int tab, int position, uint spellId) => new(false, tab, position, spellId);
    public static SpellDeckFavoriteOp Remove(int tab, uint spellId) => new(true, tab, -1, spellId);
}

public enum SpellDeckDropKind { Reorder, Insert, Move, Remove, RefusedFull }

public sealed record SpellDeckDropPlan(SpellDeckDropKind Kind, IReadOnlyList<SpellDeckFavoriteOp> Ops)
{
    public bool Accepted => Kind != SpellDeckDropKind.RefusedFull;
}

/// <summary>Pure Spell Deck rules. Nothing here changes state: a drop is a plan
/// the caller sends, so a refused or cancelled drag can never lose a favourite.</summary>
public static class SpellDeckModel
{
    public const int Tabs = 8;

    public static IReadOnlyList<uint> Visible(IReadOnlyList<uint> tab, int capacity) =>
        tab.Count <= capacity ? tab : tab.Take(Math.Max(0, capacity)).ToArray();

    public static int MinSlots(IReadOnlyList<SpellDeckRow> rows, Func<int, IReadOnlyList<uint>> favorites)
    {
        int most = 1;
        foreach (SpellDeckRow row in rows) most = Math.Max(most, favorites(row.Tab).Count);
        return Math.Min(most, SpellDeckGeometry.MaxCapacity);
    }

    public static int TabForNewRow(IReadOnlyList<SpellDeckRow> rows)
    {
        for (int tab = 0; tab < Tabs; tab++)
            if (!rows.Any(r => r.Tab == tab)) return tab;
        return 0;
    }

    /// <summary>A drop of <paramref name="source"/> on slot <paramref name="index"/> of a row showing <paramref name="destTab"/>.
    /// The capacity is checked against the tab's count after the drop.</summary>
    public static SpellDeckDropPlan PlanDrop(
        SpellDeckDragSource source, int destTab, int index,
        Func<int, IReadOnlyList<uint>> favorites, int capacity)
    {
        IReadOnlyList<uint> dest = favorites(destTab);
        if (dest.Contains(source.SpellId))
            return new(SpellDeckDropKind.Reorder,
                [SpellDeckFavoriteOp.Set(destTab, Math.Clamp(index, 0, dest.Count - 1), source.SpellId)]);
        if (dest.Count + 1 > capacity)
            return new(SpellDeckDropKind.RefusedFull, []);
        var set = SpellDeckFavoriteOp.Set(destTab, Math.Clamp(index, 0, dest.Count), source.SpellId);
        return source.IsFromSpellbook || source.Tab == destTab
            ? new(SpellDeckDropKind.Insert, [set])
            : new(SpellDeckDropKind.Move, [set, SpellDeckFavoriteOp.Remove(source.Tab, source.SpellId)]);
    }

    public static SpellDeckDropPlan PlanRemove(SpellDeckDragSource source) =>
        new(SpellDeckDropKind.Remove, [SpellDeckFavoriteOp.Remove(source.Tab, source.SpellId)]);
}
```

- [ ] **Step 4: Run the Verify command and expect it to pass.**
- [ ] **Step 5: Commit** `Spell Deck: model rules (visible slots, drop plans)`.

---

### Task 6: SpellDeckOutline (outline value and transitions)

**Goal:** The outline value and every transition in the spec's outline table, as pure functions.

**Files:**
- Create: `src/AcDream.App/UI/Hud/SpellDeckOutline.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/SpellDeckOutlineTests.cs`

**Acceptance Criteria:**
- [ ] `Repair` gives row 0's first filled slot, else `Item` when there is an item spell, else `None`
- [ ] `Validate`:
  - keeps a valid `Slot` and follows its spell after a reorder (same spell, same row)
  - repairs when the spell has left the visible list or the row is gone
  - moves to the row's first filled slot when the row's tab changed (or repairs if the row is empty)
  - repairs `Item` when the item is gone
  - repairs `None` whenever something is available
- [ ] `Next`/`Prev` wrap over the filled slots of the outlined row; in row 0 the item (if present) sits before slot 0
  - from `Item`, they go to row 0's first / last filled slot, or stay `Item` if row 0 is empty
  - from `None`, they act as First / Last
- [ ] `First`/`Last` give the first / last filled slot of the outlined row (row 0 for `Item` / `None`); with an empty row they fall back to `Item`, then `None`
- [ ] Two rows showing the same tab are told apart by row id

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SpellDeckOutline' run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.**

```csharp
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckOutlineTests
{
    private static SpellDeckOutlineContext Ctx(bool item, params (int Id, int Tab, uint[] Spells)[] rows) =>
        new(rows.Select(r => new SpellDeckRow(r.Id, r.Tab)).ToArray(),
            tab => rows.FirstOrDefault(r => r.Tab == tab).Spells ?? [],
            item);

    [Fact]
    public void Repair_PrefersRow0_ThenItem_ThenNone()
    {
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 5u), SpellDeckOutline.Repair(Ctx(true, (1, 0, [5, 6]))));
        Assert.Equal(SpellDeckOutline.Item, SpellDeckOutline.Repair(Ctx(true, (1, 0, []), (2, 3, [9]))));
        Assert.Equal(SpellDeckOutline.None, SpellDeckOutline.Repair(Ctx(false, (1, 0, []))));
    }

    [Fact]
    public void Validate_FollowsTheSpell_AndRepairsWhenItLeaves()
    {
        var o = SpellDeckOutline.ForSlot(1, 0, 6u);
        Assert.Equal(o, SpellDeckOutline.Validate(o, Ctx(false, (1, 0, [6, 5]))));      // reordered
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 5u),
            SpellDeckOutline.Validate(o, Ctx(false, (1, 0, [5]))));                     // removed
        Assert.Equal(SpellDeckOutline.None, SpellDeckOutline.Validate(o, Ctx(false)));   // row gone
    }

    [Fact]
    public void Validate_RowTabChanged_GoesToThatRowsFirstSpell()
    {
        var o = SpellDeckOutline.ForSlot(2, 1, 6u);
        Assert.Equal(SpellDeckOutline.ForSlot(2, 4, 9u),
            SpellDeckOutline.Validate(o, Ctx(false, (1, 0, [5]), (2, 4, [9, 10]))));
    }

    [Fact]
    public void Validate_ItemGone_Repairs_NoneFilledWhenAvailable()
    {
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 5u),
            SpellDeckOutline.Validate(SpellDeckOutline.Item, Ctx(false, (1, 0, [5]))));
        Assert.Equal(SpellDeckOutline.Item,
            SpellDeckOutline.Validate(SpellDeckOutline.None, Ctx(true, (1, 0, []))));
    }

    [Fact]
    public void Next_WrapsAndVisitsTheItemBeforeRow0()
    {
        var ctx = Ctx(true, (1, 0, [5, 6]));
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 6u), SpellDeckOutline.Next(SpellDeckOutline.ForSlot(1, 0, 5u), ctx));
        Assert.Equal(SpellDeckOutline.Item, SpellDeckOutline.Next(SpellDeckOutline.ForSlot(1, 0, 6u), ctx));
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 5u), SpellDeckOutline.Next(SpellDeckOutline.Item, ctx));
        Assert.Equal(SpellDeckOutline.Item, SpellDeckOutline.Prev(SpellDeckOutline.ForSlot(1, 0, 5u), ctx));
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 6u), SpellDeckOutline.Prev(SpellDeckOutline.Item, ctx));
    }

    [Fact]
    public void Next_InOtherRows_WrapsWithoutTheItem()
    {
        var ctx = Ctx(true, (1, 0, [5]), (2, 3, [7, 8]));
        Assert.Equal(SpellDeckOutline.ForSlot(2, 3, 7u), SpellDeckOutline.Next(SpellDeckOutline.ForSlot(2, 3, 8u), ctx));
    }

    [Fact]
    public void FromNone_NextIsFirst_PrevIsLast()
    {
        var ctx = Ctx(false, (1, 0, [5, 6, 7]));
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 5u), SpellDeckOutline.Next(SpellDeckOutline.None, ctx));
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 7u), SpellDeckOutline.Prev(SpellDeckOutline.None, ctx));
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 7u), SpellDeckOutline.Last(SpellDeckOutline.Item, ctx));
    }

    [Fact]
    public void SameTabOnTwoRows_AreDistinct()
    {
        var ctx = Ctx(false, (1, 0, [5, 6]), (2, 0, [5, 6]));
        var row2 = SpellDeckOutline.ForSlot(2, 0, 5u);
        Assert.Equal(SpellDeckOutline.ForSlot(2, 0, 6u), SpellDeckOutline.Next(row2, ctx));
        Assert.Equal(row2, SpellDeckOutline.Validate(row2, ctx));
    }
}
```

The `Ctx` helper looks rows up by tab. Two rows with the same tab share that tab's spells, which is the real behaviour.

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: Implement.**

```csharp
namespace AcDream.App.UI.Hud;

public enum SpellDeckOutlineKind { None, Item, Slot }

/// <summary>The gold "current spell": nothing, the item spell, or one spell in
/// one row (by stable row id, so rows showing the same tab are told apart).</summary>
public readonly record struct SpellDeckOutline(SpellDeckOutlineKind Kind, int RowId, int Tab, uint SpellId)
{
    public static SpellDeckOutline None => default;
    public static SpellDeckOutline Item => new(SpellDeckOutlineKind.Item, 0, -1, 0u);
    public static SpellDeckOutline ForSlot(int rowId, int tab, uint spellId) => new(SpellDeckOutlineKind.Slot, rowId, tab, spellId);

    /// <summary>Row 0's first filled slot, else the item, else nothing.</summary>
    public static SpellDeckOutline Repair(in SpellDeckOutlineContext ctx) =>
        ctx.Rows.Count > 0 && FirstOf(ctx, ctx.Rows[0]) is { } first ? first
        : ctx.HasItem ? Item
        : None;

    /// <summary>The outline after any change to rows, favourites or the item (the outline table's state rows).</summary>
    public static SpellDeckOutline Validate(SpellDeckOutline o, in SpellDeckOutlineContext ctx)
    {
        switch (o.Kind)
        {
            case SpellDeckOutlineKind.Item:
                return ctx.HasItem ? o : Repair(ctx);
            case SpellDeckOutlineKind.Slot:
                int index = ctx.IndexOfRow(o.RowId);
                if (index < 0) return Repair(ctx);
                SpellDeckRow row = ctx.Rows[index];
                if (row.Tab != o.Tab) return FirstOf(ctx, row) ?? Repair(ctx);
                return ctx.Visible(row.Tab).Contains(o.SpellId) ? o : Repair(ctx);
            default:
                return Repair(ctx);
        }
    }

    public static SpellDeckOutline Next(SpellDeckOutline o, in SpellDeckOutlineContext ctx) => Step(o, ctx, +1);
    public static SpellDeckOutline Prev(SpellDeckOutline o, in SpellDeckOutlineContext ctx) => Step(o, ctx, -1);

    public static SpellDeckOutline First(SpellDeckOutline o, in SpellDeckOutlineContext ctx) => Edge(o, ctx, last: false);
    public static SpellDeckOutline Last(SpellDeckOutline o, in SpellDeckOutlineContext ctx) => Edge(o, ctx, last: true);

    private static SpellDeckOutline Step(SpellDeckOutline o, in SpellDeckOutlineContext ctx, int delta)
    {
        if (o.Kind == SpellDeckOutlineKind.None) return Edge(o, ctx, last: delta < 0);
        SpellDeckRow row = o.Kind == SpellDeckOutlineKind.Item || ctx.IndexOfRow(o.RowId) < 0
            ? (ctx.Rows.Count > 0 ? ctx.Rows[0] : default)
            : ctx.Rows[ctx.IndexOfRow(o.RowId)];
        // The cycle for this row: the item first when this is row 0 and there is one, then the filled slots.
        var cycle = new List<SpellDeckOutline>();
        bool isRow0 = ctx.Rows.Count > 0 && ctx.Rows[0].Id == row.Id;
        if (isRow0 && ctx.HasItem) cycle.Add(Item);
        foreach (uint spell in ctx.Visible(row.Tab)) cycle.Add(ForSlot(row.Id, row.Tab, spell));
        if (cycle.Count == 0) return o;
        int at = cycle.IndexOf(o);
        if (at < 0) return delta > 0 ? cycle[0] : cycle[^1];
        return cycle[(at + delta + cycle.Count) % cycle.Count];
    }

    private static SpellDeckOutline Edge(SpellDeckOutline o, in SpellDeckOutlineContext ctx, bool last)
    {
        int index = o.Kind == SpellDeckOutlineKind.Slot ? ctx.IndexOfRow(o.RowId) : 0;
        if (index >= 0 && index < ctx.Rows.Count)
        {
            SpellDeckRow row = ctx.Rows[index];
            IReadOnlyList<uint> spells = ctx.Visible(row.Tab);
            if (spells.Count > 0) return ForSlot(row.Id, row.Tab, last ? spells[^1] : spells[0]);
        }
        return ctx.HasItem ? Item : None;
    }

    private static SpellDeckOutline? FirstOf(in SpellDeckOutlineContext ctx, SpellDeckRow row)
    {
        IReadOnlyList<uint> spells = ctx.Visible(row.Tab);
        return spells.Count > 0 ? ForSlot(row.Id, row.Tab, spells[0]) : null;
    }
}

/// <summary>What the outline rules look at: the rows in order, each tab's visible spells, and whether there is an item spell.</summary>
public readonly record struct SpellDeckOutlineContext(
    IReadOnlyList<SpellDeckRow> Rows, Func<int, IReadOnlyList<uint>> Visible, bool HasItem)
{
    public int IndexOfRow(int id)
    {
        for (int i = 0; i < Rows.Count; i++) if (Rows[i].Id == id) return i;
        return -1;
    }
}
```

`Visible` must already be capped to the capacity (the controller passes `tab => SpellDeckModel.Visible(favorites(tab), capacity)`).

- [ ] **Step 4: Run the Verify command and expect it to pass.**
- [ ] **Step 5: Commit** `Spell Deck: outline rules`.

---

### Task 7: Settings (rows, attachment, click mode)

**Goal:** Save the rows and attachment per character, and the click mode for all characters.

**Files:**
- Create: `src/AcDream.App/UI/Hud/SpellDeckSettings.cs`
- Modify: `src/AcDream.UI.Abstractions/Panels/Settings/MiscSettings.cs`, `SettingsStore.cs` (`LoadMisc`, `BuildMiscObject`, new `LoadSpellDeck` / `SaveSpellDeck`)
- Test: `tests/AcDream.App.Tests/UI/Hud/SpellDeckSettingsTests.cs`, `tests/AcDream.UI.Abstractions.Tests/Panels/Settings/SettingsStoreSpellDeckTests.cs`

**Acceptance Criteria:**
- [ ] `SpellDeckSettings.Default` = rows `(1,0),(2,1),(3,2)`, attached
- [ ] `Normalize`:
  - clamps the row count to 1–10 (cuts extra rows; an empty list becomes the default)
  - wraps tabs outside 0–7 to 0
  - re-numbers duplicate or non-positive ids so they are unique and positive
  - `NextRowId` = max id + 1
- [ ] The store round-trips `spellDeck[toonKey] = { attached, rows:[{id,tab}] }` and keeps other sections; a missing entry gives null
- [ ] `MiscSettings.SpellDeckClickCasts` defaults to false and round-trips as `spellDeckClickCasts`

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SpellDeckSettings|FullyQualifiedName~SettingsStore' run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.**

```csharp
// tests/AcDream.App.Tests/UI/Hud/SpellDeckSettingsTests.cs
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckSettingsTests
{
    [Fact]
    public void Default_IsThreeRowsOnTabs123_Attached()
    {
        var d = SpellDeckSettings.Default;
        Assert.True(d.Attached);
        Assert.Equal([new SpellDeckRow(1, 0), new SpellDeckRow(2, 1), new SpellDeckRow(3, 2)], d.Rows);
    }

    [Fact]
    public void Normalize_FixesBadInput()
    {
        var rows = Enumerable.Range(0, 12).Select(i => new SpellDeckRow(i % 3, i == 0 ? 9 : 1)).ToArray();
        var n = new SpellDeckSettings(rows, Attached: false).Normalize();
        Assert.Equal(10, n.Rows.Count);
        Assert.Equal(0, n.Rows[0].Tab);
        Assert.Equal(n.Rows.Count, n.Rows.Select(r => r.Id).Distinct().Count());
        Assert.All(n.Rows, r => Assert.True(r.Id > 0));
        Assert.Equal(n.Rows.Max(r => r.Id) + 1, n.NextRowId);
        Assert.Equal(SpellDeckSettings.Default.Rows, new SpellDeckSettings([], true).Normalize().Rows);
    }
}
```

```csharp
// tests/AcDream.UI.Abstractions.Tests/Panels/Settings/SettingsStoreSpellDeckTests.cs
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.UI.Abstractions.Tests.Panels.Settings;

public sealed class SettingsStoreSpellDeckTests
{
    [Fact]
    public void SpellDeck_RoundTrips_PerCharacter_AndKeepsOtherSections()
    {
        string path = Path.Combine(Path.GetTempPath(), $"s-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            store.SaveMisc(MiscSettings.Default with { ModernHud = true, SpellDeckClickCasts = true });
            Assert.Null(store.LoadSpellDeck("toon"));
            store.SaveSpellDeck("toon", new StoredSpellDeck(false, [(4, 2), (7, 0)]));

            StoredSpellDeck? back = new SettingsStore(path).LoadSpellDeck("toon");
            Assert.NotNull(back);
            Assert.False(back!.Attached);
            Assert.Equal([(4, 2), (7, 0)], back.Rows);
            Assert.Null(store.LoadSpellDeck("other"));
            MiscSettings misc = store.LoadMisc();
            Assert.True(misc.ModernHud);
            Assert.True(misc.SpellDeckClickCasts);
        }
        finally { File.Delete(path); }
    }
}
```

The store works on plain `(int Id, int Tab)` tuples so `UI.Abstractions` doesn't depend on `App`.

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: Implement.**

`MiscSettings`: add `bool SpellDeckClickCasts = false` after `ModernHud`, with a `<param>` doc line ("The Spell Deck casts on a single click instead of outlining; double-click casts either way when off."). In `SettingsStore.LoadMisc` add `SpellDeckClickCasts: ReadBool(misc, "spellDeckClickCasts", d.SpellDeckClickCasts)`, and in `BuildMiscObject` add `["spellDeckClickCasts"] = m.SpellDeckClickCasts`.

In `SettingsStore` (next to `LoadCharacter`):

```csharp
/// <summary>A character's Spell Deck rows (id, tab) and whether it is attached to the Command Deck.</summary>
public sealed record StoredSpellDeck(bool Attached, IReadOnlyList<(int Id, int Tab)> Rows);

    public StoredSpellDeck? LoadSpellDeck(string toonKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonKey);
        if (!File.Exists(_path)) return null;
        try
        {
            var root = JsonNode.Parse(ReadSettingsText()) as JsonObject;
            if (root?["spellDeck"]?[toonKey] is not JsonObject deck) return null;
            var rows = new List<(int, int)>();
            if (deck["rows"] is JsonArray array)
                foreach (JsonNode? node in array)
                    if (node is JsonObject row
                        && row["id"]?.GetValue<int>() is int id
                        && row["tab"]?.GetValue<int>() is int tab)
                        rows.Add((id, tab));
            return new StoredSpellDeck(deck["attached"]?.GetValue<bool>() ?? true, rows);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"settings: failed to load spell deck from {_path}: {ex.Message}");
            return null;
        }
    }

    public void SaveSpellDeck(string toonKey, StoredSpellDeck deck)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonKey);
        ArgumentNullException.ThrowIfNull(deck);
        JsonObject root = LoadMutableRoot();
        var rows = new JsonArray();
        foreach ((int id, int tab) in deck.Rows) rows.Add(new JsonObject { ["id"] = id, ["tab"] = tab });
        GetOrCreateObject(root, "spellDeck")[toonKey] = new JsonObject { ["attached"] = deck.Attached, ["rows"] = rows };
        root["version"] = CurrentSchemaVersion;
        WriteMutableRoot(root);
    }
```

`SpellDeckSettings.cs`:

```csharp
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI.Hud;

/// <summary>A character's Spell Deck rows and attachment, validated.</summary>
public sealed record SpellDeckSettings(IReadOnlyList<SpellDeckRow> Rows, bool Attached)
{
    public static SpellDeckSettings Default { get; } =
        new([new(1, 0), new(2, 1), new(3, 2)], Attached: true);

    public int NextRowId => Rows.Count == 0 ? 1 : Rows.Max(r => r.Id) + 1;

    public SpellDeckSettings Normalize()
    {
        if (Rows.Count == 0) return Default with { Attached = Attached };
        var seen = new HashSet<int>();
        int next = Math.Max(1, Rows.Max(r => r.Id) + 1);
        var rows = new List<SpellDeckRow>();
        foreach (SpellDeckRow row in Rows.Take(SpellDeckGeometry.MaxRows))
        {
            int id = row.Id > 0 && seen.Add(row.Id) ? row.Id : next++;
            seen.Add(id);
            rows.Add(new(id, row.Tab is >= 0 and < SpellDeckModel.Tabs ? row.Tab : 0));
        }
        return new(rows, Attached);
    }

    public static SpellDeckSettings Load(SettingsStore? store, string toonKey) =>
        store?.LoadSpellDeck(toonKey) is { } s
            ? new SpellDeckSettings(s.Rows.Select(r => new SpellDeckRow(r.Id, r.Tab)).ToArray(), s.Attached).Normalize()
            : Default;

    public void Save(SettingsStore? store, string toonKey) =>
        store?.SaveSpellDeck(toonKey, new StoredSpellDeck(Attached, Rows.Select(r => (r.Id, r.Tab)).ToArray()));
}
```

- [ ] **Step 4: Run the Verify command and expect it to pass.**
- [ ] **Step 5: Commit** `Spell Deck: settings (rows, attachment, click mode)`.

---

### Task 8: Command Deck: CAST button, outside minimum stretch, move limit

**Goal:**
- The CASTING row shows the outlined spell, and its button becomes CAST.
- The deck accepts a minimum stretch from outside and enforces it.
- The deck accepts a top limit from outside, so it can't be moved where the attached Spell Deck would leave the screen.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/DeckGeometry.cs` (rename `Recast` → `Cast`, `DeckZone.Recast` → `DeckZone.Cast`)
- Modify: `src/AcDream.App/UI/Hud/CommandDeckModel.cs` (`CommandDeckInput.LastSpellName` → `OutlinedSpellName`; `RecastEnabled` → `CastEnabled`; text order)
- Modify: `src/AcDream.App/UI/Hud/ICommandDeckActions.cs` (`Recast()` → `CastOutlined()`)
- Modify: `src/AcDream.App/UI/Hud/UiCommandDeck.cs`, `UiCommandDeck.Draw.cs` (CAST; `MinStretch`; `TopLimit`)
- Modify: `src/AcDream.App/UI/Hud/EmbeddedSvgSpriteCache.cs` (`HudIcons.Cast`); create `assets/hud/cast.svg`
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (`ReadCommandDeckInput` + `CommandDeckActions` compile fix only; real wiring in Task 13)
- Test: update `CommandDeckModelTests.cs`, `UiCommandDeckTests.cs`, `UiCommandDeckResizeTests.cs`; add cases below

**Acceptance Criteria:**
- [ ] CASTING text order: pending known spell (lit) → `Casting...` (lit) → outlined spell (normal ink, not dimmed) → `No spell selected` (dimmed)
- [ ] `CastEnabled` = not `HasPendingCast` and `OutlinedSpellName` is not null; pressing the CAST stud calls `CastOutlined()` only when enabled
- [ ] Tooltip: title `Cast`, value = outlined spell name, body `Cast the outlined spell on your target.`, hint `Click to cast` / `Nothing selected`
- [ ] `UiCommandDeck.MinStretch` (a `Func<float>?`) limits right-edge and corner resizes and `SyncGeometry` restores
- [ ] `EnforceMinStretch()` widens the deck at once (keeping `Left` unless the screen forces it left) and returns true when it changed
- [ ] `UiCommandDeck.TopLimit` (a `Func<float>?`) is honoured by window drags through `ConstrainWindowDrag` (`top >= TopLimit()`)
- [ ] All existing Command Deck tests pass after the renames

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~CommandDeck|FullyQualifiedName~DeckGeometry|FullyQualifiedName~ModernHud' run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck` → all pass

**Steps:**

- [ ] **Step 1: Update and add the failing tests.**
  - In `CommandDeckModelTests`, change the casting-text cases to:

```csharp
    [Theory]
    [InlineData(true, "Flame Bolt", null, "Flame Bolt", true)]
    [InlineData(true, null, "Frost Bolt", "Casting...", true)]
    [InlineData(false, null, "Frost Bolt", "Frost Bolt", false)]
    [InlineData(false, null, null, "No spell selected", false)]
    public void CastingText_PendingThenOutlined(bool pending, string? pendingName, string? outlined, string text, bool lit)
    {
        var view = new CommandDeckModel().Update(default(CommandDeckInput) with
        {
            Mode = CombatMode.Magic, HasPendingCast = pending,
            PendingSpellName = pendingName, OutlinedSpellName = outlined,
        }, 0);
        Assert.Equal(text, view.CastingText);
        Assert.Equal(lit, view.CastingLit);
        Assert.Equal(!pending && outlined is not null, view.CastEnabled);
        Assert.Equal(outlined is null && !pending, view.CastingDim);
    }
```

  - In `UiCommandDeckTests`, change the recorder to `public void CastOutlined() => Calls.Add("cast");` and change the recast tests to "pressing CAST with an outlined spell records `cast`; pressing it with none records nothing".
  - Add resize tests in `UiCommandDeckResizeTests` (copy its fixture for driving `ConstrainResize`):

```csharp
    [Fact]
    public void MinStretch_StopsANarrowingDrag()
    {
        var deck = MakeDeck();                           // existing helper in this file
        deck.ApplyGeometry(DeckGeometry.Create(1f, 1.8f));
        deck.MinStretch = () => 1.5f;
        float w = DeckGeometry.BaseWidth * 1.1f, h = deck.Height;
        deck.ConstrainResize(ResizeEdges.Right, ref w, ref h);
        Assert.Equal(1.5f, deck.Geometry.Stretch, 3);
    }

    [Fact]
    public void EnforceMinStretch_WidensAtOnce()
    {
        var deck = MakeDeck();
        deck.MinStretch = () => 1.25f;
        Assert.True(deck.EnforceMinStretch());
        Assert.Equal(1.25f, deck.Geometry.Stretch, 3);
        Assert.False(deck.EnforceMinStretch());
    }

    [Fact]
    public void TopLimit_StopsAWindowDrag()
    {
        var deck = MakeDeck();
        deck.TopLimit = () => 120f;
        float left = 10f, top = 40f;
        deck.ConstrainWindowDrag(ref left, ref top, 0, 0);
        Assert.Equal(120f, top);
    }
```

  `ConstrainResize`, `ConstrainWindowDrag` and `ApplyGeometry` are `internal`. The test project already sees internals (the existing resize tests call them).

- [ ] **Step 2: Run, expect compile failures and red tests.**

- [ ] **Step 3: Implement.**
  - `DeckGeometry`: add an overload `public static DeckGeometry Create(float scale, float stretch, float minStretch)` that clamps the stretch to `[max(MinStretch, minStretch), MaxStretch]`, and make the two-argument `Create` call it with `MinStretch`. Rename `Recast` → `Cast` (rect) and `DeckZone.Recast` → `DeckZone.Cast` everywhere (`grep -rn "Recast" src tests`).
  - `CommandDeckInput`: rename `LastSpellName` → `OutlinedSpellName`. `CommandDeckView`: rename `RecastEnabled` → `CastEnabled` and add `bool CastingDim`. Model:

```csharp
        (string castingText, bool castingLit, bool castingDim) = input switch
        {
            { HasPendingCast: true, PendingSpellName: string name } => (name, true, false),
            { HasPendingCast: true } => ("Casting...", true, false),
            { OutlinedSpellName: string outlined } => (outlined, false, false),
            _ => ("No spell selected", false, true),
        };
        ...
            CastEnabled: !input.HasPendingCast && input.OutlinedSpellName is not null,
            CastingDim: castingDim,
```

  - Draw: in `DrawCastingRow`, the ink is `v.CastingLit ? CastLit : v.CastingDim ? StudOffInk : TextInk`, and the stud is `DrawStud(ctx, _geometry.Cast, on: false, icon: HudIcons.Cast, letter: null, dim: !v.CastEnabled)`. Rename `FitCastingText`'s "RECAST" comment to CAST.
  - `ICommandDeckActions.Recast()` → `CastOutlined()`; in `Press`, `case DeckZone.Cast: if (_view.CastEnabled) _actions.CastOutlined(); return true;`. Tooltip:

```csharp
        DeckZone.Cast => new("Cast", view.CastEnabled ? view.CastingText : null,
            "Cast the outlined spell on your target.",
            view.CastEnabled ? "Click to cast" : "Nothing selected"),
```

  - `HudIcons.Cast = "cast"`, added to `All`. `assets/hud/cast.svg` (an arrow, the same style as recast):

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M4 12 H15" fill="none" stroke="#fff" stroke-width="2.5"/><path d="M13 6 L20 12 L13 18 Z" fill="#fff"/></svg>
```

    Leave `recast.svg` and `HudIcons.Recast` in place; nothing else uses them, so remove both if `grep` confirms that.
  - `UiCommandDeck`:

```csharp
    /// <summary>The narrowest stretch the deck may have (the attached Spell Deck's fullest row); null = no limit.</summary>
    internal Func<float>? MinStretch { get; set; }

    /// <summary>The highest the deck's top may go (room for the attached Spell Deck above it); null = no limit.</summary>
    internal Func<float>? TopLimit { get; set; }

    private float MinStretchNow => MinStretch?.Invoke() ?? DeckGeometry.MinStretch;

    internal override void ConstrainWindowDrag(ref float left, ref float top, int pointerX, int pointerY)
    {
        if (TopLimit?.Invoke() is float limit && top < limit) top = limit;
    }

    /// <summary>Widens the deck to <see cref="MinStretch"/> when it is narrower, keeping it on screen. True when it changed.</summary>
    internal bool EnforceMinStretch()
    {
        float min = MinStretchNow;
        if (_geometry.Stretch >= min - 1e-4f) return false;
        ApplyGeometry(DeckGeometry.Create(_geometry.Scale, min, min));
        if (Parent is { } parent && Left + Width > parent.Width)
            Left = Math.Max(0f, parent.Width - Width);
        return true;
    }
```

    In `ConstrainResize`, build with the minimum: `corner ? from.WithCornerSize(...)` then `_geometry = DeckGeometry.Create(_geometry.Scale, _geometry.Stretch, MinStretchNow);` before the `FitWithin` step. In `SyncGeometry`, after `FromSize`, apply the same `Create(..., MinStretchNow)`.
    Check that `UiRoot`'s drag calls `ConstrainWindowDrag` on the **frame** that `RetailWindowFrame.Mount` creates, not on the deck (see `UiRoot.cs:540`, `_windowDragTarget.ConstrainWindowDrag`). If the target is the outer frame, route it: give `RetailWindowFrame.Options` a `Func<float>? TopLimit` that the frame's `ConstrainWindowDrag` applies, set it from `UiCommandDeck.MountOptions`, and have the deck's `TopLimit` setter forward to it. Find out by reading `RetailWindowFrame.Mount` (`grep -rn "static .* Mount(" src/AcDream.App/UI`), and pick whichever path makes the `TopLimit_StopsAWindowDrag` test meaningful (test the frame if that's where it lives).
  - `RetailUiRuntime`: `ReadCommandDeckInput` passes `OutlinedSpellName: null` for now, and `CommandDeckActions.CastOutlined()` is an empty body with `// wired to the Spell Deck in Task 13`. Remove the runtime's `Recast` implementation.

- [ ] **Step 4: Run the Verify command and expect it to pass.**
- [ ] **Step 5: Commit** `Command Deck: CAST the outlined spell; outside min width and top limit`.

---

### Task 9: SpellDeckController (state, hotkeys, outline upkeep, drops)

**Goal:** One class that owns the live Spell Deck state, runs the model and outline rules against the spellbook, and turns hotkeys, clicks and drops into `ISpellDeckActions` calls.

**Files:**
- Create: `src/AcDream.App/UI/Hud/ISpellDeckActions.cs`, `src/AcDream.App/UI/Hud/SpellDeckController.cs`, `src/AcDream.App/UI/Hud/SpellDeckKeyLabel.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/SpellDeckControllerTests.cs`, `tests/AcDream.App.Tests/UI/Hud/SpellDeckKeyLabelTests.cs`

**Acceptance Criteria:**
- [ ] `Handle(UseSpellSlot_n)` casts row 0 slot n−1, and `Handle(UseSpellRow{r}Slot{n})` casts row r−1. Both outline the slot and return true. A missing row or empty slot returns true and does nothing
- [ ] `CombatPrev/NextSpellTab` cycle row 0's tab 0–7 with wrapping; `First/LastSpellTab` set it to 0 / 7; the settings are saved
- [ ] `CombatPrev/Next/First/LastSpell` move the outline by the rules; `CombatCastCurrentSpell` casts the outline (spell → `CastSpell`, item → `CastItemSpell`); `CastItemSpell` casts the item and outlines it
- [ ] Any other action returns false
- [ ] `Click(row, slot)` outlines, and also casts when `ClickCasts`; `DoubleClick` casts only when not `ClickCasts`; `ClickItem` / `DoubleClickItem` follow the same rule
- [ ] `Drop(source, row, slot)`: sends the plan's ops through `SetFavorite` / `RemoveFavorite` in order; a refused plan sends nothing and shows `That spell bar row is full.`; a cross-row move outlines the moved spell in the destination row
- [ ] `DropOutside(source)` removes the spell
- [ ] `AddRowBelow(rowId)` inserts a row using `TabForNewRow` with a fresh id (not past 10 rows, and only when `CanAddRow()` allows); `RemoveRow` is a no-op on the last row; `SetRowTab` changes the tab; each change saves
- [ ] `AddFavoriteFromSpellbook(spell)` appends to the outlined row's tab (row 0 if nothing is outlined); full → the message; already present → it only gets outlined
- [ ] `Tick()` re-validates the outline every call (cheap, pure), so external favourite changes, item changes and capacity changes repair it
- [ ] `OutlinedSpellName`, `CanCastOutlined`, `CastOutlined()` serve the Command Deck
- [ ] `SpellDeckKeyLabel.For(bindings, action)` gives `"4"`, `"⇧4"`, `"^4"`, `"⌥4"`, `"-"`, `"="`, `"F5"`, or `""`

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SpellDeckController|FullyQualifiedName~SpellDeckKeyLabel' run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the interfaces and failing tests.**

`ISpellDeckActions.cs`:

```csharp
namespace AcDream.App.UI.Hud;

/// <summary>What the Spell Deck asks the game to do: the same calls the retail spell bar makes.</summary>
public interface ISpellDeckActions
{
    void CastSpell(uint spellId);
    void CastItemSpell(uint itemId);
    void SetFavorite(int tab, int position, uint spellId);
    void RemoveFavorite(int tab, uint spellId);
    void ExamineSpell(uint spellId);
    void ShowMessage(string text);
}
```

Tests (use a recorder and a plain in-memory favourites array that `SetFavorite` / `RemoveFavorite` update the way `Spellbook.SetFavorite` does: remove, then insert at `min(position, count)`):

```csharp
using AcDream.App.Spells;
using AcDream.App.UI.Hud;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckControllerTests
{
    private sealed class Game : ISpellDeckActions
    {
        public readonly List<uint>[] Tabs = Enumerable.Range(0, 8).Select(_ => new List<uint>()).ToArray();
        public readonly List<string> Calls = [];
        public ItemSpell? Item;
        public void CastSpell(uint id) => Calls.Add($"cast {id}");
        public void CastItemSpell(uint itemId) => Calls.Add($"item {itemId}");
        public void SetFavorite(int tab, int pos, uint id)
        {
            Calls.Add($"set {tab} {pos} {id}");
            Tabs[tab].Remove(id); Tabs[tab].Insert(Math.Min(pos, Tabs[tab].Count), id);
        }
        public void RemoveFavorite(int tab, uint id) { Calls.Add($"remove {tab} {id}"); Tabs[tab].Remove(id); }
        public void ExamineSpell(uint id) => Calls.Add($"examine {id}");
        public void ShowMessage(string text) => Calls.Add("msg " + text);
    }

    private static (SpellDeckController C, Game G, List<SpellDeckSettings> Saved) Make(bool clickCasts = false)
    {
        var g = new Game();
        g.Tabs[0].AddRange([10u, 11u, 12u]);
        g.Tabs[1].AddRange([20u]);
        var saved = new List<SpellDeckSettings>();
        var c = new SpellDeckController(
            g, favorites: tab => g.Tabs[tab], itemSpell: () => g.Item,
            capacity: () => 12, settings: SpellDeckSettings.Default, save: saved.Add,
            clickCasts: () => clickCasts, canAddRow: () => true);
        c.Tick();
        return (c, g, saved);
    }

    [Fact]
    public void Hotkeys_CastRowSlots_AndOutlineThem()
    {
        var (c, g, _) = Make();
        Assert.True(c.Handle(InputAction.UseSpellSlot_2));
        Assert.True(c.Handle(InputAction.UseSpellRow2Slot1));
        Assert.Equal(["cast 11", "cast 20"], g.Calls);
        Assert.Equal(SpellDeckOutline.ForSlot(2, 1, 20u), c.Outline);
        Assert.True(c.Handle(InputAction.UseSpellRow3Slot5));   // empty slot: handled, nothing
        Assert.True(c.Handle(InputAction.UseSpellRow9Slot1));   // row not shown: handled, nothing
        Assert.Equal(2, g.Calls.Count);
        Assert.False(c.Handle(InputAction.Jump));
    }

    [Fact]
    public void TabKeys_CycleRow0()
    {
        var (c, _, saved) = Make();
        c.Handle(InputAction.CombatPrevSpellTab);
        Assert.Equal(7, c.Rows[0].Tab);
        c.Handle(InputAction.CombatNextSpellTab);
        Assert.Equal(0, c.Rows[0].Tab);
        c.Handle(InputAction.CombatLastSpellTab);
        Assert.Equal(7, c.Rows[0].Tab);
        Assert.Equal(3, saved.Count);
    }

    [Fact]
    public void ClickModes_CastCounts()
    {
        var (c, g, _) = Make(clickCasts: false);
        c.Click(0, 1); c.Click(0, 1); c.DoubleClick(0, 1);   // one physical double-click
        Assert.Equal(["cast 11"], g.Calls);

        var (c2, g2, _) = Make(clickCasts: true);
        c2.Click(0, 1); c2.Click(0, 1); c2.DoubleClick(0, 1);
        Assert.Equal(["cast 11", "cast 11"], g2.Calls);
    }

    [Fact]
    public void Drop_CrossRow_SetsThenRemoves_AndOutlinesTheDestination()
    {
        var (c, g, _) = Make();
        c.Drop(new SpellDeckDragSource(0, 10u), row: 1, slot: 0);
        Assert.Equal(["set 1 0 10", "remove 0 10"], g.Calls);
        Assert.Equal(SpellDeckOutline.ForSlot(2, 1, 10u), c.Outline);
    }

    [Fact]
    public void Drop_IntoFullRow_SendsNothing_AndSays()
    {
        var (c, g, _) = Make();
        for (uint s = 30; s < 41; s++) g.Tabs[1].Add(s);    // tab 1 now holds 12
        c.Drop(SpellDeckDragSource.FromSpellbook(99u), row: 1, slot: 0);
        Assert.Equal(["msg That spell bar row is full."], g.Calls);
    }

    [Fact]
    public void DropOutside_Removes()
    {
        var (c, g, _) = Make();
        c.DropOutside(new SpellDeckDragSource(0, 12u));
        Assert.Equal(["remove 0 12"], g.Calls);
    }

    [Fact]
    public void Rows_AddRemoveSetTab()
    {
        var (c, _, saved) = Make();
        c.AddRowBelow(c.Rows[0].Id);
        Assert.Equal(4, c.Rows.Count);
        Assert.Equal(3, c.Rows[1].Tab);          // lowest tab no row shows
        foreach (var row in c.Rows.ToArray().Skip(1)) c.RemoveRow(row.Id);
        c.RemoveRow(c.Rows[0].Id);               // last row stays
        Assert.Single(c.Rows);
        c.SetRowTab(c.Rows[0].Id, 5);
        Assert.Equal(5, c.Rows[0].Tab);
        Assert.True(saved.Count >= 5);
    }

    [Fact]
    public void OutlineFollowsExternalChanges_AndItemCasts()
    {
        var (c, g, _) = Make();
        c.Click(0, 2);
        g.Tabs[0].Remove(12u);
        c.Tick();
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 10u), c.Outline);
        g.Item = new ItemSpell(0x7000u, 55u);
        Assert.True(c.Handle(InputAction.CastItemSpell));
        Assert.Equal(SpellDeckOutline.Item, c.Outline);
        Assert.True(c.CanCastOutlined);
        c.CastOutlined();
        Assert.Equal(["item 28672", "item 28672"], g.Calls);
    }

    [Fact]
    public void SpellbookAdd_GoesToTheOutlinedRow()
    {
        var (c, g, _) = Make();
        c.Handle(InputAction.UseSpellRow2Slot1);   // outline row 2 (tab 1)
        g.Calls.Clear();
        c.AddFavoriteFromSpellbook(77u);
        Assert.Equal(["set 1 1 77"], g.Calls);
    }
}
```

```csharp
// SpellDeckKeyLabelTests.cs
public sealed class SpellDeckKeyLabelTests
{
    [Fact]
    public void Labels_FromTheFirstChord()
    {
        var b = new KeyBindings();
        b.Add(new(new KeyChord(Key.Number4, ModifierMask.None), InputAction.UseSpellSlot_4, Scope: InputScope.MagicCombat));
        b.Add(new(new KeyChord(Key.Number4, ModifierMask.Shift), InputAction.UseSpellRow2Slot4, Scope: InputScope.MagicCombat));
        b.Add(new(new KeyChord(Key.Minus, ModifierMask.Ctrl), InputAction.UseSpellRow3Slot11, Scope: InputScope.MagicCombat));
        b.Add(new(new KeyChord(Key.F5, ModifierMask.Alt), InputAction.CastItemSpell, Scope: InputScope.MagicCombat));
        Assert.Equal("4", SpellDeckKeyLabel.For(b, InputAction.UseSpellSlot_4));
        Assert.Equal("⇧4", SpellDeckKeyLabel.For(b, InputAction.UseSpellRow2Slot4));
        Assert.Equal("^-", SpellDeckKeyLabel.For(b, InputAction.UseSpellRow3Slot11));
        Assert.Equal("⌥F5", SpellDeckKeyLabel.For(b, InputAction.CastItemSpell));
        Assert.Equal("", SpellDeckKeyLabel.For(b, InputAction.UseSpellRow4Slot1));
    }
}
```

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: Implement.**

`SpellDeckKeyLabel.cs`:

```csharp
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.UI.Hud;

/// <summary>The short key label drawn in a slot's corner, from the first chord bound to its action.
/// The modifier glyphs live here so a font without them is a one-line change.</summary>
public static class SpellDeckKeyLabel
{
    public const string Shift = "⇧", Ctrl = "^", Alt = "⌥";

    public static string For(KeyBindings bindings, InputAction action)
    {
        Binding? first = bindings.ForAction(action).Cast<Binding?>().FirstOrDefault();
        if (first is not { } b) return string.Empty;
        string mods = (b.Chord.Modifiers.HasFlag(ModifierMask.Ctrl) ? Ctrl : "")
            + (b.Chord.Modifiers.HasFlag(ModifierMask.Alt) ? Alt : "")
            + (b.Chord.Modifiers.HasFlag(ModifierMask.Shift) ? Shift : "");
        return mods + KeyName(b.Chord.Key);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.Number0 and <= Key.Number9 => ((int)(key - Key.Number0)).ToString(),
        Key.Minus => "-",
        Key.Equal => "=",
        _ => key.ToString(),
    };
}
```

`SpellDeckController.cs`. This holds the rules; the UI element only calls it.

```csharp
using AcDream.App.Spells;
using AcDream.UI.Abstractions.Input;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The Spell Deck's live state (rows, outline) and behaviour: hotkeys,
/// clicks, drops and row edits, run through the pure model and outline rules.
/// Favourites are read through <c>favorites</c>; every change is sent through
/// <see cref="ISpellDeckActions"/> (which also updates the local spellbook).
/// </summary>
public sealed class SpellDeckController
{
    public const string FullRowMessage = "That spell bar row is full.";

    private readonly ISpellDeckActions _actions;
    private readonly Func<int, IReadOnlyList<uint>> _favorites;
    private readonly Func<ItemSpell?> _itemSpell;
    private readonly Func<int> _capacity;
    private readonly Action<SpellDeckSettings> _save;
    private readonly Func<bool> _clickCasts;
    private readonly Func<bool> _canAddRow;
    private readonly List<SpellDeckRow> _rows;
    private bool _attached;
    private int _nextRowId;

    public SpellDeckController(
        ISpellDeckActions actions,
        Func<int, IReadOnlyList<uint>> favorites,
        Func<ItemSpell?> itemSpell,
        Func<int> capacity,
        SpellDeckSettings settings,
        Action<SpellDeckSettings> save,
        Func<bool> clickCasts,
        Func<bool> canAddRow)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _favorites = favorites ?? throw new ArgumentNullException(nameof(favorites));
        _itemSpell = itemSpell ?? throw new ArgumentNullException(nameof(itemSpell));
        _capacity = capacity ?? throw new ArgumentNullException(nameof(capacity));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _clickCasts = clickCasts ?? throw new ArgumentNullException(nameof(clickCasts));
        _canAddRow = canAddRow ?? throw new ArgumentNullException(nameof(canAddRow));
        SpellDeckSettings s = settings.Normalize();
        _rows = [.. s.Rows];
        _attached = s.Attached;
        _nextRowId = s.NextRowId;
    }

    public IReadOnlyList<SpellDeckRow> Rows => _rows;
    public SpellDeckOutline Outline { get; private set; }
    public bool Attached => _attached;
    public bool ClickCasts => _clickCasts();
    public ItemSpell? ItemSpell => _itemSpell();

    /// <summary>Replaces the rows and attachment (character change). The outline is repaired.</summary>
    public void Load(SpellDeckSettings settings)
    {
        SpellDeckSettings s = settings.Normalize();
        _rows.Clear(); _rows.AddRange(s.Rows);
        _attached = s.Attached; _nextRowId = s.NextRowId;
        Outline = SpellDeckOutline.None;
        Tick();
    }

    public IReadOnlyList<uint> Visible(int tab) => SpellDeckModel.Visible(_favorites(tab), _capacity());
    public IReadOnlyList<uint> Favorites(int tab) => _favorites(tab);

    /// <summary>The fewest slots per row the deck may show: its fullest row.</summary>
    public int MinSlots => SpellDeckModel.MinSlots(_rows, _favorites);

    private SpellDeckOutlineContext Context => new(_rows, Visible, _itemSpell() is not null);

    /// <summary>Re-checks the outline against the current favourites, rows, capacity and item.</summary>
    public void Tick() => Outline = SpellDeckOutline.Validate(Outline, Context);

    // ── Command Deck ──────────────────────────────────────────────
    public bool CanCastOutlined => Outline.Kind != SpellDeckOutlineKind.None;
    public uint? OutlinedSpellId => Outline.Kind switch
    {
        SpellDeckOutlineKind.Slot => Outline.SpellId,
        SpellDeckOutlineKind.Item => _itemSpell()?.SpellId,
        _ => null,
    };
    public void CastOutlined()
    {
        switch (Outline.Kind)
        {
            case SpellDeckOutlineKind.Slot: _actions.CastSpell(Outline.SpellId); break;
            case SpellDeckOutlineKind.Item when _itemSpell() is { } item: _actions.CastItemSpell(item.ItemId); break;
        }
    }

    // ── Hotkeys ───────────────────────────────────────────────────
    public bool Handle(InputAction action)
    {
        if (SpellBarActions.TryParse(action, out int row, out int slot))
        {
            if (SlotSpell(row, slot) is { } hit) { Outline = hit; CastOutlined(); }
            return true;
        }
        switch (action)
        {
            case InputAction.CastItemSpell: ClickItem(cast: true); return true;
            case InputAction.CombatPrevSpellTab: SetRow0Tab(t => (t + 7) % 8); return true;
            case InputAction.CombatNextSpellTab: SetRow0Tab(t => (t + 1) % 8); return true;
            case InputAction.CombatFirstSpellTab: SetRow0Tab(_ => 0); return true;
            case InputAction.CombatLastSpellTab: SetRow0Tab(_ => 7); return true;
            case InputAction.CombatPrevSpell: Outline = SpellDeckOutline.Prev(Outline, Context); return true;
            case InputAction.CombatNextSpell: Outline = SpellDeckOutline.Next(Outline, Context); return true;
            case InputAction.CombatFirstSpell: Outline = SpellDeckOutline.First(Outline, Context); return true;
            case InputAction.CombatLastSpell: Outline = SpellDeckOutline.Last(Outline, Context); return true;
            case InputAction.CombatCastCurrentSpell: CastOutlined(); return true;
            default: return false;
        }
    }

    private void SetRow0Tab(Func<int, int> next)
    {
        if (_rows.Count == 0) return;
        SetRowTab(_rows[0].Id, next(_rows[0].Tab));
    }

    private SpellDeckOutline? SlotSpell(int row, int slot)
    {
        if (row >= _rows.Count) return null;
        IReadOnlyList<uint> spells = Visible(_rows[row].Tab);
        return slot < spells.Count ? SpellDeckOutline.ForSlot(_rows[row].Id, _rows[row].Tab, spells[slot]) : null;
    }

    // ── Pointer ───────────────────────────────────────────────────
    public void Click(int row, int slot)
    {
        if (SlotSpell(row, slot) is not { } hit) return;
        Outline = hit;
        if (ClickCasts) CastOutlined();
    }

    public void DoubleClick(int row, int slot)
    {
        if (ClickCasts || SlotSpell(row, slot) is not { } hit) return;
        Outline = hit;
        CastOutlined();
    }

    public void ClickItem() => ClickItem(cast: ClickCasts);
    public void DoubleClickItem() { if (!ClickCasts) ClickItem(cast: true); }

    private void ClickItem(bool cast)
    {
        if (_itemSpell() is null) return;
        Outline = SpellDeckOutline.Item;
        if (cast) CastOutlined();
    }

    /// <summary>Whether a drop of <paramref name="source"/> on (row, slot) would be accepted, for the drag highlight.</summary>
    public bool CanDrop(SpellDeckDragSource source, int row, int slot) =>
        row < _rows.Count
        && SpellDeckModel.PlanDrop(source, _rows[row].Tab, slot, _favorites, _capacity()).Accepted;

    public void Drop(SpellDeckDragSource source, int row, int slot)
    {
        if (row >= _rows.Count) return;
        SpellDeckRow dest = _rows[row];
        SpellDeckDropPlan plan = SpellDeckModel.PlanDrop(source, dest.Tab, slot, _favorites, _capacity());
        if (!plan.Accepted) { _actions.ShowMessage(FullRowMessage); return; }
        bool wasOutlined = Outline.Kind == SpellDeckOutlineKind.Slot && Outline.SpellId == source.SpellId;
        Send(plan);
        if (wasOutlined || plan.Kind == SpellDeckDropKind.Move)
            Outline = SpellDeckOutline.ForSlot(dest.Id, dest.Tab, source.SpellId);
        Tick();
    }

    public void DropOutside(SpellDeckDragSource source)
    {
        if (source.IsFromSpellbook) return;
        Send(SpellDeckModel.PlanRemove(source));
        Tick();
    }

    public void RemoveFromRow(int row, int slot)
    {
        if (SlotSpell(row, slot) is { } hit) DropOutside(new SpellDeckDragSource(hit.Tab, hit.SpellId));
    }

    public void Examine(int row, int slot)
    {
        if (SlotSpell(row, slot) is { } hit) _actions.ExamineSpell(hit.SpellId);
    }

    private void Send(SpellDeckDropPlan plan)
    {
        foreach (SpellDeckFavoriteOp op in plan.Ops)
            if (op.IsRemove) _actions.RemoveFavorite(op.Tab, op.SpellId);
            else _actions.SetFavorite(op.Tab, op.Position, op.SpellId);
    }

    /// <summary>The spellbook's "add to favourites": the outlined row's tab (row 0 when nothing is outlined), at the end.</summary>
    public void AddFavoriteFromSpellbook(uint spellId)
    {
        int index = Outline.Kind == SpellDeckOutlineKind.Slot ? Math.Max(0, Context.IndexOfRow(Outline.RowId)) : 0;
        if (index >= _rows.Count) return;
        SpellDeckRow row = _rows[index];
        IReadOnlyList<uint> tab = _favorites(row.Tab);
        if (tab.Contains(spellId)) { Outline = SpellDeckOutline.ForSlot(row.Id, row.Tab, spellId); Tick(); return; }
        Drop(SpellDeckDragSource.FromSpellbook(spellId), index, tab.Count);
    }

    // ── Rows ──────────────────────────────────────────────────────
    public bool CanAddRow() => _rows.Count < SpellDeckGeometry.MaxRows && _canAddRow();
    public bool CanRemoveRow => _rows.Count > 1;

    public void AddRowBelow(int rowId)
    {
        if (!CanAddRow()) return;
        int at = Math.Max(0, Context.IndexOfRow(rowId)) + 1;
        _rows.Insert(Math.Min(at, _rows.Count), new SpellDeckRow(_nextRowId++, SpellDeckModel.TabForNewRow(_rows)));
        Changed();
    }

    public void RemoveRow(int rowId)
    {
        int at = Context.IndexOfRow(rowId);
        if (at < 0 || !CanRemoveRow) return;
        _rows.RemoveAt(at);
        Changed();
    }

    public void SetRowTab(int rowId, int tab)
    {
        int at = Context.IndexOfRow(rowId);
        if (at < 0 || tab is < 0 or >= SpellDeckModel.Tabs || _rows[at].Tab == tab) return;
        _rows[at] = _rows[at] with { Tab = tab };
        Changed();
    }

    public void SetAttached(bool attached)
    {
        if (_attached == attached) return;
        _attached = attached;
        Changed();
    }

    private void Changed()
    {
        Tick();
        _save(new SpellDeckSettings([.. _rows], _attached));
    }
}
```

Check `TabKeys_CycleRow0`: it expects 3 saves for 3 tab changes. `SetRowTab` saves on each real change. The `NextSpellTab` from 7 → 0 is a real change.

- [ ] **Step 4: Run the Verify command and expect it to pass.** If a test's expected outline id disagrees with the default row ids (1, 2, 3), the test is wrong, not the code: rows are `(1,0),(2,1),(3,2)`.
- [ ] **Step 5: Commit** `Spell Deck: controller (hotkeys, clicks, drops, rows)`.

---

### Task 10: UiSpellDeck element (draw, hit, click modes, tooltip)

**Goal:** The element draws a controller's state in Command Deck style and turns clicks, double-clicks, hovers and tab-chip clicks into controller calls.

**Files:**
- Create: `src/AcDream.App/UI/Hud/UiSpellDeck.cs`, `src/AcDream.App/UI/Hud/UiSpellDeck.Draw.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/UiSpellDeckTests.cs`

**Acceptance Criteria:**
- [ ] Constructed with `(SpellDeckController controller, Func<uint, uint> spellIcon, Func<uint, SpellMetadata?> spellInfo, Func<InputAction, string> keyLabel, Func<double> clock, DeckFonts? fonts)`
- [ ] `Geometry` = `SpellDeckGeometry.Create(scale, stretch, controller.Rows.Count)`; `ApplyGeometry(g)` sets `Width` / `Height`
- [ ] `Refresh()` calls `controller.Tick()` and refits the height when the row count changed (keeping the bottom edge while attached; the dock handles that, here just Height)
- [ ] Click on a slot → `controller.Click(row, slot)`; DoubleClick → `controller.DoubleClick`; Click on the item → `ClickItem`; DoubleClick on the item → `DoubleClickItem`; Click on a tab chip → `SetRowTab(row, (tab+1)%8)`
- [ ] Each physical double-click gives the counts from Task 9 through the element (test with the real event sequence Click, Click, DoubleClick)
- [ ] Slot tooltips give spell name, school and key label; the item tooltip gives item and spell; the chip tooltip reads `Tab {n}` with hint `Click for the next tab, right-click for more`
- [ ] Draw (with a recording context, as in `UiCommandDeckDrawTests`) draws one sprite per visible spell, the gold outline rect around the outlined slot, and the blue glow on the casting slot

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiSpellDeck' run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck` → all pass

**Steps:**

- [ ] **Step 1: Read** `tests/AcDream.App.Tests/UI/Hud/UiCommandDeckDrawTests.cs` and `UiCommandDeckTests.cs` for how they build the element, send `UiEvent`s (`new UiEvent(0, deck, UiEventType.Click, Data1: x, Data2: y)`) and record draw calls. Write `UiSpellDeckTests` the same way:
  - **Click modes through events:** with `clickCasts=false`, send Click, Click, DoubleClick on slot (0,1) and expect one `cast`. With `true`, expect two.
  - **Tab chip:** a click on row 1's chip makes row 1 show tab 2 (default tab 1 + 1).
  - **Tooltip:** after a hover move onto slot (0,0) and advancing the clock past `UiCommandDeck.TooltipDwell`, `VisibleTooltip` has title = the spell name.
  - **Draw:** three visible spells give three `DrawSprite` calls with the icon texture ids from `spellIcon`.

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: Implement.** Follow `UiCommandDeck`'s patterns closely: hover dwell, `_tooltipSuppressed`, `HudTooltip` overlay, and the `P()` / `Line()` scale helpers.
  - Share the palette and frame: move `Rgb`, the palette fields and `DrawFrame`'s body from `UiCommandDeck.Draw.cs` into a new `internal static class DeckChrome` in `src/AcDream.App/UI/Hud/DeckChrome.cs`, with `DrawFrame(UiRenderContext ctx, DeckRect frame, float scale)` and `DrawStud(...)`. Have `UiCommandDeck` call it, which is a pure move. Run the Command Deck draw tests afterwards to prove nothing changed.
  - **Drawing** (`UiSpellDeck.Draw.cs`), in this order:
    1. `DeckChrome.DrawFrame(ctx, g.Frame, scale)`.
    2. The divider in `Bronze`.
    3. The item socket: a circle (`FillRoundedRect` with radius = half the size) in `Track`, rimmed `Handle` gold when there is an item spell. Draw the item's spell icon inside, clipped round if `ctx` supports it (otherwise square inside the circle). It is dim when there is no item spell.
    4. Per row: the chip (`DrawStud` with the letter `(tab+1).ToString()`), then slots `0..SlotsPerRow-1`. A filled slot is the spell icon sprite (`ctx.DrawSprite(spellIcon(id), x, y, w, h, 0, 0, 1, 1, Vector4.One)`); an empty one is a `Track` rounded rect.
    5. The key label (small face, `LabelInk`, top-left, 2 px in) for `SpellBarActions.ForSlot(row, slot)` when `slot < 12` and the label isn't empty.
    6. The outline: a 2 px `Handle` stroke 1 px outside the outlined slot. Blue glow: a `CastLit` stroke at alpha 0.8, 3 px outside the slot whose spell id equals the pending spell. Take the pending spell through an extra constructor argument `Func<uint?> pendingSpell` (`RuntimeSpellCastState.HasPendingCast ? PendingSpellId : null`).
    7. The resize grip, as `UiCommandDeck.DrawResizeGrip`, only when detached.
  - **Input** (`OnEvent`):
    - `MouseDown` suppresses the tooltip.
    - `Click`: hit at (`Data1`, `Data2`). Slot → `Click`; Item → `ClickItem`; TabChip → `SetRowTab(rowId, (tab + 1) % 8)`.
    - `DoubleClick`: slot → `DoubleClick`; Item → `DoubleClickItem`.
    - `MouseMove`: hover tracking.
    - `HoverLeave`: clear.
  - `HandlesClick` is true over slot, item and chip. `CapturesPointerDrag` stays **false** (slots are drag sources through `IsDragSource` in Task 11; the frame body drags the window).
  - `OnHitTest` returns `Frame.Contains`.

- [ ] **Step 4: Run the Verify command, plus `MAC_TEST_FILTER='FullyQualifiedName~CommandDeck'` (because of the `DeckChrome` move). Expect everything to pass.**
- [ ] **Step 5: Commit** `Spell Deck: element (draw, click modes, tooltips)`.

---

### Task 11: UiSpellDeck drag and drop, right-click menu

**Goal:** Slots are drag sources with no change at drag start. Drops on the Spell Deck and drops outside every window run the controller's drag transaction. Right-click opens the row/slot menu.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/UiSpellDeck.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/UiSpellDeckDragTests.cs`

**Acceptance Criteria:**
- [ ] `GetDragPayload()` over a filled slot (and not `IsLocked`) gives `SpellDeckDragPayload(Tab, SpellId)`; over anything else, or while locked, it gives null (no drag)
- [ ] `DragBegin` and `SetDragSourceActive` change nothing (the recorder sees no calls)
- [ ] `DropReleased` with `SpellDeckDragPayload` or `SpellbookShortcutDragPayload` over a slot calls `controller.Drop(source, row, slot)`; over a row past its filled slots it drops at the end; anywhere else does nothing
- [ ] `DragEnter` over a slot sets an accept / reject highlight from `controller.CanDrop`; `DragOver` (leave) clears it
- [ ] `OnDragReleasedOutsideUi(payload)` with a `SpellDeckDragPayload` calls `DropOutside`; any other payload is ignored
- [ ] Cancelled drag (`SetDragSourceActive(false)` with no drop) leaves favourites untouched
- [ ] The right-click menu items, in order, for a slot: `Examine spell`, `Remove from row`, `-`, then the row items:
  - `Add row below` (disabled when `!CanAddRow()`)
  - `Remove this row` (disabled when `!CanRemoveRow`)
  - `-`
  - `Show tab 1` … `Show tab 8` (the current one gets ` (shown)`)
  - `-`
  - `Click casts: on|off`
  - `-`
  - `Detach from deck` | `Attach to deck`
- [ ] On a chip, the slot items are left out. Selecting an item calls the matching controller method or `ToggleClickCasts` / `ToggleAttached` callbacks

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiSpellDeck' run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** against the element directly:
  - Call `GetDragPayload()` after a `MouseDown` at a slot.
  - Send `UiEvent(…, UiEventType.DropReleased, Data1, Data2, Payload: …)`.
  - Call the internal `OnDragReleasedOutsideUi(payload)`.
  - For the menu, call the internal `MenuItemsAt(x, y)` (a list of `(string Text, bool Enabled)`) and `SelectMenuItem(index)`. Keep the menu pure through these two methods so it can be tested without opening a popup.
  - **Cancel test:** `MouseDown` on a slot, `GetDragPayload()`, `SetDragSourceActive(true, p)`, `SetDragSourceActive(false, p)`, and assert the recorder has no calls.

- [ ] **Step 2: Run, expect failures.**

- [ ] **Step 3: Implement.**

```csharp
/// <summary>A spell lifted from a Spell Deck slot. Nothing changes until it lands.</summary>
public sealed record SpellDeckDragPayload(int Tab, uint SpellId);
```

  - Remember the last `MouseDown` point (`_pressX`, `_pressY`). Override `IsDragSource => HitAt(_pressX, _pressY).Zone == SpellDeckZone.Slot && !IsLocked()` and `GetDragPayload()` to give the payload of the filled slot under the press (null if empty or locked). Also override `GetDragGhost()` → `(spellIcon(id), 32, 32)`. `IsLocked` is a constructor `Func<bool>` (the runtime passes `() => Host.WindowManager.IsLocked`).
  - Leave `SetDragSourceActive` alone. No state change at drag start is the point, so nothing should hide or remove the slot while dragging.
  - In `OnEvent`:
    - `DropReleased`: map the payload to a source (`SpellDeckDragPayload p` → `new SpellDeckDragSource(p.Tab, p.SpellId)`; `SpellbookShortcutDragPayload s` → `FromSpellbook(s.SpellId)`). For a Slot hit → `controller.Drop(source, row, slot)`. For a Frame hit in a row band (between the row's first slot top and bottom) → `Drop(source, row, controller.Favorites(tab).Count)`. Otherwise ignore. Return true when the payload was ours or the spellbook's.
    - `DragEnter`: set `_dropHighlight = (row, slot, accept)` from `CanDrop`. `DragOver` (leave): clear it. Draw the highlight as a green or red 2 px stroke around that slot.
  - `internal void OnDragReleasedOutsideUi(object payload)`: if it's a `SpellDeckDragPayload p` → `controller.DropOutside(new(p.Tab, p.SpellId))`. The runtime subscribes this to `Host.Root.DragReleasedOutsideUi` in Task 13.
  - **Menu:** set `ContextMenu = _menu` where `_menu = new UiMarkupContextMenu { ItemsSource = …, ItemsEnabledSource = …, Selected = SelectMenuItem, Opening = (x, y) => _menuAt = HitAt(x, y) }` and add it as a child (as `MarkupContextMenu` does; see `src/AcDream.App/UI/Markup/MarkupContextMenu.cs:25`). Use `DeckFonts.Small` for its `DatFont` when fonts exist. Build the items from `_menuAt` with `MenuItemsAt`, keeping a parallel `List<Action?>` (separators are null) so `SelectMenuItem(i)` runs entry i. The toggles call constructor callbacks `Action toggleClickCasts` and `Action<bool> setAttached`.

- [ ] **Step 4: Run the Verify command and expect it to pass.**
- [ ] **Step 5: Commit** `Spell Deck: drag transaction and right-click menu`.

---

### Task 12: Persistence capture gate and SpellDeckDock

**Goal:**
- Window persistence can pause per window.
- The dock ties the Spell Deck to the Command Deck: position, scale and stretch, the minimum width, the top limit, Add-row availability, the restore fit, and detach / attach.

**Files:**
- Modify: `src/AcDream.App/UI/RetailWindowLayoutPersistence.cs`
- Create: `src/AcDream.App/UI/Hud/SpellDeckDock.cs`
- Test: `tests/AcDream.App.Tests/UI/RetailWindowLayoutPersistenceCaptureGateTests.cs`, `tests/AcDream.App.Tests/UI/Hud/SpellDeckDockTests.cs`

**Acceptance Criteria:**
- [ ] `SetCaptureGate(name, Func<bool> open)`: while closed, `Moved` / `Resized` for that window neither update the stored placement nor save, and `RestoreAll` / `ReflowToScreen` / `ResetToDefaults` don't move it. `ApplyStoredPlacement(name)` puts the window at its stored placement
- [ ] Attached dock tick:
  - the Spell Deck's left = deck left, its bottom = deck top, its scale and stretch = the deck's, and rows follow the controller
  - deck `MinStretch` = `StretchFor(controller.MinSlots)`; deck `TopLimit` = Spell Deck height
  - `CanAddRow` is false when one more row's height would put the top above 0
- [ ] A rising `MinSlots` widens the deck on the next tick (`EnforceMinStretch`), even while the Spell Deck is hidden
- [ ] Restore fit: if `deck.Top − spell.Height < 0`, the deck moves down to `spell.Height` (clamped to the screen). If it still doesn't fit (`deck.Height + spell.Height > screen`), the deck's scale steps down 0.05 at a time until it does or reaches 0.75
- [ ] Detach: gate open, `ApplyStoredPlacement`, the detached Spell Deck gets its own size from `FromSize` with the current rows, and the deck's `MinStretch` / `TopLimit` become null. Attach: gate closed, snaps back on the next tick. Repeating attach → move deck → detach puts the Spell Deck back at the same detached spot
- [ ] The default (never-detached) stored placement is just above the deck's default top-left (set by the mount options in Task 13)

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~CaptureGate|FullyQualifiedName~SpellDeckDock|FullyQualifiedName~RetailWindowLayoutPersistence' run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.**
  - **Persistence:** copy the setup of an existing `RetailWindowLayoutPersistence` test (`grep -rln "RetailWindowLayoutPersistence" tests/AcDream.App.Tests`). Register a window `"w"`, `SetCaptureGate("w", () => open)` with `open = false`, `handle.MoveTo(300, 300)`, and assert that the store's saved placement for `"w"` is unchanged. Call `RestoreAll()` and assert the window is still at 300,300. Set `open = true` and call `ApplyStoredPlacement("w")`; assert it went back to the earlier stored left/top.
  - **Dock:** test it against plain stand-ins rather than a full `UiRoot`. `SpellDeckDock` takes an `ISpellDeckDockHost`, so tests use a fake:

```csharp
public interface ISpellDeckDockHost
{
    (float Left, float Top, float Width, float Height) Screen { get; }
    // Command Deck
    DeckGeometry DeckGeometry { get; }
    float DeckLeft { get; }
    float DeckTop { get; set; }
    void SetDeckGeometry(DeckGeometry geometry);
    void SetDeckLimits(Func<float>? minStretch, Func<float>? topLimit);
    bool EnforceDeckMinStretch();
    // Spell Deck window
    void PlaceSpellDeck(float left, float top, SpellDeckGeometry geometry);
    SpellDeckGeometry SpellDeckGeometry { get; }
    void ApplyStoredSpellDeckPlacement();
    void SetSpellDeckCaptureOpen(bool open);
    void SetSpellDeckResizable(bool resizable);
}
```

  Tests cover each Acceptance Criteria bullet with a fake host that records calls and holds the numbers. One test per bullet; name each after its bullet.

- [ ] **Step 2: Run, expect failures.**

- [ ] **Step 3: Implement.**
  - **Persistence:**

```csharp
    private readonly Dictionary<string, Func<bool>> _captureGates = new(StringComparer.Ordinal);

    /// <summary>While <paramref name="open"/> is false, the window's moves and resizes are
    /// not recorded or saved, and restores leave it where it is (its owner places it).</summary>
    public void SetCaptureGate(string windowName, Func<bool> open) => _captureGates[windowName] = open;

    private bool Capturing(RetailWindowHandle handle) =>
        !_captureGates.TryGetValue(handle.Name, out Func<bool>? open) || open();

    /// <summary>Moves a window to its recorded placement (for a gated window being let go).</summary>
    public void ApplyStoredPlacement(string windowName)
    {
        RetailWindowHandle? handle = _attached.FirstOrDefault(h => h.Name == windowName);
        if (handle is null || !_placements.TryGetValue(windowName, out UiWindowPlacement placement)) return;
        _restoring = true;
        try { ApplyPlacement(handle, placement, ValidScreenSize(), restoreVisibility: false); }
        finally { _restoring = false; }
    }
```

    Add `if (!Capturing(handle)) return;` at the top of `OnGeometryChanged`. In `RestoreHandle` (the overload that applies), in `ResetToDefaults`, in `ReflowToScreen` and in `ClampAllToScreen`, skip `ApplyPlacement` / `MoveTo` for handles where `!Capturing(handle)`, but still record `_placements[handle.Name]` from the saved data in `RestoreHandle`. `SaveAll` keeps saving `_placements`, which is not updated while gated, so the last detached placement survives.
  - **Dock:**
    - **Constructor:** `SpellDeckDock(ISpellDeckDockHost host, SpellDeckController controller)`.
    - **`Tick()`** (called every frame by the runtime, whether or not the Spell Deck is shown):
      1. When attached: `host.SetDeckLimits(() => SpellDeckGeometry.StretchFor(controller.MinSlots), () => SpellHeight())`.
      2. `if (host.EnforceDeckMinStretch()) {}`. The deck saves through its own `Resized` event, and `UiCommandDeck` raises it through the handle when `ResizeTo` is used. The runtime host implements `EnforceDeckMinStretch` with `handle.ResizeTo(...)` so persistence sees it.
      3. `var g = SpellDeckGeometry.Create(deck.Scale, deck.Stretch, controller.Rows.Count)`, then `host.PlaceSpellDeck(deck.Left, deck.Top - g.Height, g)`.
      4. When detached: `host.SetDeckLimits(null, null)`, and keep the Spell Deck's own geometry with the row count: `host.PlaceSpellDeck(current.Left, current.Top, host.SpellDeckGeometry.WithRows(n))` with the minimum stretch `StretchFor(controller.MinSlots)` applied through `WithStretch(Math.Max(...))`.
    - **`CanAddRow()`:** attached → `host.DeckTop - SpellDeckGeometry.Create(scale, stretch, rows + 1).Height >= 0`. Detached → the Spell Deck's top minus 28 × scale ≥ 0, or the bottom stays on screen.
    - **`FitToScreen()`** (called after restore and after a resolution change): attached → the rules in the Acceptance Criteria.
    - **`Detach()` / `Attach()`:** `controller.SetAttached(...)` plus the host calls in the Acceptance Criteria.
  - Pass `CanAddRow` into the controller as its `canAddRow` delegate (constructor wiring in Task 13).

- [ ] **Step 4: Run the Verify command and expect it to pass.**
- [ ] **Step 5: Commit** `Spell Deck: dock to the Command Deck; per-window persistence gate`.

---

### Task 13: Runtime wiring (mount, switch, routing, Command Deck, spellbook)

**Goal:** Put the Spell Deck in the game, behind Modern HUD.
- It mounts and docks.
- It shows only in magic.
- It gets the spell hotkeys, falling back to the retail controller when it didn't mount.
- It feeds the Command Deck's CASTING row and CAST, and receives the spellbook's "add to favourites".
- It loads and saves per character.

**Files:**
- Modify: `src/AcDream.App/UI/WindowNames.cs` (`SpellDeck = "spell-deck"`)
- Modify: `src/AcDream.App/UI/Hud/ModernHudSwitch.cs`
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (`MountSpellDeck`, ctor order, `Tick`, `HandleInputAction`, `ReadCommandDeckInput`, `CommandDeckActions.CastOutlined`, spellbook add-favourite, persistence gate, `DragReleasedOutsideUi`, character change)
- Test: `tests/AcDream.App.Tests/UI/Hud/ModernHudSwitchTests.cs` (add), `tests/AcDream.App.Tests/UI/Hud/ModernHudWiringTests.cs` (add)

**Acceptance Criteria:**
- [ ] `ModernHudSwitch` gets `Func<bool> spellDeckMounted` and `Func<bool> magicMode`. While active and in gameplay it shows `spell-deck` only in magic, and hides it when leaving magic, turning off, or leaving the world. `SpellDeckActive` = active and mounted
- [ ] With Modern HUD on and the Spell Deck mounted, `HandleInputAction` gives spell actions to `SpellDeckController.Handle` before `SpellcastingUiController`. With it off, or not mounted, the old order is unchanged, and one log line notes the fallback
- [ ] `ReadCommandDeckInput.OutlinedSpellName` = the name of `controller.OutlinedSpellId` (item → `"{item name} ({spell})"`) while `SpellDeckActive`, else null; `CastOutlined()` → `controller.CastOutlined()`
- [ ] The spellbook's add-favourite callback goes to `controller.AddFavoriteFromSpellbook` while `SpellDeckActive`, else to the retail controller as before
- [ ] `ISpellDeckActions` is implemented over:
  - `_bindings.Magic.Casting.Cast(id)`
  - `_bindings.Magic.ActivateEndowment(itemId)`
  - `_bindings.Magic.AddFavorite(tab, pos, id)`
  - `_bindings.Magic.RemoveFavorite(tab, id)`
  - `AppraisalController?.ExamineSpell(id)`
  - `_bindings.Options.DisplaySystemMessage(text)`
- [ ] Settings load from `SpellDeckSettings.Load(store, characterKey())` when the character key changes, and save through `SpellDeckSettings.Save`. Click mode reads and toggles `MiscSettings.SpellDeckClickCasts` through the same store as Modern HUD
- [ ] The persistence capture gate for `spell-deck` is closed while attached. `spell-deck` is in `stateManagedVisibilityWindows`
- [ ] `Host.Root.DragReleasedOutsideUi += payload => _spellDeck?.OnDragReleasedOutsideUi(payload)`
- [ ] The full suite passes

**Verify:** `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-spell-deck` → 0 failures

**Steps:**

- [ ] **Step 1: Write the failing switch tests** (extend `ModernHudSwitchTests.Harness` with `public bool Magic; public bool SpellMounted = true;`, pass them in, and add `WindowNames.SpellDeck` handling):

```csharp
    [Fact]
    public void SpellDeck_ShownOnlyInMagic()
    {
        var h = new Harness();
        h.Switch.Apply(true);
        Assert.DoesNotContain(WindowNames.SpellDeck, h.Visible);
        h.Magic = true; h.Switch.Tick();
        Assert.Contains(WindowNames.SpellDeck, h.Visible);
        h.Magic = false; h.Switch.Tick();
        Assert.DoesNotContain(WindowNames.SpellDeck, h.Visible);
        h.Magic = true; h.Switch.Tick(); h.Switch.Apply(false);
        Assert.DoesNotContain(WindowNames.SpellDeck, h.Visible);
    }

    [Fact]
    public void SpellDeckNotMounted_DeckStillOn_SpellDeckInactive()
    {
        var h = new Harness { SpellMounted = false, Magic = true };
        h.Switch.Apply(true);
        h.Switch.Tick();
        Assert.True(h.Switch.Active);
        Assert.False(h.Switch.SpellDeckActive);
        Assert.DoesNotContain(WindowNames.SpellDeck, h.Visible);
    }
```

  In `ModernHudWiringTests`, follow its existing pattern (read it first) and add a routing test: with the switch active and the Spell Deck mounted, `UseSpellSlot_1` reaches the controller and not `SpellcastingUiController`. If that test's harness can't reach `HandleInputAction` without a full runtime, extract the routing into a small static `SpellActionRouter.Route(InputAction, bool spellDeckActive, Func<InputAction,bool> spellDeck, Func<InputAction,bool> retail)` in `src/AcDream.App/UI/Hud/` and test that instead.

- [ ] **Step 2: Run, expect failures.**

- [ ] **Step 3: Implement.**
  - **`ModernHudSwitch`:** add the two constructor arguments (default `() => false` for `magicMode` keeps existing callers compiling), `SpellDeckActive => Active && _spellDeckMounted()`, and in `Tick()` `SetSpellDeckShown(Active && _gameplayActive() && _magicMode() && _spellDeckMounted())`. It mirrors `SetDeckShown` (show/hide `WindowNames.SpellDeck` plus an `onSpellDeckHidden` callback that ends any hover). `Apply(false)` hides it.
  - **`RetailUiRuntime`:**
    1. **Fields:** `_spellDeckController`, `_spellDeck` (`UiSpellDeck`), `_spellDeckDock`, `_spellDeckCharacter` (last loaded key).
    2. **The switch:** pass `spellDeckMounted: () => _spellDeck is not null`, `magicMode: () => _bindings.Combat.State.CurrentMode == CombatMode.Magic`.
    3. **`MountSpellDeck()`**, called right after `MountCommandDeck()`. Build `SpellDeckController` with:
       - `favorites: _bindings.Magic.Spellbook.GetFavorites`
       - `itemSpell: () => ItemSpellSource.Find(_bindings.Magic.Objects, _bindings.Magic.PlayerGuid())`
       - `capacity: () => _spellDeck?.Geometry.SlotsPerRow ?? 12`
       - `settings: SpellDeckSettings.Default` (loaded per character in Tick)
       - `save: s => s.Save(_bindings.Persistence?.Store, _bindings.Persistence?.CharacterKey() ?? "default")`
       - `clickCasts: () => _bindings.Persistence?.Store?.LoadMisc().SpellDeckClickCasts ?? false`. Cache it in a field refreshed when toggled; don't read the file every frame.
       - `canAddRow: () => _spellDeckDock?.CanAddRow() ?? true`

       Then build `UiSpellDeck` with `spellIcon: _bindings.Magic.ResolveSpellIcon` and the other arguments as in Task 10:
       - `spellInfo`: through `Spellbook.TryGetMetadata`
       - `keyLabel`: `a => _bindings.Keyboard?.Dispatcher is { } d ? SpellDeckKeyLabel.For(d.Bindings, a) : ""`. It reads the live bindings each call, as the spec requires.
       - `pendingSpell`: from `Casting`
       - `isLocked`: `() => Host.WindowManager.IsLocked`
       - toggles: update `MiscSettings` through `SaveMisc(LoadMisc() with { SpellDeckClickCasts = !… })`

       Mount it with `RetailWindowFrame.Mount` (imported chrome, `WindowName = WindowNames.SpellDeck`, `Visible = false`, `ResizableEdges = ResizeEdges.Right`, `ConstrainResizeToParent = true`, `ContentClickThrough = false`). Use `UiSpellDeck.MountOptions(left, top)`, written the same way as `UiCommandDeck.MountOptions`. The default left/top is just above the deck's default top-left:

```csharp
float deckLeft = Math.Max(0f, (Host.Root.Width - Hud.DeckGeometry.Default.Width) * 0.5f);
float deckTop = Math.Max(0f, Host.Root.Height - Hud.DeckGeometry.Default.Height - 20f);
var spellDefault = Hud.SpellDeckGeometry.Create(1f, 1f, 3);
Hud.UiSpellDeck.MountOptions(deckLeft, Math.Max(0f, deckTop - spellDefault.Height - 8f))
```

       Build `SpellDeckDock` with a private `RuntimeSpellDeckDockHost` that implements `ISpellDeckDockHost` over the deck and Spell Deck handles (`Host.WindowManager.TryGet(...)`), `_commandDeck` (`Geometry`, `ApplyGeometry`, `MinStretch`, `TopLimit`, `EnforceMinStretch` followed by `handle.ResizeTo(deck.Width, deck.Height)`), and `_persistence` (`SetCaptureGate`, `ApplyStoredPlacement`).
       If either deck failed to mount, log `[UI] Spell Deck unavailable; spell keys stay on the retail bar.` and leave `_spellDeck` null.
    4. **After the persistence block:** `_persistence.SetCaptureGate(WindowNames.SpellDeck, () => !(_spellDeckController?.Attached ?? false));` and add `WindowNames.SpellDeck` to `stateManagedVisibilityWindows`.
    5. **`Tick`:**
       - If the character key changed (`_bindings.Persistence?.CharacterKey()`), call `_spellDeckController.Load(SpellDeckSettings.Load(store, key))` and `_spellDeckDock.FitToScreen()`.
       - Then `_spellDeckController?.Tick(); _spellDeckDock?.Tick();`
       - Call `FitToScreen()` after `RestoreAfterDisplayChange` / `ReflowToScreen` too.
    6. **`HandleInputAction`:**

```csharp
        if (_hudSwitch?.SpellDeckActive == true && _spellDeckController is { } deck)
        {
            if (deck.Handle(action)) return true;
        }
        else if (SpellcastingUiController?.Handle(action) == true)
            return true;
```

       This replaces the single `SpellcastingUiController?.Handle` line. Log the fallback once in `MountSpellDeck` when it fails, not on every key.
    7. **`ReadCommandDeckInput`:** `OutlinedSpellName: _hudSwitch?.SpellDeckActive == true ? OutlinedName() : null`, where `OutlinedName()` uses `SpellName(controller.OutlinedSpellId)` and, for `Item`, `ComposeItemName` (`"{item.GetAppropriateName()} ({spell name})"`).
    8. **`CommandDeckActions`:** pass a `Func<SpellDeckController?>` in; `CastOutlined() => spellDeck()?.CastOutlined();`.
    9. **Spellbook add-favourite** (in `MountSpellbook`):

```csharp
            spellId =>
            {
                if (_hudSwitch?.SpellDeckActive == true && _spellDeckController is { } deck)
                    deck.AddFavoriteFromSpellbook(spellId);
                else
                    SpellcastingUiController?.AddFavorite(spellId);
            },
```

    10. **`Host.Root.DragReleasedOutsideUi`:** subscribe in `MountSpellDeck` and unsubscribe where the deck is detached (`RetailUiRuntime.cs:5643` area, next to `_commandDeck?.Detach()`).

- [ ] **Step 4: Run the full Verify command** and expect 0 failures. Then build and launch-check that the app starts headless without exceptions in the log, as the Command Deck plan did (`grep -n "Spell Deck" <log>` shows the mount line).
- [ ] **Step 5: Commit** `Spell Deck: mount, Modern HUD switch, hotkey routing, CAST wiring`.

---

### Task 14: Live check on the local ACE server (user gate)

**Goal:** The user confirms the Spell Deck works in the real client on the local ACE server.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- None (fixes found here become their own commits on `feat/spell-deck`)

**Acceptance Criteria:**
- [ ] The user says they are available. Only then launch the dev client against 10.10.20.20 (`~/OpenAC-dev/dev-client.env`), never Dreamweave
- [ ] With Modern HUD on, in magic mode the Spell Deck sits flush on the deck. Peace, melee and missile hide it. Turning Modern HUD off brings the retail spell bar back unchanged
- [ ] Rows: add, remove and change tab from the right-click menu; Add row is disabled near the top of the screen
- [ ] Hotkeys: 1–9 for row 1, Shift for row 2, Ctrl for row 3. Rebind one in Configure Keyboard → the slot label changes. Insert/PgUp change row 1's tab; Del/PgDn/End move the outline and cast
- [ ] Both click modes behave as specified (a double-click casts once by default, twice in "Click casts")
- [ ] The deck's CASTING row shows the outlined spell; CAST and End cast it
- [ ] Item spell: a wand with a spell shows and casts; unwielding it empties the socket
- [ ] Drag from the spellbook; reorder; move between rows; drag off the bar removes it; a full row refuses with the chat line; a cancelled drag (Esc) keeps the spell; dragging is blocked while the UI is locked
- [ ] Attached: right-edge stretch and corner scale move both, and the deck can't be made narrower than the fullest row. Detached: its own resize; attach/detach keeps the detached spot
- [ ] Relog keeps the rows, tabs, attachment and position. Retina text and icons are sharp. Check whether the ⇧/⌥ glyphs draw; if not, change `SpellDeckKeyLabel.Shift` / `Alt` to `"S"` / `"A"` and commit

**Verify:** The user confirms each item in chat. Capture a Retina screenshot of the attached Spell Deck with 3 rows, using the probe-script backbuffer capture from the retina-capture workflow.

```json:metadata
{"userGate": true, "tags": ["user-gate"], "files": [], "verifyCommand": "user confirmation per item + Retina screenshot", "acceptanceCriteria": ["user available before launch", "magic-only visibility; retail unchanged when off", "rows add/remove/tab", "hotkeys rows 1-3 + rebind label + nav keys", "click modes counts", "CASTING row + CAST/End", "item spell", "drag transaction cases", "attached/detached resize + min width", "relog persistence + Retina + glyphs"], "modelTier": "standard"}
```

**Steps:**

- [ ] **Step 1:** Ask the user whether they are available for a live check. Wait for a yes.
- [ ] **Step 2:** Build: `~/.dotnet/dotnet build ~/code/OpenAC/OpenAC-spell-deck/AcDream.sln -c Debug -v q`. Launch the dev client the way the Command Deck live check did (see the memory note `live-testing-rules`).
- [ ] **Step 3:** Walk the user through the checklist and record each answer. Fix failures in their own commits and re-run the matching unit tests.
- [ ] **Step 4:** Commit any fixes, with messages starting `Spell Deck:`.

---

## Self-review notes (done while writing)

- **Spec coverage:**

  | Spec section | Task |
  |---|---|
  | Layout | 4, 10 |
  | Size and attachment | 4, 8, 12 |
  | Staying on screen | 8, 12 |
  | Minimum width when it changes | 8, 12 |
  | Rows | 9, 11 |
  | Outline table | 6, 9 |
  | Click modes | 9, 10 |
  | CASTING row | 8, 13 |
  | Hotkeys, fallback, labels, keybinding page | 2, 3, 9, 13 |
  | Editing favourites and drag transaction | 5, 9, 11 |
  | Item spell | 1, 9, 10 |
  | Right-click menu | 11 (flat items, see the user decisions) |
  | Saved state and restore order | 7, 12, 13 |
  | Testing list | each task's tests; the live list is Task 14 |

- **Restore order** (rows before placement): Task 13 loads `SpellDeckSettings` on the character change before the dock's `Tick`/`FitToScreen`. The window placement restore comes from `RetailWindowLayoutPersistence`, which only applies while detached (the gate). Detached size normalisation uses `SpellDeckGeometry.FromSize(width, height, rows)` in the dock's detached branch.
- **Type names used across tasks:**
  - Pure units: `SpellDeckRow`, `SpellDeckGeometry`, `SpellDeckHit`, `SpellDeckZone`, `SpellDeckModel`, `SpellDeckDragSource`, `SpellDeckFavoriteOp`, `SpellDeckDropPlan`, `SpellDeckDropKind`, `SpellDeckOutline`, `SpellDeckOutlineKind`, `SpellDeckOutlineContext`, `SpellDeckSettings`, `StoredSpellDeck`
  - Behaviour and UI: `SpellDeckController`, `ISpellDeckActions`, `SpellDeckKeyLabel`, `UiSpellDeck`, `SpellDeckDragPayload`, `SpellDeckDock`, `ISpellDeckDockHost`, `DeckChrome`
  - Input and spells: `SpellBarActions`, `ClientActionCatalog`, `ClientActionEntry`, `ItemSpell`, `ItemSpellSource`
