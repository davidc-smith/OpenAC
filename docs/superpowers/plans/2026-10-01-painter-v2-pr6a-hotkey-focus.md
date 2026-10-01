# Painter v2 — PR 6a: Hotkeys Respect Interface Focus Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A plugin hotkey without Ctrl or Alt no longer fires while the player types in chat or any other focused interface element, and no hotkey fires while a modal dialog is open.

**Architecture:** `AppHotkeyRegistry.OnKeyDown` checks `InputDispatcher.ActiveScope` for chat and dialog scopes, but no production code pushes a scope, so those checks never fire. The live signal is the retained interface's `UiRoot`: `KeyboardFocus` and `Modal`. `GameWindow` already owns a `RetainedUiInputCaptureSlot`, which is bound to whichever `UiRoot` is live and survives an interface rebuild. The slot implements a new `IHotkeyFocusSource` (`HasKeyboardFocus`, `IsModalOpen`), and `AppHotkeyRegistry.Bind` takes it as a fourth argument. Rules at each key press: a rebind capture, a dialog scope or an open modal → no hotkey fires; a chat scope or any keyboard focus → only chords with Ctrl or Alt fire.

**Tech Stack:** C# / .NET 10, xUnit, Silk.NET input types.

**Spec:** `docs/superpowers/specs/2026-09-30-plugin-painter-v2-design.md` (section 2 item 2, section 9, and the PR 6a entries under "Plan-time corrections", which supersede section 9 where they differ).

**Checked:** this plan's code was applied to upstream `main` (`bbc83275`) in a throwaway worktree before hand-off. Every "replace this" anchor was checked to exist verbatim in `bbc83275`. It built with 0 warnings, and every new test passed, along with the full portable suite (apart from the baseline failures listed under Global Constraints, which fail identically on clean `bbc83275`). Removing the two new checks from `OnKeyDown` made all four new rule tests fail: both new `AppHotkeyRegistryTests` and the first two `HotkeyUiFocusTests`. The `HotkeyUiFocusTests` teardown test passes either way, because it checks that hotkeys fire, which they also do with no focus check at all. Fork `main` (`a4ab9664`) changes none of the files this PR touches, so Task 5's merge has no conflicts.

## Global Constraints

- Branch `fix/hotkey-focus-scope`, created from upstream `main` at `bbc83275` (`upstream/main`; fork `main` contains it). PR 6a depends on no other PR in the series, so the branch can go upstream as one topic. Nothing under `docs/superpowers/` goes on a code branch.
- No change to `AcDream.Plugin.Abstractions`: the plugin contract stays as it is; only the host's behaviour changes.
- `IHotkeyFocusSource` is `public` (it is a parameter of the public `AppHotkeyRegistry.Bind`; an internal type there fails with CS0051). `RetainedUiInputCaptureSlot` stays `internal`.
- `HasKeyboardFocus` is exactly `Root?.KeyboardFocus is not null`; `IsModalOpen` is exactly `Root?.Modal is not null`. An unbound slot reports `false` for both.
- Rule order in `OnKeyDown`: `dispatcher.IsCapturing` → return; `ActiveScope` is `Dialog` or `EditField`, or `IsModalOpen` → return; then a chord with neither Ctrl nor Alt is skipped when `ActiveScope == Chat` or `HasKeyboardFocus`. The existing `ActiveScope` checks stay.
- The focus state is read at every key press, never cached.
- Commit subjects: `fix(app): …`, `docs: …` (the style of this file's history). Every commit message ends with `Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB`.
- Environment for every command: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`.
- Builds on this Mac rewrite `src/AcDream.Launcher/packages.neutral.lock.json`, and an unlocked restore touches `packages.osx-arm64.lock.json` files. Never stage a `packages.*.lock.json`. Stage files by explicit path, and run `git checkout -- '*.lock.json'` before pushing.
- The first solution build in a fresh worktree can fail once with `NETSDK1047 … AcDream.Bake/obj/project.assets.json doesn't have a target for 'net10.0/osx-arm64'`, a restore race. Build again; the second build is clean.
- Portable gate filter: `Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure`.
- Baseline failures on this Mac, which fail identically on clean `bbc83275` and are not this PR's: run the full portable suite with `TMPDIR=/tmp/` (the default temp path makes Unix-socket paths longer than 104 characters). Even then these fail: `AcDream.App.Tests` `GraphicalPluginSessionTests.ReloadCommandLoadsAFreshCopyAndTheOldOneLeavesMemory`, `GameWindowRenderLeafCompositionTests.PaperdollComposition_SkipsEitherMissingOptionalUiSurface` and `LiveEntityNetworkUpdateControllerForcePositionWiringTests.CommittedOrDeferredCellReturnsBeforeReachingTheGenericTail`; three `AcDream.RenderPackValidator.Tests` `RenderPackValidatorCommandTests.External*`; `AcDream.Headless.Tests` `HeadlessSessionIsolationTests.ThirtySessionMixedWorkloadMaintainsIsolationAndConverges`; and 2–7 `AcDream.HostParity.Tests` `Peer*ParityTests`, which vary run to run (7 and 6 on two consecutive runs of clean `bbc83275` during planning). Record the baseline set in Task 0 and compare against it; do not try to fix them here.
- The unit tests must run outside the command sandbox, or the peer and pipe tests fail on socket permissions too.

**User decisions (already made):**
- "One spec, a PR series"; this is PR 6a (`fix/hotkey-focus-scope`), discussed and agreed 2026-09-30, with no dependencies.
- Base: upstream `main`, as for PR 5 ("Upstream main (Recommended)", 2026-10-01), then merged into fork `main` with a merge commit, as PRs 3, 4 and 5 were.
- Branches are pushed to `origin`. Do not open a pull request without asking which repository it goes to.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AcDream.App/Input/IHotkeyFocusSource.cs` | Create | The focus state hotkeys read: `HasKeyboardFocus`, `IsModalOpen`. |
| `src/AcDream.App/Input/InputCaptureSources.cs` | Modify | `RetainedUiInputCaptureSlot` implements `IHotkeyFocusSource` from its bound `UiRoot`. |
| `tests/AcDream.App.Tests/Input/RetainedUiInputCaptureSlotTests.cs` | Create | Slot reports the bound root's focus and modal, and nothing when unbound. |
| `src/AcDream.App/Input/AppHotkeyRegistry.cs` | Modify | `Bind` takes the focus source; `OnKeyDown` applies the rules. |
| `src/AcDream.App/Rendering/GameWindow.cs` | Modify | Passes `_retainedInputCapture` to `Bind`. |
| `tests/AcDream.App.Tests/Input/AppHotkeyRegistryTests.cs` | Modify | Existing tests pass a `FakeFocus`; one test per new rule. |
| `tests/AcDream.App.Tests/Input/HotkeyUiFocusTests.cs` | Create | Host-level: registry → slot → real `UiRoot`, with a focused field and with a modal. |
| `docs/plugin-api.md` | Modify | Hotkeys section matches the behaviour. |

`tests/AcDream.HostParity.Tests/Harness/WindowedArm.cs` constructs an `AppHotkeyRegistry` and hands it to `GameWindow`, which calls `Bind`; it needs no change.

---

### Task 0: Create the branch and record the baseline

**Goal:** `fix/hotkey-focus-scope` from upstream `main`, building cleanly, with the baseline test failures written down.

**Files:** none

**Acceptance Criteria:**
- [ ] `git branch --show-current` prints `fix/hotkey-focus-scope`
- [ ] `git rev-parse HEAD` is `bbc83275…` (upstream `main`)
- [ ] `docs/superpowers` does not exist in the working tree
- [ ] `dotnet build AcDream.slnx -c Release` reports `0 Warning(s)` and `0 Error(s)`
- [ ] The names of the portable-suite failures (run with `TMPDIR=/tmp/`) are saved to `/tmp/openac-hotkeys/baseline-failures.txt`

**Verify:** `git branch --show-current && git merge-base --is-ancestor HEAD upstream/main && test ! -e docs/superpowers && echo ok` → `fix/hotkey-focus-scope`, `ok`

**Steps:**

- [ ] **Step 1: Branch**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git fetch upstream && git fetch origin
git rev-parse --short upstream/main   # expect bbc83275; if upstream moved, stop and ask
git worktree add -b fix/hotkey-focus-scope .worktrees/hotkey-focus-scope upstream/main
cd .worktrees/hotkey-focus-scope
git merge-base --is-ancestor HEAD upstream/main && test ! -e docs/superpowers && echo ok
```

Expected: `bbc83275`, then `ok`. Every later command runs in `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/hotkey-focus-scope` (ignored by git). The main checkout stays on `docs/painter-v2-spec`, where this plan lives.

- [ ] **Step 2: Baseline build and tests**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
mkdir -p /tmp/openac-hotkeys
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test AcDream.slnx -c Release --no-build --filter "Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure" 2>&1 \
  | grep -E "^\s+Failed " | sed -E 's/ \[[^]]*\]$//' | sort -u > /tmp/openac-hotkeys/baseline-failures.txt
cat /tmp/openac-hotkeys/baseline-failures.txt
git checkout -- '*.lock.json'
```

Expected: `0 Warning(s)`, `0 Error(s)`. The failures listed are the baseline set under Global Constraints (the HostParity peer tests vary).

```json:metadata
{"files": [], "verifyCommand": "git branch --show-current && git merge-base --is-ancestor HEAD upstream/main && test ! -e docs/superpowers && echo ok", "acceptanceCriteria": ["on fix/hotkey-focus-scope", "HEAD is upstream main bbc83275", "no docs/superpowers", "0 warnings", "baseline failures recorded"], "modelTier": "mechanical"}
```

---

### Task 1: The capture slot reports interface focus and modals

**Goal:** `RetainedUiInputCaptureSlot` implements a new `IHotkeyFocusSource`, reporting whether its bound `UiRoot` has a keyboard-focused element and whether a modal is open, and `false` for both when unbound.

**Files:**
- Create: `src/AcDream.App/Input/IHotkeyFocusSource.cs`
- Modify: `src/AcDream.App/Input/InputCaptureSources.cs:24-38`
- Test: `tests/AcDream.App.Tests/Input/RetainedUiInputCaptureSlotTests.cs` (create)

**Acceptance Criteria:**
- [ ] `IHotkeyFocusSource` is a `public` interface in `AcDream.App.Input` with `bool HasKeyboardFocus { get; }` and `bool IsModalOpen { get; }`, each with an XML summary
- [ ] `RetainedUiInputCaptureSlot` implements it: `HasKeyboardFocus => Root?.KeyboardFocus is not null`, `IsModalOpen => Root?.Modal is not null`
- [ ] The three `RetainedUiInputCaptureSlotTests` pass: focus follows `SetKeyboardFocus`, modal follows `Modal`, an unbound or unbound-again slot reports neither
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`

**Verify:** `TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~RetainedUiInputCaptureSlotTests"` → `Passed!  - Failed:     0, Passed:     3`

**Steps:**

- [ ] **Step 1: Write the failing tests**

Create `tests/AcDream.App.Tests/Input/RetainedUiInputCaptureSlotTests.cs`:

```csharp
using AcDream.App.Input;
using AcDream.App.UI;

namespace AcDream.App.Tests.Input;

public sealed class RetainedUiInputCaptureSlotTests
{
    [Fact]
    public void FocusStateFollowsTheBoundRootsKeyboardFocus()
    {
        var slot = new RetainedUiInputCaptureSlot();
        var root = new UiRoot { Width = 800, Height = 600 };
        var field = new UiField { Width = 200, Height = 20 };
        root.AddChild(field);
        using IDisposable binding = slot.Bind(root);

        Assert.False(slot.HasKeyboardFocus);
        root.SetKeyboardFocus(field);
        Assert.True(slot.HasKeyboardFocus);
        root.SetKeyboardFocus(null);
        Assert.False(slot.HasKeyboardFocus);
    }

    [Fact]
    public void FocusStateFollowsTheBoundRootsModal()
    {
        var slot = new RetainedUiInputCaptureSlot();
        var root = new UiRoot { Width = 800, Height = 600 };
        var dialog = new UiPanel { Width = 300, Height = 150 };
        root.AddChild(dialog);
        using IDisposable binding = slot.Bind(root);

        Assert.False(slot.IsModalOpen);
        root.Modal = dialog;
        Assert.True(slot.IsModalOpen);
        root.Modal = null;
        Assert.False(slot.IsModalOpen);
    }

    [Fact]
    public void AnUnboundSlotReportsNoFocusAndNoModal()
    {
        var slot = new RetainedUiInputCaptureSlot();
        var root = new UiRoot { Width = 800, Height = 600 };
        var field = new UiField { Width = 200, Height = 20 };
        root.AddChild(field);
        root.SetKeyboardFocus(field);
        root.Modal = new UiPanel { Width = 300, Height = 150 };

        Assert.False(slot.HasKeyboardFocus);
        Assert.False(slot.IsModalOpen);

        // An interface rebuild disposes the old root's binding first.
        slot.Bind(root).Dispose();
        Assert.False(slot.HasKeyboardFocus);
        Assert.False(slot.IsModalOpen);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~RetainedUiInputCaptureSlotTests"`
Expected: build FAILS with `error CS1061: 'RetainedUiInputCaptureSlot' does not contain a definition for 'HasKeyboardFocus'` (and `IsModalOpen`).

- [ ] **Step 3: Add the interface**

Create `src/AcDream.App/Input/IHotkeyFocusSource.cs`:

```csharp
namespace AcDream.App.Input;

/// <summary>
/// The interface's focus state as plugin hotkeys see it, read at each key
/// press.
/// </summary>
public interface IHotkeyFocusSource
{
    /// <summary>Whether any interface element holds keyboard focus.</summary>
    bool HasKeyboardFocus { get; }

    /// <summary>Whether a modal dialog is open.</summary>
    bool IsModalOpen { get; }
}
```

- [ ] **Step 4: Implement it on the slot**

In `src/AcDream.App/Input/InputCaptureSources.cs`, replace

```csharp
internal sealed class RetainedUiInputCaptureSlot
{
```

with

```csharp
internal sealed class RetainedUiInputCaptureSlot : IHotkeyFocusSource
{
```

and replace

```csharp
    public bool WantCaptureKeyboard => Root?.WantsKeyboard ?? false;
```

with

```csharp
    public bool WantCaptureKeyboard => Root?.WantsKeyboard ?? false;

    public bool HasKeyboardFocus => Root?.KeyboardFocus is not null;
    public bool IsModalOpen => Root?.Modal is not null;
```

- [ ] **Step 5: Run the tests to see them pass, and build**

```bash
TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~RetainedUiInputCaptureSlotTests" 2>&1 | grep -E "Passed!|Failed!"
dotnet build AcDream.slnx -c Release 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
```

Expected: `Passed!  - Failed:     0, Passed:     3`; `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/Input/IHotkeyFocusSource.cs src/AcDream.App/Input/InputCaptureSources.cs tests/AcDream.App.Tests/Input/RetainedUiInputCaptureSlotTests.cs
git commit -m "fix(app): retained UI capture slot reports keyboard focus and modals

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Input/IHotkeyFocusSource.cs", "src/AcDream.App/Input/InputCaptureSources.cs", "tests/AcDream.App.Tests/Input/RetainedUiInputCaptureSlotTests.cs"], "verifyCommand": "TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~RetainedUiInputCaptureSlotTests\"", "acceptanceCriteria": ["public IHotkeyFocusSource with HasKeyboardFocus and IsModalOpen, XML-documented", "slot implements it from Root.KeyboardFocus and Root.Modal", "three slot tests pass", "0 warnings"], "modelTier": "mechanical"}
```

---

### Task 2: Hotkeys honour interface focus and modals

**Goal:** `AppHotkeyRegistry.Bind` takes an `IHotkeyFocusSource`, `GameWindow` passes its retained capture slot, and `OnKeyDown` lets no hotkey fire while a modal is open and only Ctrl or Alt chords fire while anything has keyboard focus.

**Files:**
- Modify: `src/AcDream.App/Input/AppHotkeyRegistry.cs:22-59,162-209`
- Modify: `src/AcDream.App/Rendering/GameWindow.cs:795`
- Modify: `tests/AcDream.App.Tests/Input/AppHotkeyRegistryTests.cs`
- Test: `tests/AcDream.App.Tests/Input/HotkeyUiFocusTests.cs` (create)

**Acceptance Criteria:**
- [ ] `Bind(IKeyboardSource, KeyBindings, InputDispatcher, IHotkeyFocusSource)` throws `ArgumentNullException` for a null focus source and stores it under `_gate`
- [ ] With `HasKeyboardFocus`, plain and Shift-only chords do not fire; Ctrl and Alt chords do; plain chords fire again once focus clears
- [ ] With `IsModalOpen`, neither a plain nor a Ctrl chord fires; both fire once it clears
- [ ] The rebind-capture, `Dialog`-scope and `Chat`-scope tests still pass unchanged apart from the extra `Bind` argument
- [ ] `GameWindow` calls `_hotkeyRegistry.Bind(_kbSource, _keyBindings, value, _retainedInputCapture)`
- [ ] The three `HotkeyUiFocusTests` pass against a real `UiRoot` through a `RetainedUiInputCaptureSlot` bound after the registry
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`

**Verify:** `TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~AppHotkeyRegistryTests|FullyQualifiedName~HotkeyUiFocusTests|FullyQualifiedName~RetainedUiInputCaptureSlotTests"` → `Passed!  - Failed:     0, Passed:    25`

**Steps:**

- [ ] **Step 1: Give the existing tests a focus source**

Every existing `Bind` call in `tests/AcDream.App.Tests/Input/AppHotkeyRegistryTests.cs` gains a fourth argument (14 `registry.Bind` calls and one `restarted.Bind`):

```bash
sed -i '' \
  -e 's/registry\.Bind(keyboard, bindings, dispatcher);/registry.Bind(keyboard, bindings, dispatcher, new FakeFocus());/' \
  -e 's/restarted\.Bind(keyboard2, bindings2, dispatcher2);/restarted.Bind(keyboard2, bindings2, dispatcher2, new FakeFocus());/' \
  tests/AcDream.App.Tests/Input/AppHotkeyRegistryTests.cs
grep -c "new FakeFocus())" tests/AcDream.App.Tests/Input/AppHotkeyRegistryTests.cs
```

Expected: `15`.

In the same file, replace

```csharp
    private sealed class FakeMouse : IMouseSource
```

with

```csharp
    private sealed class FakeFocus : IHotkeyFocusSource
    {
        public bool HasKeyboardFocus { get; set; }
        public bool IsModalOpen { get; set; }
    }

    private sealed class FakeMouse : IMouseSource
```

- [ ] **Step 2: Write the failing rule tests**

In `tests/AcDream.App.Tests/Input/AppHotkeyRegistryTests.cs`, replace

```csharp
    [Theory]
    [InlineData(PluginKey.Numpad5, Key.Keypad5)]
```

with

```csharp
    [Fact]
    public void APlainChordDoesNotFireWhileAnInterfaceElementHasKeyboardFocus()
    {
        var registry = new AppHotkeyRegistry(overridesFilePath: null);
        var keyboard = new FakeKeyboard();
        var bindings = new KeyBindings();
        InputDispatcher dispatcher = InputDispatcher.CreateDetached(
            keyboard, new FakeMouse(), bindings);
        var focus = new FakeFocus();
        registry.Bind(keyboard, bindings, dispatcher, focus);

        int plainFired = 0;
        int shiftFired = 0;
        int ctrlFired = 0;
        int altFired = 0;
        registry.Register(
            "plain", "Plain", new PluginKeyChord(PluginKey.F9), () => plainFired++);
        registry.Register(
            "shift", "Shift", new PluginKeyChord(PluginKey.F9, Shift: true), () => shiftFired++);
        registry.Register(
            "ctrl", "Ctrl", new PluginKeyChord(PluginKey.F10, Ctrl: true), () => ctrlFired++);
        registry.Register(
            "alt", "Alt", new PluginKeyChord(PluginKey.F11, Alt: true), () => altFired++);

        // Chat, a text field or a focused canvas: no dispatcher scope is
        // pushed, only the interface's focus says the player is typing.
        focus.HasKeyboardFocus = true;
        keyboard.Fire(Key.F9, ModifierMask.None);
        keyboard.Fire(Key.F9, ModifierMask.Shift);
        keyboard.Fire(Key.F10, ModifierMask.Ctrl);
        keyboard.Fire(Key.F11, ModifierMask.Alt);
        Assert.Equal(0, plainFired);
        Assert.Equal(0, shiftFired);
        Assert.Equal(1, ctrlFired);
        Assert.Equal(1, altFired);

        focus.HasKeyboardFocus = false;
        keyboard.Fire(Key.F9, ModifierMask.None);
        Assert.Equal(1, plainFired);
    }

    [Fact]
    public void NoHotkeyFiresWhileAModalIsOpen()
    {
        var registry = new AppHotkeyRegistry(overridesFilePath: null);
        var keyboard = new FakeKeyboard();
        var bindings = new KeyBindings();
        InputDispatcher dispatcher = InputDispatcher.CreateDetached(
            keyboard, new FakeMouse(), bindings);
        var focus = new FakeFocus();
        registry.Bind(keyboard, bindings, dispatcher, focus);

        int plainFired = 0;
        int ctrlFired = 0;
        registry.Register(
            "plain", "Plain", new PluginKeyChord(PluginKey.F9), () => plainFired++);
        registry.Register(
            "quick-heal", "Quick Heal", new PluginKeyChord(PluginKey.H, Ctrl: true), () => ctrlFired++);

        // A modal blocks Ctrl and Alt chords too, unlike keyboard focus.
        focus.IsModalOpen = true;
        keyboard.Fire(Key.F9, ModifierMask.None);
        keyboard.Fire(Key.H, ModifierMask.Ctrl);
        Assert.Equal(0, plainFired);
        Assert.Equal(0, ctrlFired);

        focus.IsModalOpen = false;
        keyboard.Fire(Key.F9, ModifierMask.None);
        keyboard.Fire(Key.H, ModifierMask.Ctrl);
        Assert.Equal(1, plainFired);
        Assert.Equal(1, ctrlFired);
    }

    [Theory]
    [InlineData(PluginKey.Numpad5, Key.Keypad5)]
```

- [ ] **Step 3: Write the failing host-level tests**

Create `tests/AcDream.App.Tests/Input/HotkeyUiFocusTests.cs`:

```csharp
using AcDream.App.Input;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.Tests.Input;

// The registry bound the way GameWindow binds it: to the retained
// interface's capture slot, before the interface exists, so the real
// UiRoot's focus and modal decide which hotkeys fire.
public sealed class HotkeyUiFocusTests
{
    [Fact]
    public void AFocusedInterfaceElementLetsOnlyCtrlAndAltChordsFire()
    {
        var harness = new Harness();
        var field = new UiField { Width = 200, Height = 20 };
        harness.Root.AddChild(field);
        int plainFired = 0;
        int ctrlFired = 0;
        harness.Registry.Register(
            "plain", "Plain", new PluginKeyChord(PluginKey.F9), () => plainFired++);
        harness.Registry.Register(
            "quick-heal", "Quick Heal", new PluginKeyChord(PluginKey.H, Ctrl: true), () => ctrlFired++);

        harness.Root.SetKeyboardFocus(field);
        harness.Keyboard.Fire(Key.F9, ModifierMask.None);
        harness.Keyboard.Fire(Key.H, ModifierMask.Ctrl);
        Assert.Equal(0, plainFired);
        Assert.Equal(1, ctrlFired);

        harness.Root.SetKeyboardFocus(null);
        harness.Keyboard.Fire(Key.F9, ModifierMask.None);
        Assert.Equal(1, plainFired);
    }

    [Fact]
    public void AnOpenModalLetsNoHotkeyFire()
    {
        var harness = new Harness();
        var dialog = new UiPanel { Width = 300, Height = 150 };
        harness.Root.AddChild(dialog);
        int fired = 0;
        harness.Registry.Register(
            "quick-heal", "Quick Heal", new PluginKeyChord(PluginKey.H, Ctrl: true), () => fired++);

        harness.Root.Modal = dialog;
        harness.Keyboard.Fire(Key.H, ModifierMask.Ctrl);
        Assert.Equal(0, fired);

        harness.Root.Modal = null;
        harness.Keyboard.Fire(Key.H, ModifierMask.Ctrl);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void HotkeysFireAgainOnceTheFocusedInterfaceIsTornDown()
    {
        var harness = new Harness();
        var field = new UiField { Width = 200, Height = 20 };
        harness.Root.AddChild(field);
        int fired = 0;
        harness.Registry.Register(
            "plain", "Plain", new PluginKeyChord(PluginKey.F9), () => fired++);

        harness.Root.SetKeyboardFocus(field);
        harness.UiBinding.Dispose();
        harness.Keyboard.Fire(Key.F9, ModifierMask.None);
        Assert.Equal(1, fired);
    }

    private sealed class Harness
    {
        public Harness()
        {
            var bindings = new KeyBindings();
            InputDispatcher dispatcher = InputDispatcher.CreateDetached(
                Keyboard, new FakeMouse(), bindings);
            var slot = new RetainedUiInputCaptureSlot();
            Registry.Bind(Keyboard, bindings, dispatcher, slot);
            UiBinding = slot.Bind(Root);
        }

        public AppHotkeyRegistry Registry { get; } = new(overridesFilePath: null);
        public FakeKeyboard Keyboard { get; } = new();
        public UiRoot Root { get; } = new() { Width = 800, Height = 600 };
        public IDisposable UiBinding { get; }
    }

    private sealed class FakeKeyboard : IKeyboardSource
    {
        public event Action<Key, ModifierMask>? KeyDown;
#pragma warning disable CS0067
        public event Action<Key, ModifierMask>? KeyUp;
#pragma warning restore CS0067
        public bool IsHeld(Key key) => false;
        public ModifierMask CurrentModifiers => ModifierMask.None;

        public void Fire(Key key, ModifierMask modifiers) =>
            KeyDown?.Invoke(key, modifiers);
    }

    private sealed class FakeMouse : IMouseSource
    {
#pragma warning disable CS0067
        public event Action<MouseButton, ModifierMask>? MouseDown;
        public event Action<MouseButton, ModifierMask>? MouseUp;
        public event Action<float, float>? MouseMove;
        public event Action<float>? Scroll;
#pragma warning restore CS0067
        public bool WantCaptureKeyboard { get; set; }
        public bool WantCaptureMouse { get; set; }
        public bool IsHeld(MouseButton button) => false;
    }
}
```

- [ ] **Step 4: Run them to see them fail**

Run: `TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~AppHotkeyRegistryTests|FullyQualifiedName~HotkeyUiFocusTests"`
Expected: build FAILS with `error CS1501: No overload for method 'Bind' takes 4 arguments`.

- [ ] **Step 5: Take the focus source in `Bind`**

In `src/AcDream.App/Input/AppHotkeyRegistry.cs`, replace

```csharp
    private InputDispatcher? _dispatcher;
    private bool _keyboardHooked;
```

with

```csharp
    private InputDispatcher? _dispatcher;
    private IHotkeyFocusSource? _focus;
    private bool _keyboardHooked;
```

and replace

```csharp
    /// <summary>
    /// Wires the registry to the live keyboard source and the client's own
    /// bindings (for collision detection) and the dispatcher (for the
    /// active-scope chat-focus check). Every hotkey registered before this
    /// call is resolved and armed now.
    /// </summary>
    public void Bind(
        IKeyboardSource keyboard,
        KeyBindings clientBindings,
        InputDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        ArgumentNullException.ThrowIfNull(clientBindings);
        ArgumentNullException.ThrowIfNull(dispatcher);
        Entry[] pending;
        lock (_gate)
        {
            _keyboard = keyboard;
            _clientBindings = clientBindings;
            _dispatcher = dispatcher;
```

with

```csharp
    /// <summary>
    /// Wires the registry to the live keyboard source and the client's own
    /// bindings (for collision detection), the dispatcher (for rebind
    /// capture and its input scopes) and the interface's focus state (for
    /// keyboard focus and modals). Every hotkey registered before this call
    /// is resolved and armed now.
    /// </summary>
    public void Bind(
        IKeyboardSource keyboard,
        KeyBindings clientBindings,
        InputDispatcher dispatcher,
        IHotkeyFocusSource focus)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        ArgumentNullException.ThrowIfNull(clientBindings);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(focus);
        Entry[] pending;
        lock (_gate)
        {
            _keyboard = keyboard;
            _clientBindings = clientBindings;
            _dispatcher = dispatcher;
            _focus = focus;
```

- [ ] **Step 6: Apply the rules in `OnKeyDown`**

In the same file, replace

```csharp
    // Not routed through InputDispatcher's action/scope machinery -- a
    // dynamic per-plugin action space large enough for that would be a much
    // bigger change (see the plugin-api.md Hotkeys note for the recorded
    // deviation). This still honours the two conditions that matter most:
    // a rebind capture in progress (BeginCapture) and a modal scope
    // (Dialog/EditField, not just Chat) both suppress every hotkey, the
    // same way the dispatcher itself would refuse to route a client
    // action into a text field or a capture-in-progress rebind screen.
    private void OnKeyDown(Key key, ModifierMask modifiers)
    {
        Entry[] snapshot;
        InputDispatcher? dispatcher;
        lock (_gate)
        {
            snapshot = _entries.ToArray();
            dispatcher = _dispatcher;
        }
        if (dispatcher is not null && dispatcher.IsCapturing)
            return;
        InputScope? activeScope = dispatcher?.ActiveScope;
        bool chatFocused = activeScope == InputScope.Chat;
        bool modalScope = activeScope is InputScope.Dialog or InputScope.EditField;
        if (modalScope)
            return;
```

with

```csharp
    // Not routed through InputDispatcher's action/scope machinery -- a
    // dynamic per-plugin action space large enough for that would be a much
    // bigger change (see the plugin-api.md Hotkeys note for the recorded
    // deviation). A rebind capture in progress (BeginCapture), an open
    // modal and a modal scope (Dialog/EditField) suppress every hotkey;
    // while anything holds keyboard focus (chat, a text field) only a chord
    // with Ctrl or Alt fires, so typed letters are never hotkey presses.
    // The interface's focus is the live signal: no production code pushes
    // a dispatcher scope. This handler runs before the interface's own key
    // handler, so it sees the focus from before this key is handled.
    private void OnKeyDown(Key key, ModifierMask modifiers)
    {
        Entry[] snapshot;
        InputDispatcher? dispatcher;
        IHotkeyFocusSource? focus;
        lock (_gate)
        {
            snapshot = _entries.ToArray();
            dispatcher = _dispatcher;
            focus = _focus;
        }
        if (dispatcher is not null && dispatcher.IsCapturing)
            return;
        InputScope? activeScope = dispatcher?.ActiveScope;
        bool modalScope = activeScope is InputScope.Dialog or InputScope.EditField;
        if (modalScope || focus?.IsModalOpen == true)
            return;
        bool typing = activeScope == InputScope.Chat
            || focus?.HasKeyboardFocus == true;
```

and replace

```csharp
            if (chatFocused && !chord.Ctrl && !chord.Alt) continue;
```

with

```csharp
            if (typing && !chord.Ctrl && !chord.Alt) continue;
```

- [ ] **Step 7: Pass the slot from `GameWindow`**

In `src/AcDream.App/Rendering/GameWindow.cs` (`PublishInputDispatcher`), replace

```csharp
        _hotkeyRegistry.Bind(_kbSource, _keyBindings, value);
```

with

```csharp
        _hotkeyRegistry.Bind(_kbSource, _keyBindings, value, _retainedInputCapture);
```

`_retainedInputCapture` is assigned in the constructor, before composition publishes the dispatcher, and is the same slot `InteractionRetainedUiComposition` binds to each `UiRoot` (`d.RetainedInputCapture.Bind(host.Root)`), so the registry follows interface rebuilds without rebinding.

- [ ] **Step 8: Run the tests to see them pass, and build**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~AppHotkeyRegistryTests|FullyQualifiedName~HotkeyUiFocusTests|FullyQualifiedName~RetainedUiInputCaptureSlotTests" 2>&1 | grep -E "Passed!|Failed!"
```

Expected: `0 Warning(s)`, `0 Error(s)`; `Passed!  - Failed:     0, Passed:    25` (19 `AppHotkeyRegistryTests` cases: the 12 existing facts, the five-case theory and the 2 new facts; 3 `HotkeyUiFocusTests`; 3 `RetainedUiInputCaptureSlotTests`).

If the count differs, list the tests with `--list-tests` and check the two new `AppHotkeyRegistryTests` and the three `HotkeyUiFocusTests` are there.

- [ ] **Step 9: See the rules fail without the fix**

Remove the two checks once, confirm the four rule tests fail, then restore:

```bash
cp src/AcDream.App/Input/AppHotkeyRegistry.cs /tmp/openac-hotkeys/AppHotkeyRegistry.cs.keep
sed -i '' -e 's/if (modalScope || focus?.IsModalOpen == true)/if (modalScope)/' \
  -e 's/|| focus?.HasKeyboardFocus == true;/|| false;/' src/AcDream.App/Input/AppHotkeyRegistry.cs
TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~AppHotkeyRegistryTests|FullyQualifiedName~HotkeyUiFocusTests" 2>&1 | grep -E "^\s+Failed |Failed!"
cp /tmp/openac-hotkeys/AppHotkeyRegistry.cs.keep src/AcDream.App/Input/AppHotkeyRegistry.cs
git diff --stat -- src/AcDream.App/Input/AppHotkeyRegistry.cs
```

Expected: exactly these four fail — `APlainChordDoesNotFireWhileAnInterfaceElementHasKeyboardFocus`, `NoHotkeyFiresWhileAModalIsOpen`, `AFocusedInterfaceElementLetsOnlyCtrlAndAltChordsFire`, `AnOpenModalLetsNoHotkeyFire`. After the restore the diff stat shows the file with the fix still in it.

- [ ] **Step 10: Commit**

```bash
git add src/AcDream.App/Input/AppHotkeyRegistry.cs src/AcDream.App/Rendering/GameWindow.cs tests/AcDream.App.Tests/Input/AppHotkeyRegistryTests.cs tests/AcDream.App.Tests/Input/HotkeyUiFocusTests.cs
git commit -m "fix(app): plugin hotkeys respect interface focus and modals

No production code pushes an input-dispatcher scope, so the Chat and
Dialog checks never fired and a plain letter hotkey went off while the
player typed in chat. The registry now reads the retained interface's
keyboard focus and modal through the capture slot GameWindow owns.

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Input/AppHotkeyRegistry.cs", "src/AcDream.App/Rendering/GameWindow.cs", "tests/AcDream.App.Tests/Input/AppHotkeyRegistryTests.cs", "tests/AcDream.App.Tests/Input/HotkeyUiFocusTests.cs"], "verifyCommand": "TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~AppHotkeyRegistryTests|FullyQualifiedName~HotkeyUiFocusTests|FullyQualifiedName~RetainedUiInputCaptureSlotTests\"", "acceptanceCriteria": ["Bind takes and null-checks an IHotkeyFocusSource", "keyboard focus lets only Ctrl/Alt chords fire", "an open modal lets nothing fire", "existing capture/Dialog/Chat tests still pass", "GameWindow passes _retainedInputCapture", "three HotkeyUiFocusTests pass", "four rule tests fail with the checks removed", "0 warnings"], "modelTier": "standard"}
```

---

### Task 3: Documentation

**Goal:** The plugin API guide's Hotkeys section describes the focus and modal rules as they now behave, including the key-order caveat.

**Files:**
- Modify: `docs/plugin-api.md` (section "Hotkeys", the two paragraphs starting `A hotkey does not fire while the chat bar` and `Plugin hotkeys are a raw keyboard subscription`)

**Acceptance Criteria:**
- [ ] The section says that while anything has keyboard focus (chat bar, text field, a control reached with Tab) only Ctrl or Alt chords fire, and that clicking a button does not give it focus
- [ ] It says no hotkey fires at all while a modal dialog is open or a rebind is being captured
- [ ] It says hotkeys see each key before the interface does, so a key is judged by the focus from before it
- [ ] It no longer mentions `Dialog`/`EditField` scopes or "the chat bar has keyboard focus" as the only case

**Verify:** `grep -c "a control reached with Tab\|judged by the focus from before" docs/plugin-api.md && ! grep -n "EditField" docs/plugin-api.md` → `2`, then no output

**Steps:**

- [ ] **Step 1: Rewrite the two paragraphs**

In `docs/plugin-api.md`, replace

```markdown
A hotkey does not fire while the chat bar has keyboard focus unless Ctrl or
Alt is part of the chord — otherwise every letter typed into chat would also
be a candidate hotkey press.

Plugin hotkeys are a raw keyboard subscription, not a route through
InputDispatcher's action/scope engine (a dynamic per-plugin action space
large enough to fit that machinery would be a much bigger change than the
rest of this surface) -- documented deviation. Two dispatcher states still
suppress every hotkey, matching how the dispatcher itself would refuse to
route a client action in the same situations: a rebind capture in progress
(`InputDispatcher.BeginCapture`) and a modal `Dialog`/`EditField` scope
pushed on top (not just `Chat`, which has its own Ctrl/Alt carve-out
above).
```

with

```markdown
While anything in the interface has keyboard focus (the chat bar, a text
field, a control reached with Tab), a hotkey fires only if Ctrl or Alt is
part of the chord; otherwise every letter typed would also be a candidate
hotkey press. Clicking a button does not give it keyboard focus. While a
modal dialog is open, or a key rebind is being captured
(`InputDispatcher.BeginCapture`), no hotkey fires at all, Ctrl and Alt
chords included.

Plugin hotkeys are a raw keyboard subscription, not a route through
InputDispatcher's action/scope engine (a dynamic per-plugin action space
large enough to fit that machinery would be a much bigger change than the
rest of this surface) -- documented deviation. They see each key before the
interface handles it, so a key is judged by the focus from before that key:
a key press that moves focus into or out of a text field is judged by where
focus was.
```

- [ ] **Step 2: Check and commit**

```bash
grep -c "a control reached with Tab\|judged by the focus from before" docs/plugin-api.md
grep -n "EditField" docs/plugin-api.md || echo "no EditField"
git add docs/plugin-api.md
git commit -m "docs: plugin hotkeys follow interface focus and modals

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

Expected: `2`, `no EditField`.

```json:metadata
{"files": ["docs/plugin-api.md"], "verifyCommand": "grep -c \"a control reached with Tab\\|judged by the focus from before\" docs/plugin-api.md && ! grep -n \"EditField\" docs/plugin-api.md", "acceptanceCriteria": ["focus rule with Ctrl/Alt carve-out and click note", "modal and rebind capture block everything", "key-order caveat", "no Dialog/EditField scope wording"], "modelTier": "mechanical"}
```

---

### Task 4: Full verification and push

**Goal:** The branch builds with 0 warnings, the full portable suite shows no failures beyond Task 0's baseline, and the branch is pushed to `origin`.

**Files:** none

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`
- [ ] The portable suite with `TMPDIR=/tmp/` fails only tests listed in `/tmp/openac-hotkeys/baseline-failures.txt` (HostParity peer tests may vary)
- [ ] No staged or committed `packages.*.lock.json` changes, and nothing under `docs/superpowers`
- [ ] `git log --oneline upstream/main..HEAD` shows the three commits of Tasks 1–3
- [ ] `origin/fix/hotkey-focus-scope` equals `HEAD`

**Verify:** `git fetch origin && test "$(git rev-parse HEAD)" = "$(git rev-parse origin/fix/hotkey-focus-scope)" && echo pushed` → `pushed`

**Steps:**

- [ ] **Step 1: Build and test everything**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test AcDream.slnx -c Release --no-build --filter "Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure" 2>&1 \
  | grep -E "^\s+Failed " | sed -E 's/ \[[^]]*\]$//' | sort -u > /tmp/openac-hotkeys/final-failures.txt
comm -23 /tmp/openac-hotkeys/final-failures.txt /tmp/openac-hotkeys/baseline-failures.txt | grep -v "HostParity.Tests.Peer" || echo "no new failures"
```

Expected: `0 Warning(s)`, `0 Error(s)`; `no new failures`. If a non-peer test fails, investigate with superpowers-extended-cc:systematic-debugging before going on.

- [ ] **Step 2: Check what the branch carries**

```bash
git checkout -- '*.lock.json'
git status --short
git log --oneline upstream/main..HEAD
git diff --name-only upstream/main..HEAD | grep -E "lock.json|docs/superpowers" || echo clean
```

Expected: a clean status, three commits, `clean`.

- [ ] **Step 3: Push**

```bash
git push -u origin fix/hotkey-focus-scope
git fetch origin && test "$(git rev-parse HEAD)" = "$(git rev-parse origin/fix/hotkey-focus-scope)" && echo pushed
```

Expected: `pushed`. Do not open a pull request: ask the user which repository it goes to.

```json:metadata
{"files": [], "verifyCommand": "git fetch origin && test \"$(git rev-parse HEAD)\" = \"$(git rev-parse origin/fix/hotkey-focus-scope)\" && echo pushed", "acceptanceCriteria": ["0 warnings", "no portable failures beyond baseline", "no lock files or docs/superpowers", "three commits", "pushed to origin"], "modelTier": "mechanical"}
```

---

### Task 5: Merge into fork main

**Goal:** Fork `main` takes `fix/hotkey-focus-scope` with a merge commit, as it took PRs 3, 4 and 5, builds with 0 warnings, passes the portable suite, and is pushed; the feature branch itself is unchanged.

**Files:** none (a conflict-free merge on `main`)

**Acceptance Criteria:**
- [ ] `git log -1 --format=%P main` lists two parents: the previous `origin/main` and `fix/hotkey-focus-scope`
- [ ] The merge has no conflicts
- [ ] `dotnet build AcDream.slnx -c Release` on `main` → `0 Warning(s)`, `0 Error(s)`
- [ ] The portable suite on `main` fails only baseline tests
- [ ] `origin/main` equals local `main`; `origin/fix/hotkey-focus-scope` is unchanged

**Verify:** `git fetch origin && test "$(git rev-parse main)" = "$(git rev-parse origin/main)" && git log -1 --format=%P main | wc -w` → `2`

**Steps:**

- [ ] **Step 1: Merge**

```bash
git show-ref --verify --quiet refs/heads/main && git switch main || git switch -c main --track origin/main
git pull --ff-only origin main
git rev-parse --short HEAD   # expect a4ab9664 unless fork main moved; if it moved, check the next command still lists none of this PR's files
git diff --name-only bbc83275 HEAD -- src/AcDream.App/Input/ src/AcDream.App/Rendering/GameWindow.cs tests/AcDream.App.Tests/Input/ docs/plugin-api.md | grep -E "AppHotkeyRegistry|InputCaptureSources|IHotkeyFocusSource|GameWindow.cs|RetainedUiInputCaptureSlotTests|HotkeyUiFocusTests" || echo "no overlap"
git merge --no-ff fix/hotkey-focus-scope -m "Merge fix/hotkey-focus-scope: plugin hotkeys respect interface focus (PR 6a)

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

Expected: `no overlap` (fork `main` at `a4ab9664` changes `docs/plugin-api.md` only in its Canvases section, far from Hotkeys), then a merge with no conflicts. If git reports a conflict, stop and show it to the user.

- [ ] **Step 2: Build, test, push**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test AcDream.slnx -c Release --no-build --filter "Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure" 2>&1 \
  | grep -E "^\s+Failed " | sed -E 's/ \[[^]]*\]$//' | sort -u > /tmp/openac-hotkeys/merge-failures.txt
comm -23 /tmp/openac-hotkeys/merge-failures.txt /tmp/openac-hotkeys/baseline-failures.txt | grep -v "HostParity.Tests.Peer" || echo "no new failures"
git checkout -- '*.lock.json'
git push origin main
git fetch origin && test "$(git rev-parse main)" = "$(git rev-parse origin/main)" && git log -1 --format=%P main | wc -w
```

Expected: `0 Warning(s)`, `0 Error(s)`; `no new failures`; `2`. The baseline was recorded on upstream `main`; a failure that is new here but also fails on `origin/main` before the merge is not this PR's (check it there before going on).

```json:metadata
{"files": [], "verifyCommand": "git fetch origin && test \"$(git rev-parse main)\" = \"$(git rev-parse origin/main)\" && git log -1 --format=%P main | wc -w", "acceptanceCriteria": ["merge commit with two parents", "no conflicts", "0 warnings on main", "no portable failures beyond baseline", "origin/main pushed, feature branch unchanged"], "modelTier": "mechanical"}
```
