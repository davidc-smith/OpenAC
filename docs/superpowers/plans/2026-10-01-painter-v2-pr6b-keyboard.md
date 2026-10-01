# Painter v2 — PR 6b: Canvas Keyboard Input Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A plugin canvas that opts in can take the interface's keyboard focus, from a left press or on request, and while it has focus receives keys, typed text and host-generated repeat, the game receives none, and focus goes back with exactly one `FocusLost` on every path that ends it.

**Architecture:** Focus belongs to the retained interface's `UiRoot` (`KeyboardFocus`), as it does for the chat bar and text fields, so game actions are already suppressed (`WantCaptureKeyboard`) and plugin hotkeys already follow PR 6a's rule. `PluginCanvasElement` becomes a focusable edit control: it computes `AcceptsFocus` from live state (so `UiElement.AcceptsFocus` becomes virtual), turns the root's `FocusGained`/`FocusLost`/`KeyDown`/`KeyUp`/`Char` events into `PluginKeyEvent`s for the plugin's handler under a third guard, repeats a held key itself, and gives focus back when it can no longer hold it. The plugin's handle reaches the mounted element through a small internal interface the element sets on its registration. The `PluginKey` ↔ Silk `Key` table moves out of `AppHotkeyRegistry` into a shared `PluginKeyMap`.

**Tech Stack:** C# / .NET 10, xUnit, Silk.NET input types.

**Spec:** `docs/superpowers/specs/2026-09-30-plugin-painter-v2-design.md` (section 10, the cross-cutting rules in section 2, and the PR 6b entries under "Plan-time corrections", which supersede section 10 where they differ).

**Checked:** this plan's code was applied to `fix/hotkey-focus-scope` (`f951ce16`) in a throwaway worktree, one commit per task, before hand-off. Every code block below was generated from those commits by a script that applies each "replace" in order, requires its anchor to occur exactly once at that moment, and compares the result with the commit byte for byte. Each task built with 0 warnings and its tests passed; the "run it to see it fail" steps were run too. The finished tree passed the full portable suite apart from 3 HostParity peer tests (baseline). It was also scratch-merged into fork `main` (`301852fc`); Task 8 lists the seven conflicts and their resolutions. The merged tree built with 0 warnings, passed the portable suite apart from 5 HostParity peer tests (baseline) and the 5 `Lane=Vulkan` canvas tests. Mutation checks: removing any one of these made at least one new test fail: the unhandled-Escape release, the modal check per tick, the handler check per tick, the computed `AcceptsFocus`, the modal and rebind refusals in `RequestFocus`, the control-character filter, the check before each key, and dropping the key handler on dispose.

## Global Constraints

- Branch `painter-v2/keyboard`, created from `origin/fix/hotkey-focus-scope` at `f951ce16` (PR 6a, which is upstream `main` `bbc83275` plus four commits). PR 6b depends on PR 6a only, so, as the spec's order of work says, it starts from that branch until 6a merges upstream; it can then go upstream as one topic on top of 6a. Nothing under `docs/superpowers/` goes on a code branch.
- Contract additions are additive with inert defaults: `PluginCanvasDescriptor.AcceptsKeyboardInput` (`init`, default false); `PluginKeyEventKind { Down, Up, Text, FocusGained, FocusLost }`; `PluginKeyEvent(Kind, Key, Modifiers, IsRepeat = false, Text = null)`; `IPluginCanvas.KeyHandler` (`Func<PluginKeyEvent, bool>?`, default get null / set ignored), `HasKeyboardFocus` (default false), `RequestKeyboardFocus()` (default false), `ReleaseKeyboardFocus()` (default no-op). `NoOpPluginCanvas` keeps the handler and answers false. Every new member has XML docs (the Abstractions build fails on undocumented members).
- Focus is the root's. The canvas takes it only through `UiRoot.SetKeyboardFocus` (the root's own left-press path, or `RequestFocus`) and gives it back only through `UiRoot.SetKeyboardFocus(null)` while the root's focus is still this canvas (as `CreditsUiController` does). `FocusGained` and `FocusLost` reach the plugin only from the root's focus events, which is what makes "exactly one `FocusLost`" hold.
- `RequestKeyboardFocus` succeeds only when: the canvas opted in, is not torn down, is shown (registration visible, not dropped, and every ancestor visible), has a key handler that has not been dropped, the root has no keyboard focus, no modal is open, and no rebind capture is running (`_bindings.Keyboard?.Dispatcher?.IsCapturing`). It answers true at once if the canvas already has focus.
- Focus goes back on: an Escape `Down` the handler did not handle; a left press elsewhere; the canvas hidden (the root clears focus on hide) or removed; the handler set to null or dropped by its guard; the paint callback dropped (it hides the canvas); a modal opening; interface teardown; `ReleaseKeyboardFocus()`. The handler-null, hidden and modal checks run every tick and before every key.
- Repeat: 0.40 s delay, 0.04 s interval, at most one repeat per tick, of the last key that went down; it stops on that key's `Up`, on another key going down, and on focus loss.
- Key guard: `UiDrawCallbackGuard` with `FrameBudgetMilliseconds` (2 ms) and `callUnit: "events"`, named `plugin canvas {owner}/{id} key handler`. Tripping it gives focus back and stops key delivery; pointer input and painting continue.
- Commit subjects in this series' style (`plugin api: …`, `plugin canvas: …`, `input: …`, `docs: …`). Every commit message ends with `Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB`.
- Environment for every command: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. `Lane=Vulkan` tests also need `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib`.
- Builds on this Mac rewrite `src/AcDream.Launcher/packages.neutral.lock.json`, and an unlocked restore touches `packages.osx-arm64.lock.json` files. Never stage a `packages.*.lock.json`. Stage files by explicit path, and run `git checkout -- '*.lock.json'` before pushing.
- The first solution build in a fresh worktree can fail once with `NETSDK1047 … AcDream.Bake/obj/project.assets.json doesn't have a target for 'net10.0/osx-arm64'`, a restore race. Build again; the second build is clean.
- Portable gate filter: `Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure`.
- Baseline failures on this Mac are not this PR's: run the full portable suite with `TMPDIR=/tmp/` (the default temp path makes Unix-socket paths longer than 104 characters). 2–7 `AcDream.HostParity.Tests` `Peer*ParityTests` fail, varying run to run (3 on the prototype, 5 on the scratch merge). Earlier baselines also listed `GraphicalPluginSessionTests.ReloadCommandLoadsAFreshCopyAndTheOldOneLeavesMemory`, `GameWindowRenderLeafCompositionTests.PaperdollComposition_SkipsEitherMissingOptionalUiSurface`, `LiveEntityNetworkUpdateControllerForcePositionWiringTests.CommittedOrDeferredCellReturnsBeforeReachingTheGenericTail`, three `RenderPackValidatorCommandTests.External*` and `HeadlessSessionIsolationTests.ThirtySessionMixedWorkloadMaintainsIsolationAndConverges`; they passed during planning but may fail again. Record the baseline set in Task 0 and compare against it; do not try to fix them here.
- The unit tests must run outside the command sandbox, or the peer and pipe tests fail on socket permissions too.

**User decisions (already made):**
- "One spec, a PR series"; this is PR 6b (`painter-v2/keyboard`), discussed and agreed 2026-09-30, depending on PR 6a.
- PR 6b's branch is merged into fork `main` with a merge commit, as PRs 3, 4, 5 and 6a were.
- Branches are pushed to `origin`. Do not open a pull request without asking which repository it goes to.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AcDream.Plugin.Abstractions/PluginCanvas.cs` | Modify | Descriptor flag, key event types, the four `IPluginCanvas` members, `NoOpPluginCanvas` answers. |
| `tests/AcDream.Plugin.Tests/PluginCanvasContractTests.cs` | Modify | Defaults and headless answers. |
| `src/AcDream.App/Input/PluginKeyMap.cs` | Create | The one `PluginKey` ↔ Silk `Key` table, both directions. |
| `src/AcDream.App/Input/AppHotkeyRegistry.cs` | Modify | Uses `PluginKeyMap.ToSilk`; its own `MapKey` goes. |
| `tests/AcDream.App.Tests/Input/PluginKeyMapTests.cs` | Create | Every named key round-trips; unnamed keys map to nothing. |
| `src/AcDream.App/Plugins/IPluginCanvasKeyboardFocus.cs` | Create | How a mounted element answers its registration's focus calls. |
| `src/AcDream.App/Plugins/PluginCanvasRegistration.cs` | Modify | Holds the key handler and the element's focus hook; forgets the handler on dispose. |
| `src/AcDream.Core/Plugins/ScopedPluginHost.cs` | Modify | `IndividualCanvas` forwards the four members. |
| `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryCanvasTests.cs` | Modify | The four members reach the host's canvas. |
| `src/AcDream.App/UI/UiElement.cs` | Modify | `AcceptsFocus` becomes virtual. |
| `src/AcDream.App/UI/Layout/PluginCanvasElement.cs` | Modify | Focus (Task 4), then keys, text and repeat (Task 5). |
| `src/AcDream.App/UI/RetailUiRuntime.cs` | Modify | Passes the rebind-capture signal when mounting a canvas. |
| `tests/AcDream.App.Tests/UI/Layout/PluginCanvasKeyboardTests.cs` | Create | Element tests for every focus path, refusal, key, repeat, the Escape policy and the guard. |
| `docs/plugin-api.md` | Modify | New "Keyboard input" subsection under Canvases; Canvases intro and Hotkeys mention it. |
| `docs/plugin-ui-markup.md` | Modify | Test list names the new suites. |

---

### Task 0: Create the branch and record the baseline

**Goal:** `painter-v2/keyboard` from `origin/fix/hotkey-focus-scope`, building cleanly, with the baseline test failures written down.

**Files:** none

**Acceptance Criteria:**
- [ ] `git branch --show-current` prints `painter-v2/keyboard`
- [ ] `git rev-parse --short HEAD` is `f951ce16` (`origin/fix/hotkey-focus-scope`)
- [ ] `docs/superpowers` does not exist in the working tree
- [ ] `dotnet build AcDream.slnx -c Release` reports `0 Warning(s)` and `0 Error(s)`
- [ ] The names of the portable-suite failures (run with `TMPDIR=/tmp/`) are saved to `/tmp/openac-keyboard/baseline-failures.txt`

**Verify:** `git branch --show-current && test "$(git rev-parse HEAD)" = "$(git rev-parse origin/fix/hotkey-focus-scope)" && test ! -e docs/superpowers && echo ok` → `painter-v2/keyboard`, `ok`

**Steps:**

- [ ] **Step 1: Branch**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git fetch origin
git rev-parse --short origin/fix/hotkey-focus-scope   # expect f951ce16; if it moved, stop and ask
git worktree add -b painter-v2/keyboard .worktrees/keyboard origin/fix/hotkey-focus-scope
cd .worktrees/keyboard
test "$(git rev-parse HEAD)" = "$(git rev-parse origin/fix/hotkey-focus-scope)" && test ! -e docs/superpowers && echo ok
```

Expected: `f951ce16`, then `ok`. Every later command runs in `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/keyboard` (ignored by git). The main checkout stays on `docs/painter-v2-spec`, where this plan lives. Planning left throwaway worktrees `.worktrees/keyboard-proto`, `.worktrees/keyboard-split` and `.worktrees/keyboard-merge`; leave them alone (Task 8 removes them).

- [ ] **Step 2: Baseline build and tests**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
mkdir -p /tmp/openac-keyboard
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test AcDream.slnx -c Release --no-build --filter "Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure" 2>&1 \
  | grep -E "^\s+Failed " | sed -E 's/ \[[^]]*\]$//' | sort -u > /tmp/openac-keyboard/baseline-failures.txt
cat /tmp/openac-keyboard/baseline-failures.txt
git checkout -- '*.lock.json'
```

Expected: `0 Warning(s)`, `0 Error(s)`. The failures listed are among the baseline set under Global Constraints.

```json:metadata
{"files": [], "verifyCommand": "git branch --show-current && test \"$(git rev-parse HEAD)\" = \"$(git rev-parse origin/fix/hotkey-focus-scope)\" && test ! -e docs/superpowers && echo ok", "acceptanceCriteria": ["on painter-v2/keyboard", "HEAD is origin/fix/hotkey-focus-scope f951ce16", "no docs/superpowers", "0 warnings", "baseline failures recorded"], "modelTier": "mechanical"}
```

---

### Task 1: The keyboard contract

**Goal:** The plugin contract gains `AcceptsKeyboardInput`, `PluginKeyEventKind`, `PluginKeyEvent` and the four `IPluginCanvas` keyboard members with inert defaults, and `NoOpPluginCanvas` keeps the handler and never focuses.

**Files:**
- Modify: `src/AcDream.Plugin.Abstractions/PluginCanvas.cs`
- Test: `tests/AcDream.Plugin.Tests/PluginCanvasContractTests.cs`

**Acceptance Criteria:**
- [ ] `new PluginCanvasDescriptor(...)` has `AcceptsKeyboardInput == false`
- [ ] `new PluginKeyEvent(kind, key, modifiers)` has `IsRepeat == false` and `Text == null`
- [ ] A canvas from a registry that never heard of canvases (`NoOpPluginCanvas`) keeps `KeyHandler`, answers `RequestKeyboardFocus() == false`, `HasKeyboardFocus == false`
- [ ] An `IPluginCanvas` implemented without the new members answers `KeyHandler == null` after a set, and false for the other two
- [ ] `FakePluginHost`'s canvas keeps the handler, never calls it, never focuses
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`

**Verify:** `TMPDIR=/tmp/ dotnet test tests/AcDream.Plugin.Tests -c Release --filter "FullyQualifiedName~PluginCanvasContractTests"` → `Passed!  - Failed:     0, Passed:    12`

**Steps:**

- [ ] **Step 1: Write the failing tests**

In `tests/AcDream.Plugin.Tests/PluginCanvasContractTests.cs`, replace

```csharp
        Assert.True(descriptor.StartVisible);
        Assert.False(descriptor.AcceptsPointerInput);
    }

```

with

```csharp
        Assert.True(descriptor.StartVisible);
        Assert.False(descriptor.AcceptsPointerInput);
        Assert.False(descriptor.AcceptsKeyboardInput);
    }

    [Fact]
    public void AKeyEventDefaultsToNoRepeatAndNoText()
    {
        var down = new PluginKeyEvent(PluginKeyEventKind.Down, PluginKey.A, PluginKeyModifiers.Shift);

        Assert.False(down.IsRepeat);
        Assert.Null(down.Text);
        Assert.Equal(PluginKey.A, down.Key);
    }

    [Fact]
    public void ARegistryThatNeverHeardOfKeyboardInputKeepsNothingAndNeverFocuses()
    {
        IUiRegistry registry = new BareRegistry();

        IPluginCanvas canvas = registry.RegisterCanvas(
            new PluginCanvasDescriptor("pad", 64, 64) { AcceptsKeyboardInput = true }, _ => { });
        canvas.KeyHandler = _ => true;
        canvas.ReleaseKeyboardFocus();

        // NoOpPluginCanvas keeps the handler; an older host's canvas
        // answers with the interface defaults.
        Assert.NotNull(canvas.KeyHandler);
        Assert.False(canvas.RequestKeyboardFocus());
        Assert.False(canvas.HasKeyboardFocus);
    }

    [Fact]
    public void AnOlderHostsCanvasAnswersTheKeyboardMembersWithTheirDefaults()
    {
        IPluginCanvas canvas = new PointerOnlyCanvas();

        canvas.KeyHandler = _ => true;
        canvas.ReleaseKeyboardFocus();

        Assert.Null(canvas.KeyHandler);
        Assert.False(canvas.RequestKeyboardFocus());
        Assert.False(canvas.HasKeyboardFocus);
    }

    [Fact]
    public void TheFakeHostKeepsTheKeyHandlerAndNeverFocuses()
    {
        var host = new FakePluginHost();
        int calls = 0;

        IPluginCanvas canvas = host.Ui.RegisterCanvas(
            new PluginCanvasDescriptor("pad", 8, 8) { AcceptsKeyboardInput = true }, _ => { });
        canvas.KeyHandler = _ => { calls++; return true; };

        Assert.NotNull(canvas.KeyHandler);
        Assert.False(canvas.RequestKeyboardFocus());
        Assert.False(canvas.HasKeyboardFocus);
        Assert.Equal(0, calls);
    }

    /// <summary>A canvas written against the contract before keyboard input: it implements none of it.</summary>
    private sealed class PointerOnlyCanvas : IPluginCanvas
    {
        public string CanvasId => "old";
        public int Width => 8;
        public int Height => 8;
        public bool IsVisible { get; set; }
        public PluginCanvasAnchor Anchor { get; set; }
        public PluginPoint Offset { get; set; }
        public void Invalidate() { }
        public void Dispose() { }
    }

```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/AcDream.Plugin.Tests -c Release 2>&1 | grep -E " error " | head -3`
Expected: errors such as `'PluginCanvasDescriptor' does not contain a definition for 'AcceptsKeyboardInput'` and `'IPluginCanvas' does not contain a definition for 'KeyHandler'`.

- [ ] **Step 3: Add the contract**

In `src/AcDream.Plugin.Abstractions/PluginCanvas.cs`, replace

```csharp
    /// </summary>
    public bool AcceptsPointerInput { get; init; }
}

```

with

```csharp
    /// </summary>
    public bool AcceptsPointerInput { get; init; }

    /// <summary>
    /// Whether the canvas can take keyboard focus. Off, the default, the
    /// canvas never sees a key. On, a left press on the canvas (which needs
    /// <see cref="AcceptsPointerInput"/> and a pointer handler as well) or
    /// <see cref="IPluginCanvas.RequestKeyboardFocus"/> gives it the
    /// keyboard while it is shown and has an
    /// <see cref="IPluginCanvas.KeyHandler"/>; until focus goes back, keys
    /// and typed text go to that handler and not to the game. On a host
    /// without a window the flag is kept and the canvas never takes focus.
    /// </summary>
    public bool AcceptsKeyboardInput { get; init; }
}

```

In `src/AcDream.Plugin.Abstractions/PluginCanvas.cs`, replace

```csharp
}

/// <summary>The modifier keys held while a pointer event happened.</summary>
[Flags]
public enum PluginKeyModifiers
```

with

```csharp
}

/// <summary>The modifier keys held while a pointer or key event happened.</summary>
[Flags]
public enum PluginKeyModifiers
```

In `src/AcDream.Plugin.Abstractions/PluginCanvas.cs`, replace

```csharp
    int WheelDelta = 0);

/// <summary>
/// What a canvas's paint callback draws with. Coordinates are pixels from
```

with

```csharp
    int WheelDelta = 0);

/// <summary>What happened to a canvas that has keyboard focus.</summary>
public enum PluginKeyEventKind
{
    /// <summary>A key went down, or is held and repeating (<see cref="PluginKeyEvent.IsRepeat"/>).</summary>
    Down,

    /// <summary>A key came up.</summary>
    Up,

    /// <summary>The player typed text; <see cref="PluginKeyEvent.Text"/> holds it.</summary>
    Text,

    /// <summary>The canvas took keyboard focus; keys arrive from now on.</summary>
    FocusGained,

    /// <summary>The canvas gave keyboard focus back; no keys arrive until it takes focus again.</summary>
    FocusLost,
}

/// <summary>
/// One keyboard event on a canvas that has keyboard focus.
/// <see cref="Key"/> is meaningful for <see cref="PluginKeyEventKind.Down"/>
/// and <see cref="PluginKeyEventKind.Up"/> and is
/// <see cref="PluginKey.Unknown"/> otherwise; <see cref="Text"/> carries
/// the typed characters for <see cref="PluginKeyEventKind.Text"/> and is
/// null otherwise. Keys <see cref="PluginKey"/> cannot name, the modifier
/// keys among them, are not delivered as <c>Down</c> or <c>Up</c>; the
/// modifiers held travel with every event instead.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Key">The key that went down or came up.</param>
/// <param name="Modifiers">The modifier keys held at the time.</param>
/// <param name="IsRepeat">True for a <c>Down</c> the host repeats while the key stays held.</param>
/// <param name="Text">The characters typed, for <see cref="PluginKeyEventKind.Text"/>; never a control character.</param>
public readonly record struct PluginKeyEvent(
    PluginKeyEventKind Kind,
    PluginKey Key,
    PluginKeyModifiers Modifiers,
    bool IsRepeat = false,
    string? Text = null);

/// <summary>
/// What a canvas's paint callback draws with. Coordinates are pixels from
```

In `src/AcDream.Plugin.Abstractions/PluginCanvas.cs`, replace

```csharp
/// A rectangle the plugin paints, shown over the world and under every
/// window, taking no input unless it opted in through
/// <see cref="PluginCanvasDescriptor.AcceptsPointerInput"/>. Painting is retained: the host keeps what was
/// last painted and calls the paint callback again only after
/// <see cref="Invalidate"/>, at most once per frame, on the tick thread,
```

with

```csharp
/// A rectangle the plugin paints, shown over the world and under every
/// window, taking no input unless it opted in through
/// <see cref="PluginCanvasDescriptor.AcceptsPointerInput"/> or
/// <see cref="PluginCanvasDescriptor.AcceptsKeyboardInput"/>. Painting is retained: the host keeps what was
/// last painted and calls the paint callback again only after
/// <see cref="Invalidate"/>, at most once per frame, on the tick thread,
```

In `src/AcDream.Plugin.Abstractions/PluginCanvas.cs`, replace

```csharp
    {
    }
}

```

with

```csharp
    {
    }

    /// <summary>
    /// Where keyboard events go while the canvas has keyboard focus, on a
    /// canvas registered with
    /// <see cref="PluginCanvasDescriptor.AcceptsKeyboardInput"/>. Null, the
    /// default, and the canvas cannot take focus. The handler answers true
    /// when it handled the event; only the answer to
    /// <see cref="PluginKey.Escape"/> going down changes what the host does:
    /// an Escape the handler did not handle gives focus back.
    ///
    /// <para>The handler is measured like the pointer handler: one that
    /// keeps running over its budget on several events in a row, or throws,
    /// is dropped for the rest of the session and focus goes back; pointer
    /// input and painting continue. Setting the handler on a canvas that did
    /// not opt in, or on a host without a window, keeps the value and
    /// delivers nothing; a host that predates keyboard input answers null
    /// and ignores the set.</para>
    /// </summary>
    Func<PluginKeyEvent, bool>? KeyHandler
    {
        get => null;
        set { }
    }

    /// <summary>
    /// Whether the canvas has keyboard focus right now. Always false on a
    /// host without a window or one that predates keyboard input.
    /// </summary>
    bool HasKeyboardFocus => false;

    /// <summary>
    /// Asks for keyboard focus without a press. Succeeds, and answers true,
    /// only when the canvas opted in, is mounted and shown, has a
    /// <see cref="KeyHandler"/>, nothing else in the interface has keyboard
    /// focus, no modal dialog is open and no key rebind is being captured;
    /// it never takes focus from the chat bar, a text field or a dialog.
    /// Answers true at once when the canvas already has focus, and false on
    /// a host without a window or one that predates keyboard input.
    /// </summary>
    /// <returns>True when the canvas has keyboard focus afterwards.</returns>
    bool RequestKeyboardFocus() => false;

    /// <summary>
    /// Gives keyboard focus back, with a
    /// <see cref="PluginKeyEventKind.FocusLost"/> to the handler first. Safe
    /// to call from inside the handler and at any other time; does nothing
    /// when the canvas does not have focus.
    /// </summary>
    void ReleaseKeyboardFocus()
    {
    }
}

```

In `src/AcDream.Plugin.Abstractions/PluginCanvas.cs`, replace

```csharp
    }

    /// <summary>Marks the canvas disposed; there is nothing to remove.</summary>
    public void Dispose() => IsDisposed = true;
```

with

```csharp
    }

    /// <summary>Kept so the plugin's own logic runs unchanged; never called, because there is no keyboard to focus.</summary>
    public Func<PluginKeyEvent, bool>? KeyHandler { get; set; }

    /// <summary>Always false; there is no keyboard to focus.</summary>
    public bool HasKeyboardFocus => false;

    /// <summary>Always false; there is no keyboard to focus.</summary>
    /// <returns>False.</returns>
    public bool RequestKeyboardFocus() => false;

    /// <summary>Does nothing; the canvas never has focus.</summary>
    public void ReleaseKeyboardFocus()
    {
    }

    /// <summary>Marks the canvas disposed; there is nothing to remove.</summary>
    public void Dispose() => IsDisposed = true;
```

- [ ] **Step 4: Build and run the tests**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test tests/AcDream.Plugin.Tests -c Release --no-build --filter "FullyQualifiedName~PluginCanvasContractTests"
```

Expected: `0 Warning(s)`, `0 Error(s)`; `Passed!  - Failed:     0, Passed:    12`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Plugin.Abstractions/PluginCanvas.cs tests/AcDream.Plugin.Tests/PluginCanvasContractTests.cs
git commit -m "plugin api: canvas keyboard input contract

A canvas can opt in to keyboard input, take keyboard focus from a press
or on request, and receive key, text and focus events through a handler
that says whether it handled each one. Every member has an inert
default, and the headless canvas keeps the handler and never focuses.

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.Plugin.Abstractions/PluginCanvas.cs", "tests/AcDream.Plugin.Tests/PluginCanvasContractTests.cs"], "verifyCommand": "TMPDIR=/tmp/ dotnet test tests/AcDream.Plugin.Tests -c Release --filter \"FullyQualifiedName~PluginCanvasContractTests\"", "acceptanceCriteria": ["AcceptsKeyboardInput defaults false", "PluginKeyEvent defaults: no repeat, no text", "NoOpPluginCanvas keeps the handler and never focuses", "an older canvas answers the defaults", "FakePluginHost canvas keeps the handler and never focuses", "0 warnings"], "modelTier": "standard"}
```

---

### Task 2: One plugin key table

**Goal:** The `PluginKey` ↔ Silk `Key` table lives in a new internal `PluginKeyMap` with `ToSilk` and `FromSilk`, and `AppHotkeyRegistry` uses it instead of its own `MapKey`.

**Files:**
- Create: `src/AcDream.App/Input/PluginKeyMap.cs`
- Modify: `src/AcDream.App/Input/AppHotkeyRegistry.cs` (`Resolve`, and the `MapKey` method removed)
- Test: `tests/AcDream.App.Tests/Input/PluginKeyMapTests.cs` (create)

**Acceptance Criteria:**
- [ ] Every `PluginKey` except `Unknown` maps to a distinct Silk key and back
- [ ] `ToSilk(PluginKey.Unknown)` is null; `FromSilk` of `ShiftLeft`, `ControlRight`, `CapsLock` and `Key.Unknown` is `PluginKey.Unknown`
- [ ] `AppHotkeyRegistry` has no `MapKey` and calls `PluginKeyMap.ToSilk`; the existing `AppHotkeyRegistryTests` (including the numpad and grave mappings) and `HotkeyUiFocusTests` pass unchanged
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`

**Verify:** `TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginKeyMapTests|FullyQualifiedName~AppHotkeyRegistryTests|FullyQualifiedName~HotkeyUiFocusTests"` → `Passed!  - Failed:     0, Passed:    25`

**Steps:**

- [ ] **Step 1: Write the failing tests**

Create `tests/AcDream.App.Tests/Input/PluginKeyMapTests.cs`:

```csharp
using AcDream.App.Input;
using AcDream.Plugin.Abstractions;
using Silk.NET.Input;

namespace AcDream.App.Tests.Input;

// One table serves both directions: hotkeys map a plugin key to the
// window's key, a focused canvas maps the window's key back.
public sealed class PluginKeyMapTests
{
    [Fact]
    public void EveryNamedPluginKeyMapsToADistinctSilkKeyAndBack()
    {
        var seen = new HashSet<Key>();
        foreach (PluginKey key in Enum.GetValues<PluginKey>())
        {
            if (key == PluginKey.Unknown) continue;
            Key? silk = PluginKeyMap.ToSilk(key);
            Assert.True(silk.HasValue, $"{key} has no Silk key");
            Assert.True(seen.Add(silk.Value), $"{silk} is named twice");
            Assert.Equal(key, PluginKeyMap.FromSilk(silk.Value));
        }
    }

    [Fact]
    public void UnknownAndKeysTheContractCannotNameMapToNothing()
    {
        Assert.Null(PluginKeyMap.ToSilk(PluginKey.Unknown));
        Assert.Equal(PluginKey.Unknown, PluginKeyMap.FromSilk(Key.ShiftLeft));
        Assert.Equal(PluginKey.Unknown, PluginKeyMap.FromSilk(Key.ControlRight));
        Assert.Equal(PluginKey.Unknown, PluginKeyMap.FromSilk(Key.CapsLock));
        Assert.Equal(PluginKey.Unknown, PluginKeyMap.FromSilk(Key.Unknown));
    }

    [Fact]
    public void TheWindowsOwnNamesAreTranslated()
    {
        Assert.Equal(PluginKey.Numpad5, PluginKeyMap.FromSilk(Key.Keypad5));
        Assert.Equal(PluginKey.Grave, PluginKeyMap.FromSilk(Key.GraveAccent));
        Assert.Equal(Key.KeypadEnter, PluginKeyMap.ToSilk(PluginKey.NumpadEnter));
    }
}
```

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -E " error " | head -2`
Expected: `The name 'PluginKeyMap' does not exist in the current context`.

- [ ] **Step 2: Move the table**

Create `src/AcDream.App/Input/PluginKeyMap.cs`:

```csharp
using AcDream.Plugin.Abstractions;
using Silk.NET.Input;

namespace AcDream.App.Input;

/// <summary>
/// The one table between the plugin contract's <see cref="PluginKey"/> and
/// the window's Silk <see cref="Key"/>: plugin hotkeys read it one way, a
/// plugin canvas with keyboard focus the other.
/// </summary>
internal static class PluginKeyMap
{
    private static readonly Dictionary<Key, PluginKey> FromSilkTable = BuildFromSilk();

    /// <summary>The Silk key a plugin key names, or null for <see cref="PluginKey.Unknown"/>.</summary>
    internal static Key? ToSilk(PluginKey key) => key switch
    {
        PluginKey.A => Key.A, PluginKey.B => Key.B, PluginKey.C => Key.C,
        PluginKey.D => Key.D, PluginKey.E => Key.E, PluginKey.F => Key.F,
        PluginKey.G => Key.G, PluginKey.H => Key.H, PluginKey.I => Key.I,
        PluginKey.J => Key.J, PluginKey.K => Key.K, PluginKey.L => Key.L,
        PluginKey.M => Key.M, PluginKey.N => Key.N, PluginKey.O => Key.O,
        PluginKey.P => Key.P, PluginKey.Q => Key.Q, PluginKey.R => Key.R,
        PluginKey.S => Key.S, PluginKey.T => Key.T, PluginKey.U => Key.U,
        PluginKey.V => Key.V, PluginKey.W => Key.W, PluginKey.X => Key.X,
        PluginKey.Y => Key.Y, PluginKey.Z => Key.Z,
        PluginKey.Number0 => Key.Number0, PluginKey.Number1 => Key.Number1,
        PluginKey.Number2 => Key.Number2, PluginKey.Number3 => Key.Number3,
        PluginKey.Number4 => Key.Number4, PluginKey.Number5 => Key.Number5,
        PluginKey.Number6 => Key.Number6, PluginKey.Number7 => Key.Number7,
        PluginKey.Number8 => Key.Number8, PluginKey.Number9 => Key.Number9,
        PluginKey.F1 => Key.F1, PluginKey.F2 => Key.F2, PluginKey.F3 => Key.F3,
        PluginKey.F4 => Key.F4, PluginKey.F5 => Key.F5, PluginKey.F6 => Key.F6,
        PluginKey.F7 => Key.F7, PluginKey.F8 => Key.F8, PluginKey.F9 => Key.F9,
        PluginKey.F10 => Key.F10, PluginKey.F11 => Key.F11, PluginKey.F12 => Key.F12,
        PluginKey.Space => Key.Space,
        PluginKey.Enter => Key.Enter,
        PluginKey.Escape => Key.Escape,
        PluginKey.Tab => Key.Tab,
        PluginKey.Backspace => Key.Backspace,
        PluginKey.Delete => Key.Delete,
        PluginKey.Insert => Key.Insert,
        PluginKey.Home => Key.Home,
        PluginKey.End => Key.End,
        PluginKey.PageUp => Key.PageUp,
        PluginKey.PageDown => Key.PageDown,
        PluginKey.Up => Key.Up,
        PluginKey.Down => Key.Down,
        PluginKey.Left => Key.Left,
        PluginKey.Right => Key.Right,
        PluginKey.Minus => Key.Minus,
        PluginKey.Equal => Key.Equal,
        PluginKey.LeftBracket => Key.LeftBracket,
        PluginKey.RightBracket => Key.RightBracket,
        PluginKey.BackSlash => Key.BackSlash,
        PluginKey.Semicolon => Key.Semicolon,
        PluginKey.Apostrophe => Key.Apostrophe,
        PluginKey.Comma => Key.Comma,
        PluginKey.Period => Key.Period,
        PluginKey.Slash => Key.Slash,
        PluginKey.Numpad0 => Key.Keypad0, PluginKey.Numpad1 => Key.Keypad1,
        PluginKey.Numpad2 => Key.Keypad2, PluginKey.Numpad3 => Key.Keypad3,
        PluginKey.Numpad4 => Key.Keypad4, PluginKey.Numpad5 => Key.Keypad5,
        PluginKey.Numpad6 => Key.Keypad6, PluginKey.Numpad7 => Key.Keypad7,
        PluginKey.Numpad8 => Key.Keypad8, PluginKey.Numpad9 => Key.Keypad9,
        PluginKey.NumpadDecimal => Key.KeypadDecimal,
        PluginKey.NumpadDivide => Key.KeypadDivide,
        PluginKey.NumpadMultiply => Key.KeypadMultiply,
        PluginKey.NumpadSubtract => Key.KeypadSubtract,
        PluginKey.NumpadAdd => Key.KeypadAdd,
        PluginKey.NumpadEnter => Key.KeypadEnter,
        PluginKey.Grave => Key.GraveAccent,
        PluginKey.PrintScreen => Key.PrintScreen,
        PluginKey.Pause => Key.Pause,
        _ => null,
    };

    /// <summary>The plugin key for a Silk key, or <see cref="PluginKey.Unknown"/> when the contract cannot name it.</summary>
    internal static PluginKey FromSilk(Key key) =>
        FromSilkTable.TryGetValue(key, out PluginKey named) ? named : PluginKey.Unknown;

    private static Dictionary<Key, PluginKey> BuildFromSilk()
    {
        var table = new Dictionary<Key, PluginKey>();
        foreach (PluginKey key in Enum.GetValues<PluginKey>())
        {
            if (ToSilk(key) is { } silk)
                table.Add(silk, key);
        }
        return table;
    }
}
```

The `ToSilk` switch is `AppHotkeyRegistry.MapKey` moved verbatim. Then:

In `src/AcDream.App/Input/AppHotkeyRegistry.cs`, replace

```csharp
        }

        Key? silkKey = MapKey(effective.Key);
        bool bound;
        lock (_gate)
```

with

```csharp
        }

        Key? silkKey = PluginKeyMap.ToSilk(effective.Key);
        bool bound;
        lock (_gate)
```

In `src/AcDream.App/Input/AppHotkeyRegistry.cs`, replace

```csharp
    private readonly record struct StoredChord(string Key, bool Ctrl, bool Alt, bool Shift);

    private static Key? MapKey(PluginKey key) => key switch
    {
        PluginKey.A => Key.A, PluginKey.B => Key.B, PluginKey.C => Key.C,
        PluginKey.D => Key.D, PluginKey.E => Key.E, PluginKey.F => Key.F,
        PluginKey.G => Key.G, PluginKey.H => Key.H, PluginKey.I => Key.I,
        PluginKey.J => Key.J, PluginKey.K => Key.K, PluginKey.L => Key.L,
        PluginKey.M => Key.M, PluginKey.N => Key.N, PluginKey.O => Key.O,
        PluginKey.P => Key.P, PluginKey.Q => Key.Q, PluginKey.R => Key.R,
        PluginKey.S => Key.S, PluginKey.T => Key.T, PluginKey.U => Key.U,
        PluginKey.V => Key.V, PluginKey.W => Key.W, PluginKey.X => Key.X,
        PluginKey.Y => Key.Y, PluginKey.Z => Key.Z,
        PluginKey.Number0 => Key.Number0, PluginKey.Number1 => Key.Number1,
        PluginKey.Number2 => Key.Number2, PluginKey.Number3 => Key.Number3,
        PluginKey.Number4 => Key.Number4, PluginKey.Number5 => Key.Number5,
        PluginKey.Number6 => Key.Number6, PluginKey.Number7 => Key.Number7,
        PluginKey.Number8 => Key.Number8, PluginKey.Number9 => Key.Number9,
        PluginKey.F1 => Key.F1, PluginKey.F2 => Key.F2, PluginKey.F3 => Key.F3,
        PluginKey.F4 => Key.F4, PluginKey.F5 => Key.F5, PluginKey.F6 => Key.F6,
        PluginKey.F7 => Key.F7, PluginKey.F8 => Key.F8, PluginKey.F9 => Key.F9,
        PluginKey.F10 => Key.F10, PluginKey.F11 => Key.F11, PluginKey.F12 => Key.F12,
        PluginKey.Space => Key.Space,
        PluginKey.Enter => Key.Enter,
        PluginKey.Escape => Key.Escape,
        PluginKey.Tab => Key.Tab,
        PluginKey.Backspace => Key.Backspace,
        PluginKey.Delete => Key.Delete,
        PluginKey.Insert => Key.Insert,
        PluginKey.Home => Key.Home,
        PluginKey.End => Key.End,
        PluginKey.PageUp => Key.PageUp,
        PluginKey.PageDown => Key.PageDown,
        PluginKey.Up => Key.Up,
        PluginKey.Down => Key.Down,
        PluginKey.Left => Key.Left,
        PluginKey.Right => Key.Right,
        PluginKey.Minus => Key.Minus,
        PluginKey.Equal => Key.Equal,
        PluginKey.LeftBracket => Key.LeftBracket,
        PluginKey.RightBracket => Key.RightBracket,
        PluginKey.BackSlash => Key.BackSlash,
        PluginKey.Semicolon => Key.Semicolon,
        PluginKey.Apostrophe => Key.Apostrophe,
        PluginKey.Comma => Key.Comma,
        PluginKey.Period => Key.Period,
        PluginKey.Slash => Key.Slash,
        PluginKey.Numpad0 => Key.Keypad0, PluginKey.Numpad1 => Key.Keypad1,
        PluginKey.Numpad2 => Key.Keypad2, PluginKey.Numpad3 => Key.Keypad3,
        PluginKey.Numpad4 => Key.Keypad4, PluginKey.Numpad5 => Key.Keypad5,
        PluginKey.Numpad6 => Key.Keypad6, PluginKey.Numpad7 => Key.Keypad7,
        PluginKey.Numpad8 => Key.Keypad8, PluginKey.Numpad9 => Key.Keypad9,
        PluginKey.NumpadDecimal => Key.KeypadDecimal,
        PluginKey.NumpadDivide => Key.KeypadDivide,
        PluginKey.NumpadMultiply => Key.KeypadMultiply,
        PluginKey.NumpadSubtract => Key.KeypadSubtract,
        PluginKey.NumpadAdd => Key.KeypadAdd,
        PluginKey.NumpadEnter => Key.KeypadEnter,
        PluginKey.Grave => Key.GraveAccent,
        PluginKey.PrintScreen => Key.PrintScreen,
        PluginKey.Pause => Key.Pause,
        _ => null,
    };

    private sealed class Entry(
        string id,
```

with

```csharp
    private readonly record struct StoredChord(string Key, bool Ctrl, bool Alt, bool Shift);

    private sealed class Entry(
        string id,
```

- [ ] **Step 3: Build and run the tests**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~PluginKeyMapTests|FullyQualifiedName~AppHotkeyRegistryTests|FullyQualifiedName~HotkeyUiFocusTests"
```

Expected: `0 Warning(s)`, `0 Error(s)`; `Passed!  - Failed:     0, Passed:    25`.

- [ ] **Step 4: Commit**

```bash
git add src/AcDream.App/Input/PluginKeyMap.cs src/AcDream.App/Input/AppHotkeyRegistry.cs tests/AcDream.App.Tests/Input/PluginKeyMapTests.cs
git commit -m "input: one plugin key table for hotkeys and canvases

The table between the plugin contract's keys and the window's moves out
of the hotkey registry so a focused canvas can read it the other way.

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Input/PluginKeyMap.cs", "src/AcDream.App/Input/AppHotkeyRegistry.cs", "tests/AcDream.App.Tests/Input/PluginKeyMapTests.cs"], "verifyCommand": "TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~PluginKeyMapTests|FullyQualifiedName~AppHotkeyRegistryTests|FullyQualifiedName~HotkeyUiFocusTests\"", "acceptanceCriteria": ["every named PluginKey round-trips through a distinct Silk key", "Unknown and modifier keys map to nothing", "AppHotkeyRegistry uses PluginKeyMap.ToSilk and has no MapKey", "existing hotkey suites pass unchanged", "0 warnings"], "modelTier": "mechanical"}
```

---

### Task 3: The plugin's handle reaches the mounted canvas

**Goal:** `PluginCanvasRegistration` keeps the key handler (dropped on dispose) and answers the focus calls through an `IPluginCanvasKeyboardFocus` the mounted element will set, and `ScopedPluginHost.IndividualCanvas` forwards all four members.

**Files:**
- Create: `src/AcDream.App/Plugins/IPluginCanvasKeyboardFocus.cs`
- Modify: `src/AcDream.App/Plugins/PluginCanvasRegistration.cs`
- Modify: `src/AcDream.Core/Plugins/ScopedPluginHost.cs` (`IndividualCanvas`)
- Test: `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryCanvasTests.cs`

**Acceptance Criteria:**
- [ ] `IPluginCanvasKeyboardFocus` is internal with `bool HasFocus { get; }`, `bool RequestFocus()`, `void ReleaseFocus()`
- [ ] `PluginCanvasRegistration.KeyHandler` is stored in a volatile field; `HasKeyboardFocus`, `RequestKeyboardFocus()` and `ReleaseKeyboardFocus()` go through `KeyboardFocus` and answer false / do nothing while it is null
- [ ] `Unmount(forget: true)` drops the key handler after the teardown has run
- [ ] `IndividualCanvas` forwards `KeyHandler` both ways, `HasKeyboardFocus`, `RequestKeyboardFocus()` and `ReleaseKeyboardFocus()`; the new `ScopedUiRegistryCanvasTests` case proves it
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`

**Verify:** `TMPDIR=/tmp/ dotnet test tests/AcDream.Core.Tests -c Release --filter "FullyQualifiedName~ScopedUiRegistryCanvasTests"` → `Passed!  - Failed:     0, Passed:     6`

**Steps:**

- [ ] **Step 1: Write the failing forwarder test**

In `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryCanvasTests.cs`, replace

```csharp
        public int Disposals { get; private set; }
        public int PointerReleases { get; private set; }
        public Action<PluginPointerEvent>? PointerHandler { get; set; }

        public void Invalidate() => Invalidations++;

        public void ReleasePointer() => PointerReleases++;

        public void Dispose() => Disposals++;
```

with

```csharp
        public int Disposals { get; private set; }
        public int PointerReleases { get; private set; }
        public int FocusRequests { get; private set; }
        public int FocusReleases { get; private set; }
        public Action<PluginPointerEvent>? PointerHandler { get; set; }
        public Func<PluginKeyEvent, bool>? KeyHandler { get; set; }
        public bool HasKeyboardFocus { get; set; }

        public void Invalidate() => Invalidations++;

        public void ReleasePointer() => PointerReleases++;

        public bool RequestKeyboardFocus()
        {
            FocusRequests++;
            return HasKeyboardFocus = true;
        }

        public void ReleaseKeyboardFocus()
        {
            FocusReleases++;
            HasKeyboardFocus = false;
        }

        public void Dispose() => Disposals++;
```

In `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryCanvasTests.cs`, replace

```csharp

    [Fact]
    public void DisposingThePluginDisposesEveryCanvasItStillHolds()
    {
```

with

```csharp

    [Fact]
    public void EveryKeyboardCallOnThePluginsHandleReachesTheHostsCanvas()
    {
        var inner = new FakeScopedUiRegistry();
        var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");
        IPluginCanvas canvas = scoped.Ui.RegisterCanvas(Hud, _ => { });
        FakeCanvas hosts = Assert.Single(inner.Canvases);
        Func<PluginKeyEvent, bool> handler = _ => true;

        canvas.KeyHandler = handler;
        Assert.False(canvas.HasKeyboardFocus);
        Assert.True(canvas.RequestKeyboardFocus());
        Assert.True(canvas.HasKeyboardFocus);
        canvas.ReleaseKeyboardFocus();

        Assert.Same(handler, hosts.KeyHandler);
        Assert.Same(handler, canvas.KeyHandler);
        Assert.Equal(1, hosts.FocusRequests);
        Assert.Equal(1, hosts.FocusReleases);
        Assert.False(canvas.HasKeyboardFocus);
        scoped.Dispose();
    }

    [Fact]
    public void DisposingThePluginDisposesEveryCanvasItStillHolds()
    {
```

Run:

```bash
dotnet build tests/AcDream.Core.Tests -c Release 2>&1 | tail -2
TMPDIR=/tmp/ dotnet test tests/AcDream.Core.Tests -c Release --no-build --filter "FullyQualifiedName~EveryKeyboardCallOnThePluginsHandleReachesTheHostsCanvas"
```

Expected: builds; the test fails at `Assert.True(canvas.RequestKeyboardFocus())`, because `IndividualCanvas` answers with the interface defaults.

- [ ] **Step 2: Forward the members**

In `src/AcDream.Core/Plugins/ScopedPluginHost.cs`, replace

```csharp
            public void ReleasePointer() => inner.ReleasePointer();

            public void Dispose()
            {
```

with

```csharp
            public void ReleasePointer() => inner.ReleasePointer();

            public Func<PluginKeyEvent, bool>? KeyHandler
            {
                get => inner.KeyHandler;
                set => inner.KeyHandler = value;
            }

            public bool HasKeyboardFocus => inner.HasKeyboardFocus;

            public bool RequestKeyboardFocus() => inner.RequestKeyboardFocus();

            public void ReleaseKeyboardFocus() => inner.ReleaseKeyboardFocus();

            public void Dispose()
            {
```

- [ ] **Step 3: The registration's side**

Create `src/AcDream.App/Plugins/IPluginCanvasKeyboardFocus.cs`:

```csharp
namespace AcDream.App.Plugins;

/// <summary>
/// How a mounted canvas element answers its registration's keyboard focus
/// calls. The element sets itself on the registration when it mounts and
/// clears itself when it comes down, so a canvas that is not mounted
/// answers as a host without a window does.
/// </summary>
internal interface IPluginCanvasKeyboardFocus
{
    /// <summary>Whether the element has the interface's keyboard focus.</summary>
    bool HasFocus { get; }

    /// <summary>Takes keyboard focus if every condition allows it; true when the element has it afterwards.</summary>
    bool RequestFocus();

    /// <summary>Gives keyboard focus back, if the element still has it.</summary>
    void ReleaseFocus();
}
```

In `src/AcDream.App/Plugins/PluginCanvasRegistration.cs`, replace

```csharp
/// frame.
///
/// <para>The paint delegate and the pointer handler are dropped on
/// dispose. They are the references from the client into the plugin's
/// code that the interface holds, and a plugin assembly cannot unload
/// while anything still points into it.</para>
```

with

```csharp
/// frame.
///
/// <para>The paint delegate and the pointer and key handlers are dropped
/// on dispose. They are the references from the client into the plugin's
/// code that the interface holds, and a plugin assembly cannot unload
/// while anything still points into it.</para>
```

In `src/AcDream.App/Plugins/PluginCanvasRegistration.cs`, replace

```csharp
    private Action<IPluginPainter>? _paint;
    private volatile Action<PluginPointerEvent>? _pointerHandler;
    private volatile bool _invalidated = true;
    private Action? _teardown;
```

with

```csharp
    private Action<IPluginPainter>? _paint;
    private volatile Action<PluginPointerEvent>? _pointerHandler;
    private volatile Func<PluginKeyEvent, bool>? _keyHandler;
    private volatile IPluginCanvasKeyboardFocus? _keyboardFocus;
    private volatile bool _invalidated = true;
    private Action? _teardown;
```

In `src/AcDream.App/Plugins/PluginCanvasRegistration.cs`, replace

```csharp
    internal bool AcceptsPointerInput => Descriptor.AcceptsPointerInput;

    /// <summary>
    /// Records how the element ends a press the canvas is holding, so the
```

with

```csharp
    internal bool AcceptsPointerInput => Descriptor.AcceptsPointerInput;

    /// <summary>Whether the plugin asked for keyboard input when it registered the canvas.</summary>
    internal bool AcceptsKeyboardInput => Descriptor.AcceptsKeyboardInput;

    /// <summary>
    /// The mounted element's answer to the keyboard focus calls; set by
    /// the element when it mounts, cleared by it when it comes down.
    /// </summary>
    internal IPluginCanvasKeyboardFocus? KeyboardFocus
    {
        get => _keyboardFocus;
        set => _keyboardFocus = value;
    }

    /// <summary>
    /// Records how the element ends a press the canvas is holding, so the
```

In `src/AcDream.App/Plugins/PluginCanvasRegistration.cs`, replace

```csharp
    public void ReleasePointer() => _releasePointer?.Invoke();

    public int Width => Descriptor.Width;

```

with

```csharp
    public void ReleasePointer() => _releasePointer?.Invoke();

    public Func<PluginKeyEvent, bool>? KeyHandler
    {
        get => _keyHandler;
        set => _keyHandler = value;
    }

    public bool HasKeyboardFocus => _keyboardFocus?.HasFocus ?? false;

    public bool RequestKeyboardFocus() => _keyboardFocus?.RequestFocus() ?? false;

    public void ReleaseKeyboardFocus() => _keyboardFocus?.ReleaseFocus();

    public int Width => Descriptor.Width;

```

In `src/AcDream.App/Plugins/PluginCanvasRegistration.cs`, replace

```csharp
    /// <summary>
    /// Runs the teardown once and forgets it. The paint delegate and the
    /// pointer handler stay unless <paramref name="forget"/> is set: an
    /// interface that is going away hands the canvas back to the registry
    /// to be mounted again by the next one, and the plugin's callbacks must
```

with

```csharp
    /// <summary>
    /// Runs the teardown once and forgets it. The paint delegate and the
    /// pointer and key handlers stay unless <paramref name="forget"/> is set: an
    /// interface that is going away hands the canvas back to the registry
    /// to be mounted again by the next one, and the plugin's callbacks must
```

In `src/AcDream.App/Plugins/PluginCanvasRegistration.cs`, replace

```csharp
    {
        Action? teardown = Interlocked.Exchange(ref _teardown, null);
        // The element comes down first: a press it still holds is cancelled
        // to the plugin's handler on the way, which needs the handler.
        teardown?.Invoke();
        if (forget)
```

with

```csharp
    {
        Action? teardown = Interlocked.Exchange(ref _teardown, null);
        // The element comes down first: a press it still holds is cancelled,
        // and keyboard focus it still has is lost, to the plugin's handlers
        // on the way, which needs the handlers.
        teardown?.Invoke();
        if (forget)
```

In `src/AcDream.App/Plugins/PluginCanvasRegistration.cs`, replace

```csharp
            _paint = null;
            _pointerHandler = null;
        }
    }
```

with

```csharp
            _paint = null;
            _pointerHandler = null;
            _keyHandler = null;
        }
    }
```

- [ ] **Step 4: Build and run the tests**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test tests/AcDream.Core.Tests -c Release --no-build --filter "FullyQualifiedName~ScopedUiRegistryCanvasTests"
```

Expected: `0 Warning(s)`, `0 Error(s)`; `Passed!  - Failed:     0, Passed:     6`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/Plugins/IPluginCanvasKeyboardFocus.cs src/AcDream.App/Plugins/PluginCanvasRegistration.cs src/AcDream.Core/Plugins/ScopedPluginHost.cs tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryCanvasTests.cs
git commit -m "plugin canvas: keyboard calls reach the mounted canvas

The registration keeps the key handler, dropping it with the paint
callback on dispose, and answers the focus calls through a hook the
mounted element sets. The scoped forwarder passes all four members on.

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Plugins/IPluginCanvasKeyboardFocus.cs", "src/AcDream.App/Plugins/PluginCanvasRegistration.cs", "src/AcDream.Core/Plugins/ScopedPluginHost.cs", "tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryCanvasTests.cs"], "verifyCommand": "TMPDIR=/tmp/ dotnet test tests/AcDream.Core.Tests -c Release --filter \"FullyQualifiedName~ScopedUiRegistryCanvasTests\"", "acceptanceCriteria": ["IPluginCanvasKeyboardFocus internal with HasFocus/RequestFocus/ReleaseFocus", "registration stores the handler and answers focus calls through KeyboardFocus", "Unmount(forget: true) drops the key handler after teardown", "IndividualCanvas forwards all four members", "0 warnings"], "modelTier": "standard"}
```

---

### Task 4: A canvas takes and gives back keyboard focus

**Goal:** A mounted canvas that opted in and has a key handler takes the root's keyboard focus from a left press or `RequestKeyboardFocus()` (never from anything else that has focus, never under a modal or a rebind capture), and gives it back, with exactly one `FocusLost` to the plugin, on every path in the spec except the Escape policy, which Task 5 adds with the keys.

**Files:**
- Modify: `src/AcDream.App/UI/UiElement.cs:145` (`AcceptsFocus` becomes virtual)
- Modify: `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs:4203-4232` (`MountPluginCanvases`, `HeldPointerModifiers` doc)
- Test: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasKeyboardTests.cs` (create)

**Acceptance Criteria:**
- [ ] `UiElement.AcceptsFocus` is `public virtual bool AcceptsFocus { get; set; }`; `PluginCanvasElement` overrides it with `TakesKeyboard` (opted in, key guard not tripped, handler set, `VisibleSource` true) and ignores sets
- [ ] An opted-in canvas sets `IsEditControl = true` and `registration.KeyboardFocus = this`; `ReleaseTargets` clears the hook if it is still this element
- [ ] `PluginCanvasElement` takes an optional `Func<bool>? keyboardCaptured` after `modifiers`; `RetailUiRuntime` passes `() => _bindings.Keyboard?.Dispatcher?.IsCapturing == true`
- [ ] A left press gives focus and one `FocusGained`; a right press does not; a canvas that did not opt in, or has no key handler, never takes focus; a keyboard-only canvas takes it on request, once
- [ ] `RequestKeyboardFocus()` answers false, and changes nothing, while a text field has focus, a modal is open, a rebind capture runs, the canvas is hidden, has no handler, or was disposed
- [ ] Exactly one `FocusLost` on: a left press elsewhere; another canvas taking focus; hiding; disposing (before the handler is dropped); `UnbindCanvasHost`; a modal opening (next tick); `ReleaseKeyboardFocus()` (a second call does nothing); a dropped paint callback. Taking the handler away gives focus back on the next tick with nothing delivered
- [ ] A handler that throws on `FocusGained` is dropped, `RequestKeyboardFocus()` answers false and nothing has focus
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`

**Verify:** `TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginCanvasKeyboardTests|FullyQualifiedName~PluginCanvasPointerTests|FullyQualifiedName~PluginCanvasElementTests"` → `Passed!  - Failed:     0, Passed:    56`

**Steps:**

- [ ] **Step 1: Write the failing focus tests**

The harness follows `PluginCanvasPointerTests`' (root, overlay layer, registry, a manual clock for the guards), adds a GPU frame for the paint-drop case, and fakes the modifier and rebind-capture sources. `Recorder` stands in for a plugin's key handler.

Create `tests/AcDream.App.Tests/UI/Layout/PluginCanvasKeyboardTests.cs`:

```csharp
using System.Collections.Generic;
using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// Keyboard input on a plugin canvas: a canvas that opted in and has a
/// key handler takes the interface's keyboard focus from a left press or
/// on request, never from anything else that has it; while focused it
/// gets keys, typed text and its own repeat, and the game gets none; and
/// focus goes back, with exactly one focus lost, on every path that ends
/// it. A canvas that did not opt in is untouched by all of it.
/// </summary>
public sealed class PluginCanvasKeyboardTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    /// <summary>A clock the test advances by hand: each guarded call costs what the test says.</summary>
    private sealed class ManualClock
    {
        public double Milliseconds { get; private set; }
        public double NextCallCosts { get; set; }
        private bool _atCallStart = true;

        public double Read()
        {
            if (_atCallStart)
            {
                _atCallStart = false;
                return Milliseconds;
            }
            _atCallStart = true;
            Milliseconds += NextCallCosts;
            return Milliseconds;
        }
    }

    private sealed class Harness
    {
        public RecordingGpuDevice Device { get; } = new();
        public FrameSource Frames { get; } = new();
        public BufferedUiRegistry Registry { get; } = new();
        public UiRoot Root { get; } = new() { Width = 800f, Height = 600f };
        public UiOverlayLayer Layer { get; }
        public PluginCanvasSurface Surface { get; }
        public TextRenderer MainRenderer { get; }
        public UiRenderContext MainContext { get; }
        public List<string> Reports { get; } = [];
        public ManualClock Clock { get; } = new();
        public PluginKeyModifiers Modifiers { get; set; }
        public bool KeyboardCaptured { get; set; }
        public PluginUiOwner Owner { get; } = new("example.plugin", "Example");
        public List<(UiMouseButton Button, int X, int Y)> WorldPresses { get; } = [];
        private long _now;

        public Harness()
        {
            UiOverlayHost host = UiOverlayHost.Mount(Root);
            Layer = host.AddLayer("PluginCanvases");
            Layer.Visible = true;
            var services = new PluginCanvasHostServices(Device, Frames, "unused", null);
            Surface = new PluginCanvasSurface(services, font: null);
            MainRenderer = new TextRenderer(Device, Frames, "unused");
            MainContext = new UiRenderContext(MainRenderer, new Vector2(800f, 600f));
            Root.WorldMouseFallThrough += (button, x, y, _) => WorldPresses.Add((button, x, y));
        }

        public (PluginCanvasRegistration Registration, PluginCanvasElement Element) Mount(
            PluginCanvasDescriptor descriptor,
            Func<PluginKeyEvent, bool>? keys,
            Action<PluginPointerEvent>? pointer = null,
            Action<IPluginPainter>? paint = null)
        {
            var registration = (PluginCanvasRegistration)Registry.RegisterCanvas(Owner, descriptor, paint ?? (_ => { }));
            IPluginCanvas canvas = registration;
            canvas.KeyHandler = keys;
            canvas.PointerHandler = pointer;
            Assert.Single(Registry.DrainCanvases());
            var element = new PluginCanvasElement(
                registration, Surface, () => Registry.FindImages(Owner), Reports.Add, Clock.Read,
                () => Modifiers, keyboardCaptured: () => KeyboardCaptured);
            Layer.AddChild(element, takesInput: descriptor.AcceptsPointerInput);
            Registry.CompleteCanvasMount(registration, () =>
            {
                Layer.RemoveChild(element);
                element.ReleaseTargets();
            });
            Tick();
            return (registration, element);
        }

        public void Tick(double seconds = 0.016)
        {
            _now += (long)(seconds * 1000.0);
            Root.Tick(seconds, _now);
        }

        /// <summary>One interface frame with a GPU frame open, so an invalidated canvas paints.</summary>
        public void Frame()
        {
            using IGpuFrame frame = Device.BeginFrame();
            Frames.CurrentFrame = frame;
            try
            {
                Tick();
                MainRenderer.Begin(new Vector2(Root.Width, Root.Height));
                MainContext.Begin(new Vector2(Root.Width, Root.Height), null);
                Root.Draw(MainContext);
                MainRenderer.Flush(null);
            }
            finally
            {
                Frames.CurrentFrame = null;
            }
        }

        public void Press(int x, int y)
        {
            Root.OnMouseDown(UiMouseButton.Left, x, y);
            Root.OnMouseUp(UiMouseButton.Left, x, y);
        }

        public void Type(Key key)
        {
            Root.OnKeyDown((int)key);
            Root.OnKeyUp((int)key);
        }
    }

    /// <summary>A plugin's key handler that records every event and answers as told.</summary>
    private sealed class Recorder
    {
        public List<PluginKeyEvent> Events { get; } = [];
        public bool HandlesEscape { get; set; }

        public bool Handle(PluginKeyEvent e)
        {
            Events.Add(e);
            return e.Key != PluginKey.Escape || HandlesEscape;
        }

        public List<PluginKeyEventKind> Kinds => Events.ConvertAll(e => e.Kind);

        public int Count(PluginKeyEventKind kind) => Events.FindAll(e => e.Kind == kind).Count;
    }

    // At (10, 20), 200 x 100: a press at (100, 60) lands on it.
    private static PluginCanvasDescriptor Pad(bool pointer = true, bool keyboard = true) =>
        new("pad", 200, 100)
        {
            Offset = new PluginPoint(10, 20),
            AcceptsPointerInput = pointer,
            AcceptsKeyboardInput = keyboard,
        };

    private static (Harness Harness, PluginCanvasRegistration Registration, PluginCanvasElement Element, Recorder Keys)
        MountedPad(bool pointer = true, bool keyboard = true)
    {
        var harness = new Harness();
        var keys = new Recorder();
        (PluginCanvasRegistration registration, PluginCanvasElement element) =
            harness.Mount(Pad(pointer, keyboard), keys.Handle, pointer: _ => { });
        return (harness, registration, element, keys);
    }

    private static (Harness Harness, PluginCanvasRegistration Registration, PluginCanvasElement Element, Recorder Keys)
        FocusedPad()
    {
        var mounted = MountedPad();
        mounted.Harness.Press(100, 60);
        Assert.True(mounted.Registration.HasKeyboardFocus);
        mounted.Keys.Events.Clear();
        return mounted;
    }

    [Fact]
    public void ALeftPressGivesTheCanvasFocus()
    {
        (Harness harness, PluginCanvasRegistration registration, PluginCanvasElement element, Recorder keys) = MountedPad();

        harness.Press(100, 60);

        Assert.Same(element, harness.Root.KeyboardFocus);
        Assert.True(registration.HasKeyboardFocus);
        Assert.Equal(
            [new PluginKeyEvent(PluginKeyEventKind.FocusGained, PluginKey.Unknown, PluginKeyModifiers.None)],
            keys.Events);
    }

    [Fact]
    public void ARightPressDoesNotGiveFocus()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = MountedPad();

        harness.Root.OnMouseDown(UiMouseButton.Right, 100, 60);
        harness.Root.OnMouseUp(UiMouseButton.Right, 100, 60);

        Assert.False(registration.HasKeyboardFocus);
        Assert.Empty(keys.Events);
    }

    [Fact]
    public void ACanvasThatDidNotOptInNeverTakesFocusAndSeesNoKeys()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = MountedPad(keyboard: false);

        harness.Press(100, 60);
        harness.Type(Key.A);

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.False(registration.RequestKeyboardFocus());
        Assert.False(registration.HasKeyboardFocus);
        Assert.Empty(keys.Events);
    }

    [Fact]
    public void WithoutAKeyHandlerAPressDoesNotGiveFocusButStillReachesThePointerHandler()
    {
        var harness = new Harness();
        var pointer = new List<PluginPointerEvent>();
        (PluginCanvasRegistration registration, _) = harness.Mount(Pad(), keys: null, pointer: pointer.Add);

        harness.Press(100, 60);

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.False(registration.HasKeyboardFocus);
        Assert.Equal([PluginPointerEventKind.Down, PluginPointerEventKind.Up], pointer.ConvertAll(e => e.Kind));
    }

    [Fact]
    public void AKeyboardOnlyCanvasTakesFocusOnRequestOnce()
    {
        (Harness harness, PluginCanvasRegistration registration, PluginCanvasElement element, Recorder keys) = MountedPad(pointer: false);

        Assert.True(registration.RequestKeyboardFocus());
        Assert.True(registration.RequestKeyboardFocus());

        Assert.Same(element, harness.Root.KeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusGained], keys.Kinds);
        // Click-through for the pointer all the same.
        harness.Press(100, 60);
        Assert.Equal(2, harness.WorldPresses.Count);
    }

    [Fact]
    public void ARequestNeverTakesFocusFromATextField()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = MountedPad();
        var field = new UiField { Width = 100, Height = 20 };
        harness.Root.AddChild(field);
        harness.Root.SetKeyboardFocus(field);

        Assert.False(registration.RequestKeyboardFocus());

        Assert.Same(field, harness.Root.KeyboardFocus);
        Assert.Empty(keys.Events);
    }

    [Fact]
    public void ARequestIsRefusedWhileAModalIsOpen()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = MountedPad();
        var dialog = new UiPanel { Width = 300, Height = 150 };
        harness.Root.AddChild(dialog);
        harness.Root.Modal = dialog;

        Assert.False(registration.RequestKeyboardFocus());

        harness.Root.Modal = null;
        Assert.True(registration.RequestKeyboardFocus());
        Assert.Equal(1, keys.Count(PluginKeyEventKind.FocusGained));
    }

    [Fact]
    public void ARequestIsRefusedWhileAKeyRebindIsBeingCaptured()
    {
        (Harness harness, PluginCanvasRegistration registration, _, _) = MountedPad();
        harness.KeyboardCaptured = true;

        Assert.False(registration.RequestKeyboardFocus());

        harness.KeyboardCaptured = false;
        Assert.True(registration.RequestKeyboardFocus());
    }

    [Fact]
    public void ARequestIsRefusedWhileTheCanvasIsHiddenOrHasNoHandler()
    {
        (Harness harness, PluginCanvasRegistration registration, _, _) = MountedPad();
        IPluginCanvas canvas = registration;

        canvas.IsVisible = false;
        harness.Tick();
        Assert.False(canvas.RequestKeyboardFocus());

        canvas.IsVisible = true;
        harness.Tick();
        Func<PluginKeyEvent, bool>? handler = canvas.KeyHandler;
        canvas.KeyHandler = null;
        Assert.False(canvas.RequestKeyboardFocus());

        canvas.KeyHandler = handler;
        Assert.True(canvas.RequestKeyboardFocus());
    }

    [Fact]
    public void ARequestIsRefusedOnceTheCanvasIsDisposed()
    {
        (_, PluginCanvasRegistration registration, _, Recorder keys) = MountedPad();

        registration.Dispose();

        Assert.False(registration.RequestKeyboardFocus());
        Assert.False(registration.HasKeyboardFocus);
        Assert.Empty(keys.Events);
    }

    [Fact]
    public void APressElsewhereGivesFocusBack()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        harness.Press(400, 300);

        Assert.False(registration.HasKeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void FocusMovesBetweenTwoCanvasesWithOneLostAndOneGained()
    {
        (Harness harness, PluginCanvasRegistration first, _, Recorder firstKeys) = FocusedPad();
        var secondKeys = new Recorder();
        (PluginCanvasRegistration second, _) = harness.Mount(
            new PluginCanvasDescriptor("other", 100, 100)
            {
                Offset = new PluginPoint(400, 300),
                AcceptsPointerInput = true,
                AcceptsKeyboardInput = true,
            },
            secondKeys.Handle,
            pointer: _ => { });

        harness.Press(450, 350);

        Assert.False(first.HasKeyboardFocus);
        Assert.True(second.HasKeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusLost], firstKeys.Kinds);
        Assert.Equal([PluginKeyEventKind.FocusGained], secondKeys.Kinds);
    }

    [Fact]
    public void HidingTheCanvasGivesFocusBack()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        registration.IsVisible = false;
        harness.Tick();
        harness.Type(Key.A);

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void DisposingTheCanvasGivesFocusBackBeforeTheHandlerIsDropped()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        registration.Dispose();
        harness.Type(Key.A);

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.Null(registration.KeyHandler);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void TearingTheInterfaceDownGivesFocusBackAndKeepsTheHandler()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        harness.Registry.UnbindCanvasHost();

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.False(registration.HasKeyboardFocus);
        Assert.NotNull(registration.KeyHandler);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void TakingTheHandlerAwayGivesFocusBackOnTheNextTick()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        registration.KeyHandler = null;
        harness.Tick();

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.False(registration.HasKeyboardFocus);
        Assert.Empty(keys.Events);
    }

    [Fact]
    public void AModalOpeningGivesFocusBack()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();
        var dialog = new UiPanel { Width = 300, Height = 150 };
        harness.Root.AddChild(dialog);

        harness.Root.Modal = dialog;
        harness.Tick();

        Assert.False(registration.HasKeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void ReleasingFocusDeliversOneFocusLostAndASecondReleaseDoesNothing()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        registration.ReleaseKeyboardFocus();
        registration.ReleaseKeyboardFocus();

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void ADroppedPaintCallbackGivesFocusBack()
    {
        var harness = new Harness();
        var keys = new Recorder();
        bool fail = false;
        (PluginCanvasRegistration registration, _) = harness.Mount(
            Pad(), keys.Handle, pointer: _ => { },
            paint: _ =>
            {
                if (fail) throw new InvalidOperationException("paint boom");
            });
        harness.Frame();
        harness.Press(100, 60);
        keys.Events.Clear();

        fail = true;
        registration.Invalidate();
        harness.Frame();

        Assert.True(registration.IsDropped);
        Assert.Null(harness.Root.KeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void AHandlerThatThrowsOnFocusGainedIsDroppedAndFocusEndsNowhere()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, PluginCanvasElement element) = harness.Mount(
            Pad(), _ => throw new InvalidOperationException("focus boom"), pointer: _ => { });

        Assert.False(registration.RequestKeyboardFocus());

        Assert.True(element.IsKeyInputDropped);
        Assert.Null(harness.Root.KeyboardFocus);
        Assert.False(registration.HasKeyboardFocus);
        Assert.Contains("focus boom", Assert.Single(harness.Reports));
    }
}
```

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -E " error " | head -2`
Expected: `The best overload for 'PluginCanvasElement' does not have a parameter named 'keyboardCaptured'`.

- [ ] **Step 2: Let an element compute whether it accepts focus**

The root reads `AcceptsFocus` on a left press, before the canvas sees the event, and the canvas's answer depends on the plugin's live handler and visibility; a value refreshed each tick could be a frame stale.

In `src/AcDream.App/UI/UiElement.cs`, replace

```csharp
    public Func<bool>? EnabledSource { get; set; }

    public bool AcceptsFocus    { get; set; }
    public bool FocusOnMouseClick { get; set; } = true;
    public bool TabStop         { get; set; }
```

with

```csharp
    public Func<bool>? EnabledSource { get; set; }

    /// <summary>
    /// Whether a left press (or Tab, with <see cref="TabStop"/>) gives this
    /// element keyboard focus. Virtual so an element whose answer depends
    /// on live state can compute it when the root asks.
    /// </summary>
    public virtual bool AcceptsFocus { get; set; }
    public bool FocusOnMouseClick { get; set; } = true;
    public bool TabStop         { get; set; }
```

- [ ] **Step 3: Focus in the element**

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
/// stays slow is dropped, and the canvas goes back to click-through while
/// its painting carries on.</para>
/// </summary>
internal sealed class PluginCanvasElement : UiElement
{
    /// <summary>
```

with

```csharp
/// stays slow is dropped, and the canvas goes back to click-through while
/// its painting carries on.</para>
///
/// <para>A canvas that opted in to keyboard input takes the interface's
/// keyboard focus, like a text field, while it is shown and has a key
/// handler: from a left press on it, or when the plugin asks and nothing
/// else has focus. Focus is the root's; the canvas only reports what the
/// root tells it, so exactly one focus lost follows each focus gained,
/// whichever way focus goes. While focused it hands the plugin keys,
/// typed text and its own key repeat, under a third guard.</para>
/// </summary>
internal sealed class PluginCanvasElement : UiElement, IPluginCanvasKeyboardFocus
{
    /// <summary>
```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
    private readonly UiDrawCallbackGuard _guard;
    private readonly UiDrawCallbackGuard _inputGuard;
    private readonly Func<PluginKeyModifiers> _modifiers;
    private readonly Action<string> _report;
    private readonly List<CanvasTarget> _targets = [];
```

with

```csharp
    private readonly UiDrawCallbackGuard _guard;
    private readonly UiDrawCallbackGuard _inputGuard;
    private readonly UiDrawCallbackGuard _keyGuard;
    private readonly Func<PluginKeyModifiers> _modifiers;
    private readonly Func<bool> _keyboardCaptured;
    private readonly Action<string> _report;
    private readonly List<CanvasTarget> _targets = [];
```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
    private PluginPointerButton _heldButton;
    private readonly Action? _pointerRelease;

    internal PluginCanvasElement(
```

with

```csharp
    private PluginPointerButton _heldButton;
    private readonly Action? _pointerRelease;
    private bool _focused;

    internal PluginCanvasElement(
```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
        Action<string>? report = null,
        Func<double>? nowMilliseconds = null,
        Func<PluginKeyModifiers>? modifiers = null)
    {
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
```

with

```csharp
        Action<string>? report = null,
        Func<double>? nowMilliseconds = null,
        Func<PluginKeyModifiers>? modifiers = null,
        Func<bool>? keyboardCaptured = null)
    {
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
        _report = report ?? (line => Serilog.Log.Warning("{Line}", line));
        _modifiers = modifiers ?? (static () => PluginKeyModifiers.None);
        _guard = new UiDrawCallbackGuard(
            $"plugin canvas {registration.Owner.Id}/{registration.CanvasId}",
```

with

```csharp
        _report = report ?? (line => Serilog.Log.Warning("{Line}", line));
        _modifiers = modifiers ?? (static () => PluginKeyModifiers.None);
        _keyboardCaptured = keyboardCaptured ?? (static () => false);
        _guard = new UiDrawCallbackGuard(
            $"plugin canvas {registration.Owner.Id}/{registration.CanvasId}",
```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
            UiDrawCallbackGuard.FrameBudgetMilliseconds,
            callUnit: "events");
        Name = $"PluginCanvas:{registration.Owner.Id}:{registration.CanvasId}";
        // Click-through is the element's own state; the layer it is added
```

with

```csharp
            UiDrawCallbackGuard.FrameBudgetMilliseconds,
            callUnit: "events");
        _keyGuard = new UiDrawCallbackGuard(
            $"plugin canvas {registration.Owner.Id}/{registration.CanvasId} key handler",
            _report,
            nowMilliseconds,
            UiDrawCallbackGuard.FrameBudgetMilliseconds,
            callUnit: "events");
        Name = $"PluginCanvas:{registration.Owner.Id}:{registration.CanvasId}";
        // Click-through is the element's own state; the layer it is added
```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
            registration.PointerRelease = _pointerRelease;
        }
    }

```

with

```csharp
            registration.PointerRelease = _pointerRelease;
        }
        if (registration.AcceptsKeyboardInput)
        {
            // Typed text reaches only an edit control, and the root sends
            // keys nowhere else while one has focus.
            IsEditControl = true;
            registration.KeyboardFocus = this;
        }
    }

```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
    internal bool IsInputDropped => _inputGuard.IsTripped;

    /// <summary>
    /// Whether the canvas answers for its rectangle right now: it opted in,
```

with

```csharp
    internal bool IsInputDropped => _inputGuard.IsTripped;

    /// <summary>True once the key handler was dropped; the canvas cannot take focus from then on.</summary>
    internal bool IsKeyInputDropped => _keyGuard.IsTripped;

    /// <summary>True while the root's keyboard focus is this canvas.</summary>
    public bool HasFocus => _focused;

    /// <summary>
    /// Whether the canvas may hold keyboard focus right now: it opted in,
    /// is shown, someone is listening, and neither the listener nor the
    /// paint callback has been dropped. Read by the root on a left press.
    /// </summary>
    public override bool AcceptsFocus
    {
        get => TakesKeyboard;
        // Computed from the canvas's own state; nothing sets it from outside.
        set { }
    }

    private bool TakesKeyboard =>
        _registration.AcceptsKeyboardInput
        && !_keyGuard.IsTripped
        && _registration.KeyHandler is not null
        && VisibleSource!();

    /// <summary>
    /// Whether the canvas answers for its rectangle right now: it opted in,
```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
    protected override bool ClipsChildren => true;

    protected override void OnTick(double deltaSeconds) => Layout();

    protected override bool OnHitTest(float localX, float localY) =>
```

with

```csharp
    protected override bool ClipsChildren => true;

    protected override void OnTick(double deltaSeconds)
    {
        Layout();
        if (_focused) KeepsFocus();
    }

    protected override bool OnHitTest(float localX, float localY) =>
```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
            }

            // Click, double-click and right-click are the root's reading of a
            // press and release the plugin already saw; nothing beneath the
```

with

```csharp
            }

            case UiEventType.FocusGained:
                _focused = true;
                DeliverKey(new PluginKeyEvent(PluginKeyEventKind.FocusGained, PluginKey.Unknown, _modifiers()));
                return true;

            case UiEventType.FocusLost:
                if (!_focused) return true;
                _focused = false;
                DeliverKey(new PluginKeyEvent(PluginKeyEventKind.FocusLost, PluginKey.Unknown, _modifiers()));
                return true;

            // Click, double-click and right-click are the root's reading of a
            // press and release the plugin already saw; nothing beneath the
```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp

    /// <summary>
    /// Places the canvas by anchor plus offset inside its layer, which the
    /// overlay host keeps equal to the viewport. Re-run every tick because
```

with

```csharp

    /// <summary>
    /// Gives focus back when the canvas can no longer hold it: the plugin
    /// hid it or took its handler away, or a modal dialog opened. Checked
    /// every tick and before every key, so a key between a change and the
    /// next tick does not reach the plugin. Removal is the root's to notice.
    /// </summary>
    private bool KeepsFocus()
    {
        if (TakesKeyboard && FindRoot()?.Modal is null) return true;
        ReleaseFocus();
        return false;
    }

    /// <summary>
    /// Hands one event to the key handler under its guard. Returns the
    /// handler's answer; false when nobody heard it.
    /// </summary>
    private bool DeliverKey(PluginKeyEvent key)
    {
        Func<PluginKeyEvent, bool>? handler = _registration.KeyHandler;
        if (handler is null || _keyGuard.IsTripped) return false;
        bool handled = false;
        _keyGuard.Invoke(() => handled = handler(key));
        if (_keyGuard.IsTripped)
        {
            // Nobody is listening any more: give the keyboard back.
            ReleaseFocus();
            return false;
        }
        return handled;
    }

    /// <summary>
    /// Takes the keyboard on the plugin's request, which never takes it
    /// from anything else: the chat bar, a text field, another canvas or a
    /// dialog keeps it, and so does a key rebind being captured.
    /// </summary>
    public bool RequestFocus()
    {
        if (_focused) return true;
        if (_released || !TakesKeyboard || !IsShownInTree()) return false;
        if (FindRoot() is not { } root) return false;
        if (root.KeyboardFocus is not null || root.Modal is not null || _keyboardCaptured())
            return false;
        root.SetKeyboardFocus(this);
        return _focused;
    }

    /// <summary>
    /// Gives the keyboard back if the canvas still has it; the root's focus
    /// change delivers the one focus lost.
    /// </summary>
    public void ReleaseFocus()
    {
        if (FindRoot() is { } root && ReferenceEquals(root.KeyboardFocus, this))
            root.SetKeyboardFocus(null);
    }

    private bool IsShownInTree()
    {
        for (UiElement? element = this; element is not null; element = element.Parent)
        {
            if (!element.Visible) return false;
        }
        return true;
    }

    /// <summary>
    /// Places the canvas by anchor plus offset inside its layer, which the
    /// overlay host keeps equal to the viewport. Re-run every tick because
```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
        if (ReferenceEquals(_registration.PointerRelease, _pointerRelease))
            _registration.PointerRelease = null;
        IGpuDevice device = _surface.Services.Device;
        foreach (CanvasTarget target in _targets)
```

with

```csharp
        if (ReferenceEquals(_registration.PointerRelease, _pointerRelease))
            _registration.PointerRelease = null;
        // Focus needs nothing here: the element left the tree first, and
        // the root took focus back from it on the way out.
        if (ReferenceEquals(_registration.KeyboardFocus, this))
            _registration.KeyboardFocus = null;
        IGpuDevice device = _surface.Services.Device;
        foreach (CanvasTarget target in _targets)
```

- [ ] **Step 4: Pass the rebind-capture signal at mount**

In `src/AcDream.App/UI/RetailUiRuntime.cs`, replace

```csharp
                    _pluginCanvasSurface,
                    () => plugins.FindImages(owner),
                    modifiers: HeldPointerModifiers);
                layer.AddChild(element, takesInput: canvas.AcceptsPointerInput);
                plugins.CompleteCanvasMount(canvas, () =>
```

with

```csharp
                    _pluginCanvasSurface,
                    () => plugins.FindImages(owner),
                    modifiers: HeldPointerModifiers,
                    keyboardCaptured: () => _bindings.Keyboard?.Dispatcher?.IsCapturing == true);
                layer.AddChild(element, takesInput: canvas.AcceptsPointerInput);
                plugins.CompleteCanvasMount(canvas, () =>
```

In `src/AcDream.App/UI/RetailUiRuntime.cs`, replace

```csharp
    /// <summary>
    /// The modifier keys held right now, read from the window's keyboard
    /// for each pointer event a plugin canvas delivers; none without one.
    /// </summary>
    private PluginKeyModifiers HeldPointerModifiers()
```

with

```csharp
    /// <summary>
    /// The modifier keys held right now, read from the window's keyboard
    /// for each pointer or key event a plugin canvas delivers; none without one.
    /// </summary>
    private PluginKeyModifiers HeldPointerModifiers()
```

- [ ] **Step 5: Build and run the tests**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~PluginCanvasKeyboardTests|FullyQualifiedName~PluginCanvasPointerTests|FullyQualifiedName~PluginCanvasElementTests"
```

Expected: `0 Warning(s)`, `0 Error(s)`; `Passed!  - Failed:     0, Passed:    56` (20 new focus tests, the pointer and element suites unchanged).

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/UiElement.cs src/AcDream.App/UI/Layout/PluginCanvasElement.cs src/AcDream.App/UI/RetailUiRuntime.cs tests/AcDream.App.Tests/UI/Layout/PluginCanvasKeyboardTests.cs
git commit -m "plugin canvas: a canvas takes and gives back keyboard focus

An opted-in canvas with a key handler takes the interface's keyboard
focus from a left press or on request, never from anything that already
has it, and never under a modal or a key rebind capture. It gives focus
back when hidden, removed, left without a handler, covered by a modal or
asked to, with one focus lost each time. Whether it accepts focus is
computed when the root asks, so an element's AcceptsFocus is virtual.

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/UiElement.cs", "src/AcDream.App/UI/Layout/PluginCanvasElement.cs", "src/AcDream.App/UI/RetailUiRuntime.cs", "tests/AcDream.App.Tests/UI/Layout/PluginCanvasKeyboardTests.cs"], "verifyCommand": "TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~PluginCanvasKeyboardTests|FullyQualifiedName~PluginCanvasPointerTests|FullyQualifiedName~PluginCanvasElementTests\"", "acceptanceCriteria": ["AcceptsFocus virtual; canvas computes it", "opted-in canvas is an edit control and sets the registration hook", "keyboardCaptured passed at mount", "left press focuses, right press does not, non-opted-in or handlerless never", "request refusals: text field, modal, capture, hidden, no handler, disposed", "one FocusLost on every focus-ending path", "throwing FocusGained handler dropped", "0 warnings"], "modelTier": "standard"}
```

---

### Task 5: Keys, typed text and repeat reach a focused canvas

**Goal:** While a canvas has focus, keys `PluginKey` can name arrive as `Down`/`Up`, typed characters as `Text`, a held key repeats as `Down` with `IsRepeat`, an Escape the handler did not handle gives focus back, a misbehaving key handler is dropped, and the game sees none of it.

**Files:**
- Modify: `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`
- Test: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasKeyboardTests.cs`

**Acceptance Criteria:**
- [ ] `KeyDown`/`KeyUp` map through `PluginKeyMap.FromSilk`; an unnamed key (Shift, Ctrl, Caps Lock) is consumed and not delivered; modifiers come from the same source as pointer events
- [ ] `Char` delivers `Text` for a valid, non-control `Rune`; control characters and lone surrogates are dropped
- [ ] Before every key, text or tick the canvas checks it can still hold focus (shown, handler set, no modal), so a key after a hide but before the next tick is not delivered
- [ ] A held key repeats after `KeyRepeatDelaySeconds = 0.40`, then every `KeyRepeatIntervalSeconds = 0.04`, one per tick, until its `Up` or focus loss
- [ ] An Escape `Down` (or repeat) the handler answers false to gives focus back; one it answers true to keeps it
- [ ] Releasing focus from inside the handler is safe
- [ ] A throwing key handler is dropped at once and a slow one after three overruns in a row (report names `key handler`), focus goes back, `RequestKeyboardFocus()` answers false from then on, and pointer input and painting continue
- [ ] With a canvas focused, a dispatcher binding and a plain plugin hotkey on the same keyboard do not fire; after a press elsewhere both do
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`

**Verify:** `TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginCanvasKeyboardTests"` → `Passed!  - Failed:     0, Passed:    32`

**Steps:**

- [ ] **Step 1: Write the failing key tests**

In `tests/AcDream.App.Tests/UI/Layout/PluginCanvasKeyboardTests.cs`, replace

```csharp
using System.Collections.Generic;
using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
```

with

```csharp
using System.Collections.Generic;
using System.Numerics;
using AcDream.App.Input;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
```

In `tests/AcDream.App.Tests/UI/Layout/PluginCanvasKeyboardTests.cs`, replace

```csharp
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;
using Silk.NET.Input;

```

with

```csharp
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

```

In `tests/AcDream.App.Tests/UI/Layout/PluginCanvasKeyboardTests.cs`, replace

```csharp
        Assert.Contains("focus boom", Assert.Single(harness.Reports));
    }
}
```

with

```csharp
        Assert.Contains("focus boom", Assert.Single(harness.Reports));
    }

    [Fact]
    public void KeysAndTypedTextArriveWhileTheCanvasHasFocus()
    {
        (Harness harness, _, _, Recorder keys) = FocusedPad();

        harness.Root.OnKeyDown((int)Key.A);
        harness.Root.OnChar('a');
        harness.Root.OnKeyUp((int)Key.A);

        Assert.Equal(
            [
                new PluginKeyEvent(PluginKeyEventKind.Down, PluginKey.A, PluginKeyModifiers.None),
                new PluginKeyEvent(PluginKeyEventKind.Text, PluginKey.Unknown, PluginKeyModifiers.None, Text: "a"),
                new PluginKeyEvent(PluginKeyEventKind.Up, PluginKey.A, PluginKeyModifiers.None),
            ],
            keys.Events);
    }

    [Fact]
    public void AnEscapeTheHandlerDidNotHandleGivesFocusBack()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        harness.Root.OnKeyDown((int)Key.Escape);

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.False(registration.HasKeyboardFocus);
        Assert.Equal([PluginKeyEventKind.Down, PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void AnEscapeTheHandlerHandledKeepsFocus()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();
        keys.HandlesEscape = true;

        harness.Type(Key.Escape);

        Assert.True(registration.HasKeyboardFocus);
        Assert.Equal([PluginKeyEventKind.Down, PluginKeyEventKind.Up], keys.Kinds);
    }

    [Fact]
    public void ReleasingFocusFromInsideTheHandlerIsSafe()
    {
        var harness = new Harness();
        var kinds = new List<PluginKeyEventKind>();
        IPluginCanvas? canvas = null;
        (PluginCanvasRegistration registration, _) = harness.Mount(Pad(), e =>
        {
            kinds.Add(e.Kind);
            if (e.Kind == PluginKeyEventKind.Down) canvas!.ReleaseKeyboardFocus();
            return true;
        }, pointer: _ => { });
        canvas = registration;

        harness.Press(100, 60);
        harness.Type(Key.Enter);

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusGained, PluginKeyEventKind.Down, PluginKeyEventKind.FocusLost], kinds);
    }

    [Fact]
    public void AKeyAfterTheCanvasIsHiddenButBeforeTheNextTickDoesNotReachThePlugin()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        registration.IsVisible = false;
        harness.Type(Key.A);

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void AHeldKeyRepeatsAfterTheDelayUntilItComesUp()
    {
        (Harness harness, _, _, Recorder keys) = FocusedPad();

        harness.Root.OnKeyDown((int)Key.Backspace);
        harness.Tick(0.39);
        Assert.Single(keys.Events);
        harness.Tick(0.02);
        harness.Tick(0.04);
        harness.Root.OnKeyUp((int)Key.Backspace);
        harness.Tick(1.0);

        Assert.Equal(
            [
                new PluginKeyEvent(PluginKeyEventKind.Down, PluginKey.Backspace, PluginKeyModifiers.None),
                new PluginKeyEvent(PluginKeyEventKind.Down, PluginKey.Backspace, PluginKeyModifiers.None, IsRepeat: true),
                new PluginKeyEvent(PluginKeyEventKind.Down, PluginKey.Backspace, PluginKeyModifiers.None, IsRepeat: true),
                new PluginKeyEvent(PluginKeyEventKind.Up, PluginKey.Backspace, PluginKeyModifiers.None),
            ],
            keys.Events);
    }

    [Fact]
    public void LosingFocusStopsTheRepeat()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        harness.Root.OnKeyDown((int)Key.Left);
        registration.ReleaseKeyboardFocus();
        harness.Tick(1.0);

        Assert.Equal([PluginKeyEventKind.Down, PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void KeysThePluginCannotNameAreNotDeliveredButModifiersTravelWithEveryEvent()
    {
        (Harness harness, _, _, Recorder keys) = FocusedPad();

        harness.Modifiers = PluginKeyModifiers.Shift;
        harness.Root.OnKeyDown((int)Key.ShiftLeft);
        harness.Root.OnKeyDown((int)Key.A);
        harness.Root.OnChar('A');
        harness.Root.OnKeyUp((int)Key.A);
        harness.Modifiers = PluginKeyModifiers.None;
        harness.Root.OnKeyUp((int)Key.ShiftLeft);

        Assert.Equal(
            [PluginKeyEventKind.Down, PluginKeyEventKind.Text, PluginKeyEventKind.Up],
            keys.Kinds);
        Assert.All(keys.Events, e => Assert.Equal(PluginKeyModifiers.Shift, e.Modifiers));
        Assert.Equal("A", keys.Events[1].Text);
    }

    [Fact]
    public void ControlCharactersAndLoneSurrogatesAreNotText()
    {
        (Harness harness, _, _, Recorder keys) = FocusedPad();

        harness.Root.OnChar('\r');
        harness.Root.OnChar('\b');
        harness.Root.OnChar(0xD83D);
        harness.Root.OnChar('é');

        PluginKeyEvent only = Assert.Single(keys.Events);
        Assert.Equal("é", only.Text);
    }

    [Fact]
    public void AThrowingKeyHandlerIsDroppedFocusGoesBackAndPointerAndPaintingCarryOn()
    {
        var harness = new Harness();
        int calls = 0;
        var pointer = new List<PluginPointerEvent>();
        (PluginCanvasRegistration registration, PluginCanvasElement element) = harness.Mount(Pad(), e =>
        {
            calls++;
            if (e.Kind == PluginKeyEventKind.Down) throw new InvalidOperationException("keys boom");
            return true;
        }, pointer: pointer.Add);

        harness.Press(100, 60);
        harness.Type(Key.A);
        harness.Press(100, 60);
        harness.Type(Key.B);

        Assert.Equal(2, calls);   // the focus gained, then the throwing down
        Assert.True(element.IsKeyInputDropped);
        Assert.False(element.IsInputDropped);
        Assert.Null(harness.Root.KeyboardFocus);
        Assert.False(registration.RequestKeyboardFocus());
        string report = Assert.Single(harness.Reports);
        Assert.Contains("keys boom", report);
        Assert.Contains("key handler", report);
        Assert.Equal(4, pointer.Count);
        Assert.False(registration.IsDropped);
        Assert.True(element.Visible);
    }

    [Fact]
    public void ASlowKeyHandlerIsDroppedAfterThreeOverrunsInARow()
    {
        (Harness harness, _, PluginCanvasElement element, Recorder keys) = FocusedPad();
        harness.Clock.NextCallCosts = UiDrawCallbackGuard.FrameBudgetMilliseconds + 1.0;

        harness.Root.OnKeyDown((int)Key.A);
        harness.Root.OnKeyDown((int)Key.B);
        Assert.False(element.IsKeyInputDropped);
        harness.Root.OnKeyDown((int)Key.C);
        Assert.True(element.IsKeyInputDropped);
        harness.Root.OnKeyDown((int)Key.D);

        Assert.Equal(3, keys.Events.Count);
        Assert.Contains("3 events in a row", Assert.Single(harness.Reports));
        Assert.Null(harness.Root.KeyboardFocus);
    }

    [Fact]
    public void WhileACanvasHasFocusTheGameGetsNoKeysAndPlainHotkeysDoNotFire()
    {
        var harness = new Harness();
        var keys = new Recorder();
        harness.Mount(Pad(), keys.Handle, pointer: _ => { });
        var slot = new RetainedUiInputCaptureSlot();
        using IDisposable bound = slot.Bind(harness.Root);
        var keyboard = new FakeKeyboard();
        var bindings = new KeyBindings();
        bindings.Add(new Binding(new KeyChord(Key.W, ModifierMask.None), InputAction.MovementForward));
        InputDispatcher dispatcher = InputDispatcher.CreateDetached(keyboard, new SlotMouse(slot), bindings);
        dispatcher.Attach();
        var fired = new List<InputAction>();
        dispatcher.Fired += (action, _) => fired.Add(action);
        var hotkeys = new AppHotkeyRegistry(overridesFilePath: null);
        hotkeys.Bind(keyboard, bindings, dispatcher, slot);
        int hotkeyFired = 0;
        hotkeys.Register("plain", "Plain", new PluginKeyChord(PluginKey.F9), () => hotkeyFired++);

        harness.Press(100, 60);
        keyboard.Fire(Key.W);
        keyboard.Fire(Key.F9);
        Assert.Empty(fired);
        Assert.Equal(0, hotkeyFired);

        harness.Press(400, 300);
        keyboard.Fire(Key.W);
        keyboard.Fire(Key.F9);
        Assert.Equal([InputAction.MovementForward], fired);
        Assert.Equal(1, hotkeyFired);
        dispatcher.Dispose();
    }

    private sealed class FakeKeyboard : IKeyboardSource
    {
        public event Action<Key, ModifierMask>? KeyDown;
#pragma warning disable CS0067
        public event Action<Key, ModifierMask>? KeyUp;
#pragma warning restore CS0067
        public bool IsHeld(Key key) => false;
        public ModifierMask CurrentModifiers => ModifierMask.None;

        public void Fire(Key key) => KeyDown?.Invoke(key, ModifierMask.None);
    }

    /// <summary>The mouse source the dispatcher asks, answering for the keyboard as the window's does: from the capture slot.</summary>
    private sealed class SlotMouse(RetainedUiInputCaptureSlot slot) : IMouseSource
    {
#pragma warning disable CS0067
        public event Action<MouseButton, ModifierMask>? MouseDown;
        public event Action<MouseButton, ModifierMask>? MouseUp;
        public event Action<float, float>? MouseMove;
        public event Action<float>? Scroll;
#pragma warning restore CS0067
        public bool WantCaptureKeyboard => slot.WantCaptureKeyboard;
        public bool WantCaptureMouse => slot.WantCaptureMouse;
        public bool IsHeld(MouseButton button) => false;
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet build tests/AcDream.App.Tests -c Release 2>&1 | tail -2
TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~PluginCanvasKeyboardTests"
```

Expected: `Failed:    11, Passed:    21`. The one new test that already passes is `WhileACanvasHasFocusTheGameGetsNoKeysAndPlainHotkeysDoNotFire`: focus alone takes the keyboard from the game (Task 4), which is the point of putting canvas focus on the root.

- [ ] **Step 3: Keys, text and repeat in the element**

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
using System.Collections.Generic;
using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
```

with

```csharp
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using AcDream.App.Input;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
    internal const int MaximumTargets = 8;

    private sealed class CanvasTarget(IGpuRenderTarget target, GpuTextureSlot slot)
    {
```

with

```csharp
    internal const int MaximumTargets = 8;

    /// <summary>How long a key is held before the host repeats it, as a text field does.</summary>
    internal const double KeyRepeatDelaySeconds = 0.40;

    /// <summary>How often a held key repeats after the delay, as a text field does.</summary>
    internal const double KeyRepeatIntervalSeconds = 0.04;

    private sealed class CanvasTarget(IGpuRenderTarget target, GpuTextureSlot slot)
    {
```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
    private readonly Action? _pointerRelease;
    private bool _focused;

    internal PluginCanvasElement(
```

with

```csharp
    private readonly Action? _pointerRelease;
    private bool _focused;
    private PluginKey _repeatKey;
    private double _repeatTimer;

    internal PluginCanvasElement(
```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
    {
        Layout();
        if (_focused) KeepsFocus();
    }

```

with

```csharp
    {
        Layout();
        if (!_focused || !KeepsFocus()) return;
        if (_repeatKey == PluginKey.Unknown) return;
        _repeatTimer -= deltaSeconds;
        if (_repeatTimer > 0) return;
        _repeatTimer = KeyRepeatIntervalSeconds;
        KeyDown(_repeatKey, isRepeat: true);
    }

```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
                if (!_focused) return true;
                _focused = false;
                DeliverKey(new PluginKeyEvent(PluginKeyEventKind.FocusLost, PluginKey.Unknown, _modifiers()));
                return true;

            // Click, double-click and right-click are the root's reading of a
```

with

```csharp
                if (!_focused) return true;
                _focused = false;
                _repeatKey = PluginKey.Unknown;
                DeliverKey(new PluginKeyEvent(PluginKeyEventKind.FocusLost, PluginKey.Unknown, _modifiers()));
                return true;

            case UiEventType.KeyDown:
            {
                if (!_focused) return false;
                if (!KeepsFocus()) return true;
                PluginKey key = PluginKeyMap.FromSilk((Silk.NET.Input.Key)e.Data0);
                // A key the contract cannot name is still the canvas's:
                // nothing behind a focused canvas acts on keys.
                if (key == PluginKey.Unknown) return true;
                _repeatKey = key;
                _repeatTimer = KeyRepeatDelaySeconds;
                KeyDown(key, isRepeat: false);
                return true;
            }

            case UiEventType.KeyUp:
            {
                if (!_focused) return false;
                if (!KeepsFocus()) return true;
                PluginKey key = PluginKeyMap.FromSilk((Silk.NET.Input.Key)e.Data0);
                if (key == PluginKey.Unknown) return true;
                if (key == _repeatKey) _repeatKey = PluginKey.Unknown;
                DeliverKey(new PluginKeyEvent(PluginKeyEventKind.Up, key, _modifiers()));
                return true;
            }

            case UiEventType.Char:
            {
                if (!_focused) return false;
                if (!KeepsFocus()) return true;
                // Silk hands over UTF-16 units; a lone surrogate or a control
                // character is not text a plugin can draw.
                if (!Rune.IsValid(e.Data0) || Rune.IsControl(new Rune(e.Data0))) return true;
                DeliverKey(new PluginKeyEvent(
                    PluginKeyEventKind.Text, PluginKey.Unknown, _modifiers(), Text: new Rune(e.Data0).ToString()));
                return true;
            }

            // Click, double-click and right-click are the root's reading of a
```

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp

    /// <summary>
    /// Hands one event to the key handler under its guard. Returns the
    /// handler's answer; false when nobody heard it.
```

with

```csharp

    /// <summary>
    /// A key went down or repeated: the plugin sees it, and an Escape it
    /// did not handle gives focus back, as Escape leaves a text field.
    /// </summary>
    private void KeyDown(PluginKey key, bool isRepeat)
    {
        bool handled = DeliverKey(new PluginKeyEvent(PluginKeyEventKind.Down, key, _modifiers(), isRepeat));
        if (key == PluginKey.Escape && !handled)
            ReleaseFocus();
    }

    /// <summary>
    /// Hands one event to the key handler under its guard. Returns the
    /// handler's answer; false when nobody heard it.
```

- [ ] **Step 4: Build and run the tests**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~PluginCanvasKeyboardTests"
```

Expected: `0 Warning(s)`, `0 Error(s)`; `Passed!  - Failed:     0, Passed:    32`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Layout/PluginCanvasElement.cs tests/AcDream.App.Tests/UI/Layout/PluginCanvasKeyboardTests.cs
git commit -m "plugin canvas: keys, typed text and repeat reach a focused canvas

A focused canvas hands its plugin every key the contract can name, the
text typed, and its own repeat for a held key, as the client's text
fields repeat. An Escape the plugin did not handle gives focus back. The
key handler has its own guard; dropping it gives focus back and leaves
pointer input and painting alone.

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/Layout/PluginCanvasElement.cs", "tests/AcDream.App.Tests/UI/Layout/PluginCanvasKeyboardTests.cs"], "verifyCommand": "TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~PluginCanvasKeyboardTests\"", "acceptanceCriteria": ["named keys as Down/Up, unnamed consumed, modifiers travel", "Text for valid non-control runes only", "focus re-checked before every key", "repeat 0.40 s then 0.04 s until Up or focus loss", "unhandled Escape gives focus back, handled keeps it", "release from inside the handler is safe", "throwing and slow key handlers dropped, focus back, pointer and paint continue", "game bindings and plain hotkeys suppressed while focused", "0 warnings"], "modelTier": "standard"}
```

---

### Task 6: Documentation

**Goal:** The plugin API guide has a "Keyboard input" subsection under Canvases that matches the behaviour, the Canvases intro and the Hotkeys section mention it, and the markup guide's test list names the new suites.

**Files:**
- Modify: `docs/plugin-api.md` (Hotkeys, Canvases intro, new `### Keyboard input` after `### Pointer input`)
- Modify: `docs/plugin-ui-markup.md` (Tests section)

**Acceptance Criteria:**
- [ ] `### Keyboard input` sits between `### Pointer input` and `## Dungeon map` and covers: opting in, the two ways to take focus (a left press needs `AcceptsPointerInput` and a `PointerHandler`), what the game and hotkeys see, the event fields, text versus `Down`, unnamed keys, repeat timing, the Escape policy, request refusals, every focus-ending path, the guard, and the headless answers
- [ ] The example compiles as written in a plugin (its locals are declared before the paint callback uses them)
- [ ] The Hotkeys paragraph lists "a plugin canvas with keyboard focus" among the things that hold focus
- [ ] The markup guide's test list names `UI/Layout/PluginCanvasKeyboardTests` and `Input/PluginKeyMapTests`

**Verify:** `grep -c "^### Keyboard input" docs/plugin-api.md && grep -c "a plugin canvas with keyboard focus" docs/plugin-api.md && grep -c "PluginCanvasKeyboardTests" docs/plugin-ui-markup.md` → `1`, `1`, `1`

**Steps:**

- [ ] **Step 1: The API guide**

In `docs/plugin-api.md`, replace

````markdown

While anything in the interface has keyboard focus (the chat bar, a text
field, a control reached with Tab), a hotkey fires only if Ctrl or Alt is
part of the chord; otherwise every letter typed would also be a candidate
hotkey press. Clicking a button does not give it keyboard focus. While a
modal dialog is open, or a key rebind is being captured
(`InputDispatcher.BeginCapture`), no hotkey fires at all, Ctrl and Alt
````

with

````markdown

While anything in the interface has keyboard focus (the chat bar, a text
field, a control reached with Tab, a plugin canvas with keyboard focus), a
hotkey fires only if Ctrl or Alt is part of the chord; otherwise every
letter typed would also be a candidate hotkey press. Clicking a button does not give it keyboard focus. While a
modal dialog is open, or a key rebind is being captured
(`InputDispatcher.BeginCapture`), no hotkey fires at all, Ctrl and Alt
````

In `docs/plugin-api.md`, replace

````markdown
A canvas is a rectangle the plugin paints, shown over the world and under
every window, taking no input unless it asks for it (see
[Pointer input](#pointer-input) below). It is positioned by an anchor plus
an offset, it is exactly its declared size, and everything painted is
clipped to it; there is no way to draw anywhere else on the screen.
````

with

````markdown
A canvas is a rectangle the plugin paints, shown over the world and under
every window, taking no input unless it asks for it (see
[Pointer input](#pointer-input) and [Keyboard input](#keyboard-input)
below). It is positioned by an anchor plus
an offset, it is exactly its declared size, and everything painted is
clipped to it; there is no way to draw anywhere else on the screen.
````

In `docs/plugin-api.md`, replace

````markdown
handler is never called, and `ReleasePointer()` does nothing.

## Dungeon map

````

with

````markdown
handler is never called, and `ReleasePointer()` does nothing.

### Keyboard input

A canvas that wants keys (a text box, a widget driven by the arrow keys)
opts in with `AcceptsKeyboardInput` on the descriptor and sets a
`KeyHandler`. It then takes the interface's keyboard focus, as the chat
bar does, in one of two ways: a left press on it (which needs
`AcceptsPointerInput` and a `PointerHandler` as well, since only a canvas
that takes the pointer can be clicked), or `RequestKeyboardFocus()`.
While it has focus, keys go to the handler and not to the game: the
character does not move, the client's own key bindings do not fire, and
plugin hotkeys follow the rule in [Hotkeys](#hotkeys) (only chords with
Ctrl or Alt).

```csharp
string query = "";
bool focused = false;
IPluginCanvas box = host.Ui.RegisterCanvas(
    new PluginCanvasDescriptor("search", 240, 24)
    {
        AcceptsPointerInput = true,
        AcceptsKeyboardInput = true,
    },
    painter => DrawBox(painter, query, focused));

box.PointerHandler = _ => { };   // a press on the box gives it focus
box.KeyHandler = e =>
{
    switch (e.Kind)
    {
        case PluginKeyEventKind.FocusGained or PluginKeyEventKind.FocusLost:
            focused = e.Kind == PluginKeyEventKind.FocusGained;
            break;
        case PluginKeyEventKind.Text:
            query += e.Text;
            break;
        case PluginKeyEventKind.Down when e.Key == PluginKey.Backspace && query.Length > 0:
            query = query[..^1];
            break;
        case PluginKeyEventKind.Down when e.Key == PluginKey.Enter:
            Search(query);
            box.ReleaseKeyboardFocus();
            break;
        case PluginKeyEventKind.Down when e.Key == PluginKey.Escape:
            return false;   // not handled: focus goes back to the game
    }
    box.Invalidate();
    return true;
};
```

Every event arrives on the tick thread as a `PluginKeyEvent`: its `Kind`
(`Down`, `Up`, `Text`, `FocusGained`, `FocusLost`), the `Key` for `Down`
and `Up` (`PluginKey.Unknown` otherwise), the `Modifiers` held, read from
the same place as for pointer events, `IsRepeat`, and for `Text` the typed
characters in `Text`. Text is what the player's keyboard layout typed, so
use it for anything that is typed and `Down` for keys that do something;
it never holds a control character, so Enter, Tab and Backspace arrive
only as `Down`. Keys `PluginKey` cannot name, the modifier keys among
them, arrive as neither `Down` nor `Up`. A key held down repeats as
`Down` with `IsRepeat` set, after 0.4 s and then every 0.04 s, as in the
client's own text fields, until it comes up.

The handler answers true when it handled the event. Only one answer
changes what the host does: an Escape `Down` the handler did not handle
gives focus back, as Escape leaves a text field, and the game does not see
that Escape either. A canvas that wants Escape for itself answers true.

`RequestKeyboardFocus()` never takes focus from anything else: it answers
false, and changes nothing, while the canvas did not opt in, is not
mounted or shown, has no handler, or while the chat bar, a text field,
another canvas or a dialog has focus, a modal dialog is open, or a key
rebind is being captured. It answers true when the canvas has focus
afterwards, including when it already had it. Ask in answer to something
the player did, so keys never vanish into a canvas they did not choose.

Focus goes back, with exactly one `FocusLost` to the handler, when an
Escape is not handled, the player presses the left button anywhere else,
the canvas is hidden or disposed, the handler is set to null or dropped,
the paint callback is dropped, a modal dialog opens, the interface is torn
down, or the plugin calls `ReleaseKeyboardFocus()`. `HasKeyboardFocus`
says whether the canvas has focus right now.

The key handler has its own guard, measured like the pointer handler: one
that stays over the 2 ms budget on three events in a row, or throws, is
dropped for the rest of the session and focus goes back; pointer input and
painting continue, and the client's log says why. The handler is dropped
with the paint callback when the canvas is disposed.

Without a window `AcceptsKeyboardInput` and the handler are kept, the
handler is never called, `RequestKeyboardFocus()` answers false and
`HasKeyboardFocus` is false.

## Dungeon map

````

- [ ] **Step 2: The markup guide's test list**

In `docs/plugin-ui-markup.md`, replace

````markdown
and canvases (see the plugin API guide) are covered by
`PluginImageTableTests`, `BufferedUiRegistryImagesTests`,
`BufferedUiRegistryCanvasTests` and `UI/Layout/PluginCanvasElementTests`
under `tests/AcDream.App.Tests/`, by `ScopedUiRegistryImagesTests` and
`ScopedUiRegistryCanvasTests` for the scoped forwarder, and by the contract
````

with

````markdown
and canvases (see the plugin API guide) are covered by
`PluginImageTableTests`, `BufferedUiRegistryImagesTests`,
`BufferedUiRegistryCanvasTests`, `UI/Layout/PluginCanvasElementTests`,
`UI/Layout/PluginCanvasKeyboardTests` and `Input/PluginKeyMapTests`
under `tests/AcDream.App.Tests/`, by `ScopedUiRegistryImagesTests` and
`ScopedUiRegistryCanvasTests` for the scoped forwarder, and by the contract
````

- [ ] **Step 3: Check and commit**

```bash
grep -c "^### Keyboard input" docs/plugin-api.md && grep -c "a plugin canvas with keyboard focus" docs/plugin-api.md && grep -c "PluginCanvasKeyboardTests" docs/plugin-ui-markup.md
git add docs/plugin-api.md docs/plugin-ui-markup.md
git commit -m "docs: canvas keyboard input

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

Expected: `1`, `1`, `1`.

```json:metadata
{"files": ["docs/plugin-api.md", "docs/plugin-ui-markup.md"], "verifyCommand": "grep -c \"^### Keyboard input\" docs/plugin-api.md && grep -c \"a plugin canvas with keyboard focus\" docs/plugin-api.md && grep -c \"PluginCanvasKeyboardTests\" docs/plugin-ui-markup.md", "acceptanceCriteria": ["Keyboard input subsection between Pointer input and Dungeon map covering every behaviour", "example compiles as written", "Hotkeys paragraph names a focused canvas", "markup test list names the new suites"], "modelTier": "standard"}
```

---

### Task 7: Full verification and push

**Goal:** The branch builds with 0 warnings, fails only baseline tests in the portable suite, and is pushed to `origin`.

**Files:** none

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`
- [ ] Every portable-suite failure is in `/tmp/openac-keyboard/baseline-failures.txt` or is a HostParity `Peer*ParityTests`
- [ ] `git log --oneline f951ce16..HEAD` shows the six commits of Tasks 1–6, and `git diff --name-only f951ce16 HEAD` lists exactly the 15 files of the File Structure table
- [ ] `origin/painter-v2/keyboard` equals local `HEAD`

**Verify:** `git fetch origin && test "$(git rev-parse HEAD)" = "$(git rev-parse origin/painter-v2/keyboard)" && git log --oneline f951ce16..HEAD | wc -l` → `6`

**Steps:**

- [ ] **Step 1: Build and test**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test AcDream.slnx -c Release --no-build --filter "Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure" 2>&1 \
  | grep -E "^\s+Failed " | sed -E 's/ \[[^]]*\]$//' | sort -u > /tmp/openac-keyboard/branch-failures.txt
comm -23 /tmp/openac-keyboard/branch-failures.txt /tmp/openac-keyboard/baseline-failures.txt | grep -v "HostParity.Tests.Peer" || echo "no new failures"
git diff --name-only f951ce16 HEAD | wc -l
git checkout -- '*.lock.json'
```

Expected: `0 Warning(s)`, `0 Error(s)`; `no new failures`; `15`.

- [ ] **Step 2: Push**

```bash
git push -u origin painter-v2/keyboard
git fetch origin && test "$(git rev-parse HEAD)" = "$(git rev-parse origin/painter-v2/keyboard)" && git log --oneline f951ce16..HEAD | wc -l
```

Expected: `6`.

```json:metadata
{"files": [], "verifyCommand": "git fetch origin && test \"$(git rev-parse HEAD)\" = \"$(git rev-parse origin/painter-v2/keyboard)\" && git log --oneline f951ce16..HEAD | wc -l", "acceptanceCriteria": ["0 warnings", "no portable failures beyond baseline", "six commits, fifteen files", "pushed to origin/painter-v2/keyboard"], "modelTier": "mechanical"}
```

---

### Task 8: Merge into fork main

**Goal:** Fork `main` takes `painter-v2/keyboard` with a merge commit, with the seven conflicts resolved as below, builds with 0 warnings, passes the portable suite and the `Lane=Vulkan` canvas tests, and is pushed; the feature branch itself is unchanged.

**Files:** the merge commit on `main` (conflicts in `docs/plugin-api.md`, `src/AcDream.App/Plugins/PluginCanvasRegistration.cs`, `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs`, `src/AcDream.Core/Plugins/ScopedPluginHost.cs`, `src/AcDream.Plugin.Abstractions/PluginCanvas.cs`, `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryCanvasTests.cs`)

**Acceptance Criteria:**
- [ ] `git log -1 --format=%P main` lists two parents: the previous `origin/main` and `painter-v2/keyboard`
- [ ] No conflict markers remain (`git diff --check` and `grep -rn '^<<<<<<<\|^>>>>>>>' src tests docs` find nothing)
- [ ] The test harness passes `keyboardCaptured:` by name (PR 1 put `fonts` before it on `main`)
- [ ] `dotnet build AcDream.slnx -c Release` on `main` → `0 Warning(s)`, `0 Error(s)`
- [ ] The portable suite on `main` fails only baseline tests; the 5 `Lane=Vulkan` `PluginCanvas*` tests pass
- [ ] `origin/main` equals local `main`; `origin/painter-v2/keyboard` is unchanged; the planning worktrees are removed

**Verify:** `git fetch origin && test "$(git rev-parse main)" = "$(git rev-parse origin/main)" && git log -1 --format=%P main | wc -w` → `2`

**Steps:**

- [ ] **Step 1: Merge**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree add .worktrees/main-merge main 2>/dev/null || git worktree add -B main .worktrees/main-merge origin/main
cd .worktrees/main-merge
git pull --ff-only origin main
git rev-parse --short HEAD   # expect 301852fc; if fork main moved, the resolutions below may need adjusting: show the conflicts to the user
git merge --no-ff painter-v2/keyboard -m "Merge painter-v2/keyboard: plugin canvas keyboard input (PR 6b)

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
git diff --name-only --diff-filter=U
```

Expected: the merge stops with the seven files listed above, one to four hunks each. Every hunk is "keep both" except where stated.

- [ ] **Step 2: Resolve**

1. `docs/plugin-api.md`, the Canvases intro: keep `main`'s layers wording and this branch's link:

````markdown
A canvas is a rectangle the plugin paints, shown over the world and either
under every window or over them (see [Layers and order](#layers-and-order)
below), taking no input unless it asks for it (see
[Pointer input](#pointer-input) and [Keyboard input](#keyboard-input)
below). It is positioned by an anchor plus
````

2. `src/AcDream.App/Plugins/PluginCanvasRegistration.cs`: keep `main`'s `ZOrder` property, then this branch's `KeyHandler`, `HasKeyboardFocus`, `RequestKeyboardFocus` and `ReleaseKeyboardFocus`.
3. `src/AcDream.Core/Plugins/ScopedPluginHost.cs`: keep `main`'s forwarded `ZOrder`, then this branch's four forwarded members.
4. `src/AcDream.Plugin.Abstractions/PluginCanvas.cs`, four hunks: (a) descriptor: `main`'s `Layer` and `ZOrder`, then a new `/// <summary>` line and this branch's `AcceptsKeyboardInput` doc and property; (b) the `IPluginCanvas` class doc:

```csharp
/// A rectangle the plugin paints, shown over the world and under every
/// window (or, in <see cref="PluginCanvasLayer.AboveWindows"/>, over every
/// window), taking no input unless it opted in through
/// <see cref="PluginCanvasDescriptor.AcceptsPointerInput"/> or
/// <see cref="PluginCanvasDescriptor.AcceptsKeyboardInput"/>. Painting is retained: the host keeps what was
```

(c) `IPluginCanvas`: `main`'s `ZOrder` member, then a new `/// <summary>` line and this branch's `KeyHandler` doc and the other three members; (d) `NoOpPluginCanvas`: `main`'s `ZOrder`, a blank line, then this branch's four members. In (a) and (c) the conflict starts after the shared `/// <summary>` line, so the second block needs its own.
5. `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, four hunks: (a) fields: `main`'s shape and scale fields, then `_focused`, `_repeatKey`, `_repeatTimer`; (b) constructor parameters: `Func<PluginFonts?>? fonts = null,` then `Func<bool>? keyboardCaptured = null)`; (c) constructor body: `_shapeProblems = ReportShapeProblem;` then `_keyboardCaptured = …;`; (d) `ReleaseTargets`, which PR 4 split into `ReleaseTargets` + `FollowPixelScale` + `GiveBackTargets`: the keyboard-hook clearing goes in `ReleaseTargets`, before `GiveBackTargets()`:

```csharp
        if (ReferenceEquals(_registration.PointerRelease, _pointerRelease))
            _registration.PointerRelease = null;
        // Focus needs nothing here: the element left the tree first, and
        // the root took focus back from it on the way out.
        if (ReferenceEquals(_registration.KeyboardFocus, this))
            _registration.KeyboardFocus = null;
        GiveBackTargets();
    }
```

followed by `main`'s `FollowPixelScale` and `GiveBackTargets` unchanged.
6. `src/AcDream.App/UI/RetailUiRuntime.cs`, the mount (PR 5 adds canvases to a per-layer stack; this branch's `layer.AddChild` line is dropped):

```csharp
                    modifiers: HeldPointerModifiers,
                    fonts: () => plugins.FindFonts(owner),
                    keyboardCaptured: () => _bindings.Keyboard?.Dispatcher?.IsCapturing == true);
                stack.Add(element);
```

7. `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryCanvasTests.cs`, two hunks: (a) `FakeCanvas`: `main`'s `ZOrder`, then this branch's `KeyHandler` and `HasKeyboardFocus`; (b) both tests: `main`'s `TheZOrderReachesTheHostsCanvasBothWays` body, closed with `scoped.Dispose();`, `}`, a blank line and `[Fact]`, then this branch's `EveryKeyboardCallOnThePluginsHandleReachesTheHostsCanvas`, whose closing `scoped.Dispose();` and `}` follow the conflict.

```bash
git diff --check
grep -rn '^<<<<<<<\|^>>>>>>>' src tests docs || echo "no markers"
git add docs/plugin-api.md src/AcDream.App/Plugins/PluginCanvasRegistration.cs src/AcDream.App/UI/Layout/PluginCanvasElement.cs src/AcDream.App/UI/RetailUiRuntime.cs src/AcDream.Core/Plugins/ScopedPluginHost.cs src/AcDream.Plugin.Abstractions/PluginCanvas.cs tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryCanvasTests.cs
```

Expected: nothing from `git diff --check`, then `no markers`.

- [ ] **Step 3: Build, test, commit the merge, push**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test AcDream.slnx -c Release --no-build --filter "Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure" 2>&1 \
  | grep -E "^\s+Failed " | sed -E 's/ \[[^]]*\]$//' | sort -u > /tmp/openac-keyboard/merge-failures.txt
comm -23 /tmp/openac-keyboard/merge-failures.txt /tmp/openac-keyboard/baseline-failures.txt | grep -v "HostParity.Tests.Peer" || echo "no new failures"
DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "Lane=Vulkan&FullyQualifiedName~PluginCanvas"
git checkout -- '*.lock.json'
git commit --no-edit
git push origin main
git fetch origin && test "$(git rev-parse main)" = "$(git rev-parse origin/main)" && git log -1 --format=%P main | wc -w
```

Expected: `0 Warning(s)`, `0 Error(s)`; `no new failures` (the baseline was recorded on `f951ce16`; a failure that is new here but also fails on `origin/main` before the merge is not this PR's, so check it there before going on); `Passed!  - Failed:     0, Passed:     5`; `2`.

The build will not compile until the merged `PluginCanvasKeyboardTests` harness passes `keyboardCaptured:` by name. It already does on this branch (`() => Modifiers, keyboardCaptured: () => KeyboardCaptured);`), so nothing changes here; if a hand-resolved file shows `Cannot implicitly convert type 'bool' to 'AcDream.App.Plugins.PluginFonts'`, that argument lost its name.

- [ ] **Step 4: Remove the worktrees**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree remove .worktrees/main-merge
git worktree remove --force .worktrees/keyboard-proto
git worktree remove --force .worktrees/keyboard-split
git worktree remove --force .worktrees/keyboard-merge
git worktree list
```

Expected: `.worktrees/keyboard` (the branch) remains, with `hidpi` and `layers` from earlier PRs; the planning and merge worktrees are gone.

```json:metadata
{"files": [], "verifyCommand": "git fetch origin && test \"$(git rev-parse main)\" = \"$(git rev-parse origin/main)\" && git log -1 --format=%P main | wc -w", "acceptanceCriteria": ["merge commit with two parents", "no conflict markers", "keyboardCaptured passed by name", "0 warnings on main", "no portable failures beyond baseline; 5 Vulkan canvas tests pass", "origin/main pushed, feature branch unchanged, planning worktrees removed"], "modelTier": "standard"}
```
