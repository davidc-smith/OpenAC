# Markup controls, plan 16: fork merge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring `feat/markup-controls` (a585d80d, built on upstream v0.1.26 391e709a) into fork `main` (a5db2384) with one merge commit. Then add the fork-only integration from spec §15:
- the new controls work as flex items;
- `<expander>` reflows inside flex;
- `<split>` panes can be flex containers;
- the new controls take the modern theme's shapes and title weight;
- contract `0.1.26-fork.5`.

**Architecture:**
- **Where the work happens.** All work is on an integration branch `merge/markup-controls-fork` in its own worktree (`.worktrees/mc-fork`), cut from `main`. The main checkout stays untouched until the last task. That task fast-forwards `main`, so `main` gets exactly "merge commit, then fork commits".
- **The merge.** A trial merge (made while planning, then aborted) conflicts in 16 files. Task 2 lists the resolution for every hunk. Rule: where the fork and the branch solved the same problem, keep the fork's version and adapt the branch's callers.
- **Shift+wheel.** Plan 15's `RetainedWheel` (a Shift+sideways tick read as vertical) is dropped. The fork's `UiRoot.OnScroll(dx, dy, shift)` already routes horizontal steps. Task 3 extends its existing "a Shift step nothing horizontal takes scrolls vertically" rule to the sideways ticks macOS sends.
- **Flex.** Every new element already passes through `AddElement`'s tail, so it becomes a flex item once the merge passes `flex` through. Task 4 gives each one a content size in `MarkupContentSize`. Task 5 stops a flex-placed expander from moving its siblings and lets the container reflow instead. Task 6 proves that a `<group layout>` pane, which the fork already lays out as a flex root, works inside `<split>`.
- **Theme.** Tasks 7 and 8 draw the new controls' themed state with the fork's `PluginUiStyle` shapes (rounded surfaces, focus ring and glow, chevrons, popup shadow), as the fork's existing controls do. The themed Confirm box title takes the SemiBold title font, as fork window titles do.

**Status:** DONE 2026-10-22. Merge 54d19bb (a5db2384 + a585d80d); fork commits to 22640176; main fast-forwarded and pushed (a5db2384..22640176). Gate PASS (0 warnings outside external/, failures ⊆ 171-name fork baseline, all first-parent commits build). Live check passed; one live defect fixed in 22640176 (flex <number> inner field anchored at zero baseline) and re-checked OK. Contract 0.1.26-fork.5.

**Tech Stack:** C# / .NET 10, xUnit, Silk.NET input, the fork's in-house `FlexLayout` and `PluginUiStyle`.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` §15 (on `docs/markup-controls-spec`). Also read the fork's flex spec, `docs/superpowers/specs/2026-10-05-plugin-flex-layout-design.md` (branch `docs/flex-layout-spec`), and `docs/plugin-ui-markup.md` "Flex layout" on fork main.

## Global Constraints

- Toolchain: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` before every dotnet command.
- Work only in `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/mc-fork` (branch `merge/markup-controls-fork`). Never commit in the main checkout until Task 13.
- Never use bare `git stash`. Use WIP commits.
- Release build: `dotnet build AcDream.slnx -c Release` gives 0 warnings and 0 errors after every commit.
- Tests: App+Plugin failures must be a subset, by name, of the fork-main baseline taken in Task 1 (`fm-base.txt`). UI.Abstractions failures must be a subset of `fm-uiabs-base.txt`.
- Failure-name recipe (plans 14/15): `norm() { grep -E '^[[:space:]]+Failed [A-Za-z]' | sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' | sort -u; }`. Compare with `comm -13 base after`.
- Nothing under `docs/superpowers/` lands on `main` or on the integration branch. Plans and specs live only on `docs/markup-controls-spec`.
- Code style: match the surrounding file (comment density, naming, `///` summaries on new members).
- Fork contract: bump `ForkContractVersion` in `src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj` from `0.1.26-fork.4` to `0.1.26-fork.5`. The user wrote "<Version>", but fork.2–fork.4 all bumped `ForkContractVersion`. `Directory.Build.props` `<Version>` stays `0.1.26-fork.1`, the app release version. Keep the per-feature "needs contract" notes style in `docs/plugin-api.md`.
- Headless test metrics (no dat font): 7 points per character, 14-point lines, 24-point controls.
- Live client: never launch one before the user says they are available. Use local ACE 10.10.20.20 only, never Dreamweave.
- Commit trailer on every commit: `Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC`

**User decisions (already made):**
- Merge `feat/markup-controls` into fork main. Fork-only integration goes in commits on main after the merge (spec §15).
- Contract is `0.1.26-fork.5`, because the dungeon floorplan work used fork.3 and fork.4. Note it in `docs/plugin-api.md` and update memory `fork-contract-version.md`.
- Take a fresh baseline from fork main before merging. The 162-name `mc-base.txt` is the upstream-branch baseline and doesn't apply.
- Reconcile plan 15's Shift+sideways fix with the fork's horizontal scrolling. On the fork, Shift+wheel may be meant to scroll sideways.
- ControlsDemo builds and passes on fork main.
- "Deployed" means pushed: push `main` and the docs branch at the end.
- Ask before launching any client. On fork main, ask whether Aethrix and Rynth should load too.

**Decisions taken in planning (flag to the user for veto when reporting):**
1. **Shift+wheel.** A Shift+sideways tick (macOS mouse wheel) first tries horizontal scrolling. If nothing horizontal takes it, it scrolls vertically. This settles parked item 1: a pure-sideways trackpad or tilt tick with Shift now scrolls a `scroll="x"` group sideways, and steps a focused `<number>` only where nothing horizontal is under the pointer. A focused number inside a `scroll="x"` group loses Shift+wheel to the group; the docs say so.
2. **macOS docs note.** It moves from under Number to the general wheel paragraph (parked item 2).
3. **"Shelf at the right screen edge" (parked item 3).** That paragraph doesn't exist on fork main: the fork's dock docs replaced it. Task 2 verifies with grep. The upstream branch keeps the wrong line until an upstream PR is prepared. Nothing in this plan touches `feat/markup-controls`.
4. **SemiBold.** On the fork the title weight is used only for titles: the window title, the title bar and the dock panel title. So only the themed Confirm box title takes it. List column headers and expander headers stay in the regular weight.
5. **Expander in flex.** Its `h` is the expanded height. Collapsed, it is capped at its 18-point header, so neither `grow` nor cross-axis stretch opens it.
6. **Split panes.** A `<group layout>` pane lays out in whatever size the split gives it. Its content minimum doesn't feed the split's `min`.

---

## File Structure

| File | Change |
|---|---|
| merge commit, 16 conflicted files | Resolutions in Task 2 |
| `src/AcDream.App/UI/UiRoot.cs` | Task 3: Shift+sideways falls back to vertical |
| `src/AcDream.App/UI/MarkupContentSize.cs` | Task 4: sizes for checkbox, radio, number, hotkey, colour, separator, split; Task 5: expander |
| `src/AcDream.App/UI/UiFlexBox.cs` | Task 5: collapsed state as a measured input; collapse cap |
| `src/AcDream.App/UI/UiMarkupExpander.cs` | Task 5: `FlexPlaced`, `ExpandedHeight` |
| `src/AcDream.App/UI/MarkupDocument.cs` | Task 5: `AddFlexItem` expander hookup |
| `src/AcDream.App/UI/UiCheckBoxGlyph.cs`, `UiRadioGlyph.cs`, `UiMarkupCheckbox.cs`, `UiMarkupRadio.cs`, `UiMarkupNumber.cs`, `UiMarkupHotkey.cs`, `UiMarkupColor.cs` | Task 7: themed shapes |
| `src/AcDream.App/UI/UiMarkupList.Header.cs`, `UiMarkupList.Tree.cs`, `UiMarkupExpanderHeader.cs`, `UiMarkupSplitBar.cs`, `UiMarkupContextMenu.cs`, `UiMarkupColorPopup.cs`, `UiDrawnPluginDialog.cs`, `PluginDialogHost.cs` | Task 8: themed shapes, SemiBold dialog title |
| `samples/AcDream.Plugins.FlexDemo/controls.xml`, `FlexDemoPlugin.cs` | Task 9: "Flex Controls" window |
| `src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj`, `docs/plugin-api.md` | Task 10: fork.5 |
| `docs/plugin-ui-markup.md` | Tasks 2–6, 9: merged docs, flex notes for the new elements |
| tests: `UiRootScrollTests.cs` (or the existing scroll test file), `MarkupNumberTests.cs`, new `MarkupFlexControlsTests.cs`, `PluginThemeControlTests.cs`, `FlexDemoSampleTests.cs` | per task |

---

### Task 1: Fork-main baseline and integration worktree

**Goal:** A failure-name baseline from fork main as it stands (a5db2384 or later), and a worktree on `merge/markup-controls-fork` cut from it.

**Files:** none in the repo. Baseline files go in `/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/` (`$T`).

**Acceptance Criteria:**
- [ ] `git log -1 main` is recorded in the task report. If main moved past a5db2384, the report says so and lists the new commits.
- [ ] `$T/fm-base.txt` and `$T/fm-uiabs-base.txt` exist, built from `.worktrees/mc-fork` at main's head before any merge.
- [ ] The Release build of fork main gives 0 warnings. If it doesn't, stop and report: the gate assumes a clean base.
- [ ] `.worktrees/mc-fork` exists on branch `merge/markup-controls-fork` at main's head.

**Verify:** `wc -l $T/fm-base.txt $T/fm-uiabs-base.txt && git -C .worktrees/mc-fork rev-parse --abbrev-ref HEAD` → two counts and `merge/markup-controls-fork`.

**Steps:**

- [ ] **Step 1: Create the worktree**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git log --oneline -3 main
git worktree add -b merge/markup-controls-fork .worktrees/mc-fork main
```

- [ ] **Step 2: Baseline build and tests**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
T=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/mc-fork
norm() { grep -E '^[[:space:]]+Failed [A-Za-z]' | sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' | sort -u; }
dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': error|: warning|Warning\(s\)|Error\(s\)' | sort -u
for p in tests/AcDream.App.Tests/AcDream.App.Tests.csproj tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj; do
  dotnet test "$p" -c Release 2>&1
done | norm > $T/fm-base.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/fm-uiabs-base.txt
wc -l $T/fm-base.txt $T/fm-uiabs-base.txt
git status --short
```

Expected: `0 Warning(s)`, `0 Error(s)`. The count was 70 on a Mac after the 0.1.26 sync (memory `upstream-sync-0-1-26`); any count is fine, it is the baseline. Revert lock files that only a restore changed (`git checkout -- '*.lock.json'`).

No commit in this task.

---

### Task 2: Merge `feat/markup-controls` with the conflict resolutions

**Goal:** One `--no-ff` merge commit on `merge/markup-controls-fork` that resolves all 16 conflicted files as below. It builds with 0 warnings, adds no new failures, and runs the ControlsDemo sample tests green.

**Files:** (conflicts) `AcDream.slnx`, `docs/plugin-api.md`, `docs/plugin-ui-markup.md`, `src/AcDream.App/Input/AppHotkeyRegistry.cs`, `src/AcDream.App/UI/MarkupDocument.cs`, `src/AcDream.App/UI/PluginMarkupTheme.cs`, `src/AcDream.App/UI/PluginUiThemeSettings.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs`, `src/AcDream.App/UI/RetainedUiInputBinding.cs`, `src/AcDream.App/UI/UiMarkupList.cs`, `src/AcDream.App/UI/UiMarkupToggle.cs`, `src/AcDream.App/UI/UiRoot.cs`, `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs`, `tests/AcDream.App.Tests/AcDream.App.Tests.csproj`, `tests/AcDream.App.Tests/UI/RetainedUiInputBindingTests.cs`, `tests/AcDream.App.Tests/UI/SilkRetainedMouseScrollTests.cs`; plus `tests/AcDream.App.Tests/Input/PluginKeyChordBridgeTests.cs` (semantic follow-up).

**Acceptance Criteria:**
- [ ] `git log -1 --format=%P` shows two parents: main's head and `a585d80d`.
- [ ] No `<<<<<<<`, `=======` or `>>>>>>>` markers anywhere: `git grep -nE '^(<<<<<<<|=======|>>>>>>>)( |$)'` prints nothing.
- [ ] `RetainedWheel` no longer exists (`git grep -n RetainedWheel` prints nothing). `SilkRetainedMouseSurface.OnScroll` keeps the fork's dominant-axis filter.
- [ ] Release build: 0 warnings, 0 errors.
- [ ] App+Plugin failures ⊆ `fm-base.txt`. UI.Abstractions failures ⊆ `fm-uiabs-base.txt`.
- [ ] `FullyQualifiedName~ControlsDemoSample|FullyQualifiedName~PluginThemeControl|FullyQualifiedName~MarkupFlex|FullyQualifiedName~FlexDemoSample|FullyQualifiedName~PluginKeyChordBridge` all pass.
- [ ] `grep -n 'right screen edge' docs/plugin-ui-markup.md` prints nothing.

**Verify:** the `comm -13` comparisons print nothing, and the filtered test run says `Passed!`.

**Steps:**

- [ ] **Step 1: Start the merge**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/mc-fork
git merge --no-ff --no-commit feat/markup-controls
git diff --name-only --diff-filter=U
```

Expected: the 16 files listed above. If the list differs, main moved; resolve any new file by the same rule (keep the fork's version, adapt the branch's callers) and say so in the report.

- [ ] **Step 2: Resolve each file**

**`AcDream.slnx`:** keep both sides. The four fork sample projects (CanvasDemo, WorldMarkersDemo, FlexDemo, ThemeGallery) stay, then add `samples/AcDream.Plugins.ControlsDemo/AcDream.Plugins.ControlsDemo.csproj`.

**`tests/AcDream.App.Tests/AcDream.App.Tests.csproj`:** keep both. Keep the fork's `RynthConfigSandbox.cs` compile item and the `</ItemGroup>` / `<ItemGroup>` it closes and opens. Put the branch's ControlsDemo `ProjectReference`, with its comment, in the ItemGroup after it.

**`src/AcDream.App/Input/AppHotkeyRegistry.cs`:** take HEAD (no `MapKey`). The fork moved the map to `PluginKeyMap.ToSilk`. Then edit `tests/AcDream.App.Tests/Input/PluginKeyChordBridgeTests.cs`: replace both `AppHotkeyRegistry.MapKey(key)` with `PluginKeyMap.ToSilk(key)`. Add `using AcDream.App.Input;` if `PluginKeyMap` isn't already in scope; it is `internal static` in `src/AcDream.App/Input/PluginKeyMap.cs`.

**`src/AcDream.App/UI/PluginUiThemeSettings.cs`:** keep both sides, in this order:

```csharp
    public UiDatFont? ModernTitleFont => _settings.ModernTitleFont;
    public bool HasModernFont => _settings.HasModernFont;

    /// <summary>The palette the panel draws with now, or null under Classic.</summary>
    internal PluginUiPalette? Palette => _settings.Palette;
```

**`src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs`:** keep both records. First the fork's `PluginCanvasPosition` with its summary, then a blank line, then the branch's `MarkupControlKey` with its summary.

**`src/AcDream.App/UI/UiMarkupToggle.cs`:** this hunk is the Classic path; the fork's themed path is `DrawThemed`, untouched. Take the branch side but keep Classic's outline explicit:

```csharp
        UiCaption.Draw(ctx, DatFont, caption, 17f, 0f, Height, color, outline: true);
```

**`src/AcDream.App/UI/PluginMarkupTheme.cs`:** take the branch side of the hunk: the separator, expander, split, `UiField field: ThemeField(...)`, number, hotkey and colour cases. Then add the fork's palette line to the branch's `ThemeField` helper, so fields and number fields still get the fork's rounded field:

```csharp
        panel.AddThemeAction(p => {
            field.ThemePalette = p;
            field.Outline = p is null && fieldOutline;
```

Check that the font `Bind` block above stays the fork's `if (panel.HasModernFont)` form with the branch's new cases (it auto-merged in the trial). The fork's `UiSimpleButton` `button.ThemePalette = p;`, `UiMeter` and the scrollbar `ThemePalette` lines must all still be there.

**`src/AcDream.App/UI/UiRoot.cs`:** keep both, blocker first:

```csharp
        if (BlockerFor(target) is { } blocker)
        {
            // A window-modal dialog owns this window: the press, and its
            // release, go nowhere but flash the dialog.
            _swallowedReleases.Add(btn);
            FlashBlocker(blocker);
            return;
        }

        // Focus may scroll a scrolling ancestor to reveal the target; the
        // press is reported where it landed, so read the position first.
        var sp = target.ScreenPosition;
```

If the branch's code below now declares or reads `target.ScreenPosition` a second time in the same method, use the fork's `sp` (the build reports a duplicate `sp`).

**`src/AcDream.App/UI/RetainedUiInputBinding.cs`:** take HEAD in all three hunks: the `(dx, dy)` summary, the dominant-axis filter in `SilkRetainedMouseSurface.OnScroll`, and `private void OnScroll(int dx, int dy) => Invoke(() => _root.OnScroll(dx, dy, _root.ShiftHeld));`. Delete the branch's `internal static class RetainedWheel` at the end of the file, and the `ShiftHeld()` helper that only it used. Remove any `using` left unused (the build's 0-warning rule catches it only if analyzers flag it, so check by eye).

**`tests/AcDream.App.Tests/UI/RetainedUiInputBindingTests.cs`:** hunk 1: take HEAD (`MouseScrollPassesBothStepsAndTheRootsShiftState`), and drop the branch's two `RetainedWheel` tests and the `ShiftKeyboard` proxy (Task 3 tests the behaviour at `UiRoot`). Hunks 2 and 3: keep exactly one `Scroll` method on the test surface, HEAD's `public void Scroll(int dx, int dy) => _scroll?.Invoke(dx, dy);`, in HEAD's position.

**`tests/AcDream.App.Tests/UI/SilkRetainedMouseScrollTests.cs`:**
- Hunk 1: take HEAD (`root.OnScroll(dx, dy, shift: false)`).
- Hunk 2: take HEAD's three tests (horizontal step, dominant axis, NaN). Keep the branch's zero test, adapted to HEAD's proxy:

```csharp
    [Fact]
    public void ZeroOnBothAxes_invokesNoCallback()
    {
        IMouse mouse = DispatchProxy.Create<IMouse, WheelMouseProxy>();
        var surface = new SilkRetainedMouseSurface(mouse);
        int calls = 0;
        surface.AddScroll((_, _) => calls++);
        ((WheelMouseProxy)mouse).Raise(mouse, new ScrollWheel(0f, 0f));
        Assert.Equal(0, calls);
    }
```

- Hunk 3: take HEAD's three `Raise`/`RaiseHorizontal` overloads.
- Drop `WheelEvent_reportsBothAxisSigns`: its `(0.3, -0.2) → (1, -1)` row contradicts the fork's dominant-axis filter.

**`src/AcDream.App/UI/RetailUiRuntime.cs`:**

Hunk 1 (fields): keep the fork's type for `_pluginAppearance` and add the branch's two dialog fields:

```csharp
    /// <summary>Plugins' Confirm boxes and modal windows.</summary>
    private PluginDialogHost? _pluginDialogs;
    /// <summary>A fresh retail confirmation layout for a plugin's Confirm box; null until the dialog catalog resolves.</summary>
    private Func<ImportedLayout?>? _pluginDialogLayout;
    private PluginAppearanceWindow? _pluginAppearance;
```

Hunk 2 (mount): keep the fork's `BuildWindow` call and pass the branch's arguments by name:

```csharp
                    new PluginMarkupIconResolver(iconResolver, _pluginFileIcons, panel.Owner.Id, panel.PluginDirectory),
                    _pluginThemes,
                    fallbackTitle: panel.Descriptor.Title,
                    controlState: _controlState.ForWindow(panel.WindowName),
                    describeKey: describeKey,
                    textures: markupTextures,
                    sharedControlState: _controlState.Shared);
                UiNineSlicePanel element = window.Frame;
```

The opening `MarkupWindow window = MarkupDocument.BuildWindow(` line above the hunk is the fork's and stays. Check that `describeKey` and `markupTextures`, which the branch declares just above the plugin loop, auto-merged into this method.

Hunk 3 (dispose): save control state first, then the fork's chain:

```csharp
                _controlState?.Save(ignoreBackoff: true);
                // Flushes a pending canvas move through the same store, so first.
                try { _canvasPositions?.Dispose(); }
                finally { _persistence?.Dispose(); }
```

If the branch also changed the code that builds the plugin appearance window (`_pluginAppearance = MarkupDocument.Build("""…`), keep the fork's `PluginAppearanceWindow` construction. On the fork that window isn't markup.

**`src/AcDream.App/UI/MarkupDocument.cs`** (7 hunks):

1. Usings: keep both, `using AcDream.App.UI.Layout.Flex;` and `using AcDream.App.UI.Markup;`.
2. `Build` / `BuildWindow`: keep the fork's two methods and extend both signatures with the branch's parameters after `fallbackTitle`:

```csharp
    public static UiNineSlicePanel Build(
        string xml, object binding, Func<uint, (uint, int, int)> resolve,
        ControlsIni? style = null, UiDatFont? datFont = null,
        IMarkupIconResolver? icons = null, PluginUiThemeSettings? themes = null,
        IMarkupControlState? controlState = null,
        Func<KeyChord, string>? describeKey = null,
        IMarkupTextureService? textures = null, IMarkupControlState? sharedControlState = null)
        => BuildWindow(xml, binding, resolve, style, datFont, icons, themes, fallbackTitle: null,
            controlState, describeKey, textures, sharedControlState).Frame;

    /// <summary>
    /// Builds a markup window and what mounting it needs (see
    /// <see cref="MarkupWindow"/>). <paramref name="fallbackTitle"/> names the
    /// title bar when the markup has no <c>title</c>: the registration's title.
    /// </summary>
    internal static MarkupWindow BuildWindow(
        string xml, object binding, Func<uint, (uint, int, int)> resolve,
        ControlsIni? style = null, UiDatFont? datFont = null,
        IMarkupIconResolver? icons = null, PluginUiThemeSettings? themes = null,
        string? fallbackTitle = null,
        IMarkupControlState? controlState = null,
        Func<KeyChord, string>? describeKey = null,
        IMarkupTextureService? textures = null, IMarkupControlState? sharedControlState = null)
```

Keep the first line of `Build`'s signature from whichever side has it above the hunk. Only the parameter tail and the delegation are shown here. In the body, the branch's setup (e.g. `var recentColors = new MarkupRecentColors(sharedControlState …)`) must appear before the root-element loop. Check that it auto-merged into `BuildWindow`'s body, and move it there if it landed elsewhere.

3. Root loop: keep the fork's block and add the branch's arguments:

```csharp
            AddElement(contentParent, el, binding, resolve, datFont, icons, panel as UiPluginMarkupPanel, themes,
                controlState, describeKey, textures, recentColors, rootFlex);
```

followed unchanged by the fork's `if (rootFlex is not null) { … }`, the bar-width block and `titleBar?.SetFrameWidth(panel.Width);`.

4. `AddElement` signature: the branch's parameters, then the fork's optional flex:

```csharp
        IMarkupControlState? controlState,
        Func<KeyChord, string>? describeKey,
        IMarkupTextureService? textures, MarkupRecentColors recentColors,
        UiFlexBox? flex = null)
```

The `Context()` closure's `AddChild` keeps calling `AddElement(container, child, …, recentColors)` with no flex. Expander content and split panes are not flex containers. A `<group layout>` pane makes its own flex root in the `group` case.

5. Group case:

```csharp
                    AddElement(group, child, binding, resolve, datFont, icons, themedPanel, themes,
                        controlState, describeKey, textures, recentColors, groupFlex);
```

6. Cases: keep both the fork's `case "canvas": { … }` and the branch's `case "contextmenu":` (built onto its owner afterwards).

7. Tail: the branch's block, then the fork's flex item, then the fork's helpers unchanged (`AddFlexItem`, `CollectCanvases`, `AbsoluteGroupSize`):

```csharp
        if (parent.Children.Count > firstChild)
        {
            UiElement built = parent.Children[firstChild];
            if (themedPanel is not null)
                PluginMarkupTheme.Register(themedPanel, built, el);
            if (MarkupContextMenu.Attach(el, built, binding, Context()) is { } contextMenu && themedPanel is not null)
                PluginMarkupTheme.Register(themedPanel, contextMenu, el.Element("contextmenu")!);
            if (flex is not null)
                AddFlexItem(flex, built, el);
        }
    }
```

`MarkupContextMenu.Attach` adds the menu to the owner (`owner.AddChild(menu)`), never to `parent`, and a `<contextmenu>` element builds nothing in `parent`. So a context menu never becomes a flex item. Task 4 tests this.

**`src/AcDream.App/UI/UiMarkupList.cs`** (5 hunks): keep the fork's drawing helpers and use the branch's view/plugin-row model:

1. Items path band: `if (RowBanded(index, selected)) DrawSelectionBand(context, y, contentWidth);`
2. Container: `DrawContainer(context); if (HeaderVisible) DrawHeader(context, columns);`
3. Column rows:

```csharp
            int row = _view.PluginRow(view);
            float y = RowsTop + (view - _topRow) * RowHeight;
            if (RowBanded(row, selected))
                DrawSelectionBand(context, y, contentWidth);
```

4. Button cell: keep the fork's method body (themed `PluginUiStyle.Button` branch and Classic branch). Use the branch's signature `DrawButtonCell(UiRenderContext context, int column, int view, int row, float cellX, float cellW, float y)`, `HasButtonCaption(column, row, …)` and `UiControlState state = ButtonState(view, column);`. Check that `ButtonStateForTest` still compiles; it passes a view index now.
5. Themed scrollbar: the fork's slim thumb inside the rows band:

```csharp
            float button = Math.Min(ScrollButtonExtent, barHeight / 2);
            var (top, size) = UiScrollbar.ThumbRect(_scroll, barTop + button, Math.Max(0, barHeight - 2 * button));
            PluginUiStyle.ScrollThumb(ctx, p, x, top, ScrollbarWidth, size, active: _draggingThumb);
            return;
```

If the fork's thumb-drag hit-testing elsewhere in the file uses `0..Height` while the branch's uses `RowsTop..`, keep the branch's geometry (`RowsTop`/`RowsHeight`) so dragging matches the drawn thumb.

**`docs/plugin-api.md`:** one sentence covering both: canvases and markup hotkey boxes:

```markdown
field, a control reached with Tab, a plugin canvas with keyboard focus), a
hotkey fires only if Ctrl or Alt is part of the chord; otherwise every
letter typed would also be a candidate hotkey press. Clicking a button does not give it keyboard focus. While a
modal dialog is open, a key rebind is being captured
(`InputDispatcher.BeginCapture`), or a markup `<hotkey>` box is recording,
no hotkey fires at all, Ctrl and Alt chords included.
```

**`docs/plugin-ui-markup.md`** (4 hunks):
1. Binding table: keep the fork's `icon file` row, then the branch's `list selected` row (with multi).
2. Element table: the branch's `list` row with the fork's `canvas` row after it. The footer paragraph becomes:

```markdown
Every element except the root also accepts `name` (or `id`), `visible`,
`enabled`, `tooltip`, and `anchor`. The root `panel` accepts `visible` only as
a binding. A `contextmenu` takes none of them. Inside a flex container
elements take the flex item attributes instead of `x`, `y` and `anchor` (see
[Flex layout](#flex-layout)).
```

3. Sections: the fork's whole `## Title bar` section first, then the branch's ControlsDemo paragraph and every new control section (`## Checkbox` through the context menu's last paragraph).
4. Column table: the branch's `check`, `checkbox` rows, then the fork's icon rows with `file`:

```markdown
| `values` | `check`, `checkbox` | yes | `IReadOnlyList<bool>` |
| `onchange` | `check`, `checkbox` | yes | `Action<int>` with the row index; the plugin flips its own value |
| `values` | `icon` | yes | `IReadOnlyList<uint>` or `<int>`; `IReadOnlyList<string>` paths with `iconkind="file"` |
| `iconkind` | `icon` | no | `did` (default), `spell`, `item`, or `file` |
```

- [ ] **Step 3: No markers left, then build**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
git grep -nE '^(<<<<<<<|=======|>>>>>>>)( |$)' ; git grep -n RetainedWheel ; grep -n 'right screen edge' docs/plugin-ui-markup.md
dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': error|: warning|Warning\(s\)|Error\(s\)' | sort -u
```

Expected: the three greps print nothing; `0 Warning(s)`, `0 Error(s)`. Fix compile errors from auto-merged code by the same rule: keep the fork's API and adapt the branch's caller. Record each such fix in the merge commit message.

- [ ] **Step 4: Tests**

```bash
T=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC
norm() { grep -E '^[[:space:]]+Failed [A-Za-z]' | sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' | sort -u; }
for p in tests/AcDream.App.Tests/AcDream.App.Tests.csproj tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj; do
  dotnet test "$p" -c Release 2>&1
done | norm > $T/fm16-merge.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/fm16-merge-uiabs.txt
echo "new App/Plugin:"; comm -13 $T/fm-base.txt $T/fm16-merge.txt
echo "new UI.Abstractions:"; comm -13 $T/fm-uiabs-base.txt $T/fm16-merge-uiabs.txt
```

Expected: nothing after either "new" line. A new failure is a resolution bug: fix it in the merge before committing, and don't commit with it.

- [ ] **Step 5: Commit the merge**

```bash
git add -A
git status --short | grep -v '^M \|^A \|^D \|^R ' || true
git commit -m "Merge branch 'feat/markup-controls': 13 plugin markup controls, dialogs, ControlsDemo

Conflict resolutions: fork APIs kept, branch callers adapted —
BuildWindow takes control state/key names/textures; AddElement passes flex
after the branch's parameters; contextmenu attaches before the flex item;
UiMarkupList keeps the fork's container, band, button and slim thumb drawing
over the branch's view/header geometry; RetainedWheel dropped for the fork's
(dx, dy, shift) routing (Shift+sideways reconciled in the next commit);
AppHotkeyRegistry.MapKey -> PluginKeyMap.ToSilk in the bridge test;
ThemeField sets the fork's field ThemePalette.

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
git log -1 --format='%h %P'
```

---

### Task 3: Shift+sideways wheel falls back to vertical

**Goal:** On the fork, a Shift+sideways tick (what macOS sends for Shift+mouse wheel) scrolls a horizontal group if one is under the pointer, and otherwise is routed as a vertical step. A focused `<number>` then steps ten again, as plan 15 made it do on the upstream branch.

**Files:**
- Modify: `src/AcDream.App/UI/UiRoot.cs` (`OnScroll(int dx, int dy, bool shift)` and its summary)
- Modify: `docs/plugin-ui-markup.md` (Number bullet; wheel paragraph in "Scrolling groups")
- Test: `tests/AcDream.App.Tests/UI/MarkupNumberTests.cs`; the fork's existing UiRoot scroll tests. Find them with `grep -rl 'OnScroll(.*shift' tests/AcDream.App.Tests/UI` and add the two routing tests to that file.

**Acceptance Criteria:**
- [ ] `root.OnScroll(1, 0, shift: true)` over empty screen reaches `WorldScrollFallThrough` as `1`. `root.OnScroll(1, 0, shift: false)` reaches nothing.
- [ ] Over a `scroll="x"` group, `OnScroll(1, 0, shift: true)` scrolls the group sideways and the world gets nothing.
- [ ] A focused number (step 5, value 10) under the pointer with `ShiftHeld = () => true` goes to 60 on `OnScroll(1, 0, shift: true)`.
- [ ] The Number docs bullet no longer describes `RetainedWheel`. The wheel paragraph says what macOS sends and what the fallback does, and warns about a number inside a `scroll="x"` group.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~MarkupNumberTests|FullyQualifiedName~<scroll test class>"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests**

In `MarkupNumberTests.cs`, next to `Number_WheelStepsOnlyWhileFocused`, using its `Mount(CountAttrs, b)` helper and pointer position:

```csharp
    [Fact]
    public void Number_ShiftSidewaysTickStepsTenWhenNothingScrollsSideways()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        root.OnMouseMove(30, 15);
        root.SetKeyboardFocus(number.Field);
        number.ShiftHeld = () => true;
        root.OnScroll(1, 0, shift: true);   // macOS: Shift+wheel arrives as a sideways tick
        Assert.Equal(60, b.Count);
        root.OnScroll(1, 0, shift: false);  // a plain sideways swipe steps nothing
        Assert.Equal(60, b.Count);
    }
```

In the UiRoot scroll test file:

```csharp
    [Fact]
    public void A_shift_sideways_tick_nothing_horizontal_takes_is_a_vertical_step()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var world = new List<int>();
        root.WorldScrollFallThrough += world.Add;
        root.OnMouseMove(400, 300);

        root.OnScroll(1, 0, shift: true);
        root.OnScroll(-1, 0, shift: false);

        Assert.Equal(new[] { 1 }, world);
    }

    [Fact]
    public void A_shift_sideways_tick_scrolls_a_horizontal_group_first()
    {
        var frame = MarkupDocument.Build("""
            <panel x="0" y="0" w="300" h="200">
              <group name="strip" x="0" y="0" w="100" h="40" scroll="x">
                <label x="0" y="0" text="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" />
              </group>
            </panel>
            """, new object(), _ => (0u, 0, 0));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(frame);
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);
        var world = new List<int>();
        root.WorldScrollFallThrough += world.Add;
        var strip = (IUiScrollHost)frame.Children.First(c => c.Name == "strip");
        float before = strip.ScrollArea!.OffsetX;
        root.OnMouseMove(20, 20);

        root.OnScroll(-1, 0, shift: true);

        Assert.NotEqual(before, strip.ScrollArea.OffsetX);
        Assert.Empty(world);
    }
```

If `UiScrollArea` names its horizontal offset differently, use the property the fork's existing horizontal-scroll tests read (`grep -n 'Offset' src/AcDream.App/UI/UiScrollArea.cs`). If those tests already have a mount helper for a `scroll="x"` group, reuse it. The direction (-1 scrolls content toward the end) must match what those tests use.

- [ ] **Step 2: Run, expect failures**

`dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~Number_ShiftSideways|FullyQualifiedName~A_shift_sideways"` → `Number_ShiftSideways…` and `…_is_a_vertical_step` FAIL (value stays 10; world empty). The horizontal-group test may already pass.

- [ ] **Step 3: Implement in `UiRoot.OnScroll`**

```csharp
    public void OnScroll(int dx, int dy, bool shift)
    {
        bool overPopup = PopupHit(MouseX, MouseY) is not null;
        bool taken;
        if (shift && dx == 0 && dy != 0)
        {
            taken = RouteHorizontal(dy) || RouteVertical(dy);
        }
        else if (shift && dy == 0 && dx != 0)
        {
            // macOS sends Shift with a mouse wheel as a sideways tick: it
            // scrolls sideways where something does, else it is the wheel.
            taken = RouteHorizontal(dx) || RouteVertical(dx);
        }
        else
        {
            taken = false;
            if (dx != 0) taken |= RouteHorizontal(dx);
            if (dy != 0) taken |= RouteVertical(dy);
        }
        if (!taken) return;
        // … rest unchanged
```

Add to the method's summary, after "A Shift step that nothing horizontal takes is routed as the vertical step it was.":

```
    /// The same goes for a sideways step with Shift held, which is what macOS
    /// sends for Shift with a mouse wheel: a <c>scroll="x"</c> group under
    /// the pointer takes it, and otherwise it is routed as a vertical step.
```

- [ ] **Step 4: Run, expect pass.** Same filter → `Passed!`. Then run `FullyQualifiedName~Scroll|FullyQualifiedName~RetainedUiInputBinding` → `Passed!`.

- [ ] **Step 5: Docs**

In `docs/plugin-ui-markup.md`, Number section, end the Shift sentence at "Hold Shift for ten steps." and delete the parenthesis "(macOS sends Shift with a mouse wheel as … so it steps ten too.)".

In "Scrolling groups", replace "A sideways swipe, a tilt wheel, or Shift with the wheel scrolls horizontally. Only `scroll="x"` and `scroll="both"` groups take a horizontal step; a Shift step that no such group takes scrolls vertically as before." with:

```markdown
A sideways swipe, a tilt wheel, or Shift with the wheel
scrolls horizontally. Only `scroll="x"` and `scroll="both"` groups take a
horizontal step; a Shift step that no such group takes scrolls vertically as
before. macOS sends Shift with a mouse wheel as a sideways step, and the same
rule applies: it scrolls a horizontal group under the pointer, else it is the
wheel, so Shift+wheel still steps a focused `<number>` by ten. A focused
number inside a horizontally scrolling group loses Shift+wheel to the group.
```

Keep the existing line wrapping style. The sentence after it ("One wheel step moves 48 points …") stays.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/UiRoot.cs docs/plugin-ui-markup.md tests/AcDream.App.Tests/UI/
git commit -m "fix(ui): Shift+sideways wheel scrolls sideways, else steps vertically

macOS sends Shift+mouse wheel as a sideways tick; the fork routed it only
horizontally, so a focused <number> lost Shift+wheel after the merge dropped
RetainedWheel. Same rule as Shift+vertical: horizontal first, then vertical.

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 4: Content sizes for the new controls in flex

**Goal:** Checkbox, radio, number, hotkey, colour, separator and split each take the flex item attributes and get a content size from `MarkupContentSize`. A context menu is never a flex item. (The expander comes in Task 5.)

**Files:**
- Modify: `src/AcDream.App/UI/MarkupContentSize.cs`
- Modify: `docs/plugin-ui-markup.md` (Content sizes table; Item attributes note)
- Create: `tests/AcDream.App.Tests/UI/MarkupFlexControlsTests.cs`

**Acceptance Criteria:**
- [ ] In a `layout="column" align="start"` group, headless:
  - checkbox "AB" is 31 × 20;
  - vertical radio `A|BB` is 39 × 40;
  - number is 120 × 24;
  - hotkey is 120 × 24;
  - colour is 40 × 24;
  - split with `w="200" h="100"` is 200 × 100.
- [ ] A separator is 9 points thick and runs the length of the container: horizontal in a column, vertical in a row (default `align`).
- [ ] `<contextmenu>` inside a flex group, or inside a button in one, adds no flex item. The group's `Flex.Items` counts only real elements.
- [ ] A bound checkbox caption that gets longer reflows its row on the next frame.
- [ ] The number's minimum width is 54 (40 + its 14-point spin).

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~MarkupFlexControlsTests` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** in `tests/AcDream.App.Tests/UI/MarkupFlexControlsTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI;

/// <summary>
/// The markup controls added by the markup-controls series as flex items:
/// their content sizes, separators following the axis, and context menus
/// staying out of the layout. Headless metrics: 7 points a character,
/// 14-point lines, 24-point controls.
/// </summary>
public sealed class MarkupFlexControlsTests
{
    private sealed class Binding
    {
        public bool On { get; set; } = true;
        public Action<bool> SetOn => v => On = v;
        public string Caption { get; set; } = "AB";
        public int Choice { get; set; }
        public Action<int> Choose => v => Choice = v;
        public int Count { get; set; } = 3;
        public Action<int> SetCount => v => Count = v;
        public string Key { get; set; } = string.Empty;
        public Action<string> SetKey => v => Key = v;
        public uint Colour { get; set; } = 0xFF336699;
        public Action<uint> SetColour => v => Colour = v;
        public Action<int> Pick => _ => { };
    }

    private static (UiRoot Root, UiNineSlicePanel Frame) Mount(string body, object binding)
    {
        UiNineSlicePanel frame = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"400\" h=\"500\">{body}</panel>", binding, _ => (0u, 0, 0));
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(frame);
        Frame(root);
        return (root, frame);
    }

    private static void Frame(UiRoot root)
    {
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);
    }

    private static UiElement Find(UiElement root, string name)
    {
        if (root.Name == name) return root;
        foreach (UiElement child in root.Children)
        {
            try { return Find(child, name); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"no element named {name}");
    }

    private static FlexRect Rect(UiElement e) => new(e.Left, e.Top, e.Width, e.Height);

    [Fact]
    public void New_controls_take_their_content_size_in_a_column()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="300" h="480" layout="column" align="start">
              <checkbox name="c" text="AB" checked="{On}" onchange="{SetOn}" />
              <radio name="r" items="A|BB" selected="{Choice}" onchange="{Choose}" />
              <number name="n" value="{Count}" onchange="{SetCount}" />
              <hotkey name="k" value="{Key}" onchange="{SetKey}" />
              <color name="o" value="{Colour}" onchange="{SetColour}" />
              <split name="p" w="200" h="100">
                <group />
                <group />
              </split>
            </group>
            """, new Binding());

        Assert.Equal(new FlexRect(0f, 0f, 31f, 20f), Rect(Find(frame, "c")));
        Assert.Equal(new FlexRect(0f, 20f, 39f, 40f), Rect(Find(frame, "r")));
        Assert.Equal(new FlexRect(0f, 60f, 120f, 24f), Rect(Find(frame, "n")));
        Assert.Equal(new FlexRect(0f, 84f, 120f, 24f), Rect(Find(frame, "k")));
        Assert.Equal(new FlexRect(0f, 108f, 40f, 24f), Rect(Find(frame, "o")));
        Assert.Equal(new FlexRect(0f, 132f, 200f, 100f), Rect(Find(frame, "p")));
    }

    [Fact]
    public void A_number_shrinks_no_narrower_than_a_field_and_its_spin()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="30" h="40" layout="row">
              <number name="n" value="{Count}" onchange="{SetCount}" />
            </group>
            """, new Binding());

        Assert.Equal(54f, Find(frame, "n").Width);
    }

    [Fact]
    public void A_separator_runs_across_a_column_and_down_a_row()
    {
        var (_, frame) = Mount("""
            <group x="0" y="0" w="200" h="100" layout="column">
              <label text="AB" />
              <separator name="h" />
            </group>
            <group x="0" y="120" w="200" h="40" layout="row">
              <label text="AB" />
              <separator name="v" />
            </group>
            """, new Binding());

        var h = (UiMarkupSeparator)Find(frame, "h");
        var v = (UiMarkupSeparator)Find(frame, "v");
        Assert.Equal(new FlexRect(0f, 14f, 200f, 9f), Rect(h));
        Assert.False(h.Vertical);
        Assert.Equal(new FlexRect(14f, 0f, 9f, 40f), Rect(v));
        Assert.True(v.Vertical);
    }

    [Fact]
    public void A_context_menu_is_never_a_flex_item()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="200" h="40" layout="row">
              <contextmenu items="A|B" onselect="{Pick}" />
              <button name="b" text="OK">
                <contextmenu items="C|D" onselect="{Pick}" />
              </button>
            </group>
            """, new Binding());

        var group = (UiFlexGroup)Find(frame, "g");
        Assert.Single(group.Flex.Items);
        Assert.Same(Find(frame, "b"), group.Flex.Items[0].Element);
        Assert.Contains(group.Children, c => c is UiMarkupContextMenu);
        Assert.Equal(new FlexRect(0f, 0f, 38f, 40f), Rect(Find(frame, "b")));
    }

    [Fact]
    public void A_longer_checkbox_caption_reflows_its_row()
    {
        var binding = new Binding();
        var (root, frame) = Mount("""
            <group x="0" y="0" w="300" h="20" layout="row">
              <checkbox name="c" text="{Caption}" checked="{On}" onchange="{SetOn}" />
              <button name="b" text="OK" />
            </group>
            """, binding);
        Assert.Equal(31f, Find(frame, "b").Left);

        binding.Caption = "ABCD";
        Frame(root);

        Assert.Equal(45f, Find(frame, "b").Left);
    }
}
```

The context-menu test's button is 14 + 2 × 12 = 38 wide and is stretched to the row's 40.

- [ ] **Step 2: Run, expect failures.** The filter above → sizes come out 0 (no measure), and the reflow test fails.

- [ ] **Step 3: Implement** in `MarkupContentSize.cs`. Add the constants after `ThemedToggleCaptionX`:

```csharp
    /// <summary>Where a checkbox's or radio button's caption starts: box and gap.</summary>
    internal const float CheckCaptionX = 17f;

    /// <summary>Room after each round radio option's caption.</summary>
    internal const float RadioOptionGap = 8f;

    /// <summary>A colour swatch's width.</summary>
    internal const float ColorWidth = 40f;

    /// <summary>A separator's thickness across its line: the line and room either side.</summary>
    internal const float SeparatorSize = 9f;
```

Add a helper next to `ControlHeightFor`:

```csharp
    /// <summary>The height of a toggle, checkbox or round radio option set in <paramref name="font"/>.</summary>
    internal static float ToggleHeightFor(UiDatFont? font) =>
        MathF.Max(ToggleHeight, MathF.Ceiling(LineHeight(font) + 4f));
```

Make the toggle case use `float height = ToggleHeightFor(toggle.DatFont);`. Then add the cases before `default:`:

```csharp
            case UiMarkupCheckbox checkbox:
                return Fixed(CheckCaptionX + MathF.Ceiling(TextWidth(checkbox.DatFont, text)), ToggleHeightFor(checkbox.DatFont));
            case UiMarkupRadio radio:
                return RadioSize(radio);
            case UiMarkupNumber number:
                return Range(FieldWidth, FieldMinWidth + UiMarkupNumber.SpinWidth, ControlHeightFor(number.Field.DatFont));
            case UiMarkupHotkey hotkey:
                return Range(FieldWidth, FieldMinWidth, ControlHeightFor(hotkey.DatFont));
            case UiMarkupColor:
                return Fixed(ColorWidth, ControlHeight);
            case UiMarkupSeparator:
                // Stretched along the container's cross axis by the default align,
                // it lies across a column and down a row (UiMarkupSeparator.Vertical).
                return Fixed(SeparatorSize, SeparatorSize);
            case UiMarkupSplit:
                return new FlexMeasurement(new FlexSize(ListWidth, ListHeight), new FlexSize(ListMinWidth, ListMinHeight));
```

Add the radio helper after `Measure`:

```csharp
    /// <summary>A radio group's cells: each as wide as its widest caption, one per option along its orientation.</summary>
    private static FlexMeasurement RadioSize(UiMarkupRadio radio)
    {
        IReadOnlyList<string> items = radio.Items;
        float widest = 0f;
        foreach (string item in items)
            widest = MathF.Max(widest, MathF.Ceiling(TextWidth(radio.DatFont, item ?? string.Empty)));
        float cellW = radio.Segmented ? widest + 2f * ButtonPaddingX : CheckCaptionX + widest + RadioOptionGap;
        float cellH = radio.Segmented ? ControlHeightFor(radio.DatFont) : ToggleHeightFor(radio.DatFont);
        int count = Math.Max(1, items.Count);
        return radio.Horizontal ? Fixed(cellW * count, cellH) : Fixed(cellW, cellH * count);
    }
```

Extend `Text` and `Font` so captions, option lists and fonts are measured inputs:

```csharp
        UiMarkupCheckbox checkbox => checkbox.TextSource?.Invoke() ?? checkbox.Text,
        UiMarkupRadio radio => string.Join("\n", radio.Items),
```

```csharp
        UiMarkupCheckbox checkbox => checkbox.DatFont,
        UiMarkupRadio radio => radio.DatFont,
        UiMarkupNumber number => number.Field.DatFont,
        UiMarkupHotkey hotkey => hotkey.DatFont,
```

`string.Join` allocates every frame for a radio in flex. The fork's `A_steady_state_relayout_allocates_nothing` test doesn't use a radio. Leave it, and mention it in the report as a known cost.

- [ ] **Step 4: Run, expect pass.** The same filter, plus `FullyQualifiedName~MarkupFlexTests` (so the toggle refactor changed nothing) → `Passed!`.

- [ ] **Step 5: Docs.** Add these rows to "Content sizes" after `toggle`:

```markdown
| `checkbox` | box, then its caption; tall as a toggle |
| `radio` | one cell per option along its `orientation`, each as wide as the widest caption (box, caption and 8 points; segmented: the caption with 12 points either side) and as tall as a toggle (segmented: as a button) |
| `number` | 120 wide (54 at least: a field and its spin); tall as a field |
| `hotkey` | 120 wide (40 at least); tall as a field |
| `color` | 40 × 24 |
| `separator` | 9 points thick, stretched along the container's cross axis: across a column, down a row |
| `split` | 160 × 80 (60 × 40 at least) |
```

Then add after the "Item attributes" table's paragraph: "A `<contextmenu>` belongs to the element it is inside and is never a flex item."

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/MarkupContentSize.cs tests/AcDream.App.Tests/UI/MarkupFlexControlsTests.cs docs/plugin-ui-markup.md
git commit -m "feat(ui): flex content sizes for the new markup controls

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 5: Expander collapse reflows inside flex

**Goal:** An `<expander>` placed by a flex container doesn't move its siblings when it collapses. It measures as its header, the container lays out again on the next frame, and the space goes back to the layout. Its authored `h` is the expanded height.

**Files:**
- Modify: `src/AcDream.App/UI/UiMarkupExpander.cs` (`FlexPlaced`, `ExpandedHeight`, early path in `ApplyLayout`)
- Modify: `src/AcDream.App/UI/MarkupContentSize.cs` (expander case, `Text`, `Font`, `Collapsed`)
- Modify: `src/AcDream.App/UI/UiFlexBox.cs` (`UiFlexItem`: collapsed as a measured input, `ExpandedMaxHeight`, `SyncCollapse`; `UiFlexBox.SyncHidden` calls it)
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (`AddFlexItem` expander hookup)
- Modify: `docs/plugin-ui-markup.md` (Expander section + Content sizes row)
- Test: `tests/AcDream.App.Tests/UI/MarkupFlexControlsTests.cs`

**Acceptance Criteria:**
- [ ] In a 200 × 300 column with expander `h="100"` then a button: expanded, the expander is (0,0,200,100) and the button sits at y=100. After `Toggle()` and one frame, the expander is (0,0,200,18), its content is hidden, and the button sits at y=18. After a second toggle, back to 100 and 100.
- [ ] With `grow="1"`, the expander fills the spare height while expanded and is 18 while collapsed.
- [ ] In a row (default stretch), a collapsed expander is 18 tall.
- [ ] A bound expander (`expanded="{Open}"`) reflows when the binding flips, after one tick and one frame.
- [ ] An expander in an absolute container still moves its siblings: the existing expander tests pass unchanged.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~MarkupFlexControlsTests|FullyQualifiedName~Expander|FullyQualifiedName~MarkupFlexTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to `MarkupFlexControlsTests`; add `public bool Open { get; set; } = true;` and `public Action<bool> SetOpen => v => Open = v;` to its `Binding`):

```csharp
    private const string ExpanderColumn = """
        <group x="0" y="0" w="200" h="300" layout="column">
          <expander name="e" text="Opts" h="100">
            <label x="4" y="4" text="AB" />
          </expander>
          <button name="b" text="OK" />
        </group>
        """;

    [Fact]
    public void Collapsing_an_expander_in_a_column_gives_its_space_back_to_the_layout()
    {
        var (root, frame) = Mount(ExpanderColumn, new Binding());
        var e = (UiMarkupExpander)Find(frame, "e");
        UiElement b = Find(frame, "b");
        Assert.Equal(new FlexRect(0f, 0f, 200f, 100f), Rect(e));
        Assert.Equal(100f, b.Top);

        e.Toggle();
        Frame(root);
        Assert.Equal(new FlexRect(0f, 0f, 200f, UiMarkupExpander.HeaderHeight), Rect(e));
        Assert.False(e.Content.Visible);
        Assert.Equal(UiMarkupExpander.HeaderHeight, b.Top);

        e.Toggle();
        Frame(root);
        Assert.Equal(100f, e.Height);
        Assert.True(e.Content.Visible);
        Assert.Equal(100f, b.Top);
    }

    [Fact]
    public void A_growing_expander_keeps_its_header_height_while_collapsed()
    {
        var (root, frame) = Mount("""
            <group x="0" y="0" w="200" h="300" layout="column">
              <expander name="e" text="Opts" h="100" grow="1" />
              <button name="b" text="OK" />
            </group>
            """, new Binding());
        var e = (UiMarkupExpander)Find(frame, "e");
        Assert.Equal(276f, e.Height);

        e.Toggle();
        Frame(root);
        Assert.Equal(UiMarkupExpander.HeaderHeight, e.Height);
        Assert.Equal(UiMarkupExpander.HeaderHeight, Find(frame, "b").Top);
    }

    [Fact]
    public void A_collapsed_expander_in_a_row_is_not_stretched()
    {
        var (root, frame) = Mount("""
            <group x="0" y="0" w="300" h="120" layout="row">
              <expander name="e" text="Opts" w="150" h="100" />
              <button text="OK" />
            </group>
            """, new Binding());
        var e = (UiMarkupExpander)Find(frame, "e");
        Assert.Equal(120f, e.Height);

        e.Toggle();
        Frame(root);
        Assert.Equal(UiMarkupExpander.HeaderHeight, e.Height);
    }

    [Fact]
    public void A_bound_expander_reflows_when_its_binding_flips()
    {
        var binding = new Binding();
        var (root, frame) = Mount("""
            <group x="0" y="0" w="200" h="300" layout="column">
              <expander name="e" text="Opts" h="100" expanded="{Open}" onchange="{SetOpen}" />
              <button name="b" text="OK" />
            </group>
            """, binding);
        Assert.Equal(100f, Find(frame, "b").Top);

        binding.Open = false;
        Frame(root);

        Assert.Equal(UiMarkupExpander.HeaderHeight, Find(frame, "b").Top);
    }
```

- [ ] **Step 2: Run, expect failures**, e.g. the button stays at 100, or the expander's own accordion moves it on top of the flex placement.

- [ ] **Step 3: Implement.**

`UiMarkupExpander.cs`: add after `IsRemembered`:

```csharp
    /// <summary>
    /// Set when a flex container places the expander: collapsing then only
    /// hides the content, and the container gives the space back by laying
    /// out again, instead of the expander moving its siblings.
    /// </summary>
    internal bool FlexPlaced { get; set; }

    /// <summary>The height a flex container gives the expander while it is expanded: its authored <c>h</c>.</summary>
    internal float ExpandedHeight { get; set; }
```

In `ApplyLayout`, right after `_laidOutExpanded = expanded;`:

```csharp
        if (FlexPlaced)
        {
            Content.Visible = expanded;
            return;
        }
```

`MarkupContentSize.cs`: add a case before `default:`, plus the `Text`/`Font` arms and a `Collapsed` helper:

```csharp
            case UiMarkupExpander expander:
            {
                // The header's arrow and caption, at the height for its state.
                float min = UiMarkupExpanderHeader.CaptionX + MathF.Ceiling(TextWidth(expander.Header.DatFont, text));
                float height = expander.Expanded ? expander.ExpandedHeight : UiMarkupExpander.HeaderHeight;
                return new FlexMeasurement(new FlexSize(MathF.Max(ListWidth, min), height), new FlexSize(min, height));
            }
```

```csharp
        UiMarkupExpander expander => expander.Header.TextSource?.Invoke() ?? expander.Header.Text,
```

```csharp
        UiMarkupExpander expander => expander.Header.DatFont,
```

```csharp
    /// <summary>Whether a control is an expander showing only its header.</summary>
    internal static bool Collapsed(UiElement element) => element is UiMarkupExpander { Expanded: false };
```

`UiMarkupExpanderHeader.cs` draws its caption at `14f`. Name it so both use one value: `internal const float CaptionX = 14f;`, and replace the literal in `OnDraw` with `CaptionX`.

`UiFlexBox.cs`, in `UiFlexItem`: add a `_collapsed` field next to `_themed`, record it in `Measure`, compare it in `InputsChanged`, and add:

```csharp
    /// <summary>An expander's authored <c>maxh</c>, applied only while it is expanded.</summary>
    internal float? ExpandedMaxHeight { get; set; }

    /// <summary>
    /// Caps a collapsed expander at its header, so neither grow along a
    /// column nor stretch across a row opens it up.
    /// </summary>
    internal void SyncCollapse()
    {
        if (Element is UiMarkupExpander { FlexPlaced: true } expander)
            Node.MaxHeight = expander.Expanded ? ExpandedMaxHeight : UiMarkupExpander.HeaderHeight;
    }
```

```csharp
        _collapsed = MarkupContentSize.Collapsed(Element);
```

```csharp
        || _collapsed != MarkupContentSize.Collapsed(Element)
```

In `UiFlexBox.SyncHidden`, call it for every item before the recursion:

```csharp
        foreach (UiFlexItem item in box.Items)
        {
            item.Node.Hidden = !item.Element.Visible;
            item.SyncCollapse();
            if (item.Inner is { } inner) SyncHidden(inner);
        }
```

`MarkupDocument.AddFlexItem`: after `MarkupFlexAttributes.ReadItem(el, item.Node);`:

```csharp
        if (element is UiMarkupExpander expander)
        {
            // Its h is the expanded height, given only while it is expanded;
            // collapsed it measures as its header (MarkupContentSize).
            expander.FlexPlaced = true;
            expander.ExpandedHeight = item.Node.Height ?? expander.Height;
            item.Node.Height = null;
            item.ExpandedMaxHeight = item.Node.MaxHeight;
        }
```

The expander's `Content` is anchored to all four edges inside the expander, so it follows the size the container gives it. Check this in the first test (its label stays at (4, 4) inside the content).

- [ ] **Step 4: Run, expect pass.** The Verify filter → `Passed!`. The fork's `A_steady_state_relayout_allocates_nothing` must still pass; `Expanded` must not allocate.

- [ ] **Step 5: Docs.** In the Expander section, add a paragraph:

```markdown
Inside a flex container an expander is a flex item: its `h` is the height it
takes while expanded, and collapsed it is just its 18-point header. The
container lays out again when it folds, so the items after it move up and
`grow` items take the space, and the expander doesn't move its siblings itself
as it does in an absolute window. A collapsed expander never grows or
stretches.
```

Add a row to "Content sizes": `| `expander` | its `h` while expanded, 18 while collapsed; 160 wide (its caption and arrow at least) |`.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/UiMarkupExpander.cs src/AcDream.App/UI/UiMarkupExpanderHeader.cs src/AcDream.App/UI/MarkupContentSize.cs src/AcDream.App/UI/UiFlexBox.cs src/AcDream.App/UI/MarkupDocument.cs tests/AcDream.App.Tests/UI/MarkupFlexControlsTests.cs docs/plugin-ui-markup.md
git commit -m "feat(ui): expander collapse reflows inside a flex container

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 6: Split panes as flex containers

**Goal:** A `<split>` pane written as `<group layout="row|column">` lays out its children in the pane's current size, and lays out again when the bar moves. This should already work through the fork's flex-root groups; the task proves it and documents it.

**Files:**
- Test: `tests/AcDream.App.Tests/UI/MarkupFlexControlsTests.cs`
- Modify: `docs/plugin-ui-markup.md` (Split section)
- Modify (only if the test fails): `src/AcDream.App/UI/UiMarkupSplit.cs` and/or `src/AcDream.App/UI/UiFlexGroup.cs`

**Acceptance Criteria:**
- [ ] Split `w="205" h="100" at="100"` with a column pane (`padding="4"`) holding a button: the left pane is a `UiFlexGroup` and the button is (4, 4, 92, 24).
- [ ] The right pane, a row with `grow="1"` button, gives the button the whole 100-point width.
- [ ] With `position="{Pos}"` bound and `Pos` set to 150, after one tick and frame the left button is 142 wide and the right one 50.
- [ ] A split inside a flex column is itself a flex item (Task 4), and its panes still lay out.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~MarkupFlexControlsTests` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the test** (add `public float Pos { get; set; } = 100f;` and `public Action<float> SetPos => v => Pos = v;` to `Binding`):

```csharp
    [Fact]
    public void Split_panes_may_be_flex_containers_that_follow_the_bar()
    {
        var binding = new Binding();
        var (root, frame) = Mount("""
            <group x="0" y="0" w="300" h="300" layout="column">
              <split name="s" w="205" h="100" at="100" position="{Pos}" onchange="{SetPos}">
                <group name="left" layout="column" padding="4">
                  <button name="b1" text="One" />
                </group>
                <group name="right" layout="row">
                  <button name="b2" text="Two" grow="1" />
                </group>
              </split>
            </group>
            """, binding);

        Assert.IsType<UiFlexGroup>(Find(frame, "left"));
        Assert.Equal(new FlexRect(4f, 4f, 92f, 24f), Rect(Find(frame, "b1")));
        Assert.Equal(100f, Find(frame, "b2").Width);

        binding.Pos = 150f;
        Frame(root);

        Assert.Equal(142f, Find(frame, "b1").Width);
        Assert.Equal(50f, Find(frame, "b2").Width);
    }
```

- [ ] **Step 2: Run.** If it passes, go to Step 4. If a pane doesn't follow the bar, the usual cause is that `UiMarkupSplit` sets pane sizes after the pane's `LayoutChildren` ran this frame. Fix it in `UiMarkupSplit` by applying the position before children lay out, e.g. in `OnTick` or at the start of its own `LayoutChildren`. Match how `LayoutAtDefault` sets pane rects, and add one sentence explaining the order.

- [ ] **Step 3: Run, expect pass.**

- [ ] **Step 4: Docs.** In the Split section, add:

```markdown
A pane may be a flex container: `<group layout="column">` (or `row`) as a
pane lays out its children in whatever size the split gives it, and again
whenever the bar moves. The pane's content minimum doesn't limit the bar; set
the split's `min` and `max` for that.
```

- [ ] **Step 5: Commit**

```bash
git add tests/AcDream.App.Tests/UI/MarkupFlexControlsTests.cs docs/plugin-ui-markup.md src/AcDream.App/UI/
git commit -m "test(ui): split panes as flex containers

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

(Use `feat(ui): …` if Step 2 needed a code fix.)

---

### Task 7: Modern shapes for the new input controls

**Goal:** Under a plugin theme, the checkbox, radio, number spin, hotkey box and colour swatch draw with the fork's `PluginUiStyle`, as the fork's toggle, field and tab already do:
- rounded check box with a tick (indeterminate: a dash);
- round radio buttons;
- segmented radio as a rounded track with a selected pill;
- spin chevrons with a pressed face;
- rounded field edges with the focus glow on the hotkey and colour boxes;
- focus rings instead of the gold outline.

Classic drawing does not change.

**Files:**
- Modify: `src/AcDream.App/UI/UiCheckBoxGlyph.cs`, `src/AcDream.App/UI/UiRadioGlyph.cs`, `src/AcDream.App/UI/UiMarkupCheckbox.cs`, `src/AcDream.App/UI/UiMarkupRadio.cs`, `src/AcDream.App/UI/UiMarkupNumber.cs`, `src/AcDream.App/UI/UiMarkupHotkey.cs`, `src/AcDream.App/UI/UiMarkupColor.cs`
- Test: `tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs`

**Acceptance Criteria:**
- [ ] Themed checkbox, checked: `P.Accent` drawn, no `P.Accent` vertex at the box's square top-left corner (rounded). Indeterminate: a `P.Field` dash inside an accent box. Focused: `PluginUiStyle.FocusRingColor(P)` drawn, no gold `(1, 0.82, 0.25)`.
- [ ] Themed radio: every `P.Accent` vertex of the selected option lies within 5.6 points of the glyph's centre (round, not the octagon of fills).
- [ ] Themed segmented radio: the selected pill is `P.Selected`, with no `P.Field` vertex at the control's square corner.
- [ ] Themed number: no `P.Border` vertex right of the field (the spin has no square outline). The chevrons draw in `P.Muted`.
- [ ] Themed hotkey and colour: focused, they draw `PluginUiStyle.FocusGlow(P)`. The hotkey has no `P.Border` vertex at its square corner.
- [ ] `SwitchingToClassicClearsEveryControlsPalette` and the Classic tests of these controls are unchanged and pass.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginThemeControlTests|FullyQualifiedName~MarkupCheckbox|FullyQualifiedName~MarkupRadio|FullyQualifiedName~MarkupNumber|FullyQualifiedName~MarkupHotkey|FullyQualifiedName~MarkupColor"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** in `PluginThemeControlTests`. Add the binding members to its `Binding` class: `public bool? Maybe => null;`, `public Action<bool> SetFlag => _ => { };`, `public int Choice => 1;`, `public Action<int> Choose => _ => { };`, `public int Count => 3;`, `public Action<int> SetCount => _ => { };`, `public string Key => "Ctrl+K";`, `public Action<string> SetKey => _ => { };`, `public uint Colour => 0xFF336699;`, `public Action<uint> SetColour => _ => { };`.

```csharp
    private static bool VertexAt(IEnumerable<(Vector2 Position, Vector4 Color)> v, float x, float y, Vector4 color) =>
        v.Any(p => MathF.Abs(p.Position.X - x) < 0.01f && MathF.Abs(p.Position.Y - y) < 0.01f
            && Vector4.Distance(p.Color, color) < 0.01f);

    private static readonly Vector4 Gold = new(1f, 0.82f, 0.25f, 1f);

    [Fact]
    public void AThemedCheckboxIsARoundedTickBoxWithAFocusRing()
    {
        var (root, panel) = Themed(
            "<checkbox x=\"10\" y=\"10\" w=\"120\" h=\"20\" text=\"Loot\" checked=\"{On}\" onchange=\"{SetFlag}\" />" +
            "<checkbox x=\"10\" y=\"40\" w=\"120\" h=\"20\" text=\"Some\" checked=\"{Maybe}\" onchange=\"{SetFlag}\" />");
        var on = Assert.IsType<UiMarkupCheckbox>(panel.Children[0]);
        var some = Assert.IsType<UiMarkupCheckbox>(panel.Children[1]);
        var v = DrawElement(on);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Accent));
        Assert.False(VertexAt(v, 11f, 14.5f, P.Accent));                  // rounded, no square corner
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(some), P.Field)); // the dash

        root.SetKeyboardFocus(on);
        var focused = DrawElement(on);
        Assert.True(ThemeDrawCapture.HasColor(focused, PluginUiStyle.FocusRingColor(P)));
        Assert.False(ThemeDrawCapture.HasColor(focused, Gold));
    }

    [Fact]
    public void AThemedRadioButtonIsRound()
    {
        var (_, panel) = Themed(
            "<radio x=\"10\" y=\"10\" w=\"120\" h=\"40\" items=\"A|B\" selected=\"{Choice}\" onchange=\"{Choose}\" />");
        var radio = Assert.IsType<UiMarkupRadio>(panel.Children[0]);
        var accent = DrawElement(radio).Where(p => Vector4.Distance(p.Color, P.Accent) < 0.01f).ToList();
        Assert.NotEmpty(accent);
        // Option 1 is the second 20-point cell; its glyph sits at (11, 30 + 4.5).
        var centre = new Vector2(11f + 5.5f, 30f + 4.5f + 5.5f);
        Assert.All(accent, p => Assert.True(Vector2.Distance(p.Position, centre) <= 5.6f));
    }

    [Fact]
    public void AThemedSegmentedRadioIsARoundedTrackWithASelectedPill()
    {
        var (_, panel) = Themed(
            "<radio x=\"10\" y=\"10\" w=\"120\" h=\"24\" items=\"A|B\" orientation=\"horizontal\" style=\"segmented\" selected=\"{Choice}\" onchange=\"{Choose}\" />");
        var radio = Assert.IsType<UiMarkupRadio>(panel.Children[0]);
        var v = DrawElement(radio);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Selected));
        Assert.False(VertexAt(v, 10f, 10f, P.Field));
    }

    [Fact]
    public void AThemedNumberSpinHasChevronsAndNoSquareOutline()
    {
        var (_, panel) = Themed(
            "<number x=\"10\" y=\"10\" w=\"100\" h=\"24\" value=\"{Count}\" onchange=\"{SetCount}\" />");
        var number = Assert.IsType<UiMarkupNumber>(panel.Children[0]);
        var v = DrawElement(number);
        float spinX = number.Left + number.Width - UiMarkupNumber.SpinWidth;
        Assert.DoesNotContain(v, p => p.Position.X > spinX + 0.5f && Vector4.Distance(p.Color, P.Border) < 0.01f);
        Assert.Contains(v, p => p.Position.X > spinX && Vector4.Distance(p.Color, P.Muted) < 0.01f);
    }

    [Fact]
    public void AThemedHotkeyAndColourGlowWhenFocused()
    {
        var (root, panel) = Themed(
            "<hotkey x=\"10\" y=\"10\" w=\"120\" h=\"24\" value=\"{Key}\" onchange=\"{SetKey}\" />" +
            "<color x=\"10\" y=\"40\" w=\"40\" h=\"24\" value=\"{Colour}\" onchange=\"{SetColour}\" />");
        var hotkey = Assert.IsType<UiMarkupHotkey>(panel.Children[0]);
        var colour = Assert.IsType<UiMarkupColor>(panel.Children[1]);
        Assert.False(VertexAt(DrawElement(hotkey), 10f, 10f, P.Border));

        root.SetKeyboardFocus(hotkey);
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(hotkey), PluginUiStyle.FocusGlow(P)));
        root.SetKeyboardFocus(colour);
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(colour), PluginUiStyle.FocusGlow(P)));
    }
```

If a test's focus doesn't reach the control (e.g. `SetKeyboardFocus` on the checkbox doesn't set `_keyboardActivation.Focused`), send `new UiEvent { Type = UiEventType.FocusGained }` to it as `AThemedFieldGlowsWhenFocused` does. Check `HasColor`'s optional tolerance parameter for alpha-carrying colours (`FocusGlow` has W 0.30).

- [ ] **Step 2: Run, expect failures.**

- [ ] **Step 3: Implement.**

`UiCheckBoxGlyph.Draw`, at the top:

```csharp
        if (palette is { } themed)
        {
            if (state is null)
            {
                PluginUiStyle.Surface(ctx, x, y, Size, Size, PluginUiStyle.SmallRadius, themed.Accent, themed.Accent);
                ctx.DrawSmoothLine(x + 3f, y + 5.5f, x + 8f, y + 5.5f, themed.Field, 1.5f);
            }
            else
            {
                PluginUiStyle.Check(ctx, themed, x, y, state == true);
            }
            return;
        }
```

`UiMarkupCheckbox.OnDraw`: draw the gold outline only when `ThemePalette is null`. After the caption, add `if (_keyboardActivation.Focused && ThemePalette is { } p) PluginUiStyle.FocusRing(ctx, p, Width, Height, PluginUiStyle.SmallRadius);`.

`UiRadioGlyph.Draw`, after computing `ring`, `well` and `mark`:

```csharp
        if (palette is not null)
        {
            ctx.FillEllipse(x, y, Size, Size, ring);
            ctx.FillEllipse(x + 1f, y + 1f, Size - 2f, Size - 2f, well);
            if (selected)
                ctx.FillEllipse(x + 3f, y + 3f, Size - 6f, Size - 6f, mark);
            return;
        }
```

`UiMarkupRadio`: in `DrawSegments`, for themed:

```csharp
        if (ThemePalette is { } p)
        {
            PluginUiStyle.Surface(ctx, 0f, 0f, Width, Height, PluginUiStyle.ControlRadius, Fade(p.Field), Fade(p.Border));
            for (int i = 0; i < items.Count; i++)
            {
                var (x, y, w, h) = Cell(i, items.Count);
                if (i == selected)
                    ctx.FillRoundedRect(x + 2f, y + 2f, w - 4f, h - 4f, PluginUiStyle.SmallRadius, Fade(p.Selected));
                ctx.PushClip(x, y, w, h);
                try { UiCaption.DrawCentred(ctx, DatFont, items[i] ?? string.Empty, x, y, w, h, text, outline); }
                finally { ctx.PopClip(); }
            }
            return;
        }
```

In `OnDraw`, the focus: themed uses `PluginUiStyle.FocusRing(ctx, p, Width, Height, Segmented ? PluginUiStyle.ControlRadius : PluginUiStyle.SmallRadius)`, Classic keeps `FocusColor`.

`UiMarkupNumber.OnDraw`, after `LayoutField();` and computing `x` and `half`:

```csharp
        if (ThemePalette is { } p)
        {
            // Ghost spin buttons beside the field: a pressed face, and chevrons.
            Vector4 pressedFace = Fade(PluginUiStyle.Pressed(PluginUiStyle.Hover(p, p.Field)));
            if (_spinDirection > 0)
                ctx.FillRoundedRect(x + 1f, 1f, SpinWidth - 2f, half - 1f, PluginUiStyle.SmallRadius, pressedFace);
            else if (_spinDirection < 0)
                ctx.FillRoundedRect(x + 1f, half, SpinWidth - 2f, Height - half - 1f, PluginUiStyle.SmallRadius, pressedFace);
            Vector4 chevron = Fade(p.Muted);
            float cx = x + (SpinWidth - 6f) * 0.5f;
            float upY = MathF.Floor(half * 0.5f);
            ctx.DrawSmoothLine(cx, upY + 1.5f, cx + 3f, upY - 1.5f, chevron, 1.5f);
            ctx.DrawSmoothLine(cx + 3f, upY - 1.5f, cx + 6f, upY + 1.5f, chevron, 1.5f);
            PluginUiStyle.Chevron(ctx, cx, MathF.Floor(half + (Height - half) * 0.5f) - 1.5f, 6f, 3f, chevron);
            return;
        }
```

`UiMarkupHotkey.OnDraw`: replace the fill and outline with:

```csharp
        if (p is not null)
            PluginUiStyle.Field(ctx, p, Width, Height, Fade(p.Field), IsFocused || IsCapturing);
        else
        {
            ctx.DrawFill(0f, 0f, Width, Height, Fade(ClassicBackground));
            ctx.DrawRectOutline(0f, 0f, Width, Height, Fade(IsFocused || IsCapturing ? focus : ClassicBorder), 1f);
        }
```

`UiMarkupColor.OnDraw`, for themed:

```csharp
        if (p is not null)
        {
            if (MarkupColorValue.Alpha(argb) < 0xFF)
                DrawChecker(ctx, 1f, 1f, w, h, Fade);
            ctx.FillRoundedRect(1f, 1f, w, h, PluginUiStyle.ControlRadius - 1f, Fade(MarkupColorValue.ToVector4(argb)));
            PluginUiStyle.Field(ctx, p, Width, Height, Vector4.Zero, focused);
            return;
        }
```

`PluginUiStyle.Field` skips a fill with `W == 0` (`Surface` checks `fill.W > 0f`), so only the edge and the glow draw over the colour. Compute `focused` before this branch.

- [ ] **Step 4: Run, expect pass**, with the Verify filter.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/ tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs
git commit -m "feat(ui): modern theme shapes for checkbox, radio, number, hotkey, colour

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 8: Modern shapes for lists, expander, split, popups and dialogs

**Goal:** Under a plugin theme:
- the list header sits inside the rounded container without a square band;
- tree and expander arrows are chevrons;
- the split bar is a slim grip pill;
- the context menu and colour picker popups are rounded with the popup shadow and hover colour the fork's `<menu>` popup uses;
- the drawn Confirm box is a rounded, shadowed window with a header band, themed buttons and its title in the SemiBold title font.

**Files:**
- Modify: `src/AcDream.App/UI/UiMarkupList.Header.cs`, `src/AcDream.App/UI/UiMarkupList.Tree.cs`, `src/AcDream.App/UI/UiMarkupExpanderHeader.cs`, `src/AcDream.App/UI/UiMarkupSplitBar.cs`, `src/AcDream.App/UI/UiMarkupContextMenu.cs`, `src/AcDream.App/UI/UiMarkupColorPopup.cs`, `src/AcDream.App/UI/UiDrawnPluginDialog.cs`, `src/AcDream.App/UI/PluginDialogHost.cs`
- Test: `tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs`; the existing dialog tests (`grep -rl UiDrawnPluginDialog tests`)

**Acceptance Criteria:**
- [ ] A themed list with `<column header=…>`: no `P.Field` vertex at the list's square top-left corner. The header's underline is inset by `PluginUiStyle.RowInset`.
- [ ] A themed tree parent row's arrow has at least one vertex off whole-pixel x (a smooth chevron, not pixel fills). Same for a themed expander header's arrow, in `P.Muted`.
- [ ] A themed split bar draws no `P.Field`. Its grip vertices span no more than 24 points along the bar.
- [ ] An open themed context menu draws a shadow vertex (black, W>0) below its rows. A highlighted item draws `PluginUiStyle.Hover(P, P.Field)`, not `P.Selected`.
- [ ] An open themed colour popup draws a shadow vertex outside its box.
- [ ] A themed drawn dialog: its title is drawn in the title font passed in (`PluginDialogHost` passes `panel.ModernTitleFont ?? panel.ModernFont ?? _font`). Its buttons have `ThemePalette` set. It draws no square `P.Border` vertex at (0, 0).
- [ ] Classic drawing of all these is unchanged (existing tests pass).

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginThemeControlTests|FullyQualifiedName~Dialog|FullyQualifiedName~ContextMenu|FullyQualifiedName~MarkupList|FullyQualifiedName~Expander|FullyQualifiedName~Split|FullyQualifiedName~MarkupColor"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** in `PluginThemeControlTests`. Each test follows the existing pattern: build with `Themed(…)`, draw with `DrawElement` (or `root.Draw` for popups, as in `AThemedMenuHasARoundedFaceAndAShadowedPopup`), assert colours and positions. Copy the markup, and the way each existing test opens a popup or dialog, from the matching existing test file:
- context menu: right press and release on the owner, as `MarkupContextMenuTests` does;
- colour popup: click the swatch, as `MarkupColorTests` does;
- dialog: construct `UiDrawnPluginDialog` directly with a palette func, as the dialog tests do.

The assertions are the Acceptance Criteria above, one `[Fact]` per control. For the title font, construct the dialog with two distinct baked fonts (`BundledUiFont.Bake().CreateFont(41)` for the body, `CreateFont(42)` for the title). Assert that sprite segments with texture 42 lie within the title band (y < `TitleHeight`) and that texture 41 draws the message.

- [ ] **Step 2: Run, expect failures.**

- [ ] **Step 3: Implement.**

`UiMarkupList.Header.cs` `DrawHeader`:

```csharp
        if (ThemePalette is null)
        {
            context.DrawFill(0f, 0f, Width, HeaderHeight, BackgroundColor);
            context.DrawFill(0f, HeaderHeight - 1f, Width, 1f, BorderColor);
        }
        else
        {
            // The rounded container shows through; only an inset rule under the captions.
            context.DrawFill(PluginUiStyle.RowInset, HeaderHeight - 1f, Width - 2f * PluginUiStyle.RowInset, 1f, BorderColor);
        }
```

Keep the existing first line's Classic colour if plan 15 changed it. Plan 15's Classic header is the list `BackgroundColor` with a gold underline: keep exactly what the Classic branch draws today, and move it into the `ThemePalette is null` arm.

`UiMarkupList.Tree.cs` `DrawTreeArrow`, at the top:

```csharp
        if (ThemePalette is not null)
        {
            float cy = rowY + RowHeight * 0.5f;
            if (expanded)
                PluginUiStyle.Chevron(context, slotX + 2f, cy - 2f, 7f, 4f, TextColor);
            else
            {
                context.DrawSmoothLine(slotX + 3.5f, cy - 3.5f, slotX + 7f, cy, TextColor, 1.5f);
                context.DrawSmoothLine(slotX + 7f, cy, slotX + 3.5f, cy + 3.5f, TextColor, 1.5f);
            }
            return;
        }
```

`UiMarkupExpanderHeader.OnDraw`: for themed, draw the arrow the same way in `Fade(ThemePalette.Muted)` within the first `CaptionX` points (expanded: `PluginUiStyle.Chevron(ctx, 3f, cy - 2f, 7f, 4f, arrow)`; collapsed: the two lines at x 4.5→8 like the tree). For focus, themed uses `PluginUiStyle.FocusRing(ctx, p, Width, Height, PluginUiStyle.SmallRadius)` and Classic keeps `FocusColor`.

`UiMarkupSplitBar.OnDraw`, themed:

```csharp
        if (ThemePalette is { } p)
        {
            Vector4 grip = Fade(Hot ? p.Accent : p.Border);
            const float GripLength = 24f, GripWidth = 3f;
            if (_owner.Horizontal)
                ctx.FillRoundedRect(MathF.Floor((Width - GripWidth) * 0.5f), MathF.Floor((Height - GripLength) * 0.5f),
                    GripWidth, MathF.Min(GripLength, Height), GripWidth * 0.5f, grip);
            else
                ctx.FillRoundedRect(MathF.Floor((Width - GripLength) * 0.5f), MathF.Floor((Height - GripWidth) * 0.5f),
                    MathF.Min(GripLength, Width), GripWidth, GripWidth * 0.5f, grip);
            if (_focused)
                PluginUiStyle.FocusRing(ctx, p, Width, Height, PluginUiStyle.SmallRadius);
            return;
        }
```

`UiMarkupContextMenu.OnDrawOverlay`: inside the `try`, for themed:

```csharp
            if (p is not null)
            {
                PluginUiStyle.PopupShadow(ctx, _x, _y, _w, _h);
                PluginUiStyle.Surface(ctx, _x, _y, _w, _h, PluginUiStyle.ContainerRadius, background, border);
            }
            else
            {
                ctx.DrawFill(_x, _y, _w, _h, background);
                ctx.DrawRectOutline(_x, _y, _w, _h, border, 1f);
            }
```

Set `highlight` to `p is null ? ClassicHighlight : PluginUiStyle.Hover(p, p.Field)`. Draw it themed with `PluginUiStyle.Row(ctx, _x, top, _w, ItemHeight, highlight)` and Classic as today.

`UiMarkupColorPopup.OnDrawOverlay`: the same frame swap (`PopupShadow` + `Surface(ContainerRadius)` when `p` is not null; Classic unchanged).

`UiDrawnPluginDialog`:
- Add a constructor parameter `UiDatFont? titleFont = null`, stored as `_titleFont = titleFont ?? font`.
- Measure the title with `_titleFont` when computing `Width`: `Func<string, float> measureTitle = _titleFont is { } t ? t.MeasureWidth : measure;`.
- Draw the title with `_titleFont`.
- In `Style`, set `button.ThemePalette = p;`.
- Themed `OnDraw`:

```csharp
        if (p is not null)
        {
            PluginUiStyle.WindowShadow(ctx, Width, Height);
            PluginUiStyle.Surface(ctx, 0f, 0f, Width, Height, PluginUiStyle.WindowRadius, p.Background, p.Border);
            ctx.FillVerticalGradient(0f, 0f, Width, TitleHeight,
                new CanvasCornerRadii(PluginUiStyle.WindowRadius, PluginUiStyle.WindowRadius, 0f, 0f),
                PluginUiStyle.Mix(p.Background, p.Text, 0.06f), PluginUiStyle.Mix(p.Background, p.Text, 0.02f));
            ctx.DrawFill(0f, TitleHeight, Width, 1f, p.Border);
        }
        else
        {
            ctx.DrawFill(0f, 0f, Width, Height, ClassicBackground);
            ctx.DrawFill(0f, 0f, Width, TitleHeight, ClassicTitleBar);
        }
```

then the title and message as today, with `outline: p is null`. `OnDrawAfterChildren` draws the square outline only when `_palette()` is null.

`PluginDialogHost.CreateView`:

```csharp
            return new UiDrawnPluginDialog(
                entry.Title, entry.Message, entry.Buttons, panel.ModernFont ?? _font, () => panel.Palette,
                titleFont: panel.ModernTitleFont ?? panel.ModernFont ?? _font);
```

Check that `PluginUiStyle.Mix` and `CanvasCornerRadii` are reachable from `UiDrawnPluginDialog` (same assembly; `Mix` is `internal static`). `UiPluginMarkupPanel.ModernTitleFont` comes from the fork (Task 2 kept it).

- [ ] **Step 4: Run, expect pass**, with the Verify filter, then the whole `AcDream.App.Tests.UI` namespace once.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/ tests/AcDream.App.Tests/UI/
git commit -m "feat(ui): modern theme shapes for list header, tree, expander, split, popups, dialog

The themed Confirm box title takes the SemiBold title font, as fork window
titles do.

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 9: FlexDemo "Flex Controls" window

**Goal:** The fork-only FlexDemo sample gains a fourth window, "Flex Controls", that puts the new controls in flex. It's what the live check walks:
- a column with a checkbox, a horizontal radio, a number, a separator and two stacked expanders with buttons after them;
- a split whose panes are a column and a row.

The window is themed (`theme="plugin"`) and resizable.

**Files:**
- Create: `samples/AcDream.Plugins.FlexDemo/controls.xml`
- Modify: `samples/AcDream.Plugins.FlexDemo/FlexDemoPlugin.cs` (register `controls`; `ControlsBinding`; summary)
- Modify: `samples/AcDream.Plugins.FlexDemo/AcDream.Plugins.FlexDemo.csproj` only if the other xml files are listed there explicitly (copy them the same way)
- Test: `tests/AcDream.App.Tests/UI/FlexDemoSampleTests.cs`
- Modify: `docs/plugin-ui-markup.md` (the paragraph that describes FlexDemo's windows, if any: `grep -n FlexDemo docs/plugin-ui-markup.md`)

**Acceptance Criteria:**
- [ ] `controls.xml` builds with a test binding that mirrors `ControlsBinding`'s members. Its root has `layout="column"`, `theme="plugin"`, `resizable="true"`, and a title.
- [ ] In the mounted window, collapsing the first expander moves the second expander's top up by its expanded height minus 18.
- [ ] The split's left pane is a `UiFlexGroup`.
- [ ] `dotnet build samples/AcDream.Plugins.FlexDemo -c Release` gives 0 warnings.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~FlexDemoSampleTests` → `Passed!`

**Steps:**

- [ ] **Step 1: Write `controls.xml`**

```xml
<panel title="Flex Controls" layout="column" padding="8" gap="6" w="320" h="420"
       resizable="true" theme="plugin">
  <group layout="row" gap="8" align="center">
    <checkbox text="Loot" checked="{Loot}" onchange="{SetLoot}" />
    <radio items="Slow|Fast" orientation="horizontal" style="segmented"
           selected="{Speed}" onchange="{SetSpeed}" />
  </group>
  <group layout="row" gap="6" align="center">
    <label text="Heal at %" />
    <number value="{HealAt}" onchange="{SetHealAt}" min="0" max="100" step="5" grow="1" />
  </group>
  <separator />
  <expander name="first" text="Buffs" h="80">
    <label x="8" y="6" text="Shown while open" />
  </expander>
  <expander name="second" text="Debuffs" h="80" open="false">
    <label x="8" y="6" text="Folded at start" />
  </expander>
  <split name="split" h="120" at="140" grow="1">
    <group name="left" layout="column" padding="4" gap="4">
      <button text="One" />
      <button text="Two" />
    </group>
    <group name="right" layout="row" padding="4" gap="4">
      <button text="Grow" grow="1" />
    </group>
  </split>
  <label text="{Status}" />
</panel>
```

- [ ] **Step 2: Register it.** In `FlexDemoPlugin`, add `_controls`, register `new PluginPanelDescriptor("controls", "Flex Controls") { IconText = "FC", StartVisible = true }` with `Markup("controls.xml")` and a `ControlsBinding` field, dispose it in `Disable`, and mention the window in the class summary. `ControlsBinding`:

```csharp
    /// <summary>The new markup controls inside flex: what each one last reported.</summary>
    public sealed class ControlsBinding
    {
        public bool Loot { get; private set; } = true;
        public Action<bool> SetLoot => value => { Loot = value; Status = $"Loot {value}"; };
        public int Speed { get; private set; }
        public Action<int> SetSpeed => value => { Speed = value; Status = $"Speed {value}"; };
        public int HealAt { get; private set; } = 60;
        public Action<int> SetHealAt => value => { HealAt = value; Status = $"Heal at {value}%"; };
        public string Status { get; private set; } = "Fold an expander or drag the split";
    }
```

- [ ] **Step 3: Test** in `FlexDemoSampleTests`. Add a `ControlsB` binding class with the same members (that file mirrors names because the sample isn't referenced), then:

```csharp
    [Fact]
    public void The_controls_window_reflows_when_an_expander_folds()
    {
        var (root, window) = Mount("controls.xml", new ControlsB());
        var first = (UiMarkupExpander)FindByName(window.Frame, "first");
        var second = FindByName(window.Frame, "second");
        Assert.IsType<UiFlexGroup>(FindByName(window.Frame, "left"));
        float before = second.Top;

        first.Toggle();
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);

        Assert.Equal(before - (80f - UiMarkupExpander.HeaderHeight), second.Top);
    }
```

Use the file's existing name-lookup helper if there is one; otherwise add a `FindByName` like `MarkupFlexControlsTests.Find`.

- [ ] **Step 4: Build and run**

```bash
dotnet build samples/AcDream.Plugins.FlexDemo -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'
dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~FlexDemoSampleTests
```

- [ ] **Step 5: Commit**

```bash
git add samples/AcDream.Plugins.FlexDemo tests/AcDream.App.Tests/UI/FlexDemoSampleTests.cs docs/plugin-ui-markup.md
git commit -m "feat(samples): FlexDemo Flex Controls window

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 10: Contract 0.1.26-fork.5

**Goal:** Bump `ForkContractVersion` to `0.1.26-fork.5` for the contract surface the merge brings to the fork:
- `IUiRegistry.Confirm` / `ShowModal` and `PluginDialog`;
- `PluginKeyChord.TryParseHotkeyText` / `ToHotkeyText`;
- `IMouseSource` / `IHotkeyFocusSource` `ClaimsKey` and `IsCapturingKeys`.

Note it in `docs/plugin-api.md` in the fork's "needs contract" style.

**Files:**
- Modify: `src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj` (`<ForkContractVersion>`)
- Modify: `docs/plugin-api.md` (Dialogs section; the hotkey text members' paragraph)

**Acceptance Criteria:**
- [ ] `grep -n ForkContractVersion src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj` shows `0.1.26-fork.5`.
- [ ] `Directory.Build.props` `<Version>` is still `0.1.26-fork.1`.
- [ ] `docs/plugin-api.md` says that `Confirm`/`ShowModal` and the `PluginKeyChord` hotkey-text members need contract `0.1.26-fork.5`, and what an older host does (from the branch's own docs: an older host lacks the members, so a plugin built against fork.5 must not load there; `minHostVersion` compares only MAJOR.MINOR.PATCH, so say "build against fork.5 to call them").
- [ ] `dotnet pack src/AcDream.Plugin.Abstractions -c Release -p:UseForkContractVersion=true -o $T/fm16-pack` produces `*.0.1.26-fork.5.nupkg`.

**Verify:** `ls $T/fm16-pack/*.nupkg` → a file ending `0.1.26-fork.5.nupkg`

**Steps:**

- [ ] **Step 1:** Change `<ForkContractVersion>0.1.26-fork.4</ForkContractVersion>` to `0.1.26-fork.5`.
- [ ] **Step 2:** In `docs/plugin-api.md` `## Dialogs`, end the first paragraph that introduces `Confirm` and `ShowModal` with: "`Confirm` and `ShowModal` need contract `0.1.26-fork.5`." Do the same after the paragraph that introduces `PluginKeyChord.TryParseHotkeyText` / `ToHotkeyText`: "They need contract `0.1.26-fork.5`." Match the sentence form of the existing "needs contract" notes (e.g. near "HookItemTypes").
- [ ] **Step 3:** Pack and check, then revert lock-file churn if any.

```bash
T=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC
dotnet pack src/AcDream.Plugin.Abstractions -c Release -p:UseForkContractVersion=true -o $T/fm16-pack 2>&1 | grep -E 'error|warning|Successfully' ; ls $T/fm16-pack
git status --short
```

- [ ] **Step 4: Commit**

```bash
git add src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj docs/plugin-api.md
git commit -m "chore(plugins): fork contract 0.1.26-fork.5 for dialogs and hotkey text

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 11: Gate

**Goal:**
- a clean Release build;
- no new failures by name against the fork-main baseline;
- every first-parent commit after main builds and passes the UI filters;
- ControlsDemo builds and its tests pass;
- no `docs/superpowers/` on the branch.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 warnings, 0 errors.
- [ ] App+Plugin failures ⊆ `fm-base.txt`. UI.Abstractions failures ⊆ `fm-uiabs-base.txt`.
- [ ] Each commit in `git rev-list --first-parent <main>..HEAD` builds with 0 warnings and passes `FullyQualifiedName~AcDream.App.Tests.UI|FullyQualifiedName~ControlsDemoSample|FullyQualifiedName~FlexDemoSample|FullyQualifiedName~PluginKeyChordBridge`, apart from names in `fm-base.txt`.
- [ ] `dotnet build samples/AcDream.Plugins.ControlsDemo -c Release` → 0 warnings.
- [ ] `git diff --stat <main>..HEAD | grep superpowers` prints nothing.

**Verify:** both `comm -13` comparisons print nothing; the per-commit loop shows `0 Warning(s)` and no unexpected failures.

**Steps:**

- [ ] **Step 1: Release build and tests**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/mc-fork
T=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC
norm() { grep -E '^[[:space:]]+Failed [A-Za-z]' | sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' | sort -u; }
dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': error|: warning|Warning\(s\)|Error\(s\)' | sort -u
for p in tests/AcDream.App.Tests/AcDream.App.Tests.csproj tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj; do
  dotnet test "$p" -c Release 2>&1
done | norm > $T/fm16-after.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/fm16-uiabs-after.txt
echo "new App/Plugin:"; comm -13 $T/fm-base.txt $T/fm16-after.txt
echo "new UI.Abstractions:"; comm -13 $T/fm-uiabs-base.txt $T/fm16-uiabs-after.txt
dotnet build samples/AcDream.Plugins.ControlsDemo -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'
git status --short
```

- [ ] **Step 2: Each first-parent commit builds alone**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
BASE=$(git merge-base main merge/markup-controls-fork)
for c in $(git rev-list --reverse --first-parent $BASE..merge/markup-controls-fork); do
  git worktree add -q --detach .worktrees/mc16-commit-check $c
  (cd .worktrees/mc16-commit-check \
    && dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': error|: warning|Warning\(s\)|Error\(s\)' | sort -u | sed "s/^/$(git rev-parse --short $c) build /" \
    && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --no-build \
         --filter "FullyQualifiedName~AcDream.App.Tests.UI|FullyQualifiedName~ControlsDemoSample|FullyQualifiedName~FlexDemoSample|FullyQualifiedName~PluginKeyChordBridge" 2>&1 \
       | grep -E 'Passed!|Failed!|^[[:space:]]+Failed ' | sed "s/^/$(git rev-parse --short $c) test /")
  git worktree remove --force .worktrees/mc16-commit-check
done
```

Expected: `0 Warning(s)` for every commit. A `Failed` line may only name a test that is in `fm-base.txt`.

- [ ] **Step 3: Branch contents**

```bash
git log --oneline --first-parent $BASE..merge/markup-controls-fork
git diff --stat $BASE..merge/markup-controls-fork | grep superpowers || echo "no superpowers docs"
```

No commit in this task.

---

### Task 12: Live check on the fork build

**Goal:** In the real client on the local ACE server, at 2x, the user confirms:
- the merged controls behave on fork main as they did on the branch;
- the fork integration works (flex reflow, modern shapes, Shift+wheel).

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] The user said they were available before the client was launched. They were asked whether Aethrix and Rynth should load, and the launch followed their answer.
- [ ] The client log shows `sample.controls-demo` and `sample.flex-demo` loaded, with no exception lines during the run.
- [ ] The user confirms each item:
  1. ControlsDemo, in Classic and in a modern theme: every window opens, and its controls react as in the plan 14/15 live checks.
  2. In a modern theme, checkbox, radio (round and segmented), number spin, hotkey, colour, list header, tree arrows, expander header, split grip, context menu and colour popup look rounded and consistent with the fork's buttons and fields.
  3. A themed Confirm box (ControlsDemo dialogs window) has a rounded frame, a header band and its title in the bold weight.
  4. Shift+mouse wheel on a focused "Heal at %" steps by 50; over a `scroll="x"` strip (FlexDemo Gallery's recent spells) it scrolls sideways.
  5. FlexDemo "Flex Controls": folding "Buffs" moves "Debuffs" and the split up, and unfolding moves them back. Dragging the split resizes both panes' buttons. Resizing the window reflows the column.

**Verify:** `grep -E 'sample.controls-demo|sample.flex-demo|Exception' <root>/client-run.log`, plus the user's per-item confirmation in the conversation.

**Steps:**

- [ ] **Step 1:** Ask the user whether they are available, and whether Aethrix and Rynth should load. Launch nothing before they answer.
- [ ] **Step 2: Build and install** into a scratch root `R=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/mc16-live-root`, with `R/dev-client.env` a symlink to `~/OpenAC-dev/dev-client.env`, and `R/plugins/sample.controls-demo` and `R/plugins/sample.flex-demo` holding the two samples' Release output (the dll, `.deps.json`, `plugin.json` and the `*.xml` markup).

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
R=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/mc16-live-root
mkdir -p $R/plugins/sample.controls-demo $R/plugins/sample.flex-demo
ln -sf ~/OpenAC-dev/dev-client.env $R/dev-client.env
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/mc-fork
dotnet build src/AcDream.App -c Release --nologo --artifacts-path $R/build/openac 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'
for s in ControlsDemo:sample.controls-demo FlexDemo:sample.flex-demo; do
  p=${s%%:*}; d=${s##*:}
  dotnet build samples/AcDream.Plugins.$p -c Release --nologo 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'
  cp samples/AcDream.Plugins.$p/bin/Release/net10.0/{AcDream.Plugins.$p.dll,AcDream.Plugins.$p.deps.json,plugin.json} $R/plugins/$d/
  cp samples/AcDream.Plugins.$p/*.xml $R/plugins/$d/
done
```

If the user wants Aethrix or Rynth, drop the matching `--no-aethrix` / `--no-rynth` flag. The run script then needs Aethrix's plugin build in its usual place. Ask the user rather than guess where.

- [ ] **Step 3: Launch** in the background, with output to `$R/client-run.log`:

```bash
OPENAC_ROOT=/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/mc-fork OPENAC_DEV_ROOT=$R \
  ~/code/OpenAC/OpenAC-Plugins-Aethrix/tools/run-dev-client.sh --no-build --no-aethrix --no-rynth > $R/client-run.log 2>&1
```

- [ ] **Step 4:** Walk the five items with the user, and record pass or fail per item in this plan's Status line. A failure becomes a new fix task; nothing is fixed inside this task.

---

### Task 13: Land on main, push, record

**Goal:**
- fork `main` points at the integration branch's head;
- `main` and the docs branch are pushed;
- memory records the new state.

**Files:** memory files under `/Users/davidsmith/.claude/projects/-Users-davidsmith-code-OpenAC-OpenAC/memory/` (`markup-controls.md`, `fork-contract-version.md`, `MEMORY.md`); the docs branch plan Status line.

**Acceptance Criteria:**
- [ ] `git -C /Users/davidsmith/code/OpenAC/OpenAC rev-parse main` equals `merge/markup-controls-fork`'s head (fast-forward). If main moved since Task 1, stop and ask the user: re-merge main into the integration branch and re-gate, or merge with `--no-ff`.
- [ ] `git push origin main` and `git push origin docs/markup-controls-spec` succeeded.
- [ ] `.worktrees/mc-fork` removed. Branch `merge/markup-controls-fork` deleted locally, since main now holds it.
- [ ] Memory `fork-contract-version.md` gains a line "2026-10-22: fork.5 for markup controls (dialogs, PluginKeyChord hotkey text, ClaimsKey/IsCapturingKeys), fork main <sha>, pushed". `markup-controls.md` records the merge SHA, the fork commits, the decisions taken in planning, and "Aethrix needs PluginApiPackageVersion 0.1.26-fork.5 + OPENAC_REF bump + repack". `MEMORY.md` index lines updated.
- [ ] The report to the user lists the planning decisions for veto and the Aethrix repack follow-up.

**Verify:** `git -C /Users/davidsmith/code/OpenAC/OpenAC log -1 --oneline origin/main` shows the integration head.

**Steps:**

- [ ] **Step 1: Fast-forward main** (the main checkout must be on `main` and clean):

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git status --short
git merge --ff-only merge/markup-controls-fork
git push origin main
```

- [ ] **Step 2: Docs branch.** Add a `**Status:**` line under this plan's header with the merge and head SHAs and the live-check result, and commit on `docs/markup-controls-spec`. Also commit the `.tasks.json` status. Then push:

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls-spec
git add docs/superpowers/plans/2026-10-22-markup-controls-16-fork-merge.md docs/superpowers/plans/2026-10-22-markup-controls-16-fork-merge.md.tasks.json
git commit -m "docs(plan): markup controls plan 16 fork merge done

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
git push origin docs/markup-controls-spec
```

- [ ] **Step 3: Clean up**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree remove .worktrees/mc-fork
git branch -d merge/markup-controls-fork
```

- [ ] **Step 4: Memory.** Update the three memory files as in the Acceptance Criteria.
