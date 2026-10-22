# Markup controls, plan 13: dialogs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plugins can ask the player a question with `IUiRegistry.Confirm(PluginDialog, Action<int>)` and show one of their own windows modal over another with `IUiRegistry.ShowModal(viewName, ownerView)`. Both are window-modal: only the owner window is dimmed and blocked. Every other window, chat, movement and game hotkeys keep working.

**Architecture:**
- **Contract** (`AcDream.Plugin.Abstractions`, additive):
  - a `PluginDialog` record;
  - two default members on `IUiRegistry`;
  - two owner-scoped members on `IScopedUiRegistry`. The scoped `Confirm` returns an `IDisposable?` handle that cancels the dialog.
  - Core's `ScopedUiRegistry` tracks that handle like any registration, so unloading a plugin answers its open dialogs with their last button.
- **Window blocking** (`UiRoot.BlockWindow`):
  - A top-level window can be blocked by a "blocker" element until a handle is disposed.
  - A press on a blocked window is swallowed (its release too). It flashes the blocker, brings it to the front and focuses it if it accepts focus.
  - Scrolls on a blocked window are swallowed, and it can't take keyboard focus.
  - `UiElement` gains `ModalBlockCount` (draws a dim over the window) and `ModalFlashSeconds` (a gold blinking outline).
- **Dialog widgets:**
  - `UiPluginDialog` (abstract) handles Enter → first button, Escape → last button, and press → take focus.
  - `UiRetailPluginDialog` reuses the retail confirmation layout (0x15) art. The title is a gold first line. It has 1–4 retail buttons; extra buttons come from more copies of the layout.
  - `UiDrawnPluginDialog` draws a title bar with ×, the message and a button row. It uses the owner's plugin palette, or Classic context-menu colours as a fallback when there is no retail art.
- **`PluginDialogHost`** (App, UI thread, ticked each frame):
  - queues confirms per owner window and shows the head of each queue;
  - picks the look;
  - blocks the owner and takes then restores focus;
  - cancels a dialog when its owner closes and on dispose;
  - keeps `ShowModal` blocks until the modal panel hides or is removed.
- **Wiring.** `BufferedUiRegistry` validates the dialog, resolves view names to mounted window elements and forwards to the bound host. `RetailUiRuntime` creates the host, binds it, ticks it and disposes it.

**Status:** DONE 2026-10-22 — feat ce1bea87..ac92737c (13 commits: 6 tasks, 3 task-review fixes, 4 final-review fixes; each task commit builds; gate = baseline 162, 0 warnings). Rulings: per-button swallowed releases + drops refused on blocked windows (T2); retail button bar widened with the box (T3); Tick shows a stalled queued dialog (T4). Final review: a focused dialog stopped movement/game/plugin hotkeys (any UI focus makes InputDispatcher ignore keys) — fixed by key-selective capture (UiElement.HoldsKeyboard/ClaimsKey, additive ClaimsKey on IMouseSource/IHotkeyFocusSource; a dialog claims only Enter/KeypadEnter/Escape); also focus return after a mouse answer, held answer key suppressed, Show-failure guard, mutual modals refused, docs corrected. Parked: dialog widgets reuse context-menu/hotkey colour constants (cross-series); Ctrl/Alt+Enter/Escape double-fire; auto-repeat Enter after the last dialog may open chat; press-off-button leaves focus null; hover reaches blocked windows.

**Tech Stack:** C# / .NET 10, xUnit, headless `UiRoot` + `RecordingGpuDevice` (`MarkupDrawProbe`) tests, retail layout fixtures (`FixtureLoader.LoadConfirmationDialog`), Silk.NET `Key`.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` (branch `docs/markup-controls-spec`), "Delivery" (contract), "Shared rules" and section 14.

## Global Constraints

- **Workspace.** Worktree `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls` (HEAD ce1bea87, from upstream 391e709a).
  - Never merge fork `main` in, and never add `docs/superpowers/` or `.superpowers/` files.
  - Push only when the controller says so.
- **Build hygiene.** Every shell runs `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` first. After building, run `git checkout -- '*.lock.json'`; never commit lock files.
- **Commits.** Every commit must build and pass its own tests. Commit messages end with `Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC`.
- **Contract (additive only).**
  - `PluginDialog(string Title, string Message, IReadOnlyList<string>? Buttons = null, string? OwnerView = null)` has `static DefaultButtons = ["OK", "Cancel"]` and `EffectiveButtons => Buttons ?? DefaultButtons`.
  - `IUiRegistry`: `bool Confirm(PluginDialog dialog, Action<int> onResult) => false;` and `bool ShowModal(string viewName, string? ownerView = null) => false;`.
  - `IScopedUiRegistry`: `IDisposable? Confirm(PluginUiOwner owner, PluginDialog dialog, Action<int> onResult) => null;` and `bool ShowModal(PluginUiOwner owner, string viewName, string? ownerView = null) => false;`.
  - Every public member has XML docs (the contract project fails the build otherwise). Never touch `<Version>` (upstream's call; the fork merge bumps it).
- **Dialog rules.**
  - **Buttons.** One to four, none blank. A host with a window throws `ArgumentException` otherwise (before it checks for a bound dialog host). `Buttons == null` means OK, Cancel.
  - **Results.** `onResult` is called exactly once per dialog that was accepted (`Confirm` returned true / a non-null handle), and never for one that wasn't.
    - Enter or KeypadEnter → index 0.
    - Escape, the × (drawn look), the owner window closing (hidden or removed), the handle's `Dispose` (plugin unload) and the dialog host's `Dispose` → the last index.
    - The callback runs last, after the dialog is removed, the owner unblocked, focus restored and the next queued dialog shown. An exception from it is caught and logged (`[UI] plugin dialog answer handler failed: …`).
  - **Queueing.** One dialog at a time per owner window; dialogs with no owner share one queue. Dialogs for different owners show at once. When an owner closes, every dialog queued on it is answered with its last index, in request order.
  - **Owner.** `OwnerView` names one of the calling plugin's own *mounted* windows by id or title. Null, unknown, unmounted, another plugin's, or hidden at the time of the call → no owner: the dialog is centred on the screen and blocks nothing.
  - **Placement.** Centred on the owner's rectangle (or the screen), rounded, clamped on screen. Re-placed every host tick, so it follows a moved owner. It is not a registered window: not saved, no shelf button, not draggable.
  - **Focus.**
    - The dialog takes keyboard focus when it opens and remembers what had focus before; blocking the owner also clears focus inside the owner.
    - On close, if the dialog still had focus, focus goes back to what had it, if that can take focus now.
    - Only Enter, KeypadEnter and Escape are handled; every other key falls through (to the game).
    - A press anywhere on the dialog gives it focus.
- **Window blocking (`UiRoot`).**
  - Only a direct child of the root can be blocked (`ArgumentException` otherwise). Several blockers stack; a blocked press flashes the newest.
  - A blocked press (any button) is swallowed and so is its release, so neither reaches the world. It sets the blocker's `ModalFlashSeconds = 0.6`, brings the blocker's top-level window to the front, and gives the blocker keyboard focus if it `AcceptsFocus` and `CanFocus`.
  - Scrolls over a blocked window are swallowed. `SetKeyboardFocus` into a blocked window is ignored.
  - Blocking dismisses the root's active popup if it is inside the window. It also releases pointer capture and clears keyboard focus if either is inside the window.
  - Blocked windows draw a `(0, 0, 0, 0.45)` fill over themselves after their children.
  - The flash draws a 2 px outline in `(1, 0.82, 0.25, 1)` while `((int)(ModalFlashSeconds * 10f) & 1) == 0` and seconds > 0. It decays by `dt` in the element's tick.
- **Looks.**
  - **Themed:** the owner is a `UiPluginMarkupPanel` whose palette is not null. It gets a `UiDrawnPluginDialog` with that panel's live palette and its `ModernFont` (or the default font).
  - **Classic:** otherwise, a `UiRetailPluginDialog` built from a fresh confirmation layout. If the layout is missing, lacks parts, or throws, it gets a `UiDrawnPluginDialog` with a null palette (Classic colours).
- **`ShowModal`.**
  - Shows the panel through the same seam as `ShowPanel` and returns false when that fails.
  - Blocks the owner (if any, mounted, and not the panel itself) with the panel as the blocker, until the panel is hidden or removed, or the owner is removed.
  - Showing the same panel modal again over the same owner doesn't stack; over another owner it moves the block.

**User decisions (already made):**
- One branch, one commit series per control, upstream-portable.
- "Dialogs are WINDOW-modal only": the user rejected `UiRoot.Modal` because it blocks the screen and game hotkeys. API is `IUiRegistry.Confirm` + `ShowModal` (spec section 14, memory).
- The contract changes additively. The upstream version number is Erik's call; the fork merge bumps `0.1.26-fork.n`.

**Plan choices (details the spec leaves open; flag in the final review if wrong):**
- **Buttons.** At most 4 buttons; a blank caption throws.
- **Classic retail box.**
  - The title is shown as a gold first line of the message (the retail box has no caption) and it has no × (the retail art has none). Escape is the keyboard cancel.
  - Two buttons sit exactly where retail Yes/No do. One is centred. Three or four are centred 16 px apart, and the box widens to `n×80 + (n−1)×16 + 48` when that is wider than 400.
  - Retail buttons are a fixed 80 px; long captions are not fitted.
- **Owners.** A hidden owner at the time of the call counts as no owner, instead of being cancelled at once. An owner that closes cancels its queued dialogs too, not only the one showing.
- **Blocked presses.** A blocked press swallows its release too, using a root flag, scoped to blocked presses only. The popup release swallow stays a follow-up. The flash is a 0.6 s gold blink plus bring-to-front plus refocusing the dialog.
- **Threading and errors.** Requests are expected on the tick thread, as `ShowPanel` is today; there is no cross-thread queue. Callback exceptions are caught and logged so one plugin can't stall the queue.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.Plugin.Abstractions/PluginDialog.cs` (new) | Dialog record |
| `src/AcDream.Plugin.Abstractions/IUiRegistry.cs` | `Confirm`, `ShowModal` (plain + scoped) |
| `src/AcDream.Core/Plugins/ScopedPluginHost.cs` | Scoped forwarding + handle tracking |
| `src/AcDream.App/UI/UiElement.cs` | `ModalBlockCount`, `ModalFlashSeconds`, dim + flash draw, flash decay |
| `src/AcDream.App/UI/UiRoot.cs` | `BlockWindow`, `CanFocus`, blocked input |
| `src/AcDream.App/UI/UiPluginDialog.cs` (new) | Base: keys, focus, placement |
| `src/AcDream.App/UI/UiDrawnPluginDialog.cs` (new) | Primitive look (themed / Classic fallback) |
| `src/AcDream.App/UI/UiRetailPluginDialog.cs` (new) | Retail 0x15 art look |
| `src/AcDream.App/UI/PluginDialogHost.cs` (new) | Queues, looks, blocking, focus, cancels, modals |
| `src/AcDream.App/UI/PluginUiThemeSettings.cs` | `UiPluginMarkupPanel.Palette` |
| `src/AcDream.App/Plugins/BufferedUiRegistry.cs` | Validation, view resolution, host binding |
| `src/AcDream.App/UI/RetailUiRuntime.cs` | Host creation, layout func, tick, dispose |
| `tests/AcDream.Plugin.Tests/PluginDialogContractTests.cs` (new) | Defaults |
| `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryDialogTests.cs` (new) | Scoped forwarding, unload |
| `tests/AcDream.App.Tests/UI/UiRootWindowBlockTests.cs` (new) | Blocking |
| `tests/AcDream.App.Tests/UI/UiPluginDialogTests.cs` (new) | Both looks |
| `tests/AcDream.App.Tests/UI/PluginDialogHostTests.cs` (new) | Host |
| `tests/AcDream.App.Tests/Plugins/BufferedUiRegistryDialogTests.cs` (new) | Registry |
| `docs/plugin-api.md` | Dialogs section |

---

### Task 1: Dialog contract and scoped forwarding

**Goal:** `PluginDialog`, the `Confirm`/`ShowModal` members on `IUiRegistry` and `IScopedUiRegistry`, and Core's `ScopedUiRegistry` forwarding them with the plugin's owner and tracking open dialogs, exist with tests.

**Files:**
- Create: `src/AcDream.Plugin.Abstractions/PluginDialog.cs`
- Modify: `src/AcDream.Plugin.Abstractions/IUiRegistry.cs`
- Modify: `src/AcDream.Core/Plugins/ScopedPluginHost.cs` (class `ScopedUiRegistry`, ~line 1979)
- Test: `tests/AcDream.Plugin.Tests/PluginDialogContractTests.cs`, `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryDialogTests.cs`

**Acceptance Criteria:**
- [ ] A registry that implements only the pre-dialog members answers `Confirm` false, `ShowModal` false, and the scoped `Confirm` null; it never calls back. `NoOpUiRegistry` does the same.
- [ ] `new PluginDialog("T","M").EffectiveButtons` is `["OK","Cancel"]`; with `["Yes"]` it is `["Yes"]`.
- [ ] A plugin's `Confirm` reaches the host's scoped `Confirm` with the plugin's own owner and the same dialog object, and returns true. A null host handle → false.
- [ ] A host answer reaches the plugin once, and releases the tracked handle (disposed exactly once overall, including a later unload).
- [ ] Unloading (scope `Dispose`) with a dialog open disposes the handle once and the plugin gets the last index.
- [ ] `ShowModal(view, ownerView)` reaches the host as `(owner.Id, view, ownerView)`.
- [ ] `Confirm` after unload throws `ObjectDisposedException` and reaches no host.
- [ ] `dotnet build src/AcDream.Plugin.Abstractions -c Release` has 0 warnings (XML docs complete).

**Verify:** `dotnet test tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj -c Release --filter "FullyQualifiedName~PluginDialogContractTests"` and `dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj -c Release --filter "FullyQualifiedName~ScopedUiRegistryDialogTests"` → all pass.

**Steps:**

- [ ] **Step 0: Core baseline (before any change)**

The series gate compares Core plugin-test failures against a baseline taken now:

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
T=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC
norm() { grep -E '^[[:space:]]+Failed [A-Za-z]' | sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' | sort -u; }
dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj -c Release --filter "FullyQualifiedName~AcDream.Core.Tests.Plugins" 2>&1 | norm > $T/mc13-core-base.txt
wc -l $T/mc13-core-base.txt; git checkout -- '*.lock.json' 2>/dev/null
```

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.Plugin.Tests/PluginDialogContractTests.cs`:

```csharp
// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;

namespace AcDream.Plugin.Tests;

/// <summary>
/// The dialog members are additive: a registry written before them still
/// compiles and answers false without calling back, and a dialog without
/// its own buttons has OK and Cancel.
/// </summary>
public sealed class PluginDialogContractTests
{
    private sealed class BareRegistry : IScopedUiRegistry
    {
        public void AddMarkupPanel(string markupPath, object binding)
        {
        }

        public IDisposable RegisterMarkupPanel(string markupPath, object binding) =>
            NoOpUiRegistration.Instance;
    }

    [Fact]
    public void ARegistryWrittenBeforeDialogsAnswersFalse()
    {
        IScopedUiRegistry registry = new BareRegistry();
        var owner = new PluginUiOwner("acdream.test", "Test");
        bool called = false;

        Assert.False(((IUiRegistry)registry).Confirm(new PluginDialog("T", "M"), _ => called = true));
        Assert.False(((IUiRegistry)registry).ShowModal("options", "main"));
        Assert.Null(registry.Confirm(owner, new PluginDialog("T", "M"), _ => called = true));
        Assert.False(registry.ShowModal(owner, "options", "main"));
        Assert.False(called);
    }

    [Fact]
    public void TheHostThatDrawsNothingAnswersFalse()
    {
        IScopedUiRegistry registry = NoOpUiRegistry.Instance;
        bool called = false;

        Assert.False(((IUiRegistry)registry).Confirm(new PluginDialog("T", "M"), _ => called = true));
        Assert.Null(registry.Confirm(new PluginUiOwner("p", "P"), new PluginDialog("T", "M"), _ => called = true));
        Assert.False(((IUiRegistry)registry).ShowModal("options"));
        Assert.False(called);
    }

    [Fact]
    public void ADialogWithoutButtonsHasOkAndCancel()
    {
        Assert.Equal(["OK", "Cancel"], new PluginDialog("T", "M").EffectiveButtons);
        Assert.Same(PluginDialog.DefaultButtons, new PluginDialog("T", "M").EffectiveButtons);
        Assert.Equal(["Yes"], new PluginDialog("T", "M", ["Yes"]).EffectiveButtons);
    }
}
```

`tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryDialogTests.cs`:

```csharp
using AcDream.Core.Plugins;
using AcDream.Core.Selection;
using AcDream.Plugin.Abstractions;

namespace AcDream.Core.Tests.Plugins;

/// <summary>
/// A plugin's dialogs reach the host under its own name, report one answer
/// each, and are answered with their last button when the plugin unloads.
/// </summary>
public sealed class ScopedUiRegistryDialogTests
{
    [Fact]
    public void ConfirmReachesTheHostUnderThePluginsOwnName()
    {
        var ui = new RecordingUiRegistry();
        using var scope = new ScopedPluginHost(new StubHost(ui), "acdream.test", "Test");
        var dialog = new PluginDialog("Delete", "Delete the profile?", OwnerView: "main");

        Assert.True(scope.Ui.Confirm(dialog, _ => { }));

        RecordingUiRegistry.Request request = Assert.Single(ui.Requests);
        Assert.Equal("acdream.test", request.Owner.Id);
        Assert.Same(dialog, request.Dialog);
    }

    [Fact]
    public void AHostWithoutDialogsAnswersFalseAndNeverCallsBack()
    {
        using var scope = new ScopedPluginHost(
            new StubHost(NoOpUiRegistry.Instance), "acdream.test", "Test");
        bool called = false;

        Assert.False(scope.Ui.Confirm(new PluginDialog("T", "M"), _ => called = true));
        Assert.False(scope.Ui.ShowModal("options", "main"));
        Assert.False(called);
    }

    [Fact]
    public void TheAnswerReachesThePluginOnceAndLetsGoOfTheDialog()
    {
        var ui = new RecordingUiRegistry();
        var scope = new ScopedPluginHost(new StubHost(ui), "acdream.test", "Test");
        var answers = new List<int>();
        scope.Ui.Confirm(new PluginDialog("T", "M"), answers.Add);
        RecordingUiRegistry.Request request = Assert.Single(ui.Requests);

        request.Answer(0);
        scope.Dispose();

        Assert.Equal([0], answers);
        Assert.Equal(1, request.Handle.DisposeCount);
    }

    [Fact]
    public void UnloadingAnswersAnOpenDialogWithItsLastButton()
    {
        var ui = new RecordingUiRegistry();
        var scope = new ScopedPluginHost(new StubHost(ui), "acdream.test", "Test");
        var answers = new List<int>();
        scope.Ui.Confirm(new PluginDialog("T", "M", ["Yes", "No", "Later"]), answers.Add);

        scope.Dispose();

        Assert.Equal([2], answers);
        Assert.Equal(1, Assert.Single(ui.Requests).Handle.DisposeCount);
    }

    [Fact]
    public void ShowModalReachesTheHostUnderThePluginsOwnName()
    {
        var ui = new RecordingUiRegistry();
        using var scope = new ScopedPluginHost(new StubHost(ui), "acdream.test", "Test");

        Assert.True(scope.Ui.ShowModal("options", "main"));
        Assert.True(scope.Ui.ShowModal("about"));

        Assert.Equal(
            [("acdream.test", "options", (string?)"main"), ("acdream.test", "about", null)],
            ui.Modals);
    }

    [Fact]
    public void AnUnloadedPluginCannotOpenADialog()
    {
        var ui = new RecordingUiRegistry();
        var scope = new ScopedPluginHost(new StubHost(ui), "acdream.test", "Test");
        IUiRegistry registry = scope.Ui;
        scope.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => registry.Confirm(new PluginDialog("T", "M"), _ => { }));
        Assert.Empty(ui.Requests);
    }

    private sealed class RecordingUiRegistry : IScopedUiRegistry
    {
        public sealed class Handle(Action cancel) : IDisposable
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
                cancel();
            }
        }

        public sealed class Request
        {
            public required PluginUiOwner Owner { get; init; }
            public required PluginDialog Dialog { get; init; }
            public required Action<int> OnResult { get; init; }
            public Handle Handle { get; set; } = null!;
            private bool _done;

            // A host answers once, whether by a button or by a cancel.
            public void Answer(int index)
            {
                if (_done)
                    return;
                _done = true;
                OnResult(index);
            }
        }

        public List<Request> Requests { get; } = [];
        public List<(string Owner, string View, string? OwnerView)> Modals { get; } = [];

        public void AddMarkupPanel(string markupPath, object binding)
        {
        }

        public IDisposable RegisterMarkupPanel(string markupPath, object binding) =>
            NoOpUiRegistration.Instance;

        public IDisposable? Confirm(PluginUiOwner owner, PluginDialog dialog, Action<int> onResult)
        {
            var request = new Request { Owner = owner, Dialog = dialog, OnResult = onResult };
            request.Handle = new Handle(() => request.Answer(dialog.EffectiveButtons.Count - 1));
            Requests.Add(request);
            return request.Handle;
        }

        public bool ShowModal(PluginUiOwner owner, string viewName, string? ownerView = null)
        {
            Modals.Add((owner.Id, viewName, ownerView));
            return true;
        }
    }

    private sealed class StubHost(IUiRegistry ui) : IPluginHost
    {
        public bool HasUi => true;
        public IPluginLogger Log { get; } = new SilentLogger();
        public IGameState State { get; } = new EmptyGameState();
        public IEvents Events { get; } = new WorldEvents();
        public ISelectionService Selection { get; } = new SelectionState();
        public IUiRegistry Ui { get; } = ui;
        public IAutomationSurface Automation { get; } = NoOpAutomationSurface.Instance;

        private sealed class SilentLogger : IPluginLogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message, Exception? error = null) { }
        }

        private sealed class EmptyGameState : IGameState
        {
            public IReadOnlyList<WorldEntitySnapshot> Entities { get; } = [];
        }
    }
}
```

(`StubHost` is a copy of the private one in `ScopedUiRegistryPanelControlTests.cs`; it has to be repeated because it is private there.)

- [ ] **Step 2: Run them to see them fail**

Run both Verify commands. Expected: compile errors (`PluginDialog`, `Confirm`, `ShowModal` not found).

- [ ] **Step 3: Add the record**

`src/AcDream.Plugin.Abstractions/PluginDialog.cs`:

```csharp
namespace AcDream.Plugin.Abstractions;

/// <summary>
/// A question a plugin asks the player in a dialog box, opened with
/// <see cref="IUiRegistry.Confirm"/>.
/// </summary>
/// <param name="Title">The dialog's caption. May be empty.</param>
/// <param name="Message">The question or message; long text wraps. May be empty.</param>
/// <param name="Buttons">
/// The button captions, left to right: one to four of them, none blank. The
/// last one is the cancel button: Escape, the close button, the owner window
/// closing and the plugin unloading all answer with its index. Null means
/// <see cref="DefaultButtons"/>.
/// </param>
/// <param name="OwnerView">
/// The id or title of one of the plugin's own windows. The dialog is centred
/// on it and blocks it until it is answered. Null, an unknown window or a
/// hidden one centres the dialog on the screen and blocks nothing.
/// </param>
public sealed record PluginDialog(
    string Title,
    string Message,
    IReadOnlyList<string>? Buttons = null,
    string? OwnerView = null)
{
    /// <summary>The buttons of a dialog that names none: OK and Cancel.</summary>
    public static IReadOnlyList<string> DefaultButtons { get; } = ["OK", "Cancel"];

    /// <summary>The buttons the dialog shows: <see cref="Buttons"/>, or <see cref="DefaultButtons"/> when that is null.</summary>
    public IReadOnlyList<string> EffectiveButtons => Buttons ?? DefaultButtons;
}
```

- [ ] **Step 4: Add the registry members**

In `src/AcDream.Plugin.Abstractions/IUiRegistry.cs`, in `IUiRegistry`, directly after `FocusControl(string viewName, string controlName)`:

```csharp
    /// <summary>
    /// Asks the player a question in a dialog box with one to four buttons
    /// and reports which one they picked. The dialog is window-modal: it
    /// blocks only <see cref="PluginDialog.OwnerView"/>, which draws dimmed
    /// and ignores presses until the dialog is answered, while every other
    /// window, chat and the game work as usual. Several dialogs for the same
    /// owner queue and show one at a time. Call it from the thread that calls
    /// the plugin's tick.
    /// </summary>
    /// <param name="dialog">The dialog's title, message, buttons and owner window.</param>
    /// <param name="onResult">
    /// Called exactly once, on the thread that calls the plugin's tick, with
    /// the index of the button picked. Enter picks the first button. Escape,
    /// the close button, the owner window closing and the plugin unloading
    /// report the last one.
    /// </param>
    /// <returns>
    /// True when the dialog was queued. False on a host that draws nothing --
    /// which is what the default implementation does -- and then
    /// <paramref name="onResult"/> is never called.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// On a host that draws: the dialog has no buttons, more than four, or a
    /// blank one.
    /// </exception>
    bool Confirm(PluginDialog dialog, Action<int> onResult) => false;

    /// <summary>
    /// Shows one of this plugin's own windows, as <see cref="ShowPanel"/>
    /// does, and makes it modal over another of its windows: the owner draws
    /// dimmed and ignores presses until the shown window is hidden, by
    /// <see cref="HidePanel"/> or its close button, or removed. Only the
    /// owner is blocked; every other window and the game work as usual.
    /// </summary>
    /// <param name="viewName">The id or title of the window to show.</param>
    /// <param name="ownerView">
    /// The id or title of the window it blocks. Null, an unknown window, or
    /// the shown window itself blocks nothing.
    /// </param>
    /// <returns>
    /// False when this plugin has no such window to show, before the window
    /// has been put on screen, and on a host that draws nothing -- which is
    /// what the default implementation does.
    /// </returns>
    bool ShowModal(string viewName, string? ownerView = null) => false;
```

In `IScopedUiRegistry`, directly after `FocusControl(PluginUiOwner owner, string viewName, string controlName)`:

```csharp
    /// <summary>
    /// Queues one plugin's dialog; see <see cref="IUiRegistry.Confirm"/>.
    /// Disposing the returned handle answers the dialog with its last button
    /// if it is still open or queued, which is how a plugin's dialogs end
    /// when it unloads; once the dialog is answered it does nothing.
    /// </summary>
    /// <param name="owner">The plugin the dialog belongs to; its windows are the ones <see cref="PluginDialog.OwnerView"/> names.</param>
    /// <param name="dialog">The dialog's title, message, buttons and owner window.</param>
    /// <param name="onResult">Called exactly once with the index of the button picked.</param>
    /// <returns>The dialog's handle, or null on a host that draws nothing, which never calls back.</returns>
    IDisposable? Confirm(PluginUiOwner owner, PluginDialog dialog, Action<int> onResult) => null;

    /// <summary>
    /// Shows one plugin's window modal over another of its windows; see
    /// <see cref="IUiRegistry.ShowModal"/>. False on a host that draws nothing.
    /// </summary>
    bool ShowModal(PluginUiOwner owner, string viewName, string? ownerView = null) => false;
```

- [ ] **Step 5: Forward them from the plugin's scope**

In `src/AcDream.Core/Plugins/ScopedPluginHost.cs`, class `ScopedUiRegistry`, after `SetControlVisible(...)` (before the client-window comment):

```csharp
        public bool ShowModal(string viewName, string? ownerView = null) =>
            _inner.ShowModal(_owner, viewName, ownerView);

        // An open dialog's handle is tracked like any other registration, so
        // an unloading plugin's dialogs are answered with their last button;
        // the answer lets go of it again.
        public bool Confirm(PluginDialog dialog, Action<int> onResult)
        {
            ArgumentNullException.ThrowIfNull(dialog);
            ArgumentNullException.ThrowIfNull(onResult);
            lock (_gate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ScopedUiRegistry));
            }

            IDisposable? registration = null;
            bool answered = false;
            IDisposable? handle = _inner.Confirm(_owner, dialog, index =>
            {
                IDisposable? release;
                lock (_gate)
                {
                    answered = true;
                    release = registration;
                }
                release?.Dispose();
                onResult(index);
            });
            if (handle is null)
                return false;

            IDisposable tracked = TrackRegistration(handle);
            bool early;
            lock (_gate)
            {
                registration = tracked;
                early = answered;
            }
            if (early)
                tracked.Dispose();
            return true;
        }
```

- [ ] **Step 6: Run the tests**

Run both Verify commands. Expected: all pass. Also `dotnet build src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj -c Release 2>&1 | grep -E "warning|error|Warn"` → `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.Plugin.Abstractions/PluginDialog.cs src/AcDream.Plugin.Abstractions/IUiRegistry.cs src/AcDream.Core/Plugins/ScopedPluginHost.cs tests/AcDream.Plugin.Tests/PluginDialogContractTests.cs tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryDialogTests.cs
git commit -m "feat(plugins): Confirm and ShowModal dialog contract

Additive IUiRegistry/IScopedUiRegistry members and the PluginDialog
record; a plugin's scope tracks open dialogs so unloading answers them
with their last button.

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 2: Window-modal blocking in `UiRoot`

**Goal:** `UiRoot.BlockWindow(window, blocker)` makes a top-level window dimmed and unpressable until the returned handle is disposed, with blocked presses flashing the blocker.

**Files:**
- Modify: `src/AcDream.App/UI/UiElement.cs` (properties near `IsEditControl` ~line 159; `DrawSelfAndChildren` ~line 417; `TickSelfAndChildren` ~line 496)
- Modify: `src/AcDream.App/UI/UiRoot.cs` (`OnMouseDown` ~553, `OnMouseUp` ~671, `OnScroll` ~765, `SetKeyboardFocus` ~899, new members)
- Test: `tests/AcDream.App.Tests/UI/UiRootWindowBlockTests.cs`

**Acceptance Criteria:**
- [ ] A press and release on a control in a blocked window don't click it and don't reach `WorldMouseFallThrough`. The blocker gets `ModalFlashSeconds == 0.6`, the top `ZOrder`, and keyboard focus if it accepts focus. Right-button presses are swallowed the same way.
- [ ] Presses on other windows and on empty space work as before.
- [ ] Scrolls over a blocked window don't reach its controls or the world.
- [ ] `SetKeyboardFocus` into a blocked window is ignored. Blocking clears focus, capture and an active popup inside the window. `CanFocus` is false inside a blocked window.
- [ ] A blocked window draws exactly one `(0,0,0,0.45)` quad over itself; after the handle is disposed, none.
- [ ] The flash outline draws at 0.6 s, is hidden at 0.35 s, and is gone after the time runs out.
- [ ] Two blockers: a press flashes the newest; disposing it falls back to the older one. Disposing twice is harmless. Blocking a non-top-level element throws `ArgumentException`.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~UiRootWindowBlockTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/UiRootWindowBlockTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

/// <summary>
/// A window-modal dialog blocks only its owner window: presses on the owner
/// go nowhere but flash the dialog, the owner draws dimmed, and everything
/// else keeps working.
/// </summary>
public sealed class UiRootWindowBlockTests
{
    private sealed class Recorder : UiElement
    {
        public List<int> Types { get; } = [];

        public override bool OnEvent(in UiEvent e)
        {
            Types.Add(e.Type);
            return true;
        }
    }

    private sealed class Fixture
    {
        public UiRoot Root { get; } = new() { Width = 800f, Height = 600f };
        public UiPanel Owner { get; } = new() { Left = 10f, Top = 10f, Width = 200f, Height = 150f };
        public UiSimpleButton OwnerButton { get; } = new()
        {
            Left = 10f, Top = 10f, Width = 60f, Height = 20f, AcceptsFocus = true, TabStop = true,
        };
        public Recorder OwnerScroller { get; } = new() { Left = 100f, Top = 50f, Width = 50f, Height = 50f };
        public UiPanel Other { get; } = new() { Left = 400f, Top = 10f, Width = 200f, Height = 150f };
        public UiSimpleButton OtherButton { get; } = new() { Left = 10f, Top = 10f, Width = 60f, Height = 20f };
        public UiPanel Dialog { get; } = new() { Left = 50f, Top = 300f, Width = 120f, Height = 60f, AcceptsFocus = true };
        public int OwnerClicks;
        public int OtherClicks;
        public int WorldMouse;
        public int WorldScroll;

        public Fixture()
        {
            Owner.AddChild(OwnerButton);
            Owner.AddChild(OwnerScroller);
            Other.AddChild(OtherButton);
            Root.AddChild(Owner);
            Root.AddChild(Other);
            Root.AddChild(Dialog);
            OwnerButton.Click += () => OwnerClicks++;
            OtherButton.Click += () => OtherClicks++;
            Root.WorldMouseFallThrough += (_, _, _, _) => WorldMouse++;
            Root.WorldScrollFallThrough += _ => WorldScroll++;
        }

        public void Click(int x, int y, UiMouseButton button = UiMouseButton.Left)
        {
            Root.OnMouseDown(button, x, y);
            Root.OnMouseUp(button, x, y);
        }
    }

    // OwnerButton is at (20,20)-(80,40) on screen; OtherButton at (410,20)-(470,40).

    [Fact]
    public void APressOnABlockedWindowIsSwallowedAndFlashesTheDialog()
    {
        var f = new Fixture();
        f.Root.BlockWindow(f.Owner, f.Dialog);

        f.Click(30, 30);

        Assert.Equal(0, f.OwnerClicks);
        Assert.Equal(0, f.WorldMouse);
        Assert.Equal(UiElement.ModalFlashDuration, f.Dialog.ModalFlashSeconds);
        Assert.True(f.Dialog.ZOrder > f.Owner.ZOrder && f.Dialog.ZOrder > f.Other.ZOrder);
        Assert.Same(f.Dialog, f.Root.KeyboardFocus);
    }

    [Fact]
    public void ARightPressOnABlockedWindowIsSwallowedToo()
    {
        var f = new Fixture();
        f.Root.BlockWindow(f.Owner, f.Dialog);

        f.Click(30, 30, UiMouseButton.Right);

        Assert.Equal(0, f.WorldMouse);
        Assert.Equal(UiElement.ModalFlashDuration, f.Dialog.ModalFlashSeconds);
    }

    [Fact]
    public void OtherWindowsAndTheWorldStillTakePresses()
    {
        var f = new Fixture();
        f.Root.BlockWindow(f.Owner, f.Dialog);

        f.Click(420, 30);
        f.Click(700, 500);

        Assert.Equal(1, f.OtherClicks);
        Assert.Equal(2, f.WorldMouse); // the empty-space press and its release
        Assert.Equal(0f, f.Dialog.ModalFlashSeconds);
    }

    [Fact]
    public void AScrollOverABlockedWindowGoesNowhere()
    {
        var f = new Fixture();
        f.Root.OnMouseMove(130, 80);
        f.Root.OnScroll(1);
        Assert.Contains(UiEventType.Scroll, f.OwnerScroller.Types);
        f.OwnerScroller.Types.Clear();

        f.Root.BlockWindow(f.Owner, f.Dialog);
        f.Root.OnScroll(1);

        Assert.DoesNotContain(UiEventType.Scroll, f.OwnerScroller.Types);
        Assert.Equal(0, f.WorldScroll);
    }

    [Fact]
    public void ABlockedWindowCannotTakeTheKeyboardFocus()
    {
        var f = new Fixture();
        f.Root.BlockWindow(f.Owner, f.Dialog);

        f.Root.SetKeyboardFocus(f.OwnerButton);

        Assert.Null(f.Root.KeyboardFocus);
        Assert.False(f.Root.CanFocus(f.OwnerButton));
        Assert.True(f.Root.CanFocus(f.Dialog));
    }

    [Fact]
    public void BlockingTakesFocusCaptureAndAPopupOutOfTheWindow()
    {
        var f = new Fixture();
        f.Root.SetKeyboardFocus(f.OwnerButton);
        f.Root.SetCapture(f.OwnerScroller);
        bool dismissed = false;
        f.Root.SetActivePopup(f.OwnerScroller, () => dismissed = true);

        f.Root.BlockWindow(f.Owner, f.Dialog);

        Assert.Null(f.Root.KeyboardFocus);
        Assert.Null(f.Root.Captured);
        Assert.True(dismissed);
    }

    [Fact]
    public void ABlockedWindowDrawsDimmedUntilTheBlockEnds()
    {
        var f = new Fixture();
        IDisposable block = f.Root.BlockWindow(f.Owner, f.Dialog);

        Assert.Equal(1, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(f.Owner), UiElement.ModalDimColor));

        block.Dispose();
        Assert.Equal(0, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(f.Owner), UiElement.ModalDimColor));
        Assert.Equal(0, f.Owner.ModalBlockCount);
    }

    [Fact]
    public void TheFlashBlinksAndFades()
    {
        var f = new Fixture();
        f.Root.BlockWindow(f.Owner, f.Dialog);
        f.Click(30, 30);

        Assert.True(MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(f.Dialog), UiElement.ModalFlashColor) > 0);

        f.Root.Tick(0.25d, 250L); // 0.35 s left: an odd tenth, hidden
        Assert.Equal(0, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(f.Dialog), UiElement.ModalFlashColor));

        f.Root.Tick(1d, 1250L);
        Assert.Equal(0f, f.Dialog.ModalFlashSeconds);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(f.Dialog), UiElement.ModalFlashColor));
    }

    [Fact]
    public void DisposingTheBlockLetsPressesThroughAgain()
    {
        var f = new Fixture();
        IDisposable block = f.Root.BlockWindow(f.Owner, f.Dialog);

        block.Dispose();
        block.Dispose();
        f.Click(30, 30);

        Assert.Equal(1, f.OwnerClicks);
        Assert.Equal(0, f.Owner.ModalBlockCount);
    }

    [Fact]
    public void TheNewestOfTwoBlockersFlashes()
    {
        var f = new Fixture();
        var second = new UiPanel { Left = 300f, Top = 300f, Width = 50f, Height = 50f };
        f.Root.AddChild(second);
        f.Root.BlockWindow(f.Owner, f.Dialog);
        IDisposable newest = f.Root.BlockWindow(f.Owner, second);
        Assert.Equal(2, f.Owner.ModalBlockCount);

        f.Click(30, 30);
        Assert.Equal(UiElement.ModalFlashDuration, second.ModalFlashSeconds);
        Assert.Equal(0f, f.Dialog.ModalFlashSeconds);

        newest.Dispose();
        f.Click(30, 30);
        Assert.Equal(UiElement.ModalFlashDuration, f.Dialog.ModalFlashSeconds);
        Assert.Equal(1, f.Owner.ModalBlockCount);
    }

    [Fact]
    public void OnlyATopLevelWindowCanBeBlocked()
    {
        var f = new Fixture();

        Assert.Throws<ArgumentException>(() => f.Root.BlockWindow(f.OwnerButton, f.Dialog));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run the Verify command. Expected: compile errors (`BlockWindow`, `ModalFlashDuration`, … not found).

- [ ] **Step 3: Dim and flash on `UiElement`**

In `src/AcDream.App/UI/UiElement.cs`, after the `IsEditControl` property:

```csharp
    /// <summary>
    /// How many window-modal dialogs block this top-level window; while it is
    /// above zero the window draws dimmed. Kept by <see cref="UiRoot.BlockWindow"/>.
    /// </summary>
    internal int ModalBlockCount { get; set; }

    /// <summary>Seconds left of the blink a press on a blocked window sets off on its dialog.</summary>
    internal float ModalFlashSeconds { get; set; }

    internal const float ModalFlashDuration = 0.6f;
    internal static readonly Vector4 ModalDimColor = new(0f, 0f, 0f, 0.45f);
    /// <summary>The gold of the markup focus outline.</summary>
    internal static readonly Vector4 ModalFlashColor = new(1f, 0.82f, 0.25f, 1f);
```

In `DrawSelfAndChildren`, directly after `OnDrawAfterChildren(ctx);`:

```csharp
                if (ModalBlockCount > 0)
                    ctx.DrawFill(0f, 0f, Width, Height, ModalDimColor);
                if (ModalFlashSeconds > 0f && ((int)(ModalFlashSeconds * 10f) & 1) == 0)
                    ctx.DrawRectOutline(0f, 0f, Width, Height, ModalFlashColor, 2f);
```

In `TickSelfAndChildren`, directly after `if (!Visible) return;`:

```csharp
        if (ModalFlashSeconds > 0f)
            ModalFlashSeconds = MathF.Max(0f, ModalFlashSeconds - (float)dt);
```

- [ ] **Step 4: Blocking in `UiRoot`**

In `src/AcDream.App/UI/UiRoot.cs`, add beside `SetActivePopup`/`ClearActivePopup`:

```csharp
    // Window-modal dialogs: each blocked top-level window with the elements
    // blocking it, newest last. A press on a blocked window is swallowed,
    // release and all, and flashes its newest blocker.
    private readonly Dictionary<UiElement, List<UiElement>> _windowBlocks =
        new(ReferenceEqualityComparer.Instance);
    private UiMouseButton? _swallowedRelease;

    /// <summary>
    /// Blocks a top-level window until the returned handle is disposed: it
    /// draws dimmed, presses and scrolls on it are swallowed, and nothing in
    /// it can take the keyboard focus. A blocked press flashes
    /// <paramref name="blocker"/>, brings its window to the front and gives
    /// it the keyboard focus when it accepts focus. Blocking closes a popup
    /// open in the window and takes pointer capture and keyboard focus out
    /// of it.
    /// </summary>
    internal IDisposable BlockWindow(UiElement window, UiElement blocker)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(blocker);
        if (!ReferenceEquals(window.Parent, this))
            throw new ArgumentException("Only a top-level window can be blocked.", nameof(window));
        if (!_windowBlocks.TryGetValue(window, out List<UiElement>? blockers))
            _windowBlocks[window] = blockers = new List<UiElement>();
        blockers.Add(blocker);
        window.ModalBlockCount++;

        if (_activePopup is { } popup && IsWithinSubtree(popup, window))
        {
            var dismiss = _activePopupDismiss;
            _activePopup = null;
            _activePopupDismiss = null;
            dismiss?.Invoke();
        }
        if (Captured is { } captured && IsWithinSubtree(captured, window))
            ReleaseCapture();
        if (KeyboardFocus is { } focused && IsWithinSubtree(focused, window))
            SetKeyboardFocus(null);
        return new WindowBlock(this, window, blocker);
    }

    /// <summary>Whether an element can take the keyboard focus now: shown, enabled, mounted, and not in a blocked window.</summary>
    internal bool CanFocus(UiElement element) =>
        IsAvailableInputTarget(element) && BlockerFor(element) is null;

    private void Unblock(UiElement window, UiElement blocker)
    {
        if (!_windowBlocks.TryGetValue(window, out List<UiElement>? blockers)
            || !blockers.Remove(blocker))
            return;
        window.ModalBlockCount--;
        if (blockers.Count == 0)
            _windowBlocks.Remove(window);
    }

    // The newest element blocking the top-level window an element is in.
    private UiElement? BlockerFor(UiElement element)
    {
        if (_windowBlocks.Count == 0)
            return null;
        UiElement top = TopLevelOf(element);
        return ReferenceEquals(top.Parent, this)
            && _windowBlocks.TryGetValue(top, out List<UiElement>? blockers)
            ? blockers[^1]
            : null;
    }

    private UiElement TopLevelOf(UiElement element)
    {
        UiElement top = element;
        while (top.Parent is not null && !ReferenceEquals(top.Parent, this))
            top = top.Parent;
        return top;
    }

    private void FlashBlocker(UiElement blocker)
    {
        blocker.ModalFlashSeconds = UiElement.ModalFlashDuration;
        UiElement top = TopLevelOf(blocker);
        if (ReferenceEquals(top.Parent, this))
            BringToFront(top);
        if (blocker.AcceptsFocus && CanFocus(blocker))
            SetKeyboardFocus(blocker);
    }

    private sealed class WindowBlock(UiRoot root, UiElement window, UiElement blocker) : IDisposable
    {
        private UiRoot? _root = root;

        public void Dispose() =>
            Interlocked.Exchange(ref _root, null)?.Unblock(window, blocker);
    }
```

In `OnMouseDown`, directly after the `if (target is null) { … return; }` block and before `if (btn == UiMouseButton.Left) SetKeyboardFocus(…)`:

```csharp
        if (BlockerFor(target) is { } blocker)
        {
            // A window-modal dialog owns this window: the press, and its
            // release, go nowhere but flash the dialog.
            _swallowedRelease = btn;
            FlashBlocker(blocker);
            return;
        }
```

In `OnMouseUp`, directly after `UpdateButtonFlag(btn, down: false);`:

```csharp
        if (_swallowedRelease == btn)
        {
            _swallowedRelease = null;
            return;
        }
```

In `OnScroll`, directly after the `if (target is null) { WorldScrollFallThrough?.Invoke(dy); return; }` block:

```csharp
        if (BlockerFor(target) is not null)
            return;
```

At the very start of `SetKeyboardFocus(UiElement? e)`:

```csharp
        if (e is not null && BlockerFor(e) is not null)
            return;
```

- [ ] **Step 5: Run the tests**

Run the Verify command → all pass. Then run the existing UI suite for regressions: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~AcDream.App.Tests.UI"`. Compare its failures with `$T/mc-base.txt` using the gate's `norm` and `comm -13` (see Task 7). Expected: no new failures.

- [ ] **Step 6: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/UiElement.cs src/AcDream.App/UI/UiRoot.cs tests/AcDream.App.Tests/UI/UiRootWindowBlockTests.cs
git commit -m "feat(ui): window-modal blocking in UiRoot

A blocked top-level window draws dimmed; presses on it are swallowed
(release too) and flash the blocking element; scrolls and focus are
refused. Every other window keeps working.

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 3: Dialog widgets (drawn and retail looks)

**Goal:** `UiPluginDialog` (keys, focus, placement), `UiDrawnPluginDialog` (primitive look, themed or Classic colours) and `UiRetailPluginDialog` (retail confirmation art) exist with tests.

**Files:**
- Create: `src/AcDream.App/UI/UiPluginDialog.cs`, `src/AcDream.App/UI/UiDrawnPluginDialog.cs`, `src/AcDream.App/UI/UiRetailPluginDialog.cs`
- Test: `tests/AcDream.App.Tests/UI/UiPluginDialogTests.cs`

**Acceptance Criteria:**
- [ ] Enter and KeypadEnter choose 0, Escape chooses the last index. Any other key is unhandled (`OnEvent` false) and chooses nothing.
- [ ] Drawn geometry with no font, title "Delete", message "Delete it?", OK/Cancel: width 260, height 86, buttons 64 wide at x 62 and 134, y 54, × at (242, 2) 16×16. With no message the height is 62. A long title widens the box to fit the title plus the ×. A long message wraps into lines no wider than width − 20.
- [ ] Clicking a drawn button through a `UiRoot` chooses its index. The × chooses the last index (2 of 3).
- [ ] Classic colours draw one `ClassicBackground` and one `ClassicTitleBar` quad. A Moss palette draws one `Background` quad, and buttons recolour on tick when the palette source changes.
- [ ] Retail look from the real fixture:
  - two buttons at x 80 and 240 labelled as given, box 400 wide;
  - one button centred at 160, the reject button hidden;
  - four buttons at 24/120/216/312 in a 416-wide box, all in the same bar;
  - the title is the first line in `TitleColor`, followed by the message; the popup grows by the message growth (95 → 109 for two 16 px lines);
  - clicking a retail button (after a draw) chooses its index.
- [ ] `TryCreate` returns null for a layout without the accept/reject buttons (the Message dialog layout).
- [ ] A press on the dialog's body (not a button) gives it the keyboard focus, in both looks.
- [ ] `Place` centres on an owner or the screen, rounds, and clamps on screen.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~UiPluginDialogTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/UiPluginDialogTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.Tests.UI.Layout;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

/// <summary>
/// The two looks of a plugin's Confirm box: the retail confirmation art for
/// Classic windows, and a drawn box for themed windows (or when the art is
/// not to hand). Both pick the first button on Enter and the last on Escape.
/// </summary>
public sealed class UiPluginDialogTests
{
    private static UiDrawnPluginDialog Drawn(
        string title = "Delete", string message = "Delete it?",
        IReadOnlyList<string>? buttons = null, Func<PluginUiPalette?>? palette = null) =>
        new(title, message, buttons ?? ["OK", "Cancel"], font: null, palette ?? (static () => null));

    private static UiRetailPluginDialog Retail(params string[] buttons) =>
        UiRetailPluginDialog.TryCreate(
            FixtureLoader.LoadConfirmationDialog(),
            FixtureLoader.LoadConfirmationDialog,
            "Title",
            "Message",
            buttons)!;

    private static List<int> Record(UiPluginDialog dialog)
    {
        var chosen = new List<int>();
        dialog.Chosen = chosen.Add;
        return chosen;
    }

    private static bool Press(UiPluginDialog dialog, Key key) =>
        dialog.OnEvent(new UiEvent(dialog.EventId, dialog, UiEventType.KeyDown, Data0: (int)key));

    private static UiRoot Mount(UiElement dialog)
    {
        var root = new UiRoot { Width = 800f, Height = 600f };
        root.AddChild(dialog);
        MarkupDrawProbe.Draw(root, 800f, 600f); // lays out anchored retail parts
        return root;
    }

    private static void ClickAt(UiRoot root, UiElement element)
    {
        Vector2 p = element.ScreenPosition;
        int x = (int)p.X + 4, y = (int)p.Y + 4;
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    [Fact]
    public void EnterPicksTheFirstButtonAndEscapeTheLast()
    {
        var dialog = Drawn(buttons: ["Yes", "No", "Later"]);
        List<int> chosen = Record(dialog);

        Assert.True(Press(dialog, Key.Enter));
        Assert.True(Press(dialog, Key.KeypadEnter));
        Assert.True(Press(dialog, Key.Escape));
        Assert.False(Press(dialog, Key.W));

        Assert.Equal([0, 0, 2], chosen);
        Assert.Equal(2, dialog.CancelIndex);
    }

    [Fact]
    public void TheDrawnBoxIsLaidOutFromItsText()
    {
        var dialog = Drawn();

        Assert.Equal(260f, dialog.Width);
        Assert.Equal(86f, dialog.Height);
        Assert.Equal([62f, 134f], dialog.Buttons.Select(b => b.Left));
        Assert.All(dialog.Buttons, b => Assert.Equal(54f, b.Top));
        Assert.All(dialog.Buttons, b => Assert.Equal(64f, b.Width));
        Assert.Equal(["OK", "Cancel"], dialog.Buttons.Select(b => b.Text));
        Assert.Equal((242f, 2f, 16f, 16f),
            (dialog.CloseButton.Left, dialog.CloseButton.Top, dialog.CloseButton.Width, dialog.CloseButton.Height));
        Assert.Equal(62f, Drawn(message: "").Height);
    }

    [Fact]
    public void ALongTitleWidensTheBoxAndALongMessageWraps()
    {
        string title = new('T', 40);                       // 280 px without a font
        var wide = Drawn(title: title);
        Assert.Equal(280f + 2f * UiDrawnPluginDialog.Pad + UiDrawnPluginDialog.CloseSize + 4f, wide.Width);

        var wrapped = Drawn(message: string.Join(' ', Enumerable.Repeat("word", 40)));
        Assert.True(wrapped.Lines.Count > 1);
        Assert.All(wrapped.Lines, line => Assert.True(line.Length * 7f <= wrapped.Width - 20f));
    }

    [Fact]
    public void ClickingADrawnButtonOrTheCloseButtonChooses()
    {
        var dialog = Drawn(buttons: ["Yes", "No", "Later"]);
        List<int> chosen = Record(dialog);
        UiRoot root = Mount(dialog);

        ClickAt(root, dialog.Buttons[1]);
        ClickAt(root, dialog.CloseButton);

        Assert.Equal([1, 2], chosen);
    }

    [Fact]
    public void TheDrawnBoxTakesClassicColoursOrThePalette()
    {
        var classic = MarkupDrawProbe.Draw(Drawn());
        Assert.Equal(1, MarkupDrawProbe.CountQuads(classic, UiDrawnPluginDialog.ClassicBackground));
        Assert.Equal(1, MarkupDrawProbe.CountQuads(classic, UiDrawnPluginDialog.ClassicTitleBar));

        PluginUiPalette? palette = PluginUiPalette.Moss;
        var themed = Drawn(palette: () => palette);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(themed), PluginUiPalette.Moss.Background));
        Assert.Equal(PluginUiPalette.Moss.Accent, themed.Buttons[0].BorderColor);

        palette = PluginUiPalette.Brass;
        themed.TickSelfAndChildren(0.016d);
        Assert.Equal(PluginUiPalette.Brass.Accent, themed.Buttons[0].BorderColor);
        Assert.Equal(PluginUiPalette.Brass.Field, themed.Buttons[1].BackgroundColor);
    }

    [Fact]
    public void APressOnTheBodyFocusesTheDialog()
    {
        var drawn = Drawn();
        UiRoot root = Mount(drawn);
        root.OnMouseDown(UiMouseButton.Left, (int)drawn.Left + 20, (int)drawn.Top + 35);
        Assert.Same(drawn, root.KeyboardFocus);

        var retail = Retail("Yes", "No");
        UiRoot retailRoot = Mount(retail);
        retailRoot.OnMouseDown(UiMouseButton.Left, (int)retail.Left + 200, (int)retail.Top + 20);
        Assert.Same(retail, retailRoot.KeyboardFocus);
    }

    [Fact]
    public void TwoRetailButtonsSitWhereYesAndNoDo()
    {
        var dialog = Retail("Yes", "No");

        Assert.Equal(400f, dialog.Width);
        Assert.Equal([80f, 240f], dialog.Buttons.Select(b => b.Left));
        Assert.Equal(["Yes", "No"], dialog.Buttons.Select(b => b.Label));
    }

    [Fact]
    public void OneRetailButtonIsCentred()
    {
        ImportedLayout layout = FixtureLoader.LoadConfirmationDialog();
        var dialog = UiRetailPluginDialog.TryCreate(
            layout, FixtureLoader.LoadConfirmationDialog, "", "Done.", ["OK"])!;

        UiButton only = Assert.Single(dialog.Buttons);
        Assert.Equal(160f, only.Left);
        Assert.False(((UiButton)layout.FindElement(RetailConfirmationDialogView.RejectButtonId)!).Visible);
    }

    [Fact]
    public void FourRetailButtonsWidenTheBox()
    {
        var dialog = Retail("A", "B", "C", "D");

        Assert.Equal(416f, dialog.Width);
        Assert.Equal([24f, 120f, 216f, 312f], dialog.Buttons.Select(b => b.Left));
        Assert.Equal(4, dialog.Buttons.Distinct().Count());
        Assert.All(dialog.Buttons, b => Assert.Same(dialog.Buttons[0].Parent, b.Parent));
    }

    [Fact]
    public void TheRetailTitleIsAGoldFirstLine()
    {
        var dialog = Retail("Yes", "No");

        UiText.Line[] lines = dialog.Message.LinesProvider!().ToArray();
        Assert.Equal("Title", lines[0].Text);
        Assert.Equal(UiRetailPluginDialog.TitleColor, lines[0].Color);
        Assert.Equal("Message", lines[1].Text);
        Assert.Equal(109f, dialog.Height);   // 95 + (2 × 16 − 18)
        Assert.Equal(dialog.Popup.Height, dialog.Height);
    }

    [Fact]
    public void ClickingARetailButtonChoosesIt()
    {
        var dialog = Retail("A", "B", "C");
        List<int> chosen = Record(dialog);
        UiRoot root = Mount(dialog);

        ClickAt(root, dialog.Buttons[2]);
        ClickAt(root, dialog.Buttons[1]);

        Assert.Equal([2, 1], chosen);
    }

    [Fact]
    public void ALayoutWithoutTheConfirmationButtonsGivesNoRetailBox()
    {
        ImportedLayout message = RetailDialogFactoryTests.BuildDialogLayout(RetailDialogType.Message);

        Assert.Null(UiRetailPluginDialog.TryCreate(message, () => null, "T", "M", ["OK", "Cancel"]));
    }

    [Fact]
    public void PlaceCentresOnTheOwnerOrTheScreenAndStaysOnScreen()
    {
        var dialog = Drawn();                                    // 260 × 86
        var owner = new UiPanel { Left = 100f, Top = 100f, Width = 201f, Height = 150f };

        dialog.Place(owner, new Vector2(800f, 600f));
        Assert.Equal((MathF.Round(100f + 100.5f - 130f), 132f), (dialog.Left, dialog.Top));

        dialog.Place(null, new Vector2(800f, 600f));
        Assert.Equal((270f, 257f), (dialog.Left, dialog.Top));

        owner.Left = 700f;
        dialog.Place(owner, new Vector2(800f, 600f));
        Assert.Equal(540f, dialog.Left);                          // clamped to 800 − 260
    }
}
```

Check before running: `UiText.Line` exposes `Text` and `Color`. If its members are named differently (look at `UiText.Line` in `src/AcDream.App/UI/UiText.cs`), use those names in the test. `FixtureLoader` lives in namespace `AcDream.App.Tests.UI.Layout` (see `tests/AcDream.App.Tests/UI/Layout/FixtureLoader.cs`), as does `RetailDialogFactoryTests`; fix the `using` if not.

- [ ] **Step 2: Run them to see them fail**

Run the Verify command. Expected: compile errors (the three dialog types don't exist).

- [ ] **Step 3: The base dialog**

`src/AcDream.App/UI/UiPluginDialog.cs`:

```csharp
using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// The box a plugin's <c>Confirm</c> opens: a message and a row of buttons.
/// Enter picks the first button and Escape the last; every other key passes
/// on to the game. A press anywhere on it gives it the keyboard focus.
/// </summary>
internal abstract class UiPluginDialog : UiElement
{
    protected UiPluginDialog(int buttonCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(buttonCount, 1);
        ButtonCount = buttonCount;
        AcceptsFocus = true;
        ClickThrough = false;
    }

    public int ButtonCount { get; }

    /// <summary>The index Escape, the close button and every cancel report: the last button's.</summary>
    public int CancelIndex => ButtonCount - 1;

    /// <summary>Called with the index of the button picked; the host closes the dialog.</summary>
    public Action<int>? Chosen { get; set; }

    protected void Choose(int index) => Chosen?.Invoke(index);

    /// <summary>Centres the dialog on the owner window, or on the screen without one, and keeps it on screen.</summary>
    public void Place(UiElement? owner, Vector2 screen)
    {
        Vector2 centre = screen * 0.5f;
        if (owner is not null)
            centre = owner.ScreenPosition + new Vector2(owner.Width, owner.Height) * 0.5f;
        Left = MathF.Round(Math.Clamp(centre.X - Width * 0.5f, 0f, MathF.Max(0f, screen.X - Width)));
        Top = MathF.Round(Math.Clamp(centre.Y - Height * 0.5f, 0f, MathF.Max(0f, screen.Y - Height)));
    }

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.KeyDown:
                switch ((Silk.NET.Input.Key)e.Data0)
                {
                    case Silk.NET.Input.Key.Enter:
                    case Silk.NET.Input.Key.KeypadEnter:
                        Choose(0);
                        return true;
                    case Silk.NET.Input.Key.Escape:
                        Choose(CancelIndex);
                        return true;
                    default:
                        return false;
                }
            case UiEventType.MouseDown:
            case UiEventType.RightDown:
                if (FindRoot() is { } root && !ReferenceEquals(root.KeyboardFocus, this))
                    root.SetKeyboardFocus(this);
                return true;
            case UiEventType.FocusGained:
            case UiEventType.FocusLost:
                return true;
            default:
                return base.OnEvent(in e);
        }
    }
}
```

Check: `Place` test line 1 expects `Left == round(200.5 − 130)`. `MathF.Round(70.5f)` is 70 (banker's rounding), and the test computes it the same way. Keep the test as written.

- [ ] **Step 4: The drawn look**

`src/AcDream.App/UI/UiDrawnPluginDialog.cs`:

```csharp
using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// A plugin dialog drawn with primitives: a title bar with a close button,
/// the wrapped message and a centred row of buttons, the first marked as
/// the default. Under a plugin theme it takes the owner window's palette
/// (re-read every tick); with no palette it takes the Classic context-menu
/// colours, which is how it stands in when the retail art is not to hand.
/// </summary>
internal sealed class UiDrawnPluginDialog : UiPluginDialog
{
    public const float Pad = 10f;
    public const float TitleHeight = 20f;
    public const float CloseSize = 16f;
    public const float ButtonHeight = 22f;
    public const float ButtonMinWidth = 64f;
    public const float ButtonTextPad = 8f;
    public const float ButtonGap = 8f;
    public const float MinWidth = 260f;

    public static readonly Vector4 ClassicBackground = UiMarkupContextMenu.ClassicBackground;
    public static readonly Vector4 ClassicBorder = UiMarkupContextMenu.ClassicBorder;
    public static readonly Vector4 ClassicText = UiMarkupContextMenu.ClassicText;
    public static readonly Vector4 ClassicTitleBar = UiMarkupContextMenu.ClassicHighlight;
    /// <summary>A markup button's own background.</summary>
    public static readonly Vector4 ClassicButton = new(0.1f, 0.1f, 0.15f, 0.8f);
    /// <summary>The default button's border in Classic: the gold of the focus outline.</summary>
    public static readonly Vector4 ClassicDefaultBorder = UiMarkupHotkey.FocusColor;

    private readonly string _title;
    private readonly UiDatFont? _font;
    private readonly Func<PluginUiPalette?> _palette;
    private readonly float _lineHeight;
    private readonly UiSimpleButton[] _buttons;

    public UiDrawnPluginDialog(
        string title, string message, IReadOnlyList<string> buttons,
        UiDatFont? font, Func<PluginUiPalette?> palette)
        : base(buttons.Count)
    {
        _title = title;
        _font = font;
        _palette = palette;
        Func<string, float> measure = font is { } dat ? dat.MeasureWidth : static text => text.Length * 7f;
        _lineHeight = font?.LineHeight ?? 14f;

        var widths = new float[buttons.Count];
        float row = ButtonGap * (buttons.Count - 1);
        for (int i = 0; i < buttons.Count; i++)
        {
            widths[i] = MathF.Max(ButtonMinWidth, MathF.Ceiling(measure(buttons[i])) + 2f * ButtonTextPad);
            row += widths[i];
        }
        Width = MathF.Max(MinWidth, MathF.Max(row + 2f * Pad,
            MathF.Ceiling(measure(title)) + 2f * Pad + CloseSize + 4f));
        Lines = message.Length == 0 ? Array.Empty<string>() : UiText.WrapWords(message, measure, Width - 2f * Pad);
        float messageHeight = Lines.Count * _lineHeight;
        Height = TitleHeight + Pad + (messageHeight > 0f ? messageHeight + Pad : 0f) + ButtonHeight + Pad;

        _buttons = new UiSimpleButton[buttons.Count];
        float x = MathF.Round((Width - row) * 0.5f);
        for (int i = 0; i < buttons.Count; i++)
        {
            int index = i;
            var button = new UiSimpleButton
            {
                Left = x, Top = Height - Pad - ButtonHeight, Width = widths[i], Height = ButtonHeight,
                Text = buttons[i], DatFont = font,
            };
            button.Click += () => Choose(index);
            AddChild(button);
            _buttons[i] = button;
            x += widths[i] + ButtonGap;
        }
        CloseButton = new UiSimpleButton
        {
            Left = Width - CloseSize - 2f, Top = MathF.Round((TitleHeight - CloseSize) * 0.5f),
            Width = CloseSize, Height = CloseSize, Text = "x", DatFont = font,
        };
        CloseButton.Click += () => Choose(CancelIndex);
        AddChild(CloseButton);
        ApplyColors(palette());
    }

    public IReadOnlyList<UiSimpleButton> Buttons => _buttons;

    public UiSimpleButton CloseButton { get; }

    /// <summary>The message as wrapped to the box.</summary>
    public IReadOnlyList<string> Lines { get; }

    protected override void OnTick(double deltaSeconds) => ApplyColors(_palette());

    private void ApplyColors(PluginUiPalette? p)
    {
        for (int i = 0; i < _buttons.Length; i++)
            Style(_buttons[i], p, isDefault: i == 0);
        Style(CloseButton, p, isDefault: false);
    }

    private static void Style(UiSimpleButton button, PluginUiPalette? p, bool isDefault)
    {
        button.BackgroundColor = p?.Field ?? ClassicButton;
        button.BorderColor = isDefault ? p?.Accent ?? ClassicDefaultBorder : p?.Border ?? ClassicBorder;
        button.TextColor = p?.Text ?? ClassicText;
        button.Outline = p is null;
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        PluginUiPalette? p = _palette();
        Vector4 text = p?.Text ?? ClassicText;
        ctx.DrawFill(0f, 0f, Width, Height, p?.Background ?? ClassicBackground);
        ctx.DrawFill(0f, 0f, Width, TitleHeight, p?.Field ?? ClassicTitleBar);
        UiCaption.Draw(ctx, _font, _title, Pad, 0f, TitleHeight, text, outline: p is null);
        float y = TitleHeight + Pad;
        foreach (string line in Lines)
        {
            UiCaption.Draw(ctx, _font, line, Pad, y, _lineHeight, text, outline: p is null);
            y += _lineHeight;
        }
    }

    protected override void OnDrawAfterChildren(UiRenderContext ctx) =>
        ctx.DrawRectOutline(0f, 0f, Width, Height, _palette()?.Border ?? ClassicBorder, 1f);
}
```

The Moss test counts `Background` once. `Field` is used by the title bar and the buttons, so it isn't counted. `ClassicTitleBar` (= context-menu highlight) is drawn only by the title bar.

- [ ] **Step 5: The retail look**

`src/AcDream.App/UI/UiRetailPluginDialog.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI.Layout;

namespace AcDream.App.UI;

/// <summary>
/// A plugin dialog in the retail confirmation box's art (layout 0x15). The
/// title is a gold first line over the message, since the retail box has no
/// caption. Its buttons are retail buttons in the box's button bar: two sit
/// where the retail Yes and No do, one is centred, and three or four are
/// centred 16 px apart, widening the box when they need it. Buttons past
/// the layout's own two are taken from further copies of the layout.
/// </summary>
internal sealed class UiRetailPluginDialog : UiPluginDialog
{
    public const float RowGap = 16f;
    public const float RowMargin = 24f;
    /// <summary>The gold of the markup focus outline.</summary>
    public static readonly Vector4 TitleColor = new(1f, 0.82f, 0.25f, 1f);

    private UiRetailPluginDialog(int buttonCount, UiElement popup, UiText message, IReadOnlyList<UiButton> buttons)
        : base(buttonCount)
    {
        Popup = popup;
        Message = message;
        Buttons = buttons;
    }

    public UiElement Popup { get; }

    public UiText Message { get; }

    public IReadOnlyList<UiButton> Buttons { get; }

    /// <summary>
    /// Builds the dialog from a freshly imported confirmation layout, or
    /// returns null when the layout lacks a part it needs (the host then
    /// draws one instead).
    /// </summary>
    public static UiRetailPluginDialog? TryCreate(
        ImportedLayout layout, Func<ImportedLayout?> moreLayouts,
        string title, string message, IReadOnlyList<string> buttons)
    {
        if (layout.FindElement(RetailConfirmationDialogView.PopupElementId) is not { } popup
            || layout.FindElement(RetailConfirmationDialogView.MessageElementId) is not UiText text
            || layout.FindElement(RetailConfirmationDialogView.AcceptButtonId) is not UiButton accept
            || layout.FindElement(RetailConfirmationDialogView.RejectButtonId) is not UiButton reject
            || accept.Parent is not { } bar
            || !ReferenceEquals(reject.Parent, bar))
            return null;

        var row = new List<UiButton>(buttons.Count) { accept };
        for (int i = 1; i < buttons.Count - 1; i++)
        {
            if (moreLayouts()?.FindElement(RetailConfirmationDialogView.AcceptButtonId) is not UiButton extra)
                return null;
            extra.Parent?.RemoveChild(extra);
            extra.Top = accept.Top;
            bar.AddChild(extra);
            row.Add(extra);
        }
        if (buttons.Count > 1)
            row.Add(reject);
        else
            reject.Visible = false;

        layout.Root.RemoveChild(popup);
        popup.LayoutPolicy = null;
        popup.Anchors = AnchorEdges.None;
        popup.Left = 0f;
        popup.Top = 0f;
        text.LayoutPolicy = null;
        text.Anchors = AnchorEdges.None;
        text.Padding = 0f;
        text.Selectable = false;
        if (layout.FindElement(RetailConfirmationDialogView.PendingDisplayId) is IUiDatStateful pending)
            pending.TrySetRetailState(0x19u); // the retail "nothing else waiting" state

        float buttonWidth = accept.Width;
        if (row.Count >= 3)
        {
            popup.Width = MathF.Max(popup.Width,
                row.Count * buttonWidth + (row.Count - 1) * RowGap + 2f * RowMargin);
        }
        SetLines(popup, text, title, message);

        var dialog = new UiRetailPluginDialog(buttons.Count, popup, text, row);
        // The bar spans the popup, so its width is the popup's.
        float rowWidth = row.Count * buttonWidth + (row.Count - 1) * RowGap;
        float start = MathF.Round((popup.Width - rowWidth) * 0.5f);
        for (int i = 0; i < row.Count; i++)
        {
            UiButton button = row[i];
            int index = i;
            button.LayoutPolicy = null;
            button.Anchors = AnchorEdges.None;
            button.Label = buttons[i];
            button.OnClick = () => dialog.Choose(index);
            if (row.Count == 1)
                button.Left = MathF.Round((popup.Width - buttonWidth) * 0.5f);
            else if (row.Count >= 3)
                button.Left = start + i * (buttonWidth + RowGap);
            // Two buttons keep the retail Yes/No places.
        }

        MakeClickThrough(popup);
        dialog.AddChild(popup);
        dialog.Width = popup.Width;
        dialog.Height = popup.Height;
        return dialog;
    }

    // As RetailConfirmationDialogView.SetMessage, with the title as a gold
    // first line: the popup grows by as much as the text does.
    private static void SetLines(UiElement popup, UiText text, string title, string message)
    {
        float basePopupHeight = popup.Height;
        float baseMessageHeight = text.Height;
        float maximumWidth = Math.Max(1f, text.Width - 2f * text.Padding);
        Func<string, float> measure = text.DatFont is { } font
            ? font.MeasureWidth
            : static value => value.Length * 8f;
        var lines = new List<UiText.Line>();
        if (title.Length > 0)
        {
            foreach (string line in UiText.WrapWords(title, measure, maximumWidth))
                lines.Add(new UiText.Line(line, TitleColor));
        }
        if (message.Length > 0)
        {
            foreach (string line in UiText.WrapWords(message, measure, maximumWidth))
                lines.Add(new UiText.Line(line, text.DefaultColor));
        }
        UiText.Line[] shown = lines.ToArray();
        text.LinesProvider = () => shown;

        float lineHeight = text.DatFont?.LineHeight ?? 16f;
        text.Height = Math.Max(baseMessageHeight, shown.Length * lineHeight);
        popup.Height = basePopupHeight + (text.Height - baseMessageHeight);
    }

    // The art and text are decoration: a press on them lands on the dialog,
    // which takes the keyboard focus. Only the buttons stay pressable.
    private static void MakeClickThrough(UiElement element)
    {
        if (element is UiButton)
            return;
        element.ClickThrough = true;
        foreach (UiElement child in element.Children)
            MakeClickThrough(child);
    }
}
```

Check: `UiText.Line`'s constructor takes `(string, Vector4)`, as `RetailConfirmationDialogView.SetMessage` uses it. `text.LinesProvider` returns something enumerable of `UiText.Line`; the test calls `.ToArray()` on it. If `LinesProvider` returns an array type, that still compiles.

- [ ] **Step 6: Run the tests**

Run the Verify command → all pass.

If a retail position assertion fails because the fixture's bar is not the buttons' parent, or the popup's height doesn't grow as expected, stop and report it. Don't loosen the test: the layout facts (`popup 400×95`, bar at y 48 spanning the popup, buttons 80 wide at x 80/240, message 370×18) come from `dialogs_2100003C.json`.

- [ ] **Step 7: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/UiPluginDialog.cs src/AcDream.App/UI/UiDrawnPluginDialog.cs src/AcDream.App/UI/UiRetailPluginDialog.cs tests/AcDream.App.Tests/UI/UiPluginDialogTests.cs
git commit -m "feat(ui): plugin dialog boxes in retail art and drawn looks

Enter picks the first button, Escape the last. Classic windows get the
retail confirmation art with one to four retail buttons; themed windows
(or a missing layout) get a drawn box with a close button.

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 4: `PluginDialogHost`

**Goal:** A host queues confirms per owner window, shows them in the right look, blocks and refocuses, answers each exactly once (button, owner closing, handle disposal, host disposal), and keeps `ShowModal` blocks until the modal panel goes.

**Files:**
- Create: `src/AcDream.App/UI/PluginDialogHost.cs`
- Modify: `src/AcDream.App/UI/PluginUiThemeSettings.cs` (`UiPluginMarkupPanel.Palette`)
- Test: `tests/AcDream.App.Tests/UI/PluginDialogHostTests.cs`

**Acceptance Criteria:**
- [ ] `Confirm` with an owner shows a dialog centred on the owner as a root child. It blocks the owner (`ModalBlockCount == 1`), takes the keyboard focus and returns a handle.
- [ ] Enter reports `[0]` once. The dialog is removed and the owner unblocked before the callback runs. A later handle `Dispose` reports nothing more.
- [ ] Two confirms on one owner: only the first shows; answering it shows the second. Confirms on two owners show at once.
- [ ] Owner hidden or removed → `Tick` answers every dialog queued on it with its last index, in order, and nothing is left.
- [ ] Handle `Dispose` answers the last index once. On a queued dialog it answers at once and it never shows.
- [ ] An owner hidden at the time of the call, or no owner → centred on the screen, blocks nothing, not cancelled by `Tick`.
- [ ] Focus goes back on close to what had it (also when it was inside the owner). It doesn't come back when the player moved focus away first.
- [ ] A themed owner (`UiPluginMarkupPanel`, Moss) gets a `UiDrawnPluginDialog` drawing the Moss background. A Classic owner with retail art gets a `UiRetailPluginDialog`. A failing or partless layout gets a drawn box.
- [ ] Host `Dispose` answers every open and queued dialog with its last index and ends every modal.
- [ ] A throwing callback doesn't stop the next queued dialog from showing.
- [ ] `ShowModal(panel, owner)` blocks the owner with the panel as the blocker until the panel is hidden or removed (then `Tick` unblocks). It doesn't stack, moves to a new owner, and blocks nothing without a usable owner.
- [ ] A dialog follows its owner when the owner moves (re-placed on `Tick`).

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~PluginDialogHostTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/PluginDialogHostTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.Tests.UI.Layout;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

/// <summary>
/// Plugin dialogs on screen: one at a time per owner window, each answered
/// exactly once, the owner blocked while it shows and focus handed back
/// when it closes.
/// </summary>
public sealed class PluginDialogHostTests
{
    private sealed class Fixture
    {
        public UiRoot Root { get; } = new() { Width = 800f, Height = 600f };
        public UiPanel Owner { get; } = new() { Left = 100f, Top = 100f, Width = 200f, Height = 150f };
        public UiSimpleButton OwnerField { get; } = new()
        {
            Left = 10f, Top = 10f, Width = 60f, Height = 20f, AcceptsFocus = true, TabStop = true,
        };
        public UiPanel Other { get; } = new() { Left = 500f, Top = 100f, Width = 200f, Height = 150f };
        public UiSimpleButton OtherField { get; } = new()
        {
            Left = 10f, Top = 10f, Width = 60f, Height = 20f, AcceptsFocus = true, TabStop = true,
        };
        public PluginDialogHost Host { get; }

        public Fixture(Func<ImportedLayout?>? retailLayout = null)
        {
            Owner.AddChild(OwnerField);
            Other.AddChild(OtherField);
            Root.AddChild(Owner);
            Root.AddChild(Other);
            Host = new PluginDialogHost(Root, retailLayout ?? (static () => null), font: null);
        }

        public IDisposable Confirm(UiElement? owner, List<int> answers, params string[] buttons) =>
            Host.Confirm("Title", "Message", buttons.Length == 0 ? ["OK", "Cancel"] : buttons, owner, answers.Add)!;

        public void Press(Key key) => Root.OnKeyDown((int)key);
    }

    [Fact]
    public void AConfirmShowsCentredOnItsOwnerBlocksItAndTakesFocus()
    {
        var f = new Fixture();

        IDisposable handle = f.Confirm(f.Owner, []);

        UiPluginDialog view = f.Host.ShownFor(f.Owner)!;
        Assert.NotNull(handle);
        Assert.IsType<UiDrawnPluginDialog>(view);
        Assert.Same(f.Root, view.Parent);
        Assert.Equal(1, f.Owner.ModalBlockCount);
        Assert.Equal(MathF.Round(200f - view.Width * 0.5f), view.Left);
        Assert.Same(view, f.Root.KeyboardFocus);
    }

    [Fact]
    public void AButtonAnswersOnceAfterTheDialogIsGone()
    {
        var f = new Fixture();
        var answers = new List<int>();
        bool goneFirst = false;
        IDisposable handle = f.Host.Confirm("T", "M", ["OK", "Cancel"], f.Owner, index =>
        {
            answers.Add(index);
            goneFirst = f.Host.ShownFor(f.Owner) is null && f.Owner.ModalBlockCount == 0;
        })!;
        UiPluginDialog view = f.Host.ShownFor(f.Owner)!;

        f.Press(Key.Enter);
        handle.Dispose();

        Assert.Equal([0], answers);
        Assert.True(goneFirst);
        Assert.Null(view.Parent);
    }

    [Fact]
    public void DialogsForOneOwnerQueueAndOthersShowAtOnce()
    {
        var f = new Fixture();
        var first = new List<int>();
        var second = new List<int>();
        var other = new List<int>();
        f.Confirm(f.Owner, first);
        f.Confirm(f.Owner, second);
        f.Confirm(f.Other, other);
        UiPluginDialog firstView = f.Host.ShownFor(f.Owner)!;
        Assert.NotNull(f.Host.ShownFor(f.Other));
        Assert.Equal(3, f.Host.PendingCount);

        f.Root.SetKeyboardFocus(firstView);
        f.Press(Key.Escape);

        Assert.Equal([1], first);
        UiPluginDialog secondView = f.Host.ShownFor(f.Owner)!;
        Assert.NotSame(firstView, secondView);
        f.Press(Key.Enter);
        Assert.Equal([0], second);
        Assert.Empty(other);
    }

    [Fact]
    public void AnOwnerClosingAnswersEveryDialogQueuedOnIt()
    {
        var f = new Fixture();
        var answers = new List<int>();
        f.Host.Confirm("T", "M", ["A", "B"], f.Owner, i => answers.Add(10 + i));
        f.Host.Confirm("T", "M", ["A", "B", "C"], f.Owner, i => answers.Add(20 + i));

        f.Owner.Visible = false;
        f.Host.Tick();

        Assert.Equal([11, 22], answers);
        Assert.Equal(0, f.Host.PendingCount);
        Assert.Equal(0, f.Owner.ModalBlockCount);
    }

    [Fact]
    public void AnOwnerRemovedAnswersItsDialog()
    {
        var f = new Fixture();
        var answers = new List<int>();
        f.Confirm(f.Owner, answers);

        f.Root.RemoveChild(f.Owner);
        f.Host.Tick();

        Assert.Equal([1], answers);
    }

    [Fact]
    public void DisposingTheHandleAnswersTheLastButtonOnce()
    {
        var f = new Fixture();
        var shown = new List<int>();
        var queued = new List<int>();
        IDisposable shownHandle = f.Confirm(f.Owner, shown, "Yes", "No", "Later");
        IDisposable queuedHandle = f.Confirm(f.Owner, queued);

        queuedHandle.Dispose();
        Assert.Equal([1], queued);
        Assert.NotNull(f.Host.ShownFor(f.Owner));

        shownHandle.Dispose();
        shownHandle.Dispose();
        Assert.Equal([2], shown);
        Assert.Null(f.Host.ShownFor(f.Owner));
        Assert.Equal([1], queued); // never shown, never answered twice
    }

    [Fact]
    public void AHiddenOwnerOrNoOwnerCentresOnTheScreenAndBlocksNothing()
    {
        var f = new Fixture();
        f.Owner.Visible = false;
        var answers = new List<int>();

        f.Confirm(f.Owner, answers);
        f.Host.Tick();

        UiPluginDialog view = f.Host.ShownFor(null)!;
        Assert.Equal(MathF.Round(400f - view.Width * 0.5f), view.Left);
        Assert.Equal(0, f.Owner.ModalBlockCount);
        Assert.Empty(answers);
    }

    [Fact]
    public void FocusComesBackWhenTheDialogCloses()
    {
        var f = new Fixture();
        f.Root.SetKeyboardFocus(f.OtherField);
        f.Confirm(f.Owner, []);
        f.Press(Key.Enter);
        Assert.Same(f.OtherField, f.Root.KeyboardFocus);

        f.Root.SetKeyboardFocus(f.OwnerField);
        f.Confirm(f.Owner, []);
        Assert.NotSame(f.OwnerField, f.Root.KeyboardFocus);
        f.Press(Key.Enter);
        Assert.Same(f.OwnerField, f.Root.KeyboardFocus);

        IDisposable handle = f.Confirm(f.Owner, []);
        f.Root.SetKeyboardFocus(f.OtherField);   // the player moved on
        handle.Dispose();
        Assert.Same(f.OtherField, f.Root.KeyboardFocus);
    }

    [Fact]
    public void AThemedOwnerGetsADrawnBoxInItsPalette()
    {
        var f = new Fixture();
        var themed = new UiPluginMarkupPanel(
            _ => (0u, 0, 0), new PluginUiThemeSettings { Theme = PluginUiTheme.Moss })
        {
            Left = 100f, Top = 300f, Width = 200f, Height = 150f,
        };
        f.Root.AddChild(themed);

        f.Confirm(themed, []);

        var view = Assert.IsType<UiDrawnPluginDialog>(f.Host.ShownFor(themed));
        Assert.Equal(1, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(view), PluginUiPalette.Moss.Background));
    }

    [Fact]
    public void AClassicOwnerGetsTheRetailBoxWhenTheArtIsThere()
    {
        var f = new Fixture(FixtureLoader.LoadConfirmationDialog);

        f.Confirm(f.Owner, [], "Yes", "No", "Later");

        var view = Assert.IsType<UiRetailPluginDialog>(f.Host.ShownFor(f.Owner));
        Assert.Equal(["Yes", "No", "Later"], view.Buttons.Select(b => b.Label));
    }

    [Fact]
    public void WithoutUsableArtTheClassicBoxIsDrawn()
    {
        var throwing = new Fixture(static () => throw new InvalidOperationException("no catalog"));
        throwing.Confirm(throwing.Owner, []);
        Assert.IsType<UiDrawnPluginDialog>(throwing.Host.ShownFor(throwing.Owner));

        var partless = new Fixture(static () => RetailDialogFactoryTests.BuildDialogLayout(RetailDialogType.Message));
        partless.Confirm(partless.Owner, []);
        Assert.IsType<UiDrawnPluginDialog>(partless.Host.ShownFor(partless.Owner));
    }

    [Fact]
    public void DisposingTheHostAnswersEverythingAndEndsModals()
    {
        var f = new Fixture();
        var answers = new List<int>();
        f.Host.Confirm("T", "M", ["A", "B"], f.Owner, i => answers.Add(10 + i));
        f.Host.Confirm("T", "M", ["A", "B", "C"], f.Owner, i => answers.Add(20 + i));
        f.Host.Confirm("T", "M", ["A"], null, i => answers.Add(30 + i));
        var panel = new UiPanel { Left = 300f, Top = 300f, Width = 100f, Height = 100f };
        f.Root.AddChild(panel);
        f.Host.ShowModal(panel, f.Other);

        f.Host.Dispose();

        Assert.Equal([11, 22, 30], answers);
        Assert.Equal(0, f.Owner.ModalBlockCount);
        Assert.Equal(0, f.Other.ModalBlockCount);
        Assert.Null(f.Host.Confirm("T", "M", ["OK"], null, _ => { }));
    }

    [Fact]
    public void AThrowingAnswerDoesNotStopTheQueue()
    {
        var f = new Fixture();
        var second = new List<int>();
        f.Host.Confirm("T", "M", ["OK"], f.Owner, _ => throw new InvalidOperationException("plugin bug"));
        f.Confirm(f.Owner, second);

        f.Press(Key.Enter);

        Assert.NotNull(f.Host.ShownFor(f.Owner));
        f.Press(Key.Enter);
        Assert.Equal([0], second);
    }

    [Fact]
    public void AModalPanelBlocksItsOwnerUntilItHides()
    {
        var f = new Fixture();
        var panel = new UiPanel { Left = 300f, Top = 300f, Width = 100f, Height = 100f };
        f.Root.AddChild(panel);

        f.Host.ShowModal(panel, f.Owner);
        f.Host.ShowModal(panel, f.Owner);
        Assert.Equal(1, f.Owner.ModalBlockCount);
        Assert.True(panel.ZOrder > f.Owner.ZOrder);

        f.Root.OnMouseDown(UiMouseButton.Left, 115, 115);
        f.Root.OnMouseUp(UiMouseButton.Left, 115, 115);
        Assert.Equal(UiElement.ModalFlashDuration, panel.ModalFlashSeconds);

        panel.Visible = false;
        f.Host.Tick();
        Assert.Equal(0, f.Owner.ModalBlockCount);
    }

    [Fact]
    public void AModalPanelEndsWhenRemovedAndMovesToANewOwner()
    {
        var f = new Fixture();
        var panel = new UiPanel { Left = 300f, Top = 300f, Width = 100f, Height = 100f };
        f.Root.AddChild(panel);

        f.Host.ShowModal(panel, f.Owner);
        f.Host.ShowModal(panel, f.Other);
        Assert.Equal(0, f.Owner.ModalBlockCount);
        Assert.Equal(1, f.Other.ModalBlockCount);

        f.Root.RemoveChild(panel);
        f.Host.Tick();
        Assert.Equal(0, f.Other.ModalBlockCount);
    }

    [Fact]
    public void AModalPanelWithoutAUsableOwnerBlocksNothing()
    {
        var f = new Fixture();
        var panel = new UiPanel { Left = 300f, Top = 300f, Width = 100f, Height = 100f };
        f.Root.AddChild(panel);

        f.Host.ShowModal(panel, null);
        f.Host.ShowModal(panel, panel);

        Assert.Equal(0, panel.ModalBlockCount);
        Assert.Equal(0, f.Owner.ModalBlockCount);
    }

    [Fact]
    public void ADialogFollowsItsOwner()
    {
        var f = new Fixture();
        f.Confirm(f.Owner, []);
        UiPluginDialog view = f.Host.ShownFor(f.Owner)!;
        float left = view.Left;

        f.Owner.Left += 50f;
        f.Host.Tick();

        Assert.Equal(left + 50f, view.Left);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run the Verify command. Expected: compile errors (`PluginDialogHost`, `UiPluginMarkupPanel.Palette` missing).

- [ ] **Step 3: Expose the panel's palette**

In `src/AcDream.App/UI/PluginUiThemeSettings.cs`, class `UiPluginMarkupPanel`, after `ModernFont`:

```csharp
    /// <summary>The palette the panel draws with now, or null under Classic.</summary>
    internal PluginUiPalette? Palette => _settings.Palette;
```

- [ ] **Step 4: The host**

`src/AcDream.App/UI/PluginDialogHost.cs`:

```csharp
using AcDream.App.UI.Layout;

namespace AcDream.App.UI;

/// <summary>
/// Plugin dialogs on screen. <c>Confirm</c> boxes queue per owner window
/// (ownerless ones share a queue) and show one at a time per owner; plugin
/// windows can be shown modal over another. Both are window-modal through
/// <see cref="UiRoot.BlockWindow"/>: only the owner is blocked. Every dialog
/// is answered exactly once, and the answer runs after the dialog is gone.
/// UI thread only; <see cref="Tick"/> once per frame.
/// </summary>
internal sealed class PluginDialogHost : IDisposable
{
    private sealed class ConfirmEntry
    {
        public required string Title { get; init; }
        public required string Message { get; init; }
        public required IReadOnlyList<string> Buttons { get; init; }
        public required UiElement? Owner { get; init; }
        public required Action<int> OnResult { get; init; }
        public UiPluginDialog? View { get; set; }
        public IDisposable? Block { get; set; }
        public UiElement? PreviousFocus { get; set; }
        public bool Done { get; set; }
        public int CancelIndex => Buttons.Count - 1;
    }

    private sealed record ModalEntry(UiElement Panel, UiElement Owner, IDisposable Block);

    private readonly UiRoot _root;
    private readonly Func<ImportedLayout?> _retailLayout;
    private readonly UiDatFont? _font;
    private readonly List<ConfirmEntry> _confirms = new();
    private readonly List<ModalEntry> _modals = new();
    private bool _disposed;

    /// <param name="root">The root the dialogs are added to.</param>
    /// <param name="retailLayout">A fresh retail confirmation layout on each call, or null when the art is not to hand.</param>
    /// <param name="font">The Classic font for a drawn box.</param>
    public PluginDialogHost(UiRoot root, Func<ImportedLayout?> retailLayout, UiDatFont? font)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _retailLayout = retailLayout ?? throw new ArgumentNullException(nameof(retailLayout));
        _font = font;
    }

    /// <summary>The dialog showing for an owner window, or for the ownerless queue when null.</summary>
    internal UiPluginDialog? ShownFor(UiElement? owner) => HeadFor(owner)?.View;

    /// <summary>Dialogs showing or queued.</summary>
    internal int PendingCount => _confirms.Count;

    /// <summary>
    /// Queues a dialog and returns a handle whose disposal answers it with
    /// its last button; null once the host is disposed. An owner that is not
    /// a shown top-level window counts as none: the dialog is centred on the
    /// screen and blocks nothing.
    /// </summary>
    public IDisposable? Confirm(
        string title, string message, IReadOnlyList<string> buttons,
        UiElement? owner, Action<int> onResult)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(buttons);
        ArgumentNullException.ThrowIfNull(onResult);
        if (buttons.Count == 0)
            throw new ArgumentException("A dialog needs a button.", nameof(buttons));
        if (_disposed)
            return null;
        if (OwnerGone(owner))
            owner = null;

        var entry = new ConfirmEntry
        {
            Title = title, Message = message, Buttons = buttons, Owner = owner, OnResult = onResult,
        };
        _confirms.Add(entry);
        if (ReferenceEquals(HeadFor(owner), entry))
            Show(entry);
        return new ConfirmHandle(this, entry);
    }

    /// <summary>
    /// Blocks <paramref name="owner"/> until <paramref name="panel"/> is
    /// hidden or removed. The same panel again over the same owner changes
    /// nothing; over another owner the block moves. No owner, an unmounted
    /// one, or the panel itself blocks nothing.
    /// </summary>
    public void ShowModal(UiElement panel, UiElement? owner)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (_disposed)
            return;
        foreach (ModalEntry existing in _modals.Where(m => ReferenceEquals(m.Panel, panel)).ToArray())
        {
            if (ReferenceEquals(existing.Owner, owner))
            {
                _root.BringToFront(panel);
                return;
            }
            End(existing);
        }
        if (owner is null
            || ReferenceEquals(owner, panel)
            || !ReferenceEquals(owner.Parent, _root)
            || !ReferenceEquals(panel.Parent, _root))
            return;
        _modals.Add(new ModalEntry(panel, owner, _root.BlockWindow(owner, panel)));
        _root.BringToFront(panel);
    }

    /// <summary>Answers the dialogs of closed owners, keeps dialogs on their owners, and ends modals whose panel is gone.</summary>
    public void Tick()
    {
        foreach (ConfirmEntry entry in _confirms.ToArray())
        {
            if (entry.Done)
                continue;
            if (entry.Owner is not null && OwnerGone(entry.Owner))
                Finish(entry, entry.CancelIndex);
            else
                entry.View?.Place(entry.Owner, _root.EffectiveCanvasSize);
        }
        foreach (ModalEntry modal in _modals.ToArray())
        {
            if (!modal.Panel.Visible
                || !ReferenceEquals(modal.Panel.Parent, _root)
                || !ReferenceEquals(modal.Owner.Parent, _root))
                End(modal);
        }
    }

    /// <summary>Answers every dialog with its last button and ends every modal.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (ConfirmEntry entry in _confirms.ToArray())
            Finish(entry, entry.CancelIndex);
        foreach (ModalEntry modal in _modals.ToArray())
            End(modal);
    }

    private bool OwnerGone(UiElement? owner) =>
        owner is not null && (!owner.Visible || !ReferenceEquals(owner.Parent, _root));

    private ConfirmEntry? HeadFor(UiElement? owner) =>
        _confirms.FirstOrDefault(entry => ReferenceEquals(entry.Owner, owner));

    private void Show(ConfirmEntry entry)
    {
        UiPluginDialog view = CreateView(entry);
        entry.PreviousFocus = _root.KeyboardFocus;
        view.Chosen = index => Finish(entry, index);
        _root.AddChild(view);
        _root.BringToFront(view);
        view.Place(entry.Owner, _root.EffectiveCanvasSize);
        entry.View = view;
        if (entry.Owner is { } owner)
            entry.Block = _root.BlockWindow(owner, view);
        _root.SetKeyboardFocus(view);
    }

    private UiPluginDialog CreateView(ConfirmEntry entry)
    {
        if (entry.Owner is UiPluginMarkupPanel panel && panel.Palette is not null)
        {
            return new UiDrawnPluginDialog(
                entry.Title, entry.Message, entry.Buttons, panel.ModernFont ?? _font, () => panel.Palette);
        }
        try
        {
            if (_retailLayout() is { } layout
                && UiRetailPluginDialog.TryCreate(layout, _retailLayout, entry.Title, entry.Message, entry.Buttons)
                    is { } retail)
                return retail;
        }
        catch (Exception error)
        {
            Console.WriteLine($"[UI] plugin dialog drawn without retail art: {error.Message}");
        }
        return new UiDrawnPluginDialog(entry.Title, entry.Message, entry.Buttons, _font, static () => null);
    }

    private void Finish(ConfirmEntry entry, int index)
    {
        if (entry.Done)
            return;
        entry.Done = true;
        _confirms.Remove(entry);
        if (entry.View is { } view)
        {
            view.Chosen = null;
            bool hadFocus = ReferenceEquals(_root.KeyboardFocus, view);
            entry.Block?.Dispose();
            entry.Block = null;
            _root.RemoveChild(view);
            entry.View = null;
            if (hadFocus && entry.PreviousFocus is { } previous && _root.CanFocus(previous))
                _root.SetKeyboardFocus(previous);
            if (!_disposed && HeadFor(entry.Owner) is { } next && !OwnerGone(next.Owner))
                Show(next);
        }
        try
        {
            entry.OnResult(index);
        }
        catch (Exception error)
        {
            Console.WriteLine($"[UI] plugin dialog answer handler failed: {error.Message}");
        }
    }

    private void End(ModalEntry modal)
    {
        if (_modals.Remove(modal))
            modal.Block.Dispose();
    }

    private sealed class ConfirmHandle(PluginDialogHost host, ConfirmEntry entry) : IDisposable
    {
        public void Dispose() => host.Finish(entry, entry.CancelIndex);
    }
}
```

Check: `PluginUiThemeSettings` has a public parameterless-callable constructor `(SettingsStore? store = null, UiDatFont? modernFont = null)` and a settable `Theme`, so the test's object initializer compiles. `UiPluginMarkupPanel` is `internal`; App tests already use App internals.

- [ ] **Step 5: Run the tests**

Run the Verify command → all pass. Also rerun `UiPluginDialogTests` and `UiRootWindowBlockTests`.

- [ ] **Step 6: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/PluginDialogHost.cs src/AcDream.App/UI/PluginUiThemeSettings.cs tests/AcDream.App.Tests/UI/PluginDialogHostTests.cs
git commit -m "feat(ui): plugin dialog host

Confirm boxes queue per owner window and show one at a time, blocking
only their owner; each is answered once (button, owner closing, handle
or host disposal). Modal plugin windows block their owner until hidden.

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 5: Registry and runtime wiring

**Goal:** `BufferedUiRegistry` implements `Confirm` and `ShowModal` (plain and scoped): it validates dialogs, resolves view names to the plugin's mounted windows and forwards to a bound `PluginDialogHost`. `RetailUiRuntime` creates, binds, ticks and disposes the host.

**Files:**
- Modify: `src/AcDream.App/Plugins/BufferedUiRegistry.cs`
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (fields ~line 405, `MountDialogFactory` ~3755, `MountPlugins` ~4076, tick ~764, shutdown ~5196)
- Test: `tests/AcDream.App.Tests/Plugins/BufferedUiRegistryDialogTests.cs`

**Acceptance Criteria:**
- [ ] Unbound: the scoped `Confirm` returns null and the plain one false. Validation still throws.
- [ ] Bound: `OwnerView` by id or by title blocks that mounted window, and the dialog is centred on it. An unknown view or another plugin's window gives an ownerless dialog (owner not blocked).
- [ ] `Buttons == null` shows OK and Cancel. 0 buttons, 5 buttons, or a blank caption → `ArgumentException`.
- [ ] Disposing the returned handle answers with the last index.
- [ ] `ShowModal(owner, "options", "main")` shows the hidden window and blocks "main". `HidePanel` then a host tick unblocks it.
- [ ] `ShowModal` without an owner shows but blocks nothing. An unknown view, an unbound registry, or a registry after `UnbindClientWindowControl` → false.
- [ ] The solution builds in Release with 0 warnings.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~BufferedUiRegistryDialogTests|FullyQualifiedName~BufferedUiRegistryTests"` → all pass; `dotnet build AcDream.slnx -c Release` → 0 warnings, 0 errors.

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/Plugins/BufferedUiRegistryDialogTests.cs`:

```csharp
using AcDream.App.Plugins;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// A plugin's dialogs name its own mounted windows as owners, are checked
/// before they are queued, and reach the bound dialog host.
/// </summary>
public sealed class BufferedUiRegistryDialogTests
{
    private static readonly PluginUiOwner Owner = new("acdream.test", "Test");

    private sealed class Fixture
    {
        public BufferedUiRegistry Registry { get; } = new();
        public UiRoot Root { get; } = new() { Width = 1280f, Height = 720f };
        public PluginDialogHost Host { get; }

        public Fixture(bool bind = true)
        {
            Host = new PluginDialogHost(Root, static () => null, font: null);
            if (bind)
            {
                Registry.BindPluginWindowControl(Root.ShowWindow, Root.HideWindow);
                Registry.BindPluginDialogs(Host);
            }
        }

        public UiPanel Mount(PluginUiOwner owner, string windowId, string title, bool startVisible = true)
        {
            Registry.RegisterPanelContent(owner, new PluginPanelDescriptor(windowId, title), "<panel />", new object());
            BufferedUiRegistry.Pending pending = Assert.Single(Registry.Drain());
            var frame = new UiPanel { Left = 100f, Top = 100f, Width = 200f, Height = 100f };
            var visibility = new PluginWindowVisibilityController(null, startVisible);
            frame.VisibleSource = visibility.ShouldBeVisible;
            frame.Visible = visibility.ShouldBeVisible();
            Root.AddChild(frame);
            Registry.CompleteMount(pending, Root, frame);
            Root.WindowManager.Register(pending.WindowName, frame, frame, visibility);
            Registry.CompleteWindowMount(pending, () => Root.WindowManager.Unregister(pending.WindowName));
            return frame;
        }
    }

    [Fact]
    public void WithoutADialogHostNothingIsQueued()
    {
        var f = new Fixture(bind: false);

        Assert.Null(f.Registry.Confirm(Owner, new PluginDialog("T", "M"), _ => { }));
        Assert.False(f.Registry.Confirm(new PluginDialog("T", "M"), _ => { }));
    }

    [Fact]
    public void TheOwnerIsThePluginsMountedWindowByIdOrTitle()
    {
        var f = new Fixture();
        UiPanel main = f.Mount(Owner, "main", "Main");

        Assert.NotNull(f.Registry.Confirm(Owner, new PluginDialog("T", "M", OwnerView: "main"), _ => { }));
        Assert.Equal(1, main.ModalBlockCount);
        UiPluginDialog view = f.Host.ShownFor(main)!;
        Assert.Equal(MathF.Round(200f - view.Width * 0.5f), view.Left);

        f.Registry.Confirm(Owner, new PluginDialog("T", "M", OwnerView: "Main"), _ => { });
        Assert.Equal(2, f.Host.PendingCount);
    }

    [Fact]
    public void AnUnknownOrForeignOwnerBlocksNothing()
    {
        var f = new Fixture();
        UiPanel main = f.Mount(Owner, "main", "Main");

        Assert.NotNull(f.Registry.Confirm(Owner, new PluginDialog("T", "M", OwnerView: "missing"), _ => { }));
        Assert.NotNull(f.Registry.Confirm(
            new PluginUiOwner("another", "Other"), new PluginDialog("T", "M", OwnerView: "main"), _ => { }));

        Assert.Equal(0, main.ModalBlockCount);
        Assert.NotNull(f.Host.ShownFor(null));
    }

    [Fact]
    public void ADialogWithoutButtonsShowsOkAndCancel()
    {
        var f = new Fixture();

        f.Registry.Confirm(Owner, new PluginDialog("T", "M"), _ => { });

        var view = Assert.IsType<UiDrawnPluginDialog>(f.Host.ShownFor(null));
        Assert.Equal(["OK", "Cancel"], view.Buttons.Select(b => b.Text));
    }

    [Fact]
    public void BadButtonsAreRefusedEvenWithoutAHost()
    {
        foreach (bool bind in new[] { true, false })
        {
            var f = new Fixture(bind);
            Assert.Throws<ArgumentException>(() => f.Registry.Confirm(Owner, new PluginDialog("T", "M", []), _ => { }));
            Assert.Throws<ArgumentException>(() => f.Registry.Confirm(
                Owner, new PluginDialog("T", "M", ["1", "2", "3", "4", "5"]), _ => { }));
            Assert.Throws<ArgumentException>(() => f.Registry.Confirm(Owner, new PluginDialog("T", "M", ["OK", " "]), _ => { }));
        }
    }

    [Fact]
    public void DisposingTheHandleAnswersTheLastButton()
    {
        var f = new Fixture();
        var answers = new List<int>();

        f.Registry.Confirm(Owner, new PluginDialog("T", "M", ["A", "B", "C"]), answers.Add)!.Dispose();

        Assert.Equal([2], answers);
    }

    [Fact]
    public void ShowModalShowsTheWindowAndBlocksItsOwnerUntilItHides()
    {
        var f = new Fixture();
        UiPanel main = f.Mount(Owner, "main", "Main");
        UiPanel options = f.Mount(Owner, "options", "Options", startVisible: false);

        Assert.True(f.Registry.ShowModal(Owner, "options", "main"));
        Assert.True(options.Visible);
        Assert.Equal(1, main.ModalBlockCount);

        Assert.True(f.Registry.HidePanel(Owner, "options"));
        f.Root.Tick(0.016d, 16L);
        f.Host.Tick();
        Assert.Equal(0, main.ModalBlockCount);
    }

    [Fact]
    public void ShowModalWithoutAnOwnerShowsButBlocksNothing()
    {
        var f = new Fixture();
        UiPanel main = f.Mount(Owner, "main", "Main");
        UiPanel options = f.Mount(Owner, "options", "Options", startVisible: false);

        Assert.True(f.Registry.ShowModal(Owner, "options"));

        Assert.True(options.Visible);
        Assert.Equal(0, main.ModalBlockCount);
    }

    [Fact]
    public void ShowModalOfAnUnknownWindowOrWithoutAHostIsFalse()
    {
        var f = new Fixture();
        f.Mount(Owner, "main", "Main");
        f.Mount(Owner, "options", "Options", startVisible: false);
        Assert.False(f.Registry.ShowModal(Owner, "missing", "main"));

        f.Registry.UnbindClientWindowControl();
        Assert.False(f.Registry.ShowModal(Owner, "options", "main"));
        Assert.Null(f.Registry.Confirm(Owner, new PluginDialog("T", "M"), _ => { }));

        var unbound = new Fixture(bind: false);
        unbound.Mount(Owner, "options", "Options", startVisible: false);
        Assert.False(unbound.Registry.ShowModal(Owner, "options"));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run the Verify test command. Expected: compile errors (`BindPluginDialogs`, `Confirm`, `ShowModal` on `BufferedUiRegistry`).

- [ ] **Step 3: The registry**

In `src/AcDream.App/Plugins/BufferedUiRegistry.cs`:

After the `_showWindow`/`_hideWindow` fields:

```csharp
    // Plugins' dialogs, drawn by the UI runtime's host. Bound with the
    // window control and unbound with it.
    private PluginDialogHost? _dialogs;

    /// <summary>The most buttons a plugin's dialog may have.</summary>
    internal const int MaximumDialogButtons = 4;

    internal void BindPluginDialogs(PluginDialogHost dialogs)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        lock (_gate)
            _dialogs = dialogs;
    }
```

In `UnbindClientWindowControl`, inside the lock, after `_hideWindow = null;`:

```csharp
            _dialogs = null;
```

After `SetControlVisible(string viewName, string controlName, bool visible)` (the unscoped one):

```csharp
    public bool Confirm(PluginDialog dialog, Action<int> onResult) =>
        Confirm(new PluginUiOwner("unscoped", "Plugin"), dialog, onResult) is not null;

    public bool ShowModal(string viewName, string? ownerView = null) =>
        ShowModal(new PluginUiOwner("unscoped", "Plugin"), viewName, ownerView);
```

After `HidePanel(PluginUiOwner owner, string viewName)`:

```csharp
    public IDisposable? Confirm(PluginUiOwner owner, PluginDialog dialog, Action<int> onResult)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(onResult);
        ArgumentNullException.ThrowIfNull(dialog.Title);
        ArgumentNullException.ThrowIfNull(dialog.Message);
        IReadOnlyList<string> buttons = dialog.EffectiveButtons;
        if (buttons.Count is < 1 or > MaximumDialogButtons)
        {
            throw new ArgumentException(
                $"A dialog has 1 to {MaximumDialogButtons} buttons, not {buttons.Count}.", nameof(dialog));
        }
        if (buttons.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Every dialog button needs a caption.", nameof(dialog));

        PluginDialogHost? dialogs;
        UiElement? ownerWindow = null;
        lock (_gate)
        {
            dialogs = _dialogs;
            if (dialog.OwnerView is { } ownerView
                && FindRegistrationLocked(owner, ownerView) is { WindowCleanup: not null, Element: { } element })
                ownerWindow = element;
        }
        return dialogs?.Confirm(dialog.Title, dialog.Message, [.. buttons], ownerWindow, onResult);
    }

    public bool ShowModal(PluginUiOwner owner, string viewName, string? ownerView = null)
    {
        PluginDialogHost? dialogs;
        Func<string, bool>? show;
        string? windowName = null;
        UiElement? panel = null;
        UiElement? ownerWindow = null;
        lock (_gate)
        {
            dialogs = _dialogs;
            show = _showWindow;
            if (FindRegistrationLocked(owner, viewName) is { WindowCleanup: not null, Element: { } element } mounted)
            {
                windowName = WindowNameFor(mounted.Owner, mounted.Descriptor);
                panel = element;
            }
            if (ownerView is not null
                && FindRegistrationLocked(owner, ownerView) is { WindowCleanup: not null, Element: { } ownerElement })
                ownerWindow = ownerElement;
        }
        if (dialogs is null || show is null || windowName is null || panel is null || !show(windowName))
            return false;
        dialogs.ShowModal(panel, ownerWindow);
        return true;
    }
```

(`[.. buttons]` copies the captions, so a plugin changing its list later changes nothing on screen.)

- [ ] **Step 4: The runtime**

In `src/AcDream.App/UI/RetailUiRuntime.cs`:

Fields, after `private IDisposable? _pluginThemeFollow;`:

```csharp
    /// <summary>Plugins' Confirm boxes and modal windows.</summary>
    private PluginDialogHost? _pluginDialogs;
    /// <summary>A fresh retail confirmation layout for a plugin's Confirm box; null until the dialog catalog resolves.</summary>
    private Func<ImportedLayout?>? _pluginDialogLayout;
```

In `MountDialogFactory`, directly after `DialogFactory = new RetailDialogFactory(Host.Root, CreateLayout);`:

```csharp
        // Plugins' Confirm boxes borrow the confirmation layout's art.
        _pluginDialogLayout = () => CreateLayout(RetailDialogType.Confirmation);
```

In `MountPlugins`, at the end of the `if (_pluginThemes is null) { … }` block:

```csharp
            _pluginDialogs = new PluginDialogHost(
                Host.Root,
                () => _pluginDialogLayout?.Invoke(),
                _bindings.Assets.DefaultFont);
            _bindings.Plugins.BindPluginDialogs(_pluginDialogs);
```

In the tick, directly after `DialogFactory?.Tick();`:

```csharp
        _pluginDialogs?.Tick();
```

In the shutdown transaction, directly after `_bindings.Plugins?.UnbindClientWindowControl();`:

```csharp
                // Open plugin dialogs are answered with their last button.
                _pluginDialogs?.Dispose();
                _pluginDialogs = null;
```

If `ImportedLayout` or `RetailDialogType` doesn't resolve in `RetailUiRuntime.cs`, use the namespace the file already uses for them in `MountDialogFactory`; that method names both unqualified.

- [ ] **Step 5: Run the tests and the build**

Run both Verify commands. Expected: all pass, `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/Plugins/BufferedUiRegistry.cs src/AcDream.App/UI/RetailUiRuntime.cs tests/AcDream.App.Tests/Plugins/BufferedUiRegistryDialogTests.cs
git commit -m "feat(plugins): Confirm and ShowModal in the client

The registry checks a dialog's buttons, resolves owner windows among the
plugin's own mounted windows and forwards to the runtime's dialog host,
which the runtime ticks and disposes (answering open dialogs).

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 6: Docs

**Goal:** `docs/plugin-api.md` documents `Confirm`, `ShowModal` and the window-modal rule.

**Files:**
- Modify: `docs/plugin-api.md` (new `## Dialogs` section directly before `## Canvases`)

**Acceptance Criteria:**
- [ ] The section covers `PluginDialog` fields; buttons 1–4, default OK/Cancel and the throw; Enter/Escape/×/owner closing/unload results; exactly one answer on the tick thread; queueing per owner; the owner rules (own window, id or title; unknown or hidden → screen, blocks nothing); the looks; `ShowModal` and when it ends; return values on a host without a window.
- [ ] Every sentence matches the code in `PluginDialogHost`, `BufferedUiRegistry`, `UiPluginDialog` and its subclasses.

**Verify:** `grep -n "## Dialogs" docs/plugin-api.md` → one line, before `## Canvases`.

**Steps:**

- [ ] **Step 1: Add the section**

Insert directly before `## Canvases`:

````markdown
## Dialogs

```csharp
bool shown = host.Ui.Confirm(
    new PluginDialog(
        "Delete profile",
        $"Delete the profile \"{name}\"? This cannot be undone.",
        Buttons: ["Delete", "Keep"],
        OwnerView: "main"),
    index =>
    {
        if (index == 0)
            DeleteProfile(name);
    });

host.Ui.ShowModal("profile-editor", ownerView: "main");
```

Dialogs are **window-modal**: a dialog blocks only its owner window. The
owner draws dimmed, and a click on it does nothing but flash the dialog and
bring it to the front. Every other window, chat, movement and the game's
hotkeys work as usual. There is no way for a plugin to block the whole
screen.

`Confirm` shows a message box and calls back with the index of the button
the player picked.

- `Buttons` lists one to four captions, left to right; null means OK and
  Cancel. No buttons, more than four, or a blank caption throws
  `ArgumentException`.
- Enter picks the first button. Escape, the box's close button, the owner
  window closing and your plugin unloading all answer with the **last**
  button, so make the last one the safe choice.
- `OwnerView` is the id or title of one of your own windows. The box is
  centred on that window, follows it if it moves, and blocks it. Without one,
  or with a window that doesn't exist or is closed at the time, the box is
  centred on the screen and blocks nothing.
- The callback runs exactly once per dialog, on the thread that calls your
  tick, after the box has gone. Call `Confirm` from that thread too.
- Several dialogs for the same window wait their turn and show one at a
  time. Dialogs for different windows show together.
- The box takes the keyboard focus, so Enter and Escape reach it, and gives
  it back when it closes. Any other key goes to the game as usual.
- It looks like the client's own confirmation box, unless the owner window
  uses `theme="plugin"` and the player has picked a plugin theme. Then it is
  drawn in the theme's colours, with a close button in its title bar. The
  client's box has no title bar, so it shows the title as a gold first line.

`ShowModal` shows one of your windows, as `ShowPanel` does, and blocks
`ownerView` until that window is hidden again, by `HidePanel` or its close
button. Showing a window that your own markup keeps hidden ends the block at
once. Without an owner, or with the window itself as the owner, it is just
`ShowPanel`.

On a host without a window, `Confirm` and `ShowModal` return `false` and
the callback is never called.
````

- [ ] **Step 2: Check it against the code**

Re-read every sentence against `PluginDialogHost.cs`, `BufferedUiRegistry.cs`, `UiPluginDialog.cs`, `UiDrawnPluginDialog.cs` and `UiRetailPluginDialog.cs`. Where they differ, fix the docs, not the code, unless the code breaks a Global Constraint; in that case report it.

- [ ] **Step 3: Commit**

```bash
git add docs/plugin-api.md
git commit -m "docs(plugins): Confirm and ShowModal dialogs

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 7: Series gate

**Goal:** The Release build is clean. App, Plugin and UI.Abstractions tests have no new failures; Core plugin tests have none beyond Task 1's baseline. Every commit builds.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors, 0 warnings.
- [ ] App + Plugin failures ⊆ `mc-base.txt` (162 names). UI.Abstractions failures ⊆ `mc-uiabs-base.txt` (empty). Core `AcDream.Core.Tests.Plugins` failures ⊆ `mc13-core-base.txt`.
- [ ] Each of the series' commits builds and passes its dialog test filters on its own.
- [ ] Nothing under `docs/superpowers/` on the branch.

**Verify:** all three `comm -13` comparisons print nothing.

**Steps:**

- [ ] **Step 1: Release build and tests**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
T=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC
norm() { grep -E '^[[:space:]]+Failed [A-Za-z]' | sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' | sort -u; }
dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': error|: warning|Warning\(s\)|Error\(s\)' | sort -u
for p in tests/AcDream.App.Tests/AcDream.App.Tests.csproj tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj; do
  dotnet test "$p" -c Release 2>&1
done | norm > $T/mc13-after.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/mc13-uiabs-after.txt
dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj -c Release --filter "FullyQualifiedName~AcDream.Core.Tests.Plugins" 2>&1 | norm > $T/mc13-core-after.txt
echo "new App/Plugin:"; comm -13 $T/mc-base.txt $T/mc13-after.txt
echo "new UI.Abstractions:"; comm -13 $T/mc-uiabs-base.txt $T/mc13-uiabs-after.txt
echo "new Core plugins:"; comm -13 $T/mc13-core-base.txt $T/mc13-core-after.txt
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```

Expected: `0 Warning(s)`, `0 Error(s)`, and nothing after any "new" line. If `$T/mc-base.txt` is missing, rebuild it from 391e709a the same way (detached worktree, same `norm`) and say so. If `$T/mc13-core-base.txt` is missing, rebuild it from ce1bea87.

- [ ] **Step 2: Each commit builds alone**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
for c in $(git rev-list --reverse ce1bea87..feat/markup-controls); do
  git worktree add -q --detach .worktrees/mc-commit-check $c
  (cd .worktrees/mc-commit-check \
    && dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': error|: warning|Warning\(s\)|Error\(s\)' | sort -u | sed "s/^/$(git rev-parse --short $c) build /" \
    && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --no-build \
         --filter "FullyQualifiedName~Dialog|FullyQualifiedName~WindowBlock|FullyQualifiedName~AcDream.App.Tests.UI.Markup" 2>&1 \
       | grep -E 'Passed!|Failed!|No test matches' | sed "s/^/$(git rev-parse --short $c) app /" \
    && dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj -c Release --no-build \
         --filter "FullyQualifiedName~ScopedUiRegistry" 2>&1 \
       | grep -E 'Passed!|Failed!' | sed "s/^/$(git rev-parse --short $c) core /")
  git worktree remove --force .worktrees/mc-commit-check
done
```

Expected: every commit `0 Warning(s)`/`0 Error(s)` and `Passed!` lines. The docs commit runs the same filters as the one before it.

- [ ] **Step 3: Branch contents**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
git log --oneline ce1bea87..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```

Expected: the 6 commits of Tasks 1–6 (plus any review-fix commits), and "no superpowers docs".

No commit.
