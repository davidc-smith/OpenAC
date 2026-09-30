# Painter v2 — PR 3: Shapes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plugin canvases can draw anti-aliased convex polygons (with per-corner colours), rounded rectangles, ellipses and circles (filled and stroked), and two-colour rectangle gradients.

**Architecture:** New default members on `IPluginPainter`. The host tessellates every shape on the CPU (`CanvasGeometry`) into untextured triangles with a colour per vertex. Each edge gets a fringe one device pixel wide, centred on the true edge, whose outer vertices are transparent, so the rasteriser's linear interpolation does the anti-aliasing. The triangles are clipped with colour interpolation (`ColoredTriangleClipper`) and appended to the interface renderer's untextured sprite run (`TextRenderer.DrawTriangles`), so painter's order with fills and images holds. There is a per-paint vertex budget; going over it counts as a paint overrun. No shader changes. PR 0's blend fix is what makes the transparent fringes composite correctly.

**Tech Stack:** C# / .NET 10, `System.Numerics`, xUnit, the repository's `RecordingGpuDevice`, and MoltenVK for the local `Lane=Vulkan` run.

**Spec:** `docs/superpowers/specs/2026-09-30-plugin-painter-v2-design.md` (section 6, and "Plan-time corrections", whose PR 3 entries supersede section 6 where they differ).

**Checked:** this plan's code was applied to `painter-v2/canvas-alpha` in a throwaway worktree before hand-off. It built with 0 warnings; every new test passed, as did the existing canvas, guard and blend suites; the `Lane=Vulkan` tests passed under MoltenVK. The counts and areas the tests expect are measured values.

## Global Constraints

- Branch `painter-v2/shapes`. It depends on PR 0 only. Create it from `upstream/main` if upstream has merged PR 0; otherwise create it from `painter-v2/canvas-alpha`. **Never** create it from the fork's `main`, which also carries PR 1 (fonts). Nothing under `docs/superpowers/` goes on a code branch.
- `AcDream.Plugin.Abstractions` stays additive: every new painter member is a default interface member that draws nothing (except `FillCircle`, whose default forwards to `FillEllipse`). Every public type and member has XML docs; an undocumented member fails the build.
- Exact values: polygons have 3–64 points. Arc tolerance is 0.1 device pixel. A corner gets at most 32 chords; an ellipse gets 8–256 chords, rounded up to a multiple of 4. The fringe is one device pixel, split half inside and half outside the edge. One device pixel is one canvas pixel in this PR (`PluginPainter.DevicePixel = 1f`). `PluginPainter.MaximumShapeVerticesPerPaint = 32_768`.
- Existing primitives (`Clear`, `FillRect`, `StrokeRect`, `DrawLine`, text, images, clips) are unchanged: every existing test passes unmodified.
- Bad input never throws. It draws nothing and is reported once per canvas. A shape with no area, a zero thickness, or a collinear polygon draws nothing and is **not** reported.
- No shader source or SPIR-V changes.
- Commit subjects: `plugin api: …`, `plugin canvas: …`, `gpu: …`, `ui: …`, `tests: …`, `docs: …`. Every commit message ends with `Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB`.
- Environment for every command: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. `Lane=Vulkan` commands also need `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib`.
- Tests run in the machine's culture, and this Mac uses a decimal comma. Any report text that contains a number is built with `CultureInfo.InvariantCulture`.
- Builds on this Mac rewrite `src/AcDream.Launcher/packages.neutral.lock.json`, and an unlocked restore touches `packages.osx-arm64.lock.json` files. Never stage a `packages.*.lock.json`. Stage files by explicit path, and run `git checkout -- '*.lock.json'` before pushing.
- Portable gate filter: `Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure`.

**User decisions (already made):**
- "One spec, a PR series"; this is PR 3 (shapes), started on request ("lets start on the work for PR3").
- "Keep it on our repo only": specs and plans stay on the fork's docs branch.
- AA approach "CPU feathered geometry": no shader change.
- Branches are pushed to `origin`. Do not open a PR without asking which repository it goes to.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AcDream.Plugin.Abstractions/PluginShapes.cs` | Create | `PluginCornerRadii`, `PluginGradientDirection`. |
| `src/AcDream.Plugin.Abstractions/PluginCanvas.cs` | Modify | Eight shape default members on `IPluginPainter`; painter doc paragraph. |
| `tests/AcDream.Plugin.Tests/PluginCanvasShapesContractTests.cs` | Create | Inert defaults, the circle forward, radii helper. |
| `src/AcDream.App/Rendering/ColoredTriangleClipper.cs` | Create | `UiColorVertex`; one triangle clipped to a rect, colour interpolated. |
| `tests/AcDream.App.Tests/Rendering/ColoredTriangleClipperTests.cs` | Create | |
| `src/AcDream.App/Rendering/TextRenderer.cs` | Modify | `DrawTriangles` into the untextured sprite run. |
| `src/AcDream.App/UI/UiRenderContext.cs` | Modify | `DrawTriangles`: origin, alpha, clip. |
| `tests/AcDream.App.Tests/Rendering/TextRendererTrianglesTests.cs` | Create | |
| `tests/AcDream.App.Tests/UI/UiRenderContextTrianglesTests.cs` | Create | |
| `src/AcDream.App/UI/CanvasGeometry.cs` | Create | Pure tessellation with the AA fringe; `CanvasShapeOutcome`, `CanvasCornerRadii`. |
| `tests/AcDream.App.Tests/UI/CanvasGeometryTests.cs` | Create | |
| `src/AcDream.App/UI/UiDrawCallbackGuard.cs` | Modify | Optional "over another budget" predicate counts as an overrun. |
| `tests/AcDream.App.Tests/UI/UiDrawCallbackGuardTests.cs` | Modify | |
| `src/AcDream.App/UI/Layout/PluginPainter.cs` | Modify | Shape members, validation, vertex budget, `CanvasShapeProblem`. |
| `src/AcDream.App/UI/Layout/PluginCanvasSurface.cs` | Modify | Pass the problem sink; ask the guard about the budget. |
| `src/AcDream.App/UI/Layout/PluginCanvasElement.cs` | Modify | Report shape problems once per canvas. |
| `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs` | Modify | `partial` (one word). |
| `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.Shapes.cs` | Create | Order, clipping, report-once, budget, kept painter. |
| `tests/AcDream.App.Tests/Rendering/Gpu/Vk/PluginCanvasShapeOffscreenTests.cs` | Create | `Lane=Vulkan` pixel proof of the fringe. |
| `docs/plugin-api.md`, `docs/plugin-ui-markup.md` | Modify | `### Shapes` under Canvases; test list. |

---

### Task 0: Create the branch

**Goal:** A code branch that contains PR 0 and not PR 1.

**Files:** none

**Acceptance Criteria:**
- [ ] `git branch --show-current` prints `painter-v2/shapes`
- [ ] The branch contains PR 0's commit "plugin canvas: translucent content is shown at the alpha it was painted with"
- [ ] The branch does not contain `src/AcDream.Plugin.Abstractions/PluginFonts.cs` (PR 1)
- [ ] `docs/superpowers` does not exist in the working tree

**Verify:** `git branch --show-current && git log --oneline -30 | grep -c "shown at the alpha it was painted with" && test ! -e src/AcDream.Plugin.Abstractions/PluginFonts.cs && test ! -e docs/superpowers && echo clean` → `painter-v2/shapes`, `1`, `clean`

**Steps:**

- [ ] **Step 1: Branch**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git fetch origin && git fetch upstream
if git log --oneline upstream/main | grep -q "shown at the alpha it was painted with"; then
  git switch -c painter-v2/shapes upstream/main
else
  git switch -c painter-v2/shapes origin/painter-v2/canvas-alpha
fi
test ! -e src/AcDream.Plugin.Abstractions/PluginFonts.cs && test ! -e docs/superpowers && echo clean
```

Expected: `clean`.

- [ ] **Step 2: Baseline build**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | tail -3`
Expected: `0 Warning(s)`, `0 Error(s)`.

```json:metadata
{"files": [], "verifyCommand": "git branch --show-current", "acceptanceCriteria": ["on painter-v2/shapes", "contains PR 0", "no PluginFonts.cs", "no docs/superpowers"], "modelTier": "mechanical"}
```

---

### Task 1: The shapes contract

**Goal:** `PluginCornerRadii`, `PluginGradientDirection`, and eight default members on `IPluginPainter`. The defaults draw nothing, except `FillCircle`, which forwards to `FillEllipse`.

**Files:**
- Create: `src/AcDream.Plugin.Abstractions/PluginShapes.cs`
- Modify: `src/AcDream.Plugin.Abstractions/PluginCanvas.cs:155` (painter doc: new paragraph after this line), `:241` (members after `void PopClip();`)
- Test: `tests/AcDream.Plugin.Tests/PluginCanvasShapesContractTests.cs` (create)

**Acceptance Criteria:**
- [ ] A painter implementing only the pre-shapes members compiles, and every shape call on it draws nothing and does not throw
- [ ] `FillCircle(center, r, c)` on a painter that implements only `FillEllipse` calls it with `(cx − r, cy − r, 2r, 2r)` and the same colour
- [ ] `PluginCornerRadii.Uniform(6) == new(6, 6, 6, 6)`; `Horizontal == 0`, `Vertical == 1`
- [ ] The Abstractions build has 0 warnings (every new member documented)

**Verify:** `dotnet test tests/AcDream.Plugin.Tests -c Release --filter "FullyQualifiedName~PluginCanvas"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing test** (`tests/AcDream.Plugin.Tests/PluginCanvasShapesContractTests.cs`)

```csharp
// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;

namespace AcDream.Plugin.Tests;

/// <summary>
/// Shapes are additive: a painter written before them still builds and
/// accepts every shape call, drawing nothing; a circle is the ellipse in
/// its square.
/// </summary>
public sealed class PluginCanvasShapesContractTests
{
    /// <summary>A painter that implements only what the contract had before shapes.</summary>
    private class OlderPainter : IPluginPainter
    {
        public int Calls { get; private set; }

        public int Width => 10;

        public int Height => 10;

        public void Clear(PluginColor color) => Calls++;

        public void FillRect(PluginRect rect, PluginColor color) => Calls++;

        public void StrokeRect(PluginRect rect, PluginColor color, float thickness = 1f) => Calls++;

        public void DrawLine(PluginPoint from, PluginPoint to, PluginColor color, float thickness = 1f) => Calls++;

        public void DrawText(string text, PluginPoint position, PluginColor color, bool outline = false) => Calls++;

        public PluginSize MeasureText(string text) => default;

        public void DrawImage(PluginImage image, PluginRect destination, PluginColor tint) => Calls++;

        public void DrawImageTransformed(
            PluginImage image, PluginRect destination, PluginColor tint, double rotationRadians,
            PluginPoint pivot, double scaleX = 1.0, double scaleY = 1.0) => Calls++;

        public void PushClip(PluginRect rect) => Calls++;

        public void PopClip() => Calls++;
    }

    /// <summary>A painter that draws ellipses and nothing else new.</summary>
    private sealed class EllipsePainter : OlderPainter, IPluginPainter
    {
        public List<(PluginRect Bounds, PluginColor Color)> Ellipses { get; } = [];

        public void FillEllipse(PluginRect bounds, PluginColor color) => Ellipses.Add((bounds, color));
    }

    [Fact]
    public void APainterWrittenBeforeShapesAcceptsEveryShapeAndDrawsNothing()
    {
        var older = new OlderPainter();
        IPluginPainter painter = older;
        var rect = new PluginRect(1, 2, 3, 4);

        painter.FillPolygon([new PluginPoint(0, 0), new PluginPoint(4, 0), new PluginPoint(0, 4)], PluginColor.White);
        painter.FillPolygon(
            [new PluginPoint(0, 0), new PluginPoint(4, 0), new PluginPoint(0, 4)],
            [PluginColor.White, PluginColor.White, PluginColor.Transparent]);
        painter.FillRoundedRect(rect, PluginCornerRadii.Uniform(2), PluginColor.White);
        painter.StrokeRoundedRect(rect, PluginCornerRadii.Uniform(2), PluginColor.White, 2f);
        painter.FillEllipse(rect, PluginColor.White);
        painter.StrokeEllipse(rect, PluginColor.White);
        painter.FillCircle(new PluginPoint(5, 5), 3, PluginColor.White);
        painter.FillRectGradient(rect, PluginColor.White, PluginColor.Transparent, PluginGradientDirection.Vertical);

        Assert.Equal(0, older.Calls);
    }

    [Fact]
    public void ACircleIsTheEllipseInItsSquare()
    {
        var ellipses = new EllipsePainter();
        IPluginPainter painter = ellipses;

        painter.FillCircle(new PluginPoint(10, 20), 5, new PluginColor(1, 2, 3, 4));

        Assert.Equal((new PluginRect(5, 15, 10, 10), new PluginColor(1, 2, 3, 4)), Assert.Single(ellipses.Ellipses));
    }

    [Fact]
    public void UniformRadiiAreTheSameAtEveryCornerAndGradientsRunTwoWays()
    {
        Assert.Equal(new PluginCornerRadii(6, 6, 6, 6), PluginCornerRadii.Uniform(6));
        Assert.Equal(new PluginCornerRadii(0, 0, 0, 0), default(PluginCornerRadii));
        Assert.Equal(0, (int)PluginGradientDirection.Horizontal);
        Assert.Equal(1, (int)PluginGradientDirection.Vertical);
    }
}
```

- [ ] **Step 2: Run to see it fail**

Run: `dotnet build tests/AcDream.Plugin.Tests -c Release 2>&1 | grep -m3 error`
Expected: `error CS0246: The type or namespace name 'PluginCornerRadii' could not be found` (or CS1061 for `FillPolygon`).

- [ ] **Step 3: The types** (`src/AcDream.Plugin.Abstractions/PluginShapes.cs`)

```csharp
namespace AcDream.Plugin.Abstractions;

/// <summary>
/// The radius of each corner of a rounded rectangle, in pixels. Zero gives
/// a square corner. Radii too large for the rectangle are scaled down
/// together, keeping their proportions, until neighbouring corners fit
/// along the side they share.
/// </summary>
/// <param name="TopLeft">The top-left corner's radius.</param>
/// <param name="TopRight">The top-right corner's radius.</param>
/// <param name="BottomRight">The bottom-right corner's radius.</param>
/// <param name="BottomLeft">The bottom-left corner's radius.</param>
public readonly record struct PluginCornerRadii(double TopLeft, double TopRight, double BottomRight, double BottomLeft)
{
    /// <summary>The same radius at all four corners.</summary>
    /// <param name="radius">The radius, in pixels.</param>
    /// <returns>Radii with <paramref name="radius"/> at every corner.</returns>
    public static PluginCornerRadii Uniform(double radius) => new(radius, radius, radius, radius);
}

/// <summary>Which way a gradient runs across a rectangle.</summary>
public enum PluginGradientDirection
{
    /// <summary>From the left edge, in the first colour, to the right edge, in the second.</summary>
    Horizontal,

    /// <summary>From the top edge, in the first colour, to the bottom edge, in the second.</summary>
    Vertical,
}
```

- [ ] **Step 4: The painter doc** (`PluginCanvas.cs`)

After the line `/// callback: keeping it and drawing later throws.` (line 155) insert, leaving the existing `///` line and text paragraph that follow untouched:

```csharp
///
/// <para>Shapes -- polygons, rounded rectangles, ellipses, circles and
/// gradients -- have edges anti-aliased over one pixel; the other
/// primitives have hard edges. A shape given input it cannot draw draws
/// nothing and never throws; the client's log says so once per canvas.</para>
```

- [ ] **Step 5: The members** (`PluginCanvas.cs`, after `void PopClip();`, still inside `IPluginPainter`)

```csharp

    /// <summary>
    /// Fills a convex polygon in one colour, with anti-aliased edges. The
    /// points go round the outline in order, either way round, 3 to 64 of
    /// them. Too few or too many points, or an outline that is not convex,
    /// draws nothing. A host that predates shapes draws nothing.
    /// </summary>
    /// <param name="points">The polygon's corners, in order round its outline.</param>
    /// <param name="color">The fill colour.</param>
    void FillPolygon(ReadOnlySpan<PluginPoint> points, PluginColor color)
    {
    }

    /// <summary>
    /// Fills a convex polygon with a colour at each corner, blended across
    /// the polygon, with anti-aliased edges; otherwise as the one-colour
    /// overload. A colour count that differs from the point count draws
    /// nothing. A host that predates shapes draws nothing.
    /// </summary>
    /// <param name="points">The polygon's corners, in order round its outline.</param>
    /// <param name="colors">One colour per corner, in the same order as the points.</param>
    void FillPolygon(ReadOnlySpan<PluginPoint> points, ReadOnlySpan<PluginColor> colors)
    {
    }

    /// <summary>
    /// Fills a rectangle with rounded corners and anti-aliased edges. A host
    /// that predates shapes draws nothing.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="radii">Each corner's radius; see <see cref="PluginCornerRadii"/>.</param>
    /// <param name="color">The fill colour.</param>
    void FillRoundedRect(PluginRect rect, PluginCornerRadii radii, PluginColor color)
    {
    }

    /// <summary>
    /// Draws a rounded rectangle's outline, centred on the outline so half
    /// the thickness falls outside the rectangle, with anti-aliased edges.
    /// Outside a square corner the outline stays square. A host that
    /// predates shapes draws nothing.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="radii">Each corner's radius; see <see cref="PluginCornerRadii"/>.</param>
    /// <param name="color">The outline colour.</param>
    /// <param name="thickness">The outline's width in pixels; thinner than one pixel is drawn one pixel wide and proportionally fainter.</param>
    void StrokeRoundedRect(PluginRect rect, PluginCornerRadii radii, PluginColor color, float thickness = 1f)
    {
    }

    /// <summary>
    /// Fills the ellipse that fits a rectangle, with an anti-aliased edge. A
    /// host that predates shapes draws nothing.
    /// </summary>
    /// <param name="bounds">The rectangle the ellipse fits.</param>
    /// <param name="color">The fill colour.</param>
    void FillEllipse(PluginRect bounds, PluginColor color)
    {
    }

    /// <summary>
    /// Draws the outline of the ellipse that fits a rectangle, centred on the
    /// outline, with anti-aliased edges. A host that predates shapes draws
    /// nothing.
    /// </summary>
    /// <param name="bounds">The rectangle the ellipse fits.</param>
    /// <param name="color">The outline colour.</param>
    /// <param name="thickness">The outline's width in pixels; thinner than one pixel is drawn one pixel wide and proportionally fainter.</param>
    void StrokeEllipse(PluginRect bounds, PluginColor color, float thickness = 1f)
    {
    }

    /// <summary>
    /// Fills a circle, with an anti-aliased edge: the ellipse in the square
    /// around it. A host that predates shapes draws nothing.
    /// </summary>
    /// <param name="center">The circle's centre.</param>
    /// <param name="radius">The circle's radius in pixels.</param>
    /// <param name="color">The fill colour.</param>
    void FillCircle(PluginPoint center, double radius, PluginColor color) =>
        FillEllipse(new PluginRect(center.X - radius, center.Y - radius, radius * 2, radius * 2), color);

    /// <summary>
    /// Fills a rectangle blending from one colour at one edge to another at
    /// the opposite edge, with anti-aliased edges. A host that predates
    /// shapes draws nothing.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="from">The colour at the left or top edge.</param>
    /// <param name="to">The colour at the right or bottom edge.</param>
    /// <param name="direction">Whether the blend runs across the rectangle or down it.</param>
    void FillRectGradient(PluginRect rect, PluginColor from, PluginColor to, PluginGradientDirection direction)
    {
    }
```

- [ ] **Step 6: Run to verify**

Run: `dotnet build src/AcDream.Plugin.Abstractions -c Release 2>&1 | tail -3 && dotnet test tests/AcDream.Plugin.Tests -c Release --filter "FullyQualifiedName~PluginCanvas"`
Expected: `0 Warning(s)`; `Passed!` (the existing `PluginCanvasContractTests` too).

Then check that the app still builds. `PluginPainter` gets the defaults for now:
Run: `dotnet build AcDream.slnx -c Release 2>&1 | tail -3`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Plugin.Abstractions/PluginShapes.cs src/AcDream.Plugin.Abstractions/PluginCanvas.cs tests/AcDream.Plugin.Tests/PluginCanvasShapesContractTests.cs
git commit -m "plugin api: anti-aliased shapes on the canvas painter

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.Plugin.Abstractions/PluginShapes.cs", "src/AcDream.Plugin.Abstractions/PluginCanvas.cs", "tests/AcDream.Plugin.Tests/PluginCanvasShapesContractTests.cs"], "verifyCommand": "dotnet test tests/AcDream.Plugin.Tests -c Release --filter FullyQualifiedName~PluginCanvas", "acceptanceCriteria": ["older painter accepts every shape, draws nothing", "FillCircle forwards to FillEllipse with the square", "Uniform radii and enum values", "Abstractions 0 warnings"], "modelTier": "standard"}
```

---

### Task 2: Clipping coloured triangles

**Goal:** `UiColorVertex` and `ColoredTriangleClipper`. The clipper trims one triangle to an axis-aligned rectangle and interpolates colour at the cuts. Triangles wholly inside or wholly outside take a fast path.

**Files:**
- Create: `src/AcDream.App/Rendering/ColoredTriangleClipper.cs`
- Test: `tests/AcDream.App.Tests/Rendering/ColoredTriangleClipperTests.cs` (create)

**Acceptance Criteria:**
- [ ] A triangle wholly inside comes back as its three corners, unchanged and in order
- [ ] A triangle wholly beyond one edge, or an empty clip, gives 0
- [ ] (0,0) red, (20,0) blue, (0,20) red cut at x = 10 gives four corners (0,0) red, (10,0) and (10,10) half red/half blue, (0,20) red
- [ ] (−5,−5), (25,5), (5,25) against (0,0)–(20,20) gives seven corners, all inside the clip
- [ ] A destination shorter than 7 throws `ArgumentException`

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~ColoredTriangleClipperTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** (`tests/AcDream.App.Tests/Rendering/ColoredTriangleClipperTests.cs`)

```csharp
using System.Numerics;
using AcDream.App.Rendering;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// One coloured triangle cut to the clip: whole triangles pass untouched,
/// cut ones gain corners on the clip's edges with the colour blended to
/// match where the cut fell.
/// </summary>
public sealed class ColoredTriangleClipperTests
{
    private static readonly Vector4 Red = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 Blue = new(0f, 0f, 1f, 1f);
    private static readonly Vector4 Purple = new(0.5f, 0f, 0.5f, 1f);

    private static UiColorVertex V(float x, float y, Vector4 color) => new(new Vector2(x, y), color);

    private static void AssertNear(UiColorVertex expected, UiColorVertex actual)
    {
        Assert.InRange(Vector2.Distance(expected.Position, actual.Position), 0f, 1e-4f);
        Assert.InRange(Vector4.Distance(expected.Color, actual.Color), 0f, 1e-4f);
    }

    [Fact]
    public void ATriangleWhollyInsideComesBackUnchanged()
    {
        Span<UiColorVertex> output = stackalloc UiColorVertex[ColoredTriangleClipper.MaxClippedVertices];
        UiColorVertex a = V(1f, 1f, Red), b = V(9f, 1f, Blue), c = V(1f, 9f, Red);

        int count = ColoredTriangleClipper.Clip(0f, 0f, 10f, 10f, a, b, c, output);

        Assert.Equal(3, count);
        Assert.Equal([a, b, c], output[..3].ToArray());
    }

    [Fact]
    public void ATriangleWhollyOutsideOrAnEmptyClipLeavesNothing()
    {
        Span<UiColorVertex> output = stackalloc UiColorVertex[ColoredTriangleClipper.MaxClippedVertices];
        UiColorVertex a = V(20f, 1f, Red), b = V(29f, 1f, Blue), c = V(20f, 9f, Red);

        Assert.Equal(0, ColoredTriangleClipper.Clip(0f, 0f, 10f, 10f, a, b, c, output));
        Assert.Equal(0, ColoredTriangleClipper.Clip(0f, 0f, 0f, 10f, V(1f, 1f, Red), V(2f, 1f, Red), V(1f, 2f, Red), output));
    }

    [Fact]
    public void ACutGainsCornersOnTheEdgeWithTheColourBlended()
    {
        Span<UiColorVertex> output = stackalloc UiColorVertex[ColoredTriangleClipper.MaxClippedVertices];

        int count = ColoredTriangleClipper.Clip(
            0f, 0f, 10f, 100f, V(0f, 0f, Red), V(20f, 0f, Blue), V(0f, 20f, Red), output);

        Assert.Equal(4, count);
        AssertNear(V(0f, 0f, Red), output[0]);
        AssertNear(V(10f, 0f, Purple), output[1]);
        AssertNear(V(10f, 10f, Purple), output[2]);
        AssertNear(V(0f, 20f, Red), output[3]);
    }

    [Fact]
    public void ATriangleCutOnEveryEdgeHasSevenCornersAllInside()
    {
        Span<UiColorVertex> output = stackalloc UiColorVertex[ColoredTriangleClipper.MaxClippedVertices];

        int count = ColoredTriangleClipper.Clip(
            0f, 0f, 20f, 20f, V(-5f, -5f, Red), V(25f, 5f, Blue), V(5f, 25f, Red), output);

        Assert.Equal(7, count);
        foreach (UiColorVertex corner in output[..count])
        {
            Assert.InRange(corner.Position.X, -1e-4f, 20f + 1e-4f);
            Assert.InRange(corner.Position.Y, -1e-4f, 20f + 1e-4f);
        }
    }

    [Fact]
    public void ADestinationTooSmallForSevenCornersIsRefused()
    {
        var output = new UiColorVertex[ColoredTriangleClipper.MaxClippedVertices - 1];

        Assert.Throws<ArgumentException>(() => ColoredTriangleClipper.Clip(
            0f, 0f, 10f, 10f, V(1f, 1f, Red), V(2f, 1f, Red), V(1f, 2f, Red), output));
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -m2 error`
Expected: `error CS0246: … 'UiColorVertex' could not be found`.

- [ ] **Step 3: Implement** (`src/AcDream.App/Rendering/ColoredTriangleClipper.cs`)

```csharp
using System;
using System.Numerics;

namespace AcDream.App.Rendering;

/// <summary>
/// One corner of an untextured triangle in screen pixels, with its own
/// colour; the rasteriser blends the colours across the triangle.
/// </summary>
internal readonly record struct UiColorVertex(Vector2 Position, Vector4 Color);

/// <summary>
/// Trims one coloured triangle to an axis-aligned clip rectangle, carrying
/// each corner's colour along with its position, so a gradient or an
/// anti-aliased fringe cut by the clip keeps the colour it had at the cut.
///
/// <para>Most triangles are wholly inside or wholly outside, and those are
/// answered without clipping. The rest go through the same
/// Sutherland-Hodgman walk as <see cref="TransformedQuadClipper"/>: one pass
/// per clip edge, each adding at most one corner, so a triangle comes out
/// with at most seven.</para>
/// </summary>
internal static class ColoredTriangleClipper
{
    /// <summary>The most corners a clipped triangle can have: three, plus one per clip edge.</summary>
    public const int MaxClippedVertices = 7;

    /// <summary>
    /// Writes the part of the triangle inside the clip rectangle into
    /// <paramref name="destination"/> as a convex polygon and returns how
    /// many corners it took; zero means nothing is left.
    /// <paramref name="destination"/> must hold <see cref="MaxClippedVertices"/>.
    /// </summary>
    public static int Clip(
        float clipLeft,
        float clipTop,
        float clipRight,
        float clipBottom,
        in UiColorVertex a,
        in UiColorVertex b,
        in UiColorVertex c,
        Span<UiColorVertex> destination)
    {
        if (destination.Length < MaxClippedVertices)
            throw new ArgumentException(
                $"A clipped triangle needs room for {MaxClippedVertices} corners.", nameof(destination));
        if (clipRight <= clipLeft || clipBottom <= clipTop)
            return 0;

        Vector2 pa = a.Position, pb = b.Position, pc = c.Position;
        if ((pa.X < clipLeft && pb.X < clipLeft && pc.X < clipLeft)
            || (pa.X > clipRight && pb.X > clipRight && pc.X > clipRight)
            || (pa.Y < clipTop && pb.Y < clipTop && pc.Y < clipTop)
            || (pa.Y > clipBottom && pb.Y > clipBottom && pc.Y > clipBottom))
            return 0;

        destination[0] = a;
        destination[1] = b;
        destination[2] = c;
        if (Inside(pa) && Inside(pb) && Inside(pc))
            return 3;

        Span<UiColorVertex> scratch = stackalloc UiColorVertex[MaxClippedVertices];
        int count = ClipAgainstEdge(ClipEdge.Left, clipLeft, destination, 3, scratch);
        if (count == 0) return 0;
        count = ClipAgainstEdge(ClipEdge.Top, clipTop, scratch, count, destination);
        if (count == 0) return 0;
        count = ClipAgainstEdge(ClipEdge.Right, clipRight, destination, count, scratch);
        if (count == 0) return 0;
        return ClipAgainstEdge(ClipEdge.Bottom, clipBottom, scratch, count, destination);

        bool Inside(Vector2 p) =>
            p.X >= clipLeft && p.X <= clipRight && p.Y >= clipTop && p.Y <= clipBottom;
    }

    private enum ClipEdge
    {
        Left,
        Top,
        Right,
        Bottom,
    }

    /// <summary>
    /// Keeps the part of the outline on the inside of one clip edge, emitting
    /// the crossing point wherever consecutive corners straddle it.
    /// </summary>
    private static int ClipAgainstEdge(
        ClipEdge edge, float boundary, ReadOnlySpan<UiColorVertex> input, int count, Span<UiColorVertex> output)
    {
        int written = 0;
        for (int index = 0; index < count; index++)
        {
            UiColorVertex current = input[index];
            UiColorVertex previous = input[(index + count - 1) % count];
            float currentDistance = SignedDistance(edge, boundary, current.Position);
            float previousDistance = SignedDistance(edge, boundary, previous.Position);
            bool currentInside = currentDistance >= 0f;
            bool previousInside = previousDistance >= 0f;

            if (currentInside)
            {
                if (!previousInside)
                    output[written++] = Crossing(previous, current, previousDistance, currentDistance);
                output[written++] = current;
            }
            else if (previousInside)
            {
                output[written++] = Crossing(previous, current, previousDistance, currentDistance);
            }
        }
        return written;
    }

    /// <summary>How far a point sits on the inside of one clip edge; negative is outside.</summary>
    private static float SignedDistance(ClipEdge edge, float boundary, Vector2 position)
        => edge switch
        {
            ClipEdge.Left => position.X - boundary,
            ClipEdge.Top => position.Y - boundary,
            ClipEdge.Right => boundary - position.X,
            _ => boundary - position.Y,
        };

    private static UiColorVertex Crossing(
        in UiColorVertex from, in UiColorVertex to, float fromDistance, float toDistance)
    {
        float span = fromDistance - toDistance;
        // The ends straddle the edge, so the span is not zero; the guard is
        // for the degenerate input where both distances are exactly zero.
        float t = MathF.Abs(span) > float.Epsilon ? fromDistance / span : 0f;
        return new UiColorVertex(
            Vector2.Lerp(from.Position, to.Position, t),
            Vector4.Lerp(from.Color, to.Color, t));
    }
}
```

- [ ] **Step 4: Run to verify**

Run: `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~ColoredTriangleClipperTests"`
Expected: `Passed!  - Failed: 0, Passed: 5`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/Rendering/ColoredTriangleClipper.cs tests/AcDream.App.Tests/Rendering/ColoredTriangleClipperTests.cs
git commit -m "gpu: coloured triangles are cut to the clip with their colours blended at the cut

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Rendering/ColoredTriangleClipper.cs", "tests/AcDream.App.Tests/Rendering/ColoredTriangleClipperTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~ColoredTriangleClipperTests", "acceptanceCriteria": ["inside unchanged", "outside and empty clip give 0", "cut blends colour", "seven-corner case inside", "short destination throws"], "modelTier": "standard"}
```

---

### Task 3: Coloured triangles in the interface renderer

**Goal:** `TextRenderer.DrawTriangles` appends untextured per-vertex-coloured triangles to the untextured sprite run. `UiRenderContext.DrawTriangles` moves them by the current origin, fades them by the alpha stack and clips them.

**Files:**
- Modify: `src/AcDream.App/Rendering/TextRenderer.cs` (new method after `DrawConvexPolygon`, before `AppendVertex` at ~330)
- Modify: `src/AcDream.App/UI/UiRenderContext.cs` (field near the stacks at ~33; method after `EmitConvexQuad`, ~303)
- Test: `tests/AcDream.App.Tests/Rendering/TextRendererTrianglesTests.cs` (create)
- Test: `tests/AcDream.App.Tests/UI/UiRenderContextTrianglesTests.cs` (create)

**Acceptance Criteria:**
- [ ] fill → triangles → sprite(7) → triangles gives runs with textures `[0, 7, 0]` and vertex counts `[9, 6, 3]`
- [ ] Each vertex is written as `x, y, 0, 0, r, g, b, a` with its own colour
- [ ] `CanvasScale` stretches triangle positions as it does quads
- [ ] A list that is not whole triangles throws `ArgumentException`
- [ ] Through the context: moved by `PushTransform`, alpha multiplied by `PushAlpha`, cut by `PushClip` with colour blended at the cut; wholly clipped or empty clip draws nothing

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~TrianglesTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/Rendering/TextRendererTrianglesTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// Untextured coloured triangles join the interface's untextured sprite
/// run, so they keep painter's order with fills and images, and each
/// corner keeps its own colour.
/// </summary>
public sealed class TextRendererTrianglesTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    private static readonly Vector4 Red = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 Blue = new(0f, 0f, 1f, 0.5f);

    private static UiColorVertex[] Triangle(float x) =>
    [
        new(new Vector2(x, 0f), Red),
        new(new Vector2(x + 10f, 0f), Blue),
        new(new Vector2(x, 10f), Red),
    ];

    private static TextRenderer Renderer(RecordingGpuDevice device)
    {
        var renderer = new TextRenderer(device, new FrameSource(), "unused");
        renderer.Begin(new Vector2(64f, 64f));
        return renderer;
    }

    [Fact]
    public void TrianglesJoinTheUntexturedRunAndKeepPaintersOrder()
    {
        using var device = new RecordingGpuDevice();
        using TextRenderer renderer = Renderer(device);

        renderer.DrawFill(0f, 0f, 10f, 10f, Vector4.One);
        renderer.DrawTriangles(Triangle(0f));
        renderer.DrawSprite(7u, 0f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, Vector4.One);
        renderer.DrawTriangles(Triangle(20f));

        Assert.Equal([0u, 7u, 0u], renderer.DebugSpriteSegments.Select(run => run.Texture).ToArray());
        Assert.Equal([9, 6, 3], renderer.DebugSpriteSegments.Select(run => run.VertexCount).ToArray());
    }

    [Fact]
    public void EachCornerKeepsItsOwnColourAndNoTextureCoordinate()
    {
        using var device = new RecordingGpuDevice();
        using TextRenderer renderer = Renderer(device);

        renderer.DrawTriangles(Triangle(5f));

        (uint texture, IReadOnlyList<float> verts) = Assert.Single(renderer.DebugSpriteSegmentVerts);
        Assert.Equal(UiTextureTableHandle.None, texture);
        Assert.Equal(
            [5f, 0f, 0f, 0f, 1f, 0f, 0f, 1f,
             15f, 0f, 0f, 0f, 0f, 0f, 1f, 0.5f,
             5f, 10f, 0f, 0f, 1f, 0f, 0f, 1f],
            verts.ToArray());
    }

    [Fact]
    public void TheFixedCanvasScaleStretchesTrianglesLikeEverythingElse()
    {
        using var device = new RecordingGpuDevice();
        using TextRenderer renderer = Renderer(device);
        renderer.CanvasScale = new Vector2(2f, 3f);

        renderer.DrawTriangles(Triangle(5f));

        float[] verts = Assert.Single(renderer.DebugSpriteSegmentVerts).Verts.ToArray();
        Assert.Equal((10f, 0f), (verts[0], verts[1]));
        Assert.Equal((30f, 0f), (verts[8], verts[9]));
        Assert.Equal((10f, 30f), (verts[16], verts[17]));
    }

    [Fact]
    public void AListThatIsNotWholeTrianglesIsRefused()
    {
        using var device = new RecordingGpuDevice();
        using TextRenderer renderer = Renderer(device);
        UiColorVertex[] two = Triangle(0f)[..2];

        Assert.Throws<ArgumentException>(() => renderer.DrawTriangles(two));
    }
}
```

`tests/AcDream.App.Tests/UI/UiRenderContextTrianglesTests.cs`:

```csharp
using System.Linq;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// Triangles drawn through the interface context follow the origin, the
/// alpha stack and the clip, as every other primitive does.
/// </summary>
public sealed class UiRenderContextTrianglesTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    private static readonly Vector4 Red = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 Blue = new(0f, 0f, 1f, 1f);

    private static (TextRenderer Renderer, UiRenderContext Context) Build()
    {
        var renderer = new TextRenderer(new RecordingGpuDevice(), new FrameSource(), "unused");
        renderer.Begin(new Vector2(100f, 100f));
        return (renderer, new UiRenderContext(renderer, new Vector2(100f, 100f)));
    }

    private static UiColorVertex[] Triangle() =>
    [
        new(new Vector2(0f, 0f), Red),
        new(new Vector2(20f, 0f), Blue),
        new(new Vector2(0f, 20f), Red),
    ];

    [Fact]
    public void TrianglesMoveWithTheOriginAndFadeWithTheAlpha()
    {
        (TextRenderer renderer, UiRenderContext context) = Build();
        context.PushTransform(10f, 20f);
        context.PushAlpha(0.5f);

        context.DrawTriangles(Triangle());

        float[] verts = Assert.Single(renderer.DebugSpriteSegmentVerts).Verts.ToArray();
        Assert.Equal(3 * TextRenderer.FloatsPerVertex, verts.Length);
        Assert.Equal((10f, 20f), (verts[0], verts[1]));
        Assert.Equal((30f, 20f), (verts[8], verts[9]));
        Assert.Equal((0.5f, 0.5f, 0.5f), (verts[7], verts[15], verts[23]));
    }

    [Fact]
    public void AClipCutsTheTriangleAndBlendsTheColourAtTheCut()
    {
        (TextRenderer renderer, UiRenderContext context) = Build();
        context.PushClip(0f, 0f, 10f, 100f);

        context.DrawTriangles(Triangle());

        float[] verts = Assert.Single(renderer.DebugSpriteSegmentVerts).Verts.ToArray();
        // Four corners are left after the cut, drawn as two triangles.
        Assert.Equal(6 * TextRenderer.FloatsPerVertex, verts.Length);
        for (int i = 0; i < verts.Length; i += TextRenderer.FloatsPerVertex)
        {
            Assert.InRange(verts[i], 0f, 10f + 1e-4f);
            if (MathF.Abs(verts[i] - 10f) < 1e-4f)
                Assert.InRange(verts[i + 4], 0.5f - 1e-4f, 0.5f + 1e-4f);
        }
    }

    [Fact]
    public void ATriangleWhollyOutsideTheClipOrUnderAnEmptyClipDrawsNothing()
    {
        (TextRenderer renderer, UiRenderContext context) = Build();

        context.PushClip(50f, 50f, 10f, 10f);
        context.DrawTriangles(Triangle());
        context.PopClip();
        context.PushClip(0f, 0f, 0f, 10f);
        context.DrawTriangles(Triangle());

        Assert.Empty(renderer.DebugSpriteSegments);
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -m2 error`
Expected: `error CS1061: 'TextRenderer' does not contain a definition for 'DrawTriangles'`.

- [ ] **Step 3: Renderer** (`TextRenderer.cs`, directly after the closing brace of `DrawConvexPolygon`)

```csharp

    /// <summary>
    /// Appends untextured triangles, three vertices each, with a colour at
    /// every corner that the rasteriser blends across the triangle -- the
    /// path for anti-aliased canvas shapes, whose fringes fade to clear.
    /// They land in the untextured run like <see cref="DrawFill"/>, so a
    /// shape between two fills costs no extra draw call and keeps its place
    /// among images.
    /// </summary>
    internal void DrawTriangles(ReadOnlySpan<UiColorVertex> triangles)
    {
        if (triangles.Length % 3 != 0)
            throw new ArgumentException("A triangle list holds whole triangles.", nameof(triangles));
        if (triangles.Length == 0)
            return;

        SpriteSeg seg = OverlayMode
            ? NextSpriteSeg(_overlaySpriteSegs, ref _overlaySegUsed, UiTextureTableHandle.None)
            : NextSpriteSeg(_spriteSegs,        ref _segUsed,        UiTextureTableHandle.None);
        foreach (UiColorVertex vertex in triangles)
            AppendVertex(seg.Verts, new UiQuadVertex(vertex.Position, Vector2.Zero), vertex.Color);
    }
```

- [ ] **Step 4: Context** (`UiRenderContext.cs`)

Field, next to `_alpha`:

```csharp
    private readonly System.Collections.Generic.List<UiColorVertex> _triangles = new(256);
```

Method, directly after the closing brace of `EmitConvexQuad`:

```csharp

    /// <summary>
    /// Untextured triangles in local coordinates, three vertices each, with a
    /// colour at every corner. They are moved by the current origin, faded
    /// by the alpha stack and cut to the clip in force, with the colour
    /// blended wherever the clip cuts, then handed to the batcher in one go.
    /// </summary>
    internal void DrawTriangles(ReadOnlySpan<UiColorVertex> triangles)
    {
        if (triangles.Length < 3 || _clip is { IsEmpty: true }) return;

        _triangles.Clear();
        Span<UiColorVertex> clipped = stackalloc UiColorVertex[ColoredTriangleClipper.MaxClippedVertices];
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            UiColorVertex a = Place(triangles[i]);
            UiColorVertex b = Place(triangles[i + 1]);
            UiColorVertex c = Place(triangles[i + 2]);
            if (_clip is not { } clip)
            {
                _triangles.Add(a);
                _triangles.Add(b);
                _triangles.Add(c);
                continue;
            }

            int count = ColoredTriangleClipper.Clip(clip.Left, clip.Top, clip.Right, clip.Bottom, a, b, c, clipped);
            for (int k = 1; k + 1 < count; k++)
            {
                _triangles.Add(clipped[0]);
                _triangles.Add(clipped[k]);
                _triangles.Add(clipped[k + 1]);
            }
        }

        if (_triangles.Count > 0)
            TextRenderer.DrawTriangles(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_triangles));
    }

    private UiColorVertex Place(in UiColorVertex vertex) =>
        new(vertex.Position + _current, ApplyAlpha(vertex.Color));
```

- [ ] **Step 5: Run to verify**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | tail -3 && dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~TrianglesTests|FullyQualifiedName~TextRendererBlendTests|FullyQualifiedName~PluginCanvas"`
Expected: `0 Warning(s)`; `Passed!`.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/Rendering/TextRenderer.cs src/AcDream.App/UI/UiRenderContext.cs tests/AcDream.App.Tests/Rendering/TextRendererTrianglesTests.cs tests/AcDream.App.Tests/UI/UiRenderContextTrianglesTests.cs
git commit -m "gpu: untextured coloured triangles in the interface's sprite runs

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Rendering/TextRenderer.cs", "src/AcDream.App/UI/UiRenderContext.cs", "tests/AcDream.App.Tests/Rendering/TextRendererTrianglesTests.cs", "tests/AcDream.App.Tests/UI/UiRenderContextTrianglesTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~TrianglesTests", "acceptanceCriteria": ["runs [0,7,0] counts [9,6,3]", "vertex layout x,y,0,0,rgba", "CanvasScale applies", "partial list throws", "context origin/alpha/clip applied"], "modelTier": "standard"}
```

---

### Task 4: Tessellating shapes with an anti-aliased fringe

**Goal:** `CanvasGeometry`, a pure tessellator. It turns polygons, rounded rectangles and ellipses (filled and stroked) into coloured triangles with a one-device-pixel fringe fading to clear. The alpha-weighted covered area is within 1% of the analytic area.

**Files:**
- Create: `src/AcDream.App/UI/CanvasGeometry.cs`
- Test: `tests/AcDream.App.Tests/UI/CanvasGeometryTests.cs` (create)

**Acceptance Criteria:**
- [ ] Circle of radius 20 at pixel 1: 282 vertices, coverage within 1% of π·20², solid vertices within 19.5 of the centre, clear vertices at 20.5
- [ ] 20×20 square: 30 vertices, coverage within 0.5% of 400, clear corners at ±0.5 outside, solid corners at 0.5 inside
- [ ] Rounded rectangle and oversized-radius cases within 1%; `ClampRadii` scales the CSS way
- [ ] Strokes: rect t = 2 gives 72 vertices and coverage within 0.5% of 240; circle t = 2 within 1%; t = 0.5 and t = 0.25 have peak alpha 0.5 and 0.25 and coverage within 1% of perimeter × t; t = 30 on a 10×10 rect covers 40×40
- [ ] Polygons: a triangle either way round gives 21 vertices and coverage within 0.5%; per-corner colours are kept and fringes fade from them; concave, star and back-tracking outlines are `NotConvex`; collinear is `Nothing`; 2 points are `TooFewPoints`; 65 points are `TooManyPoints`; repeated points are merged
- [ ] `ArcSegments(20.5, 2π, 1) == 32`, `(40.5, 2π, 1) == 45`, `(20.5, 2π, 0.5) == (41, 2π, 1)`; huge rounded rect gives 1182 vertices and huge ellipse 2298
- [ ] Zero area and zero thickness write nothing

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~CanvasGeometryTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** (`tests/AcDream.App.Tests/UI/CanvasGeometryTests.cs`)

The expected counts and areas were measured on a prototype of the implementation below. Covered areas all came within 0.7% of analytic.

```csharp
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// Canvas shapes as triangles: a fringe one pixel wide, fading to clear,
/// straddles every edge, so what the triangles cover weighted by alpha is
/// the shape's own area; curves are cut finely enough to stay within a
/// tenth of a pixel of the true curve; bad polygons are told apart from
/// empty ones.
/// </summary>
public sealed class CanvasGeometryTests
{
    private static readonly Vector4 White = Vector4.One;

    /// <summary>The area the triangles cover, each weighted by its mean alpha: exact for linear blending.</summary>
    private static double Covered(IReadOnlyList<UiColorVertex> triangles)
    {
        double sum = 0;
        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector2 a = triangles[i].Position, b = triangles[i + 1].Position, c = triangles[i + 2].Position;
            double area = Math.Abs((b.X - a.X) * (double)(c.Y - a.Y) - (c.X - a.X) * (double)(b.Y - a.Y)) / 2;
            sum += area * (triangles[i].Color.W + triangles[i + 1].Color.W + triangles[i + 2].Color.W) / 3;
        }
        return sum;
    }

    private static void AssertCovers(double expected, IReadOnlyList<UiColorVertex> triangles, double tolerance = 0.01)
    {
        double covered = Covered(triangles);
        Assert.True(
            Math.Abs(covered - expected) <= expected * tolerance,
            string.Create(CultureInfo.InvariantCulture, $"covered {covered:F3}, expected {expected:F3} within {tolerance:P1}"));
    }

    private static Vector4[] Repeat(Vector4 color, int count) => Enumerable.Repeat(color, count).ToArray();

    [Fact]
    public void ACircleCoversItsAreaWithASolidInsideAndAClearRim()
    {
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.FillEllipse(30f, 10f, 40f, 40f, White, 1f, triangles);

        Assert.Equal(282, triangles.Count);
        AssertCovers(Math.PI * 20 * 20, triangles);
        var centre = new Vector2(50f, 30f);
        foreach (UiColorVertex vertex in triangles)
        {
            float distance = Vector2.Distance(vertex.Position, centre);
            if (vertex.Color.W == 1f)
            {
                Assert.InRange(distance, 0f, 19.5f + 1e-3f);
            }
            else
            {
                Assert.Equal(0f, vertex.Color.W);
                Assert.InRange(distance, 20.5f - 1e-3f, 20.5f + 1e-3f);
            }
        }
    }

    [Fact]
    public void ASquareCorneredRectangleHasItsFringeHalfInsideAndHalfOutside()
    {
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.FillRoundedRect(10f, 10f, 20f, 20f, default, White, 1f, triangles);

        Assert.Equal(30, triangles.Count);
        AssertCovers(400, triangles, tolerance: 0.005);
        var clear = new HashSet<Vector2> { new(9.5f, 9.5f), new(30.5f, 9.5f), new(30.5f, 30.5f), new(9.5f, 30.5f) };
        var solid = new HashSet<Vector2> { new(10.5f, 10.5f), new(29.5f, 10.5f), new(29.5f, 29.5f), new(10.5f, 29.5f) };
        Assert.True(clear.SetEquals(triangles.Where(v => v.Color.W == 0f).Select(v => v.Position)));
        Assert.True(solid.SetEquals(triangles.Where(v => v.Color.W == 1f).Select(v => v.Position)));
    }

    [Fact]
    public void RoundedCornersTakeTheirCornersOut()
    {
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.FillRoundedRect(10f, 10f, 60f, 30f, new CanvasCornerRadii(8f, 8f, 8f, 8f), White, 1f, triangles);

        AssertCovers(60 * 30 - (4 - Math.PI) * 8 * 8, triangles);
    }

    [Fact]
    public void RadiiTooLargeForTheRectangleShrinkTogether()
    {
        Assert.Equal(
            new CanvasCornerRadii(15f, 15f, 15f, 15f),
            CanvasGeometry.ClampRadii(new CanvasCornerRadii(40f, 40f, 40f, 40f), 60f, 30f));
        Assert.Equal(
            new CanvasCornerRadii(10f, 0f, 0f, 0f),
            CanvasGeometry.ClampRadii(new CanvasCornerRadii(10f, 0f, 0f, 0f), 60f, 30f));
        CanvasCornerRadii scaled = CanvasGeometry.ClampRadii(new CanvasCornerRadii(30f, 40f, 0f, 0f), 60f, 100f);
        Assert.InRange(scaled.TopLeft, 25.714f, 25.715f);
        Assert.InRange(scaled.TopRight, 34.285f, 34.286f);

        var triangles = new List<UiColorVertex>();
        CanvasGeometry.FillRoundedRect(10f, 10f, 60f, 30f, new CanvasCornerRadii(40f, 40f, 40f, 40f), White, 1f, triangles);
        AssertCovers(60 * 30 - (4 - Math.PI) * 15 * 15, triangles);
    }

    [Fact]
    public void AStrokeIsCentredOnTheOutline()
    {
        var rect = new List<UiColorVertex>();
        CanvasGeometry.StrokeRoundedRect(10f, 10f, 40f, 20f, default, White, 2f, 1f, rect);
        Assert.Equal(72, rect.Count);
        AssertCovers(42 * 22 - 38 * 18, rect, tolerance: 0.005);

        var circle = new List<UiColorVertex>();
        CanvasGeometry.StrokeEllipse(10f, 10f, 40f, 40f, White, 2f, 1f, circle);
        Assert.Equal(648, circle.Count);
        AssertCovers(Math.PI * (21 * 21 - 19 * 19), circle);
    }

    [Fact]
    public void AStrokeThinnerThanAPixelIsOnePixelWideAndProportionallyFainter()
    {
        var circle = new List<UiColorVertex>();
        CanvasGeometry.StrokeEllipse(10f, 10f, 40f, 40f, White, 0.5f, 1f, circle);
        // Two fringes and no solid core between them.
        Assert.Equal(432, circle.Count);
        Assert.Equal(0.5f, circle.Max(v => v.Color.W));
        AssertCovers(Math.PI * 40 * 0.5, circle);

        var rect = new List<UiColorVertex>();
        CanvasGeometry.StrokeRoundedRect(10f, 10f, 40f, 20f, new CanvasCornerRadii(5f, 5f, 5f, 5f), White, 0.25f, 1f, rect);
        Assert.Equal(0.25f, rect.Max(v => v.Color.W));
        AssertCovers((2 * (30 + 10) + 2 * Math.PI * 5) * 0.25, rect);
    }

    [Fact]
    public void AStrokeThickerThanTheShapeFillsIt()
    {
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.StrokeRoundedRect(10f, 10f, 10f, 10f, default, White, 30f, 1f, triangles);

        AssertCovers(40 * 40, triangles, tolerance: 0.005);
    }

    [Fact]
    public void APolygonCoversItsAreaWhicheverWayItWinds()
    {
        Vector2[] clockwise = [new(10f, 10f), new(50f, 10f), new(30f, 40f)];
        Vector2[] anticlockwise = [new(30f, 40f), new(50f, 10f), new(10f, 10f)];
        foreach (Vector2[] points in new[] { clockwise, anticlockwise })
        {
            var triangles = new List<UiColorVertex>();

            Assert.Equal(CanvasShapeOutcome.Drawn, CanvasGeometry.FillConvexPolygon(points, Repeat(White, 3), 1f, triangles));

            Assert.Equal(21, triangles.Count);
            AssertCovers(600, triangles, tolerance: 0.005);
        }
    }

    [Fact]
    public void EachCornerKeepsItsColourAndItsFringeFadesFromIt()
    {
        var red = new Vector4(1f, 0f, 0f, 1f);
        var blue = new Vector4(0f, 0f, 1f, 1f);
        Vector2[] strip = [new(0f, 0f), new(100f, 0f), new(100f, 10f), new(0f, 10f)];
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.FillConvexPolygon(strip, [red, blue, blue, red], 1f, triangles);

        foreach (UiColorVertex vertex in triangles)
        {
            Vector4 expected = vertex.Position.X < 50f ? red : blue;
            Assert.Equal(expected with { W = vertex.Color.W }, vertex.Color);
            Assert.True(vertex.Color.W is 0f or 1f);
        }
        Assert.Contains(triangles, v => v.Position == new Vector2(0.5f, 0.5f) && v.Color == red);
        Assert.Contains(triangles, v => v.Position == new Vector2(100.5f, -0.5f) && v.Color == blue with { W = 0f });
    }

    [Fact]
    public void APolygonThatIsNotConvexIsToldApartFromOneWithNoArea()
    {
        var triangles = new List<UiColorVertex>();
        Vector2[] star = Enumerable.Range(0, 5)
            .Select(i => new Vector2(50f + 20f * MathF.Cos(i * 4f * MathF.PI / 5f), 50f + 20f * MathF.Sin(i * 4f * MathF.PI / 5f)))
            .ToArray();
        Vector2[] tooMany = Enumerable.Range(0, 65)
            .Select(i => new Vector2(50f * MathF.Cos(i * 2f * MathF.PI / 65f), 50f * MathF.Sin(i * 2f * MathF.PI / 65f)))
            .ToArray();

        Assert.Equal(CanvasShapeOutcome.NotConvex, CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(20f, 0f), new(10f, 5f), new(20f, 20f), new(0f, 20f)], Repeat(White, 5), 1f, triangles));
        Assert.Equal(CanvasShapeOutcome.NotConvex, CanvasGeometry.FillConvexPolygon(star, Repeat(White, 5), 1f, triangles));
        Assert.Equal(CanvasShapeOutcome.NotConvex, CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(20f, 0f), new(10f, 0f), new(10f, 10f)], Repeat(White, 4), 1f, triangles));
        Assert.Equal(CanvasShapeOutcome.Nothing, CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(10f, 0f), new(20f, 0f)], Repeat(White, 3), 1f, triangles));
        Assert.Equal(CanvasShapeOutcome.TooFewPoints, CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(10f, 10f)], Repeat(White, 2), 1f, triangles));
        Assert.Equal(CanvasShapeOutcome.TooManyPoints, CanvasGeometry.FillConvexPolygon(
            tooMany, Repeat(White, 65), 1f, triangles));
        Assert.Empty(triangles);
    }

    [Fact]
    public void RepeatedPointsAreMerged()
    {
        var triangles = new List<UiColorVertex>();

        CanvasShapeOutcome outcome = CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(0f, 0f), new(20f, 0f), new(20f, 20f), new(0f, 0f)], Repeat(White, 5), 1f, triangles);

        Assert.Equal(CanvasShapeOutcome.Drawn, outcome);
        Assert.Equal(21, triangles.Count);
    }

    [Fact]
    public void CurvesAreCutFinerTheLargerTheyAreOnScreen()
    {
        float turn = MathF.PI * 2f;
        Assert.Equal(0, CanvasGeometry.ArcSegments(0f, turn, 1f));
        Assert.Equal(1, CanvasGeometry.ArcSegments(0.05f, turn, 1f));
        Assert.Equal(32, CanvasGeometry.ArcSegments(20.5f, turn, 1f));
        Assert.Equal(45, CanvasGeometry.ArcSegments(40.5f, turn, 1f));
        // Two device pixels to a canvas pixel cut as finely as twice the radius.
        Assert.Equal(CanvasGeometry.ArcSegments(41f, turn, 1f), CanvasGeometry.ArcSegments(20.5f, turn, 0.5f));
    }

    [Fact]
    public void HugeShapesAreCutIntoABoundedNumberOfPieces()
    {
        var triangles = new List<UiColorVertex>();
        CanvasGeometry.FillRoundedRect(
            0f, 0f, 20000f, 20000f, new CanvasCornerRadii(10000f, 10000f, 10000f, 10000f), White, 1f, triangles);
        // Four corners of 32 chords: 132 points a ring.
        Assert.Equal(1182, triangles.Count);

        triangles.Clear();
        CanvasGeometry.FillEllipse(0f, 0f, 100000f, 100000f, White, 1f, triangles);
        // 256 points a ring.
        Assert.Equal(2298, triangles.Count);
    }

    [Fact]
    public void ShapesWithNoAreaOrNoThicknessWriteNothing()
    {
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.FillEllipse(0f, 0f, 0f, 10f, White, 1f, triangles);
        CanvasGeometry.FillRoundedRect(0f, 0f, 10f, 0f, default, White, 1f, triangles);
        CanvasGeometry.StrokeEllipse(0f, 0f, 10f, 10f, White, 0f, 1f, triangles);
        CanvasGeometry.StrokeRoundedRect(0f, 0f, 10f, 10f, default, White, 0f, 1f, triangles);

        Assert.Empty(triangles);
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -m2 error`
Expected: `error CS0103: The name 'CanvasGeometry' does not exist in the current context`.

- [ ] **Step 3: Implement** (`src/AcDream.App/UI/CanvasGeometry.cs`)

```csharp
using System.Collections.Generic;
using System.Numerics;
using AcDream.App.Rendering;

namespace AcDream.App.UI;

/// <summary>What tessellating one shape came to.</summary>
internal enum CanvasShapeOutcome
{
    /// <summary>Triangles were written.</summary>
    Drawn,

    /// <summary>The shape covers nothing (no area, no thickness); nothing was written and nothing is wrong.</summary>
    Nothing,

    /// <summary>A polygon of fewer than three points.</summary>
    TooFewPoints,

    /// <summary>A polygon of more than <see cref="CanvasGeometry.MaximumPolygonPoints"/> points.</summary>
    TooManyPoints,

    /// <summary>A polygon whose outline turns both ways, or winds round more than once.</summary>
    NotConvex,
}

/// <summary>The four corner radii of a rounded rectangle, in canvas pixels.</summary>
internal readonly record struct CanvasCornerRadii(float TopLeft, float TopRight, float BottomRight, float BottomLeft);

/// <summary>
/// Turns canvas shapes into untextured triangles with a colour at every
/// corner, anti-aliased on the CPU: each edge gets a band one device pixel
/// wide whose outer vertices are transparent, centred on the true edge, so
/// the rasteriser's linear interpolation ramps coverage across it. Half the
/// band lies inside the shape and half outside, which keeps the covered
/// area equal to the shape's own. No shader is involved.
///
/// <para>A shape is built from rings: its outline pushed out and pulled in
/// by the fringe (and, for a stroke, by half the stroke's width), each
/// ring with the same number of points so neighbouring rings join point
/// for point. Everything here is pure: sizes come in canvas pixels, along
/// with how many canvas pixels one device pixel is, and triangles go out
/// as a list, three vertices each.</para>
/// </summary>
internal static class CanvasGeometry
{
    /// <summary>The most points a polygon may have.</summary>
    internal const int MaximumPolygonPoints = 64;

    /// <summary>How far, in device pixels, a chord may stray inside the true curve.</summary>
    internal const float ArcTolerance = 0.1f;

    /// <summary>The most chords one rounded corner is cut into.</summary>
    internal const int MaximumSegmentsPerCorner = 32;

    /// <summary>The fewest chords an ellipse is cut into.</summary>
    internal const int MinimumEllipseSegments = 8;

    /// <summary>The most chords an ellipse is cut into.</summary>
    internal const int MaximumEllipseSegments = 256;

    /// <summary>
    /// How far out a polygon corner's fringe may reach, as a multiple of half
    /// the fringe. A very sharp corner's true mitre would reach much further.
    /// </summary>
    private const float MaximumMiter = 10f;

    /// <summary>
    /// How many chords an arc of <paramref name="radius"/> canvas pixels
    /// sweeping <paramref name="sweep"/> radians needs so that no chord
    /// strays more than <see cref="ArcTolerance"/> device pixels from it,
    /// with <paramref name="pixel"/> canvas pixels to a device pixel.
    /// </summary>
    internal static int ArcSegments(float radius, float sweep, float pixel)
    {
        float radiusInPixels = radius / pixel;
        if (!(radiusInPixels > 0f)) return 0;
        if (radiusInPixels <= ArcTolerance) return 1;
        float step = 2f * MathF.Acos(1f - ArcTolerance / radiusInPixels);
        return Math.Max(1, (int)MathF.Ceiling(sweep / step));
    }

    /// <summary>
    /// Scales every radius down by one factor when two neighbours would not
    /// fit along the side they share, the way CSS does, so a corner never
    /// overruns its side and the shape keeps its proportions.
    /// </summary>
    internal static CanvasCornerRadii ClampRadii(CanvasCornerRadii radii, float width, float height)
    {
        float factor = 1f;
        factor = Fit(factor, width, radii.TopLeft + radii.TopRight);
        factor = Fit(factor, width, radii.BottomLeft + radii.BottomRight);
        factor = Fit(factor, height, radii.TopLeft + radii.BottomLeft);
        factor = Fit(factor, height, radii.TopRight + radii.BottomRight);
        return factor < 1f
            ? new CanvasCornerRadii(
                radii.TopLeft * factor, radii.TopRight * factor,
                radii.BottomRight * factor, radii.BottomLeft * factor)
            : radii;

        static float Fit(float factor, float side, float sum) =>
            sum > side ? MathF.Min(factor, side / sum) : factor;
    }

    /// <summary>
    /// A convex polygon with a colour at each point, blended across it.
    /// Repeated neighbouring points are merged; a polygon with no area draws
    /// nothing. Either winding is accepted.
    /// </summary>
    internal static CanvasShapeOutcome FillConvexPolygon(
        ReadOnlySpan<Vector2> points, ReadOnlySpan<Vector4> colors, float pixel, List<UiColorVertex> output)
    {
        if (points.Length < 3) return CanvasShapeOutcome.TooFewPoints;
        if (points.Length > MaximumPolygonPoints) return CanvasShapeOutcome.TooManyPoints;
        if (colors.Length != points.Length)
            throw new ArgumentException("Each point needs its own colour.", nameof(colors));

        Span<Vector2> corner = stackalloc Vector2[MaximumPolygonPoints];
        Span<Vector4> color = stackalloc Vector4[MaximumPolygonPoints];
        int count = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (count > 0 && SamePoint(corner[count - 1], points[i])) continue;
            corner[count] = points[i];
            color[count] = colors[i];
            count++;
        }
        while (count > 1 && SamePoint(corner[count - 1], corner[0])) count--;
        if (count < 3) return CanvasShapeOutcome.Nothing;
        corner = corner[..count];
        color = color[..count];

        // Convex means every turn goes the same way and the turns add up to
        // one full circle; a star turns one way throughout but twice round.
        int turn = 0;
        double turning = 0;
        for (int i = 0; i < count; i++)
        {
            Vector2 into = corner[(i + 1) % count] - corner[i];
            Vector2 outOf = corner[(i + 2) % count] - corner[(i + 1) % count];
            float cross = Cross(into, outOf);
            if (MathF.Abs(cross) > 1e-6f * into.Length() * outOf.Length())
            {
                int sign = Math.Sign(cross);
                if (turn == 0) turn = sign;
                else if (sign != turn) return CanvasShapeOutcome.NotConvex;
            }
            turning += Math.Atan2(cross, Vector2.Dot(into, outOf));
        }
        if (turn == 0) return CanvasShapeOutcome.Nothing;
        if (Math.Abs(Math.Abs(turning) - 2 * Math.PI) > 1e-3) return CanvasShapeOutcome.NotConvex;

        // Outward normal of each edge; which side is out depends on the winding.
        Span<Vector2> normal = stackalloc Vector2[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 edge = Vector2.Normalize(corner[(i + 1) % count] - corner[i]);
            normal[i] = turn > 0 ? new Vector2(edge.Y, -edge.X) : new Vector2(-edge.Y, edge.X);
        }

        float half = pixel * 0.5f;
        Span<Vector2> inner = stackalloc Vector2[count];
        Span<Vector2> outer = stackalloc Vector2[count];
        Span<Vector4> clear = stackalloc Vector4[count];
        for (int i = 0; i < count; i++)
        {
            // The mitre: the corner moves along the average of its two edge
            // normals, far enough that both edges move by half a pixel.
            Vector2 average = (normal[(i + count - 1) % count] + normal[i]) * 0.5f;
            Vector2 miter = average / MathF.Max(average.LengthSquared(), 1f / (MaximumMiter * MaximumMiter));
            inner[i] = corner[i] - miter * half;
            outer[i] = corner[i] + miter * half;
            clear[i] = Transparent(color[i]);
        }

        Fan(inner, color, output);
        Band(inner, color, outer, clear, output);
        return CanvasShapeOutcome.Drawn;
    }

    /// <summary>A rectangle with rounded corners; zero radii give square corners.</summary>
    internal static void FillRoundedRect(
        float x, float y, float width, float height, CanvasCornerRadii radii, Vector4 color, float pixel,
        List<UiColorVertex> output)
    {
        if (!(width > 0f && height > 0f)) return;
        radii = ClampRadii(radii, width, height);
        float half = pixel * 0.5f;
        Span<int> segments = stackalloc int[4];
        CornerSegments(radii, half, pixel, segments);
        int points = RingLength(segments);
        Span<Vector2> inner = stackalloc Vector2[points];
        Span<Vector2> outer = stackalloc Vector2[points];
        RoundedRectRing(x, y, width, height, radii, -half, segments, inner);
        RoundedRectRing(x, y, width, height, radii, half, segments, outer);
        FillRing(inner, outer, color, output);
    }

    /// <summary>
    /// A rounded rectangle's outline, <paramref name="thickness"/> wide,
    /// centred on the outline. Outside a square corner the stroke stays square.
    /// </summary>
    internal static void StrokeRoundedRect(
        float x, float y, float width, float height, CanvasCornerRadii radii, Vector4 color, float thickness,
        float pixel, List<UiColorVertex> output)
    {
        if (!(width > 0f && height > 0f && thickness > 0f)) return;
        radii = ClampRadii(radii, width, height);
        (float halfWidth, Vector4 ink) = StrokeProfile(thickness, color, pixel);
        float fringe = pixel * 0.5f;
        Span<int> segments = stackalloc int[4];
        CornerSegments(radii, halfWidth + fringe, pixel, segments);
        int points = RingLength(segments);
        Span<Vector2> rings = stackalloc Vector2[points * 4];
        RoundedRectRing(x, y, width, height, radii, halfWidth + fringe, segments, rings.Slice(0, points));
        RoundedRectRing(x, y, width, height, radii, halfWidth - fringe, segments, rings.Slice(points, points));
        RoundedRectRing(x, y, width, height, radii, fringe - halfWidth, segments, rings.Slice(points * 2, points));
        RoundedRectRing(x, y, width, height, radii, -halfWidth - fringe, segments, rings.Slice(points * 3, points));
        StrokeRings(rings, points, ink, halfWidth > fringe, output);
    }

    /// <summary>An ellipse filling a rectangle.</summary>
    internal static void FillEllipse(
        float x, float y, float width, float height, Vector4 color, float pixel, List<UiColorVertex> output)
    {
        if (!(width > 0f && height > 0f)) return;
        var centre = new Vector2(x + width * 0.5f, y + height * 0.5f);
        var radii = new Vector2(width * 0.5f, height * 0.5f);
        float half = pixel * 0.5f;
        int segments = EllipseSegments(radii, half, pixel);
        Span<Vector2> inner = stackalloc Vector2[segments];
        Span<Vector2> outer = stackalloc Vector2[segments];
        EllipseRing(centre, radii, -half, inner);
        EllipseRing(centre, radii, half, outer);
        FillRing(inner, outer, color, output);
    }

    /// <summary>
    /// An ellipse's outline, <paramref name="thickness"/> wide, centred on
    /// the outline. Its rings grow both radii alike, which is exact for a
    /// circle and close for an ellipse.
    /// </summary>
    internal static void StrokeEllipse(
        float x, float y, float width, float height, Vector4 color, float thickness, float pixel,
        List<UiColorVertex> output)
    {
        if (!(width > 0f && height > 0f && thickness > 0f)) return;
        var centre = new Vector2(x + width * 0.5f, y + height * 0.5f);
        var radii = new Vector2(width * 0.5f, height * 0.5f);
        (float halfWidth, Vector4 ink) = StrokeProfile(thickness, color, pixel);
        float fringe = pixel * 0.5f;
        int segments = EllipseSegments(radii, halfWidth + fringe, pixel);
        Span<Vector2> rings = stackalloc Vector2[segments * 4];
        EllipseRing(centre, radii, halfWidth + fringe, rings.Slice(0, segments));
        EllipseRing(centre, radii, halfWidth - fringe, rings.Slice(segments, segments));
        EllipseRing(centre, radii, fringe - halfWidth, rings.Slice(segments * 2, segments));
        EllipseRing(centre, radii, -halfWidth - fringe, rings.Slice(segments * 3, segments));
        StrokeRings(rings, segments, ink, halfWidth > fringe, output);
    }

    /// <summary>
    /// How wide a stroke is drawn and in what colour: as asked, or, thinner
    /// than a device pixel, one device pixel wide with the alpha lowered in
    /// proportion, so it covers what the thin stroke would have.
    /// </summary>
    private static (float HalfWidth, Vector4 Ink) StrokeProfile(float thickness, Vector4 color, float pixel) =>
        thickness >= pixel
            ? (thickness * 0.5f, color)
            : (pixel * 0.5f, color with { W = color.W * (thickness / pixel) });

    /// <summary>
    /// Four rings from outside in -- outer fade, outer edge, inner edge,
    /// inner fade -- joined into the two fringes and, when the stroke is
    /// wider than its fringes, the solid core between them.
    /// </summary>
    private static void StrokeRings(
        ReadOnlySpan<Vector2> rings, int points, Vector4 ink, bool hasCore, List<UiColorVertex> output)
    {
        Span<Vector4> solid = stackalloc Vector4[points];
        Span<Vector4> clear = stackalloc Vector4[points];
        solid.Fill(ink);
        clear.Fill(Transparent(ink));
        ReadOnlySpan<Vector2> outerFade = rings.Slice(0, points);
        ReadOnlySpan<Vector2> outerEdge = rings.Slice(points, points);
        ReadOnlySpan<Vector2> innerEdge = rings.Slice(points * 2, points);
        ReadOnlySpan<Vector2> innerFade = rings.Slice(points * 3, points);
        Band(outerEdge, solid, outerFade, clear, output);
        if (hasCore) Band(innerEdge, solid, outerEdge, solid, output);
        Band(innerFade, clear, innerEdge, solid, output);
    }

    /// <summary>A convex shape given as its inner (solid) and outer (clear) rings.</summary>
    private static void FillRing(
        ReadOnlySpan<Vector2> inner, ReadOnlySpan<Vector2> outer, Vector4 color, List<UiColorVertex> output)
    {
        Span<Vector4> solid = stackalloc Vector4[inner.Length];
        Span<Vector4> clear = stackalloc Vector4[inner.Length];
        solid.Fill(color);
        clear.Fill(Transparent(color));
        Fan(inner, solid, output);
        Band(inner, solid, outer, clear, output);
    }

    /// <summary>A convex ring filled as a fan from its first point.</summary>
    private static void Fan(ReadOnlySpan<Vector2> ring, ReadOnlySpan<Vector4> colors, List<UiColorVertex> output)
    {
        for (int i = 1; i + 1 < ring.Length; i++)
        {
            output.Add(new UiColorVertex(ring[0], colors[0]));
            output.Add(new UiColorVertex(ring[i], colors[i]));
            output.Add(new UiColorVertex(ring[i + 1], colors[i + 1]));
        }
    }

    /// <summary>Two rings of the same length joined point for point, all the way round.</summary>
    private static void Band(
        ReadOnlySpan<Vector2> a, ReadOnlySpan<Vector4> colorsA,
        ReadOnlySpan<Vector2> b, ReadOnlySpan<Vector4> colorsB,
        List<UiColorVertex> output)
    {
        int count = a.Length;
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            output.Add(new UiColorVertex(a[i], colorsA[i]));
            output.Add(new UiColorVertex(a[next], colorsA[next]));
            output.Add(new UiColorVertex(b[next], colorsB[next]));
            output.Add(new UiColorVertex(a[i], colorsA[i]));
            output.Add(new UiColorVertex(b[next], colorsB[next]));
            output.Add(new UiColorVertex(b[i], colorsB[i]));
        }
    }

    /// <summary>
    /// Chords per corner, from the largest radius any ring reaches there. A
    /// square corner is one point in every ring.
    /// </summary>
    private static void CornerSegments(CanvasCornerRadii radii, float grow, float pixel, Span<int> segments)
    {
        segments[0] = Corner(radii.TopLeft);
        segments[1] = Corner(radii.TopRight);
        segments[2] = Corner(radii.BottomRight);
        segments[3] = Corner(radii.BottomLeft);

        int Corner(float radius) => radius > 0f
            ? Math.Clamp(ArcSegments(radius + grow, MathF.PI * 0.5f, pixel), 1, MaximumSegmentsPerCorner)
            : 0;
    }

    private static int RingLength(ReadOnlySpan<int> segments) =>
        segments[0] + segments[1] + segments[2] + segments[3] + 4;

    /// <summary>
    /// The outline of the rectangle grown by <paramref name="grow"/> on every
    /// side (negative shrinks it, no further than to its middle), each
    /// rounded corner's radius grown alike and never below zero, clockwise on
    /// screen from the top-left corner. A square corner stays square.
    /// </summary>
    private static void RoundedRectRing(
        float x, float y, float width, float height, CanvasCornerRadii radii, float grow,
        ReadOnlySpan<int> segments, Span<Vector2> ring)
    {
        float left = x - grow, top = y - grow;
        float w = width + 2f * grow, h = height + 2f * grow;
        if (w < 0f) { left = x + width * 0.5f; w = 0f; }
        if (h < 0f) { top = y + height * 0.5f; h = 0f; }
        CanvasCornerRadii grown = ClampRadii(
            new CanvasCornerRadii(Grow(radii.TopLeft), Grow(radii.TopRight), Grow(radii.BottomRight), Grow(radii.BottomLeft)),
            w, h);

        int at = 0;
        Arc(left + grown.TopLeft, top + grown.TopLeft, grown.TopLeft, MathF.PI, segments[0], ring, ref at);
        Arc(left + w - grown.TopRight, top + grown.TopRight, grown.TopRight, MathF.PI * 1.5f, segments[1], ring, ref at);
        Arc(left + w - grown.BottomRight, top + h - grown.BottomRight, grown.BottomRight, 0f, segments[2], ring, ref at);
        Arc(left + grown.BottomLeft, top + h - grown.BottomLeft, grown.BottomLeft, MathF.PI * 0.5f, segments[3], ring, ref at);

        float Grow(float radius) => radius > 0f ? MathF.Max(radius + grow, 0f) : 0f;
    }

    /// <summary>A quarter turn clockwise on screen from <paramref name="start"/>, as segments + 1 points.</summary>
    private static void Arc(float cx, float cy, float radius, float start, int segments, Span<Vector2> ring, ref int at)
    {
        for (int i = 0; i <= segments; i++)
        {
            float angle = segments == 0 ? start : start + MathF.PI * 0.5f * i / segments;
            ring[at++] = new Vector2(cx + radius * MathF.Cos(angle), cy + radius * MathF.Sin(angle));
        }
    }

    private static int EllipseSegments(Vector2 radii, float grow, float pixel)
    {
        int segments = ArcSegments(MathF.Max(radii.X, radii.Y) + grow, MathF.PI * 2f, pixel);
        segments = Math.Clamp(segments, MinimumEllipseSegments, MaximumEllipseSegments);
        return (segments + 3) / 4 * 4;
    }

    /// <summary>The ellipse with both radii grown by <paramref name="grow"/> (never below zero), one point per slot.</summary>
    private static void EllipseRing(Vector2 centre, Vector2 radii, float grow, Span<Vector2> ring)
    {
        float rx = MathF.Max(radii.X + grow, 0f);
        float ry = MathF.Max(radii.Y + grow, 0f);
        for (int i = 0; i < ring.Length; i++)
        {
            float angle = MathF.PI * 2f * i / ring.Length;
            ring[i] = new Vector2(centre.X + rx * MathF.Cos(angle), centre.Y + ry * MathF.Sin(angle));
        }
    }

    private static bool SamePoint(Vector2 a, Vector2 b) => Vector2.DistanceSquared(a, b) <= 1e-8f;

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static Vector4 Transparent(Vector4 color) => color with { W = 0f };
}
```

- [ ] **Step 4: Run to verify**

Run: `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~CanvasGeometryTests"`
Expected: `Passed!  - Failed: 0, Passed: 14`. If a count or area assertion is off, recheck the implementation against this plan before touching the expected numbers. They were measured.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/CanvasGeometry.cs tests/AcDream.App.Tests/UI/CanvasGeometryTests.cs
git commit -m "plugin canvas: shapes tessellated with a one-pixel fringe that fades to clear

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/CanvasGeometry.cs", "tests/AcDream.App.Tests/UI/CanvasGeometryTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~CanvasGeometryTests", "acceptanceCriteria": ["circle 282 verts within 1%, rim alphas", "square 30 verts, fringe positions", "rounded and clamped radii within 1%", "strokes centred; thin strokes fainter; thick stroke fills", "polygon windings, colours, outcomes, merging", "arc segment counts and caps", "zero area/thickness writes nothing"], "modelTier": "standard"}
```

---

### Task 5: Over another budget counts as an overrun

**Goal:** `UiDrawCallbackGuard.Invoke(context, draw, overBudget)` takes an optional predicate. It is asked after a call that ran to the end, and a true answer counts that call as an overrun, the same as a slow call. The trip message says which budget was exceeded.

**Files:**
- Modify: `src/AcDream.App/UI/UiDrawCallbackGuard.cs:60` (field), `:112-160` (`Invoke` with context), `:170-198` (`Invoke(Action)`: note the kind of overrun), `:200-204` (`TripForOverruns`)
- Test: `tests/AcDream.App.Tests/UI/UiDrawCallbackGuardTests.cs` (append)

**Acceptance Criteria:**
- [ ] With the time within budget, three calls whose predicate answers true trip the guard, with 1 and 2 consecutive overruns after the first two
- [ ] The trip report says "went over its drawing budget" and not "ms on"
- [ ] A call within both budgets clears the run
- [ ] Every existing `UiDrawCallbackGuardTests` passes unchanged

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~UiDrawCallbackGuardTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** (append inside `UiDrawCallbackGuardTests`)

```csharp
    [Fact]
    public void ACallOverAnotherBudgetCountsAsAnOverrunEvenWhenItIsQuick()
    {
        (UiDrawCallbackGuard guard, ManualClock clock, List<string> reports) = Build();
        UiRenderContext context = Context();
        clock.NextCallCosts = WithinBudget;

        for (int overrun = 1; overrun < UiDrawCallbackGuard.ConsecutiveOverrunsBeforeTrip; overrun++)
        {
            Assert.True(guard.Invoke(context, static _ => { }, static () => true));
            Assert.Equal(overrun, guard.ConsecutiveOverruns);
            Assert.False(guard.IsTripped);
        }
        Assert.True(guard.Invoke(context, static _ => { }, static () => true));

        Assert.True(guard.IsTripped);
        string report = Assert.Single(reports);
        Assert.Contains("went over its drawing budget", report, StringComparison.Ordinal);
        Assert.DoesNotContain(" ms on ", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ACallWithinBothBudgetsClearsTheRun()
    {
        (UiDrawCallbackGuard guard, ManualClock clock, _) = Build();
        UiRenderContext context = Context();
        clock.NextCallCosts = WithinBudget;

        guard.Invoke(context, static _ => { }, static () => true);
        guard.Invoke(context, static _ => { }, static () => true);
        guard.Invoke(context, static _ => { }, static () => false);

        Assert.Equal(0, guard.ConsecutiveOverruns);
        Assert.False(guard.IsTripped);
    }
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -m2 error`
Expected: `error CS1501: No overload for method 'Invoke' takes 3 arguments`.

- [ ] **Step 3: Implement** (`UiDrawCallbackGuard.cs`)

Field, after `private int _consecutiveOverruns;`:

```csharp
    private bool _lastOverrunWasSlow = true;
```

The drawing `Invoke`: add the parameter and its doc. Replace the tail from `if (LastMilliseconds <= BudgetMilliseconds)` to the end of the method:

```csharp
    /// <summary>
    /// Runs the callback unless it has been dropped. Returns true when it ran
    /// to the end and left the drawing state as it found it -- including on
    /// the overrun that drops it, because that call did draw.
    /// </summary>
    /// <param name="context">The drawing state the callback must leave as it found it.</param>
    /// <param name="draw">The callback.</param>
    /// <param name="overBudget">
    /// Asked after a call that ran to the end and left the state balanced:
    /// true when the call went over a budget other than time -- a canvas
    /// paint that drew more shapes than one paint may -- which counts as an
    /// overrun just as a slow call does.
    /// </param>
    internal bool Invoke(UiRenderContext context, Action<UiRenderContext> draw, Func<bool>? overBudget = null)
```

(The body is unchanged down to the unbalanced-state check. After that check:)

```csharp
        bool slow = LastMilliseconds > BudgetMilliseconds;
        if (!slow && overBudget?.Invoke() != true)
        {
            _consecutiveOverruns = 0;
            return true;
        }

        _consecutiveOverruns++;
        _lastOverrunWasSlow = slow;
        if (_consecutiveOverruns >= ConsecutiveOverrunsBeforeTrip)
            TripForOverruns();

        // It did draw, and it drew correctly -- it was only slow, or drew too much.
        return true;
    }
```

In `Invoke(Action callback)`, immediately before its `_consecutiveOverruns++;`, add:

```csharp
        _lastOverrunWasSlow = true;
```

`TripForOverruns`:

```csharp
    private void TripForOverruns() =>
        Trip(_lastOverrunWasSlow
            ? $"took more than {BudgetMilliseconds:0.##} ms on "
                + $"{_consecutiveOverruns} {_callUnit} in a row "
                + $"(last {LastMilliseconds:0.##} ms)"
            : $"went over its drawing budget on {_consecutiveOverruns} {_callUnit} in a row");
```

- [ ] **Step 4: Run to verify**

Run: `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~UiDrawCallbackGuardTests|FullyQualifiedName~PluginCanvas"`
Expected: `Passed!`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/UiDrawCallbackGuard.cs tests/AcDream.App.Tests/UI/UiDrawCallbackGuardTests.cs
git commit -m "ui: a drawing callback over another budget counts as an overrun

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/UiDrawCallbackGuard.cs", "tests/AcDream.App.Tests/UI/UiDrawCallbackGuardTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~UiDrawCallbackGuardTests", "acceptanceCriteria": ["predicate true trips after three quick calls", "trip message names the drawing budget", "within both budgets clears the run", "existing guard tests unchanged"], "modelTier": "standard"}
```

---

### Task 6: The painter draws shapes

**Goal:** `PluginPainter` implements the eight shape members. Input is validated; bad input is reported once per canvas through the element. Shapes go through `CanvasGeometry` and `UiRenderContext.DrawTriangles`, and a vertex budget per paint counts as a paint overrun when exceeded.

**Files:**
- Modify: `src/AcDream.App/UI/Layout/PluginPainter.cs` (usings; `CanvasShapeProblem` enum before the class; constants/fields; `Bind`/`Unbind`; members after `PopClip`; helpers after `TryResolve`)
- Modify: `src/AcDream.App/UI/Layout/PluginCanvasSurface.cs:64-80` (cached predicate), `:100-124` (`Repaint`)
- Modify: `src/AcDream.App/UI/Layout/PluginCanvasElement.cs:69` (fields), ctor (`_shapeProblems`), `:326` (repaint call), new `ReportShapeProblem` before `ReleaseTargets` (~402)
- Modify: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs:20` (`sealed class` → `sealed partial class`)
- Test: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.Shapes.cs` (create)

**Acceptance Criteria:**
- [ ] fill → image → circle → fill gives runs `[0, art, 0]` with counts `[6, 6, circle + 6]`
- [ ] A vertical gradient blends from red at the top to blue at the bottom (within 0.02)
- [ ] A circle under a pushed 50×50 clip stays inside it and covers a quarter of the circle (within 2%); a stroke partly off the canvas stays inside it; a shape wholly outside draws nothing
- [ ] Nine kinds of bad input draw nothing, do not throw or trip the guard, and give exactly one report per canvas across two frames; the report names the canvas, `FillPolygon` and "convex"
- [ ] Zero-area, zero-thickness and collinear shapes draw nothing and are not reported
- [ ] 400 circles of radius 40 draw exactly `⌊32768 / per-circle⌋` circles, report once with "32768 shape vertices", count one overrun, and trip the guard after three such paints with a second report naming the drawing budget
- [ ] A painter kept past its callback throws on shape members
- [ ] Every existing `PluginCanvas*` test passes

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginCanvas"` → `Passed!`

**Steps:**

- [ ] **Step 1: Make the test class partial**

In `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs` line 20:

```csharp
public sealed partial class PluginCanvasElementTests
```

- [ ] **Step 2: Write the failing tests** (`tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.Shapes.cs`)

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// Shapes on a canvas: in painter's order among fills and images, cut to
/// the clip and the canvas, budgeted per paint, and bad input drawing
/// nothing and reported once per canvas.
/// </summary>
public sealed partial class PluginCanvasElementTests
{
    private static int CircleVertices(double radius)
    {
        var triangles = new List<UiColorVertex>();
        CanvasGeometry.FillEllipse(0f, 0f, (float)(radius * 2), (float)(radius * 2), Vector4.One, 1f, triangles);
        return triangles.Count;
    }

    /// <summary>What a run's triangles cover, each weighted by its mean alpha.</summary>
    private static double CoveredArea(IReadOnlyList<float> verts)
    {
        int stride = TextRenderer.FloatsPerVertex;
        double sum = 0;
        for (int i = 0; i + 3 * stride <= verts.Count; i += 3 * stride)
        {
            double ax = verts[i], ay = verts[i + 1];
            double bx = verts[i + stride], by = verts[i + stride + 1];
            double cx = verts[i + 2 * stride], cy = verts[i + 2 * stride + 1];
            double area = Math.Abs((bx - ax) * (cy - ay) - (cx - ax) * (by - ay)) / 2;
            sum += area * (verts[i + 7] + verts[i + stride + 7] + verts[i + 2 * stride + 7]) / 3;
        }
        return sum;
    }

    [Fact]
    public void ShapesKeepTheirPlaceAmongFillsAndImages()
    {
        var harness = new Harness();
        PluginImage art = harness.Registry.ImagesFor(harness.Owner).FromClientArt(1u);
        harness.Mount(Hud(), painter =>
        {
            painter.FillRect(new PluginRect(0, 0, 200, 100), new PluginColor(0, 0, 0, 160));
            painter.DrawImage(art, new PluginRect(10, 10, 32, 32), PluginColor.White);
            painter.FillCircle(new PluginPoint(100, 50), 30, PluginColor.White);
            painter.FillRect(new PluginRect(0, 90, 200, 10), PluginColor.White);
        });

        harness.Frame();

        Assert.Equal([0u, FakeImageBackend.ArtTexture, 0u], harness.SurfaceRuns.Select(run => run.Texture).ToArray());
        Assert.Equal([6, 6, CircleVertices(30) + 6], harness.SurfaceRuns.Select(run => run.VertexCount).ToArray());
    }

    [Fact]
    public void AGradientRunsFromOneColourToTheOther()
    {
        var harness = new Harness();
        harness.Mount(Hud(), painter => painter.FillRectGradient(
            new PluginRect(0, 0, 200, 100), new PluginColor(255, 0, 0), new PluginColor(0, 0, 255),
            PluginGradientDirection.Vertical));

        harness.Frame();

        IReadOnlyList<float> verts = Assert.Single(harness.Surface.Renderer.DebugSpriteSegmentVerts).Verts;
        for (int i = 0; i < verts.Count; i += TextRenderer.FloatsPerVertex)
        {
            // Solid from y = 0.5 (red) to y = 99.5 (blue); the fringe keeps its edge's colour.
            float expectedRed = 1f - Math.Clamp((verts[i + 1] - 0.5f) / 99f, 0f, 1f);
            Assert.InRange(verts[i + 4], expectedRed - 0.02f, expectedRed + 0.02f);
            Assert.InRange(verts[i + 4] + verts[i + 6], 1f - 1e-4f, 1f + 1e-4f);
        }
    }

    [Fact]
    public void AShapeIsCutToThePushedClip()
    {
        var harness = new Harness();
        harness.Mount(Hud(), painter =>
        {
            painter.PushClip(new PluginRect(0, 0, 50, 50));
            painter.FillCircle(new PluginPoint(50, 50), 20, PluginColor.White);
            painter.PopClip();
        });

        harness.Frame();

        IReadOnlyList<float> verts = Assert.Single(harness.Surface.Renderer.DebugSpriteSegmentVerts).Verts;
        for (int i = 0; i < verts.Count; i += TextRenderer.FloatsPerVertex)
        {
            Assert.InRange(verts[i], -1e-3f, 50f + 1e-3f);
            Assert.InRange(verts[i + 1], -1e-3f, 50f + 1e-3f);
        }
        double quarter = Math.PI * 20 * 20 / 4;
        Assert.InRange(CoveredArea(verts), quarter * 0.98, quarter * 1.02);
    }

    [Fact]
    public void AShapeStaysInsideTheCanvasAndOneWhollyOutsideDrawsNothing()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, _) = harness.Mount(Hud(), painter =>
            painter.StrokeRoundedRect(
                new PluginRect(-20, 10, 300, 60), PluginCornerRadii.Uniform(10), PluginColor.White, 4f));

        harness.Frame();

        IReadOnlyList<float> verts = Assert.Single(harness.Surface.Renderer.DebugSpriteSegmentVerts).Verts;
        for (int i = 0; i < verts.Count; i += TextRenderer.FloatsPerVertex)
        {
            Assert.InRange(verts[i], -1e-3f, 200f + 1e-3f);
            Assert.InRange(verts[i + 1], -1e-3f, 100f + 1e-3f);
        }

        var outside = new Harness();
        outside.Mount(Hud(), painter => painter.FillEllipse(new PluginRect(500, 500, 10, 10), PluginColor.White));
        outside.Frame();
        Assert.Empty(outside.SurfaceRuns);
        Assert.True(registration.IsAvailable);
    }

    [Fact]
    public void BadShapeInputDrawsNothingNeverThrowsAndIsReportedOncePerCanvas()
    {
        var harness = new Harness();
        Action<IPluginPainter> paint = painter =>
        {
            painter.FillPolygon(
                [new PluginPoint(0, 0), new PluginPoint(20, 0), new PluginPoint(10, 5), new PluginPoint(20, 20), new PluginPoint(0, 20)],
                PluginColor.White);
            painter.FillPolygon([new PluginPoint(0, 0), new PluginPoint(20, 0)], PluginColor.White);
            painter.FillPolygon(
                [new PluginPoint(0, 0), new PluginPoint(20, 0), new PluginPoint(0, 20)], [PluginColor.White]);
            painter.FillRoundedRect(new PluginRect(0, 0, -5, 5), PluginCornerRadii.Uniform(1), PluginColor.White);
            painter.FillRoundedRect(new PluginRect(0, 0, 5, 5), PluginCornerRadii.Uniform(-1), PluginColor.White);
            painter.FillEllipse(new PluginRect(double.NaN, 0, 5, 5), PluginColor.White);
            painter.StrokeEllipse(new PluginRect(0, 0, 5, 5), PluginColor.White, -1f);
            painter.FillCircle(new PluginPoint(5, 5), double.PositiveInfinity, PluginColor.White);
            painter.FillRectGradient(
                new PluginRect(0, 0, 5, 5), PluginColor.White, PluginColor.White, (PluginGradientDirection)7);
        };
        (PluginCanvasRegistration hud, PluginCanvasElement element) = harness.Mount(Hud(), paint);
        harness.Mount(new PluginCanvasDescriptor("second", 50, 50), paint);

        harness.Frame();
        hud.Invalidate();
        harness.Frame();

        Assert.Empty(harness.SurfaceRuns);
        Assert.False(element.Guard.IsTripped);
        Assert.True(hud.IsAvailable);
        Assert.Equal(2, harness.Reports.Count);
        string hudReport = Assert.Single(harness.Reports, report => report.Contains("example.plugin/hud", StringComparison.Ordinal));
        Assert.Contains("FillPolygon", hudReport, StringComparison.Ordinal);
        Assert.Contains("convex", hudReport, StringComparison.Ordinal);
        Assert.Single(harness.Reports, report => report.Contains("example.plugin/second", StringComparison.Ordinal));
    }

    [Fact]
    public void ShapesWithNoAreaOrThicknessDrawNothingAndAreNotReported()
    {
        var harness = new Harness();
        harness.Mount(Hud(), painter =>
        {
            painter.FillEllipse(new PluginRect(0, 0, 0, 10), PluginColor.White);
            painter.StrokeRoundedRect(new PluginRect(0, 0, 10, 10), PluginCornerRadii.Uniform(2), PluginColor.White, 0f);
            painter.FillPolygon([new PluginPoint(0, 0), new PluginPoint(10, 0), new PluginPoint(20, 0)], PluginColor.White);
            painter.FillRectGradient(
                new PluginRect(0, 0, 10, 0), PluginColor.White, PluginColor.Transparent, PluginGradientDirection.Horizontal);
            painter.FillCircle(new PluginPoint(5, 5), 0, PluginColor.White);
        });

        harness.Frame();

        Assert.Empty(harness.SurfaceRuns);
        Assert.Empty(harness.Reports);
    }

    [Fact]
    public void APaintOverTheShapeBudgetDrawsWhatFitsReportsOnceAndCountsAsAnOverrun()
    {
        var harness = new Harness();
        int perCircle = CircleVertices(40);
        (PluginCanvasRegistration registration, PluginCanvasElement element) = harness.Mount(Hud(), painter =>
        {
            for (int i = 0; i < 400; i++)
                painter.FillCircle(new PluginPoint(100, 50), 40, PluginColor.White);
        });

        harness.Frame();

        int fits = PluginPainter.MaximumShapeVerticesPerPaint / perCircle;
        Assert.Equal(fits * perCircle, harness.SurfaceRuns.Sum(run => run.VertexCount));
        Assert.Equal(1, element.Guard.ConsecutiveOverruns);
        Assert.False(element.Guard.IsTripped);
        string report = Assert.Single(harness.Reports);
        Assert.Contains("example.plugin/hud", report, StringComparison.Ordinal);
        Assert.Contains("32768 shape vertices", report, StringComparison.Ordinal);

        for (int frame = 1; frame < UiDrawCallbackGuard.ConsecutiveOverrunsBeforeTrip; frame++)
        {
            registration.Invalidate();
            harness.Frame();
        }

        Assert.True(element.Guard.IsTripped);
        Assert.True(registration.IsDropped);
        Assert.Equal(2, harness.Reports.Count);
        Assert.Contains("drawing budget", harness.Reports[1], StringComparison.Ordinal);
    }

    [Fact]
    public void APainterKeptPastItsCallbackThrowsOnShapesToo()
    {
        var harness = new Harness();
        IPluginPainter? kept = null;
        harness.Mount(Hud(), painter => kept = painter);
        harness.Frame();

        Assert.NotNull(kept);
        Assert.Throws<InvalidOperationException>(() => kept.FillCircle(new PluginPoint(5, 5), 3, PluginColor.White));
        Assert.Throws<InvalidOperationException>(() => kept.FillPolygon(
            [new PluginPoint(0, 0), new PluginPoint(4, 0), new PluginPoint(0, 4)], PluginColor.White));
        Assert.Throws<InvalidOperationException>(() => kept.StrokeRoundedRect(
            new PluginRect(0, 0, 4, 4), PluginCornerRadii.Uniform(1), PluginColor.White));
    }
}
```

- [ ] **Step 3: Run to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -m2 error`
Expected: `error CS0117: 'PluginPainter' does not contain a definition for 'MaximumShapeVerticesPerPaint'`.

- [ ] **Step 4: Painter** (`PluginPainter.cs`)

Usings: add `using System.Globalization;`, `using System.Runtime.InteropServices;`, `using AcDream.App.Rendering;`.

Before the class:

```csharp
/// <summary>What went wrong with a shape, for the canvas to report once per kind.</summary>
internal enum CanvasShapeProblem
{
    /// <summary>A shape was given input it cannot draw: not finite, negative, a wrong count, not convex.</summary>
    InvalidInput,

    /// <summary>A paint drew more shape vertices than one paint may.</summary>
    VertexBudget,
}
```

Class doc: after the paragraph on coordinates, add:

```csharp
///
/// <para>Shapes are tessellated here into anti-aliased triangles
/// (<see cref="CanvasGeometry"/>). Input they cannot draw draws nothing
/// and is handed to the problem sink the surface binds, never thrown: a
/// throw would drop the whole canvas.</para>
```

Constants and fields, after `private int _height;`:

```csharp
    /// <summary>
    /// The most shape vertices one paint may draw. Shape triangles go into
    /// the frame's vertex ring, 16 MiB per frame slot and shared by
    /// everything drawn in that frame (<c>GpuMemoryProfile.RingCapacityBytesPerSlot</c>);
    /// at 32 bytes a vertex this is 1 MiB of it -- about 75 large circles or
    /// 130 rounded panels. Tessellating and clipping this many takes about
    /// 0.6 ms, well inside the paint budget.
    /// </summary>
    internal const int MaximumShapeVerticesPerPaint = 32_768;

    /// <summary>Canvases are painted at their own size, so one device pixel of the target is one canvas pixel.</summary>
    private const float DevicePixel = 1f;

    private readonly List<UiColorVertex> _shape = new(1024);
    private Action<CanvasShapeProblem, string>? _shapeProblems;
    private int _shapeVertices;
```

`Bind` and `Unbind` become:

```csharp
    /// <summary>Points the painter at one repaint. Only the surface calls this.</summary>
    internal void Bind(
        UiRenderContext context, UiDatFont? font, PluginImages? images, int width, int height,
        Action<CanvasShapeProblem, string>? shapeProblems = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _font = font;
        _images = images;
        _width = width;
        _height = height;
        _shapeProblems = shapeProblems;
        _shapeVertices = 0;
        ShapeBudgetExceeded = false;
    }

    /// <summary>Forgets the repaint; every call after this throws.</summary>
    internal void Unbind()
    {
        _context = null;
        _font = null;
        _images = null;
        _shapeProblems = null;
    }

    /// <summary>
    /// True once this repaint drew as many shape vertices as one paint may;
    /// later shapes in it are skipped. Kept after <see cref="Unbind"/> for the
    /// guard to read, cleared by the next <see cref="Bind"/>.
    /// </summary>
    internal bool ShapeBudgetExceeded { get; private set; }
```

Members, after `PopClip`:

```csharp
    public void FillPolygon(ReadOnlySpan<PluginPoint> points, PluginColor color)
    {
        UiRenderContext context = Context;
        Span<Vector4> colors = stackalloc Vector4[Math.Min(points.Length, CanvasGeometry.MaximumPolygonPoints)];
        colors.Fill(ToVector(color));
        FillPolygonCore(context, points, colors);
    }

    public void FillPolygon(ReadOnlySpan<PluginPoint> points, ReadOnlySpan<PluginColor> colors)
    {
        UiRenderContext context = Context;
        if (colors.Length != points.Length)
        {
            Reject(nameof(FillPolygon), string.Create(
                CultureInfo.InvariantCulture, $"{colors.Length} colours for {points.Length} points"));
            return;
        }
        Span<Vector4> converted = stackalloc Vector4[Math.Min(colors.Length, CanvasGeometry.MaximumPolygonPoints)];
        for (int i = 0; i < converted.Length; i++)
            converted[i] = ToVector(colors[i]);
        FillPolygonCore(context, points, converted);
    }

    public void FillRoundedRect(PluginRect rect, PluginCornerRadii radii, PluginColor color)
    {
        UiRenderContext context = Context;
        if (!TryConvert(rect, nameof(FillRoundedRect), out float x, out float y, out float w, out float h)
            || !TryConvert(radii, nameof(FillRoundedRect), out CanvasCornerRadii corners)
            || ShapeBudgetExceeded)
            return;
        _shape.Clear();
        CanvasGeometry.FillRoundedRect(x, y, w, h, corners, ToVector(color), DevicePixel, _shape);
        Emit(context);
    }

    public void StrokeRoundedRect(PluginRect rect, PluginCornerRadii radii, PluginColor color, float thickness = 1f)
    {
        UiRenderContext context = Context;
        if (!TryConvert(rect, nameof(StrokeRoundedRect), out float x, out float y, out float w, out float h)
            || !TryConvert(radii, nameof(StrokeRoundedRect), out CanvasCornerRadii corners)
            || !CheckThickness(thickness, nameof(StrokeRoundedRect))
            || ShapeBudgetExceeded)
            return;
        _shape.Clear();
        CanvasGeometry.StrokeRoundedRect(x, y, w, h, corners, ToVector(color), thickness, DevicePixel, _shape);
        Emit(context);
    }

    public void FillEllipse(PluginRect bounds, PluginColor color)
    {
        UiRenderContext context = Context;
        if (!TryConvert(bounds, nameof(FillEllipse), out float x, out float y, out float w, out float h)
            || ShapeBudgetExceeded)
            return;
        _shape.Clear();
        CanvasGeometry.FillEllipse(x, y, w, h, ToVector(color), DevicePixel, _shape);
        Emit(context);
    }

    public void StrokeEllipse(PluginRect bounds, PluginColor color, float thickness = 1f)
    {
        UiRenderContext context = Context;
        if (!TryConvert(bounds, nameof(StrokeEllipse), out float x, out float y, out float w, out float h)
            || !CheckThickness(thickness, nameof(StrokeEllipse))
            || ShapeBudgetExceeded)
            return;
        _shape.Clear();
        CanvasGeometry.StrokeEllipse(x, y, w, h, ToVector(color), thickness, DevicePixel, _shape);
        Emit(context);
    }

    public void FillCircle(PluginPoint center, double radius, PluginColor color)
    {
        UiRenderContext context = Context;
        if (!double.IsFinite(radius) || radius < 0)
        {
            Reject(nameof(FillCircle), "a radius that is negative or not a finite number");
            return;
        }
        var bounds = new PluginRect(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        if (!TryConvert(bounds, nameof(FillCircle), out float x, out float y, out float w, out float h)
            || ShapeBudgetExceeded)
            return;
        _shape.Clear();
        CanvasGeometry.FillEllipse(x, y, w, h, ToVector(color), DevicePixel, _shape);
        Emit(context);
    }

    public void FillRectGradient(PluginRect rect, PluginColor from, PluginColor to, PluginGradientDirection direction)
    {
        UiRenderContext context = Context;
        if (!TryConvert(rect, nameof(FillRectGradient), out float x, out float y, out float w, out float h))
            return;
        if (direction is not (PluginGradientDirection.Horizontal or PluginGradientDirection.Vertical))
        {
            Reject(nameof(FillRectGradient), "a direction that is neither horizontal nor vertical");
            return;
        }
        if (ShapeBudgetExceeded) return;

        Vector4 start = ToVector(from);
        Vector4 end = ToVector(to);
        Span<Vector2> corners = [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)];
        Span<Vector4> colors = stackalloc Vector4[4];
        if (direction == PluginGradientDirection.Horizontal)
        {
            colors[0] = start; colors[1] = end; colors[2] = end; colors[3] = start;
        }
        else
        {
            colors[0] = start; colors[1] = start; colors[2] = end; colors[3] = end;
        }
        _shape.Clear();
        CanvasGeometry.FillConvexPolygon(corners, colors, DevicePixel, _shape);
        Emit(context);
    }
```

Helpers, after `TryResolve`:

```csharp
    private void FillPolygonCore(UiRenderContext context, ReadOnlySpan<PluginPoint> points, ReadOnlySpan<Vector4> colors)
    {
        const string member = nameof(FillPolygon);
        if (points.Length < 3)
        {
            Reject(member, "fewer than three points");
            return;
        }
        if (points.Length > CanvasGeometry.MaximumPolygonPoints)
        {
            Reject(member, string.Create(
                CultureInfo.InvariantCulture, $"more than {CanvasGeometry.MaximumPolygonPoints} points"));
            return;
        }
        Span<Vector2> corners = stackalloc Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            corners[i] = new Vector2((float)points[i].X, (float)points[i].Y);
            if (!float.IsFinite(corners[i].X) || !float.IsFinite(corners[i].Y))
            {
                Reject(member, "a point that is not a finite number");
                return;
            }
        }
        if (ShapeBudgetExceeded) return;

        _shape.Clear();
        if (CanvasGeometry.FillConvexPolygon(corners, colors, DevicePixel, _shape) == CanvasShapeOutcome.NotConvex)
        {
            Reject(member, "points that do not make a convex polygon");
            return;
        }
        Emit(context);
    }

    /// <summary>
    /// Hands the shape just tessellated to the context, unless it would take
    /// the paint past its vertex budget; then it and every later shape in
    /// this paint are skipped, and the surface tells the guard.
    /// </summary>
    private void Emit(UiRenderContext context)
    {
        int count = _shape.Count;
        if (count == 0) return;
        if (_shapeVertices + count > MaximumShapeVerticesPerPaint)
        {
            ShapeBudgetExceeded = true;
            _shapeProblems?.Invoke(CanvasShapeProblem.VertexBudget, string.Create(
                CultureInfo.InvariantCulture,
                $"one paint drew more than {MaximumShapeVerticesPerPaint} shape vertices; the shapes past that were not drawn"));
            return;
        }
        _shapeVertices += count;
        context.DrawTriangles(CollectionsMarshal.AsSpan(_shape));
    }

    private bool TryConvert(PluginRect rect, string member, out float x, out float y, out float width, out float height)
    {
        x = (float)rect.X;
        y = (float)rect.Y;
        width = (float)rect.Width;
        height = (float)rect.Height;
        if (!(float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(width) && float.IsFinite(height)))
            return Reject(member, "a rectangle that is not a finite number");
        if (width < 0f || height < 0f)
            return Reject(member, "a rectangle of negative size");
        return true;
    }

    private bool TryConvert(PluginCornerRadii radii, string member, out CanvasCornerRadii corners)
    {
        corners = new CanvasCornerRadii(
            (float)radii.TopLeft, (float)radii.TopRight, (float)radii.BottomRight, (float)radii.BottomLeft);
        if (!(float.IsFinite(corners.TopLeft) && float.IsFinite(corners.TopRight)
              && float.IsFinite(corners.BottomRight) && float.IsFinite(corners.BottomLeft)))
            return Reject(member, "a corner radius that is not a finite number");
        if (corners.TopLeft < 0f || corners.TopRight < 0f || corners.BottomRight < 0f || corners.BottomLeft < 0f)
            return Reject(member, "a negative corner radius");
        return true;
    }

    private bool CheckThickness(float thickness, string member) =>
        (float.IsFinite(thickness) && thickness >= 0f)
        || Reject(member, "a thickness that is negative or not a finite number");

    private bool Reject(string member, string what)
    {
        _shapeProblems?.Invoke(CanvasShapeProblem.InvalidInput, $"{member} was given {what}");
        return false;
    }
```

- [ ] **Step 5: Surface** (`PluginCanvasSurface.cs`)

Field after `_painter`:

```csharp
    private readonly Func<bool> _overShapeBudget;
```

At the end of the constructor:

```csharp
        _overShapeBudget = () => _painter.ShapeBudgetExceeded;
```

`Repaint` gains a trailing parameter and passes it on. Add to its summary: "A paint that drew more shape vertices than one paint may counts against the guard as an overrun."

```csharp
    internal bool Repaint(
        PluginCanvasRegistration registration,
        IGpuRenderTarget target,
        UiDrawCallbackGuard guard,
        PluginImages? images,
        Action<CanvasShapeProblem, string>? shapeProblems = null)
```

```csharp
        _painter.Bind(_context, Font, images, width, height, shapeProblems);
```

```csharp
            drew = paint is not null && guard.Invoke(_context, _ => paint(_painter), _overShapeBudget);
```

- [ ] **Step 6: Element** (`PluginCanvasElement.cs`)

Fields after `private readonly Action? _pointerRelease;`:

```csharp
    private readonly Action<CanvasShapeProblem, string> _shapeProblems;
    private bool _reportedBadShape;
    private bool _reportedShapeBudget;
```

In the constructor, after `_modifiers = …;`:

```csharp
        _shapeProblems = ReportShapeProblem;
```

The repaint call (named, so it sits beside other trailing arguments):

```csharp
        bool drew = _surface.Repaint(_registration, target.Target, _guard, _images(), shapeProblems: _shapeProblems);
```

Before `ReleaseTargets`:

```csharp
    /// <summary>
    /// Where the painter says a shape went wrong. Each kind is reported once
    /// per canvas: the paint callback runs again every time the plugin
    /// invalidates, and the same bad shape would otherwise fill the log.
    /// </summary>
    private void ReportShapeProblem(CanvasShapeProblem problem, string detail)
    {
        string canvas = $"{_registration.Owner.Id}/{_registration.CanvasId}";
        switch (problem)
        {
            case CanvasShapeProblem.InvalidInput when !_reportedBadShape:
                _reportedBadShape = true;
                _report(
                    $"Plugin canvas '{canvas}': {detail}, so that shape drew nothing. "
                    + "A shape given input it cannot draw draws nothing; this is reported once per canvas.");
                break;
            case CanvasShapeProblem.VertexBudget when !_reportedShapeBudget:
                _reportedShapeBudget = true;
                _report(
                    $"Plugin canvas '{canvas}': {detail}, and the paint counts as over its budget. "
                    + "This is reported once per canvas.");
                break;
        }
    }
```

- [ ] **Step 7: Run to verify**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | tail -3 && dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~PluginCanvas|FullyQualifiedName~UiDrawCallbackGuardTests"`
Expected: `0 Warning(s)`; `Passed!`.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App/UI/Layout/PluginPainter.cs src/AcDream.App/UI/Layout/PluginCanvasSurface.cs src/AcDream.App/UI/Layout/PluginCanvasElement.cs tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.Shapes.cs
git commit -m "plugin canvas: polygons, rounded rectangles, ellipses and gradients, anti-aliased

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/Layout/PluginPainter.cs", "src/AcDream.App/UI/Layout/PluginCanvasSurface.cs", "src/AcDream.App/UI/Layout/PluginCanvasElement.cs", "tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs", "tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.Shapes.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~PluginCanvas", "acceptanceCriteria": ["runs [0,art,0] with circle in the last run", "vertical gradient red to blue", "clip and canvas bounds respected; outside draws nothing", "bad input: nothing drawn, one report per canvas naming FillPolygon/convex", "zero-area shapes silent", "vertex budget: fits drawn, one report, overrun counted, trips after three", "kept painter throws on shapes", "existing canvas tests green"], "modelTier": "standard"}
```

---

### Task 7: The fringe on a real device

**Goal:** A `Lane=Vulkan` proof that a straight edge through a pixel centre covers half of that pixel and that a circle's rim is partly covered. It runs through the real painter, context, renderer and premultiplied target.

**Files:**
- Test: `tests/AcDream.App.Tests/Rendering/Gpu/Vk/PluginCanvasShapeOffscreenTests.cs` (create)

**Acceptance Criteria:**
- [ ] Rectangle `(0, 0, 8.5, 16)`: pixel (7, 4) is 255 in every channel, (8, 4) is 128 ± 2, and (9, 4) is 0
- [ ] Circle centred (8, 8), radius √20.5: centre pixel 255, pixel (0, 0) 0, rim pixels (3, 8) and (12, 8) have alpha in [80, 150] with colour equal to alpha ± 2 (premultiplied white)
- [ ] Passes locally under MoltenVK; skipped by the portable filter

**Verify:** `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginCanvasShapeOffscreenTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the test**

The expected values come from sampling the tessellation at pixel centres: 1.0, 0.5 and 0.0 for the rectangle, and 0.428 at the rim. Both shapes are symmetric under a vertical flip, so the test does not depend on the readback's row order.

```csharp
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Rendering.Gpu.Vk;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Rendering.Gpu.Vk;

/// <summary>
/// Shapes painted on a real device: solid inside, clear outside, and
/// partly covered across the one-pixel fringe at their edges.
/// </summary>
public sealed class PluginCanvasShapeOffscreenTests
{
    private const int Extent = 16;

    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    [Trait("Lane", "Vulkan")]
    [Fact]
    public void AStraightEdgeThroughAPixelCentreCoversHalfOfIt()
    {
        byte[] painted = Paint(painter => painter.FillRoundedRect(
            new PluginRect(0, 0, 8.5, Extent), default, PluginColor.White));

        AssertNear([255, 255, 255, 255], Pixel(painted, 7, 4), 1);
        AssertNear([128, 128, 128, 128], Pixel(painted, 8, 4), 2);
        AssertNear([0, 0, 0, 0], Pixel(painted, 9, 4), 0);
    }

    [Trait("Lane", "Vulkan")]
    [Fact]
    public void ACircleIsSolidInsideClearOutsideAndPartlyCoveredAtItsRim()
    {
        byte[] painted = Paint(painter => painter.FillCircle(
            new PluginPoint(8, 8), Math.Sqrt(20.5), PluginColor.White));

        AssertNear([255, 255, 255, 255], Pixel(painted, 8, 8), 1);
        AssertNear([0, 0, 0, 0], Pixel(painted, 0, 0), 0);
        foreach ((int x, int y) in new[] { (3, 8), (12, 8) })
        {
            ReadOnlySpan<byte> rim = Pixel(painted, x, y);
            Assert.InRange(rim[3], (byte)80, (byte)150);
            for (int channel = 0; channel < 3; channel++)
                Assert.InRange(rim[channel], (byte)(rim[3] - 2), (byte)(rim[3] + 2));
        }
    }

    private static byte[] Paint(Action<IPluginPainter> paint)
    {
        using var host = HeadlessVulkanTestHost.Create(HeadlessVulkanTestHost.CommittedShaderDirectory());
        VulkanGpuDevice device = host.Device;
        var frames = new FrameSource();
        using var renderer = new TextRenderer(device, frames, "unused", GpuBlendMode.StraightAlphaIntoPremultiplied);
        using IGpuRenderTarget canvas = device.CreateRenderTarget(new GpuRenderTargetDescription(
            "canvas-shape", Extent, Extent, GpuTextureFormat.Rgba8UnormRenderTarget, DepthFormat: null, SampleCount: 1));
        var size = new Vector2(Extent, Extent);
        var context = new UiRenderContext(renderer, size);
        var painter = new PluginPainter();

        using (IGpuFrame frame = device.BeginFrame())
        {
            frames.CurrentFrame = frame;
            renderer.Begin(size);
            context.Begin(size, null);
            context.PushClip(0f, 0f, Extent, Extent);
            painter.Bind(context, null, null, Extent, Extent);
            paint(painter);
            painter.Unbind();
            context.PopClip();
            renderer.FlushTo(canvas, Vector4.Zero, null, "canvas-shape-paint");
            frames.CurrentFrame = null;
        }
        device.WaitIdle();
        return host.ReadBack(canvas, Extent, Extent);
    }

    private static ReadOnlySpan<byte> Pixel(byte[] image, int x, int y) => image.AsSpan((y * Extent + x) * 4, 4);

    private static void AssertNear(int[] expected, ReadOnlySpan<byte> actual, int tolerance)
    {
        for (int channel = 0; channel < 4; channel++)
        {
            Assert.True(
                Math.Abs(expected[channel] - actual[channel]) <= tolerance,
                $"channel {channel}: expected {expected[channel]}±{tolerance}, read {actual[channel]} "
                + $"(pixel {actual[0]},{actual[1]},{actual[2]},{actual[3]})");
        }
    }
}
```

- [ ] **Step 2: Run it**

Run: `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginCanvasShapeOffscreenTests"`
Expected: `Passed!  - Failed: 0, Passed: 2`. If the half-covered pixel reads near 64 rather than 128, PR 0's blend is not in effect on this branch. Stop and check Task 0.

- [ ] **Step 3: Commit**

```bash
git add tests/AcDream.App.Tests/Rendering/Gpu/Vk/PluginCanvasShapeOffscreenTests.cs
git commit -m "tests: a shape's edge is partly covered on a real device

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["tests/AcDream.App.Tests/Rendering/Gpu/Vk/PluginCanvasShapeOffscreenTests.cs"], "verifyCommand": "DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~PluginCanvasShapeOffscreenTests", "acceptanceCriteria": ["rect edge pixel 128±2 between 255 and 0", "circle centre 255, corner 0, rim alpha 80-150 premultiplied"], "modelTier": "standard"}
```

---

### Task 8: Documentation

**Goal:** Plugin authors can find and use shapes from the docs, and the markup guide's test list names the new suites.

**Files:**
- Modify: `docs/plugin-api.md` (new `### Shapes` directly before `### Pointer input`, ~line 1355)
- Modify: `docs/plugin-ui-markup.md` (Tests section: a sentence after the one ending ``(`Lane=Vulkan`).``, ~line 405)

**Acceptance Criteria:**
- [ ] `### Shapes` documents all eight members, `PluginCornerRadii` and CSS-style clamping, convex 3–64 points and either winding, per-corner colours, centred strokes and thin-stroke behaviour, one-pixel anti-aliasing (existing primitives keep hard edges), silent no-ops, the report-once rule, the 32,768-vertex budget counting as an overrun, and older hosts drawing nothing
- [ ] The Canvases primitives paragraph is not edited (PR 1 edits it)
- [ ] The Tests paragraph names the six new suites

**Verify:** `grep -n "^### Shapes" docs/plugin-api.md && grep -c "CanvasGeometryTests" docs/plugin-ui-markup.md` → one heading line and `1`

**Steps:**

- [ ] **Step 1: Add `### Shapes`** (immediately before `### Pointer input`)

````markdown
### Shapes

Besides rectangles and lines, the painter draws shapes with smooth,
anti-aliased edges:

```csharp
painter.FillRoundedRect(new PluginRect(0, 0, 200, 60), PluginCornerRadii.Uniform(8), new PluginColor(0, 0, 0, 160));
painter.StrokeRoundedRect(new PluginRect(0, 0, 200, 60), PluginCornerRadii.Uniform(8), PluginColor.White, 1.5f);
painter.FillRectGradient(new PluginRect(8, 40, 184 * healthFraction, 12),
    new PluginColor(200, 30, 30), new PluginColor(255, 120, 60), PluginGradientDirection.Horizontal);
painter.FillCircle(new PluginPoint(180, 20), 6, online ? new PluginColor(60, 200, 90) : new PluginColor(120, 120, 120));
painter.FillPolygon([new PluginPoint(10, 10), new PluginPoint(20, 20), new PluginPoint(10, 30)], PluginColor.White);
```

`FillPolygon` fills a convex polygon of 3 to 64 points, given in order
round its outline in either direction; its second overload takes one
colour per point and blends between them. `FillRoundedRect` and
`StrokeRoundedRect` take a radius per corner (`PluginCornerRadii`, or
`PluginCornerRadii.Uniform` for all four). If the radii are too large for
the rectangle they are scaled down together, as CSS does, and zero gives a
square corner. `FillEllipse`, `StrokeEllipse` and `FillCircle` draw
ellipses and circles. `FillRectGradient` fills a rectangle blending from
one colour to another, across it or down it.

The edges of these shapes are anti-aliased over one pixel; `FillRect`,
`StrokeRect` and `DrawLine` keep their hard edges. Strokes are centred on
the outline, so half the thickness falls outside the shape. A stroke
thinner than a pixel is drawn one pixel wide and proportionally fainter.
A shape with no area or no thickness simply draws nothing.

Some input cannot be drawn: a polygon that is not convex or has too few or
too many points, a colour count that does not match the point count, a
negative size, radius or thickness, or a coordinate that is not a finite
number. Such a shape draws nothing, never throws, and is reported once per
canvas in the client's log.

One paint may draw at most 32,768 shape vertices; a large circle takes a
few hundred. Past that limit the remaining shapes are skipped and the log
says so once. The paint also counts against the paint budget, like a slow
one, so a callback that keeps going over is dropped. On a host that
predates shapes, they draw nothing.
````

- [ ] **Step 2: Test list** (`docs/plugin-ui-markup.md`, directly after the line `(`Lane=Vulkan`).` in the Tests section)

```markdown
Canvas shapes are covered by `UI/CanvasGeometryTests`,
`Rendering/ColoredTriangleClipperTests`, `Rendering/TextRendererTrianglesTests`,
`UI/UiRenderContextTrianglesTests` and the shape cases in
`UI/Layout/PluginCanvasElementTests` under `tests/AcDream.App.Tests/`, by
`PluginCanvasShapesContractTests` for the inert answers of an older host,
and, on a Vulkan device, by `Rendering/Gpu/Vk/PluginCanvasShapeOffscreenTests`
(`Lane=Vulkan`).
```

- [ ] **Step 3: Commit**

```bash
git add docs/plugin-api.md docs/plugin-ui-markup.md
git commit -m "docs: canvas shapes

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["docs/plugin-api.md", "docs/plugin-ui-markup.md"], "verifyCommand": "grep -n \"^### Shapes\" docs/plugin-api.md", "acceptanceCriteria": ["Shapes section covers members, radii, polygons, strokes, AA, no-ops, report-once, budget, older hosts", "primitives paragraph untouched", "test list names the new suites"], "modelTier": "mechanical"}
```

---

### Task 9: Full verification and push

**Goal:** A green build, green portable gate and green Vulkan lane; the branch pushed; the PR description drafted; the user asked where the PR goes.

**Files:** none

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` reports 0 warnings, 0 errors
- [ ] Portable filter: every assembly `Passed!`. Known Mac-only failures are compared against the base branch and reported, not fixed here
- [ ] `Lane=Vulkan` on `AcDream.App.Tests`: `Passed!` under MoltenVK
- [ ] No shader file changed against the base; no `packages.*.lock.json` staged
- [ ] Branch pushed; PR opened only after the user picks the repository

**Verify:** the commands below with the stated output

**Steps:**

- [ ] **Step 1: Build, gate, Vulkan lane**

```bash
BASE=$(git merge-base HEAD origin/painter-v2/canvas-alpha)
dotnet build AcDream.slnx -c Release 2>&1 | tail -4
dotnet test AcDream.slnx -c Release --no-build --filter "Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure" 2>&1 | grep -E "Passed!|Failed!"
DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --no-build --filter "Lane=Vulkan" 2>&1 | grep -E "Passed!|Failed!"
git diff "$BASE" --stat -- src/AcDream.App/Rendering/Shaders/ | wc -l
git checkout -- '*.lock.json' && git status --short
```

Expected: `0 Warning(s)`; every line `Passed!` apart from the known Mac-only failures (Launcher pipe tests without `TMPDIR=/tmp/acdt/`, HostParity IPC timeouts, and the three App flakes that pass when rerun alone). Also `0` shader lines and a clean status.

- [ ] **Step 2: Push**

```bash
git push -u origin painter-v2/shapes
```

- [ ] **Step 3: Draft the PR description, then ask where to open it**

```markdown
## Plugin API: anti-aliased canvas shapes

Plugin canvases can draw convex polygons (optionally with a colour per
corner), rounded rectangles and ellipses (filled and stroked), circles, and
two-colour rectangle gradients, all with anti-aliased edges.

- Contract (additive, default members that draw nothing on older hosts):
  `IPluginPainter.FillPolygon` (×2), `FillRoundedRect`, `StrokeRoundedRect`,
  `FillEllipse`, `StrokeEllipse`, `FillCircle` (defaults to `FillEllipse`),
  `FillRectGradient`; new `PluginCornerRadii`, `PluginGradientDirection`.
- Host: shapes are tessellated on the CPU into coloured triangles with a
  one-pixel fringe that fades to clear, centred on each edge; clipped with
  colour interpolation; drawn in the untextured sprite run, so painter's order
  with fills and images holds. No shader changes. Relies on PR 0's blend fix.
- Semantics: convex polygons of 3–64 points, either winding; CSS-style radius
  clamping; strokes centred, sub-pixel strokes drawn one pixel wide and fainter.
  Bad input draws nothing, never throws, and is logged once per canvas. A paint
  may draw 32,768 shape vertices; past that the rest is skipped and the paint
  counts as an overrun (three in a row drop the callback, as for time).
- Tests: tessellation areas within 1% of analytic, clipper, renderer and
  context plumbing, guard, painter integration (order, clipping, report-once,
  budget), contract defaults, and a `Lane=Vulkan` pixel check of the fringe.

API change: eight `IPluginPainter` members; `PluginCornerRadii`,
`PluginGradientDirection`.

https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB
```

Ask the user which repository the PR goes to (`origin` or upstream) and what its base is: PR 0 if unmerged, `main` if merged. Also ask whether the fork's `main` should take this branch now. That is a merge, not a fast-forward, because the fork's `main` carries PR 1. The expected conflicts are all "keep both sides":
- `PluginPainter.Bind`/`Unbind` (fonts and shape-problems parameters)
- `PluginCanvasSurface.Repaint` (the `fonts` and `shapeProblems` parameters)
- the element's `Repaint` call
- the `plugin-ui-markup.md` test paragraph (both append a sentence)
- possibly `TextRenderer.NextSpriteSeg` call sites (PR 1 adds a coverage argument; `DrawTriangles` passes none)

```json:metadata
{"files": [], "verifyCommand": "dotnet build AcDream.slnx -c Release", "acceptanceCriteria": ["0 warnings 0 errors", "portable gate green (known Mac failures compared and reported)", "Vulkan lane green on MoltenVK", "no shader changes, no lock files", "pushed; PR target asked"], "modelTier": "mechanical"}
```

---

## Later PRs

PR 2 (image regions), PR 4 (HiDPI) and later PRs each get their own plan. For PR 4, the shape inputs are: `PluginPainter.DevicePixel` becomes `1 / s`; `CanvasGeometry` already takes the device pixel size, and its arc counts and fringe width follow from it. The vertex budget counts vertices, not pixels, so it is unchanged by scale.
