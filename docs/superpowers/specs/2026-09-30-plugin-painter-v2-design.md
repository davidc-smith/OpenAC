# Plugin painter v2: design

Status: agreed in brainstorming 2026-09-30; awaiting spec review.
Scope: extend the plugin canvas API so plugins (first user: the Golem plugin's
HUDs and remote-control widgets) can draw rich widgets themselves. No ImGui:
native libraries cannot ship in plugins and the Vulkan device is internal to
`AcDream.App`.

Delivery: one spec, landed as a series of single-topic pull requests (see
[Order of work](#order-of-work)). Gap 7 (world-anchored drawing) is design only.

---

## 1. Confirmed current behaviour

Every point of the brief was checked against `main` at `bbc83275`.

### Contract (`AcDream.Plugin.Abstractions`)

- `IPluginPainter` (`PluginCanvas.cs:161-242`): `Width`, `Height`, `Clear`,
  `FillRect`, `StrokeRect`, `DrawLine`, `DrawText`/`MeasureText` (one font, one
  size, `:157-158`), `DrawImage`, `DrawImageTransformed`, `PushClip`/`PopClip`.
- `IPluginCanvas` (`PluginCanvas.cs:259-322`): `IsAvailable => false` (`:271`),
  `PointerHandler` and `ReleasePointer` are default interface members (`:304-321`)
  — the pattern every addition below follows.
- `PluginCanvasDescriptor` (`PluginCanvas.cs:52-75`): `CanvasId`, `Width`,
  `Height`, `Anchor`, `Offset`, `StartVisible`, `AcceptsPointerInput`. No layer
  or z field.
- `NoOpPluginCanvas` (`PluginCanvas.cs:328-379`) keeps state, never paints.
- `IUiRegistry.Images` / `RegisterCanvas` (`IUiRegistry.cs:179, 193`);
  `IScopedUiRegistry.ImagesFor` / `RegisterCanvas(owner, …)` (`:287, 294`).
- `IPluginImages` (`PluginImages.cs:46-107`), budgets `MaximumCount`,
  `MaximumBytes`, `MaximumDimension`; `NoOpPluginImages` (`:113`).

### Implementation (`AcDream.App`)

- `PluginPainter` (`UI/Layout/PluginPainter.cs`) forwards to `UiRenderContext`.
  `DrawImage` and `DrawImageTransformed` always pass UVs `0,0,1,1`
  (`:85, :103`) although `UiRenderContext.DrawSprite` takes arbitrary UVs
  (`UI/UiRenderContext.cs:165`). `DrawText` uses the DAT font
  (`DrawStringDat`, `:68`).
- `PluginCanvasSurface` (`UI/Layout/PluginCanvasSurface.cs:57-132`) owns its own
  `TextRenderer` + `UiRenderContext`, begins at the canvas's logical size and
  flushes into the target in the `"plugin-canvas"` pass (`:107-122`). It is
  constructed with `_bindings.Assets.DefaultFont`, the DAT font
  (`UI/RetailUiRuntime.cs:4195`).
- `PluginCanvasElement` (`UI/Layout/PluginCanvasElement.cs`): 4 ms repaint
  budget (`:43`), at most 8 pooled targets (`:46`), pointer input (`:142-253`),
  anchor layout (`:261-295`). Targets are created at the logical size
  (`:356-362`) with `GpuSamplerDescription.WorldClamp` (`:375`), which is
  linear/linear/linear-mip clamp (`Rendering/Gpu/GpuResourceDescriptions.cs`).
- Registration: `Plugins/PluginCanvasRegistration.cs`; per-plugin cap of 8 and
  unique id check in `Plugins/BufferedUiRegistry.cs:210-258`.
- Scoped forwarder: `ScopedPluginHost.ScopedUiRegistry.Images` and
  `RegisterCanvas` (`src/AcDream.Core/Plugins/ScopedPluginHost.cs:2057-2086`);
  every canvas call goes through `IndividualCanvas` (`:2162-2210`). **A new
  `IPluginCanvas` member that is not forwarded there silently falls back to its
  inert default.**
- Mounting: `RetailUiRuntime.MountPluginCanvases` (`UI/RetailUiRuntime.cs:4183-4227`)
  adds every canvas of every plugin to one overlay layer `"PluginCanvases"`.

### Rendering pieces

- `TextRenderer` (`Rendering/TextRenderer.cs`): one `"ui-text"` pipeline,
  `StraightAlpha`, sample count 1, no anti-aliasing (`:86-98`). Vertex is
  position + uv + **per-vertex RGBA colour**, interpolated (`:11-20`). Solid
  fills are sprites with texture handle 0 (`DrawFill`, `:127-128`), so they keep
  painter's order with images. Within one pass `DrawLayer` draws all sprite runs,
  then `TextRenderer.DrawRect` quads, then BitmapFont text (`:386-413`).
- `TextRenderer.DrawConvexPolygon` is internal (`:238`), one colour, fan
  triangulated; only caller is `UiRenderContext.EmitConvexQuad`.
- Clipping is geometric on the CPU (`QuadClipper`, `TransformedQuadClipper`,
  the latter at most 4 input vertices, lerps position and UV only).
- `BundledUiFont.Load(textures, pixelHeight)` (`UI/BundledUiFont.cs:14`) bakes
  embedded Noto Sans with `stbtt_BakeFontBitmap` into a `UiDatFont`. 8–32 px
  only (`:31`) because `FontCharDesc` stores sizes in bytes. No kerning.
  StbTrueTypeSharp 1.26.12 (`Directory.Packages.props:34`).
- Plugin zips allow `.ttf`/`.otf` (`docs/plugin-manifest.md:76`).
- UI shaders `ui_text.vert/.frag`; SPIR-V is pinned both by the manifest
  (`Rendering/Shaders/spv/shaders.manifest.json`) and by byte hash in
  `VulkanShaderManifestTests.RetailOracleSpirvSha256`. **This design needs no
  shader changes.**

### Findings beyond the brief

1. **Translucent canvas content composites at a².** The pipeline uses the same
   `SrcAlpha, OneMinusSrcAlpha` factors for the alpha channel as for colour
   (`Rendering/Gpu/Vk/VulkanGpuPipeline.cs:170-179`). A canvas target is cleared
   to 0, so a 50 % fill is stored with alpha 0.25 and colour already multiplied
   by 0.5, then blended again with straight alpha. Anything translucent on a
   canvas — and every feathered edge — comes out too faint.
2. **Plugin hotkeys ignore chat and dialog focus.** `AppHotkeyRegistry.OnKeyDown`
   (`Input/AppHotkeyRegistry.cs:170-214`) suppresses on `ActiveScope == Chat` and
   `Dialog`/`EditField`, but `InputDispatcher.PushScope`
   (`UI.Abstractions/Input/InputDispatcher.cs:343`) has no production caller. The
   live signal is `UiRoot.KeyboardFocus` (`UI/UiRoot.cs:115`), reaching the
   dispatcher as `WantCaptureKeyboard` (`InputDispatcher.cs:414`). So a plain
   letter hotkey fires while the player types in chat, contrary to
   `docs/plugin-api.md:1136-1147`.
3. **"Above everything" today works by integer overflow.**
   `RetailWindowManager.BringToFront` (`UI/RetailWindowManager.cs:229-236`)
   takes `max(child.ZOrder + 1)`; children at `int.MaxValue` (SpewBox,
   pre-game screens) wrap to `int.MinValue` and are ignored.
4. **Order between canvases is not defined.** All canvases share ZOrder 0 in
   one layer; order is `Dictionary` enumeration order and `Array.Sort` is not
   stable past 16 children (`UI/UiElement.cs:269-279`).
5. **World labels** (`host.Automation.Labels`, `docs/plugin-api.md:1226-1265`)
   already project text onto objects each frame — the precedent for gap 7.

### HiDPI answer

Canvases are blurry on a Retina display; the main UI is not blurry in the same
way. The UI is sized in window points (`Rendering/GameWindow.cs:1619`,
`_window.Size`), the swapchain in framebuffer pixels
(`Rendering/Gpu/Vk/VulkanGraphicsContext.cs`), and the UI projection stretches
points over the full framebuffer. Main-UI geometry is therefore rasterised at
physical resolution, and its bitmap art is magnified with nearest filtering
(blocky but crisp). A canvas is rasterised into a logical-size texture and
magnified with bilinear filtering by (framebuffer ÷ window) × `UiRoot.CanvasScale`:
2× in gameplay on a 2× display, more on a stretched pre-game fixed canvas
(`CanvasScale` is only the pre-game fixed canvas, `UI/UiRoot.cs:22-81`, not a
DPI setting). This is derived from the code; PR 4 opens with before/after
screenshots on a Retina Mac as its acceptance gate.

---

## 2. Cross-cutting rules

- **Additive contract.** New interface members are default members with inert
  answers (`false`, `None`, no-op). New descriptor properties are `init`
  properties whose defaults keep today's behaviour. XML docs state when a member
  returns false or none (the Abstractions build fails on undocumented members).
- **Forwarders forward.** Every new `IPluginCanvas` member is forwarded in
  `ScopedPluginHost.IndividualCanvas`, and every new registry surface is cached
  and tracked in `ScopedUiRegistry` like `Images`. Each gets a
  `ScopedUiRegistry*Tests` case proving it reaches the host.
- **Headless inert.** `NoOpPluginCanvas`, `NoOpPluginImages` and the new
  `NoOpPluginFonts` keep what the plugin sets and never throw. A headless
  contract test covers each new surface.
- **Guards unchanged.** 4 ms paint budget, 2 ms input budget (three strikes),
  balanced clip stack, 8 canvases per plugin. Expensive host work (font baking,
  target reallocation) happens outside the guarded call.
- **Thread.** Everything stays on the tick/interface thread; the retained,
  `Invalidate()`-driven model is unchanged.
- **Docs and tests per PR.** Each PR updates `docs/plugin-api.md` and the test
  list in `docs/plugin-ui-markup.md` (Tests section), and states the API change
  in its description.

---

## 3. PR 0 — Canvas alpha compositing

**Problem:** finding 1.

**Change (no shader change):**

- `GpuBlendMode` gains a mode that blends colour as straight alpha and alpha as
  `One, OneMinusSrcAlpha` (working name `StraightAlphaOverTransparent`).
  `VulkanViewportMapping.BlendFactorsOf` returns colour and alpha factors
  separately; `VulkanGpuPipeline` sets `SrcAlphaBlendFactor`/`DstAlphaBlendFactor`
  from them. Existing modes map to identical factors as today.
- `TextRenderer` gets a second pipeline built with the new mode, used by
  `FlushTo` for passes into a canvas target. The target then holds
  premultiplied colour with correct coverage.
- The canvas quad on the interface is drawn with the existing
  `PremultipliedAlpha` mode (`One, OneMinusSrcAlpha`). `SpriteSeg` carries a
  blend flag; `DrawLayer` rebinds the pipeline only when the flag changes. The
  tint passed for a premultiplied sprite is premultiplied (`rgb *= a`) so window
  and fade alpha still apply.
- The same `RecordingGpuDevice` fake records pipeline descriptions and binds.

**Tests:** pipeline description and bind order on the recording device
(`PluginCanvasElementTests`, `TextRenderer` tests); `Lane=Vulkan` readback: a
50 % white fill on a canvas over black composites to 50 % grey (±1/255).

---

## 4. PR 1 — Fonts

### Contract (new `PluginFonts.cs`)

```csharp
/// A font at one size, held by the host for a plugin. 0 means no font.
public readonly record struct PluginFont(int Handle, float PixelSize, float LineHeight, float Ascent)
{
    public static PluginFont None => default;
    public bool IsValid => Handle != 0;
}

/// An inclusive range of Unicode code points.
public readonly record struct PluginCodepointRange(int First, int Last);

public sealed record PluginFontOptions
{
    /// Null bakes the bundled default ranges: U+0020–U+024F, U+0370–U+052F,
    /// U+2000–U+206F.
    public IReadOnlyList<PluginCodepointRange>? Ranges { get; init; }
}

public interface IPluginFonts
{
    bool IsAvailable => false;
    PluginFont Bundled(float pixelSize) => PluginFont.None;
    PluginFont FromStream(string name, Func<Stream> open, float pixelSize,
                          PluginFontOptions? options = null) => PluginFont.None;
    bool Release(PluginFont font) => false;
    int Count => 0;
    int MaximumCount => 0;
    long MaximumBytes => 0;
    int MaximumGlyphs => 0;
    float MinimumPixelSize => 0;
    float MaximumPixelSize => 0;
}

public sealed class NoOpPluginFonts : IPluginFonts
{
    public static NoOpPluginFonts Instance { get; } = new();
    private NoOpPluginFonts() { }
}
```

- `IUiRegistry`: `IPluginFonts Fonts => NoOpPluginFonts.Instance;`
- `IScopedUiRegistry`: `IPluginFonts FontsFor(PluginUiOwner owner) => NoOpPluginFonts.Instance;`
  (the drawing host returns a surface that also implements `IDisposable`).
- `IPluginPainter` (defaults fall back to the DAT font so an older host still
  draws the text):

```csharp
void DrawText(string text, PluginPoint position, PluginColor color, PluginFont font, bool outline = false)
    => DrawText(text, position, color, outline);
PluginSize MeasureText(string text, PluginFont font) => MeasureText(text);
```

### Semantics

- Sizes are in canvas (logical) pixels. `Bundled` is the client's Noto Sans.
  `FromStream` accepts TrueType (`.ttf`) and, best effort, CFF OpenType (`.otf`);
  the stream is opened only when the host does not already hold that key and is
  disposed by the host.
- Key: `(name, pixelSize, ranges)`; `Bundled` keys on `(pixelSize)`. A repeat
  request returns the same handle, held once more; it takes as many releases.
- Budgets (starting points, like the image budget, not measurements):
  `MaximumCount` 16 handles, `MaximumBytes` 16 MB covering retained font files
  and the atlases of the plugin's own fonts, `MaximumGlyphs` 2048 per handle,
  pixel size 6–72. Bundled atlases are cached host-wide by `(size, scale)` and,
  like client art, not counted against bytes (still counted against
  `MaximumCount`). A refusal returns `PluginFont.None` and is logged once.
- A code point the font lacks, or outside the baked ranges, draws the font's
  `.notdef`/`?` fallback.
- Handles are dropped when the interface is torn down (e.g. a reconnect); a
  released or dropped font draws nothing and measures `(0, 0)`.
- Text is one line, as today. `LineHeight` and `Ascent` let plugins align
  baselines across fonts. `MeasureText` includes kerning, matching `DrawText`.
- Calls only from the tick thread; any other thread is refused (throws), as for
  images. Baking runs synchronously at request time, outside any paint.

### Implementation

- `CanvasFontBaker` (new, internal, in `UI/`): the StbTrueType code factored
  out of `BundledUiFont.Bake`, taking `byte[]`, pixel size, ranges and scale.
  Uses the pack API (`stbtt_PackBegin`/`stbtt_PackFontRanges`), growing the
  atlas page up to 2048×2048, and fails cleanly when glyphs do not fit or exceed
  the cap. `BundledUiFont` keeps its public behaviour and tests and calls the
  baker.
- `CanvasFontAtlas` (new, internal): float metrics per glyph (advance, bearing,
  size, UV rect), `LineHeight`, `Ascent`, an RGBA atlas (white, alpha =
  coverage) uploaded releasable with a linear clamp sampler, and the retained
  `stbtt_fontinfo` for kerning (`stbtt_GetCodepointKernAdvance`) and later
  rebakes.
- `PluginFontTable` (new, `Plugins/`): modelled on `PluginImageTable` — bind to
  texture services and the UI thread, key → entry with ref count, byte and count
  accounting, report-once, unbind drops everything. Backend interface
  `IPluginFontBackend` so tests use a fake uploader.
- `PluginFonts` (new, `Plugins/`): the contract wrapper, disposable,
  `BufferedUiRegistry.FontsFor(owner)`; `ScopedUiRegistry.Fonts` caches and
  tracks it like `Images`.
- `UiRenderContext.DrawStringCanvasFont(CanvasFontAtlas, text, x, y, color, outline)`:
  glyphs go through the sprite path (painter's order preserved), origins snapped
  to device pixels, outline as eight offset copies in the outline colour.
- `PluginPainter` resolves the handle through the plugin's `PluginFonts`
  (bound alongside images) and draws or measures.

### Tests

- `CanvasFontBakerTests`: Noto at 6, 16 and 72 px; a custom range; glyph cap
  refusal; a known kerning pair; the OTF path against a small permissively
  licensed CFF test font committed with its licence (if none fits, OTF is
  documented as best effort and the test is omitted).
- `PluginFontTableTests`: dedup and ref counts, count/bytes/size/glyph refusals,
  bad stream, null stream, wrong thread, unbind, report-once.
- `BufferedUiRegistryFontsTests`, `ScopedUiRegistryFontsTests` (tracking and
  disposal with the plugin).
- `PluginCanvasElementTests`: glyph runs land in call order among fills and
  images; `MeasureText(text, font)` equals the drawn advance; an invalid font
  draws nothing; the default-member fallback on a stub painter.
- Headless contract: `Fonts` is inert.
- Docs: new `## Fonts` section in `plugin-api.md`; Canvases primitives list.

---

## 5. PR 2 — Image regions and nine-slice

### Contract (`IPluginPainter`, default no-ops)

```csharp
public readonly record struct PluginInsets(double Left, double Top, double Right, double Bottom)
{
    public static PluginInsets Uniform(double all) => new(all, all, all, all);
}

void DrawImageRegion(PluginImage image, PluginRect source, PluginRect destination, PluginColor tint) { }

void DrawImageRegionTransformed(PluginImage image, PluginRect source, PluginRect destination, PluginColor tint,
    double rotationRadians, PluginPoint pivot, double scaleX = 1.0, double scaleY = 1.0) { }

void DrawImageNineSlice(PluginImage image, PluginRect destination, PluginInsets insets, PluginColor tint,
    PluginRect? source = null, bool drawCenter = true) { }
```

### Semantics

- `source` is in the image's own pixels, clamped to its bounds; an empty or
  wholly outside region draws nothing.
- Nine-slice `insets` are in source pixels. Corners draw at inset size; edges
  and centre stretch (no tiling). When the destination is smaller than the
  insets' sum on an axis, the corners on that axis shrink proportionally.
  `source` selects the frame inside a sheet; `drawCenter: false` draws a frame.
- On a host that predates these members they draw nothing (drawing the whole
  sheet instead would be wrong).

### Implementation

- UV arithmetic in `PluginPainter` over `DrawSprite`/`DrawSpriteTransformed`;
  a nine-slice is up to nine sprites, each clipped by `QuadClipper`.
- Bleed: plugin-owned uploads (`TextureCache.UploadReleasableRgba8` via
  `RetailPluginImageBackend.UploadOwned`) move to a clamp sampler (whole-image
  draws are unaffected). `PluginImageTable.TryResolve` also reports whether the
  texture is linear-filtered; regions on linear textures are inset by half a
  texel, nearest-filtered client art is not inset.

### Tests

`PluginCanvasElementTests`: UVs for a region, half-texel inset present/absent,
clamping, nine-slice rectangles and UVs for large, small and frame-only cases,
transformed region. `PluginImageTableTests`: filter flag. Docs: Canvases.

---

## 6. PR 3 — Shapes

### Contract (`IPluginPainter`, default no-ops unless noted)

```csharp
public readonly record struct PluginCornerRadii(double TopLeft, double TopRight, double BottomRight, double BottomLeft)
{
    public static PluginCornerRadii Uniform(double radius) => new(radius, radius, radius, radius);
}

public enum PluginGradientDirection { Horizontal, Vertical }

void FillPolygon(ReadOnlySpan<PluginPoint> points, PluginColor color) { }
void FillPolygon(ReadOnlySpan<PluginPoint> points, ReadOnlySpan<PluginColor> colors) { }
void FillRoundedRect(PluginRect rect, PluginCornerRadii radii, PluginColor color) { }
void StrokeRoundedRect(PluginRect rect, PluginCornerRadii radii, PluginColor color, float thickness = 1f) { }
void FillEllipse(PluginRect bounds, PluginColor color) { }
void StrokeEllipse(PluginRect bounds, PluginColor color, float thickness = 1f) { }
void FillCircle(PluginPoint center, double radius, PluginColor color)
    => FillEllipse(new PluginRect(center.X - radius, center.Y - radius, radius * 2, radius * 2), color);
void FillRectGradient(PluginRect rect, PluginColor from, PluginColor to, PluginGradientDirection direction) { }
```

### Semantics

- New shapes are anti-aliased: edges feathered over one device pixel. Existing
  primitives are unchanged.
- Polygons are convex, 3–64 points, either winding. Per-vertex colours are
  interpolated linearly (simple gradients).
- Radii are clamped the CSS way: if adjacent radii exceed a side, all are scaled
  down by the same factor. Zero radii give square corners.
- Strokes are centred on the outline. A stroke thinner than one device pixel is
  drawn one device pixel wide with proportionally lower alpha.
- Invalid input (non-convex, too many points, mismatched colour count,
  non-finite values, negative sizes) draws nothing and is reported once per
  canvas; it never throws, since a throw drops the canvas.

### Implementation (CPU feathering, no shader change)

- `CanvasGeometry` (new, internal, pure): tessellates each shape into a coloured
  triangle list plus a feather ring whose outer vertices have alpha 0; arc
  segment count derives from radius × device scale (bounded per corner).
- `UiColorVertex` (position, colour) and `TextRenderer.DrawTriangles(ReadOnlySpan<UiColorVertex>)`
  (new, internal): untextured triangles appended to the sprite runs (texture 0),
  so painter's order holds.
- `ColoredTriangleClipper` (new): clips each triangle against the current clip
  rect, lerping colour; fast accept/reject for triangles wholly inside/outside.
  `TransformedQuadClipper` is untouched.
- `UiRenderContext` gains the translate/alpha-aware wrapper that feeds the
  clipper and `DrawTriangles`.
- A per-paint vertex cap, sized in the plan from the per-frame vertex ring
  capacity; exceeding it counts as a paint overrun and is reported.
- Requires PR 0; without it feathered edges composite at a².

### Tests

`CanvasGeometryTests` (triangle counts, covered area within tolerance of the
analytic area, feather alphas, radius clamping, convexity rejection, thin
strokes), `ColoredTriangleClipperTests`, `PluginCanvasElementTests` (order,
clipping, report-once on bad input). Optional `Lane=Vulkan` readback of a circle
edge. Docs: Canvases.

---

## 7. PR 4 — HiDPI sharpness

### Contract

```csharp
// IPluginPainter
/// Device pixels per canvas pixel for this paint; 1 on a host that does not scale.
double PixelScale => 1.0;
```

### Implementation

- `PluginCanvasHostServices` gains `Func<Vector2> PixelScale`, supplied at
  `Composition/InteractionRetainedUiComposition.cs:1055` as
  `FramebufferSize / Window.Size`.
- The element computes `s = max(pixelScale.X, pixelScale.Y) × root.CanvasScale`,
  rounded up to a multiple of 0.25 and clamped to [1, 4], then lowered further
  if `ceil(W·s)` or `ceil(H·s)` would exceed the device's render-target limit.
- Targets are `ceil(W·s) × ceil(H·s)`. When `s` changes, the pool is released
  through the device (the existing deferred release), the canvas invalidated,
  and a prepare step outside the guard rebakes the plugin's held fonts at
  `size·s`.
- Repaint: `_renderer.CanvasScale = s`, `_renderer.Begin(size·s)`; context,
  clip and painter `Width`/`Height` stay logical, as `UiRoot` does for the fixed
  canvas. The surface's renderer disables the linear-twin swap
  (`TextRenderer.cs:219`) so DAT glyphs keep nearest filtering. Feathering uses
  one device pixel = `1/s` logical.
- Display unchanged: `DrawSprite(0, 0, W, H)`, texels now 1:1 with device
  pixels. Pointer coordinates are unchanged (already logical).
- Out of scope: HiDPI for the main UI (follow-up).

### Tests

Harness with a fake pixel scale: target description at 2×, renderer projection
size at 2×, painter and pointer logical; scale change reallocates and
invalidates; fonts rebaked at `size·s`; clamp to the device limit; `PixelScale`
reported; `PixelScale` default on a stub. Manual: before/after screenshots on a
Retina Mac, attached to the PR (acceptance gate).

---

## 8. PR 5 — Layering

### Contract

```csharp
public enum PluginCanvasLayer
{
    /// Over the world, under every window (today's behaviour).
    World = 0,
    /// Over every window, under dialogs, tooltips and menus.
    AboveWindows = 1,
}

// PluginCanvasDescriptor
public PluginCanvasLayer Layer { get; init; } = PluginCanvasLayer.World;
/// Order among this plugin's canvases in the same layer; higher is drawn on top.
public int ZOrder { get; init; }

// IPluginCanvas
int ZOrder { get => 0; set { } }
```

`NoOpPluginCanvas` keeps `ZOrder`; the descriptor's `Layer` is fixed at
registration.

### Resulting order (bottom → top)

world and world labels → `World` canvases → windows → `AboveWindows` canvases →
dialogs and tooltips → menus and drag ghost (overlay pass) → `int.MaxValue`
elements (SpewBox, pre-game screens).

### Implementation

- Two canvas layers: the existing one on the overlay host, and a new
  viewport-sized click-through layer mounted on `UiRoot` at a new band constant
  above windows. Each plugin gets its own sub-layer in each, ordered by plugin
  mount order (first mounted lowest). Within a sub-layer the canvases are
  ranked by `(ZOrder, registration sequence)` and each element's UI ZOrder is
  set to its rank, recomputed when a canvas is added, removed or changes
  `ZOrder`; ranks are unique, so the unstable sort never sees ties.
- Banded z-order: `RetailWindowManager.BringToFront(window)` raises within the
  window band only and never reaches the canvas band; a new
  `BringToFront(window, UiBand.DialogsAndTooltips)` is used by
  `RetailDialogFactory` (`UI/Layout/RetailDialogFactory.cs:277, 398`) and
  `RetailTooltipPresenter`. `int.MaxValue` children are skipped explicitly
  instead of by overflow.
- Input: an `AboveWindows` canvas with input takes clicks over windows beneath
  it (documented). Under a modal it gets none (existing `UiRoot.Modal` gate).
  While a drag is in progress (`UiRoot.DragSource != null`) no canvas answers
  the hit-test, so drops reach the windows or world beneath.
- Plan task: list callers of `TextRenderer.DrawRect` and BitmapFont text in
  retail windows; those draw after all sprites in the pass and could appear over
  a top canvas. If any matter, move them to the sprite path.

### Tests

`RetailWindowManager` banding (window to front stays below the band; dialog
and tooltip go above it; `int.MaxValue` untouched), hit-test order across
layers, modal blocking, drag pass-through, ZOrder within and across plugins,
runtime ZOrder change, forwarder test, headless inert test. Docs: Canvases
(replace "under every window" with the layer description).

---

## 9. PR 6a — Hotkeys respect UI focus (bug fix)

- `AppHotkeyRegistry` takes a focus-state source bound from `UiRoot`:
  `HasKeyboardFocus` (`KeyboardFocus != null`) and `IsModalOpen`
  (`Modal != null`).
- Rules: rebind capture → no hotkeys; modal → no hotkeys; any keyboard focus
  (chat, a text field, a focused canvas) → only chords with Ctrl or Alt. The
  existing `ActiveScope` checks remain.
- Caveat, documented: hotkeys run first in the key handler order, so they see
  the focus state from before the key is handled.
- Tests: `AppHotkeyRegistryTests` per rule; a host-level test with a focused
  `UiRoot` element and with a modal.
- Docs: Hotkeys section wording, which then matches behaviour.

---

## 10. PR 6b — Canvas keyboard input

### Contract

```csharp
// PluginCanvasDescriptor
public bool AcceptsKeyboardInput { get; init; }

public enum PluginKeyEventKind { Down, Up, Text, FocusGained, FocusLost }

/// Key is meaningful for Down and Up (PluginKey.Unknown otherwise); Text carries the typed characters for Text.
/// Keys PluginKey cannot name are not delivered as Down/Up.
public readonly record struct PluginKeyEvent(
    PluginKeyEventKind Kind,
    PluginKey Key,
    PluginKeyModifiers Modifiers,
    bool IsRepeat = false,
    string? Text = null);

// IPluginCanvas
/// Returns true when the plugin handled the event; only Escape's answer changes what the host does.
Func<PluginKeyEvent, bool>? KeyHandler { get => null; set { } }
bool HasKeyboardFocus => false;
bool RequestKeyboardFocus() => false;
void ReleaseKeyboardFocus() { }
```

### Semantics

- A canvas takes focus by a press on it (which needs `AcceptsPointerInput` as
  well, since only a hit-testable canvas can be clicked) or by
  `RequestKeyboardFocus()`, which succeeds only when the canvas is mounted,
  shown and has a `KeyHandler`, nothing else holds keyboard focus, no modal is
  up, and no rebind capture is running; otherwise false. It never takes focus
  from chat, a text field or a dialog.
- While focused the canvas receives `Down`/`Up` for keys `PluginKey` can name,
  `Text` for typed characters, host-generated repeat as `Down` with
  `IsRepeat = true` (0.40 s delay, 0.04 s interval, as `UiField`), and
  modifiers from the same source as pointer events. Game actions are suppressed
  (existing `WantCaptureKeyboard`); plugin hotkeys follow PR 6a's focus rule.
- Focus is given back, with exactly one `FocusLost`, on: Escape the handler did
  not handle; a press elsewhere; the canvas hidden or disposed; the handler set
  to null or dropped by its guard; the paint callback dropped; a modal opening;
  interface teardown; `ReleaseKeyboardFocus()`.
- The key handler has its own guard (2 ms, three strikes). Tripping it releases
  focus and stops key delivery; pointer input and painting continue.
- Headless: the handler is kept and never called; `RequestKeyboardFocus`
  returns false; `HasKeyboardFocus` is false.

### Implementation

- `PluginCanvasElement`: `AcceptsFocus` while opted in, shown, handler set and
  not dropped; `IsEditControl = true` so `UiRoot.OnChar` delivers text; handles
  `KeyDown`/`KeyUp`/`Char`/`FocusGained`/`FocusLost` in `OnEvent`; repeat timer
  in `OnTick`; releases focus in every path above (following
  `CreditsUiController.cs:360-361`: release only if it still owns focus).
- The `PluginKey` ↔ Silk `Key` table moves out of `AppHotkeyRegistry.MapKey`
  into a shared internal mapper used by both.
- Registration and `ScopedPluginHost.IndividualCanvas` forward the four members.

### Tests

Element tests for each focus path, request refusals, repeat, the Escape policy,
the guard; `UiRoot` test that game actions are suppressed while a canvas is
focused; forwarder tests for all four members; headless inert tests. Docs: new
"Keyboard input" subsection under Canvases.

---

## 11. Gap 7 — World-anchored drawing (design only)

Not implemented in this series. Recommended direction, modelled on World labels:

- **B — object placement (primary).** A descriptor placement alternative to the
  screen anchor, e.g. `PluginCanvasPlacement.OnObject(uint objectId, double heightOffset, double maxRange)`.
  Each frame the host projects the object's head point with the same projection
  World labels use (`Composition/InteractionUiRuntimeSources.cs` `UiSnapshot`)
  and moves the retained quad; painted content is reused, so movement costs no
  repaint. Hidden off-screen, beyond range, or when the object is unknown; fades
  over the last fifth of the range; not occluded (as labels).
- **A — projection query (secondary).** `bool TryProjectObject(uint objectId, double heightOffset, out PluginPoint screen)`
  for plugins that lay out their own overlay canvas; one-frame lag documented.
- **Open issue.** The eight-canvas cap rules out a canvas per creature for health
  bars. A later "instanced canvas" (one paint callback invoked per object id,
  with a per-plugin instance cap and shared target atlas) belongs in its own
  spec.

---

## Plan-time corrections (2026-09-30)

Measured or found while writing the PR 0, PR 1, PR 3, PR 4, PR 5, PR 6a and PR 6b plans; these
supersede the sections above where they differ.

- **PR 0, lazy composite pipeline.** `ResourceCleanupGroupTests.TextRendererConstructionCreatesAndDisposesOnlyOnePipeline`
  pins a `TextRenderer` to one pipeline at construction, so the
  `PremultipliedAlpha` composite pipeline is created on the first premultiplied
  sprite, not in the constructor. The canvas surface's renderer is constructed
  with the new blend mode and still creates exactly one pipeline.
- **PR 1, coverage atlases.** Glyph atlases are single-channel (`R8Unorm`) and
  drawn through the fragment shader's existing coverage branch
  (`uTextureIndexB`), in sprite runs keyed by the coverage texture, so painter's
  order holds and atlases are a quarter of the RGBA size. Still no shader change.
- **PR 1, sizes.** Measured with the pack API: Noto Sans with the default ranges
  needs a 512² atlas at 16 px, 1024² at 32 px and 2048² at 72 px; baking takes
  about 11 ms at 16 px and 29 ms at 72 px (hence baking outside paint).
  `MaximumPixelSize` is 64 and atlases are capped at 2048².
- **PR 1, glyph selection.** `stbtt_PackSetSkipMissingCodepoints` makes
  `stbtt_PackFontRanges` report failure in StbTrueTypeSharp 1.26.12 even when
  every glyph packed. The baker instead scans the requested ranges, keeps only
  code points the font has (`stbtt_FindGlyphIndex != 0`), and packs them from an
  explicit code point list. `MaximumGlyphs` (2048) counts those present glyphs,
  so a sparse icon font can name all of U+E000–U+F8FF; the scan is capped at
  65,536 code points per request.
- **PR 1, fallback.** A code point the font lacks draws the font's `?` when that
  glyph was baked, and nothing otherwise (an icon-only range usually has no `?`).
- **PR 1, BundledUiFont untouched.** Its atlas layout and metrics feed the markup
  "plugin" theme; refactoring it onto the new baker would shift that UI. It only
  gains an internal accessor for the embedded font bytes.
- **PR 1, OTF fixture.** A 1.4 KB CFF subset of the repository's Noto Sans
  ("AVTo?", OFL) generated with fontTools 4.60.1 rasterises through
  StbTrueTypeSharp and reports the same GPOS kerning as the TTF (A–V −40,
  T–o −70 units), so `.otf` support is tested, not best effort.

- **PR 3, vertex budget.** 32,768 shape vertices per paint. A vertex is
  32 bytes, so that is 1 MiB of the 16 MiB per-slot vertex ring
  (`GpuMemoryProfile.RingCapacityBytesPerSlot`), and it takes about 0.6 ms
  to tessellate and clip. Going over is reported once per canvas, and the
  rest of that paint's shapes are skipped. The paint counts as an overrun
  through a new optional predicate on `UiDrawCallbackGuard.Invoke`, so
  three such paints in a row drop the callback, as for time; the trip
  message names the drawing budget.
- **PR 3, curves.** Chords stray at most 0.1 device pixel from the true curve
  (0.25 px was considered; 0.1 keeps covered area within 1% from a
  20 px radius). A corner gets at most 32 chords; an ellipse gets 8–256,
  rounded up to a multiple of 4. Ellipse strokes grow both radii, which is
  exact for circles. Outside a zero-radius corner a stroke stays square, as
  in CSS.
- **PR 3, what is reported.** Reported once per canvas: non-finite values;
  negative sizes, radii or thicknesses; wrong point or colour counts;
  non-convex outlines (checked by the sign of each turn *and* a total
  turning of exactly one circle, which rejects stars); an unknown gradient
  direction. Silent: zero area, zero thickness, collinear points.
- **PR 3, gradients** are drawn as a four-point polygon, so their edges are
  feathered too.
- **PR 3, branch.** Built from `painter-v2/canvas-alpha`, not from the fork's
  `main`, which carries PR 1. Merging the two conflicts only mechanically:
  both sides add a parameter to `PluginPainter.Bind` and
  `PluginCanvasSurface.Repaint`, and both append to the
  `plugin-ui-markup.md` test paragraph.
- **PR 3, input for PR 4.** `PluginPainter.DevicePixel` (1 today) becomes
  `1 / s`. `CanvasGeometry` already takes the device pixel size, and the
  fringe width and chord counts follow from it.

- **PR 3, polygon fringe (found in task review; supersedes the plan's Task 4
  polygon code).** A fixed half-pixel inset folds on polygons thinner than a
  pixel and loses coverage at sharp corners. Instead, the inner ring pulls the
  edge lines in by half a device pixel, in double precision. An edge that
  shrinks to nothing drops out and its neighbours meet (a straight skeleton;
  the edge that set the step always drops). The outer ring is mitred, and
  bevelled past a 10× mitre. The pull-in stops early when the polygon
  collapses to a point or a line, or when it is thinner than the remaining
  pull-in (area < perimeter × remaining). The polygon is then mostly fringe,
  and its alpha is lowered until it covers its own area (never raised). A
  fold-back spike, which reverses with zero cross product, is not convex.
  Measured: named cases within 0.33% of the analytic area. Across 200k random
  convex polygons, 449 big shapes (> 4 px²) are more than 2% off their area,
  against 7298 for the plan's code. The residual is needles under 2 px wide
  with ~1° tips, which under-cover by up to ~9%.
- **PR 3, sub-pixel fills and strokes.** A rounded rect or ellipse at most
  one device pixel across is area-corrected in the same way. A stroke that
  leaves no hole is drawn as the fill of the rectangle grown by half the
  stroke. A stroke whose hole is narrower than the fringe under-covers by
  about 6% (accepted).
- **PR 3, gradients to transparent.** Vertex colours blend as straight
  colour, so a fade to `PluginColor.Transparent` (transparent black) darkens
  the middle of the fade. The docs say to fade to the same colour with alpha
  0. Interpolating premultiplied colour would need its own run type; that is
  left as a design choice for later.
- **PR 3 × PR 1 merge.** A scratch merge conflicted in five files, all "keep
  both". One of them, not listed in the plan, is the `IPluginPainter` class
  doc paragraph: keep PR 3's Shapes paragraph followed by PR 1's text
  paragraph. Use parameter order `Bind(…, fonts, shapeProblems)` and
  `Repaint(…, _images(), _fonts(), shapeProblems: _shapeProblems)`. The
  merged tree built with 0 warnings and passed the shape and font suites.

- **PR 4, host services.** `PluginCanvasHostServices` takes
  `Func<Vector2> FramebufferPerPoint` (framebuffer ÷ window size, 1 for a
  minimised window) in place of the linear-twin resolver. The canvas surface
  never swaps to linear twins, so the old parameter had no use left.
- **PR 4, which scale fonts follow.** Each canvas paints at its own clamped
  scale. Fonts are prepared for the *interface* scale, which is lower for a
  canvas only when it is too large for the device, so two canvases of one
  plugin never make its fonts rebake back and forth. A glyph from a bake at
  a higher scale than its canvas is drawn at `1/bake scale` size and snapped
  to the canvas's device pixels.
- **PR 4, sharper bakes keep logical layout.** The sharper bake supplies only
  glyph boxes and UVs (divided by its scale). The pen, kerning, `LineHeight`,
  `Ascent` and `MeasureText` come from the font's own bake, so no handle or
  measurement changes. stbtt's packed advances are `scale × advanceWidth`,
  exactly linear, so the two bakes agree to float precision.
- **PR 4, the fallback ladder.** `TryBakeForScale` tries `s`, then `s − 0.25`,
  … while above 1. The baker first sums the packer's rectangles
  (`stbtt_GetGlyphBitmapBoxSubpixel` + 1 px padding) and skips atlas sizes
  below that area, an exact lower bound, so a hopeless bake fails in under
  1 ms. Measured: 16 px at 2× takes 1024×512 (~9 ms); 32 px at 2× takes
  2048×1024 (~24 ms). 64 px at 2× passes the bound but fails packing (~23 ms),
  then fits at 1.75× in 2048×2048 (~47 ms in all). A font below its scale is
  reported once per (font, scale).
- **PR 4, the sharper budget.** `PluginFontBudget.MaximumSharpBytes` is 32 MiB
  per plugin, for the plugin's own sharper atlases only. Bundled sharper bakes
  live on the shared cache's `CanvasFont` and cost no plugin anything. When
  the room left is below 2048², the shortfall reported is the budget.
- **PR 4, one face per file.** `CanvasFontFace` holds the bytes (kept for
  rebakes) and the parser's copy (for kerning). The bundled cache shares one
  face across every size; a plugin's own font keeps a face of its own, as its
  bytes are already counted per request. Sizes are rounded to quarter pixels
  *after* the min/max check, so PR 1's refusals (5.9, 64.1) are unchanged and
  `PluginFont.PixelSize` is the rounded size (contract docs updated).
- **PR 4, what stays as before.** The interface (DAT) font is drawn on whole
  canvas pixels with nearest sampling, so at 2× it is crisply pixel-doubled.
  Plugin images keep their own linear sampler. Outline offsets stay one
  canvas pixel. The 32,768-vertex budget is unchanged although curves take
  ~1.3–1.4× the vertices at 2× (a 100 px circle: 642 → 894), and the docs
  say so.
- **PR 4, test harness.** `PluginCanvasElementTests.Harness` now mounts an input
  canvas with `takesInput`, as the runtime does; before, only the pointer
  suite's own harness did.
- **PR 4, no pre-game stretch (from the final review, 2026-10-01; supersedes
  "`× root.CanvasScale`" in section 7).** The scale is framebuffer pixels per
  point only, rounded up to a quarter, from 1 to 4. A pre-game fixed canvas
  stretches the interface, but every screen that declares one (connecting,
  character select and creation, credits) is an opaque window over the whole
  interface, and plugin canvases sit on the overlay band at z −9000, behind
  every window. Painting at the stretch (up to 3.25 on a Retina display,
  pre-game) cost target memory and font rebakes on every quarter step of a
  window resize, all for a canvas nobody could see. Confirmed by a capture of
  the connecting screen with the demo canvas mounted (not visible). The
  review's concern about fractional scales therefore does not arise.
- **PR 4, platforms.** GLFW on Windows reports window and framebuffer sizes
  both in pixels, so `PixelScale` stays 1 at 150 % there; the plugin docs name
  a Retina Mac (or a desktop with more framebuffer pixels than points, such as
  scaled Wayland), not Windows.
- **Finding (separate fix, not PR 4).** The client's backbuffer screenshot
  (`RenderFrameOrchestrator` → `FrameScreenshotController.CapturePending`)
  passes `input.ViewportWidth/Height`, the window size in points. On a
  high-density display it fails with "The retained capture is 1600x1200;
  800x600 was requested". PR 4's captures patch this in a throwaway worktree
  only.
- **Finding (environment).** On this Mac the default `TMPDIR` makes Unix-socket
  paths longer than 104 characters, which fails Launcher pipe tests and most
  HostParity peer tests. With `TMPDIR=/tmp/`, 4–5 HostParity `Peer*ParityTests`
  still time out on clean `44a505ee`.

- **PR 5, pre-game screens (supersedes "pre-game screens" among the
  `int.MaxValue` elements in sections 1 and 8).** Only the SpewBox, the
  credits' click surface and a few non-root children are pinned at
  `int.MaxValue`. The connecting, character select, character creation and
  credits roots are raised with `BringToFront`, so in a banded scheme they
  would land in the window band, under `AboveWindows` canvases. They get a
  band of their own between those canvases and dialogs (user's choice), so
  canvases stay hidden behind pre-game screens, as PR 4's pixel scale
  assumes, while dialogs and tooltips still show above them. Order, bottom
  to top: world and world labels → `World` canvases → windows →
  `AboveWindows` canvases → screens → dialogs and tooltips → pinned
  children (the SpewBox, the credits' click surface) → menus and drag ghost.
  Pinned children are drawn in the tree walk, before the overlay pass, as
  before; the plan's Global Constraints list them last, which is wrong.
- **PR 5, bands.** `UiBands`: windows from 0 to 1,000,000,000 (exclusive);
  the layer of canvases over windows at exactly 1,000,000,000, in no band;
  screens from 1,000,000,001; dialogs and tooltips from 1,500,000,000 up to
  `int.MaxValue` (exclusive); `int.MaxValue` pinned. `BringToFront(e)`
  raises within the band `e` is already in, not "within the window band
  only": `UiRoot.OnMouseDown` raises a clicked dialog with the plain call,
  and must not drop it under the canvases. `BringToFront(e, band)` moves
  `e` into a band. Window-band members below 0 (overlays, an imported root
  one level back) keep their values until raised, as
  `UiOverlayHostTests` expects. A raise that would reach a band's ceiling
  renumbers the band from its floor in order: dialogs and tooltips are
  raised every tick (about 120 a second with one of each open), which would
  fill the dialog band in about 1,500 hours.
- **PR 5, an upper draw layer instead of moving callers to the sprite path
  (supersedes the plan task in section 8).** The audit found the live
  interface's default font is the debug `BitmapFont`
  (`InteractionRetainedUiComposition` passes `d.DebugFont`), and `UiText`,
  `UiLabel`, `UiPanel` captions, `UiMenu`, `UiField` and the markup list,
  log and toggle all fall back to it, so bitmap-font text appears in
  ordinary windows. Moving every caller would also change painter's order
  inside windows. Instead `TextRenderer` draws three layers (main, upper,
  overlay), each sprites, then rectangles, then text; the root switches to
  the upper layer before its first child at or above the canvas layer.
  Side effect: window text and rectangles no longer show through dialogs,
  tooltips and screens either. The debug accessors report main and upper
  together, so existing renderer tests are unchanged.
- **PR 5, groups.** Each plugin gets a group per layer, a `UiOverlayLayer`
  that follows the layer's size and re-ranks its canvases every tick by
  `(ZOrder, registration id)` (at most 8, and the z-order setter ignores an
  unchanged value). A group's z-order is the order it was created in;
  groups are never removed. `DrainCanvases` sorts by registration id,
  because the dictionary loses insertion order once an entry is removed. A
  layer value other than `AboveWindows` is drawn in the world layer.
- **PR 5, drag.** `PluginCanvasElement.OnHitTest` answers false while
  `UiRoot.DragSource` is set, so drag hover and drop see what is beneath.
- **PR 5, branch and merge.** Built from upstream `main` (`bbc83275`, user's
  choice), then merged into fork `main` with a merge commit, as PR 3 was. A
  scratch merge into `450dd2b9` conflicted in four files, one hunk each
  (`TextRenderer` flush loop, `RetailUiRuntime` mount, the plugin API
  guide's Canvases subsections, the markup test paragraph). PR 0, 1 and 3's
  `DrawPremultipliedSprite`, `DrawCoverageSprite`, `DrawTriangles` and two
  debug accessors merged cleanly but still named the removed fields; the
  plan's Task 8 gives every resolution. The merged tree built with 0
  warnings, passed the portable suite (baseline failures only) and the 5
  `Lane=Vulkan` canvas tests.
- **Finding (environment).** On clean `bbc83275` with `TMPDIR=/tmp/` these
  also fail: `GraphicalPluginSessionTests.ReloadCommandLoadsAFreshCopyAndTheOldOneLeavesMemory`,
  `GameWindowRenderLeafCompositionTests.PaperdollComposition_SkipsEitherMissingOptionalUiSurface`,
  `LiveEntityNetworkUpdateControllerForcePositionWiringTests.CommittedOrDeferredCellReturnsBeforeReachingTheGenericTail`,
  three `RenderPackValidatorCommandTests.External*` and
  `HeadlessSessionIsolationTests.ThirtySessionMixedWorkloadMaintainsIsolationAndConverges`,
  besides 2–6 HostParity peer tests.
- **PR 6a, the focus source (refines "bound from `UiRoot`" in section 9).**
  `AppHotkeyRegistry.Bind` takes a fourth argument, a new public
  `IHotkeyFocusSource` (`HasKeyboardFocus`, `IsModalOpen`). `GameWindow`
  passes the `RetainedUiInputCaptureSlot` it already owns: the slot is bound
  to whichever `UiRoot` is live (`InteractionRetainedUiComposition`), reports
  `false` for both while unbound, and follows an interface rebuild without the
  registry rebinding. The registry is bound before any `UiRoot` exists, so
  binding to the root directly would need a second publish step.
- **PR 6a, what "any keyboard focus" covers.** The chat bar, text fields and
  markup controls reached with Tab. Clicking a markup button, tab or toggle
  does not take focus (`FocusOnMouseClick = false`), so a click never
  silences plain hotkeys. "A focused canvas" in section 9 arrives with PR 6b,
  which focuses canvases through `UiRoot.KeyboardFocus` and so needs no
  further hotkey change; PR 6a's docs leave canvases out.
- **PR 6a, the key-order caveat in the docs is generic.** Enter and Tab, the
  keys that move focus into chat, are client bindings, so a plugin cannot bind
  them without Ctrl or Alt; the docs state the rule (a key is judged by the
  focus from before it) without a key example.
- **Finding (environment).** On clean `bbc83275` the HostParity peer tests
  failed 7 and 6 of 10 on two consecutive runs; the baseline range is 2–7.
- **PR 6b, the press (refines "a press on it" in section 10).** The root
  gives keyboard focus only on a *left* press, to an element whose
  `AcceptsFocus` is true at that moment, and before the element sees the
  press. A canvas's answer depends on the plugin's live handler and
  visibility, so `UiElement.AcceptsFocus` becomes virtual and the canvas
  computes it (opted in, key guard not tripped, handler set, shown). A
  press needs `AcceptsPointerInput` *and* a `PointerHandler`: without one
  the canvas is click-through and the press never reaches it. A right or
  middle press neither gives nor takes focus, as for every other element.
- **PR 6b, one source of focus events.** `FocusGained` and `FocusLost` reach
  the plugin only from the root's focus events, which is what guarantees
  exactly one `FocusLost` per `FocusGained`. Hiding (including a dropped
  paint callback, which hides the canvas) and removal are the root's to
  notice; the canvas itself checks every tick *and before every key* that
  it is still shown, still has a handler and no modal is open, so a key
  between a change and the next tick never reaches the plugin. When the
  handler is set to null or dropped by its guard, focus goes back with
  nobody left to tell. A canvas that is not mounted answers as a headless
  one. The runtime unbinds canvases while its root is whole, so no
  fallback `FocusLost` in `ReleaseTargets` is needed (one was prototyped,
  found unreachable, and removed).
- **PR 6b, the request.** "No rebind capture" is
  `_bindings.Keyboard?.Dispatcher?.IsCapturing`, passed to the element at
  mount. A handler that throws on `FocusGained` is dropped inside the
  root's `SetKeyboardFocus`, which then clears focus re-entrantly; the only
  `KeyboardFocusChanged` listener (`RetailWindowManager`) is unaffected,
  since canvases sit outside every window frame. Open: a request is not
  refused while a pre-game screen covers the canvases (PR 4 and 5 keep
  canvases behind those screens), so the docs tell plugins to ask in answer
  to something the player did.
- **PR 6b, keys.** Escape follows `UiField`: an Escape the handler did not
  handle gives focus back and is consumed, so the game never sees it (the
  dispatcher already saw the focus and suppressed the Escape action). A key
  `PluginKey` cannot name is consumed too. Silk hands typed text over as
  UTF-16 units (truncating above U+FFFF), so `Text` carries one valid,
  non-control `Rune`; control characters and lone surrogates are dropped,
  and Enter, Tab and Backspace arrive only as `Down`. Repeat is of the last
  key that went down, at most once per tick, and a repeated Escape follows
  the same Escape rule.
- **PR 6b, hotkeys.** PR 6a's rule needs no change (its source reads
  `UiRoot.KeyboardFocus`); the Hotkeys docs now list a focused canvas, which
  PR 6a's docs left out.
- **PR 6b, branch and merge.** Built from `fix/hotkey-focus-scope`
  (`f951ce16`), the branch it depends on, per the order of work. A scratch
  merge into fork `main` (`301852fc`) conflicted in seven files, every hunk
  "keep both" apart from the element's `ReleaseTargets` (PR 4 split it into
  `GiveBackTargets`) and the runtime's mount (PR 5's per-layer stack
  replaces `layer.AddChild`). PR 1's `fonts` parameter precedes the new
  `keyboardCaptured`, so callers pass it by name. The merged tree built with
  0 warnings and passed the portable suite (HostParity peer tests only) and
  the 5 `Lane=Vulkan` canvas tests.
- **Finding (environment).** On the PR 6b prototype the full portable suite
  failed only 3 HostParity peer tests; the other baseline failures listed
  for PR 5 passed.
- **PR 6b, the text cursor (from the final review, 2026-10-01).** Section 10's
  `IsEditControl = true` also made `CursorFeedbackController` show the
  text-edit cursor over every canvas that takes keys, focused or not, ahead
  of the combat cursor. Canvases are left out of `HoverTextEdit`: a canvas
  is an edit control only so typed text reaches it, and whether it looks
  like a text field is the plugin's to paint.
- **PR 6b, docs (from review).** A handler set to null or dropped by its
  guard gets no `FocusLost` (nobody is left to hear it); dispose and a
  dropped paint callback do deliver one. Every key `PluginKey` can name
  repeats, the last one down, so the docs tell plugins to check `IsRepeat`
  for keys that act once, and that a key held before focus can arrive as an
  `Up` with no `Down`.

## Status and carry-forward (2026-09-30)

- PR 0 pushed as `origin/painter-v2/canvas-alpha` (6 commits, reviewed).
- PR 1 pushed as `origin/painter-v2/fonts`, stacked on PR 0 (reviewed).
- PR 3 built on `painter-v2/shapes` from PR 0 (reviewed; see the PR 3 corrections above). Two contract-level changes beyond section 4: font requests that would bake inside a paint callback answer `PluginFont.None` (cache hits allowed), and font files are refused unless their sfnt header and table directory are structurally sound.

- PR 4 planned 2026-10-01: `docs/superpowers/plans/2026-10-01-painter-v2-pr4-hidpi.md`, branch `painter-v2/hidpi` from fork `main`. Verified by a prototype on 44a505ee, including 2× captures on the built-in Retina display (before: soft; after: sharp text and edges, interface font crisply pixel-doubled).
- PR 4 done 2026-10-01: `origin/painter-v2/hidpi` (12 commits, reviewed; the review's docs fixes and the pre-game stretch removal included), merged into fork `main` as 450dd2b9. Review follow-ups not taken: a per-font guard around `PrepareScale` in `OnDraw`; spreading a scale change's rebakes across frames; retrying a budget-refused font when room frees.
- PR 5 planned 2026-10-01: `docs/superpowers/plans/2026-10-01-painter-v2-pr5-layers.md`, branch `painter-v2/layers` from upstream `main` (`bbc83275`), then merged into fork `main`. Its code was prototyped on `bbc83275` and scratch-merged into `450dd2b9` before hand-off (see the PR 5 corrections above).
- PR 5 done 2026-10-01: `origin/painter-v2/layers` (8 commits: the plan's six plus a final-review fix wave — `BringToFront(e, band)` leaves the canvas layer alone; the docs say a plugin needing layers sets `minHostVersion`), merged into fork `main` as a4ab9664 (pushed). Review follow-ups not taken: an `OverlayMode = false` that restores the previous layer rather than Main; groups anchored to follow a resize at draw time instead of one tick later; a main-only test of premultiplied runs across all three layers. Group order after an interface rebuild follows the lowest surviving registration id (plan-mandated). Next: PR 6a, 6b; PR 2 still skipped.
- PR 6a planned 2026-10-01: `docs/superpowers/plans/2026-10-01-painter-v2-pr6a-hotkey-focus.md`, branch `fix/hotkey-focus-scope` from upstream `main` (`bbc83275`), then merged into fork `main`. Its code was prototyped on `bbc83275` (see the PR 6a corrections above); fork `main` touches none of its files.
- PR 6a done 2026-10-01: `origin/fix/hotkey-focus-scope` (4 commits: the plan's three plus a final-review wording fix — the docs say "Plugin hotkeys see each key…" and the `OnKeyDown` comment names where the key order comes from), merged into fork `main` as 301852fc (pushed). Review follow-ups not taken: a combined modal + focus test; a shared test helper for the duplicated keyboard and mouse fakes; a test that pins the keyboard-source-before-interface subscription order. `HasKeyboardFocus` also counts Tab-reached controls, an open searchable menu and the credits surface (all clear focus when hidden). Next: PR 6b; PR 2 still skipped.
- PR 6b planned 2026-10-01: `docs/superpowers/plans/2026-10-01-painter-v2-pr6b-keyboard.md`, branch `painter-v2/keyboard` from `fix/hotkey-focus-scope` (`f951ce16`), then merged into fork `main`. Its code was prototyped on `f951ce16`, replayed as one commit per task, and scratch-merged into `301852fc` before hand-off (see the PR 6b corrections above).
- PR 6b done 2026-10-01: `origin/painter-v2/keyboard` (8 commits: the plan's six, a docs fix from task review, and a final-review fix wave that keeps the text cursor off canvases, renames a contract test and says which keys repeat), merged into fork `main` as 84ada220 (pushed). Review follow-ups not taken: an unnamed key (Shift, Ctrl) going down does not stop a repeat; `KeyboardFocusChanged` fires out of order on a re-entrant release (only listener unaffected); a left press during a rebind capture still focuses (as `UiField`); untested: a hidden ancestor refusing a request, a handler throwing on `FocusLost`, a repeated Escape, a modal between ticks. Next: upstream PRs (deferred by the user); PR 2 still skipped.

### Inputs for the PR 4 (HiDPI) plan

- PluginFont carries LineHeight/Ascent and record equality includes them; a device-scale rebake must keep logical metrics identical.
- DrawCanvasFontPass snaps in logical pixels; must snap in device pixels.
- 64 px already needs 2048x1024; a 2x bake of 64 px default ranges won't fit 2048² and atlas bytes grow 4x against the budget — define a fallback (e.g. keep the 1x bake).
- CanvasFont does not retain managed bytes; a rebake needs them retained in the table entry.
- Bundled cache keyed by exact float size only; round sizes (e.g. 0.25 px), key by (size, scale), share one fontinfo per file.
- Optional hardening: head/hhea/maxp minimum lengths, cmap subtable and loca offsets inside their tables.

## Order of work

| PR | Branch | Topic | Depends on |
|---|---|---|---|
| 0 | `painter-v2/canvas-alpha` | Canvas blend fix | — |
| 1 | `painter-v2/fonts` | `IPluginFonts`, font-aware text | 0 |
| 2 | `painter-v2/image-regions` | Source rects, nine-slice | — |
| 3 | `painter-v2/shapes` | AA polygon, rounded rect, ellipse, strokes, gradients | 0 |
| 4 | `painter-v2/hidpi` | Physical-resolution canvas targets | 1 |
| 5 | `painter-v2/layers` | Layer + ZOrder, banded window z-order | — |
| 6a | `fix/hotkey-focus-scope` | Hotkeys respect UI focus and modals | — |
| 6b | `painter-v2/keyboard` | Canvas keyboard focus and events | 6a |

Each branch starts from `main` (or from the branch it depends on until that
merges), commits with subjects in the repo's style (`plugin canvas: …`,
`plugin api: …`), passes the portable filter from `CONTRIBUTING.md` locally plus
`Lane=Vulkan` where the PR touches rendering, and is pushed to `origin`. The
target repository for each PR is decided when it is opened.

## Test plan summary

| Area | Suites (new in bold) | Lane |
|---|---|---|
| Alpha | `PluginCanvasElementTests`, TextRenderer tests, **canvas composite readback** | portable; readback `Vulkan` |
| Fonts | **`CanvasFontBakerTests`**, **`PluginFontTableTests`**, **`BufferedUiRegistryFontsTests`**, **`ScopedUiRegistryFontsTests`**, `PluginCanvasElementTests`, headless contract | portable |
| Regions | `PluginCanvasElementTests`, `PluginImageTableTests` | portable |
| Shapes | **`CanvasGeometryTests`**, **`ColoredTriangleClipperTests`**, `PluginCanvasElementTests` | portable; optional `Vulkan` |
| HiDPI | `PluginCanvasElementTests` with fake pixel scale | portable; manual Retina check |
| Layers | `RetailWindowManager`/`UiRoot` banding tests, `BufferedUiRegistryCanvasTests`, `ScopedUiRegistryCanvasTests` | portable |
| Hotkeys | `AppHotkeyRegistryTests`, host-level focus test | portable |
| Keyboard | `PluginCanvasElementTests`, `UiRoot` tests, `ScopedUiRegistryCanvasTests`, headless contract | portable |

All portable tests use the recording GPU device and fake image/font backends, as
the existing canvas suites do; anything needing a real device carries
`Lane=Vulkan`.
