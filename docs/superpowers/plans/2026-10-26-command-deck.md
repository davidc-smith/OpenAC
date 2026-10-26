# Command Deck Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A "Modern HUD" setting that replaces the retail vitals, side-by-side vitals, combat and jump-powerbar windows with one game-style Command Deck window.

**Architecture:** A pure `CommandDeckModel` turns a per-frame `CommandDeckInput` snapshot into a `CommandDeckView`; `UiCommandDeck` (a code-built `UiElement`) draws that view with the existing rounded-rect/gradient/ellipse helpers and SVG coverage icons, and forwards clicks to `ICommandDeckActions`. `ModernHudSwitch` gates the four retail windows' show requests and owns the deck's visibility. Everything is wired in `RetailUiRuntime` and `InteractionRetainedUiComposition`.

**Tech Stack:** C# / .NET (`~/.dotnet` SDK), xUnit, OpenAC retained UI (`AcDream.App/UI`).

**Spec:** `docs/superpowers/specs/2026-10-26-command-deck-design.md` (branch `docs/command-deck-spec`). Read it first.

## Global Constraints

- **Fork-only.** Work on branch `feat/command-deck` cut from fork `main` (`fd2a66f1` or later). No upstream PR. No plugin-contract change, no `-fork.n` bump.
- **Setting off = today's behaviour.** With `modernHud=false` no retail window, controller or code path may behave differently. Every change outside `UI/Hud/` must keep its current behaviour when the switch is inactive.
- **Fixed geometry.** The deck is `CommandDeckLayout.Width` × `CommandDeckLayout.Height` (420 × 150 points) in every mode. Nothing in the deck may change size or position when the combat mode changes.
- **Context row by mode:** Melee/Missile = power track + H/M/L; Magic = `CASTING` + spell text + RECAST; Peace (NonCombat/Undef) = `RECALL` + 5 studs. Missile labels the track `ACCURACY`, melee `POWER`.
- **Studs** (repeat attacks, auto-target, keep target in view) are drawn and hit-testable only in combat (any mode other than NonCombat/Undef).
- **Attack release contract:** every `PressAttack` from the deck is matched by exactly one `ReleaseAttack` (mouse-up anywhere, capture lost, row leaves Power, deck hidden, deck detached).
- **Damage trail:** hold 0.3 s, shrink over 0.6 s; first sample and session change seed without animation; rises have no trail.
- **Tests:** run through `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC` (narrow with `MAC_TEST_FILTER='FullyQualifiedName~X'`, never a second `--filter`). Never run known-failing tests directly.
- **Live client:** never launch one until the user says they are available. Local ACE (10.10.20.20, `~/OpenAC-dev/dev-client.env`) only, never Dreamweave.
- Commits end with `Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258`.

**User decisions (already made):**
- "I only really like 3 … the other 2 look too much like plugin views as opposed to a game window" → game-window look; final pick was layout D1 (orb + three thick labelled bars).
- No melee/missile/magic/peace chips: "you can only ever be in peace or combat mode — whether you are melee, missile or magic depends on what you are holding."
- "I think it will be too disconcerting to have the control height adjust when switching" → fixed height; context row filled per mode.
- Peace row = recall buttons; magic row = "just recast and spell name that is being cast (windup not required)".
- Spell bar: "if we just create what is there right now, spell casting would be broken, but that is ok" → Modern HUD on = no retail spell bar until a separate spellcasting component is designed.
- Approach: client-built `UiElement` window with a model/view split (option 1).
- Defaults accepted: movable/saved window, bottom-centre; live toggle in Options → Config; low-health pulse below 25%; tooltips; keybinds unchanged.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.Runtime/Gameplay/RuntimeSpellCastState.cs` (modify) | add `HasPendingCast` |
| `src/AcDream.App/UI/Layout/VitalsSideBySideController.cs` (modify) | show/hide callbacks + `SyncVisibility()` |
| `src/AcDream.UI.Abstractions/Panels/Settings/MiscSettings.cs`, `SettingsStore.cs` (modify) | `ModernHud` setting |
| `src/AcDream.App/UI/Layout/ConfigOptionsPageController.cs` (modify) | `ModernHudBindings` + "Modern HUD" row |
| `src/AcDream.App/UI/Hud/VitalTrail.cs` | damage-trail state machine for one vital |
| `src/AcDream.App/UI/Hud/CommandDeckModel.cs` | input/view records + model |
| `src/AcDream.App/UI/Hud/CommandDeckLayout.cs` | fixed geometry + hit zones (pure) |
| `src/AcDream.App/UI/Hud/ICommandDeckActions.cs` | the deck's outgoing actions |
| `src/AcDream.App/UI/Hud/UiCommandDeck.cs` | drawing, input, release contract |
| `src/AcDream.App/UI/Hud/EmbeddedSvgSpriteCache.cs` | embedded SVG → coverage textures |
| `assets/hud/*.svg` + `src/AcDream.App/AcDream.App.csproj` (modify) | icons |
| `src/AcDream.App/UI/Hud/ModernHudSwitch.cs` | gating + deck visibility |
| `src/AcDream.App/UI/WindowNames.cs`, `RetailUiRuntime.cs`, `Composition/InteractionRetainedUiComposition.cs`, `Composition/InteractionUiRuntimeSources.cs` (modify) | wiring |

Tests live in `tests/AcDream.Runtime.Tests/Gameplay/`, `tests/AcDream.App.Tests/UI/Layout/`, `tests/AcDream.App.Tests/UI/Hud/` (new folder), `tests/AcDream.UI.Abstractions.Tests/Panels/Settings/`.

---

### Task 0: Branch

**Goal:** A feature branch from fork main with the spec and plan available.

**Files:** none (git only)

**Acceptance Criteria:**
- [ ] Branch `feat/command-deck` exists, based on fork `main`, checked out in a worktree at `~/code/OpenAC/OpenAC-command-deck`.
- [ ] The spec and plan files are present in that worktree (cherry-picked from `docs/command-deck-spec`).

**Verify:** `git -C ~/code/OpenAC/OpenAC-command-deck log --oneline -3` → shows the spec/plan commits on top of main.

**Steps:**

- [ ] **Step 1: Create the worktree**

```bash
cd ~/code/OpenAC/OpenAC
git worktree add -b feat/command-deck ~/code/OpenAC/OpenAC-command-deck main
cd ~/code/OpenAC/OpenAC-command-deck
git cherry-pick $(git rev-list --reverse main..docs/command-deck-spec)
```

- [ ] **Step 2: Build once**

```bash
export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet
dotnet build AcDream.slnx -c Debug 2>&1 | tail -3
```
Expected: `Build succeeded.`

All later paths are relative to `~/code/OpenAC/OpenAC-command-deck`; run tests with `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-command-deck`.

---

### Task 1: `RuntimeSpellCastState.HasPendingCast`

**Goal:** Expose whether any cast receipt is outstanding, independent of `PendingSpellId`.

**Files:**
- Modify: `src/AcDream.Runtime/Gameplay/RuntimeSpellCastState.cs` (next to `PendingSpellId`, ~line 52)
- Test: `tests/AcDream.Runtime.Tests/Gameplay/RuntimeSpellCastStateTests.cs`

**Acceptance Criteria:**
- [ ] `HasPendingCast` is true after a sent cast and false once every receipt completes.
- [ ] `HasPendingCast` stays true across two overlapping casts of different spells while `PendingSpellId` is null.
- [ ] `Reset()` makes it false.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~RuntimeSpellCastStateTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-command-deck` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** (append inside `RuntimeSpellCastStateTests`, reusing its `MakeBook`/`Create`/`FakeOperations` helpers)

```csharp
    [Fact]
    public void HasPendingCast_TracksOutstandingReceipts()
    {
        Spellbook book = MakeBook(flags: 0, untargeted: true, targetMask: 0);
        RuntimeSpellCastState state = Create(book, new FakeOperations { LocalPlayerId = 42u });

        Assert.False(state.HasPendingCast);
        Assert.Equal(CastRequestResult.Sent, state.Cast(1));
        Assert.True(state.HasPendingCast);
        Assert.True(state.CompleteUse(0u));
        Assert.False(state.HasPendingCast);
    }

    [Fact]
    public void HasPendingCast_StaysTrueForOverlappingDifferentSpells()
    {
        Spellbook book = MakeBook(
            flags: 0, untargeted: true, targetMask: 0,
            includeSecondSpell: true);
        RuntimeSpellCastState state = Create(book, new FakeOperations { LocalPlayerId = 42u });

        Assert.Equal(CastRequestResult.Sent, state.Cast(1));
        Assert.Equal(CastRequestResult.Sent, state.Cast(2));
        Assert.Null(state.PendingSpellId);
        Assert.True(state.HasPendingCast);

        state.CompleteUse(0u);
        Assert.True(state.HasPendingCast);
        state.CompleteUse(0u);
        Assert.False(state.HasPendingCast);
    }

    [Fact]
    public void HasPendingCast_ClearedByReset()
    {
        Spellbook book = MakeBook(flags: 0, untargeted: true, targetMask: 0);
        RuntimeSpellCastState state = Create(book, new FakeOperations { LocalPlayerId = 42u });
        state.Cast(1);

        state.Reset();

        Assert.False(state.HasPendingCast);
    }
```

- [ ] **Step 2: Run, expect compile failure** (`HasPendingCast` does not exist).

- [ ] **Step 3: Implement** — add after `public uint? PendingTargetId { get; private set; }`:

```csharp
    /// <summary>Whether any cast is still waiting for its server receipt. Unlike
    /// <see cref="PendingSpellId"/>, which is cleared when overlapping casts differ,
    /// this stays true until every receipt has come back.</summary>
    public bool HasPendingCast => _outstandingCastReceipts > 0;
```

- [ ] **Step 4: Run the filter again** → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Runtime/Gameplay/RuntimeSpellCastState.cs tests/AcDream.Runtime.Tests/Gameplay/RuntimeSpellCastStateTests.cs
git commit -m "feat(magic): RuntimeSpellCastState.HasPendingCast

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 2: `VitalsSideBySideController` callbacks + `SyncVisibility`

**Goal:** Let the side-by-side vitals controller be gated, without changing its behaviour.

**Files:**
- Modify: `src/AcDream.App/UI/Layout/VitalsSideBySideController.cs`
- Test: `tests/AcDream.App.Tests/UI/Layout/VitalsSideBySideControllerTests.cs`

**Acceptance Criteria:**
- [ ] Existing `UiRoot` constructor still works and all existing tests pass unchanged.
- [ ] New constructor takes `Func<string,bool> show`, `Func<string,bool> hide`; the controller only uses those.
- [ ] `SyncVisibility()` clears the cached bit and re-applies the current option even when it has not changed.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~VitalsSideBySideControllerTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-command-deck` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to the test class)

```csharp
    [Fact]
    public void CallbackConstructor_RoutesThroughTheCallbacks()
    {
        var calls = new List<string>();
        var controller = new VitalsSideBySideController(
            show: name => { calls.Add("show " + name); return true; },
            hide: name => { calls.Add("hide " + name); return true; },
            sideBySideVitals: () => true,
            stackedWindow: WindowNames.Vitals,
            sideWindow: WindowNames.SideVitals);

        controller.Tick();

        Assert.Equal(
            ["hide " + WindowNames.Vitals, "show " + WindowNames.SideVitals],
            calls);
    }

    [Fact]
    public void SyncVisibility_ReappliesTheUnchangedBit()
    {
        var calls = new List<string>();
        var controller = new VitalsSideBySideController(
            show: name => { calls.Add("show " + name); return true; },
            hide: name => { calls.Add("hide " + name); return true; },
            sideBySideVitals: () => false,
            stackedWindow: WindowNames.Vitals,
            sideWindow: WindowNames.SideVitals);
        controller.Tick();
        calls.Clear();

        controller.Tick();
        Assert.Empty(calls);

        controller.SyncVisibility();
        Assert.Equal(
            ["show " + WindowNames.Vitals, "hide " + WindowNames.SideVitals],
            calls);
    }
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement** — replace the class body with:

```csharp
public sealed class VitalsSideBySideController
{
    private readonly Func<string, bool> _show;
    private readonly Func<string, bool> _hide;
    private readonly Func<bool> _sideBySideVitals;
    private readonly string _stackedWindow;
    private readonly string _sideWindow;
    private bool? _applied;

    public VitalsSideBySideController(
        UiRoot root,
        Func<bool> sideBySideVitals,
        string stackedWindow,
        string sideWindow)
        : this(
            (root ?? throw new ArgumentNullException(nameof(root))).ShowWindow,
            root.HideWindow,
            sideBySideVitals,
            stackedWindow,
            sideWindow)
    {
    }

    /// <summary>Shows and hides through <paramref name="show"/> and
    /// <paramref name="hide"/>, so a caller can gate the windows (Modern HUD).</summary>
    public VitalsSideBySideController(
        Func<string, bool> show,
        Func<string, bool> hide,
        Func<bool> sideBySideVitals,
        string stackedWindow,
        string sideWindow)
    {
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _hide = hide ?? throw new ArgumentNullException(nameof(hide));
        _sideBySideVitals = sideBySideVitals
            ?? throw new ArgumentNullException(nameof(sideBySideVitals));
        _stackedWindow = stackedWindow;
        _sideWindow = sideWindow;
    }

    /// <summary>The last applied bit (null before the first apply). Exposed for tests.</summary>
    public bool? Applied => _applied;

    public void Tick()
    {
        bool sideBySide = _sideBySideVitals();
        if (_applied == sideBySide) return;
        _applied = sideBySide;

        if (sideBySide)
        {
            _hide(_stackedWindow);
            _show(_sideWindow);
        }
        else
        {
            _show(_stackedWindow);
            _hide(_sideWindow);
        }
    }

    /// <summary>Forgets the applied bit and applies the current one again.</summary>
    public void SyncVisibility()
    {
        _applied = null;
        Tick();
    }
}
```

- [ ] **Step 4: Run the filter** → PASS (old and new tests).

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Layout/VitalsSideBySideController.cs tests/AcDream.App.Tests/UI/Layout/VitalsSideBySideControllerTests.cs
git commit -m "refactor(vitals): side-by-side controller takes show/hide callbacks, adds SyncVisibility

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 3: `ModernHud` setting

**Goal:** Persist the Modern HUD on/off flag in `misc` settings.

**Files:**
- Modify: `src/AcDream.UI.Abstractions/Panels/Settings/MiscSettings.cs`
- Modify: `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs` (`LoadMisc` ~line 303, `BuildMiscObject` ~line 1001)
- Test: `tests/AcDream.UI.Abstractions.Tests/Panels/Settings/SettingsStoreTests.cs`

**Acceptance Criteria:**
- [ ] `MiscSettings.Default.ModernHud` is false.
- [ ] `SaveMisc(... ModernHud = true)` then `LoadMisc()` returns true; the JSON key is `modernHud`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SettingsStoreTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-command-deck` → all pass.

**Steps:**

- [ ] **Step 1: Failing test** (next to `Extended_levels_are_off_by_default_and_round_trip`)

```csharp
    [Fact]
    public void Modern_hud_is_off_by_default_and_round_trips()
    {
        var store = new SettingsStore(_tempPath);
        Assert.False(MiscSettings.Default.ModernHud);

        store.SaveMisc(MiscSettings.Default with { ModernHud = true });

        Assert.True(store.LoadMisc().ModernHud);
        Assert.Contains("\"modernHud\"", File.ReadAllText(_tempPath));
    }
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement**

`MiscSettings.cs`: add a doc line and parameter:

```csharp
/// <param name="ModernHud">
/// Replace the retail vitals, combat and jump windows with the Command Deck.
/// </param>
public sealed record MiscSettings(
    bool TooltipEnable,
    float TooltipDelaySeconds,
    bool ExtendedLevels = false,
    bool ModernHud = false)
```

`SettingsStore.LoadMisc`: extend the constructor call:

```csharp
                ExtendedLevels:      ReadBool (misc, "extendedLevels",      d.ExtendedLevels),
                ModernHud:           ReadBool (misc, "modernHud",           d.ModernHud));
```

`BuildMiscObject`: add (keys are ordinal-sorted by the dictionary):

```csharp
            ["modernHud"]           = m.ModernHud,
```

- [ ] **Step 4: Run the filter** → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.UI.Abstractions/Panels/Settings tests/AcDream.UI.Abstractions.Tests/Panels/Settings/SettingsStoreTests.cs
git commit -m "feat(settings): misc ModernHud flag

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 4: "Modern HUD" row on Options → Config

**Goal:** A toggle row that reads and writes through `ModernHudBindings`.

**Files:**
- Modify: `src/AcDream.App/UI/Layout/ConfigOptionsPageController.cs` (bindings ~line 126–134, row after the Extended Levels row ~line 1360)
- Test: `tests/AcDream.App.Tests/UI/Layout/ConfigOptionsPageControllerTests.cs`

**Acceptance Criteria:**
- [ ] Without `ModernHud` bindings the row count is unchanged (existing tests pass).
- [ ] With them, one extra `BoolOptionRow` is appended last, default false, and `SetCurrentValue(true)` calls `Save(true)`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ConfigOptionsPageControllerTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-command-deck` → all pass.

**Steps:**

- [ ] **Step 1: Failing test** (copy of the Extended Levels test with the new binding)

```csharp
    [Fact]
    public void ModernHudBindings_append_one_toggle_that_writes_through_them()
    {
        ImportedLayout layout = FixtureLoader.LoadOptionsPanelHost();
        OptionsPanelController controller = OptionsPanelController.Bind(
            layout,
            new OptionsPanelController.Callbacks(
                Toggle: () => { },
                RequestExitToCharacterSelection: () => { },
                ExitGame: () => { },
                UseMouseTurningSettings: () => { },
                DisplaySystemMessage: _ => { }))!;
        bool modern = false;
        var saves = new List<bool>();
        var fake = new FakeBindings();

        bool bound = ConfigOptionsPageController.Bind(
            layout,
            controller.ConfigPage,
            MakeTemplateResolver(),
            (_, _) => null,
            fake.ToBindings() with
            {
                ModernHud = new ConfigOptionsPageController.ModernHudBindings(
                    Load: () => modern,
                    Save: value => { modern = value; saves.Add(value); }),
            });

        Assert.True(bound);
        Assert.Equal(40, controller.ConfigPage.Rows.Count);
        var row = Assert.IsType<BoolOptionRow>(controller.ConfigPage.Rows[^1]);
        Assert.False(row.DefaultValue);
        Assert.False(row.Current);

        row.SetCurrentValue(true);

        Assert.True(modern);
        Assert.Equal([true], saves);
    }
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement**

In the bindings type, next to `ExtendedLevels`:

```csharp
        /// <summary>The Modern HUD row's switch; the row is left out without it.</summary>
        public ModernHudBindings? ModernHud { get; init; }
```

Next to `ExtendedLevelsBindings`:

```csharp
    public sealed record ModernHudBindings(Func<bool> Load, Action<bool> Save);
```

After the Extended Levels block:

```csharp
        // Modern HUD: the Command Deck in place of the retail vitals, combat
        // and jump windows. Not a retail option, so explicit text.
        if (bindings.ModernHud is { } modernHud)
        {
            BuildExplicitToggleRow(
                listBox,
                "Modern HUD",
                MiscSettings.Default.ModernHud,
                page,
                read: modernHud.Load,
                apply: value =>
                {
                    modernHud.Save(value);
                    return true;
                },
                isCurrent: static () => true,
                tooltip:
                    "Show the Command Deck in place of the retail vitals, "
                    + "combat and jump bars. The retail spell bar is not "
                    + "shown while this is on.");
        }
```

- [ ] **Step 4: Run the filter** → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Layout/ConfigOptionsPageController.cs tests/AcDream.App.Tests/UI/Layout/ConfigOptionsPageControllerTests.cs
git commit -m "feat(options): Modern HUD toggle on the Config page

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 5: `VitalTrail`

**Goal:** The damage-trail state machine for one vital, pure and time-driven.

**Files:**
- Create: `src/AcDream.App/UI/Hud/VitalTrail.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/VitalTrailTests.cs`

**Acceptance Criteria:**
- [ ] First sample: trail == fill (no animation).
- [ ] Drop: trail holds the old value for 0.3 s, then shrinks linearly to the new fill over 0.6 s.
- [ ] Second drop during an active trail keeps the currently drawn trail edge and restarts the hold.
- [ ] Rise: no trail; a rise above the drawn trail cuts it.
- [ ] `null` fill clears the trail and the next real value seeds again; `Reset()` seeds again.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~VitalTrailTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-command-deck` → all pass.

**Steps:**

- [ ] **Step 1: Failing tests**

```csharp
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class VitalTrailTests
{
    [Fact]
    public void FirstSample_SeedsWithoutAnimation()
    {
        var trail = new VitalTrail();
        Assert.Equal(0.6f, trail.Update(0.6f, now: 10.0));
    }

    [Fact]
    public void Drop_HoldsThenShrinks()
    {
        var trail = new VitalTrail();
        trail.Update(1.0f, 0.0);

        Assert.Equal(1.0f, trail.Update(0.5f, 1.0));
        Assert.Equal(1.0f, trail.Update(0.5f, 1.29));
        Assert.Equal(0.75f, trail.Update(0.5f, 1.6), 3);
        Assert.Equal(0.5f, trail.Update(0.5f, 1.9));
        Assert.Equal(0.5f, trail.Update(0.5f, 5.0));
    }

    [Fact]
    public void DropDuringTrail_KeepsDrawnEdgeAndRestartsHold()
    {
        var trail = new VitalTrail();
        trail.Update(1.0f, 0.0);
        trail.Update(0.6f, 1.0);                       // trail held at 1.0 until 1.3
        float drawn = trail.Update(0.6f, 1.6);         // halfway: 0.8
        Assert.Equal(0.8f, drawn, 3);

        Assert.Equal(0.8f, trail.Update(0.4f, 1.6), 3);
        Assert.Equal(0.8f, trail.Update(0.4f, 1.85), 3); // held again
        Assert.Equal(0.4f, trail.Update(0.4f, 2.6));
    }

    [Fact]
    public void Rise_HasNoTrail()
    {
        var trail = new VitalTrail();
        trail.Update(0.5f, 0.0);
        Assert.Equal(0.7f, trail.Update(0.7f, 1.0));
    }

    [Fact]
    public void RiseAboveTrail_CutsIt()
    {
        var trail = new VitalTrail();
        trail.Update(0.8f, 0.0);
        trail.Update(0.4f, 1.0);                       // trail held at 0.8
        Assert.Equal(0.9f, trail.Update(0.9f, 1.1));
        Assert.Equal(0.9f, trail.Update(0.9f, 1.5));
    }

    [Fact]
    public void Null_ClearsAndNextValueSeeds()
    {
        var trail = new VitalTrail();
        trail.Update(1.0f, 0.0);
        trail.Update(0.5f, 1.0);
        Assert.Equal(0f, trail.Update(null, 1.1));
        Assert.Equal(0.3f, trail.Update(0.3f, 1.2));
    }

    [Fact]
    public void Reset_Seeds()
    {
        var trail = new VitalTrail();
        trail.Update(1.0f, 0.0);
        trail.Reset();
        Assert.Equal(0.2f, trail.Update(0.2f, 0.1));
    }
}
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement**

```csharp
namespace AcDream.App.UI.Hud;

/// <summary>
/// The pale "damage taken" segment behind one vital bar. After a drop the
/// trail holds the old value for <see cref="HoldSeconds"/>, then shrinks to
/// the new value over <see cref="ShrinkSeconds"/>. Rises have no trail. The
/// first value (and the first after <see cref="Reset"/> or a null) seeds both
/// ends, so nothing slides at login.
/// </summary>
internal sealed class VitalTrail
{
    public const double HoldSeconds = 0.3;
    public const double ShrinkSeconds = 0.6;

    private float? _fill;
    private float _from;
    private double _holdUntil;

    public void Reset() => _fill = null;

    /// <summary>The trail fraction to draw for <paramref name="fill"/> at <paramref name="now"/> (seconds); never below the fill, 0 for an unknown vital.</summary>
    public float Update(float? fill, double now)
    {
        if (fill is not float value)
        {
            _fill = null;
            return 0f;
        }

        if (_fill is not float previous)
        {
            Seed(value, now);
            return value;
        }

        if (value < previous)
        {
            _from = Math.Max(DrawnAt(previous, now), previous);
            _holdUntil = now + HoldSeconds;
        }
        else if (value > previous && DrawnAt(previous, now) <= value)
        {
            Seed(value, now);
        }

        _fill = value;
        return DrawnAt(value, now);
    }

    private void Seed(float value, double now)
    {
        _fill = value;
        _from = value;
        _holdUntil = now - ShrinkSeconds;
    }

    private float DrawnAt(float fill, double now)
    {
        if (now < _holdUntil) return Math.Max(_from, fill);
        double t = (now - _holdUntil) / ShrinkSeconds;
        if (t >= 1.0) return fill;
        return Math.Max(fill, (float)(_from + (fill - _from) * t));
    }
}
```

Note for `DropDuringTrail`: at t=1.6 after a drop at 1.0 the trail is `1.0 + (0.6−1.0)·0.5 = 0.8`; the second drop keeps 0.8 as `_from`.

- [ ] **Step 4: Run the filter** → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud/VitalTrail.cs tests/AcDream.App.Tests/UI/Hud/VitalTrailTests.cs
git commit -m "feat(hud): VitalTrail damage-trail state

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 6: `CommandDeckModel`

**Goal:** Turn a `CommandDeckInput` snapshot into a `CommandDeckView`.

**Files:**
- Create: `src/AcDream.App/UI/Hud/CommandDeckModel.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/CommandDeckModelTests.cs`

**Acceptance Criteria:**
- [ ] Row per mode: Melee/Missile → `Power`, Magic → `Casting`, NonCombat/Undef → `Recall`; `ShowStuds` false only in peace.
- [ ] Power label `POWER` (melee) / `ACCURACY` (missile).
- [ ] Vital text `c / m` when both known; `61%` when health max unknown; `–` with `Known=false`, fill 0, trail 0 when percent is null.
- [ ] `LowHealth` true below 0.25 health percent (strictly).
- [ ] Casting text precedence: pending name (lit) → `Casting…` (lit) → last name (dim) → `No spell cast yet` (dim); `RecastEnabled` = `!HasPendingCast && LastSpellName != null`.
- [ ] Jump fill = `Power` only while `IsCharging`, else 0.
- [ ] A change of `SessionKey` reseeds all trails.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~CommandDeckModelTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-command-deck` → all pass.

**Steps:**

- [ ] **Step 1: Failing tests**

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Combat;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.Tests.UI.Hud;

public sealed class CommandDeckModelTests
{
    private static CommandDeckInput Input(
        CombatMode mode = CombatMode.Melee,
        DeckVitalInput? health = null,
        bool pending = false, string? pendingName = null, string? lastName = null,
        JumpChargeSnapshot jump = default,
        long session = 1) => new(
            Health: health ?? new DeckVitalInput(0.61f, 412, 675),
            Stamina: new DeckVitalInput(0.88f, 390, 443),
            Mana: new DeckVitalInput(0.45f, 180, 400),
            Mode: mode,
            DesiredPower: 0.75f,
            PowerBarLevel: 0.4f,
            RequestedHeight: AttackHeight.Medium,
            HasPendingCast: pending,
            PendingSpellName: pendingName,
            LastSpellName: lastName,
            Jump: jump,
            RepeatAttacks: true,
            AutoTarget: false,
            KeepInView: false,
            SessionKey: session);

    [Theory]
    [InlineData(CombatMode.Melee, DeckContextRow.Power, true)]
    [InlineData(CombatMode.Missile, DeckContextRow.Power, true)]
    [InlineData(CombatMode.Magic, DeckContextRow.Casting, true)]
    [InlineData(CombatMode.NonCombat, DeckContextRow.Recall, false)]
    [InlineData(CombatMode.Undef, DeckContextRow.Recall, false)]
    public void RowAndStudsFollowTheMode(CombatMode mode, DeckContextRow row, bool studs)
    {
        CommandDeckView view = new CommandDeckModel().Update(Input(mode), 0.0);
        Assert.Equal(row, view.Row);
        Assert.Equal(studs, view.ShowStuds);
    }

    [Fact]
    public void PowerLabelIsAccuracyForMissile()
    {
        var model = new CommandDeckModel();
        Assert.Equal("POWER", model.Update(Input(CombatMode.Melee), 0).PowerLabel);
        Assert.Equal("ACCURACY", model.Update(Input(CombatMode.Missile), 0).PowerLabel);
    }

    [Fact]
    public void VitalText()
    {
        var model = new CommandDeckModel();
        CommandDeckView known = model.Update(Input(), 0);
        Assert.Equal("412 / 675", known.Health.Text);

        CommandDeckView percentOnly = model.Update(
            Input(health: new DeckVitalInput(0.61f, null, null)), 0);
        Assert.Equal("61%", percentOnly.Health.Text);

        CommandDeckView unknown = new CommandDeckModel().Update(
            Input(health: new DeckVitalInput(null, null, null)), 0);
        Assert.False(unknown.Health.Known);
        Assert.Equal("–", unknown.Health.Text);
        Assert.Equal(0f, unknown.Health.Fill);
        Assert.Equal(0f, unknown.Health.Trail);
    }

    [Fact]
    public void LowHealthBelowAQuarter()
    {
        var model = new CommandDeckModel();
        Assert.False(model.Update(Input(health: new DeckVitalInput(0.25f, 25, 100)), 0).LowHealth);
        Assert.True(model.Update(Input(health: new DeckVitalInput(0.24f, 24, 100)), 0).LowHealth);
    }

    [Fact]
    public void CastingTextPrecedenceAndRecast()
    {
        var model = new CommandDeckModel();
        CommandDeckView pendingNamed = model.Update(
            Input(CombatMode.Magic, pending: true, pendingName: "Flame Bolt I", lastName: "Flame Bolt I"), 0);
        Assert.Equal("Flame Bolt I", pendingNamed.CastingText);
        Assert.True(pendingNamed.CastingLit);
        Assert.False(pendingNamed.RecastEnabled);

        CommandDeckView overlapping = model.Update(
            Input(CombatMode.Magic, pending: true, pendingName: null, lastName: "Frost Bolt I"), 0);
        Assert.Equal("Casting…", overlapping.CastingText);
        Assert.True(overlapping.CastingLit);
        Assert.False(overlapping.RecastEnabled);

        CommandDeckView idle = model.Update(
            Input(CombatMode.Magic, lastName: "Frost Bolt I"), 0);
        Assert.Equal("Frost Bolt I", idle.CastingText);
        Assert.False(idle.CastingLit);
        Assert.True(idle.RecastEnabled);

        CommandDeckView none = model.Update(Input(CombatMode.Magic), 0);
        Assert.Equal("No spell cast yet", none.CastingText);
        Assert.False(none.RecastEnabled);
    }

    [Fact]
    public void JumpFillOnlyWhileCharging()
    {
        var model = new CommandDeckModel();
        Assert.Equal(0f, model.Update(Input(jump: new JumpChargeSnapshot(false, 0.7f)), 0).JumpFill);
        CommandDeckView charging = model.Update(Input(jump: new JumpChargeSnapshot(true, 0.7f)), 0);
        Assert.Equal(0.7f, charging.JumpFill);
        Assert.True(charging.JumpCharging);
    }

    [Fact]
    public void SessionChangeReseedsTrails()
    {
        var model = new CommandDeckModel();
        model.Update(Input(health: new DeckVitalInput(1.0f, 100, 100)), 0.0);
        CommandDeckView dropped = model.Update(Input(health: new DeckVitalInput(0.5f, 50, 100)), 1.0);
        Assert.Equal(1.0f, dropped.Health.Trail);

        CommandDeckView newSession = model.Update(
            Input(health: new DeckVitalInput(0.3f, 30, 100), session: 2), 1.05);
        Assert.Equal(0.3f, newSession.Health.Trail);
    }
}
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement**

```csharp
using AcDream.Core.Combat;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Hud;

public enum DeckOrbMode { Peace, Melee, Missile, Magic }

public enum DeckContextRow { Power, Casting, Recall }

/// <summary>One vital as the game knows it: percent may be null until the max is known.</summary>
public readonly record struct DeckVitalInput(float? Percent, uint? Current, uint? Max);

/// <summary>Everything the deck reads, sampled once per frame.</summary>
public readonly record struct CommandDeckInput(
    DeckVitalInput Health,
    DeckVitalInput Stamina,
    DeckVitalInput Mana,
    CombatMode Mode,
    float DesiredPower,
    float PowerBarLevel,
    AttackHeight RequestedHeight,
    bool HasPendingCast,
    string? PendingSpellName,
    string? LastSpellName,
    JumpChargeSnapshot Jump,
    bool RepeatAttacks,
    bool AutoTarget,
    bool KeepInView,
    long SessionKey);

public readonly record struct DeckVitalView(float Fill, float Trail, string Text, bool Known);

/// <summary>What to draw this frame.</summary>
public sealed record CommandDeckView(
    DeckVitalView Health,
    DeckVitalView Stamina,
    DeckVitalView Mana,
    bool LowHealth,
    DeckOrbMode Orb,
    DeckContextRow Row,
    string PowerLabel,
    float DesiredPower,
    float PowerFill,
    AttackHeight Height,
    string CastingText,
    bool CastingLit,
    bool RecastEnabled,
    bool ShowStuds,
    bool RepeatAttacks,
    bool AutoTarget,
    bool KeepInView,
    float JumpFill,
    bool JumpCharging)
{
    public static CommandDeckView Empty { get; } = new CommandDeckModel().Update(default, 0.0);
}

/// <summary>Pure view logic for the Command Deck; owns the damage trails.</summary>
public sealed class CommandDeckModel
{
    public const float LowHealthThreshold = 0.25f;

    private readonly VitalTrail _health = new();
    private readonly VitalTrail _stamina = new();
    private readonly VitalTrail _mana = new();
    private long? _session;

    public CommandDeckView Update(in CommandDeckInput input, double now)
    {
        if (_session != input.SessionKey)
        {
            _session = input.SessionKey;
            _health.Reset();
            _stamina.Reset();
            _mana.Reset();
        }

        DeckOrbMode orb = input.Mode switch
        {
            CombatMode.Melee => DeckOrbMode.Melee,
            CombatMode.Missile => DeckOrbMode.Missile,
            CombatMode.Magic => DeckOrbMode.Magic,
            _ => DeckOrbMode.Peace,
        };
        DeckContextRow row = orb switch
        {
            DeckOrbMode.Melee or DeckOrbMode.Missile => DeckContextRow.Power,
            DeckOrbMode.Magic => DeckContextRow.Casting,
            _ => DeckContextRow.Recall,
        };

        (string castingText, bool castingLit) = input switch
        {
            { HasPendingCast: true, PendingSpellName: string name } => (name, true),
            { HasPendingCast: true } => ("Casting…", true),
            { LastSpellName: string last } => (last, false),
            _ => ("No spell cast yet", false),
        };

        return new CommandDeckView(
            Health: Vital(input.Health, _health, now),
            Stamina: Vital(input.Stamina, _stamina, now),
            Mana: Vital(input.Mana, _mana, now),
            LowHealth: input.Health.Percent is float hp && hp < LowHealthThreshold,
            Orb: orb,
            Row: row,
            PowerLabel: orb == DeckOrbMode.Missile ? "ACCURACY" : "POWER",
            DesiredPower: Math.Clamp(input.DesiredPower, 0f, 1f),
            PowerFill: Math.Clamp(input.PowerBarLevel, 0f, 1f),
            Height: input.RequestedHeight,
            CastingText: castingText,
            CastingLit: castingLit,
            RecastEnabled: !input.HasPendingCast && input.LastSpellName is not null,
            ShowStuds: orb != DeckOrbMode.Peace,
            RepeatAttacks: input.RepeatAttacks,
            AutoTarget: input.AutoTarget,
            KeepInView: input.KeepInView,
            JumpFill: input.Jump.IsCharging ? Math.Clamp(input.Jump.Power, 0f, 1f) : 0f,
            JumpCharging: input.Jump.IsCharging);
    }

    private static DeckVitalView Vital(DeckVitalInput vital, VitalTrail trail, double now)
    {
        if (vital.Percent is not float percent)
        {
            trail.Update(null, now);
            return new DeckVitalView(0f, 0f, "–", Known: false);
        }

        float fill = Math.Clamp(percent, 0f, 1f);
        string text = (vital.Current, vital.Max) is (uint current, uint max)
            ? $"{current} / {max}"
            : $"{MathF.Round(fill * 100f):0}%";
        return new DeckVitalView(fill, trail.Update(fill, now), text, Known: true);
    }
}
```

- [ ] **Step 4: Run the filter** → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud/CommandDeckModel.cs tests/AcDream.App.Tests/UI/Hud/CommandDeckModelTests.cs
git commit -m "feat(hud): CommandDeckModel view logic

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 7: `CommandDeckLayout` geometry and hit zones

**Goal:** Fixed rectangles for every part of the deck, and the zone under a point for the current view.

**Files:**
- Create: `src/AcDream.App/UI/Hud/CommandDeckLayout.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/CommandDeckLayoutTests.cs`

**Acceptance Criteria:**
- [ ] `Width == 420`, `Height == 150`; all rectangles lie inside the deck.
- [ ] `ZoneAt` returns `Orb` at the orb centre in every mode.
- [ ] Power-row zones (`PowerTrack`, `High`, `Medium`, `Low`) only for `Row == Power`; `Recast` only for `Casting`; `Recall0..4` only for `Recall`.
- [ ] Stud zones only when `ShowStuds`.
- [ ] Empty frame area returns `None` (so the window can be dragged there).
- [ ] `PowerAt(x)` maps the track's left edge to 0 and right edge to 1, clamped.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~CommandDeckLayoutTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-command-deck` → all pass.

**Steps:**

- [ ] **Step 1: Failing tests**

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Combat;

namespace AcDream.App.Tests.UI.Hud;

public sealed class CommandDeckLayoutTests
{
    private static CommandDeckView View(CombatMode mode) =>
        new CommandDeckModel().Update(default(CommandDeckInput) with { Mode = mode, SessionKey = 1 }, 0);

    private static (float X, float Y) Centre(DeckRect r) => (r.X + r.W / 2f, r.Y + r.H / 2f);

    [Fact]
    public void FixedSize_AndEveryRectInside()
    {
        Assert.Equal(420f, CommandDeckLayout.Width);
        Assert.Equal(150f, CommandDeckLayout.Height);
        foreach (DeckRect r in CommandDeckLayout.AllRects())
        {
            Assert.True(r.X >= 0 && r.Y >= 0, r.ToString());
            Assert.True(r.X + r.W <= CommandDeckLayout.Width, r.ToString());
            Assert.True(r.Y + r.H <= CommandDeckLayout.Height, r.ToString());
        }
    }

    [Theory]
    [InlineData(CombatMode.NonCombat)]
    [InlineData(CombatMode.Melee)]
    [InlineData(CombatMode.Magic)]
    public void OrbInEveryMode(CombatMode mode)
    {
        var (x, y) = Centre(CommandDeckLayout.Orb);
        Assert.Equal(DeckZone.Orb, CommandDeckLayout.ZoneAt(View(mode), x, y));
    }

    [Fact]
    public void RowZonesFollowTheRow()
    {
        var (hx, hy) = Centre(CommandDeckLayout.HeightButton(0));
        var (rx, ry) = Centre(CommandDeckLayout.Recast);
        var (lx, ly) = Centre(CommandDeckLayout.RecallStud(2));

        Assert.Equal(DeckZone.High, CommandDeckLayout.ZoneAt(View(CombatMode.Melee), hx, hy));
        Assert.Equal(DeckZone.Recast, CommandDeckLayout.ZoneAt(View(CombatMode.Magic), rx, ry));
        Assert.Equal(DeckZone.Recall2, CommandDeckLayout.ZoneAt(View(CombatMode.NonCombat), lx, ly));

        Assert.NotEqual(DeckZone.High, CommandDeckLayout.ZoneAt(View(CombatMode.Magic), hx, hy));
        Assert.NotEqual(DeckZone.Recast, CommandDeckLayout.ZoneAt(View(CombatMode.NonCombat), rx, ry));
        Assert.NotEqual(DeckZone.Recall2, CommandDeckLayout.ZoneAt(View(CombatMode.Melee), lx, ly));
    }

    [Fact]
    public void StudsOnlyInCombat()
    {
        var (x, y) = Centre(CommandDeckLayout.Stud(0));
        Assert.Equal(DeckZone.StudRepeat, CommandDeckLayout.ZoneAt(View(CombatMode.Melee), x, y));
        Assert.Equal(DeckZone.None, CommandDeckLayout.ZoneAt(View(CombatMode.NonCombat), x, y));
    }

    [Fact]
    public void FrameEdgeIsNone()
    {
        DeckRect f = CommandDeckLayout.Frame;
        Assert.Equal(DeckZone.None, CommandDeckLayout.ZoneAt(View(CombatMode.Melee), f.X + f.W - 4, f.Y + 4));
    }

    [Fact]
    public void PowerAtMapsTheTrack()
    {
        DeckRect t = CommandDeckLayout.PowerTrack;
        Assert.Equal(0f, CommandDeckLayout.PowerAt(t.X - 10));
        Assert.Equal(1f, CommandDeckLayout.PowerAt(t.X + t.W + 10));
        Assert.Equal(0.5f, CommandDeckLayout.PowerAt(t.X + t.W / 2f), 3);
    }
}
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement**

```csharp
namespace AcDream.App.UI.Hud;

public readonly record struct DeckRect(float X, float Y, float W, float H)
{
    public bool Contains(float x, float y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

public enum DeckZone
{
    None,
    Orb,
    StudRepeat, StudAutoTarget, StudKeepInView,
    Health, Stamina, Mana,
    PowerTrack, High, Medium, Low,
    Recast,
    Recall0, Recall1, Recall2, Recall3, Recall4,
    Jump,
}

/// <summary>The deck's fixed geometry in points. Nothing here depends on the
/// combat mode except which zones are live, so the window never changes size.</summary>
public static class CommandDeckLayout
{
    public const float Width = 420f;
    public const float Height = 150f;

    public static DeckRect Frame { get; } = new(70f, 4f, 350f, 142f);
    public static DeckRect Orb { get; } = new(32f, 29f, 92f, 92f);

    private const float ContentLeft = 134f;
    private const float ContentRight = 408f;
    private const float LabelWidth = 60f;
    private const float RowValueLeft = ContentLeft + LabelWidth + 6f;   // 200

    public static DeckRect HealthBar { get; } = new(ContentLeft, 16f, ContentRight - ContentLeft, 20f);
    public static DeckRect StaminaBar { get; } = new(ContentLeft, 41f, ContentRight - ContentLeft, 20f);
    public static DeckRect ManaBar { get; } = new(ContentLeft, 66f, ContentRight - ContentLeft, 20f);

    public static DeckRect ContextRow { get; } = new(ContentLeft, 92f, ContentRight - ContentLeft, 22f);
    public static DeckRect RowLabel { get; } = new(ContentLeft, 92f, LabelWidth, 22f);

    public static DeckRect PowerTrack { get; } = new(RowValueLeft, 98f, 118f, 10f);
    public static DeckRect HeightButton(int index) => new(326f + index * 28f, 92f, 26f, 22f);

    public static DeckRect CastingText { get; } = new(RowValueLeft, 92f, 128f, 22f);
    public static DeckRect Recast { get; } = new(334f, 92f, 74f, 22f);

    public static DeckRect RecallStud(int index) => new(RowValueLeft + index * 30f, 92f, 26f, 22f);

    public static DeckRect JumpLabel { get; } = new(ContentLeft, 122f, LabelWidth, 14f);
    public static DeckRect JumpTrack { get; } = new(RowValueLeft, 125f, ContentRight - RowValueLeft, 8f);

    public static DeckRect Stud(int index) => new(2f, 44f + index * 26f, 26f, 22f);

    public static IEnumerable<DeckRect> AllRects()
    {
        yield return Frame; yield return Orb;
        yield return HealthBar; yield return StaminaBar; yield return ManaBar;
        yield return ContextRow; yield return RowLabel; yield return PowerTrack;
        for (int i = 0; i < 3; i++) yield return HeightButton(i);
        yield return CastingText; yield return Recast;
        for (int i = 0; i < 5; i++) yield return RecallStud(i);
        yield return JumpLabel; yield return JumpTrack;
        for (int i = 0; i < 3; i++) yield return Stud(i);
    }

    /// <summary>Desired power for a pointer at <paramref name="x"/>, clamped to 0..1.</summary>
    public static float PowerAt(float x) =>
        Math.Clamp((x - PowerTrack.X) / PowerTrack.W, 0f, 1f);

    public static DeckZone ZoneAt(CommandDeckView view, float x, float y)
    {
        if (InCircle(Orb, x, y)) return DeckZone.Orb;

        if (view.ShowStuds)
        {
            if (Stud(0).Contains(x, y)) return DeckZone.StudRepeat;
            if (Stud(1).Contains(x, y)) return DeckZone.StudAutoTarget;
            if (Stud(2).Contains(x, y)) return DeckZone.StudKeepInView;
        }

        if (HealthBar.Contains(x, y)) return DeckZone.Health;
        if (StaminaBar.Contains(x, y)) return DeckZone.Stamina;
        if (ManaBar.Contains(x, y)) return DeckZone.Mana;
        if (JumpTrack.Contains(x, y)) return DeckZone.Jump;

        switch (view.Row)
        {
            case DeckContextRow.Power:
                // The track's hit band is the full row height so it is easy to grab.
                if (new DeckRect(PowerTrack.X, ContextRow.Y, PowerTrack.W, ContextRow.H).Contains(x, y))
                    return DeckZone.PowerTrack;
                if (HeightButton(0).Contains(x, y)) return DeckZone.High;
                if (HeightButton(1).Contains(x, y)) return DeckZone.Medium;
                if (HeightButton(2).Contains(x, y)) return DeckZone.Low;
                break;
            case DeckContextRow.Casting:
                if (Recast.Contains(x, y)) return DeckZone.Recast;
                break;
            case DeckContextRow.Recall:
                for (int i = 0; i < 5; i++)
                    if (RecallStud(i).Contains(x, y)) return DeckZone.Recall0 + i;
                break;
        }
        return DeckZone.None;
    }

    private static bool InCircle(DeckRect r, float x, float y)
    {
        float cx = r.X + r.W / 2f, cy = r.Y + r.H / 2f, radius = r.W / 2f;
        float dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy <= radius * radius;
    }
}
```

- [ ] **Step 4: Run the filter** → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud/CommandDeckLayout.cs tests/AcDream.App.Tests/UI/Hud/CommandDeckLayoutTests.cs
git commit -m "feat(hud): Command Deck fixed geometry and hit zones

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 8: HUD icons and `EmbeddedSvgSpriteCache`

**Goal:** Bundle the deck's SVG icons and turn them into coverage textures per device size.

**Files:**
- Create: `assets/hud/peace.svg`, `melee.svg`, `missile.svg`, `magic.svg`, `repeat.svg`, `autotarget.svg`, `keepinview.svg`, `recast.svg`, `recall-lifestone.svg`, `recall-marketplace.svg`, `recall-house.svg`, `recall-mansion.svg`, `recall-allegiance.svg`
- Modify: `src/AcDream.App/AcDream.App.csproj` (EmbeddedResource item group near line 68)
- Create: `src/AcDream.App/UI/Hud/EmbeddedSvgSpriteCache.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/EmbeddedSvgSpriteCacheTests.cs`

**Acceptance Criteria:**
- [ ] Every name in `HudIcons.All` loads from the App assembly and parses with `PluginSvgIcon.TryParse`.
- [ ] `Texture(name, px)` uploads once per (name, px); a different px for the same name releases the old texture.
- [ ] A missing or unparsable icon returns 0 and logs exactly once.
- [ ] `Dispose()` releases every uploaded texture.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~EmbeddedSvgSpriteCacheTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-command-deck` → all pass.

**Steps:**

- [ ] **Step 1: Add the SVGs.** Use only `path`, `circle`, `ellipse`, `rect`, `line` with `fill`/`stroke` attributes (the supported subset in `src/AcDream.Core/Plugins/PluginSvgIcon.cs`); viewBox `0 0 24 24`, white ink (colour comes from the draw tint). Content:

`assets/hud/peace.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9" fill="none" stroke="#fff" stroke-width="2"/><line x1="12" y1="3" x2="12" y2="21" stroke="#fff" stroke-width="2"/><line x1="12" y1="12" x2="5.6" y2="18.4" stroke="#fff" stroke-width="2"/><line x1="12" y1="12" x2="18.4" y2="18.4" stroke="#fff" stroke-width="2"/></svg>
```
`assets/hud/melee.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M4 3 L14 13 L12 15 L2 5 Z" fill="#fff"/><path d="M20 3 L10 13 L12 15 L22 5 Z" fill="#fff"/><line x1="7" y1="17" x2="11" y2="13" stroke="#fff" stroke-width="2"/><line x1="17" y1="17" x2="13" y2="13" stroke="#fff" stroke-width="2"/><circle cx="6" cy="18" r="2" fill="#fff"/><circle cx="18" cy="18" r="2" fill="#fff"/></svg>
```
`assets/hud/missile.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M6 3 Q20 12 6 21" fill="none" stroke="#fff" stroke-width="2"/><line x1="6" y1="3" x2="6" y2="21" stroke="#fff" stroke-width="1"/><line x1="3" y1="12" x2="21" y2="12" stroke="#fff" stroke-width="2"/><path d="M21 12 L17 9.5 L17 14.5 Z" fill="#fff"/></svg>
```
`assets/hud/magic.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M12 2 L14 10 L22 12 L14 14 L12 22 L10 14 L2 12 L10 10 Z" fill="#fff"/></svg>
```
`assets/hud/repeat.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M19 12 A7 7 0 1 1 16.9 7" fill="none" stroke="#fff" stroke-width="2.5"/><path d="M14 3 L20 6 L15 10 Z" fill="#fff"/></svg>
```
`assets/hud/autotarget.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="7" fill="none" stroke="#fff" stroke-width="2"/><circle cx="12" cy="12" r="2" fill="#fff"/><line x1="12" y1="1" x2="12" y2="6" stroke="#fff" stroke-width="2"/><line x1="12" y1="18" x2="12" y2="23" stroke="#fff" stroke-width="2"/><line x1="1" y1="12" x2="6" y2="12" stroke="#fff" stroke-width="2"/><line x1="18" y1="12" x2="23" y2="12" stroke="#fff" stroke-width="2"/></svg>
```
`assets/hud/keepinview.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M1 12 Q12 2 23 12 Q12 22 1 12 Z" fill="none" stroke="#fff" stroke-width="2"/><circle cx="12" cy="12" r="3.5" fill="#fff"/></svg>
```
`assets/hud/recast.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M5 12 A7 7 0 1 0 7.1 7" fill="none" stroke="#fff" stroke-width="2.5"/><path d="M10 3 L4 6 L9 10 Z" fill="#fff"/></svg>
```
`assets/hud/recall-lifestone.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><rect x="9.5" y="3" width="5" height="18" fill="#fff"/><rect x="3" y="9.5" width="18" height="5" fill="#fff"/></svg>
```
`assets/hud/recall-marketplace.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><line x1="12" y1="3" x2="12" y2="20" stroke="#fff" stroke-width="2"/><line x1="4" y1="6" x2="20" y2="6" stroke="#fff" stroke-width="2"/><path d="M1 14 L4 7 L7 14 Z" fill="#fff"/><path d="M17 14 L20 7 L23 14 Z" fill="#fff"/><rect x="8" y="19" width="8" height="2" fill="#fff"/></svg>
```
`assets/hud/recall-house.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M12 3 L22 12 L19 12 L19 21 L5 21 L5 12 L2 12 Z" fill="#fff"/><rect x="10" y="15" width="4" height="6" fill="#000"/></svg>
```
`assets/hud/recall-mansion.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><rect x="4" y="9" width="16" height="12" fill="#fff"/><rect x="4" y="5" width="3" height="4" fill="#fff"/><rect x="10.5" y="5" width="3" height="4" fill="#fff"/><rect x="17" y="5" width="3" height="4" fill="#fff"/></svg>
```
`assets/hud/recall-allegiance.svg`
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><line x1="5" y1="2" x2="5" y2="22" stroke="#fff" stroke-width="2"/><path d="M6 3 L20 3 L16 8 L20 13 L6 13 Z" fill="#fff"/></svg>
```

If `PluginSvgIcon.TryParse` rejects `Q` or `A` path commands (check the parser's path grammar first), replace those curves with straight `L` segments approximating the same shape; the test in Step 2 is the check.

- [ ] **Step 2: Embed them** — add to `AcDream.App.csproj` next to the other `EmbeddedResource` items:

```xml
    <EmbeddedResource Include="..\..\assets\hud\*.svg"
                      LogicalName="AcDream.Hud.%(Filename).svg" />
```

- [ ] **Step 3: Failing tests**

```csharp
using AcDream.App.Plugins;
using AcDream.App.UI.Hud;
using AcDream.Core.Plugins;

namespace AcDream.App.Tests.UI.Hud;

public sealed class EmbeddedSvgSpriteCacheTests
{
    private sealed class FakeBackend : IPluginFontBackend
    {
        public readonly List<uint> Uploaded = [];
        public readonly List<uint> Released = [];
        private uint _next = 100;
        public uint UploadCoverage(byte[] coverage, int width, int height, string debugName)
        {
            uint id = _next++;
            Uploaded.Add(id);
            return id;
        }
        public bool ReleaseCoverage(uint texture) { Released.Add(texture); return true; }
    }

    [Fact]
    public void EveryHudIconIsEmbeddedAndParses()
    {
        foreach (string name in HudIcons.All)
        {
            byte[]? bytes = EmbeddedSvgSpriteCache.LoadFromAssembly(name);
            Assert.True(bytes is not null, name);
            Assert.True(PluginSvgIcon.TryParse(bytes!, out _, out string? reason), name + ": " + reason);
        }
    }

    [Fact]
    public void UploadsOncePerSize_AndReleasesTheOldSize()
    {
        var backend = new FakeBackend();
        using var cache = new EmbeddedSvgSpriteCache(backend);

        uint a = cache.Texture(HudIcons.Melee, 32);
        uint again = cache.Texture(HudIcons.Melee, 32);
        Assert.NotEqual(0u, a);
        Assert.Equal(a, again);
        Assert.Single(backend.Uploaded);

        uint b = cache.Texture(HudIcons.Melee, 64);
        Assert.NotEqual(a, b);
        Assert.Equal([a], backend.Released);
    }

    [Fact]
    public void MissingIconReturnsZeroAndLogsOnce()
    {
        var log = new List<string>();
        using var cache = new EmbeddedSvgSpriteCache(new FakeBackend(), report: log.Add);

        Assert.Equal(0u, cache.Texture("no-such-icon", 32));
        Assert.Equal(0u, cache.Texture("no-such-icon", 32));
        Assert.Single(log);
    }

    [Fact]
    public void DisposeReleasesEverything()
    {
        var backend = new FakeBackend();
        var cache = new EmbeddedSvgSpriteCache(backend);
        cache.Texture(HudIcons.Melee, 32);
        cache.Texture(HudIcons.Magic, 32);

        cache.Dispose();

        Assert.Equal(backend.Uploaded.Order(), backend.Released.Order());
    }
}
```

- [ ] **Step 4: Run, expect compile failure.**

- [ ] **Step 5: Implement**

```csharp
using System.Reflection;
using AcDream.App.Plugins;
using AcDream.Core.Plugins;

namespace AcDream.App.UI.Hud;

/// <summary>Names of the bundled HUD icons (embedded as <c>AcDream.Hud.&lt;name&gt;.svg</c>).</summary>
public static class HudIcons
{
    public const string Peace = "peace";
    public const string Melee = "melee";
    public const string Missile = "missile";
    public const string Magic = "magic";
    public const string Repeat = "repeat";
    public const string AutoTarget = "autotarget";
    public const string KeepInView = "keepinview";
    public const string Recast = "recast";
    public const string RecallLifestone = "recall-lifestone";
    public const string RecallMarketplace = "recall-marketplace";
    public const string RecallHouse = "recall-house";
    public const string RecallMansion = "recall-mansion";
    public const string RecallAllegiance = "recall-allegiance";

    public static IReadOnlyList<string> All { get; } =
    [
        Peace, Melee, Missile, Magic, Repeat, AutoTarget, KeepInView, Recast,
        RecallLifestone, RecallMarketplace, RecallHouse, RecallMansion, RecallAllegiance,
    ];
}

/// <summary>
/// Bundled SVG icons baked to single-channel coverage textures, one device
/// size per icon at a time (a new size gives the old texture back). Used from
/// the interface thread only. A missing or unusable icon draws nothing and is
/// logged once.
/// </summary>
internal sealed class EmbeddedSvgSpriteCache : IDisposable
{
    private readonly IPluginFontBackend _backend;
    private readonly Action<string> _report;
    private readonly Func<string, byte[]?> _load;
    private readonly Func<SvgIconDocument, int, byte[]?> _bake;
    private readonly Dictionary<string, SvgIconDocument?> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Pixels, uint Texture)> _textures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    public EmbeddedSvgSpriteCache(
        IPluginFontBackend backend,
        Action<string>? report = null,
        Func<string, byte[]?>? load = null,
        Func<SvgIconDocument, int, byte[]?>? bake = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _report = report ?? Console.WriteLine;
        _load = load ?? LoadFromAssembly;
        _bake = bake ?? SvgIconRasterizer.Bake;
    }

    public static byte[]? LoadFromAssembly(string name)
    {
        using Stream? stream = typeof(EmbeddedSvgSpriteCache).Assembly
            .GetManifestResourceStream($"AcDream.Hud.{name}.svg");
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>The coverage texture for <paramref name="name"/> baked
    /// <paramref name="devicePixels"/> square, or 0 when it cannot be drawn.</summary>
    public uint Texture(string name, int devicePixels)
    {
        devicePixels = Math.Clamp(devicePixels, 1, SvgIconRasterizer.MaximumSize);
        if (_textures.TryGetValue(name, out var baked) && baked.Pixels == devicePixels)
            return baked.Texture;

        SvgIconDocument? document = Document(name);
        if (document is null) return 0;

        uint texture = 0;
        try
        {
            byte[]? coverage = _bake(document, devicePixels);
            if (coverage is not null)
                texture = _backend.UploadCoverage(coverage, devicePixels, devicePixels, "hud icon " + name);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(name, "could not be drawn: " + ex.Message);
            return 0;
        }
        if (texture == 0)
        {
            Report(name, "could not be drawn at this size");
            return 0;
        }

        if (_textures.TryGetValue(name, out var old))
            _backend.ReleaseCoverage(old.Texture);
        _textures[name] = (devicePixels, texture);
        return texture;
    }

    private SvgIconDocument? Document(string name)
    {
        if (_documents.TryGetValue(name, out SvgIconDocument? cached)) return cached;
        SvgIconDocument? document = null;
        byte[]? bytes = _load(name);
        if (bytes is null)
            Report(name, "is not bundled");
        else if (!PluginSvgIcon.TryParse(bytes, out document, out string? reason))
            Report(name, reason ?? "could not be parsed");
        _documents[name] = document;
        return document;
    }

    private void Report(string name, string reason)
    {
        if (_reported.Add(name))
            _report($"[UI] hud icon '{name}' ignored: {reason}");
    }

    public void Dispose()
    {
        foreach ((int _, uint texture) in _textures.Values)
            _backend.ReleaseCoverage(texture);
        _textures.Clear();
    }
}
```

- [ ] **Step 6: Run the filter** → PASS. If `EveryHudIconIsEmbeddedAndParses` fails on a path, simplify that SVG as Step 1 says.

- [ ] **Step 7: Commit**

```bash
git add assets/hud src/AcDream.App/AcDream.App.csproj src/AcDream.App/UI/Hud/EmbeddedSvgSpriteCache.cs tests/AcDream.App.Tests/UI/Hud/EmbeddedSvgSpriteCacheTests.cs
git commit -m "feat(hud): bundled HUD icons and EmbeddedSvgSpriteCache

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 9: `UiCommandDeck` (input + release contract)

**Goal:** The deck element: forwards clicks to `ICommandDeckActions`, honours the release contract, and supplies tooltips. Drawing is Task 10.

**Files:**
- Create: `src/AcDream.App/UI/Hud/ICommandDeckActions.cs`
- Create: `src/AcDream.App/UI/Hud/UiCommandDeck.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/UiCommandDeckTests.cs`

**Acceptance Criteria:**
- [ ] Orb click → `ToggleCombat()`. Stud click → `SetOption(id, !current)` for `AutoRepeatAttack`/`AutoTarget`/`ViewCombatTarget`.
- [ ] Power track press and drag → `SetDesiredPower(PowerAt(x))`; wheel over the track steps by `RuntimeCombatAttackState.DesiredPowerStep` (positive `Data0` raises).
- [ ] H/M/L press → `PressAttack(height)`; exactly one `ReleaseAttack()` for that press on: mouse-up inside, mouse-up outside, `CaptureChanged`, view row leaving Power (on tick), `ReleaseHeldAttack()`, `Detach()`.
- [ ] Recast click → `Recast()` only when `RecastEnabled`. Recall stud click → `Recall(command)` in order Lifestone, Marketplace, House, Mansion, Allegiance.
- [ ] `OnHitTest` is false where `ZoneAt` is `None`.
- [ ] Tooltip text for every zone except `None`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiCommandDeckTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-command-deck` → all pass.

**Steps:**

- [ ] **Step 1: The actions interface**

```csharp
using AcDream.Core.Combat;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;

namespace AcDream.App.UI.Hud;

/// <summary>What the Command Deck asks the game to do. Implemented over the
/// same handlers the retail windows and keybinds use.</summary>
public interface ICommandDeckActions
{
    void ToggleCombat();
    void PressAttack(AttackHeight height);
    void ReleaseAttack();
    void SetDesiredPower(float power);
    void SetOption(CharacterOptionId option, bool value);
    void Recall(RuntimePortalCommand command);
    void Recast();
}
```

(`CharacterOptionId` lives where `CombatUiController` imports it from: `AcDream.Core.Net.Messages`. Confirm with `grep -rn "enum CharacterOptionId" src` and adjust the `using` if needed.)

- [ ] **Step 2: Failing tests**

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Combat;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiCommandDeckTests
{
    private sealed class Recorder : ICommandDeckActions
    {
        public readonly List<string> Calls = [];
        public void ToggleCombat() => Calls.Add("toggle");
        public void PressAttack(AttackHeight height) => Calls.Add("press " + height);
        public void ReleaseAttack() => Calls.Add("release");
        public void SetDesiredPower(float power) => Calls.Add($"power {power:0.00}");
        public void SetOption(CharacterOptionId option, bool value) => Calls.Add($"option {option} {value}");
        public void Recall(RuntimePortalCommand command) => Calls.Add("recall " + command);
        public void Recast() => Calls.Add("recast");
    }

    private static CommandDeckInput Input(CombatMode mode, string? last = null) =>
        default(CommandDeckInput) with
        {
            Mode = mode, DesiredPower = 0.5f, LastSpellName = last, SessionKey = 1,
            RepeatAttacks = true,
        };

    private static (UiCommandDeck Deck, Recorder Actions, Action<CommandDeckInput> Set) Make(CombatMode mode, string? last = null)
    {
        var model = new CommandDeckModel();
        CommandDeckInput input = Input(mode, last);
        var actions = new Recorder();
        var deck = new UiCommandDeck(() => input, model, actions, icons: null, clock: () => 0.0);
        deck.Refresh();
        return (deck, actions, next => { input = next; deck.Refresh(); });
    }

    private static void Down(UiCommandDeck d, DeckRect r) =>
        d.OnEvent(new UiEvent(0, d, UiEventType.MouseDown, Data1: (int)(r.X + r.W / 2), Data2: (int)(r.Y + r.H / 2)));

    private static void Up(UiCommandDeck d, float x, float y) =>
        d.OnEvent(new UiEvent(0, d, UiEventType.MouseUp, Data1: (int)x, Data2: (int)y));

    [Fact]
    public void OrbTogglesCombat()
    {
        var (deck, actions, _) = Make(CombatMode.NonCombat);
        Down(deck, CommandDeckLayout.Orb);
        Assert.Equal(["toggle"], actions.Calls);
    }

    [Fact]
    public void StudFlipsItsOption()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        Down(deck, CommandDeckLayout.Stud(0));
        Down(deck, CommandDeckLayout.Stud(1));
        Assert.Equal(
            [$"option {CharacterOptionId.AutoRepeatAttack} False", $"option {CharacterOptionId.AutoTarget} True"],
            actions.Calls);
    }

    [Fact]
    public void PowerTrackPressDragAndWheel()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        DeckRect t = CommandDeckLayout.PowerTrack;
        deck.OnEvent(new UiEvent(0, deck, UiEventType.MouseDown, Data1: (int)(t.X + t.W / 4), Data2: (int)(t.Y + 2)));
        deck.OnEvent(new UiEvent(0, deck, UiEventType.MouseMove, Data1: (int)(t.X + t.W + 50), Data2: (int)(t.Y + 2)));
        Up(deck, t.X + t.W + 50, t.Y + 2);
        deck.OnEvent(new UiEvent(0, deck, UiEventType.Scroll, Data0: 1, Data1: (int)(t.X + 5), Data2: (int)(t.Y + 2)));

        Assert.Equal("power 0.25", actions.Calls[0][..10]);
        Assert.Equal("power 1.00", actions.Calls[1]);
        Assert.Equal($"power {0.5f + RuntimeCombatAttackState.DesiredPowerStep:0.00}", actions.Calls[^1]);
    }

    [Fact]
    public void AttackReleasedOnMouseUpInside()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        DeckRect m = CommandDeckLayout.HeightButton(1);
        Down(deck, m);
        Up(deck, m.X + 2, m.Y + 2);
        deck.ReleaseHeldAttack();
        Assert.Equal(["press Medium", "release"], actions.Calls);
    }

    [Fact]
    public void AttackReleasedOnMouseUpOutside()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        Down(deck, CommandDeckLayout.HeightButton(0));
        Up(deck, -50, -50);
        Assert.Equal(["press High", "release"], actions.Calls);
    }

    [Fact]
    public void AttackReleasedOnCaptureLost()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        Down(deck, CommandDeckLayout.HeightButton(2));
        deck.OnEvent(new UiEvent(0, deck, UiEventType.CaptureChanged));
        Up(deck, 0, 0);
        Assert.Equal(["press Low", "release"], actions.Calls);
    }

    [Fact]
    public void AttackReleasedWhenTheRowLeavesPower()
    {
        var (deck, actions, set) = Make(CombatMode.Melee);
        Down(deck, CommandDeckLayout.HeightButton(1));
        set(Input(CombatMode.Magic));
        Assert.Equal(["press Medium", "release"], actions.Calls);
    }

    [Fact]
    public void AttackReleasedOnDetach()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        Down(deck, CommandDeckLayout.HeightButton(1));
        deck.Detach();
        deck.Detach();
        Assert.Equal(["press Medium", "release"], actions.Calls);
    }

    [Fact]
    public void RecastOnlyWhenEnabled()
    {
        var (none, noneActions, _) = Make(CombatMode.Magic);
        Down(none, CommandDeckLayout.Recast);
        Assert.Empty(noneActions.Calls);

        var (deck, actions, _) = Make(CombatMode.Magic, last: "Flame Bolt I");
        Down(deck, CommandDeckLayout.Recast);
        Assert.Equal(["recast"], actions.Calls);
    }

    [Fact]
    public void RecallStudsInOrder()
    {
        var (deck, actions, _) = Make(CombatMode.NonCombat);
        for (int i = 0; i < 5; i++) Down(deck, CommandDeckLayout.RecallStud(i));
        Assert.Equal(
        [
            "recall RecallLifestone", "recall RecallMarketplace", "recall RecallHouse",
            "recall RecallMansion", "recall RecallAllegiance",
        ], actions.Calls);
    }

    [Fact]
    public void EmptyFrameDoesNotHitTest()
    {
        var (deck, _, _) = Make(CombatMode.Melee);
        DeckRect f = CommandDeckLayout.Frame;
        Assert.False(deck.HitTestLocal(f.X + f.W - 4, f.Y + 4));
        Assert.True(deck.HitTestLocal(CommandDeckLayout.Orb.X + 46, CommandDeckLayout.Orb.Y + 46));
    }

    [Fact]
    public void EveryLiveZoneHasATooltip()
    {
        foreach (DeckZone zone in Enum.GetValues<DeckZone>())
        {
            if (zone == DeckZone.None) continue;
            Assert.False(string.IsNullOrWhiteSpace(UiCommandDeck.TooltipFor(zone, CommandDeckView.Empty)), zone.ToString());
        }
    }
}
```

- [ ] **Step 3: Run, expect compile failure.**

- [ ] **Step 4: Implement** (drawing left as an empty `OnDraw` for now; Task 10 fills it)

```csharp
using AcDream.Core.Combat;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The Command Deck: one fixed-size element that draws a
/// <see cref="CommandDeckView"/> and turns pointer input into
/// <see cref="ICommandDeckActions"/>. Areas with no control do not hit-test,
/// so the window can be dragged by them.
/// </summary>
internal sealed partial class UiCommandDeck : UiElement
{
    private static readonly RuntimePortalCommand[] Recalls =
    [
        RuntimePortalCommand.RecallLifestone,
        RuntimePortalCommand.RecallMarketplace,
        RuntimePortalCommand.RecallHouse,
        RuntimePortalCommand.RecallMansion,
        RuntimePortalCommand.RecallAllegiance,
    ];

    private readonly Func<CommandDeckInput> _input;
    private readonly CommandDeckModel _model;
    private readonly ICommandDeckActions _actions;
    private readonly EmbeddedSvgSpriteCache? _icons;
    private readonly Func<double> _clock;
    private CommandDeckView _view = CommandDeckView.Empty;
    private AttackHeight? _held;
    private bool _draggingPower;
    private DeckZone _hover;

    public UiCommandDeck(
        Func<CommandDeckInput> input,
        CommandDeckModel model,
        ICommandDeckActions actions,
        EmbeddedSvgSpriteCache? icons,
        Func<double> clock)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _icons = icons;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Width = CommandDeckLayout.Width;
        Height = CommandDeckLayout.Height;
        MinWidth = MaxWidth = Width;
        MinHeight = MaxHeight = Height;
        Resizable = false;
        CapturesPointerDrag = true;
        RuntimeTooltipTextSource = () => _hover == DeckZone.None ? null : TooltipFor(_hover, _view);
    }

    public override bool HandlesClick => true;
    public override bool ReceivesHoverMouseMove => true;

    internal CommandDeckView View => _view;

    /// <summary>Samples the game and rebuilds the view; releases a held attack if the power row went away.</summary>
    internal void Refresh()
    {
        _view = _model.Update(_input(), _clock());
        if (_held is not null && _view.Row != DeckContextRow.Power)
            ReleaseHeldAttack();
    }

    protected override void OnTick(double deltaSeconds) => Refresh();

    internal bool HitTestLocal(float x, float y) => OnHitTest(x, y);

    protected override bool OnHitTest(float localX, float localY) =>
        CommandDeckLayout.ZoneAt(_view, localX, localY) != DeckZone.None;

    /// <summary>Ends an attack started from the deck, once.</summary>
    public void ReleaseHeldAttack()
    {
        if (_held is null) return;
        _held = null;
        _actions.ReleaseAttack();
    }

    /// <summary>Called when the deck is torn down.</summary>
    public void Detach()
    {
        _draggingPower = false;
        ReleaseHeldAttack();
    }

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.MouseDown:
                return Press(CommandDeckLayout.ZoneAt(_view, e.Data1, e.Data2), e.Data1);
            case UiEventType.MouseMove:
                _hover = CommandDeckLayout.ZoneAt(_view, e.Data1, e.Data2);
                if (_draggingPower)
                {
                    _actions.SetDesiredPower(CommandDeckLayout.PowerAt(e.Data1));
                    return true;
                }
                return _hover != DeckZone.None;
            case UiEventType.MouseUp:
            case UiEventType.CaptureChanged:
                _draggingPower = false;
                ReleaseHeldAttack();
                return true;
            case UiEventType.HoverLeave:
                _hover = DeckZone.None;
                return true;
            case UiEventType.Scroll:
                if (_view.Row != DeckContextRow.Power
                    || CommandDeckLayout.ZoneAt(_view, e.Data1, e.Data2) != DeckZone.PowerTrack
                    || e.Data0 == 0)
                    return false;
                _actions.SetDesiredPower(Math.Clamp(
                    _view.DesiredPower + Math.Sign(e.Data0) * RuntimeCombatAttackState.DesiredPowerStep,
                    0f, 1f));
                return true;
            default:
                return false;
        }
    }

    private bool Press(DeckZone zone, float x)
    {
        switch (zone)
        {
            case DeckZone.Orb:
                _actions.ToggleCombat();
                return true;
            case DeckZone.StudRepeat:
                _actions.SetOption(CharacterOptionId.AutoRepeatAttack, !_view.RepeatAttacks);
                return true;
            case DeckZone.StudAutoTarget:
                _actions.SetOption(CharacterOptionId.AutoTarget, !_view.AutoTarget);
                return true;
            case DeckZone.StudKeepInView:
                _actions.SetOption(CharacterOptionId.ViewCombatTarget, !_view.KeepInView);
                return true;
            case DeckZone.PowerTrack:
                _draggingPower = true;
                _actions.SetDesiredPower(CommandDeckLayout.PowerAt(x));
                return true;
            case DeckZone.High:
            case DeckZone.Medium:
            case DeckZone.Low:
                ReleaseHeldAttack();
                _held = zone switch
                {
                    DeckZone.High => AttackHeight.High,
                    DeckZone.Medium => AttackHeight.Medium,
                    _ => AttackHeight.Low,
                };
                _actions.PressAttack(_held.Value);
                return true;
            case DeckZone.Recast:
                if (_view.RecastEnabled) _actions.Recast();
                return true;
            case >= DeckZone.Recall0 and <= DeckZone.Recall4:
                _actions.Recall(Recalls[zone - DeckZone.Recall0]);
                return true;
            case DeckZone.None:
                return false;
            default:
                return true;   // bars and jump: tooltip only
        }
    }

    internal static string TooltipFor(DeckZone zone, CommandDeckView view) => zone switch
    {
        DeckZone.Orb => view.Orb == DeckOrbMode.Peace
            ? "Peace. Click to enter combat."
            : "Combat. Click to return to peace.",
        DeckZone.StudRepeat => "Repeat attacks: " + OnOff(view.RepeatAttacks),
        DeckZone.StudAutoTarget => "Auto-target: " + OnOff(view.AutoTarget),
        DeckZone.StudKeepInView => "Keep target in view: " + OnOff(view.KeepInView),
        DeckZone.Health => "Health " + view.Health.Text,
        DeckZone.Stamina => "Stamina " + view.Stamina.Text,
        DeckZone.Mana => "Mana " + view.Mana.Text,
        DeckZone.PowerTrack => $"{(view.PowerLabel == "ACCURACY" ? "Accuracy" : "Power")} {MathF.Round(view.DesiredPower * 100f):0}%. Drag or scroll to change.",
        DeckZone.High => "Hold to attack high",
        DeckZone.Medium => "Hold to attack medium",
        DeckZone.Low => "Hold to attack low",
        DeckZone.Recast => view.RecastEnabled ? "Recast " + view.CastingText : "Nothing to recast",
        DeckZone.Recall0 => "Lifestone recall",
        DeckZone.Recall1 => "Marketplace recall",
        DeckZone.Recall2 => "House recall",
        DeckZone.Recall3 => "Mansion recall",
        DeckZone.Recall4 => "Allegiance hometown recall",
        DeckZone.Jump => "Jump charge",
        _ => "",
    };

    private static string OnOff(bool on) => on ? "on" : "off";
}
```

Put the class `partial` so Task 10 adds drawing in `UiCommandDeck.Draw.cs`.

- [ ] **Step 5: Run the filter** → PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/Hud/ICommandDeckActions.cs src/AcDream.App/UI/Hud/UiCommandDeck.cs tests/AcDream.App.Tests/UI/Hud/UiCommandDeckTests.cs
git commit -m "feat(hud): UiCommandDeck input, attack release contract, tooltips

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 10: `UiCommandDeck` drawing

**Goal:** Draw the D1 look: dark bevelled frame with bronze inner edge, orb, labelled bars with numbers and trails, context row per mode, jump strip, studs.

**Files:**
- Create: `src/AcDream.App/UI/Hud/UiCommandDeck.Draw.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/UiCommandDeckDrawTests.cs`

**Acceptance Criteria:**
- [ ] Drawing every mode (peace, melee, missile, magic) with known and unknown vitals does not throw, using a `UiRenderContext` the way existing draw tests do (see `tests/AcDream.App.Tests/UI/MarkupDrawProbe.cs`).
- [ ] Nothing is drawn outside 0..Width × 0..Height (check via the probe's recorded vertex bounds if it exposes them; otherwise this criterion is checked in the live gate).
- [ ] Colours match the mockup palette below.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiCommandDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-command-deck` → all pass.

**Steps:**

- [ ] **Step 1: Read `tests/AcDream.App.Tests/UI/MarkupDrawProbe.cs`** to see how tests build a `UiRenderContext` and draw an element tree; write `UiCommandDeckDrawTests` with one `[Theory]` over `CombatMode.NonCombat/Melee/Missile/Magic` × `{known vitals, null vitals}` that builds a deck (as in Task 9's `Make`) and draws it through that probe, asserting no exception (and bounds, if available).

- [ ] **Step 2: Run, expect failure only if drawing is missing** (empty `OnDraw` passes the no-throw test; that is fine, the test guards regressions).

- [ ] **Step 3: Implement** `UiCommandDeck.Draw.cs`:

```csharp
using System.Numerics;
using AcDream.Core.Combat;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiCommandDeck
{
    private static Vector4 Rgb(int hex, float a = 1f) =>
        new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, a);

    private static readonly Vector4 FrameTop = Rgb(0x2B2A31);
    private static readonly Vector4 FrameBottom = Rgb(0x17161B);
    private static readonly Vector4 FrameEdge = Rgb(0x0B0A0D);
    private static readonly Vector4 Bronze = Rgb(0x6B5A3A, 0.7f);
    private static readonly Vector4 Track = Rgb(0x0C0B0F);
    private static readonly Vector4 TrailInk = Rgb(0xF3E2C0, 0.45f);
    private static readonly Vector4 TextInk = Rgb(0xFFFFFF);
    private static readonly Vector4 LabelInk = Rgb(0xC9BFA6);
    private static readonly Vector4 StudOff = Rgb(0x34323B);
    private static readonly Vector4 StudOffInk = Rgb(0xA8A3B5);
    private static readonly Vector4 StudOn = Rgb(0xD14A5C);
    private static readonly Vector4 CombatGlow = Rgb(0xC2233A, 0.55f);
    private static readonly Vector4 PeaceGlow = Rgb(0x78C8A0, 0.35f);
    private static readonly (Vector4 Top, Vector4 Bottom) HealthInk = (Rgb(0xFF6B7D), Rgb(0x7D0F1C));
    private static readonly (Vector4 Top, Vector4 Bottom) StaminaInk = (Rgb(0xFFE27A), Rgb(0x8A6C10));
    private static readonly (Vector4 Top, Vector4 Bottom) ManaInk = (Rgb(0x8FB0FF), Rgb(0x1A3590));
    private static readonly Vector4 PowerInk = Rgb(0xFFF6D8);
    private static readonly Vector4 Handle = Rgb(0xE2C27A);
    private static readonly Vector4 JumpInk = Rgb(0x7FD6A8);
    private static readonly Vector4 CastLit = Rgb(0xCFE0FF);

    protected override void OnDraw(UiRenderContext ctx)
    {
        CommandDeckView v = _view;
        double now = _clock();

        DrawFrame(ctx);
        DrawBar(ctx, CommandDeckLayout.HealthBar, "HEALTH", v.Health, HealthInk,
            pulse: v.LowHealth ? 0.25f + 0.25f * MathF.Sin((float)now * 6f) : 0f);
        DrawBar(ctx, CommandDeckLayout.StaminaBar, "STAMINA", v.Stamina, StaminaInk, 0f);
        DrawBar(ctx, CommandDeckLayout.ManaBar, "MANA", v.Mana, ManaInk, 0f);

        switch (v.Row)
        {
            case DeckContextRow.Power: DrawPowerRow(ctx, v); break;
            case DeckContextRow.Casting: DrawCastingRow(ctx, v); break;
            case DeckContextRow.Recall: DrawRecallRow(ctx); break;
        }
        DrawJump(ctx, v);
        DrawOrb(ctx, v);
        if (v.ShowStuds) DrawStuds(ctx, v);
    }

    private static void DrawFrame(UiRenderContext ctx)
    {
        DeckRect f = CommandDeckLayout.Frame;
        ctx.FillRoundedRect(f.X - 2, f.Y - 2, f.W + 4, f.H + 4, 10f, FrameEdge);
        ctx.FillVerticalGradient(f.X, f.Y, f.W, f.H, new CanvasCornerRadii(8f, 8f, 8f, 8f), FrameTop, FrameBottom);
        ctx.StrokeRoundedRect(f.X + 3, f.Y + 3, f.W - 6, f.H - 6, 5f, Bronze, 1f);
    }

    private void DrawBar(UiRenderContext ctx, DeckRect r, string label, DeckVitalView vital,
        (Vector4 Top, Vector4 Bottom) ink, float pulse)
    {
        ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, 4f, Track);
        if (vital.Known)
        {
            if (vital.Trail > vital.Fill)
                ctx.FillRoundedRect(r.X, r.Y, r.W * vital.Trail, r.H, 3f, TrailInk);
            if (vital.Fill > 0f)
                ctx.FillVerticalGradient(r.X, r.Y, r.W * vital.Fill, r.H,
                    new CanvasCornerRadii(3f, 3f, 3f, 3f), ink.Top, ink.Bottom);
            if (pulse > 0f)
                ctx.FillRoundedRect(r.X, r.Y, r.W * vital.Fill, r.H, 3f, new Vector4(1f, 1f, 1f, pulse * 0.4f));
        }
        DrawText(ctx, label, r.X + 8, r.Y + 4, LabelInk, small: true);
        DrawTextRight(ctx, vital.Text, r.X + r.W - 8, r.Y + 3, TextInk);
    }

    private void DrawPowerRow(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect label = CommandDeckLayout.RowLabel;
        DrawText(ctx, v.PowerLabel, label.X, label.Y + 6, LabelInk, small: true);

        DeckRect t = CommandDeckLayout.PowerTrack;
        ctx.FillRoundedRect(t.X, t.Y, t.W, t.H, 5f, Track);
        for (int i = 1; i < 4; i++)
            ctx.FillRect(t.X + t.W * i / 4f, t.Y, 1f, t.H, Rgb(0x4A4752));
        if (v.PowerFill > 0f)
            ctx.FillRoundedRect(t.X, t.Y, t.W * v.PowerFill, t.H, 4f, PowerInk);
        float hx = t.X + t.W * v.DesiredPower;
        ctx.FillRoundedRect(hx - 5f, t.Y - 5f, 10f, t.H + 10f, 3f, Handle);

        string[] letters = ["H", "M", "L"];
        AttackHeight[] heights = [AttackHeight.High, AttackHeight.Medium, AttackHeight.Low];
        for (int i = 0; i < 3; i++)
            DrawStud(ctx, CommandDeckLayout.HeightButton(i), on: v.Height == heights[i], icon: null, letter: letters[i]);
    }

    private void DrawCastingRow(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect label = CommandDeckLayout.RowLabel;
        DrawText(ctx, "CASTING", label.X, label.Y + 6, LabelInk, small: true);
        DeckRect text = CommandDeckLayout.CastingText;
        DrawText(ctx, v.CastingText, text.X, text.Y + 4, v.CastingLit ? CastLit : StudOffInk);
        DeckRect recast = CommandDeckLayout.Recast;
        DrawStud(ctx, recast, on: false, icon: HudIcons.Recast, letter: "RECAST", dim: !v.RecastEnabled);
    }

    private void DrawRecallRow(UiRenderContext ctx)
    {
        DeckRect label = CommandDeckLayout.RowLabel;
        DrawText(ctx, "RECALL", label.X, label.Y + 6, LabelInk, small: true);
        string[] icons =
        [
            HudIcons.RecallLifestone, HudIcons.RecallMarketplace, HudIcons.RecallHouse,
            HudIcons.RecallMansion, HudIcons.RecallAllegiance,
        ];
        for (int i = 0; i < 5; i++)
            DrawStud(ctx, CommandDeckLayout.RecallStud(i), on: false, icon: icons[i], letter: null);
    }

    private void DrawJump(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect label = CommandDeckLayout.JumpLabel;
        DrawText(ctx, "JUMP", label.X, label.Y, v.JumpCharging ? JumpInk : LabelInk with { W = 0.5f }, small: true);
        DeckRect t = CommandDeckLayout.JumpTrack;
        ctx.FillRoundedRect(t.X, t.Y, t.W, t.H, 3f, Track);
        if (v.JumpFill > 0f)
            ctx.FillRoundedRect(t.X, t.Y, t.W * v.JumpFill, t.H, 3f, JumpInk);
    }

    private void DrawOrb(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect o = CommandDeckLayout.Orb;
        bool combat = v.Orb != DeckOrbMode.Peace;
        ctx.FillEllipse(o.X - 6, o.Y - 6, o.W + 12, o.H + 12, combat ? CombatGlow : PeaceGlow);
        ctx.FillEllipse(o.X - 2, o.Y - 2, o.W + 4, o.H + 4, FrameEdge);
        ctx.FillEllipse(o.X, o.Y, o.W, o.H, Rgb(0x6B5A3A));
        ctx.FillEllipse(o.X + 3, o.Y + 3, o.W - 6, o.H - 6, Rgb(0x0B0C12));
        ctx.FillEllipse(o.X + 8, o.Y + 6, o.W - 24, o.H - 24, Rgb(0x2A2D3C, 0.9f));
        ctx.FillEllipse(o.X + 22, o.Y + 12, 22, 12, new Vector4(1f, 1f, 1f, 0.12f));

        string icon = v.Orb switch
        {
            DeckOrbMode.Melee => HudIcons.Melee,
            DeckOrbMode.Missile => HudIcons.Missile,
            DeckOrbMode.Magic => HudIcons.Magic,
            _ => HudIcons.Peace,
        };
        DrawIcon(ctx, icon, o.X + o.W / 2 - 16, o.Y + o.H / 2 - 22, 32f, TextInk);
        string label = v.Orb switch
        {
            DeckOrbMode.Melee => "MELEE",
            DeckOrbMode.Missile => "MISSILE",
            DeckOrbMode.Magic => "MAGIC",
            _ => "PEACE",
        };
        DrawTextCentered(ctx, label, o.X + o.W / 2, o.Y + o.H / 2 + 14, LabelInk, small: true);
    }

    private void DrawStuds(UiRenderContext ctx, CommandDeckView v)
    {
        DrawStud(ctx, CommandDeckLayout.Stud(0), v.RepeatAttacks, HudIcons.Repeat, null);
        DrawStud(ctx, CommandDeckLayout.Stud(1), v.AutoTarget, HudIcons.AutoTarget, null);
        DrawStud(ctx, CommandDeckLayout.Stud(2), v.KeepInView, HudIcons.KeepInView, null);
    }

    private void DrawStud(UiRenderContext ctx, DeckRect r, bool on, string? icon, string? letter, bool dim = false)
    {
        ctx.FillRoundedRect(r.X - 1, r.Y - 1, r.W + 2, r.H + 2, 6f, FrameEdge);
        ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, 5f, on ? StudOn : StudOff);
        Vector4 ink = on ? TextInk : StudOffInk;
        if (dim) ink = ink with { W = 0.4f };
        float x = r.X + 4;
        if (icon is not null)
        {
            DrawIcon(ctx, icon, r.X + (letter is null ? (r.W - 14) / 2 : 6), r.Y + (r.H - 14) / 2, 14f, ink);
            x = r.X + 24;
        }
        if (letter is not null)
        {
            if (icon is null) DrawTextCentered(ctx, letter, r.X + r.W / 2, r.Y + 5, ink, small: true);
            else DrawText(ctx, letter, x, r.Y + 6, ink, small: true);
        }
    }

    private void DrawIcon(UiRenderContext ctx, string name, float x, float y, float size, Vector4 ink)
    {
        if (_icons is null) return;
        int pixels = Math.Max(1, (int)MathF.Ceiling(size * ctx.PixelScale));
        uint texture = _icons.Texture(name, pixels);
        if (texture != 0) ctx.DrawCoverageIcon(texture, x, y, pixels, ink);
    }

    private static void DrawText(UiRenderContext ctx, string text, float x, float y, Vector4 ink, bool small = false) =>
        ctx.DrawString(text, x, y, ink);

    private static void DrawTextRight(UiRenderContext ctx, string text, float right, float y, Vector4 ink) =>
        ctx.DrawString(text, right - MeasureText(ctx, text), y, ink);

    private static void DrawTextCentered(UiRenderContext ctx, string text, float centre, float y, Vector4 ink, bool small = false) =>
        ctx.DrawString(text, centre - MeasureText(ctx, text) / 2f, y, ink);

    private static float MeasureText(UiRenderContext ctx, string text) =>
        ctx.DefaultFont?.MeasureWidth(text) ?? 0f;
}
```

`small` is accepted but ignored in this first cut (every label uses `ctx.DefaultFont`). If the live check shows the labels too large, add a smaller `BitmapFont` field set at mount and pass it to `DrawString`; that is a follow-up, not part of this task.

- [ ] **Step 4: Run the filter** → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud/UiCommandDeck.Draw.cs tests/AcDream.App.Tests/UI/Hud/UiCommandDeckDrawTests.cs
git commit -m "feat(hud): draw the Command Deck

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 11: `ModernHudSwitch`

**Goal:** Gate the four retail windows and own the deck's visibility, fail-safe.

**Files:**
- Create: `src/AcDream.App/UI/Hud/ModernHudSwitch.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/ModernHudSwitchTests.cs`

**Acceptance Criteria:**
- [ ] `Apply(true)` with the deck mounted: hides the four retail windows, `Active` true, deck shown when gameplay is active.
- [ ] While active, gated show requests for retail windows do nothing; gated hides still hide.
- [ ] `Apply(true)` with the deck not mounted: nothing hidden, `Active` false, one log line (not repeated).
- [ ] `Apply(false)` after active: deck hidden, `onDeckHidden` called, `resyncRetail` called once, `Active` false.
- [ ] `Apply(false)` when never active: does not call `resyncRetail`.
- [ ] `Tick()` shows/hides the deck to follow `gameplayActive()` only while active, and calls `onDeckHidden` when it hides.
- [ ] Repeated `Apply` with the same value is a no-op.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ModernHudSwitchTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-command-deck` → all pass.

**Steps:**

- [ ] **Step 1: Failing tests**

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ModernHudSwitchTests
{
    private sealed class Harness
    {
        public readonly HashSet<string> Visible = [WindowNames.Vitals, WindowNames.Combat];
        public readonly List<string> Log = [];
        public bool Mounted = true;
        public bool Gameplay = true;
        public int Resyncs;
        public int DeckHidden;
        public ModernHudSwitch Switch = null!;

        public Harness()
        {
            Switch = new ModernHudSwitch(
                show: n => Visible.Add(n),
                hide: n => Visible.Remove(n),
                deckMounted: () => Mounted,
                gameplayActive: () => Gameplay,
                resyncRetail: () => Resyncs++,
                onDeckHidden: () => DeckHidden++,
                log: Log.Add);
        }
    }

    [Fact]
    public void On_HidesRetailAndShowsDeck()
    {
        var h = new Harness();
        h.Switch.Apply(true);

        Assert.True(h.Switch.Active);
        Assert.DoesNotContain(WindowNames.Vitals, h.Visible);
        Assert.DoesNotContain(WindowNames.Combat, h.Visible);
        Assert.Contains(WindowNames.CommandDeck, h.Visible);
    }

    [Fact]
    public void WhileOn_GatedShowsAreIgnored_HidesStillWork()
    {
        var h = new Harness();
        h.Switch.Apply(true);

        h.Switch.GateVisibility(WindowNames.Combat)(true);
        Assert.False(h.Switch.GatedShow(WindowNames.SideVitals));
        Assert.DoesNotContain(WindowNames.Combat, h.Visible);
        Assert.DoesNotContain(WindowNames.SideVitals, h.Visible);

        h.Visible.Add(WindowNames.JumpPowerbar);
        h.Switch.GateVisibility(WindowNames.JumpPowerbar)(false);
        Assert.DoesNotContain(WindowNames.JumpPowerbar, h.Visible);
    }

    [Fact]
    public void WhileOff_GatesPassThrough()
    {
        var h = new Harness();
        h.Switch.GateVisibility(WindowNames.JumpPowerbar)(true);
        Assert.True(h.Switch.GatedShow(WindowNames.SideVitals));
        Assert.Contains(WindowNames.JumpPowerbar, h.Visible);
        Assert.Contains(WindowNames.SideVitals, h.Visible);
    }

    [Fact]
    public void On_WithoutDeck_LeavesRetailAndLogsOnce()
    {
        var h = new Harness { };
        h.Mounted = false;
        h.Switch.Apply(true);
        h.Switch.Apply(false);
        h.Switch.Apply(true);

        Assert.False(h.Switch.Active);
        Assert.Contains(WindowNames.Vitals, h.Visible);
        Assert.Single(h.Log);
        Assert.Equal(0, h.Resyncs);
    }

    [Fact]
    public void Off_RestoresRetailAndHidesDeck()
    {
        var h = new Harness();
        h.Switch.Apply(true);
        h.Switch.Apply(false);

        Assert.False(h.Switch.Active);
        Assert.DoesNotContain(WindowNames.CommandDeck, h.Visible);
        Assert.Equal(1, h.Resyncs);
        Assert.Equal(1, h.DeckHidden);
    }

    [Fact]
    public void Off_WhenNeverOn_DoesNotResync()
    {
        var h = new Harness();
        h.Switch.Apply(false);
        Assert.Equal(0, h.Resyncs);
    }

    [Fact]
    public void Tick_FollowsGameplay()
    {
        var h = new Harness();
        h.Switch.Apply(true);

        h.Gameplay = false;
        h.Switch.Tick();
        Assert.DoesNotContain(WindowNames.CommandDeck, h.Visible);
        Assert.Equal(1, h.DeckHidden);

        h.Gameplay = true;
        h.Switch.Tick();
        Assert.Contains(WindowNames.CommandDeck, h.Visible);
    }

    [Fact]
    public void RepeatedApplyIsANoOp()
    {
        var h = new Harness();
        h.Switch.Apply(true);
        h.Visible.Add(WindowNames.Vitals);   // something else showed it
        h.Switch.Apply(true);
        Assert.Contains(WindowNames.Vitals, h.Visible);
    }
}
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Add the window name** in `src/AcDream.App/UI/WindowNames.cs` after `JumpPowerbar`:

```csharp
    public const string CommandDeck = "command-deck";
```

- [ ] **Step 4: Implement**

```csharp
namespace AcDream.App.UI.Hud;

/// <summary>
/// Turns Modern HUD on and off. While active, the retail vitals, side
/// vitals, combat and jump windows cannot be shown (their controllers' show
/// requests go through <see cref="GateVisibility"/> / <see cref="GatedShow"/>)
/// and the Command Deck is shown while gameplay is on screen. Turning on is
/// refused, leaving retail untouched, when the deck did not mount.
/// </summary>
public sealed class ModernHudSwitch
{
    public static IReadOnlyList<string> RetailWindows { get; } =
    [
        WindowNames.Vitals,
        WindowNames.SideVitals,
        WindowNames.Combat,
        WindowNames.JumpPowerbar,
    ];

    private readonly Func<string, bool> _show;
    private readonly Func<string, bool> _hide;
    private readonly Func<bool> _deckMounted;
    private readonly Func<bool> _gameplayActive;
    private readonly Action _resyncRetail;
    private readonly Action _onDeckHidden;
    private readonly Action<string> _log;
    private bool? _requested;
    private bool _deckShown;
    private bool _reportedMountFailure;

    public ModernHudSwitch(
        Func<string, bool> show,
        Func<string, bool> hide,
        Func<bool> deckMounted,
        Func<bool> gameplayActive,
        Action resyncRetail,
        Action onDeckHidden,
        Action<string>? log = null)
    {
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _hide = hide ?? throw new ArgumentNullException(nameof(hide));
        _deckMounted = deckMounted ?? throw new ArgumentNullException(nameof(deckMounted));
        _gameplayActive = gameplayActive ?? throw new ArgumentNullException(nameof(gameplayActive));
        _resyncRetail = resyncRetail ?? throw new ArgumentNullException(nameof(resyncRetail));
        _onDeckHidden = onDeckHidden ?? throw new ArgumentNullException(nameof(onDeckHidden));
        _log = log ?? Console.WriteLine;
    }

    public bool Active { get; private set; }

    /// <summary>A retail controller's <c>setWindowVisible</c> delegate, gated.</summary>
    public Action<bool> GateVisibility(string window) => visible =>
    {
        if (!visible) _hide(window);
        else if (!Active) _show(window);
    };

    /// <summary>A show call that does nothing while active.</summary>
    public bool GatedShow(string window) => !Active && _show(window);

    public void Apply(bool enabled)
    {
        if (_requested == enabled) return;
        _requested = enabled;

        if (enabled)
        {
            if (!_deckMounted())
            {
                if (!_reportedMountFailure)
                {
                    _reportedMountFailure = true;
                    _log("[UI] modern HUD: Command Deck unavailable; keeping the retail windows.");
                }
                return;
            }
            Active = true;
            foreach (string window in RetailWindows) _hide(window);
            Tick();
            return;
        }

        if (!Active) return;
        Active = false;
        SetDeckShown(false);
        _resyncRetail();
    }

    /// <summary>Keeps the deck shown only while active and gameplay is on screen.</summary>
    public void Tick() => SetDeckShown(Active && _gameplayActive());

    private void SetDeckShown(bool shown)
    {
        if (shown == _deckShown) return;
        _deckShown = shown;
        if (shown)
        {
            _show(WindowNames.CommandDeck);
        }
        else
        {
            _hide(WindowNames.CommandDeck);
            _onDeckHidden();
        }
    }
}
```

Note `On_WithoutDeck_LeavesRetailAndLogsOnce`: the second `Apply(true)` follows an `Apply(false)` so `_requested` changed, and `_reportedMountFailure` keeps the log single.

- [ ] **Step 5: Run the filter** → PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/WindowNames.cs src/AcDream.App/UI/Hud/ModernHudSwitch.cs tests/AcDream.App.Tests/UI/Hud/ModernHudSwitchTests.cs
git commit -m "feat(hud): ModernHudSwitch gating and deck visibility

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 12: Wire it into the client

**Goal:** Mount the deck, gate the retail controllers, apply the setting live and at startup, and connect the actions.

**Files:**
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (bindings records ~line 60–360, constructor mount list ~line 506–530, `stateManagedVisibilityWindows` ~line 557, `MountVitals`/`MountSideVitals` ~1553–1620, `MountCombat` ~1983, `MountJumpPowerbar` ~3310, `Tick` ~768, `Dispose`)
- Modify: `src/AcDream.App/Composition/InteractionUiRuntimeSources.cs` (`DeferredGameRuntimeStateCommands`: add `Recall`)
- Modify: `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs` (Options bindings ~line 906, new `CommandDeck` bindings)
- Test: `tests/AcDream.App.Tests/UI/Hud/ModernHudWiringTests.cs`

**Acceptance Criteria:**
- [ ] With `ModernHud` off, retail behaviour is unchanged (full test run shows no new failures).
- [ ] `RetailUiRuntimeBindings` gains optional `CommandDeckRuntimeBindings? CommandDeck = null`; when null no deck mounts and the switch stays off.
- [ ] Combat, jump and side-vitals controllers receive gated show delegates; `MountCommandDeck` registers `command-deck` (Imported chrome, draggable, not resizable, hidden, bottom-centre) and it is in `stateManagedVisibilityWindows`.
- [ ] The switch is applied once at the end of construction (startup) and every `Tick` from `CommandDeck.Enabled()`; `Tick` calls `_hudSwitch.Tick()`.
- [ ] Off-path resync calls `CombatUiController.SyncVisibility()`, `JumpPowerbarController.SyncVisibility()` and `_vitalsSideBySide.SyncVisibility()`.
- [ ] Disposing the runtime calls `deck.Detach()` and disposes the icon cache.
- [ ] Config "Modern HUD" row saves to `misc.modernHud` and the deck appears/disappears on the next frame.

**Verify:** `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-command-deck` → 0 failures.

**Steps:**

- [ ] **Step 1: Bindings record** — add next to `JumpPowerbarRuntimeBindings`:

```csharp
public sealed record CommandDeckRuntimeBindings(
    Func<bool> Enabled,
    Action ToggleCombat,
    Action<AcDream.Runtime.RuntimePortalCommand> Recall);
```

and at the end of `RetailUiRuntimeBindings`'s parameter list:

```csharp
    WorldDragRuntimeBindings? WorldDrag = null,
    CommandDeckRuntimeBindings? CommandDeck = null);
```

- [ ] **Step 2: Fields and switch** in `RetailUiRuntime` (near the other controller fields):

```csharp
    private Hud.ModernHudSwitch? _hudSwitch;
    private Hud.UiCommandDeck? _commandDeck;
    private Hud.EmbeddedSvgSpriteCache? _hudIcons;
```

Create the switch at the start of the constructor's mount sequence, **before** `MountVitals()`:

```csharp
        _hudSwitch = new Hud.ModernHudSwitch(
            show: Host.ShowWindow,
            hide: Host.HideWindow,
            deckMounted: () => _commandDeck is not null,
            gameplayActive: () => _bindings.IsGameplayDisplay?.Invoke() ?? true,
            resyncRetail: () =>
            {
                CombatUiController?.SyncVisibility();
                JumpPowerbarController?.SyncVisibility();
                _vitalsSideBySide?.SyncVisibility();
            },
            onDeckHidden: () => _commandDeck?.ReleaseHeldAttack());
```

- [ ] **Step 3: Gate the retail controllers**

`MountCombat`, replace the visibility lambda passed to `CombatUiController.Bind` with:

```csharp
            _hudSwitch!.GateVisibility(WindowNames.Combat));
```

`MountJumpPowerbar`, same for `JumpPowerbarController.Bind`:

```csharp
            _hudSwitch!.GateVisibility(WindowNames.JumpPowerbar));
```

`MountSideVitals`, construct the side controller with the callback constructor:

```csharp
        _vitalsSideBySide = new Layout.VitalsSideBySideController(
            show: _hudSwitch!.GatedShow,
            hide: Host.HideWindow,
            sideBySideVitals: () => _bindings.Options.CurrentCharacterOption(
                (uint)CharacterOptionId.SideBySideVitals),
            stackedWindow: WindowNames.Vitals,
            sideWindow: WindowNames.SideVitals);
```

- [ ] **Step 4: `MountCommandDeck`** — call it right after `MountJumpPowerbar();` in the constructor, and add `WindowNames.CommandDeck` to the `stateManagedVisibilityWindows` array. The method:

```csharp
    private void MountCommandDeck()
    {
        if (_bindings.CommandDeck is not { } deckBindings) return;

        var actions = new CommandDeckActions(_bindings, deckBindings);
        _hudIcons = new Hud.EmbeddedSvgSpriteCache(new RetailPluginFontBackend(_bindings.Assets.TextureCache));
        var model = new Hud.CommandDeckModel();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _commandDeck = new Hud.UiCommandDeck(
            ReadCommandDeckInput,
            model,
            actions,
            _hudIcons,
            () => clock.Elapsed.TotalSeconds);

        RetailWindowFrame.Mount(
            Host.Root,
            _commandDeck,
            _bindings.Assets.ResolveSprite,
            new RetailWindowFrame.Options
            {
                WindowName = WindowNames.CommandDeck,
                Chrome = RetailWindowChrome.Imported,
                Left = Math.Max(0f, (Host.Root.Width - Hud.CommandDeckLayout.Width) * 0.5f),
                Top = Math.Max(0f, Host.Root.Height - Hud.CommandDeckLayout.Height - 20f),
                Resizable = false,
                ResizeX = false,
                ResizeY = false,
                Visible = false,
                ContentClickThrough = false,
            });
        Console.WriteLine("[UI] Command Deck mounted (Modern HUD).");
    }

    private Hud.CommandDeckInput ReadCommandDeckInput()
    {
        VitalsVM vm = _bindings.Vitals.ViewModel;
        RuntimeCombatAttackState attacks = _bindings.Combat.Attacks;
        RuntimeSpellCastState casting = _bindings.Magic.Casting;
        return new Hud.CommandDeckInput(
            Health: new(vm.HealthPercent, vm.HealthCurrent, vm.HealthMax),
            Stamina: new(vm.StaminaPercent, vm.StaminaCurrent, vm.StaminaMax),
            Mana: new(vm.ManaPercent, vm.ManaCurrent, vm.ManaMax),
            Mode: _bindings.Combat.State.CurrentMode,
            DesiredPower: attacks.DesiredPower,
            PowerBarLevel: attacks.PowerBarLevel,
            RequestedHeight: attacks.RequestedHeight,
            HasPendingCast: casting.HasPendingCast,
            PendingSpellName: SpellName(casting.PendingSpellId),
            LastSpellName: SpellName(casting.LastRequestedSpellId),
            Jump: _bindings.JumpPowerbar.Snapshot(),
            RepeatAttacks: _bindings.Options.CurrentCharacterOption((uint)CharacterOptionId.AutoRepeatAttack),
            AutoTarget: _bindings.Options.CurrentCharacterOption((uint)CharacterOptionId.AutoTarget),
            KeepInView: _bindings.Options.CurrentCharacterOption((uint)CharacterOptionId.ViewCombatTarget),
            SessionKey: _bindings.Magic.PlayerGuid());
    }

    private string? SpellName(uint? spellId) =>
        spellId is uint id
            ? _bindings.Magic.Spellbook.TryGetMetadata(id, out var meta) && !string.IsNullOrEmpty(meta.Name)
                ? meta.Name
                : $"Spell {id}"
            : null;
```

`CommandDeckActions` (nested private sealed class in `RetailUiRuntime`, or `src/AcDream.App/UI/Hud/CommandDeckActions.cs` taking the two binding records):

```csharp
    private sealed class CommandDeckActions(
        RetailUiRuntimeBindings bindings,
        CommandDeckRuntimeBindings deck) : Hud.ICommandDeckActions
    {
        public void ToggleCombat() => deck.ToggleCombat();
        public void PressAttack(AttackHeight height) => bindings.Combat.Attacks.PressAttack(height);
        public void ReleaseAttack() => bindings.Combat.Attacks.ReleaseAttack();
        public void SetDesiredPower(float power) => bindings.Combat.Attacks.SetDesiredPower(power);
        public void SetOption(CharacterOptionId option, bool value) =>
            bindings.Options.CommandBus().Publish(new SetSingleCharacterOptionRuntimeCmd((uint)option, value));
        public void Recall(AcDream.Runtime.RuntimePortalCommand command) => deck.Recall(command);
        public void Recast()
        {
            RuntimeSpellCastState casting = bindings.Magic.Casting;
            if (casting.HasPendingCast || casting.LastRequestedSpellId is not uint spell) return;
            if (casting.LastRequestedTargetId is uint target && target != 0)
                casting.Cast(spell, target);
            else
                casting.Cast(spell);
        }
    }
```

Check the exact names while wiring: `VitalsVM` namespace (`AcDream.UI.Abstractions.Panels.Vitals`, already imported), `SpellMetadata.Name` (`grep -n "record.*SpellMetadata\|class SpellMetadata" -r src/AcDream.Core/Spells`), and `RetailUiAssets.TextureCache` (used by `MountPlugins`' `RetailPluginFontBackend`). If `RetailWindowFrame.Options` has no `Resizable`, drop that line (keep `ResizeX/ResizeY = false`).

- [ ] **Step 5: Apply, tick, dispose**

End of the constructor's mount sequence (after the last `Mount*` call and after `_persistence` is created):

```csharp
        _hudSwitch.Apply(_bindings.CommandDeck?.Enabled() ?? false);
```

In `Tick`, right after `_persistence?.SetGameplayActive(...)`:

```csharp
        _hudSwitch?.Apply(_bindings.CommandDeck?.Enabled() ?? false);
        _hudSwitch?.Tick();
```

In `Dispose` (with the other controller teardown):

```csharp
        _commandDeck?.Detach();
        _hudIcons?.Dispose();
```

- [ ] **Step 6: `Recall` on the deferred runtime commands** — in `DeferredGameRuntimeStateCommands` (`InteractionUiRuntimeSources.cs`), next to `FellowshipCreate`:

```csharp
    public RuntimeCommandResult Recall(RuntimePortalCommand command) =>
        Invoke((commands, generation) => commands.Portal.Execute(generation, command));
```

- [ ] **Step 7: Composition** — in `InteractionRetainedUiComposition`, before building `OptionsRuntimeBindings`, create the shared flag:

```csharp
        bool modernHud = d.Settings.LayoutStore?.LoadMisc().ModernHud ?? false;
```

In the `OptionsRuntimeBindings(...)` call, after `ExtendedLevels: ...`:

```csharp
                    ModernHud: new ConfigOptionsPageController.ModernHudBindings(
                        Load: () => modernHud,
                        Save: value =>
                        {
                            modernHud = value;
                            if (d.Settings.LayoutStore is { } store)
                                store.SaveMisc(store.LoadMisc() with { ModernHud = value });
                        })),
```

(`OptionsRuntimeBindings` needs a matching optional `ConfigOptionsPageController.ModernHudBindings? ModernHud = null` parameter, passed into the Config page bindings the same way `ExtendedLevels` is at `RetailUiRuntime.cs` ~line 2884: `ModernHud = _bindings.Options.ModernHud,`.)

In the `RetailUiRuntimeBindings(...)` construction, add:

```csharp
                CommandDeck: new CommandDeckRuntimeBindings(
                    Enabled: () => modernHud,
                    ToggleCombat: d.CombatModeCommands.Toggle,
                    Recall: command => late.GameRuntime.Recall(command)),
```

- [ ] **Step 8: Wiring test** — `ModernHudWiringTests` checks the parts that are cheap to test without a full runtime: a `VitalsSideBySideController` built with `ModernHudSwitch.GatedShow` over a `UiRoot` (as in `VitalsSideBySideControllerTests.Windows()`) never shows either vitals window across repeated ticks and option flips while the switch is active, and shows the right one after `Apply(false)` + `SyncVisibility()`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ModernHudWiringTests
{
    [Fact]
    public void SideVitalsStayHiddenWhileActive_AndComeBackAfter()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var stacked = new UiPanel { Width = 160, Height = 58 };
        var side = new UiPanel { Width = 460, Height = 26, Visible = false };
        var deck = new UiPanel { Width = 420, Height = 150, Visible = false };
        root.AddChild(stacked); root.AddChild(side); root.AddChild(deck);
        root.RegisterWindow(WindowNames.Vitals, stacked, stacked, null);
        root.RegisterWindow(WindowNames.SideVitals, side, side, null);
        root.RegisterWindow(WindowNames.CommandDeck, deck, deck, null);

        bool sideBySide = false;
        VitalsSideBySideController? vitals = null;
        var hud = new ModernHudSwitch(
            root.ShowWindow, root.HideWindow,
            deckMounted: () => true,
            gameplayActive: () => true,
            resyncRetail: () => vitals!.SyncVisibility(),
            onDeckHidden: () => { });
        vitals = new VitalsSideBySideController(
            hud.GatedShow, root.HideWindow, () => sideBySide,
            WindowNames.Vitals, WindowNames.SideVitals);
        vitals.Tick();

        hud.Apply(true);
        for (int i = 0; i < 4; i++)
        {
            sideBySide = !sideBySide;
            vitals.Tick();
            Assert.False(stacked.Visible);
            Assert.False(side.Visible);
        }
        Assert.True(deck.Visible);

        sideBySide = true;
        hud.Apply(false);
        Assert.False(deck.Visible);
        Assert.False(stacked.Visible);
        Assert.True(side.Visible);
    }
}
```

- [ ] **Step 9: Build and run everything**

```bash
export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet
dotnet build AcDream.slnx -c Debug 2>&1 | tail -3
~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-command-deck --no-build
```
Expected: `Build succeeded.`, 0 failures.

- [ ] **Step 10: Commit**

```bash
git add -A src tests
git commit -m "feat(hud): mount the Command Deck behind the Modern HUD setting

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 13: Live check (user gate)

**Goal:** Confirm the deck works in the real client on the local ACE server.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:** none

**Acceptance Criteria:**
- [ ] The user said they are available before any client was launched.
- [ ] With Modern HUD off: retail vitals, combat, jump bar and spell bar behave as before.
- [ ] Turning Modern HUD on in Options → Config hides those windows and shows the deck immediately; off restores them (combat window back if in combat).
- [ ] Peace: recall studs each send their recall (server message or teleport).
- [ ] Melee: power drag and wheel change power; H/M/L hold-and-release attacks; studs toggle and match the retail options.
- [ ] Magic: casting name shows while casting; RECAST recasts the last spell; no retail spell bar (accepted).
- [ ] Jump strip fills in every mode; damage trail on taking damage; low-health pulse.
- [ ] Deck height never changes across modes; icons and text are sharp on the Retina display.
- [ ] Position is kept across relog; client restart with Modern HUD on never flashes the retail windows.

**Verify:** user confirms each item in the live client; screenshots captured with the probe-script backbuffer capture (see memory `retina-capture-workflow`).

**Steps:**

- [ ] **Step 1:** Ask the user whether they are available for a live test. Do not launch anything until they say yes.
- [ ] **Step 2:** Build the dev root and launch against 10.10.20.20 using `~/OpenAC-dev/dev-client.env` (see memory `live-testing-rules`, `local-dotnet-and-vulkan-env`).
- [ ] **Step 3:** Walk the checklist above with the user, capturing a 2x screenshot per mode.
- [ ] **Step 4:** Fix anything found (each fix its own commit with a test where possible), re-run the full test script, and repeat the affected checks.
