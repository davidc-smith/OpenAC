# Plugin flex layout, title bars and scrolling groups — design

Date: 2026-10-05. Status: approved in brainstorming; written spec awaiting
review.

## Goal

Let a plugin author describe a markup window that lays itself out from the
size of the window instead of from absolute coordinates: rows and columns
that share space, controls that size to their content, rows that wrap when
the window narrows, and areas that scroll when their content does not fit.
Alongside it, give plugin windows a real title bar (window name and a close
button) so the window's own title no longer collides with the plugin's
content.

Scope is **markup windows only** (`<panel>` XML through `IUiRegistry`).
Canvases (painter v2) are out of scope.

## What already exists

- **Parsing** (`src/AcDream.App/UI/MarkupDocument.cs`): `Build` parses the
  XML with `System.Xml.Linq` and builds retained `UiElement` controls
  directly, recursively, in `AddElement`; there is no intermediate model.
  Bindings are resolved once into `Func<>`/`Action` delegates and re-read
  every frame. Updates never re-parse; only a plugin reload rebuilds.
- **Geometry** (`UiElement.cs`): `Left/Top/Width/Height` local to the
  parent, in points. Every element clips its children by default (labels and
  menus do not).
- **Anchors** (`UiElement.ApplyAnchor`, `ComputeAnchoredRect`): margins are
  captured once from the authored layout (`CaptureAuthoredAnchorBaselines`
  at the end of `Build`) and reapplied to every child of every visible
  element on every frame inside `DrawSelfAndChildren`. Layout is therefore
  lazy and only covers what is drawn.
- **Auto-sizing today**: `UiLabel.OnDraw` measures its text every frame and
  overwrites its own `Width`/`Height` (so a `left,right` label does not
  actually stretch); `<list>` columns share leftover width with `width="*"`;
  `log` rewraps on width change. No stacks, flow, or measure/arrange pass.
- **Scrolling** exists only inside `list` (`UiMarkupList`, built on
  `UiScrollable`, 16-point bar with game art), `log`, and multiline `field`.
  Groups and panels never scroll.
- **Window chrome**: `UiNineSlicePanel` (Classic, 5-point border,
  `RetailChromeSprites.Border`) and `UiPluginMarkupPanel` (modern themes,
  in `PluginUiThemeSettings.cs`, draws a 24-point header band —
  `PluginUiStyle.HeaderHeight` — when `HasTitle`). `title` adds a plain
  `UiLabel` at (8, 4). Children are positioned from the frame's corner: the
  border and the title row are not subtracted, which is why content and
  title clash.
- **No close button** exists on plugin windows, although
  `docs/plugin-ui-markup.md` ("Showing and hiding your own window") refers to
  one. Visibility is the player's request (dock slot) combined with the
  bound `visible`, in `PluginWindowVisibilityController`.
- **Resize**: opt-in with `resizable="true"`; `minw`/`minh` default to the
  authored size; `UiRoot` resizes by dragging a 5-point edge and raises
  `WindowResized` on mouse-up, consumed only by layout persistence. A stored
  size is reset when `RetailWindowManager.ComputeAuthoredGeometryRevision`
  changes. Plugins are never told about a resize.
- **Theming** is a post-build pass (`PluginMarkupTheme.Register`) keyed on
  control type; Classic must stay byte-identical for windows that do not opt
  in.

## Constraints

- **No contract change.** `AcDream.Plugin.Abstractions` is untouched, so no
  fork contract version bump. New markup elements and attributes are a
  documented behaviour contract in `docs/plugin-ui-markup.md`.
- **Existing markup keeps working byte-for-byte.** Everything new is opt-in
  by attribute, except where stated below for windows that already opt in
  to `layout`.
- **Fork first.** Work lands on fork main as a PR series; nothing goes
  upstream until the user decides. The spec and plans live only on the
  fork-only branch `docs/flex-layout-spec` (docfx publishes all of
  `docs/**.md`, so `docs/superpowers/` never enters an upstream PR).

## Decisions

| Question | Decision |
|---|---|
| Surface | Markup windows only |
| Flex fidelity | Practical flexbox subset (no `order`, `align-content`, baseline) |
| Item sizing | Content sizes, with explicit `w`/`h`/`basis` as overrides |
| Overflow | Content minimum drives the window minimum, plus scrolling groups |
| Syntax | Attributes on `<group>` and the root `<panel>` (no new element names) |
| Engine | In-house pure C# `FlexLayout` (not Yoga, not `UiLayoutPolicy`) |
| Title bar default | On for windows that use `layout`; opt-in `titlebar="true"` elsewhere; `titlebar="false"` turns it off |

## 1. Markup vocabulary

### 1.1 Flex containers

`layout="row|column"` on a `<group>` or on the root `<panel>` makes it a flex
container. Without `layout` the element keeps today's absolute layout.

| Attribute | Values | Default |
|---|---|---|
| `layout` | `row`, `column` | absent: absolute |
| `gap` | points between items on a line and between wrapped lines | `0` |
| `padding` | 1, 2 or 4 numbers in CSS order (all; vertical horizontal; top right bottom left) | `0` |
| `justify` | `start`, `center`, `end`, `space-between` | `start` |
| `align` | `start`, `center`, `end`, `stretch` | `stretch` |
| `wrap` | `true`, `false` | `false` |

These attributes (other than `layout`) are a build error on an element
without `layout`.

### 1.2 Flex items

Every child of a flex container may carry:

| Attribute | Meaning | Default |
|---|---|---|
| `grow` | share of positive free space on the main axis | `0` |
| `shrink` | share of negative free space, weighted by basis | `1` |
| `basis` | `auto` or points; the main-axis starting size | `auto` (the item's `w`/`h` on that axis, else its content size) |
| `w`, `h` | preferred size, overriding the content size | content size |
| `minw`, `maxw`, `minh`, `maxh` | clamps | min: the control's content minimum; max: unbounded |
| `alignself` | `start`, `center`, `end`, `stretch` | the container's `align` |

As in CSS (`min-width: auto`), an item never shrinks below its content
minimum unless `minw`/`minh` set a lower floor.

### 1.3 Rules

- **`x`, `y` and `anchor` on a flex item are a build error.** The container
  places its items.
- **Item attributes on a child of an absolute container are a build error**
  (`grow`, `shrink`, `basis`, `alignself`, and `minw`/`maxw`/`minh`/`maxh`
  on a non-root element).
- **Mixing by nesting.** An absolute `<group>` (no `layout`) inside a flex
  container is an ordinary item whose content size is its authored `w`/`h`.
  When flex gives it a different size, its children follow their `anchor`s
  exactly as children of a resized panel do today (baselines from the
  authored layout). A flex `<group>` inside an absolute container is placed
  by its `x`/`y`/`w`/`h`/`anchor` like any other element and lays out its own
  children in whatever size it ends up with.
- **Hidden items take no space** (CSS `display: none`). Changing `visible`
  (literal or bound) reflows the container. Absolute layout keeps today's
  behaviour: hiding only hides.
- **Units** are points, as everywhere else in markup.

### 1.4 The content area of the root panel

A root panel has a **content area** when it uses `layout` or has the title
bar (section 2). For such a panel:

- children are placed inside the content area: `x`/`y` of absolute children
  are measured from its top-left corner, and flex lays out inside it (after
  `padding`);
- the content area is inset from the frame by the chrome: 5 points on the
  left, right and bottom (the Classic border; themed windows use the same
  inset), and on top either the 24-point title bar or, without a bar, the
  5-point border. The insets are constants in one place (`PluginWindowChrome`);
- `w`, `h`, `minw` and `minh` describe the **content area**; the host adds
  the chrome to get the frame;
- a root with `layout` may omit `w`/`h`: the window opens at its content
  size (section 3.4).

A root panel with neither `layout` nor a title bar is unchanged: no content
area, coordinates from the frame corner, `w`/`h` the frame size.

## 2. Window title bar

### 2.1 Behaviour

- A strip across the top of the window, `PluginWindowChrome.TitleBarHeight`
  (24 points) tall.
- **Title text**, left-aligned and ellipsised if it does not fit: the
  markup's `title`, else the registration's `PluginPanelDescriptor.Title`
  (always set, per window). `MarkupDocument.Build` gains a fallback-title
  parameter for this; the plugin API does not change.
- **Close button**, right-aligned, square, the height of the bar. Clicking
  it does exactly what the dock slot does when the window is open: it clears
  the player's request through `PluginWindowVisibilityController`. The plugin
  keeps running; the dock reopens the window. It is keyboard-focusable like
  other markup buttons (Tab, then Enter/Space) and does not take focus from
  the game on click.
- **Dragging** the bar moves the window (windows already drag from any
  non-interactive point; the bar is non-interactive apart from the close
  button).
- With the bar on, `title` no longer creates the (8, 4) label; the text is
  drawn by the bar.

### 2.2 Defaults

| Root panel | Bar |
|---|---|
| has `layout` | on, unless `titlebar="false"` |
| no `layout`, `titlebar="true"` | on |
| no `layout`, no `titlebar` | off (today's behaviour, unchanged) |

`titlebar` takes `true` or `false`; anything else is a build error. An
absolute window adopting the bar needs only `titlebar="true"`: its children
keep their coordinates (now measured from the content area) and the window
grows by the chrome.

### 2.3 Looks

- **Classic**: the game's close-button art (the same sprites the retail
  windows' close buttons use, `WindowChromeController`), title in the
  `ControlsIni` `title` colour that the title label uses today, over the
  window's own frame.
- **Modern themes**: today's `PluginUiStyle.Header` band with the title in
  the themed title font, and an X drawn with the painter-v2 shapes
  (`CanvasGeometry`), muted at rest, text colour on hover, accent ring when
  focused — matching themed buttons.

### 2.4 Docs correction

`docs/plugin-ui-markup.md` currently describes a close button that does not
exist. The title-bar PR rewrites that paragraph to say the close button is
on the title bar, and that a window without the bar is closed from its dock
slot or by `HidePanel`.

## 3. Layout engine

### 3.1 `FlexLayout` module

A pure module in `src/AcDream.App/UI/Layout/Flex/` with no dependency on
`UiElement`:

- **Input**: a tree of `FlexNode`s. A node has container settings
  (direction, gap, padding, justify, align, wrap, scroll axes), item
  settings (grow, shrink, basis, preferred w/h, min/max, alignself, hidden),
  and, for a leaf, a measure callback `Measure(float? availableWidth)`
  returning `(Size preferred, Size minimum)`.
- **Output**: a rectangle per node, relative to its parent's content box,
  plus the container's content size (for scrolling) and its content minimum.
- **Measure** (bottom-up): content size and content minimum for every node.
  A container's content size is the size its items need at their bases with
  gaps and padding; its minimum is the same at item minimums (for `wrap`,
  the minimum main size is the widest single item, and the minimum cross
  size is computed at that main size).
- **Arrange** (top-down), CSS flexbox reduced to the chosen subset:
  1. Resolve each visible item's flex basis (`basis`, else the main-axis
     `w`/`h`, else the measured preferred size) and its hypothetical main
     size (basis clamped to min/max).
  2. If `wrap`, break items into lines greedily by hypothetical size plus
     gap; otherwise one line.
  3. Per line, resolve flexible lengths: distribute positive free space by
     `grow`, or negative free space by `shrink × basis`; clamp to min/max;
     freeze violators and repeat until stable (at most item-count passes).
  4. Cross size per item: `stretch` fills the line's cross size (respecting
     `minh`/`maxh` or `minw`/`maxw`), others use their preferred cross size.
     Line cross size is the largest item; without `wrap` the single line
     takes the container's cross size.
  5. Main-axis position from `justify` and `gap`; cross-axis position from
     `align`/`alignself`.
  6. Snap final positions and sizes to whole points (edges are rounded, sizes
     derived from rounded edges), so adjacent items never overlap or gap by a
     subpixel.
- Free space that cannot be satisfied (items at their minimums) overflows
  the container's end and is clipped (or scrolls, section 4).
- Never throws: NaN, infinity and negative results clamp to 0.

### 3.2 Content sizes

Controls implement a new internal interface `IUiContentSize` with
`Measure(float? availableWidth) → (preferred, minimum)`. All defaults live
as named constants in one class so they can be tuned in one place.

| Control | Preferred | Minimum |
|---|---|---|
| `label` | text width × line height, in its font | same (no wrapping in v1) |
| `button`, `tab` | text + icon + horizontal padding × the theme's control height | same |
| `toggle` | glyph + gap (the themed 18-point offset where it applies) + text | same |
| `icon` | 32 × 32 (or authored `w`/`h`) | same |
| `field`, `menu` | 120 wide × the font's control height | 40 wide |
| `slider`, `meter` | 120 wide × their art or default height | 40 wide |
| `list`, `log` | 160 × 80 | 60 × 40 |
| flex `group` | its own layout's content size | its own content minimum |
| absolute `group` | authored `w` × `h` | same |

Inside a flex container a label stops resizing itself in `OnDraw`; it reports
its size through `IUiContentSize` and draws in the rectangle it is given.
Labels in absolute layouts keep today's behaviour.

### 3.3 Integration and caching

- A new `UiFlexGroup` (a `UiPanel` subclass, same background/border
  attributes as `group`) and the root panel's content area own a cached
  `FlexNode` tree mirroring their children.
- In `DrawSelfAndChildren`, where `ApplyAnchor` runs for children today, a
  flex container calls `EnsureLayout()` and assigns its children's
  `Left/Top/Width/Height` from the result instead of applying anchors.
- The cache is invalidated when the container's size changes, a child's
  visibility changes, a child's content size changes, or the theme changes
  (fonts and paddings change). Content size changes are detected cheaply in
  tick: a control whose measured inputs changed (bound caption text, font)
  marks itself dirty, and dirtiness propagates up to the nearest flex root
  whose size depends on it.
- Hidden subtrees stay dirty until shown and lay out on their first draw,
  preserving the existing guarantee that whether and when an element was
  visible never changes where it lands.
- Hit-testing and `ScreenPosition` read the same `Left/Top/Width/Height`,
  so they need no change for flex (scrolling adds an offset, section 4).
  As today, geometry is valid after the frame that laid it out.

### 3.4 Window size and minimum

- When the root uses `layout`, the window's `MinWidth`/`MinHeight` are the
  content minimum plus chrome, unless `minw`/`minh` are authored (authored
  values win, as content-area sizes plus chrome).
- A root without `w`/`h` opens at its content size plus chrome.
- The minimum is recomputed when the content minimum changes (a longer
  caption). If the window is now smaller than its minimum, it grows to the
  minimum through `RetailWindowManager.ResizeTo`, which also keeps it on
  screen.
- `ComputeAuthoredGeometryRevision` also covers the root's `layout` and
  `titlebar` settings and whether `w`/`h` were authored, so changing a
  window's design resets each player's stored size once (position kept), as
  authored size changes do today. Saved sizes are frame sizes and stay so.

## 4. Scrolling groups

### 4.1 Markup

`scroll="y|x|both"` on a `<group>` (flex or absolute) or on the root
`<panel>` (scrolls the content area). Absent: no scrolling, content clips as
today. Any other value is a build error.

### 4.2 Extent

- **Flex**: along a scrolling axis the container's available size is
  unbounded for layout. A scrolling `column` does not shrink its items; a
  `row wrap` with `scroll="y"` keeps its width and grows downward. The
  scroll extent is the laid-out content size.
- **Absolute**: the extent is the bounding box of the visible children
  (plus nothing else; authors add their own trailing margin).
- **Minimum**: along a scrolled axis, the group's content minimum is the
  scrollbar width plus 40 points, so a scrolling area never pushes the
  window's minimum up.

### 4.3 Mechanics

- **Content offset**: `UiElement` gains an internal content offset applied
  to children in draw (transform), hit-testing, clipping and
  `ScreenPosition`, so popups (a `menu` inside a scrolled group) open in the
  right place.
- **Scrollbar**: a 16-point bar per scrolling axis, reserved only while
  content overflows on that axis (the `list` rule). The bar drawing, thumb
  maths and thumb dragging in `UiMarkupList` (on `UiScrollable`) move into a
  shared helper used by `list`, `log` and scrolling groups. Classic uses the
  game's scrollbar art; modern themes use the themed rounded bar. When both
  axes overflow, the corner square is left empty.
- **Wheel**: delivered to the innermost element under the pointer that
  consumes it; a list inside a scrolling group scrolls itself, the group
  scrolls elsewhere. Shift+wheel scrolls horizontally on `x`/`both` groups.
- **Focus**: when keyboard focus moves to a control (Tab, or a field taking
  focus), each scrolling ancestor scrolls the minimum distance to bring it
  into view.
- **Position** survives hide/show and theme changes, is clamped when the
  group resizes or its content shrinks, and is not persisted across
  sessions.

### 4.4 Out of scope (v1)

Smooth or kinetic scrolling, sticky headers, and any plugin API to read or
set the scroll position (a later additive contract member if needed).

## 5. Errors

Build-time `FormatException`, naming the element and the offending value:

- unknown `layout`, `justify`, `align`, `alignself`, `scroll` or `titlebar`
  value;
- container attributes (`gap`, `padding`, `justify`, `align`, `wrap`) on an
  element without `layout`;
- `x`, `y` or `anchor` on a flex item;
- item attributes on a child of an absolute container (section 1.3);
- negative `grow`, `shrink`, `gap` or `padding`;
- `minw` greater than `maxw`, or `minh` greater than `maxh`;
- `padding` with other than 1, 2 or 4 numbers, or a non-number.

Run-time layout never throws (section 3.1).

## 6. Theming

- `PluginMarkupTheme.Register` gets cases for `UiFlexGroup` (as `group`), the
  title bar and close button, and the shared scrollbar.
- Content sizes are measured with the active theme's fonts and paddings; a
  theme switch invalidates layout (section 3.3).
- `PluginThemeClassicIdentityTests` (and the existing markup suites) must
  pass unchanged: windows that opt in to nothing render byte-identically.

## 7. Testing

All headless, against fake resolvers, under `tests/AcDream.App.Tests/UI/`:

- `Layout/Flex/FlexLayoutTests`: table-driven cases with hand-computed CSS
  results — grow, shrink (basis-weighted), basis forms, min/max clamping and
  re-resolution, wrap line breaking, each `justify` and `align`, `alignself`,
  gap, padding, hidden items, nesting, whole-point snapping, overflow, NaN
  inputs.
- `MarkupFlexTests`: parsing of every attribute, every error in section 5,
  mixed nesting (absolute group in flex and the reverse), label behaviour in
  and out of flex, reflow on visibility and caption change, theme switch.
- `MarkupTitleBarTests`: the default table in 2.2, title fallback to the
  descriptor, the content-area offset and chrome sizing, close button
  clearing the player's request, keyboard activation, Classic and modern
  looks.
- `UiScrollGroupTests`: extent (flex and absolute), bar reservation,
  offset in draw and hit-testing, `ScreenPosition` and popup placement, wheel
  routing with a nested list, Shift+wheel, focus scrolling into view,
  clamping on resize, position across hide/show.
- Window minimum flow, open-at-content-size, grow-to-minimum, and the
  persistence revision reset (extending `RetailWindowLayoutPersistenceTests`).
- A budget test: a 200-icon wrapping grid relaid out every frame during a
  simulated resize stays within a fixed time budget.

## 8. Sample

`samples/AcDream.Plugins.FlexDemo`, three windows:

1. a toolbar (`field grow="1"` + buttons) over a growing list over an
   OK/Cancel footer (`justify="end"`);
2. a wrapping icon grid in a `scroll="y"` group;
3. a scrolling settings form whose bound caption changes length at runtime.

## 9. Delivery

Single-topic PRs, each on a branch from fork main (the modern-theme look the
title bar and scrollbar need lives there), merged into fork main after
review:

1. **`flex/engine`**: `FlexLayout`, `FlexNode` and `FlexLayoutTests`. No
   markup change.
2. **`flex/titlebar`**: `PluginWindowChrome`, the content area, the title
   bar and close button, `titlebar` on absolute windows, the docs correction
   (2.4). Fixes the title/content clash in existing plugins by a one-attribute
   opt-in.
3. **`flex/markup`**: `layout` and item attributes, `IUiContentSize`,
   `UiFlexGroup`, window minimum flow, docs section "Flex layout", the
   `FlexDemo` sample (windows 1 and 3 without scrolling).
4. **`flex/scroll`**: scrolling groups, the shared scrollbar helper, docs
   section "Scrolling groups", `FlexDemo` window 2 and scrolling in window 3.

Gates: each PR passes the App test suite with no new failures against the
fork-main baseline; PRs 2–4 are checked live on the local ACE test server
(10.10.20.20, `~/OpenAC-dev/dev-client.env`) with 2× captures in Classic and
both modern themes.

## Open questions

None at the time of writing.
