# Modern Plugin Theme Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plugin windows that opt in with `<panel theme="plugin">` draw in a "soft modern" style (rounded corners, header band, soft shadow, hover/pressed/focus states, switch toggles, sharp text) when the player picks Moss or Brass, while Classic stays exactly as it is.

**Architecture:** `UiRenderContext` gains smooth rounded shapes (built by painter-v2's `CanvasGeometry`, drawn through the existing `DrawTriangles`) and a per-frame `PixelScale`. A static `PluginUiStyle` holds every modern shape and colour; each control's existing "palette is set" branch calls it, and Classic branches are not edited. Bundled Noto Sans gains a SemiBold weight and a 2× "sharp" companion used on Retina.

**Tech Stack:** C# / .NET 10, xUnit, StbTrueTypeSharp, the retained UI in `src/AcDream.App/UI`.

**Spec:** `docs/superpowers/specs/2026-10-01-modern-plugin-theme-design.md` (fork-only docs branch `docs/modern-theme-spec`).

## Global Constraints

- **Classic is byte-identical.** No Classic drawing path changes. Every new branch is guarded by a non-null `PluginUiPalette`. The guard test from Task 0 (`PluginThemeClassicIdentityTests`) must pass after every task.
- **No plugin API change:** nothing in `src/AcDream.Plugin.Abstractions`, no new markup elements or attributes.
- **Authored layout is unchanged:** x/y/w/h, anchors, `MeasureWidth` and `LineHeight` give the same numbers on every display and in every theme. The only themed position change is the window title label (Task 4).
- **Live switching works:** an open window redraws correctly when the player switches between Classic/Moss/Brass, with no rebuild.
- **Sizes and colours come from `PluginUiStyle` constants only** (Task 2). Values: window radius 10, control radius 6, small radius 4, container radius 8, row radius 5, row inset 3, header 24, switch 26×14 with an 8px knob, check 11, slider track 4 and thumb 14, scroll thumb 4 (6 when active), window shadow spread 12 / offset 4 / black 45%, popup shadow spread 8 / offset 3 / black 35%, shelf shadow spread 8 / offset 3 / black 40%.
- **Derived colours:** `Hover(fill) = Mix(fill, Border, 0.30)`, `HoverEdge(edge) = Mix(edge, Text, 0.15)`, `Pressed(fill) = Mix(fill, black, 0.25)`, focus glow = `Accent` at alpha 0.30, focus ring = `Accent` at alpha 0.60, disabled = alpha × 0.45.
- **Focus rings and glows are drawn inside the control's bounds**, because elements clip to their own bounds.
- **Branching:** code goes on `modern-theme/controls`, made from fork `main` (c14dad9b or later) in worktree `.worktrees/modern-theme`. `docs/superpowers/` never goes on code branches. Don't push or merge into `main` without asking the user (Task 11).
- **Build env:** `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. Building rewrites `*.lock.json`; revert it with `git checkout -- '*.lock.json'` before each commit. The exception is the new sample's own lock file in Task 9.
- **Commit trailer:** end every commit message with `Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB`.

**User decisions (already made):**
- "A · Soft modern" direction, with "C"'s switch-style toggles.
- Scope: windows + plugin shelf; tooltips are out.
- Approach 1: one `PluginUiStyle` helper called from the existing palette branches.
- Fork first, upstream later; keep Erik's structure.
- No `variant="primary"` button in this pass (follow-up).
- Square corners of authored content inside rounded windows are accepted.

**Known risks (check in Task 10):**
- A themed toggle's caption starts 18pt further right (x=35 instead of 17), so tightly sized toggles clip their caption.
- Slider thumbs are drawn centred on the existing hit-test thumb, so the visual 14pt circle is wider than the hit area.

Test command used throughout (the filter varies):

```bash
cd .worktrees/modern-theme && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~<Name>"
```

---

### Task 0: Branch, draw-capture helper, Classic identity guard

**Goal:** A worktree on `modern-theme/controls`, a reusable test helper that records drawn vertices, and a test proving that a themed window switched back to Classic draws exactly like a never-themed window.

**Files:**
- Create: `tests/AcDream.App.Tests/UI/ThemeDrawCapture.cs`
- Create: `tests/AcDream.App.Tests/UI/PluginThemeClassicIdentityTests.cs`

**Acceptance Criteria:**
- [ ] Worktree `.worktrees/modern-theme` exists on branch `modern-theme/controls`, based on fork `main`.
- [ ] `PluginThemeClassicIdentityTests` passes on the unmodified code.
- [ ] `ThemeDrawCapture` exposes `Context`, `Vertices`, `Draw` and `HasColor`.

**Verify:** `dotnet test ... --filter "FullyQualifiedName~PluginThemeClassicIdentityTests"` → 1 passed

**Steps:**

- [ ] **Step 1: Create the worktree**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git fetch origin
git worktree add .worktrees/modern-theme -b modern-theme/controls main
cd .worktrees/modern-theme
```

- [ ] **Step 2: Write the capture helper**

`tests/AcDream.App.Tests/UI/ThemeDrawCapture.cs`:

```csharp
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>Draws interface elements into a recording renderer and reads back what was drawn.</summary>
internal static class ThemeDrawCapture
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }

    internal static (TextRenderer Renderer, UiRenderContext Context) Context(
        float width = 600f, float height = 600f, float pixelScale = 1f)
    {
        var renderer = new TextRenderer(new RecordingGpuDevice(), new FrameSource(), "unused");
        renderer.Begin(new Vector2(width, height));
        var context = new UiRenderContext(renderer, new Vector2(width, height));
        context.Begin(new Vector2(width, height), null, pixelScale);
        return (renderer, context);
    }

    /// <summary>Every vertex drawn, in order: position and colour (x, y, u, v, r, g, b, a per vertex).</summary>
    internal static List<(Vector2 Position, Vector4 Color)> Vertices(TextRenderer renderer)
    {
        var result = new List<(Vector2, Vector4)>();
        foreach (var segment in renderer.DebugSpriteSegmentVerts)
        {
            var f = segment.Verts;
            for (int i = 0; i + TextRenderer.FloatsPerVertex <= f.Count; i += TextRenderer.FloatsPerVertex)
                result.Add((new Vector2(f[i], f[i + 1]), new Vector4(f[i + 4], f[i + 5], f[i + 6], f[i + 7])));
        }
        return result;
    }

    /// <summary>Draws a root once and returns every float the renderer recorded, with texture ids.</summary>
    internal static float[] Draw(UiRoot root, float pixelScale = 1f)
    {
        (TextRenderer renderer, UiRenderContext context) = Context(root.Width, root.Height, pixelScale);
        root.Draw(context);
        return renderer.DebugSpriteSegmentVerts
            .SelectMany(s => s.Verts.Prepend((float)s.Texture))
            .ToArray();
    }

    /// <summary>Whether any opaque-ish vertex carries this colour's RGB (alpha is ignored).</summary>
    internal static bool HasColor(IEnumerable<(Vector2 Position, Vector4 Color)> vertices, Vector4 color,
        float tolerance = 0.004f) =>
        vertices.Any(v => v.Color.W > 0.05f
            && MathF.Abs(v.Color.X - color.X) <= tolerance
            && MathF.Abs(v.Color.Y - color.Y) <= tolerance
            && MathF.Abs(v.Color.Z - color.Z) <= tolerance);
}
```

`UiRenderContext.Begin` doesn't take a pixel scale until Task 1. For this task, call `context.Begin(new Vector2(width, height), null)` and leave `pixelScale` unused. Task 1 then passes it through.

- [ ] **Step 3: Write the guard test**

`tests/AcDream.App.Tests/UI/PluginThemeClassicIdentityTests.cs`:

```csharp
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// The modern theme must never leak into Classic: a themed window switched
/// back to Classic draws exactly what a window that never opted in draws.
/// </summary>
public sealed class PluginThemeClassicIdentityTests
{
    private sealed class Binding
    {
        public Action Click => () => { };
        public bool TabSelected => true;
        public bool Checked => true;
        public Action Toggle => () => { };
        public float Value => 0.6f;
        public Action<float> Changed => _ => { };
        public string Text => "Asheron";
        public IReadOnlyList<string> Items => ["Strength Self VII", "Invulnerability Self VII", "Impregnability Self VII"];
        public string Selected => "Strength Self VII";
        public int Index => 1;
        public IReadOnlyList<bool> Flags => [true, false, true];
        public Action<int> Check => _ => { };
        public IReadOnlyList<string> Log => ["one", "two", "three"];
        public float Fill => 0.4f;
    }

    internal const string Body = """
          <group x="8" y="28" w="300" h="20" background="#FF202020" border="#FF404040" />
          <label x="12" y="52" text="Target" />
          <button x="12" y="70" w="80" h="24" text="Start" onclick="{Click}" />
          <tab x="100" y="70" w="80" h="24" text="Status" selected="{TabSelected}" onclick="{Click}" />
          <toggle x="12" y="100" w="160" h="20" text="Auto-rebuff" checked="{Checked}" onclick="{Toggle}" />
          <slider x="12" y="126" w="200" h="16" value="{Value}" onchange="{Changed}" />
          <field x="12" y="148" w="200" h="24" text="{Text}" />
          <menu x="12" y="178" w="200" h="24" items="{Items}" selected="{Selected}" />
          <list x="12" y="208" w="200" h="60" items="{Items}" selected="{Index}" selectionband="true" />
          <list x="220" y="208" w="160" h="60" selected="{Index}">
            <column type="check" width="20" values="{Flags}" onchange="{Check}" />
            <column type="text" width="*" items="{Items}" />
          </list>
          <log x="12" y="274" w="200" h="60" items="{Log}" />
          <meter x="12" y="340" w="200" h="12" fill="{Fill}" color="#FFCC3333" />
        """;

    private static UiRoot Root(string themeAttribute, PluginUiThemeSettings settings, UiDatFont font)
    {
        var panel = MarkupDocument.Build(
            $"<panel x=\"10\" y=\"10\" w=\"400\" h=\"360\" title=\"Gallery\" {themeAttribute}>{Body}</panel>",
            new Binding(), _ => (0u, 0, 0), datFont: font, themes: settings);
        var root = new UiRoot { Width = 600, Height = 600 };
        root.AddChild(panel);
        return root;
    }

    [Fact]
    public void ThemedWindowSwitchedBackToClassicDrawsExactlyLikeAClassicWindow()
    {
        var classicFont = BundledUiFont.Bake(12).CreateFont(7);
        var modernFont = BundledUiFont.Bake().CreateFont(8);

        var plainSettings = new PluginUiThemeSettings(modernFont: modernFont);
        UiRoot plain = Root("", plainSettings, classicFont);
        plain.Tick(0.016, 1);
        plain.Tick(0.016, 2);

        var settings = new PluginUiThemeSettings(modernFont: modernFont);
        UiRoot themed = Root("theme=\"plugin\"", settings, classicFont);
        settings.Theme = PluginUiTheme.Moss;
        themed.Tick(0.016, 1);
        ThemeDrawCapture.Draw(themed);
        settings.Theme = PluginUiTheme.Classic;
        themed.Tick(0.016, 2);

        Assert.Equal(ThemeDrawCapture.Draw(plain), ThemeDrawCapture.Draw(themed));
    }
}
```

- [ ] **Step 4: Run it on unmodified code**

Run: `dotnet test ... --filter "FullyQualifiedName~PluginThemeClassicIdentityTests"`
Expected: PASS. If it fails here, Classic already drifts on today's code. That's not caused by this work, so stop and report the first differing index to the coordinator; don't change product code to make it pass.

- [ ] **Step 5: Commit**

```bash
git add tests/AcDream.App.Tests/UI/ThemeDrawCapture.cs tests/AcDream.App.Tests/UI/PluginThemeClassicIdentityTests.cs
git commit -m "test: a themed plugin window back on Classic draws like a Classic one"
```

```json:metadata
{"files": ["tests/AcDream.App.Tests/UI/ThemeDrawCapture.cs", "tests/AcDream.App.Tests/UI/PluginThemeClassicIdentityTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter FullyQualifiedName~PluginThemeClassicIdentityTests", "acceptanceCriteria": ["worktree .worktrees/modern-theme on modern-theme/controls from fork main", "PluginThemeClassicIdentityTests passes on unmodified code", "ThemeDrawCapture exposes Context, Vertices, Draw, HasColor"], "modelTier": "standard"}
```

---

### Task 1: Smooth shapes and pixel scale on the render context

**Goal:** `UiRenderContext` can fill and stroke smooth rounded rectangles and ellipses, draw smooth lines, vertical gradients and soft shadows. It knows the frame's pixel scale, which `RetailUiRuntime` supplies every frame.

**Files:**
- Modify: `src/AcDream.App/UI/UiRenderContext.cs` (`Begin`, new shape methods after `DrawTriangles`)
- Modify: `src/AcDream.App/UI/UiHost.cs` (`PixelScale`, `Draw`)
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (the `Host.Draw(screenSize);` call at the end of `Draw`, about line 787)
- Modify: `tests/AcDream.App.Tests/UI/ThemeDrawCapture.cs` (pass `pixelScale` to `Begin`)
- Test: `tests/AcDream.App.Tests/UI/UiRenderContextShapesTests.cs`

**Acceptance Criteria:**
- [ ] `Begin(screenSize, font, pixelScale)` stores `PixelScale`. Values below 1 or not finite become 1.
- [ ] `FillRoundedRect`/`StrokeRoundedRect`/`FillEllipse`/`StrokeEllipse`/`DrawSmoothLine` emit triangles that follow the transform and clip. The anti-aliased fringe reaches 0.5/PixelScale beyond the shape.
- [ ] `FillVerticalGradient` colours vertices by height between the two colours.
- [ ] `DrawSoftShadow` draws outside the current element clip and restores the clip afterwards.
- [ ] `UiHost.Draw` passes `UiHost.PixelScale`, and `RetailUiRuntime` sets it from the plugin canvas services' framebuffer-per-point (1 when those are absent).
- [ ] The Classic identity guard still passes.

**Verify:** `dotnet test ... --filter "FullyQualifiedName~UiRenderContextShapesTests|FullyQualifiedName~UiRenderContextTrianglesTests|FullyQualifiedName~PluginThemeClassicIdentityTests"` → all pass

**Steps:**

- [ ] **Step 1: Write failing tests**

`tests/AcDream.App.Tests/UI/UiRenderContextShapesTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiRenderContextShapesTests
{
    private static readonly Vector4 Red = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 Blue = new(0f, 0f, 1f, 1f);

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void RoundedFillFollowsTheOriginAndItsFringeIsOneDevicePixel(float scale)
    {
        var (renderer, ctx) = ThemeDrawCapture.Context(pixelScale: scale);
        ctx.PushTransform(10f, 20f);
        ctx.FillRoundedRect(0f, 0f, 40f, 20f, 6f, Red);
        var v = ThemeDrawCapture.Vertices(renderer);
        Assert.NotEmpty(v);
        float half = 0.5f / scale;
        Assert.Equal(10f - half, v.Min(p => p.Position.X), 3);
        Assert.Equal(50f + half, v.Max(p => p.Position.X), 3);
        Assert.Equal(20f - half, v.Min(p => p.Position.Y), 3);
        Assert.Equal(scale, ctx.PixelScale);
    }

    [Fact]
    public void ABadPixelScaleIsOne()
    {
        var (_, ctx) = ThemeDrawCapture.Context(pixelScale: float.NaN);
        Assert.Equal(1f, ctx.PixelScale);
        ctx.Begin(new Vector2(10, 10), null, 0.5f);
        Assert.Equal(1f, ctx.PixelScale);
    }

    [Fact]
    public void ShapesAreCutToTheClip()
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        ctx.PushClip(0f, 0f, 20f, 100f);
        ctx.FillEllipse(0f, 0f, 40f, 40f, Red);
        ctx.StrokeRoundedRect(0f, 50f, 40f, 20f, 6f, Red, 1f);
        ctx.DrawSmoothLine(0f, 90f, 40f, 90f, Red, 1.5f);
        Assert.All(ThemeDrawCapture.Vertices(renderer), v => Assert.InRange(v.Position.X, 0f, 20f + 1e-4f));
    }

    [Fact]
    public void AGradientRunsFromTopToBottom()
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        ctx.FillVerticalGradient(0f, 0f, 40f, 20f, new CanvasCornerRadii(6f, 6f, 0f, 0f), Red, Blue);
        var opaque = ThemeDrawCapture.Vertices(renderer).Where(v => v.Color.W > 0.99f).ToList();
        var top = opaque.MinBy(v => v.Position.Y);
        var bottom = opaque.MaxBy(v => v.Position.Y);
        Assert.True(top.Color.X > top.Color.Z);
        Assert.True(bottom.Color.Z > bottom.Color.X);
    }

    [Fact]
    public void AShadowEscapesTheElementClipAndLeavesItInPlace()
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        ctx.PushClip(100f, 100f, 50f, 50f);
        ctx.DrawSoftShadow(100f, 100f, 50f, 50f, 10f, 12f, new Vector4(0f, 0f, 0f, 0.45f));
        Assert.True(ThemeDrawCapture.Vertices(renderer).Min(v => v.Position.X) < 100f);
        Assert.Equal(1, ctx.ClipStackDepth);
        ctx.FillRoundedRect(0f, 0f, 20f, 20f, 4f, Red);
        Assert.DoesNotContain(ThemeDrawCapture.Vertices(renderer), v => v.Color == Red && v.Position.X < 100f);
    }
}
```

`CanvasCornerRadii` lives in `AcDream.App.UI` (file `CanvasGeometry.cs`) and is internal; the test project already sees internals.

- [ ] **Step 2: Run and see them fail**

Run: `dotnet test ... --filter "FullyQualifiedName~UiRenderContextShapesTests"`
Expected: compile errors (`FillRoundedRect`, `PixelScale` etc. not defined).

- [ ] **Step 3: Add `PixelScale` to `Begin`**

In `src/AcDream.App/UI/UiRenderContext.cs`, replace the `Begin` method with:

```csharp
    /// <summary>
    /// Device pixels per interface point this frame: 1 on a standard display,
    /// 2 on a typical high-density one. Shapes use it for the width of their
    /// anti-aliased edge, and fonts that carry a sharp companion use it to
    /// pick their sharper glyphs. Layout never reads it.
    /// </summary>
    public float PixelScale { get; private set; } = 1f;

    public void Begin(Vector2 screenSize, BitmapFont? defaultFont, float pixelScale = 1f)
    {
        ScreenSize = screenSize;
        DefaultFont = defaultFont;
        PixelScale = float.IsFinite(pixelScale) && pixelScale >= 1f ? pixelScale : 1f;
        _stack.Clear();
        _current = default;
        _clipStack.Clear();
        _clip = null;
        _alphaStack.Clear();
        _alpha = 1f;
    }
```

- [ ] **Step 4: Add the shape methods**

In `UiRenderContext.cs`, add a field next to `_triangles`:

```csharp
    private readonly System.Collections.Generic.List<UiColorVertex> _shape = new(256);
```

Add these methods directly after `DrawTriangles` (before `Place`):

```csharp
    /// <summary>One device pixel, in interface points: the width of a shape's soft edge.</summary>
    private float DevicePixel => 1f / PixelScale;

    private void DrawShape()
    {
        if (_shape.Count > 0)
            DrawTriangles(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_shape));
    }

    /// <summary>A rectangle with rounded corners and a smooth edge, in local coordinates.</summary>
    internal void FillRoundedRect(float x, float y, float w, float h, CanvasCornerRadii radii, Vector4 color)
    {
        _shape.Clear();
        CanvasGeometry.FillRoundedRect(x, y, w, h, radii, color, DevicePixel, _shape);
        DrawShape();
    }

    internal void FillRoundedRect(float x, float y, float w, float h, float radius, Vector4 color) =>
        FillRoundedRect(x, y, w, h, new CanvasCornerRadii(radius, radius, radius, radius), color);

    /// <summary>A rounded rectangle's outline, centred on the outline.</summary>
    internal void StrokeRoundedRect(float x, float y, float w, float h, float radius, Vector4 color, float thickness)
    {
        _shape.Clear();
        CanvasGeometry.StrokeRoundedRect(
            x, y, w, h, new CanvasCornerRadii(radius, radius, radius, radius), color, thickness, DevicePixel, _shape);
        DrawShape();
    }

    internal void FillEllipse(float x, float y, float w, float h, Vector4 color)
    {
        _shape.Clear();
        CanvasGeometry.FillEllipse(x, y, w, h, color, DevicePixel, _shape);
        DrawShape();
    }

    internal void StrokeEllipse(float x, float y, float w, float h, Vector4 color, float thickness)
    {
        _shape.Clear();
        CanvasGeometry.StrokeEllipse(x, y, w, h, color, thickness, DevicePixel, _shape);
        DrawShape();
    }

    /// <summary>A straight segment with smooth sides and square, flush ends.</summary>
    internal void DrawSmoothLine(float x0, float y0, float x1, float y1, Vector4 color, float thickness)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (!(length > 0f) || !(thickness > 0f)) return;
        float nx = -dy / length * thickness * 0.5f;
        float ny = dx / length * thickness * 0.5f;
        Span<Vector2> corners =
        [
            new(x0 + nx, y0 + ny), new(x1 + nx, y1 + ny), new(x1 - nx, y1 - ny), new(x0 - nx, y0 - ny),
        ];
        Span<Vector4> colors = [color, color, color, color];
        _shape.Clear();
        CanvasGeometry.FillConvexPolygon(corners, colors, DevicePixel, _shape);
        DrawShape();
    }

    /// <summary>
    /// A rounded rectangle whose colour runs from <paramref name="top"/> to
    /// <paramref name="bottom"/>. Each corner of the shape takes the colour
    /// at its height; the soft edge keeps its own transparency.
    /// </summary>
    internal void FillVerticalGradient(
        float x, float y, float w, float h, CanvasCornerRadii radii, Vector4 top, Vector4 bottom)
    {
        if (!(h > 0f)) return;
        _shape.Clear();
        CanvasGeometry.FillRoundedRect(x, y, w, h, radii, Vector4.One, DevicePixel, _shape);
        for (int i = 0; i < _shape.Count; i++)
        {
            UiColorVertex v = _shape[i];
            float t = Math.Clamp((v.Position.Y - y) / h, 0f, 1f);
            Vector4 c = Vector4.Lerp(top, bottom, t);
            _shape[i] = new UiColorVertex(v.Position, c with { W = c.W * v.Color.W });
        }
        DrawShape();
    }

    /// <summary>
    /// A soft shadow under a rounded rectangle: six stacked rounded fills,
    /// each grown by a sixth of <paramref name="spread"/>, so the darkness
    /// fades outwards. Drawn with the clip lifted to the whole screen,
    /// because a shadow lies outside the element that casts it.
    /// </summary>
    internal void DrawSoftShadow(float x, float y, float w, float h, float radius, float spread, Vector4 color)
    {
        if (!(spread > 0f) || !(color.W > 0f) || !(w > 0f) || !(h > 0f)) return;
        const int Layers = 6;
        Vector4 layer = color with { W = color.W / Layers };
        PushClipUnbounded();
        try
        {
            for (int i = Layers; i >= 1; i--)
            {
                float grow = spread * i / Layers;
                FillRoundedRect(x - grow, y - grow, w + 2f * grow, h + 2f * grow, radius + grow, layer);
            }
        }
        finally
        {
            PopClip();
        }
    }
```

Then in `tests/AcDream.App.Tests/UI/ThemeDrawCapture.cs`, change `context.Begin(new Vector2(width, height), null);` to `context.Begin(new Vector2(width, height), null, pixelScale);`.

- [ ] **Step 5: Plumb the pixel scale**

In `src/AcDream.App/UI/UiHost.cs`, add above `Draw`:

```csharp
    /// <summary>Device pixels per interface point, handed to every frame's render context.</summary>
    public float PixelScale { get; set; } = 1f;
```

and change `ctx.Begin(screenSize, DefaultFont);` to `ctx.Begin(screenSize, DefaultFont, PixelScale);`.

In `src/AcDream.App/UI/RetailUiRuntime.cs`, in the method that ends with `Host.Draw(screenSize);` (about line 787), replace that line with:

```csharp
        // Themed plugin windows draw their soft edges and sharp text at the
        // display's density; the canvas services already know it.
        Host.PixelScale = _bindings.PluginCanvases is { } canvases
            ? Layout.CanvasPixelScale.ForInterface(canvases.FramebufferPerPoint())
            : 1f;
        Host.Draw(screenSize);
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test ... --filter "FullyQualifiedName~UiRenderContextShapesTests|FullyQualifiedName~UiRenderContextTrianglesTests|FullyQualifiedName~PluginThemeClassicIdentityTests"`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/UiRenderContext.cs src/AcDream.App/UI/UiHost.cs src/AcDream.App/UI/RetailUiRuntime.cs tests/AcDream.App.Tests/UI/ThemeDrawCapture.cs tests/AcDream.App.Tests/UI/UiRenderContextShapesTests.cs
git commit -m "ui: smooth rounded shapes, gradients and soft shadows on the render context"
```

```json:metadata
{"files": ["src/AcDream.App/UI/UiRenderContext.cs", "src/AcDream.App/UI/UiHost.cs", "src/AcDream.App/UI/RetailUiRuntime.cs", "tests/AcDream.App.Tests/UI/UiRenderContextShapesTests.cs", "tests/AcDream.App.Tests/UI/ThemeDrawCapture.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~UiRenderContextShapesTests|FullyQualifiedName~UiRenderContextTrianglesTests|FullyQualifiedName~PluginThemeClassicIdentityTests\"", "acceptanceCriteria": ["Begin stores PixelScale, bad values become 1", "shape methods follow transform and clip with fringe 0.5/PixelScale", "FillVerticalGradient colours by height", "DrawSoftShadow escapes the element clip and restores it", "UiHost.Draw passes PixelScale; RetailUiRuntime sets it from canvas services", "Classic identity guard passes"], "modelTier": "standard"}
```

---

### Task 2: `PluginUiStyle` and `UiPointerState`

**Goal:** One static style class holds every modern shape, size and colour. A small struct records hover and press for themed controls.

**Files:**
- Create: `src/AcDream.App/UI/PluginUiStyle.cs`
- Create: `src/AcDream.App/UI/UiPointerState.cs`
- Modify: `src/AcDream.App/UI/PluginUiThemeSettings.cs` (`PluginUiPalette.DrawCheck` delegates to the style)
- Test: `tests/AcDream.App.Tests/UI/PluginUiStyleTests.cs`

**Acceptance Criteria:**
- [ ] `PluginUiStyle` constants match the Global Constraints values.
- [ ] `Hover`, `HoverEdge`, `Pressed`, `FocusGlow`, `FocusRingColor` and `Faded` follow the Global Constraints formulas.
- [ ] `Button` draws the hover colour when hovered, the pressed colour when pressed, and fades when disabled.
- [ ] `Switch` puts the knob on the right in `Text` when on, and on the left in `Muted` when off.
- [ ] `UiPointerState` covers enter, leave, press, drag-out and release.
- [ ] `PluginUiPalette.DrawCheck` keeps its signature and draws through `PluginUiStyle.Check`.

**Verify:** `dotnet test ... --filter "FullyQualifiedName~PluginUiStyleTests|FullyQualifiedName~PluginThemeClassicIdentityTests|FullyQualifiedName~MarkupListColumnsTests"` → all pass

**Steps:**

- [ ] **Step 1: Write failing tests**

`tests/AcDream.App.Tests/UI/PluginUiStyleTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class PluginUiStyleTests
{
    private static readonly PluginUiPalette P = PluginUiPalette.Moss;

    [Fact]
    public void DerivedColoursFollowThePalette()
    {
        Assert.Equal(PluginUiStyle.Mix(P.Field, P.Border, 0.30f), PluginUiStyle.Hover(P, P.Field));
        Assert.Equal(PluginUiStyle.Mix(P.Field, new Vector4(0, 0, 0, 1), 0.25f), PluginUiStyle.Pressed(P.Field));
        Assert.Equal(0.30f, PluginUiStyle.FocusGlow(P).W);
        Assert.Equal(0.60f, PluginUiStyle.FocusRingColor(P).W);
        Assert.Equal(P.Field.W * 0.45f, PluginUiStyle.Faded(P.Field).W, 5);
        Assert.Equal(P.Field.W, PluginUiStyle.Hover(P, P.Field).W);
    }

    [Theory]
    [InlineData(UiControlState.Normal)]
    [InlineData(UiControlState.Hovered)]
    [InlineData(UiControlState.Pressed)]
    public void AButtonDrawsItsStateColour(UiControlState state)
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        PluginUiStyle.Button(ctx, P, 80f, 24f, P.Field, P.Border, state);
        Vector4 expected = state switch
        {
            UiControlState.Hovered => PluginUiStyle.Hover(P, P.Field),
            UiControlState.Pressed => PluginUiStyle.Pressed(P.Field),
            _ => P.Field,
        };
        Assert.True(ThemeDrawCapture.HasColor(ThemeDrawCapture.Vertices(renderer), expected));
    }

    [Fact]
    public void ADisabledButtonIsFaded()
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        PluginUiStyle.Button(ctx, P, 80f, 24f, P.Field, P.Border, UiControlState.Disabled);
        Assert.All(ThemeDrawCapture.Vertices(renderer), v => Assert.True(v.Color.W <= 0.45f + 1e-4f));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheSwitchKnobSitsOnTheSideOfItsValue(bool on)
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        PluginUiStyle.Switch(ctx, P, 0f, 0f, on, enabled: true);
        Vector4 knob = on ? P.Text : P.Muted;
        var knobVerts = ThemeDrawCapture.Vertices(renderer)
            .Where(v => v.Color.W > 0.99f && Vector4.Distance(v.Color, knob) < 0.01f).ToList();
        Assert.NotEmpty(knobVerts);
        float centre = knobVerts.Average(v => v.Position.X);
        if (on) Assert.True(centre > PluginUiStyle.SwitchWidth / 2f);
        else Assert.True(centre < PluginUiStyle.SwitchWidth / 2f);
    }

    [Fact]
    public void PointerStateFollowsHoverAndPress()
    {
        var s = new UiPointerState();
        Assert.Equal(UiControlState.Normal, s.State(enabled: true));
        s.Observe(new UiEvent { Type = UiEventType.HoverEnter });
        Assert.Equal(UiControlState.Hovered, s.State(true));
        s.Observe(new UiEvent { Type = UiEventType.MouseDown });
        Assert.Equal(UiControlState.Pressed, s.State(true));
        s.Observe(new UiEvent { Type = UiEventType.HoverLeave });
        Assert.Equal(UiControlState.Normal, s.State(true));
        s.Observe(new UiEvent { Type = UiEventType.HoverEnter });
        Assert.Equal(UiControlState.Pressed, s.State(true));
        s.Observe(new UiEvent { Type = UiEventType.MouseUp });
        Assert.Equal(UiControlState.Hovered, s.State(true));
        Assert.Equal(UiControlState.Disabled, s.State(false));
    }

    [Fact]
    public void ThePaletteCheckDrawsTheModernCheck()
    {
        var (a, ctxA) = ThemeDrawCapture.Context();
        var (b, ctxB) = ThemeDrawCapture.Context();
        P.DrawCheck(ctxA, 3f, 4f, true);
        PluginUiStyle.Check(ctxB, P, 3f, 4f, true);
        Assert.Equal(ThemeDrawCapture.Vertices(b), ThemeDrawCapture.Vertices(a));
    }
}
```

`UiEvent` is a positional record struct with defaults. If `new UiEvent { Type = ... }` doesn't compile, use `new UiEvent(0, null!, UiEventType.HoverEnter)`, following `UiRoot`'s calls.

- [ ] **Step 2: Run, expect compile failure**

Run: `dotnet test ... --filter "FullyQualifiedName~PluginUiStyleTests"` → fails, `PluginUiStyle` not defined.

- [ ] **Step 3: Write `UiPointerState`**

`src/AcDream.App/UI/UiPointerState.cs`:

```csharp
namespace AcDream.App.UI;

/// <summary>The look a themed control takes from the pointer and its own enabled flag.</summary>
internal enum UiControlState { Normal, Hovered, Pressed, Disabled }

/// <summary>
/// Whether the pointer is over a control and whether the left button went
/// down on it. It only watches events: the control's own handling decides
/// what they do, and what it returns. A press shows as pressed only while
/// the pointer is still over the control.
/// </summary>
internal struct UiPointerState
{
    public bool Hovered { get; private set; }

    public bool Pressed { get; private set; }

    public void Observe(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.HoverEnter: Hovered = true; break;
            case UiEventType.HoverLeave: Hovered = false; break;
            case UiEventType.MouseDown: Pressed = true; Hovered = true; break;
            case UiEventType.MouseUp:
            case UiEventType.CaptureChanged: Pressed = false; break;
        }
    }

    public readonly UiControlState State(bool enabled) =>
        !enabled ? UiControlState.Disabled
        : Pressed && Hovered ? UiControlState.Pressed
        : Hovered ? UiControlState.Hovered
        : UiControlState.Normal;
}
```

- [ ] **Step 4: Write `PluginUiStyle`**

`src/AcDream.App/UI/PluginUiStyle.cs`:

```csharp
using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// How the shared plugin themes draw: every rounded shape, state colour and
/// size of the modern look lives here, so a themed control only says what
/// it is and which state it is in. Colours come from the palette, with
/// hover, pressed and focus colours worked out from it. Classic never comes
/// here.
/// <para>Everything is drawn inside the bounds given: an element clips to
/// its own rectangle, so a ring or glow outside it would be cut off.</para>
/// </summary>
internal static class PluginUiStyle
{
    internal const float WindowRadius = 10f;
    internal const float ControlRadius = 6f;
    internal const float SmallRadius = 4f;
    internal const float ContainerRadius = 8f;
    internal const float RowRadius = 5f;
    internal const float RowInset = 3f;
    internal const float HeaderHeight = 24f;
    internal const float SwitchWidth = 26f;
    internal const float SwitchHeight = 14f;
    internal const float SwitchKnob = 8f;
    internal const float SwitchCaptionGap = 8f;
    internal const float CheckSize = 11f;
    internal const float SliderTrackHeight = 4f;
    internal const float SliderThumbSize = 14f;
    internal const float ScrollThumbWidth = 4f;
    internal const float ScrollThumbActiveWidth = 6f;
    internal const float DisabledAlpha = 0.45f;

    private static readonly Vector4 Black = new(0f, 0f, 0f, 1f);

    /// <summary>Moves <paramref name="a"/>'s colour toward <paramref name="b"/>'s; keeps <paramref name="a"/>'s alpha.</summary>
    internal static Vector4 Mix(Vector4 a, Vector4 b, float t) => new(
        a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.W);

    internal static Vector4 Hover(PluginUiPalette p, Vector4 fill) => Mix(fill, p.Border, 0.30f);
    internal static Vector4 HoverEdge(PluginUiPalette p, Vector4 edge) => Mix(edge, p.Text, 0.15f);
    internal static Vector4 Pressed(Vector4 fill) => Mix(fill, Black, 0.25f);
    internal static Vector4 FocusGlow(PluginUiPalette p) => p.Accent with { W = 0.30f };
    internal static Vector4 FocusRingColor(PluginUiPalette p) => p.Accent with { W = 0.60f };
    internal static Vector4 Faded(Vector4 c) => c with { W = c.W * DisabledAlpha };

    /// <summary>A rounded fill with a one-pixel edge lying just inside it.</summary>
    internal static void Surface(
        UiRenderContext ctx, float x, float y, float w, float h, float radius, Vector4 fill, Vector4 edge)
    {
        if (!(w > 0f && h > 0f)) return;
        if (fill.W > 0f) ctx.FillRoundedRect(x, y, w, h, radius, fill);
        if (edge.W > 0f)
            ctx.StrokeRoundedRect(x + 0.5f, y + 0.5f, w - 1f, h - 1f, MathF.Max(0f, radius - 0.5f), edge, 1f);
    }

    /// <summary>A two-pixel ring one pixel inside a control's edge.</summary>
    internal static void FocusRing(UiRenderContext ctx, PluginUiPalette p, float w, float h, float radius) =>
        ctx.StrokeRoundedRect(2f, 2f, w - 4f, h - 4f, MathF.Max(0f, radius - 2f), FocusRingColor(p), 2f);

    internal static void Button(
        UiRenderContext ctx, PluginUiPalette p, float w, float h, Vector4 fill, Vector4 edge, UiControlState state)
    {
        (fill, edge) = state switch
        {
            UiControlState.Hovered => (Hover(p, fill), HoverEdge(p, edge)),
            UiControlState.Pressed => (Pressed(fill), edge),
            UiControlState.Disabled => (Faded(fill), Faded(edge)),
            _ => (fill, edge),
        };
        Surface(ctx, 0f, 0f, w, h, ControlRadius, fill, edge);
    }

    /// <summary>A button with no face until the pointer is over it.</summary>
    internal static void GhostButton(UiRenderContext ctx, PluginUiPalette p, float w, float h, UiControlState state)
    {
        if (state == UiControlState.Hovered)
            ctx.FillRoundedRect(0f, 0f, w, h, SmallRadius, Hover(p, p.Field));
        else if (state == UiControlState.Pressed)
            ctx.FillRoundedRect(0f, 0f, w, h, SmallRadius, Pressed(Hover(p, p.Field)));
    }

    internal static void Tab(UiRenderContext ctx, PluginUiPalette p, float w, float h, bool selected, UiControlState state)
    {
        if (selected)
            ctx.FillRoundedRect(0f, 0f, w, h, ControlRadius, p.Selected);
        else if (state is UiControlState.Hovered or UiControlState.Pressed)
            ctx.FillRoundedRect(0f, 0f, w, h, ControlRadius, Hover(p, p.Field));
    }

    internal static void Field(UiRenderContext ctx, PluginUiPalette p, float w, float h, Vector4 fill, bool focused)
    {
        Surface(ctx, 0f, 0f, w, h, ControlRadius, fill, focused ? p.Accent : p.Border);
        if (focused)
            ctx.StrokeRoundedRect(2f, 2f, w - 4f, h - 4f, ControlRadius - 2f, FocusGlow(p), 2f);
    }

    /// <summary>A pill switch at (x, y), <see cref="SwitchWidth"/> by <see cref="SwitchHeight"/>.</summary>
    internal static void Switch(UiRenderContext ctx, PluginUiPalette p, float x, float y, bool on, bool enabled)
    {
        Vector4 track = on ? p.Accent with { W = 0.35f } : p.Field;
        Vector4 edge = on ? p.Accent : p.Border;
        Vector4 knob = on ? p.Text : p.Muted;
        if (!enabled) { track = Faded(track); edge = Faded(edge); knob = Faded(knob); }
        Surface(ctx, x, y, SwitchWidth, SwitchHeight, SwitchHeight / 2f, track, edge);
        float knobX = on ? x + SwitchWidth - 3f - SwitchKnob : x + 3f;
        ctx.FillEllipse(knobX, y + (SwitchHeight - SwitchKnob) / 2f, SwitchKnob, SwitchKnob, knob);
    }

    /// <summary>A rounded box with a tick, for check columns in lists.</summary>
    internal static void Check(UiRenderContext ctx, PluginUiPalette p, float x, float y, bool on)
    {
        Surface(ctx, x, y, CheckSize, CheckSize, SmallRadius, on ? p.Accent : p.Field, on ? p.Accent : p.Border);
        if (!on) return;
        ctx.DrawSmoothLine(x + 2.5f, y + 5.5f, x + 4.5f, y + 7.5f, p.Field, 1.5f);
        ctx.DrawSmoothLine(x + 4.5f, y + 7.5f, x + 8.5f, y + 3.5f, p.Field, 1.5f);
    }

    /// <summary>A slider's track, filled part and round thumb centred on <paramref name="thumbCentre"/>.</summary>
    internal static void Slider(
        UiRenderContext ctx, PluginUiPalette p, float w, float h, float thumbCentre, bool active)
    {
        float trackY = (h - SliderTrackHeight) / 2f;
        float r = SliderTrackHeight / 2f;
        ctx.FillRoundedRect(0f, trackY, w, SliderTrackHeight, r, p.Field);
        float filled = Math.Clamp(thumbCentre, 0f, w);
        if (filled > 0f) ctx.FillRoundedRect(0f, trackY, filled, SliderTrackHeight, r, p.Accent);
        float size = MathF.Min(SliderThumbSize, h);
        float tx = Math.Clamp(thumbCentre - size / 2f, 0f, MathF.Max(0f, w - size));
        float ty = (h - size) / 2f;
        ctx.FillEllipse(tx, ty + 1f, size, size, Black with { W = 0.35f });
        ctx.FillEllipse(tx, ty, size, size, p.Text);
        if (active) ctx.StrokeEllipse(tx + 1f, ty + 1f, size - 2f, size - 2f, p.Accent, 2f);
    }

    /// <summary>A slim rounded scroll thumb centred in a lane <paramref name="laneWidth"/> wide.</summary>
    internal static void ScrollThumb(
        UiRenderContext ctx, PluginUiPalette p, float x, float y, float laneWidth, float h, bool active)
    {
        float w = active ? ScrollThumbActiveWidth : ScrollThumbWidth;
        ctx.FillRoundedRect(x + (laneWidth - w) / 2f, y, w, h, w / 2f, active ? p.Text with { W = 0.7f } : p.Muted);
    }

    /// <summary>A row highlight inset from the container's sides.</summary>
    internal static void Row(UiRenderContext ctx, float x, float y, float w, float h, Vector4 color) =>
        ctx.FillRoundedRect(x + RowInset, y + 1f, w - 2f * RowInset, h - 2f, RowRadius, color);

    /// <summary>A downward chevron, <paramref name="w"/> wide and <paramref name="h"/> tall.</summary>
    internal static void Chevron(UiRenderContext ctx, float x, float y, float w, float h, Vector4 color)
    {
        ctx.DrawSmoothLine(x, y, x + w / 2f, y + h, color, 1.5f);
        ctx.DrawSmoothLine(x + w / 2f, y + h, x + w, y, color, 1.5f);
    }

    internal static void Meter(
        UiRenderContext ctx, PluginUiPalette p, float w, float h, (float X, float Y, float W, float H) fill, Vector4 bar)
    {
        float r = MathF.Min(3f, h / 2f);
        ctx.FillRoundedRect(0f, 0f, w, h, r, p.Field);
        if (fill.W > 0f && fill.H > 0f) ctx.FillRoundedRect(fill.X, fill.Y, fill.W, fill.H, r, bar);
    }

    internal static void WindowShadow(UiRenderContext ctx, float w, float h) =>
        ctx.DrawSoftShadow(0f, 4f, w, h, WindowRadius, 12f, Black with { W = 0.45f });

    internal static void PopupShadow(UiRenderContext ctx, float x, float y, float w, float h) =>
        ctx.DrawSoftShadow(x, y + 3f, w, h, ContainerRadius, 8f, Black with { W = 0.35f });

    internal static void ShelfShadow(UiRenderContext ctx, float w, float h) =>
        ctx.DrawSoftShadow(0f, 3f, w, h, WindowRadius, 8f, Black with { W = 0.40f });

    /// <summary>The band behind a window's title, with a separator line under it.</summary>
    internal static void Header(UiRenderContext ctx, PluginUiPalette p, float w)
    {
        ctx.FillVerticalGradient(0f, 0f, w, HeaderHeight,
            new CanvasCornerRadii(WindowRadius, WindowRadius, 0f, 0f),
            Mix(p.Background, p.Text, 0.06f), Mix(p.Background, p.Text, 0.02f));
        ctx.DrawFill(0f, HeaderHeight, w, 1f, p.Border);
    }

    /// <summary>The resize grip: three dots in the bottom-right corner.</summary>
    internal static void ResizeGrip(UiRenderContext ctx, PluginUiPalette p, float w, float h)
    {
        for (int i = 0; i < 3; i++)
            ctx.FillEllipse(w - 8f - i * 4f, h - 6f, 2f, 2f, p.Muted);
    }
}
```

- [ ] **Step 5: Point the palette's check at the style**

In `src/AcDream.App/UI/PluginUiThemeSettings.cs`, replace the body of `PluginUiPalette.DrawCheck` with:

```csharp
    internal void DrawCheck(UiRenderContext ctx, float x, float y, bool value) =>
        PluginUiStyle.Check(ctx, this, x, y, value);
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test ... --filter "FullyQualifiedName~PluginUiStyleTests|FullyQualifiedName~PluginThemeClassicIdentityTests|FullyQualifiedName~MarkupListColumnsTests"`
Expected: all PASS. If a `MarkupListColumnsTests` case asserts the old themed square check exactly, update that themed assertion to `PluginUiStyle.Check`'s output and note it in the commit body. Don't touch Classic assertions.

- [ ] **Step 7: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/PluginUiStyle.cs src/AcDream.App/UI/UiPointerState.cs src/AcDream.App/UI/PluginUiThemeSettings.cs tests/AcDream.App.Tests/UI/PluginUiStyleTests.cs
git commit -m "ui: one style for the modern plugin theme, and pointer state for its controls"
```

```json:metadata
{"files": ["src/AcDream.App/UI/PluginUiStyle.cs", "src/AcDream.App/UI/UiPointerState.cs", "src/AcDream.App/UI/PluginUiThemeSettings.cs", "tests/AcDream.App.Tests/UI/PluginUiStyleTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~PluginUiStyleTests|FullyQualifiedName~PluginThemeClassicIdentityTests|FullyQualifiedName~MarkupListColumnsTests\"", "acceptanceCriteria": ["constants match Global Constraints", "derived colours follow the formulas", "Button draws hover/pressed/faded", "Switch knob right+Text when on, left+Muted when off", "UiPointerState enter/leave/press/drag-out/release", "DrawCheck delegates to PluginUiStyle.Check"], "modelTier": "standard"}
```

---

### Task 3: SemiBold and sharp text for the bundled font

**Goal:** Bundled Noto Sans comes in Regular and SemiBold. Each loaded bundled font carries a 2× sharp companion that `DrawStringDat` uses when `PixelScale ≥ 1.5`, while every measurement stays at 1×. Themed windows can use a SemiBold title font.

**Files:**
- Create: `assets/fonts/NotoSans/NotoSans-SemiBold.ttf`
- Modify: `assets/fonts/NotoSans/README.md`, `NOTICE.md` (Noto Sans section, about line 90)
- Modify: `src/AcDream.App/AcDream.App.csproj` (embedded resource, about line 64)
- Modify: `src/AcDream.App/UI/BundledUiFont.cs`
- Modify: `src/AcDream.App/UI/UiDatFont.cs` (`Sharp`, `UiDatFontSharp`)
- Modify: `src/AcDream.App/UI/UiRenderContext.cs` (`DrawStringDatPass`)
- Modify: `src/AcDream.App/UI/PluginUiThemeSettings.cs` (`ModernTitleFont` on settings and panel)
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (`RetailUiAssets.ModernTitleFont`, line 46; `new PluginUiThemeSettings(...)`, line 4069)
- Modify: `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs` (about line 612)
- Test: `tests/AcDream.App.Tests/UI/BundledUiFontTests.cs` (add cases)

**Acceptance Criteria:**
- [ ] `NotoSans-SemiBold.ttf` has SHA-256 `87a8b90ece1e89746b544e4e086f85a3710e41485a8078f9be874837dfad45d5` and is embedded as `AcDream.App.Fonts.NotoSans-SemiBold.ttf`. The README and NOTICE name it.
- [ ] `BundledUiFont.Bake(size, BundledUiFontWeight.SemiBold)` bakes, and its pixels differ from Regular at the same size.
- [ ] With a sharp companion and `PixelScale` 2, glyphs are drawn from the companion's texture at half its pixel size. At `PixelScale` 1 they come from the 1× texture.
- [ ] `MeasureWidth`/`LineHeight` are identical with and without a companion.
- [ ] `PluginUiThemeSettings.ModernTitleFont` and `UiPluginMarkupPanel.ModernTitleFont` exist, and the client loads a SemiBold 16 font into them.
- [ ] Existing `BundledUiFontTests` pass unchanged. The Classic identity guard passes.

**Verify:** `dotnet test ... --filter "FullyQualifiedName~BundledUiFontTests|FullyQualifiedName~PluginThemeClassicIdentityTests|FullyQualifiedName~UiDatFont"` → all pass

**Steps:**

- [ ] **Step 1: Add the font file**

```bash
curl -sL -o assets/fonts/NotoSans/NotoSans-SemiBold.ttf \
  https://raw.githubusercontent.com/notofonts/noto-fonts/ffebf8c1ee449e544955a7e813c54f9b73848eac/hinted/ttf/NotoSans/NotoSans-SemiBold.ttf
shasum -a 256 assets/fonts/NotoSans/NotoSans-SemiBold.ttf
# expect 87a8b90ece1e89746b544e4e086f85a3710e41485a8078f9be874837dfad45d5
```

Replace `assets/fonts/NotoSans/README.md` with:

```markdown
# Noto Sans Regular and SemiBold

Unmodified hinted TrueType fonts from the Noto project, under SIL OFL 1.1.

| File | Source | SHA-256 |
|---|---|---|
| NotoSans-Regular.ttf | https://github.com/notofonts/noto-fonts/blob/ffebf8c1ee449e544955a7e813c54f9b73848eac/hinted/ttf/NotoSans/NotoSans-Regular.ttf | b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5 |
| NotoSans-SemiBold.ttf | https://github.com/notofonts/noto-fonts/blob/ffebf8c1ee449e544955a7e813c54f9b73848eac/hinted/ttf/NotoSans/NotoSans-SemiBold.ttf | 87a8b90ece1e89746b544e4e086f85a3710e41485a8078f9be874837dfad45d5 |

Full license: OFL.txt. The fonts are embedded in the graphical client assembly;
the license is also copied into its output/publish licenses folder.
```

In `NOTICE.md`, change "embeds unmodified Noto Sans Regular from the Noto project." to "embeds unmodified Noto Sans Regular and SemiBold from the Noto project."

In `src/AcDream.App/AcDream.App.csproj`, after the Regular `EmbeddedResource` element add:

```xml
    <EmbeddedResource Include="..\..\assets\fonts\NotoSans\NotoSans-SemiBold.ttf"
                      LogicalName="AcDream.App.Fonts.NotoSans-SemiBold.ttf" />
```

- [ ] **Step 2: Write failing tests**

Append to `tests/AcDream.App.Tests/UI/BundledUiFontTests.cs`, inside the class:

```csharp
    [Fact]
    public void SemiBoldBakesAndDiffersFromRegular()
    {
        var regular = BundledUiFont.Bake(16f);
        var bold = BundledUiFont.Bake(16f, BundledUiFontWeight.SemiBold);
        Assert.True(bold.Glyphs.ContainsKey('W'));
        Assert.NotEqual(regular.Pixels, bold.Pixels);
    }

    [Fact]
    public void ASharpCompanionDrawsAtHalfSizeOnlyAtScaleTwoAndNeverChangesMetrics()
    {
        var font = BundledUiFont.Bake(16f).CreateFont(11);
        float width = font.MeasureWidth("Buff Bot");
        float line = font.LineHeight;
        var sharpAtlas = BundledUiFont.Bake(32f);
        font.Sharp = new UiDatFontSharp(sharpAtlas.CreateFont(22), 2f);
        Assert.Equal(width, font.MeasureWidth("Buff Bot"));
        Assert.Equal(line, font.LineHeight);

        var (r1, c1) = ThemeDrawCapture.Context(pixelScale: 1f);
        c1.DrawStringDat(font, "B", 0f, 0f, Vector4.One);
        Assert.All(r1.DebugSpriteSegmentVerts, s => Assert.Equal(11u, s.Texture));

        var (r2, c2) = ThemeDrawCapture.Context(pixelScale: 2f);
        c2.DrawStringDat(font, "B", 0f, 0f, Vector4.One);
        var segment = Assert.Single(r2.DebugSpriteSegmentVerts);
        Assert.Equal(22u, segment.Texture);
        Assert.True(sharpAtlas.Glyphs.TryGetValue('B', out var fine));
        var xs = ThemeDrawCapture.Vertices(r2).Select(v => v.Position.X).ToList();
        Assert.Equal(fine.Width / 2f, xs.Max() - xs.Min(), 3);
    }
```

Add `using System.Numerics;` at the top of the file if it's missing.

Run: `dotnet test ... --filter "FullyQualifiedName~BundledUiFontTests"` → compile failure.

- [ ] **Step 3: Weights in `BundledUiFont`**

In `src/AcDream.App/UI/BundledUiFont.cs`:

1. Above the class, add:

```csharp
/// <summary>The weights of the bundled sans font.</summary>
public enum BundledUiFontWeight { Regular, SemiBold }
```

2. Replace `Load` with:

```csharp
    /// <summary>How much sharper the companion bake is than the font it draws for.</summary>
    public const float SharpScale = 2f;

    public static UiDatFont Load(
        TextureCache textures, float pixelHeight = DefaultPixelHeight,
        BundledUiFontWeight weight = BundledUiFontWeight.Regular)
    {
        ArgumentNullException.ThrowIfNull(textures);
        UiDatFont font = Upload(textures, Bake(pixelHeight, weight));
        // A sharper twin for high-density displays, where the bake fits.
        if (pixelHeight * SharpScale <= MaximumPixelHeight)
            font.Sharp = new UiDatFontSharp(Upload(textures, Bake(pixelHeight * SharpScale, weight)), SharpScale);
        return font;
    }

    private static UiDatFont Upload(TextureCache textures, Atlas atlas) =>
        atlas.CreateFont(textures.UploadRgba8(atlas.Pixels, atlas.Width, atlas.Height, nearest: true));

    private const float MaximumPixelHeight = 32f;
```

3. Replace `ReadEmbeddedFontBytes()` with:

```csharp
    /// <summary>The embedded Noto Sans file, for anything else that bakes it.</summary>
    internal static byte[] ReadEmbeddedFontBytes() => ReadEmbeddedFontBytes(BundledUiFontWeight.Regular);

    internal static byte[] ReadEmbeddedFontBytes(BundledUiFontWeight weight)
    {
        string name = weight == BundledUiFontWeight.SemiBold
            ? "AcDream.App.Fonts.NotoSans-SemiBold.ttf"
            : "AcDream.App.Fonts.NotoSans-Regular.ttf";
        using var stream = typeof(BundledUiFont).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Bundled font {name} is missing.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
```

4. Change the `Bake` signature and its first lines to:

```csharp
    internal static unsafe Atlas Bake(
        float pixelHeight = DefaultPixelHeight, BundledUiFontWeight weight = BundledUiFontWeight.Regular)
    {
        if (!float.IsFinite(pixelHeight) || pixelHeight < 8 || pixelHeight > MaximumPixelHeight)
            throw new ArgumentOutOfRangeException(nameof(pixelHeight), "Font size must be between 8 and 32 pixels.");
        byte[] fontBytes = ReadEmbeddedFontBytes(weight);
```

Leave the rest of `Bake` unchanged.

- [ ] **Step 4: The sharp companion on `UiDatFont`**

In `src/AcDream.App/UI/UiDatFont.cs`, inside the class after `BorderY`:

```csharp
    /// <summary>
    /// The same font baked at a multiple of its size, whose glyphs are drawn
    /// in its place on displays with that many device pixels per point.
    /// Every measurement, and so all layout, still comes from this font.
    /// Only bundled fonts have one; dat fonts never do.
    /// </summary>
    internal UiDatFontSharp? Sharp { get; set; }
```

and after the class, in the same file:

```csharp
/// <summary>A sharper bake of a font and how many times its size it was baked at.</summary>
internal sealed record UiDatFontSharp(UiDatFont Font, float Scale);
```

- [ ] **Step 5: Draw from the companion**

In `src/AcDream.App/UI/UiRenderContext.cs`, replace `DrawStringDatPass` with:

```csharp
    public void DrawStringDatPass(
        UiDatFont font, string text, float x, float y, Vector4 tint, bool isOutlinePass)
    {
        if (font is null || string.IsNullOrEmpty(text)) return;

        float originX = _current.X + x;
        float originY = _current.Y + y;

        float baseY = System.MathF.Floor(originY + 0.5f);

        // On a high-density display a bundled font's sharper twin supplies
        // the glyph images; the pen still advances by this font's own metrics.
        UiDatFontSharp? sharp = !isOutlinePass && PixelScale >= 1.5f ? font.Sharp : null;
        float grid = sharp is null ? 1f : MathF.Min(PixelScale, sharp.Scale);

        float pen = originX;
        for (int i = 0; i < text.Length; i++)
        {
            if (!font.TryGetGlyph(text[i], out var g))
                continue;

            if (sharp is not null && sharp.Font.TryGetGlyph(text[i], out var fine)
                && fine.Width > 0 && fine.Height > 0)
            {
                float texel = 1f / sharp.Scale;
                float fx = Snap(pen + fine.HorizontalOffsetBefore * texel, grid);
                float fy = baseY + MathF.Round(fine.VerticalOffsetBefore * texel * grid) / grid;
                DrawFillGlyph(sharp.Font, fine, fx, fy, fine.Width * texel, fine.Height * texel, tint);
                pen += UiDatFont.GlyphAdvance(g);
                continue;
            }

            // Horizontal: snap each glyph's dest X to a whole pixel (the pen keeps its
            // true fractional advance). Vertical: integer baseline + integer per-glyph
            // offset — never an independent per-glyph round (see baseY's note above).
            // Half-up for the same anti-vibration reason as baseY.
            float gx = System.MathF.Floor(pen + g.HorizontalOffsetBefore + 0.5f);
            float gy = baseY + g.VerticalOffsetBefore;
            float gw = g.Width;
            float gh = g.Height;

            if (gw > 0f && gh > 0f)
            {
                if (isOutlinePass)
                    DrawOutlineGlyph(font, g, gx, gy, gw, gh, tint);
                else
                    DrawFillGlyph(font, g, gx, gy, gw, gh, tint);
            }

            pen += UiDatFont.GlyphAdvance(g);
        }
    }
```

Keep the existing comment block above the method (the one about `baseY`), if there is one. The 1× branch is the current code moved below the new branch, so a font without `Sharp` takes exactly today's path.

- [ ] **Step 6: Title font in theme settings and the client**

In `src/AcDream.App/UI/PluginUiThemeSettings.cs`:

```csharp
    public UiDatFont? ModernFont { get; }

    /// <summary>The heavier weight themed windows set their titles in; null falls back to <see cref="ModernFont"/>.</summary>
    public UiDatFont? ModernTitleFont { get; }

    public PluginUiThemeSettings(
        SettingsStore? store = null, UiDatFont? modernFont = null, UiDatFont? modernTitleFont = null)
    {
        _store = store;
        ModernFont = modernFont;
        ModernTitleFont = modernTitleFont;
        _theme = Enum.TryParse<PluginUiTheme>(store?.LoadPluginUiTheme(), out var value)
            && Enum.IsDefined(value) ? value : PluginUiTheme.Classic;
    }
```

and in `UiPluginMarkupPanel`, after `ModernFont`:

```csharp
    public UiDatFont? ModernTitleFont => _settings.ModernTitleFont;
```

In `src/AcDream.App/UI/RetailUiRuntime.cs`, `RetailUiAssets` (line 46): change `UiDatFont? ModernFont = null);` to

```csharp
    UiDatFont? ModernFont = null,
    UiDatFont? ModernTitleFont = null);
```

and at about line 4069:

```csharp
        _pluginThemes ??= new PluginUiThemeSettings(
            _bindings.Chat.Store, _bindings.Assets.ModernFont, _bindings.Assets.ModernTitleFont);
```

In `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs` (about line 612), replace `BundledUiFont.Load(d.TextureCache));` with

```csharp
                BundledUiFont.Load(d.TextureCache),
                BundledUiFont.Load(d.TextureCache, weight: BundledUiFontWeight.SemiBold));
```

- [ ] **Step 7: Run tests**

Run: `dotnet test ... --filter "FullyQualifiedName~BundledUiFontTests|FullyQualifiedName~PluginThemeClassicIdentityTests|FullyQualifiedName~UiDatFont"`
Expected: all PASS. Then build the whole solution: `dotnet build AcDream.slnx -c Release` → 0 errors.

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json'
git add assets/fonts/NotoSans NOTICE.md src/AcDream.App/AcDream.App.csproj src/AcDream.App/UI/BundledUiFont.cs src/AcDream.App/UI/UiDatFont.cs src/AcDream.App/UI/UiRenderContext.cs src/AcDream.App/UI/PluginUiThemeSettings.cs src/AcDream.App/UI/RetailUiRuntime.cs src/AcDream.App/Composition/InteractionRetainedUiComposition.cs tests/AcDream.App.Tests/UI/BundledUiFontTests.cs
git commit -m "ui: SemiBold and sharp high-density text for the bundled font"
```

```json:metadata
{"files": ["assets/fonts/NotoSans/NotoSans-SemiBold.ttf", "assets/fonts/NotoSans/README.md", "NOTICE.md", "src/AcDream.App/AcDream.App.csproj", "src/AcDream.App/UI/BundledUiFont.cs", "src/AcDream.App/UI/UiDatFont.cs", "src/AcDream.App/UI/UiRenderContext.cs", "src/AcDream.App/UI/PluginUiThemeSettings.cs", "src/AcDream.App/UI/RetailUiRuntime.cs", "src/AcDream.App/Composition/InteractionRetainedUiComposition.cs", "tests/AcDream.App.Tests/UI/BundledUiFontTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~BundledUiFontTests|FullyQualifiedName~PluginThemeClassicIdentityTests|FullyQualifiedName~UiDatFont\"", "acceptanceCriteria": ["SemiBold file with pinned SHA-256, embedded, README+NOTICE", "SemiBold bakes and differs from Regular", "sharp companion used at scale 2 at half size, 1x texture at scale 1", "MeasureWidth/LineHeight unchanged by companion", "ModernTitleFont on settings and panel, client loads SemiBold 16", "existing font tests and Classic guard pass"], "modelTier": "standard"}
```

---

### Task 4: Window chrome and title

**Goal:** A themed window draws a soft shadow, a rounded surface, a header band behind its title, a rounded edge and a dotted resize grip. Its title moves into the header in SemiBold. Classic keeps today's look.

**Files:**
- Modify: `src/AcDream.App/UI/PluginUiThemeSettings.cs` (`UiPluginMarkupPanel`)
- Modify: `src/AcDream.App/UI/PluginMarkupTheme.cs` (new `RegisterTitle`)
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (lines 75–76)
- Test: `tests/AcDream.App.Tests/UI/PluginThemeWindowTests.cs`

**Acceptance Criteria:**
- [ ] Under Moss, a titled window's draw includes vertices left of x=0 (shadow), the header gradient's top colour, and the `Border` separator at y=24.
- [ ] An untitled themed window draws no header.
- [ ] The title label sits at `Left=12`, `Top=round((24-LineHeight)/2)` in `ModernTitleFont` when one is set, otherwise `ModernFont`. Under Classic it returns to `Left=8, Top=4` and the classic font.
- [ ] `BundledUiFontTests.ThemeFontSwitchRestoresClassicFont` and the Classic guard pass unchanged.

**Verify:** `dotnet test ... --filter "FullyQualifiedName~PluginThemeWindowTests|FullyQualifiedName~BundledUiFontTests|FullyQualifiedName~PluginUiThemeTests|FullyQualifiedName~PluginThemeClassicIdentityTests"` → all pass

**Steps:**

- [ ] **Step 1: Write failing tests**

`tests/AcDream.App.Tests/UI/PluginThemeWindowTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class PluginThemeWindowTests
{
    private static (UiRoot Root, UiNineSlicePanel Panel, PluginUiThemeSettings Settings) Build(
        string title, UiDatFont? modern = null, UiDatFont? bold = null, UiDatFont? classic = null)
    {
        var settings = new PluginUiThemeSettings(modernFont: modern, modernTitleFont: bold);
        var panel = MarkupDocument.Build(
            $"<panel x=\"100\" y=\"100\" w=\"200\" h=\"120\" {title} theme=\"plugin\"><label x=\"12\" y=\"30\" text=\"Body\" /></panel>",
            new object(), _ => (0u, 0, 0), datFont: classic, themes: settings);
        var root = new UiRoot { Width = 600, Height = 600 };
        root.AddChild(panel);
        return (root, panel, settings);
    }

    [Fact]
    public void ATitledThemedWindowDrawsShadowHeaderAndSeparator()
    {
        var (root, _, settings) = Build("title=\"Buff Bot\"");
        settings.Theme = PluginUiTheme.Moss;
        root.Tick(0.016, 1);
        var (renderer, ctx) = ThemeDrawCapture.Context();
        root.Draw(ctx);
        var v = ThemeDrawCapture.Vertices(renderer);
        var p = PluginUiPalette.Moss;
        Assert.Contains(v, x => x.Position.X < 100f);
        Assert.True(ThemeDrawCapture.HasColor(v, PluginUiStyle.Mix(p.Background, p.Text, 0.06f), 0.01f));
        Assert.Contains(v, x => MathF.Abs(x.Position.Y - 124f) < 0.01f
            && Vector4.Distance(x.Color, p.Border) < 0.01f);
    }

    [Fact]
    public void AnUntitledThemedWindowHasNoHeader()
    {
        var (root, _, settings) = Build("");
        settings.Theme = PluginUiTheme.Moss;
        root.Tick(0.016, 1);
        var (renderer, ctx) = ThemeDrawCapture.Context();
        root.Draw(ctx);
        var p = PluginUiPalette.Moss;
        Assert.False(ThemeDrawCapture.HasColor(
            ThemeDrawCapture.Vertices(renderer), PluginUiStyle.Mix(p.Background, p.Text, 0.06f), 0.002f));
    }

    [Fact]
    public void TheTitleMovesIntoTheHeaderInTheTitleFontAndBack()
    {
        var classic = BundledUiFont.Bake(12).CreateFont(1);
        var modern = BundledUiFont.Bake().CreateFont(2);
        var bold = BundledUiFont.Bake(16, BundledUiFontWeight.SemiBold).CreateFont(3);
        var (root, panel, settings) = Build("title=\"Buff Bot\"", modern, bold, classic);
        var title = Assert.IsType<UiLabel>(panel.Children[0]);
        Assert.Equal((8f, 4f), (title.Left, title.Top));
        settings.Theme = PluginUiTheme.Brass;
        root.Tick(0.016, 1);
        Assert.Same(bold, title.DatFont);
        Assert.Equal(12f, title.Left);
        Assert.Equal(MathF.Round((PluginUiStyle.HeaderHeight - bold.LineHeight) / 2f), title.Top);
        settings.Theme = PluginUiTheme.Classic;
        root.Tick(0.016, 2);
        Assert.Same(classic, title.DatFont);
        Assert.Equal((8f, 4f), (title.Left, title.Top));
    }
}
```

Run → fails (no header, no title move).

- [ ] **Step 2: Window drawing**

In `src/AcDream.App/UI/PluginUiThemeSettings.cs`, in `UiPluginMarkupPanel`, add the property:

```csharp
    /// <summary>Whether the window's markup gave it a title, so the header band is drawn.</summary>
    internal bool HasTitle { get; set; }
```

and replace `OnDraw` and `OnDrawAfterChildren` with:

```csharp
    protected override void OnDraw(UiRenderContext ctx)
    {
        if (_settings.Palette is not { } p) { base.OnDraw(ctx); return; }
        PluginUiStyle.WindowShadow(ctx, Width, Height);
        ctx.FillRoundedRect(0, 0, Width, Height, PluginUiStyle.WindowRadius, p.Background);
        if (HasTitle) PluginUiStyle.Header(ctx, p, Width);
    }
    protected override void OnDrawAfterChildren(UiRenderContext ctx)
    {
        if (_settings.Palette is not { } p) { base.OnDrawAfterChildren(ctx); return; }
        ctx.StrokeRoundedRect(0.5f, 0.5f, Width - 1f, Height - 1f, PluginUiStyle.WindowRadius - 0.5f, p.Border, 1f);
        if (Resizable && DrawResizeAffordances)
            PluginUiStyle.ResizeGrip(ctx, p, Width, Height);
    }
```

- [ ] **Step 3: Title theme action**

In `src/AcDream.App/UI/PluginMarkupTheme.cs`, add to the class:

```csharp
    /// <summary>
    /// Moves a themed window's title into the header band and sets it in the
    /// title weight. Register's label action has already run for the title,
    /// so Classic's font is restored there; this only puts the position back.
    /// </summary>
    public static void RegisterTitle(UiPluginMarkupPanel panel, UiLabel title)
    {
        float left = title.Left, top = title.Top;
        panel.AddThemeAction(p =>
        {
            if (p is null) { title.Left = left; title.Top = top; return; }
            if (panel.ModernTitleFont is { } bold) title.DatFont = bold;
            float lineHeight = title.DatFont?.LineHeight ?? 14f;
            title.Left = 12f;
            title.Top = MathF.Max(0f, MathF.Round((PluginUiStyle.HeaderHeight - lineHeight) / 2f));
        });
    }
```

In `src/AcDream.App/UI/MarkupDocument.cs`, replace

```csharp
        if (panel is UiPluginMarkupPanel titlePanel && panel.Children.Count > 0)
            PluginMarkupTheme.Register(titlePanel, panel.Children[0], new XElement("label"));
```

with

```csharp
        if (panel is UiPluginMarkupPanel titlePanel && panel.Children.Count > 0)
        {
            titlePanel.HasTitle = true;
            PluginMarkupTheme.Register(titlePanel, panel.Children[0], new XElement("label"));
            PluginMarkupTheme.RegisterTitle(titlePanel, (UiLabel)panel.Children[0]);
        }
```

- [ ] **Step 4: Run tests** (Verify command) → all PASS.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/PluginUiThemeSettings.cs src/AcDream.App/UI/PluginMarkupTheme.cs src/AcDream.App/UI/MarkupDocument.cs tests/AcDream.App.Tests/UI/PluginThemeWindowTests.cs
git commit -m "ui: themed plugin windows get a shadow, header band and rounded edge"
```

```json:metadata
{"files": ["src/AcDream.App/UI/PluginUiThemeSettings.cs", "src/AcDream.App/UI/PluginMarkupTheme.cs", "src/AcDream.App/UI/MarkupDocument.cs", "tests/AcDream.App.Tests/UI/PluginThemeWindowTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~PluginThemeWindowTests|FullyQualifiedName~BundledUiFontTests|FullyQualifiedName~PluginUiThemeTests|FullyQualifiedName~PluginThemeClassicIdentityTests\"", "acceptanceCriteria": ["titled themed window draws shadow outside, header gradient, separator at y=24", "untitled themed window draws no header", "title Left 12 / centred Top in title font, back to 8/4 + classic font", "existing font/theme tests and Classic guard pass"], "modelTier": "standard"}
```

---

### Task 5: Buttons, tabs and toggles

**Goal:** Themed buttons are rounded and react to hover and press. Tabs are pills. Toggles are switches. Focus shows an accent ring.

**Files:**
- Modify: `src/AcDream.App/UI/UiPanel.cs` (`UiSimpleButton`)
- Modify: `src/AcDream.App/UI/UiMarkupTabButton.cs`
- Modify: `src/AcDream.App/UI/UiMarkupToggle.cs`
- Modify: `src/AcDream.App/UI/PluginMarkupTheme.cs` (button and toggle cases)
- Test: `tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs` (create; extended by Tasks 6–7)

**Acceptance Criteria:**
- [ ] `UiSimpleButton.ThemePalette` (public, settable) exists. `UiMarkupTabButton` uses the inherited one, so its own property is removed.
- [ ] A themed button draws `Hover(fill)` after `HoverEnter` and `Pressed(fill)` after `MouseDown`. Its `OnEvent` return values are unchanged for every event type.
- [ ] A selected themed tab fills `Selected` and has no underline. Its text is `Text` when selected and `Muted` otherwise.
- [ ] A themed toggle draws a switch, and its caption starts at x = 1 + 26 + 8.
- [ ] `PluginMarkupTheme` sets `ThemePalette` on buttons and toggles.
- [ ] Classic guard and `PluginUiThemeTests` pass.

**Verify:** `dotnet test ... --filter "FullyQualifiedName~PluginThemeControlTests|FullyQualifiedName~PluginUiThemeTests|FullyQualifiedName~PluginThemeClassicIdentityTests|FullyQualifiedName~UiSimpleButton|FullyQualifiedName~MarkupPanelClickTests"` → all pass

**Steps:**

- [ ] **Step 1: Write failing tests**

`tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>Themed controls draw the modern shapes and states; see PluginUiStyle.</summary>
public sealed class PluginThemeControlTests
{
    private static readonly PluginUiPalette P = PluginUiPalette.Moss;

    internal sealed class Binding
    {
        public Action Click => () => { };
        public bool On => true;
        public bool Off => false;
        public Action Toggle => () => { };
        public IReadOnlyList<string> Items => ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];
        public string Selected => "Beta";
        public int Index => 1;
        public float Value => 0.5f;
        public Action<float> Changed => _ => { };
        public IReadOnlyList<string> Log => Enumerable.Range(0, 40).Select(i => $"line {i}").ToArray();
        public float Fill => 0.5f;
    }

    /// <summary>Builds one themed window around <paramref name="body"/>, switched to Moss.</summary>
    internal static (UiRoot Root, UiNineSlicePanel Panel) Themed(string body)
    {
        var settings = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"400\" h=\"300\" theme=\"plugin\">{body}</panel>",
            new Binding(), _ => (0u, 0, 0), themes: settings);
        var root = new UiRoot { Width = 600, Height = 600 };
        root.AddChild(panel);
        root.Tick(0.016, 1);
        return (root, panel);
    }

    internal static List<(Vector2 Position, Vector4 Color)> DrawElement(UiElement element)
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        element.DrawSelfAndChildren(ctx);
        return ThemeDrawCapture.Vertices(renderer);
    }

    [Fact]
    public void AThemedButtonShowsHoverAndPressWithoutChangingWhatItHandles()
    {
        var (_, panel) = Themed("<button x=\"10\" y=\"10\" w=\"80\" h=\"24\" text=\"Go\" onclick=\"{Click}\" />");
        var button = Assert.IsType<UiSimpleButton>(panel.Children[0]);
        Assert.Same(P, button.ThemePalette);
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(button), P.Field));

        button.OnEvent(new UiEvent { Type = UiEventType.HoverEnter });
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(button), PluginUiStyle.Hover(P, P.Field)));
        bool handledDown = button.OnEvent(new UiEvent { Type = UiEventType.MouseDown });
        Assert.False(handledDown);
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(button), PluginUiStyle.Pressed(P.Field)));
    }

    [Fact]
    public void ASelectedThemedTabIsAPillWithoutUnderline()
    {
        var (_, panel) = Themed(
            "<tab x=\"10\" y=\"10\" w=\"80\" h=\"24\" text=\"Status\" selected=\"{On}\" onclick=\"{Click}\" />" +
            "<tab x=\"100\" y=\"10\" w=\"80\" h=\"24\" text=\"Spells\" selected=\"{Off}\" onclick=\"{Click}\" />");
        var selected = Assert.IsType<UiMarkupTabButton>(panel.Children[0]);
        var other = Assert.IsType<UiMarkupTabButton>(panel.Children[1]);
        var v = DrawElement(selected);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Selected));
        Assert.False(ThemeDrawCapture.HasColor(v, P.Accent));
        Assert.Equal(P.Text, selected.TextColor);
        Assert.Equal(P.Muted, other.TextColor);
    }

    [Fact]
    public void AThemedToggleIsASwitchWithItsCaptionAfterIt()
    {
        var (_, panel) = Themed("<toggle x=\"10\" y=\"10\" w=\"160\" h=\"20\" text=\"Auto\" checked=\"{On}\" onclick=\"{Toggle}\" />");
        var toggle = Assert.IsType<UiMarkupToggle>(panel.Children[0]);
        var v = DrawElement(toggle);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Text));                    // knob, on
        Assert.True(ThemeDrawCapture.HasColor(v, P.Accent));                  // switch edge, on
        // No font in this build, so only the switch is drawn: it ends where the caption's gap begins.
        Assert.True(v.Where(p => p.Color.W > 0.05f).Max(p => p.Position.X)
            <= toggle.Left + 1f + PluginUiStyle.SwitchWidth + 0.51f);
    }
}
```

`DrawSelfAndChildren` is internal on `UiElement`. If `UiEvent` needs a constructor, use the form given in Task 2.

Run → compile failure (`UiSimpleButton.ThemePalette`).

- [ ] **Step 2: `UiSimpleButton`**

In `src/AcDream.App/UI/UiPanel.cs`, in `UiSimpleButton`:

Add members after `Outline`:

```csharp
    /// <summary>The shared plugin theme this button draws in; null draws Classic.</summary>
    public PluginUiPalette? ThemePalette { get; set; }

    private UiPointerState _pointer;

    /// <summary>The state a themed face shows: hovered, pressed, disabled or normal.</summary>
    private protected UiControlState ThemeState => _pointer.State(Enabled);

    private protected bool KeyboardFocused => _keyboardActivation.Focused;
```

At the top of `OnEvent`, before the existing first line:

```csharp
        _pointer.Observe(in e);
```

In `OnDraw`, replace

```csharp
        base.OnDraw(ctx);
        if (_keyboardActivation.Focused)
            ctx.DrawRectOutline(1f, 1f, Width - 2f, Height - 2f,
                new Vector4(1f, 0.82f, 0.25f, 1f), 1f);
```

with

```csharp
        if (ThemePalette is { } palette)
        {
            DrawThemedFace(ctx, palette);
        }
        else
        {
            base.OnDraw(ctx);
            if (_keyboardActivation.Focused)
                ctx.DrawRectOutline(1f, 1f, Width - 2f, Height - 2f,
                    new Vector4(1f, 0.82f, 0.25f, 1f), 1f);
        }
```

and add the method to the class:

```csharp
    /// <summary>The face a themed button draws under its icon and caption.</summary>
    private protected virtual void DrawThemedFace(UiRenderContext ctx, PluginUiPalette palette)
    {
        PluginUiStyle.Button(ctx, palette, Width, Height,
            BackgroundColorSource?.Invoke() ?? BackgroundColor,
            BorderColorSource?.Invoke() ?? BorderColor, ThemeState);
        if (KeyboardFocused)
            PluginUiStyle.FocusRing(ctx, palette, Width, Height, PluginUiStyle.ControlRadius);
    }
```

- [ ] **Step 3: Tabs**

Replace the body of `src/AcDream.App/UI/UiMarkupTabButton.cs`'s class from `public PluginUiPalette? ThemePalette { get; set; }` through the end with:

```csharp
    public Func<bool>? SelectedSource { get; set; }

    public bool IsSelected => SelectedSource?.Invoke() ?? false;

    public UiMarkupTabButton()
    {
        BackgroundColor = Vector4.Zero;
        BorderColor = Vector4.Zero;
        BorderThickness = 0f;
        Outline = true;
    }

    protected override void OnTick(double deltaSeconds)
    {
        base.OnTick(deltaSeconds);
        TextColor = !Enabled
            ? DisabledText
            : IsSelected ? ThemePalette?.Text ?? ActiveText : ThemePalette?.Muted ?? NormalText;
    }

    private protected override void DrawThemedFace(UiRenderContext ctx, PluginUiPalette palette)
    {
        PluginUiStyle.Tab(ctx, palette, Width, Height, IsSelected, ThemeState);
        if (KeyboardFocused)
            PluginUiStyle.FocusRing(ctx, palette, Width, Height, PluginUiStyle.ControlRadius);
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        base.OnDraw(ctx);
        if (IsSelected && ThemePalette is null)
            ctx.DrawFill(2f, Height - 2f, MathF.Max(0f, Width - 4f), 1f, Underline);
    }
}
```

Keep the four colour constants at the top of the class.

- [ ] **Step 4: Toggles**

In `src/AcDream.App/UI/UiMarkupToggle.cs`, make `OnDraw` start with

```csharp
        if (ThemePalette is { } themed)
        {
            DrawThemed(ctx, themed);
            return;
        }
```

and in the rest of `OnDraw` replace

```csharp
        if (ThemePalette is { } p) p.DrawCheck(ctx, 1f, checkY, IsChecked);
        else UiCheckLamp.Draw(ctx, 1f, checkY, IsChecked);
```

with `UiCheckLamp.Draw(ctx, 1f, checkY, IsChecked);`. Change `outline: ThemePalette is null` to `outline: true` (that line is only reached by Classic now). Then add:

```csharp
    private void DrawThemed(UiRenderContext ctx, PluginUiPalette palette)
    {
        float switchY = MathF.Max(0f, (Height - PluginUiStyle.SwitchHeight) * 0.5f);
        PluginUiStyle.Switch(ctx, palette, 1f, switchY, IsChecked, Enabled);
        if (_keyboardActivation.Focused)
            PluginUiStyle.FocusRing(ctx, palette, Width, Height, PluginUiStyle.SmallRadius);

        string caption = TextSource?.Invoke() ?? Text;
        Vector4 textColor = TextColorSource?.Invoke() ?? TextColor;
        Vector4 color = Enabled ? textColor : textColor with { W = 0.42f };
        float x = 1f + PluginUiStyle.SwitchWidth + PluginUiStyle.SwitchCaptionGap;
        if (DatFont is { } dat)
            ctx.DrawStringDat(dat, caption, x, (Height - dat.LineHeight) * 0.5f, color, outline: false);
        else
            ctx.DrawString(caption, x, 1f, color);
    }
```

- [ ] **Step 5: Wire the palette**

In `src/AcDream.App/UI/PluginMarkupTheme.cs`:
- `case UiSimpleButton button:` — inside its theme action add `button.ThemePalette = p;` as the first statement.
- `case UiMarkupToggle toggle:` — it already sets `toggle.ThemePalette = p`, so no change.
- `case UiMarkupTabButton tab:` — it already sets `tab.ThemePalette = p`, which is now the inherited property, so no change.

- [ ] **Step 6: Run tests** (Verify command) → all PASS. Then run the wider UI suite once: `dotnet test ... --filter "FullyQualifiedName~AcDream.App.Tests.UI"` and confirm there are no new failures. Known flaky tests are listed in the local env memory.

- [ ] **Step 7: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/UiPanel.cs src/AcDream.App/UI/UiMarkupTabButton.cs src/AcDream.App/UI/UiMarkupToggle.cs src/AcDream.App/UI/PluginMarkupTheme.cs tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs
git commit -m "ui: themed buttons, tabs and switches with hover, press and focus"
```

```json:metadata
{"files": ["src/AcDream.App/UI/UiPanel.cs", "src/AcDream.App/UI/UiMarkupTabButton.cs", "src/AcDream.App/UI/UiMarkupToggle.cs", "src/AcDream.App/UI/PluginMarkupTheme.cs", "tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~PluginThemeControlTests|FullyQualifiedName~PluginUiThemeTests|FullyQualifiedName~PluginThemeClassicIdentityTests|FullyQualifiedName~UiSimpleButton|FullyQualifiedName~MarkupPanelClickTests\"", "acceptanceCriteria": ["UiSimpleButton.ThemePalette; tab uses inherited", "themed button hover/pressed colours, OnEvent returns unchanged", "selected tab pill Selected, no underline; Text/Muted text", "toggle draws switch, caption at 35", "PluginMarkupTheme sets ThemePalette", "Classic guard and theme tests pass"], "modelTier": "standard"}
```

---

### Task 6: Fields and menus

**Goal:** Themed fields are rounded and glow when focused. Themed menus get a rounded face with a chevron, a rounded popup with a shadow, inset rows, a themed search band and a slim scroll thumb.

**Files:**
- Modify: `src/AcDream.App/UI/UiField.cs` (`ThemePalette`; `OnDraw` background, about line 476)
- Modify: `src/AcDream.App/UI/UiMenu.cs` (`ThemePalette`, pointer, `DrawPlainClosedState`, `DrawSearch`, both plain popups, `DrawPopupScrollbarPlain`)
- Modify: `src/AcDream.App/UI/PluginMarkupTheme.cs` (field and menu cases)
- Test: `tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs` (add cases)

**Acceptance Criteria:**
- [ ] A themed field draws `Border` when blurred, and `Accent` plus the focus glow when focused.
- [ ] A themed menu face shows `Hover(fill)` on hover and an `Accent` edge when open. Its chevron is drawn in `PlainTriangleColor` instead of the stepped triangle.
- [ ] A themed open popup draws a shadow, a rounded surface and inset rounded rows. Its search field is themed. A popup that overflows draws a slim thumb with no track box.
- [ ] A Classic plain menu (no palette) draws exactly as before (covered by the Classic guard), and `UiMenu*Tests` pass.

**Verify:** `dotnet test ... --filter "FullyQualifiedName~PluginThemeControlTests|FullyQualifiedName~UiMenu|FullyQualifiedName~UiField|FullyQualifiedName~PluginUiThemeTests|FullyQualifiedName~PluginThemeClassicIdentityTests"` → all pass

**Steps:**

- [ ] **Step 1: Add failing tests** to `PluginThemeControlTests`:

```csharp
    [Fact]
    public void AThemedFieldGlowsWhenFocused()
    {
        var (_, panel) = Themed("<field x=\"10\" y=\"10\" w=\"160\" h=\"24\" text=\"abc\" />");
        var field = Assert.IsType<UiField>(panel.Children[0]);
        Assert.Same(P, field.ThemePalette);
        var blurred = DrawElement(field);
        Assert.True(ThemeDrawCapture.HasColor(blurred, P.Border));
        Assert.False(ThemeDrawCapture.HasColor(blurred, P.Accent));
        field.OnEvent(new UiEvent { Type = UiEventType.FocusGained });
        Assert.True(field.IsFocused);
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(field), P.Accent));
    }

    [Fact]
    public void AThemedMenuHasARoundedFaceAndAShadowedPopup()
    {
        var (root, panel) = Themed("<menu x=\"10\" y=\"10\" w=\"160\" h=\"24\" items=\"{Items}\" selected=\"{Selected}\" rows=\"3\" />");
        var menu = Assert.IsType<UiMenu>(panel.Children[0]);
        Assert.Same(P, menu.ThemePalette);
        menu.OnEvent(new UiEvent { Type = UiEventType.HoverEnter });
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(menu), PluginUiStyle.Hover(P, menu.PlainBackgroundColor)));

        int x = (int)(menu.Left + 20), y = (int)(menu.Top + 12);
        root.OnMouseDown(UiMouseButton.Left, x, y, 0);
        root.OnMouseUp(UiMouseButton.Left, x, y, 0);
        Assert.True(menu.IsOpen);
        var (renderer, ctx) = ThemeDrawCapture.Context();
        root.Draw(ctx);
        var v = ThemeDrawCapture.Vertices(renderer);
        Assert.Contains(v, p => p.Color.X == 0f && p.Color.Y == 0f && p.Color.Z == 0f && p.Color.W > 0f
            && p.Position.Y > menu.Top + menu.Height);                                  // popup shadow
        Assert.True(ThemeDrawCapture.HasColor(v, P.Accent));                         // open edge
    }
```

If `UiMenu` exposes no `IsOpen`, use the existing open-state accessor the menu tests use (search `tests/AcDream.App.Tests/UI/UiMenu*Tests.cs` for how they assert the popup is open) and adjust the line.

Run → compile failure.

- [ ] **Step 2: Field**

In `src/AcDream.App/UI/UiField.cs` add near `SelectionColor`:

```csharp
    /// <summary>The shared plugin theme this field draws in; null draws Classic.</summary>
    public PluginUiPalette? ThemePalette { get; set; }
```

In `OnDraw`, replace

```csharp
        if (!lit)
            ctx.DrawFill(0, 0, Width, Height, BackgroundColorSource?.Invoke() ?? BackgroundColor);
```

with

```csharp
        if (!lit)
        {
            Vector4 fill = BackgroundColorSource?.Invoke() ?? BackgroundColor;
            if (ThemePalette is { } palette)
                PluginUiStyle.Field(ctx, palette, Width, Height, fill, _focused);
            else
                ctx.DrawFill(0, 0, Width, Height, fill);
        }
```

- [ ] **Step 3: Menu**

In `src/AcDream.App/UI/UiMenu.cs`:

1. Next to `PlainHoverColor` add:

```csharp
    /// <summary>The shared plugin theme the plain look draws in; null draws Classic plain.</summary>
    public PluginUiPalette? ThemePalette { get; set; }

    private UiPointerState _pointer;
```

2. First statement of `OnEvent`: `_pointer.Observe(in e);`

3. Replace `DrawPlainClosedState` with:

```csharp
    private void DrawPlainClosedState(UiRenderContext ctx)
    {
        bool open = _open || _facePressed;
        if (ThemePalette is { } palette)
        {
            PluginUiStyle.Button(ctx, palette, Width, Height, PlainBackgroundColor,
                open ? PlainOpenBorderColor : PlainBorderColor,
                open ? UiControlState.Normal : _pointer.State(Enabled));
        }
        else
        {
            ctx.DrawFill(0f, 0f, Width, Height, PlainBackgroundColor);
            ctx.DrawRectOutline(0f, 0f, Width, Height, open ? PlainOpenBorderColor : PlainBorderColor, 1f);
        }

        string caption = ButtonLabelProvider?.Invoke() ?? "";
        UiDatFont? captionFont = ButtonDatFont ?? DatFont;
        float captionLineH = captionFont?.LineHeight ?? Font?.LineHeight ?? 14f;
        float textY = (Height - captionLineH) * 0.5f;
        if (captionFont is { } cf)
            ctx.DrawStringDat(cf, caption, PlainPadding, textY, PlainTextColor, Outline, OutlineColor);
        else
            ctx.DrawString(caption, PlainPadding, textY, PlainTextColor, Font);

        if (ThemePalette is not null)
            PluginUiStyle.Chevron(ctx, Width - 6f - 8f, (Height - 4f) * 0.5f, 8f, 4f, PlainTriangleColor);
        else
            DrawPlainTriangle(ctx);
    }
```

`PluginMarkupTheme` maps `PlainOpenBorderColor` to `Accent` when themed, so an open themed menu's edge is `Accent`.

4. In `DrawSearch`, after `_searchField.SelectionColor = PlainSelectedColor;` add `_searchField.ThemePalette = ThemePalette;`, and wrap the outline:

```csharp
        if (ThemePalette is null)
            ctx.DrawRectOutline(Border, PopupTop + Border, InteriorW, SearchHeight - 2f, PlainOpenBorderColor, 1f);
```

5. Add two helpers next to `DrawGridPopupPlain`:

```csharp
    private void DrawPlainPopupSurface(UiRenderContext ctx, float top)
    {
        if (ThemePalette is not null)
        {
            PluginUiStyle.PopupShadow(ctx, 0f, top, OuterW, OuterH);
            PluginUiStyle.Surface(ctx, 0f, top, OuterW, OuterH, PluginUiStyle.ContainerRadius,
                PlainBackgroundColor, PlainBorderColor);
            return;
        }
        ctx.DrawFill(0f, top, OuterW, OuterH, PlainBackgroundColor);
        ctx.DrawRectOutline(0f, top, OuterW, OuterH, PlainBorderColor, 1f);
    }

    private void DrawPlainRowHighlight(UiRenderContext ctx, float x, float y, Vector4 color)
    {
        if (ThemePalette is not null)
            PluginUiStyle.Row(ctx, x, y, ColumnWidth, RowHeight, color);
        else
            ctx.DrawFill(x, y, ColumnWidth, RowHeight, color);
    }
```

6. In `DrawGridPopupPlain` and `DrawScrollablePopupPlain`, replace each pair

```csharp
        ctx.DrawFill(0f, outerTop, OuterW, OuterH, PlainBackgroundColor);
        ctx.DrawRectOutline(0f, outerTop, OuterW, OuterH, PlainBorderColor, 1f);
```

with `DrawPlainPopupSurface(ctx, outerTop);`. Replace each `ctx.DrawFill(x, y, ColumnWidth, RowHeight, PlainSelectedColor);` / `PlainHoverColor` (grid) and `ctx.DrawFill(inX, y, ColumnWidth, RowHeight, ...)` (scrollable) with `DrawPlainRowHighlight(ctx, <same x>, <same y>, <same colour>);`. That's four replacements.

7. Replace `DrawPopupScrollbarPlain` with:

```csharp
    private void DrawPopupScrollbarPlain(UiRenderContext ctx, float x, float y)
    {
        if (!IsPopupScrollbarPresentationVisible) return;

        if (ThemePalette is null)
        {
            ctx.DrawFill(x, y, ScrollbarWidth, InteriorH, PlainBackgroundColor);
            ctx.DrawRectOutline(x, y, ScrollbarWidth, InteriorH, PlainBorderColor, 1f);
        }

        if (!PopupScroll.HasOverflow) return;

        float decExtent = System.Math.Clamp(ScrollButtonExtent, 0f, InteriorH);
        float incExtent = System.Math.Clamp(ScrollButtonExtent, 0f, InteriorH - decExtent);
        float trackTop = decExtent;
        float trackLen = MathF.Max(0f, InteriorH - decExtent - incExtent);
        var (ty, th) = UiScrollbar.ThumbRect(PopupScroll, trackTop, trackLen);
        if (ThemePalette is { } palette)
            PluginUiStyle.ScrollThumb(ctx, palette, x, y + ty, ScrollbarWidth, th, _draggingPopupThumb);
        else
            ctx.DrawFill(x + 1f, y + ty, MathF.Max(0f, ScrollbarWidth - 2f), th, PlainBorderColor);
    }
```

- [ ] **Step 4: Wire the palette** — in `PluginMarkupTheme.cs`:
- In the `UiField` case's action, add `field.ThemePalette = p;`.
- In the `UiMenu` case's action, add `menu.ThemePalette = p;`.

- [ ] **Step 5: Run tests** (Verify command) → all PASS.

- [ ] **Step 6: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/UiField.cs src/AcDream.App/UI/UiMenu.cs src/AcDream.App/UI/PluginMarkupTheme.cs tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs
git commit -m "ui: themed fields and menus, with focus glow and a shadowed popup"
```

```json:metadata
{"files": ["src/AcDream.App/UI/UiField.cs", "src/AcDream.App/UI/UiMenu.cs", "src/AcDream.App/UI/PluginMarkupTheme.cs", "tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~PluginThemeControlTests|FullyQualifiedName~UiMenu|FullyQualifiedName~UiField|FullyQualifiedName~PluginUiThemeTests|FullyQualifiedName~PluginThemeClassicIdentityTests\"", "acceptanceCriteria": ["field Border blurred, Accent + glow focused", "menu face hover colour, Accent edge open, chevron", "popup shadow + rounded surface + inset rows, themed search, slim thumb", "Classic plain menu unchanged, UiMenu tests pass"], "modelTier": "standard"}
```

---

### Task 7: Lists, logs, sliders and meters

**Goal:** Themed lists and logs sit in rounded containers with inset rows and slim scroll thumbs. Themed sliders get a track, a filled part and a round thumb. Themed meters without sprite art are rounded.

**Files:**
- Modify: `src/AcDream.App/UI/UiMarkupList.cs` (both container draws, both selection bands, `DrawScrollbar`)
- Modify: `src/AcDream.App/UI/UiMarkupLog.cs` (`OnDraw`)
- Modify: `src/AcDream.App/UI/UiScrollbar.cs` (`ThemePalette`, `DrawPlainScalar`)
- Modify: `src/AcDream.App/UI/UiMeter.cs` (`ThemePalette`, the no-sprite branch of `OnDraw`)
- Modify: `src/AcDream.App/UI/PluginMarkupTheme.cs` (scrollbar case; new meter case)
- Test: `tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs` (add cases)

**Acceptance Criteria:**
- [ ] A themed list draws inset rounded selection rows (x ≥ 3) and, when it overflows, a thumb at most 4 wide with no `Muted` arrow ticks.
- [ ] A themed log draws a rounded container, and its scrollbar is a slim thumb.
- [ ] A themed slider draws its `Accent` fill and a `Text` thumb centred on the scrollbar's hit-test thumb. Dragging and clicking behave as before.
- [ ] A themed meter with no sprite art draws a rounded `Field` track and a rounded bar in its authored colour. A meter with sprite art is unchanged.
- [ ] Classic guard, `UiMarkupList*Tests`, `UiScrollbarTests`, `UiMeter*` and `MarkupListColumnsTests` pass.

**Verify:** `dotnet test ... --filter "FullyQualifiedName~PluginThemeControlTests|FullyQualifiedName~UiMarkupList|FullyQualifiedName~UiScrollbar|FullyQualifiedName~UiMeter|FullyQualifiedName~MarkupListColumnsTests|FullyQualifiedName~UiMarkupLog|FullyQualifiedName~PluginThemeClassicIdentityTests"` → all pass

**Steps:**

- [ ] **Step 1: Add failing tests** to `PluginThemeControlTests`:

```csharp
    [Fact]
    public void AThemedListHasInsetRowsAndASlimThumb()
    {
        var (_, panel) = Themed("<list x=\"0\" y=\"0\" w=\"200\" h=\"54\" items=\"{Items}\" selected=\"{Index}\" selectionband=\"true\" />");
        var list = Assert.IsType<UiMarkupList>(panel.Children[0]);
        var v = DrawElement(list);
        var band = v.Where(p => p.Color.W > 0.99f && Vector4.Distance(p.Color, P.Selected) < 0.01f).ToList();
        Assert.NotEmpty(band);
        Assert.True(band.Min(p => p.Position.X) >= PluginUiStyle.RowInset - 0.51f);
        var thumb = v.Where(p => p.Color.W > 0.99f && Vector4.Distance(p.Color, P.Muted) < 0.01f).ToList();
        Assert.NotEmpty(thumb);
        Assert.True(thumb.Max(p => p.Position.X) - thumb.Min(p => p.Position.X) <= PluginUiStyle.ScrollThumbWidth + 1.01f);
    }

    [Fact]
    public void AThemedSliderHasAFillAndARoundThumb()
    {
        var (root, panel) = Themed("<slider x=\"0\" y=\"0\" w=\"200\" h=\"16\" value=\"{Value}\" onchange=\"{Changed}\" />");
        var slider = Assert.IsType<UiScrollbar>(panel.Children[0]);
        root.Tick(0.016, 2);
        var v = DrawElement(slider);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Accent));
        Assert.True(ThemeDrawCapture.HasColor(v, P.Text));
    }

    [Fact]
    public void AThemedMeterIsRoundedInItsOwnColour()
    {
        var (_, panel) = Themed("<meter x=\"0\" y=\"0\" w=\"200\" h=\"12\" fill=\"{Fill}\" color=\"#FFCC3333\" />");
        var meter = Assert.IsType<UiMeter>(panel.Children[0]);
        Assert.Same(P, meter.ThemePalette);
        var v = DrawElement(meter);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Field));
        Assert.True(ThemeDrawCapture.HasColor(v, new Vector4(0xCC / 255f, 0x33 / 255f, 0x33 / 255f, 1f)));
        Assert.True(v.Count > 12);  // rounded, not two rectangles
    }

    [Fact]
    public void AThemedLogSitsInARoundedContainer()
    {
        var (root, panel) = Themed("<log x=\"0\" y=\"0\" w=\"200\" h=\"80\" items=\"{Log}\" />");
        root.Tick(0.016, 2);
        var v = DrawElement(panel.Children[0]);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Field));
        Assert.True(v.Count > 12);
    }
```

Run → compile failure (`UiMeter.ThemePalette`).

Also add the live-switch test the spec asks for:

```csharp
    [Fact]
    public void SwitchingToClassicClearsEveryControlsPalette()
    {
        var settings = new PluginUiThemeSettings { Theme = PluginUiTheme.Brass };
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"400\" h=\"400\" theme=\"plugin\">{PluginThemeClassicIdentityTests.Body}</panel>",
            new PluginThemeClassicIdentityTests.Binding(), _ => (0u, 0, 0), themes: settings);
        var root = new UiRoot { Width = 600, Height = 600 };
        root.AddChild(panel);
        root.Tick(0.016, 1);
        PluginUiPalette?[] Palettes() =>
        [
            panel.Children.OfType<UiSimpleButton>().First().ThemePalette,
            panel.Children.OfType<UiMarkupTabButton>().First().ThemePalette,
            panel.Children.OfType<UiMarkupToggle>().First().ThemePalette,
            panel.Children.OfType<UiField>().First().ThemePalette,
            panel.Children.OfType<UiMenu>().First().ThemePalette,
            panel.Children.OfType<UiScrollbar>().First().ThemePalette,
            panel.Children.OfType<UiMarkupList>().First().ThemePalette,
            panel.Children.OfType<UiMeter>().First().ThemePalette,
        ];
        Assert.All(Palettes(), p => Assert.Same(PluginUiPalette.Brass, p));
        settings.Theme = PluginUiTheme.Classic;
        root.Tick(0.016, 2);
        Assert.All(Palettes(), Assert.Null);
    }
```

This uses the guard test's binding, so in `PluginThemeClassicIdentityTests.cs` change `private sealed class Binding` to `internal sealed class Binding`.

- [ ] **Step 2: List**

In `src/AcDream.App/UI/UiMarkupList.cs`, add two helpers to the class:

```csharp
    private void DrawContainer(UiRenderContext context)
    {
        if (ThemePalette is not null)
        {
            PluginUiStyle.Surface(context, 0f, 0f, Width, Height, PluginUiStyle.ContainerRadius,
                BackgroundColor, BorderColor);
            return;
        }
        context.DrawFill(0f, 0f, Width, Height, BackgroundColor);
        context.DrawRectOutline(0f, 0f, Width, Height, BorderColor, 1f);
    }

    private void DrawSelectionBand(UiRenderContext context, float y, float contentWidth)
    {
        if (ThemePalette is not null)
            PluginUiStyle.Row(context, 0f, y, contentWidth, RowHeight, SelectedColor);
        else
            context.DrawFill(1f, y + 1f, contentWidth - 2f, RowHeight - 1f, SelectedColor);
    }
```

In both draw paths (about lines 105 and 284), replace the `DrawFill` + `DrawRectOutline` pair with `DrawContainer(context);`. Replace each `context.DrawFill(1f, y + 1f, contentWidth - 2f, RowHeight - 1f, SelectedColor);` with `DrawSelectionBand(context, y, contentWidth);`.

In `DrawScrollbar`, replace the `if (ThemePalette is { } p) { ... return; }` block with:

```csharp
        if (ThemePalette is { } p)
        {
            float button = Math.Min(ScrollButtonExtent, Height / 2);
            var (top, size) = UiScrollbar.ThumbRect(_scroll, button, Math.Max(0, Height - 2 * button));
            PluginUiStyle.ScrollThumb(ctx, p, x, top, ScrollbarWidth, size, active: false);
            return;
        }
```

- [ ] **Step 3: Scrollbar (slider and log bar)**

In `src/AcDream.App/UI/UiScrollbar.cs`, after `PlainNubColor` add:

```csharp
    /// <summary>The shared plugin theme the plain look draws in; null draws Classic plain.</summary>
    public PluginUiPalette? ThemePalette { get; set; }
```

Insert at the top of `DrawPlainScalar`:

```csharp
        if (ThemePalette is { } palette)
        {
            DrawThemedPlain(ctx, palette);
            return;
        }
```

and add:

```csharp
    /// <summary>
    /// The themed plain look. A slider's thumb is centred on the thumb its
    /// hit test uses, so it is dragged where it is drawn; a bar that follows
    /// a scroll model draws its thumb where the model's hit test puts it.
    /// </summary>
    private void DrawThemedPlain(UiRenderContext ctx, PluginUiPalette palette)
    {
        bool active = _draggingThumb || _hoveredThumb;
        if (Horizontal)
        {
            float thumbWidth = ScalarThumbWidth(SpriteResolve);
            float thumbX = MathF.Max(0f, Width - thumbWidth) * ScalarPosition;
            PluginUiStyle.Slider(ctx, palette, Width, Height, thumbX + thumbWidth * 0.5f, active);
            return;
        }
        if (Model is { } m)
        {
            float trackTop = AxisExtent(DecrementButtonExtent, Height);
            float trackLen = MathF.Max(0f,
                Height - AxisExtent(DecrementButtonExtent, Height) - AxisExtent(IncrementButtonExtent, Height));
            var (ty, th) = ModelThumbRect(m, trackTop, trackLen);
            PluginUiStyle.ScrollThumb(ctx, palette, 0f, ty, Width, th, active);
            return;
        }
        float thumbHeight = ScalarThumbExtent(SpriteResolve, Height);
        float thumbY = MathF.Max(0f, Height - thumbHeight) * ScalarPosition;
        PluginUiStyle.ScrollThumb(ctx, palette, 0f, thumbY, Width, thumbHeight, active);
    }
```

If `ScalarThumbWidth`/`ScalarThumbExtent` aren't callable with a null `SpriteResolve`, pass `SpriteResolve` exactly as `ThumbAt` does (it already passes the nullable).

In `PluginMarkupTheme.cs`, `case UiScrollbar scroll:` action: add `scroll.ThemePalette = p;`.

- [ ] **Step 4: Log**

In `src/AcDream.App/UI/UiMarkupLog.cs` `OnDraw`, replace

```csharp
        ctx.DrawFill(0, 0, Width, Height, p?.Field ?? BackgroundColor);
        ctx.DrawRectOutline(0, 0, Width, Height, p?.Border ?? BorderColor, 1);
```

with

```csharp
        if (p is not null)
            PluginUiStyle.Surface(ctx, 0, 0, Width, Height, PluginUiStyle.ContainerRadius, p.Field, p.Border);
        else
        {
            ctx.DrawFill(0, 0, Width, Height, BackgroundColor);
            ctx.DrawRectOutline(0, 0, Width, Height, BorderColor, 1);
        }
        _bar.ThemePalette = p;
```

- [ ] **Step 5: Meter**

In `src/AcDream.App/UI/UiMeter.cs`, after `BarColorSource` add:

```csharp
    /// <summary>The shared plugin theme a meter without sprite art draws in; null draws Classic.</summary>
    public PluginUiPalette? ThemePalette { get; set; }
```

In `OnDraw`'s `else` branch (no sprite art), replace

```csharp
            ctx.DrawRect(0, 0, Width, Height, BgColor);
            if (pct is not null && p > 0f)
            {
                var (fx, fy, fw, fh) = ComputeFillRect(p, Width, Height);
                if (fw > 0f)
                    ctx.DrawRect(fx, fy, fw, fh, BarColorSource?.Invoke() ?? BarColor);
            }
```

with

```csharp
            if (ThemePalette is { } palette)
            {
                var fill = pct is not null && p > 0f ? ComputeFillRect(p, Width, Height) : (0f, 0f, 0f, 0f);
                PluginUiStyle.Meter(ctx, palette, Width, Height, fill, BarColorSource?.Invoke() ?? BarColor);
            }
            else
            {
                ctx.DrawRect(0, 0, Width, Height, BgColor);
                if (pct is not null && p > 0f)
                {
                    var (fx, fy, fw, fh) = ComputeFillRect(p, Width, Height);
                    if (fw > 0f)
                        ctx.DrawRect(fx, fy, fw, fh, BarColorSource?.Invoke() ?? BarColor);
                }
            }
```

`ComputeFillRect` returns a 4-tuple. If its element names differ, deconstruct and rebuild `(fx, fy, fw, fh)`.

In `PluginMarkupTheme.cs`, add a case before `case UiScrollbar scroll:`:

```csharp
            case UiMeter meter:
                panel.AddThemeAction(p => meter.ThemePalette = p);
                break;
```

- [ ] **Step 6: Run tests** (Verify command) → all PASS.

- [ ] **Step 7: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/UiMarkupList.cs src/AcDream.App/UI/UiMarkupLog.cs src/AcDream.App/UI/UiScrollbar.cs src/AcDream.App/UI/UiMeter.cs src/AcDream.App/UI/PluginMarkupTheme.cs tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs
git commit -m "ui: themed lists, logs, sliders and meters"
```

```json:metadata
{"files": ["src/AcDream.App/UI/UiMarkupList.cs", "src/AcDream.App/UI/UiMarkupLog.cs", "src/AcDream.App/UI/UiScrollbar.cs", "src/AcDream.App/UI/UiMeter.cs", "src/AcDream.App/UI/PluginMarkupTheme.cs", "tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~PluginThemeControlTests|FullyQualifiedName~UiMarkupList|FullyQualifiedName~UiScrollbar|FullyQualifiedName~UiMeter|FullyQualifiedName~MarkupListColumnsTests|FullyQualifiedName~UiMarkupLog|FullyQualifiedName~PluginThemeClassicIdentityTests\"", "acceptanceCriteria": ["list inset rows and slim thumb", "log rounded container and slim thumb", "slider Accent fill and Text thumb on hit thumb", "meter rounded track/bar without sprites, unchanged with sprites", "Classic guard and existing list/scrollbar/meter tests pass"], "modelTier": "standard"}
```

---

### Task 8: Plugin shelf and minimize button

**Goal:** The themed shelf has a rounded, shadowed surface and rounded entries with hover. The minimize button on themed plugin windows is a ghost button in the header.

**Files:**
- Modify: `src/AcDream.App/UI/PluginSidePanel.cs` (shelf `OnDraw`, `PluginShelfButton.RefreshPresentation`, `PluginMinimizeButton`, the `new PluginMinimizeButton(handle, _font)` call)
- Test: `tests/AcDream.App.Tests/UI/PluginSidePanelThemeTests.cs` (add cases)

**Acceptance Criteria:**
- [ ] Under Moss, shelf entries have `ThemePalette` set to the palette, and under Classic they have null. The existing `CompactShelfRemains24PixelsAndScrollsEveryEntryIntoView` passes unchanged.
- [ ] The themed shelf draws vertices outside its own bounds (shadow).
- [ ] The minimize button on a `UiPluginMarkupPanel` window gets `ThemePalette` under Moss, draws nothing until hovered, and uses `Muted` text. On a window that isn't opted in it keeps today's look: no palette, `Outline` true, white text.

**Verify:** `dotnet test ... --filter "FullyQualifiedName~PluginSidePanel|FullyQualifiedName~PluginThemeClassicIdentityTests"` → all pass

**Steps:**

- [ ] **Step 1: Add failing tests** to `PluginSidePanelThemeTests` (they reuse its `Add` helper; add a second helper that registers a themed markup window):

```csharp
    private static RetailWindowHandle AddThemed(UiRoot root, PluginSidePanel shelf, PluginUiThemeSettings settings, int index)
    {
        var frame = MarkupDocument.Build(
            "<panel x=\"100\" y=\"100\" w=\"200\" h=\"100\" title=\"T\" theme=\"plugin\" />",
            new object(), _ => (0u, 0, 0), themes: settings);
        root.AddChild(frame);
        var handle = root.WindowManager.Register($"plugin:themed:{index}", frame);
        shelf.Add(new PluginUiOwner($"themed.{index}", $"Themed {index}"),
            new PluginPanelDescriptor("main", $"Themed {index}") { IconText = "TT" }, handle);
        return handle;
    }

    [Fact]
    public void ThemedShelfEntriesAndMinimizeFollowThePalette()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var settings = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null, settings);
        root.AddChild(shelf);
        var themed = AddThemed(root, shelf, settings, 0);
        var plain = Add(root, shelf, 1);
        root.Tick(0.016, 16);

        Assert.All(shelf.Children.OfType<PluginSidePanel.PluginShelfButton>(),
            b => Assert.Same(settings.Palette, b.ThemePalette));
        var themedMin = themed.OuterFrame.Children.OfType<UiSimpleButton>().Single(b => b.Text == "–");
        var plainMin = plain.OuterFrame.Children.OfType<UiSimpleButton>().Single(b => b.Text == "–");
        Assert.Same(settings.Palette, themedMin.ThemePalette);
        Assert.Equal(settings.Palette!.Muted, themedMin.TextColor);
        Assert.Null(plainMin.ThemePalette);
        Assert.True(plainMin.Outline);

        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        shelf.DrawSelfAndChildren(ctx);
        Assert.Contains(ThemeDrawCapture.Vertices(renderer), v => v.Position.X < shelf.Left);

        settings.Theme = PluginUiTheme.Classic;
        root.Tick(0.016, 32);
        Assert.All(shelf.Children.OfType<PluginSidePanel.PluginShelfButton>(), b => Assert.Null(b.ThemePalette));
        Assert.Null(themedMin.ThemePalette);
        Assert.Equal(Vector4.One, themedMin.TextColor);
    }
```

Run → fails.

- [ ] **Step 2: Shelf surface**

In `src/AcDream.App/UI/PluginSidePanel.cs`, add to `PluginSidePanel`:

```csharp
    protected override void OnDraw(UiRenderContext ctx)
    {
        if (_themes?.Palette is not { } p) { base.OnDraw(ctx); return; }
        PluginUiStyle.ShelfShadow(ctx, Width, Height);
        PluginUiStyle.Surface(ctx, 0f, 0f, Width, Height, PluginUiStyle.WindowRadius, p.Background, p.Border);
    }
```

If `PluginSidePanel` or `UiPanel` already overrides `OnDraw` as sealed, or the class has its own `OnDraw`, fold this in at its start instead.

- [ ] **Step 3: Shelf entries**

In `PluginShelfButton.RefreshPresentation`, inside `if (theme != PluginUiTheme.Classic) { ... }` add `ThemePalette = palette;` after `PluginUiPalette palette = _themes!.Palette!;`. Add `ThemePalette = null;` as the first line after that block (the Classic path).

- [ ] **Step 4: Minimize button**

Replace `PluginMinimizeButton` with:

```csharp
    private sealed class PluginMinimizeButton : UiSimpleButton
    {
        private readonly RetailWindowHandle _handle;
        private readonly PluginUiThemeSettings? _themes;
        private readonly UiDatFont? _classicFont;

        internal PluginMinimizeButton(RetailWindowHandle handle, UiDatFont? font, PluginUiThemeSettings? themes)
        {
            _handle = handle;
            _themes = themes;
            _classicFont = font;
            Text = "–";
            DatFont = font;
            Outline = true;
            BackgroundColor = new Vector4(0.02f, 0.02f, 0.015f, 0.94f);
            BorderColor = new Vector4(0.58f, 0.46f, 0.17f, 1f);
            BorderThickness = 1f;
            Click += () => _handle.Hide();
        }

        public override string? GetTooltipText() => "Minimize to plugin sidepanel";

        /// <summary>Only windows that opted into the shared theme get the ghost button in their header.</summary>
        protected override void OnTick(double deltaSeconds)
        {
            base.OnTick(deltaSeconds);
            ThemePalette = _handle.OuterFrame is UiPluginMarkupPanel ? _themes?.Palette : null;
            Outline = ThemePalette is null;
            TextColor = ThemePalette?.Muted ?? Vector4.One;
            DatFont = ThemePalette is null ? _classicFont : _themes?.ModernFont ?? _classicFont;
        }

        private protected override void DrawThemedFace(UiRenderContext ctx, PluginUiPalette palette) =>
            PluginUiStyle.GhostButton(ctx, palette, Width, Height, ThemeState);
    }
```

Change the creation site to `new PluginMinimizeButton(handle, _font, _themes)`.

- [ ] **Step 5: Run tests** (Verify command) → all PASS.

- [ ] **Step 6: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/PluginSidePanel.cs tests/AcDream.App.Tests/UI/PluginSidePanelThemeTests.cs
git commit -m "ui: themed plugin shelf and a ghost minimize button on themed windows"
```

```json:metadata
{"files": ["src/AcDream.App/UI/PluginSidePanel.cs", "tests/AcDream.App.Tests/UI/PluginSidePanelThemeTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~PluginSidePanel|FullyQualifiedName~PluginThemeClassicIdentityTests\"", "acceptanceCriteria": ["shelf entries ThemePalette follows theme; existing compact shelf test unchanged", "themed shelf draws shadow outside bounds", "minimize ghost+Muted on themed windows, classic look on non-opted-in windows and under Classic"], "modelTier": "standard"}
```

---

### Task 9: Theme gallery sample and docs

**Goal:** A markup-only sample plugin shows every control in one themed window. The markup docs describe the modern look.

**Files:**
- Create: `samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj`
- Create: `samples/AcDream.Plugins.ThemeGallery/plugin.json`
- Create: `samples/AcDream.Plugins.ThemeGallery/ThemeGalleryPlugin.cs`
- Create: `samples/AcDream.Plugins.ThemeGallery/gallery.xml`
- Create: `samples/AcDream.Plugins.ThemeGallery/packages.neutral.lock.json` (generated by restore)
- Modify: `AcDream.slnx` (add the project next to CanvasDemo, line 19)
- Modify: `docs/plugin-ui-markup.md` ("Shared plugin appearance", lines 38–69)

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 warnings, 0 errors, including the new sample.
- [ ] The sample registers one window, `gallery.xml`, with `theme="plugin"`, a title, and at least one of each: tab, label, field, menu (searchable), toggle (on and off), slider, list with check and text columns, log, meter, button (enabled and disabled).
- [ ] `docs/plugin-ui-markup.md` describes rounded controls, hover/press/focus, switches, the header band, sharp text on high-density displays, the 18pt wider toggle caption offset, and the gallery sample.

**Verify:** `dotnet build AcDream.slnx -c Release 2>&1 | tail -3` → `0 Warning(s)` / `0 Error(s)`

**Steps:**

- [ ] **Step 1: Project files**

`samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj`: copy `samples/AcDream.Plugins.CanvasDemo/AcDream.Plugins.CanvasDemo.csproj` exactly, and add `<None Include="gallery.xml" CopyToOutputDirectory="PreserveNewest" />` to the ItemGroup that holds `plugin.json`.

`samples/AcDream.Plugins.ThemeGallery/plugin.json`:

```json
{
  "id": "sample.theme-gallery",
  "displayName": "Theme Gallery Sample",
  "version": "1.0.0",
  "entryDll": "AcDream.Plugins.ThemeGallery.dll",
  "apiVersion": 1,
  "kinds": ["gameplay"],
  "hosts": ["graphical"]
}
```

- [ ] **Step 2: Markup**

`samples/AcDream.Plugins.ThemeGallery/gallery.xml`:

```xml
<panel x="120" y="120" w="420" h="430" title="Theme Gallery" theme="plugin" resizable="true" minw="420" minh="430">
  <tab x="12" y="32" w="90" h="24" text="Status" selected="{StatusTab}" onclick="{ShowStatus}" />
  <tab x="106" y="32" w="90" h="24" text="Spells" selected="{SpellsTab}" onclick="{ShowSpells}" />
  <tab x="200" y="32" w="90" h="24" text="Settings" selected="{SettingsTab}" onclick="{ShowSettings}" />

  <label x="12" y="70" text="Target" color="theme:muted|#FF9AA99E" />
  <field x="90" y="66" w="318" h="24" text="{Target}" onchange="{SetTarget}" />

  <label x="12" y="102" text="Profile" color="theme:muted|#FF9AA99E" />
  <menu x="90" y="98" w="318" h="24" items="{Profiles}" selected="{Profile}" onchange="{SetProfile}" searchable="true" rows="5" />

  <toggle x="12" y="132" w="200" h="20" text="Auto-rebuff" checked="{AutoRebuff}" onclick="{ToggleAutoRebuff}" />
  <toggle x="212" y="132" w="200" h="20" text="Announce in chat" checked="{Announce}" onclick="{ToggleAnnounce}" />

  <label x="12" y="164" text="Threshold" color="theme:muted|#FF9AA99E" />
  <slider x="90" y="162" w="318" h="16" value="{Threshold}" onchange="{SetThreshold}" min="0" max="100" />

  <list x="12" y="190" w="396" h="96" selected="{Spell}" onchange="{SelectSpell}" selectionband="true">
    <column type="check" width="22" values="{SpellEnabled}" onchange="{ToggleSpell}" />
    <column type="text" width="*" items="{Spells}" />
  </list>

  <log x="12" y="294" w="396" h="72" items="{Log}" />

  <meter x="12" y="374" w="250" h="12" fill="{Mana}" color="#FF4A7FD0" />

  <button x="228" y="394" w="86" h="26" text="Stop" onclick="{Stop}" enabled="{Running}" anchor="right bottom" />
  <button x="322" y="394" w="86" h="26" text="Start" onclick="{Start}" anchor="right bottom" />
</panel>
```

- [ ] **Step 3: Plugin**

`samples/AcDream.Plugins.ThemeGallery/ThemeGalleryPlugin.cs`:

```csharp
using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.ThemeGallery;

/// <summary>
/// One window with every markup control, opted into the shared plugin
/// theme. It exists to be looked at -- switch between Classic, Moss and
/// Brass from the plugin shelf's right-click menu -- and does nothing else.
/// </summary>
public sealed class ThemeGalleryPlugin : IAcDreamPlugin
{
    private IPluginHost? _host;
    private IDisposable? _window;
    private readonly GalleryBinding _binding = new();

    public void Initialize(IPluginHost host) => _host = host;

    public void Enable()
    {
        if (_host is not { } host) return;
        string markup = Path.Combine(Path.GetDirectoryName(typeof(ThemeGalleryPlugin).Assembly.Location)!, "gallery.xml");
        _window = host.Ui.RegisterPanel(
            new PluginPanelDescriptor("gallery", "Theme Gallery") { IconText = "TG", StartVisible = true },
            markup, _binding);
    }

    public void Disable()
    {
        _window?.Dispose();
        _window = null;
    }

    /// <summary>Plain properties the markup binds to; every action only changes what is shown.</summary>
    public sealed class GalleryBinding
    {
        private int _tab;
        private readonly List<string> _log = ["Gallery opened."];
        private readonly bool[] _spellEnabled = [true, true, false, true];

        public bool StatusTab => _tab == 0;
        public bool SpellsTab => _tab == 1;
        public bool SettingsTab => _tab == 2;
        public Action ShowStatus => () => _tab = 0;
        public Action ShowSpells => () => _tab = 1;
        public Action ShowSettings => () => _tab = 2;

        public string Target { get; private set; } = "Asheron";
        public Action<string> SetTarget => value => Target = value;

        public IReadOnlyList<string> Profiles { get; } = ["Mage", "Melee", "Archer", "Tinker", "Support", "Lifestone runner"];
        public string Profile { get; private set; } = "Mage";
        public Action<string> SetProfile => value => { Profile = value; Write($"Profile: {value}"); };

        public bool AutoRebuff { get; private set; } = true;
        public Action ToggleAutoRebuff => () => AutoRebuff = !AutoRebuff;
        public bool Announce { get; private set; }
        public Action ToggleAnnounce => () => Announce = !Announce;

        public float Threshold { get; private set; } = 60f;
        public Action<float> SetThreshold => value => Threshold = value;

        public IReadOnlyList<string> Spells { get; } =
            ["Strength Self VII", "Invulnerability Self VII", "Impregnability Self VII", "Focus Self VII"];
        public IReadOnlyList<bool> SpellEnabled => _spellEnabled;
        public Action<int> ToggleSpell => index => { if (index >= 0 && index < _spellEnabled.Length) _spellEnabled[index] = !_spellEnabled[index]; };
        public int Spell { get; private set; } = 1;
        public Action<int> SelectSpell => index => Spell = index;

        public IReadOnlyList<string> Log { get; private set; } = ["Gallery opened."];
        public float Mana => 0.62f;

        public bool Running { get; private set; }
        public Action Start => () => { Running = true; Write("Started."); };
        public Action Stop => () => { Running = false; Write("Stopped."); };

        private void Write(string line)
        {
            _log.Add(line);
            Log = _log.ToArray();
        }
    }
}
```

Check `IAcDreamPlugin` (in `src/AcDream.Plugin.Abstractions`) for any other members it requires, and copy their empty bodies from `CanvasDemoPlugin`. If `RegisterPanel`'s second parameter is a full path, the path above is correct; otherwise follow `docs/plugin-ui-markup.md` "Registering a panel".

- [ ] **Step 4: Solution and lock file**

In `AcDream.slnx`, after the CanvasDemo line add:

```xml
    <Project Path="samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj" />
```

Then:

```bash
dotnet restore samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
```

Expected: 0 warnings, 0 errors. Keep the new sample's `packages.neutral.lock.json`, and revert every other lock file with `git checkout -- $(git diff --name-only -- '*.lock.json')`. If the new lock file has an osx-arm64 RID section that CanvasDemo's doesn't, make it match CanvasDemo's shape.

- [ ] **Step 5: Docs**

In `docs/plugin-ui-markup.md`, replace the paragraph that starts "Opted-in windows keep their authored layout" with:

```markdown
Opted-in windows keep their authored layout. In the modern themes they draw
in a softer style: the window has rounded corners, a soft shadow and a header
band behind its title, set in Noto Sans SemiBold; buttons, fields, menus,
lists and logs are rounded, and buttons, tabs and menus react to the pointer
and show an accent ring when focused. Tabs are pills and toggles are switches.
A themed toggle's caption starts 18 points further right than in Classic, so
give toggles room for it. On high-density displays themed text is drawn from
a sharper bake of the font; measurements, and so layout, are the same on
every display.

Literal and bound semantic colours remain unchanged. To adapt a decorative
colour, use a token with its original Classic fallback, for example
`color="theme:text|#FFE8DEC3"` or `background="theme:field|#FF0C0906"`.
Tokens are `text`, `muted`, `field`, `border`, `accent`, and `background`;
the fallback uses `#AARRGGBB`.

The `samples/AcDream.Plugins.ThemeGallery` plugin shows every control in one
opted-in window; switch themes from the shelf to compare them.
```

- [ ] **Step 6: Commit**

```bash
git add samples/AcDream.Plugins.ThemeGallery AcDream.slnx docs/plugin-ui-markup.md
git commit -m "samples: a theme gallery plugin, and docs for the modern plugin look"
```

```json:metadata
{"files": ["samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj", "samples/AcDream.Plugins.ThemeGallery/plugin.json", "samples/AcDream.Plugins.ThemeGallery/ThemeGalleryPlugin.cs", "samples/AcDream.Plugins.ThemeGallery/gallery.xml", "samples/AcDream.Plugins.ThemeGallery/packages.neutral.lock.json", "AcDream.slnx", "docs/plugin-ui-markup.md"], "verifyCommand": "dotnet build AcDream.slnx -c Release", "acceptanceCriteria": ["solution builds with 0 warnings 0 errors incl. sample", "gallery.xml has every control, themed, titled", "docs describe the modern look, toggle offset, sharp text, gallery sample"], "modelTier": "standard"}
```

---

### Task 10: Look at it — captures at 1× and 2×

**Goal:** Screenshots of the gallery window and the Plugin appearance dialog in Classic, Moss and Brass at 1× and 2×, compared against the chosen mockup ("A · Soft modern" with "C" switches). Any visual defects get fixed.

**Files:**
- Modify (only if a defect is found): files from Tasks 1–8, with tests updated alongside
- Output (not committed): screenshots in the session scratchpad

**Acceptance Criteria:**
- [ ] Six gallery captures (3 themes × 1×/2×) and two Plugin appearance captures (Moss 1×/2×) exist.
- [ ] Classic captures show today's look (no rounded corners, no header band).
- [ ] Moss and Brass captures show rounded windows with shadows, header band, SemiBold title, pill tabs, switches, rounded fields/menus/lists, slim thumbs. The 2× text is visibly sharper than the 1× text magnified.
- [ ] Known risks were checked: toggle captions in the gallery aren't clipped, and the slider thumb drags where it's drawn.
- [ ] The user was shown the captures and said whether they match the mockup well enough.

**Verify:** captures listed in the scratchpad; the user's verdict is recorded in the coordinator's notes.

**Steps:**

- [ ] **Step 1:** Build the client (`dotnet build AcDream.slnx -c Release`). Make a scratch `ACDREAM_ROOT_DIR` whose `plugins/` holds only the built ThemeGallery sample.
- [ ] **Step 2:** Follow the retina capture workflow from memory (`retina-capture-workflow`). Run the client offline with `ACDREAM_UI_PROBE_SCRIPT` (`sleep 45000` / `screenshot <name>` / `close-client`) and `ACDREAM_AUTOMATION_ARTIFACT_DIR`, from a temp working directory. For 2×, patch `FrameScreenshotController.CapturePending` to use `d.Window.FramebufferSize` **in a throwaway worktree only, never committed**, and ask the user to drag the window onto the built-in display during the sleep, giving a countdown.
- [ ] **Step 3:** Switch theme between captures through the Plugin appearance dialog. If the probe script can't click, set the theme in the settings file before launch: the key `SettingsStore.SavePluginUiTheme` writes.
- [ ] **Step 4:** Compare against the mockup. Fix defects with a failing test first wherever the defect can be expressed as one, and commit each fix separately (`fix: ...`).
- [ ] **Step 5:** Send the captures to the user (SendUserFile) and ask for their verdict.

```json:metadata
{"files": [], "verifyCommand": "", "acceptanceCriteria": ["8 captures exist (gallery 3 themes x 1x/2x, appearance Moss 1x/2x)", "Classic captures show today's look", "Moss/Brass show the modern look, 2x text sharper", "toggle captions not clipped, slider drags where drawn", "user verdict recorded"], "modelTier": "frontier"}
```

---

### Task 11: Final verification and merge

**Goal:** The branch is fully tested and reviewed, then pushed and merged into fork `main` with a merge commit, once the user agrees.

**Files:** none new.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 warnings, 0 errors.
- [ ] `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → no failures beyond the known flaky tests in the local env memory, each rerun alone and passing.
- [ ] No `*.lock.json` changes other than the new sample's. No `docs/superpowers/` files on the branch.
- [ ] The user approved pushing `modern-theme/controls` to `origin` and merging it into fork `main` (merge commit, as for painter-v2).

**Verify:** `git log --oneline main..modern-theme/controls` lists the task commits. `git diff --stat main...modern-theme/controls -- docs/superpowers` is empty.

**Steps:**

- [ ] **Step 1:** Run the full build and App test suite. Rerun any failing known-flaky test classes alone.
- [ ] **Step 2:** Request a final code review (superpowers-extended-cc:requesting-code-review) over `main...modern-theme/controls`.
- [ ] **Step 3:** Ask the user before pushing or merging. On approval: `git push -u origin modern-theme/controls`, then in the main checkout run `git merge --no-ff modern-theme/controls` and push `main`.
- [ ] **Step 4:** Update the `modern-plugin-theme` memory with branch, merge commit and status.

```json:metadata
{"files": [], "verifyCommand": "dotnet build AcDream.slnx -c Release && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj", "acceptanceCriteria": ["0 warnings 0 errors", "App tests pass except known flaky (rerun alone)", "no stray lock files, no docs/superpowers on branch", "user approved push+merge"], "modelTier": "standard"}
```
