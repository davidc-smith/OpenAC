# RynthAi Combat and Buffs Live (RynthAi sub-project #3) Implementation Plan

> **Status (2026-10-18):** Tasks 1–4 done and reviewed, plus a final whole-branch review and one fix wave (1c600393): the retail no-components line, CastSpell answers 1 unless Unavailable, and status lines are delivered once per tick. Task 5 (live check) not yet run.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RynthAi's combat manager and buff manager work against a live server. The RynthCore table fills the cast, cast-gate, attack, cancel, target-vitals, use-on and object-enchantment slots. The host raises the callbacks RynthAi's combat and buffing read: busy count, selected target, combat mode, combat damage, enchantments, absolute health, and client status lines.

**Architecture:** This follows the pattern of #2:
- `RynthApiTable` (a new partial, `RynthApiTable.Combat.cs`) only marshals and catches.
- `IRynthGameView` grows members in RynthCore's terms.
- `RuntimeRynthGameView` translates over `GameRuntime` and `IAutomationSurface`.

Four small OpenAC seams supply what the runtime lacks:
- A cast at an explicit target that leaves the selection alone.
- A public "motions pending" read on the player's movement state.
- A creature-vitals record on `CombatState`, filled from appraisals.
- A `StatusTextShown` event for client status lines, which never enter the chat log.

A new `RynthCombatEvents` class subscribes to the runtime events and forwards them through `RynthEventBridge` to new `RynthPlugin` export calls.

**Tech Stack:** .NET 10 (`~/.dotnet/dotnet`), xunit v3 (VSTest runner: `--filter "FullyQualifiedName~X"`), RynthCore/RynthSuite submodules (read only).

**Spec:** No separate spec file. As for #2, the user asked to go straight from the agreed design to this plan. The design was agreed in conversation on 2026-10-18 and is recorded under "User decisions" and "Design" below. RynthAi sub-project split:
- #1 compile + dashboard (done)
- #2 state + basic actions (done)
- **#3 this plan**
- #4 navigation + Nav3D
- #5 loot/salvage/vendor/trade
- #6 dat raycasting and dungeon pathfinder

## Global Constraints

- Repo `~/code/OpenAC/OpenAC-Rynth`. Create branch `feat/rynth-ai-combat` from `main` before Task 1. Do not push.
- Use `~/.dotnet/dotnet` (the system `dotnet` lacks SDK 10.0.300).
- **Never edit, copy or move anything inside `external/RynthCore` or `external/RynthSuite`.** `git status --porcelain external` must stay empty.
- `AcDream.Core`, `AcDream.Core.Net`, `AcDream.Runtime`, `AcDream.App`, `AcDream.ClientExtensions` and `AcDream.Rynth.Host` build with warnings as errors and analyzers on. New code there must build clean.
- **Table rule:** every `[UnmanagedCallersOnly]` table function catches every exception, logs through `Fail`, and returns RynthCore's "not available" value. An exception crossing that boundary ends the process.
- **Plugin API is untouched:** no change to `AcDream.Plugin.Abstractions`. `IAutomationSurface` is only consumed.
- `RynthApiTable.ApiVersion` stays **86**.
- **Slots still deliberately left unset:**
  - `NativeAttackFn`: RynthAi uses it only when `UseNativeAttack` is on, and that defaults to off.
  - `SetMotionFn`, `TurnToHeadingFn`, `StopCompletelyFn`, `SetAutoRunFn`, `StopMovementFn`, `GetObjectStateFn`, `GetObjectMotionOnFn`, `GetViewportSizeFn`, `WorldToScreenFn`, `GetRadarRectFn`: #4.
  - `MoveItemExternalFn`, `GetObjectPalettesFn`, `GetNumContainedItemsFn`, `GetNumContainedContainersFn`: #5.
  - Unused by RynthAi: `IsPlayerReadyFn`, `SetSelectedObjectIdFn`, `GetPreviousSelectedItemIdFn`, `GetEngineStatusJsonFn`, `GetVTankStateFn`, `GetLastIdTimeFn`, `SetPowerbarSuppressedFn`.
- All table functions and all forwarded events run on the game update thread. That is the thread `IAutomationSurface` and the runtime objects require.
- Rynth host tests run without parallelism (assembly attribute in `FakeGameView.cs`). App tests that touch Rynth use `[Collection("Rynth")]`. Only one `RynthApiTable` can be live at a time, so each test disposes its `RynthHost` or extension.
- Known unrelated failures exist in `AcDream.App.Tests` (dat files, live server). Run App tests with the filters each task's Verify shows. `AcDream.Rynth.Host.Tests` must be fully green.
- Never launch a client without asking the user first. Only the local ACE server 10.10.20.20, never Dreamweave. Never print passwords (`~/OpenAC-dev/dev-client.env`).

**User decisions (already made):**
- Scope: "Core + events + kits". CastSpell, CastBusyState, Melee/Missile/CancelAttack, TargetVitals, UseObjectOn, plus the missing events (busy count, absolute health, selected target, combat damage, enchantments). Facing (SetMotion/TurnToHeading/StopCompletely) stays in #4.
- Attack path: "Direct send, like RynthCore". Send 0x08/0x0A/0x1B7 at once with RynthAi's power, after the same body-stop OpenAC's own attack does. No power bar.
- Cast target: "No: cast at the given target". Add an explicit-target overload to `RuntimeSpellCastState.Cast` that runs the same checks against the given id without touching the selection.
- The slot mapping (design Section 1 below) was approved. The user then asked to go straight to the implementation plan ("Yes go to implementation plan"), so the events and chat sections are recorded here without a separate approval round.
- Standing rule: run their code unchanged ("we cant make changes to their code, and we need to be able to pull in their latest daily changes").

## Design

### Slots (approved)

| Slot | View member | Source |
|---|---|---|
| `CastSpellFn(target, spell)` | `CastSpell` | `ActionOwner.SpellCast.Cast(spellId, targetId)` (new overload). Same known/components/compatible checks, body-stop, busy increment and receipt bookkeeping as the selection cast. A self or untargeted spell ignores the target. Refused while portaling. 1 only when `Sent`. |
| `GetCastBusyStateFn` | `IsCastGestureActive` | `MovementOwner.HasPendingMotions` (new) = the player's `Motion.MotionsPending()`, as RynthCore's `CastGate`. 0 with no body. |
| `MeleeAttackFn` / `MissileAttackFn` | `MeleeAttack` / `MissileAttack` | `PrepareLocalPlayerForAttackRequest()`, then `session.SendMeleeAttack` / `SendMissileAttack`. The table refuses height outside 1–3 and clamps power to 0–1. |
| `CancelAttackFn` | `CancelAttack` | If OpenAC's attack state holds anything (`AttackServerResponsePending`, `AttackRequestInProgress` or `RepeatAttackInProgress`), `CombatAttack.AbortAutomaticAttack()`, which sends the cancel and clears that state. Otherwise `session.SendCancelAttack()`. |
| `GetTargetVitalsFn` | `TryGetTargetVitals` | `Combat.TryGetCreatureVitals` (new), filled from appraisal CreatureProfiles. Stamina and mana are 0 when the appraisal did not show them. |
| `UseObjectOnFn` | `UseObjectOn` | `Items.Apply(source, target).Accepted`. |
| `ReadObjectEnchantmentsFn` | `GetObjectEnchantments` | The player: the same list as `PlayerEnchantments`. Anything else: null, which the table returns as −1 (RynthCore's "not found"). OpenAC keeps no other enchantment registry. |

### Events

| RynthCore export | Raised from | Translation |
|---|---|---|
| `RynthPluginOnBusyCountIncremented` / `Decremented` | `InventoryOwner.Transactions.StateChanged` | Diff `BusyCount` against the last value and raise one event per step (at most 64), as RynthCore raises one per `Increment`/`DecrementBusyCount`. |
| `RynthPluginOnSelectedTargetChange(cur, prev)` | `ActionOwner.Selection.Changed` | null → 0. Not raised when cur == prev (`Reset` fires null→null). |
| `RynthPluginOnCombatModeChange(cur, prev)` | `ActionOwner.Combat.CombatModeChanged` | `CombatMode` values already are 1/2/4/8. The previous value is tracked. Not raised when unchanged. |
| `RynthPluginOnCombatDamage(dmg, type, crit, isAttacker)` | `Combat.DamageDealtAccepted` (1) and `Combat.DamageTaken` (0) | Raw wire damage type; crit as 0/1. Skipped when damage is 0 or above 1,000,000, as RynthCore does. |
| `RynthPluginOnEnchantmentAdded(spell, seconds)` | `Spellbook.EnchantmentAdded` | `SpellId & 0xFFFF`, `Duration` as on the wire (−1 permanent). |
| `RynthPluginOnEnchantmentRemoved(spell)` | `Spellbook.EnchantmentRemoved` | `SpellId & 0xFFFF`. Cooldown-bucket records are skipped: OpenAC also removes an expired cooldown locally, and RynthCore never reports that. |
| `RynthPluginOnUpdateHealth(id, ratio, cur, max)` (exists) | `Combat.HealthChanged` | max = the creature's appraised `HealthMax` when known, and cur = round(max × ratio). Both are 0 when unknown, as RynthCore computes them. |
| `RynthPluginOnChatWindowText(text, 0x1A)` (exists) | `CommunicationOwner.StatusTextShown` (new) | Client status lines ("You're too busy!", missing components, not enough mana) go to OpenAC's floating text and never enter the chat log. In retail RynthCore sees them in the chat window, and RynthAi's buff and combat code matches on them. |
| Login push | `RynthEventBridge.LoggedIn` (new, raised after `OnLoginComplete`) | `OnCombatModeChange(current, 1)` always, and `OnSelectedTargetChange(selected, 0)` when something is selected, as RynthCore's `PluginManager` does. |

The existing `HealthChanged` subscription moves out of `RynthClientExtension` into `RynthCombatEvents`. `KillLanded` and the rest stay where they are.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AcDream.Runtime/Gameplay/RuntimeSpellCastState.cs` | modify | `Cast(spellId, targetId)` without selection |
| `src/AcDream.Runtime/Gameplay/RuntimeLocalPlayerMovementState.cs` | modify | `HasPendingMotions` |
| `src/AcDream.Core/Combat/CombatState.cs` | modify | `CreatureVitals`, `OnCreatureProfile`, `TryGetCreatureVitals`, cleared in `Clear` |
| `src/AcDream.Core.Net/GameEventWiring.cs` | modify | the appraisal handler records the profile (~865) |
| `src/AcDream.Runtime/Gameplay/RuntimeCommunicationState.cs` | modify | `StatusTextShown` |
| `src/AcDream.Rynth.Host/IRynthGameView.cs` | modify | combat members |
| `src/AcDream.Rynth.Host/RynthTranslate.cs` | modify | `ToAttackHeight` |
| `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs` | modify | implements the combat members |
| `src/AcDream.Rynth.Host/RynthApiTable.Combat.cs` | create | the combat slots |
| `src/AcDream.Rynth.Host/RynthApiTable.cs` | modify | calls `BindCombatSlots()` |
| `src/AcDream.Rynth.Host/RynthPlugin.cs` | modify | the seven new export calls |
| `src/AcDream.Rynth.Host/RynthEventBridge.cs` | modify | forwarding methods, `LoggedIn`, health with current/max |
| `src/AcDream.Rynth.Host/RynthCombatEvents.cs` | create | runtime events → bridge |
| `src/AcDream.Rynth.Host/RynthClientExtension.cs` | modify | owns a `RynthCombatEvents`; health moves there |
| `tests/AcDream.Runtime.Tests/Gameplay/RuntimeSpellCastStateTests.cs` | modify | explicit-target casts |
| `tests/AcDream.Runtime.Tests/Gameplay/RuntimeLocalPlayerMovementStateTests.cs` | modify | `HasPendingMotions` |
| `tests/AcDream.Core.Tests/Combat/CombatStateTests.cs` | modify | creature vitals |
| `tests/AcDream.Core.Net.Tests/GameEventWiringTests.cs` | modify | the appraisal stores vitals |
| `tests/AcDream.Runtime.Tests/Gameplay/RuntimeCommunicationStateTests.cs` | modify | `StatusTextShown` |
| `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs` | modify | combat members |
| `tests/AcDream.Rynth.Host.Tests/RynthApiTableTests.cs` | modify | combat slots |
| `tests/AcDream.Rynth.Host.Tests/RynthTranslateTests.cs` | modify | `ToAttackHeight` |
| `tests/AcDream.App.Tests/Extensions/FakeRynthAutomation.cs` | modify | `FakeItems.Apply` records |
| `tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs` | modify | combat reads and actions over a real runtime |
| `tests/AcDream.App.Tests/Extensions/RynthCombatEventsTests.cs` | create | events reach a recorder plugin in RynthCore's terms |

---

### Task 1: Runtime cast seams (explicit-target cast, pending motions)

**Goal:** `RuntimeSpellCastState.Cast(spellId, targetId)` casts at the given id without reading or changing the selection. `RuntimeLocalPlayerMovementState.HasPendingMotions` reports whether the player's motion queue is non-empty.

**Files:**
- Modify: `src/AcDream.Runtime/Gameplay/RuntimeSpellCastState.cs:79-168`
- Modify: `src/AcDream.Runtime/Gameplay/RuntimeLocalPlayerMovementState.cs` (next to `IsInReadyPosition`, ~415)
- Test: `tests/AcDream.Runtime.Tests/Gameplay/RuntimeSpellCastStateTests.cs`, `tests/AcDream.Runtime.Tests/Gameplay/RuntimeLocalPlayerMovementStateTests.cs`

**Acceptance Criteria:**
- [ ] `Cast(1, 77)` for a targeted spell sends a targeted cast to 77, leaves the selection at 99, increments busy once and sets `PendingTargetId` to 77. A later `CompleteUse(0)` records `LastCompletion.TargetObjectId == 77`.
- [ ] `Cast(1, 0)` for a targeted spell returns `NoTarget` even with a selection, and sends nothing.
- [ ] A self-targeted spell sent through `Cast(1, 77)` goes to the local player.
- [ ] An incompatible explicit target returns `IncompatibleTarget`.
- [ ] `Cast(uint)` behaves as before (existing tests green).
- [ ] `HasPendingMotions` is false with no controller. It is true after `controller.Motion.AddToQueue(...)` and false again after `MotionDone`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~RuntimeSpellCastStateTests|FullyQualifiedName~RuntimeLocalPlayerMovementStateTests"` → all pass

**Steps:**

- [ ] **Step 1: Branch**

```bash
git switch -c feat/rynth-ai-combat main
```

- [ ] **Step 2: Write the failing cast tests** (append to `RuntimeSpellCastStateTests`, before the `Create` helper)

```csharp
    [Fact]
    public void CastAtTarget_SendsToThatTargetAndLeavesTheSelectionAlone()
    {
        Spellbook book = MakeBook(flags: 0, untargeted: false, targetMask: 0x10);
        uint sentTo = 0;
        var operations = new FakeOperations
        {
            LocalPlayerId = 42u,
            SendTargetedAction = (id, _) => sentTo = id,
        };
        var selection = new SelectionState();
        selection.Select(99u, SelectionChangeSource.System);
        var state = new RuntimeSpellCastState(book, selection, operations);

        Assert.Equal(CastRequestResult.Sent, state.Cast(1, 77u));
        Assert.Equal(77u, sentTo);
        Assert.Equal(99u, selection.SelectedObjectId);
        Assert.Equal(1, operations.BusyIncrements);
        Assert.Equal(77u, state.PendingTargetId);

        Assert.True(state.CompleteUse(0u));
        Assert.Equal(77u, state.LastCompletion.TargetObjectId);
    }

    [Fact]
    public void CastAtTarget_ZeroTargetForATargetedSpellIsNoTargetEvenWithASelection()
    {
        Spellbook book = MakeBook(flags: 0, untargeted: false, targetMask: 0x10);
        var operations = new FakeOperations { LocalPlayerId = 42u };
        RuntimeSpellCastState state = Create(book, operations, selected: 99u);

        Assert.Equal(CastRequestResult.NoTarget, state.Cast(1, 0u));
        Assert.Equal(0, operations.TargetedSends);
    }

    [Fact]
    public void CastAtTarget_SelfSpellStillGoesToThePlayer()
    {
        Spellbook book = MakeBook(flags: 0x8, untargeted: true, targetMask: 0x10);
        uint sentTo = 0;
        var operations = new FakeOperations
        {
            LocalPlayerId = 42u,
            SendTargetedAction = (id, _) => sentTo = id,
        };
        RuntimeSpellCastState state = Create(book, operations);

        Assert.Equal(CastRequestResult.Sent, state.Cast(1, 77u));
        Assert.Equal(42u, sentTo);
    }

    [Fact]
    public void CastAtTarget_IncompatibleTargetIsRefused()
    {
        Spellbook book = MakeBook(flags: 0, untargeted: false, targetMask: 0x10);
        var operations = new FakeOperations { LocalPlayerId = 42u, TargetCompatible = false };
        RuntimeSpellCastState state = Create(book, operations);

        Assert.Equal(CastRequestResult.IncompatibleTarget, state.Cast(1, 77u));
        Assert.Equal(0, operations.TargetedSends);
    }
```

- [ ] **Step 3: Write the failing movement test** (append to `tests/AcDream.Runtime.Tests/Gameplay/RuntimeLocalPlayerMovementStateTests.cs`. The file already builds `PlayerMovementController(new PhysicsEngine())` and reads `controller.Motion`. Match the `MotionCommand` casts used in `DispatchedMotionDrainTests`.)

```csharp
    [Fact]
    public void HasPendingMotionsFollowsTheControllersMotionQueue()
    {
        using var bodiless = new RuntimeLocalPlayerMovementState();
        Assert.False(bodiless.HasPendingMotions);

        var controller = new PlayerMovementController(new PhysicsEngine());
        using var movement = new RuntimeLocalPlayerMovementState
        {
            Controller = controller,
        };
        Assert.False(movement.HasPendingMotions);

        controller.Motion.AddToQueue(0u, (uint)MotionCommand.Ready, 0u);
        Assert.True(movement.HasPendingMotions);

        controller.Motion.MotionDone((uint)MotionCommand.Ready, success: true);
        Assert.False(movement.HasPendingMotions);
    }
```

- [ ] **Step 4: Run them and see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~CastAtTarget|FullyQualifiedName~HasPendingMotions"`
Expected: build error. `Cast(uint, uint)` and `HasPendingMotions` do not exist.

- [ ] **Step 5: Implement the explicit-target cast.** In `RuntimeSpellCastState`, rename the existing `public CastRequestResult Cast(uint spellId)` to `private CastRequestResult CastAt(uint spellId, uint? candidateTarget)`. In its final `else` branch, replace `target = _selection.SelectedObjectId;` with `target = candidateTarget;`. Nothing else in the body changes. Then add the two public entry points above it:

```csharp
    public CastRequestResult Cast(uint spellId) =>
        CastAt(spellId, _selection.SelectedObjectId);

    /// <summary>
    /// Casts at <paramref name="targetId"/> without reading or changing the
    /// selection, as the original client's FreeHandsAndCastSpell does for a
    /// plugin. A self or untargeted spell ignores the target, as
    /// <see cref="Cast(uint)"/> ignores the selection; 0 is no target.
    /// </summary>
    public CastRequestResult Cast(uint spellId, uint targetId) =>
        CastAt(spellId, targetId == 0u ? null : targetId);
```

- [ ] **Step 6: Implement `HasPendingMotions`** (in `RuntimeLocalPlayerMovementState`, just above `IsInReadyPosition`)

```csharp
    /// <summary>
    /// True while the character's body still has motions queued: a cast or
    /// action gesture is playing. The original client's cast gate reads the
    /// same queue. False with no body.
    /// </summary>
    public bool HasPendingMotions
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _controller?.Motion.MotionsPending() == true;
        }
    }
```

- [ ] **Step 7: Run the task's Verify command**

Run: `~/.dotnet/dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~RuntimeSpellCastStateTests|FullyQualifiedName~RuntimeLocalPlayerMovementStateTests"`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Runtime/Gameplay/RuntimeSpellCastState.cs src/AcDream.Runtime/Gameplay/RuntimeLocalPlayerMovementState.cs tests/AcDream.Runtime.Tests/Gameplay/RuntimeSpellCastStateTests.cs tests/AcDream.Runtime.Tests/Gameplay/RuntimeLocalPlayerMovementStateTests.cs
git commit -m "feat(runtime): cast at an explicit target; expose pending motions"
```

---

### Task 2: Creature vitals from appraisals; status-text event

**Goal:** An appraisal's CreatureProfile is kept on `CombatState` as the creature's last known vitals, and its health ratio still applies as before. Every client status line shown (`RetailLogTextType.ClientLocal`, not filtered out) raises `RuntimeCommunicationState.StatusTextShown`.

**Files:**
- Modify: `src/AcDream.Core/Combat/CombatState.cs` (fields ~8, `Clear` ~143)
- Modify: `src/AcDream.Core.Net/GameEventWiring.cs:865-871`
- Modify: `src/AcDream.Runtime/Gameplay/RuntimeCommunicationState.cs` (`AddText` ~176)
- Test: `tests/AcDream.Core.Tests/Combat/CombatStateTests.cs`, `tests/AcDream.Core.Net.Tests/GameEventWiringTests.cs:188-217`, `tests/AcDream.Runtime.Tests/Gameplay/RuntimeCommunicationStateTests.cs`

**Acceptance Criteria:**
- [ ] `OnCreatureProfile(guid, vitals)` with `HealthMax > 0` stores the vitals, and raises `HealthChanged(guid, Health/HealthMax)` as `OnUpdateHealth` does. `HealthMax == 0` is ignored.
- [ ] `TryGetCreatureVitals` returns the stored vitals. `Clear()` empties them.
- [ ] Dispatching an IdentifyObjectResponse with a creature profile (75/100, no attribute flag) leaves `TryGetCreatureVitals(guid)` = (75, 100, null, null, null, null) and health 0.75.
- [ ] `AddText(" You're too busy! ", ClientLocal)` raises `StatusTextShown("You're too busy!")`. Other text types never raise it. A filtered-out status line raises nothing.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~CombatStateTests" && ~/.dotnet/dotnet test tests/AcDream.Core.Net.Tests --filter "FullyQualifiedName~GameEventWiringTests" && ~/.dotnet/dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~RuntimeCommunicationStateTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing CombatState tests** (append to `CombatStateTests`)

```csharp
    [Fact]
    public void CreatureProfile_IsKeptAndAppliesItsHealthRatio()
    {
        var combat = new CombatState();
        var seen = new List<(uint, float)>();
        combat.HealthChanged += (guid, ratio) => seen.Add((guid, ratio));
        var vitals = new CombatState.CreatureVitals(75u, 100u, 40u, 80u, null, null);

        combat.OnCreatureProfile(0x80000010u, vitals);

        Assert.True(combat.TryGetCreatureVitals(0x80000010u, out CombatState.CreatureVitals kept));
        Assert.Equal(vitals, kept);
        Assert.Equal([(0x80000010u, 0.75f)], seen);
        Assert.Equal(0.75f, combat.GetHealthPercent(0x80000010u), 3);
        Assert.False(combat.TryGetCreatureVitals(0x80000011u, out _));
    }

    [Fact]
    public void CreatureProfile_WithoutAMaximumIsIgnoredAndClearForgetsProfiles()
    {
        var combat = new CombatState();
        combat.OnCreatureProfile(0x80000010u, new CombatState.CreatureVitals(5u, 0u, null, null, null, null));
        Assert.False(combat.TryGetCreatureVitals(0x80000010u, out _));
        Assert.False(combat.HasHealth(0x80000010u));

        combat.OnCreatureProfile(0x80000010u, new CombatState.CreatureVitals(5u, 10u, null, null, null, null));
        combat.Clear();
        Assert.False(combat.TryGetCreatureVitals(0x80000010u, out _));
    }
```

- [ ] **Step 2: Extend the wiring test.** At the end of `WireAll_LiteralAceCreatureFlag_ForwardsAndUpdatesHealth`, add:

```csharp
        Assert.True(combat.TryGetCreatureVitals(guid, out CombatState.CreatureVitals vitals));
        Assert.Equal(new CombatState.CreatureVitals(75u, 100u, null, null, null, null), vitals);
```

- [ ] **Step 3: Write the failing status-text tests** (append to `RuntimeCommunicationStateTests`)

```csharp
    [Fact]
    public void AddText_ClientLocal_RaisesStatusTextShownWithTheTrimmedText()
    {
        using var state = new RuntimeCommunicationState();
        var shown = new List<string>();
        state.StatusTextShown += shown.Add;

        state.AddText("  You're too busy!  ", RetailLogTextType.ClientLocal);
        state.AddText("Your spell fizzled.", RetailLogTextType.Magic);

        Assert.Equal(["You're too busy!"], shown);
    }
```

Also add a filtered case. Find how this file (or `RuntimeCommunicationChatProjectionTests`) installs a chat filter that suppresses `PluginChatMessage.StatusTextKind`, and copy that setup. Then assert `shown` stays empty after `AddText("Out of Range!", RetailLogTextType.ClientLocal)`. If no test in the repo installs such a filter, skip this case and say so in the report.

- [ ] **Step 4: Run them and see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~CombatStateTests"`
Expected: build error. `CreatureVitals`, `OnCreatureProfile` and `TryGetCreatureVitals` do not exist.

- [ ] **Step 5: Implement on `CombatState`.** Add next to `_healthByGuid`:

```csharp
    private readonly ConcurrentDictionary<uint, CreatureVitals> _vitalsByGuid = new();
```

Add the record next to the other record structs:

```csharp
    /// <summary>
    /// A creature's vitals as its last appraisal reported them. Stamina and
    /// mana are null unless the appraisal showed attributes.
    /// </summary>
    public readonly record struct CreatureVitals(
        uint Health,
        uint HealthMax,
        uint? Stamina,
        uint? StaminaMax,
        uint? Mana,
        uint? ManaMax);
```

Add after `OnUpdateHealth`:

```csharp
    /// <summary>
    /// An appraisal's creature profile: kept as the creature's last known
    /// vitals, and its health share applied as a health update.
    /// </summary>
    public void OnCreatureProfile(uint targetGuid, in CreatureVitals vitals)
    {
        if (vitals.HealthMax == 0u)
            return;

        _vitalsByGuid[targetGuid] = vitals;
        OnUpdateHealth(
            targetGuid,
            Math.Clamp((float)vitals.Health / vitals.HealthMax, 0f, 1f));
    }

    /// <summary>The vitals of the creature's last appraisal; false when none was seen.</summary>
    public bool TryGetCreatureVitals(uint guid, out CreatureVitals vitals) =>
        _vitalsByGuid.TryGetValue(guid, out vitals);
```

In `Clear()`, after `_healthByGuid.Clear();`, add `_vitalsByGuid.Clear();`.

- [ ] **Step 6: Record the profile in the wiring.** Replace `GameEventWiring.cs:865-871`:

```csharp
            if (p.Value.CreatureProfile is { HealthMax: > 0u } creature)
                combat.OnCreatureProfile(
                    p.Value.Guid,
                    new CombatState.CreatureVitals(
                        creature.Health,
                        creature.HealthMax,
                        creature.Stamina,
                        creature.StaminaMax,
                        creature.Mana,
                        creature.ManaMax));
```

- [ ] **Step 7: Raise `StatusTextShown`.** In `RuntimeCommunicationState`, add next to `LocalPlayerDied`:

```csharp
    /// <summary>
    /// A client status line (<see cref="RetailLogTextType.ClientLocal"/>) was
    /// shown. Those lines never enter the chat log, so a listener that needs
    /// what the player saw ("You're too busy!") takes them from here.
    /// </summary>
    public event Action<string>? StatusTextShown;
```

In `AddText`'s ClientLocal branch, change `SpewBox.Enqueue(text); return;` to:

```csharp
            SpewBox.Enqueue(text);
            StatusTextShown?.Invoke(text);
            return;
```

- [ ] **Step 8: Run the task's Verify command.** Expected: all pass.

- [ ] **Step 9: Commit**

```bash
git add src/AcDream.Core/Combat/CombatState.cs src/AcDream.Core.Net/GameEventWiring.cs src/AcDream.Runtime/Gameplay/RuntimeCommunicationState.cs tests/AcDream.Core.Tests/Combat/CombatStateTests.cs tests/AcDream.Core.Net.Tests/GameEventWiringTests.cs tests/AcDream.Runtime.Tests/Gameplay/RuntimeCommunicationStateTests.cs
git commit -m "feat(core): keep appraised creature vitals; raise shown status lines"
```

---

### Task 3: Combat and buff table slots

**Goal:** The table fills `CastSpellFn`, `GetCastBusyStateFn`, `MeleeAttackFn`, `MissileAttackFn`, `CancelAttackFn`, `GetTargetVitalsFn`, `UseObjectOnFn` and `ReadObjectEnchantmentsFn` from new `IRynthGameView` members. `RuntimeRynthGameView` serves them as the Design's slot table says.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthApiTable.Combat.cs`
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.cs:71-72` (bind call), `src/AcDream.Rynth.Host/IRynthGameView.cs`, `src/AcDream.Rynth.Host/RynthTranslate.cs`, `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`, `tests/AcDream.Rynth.Host.Tests/RynthApiTableTests.cs`, `tests/AcDream.Rynth.Host.Tests/RynthTranslateTests.cs`, `tests/AcDream.App.Tests/Extensions/FakeRynthAutomation.cs`, `tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs`

**Acceptance Criteria:**
- [ ] The SDK's `HasCastSpell`, `HasGetCastBusyState`, `HasMeleeAttack`, `HasMissileAttack`, `HasCancelAttack`, `HasUseObjectOn` and `HasReadObjectEnchantments` are true. `GetTargetVitalsFn` is set. `NativeAttackFn`, `SetMotionFn`, `TurnToHeadingFn` and `StopCompletelyFn` stay zero.
- [ ] Through the SDK:
  - `CastSpell`, `MeleeAttack`, `MissileAttack`, `CancelAttack` and `UseObjectOn` reach the view with their arguments, with power clamped to 0–1.
  - `GetCastBusyState` is 1 while the view reports a gesture.
  - `TryGetTargetVitals` returns the view's vitals.
  - `ReadObjectEnchantments(player)` returns the player's list, and another id returns −1.
- [ ] Spell id ≤ 0, height outside 1–3, target 0 for an attack, and either id 0 for use-on are all refused without calling the view.
- [ ] When the view throws, every new slot returns its not-available value (0/false/−1) and logs `"<Slot> threw"`.
- [ ] Over a real runtime:
  - `IsCastGestureActive` is false with no body.
  - `TryGetTargetVitals` reads `CombatState`'s appraised vitals, with null stamina/mana as 0.
  - `GetObjectEnchantments` is null for non-players.
  - Attacks, cancel and cast return false without a session.
  - `UseObjectOn` goes through `Items.Apply`.
  - `CastSpell` is false when the surface is unavailable.
- [ ] `AcDream.Rynth.Host.Tests` fully green.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests && ~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RuntimeRynthGameViewTests"` → all pass

**Steps:**

- [ ] **Step 1: Update the slot-coverage tests.** In `RynthApiTableTests.NewTable_HasApiVersionAndOnlyTheImplementedSlots`, change `Assert.False(host.HasCastSpell);` to `Assert.True(host.HasCastSpell);`. In `DeferredSlotsStayUnset`, delete the three lines for `GetCastBusyStateFn`, `GetTargetVitalsFn` and `ReadObjectEnchantmentsFn`, and add:

```csharp
        Assert.Equal(IntPtr.Zero, table.Native->NativeAttackFn);
        Assert.Equal(IntPtr.Zero, table.Native->SetMotionFn);
        Assert.Equal(IntPtr.Zero, table.Native->TurnToHeadingFn);
        Assert.Equal(IntPtr.Zero, table.Native->StopCompletelyFn);
```

- [ ] **Step 2: Write the failing table tests** (append to `RynthApiTableTests`)

```csharp
    [Fact]
    public void CombatSlots_CallTheGameView()
    {
        const uint player = 0x50000001;
        var view = new FakeGameView
        {
            PlayerId = player,
            CastGesture = true,
            Enchantments = [new RynthEnchantment(2, 1_300.0), new RynthEnchantment(3, double.MaxValue)],
        };
        view.TargetVitals[0x80000010] = new RynthVitals(75, 100, 40, 80, 0, 0);
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.HasGetCastBusyState);
        Assert.True(host.HasMeleeAttack);
        Assert.True(host.HasMissileAttack);
        Assert.True(host.HasCancelAttack);
        Assert.True(host.HasUseObjectOn);
        Assert.True(host.HasReadObjectEnchantments);
        Assert.NotEqual(IntPtr.Zero, table.Native->GetTargetVitalsFn);

        Assert.Equal(1, host.GetCastBusyState());
        Assert.False(host.CanCastNow);
        Assert.True(host.CastSpell(0x80000010, 1234));
        Assert.True(host.CastSpell(player, 2));
        Assert.True(host.MeleeAttack(0x80000010, 2, 0.75f));
        Assert.True(host.MissileAttack(0x80000010, 1, 1.5f));   // clamped to 1
        Assert.True(host.CancelAttack());
        Assert.True(host.UseObjectOn(0x80000020, player));
        Assert.Equal(
            new[]
            {
                "cast 80000010 1234", "cast 50000001 2", "melee 80000010 2 0.75", "missile 80000010 1 1.00",
                "cancel", "useon 80000020 50000001",
            },
            view.Actions);

        Assert.True(host.TryGetTargetVitals(0x80000010, out uint hp, out uint maxHp, out uint st, out uint maxSt,
            out uint mp, out uint maxMp));
        Assert.Equal((75u, 100u, 40u, 80u, 0u, 0u), (hp, maxHp, st, maxSt, mp, maxMp));
        Assert.False(host.TryGetTargetVitals(0x80000011, out _, out _, out _, out _, out _, out _));

        var spellIds = new uint[4];
        var expiries = new double[4];
        Assert.Equal(2, host.ReadObjectEnchantments(player, spellIds, expiries, 4));
        Assert.Equal((2u, 1_300.0, 3u, double.MaxValue), (spellIds[0], expiries[0], spellIds[1], expiries[1]));
        Assert.Equal(1, host.ReadObjectEnchantments(player, spellIds, expiries, 1));
        Assert.Equal(-1, host.ReadObjectEnchantments(0x80000010, spellIds, expiries, 4));

        view.CastGesture = false;
        Assert.True(host.CanCastNow);
        view.ActionAnswer = false;
        Assert.False(host.CastSpell(0x80000010, 1234));
    }

    [Fact]
    public void CombatSlots_RefuseBadArgumentsWithoutCallingTheView()
    {
        var view = new FakeGameView();
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.False(host.CastSpell(0x80000010, 0));
        Assert.False(host.CastSpell(0x80000010, -5));
        Assert.False(host.MeleeAttack(0x80000010, 0, 0.5f));
        Assert.False(host.MissileAttack(0x80000010, 4, 0.5f));
        Assert.False(host.MeleeAttack(0, 2, 0.5f));
        Assert.False(host.UseObjectOn(0, 0x80000010));
        Assert.False(host.UseObjectOn(0x80000010, 0));
        Assert.Empty(view.Actions);
    }

    [Fact]
    public void CombatSlots_ReturnNotAvailableWhenTheViewThrows()
    {
        var view = new FakeGameView { Throw = true };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.Equal(0, host.GetCastBusyState());
        Assert.False(host.CastSpell(1, 1));
        Assert.False(host.MeleeAttack(1, 2, 0.5f));
        Assert.False(host.MissileAttack(1, 2, 0.5f));
        Assert.False(host.CancelAttack());
        Assert.False(host.TryGetTargetVitals(1, out _, out _, out _, out _, out _, out _));
        Assert.False(host.UseObjectOn(1, 2));
        Assert.Equal(-1, host.ReadObjectEnchantments(1, new uint[2], new double[2], 2));
        Assert.Contains(_log, line => line.Contains("CastSpell threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("GetCastBusyState threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("GetTargetVitals threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("ReadObjectEnchantments threw", StringComparison.Ordinal));
    }
```

- [ ] **Step 3: Write the failing translate test** (append to `RynthTranslateTests`)

```csharp
    [Theory]
    [InlineData(1, AcDream.Core.Combat.AttackHeight.High)]
    [InlineData(2, AcDream.Core.Combat.AttackHeight.Medium)]
    [InlineData(3, AcDream.Core.Combat.AttackHeight.Low)]
    public void AttackHeightsMapOneToThree(int rynth, AcDream.Core.Combat.AttackHeight expected) =>
        Assert.Equal(expected, RynthTranslate.ToAttackHeight(rynth));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void OtherAttackHeightsAreRefused(int rynth) => Assert.Null(RynthTranslate.ToAttackHeight(rynth));
```

(Check that `AttackHeight` in `src/AcDream.Core/Combat/CombatModel.cs` is High = 1, Medium = 2, Low = 3. If the member names differ, use them.)

- [ ] **Step 4: Extend `FakeGameView`** (add before the `Actions` list, and the action members after `WieldItem`)

```csharp
    public bool CastGesture { get; set; }
    public Dictionary<uint, RynthVitals> TargetVitals { get; } = new();

    public bool IsCastGestureActive => Read(CastGesture);

    public bool TryGetTargetVitals(uint objectId, out RynthVitals vitals) =>
        Read(TargetVitals).TryGetValue(objectId, out vitals);

    public IReadOnlyList<RynthEnchantment>? GetObjectEnchantments(uint objectId) =>
        objectId == _playerId ? Read(Enchantments) : Read((List<RynthEnchantment>?)null);
```

```csharp
    public bool CastSpell(uint targetId, uint spellId) => Act($"cast {targetId:X8} {spellId}");
    public bool MeleeAttack(uint targetId, int height, float power) =>
        Act($"melee {targetId:X8} {height} {power.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
    public bool MissileAttack(uint targetId, int height, float accuracy) =>
        Act($"missile {targetId:X8} {height} {accuracy.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
    public bool CancelAttack() => Act("cancel");
    public bool UseObjectOn(uint sourceObjectId, uint targetObjectId) =>
        Act($"useon {sourceObjectId:X8} {targetObjectId:X8}");
```

- [ ] **Step 5: Run and see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthApiTableTests|FullyQualifiedName~RynthTranslateTests"`
Expected: build error. The new members do not exist on `IRynthGameView` and `RynthTranslate`.

- [ ] **Step 6: Grow `IRynthGameView`** (append before the closing brace)

```csharp
    /// <summary>True while the player's cast or action gesture is still playing (RynthCore's cast gate).</summary>
    bool IsCastGestureActive { get; }

    /// <summary>A creature's vitals from its last appraisal; stamina and mana 0 when it did not show them.</summary>
    bool TryGetTargetVitals(uint objectId, out RynthVitals vitals);

    /// <summary>An object's enchantments in force; null when the client keeps none for it (it keeps the player's only).</summary>
    IReadOnlyList<RynthEnchantment>? GetObjectEnchantments(uint objectId);

    /// <summary>Casts at a target without changing the selection; a self spell goes to the player. True when sent.</summary>
    bool CastSpell(uint targetId, uint spellId);

    /// <summary>Sends a melee attack at once, past the power bar. Height 1 high, 2 medium, 3 low; power 0..1. True when sent.</summary>
    bool MeleeAttack(uint targetId, int height, float power);

    /// <summary>Sends a missile attack at once, past the power bar. Accuracy 0..1. True when sent.</summary>
    bool MissileAttack(uint targetId, int height, float accuracy);

    /// <summary>Cancels the attack in progress. True when sent.</summary>
    bool CancelAttack();

    /// <summary>Uses one object on another (a kit on the player, a mana stone on an item). True when sent.</summary>
    bool UseObjectOn(uint sourceObjectId, uint targetObjectId);
```

- [ ] **Step 7: Add `ToAttackHeight`** (in `RynthTranslate`)

```csharp
    /// <summary>RynthCore's attack heights: 1 high, 2 medium, 3 low.</summary>
    public static AttackHeight? ToAttackHeight(int height) => height switch
    {
        1 => AttackHeight.High,
        2 => AttackHeight.Medium,
        3 => AttackHeight.Low,
        _ => null,
    };
```

Add `using AcDream.Core.Combat;` at the top.

- [ ] **Step 8: Create `src/AcDream.Rynth.Host/RynthApiTable.Combat.cs`**

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// The table's combat and buff slots (RynthAi sub-project #3): 1 = sent, 0 = refused or failed.
public sealed unsafe partial class RynthApiTable
{
    private void BindCombatSlots()
    {
        Native->CastSpellFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int, int>)&CastSpell;
        Native->GetCastBusyStateFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&GetCastBusyState;
        Native->MeleeAttackFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int, float, int>)&MeleeAttack;
        Native->MissileAttackFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int, float, int>)&MissileAttack;
        Native->CancelAttackFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&CancelAttack;
        Native->GetTargetVitalsFn =
            (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, uint*, uint*, uint*, uint*, uint*, int>)&GetTargetVitals;
        Native->UseObjectOnFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int>)&UseObjectOn;
        Native->ReadObjectEnchantmentsFn =
            (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, double*, int, int>)&ReadObjectEnchantments;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int CastSpell(uint targetId, int spellId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return spellId > 0 && table._view.CastSpell(targetId, (uint)spellId) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(CastSpell), error);
            return 0;
        }
    }

    // RynthCore: 1 while a cast or action gesture plays, 0 when clear (also its "cannot tell").
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetCastBusyState()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return table._view.IsCastGestureActive ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetCastBusyState), error);
            return 0;
        }
    }

    // RynthCore clamps power and accuracy to 0..1 before sending.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int MeleeAttack(uint targetId, int height, float power)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return targetId != 0 && RynthTranslate.ToAttackHeight(height) is not null
                && table._view.MeleeAttack(targetId, height, Math.Clamp(power, 0f, 1f)) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(MeleeAttack), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int MissileAttack(uint targetId, int height, float accuracy)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return targetId != 0 && RynthTranslate.ToAttackHeight(height) is not null
                && table._view.MissileAttack(targetId, height, Math.Clamp(accuracy, 0f, 1f)) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(MissileAttack), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int CancelAttack()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return table._view.CancelAttack() ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(CancelAttack), error);
            return 0;
        }
    }

    // RynthCore: 1 with the creature's appraised vitals, 0 when it holds none.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetTargetVitals(
        uint objectId, uint* health, uint* maxHealth, uint* stamina, uint* maxStamina, uint* mana, uint* maxMana)
    {
        RynthApiTable? table = _current;
        if (table is null || health is null || maxHealth is null || stamina is null
            || maxStamina is null || mana is null || maxMana is null)
            return 0;
        *health = *maxHealth = *stamina = *maxStamina = *mana = *maxMana = 0;
        try
        {
            if (objectId == 0 || !table._view.TryGetTargetVitals(objectId, out RynthVitals vitals))
                return 0;
            *health = vitals.Health;
            *maxHealth = vitals.MaxHealth;
            *stamina = vitals.Stamina;
            *maxStamina = vitals.MaxStamina;
            *mana = vitals.Mana;
            *maxMana = vitals.MaxMana;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetTargetVitals), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int UseObjectOn(uint sourceObjectId, uint targetObjectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return sourceObjectId != 0 && targetObjectId != 0
                && table._view.UseObjectOn(sourceObjectId, targetObjectId) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(UseObjectOn), error);
            return 0;
        }
    }

    // RynthCore: 0 for max <= 0, -1 when the object's registry cannot be read, else how many it wrote.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ReadObjectEnchantments(uint objectId, uint* spellIds, double* expiryTimes, int max)
    {
        RynthApiTable? table = _current;
        if (max <= 0)
            return 0;
        if (table is null || spellIds is null || expiryTimes is null)
            return -1;
        try
        {
            if (objectId == 0 || table._view.GetObjectEnchantments(objectId) is not { } enchantments)
                return -1;
            int count = Math.Min(enchantments.Count, max);
            for (int i = 0; i < count; i++)
            {
                spellIds[i] = enchantments[i].SpellId;
                expiryTimes[i] = enchantments[i].ExpiryServerTime;
            }

            return count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(ReadObjectEnchantments), error);
            return -1;
        }
    }
}
```

In `RynthApiTable.cs`, add `BindCombatSlots();` right after `BindActionSlots();` (line 72).

- [ ] **Step 9: Run the host tests.** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` should pass. The App build still fails until Step 11.

- [ ] **Step 10: Write the failing view tests.** First, in `FakeRynthAutomation.cs`, give `FakeItems` an `Apply` that records:

```csharp
    public PluginItemCommandResult Apply(uint objectId, uint targetObjectId)
    {
        Calls.Add($"apply {objectId:X8} {targetObjectId:X8}");
        return new(Answer);
    }
```

Then append to `RuntimeRynthGameViewTests`. Add `using AcDream.Core.Combat;` if it is not already there.

```csharp
    [Fact]
    public void CombatReadsComeFromTheRuntime()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var view = new RuntimeRynthGameView(runtime, automation: new FakeRynthAutomation());

        Assert.False(view.IsCastGestureActive);   // the test runtime has no body
        Assert.False(view.TryGetTargetVitals(0x80000010, out _));
        runtime.ActionOwner.Combat.OnCreatureProfile(
            0x80000010, new CombatState.CreatureVitals(75, 100, 40, 80, null, null));
        Assert.True(view.TryGetTargetVitals(0x80000010, out RynthVitals vitals));
        Assert.Equal(new RynthVitals(75, 100, 40, 80, 0, 0), vitals);
        Assert.Null(view.GetObjectEnchantments(0x80000010));
    }

    [Fact]
    public void CombatActionsNeedASessionAndUseObjectOnGoesThroughTheItems()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var automation = new FakeRynthAutomation();
        var view = new RuntimeRynthGameView(runtime, automation: automation);

        Assert.False(view.MeleeAttack(0x80000010, 2, 0.5f));   // the test runtime has no session
        Assert.False(view.MissileAttack(0x80000010, 2, 0.5f));
        Assert.False(view.CancelAttack());
        Assert.False(view.CastSpell(0x80000010, 1));           // not known, and nothing to send on
        Assert.True(view.UseObjectOn(0x80000020, 0x50000001));
        Assert.Contains("apply 80000020 50000001", automation.ItemArea.Calls);

        automation.Available = false;
        Assert.False(view.CastSpell(0x80000010, 1));
    }
```

- [ ] **Step 11: Implement in `RuntimeRynthGameView`** (append before the closing brace; add `using AcDream.Runtime.Gameplay;`)

```csharp
    public bool IsCastGestureActive => _runtime.MovementOwner.HasPendingMotions;

    public bool TryGetTargetVitals(uint objectId, out RynthVitals vitals)
    {
        if (objectId != 0
            && _runtime.ActionOwner.Combat.TryGetCreatureVitals(objectId, out CombatState.CreatureVitals found))
        {
            vitals = new RynthVitals(
                found.Health, found.HealthMax,
                found.Stamina ?? 0, found.StaminaMax ?? 0,
                found.Mana ?? 0, found.ManaMax ?? 0);
            return true;
        }

        vitals = default;
        return false;
    }

    // OpenAC keeps the player's enchantment registry only.
    public IReadOnlyList<RynthEnchantment>? GetObjectEnchantments(uint objectId) =>
        IsPlayer(objectId) ? PlayerEnchantments : null;

    // RynthCore holds casts while the player is in portal space.
    public bool CastSpell(uint targetId, uint spellId) =>
        _automation.IsAvailable && !IsPortaling
        && _runtime.ActionOwner.SpellCast.Cast(spellId, targetId) == CastRequestResult.Sent;

    // RynthCore sends the attack itself, past the power bar. The body stops first, as OpenAC's own attack does.
    public bool MeleeAttack(uint targetId, int height, float power)
    {
        if (RynthTranslate.ToAttackHeight(height) is not { } attackHeight
            || !_automation.IsAvailable
            || _runtime.Session.CurrentSession is not { } session)
            return false;
        _runtime.PrepareLocalPlayerForAttackRequest();
        session.SendMeleeAttack(targetId, attackHeight, power);
        return true;
    }

    public bool MissileAttack(uint targetId, int height, float accuracy)
    {
        if (RynthTranslate.ToAttackHeight(height) is not { } attackHeight
            || !_automation.IsAvailable
            || _runtime.Session.CurrentSession is not { } session)
            return false;
        _runtime.PrepareLocalPlayerForAttackRequest();
        session.SendMissileAttack(targetId, attackHeight, accuracy);
        return true;
    }

    // OpenAC's own attack state sends the cancel itself when it holds anything (a bot swing the
    // server commenced included), and clears what it holds; otherwise the cancel goes out bare.
    public bool CancelAttack()
    {
        if (!_automation.IsAvailable || _runtime.Session.CurrentSession is not { } session)
            return false;
        RuntimeCombatAttackState attack = _runtime.ActionOwner.CombatAttack;
        if (attack.AttackServerResponsePending || attack.AttackRequestInProgress || attack.RepeatAttackInProgress)
            attack.AbortAutomaticAttack();
        else
            session.SendCancelAttack();
        return true;
    }

    public bool UseObjectOn(uint sourceObjectId, uint targetObjectId) =>
        _automation.Items.Apply(sourceObjectId, targetObjectId).Accepted;
```

- [ ] **Step 12: Run the task's Verify command.** Expected: all pass. If `view.CastSpell` throws in the test runtime because its spell-cast operations are not bound, find how `GameRuntimeTestFactory` binds `SpellCastOperations`, and make the view return false through the existing `CastRequestResult` path rather than catching. Report what you found.

- [ ] **Step 13: Commit**

```bash
git add src/AcDream.Rynth.Host/RynthApiTable.Combat.cs src/AcDream.Rynth.Host/RynthApiTable.cs src/AcDream.Rynth.Host/IRynthGameView.cs src/AcDream.Rynth.Host/RynthTranslate.cs src/AcDream.Rynth.Host/RuntimeRynthGameView.cs tests/AcDream.Rynth.Host.Tests tests/AcDream.App.Tests/Extensions/FakeRynthAutomation.cs tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs
git commit -m "feat(rynth): combat and buff slots (cast, cast gate, attacks, vitals, use-on, enchantments)"
```

---

### Task 4: Combat, buff and status events reach the plugins

**Goal:** RynthAi receives these callbacks in RynthCore's terms, as the Design's events table says:
- `OnBusyCountIncremented`/`Decremented`, `OnSelectedTargetChange`, `OnCombatModeChange`, `OnCombatDamage`, `OnEnchantmentAdded`/`Removed`.
- `OnUpdateHealth` with current/max when appraised.
- Client status lines through `OnChatWindowText`.
- The login push of mode and selection.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthCombatEvents.cs`
- Modify: `src/AcDream.Rynth.Host/RynthPlugin.cs`, `src/AcDream.Rynth.Host/RynthEventBridge.cs`, `src/AcDream.Rynth.Host/RynthClientExtension.cs` (subscriptions ~90-98, `Dispose` ~206-216, handlers ~246)
- Test: `tests/AcDream.App.Tests/Extensions/RynthCombatEventsTests.cs`

**Acceptance Criteria:**
- [ ] With a recorder plugin, these runtime events produce exactly these calls, in this order:
  - select 0x80000010 → `select 80000010 00000000`
  - mode Melee → `mode 2 1`
  - dealt 25 type 4 crit → `damage 25 4 1 1`
  - taken 7 type 1 → `damage 7 1 0 0`
  - dealt 0 → nothing
  - enchantment 2 (1800 s) added then removed → `ench+ 2 1800`, `ench- 2`
  - a cooldown-bucket removal → nothing
  - busy +1 then cleared → `busy+`, `busy-`
  - appraisal 75/100 → `health 80000010 0.75 75 100`
  - health 0.5 → `health 80000010 0.50 50 100`
  - status line → `chat 1A You're too busy!`
- [ ] An unappraised target's health update carries 0/0.
- [ ] Entering the world raises `mode <current> 1`, then `select <id> 00000000` when something is selected, after the login callback.
- [ ] `RynthClientExtension` forwards through `RynthCombatEvents` (a selection change reaches a recorder plugin loaded by the extension), and after `Dispose` nothing more is forwarded.
- [ ] `AcDream.Rynth.Host.Tests` and the Rynth App tests are green.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests && ~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~Rynth"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (`tests/AcDream.App.Tests/Extensions/RynthCombatEventsTests.cs`)

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AcDream.ClientExtensions;
using AcDream.Core.Chat;
using AcDream.Core.Combat;
using AcDream.Core.Selection;
using AcDream.Core.Spells;
using AcDream.Runtime;
using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RynthCombatEventsTests : IDisposable
{
    private readonly RynthConfigSandbox _sandbox = new("rynth-combat-");

    public void Dispose() => _sandbox.Dispose();

    // Other lines (chat the plugins write) are not what these tests are about.
    private static string[] Recorded() =>
        [.. CombatRecorder.Calls.Where(call => !call.StartsWith("chat ", StringComparison.Ordinal)
            || call.StartsWith("chat 1A ", StringComparison.Ordinal))];

    [Fact]
    public void BusyHealthStatusAndCooldownsReachThePluginInRynthCoresTerms()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        using var host = new RynthHost(new RuntimeRynthGameView(runtime), _ => { }, [typeof(CombatRecorder)]);
        using var events = new RynthCombatEvents(runtime, host.Bridge);
        CombatState combat = runtime.ActionOwner.Combat;
        Spellbook spellbook = runtime.CharacterOwner.Spellbook;

        spellbook.OnEnchantmentAdded(new ActiveEnchantmentRecord(0x8005u, 1u, 30.0, 0x50000001u,
            Bucket: Spellbook.CooldownBucket));
        CombatRecorder.Calls.Clear();   // the cooldown's added line is not under test; its removal is
        spellbook.OnEnchantmentRemoved(1u, 0x8005u);
        Assert.Empty(Recorded());
        runtime.InventoryOwner.Transactions.IncrementBusyCount();
        runtime.InventoryOwner.Transactions.ClearBusy();
        combat.OnCreatureProfile(0x80000010, new CombatState.CreatureVitals(75, 100, null, null, null, null));
        combat.OnUpdateHealth(0x80000010, 0.5f);
        combat.OnUpdateHealth(0x80000011, 0.25f);
        runtime.CommunicationOwner.AddText("You're too busy!", RetailLogTextType.ClientLocal);

        Assert.Equal(
            [
                "busy+", "busy-",
                "health 80000010 0.75 75 100", "health 80000010 0.50 50 100", "health 80000011 0.25 0 0",
                "chat 1A You're too busy!",
            ],
            Recorded());
    }

    [Fact]
    public void SelectionModeDamageAndEnchantmentsAreForwardedInOrder()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        using var host = new RynthHost(new RuntimeRynthGameView(runtime), _ => { }, [typeof(CombatRecorder)]);
        using var events = new RynthCombatEvents(runtime, host.Bridge);
        CombatRecorder.Calls.Clear();
        CombatState combat = runtime.ActionOwner.Combat;

        runtime.ActionOwner.Selection.Select(0x80000010, SelectionChangeSource.System);
        combat.SetCombatMode(CombatMode.Melee);
        combat.OnAttackerNotification("drudge", 4u, 25u, 0.1, critical: 1u);
        combat.OnDefenderNotification("drudge", 0x80000010u, 1u, 7u, 0u, 0u);
        combat.OnAttackerNotification("drudge", 4u, 0u, 0.0);
        runtime.CharacterOwner.Spellbook.OnEnchantmentAdded(
            new ActiveEnchantmentRecord(2u, 1u, 1800.0, 0x50000001u, Bucket: 1u));
        runtime.CharacterOwner.Spellbook.OnEnchantmentRemoved(1u, 2u);

        Assert.Equal(
            [
                "select 80000010 00000000", "mode 2 1", "damage 25 4 1 1", "damage 7 1 0 0",
                "ench+ 2 1800", "ench- 2",
            ],
            Recorded());
    }

    [Fact]
    public void EnteringTheWorldPushesTheModeAndTheSelection()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        using var host = new RynthHost(new RuntimeRynthGameView(runtime), _ => { }, [typeof(CombatRecorder)]);
        using var events = new RynthCombatEvents(runtime, host.Bridge);
        runtime.ActionOwner.Selection.Select(0x80000010, SelectionChangeSource.System);
        runtime.ActionOwner.Combat.SetCombatMode(CombatMode.Magic);
        CombatRecorder.Calls.Clear();

        host.Bridge.OnLifecycle(new RuntimeLifecycleDelta(
            default, RuntimeLifecycleState.Starting, RuntimeLifecycleState.InWorld));

        Assert.Equal(["login", "mode 8 1", "select 80000010 00000000"], Recorded());
    }

    [Fact]
    public void TheExtensionForwardsUntilDisposed()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = _ => { },
            RegisterChatCommand = (_, _) => new Registration(),
        };
        var extension = new RynthClientExtension { AdditionalPluginExports = [typeof(CombatRecorder)] };
        extension.Attach(context);
        CombatRecorder.Calls.Clear();

        runtime.ActionOwner.Selection.Select(0x80000010, SelectionChangeSource.System);
        extension.Dispose();
        runtime.ActionOwner.Selection.Select(0x80000020, SelectionChangeSource.System);

        Assert.Equal(["select 80000010 00000000"], Recorded());
    }

    private sealed class Registration : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private static unsafe class CombatRecorder
    {
        public static readonly List<string> Calls = new();

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnLoginComplete", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Login() => Calls.Add("login");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnBusyCountIncremented", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void BusyUp() => Calls.Add("busy+");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnBusyCountDecremented", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void BusyDown() => Calls.Add("busy-");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnSelectedTargetChange", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Selected(uint current, uint previous) => Calls.Add($"select {current:X8} {previous:X8}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnCombatModeChange", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Mode(int current, int previous) => Calls.Add($"mode {current} {previous}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnCombatDamage", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Damage(uint damage, uint type, uint critical, uint isAttacker) =>
            Calls.Add($"damage {damage} {type} {critical} {isAttacker}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnEnchantmentAdded", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Added(uint spellId, double seconds) =>
            Calls.Add($"ench+ {spellId} {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnEnchantmentRemoved", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Removed(uint spellId) => Calls.Add($"ench- {spellId}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnUpdateHealth", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Health(uint id, float ratio, uint current, uint max) =>
            Calls.Add($"health {id:X8} {ratio.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} {current} {max}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnChatWindowText", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Chat(IntPtr text, int chatType, IntPtr eat) =>
            Calls.Add($"chat {chatType:X} {Marshal.PtrToStringUni(text)}");
    }
}
```

Notes for the implementer:
- `RynthConfigSandbox` exists in the App test project (`RynthClientExtensionTests` uses it). Use it as that file does.
- Check how `RynthHost` names an `AdditionalPluginExports` type and loads its `UnmanagedCallersOnly` exports (`RynthPluginExports`). If `RynthHost` cannot take a nested private static class, mirror `ContainerRecorder` in `RynthClientExtensionTests`, which works.
- If `Spellbook.OnEnchantmentAdded` needs the spell in its table, or `ClearBusy` raises no `StateChanged`, find the real call that does it (for example `CompleteUse`). Keep the expected lines unchanged.

- [ ] **Step 2: Run and see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RynthCombatEventsTests"`
Expected: build error. `RynthCombatEvents` does not exist.

- [ ] **Step 3: Add the export calls to `RynthPlugin`** (after `OnUpdateHealth`)

```csharp
    public void OnBusyCountIncremented() => CallVoid("RynthPluginOnBusyCountIncremented");

    public void OnBusyCountDecremented() => CallVoid("RynthPluginOnBusyCountDecremented");

    public void OnSelectedTargetChange(uint currentTargetId, uint previousTargetId)
    {
        if (IsActive && _exports.TryGet("RynthPluginOnSelectedTargetChange", out IntPtr export))
        {
            using RynthDispatch.Scope scope = RynthDispatch.Enter(this);
            ((delegate* unmanaged[Cdecl]<uint, uint, void>)export)(currentTargetId, previousTargetId);
        }
    }

    /// <summary>Modes in RynthCore's 1 NonCombat, 2 Melee, 4 Missile, 8 Magic.</summary>
    public void OnCombatModeChange(int currentMode, int previousMode)
    {
        if (IsActive && _exports.TryGet("RynthPluginOnCombatModeChange", out IntPtr export))
        {
            using RynthDispatch.Scope scope = RynthDispatch.Enter(this);
            ((delegate* unmanaged[Cdecl]<int, int, void>)export)(currentMode, previousMode);
        }
    }

    /// <summary>A hit: the wire damage type, critical and attacker as 0/1 (attacker 1 = the player hit).</summary>
    public void OnCombatDamage(uint damage, uint damageType, uint critical, uint isAttacker)
    {
        if (IsActive && _exports.TryGet("RynthPluginOnCombatDamage", out IntPtr export))
        {
            using RynthDispatch.Scope scope = RynthDispatch.Enter(this);
            ((delegate* unmanaged[Cdecl]<uint, uint, uint, uint, void>)export)(damage, damageType, critical, isAttacker);
        }
    }

    /// <summary>An enchantment on the player; duration as the server sent it (-1 permanent).</summary>
    public void OnEnchantmentAdded(uint spellId, double durationSeconds)
    {
        if (IsActive && _exports.TryGet("RynthPluginOnEnchantmentAdded", out IntPtr export))
        {
            using RynthDispatch.Scope scope = RynthDispatch.Enter(this);
            ((delegate* unmanaged[Cdecl]<uint, double, void>)export)(spellId, durationSeconds);
        }
    }

    /// <summary>An enchantment gone from the player, by spell id (layer stripped), as RynthCore reports it.</summary>
    public void OnEnchantmentRemoved(uint spellId) => CallObject("RynthPluginOnEnchantmentRemoved", spellId);
```

Update the `OnUpdateHealth` doc comment. It should now say that current and max are 0 unless the creature was appraised (or for the player's own death).

- [ ] **Step 4: Grow `RynthEventBridge`.**
  - Add `using AcDream.Core.Chat;`.
  - Add the `LoggedIn` event, and raise it in `OnLifecycle` after the `OnLoginComplete` loop.
  - Give `OnHealthChanged` current/max.
  - Add the forwarders.

```csharp
    /// <summary>Raised once the plugins have been told of the login, for pushes that follow it.</summary>
    public event Action? LoggedIn;
```

In `OnLifecycle`, after the `foreach (... plugin.OnLoginComplete();)` loop, add `LoggedIn?.Invoke();`.

Replace `OnHealthChanged` with:

```csharp
    /// <summary>From <c>CombatState.HealthChanged</c>; current and max are 0 unless the creature was appraised.</summary>
    public void OnHealthChanged(uint targetId, float healthRatio, uint currentHealth = 0, uint maxHealth = 0)
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnUpdateHealth(targetId, healthRatio, currentHealth, maxHealth);
    }
```

Add:

```csharp
    public void OnBusyCountIncremented()
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnBusyCountIncremented();
    }

    public void OnBusyCountDecremented()
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnBusyCountDecremented();
    }

    public void OnSelectedTargetChange(uint currentTargetId, uint previousTargetId)
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnSelectedTargetChange(currentTargetId, previousTargetId);
    }

    public void OnCombatModeChange(int currentMode, int previousMode)
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnCombatModeChange(currentMode, previousMode);
    }

    public void OnCombatDamage(uint damage, uint damageType, uint critical, uint isAttacker)
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnCombatDamage(damage, damageType, critical, isAttacker);
    }

    public void OnEnchantmentAdded(uint spellId, double durationSeconds)
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnEnchantmentAdded(spellId, durationSeconds);
    }

    public void OnEnchantmentRemoved(uint spellId)
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnEnchantmentRemoved(spellId);
    }

    /// <summary>
    /// A client status line ("You're too busy!"). The retail client shows these in the
    /// chat window, where RynthCore reads them; OpenAC shows them as floating text only.
    /// </summary>
    public void OnStatusText(string text)
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnChatWindowText(text, (int)RetailLogTextType.ClientLocal);
    }
```

- [ ] **Step 5: Create `src/AcDream.Rynth.Host/RynthCombatEvents.cs`**

```csharp
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Selection;
using AcDream.Core.Spells;
using AcDream.Runtime;

namespace AcDream.Rynth.Host;

/// <summary>
/// Forwards the client's combat, selection, enchantment, busy and status-line
/// events to the RynthCore callbacks RynthAi's combat and buffing read, in
/// RynthCore's terms. Raised on the game update thread.
/// </summary>
public sealed class RynthCombatEvents : IDisposable
{
    // RynthCore raises one event per busy step; a reset can drop many at once.
    private const int MaxBusySteps = 64;

    // RynthCore drops a damage figure of 0 or above this as a bad read.
    private const uint MaxDamage = 1_000_000;

    private readonly GameRuntime _runtime;
    private readonly RynthEventBridge _bridge;
    private readonly CombatState _combat;
    private readonly SelectionState _selection;
    private readonly Spellbook _spellbook;
    private readonly InventoryTransactionState _transactions;
    private int _combatMode;
    private int _busyCount;
    private bool _disposed;

    public RynthCombatEvents(GameRuntime runtime, RynthEventBridge bridge)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _combat = runtime.ActionOwner.Combat;
        _selection = runtime.ActionOwner.Selection;
        _spellbook = runtime.CharacterOwner.Spellbook;
        _transactions = runtime.InventoryOwner.Transactions;
        _combatMode = (int)_combat.CurrentMode;
        _busyCount = _transactions.BusyCount;

        _combat.HealthChanged += OnHealthChanged;
        _combat.CombatModeChanged += OnCombatModeChanged;
        _combat.DamageDealtAccepted += OnDamageDealt;
        _combat.DamageTaken += OnDamageTaken;
        _selection.Changed += OnSelectionChanged;
        _spellbook.EnchantmentAdded += OnEnchantmentAdded;
        _spellbook.EnchantmentRemoved += OnEnchantmentRemoved;
        _transactions.StateChanged += OnTransactionsChanged;
        _runtime.CommunicationOwner.StatusTextShown += OnStatusTextShown;
        _bridge.LoggedIn += OnLoggedIn;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _combat.HealthChanged -= OnHealthChanged;
        _combat.CombatModeChanged -= OnCombatModeChanged;
        _combat.DamageDealtAccepted -= OnDamageDealt;
        _combat.DamageTaken -= OnDamageTaken;
        _selection.Changed -= OnSelectionChanged;
        _spellbook.EnchantmentAdded -= OnEnchantmentAdded;
        _spellbook.EnchantmentRemoved -= OnEnchantmentRemoved;
        _transactions.StateChanged -= OnTransactionsChanged;
        _runtime.CommunicationOwner.StatusTextShown -= OnStatusTextShown;
        _bridge.LoggedIn -= OnLoggedIn;
    }

    // The server's health updates carry a share; with an appraised maximum the current
    // health is that share of it, as RynthCore computes it. 0/0 when never appraised.
    private void OnHealthChanged(uint targetId, float healthRatio)
    {
        uint max = _combat.TryGetCreatureVitals(targetId, out CombatState.CreatureVitals vitals) ? vitals.HealthMax : 0u;
        uint current = max == 0 ? 0u : (uint)Math.Round(max * (double)healthRatio);
        _bridge.OnHealthChanged(targetId, healthRatio, current, max);
    }

    // OpenAC's CombatMode values are RynthCore's 1/2/4/8.
    private void OnCombatModeChanged(CombatMode mode)
    {
        int previous = _combatMode;
        _combatMode = (int)mode;
        if (_combatMode != previous)
            _bridge.OnCombatModeChange(_combatMode, previous);
    }

    private void OnDamageDealt(CombatState.DamageDealt hit)
    {
        if (hit.Damage is > 0 and <= MaxDamage)
            _bridge.OnCombatDamage(hit.Damage, hit.DamageType, hit.Critical ? 1u : 0u, isAttacker: 1u);
    }

    private void OnDamageTaken(CombatState.DamageIncoming hit)
    {
        if (hit.Damage is > 0 and <= MaxDamage)
            _bridge.OnCombatDamage(hit.Damage, hit.DamageType, hit.Critical ? 1u : 0u, isAttacker: 0u);
    }

    // A reset raises null -> null; RynthCore reports changes only.
    private void OnSelectionChanged(SelectionTransition transition)
    {
        uint current = transition.SelectedObjectId ?? 0u;
        uint previous = transition.PreviousObjectId ?? 0u;
        if (current != previous)
            _bridge.OnSelectedTargetChange(current, previous);
    }

    private void OnEnchantmentAdded(ActiveEnchantmentRecord record) =>
        _bridge.OnEnchantmentAdded(record.SpellId & 0xFFFFu, record.Duration);

    // RynthCore reports the server's removals; OpenAC also drops an expired cooldown
    // locally, which RynthCore never reports, so cooldowns are left out.
    private void OnEnchantmentRemoved(ActiveEnchantmentRecord record)
    {
        if (record.Bucket != Spellbook.CooldownBucket)
            _bridge.OnEnchantmentRemoved(record.SpellId & 0xFFFFu);
    }

    private void OnTransactionsChanged()
    {
        int current = _transactions.BusyCount;
        int delta = current - _busyCount;
        _busyCount = current;
        int steps = Math.Min(Math.Abs(delta), MaxBusySteps);
        for (int i = 0; i < steps; i++)
        {
            if (delta > 0)
                _bridge.OnBusyCountIncremented();
            else
                _bridge.OnBusyCountDecremented();
        }
    }

    private void OnStatusTextShown(string text) => _bridge.OnStatusText(text);

    // RynthCore tells a plugin the mode (from NonCombat) and any selection once the player is in the world.
    private void OnLoggedIn()
    {
        _combatMode = (int)_combat.CurrentMode;
        _bridge.OnCombatModeChange(_combatMode, (int)CombatMode.NonCombat);
        if (_selection.SelectedObjectId is uint selected and not 0u)
            _bridge.OnSelectedTargetChange(selected, 0u);
    }
}
```

(`SelectionState.SelectedObjectId` is `uint?`, and `InventoryTransactionState` is in `AcDream.Core.Items`. If a namespace differs, fix the `using`. Do not change behaviour.)

- [ ] **Step 6: Wire it into `RynthClientExtension`.**
  - Add a field `private RynthCombatEvents? _combatEvents;`.
  - In `Attach`, replace `_combat.HealthChanged += OnHealthChanged;` with `_combatEvents = new RynthCombatEvents(context.Runtime, _host.Bridge);`. Keep `_combat = ...` and `_combat.KillLanded += OnKillLanded;`.
  - In `Dispose`, delete `_combat.HealthChanged -= OnHealthChanged;`.
  - Add `_combatEvents?.Dispose(); _combatEvents = null;` just before `_runtimeSubscription?.Dispose();`.
  - Delete the private `OnHealthChanged` handler.

- [ ] **Step 7: Run the task's Verify command.** Expected: all pass. The existing `AttachesTicksAndReleasesItsSlotOnDispose` still raises a health update through the extension, which now goes through `RynthCombatEvents`.

- [ ] **Step 8: Check the build is clean and the submodules untouched**

Run: `~/.dotnet/dotnet build src/AcDream.App -c Release 2>&1 | tail -3 && git status --porcelain external`
Expected: `0 Warning(s)`, `0 Error(s)`, and no submodule output.

- [ ] **Step 9: Commit**

```bash
git add src/AcDream.Rynth.Host/RynthCombatEvents.cs src/AcDream.Rynth.Host/RynthPlugin.cs src/AcDream.Rynth.Host/RynthEventBridge.cs src/AcDream.Rynth.Host/RynthClientExtension.cs tests/AcDream.App.Tests/Extensions/RynthCombatEventsTests.cs
git commit -m "feat(rynth): forward busy, selection, mode, damage, enchantment, health and status events"
```

---

### Task 5: Live check on local ACE (user-run)

**Goal:** RynthAi fights and buffs on the local ACE server, using the slots and events from Tasks 1–4. This also covers the pending #2 live check.

**Files:** none (observation only; record results in the plan's status line and the memory file `rynth-status.md`)

**Acceptance Criteria:**
- [ ] The user has approved launching the client (ask first; local ACE 10.10.20.20 only, never Dreamweave).
- [ ] With the RynthAi macro on and a melee weapon, the character attacks a nearby monster: the server sends damage lines and the kill is counted.
- [ ] With buffing enabled, the character casts at least one self buff ("You cast …" in chat), waits for the gesture between casts, and does not spam "You're too busy!".
- [ ] With a war spell configured, the character casts it at a target without the selection changing away from what the bot targets.
- [ ] A healing kit is used on self when health drops (if a kit is in the pack).
- [ ] The client log has no `threw` lines from `RynthApiTable`.
- [ ] #2 items: the dashboard shows vitals and combat mode, and `/ra` basic actions work.
- [ ] With a buff and a war spell whose components are missing, RynthAi shows "You don't have all the components for this spell." once, then backs off (a family cooldown for buffs, a lower tier for war). There is no retry every 400 ms.
- [ ] Casting while walking: the cast gate (pending motions) does not hold casts for long, and RynthAi's own pre-cast stop takes over.
- [ ] Cancelling during a melee swing the server is repeating stops the swing, and the next attack goes out.

**Verify:** the live run above, with the user watching. Capture the client log lines (`grep -i "rynth\|threw"`) and the user's confirmation for each criterion.

**Steps:**

- [ ] **Step 1:** Ask the user whether to launch now. Use the setup in memory `live-testing-rules`: Release build, `source ~/OpenAC-dev/dev-client.env` inside a script, map the user/pass env vars without echoing them, `ACDREAM_DAT_DIR=/Users/davidsmith/AsheronsCall`, and the pak path.
- [ ] **Step 2:** Walk the checklist with the user, and fix any failures through systematic debugging before closing.
- [ ] **Step 3:** Update this plan's status line and `rynth-status.md` with the results.
