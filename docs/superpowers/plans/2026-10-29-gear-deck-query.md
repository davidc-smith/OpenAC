# Gear Deck Plan 2: Query Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Gear Deck query bar: type groups, chips with suggestions, sort with direction, saved filters per character, flat results across every pack with pack badges, multi-select, a right-click item menu, and "Add N to salvage" into the open retail salvage panel.

**Architecture:** Pure Core code does the work: `GearProjection` turns the plan-1 `GearInventoryModel` into `GearItemView` rows, `GearQueryEngine` filters and sorts them, `GearChipSuggester` turns typed text into chips, and `GearQueryJson` saves filters. `GearDeckGeometry` gains a query-bar row, a saved-filter row, a suggestion list and a footer button. `UiGearDeck` shows one list, `Shown`. It holds the selected pack's items when the query is empty and the results when it is not. Drawing, hit tests, drag, drop, double-click, tooltips and selection all work on `Shown`. Salvage goes through a small `IGearSalvage` seam over the existing `SalvageUiController`.

**Tech Stack:** C# / .NET 10, xUnit, `System.Text.Json.Nodes`, the retained UI (`UiElement`, `UiField`, `UiMarkupContextMenu`), `DeckChrome`, `DeckFonts`.

**Spec:** `docs/superpowers/specs/2026-10-28-gear-deck-design.md` (Delivery item 2). The plan-1 plan is `docs/superpowers/plans/2026-10-28-gear-deck-core.md`. Its SDD ledger, with the deferred minors, is in the main checkout at `.superpowers/sdd/2026-10-28-gear-deck-core/progress.md`.

**Worktree:** implement in `~/code/OpenAC/OpenAC-gear-deck` on branch `feat/gear-deck-query`, made from `docs/gear-deck-spec`. That branch is main `fb7d3378` plus this plan. The Verify commands use that path.

## Global Constraints

- Width stays fixed at **300 points**. The window's minimum height still comes from `GearDeckGeometry.MinHeightFor`, which now also counts the query bar and the saved-filter row (when there are saved filters).
- The look is `DeckChrome` palette + `DeckFonts` only, never the plugin theme. Menus use `UiMarkupContextMenu` with `ThemePalette = ChatDeckPalette.Menu`, as the Chat Deck does.
- **Public data only.** Filters and sorts read only what create/update messages carry (name, `ItemType`, material, workmanship, value, burden, stack). Nothing sends an appraisal (ID) request.
- **No new network messages.** Salvage goes through `SalvageUiController.CanAdd` / `AddItem`. Drops go through the plan-1 `GearDropDispatcher`.
- **Retail stays untouched.** The retail inventory, paper doll and salvage windows behave exactly as before. The one shared-control change is the additive `UiField.OnBackspaceWhenEmpty` hook (Task 7).
- Text drawn in the deck is ASCII apart from `·`, which plan 1 already draws. Write `>=`, `<=` and `-` rather than `≥`, `≤` or `–`, and draw the sort direction as a chevron rather than an arrow glyph.
- Children placed by deck code need `Anchors = None`; use the `Place` helper pattern from `UiChatDeck.cs:233`.
- Tests run through `~/code/OpenAC/test-baseline/run-mac-tests.sh`, narrowed with `MAC_TEST_FILTER` and never with a second `--filter`. Never run tests that are known to fail.
- Commit messages end with `Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs`.
- Fork-only: no upstream PR, no plugin contract change.

**User decisions (already made):**
- Query = chips + free text + sort with direction; saved filters per character.
- A non-empty query shows flat results across all packs, each with a pack badge; an empty query shows the normal pack view.
- Public data only (no ID requests); ID-based filters come later.
- Salvage and tinkering stay in the retail windows; the Gear Deck only feeds the open retail salvage panel ("Add N to salvage").
- Grid and List toggle (plan 1, remembered per character).
- The live query is not saved between sessions.
- Modern HUD windows look like the decks, never the plugin theme.

**Plan-2 scope notes (decided while planning; flag at review if wrong):**
- **Locks are plan 4.** There is no Locked chip, padlock or lock filtering in plan 2. "Add N to salvage" skips nothing for locks yet. Plan 4 adds that skip in one place (`GearSalvageHandOff.Candidates`).
- **Group drag is plan 4.** Multi-select chooses the targets of "Add to salvage" and the menu. A drag still moves only the item under the pointer, because moving many items needs plan 4's `GearMoveQueue`. Split…, Lock and Combine are left out of the menu until plan 4.
- **No "All" cell in the side column.** While results are shown, clicking a pack cell toggles a Pack chip for that pack. Several Pack chips are OR'd, and no Pack chip means every pack. Pack cells with a chip are outlined, and with none outlined every pack is searched. An "All" cell would add a cell to the column and push `MinHeightFor` up, so the packs themselves do the job.
- **Worn items are excluded unless the query has a Worn chip.** With a Worn chip, only worn items are shown. Typing "steel" therefore never puts worn armour into a salvage hand-off.
- **Side packs are rows in the results** (group Containers, badge M), so "Type: Containers" finds them. They are never salvage candidates.
- **Sort applies to results only.** The normal pack view keeps the retail slot order, so drops land where the player expects.
- `>` and `>=` (and `<` and `<=`) mean the same thing in typed ranges: "work > 7" is Workmanship >= 7.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.Core/Items/MaterialNames.cs` (create) | The 77 retail material names by `MaterialType` id; `GameEventWiring` uses it |
| `src/AcDream.Core.Net/GameEventWiring.cs` (modify) | `SalvageMaterialName` → `MaterialNames.Name` |
| `src/AcDream.Core/Items/Gear/GearTypeGroup.cs` (create) | The 13 friendly type groups, labels and classification |
| `src/AcDream.Core/Items/Gear/GearProjection.cs` (create) | `GearItemView` rows: every pack's items, worn items and side packs |
| `src/AcDream.Core/Items/Gear/GearQuery.cs` (create) | `GearField`, `GearChip`, `GearSortField`, `GearSort`, `GearQuery`, `SavedFilter` |
| `src/AcDream.Core/Items/Gear/GearQueryEngine.cs` (create) | Filter (OR within a field, AND across) and sort (missing values last, ties by name then pack order) |
| `src/AcDream.Core/Items/Gear/GearChipSuggester.cs` (create) | Typed text → suggested chips |
| `src/AcDream.Core/Items/Gear/GearChipText.cs` (create) | Chip, suggestion, sort-field and value-column labels |
| `src/AcDream.Core/Items/Gear/GearQueryJson.cs` (create) | Saved filter ↔ JSON |
| `src/AcDream.App/UI/Hud/GearDeckSettings.cs` (modify) | `SavedFilters` (+ unread entries kept), add/replace/remove helpers |
| `src/AcDream.App/UI/Hud/GearDeckSession.cs` (modify) | `Ended` event, so the deck clears the live query at logout |
| `src/AcDream.App/UI/Hud/GearDeckGeometry.cs` (modify) | Query bar, chips, saved-filter row, suggestions, footer button, new zones |
| `src/AcDream.App/UI/Hud/UiGearDeck.cs` (modify) | Query state, projection, `Shown`, selection, pills for geometry |
| `src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs` (modify) | Results grid/list with pack badges and value columns, selection, footer count |
| `src/AcDream.App/UI/Hud/UiGearDeck.Input.cs` (modify) | Plan-1 input moved onto `Shown`; side column as Pack chips while results show |
| `src/AcDream.App/UI/Hud/UiGearDeck.Query.cs` (create) | Query bar: field, chips, suggestions, sort menu, direction, saved filters |
| `src/AcDream.App/UI/Hud/UiGearDeck.Menu.cs` (create) | The shared deck menu: item, saved-filter, sort and hidden-chip menus |
| `src/AcDream.App/UI/Hud/GearSalvageHandOff.cs` (create) | `IGearSalvage` seam, runtime adapter, candidate count and add |
| `src/AcDream.App/UI/UiField.cs` (modify) | `OnBackspaceWhenEmpty` hook |
| `src/AcDream.App/UI/RetailUiRuntime.cs` (modify) | Pass `Keyboard` and the salvage adapter into `GearDeckBindings` |

---

### Task 1: Material names, type groups and the projection (Core)

**Goal:** Core can turn the Gear Deck's model into flat `GearItemView` rows that carry each item's friendly type group, material, workmanship, value, burden, stack, pack and slot.

**Files:**
- Create: `src/AcDream.Core/Items/MaterialNames.cs`, `src/AcDream.Core/Items/Gear/GearTypeGroup.cs`, `src/AcDream.Core/Items/Gear/GearProjection.cs`
- Modify: `src/AcDream.Core.Net/GameEventWiring.cs:1175-1197`
- Test: `tests/AcDream.Core.Tests/Items/MaterialNamesTests.cs`, `tests/AcDream.Core.Tests/Items/Gear/GearTypeGroupTests.cs`, `tests/AcDream.Core.Tests/Items/Gear/GearProjectionTests.cs`

**Acceptance Criteria:**
- [ ] `MaterialNames.Name(64)` is "Steel", `Name(1)` is "Ceramic", `Name(77)` is "Teak", and `Name(0)` / `Name(78)` are "Unknown". `MaterialNames.All` yields ids 1-77 in order.
- [ ] `GameEventWiring.SalvageMaterialName` is gone and its caller uses `MaterialNames.Name`. Existing salvage-message tests still pass.
- [ ] `GearTypeGroups.Classify` maps: `TinkeringMaterial` → Salvage, `ManaStone` → ManaStones, a name containing "Healing Kit" → HealingKits, `Food` → Food, `Gem` → Gems, `Writable` with a spell id or a name starting "Scroll" → Scrolls, `Key` → Keys, `Armor` → Armour, melee/missile/caster → Weapons, `Jewelry` → Jewellery, `Clothing` → Clothing, `Container` → Containers, anything else → Other. Checks run in that order.
- [ ] `GearProjection.Build(model, objects, displayName)`:
  - `ForPack(id)` holds the pack's loose items in exactly `model.Contents(id)` order, with `Slot` = index in that list and `PackIndex` = index in `model.Packs`
  - `All` holds every pack's items in pack order, then one `IsPack` row per side pack (PackId = the main pack, PackIndex 0, Slot -1), then one `Worn` row per worn item (PackId 0, PackIndex -1, Slot -1)
  - `PackItemCount` counts the loose items in all packs
- [ ] `Name` on each row comes from the `displayName` delegate. `Group` is classified from the raw `ClientObject.Name`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~MaterialNamesTests|FullyQualifiedName~GearTypeGroupTests|FullyQualifiedName~GearProjectionTests|FullyQualifiedName~GameEventWiring' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.**

`tests/AcDream.Core.Tests/Items/MaterialNamesTests.cs`:

```csharp
using AcDream.Core.Items;
using Xunit;

namespace AcDream.Core.Tests.Items;

public sealed class MaterialNamesTests
{
    [Theory]
    [InlineData(1u, "Ceramic")]
    [InlineData(64u, "Steel")]
    [InlineData(77u, "Teak")]
    [InlineData(0u, "Unknown")]
    [InlineData(78u, "Unknown")]
    public void Name_ByRetailId(uint id, string name) => Assert.Equal(name, MaterialNames.Name(id));

    [Fact]
    public void All_IsOneToSeventySevenInOrder()
    {
        var all = MaterialNames.All.ToList();
        Assert.Equal(77, all.Count);
        Assert.Equal((1u, "Ceramic"), all[0]);
        Assert.Equal((77u, "Teak"), all[^1]);
    }
}
```

`tests/AcDream.Core.Tests/Items/Gear/GearTypeGroupTests.cs`:

```csharp
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearTypeGroupTests
{
    [Theory]
    [InlineData(ItemType.TinkeringMaterial, "Salvaged Steel", GearTypeGroup.Salvage)]
    [InlineData(ItemType.ManaStone, "Major Mana Stone", GearTypeGroup.ManaStones)]
    [InlineData(ItemType.Misc, "Peerless Healing Kit", GearTypeGroup.HealingKits)]
    [InlineData(ItemType.Food, "Apple", GearTypeGroup.Food)]
    [InlineData(ItemType.Gem, "Sapphire", GearTypeGroup.Gems)]
    [InlineData(ItemType.Writable, "Scroll of Blade Bane", GearTypeGroup.Scrolls)]
    [InlineData(ItemType.Writable, "Journal", GearTypeGroup.Other)]
    [InlineData(ItemType.Key, "Copper Key", GearTypeGroup.Keys)]
    [InlineData(ItemType.Armor, "Chainmail Hauberk", GearTypeGroup.Armour)]
    [InlineData(ItemType.MissileWeapon, "Longbow", GearTypeGroup.Weapons)]
    [InlineData(ItemType.Caster, "Wand", GearTypeGroup.Weapons)]
    [InlineData(ItemType.Jewelry, "Ring", GearTypeGroup.Jewellery)]
    [InlineData(ItemType.Clothing, "Shirt", GearTypeGroup.Clothing)]
    [InlineData(ItemType.Container, "Pack", GearTypeGroup.Containers)]
    [InlineData(ItemType.Misc, "Pyreal Mote", GearTypeGroup.Other)]
    public void Classify(ItemType type, string name, GearTypeGroup expected) =>
        Assert.Equal(expected, GearTypeGroups.Classify(type, name, spellId: null));

    [Fact]
    public void Classify_WritableWithSpell_IsScroll() =>
        Assert.Equal(GearTypeGroup.Scrolls, GearTypeGroups.Classify(ItemType.Writable, "Inscription", spellId: 42u));

    [Fact]
    public void Labels_AreFriendly()
    {
        Assert.Equal("Mana stones", GearTypeGroups.Label(GearTypeGroup.ManaStones));
        Assert.Equal("Healing kits", GearTypeGroups.Label(GearTypeGroup.HealingKits));
        Assert.Equal(13, GearTypeGroups.All.Count);
    }
}
```

`tests/AcDream.Core.Tests/Items/Gear/GearProjectionTests.cs` — use the fixture pattern from `GearInventoryModelTests` (`AddOrUpdate` + `MoveItem(id, container, slot[, mask])`):

```csharp
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearProjectionTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, Helm = 0xC1;

    private static void Place(ClientObjectTable t, ClientObject o, uint container, int slot)
    {
        t.AddOrUpdate(o);
        Assert.True(t.MoveItem(o.ObjectId, container, slot));
    }

    private static ClientObjectTable World()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        Place(t, new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, Name = "Pack" }, Player, 0);
        Place(t, new ClientObject { ObjectId = 0xA0, Name = "Salvaged Steel", Type = ItemType.TinkeringMaterial,
            MaterialType = 64, Workmanship = 7.5f, Value = 900, Burden = 50, StackSize = 1 }, Player, 0);
        Place(t, new ClientObject { ObjectId = 0xA1, Name = "Apple", Type = ItemType.Food, StackSize = 5, StackSizeMax = 100 }, Player, 1);
        Place(t, new ClientObject { ObjectId = 0xA2, Name = "Sapphire", Type = ItemType.Gem }, Bag, 0);
        t.AddOrUpdate(new ClientObject { ObjectId = Helm, WielderId = Player, Name = "Helm", Type = ItemType.Armor, ValidLocations = EquipMask.HeadWear });
        Assert.True(t.MoveItem(Helm, Player, -1, EquipMask.HeadWear));
        return t;
    }

    private static GearProjection Build(ClientObjectTable t) =>
        GearProjection.Build(GearInventoryModel.Build(t, Player, 100), t, o => "[" + o.Name + "]");

    [Fact]
    public void ForPack_MatchesModelContentsOrder()
    {
        ClientObjectTable t = World();
        GearInventoryModel model = GearInventoryModel.Build(t, Player, 100);
        GearProjection p = GearProjection.Build(model, t, o => o.Name);
        Assert.Equal(model.Contents(Player).Select(o => o.ObjectId), p.ForPack(Player).Select(v => v.Id));
        Assert.Equal([0, 1], p.ForPack(Player).Select(v => v.Slot));
        Assert.All(p.ForPack(Bag), v => Assert.Equal(1, v.PackIndex));
        Assert.Empty(p.ForPack(0xDEAD));
    }

    [Fact]
    public void All_IsPackItemsThenSidePacksThenWorn()
    {
        GearProjection p = Build(World());
        Assert.Equal([0xA0u, 0xA1u, 0xA2u, Bag, Helm], p.All.Select(v => v.Id));
        GearItemView bag = p.All[3];
        Assert.True(bag.IsPack);
        Assert.Equal((Player, 0, -1), (bag.PackId, bag.PackIndex, bag.Slot));
        GearItemView helm = p.All[4];
        Assert.True(helm.Worn);
        Assert.Equal((0u, -1, -1), (helm.PackId, helm.PackIndex, helm.Slot));
        Assert.Equal(3, p.PackItemCount);
    }

    [Fact]
    public void Rows_CarryPublicFields()
    {
        GearItemView steel = Build(World()).All[0];
        Assert.Equal("[Salvaged Steel]", steel.Name);
        Assert.Equal(GearTypeGroup.Salvage, steel.Group);
        Assert.Equal(64u, steel.Material);
        Assert.Equal(7.5f, steel.Workmanship);
        Assert.Equal((900, 50, 1), (steel.Value, steel.Burden, steel.Stack));
    }

    [Fact]
    public void EmptyModel_IsEmpty()
    {
        GearProjection p = GearProjection.Build(GearInventoryModel.Empty, new ClientObjectTable(), o => o.Name);
        Assert.Empty(p.All);
        Assert.Equal(0, p.PackItemCount);
    }
}
```

- [ ] **Step 2: Run** the Verify command → compile errors (types missing).

- [ ] **Step 3: Implement `MaterialNames`.** Move the names out of `GameEventWiring.SalvageMaterialName` (same strings, same ids):

```csharp
namespace AcDream.Core.Items;

/// <summary>The retail material names by MaterialType id (1-77), as the salvage result message prints them.</summary>
public static class MaterialNames
{
    private static readonly string[] Names =
    [
        "Unknown",
        "Ceramic", "Porcelain", "Cloth", "Linen", "Satin", "Silk", "Velvet", "Wool",
        "Gem", "Agate", "Amber", "Amethyst", "Aquamarine", "Azurite", "Black Garnet",
        "Black Opal", "Bloodstone", "Carnelian", "Citrine", "Diamond", "Emerald", "Fire Opal",
        "Green Garnet", "Green Jade", "Hematite", "Imperial Topaz", "Jet", "Lapis Lazuli",
        "Lavender Jade", "Malachite", "Moonstone", "Onyx", "Opal", "Peridot", "Red Garnet",
        "Red Jade", "Rose Quartz", "Ruby", "Sapphire", "Smokey Quartz", "Sunstone", "Tiger Eye",
        "Tourmaline", "Turquoise", "White Jade", "White Quartz", "White Sapphire", "Yellow Garnet",
        "Yellow Topaz", "Zircon", "Ivory", "Leather", "Armoredillo Hide", "Gromnie Hide", "Reed Shark Hide",
        "Metal", "Brass", "Bronze", "Copper", "Gold", "Iron", "Pyreal", "Silver",
        "Steel", "Stone", "Alabaster", "Granite", "Marble", "Obsidian", "Sandstone", "Serpentine",
        "Wood", "Ebony", "Mahogany", "Oak", "Pine", "Teak",
    ];

    public static string Name(uint id) => id >= 1 && id < Names.Length ? Names[id] : Names[0];

    public static IEnumerable<(uint Id, string Name)> All
    {
        get
        {
            for (uint i = 1; i < Names.Length; i++) yield return (i, Names[i]);
        }
    }
}
```

In `GameEventWiring.cs`, delete `SalvageMaterialName` and change `FormatSalvageMaterial` to call `MaterialNames.Name(result.MaterialType)` (add `using AcDream.Core.Items;` if it is not there).

- [ ] **Step 4: Implement `GearTypeGroup.cs`:**

```csharp
namespace AcDream.Core.Items.Gear;

/// <summary>The friendly groups the Type chip offers.</summary>
public enum GearTypeGroup { Salvage, ManaStones, HealingKits, Food, Gems, Scrolls, Keys, Armour, Weapons, Jewellery, Clothing, Containers, Other }

public static class GearTypeGroups
{
    public static IReadOnlyList<GearTypeGroup> All { get; } = Enum.GetValues<GearTypeGroup>();

    public static string Label(GearTypeGroup group) => group switch
    {
        GearTypeGroup.Salvage => "Salvage",
        GearTypeGroup.ManaStones => "Mana stones",
        GearTypeGroup.HealingKits => "Healing kits",
        GearTypeGroup.Food => "Food",
        GearTypeGroup.Gems => "Gems",
        GearTypeGroup.Scrolls => "Scrolls",
        GearTypeGroup.Keys => "Keys",
        GearTypeGroup.Armour => "Armour",
        GearTypeGroup.Weapons => "Weapons",
        GearTypeGroup.Jewellery => "Jewellery",
        GearTypeGroup.Clothing => "Clothing",
        GearTypeGroup.Containers => "Containers",
        _ => "Other",
    };

    /// <summary>The group for an item's type bits, with a name rule where the bits are not enough
    /// (healing kits are Misc; scrolls share Writable with books).</summary>
    public static GearTypeGroup Classify(ItemType type, string name, uint? spellId)
    {
        name ??= "";
        if ((type & ItemType.TinkeringMaterial) != 0) return GearTypeGroup.Salvage;
        if ((type & ItemType.ManaStone) != 0) return GearTypeGroup.ManaStones;
        if (name.Contains("Healing Kit", StringComparison.OrdinalIgnoreCase)) return GearTypeGroup.HealingKits;
        if ((type & ItemType.Food) != 0) return GearTypeGroup.Food;
        if ((type & ItemType.Gem) != 0) return GearTypeGroup.Gems;
        if ((type & ItemType.Writable) != 0
            && (spellId is not null || name.StartsWith("Scroll", StringComparison.OrdinalIgnoreCase)))
            return GearTypeGroup.Scrolls;
        if ((type & ItemType.Key) != 0) return GearTypeGroup.Keys;
        if ((type & ItemType.Armor) != 0) return GearTypeGroup.Armour;
        if ((type & (ItemType.MeleeWeapon | ItemType.MissileWeapon | ItemType.Caster)) != 0) return GearTypeGroup.Weapons;
        if ((type & ItemType.Jewelry) != 0) return GearTypeGroup.Jewellery;
        if ((type & ItemType.Clothing) != 0) return GearTypeGroup.Clothing;
        if ((type & ItemType.Container) != 0) return GearTypeGroup.Containers;
        return GearTypeGroup.Other;
    }
}
```

- [ ] **Step 5: Implement `GearProjection.cs`:**

```csharp
namespace AcDream.Core.Items.Gear;

/// <summary>One item as the query bar sees it: public data only (what create and update messages carry).</summary>
public sealed record GearItemView(
    uint Id, string Name, GearTypeGroup Group, uint Material, float Workmanship,
    int Value, int Burden, int Stack, int StackMax,
    uint PackId, int PackIndex, int Slot, bool Worn, bool IsPack);

/// <summary>
/// The Gear Deck's items as flat rows. <see cref="ForPack"/> keeps the model's
/// slot order, so a row's <see cref="GearItemView.Slot"/> is the index a drop
/// on it uses. <see cref="All"/> is what a query searches.
/// </summary>
public sealed class GearProjection
{
    public static GearProjection Empty { get; } = new([], new Dictionary<uint, IReadOnlyList<GearItemView>>(), 0);

    private readonly IReadOnlyDictionary<uint, IReadOnlyList<GearItemView>> _byPack;

    private GearProjection(IReadOnlyList<GearItemView> all, IReadOnlyDictionary<uint, IReadOnlyList<GearItemView>> byPack, int packItems)
    {
        All = all;
        _byPack = byPack;
        PackItemCount = packItems;
    }

    public IReadOnlyList<GearItemView> All { get; }
    public int PackItemCount { get; }

    public IReadOnlyList<GearItemView> ForPack(uint packId) =>
        _byPack.TryGetValue(packId, out IReadOnlyList<GearItemView>? rows) ? rows : [];

    public static GearProjection Build(GearInventoryModel model, ClientObjectTable objects, Func<ClientObject, string> displayName)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(displayName);
        if (model.Packs.Count == 0) return Empty;

        var all = new List<GearItemView>();
        var byPack = new Dictionary<uint, IReadOnlyList<GearItemView>>();
        for (int p = 0; p < model.Packs.Count; p++)
        {
            GearPack pack = model.Packs[p];
            IReadOnlyList<ClientObject> contents = model.Contents(pack.Id);
            var rows = new List<GearItemView>(contents.Count);
            for (int s = 0; s < contents.Count; s++)
                rows.Add(View(contents[s], displayName, pack.Id, p, s, worn: false, isPack: false));
            byPack[pack.Id] = rows;
            all.AddRange(rows);
        }
        int packItems = all.Count;

        uint main = model.Packs[0].Id;
        for (int p = 1; p < model.Packs.Count; p++)
            if (objects.Get(model.Packs[p].Id) is { } bag)
                all.Add(View(bag, displayName, main, 0, -1, worn: false, isPack: true));
        foreach (ClientObject worn in model.Worn)
            all.Add(View(worn, displayName, 0u, -1, -1, worn: true, isPack: false));

        return new GearProjection(all, byPack, packItems);
    }

    private static GearItemView View(ClientObject o, Func<ClientObject, string> displayName,
        uint packId, int packIndex, int slot, bool worn, bool isPack) =>
        new(o.ObjectId, displayName(o), GearTypeGroups.Classify(o.Type, o.Name, o.SpellId),
            o.MaterialType ?? 0u, o.Workmanship, o.Value, o.Burden, o.StackSize, o.StackSizeMax,
            packId, packIndex, slot, worn, isPack);
}
```

- [ ] **Step 6: Run** the Verify command → all pass.
- [ ] **Step 7: Commit:** "Gear Deck: material names, type groups and item projection".

---

### Task 2: Query model and engine (Core)

**Goal:** A `GearQuery` (chips, free text and sort) filters and orders `GearItemView` rows the way the spec says.

**Files:**
- Create: `src/AcDream.Core/Items/Gear/GearQuery.cs`, `src/AcDream.Core/Items/Gear/GearQueryEngine.cs`
- Test: `tests/AcDream.Core.Tests/Items/Gear/GearQueryEngineTests.cs`

**Acceptance Criteria:**
- [ ] `GearQuery.IsEmpty` is true when there are no chips and the text is blank (sort alone does not count). `Run(items, emptyQuery)` returns `items` unchanged.
- [ ] Chips on the same field are OR'd, different fields are AND'd, and free text is an extra AND'd name-contains (case-insensitive).
- [ ] Range chips (Workmanship, Value, Burden, Stack) are inclusive on `Min`/`Max`. A row with no workmanship (`<= 0`) never matches a Workmanship chip. Material chips match `Material == Id`; a row with material 0 never matches.
- [ ] Without a Worn chip, worn rows are excluded. With one, only worn rows are kept.
- [ ] A Pack chip matches non-worn rows whose `PackId` equals its `Id`.
- [ ] Sort: by the field and direction. Rows missing the field (material 0, workmanship `<= 0`) go last in **both** directions. Ties go by name (ordinal, ignore case, ascending), then `PackIndex`, then `Slot`. Name sort uses the direction on the name itself.
- [ ] `GearSort.DefaultFor(field)` is descending for Workmanship, Value, Burden and Stack, and ascending for Name, Type and Material.
- [ ] `GearQuery` equality compares chips by sequence (`SequenceEqual`), text and sort. `With(chip)` does not add a duplicate. `Without(index)` removes one chip.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearQueryEngineTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests:**

```csharp
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearQueryEngineTests
{
    private const uint Main = 0x50000001, Bag = 0xB1;

    private static GearItemView Row(uint id, string name, GearTypeGroup group = GearTypeGroup.Other, uint material = 0,
        float work = 0, int value = 0, int burden = 0, int stack = 1, uint pack = Main, int packIndex = 0, int slot = 0,
        bool worn = false) =>
        new(id, name, group, material, work, value, burden, stack, 100, worn ? 0u : pack, worn ? -1 : packIndex, worn ? -1 : slot, worn, false);

    private static readonly IReadOnlyList<GearItemView> Items =
    [
        Row(1, "Salvaged Steel", GearTypeGroup.Salvage, material: 64, work: 7.5f, value: 900, slot: 0),
        Row(2, "Salvaged Steel", GearTypeGroup.Salvage, material: 64, work: 9.0f, value: 1200, pack: Bag, packIndex: 1, slot: 0),
        Row(3, "Salvaged Iron", GearTypeGroup.Salvage, material: 61, work: 5.0f, value: 300, slot: 1),
        Row(4, "Apple", GearTypeGroup.Food, value: 5, stack: 20, slot: 2),
        Row(5, "Steel Helm", GearTypeGroup.Armour, material: 64, work: 6.0f, worn: true),
    ];

    private static uint[] Ids(GearQuery q) => GearQueryEngine.Run(Items, q).Select(v => v.Id).ToArray();
    private static GearQuery Q(params GearChip[] chips) => GearQuery.Empty with { Chips = chips };

    [Fact]
    public void EmptyQuery_ReturnsItemsUnchanged()
    {
        Assert.Same(Items, GearQueryEngine.Run(Items, GearQuery.Empty));
        Assert.True((GearQuery.Empty with { Sort = new GearSort(GearSortField.Value, true) }).IsEmpty);
    }

    [Fact]
    public void SameField_IsOr_DifferentFields_AreAnd()
    {
        Assert.Equal([3u, 1, 2], Ids(Q(GearChip.OfMaterial(64), GearChip.OfMaterial(61))));
        Assert.Equal([1u, 2], Ids(Q(GearChip.OfMaterial(64), GearChip.OfType(GearTypeGroup.Salvage))));
    }

    [Fact]
    public void FreeText_IsAndedNameContains()
    {
        Assert.Equal([1u, 2], Ids(GearQuery.Empty with { Text = "STEEL" }));
        Assert.Empty(Ids(Q(GearChip.OfType(GearTypeGroup.Food)) with { Text = "steel" }));
    }

    [Fact]
    public void Ranges_AreInclusive_AndMissingWorkmanshipNeverMatches()
    {
        Assert.Equal([1u, 2], Ids(Q(GearChip.Range(GearField.Workmanship, 7.5, null))));
        Assert.Equal([3u], Ids(Q(GearChip.Range(GearField.Workmanship, null, 5.0))));
        Assert.Equal([4u], Ids(Q(GearChip.Range(GearField.Stack, 20, 20))));
        Assert.DoesNotContain(4u, Ids(Q(GearChip.Range(GearField.Workmanship, null, 10))));
    }

    [Fact]
    public void Worn_ExcludedUnlessChip()
    {
        Assert.DoesNotContain(5u, Ids(Q(GearChip.OfMaterial(64))));
        Assert.Equal([5u], Ids(Q(GearChip.OfMaterial(64), GearChip.WornOnly)));
    }

    [Fact]
    public void PackChip_MatchesPack()
    {
        Assert.Equal([2u], Ids(Q(GearChip.InPack(Bag), GearChip.OfType(GearTypeGroup.Salvage))));
        Assert.Equal([3u, 1, 2], Ids(Q(GearChip.InPack(Bag), GearChip.InPack(Main), GearChip.OfType(GearTypeGroup.Salvage))));
    }

    [Fact]
    public void SortByWorkmanshipBestFirst_MissingLastBothWays()
    {
        var all = Q(GearChip.Range(GearField.Value, 0, null));
        Assert.Equal([2u, 1, 3, 4], Ids(all with { Sort = new GearSort(GearSortField.Workmanship, true) }));
        Assert.Equal([3u, 1, 2, 4], Ids(all with { Sort = new GearSort(GearSortField.Workmanship, false) }));
    }

    [Fact]
    public void Ties_ByNameThenPackOrderThenSlot()
    {
        var steel = Q(GearChip.OfMaterial(64)) with { Sort = new GearSort(GearSortField.Material, false) };
        Assert.Equal([1u, 2], Ids(steel));   // same material and name: main pack (0) before the bag (1)
        var byName = Q(GearChip.Range(GearField.Value, 0, null)) with { Sort = new GearSort(GearSortField.Name, true) };
        Assert.Equal([1u, 2, 3, 4], Ids(byName));
    }

    [Fact]
    public void DefaultDirections()
    {
        Assert.True(GearSort.DefaultFor(GearSortField.Workmanship).Descending);
        Assert.True(GearSort.DefaultFor(GearSortField.Value).Descending);
        Assert.False(GearSort.DefaultFor(GearSortField.Name).Descending);
        Assert.False(GearSort.DefaultFor(GearSortField.Material).Descending);
    }

    [Fact]
    public void QueryEquality_AndChipEdits()
    {
        GearQuery a = Q(GearChip.OfMaterial(64)), b = Q(GearChip.OfMaterial(64));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Same(a, a.With(GearChip.OfMaterial(64)));
        GearQuery two = a.With(GearChip.WornOnly);
        Assert.Equal(2, two.Chips.Count);
        Assert.Equal(a, two.Without(1));
    }
}
```

- [ ] **Step 2: Run** → compile errors.

- [ ] **Step 3: Implement `GearQuery.cs`:**

```csharp
namespace AcDream.Core.Items.Gear;

public enum GearField { Name, Type, Material, Workmanship, Value, Burden, Stack, Pack, Worn }

public enum GearSortField { Name, Type, Material, Workmanship, Value, Burden, Stack }

/// <summary>One filter chip. Which members matter depends on <see cref="Field"/>: Text (Name),
/// Group (Type), Id (Material, Pack), Min/Max (Workmanship, Value, Burden, Stack); Worn has none.</summary>
public sealed record GearChip(GearField Field)
{
    public string Text { get; init; } = "";
    public GearTypeGroup Group { get; init; }
    public uint Id { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }

    public bool IsRange => Field is GearField.Workmanship or GearField.Value or GearField.Burden or GearField.Stack;

    public static GearChip NameContains(string text) => new(GearField.Name) { Text = text };
    public static GearChip OfType(GearTypeGroup group) => new(GearField.Type) { Group = group };
    public static GearChip OfMaterial(uint material) => new(GearField.Material) { Id = material };
    public static GearChip InPack(uint packId) => new(GearField.Pack) { Id = packId };
    public static GearChip Range(GearField field, double? min, double? max) => new(field) { Min = min, Max = max };
    public static GearChip WornOnly { get; } = new(GearField.Worn);
}

public readonly record struct GearSort(GearSortField Field, bool Descending)
{
    public static GearSort Default => new(GearSortField.Name, false);

    /// <summary>Numbers sort best (largest) first; names, types and materials A to Z.</summary>
    public static GearSort DefaultFor(GearSortField field) =>
        new(field, field is GearSortField.Workmanship or GearSortField.Value or GearSortField.Burden or GearSortField.Stack);
}

public sealed record GearQuery(IReadOnlyList<GearChip> Chips, string Text, GearSort Sort)
{
    public static GearQuery Empty { get; } = new([], "", GearSort.Default);

    /// <summary>No chips and no text: the deck shows the normal pack view. The sort alone is not a query.</summary>
    public bool IsEmpty => Chips.Count == 0 && string.IsNullOrWhiteSpace(Text);

    public GearQuery With(GearChip chip) => Chips.Contains(chip) ? this : this with { Chips = [.. Chips, chip] };

    public GearQuery Without(int index) =>
        index < 0 || index >= Chips.Count ? this : this with { Chips = [.. Chips.Take(index), .. Chips.Skip(index + 1)] };

    public bool Equals(GearQuery? other) =>
        other is not null && Text == other.Text && Sort == other.Sort && Chips.SequenceEqual(other.Chips);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Text);
        h.Add(Sort);
        foreach (GearChip c in Chips) h.Add(c);
        return h.ToHashCode();
    }
}

/// <summary>A query the player named with the ★ button.</summary>
public sealed record SavedFilter(string Name, GearQuery Query)
{
    public const int MaxNameLength = 24;
    public const int MaxCount = 20;
}
```

- [ ] **Step 4: Implement `GearQueryEngine.cs`:**

```csharp
namespace AcDream.Core.Items.Gear;

/// <summary>Filters and sorts the Gear Deck's rows for a query.</summary>
public static class GearQueryEngine
{
    public static IReadOnlyList<GearItemView> Run(IReadOnlyList<GearItemView> items, GearQuery query)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(query);
        if (query.IsEmpty) return items;

        IGrouping<GearField, GearChip>[] fields = query.Chips.Where(c => c.Field != GearField.Worn)
            .GroupBy(c => c.Field).ToArray();
        bool worn = query.Chips.Any(c => c.Field == GearField.Worn);
        string text = query.Text.Trim();

        var hits = new List<GearItemView>();
        foreach (GearItemView item in items)
        {
            if (item.Worn != worn) continue;
            if (text.Length > 0 && !item.Name.Contains(text, StringComparison.OrdinalIgnoreCase)) continue;
            bool ok = true;
            foreach (IGrouping<GearField, GearChip> field in fields)
                if (!field.Any(chip => Matches(chip, item))) { ok = false; break; }
            if (ok) hits.Add(item);
        }
        GearSort sort = query.Sort;
        hits.Sort((a, b) => Compare(sort, a, b));
        return hits;
    }

    private static bool Matches(GearChip chip, GearItemView item) => chip.Field switch
    {
        GearField.Name => item.Name.Contains(chip.Text, StringComparison.OrdinalIgnoreCase),
        GearField.Type => item.Group == chip.Group,
        GearField.Material => item.Material != 0 && item.Material == chip.Id,
        GearField.Pack => !item.Worn && item.PackId == chip.Id,
        GearField.Workmanship => item.Workmanship > 0 && InRange(chip, item.Workmanship),
        GearField.Value => InRange(chip, item.Value),
        GearField.Burden => InRange(chip, item.Burden),
        GearField.Stack => InRange(chip, item.Stack),
        _ => true,
    };

    private static bool InRange(GearChip chip, double value) =>
        (chip.Min is not double min || value >= min) && (chip.Max is not double max || value <= max);

    private static bool Missing(GearSortField field, GearItemView v) => field switch
    {
        GearSortField.Material => v.Material == 0,
        GearSortField.Workmanship => v.Workmanship <= 0,
        _ => false,
    };

    private static int Primary(GearSortField field, GearItemView a, GearItemView b) => field switch
    {
        GearSortField.Type => a.Group.CompareTo(b.Group),
        GearSortField.Material => string.Compare(MaterialNames.Name(a.Material), MaterialNames.Name(b.Material), StringComparison.OrdinalIgnoreCase),
        GearSortField.Workmanship => a.Workmanship.CompareTo(b.Workmanship),
        GearSortField.Value => a.Value.CompareTo(b.Value),
        GearSortField.Burden => a.Burden.CompareTo(b.Burden),
        GearSortField.Stack => a.Stack.CompareTo(b.Stack),
        _ => 0,
    };

    /// <summary>The sort field and direction; rows missing the field last either way; then name, pack order, slot.</summary>
    private static int Compare(GearSort sort, GearItemView a, GearItemView b)
    {
        int c;
        if (sort.Field == GearSortField.Name)
        {
            c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return sort.Descending ? -c : c;
        }
        else
        {
            bool ma = Missing(sort.Field, a), mb = Missing(sort.Field, b);
            if (ma != mb) return ma ? 1 : -1;
            c = Primary(sort.Field, a, b);
            if (c != 0) return sort.Descending ? -c : c;
        }
        c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        if (c != 0) return c;
        c = a.PackIndex.CompareTo(b.PackIndex);
        return c != 0 ? c : a.Slot.CompareTo(b.Slot);
    }
}
```

- [ ] **Step 5: Run** → all pass. **Commit:** "Gear Deck: query model and engine".

---

### Task 3: Chip suggester and labels (Core)

**Goal:** Typed words become suggested chips ("steel" → Material: Steel, "salv" → Type: Salvage, "work 7" → Workmanship >= 7), and every chip, suggestion, sort field and value column has a short label.

**Files:**
- Create: `src/AcDream.Core/Items/Gear/GearChipSuggester.cs`, `src/AcDream.Core/Items/Gear/GearChipText.cs`
- Test: `tests/AcDream.Core.Tests/Items/Gear/GearChipSuggesterTests.cs`, `tests/AcDream.Core.Tests/Items/Gear/GearChipTextTests.cs`

**Acceptance Criteria:**
- [ ] `Suggest("")` and whitespace give `[]`. Otherwise at most `MaxSuggestions` (5) chips, and the **last** one is always `NameContains(trimmed text)`.
- [ ] The order is: the range chip (when the text parses as one), then type groups, then materials whose name starts with the text, then materials whose name contains it, then Worn. Groups and materials need at least 3 characters; Worn needs at least 2 ("wo", "wor", "worn").
- [ ] Ranges: `^(work|workmanship|ws|value|val|burden|bu|stack|st)\s*(<=|>=|<|>|=)?\s*(number)$`, case-insensitive. No operator, `>` or `>=` gives Min. `<` or `<=` gives Max. `=` gives Min = Max.
- [ ] Group matching: the text is a prefix (ignore case) of the group's label or of one of its aliases (Salvage: salvage; ManaStones: mana, stone; HealingKits: heal, kit; Food: food; Gems: gem; Scrolls: scroll; Keys: key; Armour: armour, armor; Weapons: weapon, wand, bow, sword; Jewellery: jewel, jewelry, ring, necklace; Clothing: clothing, shirt, pants; Containers: container, pack, bag; Other: none).
- [ ] `GearChipText.Label`:
  - Name → `"x"` (in quotes); Type → the group label; Material → the material name; Pack → `packName(id)`; Worn → "Worn"
  - ranges → "Work >= 7", "Value <= 500", "Burden 50-100", "Stack = 20" (numbers formatted `0.##`, invariant culture)
- [ ] `GearChipText.Suggestion`: "Type: Salvage", "Material: Steel", "Name contains \"x\"", "Worn items", and the `Label` for ranges.
- [ ] `GearChipText.SortLabel`: Workmanship → "Work", the others by name. `GearChipText.Value(field, row)`: Work "7.50" (`0.00`; empty when `<= 0`), Value "1,200" (`N0`), Burden, Stack, Material name (empty when 0), Type label. Name → empty.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearChipSuggesterTests|FullyQualifiedName~GearChipTextTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests:**

```csharp
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearChipSuggesterTests
{
    [Fact]
    public void Blank_GivesNothing()
    {
        Assert.Empty(GearChipSuggester.Suggest(""));
        Assert.Empty(GearChipSuggester.Suggest("   "));
    }

    [Fact]
    public void Steel_SuggestsMaterialFirst_NameLast()
    {
        var s = GearChipSuggester.Suggest("steel");
        Assert.Equal(GearChip.OfMaterial(64), s[0]);
        Assert.Equal(GearChip.NameContains("steel"), s[^1]);
    }

    [Fact]
    public void Salv_SuggestsSalvageGroup() =>
        Assert.Equal(GearChip.OfType(GearTypeGroup.Salvage), GearChipSuggester.Suggest("salv")[0]);

    [Theory]
    [InlineData("work 7", 7.0, null)]
    [InlineData("ws>=7.5", 7.5, null)]
    [InlineData("work > 7", 7.0, null)]
    [InlineData("value < 500", null, 500.0)]
    [InlineData("stack = 20", 20.0, 20.0)]
    public void Ranges(string typed, double? min, double? max)
    {
        GearChip c = GearChipSuggester.Suggest(typed)[0];
        Assert.True(c.IsRange);
        Assert.Equal((min, max), (c.Min, c.Max));
    }

    [Fact]
    public void RangeField_ByWord()
    {
        Assert.Equal(GearField.Workmanship, GearChipSuggester.Suggest("work 7")[0].Field);
        Assert.Equal(GearField.Value, GearChipSuggester.Suggest("val 7")[0].Field);
        Assert.Equal(GearField.Burden, GearChipSuggester.Suggest("bu 7")[0].Field);
        Assert.Equal(GearField.Stack, GearChipSuggester.Suggest("st 7")[0].Field);
    }

    [Fact]
    public void TwoLetters_OnlyWornAndName()
    {
        var s = GearChipSuggester.Suggest("wo");
        Assert.Equal([GearChip.WornOnly, GearChip.NameContains("wo")], s);
    }

    [Fact]
    public void AtMostFive_NameAlwaysLast()
    {
        var s = GearChipSuggester.Suggest("gar");   // Black/Green/Red/Yellow Garnet contain "gar"
        Assert.True(s.Count <= GearChipSuggester.MaxSuggestions);
        Assert.Equal(GearChip.NameContains("gar"), s[^1]);
    }

    [Fact]
    public void Gem_SuggestsGroupThenMaterial()
    {
        var s = GearChipSuggester.Suggest("gem");
        Assert.Equal(GearChip.OfType(GearTypeGroup.Gems), s[0]);
        Assert.Contains(GearChip.OfMaterial(9), s);
    }
}
```

```csharp
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearChipTextTests
{
    private static string L(GearChip c) => GearChipText.Label(c, id => id == 7 ? "Pack 2" : "Main");

    [Fact]
    public void Labels()
    {
        Assert.Equal("\"kit\"", L(GearChip.NameContains("kit")));
        Assert.Equal("Salvage", L(GearChip.OfType(GearTypeGroup.Salvage)));
        Assert.Equal("Steel", L(GearChip.OfMaterial(64)));
        Assert.Equal("Pack 2", L(GearChip.InPack(7)));
        Assert.Equal("Worn", L(GearChip.WornOnly));
        Assert.Equal("Work >= 7", L(GearChip.Range(GearField.Workmanship, 7, null)));
        Assert.Equal("Value <= 500", L(GearChip.Range(GearField.Value, null, 500)));
        Assert.Equal("Burden 50-100", L(GearChip.Range(GearField.Burden, 50, 100)));
        Assert.Equal("Stack = 20", L(GearChip.Range(GearField.Stack, 20, 20)));
        Assert.Equal("Work >= 7.25", L(GearChip.Range(GearField.Workmanship, 7.25, null)));
    }

    [Fact]
    public void Suggestions()
    {
        Assert.Equal("Type: Salvage", GearChipText.Suggestion(GearChip.OfType(GearTypeGroup.Salvage)));
        Assert.Equal("Material: Steel", GearChipText.Suggestion(GearChip.OfMaterial(64)));
        Assert.Equal("Name contains \"kit\"", GearChipText.Suggestion(GearChip.NameContains("kit")));
        Assert.Equal("Worn items", GearChipText.Suggestion(GearChip.WornOnly));
    }

    [Fact]
    public void SortLabelsAndValues()
    {
        Assert.Equal("Work", GearChipText.SortLabel(GearSortField.Workmanship));
        Assert.Equal("Value", GearChipText.SortLabel(GearSortField.Value));
        var row = new GearItemView(1, "x", GearTypeGroup.Salvage, 64, 7.5f, 1200, 50, 3, 100, 1, 0, 0, false, false);
        Assert.Equal("7.50", GearChipText.Value(GearSortField.Workmanship, row));
        Assert.Equal("1,200", GearChipText.Value(GearSortField.Value, row));
        Assert.Equal("Steel", GearChipText.Value(GearSortField.Material, row));
        Assert.Equal("Salvage", GearChipText.Value(GearSortField.Type, row));
        Assert.Equal("", GearChipText.Value(GearSortField.Name, row));
        Assert.Equal("", GearChipText.Value(GearSortField.Workmanship, row with { Workmanship = 0 }));
    }
}
```

- [ ] **Step 2: Run** → compile errors.

- [ ] **Step 3: Implement `GearChipSuggester.cs`:**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace AcDream.Core.Items.Gear;

/// <summary>Turns a typed word into the chips the query bar offers under the field.</summary>
public static partial class GearChipSuggester
{
    public const int MaxSuggestions = 5;

    private static readonly (GearTypeGroup Group, string[] Aliases)[] GroupWords =
    [
        (GearTypeGroup.Salvage, ["salvage"]),
        (GearTypeGroup.ManaStones, ["mana", "stone"]),
        (GearTypeGroup.HealingKits, ["heal", "kit"]),
        (GearTypeGroup.Food, ["food"]),
        (GearTypeGroup.Gems, ["gem"]),
        (GearTypeGroup.Scrolls, ["scroll"]),
        (GearTypeGroup.Keys, ["key"]),
        (GearTypeGroup.Armour, ["armour", "armor"]),
        (GearTypeGroup.Weapons, ["weapon", "wand", "bow", "sword"]),
        (GearTypeGroup.Jewellery, ["jewel", "jewelry", "ring", "necklace"]),
        (GearTypeGroup.Clothing, ["clothing", "shirt", "pants"]),
        (GearTypeGroup.Containers, ["container", "pack", "bag"]),
    ];

    [GeneratedRegex(@"^(?<f>workmanship|work|ws|value|val|burden|bu|stack|st)\s*(?<op><=|>=|<|>|=)?\s*(?<n>\d+(?:\.\d+)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RangePattern();

    public static IReadOnlyList<GearChip> Suggest(string typed)
    {
        string t = (typed ?? "").Trim();
        if (t.Length == 0) return [];

        var picks = new List<GearChip>();
        if (TryRange(t, out GearChip? range)) picks.Add(range!);
        if (t.Length >= 3)
        {
            foreach ((GearTypeGroup group, string[] aliases) in GroupWords)
                if (StartsWith(GearTypeGroups.Label(group), t) || aliases.Any(a => StartsWith(a, t)))
                    picks.Add(GearChip.OfType(group));
            foreach ((uint id, string name) in MaterialNames.All)
                if (StartsWith(name, t)) picks.Add(GearChip.OfMaterial(id));
            foreach ((uint id, string name) in MaterialNames.All)
                if (!StartsWith(name, t) && name.Contains(t, StringComparison.OrdinalIgnoreCase)) picks.Add(GearChip.OfMaterial(id));
        }
        if (t.Length >= 2 && StartsWith("worn", t)) picks.Add(GearChip.WornOnly);

        var result = picks.Distinct().Take(MaxSuggestions - 1).ToList();
        result.Add(GearChip.NameContains(t));
        return result;
    }

    private static bool StartsWith(string text, string prefix) => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static bool TryRange(string t, out GearChip? chip)
    {
        chip = null;
        Match m = RangePattern().Match(t);
        if (!m.Success) return false;
        GearField field = m.Groups["f"].Value.ToLowerInvariant() switch
        {
            "workmanship" or "work" or "ws" => GearField.Workmanship,
            "value" or "val" => GearField.Value,
            "burden" or "bu" => GearField.Burden,
            _ => GearField.Stack,
        };
        double n = double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
        chip = m.Groups["op"].Value switch
        {
            "<" or "<=" => GearChip.Range(field, null, n),
            "=" => GearChip.Range(field, n, n),
            _ => GearChip.Range(field, n, null),
        };
        return true;
    }
}
```

- [ ] **Step 4: Implement `GearChipText.cs`:**

```csharp
using System.Globalization;

namespace AcDream.Core.Items.Gear;

/// <summary>The short texts the query bar draws. ASCII only (the deck fonts' safe range).</summary>
public static class GearChipText
{
    public static string Label(GearChip chip, Func<uint, string> packName) => chip.Field switch
    {
        GearField.Name => "\"" + chip.Text + "\"",
        GearField.Type => GearTypeGroups.Label(chip.Group),
        GearField.Material => MaterialNames.Name(chip.Id),
        GearField.Pack => packName(chip.Id),
        GearField.Worn => "Worn",
        _ => RangeLabel(chip),
    };

    public static string Suggestion(GearChip chip) => chip.Field switch
    {
        GearField.Name => "Name contains \"" + chip.Text + "\"",
        GearField.Type => "Type: " + GearTypeGroups.Label(chip.Group),
        GearField.Material => "Material: " + MaterialNames.Name(chip.Id),
        GearField.Worn => "Worn items",
        _ => Label(chip, _ => "Pack"),
    };

    public static string SortLabel(GearSortField field) => field == GearSortField.Workmanship ? "Work" : field.ToString();

    /// <summary>A row's value for a list column; empty where the row has none.</summary>
    public static string Value(GearSortField field, GearItemView row) => field switch
    {
        GearSortField.Workmanship => row.Workmanship > 0 ? row.Workmanship.ToString("0.00", CultureInfo.InvariantCulture) : "",
        GearSortField.Value => row.Value.ToString("N0", CultureInfo.InvariantCulture),
        GearSortField.Burden => row.Burden.ToString(CultureInfo.InvariantCulture),
        GearSortField.Stack => row.Stack.ToString(CultureInfo.InvariantCulture),
        GearSortField.Material => row.Material != 0 ? MaterialNames.Name(row.Material) : "",
        GearSortField.Type => GearTypeGroups.Label(row.Group),
        _ => "",
    };

    /// <summary>The sort field a range chip's column shows.</summary>
    public static GearSortField? SortFieldOf(GearField field) => field switch
    {
        GearField.Workmanship => GearSortField.Workmanship,
        GearField.Value => GearSortField.Value,
        GearField.Burden => GearSortField.Burden,
        GearField.Stack => GearSortField.Stack,
        _ => null,
    };

    private static string RangeLabel(GearChip chip)
    {
        string name = chip.Field == GearField.Workmanship ? "Work" : chip.Field.ToString();
        string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        return (chip.Min, chip.Max) switch
        {
            (double a, double b) when a == b => $"{name} = {N(a)}",
            (double a, double b) => $"{name} {N(a)}-{N(b)}",
            (double a, null) => $"{name} >= {N(a)}",
            (null, double b) => $"{name} <= {N(b)}",
            _ => name,
        };
    }
}
```

- [ ] **Step 5: Run** → all pass. **Commit:** "Gear Deck: chip suggester and labels".

---

### Task 4: Saved filters in JSON and settings

**Goal:** Saved filters round-trip through the per-character Gear Deck settings, and an entry this build cannot read is kept and written back unchanged.

**Files:**
- Create: `src/AcDream.Core/Items/Gear/GearQueryJson.cs`
- Modify: `src/AcDream.App/UI/Hud/GearDeckSettings.cs`, `src/AcDream.App/UI/Hud/GearDeckSession.cs`
- Test: `tests/AcDream.Core.Tests/Items/Gear/GearQueryJsonTests.cs`, `tests/AcDream.App.Tests/UI/Hud/GearDeckSessionTests.cs` (add cases)

**Acceptance Criteria:**
- [ ] `GearQueryJson.Write(filter)` gives `{"name":..,"chips":[..],"text":..,"sort":..,"desc":..}`. Chips are `{"f":"name","text":..}`, `{"f":"type","group":"Salvage"}`, `{"f":"material","id":64}`, `{"f":"pack","id":..}`, `{"f":"worn"}`, and `{"f":"work"|"value"|"burden"|"stack","min":..,"max":..}`, with min/max left out when null. Sort is one of `name,type,material,work,value,burden,stack`.
- [ ] `TryRead` returns false, without throwing, for: a non-object, a blank or missing name, a missing or non-array `chips`, any chip with an unknown `f` or a bad or numeric `group`, an unknown sort, or a wrong value type. Every `Write` output reads back `Equal`.
- [ ] `GearDeckSettings.SavedFilters` (default empty) loads from `"savedFilters"`. Unreadable entries are kept, in their original order *after* the readable ones, and written back unchanged. Settings equality includes `SavedFilters` (by sequence).
- [ ] `WithSaved(filter)` replaces a filter of the same name (ordinal, ignore case) in place, or appends one, up to `SavedFilter.MaxCount`; past the cap it returns `this`. The name is trimmed and cut to `MaxNameLength`. `WithoutSaved(name)` removes it.
- [ ] Old settings JSON without `savedFilters` loads with an empty list. The plan-1 keys keep their meaning and `version` stays 1.
- [ ] `GearDeckSession` raises a new `Ended` event from `EndSession()`, after the flush.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearQueryJsonTests|FullyQualifiedName~GearDeckSessionTests|FullyQualifiedName~SettingsStoreGearDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** `GearQueryJsonTests`:

```csharp
using System.Text.Json.Nodes;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearQueryJsonTests
{
    private static readonly SavedFilter Full = new("Best steel", new GearQuery(
        [GearChip.OfMaterial(64), GearChip.OfType(GearTypeGroup.Salvage), GearChip.Range(GearField.Workmanship, 7, null),
         GearChip.Range(GearField.Value, 10, 500), GearChip.NameContains("bag"), GearChip.InPack(0xB1), GearChip.WornOnly],
        "x", new GearSort(GearSortField.Workmanship, true)));

    [Fact]
    public void RoundTrip()
    {
        JsonObject o = GearQueryJson.Write(Full);
        Assert.True(GearQueryJson.TryRead(JsonNode.Parse(o.ToJsonString()), out SavedFilter? back));
        Assert.Equal(Full, back);
    }

    [Fact]
    public void Shape()
    {
        JsonObject o = GearQueryJson.Write(Full);
        Assert.Equal("work", (string?)o["sort"]);
        Assert.True((bool)o["desc"]!);
        JsonObject work = o["chips"]![2]!.AsObject();
        Assert.Equal("work", (string?)work["f"]);
        Assert.False(work.ContainsKey("max"));
        Assert.Equal("Salvage", (string?)o["chips"]![1]!["group"]);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"chips\":[]}")]
    [InlineData("{\"name\":\" \",\"chips\":[]}")]
    [InlineData("{\"name\":\"a\"}")]
    [InlineData("{\"name\":\"a\",\"chips\":[{\"f\":\"armourLevel\",\"min\":300}]}")]
    [InlineData("{\"name\":\"a\",\"chips\":[{\"f\":\"type\",\"group\":\"3\"}]}")]
    [InlineData("{\"name\":\"a\",\"chips\":[{\"f\":\"type\",\"group\":\"Shoes\"}]}")]
    [InlineData("{\"name\":\"a\",\"chips\":[],\"sort\":\"damage\"}")]
    [InlineData("{\"name\":\"a\",\"chips\":[{\"f\":\"material\",\"id\":\"steel\"}]}")]
    public void Unreadable(string json) => Assert.False(GearQueryJson.TryRead(JsonNode.Parse(json), out _));

    [Fact]
    public void MissingTextAndSort_Default()
    {
        Assert.True(GearQueryJson.TryRead(JsonNode.Parse("{\"name\":\"a\",\"chips\":[{\"f\":\"worn\"}]}"), out SavedFilter? f));
        Assert.Equal(GearQuery.Empty with { Chips = [GearChip.WornOnly] }, f!.Query);
    }
}
```

Add to `GearDeckSessionTests` (App):

```csharp
[Fact]
public void SavedFilters_RoundTrip_KeepUnreadable()
{
    const string json = "{\"version\":1,\"folded\":false,\"view\":\"grid\",\"selectedPack\":0," +
        "\"savedFilters\":[{\"name\":\"future\",\"chips\":[{\"f\":\"armourLevel\",\"min\":300}]}," +
        "{\"name\":\"Steel\",\"chips\":[{\"f\":\"material\",\"id\":64}]}]}";
    GearDeckSettings s = GearDeckSettings.FromJson(json);
    Assert.Equal(["Steel"], s.SavedFilters.Select(f => f.Name));
    string back = s.ToJson();
    Assert.Contains("armourLevel", back);
    Assert.True(back.IndexOf("\"Steel\"", StringComparison.Ordinal) < back.IndexOf("future", StringComparison.Ordinal));
    Assert.Equal(s, GearDeckSettings.FromJson(back));
}

[Fact]
public void WithSaved_ReplacesByName_CapsCount()
{
    var q = GearQuery.Empty with { Text = "a" };
    GearDeckSettings s = GearDeckSettings.Default.WithSaved(new SavedFilter("Steel", q));
    s = s.WithSaved(new SavedFilter("  steel ", q with { Text = "b" }));
    Assert.Single(s.SavedFilters);
    Assert.Equal("b", s.SavedFilters[0].Query.Text);
    Assert.Equal("steel", s.SavedFilters[0].Name);
    for (int i = 0; i < 30; i++) s = s.WithSaved(new SavedFilter($"f{i}", q));
    Assert.Equal(SavedFilter.MaxCount, s.SavedFilters.Count);
    Assert.Equal(SavedFilter.MaxCount - 1, s.WithoutSaved("STEEL").SavedFilters.Count);
    Assert.Equal(SavedFilter.MaxNameLength, GearDeckSettings.Default.WithSaved(new SavedFilter(new string('x', 40), q)).SavedFilters[0].Name.Length);
}

[Fact]
public void SavedFilters_InEquality()
{
    var q = GearQuery.Empty with { Text = "a" };
    Assert.NotEqual(GearDeckSettings.Default, GearDeckSettings.Default.WithSaved(new SavedFilter("a", q)));
    Assert.Equal(GearDeckSettings.Default.WithSaved(new SavedFilter("a", q)), GearDeckSettings.Default.WithSaved(new SavedFilter("a", q)));
}

[Fact]
public void EndSession_RaisesEnded()
{
    var session = new GearDeckSession(null, () => "", () => false);
    int ended = 0;
    session.Ended += () => ended++;
    session.EndSession();
    Assert.Equal(1, ended);
}
```

(Add `using AcDream.Core.Items.Gear;` to the test file.)

- [ ] **Step 2: Run** → compile errors.

- [ ] **Step 3: Implement `GearQueryJson.cs`:**

```csharp
using System.Text.Json.Nodes;

namespace AcDream.Core.Items.Gear;

/// <summary>Saved filters as JSON. An entry this build cannot read is reported unreadable, never half-read.</summary>
public static class GearQueryJson
{
    private static readonly (GearField Field, string Key)[] FieldKeys =
    [
        (GearField.Name, "name"), (GearField.Type, "type"), (GearField.Material, "material"),
        (GearField.Workmanship, "work"), (GearField.Value, "value"), (GearField.Burden, "burden"),
        (GearField.Stack, "stack"), (GearField.Pack, "pack"), (GearField.Worn, "worn"),
    ];

    private static readonly (GearSortField Field, string Key)[] SortKeys =
    [
        (GearSortField.Name, "name"), (GearSortField.Type, "type"), (GearSortField.Material, "material"),
        (GearSortField.Workmanship, "work"), (GearSortField.Value, "value"), (GearSortField.Burden, "burden"),
        (GearSortField.Stack, "stack"),
    ];

    public static JsonObject Write(SavedFilter filter)
    {
        var chips = new JsonArray();
        foreach (GearChip c in filter.Query.Chips) chips.Add(WriteChip(c));
        return new JsonObject
        {
            ["name"] = filter.Name,
            ["chips"] = chips,
            ["text"] = filter.Query.Text,
            ["sort"] = SortKeys.First(k => k.Field == filter.Query.Sort.Field).Key,
            ["desc"] = filter.Query.Sort.Descending,
        };
    }

    private static JsonObject WriteChip(GearChip c)
    {
        var o = new JsonObject { ["f"] = FieldKeys.First(k => k.Field == c.Field).Key };
        switch (c.Field)
        {
            case GearField.Name: o["text"] = c.Text; break;
            case GearField.Type: o["group"] = c.Group.ToString(); break;
            case GearField.Material or GearField.Pack: o["id"] = c.Id; break;
            case GearField.Worn: break;
            default:
                if (c.Min is double min) o["min"] = min;
                if (c.Max is double max) o["max"] = max;
                break;
        }
        return o;
    }

    public static bool TryRead(JsonNode? node, out SavedFilter? filter)
    {
        filter = null;
        try
        {
            if (node is not JsonObject o || o["chips"] is not JsonArray array) return false;
            string name = ((string?)o["name"] ?? "").Trim();
            if (name.Length == 0) return false;
            var chips = new List<GearChip>();
            foreach (JsonNode? c in array)
            {
                if (ReadChip(c) is not { } chip) return false;
                chips.Add(chip);
            }
            GearSort sort = GearSort.Default;
            if (o["sort"] is { } sortNode)
            {
                string key = (string?)sortNode ?? "";
                int i = Array.FindIndex(SortKeys, k => k.Key == key);
                if (i < 0) return false;
                sort = new GearSort(SortKeys[i].Field, (bool?)o["desc"] ?? false);
            }
            filter = new SavedFilter(name, new GearQuery(chips, (string?)o["text"] ?? "", sort));
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static GearChip? ReadChip(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        string key = (string?)o["f"] ?? "";
        int i = Array.FindIndex(FieldKeys, k => k.Key == key);
        if (i < 0) return null;
        GearField field = FieldKeys[i].Field;
        switch (field)
        {
            case GearField.Name: return GearChip.NameContains((string?)o["text"] ?? "");
            case GearField.Type:
                string g = (string?)o["group"] ?? "";
                return g.Length > 0 && !char.IsDigit(g[0]) && Enum.TryParse(g, ignoreCase: true, out GearTypeGroup group)
                    && Enum.IsDefined(group) ? GearChip.OfType(group) : null;
            case GearField.Material: return o["id"] is { } m ? GearChip.OfMaterial(m.GetValue<uint>()) : null;
            case GearField.Pack: return o["id"] is { } p ? GearChip.InPack(p.GetValue<uint>()) : null;
            case GearField.Worn: return GearChip.WornOnly;
            default: return GearChip.Range(field, o["min"]?.GetValue<double>(), o["max"]?.GetValue<double>());
        }
    }
}
```

- [ ] **Step 4: Extend `GearDeckSettings`.** Keep the record's positional shape, so plan-1 call sites compile unchanged:

```csharp
using System.Text.Json.Nodes;
using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

internal enum GearPackView { Grid, List }

/// <summary>The Gear Deck's per-character view state and saved filters. Unknown keys and
/// saved filters this build cannot read are kept and written back.</summary>
internal sealed record GearDeckSettings(bool Folded, GearPackView View, uint SelectedPack)
{
    public static GearDeckSettings Default { get; } = new(false, GearPackView.Grid, 0u);
    private JsonObject? Extra { get; init; }
    private JsonArray? UnreadFilters { get; init; }
    public IReadOnlyList<SavedFilter> SavedFilters { get; init; } = [];

    public static GearDeckSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return Default;
            bool folded = o["folded"]?.GetValue<bool>() ?? false;
            GearPackView view = (string?)o["view"] == "list" ? GearPackView.List : GearPackView.Grid;
            uint pack = o["selectedPack"]?.GetValue<uint>() ?? 0u;
            var saved = new List<SavedFilter>();
            JsonArray? unread = null;
            if (o["savedFilters"] is JsonArray filters)
                foreach (JsonNode? f in filters)
                {
                    if (GearQueryJson.TryRead(f, out SavedFilter? read)) saved.Add(read!);
                    else (unread ??= []).Add(f?.DeepClone());
                }
            var extra = (JsonObject)o.DeepClone();
            foreach (string k in new[] { "version", "folded", "view", "selectedPack", "savedFilters" }) extra.Remove(k);
            return new GearDeckSettings(folded, view, pack)
            {
                Extra = extra.Count > 0 ? extra : null,
                SavedFilters = saved,
                UnreadFilters = unread,
            };
        }
        catch (Exception) { return Default; }
    }

    public string ToJson()
    {
        var o = Extra is null ? new JsonObject() : (JsonObject)Extra.DeepClone();
        o["version"] = 1;
        o["folded"] = Folded;
        o["view"] = View == GearPackView.List ? "list" : "grid";
        o["selectedPack"] = SelectedPack;
        if (SavedFilters.Count > 0 || UnreadFilters is { Count: > 0 })
        {
            var filters = new JsonArray();
            foreach (SavedFilter f in SavedFilters) filters.Add(GearQueryJson.Write(f));
            if (UnreadFilters is not null) foreach (JsonNode? f in UnreadFilters) filters.Add(f?.DeepClone());
            o["savedFilters"] = filters;
        }
        return o.ToJsonString();
    }

    /// <summary>Saves a filter: a same-named one (ignoring case) is replaced in place; a new one is added up to the cap.</summary>
    public GearDeckSettings WithSaved(SavedFilter filter)
    {
        string name = filter.Name.Trim();
        if (name.Length > SavedFilter.MaxNameLength) name = name[..SavedFilter.MaxNameLength];
        if (name.Length == 0) return this;
        var named = filter with { Name = name };
        var list = SavedFilters.ToList();
        int i = list.FindIndex(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) list[i] = named;
        else if (list.Count >= SavedFilter.MaxCount) return this;
        else list.Add(named);
        return this with { SavedFilters = list };
    }

    public GearDeckSettings WithoutSaved(string name) => this with
    {
        SavedFilters = SavedFilters.Where(f => !string.Equals(f.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList(),
    };

    public bool Equals(GearDeckSettings? other) =>
        other is not null && Folded == other.Folded && View == other.View && SelectedPack == other.SelectedPack
        && SavedFilters.SequenceEqual(other.SavedFilters);
    public override int GetHashCode() => HashCode.Combine(Folded, View, SelectedPack, SavedFilters.Count);
}
```

- [ ] **Step 5: Add `Ended` to `GearDeckSession`:** `public event Action? Ended;`, raised as the last line of `EndSession()` (after `Changed?.Invoke()`).
- [ ] **Step 6: Run** → all pass. **Commit:** "Gear Deck: saved filters in the per-character settings".

---

### Task 5: Geometry for the query bar, saved filters, suggestions and footer button

**Goal:** `GearDeckGeometry` lays out the query bar row, the saved-filter row, the suggestion list and the footer button, and hit-tests their zones.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/GearDeckGeometry.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/GearDeckGeometryTests.cs` (add cases)

**Acceptance Criteria:**
- [ ] `GearDeckLayoutInput` gains trailing defaulted members `int SavedCount = 0, int Suggestions = 0, bool SalvageButton = false`, so plan-1 call sites compile unchanged.
- [ ] New constants: `QueryH = 22`, `SavedH = 18`, `PillH = 18`, `PillGap = 4`, `PillPadX = 6`, `ChipCloseW = 8`, `MaxPillW = 110`, `MoreW = 26`, `SortW = 58`, `DirW = 18`, `SaveW = 18`, `FieldMinW = 60`, `SuggestH = 18`, `FooterButtonW = 120`.
- [ ] Rows from the top: … burden bar, `Gap`, **QueryBar** (`QueryH`), `Gap`, then **SavedRow** (`SavedH` + `Gap`) only when `SavedCount > 0`, then the packs area. `MinHeightFor` adds the same heights, so every side-pack cell still fits.
- [ ] Inside the query bar, from the right: `DirStud`, `SortStud` and `SaveStud`, separated by `StudGap`, each `StudH` tall and centred in the bar. `QueryText` is the rest from the left (`Pad`) to `SaveStud.X - Gap`.
- [ ] `Create(height, input, GearPills? pills = null)`. `GearPills(IReadOnlyList<string> Chips, IReadOnlyList<string> Saved, Func<string, float> Measure)`. A chip's width is `min(MaxPillW, Measure(label) + 2·PillPadX + ChipCloseW)`; a saved stud's is `min(MaxPillW, Measure(label) + 2·PillPadX)`.
- [ ] `FitPills(widths, x, y, room, h)` places pills left to right while each fits, keeping room for a `MoreW` "+N" pill when more follow. It returns the shown rects, the `More` rect (null when every pill fits) and the hidden count. Chips get `room = QueryText.W - FieldMinW - Gap`; saved studs get the saved row's full width.
- [ ] `QueryField` runs from after the last chip or "+N" (plus `PillGap`) to `QueryText`'s right edge. It is never narrower than `FieldMinW`.
- [ ] `Suggestion(i)` for `i < input.Suggestions`: `new DeckRect(QueryText.X, QueryBar.Bottom + 1 + i·SuggestH, QueryText.W, SuggestH)`. These overlay the packs area.
- [ ] `FooterButton`: right-aligned in the footer, `FooterButtonW` × `FooterH`.
- [ ] `GearZone` gains `QueryField, Chip, ChipMore, SaveStud, SortStud, DirStud, SavedFilter, SavedMore, Suggestion, FooterButton`. `HitAt` order: Close, the header studs, **suggestions** (above everything they cover), SaveStud, SortStud, DirStud, Chip i, ChipMore, QueryField, SavedFilter i, SavedMore, FooterButton (only when `SalvageButton`), then plan-1's equipment, contents and side column.
- [ ] All existing geometry tests pass unchanged except those that assert the old `Contents.Y`/`MinHeightFor` numbers. Update those to the new values and name the reason in the commit message.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearDeckGeometryTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (add to `GearDeckGeometryTests`):

```csharp
private static readonly Func<string, float> Six = s => s.Length * 6f;

[Fact]
public void QueryBar_SitsUnderTheBurdenBar()
{
    var g = GearDeckGeometry.Create(620f, new GearDeckLayoutInput(false, false, 2, 30, 7, 0));
    Assert.Equal(g.BurdenBar.Y + GearDeckGeometry.BurdenH + GearDeckGeometry.Gap, g.QueryBar.Y);
    Assert.Equal(GearDeckGeometry.QueryH, g.QueryBar.H);
    Assert.Equal(g.QueryBar.Y + GearDeckGeometry.QueryH + GearDeckGeometry.Gap, g.Contents.Y);
    Assert.Equal(0f, g.SavedRow.H);
}

[Fact]
public void SavedRow_OnlyWithSavedFilters_AndMinHeightGrows()
{
    var none = new GearDeckLayoutInput(false, false, 2, 30, 7, 0);
    var some = none with { SavedCount = 2 };
    var g = GearDeckGeometry.Create(620f, some);
    Assert.Equal(GearDeckGeometry.SavedH, g.SavedRow.H);
    Assert.Equal(g.SavedRow.Y + GearDeckGeometry.SavedH + GearDeckGeometry.Gap, g.Contents.Y);
    Assert.Equal(GearDeckGeometry.MinHeightFor(none) + GearDeckGeometry.SavedH + GearDeckGeometry.Gap,
        GearDeckGeometry.MinHeightFor(some));
}

[Fact]
public void EverySidePackFits_AtMinHeight_WithSavedRow()
{
    var input = new GearDeckLayoutInput(false, false, 2, 30, 7, 0, SavedCount: 1);
    var g = GearDeckGeometry.Create(GearDeckGeometry.MinHeightFor(input), input);
    Assert.Equal(8, g.VisibleSidePacks);
}

[Fact]
public void QueryStuds_RightToLeft()
{
    var g = GearDeckGeometry.Create(620f, new GearDeckLayoutInput(false, false, 0, 0, 7, 0));
    Assert.Equal(GearDeckGeometry.Width - GearDeckGeometry.Pad, g.DirStud.X + g.DirStud.W);
    Assert.Equal(g.DirStud.X - GearDeckGeometry.StudGap, g.SortStud.X + g.SortStud.W);
    Assert.Equal(g.SortStud.X - GearDeckGeometry.StudGap, g.SaveStud.X + g.SaveStud.W);
    Assert.Equal(g.SaveStud.X - GearDeckGeometry.Gap, g.QueryText.X + g.QueryText.W);
}

[Fact]
public void Chips_FitThenMore_FieldKeepsMinimum()
{
    var input = new GearDeckLayoutInput(false, false, 0, 0, 7, 0);
    var pills = new GearPills(["Salvage", "Steel", "Work >= 7", "Iron", "Copper"], [], Six);
    var g = GearDeckGeometry.Create(620f, input, pills);
    Assert.True(g.Chips.Count < 5);
    Assert.NotNull(g.ChipMore);
    Assert.Equal(5 - g.Chips.Count, g.HiddenChips);
    Assert.True(g.QueryField.W >= GearDeckGeometry.FieldMinW);
    Assert.Equal(g.QueryText.X + g.QueryText.W, g.QueryField.X + g.QueryField.W);
    Assert.Equal(new GearDeckHit(GearZone.Chip, 0), g.HitAt(g.Chips[0].X + 2, g.Chips[0].Y + 2));
    Assert.Equal(GearZone.ChipMore, g.HitAt(g.ChipMore!.Value.X + 2, g.ChipMore.Value.Y + 2).Zone);
    Assert.Equal(GearZone.QueryField, g.HitAt(g.QueryField.X + 2, g.QueryField.Y + 2).Zone);
}

[Fact]
public void NoChips_FieldFillsQueryText()
{
    var g = GearDeckGeometry.Create(620f, new GearDeckLayoutInput(false, false, 0, 0, 7, 0));
    Assert.Empty(g.Chips);
    Assert.Null(g.ChipMore);
    Assert.Equal(g.QueryText.X, g.QueryField.X);
}

[Fact]
public void Suggestions_OverlayAndHitFirst()
{
    var g = GearDeckGeometry.Create(620f, new GearDeckLayoutInput(false, false, 0, 30, 7, 0, Suggestions: 3));
    DeckRect s1 = g.Suggestion(1);
    Assert.Equal(g.QueryBar.Y + GearDeckGeometry.QueryH + 1 + GearDeckGeometry.SuggestH, s1.Y);
    Assert.Equal(new GearDeckHit(GearZone.Suggestion, 1), g.HitAt(s1.X + 2, s1.Y + 2));   // over the grid
}

[Fact]
public void FooterButton_OnlyWhenShown()
{
    var off = GearDeckGeometry.Create(620f, new GearDeckLayoutInput(false, false, 0, 0, 7, 0));
    var on = GearDeckGeometry.Create(620f, new GearDeckLayoutInput(false, false, 0, 0, 7, 0, SalvageButton: true));
    DeckRect b = on.FooterButton;
    Assert.Equal(GearDeckGeometry.FooterButtonW, b.W);
    Assert.Equal(GearZone.FooterButton, on.HitAt(b.X + 2, b.Y + 2).Zone);
    Assert.NotEqual(GearZone.FooterButton, off.HitAt(b.X + 2, b.Y + 2).Zone);
}

[Fact]
public void SavedStuds_HitByIndex()
{
    var input = new GearDeckLayoutInput(false, false, 0, 0, 7, 0, SavedCount: 2);
    var g = GearDeckGeometry.Create(620f, input, new GearPills([], ["Steel", "Kits"], Six));
    Assert.Equal(2, g.SavedStuds.Count);
    Assert.Equal(new GearDeckHit(GearZone.SavedFilter, 1), g.HitAt(g.SavedStuds[1].X + 2, g.SavedStuds[1].Y + 2));
}
```

- [ ] **Step 2: Run** → compile errors.

- [ ] **Step 3: Implement.** The additions to `GearDeckGeometry.cs`:

```csharp
internal readonly record struct GearDeckLayoutInput(bool Folded, bool List, int WornCount, int ItemCount, int SidePackSlots, int ScrollRow,
    int SavedCount = 0, int Suggestions = 0, bool SalvageButton = false);

internal enum GearZone
{
    None, Frame, Close, FoldStud, FigureStud, SlotsStud, GridStud, ListStud, EquipSlot, WornCell, Item, SidePack,
    QueryField, Chip, ChipMore, SaveStud, SortStud, DirStud, SavedFilter, SavedMore, Suggestion, FooterButton,
}

/// <summary>The labels the query bar's pills show and how wide a label draws.</summary>
internal sealed record GearPills(IReadOnlyList<string> Chips, IReadOnlyList<string> Saved, Func<string, float> Measure);
```

New constants (with the others):

```csharp
public const float QueryH = 22f, SavedH = 18f, PillH = 18f, PillGap = 4f, PillPadX = 6f, ChipCloseW = 8f, MaxPillW = 110f;
public const float MoreW = 26f, SortW = 58f, DirW = 18f, SaveW = 18f, FieldMinW = 60f, SuggestH = 18f, FooterButtonW = 120f;
```

New properties (all `required … { get; init; }` like the existing ones): `QueryBar`, `QueryText`, `QueryField`, `SaveStud`, `SortStud`, `DirStud`, `SavedRow`, `FooterButton` (`DeckRect`); `Chips`, `SavedStuds` (`IReadOnlyList<DeckRect>`); `ChipMore`, `SavedMore` (`DeckRect?`); `HiddenChips`, `HiddenSaved` (`int`).

`MinHeightFor`: in `top`, after `BurdenH + Gap`, add `QueryH + Gap + (input.SavedCount > 0 ? SavedH + Gap : 0f)`.

In `Create`, after the burden bar (`y += BurdenH + Gap;`):

```csharp
var queryBar = new DeckRect(Pad, y, inner, QueryH);
float qStudY = y + (QueryH - StudH) / 2f;
var dir = new DeckRect(Width - Pad - DirW, qStudY, DirW, StudH);
var sort = new DeckRect(dir.X - StudGap - SortW, qStudY, SortW, StudH);
var save = new DeckRect(sort.X - StudGap - SaveW, qStudY, SaveW, StudH);
var queryText = new DeckRect(Pad, y, MathF.Max(0f, save.X - Gap - Pad), QueryH);
float pillY = y + (QueryH - PillH) / 2f;
float[] chipW = pills is null ? [] : pills.Chips.Select(c => MathF.Min(MaxPillW, pills.Measure(c) + 2 * PillPadX + ChipCloseW)).ToArray();
var (chips, chipMore, hiddenChips) = FitPills(chipW, queryText.X, pillY, queryText.W - FieldMinW - Gap, PillH);
float fieldX = chipMore is { } m ? m.X + m.W + PillGap
    : chips.Length > 0 ? chips[^1].X + chips[^1].W + PillGap : queryText.X;
var queryField = new DeckRect(fieldX, y, queryText.X + queryText.W - fieldX, QueryH);
y += QueryH + Gap;

DeckRect savedRow = new(Pad, y, inner, 0f);
DeckRect[] savedStuds = [];
DeckRect? savedMore = null;
int hiddenSaved = 0;
if (input.SavedCount > 0)
{
    savedRow = savedRow with { H = SavedH };
    float[] savedW = pills is null ? [] : pills.Saved.Select(s => MathF.Min(MaxPillW, pills.Measure(s) + 2 * PillPadX)).ToArray();
    (savedStuds, savedMore, hiddenSaved) = FitPills(savedW, Pad, y, inner, SavedH);
    y += SavedH + Gap;
}
```

And after the footer: `var footerButton = new DeckRect(footer.X + footer.W - FooterButtonW, footer.Y, FooterButtonW, FooterH);`

Add the helpers:

```csharp
/// <summary>Pills left to right while each fits, keeping room for a "+N" pill when more follow.</summary>
internal static (DeckRect[] Rects, DeckRect? More, int Hidden) FitPills(IReadOnlyList<float> widths, float x, float y, float room, float h)
{
    var rects = new List<DeckRect>();
    float used = 0f;
    int i = 0;
    for (; i < widths.Count; i++)
    {
        bool last = i == widths.Count - 1;
        float need = widths[i] + (last ? 0f : PillGap + MoreW);
        if (used + need > room) break;
        rects.Add(new DeckRect(x + used, y, widths[i], h));
        used += widths[i] + PillGap;
    }
    int hidden = widths.Count - i;
    return (rects.ToArray(), hidden > 0 ? new DeckRect(x + used, y, MoreW, h) : null, hidden);
}

public DeckRect Suggestion(int index) =>
    new(QueryText.X, QueryBar.Y + QueryH + 1f + index * SuggestH, QueryText.W, SuggestH);
```

In `HitAt`, after the Grid/List studs and before `Equipment`:

```csharp
for (int i = 0; i < Input.Suggestions; i++)
    if (Suggestion(i).Contains(x, y)) return new GearDeckHit(GearZone.Suggestion, i);
if (SaveStud.Contains(x, y)) return new GearDeckHit(GearZone.SaveStud);
if (SortStud.Contains(x, y)) return new GearDeckHit(GearZone.SortStud);
if (DirStud.Contains(x, y)) return new GearDeckHit(GearZone.DirStud);
for (int i = 0; i < Chips.Count; i++)
    if (Chips[i].Contains(x, y)) return new GearDeckHit(GearZone.Chip, i);
if (ChipMore is { } cm && cm.Contains(x, y)) return new GearDeckHit(GearZone.ChipMore);
if (QueryField.Contains(x, y)) return new GearDeckHit(GearZone.QueryField);
for (int i = 0; i < SavedStuds.Count; i++)
    if (SavedStuds[i].Contains(x, y)) return new GearDeckHit(GearZone.SavedFilter, i);
if (SavedMore is { } sm && sm.Contains(x, y)) return new GearDeckHit(GearZone.SavedMore);
if (Input.SalvageButton && FooterButton.Contains(x, y)) return new GearDeckHit(GearZone.FooterButton);
```

- [ ] **Step 4: Run** → fix the plan-1 tests that pinned the old `Contents.Y` / `MinHeightFor` numbers (expected: the query row shifts them by `QueryH + Gap = 28`). Run again → all pass.
- [ ] **Step 5: Commit:** "Gear Deck: geometry for the query bar, saved filters, suggestions and footer button" (the body names the shifted plan-1 numbers).

---

### Task 6: Query state, results view and the move of input onto `Shown`

**Goal:** The deck holds a live `GearQuery`. A non-empty query shows the matches from every pack as one flat grid or list with pack badges. Everything plan 1 did on `Items` (draw, hit, drag, drop, double-click, tooltip) now works on `Shown`, which is the pack's rows or the results.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Input.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/UiGearDeckResultsTests.cs` (create); existing `UiGearDeckInputTests` and `UiGearDeckDrawTests` must still pass

**Acceptance Criteria:**
- [ ] `UiGearDeck` gains `internal GearQuery Query`, `internal void SetQuery(GearQuery q)` (it resets the scroll and bumps a query version), `internal bool ShowsResults => !Query.IsEmpty`, and `internal IReadOnlyList<GearItemView> Shown`. `Shown` is `GearQueryEngine.Run(projection.All, Query)` when results are shown, else `projection.ForPack(SelectedPackId)`. It is cached and rebuilt only when the model or the query version changes. `GearProjection` is rebuilt with the model in `Refresh`, using `_b.DisplayName`.
- [ ] `LayoutInput().ItemCount` is `Shown.Count`. `ContentRows`, `MaxScrollRow` and the scroll clamp use `Shown`.
- [ ] Drawing, hit tests, `DragItem`, `DropTargetAt`, `DropReleased`, `DoubleClick` and `TooltipFor` index `Shown`. When results are **not** shown, behaviour is the same as plan 1, and every existing `UiGearDeckInputTests` / `UiGearDeckDrawTests` case passes unchanged.
- [ ] While results are shown:
  - a drop on a result row: a worn row → `DropOnSlot(row's CurrentlyEquippedLocation)`; an `IsPack` row → `DropOnPack(row.Id)`; else `DropOnCell(item, row.PackId, row.Id, row.Slot, fromGround)`
  - a drop on empty contents space is **not** a target (no highlight, `DropReleased` returns false)
  - double-click: a worn row → `TakeOff`; else `Activate`
  - drag: a worn row lifts as `ItemDragSource.Equipment`, others as `Inventory`
- [ ] Grid results draw a pack badge top-left in each cell ("M" for the main pack, "2"…"8" for side packs, "W" for worn). List results draw: icon, name, up to two value columns (each 44 points, right-aligned), then the badge. The value columns are the sort field when it is not Name, then the first range chip's field (`GearChipText.SortFieldOf`) when that differs from the sort.
- [ ] The side column while results are shown: clicking a pack cell adds a `GearChip.InPack(id)` or removes it when present. It sends no `OpenPack`. Pack cells with a Pack chip are outlined in `Handle`, and no outline means every pack. When results are not shown, side-pack clicks behave as in plan 1.
- [ ] Footer: with results shown, the left side draws "`n` of `PackItemCount` match". The right-side pack text is drawn only when results are not shown.
- [ ] `Session.Ended` clears the query (subscribe in the constructor).
- [ ] Draw probe: nothing is drawn outside the frame in results grid and results list, with and without fonts.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiGearDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** in `UiGearDeckResultsTests`. Copy the `Fake`, `Place`, `Wear`, `Rig`, `Make`, `Send`, `Click`, `DoubleClick` and `Centre` helpers from `UiGearDeckInputTests`. Change its `World` so that the main pack holds two steel salvage bags (`Type = ItemType.TinkeringMaterial, MaterialType = 64`, workmanships 7.5 and 6.0) plus three `Misc` items, the bag holds one steel salvage (workmanship 9.0) and one `Food`, and a steel helm is worn (`Type = ItemType.Armor, MaterialType = 64`). Tests:
  - `SetQuery(OfMaterial(64))` shows the three salvage bags (not the helm), and `ShowsResults` is true
  - with `Sort = new GearSort(Workmanship, true)`, `Shown` ids are 9.0, 7.5, 6.0
  - `SetQuery(Empty)` restores `Shown` to the selected pack's rows in slot order
  - while results are shown, clicking side pack 1 (the bag) adds `InPack(Bag)` and records no `open`; clicking it again removes the chip
  - a double-click on the bag's result row records `use <id>`
  - a `DropReleased` with an `ItemDragPayload` on the bag's result row records a `put` into the bag at that row's slot (or a `merge` when same-kind; use a `Misc` source so it is a `put`)
  - a drop on the empty contents area below the results returns false and records nothing
  - `SetQuery(WornOnly)` + a double-click on the helm row records the take-off `put`
  - `Session.EndSession()` clears the query
  - draw probe in results grid and results list (copy the probe setup from `UiGearDeckDrawTests`)
- [ ] **Step 2: Run** → compile errors.
- [ ] **Step 3: Implement in `UiGearDeck.cs`:**
  - fields `GearProjection _projection = GearProjection.Empty`, `GearQuery _query = GearQuery.Empty`, `int _queryVersion`, `IReadOnlyList<GearItemView> _shown = []`, `(ulong Revision, int QueryVersion, uint Pack)? _shownFor`
  - in `Refresh`'s gated rebuild: `_projection = GearProjection.Build(_model, table, _b.DisplayName);`
  - `Shown` getter: recompute when `_shownFor != (_lastRevision, _queryVersion, SelectedPackId)`
  - `SetQuery(GearQuery q)`: when `q.Equals(_query)`, return; else set it, `_queryVersion++`, `ScrollTo(0)`
  - in the constructor: `_b.Session.Ended += () => SetQuery(GearQuery.Empty);`
  - `LayoutInput()` uses `Shown.Count`; `ContentRows` uses `Shown.Count`
  - a `PackBadge(GearItemView row)` helper: `row.Worn ? "W" : _badges[row.PackIndex]`, guarded by the index range
- [ ] **Step 4: Move input onto `Shown`** in `UiGearDeck.Input.cs`. Replace `Items[hit.Index]` / `Items.Count` with `Shown[hit.Index]` / `Shown.Count` in `DragItem`, `DropTargetAt`, `DoubleClick` and `TooltipFor`. Build `DropReleased`'s `GearZone.Item` case from the row:

```csharp
case GearZone.Item:
{
    GearItemView row = Shown[hit.Index];
    if (row.Worn) { action = nameof(GearDropDispatcher.DropOnSlot); Dispatcher.DropOnSlot(p.ObjId, WornMask(row.Id)); }
    else if (row.IsPack) { action = nameof(GearDropDispatcher.DropOnPack); Dispatcher.DropOnPack(p.ObjId, row.Id, fromGround); }
    else { action = nameof(GearDropDispatcher.DropOnCell); Dispatcher.DropOnCell(p.ObjId, row.PackId, row.Id, row.Slot, fromGround); }
    break;
}
```

  Here `WornMask(id)` is `_b.Objects().Get(id)?.CurrentlyEquippedLocation ?? EquipMask.None`. In normal mode `row.PackId == SelectedPackId` and `row.Slot == hit.Index`, so plan-1 behaviour is kept. `DropTargetAt`'s `GearZone.Frame when _g.Contents.Contains(x, y)` case only applies when `!ShowsResults`. `DoubleClick` on a worn row calls `Dispatcher.TakeOff(row.Id)`. `SidePack` clicks go to `TogglePackChip(pack)` when `ShowsResults`:

```csharp
private void TogglePackChip(GearPack pack)
{
    int i = -1;
    for (int c = 0; c < _query.Chips.Count; c++)
        if (_query.Chips[c] is { Field: GearField.Pack } chip && chip.Id == pack.Id) { i = c; break; }
    SetQuery(i >= 0 ? _query.Without(i) : _query.With(GearChip.InPack(pack.Id)));
}
```

- [ ] **Step 5: Draw** in `UiGearDeck.Draw.cs`. `DrawGrid` and `DrawList` iterate `Shown` and look up each `ClientObject` with `objects.Get(row.Id)` for the icon (skip a row whose object is gone). In results mode, draw the badge in `Small` at the cell's top-left (`r.X + 2, r.Y + 1`) in `LabelInk`. The list's value columns: compute `(GearSortField? A, GearSortField? B)` once per query version; draw each column right-aligned in a 44-point slot before the badge slot (16 points), and fit the name into what is left. Cache column texts per `(row.Id, field)` and clear them with `_fittedNames`. `DrawSideColumn` outlines the packs with a Pack chip when results are shown, else the selected pack. Footer: left text = `$"{Shown.Count} of {_projection.PackItemCount} match"` (cached per query version and revision) when `ShowsResults`.
- [ ] **Step 6: Run** the Verify command → all pass. **Commit:** "Gear Deck: query results across all packs".

---

### Task 7: The query bar: field, chips and suggestions

**Goal:** The player types in the query bar, picks suggested chips, and removes chips. The results follow as they type.

**Files:**
- Create: `src/AcDream.App/UI/Hud/UiGearDeck.Query.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Menu.cs`
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Input.cs`, `src/AcDream.App/UI/UiField.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs` (pass `Keyboard: Host.Keyboard` into `GearDeckBindings`)
- Test: `tests/AcDream.App.Tests/UI/Hud/UiGearDeckQueryTests.cs` (create), `tests/AcDream.App.Tests/UI/UiFieldTests.cs` (add a case; if the file has another name, find it with `grep -rln "new UiField" tests/AcDream.App.Tests/UI`)

**Acceptance Criteria:**
- [ ] `UiField.OnBackspaceWhenEmpty` (`Action?`): on a Backspace **key-down** (not auto-repeat) with empty text and no selection, it is invoked and the key is consumed. With text present, Backspace behaves as before.
- [ ] `GearDeckBindings` gains trailing optional `Silk.NET.Input.IKeyboard? Keyboard = null`. The deck owns a `UiField QueryInput` (OneLine, Selectable, `ClearOnSubmit = false`, `RecordHistory = false`, `MaxCharacters = 40`, the deck's small font via `_fonts?.Face(...)` as the Chat Deck does). It is placed at `Geometry.QueryField` with the `Place` pattern (`Anchors = None`), hidden while the model has no packs, and drawn on a `ChatDeckPalette.FieldFill`-style dark fill with placeholder text "Filter…" (`LabelInk`) when it is empty and unfocused.
- [ ] Typing updates `Query.Text` live (via `OnTextChanged`, trimmed). The suggestions are `GearChipSuggester.Suggest(text)` and appear under the bar as `Suggestion(i)` rows while the field has focus and the text is not blank. The first row is highlighted.
- [ ] **Enter** turns the first suggestion into a chip, clears the text and keeps focus. **Esc** clears the text and the suggestions, and releases focus (UiField's own Escape). **Backspace on an empty field** removes the last chip. A click on a suggestion row adds that chip and clears the text. Up and Down move the highlighted suggestion, and Enter takes the highlighted one.
- [ ] Chips draw as pills (`HeaderFill` fill, `CellEdge` stroke, `PillH` tall, `Bold` label fitted with `Fit` to the pill, a small × drawn as two lines in the right `ChipCloseW`). A click anywhere on a chip removes it. The "+N" pill opens the deck menu (see the next criterion) listing the hidden chips' labels; choosing one removes it.
- [ ] `UiGearDeck.Menu.cs` holds the one deck menu: `UiMarkupContextMenu _menu` (`ThemePalette = ChatDeckPalette.Menu`), added as a child and set as the deck's `ContextMenu`, exposed as `internal UiMarkupContextMenu Menu` for tests (like `UiChatDeck.LineMenu`). A `_menuKind` enum (`None, HiddenChips, Sort, SavedFilter, SavedMore, Item`; Tasks 8 and 9 fill in the later kinds) is chosen in `Opening(x, y)` from `_g.HitAt(x, y)` for right-clicks. A left-click opener calls `_menu.TryOpen` at the screen point `ScreenPosition + (stud.X + 1, stud.Y + stud.H - 1)`, which is inside the stud, so `Opening` picks the same kind. Unhandled kinds give `[]` (no menu).
- [ ] The geometry gets `GearPills` built from the chip labels (`GearChipText.Label` with a pack-name lookup: "Main" / "Pack N" / "Pack ?") and the saved names. It is measured with `Bold.Font?.MeasureWidth(t) ?? t.Length * 6f` and rebuilt when the chips or the saved names change (track a pills version beside the layout input). `LayoutInput().Suggestions` is the visible suggestion count.
- [ ] Clicking a pack cell while results are shown still toggles Pack chips, and the chips show as "Main" / "Pack N".
- [ ] `RetailUiRuntime.MountGearDeck` passes `Keyboard: Host.Keyboard`.
- [ ] Draw probe: nothing is drawn outside the frame with three chips, with overflow, and with suggestions open.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiGearDeck|FullyQualifiedName~UiFieldTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.**
  - The UiField case: create a focused `UiField`, set `OnBackspaceWhenEmpty`, and send a Backspace key-down the way the existing UiField key tests do (find them with `grep -n "Key.Backspace" tests/AcDream.App.Tests -r`). The hook fires once. With text "ab" it does not fire and the text becomes "a".
  - `UiGearDeckQueryTests` (the rig from Task 6, and a real `UiRoot` parent when focus is needed; copy how `UiChatDeck` tests focus a field):
    - setting `QueryInput` text to "steel" via `SetText` + the `OnTextChanged` path gives `Query.Text == "steel"` and a first suggestion of `OfMaterial(64)`
    - submitting (call the field's `OnSubmit` with the text, as Enter does) adds the `OfMaterial(64)` chip and clears the text
    - the Backspace-when-empty hook removes the last chip
    - a click on `Suggestion(1)` adds that suggestion
    - a click on `Chips[0]` removes chip 0
    - `Geometry.Chips.Count` follows the chips, with `ChipMore` set when there are six long chips
    - the draw probe cases from the AC above
- [ ] **Step 2: Run** → fail.
- [ ] **Step 3: Implement `OnBackspaceWhenEmpty`** in `UiField.cs`, in the key-down switch (`case Silk.NET.Input.Key.Backspace:` near line 955), *not* in the auto-repeat `OnTick` path:

```csharp
case Silk.NET.Input.Key.Backspace:
    if (_text.Length == 0 && OnBackspaceWhenEmpty is { } onEmpty) { onEmpty(); return true; }
    Backspace(); StartRepeat(key); return true;
```

  Add the property beside `OnEscape`, with a one-line doc: `/// <summary>Backspace pressed on an empty field (the owner may remove a chip); never on auto-repeat.</summary>`.
- [ ] **Step 4: Implement `UiGearDeck.Query.cs`:** the field's creation (called from the constructor after the plan-1 setup), `OnTextChanged` → `SetQuery(_query with { Text = text.Trim() })` plus a suggestion refresh, `OnSubmit` → `TakeSuggestion(_suggestIndex)`, `OnEscape` → clear the text and suggestions, `OnBackspaceWhenEmpty` → `SetQuery(_query.Without(_query.Chips.Count - 1))`, and `OnUp`/`OnDown` → move `_suggestIndex`. `TakeSuggestion(i)` adds `_suggestions[i]` with `_query.With(...)` and **sets `Text = ""` in the same `SetQuery` call**, then `QueryInput.SetText("")`. Then keep focus with `FindRoot()?.SetKeyboardFocus(QueryInput)`, because Enter's default releases focus unless `StayFocusedAfterSubmit` returns true — set `StayFocusedAfterSubmit = () => true`. Place the field from `SyncGeometry` (after the geometry is rebuilt) through a `Place` helper like `UiChatDeck.Place`.
- [ ] **Step 5: Hit handling** in `UiGearDeck.Input.cs` `Click`: `GearZone.Chip` → remove, `GearZone.Suggestion` → `TakeSuggestion(index)`, `GearZone.ChipMore` → open the hidden-chip menu, `GearZone.QueryField` → focus the field (normally the field child takes the click itself). `TooltipFor`: a chip shows "Click to remove".
- [ ] **Step 6: Draw** the query bar (`HeaderFill` rounded rect over `QueryBar`), the chips, "+N", the placeholder, and the suggestions overlay. Draw the overlay in `OnDrawOverlay` before the tooltip, so it sits above the grid: a `CellFill` panel with a `CellEdge` stroke, each row's `GearChipText.Suggestion` in `Small`, and `MenuHighlight` behind the highlighted row.
- [ ] **Step 7: Wire `Keyboard: Host.Keyboard`** in `MountGearDeck`.
- [ ] **Step 8: Run** → all pass. **Commit:** "Gear Deck: query bar with chips and suggestions".

---

### Task 8: Sort, direction and saved filters

**Goal:** The player picks the sort field and flips its direction, saves the current query under a name with ★, applies a saved filter with one click, and deletes saved filters from a right-click menu.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.Menu.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Query.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Input.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/UiGearDeckSavedFilterTests.cs` (create)

**Acceptance Criteria:**
- [ ] The Task 7 deck menu gains the Sort, SavedFilter and SavedMore kinds.
- [ ] The sort stud draws `GearChipText.SortLabel(Query.Sort.Field)` and opens a menu: Name, Type, Material, Work, Value, Burden, Stack (the current one first-marked with "* "). Choosing one sets `Sort = GearSort.DefaultFor(field)`. The direction stud draws an up (ascending) or down (descending) chevron and flips `Descending`. Both studs are drawn dim, but stay clickable, while results are not shown, because the sort applies to results only.
- [ ] ★ (`SaveStud`, a five-point star drawn as five `DrawLine` strokes joining every second point of a pentagon of radius 5 centred on the stud) is dim and does nothing while the query is empty. Otherwise it shows an inline `UiField SaveNameInput` over `QueryText`, with the text pre-filled from the chip labels joined by " " (cut to `SavedFilter.MaxNameLength`) and selected. Enter saves through `Session.Update(s => s.WithSaved(new SavedFilter(name, Query)))`. Esc or focus loss cancels. It uses the same pattern as `UiChatDeck.TabEdit.cs`.
- [ ] The saved row draws one stud per saved filter (`DeckChrome.DrawStud`, lit when `Query.Equals(filter.Query)`), with the name fitted. A click applies the filter (`SetQuery(filter.Query)` and clears the field text), or clears the query when that filter is already lit. Right-click → menu "Delete" → `Session.Update(s => s.WithoutSaved(name))`. "+N" opens a menu of the hidden filters' names, where choosing one applies it.
- [ ] `LayoutInput().SavedCount` is `Session.Settings.SavedFilters.Count`, so adding the first filter grows the window's minimum (the plan-1 `FitMinHeight` already grows the window).
- [ ] Draw probe: nothing is drawn outside the frame with saved filters, with overflow, and with the name field open.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiGearDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (Task 6 rig):
  - choosing "Work" from the sort menu (open it with a click on the sort stud, then call `deck.Menu.Selected!(index)`) gives `Sort == new GearSort(Workmanship, true)`; a click on the direction stud flips it
  - ★ with an empty query does nothing (`SaveNameInput.Visible` stays false)
  - ★ with `OfMaterial(64)` opens the name field pre-filled "Steel"; submitting "Best steel" adds one saved filter; the saved row appears (`Geometry.SavedStuds.Count == 1`); the window's `MinHeight` grew by `SavedH + Gap`
  - `SetQuery(Empty)`, then a click on the saved stud applies that query; a second click clears it
  - right-click on the saved stud lists `["Delete"]`; choosing it removes the filter
  - saving a second filter with the same name (any case) replaces the first
  - draw probes
- [ ] **Step 2: Run** → fail.
- [ ] **Step 3: Implement** the new menu kinds in `UiGearDeck.Menu.cs` (`ItemsSource`/`Selected` per kind; `ItemsEnabledSource` all true for now; the Item kind returns `[]` until Task 9), the sort and direction clicks, the ★ name field, the saved-stud clicks and the drawing. Add a `DrawChevron` up variant: extend the plan-1 helper to take a direction enum (`Down, Right, Up`) instead of `bool down`, and update its two call sites.
- [ ] **Step 4: Run** → all pass. **Commit:** "Gear Deck: sort, direction and saved filters".

---

### Task 9: Multi-select and the item menu

**Goal:** The player selects items (click, Ctrl/Cmd-click, Shift-click) in the pack view and in the results. A right-click menu offers Show in pack, Select all and Clear selection.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Input.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Menu.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/UiGearDeckSelectionTests.cs` (create)

**Acceptance Criteria:**
- [ ] `GearDeckBindings` gains trailing optional `Func<(bool Toggle, bool Range)>? Modifiers = null`. When null, the deck reads `FindRoot()`: `Toggle = root.CopyModifierHeld` (Ctrl, or Cmd on macOS) and `Range = root.ShiftHeld`.
- [ ] `internal IReadOnlyCollection<uint> Selection` (ids) and an anchor index. A plain click on an item selects only it and sets the anchor. Toggle-click adds or removes it and sets the anchor. Range-click selects `Shown[min(anchor, i)..max(anchor, i)]`, replacing the selection. A press on empty contents space clears the selection.
- [ ] The selection is pruned to ids still in `Shown` whenever `Shown` is rebuilt. It is cleared by `SetQuery` only when the shown list no longer contains the ids (pruning covers it), and by a pack change in the normal view.
- [ ] Selected cells draw a `Handle`-coloured 1-point stroke over a `MenuHighlight` tint. Selected list rows draw a `MenuHighlight` fill (hover stays a thinner outline).
- [ ] A right-click on an item (`_menuKind = Item`): when it is not already selected, it becomes the only selection first. The menu items, in order: "Show in pack" (enabled only for a single non-worn, non-`IsPack` result while results are shown), "-", "Select all", "Clear selection" (enabled when something is selected). Task 10 puts "Add N to salvage" first.
- [ ] Show in pack: it saves the row, clears the query (chips and field text), selects that pack (plan-1 `SelectPack`, which opens a side pack on the server), scrolls so the row is visible, and selects it.
- [ ] The footer's left text appends " · `k` selected" when `k > 0` (in both modes; in the normal view it is the only left text).
- [ ] Drag is unchanged: it lifts the item under the pointer, selected or not (group drag is plan 4).

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiGearDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (Task 6 rig with a settable `Modifiers` func):
  - click item 2 → `Selection == {id2}`; toggle-click item 4 → `{id2, id4}`; toggle-click item 2 → `{id4}`
  - click item 1, then range-click item 3 → `{id1, id2, id3}`
  - a `MouseDown` on empty contents space clears it
  - `SetQuery(OfMaterial(64))` prunes a selection of misc items to empty
  - right-click on an unselected item selects it; the menu lists `["Show in pack", "-", "Select all", "Clear selection"]`
  - Show in pack on the bag's steel result: query empty, selected pack = Bag, `open B1` recorded, `Selection == {that id}`, and its row index within `[ScrollRow, ScrollRow + visible)`
  - "Select all" selects every `Shown` id
  - the footer text contains "2 selected" after two toggles
- [ ] **Step 2: Run** → fail.
- [ ] **Step 3: Implement.** Click on `GearZone.Item` in `Click(...)` now updates the selection (plan 1 did nothing there; double-click still activates). Clear the selection in the `MouseDown` case when the press hit is `GearZone.Frame` inside `_g.Contents`. The root always sends `MouseDown` to the deck, but empty frame space is not a control (`IsControl` stays false), so the press there still drags the window, as plan 1 intends, and no `Click` can be relied on.
- [ ] **Step 4: Run** → all pass. **Commit:** "Gear Deck: multi-select and item menu".

---

### Task 10: "Add N to salvage"

**Goal:** While the retail salvage window is open, the footer shows "Add N to salvage" (N = the selection, or every shown item when nothing is selected, counting only what the salvage panel would take), and the item menu offers the same.

**Files:**
- Create: `src/AcDream.App/UI/Hud/GearSalvageHandOff.cs`
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Input.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Menu.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/GearSalvageHandOffTests.cs` (create), `tests/AcDream.App.Tests/UI/Hud/GearDeckWiringTests.cs` (add a case)

**Acceptance Criteria:**
- [ ] `IGearSalvage { bool IsOpen { get; } int Revision { get; } bool CanAdd(uint id); bool Add(uint id); void Report(string text); }`. `RuntimeGearSalvage(Func<SalvageUiController?> controller, Func<bool> windowVisible, Action<string> report)`: `IsOpen` = window visible and `controller()?.ToolId != 0`. `Revision` = a hash of `(ToolId, ItemCount)` that changes as items go in. `CanAdd`/`Add` forward to the controller (false when it is null).
- [ ] `GearSalvageHandOff.Candidates(IReadOnlyList<GearItemView> rows, IGearSalvage salvage)` keeps rows that are not `Worn`, not `IsPack`, and `salvage.CanAdd(row.Id)`, in order. This is the one place plan 4 adds the lock skip.
- [ ] `GearSalvageHandOff.AddAll(candidates, salvage)` adds each one in order through `Add` and counts the successes. `Add` re-checks `CanAdd`, so after the first item fixes the material, items of another material are refused. When `added < candidates.Count`, it reports one chat line: "Added `added` of `n` to salvage." When `added == 0`: "None of those can be salvaged with that tool."
- [ ] `GearDeckBindings` gains trailing optional `IGearSalvage? Salvage = null`. The deck's targets are the selected rows (in `Shown` order) when there is a selection, else all of `Shown`. N is `Candidates(targets).Count`, cached per `(revision, query version, selection version, salvage.Revision)`. `LayoutInput().SalvageButton` is `Salvage?.IsOpen == true && N > 0`.
- [ ] The footer button draws as a lit stud with "Add N to salvage" (fitted). A click runs `AddAll`. The item menu's first entry, shown only while salvage is open, is "Add N to salvage" (then "-"), enabled when N > 0.
- [ ] `MountGearDeck` passes `Salvage: new Hud.RuntimeGearSalvage(() => SalvageController, () => Host.IsWindowVisible(WindowNames.Salvage), _bindings.Options.DisplaySystemMessage)`. The lazy `SalvageController` read is needed because `MountSalvage` runs after `MountGearDeck`.
- [ ] The retail salvage window and its controller are unchanged.
- [ ] The full Mac suite passes.

**Verify:** `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → 0 failures

**Steps:**

- [ ] **Step 1: Write the failing tests.** `GearSalvageHandOffTests` with a fake `IGearSalvage` (a set of addable ids; `Add` removes from it and records; `IsOpen` settable; `Revision` increments on `Add`):
  - `Candidates` drops worn, `IsPack` and not-addable rows and keeps the order
  - `AddAll` with 3 candidates where the fake refuses the 2nd on `Add` reports "Added 2 of 3 to salvage."
  - with all refused it reports "None of those can be salvaged with that tool."
  - deck: with `Salvage.IsOpen` false there is no footer button (`Geometry.Input.SalvageButton` false)
  - deck: with it open and `OfType(Salvage)` → 3 results, the button reads "Add 3 to salvage"; select one → "Add 1 to salvage"; a click adds exactly that id
  - deck: the item menu while salvage is open starts with "Add 1 to salvage", "-"
  - wiring (`GearDeckWiringTests`): `RuntimeGearSalvage` with a null controller is not open and `CanAdd` is false
- [ ] **Step 2: Run** → fail.
- [ ] **Step 3: Implement** `GearSalvageHandOff.cs` (the interface, `RuntimeGearSalvage`, and the static `Candidates`/`AddAll`), then the deck's targets, the cached N, the footer button and the menu entry. Then the runtime wiring.
- [ ] **Step 4: Run the full suite** → 0 failures. **Commit:** "Gear Deck: Add N to salvage".

---

### Task 11: Live check (user)

**Goal:** The user confirms the query bar works in the real client against the local ACE server.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:** none (fixes found here get their own commits)

**Acceptance Criteria:**
- [ ] Ask the user before launching. Use the local ACE server (10.10.20.20 through `~/OpenAC-dev/dev-client.env`), never Dreamweave.
- [ ] Typing "steel", Enter, then "salv", Enter, and sorting by Work with the direction down shows steel salvage from every pack, best workmanship first, with pack badges. Items in side packs that were never opened this session are present. If they are not, note it: side-pack contents may need an open first.
- [ ] Chips are removed by a click and by Backspace on the empty field. Esc clears the text.
- [ ] Clicking pack cells while results are shown narrows to those packs.
- [ ] ★ saves a filter, the saved stud applies and clears it, the filter survives a relog, and Delete removes it.
- [ ] Click, Ctrl/Cmd-click and Shift-click select. Show in pack jumps to the item.
- [ ] With an Ust used on the retail salvage window, "Add N to salvage" fills the retail salvage panel with the shown (or selected) items. The retail Salvage button then works as before.
- [ ] Drag and drop from the results into a pack or onto a slot still works.
- [ ] The user says it passes.

**Verify:** the user's confirmation in the conversation, plus client log lines with `[gear-deck]`.

---

## Self-review notes

- **Spec coverage (Delivery item 2):**
  - type groups → T1
  - query model, engine and suggester → T2, T3
  - query bar, chips and sort → T5, T7, T8
  - flat results → T6
  - saved filters → T4, T8
  - multi-select and the right-click menu → T9
  - "Add N to salvage" → T10
  - Spec items that wait for plan 4 (named in the scope notes): the Locked chip and padlock, Split…, Lock/Unlock, Combine like stacks, and group drag.
  - Spec item replaced: the "All" side cell becomes Pack-chip toggles on the pack cells (scope note).
- **Types across tasks:** `MaterialNames`, `GearTypeGroup`/`GearTypeGroups`, `GearItemView`, `GearProjection` (T1); `GearField`, `GearChip`, `GearSortField`, `GearSort`, `GearQuery`, `SavedFilter`, `GearQueryEngine` (T2); `GearChipSuggester`, `GearChipText` (T3); `GearQueryJson`, `GearDeckSettings.SavedFilters`/`WithSaved`/`WithoutSaved`, `GearDeckSession.Ended` (T4); `GearDeckLayoutInput.SavedCount`/`Suggestions`/`SalvageButton`, `GearPills`, the new `GearZone` members, and `GearDeckGeometry.QueryBar`/`QueryText`/`QueryField`/`Chips`/`ChipMore`/`HiddenChips`/`SaveStud`/`SortStud`/`DirStud`/`SavedRow`/`SavedStuds`/`SavedMore`/`HiddenSaved`/`Suggestion(i)`/`FooterButton`/`FitPills` (T5); `UiGearDeck.Query`/`SetQuery`/`ShowsResults`/`Shown` (T6); `UiField.OnBackspaceWhenEmpty`, `GearDeckBindings.Keyboard`, `UiGearDeck.QueryInput` (T7); `UiGearDeck.SaveNameInput`, the deck menu and its kinds (T8); `GearDeckBindings.Modifiers`, `UiGearDeck.Selection` (T9); `IGearSalvage`, `RuntimeGearSalvage`, `GearSalvageHandOff`, `GearDeckBindings.Salvage` (T10).
- `GearDeckBindings` gains `Keyboard` (T7), `Modifiers` (T9) and `Salvage` (T10) as trailing optional parameters after `Log`, so the plan-1 tests and the runtime call compile at every step.
