# Gear Deck Plan 1: Gear Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the Gear Deck window with Modern HUD on: one 300-point column with a fold-down Equipment section (Slots view and folded row), the packs as a grid or list with the side packs down the right edge, burden, drag-and-drop moves, double-click wear/use/take-off, opened by the existing inventory key and toolbar button.

**Architecture:** Pure Core model (`GearInventoryModel`, `GearSlotCatalog`) projects `ClientObjectTable` into packs, contents and worn slots. A pure `GearDeckGeometry` lays out the column. `UiGearDeck` (native, `DeckChrome` look) draws and handles input. Moves go through the existing `RuntimeItemInteraction` and Core placement policies behind a small `IGearItemActions` seam. `ModernHudSwitch` gains *on-demand* replacements (shown only when the player opens them) and the runtime's `ToggleWindow`/`ShowWindow`/`HideWindow`/`IsWindowVisible` redirect `WindowNames.Inventory` to the Gear Deck while Modern HUD is on.

**Tech Stack:** C# / .NET 10, xUnit, the retained UI (`UiElement`, `UiRenderContext`), `DeckChrome`, `DeckFonts`.

**Spec:** `docs/superpowers/specs/2026-10-28-gear-deck-design.md`

**Worktree:** implement in `~/code/OpenAC/OpenAC-gear-deck` on branch `feat/gear-deck-core`, made from `docs/gear-deck-spec` (the Verify commands use that path).

## Global Constraints

- Width is fixed at **300 points**. Height is resizable vertically only: min 360, max the parent's height. There is no deck scale; crisp text on Retina comes from `ctx.PixelScale`, as in the Chat Deck.
- The look is `DeckChrome` palette + `DeckFonts` only, never the plugin theme.
- Retail Inventory/Paperdoll code stays behaviourally unchanged. With Modern HUD off, nothing about retail changes.
- No new network messages. Every move goes through `RuntimeItemInteraction` or the Core placement policies (`InventoryContainerPlacementPolicy`, `InventoryPlacementSearch`, `ItemListDropPlacement`).
- Children placed by deck code need `Anchors = None` (the draw-time anchor pass otherwise snaps them back; see Chat Deck note in memory).
- Tests run through `~/code/OpenAC/test-baseline/run-mac-tests.sh`, narrowed with `MAC_TEST_FILTER` and never with a second `--filter`. Never run tests that are known to fail.
- Commit messages end with `Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs`.
- Fork-only: no upstream PR, no plugin contract change.

**User decisions (already made):**
- "C": one 300-point column; the Equipment section folds to a row of worn items, which stay drop targets.
- Vertical like retail, never wide.
- Equipment: Figure (3D, plan 3) or Slots view; hovering a slot highlights matching pack items (plan 3); double-click wears or takes off.
- Packs: Grid and List toggle, remembered per character.
- Public data only (no ID requests).
- Salvage/tinkering stay in the retail windows (plan 2 adds "Add N to salvage").
- Outside containers: a separate Loot window later; the retail container window is unchanged until then.
- Modern HUD windows look like the decks, never the plugin theme.

**Plan-1 scope notes:**
- Figure view is plan 3. In plan 1 the Figure stud is drawn dimmed and does nothing; Slots is the only expanded view.
- The query bar is plan 2. Plan 1 leaves no gap for it; plan 2 adds the row to the geometry.
- Drags move the whole stack. Split by amount is plan 4. The retail split-quantity slider is not consulted.
- Window visibility is *not* restored at login: the deck starts closed, like the other decks. `WindowNames.GearDeck` goes in the `stateManagedVisibilityWindows` list.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.App/UI/Hud/ModernHudSwitch.cs` (modify) | `OnDemand` replacements: open/close state, `RedirectTarget`, convert a retail show into a deck show |
| `src/AcDream.Core/Items/Gear/GearInventoryModel.cs` (create) | Pure projection: packs (main + side, capacities), pack contents in slot order, worn items, burden |
| `src/AcDream.Core/Items/Gear/GearSlotCatalog.cs` (create) | The 24 equipment slots (mask, short label, Aetheria flag) and each slot's occupant |
| `src/AcDream.App/UI/Hud/GearDeckGeometry.cs` (create) | Pure layout of the column at a height and state, and hit-testing |
| `src/AcDream.App/UI/Hud/GearDeckSettings.cs` (create) | Per-character view state + JSON (de)serialisation |
| `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs` (modify) | `LoadGearDeck` / `SaveGearDeck` |
| `src/AcDream.App/UI/Hud/GearDeckSession.cs` (create) | Loads and saves settings for the active character, 0.5 s coalesced saves |
| `src/AcDream.App/UI/Hud/IGearItemActions.cs` (create) | The seam to `RuntimeItemInteraction` + `RuntimeGearItemActions` adapter |
| `src/AcDream.App/UI/Hud/GearDropDispatcher.cs` (create) | Decides and sends a drop: pack icon, grid cell (merge or place), equipment slot |
| `src/AcDream.App/UI/Hud/UiGearDeck.cs` (create) | The window: state, refresh, mount options |
| `src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs` (create) | Drawing |
| `src/AcDream.App/UI/Hud/UiGearDeck.Input.cs` (create) | Clicks, double-clicks, wheel, hover tooltip, drag source and target |
| `src/AcDream.App/UI/WindowNames.cs` (modify) | `GearDeck = "gear-deck"` |
| `src/AcDream.App/UI/RetailUiRuntime.cs` (modify) | Mount, switch replacement, redirect, toolbar lit state, layout list |

---

### Task 1: On-demand replacements in ModernHudSwitch

**Goal:** `ModernHudSwitch` supports a replacement the player opens and closes (the Gear Deck), and it tells the runtime where a retail window's open request should go.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/ModernHudSwitch.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/ModernHudSwitchTests.cs`

**Acceptance Criteria:**
- [ ] `ModernHudReplacement` has a new trailing parameter `bool OnDemand = false`; existing call sites compile unchanged.
- [ ] An on-demand replacement in force is shown only while it is open *and* gameplay is active.
- [ ] `RedirectTarget(retailWindow)` returns the deck window name for an in-force on-demand replacement that lists that retail window, and null otherwise (Modern HUD off, deck not mounted, or a window not listed).
- [ ] `SetOpen(window, bool)` / `ToggleOpen(window)` / `IsOpen(window)` work on on-demand replacements; they return false or do nothing for others.
- [ ] If a replaced retail window becomes visible anyway (a path that bypassed the redirect), the next `Tick` hides it and opens the deck.
- [ ] `Apply(false)` closes the deck and puts the retail window back as `HoldsRetailVisibility` does today; `CloseAllOnDemand()` closes every on-demand deck (used at logout).
- [ ] All existing `ModernHudSwitchTests` still pass.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ModernHudSwitchTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** In `ModernHudSwitchTests.Harness`, add an optional `withGear` flag. It adds this replacement and records calls:

```csharp
if (withGear)
    replacements.Add(new("Gear Deck", WindowNames.GearDeck, [WindowNames.Inventory],
        () => GearMounted, OnShown: () => Calls.Add("gear shown"), OnHidden: () => Calls.Add("gear hidden"),
        HoldsRetailVisibility: true, OnDemand: true));
```

with `public bool GearMounted = true;` on the harness. Add `WindowNames.GearDeck = "gear-deck"` to `src/AcDream.App/UI/WindowNames.cs` now (next to `TargetDeck`) so the test compiles. Then add these tests:

```csharp
[Fact]
public void OnDemand_StaysClosedUntilOpened()
{
    var h = new Harness(withGear: true);
    h.Switch.Apply(true);
    Assert.DoesNotContain(WindowNames.GearDeck, h.Visible);

    Assert.True(h.Switch.SetOpen(WindowNames.GearDeck, true));
    h.Switch.Tick();
    Assert.Contains(WindowNames.GearDeck, h.Visible);
    Assert.Contains("gear shown", h.Calls);

    h.Switch.ToggleOpen(WindowNames.GearDeck);
    h.Switch.Tick();
    Assert.DoesNotContain(WindowNames.GearDeck, h.Visible);
    Assert.False(h.Switch.IsOpen(WindowNames.GearDeck));
}

[Fact]
public void OnDemand_HiddenOutsideGameplay_ReturnsAfter()
{
    var h = new Harness(withGear: true);
    h.Switch.Apply(true);
    h.Switch.SetOpen(WindowNames.GearDeck, true);
    h.Gameplay = false;
    h.Switch.Tick();
    Assert.DoesNotContain(WindowNames.GearDeck, h.Visible);
    h.Gameplay = true;
    h.Switch.Tick();
    Assert.Contains(WindowNames.GearDeck, h.Visible);
}

[Fact]
public void RedirectTarget_OnlyWhileInForce()
{
    var h = new Harness(withGear: true);
    Assert.Null(h.Switch.RedirectTarget(WindowNames.Inventory));
    h.Switch.Apply(true);
    Assert.Equal(WindowNames.GearDeck, h.Switch.RedirectTarget(WindowNames.Inventory));
    Assert.Null(h.Switch.RedirectTarget(WindowNames.Chat));
    h.Switch.Apply(false);
    Assert.Null(h.Switch.RedirectTarget(WindowNames.Inventory));
}

[Fact]
public void RedirectTarget_NullWhenDeckDidNotMount()
{
    var h = new Harness(withGear: true) { };
    h.GearMounted = false;
    h.Switch.Apply(true);
    Assert.Null(h.Switch.RedirectTarget(WindowNames.Inventory));
}

[Fact]
public void OnDemand_RetailShownAnyway_BecomesDeck()
{
    var h = new Harness(withGear: true);
    h.Switch.Apply(true);
    h.Visible.Add(WindowNames.Inventory);      // e.g. the indicator bar opened it directly
    h.Switch.Tick();
    Assert.DoesNotContain(WindowNames.Inventory, h.Visible);
    Assert.True(h.Switch.IsOpen(WindowNames.GearDeck));
    Assert.Contains(WindowNames.GearDeck, h.Visible);
}

[Fact]
public void OnDemand_OffClosesDeck_RestoresHeldRetail()
{
    var h = new Harness(withGear: true);
    h.Visible.Add(WindowNames.Inventory);
    h.Switch.Apply(true);                      // holds "inventory was open"
    h.Switch.SetOpen(WindowNames.GearDeck, true);
    h.Switch.Tick();
    h.Switch.Apply(false);
    Assert.DoesNotContain(WindowNames.GearDeck, h.Visible);
    Assert.Contains(WindowNames.Inventory, h.Visible);
    Assert.False(h.Switch.IsOpen(WindowNames.GearDeck));
}

[Fact]
public void CloseAllOnDemand_ClosesGear()
{
    var h = new Harness(withGear: true);
    h.Switch.Apply(true);
    h.Switch.SetOpen(WindowNames.GearDeck, true);
    h.Switch.CloseAllOnDemand();
    h.Switch.Tick();
    Assert.DoesNotContain(WindowNames.GearDeck, h.Visible);
}

[Fact]
public void SetOpen_FalseForAlwaysOnReplacement()
{
    var h = new Harness(withChat: true);
    h.Switch.Apply(true);
    Assert.False(h.Switch.SetOpen(WindowNames.ChatDeck, true));
}
```

`Harness(bool withChat = false)` becomes `Harness(bool withChat = false, bool withGear = false)`.

- [ ] **Step 2: Run them.** `MAC_TEST_FILTER='FullyQualifiedName~ModernHudSwitchTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → compile errors (`OnDemand`, `SetOpen`, …).

- [ ] **Step 3: Implement.**

Record (add the parameter last and document it):

```csharp
/// <param name="OnDemand">Shown only while the player has it open (the Gear Deck), not whenever
/// gameplay is on screen. Opening one of its retail windows opens it instead (<see cref="ModernHudSwitch.RedirectTarget"/>).</param>
public sealed record ModernHudReplacement(
    string Name,
    string Window,
    IReadOnlyList<string> RetailWindows,
    Func<bool> Mounted,
    Action OnShown,
    Action OnHidden,
    Action? ResyncRetail = null,
    bool HoldsRetailVisibility = false,
    bool OnDemand = false);
```

Fields and members in `ModernHudSwitch`:

```csharp
private readonly HashSet<string> _open = new(StringComparer.Ordinal);

/// <summary>The Modern HUD window that stands in for <paramref name="retailWindow"/> when it
/// is opened, or null when the retail window should open as usual.</summary>
public string? RedirectTarget(string retailWindow)
{
    foreach (ModernHudReplacement r in _replacements)
        if (r.OnDemand && _inForce.Contains(r.Window) && r.RetailWindows.Contains(retailWindow))
            return r.Window;
    return null;
}

public bool IsOpen(string window) => _open.Contains(window);

/// <summary>Opens or closes an on-demand replacement that is in force. False for any other window.</summary>
public bool SetOpen(string window, bool open)
{
    if (!IsOnDemandInForce(window)) return false;
    if (open) _open.Add(window); else _open.Remove(window);
    return true;
}

public bool ToggleOpen(string window) => SetOpen(window, !_open.Contains(window));

/// <summary>Closes every on-demand replacement (logout, character switch).</summary>
public void CloseAllOnDemand() => _open.Clear();

private bool IsOnDemandInForce(string window)
{
    foreach (ModernHudReplacement r in _replacements)
        if (r.OnDemand && r.Window == window) return _inForce.Contains(window);
    return false;
}
```

`Tick()` changes to:

```csharp
public void Tick()
{
    bool gameplay = _gameplayActive();
    foreach (ModernHudReplacement r in _replacements)
    {
        bool inForce = _inForce.Contains(r.Window);
        if (inForce && r.HoldsRetailVisibility)
        {
            foreach (string window in r.RetailWindows)
            {
                if (!_isVisible(window)) continue;
                _hide(window);
                if (r.OnDemand) _open.Add(r.Window);      // a retail open that bypassed the redirect
            }
        }
        bool wanted = inForce && gameplay && (!r.OnDemand || _open.Contains(r.Window));
        SetShown(r, wanted);
    }
    SetSpellDeckShown(_shown.Contains(WindowNames.CommandDeck) && _magicMode() && _spellDeckMounted());
}
```

The re-hide must now happen *before* `SetShown`. Check that the existing chat tests still pass with that order; nothing in them depends on it. In `Apply(false)`, after `_inForce.Remove(r.Window)`, add `_open.Remove(r.Window);`.

Update the class summary: add one sentence that on-demand replacements show only while open, and that their retail windows redirect to them.

- [ ] **Step 4: Run tests.** Same command → all pass.

- [ ] **Step 5: Commit.**

```bash
git add src/AcDream.App/UI/Hud/ModernHudSwitch.cs src/AcDream.App/UI/WindowNames.cs tests/AcDream.App.Tests/UI/Hud/ModernHudSwitchTests.cs
git commit -m "Modern HUD switch: on-demand replacements and retail redirect

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 2: GearInventoryModel (Core)

**Goal:** A pure, tested projection of the player's packs, pack contents, worn items and burden that the deck draws from.

**Files:**
- Create: `src/AcDream.Core/Items/Gear/GearInventoryModel.cs`
- Test: `tests/AcDream.Core.Tests/Items/Gear/GearInventoryModelTests.cs`

**Acceptance Criteria:**
- [ ] `GearInventoryModel.Build(objects, playerId, strength)` returns packs: the main pack (the player, capacity `ItemsCapacity` or 102) first, then each non-equipped side pack in `GetContents(player)` order (capacity `ItemsCapacity` or 24). `SidePackSlots` is `player.ContainersCapacity` or 7.
- [ ] `Contents(packId)` lists loose items in slot order (`GetContainedItems`), excluding equipped items and bags.
- [ ] `Worn` lists items with `CurrentlyEquippedLocation != None` that the player wields or contains.
- [ ] Burden matches retail: `capacity = BurdenMath.EncumbranceCapacity(strength ?? 10, player.GetInt(0xE6))`, `burden = player.Ints[5]` if present, else `SumCarriedBurden`. It exposes `BurdenUnits`, `CapacityUnits`, `BurdenFill` (`LoadToFill`) and `BurdenPercent` (`LoadToPercent`).
- [ ] With no player in the table, `Build` returns `GearInventoryModel.Empty`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearInventoryModelTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.**

```csharp
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearInventoryModelTests
{
    private const uint Player = 0x50000001;

    private static ClientObjectTable Table()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ContainersCapacity = 7 });
        return t;
    }

    [Fact]
    public void NoPlayer_IsEmpty()
    {
        Assert.Same(GearInventoryModel.Empty, GearInventoryModel.Build(new ClientObjectTable(), Player, 100));
    }

    [Fact]
    public void Packs_MainFirst_ThenSidePacksInSlotOrder()
    {
        var t = Table();
        t.AddOrUpdate(new ClientObject { ObjectId = 0xB2, Type = ItemType.Container, ItemsCapacity = 24, ContainerId = Player, ContainerSlot = 1 });
        t.AddOrUpdate(new ClientObject { ObjectId = 0xB1, Type = ItemType.Container, ItemsCapacity = 24, ContainerId = Player, ContainerSlot = 0 });

        GearInventoryModel m = GearInventoryModel.Build(t, Player, 100);

        Assert.Equal([Player, 0xB1u, 0xB2u], m.Packs.Select(p => p.Id));
        Assert.Equal(102, m.Packs[0].Capacity);
        Assert.Equal(24, m.Packs[1].Capacity);
        Assert.Equal(7, m.SidePackSlots);
    }

    [Fact]
    public void Contents_SlotOrder_NoBagsNoWorn()
    {
        var t = Table();
        t.AddOrUpdate(new ClientObject { ObjectId = 0xA2, Name = "Two", ContainerId = Player, ContainerSlot = 1 });
        t.AddOrUpdate(new ClientObject { ObjectId = 0xA1, Name = "One", ContainerId = Player, ContainerSlot = 0 });
        t.AddOrUpdate(new ClientObject { ObjectId = 0xB1, Type = ItemType.Container, ItemsCapacity = 24, ContainerId = Player, ContainerSlot = 0 });
        t.AddOrUpdate(new ClientObject { ObjectId = 0xC1, ContainerId = Player, WielderId = Player, CurrentlyEquippedLocation = EquipMask.HeadWear });

        GearInventoryModel m = GearInventoryModel.Build(t, Player, 100);

        Assert.Equal([0xA1u, 0xA2u], m.Contents(Player).Select(o => o.ObjectId));
        Assert.Equal([0xC1u], m.Worn.Select(o => o.ObjectId));
    }

    [Fact]
    public void Burden_UsesWireEncumbranceWhenPresent()
    {
        var t = Table();
        t.Get(Player)!.Ints[5] = 1500;

        GearInventoryModel m = GearInventoryModel.Build(t, Player, 100);

        Assert.Equal(1500, m.BurdenUnits);
        Assert.Equal(BurdenMath.EncumbranceCapacity(100, 0), m.CapacityUnits);
        float load = BurdenMath.LoadRatio(m.CapacityUnits, 1500);
        Assert.Equal(BurdenMath.LoadToPercent(load), m.BurdenPercent);
        Assert.Equal(BurdenMath.LoadToFill(load), m.BurdenFill);
    }

    [Fact]
    public void Burden_FallsBackToCarriedSum_AndStrength10()
    {
        var t = Table();
        t.AddOrUpdate(new ClientObject { ObjectId = 0xA1, ContainerId = Player, ContainerSlot = 0, Burden = 40 });

        GearInventoryModel m = GearInventoryModel.Build(t, Player, strength: null);

        Assert.Equal(t.SumCarriedBurden(Player), m.BurdenUnits);
        Assert.Equal(BurdenMath.EncumbranceCapacity(10, 0), m.CapacityUnits);
    }
}
```

If `ItemType.Creature` is not a member, use any non-container type the other tests use for the player (see `InventoryControllerTests`).

- [ ] **Step 2: Run them** → compile errors.

- [ ] **Step 3: Implement.** The bag test and capacity defaults copy `InventoryController` (lines ~438-444, 547, 772-785, 1354-1368). Keep them in sync by referring to the same rule:

```csharp
namespace AcDream.Core.Items.Gear;

/// <summary>One of the player's packs: the main pack (the player) or a side pack.</summary>
public readonly record struct GearPack(uint Id, int Capacity, bool IsMain);

/// <summary>
/// What the Gear Deck shows, projected from the object table: the packs, each
/// pack's loose items in slot order, the worn items, and burden. The same rules
/// as the retail inventory (InventoryController): a bag is a container-list member,
/// equipped items never count as pack contents, and burden uses the wire's
/// EncumbranceVal when the server sent it.
/// </summary>
public sealed class GearInventoryModel
{
    public const int MainPackDefaultCapacity = 102;
    public const int SidePackDefaultCapacity = 24;
    public const int SidePackDefaultSlots = 7;

    public static GearInventoryModel Empty { get; } = new([], new Dictionary<uint, IReadOnlyList<ClientObject>>(), [], 0, 0, 0, 0f, 0);

    private readonly IReadOnlyDictionary<uint, IReadOnlyList<ClientObject>> _contents;

    private GearInventoryModel(IReadOnlyList<GearPack> packs, IReadOnlyDictionary<uint, IReadOnlyList<ClientObject>> contents,
        IReadOnlyList<ClientObject> worn, int sidePackSlots, int burden, int capacity, float fill, int percent)
    {
        Packs = packs; _contents = contents; Worn = worn; SidePackSlots = sidePackSlots;
        BurdenUnits = burden; CapacityUnits = capacity; BurdenFill = fill; BurdenPercent = percent;
    }

    public IReadOnlyList<GearPack> Packs { get; }
    public IReadOnlyList<ClientObject> Worn { get; }
    public int SidePackSlots { get; }
    public int BurdenUnits { get; }
    public int CapacityUnits { get; }
    public float BurdenFill { get; }
    public int BurdenPercent { get; }

    public IReadOnlyList<ClientObject> Contents(uint packId) =>
        _contents.TryGetValue(packId, out IReadOnlyList<ClientObject>? items) ? items : [];

    public static GearInventoryModel Build(ClientObjectTable objects, uint playerId, int? strength)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (playerId == 0 || objects.Get(playerId) is not { } player) return Empty;

        var packs = new List<GearPack> { new(playerId, player.ItemsCapacity > 0 ? player.ItemsCapacity : MainPackDefaultCapacity, true) };
        var worn = new List<ClientObject>();
        foreach (uint id in objects.GetContents(playerId))
        {
            if (objects.Get(id) is not { } o || o.CurrentlyEquippedLocation != EquipMask.None) continue;
            if (IsBag(o)) packs.Add(new(o.ObjectId, o.ItemsCapacity > 0 ? o.ItemsCapacity : SidePackDefaultCapacity, false));
        }
        foreach (ClientObject o in objects.GetEquippedBy(playerId)) worn.Add(o);

        var contents = new Dictionary<uint, IReadOnlyList<ClientObject>>();
        foreach (GearPack p in packs)
        {
            var items = new List<ClientObject>();
            foreach (uint id in objects.GetContainedItems(p.Id))
                if (objects.Get(id) is { } o && o.CurrentlyEquippedLocation == EquipMask.None) items.Add(o);
            contents[p.Id] = items;
        }

        int capacity = BurdenMath.EncumbranceCapacity(strength ?? 10, player.GetInt(0xE6));
        int burden = player.Ints.TryGetValue(5, out int wire) ? wire : objects.SumCarriedBurden(playerId);
        float load = BurdenMath.LoadRatio(capacity, burden);
        return new GearInventoryModel(packs, contents, worn,
            player.ContainersCapacity > 0 ? player.ContainersCapacity : SidePackDefaultSlots,
            burden, capacity, BurdenMath.LoadToFill(load), BurdenMath.LoadToPercent(load));
    }

    /// <summary>The retail container-list test (ClientObjectTable.IsContainerListMember).</summary>
    public static bool IsBag(ClientObject o) =>
        o.ContainerTypeHint != 0 || o.Type.HasFlag(ItemType.Container) || o.ItemsCapacity > 0;
}
```

Check two things against the real code before relying on them: `InventoryController.RefreshBurden` (for the exact wire key) and `ClientObjectTable.IsContainerListMember` (for the bag test). If `IsContainerListMember` is public, call it and don't copy it. Use whatever the code really does.

- [ ] **Step 4: Run tests** → pass.

- [ ] **Step 5: Commit.** `git add src/AcDream.Core/Items/Gear tests/AcDream.Core.Tests/Items/Gear && git commit -m "Gear Deck: inventory model (packs, contents, worn, burden)" -m "Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"`

---

### Task 3: GearSlotCatalog (Core)

**Goal:** The 24 equipment slots in Slots-view order, with short labels, and which worn item fills each.

**Files:**
- Create: `src/AcDream.Core/Items/Gear/GearSlotCatalog.cs`
- Test: `tests/AcDream.Core.Tests/Items/Gear/GearSlotCatalogTests.cs`

**Acceptance Criteria:**
- [ ] `GearSlotCatalog.Slots` has exactly 24 entries in this order (6 per row):
  - Row 1: Head (`HeadWear`), Neck (`NeckWear`), Cloak (`Cloak`), Blue (`SigilOne`), Yellow (`SigilTwo`), Red (`SigilThree`)
  - Row 2: Chest (`ChestArmor`), Abdomen (`AbdomenArmor`), Upper arm (`UpperArmArmor`), Lower arm (`LowerArmArmor`), Shirt (`ChestWear`), Pants (`UpperLegWear`)
  - Row 3: Upper leg (`UpperLegArmor`), Lower leg (`LowerLegArmor`), Hands (`HandWear`), Feet (`FootWear`), L wrist (`WristWearLeft`), R wrist (`WristWearRight`)
  - Row 4: L ring (`FingerWearLeft`), R ring (`FingerWearRight`), Weapon (`MeleeWeapon|MissileWeapon|Held|TwoHanded`), Shield (`Shield`), Ammo (`MissileAmmo`), Trinket (`TrinketOne`)
- [ ] These are the same masks as `PaperdollSlotBackgrounds.Definitions`; a test asserts every catalog mask exists there and vice versa.
- [ ] The three Sigil slots have `IsAetheria = true`.
- [ ] `Occupant(objects, playerId, slot)` returns `PaperdollSelectionPolicy.GetUpperInventoryObject(...)`, or 0 when that returns the player id (nothing worn).

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearSlotCatalogTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** The parity test lives in the App test project, because `PaperdollSlotBackgrounds` is App code. Put `GearSlotCatalogParityTests.cs` in `tests/AcDream.App.Tests/UI/Hud/`. Before writing it, read `PaperdollSlotBackgrounds.Definitions` for its element type; the shape assumed here is `(uint Element, EquipMask Mask)`. Core tests:

```csharp
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearSlotCatalogTests
{
    private const uint Player = 0x50000001;

    [Fact]
    public void TwentyFourSlots_FirstAndLast()
    {
        Assert.Equal(24, GearSlotCatalog.Slots.Count);
        Assert.Equal(EquipMask.HeadWear, GearSlotCatalog.Slots[0].Mask);
        Assert.Equal("Head", GearSlotCatalog.Slots[0].Label);
        Assert.Equal(EquipMask.TrinketOne, GearSlotCatalog.Slots[23].Mask);
    }

    [Fact]
    public void SigilsAreAetheria()
    {
        Assert.Equal(3, GearSlotCatalog.Slots.Count(s => s.IsAetheria));
        Assert.All(GearSlotCatalog.Slots.Where(s => s.IsAetheria), s => Assert.True((s.Mask & EquipMask.Sigil) != 0));
    }

    [Fact]
    public void Occupant_WornItem_ElseZero()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player });
        t.AddOrUpdate(new ClientObject { ObjectId = 0xC1, ContainerId = Player, WielderId = Player,
            CurrentlyEquippedLocation = EquipMask.HeadWear, ValidLocations = EquipMask.HeadWear });

        Assert.Equal(0xC1u, GearSlotCatalog.Occupant(t, Player, GearSlotCatalog.Slots[0]));
        Assert.Equal(0u, GearSlotCatalog.Occupant(t, Player, GearSlotCatalog.Slots[1]));
    }
}
```

The App parity test:

```csharp
[Fact]
public void SameMasksAsRetailPaperdoll()
{
    var retail = PaperdollSlotBackgrounds.Definitions.Select(d => d.Mask).ToHashSet();
    var gear = GearSlotCatalog.Slots.Select(s => s.Mask).ToHashSet();
    Assert.Equal(retail, gear);
}
```

- [ ] **Step 2: Run** → compile errors.

- [ ] **Step 3: Implement.**

```csharp
namespace AcDream.Core.Items.Gear;

public sealed record GearSlot(EquipMask Mask, string Label, bool IsAetheria = false);

/// <summary>The Gear Deck's equipment slots in Slots-view order (six to a row), the same
/// masks as the retail paper doll (PaperdollSlotBackgrounds).</summary>
public static class GearSlotCatalog
{
    public const EquipMask Weapon = EquipMask.MeleeWeapon | EquipMask.MissileWeapon | EquipMask.Held | EquipMask.TwoHanded;

    public static IReadOnlyList<GearSlot> Slots { get; } =
    [
        new(EquipMask.HeadWear, "Head"), new(EquipMask.NeckWear, "Neck"), new(EquipMask.Cloak, "Cloak"),
        new(EquipMask.SigilOne, "Blue", true), new(EquipMask.SigilTwo, "Yellow", true), new(EquipMask.SigilThree, "Red", true),
        new(EquipMask.ChestArmor, "Chest"), new(EquipMask.AbdomenArmor, "Abdomen"), new(EquipMask.UpperArmArmor, "Upper arm"),
        new(EquipMask.LowerArmArmor, "Lower arm"), new(EquipMask.ChestWear, "Shirt"), new(EquipMask.UpperLegWear, "Pants"),
        new(EquipMask.UpperLegArmor, "Upper leg"), new(EquipMask.LowerLegArmor, "Lower leg"), new(EquipMask.HandWear, "Hands"),
        new(EquipMask.FootWear, "Feet"), new(EquipMask.WristWearLeft, "L wrist"), new(EquipMask.WristWearRight, "R wrist"),
        new(EquipMask.FingerWearLeft, "L ring"), new(EquipMask.FingerWearRight, "R ring"), new(Weapon, "Weapon"),
        new(EquipMask.Shield, "Shield"), new(EquipMask.MissileAmmo, "Ammo"), new(EquipMask.TrinketOne, "Trinket"),
    ];

    /// <summary>The item shown in <paramref name="slot"/> (the retail upper-inventory pick), or 0.</summary>
    public static uint Occupant(ClientObjectTable objects, uint playerId, GearSlot slot)
    {
        uint id = PaperdollSelectionPolicy.GetUpperInventoryObject(objects, playerId, slot.Mask);
        return id == playerId ? 0u : id;
    }
}
```

- [ ] **Step 4: Run both test classes** (`MAC_TEST_FILTER='FullyQualifiedName~GearSlotCatalog'`) → pass.

- [ ] **Step 5: Commit** both test files and the catalog: "Gear Deck: equipment slot catalog".

---

### Task 4: GearDeckGeometry

**Goal:** A pure layout of the column at a given height and state, with hit-testing, so drawing and input share one set of rectangles.

**Files:**
- Create: `src/AcDream.App/UI/Hud/GearDeckGeometry.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/GearDeckGeometryTests.cs`

**Acceptance Criteria:**
- [ ] `GearDeckGeometry.Width == 300`, `MinHeight == 360`, `DefaultHeight == 600`.
- [ ] `Create(height, GearDeckLayoutInput input)` gives, top to bottom, with `Pad = 10` and 6-point gaps:
  - `Title` (22 high) holding `Close` (16×16 at the right)
  - `EquipHeader` (20) with `FoldStud` (the ▾/▸ area at the left), `FigureStud` and `SlotsStud` (52×18 each at the right)
  - `Equipment`: Slots expanded is 4 rows of 38-point cells with 4-point gaps (164 high, 6 columns centred); folded is rows of 24-point cells with 3-point gaps, as many rows as `input.WornCount` needs at 10 per row, at least 1 and at most 3
  - `PacksHeader` (20) with `GridStud` and `ListStud` at the right
  - `BurdenBar` (8)
  - `PackArea`: the rest above the footer, split into `Contents` (left) and `SideColumn` (36 wide at the right, 6-point gap)
  - `Footer` (16)
- [ ] `Contents` grid: 6 columns of 36-point cells with 2-point gaps. `GridCell(i, scrollRow)` returns the rect for item index i, or null when it is scrolled out. `VisibleGridRows` is the number of full rows that fit.
- [ ] List view: rows 22 high. `ListRow(i, scrollRow)` behaves the same way.
- [ ] `SidePack(i)`: 34×34 cells stacked with 4-point gaps; index 0 is the main pack. The column holds `1 + input.SidePackSlots` cells, clipped to the area.
- [ ] `EquipSlot(i)` for i in 0..23 (Slots view), and `WornCell(i)` (folded).
- [ ] `HitAt(x, y)` returns a `GearDeckHit(GearZone Zone, int Index = -1)` for Close, FoldStud, FigureStud, SlotsStud, GridStud, ListStud, EquipSlot(i), WornCell(i), Item(i, grid or list), SidePack(i), Frame and None.
- [ ] Below `MinHeight` the layout behaves as if at `MinHeight` (no negative rectangles).

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearDeckGeometryTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.**

```csharp
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearDeckGeometryTests
{
    private static GearDeckLayoutInput Input(bool folded = false, bool list = false, int worn = 12, int itemCount = 96, int sideSlots = 7) =>
        new(folded, list, worn, itemCount, sideSlots, ScrollRow: 0);

    [Fact]
    public void SectionsStackTopToBottom()
    {
        var g = GearDeckGeometry.Create(600, Input());
        Assert.True(g.Title.Y < g.EquipHeader.Y);
        Assert.True(g.EquipHeader.Y < g.Equipment.Y);
        Assert.True(g.Equipment.Y + g.Equipment.H <= g.PacksHeader.Y);
        Assert.True(g.PacksHeader.Y < g.BurdenBar.Y);
        Assert.True(g.BurdenBar.Y < g.PackArea.Y);
        Assert.True(g.PackArea.Y + g.PackArea.H <= g.Footer.Y);
        Assert.Equal(600, g.Frame.H);
        Assert.Equal(300, g.Frame.W);
    }

    [Fact]
    public void Folding_GivesRoomToPacks()
    {
        var open = GearDeckGeometry.Create(600, Input());
        var folded = GearDeckGeometry.Create(600, Input(folded: true));
        Assert.True(folded.PackArea.H > open.PackArea.H);
        Assert.True(folded.VisibleGridRows > open.VisibleGridRows);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(30, 3)]
    [InlineData(0, 1)]
    public void FoldedRowCount(int worn, int rows)
    {
        var g = GearDeckGeometry.Create(600, Input(folded: true, worn: worn));
        Assert.Equal(rows * 24 + (rows - 1) * 3, g.Equipment.H);
    }

    [Fact]
    public void GridCell_SixColumns_ScrolledOutIsNull()
    {
        var g = GearDeckGeometry.Create(600, Input());
        DeckRect a = g.GridCell(0, 0)!.Value, b = g.GridCell(5, 0)!.Value, c = g.GridCell(6, 0)!.Value;
        Assert.Equal(a.Y, b.Y);
        Assert.Equal(a.X, c.X);
        Assert.Equal(a.Y + 38, c.Y);
        Assert.Null(g.GridCell(0, scrollRow: 1));
    }

    [Fact]
    public void Hit_RoundTrips()
    {
        var g = GearDeckGeometry.Create(600, Input());
        DeckRect cell = g.GridCell(7, 0)!.Value;
        Assert.Equal(new GearDeckHit(GearZone.Item, 7), g.HitAt(cell.X + 2, cell.Y + 2));
        DeckRect slot = g.EquipSlot(3);
        Assert.Equal(new GearDeckHit(GearZone.EquipSlot, 3), g.HitAt(slot.X + 1, slot.Y + 1));
        DeckRect pack = g.SidePack(2);
        Assert.Equal(new GearDeckHit(GearZone.SidePack, 2), g.HitAt(pack.X + 1, pack.Y + 1));
        Assert.Equal(GearZone.Close, g.HitAt(g.Close.X + 1, g.Close.Y + 1).Zone);
    }

    [Fact]
    public void ListRows_22High()
    {
        var g = GearDeckGeometry.Create(600, Input(list: true));
        Assert.Equal(22, g.ListRow(1, 0)!.Value.Y - g.ListRow(0, 0)!.Value.Y);
    }

    [Fact]
    public void BelowMinHeight_ClampsToMin()
    {
        var g = GearDeckGeometry.Create(100, Input());
        Assert.Equal(GearDeckGeometry.MinHeight, g.Frame.H);
        Assert.True(g.PackArea.H > 0);
    }
}
```

- [ ] **Step 2: Run** → compile errors.

- [ ] **Step 3: Implement.** Model the API on `ChatDeckGeometry`: a sealed class, `const` sizes, `required DeckRect` init properties and a static `Create`. Use these signatures:

```csharp
internal readonly record struct GearDeckLayoutInput(bool Folded, bool List, int WornCount, int ItemCount, int SidePackSlots, int ScrollRow);
internal enum GearZone { None, Frame, Close, FoldStud, FigureStud, SlotsStud, GridStud, ListStud, EquipSlot, WornCell, Item, SidePack }
internal readonly record struct GearDeckHit(GearZone Zone, int Index = -1);

internal sealed class GearDeckGeometry
{
    public const float Width = 300f, MinHeight = 360f, DefaultHeight = 600f, Pad = 10f, Gap = 6f;
    public const float TitleH = 22f, HeaderH = 20f, BurdenH = 8f, FooterH = 16f;
    public const float SlotCell = 38f, SlotGap = 4f, WornCellSize = 24f, WornGap = 3f, WornPerRow = 10;
    public const float ItemCell = 36f, ItemGap = 2f, Columns = 6, ListRowH = 22f, SideW = 36f, SideCell = 34f, SideGap = 4f;

    public required DeckRect Frame { get; init; }
    public required DeckRect Title { get; init; }
    public required DeckRect Close { get; init; }
    public required DeckRect EquipHeader { get; init; }
    public required DeckRect FoldStud { get; init; }
    public required DeckRect FigureStud { get; init; }
    public required DeckRect SlotsStud { get; init; }
    public required DeckRect Equipment { get; init; }
    public required DeckRect PacksHeader { get; init; }
    public required DeckRect GridStud { get; init; }
    public required DeckRect ListStud { get; init; }
    public required DeckRect BurdenBar { get; init; }
    public required DeckRect PackArea { get; init; }
    public required DeckRect Contents { get; init; }
    public required DeckRect SideColumn { get; init; }
    public required DeckRect Footer { get; init; }
    public required GearDeckLayoutInput Input { get; init; }

    public int VisibleGridRows => (int)MathF.Floor((Contents.H + ItemGap) / (ItemCell + ItemGap));
    public int VisibleListRows => (int)MathF.Floor(Contents.H / ListRowH);

    public static GearDeckGeometry Create(float height, GearDeckLayoutInput input) { /* stack the sections as in the AC */ }
    public DeckRect? GridCell(int index, int scrollRow) { /* row = index / 6 - scrollRow; null when row < 0 or row >= VisibleGridRows */ }
    public DeckRect? ListRow(int index, int scrollRow) { /* same with ListRowH */ }
    public DeckRect EquipSlot(int index) { /* 6 x 4, centred in Equipment */ }
    public DeckRect WornCell(int index) { /* 10 per row from Equipment's left */ }
    public DeckRect SidePack(int index) { /* SideColumn.X+1, SideColumn.Y + index*(SideCell+SideGap) */ }
    public GearDeckHit HitAt(float x, float y) { /* test buttons, then cells; Frame when inside Frame; None otherwise */ }
}
```

Write the bodies so the tests pass and match the Acceptance Criteria exactly. The inner width is `Width - 2*Pad = 280`; the contents grid's width is `6*36 + 5*2 = 226`, which fits beside the side column (`280 - 36 - 6 = 238`). `HitAt` must check `SidePack` only for `index < 1 + input.SidePackSlots`, and `Item` only for indices that exist (`< input.ItemCount`). `EquipSlot` only counts when the section is not folded; `WornCell` only when it is folded and `< WornCount`.

- [ ] **Step 4: Run** → pass.

- [ ] **Step 5: Commit** "Gear Deck: column geometry".

---

### Task 5: Per-character settings and session

**Goal:** The deck's view state (folded, grid/list, selected pack) is saved per character and restored at login.

**Files:**
- Modify: `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs` (add `LoadGearDeck` / `SaveGearDeck` after `SaveChatDeck`)
- Create: `src/AcDream.App/UI/Hud/GearDeckSettings.cs`
- Create: `src/AcDream.App/UI/Hud/GearDeckSession.cs`
- Test: `tests/AcDream.UI.Abstractions.Tests/Panels/Settings/SettingsStoreGearDeckTests.cs`, `tests/AcDream.App.Tests/UI/Hud/GearDeckSessionTests.cs`

**Acceptance Criteria:**
- [ ] `SettingsStore.LoadGearDeck(toonKey)` / `SaveGearDeck(toonKey, json)` read and write `character[toonKey].gearDeck`, keep every other key, and reject text that is not a JSON object (`ArgumentException`), exactly like the Chat Deck pair.
- [ ] `GearDeckSettings` (record): `Folded` (false), `View` (`GearPackView.Grid`), `SelectedPack` (`uint`, 0 = main). `ToJson` writes `{"version":1,"folded":…,"view":"grid"|"list","selectedPack":…}`. `FromJson` returns defaults on null or bad JSON, and keeps unknown keys so a later `ToJson` writes them back.
- [ ] `GearDeckSession(store, characterKey, gameplayActive, clock)`: `Tick()` loads when gameplay is active and the key is real (not blank, not `"default"`). A key change flushes and reloads. `Update(Func<GearDeckSettings, GearDeckSettings>)` changes the settings and saves after 0.5 s of no further change. `EndSession()` flushes and resets to defaults.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → pass

**Steps:**

- [ ] **Step 1: Write the failing store tests.** Copy `SettingsStoreChatDeckTests` (same temporary-file fixture) as `SettingsStoreGearDeckTests`, with `LoadGearDeck`/`SaveGearDeck` and the key `gearDeck`. Keep its four facts: missing returns null; round trip; other keys kept; not an object throws. Add one more fact: saving the gear deck keeps an existing `chatDeck` value for the same character.

- [ ] **Step 2: Write the failing session tests.**

```csharp
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearDeckSessionTests
{
    private sealed class FakeStore : IGearDeckStore
    {
        public readonly Dictionary<string, string> Saved = [];
        public string? Load(string key) => Saved.GetValueOrDefault(key);
        public void Save(string key, string json) => Saved[key] = json;
    }

    [Fact]
    public void LoadsForRealCharacterOnly()
    {
        var store = new FakeStore();
        store.Saved["Davian"] = """{"version":1,"folded":true,"view":"list","selectedPack":7}""";
        string key = "default";
        var s = new GearDeckSession(store, () => key, () => true, () => 0);
        s.Tick();
        Assert.False(s.Settings.Folded);
        key = "Davian";
        s.Tick();
        Assert.True(s.Settings.Folded);
        Assert.Equal(GearPackView.List, s.Settings.View);
        Assert.Equal(7u, s.Settings.SelectedPack);
    }

    [Fact]
    public void Update_SavesAfterHalfSecondIdle()
    {
        var store = new FakeStore();
        double now = 0;
        var s = new GearDeckSession(store, () => "Davian", () => true, () => now);
        s.Tick();
        s.Update(x => x with { Folded = true });
        now = 0.3; s.Tick();
        Assert.False(store.Saved.ContainsKey("Davian"));
        now = 0.6; s.Tick();
        Assert.Contains("\"folded\":true", store.Saved["Davian"]);
    }

    [Fact]
    public void BadJson_Defaults_UnknownKeysKept()
    {
        Assert.Equal(GearDeckSettings.Default, GearDeckSettings.FromJson("not json"));
        GearDeckSettings s = GearDeckSettings.FromJson("""{"version":1,"folded":true,"future":{"x":1}}""");
        Assert.Contains("\"future\"", s.ToJson());
    }

    [Fact]
    public void EndSession_FlushesAndResets()
    {
        var store = new FakeStore();
        var s = new GearDeckSession(store, () => "Davian", () => true, () => 0);
        s.Tick();
        s.Update(x => x with { View = GearPackView.List });
        s.EndSession();
        Assert.Contains("\"view\":\"list\"", store.Saved["Davian"]);
        Assert.Equal(GearDeckSettings.Default, s.Settings);
    }
}
```

- [ ] **Step 3: Run** → compile errors.

- [ ] **Step 4: Implement the store pair.** Copy `LoadChatDeck`/`SaveChatDeck` with the key `gearDeck`, the doc comments reworded and the error messages saying "Gear Deck". Do not share a helper unless it already exists. The repo repeats these pairs per deck.

- [ ] **Step 5: Implement settings and session.**

```csharp
using System.Text.Json.Nodes;

namespace AcDream.App.UI.Hud;

internal enum GearPackView { Grid, List }

/// <summary>The Gear Deck's per-character view state. Unknown keys from a newer build are kept and written back.</summary>
internal sealed record GearDeckSettings(bool Folded, GearPackView View, uint SelectedPack)
{
    public static GearDeckSettings Default { get; } = new(false, GearPackView.Grid, 0u);
    private JsonObject? Extra { get; init; }

    public static GearDeckSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return Default;
            bool folded = o["folded"]?.GetValue<bool>() ?? false;
            GearPackView view = (string?)o["view"] == "list" ? GearPackView.List : GearPackView.Grid;
            uint pack = o["selectedPack"]?.GetValue<uint>() ?? 0u;
            var extra = (JsonObject)o.DeepClone();
            foreach (string k in new[] { "version", "folded", "view", "selectedPack" }) extra.Remove(k);
            return new GearDeckSettings(folded, view, pack) { Extra = extra.Count > 0 ? extra : null };
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
        return o.ToJsonString();
    }

    public bool Equals(GearDeckSettings? other) =>
        other is not null && Folded == other.Folded && View == other.View && SelectedPack == other.SelectedPack;
    public override int GetHashCode() => HashCode.Combine(Folded, View, SelectedPack);
}
```

```csharp
namespace AcDream.App.UI.Hud;

internal interface IGearDeckStore { string? Load(string toonKey); void Save(string toonKey, string json); }

internal sealed class SettingsStoreGearDeckStore(AcDream.UI.Abstractions.Panels.Settings.SettingsStore store) : IGearDeckStore
{
    public string? Load(string toonKey) => store.LoadGearDeck(toonKey);
    public void Save(string toonKey, string json) => store.SaveGearDeck(toonKey, json);
}

/// <summary>Loads and saves the Gear Deck's settings for the logged-in character, coalescing saves
/// over <see cref="SaveIdleSeconds"/> like ChatDeckSession.</summary>
internal sealed class GearDeckSession(IGearDeckStore? store, Func<string> characterKey, Func<bool> gameplayActive,
    Func<double>? clock = null, Action<string>? report = null)
{
    public const double SaveIdleSeconds = 0.5;
    private readonly Func<double> _clock = clock ?? (() => Environment.TickCount64 / 1000.0);
    private string? _key;
    private bool _dirty;
    private double _lastChange;

    public GearDeckSettings Settings { get; private set; } = GearDeckSettings.Default;
    public event Action? Changed;

    public void Tick()
    {
        string key = characterKey();
        bool real = gameplayActive() && !string.IsNullOrWhiteSpace(key) && key != RuntimeSettingsController.DefaultToonKey;
        if (real && key != _key)
        {
            Flush();
            _key = key;
            Settings = GearDeckSettings.FromJson(SafeLoad(key));
            Changed?.Invoke();
        }
        if (_dirty && _clock() - _lastChange >= SaveIdleSeconds) Flush();
    }

    public void Update(Func<GearDeckSettings, GearDeckSettings> change)
    {
        GearDeckSettings next = change(Settings);
        if (next.Equals(Settings)) return;
        Settings = next;
        _dirty = true;
        _lastChange = _clock();
        Changed?.Invoke();
    }

    public void EndSession()
    {
        Flush();
        _key = null;
        Settings = GearDeckSettings.Default;
        Changed?.Invoke();
    }

    private void Flush()
    {
        if (!_dirty || _key is null || store is null) { _dirty = false; return; }
        try { store.Save(_key, Settings.ToJson()); }
        catch (Exception ex) { report?.Invoke($"[gear-deck] save failed: {ex.Message}"); }
        _dirty = false;
    }

    private string? SafeLoad(string key)
    {
        try { return store?.Load(key); }
        catch (Exception ex) { report?.Invoke($"[gear-deck] load failed: {ex.Message}"); return null; }
    }
}
```

`RuntimeSettingsController.DefaultToonKey` is what `ChatDeckSession` checks; use the same reference and add whatever `using` it needs.

- [ ] **Step 6: Run** → pass. **Commit:** "Gear Deck: per-character settings and session".

---

### Task 6: Item actions seam and drop dispatcher

**Goal:** Every move the deck makes goes through one tested dispatcher that uses the retail rules and `RuntimeItemInteraction`.

**Files:**
- Create: `src/AcDream.App/UI/Hud/IGearItemActions.cs` (interface + `RuntimeGearItemActions` adapter)
- Create: `src/AcDream.App/UI/Hud/GearDropDispatcher.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/GearDropDispatcherTests.cs`

**Acceptance Criteria:**
- [ ] `IGearItemActions` has these members:
  - `bool Ready()` (wraps `EnsureInventoryRequestReady`)
  - `void Report(string text)` (`ReportClientLocal`)
  - `bool PutInContainer(uint item, uint container, int placement, bool fromGround)` (`TryDispatchPendingBackpackPlacement` + the put-in-container send)
  - `bool Merge(uint from, uint to)` (`TryMergeItemsForAutomation`)
  - `bool WieldFromPaperdoll(uint item, EquipMask mask)`, `bool WieldSecondary(uint item)`
  - `bool Activate(uint item)` (`ActivateItem`)
  - `bool OpenPack(uint pack)` (`ActivateItem` on the bag)
- [ ] `GearDropDispatcher.DropOnPack(item, packId, fromGround)` puts the item at the top (placement 0) of the pack. A bag dropped on any pack goes into the main pack. If the pack is full, it falls through to `InventoryPlacementSearch.ChooseContainer`. If nothing has room, or the item is illegal there, it reports `InventoryContainerPlacementPolicy.ComposeClientLocal` text and sends nothing.
- [ ] `DropOnCell(item, packId, targetItemId, targetIndex, fromGround)`: dropping on an item with the same weenie class that can stack sends `Merge`. Otherwise it places at `ItemListDropPlacement.Resolve(targetIndex, count, sourceIndex, wholeStack: true)` in that pack, and does nothing when that resolves to no move.
- [ ] `DropOnSlot(item, slotMask)`: `ItemEquipRules.IsOffHandWeaponDrop(item, mask)` sends `WieldSecondary`; otherwise `ResolvePaperdollDropWieldMask(item, mask)` → `WieldFromPaperdoll`. A zero mask sends nothing.
- [ ] `TakeOff(item)` moves a worn item into `ChooseContainer(objects, item, player, 0, player, out _)` at placement 0, or reports the refusal.
- [ ] Nothing is sent when `Ready()` is false.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearDropDispatcherTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → pass

**Steps:**

- [ ] **Step 1: Read before writing.** Read `InventoryController.HandleDropRelease` (`src/AcDream.App/UI/Layout/InventoryController.cs:933-1109`) and `TryMergeStacks` (:1111). Also read the signatures of `ItemListDropPlacement.Resolve`, `StackMergePlanner.Plan`, `ItemEquipRules` and `RuntimeItemInteraction.TryMergeItemsForAutomation` / `TryDispatchPendingBackpackPlacement`. The dispatcher applies the same policy calls in the same order; it does not invent new rules. Where the retail code does something these criteria don't mention (for example the pending-placement conflict check), do the same through the seam.

- [ ] **Step 2: Write the failing tests** with a fake `IGearItemActions` that records calls, and a real `ClientObjectTable`:

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Items;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearDropDispatcherTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, Sword = 0xA1;

    private sealed class Fake : IGearItemActions
    {
        public bool IsReady = true;
        public readonly List<string> Calls = [];
        public bool Ready() => IsReady;
        public void Report(string text) => Calls.Add($"report {text}");
        public bool PutInContainer(uint item, uint container, int placement, bool fromGround) { Calls.Add($"put {item:X} {container:X} {placement}"); return true; }
        public bool Merge(uint from, uint to) { Calls.Add($"merge {from:X} {to:X}"); return true; }
        public bool WieldFromPaperdoll(uint item, EquipMask mask) { Calls.Add($"wield {item:X} {mask}"); return true; }
        public bool WieldSecondary(uint item) { Calls.Add($"secondary {item:X}"); return true; }
        public bool Activate(uint item) { Calls.Add($"use {item:X}"); return true; }
        public bool OpenPack(uint pack) { Calls.Add($"open {pack:X}"); return true; }
    }

    private static (ClientObjectTable t, Fake a, GearDropDispatcher d) World()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, ItemsCapacity = 102, ContainersCapacity = 7 });
        t.AddOrUpdate(new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, ContainerId = Player, ContainerSlot = 0 });
        t.AddOrUpdate(new ClientObject { ObjectId = Sword, Type = ItemType.MeleeWeapon, ContainerId = Player, ContainerSlot = 0,
            ValidLocations = EquipMask.MeleeWeapon });
        var a = new Fake();
        return (t, a, new GearDropDispatcher(t, () => Player, a));
    }

    [Fact]
    public void DropOnPack_PutsAtTop()
    {
        var (_, a, d) = World();
        d.DropOnPack(Sword, Bag, fromGround: false);
        Assert.Equal(["put A1 B1 0"], a.Calls);
    }

    [Fact]
    public void NotReady_SendsNothing()
    {
        var (_, a, d) = World();
        a.IsReady = false;
        d.DropOnPack(Sword, Bag, false);
        Assert.Empty(a.Calls);
    }

    [Fact]
    public void DropOnSameKindStack_Merges()
    {
        var (t, a, d) = World();
        t.AddOrUpdate(new ClientObject { ObjectId = 0xD1, WeenieClassId = 300, StackSize = 5, StackSizeMax = 100, ContainerId = Player, ContainerSlot = 1 });
        t.AddOrUpdate(new ClientObject { ObjectId = 0xD2, WeenieClassId = 300, StackSize = 7, StackSizeMax = 100, ContainerId = Bag, ContainerSlot = 0 });
        d.DropOnCell(0xD2, Player, targetItemId: 0xD1, targetIndex: 1, fromGround: false);
        Assert.Equal(["merge D2 D1"], a.Calls);
    }

    [Fact]
    public void DropOnSlot_Wields()
    {
        var (_, a, d) = World();
        d.DropOnSlot(Sword, GearSlotCatalogWeaponMask());
        Assert.Single(a.Calls, c => c.StartsWith("wield A1"));
    }

    [Fact]
    public void FullPackEverywhere_Reports()
    {
        var (t, a, d) = World();
        t.Get(Bag)!.ItemsCapacity = 0;   // ItemsCapacity 0 → use a real full bag instead if the policy treats 0 as default
        for (int i = 0; i < 24; i++)
            t.AddOrUpdate(new ClientObject { ObjectId = 0xE000u + (uint)i, ContainerId = Bag, ContainerSlot = i });
        t.Get(Bag)!.ItemsCapacity = 24;
        t.Get(Player)!.ItemsCapacity = 1;  // the main pack already holds the sword
        d.DropOnPack(0xE000u + 30, Bag, false);  // an id not in the table: InvalidItem
        Assert.All(a.Calls, c => Assert.StartsWith("report", c));
    }

    [Fact]
    public void TakeOff_MovesToPackWithRoom()
    {
        var (t, a, d) = World();
        t.AddOrUpdate(new ClientObject { ObjectId = 0xC1, ContainerId = Player, WielderId = Player,
            CurrentlyEquippedLocation = EquipMask.HeadWear, ValidLocations = EquipMask.HeadWear });
        d.TakeOff(0xC1);
        Assert.Single(a.Calls, c => c.StartsWith("put C1"));
    }

    private static EquipMask GearSlotCatalogWeaponMask() => AcDream.Core.Items.Gear.GearSlotCatalog.Weapon;
}
```

`FullPackEverywhere_Reports` is a sketch. Make it test a real "no room anywhere" case: fill the main pack and the bag to capacity, then drop a new loose ground item (`fromGround: true`, in the table with `ContainerId = 0`). Assert that exactly one `report` call happens and no `put`. Fix the fixture until `InventoryPlacementSearch.ChooseContainer` really refuses.

- [ ] **Step 3: Run** → compile errors.

- [ ] **Step 4: Implement.** Interface and adapter:

```csharp
using AcDream.Core.Items;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Hud;

/// <summary>What the Gear Deck asks of the game: the retail inventory's own request paths.</summary>
internal interface IGearItemActions
{
    bool Ready();
    void Report(string text);
    bool PutInContainer(uint item, uint container, int placement, bool fromGround);
    bool Merge(uint from, uint to);
    bool WieldFromPaperdoll(uint item, EquipMask mask);
    bool WieldSecondary(uint item);
    bool Activate(uint item);
    bool OpenPack(uint pack);
}

internal sealed class RuntimeGearItemActions(RuntimeItemInteraction items, Action<uint, uint, int>? sendPutItemInContainer) : IGearItemActions
{
    public bool Ready() => items.EnsureInventoryRequestReady();
    public void Report(string text) => items.ReportClientLocal(text);
    public bool PutInContainer(uint item, uint container, int placement, bool fromGround) =>
        items.TryDispatchPendingBackpackPlacement(item, container, placement,
            fromGround ? InventoryRequestKind.Pickup : InventoryRequestKind.PutInContainer,
            () => { if (sendPutItemInContainer is null) return false; sendPutItemInContainer(item, container, placement); return true; });
    public bool Merge(uint from, uint to) => items.TryMergeItemsForAutomation(from, to);
    public bool WieldFromPaperdoll(uint item, EquipMask mask) => items.WieldFromPaperdoll(item, mask);
    public bool WieldSecondary(uint item) => items.TryWieldItemSecondary(item);
    public bool Activate(uint item) => items.ActivateItem(item);
    public bool OpenPack(uint pack) => items.ActivateItem(pack);
}
```

Fit the adapter to the real signatures: the `sendPutItemInContainer` delegate type comes from `InventoryRuntimeBindings.SendPutItemInContainer`, and the return types come from `RuntimeItemInteraction`.

Dispatcher outline. Fill in the bodies by following `HandleDropRelease`:

```csharp
internal sealed class GearDropDispatcher(ClientObjectTable objects, Func<uint> playerGuid, IGearItemActions actions)
{
    public void DropOnPack(uint item, uint packId, bool fromGround) { /* bag → main pack; Evaluate; capacity → ChooseContainer; Ready; PutInContainer(…, 0) */ }
    public void DropOnCell(uint item, uint packId, uint targetItemId, int targetIndex, bool fromGround) { /* Evaluate; Ready; merge if StackMergePlanner says so; else ItemListDropPlacement.Resolve → PutInContainer */ }
    public void DropOnSlot(uint item, EquipMask slotMask) { /* IsOffHandWeaponDrop → WieldSecondary; ResolvePaperdollDropWieldMask → WieldFromPaperdoll */ }
    public void TakeOff(uint item) { /* ChooseContainer(objects, item, player, 0, player, out refusal) → PutInContainer(item, c, 0, false) or Report */ }
    private bool Refuse(InventoryContainerPlacementRejection r, uint item, uint dest) { /* ComposeClientLocal → Report; return true when refused */ }
}
```

- [ ] **Step 5: Run** → pass. **Commit:** "Gear Deck: item actions seam and drop dispatcher".

---

### Task 7: UiGearDeck window (draw)

**Goal:** The window draws the whole column from the model, geometry and settings, in the deck look.

**Files:**
- Create: `src/AcDream.App/UI/Hud/UiGearDeck.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/UiGearDeckDrawTests.cs`

**Acceptance Criteria:**
- [ ] `UiGearDeck(GearDeckBindings b, DeckFonts? fonts, EmbeddedSvgSpriteCache? icons)`. `GearDeckBindings` is a record with:
  - `Func<ClientObjectTable> Objects`, `Func<uint> PlayerGuid`, `Func<int?> Strength`
  - `Func<ItemType, uint, uint, uint, uint, uint> ResolveIcon`, `Func<ItemType, uint, uint, uint, uint, uint> ResolveDragIcon`
  - `Func<ClientObject, string> DisplayName`, `GearDeckSession Session`, `IGearItemActions Actions`
  - `Func<uint, bool> IsPending`, `Action Close`
- [ ] `Width` is fixed at 300 and `Height` defaults to `DefaultHeight`. `MountOptions(left, top)` gives `WindowName = WindowNames.GearDeck`, `Chrome = RetailWindowChrome.Imported`, `Resizable`, `ResizeY` only (`ResizableEdges` top and bottom only), `MinHeight = 360`, `ConstrainResizeToParent`, `Visible = false`.
- [ ] Every `OnTick` it rebuilds the model only when `ClientObjectTable.MutationRevision`, the player id or the strength changed. It rebuilds the geometry when the height or input changed.
- [ ] It draws:
  - the frame (`DeckChrome.DrawFrame` on the inset body), the title "Gear" and a ✕
  - the Equipment header: ▾/▸ and "Equipment", with Figure (dimmed in plan 1) and Slots studs (`DeckChrome.DrawStud`)
  - the Slots view: dark cells, the worn icon, or the slot label in `LabelInk` in the small font when empty. Locked Aetheria slots are dimmed (`AetheriaUnlocks.Read(player)` the way `PaperdollController.ApplyAetheriaVisibility` reads it).
  - the folded row of worn icons
  - the Packs header with "Packs", the burden percentage, and the Grid/List studs
  - a burden gauge (`DeckChrome.DrawGauge` with `StaminaInk`, fill `BurdenFill`)
  - grid cells: the icon from `ResolveIcon`, a stack count bottom-right when `StackSize > 1` (thousands as `9k`), and a dimmed cell when `IsPending(id)`
  - list rows: icon, name (truncated with `UiCommandDeck.FitCastingText`), stack count right-aligned
  - the side column: the main pack icon (`InventoryController.PlayerPackBaseIcon` through `ResolveIcon`'s sprite path, or the main pack's own icon if simpler) and the side-pack icons, with the selected pack outlined in `Handle`, plus empty slots up to `SidePackSlots`
  - the footer "Main · 96 / 102" or "Pack 3 · 10 / 24"
- [ ] Nothing is drawn outside the frame (draw probe test), with and without icons and fonts.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiGearDeckDrawTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → pass

**Steps:**

- [ ] **Step 1: Write the failing draw tests,** in the style of `UiTargetDeckDrawTests` (using `MarkupDrawProbe.Draw(deck, w, h)` and checking that `renderer.DebugSpriteSegmentVerts` stay within bounds). Build bindings over a real `ClientObjectTable` with a player, one bag, 30 items and two worn items. Use `ResolveIcon = (_, _, _, _, _) => 0u` and a `GearDeckSession` with a null store. Cover these states:
  - expanded, grid
  - folded, list
  - height 360
  - an empty table (no player): only the frame and the title draw
- [ ] **Step 2: Run** → compile errors.
- [ ] **Step 3: Implement** `UiGearDeck.cs`:
  - fields: bindings, fonts, icons, `GearInventoryModel _model`, `GearDeckGeometry _g`, `_scrollRow`, `_lastRevision`
  - constructor: `Width = GearDeckGeometry.Width`, `Height = DefaultHeight`, `MinHeight`, `Resizable = true`, `ResizeX = false`, `ResizeY = true`
  - `MountOptions`
  - `OnTick`, which also calls `Session.Tick()`
  - `SelectedPackId`: `Session.Settings.SelectedPack` if that pack still exists in `_model.Packs`, else the player
  - `Items`: `_model.Contents(SelectedPackId)`
  - `OnShown()` / `OnHidden()`: clear the hover, keep the scroll
  - `ConstrainResize`, which keeps the width at 300 and clamps the height to at least `MinHeight`

  Then implement `UiGearDeck.Draw.cs` from the geometry rectangles. Draw text with `DeckFace.For(_fonts, 1f, small)` / `.Draw(ctx, text, x, y, ink)`, and item icons with `ctx.DrawSprite(tex, r.X + 1, r.Y + 1, r.W - 2, r.H - 2, 0, 0, 1, 1, Vector4.One)` after filling the cell `Track` / `Rgb(0x16151B)` with a `Rgb(0x2C2933)` stroke, as in the mockup. Use `ctx.PushClip` around the contents area so scrolled rows clip.
- [ ] **Step 4: Run** → pass. **Commit:** "Gear Deck: window drawing".

---

### Task 8: UiGearDeck input

**Goal:** Every control works: close, fold, view toggles, pack selection, scroll, hover tooltips, double-click, drag from and drop onto the deck.

**Files:**
- Create: `src/AcDream.App/UI/Hud/UiGearDeck.Input.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/UiGearDeckInputTests.cs`

**Acceptance Criteria:**
- [ ] ✕ calls `Close`. ▸/▾ toggles `Folded` and Slots/Grid/List set their settings (through `Session.Update`). Figure does nothing in plan 1.
- [ ] Clicking a side pack selects it (`Session.Update` with `SelectedPack`), resets the scroll, and calls `Actions.OpenPack` for a side pack (not the main pack), as retail `OpenContainer` does.
- [ ] The wheel over the contents scrolls by one row per notch, clamped to `[0, rows - visibleRows]`.
- [ ] Double-click on a pack item → `Actions.Activate`. On an equipment slot or worn cell with an occupant → `GearDropDispatcher.TakeOff`.
- [ ] Drag source: `IsDragSource` is true over an item cell, list row, occupied equipment slot or worn cell. `GetDragPayload()` returns `new ItemDragPayload(id, kind, index, null)` with `kind` `Inventory` for pack items and `Equipment` for worn ones. `GetDragGhost()` uses `ResolveDragIcon` at 32×32.
- [ ] Drop target: `DropReleased` with an `ItemDragPayload`:
  - on a side pack → `DropOnPack`
  - on an item cell or row → `DropOnCell`
  - on the empty contents area → `DropOnPack(selected)`
  - on an equipment slot → `DropOnSlot(slot.Mask)`
  - on a worn cell → `DropOnSlot(that item's CurrentlyEquippedLocation)`
  - `fromGround` is `payload.SourceKind == ItemDragSource.Ground`; a `ShortcutBar` payload is ignored
- [ ] `DragEnter`/`DragOver` highlight the hovered target, and the highlight is cleared on leave and on drop.
- [ ] Hovering an item for 0.45 s shows a `HudTooltip` (Compact) with the `ItemTooltipCaption.Resolve` text. Hovering an empty slot shows its label.
- [ ] Pressing on empty frame space still drags the window (don't set `CapturesPointerDrag` or `HandlesClick` there).

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiGearDeckInputTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → pass

**Steps:**

- [ ] **Step 1: Read** `UiSpellDeck.cs:322-470` (drag source and target, `MouseDown` returning false) and the hit and hover handling in `UiTargetDeck`. Copy their patterns: `OnHitTest` stores `_hit = _g.HitAt(x, y)` and sets `CapturesPointerDrag` for controls; `ReceivesHoverMouseMove => true`; the tooltip timing uses `_hoverSince` and `UiCommandDeck.TooltipDwell`.
- [ ] **Step 2: Write the failing tests.** Construct the deck with a fake `IGearItemActions` (as in Task 6) and a fake store. Send `UiEvent`s straight to `deck.OnEvent` after `deck.OnHitTest(x, y)`, at points taken from the deck's geometry (expose `internal GearDeckGeometry Geometry => _g;`). Tests:
  - clicking the fold stud flips `Session.Settings.Folded`
  - clicking side pack 1 selects it and records `open B1`
  - clicking the main pack records no `open`
  - a double-click on item 0 records `use <id>`
  - a double-click on a worn slot records a `put` (take off)
  - a `DropReleased` with an `ItemDragPayload` on side pack 1 records `put <id> B1 0`
  - a `ShortcutBar` payload records nothing
  - six wheel notches down on 30 items with 4 visible rows clamp at `rows - visibleRows`
  - the close stud calls the bound `Close`
- [ ] **Step 3: Run** → fail.
- [ ] **Step 4: Implement** `UiGearDeck.Input.cs`. Build one `GearDropDispatcher` in the constructor from the bindings, and route every drop and take-off through it.
- [ ] **Step 5: Run** → pass. **Commit:** "Gear Deck: input, drag and drop".

---

### Task 9: Mount, switch replacement and inventory redirect

**Goal:** With Modern HUD on, the inventory key, the toolbar button and plugin `ToggleClientWindow("inventory")` open and close the Gear Deck, and the retail inventory stays hidden. With Modern HUD off, retail behaves as before.

**Files:**
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/GearDeckWiringTests.cs`

**Acceptance Criteria:**
- [ ] `MountGearDeck()` runs after `MountInventory()`, and only when `_bindings.CommandDeck` is not null and `_bindings.Inventory.ItemInteraction` exists. It mounts through `RetailWindowFrame.Mount(Host.Root, deck, _bindings.Assets.ResolveSprite, UiGearDeck.MountOptions(left, top))`. The default spot is the right edge, 10 points in, under the radar's height (top 120). On failure it undoes what it built and logs `[UI] Gear Deck unavailable`, like `MountChatDeck`.
- [ ] The switch gets `GearDeckReplacement(() => _gearDeck)`, an `internal static` factory like `TargetDeckReplacement`: `new("Gear Deck", WindowNames.GearDeck, [WindowNames.Inventory], Mounted, OnShown, OnHidden, HoldsRetailVisibility: true, OnDemand: true)`.
- [ ] The switch's `show`/`hide`/`isVisible` go to *raw* operations, which send catalogued panels to `_panelUi` and everything else to `Host`. They are the current bodies of `ShowWindow`/`HideWindow`/`IsWindowVisible`, moved to `RawShowWindow`/`RawHideWindow`/`RawIsWindowVisible`.
- [ ] The public `ToggleWindow`/`ShowWindow`/`HideWindow`/`IsWindowVisible(name)` first ask `_hudSwitch?.RedirectTarget(name)`. When it returns a deck, they toggle, open or close it through the switch, or report `_hudSwitch.IsOpen(deck)`, then return true. Otherwise they behave as today.
- [ ] The indicator bar's `panelId => _panelUi.TogglePanel(panelId)` goes through `ToggleWindow` when `RetailPanelCatalog` maps the id to a name, so it reaches the redirect.
- [ ] The toolbar Inventory button is lit while the Gear Deck is open: `OnWindowVisibilityChanged` maps `WindowNames.GearDeck` to `RetailPanelCatalog.Inventory`'s panel id. The deck's ✕ calls `_hudSwitch.SetOpen(WindowNames.GearDeck, false)`.
- [ ] `WindowNames.GearDeck` is added to `stateManagedVisibilityWindows`. `ResetSessionTransientUi` calls `_hudSwitch?.CloseAllOnDemand()` and `_gearDeckSession?.EndSession()`. `_gearDeckSession.Tick()` runs from the deck's `OnTick` only, so it does not run while the deck is hidden. Check that the first open after login loads the settings before drawing.
- [ ] Wiring tests in the style of `TargetDeckWiringTests`, with a real `UiRoot`, `RetailWindowFrame.Mount` and `ModernHudSwitch`:
  - Modern HUD on with a stand-in inventory window registered: `RedirectTarget("inventory")` is the gear deck and toggling it shows the deck
  - Modern HUD off: the deck is hidden
  - a retail inventory shown directly becomes the deck on the next tick
- [ ] The full Mac suite passes.

**Verify:** `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → 0 failures

**Steps:**

- [ ] **Step 1: Read** `RetailUiRuntime.cs:599-640` (switch construction), `:656-669` (mount order), `:1461` and `:1538-1560` (window entry points), `:1574-1600` (toolbar sync and `OnWindowVisibilityChanged`), `:2791` (indicator bar), `:3700-3830` (Chat/Target mount and `TargetDeckReplacement`), `:5236-5325` (`MountInventory`) and `:691` (the layout persistence list).
- [ ] **Step 2: Write the failing wiring tests** (`GearDeckWiringTests`), copying the `TargetDeckWiringTests.World` setup. Register a `UiPanel` stand-in as `WindowNames.Inventory`, mount a real `UiGearDeck` with fake bindings, and build the switch with `GearDeckReplacement(() => deck)`. Testing the runtime redirect itself would mean constructing `RetailUiRuntime`. If that is impractical, test the redirect logic through a small `internal static bool RouteWindow(ModernHudSwitch? s, string name, WindowOp op, Func<string, bool> raw)` helper that the public methods call, and unit-test that helper.
- [ ] **Step 3: Run** → fail.
- [ ] **Step 4: Implement** the mount, factory, raw operations, routing helper, indicator bar route, toolbar mapping, layout list entry and logout reset. Build `GearDeckBindings` from `_bindings.Inventory` (`Objects`, `PlayerGuid`, `Strength`, `ResolveIcon`, `ResolveDragIcon`, `ItemInteraction` → `RuntimeGearItemActions(ItemInteraction, SendPutItemInContainer)`, `IsPending = id => ItemInteraction.IsPendingInventorySource(id)`). For `DisplayName`, use the same resolver `InventoryController` passes to `ItemTooltipCaption`; find it in `MountInventory`. The session is `new GearDeckSession(new SettingsStoreGearDeckStore(store), _bindings.Persistence?.CharacterKey ?? (() => "default"), () => _bindings.IsGameplayDisplay?.Invoke() ?? true)`, where `store` is `_bindings.Persistence?.Store` (null store allowed).
- [ ] **Step 5: Run the full suite** → 0 failures. **Commit:** "Gear Deck: mount, Modern HUD replacement and inventory redirect".

---

### Task 10: Live check (user)

**Goal:** The user confirms the Gear Deck works in the real client against the local ACE server.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:** none (fixes found here get their own commits)

**Acceptance Criteria:**
- [ ] Ask the user before launching. Use the local ACE server (10.10.20.20 through `~/OpenAC-dev/dev-client.env`), never Dreamweave.
- [ ] With Modern HUD on, F12 and the toolbar Inventory button open and close the Gear Deck, the retail inventory never appears, and the toolbar button lights while the deck is open.
- [ ] Folding, Slots view, Grid/List and side-pack selection all work and survive a relog.
- [ ] Dragging pack → pack, pack → slot, slot → pack and pack → ground all work, and so do the double-click use, wear and take-off.
- [ ] With Modern HUD off, the retail inventory and paper doll work as before.
- [ ] The user says it passes.

**Verify:** the user's confirmation in the conversation, plus the client log lines with `[gear-deck]` or `[UI] Gear Deck`.

---

## Self-review notes

- Spec coverage for plan 1 (Delivery item 1): projector (T2), Slots view and folded row (T3, T7, T8), window and geometry (T4, T7), Modern HUD switch (T1, T9), packs grid/list, side column and burden (T2, T4, T7), drag-and-drop moves (T6, T8), double-click (T8), persistence (T5). **Moved to plan 2:** `GearTypeGroup` (the query bar is its only user). The plan 3 Figure stud is drawn dimmed.
- Types used across tasks: `GearInventoryModel`, `GearPack`, `GearSlot`, `GearSlotCatalog.Weapon`, `GearDeckLayoutInput`, `GearZone`, `GearDeckHit`, `GearDeckSettings`, `GearPackView`, `IGearDeckStore`, `GearDeckSession`, `IGearItemActions`, `RuntimeGearItemActions`, `GearDropDispatcher`, `GearDeckBindings` and `UiGearDeck`. Each is defined in the task that creates it.
