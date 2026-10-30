# Gear Deck Plan 4: Organising Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Gear Deck's organising tools: a move queue that sends moves one at a time and waits for the server, Combine (a button, a menu item, and on arrival), a split picker, pack tidy, pack rules (by hand and on arrival), group drag of a selection, and item locks. A locked item can't be sold, given, dropped or salvaged, in the deck or through the retail windows.

**Architecture:** Pure Core planners (`StackCombinePlanner`, `PackTidyPlanner`, `PackRulePlanner`) turn `GearItemView` rows into a list of `GearMove`s and send nothing. An App `GearMoveQueue` sends those moves through a `IGearMoveSender`, one at a time. It waits for `InventoryTransactionState.RequestCompleted`/`RequestFailed` (behind `IGearMoveChannel`), stops at the first failure or after a 2 s timeout, and can be cancelled. Locks live in one Core `ItemLocks` set. `GearDeckSession` loads it from and saves it to the per-character Gear Deck settings, and the retail paths read it: `ItemInteractionPolicy.DecidePlacement`, the `RuntimeItemInteraction` give/drop/sell/salvage entry points, `VendorUiController`, `SalvageUiController` and the trade window's add. A runtime-ticked `GearInventoryWatcher` drops stale lock ids once the inventory is complete and queues combine and pack rules for items that arrive. The split picker and the pack-rules editor are deck-look markup popups, mounted the way the Chat Deck's popups are.

**Tech Stack:** C# / .NET 10, xUnit, the retained UI (`UiElement`, `UiRoot`), `DeckChrome` + `DeckFonts`, markup popups (`MarkupDocument`, `PluginAppearanceWindow`, `PluginUiThemeSettings.WithFixedLook`).

**Spec:** `docs/superpowers/specs/2026-10-28-gear-deck-design.md`. Delivery item 4: "`GearMoveQueue`; combine (button and on arrival); split picker; pack tidy; pack rules; locks (including the retail paths)". See also the sections Core (planners, `GearPackRule`, `GearLockSet`), Runtime and network, Interaction (Right-click menu, Locks, Drag) and Persistence. Plans 1-3 are `docs/superpowers/plans/2026-10-28-gear-deck-core.md`, `…-29-gear-deck-query.md` and `…-29-gear-deck-figure.md`. Their SDD ledgers, with deferred minors, are in the main checkout under `.superpowers/sdd/`.

**Worktree:** implement in `~/code/OpenAC/OpenAC-gear-deck` on branch `feat/gear-deck-organising`, made from `docs/gear-deck-spec`. That branch is main `fcbb127e` plus this plan. The Verify commands use that path.

## Global Constraints

- **No new network messages.** Moves use what already exists: `IGearItemActions.PutInContainer` (`TryDispatchPendingBackpackPlacement`), `Merge` (`TryMergeItemsForAutomation`), and a new `Split` over `RuntimeItemInteraction.TrySplitToContainer`.
- **One inventory request at a time.** `InventoryTransactionState` holds a single pending request and has **no timeout**. The queue sends only when `RuntimeItemInteraction.CanMakeInventoryRequest` is true. It must never call `EnsureInventoryRequestReady` to probe, because that prints "You can only move or use one item at a time". It treats a move as done only on `RequestCompleted` for that move's item id.
- **Timeout 2 s, then stop.** A pending request that never completes blocks every later request, so a timed-out queue stops. It does not go on to the next move.
- **Lock message:** `"The {name} is locked."`. In Core it comes from `ItemLocks.Message(name)`. Every refusal prints exactly this one line through that path's existing chat output.
- **Locks block only loss.** They block dropping to the ground, giving (to an NPC or a player, the trade window, automation), selling (vendor drag, staging, the Sell button, automation) and salvaging (panel add, automation). A container holding a locked item is blocked the same way (`ItemLocks.BlockerOf`). Moving between your own packs, wearing, using, tidy, and being a merge *target* are all allowed.
- The look is `DeckChrome` palette + `DeckFonts` only, never the plugin theme. Text drawn in the deck stays ASCII apart from `·`. The queue text is `"Tidying 12 / 40"`, with no ellipsis character.
- Every new size is a constant at scale 1, multiplied by `S(...)` in `GearDeckGeometry` and by `P(...)` in the deck's drawing.
- Settings JSON stays forward-compatible: unknown keys and unreadable rules are kept and written back, like `UnreadFilters`.
- Tests run through `~/code/OpenAC/test-baseline/run-mac-tests.sh`, narrowed with `MAC_TEST_FILTER` and never with a second `--filter`. Never run tests that are known to fail.
- Commit messages end with `Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs`.
- Fork-only: no upstream PR, no plugin contract change.

**User decisions (already made):**
- Organising = combine stacks (button + optional on arrival), split by amount, tidy a pack on the server, pack rules, item locks (all four).
- Moves are sent one at a time with a 2 s confirmation timeout, and a manual drag cancels the queue. Only one queue runs at a time.
- Tidy sorts by type group, then name, then workmanship (descending). Combine merges into the fullest stack first.
- Locks are also enforced in retail sell, give, drop and salvage. A locked item shows a small padlock.
- Salvage and tinkering stay in the retail windows.
- Data is public only (create/update messages); no ID requests.
- Modern HUD windows look like the decks, never the plugin theme. Each deck scales on its own.

**Plan-4 scope notes (decided while planning; flag at review if wrong):**
- **Where the tools live.** The item right-click menu gains Split..., Lock / Unlock and Combine like stacks. A new **pack menu** (right-click a side-pack cell) holds Tidy this pack, Combine stacks, Apply pack rules and Pack rules.... The footer gains a **Combine** button when any stacks can merge, and shows the queue's progress with a **Stop** button while a queue runs.
- **Group drag** moves the whole selection only onto a pack (side cell, pack row, a cell, or empty pack space), through the queue as "Moving". A drop on equipment, the figure or the world takes only the dragged item, as before.
- **Split-drag:** Ctrl/Cmd- or Shift-dragging a stack of more than one opens the split picker for that destination. A split dropped on a stack of the same kind goes to that cell; it does not merge.
- **Pack rules:** a rule names a saved filter (by name), a target pack and "on arrival". Rules run in order, and the first rule that matches an item wins. Items go to the end of the target pack. A rule whose filter was deleted or whose pack is gone is skipped and listed as "(missing)". A full pack leaves the rest where they are, with one chat line. Worn items, packs and locked items are never moved by a rule.
- **On arrival** means an item entering one of your packs from outside (pickup, loot, trade, purchase), confirmed by the server. The watcher waits 0.5 s after the last arrival and runs only while no queue runs. Combine on arrival merges only the groups an arrived item belongs to. Rules on arrival move only arrived items.
- **A Locked chip** joins the query bar (suggested for "lo…", saved as `"locked"`), so a saved filter can say "not locked" by combining with other chips. Only "is locked" exists, no negation.
- **Stale locks:** once per login, after `ClientObjectTable.HasCompleteInventory(player)`, lock ids for objects that no longer exist are dropped.
- **The session ticks every frame** from the runtime (it was `TickSave`). Locks then load at login even while the deck is closed or Modern HUD is off, provided the deck mounted.
- **Combine never takes from a locked stack**: it would vanish into another id and lose the lock. A locked stack can still be filled.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.Core/Items/ItemLocks.cs` (create) | The player's locked ids, `Revision`/`Changed`, `BlockerOf` (self or contents), `Message` |
| `src/AcDream.Core/Items/Gear/GearProjection.cs` (modify) | `GearItemView` gains `Locked`, `Weenie`; `Build` takes `isLocked` |
| `src/AcDream.Core/Items/Gear/GearQuery.cs`, `GearQueryEngine.cs`, `GearQueryJson.cs`, `GearChipText.cs`, `GearChipSuggester.cs` (modify) | `GearField.Locked` chip |
| `src/AcDream.Core/Items/ItemInteractionPolicy.cs` (modify) | `ItemPlacementPolicyInput.Locked`; loss actions become a Reject |
| `src/AcDream.Runtime/Gameplay/RuntimeItemInteraction.cs` (modify) | `Locks`, `RefuseIfLocked`; guards in `PlaceIn3D`, give, drop, sell, salvage |
| `src/AcDream.Core/Items/VendorSellAcceptability.cs`, `src/AcDream.App/UI/Layout/VendorUiController.cs` (modify) | `VendorSellRejection.Locked` |
| `src/AcDream.App/UI/Layout/SalvageUiController.cs` (modify) | `IsLocked` binding; refuse + report |
| `src/AcDream.Core/Items/Gear/GearMove.cs` (create) | `GearMove` / `GearMoveKind` |
| `src/AcDream.Core/Items/Gear/StackCombinePlanner.cs`, `PackTidyPlanner.cs`, `PackRulePlanner.cs`, `GearPackRule.cs` (create) | The three planners and the rule record |
| `src/AcDream.App/UI/Hud/GearDeckSettings.cs` (modify) | `LockedIds`, `PackRules`, `CombineOnArrival` + JSON |
| `src/AcDream.App/UI/Hud/GearDeckSession.cs` (modify) | Keeps an `ItemLocks` in step with the settings |
| `src/AcDream.App/UI/Hud/GearMoveQueue.cs` (create) | `IGearMoveChannel`, `IGearMoveSender`, `GearMoveQueue`, `GearMoveSender`, `RuntimeGearMoveChannel` |
| `src/AcDream.App/UI/Hud/IGearItemActions.cs` (modify) | `Split` (default member) |
| `src/AcDream.App/UI/Hud/GearInventoryWatcher.cs` (create) | Stale-lock prune; on-arrival combine + rules |
| `src/AcDream.App/UI/Hud/GearDeckGeometry.cs` (modify) | `CombineButton`, `StopButton`, zones, inputs |
| `src/AcDream.App/UI/Hud/UiGearDeck.cs`, `.Draw.cs`, `.Input.cs`, `.Menu.cs` (modify) | Queue + lock wiring, footer, padlocks, pack menu, item menu, group drag, split-drag |
| `src/AcDream.App/UI/Hud/UiGearDeck.Organise.cs` (create) | The deck's organising actions (combine, tidy, rules, locks, group move, split requests) |
| `src/AcDream.App/UI/Hud/GearSplitBinding.cs`, `GearRulesBinding.cs` (create) | The two markup popups |
| `src/AcDream.App/UI/RetailUiRuntime.cs` (modify) | Owns `ItemLocks`, queue, watcher, popups; per-frame ticks; retail bindings |

---

### Task 1: Item locks and the Locked chip in Core

**Goal:** Add a Core `ItemLocks` set, carry `Locked` and the weenie class on every `GearItemView`, and let a query filter by a Locked chip.

**Files:**
- Create: `src/AcDream.Core/Items/ItemLocks.cs`
- Modify: `src/AcDream.Core/Items/Gear/GearProjection.cs`, `GearQuery.cs`, `GearQueryEngine.cs`, `GearQueryJson.cs`, `GearChipText.cs`, `GearChipSuggester.cs`
- Test: `tests/AcDream.Core.Tests/Items/ItemLocksTests.cs` (create), `tests/AcDream.Core.Tests/Items/Gear/GearProjectionTests.cs`, `GearQueryEngineTests.cs`, `GearQueryJsonTests.cs`, `GearChipSuggesterTests.cs`, `GearChipTextTests.cs`

**Acceptance Criteria:**
- [ ] `ItemLocks.Set/Replace/IsLocked` work, ignore id 0, and bump `Revision` + raise `Changed` only on a real change
- [ ] `BlockerOf(table, id)` returns `id` when locked, the first locked contained item for a container, else 0
- [ ] `GearProjection.Build(..., isLocked)` fills `Locked` and `Weenie` on every row (pack items, side packs, worn)
- [ ] A `GearChip.LockedOnly` chip keeps only locked rows; JSON round-trips it as `"locked"`; "lo"/"lock" suggests it; it labels "Locked" / "Locked items"
- [ ] Existing Gear Core tests still pass

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~AcDream.Core.Tests.Items.Gear|FullyQualifiedName~ItemLocksTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.Core.Tests/Items/ItemLocksTests.cs`:

```csharp
using AcDream.Core.Items;
using Xunit;

namespace AcDream.Core.Tests.Items;

public sealed class ItemLocksTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, Sword = 0xA1, Gem = 0xA2;

    private static ClientObjectTable Table()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        t.AddOrUpdate(new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, Name = "Pack" });
        Assert.True(t.MoveItem(Bag, Player, 0));
        t.AddOrUpdate(new ClientObject { ObjectId = Sword, Type = ItemType.MeleeWeapon, Name = "Sword" });
        Assert.True(t.MoveItem(Sword, Player, 0));
        t.AddOrUpdate(new ClientObject { ObjectId = Gem, Type = ItemType.Gem, Name = "Gem" });
        Assert.True(t.MoveItem(Gem, Bag, 0));
        return t;
    }

    [Fact]
    public void SetAndReplace_ChangeOnlyWhenTheSetChanges()
    {
        var locks = new ItemLocks();
        int changed = 0;
        locks.Changed += () => changed++;
        Assert.True(locks.Set(Sword, true));
        Assert.False(locks.Set(Sword, true));
        Assert.False(locks.Set(0u, true));
        Assert.True(locks.IsLocked(Sword));
        Assert.False(locks.IsLocked(0u));
        locks.Replace([Sword]);
        Assert.Equal(1, changed);
        Assert.Equal(1, locks.Revision);
        locks.Replace([Gem, 0u]);
        Assert.Equal(2, changed);
        Assert.Equal(new[] { Gem }, locks.Ids);
        Assert.True(locks.Set(Gem, false));
        Assert.Empty(locks.Ids);
    }

    [Fact]
    public void BlockerOf_IsTheItemItself_OrALockedItemInsideAContainer()
    {
        ClientObjectTable t = Table();
        var locks = new ItemLocks();
        Assert.Equal(0u, locks.BlockerOf(t, Sword));
        locks.Set(Sword, true);
        Assert.Equal(Sword, locks.BlockerOf(t, Sword));
        Assert.Equal(0u, locks.BlockerOf(t, Bag));
        locks.Set(Gem, true);
        Assert.Equal(Gem, locks.BlockerOf(t, Bag));
    }

    [Fact]
    public void Message_NamesTheItem() => Assert.Equal("The Sword is locked.", ItemLocks.Message("Sword"));
}
```

Add to `GearProjectionTests` (use the file's existing table helpers; one pack item, one side pack, one worn item):

```csharp
[Fact]
public void Build_FillsLockedAndWeenie_OnEveryRow()
{
    // Arrange with the file's existing fixture: a player, a side pack, a loose item and a worn item,
    // each with a distinct WeenieClassId. Lock the loose item and the side pack.
    var locked = new HashSet<uint> { LooseId, SidePackId };
    GearProjection p = GearProjection.Build(model, table, o => o.Name, locked.Contains);
    Assert.All(p.All, row => Assert.Equal(locked.Contains(row.Id), row.Locked));
    Assert.All(p.All, row => Assert.Equal(table.Get(row.Id)!.WeenieClassId, row.Weenie));
}
```

(The comment names the fixture the implementer reuses from that file. The asserts are complete as written.)

Add to `GearQueryEngineTests`:

```csharp
[Fact]
public void LockedChip_KeepsOnlyLockedRows()
{
    var a = Row(1, "Axe") with { Locked = true };
    var b = Row(2, "Bow");
    IReadOnlyList<GearItemView> hits = GearQueryEngine.Run([a, b], GearQuery.Empty.With(GearChip.LockedOnly));
    Assert.Equal(new[] { 1u }, hits.Select(h => h.Id));
}
```

(`Row` is the file's existing row helper. If it has another name, use that.)

Add to `GearQueryJsonTests`: a saved filter holding `GearChip.LockedOnly` written and read back equal, with `"f":"locked"` in the written JSON. Add to `GearChipSuggesterTests`: `Assert.Contains(GearChip.LockedOnly, GearChipSuggester.Suggest("lo"))`. (Use the suggester's real entry-point name, the one the existing tests call.) Add to `GearChipTextTests`: `Label(GearChip.LockedOnly, _ => "")` is `"Locked"` and `Suggestion(GearChip.LockedOnly)` is `"Locked items"`.

- [ ] **Step 2: Run the tests and see them fail.** The Verify command fails to compile, because `ItemLocks`, `Locked`, `Weenie` and `LockedOnly` don't exist yet.

- [ ] **Step 3: Implement**

`src/AcDream.Core/Items/ItemLocks.cs`:

```csharp
namespace AcDream.Core.Items;

/// <summary>
/// The items the player has locked against loss: a locked item can't be sold,
/// given, dropped or salvaged. One set is shared by the Gear Deck (which saves
/// it per character) and the retail paths that refuse it.
/// </summary>
public sealed class ItemLocks
{
    private readonly HashSet<uint> _ids = [];

    /// <summary>Bumped on every change, for caches that follow the locks.</summary>
    public int Revision { get; private set; }
    public event Action? Changed;
    public IReadOnlyCollection<uint> Ids => _ids;

    public bool IsLocked(uint id) => id != 0u && _ids.Contains(id);

    /// <summary>Locks or unlocks one item; true when that changed anything.</summary>
    public bool Set(uint id, bool locked)
    {
        if (id == 0u) return false;
        bool changed = locked ? _ids.Add(id) : _ids.Remove(id);
        if (changed) Bump();
        return changed;
    }

    /// <summary>Makes the set exactly <paramref name="ids"/> (zeros ignored).</summary>
    public void Replace(IEnumerable<uint> ids)
    {
        var next = new HashSet<uint>(ids.Where(id => id != 0u));
        if (next.SetEquals(_ids)) return;
        _ids.Clear();
        _ids.UnionWith(next);
        Bump();
    }

    /// <summary>The locked item that stops <paramref name="id"/> leaving: itself, or for a pack the first
    /// locked item inside it; 0 when nothing is locked.</summary>
    public uint BlockerOf(ClientObjectTable objects, uint id)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (_ids.Count == 0 || id == 0u) return 0u;
        if (_ids.Contains(id)) return id;
        foreach (uint inside in objects.GetContainedItems(id))
            if (_ids.Contains(inside)) return inside;
        return 0u;
    }

    public static string Message(string name) => $"The {name} is locked.";

    private void Bump()
    {
        Revision++;
        Changed?.Invoke();
    }
}
```

`GearProjection.cs`: append two members to the record and thread `isLocked` through:

```csharp
public sealed record GearItemView(
    uint Id, string Name, GearTypeGroup Group, uint Material, float Workmanship,
    int Value, int Burden, int Stack, int StackMax,
    uint PackId, int PackIndex, int Slot, bool Worn, bool IsPack,
    bool Locked = false, uint Weenie = 0u);
```

`Build(GearInventoryModel model, ClientObjectTable objects, Func<ClientObject, string> displayName, Func<uint, bool>? isLocked = null)`. Pass `isLocked` to `View`, which sets `Locked: isLocked?.Invoke(o.ObjectId) ?? false, Weenie: o.WeenieClassId`.

`GearQuery.cs`: add `Locked` at the **end** of `GearField` (`…, Pack, Worn, Locked`) and `public static GearChip LockedOnly { get; } = new(GearField.Locked);`.

`GearQueryEngine.Run`: Worn stays special. Add `GearField.Locked => item.Locked,` to `Matches`.

`GearQueryJson.FieldKeys`: add `(GearField.Locked, "locked")`. `GearChipText.Label`: `GearField.Locked => "Locked",`. `Suggestion`: `GearField.Locked => "Locked items",`. `GearChipSuggester`, after the worn line: `if (t.Length >= 2 && StartsWith("locked", t)) picks.Add(GearChip.LockedOnly);`.

- [ ] **Step 4: Run the Verify command.** Expected: all pass. Fix any `switch` over `GearField` elsewhere in App that the compiler flags (`dotnet build` the App project), giving `Locked` the same handling as `Worn`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Core/Items tests/AcDream.Core.Tests/Items
git commit -m "Gear Deck: item locks set and the Locked chip

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 2: Retail paths refuse locked items

**Goal:** With an `ItemLocks` attached, dropping, giving, selling and salvaging a locked item (or a pack holding one) is refused with `"The {name} is locked."`, in the retail windows and the runtime entry points.

**Files:**
- Modify: `src/AcDream.Core/Items/ItemInteractionPolicy.cs` (`ItemPlacementPolicyInput` ~:132, `DecidePlacement` :340)
- Modify: `src/AcDream.Runtime/Gameplay/RuntimeItemInteraction.cs` (`TrySell` :333, `TryDropItemForAutomation` :542, `TryGiveItemForAutomation` :575, `TrySalvageItems` :609, `PlaceIn3D` :1585)
- Modify: `src/AcDream.Core/Items/VendorSellAcceptability.cs`, `src/AcDream.App/UI/Layout/VendorUiController.cs` (`EvaluateSellAcceptability` :1640)
- Modify: `src/AcDream.App/UI/Layout/SalvageUiController.cs` (`Bindings`, `CanAdd` :122, `AddItem` :133, `AddRecursive` :145)
- Test: `tests/AcDream.Core.Tests/Items/ItemInteractionPolicyTests.cs`, `tests/AcDream.Runtime.Tests/Gameplay/LockedItemTests.cs` (create), `tests/AcDream.App.Tests/UI/Layout/SalvageUiControllerTests.cs`, `tests/AcDream.App.Tests/UI/Layout/VendorUiControllerTests.cs`

**Acceptance Criteria:**
- [ ] `DecidePlacement` with `Locked: true` turns any DropToWorld / SplitToWorld / GiveToTarget / SellToVendor / StartSecureTrade decision into one Reject `"The X is locked."` (ReturnValue false). Placement into the player's own pack is unchanged.
- [ ] `RuntimeItemInteraction` with `Locks` set: `PlaceIn3D` passes `Locked`; `TryGiveItemForAutomation`, `TryDropItemForAutomation`, `TrySell` (any item in the list) and `TrySalvageItems` (any id) send nothing, return false and report the message once; with `Locks` null nothing changes
- [ ] Vendor: `EvaluateSellAcceptability` returns `VendorSellRejection.Locked` for a locked item, and `MessageFor(Locked)` is `"That item is locked"`. The drop path prints it, and pack staging skips locked children.
- [ ] Salvage: `CanAdd` is false for a locked id. `AddItem` on a locked id reports the message. A locked item inside an added pack is skipped and reported.
- [ ] All existing policy, vendor, salvage and runtime tests still pass

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ItemInteractionPolicy|FullyQualifiedName~LockedItemTests|FullyQualifiedName~SalvageUiController|FullyQualifiedName~VendorUiController|FullyQualifiedName~VendorSellAcceptability' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

In `ItemInteractionPolicyTests` (reuse its `Place`/`PlaceOnGround` helpers and its existing vendor/player/item fixtures):

```csharp
[Fact]
public void ALockedItem_IsRefused_ForEveryWayOfLosingIt()
{
    var item = /* the file's owned item fixture */;
    foreach (ItemPlacementPolicyInput input in new[]
             {
                 PlaceOnGround(item) with { Locked = true },
                 Place(item, /* vendor fixture */) with { Locked = true },
                 Place(item, /* player fixture */) with { Locked = true },
             })
    {
        ItemPlacementPolicyDecision d = ItemInteractionPolicy.DecidePlacement(input);
        Assert.False(d.ReturnValue);
        ItemPolicyAction only = Assert.Single(d.Actions);
        Assert.Equal(ItemPolicyActionKind.Reject, only.Kind);
        Assert.EndsWith("is locked.", only.Message);
    }
}

[Fact]
public void ALockedItem_StillGoesIntoTheBackpack()
{
    var item = /* the file's owned item fixture */;
    ItemPlacementPolicyDecision d = ItemInteractionPolicy.DecidePlacement(
        PlaceOnGround(item) with { TargetId = Player, Locked = true });
    Assert.Contains(d.Actions, a => a.Kind == ItemPolicyActionKind.PlaceInBackpack);
}
```

(The `/* … */` markers name fixtures that already exist in that file. Use them as they are.)

`tests/AcDream.Runtime.Tests/Gameplay/LockedItemTests.cs`. Build the harness the way `FullPackStackPickupTests.Harness` does: a player holding `Sword` and a side pack `Bag` holding `Gem`, an NPC `Npc` in the table, and recording lambdas for `sendGive`, `sendDrop`, `sendSell` and `sendSalvage`, with `systemMessage: Notices.Add`.

```csharp
[Fact]
public void Give_Drop_Sell_AndSalvage_RefuseALockedItem_WithOneLine()
{
    using var h = new Harness();
    h.Interaction.Locks = h.Locks;
    h.Locks.Set(Sword, true);

    Assert.False(h.Interaction.TryGiveItemForAutomation(Sword, Npc));
    Assert.False(h.Interaction.TryDropItemForAutomation(Sword));
    Assert.False(h.Interaction.TrySell(Vendor, [(1, Sword)]));
    Assert.False(h.Interaction.TrySalvageItems(Tool, [Sword]));

    Assert.Empty(h.Gives); Assert.Empty(h.Drops); Assert.Empty(h.Sells); Assert.Empty(h.Salvages);
    Assert.Equal(4, h.Notices.Count(n => n == "The Sword is locked."));
}

[Fact]
public void APackHoldingALockedItem_IsRefused_ByItsContent()
{
    using var h = new Harness();
    h.Interaction.Locks = h.Locks;
    h.Locks.Set(Gem, true);
    Assert.False(h.Interaction.TryDropItemForAutomation(Bag));
    Assert.Contains("The Gem is locked.", h.Notices);
}

[Fact]
public void WithoutLocks_NothingChanges()
{
    using var h = new Harness();
    Assert.True(h.Interaction.TryGiveItemForAutomation(Sword, Npc));
    Assert.Single(h.Gives);
}
```

`Harness` exposes `Locks = new ItemLocks()`, `Gives`, `Drops`, `Sells`, `Salvages` and `Notices`. Its `Vendor` and `Tool` are table objects; `Tool` is an owned `ItemType.TinkeringTool`. `sendDrop` is the `Action<uint>` constructor argument and `sendGive` is `Action<uint,uint,uint>`. `sendSell` and `sendSalvage` return true.

`SalvageUiControllerTests` (reuse the file's binding builder, adding `IsLocked: locks.IsLocked`):

```csharp
[Fact]
public void ALockedItem_CannotBeAdded_AndSaysWhy()
{
    // existing rig with a tool loaded and a suitable item `Item`
    locks.Set(Item, true);
    Assert.False(controller.CanAdd(Item));
    Assert.False(controller.AddItem(Item));
    Assert.Contains($"The {name} is locked.", reports);
}
```

`VendorUiControllerTests`: a locked owned item dropped on the vendor's sell list is not staged, and the system message contains `"That item is locked"`. Reuse the file's drop helper.

- [ ] **Step 2: Run the tests and see them fail.** Verify fails to compile (`Locked`, `Locks`, `IsLocked` are missing).

- [ ] **Step 3: Implement**

`ItemInteractionPolicy.cs`: append `bool Locked = false` as the last positional member of `ItemPlacementPolicyInput`. Rename the current `DecidePlacement` body to `private static ItemPlacementPolicyDecision DecidePlacementCore(in ItemPlacementPolicyInput input)` and add:

```csharp
private static readonly ItemPolicyActionKind[] LossKinds =
[
    ItemPolicyActionKind.DropToWorld, ItemPolicyActionKind.SplitToWorld, ItemPolicyActionKind.GiveToTarget,
    ItemPolicyActionKind.SellToVendor, ItemPolicyActionKind.StartSecureTrade,
];

/// <summary>Retail's placement rules; a locked item (<see cref="ItemPlacementPolicyInput.Locked"/>) is refused
/// any placement that would lose it: the ground, a give, a sale or a trade.</summary>
public static ItemPlacementPolicyDecision DecidePlacement(in ItemPlacementPolicyInput input)
{
    ItemPlacementPolicyDecision decision = DecidePlacementCore(input);
    if (!input.Locked || !decision.Actions.Any(a => Array.IndexOf(LossKinds, a.Kind) >= 0)) return decision;
    return Placement(false, RejectAction(ItemLocks.Message(NameOf(input.Item))));
}
```

`RuntimeItemInteraction.cs`:

```csharp
/// <summary>The player's locked items; null (the default) locks nothing.</summary>
public ItemLocks? Locks { get; set; }

/// <summary>True (and one chat line) when <paramref name="itemId"/>, or a locked item inside it, is locked.</summary>
public bool RefuseIfLocked(uint itemId)
{
    if (Locks is not { } locks || locks.BlockerOf(_objects, itemId) is not (var blocker and not 0u)) return false;
    ReportClientLocal(ItemLocks.Message(_objects.Get(blocker)?.GetAppropriateName() ?? "item"));
    return true;
}
```

The guards:
- `TryGiveItemForAutomation` and `TryDropItemForAutomation`: right after the existing argument/owned checks that return false, add `if (RefuseIfLocked(itemId)) return false;`.
- `TrySell`: after the null/empty check, `foreach ((int _, uint guid) in items) if (RefuseIfLocked(guid)) return false;`.
- `TrySalvageItems`: after the argument checks, `foreach (uint id in itemIds) if (RefuseIfLocked(id)) return false;`.
- `PlaceIn3D`: add `Locked: Locks?.BlockerOf(_objects, item.ObjectId) is > 0u` to the `ItemPlacementPolicyInput`. The policy then builds the message from the item's own name. When the blocker is a contained item, that names the pack, which is fine.

`VendorSellAcceptability.cs`: add `Locked` at the end of `VendorSellRejection`, plus `VendorSellRejection.Locked => "That item is locked",` in `MessageFor`. In `VendorUiController.EvaluateSellAcceptability`, after the null check: `if (_itemInteraction.Locks?.BlockerOf(_objects, itemGuid) is > 0u) return VendorSellRejection.Locked;`. `SellItemButtonPressed` and SellAll reach `TrySell`, which refuses.

`SalvageUiController.cs`: append `Func<uint, bool>? IsLocked = null` to `Bindings`. `CanAdd` returns false when `_bindings.IsLocked?.Invoke(itemId) == true`. Report from `AddItem` and `AddRecursive` without changing `CanAdd`'s silence:

```csharp
public bool AddItem(uint itemId)
{
    if (ReportIfLocked(itemId)) return false;
    if (!CanAdd(itemId)) return false;
    …
}

private bool ReportIfLocked(uint itemId)
{
    if (_bindings.IsLocked?.Invoke(itemId) != true) return false;
    _bindings.Report(ItemLocks.Message(_bindings.Objects.Get(itemId) is { } o ? _bindings.ResolveAppropriateName(o) : "item"));
    return true;
}
```

In `AddRecursive`, the child loop calls `if (ReportIfLocked(child)) continue;` before `AddRecursive(child, visited)`.

- [ ] **Step 4: Run the Verify command.** Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "Locks: retail drop, give, sell and salvage refuse a locked item

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 3: Settings, session and runtime ownership of locks, rules and combine-on-arrival

**Goal:** Save locked ids, pack rules and "combine on arrival" per character. Keep the shared `ItemLocks` in step with the settings, and wire the one `ItemLocks` into the runtime, vendor, salvage and trade paths. Tick the session every frame.

**Files:**
- Create: `src/AcDream.Core/Items/Gear/GearPackRule.cs`
- Modify: `src/AcDream.App/UI/Hud/GearDeckSettings.cs`, `src/AcDream.App/UI/Hud/GearDeckSession.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs` (:982 `TickSave`, `MountGearDeck` :3966, salvage bindings :5742, trade `AddToTrade` :5838)
- Test: `tests/AcDream.App.Tests/UI/Hud/GearDeckSessionTests.cs`, `tests/AcDream.App.Tests/UI/Hud/GearDeckSettingsTests.cs` (create if absent; otherwise extend the file that tests `GearDeckSettings.FromJson`)

**Acceptance Criteria:**
- [ ] `GearDeckSettings` round-trips `lockedIds` (sorted numbers), `packRules` (`{filter, pack, arrival, on}`) and `combineOnArrival`. Unreadable rule entries and unknown keys are kept on save. A wrong-typed key reads as its default.
- [ ] `WithLock(ids, bool)` and `WithRules(list)` helpers; `Equals` covers the new members
- [ ] `GearDeckSession(…, locks)`: a load replaces the locks with the saved ids, `Update` changing `LockedIds` replaces them, `EndSession` clears them
- [ ] The runtime owns one `ItemLocks`. It is set on `RuntimeItemInteraction.Locks` and passed to the salvage `IsLocked`. The trade window's `AddToTrade` refuses a locked item through `RefuseIfLocked`. The session is passed the same instance and ticked every frame.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearDeckSession|FullyQualifiedName~GearDeckSettings' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass; `dotnet build` of `src/AcDream.App` succeeds

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void LocksRulesAndCombine_RoundTrip_AndUnreadableRulesAreKept()
{
    string json = """
        {"version":1,"lockedIds":[7,3],"combineOnArrival":true,
         "packRules":[{"filter":"Salvage","pack":177,"arrival":true,"on":true},{"filter":5}],
         "future":"x"}
        """;
    GearDeckSettings s = GearDeckSettings.FromJson(json);
    Assert.Equal(new uint[] { 3, 7 }, s.LockedIds);
    Assert.True(s.CombineOnArrival);
    Assert.Equal(new[] { new GearPackRule("Salvage", 177u, true, true) }, s.PackRules);
    string back = s.ToJson();
    Assert.Contains("\"future\":\"x\"", back);
    Assert.Contains("{\"filter\":5}", back);   // the unreadable rule comes back as it was
    Assert.Equal(s, GearDeckSettings.FromJson(back));
}

[Fact]
public void WrongTypedLockedIds_ReadAsNone_AndTheRestStillLoads()
{
    GearDeckSettings s = GearDeckSettings.FromJson("""{"lockedIds":"x","folded":true}""");
    Assert.Empty(s.LockedIds);
    Assert.True(s.Folded);
}

[Fact]
public void WithLock_AddsAndRemovesSorted()
{
    GearDeckSettings s = GearDeckSettings.Default.WithLock([9u, 2u], true).WithLock([9u], false);
    Assert.Equal(new uint[] { 2 }, s.LockedIds);
}
```

`GearDeckSessionTests` (reuse the file's fake store and gameplay flag):

```csharp
[Fact]
public void TheLocksFollowTheSettings_ThroughLoadUpdateAndEnd()
{
    var store = new FakeStore { ["Toon"] = """{"lockedIds":[5]}""" };
    var locks = new ItemLocks();
    var session = new GearDeckSession(store, () => "Toon", () => true, locks: locks);
    session.Tick();
    Assert.True(locks.IsLocked(5));
    session.Update(s => s.WithLock([6u], true));
    Assert.True(locks.IsLocked(6));
    session.EndSession();
    Assert.Empty(locks.Ids);
}
```

(`FakeStore` is the session test file's existing store double. Match its real name and indexer.)

- [ ] **Step 2: Run the tests and see them fail.**

- [ ] **Step 3: Implement**

`src/AcDream.Core/Items/Gear/GearPackRule.cs`:

```csharp
namespace AcDream.Core.Items.Gear;

/// <summary>Send what a saved filter (by name) matches to a pack; "on arrival" also applies it to new items.</summary>
public sealed record GearPackRule(string Filter, uint PackId, bool OnArrival, bool Enabled = true);
```

`GearDeckSettings`: new init members.

```csharp
/// <summary>Locked object ids, ascending.</summary>
public IReadOnlyList<uint> LockedIds { get; init; } = [];
public IReadOnlyList<GearPackRule> PackRules { get; init; } = [];
private JsonArray? UnreadRules { get; init; }
public bool CombineOnArrival { get; init; }

public GearDeckSettings WithLock(IEnumerable<uint> ids, bool locked)
{
    var set = new SortedSet<uint>(LockedIds);
    foreach (uint id in ids) { if (id == 0u) continue; if (locked) set.Add(id); else set.Remove(id); }
    return this with { LockedIds = set.ToArray() };
}

public GearDeckSettings WithRules(IReadOnlyList<GearPackRule> rules) => this with { PackRules = rules };
```

In `FromJson`, read `lockedIds` as numbers only: `o["lockedIds"] is JsonArray a ? a.Select(n => n is JsonValue v && v.TryGetValue(out uint id) ? id : 0u).Where(id => id != 0u).Distinct().Order().ToArray() : []`. Read `combineOnArrival` with `Read<bool>`. Read `packRules` like `savedFilters`, with `TryReadRule(JsonNode?, out GearPackRule?)`. A rule needs a string `filter` and a number `pack`; `arrival` and `on` are optional bools defaulting to false and true. Anything else goes to `UnreadRules`. Add the three keys to the removed-from-`extra` list (`packRules` only when it was an array, as with `savedFilters`). `ToJson` writes `lockedIds` (when not empty), `combineOnArrival`, and `packRules` (read rules, then the unread ones, when either is non-empty), each rule as `{"filter":…, "pack":…, "arrival":…, "on":…}`. `Equals` and `GetHashCode` add `LockedIds.SequenceEqual`, `PackRules.SequenceEqual` and `CombineOnArrival`.

`GearDeckSession`: add an optional last constructor parameter `AcDream.Core.Items.ItemLocks? locks = null`. After every place `Settings` is assigned (the load in `Tick`, `Update`, `EndSession`), call `SyncLocks()`:

```csharp
private void SyncLocks() => locks?.Replace(Settings.LockedIds);
```

`RetailUiRuntime`:
- Add a field `private readonly ItemLocks _itemLocks = new();`.
- In `MountGearDeck`, before the `try`, set `items.Locks = _itemLocks;`. Pass `locks: _itemLocks` to the `GearDeckSession`.
- At :982, replace `_gearDeckSession?.TickSave();` with `_gearDeckSession?.Tick();`. `Tick` already ends with `TickSave`. It ticks while the deck is closed, so locks load at login.
- Salvage bindings (:5742): add the named argument `IsLocked: _itemLocks.IsLocked`.
- Trade (:5838): `AddToTrade: item => { if (_bindings.Inventory.ItemInteraction?.RefuseIfLocked(item) == true) return; _bindings.Options.CommandBus().Publish(new AddToTradeRuntimeCmd(item)); },`.

- [ ] **Step 4: Run the Verify command and build the App project.** Expected: pass + build OK.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "Gear Deck: save locks, pack rules and combine-on-arrival; one lock set for the runtime

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 4: The organising planners

**Goal:** Add pure Core planners for combine, tidy and pack rules that return `GearMove` lists.

**Files:**
- Create: `src/AcDream.Core/Items/Gear/GearMove.cs`, `StackCombinePlanner.cs`, `PackTidyPlanner.cs`, `PackRulePlanner.cs`
- Test: `tests/AcDream.Core.Tests/Items/Gear/StackCombinePlannerTests.cs`, `PackTidyPlannerTests.cs`, `PackRulePlannerTests.cs` (create)

**Acceptance Criteria:**
- [ ] Combine: per weenie with `StackMax > 1`, partial stacks merge emptiest into fullest. Amounts never overflow `StackMax`. A locked stack is never a source but can be a target. `only` limits it to groups holding one of those ids. Worn rows and pack rows are ignored.
- [ ] Tidy: emits `Put(item, pack, i)` only for items not already at `i` when replayed in order. The final order is type group, then name (case-insensitive), then workmanship descending, then old slot. An already-tidy pack gives no moves.
- [ ] Rules: in order, first match wins. Worn, pack, locked and already-claimed rows are skipped. Items already in the target count as claimed and don't move. Each move is `Put(item, pack, -1)` (end). The planner stops for a pack at its free room and reports `Full` counts. Disabled rules, empty queries and missing packs are skipped. `only` limits it to those ids.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~StackCombinePlanner|FullyQualifiedName~PackTidyPlanner|FullyQualifiedName~PackRulePlanner' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

A shared row builder (put it in each test file as a private static):

```csharp
private static GearItemView Row(uint id, uint weenie = 0, int stack = 1, int max = 1, uint pack = 1, int packIndex = 0,
    int slot = 0, bool locked = false, string name = "x", GearTypeGroup group = GearTypeGroup.Other, float work = 0f,
    bool worn = false, bool isPack = false) =>
    new(id, name, group, 0u, work, 0, 0, stack, max, pack, packIndex, slot, worn, isPack, locked, weenie);
```

`StackCombinePlannerTests`:

```csharp
[Fact]
public void MergesEmptiestIntoFullest_WithoutOverflow()
{
    var rows = new[] { Row(1, 9, 50, 100, slot: 0), Row(2, 9, 70, 100, slot: 1), Row(3, 9, 40, 100, slot: 2) };
    Assert.Equal(new[] { GearMove.Merge(3, 2, 30), GearMove.Merge(3, 1, 10) }, StackCombinePlanner.Plan(rows));
}

[Fact]
public void ALockedStack_IsFilled_ButNeverEmptied()
{
    var rows = new[] { Row(1, 9, 10, 100, locked: true), Row(2, 9, 60, 100, slot: 1) };
    Assert.Empty(StackCombinePlanner.Plan(rows));   // 2 is fullest; 1 is the only source and it is locked
    var filled = new[] { Row(1, 9, 60, 100, locked: true), Row(2, 9, 10, 100, slot: 1) };
    Assert.Equal(new[] { GearMove.Merge(2, 1, 10) }, StackCombinePlanner.Plan(filled));
}

[Fact]
public void IgnoresFullUnstackableWornAndPackRows_AndOnlyLimitsTheGroups()
{
    var rows = new[]
    {
        Row(1, 9, 100, 100), Row(2, 9, 5, 100, slot: 1), Row(3, 9, 5, 100, slot: 2),
        Row(4, 8, 1, 1, slot: 3), Row(5, 7, 5, 10, slot: 4), Row(6, 7, 5, 10, worn: true),
        Row(7, 6, 5, 10, slot: 5), Row(8, 6, 5, 10, slot: 6),
    };
    Assert.Equal(new[] { GearMove.Merge(3, 2, 5) }, StackCombinePlanner.Plan(rows, only: [2]));
    Assert.Equal(new[] { GearMove.Merge(3, 2, 5), GearMove.Merge(8, 7, 5) }, StackCombinePlanner.Plan(rows));
}
```

`PackTidyPlannerTests`:

```csharp
[Fact]
public void ReordersByGroupNameThenWorkmanship_MovingOnlyWhatIsOutOfPlace()
{
    var rows = new[]
    {
        Row(1, name: "Zircon", group: GearTypeGroup.Gems, slot: 0),
        Row(2, name: "Steel", group: GearTypeGroup.Salvage, work: 5f, slot: 1),
        Row(3, name: "steel", group: GearTypeGroup.Salvage, work: 9f, slot: 2),
        Row(4, name: "Amber", group: GearTypeGroup.Gems, slot: 3),
    };
    // Target: 3 (steel w9), 2 (Steel w5), 4 (Amber), 1 (Zircon).
    Assert.Equal(new[] { GearMove.Put(3, 1, 0), GearMove.Put(2, 1, 1), GearMove.Put(4, 1, 2) },
        PackTidyPlanner.Plan(1u, rows));
}

[Fact]
public void ATidyPack_NeedsNoMoves()
{
    var rows = new[] { Row(1, name: "A", slot: 0), Row(2, name: "B", slot: 1) };
    Assert.Empty(PackTidyPlanner.Plan(1u, rows));
}
```

`PackRulePlannerTests`:

```csharp
private static readonly GearPack Main = new(1u, 102, true), Side = new(2u, 3, false);

private static (GearPackRule, GearQuery) Rule(uint pack, GearChip chip, bool on = true) =>
    (new GearPackRule("f", pack, false, on), GearQuery.Empty.With(chip));

[Fact]
public void FirstMatchingRuleWins_AndSkipsWornPacksLockedAndInPlace()
{
    var rows = new[]
    {
        Row(10, group: GearTypeGroup.Salvage, pack: 1, slot: 0),
        Row(11, group: GearTypeGroup.Salvage, pack: 1, slot: 1, locked: true),
        Row(12, group: GearTypeGroup.Salvage, pack: 2, packIndex: 1, slot: 0),
        Row(13, group: GearTypeGroup.Gems, pack: 1, slot: 2),
        Row(14, group: GearTypeGroup.Salvage, worn: true, pack: 0, packIndex: -1, slot: -1),
    };
    var rules = new[] { Rule(2u, GearChip.OfType(GearTypeGroup.Salvage)), Rule(1u, GearChip.OfType(GearTypeGroup.Salvage)) };
    GearRulePlan plan = PackRulePlanner.Plan(rows, [Main, Side], rules);
    Assert.Equal(new[] { GearMove.Put(10, 2, -1) }, plan.Moves);
    Assert.Empty(plan.Full);
}

[Fact]
public void AFullPack_TakesWhatFits_AndCountsTheRest()
{
    var rows = Enumerable.Range(0, 4).Select(i => Row(20u + (uint)i, group: GearTypeGroup.Gems, pack: 1, slot: i))
        .Append(Row(30, pack: 2, packIndex: 1, slot: 0)).Append(Row(31, pack: 2, packIndex: 1, slot: 1)).ToArray();
    GearRulePlan plan = PackRulePlanner.Plan(rows, [Main, Side], [Rule(2u, GearChip.OfType(GearTypeGroup.Gems))]);
    Assert.Single(plan.Moves);                       // Side holds 2 of 3
    Assert.Equal(3, plan.Full[2u]);                  // three gems stayed where they were
}

[Fact]
public void DisabledEmptyAndMissingPackRules_AreSkipped_AndOnlyLimitsTheItems()
{
    var rows = new[] { Row(40, group: GearTypeGroup.Gems, pack: 1, slot: 0), Row(41, group: GearTypeGroup.Gems, pack: 1, slot: 1) };
    var rules = new (GearPackRule, GearQuery)[]
    {
        Rule(2u, GearChip.OfType(GearTypeGroup.Gems), on: false),
        (new GearPackRule("e", 2u, false), GearQuery.Empty),
        Rule(99u, GearChip.OfType(GearTypeGroup.Gems)),
        Rule(2u, GearChip.OfType(GearTypeGroup.Gems)),
    };
    Assert.Equal(new[] { GearMove.Put(41, 2, -1) }, PackRulePlanner.Plan(rows, [Main, Side], rules, only: [41]).Moves);
}
```

- [ ] **Step 2: Run the tests and see them fail.**

- [ ] **Step 3: Implement**

`GearMove.cs`:

```csharp
namespace AcDream.Core.Items.Gear;

public enum GearMoveKind { Merge, Split, Put }

/// <summary>
/// One organising move. Merge: <see cref="Item"/> into stack <see cref="Target"/>, <see cref="Amount"/> of it.
/// Split: <see cref="Amount"/> off stack <see cref="Item"/> into container <see cref="Target"/> at <see cref="Slot"/>.
/// Put: <see cref="Item"/> into container <see cref="Target"/> at <see cref="Slot"/>; -1 is the end of the pack.
/// </summary>
public sealed record GearMove(GearMoveKind Kind, uint Item, uint Target, int Amount, int Slot)
{
    public static GearMove Merge(uint from, uint to, int amount) => new(GearMoveKind.Merge, from, to, amount, -1);
    public static GearMove Split(uint stack, int amount, uint container, int slot) => new(GearMoveKind.Split, stack, container, amount, slot);
    public static GearMove Put(uint item, uint container, int slot) => new(GearMoveKind.Put, item, container, 0, slot);
}
```

`StackCombinePlanner.cs`:

```csharp
namespace AcDream.Core.Items.Gear;

/// <summary>The merges that combine every partial stack of a kind: the emptiest into the fullest first.
/// A locked stack is never emptied (its id, and so its lock, would vanish); it can still be filled.</summary>
public static class StackCombinePlanner
{
    private sealed class Stack(GearItemView row) { public GearItemView Row = row; public int Count = row.Stack; }

    public static IReadOnlyList<GearMove> Plan(IEnumerable<GearItemView> rows, IReadOnlyCollection<uint>? only = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var moves = new List<GearMove>();
        IEnumerable<IGrouping<uint, GearItemView>> groups = rows
            .Where(r => !r.Worn && !r.IsPack && r.Weenie != 0u && r.StackMax > 1 && r.Stack > 0 && r.Stack < r.StackMax)
            .GroupBy(r => r.Weenie);
        foreach (IGrouping<uint, GearItemView> group in groups)
        {
            if (only is not null && !group.Any(r => only.Contains(r.Id))) continue;
            List<Stack> stacks = group.OrderByDescending(r => r.Stack).ThenBy(r => r.PackIndex).ThenBy(r => r.Slot)
                .Select(r => new Stack(r)).ToList();
            int t = 0, s = stacks.Count - 1;
            while (t < s)
            {
                Stack target = stacks[t], source = stacks[s];
                if (source.Row.Locked) { s--; continue; }
                int room = target.Row.StackMax - target.Count;
                if (room <= 0) { t++; continue; }
                int amount = Math.Min(room, source.Count);
                moves.Add(GearMove.Merge(source.Row.Id, target.Row.Id, amount));
                target.Count += amount;
                source.Count -= amount;
                if (source.Count == 0) s--;
            }
        }
        return moves;
    }
}
```

`PackTidyPlanner.cs`:

```csharp
namespace AcDream.Core.Items.Gear;

/// <summary>The puts that reorder one pack by type group, then name, then workmanship (best first).
/// Each put takes the next item to its place; places before it are already right, so it only moves up.</summary>
public static class PackTidyPlanner
{
    public static IReadOnlyList<GearMove> Plan(uint packId, IReadOnlyList<GearItemView> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        List<GearItemView> order = rows.OrderBy(r => (int)r.Group)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(r => r.Workmanship)
            .ThenBy(r => r.Slot).ToList();
        var now = rows.ToList();
        var moves = new List<GearMove>();
        for (int i = 0; i < order.Count; i++)
        {
            int at = now.IndexOf(order[i]);
            if (at == i) continue;
            moves.Add(GearMove.Put(order[i].Id, packId, i));
            now.RemoveAt(at);
            now.Insert(i, order[i]);
        }
        return moves;
    }
}
```

`PackRulePlanner.cs`:

```csharp
namespace AcDream.Core.Items.Gear;

/// <summary>The moves a rule run makes, and per target pack how many matches stayed put because it was full.</summary>
public sealed record GearRulePlan(IReadOnlyList<GearMove> Moves, IReadOnlyDictionary<uint, int> Full);

/// <summary>Applies pack rules in order: the first rule an item matches decides its pack. Worn items, packs and
/// locked items never move; an item already in its rule's pack is left there.</summary>
public static class PackRulePlanner
{
    public static GearRulePlan Plan(IReadOnlyList<GearItemView> rows, IReadOnlyList<GearPack> packs,
        IReadOnlyList<(GearPackRule Rule, GearQuery Query)> rules, IReadOnlyCollection<uint>? only = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(rules);
        var free = new Dictionary<uint, int>();
        foreach (GearPack p in packs) free[p.Id] = p.Capacity - rows.Count(r => !r.Worn && !r.IsPack && r.PackId == p.Id);
        var claimed = new HashSet<uint>();
        var moves = new List<GearMove>();
        var full = new Dictionary<uint, int>();
        foreach ((GearPackRule rule, GearQuery query) in rules)
        {
            if (!rule.Enabled || query.IsEmpty || !free.ContainsKey(rule.PackId)) continue;
            foreach (GearItemView row in GearQueryEngine.Run(rows, query with { Sort = GearSort.Default }))
            {
                if (row.Worn || row.IsPack || row.Locked || !claimed.Add(row.Id)) continue;
                if (only is not null && !only.Contains(row.Id)) continue;
                if (row.PackId == rule.PackId) continue;
                if (free[rule.PackId] <= 0)
                {
                    full[rule.PackId] = full.GetValueOrDefault(rule.PackId) + 1;
                    continue;
                }
                moves.Add(GearMove.Put(row.Id, rule.PackId, -1));
                free[rule.PackId]--;
                if (free.ContainsKey(row.PackId)) free[row.PackId]++;
            }
        }
        return new GearRulePlan(moves, full);
    }
}
```

- [ ] **Step 4: Run the Verify command.** Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Core/Items/Gear tests/AcDream.Core.Tests/Items/Gear
git commit -m "Gear Deck: combine, tidy and pack-rule planners

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 5: The move queue

**Goal:** Add `GearMoveQueue`, which sends moves one at a time over a channel (send when free, done on the server's confirm, stop on failure, timeout or cancel), with the runtime channel, the sender and a `Split` action.

**Files:**
- Create: `src/AcDream.App/UI/Hud/GearMoveQueue.cs`
- Modify: `src/AcDream.App/UI/Hud/IGearItemActions.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/GearMoveQueueTests.cs`, `tests/AcDream.App.Tests/UI/Hud/GearMoveSenderTests.cs` (create)

**Acceptance Criteria:**
- [ ] `Start` refuses while running (one chat line) and refuses an empty list. It sends the first move at once when the channel can send.
- [ ] A move counts as done only on `Completed` for its item id. Other ids are ignored. The next move goes out on a later `Tick` when `CanSend`.
- [ ] `Failed` for the waiting id stops the queue. A sender `Refused` stops it with `"{Label} stopped at {Done} of {Total}."`. Waiting `TimeoutSeconds` (2.0) stops it with `"{Label} stopped: no answer from the server."`. `Cancel` stops it silently.
- [ ] `Skipped` moves count as done without waiting. When the last move is done, `IsRunning` is false and `Changed` has fired.
- [ ] Sender: Put to the slot the item already holds → Skipped. Put with slot -1 resolves to the end of the target. Put that the container can't take → Refused with the placement policy's text. Merge with a missing source or target → Skipped. Split → `IGearItemActions.Split`.
- [ ] `IGearItemActions.Split` is a default member returning false (the existing fakes compile unchanged). `RuntimeGearItemActions.Split` calls `TrySplitToContainer`, and `LoggedGearItemActions` logs it.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearMoveQueue|FullyQualifiedName~GearMoveSender|FullyQualifiedName~AcDream.App.Tests.UI.Hud.Gear' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

`GearMoveQueueTests.cs`:

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearMoveQueueTests
{
    private sealed class Channel : IGearMoveChannel
    {
        public bool CanSend { get; set; } = true;
        public event Action<uint>? Completed;
        public event Action<uint>? Failed;
        public void Complete(uint id) => Completed?.Invoke(id);
        public void Fail(uint id) => Failed?.Invoke(id);
    }

    private sealed class Sender : IGearMoveSender
    {
        public readonly List<GearMove> Sent = [];
        public Func<GearMove, GearMoveResult> Result = _ => GearMoveResult.Sent;
        public GearMoveResult Send(GearMove move) { GearMoveResult r = Result(move); if (r == GearMoveResult.Sent) Sent.Add(move); return r; }
    }

    private sealed class Rig
    {
        public readonly Channel Channel = new();
        public readonly Sender Sender = new();
        public readonly List<string> Lines = [];
        public double Now;
        public readonly GearMoveQueue Queue;
        public Rig() => Queue = new GearMoveQueue(Channel, Sender, () => Now, Lines.Add);
    }

    private static readonly GearMove[] Three = [GearMove.Put(1, 9, 0), GearMove.Put(2, 9, 1), GearMove.Put(3, 9, 2)];

    [Fact]
    public void SendsOneAtATime_AndFinishesOnTheLastConfirm()
    {
        var rig = new Rig();
        Assert.True(rig.Queue.Start("Tidying", Three));
        Assert.Single(rig.Sender.Sent);
        rig.Queue.Tick();
        Assert.Single(rig.Sender.Sent);                // still waiting for item 1
        rig.Channel.Complete(77);                      // someone else's request
        rig.Queue.Tick();
        Assert.Single(rig.Sender.Sent);
        rig.Channel.Complete(1);
        rig.Channel.CanSend = false;
        rig.Queue.Tick();
        Assert.Single(rig.Sender.Sent);                // not free yet
        rig.Channel.CanSend = true;
        rig.Queue.Tick();
        Assert.Equal(2, rig.Sender.Sent.Count);
        rig.Channel.Complete(2); rig.Queue.Tick();
        rig.Channel.Complete(3); rig.Queue.Tick();
        Assert.False(rig.Queue.IsRunning);
        Assert.Equal(3, rig.Queue.Done);
        Assert.Empty(rig.Lines);
    }

    [Fact]
    public void AFailure_Stops_WithoutALineOfItsOwn()
    {
        var rig = new Rig();
        rig.Queue.Start("Tidying", Three);
        rig.Channel.Fail(1);
        rig.Queue.Tick();
        Assert.False(rig.Queue.IsRunning);
        Assert.Single(rig.Sender.Sent);
        Assert.Empty(rig.Lines);                       // the runtime already printed the server's message
    }

    [Fact]
    public void ATimeout_Stops_WithALine()
    {
        var rig = new Rig();
        rig.Queue.Start("Tidying", Three);
        rig.Now = GearMoveQueue.TimeoutSeconds - 0.01;
        rig.Queue.Tick();
        Assert.True(rig.Queue.IsRunning);
        rig.Now = GearMoveQueue.TimeoutSeconds;
        rig.Queue.Tick();
        Assert.False(rig.Queue.IsRunning);
        Assert.Equal(["Tidying stopped: no answer from the server."], rig.Lines);
    }

    [Fact]
    public void ARefusal_Stops_AndSaysWhere_AndSkipsCountAsDone()
    {
        var rig = new Rig();
        rig.Sender.Result = m => m.Item switch { 1 => GearMoveResult.Skipped, 2 => GearMoveResult.Refused, _ => GearMoveResult.Sent };
        rig.Queue.Start("Combining", Three);
        Assert.False(rig.Queue.IsRunning);
        Assert.Equal(["Combining stopped at 1 of 3."], rig.Lines);
    }

    [Fact]
    public void Cancel_IsSilent_AndStartRefusesWhileRunning()
    {
        var rig = new Rig();
        rig.Queue.Start("Tidying", Three);
        Assert.False(rig.Queue.Start("Combining", Three));
        Assert.Equal(["Tidying is still running."], rig.Lines);
        rig.Queue.Cancel();
        Assert.False(rig.Queue.IsRunning);
        Assert.False(rig.Queue.Start("Combining", []));
        Assert.True(rig.Queue.Start("Combining", Three));
    }
}
```

`GearMoveSenderTests.cs`: build a table (player with `ItemsCapacity = 102`, items A at slot 0 and B at slot 1, a side pack with `ItemsCapacity = 1` already holding one item) and a recording `IGearItemActions` fake that overrides `Split`. Assert:
- `Put(A, Player, 0)` → `Skipped`, nothing sent.
- `Put(A, Player, -1)` → `Sent`, recorded as `put A Player 2`. The end of the pack is its contained count, 2.
- `Put(A, FullPack, 0)` → `Refused`, with one report from `InventoryContainerPlacementPolicy.ComposeClientLocal`.
- `Merge(Missing, A, 1)` → `Skipped`.
- `Split(A, 3, Player, 1)` → `Sent`, recorded as `split A Player 1 3`.

- [ ] **Step 2: Run the tests and see them fail.**

- [ ] **Step 3: Implement**

`IGearItemActions.cs`: add to the interface

```csharp
/// <summary>Splits <paramref name="amount"/> off a stack into a container slot. Not every caller can; false by default.</summary>
bool Split(uint stack, uint container, int placement, int amount) => false;
```

Add `public bool Split(uint stack, uint container, int placement, int amount) => items.TrySplitToContainer(stack, container, (uint)Math.Max(0, placement), (uint)Math.Max(0, amount));` to `RuntimeGearItemActions`. Add `Note(inner.Split(stack, container, placement, amount), $"split 0x{stack:X8} 0x{container:X8} {placement} {amount}")` to `LoggedGearItemActions`.

`GearMoveQueue.cs`:

```csharp
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Hud;

/// <summary>When the game can take another inventory request, and which item's request the server settled.</summary>
internal interface IGearMoveChannel
{
    /// <summary>No request is pending and nothing is busy. Never prints anything.</summary>
    bool CanSend { get; }
    event Action<uint>? Completed;
    event Action<uint>? Failed;
}

internal enum GearMoveResult { Sent, Skipped, Refused }

internal interface IGearMoveSender { GearMoveResult Send(GearMove move); }

/// <summary>
/// Sends a planner's moves one at a time: the next goes out once the server confirmed the last and the game is
/// free. It stops at the first failure (the runtime has already printed the server's message), at a refusal,
/// after <see cref="TimeoutSeconds"/> without an answer (a request that never settles blocks every later one),
/// or on <see cref="Cancel"/>. One queue runs at a time.
/// </summary>
internal sealed class GearMoveQueue : IDisposable
{
    public const double TimeoutSeconds = 2.0;

    private readonly IGearMoveChannel _channel;
    private readonly IGearMoveSender _sender;
    private readonly Func<double> _clock;
    private readonly Action<string>? _report;
    private IReadOnlyList<GearMove> _moves = [];
    private int _next;
    private uint _waiting;
    private double _sentAt;

    public GearMoveQueue(IGearMoveChannel channel, IGearMoveSender sender, Func<double> clock, Action<string>? report = null)
    {
        _channel = channel;
        _sender = sender;
        _clock = clock;
        _report = report;
        _channel.Completed += OnCompleted;
        _channel.Failed += OnFailed;
    }

    public bool IsRunning { get; private set; }
    public string Label { get; private set; } = "";
    public int Done { get; private set; }
    public int Total => _moves.Count;
    /// <summary>Bumped on every change, for the deck's cached progress text.</summary>
    public int Version { get; private set; }
    public event Action? Changed;

    public bool Start(string label, IReadOnlyList<GearMove> moves)
    {
        ArgumentNullException.ThrowIfNull(moves);
        if (IsRunning)
        {
            _report?.Invoke($"{Label} is still running.");
            return false;
        }
        if (moves.Count == 0) return false;
        Label = label;
        _moves = moves;
        _next = 0;
        _waiting = 0u;
        Done = 0;
        IsRunning = true;
        Bump();
        Tick();
        return true;
    }

    public void Cancel() => Stop(null);

    public void Tick()
    {
        if (!IsRunning) return;
        if (_waiting != 0u)
        {
            if (_clock() - _sentAt >= TimeoutSeconds) Stop($"{Label} stopped: no answer from the server.");
            return;
        }
        while (IsRunning && _waiting == 0u && _next < _moves.Count && _channel.CanSend)
        {
            GearMove move = _moves[_next];
            switch (_sender.Send(move))
            {
                case GearMoveResult.Sent:
                    _waiting = move.Item;
                    _sentAt = _clock();
                    _next++;
                    break;
                case GearMoveResult.Skipped:
                    _next++;
                    Done++;
                    Bump();
                    break;
                default:
                    Stop($"{Label} stopped at {Done} of {Total}.");
                    return;
            }
        }
        if (IsRunning && _waiting == 0u && _next >= _moves.Count) Stop(null);
    }

    private void OnCompleted(uint id)
    {
        if (!IsRunning || id != _waiting) return;
        _waiting = 0u;
        Done++;
        Bump();
    }

    private void OnFailed(uint id)
    {
        if (IsRunning && id == _waiting) Stop(null);
    }

    private void Stop(string? line)
    {
        if (!IsRunning) return;
        IsRunning = false;
        _waiting = 0u;
        if (line is not null) _report?.Invoke(line);
        Bump();
    }

    private void Bump()
    {
        Version++;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _channel.Completed -= OnCompleted;
        _channel.Failed -= OnFailed;
    }
}

/// <summary>Turns a <see cref="GearMove"/> into the retail inventory's request, re-checking the table as it goes:
/// a move the game already made is skipped, a pack that can't take the item is refused with retail's text.</summary>
internal sealed class GearMoveSender(ClientObjectTable objects, Func<uint> playerGuid, IGearItemActions actions) : IGearMoveSender
{
    public GearMoveResult Send(GearMove move)
    {
        if (objects.Get(move.Item) is not { } item) return GearMoveResult.Skipped;
        switch (move.Kind)
        {
            case GearMoveKind.Merge:
                if (objects.Get(move.Target) is null) return GearMoveResult.Skipped;
                return actions.Merge(move.Item, move.Target) ? GearMoveResult.Sent : GearMoveResult.Refused;
            case GearMoveKind.Split:
                return actions.Split(move.Item, move.Target, move.Slot, move.Amount) ? GearMoveResult.Sent : GearMoveResult.Refused;
            default:
                IReadOnlyList<uint> inside = objects.GetContainedItems(move.Target);
                int slot = move.Slot < 0 ? inside.Count : move.Slot;
                if (item.ContainerId == move.Target && slot < inside.Count && inside[slot] == move.Item) return GearMoveResult.Skipped;
                uint player = playerGuid();
                InventoryContainerPlacementRejection refusal =
                    InventoryContainerPlacementPolicy.Evaluate(objects, move.Item, move.Target, player);
                if (refusal != InventoryContainerPlacementRejection.None)
                {
                    if (InventoryContainerPlacementPolicy.ComposeClientLocal(refusal, item, objects.Get(move.Target), player) is { } text)
                        actions.Report(text);
                    return GearMoveResult.Refused;
                }
                return actions.PutInContainer(move.Item, move.Target, slot, fromGround: false)
                    ? GearMoveResult.Sent : GearMoveResult.Refused;
        }
    }
}

/// <summary><see cref="IGearMoveChannel"/> over the runtime's inventory transaction state.</summary>
internal sealed class RuntimeGearMoveChannel : IGearMoveChannel, IDisposable
{
    private readonly RuntimeItemInteraction _items;
    private readonly InventoryTransactionState _state;

    public RuntimeGearMoveChannel(RuntimeItemInteraction items)
    {
        _items = items;
        _state = items.RuntimeTransactions.Inventory;
        _state.RequestCompleted += OnCompleted;
        _state.RequestFailed += OnFailed;
    }

    public bool CanSend => _items.CanMakeInventoryRequest;
    public event Action<uint>? Completed;
    public event Action<uint>? Failed;

    private void OnCompleted(PendingInventoryRequest r) => Completed?.Invoke(r.ItemId);
    private void OnFailed(PendingInventoryRequest r, uint _) => Failed?.Invoke(r.ItemId);

    public void Dispose()
    {
        _state.RequestCompleted -= OnCompleted;
        _state.RequestFailed -= OnFailed;
    }
}
```

Note: when the source of a Put already sits after the target slot in the same container, the "already there" check compares `inside[slot]`, which is enough for tidy, because tidy only moves items up. If `InventoryContainerPlacementPolicy.Evaluate`/`ComposeClientLocal` have other exact signatures, follow `GearDropDispatcher.ResolveLegality`/`ReportRefusal`, which call them.

- [ ] **Step 4: Run the Verify command.** Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud tests/AcDream.App.Tests/UI/Hud
git commit -m "Gear Deck: move queue (one at a time, confirm, timeout, cancel) and split action

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 6: Deck queue wiring, footer, Combine, group drag

**Goal:** The deck holds the queue and the locks. Its footer shows a Combine button and the queue's progress with Stop. A manual drag cancels a running queue, and dropping a dragged selection on a pack moves the whole selection through the queue.

**Files:**
- Create: `src/AcDream.App/UI/Hud/UiGearDeck.Organise.cs`
- Modify: `src/AcDream.App/UI/Hud/GearDeckGeometry.cs`, `UiGearDeck.cs`, `UiGearDeck.Draw.cs`, `UiGearDeck.Input.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs` (`MountGearDeck`, the per-frame tick at :982, logout at :1458)
- Test: `tests/AcDream.App.Tests/UI/Hud/GearDeckGeometryTests.cs`, `tests/AcDream.App.Tests/UI/Hud/UiGearDeckOrganiseTests.cs` (create)

**Acceptance Criteria:**
- [ ] `GearDeckBindings` gains `GearMoveQueue? Queue = null` and `ItemLocks? Locks = null` (appended). The model and projection rebuild when `Locks.Revision` changes, and `isLocked` feeds the projection.
- [ ] Geometry: `GearDeckLayoutInput` gains `CombineButton = false, Queue = false`. `CombineButton` (64 pt) sits left of the salvage button, or at the right edge without it. `StopButton` (48 pt) sits at the right edge. While `Queue` is set, only `StopButton` hits (`GearZone.QueueStop`); otherwise `GearZone.CombineButton` hits when shown.
- [ ] The Combine button shows when `StackCombinePlanner.Plan(all pack rows)` is non-empty and no queue runs, and clicking it starts "Combining". While a queue runs, the footer's left text is `"{Label} {Done} / {Total}"`, cached on `Queue.Version`, and Stop cancels.
- [ ] A drag that begins on the deck cancels a running queue. Dropping an item that is part of a selection of 2+ on a side pack, a pack row, a cell or empty pack space starts "Moving" with `Put(id, pack, k)` for each selected non-worn, non-pack row in `Shown` order. k is consecutive from the target slot, or 0 for a pack. Equipment and figure drops are unchanged.
- [ ] Runtime: builds `GearMoveSender` + `RuntimeGearMoveChannel` + `GearMoveQueue` (report = `DisplaySystemMessage`) in `MountGearDeck`, ticks the queue every frame, and cancels it at logout and on disposal.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearDeckGeometry|FullyQualifiedName~UiGearDeckOrganise|FullyQualifiedName~AcDream.App.Tests.UI.Hud.UiGearDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

`GearDeckGeometryTests` (use the file's `Input()` helper or its default input):

```csharp
[Theory]
[InlineData(1f)]
[InlineData(1.5f)]
public void FooterButtons_SitRightToLeft_AndOnlyStopHitsWhileAQueueRuns(float scale)
{
    var input = DefaultInput with { SalvageButton = true, CombineButton = true };
    GearDeckGeometry g = GearDeckGeometry.Create(GearDeckGeometry.DefaultHeightAt(scale), input, null, scale);
    Assert.Equal(g.FooterButton.X - GearDeckGeometry.Gap * scale, g.CombineButton.X + g.CombineButton.W, 3);
    Assert.Equal(GearDeckGeometry.CombineW * scale, g.CombineButton.W, 3);
    Assert.Equal(GearZone.CombineButton, g.HitAt(g.CombineButton.X + 2, g.CombineButton.Y + 2).Zone);

    GearDeckGeometry alone = GearDeckGeometry.Create(GearDeckGeometry.DefaultHeightAt(scale), DefaultInput with { CombineButton = true }, null, scale);
    Assert.Equal(alone.Footer.X + alone.Footer.W, alone.CombineButton.X + alone.CombineButton.W, 3);

    GearDeckGeometry busy = GearDeckGeometry.Create(GearDeckGeometry.DefaultHeightAt(scale), input with { Queue = true }, null, scale);
    Assert.Equal(GearZone.QueueStop, busy.HitAt(busy.StopButton.X + 2, busy.StopButton.Y + 2).Zone);
    Assert.NotEqual(GearZone.FooterButton, busy.HitAt(busy.FooterButton.X + 2, busy.FooterButton.Y + 2).Zone);
}
```

`UiGearDeckOrganiseTests.cs`: build the deck as `UiGearDeckSelectionTests.Make` does, with a real `GearMoveQueue` over a fake `Channel` (as in `GearMoveQueueTests`) and a recording sender. Put two coin stacks of one weenie in the main pack (`StackSizeMax = 100`, sizes 30 and 50), a side pack `Bag`, and three misc items.

```csharp
[Fact]
public void CombineButton_ShowsWhenStacksCanMerge_AndStartsCombining()
{
    Rig rig = Make();
    Assert.True(rig.Deck.Geometry.Input.CombineButton);
    Click(rig.Deck, rig.Deck.Geometry.CombineButton);
    Assert.True(rig.Queue.IsRunning);
    Assert.Equal("Combining", rig.Queue.Label);
    Assert.Equal(GearMove.Merge(Coins30, Coins50, 30), rig.Sender.Sent.Single());
    rig.Deck.Refresh();
    Assert.True(rig.Deck.Geometry.Input.Queue);
    Assert.Equal("Combining 0 / 1", rig.Deck.FooterLeftText);
    Click(rig.Deck, rig.Deck.Geometry.StopButton);
    Assert.False(rig.Queue.IsRunning);
}

[Fact]
public void ADragFromTheDeck_CancelsARunningQueue()
{
    Rig rig = Make();
    rig.Queue.Start("Tidying", [GearMove.Put(Misc0, Player, 5)]);
    rig.Deck.OnEvent(new UiEvent(0, rig.Deck, UiEventType.DragBegin, Payload: new ItemDragPayload(Misc0, ItemDragSource.Inventory, 0, null)));
    Assert.False(rig.Queue.IsRunning);
}

[Fact]
public void DroppingASelection_OnAPack_MovesEveryPickedItem_InOrder()
{
    Rig rig = Make();
    SelectRows(rig, Misc0, Misc1, Misc2);
    DropOn(rig, Misc1, rig.Deck.Geometry.SideCell(1));   // the side pack's cell
    Assert.Equal("Moving", rig.Queue.Label);
    Assert.Equal(GearMove.Put(Misc0, Bag, 0), rig.Sender.Sent[0]);
    rig.Channel.Complete(Misc0); rig.Queue.Tick();
    Assert.Equal(GearMove.Put(Misc1, Bag, 1), rig.Sender.Sent[1]);
}

[Fact]
public void DroppingOneUnselectedItem_StillMovesJustThatItem()
{
    Rig rig = Make();
    SelectRows(rig, Misc0, Misc1);
    DropOn(rig, Misc2, rig.Deck.Geometry.SideCell(1));
    Assert.False(rig.Queue.IsRunning);
    Assert.Contains(rig.Actions.Calls, c => c.StartsWith("put"));
}
```

Helpers: `Click` sends MouseDown + Click at the rect's centre, as `Send` does in the selection tests. `SelectRows` clicks the first row, then Ctrl-clicks the rest, using the rig's `Mods`. `DropOn` sends `DragBegin` with the payload, then `DropReleased` at the rect's centre. Use the geometry's real side-cell accessor (`SideCell(i)` or its actual name in `GearDeckGeometry`).

- [ ] **Step 2: Run the tests and see them fail.**

- [ ] **Step 3: Implement**

Geometry:
- Add the constants `CombineW = 64f, StopW = 48f`.
- Extend the input: `bool CombineButton = false, bool Queue = false`. Add them at the end so existing call sites compile.
- Add the zones `CombineButton, QueueStop` at the end of `GearZone`.
- Add `required DeckRect CombineButton` and `StopButton`. In `Create`:

```csharp
float combineRight = input.SalvageButton ? footerButton.X - gap : footer.X + footer.W;
var combineButton = new DeckRect(combineRight - S(CombineW), footer.Y, S(CombineW), S(FooterH));
var stopButton = new DeckRect(footer.X + footer.W - S(StopW), footer.Y, S(StopW), S(FooterH));
```

In `HitAt`, replace the salvage line with:

```csharp
if (Input.Queue)
{
    if (StopButton.Contains(x, y)) return new GearDeckHit(GearZone.QueueStop);
}
else
{
    if (Input.SalvageButton && FooterButton.Contains(x, y)) return new GearDeckHit(GearZone.FooterButton);
    if (Input.CombineButton && CombineButton.Contains(x, y)) return new GearDeckHit(GearZone.CombineButton);
}
```

`UiGearDeck.cs`:
- Add the two bindings.
- In `Refresh`, add `int locksRev = _b.Locks?.Revision ?? 0;` to the rebuild condition (store `_lastLocks`). Pass `_b.Locks is { } locks ? locks.IsLocked : null` to `GearProjection.Build`.
- `LayoutInput` adds:

```csharp
Queue: _b.Queue?.IsRunning == true,
CombineButton: _b.Queue is { IsRunning: false } && CombineMoves.Count > 0,
```

- `SalvageButton:` becomes `… && _b.Queue?.IsRunning != true`.
- `FooterLeftText`: when `_b.Queue is { IsRunning: true } q`, return `QueueText`, cached on `(q.Version)`: `$"{q.Label} {q.Done} / {q.Total}"`.

`UiGearDeck.Organise.cs` (new partial):

```csharp
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

/// <summary>The deck's organising actions: they plan with the Core planners and hand the moves to the queue.</summary>
internal sealed partial class UiGearDeck
{
    private IReadOnlyList<GearMove> _combineMoves = [];
    private ulong _combineFor = ulong.MaxValue;

    /// <summary>Every merge that would combine the pack items' partial stacks; cached per model build.</summary>
    internal IReadOnlyList<GearMove> CombineMoves
    {
        get
        {
            if (_combineFor != _modelBuild)
            {
                _combineMoves = StackCombinePlanner.Plan(_projection.All);
                _combineFor = _modelBuild;
            }
            return _combineMoves;
        }
    }

    private bool Run(string label, IReadOnlyList<GearMove> moves)
    {
        if (_b.Queue is not { } queue || moves.Count == 0) return false;
        bool started = queue.Start(label, moves);
        _b.Log?.Invoke($"[gear-deck] {label}: {moves.Count} moves {(started ? "started" : "refused")}");
        SyncGeometry();
        return started;
    }

    internal void CombineAll() => Run("Combining", CombineMoves);

    internal void StopQueue()
    {
        _b.Queue?.Cancel();
        SyncGeometry();
    }

    /// <summary>A drag of the selection onto a pack: every selected pack item, in shown order, from
    /// <paramref name="firstSlot"/> on. Worn items and packs stay where they are.</summary>
    private bool MoveSelection(uint packId, int firstSlot)
    {
        var moves = new List<GearMove>();
        int slot = Math.Max(0, firstSlot);
        foreach (GearItemView row in Shown)
            if (_selection.Contains(row.Id) && !row.Worn && !row.IsPack)
                moves.Add(GearMove.Put(row.Id, packId, slot++));
        return Run("Moving", moves);
    }
}
```

`UiGearDeck.Input.cs`:
- `Click`: `case GearZone.CombineButton: CombineAll(); return true;` and `case GearZone.QueueStop: StopQueue(); return true;`. Add both zones to `IsControl`.
- `DragBegin`: before the log line, `if (_b.Queue is { IsRunning: true } q) { q.Cancel(); SyncGeometry(); }`, then record `_dragFromDeck = lifted.ObjId`. Clear it on `DropReleased` and on `CaptureChanged`.
- At the top of `DropReleased`, after resolving `p` and `hit`:

```csharp
if (_dragFromDeck == p.ObjId && _selection.Count > 1 && _selection.Contains(p.ObjId) && GroupTarget(hit) is { } target)
{
    _dragFromDeck = 0u;
    _b.Log?.Invoke($"[gear-deck] drop selection ({_selection.Count}) on {hit.Zone} {hit.Index}");
    return MoveSelection(target.Pack, target.Slot) || true;
}
```

with

```csharp
/// <summary>The pack and first slot a selection dropped on <paramref name="hit"/> goes to, or null for
/// equipment, the figure, worn rows and anything else that takes one item.</summary>
private (uint Pack, int Slot)? GroupTarget(GearDeckHit hit) => hit.Zone switch
{
    GearZone.SidePack => (_model.Packs[hit.Index].Id, 0),
    GearZone.Item when Shown[hit.Index] is { Worn: false, IsPack: true } bag => (bag.Id, 0),
    GearZone.Item when Shown[hit.Index] is { Worn: false } row => (row.PackId, row.Slot),
    GearZone.Frame => (SelectedPackId, Items.Count),
    _ => null,
};
```

`UiGearDeck.Draw.cs`:
- Footer: when `g.Input.Queue`, draw the left text fitted to `g.StopButton.X - g.Footer.X - P(4f)` and a stud labelled "Stop" (`DeckChrome.DrawStud(ctx, r, _scale, on: true)` + `DrawCentred`). Draw nothing else on the right.
- Otherwise, when `g.Input.CombineButton`, draw an unlit stud "Combine" at `g.CombineButton`. Fit the left text to the leftmost shown button.
- `RectFor` maps `CombineButton` and `QueueStop` to their rects for hover backings.

`RetailUiRuntime`:
- Add `private Hud.GearMoveQueue? _gearQueue; private Hud.RuntimeGearMoveChannel? _gearChannel;`.
- In `MountGearDeck` (inside the `try`):

```csharp
var gearActions = new Hud.RuntimeGearItemActions(items, b.Objects, b.SendPutItemInContainer, b.SendStackableMerge);
_gearChannel = new Hud.RuntimeGearMoveChannel(items);
_gearQueue = new Hud.GearMoveQueue(_gearChannel, new Hud.GearMoveSender(b.Objects, b.PlayerGuid, gearActions),
    () => Environment.TickCount64 / 1000.0, _bindings.Options.DisplaySystemMessage);
```

  Pass `Actions: gearActions, Queue: _gearQueue, Locks: _itemLocks`.
- In the catch, dispose and null both.
- At :982, also `_gearQueue?.Tick();`. At logout (:1458) and disposal (:6514), `_gearQueue?.Cancel();`. At disposal, also `_gearQueue?.Dispose(); _gearChannel?.Dispose();`.

- [ ] **Step 4: Run the Verify command.** Expected: all pass (plans 1-3's deck tests included).

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "Gear Deck: queue in the footer, Combine button, Stop, group drag of a selection

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 7: Locks, combine and tidy in the menus, padlock badges

**Goal:** The item menu gains Split..., Lock / Unlock and Combine like stacks. A new pack menu on the side-pack cells offers Tidy this pack, Combine stacks, Apply pack rules and Pack rules.... Locked items draw a small padlock.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.Menu.cs`, `UiGearDeck.Organise.cs`, `UiGearDeck.Draw.cs`
- Create: `src/AcDream.App/UI/Hud/GearSplitRequest.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/UiGearDeckOrganiseTests.cs`, `tests/AcDream.App.Tests/UI/Hud/UiGearDeckDrawTests.cs`

**Acceptance Criteria:**
- [ ] `GearDeckBindings` gains `Action<GearSplitRequest>? OpenSplit = null` and `Action? OpenRules = null` (appended); `GearSplitRequest` is a `public sealed record` (the public split binding in Task 8 takes it)
- [ ] The item menu reads, top to bottom: (Add N to salvage, separator, when salvage is open), Split..., Lock or Unlock, Combine like stacks, separator, Show in pack, separator, Select all, Clear selection.
- [ ] Split... is enabled only for a single selected non-worn row with `Stack > 1`. It calls `OpenSplit(new GearSplitRequest(id, name, stack, row.PackId, row.Slot + 1))`.
- [ ] Lock is shown when any menu-selection row is unlocked (it locks them all); otherwise Unlock (unlocks all). Both go through `Session.Update(s => s.WithLock(ids, on))`.
- [ ] Combine like stacks runs `StackCombinePlanner.Plan(all, only: selection)` as "Combining" and is enabled only when that plan is non-empty.
- [ ] Right-clicking a side-pack cell opens the pack menu. Tidy this pack runs `PackTidyPlanner.Plan(pack, ForPack(pack))` as "Tidying" and is enabled when that plan is non-empty. Combine stacks = `CombineAll`. Apply pack rules runs `RulePlan` (Task 9 fills it; here it returns an empty plan, so the item is disabled). Pack rules... calls `OpenRules`.
- [ ] A locked row draws a padlock glyph in the cell's bottom-left corner (grid) or over the icon's bottom-left (list). This is the figure padlock's glyph, factored into `DrawPadlockGlyph`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiGearDeckOrganise|FullyQualifiedName~UiGearDeckDraw|FullyQualifiedName~UiGearDeckSelection|FullyQualifiedName~UiGearDeckResults' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (in `UiGearDeckOrganiseTests`; open menus with the `RightClick` helper from the selection tests and choose by label through `Menu.Selected`, as those tests do)

```csharp
private static void Choose(Rig rig, string label)
{
    IReadOnlyList<string> items = rig.Deck.Menu.ItemsSource!();
    int i = items.ToList().IndexOf(label);
    Assert.True(i >= 0, $"no '{label}' in [{string.Join(", ", items)}]");
    Assert.True(rig.Deck.Menu.ItemsEnabledSource!()[i], $"'{label}' is disabled");
    rig.Deck.Menu.Selected!(i);
}

[Fact]
public void LockFromTheItemMenu_LocksTheSelection_AndTheMenuThenOffersUnlock()
{
    var (root, rig) = Rooted();
    SelectRows(rig, Misc0, Misc1);
    RightClick(root, rig.Deck, CellOf(rig, Misc0));
    Choose(rig, "Lock");
    Assert.True(rig.Locks.IsLocked(Misc0) && rig.Locks.IsLocked(Misc1));
    Assert.Equal(new uint[] { Misc0, Misc1 }.Order(), rig.Session.Settings.LockedIds);
    RightClick(root, rig.Deck, CellOf(rig, Misc0));
    Choose(rig, "Unlock");
    Assert.Empty(rig.Locks.Ids);
}

[Fact]
public void SplitFromTheItemMenu_AsksForTheStackAndItsNextSlot()
{
    var (root, rig) = Rooted();
    RightClick(root, rig.Deck, CellOf(rig, Coins50));
    Choose(rig, "Split...");
    GearSplitRequest r = rig.SplitRequests.Single();
    Assert.Equal((Coins50, 50, Player), (r.Stack, r.Max, r.Container));
    Assert.Equal(SlotOf(rig, Coins50) + 1, r.Slot);
}

[Fact]
public void TidyFromThePackMenu_QueuesTheTidyMoves()
{
    var (root, rig) = Rooted();
    RightClick(root, rig.Deck, rig.Deck.Geometry.SideCell(0));
    Choose(rig, "Tidy this pack");
    Assert.Equal("Tidying", rig.Queue.Label);
}

[Fact]
public void CombineLikeStacks_TakesOnlyTheSelectionsKinds()
{
    var (root, rig) = Rooted();
    RightClick(root, rig.Deck, CellOf(rig, Coins30));
    Choose(rig, "Combine like stacks");
    Assert.Equal(GearMove.Merge(Coins30, Coins50, 30), rig.Sender.Sent.Single());
}
```

The rig gains `Locks`, `Session` and `SplitRequests` (the `OpenSplit` binding appends to it). `CellOf`/`SlotOf` find the shown index of an id. In `UiGearDeckDrawTests`, add one test: a locked item in the grid draws the padlock. Use the draw tests' recording render context and assert a `FillRoundedRect` inside the cell's bottom-left quarter that is absent when the item is unlocked.

- [ ] **Step 2: Run the tests and see them fail.**

- [ ] **Step 3: Implement**

`GearSplitRequest.cs`:

```csharp
namespace AcDream.App.UI.Hud;

/// <summary>What the split picker asks about: which stack, how many it holds, and where the split goes.</summary>
public sealed record GearSplitRequest(uint Stack, string Name, int Max, uint Container, int Slot);
```

`UiGearDeck.Menu.cs`:
- Add `MenuKind.Pack`.
- In `PickMenu`, map `GearZone.SidePack when hit.Index < _model.Packs.Count => MenuKind.Pack` and keep `_menuPack = _model.Packs[hit.Index].Id`.
- Labels: `SplitLabel = "Split..."`, `LockLabel = "Lock"`, `UnlockLabel = "Unlock"`, `CombineLikeLabel = "Combine like stacks"`, `TidyLabel = "Tidy this pack"`, `CombineStacksLabel = "Combine stacks"`, `ApplyRulesLabel = "Apply pack rules"`, `RulesLabel = "Pack rules..."`.
- Build the item list per open in `BuildItemMenu`:

```csharp
bool anyUnlocked = _menuSelection.Any(id => _b.Locks?.IsLocked(id) != true);
_menuLockLabel = anyUnlocked ? LockLabel : UnlockLabel;
_menuCombine = StackCombinePlanner.Plan(_projection.All, only: _menuSelection);
string[] organise = [SplitLabel, _menuLockLabel, CombineLikeLabel, UiMarkupContextMenu.Separator];
_menuItems = [.. salvagePart, .. organise, .. ItemMenuItems];
```

- `_menuSplit` is the single selected row when `_menuSelection.Length == 1` and that row is `{ Worn: false, IsPack: false, Stack: > 1 }`, else null. Lock/Unlock are disabled without `_b.Locks`.
- For the pack menu, compute `_menuTidy = PackTidyPlanner.Plan(_menuPack, _projection.ForPack(_menuPack))` and `_menuRules = RulePlan(null).Moves` at open. The items are `[TidyLabel, CombineStacksLabel, ApplyRulesLabel, Separator, RulesLabel]`, enabled when, in order: the tidy plan is non-empty, `CombineMoves` is non-empty, the rules plan is non-empty, and `_b.OpenRules is not null`.
- `ChooseMenuItem` dispatches each label to `Organise.cs` methods: `RequestSplit(row)`, `SetLocks(ids, on)`, `Run("Combining", _menuCombine)`, `Run("Tidying", _menuTidy)`, `CombineAll()`, `ApplyRules(_menuRules)`, `_b.OpenRules?.Invoke()`.

`UiGearDeck.Organise.cs` adds:

```csharp
internal void SetLocks(IReadOnlyCollection<uint> ids, bool locked)
{
    if (_b.Locks is null || ids.Count == 0) return;
    _b.Session.Update(s => s.WithLock(ids, locked));
    _b.Log?.Invoke($"[gear-deck] {(locked ? "lock" : "unlock")} {ids.Count}");
}

private void RequestSplit(GearItemView row) =>
    _b.OpenSplit?.Invoke(new GearSplitRequest(row.Id, row.Name, row.Stack, row.PackId, row.Slot + 1));

/// <summary>The pack rules' moves; Task 9 fills this in. <paramref name="only"/> limits it to arrived items.</summary>
internal GearRulePlan RulePlan(IReadOnlyCollection<uint>? only) => new([], new Dictionary<uint, int>());

private void ApplyRules(IReadOnlyList<GearMove> moves) => Run("Sorting", moves);
```

`UiGearDeck.Draw.cs`: factor the body of `DrawFigureLock` after the stud into `DrawPadlockGlyph(UiRenderContext ctx, DeckRect r, bool locked, Vector4 ink)`; `DrawFigureLock` calls it. In `DrawGrid`, after the stack text, add `if (row.Locked) DrawPadlockGlyph(ctx, new DeckRect(r.X + P(1f), r.Y + r.H - P(12f), P(11f), P(11f)), true, TextInk);`. Do the same in `DrawList`, over the icon's bottom-left (`icon.X`, `icon.Y + icon.H - P(10f)`, 10×10).

- [ ] **Step 4: Run the Verify command.** Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "Gear Deck: lock, split and combine in the item menu; pack menu with tidy; padlock badges

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 8: The split picker and split-drag

**Goal:** A deck-look popup asks how many to split off (1 to stack−1, Half, OK, Cancel) and sends the split. Ctrl/Cmd- or Shift-dragging a stack onto a pack place opens it for that place.

**Files:**
- Create: `src/AcDream.App/UI/Hud/GearSplitBinding.cs`
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.Input.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/GearSplitBindingTests.cs` (create), `tests/AcDream.App.Tests/UI/Hud/UiGearDeckOrganiseTests.cs`

**Acceptance Criteria:**
- [ ] `GearSplitBinding.Open(request, send)` sets `Amount` to half the stack (rounded down, at least 1). `SetAmount` clamps to `[1, Max-1]`. `Half` sets half. `Ok` calls `send(stack, container, slot, amount)` once and then `Close`. `Cancel` only closes. `Title` is `"Split {Name} ({Max})"`.
- [ ] The markup builds through `MarkupDocument.Build` with this binding (a smoke test, like the Chat Deck's binding tests).
- [ ] A drop with the Toggle or Range modifier, of a deck item with `Stack > 1`, on a pack target (`GroupTarget` rules, any selection size) calls `OpenSplit(new GearSplitRequest(id, name, stack, pack, slot))` and sends nothing else. Without a modifier the drop is unchanged.
- [ ] The runtime opens the popup docked beside the deck (`ChatDeckSettingsPlacement.Dock(Host.Root, _gearDeck, window)`), in the Chat Deck popup look, and sends through `items.TrySplitToContainer`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearSplitBinding|FullyQualifiedName~UiGearDeckOrganise' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
public sealed class GearSplitBindingTests
{
    [Fact]
    public void OpensAtHalf_ClampsAmount_AndOkSendsOnceThenCloses()
    {
        var sent = new List<(uint, uint, int, int)>();
        int closed = 0;
        var b = new GearSplitBinding { Close = () => closed++ };
        b.Open(new GearSplitRequest(0xA1, "Pyreal Mote", 25, 0x50000001, 4), (s, c, slot, n) => { sent.Add((s, c, slot, n)); return true; });
        Assert.Equal(12, b.Amount);
        Assert.Equal("Split Pyreal Mote (25)", b.Title);
        b.SetAmount(99); Assert.Equal(24, b.Amount);
        b.SetAmount(0); Assert.Equal(1, b.Amount);
        b.Half(); Assert.Equal(12, b.Amount);
        b.Ok();
        b.Ok();
        Assert.Equal([(0xA1u, 0x50000001u, 4, 12)], sent);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Cancel_SendsNothing()
    {
        int sends = 0;
        var b = new GearSplitBinding { Close = () => { } };
        b.Open(new GearSplitRequest(1, "x", 2, 2, 0), (_, _, _, _) => { sends++; return true; });
        b.Cancel();
        b.Ok();
        Assert.Equal(0, sends);
    }
}
```

Add a markup smoke test, built the way the Chat Deck settings binding test builds its markup (find it with `grep -rn "ChatDeckSettingsBinding.Markup" tests`). Add the deck test `AModifiedDragOfAStack_OntoAPack_AsksForASplit`: Ctrl mods, drag `Coins50` onto the `Bag` side cell, and expect one `SplitRequests` entry `(Coins50, 50, Bag, 0)`, no queue, and no put.

- [ ] **Step 2: Run the tests and see them fail.**

- [ ] **Step 3: Implement**

`GearSplitBinding.cs`:

```csharp
namespace AcDream.App.UI.Hud;

/// <summary>The split picker: how many to take off a stack, and where they go. Built once and re-pointed per open.</summary>
public sealed class GearSplitBinding
{
    private GearSplitRequest? _request;
    private Func<uint, uint, int, int, bool>? _send;

    public const string Markup = """
        <panel x="120" y="120" w="260" h="118" title="{Title}" theme="plugin" layout="column" padding="10" gap="8">
          <group layout="row" gap="8" align="center" h="22">
            <label text="Amount" w="60" />
            <number value="{Amount}" min="1" max="999999" step="1" onchange="{SetAmount}" w="90" />
            <button text="Half" onclick="{Half}" w="50" h="22" />
          </group>
          <group layout="row" gap="8" h="22">
            <group grow="1" />
            <button text="Cancel" onclick="{Cancel}" w="70" h="22" />
            <button text="Split" onclick="{Ok}" w="70" h="22" />
          </group>
        </panel>
        """;

    internal void Open(GearSplitRequest request, Func<uint, uint, int, int, bool> send)
    {
        _request = request;
        _send = send;
        Amount = Math.Max(1, request.Max / 2);
    }

    public string Title => _request is { } r ? $"Split {r.Name} ({r.Max})" : "Split";
    public int Amount { get; private set; } = 1;
    public Action<int> SetAmount => v => Amount = _request is { } r ? Math.Clamp(v, 1, Math.Max(1, r.Max - 1)) : 1;
    public Action Half => () => SetAmount(_request is { } r ? r.Max / 2 : 1);
    public Action? Close { get; set; }
    public Action Cancel => () => { _request = null; Close?.Invoke(); };
    public Action Ok => () =>
    {
        if (_request is not { } r || _send is null) return;
        _request = null;
        _send(r.Stack, r.Container, r.Slot, Amount);
        Close?.Invoke();
    };
}
```

Deck (`DropReleased`), after the group-drag branch:

```csharp
(bool toggle, bool range) = ReadModifiers();
if ((toggle || range) && _dragFromDeck == p.ObjId && _b.OpenSplit is { } openSplit
    && _b.Objects().Get(p.ObjId) is { StackSize: > 1 } stack && GroupTarget(hit) is { } where)
{
    _dragFromDeck = 0u;
    openSplit(new GearSplitRequest(p.ObjId, _b.DisplayName(stack), stack.StackSize, where.Pack, where.Slot));
    return true;
}
```

`RetailUiRuntime`: `_gearSplit` (`PluginAppearanceWindow?`) and `_gearSplitBinding`, built the way `ChatDeckEditorWindow()` is. Use `Hud.ChatDeckSettingsBinding.Prepare(MarkupDocument.Build(Hud.GearSplitBinding.Markup, binding, …, themes: ChatDeckPopupThemes(), …), close)` and `onOpen: window => Hud.ChatDeckSettingsPlacement.Dock(Host.Root, _gearDeck, window)`. Add `OpenGearSplit(Hud.GearSplitRequest r)`: `binding.Open(r, (s, c, slot, n) => items.TrySplitToContainer(s, c, (uint)slot, (uint)n)); window.Show();`. Pass `OpenSplit: OpenGearSplit` in the bindings. Close the popup at logout. If `Dock` takes a `UiChatDeck`, add an overload taking a `UiElement`, or call the general placement it uses inside.

- [ ] **Step 4: Run the Verify command.** Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "Gear Deck: split picker popup and Ctrl/Shift split-drag

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 9: Pack rules, on-arrival sorting and stale-lock pruning

**Goal:** A Pack rules popup edits the rules and "combine on arrival". Apply pack rules runs them. A runtime-ticked watcher combines and sorts items that arrive, and drops stale lock ids once per login.

**Files:**
- Create: `src/AcDream.App/UI/Hud/GearRulesBinding.cs`, `src/AcDream.App/UI/Hud/GearInventoryWatcher.cs`
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.Organise.cs` (`RulePlan`), `src/AcDream.App/UI/RetailUiRuntime.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/GearRulesBindingTests.cs`, `tests/AcDream.App.Tests/UI/Hud/GearInventoryWatcherTests.cs` (create), `UiGearDeckOrganiseTests.cs`

**Acceptance Criteria:**
- [ ] `GearRules.Resolve(settings)` pairs each rule with its saved filter's query. A rule with a missing filter gets `GearQuery.Empty`, so the planner skips it. `UiGearDeck.RulePlan(only)` = `PackRulePlanner.Plan(_projection.All, _model.Packs, resolved, only)`. Apply pack rules runs it as "Sorting", plus one chat line per full pack: `"{PackName} is full; {n} left where they were."`.
- [ ] Rules editor binding: list rows (On, Filter, Pack, On arrival), with Filter `"(missing)"` and Pack `"(missing)"` when gone. Add (first saved filter → main pack), Delete, Up, Down, filter and pack pickers (`<menu>`), the On and On-arrival checkboxes, and a "Combine stacks when items arrive" checkbox. Each edit goes through `Session.Update`. It is disabled with a hint when there are no saved filters.
- [ ] Watcher: an `ObjectMoved` with `Origin == AuthoritativeResponse` into the player or a side pack, from outside the player's inventory, and an `ObjectAdded` into one once `HasCompleteInventory`, record the id as arrived. Once 0.5 s have passed since the last arrival and no queue runs, it starts one queue, "Sorting new items". It holds combine moves for the arrived ids' groups (when `CombineOnArrival`) followed by the rule moves for `OnArrival` rules limited to arrived ids. Moves inside the player's own packs are not arrivals.
- [ ] Watcher prune: on the first tick after `HasCompleteInventory(player)` in a session, lock ids with no object are removed through `Session.Update(s => s with { LockedIds = kept })`. `Session.Ended` resets it.
- [ ] Runtime: builds the watcher in `MountGearDeck`, ticks it every frame, and disposes it with the deck. `OpenRules` opens the rules popup docked by the deck.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearRulesBinding|FullyQualifiedName~GearInventoryWatcher|FullyQualifiedName~UiGearDeckOrganise' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

`GearInventoryWatcherTests.cs`: build a table with the player (`ItemsCapacity = 102`, `ContainersCapacity = 7`), a side pack `Bag` (`ItemsCapacity = 24`), a corpse `Corpse` holding `Loot` (a coin stack of weenie 9, size 10, max 100), and the player holding `Coins` (weenie 9, size 40). Add a `GearDeckSession` (null store, gameplay true, key "Toon") with `CombineOnArrival = true` and a saved filter "Coins" (Type: Other, or a Name chip "coin"). Add a rule `("Coins", Bag, OnArrival: true)` and a recording queue (fake channel + sender, as in Task 5).

```csharp
[Fact]
public void ALootedStack_IsCombined_ThenSortedByRule_AfterTheQuietTime()
{
    Rig rig = Make();
    rig.Table.ApplyConfirmedServerMove(Loot, Player, 0);   // the server's InventoryPutObjInContainer
    rig.Now = 0.4; rig.Watcher.Tick();
    Assert.False(rig.Queue.IsRunning);
    rig.Now = 0.5; rig.Watcher.Tick();
    Assert.Equal("Sorting new items", rig.Queue.Label);
    Assert.Equal(GearMove.Merge(Loot, Coins, 10), rig.Sender.Sent[0]);
}

[Fact]
public void MovesInsideTheInventory_AreNotArrivals()
{
    Rig rig = Make();
    rig.Table.ApplyConfirmedServerMove(Coins, Bag, 0);
    rig.Now = 5; rig.Watcher.Tick();
    Assert.False(rig.Queue.IsRunning);
}

[Fact]
public void StaleLocks_AreDroppedOnce_WhenTheInventoryIsComplete()
{
    Rig rig = Make(complete: false);
    rig.Session.Update(s => s.WithLock([Coins, 0xDEADu], true));
    rig.Watcher.Tick();
    Assert.Contains(0xDEADu, rig.Session.Settings.LockedIds);
    rig.MarkInventoryComplete();
    rig.Watcher.Tick();
    Assert.Equal(new[] { Coins }, rig.Session.Settings.LockedIds);
}
```

Use the table's real confirmed-move API, the one `GameEventWiring` calls for `InventoryPutObjInContainer` (`ApplyConfirmedServerMove`), with its real argument list. Mark the inventory complete the way `ClientObjectTable.HasCompleteInventory`'s own tests do.

`GearRulesBindingTests.cs`: with a session holding saved filters "Salvage" and "Gems" and packs `[Main, Bag]`:
- Add makes the rule ("Salvage", Main).
- Choosing pack index 1 sets `PackId = Bag`.
- Down/Up reorder the rules.
- Delete removes the selected rule.
- Toggling arrival and the combine checkbox updates the settings.
- A rule whose filter "Old" isn't saved shows `"(missing)"` in the Filter column.
- The markup builds, as a smoke test like Task 8's.

In `UiGearDeckOrganiseTests`, `ApplyPackRules_FromThePackMenu_QueuesSorting_AndReportsAFullPack`: a rule to a side pack with capacity 1 and two matching items gives one put, the label "Sorting" and one report line ending "1 left where they were.".

- [ ] **Step 2: Run the tests and see them fail.**

- [ ] **Step 3: Implement**

A `GearRules` static helper, in `GearRulesBinding.cs`:

```csharp
internal static class GearRules
{
    /// <summary>Each rule with its saved filter's query; a rule whose filter is gone gets the empty query (skipped).</summary>
    public static IReadOnlyList<(GearPackRule Rule, GearQuery Query)> Resolve(GearDeckSettings s, Func<GearPackRule, bool>? keep = null) =>
        s.PackRules.Where(r => keep?.Invoke(r) ?? true)
            .Select(r => (r, s.SavedFilters.FirstOrDefault(f => string.Equals(f.Name, r.Filter, StringComparison.OrdinalIgnoreCase))?.Query ?? GearQuery.Empty))
            .ToArray();

    public static string PackName(IReadOnlyList<GearPack> packs, uint id)
    {
        for (int i = 0; i < packs.Count; i++)
            if (packs[i].Id == id) return packs[i].IsMain ? "Main" : $"Pack {i + 1}";
        return "(missing)";
    }
}
```

`UiGearDeck.RulePlan(only)` becomes `PackRulePlanner.Plan(_projection.All, _model.Packs, GearRules.Resolve(_b.Session.Settings), only)`. `ApplyRules` takes the `GearRulePlan`: it reports each `Full` entry through `_actions.Report($"{GearRules.PackName(_model.Packs, id)} is full; {n} left where they were.")` and runs "Sorting". Store the whole plan in `_menuRules`.

`GearRulesBinding` follows `ChatDeckEditorBinding.Rules`. It is constructed with `Func<GearDeckSession?> session` and `Func<IReadOnlyList<GearPack>> packs`, and exposes:
- `RuleOn`, `RuleFilter`, `RulePack`, `RuleArrival` (`IReadOnlyList<bool|string>`), `SelectedRuleIndex`, `SelectRuleRow`
- `ToggleRule(int)`, `ToggleArrival(int)`
- `FilterNames`, `SelectedFilter`, `SelectFilter(int)`
- `PackNames`, `SelectedPackIndex`, `SelectPack(int)`
- `AddRule`, `DeleteRule`, `MoveUp`, `MoveDown`, `HasSelectedRule`, `HasFilters`
- `CombineOnArrival`, `SetCombineOnArrival`, `Close`

Markup:

```xml
<panel x="100" y="80" w="420" h="380" title="Pack rules" theme="plugin" layout="column" padding="10" gap="6">
  <label text="Send what a saved filter matches to a pack. The first rule that matches wins." h="16" />
  <list header="true" selected="{SelectedRuleIndex}" onchange="{SelectRuleRow}" rowheight="18" selectionband="true" grow="1">
    <column type="checkbox" header="On" width="30" values="{RuleOn}" onchange="{ToggleRule}" />
    <column type="text" header="Filter" width="*" items="{RuleFilter}" />
    <column type="text" header="Pack" width="80" items="{RulePack}" />
    <column type="checkbox" header="On arrival" width="70" values="{RuleArrival}" onchange="{ToggleArrival}" />
  </list>
  <group layout="row" gap="6" align="center" h="24">
    <label text="Filter" w="40" />
    <menu items="{FilterNames}" selected="{SelectedFilter}" onchange="{SelectFilter}" w="150" h="22" />
    <label text="Pack" w="34" />
    <menu items="{PackNames}" selected="{SelectedPackIndex}" onchange="{SelectPack}" w="110" h="22" />
  </group>
  <group layout="row" gap="6" h="22">
    <button text="+ Rule" onclick="{AddRule}" enabled="{HasFilters}" w="60" h="22" />
    <button text="Delete" onclick="{DeleteRule}" enabled="{HasSelectedRule}" w="60" h="22" />
    <group grow="1" />
    <button text="Up" onclick="{MoveUp}" enabled="{HasSelectedRule}" w="40" h="22" />
    <button text="Down" onclick="{MoveDown}" enabled="{HasSelectedRule}" w="44" h="22" />
  </group>
  <checkbox text="Combine stacks when items arrive" checked="{CombineOnArrival}" onchange="{SetCombineOnArrival}" h="16" />
</panel>
```

If the `<menu>` element's `selected`/`onchange` bind to another type than `int`/`Action<int>`, match `MarkupDocument`'s `case "menu"` (and the Theme/Dock menu binding at `RetailUiRuntime.cs:5306`).

`GearInventoryWatcher.cs`:

```csharp
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

/// <summary>
/// Keeps the Gear Deck's organising going while the deck is closed: drops lock ids for objects that are
/// gone (once per login, when the inventory is complete), and after items arrive from outside (pickup,
/// loot, trade, purchase) queues combine-on-arrival and the on-arrival pack rules for them.
/// </summary>
internal sealed class GearInventoryWatcher : IDisposable
{
    public const double QuietSeconds = 0.5;

    private readonly Func<ClientObjectTable> _objects;
    private readonly Func<uint> _player;
    private readonly GearDeckSession _session;
    private readonly GearMoveQueue _queue;
    private readonly Func<ClientObject, string> _displayName;
    private readonly ItemLocks? _locks;
    private readonly Func<double> _clock;
    private readonly Action<string>? _report;
    private readonly HashSet<uint> _arrived = [];
    private double _lastArrival;
    private bool _pruned;
    private ClientObjectTable? _table;

    public GearInventoryWatcher(Func<ClientObjectTable> objects, Func<uint> player, GearDeckSession session, GearMoveQueue queue,
        Func<ClientObject, string> displayName, ItemLocks? locks, Func<double> clock, Action<string>? report = null)
    {
        _objects = objects; _player = player; _session = session; _queue = queue;
        _displayName = displayName; _locks = locks; _clock = clock; _report = report;
        _session.Ended += OnEnded;
    }

    public void Tick()
    {
        Follow(_objects());
        uint player = _player();
        if (player == 0u || _table is null) return;
        if (!_pruned && _table.HasCompleteInventory(player))
        {
            _pruned = true;
            ClientObjectTable t = _table;
            uint[] kept = _session.Settings.LockedIds.Where(id => t.Get(id) is not null).ToArray();
            if (kept.Length != _session.Settings.LockedIds.Count) _session.Update(s => s with { LockedIds = kept });
        }
        if (_arrived.Count == 0 || _queue.IsRunning || _clock() - _lastArrival < QuietSeconds) return;
        uint[] only = _arrived.ToArray();
        _arrived.Clear();
        GearInventoryModel model = GearInventoryModel.Build(_table, player, null);
        GearProjection rows = GearProjection.Build(model, _table, _displayName, _locks is { } l ? l.IsLocked : null);
        GearDeckSettings s = _session.Settings;
        var moves = new List<GearMove>();
        if (s.CombineOnArrival) moves.AddRange(StackCombinePlanner.Plan(rows.All, only));
        GearRulePlan rules = PackRulePlanner.Plan(rows.All, model.Packs, GearRules.Resolve(s, r => r.OnArrival), only);
        moves.AddRange(rules.Moves);
        if (moves.Count > 0) _queue.Start("Sorting new items", moves);
    }

    private void Follow(ClientObjectTable table)
    {
        if (ReferenceEquals(table, _table)) return;
        if (_table is not null) { _table.ObjectMoved -= OnMoved; _table.ObjectAdded -= OnAdded; }
        _table = table;
        _table.ObjectMoved += OnMoved;
        _table.ObjectAdded += OnAdded;
        _arrived.Clear();
    }

    /// <summary>The player or one of their side packs (not equipped).</summary>
    private bool IsOwnPack(uint container)
    {
        uint player = _player();
        if (container == 0u || player == 0u) return false;
        if (container == player) return true;
        return _table?.Get(container) is { } c && c.ContainerId == player && c.CurrentlyEquippedLocation == EquipMask.None
            && ClientObjectTable.IsContainerListMember(c);
    }

    private void OnMoved(ClientObjectMove move)
    {
        if (move.Origin != ClientObjectMoveOrigin.AuthoritativeResponse) return;
        if (!IsOwnPack(move.Current.ContainerId)) return;
        if (IsOwnPack(move.Previous.ContainerId) || move.Previous.WielderId == _player()) return;   // a move inside
        Arrive(move.ItemId);
    }

    private void OnAdded(ClientObject o)
    {
        if (_pruned && IsOwnPack(o.ContainerId)) Arrive(o.ObjectId);   // before the inventory is complete, adds are the login dump
    }

    private void Arrive(uint id)
    {
        _arrived.Add(id);
        _lastArrival = _clock();
    }

    private void OnEnded()
    {
        _pruned = false;
        _arrived.Clear();
    }

    public void Dispose()
    {
        _session.Ended -= OnEnded;
        if (_table is not null) { _table.ObjectMoved -= OnMoved; _table.ObjectAdded -= OnAdded; }
    }
}
```

`RetailUiRuntime`:
- Add the `_gearWatcher` and `_gearRules` popup fields.
- In `MountGearDeck`, after the queue: `_gearWatcher = new Hud.GearInventoryWatcher(() => b.Objects, b.PlayerGuid, session, _gearQueue, ResolveAppropriateItemName, _itemLocks, () => Environment.TickCount64 / 1000.0, _bindings.Options.DisplaySystemMessage);`.
- Tick it every frame after the queue, and dispose it with the deck.
- Add `OpenRules: ToggleGearRules`. It is built like `ChatDeckEditorWindow()`, with `new Hud.GearRulesBinding(() => _gearDeckSession, () => _gearDeck?.Model.Packs ?? [])`, and docked by the deck. Close it at logout.

- [ ] **Step 4: Run the Verify command.** Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "Gear Deck: pack rules popup, apply rules, on-arrival combine and rules, stale-lock prune

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 10: Full suite and live check

**Goal:** Run the full Mac suite with no failures, then have the user check plan 4 live on the local ACE server.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- Test: whole suite (no new files)

**Acceptance Criteria:**
- [ ] `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` reports 0 failures (output captured)
- [ ] The user says they are available **before** any client launches (never launch on your own; local ACE 10.10.20.20 via `~/OpenAC-dev/dev-client.env`, never Dreamweave)
- [ ] The user confirms live, Modern HUD on:
  - (a) Combine button merges two partial stacks, with progress shown.
  - (b) Tidy this pack reorders the pack, and Stop halts it.
  - (c) Split... and a Ctrl-drag split both make the chosen amount.
  - (d) Lock shows a padlock. Dropping, giving (NPC), selling (vendor) and adding to salvage are all refused with "The X is locked.", in the deck and from the retail windows with Modern HUD off.
  - (e) A pack rule moves matches, and a looted item matching an on-arrival rule moves by itself.
  - (f) Dragging a selection onto a side pack moves all of it.
- [ ] Every issue found live is fixed, reviewed, and re-checked by the user

**Verify:** `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → 0 failures; then the user's live confirmation of (a)-(f)

**Steps:**

- [ ] **Step 1:** Run the full suite and capture the summary lines. Fix any failure, keeping to the plan's own rules, before going on.
- [ ] **Step 2:** Ask the user whether they are available for a live check. Don't launch until they say so. Auto mode blocks `pkill` of the client, so ask the user to close it between runs.
- [ ] **Step 3:** Walk the user through (a)-(f), with the `[gear-deck]` log lines (`Combining: n moves started`, drop lines) on hand for anything that goes wrong.
- [ ] **Step 4:** Record the outcome in the SDD ledger and stop. Merging and pushing wait for the user.
