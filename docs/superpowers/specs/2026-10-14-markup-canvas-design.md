# Markup canvas: a plugin canvas inside a markup panel

**Date:** 2026-10-14
**Gap:** Aethrix `docs/api-gaps.md` #62 ("Markup has no canvas element, so a panel can't hold a drawing")
**Branch:** `feat/markup-canvas` (from fork `main`), fork-only for now
**Contract:** `0.1.25-fork.4` → `0.1.25-fork.5`

## Problem

A markup panel is built only from markup elements. Lines and filled shapes come only from a plugin canvas
(`IPluginPainter`), and a canvas is always its own rectangle on screen, anchored to the viewport, never part of a
panel. A plugin that wants a drawing inside a page (Aethrix: a static dungeon map on Navigation › Delve) has no way to
put one there.

## Decision

A plugin registers a canvas exactly as today, marked `InMarkup = true`, and a `<canvas id="...">` element in its
markup hosts it. The plugin keeps the ordinary `IPluginCanvas` handle, so paint code, invalidation, pointer and
keyboard input written for a HUD canvas work unchanged. The panel's layout owns where the canvas sits and how big it
is.

Rejected: a binding-style element (`onpaint="{Paint}" onpointer="{...}" repaint="{Revision}"`). It fits the panel
binding model but duplicates invalidation and input, and paint code written for `IPluginCanvas` can't be reused.

Out of scope: the Aethrix Delve page itself (a separate task once this is on fork `main`).

## Plugin-facing behaviour

```csharp
IPluginCanvas map = ui.RegisterCanvas(
    new PluginCanvasDescriptor("dungeon-map", 200, 200) { InMarkup = true, AcceptsPointerInput = true },
    Paint);
```

```xml
<canvas id="dungeon-map" grow="1" />
<canvas id="minimap" w="160" h="160" />
```

### Contract

- `PluginCanvasDescriptor.InMarkup` (`bool`, init, default `false`). XML docs describe everything below.
- No other contract member changes. Contract version goes to `0.1.25-fork.5`.

### Size and placement

- The `<canvas>` element is a flex leaf. It measures as its `w` and `h` attributes (0 when absent), and takes every
  flex item attribute (`grow`, `shrink`, `basis`, `min*`/`max*`, `alignself`). It also works outside flex with plain
  `w`/`h` and the common attributes (`anchor`, `visible`, `tooltip`, `name`/`id`).
- The hosted canvas fills the element. Its size is the element's laid-out size rounded to whole pixels;
  `IPluginCanvas.Width/Height` and `painter.Width/Height` report it.
- A change of laid-out size repaints the canvas in the same draw, as `TryResize` does today.
- The descriptor's `Width`/`Height` are the canvas size only until it is first laid out in a `<canvas>`.
- While the element's laid-out size is under 1×1 the canvas keeps its last size and is not drawn.

### Members that don't apply

- `TryResize` returns `false`.
- `Anchor`, `Offset`, `Layer` and `ZOrder` are kept but ignored.

### Visibility

- The canvas is drawn and takes input only when `IsVisible` is true **and** the element is shown: its window is open,
  its tab is selected, no ancestor or the element itself is `visible="false"`.
- `IsAvailable` is true only while a `<canvas>` hosts the canvas (plus the existing rules: not dropped, not disposed).

### Pairing

- Pairing is by the plugin's own canvas id; another plugin's canvas ids are never matched.
- Either side may come first. An `InMarkup` canvas with no `<canvas>` for it mounts nothing and logs once that it is
  waiting. A `<canvas>` with no `InMarkup` canvas for it draws nothing and takes no input.
- A `<canvas>` whose id names a canvas registered without `InMarkup` draws nothing and logs once.
- The same `<canvas>` id twice in one window is a markup error (`FormatException`, the window fails to mount as other
  markup errors do). If two windows of the same plugin use the same id, the first mounted hosts the canvas; the other
  draws nothing and logs once, and takes over if the first one goes away.
- `<canvas>` without an `id` (or `name`) is a markup error.

### Lifetime

- Window torn down: the canvas stays registered and waits for another `<canvas>`; its handle keeps working
  (`IsAvailable` goes false).
- Canvas disposed: its element is left empty.
- An `InMarkup` canvas counts against the 8-canvases-per-plugin limit.

### Input

- Pointer, wheel, keyboard focus, `ReleasePointer` and outside presses behave as for any canvas, with coordinates in the
  canvas's own pixels.
- Inside a scroll group, drawing and hit-testing are clipped and offset by the existing scroll code; pointer
  coordinates stay canvas-local.
- A wheel the canvas doesn't take bubbles on to the scroll group, as today.
- A press on the canvas doesn't drag the window (`CapturesPointerDrag`, as today).

## Implementation

### Contract (`src/AcDream.Plugin.Abstractions`)

- `PluginCanvas.cs`: `InMarkup` on `PluginCanvasDescriptor`, with XML docs.
- `AcDream.Plugin.Abstractions.csproj`: `ForkContractVersion` → `0.1.25-fork.5`.

### Markup (`src/AcDream.App/UI`)

- `MarkupDocument.AddElement`: a `canvas` case builds a new `UiMarkupCanvas`, then `ApplyCommon`, theme and flex
  registration like every other element. It requires an id and rejects a duplicate id in the same window.
- `MarkupContentSize.Measure`: a `UiMarkupCanvas` measures as its authored `w`/`h`.
- `UiMarkupCanvas` (new): an empty `UiElement` holding at most one hosted child. It exposes its canvas id and an
  attach/detach pair used by the pairing table.
- `MarkupWindow` (the build result) lists the window's `UiMarkupCanvas` elements so mounting can register them.

### Canvas element (`src/AcDream.App/UI/Layout/PluginCanvasElement.cs`)

- A hosted mode, chosen when the element is created for an `InMarkup` registration. In it, `Layout()` sets
  `Left = Top = 0` and `Width/Height` to the parent's size rounded to whole pixels. When that is at least 1×1 and
  differs from the registration's size, it writes it into the registration through a new internal
  `PluginCanvasRegistration.SetLaidOutSize`, so the existing `FollowSize` reallocates targets and invalidates.
- The visibility read also includes the host element's effective visibility. Hit-testing and keyboard focus use that
  combined answer, so a hidden tab takes no input.
- Wheel position: the element already works it out from `ScreenPosition`, which accounts for scroll offsets. A test
  confirms it.

### Registration (`src/AcDream.App/Plugins`)

- `PluginCanvasRegistration`:
  - `TryResize` returns `false` when `InMarkup`.
  - New internal `SetLaidOutSize(width, height)`: the same write as `TryResize`, minus that check.
  - `IsMounted` reflects being hosted.
- `BufferedUiRegistry.RegisterCanvas`: no change in limits; `InMarkup` registrations drain as usual.

### Pairing (`src/AcDream.App/UI`)

- `PluginMarkupCanvasSlots` (new, owned by `RetailUiRuntime`): a table keyed by (plugin id, canvas id).
  - Holds the waiting `InMarkup` registrations and the `UiMarkupCanvas` elements of mounted windows.
  - Pairs them whenever both sides exist: it creates a hosted `PluginCanvasElement`, attaches it to the element, and
    completes the registration's mount with a teardown that detaches it.
  - On window teardown it detaches the window's canvases and returns their registrations to waiting (re-pairing with
    another window's element of the same id if one exists).
  - On canvas dispose its teardown detaches the element and forgets the registration.
- `RetailUiRuntime.MountPluginCanvases`: `InMarkup` registrations go to the table instead of a `PluginCanvasStack`.
- `RetailUiRuntime.MountPlugins` and plugin window teardown: register and unregister the window's `<canvas>`
  elements.
- `RetailUiRuntime.ReportPressToCanvases`: also reports to hosted canvases that are shown.

### Hosts without a UI

- Scoped and headless hosts pass descriptors through unchanged. Tests confirm `InMarkup` round-trips and that a
  headless `RegisterCanvas` with `InMarkup` still returns the inert canvas.

## Tests

New `tests/AcDream.App.Tests/UI/MarkupCanvasTests.cs`, in the style of `MarkupFlexTests` plus the
`PluginCanvasElementTests` harness (recording GPU device, buffered registry, canvas surface):

- A `grow="1"` canvas in a sized window paints at the laid-out size (`painter.Width/Height`).
- A window resize changes the size and repaints once.
- `w`/`h` without flex.
- Registering the canvas before the window mounts, and after it.
- Hidden tab: no paint, no hit. Selecting the tab paints.
- `IsVisible = false` hides it inside a shown window.
- Pointer coordinates are canvas-local inside a scrolled group; a wheel the canvas doesn't take scrolls the group.
- Outside presses reach a shown hosted canvas and not a hidden one.
- Window teardown → `IsAvailable` false; remount → paints again.
- Dispose empties the element.
- Duplicate id in one window → `FormatException`; missing id → `FormatException`.
- The same id in two windows: the first hosts it; it moves to the second when the first goes away.
- A `<canvas>` naming a non-`InMarkup` canvas draws nothing.
- `TryResize` returns `false`.

Additions elsewhere:

- `MarkupContentSizeTests`: canvas measurement.
- Contract tests: `InMarkup` defaults to `false` and round-trips.
- `ScopedUiRegistryCanvasTests`, `HeadlessPluginApiSurfaceTests` and `ContractParityTests` as needed.

Gate: the full test suite as the existing gate runs it (`dotnet test` with the `~/.dotnet` SDK), with no new failures
beyond the known pre-existing ones. A live check with a scratch probe plugin on the local ACE server: a `<canvas>` in a
themed flex window with a tab and a scroll group. It should paint, resize with the window, take pointer input, hide
with its tab, and look right at 2× (Retina).

## Docs

- `docs/plugin-ui-markup.md`:
  - The elements table gets a `<canvas>` row.
  - A new `<canvas>` section covers pairing, sizing, visibility and lifetime.
  - The canvas tests are listed under Tests.
- `docs/plugin-api.md`: a "Canvases in markup" subsection under Canvases, plus a note on `TryResize`.

## Delivery

1. `feat/markup-canvas` from fork `main`; merge into fork `main` with a merge commit; push to origin.
2. Then update Aethrix `docs/api-gaps.md` #62 to **Resolved**, in the style of the other resolved entries. Include:
   - the fork `main` merge commit and the feature commit
   - contract `0.1.25-fork.5` as the minimum package version (Aethrix pins `0.1.25-fork.4` in `Directory.Build.props`
     today and must bump)
   - the descriptor flag and the markup element, with a short usage example
   - what doesn't apply (`TryResize`, `Anchor`/`Offset`/`Layer`/`ZOrder`)
   - the visibility, pairing and lifetime rules a plugin author needs
3. No upstream PR for now (fork-only by choice; the feature needs only `w`/`h`, so it is upstream-portable later).
