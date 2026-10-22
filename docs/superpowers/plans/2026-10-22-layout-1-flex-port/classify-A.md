# Group A: hunk classification (BASE = feat/plugin-controls-upgrade, main = fork main)

Method: `git diff feat/plugin-controls-upgrade main -- <file>` plus `git blame -l main` on every added line, mapped through
commit-series.txt. The annotated diffs are in the scratchpad: annA-doc.txt (MarkupDocument), annA-new.txt (MarkupWindow,
MarkupFlexAttributes, MarkupContentSize), annA-flexdemo.txt. Line numbers below are **main** line numbers.

Fork-only commits found in these files (none of them in the map):
- 78d44223 (2026-10-02) "ui: themed plugin windows get a shadow, header band and rounded edge" (modern theme: HasTitle, RegisterTitle)
- a55d8425 (2026-10-02) "fix: themed popup hover colour, list thumb state, docs and doc comments" (modern theme)
- a4b32e81 "ui: bake the sharp font twin at the display's scale, and only when drawn" (HasModernFont / lazy ModernFont)
- e23742a2 "ui: SemiBold and sharp high-density text for the bundled font" (ModernTitleFont)
- 6b071591 / 0339c150 / 864292d9 "ui: themed buttons, tabs..." / "themed fields and menus" / "themed lists, logs, sliders" (ThemePalette on button/menu/meter/scrollbar)
- 75edaf69 (2026-10-08) "feat(ui): plugin markup can draw PNG and JPEG icons from its folder" (markup file icons)
- 2135b164 "Merge branch 'feat/tab-icons-upstream' into feat/tab-icons" (tab icons, file kind)
- 19006486 (2026-10-14) "feat(ui): <canvas id> markup element, laid out like any leaf" (markup canvas)
- 48a5c7d2 "samples: a theme gallery plugin", 72d8ba64 "test(ui): tab icons load f...", 8cbe723d "feat(markup): a list column of buttons" (ThemeGallery sample)
- 9144c4d5 "build: build again under the October ImageSharp advisories" (only a trailing `}` in a lock file: blame artifact)
- FORK-ONLY-modern-theme-or-contract series (45a83200 / e239a759 / a87457da: modern shapes, SemiBold list header)

Blame artifacts (lines blamed on BASE/other commits that are just brace realignment, part of the surrounding flex hunk):
MarkupDocument 935 (`}` -> e6a7ec24, a BASE control commit), 996-997 (`}`/blank -> 3b6bb588 Initial), 1014 (unchanged `}`), 869 (`{` -> Initial, inside the file-icon hunk).

---

## 1. src/AcDream.App/UI/MarkupDocument.cs (+518/-58)

| main lines | summary | series | notes |
|---|---|---|---|
| 6 | `using AcDream.App.UI.Layout.Flex;` | 3-markup | |
| 15-16 | RuntimeTooltipRootElementId / LayoutDid `private` -> `internal` | 2-titlebar | used by title bar close tooltip |
| 31-32 | `Build(...)` now `=> BuildWindow(..., fallbackTitle: null, controlState, describeKey, textures, sharedControlState).Frame` | 5 (merge-resolution) over 2-titlebar | BASE `Build` keeps its signature (controlState/describeKey/textures/sharedControlState are BASE params); only the body changes |
| 33-46 | new `internal static MarkupWindow BuildWindow(...)` doc + signature | 2-titlebar (33-42), 5 merge-resolution (43-46: the BASE control params appended after `fallbackTitle`) | the BASE method body becomes BuildWindow's body |
| 60-81 | hasLayout / hasTitleBar / hasContentArea; chromeW/H; RejectContainer; authoredW/H/MinW/MinH via MarkupFlexAttributes.RootSize; contentW/H | 3-markup (60-65, 71-81); 4-scroll (66-70: root `scroll` read + error when no content area) | |
| 83 | `panel.Width = contentW + chromeW; panel.Height = contentH + chromeH;` | 2-titlebar | replaces BASE `panel.Width = F(root,"w")...` |
| 87-90 | MinWidth via `PluginWindowChrome.FrameMinimumWidth(...)` when content area; MinHeight `+ chromeH` | 4-scroll (87-89), 2-titlebar (90) | series-2/3 form was `panel.MinWidth = FOr(root, "minw", contentW) + chromeW;` (681189b2 in 4-scroll changed it) |
| 115-136 | titleColor; contentParent; `content` host var; PluginTitleBar construction, close tooltip, `panel.AddChild(titleBar)`; themed: `barPanel.HasTitle = true; PluginMarkupTheme.RegisterTitleBar(barPanel, titleBar)` | 2-titlebar (117 is 3-markup) | **MIXED: line 133 `barPanel.HasTitle = true;` needs FORK-ONLY `UiPluginMarkupPanel.HasTitle` (78d44223, draws `PluginUiStyle.Header` band). Strip it.** RegisterTitleBar (134) is flex (see PluginMarkupTheme) |
| 137-142 | `else if (!hasLayout && title)` classic UiLabel title; `tc` renamed to `titleColor` | 3-markup (137), 2-titlebar (141) | BASE's label creation itself unchanged |
| 143-148 | themed label title: `titlePanel.HasTitle = true; Register(...label); RegisterTitle(...)` | FORK-ONLY modern theme (a55d8425) | Replaces BASE's two lines (BASE 131-132: `if (panel is UiPluginMarkupPanel titlePanel && panel.Children.Count > 0) PluginMarkupTheme.Register(titlePanel, panel.Children[0], new XElement("label"));`). **Do not port; restore BASE's Register call, but move it inside this else-branch** (see Port instructions) |
| 149 | `}` | blame -> 78d44223 | brace artifact |
| 150-160 | `if (hasContentArea)` create `UiPluginContentHost` at Border/TopInset, size contentW/H, contentParent = content | 3-markup | |
| (BASE 131-132 removed) | BASE title Register | removed by FORK theme | see 143-148 |
| 163-168 | `rootFlex = content!.UseFlex(); ReadContainer(root, rootFlex.Node)` | 3-markup | |
| 169-173 | root scroll: `content!.UseScroll(...)`; `content.ScrollArea!.RegisterTheme(scrollPanel)` | 4-scroll | RegisterTheme is 4-scroll (73fbd4f1) in UiScrollArea; check that file for PluginUiStyle use (other group) |
| 177-178 | AddElement into `contentParent`, passes `rootFlex` as last arg | 5 merge-resolution (series-3 form passed `rootFlex` too, before BASE params existed) | |
| 180-194 | first measure: `rootFlex.MeasureNow()`, min/preferred sizes, panel/content resize | 3-markup; 192 is 4-scroll (FrameMinimumWidth) | series-3 form of 192: `panel.MinWidth = minW + chromeW; panel.MinHeight = minH + chromeH;` |
| 195-201 | narrow title-bar window opens at bar minimum; `titleBar?.SetFrameWidth(panel.Width)` | 4-scroll | |
| 210-214 | geometry revision: `AuthoredInputs(root)` overload when content area, else BASE's numeric overload | 2-titlebar (211 is 3-markup `hasContentArea`) | |
| 215 | `var window = new MarkupWindow(panel, contentParent, titleBar, revision) { Canvases = CollectCanvases(panel) };` | blame FORK (19006486 markup canvas) over 2/3 | **MIXED: strip `{ Canvases = CollectCanvases(panel) }`**; flex form is `new MarkupWindow(panel, contentParent, titleBar, revision);` |
| 216-218 | `TrackContentMinimum(content!, chromeH, authoredMinW, authoredMinH)`; `return window;` | 3-markup / 4-scroll (217) | series-3 TrackContentMinimum had fewer params (4-scroll added `authoredMinW/H`) |
| 221-232 | `TitleBar(XElement root, bool hasLayout)` titlebar attribute parser | 2-titlebar (3-markup changed default to `hasLayout`) | |
| 243-244 | AddElement gains `UiFlexBox? flex = null` after BASE `recentColors` | 3-markup + 5 merge-resolution | |
| 247 | `if (flex is null) MarkupFlexAttributes.RejectItem(el);` | 3-markup | |
| 271-302 | `case "group"`: flex group (UiFlexGroup + ReadContainer) vs absolute (UiPanel / UiScrollPanel), scroll theme registration, shared property sets, `ClickThrough = !scrolls` | 3-markup; 4-scroll (273-274, 279, 286, 288-289, 301-302) | replaces BASE object-initialiser UiPanel |
| 314-315 | child AddElement passes `groupFlex` | 5 merge-resolution (flex arg) | |
| 426 | iconkind error text adds `/file` | FORK-ONLY tab icons / file icons (2135b164) | skip |
| 432-458, 465 | `<icon file=...>` source (fileAttr, BuildFileIconSource) | FORK-ONLY markup file icons (75edaf69) | skip |
| 868-888 | list `iconkind="file"` path (FileIconIds) | FORK-ONLY markup file icons (75edaf69) | skip |
| 900-918 | `case "canvas"` -> UiMarkupCanvas | FORK-ONLY markup canvas (19006486) | skip |
| 933-934 | tail of AddElement: `if (flex is not null) AddFlexItem(flex, built, el);` (after BASE's contextmenu attach at 931-932) | 5 merge-resolution (originally 3-markup) | must stay after the contextmenu attach |
| 936-951 | `AddFlexItem(UiFlexBox, UiElement, XElement)`: container vs leaf (+ `AbsoluteGroupSize` for a group, 950 is 4-scroll), ReadItem | 3-markup (950: 4-scroll) | |
| 952-981 | AddFlexItem: expander (FlexPlaced, ExpandedHeight, clears Node.Height, ExpandedMax/MinHeight, starts at ListWidth + LayoutParts) and split (default size, DefaultPosition, LayoutAtDefault) | 5-controls-in-flex | needs UiFlexItem.ExpandedMaxHeight/ExpandedMinHeight (UiFlexBox, other group); everything on the controls exists in BASE except the two new expander props (this group, below) |
| 982-995 | `Anchors = None`; `label.SizedByLayout = true`; menu popup width via BeforeOpen/ColumnWidth | 3-markup | UiLabel.SizedByLayout is 3-markup (c15efdec in UiPanel.cs, other group); UiMenu.BeforeOpen/ColumnWidth exist in BASE |
| 998-1023 | `CollectCanvases` | FORK-ONLY markup canvas (19006486) | skip |
| 1025-1036 | `AbsoluteGroupSize` | 4-scroll | needs UiScrollPanel, UiScrollArea.BarSize/ScrollsX/Y, FlexLayout.MinimumScrollViewport (all 4-scroll, other groups) |
| 1051-1057 | tab/button `iconkind="file"` | FORK-ONLY tab icons (2135b164) | skip |
| 1067, 1069 | ValidateIconKind accepts `file` | FORK-ONLY file icons (75edaf69) | skip |
| 1112-1187 | BuildFileIconSource / BindPathLiteralOrBinding / FileIconIds | FORK-ONLY file icons (75edaf69) | skip |
| 1443-1462 | list `<column type="icon" iconkind="file">` | FORK-ONLY file icons (75edaf69) | skip |
| 1563-1574 | BindBool: seed the bound value with `setLiteral(source())` before the first tick (TargetInvocationException guarded) | 6-bound-visible | self-contained; `setLiteral` is already BindBool's BASE parameter |

## 2. src/AcDream.App/UI/MarkupWindow.cs (new, 65 lines)

| main lines | member | series | notes |
|---|---|---|---|
| 1-11, 15-19 | class `MarkupWindow(frame, contentRoot, titleBar, authoredGeometryRevision)`, Frame/ContentRoot/TitleBar/AuthoredGeometryRevision | 2-titlebar | |
| 12-14 | `_manager`, `_name` fields | 3-markup | |
| 20-22 | `Canvases` init property | FORK-ONLY markup canvas (19006486) | strip |
| 23-36, 39-40 | `Register(manager, name, controller)`: `manager.Register(name, Frame, ContentRoot, controller, authoredGeometryRevision:)`, close button -> `manager.Close(name)` | 2-titlebar | needs RetailWindowManager.Register overload with contentRoot (2-titlebar, other group) |
| 37-38 | remember manager/name | 3-markup | |
| 41-64 | `TrackContentMinimum(content, chromeH, authoredMinW, authoredMinH)` on `ContentMinimumChanged`, `EnforceMinimumSize` | 3-markup; 4-scroll (50 signature, 54 FrameMinimumWidth) | series-3 version: no authoredMin params, `Frame.MinWidth = minimum.Width + chromeW`-style (check 3-markup commit cf9f9573 when replaying); needs RetailWindowManager.EnforceMinimumSize (3969d797, 3-markup, other group) |

## 3. src/AcDream.App/UI/MarkupFlexAttributes.cs (new, 176 lines)

| main lines | member | series | notes |
|---|---|---|---|
| 1-94 | usings, attribute name lists, IsContainer, RejectContainer, ReadContainer, ReadItem, RejectItem | 3-markup | pure; depends only on Layout.Flex types (1-engine) |
| 95-107 | `Scroll(XElement)` (`scroll="x|y|both"`) | 4-scroll | |
| 108-176 | RootSize, Align, Padding, NonNegative, Number, CheckLimits, Error, Format, Identity | 3-markup | |

No fork-only content.

## 4. src/AcDream.App/UI/MarkupContentSize.cs (new, 200 lines)

| main lines | member | series | notes |
|---|---|---|---|
| 1-35 | header, FallbackCharWidth/LineHeight, ControlHeight, ControlTextPadding, ButtonPaddingX, ToggleHeight, ClassicToggleCaptionX | 3-markup | |
| 36 | `ThemedToggleCaptionX = 1f + PluginUiStyle.SwitchWidth + PluginUiStyle.SwitchCaptionGap` | 3-markup, **DEPENDENCY on FORK-ONLY PluginUiStyle** | BASE's themed toggle draws `p.DrawCheck` and its caption at 17f in both themes (BASE UiMarkupToggle.cs:45,53). Replace: delete constant |
| 38-48 | CheckCaptionX, RadioOptionGap, ColorWidth, SeparatorSize | 5-controls-in-flex | |
| 50-71 | IconSize, Field/Slider/Meter/List sizes, TextWidth, LineHeight, ControlHeightFor | 3-markup | |
| 73-75 | ToggleHeightFor | 5-controls-in-flex | |
| 77-115 | Measure: label, button (uses BASE `UiSimpleButton.IconSource`), toggle, icon, field, menu (`ButtonDatFont`, BASE), scrollbar, meter, list/log | 3-markup (101: 5-controls swapped toggle height to ToggleHeightFor) | line 100 `captionX = toggle.ThemePalette is null ? ClassicToggleCaptionX : ThemedToggleCaptionX` -> **replace with `ClassicToggleCaptionX`** |
| 116-119 | `case UiMarkupCanvas: return Fixed(0,0)` | FORK-ONLY markup canvas (19006486) | strip |
| 120-142 | checkbox, radio, number, hotkey, color, separator, split, expander | 5-controls-in-flex | needs `UiMarkupExpanderHeader.CaptionX` (5-controls a14340b9, other group: UiMarkupExpanderHeader.cs) and `UiMarkupExpander.ExpandedHeight` (this group) |
| 143-146 | default null | 3-markup | |
| 148-159 | RadioSize | 5-controls-in-flex | BASE has Items/Segmented/Horizontal/DatFont |
| 161-187 | Text(), Font() | 3-markup; 167-169, 181-185 are 5-controls-in-flex | |
| 189-190 | Collapsed() | 5-controls-in-flex | |
| 192-193 | Themed() (`UiMarkupToggle { ThemePalette: not null }`) | 3-markup | after stripping the themed caption, nothing's size changes with theme beyond font; keeping Themed() is harmless (extra re-measure on theme switch in UiFlexBox.cs:190,220). Keep it to keep UiFlexBox unchanged |
| 195-199 | Fixed/Range | 3-markup | |

## 5. src/AcDream.App/UI/PluginMarkupTheme.cs (+54/-?)

| main lines | summary | series | notes |
|---|---|---|---|
| 6-23 | `RegisterTitle(panel, UiLabel)`: moves themed title into header band, `ModernTitleFont`, `PluginUiStyle.HeaderHeight` | FORK-ONLY modern theme (78d44223) | skip |
| 24-41 | `RegisterTitleBar(panel, PluginTitleBar)`: ThemePalette, font, TextColor, Outline | 2-titlebar | **MIXED/DEPENDENCY: line 37 uses `panel.ModernTitleFont` (FORK-ONLY SemiBold e23742a2). Replace with `bar.DatFont = p is null ? classicFont : panel.ModernFont ?? classicFont;`** (BASE `ModernFont` is a plain property; comment line 28-29 "read only once a theme is on" refers to the fork's lazy font, reword) |
| 45-52 | `HasModernFont` + lazy `panel.ModernFont ?? classic` | FORK-ONLY sharp font twin (a4b32e81) | skip; keep BASE `if (panel.ModernFont is { } modern)` |
| 91 | `button.ThemePalette = p;` | FORK-ONLY themed buttons (6b071591) | skip |
| 150, 156 | `menu.ThemePalette = p`; hover `PluginUiStyle.Hover(...)` | FORK-ONLY themed menus (0339c150, a55d8425) | skip |
| 168 | `list.HeaderFont = ... ModernTitleFont` | FORK-ONLY-modern-theme-or-contract (a87457da SemiBold header) | skip |
| 175-177, 183 | UiMeter / UiScrollbar ThemePalette | FORK-ONLY themed lists/sliders (864292d9) | skip |
| 196 | `field.ThemePalette = p;` | merge-resolution 54d19bb7 but **FORK-ONLY**: merge message says "ThemeField sets the fork's field ThemePalette"; BASE UiField has no ThemePalette | skip |

Only flex content: RegisterTitleBar (24-41).

## 6. src/AcDream.App/UI/UiMarkupExpander.cs (+15)

| main lines | summary | series | notes |
|---|---|---|---|
| 35-44 | `internal bool FlexPlaced`, `internal float ExpandedHeight` | 5-controls-in-flex | |
| 161-165 | in the layout-on-state-change path: `if (FlexPlaced) { Content.Visible = expanded; return; }` | 5-controls-in-flex | |

Clean, no fork bits.

## 7. src/AcDream.App/UI/UiMarkupNumber.cs (+20)

| main lines | summary | series | notes |
|---|---|---|---|
| 60-62 | inner Field gets `Anchors = AnchorEdges.None` (flex-sized number shows its value) | 5-controls-in-flex (22640176) | clean |
| 224-240 | themed ghost spin buttons (`PluginUiStyle.Pressed/Hover/SmallRadius/Chevron`, FillRoundedRect, DrawSmoothLine) | FORK-ONLY modern theme shapes (e239a759) | skip; BASE flat draw (`ThemePalette?.Field` / `?.Selected` rectangles at 241+) stays |

## 8. src/AcDream.App/UI/Markup/ (whole dir)

No differences between BASE and main (`git diff --stat` empty). Nothing to port.

## 9. samples/AcDream.Plugins.FlexDemo/ (all new)

| file | lines | series | notes |
|---|---|---|---|
| AcDream.Plugins.FlexDemo.csproj | 1-19, 22-23: 3-markup; 20 (gallery.xml): 4-scroll; 21 (controls.xml): 5-controls | | also needs the AcDream.slnx entry (main AcDream.slnx:21, not in this group) |
| plugin.json | all 3-markup | | |
| packages.neutral.lock.json | all 3-markup (line 10 `}` blamed on FORK 9144c4d5, artifact; BASE lock files also lack trailing newline) | | take as is |
| finder.xml | all 3-markup | | |
| settings.xml | 1 (`scroll="y"` on root): 4-scroll; 6 (list bound to ProfileIndex): 4-scroll-followups; rest 3-markup | | |
| gallery.xml | all 4-scroll | | |
| controls.xml | all 5-controls-in-flex | | uses checkbox/radio/number/separator/expander/split: all in BASE |
| FlexDemoPlugin.cs | 1-5, 18-23, 26-27, 29-43, 50-57, 74-75 (closing), 79-116, 119-133: 3-markup; 7-13, 24, 44-46, 58-59, 76-77: 4-scroll; 117-118 (ProfileIndex/PickProfile): 4-scroll-followups; 6, 14-17, 25, 28, 47-49, 60-73: 5-controls-in-flex | | comment line 13 "switch the shared appearance from the dock's gear" refers to the FORK dock redesign gear; BASE switches theme through the plugin side panel's appearance setting (PluginAppearanceBinding). Reword the comment for BASE. Uses only BASE API (PluginPanelDescriptor IconText/StartVisible, RegisterPanel, PluginDirectory) |

No fork-only code in the sample apart from that comment.

## 10. samples/AcDream.Plugins.ThemeGallery/gallery.xml (new vs BASE)

BASE has **no ThemeGallery sample at all** (`samples/` in BASE: ControlsDemo + 3 RenderPacks). The whole sample is FORK-ONLY ("samples: a theme gallery plugin", 48a5c7d2; tab icons 72d8ba64; list button column 8cbe723d). Lines 1, 4, 6-7, 9-10, 12-13, 15-16, 18, 24, 26, 28-29 are blamed on 2-titlebar 9e4d205e ("gallery adopts it": `titlebar="true"` and y offsets moved up into the content area), but they are edits to a fork-only file. **Do not port.** (If the port wants a titlebar demo, FlexDemo windows already get the title bar by default via `layout`.)

---

## Dependencies

Flex code in these files that needs something not in BASE:

| flex code (main) | needs | origin | small enough to bring? / replacement |
|---|---|---|---|
| MarkupDocument 133 `barPanel.HasTitle = true;` | `UiPluginMarkupPanel.HasTitle` (PluginUiThemeSettings.cs:166, draws `PluginUiStyle.Header` at :173) | FORK modern theme 78d44223 | **Replace: delete the line.** BASE flat theme has no header band; the PluginTitleBar draws its title text on the flat panel |
| PluginMarkupTheme 37 `panel.ModernTitleFont` | SemiBold title font | FORK e23742a2 / a4b32e81 | **Replace** with `panel.ModernFont ?? classicFont` |
| MarkupContentSize 36/100 `PluginUiStyle.SwitchWidth/SwitchCaptionGap` (ThemedToggleCaptionX) | PluginUiStyle (fork modern theme) and the fork's switch-style toggle | FORK modern theme | **Replace**: captionX = `ClassicToggleCaptionX` (17f) always, matching BASE UiMarkupToggle |
| MarkupDocument 215 `{ Canvases = CollectCanvases(panel) }` / MarkupWindow 20-22 | UiMarkupCanvas | FORK markup canvas | **Strip** (flex never needed it; canvas came later). Also strip the RetailUiRuntime consumer of `window.Canvases` (other group) |
| MarkupContentSize 116-119 `case UiMarkupCanvas` | UiMarkupCanvas | FORK markup canvas | **Strip** |
| RegisterTitleBar sets `bar.ThemePalette` -> PluginCloseButton draws `PluginUiStyle.CloseButton` (PluginTitleBar.cs:147) | PluginUiStyle | FORK | not in this group's files, but flagged: PluginTitleBar.cs needs a flat replacement for the themed close button, e.g. palette rectangles like BASE themed buttons (`ctx.DrawFill(... p.Field/p.Selected on hover/press)` plus an X glyph in `p.Text`), Classic art path unchanged |
| UiScrollArea.RegisterTheme (MarkupDocument 172, 289) | may use PluginUiStyle for themed bars | 4-scroll 73fbd4f1 | check in that group's file; BASE equivalent is flat `p.Field/p.Border/p.Muted` like BASE UiScrollbar theming (PluginMarkupTheme scrollbar case: track=Field, rail=Border, nub=Muted) |

Cross-group flex dependencies (exist on main only via the flex series, so they arrive with the other groups' files, not BASE gaps):
PluginTitleBar/PluginCloseButton, PluginWindowChrome (HorizontalInsets, VerticalInsetsFor, TopInset, Border, FrameMinimumWidth, MinimumBarFrameWidth, AuthoredInputs), UiPluginContentHost (UseFlex, UseScroll, ScrollArea, ContentMinimumChanged), UiFlexBox/UiFlexGroup/UiFlexItem (AddLeaf, AddContainer, MeasureNow, ExpandedMaxHeight, ExpandedMinHeight), FlexNode/FlexMeasurement/FlexSize/FlexEdges/FlexLayout.MinimumScrollViewport (1-engine), UiScrollPanel/IUiScrollHost/UiScrollArea (4-scroll), UiLabel.SizedByLayout (UiPanel.cs, 3-markup c15efdec), RetailWindowManager.ComputeAuthoredGeometryRevision(IReadOnlyList<string?>) + Register(..., contentRoot, ...) (2-titlebar) + EnforceMinimumSize (3-markup), UiMarkupExpanderHeader.CaptionX (5-controls a14340b9; that file also has fork-only PluginUiStyle chevron/focus ring from 45a83200: bring only the `CaptionX` const and the caption x change).

Verified present in BASE: UiMenu.BeforeOpen/ColumnWidth/ButtonDatFont, UiSimpleButton.IconSource (UiPanel.cs), UiMarkupExpander.LayoutParts/Header/Expanded/HeaderHeight, UiMarkupSplit.Horizontal/BarSize/DefaultPosition/LayoutAtDefault, UiMarkupNumber.SpinWidth/Field, UiMarkupRadio.Items/Segmented/Horizontal/DatFont, UiMarkupHotkey/Checkbox DatFont, UiMarkupToggle.ThemePalette, PluginUiThemeSettings.ModernFont, PluginPanelDescriptor.IconText/StartVisible, `theme:` colour syntax, BindBool's `setLiteral`.

---

## Port instructions (by series)

**1-engine**: nothing in these files except the `using AcDream.App.UI.Layout.Flex` they rely on.

**2-titlebar**
- MarkupDocument: 15-16 (internal consts); add `BuildWindow` (33-42 shape) and make `Build` delegate to it (31-32); 83 (panel size = content + chrome); MinHeight `+ chromeH` (90) and series-2 MinWidth `FOr(root,"minw",contentW) + chromeW`; 115-136 title bar block **without line 133 `HasTitle`**; 141 `titleColor`; 210-214 revision; 215 as `new MarkupWindow(panel, contentParent, titleBar, revision)` (no Canvases); `TitleBar()` parser 221-232 (series-2 form: `TitleBar(XElement root)` with `null or "false" => false`; 3-markup adds the `hasLayout` param and `null => hasLayout`).
- For the label title (non-bar, BASE behaviour) keep BASE's Register call but place it right after the UiLabel is added inside the label branch: `if (panel is UiPluginMarkupPanel titlePanel) PluginMarkupTheme.Register(titlePanel, panel.Children[^1], new XElement("label"));` (BASE used `panel.Children[0]` after the title block; with a title bar or content host child 0 is no longer the label). Do NOT bring 143-148 (HasTitle/RegisterTitle).
- MarkupWindow: 1-11, 15-19, 23-36, 39-40 (no Canvases).
- PluginMarkupTheme: RegisterTitleBar 24-41 with `ModernTitleFont` replaced by `panel.ModernFont ?? classicFont`, comment reworded. Skip 6-23 and every other hunk.
- ThemeGallery: skip (sample not in BASE).

**3-markup**
- MarkupDocument: 6; 60-65, 71-81; 117; 137 (`else if (!hasLayout && ...)`); 150-160 content host; 163-168 rootFlex; 177-178 pass rootFlex (as last arg after BASE's `recentColors`); 180-194 first measurement (series-3 form of 192 `panel.MinWidth = minW + chromeW`); 216, 218; TitleBar default `hasLayout`; 243-244 `UiFlexBox? flex = null` param; 247 RejectItem; group case 271-300 (non-scroll parts); 314-315 pass groupFlex; 933-934 `if (flex is not null) AddFlexItem(flex, built, el);` after the contextmenu attach; AddFlexItem 936-951 (series-3 form of line 950: `el.Name.LocalName == "group" ? new FlexSize(element.Width, element.Height) : null`; 4-scroll swaps in `AbsoluteGroupSize(element)`) and 982-995.
- MarkupWindow: 12-14, 37-38, TrackContentMinimum (series-3 signature).
- MarkupFlexAttributes: all except 95-107.
- MarkupContentSize: all 3-markup sections, with line 36 deleted and line 100 replaced by `float captionX = ClassicToggleCaptionX;`; skip 116-119 canvas.
- FlexDemo: csproj (minus gallery/controls lines), plugin.json, lock file, finder.xml, settings.xml (series-3 form: no `scroll="y"`, no line 6 list), FlexDemoPlugin.cs 3-markup lines; add FlexDemo to AcDream.slnx.

**4-scroll** (+ followups, shift-sideways)
- MarkupDocument: 66-70 root scroll check; 87-89 FrameMinimumWidth; 169-173 root UseScroll + RegisterTheme; 192 FrameMinimumWidth; 195-201 bar minimum + SetFrameWidth; 217 TrackContentMinimum with authoredMinW/H; group scroll bits 273-274, 279, 286, 288-289, 301-302; 950 `AbsoluteGroupSize(element)`; 1025-1036 AbsoluteGroupSize.
- MarkupWindow: 50, 54.
- MarkupFlexAttributes: 95-107 Scroll().
- FlexDemo: gallery.xml, csproj line 20, settings.xml line 1 `scroll="y"`, FlexDemoPlugin 4-scroll lines; followups: settings.xml line 6 + FlexDemoPlugin 117-118. Reword FlexDemoPlugin line 13 ("from the dock's gear" -> BASE's appearance setting in the plugin side panel).
- shift-sideways: nothing in these files.

**5-controls-in-flex** (incl. merge resolution)
- MarkupDocument: BuildWindow/Build param plumbing 31-32, 43-46 (natural on BASE: BASE already has the control params, so BuildWindow simply carries them); 177-178, 243, 314-315, 933-934 argument order; AddFlexItem 952-981 (expander/split).
- UiMarkupExpander: 35-44, 161-165 (clean).
- UiMarkupNumber: 60-62 only (skip 224-240).
- MarkupContentSize: 38-48, 73-75, 101, 120-142, 148-159, 167-169, 181-185, 189-190.
- UiMarkupExpanderHeader.cs (other group): only the `CaptionX` const + use it for the caption x; skip the PluginUiStyle chevron/focus ring.
- FlexDemo: controls.xml, csproj line 21, FlexDemoPlugin 5-controls lines.
- PluginMarkupTheme 196 `field.ThemePalette = p;` is fork-only despite the merge-resolution attribution: skip.

**6-bound-visible**
- MarkupDocument 1563-1574 (BindBool seeding). Self-contained.

**Never port (FORK-ONLY in these files)**: file icons (MarkupDocument 426, 432-458, 465, 868-888, 1051-1057, 1067, 1069, 1112-1187, 1443-1462), markup canvas (215 init, 900-918, 998-1023; MarkupWindow 20-22; MarkupContentSize 116-119), modern theme title (MarkupDocument 133, 143-148; PluginMarkupTheme 6-23), sharp font twin (PluginMarkupTheme 45-52), themed palettes on button/menu/list header/meter/scrollbar/field (PluginMarkupTheme 91, 150, 156, 168, 175-177, 183, 196), modern number spin (UiMarkupNumber 224-240), ThemeGallery sample.
