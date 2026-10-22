# Group C classification: core UI files (flex port onto feat/plugin-controls-upgrade)

Method: `git diff -U0 feat/plugin-controls-upgrade main -- <file>`, then `git blame -l main` on every added line, looked up in commit-series.txt. Line numbers are **main** line numbers. "BASE" = feat/plugin-controls-upgrade. Short shas: 71d8f955 / 5b9b7c7e / 226ac1ff / 73fbd4f1 / ce549264 / 914ed099 / 681189b2 / 8463b376 = 4-scroll; 28cc84d8 = 4-scroll-followups; 9be9e56f / 243f6a9d = 4-scroll-shift-sideways; c15efdec = 3-markup; 54d19bb7 = merge resolution.

---

## src/AcDream.App/UI/UiElement.cs

| main lines | summary | series | notes |
|---|---|---|---|
| 97, 101-102 | `ScreenPosition` walks parents and subtracts `parent.OffsetFor(child)` | 4-scroll (71d8f955) | clean |
| 109-133 | `ScrollChrome`, `ContentViewport`, `ContentOffset`, `OffsetFor` | 4-scroll (71d8f955) | clean |
| 172-177 | `AcceptsFocus` becomes `virtual` + doc | FORK-ONLY: plugin canvas focus (3e8b2dfa "a canvas takes and gives back keyboard focus") | not needed by flex; skip |
| 242-249 | `internal virtual void ConstrainWindowDrag(...)` | FORK-ONLY: window drag hook (b2b58f26) | skip (UiRoot line 515 calls it, skip that too) |
| 390-392 | `private protected virtual void OnDrawingChild(...)` | FORK-ONLY: upper draw layer (6f7f796b) | skip |
| 504 (replaces BASE 461-462) | `LayoutChildren();` call replaces the inline `ApplyAnchor` loop | 3-markup (c15efdec) | clean |
| 510-516 | child draw loop: `{`, `OnDrawingChild(ctx, ordered[i]);`, `if (ContentViewport ... ) DrawScrolled(...) else DrawSelfAndChildren`, `}` | MIXED: lines 510, 511, 516 = FORK-ONLY upper layer (6f7f796b); 512-515 = 4-scroll (71d8f955) | **strip line 511**; keep the braces and the 4-line if/else |
| 535-550 | `DrawScrolled` (push clip to viewport, push transform -offset) | 4-scroll (71d8f955) | uses `PushTransform/PushClip`, both on BASE |
| 577-586 | `DrawOverlays`: shift overlays of scrolled children by `-ContentOffset` | 4-scroll (71d8f955) | clean |
| 616-626 | `private protected virtual void LayoutChildren()` (default = anchors) | 3-markup (c15efdec) | clean |
| 640-648 | `HitTest`: scrolled children reachable only through the viewport, offset added | 4-scroll (71d8f955) | clean |

## src/AcDream.App/UI/UiRoot.cs

| main lines | summary | series | notes |
|---|---|---|---|
| 4 | `using AcDream.App.Rendering;` | FORK-ONLY: upper draw layer (6f7f796b) | skip |
| 223-225 | `_tooltipExpired` field | FORK-ONLY: tooltip expiry (eeb0d066) | skip |
| 244-250 | `PressStarting` event | FORK-ONLY: canvas outside press (d7e18400) | skip |
| 364 | `_tooltipExpired = false` | FORK-ONLY eeb0d066 | skip |
| 398 | `&& !_tooltipExpired` | FORK-ONLY eeb0d066 | skip |
| 410-414 (BASE 398-402) | tooltip timeout no longer sends HoverLeave | FORK-ONLY eeb0d066 | skip |
| 434-443 | `OnDrawingChild` override, `BeginUpperLayer` | FORK-ONLY upper draw layer (6f7f796b) | skip |
| 515 | `_windowDragTarget.ConstrainWindowDrag(...)` | FORK-ONLY b2b58f26 | skip |
| 682 | `PressStarting?.Invoke(...)` | FORK-ONLY d7e18400 | skip |
| 727-729 + deletion of BASE line 774 (`var sp = target.ScreenPosition;`) | read the press position before `SetKeyboardFocus` (focus may scroll a group to reveal the target) | 4-scroll (ce549264) | port both halves together |
| 902-903 | `OnScroll(int dy) => OnScroll(0, dy, shift: false)` | 4-scroll (226ac1ff) | |
| 905-922 | doc comment | 4-scroll 226ac1ff + ce549264; 915-917 = 9be9e56f; 918-921 = 28cc84d8 | |
| 923-955 | `OnScroll(int dx, int dy, bool shift)` | 226ac1ff (skeleton); 925, 929-930, 937-954 = 28cc84d8 (popup close + hover refresh + `taken`); 926-927, 931-936, 939 = 9be9e56f (Shift+sideways branch) | all flex |
| 957-960 | `internal bool ShiftHeld` | 4-scroll (226ac1ff) | replaces the binding's private `ShiftHeld()` |
| 962-972 | `RouteHorizontal` | 226ac1ff; 964-965, 967 = 54d19bb7 merge (popup target, `BlockerFor` dialog check) | BASE has `BlockerFor` (dialogs); keep the merged form from the start |
| 974-975, 983, 990, 996 | old `OnScroll(int)` body becomes `bool RouteVertical(int dy)` | 28cc84d8 (4-scroll-followups) | |
| 993 | `return false; // a window-modal dialog owns this window` | 54d19bb7 merge | BASE line 917 had `return;` |
| 1007-1010 | hidden `DefaultTextInput` never takes Tab/Enter | FORK-ONLY: Rynth chat fix (dc4f65c4) | skip |
| 1018 (BASE 917-919) | Tab uses `MoveMarkupFocus(ShiftHeld)` | 4-scroll (226ac1ff) | trivial refactor; needed only because ShiftHeld moved |
| 1140 | `RevealInScrollingAncestors(e)` in `SetKeyboardFocus` | 4-scroll (5b9b7c7e) | |
| 1145-1151 | `RevealInScrollingAncestors` (uses `IUiScrollHost`) | 4-scroll (5b9b7c7e) | needs UiScrollArea.cs |
| 1232-1234 | `BringToFront(window, UiBand band)` | FORK-ONLY: z-order bands (9113426b) | skip |
| 1285-1302 | `CancelDrag()` | FORK-ONLY: drag release interceptor (6c685b29) + drop-highlight fix (758b4e3e) | skip |
| 1365, 1381 | `_tooltipExpired = false` | FORK-ONLY eeb0d066 | skip |

BASE already has everything the flex hunks call: `PopupHit`, `_activePopup/_activePopupDismiss`, `_resizeTarget`, `_windowDragTarget`, `UpdateHover`, `BlockerFor`, `bool BubbleEvent`, `MoveMarkupFocus`.

## src/AcDream.App/UI/UiPanel.cs

| main lines | summary | series | notes |
|---|---|---|---|
| 64-70 | `UiLabel.SizedByLayout` | 3-markup (c15efdec) | |
| 78-98 (BASE 72-82) | `UiLabel.OnDraw`: when SizedByLayout, don't self-size, centre the line vertically (`y`) | 3-markup (c15efdec) | clean |
| 115-124 | `UiSimpleButton.ThemePalette`, `_pointer`, `ThemeState`, `KeyboardFocused` | FORK-ONLY: themed buttons (6b071591) | skip |
| 144 | `_pointer.Observe(in e)` | FORK-ONLY 6b071591 | skip |
| 155-164 | `DrawThemedFace` (`PluginUiStyle.Button/FocusRing`) | FORK-ONLY 6b071591 | skip |
| 167-177 | themed/classic face split in OnDraw | FORK-ONLY 6b071591 | skip |
| 179, 204 | `fade` + faded caption | FORK-ONLY: disabled themed caption (5657e62b) | skip |
| 197 | faded icon tint | FORK-ONLY (tab-icons merge 367015d3) | skip |

## src/AcDream.App/UI/UiEvent.cs

| main lines | summary | series | notes |
|---|---|---|---|
| 23-24 | `UiEventType.ScrollHorizontal = 0x0B` | 4-scroll (226ac1ff) | clean. BASE does not use 0x0B |

## src/AcDream.App/UI/UiMarkupList.cs

| main lines | summary | series | notes |
|---|---|---|---|
| 41-42 | `HeaderFont` | FORK-ONLY-modern-theme-or-contract (a87457da, SemiBold header) | skip |
| 63 | `internal int TopRowForTest => _topRow;` | 4-scroll (ce549264) | used by UiMarkupListScrollbarTests (followups) |
| 153, 159 | `DrawContainer(...)`, `DrawSelectionBand(...)` calls (plain list) | FORK-ONLY: themed lists (864292d9) | skip; keep BASE fill/outline/band |
| 216-217 | plain list: the wheel is taken only when `items.Count > visibleRows` | 4-scroll-followups (28cc84d8) | clean |
| 331, 341 | DrawContainer / DrawSelectionBand calls (column list) | FORK-ONLY 864292d9 | skip |
| 478-511 (BASE 476-500) | themed button cell (`PluginUiStyle.Button/Faded`) | FORK-ONLY (8cbe723d list button column + a331421e 0.1.26 sync) | skip |
| 723 | column list: the wheel is taken only when `rowCount > visibleRows` | 4-scroll-followups (28cc84d8) | clean |
| 802-821 | `DrawContainer`, `DrawSelectionBand` (PluginUiStyle.Surface/Row) | FORK-ONLY 864292d9 | skip |
| 830 (BASE 796-800) | themed list scrollbar becomes `PluginUiStyle.ScrollThumb(...)` | FORK-ONLY a55d8425 / 864292d9 | skip; keep BASE flat drawing (Field fill, Border outline, Muted thumb inset by 3, two 1-pt Muted ticks) |

## src/AcDream.App/UI/UiMarkupLog.cs

| main lines | summary | series | notes |
|---|---|---|---|
| 101-108 (BASE 101-102) | themed `PluginUiStyle.Surface` container + `_bar.ThemePalette = p` | FORK-ONLY: themed logs (864292d9) | skip, including line 108 (`UiScrollbar.ThemePalette` does not exist on BASE) |
| 132-133 | `if (!Scroll.HasOverflow) return false;` (a log with nothing to scroll leaves the wheel to the group around it) | 4-scroll-followups (28cc84d8) | `UiScrollable.HasOverflow` is on BASE |

## src/AcDream.App/UI/UiScrollbar.cs

| main lines | summary | series | notes |
|---|---|---|---|
| 24-26 | `public PluginUiPalette? ThemePalette` | FORK-ONLY: themed lists/sliders (864292d9) | skip |
| 329-336, 346-365 | `DrawThemedPlain` (PluginUiStyle.Slider / ScrollThumb) | FORK-ONLY 864292d9 | skip |
| 337-345 | horizontal **Model** branch in `DrawThemedPlain`: `ModelThumbRect` → `PluginUiStyle.ScrollThumbHorizontal` | 4-scroll (73fbd4f1), but nested in fork-only code | **DEPENDENCY**, see below: re-express as flat drawing in BASE's `DrawPlainScalar` |
| 369-373 | `if (ThemePalette is {} palette) { DrawThemedPlain(...); return; }` | FORK-ONLY 864292d9 | skip |

### What BASE draws today, and what to write instead
On BASE a themed markup scrollbar goes through `PluginMarkupTheme.Register` → `case UiScrollbar` (BASE PluginMarkupTheme.cs:133-138). That sets `RetailArt = false`, `PlainTrackColor = p.Field`, `PlainBorderColor = p.Border`, `PlainNubColor = p.Muted`. `OnDraw` → `DrawPlainScalar`, which draws a track fill, a 1-pt outline, and a **6-pt nub at `ScalarPosition`**. A Model-backed bar never updates `ScalarPosition`, so on BASE a themed Model bar draws its nub stuck at the start. This is a latent BASE bug that also affects UiMarkupLog's themed bar. BASE's themed UiMarkupList scrollbar avoids it by drawing itself flat: Field fill, Border outline, a Muted thumb at `x+3, width-6` from `UiScrollbar.ThumbRect`, and two 1-pt Muted ticks at the arrow-button centres.

For the scroll groups (UiScrollArea bars are Model-backed, vertical and horizontal), add a flat Model branch to BASE's `DrawPlainScalar` after the track and outline. It replaces both 73fbd4f1's horizontal branch and fork 864292d9's vertical Model branch:
```csharp
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
    return;   // optionally also the 1-pt Muted ticks at the button centres, as UiMarkupList does
}
```
`AxisExtent` (private static, BASE line 763) and `ModelThumbRect` (BASE line 150) already exist, and the input path already uses the model in plain mode. This is about 15 lines, upstream-friendly, and also fixes BASE's log bar. Do not bring `ThemePalette`, `DrawThemedPlain` or `PluginUiStyle`.

## src/AcDream.App/UI/RetainedUiInputBinding.cs (also defines RetainedWheel on BASE)

| main lines | summary | series | notes |
|---|---|---|---|
| 22 | doc comment on `AddScroll` (dx, dy) | 4-scroll (226ac1ff) | BASE already has `AddScroll(Action<int,int>)` from 31c7502d (<number>) |
| 125-133 (BASE 124-128) | Silk surface keeps only the dominant axis of a diagonal trackpad event | 4-scroll (226ac1ff + ce549264) | changes behaviour: BASE reports both signs (BASE test `WheelEvent_reportsBothAxisSigns`, `(0.3,-0.2)→(1,-1)`) |
| 209 (BASE 204-212) | `OnScroll(dx, dy) => _root.OnScroll(dx, dy, _root.ShiftHeld)`; BASE's `RetainedWheel.Vertical` call and private `ShiftHeld()` removed | 4-scroll (226ac1ff); the removal came in merge 54d19bb7 | see the RetainedWheel section |
| deletion of BASE 452-462 | `internal static class RetainedWheel` removed | merge 54d19bb7 + 9be9e56f (which restores the behaviour in UiRoot) | |

## New files (all clean flex: no fork-only symbols)

| file | blame | notes |
|---|---|---|
| UI/Layout/Flex/FlexTypes.cs | 1-engine (c2284293) | self-contained (System, System.Collections.Generic only) |
| UI/Layout/Flex/FlexNode.cs | 1-engine 104 lines; 4-scroll 8463b376 32 lines (scroll fields) | split by series if porting per series |
| UI/Layout/Flex/FlexLayout.cs | 1-engine 658 lines; 4-scroll 8463b376 88 lines + ce549264 3 lines (`ScrollbarThickness`, scrollbar settle, grid lines) | split by series |
| UI/Layout/Flex/FlexFit.cs | 3-markup 40 lines (2a115f5a, 9f7bf5e0); 4-scroll 5 lines (8463b376, ce549264) | |
| UI/UiFlexBox.cs | 3-markup c15efdec 195 lines; 4-scroll 5b9b7c7e 4; 5-controls a14340b9 16 + 5fb02a19 8 (`SyncCollapse`, `ExpandedMax/MinHeight`, `_collapsed`) | depends on `MarkupContentSize` (3-markup, other group) and `UiMarkupExpander.FlexPlaced` (5-controls, other group; BASE has `HeaderHeight`) |
| UI/UiFlexGroup.cs | 3-markup 18; 4-scroll 19 (`IUiScrollHost`, `UseScroll`, `ScrollArea`) | |
| UI/UiScrollArea.cs | 4-scroll 5b9b7c7e 201; 73fbd4f1 7 (`RegisterTheme`, lines 202-207) | `RegisterTheme` works unchanged on BASE (`PluginMarkupTheme.Register(panel, bar, <scroll/>)`), but you get the flat plain look only after the UiScrollbar fix above. Uses BASE-present `UiScrollable.SetExtents/ScrollByLines/SetScrollY/HasOverflow/PositionChanged` and `RetailScrollbarChrome.ApplyVertical/ApplyHorizontal`. `ZOrder = int.MaxValue` on a child is fine on BASE (fork's UiBand change is about root z only) |
| UI/UiScrollPanel.cs | 4-scroll 5b9b7c7e | clean |

---

## Dependencies

1. **UiScrollbar themed drawing (73fbd4f1 inside fork 864292d9):** needs `UiScrollbar.ThemePalette`, `DrawThemedPlain` and `PluginUiStyle.ScrollThumb/ScrollThumbHorizontal` (fork modern theme + 2e29c130 rounded shapes). Too much to bring along. **Replace** with the flat Model branch in `DrawPlainScalar` above (about 15 lines).
2. **`UiElement.OnDrawingChild` (fork upper layer):** threaded through the 4-scroll draw loop (line 511). **Strip.** Nothing in flex needs it.
3. **`ThemeDrawCapture` test helper** (tests/.../UI/ThemeDrawCapture.cs, fork 017e093b/2e29c130, 57 lines). Used by many flex tests: FlexDemoSample, MarkupFlex*, MarkupScrollWindow, MarkupTitleBar, PluginTitleBar, UiContentOffset, UiRootWheelRouting, UiScrollGroup. **Bring along** (small). Change `context.Begin(size, null, pixelScale)` to `context.Begin(size, null)`, because BASE `UiRenderContext.Begin(Vector2, BitmapFont?)` has no pixelScale (that is the Retina fork). Drop the `pixelScale` parameters; no flex test passes one. `TextRenderer.FloatsPerVertex` and `DebugSpriteSegmentVerts` are on BASE. The alternative is BASE's `MarkupDrawProbe`, but it does not call `Begin`, so clip and transform tests want ThemeDrawCapture.
4. **`MarkupContentSize.ThemedToggleCaptionX = 1 + PluginUiStyle.SwitchWidth + SwitchCaptionGap`** (other group's file). This is fork modern switch geometry. BASE's themed toggle draws `p.DrawCheck` with the caption at 17, the same as Classic. So the BASE port must use 17, and the 63f assertions in tests become 45f (see Tests).
5. **`UiMarkupExpander.FlexPlaced`** (5-controls) and **`MarkupContentSize`** (3-markup) are referenced from UiFlexBox. These are other groups' files; port them in the same series.
6. **UiRoot wheel routing:** no fork deps. `RouteHorizontal`'s `BlockerFor` comes from BASE's dialogs, so take the merged (54d19bb7) form directly.
7. Not dependencies, just things to leave out: UiSimpleButton theming, UiMarkupList/Log `PluginUiStyle`, `HeaderFont`, UiRoot tooltip, PressStarting, CancelDrag, UiBand, ConstrainWindowDrag, Rynth DefaultTextInput, and the `AcceptsFocus` virtual. None is referenced by flex code. (I checked: no `override bool AcceptsFocus` or `ConstrainWindowDrag` in flex files.)

### Replacing BASE's `RetainedWheel.Vertical` stopgap with main's horizontal-first routing (9be9e56f)
BASE today (RetainedUiInputBinding.cs): `OnScroll(dx,dy)` calls `RetainedWheel.Vertical(dx, dy, ShiftHeld())` → `_root.OnScroll(step)`. The rule is: any vertical part wins; a pure sideways tick counts as the wheel only while Shift is held; otherwise nothing. Horizontal never reaches the UI.

Replacing it on BASE involves:
1. **UiEvent.cs:** add `ScrollHorizontal = 0x0B` (main 23-24).
2. **UiRoot.cs:** replace BASE's `public void OnScroll(int dy)` (BASE 876-899) with main lines 902-997 verbatim. That is the `OnScroll(int dy)` wrapper, `OnScroll(int dx, int dy, bool shift)` including the 9be9e56f branch `else if (shift && dy == 0 && dx != 0) taken = RouteHorizontal(dx) || RouteVertical(dx);`, `internal bool ShiftHeld`, `RouteHorizontal` (with popup plus `BlockerFor`), and `RouteVertical` returning bool. Also take the 28cc84d8 popup-dismiss and hover-refresh lines; they sit inside the same method and are tested by UiRootWheelRoutingTests. Then change line 1018 (Tab) to `MoveMarkupFocus(ShiftHeld)`.
3. **RetainedUiInputBinding.cs:**
   - `OnScroll(int dx, int dy) => Invoke(() => _root.OnScroll(dx, dy, _root.ShiftHeld));`
   - delete the private `ShiftHeld()` (BASE 211-212) and the `RetainedWheel` class (BASE 452-462)
   - add the doc line on `AddScroll`
   - in `SilkRetainedMouseSurface.OnScroll` add the dominant-axis filter (main 125-133)
   - `using Silk.NET.Input` stays (IMouse etc.)
4. **Behaviour check against RetainedWheel:**
   - Shift + sideways tick (macOS Shift+wheel): over a `scroll="x"` group it now scrolls sideways; otherwise it is the same vertical step as before. A focused `<number>` still steps 10, because `UiMarkupNumber` reads its own `ShiftHeld`.
   - Plain sideways tick: BASE dropped it. Now it reaches only a horizontally scrolling group, never the world.
   - Shift + vertical wheel: now tries horizontal first, then falls back to vertical. Over non-x content this is identical to before.
   - Diagonal events: the surface now keeps only the dominant axis.
5. **Tests on BASE:**
   - RetainedUiInputBindingTests: delete the `RetainedWheel.Vertical` theory. Add main's `MouseScrollPassesBothStepsAndTheRootsShiftState`, and change `MouseSurface.Scroll` to `_scroll?.Invoke`. BASE's `Sideways_tick_reaches_the_root_as_a_step_only_while_Shift_is_held`, with its `ShiftKeyboard` proxy, still passes against the new code. Main dropped it, but it is worth keeping: it is the end-to-end macOS regression test.
   - SilkRetainedMouseScrollTests: replace `WheelEvent_reportsBothAxisSigns` with main's `HorizontalWheelEvent_emitsOneHorizontalStep`, `DiagonalWheelEvent_keepsOnlyTheDominantAxis` and `NaNWheelComponents_areTreatedAsZero`, plus the `Raise/RaiseHorizontal/Raise(ScrollWheel)` helpers. Change line 24's `onScroll` to `(dx, dy) => root.OnScroll(dx, dy, shift: false)` (54d19bb7).
   - Add MarkupNumberTests `Number_ShiftSidewaysTickStepsTenWhenNothingScrollsSideways` (main 266-279) and UiRootWheelRoutingTests (whole file).
6. **Ordering:** if 4-scroll is ported before 4-scroll-shift-sideways as separate commits, deleting RetainedWheel in the 4-scroll commit reintroduces the bug 9be9e56f fixed. In that window a Shift+sideways tick on a `<number>` routes only horizontally and is lost. Either fold the 9be9e56f branch into the 4-scroll UiRoot commit, or keep the binding on `RetainedWheel` until the shift-sideways commit, which then does the switch and the deletion together.

---

## Tests (tests/AcDream.App.Tests/UI/ files touched by flex commits)

| file | series | fork-only content to drop or rewrite |
|---|---|---|
| Layout/Flex/FlexTestNodes, FlexLayoutMeasureTests, FlexLayoutArrangeTests, FlexLayoutNestingTests, FlexLayoutFuzzTests, FlexLayoutWrapScrollTests | 1-engine | none |
| Layout/Flex/FlexLayoutScalingTests | 1-engine (dbd0322b) + 4-scroll-followups (ddcc2d13) | none |
| Layout/Flex/FlexLayoutScrollbarTests | 4-scroll (8463b376) | none |
| Layout/Flex/FlexFitTests | 3-markup | none |
| MarkupFlexAttributesTests | 3-markup | none |
| MarkupContentSizeTests | 3-markup (8491fa9b) | **Rewrite** `A_toggle_starts_its_caption_after_its_lamp_or_switch`: themed width 63f is the fork switch; on BASE themed = 45f (caption at 17) |
| MarkupFlexTests | 3-markup (c15efdec, c76c878f) + 5-controls (5fb02a19) | Uses ThemeDrawCapture (bring it). **Rewrite** `A_theme_switch_reflows_items_whose_size_depends_on_it` (45→63 is the fork switch width). On BASE a theme switch changes a toggle's size only through the font: give `PluginUiThemeSettings(modernFont: …)` a font with different advances and assert the reflow from that |
| MarkupFlexWindowTests | 3-markup, 4-scroll (681189b2), 6-bound-visible | ThemeDrawCapture only |
| RetailWindowManagerMinimumTests | 3-markup | none |
| FlexDemoSampleTests | 3-markup, 4-scroll, 5-controls | ThemeDrawCapture only |
| MarkupTitleBarTests | 2-titlebar (e4c69c2a, 46ec8946, ad953c38) | **Rewrite** `Themed_TheBarFollowsTheThemeAndBack` (main 158-187): uses `BundledUiFontWeight.SemiBold` and `PluginUiThemeSettings(modernTitleFont: new(() => bold))`, both fork SemiBold/Lazy-font and absent on BASE. Use `new PluginUiThemeSettings(null, modernFont)` and assert the bar takes `ModernFont`. `Themed_TheCloseButtonFollowsALiveThemeSwitchAfterBuild` (Top 5→3) pins the fork's modern close geometry (`PluginUiStyle.CloseButton`, `TitleBarHeight = PluginUiStyle.HeaderHeight`); adjust it to whatever the BASE title-bar port uses |
| PluginTitleBarTests | 2-titlebar (826cadd5, 46ec8946, ad953c38) | **Fork-only shapes:** `Modern_DrawsAMutedXThatTakesTheTextColourOnHover` (128-146, modern ghost X via PluginUiStyle.CloseButton), `ATheme_CentresTheCloseButtonInTheBand` (48-58, Top 3) and the themed half of `TheTitleTextOriginIsPinnedPerLook` (179-180, (12, …24-pt band)). Rewrite these to the BASE flat themed close button, or drop them. `TheCloseButtonSaysWhatItDoes` lines 188-189 use `PluginWindowChrome.DockedCloseTooltip`, which only the fork dock (PluginSidePanel) uses; drop those two lines (or keep the constant, which is harmless) |
| PluginWindowChromeTests | 2-titlebar (1d18d421) | none found |
| MarkupWindowMountTests | 2-titlebar (814f2c61), 4-scroll (681189b2) | **Drop** `TheDockLeavesItsMinimizeOffAWindowWithABar` and `TheDockLeavesItsMinimizeOffALayoutWindowWithoutABar` (main 79-127): fork PluginSidePanel dock. If the BASE port omits 681189b2's dock-minus code, that part goes too |
| MarkupScrollWindowTests | 4-scroll (914ed099) | ThemeDrawCapture only |
| UiContentOffsetTests | 4-scroll (71d8f955) | ThemeDrawCapture only |
| UiScrollGroupTests | 4-scroll (5b9b7c7e, 73fbd4f1, ce549264), followups | **Rewrite** `Bars_take_the_window_theme_and_go_back_to_classic_art` (main ~528-546): asserts `area.Vertical/Horizontal.ThemePalette` (fork). On BASE assert `RetailArt == false` and `PlainTrackColor == PluginUiPalette.Moss.Field` (or PlainNubColor == Muted), then back to `RetailArt == true` |
| PluginThemeControlTests | mostly FORK-ONLY-modern; only 73fbd4f1's `A_themed_horizontal_scroll_bar_draws_a_slim_thumb_along_its_track` (main 539-563) is flex | The file does not exist on BASE; don't port it. If you want coverage, add the horizontal-thumb test elsewhere (e.g. UiScrollGroupTests), rewritten for the flat thumb. Drop the `ThemePalette = P` setter and set `RetailArt=false, PlainNubColor=P.Muted`; assert the thumb is about 17 pt long (68-pt track × ¼) spanning y 3..13, not the 4-pt pill centred at y 6..10 |
| MarkupFlexControlsTests | 5-controls | ThemeDrawCapture only |
| MarkupExpanderTests | 54d19bb7 merge only (line 593) | **Do not port.** The hunk adapts to the fork's `Lazy<UiDatFont>` ctor; BASE's `new PluginUiThemeSettings(null, modern)` is already right |
| MarkupListMultiSelectTests | 54d19bb7 merge only (230-236) | **Do not port.** It adapts to the fork's rounded `PluginUiStyle.Row`; BASE's `Assert.Single(...)` is right because BASE's band is one quad |
| MarkupNumberTests | 9be9e56f (266-279) + FORK-ONLY e239a759 (line 2 `using System.Numerics`, 514-517 chevron colour) | Port only 266-279 |
| RetainedUiInputBindingTests | 4-scroll (226ac1ff) + merge | see the RetainedWheel section |
| SilkRetainedMouseScrollTests | 4-scroll (226ac1ff, ce549264) + merge | see the RetainedWheel section |
| UiRootWheelRoutingTests | 4-scroll, followups, shift-sideways | ThemeDrawCapture only (line ~195); depends on scroll groups |
| UiMarkupListScrollbarTests | 4-scroll-followups (28cc84d8) | none (needs `TopRowForTest`, main UiMarkupList.cs:63) |
| UiMarkupLogTests | 4-scroll-followups (28cc84d8) | none |
| MarkupCheckboxTests, MarkupListHeaderTests, MarkupRadioTests, MarkupSplitTests, PluginDialogHostTests, UiMarkupColorTests, UiMarkupHotkeyTests, UiPluginDialogTests | FORK-ONLY-modern-theme-or-contract only | not part of the port |

---

## Port instructions per series (group C files)

**1-engine.** Add Layout/Flex/FlexTypes.cs, FlexNode.cs and FlexLayout.cs without 8463b376/ce549264's scroll parts. The easiest route is to cherry-pick c2284293, 60063be6, 17c77905, 90e135b7, 75ea8cb8, e358693f, 0548b87b and dbd0322b in order; they touch only new files. Tests: FlexTestNodes plus the Measure, Arrange, Nesting, Fuzz, WrapScroll and Scaling tests. No fork deps.

**2-titlebar.** None of this group's source files is involved. For tests: PluginTitleBarTests, MarkupTitleBarTests and PluginWindowChromeTests need the rewrites above (modern close X and Top 3, SemiBold title font, DockedCloseTooltip).

**3-markup.**
- UiElement: line 504 (`LayoutChildren();` in place of the ApplyAnchor loop) and 616-626 (`LayoutChildren` virtual).
- UiPanel: `UiLabel.SizedByLayout` and its OnDraw (64-70, 78-98).
- New files UiFlexBox.cs (without `SyncCollapse`, `ExpandedMax/MinHeight`, `_collapsed`: those are 5-controls) and UiFlexGroup.cs (without the scroll parts); FlexFit.cs.
- Tests: FlexFitTests, MarkupFlexAttributesTests, MarkupContentSizeTests (rewrite 63→45), MarkupFlexTests (rewrite the theme-reflow test), MarkupFlexWindowTests, RetailWindowManagerMinimumTests, FlexDemoSampleTests. Bring ThemeDrawCapture (adapted Begin) in this series, since it is the first that needs it, unless 2-titlebar already brought it.

**4-scroll** (+ followups + shift-sideways, best squashed for UiRoot, see Ordering above).
- UiElement: 97, 101-102, 109-133, 512-515 (no `OnDrawingChild` line 511), 535-550, 577-586, 640-648.
- UiEvent: 23-24.
- UiRoot: 727-729 + delete the old `var sp` (BASE 774); 902-997 (complete wheel routing incl. 28cc84d8, 9be9e56f, 54d19bb7 bits); 1018; 1140; 1145-1151.
- RetainedUiInputBinding: 22, 125-133, 209; delete `ShiftHeld()` and `RetainedWheel`.
- UiMarkupList: 63, 216-217, 723.
- UiMarkupLog: 132-133.
- New: UiScrollArea.cs, UiScrollPanel.cs, the scroll parts of UiFlexGroup.cs (`IUiScrollHost`, `UseScroll`, `ScrollArea?.ApplyFlex`, OnEvent), UiFlexBox (5b9b7c7e's 4 lines), and the 8463b376/ce549264 parts of FlexNode, FlexLayout and FlexFit.
- **UiScrollbar:** don't port 864292d9 or 73fbd4f1. Add the flat Model-thumb branch to BASE `DrawPlainScalar` (code above).
- Tests: UiContentOffsetTests, UiScrollGroupTests (rewrite the ThemePalette test), MarkupScrollWindowTests, FlexLayoutScrollbarTests, UiRootWheelRoutingTests, RetainedUiInputBindingTests and SilkRetainedMouseScrollTests changes, MarkupNumberTests 266-279, UiMarkupListScrollbarTests, UiMarkupLogTests, the ddcc2d13 change to FlexLayoutScalingTests, and the horizontal flat-thumb test.

**5-controls-in-flex.** In group C files only the UiFlexBox.cs parts: `SyncCollapse`, `ExpandedMaxHeight/ExpandedMinHeight`, `_collapsed` and `MarkupContentSize.Collapsed` in `Measure/InputsChanged`, from a14340b9/5fb02a19 (needs `UiMarkupExpander.FlexPlaced`). The 54d19bb7 merge's UiRoot bits are already covered under 4-scroll. Do not port its MarkupExpanderTests or MarkupListMultiSelectTests hunks. Tests: MarkupFlexControlsTests, plus the 5fb02a19 part of MarkupFlexTests.

**6-bound-visible.** Nothing in group C source; the MarkupFlexWindowTests hunk from 2e6e1d29 only.
