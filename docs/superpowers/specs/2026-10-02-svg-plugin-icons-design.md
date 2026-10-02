# SVG plugin icons — design

Date: 2026-10-02. Status: approved design, decisions taken (see Decisions),
awaiting spec review.

## Goal

Let a plugin give its dock buttons a vector icon that stays sharp at every
display scale and takes its colour from the plugin theme. A plugin ships
`icon.svg` beside its manifest, and any window can name its own SVG through
the descriptor. The client turns the file into a single-channel coverage
texture at the display's scale, as it does for the sharp font twin, and the
dock tints it for each state (closed, hover, open).

Today every installed plugin on the reference machine (MossTank, GoArrow,
Golem) falls back to a DAT sprite or initials, because none ships an
`icon.png`. A PNG is a fixed 64×64 RGBA bitmap: it cannot follow Moss or
Brass, and it is resampled at every scale except one.

## Constraints

- **Additive plugin API.** One new optional `init` property on
  `PluginPanelDescriptor`. A plugin built against the current contract
  compiles and loads unchanged (README: "Changes to the contract are
  additive").
- **No new dependency.** Rasterize with `StbTrueType.stbtt_Rasterize` from
  StbTrueTypeSharp 1.26.12, which `AcDream.App` already references for fonts.
- **Never fail a plugin.** A bad icon file only means the next icon in the
  fallback chain is drawn. The plugin and its windows load as before.
- **Mono-colour.** An icon is coverage only. Colour comes from the caller.
- **Fork first.** Same delivery rules as painter v2: the feature branch can go
  upstream later, and this spec stays on the fork-only docs branch.

## Non-goals

- Full-colour SVG, gradients, patterns, masks, clip paths, filters, text,
  embedded images, `<use>`, CSS stylesheets, animation.
- SVG in the launcher's plugin list (it reads `icon.png` itself) and in the
  release catalog (`plugin-manifest.md`, the icon copy beside the zip).
- SVG in panel markup (`<icon>` / `UiMarkupIcon`). The rasterizer could
  serve it later because `IMarkupIconResolver` already returns
  `(tex, w, h)`, but markup icons are RGBA DAT art today, so that needs a
  separate tint design.
- Dock layout and state colours. The dock redesign spec owns those. This spec
  provides an icon that can be drawn in any colour.

## How icons flow today

- `PluginPanelDescriptor` (`IUiRegistry.cs`): `IconText` (initials) and
  `IconSurfaceId` (DAT id, normalized by `PluginIcons.Normalize`).
- `PluginIconFile` (`AcDream.Core/Plugins`): `icon.png` at the plugin root.
  It must be exactly 64×64, at most 64 KiB, not animated, and fully
  header-checked. `TryLoad` never throws. `LauncherPluginIcon` is the
  launcher's own copy of the same rules, pinned to it by
  `PluginIconParityTests`.
- `RetailUiRuntime.ResolvePluginFileIcon(pluginId, pluginDirectory)` uploads
  the PNG once per plugin id into `_pluginIcons` and never releases or
  refreshes it.
- `PluginSidePanel.PluginShelfButton` draws the file icon if there is one,
  else the DAT surface, else the initials. That is the order
  `docs/plugin-ui-markup.md` documents ("the plugin's own `icon.png` … else
  `IconSurfaceId`, else the initials").
- **Launcher content policy** (`PluginContentPolicy.AllowedExtensions`):
  `.dll .pdb .json .xml .txt .md .png .jpg .jpeg .ttf .otf`. **`.svg` is not
  allowed.** A launcher-installed plugin whose zip carries an `.svg` is
  rejected with "has a disallowed extension". This PR must add `.svg`
  (see Plugin-facing API), even though the launcher never draws it.

## Plugin-facing API

```csharp
public sealed record PluginPanelDescriptor(string WindowId, string Title)
{
    // existing: IconText, IconSurfaceId, StartVisible, ShowInSidePanel

    /// <summary>
    /// An SVG icon for this window's dock button: a path relative to the
    /// plugin's install folder, such as "icons/loot.svg". Null uses the
    /// plugin's own icon.svg, if it ships one. A file that is missing or
    /// outside the supported subset falls back to the next icon.
    /// </summary>
    public string? IconFile { get; init; }
}
```

- **Plugin-level icon:** `icon.svg` at the plugin root (beside `icon.png`
  and `plugin.json`), found by name like `icon.png`.
- **Path rules for `IconFile`:** relative, with forward or back slashes, and
  an `.svg` extension (case-insensitive). The path is normalized against the
  plugin directory and must stay inside it after symlinks are resolved
  (`FileSystemInfo.ResolveLinkTarget(returnFinalTarget: true)`). Rooted
  paths, drive letters, `..` that escapes the folder, and links that point
  out of it are rejected. If the registration has no plugin directory
  (`PluginDirectory == null`), `IconFile` is ignored.
- **Headless host:** accepts the property and ignores it, like
  `IconSurfaceId`.
- **Launcher / PluginCheck:** add `.svg` to `AllowedExtensions`. PluginCheck
  adds an advisory item, "icon.svg present, N bytes", with no parse. Both
  enforce the 16 KiB limit on `.svg` files. The client does the real parse;
  a file it rejects only falls back to the next icon.
- **Docs:** `docs/plugin-ui-markup.md` (icon order, `IconFile`, the subset),
  `docs/plugin-development.md` and `docs/plugin-manifest.md` (zip may carry
  `.svg` files).

## File format: the supported subset

The parser accepts a file only if everything in it is understood. Anything
that would change what is drawn and is not supported rejects the whole file,
so we never draw an icon wrong.

**Document**

- UTF-8, at most **16 KiB**. Root element `<svg>`, in the SVG namespace or in
  no namespace.
- `viewBox="minX minY w h"` with `w, h > 0`. Without a viewBox,
  `width`/`height` give `0 0 w h` (unitless or `px` only). Without either,
  the file is rejected.
- The icon is fitted into a square and centred (`xMidYMid meet`), whatever
  `preserveAspectRatio` says. `width`/`height` are otherwise ignored.
- Limits: at most 256 elements, nesting depth 8, 4,096 path commands, and
  65,536 flattened points across the file.

**Elements**

- Drawn: `path`, `circle`, `ellipse`, `rect` (with `rx`/`ry`), `line`,
  `polyline`, `polygon`.
- Grouping: `g`.
- Skipped with their content: `title`, `desc`, `metadata`. An empty `defs`
  is skipped too.
- Rejected: everything else, including `use`, `symbol`, `image`, `text`,
  `style`, `script`, `foreignObject`, `clipPath`, `mask`, `pattern`,
  gradients, filters, `switch`, `a`, and any non-empty `defs`.

**Attributes**

- Presentation properties, given as attributes or in a `style="a:b; c:d"`
  list. All are inherited through `g`.
  - `fill`, `stroke`: `none`, `currentColor`, or any colour (named, `#rgb`,
    `#rrggbb`, `rgb()`). Any colour means "ink". The hue is ignored and the
    file stays mono.
  - `fill-opacity`, `stroke-opacity`, `opacity`: 0..1, multiplied into
    coverage. Group `opacity` is applied per child, an approximation that
    is noted in the docs.
  - `stroke-width`: user units, default 1.
  - `stroke-linecap`: `butt` | `round` | `square`.
  - `stroke-linejoin`: `miter` | `round` | `bevel`.
  - `stroke-miterlimit`: default 4.
  - `fill-rule`: `nonzero` only. `evenodd` is rejected (see Rasterizer facts).
- `transform` on any drawn element or `g`: `matrix`, `translate`, `scale`,
  `rotate(a [cx cy])`, `skewX`, `skewY`.
- Ignored: `id`, `class`, `xmlns*`, `version`, `aria-*`, `data-*`,
  `stroke-dasharray`/`-dashoffset` when `none`, and `vector-effect`. A
  non-`none` `stroke-dasharray` rejects the file.
- `fill="url(#…)"` or any paint server is rejected.
- SVG defaults apply: `fill` defaults to ink (black), `stroke` to `none`.

**Path data:** the full SVG 1.1 grammar, `M L H V C S Q T A Z`, both
absolute and relative. This includes implicit repeated commands, compact
numbers (`1.5.5`, `-.5-1`), exponents, and packed arc flags (`a1 1 0 011 1`).
A malformed path rejects the file.

**XML safety:** `XmlReaderSettings { DtdProcessing = Prohibit, XmlResolver =
null, MaxCharactersFromEntities = 0, MaxCharactersInDocument = 16384,
IgnoreComments = true, IgnoreProcessingInstructions = true }`. The file is
read from bytes already capped at 16 KiB, so the reader never touches the
file system or the network.

## Rasterizer facts (verified 2026-10-02 against StbTrueTypeSharp 1.26.12)

Reflection and a throwaway probe in the session scratchpad confirmed:

- `public static unsafe void StbTrueType.stbtt_Rasterize(stbtt__bitmap*
  result, float flatness_in_pixels, stbtt_vertex* vertices, int num_verts,
  float scale_x, float scale_y, float shift_x, float shift_y, int x_off,
  int y_off, int invert, void* userdata, bool useOldRasterizer)` is public.
- `stbtt__bitmap { int w, h, stride; byte* pixels; }` is public. **The
  caller allocates** the pixel buffer: pin a `byte[]`.
- `stbtt_vertex { short x, y, cx, cy, cx1, cy1; byte type; byte padding; }`.
  Types are `vmove = 1`, `vline = 2`, `vcurve = 3` (quadratic) and
  `vcubic = 4`. **Coordinates are `Int16`.** `stbtt_setvertex` does not set
  `cx1/cy1`, so cubic vertices are written field by field.
- **Cubics work** (probe: a cubic lobe renders correctly). **`invert = 0` is
  y-down**, SVG's own orientation. `invert = 1` flips the shape out of a
  bitmap with no y offset.
- **Winding:** coverage is the absolute value of the signed winding sum,
  clamped to 255. That is SVG `nonzero`. Two overlapping same-direction
  contours give a union. A nested opposite-direction contour gives a hole. A
  nested same-direction contour stays filled. A lone contour fills whichever
  way it runs. So `evenodd` cannot be passed straight through.
- Edges get analytic area coverage (the "new" rasterizer,
  `useOldRasterizer: false`), so pieces that touch edge to edge leave no
  seam.

**Fixed point:** outlines are transformed to device pixels first, then
multiplied by 64. `scale_x = scale_y = 1/64`, `shift = 0`. The largest bake
(see Caching) is 128 device px, so 128 × 64 = 8,192, which is well inside
`Int16`. Points outside −4..(size + 4) px are clamped before conversion, so
a stray huge coordinate cannot overflow.

## Pipeline

All of it runs on the UI thread at first draw, and only once per icon and
device size.

1. **Load** (`AcDream.Core/Plugins/PluginSvgIcon.cs`, pure, no GL): read at
   most 16 KiB + 1 bytes, check the size, then parse the XML with the
   settings above. Walk the element tree with inherited style and the
   current transform. The output is `SvgIconDocument`: the viewBox plus a
   list of **paint layers**, each `(kind: Fill | Stroke, opacity,
   subpaths, stroke style, transform)`. Rejects return a reason string.
2. **Outline** (`AcDream.Core/Plugins/SvgOutline.cs`, pure): convert every
   element to path segments: lines, quadratics and cubics.
   - `circle`/`ellipse` become 4 cubics (k = 0.5523), and `rect` with
     radii becomes lines plus 4 cubics.
   - `A` (arc) uses SVG 1.1 implementation notes F.6.5 (endpoint to
     centre), with F.6.6 out-of-range radii scaled up and zero radii drawn
     as a line. The arc is split into segments of ≤ 90°, each one cubic.
   - `S`/`T` reflect the previous control point.
3. **Stroke expansion** (`SvgStroker`, pure): a stroke is turned into
   fillable contours in local (pre-transform) space, then transformed, so
   any affine transform (including non-uniform scale and skew) is exact.
   - Flatten each subpath to a polyline. The tolerance is 0.2 device px,
     converted to local units by the layer's total scale.
   - Each segment becomes a rectangle `w` wide.
   - Joins between consecutive segments:
     - `round`: a circle polygon at the vertex.
     - `bevel`: the triangle between the two outer offset corners.
     - `miter`: that triangle plus the miter tip while
       `1/sin(θ/2) ≤ miterlimit`, else bevel.
   - Caps on open subpaths: `butt` adds nothing, `round` adds a half circle
     (a full circle polygon), and `square` extends the end segments by `w/2`.
     A closed subpath (`Z`) gets a join at its closing vertex and no caps.
   - A zero-length subpath with round or square caps becomes a dot, as in
     browsers. With butt caps it draws nothing.
   - **Every piece is oriented to positive signed area** before
     rasterizing, so the pieces union under the nonzero rule instead of
     cancelling.
4. **Rasterize** (`AcDream.App/UI/SvgIconRasterizer.cs`, the only part
   that touches stb): bake each paint layer into its own `byte[]` of
   `size × size`. Fill layers pass curves straight to stb as
   `vcurve`/`vcubic` with `flatness_in_pixels = 0.35`. Stroke layers pass
   their already-flat contours. Layers are composited in document order with
   coverage source-over, `c = c + a·(1 − c)`, where `a` is the layer's
   coverage times its opacity. Separate passes are needed because a fill's
   hole orientation must not cancel a stroke that crosses it.
5. **Upload** through the existing seam `IPluginFontBackend.UploadCoverage
   (coverage, w, h, debugName)` (`RetailPluginFontBackend`, which calls
   `TextureCache.UploadReleasableCoverage8`). It is given back with
   `ReleaseCoverage`. Unit tests use a fake backend, as the font table tests
   do.
6. **Draw** with `UiRenderContext.DrawCoverageSprite(tex, x, y, extent,
   extent, 0, 0, 1, 1, colour)`, where `extent = devicePx / ctx.PixelScale`
   and `x, y` are snapped to the device grid (as the sharp font baseline
   is), so each texel lands on one device pixel.

## Caching and rescale

- `PluginSvgIconCache` (App) is owned by `RetailUiRuntime` and keyed by
  **resolved file path + length + last-write time**. A plugin hot reload
  that changes the file therefore loads it again. The PNG cache's
  "never refresh" behaviour is left alone: it is noted, not fixed here.
- Each entry holds the parsed `SvgIconDocument`, the reject reason if it
  failed, a reference count of dock buttons using it, and the baked textures
  by device pixel size.
- **Device size:** `ceil(extentPoints × ctx.PixelScale)`, capped at 128.
  `PixelScale` already moves in quarter steps (`CanvasPixelScale.Step`), so
  sizes are few. A bake happens lazily on the first draw at a new size, like
  `BundledUiFont.SharpSource`.
- An entry keeps **at most two sizes**. A third size releases the least
  recently drawn one, which covers dragging the window between a 1× and a 2×
  display without growing.
- **Release:** when a dock button is removed (`OnWindowUnregistered`,
  plugin disable or reload, dock dispose), its reference is dropped. At zero
  references every texture is released through `ReleaseCoverage` and the
  entry is removed.

## Precedence and fallback

Per dock button, the first one that resolves wins:

1. The window's `IconFile` SVG.
2. The plugin's `icon.svg`.
3. The plugin's `icon.png` (unchanged behaviour).
4. `IconSurfaceId` (DAT).
5. Initials from `IconText` / the title. The dock spec draws these as a
   monogram.

SVG resolution happens at `PluginSidePanel.Add` (parse) and first draw
(bake). If a bake fails (upload returns 0), the button marks it failed and
uses the next source. It never retries every frame.

**Classic** uses the same order. The dock redesign brings Classic into the
new dock with its own black-and-gold colour set, so a Classic dock tints the
SVG from that set exactly as Moss and Brass tint it from theirs.

## Failure handling

- A parse or path rejection logs once per resolved file per session, in the
  existing style:
  `[UI] plugin icon 'acdream.mosstank/icons/loot.svg' ignored: <reason>`.
  Reasons are short and specific, for example "uses <mask>, which plugin
  icons do not support", "is larger than 16 KiB", "path data is malformed
  at offset 212", or "points outside the plugin folder".
- Nothing throws out of the loader or the cache. Everything is caught at the
  cache boundary and becomes a fallback, as with `PluginIconFile.TryLoad`.
- Malformed numbers, NaN or ∞ coordinates, and transforms with a zero
  determinant reject the file.

## Theme tinting

The icon has no colour of its own. The dock asks for a colour per state from
the active palette, or from the dock's Classic colour set. Proposed (the
dock spec decides): closed `Muted`, hover `Text`, open `Accent`, disabled
`Muted` at 45%. The same `icon.svg` therefore follows Classic, Moss and Brass
and every state with no extra files.

## Testing

- **`AcDream.Core.Tests/Plugins/PluginSvgIconTests.cs`** (parser): an
  accept/reject table with one case per rejected element, DTD, entity,
  over-size file, nesting/element/command limits, `url()` paint, `evenodd`
  and dasharray. Path grammar cases cover implicit repeats, relative
  commands, compact numbers, exponents and packed arc flags. Style inherits
  through `g`, `style=""` parses, and colour becomes ink.
- **`SvgOutlineTests`:** an arc's endpoints and midpoint against known
  values, including large-arc/sweep flag combinations, radius scale-up, and
  zero radius. Circle and rect cubic control points. A transform is applied
  after stroke expansion (non-uniform scale gives an elliptical stroke).
- **`SvgStrokerTests`:** every piece has positive area. Join and cap counts
  per style. Miter falls back to bevel past the limit. A zero-length subpath
  draws a dot only with round or square caps.
- **`AcDream.App.Tests/UI/SvgIconRasterizerTests.cs`:**
  - Golden coverage for a filled square: 255 inside and exact fractional
    edges.
  - A ring with an opposite-direction hole.
  - A stroked "X" with round caps, compared to a checked-in 24×24 golden PGM
    within ±2 per pixel.
  - The same icon at 16, 24 and 48 px.
  - Fill with a hole plus a stroke across the hole, which proves the
    separate-pass compositing.
- **`PluginSvgIconCacheTests`** (fake `IPluginFontBackend`):
  - One bake per size.
  - The two-size limit releases the oldest.
  - Unregistering the last button releases all of its textures.
  - Changing the file's length or time reloads it.
  - A failed upload falls back and is not retried.
- **`PluginSidePanelTests`:** the five-step precedence, an `IconFile`
  outside the folder being rejected, and a Classic dock drawing the SVG in
  its Classic colours.
- **Launcher:** `PluginContentPolicy` accepts `.svg` up to 16 KiB and
  rejects a larger one. `PluginCheck` reports it.
- **Visual gate:** a 2× backbuffer capture of the dock with the
  ThemeGallery sample carrying an `icon.svg`, following the retina capture
  workflow. Optionally, a `Lane=Vulkan` offscreen capture at 1× and 2×, kept
  out of the repo unless the user asks.
- **Sample:** `samples/AcDream.Plugins.ThemeGallery` gets an `icon.svg` and
  one window with its own `IconFile`.

## Delivery

- **One PR, branch `plugin-icons/svg`.** It needs `UiRenderContext.PixelScale`
  and `DrawCoverageSprite` (painter-v2 hidpi and fonts) and the themed dock
  palette (modern theme), and only fork main has all of these. So, like
  `painter-v2/hidpi`, it branches **from fork main**, and the upstream PR
  waits for those to land upstream.
- The order with the dock redesign is free. If SVG lands first, the current
  tile draws the icon in `Text` (`Accent` when open). If the dock lands
  first, it calls the same draw helper. The shared seam is one method,
  `PluginShelfButton.DrawIcon(ctx, x, y, extent, colour)`, so the dock work
  must not hard-code the PNG path.
- The upstream PR must say: a new descriptor property, `.svg` allowed in
  plugin zips, and a changed icon order (an SVG now beats `icon.png`).
- This spec lives on the fork-only docs branch, never on the feature branch.

## Decisions (2026-10-02)

1. **Classic** draws the SVG in the dock's Classic colours (it shares the
   new dock with Moss and Brass).
2. **`evenodd`** rejects the file, with a clear log line. Most icon sets use
   `nonzero`.
3. **Launcher validation** only allows the `.svg` extension and checks the
   16 KiB size. There is no second parser copy.
4. **Size limit:** 16 KiB.
5. **Group opacity** is applied to each child, an approximation documented
   in `plugin-ui-markup.md`.
