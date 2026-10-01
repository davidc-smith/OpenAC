# Modern plugin theme — design

Date: 2026-10-01. Status: approved direction, awaiting spec review.

## Goal

Make plugin windows that opt in with `<panel theme="plugin">` look like a
current desktop app instead of a 1990s dialog, when the player picks
**Charcoal + moss** or **Warm graphite + brass**. The chosen direction is
"A · Soft modern" (rounded corners, a header band, soft shadow, segmented-style
tabs, real hover/pressed/focus states) with "C"'s switch-style toggles.

## Constraints

- **Classic is untouched.** Every change sits behind the existing
  "palette is not null" branches. Existing Classic tests pass unchanged and
  Classic draw output is identical.
- **No plugin API change.** No new markup elements or attributes, no new
  members in `AcDream.Plugin.Abstractions`. Authored layout (x/y/w/h, anchors)
  means exactly what it means today.
- **Live theme switching keeps working.** A window open while the player
  switches theme redraws correctly with no rebuild, as today.
- **Fork first, upstream later.** Keep Erik's structure (`PluginUiPalette`,
  `PluginUiThemeSettings`, `PluginMarkupTheme`, `UiPluginMarkupPanel`) so the
  work can be offered upstream with few conflicts.

## Non-goals (this pass)

- Tooltips (shared with retail, built from a dat layout).
- A "primary" button variant. It needs a markup attribute
  (`variant="primary"`), which is an API change; see Follow-ups.
- Layout containers, themes as data files, new palettes.
- A segmented-control track behind a row of tabs. Tabs are independent
  elements in markup; each tab draws its own pill.

## Look: the numbers

All lengths are in interface points. Colours come from the active palette;
two are derived so `PluginUiPalette` needs no new fields:

- `Hover` = `Field` mixed 12% toward `Border`.
- `Pressed` = `Field` mixed 20% toward `Background`.
- Focus glow = `Accent` at 18% alpha.

| Piece | Radius | Fill | Edge | States |
|---|---|---|---|---|
| Window | 10 | `Background` | 1px `Border` (rounded) | soft shadow, see Window chrome |
| Header band | top corners 10 | vertical gradient: `Background` mixed 6% toward `Text` at the top, 2% at the bottom | 1px separator line `Border` at y=24 | — |
| Button | 6 | `Field` | 1px `Border` | hover: `Hover` fill, `Border` lightened 15%; pressed: `Pressed`; disabled: 45% alpha; focus: ring |
| Tab | 6 | none | none | selected: `Selected` fill, `Text` colour; unselected: `Muted` text; hover: `Hover` fill |
| Field | 6 | `Field` | 1px `Border` | focused: 1px `Accent` edge + 3px focus glow |
| Toggle (switch) | full (pill) 26×14 | off: `Field`; on: `Accent` at 35% | 1px `Border` / `Accent` | knob 8px circle, `Muted` off / `Text` on, slides left↔right |
| List check column | 4, 11×11 | off: `Field`; on: `Accent` | 1px `Border` / `Accent` | on: two-stroke tick in `Field` |
| Slider | track 2, 4px tall | `Field`; filled part `Accent` | — | thumb 14px circle `Text` with small shadow; hover/drag: 2px `Accent` ring |
| Scrollbar (list, log, menu popup) | full | track: none | — | thumb 4px wide, `Muted`; hover/drag: 6px wide, `Text` at 70%; no arrow ticks |
| Menu face | 6 | as Button | as Button | chevron (two 1.5px strokes) in `Muted` |
| Menu popup | 8 | `Field` | 1px `Border` | rows inset 3, radius 5; hover `Hover`, selected `Selected`; soft shadow |
| List / Log container | 8 | `Field` | 1px `Border` | rows inset 3, radius 5; selection `Selected` |
| Meter (no sprite art) | 3 | track `Field` | — | bar in its authored colour, rounded |
| Focus ring (any control) | control radius + 2 | — | 2px `Accent` at 60% | drawn 2px outside the control |

A meter with authored sprite art (`backleft`…`frontright`) keeps that art.

## Architecture

### 1. Rendering basics

`UiRenderContext` (internal, additive):

- `PixelScale` (float, default 1), set by `Begin(screenSize, font, pixelScale)`.
  `UiHost.Draw` passes `CanvasPixelScale.ForInterface(framebufferPerPoint)`,
  read from the same window-size/framebuffer-size source the plugin canvas
  host services use (`InteractionRetainedUiComposition`). Callers that pass
  nothing get 1.
- `FillRoundedRect(x, y, w, h, CanvasCornerRadii, color)`,
  `StrokeRoundedRect(..., thickness)`, `FillEllipse(...)`,
  `StrokeEllipse(...)`: build triangles with `CanvasGeometry` using
  `pixel = 1 / PixelScale` and draw them with the existing `DrawTriangles`,
  which already transforms, clips and applies alpha.
- `FillVerticalGradient(x, y, w, h, CanvasCornerRadii, top, bottom)`: the
  rounded fill with per-vertex colour by y.
- `DrawSoftShadow(x, y, w, h, radius, spread, color)`: six stacked rounded
  fills, each grown by `spread / 6` and fading, drawn inside
  `PushClipUnbounded()` because elements clip to their own bounds by default
  (`UiElement.ClipsChildren` is true).

`PluginUiStyle` (new, `src/AcDream.App/UI/PluginUiStyle.cs`): a static class
of `(UiRenderContext ctx, PluginUiPalette p, ...)` helpers holding the table
above. It owns every modern shape, so controls only choose a state:
`WindowShadow`, `WindowSurface`, `Header`, `Button(state)`, `Tab(selected,
state)`, `Field(focused)`, `Switch(on, enabled)`, `Check(on)`,
`SliderTrack`, `SliderThumb(state)`, `ScrollThumb(state)`, `MenuFace(state,
open)`, `Chevron`, `Container`, `RowHighlight`, `MeterTrack/Bar`,
`FocusRing(radius)`, plus `Hover(p)`/`Pressed(p)`/`FocusGlow(p)`.

`PluginUiPalette.DrawCheck` keeps its signature and delegates to
`PluginUiStyle.Check`.

`UiPointerState` (new, small struct): tracks `Hovered`, `Pressed` from
`HoverEnter`/`HoverLeave`/`MouseDown`/`MouseUp`/`MouseMove`, following
`UiButton`'s pattern. It only observes: a control calls
`_pointer.Observe(in e)` at the top of `OnEvent` and then runs its existing
logic, so input behaviour and return values do not change. `UiRoot` already
sends `HoverEnter`/`HoverLeave` to whichever element is under the pointer and
`MouseDown`/`MouseUp` to the pressed element, so no opt-in is needed; no
themed state depends on `MouseMove`.

### 2. Controls

Each control's existing themed branch calls `PluginUiStyle` instead of
`DrawFill`/`DrawRectOutline`. The Classic branch is not edited.

| Control | File | Change |
|---|---|---|
| Button | `UiPanel.cs` (`UiSimpleButton`) | add `ThemePalette`; when set: `PluginUiStyle.Button(state)`, themed focus ring, caption unchanged |
| Tab | `UiMarkupTabButton.cs` | pill per tab replaces the underline |
| Toggle | `UiMarkupToggle.cs` | `Switch` replaces the square check; caption moves to x=34 when themed (26px switch + 8 gap); themed focus ring |
| Field | `UiField.cs` | add `ThemePalette`; rounded field, focus edge and glow from `IsFocused`; caret and selection unchanged |
| Menu | `UiMenu.cs` | add `ThemePalette`; plain-mode face, popup, rows, search band and popup scrollbar draw through the style when set |
| List | `UiMarkupList.cs` | container, inset rounded rows, `Check`, slim scroll thumb |
| Log | `UiMarkupLog.cs` | container and slim scroll thumb |
| Slider / scrollbar | `UiScrollbar.cs` | add `ThemePalette`; plain mode draws track/thumb through the style when set |
| Meter | `UiMeter.cs` | add `ThemePalette`; only when no sprite art is authored |
| Label | — | font only (see Fonts) |

`PluginMarkupTheme.Register` sets each new `ThemePalette` alongside the
colour bindings it already makes, and registers `UiMeter` (new case).

### 3. Window chrome and the plugin shelf

`UiPluginMarkupPanel` (themed path only):

- `OnDraw`: `WindowShadow` (12pt spread, black at 45%, offset 4pt down),
  `WindowSurface` (radius 10), then `Header` over the top 24pt when the panel
  has a title. The header is background only: authored children draw over
  it, so nothing authored can be hidden.
- Title label: SemiBold font, `Top = (24 - LineHeight) / 2`, `Left = 12`
  when themed; Classic keeps `Left = 8, Top = 4`. Applied as a theme action
  so live switching moves it back.
- `OnDrawAfterChildren`: rounded 1px `Border` edge; resize grip becomes three
  2pt `Muted` dots.
- Authored children with square backgrounds flush to the window corner will
  show square corners inside the rounded window. Accepted for this pass.

Plugin shelf (`PluginSidePanel.cs`), themed path only:

- Shelf surface: radius 10, soft shadow, rounded `Border` edge.
- Entry buttons (`PluginShelfButton`): radius 6; visible → `Selected` fill +
  `Accent` edge; hidden → `Field`; hover → `Hover`. Icons unchanged.
- Minimize button added to each plugin window (`PluginMinimizeButton`): when
  the window is a `UiPluginMarkupPanel` and a palette is set, a ghost button
  (no fill, `Muted` "–", `Hover` fill radius 4 on hover) sitting in the
  header. Non-opted-in windows keep today's button.

### 4. Fonts

- Bundle `assets/fonts/NotoSans/NotoSans-SemiBold.ttf`: unmodified hinted
  TrueType from the same pinned Noto commit as Regular
  (`ffebf8c1ee449e544955a7e813c54f9b73848eac`), SHA-256
  `87a8b90ece1e89746b544e4e086f85a3710e41485a8078f9be874837dfad45d5`,
  572,924 bytes, SIL OFL 1.1. Update `assets/fonts/NotoSans/README.md` and
  `NOTICE.md`; embed as `AcDream.App.Fonts.NotoSans-SemiBold.ttf`.
- `BundledUiFont.Load(textures, pixelHeight, weight)` with
  `BundledUiFontWeight { Regular, SemiBold }`; Regular stays the default.
- `PluginUiThemeSettings` gains `ModernTitleFont` (SemiBold 16) next to
  `ModernFont`. Used for themed window titles only.
- **Sharp text on Retina.** `UiDatFont` gains an optional sharp companion:
  the same font baked at 2× (`pixelHeight` 32) plus its scale. When
  `ctx.PixelScale >= 1.5` and a companion exists, `DrawStringDatPass` takes
  each glyph's quad from the companion (size and offsets ÷ 2, snapped to
  device pixels) while the pen advances by the 1× metrics. `MeasureWidth`,
  `LineHeight` and every layout number stay the 1× values, so layout is the
  same on every display. Both bundled weights bake their 2× companion when
  loaded. Scales above 2 use the 2× bake. Dat fonts never get a companion,
  so Classic text is unchanged.

## Testing

- **Classic:** all existing tests pass unchanged, in particular
  `PluginUiThemeTests`, `PluginSidePanelThemeTests`, `MarkupDocument*Tests`,
  `UiMenu*Tests`, `UiScrollbarTests`, `UiMarkupList*Tests`. Add one
  regression test that builds a themed window, switches to Classic, and
  checks the recorded draw calls match a never-themed build of the same
  markup.
- **Unit:** `UiRenderContext` rounded/ellipse/gradient methods emit clipped
  triangles at the current transform and honour `PixelScale` (fringe
  width); `DrawSoftShadow` escapes the element clip; derived colours;
  `UiPointerState` transitions (enter, leave, press, drag out, release);
  `BundledUiFont` SemiBold bakes; a sharp companion leaves `MeasureWidth`
  and `LineHeight` identical and changes only glyph quads at scale 2.
- **Per control:** using `RecordingGpuDevice` (as `UiRectOutlinePainterOrderTests`
  does), each themed control draws rounded triangles instead of rectangles,
  and the hover/pressed/focused states change fill colour.
- **Live switching:** extend `OptedInWindowSwitchesAndRestoresClassic…` to
  cover the new `ThemePalette` properties (button, field, menu, scrollbar,
  meter).
- **Visual gate:** a markup-only sample plugin,
  `samples/AcDream.Plugins.ThemeGallery`, with one window using every
  control (title, tabs, label, field, menu, toggles, slider, list with check
  and icon columns, log, meter, buttons). Capture it and the Plugin appearance
  dialog in Classic, Moss and Brass at 1× and 2× with the probe-script
  workflow, and compare against the chosen mockup.

## Delivery

- Branch `modern-theme/controls` from fork `main`. It depends on painter-v2
  shapes (`CanvasGeometry`) and hidpi (`CanvasPixelScale`), which are not
  upstream yet, so an upstream PR must follow those.
- Branch `modern-theme/sharp-text` (SemiBold + 2× companion + `PixelScale`
  plumbing) can be split out as its own PR if it is ready first; the controls
  branch then builds on it.
- Merge into fork `main` with merge commits, as for painter-v2. This spec and
  its plan stay on the fork-only docs branch `docs/modern-theme-spec`.
- Update `docs/plugin-ui-markup.md` ("Shared plugin appearance") to describe
  the new look and the gallery sample.

## Follow-ups (not in this pass)

- `variant="primary"` on `<button>` for the filled accent button in the
  mockup (small, additive markup API).
- Themed tooltips.
- Soft-clipping authored children to the window's rounded corners.
