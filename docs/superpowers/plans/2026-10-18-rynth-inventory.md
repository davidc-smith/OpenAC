# RynthInventory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `/ginv` opens RynthSuite's RynthInventory "Global Inventory" window inside OpenAC, listing every character's carried items and the storage they opened.

**Architecture:** A new wrapper project compiles the RynthInventory submodule unchanged. `IRynthGameView` grows object, contents, identity and position reads over `GameRuntime`; `RynthApiTable` fills 15 more RynthCore API slots from it (stat-number mapping in a pure `RynthObjectProperties` class); the bridge forwards the client's container open/close/contents events to three new plugin callbacks; the extension loads the plugin and registers `/ginv`.

**Tech Stack:** .NET 10 (`~/.dotnet/dotnet`), xunit v3 (VSTest runner: `--filter "FullyQualifiedName~X"`), RynthCore PluginSdk/PluginCore (compiled from `external/`).

**Spec:** `docs/superpowers/specs/2026-10-18-rynth-inventory-design.md`

## Global Constraints

- Repo `~/code/OpenAC/OpenAC-Rynth`, branch `feat/rynth-inventory` (already created; it sits on top of `feat/rynth-tracker-deaths`). Do not push.
- Use `~/.dotnet/dotnet` (the system `dotnet` lacks SDK 10.0.300).
- Never edit anything inside `external/RynthCore` or `external/RynthSuite`; wrapper projects include files from there.
- `AcDream.App`, `AcDream.ClientExtensions` and `AcDream.Rynth.Host` build with warnings as errors and analyzers on: new code there must build clean. The plugin wrapper has warnings off, like the RynthOracle wrapper.
- Every `[UnmanagedCallersOnly]` table function catches every exception, logs through `Fail`, and returns 0 (or false/`IntPtr.Zero`). An exception must never cross that boundary.
- Unknown objects answer **false** from every per-object slot, never zeros with true.
- `GetContainerContents(id)` returns contained items **plus** items `id` wields, sorted by ascending id (RynthCore's behaviour; the scanner only finds equipped items this way).
- Strings handed to plugins (object name, world name, account name) are ANSI HGlobal buffers the table owns, one per slot, replaced on the next call and freed on `Dispose`.
- Tests that load RynthInventory set `XDG_CONFIG_HOME` to a fresh temp directory first (on macOS/Linux .NET maps `ApplicationData` there), so they never touch `~/.config/RynthCore`. Rynth host tests run without parallelism (`FakeGameView.cs` assembly attribute); App tests use `[Collection("Rynth")]`.
- Known unrelated failures exist in `AcDream.App.Tests` (dat files, live server). Run App tests with the filters each task's Verify shows. `AcDream.Rynth.Host.Tests` must be fully green.
- Never launch a client without asking the user first; only the local ACE server 10.10.20.20, never Dreamweave; never print passwords (`~/OpenAC-dev/dev-client.env`).

**User decisions (already made):**
- Next display plugin is RynthInventory (over RynthVision and RynthChat).
- First version records carried items **and** storage the character opens.
- No auto-identify; item spells only for items the player identified.
- Files stay at the plugin's default location (`~/.config/RynthCore/inventory` on macOS); the submodule is not changed.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AcDream.Rynth.Plugin.RynthInventory/AcDream.Rynth.Plugin.RynthInventory.csproj` | create | compile the submodule plugin as plain net10.0, embed SpellData.txt |
| `AcDream.slnx` | modify | list the wrapper |
| `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj` | modify | reference the wrapper |
| `src/AcDream.Rynth.Host/RynthObjectInfo.cs` | create | record of an object's typed fields + property dictionaries |
| `src/AcDream.Rynth.Host/RynthObjectProperties.cs` | create | RynthCore stat numbers → `RynthObjectInfo` values |
| `src/AcDream.Rynth.Host/IRynthGameView.cs` | modify | `TryGetObject`, `GetContainerContents`, `AccountName`, `WorldName`, `TryGetObjectPosition` |
| `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs` | modify | those members over `GameRuntime` |
| `src/AcDream.Rynth.Host/RynthApiTable.cs` | modify | 15 new slots |
| `src/AcDream.Rynth.Host/RynthPlugin.cs` | modify | 3 container callbacks |
| `src/AcDream.Rynth.Host/RynthEventBridge.cs` | modify | container-event methods |
| `src/AcDream.Rynth.Host/RynthClientExtension.cs` | modify | load RynthInventory, `/ginv`, container subscriptions |
| `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs` | modify | the new view members |
| `tests/AcDream.Rynth.Host.Tests/RynthObjectPropertiesTests.cs` | create | mapping tests |
| `tests/AcDream.Rynth.Host.Tests/RynthInventoryTableTests.cs` | create | slot tests through `RynthCoreHost` |
| `tests/AcDream.Rynth.Host.Tests/RynthInventoryTests.cs` | create | plugin load, `/ginv`, scan, container callbacks |
| `tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs` | modify | new view members over a real runtime |
| `tests/AcDream.App.Tests/Extensions/RynthClientExtensionTests.cs` | modify | `/ginv` registered, container events wired |

---

### Task 1: RynthInventory wrapper project loads in the host

**Goal:** A plain-net10.0 wrapper compiles the unchanged RynthInventory plugin, and `RynthHost` initialises it.

**Files:**
- Create: `src/AcDream.Rynth.Plugin.RynthInventory/AcDream.Rynth.Plugin.RynthInventory.csproj`
- Modify: `AcDream.slnx` (after the RynthOracle line, 18)
- Modify: `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj:11`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthInventoryTests.cs`

**Acceptance Criteria:**
- [ ] The wrapper builds and embeds `RynthInventory.SpellData.txt`.
- [ ] A `RynthHost` over `RynthCore.Plugin.RynthInventory.PluginExports` has one active plugin.
- [ ] `/ginv help` is eaten by the plugin and, after a tick, a chat line starting `/ginv (window)` reaches the view.
- [ ] No file is written under the real `~/.config/RynthCore` by the tests.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~RynthInventoryTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing test**

```csharp
// tests/AcDream.Rynth.Host.Tests/RynthInventoryTests.cs
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthInventoryTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly string _config = Directory.CreateTempSubdirectory("rynth-inventory-").FullName;
    private readonly string? _previousConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly FakeGameView _view = new()
    {
        PlayerId = 0x50000000u | (uint)Random.Shared.Next(1, 0xFFFF),
    };

    // RynthInventory saves under ApplicationData, which .NET reads from XDG_CONFIG_HOME here.
    public RynthInventoryTests() => Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _config);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _previousConfig);
        try { Directory.Delete(_config, recursive: true); }
        catch (IOException) { }
    }

    private RynthHost CreateInventoryHost() =>
        new(_view, _log.Add, [typeof(RynthCore.Plugin.RynthInventory.PluginExports)]);

    [Fact]
    public void InventoryLoads()
    {
        using RynthHost host = CreateInventoryHost();
        Assert.True(Assert.Single(host.Plugins).IsActive);
    }

    [Fact]
    public void SlashGinvHelpPrintsTheCommands()
    {
        using RynthHost host = CreateInventoryHost();

        Assert.True(host.Plugins[0].OnChatBarEnter("/ginv help"));
        host.Tick();

        Assert.Contains(_view.ChatLines, line => line.Text.Contains("/ginv (window)", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~RynthInventoryTests"`
Expected: build error, `RynthCore.Plugin.RynthInventory` not found.

- [ ] **Step 3: Create the wrapper and reference it**

```xml
<!-- src/AcDream.Rynth.Plugin.RynthInventory/AcDream.Rynth.Plugin.RynthInventory.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <!-- RynthSuite's RynthInventory, compiled unchanged from the submodule as plain net10.0.
       Item spell names come from RynthAi's spell table, embedded under the plugin's own
       logical name. -->
  <PropertyGroup>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <RunAnalyzers>false</RunAnalyzers>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <IsPackable>false</IsPackable>
    <RynthSuitePlugins>$(MSBuildThisFileDirectory)..\..\external\RynthSuite\Plugins\</RynthSuitePlugins>
    <InventoryPlugin>$(RynthSuitePlugins)RynthCore.Plugin.RynthInventory\</InventoryPlugin>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="$(InventoryPlugin)**\*.cs" Exclude="$(InventoryPlugin)obj\**;$(InventoryPlugin)bin\**" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="$(RynthSuitePlugins)RynthCore.Plugin.RynthAi\Combat\SpellData.txt" LogicalName="RynthInventory.SpellData.txt" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AcDream.Rynth.Sdk\AcDream.Rynth.Sdk.csproj" />
  </ItemGroup>
</Project>
```

In `AcDream.slnx`, after the RynthOracle `<Project .../>` line add:

```xml
    <Project Path="src/AcDream.Rynth.Plugin.RynthInventory/AcDream.Rynth.Plugin.RynthInventory.csproj" />
```

In `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj`, after the RynthOracle `ProjectReference` add:

```xml
    <ProjectReference Include="..\AcDream.Rynth.Plugin.RynthInventory\AcDream.Rynth.Plugin.RynthInventory.csproj" />
```

If the wrapper fails to compile because a submodule file uses something the Sdk wrapper lacks (as RynthOracle's `FellowshipTracker.cs` did), stop and report it; do not edit the submodule. The audit found no `RynthCore.Engine` or kernel32 use in this plugin, so none is expected.

- [ ] **Step 4: Run test to verify it passes**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~RynthInventoryTests"`
Expected: PASS (2). Then check `ls ~/.config/RynthCore 2>/dev/null` shows nothing new from this run.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Rynth.Plugin.RynthInventory AcDream.slnx src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj tests/AcDream.Rynth.Host.Tests/RynthInventoryTests.cs
git commit -m "feat(rynth): RynthInventory compiled from the submodule and loaded by the host"
```

---

### Task 2: RynthObjectInfo and the stat-number mapping

**Goal:** A pure record of an object's fields and a mapper that answers RynthCore's int / data-id / instance-id property reads from it.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthObjectInfo.cs`
- Create: `src/AcDream.Rynth.Host/RynthObjectProperties.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthObjectPropertiesTests.cs`

**Acceptance Criteria:**
- [ ] Int: 1 Type, 5 Burden, 6 ItemsCapacity, 10 EquipLocation, 11 StackSizeMax, 12 StackSize, 19 Value, 91 MaxStructure, 92 Structure, 105 Workmanship (rounded), 131 MaterialType (only when known); any other number reads the Ints dictionary; missing → false.
- [ ] DataId: 8 Icon, 50 IconOverlay, 52 IconUnderlay answer true even when 0; 28 SpellId only when known; others from the DataIds dictionary.
- [ ] InstanceId: 2 Container, 3 Wielder, 32 HouseOwner (when known); others from the InstanceIds dictionary; a 0 value answers false.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~RynthObjectPropertiesTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AcDream.Rynth.Host.Tests/RynthObjectPropertiesTests.cs
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthObjectPropertiesTests
{
    private static RynthObjectInfo Item(
        Dictionary<uint, int>? ints = null,
        Dictionary<uint, uint>? dataIds = null,
        Dictionary<uint, uint>? instanceIds = null) => new()
    {
        ContainerId = 0x50000001,
        WielderId = 0,
        EquipLocation = 0,
        ItemType = 0x200,
        Wcid = 136,
        Bitfield = 0x1,
        IconId = 0x06001234,
        IconUnderlayId = 0,
        IconOverlayId = 0x06005678,
        SpellId = null,
        HouseOwnerId = null,
        StackSize = 3,
        StackSizeMax = 100,
        Value = 25,
        Burden = 15,
        ItemsCapacity = 24,
        Structure = 7,
        MaxStructure = 10,
        Workmanship = 6.6f,
        MaterialType = null,
        Identified = false,
        AppraisedSpellIds = [],
        Ints = ints ?? new(),
        DataIds = dataIds ?? new(),
        InstanceIds = instanceIds ?? new(),
    };

    [Theory]
    [InlineData(1u, 0x200)]
    [InlineData(5u, 15)]
    [InlineData(6u, 24)]
    [InlineData(10u, 0)]
    [InlineData(11u, 100)]
    [InlineData(12u, 3)]
    [InlineData(19u, 25)]
    [InlineData(91u, 10)]
    [InlineData(92u, 7)]
    [InlineData(105u, 7)]
    public void IntsComeFromTheTypedFields(uint stat, int expected)
    {
        Assert.True(RynthObjectProperties.TryGetInt(Item(), stat, out int value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void OtherIntsComeFromTheDictionaryAndMaterialOnlyWhenKnown()
    {
        RynthObjectInfo item = Item(ints: new() { [28] = 120 });

        Assert.True(RynthObjectProperties.TryGetInt(item, 28, out int armor));
        Assert.Equal(120, armor);
        Assert.False(RynthObjectProperties.TryGetInt(item, 131, out _));
        Assert.False(RynthObjectProperties.TryGetInt(item, 265, out _));
        Assert.True(RynthObjectProperties.TryGetInt(item with { MaterialType = 61 }, 131, out int material));
        Assert.Equal(61, material);
    }

    [Fact]
    public void IconsAnswerEvenWhenNone()
    {
        RynthObjectInfo item = Item(dataIds: new() { [22] = 0x0D000001 });

        Assert.True(RynthObjectProperties.TryGetDataId(item, 8, out uint icon));
        Assert.Equal(0x06001234u, icon);
        Assert.True(RynthObjectProperties.TryGetDataId(item, 52, out uint underlay));
        Assert.Equal(0u, underlay);
        Assert.True(RynthObjectProperties.TryGetDataId(item, 50, out uint overlay));
        Assert.Equal(0x06005678u, overlay);
        Assert.False(RynthObjectProperties.TryGetDataId(item, 28, out _));
        Assert.True(RynthObjectProperties.TryGetDataId(item with { SpellId = 2053 }, 28, out uint spell));
        Assert.Equal(2053u, spell);
        Assert.True(RynthObjectProperties.TryGetDataId(item, 22, out uint other));
        Assert.Equal(0x0D000001u, other);
    }

    [Fact]
    public void InstanceIdsAreFalseWhenUnset()
    {
        RynthObjectInfo item = Item(instanceIds: new() { [26] = 0x50000009 });

        Assert.True(RynthObjectProperties.TryGetInstanceId(item, 2, out uint container));
        Assert.Equal(0x50000001u, container);
        Assert.False(RynthObjectProperties.TryGetInstanceId(item, 3, out _));
        Assert.False(RynthObjectProperties.TryGetInstanceId(item, 32, out _));
        Assert.True(RynthObjectProperties.TryGetInstanceId(item with { HouseOwnerId = 0x50000002 }, 32, out uint owner));
        Assert.Equal(0x50000002u, owner);
        Assert.True(RynthObjectProperties.TryGetInstanceId(item, 26, out uint monarch));
        Assert.Equal(0x50000009u, monarch);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~RynthObjectPropertiesTests"`
Expected: build error, `RynthObjectInfo` not found.

- [ ] **Step 3: Implement**

```csharp
// src/AcDream.Rynth.Host/RynthObjectInfo.cs
namespace AcDream.Rynth.Host;

/// <summary>
/// What the RynthCore table reads of one object: the client's typed fields
/// (from the object's public description) and its property tables.
/// </summary>
public sealed record RynthObjectInfo
{
    public required uint ContainerId { get; init; }
    public required uint WielderId { get; init; }
    public required uint EquipLocation { get; init; }
    public required uint ItemType { get; init; }
    public required uint Wcid { get; init; }
    public required uint Bitfield { get; init; }
    public required uint IconId { get; init; }
    public required uint IconUnderlayId { get; init; }
    public required uint IconOverlayId { get; init; }
    public required uint? SpellId { get; init; }
    public required uint? HouseOwnerId { get; init; }
    public required int StackSize { get; init; }
    public required int StackSizeMax { get; init; }
    public required int Value { get; init; }
    public required int Burden { get; init; }
    public required int ItemsCapacity { get; init; }
    public required int Structure { get; init; }
    public required int MaxStructure { get; init; }
    public required float Workmanship { get; init; }
    public required uint? MaterialType { get; init; }

    /// <summary>An identify of this object came back this session.</summary>
    public required bool Identified { get; init; }

    public required IReadOnlyList<uint> AppraisedSpellIds { get; init; }
    public required IReadOnlyDictionary<uint, int> Ints { get; init; }
    public required IReadOnlyDictionary<uint, uint> DataIds { get; init; }
    public required IReadOnlyDictionary<uint, uint> InstanceIds { get; init; }
}
```

```csharp
// src/AcDream.Rynth.Host/RynthObjectProperties.cs
namespace AcDream.Rynth.Host;

/// <summary>
/// RynthCore's property reads by stat number. The numbers the client keeps in
/// typed fields answer from those (as RynthCore reads its public description
/// first); the rest come from the object's property tables.
/// </summary>
public static class RynthObjectProperties
{
    public static bool TryGetInt(RynthObjectInfo info, uint stat, out int value)
    {
        ArgumentNullException.ThrowIfNull(info);
        int? known = stat switch
        {
            1 => unchecked((int)info.ItemType),
            5 => info.Burden,
            6 => info.ItemsCapacity,
            10 => unchecked((int)info.EquipLocation),
            11 => info.StackSizeMax,
            12 => info.StackSize,
            19 => info.Value,
            91 => info.MaxStructure,
            92 => info.Structure,
            105 => (int)MathF.Round(info.Workmanship),
            131 => info.MaterialType is uint material ? unchecked((int)material) : null,
            _ => null,
        };
        if (known is int found)
        {
            value = found;
            return true;
        }

        return info.Ints.TryGetValue(stat, out value);
    }

    // Icon, overlay and underlay answer true with 0 for "none", as RynthCore does.
    public static bool TryGetDataId(RynthObjectInfo info, uint stat, out uint value)
    {
        ArgumentNullException.ThrowIfNull(info);
        switch (stat)
        {
            case 8:
                value = info.IconId;
                return true;
            case 50:
                value = info.IconOverlayId;
                return true;
            case 52:
                value = info.IconUnderlayId;
                return true;
            case 28 when info.SpellId is uint spell:
                value = spell;
                return true;
            default:
                return info.DataIds.TryGetValue(stat, out value);
        }
    }

    // 0 is "not set": RynthCore answers false for it.
    public static bool TryGetInstanceId(RynthObjectInfo info, uint stat, out uint value)
    {
        ArgumentNullException.ThrowIfNull(info);
        value = stat switch
        {
            2 => info.ContainerId,
            3 => info.WielderId,
            32 => info.HouseOwnerId ?? 0,
            _ => info.InstanceIds.TryGetValue(stat, out uint found) ? found : 0,
        };
        return value != 0;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~RynthObjectPropertiesTests"`
Expected: PASS (13).

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Rynth.Host/RynthObjectInfo.cs src/AcDream.Rynth.Host/RynthObjectProperties.cs tests/AcDream.Rynth.Host.Tests/RynthObjectPropertiesTests.cs
git commit -m "feat(rynth): RynthCore property reads by stat number over an object record"
```

---

### Task 3: The game view reads objects, contents, identity and position

**Goal:** `IRynthGameView` gains the reads RynthInventory's slots need, implemented over `GameRuntime` and in the test fake.

**Files:**
- Modify: `src/AcDream.Rynth.Host/IRynthGameView.cs`
- Modify: `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs`
- Modify: `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`
- Test: `tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs`

**Acceptance Criteria:**
- [ ] `TryGetObject` is false for an unknown id and fills every `RynthObjectInfo` field from the `ClientObject` (bitfield `null` → 0, `Type`/`CurrentlyEquippedLocation` cast to uint, `Identified` = `AppraisalAnswered && !LastAppraisalUnsuccessful`).
- [ ] For the player, the Ints/DataIds/InstanceIds tables come from `CharacterOwner.LocalPlayer.Properties` when it has a description, merged over the player object's own (player description wins).
- [ ] `GetContainerContents(c)` is `Objects.GetContents(c)` ∪ ids of `Objects.GetEquippedBy(c)`, distinct, ascending.
- [ ] `AccountName` / `WorldName` read `CharacterSelection.Snapshot`.
- [ ] `TryGetObjectPosition` is true only for an entity with a physics body and a non-zero cell; it returns the cell id and the cell-local origin.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~RuntimeRynthGameViewTests"` → all pass, and `~/.dotnet/dotnet build tests/AcDream.Rynth.Host.Tests -nologo` succeeds

**Steps:**

- [ ] **Step 1: Write the failing test**

Add to `RuntimeRynthGameViewTests` (add `using AcDream.Rynth.Host;` is already there):

```csharp
    [Fact]
    public void ReadsObjectFieldsAndContentsWithWieldedItems()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var pack = new ClientObject
        {
            ObjectId = 0x80000020,
            Name = "Pack",
            Type = ItemType.Container,
            ContainerId = 0x50000001,
            ItemsCapacity = 24,
            IconId = 0x06001111,
            WeenieClassId = 136,
            PublicWeenieBitfield = 0x1,
        };
        pack.Properties.Ints[28] = 5;
        var sword = new ClientObject
        {
            ObjectId = 0x80000010,
            Name = "Sword",
            WielderId = 0x50000001,
            CurrentlyEquippedLocation = EquipMask.MeleeWeapon,
        };
        runtime.InventoryOwner.Objects.AddOrUpdate(pack);
        runtime.InventoryOwner.Objects.AddOrUpdate(sword);
        var view = new RuntimeRynthGameView(runtime);

        Assert.False(view.TryGetObject(0x80000099, out _));
        Assert.True(view.TryGetObject(0x80000020, out RynthObjectInfo info));
        Assert.Equal(0x50000001u, info.ContainerId);
        Assert.Equal((uint)ItemType.Container, info.ItemType);
        Assert.Equal(24, info.ItemsCapacity);
        Assert.Equal(0x06001111u, info.IconId);
        Assert.Equal(136u, info.Wcid);
        Assert.Equal(0x1u, info.Bitfield);
        Assert.False(info.Identified);
        Assert.Equal(5, info.Ints[28]);
        Assert.True(view.TryGetObject(0x80000010, out RynthObjectInfo swordInfo));
        Assert.Equal((uint)EquipMask.MeleeWeapon, swordInfo.EquipLocation);
        Assert.Equal(0u, swordInfo.Bitfield);

        Assert.Equal([0x80000010u, 0x80000020u], view.GetContainerContents(0x50000001));
        Assert.Empty(view.GetContainerContents(0x80000020));
        Assert.False(view.TryGetObjectPosition(0x80000020, out _, out _));
        Assert.Equal(string.Empty, view.WorldName);
        Assert.Equal(string.Empty, view.AccountName);
    }
```

If `ItemType`/`EquipMask` are not in scope, they live in `AcDream.Core.Items` (already imported). If `ContainerId`/`WielderId` placement is not indexed by `AddOrUpdate` alone (check `ClientObjectTable.AddOrUpdate` and `GetContents`), set the placement the way existing App tests do (`git grep -n "GetEquippedBy\|GetContents(" tests/`) and keep the assertions.

- [ ] **Step 2: Run to verify it fails**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~RuntimeRynthGameViewTests"`
Expected: build error, `TryGetObject` not defined.

- [ ] **Step 3: Extend the interface**

Append to `IRynthGameView`:

```csharp
    /// <summary>An object's fields and property tables; false when the client does not know it.</summary>
    bool TryGetObject(uint objectId, out RynthObjectInfo info);

    /// <summary>
    /// The objects inside a container and the objects it wields, ascending by id,
    /// as RynthCore lists them (its inventory walk finds worn items this way).
    /// </summary>
    IReadOnlyList<uint> GetContainerContents(uint containerId);

    /// <summary>The logged-in account's name; empty before the character list.</summary>
    string AccountName { get; }

    /// <summary>The server's world name; empty before the server sends it.</summary>
    string WorldName { get; }

    /// <summary>A world object's cell and its position inside that cell.</summary>
    bool TryGetObjectPosition(uint objectId, out uint cellId, out System.Numerics.Vector3 origin);
```

- [ ] **Step 4: Implement in `RuntimeRynthGameView`**

Add `using AcDream.Runtime.Entities;` and these members:

```csharp
    public string AccountName => _runtime.CharacterSelection.Snapshot.AccountName;

    public string WorldName => _runtime.CharacterSelection.Snapshot.WorldName;

    public bool TryGetObject(uint objectId, out RynthObjectInfo info)
    {
        ClientObject? item = _runtime.InventoryOwner.Objects.Get(objectId);
        if (item is null)
        {
            info = null!;
            return false;
        }

        PropertyBundle properties = item.Properties;
        // The player's own tables come from the player description first, as
        // TryGetInt64Property reads them.
        if (objectId != 0 && objectId == PlayerId
            && _runtime.CharacterOwner.LocalPlayer is { HasDescription: true } player)
        {
            properties = Merge(item.Properties, player.Properties);
        }

        info = new RynthObjectInfo
        {
            ContainerId = item.ContainerId,
            WielderId = item.WielderId,
            EquipLocation = (uint)item.CurrentlyEquippedLocation,
            ItemType = (uint)item.Type,
            Wcid = item.WeenieClassId,
            Bitfield = item.PublicWeenieBitfield ?? 0,
            IconId = item.IconId,
            IconUnderlayId = item.IconUnderlayId,
            IconOverlayId = item.IconOverlayId,
            SpellId = item.SpellId,
            HouseOwnerId = item.HouseOwnerId,
            StackSize = item.StackSize,
            StackSizeMax = item.StackSizeMax,
            Value = item.Value,
            Burden = item.Burden,
            ItemsCapacity = item.ItemsCapacity,
            Structure = item.Structure,
            MaxStructure = item.MaxStructure,
            Workmanship = item.Workmanship,
            MaterialType = item.MaterialType,
            Identified = item.AppraisalAnswered && !item.LastAppraisalUnsuccessful,
            AppraisedSpellIds = item.AppraisedSpellIds,
            Ints = properties.Ints,
            DataIds = properties.DataIds,
            InstanceIds = properties.InstanceIds,
        };
        return true;
    }

    public IReadOnlyList<uint> GetContainerContents(uint containerId)
    {
        var ids = new SortedSet<uint>(_runtime.InventoryOwner.Objects.GetContents(containerId));
        foreach (ClientObject worn in _runtime.InventoryOwner.Objects.GetEquippedBy(containerId))
            ids.Add(worn.ObjectId);
        return [.. ids];
    }

    public bool TryGetObjectPosition(uint objectId, out uint cellId, out System.Numerics.Vector3 origin)
    {
        if (_runtime.EntityObjects.Entities.TryGetActive(objectId, out RuntimeEntityRecord record)
            && record.PhysicsBody is { } body
            && body.CellPosition.ObjCellId != 0)
        {
            cellId = body.CellPosition.ObjCellId;
            origin = body.CellPosition.Frame.Origin;
            return true;
        }

        cellId = 0;
        origin = default;
        return false;
    }

    private static PropertyBundle Merge(PropertyBundle objectProperties, PropertyBundle description)
    {
        PropertyBundle merged = objectProperties.Clone();
        foreach (KeyValuePair<uint, int> pair in description.Ints)
            merged.Ints[pair.Key] = pair.Value;
        foreach (KeyValuePair<uint, uint> pair in description.DataIds)
            merged.DataIds[pair.Key] = pair.Value;
        foreach (KeyValuePair<uint, uint> pair in description.InstanceIds)
            merged.InstanceIds[pair.Key] = pair.Value;
        return merged;
    }
```

`PropertyBundle` is in `AcDream.Core.Items` (already imported). If `TryGetActive`'s out type or namespace differs, follow `RuntimeAutomationSurface.cs:3387`.

- [ ] **Step 5: Extend `FakeGameView`**

```csharp
    public Dictionary<uint, RynthObjectInfo> Objects { get; } = new();
    public Dictionary<uint, List<uint>> Contents { get; } = new();
    public Dictionary<uint, (uint Cell, System.Numerics.Vector3 Origin)> Positions { get; } = new();
    public string AccountName { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;

    public bool TryGetObject(uint objectId, out RynthObjectInfo info)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        return Objects.TryGetValue(objectId, out info!);
    }

    public IReadOnlyList<uint> GetContainerContents(uint containerId)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        return Contents.TryGetValue(containerId, out List<uint>? ids) ? ids : [];
    }

    public bool TryGetObjectPosition(uint objectId, out uint cellId, out System.Numerics.Vector3 origin)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        bool found = Positions.TryGetValue(objectId, out var position);
        (cellId, origin) = position;
        return found;
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~RuntimeRynthGameViewTests"` → PASS (3)
Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo` → all pass

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Rynth.Host/IRynthGameView.cs src/AcDream.Rynth.Host/RuntimeRynthGameView.cs tests/AcDream.Rynth.Host.Tests/FakeGameView.cs tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs
git commit -m "feat(rynth): the game view reads objects, container contents, names and positions"
```

---

### Task 4: The API table fills RynthInventory's slots

**Goal:** `RynthApiTable` sets the 15 slots RynthInventory reads, answering from `IRynthGameView` through `RynthObjectProperties`.

**Files:**
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthInventoryTableTests.cs`

**Acceptance Criteria:**
- [ ] Slots set: `GetObjectIntPropertyFn`, `GetObjectDataIdPropertyFn`, `GetObjectInstanceIdPropertyFn`, `GetContainerContentsFn`, `GetObjectOwnershipInfoFn`, `GetObjectWielderInfoFn`, `GetItemTypeFn`, `GetObjectWcidFn`, `GetObjectBitfieldFn`, `HasAppraisalDataFn`, `GetObjectSpellIdsFn`, `GetObjectPositionFn`, `GetCurCoordsFn`, `GetAccountNameFn`, `GetWorldNameFn`.
- [ ] Through `RynthCoreHost`: ownership info for a known item returns container/wielder/location and is false for an unknown id; contents copy at most `max` ids and return the full count; spell ids return -1 for an unidentified item and the count for an identified one; account/world names round-trip and an empty name is false.
- [ ] `TryGetCurCoords` for the player at cell `0xA9B40031`, origin (84, 108, 0) gives EW = (0xA9*8 + 84/24 − 1019.5)/10 and NS = (0xB4*8 + 108/24 − 1019.5)/10.
- [ ] When the view throws, every new slot returns 0/false and logs, without crashing.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~RynthInventoryTableTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AcDream.Rynth.Host.Tests/RynthInventoryTableTests.cs
using System.Numerics;
using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthInventoryTableTests
{
    private const uint Player = 0x50000001;
    private const uint Pack = 0x80000020;
    private const uint Sword = 0x80000010;

    private readonly List<string> _log = new();
    private readonly FakeGameView _view = new() { PlayerId = Player };

    private static RynthObjectInfo Info(uint container, uint wielder, uint location, uint type) => new()
    {
        ContainerId = container,
        WielderId = wielder,
        EquipLocation = location,
        ItemType = type,
        Wcid = 136,
        Bitfield = 0x2000,
        IconId = 0x06001234,
        IconUnderlayId = 0,
        IconOverlayId = 0,
        SpellId = null,
        HouseOwnerId = null,
        StackSize = 1,
        StackSizeMax = 1,
        Value = 25,
        Burden = 15,
        ItemsCapacity = 24,
        Structure = 0,
        MaxStructure = 0,
        Workmanship = 0,
        MaterialType = null,
        Identified = false,
        AppraisedSpellIds = [],
        Ints = new Dictionary<uint, int>(),
        DataIds = new Dictionary<uint, uint>(),
        InstanceIds = new Dictionary<uint, uint>(),
    };

    private (RynthApiTable Table, RynthCoreHost Host) Create()
    {
        var table = new RynthApiTable(_view, _log.Add);
        return (table, new RynthCoreHost(*table.Native));
    }

    [Fact]
    public void ObjectReadsAnswerFromTheView()
    {
        _view.Objects[Pack] = Info(Player, 0, 0, 0x200);
        _view.Objects[Sword] = Info(0, Player, 0x100, 0x1) with
        {
            Identified = true,
            AppraisedSpellIds = [1, 2, 3],
        };
        var (table, host) = Create();
        using (table)
        {
            Assert.True(host.TryGetObjectOwnershipInfo(Pack, out uint container, out uint wielder, out uint location));
            Assert.Equal((Player, 0u, 0u), (container, wielder, location));
            Assert.False(host.TryGetObjectOwnershipInfo(0x80000099, out _, out _, out _));
            Assert.True(host.TryGetObjectWielderInfo(Sword, out uint swordWielder, out uint swordLocation));
            Assert.Equal((Player, 0x100u), (swordWielder, swordLocation));
            Assert.True(host.TryGetItemType(Pack, out uint type));
            Assert.Equal(0x200u, type);
            Assert.True(host.TryGetObjectWcid(Pack, out uint wcid));
            Assert.Equal(136u, wcid);
            Assert.True(host.TryGetObjectBitfield(Pack, out uint bitfield));
            Assert.Equal(0x2000u, bitfield);
            Assert.True(host.TryGetObjectIntProperty(Pack, 6, out int capacity));
            Assert.Equal(24, capacity);
            Assert.True(host.TryGetObjectDataIdProperty(Pack, 8, out uint icon));
            Assert.Equal(0x06001234u, icon);
            Assert.True(host.TryGetObjectInstanceIdProperty(Sword, 3, out uint iid));
            Assert.Equal(Player, iid);
            Assert.False(host.HasAppraisalData(Pack));
            Assert.True(host.HasAppraisalData(Sword));
            Assert.Equal(-1, host.GetObjectSpellIds(Pack, new uint[4], 4));
            uint[] spells = new uint[2];
            Assert.Equal(3, host.GetObjectSpellIds(Sword, spells, 2));
            Assert.Equal([1u, 2u], spells);
        }
    }

    [Fact]
    public void ContentsCopyAtMostTheBufferAndReturnTheCount()
    {
        _view.Contents[Player] = [Sword, Pack, 0x80000030];
        var (table, host) = Create();
        using (table)
        {
            uint[] two = new uint[2];
            Assert.Equal(2, host.GetContainerContents(Player, two));
            Assert.Equal([Sword, Pack], two);
            Assert.Equal(0, host.GetContainerContents(Pack, new uint[4]));
        }
    }

    [Fact]
    public void NamesAndCoordinates()
    {
        _view.AccountName = "acct";
        _view.WorldName = "Local ACE";
        _view.Positions[Player] = (0xA9B40031u, new Vector3(84, 108, 0));
        var (table, host) = Create();
        using (table)
        {
            Assert.True(host.TryGetAccountName(out string account));
            Assert.Equal("acct", account);
            Assert.True(host.TryGetWorldName(out string world));
            Assert.Equal("Local ACE", world);
            Assert.True(host.TryGetObjectPosition(Player, out uint cell, out float x, out float y, out _));
            Assert.Equal((0xA9B40031u, 84f, 108f), (cell, x, y));
            Assert.True(host.TryGetCurCoords(out double ns, out double ew));
            Assert.Equal((0xA9 * 8 + 84 / 24.0 - 1019.5) / 10, ew, 6);
            Assert.Equal((0xB4 * 8 + 108 / 24.0 - 1019.5) / 10, ns, 6);

            _view.AccountName = string.Empty;
            Assert.False(host.TryGetAccountName(out _));
        }
    }

    [Fact]
    public void AThrowingViewAnswersNothingAndLogs()
    {
        _view.Throw = true;
        var (table, host) = Create();
        using (table)
        {
            Assert.False(host.TryGetObjectOwnershipInfo(Pack, out _, out _, out _));
            Assert.False(host.TryGetObjectIntProperty(Pack, 6, out _));
            Assert.Equal(0, host.GetContainerContents(Player, new uint[4]));
            Assert.False(host.TryGetWorldName(out _));
            Assert.False(host.TryGetCurCoords(out _, out _));
            Assert.Contains(_log, line => line.Contains("threw InvalidOperationException", StringComparison.Ordinal));
        }
    }
}
```

`FakeGameView.PlayerId` throws when `Throw` is set; `TryGetCurCoords` reads it, which is the intended failure path.

- [ ] **Step 2: Run to verify they fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~RynthInventoryTableTests"`
Expected: FAIL (slots are zero: ownership/contents/name reads return false/0).

- [ ] **Step 3: Implement the slots**

In the constructor, after `GetSelectedItemIdFn`:

```csharp
        Native->GetObjectIntPropertyFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int*, int>)&GetObjectIntProperty;
        Native->GetObjectDataIdPropertyFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, uint*, int>)&GetObjectDataIdProperty;
        Native->GetObjectInstanceIdPropertyFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, uint*, int>)&GetObjectInstanceIdProperty;
        Native->GetContainerContentsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, int, int>)&GetContainerContents;
        Native->GetObjectOwnershipInfoFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, uint*, uint*, int>)&GetObjectOwnershipInfo;
        Native->GetObjectWielderInfoFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, uint*, int>)&GetObjectWielderInfo;
        Native->GetItemTypeFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, int>)&GetItemType;
        Native->GetObjectWcidFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint>)&GetObjectWcid;
        Native->GetObjectBitfieldFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint>)&GetObjectBitfield;
        Native->HasAppraisalDataFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&HasAppraisalData;
        Native->GetObjectSpellIdsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, int, int>)&GetObjectSpellIds;
        Native->GetObjectPositionFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, float*, float*, float*, int>)&GetObjectPosition;
        Native->GetCurCoordsFn = (IntPtr)(delegate* unmanaged[Cdecl]<double*, double*, int>)&GetCurCoords;
        Native->GetAccountNameFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr>)&GetAccountName;
        Native->GetWorldNameFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr>)&GetWorldName;
```

Add fields `private IntPtr _accountBuffer;` and `private IntPtr _worldBuffer;`, free them in `Dispose` next to `_nameBuffer`, and replace the body of `GetObjectName`'s buffer swap with a shared helper:

```csharp
    // RynthCore hands back a pointer the plugin reads at once (PtrToStringAnsi) and never
    // frees, so the host keeps one buffer per function and replaces it on the next call.
    private static IntPtr Replace(ref IntPtr buffer, string text)
    {
        IntPtr previous = buffer;
        buffer = Marshal.StringToHGlobalAnsi(text);
        if (previous != IntPtr.Zero)
            Marshal.FreeHGlobal(previous);
        return buffer;
    }
```

(`GetObjectName` then returns `Replace(ref table._nameBuffer, name)`; move the existing comment onto the helper.)

The new functions, each following the existing pattern:

```csharp
    private bool TryObject(uint objectId, out RynthObjectInfo info) => _view.TryGetObject(objectId, out info);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectIntProperty(uint objectId, uint stat, int* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info)
                || !RynthObjectProperties.TryGetInt(info, stat, out int found))
                return 0;
            *value = found;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectIntProperty), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectDataIdProperty(uint objectId, uint stat, uint* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info)
                || !RynthObjectProperties.TryGetDataId(info, stat, out uint found))
                return 0;
            *value = found;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectDataIdProperty), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectInstanceIdProperty(uint objectId, uint stat, uint* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info)
                || !RynthObjectProperties.TryGetInstanceId(info, stat, out uint found))
                return 0;
            *value = found;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectInstanceIdProperty), error);
            return 0;
        }
    }

    // RynthCore: copies at most max ids and returns how many there are.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetContainerContents(uint containerId, uint* ids, int max)
    {
        RynthApiTable? table = _current;
        if (table is null || ids is null || max <= 0)
            return 0;
        try
        {
            IReadOnlyList<uint> contents = table._view.GetContainerContents(containerId);
            int count = Math.Min(contents.Count, max);
            for (int i = 0; i < count; i++)
                ids[i] = contents[i];
            return contents.Count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetContainerContents), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectOwnershipInfo(uint objectId, uint* container, uint* wielder, uint* location)
    {
        RynthApiTable? table = _current;
        if (table is null || container is null || wielder is null || location is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info))
                return 0;
            *container = info.ContainerId;
            *wielder = info.WielderId;
            *location = info.EquipLocation;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectOwnershipInfo), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectWielderInfo(uint objectId, uint* wielder, uint* location)
    {
        RynthApiTable? table = _current;
        if (table is null || wielder is null || location is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info))
                return 0;
            *wielder = info.WielderId;
            *location = info.EquipLocation;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectWielderInfo), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetItemType(uint objectId, uint* flags)
    {
        RynthApiTable? table = _current;
        if (table is null || flags is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info))
                return 0;
            *flags = info.ItemType;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetItemType), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint GetObjectWcid(uint objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.TryObject(objectId, out RynthObjectInfo info) ? info.Wcid : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectWcid), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint GetObjectBitfield(uint objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.TryObject(objectId, out RynthObjectInfo info) ? info.Bitfield : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectBitfield), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int HasAppraisalData(uint objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.TryObject(objectId, out RynthObjectInfo info) && info.Identified ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(HasAppraisalData), error);
            return 0;
        }
    }

    // RynthCore: -1 when the object was never identified, else the spell count (may exceed max).
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectSpellIds(uint objectId, uint* ids, int max)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return -1;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info) || !info.Identified)
                return -1;
            int count = ids is null ? 0 : Math.Min(info.AppraisedSpellIds.Count, Math.Max(max, 0));
            for (int i = 0; i < count; i++)
                ids[i] = info.AppraisedSpellIds[i];
            return info.AppraisedSpellIds.Count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectSpellIds), error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectPosition(uint objectId, uint* cellId, float* x, float* y, float* z)
    {
        RynthApiTable? table = _current;
        if (table is null || cellId is null || x is null || y is null || z is null)
            return 0;
        try
        {
            if (!table._view.TryGetObjectPosition(objectId, out uint cell, out System.Numerics.Vector3 origin))
                return 0;
            *cellId = cell;
            *x = origin.X;
            *y = origin.Y;
            *z = origin.Z;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectPosition), error);
            return 0;
        }
    }

    // RynthCore's map coordinates from the player's cell (PlayerPhysicsHooks.TryGetLiveCoords):
    // EW = ((cell >> 24) * 8 + x / 24 - 1019.5) / 10, NS likewise from (cell >> 16) & 0xFF and y.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetCurCoords(double* northSouth, double* eastWest)
    {
        RynthApiTable? table = _current;
        if (table is null || northSouth is null || eastWest is null)
            return 0;
        try
        {
            if (!table._view.TryGetObjectPosition(table._view.PlayerId, out uint cell, out System.Numerics.Vector3 origin))
                return 0;
            *eastWest = (((cell >> 24) & 0xFF) * 8.0 + origin.X / 24.0 - 1019.5) / 10.0;
            *northSouth = (((cell >> 16) & 0xFF) * 8.0 + origin.Y / 24.0 - 1019.5) / 10.0;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetCurCoords), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr GetAccountName()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return IntPtr.Zero;
        try
        {
            string name = table._view.AccountName;
            return name.Length == 0 ? IntPtr.Zero : Replace(ref table._accountBuffer, name);
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetAccountName), error);
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr GetWorldName()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return IntPtr.Zero;
        try
        {
            string name = table._view.WorldName;
            return name.Length == 0 ? IntPtr.Zero : Replace(ref table._worldBuffer, name);
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetWorldName), error);
            return IntPtr.Zero;
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo`
Expected: all pass (the new 4 plus every existing test).

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Rynth.Host/RynthApiTable.cs tests/AcDream.Rynth.Host.Tests/RynthInventoryTableTests.cs
git commit -m "feat(rynth): API table answers object, contents, identity and coordinate reads"
```

---

### Task 5: A logged-in character's carried items are scanned

**Goal:** With the slots filled, RynthInventory scans a character on login and `/ginv status` reports the carried items.

**Files:**
- Test: `tests/AcDream.Rynth.Host.Tests/RynthInventoryTests.cs`

**Acceptance Criteria:**
- [ ] After `OnLifecycle(InWorld)`, a tick, `/ginv scan` and another tick, `/ginv status` prints a line containing `Tester on Local ACE (account acct)` and `2 items carried` for a player carrying one pack and one wielded sword.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~RynthInventoryTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the test**

Add to `RynthInventoryTests`:

```csharp
    private static RynthObjectInfo Item(uint container, uint wielder, uint type, int capacity = 0) => new()
    {
        ContainerId = container,
        WielderId = wielder,
        EquipLocation = wielder != 0 ? 0x100u : 0u,
        ItemType = type,
        Wcid = 1,
        Bitfield = 0,
        IconId = 0x06001234,
        IconUnderlayId = 0,
        IconOverlayId = 0,
        SpellId = null,
        HouseOwnerId = null,
        StackSize = 1,
        StackSizeMax = 1,
        Value = 10,
        Burden = 5,
        ItemsCapacity = capacity,
        Structure = 0,
        MaxStructure = 0,
        Workmanship = 0,
        MaterialType = null,
        Identified = false,
        AppraisedSpellIds = [],
        Ints = new Dictionary<uint, int>(),
        DataIds = new Dictionary<uint, uint>(),
        InstanceIds = new Dictionary<uint, uint>(),
    };

    [Fact]
    public void ScansTheCharactersCarriedItems()
    {
        const uint pack = 0x80000020, sword = 0x80000010;
        _view.AccountName = "acct";
        _view.WorldName = "Local ACE";
        _view.Names[_view.PlayerId] = "Tester";
        _view.Names[pack] = "Pack";
        _view.Names[sword] = "Sword";
        _view.Objects[_view.PlayerId] = Item(0, 0, 0x10);
        _view.Objects[pack] = Item(_view.PlayerId, 0, 0x200, capacity: 24);
        _view.Objects[sword] = Item(0, _view.PlayerId, 0x1);
        _view.Contents[_view.PlayerId] = [sword, pack];
        using RynthHost host = CreateInventoryHost();

        host.Bridge.OnLifecycle(new AcDream.Runtime.RuntimeLifecycleDelta(
            default, AcDream.Runtime.RuntimeLifecycleState.Starting, AcDream.Runtime.RuntimeLifecycleState.InWorld));
        host.Tick();
        host.Plugins[0].OnChatBarEnter("/ginv scan");
        host.Tick();
        host.Plugins[0].OnChatBarEnter("/ginv status");
        host.Tick();

        Assert.Contains(_view.ChatLines, line =>
            line.Text.Contains("Tester on Local ACE (account acct)", StringComparison.Ordinal)
            && line.Text.Contains("2 items carried", StringComparison.Ordinal));
    }
```

- [ ] **Step 2: Run it**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~RynthInventoryTests"`
Expected: PASS. If the count differs, read `InventoryScanner.FullScan`/`ScanContainer` (`external/RynthSuite/Plugins/RynthCore.Plugin.RynthInventory/Scanning/InventoryScanner.cs`) for what it skips (no name, unknown type 0, stale ownership) and fix the **host** side (Tasks 3–4) or the test fixture to match what the real client would report — never the submodule. A pack counts as an item if the scanner records it; adjust the expected count only after confirming from that code, and say why in the commit message.

- [ ] **Step 3: Commit**

```bash
git add tests/AcDream.Rynth.Host.Tests/RynthInventoryTests.cs
git commit -m "test(rynth): RynthInventory scans a character's carried items through the host"
```

---

### Task 6: Container open, close and contents reach the plugins

**Goal:** Plugins get `RynthPluginOnViewObjectContents`, `RynthPluginOnStopViewingObjectContents` and `RynthPluginOnUpdateObjectInventory` through the bridge.

**Files:**
- Modify: `src/AcDream.Rynth.Host/RynthPlugin.cs`
- Modify: `src/AcDream.Rynth.Host/RynthEventBridge.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthHostTests.cs`

**Acceptance Criteria:**
- [ ] `RynthPlugin.OnViewObjectContents/OnStopViewingObjectContents/OnUpdateObjectInventory(uint)` call the matching export inside a dispatch scope and are skipped when the export is missing or the plugin is inactive.
- [ ] `RynthEventBridge.OnContainerOpened(previous, id)` sends stop-viewing for a non-zero `previous` first, then view-contents for `id`; `OnContainerClosed(id)` sends stop-viewing; `OnContainerContentsChanged(id)` sends update-inventory; id 0 is never sent.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~RynthHostTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing test**

Add to `RynthHostTests` a recording exports class and a test:

```csharp
    [Fact]
    public void ContainerEventsReachThePlugin()
    {
        ContainerRecorder.Calls.Clear();
        using var host = new RynthHost(_view, _log.Add, [typeof(ContainerRecorder)]);

        host.Bridge.OnContainerOpened(0, 0x80000100);
        host.Bridge.OnContainerOpened(0x80000100, 0x80000200);
        host.Bridge.OnContainerContentsChanged(0x80000200);
        host.Bridge.OnContainerClosed(0x80000200);
        host.Bridge.OnContainerContentsChanged(0);

        Assert.Equal(
            [
                "view 80000100",
                "stop 80000100",
                "view 80000200",
                "update 80000200",
                "stop 80000200",
            ],
            ContainerRecorder.Calls);
    }

    private static unsafe class ContainerRecorder
    {
        public static readonly List<string> Calls = new();

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnViewObjectContents", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void View(uint id) => Calls.Add($"view {id:X8}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnStopViewingObjectContents", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Stop(uint id) => Calls.Add($"stop {id:X8}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnUpdateObjectInventory", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Update(uint id) => Calls.Add($"update {id:X8}");
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~RynthHostTests"`
Expected: build error, `OnContainerOpened` not defined.

- [ ] **Step 3: Implement**

In `RynthPlugin`, after `OnDeleteObject`:

```csharp
    public void OnViewObjectContents(uint containerId) => CallObject("RynthPluginOnViewObjectContents", containerId);

    public void OnStopViewingObjectContents(uint containerId) =>
        CallObject("RynthPluginOnStopViewingObjectContents", containerId);

    public void OnUpdateObjectInventory(uint containerId) =>
        CallObject("RynthPluginOnUpdateObjectInventory", containerId);
```

In `RynthEventBridge`, after `OnKillLanded`:

```csharp
    /// <summary>
    /// From <c>ExternalContainerState.Changed</c> (Opened): the client now shows
    /// <paramref name="containerId"/>, having stopped showing <paramref name="previousId"/>.
    /// </summary>
    public void OnContainerOpened(uint previousId, uint containerId)
    {
        if (previousId != 0 && previousId != containerId)
            OnContainerClosed(previousId);
        if (containerId == 0)
            return;
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnViewObjectContents(containerId);
    }

    /// <summary>From <c>ExternalContainerState.Changed</c> (Closed or Reset).</summary>
    public void OnContainerClosed(uint containerId)
    {
        if (containerId == 0)
            return;
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnStopViewingObjectContents(containerId);
    }

    /// <summary>From the object table: a container's contents were replaced or an item moved in or out.</summary>
    public void OnContainerContentsChanged(uint containerId)
    {
        if (containerId == 0)
            return;
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnUpdateObjectInventory(containerId);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Rynth.Host/RynthPlugin.cs src/AcDream.Rynth.Host/RynthEventBridge.cs tests/AcDream.Rynth.Host.Tests/RynthHostTests.cs
git commit -m "feat(rynth): container open, close and contents changes reach plugins"
```

---

### Task 7: The client extension loads RynthInventory, `/ginv` and container events

**Goal:** In the real client, RynthInventory runs, `/ginv` reaches it, and the runtime's container events feed the bridge.

**Files:**
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs`
- Test: `tests/AcDream.App.Tests/Extensions/RynthClientExtensionTests.cs`

**Acceptance Criteria:**
- [ ] The plugin list includes `typeof(RynthCore.Plugin.RynthInventory.PluginExports)`; the class summary names RynthInventory and `/ginv`.
- [ ] `/ginv` is registered and offers `"/ginv " + arguments` (trimmed) to the plugins, like `/ro`.
- [ ] `ExternalContainers.Changed` → `Opened` calls `OnContainerOpened(previous, id)`; `Closed` and `Reset` call `OnContainerClosed(previous)`; `Objects.ContainerContentsReplaced(id)` and `Objects.ObjectMoved` (previous and current container ids) call `OnContainerContentsChanged`.
- [ ] All four subscriptions are removed in `Dispose`.
- [ ] Extension test: `ginv` is among the registered verbs; requesting, opening and closing a container through `runtime.InventoryOwner.ExternalContainers` and replacing contents through `Objects.ReplaceContents` does not throw, before and after `Dispose`; the log has `[RynthInventory]`'s init line.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~Extensions.Rynth|FullyQualifiedName~RuntimeRynthGameView"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing test**

In `RynthClientExtensionTests`, set `XDG_CONFIG_HOME` to a temp directory for the class (same pattern as Task 1's `RynthInventoryTests`: constructor sets, `Dispose` restores and deletes; make the class `IDisposable`). Add:

```csharp
    [Fact]
    public void GinvIsRegisteredAndContainerEventsAreWired()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var log = new List<string>();
        var verbs = new List<string>();
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = log.Add,
            RegisterChatCommand = (verb, _) =>
            {
                verbs.Add(verb);
                return new Registration();
            },
        };
        var extension = new RynthClientExtension();
        extension.Attach(context);

        Assert.Contains("ginv", verbs);
        Assert.Contains(log, line => line.Contains("[RynthInventory]", StringComparison.Ordinal));
        var containers = runtime.InventoryOwner.ExternalContainers;
        Assert.True(containers.RequestOpen(0x80000100));
        Assert.True(containers.ApplyViewContents(0x80000100));
        runtime.InventoryOwner.Objects.ReplaceContents(0x80000100, [0x80000101u]);
        Assert.True(containers.ApplyClose(0x80000100));

        extension.Dispose();
        Assert.True(containers.RequestOpen(0x80000200));
        Assert.True(containers.ApplyViewContents(0x80000200));
    }
```

Check RynthInventory's init log text with `grep -n "Host.Log" external/RynthSuite/Plugins/RynthCore.Plugin.RynthInventory/RynthInventoryPlugin.cs | head -3` and assert on that exact prefix if it is not `[RynthInventory]`.

- [ ] **Step 2: Run to verify it fails**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~Extensions.RynthClientExtensionTests"`
Expected: FAIL, `ginv` not registered.

- [ ] **Step 3: Implement**

In `RynthClientExtension`:

1. Add `typeof(RynthCore.Plugin.RynthInventory.PluginExports),` after RynthOracle in the plugin list, and extend the summary: "Runs RynthCore plugins (RynthTracker, RynthOracle, RynthInventory) … `/rynth tracker`, `/ro` and `/ginv` reach the plugins."
2. After the `/ro` registration:

```csharp
        _commands.Add(context.RegisterChatCommand("ginv", arguments => OfferChatBarText("/ginv " + arguments)));
```

3. After the `LocalPlayerDied` subscription:

```csharp
        _runtime.InventoryOwner.ExternalContainers.Changed += OnExternalContainerChanged;
        _runtime.InventoryOwner.Objects.ContainerContentsReplaced += OnContainerContentsReplaced;
        _runtime.InventoryOwner.Objects.ObjectMoved += OnObjectMoved;
```

4. In `Dispose`, inside the `_runtime is not null` block before `_runtime = null;`:

```csharp
            _runtime.InventoryOwner.ExternalContainers.Changed -= OnExternalContainerChanged;
            _runtime.InventoryOwner.Objects.ContainerContentsReplaced -= OnContainerContentsReplaced;
            _runtime.InventoryOwner.Objects.ObjectMoved -= OnObjectMoved;
```

5. Handlers, after `OnLocalPlayerDied` (add `using AcDream.Core.Items;`):

```csharp
    private void OnExternalContainerChanged(ExternalContainerTransition transition)
    {
        if (_host is not { } host)
            return;
        switch (transition.Kind)
        {
            case ExternalContainerTransitionKind.Opened:
                host.Bridge.OnContainerOpened(transition.PreviousContainerId, transition.ContainerId);
                break;
            case ExternalContainerTransitionKind.Closed:
            case ExternalContainerTransitionKind.Reset:
                host.Bridge.OnContainerClosed(transition.PreviousContainerId);
                break;
        }
    }

    private void OnContainerContentsReplaced(uint containerId) =>
        _host?.Bridge.OnContainerContentsChanged(containerId);

    private void OnObjectMoved(ClientObjectMove move)
    {
        if (_host is not { } host)
            return;
        host.Bridge.OnContainerContentsChanged(move.Previous.ContainerId);
        if (move.Current.ContainerId != move.Previous.ContainerId)
            host.Bridge.OnContainerContentsChanged(move.Current.ContainerId);
    }
```

Check `Reset`'s `PreviousContainerId` in `ExternalContainerState` (`git grep -n "ExternalContainerTransitionKind.Reset" src/AcDream.Core`); if Reset carries the closed id elsewhere, pass that.

- [ ] **Step 4: Run tests to verify they pass**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~Extensions.Rynth|FullyQualifiedName~RuntimeRynthGameView"` → all pass
Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo` → all pass
Run: `~/.dotnet/dotnet build AcDream.slnx -nologo` → 0 errors

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Rynth.Host/RynthClientExtension.cs tests/AcDream.App.Tests/Extensions/RynthClientExtensionTests.cs
git commit -m "feat(rynth): the client runs RynthInventory: /ginv and container events"
```

---

### Task 8: Live check on local ACE

**Goal:** In the real client against local ACE, `/ginv` lists the character's carried items and an opened storage container is recorded.

**Files:**
- Modify: `docs/superpowers/plans/2026-10-18-rynth-inventory.md` (status note at the top)

**Acceptance Criteria:**
- [ ] The user agreed to the launch before it happened.
- [ ] `/ginv` opens the "Global Inventory" window listing items the character carries, with icons.
- [ ] `/ginv status` prints `<character> on <world> (account <account>)` with a non-zero item count, and after ~10 s a file exists at `~/.config/RynthCore/inventory/<world>/<account>/<character>.json`.
- [ ] Opening a storage chest (or `/ginv keep` on an opened container) then `/ginv status` shows `1 storage container(s)`.
- [ ] Findings are written as a status note at the top of this plan.

**Verify:** the user's observations in game plus `ls ~/.config/RynthCore/inventory/*/*/`

**Steps:**

- [ ] **Step 1: Ask the user before launching** — the client, the local ACE server 10.10.20.20 only, never Dreamweave.
- [ ] **Step 2: Launch** — `source ~/OpenAC-dev/dev-client.env`, `ACDREAM_LIVE=1`, a scratch `ACDREAM_ROOT_DIR`, `DYLD_LIBRARY_PATH`/`VK_DRIVER_FILES` from brew; never print the env file. "CharacterList not received" on the first launch is intermittent: relaunch.
- [ ] **Step 3: Check each acceptance criterion with the user and note the results** (also the RynthTracker deaths check from `feat/rynth-tracker-deaths` if the user wants to die once).
- [ ] **Step 4: Commit the status note**

```bash
git add docs/superpowers/plans/2026-10-18-rynth-inventory.md
git commit -m "docs: RynthInventory live check results"
```
