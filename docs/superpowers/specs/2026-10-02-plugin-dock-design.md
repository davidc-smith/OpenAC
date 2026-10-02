# Plugin dock — design

Date: 2026-10-02. Status: approved design, awaiting spec review.

## Goal

Make the plugin dock (`PluginSidePanel`, window name `plugin-shelf`) look
designed instead of thrown together, in all three plugin themes. The chosen
direction is "A · Floating dock" from the dock concepts page
(https://claude.ai/artifact/NZZBrxxAwnrw1CHUM1Sfhe): one soft rounded pill
with roomy slots, an open dot, a hover well and a hover label. It also gets
an **edge rail mode** that locks it to the left or right screen edge (concept
B's look).

"Dock" is the user-facing name from now on. Code names (`PluginSidePanel`,
`PluginShelfButton`, `WindowNames.PluginShelf`) stay as they are, so saved
layouts and upstream merges are not disturbed.

## Why it looks cheap today

- Moss/Brass: 22pt tiles 2pt apart with 1pt padding, so the panel reads as a
  frame around buttons. Classic: 28pt tiles with 4pt padding that wrap into
  columns.
- Open and closed differ only by fill and border colour.
- A plugin without an icon gets one letter (Moss/Brass) or up to three
  (Classic).
- The grip dashes, the 3-pixel chevron and the tile outlines are 1-pixel
  `DrawFill` calls. The dock uses almost none of the painter-v2 shapes.
- Appearance settings and scrolling are only discoverable through the
  tooltip.

## Decisions (2026-10-02)

1. Direction A, plus an edge rail mode.
2. The rail locks to the **left or right** edge only. Top and bottom are
   crowded by the retail toolbar, chat and hotbar, and would need a second
   layout.
3. Switching uses **a setting plus snap**: Plugin appearance gets a Dock
   menu, and dragging the dock to an edge also snaps it into rail mode.
4. In rail mode the rail **slides along its edge**, and its height is saved.
5. **Classic gets the new shape** in its own black-and-gold colours. This
   reverses the modern-theme rule that Classic stays byte-identical, for the
   dock only. Every other Classic window is unchanged.
6. A **gear slot** at the bottom opens Plugin appearance. Right-click on the
   dock keeps working.
7. The icon seam is shared with the SVG icons spec
   (`2026-10-02-svg-plugin-icons-design.md`): the dock draws every icon
   through one `DrawIcon(ctx, x, y, extent, colour)` method and never
   hard-codes the PNG path.

## Constraints

- **No plugin API change.** `PluginPanelDescriptor` and `IUiRegistry` are
  untouched. SVG icons add their own property in their own PR.
- **Public surface of `PluginSidePanel` stays the same**: `Add`, `Show`,
  `Hide`, `EntryCount`, `Dispose`, the state controller interfaces, the
  minimize button injected into each plugin window, the window-reachability
  clamp, right-click for appearance.
- **Saved layouts still load.** `RetainedWindowState.Collapsed` and
  `RequestedVisible` keep their meaning. A saved floating position restores
  as before. A settings file without a dock mode means Floating.
- **Live theme and mode switching** redraw with no rebuild, as theme
  switching does today.

## Non-goals

- Grouping a plugin's windows under one icon (concept D). It can come later
  on top of this layout.
- Top and bottom edges, a horizontal layout.
- Themed tooltips in general. The dock's hover label is its own element. Its
  drawing lives in `PluginUiStyle`, so themed tooltips can reuse it later.
- SVG icons (their own spec and PR).
- Dragging slots to reorder them.

## Colours

Moss and Brass use their `PluginUiPalette`. Classic gets a dock-only palette,
`PluginUiPalette.ClassicDock`, built from today's Classic dock colours, so all
three themes go through one drawing path:

| Token | Classic dock | Taken from |
|---|---|---|
| Background | `#000000` at 88% | today's dock fill |
| Field | `#060605` | `HiddenBackground` |
| Border | `#9E7A29` | today's dock border |
| Text | `#F0EBDD` | — |
| Muted | `#A8925C` | — |
| Accent | `#DBB852` | `ToggleGlyphColor` |
| Selected | `#17300E` | `VisibleBackground` |

`ClassicDock` is internal to the App. It is not one of the selectable themes,
and `PluginUiThemeSettings.Palette` still returns null for Classic, so no
other Classic window changes. The dock asks for `DockPalette`, which is the
theme palette or `ClassicDock`.

**Fonts:** Moss/Brass labels and monograms use `ModernFont` and
`ModernTitleFont` (the sharp Noto twin). Classic uses the dock's DAT font.

## Look: the numbers

All lengths are in interface points. Every shape is a painter-v2 call:
`FillRoundedRect`, `StrokeRoundedRect`, `DrawSoftShadow`,
`FillVerticalGradient`, `FillEllipse`, `DrawSmoothLine`.

**Floating dock**

| Piece | Size | Drawing |
|---|---|---|
| Body | 48 wide (6 padding + 36 slot + 6) | radius 14; `Background`; 1px rim `Border` mixed 8% toward `Text`; top sheen: vertical gradient from `Text` at 4% to transparent over the top 12pt; soft shadow offset 4, blur 14, black 42% |
| Handle row | 12 tall, above the first slot | three 2pt dots 3pt apart in `Muted`, centred in the left 30pt; shown only while the pointer is over the dock; 50% alpha while the UI is locked |
| Collapse button | 12×12 at the right of the handle row | ghost button, chevron `Muted`; hover-only like the dots |
| Slot | 36×36, 4 apart | no fill when idle |
| Slot well | slot rect, radius 9 | hover: `Hover(Field)`; pressed: `Pressed(Hover(Field))` |
| Artwork | 24×24, centred | see Icons |
| Open dot | 3pt circle, 1.5pt in from the dock's side nearer the screen edge (left in the left half of the screen, right in the right half), centred on the slot | `Accent` |
| Divider | 1pt line inset 8 each side, 4pt space above and below | `Border` |
| Gear slot | a normal slot after a divider | gear drawn with smooth lines and an ellipse in `Muted`; hover `Text` |

**Edge rail** (left edge shown; right is mirrored)

| Piece | Size | Drawing |
|---|---|---|
| Body | 46 wide, flush with the edge | corners: 0 on the edge side, 12 on the other; no rim on the edge side; shadow offset sideways away from the edge |
| Slot | 46×38 | well inset 1pt top/bottom, 5pt sides |
| Edge pill | 3pt wide, on the edge, radius 3 on the inner side | height 0 closed, 4 on hover of a closed slot (`Muted`), 8 open, 20 for the plugin window in front (`Accent`); height eases over 120ms (instant with reduced motion) |
| Handle row, divider, gear | as floating | — |

The open dot is not drawn in rail mode; the edge pill replaces it.

**Front window:** of the dock's visible windows, the one highest in the
root's child order (the order `BringToFront` maintains). Worked out each
tick; the dock holds at most a few dozen entries.

**Collapsed**

- Floating: a 48×24 pill with the handle dots and an expand chevron.
- Rail: an 8pt-wide, 40pt-tall tab on the edge with a chevron pointing
  inward. The tab is also the drag handle.
- Clicking the chevron (floating) or the tab (rail) expands the dock.

**Hover label**

- Appears with no delay beside the hovered slot, 8pt from the dock, on the
  side away from the screen edge: right of a left rail or a floating dock in
  the left half of the screen, left of a right rail or a floating dock in the
  right half.
- Padding 5×9, radius 7, `Background`, 1px `Border`, soft shadow offset 3,
  blur 8, black 35%.
- Line 1: the window title in the title font, `Text`. Line 2: the plugin's
  display name in `Muted`, left out when it equals the title. The gear's
  label is "Plugin appearance".
- It is a separate, non-hit-testable element the dock adds to its parent,
  raised above windows while it shows, so the dock's own clip does not cut
  it off. Slots no longer return tooltip text.

**Monograms** (no icon file, no DAT surface)

- Two letters, from `IconText` or the title, using today's `Initials` rule
  but capped at two characters in every theme.
- A rounded square, radius 6, filling the 24pt artwork box, with a vertical
  gradient: the base colour mixed 12% toward white at the top, base at the
  bottom. Letters `#F1ECE2`.
- The base colour is one of eight fixed muted hues, chosen by FNV-1a of the
  plugin id modulo 8, so it never changes between sessions:
  `#6E8B5E #5E7F8B #8B6E5E #7A6A99 #8B8259 #5E6E8B #8B5E74 #5E8B7A`.
  They read on all three dock backgrounds.

**Icons**

- `PluginShelfButton.DrawIcon(ctx, x, y, extent, colour)` is the only place
  icons are drawn. Today it draws the PNG or DAT sprite, or the monogram.
  The SVG PR adds coverage icons behind the same method.
- RGBA art (PNG, DAT) ignores `colour`'s hue: drawn at 85% alpha when
  closed, 100% on hover or open.
- Coverage art (SVG, later) is tinted: closed `Muted`, hover `Text`, open
  `Accent`.

**Dividers** go between consecutive entries whose `PluginUiOwner.Id`
differs. Entries keep their mount order, so a plugin's windows sit together.

**Overflow**

- The dock grows to fit its slots, up to the parent's height below its top
  minus 8pt.
- Beyond that it scrolls by one slot per wheel step. The handle row and gear
  stay put; only the slot list scrolls.
- A 12pt fade (vertical gradient from transparent to `Background`) marks a
  clipped end.
- Classic's column wrapping is removed. Every theme scrolls.

## Dock modes

`PluginDockMode { Floating, Left, Right }`, saved in the `pluginUi` settings
section beside the theme: `{ "theme": "Moss", "dock": "left" }`. A missing or
unknown value is Floating.

- `PluginUiThemeSettings` gains `Dock` (get/set, persisted like `Theme`).
- `SettingsStore` replaces `SavePluginUiTheme` with one `SavePluginUi(theme,
  dock)` that writes the whole section. Today's method writes only the theme,
  so it would wipe the dock mode. Loading reads both.
- **Plugin appearance** gets a second menu under the theme:
  `Dock: Floating / Left edge / Right edge`. The window grows to fit.

**Changing mode from the menu**

- Left: the dock moves to x = 0 and keeps its top.
- Right: the dock moves to x = parent width − dock width and keeps its top.
- Floating: the dock moves 10pt in from the edge it was on and keeps its top.

**Snap while dragging**

- `UiRoot` gets one hook: `UiElement.ConstrainWindowDrag(ref float left, ref
  float top, int pointerX, int pointerY)`, `internal virtual` and a no-op by
  default. It is called in the window-move branch of mouse move, after the
  existing clamp to the parent. Nothing else in `UiRoot` changes.
- Floating: when the dragged left edge comes within 8pt of x = 0, the dock
  becomes a left rail at once. The same happens within 8pt of the right
  edge.
- Rail: `left` is pinned to the edge, and `top` follows the pointer. When
  the pointer is more than 32pt from the edge, the dock floats again under
  the pointer.
- The mode is saved when the drag ends (the handle's `Moved` notification).
  The menu therefore always shows the current mode.

**Right rail and resizing:** in Right mode, each tick puts the dock at
parent width − dock width, so it stays attached when the game window
resizes. Left and Floating keep today's reachability clamp.

**Saved position:** layout persistence keeps saving the dock's left and top.
On restore, a rail's left is replaced by its edge, and its top is kept.
Today's private "dock" fields (`_dockLeft`, `_dockTop`,
`_initialDockApplied`), which mean the default spot, are renamed to "home"
so they don't clash with the new meaning.

## Copy

- The minimize button in plugin windows: "Minimize to the dock" (today
  "Minimize to plugin sidepanel").
- The dock body tooltip ("Right-click for plugin appearance. Scroll to browse
  plugins.") is removed. The gear and the fades replace it.
- Docs that say "sidepanel" or "shelf" to players
  (`docs/plugin-ui-markup.md`) say "dock".

## Code shape

- `PluginSidePanel.cs`: layout, modes, snap, overflow, front window, hover
  label ownership. The nested classes (`PluginShelfButton`, the minimize
  button) stay, and the grip and toggle classes are replaced by a handle row
  and a collapse button. If the file grows past about 800 lines, split the
  layout maths into `PluginDockLayout` (pure, unit-tested).
- `PluginUiStyle.cs`: `DockBody`, `DockSlotWell`, `OpenDot`, `EdgePill`,
  `DockDivider`, `DockFade`, `Monogram`, `HoverLabel`, `Gear`, `Chevron`
  sizing. The numbers above live here as constants.
- `PluginUiThemeSettings.cs`: `PluginDockMode`, `Dock`,
  `PluginUiPalette.ClassicDock`, `DockPalette`.
- `PluginAppearanceBinding.cs` and its markup in `RetailUiRuntime`: the Dock
  menu.
- `SettingsStore.cs`: `LoadPluginUi` / `SavePluginUi`.
- `UiElement.cs` and `UiRoot.cs`: the drag hook.

## Testing

- **Rewrite** the parts of `PluginSidePanelTests`,
  `PluginSidePanelThemeTests` and `PluginSidePanelToggleGlyphClipTests` that
  pin the old geometry (28/22pt tiles, column wrap, grip and toggle glyphs).
  Tests of behaviour keep their intent: entries, visibility and
  `RequestedVisible`, unregister cleanup, the minimize button, window
  reachability, persistence.
- **New:**
  - Floating sizes per entry count, and divider placement by owner id.
  - Overflow: the visible slot count, wheel scrolling, the fades.
  - Snap: within 8pt snaps to each edge. Pulling more than 32pt away
    floats. The mode is saved on drag end, not during the drag.
  - The menu sets each mode and moves the dock.
  - A right rail re-attaches after the parent resizes.
  - The mode round-trips through `SettingsStore`. Saving the theme keeps the
    dock mode and the other way round. An old file without `dock` loads as
    Floating.
  - The edge pill for closed, open and front windows. The front window
    follows `BringToFront`.
  - The monogram hue is stable for an id, and two characters at most.
  - The hover label: its side by mode and screen half, one line when the
    title equals the plugin name, never hit-testable.
  - Collapse and expand in both modes, restored from `Collapsed`.
  - `ConstrainWindowDrag` is a no-op for every other window (an existing
    drag test passes unchanged).
- **Layout persistence:** `RetailWindowLayoutPersistenceTests.Viewport`
  still passes. A saved rail restores to its edge with its top.
- **Visual gate:** 2× backbuffer captures through the retina capture
  workflow (the user drags the window to the built-in display). Cover
  Classic, Moss and Brass × floating, left rail and right rail, plus one
  collapsed and one hover-label shot. Use the ThemeGallery and CanvasDemo
  samples plus the installed plugins in a scratch root. The user judges the
  look before merge.

## Delivery

- Branch `modern-theme/dock` from fork main (16b0db10), worktree
  `.worktrees/dock`. It needs the modern theme and painter v2, which are only
  on fork main.
- Merge into fork main after review, with the user's OK before any push or
  merge.
- A later upstream PR must say that the Classic dock's look changed, that
  `UiElement` gained `ConstrainWindowDrag`, and that the `pluginUi` settings
  section gained `dock`.
- This spec and its plan stay on the fork-only docs branch
  `docs/modern-theme-spec`.
- The SVG icons PR (`plugin-icons/svg`) is independent. Whichever lands
  second calls or fills `DrawIcon`.
