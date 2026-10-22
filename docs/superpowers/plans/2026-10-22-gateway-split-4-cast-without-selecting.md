# Gateway Split, Plan 4: Cast at a Target Without Selecting It — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plugins (Aethrix first) can check and send a cast at an object without the host changing the player's selection.

**Architecture:** The runtime already sends a cast at a given target without the selection (`RuntimeSpellCastState.Cast(spellId, targetId)`); this plan adds the matching gate check (`EvaluateCastGate(spellId, targetId)`), exposes both through two new default members on `IMagicCommands` (`EvaluateGateWithoutSelecting`, `RequestCastWithoutSelecting`), implements them in `RuntimeAutomationSurface` without touching `ActionOwner.Selection`, bumps the fork contract to `0.1.26-fork.6`, and switches Aethrix's `MagicSession.Send` to them.

**Tech Stack:** C# (.NET 10), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-22-cast-without-selecting-design.md`

## Global Constraints

- Additive only: `EvaluateGate(uint, uint)`, `RequestCast(uint, uint)` and `Cast(uint, uint)` keep selecting the target; their behaviour and docs do not change.
- Exact new contract members, on `IMagicCommands` in `src/AcDream.Plugin.Abstractions/Automation.cs`, with these defaults:
  - `PluginCastGate EvaluateGateWithoutSelecting(uint spellId, uint targetObjectId) => PluginCastGate.Refused;`
  - `PluginCastRequestResult RequestCastWithoutSelecting(uint spellId, uint targetObjectId) => PluginCastRequestResult.Unavailable;`
  - A host without them never falls back to selecting. `NoOpAutomationSurface` answers `PluginCastGate.Unavailable` / `PluginCastRequestResult.Unavailable`.
- `ForkContractVersion` becomes exactly `0.1.26-fork.6`. `<Version>`, `OpenACReleaseNumber` and lock files do not change.
- OpenAC work happens on branch `feat/cast-without-selecting` (already created from `main` in `~/code/OpenAC/OpenAC`, holding the spec and this plan). It is merged to `main` with `--no-ff` in Task 3, then `main` is merged into `rynth` in the worktree `~/code/OpenAC/OpenAC-gateway-rynth` (never `~/code/OpenAC/OpenAC-rynth` or `OpenAC-Rynth`). Task 3 runs in-session (background subagents are denied in the rynth worktree).
- Aethrix repo: `~/code/OpenAC/OpenAC-Plugins-Aethrix`, branch `main`. It builds against the OpenAC checkout at `../OpenAC`.
- Build/test environment (sandbox disabled for builds and tests):
  ```bash
  export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH \
         ACDREAM_DAT_DIR=$HOME/AsheronsCall TMPDIR=/tmp/acdt/ \
         DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib
  mkdir -p /tmp/acdt
  ```
- Full-suite baseline: `$HOME/code/OpenAC/split-baseline/presplit-failed.txt` (70 names) and `failed-tests.py`. Failures must stay a subset of it.
- After every OpenAC build revert lock churn: `git checkout -- '*.lock.json'` and delete untracked `packages.*.lock.json`.
- No live client is launched by the agent; the live check is the user's, when they say they are available.

**User decisions (already made):**
- "Yes proceed to write the implementation plan" — approach 1 (new members alongside the old ones; existing overloads keep selecting), contract bump to `0.1.26-fork.6`, Aethrix switches to the new members, no upstream PR in this plan, no `bool Cast…WithoutSelecting`.

---

## File Structure

| File | Change |
|---|---|
| `src/AcDream.Runtime/Gameplay/RuntimeSpellCastState.cs` | add `EvaluateCastGate(uint spellId, uint targetId)`; one-arg form delegates |
| `tests/AcDream.Runtime.Tests/Gameplay/RuntimeSpellCastStateTests.cs` | gate-at-target tests |
| `src/AcDream.Plugin.Abstractions/Automation.cs` | two `IMagicCommands` defaults; `NoOpAutomationSurface` answers |
| `src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj` | `ForkContractVersion` fork.6 |
| `src/AcDream.Runtime/Plugins/RuntimeAutomationSurface.cs` | `IsCastTarget`, two implementations |
| `tests/AcDream.Runtime.Tests/Plugins/CampaignAutomationSurfaceTests.cs` | host tests |
| `docs/plugin-api.md` | "Casting without selecting" under `## Spells` |
| Aethrix `Directory.Build.props`, `src/Aethrix/Core/Magic/MagicSession.cs`, `tests/Aethrix.Tests/Fakes/FakeAutomation.cs`, a magic-session test, `docs/api-gaps.md` | switch + fakes + gap entry |

---

### Task 1: Runtime gate check at a given target

**Goal:** `RuntimeSpellCastState.EvaluateCastGate(spellId, targetId)` answers from the given target without reading the selection.

**Files:**
- Modify: `src/AcDream.Runtime/Gameplay/RuntimeSpellCastState.cs` (the `EvaluateCastGate(uint spellId)` method, ~line 64)
- Test: `tests/AcDream.Runtime.Tests/Gameplay/RuntimeSpellCastStateTests.cs`

**Acceptance Criteria:**
- [ ] With object 99 selected, `EvaluateCastGate(1, 77u)` for a targeted spell returns `TargetCompatible` and the fake's compatibility check saw target 77, not 99.
- [ ] `EvaluateCastGate(1, 0u)` for a targeted spell returns `NoTargetSelected` even with 99 selected.
- [ ] A self-targeted spell returns `NoTargetNeeded` for any target.
- [ ] `EvaluateCastGate(1)` still answers from the selection (existing tests pass).

**Verify:** `dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~RuntimeSpellCastStateTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** The fake's `IsTargetCompatible` does not record the target yet; add a recorder to `FakeOperations` (in the same file):

```csharp
        public uint LastCompatibilityTarget { get; private set; }

        public bool IsTargetCompatible(
            uint targetId,
            SpellMetadata spell,
            bool showMessage)
        {
            LastCompatibilityTarget = targetId;
            return TargetCompatible;
        }
```

(replacing the existing expression-bodied `IsTargetCompatible`). Then add after `CastAtTarget_IncompatibleTargetIsRefused`:

```csharp
    [Fact]
    public void GateAtTarget_AnswersFromThatTargetNotTheSelection()
    {
        Spellbook book = MakeBook(flags: 0, untargeted: false, targetMask: 0x10);
        var operations = new FakeOperations { LocalPlayerId = 42u };
        RuntimeSpellCastState state = Create(book, operations, selected: 99u);

        Assert.Equal(SpellCastGate.TargetCompatible, state.EvaluateCastGate(1, 77u));
        Assert.Equal(77u, operations.LastCompatibilityTarget);
    }

    [Fact]
    public void GateAtTarget_IncompatibleTargetIsIncompatibleEvenWithASelection()
    {
        Spellbook book = MakeBook(flags: 0, untargeted: false, targetMask: 0x10);
        var operations = new FakeOperations { LocalPlayerId = 42u, TargetCompatible = false };
        RuntimeSpellCastState state = Create(book, operations, selected: 99u);

        Assert.Equal(SpellCastGate.TargetIncompatible, state.EvaluateCastGate(1, 77u));
    }

    [Fact]
    public void GateAtTarget_ZeroIsNoTargetEvenWithASelection()
    {
        Spellbook book = MakeBook(flags: 0, untargeted: false, targetMask: 0x10);
        var operations = new FakeOperations { LocalPlayerId = 42u };
        RuntimeSpellCastState state = Create(book, operations, selected: 99u);

        Assert.Equal(SpellCastGate.NoTargetSelected, state.EvaluateCastGate(1, 0u));
    }

    [Fact]
    public void GateAtTarget_SelfSpellNeedsNoTarget()
    {
        Spellbook book = MakeBook(flags: 0x8, untargeted: true, targetMask: 0x10);
        RuntimeSpellCastState state = Create(book, new FakeOperations { LocalPlayerId = 42u });

        Assert.Equal(SpellCastGate.NoTargetNeeded, state.EvaluateCastGate(1, 77u));
    }

    [Fact]
    public void Gate_WithoutATargetStillReadsTheSelection()
    {
        Spellbook book = MakeBook(flags: 0, untargeted: false, targetMask: 0x10);
        var operations = new FakeOperations { LocalPlayerId = 42u };
        RuntimeSpellCastState state = Create(book, operations, selected: 99u);

        Assert.Equal(SpellCastGate.TargetCompatible, state.EvaluateCastGate(1));
        Assert.Equal(99u, operations.LastCompatibilityTarget);
    }
```

- [ ] **Step 2: Run, expect a compile failure** (no `EvaluateCastGate(uint, uint)`):
`dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~RuntimeSpellCastStateTests"`

- [ ] **Step 3: Implement.** Replace the body of `EvaluateCastGate(uint spellId)` in `RuntimeSpellCastState.cs`:

```csharp
    public SpellCastGate EvaluateCastGate(uint spellId) =>
        EvaluateCastGate(spellId, _selection.SelectedObjectId ?? 0u);

    /// <summary>
    /// Whether casting at <paramref name="targetId"/> would be accepted,
    /// without reading the selection; 0 is no target. A self or untargeted
    /// spell needs no target, as in <see cref="EvaluateCastGate(uint)"/>.
    /// </summary>
    public SpellCastGate EvaluateCastGate(uint spellId, uint targetId)
    {
        if (!_spellbook.Knows(spellId)
            || !_spellbook.TryGetMetadata(spellId, out SpellMetadata spell))
            return SpellCastGate.Unknown;
        if (spell.IsSelfTargeted || spell.IsUntargeted || spell.TargetMask == 0u)
            return SpellCastGate.NoTargetNeeded;
        if (targetId == 0u)
            return SpellCastGate.NoTargetSelected;
        return _operations.IsTargetCompatible(targetId, spell, showMessage: false)
            ? SpellCastGate.TargetCompatible
            : SpellCastGate.TargetIncompatible;
    }
```

Check `_selection.SelectedObjectId`'s type first (`uint?` per the existing `is uint target` pattern); if it is a non-nullable `uint`, drop `?? 0u`.

- [ ] **Step 4: Run, expect PASS** (same command). Also run the whole runtime test project's `Gameplay` folder: `dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~Gameplay"`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Runtime/Gameplay/RuntimeSpellCastState.cs tests/AcDream.Runtime.Tests/Gameplay/RuntimeSpellCastStateTests.cs
git commit -m "feat(runtime): evaluate a cast gate at a given target without the selection"
```

---

### Task 2: Plugin contract and host members, contract fork.6

**Goal:** `IMagicCommands.EvaluateGateWithoutSelecting` / `RequestCastWithoutSelecting` exist with refusing defaults, the inert surface answers `Unavailable`, `RuntimeAutomationSurface` implements them without touching the selection, the contract is `0.1.26-fork.6`, and `docs/plugin-api.md` documents them.

**Files:**
- Modify: `src/AcDream.Plugin.Abstractions/Automation.cs` (`IMagicCommands` ~line 1076-1140; `NoOpAutomationSurface` magic members ~line 1452-1463)
- Modify: `src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj:39`
- Modify: `src/AcDream.Runtime/Plugins/RuntimeAutomationSurface.cs` (magic members ~4138-4225; `SelectExplicitTarget` ~6341)
- Modify: `docs/plugin-api.md` (end of `## Spells`, before `## Storage`)
- Test: `tests/AcDream.Runtime.Tests/Plugins/CampaignAutomationSurfaceTests.cs`

**Acceptance Criteria:**
- [ ] With object A selected and active object B, `EvaluateGateWithoutSelecting(42, B)` is `Ready`, `RequestCastWithoutSelecting(42, B)` is `Sent`, the operations fake saw a targeted send to B, and `Selection.SelectedObjectId` is still A.
- [ ] An inactive/unknown target gives `Refused` / `IncompatibleTarget`, sends nothing, and leaves the selection alone.
- [ ] `RequestCast(42, B)` (selecting overload) still changes the selection to B.
- [ ] `NoOpAutomationSurface.Instance` answers `Unavailable` for both new members; an `IMagicCommands` implementation that does not override them answers `Refused` / `Unavailable`.
- [ ] `ForkContractVersion` is `0.1.26-fork.6`; `docs/plugin-api.md` names both members and the contract.

**Verify:** `dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~CampaignAutomationSurfaceTests"` → all pass; `dotnet build src/AcDream.Plugin.Abstractions -c Release` → 0 warnings 0 errors

**Steps:**

- [ ] **Step 1: Write the failing tests** in `CampaignAutomationSurfaceTests.cs`, after `PendingCastReceiptDoesNotBlockAnotherCast`. They reuse the file's `DurationSpell()` (spell 42, targeted: `IsUntargeted` false, `TargetMask` 1) and change `SpellOperations` to record targeted sends:

```csharp
    private sealed class SpellOperations : IRuntimeSpellCastOperations
    {
        public List<(uint TargetId, uint SpellId)> TargetedSends { get; } = [];
        public uint LocalPlayerId => 1u;
        public bool CanSend => true;
        public bool HasRequiredComponents(uint spellId) => true;
        public bool IsTargetCompatible(
            uint targetId,
            SpellMetadata spell,
            bool showMessage) => true;
        public void StopCompletely() { }
        public void SendUntargeted(uint spellId) { }
        public void SendTargeted(uint targetId, uint spellId) =>
            TargetedSends.Add((targetId, spellId));
        public void DisplayMessage(string message) { }
        public void IncrementBusy() { }
    }
```

Tests (add `using AcDream.Core.Net;`, `using AcDream.Core.Net.Messages;` and `using AcDream.Runtime.Entities;` if missing):

```csharp
    [Fact]
    public void CastWithoutSelectingSendsAtTheTargetAndKeepsTheSelection()
    {
        const uint selected = 0x70000001u;
        const uint target = 0x70000002u;
        var operations = new SpellOperations();
        using var runtime = GameRuntimeTestFactory.Create(spellCast: operations);
        runtime.CharacterOwner.InstallSpellMetadata(SpellTable.Create([DurationSpell()]));
        runtime.CharacterOwner.Spellbook.OnSpellLearned(42u);
        Activate(runtime, selected);
        Activate(runtime, target);
        runtime.ActionOwner.Selection.Select(selected, SelectionChangeSource.Plugin);
        using var surface = new RuntimeAutomationSurface();
        surface.Bind(runtime, runtime.CharacterOwner, runtime.ActionOwner.SpellCast);

        Assert.Equal(
            PluginCastGate.Ready,
            surface.Magic.EvaluateGateWithoutSelecting(42u, target));
        Assert.Equal(
            PluginCastRequestResult.Sent,
            surface.Magic.RequestCastWithoutSelecting(42u, target));

        Assert.Equal((target, 42u), Assert.Single(operations.TargetedSends));
        Assert.Equal(selected, runtime.ActionOwner.Selection.SelectedObjectId);
    }

    [Fact]
    public void CastWithoutSelectingRefusesAnObjectThatIsNotInTheWorld()
    {
        const uint selected = 0x70000001u;
        var operations = new SpellOperations();
        using var runtime = GameRuntimeTestFactory.Create(spellCast: operations);
        runtime.CharacterOwner.InstallSpellMetadata(SpellTable.Create([DurationSpell()]));
        runtime.CharacterOwner.Spellbook.OnSpellLearned(42u);
        Activate(runtime, selected);
        runtime.ActionOwner.Selection.Select(selected, SelectionChangeSource.Plugin);
        using var surface = new RuntimeAutomationSurface();
        surface.Bind(runtime, runtime.CharacterOwner, runtime.ActionOwner.SpellCast);

        Assert.Equal(
            PluginCastGate.Refused,
            surface.Magic.EvaluateGateWithoutSelecting(42u, 0x70000099u));
        Assert.Equal(
            PluginCastRequestResult.IncompatibleTarget,
            surface.Magic.RequestCastWithoutSelecting(42u, 0x70000099u));
        Assert.Empty(operations.TargetedSends);
        Assert.Equal(selected, runtime.ActionOwner.Selection.SelectedObjectId);
    }

    [Fact]
    public void TheSelectingCastOverloadStillSelects()
    {
        const uint target = 0x70000002u;
        var operations = new SpellOperations();
        using var runtime = GameRuntimeTestFactory.Create(spellCast: operations);
        runtime.CharacterOwner.InstallSpellMetadata(SpellTable.Create([DurationSpell()]));
        runtime.CharacterOwner.Spellbook.OnSpellLearned(42u);
        Activate(runtime, target);
        using var surface = new RuntimeAutomationSurface();
        surface.Bind(runtime, runtime.CharacterOwner, runtime.ActionOwner.SpellCast);

        Assert.Equal(
            PluginCastRequestResult.Sent,
            surface.Magic.RequestCast(42u, target));
        Assert.Equal(target, runtime.ActionOwner.Selection.SelectedObjectId);
    }

    [Fact]
    public void CastWithoutSelectingIsRefusedWhereTheHostLacksIt()
    {
        Assert.Equal(
            PluginCastGate.Unavailable,
            NoOpAutomationSurface.Instance.Magic.EvaluateGateWithoutSelecting(1u, 2u));
        Assert.Equal(
            PluginCastRequestResult.Unavailable,
            NoOpAutomationSurface.Instance.Magic.RequestCastWithoutSelecting(1u, 2u));

        IMagicCommands bare = new BareMagic();
        Assert.Equal(PluginCastGate.Refused, bare.EvaluateGateWithoutSelecting(1u, 2u));
        Assert.Equal(
            PluginCastRequestResult.Unavailable,
            bare.RequestCastWithoutSelecting(1u, 2u));
    }

    private sealed class BareMagic : IMagicCommands
    {
        public bool IsCasting => false;
        public PluginCastGate EvaluateGate(uint spellId) => PluginCastGate.Ready;
        public bool Cast(uint spellId) => true;
    }

    /// <summary>Puts <paramref name="guid"/> in the world as an active entity.</summary>
    private static void Activate(GameRuntime runtime, uint guid)
    {
        var spawn = new WorldSession.EntitySpawn(
            guid,
            null,
            0x02000001u,
            Array.Empty<CreateObject.AnimPartChange>(),
            Array.Empty<CreateObject.TextureChange>(),
            Array.Empty<CreateObject.SubPaletteSwap>(),
            null,
            null,
            "a creature",
            null,
            null,
            null,
            PhysicsState: 0,
            InstanceSequence: 1,
            MovementSequence: 1,
            ServerControlSequence: 1,
            PositionSequence: 1);
        RuntimeEntityRecord record = runtime.EntityObjects
            .RegisterEntity(spawn)
            .Canonical!;
        Assert.True(runtime.EntityObjects.ApplyAcceptedSpawn(
            record,
            record.CreateIntegrationVersion,
            record.Snapshot,
            replaceGeneration: false));
        Assert.True(runtime.EntityObjects.Entities.TryGetActive(guid, out _));
    }
```

`Activate` is the same registration `RuntimeForgetStaleItemTests.Carry` uses, minus the container. If `TryGetActive` is still false after it (the final assert fails), look at how `RuntimeGenerationResetTests.Spawn` builds a positioned spawn with physics and use that shape instead; the test's intent (an active entity) is fixed, the construction is not.

- [ ] **Step 2: Run, expect compile failure** (members missing):
`dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~CampaignAutomationSurfaceTests"`

- [ ] **Step 3: Contract.** In `IMagicCommands` (`Automation.cs`), after the `RequestCast(uint spellId, uint targetObjectId)` default and before `HasComponents`:

```csharp
    /// <summary>
    /// Whether casting this spell at a particular object would be accepted,
    /// without selecting it: the player's selection is neither read nor
    /// changed. Reports <see cref="PluginCastGate.Refused"/> when that
    /// object is not in the world, and on a host that cannot cast without
    /// selecting.
    /// </summary>
    PluginCastGate EvaluateGateWithoutSelecting(
        uint spellId, uint targetObjectId) =>
        PluginCastGate.Refused;

    /// <summary>
    /// Casts the spell at the given object without selecting it, and reports
    /// what happened; the player's selection is neither read nor changed.
    /// Reports <see cref="PluginCastRequestResult.IncompatibleTarget"/> when
    /// that object is not in the world, and
    /// <see cref="PluginCastRequestResult.Unavailable"/> on a host that
    /// cannot cast without selecting.
    /// </summary>
    PluginCastRequestResult RequestCastWithoutSelecting(
        uint spellId, uint targetObjectId) =>
        PluginCastRequestResult.Unavailable;
```

In `NoOpAutomationSurface`, after `public bool Cast(uint spellId, uint targetObjectId) => false;`:

```csharp
    /// <inheritdoc/>
    public PluginCastGate EvaluateGateWithoutSelecting(
        uint spellId, uint targetObjectId) =>
        PluginCastGate.Unavailable;

    /// <inheritdoc/>
    public PluginCastRequestResult RequestCastWithoutSelecting(
        uint spellId, uint targetObjectId) =>
        PluginCastRequestResult.Unavailable;
```

In `AcDream.Plugin.Abstractions.csproj` line 39: `<ForkContractVersion>0.1.26-fork.6</ForkContractVersion>`.

- [ ] **Step 4: Host.** In `RuntimeAutomationSurface.cs`, split `SelectExplicitTarget` into a check and the select:

```csharp
    private bool SelectExplicitTarget(uint targetObjectId)
    {
        if (!IsCastTarget(targetObjectId, out GameRuntime? runtime))
            return false;
        runtime.ActionOwner.Selection.Select(
            targetObjectId,
            SelectionChangeSource.Plugin);
        return true;
    }

    private bool IsCastTarget(
        uint targetObjectId,
        [NotNullWhen(true)] out GameRuntime? runtime)
    {
        lock (_gate)
            runtime = _runtime;
        return runtime is not null && IsAvailable && targetObjectId != 0u
            && runtime.EntityObjects.Entities.TryGetActive(targetObjectId, out _);
    }
```

(add `using System.Diagnostics.CodeAnalysis;` if the file lacks it). Extract the two existing result mappings so the new members share them — change `EvaluateGate(uint spellId)` and `RequestCast(uint spellId)` to:

```csharp
    public PluginCastGate EvaluateGate(uint spellId) =>
        EvaluateGateAt(spellId, cast => cast.EvaluateCastGate(spellId));

    public PluginCastGate EvaluateGateWithoutSelecting(
        uint spellId, uint targetObjectId) =>
        IsCastTarget(targetObjectId, out _)
            ? EvaluateGateAt(
                spellId,
                cast => cast.EvaluateCastGate(spellId, targetObjectId))
            : PluginCastGate.Refused;

    private PluginCastGate EvaluateGateAt(
        uint spellId, Func<RuntimeSpellCastState, SpellCastGate> evaluate)
    {
        RuntimeSpellCastState? cast;
        Spellbook? spellbook;
        lock (_gate)
        {
            cast = _cast;
            spellbook = _spellbook;
        }
        if (cast is null || spellbook is null || !IsAvailable)
            return PluginCastGate.Unavailable;
        if (!spellbook.Knows(spellId))
            return PluginCastGate.NotKnown;
        return evaluate(cast) switch
        {
            SpellCastGate.Unknown => PluginCastGate.NotKnown,
            SpellCastGate.NoTargetNeeded => PluginCastGate.Ready,
            SpellCastGate.TargetCompatible => PluginCastGate.Ready,
            SpellCastGate.NoTargetSelected => PluginCastGate.NoTargetSelected,
            SpellCastGate.TargetIncompatible => PluginCastGate.TargetIncompatible,
            _ => PluginCastGate.Refused,
        };
    }
```

and

```csharp
    public PluginCastRequestResult RequestCast(uint spellId) =>
        RequestCastAt(cast => cast.Cast(spellId));

    public PluginCastRequestResult RequestCastWithoutSelecting(
        uint spellId, uint targetObjectId) =>
        IsCastTarget(targetObjectId, out _)
            ? RequestCastAt(cast => cast.Cast(spellId, targetObjectId))
            : PluginCastRequestResult.IncompatibleTarget;

    private PluginCastRequestResult RequestCastAt(
        Func<RuntimeSpellCastState, CastRequestResult> send)
    {
        RuntimeSpellCastState? cast;
        lock (_gate)
            cast = _cast;
        if (cast is null)
            return PluginCastRequestResult.Unavailable;
        return send(cast) switch
        {
            CastRequestResult.Sent => PluginCastRequestResult.Sent,
            CastRequestResult.UnknownSpell => PluginCastRequestResult.UnknownSpell,
            CastRequestResult.NoTarget => PluginCastRequestResult.NoTarget,
            CastRequestResult.IncompatibleTarget =>
                PluginCastRequestResult.IncompatibleTarget,
            CastRequestResult.MissingComponents =>
                PluginCastRequestResult.MissingComponents,
            _ => PluginCastRequestResult.Unavailable,
        };
    }
```

The existing selecting overloads (`EvaluateGate(uint, uint)`, `Cast(uint, uint)`, `RequestCast(uint, uint)`) stay as they are.

- [ ] **Step 5: Docs.** In `docs/plugin-api.md`, at the end of `## Spells` (just before `## Storage`), add:

````markdown
### Casting without selecting

`EvaluateGate(spellId, target)` and `RequestCast(spellId, target)` select the
target first, as a player clicking it would. To cast at an object and leave
the player's selection alone, use the `WithoutSelecting` pair:

```csharp
IMagicCommands magic = host.Automation.Magic;
if (magic.EvaluateGateWithoutSelecting(spellId, creatureId) == PluginCastGate.Ready)
    magic.RequestCastWithoutSelecting(spellId, creatureId);
```

The target must be in the world, or the gate is `Refused` and the request
`IncompatibleTarget`; self and untargeted spells ignore it. A host without
them refuses (`Refused` / `Unavailable`) rather than selecting.
They need contract `0.1.26-fork.6`: build against contract `0.1.26-fork.6` to
call them. An older host checks only MAJOR.MINOR.PATCH, so the plugin loads,
and the calls get the refusing defaults.
````

- [ ] **Step 6: Run, expect PASS:**
`dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~CampaignAutomationSurfaceTests|FullyQualifiedName~RuntimeSpellCastStateTests"` and `dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ScopedAutomationSurfaceTests"`. Then `dotnet build src/AcDream.Plugin.Abstractions -c Release` (no new warnings; the contract project treats missing XML docs as warnings). Revert lock churn.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Plugin.Abstractions/Automation.cs src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj src/AcDream.Runtime/Plugins/RuntimeAutomationSurface.cs tests/AcDream.Runtime.Tests/Plugins/CampaignAutomationSurfaceTests.cs docs/plugin-api.md
git commit -m "feat(plugins): cast at a target without selecting it (contract 0.1.26-fork.6)"
```

---

### Task 3: Full suite, merge to main and rynth, push

**Goal:** The branch is merged into `main` and `main` into `rynth`, both pushed, with the full suite inside the baseline on each.

**Files:**
- No source changes (merge commits only; conflict resolution if any).

**Acceptance Criteria:**
- [ ] Full OpenAC suite on `feat/cast-without-selecting`: failures ⊆ `presplit-failed.txt`.
- [ ] `main` has a `--no-ff` merge of the branch; `rynth` has a merge of `main`; both pushed to `origin`.
- [ ] `rynth` builds, and its `RuntimeSpellCastStateTests` + `CampaignAutomationSurfaceTests` pass.

**Verify:** `git log --oneline -1 origin/main origin/rynth` shows the two merge commits; `python3 $HOME/code/OpenAC/split-baseline/failed-tests.py …` reports no new failures.

**Steps:**

- [ ] **Step 1:** In `~/code/OpenAC/OpenAC` on the branch, run the full suite the way plans 1-3 did (`dotnet test AcDream.slnx` with the env above, results to `/tmp/acdt/plan4`), collect failures with `failed-tests.py`, and compare with `presplit-failed.txt`. Any new failure: stop and debug (superpowers-extended-cc:systematic-debugging) before merging. Revert lock churn.
- [ ] **Step 2:** `git switch main && git merge --no-ff feat/cast-without-selecting -m "Merge branch 'feat/cast-without-selecting': cast at a target without selecting it"`.
- [ ] **Step 3:** In `~/code/OpenAC/OpenAC-gateway-rynth` (on `rynth`): `git merge main`. Resolve any conflict as "rynth content + main's change". Build `AcDream.slnx` and run `dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~RuntimeSpellCastStateTests|FullyQualifiedName~CampaignAutomationSurfaceTests"`. Revert lock churn.
- [ ] **Step 4:** Push: `git -C ~/code/OpenAC/OpenAC push origin main feat/cast-without-selecting` and `git -C ~/code/OpenAC/OpenAC-gateway-rynth push origin rynth`.

---

### Task 4: Aethrix casts without selecting

**Goal:** Aethrix builds against contract `0.1.26-fork.6` and `MagicSession.Send` uses `EvaluateGateWithoutSelecting` / `RequestCastWithoutSelecting`.

**Files (repo `~/code/OpenAC/OpenAC-Plugins-Aethrix`):**
- Modify: `Directory.Build.props:37` (`PluginApiPackageVersion`)
- Modify: `src/Aethrix/Core/Magic/MagicSession.cs:261,273`
- Modify: `tests/Aethrix.Tests/Fakes/FakeAutomation.cs` (magic fake, ~line 656-690)
- Test: the test file that covers `MagicSession` (find it with `grep -rln "MagicSession" tests/Aethrix.Tests`; if none fits, `tests/Aethrix.Tests/Combat/MagicFacetTests.cs`)
- Modify: `docs/api-gaps.md` (new resolved entry `## 79.`)

**Acceptance Criteria:**
- [ ] `PluginApiPackageVersion` is `0.1.26-fork.6` and `tools/pack-contract.sh` packs it from `../OpenAC` on `main`.
- [ ] `MagicSession.Send` calls only the `WithoutSelecting` members; `grep -n "EvaluateGate(\|RequestCast(" src/Aethrix/Core/Magic/MagicSession.cs` finds nothing.
- [ ] The fake counts calls to the selecting overloads (`SelectingCalls`); a test drives a cast through the facet and asserts `SelectingCalls == 0` and the cast reached `Casts`.
- [ ] Full Aethrix suite passes.

**Verify:** `dotnet test` in the Aethrix repo → all pass

**Steps:**

- [ ] **Step 1: Fake.** In `FakeAutomation.cs`'s magic fake, add the counter and the two members, and count the selecting ones:

```csharp
    /// <summary>Calls to the overloads that select the target; Aethrix should make none.</summary>
    public int SelectingCalls { get; private set; }

    public PluginCastGate EvaluateGate(uint spellId, uint targetObjectId)
    {
        SelectingCalls++;
        return EvaluateGateWithoutSelecting(spellId, targetObjectId);
    }

    public PluginCastGate EvaluateGateWithoutSelecting(uint spellId, uint targetObjectId) =>
        RefusedTargets.Contains(targetObjectId) ? PluginCastGate.Refused : Gate;

    public PluginCastRequestResult RequestCast(uint spellId, uint targetObjectId)
    {
        SelectingCalls++;
        return RequestCastWithoutSelecting(spellId, targetObjectId);
    }

    public PluginCastRequestResult RequestCastWithoutSelecting(uint spellId, uint targetObjectId)
    {
        if (RefusedTargets.Contains(targetObjectId))
            return PluginCastRequestResult.IncompatibleTarget;
        if (NextResult == PluginCastRequestResult.Sent)
        {
            Casts.Add((spellId, targetObjectId));
            IsCasting = !CastingEndsBeforeAnswer;
        }

        return NextResult;
    }
```

replacing the existing `EvaluateGate(uint, uint)` and `RequestCast(uint, uint)`. `RequestCast(uint spellId)` and `Cast(uint, uint)` keep calling `RequestCast(spellId, …)` as now.

- [ ] **Step 2: Failing test.** In the chosen test file, add a test next to an existing cast test that reuses its setup (for `MagicFacetTests`, copy the arrangement of `A_better_target_mid_cast_never_aborts_the_cast` up to its first `_world.NextCast()`), then:

```csharp
        Assert.NotEmpty(_world.Magic.Casts);
        Assert.Equal(0, _world.Magic.SelectingCalls);
```

Name it `Casts_never_move_the_selection`. Run `dotnet test --filter "FullyQualifiedName~Casts_never_move_the_selection"` — expect FAIL (`SelectingCalls` > 0) once the contract bump below compiles. Bump `PluginApiPackageVersion` to `0.1.26-fork.6` in `Directory.Build.props` and run `tools/pack-contract.sh` first if the build restores from `packages-local`.

- [ ] **Step 3: Switch `MagicSession.Send`:** line 261 `magic.EvaluateGateWithoutSelecting(spell.SpellId, target)`; line 273 `magic.RequestCastWithoutSelecting(spell.SpellId, target)`.

- [ ] **Step 4:** Run the new test (PASS), then the full suite: `dotnet test` → all pass. Any other test that relied on the selecting calls is a fake-usage fix, not a behaviour change; check each before editing.

- [ ] **Step 5: api-gaps.** Append to `docs/api-gaps.md`:

```markdown
## 79. Casting at a target selects it (resolved)

**Resolved** on the OpenAC fork's `main` (contract `0.1.26-fork.6`):
`IMagicCommands.EvaluateGateWithoutSelecting` and `RequestCastWithoutSelecting` check and send a cast at an object
without reading or changing the player's selection. `MagicSession.Send` uses them. Not proposed upstream yet.

**Affects:** Combat and Buffs (every cast goes through `MagicSession`).

**What was missing:** `EvaluateGate(spell, target)` and `RequestCast(spell, target)` select the target first, so
every cast moved the player's selection to the creature (or to the player for a self buff).
```

- [ ] **Step 6: Commit** (Aethrix `main`; do not push until the user says so):

```bash
git add Directory.Build.props src/Aethrix/Core/Magic/MagicSession.cs tests/Aethrix.Tests docs/api-gaps.md
git commit -m "api-gaps #79: cast without selecting the target (contract 0.1.26-fork.6)"
```

---

### Task 5: Live check (user)

**Goal:** The user confirms in the client that Aethrix combat casts at creatures while another object stays selected.

**Files:** none

**Acceptance Criteria:**
- [ ] The user reports: with a non-target object selected, Aethrix combat casts land on the creature and the selection does not change.

**Verify:** the user's report.

**Steps:**

- [ ] Ask the user whether they are available. Only then rebuild the dev install from the rynth worktree (to keep Rynth) with the new Aethrix packed in, per the local test env (local ACE `10.10.20.20`, never Dreamweave), and hand over. Record the result in memory.
