# Target Deck Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Modern HUD nameplate, the Target Deck. It describes the selected target across ten kinds, auto-identifies monsters, summons and corpses for stamina and mana, and lets plugins add rows and ⋯ menu actions.

**Architecture:**
- **Core** holds the pure pieces: `PublicWeenieFlags`, `TargetKindClassifier` and `TargetPlateModelBuilder`.
- **Runtime** holds `TargetAutoAppraisal`, a throttled policy that sends Automation-origin appraisals.
- **App** holds `UiTargetDeck`. It is drawn with `DeckChrome` and `DeckFonts`, mounted through `RetailWindowFrame`, and registered with `ModernHudSwitch` with no retail windows.
- **Plugins** get `IUiRegistry.TargetPlate`, modelled on `WorldMarkers`. The host keeps a per-plugin store.

**Tech Stack:** C# / .NET (`~/.dotnet`), xunit, the existing retained UI (`UiElement`, `UiRenderContext`).

**Spec:** `docs/superpowers/specs/2026-10-27-target-deck-design.md`. Read it before any task.

**Worktree:** implement in `~/code/OpenAC/OpenAC-target-deck` on branch `feat/target-deck`, cut from `main`:

```bash
git -C ~/code/OpenAC/OpenAC worktree add -b feat/target-deck ../OpenAC-target-deck main
```

**Test command** (always this script, never a bare `dotnet test`; narrow with `MAC_TEST_FILTER`):

```bash
cd ~/code/OpenAC/OpenAC-target-deck && ~/.dotnet/dotnet build -c Release AcDream.slnx -v q
MAC_TEST_FILTER='FullyQualifiedName~<Name>' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-target-deck --no-build
```

## Global Constraints

- **Purely additive.** No retail window is hidden, moved or changed. The `ModernHudReplacement` for the deck has an empty `RetailWindows` list.
- **Modern HUD only.** The deck exists only while `MiscSettings.ModernHud` is on and gameplay is on screen. Auto-ID also runs only then, because it is driven by the mounted deck's tick.
- **Visibility.** The window is shown by `ModernHudSwitch` like the other decks. With no selection it draws nothing and hit-tests false, so the player sees it appear and disappear with the selection.
- **Shape B, fixed size.** The base size is 340 × 104 at scale 1 and is the same for every kind. There are no resize edges. The default position is top centre, 12 px from the top. Long names end in "…".
- **No Command Deck duplicates.** No attack, stance or recast actions.
- **Auto-ID** covers Monster, Summon and Corpse only:
  - Triggers: select; a melee/missile hit on the target (matched by name); a health change since the last ID.
  - At least 2.0 s between sends per target, with triggers during the gap combined into one send.
  - Never for an unselected target.
  - The existing `AppraisalRequestOrigin.Automation` is used, so it never opens or switches the examine window.
- **Vitals.**
  - Live health comes from `CombatState.GetHealthPercent`.
  - Stamina and mana come from `CombatState.TryGetCreatureVitals`.
  - After 10.0 s since the last successful ID they are drawn in the dimmed "stale" style.
  - A failed ID draws dimmed bars with an "Assess failed" tooltip.
- **Fonts and colours.** Text uses `DeckFonts` (`DeckFace`) and never `ctx.DefaultFont`. Colours come from `DeckChrome`.
- **Plugin budget.**
  - Two plugin lines on the plate in total, at most two rows and four actions per provider.
  - Providers are ordered by registration order.
  - Overflow goes in the plugin-row tooltip.
  - Exceptions are caught, logged once per object, and that provider is skipped for that object until its next trigger.
- **Contract.** `ForkContractVersion` becomes `0.1.28-fork.2`.

**User decisions (already made):**
- "Summons are their own entity"; split corpses and containers. That gives ten kinds: Monster, Summon, Player, NPC, Item, Corpse, Container, Portal, World object, Self.
- Full action hub with context actions. "attack on the plate doesn't make sense because it's already there in the command deck".
- "Let's not replace any current panel for this work — pure addition only".
- Visibility: "A — it's all part of the modern hud work". Modern HUD only, auto show/hide.
- Shape: "I like B".
- Auto-ID: "only things like corpses and monsters get auto appraised". Summons count as creatures.
- Vitals refresh: "C". Once on select, then on events, with at least 2 s between sends.
- Plugins: "Yes b". Info rows plus ⋯ menu actions.
- Per-kind content: "Looks ok", including the small Self card.

**Plan-time adjustments to the spec** (forced by what the code has; apply them and tell the user at handoff):
1. **No new `HudAuto` origin.** The existing `Automation` origin already does exactly what the spec asks: it is accepted, `PresentInUi` is false unless the window already shows the object, and `AppraisalReceived` still fires.
2. **No "spell landed" event exists.** Spell hits reach auto-ID through the health-change trigger. Melee and missile hits use `CombatState.DamageDealtAccepted`, which carries only the defender's name, matched against the selected object's name.
3. **"Mark on map" and summon "Dismiss" are dropped.** The client has no built-in path for either; plugins can add them through ⋯ actions.
4. **⋯ menus are trimmed to paths that exist.**
   - Kept: Examine, Copy name, Follow, Swear allegiance.
   - Dropped: Split, Salvage, Add friend, Squelch.
5. **The vitals cache is not a separate store.** `CombatState` already keeps creature vitals per guid. The new `TargetAppraisalLog` only records when each target was last identified and whether it succeeded.

---

## File map

| File | Role |
|---|---|
| `src/AcDream.Core/Items/PublicWeenieFlags.cs` (new) | `[Flags]` enum of PWD bits |
| `src/AcDream.Core/Combat/SelectedObjectHealthPolicy.cs` (modify) | its `Bf*` constants alias the enum |
| `src/AcDream.Core/Ui/RadarBlipColors.cs` (modify) | its private constants alias the enum |
| `src/AcDream.Core/Targeting/TargetKind.cs` (new) | `TargetKind`, `WorldObjectKind` |
| `src/AcDream.Core/Targeting/TargetKindClassifier.cs` (new) | classification precedence |
| `src/AcDream.Core/Targeting/TargetAppraisalLog.cs` (new) | last-ID time and success per object |
| `src/AcDream.Core/Targeting/TargetPlateModel.cs` (new) | plate snapshot records |
| `src/AcDream.Core/Targeting/TargetPlateModelBuilder.cs` (new) | builds the snapshot for each kind |
| `src/AcDream.Runtime/Gameplay/TargetAutoAppraisal.cs` (new) | auto-ID policy and throttle |
| `src/AcDream.Plugin.Abstractions/TargetPlate.cs` (new) | plugin contract types and the NoOp |
| `src/AcDream.Plugin.Abstractions/IUiRegistry.cs` (modify) | `TargetPlate` / `TargetPlateFor` defaults |
| `src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj` (modify) | `0.1.28-fork.2` |
| `docs/plugin-api.md` (modify) | "Target plate" section |
| `src/AcDream.App/Plugins/PluginTargetPlateStore.cs` (new) | host store: providers, budget, faults |
| `src/AcDream.App/Plugins/BufferedUiRegistry.cs` (modify) | exposes the store |
| `src/AcDream.Core/Plugins/ScopedPluginHost.cs` (modify) | scopes `TargetPlate` per plugin |
| `src/AcDream.App/UI/Hud/TargetDeckGeometry.cs` (new) | rects and zones |
| `src/AcDream.App/UI/Hud/ITargetDeckActions.cs` (new) | action interface |
| `src/AcDream.App/UI/Hud/UiTargetDeck.cs` + `.Draw.cs` (new) | the plate element |
| `src/AcDream.App/UI/WindowNames.cs` (modify) | `TargetDeck = "target-deck"` |
| `src/AcDream.App/UI/RetailUiRuntime.cs` (modify) | bindings record, actions class, mount, switch entry |
| `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs` (modify) | builds `TargetDeckRuntimeBindings` |

---

### Task 1: PublicWeenieFlags enum

**Goal:** Gather the PWD bits into one `[Flags]` enum and make the two existing constant sets alias it, with no change in behaviour.

**Files:**
- Create: `src/AcDream.Core/Items/PublicWeenieFlags.cs`
- Modify: `src/AcDream.Core/Combat/SelectedObjectHealthPolicy.cs` (the `Bf*` consts)
- Modify: `src/AcDream.Core/Ui/RadarBlipColors.cs` (private consts in `RadarObjectTraits`)
- Test: `tests/AcDream.Core.Tests/Items/PublicWeenieFlagsTests.cs`

**Acceptance Criteria:**
- [ ] The enum values match the retail `PublicWeenieDesc` bitfield listed below.
- [ ] `SelectedObjectHealthPolicy.BfPlayer` and the other `Bf*` constants are still public `uint` consts with the same values.
- [ ] The existing `SelectedObjectHealthPolicy*` and `RadarBlipColors*` tests stay green.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~PublicWeenieFlags|FullyQualifiedName~SelectedObjectHealthPolicy|FullyQualifiedName~RadarBlip' run-mac-tests.sh … --no-build` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing test**

```csharp
using AcDream.Core.Combat;
using AcDream.Core.Items;
using Xunit;

namespace AcDream.Core.Tests.Items;

public sealed class PublicWeenieFlagsTests
{
    [Theory]
    [InlineData(PublicWeenieFlags.Openable, 0x1u)]
    [InlineData(PublicWeenieFlags.Player, 0x8u)]
    [InlineData(PublicWeenieFlags.Attackable, 0x10u)]
    [InlineData(PublicWeenieFlags.PlayerKiller, 0x20u)]
    [InlineData(PublicWeenieFlags.Vendor, 0x200u)]
    [InlineData(PublicWeenieFlags.Door, 0x1000u)]
    [InlineData(PublicWeenieFlags.Corpse, 0x2000u)]
    [InlineData(PublicWeenieFlags.LifeStone, 0x4000u)]
    [InlineData(PublicWeenieFlags.Portal, 0x40000u)]
    [InlineData(PublicWeenieFlags.FreePkStatus, 0x200000u)]
    [InlineData(PublicWeenieFlags.PkLiteStatus, 0x2000000u)]
    [InlineData(PublicWeenieFlags.BindStone, 0x8000000u)]
    public void BitsMatchTheRetailBitfield(PublicWeenieFlags flag, uint bit) =>
        Assert.Equal(bit, (uint)flag);

    [Fact]
    public void TheHealthPolicyConstantsAreTheEnumBits()
    {
        Assert.Equal((uint)PublicWeenieFlags.Player, SelectedObjectHealthPolicy.BfPlayer);
        Assert.Equal((uint)PublicWeenieFlags.Attackable, SelectedObjectHealthPolicy.BfAttackable);
        Assert.Equal((uint)PublicWeenieFlags.PlayerKiller, SelectedObjectHealthPolicy.BfPlayerKiller);
        Assert.Equal((uint)PublicWeenieFlags.FreePkStatus, SelectedObjectHealthPolicy.BfFreePkStatus);
        Assert.Equal((uint)PublicWeenieFlags.PkLiteStatus, SelectedObjectHealthPolicy.BfPkLiteStatus);
    }

    [Fact]
    public void FlagsOfReadsTheObjectsBitfield()
    {
        var obj = new ClientObject { ObjectId = 1, PublicWeenieBitfield = 0x2010u };
        Assert.Equal(PublicWeenieFlags.Corpse | PublicWeenieFlags.Attackable, PublicWeenieFlagsExtensions.FlagsOf(obj));
        Assert.Equal(PublicWeenieFlags.None, PublicWeenieFlagsExtensions.FlagsOf(new ClientObject { ObjectId = 2 }));
    }
}
```

- [ ] **Step 2: Run it.** Expected: FAIL to compile (`PublicWeenieFlags` not defined).

- [ ] **Step 3: Implement.** Create `PublicWeenieFlags.cs`:

```csharp
namespace AcDream.Core.Items;

/// <summary>
/// The PublicWeenieDesc bitfield every object description carries
/// (<see cref="ClientObject.PublicWeenieBitfield"/>).
/// </summary>
[Flags]
public enum PublicWeenieFlags : uint
{
    None = 0,
    Openable = 0x00000001,
    Inscribable = 0x00000002,
    Stuck = 0x00000004,
    Player = 0x00000008,
    Attackable = 0x00000010,
    PlayerKiller = 0x00000020,
    HiddenAdmin = 0x00000040,
    UiHidden = 0x00000080,
    Book = 0x00000100,
    Vendor = 0x00000200,
    PkSwitch = 0x00000400,
    NpkSwitch = 0x00000800,
    Door = 0x00001000,
    Corpse = 0x00002000,
    LifeStone = 0x00004000,
    Food = 0x00008000,
    Healer = 0x00010000,
    Lockpick = 0x00020000,
    Portal = 0x00040000,
    Admin = 0x00100000,
    FreePkStatus = 0x00200000,
    ImmuneCellRestrictions = 0x00400000,
    RequiresPackSlot = 0x00800000,
    Retained = 0x01000000,
    PkLiteStatus = 0x02000000,
    IncludesSecondHeader = 0x04000000,
    BindStone = 0x08000000,
    VolatileRare = 0x10000000,
    WieldOnUse = 0x20000000,
    WieldLeft = 0x40000000,
}

public static class PublicWeenieFlagsExtensions
{
    /// <summary>The object's bitfield as flags; None when it has none.</summary>
    public static PublicWeenieFlags FlagsOf(ClientObject obj) =>
        (PublicWeenieFlags)(obj.PublicWeenieBitfield ?? 0u);
}
```

In `SelectedObjectHealthPolicy.cs`, change each constant to alias the enum, keeping the names and visibility:

```csharp
public const uint BfPlayer = (uint)PublicWeenieFlags.Player;
public const uint BfAttackable = (uint)PublicWeenieFlags.Attackable;
public const uint BfPlayerKiller = (uint)PublicWeenieFlags.PlayerKiller;
public const uint BfFreePkStatus = (uint)PublicWeenieFlags.FreePkStatus;
public const uint BfPkLiteStatus = (uint)PublicWeenieFlags.PkLiteStatus;
```

In `RadarBlipColors.cs` (`RadarObjectTraits`), do the same for `BfPlayer`, `BfAttackable`, `BfPlayerKiller`, `BfHiddenAdmin`, `BfVendor`, `BfPortal`, `BfAdmin`, `BfFreePk` and `BfPkLite`. Leave the `0x80000000u` validity check as it is: it is not a PWD flag. Add `using AcDream.Core.Items;` where needed.

- [ ] **Step 4: Run the Verify filter.** Expected: all pass.

- [ ] **Step 5: Commit** with `git commit -m "Target Deck: PublicWeenieFlags enum; health policy and radar constants alias it"`.

---

### Task 2: TargetKind classifier

**Goal:** `TargetKindClassifier.Classify` maps any `ClientObject` to one of the ten kinds (plus a world-object sub-type), following the spec's precedence.

**Files:**
- Create: `src/AcDream.Core/Targeting/TargetKind.cs`
- Create: `src/AcDream.Core/Targeting/TargetKindClassifier.cs`
- Test: `tests/AcDream.Core.Tests/Targeting/TargetKindClassifierTests.cs`

**Acceptance Criteria:**
- [ ] Every row of the precedence table classifies correctly.
- [ ] Edge cases pass: a PK player is Player, a pet of anyone is Summon, a vendor creature is NPC, a corpse container is Corpse, an ItemType.LifeStone is WorldObject/LifeStone, a hook is WorldObject/Hook, and a door is WorldObject/Door.
- [ ] A null object classifies as `TargetKind.None`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~TargetKindClassifier'` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing test**

```csharp
using AcDream.Core.Items;
using AcDream.Core.Targeting;
using Xunit;

namespace AcDream.Core.Tests.Targeting;

public sealed class TargetKindClassifierTests
{
    private const uint Me = 0x50000001u;

    private static ClientObject Obj(uint id, ItemType type, PublicWeenieFlags flags = PublicWeenieFlags.None,
        uint petOwner = 0, uint hookType = 0, uint hookItemTypes = 0) =>
        new() { ObjectId = id, Type = type, PublicWeenieBitfield = (uint)flags, PetOwnerId = petOwner,
                HookType = hookType, HookItemTypes = hookItemTypes };

    public static TheoryData<ClientObject, TargetKind, WorldObjectKind> Cases => new()
    {
        { Obj(Me, ItemType.Creature, PublicWeenieFlags.Player), TargetKind.Self, WorldObjectKind.None },
        { Obj(2, ItemType.Creature, PublicWeenieFlags.Player | PublicWeenieFlags.PlayerKiller | PublicWeenieFlags.Attackable), TargetKind.Player, WorldObjectKind.None },
        { Obj(3, ItemType.Creature, PublicWeenieFlags.Attackable, petOwner: Me), TargetKind.Summon, WorldObjectKind.None },
        { Obj(4, ItemType.Creature, PublicWeenieFlags.None, petOwner: 0x50000009u), TargetKind.Summon, WorldObjectKind.None },
        { Obj(5, ItemType.Creature, PublicWeenieFlags.Attackable), TargetKind.Monster, WorldObjectKind.None },
        { Obj(6, ItemType.Creature, PublicWeenieFlags.Vendor), TargetKind.Npc, WorldObjectKind.None },
        { Obj(7, ItemType.Container, PublicWeenieFlags.Corpse | PublicWeenieFlags.Openable), TargetKind.Corpse, WorldObjectKind.None },
        { Obj(8, ItemType.Portal, PublicWeenieFlags.Portal), TargetKind.Portal, WorldObjectKind.None },
        { Obj(9, ItemType.Misc, PublicWeenieFlags.Door), TargetKind.WorldObject, WorldObjectKind.Door },
        { Obj(10, ItemType.LifeStone, PublicWeenieFlags.LifeStone), TargetKind.WorldObject, WorldObjectKind.LifeStone },
        { Obj(11, ItemType.Misc, PublicWeenieFlags.BindStone), TargetKind.WorldObject, WorldObjectKind.BindStone },
        { Obj(12, ItemType.Misc, PublicWeenieFlags.Stuck, hookType: 1, hookItemTypes: 0x2), TargetKind.WorldObject, WorldObjectKind.Hook },
        { Obj(13, ItemType.Misc, PublicWeenieFlags.Stuck), TargetKind.WorldObject, WorldObjectKind.Fixture },
        { Obj(14, ItemType.Container, PublicWeenieFlags.Openable), TargetKind.Container, WorldObjectKind.None },
        { Obj(15, ItemType.MeleeWeapon), TargetKind.Item, WorldObjectKind.None },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ClassifiesEachKind(ClientObject obj, TargetKind kind, WorldObjectKind sub)
    {
        TargetClassification c = TargetKindClassifier.Classify(obj, Me);
        Assert.Equal(kind, c.Kind);
        Assert.Equal(sub, c.WorldObject);
    }

    [Fact]
    public void NullIsNone() => Assert.Equal(TargetKind.None, TargetKindClassifier.Classify(null, Me).Kind);

    [Fact]
    public void YourOwnSummonIsMarkedOwn() =>
        Assert.True(TargetKindClassifier.Classify(Obj(3, ItemType.Creature, petOwner: Me), Me).OwnedByPlayer);
}
```

- [ ] **Step 2: Run it.** Expected: FAIL to compile.

- [ ] **Step 3: Implement.** `TargetKind.cs`:

```csharp
namespace AcDream.Core.Targeting;

/// <summary>What the selected object is, for the Target Deck.</summary>
public enum TargetKind { None, Monster, Summon, Player, Npc, Item, Corpse, Container, Portal, WorldObject, Self }

/// <summary>Which fixture a <see cref="TargetKind.WorldObject"/> is.</summary>
public enum WorldObjectKind { None, Door, LifeStone, BindStone, Hook, Fixture }

/// <param name="OwnedByPlayer">True for the player and for the player's own summon.</param>
public readonly record struct TargetClassification(TargetKind Kind, WorldObjectKind WorldObject, bool OwnedByPlayer);
```

`TargetKindClassifier.cs`:

```csharp
using AcDream.Core.Items;

namespace AcDream.Core.Targeting;

public static class TargetKindClassifier
{
    public static TargetClassification Classify(ClientObject? obj, uint playerId)
    {
        if (obj is null)
            return default;
        if (obj.ObjectId == playerId)
            return new(TargetKind.Self, WorldObjectKind.None, OwnedByPlayer: true);

        PublicWeenieFlags flags = PublicWeenieFlagsExtensions.FlagsOf(obj);
        bool creature = (obj.Type & ItemType.Creature) != 0;

        if ((flags & PublicWeenieFlags.Player) != 0)
            return new(TargetKind.Player, WorldObjectKind.None, false);
        if (obj.PetOwnerId != 0)
            return new(TargetKind.Summon, WorldObjectKind.None, obj.PetOwnerId == playerId);
        if (creature)
            return new((flags & PublicWeenieFlags.Attackable) != 0 ? TargetKind.Monster : TargetKind.Npc,
                WorldObjectKind.None, false);
        if ((flags & PublicWeenieFlags.Corpse) != 0)
            return new(TargetKind.Corpse, WorldObjectKind.None, false);
        if ((flags & PublicWeenieFlags.Portal) != 0 || (obj.Type & ItemType.Portal) != 0)
            return new(TargetKind.Portal, WorldObjectKind.None, false);

        WorldObjectKind fixture =
            (flags & PublicWeenieFlags.Door) != 0 ? WorldObjectKind.Door
            : (flags & PublicWeenieFlags.LifeStone) != 0 || (obj.Type & ItemType.LifeStone) != 0 ? WorldObjectKind.LifeStone
            : (flags & PublicWeenieFlags.BindStone) != 0 ? WorldObjectKind.BindStone
            : obj.IsHook ? WorldObjectKind.Hook
            : (obj.Type & ItemType.Container) == 0 && (flags & PublicWeenieFlags.Stuck) != 0 ? WorldObjectKind.Fixture
            : WorldObjectKind.None;
        if (fixture != WorldObjectKind.None)
            return new(TargetKind.WorldObject, fixture, false);

        if ((obj.Type & ItemType.Container) != 0)
            return new(TargetKind.Container, WorldObjectKind.None, false);
        return new(TargetKind.Item, WorldObjectKind.None, false);
    }
}
```

The "non-item useable world fixture" rule from the spec uses the `Stuck` flag, because levers and switches are stuck in place. A stuck container, such as a chest, stays a Container, because the container check runs after the fixture rule and the fixture rule excludes containers.

- [ ] **Step 4: Run the Verify filter.** Expected: all pass.

- [ ] **Step 5: Commit** with `git commit -m "Target Deck: TargetKind classifier"`.

---

### Task 3: TargetAppraisalLog and TargetAutoAppraisal

**Goal:** A throttled Automation-origin auto-ID policy for monsters, summons and corpses, plus a log of when each object was last identified and whether it succeeded.

**Files:**
- Create: `src/AcDream.Core/Targeting/TargetAppraisalLog.cs`
- Create: `src/AcDream.Runtime/Gameplay/TargetAutoAppraisal.cs`
- Test: `tests/AcDream.Core.Tests/Targeting/TargetAppraisalLogTests.cs`
- Test: `tests/AcDream.Runtime.Tests/Gameplay/TargetAutoAppraisalTests.cs`

**Acceptance Criteria:**
- [ ] Selecting a Monster, Summon or Corpse sends one request. Selecting any other kind sends none.
- [ ] A second trigger within 2.0 s does not send. One `Tick` at or after 2.0 s sends exactly one combined request.
- [ ] Changing the selection cancels a pending send. Clearing it sends nothing.
- [ ] A `DamageDealtAccepted` whose `DefenderName` equals the selected object's name triggers. Another name does not.
- [ ] A `HealthChanged` for the selected guid triggers only if its percent differs from the percent recorded at the last ID.
- [ ] Re-selecting a target with a successful ID younger than 2.0 s sends nothing.
- [ ] A failed ID (`Success == false`) is logged as failed, and later triggers still send.
- [ ] Requests go through the injected `Func<uint, bool>`. The wiring passes `RuntimeItemInteraction.TryAppraiseForAutomation`, which uses `AppraisalRequestOrigin.Automation`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~TargetAutoAppraisal|FullyQualifiedName~TargetAppraisalLog'` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests.** `TargetAppraisalLogTests.cs`:

```csharp
using AcDream.Core.Targeting;
using Xunit;

namespace AcDream.Core.Tests.Targeting;

public sealed class TargetAppraisalLogTests
{
    [Fact]
    public void RecordsTimeSuccessAndHealth()
    {
        var log = new TargetAppraisalLog();
        log.Record(7, success: true, healthPercent: 0.5f, now: 10.0);
        Assert.True(log.TryGet(7, out TargetAppraisalEntry e));
        Assert.Equal(10.0, e.At);
        Assert.True(e.Success);
        Assert.Equal(0.5f, e.HealthPercent);
        log.Forget(7);
        Assert.False(log.TryGet(7, out _));
    }
}
```

`TargetAutoAppraisalTests.cs`:

```csharp
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Selection;
using AcDream.Core.Targeting;
using AcDream.Runtime.Gameplay;
using Xunit;

namespace AcDream.Runtime.Tests.Gameplay;

public sealed class TargetAutoAppraisalTests
{
    private const uint Player = 0x50000001u, Monster = 0x80000010u, Chest = 0x80000020u, Corpse = 0x80000030u;
    private double _now = 100.0;

    private (TargetAutoAppraisal auto, SelectionState sel, CombatState combat, List<uint> sent, TargetAppraisalLog log) Make()
    {
        var objects = new ClientObjectTable();
        objects.AddOrUpdate(new ClientObject { ObjectId = Monster, Name = "Drudge Ravener", Type = ItemType.Creature, PublicWeenieBitfield = (uint)PublicWeenieFlags.Attackable });
        objects.AddOrUpdate(new ClientObject { ObjectId = Chest, Name = "Chest", Type = ItemType.Container, PublicWeenieBitfield = (uint)PublicWeenieFlags.Openable });
        objects.AddOrUpdate(new ClientObject { ObjectId = Corpse, Name = "Corpse of Drudge", Type = ItemType.Container, PublicWeenieBitfield = (uint)PublicWeenieFlags.Corpse });
        var sel = new SelectionState();
        var combat = new CombatState();
        var sent = new List<uint>();
        var log = new TargetAppraisalLog();
        var auto = new TargetAutoAppraisal(sel, objects, combat, log, () => Player,
            id => { sent.Add(id); return true; }, () => _now);
        return (auto, sel, combat, sent, log);
    }

    [Fact]
    public void SelectingAMonsterAppraisesIt()
    {
        var (auto, sel, _, sent, _) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            Assert.Equal(new[] { Monster }, sent);
        }
    }

    [Fact]
    public void SelectingACorpseAppraisesItButAChestIsLeftAlone()
    {
        var (auto, sel, _, sent, _) = Make();
        using (auto)
        {
            sel.Select(Chest, SelectionChangeSource.World);
            Assert.Empty(sent);
            sel.Select(Corpse, SelectionChangeSource.World);
            Assert.Equal(new[] { Corpse }, sent);
        }
    }

    [Fact]
    public void TriggersInsideTheGapCoalesceIntoOneSendWhenItEnds()
    {
        var (auto, sel, combat, sent, _) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            _now += 0.5;
            combat.OnAttackerNotification("Drudge Ravener", 1u, 10u, 0.1);
            _now += 0.5;
            combat.OnAttackerNotification("Drudge Ravener", 1u, 10u, 0.1);
            auto.Tick();
            Assert.Single(sent);
            _now += 1.0; // 2.0 s since the first send
            auto.Tick();
            Assert.Equal(new[] { Monster, Monster }, sent);
            auto.Tick();
            Assert.Equal(2, sent.Count);
        }
    }

    [Fact]
    public void AHitOnSomethingElseDoesNotTrigger()
    {
        var (auto, sel, combat, sent, _) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            _now += 3.0;
            combat.OnAttackerNotification("Olthoi Soldier", 1u, 10u, 0.1);
            auto.Tick();
            Assert.Single(sent);
        }
    }

    [Fact]
    public void AHealthChangeSinceTheLastIdTriggers()
    {
        var (auto, sel, combat, sent, log) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            auto.OnAppraised(Monster, success: true);   // health 1.0 recorded
            _now += 3.0;
            combat.OnUpdateHealth(Monster, 1.0f);       // unchanged: no trigger
            auto.Tick();
            Assert.Single(sent);
            combat.OnUpdateHealth(Monster, 0.6f);
            auto.Tick();
            Assert.Equal(2, sent.Count);
        }
    }

    [Fact]
    public void ChangingTheSelectionCancelsAPendingSend()
    {
        var (auto, sel, combat, sent, _) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            combat.OnAttackerNotification("Drudge Ravener", 1u, 10u, 0.1); // pending
            sel.Clear(SelectionChangeSource.System);
            _now += 5.0;
            auto.Tick();
            Assert.Single(sent);
        }
    }

    [Fact]
    public void ReselectingWithAFreshIdSendsNothing()
    {
        var (auto, sel, _, sent, _) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            auto.OnAppraised(Monster, success: true);
            _now += 0.5;
            sel.Clear(SelectionChangeSource.System);
            sel.Select(Monster, SelectionChangeSource.World);
            Assert.Single(sent);
        }
    }

    [Fact]
    public void AFailedIdIsLoggedAndRetriedOnTriggers()
    {
        var (auto, sel, combat, sent, log) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            auto.OnAppraised(Monster, success: false);
            Assert.True(log.TryGet(Monster, out TargetAppraisalEntry e));
            Assert.False(e.Success);
            _now += 2.5;
            combat.OnAttackerNotification("Drudge Ravener", 1u, 10u, 0.1);
            Assert.Equal(2, sent.Count);
        }
    }
}
```

When writing these tests, check `CombatState`'s constructor, and `OnAttackerNotification` / `OnUpdateHealth` (`src/AcDream.Core/Combat/CombatState.cs`). If `CombatState` needs constructor arguments, build it the way `tests/AcDream.Core.Tests/Combat/CombatState*Tests.cs` does.

- [ ] **Step 2: Run them.** Expected: FAIL to compile.

- [ ] **Step 3: Implement.** `TargetAppraisalLog.cs`:

```csharp
namespace AcDream.Core.Targeting;

/// <param name="At">Clock seconds when the ID arrived.</param>
/// <param name="HealthPercent">Live health (0..1) when it arrived, to detect later changes.</param>
public readonly record struct TargetAppraisalEntry(double At, bool Success, float HealthPercent);

/// <summary>When each object was last identified, and whether the assess succeeded.</summary>
public sealed class TargetAppraisalLog
{
    private readonly Dictionary<uint, TargetAppraisalEntry> _entries = new();

    public void Record(uint objectId, bool success, float healthPercent, double now) =>
        _entries[objectId] = new(now, success, healthPercent);

    public bool TryGet(uint objectId, out TargetAppraisalEntry entry) => _entries.TryGetValue(objectId, out entry);

    public void Forget(uint objectId) => _entries.Remove(objectId);

    public void Clear() => _entries.Clear();
}
```

`TargetAutoAppraisal.cs`:

```csharp
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Selection;
using AcDream.Core.Targeting;

namespace AcDream.Runtime.Gameplay;

/// <summary>
/// Identifies the selected monster, summon or corpse so the Target Deck can
/// show its stamina and mana: once on select, then again when something has
/// probably changed them (the player's hit lands on it, or its health moves),
/// never more often than <see cref="MinimumGapSeconds"/> per target. The
/// request goes out with the Automation origin, so the examination window is
/// never opened or switched by it.
/// </summary>
public sealed class TargetAutoAppraisal : IDisposable
{
    public const double MinimumGapSeconds = 2.0;

    private readonly SelectionState _selection;
    private readonly ClientObjectTable _objects;
    private readonly CombatState _combat;
    private readonly TargetAppraisalLog _log;
    private readonly Func<uint> _playerGuid;
    private readonly Func<uint, bool> _requestAppraisal;
    private readonly Func<double> _clock;
    private uint _target;
    private double _lastSentAt = double.NegativeInfinity;
    private bool _pending;
    private bool _disposed;

    public TargetAutoAppraisal(SelectionState selection, ClientObjectTable objects, CombatState combat,
        TargetAppraisalLog log, Func<uint> playerGuid, Func<uint, bool> requestAppraisal, Func<double> clock)
    {
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _playerGuid = playerGuid ?? throw new ArgumentNullException(nameof(playerGuid));
        _requestAppraisal = requestAppraisal ?? throw new ArgumentNullException(nameof(requestAppraisal));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _selection.Changed += OnSelectionChanged;
        _combat.DamageDealtAccepted += OnDamageDealt;
        _combat.HealthChanged += OnHealthChanged;
        _objects.ObjectRemoved += OnObjectRemoved;
        Retarget(_selection.SelectedObjectId ?? 0u);
    }

    /// <summary>The object auto-ID is following; zero when none qualifies.</summary>
    public uint TargetId => _target;

    public static bool Qualifies(TargetKind kind) =>
        kind is TargetKind.Monster or TargetKind.Summon or TargetKind.Corpse;

    /// <summary>Sends a combined trigger once the gap has passed. Call every frame.</summary>
    public void Tick()
    {
        if (_pending && _target != 0u && _clock() - _lastSentAt >= MinimumGapSeconds)
            Send();
    }

    /// <summary>An identify reply arrived for <paramref name="objectId"/> (any origin).</summary>
    public void OnAppraised(uint objectId, bool success) =>
        _log.Record(objectId, success, _combat.GetHealthPercent(objectId), _clock());

    private void OnSelectionChanged(SelectionTransition _) => Retarget(_selection.SelectedObjectId ?? 0u);

    private void Retarget(uint selected)
    {
        _pending = false;
        _lastSentAt = double.NegativeInfinity;
        TargetKind kind = TargetKindClassifier.Classify(_objects.Get(selected), _playerGuid()).Kind;
        _target = selected != 0u && Qualifies(kind) ? selected : 0u;
        if (_target == 0u)
            return;
        if (_log.TryGet(_target, out TargetAppraisalEntry e) && e.Success && _clock() - e.At < MinimumGapSeconds)
            return;
        Trigger();
    }

    private void OnDamageDealt(DamageDealt hit)
    {
        if (_target != 0u && _objects.Get(_target) is { } obj
            && string.Equals(obj.Name, hit.DefenderName, StringComparison.Ordinal))
            Trigger();
    }

    private void OnHealthChanged(uint guid, float percent)
    {
        if (guid != _target || _target == 0u)
            return;
        if (_log.TryGet(guid, out TargetAppraisalEntry e) && Math.Abs(e.HealthPercent - percent) < 0.0001f)
            return;
        Trigger();
    }

    private void OnObjectRemoved(ClientObject obj) => _log.Forget(obj.ObjectId);

    private void Trigger()
    {
        _pending = true;
        Tick();
    }

    private void Send()
    {
        _pending = false;
        _lastSentAt = _clock();
        _requestAppraisal(_target);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _selection.Changed -= OnSelectionChanged;
        _combat.DamageDealtAccepted -= OnDamageDealt;
        _combat.HealthChanged -= OnHealthChanged;
        _objects.ObjectRemoved -= OnObjectRemoved;
    }
}
```

If `ClientObjectTable.ObjectRemoved` or `SelectionState.Changed` turn out to be raised off the UI thread, check how `RuntimeSelectedObjectHealthQuery` handles it and do the same. Both are expected to be raised on the session thread, which is the UI thread.

- [ ] **Step 4: Run the Verify filter.** Expected: all pass.

- [ ] **Step 5: Commit** with `git commit -m "Target Deck: throttled auto-ID for monsters, summons and corpses"`.

---

### Task 4: TargetPlateModel and builder

**Goal:** An immutable snapshot of everything the plate draws, built for each kind from the object, combat state, appraisal log, player and plugin content.

**Files:**
- Create: `src/AcDream.Core/Targeting/TargetPlateModel.cs`
- Create: `src/AcDream.Core/Targeting/TargetPlateModelBuilder.cs`
- Test: `tests/AcDream.Core.Tests/Targeting/TargetPlateModelBuilderTests.cs`

**Acceptance Criteria:**
- [ ] Each of the ten kinds produces the identity line and studs from the spec's per-kind table, as adjusted by plan-time adjustments 3 and 4 (no Mark, no Dismiss, trimmed ⋯).
- [ ] The vitals state is one of Fresh, Stale (>10 s), Failed or Unknown. Only creature kinds have vitals; for the others `Vitals` is null.
- [ ] A kind that is not auto-identified and has no appraisal entry has `NeedsAssess == true`, and its ID-only rows are replaced by one "Assess for details" row.
- [ ] Studs with a missing precondition (Give with no pack item chosen) are `Enabled == false` with a non-empty `DisabledReason`.
- [ ] Plugin rows pass through unchanged: the builder takes them as input and the host budgets them in Task 6.
- [ ] The level con colour compares the target's level with the player's: Red is 5 or more above, Yellow within ±4, Blue 5 or more below, Grey unknown.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~TargetPlateModelBuilder'` → all pass.

**Steps:**

- [ ] **Step 1: Write the model types.** These are needed for the test to compile. `TargetPlateModel.cs`:

```csharp
namespace AcDream.Core.Targeting;

public enum TargetVitalsState { Unknown, Fresh, Stale, Failed }
public enum TargetCon { Unknown, Lower, Even, Higher }
public enum TargetChip { None, Yours, Fellow, Pk, Npk, PkLite }

/// <summary>The actions a plate can offer; the deck maps each to an icon and an action call.</summary>
public enum TargetAction
{
    None, Assess, UseItemOn, Follow, More, Trade, Give, Tell, FellowshipInvite,
    UseOrTalk, PickUpOrUse, Wield, Open, UseKey, UsePortal, Use, CharacterSheet,
    Examine, CopyName, SwearAllegiance,
}

public readonly record struct TargetStud(TargetAction Action, bool Enabled, string? DisabledReason = null);
public readonly record struct TargetRow(string Label, string Value, bool Dim = false);
public readonly record struct TargetVitals(
    float Health, float? Stamina, float? Mana, uint? StaminaMax, uint? ManaMax, TargetVitalsState State);

/// <summary>A row a plugin added. Colour is RGBA, or null for the default ink.</summary>
public readonly record struct TargetPluginRow(string Label, string Value, uint? Rgba, string? Tooltip);

/// <summary>A ⋯ menu entry a plugin added, grouped under <paramref name="PluginName"/>.</summary>
public readonly record struct TargetPluginAction(string PluginId, string PluginName, string ActionId, string Label, bool Enabled);

public sealed record TargetPlateModel(
    uint ObjectId,
    TargetKind Kind,
    WorldObjectKind WorldObject,
    string Name,
    string SubLine,
    int? Level,
    TargetCon Con,
    IReadOnlyList<TargetChip> Chips,
    TargetVitals? Vitals,
    IReadOnlyList<TargetRow> Rows,
    IReadOnlyList<TargetStud> Studs,
    IReadOnlyList<TargetAction> MoreMenu,
    IReadOnlyList<TargetPluginRow> PluginRows,
    string? PluginOverflowTooltip,
    IReadOnlyList<TargetPluginAction> PluginActions,
    bool NeedsAssess);
```

- [ ] **Step 2: Write the failing tests.**

```csharp
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Properties;
using AcDream.Core.Targeting;
using Xunit;

namespace AcDream.Core.Tests.Targeting;

public sealed class TargetPlateModelBuilderTests
{
    private const uint Me = 0x50000001u;
    private double _now = 50.0;

    private TargetPlateModel? Build(ClientObject obj, CombatState? combat = null, TargetAppraisalLog? log = null,
        int playerLevel = 100, uint selectedPackItem = 0, float distance = 4f)
    {
        var input = new TargetPlateInput(
            Object: obj, PlayerId: Me, PlayerLevel: playerLevel, Combat: combat ?? new CombatState(),
            Log: log ?? new TargetAppraisalLog(), Now: _now, DistanceMeters: distance,
            SelectedPackItemId: selectedPackItem, IsFellow: _ => false,
            PluginRows: Array.Empty<TargetPluginRow>(), PluginOverflowTooltip: null,
            PluginActions: Array.Empty<TargetPluginAction>());
        return TargetPlateModelBuilder.Build(input);
    }

    private static ClientObject Monster(int level = 45)
    {
        var o = new ClientObject { ObjectId = 7, Name = "Drudge Ravener", Type = ItemType.Creature,
            PublicWeenieBitfield = (uint)PublicWeenieFlags.Attackable };
        o.Properties.Ints[(uint)PropertyInt.Level] = level;
        return o;
    }

    [Fact]
    public void AMonsterHasVitalsAndNoAttackStud()
    {
        TargetPlateModel m = Build(Monster())!;
        Assert.Equal(TargetKind.Monster, m.Kind);
        Assert.NotNull(m.Vitals);
        Assert.Equal(new[] { TargetAction.Assess, TargetAction.UseItemOn, TargetAction.Follow, TargetAction.More },
            m.Studs.Select(s => s.Action));
        Assert.Contains(TargetAction.Examine, m.MoreMenu);
        Assert.Contains(TargetAction.CopyName, m.MoreMenu);
    }

    [Theory]
    [InlineData(100, 106, TargetCon.Higher)]
    [InlineData(100, 104, TargetCon.Even)]
    [InlineData(100, 95, TargetCon.Lower)]
    public void ConComparesLevels(int player, int target, TargetCon expected) =>
        Assert.Equal(expected, Build(Monster(target), playerLevel: player)!.Con);

    [Fact]
    public void VitalsAreStaleAfterTenSeconds()
    {
        var log = new TargetAppraisalLog();
        log.Record(7, true, 1f, now: 30.0);
        Assert.Equal(TargetVitalsState.Stale, Build(Monster(), log: log)!.Vitals!.Value.State);
        log.Record(7, true, 1f, now: 45.0);
        Assert.Equal(TargetVitalsState.Fresh, Build(Monster(), log: log)!.Vitals!.Value.State);
        log.Record(7, false, 1f, now: 49.0);
        Assert.Equal(TargetVitalsState.Failed, Build(Monster(), log: log)!.Vitals!.Value.State);
    }

    [Fact]
    public void AnUnassessedItemAsksForAssess()
    {
        var sword = new ClientObject { ObjectId = 9, Name = "Fine Silver Long Sword", Type = ItemType.MeleeWeapon, Value = 4200, Burden = 320 };
        TargetPlateModel m = Build(sword)!;
        Assert.Equal(TargetKind.Item, m.Kind);
        Assert.True(m.NeedsAssess);
        Assert.Null(m.Vitals);
        Assert.Contains(m.Rows, r => r.Label == "Value" && r.Value == "4,200");
        Assert.Contains(m.Rows, r => r.Value == "Assess for details");
    }

    [Fact]
    public void GiveIsDisabledWithoutAPackItem()
    {
        var player = new ClientObject { ObjectId = 2, Name = "Sammy", Type = ItemType.Creature, PublicWeenieBitfield = (uint)PublicWeenieFlags.Player };
        TargetStud give = Build(player)!.Studs.Single(s => s.Action == TargetAction.Give);
        Assert.False(give.Enabled);
        Assert.False(string.IsNullOrEmpty(give.DisabledReason));
        Assert.True(Build(player, selectedPackItem: 0x80001234u)!.Studs.Single(s => s.Action == TargetAction.Give).Enabled);
    }

    [Fact]
    public void EveryKindHasFourOrFewerStudsEndingInMore()
    {
        ClientObject[] objs =
        {
            Monster(),
            new() { ObjectId = 3, Name = "Wolf", Type = ItemType.Creature, PetOwnerId = Me },
            new() { ObjectId = 2, Name = "Sammy", Type = ItemType.Creature, PublicWeenieBitfield = (uint)PublicWeenieFlags.Player },
            new() { ObjectId = 4, Name = "Thorsten", Type = ItemType.Creature, PublicWeenieBitfield = (uint)PublicWeenieFlags.Vendor },
            new() { ObjectId = 5, Name = "Sword", Type = ItemType.MeleeWeapon },
            new() { ObjectId = 6, Name = "Corpse of Drudge", Type = ItemType.Container, PublicWeenieBitfield = (uint)PublicWeenieFlags.Corpse },
            new() { ObjectId = 8, Name = "Chest", Type = ItemType.Container },
            new() { ObjectId = 10, Name = "Portal to Ayan Baqur", Type = ItemType.Portal, PublicWeenieBitfield = (uint)PublicWeenieFlags.Portal },
            new() { ObjectId = 11, Name = "Door", Type = ItemType.Misc, PublicWeenieBitfield = (uint)PublicWeenieFlags.Door },
            new() { ObjectId = Me, Name = "Me", Type = ItemType.Creature, PublicWeenieBitfield = (uint)PublicWeenieFlags.Player },
        };
        foreach (ClientObject o in objs)
        {
            TargetPlateModel m = Build(o)!;
            Assert.InRange(m.Studs.Count, 2, 4);
            Assert.Equal(TargetAction.More, m.Studs[^1].Action);
            Assert.DoesNotContain(m.Studs, s => s.Action == TargetAction.None);
        }
    }
}
```

- [ ] **Step 3: Run them.** Expected: FAIL to compile (no `TargetPlateInput` or `TargetPlateModelBuilder`).

- [ ] **Step 4: Implement `TargetPlateModelBuilder.cs`.**

```csharp
using System.Globalization;
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Properties;

namespace AcDream.Core.Targeting;

public sealed record TargetPlateInput(
    ClientObject? Object, uint PlayerId, int PlayerLevel, CombatState Combat, TargetAppraisalLog Log,
    double Now, float? DistanceMeters, uint SelectedPackItemId, Func<uint, bool> IsFellow,
    IReadOnlyList<TargetPluginRow> PluginRows, string? PluginOverflowTooltip,
    IReadOnlyList<TargetPluginAction> PluginActions);

public static class TargetPlateModelBuilder
{
    public const double StaleAfterSeconds = 10.0;
    private const string NoPackItem = "Select an item in your pack first";

    public static TargetPlateModel? Build(TargetPlateInput input)
    {
        ClientObject? obj = input.Object;
        if (obj is null)
            return null;
        TargetClassification c = TargetKindClassifier.Classify(obj, input.PlayerId);
        bool appraised = input.Log.TryGet(obj.ObjectId, out TargetAppraisalEntry entry);
        int? level = obj.Properties.Ints.TryGetValue((uint)PropertyInt.Level, out int lv) && lv > 0 ? lv : null;
        bool hasPackItem = input.SelectedPackItemId != 0u;
        var rows = new List<TargetRow>();
        var chips = new List<TargetChip>();
        TargetVitals? vitals = null;
        TargetStud[] studs;
        TargetAction[] more = { TargetAction.Examine, TargetAction.CopyName };
        string kindWord;
        bool needsAssess = false;

        TargetStud Give() => new(TargetAction.Give, hasPackItem, hasPackItem ? null : NoPackItem);
        TargetStud UseOn() => new(TargetAction.UseItemOn, hasPackItem, hasPackItem ? null : NoPackItem);
        TargetStud Key() => new(TargetAction.UseKey, hasPackItem, hasPackItem ? null : "Select a key or lockpick in your pack first");
        TargetStud S(TargetAction a) => new(a, true);

        switch (c.Kind)
        {
            case TargetKind.Monster:
                kindWord = "MONSTER";
                vitals = Vitals(input, obj.ObjectId, appraised, entry);
                studs = new[] { S(TargetAction.Assess), UseOn(), S(TargetAction.Follow), S(TargetAction.More) };
                break;
            case TargetKind.Summon:
                kindWord = "SUMMON";
                if (c.OwnedByPlayer) chips.Add(TargetChip.Yours);
                vitals = Vitals(input, obj.ObjectId, appraised, entry);
                studs = new[] { S(TargetAction.Assess), S(TargetAction.Follow), S(TargetAction.More) };
                break;
            case TargetKind.Player:
                kindWord = "PLAYER";
                chips.Add(PkChip(obj));
                if (input.IsFellow(obj.ObjectId)) chips.Add(TargetChip.Fellow);
                if (appraised || input.IsFellow(obj.ObjectId))
                    vitals = Vitals(input, obj.ObjectId, appraised, entry);
                studs = new[] { S(TargetAction.Trade), Give(), S(TargetAction.Tell), S(TargetAction.More) };
                more = new[] { TargetAction.FellowshipInvite, TargetAction.Follow, TargetAction.SwearAllegiance,
                               TargetAction.Examine, TargetAction.CopyName };
                break;
            case TargetKind.Npc:
                kindWord = (PublicWeenieFlagsExtensions.FlagsOf(obj) & PublicWeenieFlags.Vendor) != 0 ? "VENDOR" : "NPC";
                studs = new[] { S(TargetAction.UseOrTalk), Give(), S(TargetAction.Follow), S(TargetAction.More) };
                break;
            case TargetKind.Item:
                kindWord = "ITEM";
                rows.Add(new("Value", obj.Value.ToString("N0", CultureInfo.InvariantCulture)));
                rows.Add(new("Burden", obj.Burden.ToString("N0", CultureInfo.InvariantCulture)));
                if (!appraised) { needsAssess = true; rows.Add(new("", "Assess for details", Dim: true)); }
                else AddItemDetails(obj, rows);
                studs = new[] { S(TargetAction.PickUpOrUse), S(TargetAction.Wield), S(TargetAction.More) };
                break;
            case TargetKind.Corpse:
                kindWord = "CORPSE";
                studs = new[] { S(TargetAction.Open), S(TargetAction.More) };
                break;
            case TargetKind.Container:
                kindWord = "CONTAINER";
                if (!appraised) { needsAssess = true; rows.Add(new("", "Assess for details", Dim: true)); }
                studs = new[] { S(TargetAction.Open), Key(), S(TargetAction.More) };
                break;
            case TargetKind.Portal:
                kindWord = "PORTAL";
                AddPortalLevels(obj, input.PlayerLevel, rows, appraised, ref needsAssess);
                studs = new[] { S(TargetAction.UsePortal), S(TargetAction.More) };
                break;
            case TargetKind.WorldObject:
                kindWord = c.WorldObject switch
                {
                    WorldObjectKind.Door => "DOOR", WorldObjectKind.LifeStone => "LIFESTONE",
                    WorldObjectKind.BindStone => "BINDSTONE", WorldObjectKind.Hook => "HOOK", _ => "OBJECT",
                };
                studs = c.WorldObject == WorldObjectKind.Door
                    ? new[] { S(TargetAction.Use), Key(), S(TargetAction.More) }
                    : new[] { S(TargetAction.Use), S(TargetAction.More) };
                break;
            case TargetKind.Self:
                kindWord = "YOU";
                studs = new[] { S(TargetAction.CharacterSheet), S(TargetAction.More) };
                more = new[] { TargetAction.Examine };
                break;
            default:
                return null;
        }

        string sub = level is int l && c.Kind is TargetKind.Monster or TargetKind.Summon or TargetKind.Player
            ? $"LEVEL {l} · {kindWord}" : kindWord;
        if (input.DistanceMeters is float d && c.Kind != TargetKind.Self)
            sub += $" · {MathF.Round(d):0} m";

        return new TargetPlateModel(obj.ObjectId, c.Kind, c.WorldObject, obj.Name, sub, level,
            Con(level, input.PlayerLevel, c.Kind), chips, vitals, rows, studs, more,
            input.PluginRows, input.PluginOverflowTooltip, input.PluginActions, needsAssess);
    }

    private static TargetVitals Vitals(TargetPlateInput input, uint id, bool appraised, TargetAppraisalEntry entry)
    {
        float health = input.Combat.GetHealthPercent(id);
        TargetVitalsState state = !appraised ? TargetVitalsState.Unknown
            : !entry.Success ? TargetVitalsState.Failed
            : input.Now - entry.At > StaleAfterSeconds ? TargetVitalsState.Stale
            : TargetVitalsState.Fresh;
        if (state is TargetVitalsState.Fresh or TargetVitalsState.Stale
            && input.Combat.TryGetCreatureVitals(id, out CombatState.CreatureVitals v))
        {
            float? Frac(uint? cur, uint? max) => cur is uint c && max is uint m && m > 0 ? Math.Clamp(c / (float)m, 0f, 1f) : null;
            return new(health, Frac(v.Stamina, v.StaminaMax), Frac(v.Mana, v.ManaMax), v.StaminaMax, v.ManaMax, state);
        }
        return new(health, null, null, null, null, state);
    }

    private static TargetCon Con(int? level, int playerLevel, TargetKind kind)
    {
        if (level is not int l || playerLevel <= 0 || kind is not (TargetKind.Monster or TargetKind.Summon))
            return TargetCon.Unknown;
        int diff = l - playerLevel;
        return diff >= 5 ? TargetCon.Higher : diff <= -5 ? TargetCon.Lower : TargetCon.Even;
    }

    private static TargetChip PkChip(ClientObject obj)
    {
        PublicWeenieFlags f = PublicWeenieFlagsExtensions.FlagsOf(obj);
        return (f & PublicWeenieFlags.PlayerKiller) != 0 ? TargetChip.Pk
            : (f & PublicWeenieFlags.PkLiteStatus) != 0 ? TargetChip.PkLite
            : TargetChip.Npk;
    }

    private static void AddItemDetails(ClientObject obj, List<TargetRow> rows)
    {
        int work = obj.Properties.GetInt((uint)PropertyInt.ItemWorkmanship);
        int spells = obj.AppraisedSpellIds.Count;
        if (work > 0 || spells > 0)
            rows.Add(new("Work", spells > 0 ? $"{work} · {spells} spells" : $"{work}"));
        int wieldDiff = obj.Properties.GetInt((uint)PropertyInt.WieldDifficulty);
        if (wieldDiff > 0)
            rows.Add(new("Wield", wieldDiff.ToString(CultureInfo.InvariantCulture)));
    }

    private static void AddPortalLevels(ClientObject obj, int playerLevel, List<TargetRow> rows, bool appraised, ref bool needsAssess)
    {
        int min = obj.Properties.GetInt((uint)PropertyInt.MinLevel);
        int max = obj.Properties.GetInt((uint)PropertyInt.MaxLevel);
        if (min == 0 && max == 0)
        {
            if (!appraised) { needsAssess = true; rows.Add(new("", "Assess for details", Dim: true)); }
            return;
        }
        bool ok = (min == 0 || playerLevel >= min) && (max == 0 || playerLevel <= max);
        rows.Add(new("Levels", $"{(min == 0 ? 1 : min)} – {(max == 0 ? "any" : max.ToString(CultureInfo.InvariantCulture))}", Dim: !ok));
    }
}
```

Check that `PropertyInt.ItemWorkmanship`, `WieldDifficulty`, `MinLevel` and `MaxLevel` exist in `src/AcDream.Core/Properties/PropertyInt.cs`; they are the standard ACE names. If one is named differently, use the existing name. Do not add new enum members.

- [ ] **Step 5: Run the Verify filter.** Expected: all pass.

- [ ] **Step 6: Commit** with `git commit -m "Target Deck: plate model and per-kind builder"`.

---

### Task 5: Plugin contract (TargetPlate)

**Goal:** The public plugin contract types with a no-op default on `IUiRegistry` and `IScopedUiRegistry`, documented, at contract version `0.1.28-fork.2`.

**Files:**
- Create: `src/AcDream.Plugin.Abstractions/TargetPlate.cs`
- Modify: `src/AcDream.Plugin.Abstractions/IUiRegistry.cs` (next to `WorldMarkers`, line ~280, and `WorldMarkersFor`, line ~467)
- Modify: `src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj:39`
- Modify: `docs/plugin-api.md` (new "## Target plate" section after "## World markers")
- Test: `tests/AcDream.Plugin.Tests/PluginTargetPlateContractTests.cs`

**Acceptance Criteria:**
- [ ] `NoOpUiRegistry.Instance.TargetPlate.IsAvailable` is false. `AddProvider` returns a disposable that does nothing, and `Refresh` does nothing.
- [ ] `IScopedUiRegistry.TargetPlateFor(owner)` returns `NoOpPluginTargetPlate.Instance` by default.
- [ ] `PluginTargetPlateContent` exposes `MaximumRows = 2` and `MaximumActions = 4` as constants.
- [ ] `<ForkContractVersion>` reads `0.1.28-fork.2`.
- [ ] `docs/plugin-api.md` documents every type and rule from the spec's "Plugin hooks" section and says "Needs contract `0.1.28-fork.2`."

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~PluginTargetPlateContract'` → all pass. `grep -n "0.1.28-fork.2" src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj docs/plugin-api.md` → both match.

**Steps:**

- [ ] **Step 1: Write the failing test**

```csharp
using AcDream.Plugin.Abstractions;
using Xunit;

namespace AcDream.Plugin.Tests;

public sealed class PluginTargetPlateContractTests
{
    private sealed class Provider : IPluginTargetPlateProvider
    {
        public PluginTargetPlateContent? Describe(PluginTargetPlateQuery query) => null;
        public void OnAction(uint objectId, string actionId) { }
    }

    [Fact]
    public void AHostThatDrawsNothingHasNoTargetPlate()
    {
        IUiRegistry ui = NoOpUiRegistry.Instance;
        IScopedUiRegistry scoped = NoOpUiRegistry.Instance;

        Assert.False(ui.TargetPlate.IsAvailable);
        using (ui.TargetPlate.AddProvider(new Provider())) { }
        ui.TargetPlate.Refresh(1u);
        Assert.Same(NoOpPluginTargetPlate.Instance, scoped.TargetPlateFor(new PluginUiOwner("example.plugin", "Example")));
    }

    [Fact]
    public void ContentLimitsArePublished()
    {
        Assert.Equal(2, PluginTargetPlateContent.MaximumRows);
        Assert.Equal(4, PluginTargetPlateContent.MaximumActions);
    }
}
```

- [ ] **Step 2: Run it.** Expected: FAIL to compile.

- [ ] **Step 3: Implement `TargetPlate.cs`.** Write doc comments on every public member, in the same style as `WorldMarkers.cs`.

```csharp
namespace AcDream.Plugin.Abstractions;

/// <summary>What the selected object is, as the Target Deck sorts it.</summary>
public enum PluginTargetKind { None, Monster, Summon, Player, Npc, Item, Corpse, Container, Portal, WorldObject, Self }

/// <summary>
/// The selected object a provider is asked to describe. Vitals are fractions
/// (0..1); stamina and mana are null until an identify has answered with them.
/// </summary>
public readonly record struct PluginTargetPlateQuery(
    uint ObjectId,
    PluginTargetKind Kind,
    string Name,
    uint WeenieClassId,
    int? Level,
    bool OwnedByPlayer,
    bool Identified,
    float? Health,
    float? Stamina,
    float? Mana);

/// <summary>One line a plugin adds to the plate. A null colour uses the plate's ink.</summary>
public readonly record struct PluginTargetPlateRow(string Label, string Value, PluginColor? Color = null, string? Tooltip = null);

/// <summary>One ⋯ menu entry a plugin adds; <see cref="IPluginTargetPlateProvider.OnAction"/> gets its id.</summary>
public readonly record struct PluginTargetPlateAction(string Id, string Label, bool Enabled = true);

/// <summary>What a provider adds for one object. Rows past <see cref="MaximumRows"/> and actions past <see cref="MaximumActions"/> are dropped.</summary>
public sealed record PluginTargetPlateContent(
    IReadOnlyList<PluginTargetPlateRow> Rows,
    IReadOnlyList<PluginTargetPlateAction> Actions)
{
    public const int MaximumRows = 2;
    public const int MaximumActions = 4;
}

/// <summary>
/// Describes selected objects for the Target Deck. Called on the UI thread when
/// the selection changes, when an identify for the object arrives, and after
/// <see cref="IPluginTargetPlate.Refresh"/>; never every frame. Must return
/// quickly: a provider with a slow lookup returns null, then calls Refresh.
/// </summary>
public interface IPluginTargetPlateProvider
{
    PluginTargetPlateContent? Describe(PluginTargetPlateQuery query);
    void OnAction(uint objectId, string actionId);
}

/// <summary>The Target Deck's plugin surface.</summary>
public interface IPluginTargetPlate
{
    /// <summary>False while the Modern HUD is off, or on a host with no window.</summary>
    bool IsAvailable { get; }

    /// <summary>Adds a provider; disposing the result removes its rows and actions at once.</summary>
    IDisposable AddProvider(IPluginTargetPlateProvider provider);

    /// <summary>Asks every provider of this plugin to describe <paramref name="objectId"/> again.</summary>
    void Refresh(uint objectId);
}

/// <summary>The target plate of a host that draws nothing.</summary>
public sealed class NoOpPluginTargetPlate : IPluginTargetPlate
{
    public static NoOpPluginTargetPlate Instance { get; } = new();
    private NoOpPluginTargetPlate() { }
    public bool IsAvailable => false;
    public IDisposable AddProvider(IPluginTargetPlateProvider provider) => Nothing.Instance;
    public void Refresh(uint objectId) { }

    private sealed class Nothing : IDisposable
    {
        public static Nothing Instance { get; } = new();
        public void Dispose() { }
    }
}
```

In `IUiRegistry.cs`, add this after the `WorldMarkers` member:

```csharp
/// <summary>
/// Rows and ⋯ menu actions this plugin adds to the Modern HUD Target Deck,
/// the plate that describes the selected object. Inert on a host that draws
/// nothing or while the Modern HUD is off (<see cref="IPluginTargetPlate.IsAvailable"/>).
/// </summary>
IPluginTargetPlate TargetPlate => NoOpPluginTargetPlate.Instance;
```

In `IScopedUiRegistry`, add this after `WorldMarkersFor`:

```csharp
/// <summary>
/// One plugin's target-plate surface, the same object on every call for the
/// same owner until it is disposed; disposing it removes every provider the
/// plugin added.
/// </summary>
IPluginTargetPlate TargetPlateFor(PluginUiOwner owner) => NoOpPluginTargetPlate.Instance;
```

In the csproj, change line 39 to `<ForkContractVersion>0.1.28-fork.2</ForkContractVersion>`.

In `docs/plugin-api.md`, add a "## Target plate" section after "## World markers", in the same tone as the neighbouring sections. It should cover:
- what the plate is;
- `IsAvailable`, `AddProvider` and `Refresh`;
- what `Describe` gets and returns;
- when `Describe` is called;
- the two-row and four-action limits, the two-line shared plate budget with overflow in the tooltip, and registration order;
- that ⋯ actions are grouped under the plugin's name;
- fault isolation;
- that dispose removes rows at once;
- a short example provider;
- "Needs contract `0.1.28-fork.2`."

- [ ] **Step 4: Run the Verify commands.** Expected: pass and match.

- [ ] **Step 5: Commit** with `git commit -m "Target Deck: plugin TargetPlate contract (0.1.28-fork.2)"`.

---

### Task 6: Host target-plate store and scoping

**Goal:** Plugin providers are registered per plugin. The host asks them, budgets their rows, catches their faults and routes their actions. `ScopedPluginHost` hands each plugin its own surface.

**Files:**
- Create: `src/AcDream.App/Plugins/PluginTargetPlateStore.cs`
- Modify: `src/AcDream.App/Plugins/BufferedUiRegistry.cs` (next to `WorldMarkerStore`, lines ~218-225 and ~766)
- Modify: `src/AcDream.Core/Plugins/ScopedPluginHost.cs` (in `ScopedUiRegistry`, next to `_worldMarkers`, lines ~2310-2331)
- Test: `tests/AcDream.App.Tests/Plugins/PluginTargetPlateStoreTests.cs`
- Test: `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryTargetPlateTests.cs`

**Acceptance Criteria:**
- [ ] `PluginTargetPlateStore.Describe(query)` returns the budgeted rows, the overflow tooltip text and the grouped actions, ordered by registration.
- [ ] Two providers with two rows each give provider 1's first row and provider 2's first row on the plate. The other two rows are in the overflow tooltip as "Label: Value" lines.
- [ ] A provider that returns three rows keeps two, and its plugin log gets exactly one warning.
- [ ] A provider that throws is skipped for that object and logged once. Other providers still contribute.
- [ ] Results are cached per object id. `Describe` is not re-invoked until `Invalidate(objectId)`, a `Refresh`, or a new object.
- [ ] Disposing a registration removes its rows and actions. The next `Describe` reflects that without needing a refresh.
- [ ] `IsAvailable` returns the store's `Available` delegate (set at mount to Modern HUD on).
- [ ] `ScopedUiRegistry.TargetPlate` asks `_inner.TargetPlateFor(_owner)` once, tracks it if it's `IDisposable`, and throws `ObjectDisposedException` after dispose. This mirrors `WorldMarkers`.
- [ ] `RunAction(pluginId, objectId, actionId)` calls that plugin's `OnAction`, catching and logging any exception.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~PluginTargetPlateStore|FullyQualifiedName~ScopedUiRegistryTargetPlate'` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing store tests.**

```csharp
using AcDream.App.Plugins;
using AcDream.Core.Targeting;
using AcDream.Plugin.Abstractions;
using Xunit;

namespace AcDream.App.Tests.Plugins;

public sealed class PluginTargetPlateStoreTests
{
    private sealed class P(Func<PluginTargetPlateQuery, PluginTargetPlateContent?> describe) : IPluginTargetPlateProvider
    {
        public int Calls;
        public List<(uint, string)> Actions = new();
        public PluginTargetPlateContent? Describe(PluginTargetPlateQuery q) { Calls++; return describe(q); }
        public void OnAction(uint objectId, string actionId) => Actions.Add((objectId, actionId));
    }

    private static PluginTargetPlateContent Rows(params string[] labels) =>
        new(labels.Select(l => new PluginTargetPlateRow(l, l + "v")).ToArray(), Array.Empty<PluginTargetPlateAction>());

    private static readonly PluginTargetPlateQuery Q = new(7, PluginTargetKind.Monster, "Drudge", 1, 45, false, true, 1f, null, null);

    [Fact]
    public void TwoProvidersShareTheTwoLineBudgetInRegistrationOrder()
    {
        var warnings = new List<string>();
        var store = new PluginTargetPlateStore(() => true, (id, msg) => warnings.Add($"{id}:{msg}"));
        store.For("a", "Alpha").AddProvider(new P(_ => Rows("a1", "a2")));
        store.For("b", "Beta").AddProvider(new P(_ => Rows("b1", "b2")));

        TargetPlatePluginContent c = store.Describe(Q);

        Assert.Equal(new[] { "a1", "b1" }, c.Rows.Select(r => r.Label));
        Assert.Equal("a2: a2v\nb2: b2v", c.OverflowTooltip);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ExtraRowsAreDroppedWithOneWarning()
    {
        var warnings = new List<string>();
        var store = new PluginTargetPlateStore(() => true, (id, msg) => warnings.Add(id));
        var p = new P(_ => Rows("1", "2", "3"));
        store.For("a", "Alpha").AddProvider(p);
        store.Describe(Q);
        store.Invalidate(7);
        store.Describe(Q);
        Assert.Single(warnings);
    }

    [Fact]
    public void AThrowingProviderIsSkippedAndLoggedOnce()
    {
        var warnings = new List<string>();
        var store = new PluginTargetPlateStore(() => true, (id, msg) => warnings.Add(id));
        store.For("bad", "Bad").AddProvider(new P(_ => throw new InvalidOperationException("boom")));
        store.For("good", "Good").AddProvider(new P(_ => Rows("g")));
        TargetPlatePluginContent c = store.Describe(Q);
        Assert.Equal(new[] { "g" }, c.Rows.Select(r => r.Label));
        Assert.Equal(new[] { "bad" }, warnings);
    }

    [Fact]
    public void ResultsAreCachedUntilRefreshed()
    {
        var store = new PluginTargetPlateStore(() => true, (_, _) => { });
        var p = new P(_ => Rows("x"));
        IPluginTargetPlate plate = store.For("a", "Alpha");
        plate.AddProvider(p);
        store.Describe(Q);
        store.Describe(Q);
        Assert.Equal(1, p.Calls);
        plate.Refresh(7);
        store.Describe(Q);
        Assert.Equal(2, p.Calls);
    }

    [Fact]
    public void DisposingARegistrationRemovesItsRows()
    {
        var store = new PluginTargetPlateStore(() => true, (_, _) => { });
        IDisposable reg = store.For("a", "Alpha").AddProvider(new P(_ => Rows("x")));
        Assert.Single(store.Describe(Q).Rows);
        reg.Dispose();
        Assert.Empty(store.Describe(Q).Rows);
    }

    [Fact]
    public void ActionsAreGroupedByPluginAndRouted()
    {
        var store = new PluginTargetPlateStore(() => true, (_, _) => { });
        var p = new P(_ => new PluginTargetPlateContent(Array.Empty<PluginTargetPlateRow>(),
            new[] { new PluginTargetPlateAction("nav", "Navigate here") }));
        store.For("aethrix", "Aethrix").AddProvider(p);
        TargetPluginAction a = store.Describe(Q).Actions.Single();
        Assert.Equal(("aethrix", "Aethrix", "nav", "Navigate here"), (a.PluginId, a.PluginName, a.ActionId, a.Label));
        store.RunAction("aethrix", 7, "nav");
        Assert.Equal(new[] { (7u, "nav") }, p.Actions);
    }

    [Fact]
    public void AvailabilityFollowsTheDelegate()
    {
        bool on = false;
        var store = new PluginTargetPlateStore(() => on, (_, _) => { });
        Assert.False(store.For("a", "A").IsAvailable);
        on = true;
        Assert.True(store.For("a", "A").IsAvailable);
    }
}
```

- [ ] **Step 2: Run them.** Expected: FAIL to compile.

- [ ] **Step 3: Implement `PluginTargetPlateStore.cs`.**

```csharp
using AcDream.Core.Targeting;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Plugins;

/// <summary>What every plugin added for one object, after the plate's budget.</summary>
public sealed record TargetPlatePluginContent(
    IReadOnlyList<TargetPluginRow> Rows, string? OverflowTooltip, IReadOnlyList<TargetPluginAction> Actions)
{
    public static TargetPlatePluginContent Empty { get; } = new(Array.Empty<TargetPluginRow>(), null, Array.Empty<TargetPluginAction>());
}

/// <summary>
/// Every plugin's Target Deck providers. Asks them once per object (until a
/// refresh), keeps each to its limits, shares the plate's two plugin lines in
/// registration order and keeps a throwing provider from breaking the plate.
/// </summary>
public sealed class PluginTargetPlateStore
{
    public const int PlateLines = 2;

    private readonly Func<bool> _available;
    private readonly Action<string, string> _warn;
    private readonly List<Registration> _registrations = new();
    private readonly Dictionary<string, Surface> _surfaces = new(StringComparer.Ordinal);
    private readonly HashSet<(Registration, uint)> _warned = new();
    private uint _cachedObject;
    private TargetPlatePluginContent? _cached;

    /// <param name="available">True while the Target Deck is mounted (Modern HUD on).</param>
    /// <param name="warn">Writes one warning to a plugin's log: (plugin id, message).</param>
    public PluginTargetPlateStore(Func<bool> available, Action<string, string> warn)
    {
        _available = available ?? throw new ArgumentNullException(nameof(available));
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
    }

    /// <summary>Raised when a plugin asks for an object to be described again.</summary>
    public event Action<uint>? RefreshRequested;

    public IPluginTargetPlate For(string pluginId, string displayName)
    {
        if (!_surfaces.TryGetValue(pluginId, out Surface? s))
            _surfaces[pluginId] = s = new Surface(this, pluginId, displayName);
        return s;
    }

    public void Invalidate(uint objectId)
    {
        if (_cachedObject == objectId)
            _cached = null;
    }

    public TargetPlatePluginContent Describe(PluginTargetPlateQuery query)
    {
        if (_cached is not null && _cachedObject == query.ObjectId)
            return _cached;
        var plate = new List<TargetPluginRow>();
        var overflow = new List<string>();
        var actions = new List<TargetPluginAction>();
        var perProvider = new List<(Registration Reg, List<PluginTargetPlateRow> Rows)>();
        foreach (Registration reg in _registrations.ToArray())
        {
            PluginTargetPlateContent? content;
            try { content = reg.Provider.Describe(query); }
            catch (Exception error) { WarnOnce(reg, query.ObjectId, $"target plate provider threw {error.GetType().Name}: {error.Message}"); continue; }
            if (content is null)
                continue;
            var rows = content.Rows.Take(PluginTargetPlateContent.MaximumRows).ToList();
            if (content.Rows.Count > PluginTargetPlateContent.MaximumRows || content.Actions.Count > PluginTargetPlateContent.MaximumActions)
                WarnOnce(reg, query.ObjectId, $"target plate provider returned more than {PluginTargetPlateContent.MaximumRows} rows or {PluginTargetPlateContent.MaximumActions} actions; the rest were dropped");
            perProvider.Add((reg, rows));
            foreach (PluginTargetPlateAction a in content.Actions.Take(PluginTargetPlateContent.MaximumActions))
                actions.Add(new(reg.Surface.Id, reg.Surface.Name, a.Id, a.Label, a.Enabled));
        }
        // One line per provider in order, then the rest go to the tooltip.
        foreach (var (_, rows) in perProvider)
            if (rows.Count > 0 && plate.Count < PlateLines) { plate.Add(Map(rows[0])); rows.RemoveAt(0); }
        foreach (var (_, rows) in perProvider)
            foreach (PluginTargetPlateRow r in rows)
                if (plate.Count < PlateLines && perProvider.Count == 1) plate.Add(Map(r));
                else overflow.Add($"{r.Label}: {r.Value}");
        _cachedObject = query.ObjectId;
        _cached = new(plate, overflow.Count > 0 ? string.Join("\n", overflow) : null, actions);
        return _cached;
    }

    public void RunAction(string pluginId, uint objectId, string actionId)
    {
        foreach (Registration reg in _registrations.ToArray())
        {
            if (reg.Surface.Id != pluginId)
                continue;
            try { reg.Provider.OnAction(objectId, actionId); }
            catch (Exception error) { _warn(pluginId, $"target plate action '{actionId}' threw {error.GetType().Name}: {error.Message}"); }
        }
    }

    private static TargetPluginRow Map(PluginTargetPlateRow r) =>
        new(r.Label, r.Value, r.Color is { } c ? (uint)(c.R << 24 | c.G << 16 | c.B << 8 | c.A) : null, r.Tooltip);

    private void WarnOnce(Registration reg, uint objectId, string message)
    {
        if (_warned.Add((reg, objectId)))
            _warn(reg.Surface.Id, message);
    }

    private void Remove(Registration reg)
    {
        _registrations.Remove(reg);
        _cached = null;
    }

    private sealed record Registration(Surface Surface, IPluginTargetPlateProvider Provider);

    private sealed class Surface(PluginTargetPlateStore store, string id, string name) : IPluginTargetPlate, IDisposable
    {
        public string Id => id;
        public string Name => name;
        public bool IsAvailable => store._available();

        public IDisposable AddProvider(IPluginTargetPlateProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            var reg = new Registration(this, provider);
            store._registrations.Add(reg);
            store._cached = null;
            return new Unregister(store, reg);
        }

        public void Refresh(uint objectId)
        {
            store.Invalidate(objectId);
            store.RefreshRequested?.Invoke(objectId);
        }

        public void Dispose()
        {
            foreach (Registration reg in store._registrations.Where(r => r.Surface == this).ToArray())
                store.Remove(reg);
            store._surfaces.Remove(id);
        }
    }

    private sealed class Unregister(PluginTargetPlateStore store, Registration reg) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            store.Remove(reg);
        }
    }
}
```

The budget rule, spelled out (the tests define it):
- Each provider's first row goes on the plate, in order, until the two lines are used.
- A lone provider may use both lines.
- Every row left over goes to the tooltip.

In `BufferedUiRegistry.cs`, add this next to `WorldMarkerStore`:

```csharp
/// <summary>Every plugin's Target Deck providers, read by the Target Deck.</summary>
internal PluginTargetPlateStore TargetPlateStore { get; } = new(() => TargetPlateAvailable?.Invoke() == true,
    (id, message) => Serilog.Log.Warning("[plugin {Plugin}] {Message}", id, message));

/// <summary>Set by the UI when the Target Deck mounts: true while the Modern HUD is on.</summary>
internal Func<bool>? TargetPlateAvailable { get; set; }

public IPluginTargetPlate TargetPlateFor(PluginUiOwner owner)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(owner.Id);
    return TargetPlateStore.For(owner.Id, owner.DisplayName);
}
```

Also add `public IPluginTargetPlate TargetPlate => TargetPlateFor(new PluginUiOwner("unscoped", "Plugin"));` beside the unscoped `WorldMarkers` (line ~766).

Plugin warnings should go to the plugin's own log where one exists. If `BufferedUiRegistry` already has a per-plugin log route (search for how theme handler failures name the plugin), use it in place of the Serilog lambda.

In `ScopedPluginHost.ScopedUiRegistry`, add this, copying the `_worldMarkers` block exactly:

```csharp
// The target-plate surface is asked for once and kept, like world markers;
// a disposable one is tracked so the plugin's providers go with the plugin.
private IPluginTargetPlate? _targetPlate;

public IPluginTargetPlate TargetPlate
{
    get
    {
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ScopedUiRegistry));
            if (_targetPlate is null)
            {
                _targetPlate = _inner is IScopedUiRegistry scoped
                    ? scoped.TargetPlateFor(_owner)
                    : _inner.TargetPlate;
                if (_targetPlate is IDisposable disposable)
                    _registrations.Add(disposable);
            }
            return _targetPlate;
        }
    }
}
```

Match the way `WorldMarkers` reaches `WorldMarkersFor`. If `_inner` is already typed `IScopedUiRegistry`, call `TargetPlateFor` directly.

- [ ] **Step 4: Write the scoping test** `ScopedUiRegistryTargetPlateTests.cs`. Copy the structure of `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryWorldMarkersTests.cs`, with its `FakeScopedUiRegistry` returning a disposable fake `IPluginTargetPlate`. Assert three things:
  1. Two reads of `TargetPlate` return the same instance and `TargetPlateFor` was called once with the plugin's owner.
  2. Disposing the scoped host disposes the fake once.
  3. Reading after dispose throws `ObjectDisposedException`.

- [ ] **Step 5: Run the Verify filter.** Expected: all pass.

- [ ] **Step 6: Commit** with `git commit -m "Target Deck: host plugin target-plate store and per-plugin scoping"`.

---

### Task 7: UiTargetDeck element (geometry, draw, input)

**Goal:** The shape B plate as a `UiElement`. It is a fixed 340×104 frame with the portrait, name, chips, sub line, health/stamina/mana bars or rows, plugin rows, and a four-stud action column with tooltips and a ⋯ popup menu. It pulls a `TargetPlateModel` each frame.

**Files:**
- Create: `src/AcDream.App/UI/Hud/TargetDeckGeometry.cs`
- Create: `src/AcDream.App/UI/Hud/ITargetDeckActions.cs`
- Create: `src/AcDream.App/UI/Hud/UiTargetDeck.cs`
- Create: `src/AcDream.App/UI/Hud/UiTargetDeck.Draw.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/UiTargetDeckTests.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/UiTargetDeckDrawTests.cs`

**Acceptance Criteria:**
- [ ] For a model of each of the ten kinds, every drawn quad lies inside 0..340 × 0..104. Checked with `MarkupDrawProbe`, as in `UiCommandDeckDrawTests.DrawsEveryModeInsideTheDeck`.
- [ ] With a null model the deck draws no quads and `HitTestLocal` returns false everywhere.
- [ ] `Width`/`Height` stay 340×104 for every kind, and the deck is not resizable.
- [ ] A name too long for the name rect is drawn shortened with "…", using the same fit helper as `UiCommandDeck.FitCastingText`.
- [ ] A MouseDown on an enabled stud calls the matching `ITargetDeckActions.Run(TargetAction, model)`. On a disabled stud it calls nothing and returns true, so the window does not drag.
- [ ] A MouseDown on ⋯ opens a popup listing `MoreMenu`, then the plugin actions under a header with the plugin's name. Clicking an entry calls `Run`, or `RunPluginAction(pluginId, objectId, actionId)`, then closes the popup. Escape or a click outside closes it.
- [ ] Hovering a stud for `UiCommandDeck.TooltipDwell` shows a `HudTooltipContent` with the action's name, and the `DisabledReason` as `Detail` when disabled. Hovering stamina or mana in the Failed state shows "Assess failed". Hovering the plugin row shows `PluginOverflowTooltip` when it is non-null.
- [ ] Stale and failed vitals are drawn at 45% alpha. Unknown vitals draw empty tracks.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiTargetDeck'` → all pass.

**Steps:**

- [ ] **Step 1: Write `ITargetDeckActions.cs` and `TargetDeckGeometry.cs`.** These come first because the tests need them.

```csharp
using AcDream.Core.Targeting;

namespace AcDream.App.UI.Hud;

/// <summary>What the Target Deck asks the game to do.</summary>
public interface ITargetDeckActions
{
    void Run(TargetAction action, TargetPlateModel target);
    void RunPluginAction(string pluginId, uint objectId, string actionId);
}
```

```csharp
namespace AcDream.App.UI.Hud;

public enum TargetZone { None, Portrait, Name, Health, Stamina, Mana, Row0, Row1, Row2, Row3, PluginRow0, PluginRow1, Stud0, Stud1, Stud2, Stud3 }

/// <summary>Fixed layout of the Target Deck at scale 1 (shape B: info left, action column right).</summary>
public static class TargetDeckGeometry
{
    public const float Width = 340f, Height = 104f;
    public const float Pad = 10f, StudW = 24f, StudH = 22f, StudGap = 3f;
    public static readonly DeckRect Frame = new(0, 0, Width, Height);
    public static readonly DeckRect Portrait = new(Pad, Pad, 30, 30);
    public static readonly DeckRect Name = new(Pad + 36, Pad - 1, Width - Pad * 2 - 36 - StudW - 8, 16);
    public static readonly DeckRect Sub = new(Pad + 36, Pad + 16, Name.W, 13);
    public static readonly DeckRect Health = new(Pad, 46, Width - Pad * 2 - StudW - 8, 14);
    public static readonly DeckRect Stamina = new(Pad, 63, Health.W, 6);
    public static readonly DeckRect Mana = new(Pad, 72, Health.W, 6);

    /// <summary>Info line i (0-3) below the identity block: kind rows, then plugin rows.</summary>
    public static DeckRect Line(int i) => new(Pad, 46 + i * 13, Health.W, 13);

    /// <summary>Plugin line i (0-1) under the vitals bars.</summary>
    public static DeckRect PluginLine(int i) => new(Pad, 82 + i * 11, Health.W, 11);

    public static DeckRect Stud(int i) => new(Width - Pad - StudW, Pad + i * (StudH + StudGap), StudW, StudH);
}
```

Layout rules:
- **Creature kinds with vitals:** health, stamina and mana bars at `Health`, `Stamina` and `Mana`, then plugin rows at `PluginLine(0..1)`.
- **Other kinds:** kind rows at `Line(0..)`, at most `4 − pluginRows` of them, then plugin rows on the next lines.
- Four studs stack down the right edge (4 × 22 + 3 × 3 = 97 ≤ 104 − 10).

- [ ] **Step 2: Write the failing tests.** In `UiTargetDeckDrawTests.cs`, build one `TargetPlateModel` per kind with `TargetPlateModelBuilder.Build`, using the objects from Task 4's `EveryKindHasFourOrFewerStudsEndingInMore`, plus one plugin row. Then assert:

```csharp
var deck = new UiTargetDeck(() => model, new NoActions(), icons: null, clock: () => 1.0);
deck.Refresh();
var renderer = MarkupDrawProbe.Draw(deck, TargetDeckGeometry.Width, TargetDeckGeometry.Height);
int quads = 0;
foreach (var s in renderer.DebugSpriteSegmentVerts)
    for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
    {
        quads++;
        Assert.InRange(s.Verts[i], -0.01f, TargetDeckGeometry.Width + 0.01f);
        Assert.InRange(s.Verts[i + 1], -0.01f, TargetDeckGeometry.Height + 0.01f);
    }
Assert.True(quads > 0);
```

Add a `[Fact]` asserting that a null model draws 0 quads.

In `UiTargetDeckTests.cs`, write five tests:
1. A null model gives `HitTestLocal(170, 50) == false`.
2. A Monster model gives `HitTestLocal` true inside `Stud(0)`.
3. A MouseDown at the centre of `Stud(0)` (Assess) records `Run(TargetAction.Assess, model)` in a recording `ITargetDeckActions`.
4. A Player model without a pack item: MouseDown on the Give stud returns true and records nothing.
5. Width and Height are 340×104 for all ten models.

Send events the way `UiCommandDeckTests` does (copy its `UiEvent` MouseDown construction).

- [ ] **Step 3: Run them.** Expected: FAIL to compile.

- [ ] **Step 4: Implement `UiTargetDeck.cs`.** Mirror `UiCommandDeck` with these differences:
- **Base and construction:** `internal sealed partial class UiTargetDeck : UiElement`. The constructor is `(Func<TargetPlateModel?> model, ITargetDeckActions actions, EmbeddedSvgSpriteCache? icons, Func<double> clock, DeckFonts? fonts = null)`. It sets `Width = TargetDeckGeometry.Width; Height = TargetDeckGeometry.Height; Resizable = false;`.
- **Mount options:** `internal static RetailWindowFrame.Options MountOptions(float left, float top)` is a copy of `UiCommandDeck.MountOptions` with `WindowName = WindowNames.TargetDeck`, `Resizable = false`, no resize edges, min = max = the fixed size, `Visible = false` and `ContentClickThrough = false`.
- **Per-frame refresh:** `internal void Refresh() { _view = _model(); if (_view is null) { ResetHover(); _menuOpen = false; } }`, and `protected override void OnTick(double) => Refresh();`.
- **Hit testing:** `protected override bool OnHitTest(float x, float y)` returns false when `_view is null`. Otherwise it sets `_hitZone = ZoneAt(x, y)` and `CapturesPointerDrag = _hitZone is >= TargetZone.Stud0`, and returns true for any point inside the frame. Non-stud presses fall through to the window drag, as in the Command Deck.
- **Input:** `public override bool OnEvent(in UiEvent e)` handles:
  - MouseMove: hover and tooltip timing, copied from `UiCommandDeck.SetHover` with `TooltipDwell`.
  - MouseDown on a stud: `Press(i)`.
  - MouseDown while the menu is open: pick an entry or close.
  - Key Escape: close the menu.
- **`Press(int i)`:**
  - Out of range: return false.
  - Disabled stud: return true.
  - `More`: toggle `_menuOpen` and return true.
  - Otherwise: call `_actions.Run(stud.Action, _view)` and return true.
- **Menu:** the ⋯ popup is drawn in `OnDrawOverlay` (keep `ExpandsClipForPopup => true`) below the plate, with `DeckChrome.DrawFrame` and one 18 px line per entry: the `MoreMenu` labels, then for each plugin a `LabelInk` header with the plugin name and its actions. Its hit rects are computed in local coordinates below `Height`, and the hit test accepts points inside the open menu.
- **Labels:** `internal static string LabelFor(TargetAction a)` maps every `TargetAction` to text:

| Action | Label |
|---|---|
| Assess | Assess again |
| UseItemOn | Use selected item on it |
| Follow | Follow |
| More | More |
| Trade | Trade |
| Give | Give selected item |
| Tell | Tell |
| FellowshipInvite | Invite to fellowship |
| UseOrTalk | Use / talk |
| PickUpOrUse | Pick up / use |
| Wield | Wield |
| Open | Open |
| UseKey | Use selected key or lockpick |
| UsePortal | Use portal |
| Use | Use |
| CharacterSheet | Character sheet |
| Examine | Examine |
| CopyName | Copy name |
| SwearAllegiance | Swear allegiance |

- **Icons:** `internal static string IconFor(TargetAction a)` returns an embedded SVG sprite name. Use names that already exist in the HUD sprite set (see how `UiCommandDeck.Draw.cs` calls `DrawIcon` and where `EmbeddedSvgSpriteCache` loads its names). If a needed icon is missing, add a small SVG beside the existing ones, following how the Command Deck icons were added in its resize/icons commits. Fall back to `DrawStud` with a one-letter label when the icon cache is null, as in tests.
- **Tooltips:** `internal static HudTooltipContent? TooltipFor(TargetZone zone, TargetPlateModel view)`:
  - Studs: `new(LabelFor(action), null, stud.Enabled ? null : stud.DisabledReason, null)`.
  - Stamina/Mana in the Failed state: `new("Assess failed", null, "Your assess skill check failed; health is still live.", null)`.
  - Stale: `new("Stamina", null, "Last assessed over 10 s ago", null)` (same for Mana).
  - PluginRow0/1: `view.PluginOverflowTooltip` as Detail, or the row's own Tooltip.

- [ ] **Step 5: Implement `UiTargetDeck.Draw.cs`.**
- `OnDraw(ctx)` returns at once when `_view is null`.
- Otherwise:
  1. Draw `DeckChrome.DrawFrame(ctx, TargetDeckGeometry.Frame, 1f)`.
  2. Draw the portrait as a dark circle with a bronze ring, using `DeckChrome` colours, and an icon per kind from `IconFor`, with `Kind` mapped to a glyph.
  3. Draw the name with `DeckFace.SmallBoldFor` at 14 px (`DeckFonts.Bold`), fitted with the "…" helper.
  4. Draw chips as small filled rects with 9 px bold text: Yours/Fellow gold, PK red, NPK green, PKL amber.
  5. Draw the con as the level pill colour: Higher `StudOn` red, Even amber, Lower blue, Unknown no pill.
  6. Draw the sub line in `LabelInk`, 12 px.
  7. Draw the bars by copying `UiCommandDeck.DrawBar`'s gradient-in-track drawing. To share it, move `DrawBar` into `DeckChrome` as `internal static void DrawGauge(UiRenderContext ctx, DeckRect r, float fraction, (Vector4 Top, Vector4 Bottom) ink, float alpha)` and make `UiCommandDeck` call it. Keep the Command Deck tests green.
  8. Draw health text as a percent, centred. Stamina and mana bars have no text.
  9. Draw rows as label in `LabelInk` and value in `TextInk` at 12 px, with `Dim` rows at 45% alpha.
  10. Draw plugin rows with a 2 px green (`DeckChrome.JumpInk`) left edge, coloured with `Rgba` when it is set.
  11. Draw studs with `DeckChrome.DrawStud(ctx, rect, 1f, on: action is the kind's primary, i.e. stud 0, dim: !enabled)` plus the icon.

- [ ] **Step 6: Run the Verify filter, plus `UiCommandDeck` after the `DrawGauge` move.**

```bash
MAC_TEST_FILTER='FullyQualifiedName~UiTargetDeck|FullyQualifiedName~UiCommandDeck' …
```

Expected: all pass.

- [ ] **Step 7: Commit** with `git commit -m "Target Deck: UiTargetDeck plate (shape B, studs, tooltips, ⋯ menu)"`.

---

### Task 8: Wire the Target Deck into the Modern HUD

**Goal:** The deck mounts with the other decks. `ModernHudSwitch` shows it with no retail windows. Its model is built every frame from live state. Auto-ID ticks only while it is mounted. Actions reach the existing game paths, and plugin content reaches the plate.

**Files:**
- Modify: `src/AcDream.App/UI/WindowNames.cs` (add `TargetDeck = "target-deck"` after `ChatDeck`)
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs`:
  - the `TargetDeckRuntimeBindings` record next to `CommandDeckRuntimeBindings` (line ~97);
  - a new optional `TargetDeck` parameter on `RetailUiRuntimeBindings` (line ~366);
  - the switch entry (lines ~537-575);
  - `stateManagedVisibilityWindows` (line ~630);
  - `MountTargetDeck()` next to `MountChatDeck()` (line ~3610), called at lines ~591-593;
  - `TargetDeckActions`, nested next to `CommandDeckActions` (line ~3701).
- Modify: `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs` (next to `CommandDeck: new CommandDeckRuntimeBindings(`, line ~1104)
- Test: `tests/AcDream.App.Tests/UI/Hud/TargetDeckWiringTests.cs`
- Test: `tests/AcDream.App.Tests/UI/RetailWindowLayoutPersistenceTests.TargetDeck.cs`

**Acceptance Criteria:**
- [ ] With Modern HUD on, `ModernHudSwitch` has a "Target Deck" replacement whose `RetailWindows` list is empty. Turning Modern HUD off hides `WindowNames.TargetDeck` and leaves every retail window unchanged.
- [ ] `WindowNames.TargetDeck` is in `stateManagedVisibilityWindows`.
- [ ] The saved position round-trips through the layout persistence. This is a copy of `RetailWindowLayoutPersistenceTests.CommandDeck.cs`, with `UiTargetDeck.MountOptions`.
- [ ] The default mount position is horizontally centred, `top = 12`.
- [ ] `TargetAutoAppraisal.Tick()` is called from the deck's tick only while mounted, and the `TargetAutoAppraisal` is disposed on logout or session reset.
- [ ] An appraisal reply for the auto-ID target calls `TargetAutoAppraisal.OnAppraised(guid, parsed.Success)`, and `PluginTargetPlateStore.Invalidate(guid)`.
- [ ] Each `TargetAction` maps to an existing path:

| Action | Path |
|---|---|
| Assess, Examine | `RuntimeItemInteraction.ExamineSelectedOrEnterMode(id)` (user origin, opens the examine window) |
| Use, UseOrTalk, PickUpOrUse, Open, UsePortal | `RuntimeItemInteraction.UseSelectedOrEnterMode(id)` |
| UseItemOn, UseKey | the toolbar's use-item-on-target path for the selected pack item (find it next to `UseSelectedOrEnterMode` in `RuntimeItemInteraction`, the "use with target" mode) |
| Give | `RuntimeItemInteraction.TryGiveItemForAutomation(packItem, id)` |
| Trade | `RuntimeItemInteraction.TryOpenSecureTradeWithPlayer(id)` |
| FellowshipInvite | `_bindings.Social.FellowshipRecruit(id)` |
| SwearAllegiance | `_bindings.Social.AllegianceSwear(id)` |
| Tell | `ComposeChatText($"/tell {name}, ")` |
| Follow | `RuntimeNavigationAutomation.Follow(id, DefaultFollowMeters)`, exposed through `TargetDeckRuntimeBindings.Follow` |
| CharacterSheet | `ToggleWindow(WindowNames.Character)` |
| CopyName | sets the keyboard clipboard text (`Host.Root` keyboard `ClipboardText`, as `UiField` does) |
| Wield | the inventory's existing wield path for an item (the one used by double-click on a wieldable item in `InventoryController`; reuse it, do not write a new one) |

- [ ] `RunPluginAction` calls `PluginTargetPlateStore.RunAction`.
- [ ] `BufferedUiRegistry.TargetPlateAvailable` is set to "deck mounted and Modern HUD in force".
- [ ] A build failure in `MountTargetDeck` is caught: the mount is undone and logged, and the switch reports the deck unavailable. This mirrors `MountChatDeck`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~TargetDeck|FullyQualifiedName~ModernHud'` → all pass. Then run the full suite once, `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-target-deck --no-build`, and expect 0 failures.

**Steps:**

- [ ] **Step 1: Write the failing wiring tests.** Copy the structure of `tests/AcDream.App.Tests/UI/Hud/ChatDeckWiringTests.cs` and `ModernHudWiringTests.cs`.
  - Assert the switch's replacement list contains `Name == "Target Deck"`, `Window == WindowNames.TargetDeck` and an empty `RetailWindows`.
  - Assert that toggling Modern HUD off hides the deck and does not change any retail window's visibility.
  - Copy `RetailWindowLayoutPersistenceTests.CommandDeck.cs` to `.TargetDeck.cs`, swapping in `UiTargetDeck` and `UiTargetDeck.MountOptions`, with `new NoActions()` and `() => null` for the model.

- [ ] **Step 2: Run them.** Expected: FAIL.

- [ ] **Step 3: Add the bindings record** next to `CommandDeckRuntimeBindings`:

```csharp
/// <summary>The Modern HUD Target Deck: the state it reads and the actions it needs
/// beyond the item, social and chat bindings.</summary>
public sealed record TargetDeckRuntimeBindings(
    Func<bool> Enabled,
    CombatState Combat,
    Func<int> PlayerLevel,
    Func<uint, float?> DistanceMeters,
    Func<uint> SelectedPackItem,
    Func<uint, bool> IsFellow,
    Func<uint, bool> Follow);
```

Add `TargetDeckRuntimeBindings? TargetDeck = null` as the last parameter of `RetailUiRuntimeBindings`.

In `InteractionRetainedUiComposition.cs`, next to `CommandDeck:`, build it:
- `Enabled: () => modernHud`.
- `Combat:` the same `CombatState` the toolbar bindings use (`d.Actions.Combat` state or the session's combat state; follow what `CombatRuntimeBindings.Combat` is given).
- `PlayerLevel:` the local player's `PropertyInt.Level` from `d.Character.LocalPlayer.Properties`.
- `DistanceMeters:` the distance from the player to the object, from the same position source the radar or `WorldTooltip` uses. Return null when it's unknown.
- `SelectedPackItem:` the inventory's currently selected pack item, the one the toolbar's "use with" mode reads.
- `IsFellow:` `id => social.FellowshipMembers().Any(m => m.Guid == id)`, or the existing fellowship lookup.
- `Follow:` `id => late.GameRuntime.Navigation.Follow(id, RuntimeNavigationAutomation.DefaultFollowMeters) == PluginNavigationCommandStatus.Accepted`. Use the real accepted status name.

- [ ] **Step 4: Mount it.** Add `MountTargetDeck()`, modelled on `MountChatDeck` with its try/catch undo:

```csharp
private void MountTargetDeck()
{
    if (_bindings.TargetDeck is not { } b) return;
    try
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _targetAppraisalLog = new TargetAppraisalLog();
        _targetAuto = new TargetAutoAppraisal(
            _bindings.Toolbar.Selection, _bindings.Objects, b.Combat, _targetAppraisalLog,
            _bindings.PlayerGuid, ItemInteraction.TryAppraiseForAutomation, () => clock.Elapsed.TotalSeconds);
        var actions = new TargetDeckActions(this, _bindings, b);
        _targetDeck = new Hud.UiTargetDeck(() => BuildTargetPlate(b, clock.Elapsed.TotalSeconds), actions,
            _hudIcons, () => clock.Elapsed.TotalSeconds, _deckFonts ??= Hud.DeckFonts.Load(_bindings.Assets.TextureCache));
        _targetDeck.Ticked += () => _targetAuto?.Tick();
        RetailWindowFrame.Mount(Host.Root, _targetDeck, _bindings.Assets.ResolveSprite,
            Hud.UiTargetDeck.MountOptions(
                Math.Max(0f, (Host.Root.Width - Hud.TargetDeckGeometry.Width) * 0.5f), 12f));
        if (_bindings.Plugins is { } plugins)
            plugins.TargetPlateAvailable = () => _targetDeck is not null && b.Enabled();
    }
    catch (Exception error)
    {
        Serilog.Log.Warning(error, "[UI] Target Deck failed to mount");
        UnmountTargetDeck();
    }
}
```

Notes on this sketch:
- **Fields:** add `_targetDeck`, `_targetAuto` and `_targetAppraisalLog`.
- **Unmount:** `UnmountTargetDeck()` disposes `_targetAuto`, removes the window the way `MountChatDeck`'s undo does, and nulls the fields.
- **Tick hook:** give `UiTargetDeck` an `internal event Action? Ticked` raised at the end of `OnTick`. Alternatively, pass a tick callback to its constructor; pick one and keep it consistent with the Task 7 tests.
- **Icons:** `_hudIcons` is created in `MountCommandDeck`. If the Command Deck is not mounted, create it the same way here (`??=`).
- **Order:** call `MountTargetDeck()` after `MountChatDeck()`.
- **Reset:** in `ResetSessionTransientUi`, clear `_targetAppraisalLog`.

Add the builder:

```csharp
private TargetPlateModel? BuildTargetPlate(TargetDeckRuntimeBindings b, double now)
{
    uint id = _bindings.Toolbar.Selection.SelectedObjectId ?? 0u;
    if (id == 0u || _targetAppraisalLog is null) return null;
    ClientObject? obj = _bindings.Objects.Get(id);
    if (obj is null) return null;
    TargetClassification c = TargetKindClassifier.Classify(obj, _bindings.PlayerGuid());
    TargetPlatePluginContent plugin = _bindings.Plugins?.TargetPlateStore.Describe(ToQuery(obj, c, b))
        ?? TargetPlatePluginContent.Empty;
    return TargetPlateModelBuilder.Build(new TargetPlateInput(
        obj, _bindings.PlayerGuid(), b.PlayerLevel(), b.Combat, _targetAppraisalLog, now,
        b.DistanceMeters(id), b.SelectedPackItem(), b.IsFellow,
        plugin.Rows, plugin.OverflowTooltip, plugin.Actions));
}
```

`ToQuery` maps the object to a `PluginTargetPlateQuery`:
- `(PluginTargetKind)(int)c.Kind`. Both enums have the same member order; add a test asserting `Enum.GetNames` are equal.
- `Name`, `WeenieClassId` and `Level` from the object.
- `OwnedByPlayer` from the classification.
- `Identified` from `_targetAppraisalLog.TryGet`.
- Health from `b.Combat.GetHealthPercent`; stamina and mana fractions from `b.Combat.TryGetCreatureVitals`.

The store caches per object, so calling `Describe` every frame is cheap.

Hook appraisal replies: in `HandleAppraisal` (line ~1282), before the existing body runs, add:

```csharp
_targetAuto?.OnAppraised(appraisal.Guid, appraisal.Success);
_bindings.Plugins?.TargetPlateStore.Invalidate(appraisal.Guid);
```

Keep the existing return expression unchanged. Also subscribe `TargetPlateStore.RefreshRequested`. Nothing more is needed there, because the deck pulls each frame.

- [ ] **Step 5: Register with the switch** in the `ModernHudSwitch` list:

```csharp
new Hud.ModernHudReplacement(
    "Target Deck", WindowNames.TargetDeck, Array.Empty<string>(),
    Mounted: () => _targetDeck is not null,
    OnShown: () => { },
    OnHidden: () => _targetDeck?.OnHidden()),
```

`OnHidden` closes the ⋯ menu and resets hover. Add `WindowNames.TargetDeck` to `stateManagedVisibilityWindows`, and `public const string TargetDeck = "target-deck";` to `WindowNames`.

- [ ] **Step 6: Implement `TargetDeckActions`,** nested next to `CommandDeckActions`, implementing `Hud.ITargetDeckActions`. Its `Run` does a `switch` over every `TargetAction`, with one call per row of the acceptance table. Use `target.ObjectId` and `target.Name`. `None` and `More` do nothing. `RunPluginAction` forwards to `_bindings.Plugins?.TargetPlateStore.RunAction(pluginId, objectId, actionId)`.

- [ ] **Step 7: Build and run the Verify filter.** Then run the full Mac suite once. Expected: 0 failures.

- [ ] **Step 8: Commit** with `git commit -m "Target Deck: wire into the Modern HUD (bindings, actions, auto-ID tick, plugin store)"`.

---

### Task 9: Live check on the local ACE server

**Goal:** Confirm the Target Deck in the real client, on the local ACE test server, for every target kind and behaviour in the spec.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- Modify: `docs/superpowers/plans/2026-10-27-target-deck.md` (task status)

**Acceptance Criteria:**
- [ ] Ask the user first, and launch the client only after they say they are available.
  - Use the local ACE server, 10.10.20.20 via `~/OpenAC-dev/dev-client.env`. Never Dreamweave.
  - Use the Holtburg test hub.
- [ ] The plate shows the right kind, rows and studs for each target:
  - a drudge and one other monster;
  - your own summon and Sammy's;
  - a player, a vendor, an item on the ground, a corpse and a chest;
  - a portal, a door and a lifestone;
  - yourself.
- [ ] During a fight, the monster's stamina and mana bars update after hits, and are no older than about 2 s while hits keep landing.
- [ ] Auto-ID never opens the examine window. Pressing 👁 Assess does open it.
- [ ] Toggling Modern HUD in Options → Config hides and shows the plate live, with no retail window changing.
- [ ] A dragged plate position survives a relog.
- [ ] The user confirms the look and behaviour. Record their words in the plan.

**Verify:** the user's confirmation in chat, plus a screenshot per kind via the Retina backbuffer capture probe described in memory `retina-capture-workflow`. The user drags the window to the built-in display.

**Steps:**

- [ ] **Step 1:** Ask the user whether they're available for a live check, and wait.
- [ ] **Step 2:** Launch with the dev env and log in to the test character.
- [ ] **Step 3:** Walk the checklist, capturing a screenshot per kind.
- [ ] **Step 4:** Fix anything found. Each fix is its own commit, with tests where testable.
- [ ] **Step 5:** Record the result and the user's words in this plan, then commit: `git commit -m "Target Deck: live check passed"`.

---

## After the plan

- Merge `feat/target-deck` to `main`, then to `rynth`, running the rynth suite.
- Push both only when the user says so ("committed and deployed" means push all repos and branches).
- Archive the spec branch as an `archive/specs/target-deck-spec` tag, as previous decks were.
- Aethrix needs a repack against `0.1.28-fork.2` before it can use `TargetPlate`. Its creature-db provider is separate work.
