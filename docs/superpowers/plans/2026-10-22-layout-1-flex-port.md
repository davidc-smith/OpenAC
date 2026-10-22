# Layout upgrade plan 1: flex port onto the upstream controls line

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the fork's plugin-markup flex layout (engine, title bar, markup flex, scrolling groups, new controls in flex, bound-visible fix, FlexDemo, docs) onto `feat/plugin-flex-layout` as seven upstream-ready commits, with every fork-only piece cut or replaced by upstream's flat look.

**Architecture:** This is a regroup, not a replay. The finished code is read from fork `main` at **eca0047c** (`git show eca0047c:<path>`). Each task brings one series' hunks into the branch, cuts the fork-only parts, and applies the flat replacements given here. Which hunk belongs to which series was settled by blame against `commit-series.txt` and is written down per file in the notes beside this plan (`classify-A.md`, `classify-B.md`, `classify-C.md`). The notes give line numbers on eca0047c, and they are the source of truth for "bring X, strip Y".

**Tech Stack:** C# / .NET 10 (SDK 10.0.300 in `~/.dotnet`), xUnit, the retained UI in `src/AcDream.App/UI`.

**Spec:** `docs/superpowers/specs/2026-10-22-layout-upgrade-design.md` (Part 1), on branch `docs/layout-upgrade-spec`.

**Notes directory** (on `docs/layout-upgrade-spec`, worktree `.worktrees/layout-upgrade-spec`):
`docs/superpowers/plans/2026-10-22-layout-1-flex-port/`, which holds `classify-A.md` (markup side), `classify-B.md` (runtime, window manager, dock, title bar), `classify-C.md` (UiElement, UiRoot, scrolling, engine, tests), `commit-series.txt`, `baseline-1a777f1a-failures.txt` and `gate.sh`.
In the tasks below, `$NOTES` means that absolute path:
`/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/layout-upgrade-spec/docs/superpowers/plans/2026-10-22-layout-1-flex-port`.

## Global Constraints

- Work in `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/plugin-flex-layout`, on branch `feat/plugin-flex-layout`, which starts at 1a777f1a (PR 236's tip). Never commit to `upstream-main`, `main` or `feat/plugin-controls-upgrade`.
- Read fork code only from `eca0047c`. Use `git show eca0047c:<path>` and `git diff feat/plugin-controls-upgrade eca0047c -- <path>`; never check main's files out wholesale over the branch's.
- Nothing fork-only may land. These names must not appear anywhere under `src/`, `tests/` or `samples/` on the branch: `PluginUiStyle`, `ModernTitleFont`, `PluginThemeMetrics`, `UiMarkupCanvas`, `InMarkup`, `CollectCanvases`, `OnDrawingChild`, `HasTitle`, `RegisterTitle`, `UiPointerState`, `DrawThemedPlain`, `FillRoundedRect`, `DrawSmoothLine`, `BundledUiFontWeight`. Every task's verify step greps for them.
- No spec, plan or notes files on the branch (`docs/superpowers/**` stays off it).
- Every commit builds `AcDream.slnx -c Release` with **0 warnings** outside `external/`. Its failing test names must be a subset of `$NOTES/baseline-1a777f1a-failures.txt` (335 names), so no new failure is allowed.
- Don't edit lock files. After a build, put the branch's lock files back (`git checkout -- '*.lock.json'`). The gate script does this. FlexDemo's new `packages.neutral.lock.json` is the one exception: it is copied from eca0047c.
- Commit messages: conventional style as on PR 236 (`feat(ui): …`, `test(ui): …`). Each message ends with:
  `Claude-Session: https://claude.ai/code/session_01C83Twxs4TYLEbsJtnPsN4a`
- Code should read like the surrounding code: same comment density, and the doc comments carry over from main as they are, reworded only where they mention fork-only things (the dock gear, SemiBold, modern shapes).

**User decisions (already made):**
- "All of it, one PR": engine, title bar, markup flex, scrolling, wheel follow-ups, bound-visible and the controls' flex integration go into one upstream PR, as a commit series like PR 236.
- Flex goes upstream stacked on PR 236 (`feat/plugin-controls-upgrade` → `feat/plugin-flex-layout` → `feat/markup-repeat`), not folded into 236.
- Regroup from main's finished code (spec part 1, agreed).
- Themed (`theme="plugin"`) windows on the port draw with upstream's flat palette; the modern rounded shapes stay fork-only.
- Live client checks only after the user says they are available. Local ACE only (10.10.20.20), never Dreamweave.

**Calls made in this plan (flag to the user at the end, vetoable):**
1. The themed close X is upstream's dialog-close idiom: a `UiSimpleButton` face with `Text = "x"`, `BackgroundColor = p.Field`, `BorderColor = p.Border`, `TextColor = p.Text`, `Outline = false`, drawn by `base.OnDraw`. The close button keeps its own small hover/pressed tracker for the Classic sprite, and `UiSimpleButton` is left unchanged.
2. `PluginWindowChrome.TitleBarHeight = 24f` is a literal, the same value the fork's `PluginUiStyle.HeaderHeight` has.
3. `DockedCloseTooltip` = "Close (reopen from the plugin sidepanel)", which matches upstream's "Minimize to plugin sidepanel" wording.
4. Shift+sideways (9be9e56f) is folded into the scrolling commit, so `RetainedWheel` is never deleted without its replacement.
5. The flat model thumb in `UiScrollbar.DrawPlainScalar` also fixes an upstream bug: a themed log scrollbar's nub was stuck at the top. Say so in the PR text.
6. The upstream end-to-end test `Sideways_tick_reaches_the_root_as_a_step_only_while_Shift_is_held` is kept, although main dropped it.

---

## How every task verifies

Gate command, run from anywhere:

```bash
OUT=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/ee434c69-cb5a-4da7-98e6-8c90bc95af3b/scratchpad/gate
WT=/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/plugin-flex-layout
NOTES=/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/layout-upgrade-spec/docs/superpowers/plans/2026-10-22-layout-1-flex-port
GATE_OUT=$OUT zsh $NOTES/gate.sh $WT taskN
comm -13 $NOTES/baseline-1a777f1a-failures.txt $OUT/taskN-failures.txt   # must print nothing
cat $OUT/taskN-warnings.txt                                             # must print nothing
cd $WT && git grep -n -E 'PluginUiStyle|ModernTitleFont|PluginThemeMetrics|UiMarkupCanvas|InMarkup|CollectCanvases|OnDrawingChild|HasTitle|RegisterTitle|UiPointerState|DrawThemedPlain|FillRoundedRect|DrawSmoothLine|BundledUiFontWeight' -- src tests samples   # must print nothing
```

The full gate takes about 10–15 minutes. While iterating, run only the task's test classes:
`DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~<Class>"`.
Run the full gate once before each commit.

---

### Task 1: Flex engine

**Goal:** Add the pure flex layout engine (`UI/Layout/Flex/*`) in its series-1 form, with its tests.

**Files:**
- Create: `src/AcDream.App/UI/Layout/Flex/FlexTypes.cs`, `FlexNode.cs`, `FlexLayout.cs`
- Create: `tests/AcDream.App.Tests/UI/Layout/Flex/FlexTestNodes.cs`, `FlexLayoutMeasureTests.cs`, `FlexLayoutArrangeTests.cs`, `FlexLayoutNestingTests.cs`, `FlexLayoutFuzzTests.cs`, `FlexLayoutWrapScrollTests.cs`, `FlexLayoutScalingTests.cs`

**Acceptance Criteria:**
- [ ] The engine files equal the series-1 state: the tree at the tip of `flex/engine` (17c77905) for these paths. They have no `ScrollbarThickness`, no scrollbar settle and no grid lines, which come in Task 4.
- [ ] The flex engine tests pass (`FullyQualifiedName~Layout.Flex`).
- [ ] The gate passes: no new failures, 0 warnings, the fork-only grep is empty.
- [ ] One commit: `feat(ui): flex layout engine for plugin markup`.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~Layout.Flex"` → all pass; then the gate.

**Steps:**

- [ ] **Step 1: Bring the series-1 files from the `flex/engine` tip.** These paths are new on the branch and the engine series touched nothing else (classify-C "Port instructions per series", 1-engine):

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/plugin-flex-layout
git checkout 17c77905 -- src/AcDream.App/UI/Layout/Flex/FlexTypes.cs src/AcDream.App/UI/Layout/Flex/FlexNode.cs src/AcDream.App/UI/Layout/Flex/FlexLayout.cs tests/AcDream.App.Tests/UI/Layout/Flex/
git status --short
```

The listing should show exactly the 3 source files and the engine test files. If `FlexFitTests.cs` or `FlexLayoutScrollbarTests.cs` appear, delete them, because they belong to Tasks 3 and 4.
Then apply the one later engine-only change: the allocation test takes the least of five rounds (ddcc2d13), with `git show ddcc2d13 -- tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutScalingTests.cs | git apply`.
Also check whether a later engine fix to the 1-engine parts landed after 17c77905. Run `git diff 17c77905 eca0047c -- src/AcDream.App/UI/Layout/Flex/FlexLayout.cs`. Only 8463b376 and ce549264 (scroll, Task 4) should show; if any other commit shows, apply it too.

- [ ] **Step 2: Build and run the engine tests.** Expected: all pass, and the build has 0 warnings.

- [ ] **Step 3: Run the full gate** (section "How every task verifies", label `task1`).

- [ ] **Step 4: Commit.**

```bash
git add src/AcDream.App/UI/Layout/Flex tests/AcDream.App.Tests/UI/Layout/Flex
git commit -m "feat(ui): flex layout engine for plugin markup

A small flexbox subset in plain C#: rows and columns, grow and shrink,
wrap, gaps, padding, alignment, content minimums and height for width.
Nothing uses it yet.

Claude-Session: https://claude.ai/code/session_01C83Twxs4TYLEbsJtnPsN4a"
```

---

### Task 2: Window chrome and title bar

**Goal:** Plugin windows can have a host title bar (name plus close X) with a content area inside the chrome: `titlebar="true"`, and the bar's X replaces the dock's "–".

**Files:**
- Create: `src/AcDream.App/UI/PluginWindowChrome.cs`, `PluginTitleBar.cs`, `UiPluginContentHost.cs`, `MarkupWindow.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (BuildWindow, chrome sizes, title bar block, `TitleBar()` parser), `PluginMarkupTheme.cs` (`RegisterTitleBar`), `RetailWindowManager.cs` (string-list `ComputeAuthoredGeometryRevision`), `RetailUiRuntime.cs` (plugin mount), `PluginSidePanel.cs` (X replaces "–")
- Test: `tests/AcDream.App.Tests/UI/PluginWindowChromeTests.cs`, `PluginTitleBarTests.cs`, `MarkupTitleBarTests.cs`, `MarkupWindowMountTests.cs` (series-2 parts), plus `ThemeDrawCapture.cs` (helper)

**Acceptance Criteria:**
- [ ] The series-2 hunks listed in classify-A "2-titlebar", classify-B "Port instructions → 2-titlebar" and classify-C "2-titlebar" are present. None of the strip-list hunks are.
- [ ] `PluginWindowChrome.TitleBarHeight` is the literal `24f`, with a comment.
- [ ] A themed close button draws the upstream flat face: `p.Field` fill, `p.Border` edge, an "x" in `p.Text`. Classic keeps sprites 0x06004D0C / 0x06004D0D.
- [ ] A window with a title bar gets no dock "–", and its X's tooltip reads "Close (reopen from the plugin sidepanel)".
- [ ] A window without a title bar still gets the upstream title label, and that label is theme-registered: `panel.Children[^1]` right after it is added.
- [ ] The tests in the four test files pass, rewritten as described in Step 6.
- [ ] The gate passes. One commit: `feat(ui): plugin window title bar and content area`.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginWindowChromeTests|FullyQualifiedName~PluginTitleBarTests|FullyQualifiedName~MarkupTitleBarTests|FullyQualifiedName~MarkupWindowMountTests|FullyQualifiedName~MarkupDocumentTests"` → all pass; then the gate.

**Steps:**

- [ ] **Step 1: New chrome files in their series-2 shape.** For each file, start from its series-2 text and then apply the replacements:
  - `PluginWindowChrome.cs`: start from `git show 46ec8946:src/AcDream.App/UI/PluginWindowChrome.cs`. Classify-B step 2-titlebar.1 lists what it may contain: no `TopInset`/`VerticalInsetsFor` (Task 3), no `MinimumBarFrameWidth`/`FrameMinimumWidth` or `scroll` input (Task 4). Replace the `TitleBarHeight` line with:

```csharp
    /// <summary>The plugin title bar's height, the same in Classic and in a theme.</summary>
    internal const float TitleBarHeight = 24f;
```

    Set `DockedCloseTooltip` to `"Close (reopen from the plugin sidepanel)"`.
  - `PluginTitleBar.cs`: start from `git show 46ec8946:src/AcDream.App/UI/PluginTitleBar.cs`, then bring in the later series-2 fixes (ad953c38: the close tooltip skin, and Classic keeping sprite 0x06004D0C, pressed 0x06004D0D, with a hover highlight). Leave out `SetFrameWidth` (Task 3). Replace the themed draw with upstream's flat button. `PluginCloseButton` derives from `UiSimpleButton`; give it:

```csharp
    /// <summary>The window's theme palette, or null for Classic art.</summary>
    internal PluginUiPalette? ThemePalette
    {
        get => _palette;
        set
        {
            _palette = value;
            // A theme draws the close button the way it draws any plugin
            // button: a flat face and edge in the palette, an "x" caption.
            Text = value is null ? null : "x";
            if (value is { } p)
            {
                BackgroundColor = p.Field;
                BorderColor = p.Border;
                TextColor = p.Text;
                Outline = false;
            }
        }
    }
    private PluginUiPalette? _palette;
```

    On the branch, `UiSimpleButton.Text` is a non-null `string`, so use `Text = value is null ? string.Empty : "x";` above. Upstream's `UiSimpleButton` tracks no hover or pressed state, and its focus flag is private. Its `OnDraw` does draw the gold focus outline, and with the close button's zero background and border it draws nothing else. So the close button tracks the pointer itself and lets `base.OnDraw` draw the focus outline in both looks:

```csharp
    private bool _hovered, _pressed;

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.HoverEnter: _hovered = true; break;
            case UiEventType.HoverLeave: _hovered = false; break;
            case UiEventType.MouseDown: _pressed = true; _hovered = true; break;
            case UiEventType.MouseUp:
            case UiEventType.CaptureChanged: _pressed = false; break;
        }
        return base.OnEvent(e);
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (_palette is not null)
        {
            base.OnDraw(ctx);
            return;
        }
        var (tex, tw, th) = (0u, 0, 0);
        if (_pressed && _hovered)
            (tex, tw, th) = _resolve(PluginWindowChrome.ClassicClosePressedSprite);
        if (tex == 0u)
            (tex, tw, th) = _resolve(PluginWindowChrome.ClassicCloseSprite);
        if (tex != 0u && tw > 0 && th > 0)
        {
            float scale = MathF.Min(Width / tw, Height / th);
            float w = tw * scale, h = th * scale;
            float x = (Width - w) / 2f, y = (Height - h) / 2f;
            ctx.DrawSprite(tex, x, y, w, h, 0f, 0f, 1f, 1f, Vector4.One);
            if (_hovered && !_pressed)
                ctx.DrawFill(x, y, w, h, ClassicCloseHover);
        }
        // Zero face and edge: the base draws only the keyboard focus outline.
        base.OnDraw(ctx);
    }
```

    Going from a theme back to Classic must restore the transparent face, so the setter's `null` case also sets `BackgroundColor = Vector4.Zero; BorderColor = Vector4.Zero;`. The PluginTitleBarTests need a case for this: theme → Classic draws no fill.
  - `UiPluginContentHost.cs`: series-2 shape, meaning the constructor and `ClipsChildrenForTest` only (classify-B 2-titlebar.3; `git show 814f2c61:src/AcDream.App/UI/UiPluginContentHost.cs`).
  - `MarkupWindow.cs`: main lines 1-11, 15-19, 23-36 and 39-40 (classify-A 2-titlebar). Leave out `Canvases` (20-22), the flex/scroll members and `TrackContentMinimum`.

- [ ] **Step 2: MarkupDocument.** Apply classify-A "2-titlebar" exactly: consts 15-16; `BuildWindow` with `Build` delegating to it; the chrome-sized panel; MinHeight `+ chromeH`; the series-2 MinWidth `FOr(root,"minw",contentW) + chromeW`; the title-bar block 115-136 **without** line 133; `titleColor`; the revision; `new MarkupWindow(panel, contentParent, titleBar, revision)`; the series-2 `TitleBar(XElement root)` parser (`null or "false" => false`). For the label title keep upstream's register call, placed inside the label branch:

```csharp
            if (panel is UiPluginMarkupPanel titlePanel)
                PluginMarkupTheme.Register(titlePanel, panel.Children[^1], new XElement("label"));
```

  `BuildWindow` takes every parameter `Build` takes on the branch today (the 13 controls' `controlState`, `describeKey`, `textures`, `sharedControlState`, `recentColors` …) plus `fallbackTitle`. `Build` passes them through and returns `.Frame`.

- [ ] **Step 3: PluginMarkupTheme.** Add `RegisterTitleBar` (main 24-41), using `panel.ModernFont ?? classicFont` where main uses `ModernTitleFont`. Reword the comment so it says the bar takes the theme's font. Bring nothing else from that diff.

- [ ] **Step 4: RetailWindowManager, RetailUiRuntime, PluginSidePanel.** Follow classify-B 2-titlebar steps 4–6 as written. The RetailUiRuntime mount becomes `MarkupDocument.BuildWindow(…, fallbackTitle: panel.Descriptor.Title, …)` → `window.Frame` → `window.Register(Host.WindowManager, panel.WindowName, visibility)`. Strip `PluginMarkupIconResolver`, `window.Canvases`, `dockIcons.Resolve` and `TogglePluginAppearance`. In PluginSidePanel, use the code block from classify-B step 6 with upstream's `new PluginMinimizeButton(handle, _font)`, and make `ShelfEntry.Minimize` nullable with the two guarded removals.

- [ ] **Step 5: Test helper.** Copy `tests/AcDream.App.Tests/UI/ThemeDrawCapture.cs` from eca0047c. Change `context.Begin(size, null, pixelScale)` to `context.Begin(size, null)` and drop the `pixelScale` parameters (classify-C Dependencies 3).

- [ ] **Step 6: Tests.** Copy `PluginWindowChromeTests.cs`, `PluginTitleBarTests.cs`, `MarkupTitleBarTests.cs` and `MarkupWindowMountTests.cs` from 46ec8946/ad953c38's state (`git show ad953c38:<path>`; for MarkupWindowMountTests use 814f2c61's). Then fix them for upstream, as classify-C "Tests" describes:
  - PluginTitleBarTests: replace `Modern_DrawsAMutedXThatTakesTheTextColourOnHover` with `Themed_CloseButtonIsAFlatFieldFaceWithAnX`. Build a bar with `ThemePalette = PluginUiPalette.Moss`, draw it through `ThemeDrawCapture`, and assert there's a fill in `Moss.Field` covering the close button's rect and that the caption is "x". `ATheme_CentresTheCloseButtonInTheBand` keeps `Top == 3`, because the 19-point button in the 24-point bar is unchanged. In the tooltip test, expect "Close (reopen from the plugin sidepanel)".
  - MarkupTitleBarTests `Themed_TheBarFollowsTheThemeAndBack`: use `new PluginUiThemeSettings(null, modernFont)` and assert the bar's font is `ModernFont`. Keep the close-position assertions in line with Top 3.
  - MarkupWindowMountTests: drop `TheDockLeavesItsMinimizeOffALayoutWindowWithoutABar` (Task 4 adds it back, rewritten). Keep `TheDockLeavesItsMinimizeOffAWindowWithABar`, running against the upstream `PluginSidePanel`.
  Run the verify filter. Every rewritten test must fail before the code change and pass after it, so check each rewrite once by temporarily breaking the line it pins.

- [ ] **Step 7: Gate** (label `task2`), then commit:

```bash
git add -A src tests
git commit -m "feat(ui): plugin window title bar and content area

titlebar=\"true\" gives a markup window a host title bar: its name and a
close X, with the content laid out inside the chrome. On a docked window
the X replaces the sidepanel's minimize button. Classic uses the retail
close art; a themed window draws a flat palette button like its others.

Claude-Session: https://claude.ai/code/session_01C83Twxs4TYLEbsJtnPsN4a"
```

---

### Task 3: Flex layout in markup

**Goal:** `layout="row|column"` on the root `<panel>` and on `<group>`, the flex item attributes, content sizes for every control (including PR 236's 13), the fitted window minimum, and title bars on by default for layout windows.

**Files:**
- Create: `src/AcDream.App/UI/MarkupFlexAttributes.cs` (minus `Scroll()`), `MarkupContentSize.cs`, `UiFlexBox.cs`, `UiFlexGroup.cs` (non-scroll parts), `Layout/Flex/FlexFit.cs` (series-3 part)
- Modify: `MarkupDocument.cs`, `MarkupWindow.cs`, `PluginWindowChrome.cs` (`TopInset`, `VerticalInsetsFor`), `PluginTitleBar.cs` (`SetFrameWidth`), `UiPluginContentHost.cs` (flex members), `UiElement.cs` (`LayoutChildren`), `UiPanel.cs` (`UiLabel.SizedByLayout`), `RetailWindowManager.cs` (`EnforceMinimumSize`, `FitAxis`)
- Test: `FlexFitTests.cs`, `MarkupFlexAttributesTests.cs`, `MarkupContentSizeTests.cs`, `MarkupFlexTests.cs`, `MarkupFlexWindowTests.cs`, `RetailWindowManagerMinimumTests.cs`

**Acceptance Criteria:**
- [ ] The series-3 hunks from classify-A/B/C "3-markup" are present, including c76c878f (a flex menu's popup is as wide as the menu).
- [ ] `MarkupContentSize` has no `ThemedToggleCaptionX`. The toggle caption is at `ClassicToggleCaptionX` (17) in both looks, and there's no canvas case.
- [ ] `MarkupContentSize` covers the 13 new controls at their series-3 sizes. Their flex-specific behaviour (expander collapse, split panes) comes in Task 5.
- [ ] The tests pass with the upstream rewrites (63f → 45f; theme reflow driven by font advances).
- [ ] The gate passes. One commit: `feat(ui): flex layout in plugin markup`.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~FlexFitTests|FullyQualifiedName~MarkupFlex|FullyQualifiedName~MarkupContentSizeTests|FullyQualifiedName~RetailWindowManagerMinimumTests|FullyQualifiedName~MarkupTitleBarTests|FullyQualifiedName~MarkupDocumentTests"` → all pass; then the gate.

**Steps:**

- [ ] **Step 1: Source.** Apply classify-A "3-markup", classify-B "3-markup" (steps 1-5, but leave AcDream.slnx to Task 7) and classify-C "3-markup" (UiElement 504 + 616-626, UiPanel 64-70 + 78-98, UiFlexBox without the 5-controls members, UiFlexGroup without the scroll members, FlexFit's series-3 lines).
  Where a file was changed again in series 4 or 5, use the series-3 form the notes give. In particular:
  - `AddFlexItem` uses the group size `el.Name.LocalName == "group" ? new FlexSize(element.Width, element.Height) : null`.
  - `panel.MinWidth = minW + chromeW`.
  - The `TitleBar(XElement root, bool hasLayout)` default is `null => hasLayout`.
  - `TrackContentMinimum` takes its series-3 signature.
  For MarkupContentSize, start from `git show eca0047c:src/AcDream.App/UI/MarkupContentSize.cs`. Remove the 5-controls lines (classify-A lists them: 38-48, 73-75, 101, 120-142, 148-159, 167-169, 181-185, 189-190), and only where they are pure 5-controls additions; the 13 controls' basic sizes from the fork merge stay. Delete line 36, write line 100 as `float captionX = ClassicToggleCaptionX;`, and drop 116-119.
  If you can't tell whether a line in `MarkupContentSize` is series 3 or 5, check with `git log -L<start>,<end>:src/AcDream.App/UI/MarkupContentSize.cs eca0047c` and look the commit up in `$NOTES/commit-series.txt`.

- [ ] **Step 2: Tests.** Copy the six test files from eca0047c and drop the cases that need Task 4 or 5 code (scroll, expander collapse, split). Find them with `git log --format=%h -1 -S'<test name>' eca0047c -- <file>` and look the commit up in the series map. Rewrite:
  - MarkupContentSizeTests `A_toggle_starts_its_caption_after_its_lamp_or_switch`: themed width = 45f, the same as Classic.
  - MarkupFlexTests `A_theme_switch_reflows_items_whose_size_depends_on_it`: build the theme with a modern `UiDatFont` whose glyph advances are twice the Classic font's, toggle the theme, and assert that a label item's measured width doubles and its sibling moves by the difference. Make the test fonts the same way the existing tests in that file do (`grep -n "UiDatFont" tests/AcDream.App.Tests/UI/MarkupFlexTests.cs`).

- [ ] **Step 3: Gate** (label `task3`), then commit:

```bash
git add -A src tests
git commit -m "feat(ui): flex layout in plugin markup

layout=\"row|column\" on the window or a group lays its children out by
content size, with grow, shrink, wrap, gap, padding and alignment. A flex
window gets a title bar by default and opens at its fitted minimum; the
window manager keeps that minimum while resizing.

Claude-Session: https://claude.ai/code/session_01C83Twxs4TYLEbsJtnPsN4a"
```

---

### Task 4: Scrolling groups and wheel routing

**Goal:** `scroll="y|x|both"` on a flex group or the window root, with the wheel going to the innermost scrollable under the pointer, Shift+wheel going sideways, and the follow-up fixes. This replaces `RetainedWheel`.

**Files:**
- Create: `src/AcDream.App/UI/UiScrollArea.cs`, `UiScrollPanel.cs`
- Modify: `UiEvent.cs` (`ScrollHorizontal`), `UiRoot.cs` (wheel routing), `RetainedUiInputBinding.cs` (drop `RetainedWheel`, dominant-axis filter), `UiElement.cs` (scroll viewport), `UiFlexGroup.cs`, `UiFlexBox.cs`, `UiPluginContentHost.cs`, `Layout/Flex/FlexNode.cs`, `FlexLayout.cs`, `FlexFit.cs` (scroll parts), `UiScrollbar.cs` (flat model thumb), `UiMarkupList.cs`, `UiMarkupLog.cs` (leave the wheel when they fit), `MarkupDocument.cs`, `MarkupWindow.cs`, `MarkupFlexAttributes.cs` (`Scroll()`), `PluginWindowChrome.cs`, `PluginSidePanel.cs` (no "–" on bar-less layout windows)
- Test: `UiContentOffsetTests.cs`, `UiScrollGroupTests.cs`, `MarkupScrollWindowTests.cs`, `FlexLayoutScrollbarTests.cs`, `UiRootWheelRoutingTests.cs`, `RetainedUiInputBindingTests.cs`, `SilkRetainedMouseScrollTests.cs`, `MarkupNumberTests.cs` (one test), `UiMarkupListScrollbarTests.cs`, `UiMarkupLogTests.cs`, `MarkupWindowMountTests.cs` (the bar-less case), `MarkupFlexWindowTests.cs` (scroll cases)

**Acceptance Criteria:**
- [ ] The series-4 hunks (including the follow-ups and 9be9e56f) from classify-A/B/C are present. `OnDrawingChild` is not.
- [ ] `RetainedWheel` is gone. The binding calls `_root.OnScroll(dx, dy, _root.ShiftHeld)`, and `UiRoot.OnScroll` takes main's 902-997 form, including the 54d19bb7 dialog blocker.
- [ ] `UiScrollbar.DrawPlainScalar` draws a flat model thumb for both axes (code below). There's no `ThemePalette` on `UiScrollbar`.
- [ ] The upstream test `Sideways_tick_reaches_the_root_as_a_step_only_while_Shift_is_held` is still there and passes.
- [ ] A test pins the flat horizontal thumb: about 17 pt long on a 68-pt track at ¼ visible, spanning y 3..13.
- [ ] The gate passes. One commit: `feat(ui): scrolling groups in plugin markup`.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~Scroll|FullyQualifiedName~Wheel|FullyQualifiedName~RetainedUiInputBindingTests|FullyQualifiedName~MarkupNumberTests|FullyQualifiedName~UiMarkupLogTests|FullyQualifiedName~MarkupWindowMountTests|FullyQualifiedName~MarkupFlex|FullyQualifiedName~UiContentOffsetTests"` → all pass; then the gate.

**Steps:**

- [ ] **Step 1: Source.** Apply classify-A "4-scroll", classify-B "4-scroll" and classify-C "4-scroll" plus its "Replacing BASE's RetainedWheel" section, all six steps, in one go, so `RetainedWheel` is never removed without its replacement.

- [ ] **Step 2: Flat model thumb.** In `UiScrollbar.DrawPlainScalar`, after the track fill and outline:

```csharp
        // A model-backed bar (a scrolling group, a log) has no scalar
        // position; draw its thumb where the model puts it, flat, inset 3.
        if (ScalarChanged is null && Model is { } m)
        {
            if (Horizontal)
            {
                float start = AxisExtent(DecrementButtonExtent, Width);
                float len = MathF.Max(0f, Width - start - AxisExtent(IncrementButtonExtent, Width));
                var (tx, tw) = ModelThumbRect(m, start, len);
                ctx.DrawFill(tx, 3f, tw, MathF.Max(0f, Height - 6f), PlainNubColor);
            }
            else
            {
                float start = AxisExtent(DecrementButtonExtent, Height);
                float len = MathF.Max(0f, Height - start - AxisExtent(IncrementButtonExtent, Height));
                var (ty, th) = ModelThumbRect(m, start, len);
                ctx.DrawFill(3f, ty, MathF.Max(0f, Width - 6f), th, PlainNubColor);
            }
            return;
        }
```

  First check the member names on the branch: `grep -n "ModelThumbRect\|AxisExtent\|DecrementButtonExtent\|ScalarChanged\|PlainNubColor\|bool Horizontal" src/AcDream.App/UI/UiScrollbar.cs`. If one is named differently, use the branch's name.

- [ ] **Step 3: Tests.** Copy the listed test files at eca0047c. Make the RetainedUiInputBindingTests and SilkRetainedMouseScrollTests changes from classify-C "Replacing … 5", keeping the upstream sideways-tick test. From MarkupNumberTests take only 266-279. Rewrite UiScrollGroupTests `Bars_take_the_window_theme_and_go_back_to_classic_art`: themed means `RetailArt == false` and `PlainTrackColor == PluginUiPalette.Moss.Field`; back to Classic means `RetailArt == true`. Add `A_themed_horizontal_scroll_bar_draws_a_flat_thumb_along_its_track` to UiScrollGroupTests, following classify-C's PluginThemeControlTests row: `RetailArt = false`, `PlainNubColor = Moss.Muted`, and assert a fill in `Muted` about 17 pt long spanning y 3..13. Add back `TheDockLeavesItsMinimizeOffALayoutWindowWithoutABar` against the upstream `PluginSidePanel`.

- [ ] **Step 4: Gate** (label `task4`), then commit:

```bash
git add -A src tests
git commit -m "feat(ui): scrolling groups in plugin markup

scroll=\"y|x|both\" on a flex group or the window clips its content and
adds bars when it overflows. The wheel goes to the innermost group under
the pointer that can still move; Shift or a sideways tick scrolls
sideways first, else steps vertically (replacing RetainedWheel). Lists
and logs that fit leave the wheel to their parent, a wheel step closes
an open popup, and hover is checked again after a step. A themed
model-backed bar now draws its thumb where the model puts it.

Claude-Session: https://claude.ai/code/session_01C83Twxs4TYLEbsJtnPsN4a"
```

---

### Task 5: New controls in flex

**Goal:** PR 236's expander, split and number work inside flex containers: expander collapse reflows, split panes can be flex containers, and expanders and splits without w/h are sized before their anchors are captured.

**Files:**
- Modify: `src/AcDream.App/UI/UiMarkupExpander.cs` (35-44, 161-165), `UiMarkupExpanderHeader.cs` (`CaptionX` only), `UiMarkupNumber.cs` (60-62 only), `UiFlexBox.cs` (`SyncCollapse`, `ExpandedMaxHeight`/`ExpandedMinHeight`, `_collapsed`), `MarkupContentSize.cs` (5-controls lines), `MarkupDocument.cs` (`AddFlexItem` expander/split 952-981, argument plumbing)
- Test: `MarkupFlexControlsTests.cs`, the 5fb02a19 part of `MarkupFlexTests.cs`, `MarkupNumberTests` (anchor case if one exists at eca0047c)

**Acceptance Criteria:**
- [ ] The commits a14340b9, 5fb02a19, ffb91cec, bfb6d61e, cbd0ebf7 and 22640176 are reflected, along with the 54d19bb7 flex resolutions classify-A/C list. None of e239a759/45a83200's modern shapes are (no chevron, no focus ring, no number spin art).
- [ ] A `<number>` laid out by a flex row shows its value: the inner field has `Anchors = None`.
- [ ] `MarkupFlexControlsTests` passes.
- [ ] The gate passes. One commit: `feat(ui): expander, split and number inside flex layout`.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~MarkupFlex|FullyQualifiedName~MarkupExpander|FullyQualifiedName~MarkupSplit|FullyQualifiedName~MarkupNumber"` → all pass; then the gate.

**Steps:**

- [ ] **Step 1: Source.** Apply classify-A "5-controls-in-flex" and classify-C "5-controls-in-flex". In `UiMarkupExpanderHeader.cs` add only the `CaptionX` constant and its use for the caption x; compare with `git diff feat/plugin-controls-upgrade eca0047c -- src/AcDream.App/UI/UiMarkupExpanderHeader.cs` and leave every `PluginUiStyle` line out.
- [ ] **Step 2: Tests.** Copy `MarkupFlexControlsTests.cs` from eca0047c and the 5fb02a19 test(s) into `MarkupFlexTests.cs`. Leave out the 54d19bb7 hunks in MarkupExpanderTests and MarkupListMultiSelectTests (classify-C "Tests").
- [ ] **Step 3: Gate** (label `task5`), then commit:

```bash
git add -A src tests
git commit -m "feat(ui): expander, split and number inside flex layout

An expander in a flex container reflows its siblings as it opens and
closes; split panes may be flex groups; an expander or split with no
w/h takes its content size before its anchors are captured; a number
sized by a flex row shows its value.

Claude-Session: https://claude.ai/code/session_01C83Twxs4TYLEbsJtnPsN4a"
```

---

### Task 6: A flex window's first size follows bound visible

**Goal:** `BindBool` seeds `Visible`/`Enabled` from one build-time read, so a tabbed flex window doesn't open at the height of all its pages.

**Files:**
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (BindBool, main 1563-1574)
- Test: `tests/AcDream.App.Tests/UI/MarkupFlexWindowTests.cs` (the 2e6e1d29 cases)

**Acceptance Criteria:**
- [ ] A getter that throws at build time leaves the default (shown), and the first tick reads it again.
- [ ] The 2e6e1d29 tests pass, and the gate passes. One commit: `fix(ui): a flex window's first size follows bound visible`.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~MarkupFlexWindowTests"` → all pass; then the gate.

**Steps:**

- [ ] **Step 1:** Write the 2e6e1d29 test(s) into MarkupFlexWindowTests (`git show 2e6e1d29 -- tests/`) and run them. Expected: FAIL, because the window opens at the all-pages height.
- [ ] **Step 2:** In `BindBool`, replace the final `setSource(() => property.GetValue(binding) is true);` with main 1563-1574:

```csharp
        Func<bool> source = () => property.GetValue(binding) is true;
        setSource(source);

        // Ticks apply the source, but the window's first measurement comes
        // before any tick: seed the value it reports now so that measurement
        // leaves out what is bound hidden (a tabbed window's other pages). A
        // getter that throws this early leaves the default; the first tick
        // reads it again as usual.
        bool seeded;
        try { seeded = source(); }
        catch (TargetInvocationException) { return; }
        setLiteral(seeded);
```

  Compare with `git show eca0047c:src/AcDream.App/UI/MarkupDocument.cs | sed -n 1541,1575p` before you edit.
- [ ] **Step 3:** Run the tests (they pass), then the gate (label `task6`), then commit with that message plus the Claude-Session line.

---

### Task 7: FlexDemo sample and flex docs

**Goal:** The FlexDemo sample plugin (finder, settings, gallery and Flex Controls windows) with its tests, and the Title bar, Flex layout and Scrolling groups documentation in `docs/plugin-ui-markup.md`.

**Files:**
- Create: `samples/AcDream.Plugins.FlexDemo/*` (csproj, plugin.json, FlexDemoPlugin.cs, finder.xml, settings.xml, gallery.xml, controls.xml, packages.neutral.lock.json)
- Modify: `AcDream.slnx` (the FlexDemo line only), `docs/plugin-ui-markup.md`
- Test: `tests/AcDream.App.Tests/UI/FlexDemoSampleTests.cs` (+ test csproj reference, if main has one)

**Acceptance Criteria:**
- [ ] FlexDemo builds with 0 warnings and uses no fork-only markup (no `<canvas>`, `icon file=` or dock-gear wording). The FlexDemoPlugin comment line 13 refers to upstream's appearance setting in the plugin sidepanel.
- [ ] FlexDemoSampleTests passes, including the settings `onchange="{PickProfile}"` / `ProfileIndex` binding fixed by b6deb9d3, and the Flex Controls window placed below Flex Finder (e3eb4c53).
- [ ] The docs have these sections: Title bar, Flex layout (container attributes, item attributes, content sizes, mixing absolute and flex, flex windows) and Scrolling groups. Element-table rows mention the flex attributes. The Tests section lists the flex test classes. Nothing mentions fork-only features (modern shapes, SemiBold, dock gear, markup canvas, contract versions).
- [ ] The gate passes. One commit: `feat(samples): FlexDemo plugin; docs for flex layout`.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~FlexDemoSampleTests"` → all pass; `git grep -n -i -E "gear|SemiBold|modern shape|<canvas|fork\." -- docs/plugin-ui-markup.md samples/AcDream.Plugins.FlexDemo` → only lines that already exist on 1a777f1a; then the gate.

**Steps:**

- [ ] **Step 1: Sample.** Copy `samples/AcDream.Plugins.FlexDemo/` from eca0047c. Reword line 13 of `FlexDemoPlugin.cs`. Add the FlexDemo `<Project …/>` line to `AcDream.slnx` (main line 21). If the App test project references sample projects (as it does for ControlsDemo on the branch: `grep -n ControlsDemo tests/AcDream.App.Tests/*.csproj`), add FlexDemo the same way.
- [ ] **Step 2: Tests.** Copy `FlexDemoSampleTests.cs` from eca0047c and run it.
- [ ] **Step 3: Docs.** Bring the flex parts of `git diff feat/plugin-controls-upgrade eca0047c -- docs/plugin-ui-markup.md`: the sections `## Title bar`, `## Flex layout` (with its subsections), `## Scrolling groups`, the flex-attribute mentions in the element table (main lines ~298-325), the wheel/Shift notes, and the flex test classes in `## Tests`. Leave out every other hunk; they document fork-only features. Where a flex paragraph mentions a themed look, describe the flat look: "a theme draws the close button as a plain palette button" and "a themed bar draws a flat thumb".
- [ ] **Step 4: Gate** (label `task7`), then commit with that message plus the Claude-Session line.

---

### Task 8: Whole-branch audit and push

**Goal:** Check that the branch equals fork main's flex minus the fork-only parts, then push it.

**Files:** none changed, unless the audit finds something. Any fix goes in a `fix(ui): …` commit with the gate rerun.

**Acceptance Criteria:**
- [ ] For each flex source file, `git diff eca0047c feat/plugin-flex-layout -- <file>` shows only the strip-list items and the flat replacements recorded in this plan and the notes. Record the file-by-file result in `$NOTES/audit.md`.
- [ ] The fork-only grep is empty, and `git diff --stat 1a777f1a..HEAD -- docs/superpowers` is empty.
- [ ] `git log --oneline 1a777f1a..HEAD` shows 7 commits (plus any audit fixes), and each one passed the gate.
- [ ] The branch is pushed to `origin` (`git push -u origin feat/plugin-flex-layout`). The upstream PR is **not** opened: the user opens it as davidc-smith.

**Verify:** `git diff --stat eca0047c feat/plugin-flex-layout -- src/AcDream.App/UI/Layout/Flex src/AcDream.App/UI/UiFlexBox.cs src/AcDream.App/UI/UiFlexGroup.cs src/AcDream.App/UI/UiScrollArea.cs src/AcDream.App/UI/UiScrollPanel.cs src/AcDream.App/UI/UiPluginContentHost.cs src/AcDream.App/UI/MarkupFlexAttributes.cs` → empty, or only the recorded cuts.

**Steps:**

- [ ] **Step 1:** Run the verify diff. Then, for each file in classify-A/B/C, read the eca0047c→branch diff and tick it off against the strip lists. Write `$NOTES/audit.md` with one line per file (`same` / `cuts: …`) and commit it on `docs/layout-upgrade-spec`.
- [ ] **Step 2:** Push the branch, then push `docs/layout-upgrade-spec`.

---

### Task 9: Live check of FlexDemo and ControlsDemo (user present)

**Goal:** See the flex port working in a real client against the local ACE server, in Classic and in the plugin theme.

**Files:** none. A defect becomes a `fix(ui): …` commit on the branch, with the gate rerun.

**Acceptance Criteria:**
- [ ] The user has said they are available. Don't launch a client before that.
- [ ] Built from this worktree, with FlexDemo and ControlsDemo installed to `~/OpenAC-dev/plugins/`, running against 10.10.20.20 through `~/OpenAC-dev/dev-client.env`, never Dreamweave.
- [ ] Each of these is checked by the user, in both Classic and theme:
  - Flex Finder resizes and reflows.
  - Settings opens at its one-page height and its profile list scrolls.
  - Gallery scrolls sideways with the trackpad and with Shift+wheel.
  - The Flex Controls expander reflows, and split panes resize.
  - The title bar X closes a window and the tooltip says where it went.
  - The themed close X and the scrollbar thumbs are flat palette shapes.
  - The ControlsDemo windows (non-flex) are unchanged.
- [ ] The user says it passed, or every defect found is fixed and checked again.

**Verify:** the user's confirmation in the conversation.

**Steps:**

- [ ] **Step 1:** Ask the user whether they are available for the live check. Wait for their answer.
- [ ] **Step 2:** Build Release. Copy `samples/AcDream.Plugins.FlexDemo/bin/Release/net10.0/` and the ControlsDemo output into `~/OpenAC-dev/plugins/sample.flex-demo` and `sample.controls-demo`. Launch the dev client from this worktree the way `live-testing-rules` describes, then go through the checklist with the user.
