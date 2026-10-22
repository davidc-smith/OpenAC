# Group B classification: `git diff feat/plugin-controls-upgrade main`

BASE = `feat/plugin-controls-upgrade` (1a777f1a). MAIN = `main` (eca0047c).
Line numbers are MAIN line numbers unless marked `base:`. Attribution comes from `git blame -l main` on the added lines, plus `git show` of the flex commits that touched each file. Lines removed from BASE were attributed by reading the flex commits' own diffs.

Flex commits that touch these files:
- 2-titlebar: 1d18d421 (chrome constants + authored-inputs revision), e4c69c2a (titlebar attribute, `fallbackTitle`), 814f2c61 (mount with content root, close button wired, dock X replaces "-"), 46ec8946 (close button follows live theme, TextOrigin), 826cadd5 (adds `PluginUiStyle.CloseButton`, which lives in fork-only PluginUiStyle.cs)
- 3-markup: 3969d797 (EnforceMinimumSize), 7896260d (review fixes), c15efdec (UiElement.LayoutChildren), b8592630 (FlexDemo sample + slnx)
- 4-scroll: 681189b2 (title-bar min width; no dock minus on bar-less layout windows), ce549264 (final review fixes)

---

## 1. src/AcDream.App/UI/PluginSidePanel.cs (566+/326-)

Almost all of this diff is the fork-only dock redesign. Only 5 hunks belong to flex, all from 2-titlebar plus one 4-scroll line.

### Flex hunks

| main lines | summary | series | notes |
|---|---|---|---|
| 1 | `using System.Linq;` | 2-titlebar (814f2c61) | Needed for `OfType<>()`/`Any()`. Implicit usings probably make it redundant, but keep it. |
| 121 | `Add` doc comment: "its dock slot (a minimize button too, unless the window has a title bar, whose X replaces it)" | 2-titlebar (46ec8946) | BASE wording is "Adds one manifest-scoped plugin window and its minimize affordance." On BASE write "its shelf button" instead of "dock slot". |
| 180-202 | Minimize button becomes optional: a window with a `PluginTitleBar` child gets `titleBar.Close.Tooltip = PluginWindowChrome.DockedCloseTooltip` and no "-". A window with a `UiPluginContentHost` child but no bar (a bar-less layout window) also gets no "-". Every other window gets `PluginMinimizeButton` as before. | 2-titlebar (814f2c61), lines 188-191 from 4-scroll (681189b2 + ce549264) | **Mixed in:** line 193 is `new PluginMinimizeButton(handle, _font, _themes)`. The third argument `_themes` is fork-only (ac3d8cc9, ghost minimize on themed windows). BASE's constructor is `PluginMinimizeButton(handle, _font)`. Line 204's `_entries.Add(handle, new ShelfEntry(button, minimize))` is unchanged in shape. Lines 203-213 (`handedOver`, `_order.Add`, `try/finally svgIcon?.Release()`) are fork-only (ae9cc033/7bd7d389). |
| 570-571 | `OnWindowUnregistered`: `if (entry.Minimize is { } minimize && ReferenceEquals(minimize.Parent, handle.OuterFrame)) handle.OuterFrame.RemoveChild(minimize);` | 2-titlebar (814f2c61) | Replaces base: 325-326. In the same hunk area, main 567-568 (`_order.Remove`, `ReleaseSvgIcon`) is fork-only. BASE's `entry.Button.DisposeSubscriptions()` (base: 327) must stay. |
| 659-660 | `Dispose`: the same null-safe minimize removal | 2-titlebar (814f2c61) | Replaces base: 457-458. main 658 `ReleaseSvgIcon` is fork-only. BASE's `entry.Button.DisposeSubscriptions()` (base: 456) stays. |
| 669 | `ShelfEntry(..., PluginMinimizeButton? Minimize)`: the minimize button becomes nullable | 2-titlebar (814f2c61) | Replaces base: 466. |

### FORK-ONLY hunks (all other hunks)

| main lines | summary | feature (commit) |
|---|---|---|
| 4 | `using static AcDream.App.UI.PluginUiStyle;` | modern plugin theme / dock (ccaee1df) |
| 8-14, 19-21, 27-31, 34-36 (base: 8-10, 13-23, 29-31, 35-39 removed) | dock fields, constants, `PluginDockMode`, gear toggle callback | dock redesign "one floating plugin dock in every theme" (ccaee1df); edge rail (1aa154bc); gear toggle (807739ee) |
| 47-89 (many small hunks; base: 49-104) | ctor and fields for the floating dock, grip, hover label | dock redesign (ccaee1df); edge rail (1aa154bc) |
| 99-119 | `Mode`, `_dragMode`, rail mode | dock redesign (ccaee1df); edge rail (1aa154bc) |
| 120, 122-123, 152 | unchanged BASE text that moved (blame 3b6bb588 Initial) | context only |
| 128-161 | `Add` overloads for SVG icons and `PluginDockIcons`; `handedOver`/`try` | SVG dock icons (7bd7d389, ae9cc033); PNG dock icons (b41602b0) |
| 163-179 | `PluginShelfButton(this, descriptor, owner, handle, _resolve, icons.PluginImage, svgIcon, icons.WindowImage)`, `DockSlot` size | SVG/PNG dock icons (ae9cc033, b41602b0, 7bd7d389) |
| 203-213 | `handedOver`, `_order.Add`, `finally svgIcon?.Release()` | SVG dock icons (ae9cc033, 7bd7d389) |
| 222, 225 | `_ownHandle` rename | dock redesign (ccaee1df) |
| 231-366 | dock tick/reflow, hover label, edge rail snapping | dock redesign (ccaee1df), themed shelf (ac3d8cc9), hover label (5e6a64a9), edge rail (1aa154bc), snap (742edd56) |
| 373-470 | reflow/layout of slots, rail geometry | dock redesign (ccaee1df), edge rail (1aa154bc), snap (742edd56) |
| 481-534 | rail inset, dock hit testing, persisted state | rail inset (444f4bf3), dock redesign (ccaee1df), edge rail (1aa154bc) |
| 550 (base: 307 removed), 567-568 | `_order.Remove`, `ReleaseSvgIcon` | dock redesign (ccaee1df), SVG icons (7bd7d389) |
| 595-640 (base: 352-381, 387-412, 414-437 removed) | retained-state save/restore and dock mode | dock redesign (ccaee1df), edge rail (1aa154bc) |
| 653, 658, 663 | `_ownHandle`, `ReleaseSvgIcon`, `_order.Clear()` in Dispose | dock redesign (ccaee1df), SVG icons (7bd7d389) |
| 671-760 | `DockGrip`, `ShelfToggleButton` changes, `PluginShelfButton` rewrite (SVG/PNG icons, open dot) | dock redesign (ccaee1df), edge rail (1aa154bc), hover label (5e6a64a9), gear (807739ee), SVG (7bd7d389), PNG (b41602b0), rail inset (444f4bf3) |
| 765-866 | shelf button draw: icon sources, SVG hold release, themed slot | dock redesign (ccaee1df), PNG (b41602b0), SVG (7bd7d389), plugin themes context (634911fa) |
| 883-913 | `PluginMinimizeButton`: themed ghost face, `themes` ctor argument, tooltip "Minimize to the dock" | themed shelf + ghost minimize (ac3d8cc9); dock mode wording (c8a22759) |

---

## 2. src/AcDream.App/UI/RetailUiRuntime.cs (243+/69-)

Only the plugin-window mount (main 4212-4261) has flex code. All of it is 2-titlebar (814f2c61, with `fallbackTitle` from e4c69c2a, which the merge 54d19bb7 re-placed).

| main lines | summary | series / feature | notes |
|---|---|---|---|
| 47-48 | `RetailUiAssets.ModernFont` becomes `Lazy<UiDatFont>?`; adds `ModernTitleFont` | FORK-ONLY: Retina sharp font twin / SemiBold title (a4b32e81) | |
| 158-159 | `OptionsRuntimeBindings.ExtendedLevels` | FORK-ONLY: extended level curve (16776714) | |
| 346-348 | `WorldPosition`, `IsPortalViewVisible` bindings | FORK-ONLY: world markers (b5f878ee, bacb4762) | |
| 384 | `_canvasPositions` | FORK-ONLY: canvas RemembersPosition (175f2e83) | |
| 388 | blank line | FORK-ONLY: chat seam (cf814aec) | |
| 398 | `_worldIconOverlay` | FORK-ONLY: world markers (b5f878ee) | |
| 401-404 | markup canvas / canvas stack fields replace `_pluginCanvasLayer` | FORK-ONLY: markup canvas (e7152b87), canvas layers (2acfceb5) | |
| 427 | `_pluginAppearance` becomes `PluginAppearanceWindow?` | FORK-ONLY: dock gear toggle (807739ee) | |
| 429-431 | SVG / file-icon / dock-image caches | FORK-ONLY: SVG icons (7bd7d389), markup file icons (75edaf69), PNG dock icons (b41602b0) | |
| 460 | `BindFontServices` | FORK-ONLY: plugin fonts (866257df) | |
| 493 | `MountWorldIconOverlay()` | FORK-ONLY: world markers (b5f878ee) | |
| 559-566 | `PluginCanvasPositionPersistence` | FORK-ONLY: canvas RemembersPosition (175f2e83) | |
| 763 | `_worldIconOverlay?.Tick()` | FORK-ONLY: world markers | |
| 798 | `_canvasPositions?.Tick` | FORK-ONLY: canvas RemembersPosition | |
| 825-831 | `PublishScreenSize` | FORK-ONLY: canvas screen size (d7e18400) | |
| 833-837 | `Host.PixelScale` | FORK-ONLY: smooth rounded shapes / HiDPI (2e29c130) | |
| 1220-1222 | `_canvasPositions?.RestoreAll()` | FORK-ONLY: canvas RemembersPosition | |
| 1477-1502 | `MountWorldIconOverlay` | FORK-ONLY: world markers (b5f878ee, bacb4762) | |
| 2853 | `ExtendedLevels =` | FORK-ONLY: extended level curve | |
| 3219, 3226 | "Plugin shelf hidden" becomes "Plugin dock hidden" | FORK-ONLY: dock wording (444f4bf3) | |
| 3309-3314, 3396-3401 | `ExperienceToRaiseLevel` on the extended curve | FORK-ONLY: extended level curve (16776714) | |
| 4159-4160 | `PluginUiThemeSettings(store, ModernFont, ModernTitleFont)` | FORK-ONLY: canvases read theme / SemiBold (6bca5af2) | |
| 4179-4189 | file-icon caches, `PluginDockIconSource dockIcons` | FORK-ONLY: markup file icons (75edaf69, 17b796ce), PNG dock icons (b41602b0) | |
| **4214** | `MarkupWindow window = MarkupDocument.BuildWindow(` replaces `UiNineSlicePanel element = MarkupDocument.Build(` | **2-titlebar** (814f2c61) | |
| 4220-4221 | `new PluginMarkupIconResolver(iconResolver, _pluginFileIcons, ...)` and `_pluginThemes,` split onto its own line | FORK-ONLY: markup file icons (75edaf69) | On BASE keep `iconResolver, _pluginThemes,`. |
| **4222** | `fallbackTitle: panel.Descriptor.Title,` | **2-titlebar** (e4c69c2a/814f2c61; placed by merge 54d19bb7) | Put it after `_pluginThemes,` and before BASE's `controlState:` argument. |
| **4227** | `UiNineSlicePanel element = window.Frame;` | **2-titlebar** (814f2c61) | |
| **4248** | `RetailWindowHandle handle = window.Register(Host.WindowManager, panel.WindowName, visibility);` replaces base: 4163-4170 (`ComputeAuthoredGeometryRevision(element.Width, ...)` + `Host.WindowManager.Register(...)`) | **2-titlebar** (814f2c61) | |
| 4249-4261 | `window.Canvases` → `MarkupCanvases().AddWindow(...)`; cleanup lambda that also calls `RemoveWindow(canvases)` | FORK-ONLY: markup canvas (e7152b87) | On BASE keep `() => Host.WindowManager.Unregister(panel.WindowName)`. |
| 4270 | `ShowPluginAppearance` becomes `TogglePluginAppearance` | FORK-ONLY: gear toggle (807739ee) | |
| 4282 | `dockIcons.Resolve(...)` replaces `ResolvePluginFileIcon(...)` | FORK-ONLY: PNG dock icons (b41602b0) | |
| 4297-4313 | `TogglePluginAppearance` / `PluginAppearanceWindow` with a Dock menu | FORK-ONLY: gear toggle (807739ee), dock mode | |
| 4317-4364, 4377-4403, 4417-4440, 4443 | canvas stacks, markup canvases, press reporting, `CreateCanvasElement`, `RequireCanvasServices` | FORK-ONLY: canvas layers (2acfceb5), screen size + outside press (d7e18400), markup canvas (e7152b87, 730f0406, 8966cf38), RemembersPosition (175f2e83), canvas focus (3e8b2dfa) | |
| 5349-5351, 5358-5364, 5381-5382, 5387-5388, 5394-5396 | dispose of canvas positions, icon caches, press hook, markup canvases, font services | FORK-ONLY: RemembersPosition, SVG/PNG/file icons, markup canvas, plugin fonts | |

**Mixed-in note:** at 4214-4261, MAIN mixes the 2-titlebar mount with the fork-only icon resolver and canvases. The BASE result is shown under Port instructions.

---

## 3. src/AcDream.App/UI/RetailWindowManager.cs (122+/2-)

| main lines | summary | series / feature | notes |
|---|---|---|---|
| 103-125 | `public static int ComputeAuthoredGeometryRevision(IReadOnlyList<string?> authoredInputs)`, an FNV-1a over the authored attribute strings | **2-titlebar** (1d18d421) | A new overload. BASE's numeric overload (main 88-101) stays and is still used for windows without a content area (main MarkupDocument.cs 212-214). Uses only BCL. |
| 225-273 | `EnforceMinimumSize(string name)` + `private static FitAxis(...)` | **3-markup** (3969d797, 7896260d) | Called by MarkupWindow.cs:58. Uses `frame.ResetAnchorCapture()`, `_root.NotifyWindowResized/NotifyWindowMoved` and `MinWidth/MinHeight`, which are all on BASE. No dependency. |
| 301-306 | `BringToFront(UiElement)` doc + banded body | FORK-ONLY: z-order bands (9113426b "ui: bands for the root's z-order instead of overflow at int.MaxValue") | |
| 309-328 | `BringToFront(window, UiBand)` overload, `Raise` | FORK-ONLY: z-order bands (9113426b); canvas layer exemption (7f18a4f6) | Uses `UiBands`, which BASE lacks. No flex file in this group calls the banded overload. |
| 330-332, 334-336, 340-357 | `Raise` loop changes, `Compact` | FORK-ONLY: z-order bands (9113426b) | |

---

## 4. src/AcDream.App/UI/PluginTitleBar.cs (new on main, 167 lines)

| main lines | member | series | notes / BASE dependency |
|---|---|---|---|
| 1-12 | usings, doc, `internal sealed class PluginTitleBar : UiElement` | 2-titlebar (814f2c61/e4c69c2a/46ec8946) | |
| 13-16 | `ClassicTextLeft=8`, `ThemedTextLeft=12`, `ThemedCloseTop=3`, `TextGap=4` | 2-titlebar | `ThemedTextLeft`/`ThemedCloseTop` are tuned to the fork's modern 24-point header. On BASE they still work. `ThemedTextLeft` could become 8 to match BASE's `title` label at Left=8. |
| 18-22 | cache fields | 2-titlebar | |
| 24-36 | ctor: `ClickThrough`, `Height = PluginWindowChrome.TitleBarHeight`, anchors, `PluginCloseButton` at `Top = Border`, `Close.Click → CloseRequested` | 2-titlebar | All on BASE (`ClickThrough`, `Anchors`, `AnchorEdges`). |
| 37 | `SetFrameWidth(frameWidth);` call in ctor | 3-markup | |
| 40-50 | `internal void SetFrameWidth(float)` (bar spans the frame; close button at `frameWidth - TitleBarHeight`) | 3-markup | Needed by content-sized (flex) windows. |
| 52-60 | `Close`, `CloseRequested`, `Title`, `DatFont`, `TextColor`, `Outline` | 2-titlebar | |
| 62-74 | `ThemePalette` property; setter sets `Close.ThemePalette = value`, moves `Close.Top`, calls `Close.CaptureCurrentAnchorBaseline()` | 2-titlebar (46ec8946) | **DEPENDENCY:** `Close.ThemePalette` is the fork-only `UiSimpleButton.ThemePalette` (6b071591 "themed buttons, tabs and switches"). BASE's `UiSimpleButton` has no ThemePalette. Fix: declare `internal PluginUiPalette? ThemePalette { get; set; }` on `PluginCloseButton` itself (see Port instructions). `CaptureCurrentAnchorBaseline` is on BASE (UiElement.cs:619). |
| 76-95 | `DisplayedTitle(measure)` (cached ellipsis), `TextOrigin(lineHeight)` | 2-titlebar (46ec8946) | Uses `PluginWindowChrome.Ellipsize`. No dependency. |
| 97-113 | `OnDraw`: draws the title text only, with DatFont, outline and ellipsis | 2-titlebar | No PluginUiStyle here. The themed header band behind it is drawn by the fork-only panel draw (`PluginUiStyle.Header` in main PluginUiThemeSettings.cs:173), not by the bar. On BASE a themed bar is the flat `p.Background` with the title text, because BASE's `UiPluginMarkupPanel.OnDraw` fills `p.Background` and outlines with `p.Border`. |
| 116-141 | `PluginCloseButton : UiSimpleButton`: ctor sets `AcceptsFocus`, `TabStop`, `FocusOnMouseClick=false`, `Outline=false`, transparent colours, right/top anchors; `Tooltip` (default `CloseTooltip`) + `GetTooltipText` | 2-titlebar | All on BASE. |
| 143-149 | `OnDraw` themed branch: `if (ThemePalette is { } palette) { PluginUiStyle.CloseButton(ctx, palette, Width, Height, ThemeState, KeyboardFocused); return; }` | 2-titlebar (826cadd5 added `PluginUiStyle.CloseButton`) | **PluginUiStyle use #1 (the only one in this file).** **DEPENDENCY:** `PluginUiStyle.CloseButton` calls `GhostButton`→`ctx.FillRoundedRect`, `ctx.DrawSmoothLine` and `FocusRing`, all fork-only (2e29c130 smooth shapes, 85414088 style). `ThemeState` (via fork-only `UiPointerState`, 85414088) and `KeyboardFocused` (6b071591) are fork-only `UiSimpleButton` members. BASE replacement is under Port instructions. |
| 150-166 | Classic branch: retail close sprite 0x06004D0C, pressed sprite 0x06004D0D when `ThemeState == Pressed`, white hover overlay when `Hovered`, gold focus outline when `KeyboardFocused` | 2-titlebar | **DEPENDENCY:** `ThemeState` and `KeyboardFocused` again (fork-only). See Port instructions. |

---

## 5. src/AcDream.App/UI/PluginWindowChrome.cs (new on main, 97 lines)

| main lines | member | series | notes / BASE dependency |
|---|---|---|---|
| 1-14 | usings, doc, `internal static class PluginWindowChrome` | 2-titlebar (1d18d421) | |
| 15-16 | `Border = RetailChromeSprites.Border` | 2-titlebar | `RetailChromeSprites.Border` (int 5) is on BASE. |
| 18-19 | `TitleBarHeight = PluginUiStyle.HeaderHeight` | 2-titlebar | **PluginUiStyle use #2 (the only one in this file).** **DEPENDENCY:** `PluginUiStyle.HeaderHeight` = `PluginThemeMetrics.HeaderHeight` = 24f. Both are fork-only (PluginThemeMetrics sits in fork-only Plugin.Abstractions/PluginTheme.cs:61). On BASE use `internal const float TitleBarHeight = 24f;` with a comment. Classic and themed windows share one inset, so the value must stay 24. |
| 21-25 | `CloseButtonSize = TitleBarHeight - Border` (19), `HorizontalInsets`, `VerticalInsets` | 2-titlebar | |
| 27-31 | `TopInset(bool titleBar)`, `VerticalInsetsFor(bool titleBar)` | 3-markup | Bar-less layout windows. |
| 33-37 | `Version = 1` | 2-titlebar | |
| 39-43 | `ClassicCloseSprite`, `ClassicClosePressedSprite` | 2-titlebar | |
| 45-54 | `MinimumBarFrameWidth = 96`, `FrameMinimumWidth(contentMin, titleBar)` | 4-scroll (681189b2) | |
| 56-57 | `CloseTooltip`, `DockedCloseTooltip` | 2-titlebar | |
| 59-60 | `GeometryAttributes = [w,h,minw,minh,resizable,resize,layout,titlebar]` | 2-titlebar | |
| 62-79 | `AuthoredInputs(XElement root)` | 2-titlebar; lines 70-73 and 76-77 (optional `scroll=` input) are 4-scroll | |
| 81-96 | `Ellipsize` | 2-titlebar | |

No other fork-only members.

---

## 6. src/AcDream.App/UI/UiPluginContentHost.cs (new on main, 82 lines)

| main lines | member | series | notes / dependency |
|---|---|---|---|
| 1 | `using AcDream.App.UI.Layout.Flex;` | 3-markup | Needs the flex engine (1-engine), which another group covers. |
| 5-11 | class doc | 2-titlebar; "When the root panel has `layout`..." lines 9-10 are 3-markup | |
| 12 | `internal sealed class UiPluginContentHost : UiElement, IUiScrollHost` | 2-titlebar; `IUiScrollHost` is 4-scroll | In the 2-titlebar step the class is plain `: UiElement`. Add `IUiScrollHost` with 4-scroll. |
| 14 | `_pendingMinimum` | 3-markup | |
| 16-20 | ctor: `ClickThrough = true`, all four anchors | 2-titlebar | |
| 22-23 | `Flex` property (`UiFlexBox?`) | 3-markup | |
| 25-26 | `ScrollArea` property (`UiScrollArea?`) | 4-scroll | |
| 28-33 | `ContentMinimumChanged` event | 3-markup | |
| 35 | `ClipsChildrenForTest => ClipsChildren` | 2-titlebar | `ClipsChildren` is on BASE. |
| 37-43 | `UseFlex()` (`TracksMinimum`, `MinimumChanged`) | 3-markup | |
| 45-61 | `UseScroll`, `Settle`, `OnEvent` (scroll first) | 4-scroll | `UiScrollArea` comes from 4-scroll. |
| 63-68 | `OnTick` raises `ContentMinimumChanged` | 3-markup | |
| 70-81 | `private protected override void LayoutChildren()`: flex `EnsureLayout`, then scroll | 3-markup; lines 72-80 (ScrollArea branches) are 4-scroll | **Cross-file prerequisite:** `UiElement.LayoutChildren()` virtual (main UiElement.cs:621, from 3-markup c15efdec) is not on BASE. It belongs to the same series (other group), so it is not fork-only. For the 3-markup step the body is `if (Flex is { } flex) flex.EnsureLayout(Width, Height); base.LayoutChildren();`. |

No fork-only bits.

---

## 7. src/AcDream.App/UI/UiDrawnPluginDialog.cs (41+/8-)

**All hunks are FORK-ONLY. None are flex.** Bring nothing.

| main lines | summary | feature |
|---|---|---|
| 34, 41, 46, 49, 60 | `_titleFont` (SemiBold title) and `measureTitle` | FORK-ONLY: modern theme, SemiBold title (45a83200 "modern theme shapes for list header, tree, expander, split, popups, dialog") |
| 80 | `CloseButton = new CloseX` | FORK-ONLY: themed Confirm closes with ghost X (48565ca8) |
| 112 | `button.ThemePalette = p;` | FORK-ONLY: needs the fork-only `UiSimpleButton.ThemePalette` (45a83200) |
| 119-134 | themed dialog: `PluginUiStyle.WindowShadow/Surface/Mix`, `FillVerticalGradient` header | FORK-ONLY: modern theme (45a83200) |
| 143-160 | Classic-only outline; `CloseX` class calling `PluginUiStyle.CloseButton` | FORK-ONLY: 45a83200, 48565ca8 |

(The commit map puts 45a83200/48565ca8 under FORK-ONLY-modern-theme-or-contract.) BASE's version of this file already shows how BASE themes a close X; this is the model for `PluginCloseButton` below.

---

## 8. AcDream.slnx (4+)

| main line | project | series / feature |
|---|---|---|
| 19 | samples/AcDream.Plugins.CanvasDemo | FORK-ONLY: canvas demo plugin (cc4b6cb2) |
| 20 | samples/AcDream.Plugins.WorldMarkersDemo | FORK-ONLY: world markers demo (5d7102fa) |
| **21** | **samples/AcDream.Plugins.FlexDemo** | **3-markup** (b8592630 "docs(ui): flex layout in plugin markup; FlexDemo sample") |
| 22 | samples/AcDream.Plugins.ThemeGallery | FORK-ONLY: theme gallery (48a5c7d2) |

---

## Dependencies

Flex code in these files that needs something BASE lacks:

| # | flex code (main) | needs (fork-only) | size | recommendation |
|---|---|---|---|---|
| D1 | PluginWindowChrome.cs:19 `TitleBarHeight = PluginUiStyle.HeaderHeight` | `PluginUiStyle` (fork-only modern theme) → `PluginThemeMetrics.HeaderHeight` (fork contract, Plugin.Abstractions) | one constant | **Replace** with `internal const float TitleBarHeight = 24f;` and a comment ("the plugin title bar's height; Classic and themes share it"). Do not bring PluginUiStyle or PluginThemeMetrics, because the second is a public contract change. |
| D2 | PluginTitleBar.cs:147 `PluginUiStyle.CloseButton(...)` (added to PluginUiStyle.cs by 2-titlebar 826cadd5) | `PluginUiStyle.CloseButton` / `GhostButton` / `FocusRing`, plus `UiRenderContext.FillRoundedRect` and `DrawSmoothLine` (2e29c130 smooth shapes, HiDPI) | large (render primitives) | **Replace** with a BASE-style themed X (see Port step 2-titlebar). |
| D3 | PluginTitleBar.cs:69, 145 `Close.ThemePalette` / `ThemePalette` inherited from `UiSimpleButton` | `UiSimpleButton.ThemePalette` (6b071591) | 1 property | **Replace** by declaring `internal PluginUiPalette? ThemePalette { get; set; }` on `PluginCloseButton`. Do not change BASE's `UiSimpleButton`. |
| D4 | PluginTitleBar.cs:147, 151, 161 `ThemeState` | `UiSimpleButton.ThemeState` + `UiPointerState` struct (85414088, 32 lines) | small | **Replace** with a private hover/pressed tracker inside `PluginCloseButton` (override `OnEvent`, watch `HoverEnter`/`HoverLeave`/`MouseDown`/`MouseUp`/`CaptureChanged`, then call `base.OnEvent`). All these event types exist on BASE (UiEvent.cs:18,19,34). Copying `UiPointerState.cs` as-is would also work, but it would collide with the fork's file on the next fork sync. Inlining is cleaner. |
| D5 | PluginTitleBar.cs:147, 164 `KeyboardFocused` | `UiSimpleButton.KeyboardFocused` (6b071591) | 1 line | Either add `private protected bool KeyboardFocused => _keyboardActivation.Focused;` to BASE's `UiSimpleButton` (smallest; same shape as the fork, so it merges cleanly), or track focus in `PluginCloseButton` from `UiEventType.FocusGained/FocusLost` if BASE has them. **Recommend the one-line addition.** |
| D6 | PluginSidePanel.cs:193 `new PluginMinimizeButton(handle, _font, _themes)` | `_themes` ctor argument (ac3d8cc9 ghost minimize) | trivial | **Strip:** use BASE's `new PluginMinimizeButton(handle, _font)`. |
| D7 | UiPluginContentHost.cs:70 `override LayoutChildren()` | `UiElement.LayoutChildren()` virtual | n/a | Not fork-only: it is 3-markup (c15efdec) in UiElement.cs, another group's file. Port it in the same step. |
| D8 | RetailUiRuntime.cs:4214/4248 `MarkupDocument.BuildWindow`, `MarkupWindow.Register`, `window.Frame` | `MarkupWindow` + `BuildWindow` (2-titlebar, other group) | n/a | Same series. Note that main's `MarkupWindow.Canvases` / `CollectCanvases` is fork-only markup canvas, so the BASE `MarkupWindow` should leave it out, and RetailUiRuntime must not reference it. |

Not a dependency, but worth knowing: on MAIN the title bar's font comes from `PluginMarkupTheme.cs:37` (`panel.ModernTitleFont ?? panel.ModernFont`). `ModernTitleFont` (SemiBold) is fork-only, so on BASE use `panel.ModernFont ?? classicFont`. That file belongs to another group.

---

## Port instructions

### 2-titlebar

1. **New file PluginWindowChrome.cs:** bring main lines 1-26, 33-44, 56-69, 74-75 and 78-97 (constants, `Version`, close sprites, tooltips, `GeometryAttributes`, `AuthoredInputs` without the `scroll` lines, `Ellipsize`). Leave out `TopInset`/`VerticalInsetsFor` (27-32, 3-markup) and `MinimumBarFrameWidth`/`FrameMinimumWidth` (45-55, 4-scroll).
   - **Replace** line 19 with `internal const float TitleBarHeight = 24f;` (D1).
   - The 2-titlebar version of `AuthoredInputs` is a fixed array: the `GeometryAttributes` values, then `Version`. The "scroll only when present" code is 4-scroll. Check the original 1d18d421/e4c69c2a text with `git show 1d18d421:src/AcDream.App/UI/PluginWindowChrome.cs`.
2. **New file PluginTitleBar.cs:** bring everything except the 3-markup `SetFrameWidth` (37-50). At 2-titlebar the ctor set `Width = frameWidth; Close.Left = frameWidth - TitleBarHeight;` inline; get the original text with `git show 46ec8946:src/AcDream.App/UI/PluginTitleBar.cs`. Then rewrite `PluginCloseButton` for BASE:
   - Add `internal PluginUiPalette? ThemePalette { get; set; }` to `PluginCloseButton` (D3). `PluginTitleBar.ThemePalette`'s setter keeps `Close.ThemePalette = value;`.
   - Add a private pointer tracker (D4): fields `bool _hovered, _pressed`. Override `OnEvent(in UiEvent e)`: update the fields for HoverEnter/HoverLeave/MouseDown (pressed + hovered)/MouseUp/CaptureChanged, then `return base.OnEvent(e);`. Add a `State` helper: `!Enabled ? Disabled : _pressed && _hovered ? Pressed : _hovered ? Hovered : Normal`, using `UiControlState`, which is on BASE.
   - Add `private protected bool KeyboardFocused => _keyboardActivation.Focused;` to BASE's `UiSimpleButton` (UiPanel.cs) (D5).
   - **Themed draw (replaces `PluginUiStyle.CloseButton`)**, in BASE's flat theme language. BASE themes buttons as a flat face with a 1-point edge in palette colours (PluginMarkupTheme.cs:48-56: `BackgroundColor = p.Field`, `BorderColor = p.Border`, `TextColor = p.Text`, `Outline = false`), and BASE's own dialog close button (`UiDrawnPluginDialog.Style`) is a `UiSimpleButton` with `Text = "x"` drawn the same way. So for `ThemePalette is { } p`:
     ```csharp
     // A ghost X: no face at rest, the theme's field colour under the pointer.
     UiControlState state = State;
     if (state == UiControlState.Hovered) ctx.DrawFill(0f, 0f, Width, Height, p.Field);
     else if (state == UiControlState.Pressed) ctx.DrawFill(0f, 0f, Width, Height, p.Border);
     Vector4 color = state is UiControlState.Hovered or UiControlState.Pressed ? p.Text : p.Muted;
     // "x" centred, in the button's DatFont (set it from the title bar's font) or ctx.DefaultFont, Outline = false
     if (KeyboardFocused) ctx.DrawRectOutline(1f, 1f, Width - 2f, Height - 2f, p.Accent, 1f);
     ```
     The simplest alternative that matches BASE exactly: set `Text = "x"` with `BackgroundColor`/`BorderColor`/`TextColor` from the palette, and call `base.OnDraw(ctx)`. BASE's `UiSimpleButton.OnDraw` already draws the face, edge, gold focus outline and centred caption. Either way, use only `DrawFill`, `DrawRectOutline` and `DrawStringDat`/`DrawString`. No rounded rects and no smooth lines.
   - The Classic branch stays as on main, with `ThemeState` replaced by the new `State`.
   - `ThemedCloseTop = 3f` puts the 19-point button in the 24-point bar. Keep it. `ThemedTextLeft = 12f` can stay; 8 would line up with BASE's old title label.
3. **New file UiPluginContentHost.cs (2-titlebar shape):** `internal sealed class UiPluginContentHost : UiElement` with the ctor (16-20) and `ClipsChildrenForTest` (35) only. Check the original with `git show 814f2c61:src/AcDream.App/UI/UiPluginContentHost.cs` or the commit that created it (`git log --diff-filter=A main -- src/AcDream.App/UI/UiPluginContentHost.cs`).
4. **RetailWindowManager.cs:** add main 103-125 (`ComputeAuthoredGeometryRevision(IReadOnlyList<string?>)`) after BASE's numeric overload (BASE line ~101). Keep the numeric overload.
5. **RetailUiRuntime.cs plugin mount (BASE ~4130-4174):** the result on BASE should read:
   ```csharp
   MarkupWindow window = MarkupDocument.BuildWindow(
       xml,
       panel.Binding,
       _bindings.Assets.ResolveSprite,
       _bindings.Assets.Controls,
       _bindings.Assets.DefaultFont,
       iconResolver, _pluginThemes,
       fallbackTitle: panel.Descriptor.Title,
       controlState: _controlState.ForWindow(panel.WindowName),
       describeKey: describeKey,
       textures: markupTextures,
       sharedControlState: _controlState.Shared);
   UiNineSlicePanel element = window.Frame;
   ...
   _bindings.Plugins.CompleteMount(panel, Host.Root, element);
   RetailWindowHandle handle = window.Register(Host.WindowManager, panel.WindowName, visibility);
   _bindings.Plugins.CompleteWindowMount(
       panel,
       () => Host.WindowManager.Unregister(panel.WindowName));
   ```
   **Strip:** `PluginMarkupIconResolver`/`_pluginFileIcons` (4220), `window.Canvases`/`MarkupCanvases()`/`RemoveWindow` (4249-4261), `dockIcons.Resolve` (4282; keep BASE's `ResolvePluginFileIcon(...)`), and `TogglePluginAppearance` (4270; keep BASE's `ShowPluginAppearance`). Bring nothing else from this file.
6. **PluginSidePanel.cs:**
   - Line 1: add `using System.Linq;`.
   - Base 114-117 doc: "Adds one manifest-scoped plugin window, its shelf button and, unless the window has a title bar (whose X replaces it), its minimize button."
   - Replace base 154-162 with:
     ```csharp
     // A window with a title bar already has a close button, which does
     // what "-" would; the bar's X takes its place and says where the
     // window went.
     PluginMinimizeButton? minimize = null;
     if (handle.OuterFrame.Children.OfType<PluginTitleBar>().FirstOrDefault() is { } titleBar)
     {
         titleBar.Close.Tooltip = PluginWindowChrome.DockedCloseTooltip;
     }
     else
     {
         minimize = new PluginMinimizeButton(handle, _font)   // BASE ctor: no _themes
         { Left = MathF.Max(8f, handle.OuterFrame.Width - 23f), Top = 3f, Width = 18f, Height = 17f,
           Anchors = AnchorEdges.Top | AnchorEdges.Right };
         handle.OuterFrame.AddChild(minimize);
     }
     ```
   - Base 325-326 and base 457-458 become `if (entry.Minimize is { } minimize && ReferenceEquals(minimize.Parent, handle.OuterFrame)) handle.OuterFrame.RemoveChild(minimize);`. Keep BASE's `entry.Button.DisposeSubscriptions()` calls.
   - Base 466: `PluginMinimizeButton? Minimize`.
   - `PluginWindowChrome.DockedCloseTooltip` reads "Close (reopen from the dock)". BASE calls it the "plugin sidepanel"/shelf; its minimize tooltip is "Minimize to plugin sidepanel". Consider "Close (reopen from the plugin sidepanel)" on BASE.
   - Bring no other PluginSidePanel hunk: everything else is the fork dock redesign, edge rail, SVG/PNG icons, hover label, gear toggle and ghost minimize.

### 3-markup

1. **PluginWindowChrome.cs:** add `TopInset`/`VerticalInsetsFor` (main 27-31).
2. **PluginTitleBar.cs:** add `SetFrameWidth` (main 40-50) and call it from the ctor (main 37).
3. **UiPluginContentHost.cs:** add `using AcDream.App.UI.Layout.Flex;`, the doc lines 9-10, `_pendingMinimum`, `Flex`, `ContentMinimumChanged`, `UseFlex`, `OnTick`, and the flex-only `LayoutChildren` override. This needs the `UiElement.LayoutChildren` virtual from c15efdec (other group).
4. **RetailWindowManager.cs:** add `EnforceMinimumSize` + `FitAxis` (main 225-273) after BASE's `ResizeTo` (BASE ~line 201). Its doc cref targets `UiElement.MinWidth`/`MinHeight` and `ResizeTo`, which all exist on BASE.
5. **AcDream.slnx:** add `<Project Path="samples/AcDream.Plugins.FlexDemo/AcDream.Plugins.FlexDemo.csproj" />` in `/samples/` (main line 21) only. Leave out CanvasDemo, WorldMarkersDemo and ThemeGallery. The FlexDemo sample itself must not use fork-only markup such as `<canvas>`, `icon file=` or `theme` features that BASE lacks; check it in the sample's group.

### 4-scroll (+ followups)

1. **PluginWindowChrome.cs:** add `MinimumBarFrameWidth` and `FrameMinimumWidth` (main 45-54), and the optional `scroll` input in `AuthoredInputs` (main 70-73, 76-77).
2. **UiPluginContentHost.cs:** add `IUiScrollHost` to the class line, `ScrollArea`, `UseScroll`, `Settle`, `OnEvent`, and the ScrollArea branches of `LayoutChildren` (main 72-80).
3. **PluginSidePanel.cs:** turn the `else` into `else if (!handle.OuterFrame.Children.OfType<UiPluginContentHost>().Any())`, with the 3-line comment from main 188-190 above it, so bar-less layout windows get no "-".

### 5-controls-in-flex / 6-bound-visible

Nothing in these files. The `fallbackTitle` placement in RetailUiRuntime (main 4222) that blame credits to merge 54d19bb7 is the same 2-titlebar line moved in among the controls' new arguments. Placing it as in 2-titlebar step 5 covers it.

### Strip list (never port from these files)

- UiDrawnPluginDialog.cs: the whole diff (SemiBold title font, PluginUiStyle themed surface, CloseX ghost X).
- RetailWindowManager.cs: the z-order bands (main 301-357).
- RetailUiRuntime.cs: every hunk except 4214, 4222, 4227 and 4248.
- PluginSidePanel.cs: every hunk except 1, 121, 180-202 (minus `_themes`), 570-571, 659-660 and 669.
- AcDream.slnx: lines 19, 20 and 22.
