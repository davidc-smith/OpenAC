# Painter v2 — PR 1: Fonts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plugins draw canvas text in the bundled Noto Sans at a size they choose, or in a `.ttf`/`.otf` they ship, through `IPluginFonts` handles, with `MeasureText` in step and a per-plugin budget.

**Architecture:** A new contract surface `IPluginFonts` (like `IPluginImages`) hands out `PluginFont` handles. The graphical host bakes each (font, size, characters) once, at request time, into a single-channel coverage atlas with StbTrueType's pack API, uploads it as a releasable `R8Unorm` texture, and draws glyphs as coverage sprites through the UI shader's existing coverage branch — in the sprite runs, so painter's order holds. The bundled font's bakes are shared host-wide; plugin fonts are counted against a byte budget. No shader changes.

**Tech Stack:** C# / .NET 10, StbTrueTypeSharp 1.26.12, Vulkan via Silk.NET, xUnit, `RecordingGpuDevice`.

**Spec:** `docs/superpowers/specs/2026-09-30-plugin-painter-v2-design.md` (section 4, and "Plan-time corrections", which supersede section 4 where they differ).

## Global Constraints

- Branch `painter-v2/fonts`. It depends on PR 0 (text edges are translucent): create it from `main` if `painter-v2/canvas-alpha` has merged, otherwise from `painter-v2/canvas-alpha`, and rebase onto `main` after PR 0 merges. Nothing under `docs/superpowers/` goes on a code branch.
- `AcDream.Plugin.Abstractions` stays BCL-only and additive: every new member is a default interface member with an inert answer; every public member has XML docs (undocumented members fail the build).
- Budget defaults, exactly: `MaximumCount` 16, `MaximumBytes` 16 MiB (`16L * 1024 * 1024`), `MaximumGlyphs` 2048, `MinimumPixelSize` 6, `MaximumPixelSize` 64. Atlas at most 2048×2048. Default character ranges exactly U+0020–U+024F, U+0370–U+052F, U+2000–U+206F. At most 65,536 code points scanned per request.
- No shader or SPIR-V changes. `BundledUiFont`'s atlas and metrics are unchanged (it only gains `ReadEmbeddedFontBytes`).
- Fonts are requested and released on the interface thread only, like images; baking never runs inside a paint callback.
- Commit subjects: `plugin api: …`, `plugin fonts: …`, `plugin canvas: …`, `gpu: …`, `docs: …`, `tests: …`. Every commit message ends with `Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB`.
- Environment for every command: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`.
- Tests run in the machine's culture unless `ACDREAM_TEST_CULTURE` is set (this Mac uses a decimal comma): any reason text a test asserts on is formatted with `CultureInfo.InvariantCulture`.
- Portable gate filter: `Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure`.

**User decisions (already made):**
- Font API: "Handle per face+size" — `PluginFont` handles from `IPluginFonts.Bundled(size)` / `FromStream(name, open, size, options)`, passed to `DrawText`/`MeasureText`; baking at request time.
- Glyphs: "Optional ranges, capped" — `PluginFontOptions.Ranges`, default = the bundled ranges, capped glyph count.
- Budget numbers in the spec's Section 2 were approved ("Yes"); the pixel-size ceiling moved from 72 to 64 at plan time (measured atlas sizes, recorded in the spec's Plan-time corrections).
- "One spec, a PR series"; "Keep it on our repo only"; do not open a PR without asking which repository.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AcDream.Plugin.Abstractions/PluginFonts.cs` | Create | `PluginFont`, `PluginCodepointRange`, `PluginFontOptions`, `IPluginFonts`, `NoOpPluginFonts`. |
| `src/AcDream.Plugin.Abstractions/IUiRegistry.cs` | Modify | `IUiRegistry.Fonts`, `IScopedUiRegistry.FontsFor`. |
| `src/AcDream.Plugin.Abstractions/PluginCanvas.cs` | Modify | Font-aware `DrawText`/`MeasureText` default members; painter doc. |
| `src/AcDream.Core/Plugins/ScopedPluginHost.cs` | Modify | `ScopedUiRegistry.Fonts`, asked once and tracked. |
| `src/AcDream.App/Rendering/TextureCache.cs` | Modify | `UploadReleasableCoverage8`; byte accounting per format. |
| `src/AcDream.App/Rendering/TextRenderer.cs` | Modify | Coverage sprite runs. |
| `src/AcDream.App/UI/UiRenderContext.cs` | Modify | `DrawCoverageSprite`, `DrawStringCanvasFont`. |
| `src/AcDream.App/UI/BundledUiFont.cs` | Modify | `ReadEmbeddedFontBytes` (no other change). |
| `src/AcDream.App/UI/CanvasFontBaker.cs` | Create | Pure bake: bytes + size + ranges → coverage atlas + glyph metrics. |
| `src/AcDream.App/UI/CanvasFont.cs` | Create | A baked font the canvas draws with: glyph lookup, kerning, measuring. |
| `src/AcDream.App/Plugins/PluginFontTable.cs` | Create | One plugin's fonts: budget, dedup, ref counts, thread check, report-once. |
| `src/AcDream.App/Plugins/BundledCanvasFontCache.cs` | Create | The bundled font's bakes shared by every plugin. |
| `src/AcDream.App/Plugins/PluginFonts.cs` | Create | The contract wrapper over one table. |
| `src/AcDream.App/Plugins/BufferedUiRegistry.cs` | Modify | Font tables per plugin; bind/unbind font services. |
| `src/AcDream.App/UI/RetailPluginFontBackend.cs` | Create | Uploads/releases atlases through the texture cache. |
| `src/AcDream.App/UI/RetailUiRuntime.cs` | Modify | Bind/unbind font services; canvases get fonts. |
| `src/AcDream.App/UI/Layout/PluginPainter.cs` | Modify | Font-aware text. |
| `src/AcDream.App/UI/Layout/PluginCanvasSurface.cs` | Modify | Pass fonts to the painter. |
| `src/AcDream.App/UI/Layout/PluginCanvasElement.cs` | Modify | Fonts source. |
| `tests/Fixtures/fonts/NotoSansCffFixture.otf`, `OFL.txt`, `README.md` | Create | 1.4 KB CFF test font (OFL, derived from the repo's Noto Sans). |
| `tests/AcDream.App.Tests/AcDream.App.Tests.csproj` | Modify | Embed the fixture. |
| Tests (listed per task) | Create/Modify | |
| `docs/plugin-api.md`, `docs/plugin-ui-markup.md` | Modify | Fonts section; test list. |

---

### Task 0: Create the branch

**Goal:** A code branch in the right place.

**Files:** none

**Acceptance Criteria:**
- [ ] `git branch --show-current` prints `painter-v2/fonts`
- [ ] The branch contains PR 0's commits (merged into `main`, or as its base)
- [ ] `docs/superpowers` does not exist in the working tree

**Verify:** `git branch --show-current && git log --oneline -20 | grep -c "premultiplied" && test ! -e docs/superpowers && echo clean`

**Steps:**

- [ ] **Step 1: Branch**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git fetch origin
git switch main && git pull --ff-only origin main
if git log --oneline main | grep -q "shown at the alpha it was painted with"; then
  git switch -c painter-v2/fonts main
else
  git switch -c painter-v2/fonts painter-v2/canvas-alpha
fi
test ! -e docs/superpowers && echo clean
```

Expected: `clean`.

---

### Task 1: The font contract

**Goal:** `IPluginFonts` and its types in the contract, reachable from `IUiRegistry`/`IScopedUiRegistry`, and font-aware painter members whose defaults fall back to the interface font.

**Files:**
- Create: `src/AcDream.Plugin.Abstractions/PluginFonts.cs`
- Modify: `src/AcDream.Plugin.Abstractions/IUiRegistry.cs:174-179` (after `Images`) and `:280-287` (after `ImagesFor`)
- Modify: `src/AcDream.Plugin.Abstractions/PluginCanvas.cs:151-201` (painter doc, after `MeasureText`)
- Test: `tests/AcDream.Plugin.Tests/PluginFontsContractTests.cs` (create)
- Test: `tests/AcDream.Headless.Tests/HeadlessPluginApiSurfaceTests.cs` (add one test after `WithoutAWindowTheImageSurfaceAnswersInertly`)

**Acceptance Criteria:**
- [ ] `PluginFont.None.IsValid` is false; a non-zero handle is valid
- [ ] A registry that never heard of fonts, and `NoOpUiRegistry`, hand out `NoOpPluginFonts.Instance`
- [ ] `NoOpPluginFonts` answers None/false/0 and never opens the stream
- [ ] On a painter that implements only the old members, `DrawText(…, font, outline: true)` calls the old `DrawText(…, outline: true)` and `MeasureText(text, font)` returns the old measure
- [ ] The headless host's `Ui.Fonts` is inert
- [ ] `dotnet build AcDream.slnx -c Release` has 0 warnings (all new public members documented)

**Verify:** `dotnet test tests/AcDream.Plugin.Tests -c Release --filter "FullyQualifiedName~PluginFontsContractTests" && dotnet test tests/AcDream.Headless.Tests -c Release --filter "FullyQualifiedName~FontSurface"` → `Passed!` twice

**Steps:**

- [ ] **Step 1: Write the failing contract tests** (`tests/AcDream.Plugin.Tests/PluginFontsContractTests.cs`)

```csharp
// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;
using AcDream.Plugin.Tests.Fixtures;

namespace AcDream.Plugin.Tests;

/// <summary>
/// The font surface is additive with inert defaults: a host that never heard
/// of it answers every request with no font, and a painter that predates it
/// still draws the text, in the interface font.
/// </summary>
public sealed class PluginFontsContractTests
{
    private sealed class BareRegistry : IUiRegistry
    {
        public void AddMarkupPanel(string markupPath, object binding)
        {
        }
    }

    private sealed class BareScopedRegistry : IScopedUiRegistry
    {
        public void AddMarkupPanel(string markupPath, object binding)
        {
        }

        public IDisposable RegisterMarkupPanel(string markupPath, object binding) =>
            NoOpUiRegistration.Instance;
    }

    /// <summary>A painter written before fonts existed: only the original members.</summary>
    private sealed class OldPainter : IPluginPainter
    {
        public List<(string Text, bool Outline)> Drawn { get; } = [];
        public int Width => 10;
        public int Height => 10;
        public void Clear(PluginColor color) { }
        public void FillRect(PluginRect rect, PluginColor color) { }
        public void StrokeRect(PluginRect rect, PluginColor color, float thickness = 1f) { }
        public void DrawLine(PluginPoint from, PluginPoint to, PluginColor color, float thickness = 1f) { }
        public void DrawText(string text, PluginPoint position, PluginColor color, bool outline = false) =>
            Drawn.Add((text, outline));
        public PluginSize MeasureText(string text) => new(text.Length * 7, 13);
        public void DrawImage(PluginImage image, PluginRect destination, PluginColor tint) { }
        public void DrawImageTransformed(
            PluginImage image, PluginRect destination, PluginColor tint, double rotationRadians,
            PluginPoint pivot, double scaleX = 1.0, double scaleY = 1.0) { }
        public void PushClip(PluginRect rect) { }
        public void PopClip() { }
    }

    [Fact]
    public void NoFontIsNotValid()
    {
        Assert.False(PluginFont.None.IsValid);
        Assert.Equal(0, PluginFont.None.Handle);
        Assert.True(new PluginFont(1, 16f, 22f, 17f).IsValid);
    }

    [Fact]
    public void ARegistryThatNeverHeardOfFontsHandsOutTheInertSurface()
    {
        IUiRegistry registry = new BareRegistry();
        IScopedUiRegistry scoped = new BareScopedRegistry();

        Assert.Same(NoOpPluginFonts.Instance, registry.Fonts);
        Assert.Same(NoOpPluginFonts.Instance, scoped.FontsFor(new PluginUiOwner("p", "P")));
        Assert.Same(NoOpPluginFonts.Instance, ((IUiRegistry)NoOpUiRegistry.Instance).Fonts);
    }

    [Fact]
    public void TheInertSurfaceRefusesEverythingAndHoldsNothing()
    {
        IPluginFonts fonts = NoOpPluginFonts.Instance;
        bool opened = false;

        Assert.False(fonts.IsAvailable);
        Assert.Equal(PluginFont.None, fonts.Bundled(16f));
        Assert.Equal(PluginFont.None, fonts.FromStream("fonts/Inter.ttf", () =>
        {
            opened = true;
            return new MemoryStream();
        }, 16f, new PluginFontOptions { Ranges = [new PluginCodepointRange(0xE000, 0xF8FF)] }));
        Assert.False(opened);
        Assert.False(fonts.Release(new PluginFont(1, 16f, 22f, 17f)));
        Assert.Equal(0, fonts.Count);
        Assert.Equal(0, fonts.MaximumCount);
        Assert.Equal(0L, fonts.MaximumBytes);
        Assert.Equal(0, fonts.MaximumGlyphs);
        Assert.Equal(0f, fonts.MinimumPixelSize);
        Assert.Equal(0f, fonts.MaximumPixelSize);
    }

    [Fact]
    public void APainterThatPredatesFontsDrawsAndMeasuresInItsOwnFont()
    {
        var old = new OldPainter();
        IPluginPainter painter = old;
        var font = new PluginFont(3, 20f, 27f, 21f);

        painter.DrawText("Golem", new PluginPoint(1, 2), PluginColor.White, font, outline: true);
        PluginSize measured = painter.MeasureText("Golem", font);

        Assert.Equal([("Golem", true)], old.Drawn);
        Assert.Equal(new PluginSize(35, 13), measured);
    }

    [Fact]
    public void TheFakeHostAnswersInertly()
    {
        var host = new FakePluginHost();

        Assert.False(host.Ui.Fonts.IsAvailable);
        Assert.Equal(PluginFont.None, host.Ui.Fonts.Bundled(16f));
    }
}
```

- [ ] **Step 2: Write the failing headless test** (in `HeadlessPluginApiSurfaceTests`, after the images test)

```csharp
    /// <summary>
    /// Fonts are asked of the same surface on both hosts. Without a window
    /// there is nothing to draw text on, so every font request answers "no
    /// font", nothing is held, and the plugin's stream is never opened.
    /// </summary>
    [Fact]
    public void WithoutAWindowTheFontSurfaceAnswersInertly()
    {
        using GameRuntime runtime = NewRuntime();
        using var host = NewHost(runtime);
        IPluginHost pluginHost = host;
        bool opened = false;

        IPluginFonts fonts = pluginHost.Ui.Fonts;

        Assert.False(fonts.IsAvailable);
        Assert.Equal(PluginFont.None, fonts.Bundled(16f));
        Assert.Equal(PluginFont.None, fonts.FromStream("fonts/Inter.ttf", () =>
        {
            opened = true;
            return new MemoryStream();
        }, 16f));
        Assert.False(opened);
        Assert.False(fonts.Release(new PluginFont(1, 16f, 22f, 17f)));
        Assert.Equal(0, fonts.Count);
    }
```

- [ ] **Step 3: Run to see them fail**

Run: `dotnet build AcDream.slnx -c Release`
Expected: compile errors — `PluginFont`, `IPluginFonts`, `NoOpPluginFonts`, `Fonts`, `FontsFor` and the font `DrawText` overload do not exist.

- [ ] **Step 4: Create `PluginFonts.cs`**

```csharp
namespace AcDream.Plugin.Abstractions;

/// <summary>
/// A font at one size that the host holds on a plugin's behalf, for drawing
/// canvas text. The handle means nothing outside the host that issued it; the
/// metrics are carried so the plugin can lay text out without asking again.
/// Sizes and metrics are in canvas pixels.
/// </summary>
/// <param name="Handle">The host's number for this font; 0 means no font.</param>
/// <param name="PixelSize">The size the font was asked for, in pixels.</param>
/// <param name="LineHeight">How far apart two lines of this font sit, in pixels.</param>
/// <param name="Ascent">How far below the top of a line its baseline sits, in pixels.</param>
public readonly record struct PluginFont(int Handle, float PixelSize, float LineHeight, float Ascent)
{
    /// <summary>No font: what every refused request returns.</summary>
    public static PluginFont None => default;

    /// <summary>True when the host issued this font and it has not yet been released.</summary>
    public bool IsValid => Handle != 0;
}

/// <summary>An inclusive range of Unicode code points, such as U+0041 to U+005A for A to Z.</summary>
/// <param name="First">The first code point in the range.</param>
/// <param name="Last">The last code point in the range; at least <paramref name="First"/>.</param>
public readonly record struct PluginCodepointRange(int First, int Last);

/// <summary>How a font the plugin ships is prepared.</summary>
public sealed record PluginFontOptions
{
    /// <summary>
    /// The characters to prepare. Null prepares the client's default set:
    /// U+0020–U+024F (Latin), U+0370–U+052F (Greek and Cyrillic) and
    /// U+2000–U+206F (punctuation). Only characters the font actually has
    /// are prepared and counted, so an icon font can name the whole
    /// private-use area, U+E000–U+F8FF.
    /// </summary>
    public IReadOnlyList<PluginCodepointRange>? Ranges { get; init; }
}

/// <summary>
/// Fonts a plugin can draw canvas text with: the client's bundled sans-serif
/// at a chosen size, and TrueType (.ttf) or OpenType (.otf) fonts the plugin
/// ships, prepared by the host. Each size of each font is its own
/// <see cref="PluginFont"/>.
///
/// <para>Every request is counted once per distinct font, size and set of
/// characters, and held as many times as it was asked for: asking twice
/// returns the same font, and it takes two releases to let it go. A plugin
/// may hold at most <see cref="MaximumCount"/> fonts, and its own fonts may
/// take at most <see cref="MaximumBytes"/> of memory, counting the font files
/// it supplied and the glyph textures made from them. A request past either
/// limit, a size outside <see cref="MinimumPixelSize"/> to
/// <see cref="MaximumPixelSize"/>, or a font with more than
/// <see cref="MaximumGlyphs"/> of the requested characters is refused with
/// <see cref="PluginFont.None"/> and reported once in the client's log. The
/// bundled font is shared with every other plugin and costs nothing against
/// the byte budget.</para>
///
/// <para>Preparing a font takes some milliseconds and happens when it is
/// asked for, never while a canvas paints, so ask for fonts up front rather
/// than inside a paint callback. Call this only from the thread the plugin's
/// own callbacks run on; the host refuses any other. On a host without a
/// window, or before the client's interface is up, every request answers
/// <see cref="PluginFont.None"/> and <see cref="IsAvailable"/> is false.
/// Fonts are dropped when the interface is torn down, for example on a
/// reconnect; text drawn with a dropped font draws nothing, and the plugin
/// asks again once it is drawing again.</para>
/// </summary>
public interface IPluginFonts
{
    /// <summary>
    /// Whether requests can currently be answered: false on a host without a
    /// window and until the client's interface is up.
    /// </summary>
    bool IsAvailable => false;

    /// <summary>The client's bundled sans-serif font, Noto Sans, at a size.</summary>
    /// <param name="pixelSize">The size in canvas pixels, from <see cref="MinimumPixelSize"/> to <see cref="MaximumPixelSize"/>.</param>
    /// <returns>The font, or <see cref="PluginFont.None"/> when the request was refused.</returns>
    PluginFont Bundled(float pixelSize) => PluginFont.None;

    /// <summary>
    /// A font the plugin ships, opened through <paramref name="open"/> only
    /// when the host does not already hold it at that size with those
    /// characters. The host reads the stream and disposes it; TrueType and
    /// OpenType (CFF) outlines are accepted, and the plugin never sees pixels.
    /// </summary>
    /// <param name="name">The plugin's own name for the font file, unique within the plugin, such as a relative path.</param>
    /// <param name="open">Opens a fresh readable stream of the font file.</param>
    /// <param name="pixelSize">The size in canvas pixels, from <see cref="MinimumPixelSize"/> to <see cref="MaximumPixelSize"/>.</param>
    /// <param name="options">Which characters to prepare; null prepares the default set.</param>
    /// <returns>The font, or <see cref="PluginFont.None"/> when the stream could not be opened or read, the font has none of the characters, a limit would be passed, or the request was refused.</returns>
    PluginFont FromStream(string name, Func<Stream> open, float pixelSize, PluginFontOptions? options = null) =>
        PluginFont.None;

    /// <summary>
    /// Lets go of one hold on a font. The font stays while another hold
    /// remains and is freed on the last.
    /// </summary>
    /// <param name="font">A font this surface issued.</param>
    /// <returns>False for a font this surface did not issue or has already let go of completely.</returns>
    bool Release(PluginFont font) => false;

    /// <summary>How many distinct fonts the plugin currently holds.</summary>
    int Count => 0;

    /// <summary>The most distinct fonts the plugin may hold at once; 0 on a host that draws nothing.</summary>
    int MaximumCount => 0;

    /// <summary>The most memory, in bytes, the plugin's own fonts may take; 0 on a host that draws nothing.</summary>
    long MaximumBytes => 0;

    /// <summary>The most characters one font may prepare; 0 on a host that draws nothing.</summary>
    int MaximumGlyphs => 0;

    /// <summary>The smallest size a font may be asked for, in pixels; 0 on a host that draws nothing.</summary>
    float MinimumPixelSize => 0f;

    /// <summary>The largest size a font may be asked for, in pixels; 0 on a host that draws nothing.</summary>
    float MaximumPixelSize => 0f;
}

/// <summary>
/// The font surface a host that draws nothing hands out: every request
/// answers <see cref="PluginFont.None"/> and nothing is held.
/// </summary>
public sealed class NoOpPluginFonts : IPluginFonts
{
    /// <summary>The shared instance; this type holds no state.</summary>
    public static NoOpPluginFonts Instance { get; } = new();

    private NoOpPluginFonts()
    {
    }
}
```

- [ ] **Step 5: Registry members** (`IUiRegistry.cs`)

After `IPluginImages Images => NoOpPluginImages.Instance;` in `IUiRegistry`:

```csharp
    /// <summary>
    /// The fonts this plugin may draw canvas text with: the bundled sans at a
    /// chosen size and the plugin's own font files, held to a per-plugin
    /// budget. Inert on a host that draws nothing.
    /// </summary>
    IPluginFonts Fonts => NoOpPluginFonts.Instance;
```

After `ImagesFor` in `IScopedUiRegistry`:

```csharp
    /// <summary>
    /// One plugin's font surface, the same object on every call for the same
    /// owner until it is disposed. A host that draws nothing hands out the
    /// inert surface; a host that draws hands out a surface that also
    /// implements <see cref="IDisposable"/>, and disposing it lets go of every
    /// font the plugin held.
    /// </summary>
    IPluginFonts FontsFor(PluginUiOwner owner) => NoOpPluginFonts.Instance;
```

- [ ] **Step 6: Painter members** (`PluginCanvas.cs`)

Replace the painter's second doc paragraph (`<para>Text is drawn in the client's own interface font, at its one size, …</para>`) with:

```csharp
/// <para>Text is drawn in the client's own interface font unless a
/// <see cref="PluginFont"/> from <see cref="IPluginFonts"/> is given. Every
/// clip pushed during a paint must be popped before it returns.</para>
```

After `PluginSize MeasureText(string text);` add:

```csharp
    /// <summary>
    /// Draws one line of text in a font from <see cref="IPluginFonts"/>, with
    /// its top-left corner at a position. A released, dropped or invalid font
    /// draws nothing. A host that predates fonts draws the text in the
    /// interface font instead.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="position">Where the text's top-left corner goes.</param>
    /// <param name="color">The text colour.</param>
    /// <param name="font">The font to draw in.</param>
    /// <param name="outline">Whether to draw a dark outline around the glyphs.</param>
    void DrawText(string text, PluginPoint position, PluginColor color, PluginFont font, bool outline = false) =>
        DrawText(text, position, color, outline);

    /// <summary>
    /// How much room one line of text takes in a font from
    /// <see cref="IPluginFonts"/>: its advance width, kerning included, and
    /// the font's line height. (0, 0) for a released, dropped or invalid
    /// font; a host that predates fonts answers with the interface font's
    /// measure.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="font">The font to measure in.</param>
    /// <returns>The text's width and the font's line height, in pixels.</returns>
    PluginSize MeasureText(string text, PluginFont font) => MeasureText(text);
```

- [ ] **Step 7: Build and run**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | tail -3 && dotnet test tests/AcDream.Plugin.Tests -c Release --no-build --filter "FullyQualifiedName~PluginFontsContractTests" && dotnet test tests/AcDream.Headless.Tests -c Release --no-build --filter "FullyQualifiedName~FontSurface"`
Expected: `0 Warning(s)`, then `Passed!` twice.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Plugin.Abstractions/ tests/AcDream.Plugin.Tests/PluginFontsContractTests.cs tests/AcDream.Headless.Tests/HeadlessPluginApiSurfaceTests.cs
git commit -m "plugin api: fonts a plugin can draw canvas text with

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.Plugin.Abstractions/PluginFonts.cs", "src/AcDream.Plugin.Abstractions/IUiRegistry.cs", "src/AcDream.Plugin.Abstractions/PluginCanvas.cs", "tests/AcDream.Plugin.Tests/PluginFontsContractTests.cs", "tests/AcDream.Headless.Tests/HeadlessPluginApiSurfaceTests.cs"], "verifyCommand": "dotnet test tests/AcDream.Plugin.Tests -c Release --filter FullyQualifiedName~PluginFontsContractTests", "acceptanceCriteria": ["PluginFont.None invalid", "bare registries hand out NoOpPluginFonts", "NoOp refuses everything, never opens stream", "old painter default members forward", "headless Ui.Fonts inert", "0 warnings"], "modelTier": "standard"}
```

---

### Task 2: The scoped forwarder

**Goal:** A plugin's `host.Ui.Fonts` is asked of the host once under the plugin's owner, kept, and disposed with the plugin.

**Files:**
- Modify: `src/AcDream.Core/Plugins/ScopedPluginHost.cs:2054-2076` (after the `Images` property)
- Test: `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryFontsTests.cs` (create)

**Acceptance Criteria:**
- [ ] Two reads of `scoped.Ui.Fonts` return the host's surface once, asked under `("example.plugin", "Example")`
- [ ] Disposing the scoped host disposes a disposable surface once; reading `Fonts` afterwards throws `ObjectDisposedException`
- [ ] A plugin that never asked leaves nothing to dispose

**Verify:** `dotnet test tests/AcDream.Core.Tests -c Release --filter "FullyQualifiedName~ScopedUiRegistryFontsTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** (`tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryFontsTests.cs`)

```csharp
using AcDream.Core.Plugins;
using AcDream.Core.Selection;
using AcDream.Plugin.Abstractions;

namespace AcDream.Core.Tests.Plugins;

/// <summary>
/// A plugin's font surface is asked of the host once, under the plugin's own
/// owner, and goes with the plugin: the scoped registry tracks the host's
/// disposable surface like any other registration.
/// </summary>
public sealed class ScopedUiRegistryFontsTests
{
    private sealed class FakeFonts : IPluginFonts, IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private sealed class FakeScopedUiRegistry : IScopedUiRegistry
    {
        public List<PluginUiOwner> Asked { get; } = [];
        public FakeFonts Fonts { get; } = new();

        public void AddMarkupPanel(string markupPath, object binding)
        {
        }

        public IDisposable RegisterMarkupPanel(string markupPath, object binding) =>
            NoOpUiRegistration.Instance;

        public IPluginFonts FontsFor(PluginUiOwner owner)
        {
            Asked.Add(owner);
            return Fonts;
        }
    }

    private sealed class StubHost(IScopedUiRegistry ui) : IPluginHost
    {
        public bool HasUi => true;
        public IPluginLogger Log { get; } = new SilentLogger();
        public IGameState State { get; } = new EmptyGameState();
        public IEvents Events { get; } = new WorldEvents();
        public ISelectionService Selection { get; } = new SelectionState();
        public IUiRegistry Ui { get; } = ui;
        public IAutomationSurface Automation { get; } = NoOpAutomationSurface.Instance;

        private sealed class SilentLogger : IPluginLogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message, Exception? error = null) { }
        }

        private sealed class EmptyGameState : IGameState
        {
            public IReadOnlyList<WorldEntitySnapshot> Entities { get; } = [];
        }
    }

    [Fact]
    public void TheSurfaceIsAskedOnceUnderThePluginsOwnerAndKept()
    {
        var inner = new FakeScopedUiRegistry();
        var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");

        IPluginFonts first = scoped.Ui.Fonts;
        IPluginFonts second = scoped.Ui.Fonts;

        Assert.Same(inner.Fonts, first);
        Assert.Same(first, second);
        Assert.Equal(new PluginUiOwner("example.plugin", "Example"), Assert.Single(inner.Asked));
        scoped.Dispose();
    }

    [Fact]
    public void DisposingThePluginDisposesItsSurface()
    {
        var inner = new FakeScopedUiRegistry();
        var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");
        _ = scoped.Ui.Fonts;

        scoped.Dispose();

        Assert.Equal(1, inner.Fonts.Disposals);
        Assert.Throws<ObjectDisposedException>(() => scoped.Ui.Fonts);
    }

    [Fact]
    public void APluginThatNeverAskedLeavesNothingToDispose()
    {
        var inner = new FakeScopedUiRegistry();
        var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");

        scoped.Dispose();

        Assert.Empty(inner.Asked);
        Assert.Equal(0, inner.Fonts.Disposals);
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test tests/AcDream.Core.Tests -c Release --filter "FullyQualifiedName~ScopedUiRegistryFontsTests"`
Expected: FAIL — `scoped.Ui.Fonts` is the inert default, so `Assert.Same(inner.Fonts, first)` fails and `Asked` is empty.

- [ ] **Step 3: Implement** (in `ScopedUiRegistry`, after `Images`)

```csharp
        // The font surface is asked for once and kept, like the image
        // surface; a disposable one is tracked so the plugin's fonts go with
        // the plugin.
        private IPluginFonts? _fonts;

        public IPluginFonts Fonts
        {
            get
            {
                lock (_gate)
                {
                    if (_disposed)
                        throw new ObjectDisposedException(nameof(ScopedUiRegistry));
                    if (_fonts is null)
                    {
                        _fonts = _inner.FontsFor(_owner);
                        if (_fonts is IDisposable disposable)
                            _registrations.Add(disposable);
                    }
                    return _fonts;
                }
            }
        }
```

- [ ] **Step 4: Run to verify**

Run: `dotnet test tests/AcDream.Core.Tests -c Release --filter "FullyQualifiedName~ScopedUiRegistryFontsTests|FullyQualifiedName~ScopedUiRegistryImagesTests"`
Expected: `Passed!`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Core/Plugins/ScopedPluginHost.cs tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryFontsTests.cs
git commit -m "plugin fonts: a plugin's font surface is asked once and goes with the plugin

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.Core/Plugins/ScopedPluginHost.cs", "tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryFontsTests.cs"], "verifyCommand": "dotnet test tests/AcDream.Core.Tests -c Release --filter FullyQualifiedName~ScopedUiRegistryFontsTests", "acceptanceCriteria": ["asked once under the owner and kept", "disposed with the plugin, then ObjectDisposedException", "never asked -> nothing disposed"], "modelTier": "mechanical"}
```

---

### Task 3: Releasable single-channel textures

**Goal:** `TextureCache.UploadReleasableCoverage8` uploads an `R8Unorm` atlas with a linear clamp sampler, releasable through the existing `ReleaseUiTexture`, with memory accounting at one byte per pixel.

**Files:**
- Modify: `src/AcDream.App/Rendering/TextureCache.cs` (after `UploadReleasableRgba8` ~779-790; `TrackUploadedTexture`/`UntrackUploadedTexture` ~813-830)
- Test: `tests/AcDream.App.Tests/Rendering/TextureCacheReleasableUiTextureTests.cs`

**Acceptance Criteria:**
- [ ] The created texture has format `R8Unorm` and the upload is `width*height` bytes
- [ ] The registration's sampler is `GpuSamplerDescription.WorldClamp`
- [ ] `ReleaseUiTexture` releases it (slot released after the queue runs)
- [ ] A buffer whose length is not `width*height` throws `ArgumentException`
- [ ] Existing releasable tests unchanged and green

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~TextureCacheReleasableUiTextureTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to `TextureCacheReleasableUiTextureTests`)

```csharp
    [Fact]
    public void ACoverageUploadIsOneChannelClampedAndReleasable()
    {
        (RecordingGpuDevice device, TextureCache cache, HeldGpuRetirementQueue queue) = Build();

        uint handle = cache.UploadReleasableCoverage8(new byte[8 * 4], 8, 4, "test-glyphs");

        RecordingGpuTexture texture = Assert.Single(device.CreatedTextures, t => t.Name == "test-glyphs");
        Assert.Equal(GpuTextureFormat.R8Unorm, texture.Format);
        Assert.Equal((8, 4), (texture.Width, texture.Height));
        Assert.Equal(32, Assert.Single(texture.Uploads).ByteCount);
        GpuRecordedTextureRegistration registration = Assert.Single(
            device.OfKind<GpuRecordedTextureRegistration>(), r => r.TextureName == "test-glyphs");
        Assert.Equal(GpuSamplerDescription.WorldClamp, registration.Sampler);
        Assert.Equal(1, cache.ReleasableUiTextureCount);

        Assert.True(cache.ReleaseUiTexture(handle));
        queue.RunAll();

        Assert.Equal(0, cache.ReleasableUiTextureCount);
        Assert.Contains(
            device.OfKind<GpuRecordedTextureRelease>(),
            call => call.Slot == UiTextureTableHandle.ToSlot(handle).Index);
    }

    [Fact]
    public void ACoverageUploadOfTheWrongLengthIsRefused()
    {
        (_, TextureCache cache, _) = Build();

        Assert.Throws<ArgumentException>(() => cache.UploadReleasableCoverage8(new byte[8 * 4 * 4], 8, 4, "rgba"));
        Assert.Equal(0, cache.ReleasableUiTextureCount);
    }
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release`
Expected: compile error — `UploadReleasableCoverage8` does not exist.

- [ ] **Step 3: Implement**

Format constants and per-format accounting (replace `TrackUploadedTexture` and `UntrackUploadedTexture`):

```csharp
    private const string DecodedUploadFormat = "RGBA8_DECODED";
    private const string CoverageUploadFormat = "R8_COVERAGE";

    private static int BytesPerPixel(string format) => format == CoverageUploadFormat ? 1 : 4;

    private void TrackUploadedTexture(uint name, int width, int height, string format = DecodedUploadFormat)
    {
        _uploadMetadata[name] = (width, height, format);
        long bytes = checked((long)width * height * BytesPerPixel(format));
        Wb.GpuMemoryTracker.TrackResourceAllocation(Wb.GpuResourceType.Texture);
        Wb.GpuMemoryTracker.TrackAllocation(bytes, Wb.GpuResourceType.Texture);
    }

    private void UntrackUploadedTexture(uint name)
    {
        if (_uploadMetadata.Remove(name, out var metadata))
        {
            long bytes = checked((long)metadata.Width * metadata.Height * BytesPerPixel(metadata.Format));
            Wb.GpuMemoryTracker.TrackDeallocation(bytes, Wb.GpuResourceType.Texture);
            Wb.GpuMemoryTracker.TrackResourceDeallocation(Wb.GpuResourceType.Texture);
        }
    }
```

The upload (after `UploadReleasableRgba8`):

```csharp
    /// <summary>
    /// Uploads a single-channel coverage texture, such as a glyph atlas, that
    /// the caller gives back through <see cref="ReleaseUiTexture"/>. It is
    /// sampled linearly and clamped: glyphs are drawn at their own size, and
    /// their neighbours in the atlas must not bleed in.
    /// </summary>
    internal uint UploadReleasableCoverage8(byte[] coverage, int width, int height, string debugName)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentException.ThrowIfNullOrWhiteSpace(debugName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (coverage.Length != checked(width * height))
            throw new ArgumentException("A coverage texture holds one byte per pixel.", nameof(coverage));

        IGpuTexture texture = _device.CreateTexture(new GpuTextureDescription(
            debugName,
            GpuTextureKind.Texture2D,
            GpuTextureFormat.R8Unorm,
            Width: width,
            Height: height,
            LayerCount: 1,
            MipLevelCount: 1));
        try
        {
            texture.Upload(0, 0, coverage);
            uint glName = UploadAccountingName(texture);
            TrackUploadedTexture(glName, width, height, CoverageUploadFormat);
            GpuTextureSlot slot = _device.RegisterTexture(
                texture, _device.CreateSampler(GpuSamplerDescription.WorldClamp));
            uint handle = UiTextureTableHandle.FromSlot(slot);
            _releasableUiTextures.Add(handle, new GpuUiTextureEntry(texture, slot, glName, width, height));
            return handle;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }
```

- [ ] **Step 4: Run to verify**

Run: `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~TextureCacheReleasableUiTextureTests|FullyQualifiedName~PluginCanvasTeardownOrderTests"`
Expected: `Passed!`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/Rendering/TextureCache.cs tests/AcDream.App.Tests/Rendering/TextureCacheReleasableUiTextureTests.cs
git commit -m "gpu: releasable single-channel interface textures for glyph atlases

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Rendering/TextureCache.cs", "tests/AcDream.App.Tests/Rendering/TextureCacheReleasableUiTextureTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~TextureCacheReleasableUiTextureTests", "acceptanceCriteria": ["R8Unorm texture, width*height bytes uploaded", "WorldClamp sampler", "releasable through ReleaseUiTexture", "wrong length throws ArgumentException", "existing tests green"], "modelTier": "standard"}
```

---

### Task 4: Coverage sprites in the interface renderer

**Goal:** `TextRenderer.DrawCoverageSprite` and `UiRenderContext.DrawCoverageSprite` draw a region of a single-channel texture as the alpha of a colour, in the sprite runs (painter's order kept, one run per atlas).

**Files:**
- Modify: `src/AcDream.App/Rendering/TextRenderer.cs` (`SpriteSeg`, `NextSpriteSeg`, `DrawLayer`, new method and debug accessor)
- Modify: `src/AcDream.App/UI/UiRenderContext.cs` (new `DrawCoverageSprite` + private absolute helper)
- Test: `tests/AcDream.App.Tests/Rendering/TextRendererCoverageSpriteTests.cs` (create)

**Acceptance Criteria:**
- [ ] Fill → coverage(9) → coverage(9) → fill gives three runs: coverage `[None, 9, None]`, colour textures all `None`
- [ ] Flushing sets push constants `TextureIndexA = Unassigned, TextureIndexB = slot of 9` for the coverage run
- [ ] A coverage sprite never joins an untextured fill run
- [ ] Through `UiRenderContext`, a clip rect clips the sprite and rescales its UVs; `PushAlpha(0.5f)` halves its alpha

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~TextRendererCoverageSpriteTests|FullyQualifiedName~TextRendererBlendTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** (`tests/AcDream.App.Tests/Rendering/TextRendererCoverageSpriteTests.cs`)

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// Glyphs from a single-channel atlas are drawn in the sprite runs, so text
/// keeps its place among fills and images, and a run of glyphs from one atlas
/// is one draw.
/// </summary>
public sealed class TextRendererCoverageSpriteTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    private const uint Atlas = 9u;

    [Fact]
    public void CoverageSpritesKeepTheirPlaceAndShareARunPerAtlas()
    {
        using var device = new RecordingGpuDevice();
        var frames = new FrameSource();
        using var renderer = new TextRenderer(device, frames, "unused");
        renderer.Begin(new Vector2(64f, 64f));

        renderer.DrawFill(0f, 0f, 10f, 10f, Vector4.One);
        renderer.DrawCoverageSprite(Atlas, 0f, 0f, 4f, 4f, 0f, 0f, 0.5f, 0.5f, Vector4.One);
        renderer.DrawCoverageSprite(Atlas, 4f, 0f, 4f, 4f, 0.5f, 0f, 1f, 0.5f, Vector4.One);
        renderer.DrawFill(0f, 20f, 10f, 10f, Vector4.One);

        Assert.Equal([UiTextureTableHandle.None, UiTextureTableHandle.None, UiTextureTableHandle.None],
            renderer.DebugSpriteSegments.Select(seg => seg.Texture).ToArray());
        Assert.Equal([UiTextureTableHandle.None, Atlas, UiTextureTableHandle.None],
            renderer.DebugSpriteSegmentCoverage);
        Assert.Equal([6, 12, 6], renderer.DebugSpriteSegments.Select(seg => seg.VertexCount).ToArray());

        device.Clear();
        using (IGpuFrame frame = device.BeginFrame())
        {
            frames.CurrentFrame = frame;
            renderer.Flush(null);
            frames.CurrentFrame = null;
        }

        GpuPushConstants[] constants = device.OfKind<GpuRecordedPushConstants>().Select(c => c.Constants).ToArray();
        Assert.Equal(3, constants.Length);
        Assert.Equal(GpuTextureSlot.Unassigned.Index, constants[1].TextureIndexA);
        Assert.Equal(UiTextureTableHandle.ToSlot(Atlas).Index, constants[1].TextureIndexB);
        Assert.Equal(GpuTextureSlot.Unassigned.Index, constants[0].TextureIndexB);
    }

    [Fact]
    public void ThroughTheContextACoverageSpriteIsClippedAndFaded()
    {
        using var device = new RecordingGpuDevice();
        using var renderer = new TextRenderer(device, new FrameSource(), "unused");
        renderer.Begin(new Vector2(64f, 64f));
        var context = new UiRenderContext(renderer, new Vector2(64f, 64f));

        context.PushClip(0f, 0f, 2f, 4f);
        context.PushAlpha(0.5f);
        context.DrawCoverageSprite(Atlas, 0f, 0f, 4f, 4f, 0f, 0f, 1f, 1f, Vector4.One);

        (uint _, IReadOnlyList<float> verts) = Assert.Single(renderer.DebugSpriteSegmentVerts);
        float[] xs = [.. Enumerable.Range(0, verts.Count / TextRenderer.FloatsPerVertex).Select(v => verts[v * TextRenderer.FloatsPerVertex])];
        float[] us = [.. Enumerable.Range(0, verts.Count / TextRenderer.FloatsPerVertex).Select(v => verts[v * TextRenderer.FloatsPerVertex + 2])];
        Assert.Equal(2f, xs.Max());
        Assert.Equal(0.5f, us.Max());
        Assert.Equal(0.5f, verts[7]);
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release`
Expected: compile errors — `DrawCoverageSprite` and `DebugSpriteSegmentCoverage` do not exist.

- [ ] **Step 3: Implement in `TextRenderer.cs`**

`SpriteSeg` gains a coverage handle:

```csharp
    private sealed class SpriteSeg
    {
        public uint Texture;
        public uint Coverage;
        public bool Premultiplied;
        public readonly List<float> Verts = new(256);
    }
```

`NextSpriteSeg` matches on all three (replace):

```csharp
    private static SpriteSeg NextSpriteSeg(
        List<SpriteSeg> segs, ref int used, uint texture,
        bool premultiplied = false, uint coverage = UiTextureTableHandle.None)
    {
        if (used > 0)
        {
            SpriteSeg last = segs[used - 1];
            if (last.Texture == texture && last.Coverage == coverage && last.Premultiplied == premultiplied)
                return last;
        }
        if (used < segs.Count)
        {
            var s = segs[used++];
            s.Texture = texture;
            s.Coverage = coverage;
            s.Premultiplied = premultiplied;
            s.Verts.Clear();
            return s;
        }
        var ns = new SpriteSeg { Texture = texture, Coverage = coverage, Premultiplied = premultiplied };
        segs.Add(ns);
        used++;
        return ns;
    }
```

(`UiTextureTableHandle.None` is a `const`, so it can be the parameter default.)

In `DrawLayer`, the sprite loop sets both handles:

```csharp
            SetTextures(encoder, colorHandle: seg.Texture, coverageHandle: seg.Coverage);
```

The draw (after `DrawPremultipliedSprite`):

```csharp
    /// <summary>
    /// Draws a region of a single-channel texture, such as a glyph atlas,
    /// whose value is the alpha of <paramref name="color"/>. It lands in the
    /// sprite runs, so it keeps its place among fills and images, and
    /// consecutive glyphs from one atlas are one draw.
    /// </summary>
    internal void DrawCoverageSprite(uint coverageTexture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 color)
    {
        SpriteSeg seg = OverlayMode
            ? NextSpriteSeg(_overlaySpriteSegs, ref _overlaySegUsed, UiTextureTableHandle.None, coverage: coverageTexture)
            : NextSpriteSeg(_spriteSegs,        ref _segUsed,        UiTextureTableHandle.None, coverage: coverageTexture);
        AppendQuad(seg.Verts, x, y, w, h, u0, v0, u1, v1, color);
    }
```

Debug accessor:

```csharp
    internal IReadOnlyList<uint> DebugSpriteSegmentCoverage
    {
        get
        {
            var result = new List<uint>(_segUsed);
            for (int i = 0; i < _segUsed; i++)
                result.Add(_spriteSegs[i].Coverage);
            return result;
        }
    }
```

- [ ] **Step 4: Implement in `UiRenderContext.cs`** (after `DrawSpritePremultiplied`)

```csharp
    /// <summary>
    /// Draws a region of a single-channel texture as the alpha of
    /// <paramref name="color"/> (see <see cref="TextRenderer.DrawCoverageSprite"/>).
    /// Translation, clipping and the alpha stack apply as they do for
    /// <see cref="DrawSprite"/>.
    /// </summary>
    internal void DrawCoverageSprite(uint coverageTexture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 color) =>
        DrawCoverageSpriteAbsolute(coverageTexture, x + _current.X, y + _current.Y, w, h, u0, v0, u1, v1, color);

    private void DrawCoverageSpriteAbsolute(uint coverageTexture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 color)
    {
        if (_clip is { } clip
            && !UiClipRect.TryClipSprite(
                clip, ref x, ref y, ref w, ref h, ref u0, ref v0, ref u1, ref v1))
            return;
        TextRenderer.DrawCoverageSprite(coverageTexture, x, y, w, h, u0, v0, u1, v1, ApplyAlpha(color));
    }
```

- [ ] **Step 5: Run to verify**

Run: `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~TextRendererCoverageSpriteTests|FullyQualifiedName~TextRendererBlendTests|FullyQualifiedName~TextRendererLinearTwinTests|FullyQualifiedName~PluginCanvas"`
Expected: `Passed!`.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/Rendering/TextRenderer.cs src/AcDream.App/UI/UiRenderContext.cs tests/AcDream.App.Tests/Rendering/TextRendererCoverageSpriteTests.cs
git commit -m "gpu: coverage sprites keep their place in the interface's sprite runs

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Rendering/TextRenderer.cs", "src/AcDream.App/UI/UiRenderContext.cs", "tests/AcDream.App.Tests/Rendering/TextRendererCoverageSpriteTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~TextRendererCoverageSpriteTests", "acceptanceCriteria": ["three runs with coverage [None,9,None]", "push constants A unassigned, B = atlas slot", "coverage never joins fill run", "context clips UVs and applies alpha"], "modelTier": "standard"}
```

---

### Task 5: Baking a font into a coverage atlas

**Goal:** `CanvasFontBaker.TryBake` turns font bytes, a pixel size and code point ranges into a single-channel atlas plus glyph metrics, packing only the glyphs the font has, in the smallest atlas up to 2048² that fits; a tiny CFF fixture proves `.otf`.

**Files:**
- Create: `src/AcDream.App/UI/CanvasFontBaker.cs`
- Modify: `src/AcDream.App/UI/BundledUiFont.cs` (extract `ReadEmbeddedFontBytes`)
- Create: `tests/Fixtures/fonts/NotoSansCffFixture.otf`, `tests/Fixtures/fonts/OFL.txt`, `tests/Fixtures/fonts/README.md`
- Modify: `tests/AcDream.App.Tests/AcDream.App.Tests.csproj` (embed the fixture)
- Test: `tests/AcDream.App.Tests/UI/CanvasFontBakerTests.cs` (create)

**Acceptance Criteria:**
- [ ] Noto at 6, 16 and 64 px with the default ranges bakes; every character of `"BuffProfile_Banes åäö Ω Ж —"` has a glyph inside the atlas; atlas sides ≤ 2048; 16 px fits 512×256
- [ ] A custom range `A`–`Z` bakes exactly 26 glyphs
- [ ] A range the font has nothing in fails with "none of the requested characters"
- [ ] `maximumGlyphs: 10` on the default ranges fails naming the count
- [ ] A reversed range, a range past U+10FFFF, or more than 65,536 scanned code points fails
- [ ] Bytes that are not a font fail without throwing
- [ ] The CFF fixture (`OTTO` signature) bakes `A`, `V`, `T`, `o`, `?`
- [ ] `BundledUiFontTests` unchanged and green

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~CanvasFontBakerTests|FullyQualifiedName~BundledUiFontTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Generate the CFF fixture** (one-off; the generator is not committed, the README records it)

```bash
FIX=tests/Fixtures/fonts; mkdir -p "$FIX"
VENV=$(mktemp -d)/ft && python3 -m venv "$VENV" && "$VENV/bin/pip" install -q "fonttools==4.60.1"
"$VENV/bin/python" - <<'EOF'
from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.pens.t2CharStringPen import T2CharStringPen
from fontTools.fontBuilder import FontBuilder
src = TTFont("assets/fonts/NotoSans/NotoSans-Regular.ttf")
opts = subset.Options(); opts.layout_features = ['kern']; opts.notdef_outline = True
s = subset.Subsetter(opts); s.populate(text="AVTo?"); s.subset(src)
glyphs = src.getGlyphOrder(); gs = src.getGlyphSet(); cmap = src.getBestCmap()
charstrings = {}
for g in glyphs:
    pen = T2CharStringPen(gs[g].width, gs); gs[g].draw(pen); charstrings[g] = pen.getCharString()
fb = FontBuilder(src['head'].unitsPerEm, isTTF=False)
fb.setupGlyphOrder(glyphs); fb.setupCharacterMap(cmap)
fb.setupCFF("NotoSansCffFixture", {"FullName": "Noto Sans CFF Fixture"}, charstrings, {})
fb.setupHorizontalMetrics({g: src['hmtx'][g] for g in glyphs})
hh = src['hhea']; fb.setupHorizontalHeader(ascent=hh.ascent, descent=hh.descent)
fb.setupNameTable({"familyName": "Noto Sans CFF Fixture", "styleName": "Regular"})
os2 = src['OS/2']; fb.setupOS2(sTypoAscender=os2.sTypoAscender, usWinAscent=os2.usWinAscent, usWinDescent=os2.usWinDescent)
fb.setupPost()
fb.font['GPOS'] = src['GPOS']
fb.save("tests/Fixtures/fonts/NotoSansCffFixture.otf")
EOF
cp assets/fonts/NotoSans/OFL.txt "$FIX/OFL.txt"
head -c 4 "$FIX/NotoSansCffFixture.otf"; echo; wc -c < "$FIX/NotoSansCffFixture.otf"
```

Expected: `OTTO` and a size around 1400 bytes.

Write `tests/Fixtures/fonts/README.md`:

```markdown
# Font fixtures

`NotoSansCffFixture.otf` is a CFF-outline (OpenType, `OTTO`) subset of
`assets/fonts/NotoSans/NotoSans-Regular.ttf` containing only `A V T o ?`, with
Noto's GPOS kerning kept. It exists so tests exercise the `.otf` path of the
canvas font baker. Like its source it is licensed under the SIL Open Font
License 1.1 (`OFL.txt`).

Generated with fontTools 4.60.1: subset the TTF to `AVTo?`, redraw each glyph
with `T2CharStringPen`, and build a CFF font with `FontBuilder(isTTF=False)`,
copying `hmtx`, `hhea`, `OS/2` metrics and the `GPOS` table.
```

Embed it — add to the `ItemGroup` in `tests/AcDream.App.Tests/AcDream.App.Tests.csproj` that holds the `..\Fixtures\…` links:

```xml
    <EmbeddedResource Include="..\Fixtures\fonts\NotoSansCffFixture.otf"
                      LogicalName="AcDream.Tests.Fixtures.Fonts.NotoSansCffFixture.otf" />
```

- [ ] **Step 2: Write the failing tests** (`tests/AcDream.App.Tests/UI/CanvasFontBakerTests.cs`)

```csharp
using System.Linq;
using System.Text;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// A canvas font is baked once, when asked for, into a single-channel atlas
/// holding only the glyphs the font has among the characters asked for.
/// </summary>
public sealed class CanvasFontBakerTests
{
    private static readonly byte[] Noto = BundledUiFont.ReadEmbeddedFontBytes();

    internal static byte[] CffFixture()
    {
        using Stream stream = typeof(CanvasFontBakerTests).Assembly.GetManifestResourceStream(
            "AcDream.Tests.Fixtures.Fonts.NotoSansCffFixture.otf")
            ?? throw new InvalidOperationException("The CFF font fixture is not embedded.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    [Theory]
    [InlineData(6f)]
    [InlineData(16f)]
    [InlineData(64f)]
    public void NotoBakesTheDefaultCharactersIntoOneAtlas(float size)
    {
        Assert.True(CanvasFontBaker.TryBake(
            Noto, size, CanvasFontBaker.DefaultRanges, 2048, out CanvasFontBake? bake, out string? failure), failure);

        Assert.InRange(bake.AtlasWidth, 1, CanvasFontBaker.MaximumAtlasSide);
        Assert.InRange(bake.AtlasHeight, 1, CanvasFontBaker.MaximumAtlasSide);
        Assert.Equal(bake.AtlasWidth * bake.AtlasHeight, bake.Coverage.Length);
        Assert.Contains(bake.Coverage, value => value > 0 && value < 255);
        Assert.True(bake.LineHeight >= size);
        Assert.InRange(bake.Ascent, 1f, bake.LineHeight);
        foreach (Rune rune in "BuffProfile_Banes åäö Ω Ж —".EnumerateRunes())
        {
            CanvasGlyph glyph = bake.Glyphs[rune.Value];
            Assert.True(glyph.Advance > 0f);
            Assert.InRange(glyph.U1, 0f, 1f);
            Assert.InRange(glyph.V1, 0f, 1f);
        }
    }

    [Fact]
    public void SixteenPixelsFitsASmallAtlas()
    {
        Assert.True(CanvasFontBaker.TryBake(Noto, 16f, CanvasFontBaker.DefaultRanges, 2048, out CanvasFontBake? bake, out _));

        Assert.Equal((512, 256), (bake.AtlasWidth, bake.AtlasHeight));
    }

    [Fact]
    public void ACustomRangeBakesOnlyThoseCharacters()
    {
        Assert.True(CanvasFontBaker.TryBake(Noto, 16f, [('A', 'Z')], 2048, out CanvasFontBake? bake, out _));

        Assert.Equal(26, bake.Glyphs.Count);
        Assert.All(bake.Glyphs.Keys, codepoint => Assert.InRange(codepoint, 'A', 'Z'));
    }

    [Fact]
    public void ARangeTheFontHasNothingInIsRefused()
    {
        Assert.False(CanvasFontBaker.TryBake(Noto, 16f, [(0xE000, 0xE0FF)], 2048, out _, out string? failure));

        Assert.Contains("none of the requested characters", failure);
    }

    [Fact]
    public void MoreGlyphsThanTheCapIsRefused()
    {
        Assert.False(CanvasFontBaker.TryBake(Noto, 16f, CanvasFontBaker.DefaultRanges, 10, out _, out string? failure));

        Assert.Contains("the most a font may prepare is 10", failure);
    }

    [Theory]
    [InlineData(0x5A, 0x41)]
    [InlineData(0x10FFFF, 0x110000)]
    [InlineData(-1, 0x41)]
    public void ARangeThatIsNotOneIsRefused(int first, int last)
    {
        Assert.False(CanvasFontBaker.TryBake(Noto, 16f, [(first, last)], 2048, out _, out string? failure));

        Assert.Contains("is not a range of code points", failure);
    }

    [Fact]
    public void ScanningPastTheCodepointCeilingIsRefused()
    {
        Assert.False(CanvasFontBaker.TryBake(Noto, 16f, [(0, 0x10000)], 2048, out _, out string? failure));

        Assert.Contains("more than 65,536 code points", failure);
    }

    [Fact]
    public void BytesThatAreNotAFontAreRefusedWithoutThrowing()
    {
        Assert.False(CanvasFontBaker.TryBake(new byte[64], 16f, CanvasFontBaker.DefaultRanges, 2048, out _, out string? failure));

        Assert.Contains("not a TrueType or OpenType font", failure);
    }

    [Fact]
    public void AnOpenTypeCffFontBakes()
    {
        byte[] otf = CffFixture();
        Assert.Equal("OTTO"u8.ToArray(), otf[..4]);

        Assert.True(CanvasFontBaker.TryBake(otf, 16f, CanvasFontBaker.DefaultRanges, 2048, out CanvasFontBake? bake, out string? failure), failure);

        Assert.Equal(['?', 'A', 'T', 'V', 'o'], bake.Glyphs.Keys.Order().Select(codepoint => (char)codepoint).ToArray());
        Assert.All(bake.Glyphs.Values, glyph => Assert.True(glyph.Width > 0f && glyph.Height > 0f));
    }
}
```

- [ ] **Step 3: Run to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release`
Expected: compile errors — `CanvasFontBaker`, `CanvasFontBake`, `CanvasGlyph`, `ReadEmbeddedFontBytes` do not exist.

- [ ] **Step 4: Extract the embedded font bytes** (`BundledUiFont.cs`)

Add, and make `Bake` use it in place of its first five statements (the resource stream through `byte[] fontBytes = bytes.ToArray();`):

```csharp
    /// <summary>The embedded Noto Sans file, for anything else that bakes it.</summary>
    internal static byte[] ReadEmbeddedFontBytes()
    {
        using var stream = typeof(BundledUiFont).Assembly.GetManifestResourceStream(
            "AcDream.App.Fonts.NotoSans-Regular.ttf")
            ?? throw new InvalidOperationException("Bundled Noto Sans font is missing.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
```

and in `Bake`, after the size check: `byte[] fontBytes = ReadEmbeddedFontBytes();`. Nothing else in the file changes.

- [ ] **Step 5: Create `CanvasFontBaker.cs`**

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using StbTrueTypeSharp;

namespace AcDream.App.UI;

/// <summary>
/// One glyph as a canvas font draws it: its box relative to the pen on the
/// baseline and its advance, in canvas pixels, its place in the atlas as UVs,
/// and the font's own index for it, which kerning is looked up by.
/// </summary>
internal readonly record struct CanvasGlyph(
    float OffsetX,
    float OffsetY,
    float Width,
    float Height,
    float Advance,
    float U0,
    float V0,
    float U1,
    float V1,
    int GlyphIndex);

/// <summary>What one bake produced: a single-channel atlas and the metrics to draw from it.</summary>
internal sealed record CanvasFontBake(
    byte[] Coverage,
    int AtlasWidth,
    int AtlasHeight,
    float PixelSize,
    float Scale,
    float LineHeight,
    float Ascent,
    IReadOnlyDictionary<int, CanvasGlyph> Glyphs);

/// <summary>
/// Bakes a TrueType or CFF OpenType font at one size into a single-channel
/// coverage atlas. Only the requested characters the font actually has are
/// packed, from an explicit list: StbTrueTypeSharp's skip-missing switch
/// makes the packer report failure even when every glyph fitted. The atlas
/// is the smallest in a fixed ladder of sizes, up to 2048 on a side, that
/// holds them all.
/// </summary>
internal static class CanvasFontBaker
{
    internal const int MaximumAtlasSide = 2048;

    internal const int MaximumScannedCodepoints = 65_536;

    /// <summary>The characters baked when none are named: Latin, Greek and Cyrillic, general punctuation.</summary>
    internal static readonly IReadOnlyList<(int First, int Last)> DefaultRanges =
        [(0x20, 0x24F), (0x370, 0x52F), (0x2000, 0x206F)];

    private static readonly (int Width, int Height)[] AtlasSizes =
        [(256, 256), (512, 256), (512, 512), (1024, 512), (1024, 1024), (2048, 1024), (2048, 2048)];

    internal static unsafe bool TryBake(
        byte[] fontBytes,
        float pixelSize,
        IReadOnlyList<(int First, int Last)> ranges,
        int maximumGlyphs,
        [NotNullWhen(true)] out CanvasFontBake? bake,
        [NotNullWhen(false)] out string? failure)
    {
        ArgumentNullException.ThrowIfNull(fontBytes);
        ArgumentNullException.ThrowIfNull(ranges);
        bake = null;
        if (!float.IsFinite(pixelSize) || pixelSize <= 0f)
        {
            failure = "the size is not a positive number";
            return false;
        }

        StbTrueType.stbtt_fontinfo? info;
        try
        {
            info = StbTrueType.CreateFont(fontBytes, 0);
        }
        catch (Exception)
        {
            info = null;
        }
        if (info is null)
        {
            failure = "the file is not a TrueType or OpenType font";
            return false;
        }

        using (info)
        {
            if (!TryCollectCodepoints(info, ranges, maximumGlyphs, out int[] codepoints, out failure))
                return false;

            float scale = StbTrueType.stbtt_ScaleForPixelHeight(info, pixelSize);
            int ascent, descent, gap;
            StbTrueType.stbtt_GetFontVMetrics(info, &ascent, &descent, &gap);
            float baseline = MathF.Round(ascent * scale);
            float lineHeight = MathF.Ceiling((ascent - descent + gap) * scale);

            foreach ((int width, int height) in AtlasSizes)
            {
                byte[] coverage = new byte[width * height];
                var packed = new StbTrueType.stbtt_packedchar[codepoints.Length];
                if (!TryPack(fontBytes, pixelSize, codepoints, coverage, width, height, packed))
                    continue;

                var glyphs = new Dictionary<int, CanvasGlyph>(codepoints.Length);
                for (int i = 0; i < codepoints.Length; i++)
                {
                    StbTrueType.stbtt_packedchar p = packed[i];
                    glyphs[codepoints[i]] = new CanvasGlyph(
                        p.xoff,
                        p.yoff,
                        p.x1 - p.x0,
                        p.y1 - p.y0,
                        p.xadvance,
                        p.x0 / (float)width,
                        p.y0 / (float)height,
                        p.x1 / (float)width,
                        p.y1 / (float)height,
                        StbTrueType.stbtt_FindGlyphIndex(info, codepoints[i]));
                }
                bake = new CanvasFontBake(coverage, width, height, pixelSize, scale, lineHeight, baseline, glyphs);
                failure = null;
                return true;
            }

            failure = string.Create(
                CultureInfo.InvariantCulture,
                $"{codepoints.Length} glyphs at {pixelSize} px do not fit a {MaximumAtlasSide}x{MaximumAtlasSide} atlas");
            return false;
        }
    }

    private static bool TryCollectCodepoints(
        StbTrueType.stbtt_fontinfo info,
        IReadOnlyList<(int First, int Last)> ranges,
        int maximumGlyphs,
        out int[] codepoints,
        [NotNullWhen(false)] out string? failure)
    {
        codepoints = [];
        long scanned = 0;
        var present = new SortedSet<int>();
        foreach ((int first, int last) in ranges)
        {
            if (first < 0 || last > 0x10FFFF || last < first)
            {
                failure = $"the range U+{first:X4}-U+{last:X4} is not a range of code points";
                return false;
            }
            scanned += last - first + 1L;
            if (scanned > MaximumScannedCodepoints)
            {
                // Invariant: this reason reaches the log and the tests, whatever the machine's culture.
                failure = string.Create(
                    CultureInfo.InvariantCulture,
                    $"the ranges name more than {MaximumScannedCodepoints:N0} code points");
                return false;
            }
            for (int codepoint = first; codepoint <= last; codepoint++)
            {
                if (StbTrueType.stbtt_FindGlyphIndex(info, codepoint) != 0)
                    present.Add(codepoint);
            }
        }
        if (present.Count == 0)
        {
            failure = "the font has none of the requested characters";
            return false;
        }
        if (present.Count > maximumGlyphs)
        {
            failure = $"the font has {present.Count} of the requested characters; "
                + $"the most a font may prepare is {maximumGlyphs}";
            return false;
        }
        codepoints = [.. present];
        failure = null;
        return true;
    }

    private static unsafe bool TryPack(
        byte[] fontBytes,
        float pixelSize,
        int[] codepoints,
        byte[] coverage,
        int width,
        int height,
        StbTrueType.stbtt_packedchar[] packed)
    {
        var context = new StbTrueType.stbtt_pack_context();
        fixed (byte* pixels = coverage)
        fixed (byte* font = fontBytes)
        fixed (int* points = codepoints)
        fixed (StbTrueType.stbtt_packedchar* chars = packed)
        {
            if (StbTrueType.stbtt_PackBegin(context, pixels, width, height, width, 1, null) == 0)
                return false;
            StbTrueType.stbtt_PackSetOversampling(context, 1, 1);
            var range = new StbTrueType.stbtt_pack_range
            {
                font_size = pixelSize,
                first_unicode_codepoint_in_range = 0,
                array_of_unicode_codepoints = points,
                num_chars = codepoints.Length,
                chardata_for_range = chars,
            };
            int packedAll = StbTrueType.stbtt_PackFontRanges(context, font, 0, &range, 1);
            StbTrueType.stbtt_PackEnd(context);
            return packedAll != 0;
        }
    }
}
```

(`AcDream.App` must allow unsafe code — it already does, `BundledUiFont.Bake` is `unsafe`.)

- [ ] **Step 6: Run to verify**

Run: `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~CanvasFontBakerTests|FullyQualifiedName~BundledUiFontTests"`
Expected: `Passed!`. If `SixteenPixelsFitsASmallAtlas` reports a different size, the ladder or padding differs from the prototype (which packed 1,063 glyphs at 16 px into 512×256 with padding 1); do not loosen the test without finding why.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.App/UI/CanvasFontBaker.cs src/AcDream.App/UI/BundledUiFont.cs tests/Fixtures/fonts tests/AcDream.App.Tests/AcDream.App.Tests.csproj tests/AcDream.App.Tests/UI/CanvasFontBakerTests.cs
git commit -m "plugin fonts: bake a font at one size into a single-channel atlas

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/CanvasFontBaker.cs", "src/AcDream.App/UI/BundledUiFont.cs", "tests/Fixtures/fonts/NotoSansCffFixture.otf", "tests/Fixtures/fonts/OFL.txt", "tests/Fixtures/fonts/README.md", "tests/AcDream.App.Tests/AcDream.App.Tests.csproj", "tests/AcDream.App.Tests/UI/CanvasFontBakerTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~CanvasFontBakerTests|FullyQualifiedName~BundledUiFontTests\"", "acceptanceCriteria": ["Noto 6/16/64 px default ranges bake, sample chars present, atlas <=2048", "16px -> 512x256", "A-Z -> 26 glyphs", "empty range refused", "glyph cap refused", "bad ranges refused", "scan cap refused", "non-font refused without throwing", "CFF fixture bakes ?ATVo", "BundledUiFontTests green"], "modelTier": "standard"}
```

---

### Task 6: A baked font the canvas draws and measures with

**Goal:** `CanvasFont` (glyph lookup with `?` fallback, kerning by glyph index, measuring by `Rune`) and `UiRenderContext.DrawStringCanvasFont` (pixel-snapped coverage glyphs, optional 8-copy outline), measuring exactly what it draws.

**Files:**
- Create: `src/AcDream.App/UI/CanvasFont.cs`
- Modify: `src/AcDream.App/UI/UiRenderContext.cs` (new `DrawStringCanvasFont` + private pass, next to `DrawStringDat`)
- Test: `tests/AcDream.App.Tests/UI/CanvasFontTests.cs` (create)

**Acceptance Criteria:**
- [ ] `MeasureWidth("AV")` is less than `MeasureWidth("A") + MeasureWidth("V")` for Noto (kerning), for both the TTF and the CFF fixture
- [ ] A character the font lacks measures and draws as `?` when `?` is baked; with an `A`–`Z` bake it draws nothing and adds no advance
- [ ] Control characters draw nothing
- [ ] A surrogate pair is looked up as one code point
- [ ] Drawing `"AV"` emits 12 vertices in one coverage run; the second glyph's left edge equals `floor(x + advance(A) + kern(A,V) + offsetX(V) + 0.5)`
- [ ] `outline: true` emits 9 × the glyph quads, the first 8 in the outline colour
- [ ] `Dispose` can be called twice

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~CanvasFontTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** (`tests/AcDream.App.Tests/UI/CanvasFontTests.cs`)

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>A baked canvas font measures exactly what it draws, kerning included.</summary>
public sealed class CanvasFontTests
{
    private sealed class NullFrames : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }

    private const uint Atlas = 42u;

    private static CanvasFont Bake(byte[] bytes, IReadOnlyList<(int, int)>? ranges = null)
    {
        Assert.True(CanvasFontBaker.TryBake(
            bytes, 16f, ranges ?? CanvasFontBaker.DefaultRanges, 2048, out CanvasFontBake? bake, out string? failure), failure);
        return new CanvasFont(bake, Atlas, bytes);
    }

    public static TheoryData<string> Fonts => new() { "ttf", "otf" };

    private static byte[] Bytes(string kind) =>
        kind == "ttf" ? BundledUiFont.ReadEmbeddedFontBytes() : CanvasFontBakerTests.CffFixture();

    [Theory]
    [MemberData(nameof(Fonts))]
    public void KerningPullsAVTogether(string kind)
    {
        using CanvasFont font = Bake(Bytes(kind));

        Assert.True(font.MeasureWidth("AV") < font.MeasureWidth("A") + font.MeasureWidth("V"));
    }

    [Fact]
    public void AMissingCharacterIsAQuestionMarkWhenOneWasBakedAndNothingOtherwise()
    {
        using CanvasFont full = Bake(BundledUiFont.ReadEmbeddedFontBytes());
        using CanvasFont caps = Bake(BundledUiFont.ReadEmbeddedFontBytes(), [('A', 'Z')]);

        Assert.Equal(full.MeasureWidth("?"), full.MeasureWidth("漢"));
        Assert.Equal(caps.MeasureWidth("AB"), caps.MeasureWidth("A漢B"));
        Assert.Equal(0f, full.MeasureWidth("\n\t"));
    }

    [Fact]
    public void ASurrogatePairIsOneCharacter()
    {
        using CanvasFont full = Bake(BundledUiFont.ReadEmbeddedFontBytes());

        // U+1F600 is not in the bake: one '?', not two.
        Assert.Equal(full.MeasureWidth("?"), full.MeasureWidth("\U0001F600"));
    }

    [Fact]
    public void DrawingLandsAtTheMeasuredPositionsInOneCoverageRun()
    {
        using CanvasFont font = Bake(BundledUiFont.ReadEmbeddedFontBytes());
        using var device = new RecordingGpuDevice();
        using var renderer = new TextRenderer(device, new NullFrames(), "unused");
        renderer.Begin(new Vector2(200f, 50f));
        var context = new UiRenderContext(renderer, new Vector2(200f, 50f));

        context.DrawStringCanvasFont(font, "AV", 10.3f, 5f, Vector4.One);

        Assert.Equal([Atlas], renderer.DebugSpriteSegmentCoverage);
        (uint _, IReadOnlyList<float> verts) = Assert.Single(renderer.DebugSpriteSegmentVerts);
        Assert.Equal(12, verts.Count / TextRenderer.FloatsPerVertex);
        Assert.True(font.TryGetGlyph('A', out CanvasGlyph a));
        Assert.True(font.TryGetGlyph('V', out CanvasGlyph v));
        float expectedLeft = MathF.Floor(10.3f + a.Advance + font.Kerning(a.GlyphIndex, v.GlyphIndex) + v.OffsetX + 0.5f);
        float secondLeft = Enumerable.Range(6, 6).Min(i => verts[i * TextRenderer.FloatsPerVertex]);
        Assert.Equal(expectedLeft, secondLeft);
    }

    [Fact]
    public void AnOutlineIsEightCopiesInTheOutlineColourUnderTheFill()
    {
        using CanvasFont font = Bake(BundledUiFont.ReadEmbeddedFontBytes());
        using var device = new RecordingGpuDevice();
        using var renderer = new TextRenderer(device, new NullFrames(), "unused");
        renderer.Begin(new Vector2(200f, 50f));
        var context = new UiRenderContext(renderer, new Vector2(200f, 50f));
        var red = new Vector4(1f, 0f, 0f, 1f);

        context.DrawStringCanvasFont(font, "A", 10f, 5f, Vector4.One, outline: true, outlineColor: red);

        (uint _, IReadOnlyList<float> verts) = Assert.Single(renderer.DebugSpriteSegmentVerts);
        int vertices = verts.Count / TextRenderer.FloatsPerVertex;
        Assert.Equal(9 * 6, vertices);
        Assert.Equal(1f, verts[4]);
        Assert.Equal(0f, verts[5]);
        int fillStart = 8 * 6 * TextRenderer.FloatsPerVertex;
        Assert.Equal(1f, verts[fillStart + 5]);
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        CanvasFont font = Bake(BundledUiFont.ReadEmbeddedFontBytes());

        font.Dispose();
        font.Dispose();
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release`
Expected: compile errors — `CanvasFont` and `DrawStringCanvasFont` do not exist.

- [ ] **Step 3: Create `CanvasFont.cs`**

```csharp
using System.Text;
using StbTrueTypeSharp;

namespace AcDream.App.UI;

/// <summary>
/// A baked font a canvas draws text with: the glyph metrics, the atlas
/// texture they index, and the font's kerning. It keeps its own copy of the
/// font data for kerning lookups; disposing it lets that go. The atlas
/// texture belongs to whoever uploaded it.
/// </summary>
internal sealed class CanvasFont : IDisposable
{
    private const int KerningCacheLimit = 4096;

    private readonly IReadOnlyDictionary<int, CanvasGlyph> _glyphs;
    private readonly float _scale;
    private readonly Dictionary<(int Left, int Right), float> _kerningCache = [];
    private StbTrueType.stbtt_fontinfo? _kerning;

    internal CanvasFont(CanvasFontBake bake, uint atlasTexture, byte[] fontBytes)
    {
        ArgumentNullException.ThrowIfNull(bake);
        ArgumentNullException.ThrowIfNull(fontBytes);
        AtlasTexture = atlasTexture;
        PixelSize = bake.PixelSize;
        LineHeight = bake.LineHeight;
        Ascent = bake.Ascent;
        _glyphs = bake.Glyphs;
        _scale = bake.Scale;
        _kerning = StbTrueType.CreateFont(fontBytes, 0);
    }

    internal uint AtlasTexture { get; }

    internal float PixelSize { get; }

    internal float LineHeight { get; }

    internal float Ascent { get; }

    /// <summary>
    /// The glyph for a code point, or the font's <c>?</c> when it lacks one
    /// and <c>?</c> was baked. Control characters have no glyph.
    /// </summary>
    internal bool TryGetGlyph(int codepoint, out CanvasGlyph glyph)
    {
        if (codepoint < 0x20)
        {
            glyph = default;
            return false;
        }
        return _glyphs.TryGetValue(codepoint, out glyph) || _glyphs.TryGetValue('?', out glyph);
    }

    /// <summary>How far the pen moves between two glyphs beyond their advances, in canvas pixels.</summary>
    internal float Kerning(int leftGlyphIndex, int rightGlyphIndex)
    {
        if (_kerning is null) return 0f;
        if (_kerningCache.TryGetValue((leftGlyphIndex, rightGlyphIndex), out float cached))
            return cached;
        if (_kerningCache.Count >= KerningCacheLimit)
            _kerningCache.Clear();
        float kerning = StbTrueType.stbtt_GetGlyphKernAdvance(_kerning, leftGlyphIndex, rightGlyphIndex) * _scale;
        _kerningCache[(leftGlyphIndex, rightGlyphIndex)] = kerning;
        return kerning;
    }

    /// <summary>The pen's travel over a line of text: advances plus kerning, exactly as it is drawn.</summary>
    internal float MeasureWidth(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        float pen = 0f;
        int previous = -1;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!TryGetGlyph(rune.Value, out CanvasGlyph glyph))
            {
                previous = -1;
                continue;
            }
            if (previous >= 0)
                pen += Kerning(previous, glyph.GlyphIndex);
            pen += glyph.Advance;
            previous = glyph.GlyphIndex;
        }
        return pen;
    }

    public void Dispose()
    {
        _kerning?.Dispose();
        _kerning = null;
    }
}
```

- [ ] **Step 4: Add the draw to `UiRenderContext.cs`** (after `DrawStringDatPass`)

```csharp
    /// <summary>
    /// Draws one line of text in a baked canvas font, its top-left corner at
    /// (<paramref name="x"/>, <paramref name="y"/>). Glyphs are coverage
    /// sprites, so the text keeps its place among fills and images. The
    /// baseline and each glyph's left edge snap to whole pixels while the pen
    /// keeps its fractional advance, as the interface font does. The outline
    /// is eight copies one pixel out, drawn first.
    /// </summary>
    internal void DrawStringCanvasFont(
        CanvasFont font, string text, float x, float y, Vector4 color,
        bool outline = false, Vector4? outlineColor = null)
    {
        if (font is null || string.IsNullOrEmpty(text)) return;
        if (outline)
        {
            Vector4 shadow = outlineColor ?? DefaultOutlineColor;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx != 0 || dy != 0)
                        DrawCanvasFontPass(font, text, x + dx, y + dy, shadow);
                }
            }
        }
        DrawCanvasFontPass(font, text, x, y, color);
    }

    private void DrawCanvasFontPass(CanvasFont font, string text, float x, float y, Vector4 color)
    {
        float pen = _current.X + x;
        float baseline = MathF.Floor(_current.Y + y + font.Ascent + 0.5f);
        int previous = -1;
        foreach (System.Text.Rune rune in text.EnumerateRunes())
        {
            if (!font.TryGetGlyph(rune.Value, out CanvasGlyph glyph))
            {
                previous = -1;
                continue;
            }
            if (previous >= 0)
                pen += font.Kerning(previous, glyph.GlyphIndex);
            if (glyph.Width > 0f && glyph.Height > 0f)
            {
                float gx = MathF.Floor(pen + glyph.OffsetX + 0.5f);
                float gy = baseline + MathF.Round(glyph.OffsetY);
                DrawCoverageSpriteAbsolute(
                    font.AtlasTexture, gx, gy, glyph.Width, glyph.Height,
                    glyph.U0, glyph.V0, glyph.U1, glyph.V1, color);
            }
            pen += glyph.Advance;
            previous = glyph.GlyphIndex;
        }
    }
```

- [ ] **Step 5: Run to verify**

Run: `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~CanvasFontTests"`
Expected: `Passed!`.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/CanvasFont.cs src/AcDream.App/UI/UiRenderContext.cs tests/AcDream.App.Tests/UI/CanvasFontTests.cs
git commit -m "plugin fonts: a baked font draws and measures the same line, kerning included

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/CanvasFont.cs", "src/AcDream.App/UI/UiRenderContext.cs", "tests/AcDream.App.Tests/UI/CanvasFontTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~CanvasFontTests", "acceptanceCriteria": ["AV kerned for ttf and otf", "? fallback / nothing without ?", "control chars nothing", "surrogate pair one code point", "12 vertices one run, second glyph at snapped kerned pen", "outline 9x quads, outline colour first", "double dispose safe"], "modelTier": "standard"}
```

---

### Task 7: One plugin's fonts, and the shared bundled bakes

**Goal:** `PluginFontTable` (per-plugin handles, dedup, ref counts, budget, thread check, report-once) and `BundledCanvasFontCache` (one bake per bundled size shared by every plugin), both against a fake upload backend.

**Files:**
- Create: `src/AcDream.App/Plugins/PluginFontTable.cs` (with `PluginFontBudget` and `IPluginFontBackend`)
- Create: `src/AcDream.App/Plugins/BundledCanvasFontCache.cs`
- Test: `tests/AcDream.App.Tests/Plugins/PluginFontTableTests.cs` (create)
- Test: `tests/AcDream.App.Tests/Plugins/BundledCanvasFontCacheTests.cs` (create)

**Acceptance Criteria:**
- [ ] Unbound: every request answers `PluginFont.None` and the stream is not opened
- [ ] Two `Bundled(16)` requests return the same handle; one release keeps it, the second frees it; a third release is false
- [ ] Two plugins' `Bundled(16)` share one upload; it is released when the last holder lets go
- [ ] `FromStream` of the CFF fixture returns a valid font with `LineHeight`/`Ascent` from the bake; the same name, size and ranges returns the same handle without reopening the stream
- [ ] Owned bytes = font file length + atlas bytes; releasing the last hold gives the texture back and zeroes them
- [ ] Refusals (each reported once, answering None): size below 6 or above 64, a 17th font, a file or atlas past 16 MiB, a stream that throws, a factory returning null, bytes that are not a font, a range the font has nothing in
- [ ] A request from another thread while bound throws `InvalidOperationException`
- [ ] `Unbind` lets every font go (owned textures released, bundled holds dropped); handles stop resolving

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginFontTableTests|FullyQualifiedName~BundledCanvasFontCacheTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** (`tests/AcDream.App.Tests/Plugins/PluginFontTableTests.cs`)

```csharp
using System.Collections.Generic;
using System.Threading;
using AcDream.App.Plugins;
using AcDream.App.Tests.UI;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// One plugin's fonts: counted once per distinct font, size and characters,
/// held as often as asked, refused past the budget with one report, and let
/// go when the interface goes.
/// </summary>
public sealed class PluginFontTableTests
{
    internal sealed class FakeFontBackend : IPluginFontBackend
    {
        public const uint FirstTexture = 900u;
        private uint _next = FirstTexture;
        public List<(uint Texture, int Width, int Height)> Uploaded { get; } = [];
        public List<uint> Released { get; } = [];

        public uint UploadCoverage(byte[] coverage, int width, int height, string debugName)
        {
            uint texture = _next++;
            Uploaded.Add((texture, width, height));
            return texture;
        }

        public bool ReleaseCoverage(uint texture)
        {
            Released.Add(texture);
            return true;
        }
    }

    private sealed class Rig
    {
        public FakeFontBackend Backend { get; } = new();
        public BundledCanvasFontCache Bundled { get; }
        public List<string> Reports { get; } = [];
        public PluginFontTable Table { get; }

        public Rig(PluginFontBudget? budget = null)
        {
            Bundled = new BundledCanvasFontCache(Backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);
            Table = new PluginFontTable("example.plugin", budget ?? PluginFontBudget.Default, Reports.Add);
            Table.Bind(Backend, Bundled, Environment.CurrentManagedThreadId);
        }
    }

    private static Func<Stream> Otf(Action? opened = null) => () =>
    {
        opened?.Invoke();
        return new MemoryStream(CanvasFontBakerTests.CffFixture());
    };

    [Fact]
    public void UnboundEveryRequestAnswersNoFont()
    {
        var table = new PluginFontTable("example.plugin", PluginFontBudget.Default);
        bool opened = false;

        Assert.Equal(PluginFont.None, table.AcquireBundled(16f));
        Assert.Equal(PluginFont.None, table.AcquireStream("f.otf", Otf(() => opened = true), 16f, null));
        Assert.False(opened);
        Assert.False(table.IsBound);
    }

    [Fact]
    public void TheSameBundledSizeIsOneHandleHeldTwice()
    {
        var rig = new Rig();

        PluginFont first = rig.Table.AcquireBundled(16f);
        PluginFont second = rig.Table.AcquireBundled(16f);

        Assert.True(first.IsValid);
        Assert.Equal(first, second);
        Assert.Equal(16f, first.PixelSize);
        Assert.True(first.LineHeight >= 16f);
        Assert.Equal(1, rig.Table.Count);
        Assert.Single(rig.Backend.Uploaded);
        Assert.True(rig.Table.Release(first));
        Assert.True(rig.Table.TryResolve(first, out _));
        Assert.True(rig.Table.Release(first));
        Assert.False(rig.Table.TryResolve(first, out _));
        Assert.False(rig.Table.Release(first));
        Assert.Equal(0L, rig.Table.OwnedBytes);
    }

    [Fact]
    public void APluginFontIsReadOnceCountedAndGivenBack()
    {
        var rig = new Rig();
        int opens = 0;

        PluginFont font = rig.Table.AcquireStream("fonts/fixture.otf", Otf(() => opens++), 16f, null);
        PluginFont again = rig.Table.AcquireStream("fonts/fixture.otf", Otf(() => opens++), 16f, null);

        Assert.True(font.IsValid);
        Assert.Equal(font, again);
        Assert.Equal(1, opens);
        (uint texture, int width, int height) = Assert.Single(rig.Backend.Uploaded);
        long expected = CanvasFontBakerTests.CffFixture().Length + (long)width * height;
        Assert.Equal(expected, rig.Table.OwnedBytes);

        rig.Table.Release(font);
        rig.Table.Release(font);

        Assert.Equal([texture], rig.Backend.Released);
        Assert.Equal(0L, rig.Table.OwnedBytes);
    }

    [Fact]
    public void ADifferentSizeOrRangeIsADifferentFont()
    {
        var rig = new Rig();

        PluginFont sixteen = rig.Table.AcquireStream("f.otf", Otf(), 16f, null);
        PluginFont twenty = rig.Table.AcquireStream("f.otf", Otf(), 20f, null);
        PluginFont capsOnly = rig.Table.AcquireStream("f.otf", Otf(), 16f, [new PluginCodepointRange('A', 'Z')]);

        Assert.Equal(3, new[] { sixteen.Handle, twenty.Handle, capsOnly.Handle }.Distinct().Count());
        Assert.Equal(3, rig.Table.Count);
    }

    [Theory]
    [InlineData(5.9f)]
    [InlineData(64.1f)]
    [InlineData(float.NaN)]
    public void ASizeOutsideTheBudgetIsRefusedAndReportedOnce(float size)
    {
        var rig = new Rig();

        Assert.Equal(PluginFont.None, rig.Table.AcquireBundled(size));
        Assert.Equal(PluginFont.None, rig.Table.AcquireBundled(size));

        Assert.Single(rig.Reports);
        Assert.Contains("6 to 64", rig.Reports[0]);
    }

    [Fact]
    public void TheSeventeenthFontIsRefused()
    {
        var rig = new Rig();
        for (int size = 6; size < 22; size++)
            Assert.True(rig.Table.AcquireBundled(size).IsValid);

        Assert.Equal(PluginFont.None, rig.Table.AcquireBundled(40f));
        Assert.Contains(rig.Reports, line => line.Contains("16 fonts"));
    }

    [Fact]
    public void AFontPastTheByteBudgetIsRefused()
    {
        var rig = new Rig(PluginFontBudget.Default with { MaximumBytes = 1024 });

        Assert.Equal(PluginFont.None, rig.Table.AcquireStream("f.otf", Otf(), 16f, null));
        Assert.Contains(rig.Reports, line => line.Contains("budget"));
        Assert.Empty(rig.Backend.Uploaded);
    }

    [Fact]
    public void StreamsThatFailAreRefusedNotThrown()
    {
        var rig = new Rig();

        Assert.Equal(PluginFont.None, rig.Table.AcquireStream("throws.ttf", () => throw new IOException("gone"), 16f, null));
        Assert.Equal(PluginFont.None, rig.Table.AcquireStream("null.ttf", () => null!, 16f, null));
        Assert.Equal(PluginFont.None, rig.Table.AcquireStream("junk.ttf", () => new MemoryStream(new byte[64]), 16f, null));
        Assert.Equal(PluginFont.None, rig.Table.AcquireStream("icons.otf", Otf(), 16f, [new PluginCodepointRange(0xE000, 0xE0FF)]));

        Assert.Equal(4, rig.Reports.Count);
        Assert.Contains(rig.Reports, line => line.Contains("gone"));
        Assert.Contains(rig.Reports, line => line.Contains("not a TrueType or OpenType font"));
        Assert.Contains(rig.Reports, line => line.Contains("none of the requested characters"));
    }

    [Fact]
    public void AnotherThreadIsRefusedWhileBound()
    {
        var rig = new Rig();
        Exception? failure = null;

        var worker = new Thread(() =>
        {
            try { rig.Table.AcquireBundled(16f); }
            catch (Exception caught) { failure = caught; }
        });
        worker.Start();
        worker.Join();

        Assert.IsType<InvalidOperationException>(failure);
    }

    [Fact]
    public void UnbindingLetsEveryFontGo()
    {
        var rig = new Rig();
        PluginFont bundled = rig.Table.AcquireBundled(16f);
        PluginFont own = rig.Table.AcquireStream("f.otf", Otf(), 16f, null);

        rig.Table.Unbind();

        Assert.False(rig.Table.TryResolve(bundled, out _));
        Assert.False(rig.Table.TryResolve(own, out _));
        Assert.Equal(0, rig.Table.Count);
        Assert.Equal(0, rig.Bundled.HeldCount);
        Assert.Equal(2, rig.Backend.Released.Count);
    }
}
```

`tests/AcDream.App.Tests/Plugins/BundledCanvasFontCacheTests.cs`:

```csharp
using AcDream.App.Plugins;
using AcDream.App.UI;

namespace AcDream.App.Tests.Plugins;

/// <summary>The bundled font is baked once per size for every plugin, and freed with its last holder.</summary>
public sealed class BundledCanvasFontCacheTests
{
    [Fact]
    public void OneBakePerSizeSharedUntilTheLastRelease()
    {
        var backend = new PluginFontTableTests.FakeFontBackend();
        using var cache = new BundledCanvasFontCache(backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);

        CanvasFont? first = cache.Acquire(16f, out _);
        CanvasFont? second = cache.Acquire(16f, out _);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Single(backend.Uploaded);
        cache.Release(first!);
        Assert.Empty(backend.Released);
        cache.Release(second!);
        Assert.Equal([PluginFontTableTests.FakeFontBackend.FirstTexture], backend.Released);
        Assert.Equal(0, cache.HeldCount);
    }

    [Fact]
    public void DisposingGivesBackWhatIsStillHeld()
    {
        var backend = new PluginFontTableTests.FakeFontBackend();
        var cache = new BundledCanvasFontCache(backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);
        cache.Acquire(16f, out _);
        cache.Acquire(20f, out _);

        cache.Dispose();

        Assert.Equal(2, backend.Released.Count);
        Assert.Null(cache.Acquire(16f, out string? failure));
        Assert.NotNull(failure);
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release`
Expected: compile errors — `PluginFontTable`, `PluginFontBudget`, `IPluginFontBackend`, `BundledCanvasFontCache` do not exist.

- [ ] **Step 3: Create `BundledCanvasFontCache.cs`**

```csharp
using AcDream.App.UI;

namespace AcDream.App.Plugins;

/// <summary>
/// The bundled font's bakes, shared by every plugin: one per size, uploaded
/// once, held while any plugin holds that size and given back on the last
/// release. Like client art, a shared bake costs no plugin anything against
/// its byte budget.
/// </summary>
internal sealed class BundledCanvasFontCache : IDisposable
{
    private readonly IPluginFontBackend _backend;
    private readonly Func<byte[]> _readFontBytes;
    private readonly int _maximumGlyphs;
    private readonly Dictionary<float, (CanvasFont Font, int Holds)> _bySize = [];
    private byte[]? _fontBytes;
    private bool _disposed;

    internal BundledCanvasFontCache(IPluginFontBackend backend, Func<byte[]> readFontBytes, int maximumGlyphs)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _readFontBytes = readFontBytes ?? throw new ArgumentNullException(nameof(readFontBytes));
        _maximumGlyphs = maximumGlyphs;
    }

    /// <summary>How many sizes are baked and held.</summary>
    internal int HeldCount => _bySize.Count;

    /// <summary>The bake for a size, made on the first request. Null, with a reason, when it cannot be made.</summary>
    internal CanvasFont? Acquire(float pixelSize, out string? failure)
    {
        if (_disposed)
        {
            failure = "the interface is going away";
            return null;
        }
        if (_bySize.TryGetValue(pixelSize, out (CanvasFont Font, int Holds) held))
        {
            _bySize[pixelSize] = (held.Font, held.Holds + 1);
            failure = null;
            return held.Font;
        }
        _fontBytes ??= _readFontBytes();
        if (!CanvasFontBaker.TryBake(
                _fontBytes, pixelSize, CanvasFontBaker.DefaultRanges, _maximumGlyphs,
                out CanvasFontBake? bake, out failure))
            return null;
        uint texture = _backend.UploadCoverage(
            bake.Coverage, bake.AtlasWidth, bake.AtlasHeight, $"plugin-font-bundled-{pixelSize}px");
        var font = new CanvasFont(bake, texture, _fontBytes);
        _bySize.Add(pixelSize, (font, 1));
        return font;
    }

    /// <summary>Lets go of one hold; the last one gives the texture back.</summary>
    internal void Release(CanvasFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (!_bySize.TryGetValue(font.PixelSize, out (CanvasFont Font, int Holds) held)
            || !ReferenceEquals(held.Font, font))
            return;
        if (held.Holds > 1)
        {
            _bySize[font.PixelSize] = (held.Font, held.Holds - 1);
            return;
        }
        _bySize.Remove(font.PixelSize);
        _backend.ReleaseCoverage(font.AtlasTexture);
        font.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach ((CanvasFont font, int _) in _bySize.Values)
        {
            _backend.ReleaseCoverage(font.AtlasTexture);
            font.Dispose();
        }
        _bySize.Clear();
    }
}
```

- [ ] **Step 4: Create `PluginFontTable.cs`**

```csharp
using System.Diagnostics.CodeAnalysis;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Plugins;

/// <summary>
/// How many fonts one plugin may hold and how much memory its own fonts may
/// take. <paramref name="MaximumBytes"/> counts the font files the plugin
/// supplied and the atlases baked from them; the bundled font is shared and
/// costs nothing against it.
/// </summary>
internal readonly record struct PluginFontBudget(
    int MaximumCount,
    long MaximumBytes,
    int MaximumGlyphs,
    float MinimumPixelSize,
    float MaximumPixelSize)
{
    /// <summary>
    /// 16 fonts, 16 MB of the plugin's own fonts, 2048 characters a font,
    /// 6 to 64 pixels: starting points, like the image budget, not
    /// measurements.
    /// </summary>
    public static PluginFontBudget Default { get; } = new(16, 16L * 1024 * 1024, 2048, 6f, 64f);
}

/// <summary>The texture services a font table draws on: glyph atlases in, and back out.</summary>
internal interface IPluginFontBackend
{
    /// <summary>Uploads a single-channel atlas. The handle comes back through <see cref="ReleaseCoverage"/>.</summary>
    uint UploadCoverage(byte[] coverage, int width, int height, string debugName);

    /// <summary>Gives back an atlas from <see cref="UploadCoverage"/>.</summary>
    bool ReleaseCoverage(uint texture);
}

/// <summary>
/// One plugin's fonts: every font it has asked for, counted once per distinct
/// font, size and set of characters, with how many times it is held. Bound
/// to the interface's texture services once those exist and unbound when the
/// interface goes away; unbound, every request answers
/// <see cref="PluginFont.None"/>. Bound, requests must come from the
/// interface thread, like images.
/// </summary>
internal sealed class PluginFontTable : IDisposable
{
    private readonly record struct Key(bool Bundled, string Name, float PixelSize, string Ranges);

    private sealed class Entry(Key key, int id, CanvasFont font, long ownedBytes)
    {
        internal Key Key { get; } = key;
        internal int Id { get; } = id;
        internal CanvasFont Font { get; } = font;

        /// <summary>Zero for the shared bundled font; file plus atlas for the plugin's own.</summary>
        internal long OwnedBytes { get; } = ownedBytes;

        internal int RefCount { get; set; } = 1;

        internal PluginFont Handle => new(Id, Key.PixelSize, Font.LineHeight, Font.Ascent);
    }

    private readonly string _ownerId;
    private readonly Action<string> _report;
    private readonly Dictionary<Key, Entry> _byKey = [];
    private readonly Dictionary<int, Entry> _byId = [];
    private readonly HashSet<string> _reported = [];
    private IPluginFontBackend? _backend;
    private BundledCanvasFontCache? _bundled;
    private int _uiThreadId;
    private int _nextId;
    private long _ownedBytes;
    private bool _disposed;

    internal PluginFontTable(string ownerId, PluginFontBudget budget, Action<string>? report = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget.MaximumCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget.MaximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget.MaximumGlyphs);
        _ownerId = ownerId;
        Budget = budget;
        _report = report ?? (line => Serilog.Log.Warning("{Line}", line));
    }

    internal PluginFontBudget Budget { get; }

    internal int Count => _byId.Count;

    internal long OwnedBytes => _ownedBytes;

    internal bool IsBound => _backend is not null;

    internal void Bind(IPluginFontBackend backend, BundledCanvasFontCache bundled, int uiThreadId)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(bundled);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_backend is not null)
            throw new InvalidOperationException("The font table is already bound.");
        _backend = backend;
        _bundled = bundled;
        _uiThreadId = uiThreadId;
    }

    /// <summary>Lets go of every font and forgets the services; held handles stop resolving.</summary>
    internal void Unbind()
    {
        if (_backend is null) return;
        Clear();
        _backend = null;
        _bundled = null;
    }

    internal PluginFont AcquireBundled(float pixelSize)
    {
        if (!TryEnter(out _, out BundledCanvasFontCache? bundled) || !IsSizeAllowed(pixelSize))
            return PluginFont.None;
        var key = new Key(Bundled: true, Name: "", pixelSize, Ranges: "");
        if (TryHoldAgain(key, out PluginFont again))
            return again;
        if (!HasRoomForOneMore($"the bundled font at {pixelSize} px"))
            return PluginFont.None;
        CanvasFont? font = bundled.Acquire(pixelSize, out string? failure);
        if (font is null)
        {
            ReportOnce($"bundled:{pixelSize}", $"the bundled font at {pixelSize} px could not be prepared: {failure}");
            return PluginFont.None;
        }
        return Add(key, font, ownedBytes: 0L).Handle;
    }

    internal PluginFont AcquireStream(
        string name, Func<Stream> open, float pixelSize, IReadOnlyList<PluginCodepointRange>? ranges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(open);
        if (!TryEnter(out IPluginFontBackend? backend, out _) || !IsSizeAllowed(pixelSize))
            return PluginFont.None;

        IReadOnlyList<(int First, int Last)> baked = ranges is null
            ? CanvasFontBaker.DefaultRanges
            : [.. ranges.Select(range => (range.First, range.Last))];
        var key = new Key(Bundled: false, name, pixelSize, string.Join(",", baked.Select(r => $"{r.First:X}-{r.Last:X}")));
        if (TryHoldAgain(key, out PluginFont again))
            return again;
        if (!HasRoomForOneMore($"font '{name}'"))
            return PluginFont.None;

        long room = Budget.MaximumBytes - _ownedBytes;
        byte[]? bytes;
        try
        {
            using Stream stream = open()
                ?? throw new InvalidOperationException("the stream factory returned null");
            bytes = ReadAtMost(stream, room);
        }
        catch (Exception failure)
        {
            ReportOnce($"read:{name}", $"font '{name}' could not be read: {failure.Message}");
            return PluginFont.None;
        }
        if (bytes is null)
        {
            ReportOnce($"bytes:{name}", $"font '{name}' would take the plugin's own fonts past "
                + $"the budget of {Budget.MaximumBytes:N0} bytes");
            return PluginFont.None;
        }

        if (!CanvasFontBaker.TryBake(bytes, pixelSize, baked, Budget.MaximumGlyphs, out CanvasFontBake? bake, out string? why))
        {
            ReportOnce($"bake:{key}", $"font '{name}' at {pixelSize} px could not be prepared: {why}");
            return PluginFont.None;
        }
        long owned = bytes.LongLength + (long)bake.AtlasWidth * bake.AtlasHeight;
        if (_ownedBytes + owned > Budget.MaximumBytes)
        {
            ReportOnce($"bytes:{key}", $"font '{name}' at {pixelSize} px would take the plugin's own fonts to "
                + $"{_ownedBytes + owned:N0} bytes; the budget is {Budget.MaximumBytes:N0}");
            return PluginFont.None;
        }

        uint texture = backend.UploadCoverage(
            bake.Coverage, bake.AtlasWidth, bake.AtlasHeight, $"plugin-{_ownerId}-font-{name}-{pixelSize}px");
        _ownedBytes += owned;
        return Add(key, new CanvasFont(bake, texture, bytes), owned).Handle;
    }

    /// <summary>
    /// Lets go of one hold; on the last, the plugin's own atlas goes back to
    /// the backend and a bundled hold goes back to the shared cache. False for
    /// a font this table did not issue or has already let go of completely.
    /// </summary>
    internal bool Release(PluginFont font)
    {
        if (!font.IsValid || _disposed) return false;
        ThrowIfWrongThread();
        if (!_byId.TryGetValue(font.Handle, out Entry? entry))
            return false;
        if (--entry.RefCount > 0)
            return true;
        Remove(entry);
        return true;
    }

    internal bool TryResolve(PluginFont font, [NotNullWhen(true)] out CanvasFont? resolved)
    {
        if (font.IsValid && !_disposed && _byId.TryGetValue(font.Handle, out Entry? entry))
        {
            resolved = entry.Font;
            return true;
        }
        resolved = null;
        return false;
    }

    internal void Clear()
    {
        foreach (Entry entry in _byId.Values.ToList())
            Remove(entry);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _backend = null;
        _bundled = null;
        _disposed = true;
    }

    private static byte[]? ReadAtMost(Stream stream, long limit)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81_920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > limit)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private bool TryEnter(
        [NotNullWhen(true)] out IPluginFontBackend? backend,
        [NotNullWhen(true)] out BundledCanvasFontCache? bundled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        backend = _backend;
        bundled = _bundled;
        if (backend is null || bundled is null)
            return false;
        ThrowIfWrongThread();
        return true;
    }

    private void ThrowIfWrongThread()
    {
        if (_backend is not null && Environment.CurrentManagedThreadId != _uiThreadId)
        {
            throw new InvalidOperationException(
                "Plugin fonts may only be acquired and released from the interface thread, "
                + "the thread the plugin's own callbacks run on.");
        }
    }

    private bool IsSizeAllowed(float pixelSize)
    {
        if (float.IsFinite(pixelSize)
            && pixelSize >= Budget.MinimumPixelSize
            && pixelSize <= Budget.MaximumPixelSize)
            return true;
        ReportOnce(
            $"size:{pixelSize}",
            $"a font size of {pixelSize} px was refused; sizes run from "
            + $"{Budget.MinimumPixelSize:0.##} to {Budget.MaximumPixelSize:0.##} px");
        return false;
    }

    private bool TryHoldAgain(Key key, out PluginFont handle)
    {
        if (_byKey.TryGetValue(key, out Entry? held))
        {
            held.RefCount++;
            handle = held.Handle;
            return true;
        }
        handle = PluginFont.None;
        return false;
    }

    private bool HasRoomForOneMore(string what)
    {
        if (_byId.Count < Budget.MaximumCount)
            return true;
        ReportOnce("count", $"{what} refused: the plugin already holds {Budget.MaximumCount} fonts, which is the most it may");
        return false;
    }

    private Entry Add(Key key, CanvasFont font, long ownedBytes)
    {
        var entry = new Entry(key, checked(++_nextId), font, ownedBytes);
        _byKey.Add(key, entry);
        _byId.Add(entry.Id, entry);
        return entry;
    }

    private void Remove(Entry entry)
    {
        _byKey.Remove(entry.Key);
        _byId.Remove(entry.Id);
        if (entry.Key.Bundled)
        {
            _bundled?.Release(entry.Font);
            return;
        }
        _ownedBytes -= entry.OwnedBytes;
        _backend?.ReleaseCoverage(entry.Font.AtlasTexture);
        entry.Font.Dispose();
    }

    private void ReportOnce(string key, string message)
    {
        if (_reported.Add(key))
            _report($"Plugin '{_ownerId}': {message}.");
    }
}
```

Note: the size message says `6 to 64` for the default budget, which the test checks.

- [ ] **Step 5: Run to verify**

Run: `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginFontTableTests|FullyQualifiedName~BundledCanvasFontCacheTests"`
Expected: `Passed!`. `TheSeventeenthFontIsRefused` bakes 16 sizes of Noto (~11 ms each): a few hundred ms is expected.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/Plugins/PluginFontTable.cs src/AcDream.App/Plugins/BundledCanvasFontCache.cs tests/AcDream.App.Tests/Plugins/PluginFontTableTests.cs tests/AcDream.App.Tests/Plugins/BundledCanvasFontCacheTests.cs
git commit -m "plugin fonts: one plugin's fonts, counted and budgeted; bundled bakes shared

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Plugins/PluginFontTable.cs", "src/AcDream.App/Plugins/BundledCanvasFontCache.cs", "tests/AcDream.App.Tests/Plugins/PluginFontTableTests.cs", "tests/AcDream.App.Tests/Plugins/BundledCanvasFontCacheTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~PluginFontTableTests|FullyQualifiedName~BundledCanvasFontCacheTests\"", "acceptanceCriteria": ["unbound answers None without opening", "same bundled size one handle, ref counted", "bundled bake shared across tables", "own font read once, bytes = file + atlas, released on last", "size/count/bytes/stream/not-font/empty-range refusals reported once", "wrong thread throws", "unbind releases all"], "modelTier": "standard"}
```

---

### Task 8: The graphical host's font surface

**Goal:** `BufferedUiRegistry` hands each plugin a `PluginFonts` surface, binds and unbinds font services with the interface, and the retail runtime wires a texture-cache backend.

**Files:**
- Create: `src/AcDream.App/Plugins/PluginFonts.cs`
- Create: `src/AcDream.App/UI/RetailPluginFontBackend.cs`
- Modify: `src/AcDream.App/Plugins/BufferedUiRegistry.cs` (after the image block, ~114-199; `Fonts` next to `Images` ~440)
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs:431-436` (bind) and `:5159` (unbind)
- Test: `tests/AcDream.App.Tests/Plugins/BufferedUiRegistryFontsTests.cs` (create)

**Acceptance Criteria:**
- [ ] Through `AppPluginHost`, `host.Ui.Fonts` is not the inert instance, reports the default budget, and is unavailable until `BindFontServices`; after binding `Bundled(16)` is valid; the same surface comes back on each read
- [ ] A surface made after binding is bound at once
- [ ] A surface first asked for on a worker thread answers only the binding thread
- [ ] Disposing a plugin's surface releases its atlases and the registry forgets it (a new surface is made next time)
- [ ] `UnbindFontServices` releases every plugin's fonts and the shared bundled bakes; `IsAvailable` goes false; binding twice throws
- [ ] The runtime binds right after image services and unbinds right after image services

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~BufferedUiRegistryFontsTests|FullyQualifiedName~BufferedUiRegistryImagesTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** (`tests/AcDream.App.Tests/Plugins/BufferedUiRegistryFontsTests.cs`)

```csharp
using System.Threading;
using AcDream.App.Plugins;
using AcDream.Core.Plugins;
using AcDream.Core.Selection;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// The graphical host's font surface, reached the way a plugin reaches it:
/// inert until the interface binds its texture services, live after, one
/// table per plugin, and let go when the plugin or the interface goes.
/// </summary>
public sealed class BufferedUiRegistryFontsTests
{
    private sealed class SilentLogger : IPluginLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? error = null) { }
    }

    private static (IPluginHost Host, BufferedUiRegistry Registry) Host()
    {
        var registry = new BufferedUiRegistry();
        var host = new AppPluginHost(
            new SilentLogger(),
            new WorldGameState(),
            new WorldEvents(),
            new SelectionState(),
            registry,
            NoOpAutomationSurface.Instance);
        return (host, registry);
    }

    [Fact]
    public void ThroughTheHostTheSurfaceIsInertUntilTheInterfaceBindsAndLiveAfter()
    {
        (IPluginHost host, BufferedUiRegistry registry) = Host();
        IPluginFonts fonts = host.Ui.Fonts;

        Assert.NotSame(NoOpPluginFonts.Instance, fonts);
        Assert.False(fonts.IsAvailable);
        Assert.Equal(PluginFont.None, fonts.Bundled(16f));
        Assert.Equal(PluginFontBudget.Default.MaximumCount, fonts.MaximumCount);
        Assert.Equal(PluginFontBudget.Default.MaximumBytes, fonts.MaximumBytes);
        Assert.Equal(PluginFontBudget.Default.MaximumGlyphs, fonts.MaximumGlyphs);
        Assert.Equal(6f, fonts.MinimumPixelSize);
        Assert.Equal(64f, fonts.MaximumPixelSize);

        registry.BindFontServices(new PluginFontTableTests.FakeFontBackend());

        Assert.True(fonts.IsAvailable);
        Assert.True(fonts.Bundled(16f).IsValid);
        Assert.Equal(1, fonts.Count);
        Assert.Same(fonts, host.Ui.Fonts);
    }

    [Fact]
    public void ASurfaceMadeAfterBindingIsBoundAtOnce()
    {
        (_, BufferedUiRegistry registry) = Host();
        registry.BindFontServices(new PluginFontTableTests.FakeFontBackend());

        IPluginFonts fonts = registry.FontsFor(new PluginUiOwner("late.plugin", "Late"));

        Assert.True(fonts.IsAvailable);
    }

    [Fact]
    public void ASurfaceFirstAskedForOnAWorkerStillAnswersOnlyTheInterfaceThread()
    {
        (_, BufferedUiRegistry registry) = Host();
        registry.BindFontServices(new PluginFontTableTests.FakeFontBackend());
        var owner = new PluginUiOwner("worker.plugin", "Worker");
        Exception? workerFailure = null;

        var worker = new Thread(() =>
        {
            try { registry.FontsFor(owner).Bundled(16f); }
            catch (Exception caught) { workerFailure = caught; }
        });
        worker.Start();
        worker.Join();

        Assert.IsType<InvalidOperationException>(workerFailure);
        Assert.True(registry.FontsFor(owner).Bundled(16f).IsValid);
    }

    [Fact]
    public void DisposingAPluginsSurfaceLetsItsFontsGoAndTheRegistryForgetsIt()
    {
        (_, BufferedUiRegistry registry) = Host();
        var backend = new PluginFontTableTests.FakeFontBackend();
        registry.BindFontServices(backend);
        var owner = new PluginUiOwner("example.plugin", "Example");
        IPluginFonts fonts = registry.FontsFor(owner);
        fonts.Bundled(16f);

        ((IDisposable)fonts).Dispose();

        Assert.Single(backend.Released);
        Assert.Null(registry.FindFonts(owner));
        Assert.NotSame(fonts, registry.FontsFor(owner));
    }

    [Fact]
    public void UnbindingLetsEveryPluginsFontsGo()
    {
        (_, BufferedUiRegistry registry) = Host();
        var backend = new PluginFontTableTests.FakeFontBackend();
        registry.BindFontServices(backend);
        IPluginFonts first = registry.FontsFor(new PluginUiOwner("a.plugin", "A"));
        IPluginFonts second = registry.FontsFor(new PluginUiOwner("b.plugin", "B"));
        first.Bundled(16f);
        second.Bundled(16f);
        second.Bundled(20f);

        registry.UnbindFontServices();

        Assert.False(first.IsAvailable);
        Assert.Equal(0, first.Count);
        Assert.Equal(2, backend.Released.Count);
        registry.UnbindFontServices();
        registry.BindFontServices(backend);
        Assert.Throws<InvalidOperationException>(() => registry.BindFontServices(backend));
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release`
Expected: compile errors — `BindFontServices`, `UnbindFontServices`, `FontsFor`, `FindFonts` do not exist on `BufferedUiRegistry`.

- [ ] **Step 3: Create `src/AcDream.App/Plugins/PluginFonts.cs`**

```csharp
using System.Diagnostics.CodeAnalysis;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Plugins;

/// <summary>
/// One plugin's font surface as the plugin sees it: the contract over its
/// <see cref="PluginFontTable"/>. Disposing it is how the plugin's fonts go
/// with the plugin -- the scoped registry tracks it -- and the registry that
/// made it forgets it, so a reloaded plugin starts with a fresh table.
/// </summary>
internal sealed class PluginFonts : IPluginFonts, IDisposable
{
    private readonly PluginFontTable _table;
    private readonly Action<PluginFonts> _forget;
    private bool _disposed;

    internal PluginFonts(PluginFontTable table, Action<PluginFonts> forget)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _forget = forget ?? throw new ArgumentNullException(nameof(forget));
    }

    internal PluginFontTable Table => _table;

    public bool IsAvailable => !_disposed && _table.IsBound;

    public PluginFont Bundled(float pixelSize) =>
        _disposed ? PluginFont.None : _table.AcquireBundled(pixelSize);

    public PluginFont FromStream(string name, Func<Stream> open, float pixelSize, PluginFontOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(open);
        return _disposed ? PluginFont.None : _table.AcquireStream(name, open, pixelSize, options?.Ranges);
    }

    public bool Release(PluginFont font) => !_disposed && _table.Release(font);

    public int Count => _disposed ? 0 : _table.Count;

    public int MaximumCount => _table.Budget.MaximumCount;

    public long MaximumBytes => _table.Budget.MaximumBytes;

    public int MaximumGlyphs => _table.Budget.MaximumGlyphs;

    public float MinimumPixelSize => _table.Budget.MinimumPixelSize;

    public float MaximumPixelSize => _table.Budget.MaximumPixelSize;

    /// <summary>The baked font behind a handle, for the painter.</summary>
    internal bool TryResolve(PluginFont font, [NotNullWhen(true)] out CanvasFont? resolved)
    {
        if (!_disposed)
            return _table.TryResolve(font, out resolved);
        resolved = null;
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _table.Dispose();
        _forget(this);
    }
}
```

- [ ] **Step 4: Registry wiring** (`BufferedUiRegistry.cs`, after `FindImages`)

```csharp
    // Font tables, one per plugin, following the image tables' lifecycle:
    // made on first request, bound when the interface's texture services
    // arrive, unbound when they go. The bundled font's bakes are shared by
    // every table and live exactly as long as the binding.
    private readonly Dictionary<string, PluginFonts> _fonts = [];
    private IPluginFontBackend? _fontBackend;
    private BundledCanvasFontCache? _bundledFonts;
    private int _fontUiThreadId;

    /// <summary>The per-plugin font ceiling every table is made with.</summary>
    internal PluginFontBudget FontBudget { get; init; } = PluginFontBudget.Default;

    public IPluginFonts FontsFor(PluginUiOwner owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.Id);
        lock (_gate)
        {
            if (_fonts.TryGetValue(owner.Id, out PluginFonts? existing))
                return existing;
            var table = new PluginFontTable(owner.Id, FontBudget);
            if (_fontBackend is { } backend && _bundledFonts is { } bundled)
                table.Bind(backend, bundled, _fontUiThreadId);
            var fonts = new PluginFonts(table, ForgetFonts);
            _fonts.Add(owner.Id, fonts);
            return fonts;
        }
    }

    private void ForgetFonts(PluginFonts fonts)
    {
        lock (_gate)
        {
            foreach ((string ownerId, PluginFonts held) in _fonts)
            {
                if (ReferenceEquals(held, fonts))
                {
                    _fonts.Remove(ownerId);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Points every plugin's font table, present and future, at the
    /// interface's texture services. The calling thread is the one the tables
    /// then accept requests from.
    /// </summary>
    internal void BindFontServices(IPluginFontBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        lock (_gate)
        {
            if (_fontBackend is not null)
                throw new InvalidOperationException("Font services are already bound.");
            _fontBackend = backend;
            _bundledFonts = new BundledCanvasFontCache(
                backend, AcDream.App.UI.BundledUiFont.ReadEmbeddedFontBytes, FontBudget.MaximumGlyphs);
            _fontUiThreadId = Environment.CurrentManagedThreadId;
            foreach (PluginFonts fonts in _fonts.Values)
                fonts.Table.Bind(backend, _bundledFonts, _fontUiThreadId);
        }
    }

    /// <summary>
    /// Lets every plugin's fonts go, then the shared bundled bakes, and
    /// forgets the services. Must run before the texture cache goes.
    /// Idempotent.
    /// </summary>
    internal void UnbindFontServices()
    {
        lock (_gate)
        {
            if (_fontBackend is null)
                return;
            foreach (PluginFonts fonts in _fonts.Values)
                fonts.Table.Unbind();
            _bundledFonts?.Dispose();
            _bundledFonts = null;
            _fontBackend = null;
        }
    }

    /// <summary>The font surface of one plugin, or null when none was made.</summary>
    internal PluginFonts? FindFonts(PluginUiOwner owner)
    {
        lock (_gate)
            return _fonts.GetValueOrDefault(owner.Id);
    }
```

Next to `public IPluginImages Images => …`:

```csharp
    public IPluginFonts Fonts =>
        FontsFor(new PluginUiOwner("unscoped", "Plugin"));
```

- [ ] **Step 5: The retail backend** (`src/AcDream.App/UI/RetailPluginFontBackend.cs`)

```csharp
using AcDream.App.Plugins;
using AcDream.App.Rendering;

namespace AcDream.App.UI;

/// <summary>
/// The interface's texture services as a font table sees them: glyph atlases
/// are uploaded as releasable single-channel textures and given back one at
/// a time, like a plugin's own images.
/// </summary>
internal sealed class RetailPluginFontBackend(TextureCache textures) : IPluginFontBackend
{
    private readonly TextureCache _textures = textures ?? throw new ArgumentNullException(nameof(textures));

    public uint UploadCoverage(byte[] coverage, int width, int height, string debugName) =>
        _textures.UploadReleasableCoverage8(coverage, width, height, debugName);

    public bool ReleaseCoverage(uint texture) => _textures.ReleaseUiTexture(texture);
}
```

- [ ] **Step 6: Wire the runtime** (`RetailUiRuntime.cs`)

After the `BindImageServices(...)` statement (~line 436):

```csharp
        bindings.Plugins?.BindFontServices(new RetailPluginFontBackend(bindings.Assets.TextureCache));
```

After `_bindings.Plugins?.UnbindImageServices();` (~line 5159):

```csharp
                // Likewise every plugin's glyph atlases and the shared
                // bundled bakes, while the texture cache is still here.
                _bindings.Plugins?.UnbindFontServices();
```

- [ ] **Step 7: Run to verify**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | tail -3 && dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~BufferedUiRegistryFontsTests|FullyQualifiedName~BufferedUiRegistryImagesTests|FullyQualifiedName~BufferedUiRegistryCanvasTests"`
Expected: `0 Warning(s)`, `Passed!`.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App/Plugins/PluginFonts.cs src/AcDream.App/Plugins/BufferedUiRegistry.cs src/AcDream.App/UI/RetailPluginFontBackend.cs src/AcDream.App/UI/RetailUiRuntime.cs tests/AcDream.App.Tests/Plugins/BufferedUiRegistryFontsTests.cs
git commit -m "plugin fonts: the graphical host hands each plugin a font surface bound to the interface

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Plugins/PluginFonts.cs", "src/AcDream.App/Plugins/BufferedUiRegistry.cs", "src/AcDream.App/UI/RetailPluginFontBackend.cs", "src/AcDream.App/UI/RetailUiRuntime.cs", "tests/AcDream.App.Tests/Plugins/BufferedUiRegistryFontsTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~BufferedUiRegistryFontsTests", "acceptanceCriteria": ["inert until bound, live after, same surface, default budget reported", "made-after-bind is bound", "worker-thread first ask answers only the interface thread", "dispose releases and forgets", "unbind releases all incl. bundled; double bind throws", "runtime binds/unbinds next to images"], "modelTier": "standard"}
```

---

### Task 9: The painter draws in plugin fonts

**Goal:** `PluginPainter.DrawText(…, font, …)` and `MeasureText(text, font)` resolve the handle through the plugin's font surface and draw/measure with `CanvasFont`; canvases get the plugin's fonts at mount.

**Files:**
- Modify: `src/AcDream.App/UI/Layout/PluginPainter.cs` (`Bind`, new members, resolver)
- Modify: `src/AcDream.App/UI/Layout/PluginCanvasSurface.cs:93-124` (`Repaint` takes fonts)
- Modify: `src/AcDream.App/UI/Layout/PluginCanvasElement.cs:71-114, 325` (fonts source)
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs:4203-4207` (pass `fonts:`)
- Test: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs`

**Acceptance Criteria:**
- [ ] A paint of fill → `DrawText("AV", …, font)` → fill produces runs with coverage `[None, atlas, None]`, the text run holding 12 vertices
- [ ] `MeasureText("AV", font)` inside the paint equals `(CanvasFont.MeasureWidth("AV"), LineHeight)` and `LineHeight == font.LineHeight`
- [ ] A released font draws nothing and measures `(0, 0)`
- [ ] A painter kept past its callback still throws on the new members
- [ ] Every existing `PluginCanvasElementTests` passes

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginCanvas"` → `Passed!`

**Steps:**

- [ ] **Step 1: Extend the test harness and write the failing tests**

In `PluginCanvasElementTests.Harness`'s constructor, after `Registry.BindImageServices(new FakeImageBackend());`:

```csharp
            Registry.BindFontServices(new AcDream.App.Tests.Plugins.PluginFontTableTests.FakeFontBackend());
```

In `Harness.Mount`, pass the fonts source to the element:

```csharp
            var element = new PluginCanvasElement(
                registration, Surface, () => Registry.FindImages(Owner), Reports.Add, Clock.Read,
                fonts: () => Registry.FindFonts(Owner));
```

Append the tests:

```csharp
    [Fact]
    public void TextInAPluginFontKeepsItsPlaceAndMeasuresAsItDraws()
    {
        var harness = new Harness();
        PluginFont font = harness.Registry.FontsFor(harness.Owner).Bundled(16f);
        Assert.True(font.IsValid);
        PluginSize measured = default;
        harness.Mount(Hud(), painter =>
        {
            painter.FillRect(new PluginRect(0, 0, 200, 100), new PluginColor(0, 0, 0, 160));
            painter.DrawText("AV", new PluginPoint(4, 4), PluginColor.White, font);
            painter.FillRect(new PluginRect(0, 50, 200, 10), PluginColor.White);
            measured = painter.MeasureText("AV", font);
        });

        harness.Frame();

        uint atlas = AcDream.App.Tests.Plugins.PluginFontTableTests.FakeFontBackend.FirstTexture;
        Assert.Equal([0u, atlas, 0u], harness.Surface.Renderer.DebugSpriteSegmentCoverage);
        Assert.Equal(12, harness.SurfaceRuns[1].VertexCount);
        Assert.True(harness.Registry.FindFonts(harness.Owner)!.TryResolve(font, out CanvasFont? resolved));
        Assert.Equal(new PluginSize(resolved.MeasureWidth("AV"), resolved.LineHeight), measured);
        Assert.Equal(font.LineHeight, (float)measured.Height);
    }

    [Fact]
    public void AReleasedFontDrawsNothingAndMeasuresNothing()
    {
        var harness = new Harness();
        IPluginFonts fonts = harness.Registry.FontsFor(harness.Owner);
        PluginFont font = fonts.Bundled(16f);
        fonts.Release(font);
        PluginSize measured = new(1, 1);
        harness.Mount(Hud(), painter =>
        {
            painter.DrawText("AV", new PluginPoint(4, 4), PluginColor.White, font);
            measured = painter.MeasureText("AV", font);
        });

        harness.Frame();

        Assert.Empty(harness.SurfaceRuns);
        Assert.Equal(default, measured);
    }

    [Fact]
    public void APainterKeptPastItsCallbackThrowsOnFontTextToo()
    {
        var harness = new Harness();
        PluginFont font = harness.Registry.FontsFor(harness.Owner).Bundled(16f);
        IPluginPainter? kept = null;
        harness.Mount(Hud(), painter => kept = painter);
        harness.Frame();

        Assert.Throws<InvalidOperationException>(() => kept!.DrawText("x", default, PluginColor.White, font));
        Assert.Throws<InvalidOperationException>(() => kept!.MeasureText("x", font));
    }
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release`
Expected: compile error — `PluginCanvasElement` has no `fonts` parameter.

- [ ] **Step 3: Painter** (`PluginPainter.cs`)

Field and `Bind`/`Unbind`:

```csharp
    private PluginFonts? _fonts;

    /// <summary>Points the painter at one repaint. Only the surface calls this.</summary>
    internal void Bind(
        UiRenderContext context, UiDatFont? font, PluginImages? images, int width, int height,
        PluginFonts? fonts = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _font = font;
        _images = images;
        _fonts = fonts;
        _width = width;
        _height = height;
    }

    /// <summary>Forgets the repaint; every call after this throws.</summary>
    internal void Unbind()
    {
        _context = null;
        _font = null;
        _images = null;
        _fonts = null;
    }
```

Members (after `MeasureText(string)`):

```csharp
    public void DrawText(string text, PluginPoint position, PluginColor color, PluginFont font, bool outline = false)
    {
        UiRenderContext context = Context;
        if (string.IsNullOrEmpty(text) || !TryResolve(font, out CanvasFont? resolved)) return;
        context.DrawStringCanvasFont(resolved, text, (float)position.X, (float)position.Y, ToVector(color), outline);
    }

    public PluginSize MeasureText(string text, PluginFont font)
    {
        _ = Context;
        if (string.IsNullOrEmpty(text) || !TryResolve(font, out CanvasFont? resolved)) return default;
        return new PluginSize(resolved.MeasureWidth(text), resolved.LineHeight);
    }
```

Resolver (next to the image `TryResolve`):

```csharp
    private bool TryResolve(PluginFont font, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CanvasFont? resolved)
    {
        resolved = null;
        return _fonts is not null && _fonts.TryResolve(font, out resolved);
    }
```

- [ ] **Step 4: Surface** (`PluginCanvasSurface.Repaint`)

Add a trailing parameter `PluginFonts? fonts = null` and bind with it:

```csharp
        _painter.Bind(_context, Font, images, width, height, fonts);
```

- [ ] **Step 5: Element** (`PluginCanvasElement`)

Field `private readonly Func<PluginFonts?> _fonts;`; constructor gains a trailing `Func<PluginFonts?>? fonts = null` with `_fonts = fonts ?? (static () => null);`; the repaint call becomes:

```csharp
        bool drew = _surface.Repaint(_registration, target.Target, _guard, _images(), _fonts());
```

- [ ] **Step 6: Runtime mount** (`RetailUiRuntime.MountPluginCanvases`)

```csharp
                var element = new Layout.PluginCanvasElement(
                    canvas,
                    _pluginCanvasSurface,
                    () => plugins.FindImages(owner),
                    modifiers: HeldPointerModifiers,
                    fonts: () => plugins.FindFonts(owner));
```

- [ ] **Step 7: Run to verify**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | tail -3 && dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~PluginCanvas"`
Expected: `0 Warning(s)`, `Passed!`.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App/UI/Layout/PluginPainter.cs src/AcDream.App/UI/Layout/PluginCanvasSurface.cs src/AcDream.App/UI/Layout/PluginCanvasElement.cs src/AcDream.App/UI/RetailUiRuntime.cs tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs
git commit -m "plugin canvas: text in a plugin font, measured as it is drawn

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/Layout/PluginPainter.cs", "src/AcDream.App/UI/Layout/PluginCanvasSurface.cs", "src/AcDream.App/UI/Layout/PluginCanvasElement.cs", "src/AcDream.App/UI/RetailUiRuntime.cs", "tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter FullyQualifiedName~PluginCanvas", "acceptanceCriteria": ["coverage runs [None,atlas,None], text 12 vertices", "MeasureText equals CanvasFont measure and line height", "released font draws and measures nothing", "kept painter throws on font members", "existing canvas tests green"], "modelTier": "standard"}
```

---

### Task 10: Documentation

**Goal:** Plugin authors can find and use fonts from the docs; the markup guide's test list names the new suites.

**Files:**
- Modify: `docs/plugin-api.md` (new `## Fonts` section between `## Images` and `## Canvases`; the Canvases primitives paragraph)
- Modify: `docs/plugin-ui-markup.md` (Tests section)

**Acceptance Criteria:**
- [ ] `## Fonts` documents `Bundled`, `FromStream` with `PluginFontOptions.Ranges`, the painter overloads, the budget numbers (16, 16 MB, 2048, 6–64 px), request-time preparation, thread rule, headless/teardown behaviour, and the `?` fallback
- [ ] The Canvases paragraph no longer says text has "one size"
- [ ] The Tests paragraph names `PluginFontsContractTests`, `ScopedUiRegistryFontsTests`, `PluginFontTableTests`, `BundledCanvasFontCacheTests`, `CanvasFontBakerTests`, `CanvasFontTests`, `BufferedUiRegistryFontsTests`
- [ ] `docfx` is not required locally; links are relative and headings unique

**Verify:** `grep -n "^## Fonts" docs/plugin-api.md && grep -c "PluginFontTableTests" docs/plugin-ui-markup.md` → one heading line and `1`

**Steps:**

- [ ] **Step 1: Add `## Fonts`** (immediately before `## Canvases`)

````markdown
## Fonts

Canvas text is drawn in the client's interface font unless the plugin
passes a font from `host.Ui.Fonts`. There are two sources: the client's
bundled sans-serif (Noto Sans) at any size, and a TrueType or OpenType font
the plugin ships.

```csharp
IPluginFonts fonts = host.Ui.Fonts;

PluginFont body  = fonts.Bundled(16);                         // Noto Sans, 16 px
PluginFont title = fonts.FromStream("fonts/Inter-Bold.ttf",    // the plugin's own font
    () => File.OpenRead(Path.Combine(pluginDirectory, "fonts", "Inter-Bold.ttf")), 22);
PluginFont icons = fonts.FromStream("fonts/MaterialSymbols.ttf",
    () => File.OpenRead(Path.Combine(pluginDirectory, "fonts", "MaterialSymbols.ttf")), 20,
    new PluginFontOptions { Ranges = [new PluginCodepointRange(0xE000, 0xF8FF)] });

// in a paint callback:
painter.DrawText("Golem", new PluginPoint(8, 6), PluginColor.White, title);
PluginSize size = painter.MeasureText("Golem", title);   // width with kerning, and title.LineHeight
painter.DrawText("", new PluginPoint(8, 34), PluginColor.White, icons);

fonts.Release(title);
```

Each size of each font is its own `PluginFont`, carrying its `PixelSize`,
`LineHeight` and `Ascent` (how far below the top of a line the baseline
sits) so text in different fonts can share a baseline. Preparing a font
takes some milliseconds and happens when it is asked for, never while a
canvas paints: ask for fonts up front, not inside the paint callback.

`FromStream` prepares the characters named in `PluginFontOptions.Ranges`,
or by default U+0020–U+024F, U+0370–U+052F and U+2000–U+206F. Only the
characters the font actually has are prepared and counted, so an icon font
can name the whole private-use area. A character that was not prepared
draws as the font's `?` if that was prepared, and as nothing otherwise.

Requests are counted like images: asking twice for the same font, size and
characters returns the same `PluginFont`, and it takes two releases to let
it go. A plugin may hold at most `MaximumCount` fonts (16), sized from
`MinimumPixelSize` to `MaximumPixelSize` (6 to 64 px), each preparing at
most `MaximumGlyphs` characters (2048); its own fonts may take at most
`MaximumBytes` (16 MB), counting the font files and the glyph textures made
from them. The bundled font is shared with every plugin and costs nothing
against the byte budget. A request past any limit, a stream that cannot be
read, or a file that is not a font answers `PluginFont.None` and is
reported once in the client's log; text drawn with an invalid or released
font draws nothing and measures `(0, 0)`.

Call all of this from the tick thread. Without a window, or before the
client's interface is up, `IsAvailable` is false and every request answers
`PluginFont.None`; fonts are dropped when the interface is torn down, after
which the plugin asks again.
````

- [ ] **Step 2: Canvases paragraph**

In `## Canvases`, replace ``are `Clear`, `FillRect`, `StrokeRect`, `DrawLine`, `DrawText` with
`MeasureText` (the client's own interface font, one size), `DrawImage`,`` with:

```markdown
are `Clear`, `FillRect`, `StrokeRect`, `DrawLine`, `DrawText` with
`MeasureText` (the client's interface font, or a font from
[Fonts](#fonts)), `DrawImage`,
```

- [ ] **Step 3: Test list** (`docs/plugin-ui-markup.md`, Tests section)

After the sentence ending ``…and by the contract and headless suites for the inert answers a host without a window gives.``, add:

```markdown
Canvas fonts are covered by `CanvasFontBakerTests`, `CanvasFontTests`,
`PluginFontTableTests`, `BundledCanvasFontCacheTests` and
`BufferedUiRegistryFontsTests` under `tests/AcDream.App.Tests/`, by
`ScopedUiRegistryFontsTests` for the scoped forwarder, and by
`PluginFontsContractTests` and the headless suite for the inert answers.
```

- [ ] **Step 4: Commit**

```bash
git add docs/plugin-api.md docs/plugin-ui-markup.md
git commit -m "docs: canvas fonts

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["docs/plugin-api.md", "docs/plugin-ui-markup.md"], "verifyCommand": "grep -n \"^## Fonts\" docs/plugin-api.md", "acceptanceCriteria": ["Fonts section with sources, options, painter overloads, budgets, timing, thread, headless, fallback", "Canvases paragraph updated", "test list names the seven suites"], "modelTier": "mechanical"}
```

---

### Task 11: Full verification and push

**Goal:** Green build, green portable gate and Vulkan lane, branch pushed, PR description drafted.

**Files:** none

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` reports 0 warnings, 0 errors
- [ ] Portable filter: every assembly `Passed!`
- [ ] `Lane=Vulkan` on `AcDream.App.Tests`: `Passed!` under MoltenVK
- [ ] No shader file changed against `main`
- [ ] Branch pushed; PR opened only after the user picks the repository

**Verify:** the commands below with the stated output

**Steps:**

- [ ] **Step 1: Build, gate, Vulkan lane**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | tail -4
dotnet test AcDream.slnx -c Release --no-build --filter "Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure" 2>&1 | grep -E "Passed!|Failed!"
DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --no-build --filter "Lane=Vulkan" 2>&1 | grep -E "Passed!|Failed!"
git diff main --stat -- src/AcDream.App/Rendering/Shaders/ | wc -l
```

Expected: `0 Warning(s)`, all `Passed!`, and `0`. An unrelated failure is compared against `main` and reported, not fixed here.

- [ ] **Step 2: Push**

```bash
git push -u origin painter-v2/fonts
```

- [ ] **Step 3: Draft the PR description, then ask where to open it**

```markdown
## Plugin API: canvas fonts

Plugins can draw canvas text in the bundled Noto Sans at a size they choose,
or in a `.ttf`/`.otf` they ship, through `host.Ui.Fonts`.

- Contract (additive, default members): `IPluginFonts` (`Bundled`, `FromStream`,
  `Release`, budgets), `PluginFont`, `PluginCodepointRange`, `PluginFontOptions`,
  `NoOpPluginFonts`; `IUiRegistry.Fonts`, `IScopedUiRegistry.FontsFor`;
  `IPluginPainter.DrawText(…, PluginFont, …)` and `MeasureText(string, PluginFont)`,
  whose defaults fall back to the interface font on older hosts.
- Host: fonts are baked at request time (never in a paint) with StbTrueType's
  pack API into single-channel atlases, drawn through the UI shader's existing
  coverage branch in the sprite runs (painter's order kept). Only characters the
  font has are packed. Bundled bakes are shared across plugins.
- Budget per plugin: 16 fonts, 16 MB of own font files + atlases, 2048 glyphs a
  font, 6–64 px, atlases ≤ 2048². Refusals answer `PluginFont.None` and are logged once.
- Headless: inert (`NoOpPluginFonts`).
- No shader changes. `BundledUiFont` unchanged apart from exposing its bytes.
- Tests: contract, scoped forwarder, headless, baker (incl. a 1.4 KB OFL CFF
  fixture), font metrics/kerning, table/budget, registry binding, painter integration.

API change: `IPluginFonts` and related types; `IUiRegistry.Fonts`,
`IScopedUiRegistry.FontsFor`; two `IPluginPainter` overloads.

https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB
```

Ask the user whether to open it against `origin` or upstream, and whether PR 0 has merged (if not, the PR targets `painter-v2/canvas-alpha` or waits).

```json:metadata
{"files": [], "verifyCommand": "dotnet build AcDream.slnx -c Release", "acceptanceCriteria": ["0 warnings 0 errors", "portable gate green", "Vulkan lane green on MoltenVK", "no shader changes", "pushed; PR target asked"], "modelTier": "mechanical"}
```

---

## Later PRs

PR 2 (image regions) through PR 6b (keyboard) each get their own plan, written against `main` once the PRs before them have landed, so they can cite the code as it then is. Order and dependencies are in the spec's "Order of work".
