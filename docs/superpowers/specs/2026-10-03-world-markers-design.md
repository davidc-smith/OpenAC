# World markers — design

Date: 2026-10-03. Status: approved; corrected while planning PR 1 (see the
PR 1 plan's "Spec corrections").

## Goal

Let a plugin draw markers in the world: icons hung over creatures and
objects (a debuff tracker's row of spell icons over a mob), icons pinned to
a spot (a waypoint, a corpse), and shapes lying on the ground (a ring under
the target, an area-of-effect disc, a facing wedge). The plugin decides what
to draw and why; the client only gives it the means to draw. How a plugin
knows a creature is debuffed is the plugin's business (its own casts, cast
sharing, chat parsing) and is not part of this spec.

## What already exists

- **World lines** (`WorldLines.cs`, upstream): `host.WorldLines.CreateLayer()`
  gives a layer of solid bars drawn in the world pass with depth, optionally
  following the terrain (`PluginWorldLineRenderer`). Route lines are done.
- **World labels** (`WorldLabels.cs`, upstream):
  `host.Automation.Labels.ShowLabels(...)` hangs text over objects on the
  interface overlay band, never occluded. `WorldLabelLayout.Place` projects
  each object's head (`WorldLabelAnchor`: base plus the host's measure of the
  object's height) through `ScreenProjection`, stacks lines upward, fades the
  last fifth of the range and sorts far to near.
- **Images** (`PluginImages.cs`, painter v2): `host.Ui.Images` hands out
  `PluginImage` handles from client art, spell icons (`FromSpellIcon`, the
  icon the spell bar draws), object icons and the plugin's own files. The
  host-side `PluginImages` is per plugin (`IUiRegistry.ImagesFor(owner)`)
  and resolves a handle to a texture with `TryResolve`.
- **Pixel scale** (painter v2 PR 4, fork only): `CanvasPixelScale` /
  `PixelScale` gives the display's density so plugin drawing stays sharp at
  2×.

Nothing today draws a plugin's image in the world, and nothing draws a
shape on the ground other than straight bars.

## Constraints

- **Additive plugin API.** New types and one new default-implemented member
  on `IUiRegistry`. A plugin built against the current contract compiles and
  loads unchanged.
- **Never fail a plugin.** Bad entries are dropped; an oversized set is
  refused and reported through the return value. Nothing throws on bad data.
- **Bounded per frame.** A plugin cannot make a frame arbitrarily expensive:
  caps per plugin, a draw range for shapes, a segment budget per shape, and
  the shared vertex budget of the world pass.
- **Fork first.** Branches start from fork main and use `PixelScale`
  directly (user's choice). An upstream PR would follow painter-v2/hidpi.
  This spec stays on the fork-only docs branch.

## Non-goals

- Pointer input on markers (hover, click, tooltips).
- Icons hidden behind walls or hills. Icons are always on top, like labels.
- Edge-of-screen arrows pointing at off-screen markers.
- Text badges or timer sweeps on icons. A plugin can pair an icon with a
  label for text.
- Filled polygons, and transparency or dashes on world lines.
- Any knowledge of other creatures' enchantments in the client.

## API

All types in `AcDream.Plugin.Abstractions`, new file `WorldMarkers.cs`.

```csharp
IPluginWorldMarkerLayer? layer = host.Ui.WorldMarkers.CreateLayer(); // null without a window

layer.SetIcons([
    new PluginWorldIcon(PluginMarkerAnchor.Object(mobId), images.FromSpellIcon(vulnId))
        { Border = new PluginColor(220, 60, 60) },
    new PluginWorldIcon(PluginMarkerAnchor.At(position), waypointPng)
        { SizePixels = 32, MaxRange = 200f },
]);

layer.SetShapes([
    PluginGroundShape.Ring(PluginMarkerAnchor.Object(targetId), radius: 2f, width: 0.15f, color),
    PluginGroundShape.Disc(PluginMarkerAnchor.At(aoeCentre), radius: 6f, new PluginColor(255, 80, 0, 90)),
    PluginGroundShape.Arc(PluginMarkerAnchor.Object(mobId), radius: 5f,
        startDegrees: -45f, sweepDegrees: 90f, filled: true, color) { FacesObject = true },
]);
```

### Anchors

`PluginMarkerAnchor` is a readonly struct with two factories:

- `Object(uint objectId)` follows an object the client holds, by server id.
  While the client does not hold it the marker is simply not drawn, as with
  labels.
- `At(PluginNavigationPosition position)` pins to a world position, the same
  type world lines use. Its `IsOutdoor` decides whether a shape follows the
  terrain (see Rendering).

### Icons

```csharp
public readonly record struct PluginWorldIcon(PluginMarkerAnchor Anchor, PluginImage Image)
{
    public float SizePixels { get; init; } = 24f;       // interface points; finite values clamped 8..128
    public PluginColor Tint { get; init; } = PluginColor.White; // multiply, alpha is opacity
    public PluginColor? Border { get; init; }            // thin frame; null for none
    public float MaxRange { get; init; } = 60f;          // metres from the camera
}
```

Icons are square: the image is fitted inside `SizePixels` keeping its aspect
ratio. The border is 1 interface point outside the image. Sizes are in
interface points, the unit labels are laid out in; the interface already
draws at the display's density, so `PixelScale` is only used to snap icon
positions to whole device pixels.

There is no per-icon height offset: an object's icons share one row origin
above its head and labels, and per-icon offsets would tear a row apart.

### Ground shapes

```csharp
public readonly record struct PluginGroundShape
{
    public PluginMarkerAnchor Anchor { get; init; }
    public PluginGroundShapeKind Kind { get; init; }     // Ring, Disc, Arc
    public float Radius { get; init; }                   // metres, 0.1..100
    public float Width { get; init; }                    // metres, outline width (Ring, unfilled Arc)
    public float StartDegrees { get; init; }             // Arc: clockwise from north
    public float SweepDegrees { get; init; }             // Arc: 0 < sweep <= 360
    public bool Filled { get; init; }                    // Arc: wedge (true) or band (false)
    public bool FacesObject { get; init; }               // Arc with Object anchor: start is relative to its heading
    public PluginColor Color { get; init; }              // alpha 255 is solid

    public static PluginGroundShape Ring(PluginMarkerAnchor anchor, float radius, float width, PluginColor color);
    public static PluginGroundShape Disc(PluginMarkerAnchor anchor, float radius, PluginColor color);
    public static PluginGroundShape Arc(PluginMarkerAnchor anchor, float radius,
        float startDegrees, float sweepDegrees, bool filled, PluginColor color, float width = 0.15f);
}
```

A ring and an unfilled arc are bands from `Radius - Width` to `Radius`. A
disc and a filled arc are filled from the centre.

### Surface and layers

```csharp
public interface IPluginWorldMarkers
{
    const int MaximumIcons = 256;   // per plugin, across all its layers
    const int MaximumShapes = 256;  // per plugin, across all its layers
    IPluginWorldMarkerLayer? CreateLayer();
}

public interface IPluginWorldMarkerLayer : IDisposable
{
    bool SetIcons(IReadOnlyList<PluginWorldIcon> icons);
    bool SetShapes(IReadOnlyList<PluginGroundShape> shapes);
}
```

- `IUiRegistry` gains `IPluginWorldMarkers WorldMarkers =>
  NoOpPluginWorldMarkers.Instance;` and, host-side, `WorldMarkersFor(owner)`
  in the same pattern as `ImagesFor(owner)`, so the store can resolve each
  icon's image through that plugin's own `PluginImages`.
- `SetIcons` / `SetShapes` replace that kind's set on the layer. The list is
  copied and may be reused.
- **Refused as a whole** (returns false, the layer keeps what it had) when
  the entries given, plus the plugin's other layers, exceed its cap. The
  count is taken before drops, so the outcome doesn't depend on which
  entries happen to be invalid.
- **Dropped from an accepted set:** an anchor pinned to nothing (the default
  anchor, object id zero, or a position coordinate that is not finite); an
  icon whose image is not valid; any value that is not a finite number; a
  range that is not positive; a radius, width or sweep outside its range; a
  width of zero or more than the radius. Finite icon sizes are clamped, not
  dropped.
- **Images are looked up in the owning plugin's images only.** Image handles
  are per-plugin sequential ids, so another plugin's handle can't be told
  apart from an own one, but it can never reach the other plugin's image. A
  handle that doesn't resolve (released, or dropped with the interface)
  draws nothing.
- Calls are made on the tick thread, as with every other UI call.
  `ObjectDisposedException` after the layer is disposed, as world-line
  layers do.

## Rendering

### Icons: interface overlay band

A new `WorldIconOverlayController` with one drawing element, mounted on the
shared overlay band beside `WorldLabelOverlayController` and drawn just
before it, so label text stays readable over icons.

- A pure `WorldIconLayout.Place(icons, anchor, labelLines, camera, scale,
  output)` does the placement, the way `WorldLabelLayout.Place` does.
- **Object anchors:** the head point is found as labels find it (base plus
  the host's height). Icons start above that object's
  label stack: `labelLines(objectId)` lines of the label font's height. The
  object's icons, ordered by plugin id then the order given, are
  centred in rows of at most 8, 2 logical pixels apart, stacking upward. A
  row's height is its tallest icon.
- **Spot anchors:** centred on the projected point.
- Placements past `MaxRange` are dropped; the last fifth of the range fades
  (`WorldLabelLayout.Fade`). Placements entirely off screen are dropped.
  The rest are sorted far to near.
- The element draws borders first, then images. Runs are batched by
  texture in emission order, so ten mobs with the same spell icon cost one
  run. Positions snap to whole pixels.

### Shapes: world pass

A new `PluginGroundShapeRenderer` drawn in the same slot as
`PluginWorldLineRenderer` (both Vulkan frame-phase call sites), after the
opaque world.

- **Pipeline:** a new `GroundShapeBatch`, a sibling of
  `DebugLineRenderer` with its own vertex layout (position plus RGBA
  colour; the line renderer's is RGB only) and its own budget. Alpha
  blending, depth test on, depth write off, with a small depth bias against
  the ground. Shapes are hidden by walls and hills and never hide each
  other.
- **Tessellation:** a pure `GroundShapeTessellator` turns a shape into
  triangles. Edge segments about 0.5 m long, 16 to 128 per shape (360°
  equivalent; an arc takes its share).
- **Ground height:**
  - Outdoors (an object anchor whose base is within about 1 m of the
    sampled terrain, or a spot anchor with `IsOutdoor`), each vertex sits
    5 cm above `PhysicsEngine.SampleTerrainZ` at that vertex, so a ring on a
    slope follows the slope.
  - Otherwise (indoors, a dungeon, a bridge or roof) the shape is flat at
    the anchor's height plus 5 cm. An object anchor's height is its base.
- **Facing:** `FacesObject` adds the object's heading to `StartDegrees`.
  The internal anchor lookup (`TryResolveWorldLabelAnchor` or a sibling)
  gains the object's heading.
- **Bounds:** shapes whose centre is more than
  `PluginWorldLineRenderer.DrawRangeMeters` (250 m) from the camera are
  skipped. When the frame's shapes exceed the remaining vertex budget, the
  nearest are drawn and the farthest left out, as world lines do with
  theirs.

## Lifecycle

- `CreateLayer()` returns null on a host that draws nothing (Headless,
  PluginCheck), as `IPluginWorldLines` does.
- Disposing a layer removes its markers. Unloading a plugin disposes every
  layer it made, through a scoping wrapper like `ScopedWorldLines`.
- Leaving the world clears every layer's icons and shapes (object ids mean
  nothing in the next session). The layers stay usable. The store subscribes
  to the shared `WorldEvents.Logoff`, which fires once per stay on every way
  out (logout, lost connection, reconnect, stop). World labels do not in fact
  clear on logoff today (`RuntimeAutomationSurface.Unbind()` has no
  production caller); that is a separate upstream issue, not fixed here.
- Images dropped by an interface teardown (a reconnect) stop the icons that
  use them from drawing. The plugin asks for its images again and calls
  `SetIcons` again, as it would for a canvas.

## Testing

- `WorldIconLayout`: row centring, wrapping at 8, lifting above labels,
  ordering by plugin then given order, mixed sizes, fade, range cut,
  off-screen cut, missing object.
- `GroundShapeTessellator`: segment counts by radius and sweep, arc start
  and direction, `FacesObject`, terrain versus flat heights, band inner
  radius.
- Store: accept, refuse and drop rules; caps counted across layers; another
  plugin's image dropped; layer disposal; plugin unload; clear on leaving
  the world.
- Element: run counts by texture (as `WorldLabelLayerElement` is tested).
- Shape renderer: range skip and nearest-first under budget.
- Real-client gate: `samples/AcDream.Plugins.WorldMarkersDemo` puts a row
  of spell icons over the selected creature, a ring under it, a facing
  wedge, and a waypoint icon plus a disc at a fixed spot. Before and after
  2× captures via the probe-script backbuffer workflow.
- Docs: a "World markers" section in `docs/plugin-api.md`.

## Delivery

Two single-topic PRs, both from fork main:

1. `world-markers/icons`: `WorldMarkers.cs` with anchors, icons, the
   surface and a layer with `SetIcons`; the store and scoping; the overlay
   controller and element; docs; the demo's icon parts.
2. `world-markers/shapes`, stacked on 1: `PluginGroundShape`, `SetShapes`
   on the layer, the translucent pipeline, the tessellator and renderer,
   heading on the anchor lookup; docs; the demo's shape parts.

Each merges into fork main with a merge commit, as painter v2 did.

## Decisions

- Scope: icons over objects, icons at a spot, ground shapes. Line upgrades
  deferred.
- Icons always on top, on the overlay band, not occluded.
- Ground shapes: ring, filled disc, arc or wedge. No polygons.
- The host lays out an object's icons in rows; plugins don't place slots.
- Icon extras: tint with opacity, and a border colour. No badges or sweeps.
- One new surface on `host.Ui` with layers (approach A), not extensions of
  labels or lines, and not a raw projection API.
- Branches start from fork main and use `PixelScale` directly.
