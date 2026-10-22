# Flex port audit (Task 8)

Branch `feat/plugin-flex-layout` at d0be9eb0 (7 commits on 1a777f1a) against fork main eca0047c.
Method: for every file the branch touches, `git diff eca0047c d0be9eb0 -- <file>`. Each remaining
difference is classed as a fork-only cut, a decided replacement, or upstream (BASE) content the fork
changed for fork reasons. For files with large fork-only diffs (PluginSidePanel, RetailUiRuntime,
RetailWindowManager, UiPanel, UiMarkupList, PluginMarkupTheme, UiMarkupNumber, UiMarkupLog,
UiMarkupExpanderHeader, docs), `git diff 1a777f1a d0be9eb0` was also read to confirm that the branch is
BASE plus the flex hunks only. Reverse check: the files the flex series touched (commit-series.txt minus
FORK-ONLY, plus the files in merge 54d19bb7's combined diff) were compared with the branch's file list,
and the eca0047c-only lines of each file were scanned for flex content.

## Per file

### Same as eca0047c (41)

- AcDream.Plugins.FlexDemo/AcDream.Plugins.FlexDemo.csproj: same
- AcDream.Plugins.FlexDemo/controls.xml: same
- AcDream.Plugins.FlexDemo/finder.xml: same
- AcDream.Plugins.FlexDemo/gallery.xml: same
- AcDream.Plugins.FlexDemo/packages.neutral.lock.json: same
- AcDream.Plugins.FlexDemo/plugin.json: same
- AcDream.Plugins.FlexDemo/settings.xml: same
- UI/Layout/Flex/FlexFit.cs: same
- UI/Layout/Flex/FlexLayout.cs: same
- UI/Layout/Flex/FlexNode.cs: same
- UI/Layout/Flex/FlexTypes.cs: same
- UI/MarkupFlexAttributes.cs: same
- UI/RetainedUiInputBinding.cs: same (RetainedWheel removed as on the fork)
- UI/UiEvent.cs: same
- UI/UiFlexBox.cs: same
- UI/UiFlexGroup.cs: same
- UI/UiMarkupExpander.cs: same
- UI/UiPluginContentHost.cs: same
- UI/UiScrollArea.cs: same
- UI/UiScrollPanel.cs: same
- tests FlexDemoSampleTests: same
- tests Layout/Flex/FlexFitTests: same
- tests Layout/Flex/FlexLayoutArrangeTests: same
- tests Layout/Flex/FlexLayoutFuzzTests: same
- tests Layout/Flex/FlexLayoutMeasureTests: same
- tests Layout/Flex/FlexLayoutNestingTests: same
- tests Layout/Flex/FlexLayoutScalingTests: same
- tests Layout/Flex/FlexLayoutScrollbarTests: same
- tests Layout/Flex/FlexLayoutWrapScrollTests: same
- tests Layout/Flex/FlexTestNodes: same
- tests MarkupFlexAttributesTests: same
- tests MarkupFlexWindowTests: same
- tests MarkupScrollWindowTests: same
- tests MarkupWindowMountTests: same
- tests PluginWindowChromeTests: same
- tests RetailWindowManagerMinimumTests: same
- tests SilkRetainedMouseScrollTests: same
- tests UiContentOffsetTests: same
- tests UiMarkupListScrollbarTests: same
- tests UiMarkupLogTests: same
- tests UiRootWheelRoutingTests: same

### Differ from eca0047c (29)

- AcDream.slnx: cuts: CanvasDemo (markup canvas), WorldMarkersDemo (world markers), ThemeGallery (theme gallery sample). FlexDemo sits after BASE's ControlsDemo (order only).
- samples/AcDream.Plugins.FlexDemo/FlexDemoPlugin.cs: replacements: comment "from the dock's gear" becomes "from the plugin sidepanel" (dock gear is fork-only; classify-A FlexDemo note).
- docs/plugin-ui-markup.md: cuts: dock icon order, PNG dock icons, SVG icons (SVG/PNG dock icons), dock modern-look and ThemeGallery paragraphs (modern theme, dock redesign, gear), `<canvas>` section and table row (markup canvas), file icons section and `file` kind rows (markup file icons), themed list button cell wording (modern theme), "The plugin dock" section (dock redesign), canvas/fonts/shapes/HiDPI/layers/regions/world markers/markup canvas/SVG test paragraphs (fork features). Upstream-only: BASE's shelf/appearance/theme-token wording, "The plugin shelf" section, list icons rows. Replacements: close X is "a plain palette button with an x" (flat close button); scroll bar "flat thumb in the palette's muted colour" (DrawPlainScalar flat thumb); toggle row "its lamp, then its caption" (toggle caption at ClassicToggleCaptionX); "dock slot/dock" becomes "shelf button/shelf" in the flex/title bar text, including the "–" and sideways-wheel lines. New on the branch: the flex test-classes paragraph in Tests (Task 7 deviation: the fork had none). No flex section missing (Title bar, Flex layout and all subsections, Scrolling groups present).
- UI/MarkupContentSize.cs: cuts: `case UiMarkupCanvas` (markup canvas). Replacements: ThemedToggleCaptionX deleted, toggle captionX = ClassicToggleCaptionX in both looks, its doc reworded (Task 3 deviation 3).
- UI/MarkupDocument.cs: cuts: `barPanel.HasTitle`, label-title `HasTitle`/`RegisterTitle` (modern theme title); `{ Canvases = CollectCanvases(panel) }`, `case "canvas"`, `CollectCanvases` (markup canvas); `<icon file>`, `iconkind="file"` on list/button/tab/column, `ValidateIconKind` `file`, BuildFileIconSource/BindPathLiteralOrBinding/FileIconIds (markup file icons, tab icons). Replacements: label title registered with BASE's `PluginMarkupTheme.Register(titlePanel, panel.Children[^1], …)` inside the label branch (classify-A 2-titlebar instruction).
- UI/MarkupWindow.cs: cuts: `Canvases` property (markup canvas). Replacements: Register doc drops "the path a dock slot takes" (Task 2 deviation 2).
- UI/PluginMarkupTheme.cs: cuts: `RegisterTitle` (modern theme title), `HasModernFont`/lazy ModernFont (sharp font twin), button/menu/meter/scrollbar/field `ThemePalette` and `PluginUiStyle.Hover` menu hover (themed buttons/menus/lists/sliders/fields), list `HeaderFont` (SemiBold header). Upstream-only: BASE's `ModernFont is { } modern` binding and `p?.Selected` menu hover. Replacements: RegisterTitleBar uses `panel.ModernFont ?? classicFont` and a reworded doc.
- UI/PluginSidePanel.cs: cuts: the whole dock redesign (floating dock, edge rail, snap, grip, hover label, gear toggle, SVG/PNG dock icons, `_order`, ReleaseSvgIcon, ghost minimize `_themes` argument). Upstream-only: BASE shelf code. Flex hunks present (base diff checked): `using System.Linq`, Add doc, title-bar X tooltip instead of "–", no "–" on a bar-less layout window, nullable `Minimize` with the two guarded removals. Replacements: "dock slot" comment becomes "shelf button" (Task 4 deviation 1).
- UI/PluginTitleBar.cs: replacements: themed close button is a flat UiSimpleButton face (Field/Border/Text, "x" caption) with its own `ThemePalette`, own `_hovered/_pressed` tracker, `base.OnDraw` for the face and focus outline; class doc reworded. Cuts: `PluginUiStyle.CloseButton`, `ThemeState`, `KeyboardFocused` (modern theme, UiPointerState).
- UI/PluginWindowChrome.cs: replacements: `TitleBarHeight = 24f` literal; `DockedCloseTooltip` "Close (reopen from the plugin sidepanel)"; class doc "the themes" (Task 2 deviation 3).
- UI/RetailUiRuntime.cs: cuts: every fork hunk (sharp/SemiBold fonts, extended levels, world markers, canvas positions/stacks/markup canvases, screen size, pixel scale, dock wording, file/SVG/PNG icon caches, PluginMarkupIconResolver, TogglePluginAppearance, font services). Flex hunks present (base diff checked): BuildWindow with `fallbackTitle`, `window.Frame`, `window.Register(...)`.
- UI/RetailWindowManager.cs: cuts: banded `BringToFront`, `BringToFront(window, UiBand)`, `Raise`, `Compact` (z-order bands). Upstream-only: BASE's plain BringToFront. Flex hunks present: string `ComputeAuthoredGeometryRevision`, `EnforceMinimumSize` + `FitAxis`.
- UI/UiElement.cs: cuts: virtual `AcceptsFocus` (canvas focus), `ConstrainWindowDrag` (window drag snap), `OnDrawingChild` and its call (upper draw layer). All 3-markup/4-scroll hunks present.
- UI/UiMarkupExpanderHeader.cs: cuts: PluginUiStyle chevron and focus ring (modern theme shapes 45a83200). Upstream-only: BASE's single-arrow draw (`ThemePalette?.Text`). Flex hunk present: `CaptionX` const and its use.
- UI/UiMarkupList.cs: cuts: `HeaderFont` (SemiBold header), DrawContainer/DrawSelectionBand (themed lists), PluginUiStyle button cell (list button column theme), PluginUiStyle.ScrollThumb (themed list scrollbar). Upstream-only: BASE flat fills, flat themed scrollbar with ticks. Flex hunks present: `TopRowForTest`, both wheel guards.
- UI/UiMarkupLog.cs: cuts: PluginUiStyle.Surface and `_bar.ThemePalette` (themed logs). Upstream-only: BASE flat fill/outline. Flex hunk present: `HasOverflow` guard.
- UI/UiMarkupNumber.cs: cuts: themed ghost spin buttons (modern theme shapes e239a759). Flex hunk present: `Anchors = AnchorEdges.None`.
- UI/UiPanel.cs: cuts: UiSimpleButton `ThemePalette`, `_pointer`/`ThemeState`, `KeyboardFocused`, `DrawThemedFace`, faded icon/caption (themed buttons, tab icons merge). Flex hunk present: `UiLabel.SizedByLayout` and its draw.
- UI/UiRoot.cs: cuts: `using AcDream.App.Rendering`, `_tooltipExpired` and tooltip expiry, `PressStarting`, `OnDrawingChild` override, `ConstrainWindowDrag` call, hidden DefaultTextInput guard (Rynth chat fix), banded BringToFront, `CancelDrag`. Upstream-only: BASE tooltip timeout. All wheel routing, press-position and reveal hunks present.
- UI/UiScrollbar.cs: cuts: `ThemePalette`, `DrawThemedPlain` (themed lists/sliders). Replacements: flat Model thumb in `DrawPlainScalar` for both axes.
- tests MarkupContentSizeTests: cuts: `A_tab_icon_adds_the_same_square_column_as_a_button` (72d8ba64, fork-only tab icons; Task 3 report). Replacements: themed toggle 63 -> 45 with a comment.
- tests MarkupFlexControlsTests: replacements: 3 extra tests (An_expander_may_hold_a_flex_container, A_split_without_a_size_takes_its_content_size, An_expander_keeps_its_minh_only_while_expanded); nothing removed.
- tests MarkupFlexTests: replacements: `A_theme_switch_reflows_items_whose_size_depends_on_it` driven by font advances instead of the fork switch width.
- tests MarkupNumberTests: cuts: `using System.Numerics` and the muted chevron assert (modern spin chevrons e239a759). Upstream-only: BASE `CountQuads(Moss.Text) >= 6`. Flex test present (base diff: only `Number_ShiftSidewaysTickStepsTenWhenNothingScrollsSideways` added).
- tests MarkupTitleBarTests: replacements: bar font is `ModernFont` via `PluginUiThemeSettings(null, modern)`; cuts: SemiBold font and the `HasTitle` assert.
- tests PluginTitleBarTests: replacements: modern muted-X test replaced by `Themed_CloseButtonIsAFlatFieldFaceWithAnX` and `Themed_ThenClassic_TheCloseButtonDrawsNoFace`; tooltip text "plugin sidepanel".
- tests RetainedUiInputBindingTests: replacements: upstream `Sideways_tick_reaches_the_root_as_a_step_only_while_Shift_is_held` and its ShiftKeyboard proxy kept.
- tests ThemeDrawCapture: replacements: `Begin(size, null)`, no pixelScale parameters.
- tests UiScrollGroupTests: replacements: theme test asserts RetailArt/PlainTrackColor on both bars instead of `ThemePalette`; added `A_themed_horizontal_scroll_bar_draws_a_flat_thumb_along_its_track` (re-homed from fork-only PluginThemeControlTests).

### Flex-series files not on the branch (reverse check)

- samples/AcDream.Plugins.ThemeGallery/gallery.xml: cuts: fork-only sample (2-titlebar edits to a fork-only file).
- src/AcDream.App/UI/PluginUiStyle.cs: cuts: 826cadd5 `CloseButton` lives in the fork-only style class; replaced by the flat close button.
- tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs: cuts: fork-only file; its one flex test is re-homed in UiScrollGroupTests.
- Merge 54d19bb7 files not on the branch (docs/plugin-api.md, AppHotkeyRegistry, InputCaptureSources, BufferedUiRegistry, PluginUiThemeSettings, UiField, UiMarkupToggle, ScopedPluginHost, IUiRegistry, SettingsStore, test csproj and lock, PluginKeyChordBridgeTests, MarkupExpanderTests, MarkupListMultiSelectTests, MarkupControlStateSettingsTests): fork-side merge resolutions. Their 1a777f1a..eca0047c diffs have no flex, titlebar, scroll-group or wheel-routing lines; MarkupExpanderTests/MarkupListMultiSelectTests are "do not port" in classify-C.

## Checks

- Fork-only grep (`PluginUiStyle|ModernTitleFont|…|RetainedWheel`, with `RegisterTitle([^B]|$)`) on d0be9eb0 src/tests/samples: no output.
- `InMarkup` whole-word grep minus `XNameInMarkup`: no output.
- `git diff --stat 1a777f1a d0be9eb0 -- docs/superpowers`: empty.
- `git log --oneline 1a777f1a..d0be9eb0`: 7 commits (830a303d, 6819d7a7, c571a960, c3895f75, b2010204, 3115f701, d0be9eb0).
- Brief verify diff (`Layout/Flex`, UiFlexBox, UiFlexGroup, UiScrollArea, UiScrollPanel, UiPluginContentHost, MarkupFlexAttributes): empty.

## Summary

70 branch files audited: 41 same as eca0047c, 29 differ only by fork-only cuts, decided replacements,
or BASE content the fork had changed. The 3 flex-series files missing from the branch are fork-only.
UNEXPLAINED: none. MISSING: none. Not pushed (controller ruling); the push follows the final review.
