# Plugin flex layout, title bars and scrolling groups — design

Date: 2026-10-05. Status: approved; revised after a Codex review and while
planning PRs 1–3 — see "Planning corrections" and "Review changes" at the end.

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

**Tree ownership.** A root with a content area builds two layers under the
frame: a **content host** (`UiPluginContentHost`, a `UiElement` placed at the
content area's rectangle) that owns every plugin-authored child, and a
**chrome layer** that owns the title bar and close button. Only the content
host takes part in flex layout, scroll extent, scroll offset and content
clipping; the chrome layer never scrolls, never enters the flex tree, and
comes before the content in Tab order. The content host is what is passed to
`RetailWindowManager.Register` as the window's `ContentRoot`. `FindByName`
(used by `ControlExists`/`SetControlLabel`/`SetControlVisible`) still finds
authored controls, since it searches the whole tree; chrome controls carry no
authored name.

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
  keeps running; the dock reopens the window.
- **Wiring.** `MarkupDocument.Build` runs before the window is registered,
  so the close button cannot call the window manager directly. The built
  panel exposes an internal `CloseRequested` event; `RetailUiRuntime.MountPlugins`
  subscribes to it after `Host.WindowManager.Register` and calls
  `RetailWindowManager.Close(panel.WindowName)`, the same path a dock slot
  takes, so the visibility controller's player request is cleared and the
  handle's normal `Hidden`/`Closed` notifications fire. It is keyboard-focusable like
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
- **Unbounded is explicit.** "No limit" is represented as `null`
  (`float?` available sizes and nullable maximums), never as infinity. A
  scrolling axis lays out with a `null` available size; an absent `maxw` is a
  `null` maximum.
- **Output**: a rectangle per node, relative to its parent's top-left
  corner (its border box: the parent's padding is included in the offset,
  which is what `UiElement.Left/Top` want),
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
  4. Cross size per item: `stretch` fills the line's cross size (clamped to
     the item's limits; the minimum is its content minimum, so stretch never
     squashes an item below its content, unlike CSS's cross-axis
     `min-height: auto` of 0), others use their preferred cross size. A
     line's cross size starts as its largest item; without `wrap` the single
     line takes the container's cross size, and with `wrap` spare cross space
     is shared equally between the lines (CSS's default `align-content`).
  5. Main-axis position from `justify` and `gap`; cross-axis position from
     `align`/`alignself`.
  6. Snap to whole points by **cumulative edges**: start from the line's
     unsnapped start, accumulate the unsnapped item sizes and gaps, round each
     successive edge, and derive each item's size from its two rounded edges.
     Rounding error is therefore spread across the line instead of piling up
     at its end, the last edge lands where the unsnapped one would, and
     adjacent items never overlap or gap by a subpixel. The cross axis rounds
     each item's two edges the same way.
- **Height-for-width.** A wrapping container whose own main axis is its
  parent's cross axis (a wrapping row inside a column) and that does not
  scroll is sized, along the parent's main axis, by the lines it makes at the
  cross size it is actually given (stretched over a single line, else its
  preferred cross size within the space); its minimum along that axis is the
  same extent. Its measured minimum (the lines at its narrowest) still feeds
  the window minimum but does not lift its preferred cross size, which is
  one line.
- **Recursive.** Height-for-width applies at any depth: a node is
  *size-dependent* when it is a non-scrolling container that wraps or has a
  size-dependent child, and its parent sizes it by laying its subtree out at
  the given size with the dependent axis unbounded (a dry run that does not
  re-measure leaves). Extents are taken at both candidate snapped sizes
  (floor and ceiling), because greedy line breaking is not monotonic with
  mixed item sizes; for the same reason a root wrapping container's measured
  minimum cross size is not a strict bound.
- **Limitation (v1): one dependence direction.** Exact height-for-width
  holds when every wrapping container in a chain wraps the same way
  (wrapping rows, the toolbar and icon-grid shapes). A node that depends on
  its size both ways — a wrapping column holding wrapping rows, directly or
  through plain containers — is resolved along one axis only and may
  overflow its rect (and clip). PR 3 decides whether markup allows `wrap`
  on columns at all.
- **Known gaps for PR 3 to settle** (from the PR 1 re-review): a
  container's measured minimum is built from its items' minimum sizes, but
  lines break on their hypothetical sizes, so when items prefer more than
  their minimum (a `basis` above the content, an explicit `minw` below it, a
  nested wrapping container) a root over a size-dependent chain can need
  more than its `Measured.Minimum` and clip at the window minimum; making
  minimums strict means breaking minimum lines on `max(min, clamp(basis))`.
  A size-dependent node's `Measured.Preferred` can be below its
  `Measured.Minimum` on the dependent axis, so window sizing must clamp.
  Layout cost roughly doubles per level of nested size dependence (each
  extent is laid out at the floor and ceiling sizes), about N·2^D; cache
  extents or skip the ceiling when it equals the floor if profiling asks.
- **Explicit cross size of a size-dependent child laid across its parent**
  is capped to the available cross size, so its extent and its rect agree.
  This departs from CSS, where an explicit size overflows; ordinary items
  keep their explicit cross size.
- `Measure` and `Arrange` each run the measure pass; an additive arrange
  overload that reuses a fresh measurement can come later if profiling asks
  for it.
- Free space that cannot be satisfied (items at their minimums) overflows
  the container's end and is clipped (or scrolls, section 4).
- Never throws. Authored numbers are finite by construction (section 5);
  a computed result that is non-finite or negative (a defect, not an
  expected path) is clamped to 0. `null` constraints are not affected by
  this rule.

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
  `FlexNode` tree mirroring their children. **One tree per flex root**: a
  nested `UiFlexGroup` maps to an inner node (no measure callback) of its
  flex ancestor's tree, never to a leaf, because height-for-width only works
  across inner nodes; absolute groups and controls map to leaves with an
  `IUiContentSize` measure callback.
- In `DrawSelfAndChildren`, where `ApplyAnchor` runs for children today, a
  flex container calls `EnsureLayout()` and assigns its children's
  `Left/Top/Width/Height` from the result instead of applying anchors.
- **Invalidation protocol.** `UiElement` gains `InvalidateMeasure()` and a
  measure version. Calling it marks the element's measurement stale and walks
  up the parent chain: every flex container on the way is marked for
  re-layout, and the walk continues past a container only while that
  container's own size depends on its content (it is a flex item with
  `basis="auto"` and no `w`/`h` on the main axis, or it sits in a
  content-sized root). It stops at the first container with a fixed size, and
  at the root content host, which also re-evaluates the window minimum
  (section 3.4).
- **Measurement dependencies**, each of which calls `InvalidateMeasure()` when
  it changes:

  | Dependency | Detected |
  |---|---|
  | bound or literal text (label, button, tab, toggle) | each tick, comparing the resolved string with the last measured one |
  | icon presence on a button | each tick, comparing the resolved id's zero-ness |
  | font (theme switch, sharper bake) | the theme-change action registered by `PluginMarkupTheme` |
  | theme paddings and control heights | same theme-change action |
  | visibility of a flex item | the `Visible` setter, when the element's parent is a flex container |
  | a nested container's content size | propagated by the walk above |
  | the container's own size | compared with the size of the last layout in `EnsureLayout()` |

  Setting geometry or text from code inside the host (for example
  `SetControlLabel`) goes through the same properties, so it invalidates the
  same way.
- Hidden subtrees stay dirty until shown and lay out on their first draw,
  preserving the existing guarantee that whether and when an element was
  visible never changes where it lands.
- Hit-testing and `ScreenPosition` read the same `Left/Top/Width/Height`,
  so they need no change for flex (scrolling adds an offset, section 4).
  As today, geometry is valid after the frame that laid it out.

### 3.4 Window size and minimum

- **Eager first measurement.** At the end of `MarkupDocument.Build`, a root
  with a content area runs one full measure and layout pass (with the fonts
  and theme known at build time) before it is returned. That pass fixes the
  frame's starting `Width`/`Height` (for a root without `w`/`h`) and its
  `MinWidth`/`MinHeight`, so mounting and `RetailWindowManager.Register`
  see real geometry and layout persistence restores against it. Later
  layouts stay lazy (section 3.3).
- When the root uses `layout`, the window's `MinWidth`/`MinHeight` are the
  content minimum plus chrome, unless `minw`/`minh` are authored (authored
  values win, as content-area sizes plus chrome).
- A root without `w`/`h` opens at its content size plus chrome.
- **Enforcing a changed minimum.** The minimum is recomputed when the content
  minimum changes (a longer caption). `ResizeTo` cannot be used for this: it
  leaves an axis alone when `ResizeX`/`ResizeY` is false (markup windows are
  not resizable by default) and never moves the window. A new
  `RetailWindowManager.EnforceMinimumSize(name)` instead:
  1. grows each axis whose size is below the minimum, regardless of
     `ResizeX`/`ResizeY` (a non-resizable flex window simply tracks its
     content minimum);
  2. if the grown frame's right or bottom edge is now past the screen, moves
     the frame left or up by the excess, no further than the screen's left or
     top edge;
  3. if the minimum itself is larger than the screen on an axis, sets that
     axis to the screen size and pins the frame to the screen's origin on it;
     content beyond that clips (or scrolls);
  4. rebases anchors and raises `WindowResized` (and a move notification if
     it moved) like `ResizeTo`, so the new geometry is persisted.
- **Saved-size revision from authored inputs only.** For a root with a
  content area, `ComputeAuthoredGeometryRevision` hashes what the markup
  states, never measured sizes: each of `w`, `h`, `minw`, `minh` as the
  authored value or "absent", `resizable`, `resize`, `layout`, `titlebar`, and
  a `PluginWindowChrome.Version` constant bumped whenever the chrome insets
  change. Bound captions and theme metrics therefore never reset a player's
  saved size; changing the window's design resets it once (position kept), as
  authored size changes do today. Windows without a content area keep today's
  hash unchanged. Saved sizes are frame sizes and stay so.

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
- **Absolute**: the extent is the union of the group's own origin (0, 0) and
  the rectangles of its visible children, measured after the children's
  anchors have been applied for the group's current viewport size. The
  union's right and bottom edges give the extent; children at negative
  coordinates are not reachable by scrolling (the offset never goes below
  0), matching how they clip today. Authors add their own trailing margin.
- **Order**: within a frame, a scrolling group first lays out its children
  (flex) or applies their anchors (absolute) against its viewport, then
  computes the extent, then settles the scrollbars (4.3), then draws. Extent
  is never read from a previous frame's geometry.
- **Minimum**: on each scrollable axis the group's content minimum is a
  40-point viewport along that axis. The scrollbar's thickness is added to
  the *other* axis's minimum when that bar can appear (a `scroll="y"` group's
  minimum width includes the 16-point vertical bar). A scrolling area
  therefore never pushes the window's minimum up along the axis it scrolls.

### 4.3 Mechanics

- **Content offset**: `UiElement` gains an internal content offset applied
  to children in draw (transform), hit-testing, clipping and
  `ScreenPosition`, so popups (a `menu` inside a scrolled group) open in the
  right place.
- **Scrollbar**: a 16-point bar per scrolling axis, reserved only while
  content overflows on that axis (the `list` rule).
- **Settling the bars.** A vertical bar narrows the viewport, which can make
  content overflow horizontally, and a horizontal bar does the reverse. The
  group starts with no bars, lays out (or measures the extent) for the
  current viewport, adds every bar whose axis now overflows, and repeats
  with the reduced viewport until the set of bars stops changing. Bars are
  only ever added during one settle, so it ends after at most three layouts.
  For a flex group whose scrolling axis is unbounded, only the
  non-scrolling axis narrows; the extent is recomputed at the narrower size. The bar drawing, thumb
  maths and thumb dragging in `UiMarkupList` (on `UiScrollable`) move into a
  shared helper used by `list`, `log` and scrolling groups. Classic uses the
  game's scrollbar art; modern themes use the themed rounded bar. When both
  axes overflow, the corner square is left empty.
- **Wheel**: delivered to the innermost element under the pointer that
  consumes it; a list inside a scrolling group scrolls itself, the group
  scrolls elsewhere.
- **Horizontal wheel (input change in scope).** Today
  `RetainedUiInputBinding.OnScroll` keeps only the sign of the vertical delta
  and `UiRoot.OnScroll(int dy)` takes nothing else. PR 4 extends the path to
  carry a horizontal step and the Shift modifier: the binding passes
  `(dx, dy, shift)` with each component reduced to -1, 0 or +1 as today, and
  `UiRoot` gains an `OnScroll(int dx, int dy, bool shift)` overload; the
  existing `OnScroll(int dy)` forwards to it with `dx = 0`. Routing:
  - a native horizontal delta (trackpad sideways swipe, tilt wheel) goes to
    the innermost element under the pointer that consumes horizontal
    scrolling — only `x`/`both` groups do in v1;
  - Shift with a vertical delta is remapped to horizontal *before* routing,
    and then follows the horizontal rule; elements that consume only vertical
    scrolling (lists, logs, `y` groups) do not see it;
  - an unmodified vertical delta keeps today's routing.
  Existing controls that override vertical wheel handling are unchanged.
- **Focus**: when keyboard focus moves to a control (Tab, or a field taking
  focus), each scrolling ancestor scrolls the minimum distance to bring it
  into view.
- **Position** survives hide/show and theme changes, is clamped when the
  group resizes or its content shrinks, and is not persisted across
  sessions.

### 4.3a Engine follow-ups for PR 4

Found in the PR 1 review, to be handled additively in PR 4: a scrolling
wrapping grid laid across its parent (a `scroll="y"` icon grid in a column
without `grow`) currently gets only the 40-point viewport even when there is
room — it should get its lines' extent with the 40-point viewport as its
minimum; and the engine needs an input (or in-engine settling) so a nested
scroll container lays its children out at its viewport minus a reserved bar.

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
- `padding` with other than 1, 2 or 4 numbers, or a non-number;
- any numeric value introduced by this spec (`gap`, `padding`, `grow`,
  `shrink`, `basis`, `w`, `h`, `minw`, `maxw`, `minh`, `maxh`) that is
  `NaN` or infinite. .NET parses `NaN` and `Infinity` as numbers, so they are
  checked explicitly; the parse helpers used for these attributes reject
  non-finite values with the element, attribute and value in the message.
  (Existing attributes keep today's lenient parsing.)

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
  gap, padding, hidden items, nesting, cumulative-edge snapping (odd widths,
  fractional gaps, `space-between` with remainders), overflow, `null`
  (unbounded) constraints, and defensive clamping of a non-finite computed
  value.
- `MarkupFlexTests`: parsing of every attribute, every error in section 5
  (including `NaN`/`Infinity`), mixed nesting (absolute group in flex and the reverse), label behaviour in
  and out of flex, reflow on visibility and caption change, theme switch.
- `MarkupTitleBarTests`: the default table in 2.2, title fallback to the
  descriptor, the content-area offset and chrome sizing, the content host /
  chrome layer split (chrome outside flex, scroll and offset; chrome first in
  Tab order; content host as `ContentRoot`), `CloseRequested` wired through
  `RetailWindowManager.Close` clearing the player's request and raising
  `Hidden`/`Closed`, keyboard activation, Classic and modern looks.
- `UiScrollGroupTests`: extent (flex and absolute, negative coordinates,
  anchored children after a resize), bar settling (vertical bar causing
  horizontal overflow and the reverse), minimums per axis, offset in draw and
  hit-testing, `ScreenPosition` and popup placement, wheel routing with a
  nested list, native horizontal delta, Shift+wheel remapping, focus
  scrolling into view, clamping on resize, position across hide/show.
- Input: `RetainedUiInputBinding` passing horizontal steps and Shift, and the
  `UiRoot.OnScroll(int dy)` forwarder.
- Invalidation: each dependency in the 3.3 table invalidates; the walk stops
  at a fixed-size container and continues through content-sized ones.
- Window minimum flow, the eager first measurement (a root without `w`/`h`
  registers with its content size), `EnforceMinimumSize` (non-resizable
  window grows, frame moves back on screen, minimum larger than the screen),
  and the authored-inputs revision (a bound caption changing does not change
  it; a `layout` or `w` change does), extending
  `RetailWindowManagerTests` and `RetailWindowLayoutPersistenceTests`.
- Scaling, not wall-clock: a wrapping grid laid out at 50, 100 and 200 items
  performs a number of measure calls linear in the item count (counted with
  an instrumented measure callback), a cached layout with no invalidation
  performs none, and a steady-state re-layout allocates nothing (checked
  with `GC.GetAllocatedBytesForCurrentThread` around a warmed-up call).

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
2. **`flex/titlebar`**: `PluginWindowChrome`, the content host and chrome
   layer, the eager root measurement, the authored-inputs revision, the
   title bar and close button (`CloseRequested`), `titlebar` on absolute windows, the docs correction
   (2.4). Fixes the title/content clash in existing plugins by a one-attribute
   opt-in.
3. **`flex/markup`**: `layout` and item attributes, `IUiContentSize`,
   `InvalidateMeasure()`, `UiFlexGroup`, window minimum flow and
   `EnforceMinimumSize`, docs section "Flex layout", the
   `FlexDemo` sample (windows 1 and 3 without scrolling).
4. **`flex/scroll`**: scrolling groups, the shared scrollbar helper, the
   horizontal-wheel input change (4.3), docs section "Scrolling groups", `FlexDemo` window 2 and scrolling in window 3.

Gates: each PR passes the App test suite with no new failures against the
fork-main baseline; PRs 2–4 are checked live on the local ACE test server
(10.10.20.20, `~/OpenAC-dev/dev-client.env`) with 2× captures in Classic and
both modern themes.

## Open questions

None at the time of writing.

## Planning corrections (PR 1)

Found while prototyping the engine (see the PR 1 plan's "Spec corrections"):
height-for-width for wrapping containers laid across their parent (3.1),
wrapped lines sharing spare cross space and stretch keeping the content
minimum (3.1 step 4), and the flex types being `public` (xUnit theories
take enum parameters; `UiElement` is public too).

## Review changes

A Codex review of the first draft (2026-10-05) raised 13 findings; all were
accepted:

| Finding | Change |
|---|---|
| Minimum growth via `ResizeTo` ignores non-resizable axes and never moves the window | `EnforceMinimumSize` (3.4) |
| Unsized root registered before its first (lazy) layout | Eager first measurement in `Build` (3.4) |
| Close button had no path to the window manager | `CloseRequested` wired after registration (2.1) |
| Saved-size revision would hash content-derived sizes | Authored-inputs revision (3.4) |
| Circular scrollbar reservation; minimum added bar thickness on the wrong axis | Bar settling and per-axis minimum (4.2, 4.3) |
| Shift+wheel not representable in the input path | Horizontal wheel input change in PR 4 (4.3) |
| Chrome and content shared one tree | Content host and chrome layer (1.4) |
| "Unbounded" clashed with "infinity clamps to 0" | `null` constraints (3.1) |
| Absolute scroll extent underspecified | Origin policy and ordering (4.2) |
| Invalidation ownership vague | `InvalidateMeasure()` protocol and dependency table (3.3) |
| Snapping error distribution unspecified | Cumulative-edge snapping (3.1) |
| `NaN`/`Infinity` parse as numbers | Build-time error (5) |
| Wall-clock performance test flaky | Scaling and allocation tests (7) |

## Planning corrections (PR 2)

Found while planning the title bar (see the PR 2 plan's "Spec corrections"):

- **The dock already adds a "–" minimize button** to docked plugin windows
  (`PluginSidePanel.Add`); it is the close button the markup docs describe.
  User decision (2026-10-05): on a window with a title bar the bar's X
  replaces it, with the tooltip "Close (reopen from the dock)"; windows
  without a bar keep "–".
- The close button is a 19-point square (the bar below the Classic top
  border), at the frame's right inset; modern themes centre it in the band.
  Classic art is `0x06004D0C`, the retail inventory close button's image.
- The eager first measurement (3.4) moves to PR 3: until `layout` exists
  every root states its `w`/`h`, so no measure pass is needed.
- `CloseRequested` is on `PluginTitleBar`; `MarkupDocument.BuildWindow`
  returns a `MarkupWindow` whose `Register` wires it to
  `RetailWindowManager.Close` after registration. Public `Build` is
  unchanged.
- The authored-inputs revision is FNV-1a (process-stable); the ellipsis is
  ASCII `...`.

## Planning corrections (PR 3)

Found while prototyping the markup PR (see the PR 3 plan's "Spec corrections"):

- **Changes are detected by comparison at layout time** (replaces the
  `InvalidateMeasure()` walk and the theme-change action of 3.3). A drawn
  flex root compares each item's visibility with its node and each leaf's
  caption, font and theme with what it was measured from, and lays out only
  when something differs or its size changed. There is no invalidation API;
  hidden subtrees lay out when shown.
- **Content sizes are measured centrally** in `MarkupContentSize` instead of
  an `IUiContentSize` interface on each control (3.2). Constants: control
  height `max(24, line + 8)`, button padding 12 a side, toggle
  `max(20, line + 4)` tall with its caption at 17 (Classic) or 35 (themed);
  the rest as in the 3.2 table.
- **The window minimum is fitted** (settles the 3.1 known gaps):
  `FlexFit.Minimum` grows the measured minimum until the content laid out at
  it fits, on each non-scrolling axis.
- **`wrap` is rows only in v1**: `wrap="true"` on a column is a build error
  (settles the 3.1 one-direction limitation).
- **A changed minimum is applied on the next tick**, outside the draw,
  through `EnforceMinimumSize` once the window is registered.
- **An authored `w`/`h` below the content minimum opens at the minimum**;
  authored `minw`/`minh` still win over the content minimum.
- **A label in flex centres its line vertically** in its rect.
- **A layout root without the bar** is inset by the 5-point border on every
  side. Its `w`, `h`, `minw` and `minh` are parsed strictly; roots without
  `layout` keep lenient parsing.
- **No theme case is needed for `UiFlexGroup`**: groups have none.
- **FlexDemo** ships a finder (window 1) and a settings form (window 3,
  without scrolling); the scrolling grid comes with PR 4.
