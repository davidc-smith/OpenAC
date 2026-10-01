# Painter v2 — PR 4: HiDPI Sharpness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On a high-density display (a Retina Mac, Windows at 150 %), plugin canvases are painted at the display's resolution, so shapes and text in plugin fonts are sharp instead of magnified, with no change to how a plugin lays anything out.

**Architecture:** Each frame the canvas element computes a pixel scale: framebuffer pixels per window point, times the pre-game fixed canvas's stretch, rounded up to a quarter and clamped to [1, 4], then lowered if the canvas would exceed the device's largest texture. Targets are allocated at that multiple of the canvas size. The surface begins its renderer at the target size with `TextRenderer.CanvasScale = s`, while the context, the clip and the painter stay in canvas pixels, as `UiRoot` does for the fixed canvas. A scale change gives every target back, invalidates and repaints. Plugin fonts get a second, sharper bake at `size × s`, made before the paint and outside the guard. The pen, kerning and line metrics still come from the font's own bake, so nothing measures differently. A font too large for an atlas at `s` steps down a quarter at a time. The plugin's own sharper bakes have a host-side budget separate from `MaximumBytes`. Bundled sharper bakes are shared like bundled bakes. `IPluginPainter.PixelScale` reports the scale. No shader changes.

**Tech Stack:** C# / .NET 10, `System.Numerics`, StbTrueTypeSharp 1.26.12, xUnit, the repository's `RecordingGpuDevice`, and MoltenVK for the local `Lane=Vulkan` run.

**Spec:** `docs/superpowers/specs/2026-09-30-plugin-painter-v2-design.md` (section 7, "Inputs for the PR 4 (HiDPI) plan" under "Status and carry-forward", and "Plan-time corrections", whose PR 4 entries supersede section 7 where they differ).

**Checked:** this plan's code was applied to fork `main` (44a505ee) in a throwaway worktree before hand-off. The states after Task 5 and after Task 8 were replayed separately in a fresh worktree and each built and passed on its own. Every "replace this" anchor was checked to exist verbatim in `44a505ee`, or, for Task 9, in Task 5's result. It built with 0 warnings, and every new test passed, along with the full portable suite (apart from the baseline failures listed under Global Constraints). The `Lane=Vulkan` canvas tests passed under MoltenVK. A deliberate regression (fringe at one canvas pixel) failed the new Vulkan test as intended. The demo canvas was then captured on the built-in Retina display at 2560×1440 before and after: text, shape edges and lines were visibly sharp after, and the interface font was crisply pixel-doubled. The atlas sizes, timings and messages in the tests are measured values.

## Global Constraints

- Branch `painter-v2/hidpi`, created from the fork's `main` at or after `44a505ee` (PR 0, PR 1 and PR 3 merged). PR 4 depends on PR 1 (fonts) and PR 3 (`PluginPainter.DevicePixel`); fork `main` is exactly those merged. Nothing under `docs/superpowers/` goes on a code branch.
- `AcDream.Plugin.Abstractions` stays additive. The only new member is `double PixelScale => 1.0;` on `IPluginPainter`. Every public member has XML docs; an undocumented member fails the build.
- Exact values: scale step 0.25, minimum 1, maximum 4, rounded **up**, with a tolerance of 1e-3 steps, so 2.0000002 is 2. `ForCanvas` lowers by 0.25 until `ceil(W·s)` and `ceil(H·s)` are both ≤ `Capabilities.MaxImageDimension2D`, and never below 1. The device pixel is `1 / s`. Sharper bakes step down from `s` by 0.25 while above 1. `PluginFontBudget.MaximumSharpBytes` is 32 MiB. Font sizes are rounded to the nearest quarter pixel (midpoint away from zero) **after** the min/max check.
- At a pixel scale of 1 everything is exactly as before. Every existing test passes unmodified, except the harness changes this plan makes.
- Font metrics are logical and unchanged by any rebake. `PluginFont.LineHeight`/`Ascent` and `MeasureText` come from the font's own bake. Glyph boxes come from the sharper bake divided by its scale.
- Expensive work (baking) runs outside the paint guard; the 4 ms paint budget is unchanged.
- No shader source or SPIR-V changes.
- Commit subjects: `plugin api: …`, `plugin canvas: …`, `plugin fonts: …`, `samples: …`, `tests: …`, `docs: …`. Every commit message ends with `Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB`.
- Environment for every command: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. `Lane=Vulkan` commands also need `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib`.
- Tests run in the machine's culture, and this Mac uses a decimal comma. Any report text that contains a number is built with `CultureInfo.InvariantCulture` or `FormattableString.Invariant`.
- Builds on this Mac rewrite `src/AcDream.Launcher/packages.neutral.lock.json`, and an unlocked restore touches `packages.osx-arm64.lock.json` files. Never stage a modified `packages.*.lock.json`. The **new** `samples/AcDream.Plugins.CanvasDemo/packages.neutral.lock.json` is staged once, in Task 1. Stage files by explicit path, and run `git checkout -- '*.lock.json'` before pushing.
- The first solution build in a fresh worktree can fail once with `NETSDK1047 … AcDream.Bake/obj/project.assets.json doesn't have a target for 'net10.0/osx-arm64'`, a restore race. Build again; the second build is clean.
- Portable gate filter: `Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure`.
- Baseline failures on this Mac, which also fail on clean `44a505ee` and are not this PR's: run the full portable suite with `TMPDIR=/tmp/`. The default temp path makes Unix-socket paths longer than 104 characters, which fails `AcDream.Launcher.Tests` (2) and many `AcDream.HostParity.Tests` peer tests. Even with `TMPDIR=/tmp/`, 4–5 `AcDream.HostParity.Tests` `Peer*ParityTests` time out ("IPC parity condition was not observed"). Record the baseline set in Task 0 and compare against it; do not try to fix them here.
- A local probe session runs the graphical client. That needs `DYLD_LIBRARY_PATH=/opt/homebrew/lib`, `VK_DRIVER_FILES=/opt/homebrew/etc/vulkan/icd.d/MoltenVK_icd.json`, `ACDREAM_DAT_DIR=$HOME/AsheronsCall`, and `ACDREAM_PAK_PATH="$HOME/Library/Application Support/OpenAC/data/pak/acdream.pak"`. It also needs `ACDREAM_ROOT_DIR` pointed at a scratch install folder whose `plugins/` holds only the demo, never the user's real plugins folder.

**User decisions (already made):**
- "One spec, a PR series"; this is PR 4 (HiDPI), discussed and agreed 2026-09-30/10-01.
- Base: branch from fork `main` ("agree 1").
- Large fonts: "try the next lower scale step first, then fall back to 1×", with sharper bakes counted against "a separate host-side cap rather than the plugin's MaximumBytes" ("2b").
- Bundled cache keyed by (rounded size, scale), one face per file; the sfnt hardening stays out of PR 4 ("4").
- A scale change is "read during paint": no event, `PixelScale` is read in the paint callback.
- "add demo canvas": a sample plugin whose canvas is the subject of before/after screenshots on a Retina Mac, the acceptance gate.
- The Retina captures are made by the user dragging the client window onto the built-in display ("I'll drag the window"). The main display is a 1× QHD monitor.
- Branches are pushed to `origin`. Do not open a PR without asking which repository it goes to.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `samples/AcDream.Plugins.CanvasDemo/*` | Create | Demo plugin: one centred 360×220 canvas with every kind of content. |
| `AcDream.slnx` | Modify | Lists the sample. |
| `src/AcDream.Plugin.Abstractions/PluginCanvas.cs` | Modify | `IPluginPainter.PixelScale`; "screen pixel" wording. |
| `tests/AcDream.Plugin.Tests/PluginCanvasPixelScaleContractTests.cs` | Create | Inert default. |
| `src/AcDream.App/UI/Layout/CanvasPixelScale.cs` | Create | The scale rule: interface scale, per-canvas clamp, device size. |
| `tests/AcDream.App.Tests/UI/Layout/CanvasPixelScaleTests.cs` | Create | |
| `src/AcDream.App/UI/Layout/PluginCanvasSurface.cs` | Modify | `FramebufferPerPoint` replaces the linear-twin resolver; repaint at a scale. |
| `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs` | Modify | Supplies framebuffer ÷ window size. |
| `src/AcDream.App/UI/Layout/PluginPainter.cs` | Modify | `PixelScale`, device pixel `1/s`, text at the scale. |
| `src/AcDream.App/UI/Layout/PluginCanvasElement.cs` | Modify | Follows the scale; targets at device size; prepares fonts before paint. |
| `tests/AcDream.App.Tests/Rendering/Gpu/RecordingGpuDevice.cs` | Modify | `DefaultCapabilities`, so a test changes one field. |
| `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs` | Modify | Harness: fake framebuffer scale, device limit, font backend, input mount. |
| `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.HiDpi.cs` | Create | |
| `src/AcDream.App/UI/CanvasFontBaker.cs` | Modify | Area lower bound, atlas byte cap, `TryBakeForScale`. |
| `tests/AcDream.App.Tests/UI/CanvasFontBakerTests.cs` | Modify | |
| `src/AcDream.App/UI/CanvasFont.cs` | Rewrite | `CanvasFontFace`, `CanvasSharpGlyphs`, `CanvasFont.Sharp`/`PrepareSharp`. |
| `src/AcDream.App/UI/UiRenderContext.cs` | Modify | Canvas-font text from the sharper bake, snapped to device pixels. |
| `tests/AcDream.App.Tests/UI/CanvasFontTests.cs` | Modify | |
| `src/AcDream.App/Plugins/CanvasFontSharpening.cs` | Create | Bake, upload, swap and give back a font's sharper bake. |
| `src/AcDream.App/Plugins/BundledCanvasFontCache.cs` | Modify | One face; shared sharper bakes. |
| `src/AcDream.App/Plugins/PluginFontTable.cs` | Modify | `PrepareScale`, `MaximumSharpBytes`, quarter-pixel sizes, ranges per entry. |
| `src/AcDream.App/Plugins/PluginFonts.cs` | Modify | `PrepareScale` forwarder. |
| `src/AcDream.Plugin.Abstractions/PluginFonts.cs` | Modify | Quarter-pixel wording. |
| `tests/AcDream.App.Tests/Plugins/BundledCanvasFontCacheTests.cs` | Modify | |
| `tests/AcDream.App.Tests/Plugins/PluginFontTableScaleTests.cs` | Create | |
| `tests/AcDream.App.Tests/Rendering/Gpu/Vk/PluginCanvasHiDpiOffscreenTests.cs` | Create | `Lane=Vulkan` proof of a one-device-pixel fringe at 2×. |
| `docs/plugin-api.md`, `docs/plugin-ui-markup.md` | Modify | `### High-density displays`; wording; test list. |

Capture tooling for the screenshots lives in `/tmp/openac-hidpi/` and never enters the repository (Task 2).

---

### Task 0: Create the branch and record the baseline

**Goal:** `painter-v2/hidpi` from fork `main`, building cleanly, with the baseline test failures written down.

**Files:** none

**Acceptance Criteria:**
- [ ] `git branch --show-current` prints `painter-v2/hidpi`
- [ ] `git merge-base --is-ancestor 44a505ee HEAD` succeeds (PR 0, 1 and 3 present)
- [ ] `docs/superpowers` does not exist in the working tree
- [ ] `dotnet build AcDream.slnx -c Release` reports `0 Warning(s)` and `0 Error(s)`
- [ ] The names of the portable-suite failures (run with `TMPDIR=/tmp/`) are saved to `/tmp/openac-hidpi/baseline-failures.txt`

**Verify:** `git branch --show-current && git merge-base --is-ancestor 44a505ee HEAD && test ! -e docs/superpowers && echo ok` → `painter-v2/hidpi`, `ok`

**Steps:**

- [ ] **Step 1: Branch**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git fetch origin
git switch -c painter-v2/hidpi origin/main
git merge-base --is-ancestor 44a505ee HEAD && test ! -e docs/superpowers && echo ok
```

Expected: `ok`.

- [ ] **Step 2: Baseline build and tests**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
mkdir -p /tmp/openac-hidpi
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test AcDream.slnx -c Release --no-build --filter "Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure" 2>&1 \
  | grep -E "^\s+Failed " | sed -E 's/ \[[^]]*\]$//' | sort -u > /tmp/openac-hidpi/baseline-failures.txt
cat /tmp/openac-hidpi/baseline-failures.txt
git checkout -- '*.lock.json'
```

Expected: `0 Warning(s)`, `0 Error(s)`. The failures listed are only `AcDream.HostParity.Tests.Peer*ParityTests` (4–5 of them). The unit tests must run outside the command sandbox, or the peer and pipe tests fail on socket permissions too.

```json:metadata
{"files": [], "verifyCommand": "git branch --show-current", "acceptanceCriteria": ["on painter-v2/hidpi", "contains 44a505ee", "no docs/superpowers", "0 warnings", "baseline failures recorded"], "modelTier": "mechanical"}
```

---

### Task 1: A demo canvas to look at

**Goal:** A sample gameplay plugin with one centred 360×220 canvas showing the interface font, the bundled font at 13 and 20 px, anti-aliased shapes, a gradient, hard-edged lines and a magnified image. It is the subject of the before/after screenshots and needs nothing from PR 4.

**Files:**
- Create: `samples/AcDream.Plugins.CanvasDemo/AcDream.Plugins.CanvasDemo.csproj`
- Create: `samples/AcDream.Plugins.CanvasDemo/plugin.json`
- Create: `samples/AcDream.Plugins.CanvasDemo/CanvasDemoPlugin.cs`
- Create (generated by restore): `samples/AcDream.Plugins.CanvasDemo/packages.neutral.lock.json`
- Modify: `AcDream.slnx:18-21` (the `/samples/` folder)

**Acceptance Criteria:**
- [ ] `dotnet build samples/AcDream.Plugins.CanvasDemo -c Release` reports `0 Warning(s)`
- [ ] The output folder holds `AcDream.Plugins.CanvasDemo.dll`, `.deps.json` and `plugin.json`
- [ ] An offline client run with the demo in a scratch root logs `plugin canvas mounted: sample.canvas-demo/demo (360x220)`

**Verify:** `grep -c "plugin canvas mounted: sample.canvas-demo/demo (360x220)" /tmp/openac-hidpi/run-smoke.log` → `1`

**Steps:**

- [ ] **Step 1: The project** (`samples/AcDream.Plugins.CanvasDemo/AcDream.Plugins.CanvasDemo.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <!-- The only acdream dependency available to an external plugin. -->
    <ProjectReference Include="..\..\src\AcDream.Plugin.Abstractions\AcDream.Plugin.Abstractions.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
  </ItemGroup>
  <ItemGroup>
    <None Include="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: The manifest** (`samples/AcDream.Plugins.CanvasDemo/plugin.json`)

```json
{
  "id": "sample.canvas-demo",
  "displayName": "Canvas Demo Sample",
  "version": "1.0.0",
  "entryDll": "AcDream.Plugins.CanvasDemo.dll",
  "apiVersion": 1,
  "kinds": ["gameplay"],
  "hosts": ["graphical"]
}
```

- [ ] **Step 3: The plugin** (`samples/AcDream.Plugins.CanvasDemo/CanvasDemoPlugin.cs`)

Fonts and images answer "none" until the interface is up, and may not be asked for while painting, so the plugin asks on the first tick that has them. The image is an 8×8 BMP built in code, so the sample ships no binary file.

```csharp
using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.CanvasDemo;

/// <summary>
/// One canvas in the middle of the screen with a sample of everything a
/// painter draws: the interface font, the bundled font at two sizes, shapes
/// with anti-aliased edges, hard-edged lines and an image. It exists to be
/// looked at -- for example to compare how sharp a canvas is on a
/// high-density display -- and does nothing else.
/// </summary>
public sealed class CanvasDemoPlugin : IAcDreamPlugin
{
    private const int CanvasWidth = 360;
    private const int CanvasHeight = 220;

    private static readonly PluginColor Panel = new(16, 20, 28, 220);
    private static readonly PluginColor Accent = new(90, 170, 255);
    private static readonly PluginColor Warm = new(255, 140, 60);

    private IPluginHost? _host;
    private IPluginCanvas? _canvas;
    private PluginFont _body = PluginFont.None;
    private PluginFont _title = PluginFont.None;
    private PluginImage _checker = PluginImage.None;

    public void Initialize(IPluginHost host) => _host = host;

    public void Enable()
    {
        if (_host is not { } host) return;
        _canvas = host.Ui.RegisterCanvas(
            new PluginCanvasDescriptor("demo", CanvasWidth, CanvasHeight) { Anchor = PluginCanvasAnchor.Center },
            Paint);
        host.Events.Tick += OnTick;
    }

    public void Disable()
    {
        if (_host is { } host)
            host.Events.Tick -= OnTick;
        _canvas?.Dispose();
        _canvas = null;
    }

    /// <summary>Fonts and images can only be asked for once the interface is up, and never while painting.</summary>
    private void OnTick(double deltaSeconds)
    {
        if (_host is not { } host) return;
        bool changed = false;
        if (!_title.IsValid && host.Ui.Fonts.IsAvailable)
        {
            _title = host.Ui.Fonts.Bundled(20);
            _body = host.Ui.Fonts.Bundled(13);
            changed = true;
        }
        if (!_checker.IsValid && host.Ui.Images.IsAvailable)
        {
            _checker = host.Ui.Images.FromStream("checker.bmp", () => new MemoryStream(Checkerboard()));
            changed = true;
        }
        if (changed)
            _canvas?.Invalidate();
    }

    private void Paint(IPluginPainter painter)
    {
        painter.Clear(PluginColor.Transparent);
        var bounds = new PluginRect(0, 0, painter.Width, painter.Height);
        painter.FillRoundedRect(bounds, PluginCornerRadii.Uniform(10), Panel);
        painter.StrokeRoundedRect(new PluginRect(0.5, 0.5, painter.Width - 1, painter.Height - 1), PluginCornerRadii.Uniform(10), Accent, 1f);

        painter.DrawText("Noto Sans 20 px: Sharp canvases", new PluginPoint(14, 10), PluginColor.White, _title);
        painter.DrawText("Noto Sans 13 px: The quick brown fox jumps over 0123456789", new PluginPoint(14, 38), PluginColor.White, _body);
        painter.DrawText("Interface font: The quick brown fox jumps", new PluginPoint(14, 60), PluginColor.White);

        // Shapes: anti-aliased edges.
        painter.FillCircle(new PluginPoint(34, 110), 18, Accent);
        painter.StrokeEllipse(new PluginRect(62, 92, 56, 36), PluginColor.White, 1f);
        painter.FillPolygon([new PluginPoint(134, 128), new PluginPoint(156, 90), new PluginPoint(178, 128)], Warm);
        painter.FillRoundedRect(new PluginRect(192, 92, 60, 36), PluginCornerRadii.Uniform(8), new PluginColor(60, 200, 90));
        painter.FillRectGradient(
            new PluginRect(14, 142, 238, 12), Accent, new PluginColor(Accent.R, Accent.G, Accent.B, 0), PluginGradientDirection.Horizontal);

        // Hard-edged lines, straight and slanted.
        painter.DrawLine(new PluginPoint(14, 168), new PluginPoint(252, 168), PluginColor.White, 1f);
        painter.DrawLine(new PluginPoint(14, 200), new PluginPoint(252, 176), PluginColor.White, 1f);

        // An 8 x 8 image shown at 64 x 64: how the host samples art.
        painter.DrawImage(_checker, new PluginRect(274, 92, 64, 64), PluginColor.White);
        painter.DrawText("8x8 at 64", new PluginPoint(274, 160), new PluginColor(200, 200, 200), _body);
    }

    /// <summary>An 8 x 8 checkerboard as a 32-bit BMP, so the sample ships no binary files.</summary>
    private static byte[] Checkerboard()
    {
        const int size = 8;
        const int pixelBytes = size * size * 4;
        const int headerBytes = 14 + 40;
        var bmp = new byte[headerBytes + pixelBytes];
        using var writer = new BinaryWriter(new MemoryStream(bmp));
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(bmp.Length);
        writer.Write(0);
        writer.Write(headerBytes);
        writer.Write(40);
        writer.Write(size);
        writer.Write(size);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(0);
        writer.Write(pixelBytes);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool light = (x + y) % 2 == 0;
                writer.Write(light ? (byte)230 : (byte)40); // blue
                writer.Write(light ? (byte)230 : (byte)40); // green
                writer.Write(light ? (byte)230 : (byte)200); // red
                writer.Write((byte)255);
            }
        }
        return bmp;
    }
}
```

- [ ] **Step 4: List it in the solution**

In `AcDream.slnx`, inside `<Folder Name="/samples/">`, add as its first line:

```xml
    <Project Path="samples/AcDream.Plugins.CanvasDemo/AcDream.Plugins.CanvasDemo.csproj" />
```

- [ ] **Step 5: Build**

Run: `dotnet build samples/AcDream.Plugins.CanvasDemo/AcDream.Plugins.CanvasDemo.csproj -c Release 2>&1 | grep -E "rror\(s\)|arning\(s\)"`
Expected: `0 Warning(s)`, `0 Error(s)`, and a new `samples/AcDream.Plugins.CanvasDemo/packages.neutral.lock.json` exists.

- [ ] **Step 6: Smoke run** (offline, 1×, about 15 s; a window opens and closes itself)

```bash
mkdir -p /tmp/openac-hidpi/root-smoke/plugins/sample.canvas-demo
cp samples/AcDream.Plugins.CanvasDemo/bin/Release/net10.0/* /tmp/openac-hidpi/root-smoke/plugins/sample.canvas-demo/
dotnet build src/AcDream.App/AcDream.App.csproj -c Release 2>&1 | grep -E "rror\(s\)"
printf 'sleep 8000\nclose-client\n' > /tmp/openac-hidpi/smoke.probe
( export DYLD_LIBRARY_PATH=/opt/homebrew/lib VK_DRIVER_FILES=/opt/homebrew/etc/vulkan/icd.d/MoltenVK_icd.json \
    ACDREAM_ROOT_DIR=/tmp/openac-hidpi/root-smoke ACDREAM_DAT_DIR=$HOME/AsheronsCall \
    ACDREAM_PAK_PATH="$HOME/Library/Application Support/OpenAC/data/pak/acdream.pak" ACDREAM_NO_AUDIO=1 \
    ACDREAM_UI_PROBE_SCRIPT=/tmp/openac-hidpi/smoke.probe
  APP=$PWD/src/AcDream.App/bin/Release/net10.0/AcDream.App.dll
  cd /tmp/openac-hidpi   # the client writes a keymap folder relative to its working directory
  dotnet $APP > /tmp/openac-hidpi/run-smoke.log 2>&1 )
grep -E "plugin loaded: sample.canvas-demo|plugin canvas mounted: sample.canvas-demo" /tmp/openac-hidpi/run-smoke.log
```

Expected: `plugin loaded: sample.canvas-demo (Canvas Demo Sample)` and `[UI] plugin canvas mounted: sample.canvas-demo/demo (360x220)`.

- [ ] **Step 7: Commit**

```bash
git add AcDream.slnx samples/AcDream.Plugins.CanvasDemo/AcDream.Plugins.CanvasDemo.csproj \
  samples/AcDream.Plugins.CanvasDemo/plugin.json samples/AcDream.Plugins.CanvasDemo/CanvasDemoPlugin.cs \
  samples/AcDream.Plugins.CanvasDemo/packages.neutral.lock.json
git commit -m "samples: a canvas demo plugin with one of everything a painter draws

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
git checkout -- '*.lock.json'
```

```json:metadata
{"files": ["samples/AcDream.Plugins.CanvasDemo/AcDream.Plugins.CanvasDemo.csproj", "samples/AcDream.Plugins.CanvasDemo/plugin.json", "samples/AcDream.Plugins.CanvasDemo/CanvasDemoPlugin.cs", "samples/AcDream.Plugins.CanvasDemo/packages.neutral.lock.json", "AcDream.slnx"], "verifyCommand": "grep -c \"plugin canvas mounted: sample.canvas-demo/demo (360x220)\" /tmp/openac-hidpi/run-smoke.log", "acceptanceCriteria": ["sample builds with 0 warnings", "output has dll, deps.json, plugin.json", "offline run logs the canvas mounted at 360x220"], "modelTier": "standard"}
```

---

### Task 2: Before screenshots on the Retina display

**Goal:** Two crops of the demo canvas from a 2× framebuffer of the client at the Task 1 commit (no PR 4 code), saved as `/tmp/openac-hidpi/before-canvas.png` (720×440) and `/tmp/openac-hidpi/before-text.png`.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:** none in the repository. Capture tooling in `/tmp/openac-hidpi/`.

**Acceptance Criteria:**
- [ ] `run-before.log` contains `screenshot-complete name=before` with a size whose width is twice the window's (for example `size=2560x1440`), proving the window was on the built-in Retina display
- [ ] `/tmp/openac-hidpi/before-canvas.png` is 720×440 and shows the whole demo canvas
- [ ] The branch has no uncommitted or untracked change after the capture (`git status --short` lists nothing but lock files; in particular no `Asheron's Call/` keymap folder)

**Verify:** `grep -o "screenshot-complete name=before .*size=[0-9]*x[0-9]*" /tmp/openac-hidpi/run-before.log && sips -g pixelWidth -g pixelHeight /tmp/openac-hidpi/before-canvas.png` → a `size=` at 2× and `pixelWidth: 720`, `pixelHeight: 440`

**Background:** the main display on this Mac is a 1× QHD monitor, and a new window opens there. The built-in Liquid Retina XDR panel is the secondary display. The **user** drags the window onto the built-in display while the probe waits, and the client then saves its own backbuffer, exact device pixels with no OS scaling. The client's screenshot asks for the window size in points (`RenderFrameOrchestrator` passes `input.ViewportWidth/Height`). On a high-density display that size is not the swapchain's, and the capture fails with "The retained capture is 1600x1200; 800x600 was requested". That is a separate bug (see the spec's PR 4 corrections), so this task patches it in a **throwaway capture worktree only**.

**Steps:**

- [ ] **Step 1: A capture worktree at the Task 1 commit, with the size patch**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree add --detach /tmp/openac-hidpi/before HEAD
cd /tmp/openac-hidpi/before
python3 - <<'EOF'
p='src/AcDream.App/Diagnostics/FrameScreenshotController.cs'
s=open(p).read()
a='''    public bool CapturePending(int width, int height)
    {
        if (_pending.Count == 0)
            return false;
'''
b='''    // CAPTURE-WORKTREE ONLY: the framebuffer's size, not the window's, on a high-density display.
    internal static Func<(int Width, int Height)>? CaptureSizeOverride;

    public bool CapturePending(int width, int height)
    {
        if (_pending.Count == 0)
            return false;
        if (CaptureSizeOverride is { } captureSize)
            (width, height) = captureSize();
'''
assert a in s; open(p,'w').write(s.replace(a,b))
p='src/AcDream.App/Composition/InteractionRetainedUiComposition.cs'
s=open(p).read()
a='''            var screenshots = new FrameScreenshotController('''
b='''            FrameScreenshotController.CaptureSizeOverride = () => (d.Window.FramebufferSize.X, d.Window.FramebufferSize.Y);
            var screenshots = new FrameScreenshotController('''
assert a in s; open(p,'w').write(s.replace(a,b))
EOF
dotnet build src/AcDream.App/AcDream.App.csproj -c Release 2>&1 | grep -E "rror\(s\)"
dotnet build samples/AcDream.Plugins.CanvasDemo/AcDream.Plugins.CanvasDemo.csproj -c Release 2>&1 | grep -E "rror\(s\)"
mkdir -p /tmp/openac-hidpi/root-before/plugins/sample.canvas-demo
cp samples/AcDream.Plugins.CanvasDemo/bin/Release/net10.0/* /tmp/openac-hidpi/root-before/plugins/sample.canvas-demo/
```

Expected: `0 Error(s)` twice.

- [ ] **Step 2: The capture and crop scripts** (`/tmp/openac-hidpi/capture.sh`, `/tmp/openac-hidpi/crop.sh`)

```bash
cat > /tmp/openac-hidpi/capture.sh <<'EOF'
#!/bin/zsh
# capture.sh <worktree> <root> <name> <seconds before the frame is saved>
H=/tmp/openac-hidpi
printf 'sleep %s\nscreenshot %s\nsleep 1000\nclose-client\n' "$(( $4 * 1000 ))" "$3" > $H/$3.probe
rm -rf $H/art-$3
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH DYLD_LIBRARY_PATH=/opt/homebrew/lib \
  VK_DRIVER_FILES=/opt/homebrew/etc/vulkan/icd.d/MoltenVK_icd.json ACDREAM_ROOT_DIR=$2 \
  ACDREAM_DAT_DIR=$HOME/AsheronsCall ACDREAM_PAK_PATH="$HOME/Library/Application Support/OpenAC/data/pak/acdream.pak" \
  ACDREAM_NO_AUDIO=1 ACDREAM_UI_PROBE_SCRIPT=$H/$3.probe ACDREAM_AUTOMATION_ARTIFACT_DIR=$H/art-$3
# Run from here: the client writes a keymap folder relative to its working directory.
cd $H
dotnet $1/src/AcDream.App/bin/Release/net10.0/AcDream.App.dll > $H/run-$3.log 2>&1 &
P=$!
for i in $(seq 1 $(( $4 + 60 ))); do kill -0 $P 2>/dev/null || break; sleep 1; done
kill $P 2>/dev/null
grep -E "framebuffer resize|screenshot-complete|screenshot-failed|failed to mount" $H/run-$3.log
EOF
cat > /tmp/openac-hidpi/crop.sh <<'EOF'
#!/bin/zsh
# crop.sh <name>: the centred 360x220 canvas at 2x, and its three lines of text.
H=/tmp/openac-hidpi
F=$H/art-$1/screenshots/$1.png
W=$(sips -g pixelWidth $F | awk '/pixelWidth/ {print $2}')
T=$(sips -g pixelHeight $F | awk '/pixelHeight/ {print $2}')
X=$(( (W - 720) / 2 )); Y=$(( (T - 440) / 2 ))
sips -c 440 720 --cropOffset $Y $X $F --out $H/$1-canvas.png > /dev/null
sips -c 140 380 --cropOffset $(( Y + 10 )) $(( X + 10 )) $F --out $H/$1-text.png > /dev/null
sips -g pixelWidth -g pixelHeight $H/$1-canvas.png | grep pixel
EOF
chmod +x /tmp/openac-hidpi/capture.sh /tmp/openac-hidpi/crop.sh
```

- [ ] **Step 3: Ask the user, then capture**

Tell the user, before running: "A client window is about to open on your QHD monitor. Drag it onto the MacBook's built-in display and leave it there; it saves its own frame after 45 seconds and closes itself."

```bash
/tmp/openac-hidpi/capture.sh /tmp/openac-hidpi/before /tmp/openac-hidpi/root-before before 45
```

Expected: `window: framebuffer resize event 2560x1440` (or another size at twice the window's points), then `screenshot-complete name=before … size=<2× size>`. If the size is the window's own (for example `1280x720`), the window was not on the Retina display: ask the user and run again.

- [ ] **Step 4: Crop and look**

```bash
/tmp/openac-hidpi/crop.sh before
```

Expected: `pixelWidth: 720`, `pixelHeight: 440`. Read `/tmp/openac-hidpi/before-canvas.png` and confirm it shows the whole panel: title, two Noto lines, the interface-font line, four shapes, gradient, two lines, checkerboard. Expect the text and edges to be soft (bilinearly magnified). Show both crops to the user.

- [ ] **Step 5: Leave the branch clean**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC && git status --short
```

Expected: nothing, or only `packages.*.lock.json`. The capture worktree stays until Task 12.

```json:metadata
{"files": [], "verifyCommand": "grep -o \"screenshot-complete name=before .*size=[0-9]*x[0-9]*\" /tmp/openac-hidpi/run-before.log && sips -g pixelWidth -g pixelHeight /tmp/openac-hidpi/before-canvas.png", "acceptanceCriteria": ["run-before.log shows screenshot-complete name=before at twice the window size", "before-canvas.png is 720x440 and shows the whole demo canvas", "branch has no uncommitted or untracked change besides lock files (no Asheron's Call/ keymap folder)"], "modelTier": "standard", "userGate": true, "tags": ["user-gate"], "requireEvidenceTokens": [["before"], ["2560x1440", "size="]]}
```

---

### Task 3: The pixel scale in the contract

**Goal:** `IPluginPainter.PixelScale`, a default member answering 1. The painter docs say edges are one *screen* pixel, and the demo shows the scale and a one-screen-pixel hairline.

**Files:**
- Modify: `src/AcDream.Plugin.Abstractions/PluginCanvas.cs` (the `IPluginPainter` doc comment above `public interface IPluginPainter`; a member after `IPluginPainter`'s `int Height { get; }`, ~:172; two `thickness` param docs on `StrokeRoundedRect`/`StrokeEllipse`)
- Test: `tests/AcDream.Plugin.Tests/PluginCanvasPixelScaleContractTests.cs` (create)
- Modify: `samples/AcDream.Plugins.CanvasDemo/CanvasDemoPlugin.cs` (after the two `DrawLine` calls)

**Acceptance Criteria:**
- [ ] A painter implementing only the pre-PR-4 members compiles and reports `PixelScale == 1.0`
- [ ] The Abstractions build has 0 warnings
- [ ] The demo builds with 0 warnings

**Verify:** `dotnet test tests/AcDream.Plugin.Tests -c Release --filter "FullyQualifiedName~PluginCanvasPixelScale"` → `Passed!  - Failed:     0, Passed:     1`

**Steps:**

- [ ] **Step 1: Write the failing test** (`tests/AcDream.Plugin.Tests/PluginCanvasPixelScaleContractTests.cs`)

```csharp
// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;

namespace AcDream.Plugin.Tests;

/// <summary>
/// The pixel scale is additive: a painter written before it still builds
/// and reports one screen pixel per canvas pixel.
/// </summary>
public sealed class PluginCanvasPixelScaleContractTests
{
    /// <summary>A painter that implements only what the contract had before the pixel scale.</summary>
    private sealed class OlderPainter : IPluginPainter
    {
        public int Width => 10;

        public int Height => 10;

        public void Clear(PluginColor color) { }

        public void FillRect(PluginRect rect, PluginColor color) { }

        public void StrokeRect(PluginRect rect, PluginColor color, float thickness = 1f) { }

        public void DrawLine(PluginPoint from, PluginPoint to, PluginColor color, float thickness = 1f) { }

        public void DrawText(string text, PluginPoint position, PluginColor color, bool outline = false) { }

        public PluginSize MeasureText(string text) => default;

        public void DrawImage(PluginImage image, PluginRect destination, PluginColor tint) { }

        public void DrawImageTransformed(
            PluginImage image, PluginRect destination, PluginColor tint, double rotationRadians,
            PluginPoint pivot, double scaleX = 1.0, double scaleY = 1.0) { }

        public void PushClip(PluginRect rect) { }

        public void PopClip() { }
    }

    [Fact]
    public void APainterWrittenBeforeThePixelScaleReportsOne()
    {
        IPluginPainter painter = new OlderPainter();

        Assert.Equal(1.0, painter.PixelScale);
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet build tests/AcDream.Plugin.Tests -c Release 2>&1 | grep -E "error CS" | head -1`
Expected: `error CS1061: 'IPluginPainter' does not contain a definition for 'PixelScale'`.

- [ ] **Step 3: The member and its docs** (`src/AcDream.Plugin.Abstractions/PluginCanvas.cs`)

In the `IPluginPainter` doc comment, replace

```csharp
/// <para>Shapes -- polygons, rounded rectangles, ellipses, circles and
/// gradients -- have edges anti-aliased over one pixel; the other
```

with

```csharp
/// <para>Shapes -- polygons, rounded rectangles, ellipses, circles and
/// gradients -- have edges anti-aliased over one screen pixel; the other
```

and, after the paragraph ending `clip pushed during a paint must be popped before it returns.</para>`, add, before `/// </summary>`:

```csharp
///
/// <para>On a high-density display the host paints the canvas at more than
/// one screen pixel per canvas pixel (<see cref="PixelScale"/>), so shapes
/// and text in a <see cref="PluginFont"/> come out sharp. Coordinates and
/// sizes stay in canvas pixels whatever the scale.</para>
```

After the first `int Height { get; }` in the file (line ~172, in `IPluginPainter`; the next one belongs to `IPluginCanvas`) add:

```csharp

    /// <summary>
    /// How many screen pixels one canvas pixel covers in this paint: 2 on a
    /// typical high-density display, 1 on a host that does not scale.
    /// Coordinates are canvas pixels either way; read this only to align a
    /// detail to screen pixels, for example a hairline one screen pixel
    /// wide (1 / <see cref="PixelScale"/> canvas pixels). It can change
    /// between paints, when the window moves to another display; the host
    /// repaints the canvas when it does.
    /// </summary>
    double PixelScale => 1.0;
```

In both `thickness` param docs that read `thinner than one pixel is drawn one pixel wide and proportionally fainter`, change them to `thinner than one screen pixel is drawn one screen pixel wide and proportionally fainter`.

- [ ] **Step 4: Run the test**

Run: `dotnet test tests/AcDream.Plugin.Tests -c Release --filter "FullyQualifiedName~PluginCanvas" 2>&1 | grep -E "Passed!|Failed!"`
Expected: `Passed!`, 0 failed.

- [ ] **Step 5: The demo shows the scale** (`samples/AcDream.Plugins.CanvasDemo/CanvasDemoPlugin.cs`)

After `painter.DrawLine(new PluginPoint(14, 200), new PluginPoint(252, 176), PluginColor.White, 1f);` add:

```csharp

        // A hairline one screen pixel wide, whatever the display.
        double hairline = 1.0 / painter.PixelScale;
        painter.FillRect(new PluginRect(14, 206, 238, hairline), Accent);
        painter.DrawText(
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Pixel scale {painter.PixelScale:0.##}"),
            new PluginPoint(274, 190), new PluginColor(200, 200, 200), _body);
```

Run: `dotnet build samples/AcDream.Plugins.CanvasDemo -c Release 2>&1 | grep -E "rror\(s\)|arning\(s\)"`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Plugin.Abstractions/PluginCanvas.cs tests/AcDream.Plugin.Tests/PluginCanvasPixelScaleContractTests.cs \
  samples/AcDream.Plugins.CanvasDemo/CanvasDemoPlugin.cs
git commit -m "plugin api: the painter says how many screen pixels a canvas pixel covers

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.Plugin.Abstractions/PluginCanvas.cs", "tests/AcDream.Plugin.Tests/PluginCanvasPixelScaleContractTests.cs", "samples/AcDream.Plugins.CanvasDemo/CanvasDemoPlugin.cs"], "verifyCommand": "dotnet test tests/AcDream.Plugin.Tests -c Release --filter \"FullyQualifiedName~PluginCanvasPixelScale\"", "acceptanceCriteria": ["older painter compiles and reports PixelScale 1.0", "Abstractions 0 warnings", "demo 0 warnings"], "modelTier": "mechanical"}
```

---

### Task 4: The scale rule

**Goal:** `CanvasPixelScale`: the interface scale from framebuffer-per-point and the fixed-canvas stretch, the per-canvas clamp to the device's largest texture, and a canvas length in device pixels.

**Files:**
- Create: `src/AcDream.App/UI/Layout/CanvasPixelScale.cs`
- Test: `tests/AcDream.App.Tests/UI/Layout/CanvasPixelScaleTests.cs` (create)

**Acceptance Criteria:**
- [ ] Interface scale: (1,1)→1, (2,2)→2, (1.5,1.5)→1.5, (1.1,1.1)→1.25, 2 × stretch 1.3333334→2.75, (2,1)→2, stretch (1,1.6)→1.75, 3 × stretch 2→4, 0.5→1, NaN/∞/0→1, 2 + 1e-6→2
- [ ] Canvas clamp at interface 2: 200×100 at 16384→2; 5000×100 at 16384→2, at 8192→1.5; 100×6000 at 8192→1.25; 9000×100 at 8192→1; 20000×100 at 16384→1
- [ ] Device size rounds up: 200@2→400, 201@1.25→252, 3@1.75→6, 7@1→7

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~CanvasPixelScaleTests"` → `Passed!  - Failed:     0, Passed:    23`

**Steps:**

- [ ] **Step 1: Write the failing tests** (`tests/AcDream.App.Tests/UI/Layout/CanvasPixelScaleTests.cs`)

```csharp
using System.Numerics;
using AcDream.App.UI.Layout;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// A canvas is painted at framebuffer pixels per point times the interface's
/// stretch, rounded up to a quarter, from 1 to 4, and lowered only to fit
/// the device.
/// </summary>
public sealed class CanvasPixelScaleTests
{
    [Theory]
    [InlineData(1f, 1f, 1f, 1f, 1f)]
    [InlineData(2f, 2f, 1f, 1f, 2f)]
    [InlineData(1.5f, 1.5f, 1f, 1f, 1.5f)]
    [InlineData(1.1f, 1.1f, 1f, 1f, 1.25f)]
    [InlineData(2f, 2f, 1.3333334f, 1.3333334f, 2.75f)]
    [InlineData(2f, 1f, 1f, 1f, 2f)]
    [InlineData(1f, 1f, 1f, 1.6f, 1.75f)]
    [InlineData(3f, 3f, 2f, 2f, 4f)]
    [InlineData(0.5f, 0.5f, 1f, 1f, 1f)]
    public void TheInterfaceScaleIsTheLargerAxisRoundedUpToAQuarter(
        float framebufferX, float framebufferY, float stretchX, float stretchY, float expected)
    {
        Assert.Equal(
            expected,
            CanvasPixelScale.ForInterface(new Vector2(framebufferX, framebufferY), new Vector2(stretchX, stretchY)));
    }

    [Fact]
    public void AScaleAHairOverAStepIsThatStep()
    {
        Assert.Equal(2f, CanvasPixelScale.ForInterface(new Vector2(2880f / 1440f + 1e-6f), Vector2.One));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(0f)]
    public void AScaleThatIsNotAPositiveNumberIsOne(float framebuffer)
    {
        Assert.Equal(1f, CanvasPixelScale.ForInterface(new Vector2(framebuffer), Vector2.One));
    }

    [Theory]
    [InlineData(200, 100, 16384u, 2f)]
    [InlineData(5000, 100, 16384u, 2f)]
    [InlineData(5000, 100, 8192u, 1.5f)]
    [InlineData(100, 6000, 8192u, 1.25f)]
    [InlineData(9000, 100, 8192u, 1f)]
    [InlineData(20000, 100, 16384u, 1f)]
    public void ACanvasTooLargeForTheDeviceIsPaintedAtTheLargestStepThatFits(
        int width, int height, uint maximumDimension, float expected)
    {
        Assert.Equal(expected, CanvasPixelScale.ForCanvas(2f, width, height, maximumDimension));
    }

    [Theory]
    [InlineData(200, 2f, 400)]
    [InlineData(201, 1.25f, 252)]
    [InlineData(3, 1.75f, 6)]
    [InlineData(7, 1f, 7)]
    public void ADeviceLengthIsRoundedUp(int canvasPixels, float scale, int expected)
    {
        Assert.Equal(expected, CanvasPixelScale.DeviceSize(canvasPixels, scale));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -E "error CS" | head -1`
Expected: `error CS0103: The name 'CanvasPixelScale' does not exist in the current context`.

- [ ] **Step 3: The rule** (`src/AcDream.App/UI/Layout/CanvasPixelScale.cs`)

```csharp
using System.Numerics;

namespace AcDream.App.UI.Layout;

/// <summary>
/// How many device pixels a plugin canvas is painted at per canvas pixel.
/// The interface is laid out in window points and stretched over the
/// framebuffer, and a pre-game fixed canvas stretches it again; a canvas
/// painted at its own size would be magnified by both and come out blurred.
/// Painting it at the product instead puts one texel on each device pixel.
///
/// <para>The scale goes up in quarter steps, rounded up so a texel is never
/// stretched, from 1 to 4. A canvas too large for the device at that scale
/// is painted at the largest step that fits, and never below 1.</para>
/// </summary>
internal static class CanvasPixelScale
{
    internal const float Step = 0.25f;

    internal const float Minimum = 1f;

    internal const float Maximum = 4f;

    /// <summary>
    /// The scale every canvas of the interface is painted at, before any
    /// canvas's own size is considered: framebuffer pixels per window point
    /// times the interface's fixed-canvas stretch, the larger of the two
    /// axes, rounded up to a step.
    /// </summary>
    /// <param name="framebufferPerPoint">Framebuffer pixels per window point on each axis.</param>
    /// <param name="interfaceStretch">The root's <see cref="UiRoot.CanvasScale"/>.</param>
    internal static float ForInterface(Vector2 framebufferPerPoint, Vector2 interfaceStretch)
    {
        float raw = MathF.Max(framebufferPerPoint.X * interfaceStretch.X, framebufferPerPoint.Y * interfaceStretch.Y);
        if (!float.IsFinite(raw) || raw <= Minimum)
            return Minimum;
        // A hair of tolerance, so 2.0000002 from a float division is 2, not 2.25.
        float stepped = MathF.Ceiling(raw / Step - 1e-3f) * Step;
        return Math.Clamp(stepped, Minimum, Maximum);
    }

    /// <summary>
    /// The interface scale lowered step by step until a canvas of this size
    /// fits the device's largest texture, but never below 1: a canvas too
    /// large even at 1 fails to get a target, as it always has.
    /// </summary>
    internal static float ForCanvas(float interfaceScale, int width, int height, uint maximumDimension)
    {
        float scale = interfaceScale;
        while (scale > Minimum && (DeviceSize(width, scale) > maximumDimension || DeviceSize(height, scale) > maximumDimension))
            scale -= Step;
        return MathF.Max(scale, Minimum);
    }

    /// <summary>A canvas length in device pixels: rounded up, so the last canvas pixel is whole.</summary>
    internal static int DeviceSize(int canvasPixels, float scale) =>
        (int)Math.Ceiling(canvasPixels * (double)scale);
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~CanvasPixelScaleTests" 2>&1 | grep -E "Passed!|Failed!"` (after `dotnet build tests/AcDream.App.Tests -c Release`)
Expected: `Passed!  - Failed:     0, Passed:    23`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Layout/CanvasPixelScale.cs tests/AcDream.App.Tests/UI/Layout/CanvasPixelScaleTests.cs
git commit -m "plugin canvas: the scale a canvas is painted at on a high-density display

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/Layout/CanvasPixelScale.cs", "tests/AcDream.App.Tests/UI/Layout/CanvasPixelScaleTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~CanvasPixelScaleTests\"", "acceptanceCriteria": ["interface scale cases", "canvas clamp cases", "device size rounds up"], "modelTier": "mechanical"}
```

---

### Task 5: Canvases painted at the pixel scale

**Goal:** The host supplies framebuffer pixels per window point. Each frame the element computes the canvas's scale, gives its targets back when the scale changes, and allocates targets at `ceil(W·s) × ceil(H·s)`. The surface repaints with the renderer's projection and `CanvasScale` at `s` and everything else in canvas pixels. The painter reports `PixelScale` and uses a device pixel of `1/s` for shape fringes and chords. The surface's renderer no longer swaps to linear twins.

**Files:**
- Modify: `src/AcDream.App/UI/Layout/PluginCanvasSurface.cs` (`PluginCanvasHostServices`, lines 8-42; the constructor's twin line, :80; `Repaint`, :95-136)
- Modify: `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs:1055-1059`
- Modify: `src/AcDream.App/UI/Layout/PluginPainter.cs` (class doc; `DevicePixel` const; `Bind`; `PixelScale` after `Height`; seven `DevicePixel` uses)
- Modify: `src/AcDream.App/UI/Layout/PluginCanvasElement.cs` (class doc; fields; `PixelScale`; `OnDraw`; `RepaintIfInvalidated`; `AcquireWritable`; `ReleaseTargets`)
- Modify: `tests/AcDream.App.Tests/Rendering/Gpu/RecordingGpuDevice.cs:192` (`DefaultCapabilities`)
- Modify: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs` (`Harness`: properties, constructor, `Mount`)
- Test: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.HiDpi.cs` (create)

**Acceptance Criteria:**
- [ ] At framebuffer-per-point 2 the paint sees `(200, 100, 2.0)`, the target is 400×200, the canvas pass projects 400×200, a full-canvas fill's vertices reach (400, 200), and the interface still blits a 200-wide quad
- [ ] At 1 the target and projection are 200×100 and `PixelScale` is 1
- [ ] A fixed canvas of 400×300 in an 800×600 root paints at 2
- [ ] A change 1 → 2 repaints once, retires the old target (one pending retirement; slot count back to its value after `RunAll`), and makes one 400×200 target; a further frame does not repaint
- [ ] With a 350-pixel device limit, the 200×100 canvas paints at 1.75 into 350×175
- [ ] Pointer events at 2× arrive in canvas pixels: (600, 500) → (10, 10), (789, 589) → (199, 99)
- [ ] A painter kept past its callback throws on `PixelScale`
- [ ] Every existing `PluginCanvas*` test passes

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginCanvas&Lane!=Vulkan"` → `Passed!`, 0 failed

**Steps:**

- [ ] **Step 1: A device double whose capabilities a test can change** (`tests/AcDream.App.Tests/Rendering/Gpu/RecordingGpuDevice.cs`)

Replace

```csharp
    public GpuCapabilityRecord Capabilities { get; init; } = new()
    {
```

with

```csharp
    public GpuCapabilityRecord Capabilities { get; init; } = DefaultCapabilities;

    /// <summary>What the double reports unless a test says otherwise; a test changes one field with <c>with</c>.</summary>
    public static GpuCapabilityRecord DefaultCapabilities { get; } = new()
    {
```

(The initializer's body and closing `};` stay as they are.)

- [ ] **Step 2: The harness fakes a density and a device limit, and mounts input canvases as the runtime does** (`tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs`)

Replace

```csharp
        public Harness()
        {
            Device = new RecordingGpuDevice(retirement: Retirement);
```

with

```csharp
        /// <summary>Framebuffer pixels per window point; tests set it to stand in for a high-density display.</summary>
        public Vector2 FramebufferPerPoint { get; set; } = Vector2.One;

        public Harness(uint maximumImageDimension = 16_384)
        {
            Device = new RecordingGpuDevice(retirement: Retirement)
            {
                Capabilities = RecordingGpuDevice.DefaultCapabilities with { MaxImageDimension2D = maximumImageDimension },
            };
```

Replace `var services = new PluginCanvasHostServices(Device, Frames, "unused", null);` with

```csharp
            var services = new PluginCanvasHostServices(Device, Frames, "unused", () => FramebufferPerPoint);
```

In `Mount`, replace `Layer.AddChild(element);` with

```csharp
            Layer.AddChild(element, takesInput: descriptor.AcceptsPointerInput);
```

- [ ] **Step 3: Write the failing tests** (`tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.HiDpi.cs`)

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// On a high-density display a canvas is painted at the pixel scale: its
/// target and the surface's projection grow, while the painter, the clip,
/// the pointer and the canvas's place on the interface stay in canvas
/// pixels. A change of scale gives the targets back and repaints.
/// </summary>
public sealed partial class PluginCanvasElementTests
{
    [Fact]
    public void AtTwiceTheDensityTheTargetAndProjectionDoubleAndThePainterStaysInCanvasPixels()
    {
        var harness = new Harness { FramebufferPerPoint = new Vector2(2f, 2f) };
        (int Width, int Height, double Scale) seen = default;
        (_, PluginCanvasElement element) = harness.Mount(Hud(), painter =>
        {
            seen = (painter.Width, painter.Height, painter.PixelScale);
            painter.FillRect(new PluginRect(0, 0, 200, 100), PluginColor.White);
        });

        harness.Frame();

        Assert.Equal((200, 100, 2.0), seen);
        Assert.Equal(2f, element.PixelScale);
        GpuRecordedRenderTargetCreate created = Assert.Single(harness.Device.OfKind<GpuRecordedRenderTargetCreate>());
        Assert.Equal((400, 200), (created.Description.Width, created.Description.Height));
        Assert.Equal((400f, 200f), SurfaceProjection(harness));

        // The fill's vertices come out in target pixels.
        (uint _, IReadOnlyList<float> verts) = Assert.Single(harness.Surface.Renderer.DebugSpriteSegmentVerts);
        Assert.Equal(400f, Enumerable.Range(0, 6).Max(i => verts[i * AcDream.App.Rendering.TextRenderer.FloatsPerVertex]));
        Assert.Equal(200f, Enumerable.Range(0, 6).Max(i => verts[i * AcDream.App.Rendering.TextRenderer.FloatsPerVertex + 1]));

        // The interface still shows it as a 200 x 100 quad at its anchored place.
        Assert.Equal((200f, 100f), (element.Width, element.Height));
        (uint _, IReadOnlyList<float> blit) = Assert.Single(harness.MainRenderer.DebugSpriteSegmentVerts);
        float left = Enumerable.Range(0, 6).Min(i => blit[i * AcDream.App.Rendering.TextRenderer.FloatsPerVertex]);
        float right = Enumerable.Range(0, 6).Max(i => blit[i * AcDream.App.Rendering.TextRenderer.FloatsPerVertex]);
        Assert.Equal(200f, right - left);
    }

    [Fact]
    public void AtOneToOneNothingChanges()
    {
        var harness = new Harness();
        double scale = 0;
        harness.Mount(Hud(), painter =>
        {
            scale = painter.PixelScale;
            painter.FillRect(new PluginRect(0, 0, 200, 100), PluginColor.White);
        });

        harness.Frame();

        Assert.Equal(1.0, scale);
        GpuRecordedRenderTargetCreate created = Assert.Single(harness.Device.OfKind<GpuRecordedRenderTargetCreate>());
        Assert.Equal((200, 100), (created.Description.Width, created.Description.Height));
        Assert.Equal((200f, 100f), SurfaceProjection(harness));
    }

    [Fact]
    public void TheFixedCanvasStretchCountsTowardsTheScale()
    {
        var harness = new Harness();
        harness.Root.DeclareFixedCanvas(this, new Vector2(400f, 300f));
        double scale = 0;
        harness.Mount(Hud(), painter => scale = painter.PixelScale);

        harness.Frame();

        Assert.Equal(2.0, scale);
    }

    [Fact]
    public void AChangeOfScaleGivesTheTargetsBackAndRepaintsAtTheNewSize()
    {
        var harness = new Harness();
        var scales = new List<double>();
        (_, PluginCanvasElement element) = harness.Mount(Hud(), painter => scales.Add(painter.PixelScale));
        harness.Frame();
        harness.Frame();
        Assert.Equal(1, element.TargetCount);
        int slotsBefore = harness.Device.LiveTextureSlotCount;
        harness.Device.Clear();

        // The window moves to a high-density display.
        harness.FramebufferPerPoint = new Vector2(2f, 2f);
        harness.Frame();

        Assert.Equal([1.0, 2.0], scales);
        // The old target is given back at once; the device holds it until
        // the frames in flight are done with it.
        Assert.Single(harness.Retirement.Pending);
        harness.Retirement.RunAll();
        Assert.Equal(slotsBefore, harness.Device.LiveTextureSlotCount);
        GpuRecordedRenderTargetCreate created = Assert.Single(harness.Device.OfKind<GpuRecordedRenderTargetCreate>());
        Assert.Equal((400, 200), (created.Description.Width, created.Description.Height));
        Assert.Equal(1, element.TargetCount);
        Assert.NotEqual(0u, element.ShownTextureHandle);

        // Unchanged afterwards: no more repaints than the plugin asks for.
        harness.Frame();
        Assert.Equal(2, scales.Count);
    }

    [Fact]
    public void ACanvasTooLargeForTheDeviceIsPaintedAtTheLargestScaleThatFits()
    {
        var harness = new Harness(maximumImageDimension: 350) { FramebufferPerPoint = new Vector2(2f, 2f) };
        double scale = 0;
        harness.Mount(Hud(), painter => scale = painter.PixelScale);

        harness.Frame();

        Assert.Equal(1.75, scale);
        GpuRecordedRenderTargetCreate created = Assert.Single(harness.Device.OfKind<GpuRecordedRenderTargetCreate>());
        Assert.Equal((350, 175), (created.Description.Width, created.Description.Height));
    }

    [Fact]
    public void PointerPositionsStayInCanvasPixels()
    {
        var harness = new Harness { FramebufferPerPoint = new Vector2(2f, 2f) };
        var events = new List<PluginPointerEvent>();
        (PluginCanvasRegistration registration, _) = harness.Mount(Hud() with { AcceptsPointerInput = true }, _ => { });
        ((IPluginCanvas)registration).PointerHandler = events.Add;
        harness.Frame();

        // The canvas sits at (590, 490): 800 x 600, anchored bottom right, 10 in.
        harness.Root.OnMouseDown(UiMouseButton.Left, 600, 500);
        harness.Root.OnMouseUp(UiMouseButton.Left, 789, 589);

        Assert.Equal(
            [new PluginPoint(10, 10), new PluginPoint(199, 99)],
            events.Select(e => e.Position).ToArray());
    }

    [Fact]
    public void AKeptPainterStillThrowsForItsPixelScale()
    {
        var harness = new Harness { FramebufferPerPoint = new Vector2(2f, 2f) };
        IPluginPainter? kept = null;
        harness.Mount(Hud(), painter => kept = painter);

        harness.Frame();

        Assert.NotNull(kept);
        Assert.Throws<InvalidOperationException>(() => kept!.PixelScale);
    }

    /// <summary>The projection size the surface's pass pushed: what one target pixel of the canvas pass is.</summary>
    private static (float Width, float Height) SurfaceProjection(Harness harness)
    {
        bool inCanvasPass = false;
        foreach (GpuRecordedCall call in harness.Device.Calls)
        {
            if (call is GpuRecordedPassBegin begin)
                inCanvasPass = begin.Name == PluginCanvasSurface.PassName;
            else if (inCanvasPass && call is GpuRecordedPushConstants push)
                return (push.Constants.ParamA, push.Constants.ParamB);
        }
        throw new Xunit.Sdk.XunitException("The canvas pass pushed no constants.");
    }
}
```

- [ ] **Step 4: Run them to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -E "error CS" | sort -u | head -3`
Expected: errors that `PluginCanvasHostServices` has no constructor taking a `Func<Vector2>` lambda, and that `PluginCanvasElement` has no `PixelScale`.

- [ ] **Step 5: The host services carry framebuffer pixels per point** (`src/AcDream.App/UI/Layout/PluginCanvasSurface.cs`)

Replace the whole `PluginCanvasHostServices` class and its doc comment with:

```csharp
/// <summary>
/// What the interface needs from the renderer to paint plugin canvases:
/// the device the off-screen targets come from, the frame the passes go
/// into, where the interface shaders are, and how many framebuffer pixels
/// the window has per point. Public so it can travel in the runtime's
/// bindings; the renderer types themselves stay internal.
/// </summary>
public sealed class PluginCanvasHostServices
{
    internal PluginCanvasHostServices(
        IGpuDevice device,
        ICurrentGpuFrameSource frames,
        string shaderDirectory,
        Func<Vector2>? framebufferPerPoint)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
        Frames = frames ?? throw new ArgumentNullException(nameof(frames));
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderDirectory);
        ShaderDirectory = shaderDirectory;
        FramebufferPerPoint = framebufferPerPoint ?? (static () => Vector2.One);
    }

    /// <summary>
    /// The device the targets come from and go back to. It waits for the
    /// frames in flight itself when a target is given back; nothing here
    /// keeps a retirement ledger of its own.
    /// </summary>
    internal IGpuDevice Device { get; }

    internal ICurrentGpuFrameSource Frames { get; }

    internal string ShaderDirectory { get; }

    /// <summary>
    /// Framebuffer pixels per window point on each axis, read every frame:
    /// 2 on a typical high-density display, and it changes when the window
    /// moves to a display of another density. One without a window.
    /// </summary>
    internal Func<Vector2> FramebufferPerPoint { get; }
}
```

In the `PluginCanvasSurface` constructor replace `_renderer.LinearTwinResolver = services.LinearTwinResolver;` with

```csharp
        // No linear twins, unlike the fixed canvas: a canvas painted above
        // one pixel per canvas pixel is not magnified afterwards, so the
        // interface font keeps its nearest-sampled, whole-pixel look.
        _renderer.LinearTwinResolver = null;
```

Replace `Repaint` and its doc comment with:

```csharp
    /// <summary>
    /// Runs one plugin paint callback into a target under the guard. Returns
    /// what the guard returned: true when the callback drew and left the
    /// clip stack as it found it. The target is cleared and drawn either
    /// way, so a callback that threw halfway leaves a blank canvas rather
    /// than half of one over the last. A paint that asked for more shape
    /// vertices than one paint may counts against the guard as an overrun.
    ///
    /// <para>At a <paramref name="pixelScale"/> above 1 the target is that
    /// many times the canvas's size. The context, the clip and the painter
    /// stay in canvas pixels, as the interface does on the fixed canvas:
    /// only the renderer multiplies, as the vertices go out.</para>
    /// </summary>
    internal bool Repaint(
        PluginCanvasRegistration registration,
        IGpuRenderTarget target,
        UiDrawCallbackGuard guard,
        PluginImages? images,
        PluginFonts? fonts = null,
        Action<CanvasShapeProblem, string>? shapeProblems = null,
        float pixelScale = 1f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(guard);
        int width = registration.Width;
        int height = registration.Height;
        var size = new Vector2(width, height);

        _renderer.Begin(new Vector2(
            CanvasPixelScale.DeviceSize(width, pixelScale), CanvasPixelScale.DeviceSize(height, pixelScale)));
        _renderer.CanvasScale = new Vector2(pixelScale);
        _context.Begin(size, null);
        _context.PushClip(0f, 0f, width, height);
        _painter.Bind(_context, Font, images, width, height, fonts, shapeProblems, pixelScale);
        bool drew;
        try
        {
            Action<Plugin.Abstractions.IPluginPainter>? paint = registration.Paint;
            drew = paint is not null && guard.Invoke(_context, _ => paint(_painter), _overShapeBudget);
        }
        finally
        {
            _painter.Unbind();
            _context.PopClip();
            _renderer.CanvasScale = Vector2.One;
        }
        _renderer.FlushTo(target, Vector4.Zero, null, PassName);
        return drew;
    }
```

- [ ] **Step 6: The composition supplies framebuffer ÷ window size** (`src/AcDream.App/Composition/InteractionRetainedUiComposition.cs`)

Replace

```csharp
                PluginCanvases: new PluginCanvasHostServices(
                    d.GpuDevice,
                    d.GpuFrameSource,
                    d.ShadersDirectory,
                    d.TextureCache.GetOrCreateLinearUiTwin),
```

with

```csharp
                PluginCanvases: new PluginCanvasHostServices(
                    d.GpuDevice,
                    d.GpuFrameSource,
                    d.ShadersDirectory,
                    () => d.Window.Size is { X: > 0, Y: > 0 } points
                        ? new System.Numerics.Vector2(
                            d.Window.FramebufferSize.X / (float)points.X,
                            d.Window.FramebufferSize.Y / (float)points.Y)
                        : System.Numerics.Vector2.One),
```

(A minimised window reports size 0; the scale stays 1 until it is restored.)

- [ ] **Step 7: The painter at a scale** (`src/AcDream.App/UI/Layout/PluginPainter.cs`)

In the class doc, replace

```csharp
/// the canvas's top-left corner, and the canvas rectangle is already the
/// clip in force.</para>
```

with

```csharp
/// the canvas's top-left corner, and the canvas rectangle is already the
/// clip in force. The pixel scale the surface binds is how many target
/// pixels each canvas pixel covers; only what is measured in device pixels
/// -- a shape's fringe, a curve's chords -- reads it.</para>
```

Replace

```csharp
    /// <summary>Canvases are painted at their own size, so one device pixel of the target is one canvas pixel.</summary>
    private const float DevicePixel = 1f;
```

with

```csharp
    /// <summary>One pixel of the target, in canvas pixels: the reciprocal of the pixel scale.</summary>
    private float _devicePixel = 1f;

    private float _pixelScale = 1f;
```

Replace the start of `Bind`

```csharp
        PluginFonts? fonts = null, Action<CanvasShapeProblem, string>? shapeProblems = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
```

with

```csharp
        PluginFonts? fonts = null, Action<CanvasShapeProblem, string>? shapeProblems = null,
        float pixelScale = 1f)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        if (!(float.IsFinite(pixelScale) && pixelScale >= 1f))
            throw new ArgumentOutOfRangeException(nameof(pixelScale), pixelScale, "A pixel scale is at least 1.");
        _pixelScale = pixelScale;
        _devicePixel = 1f / pixelScale;
```

After `public int Height => _height;` add

```csharp

    public double PixelScale
    {
        get
        {
            _ = Context;
            return _pixelScale;
        }
    }
```

Replace every remaining `DevicePixel` argument (seven: `FillRoundedRect`, `StrokeRoundedRect`, `FillEllipse`, `StrokeEllipse`, `FillCircle`, `FillRectGradient`, `FillPolygonCore`) with `_devicePixel`:

```bash
sed -i '' 's/, DevicePixel, _shape)/, _devicePixel, _shape)/' src/AcDream.App/UI/Layout/PluginPainter.cs
grep -c "_devicePixel, _shape" src/AcDream.App/UI/Layout/PluginPainter.cs   # 7
grep -c "DevicePixel" src/AcDream.App/UI/Layout/PluginPainter.cs            # 0
```

- [ ] **Step 8: The element follows the scale** (`src/AcDream.App/UI/Layout/PluginCanvasElement.cs`)

In the class doc, before `/// <para>A canvas that opted in to pointer input answers the hit-test for`, add

```csharp
/// <para>Targets are painted at the interface's pixel scale
/// (<see cref="CanvasPixelScale"/>), so on a high-density display each texel
/// lands on one device pixel instead of being magnified. The element
/// reads the scale every frame it draws; when it changes, every target is
/// given back, the canvas is invalidated, and the next paint allocates at
/// the new size.</para>
///
```

After `private bool _reportedShapeBudget;` add `private float _pixelScale = 1f;`.

After `internal int TargetCount => _targets.Count;` add

```csharp

    /// <summary>The pixel scale the targets are painted at.</summary>
    internal float PixelScale => _pixelScale;
```

In `OnDraw`, replace

```csharp
        if (frameSlot is { } slot)
        {
            RepaintIfInvalidated(slot);
```

with

```csharp
        if (frameSlot is { } slot)
        {
            FollowPixelScale();
            RepaintIfInvalidated(slot);
```

In `RepaintIfInvalidated`, replace

```csharp
        bool drew = _surface.Repaint(
            _registration, target.Target, _guard, _images(), _fonts(), shapeProblems: _shapeProblems);
```

with

```csharp
        bool drew = _surface.Repaint(
            _registration, target.Target, _guard, _images(), _fonts(), _shapeProblems, _pixelScale);
```

In `AcquireWritable`, replace

```csharp
        IGpuDevice device = _surface.Services.Device;
        IGpuRenderTarget target;
```

with

```csharp
        IGpuDevice device = _surface.Services.Device;
        int width = CanvasPixelScale.DeviceSize(_registration.Width, _pixelScale);
        int height = CanvasPixelScale.DeviceSize(_registration.Height, _pixelScale);
        IGpuRenderTarget target;
```

and in the `GpuRenderTargetDescription` and the failure report below it, use `width` and `height` in place of `_registration.Width` and `_registration.Height`:

```csharp
            target = device.CreateRenderTarget(new GpuRenderTargetDescription(
                $"plugin-canvas-{_registration.Owner.Id}-{_registration.CanvasId}-{_targets.Count}",
                width,
                height,
                GpuTextureFormat.Rgba8UnormRenderTarget,
                DepthFormat: null,
                SampleCount: 1));
        }
        catch (Exception failure)
        {
            _targetsUnavailable = true;
            _report(
                $"Plugin canvas '{_registration.Owner.Id}/{_registration.CanvasId}' cannot be painted: "
                + $"no off-screen target ({width}x{height}): {failure.Message}.");
            return null;
        }
```

Replace `ReleaseTargets`' body after its doc comment

```csharp
    internal void ReleaseTargets()
    {
        if (_released) return;
        _released = true;
        _shown = null;
        if (ReferenceEquals(_registration.PointerRelease, _pointerRelease))
            _registration.PointerRelease = null;
        IGpuDevice device = _surface.Services.Device;
        foreach (CanvasTarget target in _targets)
        {
            device.ReleaseTextureSlot(target.Slot);
            target.Target.Dispose();
        }
        _targets.Clear();
    }
```

with

```csharp
    internal void ReleaseTargets()
    {
        if (_released) return;
        _released = true;
        if (ReferenceEquals(_registration.PointerRelease, _pointerRelease))
            _registration.PointerRelease = null;
        GiveBackTargets();
    }

    /// <summary>
    /// Reads the pixel scale for this frame. A change gives every target
    /// back -- the one shown too, since the repaint that follows in this
    /// same draw replaces it -- and invalidates, so the canvas is painted
    /// again at the new size. A target that could not be made at the old
    /// size may be possible at the new one, so that is tried afresh.
    /// </summary>
    private void FollowPixelScale()
    {
        float interfaceScale = CanvasPixelScale.ForInterface(
            _surface.Services.FramebufferPerPoint(), FindRoot()?.CanvasScale ?? Vector2.One);
        float scale = CanvasPixelScale.ForCanvas(
            interfaceScale,
            _registration.Width,
            _registration.Height,
            _surface.Services.Device.Capabilities.MaxImageDimension2D);
        if (scale == _pixelScale) return;
        _pixelScale = scale;
        GiveBackTargets();
        _targetsUnavailable = false;
        _registration.Invalidate();
    }

    private void GiveBackTargets()
    {
        _shown = null;
        IGpuDevice device = _surface.Services.Device;
        foreach (CanvasTarget target in _targets)
        {
            device.ReleaseTextureSlot(target.Slot);
            target.Target.Dispose();
        }
        _targets.Clear();
    }
```

- [ ] **Step 9: Build and run the tests**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | grep -E "rror\(s\)|arning\(s\)"`, then `dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~PluginCanvas&Lane!=Vulkan" 2>&1 | grep -E "Passed!|Failed!"`
Expected: `0 Warning(s)`, `0 Error(s)`; `Passed!`, 0 failed (every `PluginCanvasPointerTests` case too: they pass `null` for the new services argument, which means one pixel per point).

- [ ] **Step 10: Commit**

```bash
git add src/AcDream.App/UI/Layout/PluginCanvasSurface.cs src/AcDream.App/Composition/InteractionRetainedUiComposition.cs \
  src/AcDream.App/UI/Layout/PluginPainter.cs src/AcDream.App/UI/Layout/PluginCanvasElement.cs \
  tests/AcDream.App.Tests/Rendering/Gpu/RecordingGpuDevice.cs tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs \
  tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.HiDpi.cs
git commit -m "plugin canvas: painted at the display's pixel scale, laid out in canvas pixels

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/Layout/PluginCanvasSurface.cs", "src/AcDream.App/Composition/InteractionRetainedUiComposition.cs", "src/AcDream.App/UI/Layout/PluginPainter.cs", "src/AcDream.App/UI/Layout/PluginCanvasElement.cs", "tests/AcDream.App.Tests/Rendering/Gpu/RecordingGpuDevice.cs", "tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs", "tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.HiDpi.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~PluginCanvas&Lane!=Vulkan\"", "acceptanceCriteria": ["2x: paint sees (200,100,2.0), target 400x200, projection 400x200, verts reach (400,200), blit 200 wide", "1x unchanged", "fixed canvas 400x300 in 800x600 paints at 2", "scale change repaints once, retires old target, one 400x200 target", "350 limit gives 1.75 and 350x175", "pointer events in canvas pixels at 2x", "kept painter throws on PixelScale", "existing PluginCanvas tests pass"], "modelTier": "standard"}
```

---

### Task 6: Baking a font for a scale

**Goal:** The baker skips atlas sizes smaller than the glyphs' packed area, so a bake that cannot fit fails before drawing a glyph. It takes a cap on atlas bytes, and gains `TryBakeForScale`, which bakes at `size × s` or steps down a quarter at a time while above 1.

**Files:**
- Modify: `src/AcDream.App/UI/CanvasFontBaker.cs` (the whole file is given below; every change is an addition)
- Test: `tests/AcDream.App.Tests/UI/CanvasFontBakerTests.cs` (append four tests before the final `}`)

**Acceptance Criteria:**
- [ ] 16 px with `maximumAtlasBytes: 512*256` bakes into 512×256; one byte less fails with `1063 glyphs at 16 px do not fit a 256x256 atlas`; 1000 bytes fails with `no atlas fits in 1,000 bytes`
- [ ] `TryBakeForScale(16 px, 2)` bakes at 2 into 1024×512 at 32 px with no shortfall; the glyph set equals the 1× bake's and advances are exactly 2× (3 decimals)
- [ ] `TryBakeForScale(64 px, 2)` bakes at 1.75 (112 px) with shortfall `1063 glyphs at 128 px do not fit a 2048x2048 atlas`
- [ ] At scale 1, or with 1000 bytes of room at 1.25, nothing is baked, `bakedScale == 1`, and the shortfall is set
- [ ] Every existing baker test passes (same atlas sizes as before)

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~.CanvasFontBakerTests"` → `Passed!  - Failed:     0, Passed:    19`

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to `tests/AcDream.App.Tests/UI/CanvasFontBakerTests.cs`, inside the class)

```csharp

    [Fact]
    public void ABakeKeepsToTheAtlasBytesItIsGiven()
    {
        Assert.True(CanvasFontBaker.TryBake(
            Noto, 16f, CanvasFontBaker.DefaultRanges, 2048, out CanvasFontBake? bake, out _, maximumAtlasBytes: 512 * 256));
        Assert.Equal((512, 256), (bake.AtlasWidth, bake.AtlasHeight));

        Assert.False(CanvasFontBaker.TryBake(
            Noto, 16f, CanvasFontBaker.DefaultRanges, 2048, out _, out string? failure, maximumAtlasBytes: 512 * 256 - 1));
        Assert.Equal("1063 glyphs at 16 px do not fit a 256x256 atlas", failure);

        Assert.False(CanvasFontBaker.TryBake(
            Noto, 16f, CanvasFontBaker.DefaultRanges, 2048, out _, out failure, maximumAtlasBytes: 1000));
        Assert.Equal("no atlas fits in 1,000 bytes", failure);
    }

    [Fact]
    public void ASharperBakeHasTheSameGlyphsAtTheScale()
    {
        Assert.True(CanvasFontBaker.TryBake(Noto, 16f, CanvasFontBaker.DefaultRanges, 2048, out CanvasFontBake? own, out _));

        Assert.True(CanvasFontBaker.TryBakeForScale(
            Noto, 16f, 2f, CanvasFontBaker.DefaultRanges, 2048, long.MaxValue,
            out CanvasFontBake? sharp, out float baked, out string? shortfall));

        Assert.Equal(2f, baked);
        Assert.Null(shortfall);
        Assert.Equal(32f, sharp.PixelSize);
        Assert.Equal((1024, 512), (sharp.AtlasWidth, sharp.AtlasHeight));
        Assert.Equal(own.Glyphs.Keys.Order(), sharp.Glyphs.Keys.Order());
        // Advances scale exactly, so text laid out from the font's own bake lines up with the sharper glyphs.
        foreach (int codepoint in new[] { 'A', 'V', 'g', 0x416 })
            Assert.Equal(own.Glyphs[codepoint].Advance * 2f, sharp.Glyphs[codepoint].Advance, 3);
    }

    [Fact]
    public void ASharperBakeThatDoesNotFitStepsDownAQuarterAtATime()
    {
        Assert.True(CanvasFontBaker.TryBakeForScale(
            Noto, 64f, 2f, CanvasFontBaker.DefaultRanges, 2048, long.MaxValue,
            out CanvasFontBake? sharp, out float baked, out string? shortfall));

        Assert.Equal(1.75f, baked);
        Assert.Equal(112f, sharp.PixelSize);
        Assert.Equal("1063 glyphs at 128 px do not fit a 2048x2048 atlas", shortfall);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    public void NoSharperBakeIsMadeAtOneOrWhenNoStepFits(float scale)
    {
        long room = scale == 1f ? long.MaxValue : 1000;

        Assert.False(CanvasFontBaker.TryBakeForScale(
            Noto, 16f, scale, CanvasFontBaker.DefaultRanges, 2048, room,
            out _, out float baked, out string? shortfall));

        Assert.Equal(1f, baked);
        Assert.False(string.IsNullOrEmpty(shortfall));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -E "error CS" | sort -u | head -2`
Expected: `TryBake` has no parameter named `maximumAtlasBytes`, and `CanvasFontBaker` has no `TryBakeForScale`.

- [ ] **Step 3: The baker** (replace `src/AcDream.App/UI/CanvasFontBaker.cs` with)

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
/// holds them all. Sizes smaller than the glyphs' combined area are not
/// tried, so a bake that cannot fit fails before any glyph is drawn.
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

    /// <param name="fontBytes">The font file.</param>
    /// <param name="pixelSize">The size to bake at.</param>
    /// <param name="ranges">The characters to bake, where the font has them.</param>
    /// <param name="maximumGlyphs">The most glyphs the bake may hold.</param>
    /// <param name="bake">The bake, when it succeeds.</param>
    /// <param name="failure">Why it did not, when it does not.</param>
    /// <param name="maximumAtlasBytes">The largest atlas, in bytes, the bake may use.</param>
    internal static unsafe bool TryBake(
        byte[] fontBytes,
        float pixelSize,
        IReadOnlyList<(int First, int Last)> ranges,
        int maximumGlyphs,
        [NotNullWhen(true)] out CanvasFontBake? bake,
        [NotNullWhen(false)] out string? failure,
        long maximumAtlasBytes = long.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(fontBytes);
        ArgumentNullException.ThrowIfNull(ranges);
        bake = null;
        if (!float.IsFinite(pixelSize) || pixelSize <= 0f)
        {
            failure = "the size is not a positive number";
            return false;
        }

        if (!TryValidateSfnt(fontBytes, out failure))
            return false;

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
            long area = PackedArea(info, codepoints, scale);

            foreach ((int width, int height) in AtlasSizes)
            {
                long atlasBytes = (long)width * height;
                if (atlasBytes > maximumAtlasBytes)
                    break;
                if (atlasBytes < area)
                    continue;
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

            (int Width, int Height) largest = AtlasSizes.LastOrDefault(size => (long)size.Width * size.Height <= maximumAtlasBytes);
            failure = largest == default
                ? string.Create(CultureInfo.InvariantCulture, $"no atlas fits in {maximumAtlasBytes:N0} bytes")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{codepoints.Length} glyphs at {pixelSize} px do not fit a {largest.Width}x{largest.Height} atlas");
            return false;
        }
    }

    /// <summary>
    /// Bakes a font again for a canvas painted at <paramref name="scale"/>
    /// device pixels per canvas pixel: at <paramref name="pixelSize"/> times
    /// the scale, or, when that does not fit the atlas or the byte room, at
    /// the largest lower quarter step that does. Nothing is baked at a step
    /// of 1 or below -- the font's own bake is that.
    /// </summary>
    /// <param name="bakedScale">The step the bake was made at; 1 when none was.</param>
    /// <param name="shortfall">
    /// Why the bake is not at <paramref name="scale"/> itself: null when it
    /// is, and always set when nothing was baked.
    /// </param>
    internal static bool TryBakeForScale(
        byte[] fontBytes,
        float pixelSize,
        float scale,
        IReadOnlyList<(int First, int Last)> ranges,
        int maximumGlyphs,
        long maximumAtlasBytes,
        [NotNullWhen(true)] out CanvasFontBake? bake,
        out float bakedScale,
        out string? shortfall)
    {
        shortfall = null;
        for (float step = scale; step > 1f; step -= Layout.CanvasPixelScale.Step)
        {
            if (TryBake(fontBytes, pixelSize * step, ranges, maximumGlyphs, out bake, out string? failure, maximumAtlasBytes))
            {
                bakedScale = step;
                return true;
            }
            shortfall ??= failure;
        }
        shortfall ??= "the canvas is painted at one pixel per pixel";
        bake = null;
        bakedScale = 1f;
        return false;
    }

    /// <summary>
    /// The area the packer's rectangles take: each glyph's box at this scale
    /// plus the one pixel of padding the packer puts after it, as
    /// <c>stbtt_PackFontRangesGatherRects</c> measures them. No atlas
    /// smaller than this can hold them.
    /// </summary>
    private static unsafe long PackedArea(StbTrueType.stbtt_fontinfo info, int[] codepoints, float scale)
    {
        long area = 0;
        foreach (int codepoint in codepoints)
        {
            int x0, y0, x1, y1;
            StbTrueType.stbtt_GetGlyphBitmapBoxSubpixel(
                info, StbTrueType.stbtt_FindGlyphIndex(info, codepoint), scale, scale, 0f, 0f, &x0, &y0, &x1, &y1);
            area += (long)(x1 - x0 + 1) * (y1 - y0 + 1);
        }
        return area;
    }

    /// <summary>
    /// StbTrueTypeSharp does no bounds checking, so the sfnt header, table
    /// directory and every table's extent are checked here before the native
    /// parser sees the bytes. Residual risk: malformed outline data inside
    /// valid table bounds still reaches the native parser.
    /// </summary>
    private static bool TryValidateSfnt(byte[] bytes, [NotNullWhen(false)] out string? failure)
    {
        const string NotAFont = "the file is not a TrueType or OpenType font";
        failure = NotAFont;
        if (bytes.Length < 12)
            return false;
        uint version = ReadU32(bytes, 0);
        if (version == 0x74746366) // 'ttcf'
        {
            failure = "font collections are not supported";
            return false;
        }
        if (version != 0x00010000 && version != 0x4F54544F && version != 0x74727565)
            return false;
        int numTables = (bytes[4] << 8) | bytes[5];
        if (numTables < 1 || 12L + 16L * numTables > bytes.Length)
            return false;
        var tags = new HashSet<uint>();
        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + 16 * i;
            long offset = ReadU32(bytes, record + 8);
            long length = ReadU32(bytes, record + 12);
            if (offset + length > bytes.Length)
                return false;
            tags.Add(ReadU32(bytes, record));
        }
        foreach (string required in new[] { "cmap", "head", "hhea", "hmtx" })
        {
            if (!tags.Contains(Tag(required)))
            {
                failure = $"the font is missing its '{required}' table";
                return false;
            }
        }
        if (!tags.Contains(Tag("CFF ")) && !(tags.Contains(Tag("glyf")) && tags.Contains(Tag("loca"))))
        {
            failure = "the font is missing its outlines ('glyf' and 'loca', or 'CFF ')";
            return false;
        }
        failure = null;
        return true;
    }

    private static uint ReadU32(byte[] b, int at) =>
        ((uint)b[at] << 24) | ((uint)b[at + 1] << 16) | ((uint)b[at + 2] << 8) | b[at + 3];

    private static uint Tag(string tag) => ReadU32(System.Text.Encoding.ASCII.GetBytes(tag), 0);

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
            failure = string.Create(
                CultureInfo.InvariantCulture,
                $"the font has {present.Count} of the requested characters; the most a font may prepare is {maximumGlyphs}");
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

The lower bound is exact for the packer: `stbtt_PackFontRangesGatherRects` makes each rectangle the glyph's box at `stbtt_ScaleForPixelHeight(size)`, plus `padding + oversample − 1` = 1 pixel on each axis. No atlas whose area is below the sum can hold them, so skipping those sizes changes no outcome; it only saves the failed packing attempts. Measured on this Mac: 16 px at 2× takes 1024×512 in ~9 ms, 32 px at 2× takes 2048×1024 in ~24 ms, and 64 px at 2× fails after ~23 ms of packing (the bound passes but the packer does not), then fits at 1.75× in 2048×2048 (~47 ms in all).

- [ ] **Step 4: Run the tests**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -E "rror\(s\)|arning\(s\)"`, then `dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~.CanvasFontBakerTests" 2>&1 | grep -E "Passed!|Failed!"`
Expected: `0 Warning(s)`; `Passed!  - Failed:     0, Passed:    19`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/CanvasFontBaker.cs tests/AcDream.App.Tests/UI/CanvasFontBakerTests.cs
git commit -m "plugin fonts: bake a font for a scaled canvas, a quarter lower when it does not fit

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/CanvasFontBaker.cs", "tests/AcDream.App.Tests/UI/CanvasFontBakerTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~.CanvasFontBakerTests\"", "acceptanceCriteria": ["byte cap respected with exact messages", "16px at 2 bakes 1024x512, same glyphs, advances 2x", "64px at 2 steps down to 1.75 with exact shortfall", "scale 1 or no room bakes nothing", "existing baker tests pass"], "modelTier": "standard"}
```

---

### Task 7: Text from a sharper bake, on device pixels

**Goal:** A `CanvasFont` holds its file in a `CanvasFontFace` (bytes plus the parser's copy) and can carry a sharper bake (`CanvasSharpGlyphs`). On a scaled canvas, canvas-font text draws its glyphs from that bake at `1/scale` size, snapped to device pixels. The pen, kerning, baseline metrics and `MeasureWidth` still come from the font's own bake. The painter passes its scale.

**Files:**
- Modify: `src/AcDream.App/UI/CanvasFont.cs` (whole file below)
- Modify: `src/AcDream.App/UI/UiRenderContext.cs` (`DrawStringCanvasFont` and `DrawCanvasFontPass`, ~:463-516)
- Modify: `src/AcDream.App/UI/Layout/PluginPainter.cs` (the font `DrawText`)
- Test: `tests/AcDream.App.Tests/UI/CanvasFontTests.cs` (append before the final `}`)

**Acceptance Criteria:**
- [ ] At pixel scale 2 with a 2× bake, "AV" draws in one coverage run of the sharper atlas. The second glyph's left edge is `floor((pen + V.OffsetX/2)·2 + 0.5)/2`, with the pen from the font's own advances and kerning; its width is `V.Width/2`; every y is a multiple of 0.5
- [ ] A sharper bake changes neither `MeasureWidth` nor `LineHeight`/`Ascent`
- [ ] At pixel scale 1 the font's own atlas is used even when a sharper bake exists
- [ ] At pixel scale 2 without a sharper bake, the font's own atlas is used on whole pixels
- [ ] Every existing `CanvasFontTests` case passes unchanged

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~.CanvasFontTests"` → `Passed!  - Failed:     0, Passed:    11`

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to `tests/AcDream.App.Tests/UI/CanvasFontTests.cs`, inside the class)

```csharp

    private const uint SharpAtlas = 43u;

    /// <summary>The font of <see cref="Bake"/>, given its 2x bake as a canvas scale of 2 would.</summary>
    private static CanvasFont BakeWithSharp()
    {
        CanvasFont font = Bake(BundledUiFont.ReadEmbeddedFontBytes());
        Assert.True(CanvasFontBaker.TryBakeForScale(
            font.Face.Bytes, font.PixelSize, 2f, CanvasFontBaker.DefaultRanges, 2048, long.MaxValue,
            out CanvasFontBake? sharp, out float baked, out _));
        font.PrepareSharp(2f, new CanvasSharpGlyphs(SharpAtlas, baked, sharp.Glyphs, (long)sharp.AtlasWidth * sharp.AtlasHeight));
        return font;
    }

    private static (TextRenderer Renderer, UiRenderContext Context, RecordingGpuDevice Device) Rig()
    {
        var device = new RecordingGpuDevice();
        var renderer = new TextRenderer(device, new NullFrames(), "unused");
        renderer.Begin(new Vector2(200f, 50f));
        return (renderer, new UiRenderContext(renderer, new Vector2(200f, 50f)), device);
    }

    [Fact]
    public void OnAScaledCanvasGlyphsComeFromTheSharperBakeOnDevicePixels()
    {
        using CanvasFont font = BakeWithSharp();
        (TextRenderer renderer, UiRenderContext context, RecordingGpuDevice device) = Rig();
        using (device)
        using (renderer)
        {
            context.DrawStringCanvasFont(font, "AV", 10.3f, 5.2f, Vector4.One, pixelScale: 2f);

            Assert.Equal([SharpAtlas], renderer.DebugSpriteSegmentCoverage);
            (uint _, IReadOnlyList<float> verts) = Assert.Single(renderer.DebugSpriteSegmentVerts);
            Assert.True(font.TryGetGlyph('A', out CanvasGlyph a));
            Assert.True(font.TryGetGlyph('V', out CanvasGlyph v));
            Assert.True(font.Sharp!.TryGetGlyph('V', out CanvasGlyph sharpV));
            // The pen is the font's own; the glyph's box is the sharper bake's, halved, on half pixels.
            float pen = 10.3f + a.Advance + font.Kerning(a.GlyphIndex, v.GlyphIndex);
            float expectedLeft = MathF.Floor((pen + sharpV.OffsetX / 2f) * 2f + 0.5f) / 2f;
            float[] second = Enumerable.Range(6, 6).Select(i => verts[i * TextRenderer.FloatsPerVertex]).ToArray();
            Assert.Equal(expectedLeft, second.Min());
            Assert.Equal(sharpV.Width / 2f, second.Max() - second.Min(), 4);
            float[] ys = Enumerable.Range(0, 12).Select(i => verts[i * TextRenderer.FloatsPerVertex + 1]).ToArray();
            Assert.All(ys, y => Assert.Equal(MathF.Round(y * 2f), y * 2f, 4));
        }
    }

    [Fact]
    public void ASharperBakeChangesNoMeasurement()
    {
        using CanvasFont plain = Bake(BundledUiFont.ReadEmbeddedFontBytes());
        using CanvasFont sharp = BakeWithSharp();

        Assert.Equal(plain.MeasureWidth("BuffProfile AV Ж"), sharp.MeasureWidth("BuffProfile AV Ж"));
        Assert.Equal((plain.LineHeight, plain.Ascent), (sharp.LineHeight, sharp.Ascent));
    }

    [Fact]
    public void AtOnePixelPerPixelTheSharperBakeIsNotUsed()
    {
        using CanvasFont font = BakeWithSharp();
        (TextRenderer renderer, UiRenderContext context, RecordingGpuDevice device) = Rig();
        using (device)
        using (renderer)
        {
            context.DrawStringCanvasFont(font, "AV", 10.3f, 5f, Vector4.One);

            Assert.Equal([Atlas], renderer.DebugSpriteSegmentCoverage);
        }
    }

    [Fact]
    public void WithoutASharperBakeAScaledCanvasDrawsTheFontsOwnGlyphsOnWholePixels()
    {
        using CanvasFont font = Bake(BundledUiFont.ReadEmbeddedFontBytes());
        (TextRenderer renderer, UiRenderContext context, RecordingGpuDevice device) = Rig();
        using (device)
        using (renderer)
        {
            context.DrawStringCanvasFont(font, "AV", 10.3f, 5.2f, Vector4.One, pixelScale: 2f);

            Assert.Equal([Atlas], renderer.DebugSpriteSegmentCoverage);
            (uint _, IReadOnlyList<float> verts) = Assert.Single(renderer.DebugSpriteSegmentVerts);
            Assert.All(
                Enumerable.Range(0, 12).Select(i => verts[i * TextRenderer.FloatsPerVertex]),
                x => Assert.Equal(MathF.Round(x), x));
        }
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -E "error CS" | sort -u | head -3`
Expected: `CanvasSharpGlyphs` not found; `CanvasFont` has no `Face`/`PrepareSharp`/`Sharp`; `DrawStringCanvasFont` has no `pixelScale`.

- [ ] **Step 3: The font, its face and its sharper glyphs** (replace `src/AcDream.App/UI/CanvasFont.cs` with)

```csharp
using System.Text;
using StbTrueTypeSharp;

namespace AcDream.App.UI;

/// <summary>
/// One font file as a canvas font keeps it: the bytes, which a sharper
/// bake is made from, and the native parser's copy, which kerning is looked
/// up in. Every size baked from the same file can share one face.
/// </summary>
internal sealed class CanvasFontFace : IDisposable
{
    private StbTrueType.stbtt_fontinfo? _info;

    /// <param name="bytes">
    /// Font bytes that have already passed <see cref="CanvasFontBaker.TryBake"/>:
    /// the bake validates the sfnt structure, and the native parser is
    /// handed the same bytes here.
    /// </param>
    internal CanvasFontFace(byte[] bytes)
    {
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        _info = StbTrueType.CreateFont(bytes, 0);
    }

    internal byte[] Bytes { get; }

    /// <summary>The parser's view of the file; null once disposed, or when the parser refused it.</summary>
    internal StbTrueType.stbtt_fontinfo? Info => _info;

    public void Dispose()
    {
        _info?.Dispose();
        _info = null;
    }
}

/// <summary>
/// A canvas font's glyphs baked again at <see cref="Scale"/> times its size,
/// for a canvas painted at that many device pixels per canvas pixel. The
/// boxes are in device pixels at that scale; the font's own bake still sets
/// every advance, kerning and line metric, so text measures the same at
/// every scale.
/// </summary>
internal sealed record CanvasSharpGlyphs(
    uint AtlasTexture,
    float Scale,
    IReadOnlyDictionary<int, CanvasGlyph> Glyphs,
    long AtlasBytes)
{
    /// <summary>The glyph for a code point, falling back to <c>?</c> exactly as the font's own bake does.</summary>
    internal bool TryGetGlyph(int codepoint, out CanvasGlyph glyph) =>
        Glyphs.TryGetValue(codepoint, out glyph) || Glyphs.TryGetValue('?', out glyph);
}

/// <summary>
/// A baked font a canvas draws text with: the glyph metrics, the atlas
/// texture they index, and the font's kerning, looked up in its
/// <see cref="CanvasFontFace"/>. The atlas textures -- its own and a sharper
/// bake's -- belong to whoever uploaded them.
/// </summary>
internal sealed class CanvasFont : IDisposable
{
    private const int KerningCacheLimit = 4096;

    private readonly IReadOnlyDictionary<int, CanvasGlyph> _glyphs;
    private readonly float _scale;
    private readonly Dictionary<(int Left, int Right), float> _kerningCache = [];
    private readonly bool _ownsFace;
    private bool _disposed;

    /// <param name="bake">The bake of <paramref name="fontBytes"/>.</param>
    /// <param name="atlasTexture">The uploaded atlas texture the glyphs index.</param>
    /// <param name="fontBytes">
    /// Font bytes that have already passed <see cref="CanvasFontBaker.TryBake"/>.
    /// The font keeps them, in a face of its own, for kerning and for a
    /// sharper bake.
    /// </param>
    internal CanvasFont(CanvasFontBake bake, uint atlasTexture, byte[] fontBytes)
        : this(bake, atlasTexture, new CanvasFontFace(fontBytes ?? throw new ArgumentNullException(nameof(fontBytes))), ownsFace: true)
    {
    }

    /// <param name="bake">The bake of the face's bytes.</param>
    /// <param name="atlasTexture">The uploaded atlas texture the glyphs index.</param>
    /// <param name="face">The file the bake was made from.</param>
    /// <param name="ownsFace">Whether disposing the font disposes the face; false for a face shared between sizes.</param>
    internal CanvasFont(CanvasFontBake bake, uint atlasTexture, CanvasFontFace face, bool ownsFace)
    {
        ArgumentNullException.ThrowIfNull(bake);
        Face = face ?? throw new ArgumentNullException(nameof(face));
        _ownsFace = ownsFace;
        AtlasTexture = atlasTexture;
        PixelSize = bake.PixelSize;
        LineHeight = bake.LineHeight;
        Ascent = bake.Ascent;
        _glyphs = bake.Glyphs;
        _scale = bake.Scale;
    }

    internal CanvasFontFace Face { get; }

    internal uint AtlasTexture { get; }

    internal float PixelSize { get; }

    internal float LineHeight { get; }

    internal float Ascent { get; }

    /// <summary>The sharper bake text is drawn from on a scaled canvas, or null to draw from the font's own.</summary>
    internal CanvasSharpGlyphs? Sharp { get; private set; }

    /// <summary>
    /// The canvas scale <see cref="Sharp"/> was last prepared for. It is
    /// kept even when no sharper bake could be made, or only a lower step,
    /// so the same scale is not tried again on every paint.
    /// </summary>
    internal float PreparedScale { get; private set; } = 1f;

    /// <summary>Records what was prepared for a scale and hands back the bake it replaces, for its texture to be given back.</summary>
    internal CanvasSharpGlyphs? PrepareSharp(float scale, CanvasSharpGlyphs? sharp)
    {
        CanvasSharpGlyphs? previous = Sharp;
        Sharp = sharp;
        PreparedScale = scale;
        return previous;
    }

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
        if (_disposed || Face.Info is not { } info) return 0f;
        if (_kerningCache.TryGetValue((leftGlyphIndex, rightGlyphIndex), out float cached))
            return cached;
        if (_kerningCache.Count >= KerningCacheLimit)
            _kerningCache.Clear();
        float kerning = StbTrueType.stbtt_GetGlyphKernAdvance(info, leftGlyphIndex, rightGlyphIndex) * _scale;
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
        if (_disposed) return;
        _disposed = true;
        if (_ownsFace)
            Face.Dispose();
    }
}
```

The three-argument constructor makes a face of its own, as the font made its own parser copy before, so `PluginFontTable` and existing tests are unchanged. A face whose parser refused the bytes has `Info == null`, and kerning is then 0, as before.

- [ ] **Step 4: Text at a scale** (`src/AcDream.App/UI/UiRenderContext.cs`)

Replace `DrawStringCanvasFont`, `DrawCanvasFontPass` and the doc comment above them (from `/// Draws one line of text in a baked canvas font` to the end of `DrawCanvasFontPass`) with:

```csharp
    /// <summary>
    /// Draws one line of text in a baked canvas font, its top-left corner at
    /// (<paramref name="x"/>, <paramref name="y"/>). Glyphs are coverage
    /// sprites, so the text keeps its place among fills and images. The
    /// baseline and each glyph's left edge snap to whole pixels while the pen
    /// keeps its fractional advance, as the interface font does. The outline
    /// is eight copies one pixel out, drawn first.
    ///
    /// <para>On a canvas painted at a <paramref name="pixelScale"/> above 1,
    /// glyphs come from the font's sharper bake when it has one, and snap
    /// to that bake's pixels instead: whole device pixels when the bake
    /// matches the canvas. The pen, and so every position the text is
    /// measured at, still comes from the font's own bake.</para>
    /// </summary>
    internal void DrawStringCanvasFont(
        CanvasFont font, string text, float x, float y, Vector4 color,
        bool outline = false, Vector4? outlineColor = null, float pixelScale = 1f)
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
                        DrawCanvasFontPass(font, text, x + dx, y + dy, shadow, pixelScale);
                }
            }
        }
        DrawCanvasFontPass(font, text, x, y, color, pixelScale);
    }

    private void DrawCanvasFontPass(CanvasFont font, string text, float x, float y, Vector4 color, float pixelScale)
    {
        CanvasSharpGlyphs? sharp = pixelScale > 1f ? font.Sharp : null;
        // Pixels per canvas pixel of the grid glyphs snap to: the sharper
        // bake's, but no finer than the canvas's own device pixels.
        float grid = sharp is null ? 1f : MathF.Min(pixelScale, sharp.Scale);
        float pen = _current.X + x;
        float baseline = Snap(_current.Y + y + font.Ascent, grid);
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
            // Canvas pixels per atlas pixel of the glyph drawn.
            float texel = 1f;
            CanvasGlyph drawn = glyph;
            uint atlas = font.AtlasTexture;
            if (sharp is not null && sharp.TryGetGlyph(rune.Value, out CanvasGlyph fine))
            {
                texel = 1f / sharp.Scale;
                drawn = fine;
                atlas = sharp.AtlasTexture;
            }
            if (drawn.Width > 0f && drawn.Height > 0f)
            {
                float gx = Snap(pen + drawn.OffsetX * texel, grid);
                float gy = baseline + MathF.Round(drawn.OffsetY * texel * grid) / grid;
                DrawCoverageSpriteAbsolute(
                    atlas, gx, gy, drawn.Width * texel, drawn.Height * texel,
                    drawn.U0, drawn.V0, drawn.U1, drawn.V1, color);
            }
            pen += glyph.Advance;
            previous = glyph.GlyphIndex;
        }
    }

    /// <summary>Rounds half up to the nearest multiple of 1 / <paramref name="pixelsPerUnit"/>.</summary>
    private static float Snap(float value, float pixelsPerUnit) =>
        MathF.Floor(value * pixelsPerUnit + 0.5f) / pixelsPerUnit;
```

At `pixelScale` 1, `grid` is 1, `texel` is 1 and `Snap(v, 1)` is `MathF.Floor(v + 0.5f)`: exactly the old arithmetic, which the existing tests pin. The outline offsets stay one canvas pixel.

- [ ] **Step 5: The painter passes its scale** (`src/AcDream.App/UI/Layout/PluginPainter.cs`)

In `DrawText(string text, PluginPoint position, PluginColor color, PluginFont font, bool outline = false)`, replace

```csharp
        context.DrawStringCanvasFont(resolved, text, (float)position.X, (float)position.Y, ToVector(color), outline);
```

with

```csharp
        context.DrawStringCanvasFont(
            resolved, text, (float)position.X, (float)position.Y, ToVector(color), outline, pixelScale: _pixelScale);
```

- [ ] **Step 6: Run the tests**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | grep -E "rror\(s\)|arning\(s\)"`, then `dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "(FullyQualifiedName~Font|FullyQualifiedName~PluginCanvas)&Lane!=Vulkan" 2>&1 | grep -E "Passed!|Failed!"`
Expected: `0 Warning(s)`; `Passed!`, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.App/UI/CanvasFont.cs src/AcDream.App/UI/UiRenderContext.cs src/AcDream.App/UI/Layout/PluginPainter.cs \
  tests/AcDream.App.Tests/UI/CanvasFontTests.cs
git commit -m "plugin fonts: scaled canvases draw text from a sharper bake on device pixels

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/CanvasFont.cs", "src/AcDream.App/UI/UiRenderContext.cs", "src/AcDream.App/UI/Layout/PluginPainter.cs", "tests/AcDream.App.Tests/UI/CanvasFontTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~.CanvasFontTests\"", "acceptanceCriteria": ["2x text from sharper atlas, pen from own bake, half-pixel grid", "sharper bake changes no measurement", "scale 1 ignores sharper bake", "2x without sharper bake uses own atlas on whole pixels", "existing CanvasFontTests pass"], "modelTier": "standard"}
```

---

### Task 8: Fonts readied for a scale

**Goal:** `PluginFontTable.PrepareScale(s)` gives every held font a sharper bake at `s`, or the largest step that fits, through `CanvasFontSharpening`. Bundled fonts are prepared once in the shared cache, which now shares one face across sizes. The plugin's own sharper bakes count against `PluginFontBudget.MaximumSharpBytes` (32 MiB), not `MaximumBytes`. A font drawn below the scale is reported once. Another scale, or 1, gives the old bakes back. Font sizes keep to quarter pixels.

**Files:**
- Create: `src/AcDream.App/Plugins/CanvasFontSharpening.cs`
- Modify: `src/AcDream.App/Plugins/BundledCanvasFontCache.cs` (whole file below)
- Modify: `src/AcDream.App/Plugins/PluginFontTable.cs` (whole file below)
- Modify: `src/AcDream.Plugin.Abstractions/PluginFonts.cs` (the `PixelSize` param doc and the two `pixelSize` param docs)
- Test: `tests/AcDream.App.Tests/Plugins/BundledCanvasFontCacheTests.cs` (append three tests)
- Test: `tests/AcDream.App.Tests/Plugins/PluginFontTableScaleTests.cs` (create)

**Acceptance Criteria:**
- [ ] Two bundled sizes share one `Face`. A size's sharper bake is uploaded once (1024×512 at 16 px × 2) however many hold it, and goes back with the last release, sharper atlas first. Disposing the cache gives back sharper bakes and disposes the face
- [ ] `PrepareScale(2)` gives a bundled and an own font 2× bakes with unchanged handles, measurements and `OwnedBytes`; `SharpBytes` equals the own font's sharper atlas; nothing is reported
- [ ] The same scale again uploads nothing; 2 → 1.5 releases the 2× atlas; 1.5 → 1 releases the 1.5× atlas and `SharpBytes` returns to 0; releasing a font releases its sharper atlas, then its own
- [ ] The bundled font at 64 px prepares at 1.75 and reports exactly once: `Plugin 'example.plugin': the bundled font at 64 px is drawn at 1.75x on canvases painted at 2x, so it is less sharp than it could be: 1063 glyphs at 128 px do not fit a 2048x2048 atlas.`
- [ ] With `MaximumSharpBytes = 65536`, the first own font gets its 2× bake and the second gets none, reported as `… font 'f.otf' at 20 px is drawn at 1x on canvases painted at 2x, so it is less sharp than it could be: the plugin's sharper fonts have 0 of their 65,536 bytes left.`, while the bundled font still gets 2×
- [ ] 16.1 → 16, 16.125 → 16.25, 16.3 → 16.25, 63.9 → 64, for bundled and own fonts; the existing out-of-range cases (5.9, 64.1, NaN) are still refused
- [ ] Every existing font test passes

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~Font&Lane!=Vulkan"` → `Passed!`, 0 failed (`BundledCanvasFontCacheTests` 5, `PluginFontTableScaleTests` 10)

**Steps:**

- [ ] **Step 1: Write the failing tests**

Append to `tests/AcDream.App.Tests/Plugins/BundledCanvasFontCacheTests.cs`, inside the class:

```csharp

    [Fact]
    public void EverySizeSharesOneFace()
    {
        var backend = new PluginFontTableTests.FakeFontBackend();
        using var cache = new BundledCanvasFontCache(backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);

        CanvasFont? sixteen = cache.Acquire(16f, out _);
        CanvasFont? twenty = cache.Acquire(20f, out _);

        Assert.Same(sixteen!.Face, twenty!.Face);
    }

    [Fact]
    public void ASizesSharperBakeIsMadeOnceForEveryHolderAndGoesWithTheLastRelease()
    {
        var backend = new PluginFontTableTests.FakeFontBackend();
        using var cache = new BundledCanvasFontCache(backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);
        CanvasFont font = cache.Acquire(16f, out _)!;
        cache.Acquire(16f, out _);

        cache.PrepareScale(font, 2f, out string? shortfall);
        cache.PrepareScale(font, 2f, out _);

        Assert.Null(shortfall);
        Assert.Equal(2f, font.Sharp!.Scale);
        Assert.Equal(2, backend.Uploaded.Count);
        Assert.Equal((1024, 512), (backend.Uploaded[1].Width, backend.Uploaded[1].Height));
        cache.Release(font);
        Assert.Empty(backend.Released);
        cache.Release(font);
        Assert.Equal(
            [PluginFontTableTests.FakeFontBackend.FirstTexture + 1, PluginFontTableTests.FakeFontBackend.FirstTexture],
            backend.Released);
    }

    [Fact]
    public void DisposingGivesBackSharperBakesToo()
    {
        var backend = new PluginFontTableTests.FakeFontBackend();
        var cache = new BundledCanvasFontCache(backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);
        CanvasFont font = cache.Acquire(16f, out _)!;
        cache.PrepareScale(font, 2f, out _);

        cache.Dispose();

        Assert.Equal(2, backend.Released.Count);
        Assert.Null(font.Face.Info);
    }
```

Create `tests/AcDream.App.Tests/Plugins/PluginFontTableScaleTests.cs`:

```csharp
using AcDream.App.Plugins;
using AcDream.App.Tests.UI;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// A plugin's fonts readied for a scaled canvas: each gets a sharper bake at
/// the scale, or the largest step that fits, without changing its handle or
/// its metrics; the plugin's own count against a budget of their own; the
/// bake for another scale is given back; and sizes keep to quarter pixels.
/// </summary>
public sealed class PluginFontTableScaleTests
{
    private sealed class Rig
    {
        public PluginFontTableTests.FakeFontBackend Backend { get; } = new();
        public BundledCanvasFontCache Bundled { get; }
        public List<string> Reports { get; } = [];
        public PluginFontTable Table { get; }

        public Rig(PluginFontBudget? budget = null)
        {
            Bundled = new BundledCanvasFontCache(Backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);
            Table = new PluginFontTable("example.plugin", budget ?? PluginFontBudget.Default, Reports.Add);
            Table.Bind(Backend, Bundled, Environment.CurrentManagedThreadId);
        }

        public CanvasFont Resolve(PluginFont font)
        {
            Assert.True(Table.TryResolve(font, out CanvasFont? resolved));
            return resolved;
        }
    }

    private static Func<Stream> Otf() => () => new MemoryStream(CanvasFontBakerTests.CffFixture());

    [Fact]
    public void EveryHeldFontGetsASharperBakeAndKeepsItsHandleAndMetrics()
    {
        var rig = new Rig();
        PluginFont bundled = rig.Table.AcquireBundled(16f);
        PluginFont own = rig.Table.AcquireStream("f.otf", Otf(), 16f, null);
        float width = rig.Resolve(bundled).MeasureWidth("AVTo?");
        long owned = rig.Table.OwnedBytes;

        rig.Table.PrepareScale(2f);

        Assert.Equal(2f, rig.Resolve(bundled).Sharp!.Scale);
        Assert.Equal(2f, rig.Resolve(own).Sharp!.Scale);
        Assert.Equal(bundled, rig.Table.AcquireBundled(16f));
        Assert.Equal(width, rig.Resolve(bundled).MeasureWidth("AVTo?"));
        Assert.Equal(owned, rig.Table.OwnedBytes);
        // Only the plugin's own font's sharper atlas counts, and against its own budget.
        Assert.Equal(rig.Resolve(own).Sharp!.AtlasBytes, rig.Table.SharpBytes);
        Assert.Empty(rig.Reports);
    }

    [Fact]
    public void TheSameScaleAgainBakesNothing()
    {
        var rig = new Rig();
        rig.Table.AcquireBundled(16f);
        rig.Table.AcquireStream("f.otf", Otf(), 16f, null);
        rig.Table.PrepareScale(2f);
        int uploads = rig.Backend.Uploaded.Count;

        rig.Table.PrepareScale(2f);

        Assert.Equal(uploads, rig.Backend.Uploaded.Count);
    }

    [Fact]
    public void AnotherScaleGivesTheOldSharperBakesBackAndOneGivesThemAllBack()
    {
        var rig = new Rig();
        PluginFont own = rig.Table.AcquireStream("f.otf", Otf(), 16f, null);
        rig.Table.PrepareScale(2f);
        uint twoX = rig.Resolve(own).Sharp!.AtlasTexture;

        rig.Table.PrepareScale(1.5f);

        Assert.Equal([twoX], rig.Backend.Released);
        Assert.Equal(1.5f, rig.Resolve(own).Sharp!.Scale);
        uint oneAndAHalfX = rig.Resolve(own).Sharp!.AtlasTexture;

        rig.Table.PrepareScale(1f);

        Assert.Null(rig.Resolve(own).Sharp);
        Assert.Equal([twoX, oneAndAHalfX], rig.Backend.Released);
        Assert.Equal(0L, rig.Table.SharpBytes);
    }

    [Fact]
    public void ReleasingAFontGivesBackItsSharperBake()
    {
        var rig = new Rig();
        PluginFont own = rig.Table.AcquireStream("f.otf", Otf(), 16f, null);
        rig.Table.PrepareScale(2f);
        CanvasFont resolved = rig.Resolve(own);
        uint sharp = resolved.Sharp!.AtlasTexture;

        rig.Table.Release(own);

        Assert.Equal([sharp, resolved.AtlasTexture], rig.Backend.Released);
        Assert.Equal(0L, rig.Table.SharpBytes);
    }

    [Fact]
    public void AFontTooLargeToBakeAtTheScaleIsBakedAtTheLargestStepThatFitsAndReportedOnce()
    {
        var rig = new Rig();
        PluginFont large = rig.Table.AcquireBundled(64f);

        rig.Table.PrepareScale(2f);
        rig.Table.PrepareScale(2f);

        Assert.Equal(1.75f, rig.Resolve(large).Sharp!.Scale);
        string report = Assert.Single(rig.Reports);
        Assert.Equal(
            "Plugin 'example.plugin': the bundled font at 64 px is drawn at 1.75x on canvases painted at 2x, "
            + "so it is less sharp than it could be: 1063 glyphs at 128 px do not fit a 2048x2048 atlas.",
            report);
    }

    [Fact]
    public void ThePluginsOwnFontsStayWithinTheirSharperBudget()
    {
        // The fixture's five glyphs fit the smallest atlas at any scale: room for one sharper bake.
        var rig = new Rig(PluginFontBudget.Default with { MaximumSharpBytes = 256 * 256 });
        PluginFont first = rig.Table.AcquireStream("f.otf", Otf(), 16f, null);
        PluginFont second = rig.Table.AcquireStream("f.otf", Otf(), 20f, null);
        PluginFont bundled = rig.Table.AcquireBundled(16f);

        rig.Table.PrepareScale(2f);

        Assert.Equal(2f, rig.Resolve(first).Sharp!.Scale);
        Assert.Null(rig.Resolve(second).Sharp);
        Assert.Equal(256L * 256, rig.Table.SharpBytes);
        // The shared bundled font is not the plugin's to budget.
        Assert.Equal(2f, rig.Resolve(bundled).Sharp!.Scale);
        string report = Assert.Single(rig.Reports);
        Assert.Equal(
            "Plugin 'example.plugin': font 'f.otf' at 20 px is drawn at 1x on canvases painted at 2x, "
            + "so it is less sharp than it could be: the plugin's sharper fonts have 0 of their 65,536 bytes left.",
            report);
    }

    [Theory]
    [InlineData(16.1f, 16f)]
    [InlineData(16.125f, 16.25f)]
    [InlineData(16.3f, 16.25f)]
    [InlineData(63.9f, 64f)]
    public void SizesKeepToQuarterPixels(float asked, float prepared)
    {
        var rig = new Rig();

        PluginFont font = rig.Table.AcquireBundled(asked);

        Assert.Equal(prepared, font.PixelSize);
        Assert.Equal(font, rig.Table.AcquireBundled(prepared));
        Assert.Equal(prepared, rig.Table.AcquireStream("f.otf", Otf(), asked, null).PixelSize);
    }
}
```

The fixture font has five glyphs, so it fits the smallest (256×256) atlas at any scale: the 65,536-byte budget is room for exactly one sharper bake.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -E "error CS" | sort -u | head -3`
Expected: `BundledCanvasFontCache` has no `PrepareScale`; `PluginFontTable` has no `PrepareScale`/`SharpBytes`; `PluginFontBudget` has no `MaximumSharpBytes`.

- [ ] **Step 3: Sharpening one font** (`src/AcDream.App/Plugins/CanvasFontSharpening.cs`)

```csharp
using AcDream.App.UI;

namespace AcDream.App.Plugins;

/// <summary>
/// Prepares a canvas font for the scale canvases are painted at: bakes its
/// glyphs again at that multiple of its size, or the largest lower step that
/// fits, uploads them, and gives back the bake they replace. At a scale of
/// 1 the sharper bake is let go. The font's own bake, its metrics and every
/// handle to it stay as they were.
/// </summary>
internal static class CanvasFontSharpening
{
    /// <param name="font">The font to prepare.</param>
    /// <param name="scale">The canvas scale.</param>
    /// <param name="ranges">The characters the font was baked with.</param>
    /// <param name="maximumGlyphs">The glyph ceiling the font was baked under.</param>
    /// <param name="maximumAtlasBytes">The largest sharper atlas the font may have.</param>
    /// <param name="backend">Where atlases are uploaded and given back.</param>
    /// <param name="debugName">The font's atlas name; the step is appended.</param>
    /// <param name="shortfall">Why the font is not drawn at <paramref name="scale"/> itself, or null.</param>
    /// <returns>How many bytes of sharper atlas the font holds now less what it held before.</returns>
    internal static long Prepare(
        CanvasFont font,
        float scale,
        IReadOnlyList<(int First, int Last)> ranges,
        int maximumGlyphs,
        long maximumAtlasBytes,
        IPluginFontBackend backend,
        string debugName,
        out string? shortfall)
    {
        long before = font.Sharp?.AtlasBytes ?? 0L;
        CanvasSharpGlyphs? next = null;
        shortfall = null;
        if (scale > 1f)
        {
            if (CanvasFontBaker.TryBakeForScale(
                    font.Face.Bytes, font.PixelSize, scale, ranges, maximumGlyphs, maximumAtlasBytes,
                    out CanvasFontBake? bake, out float baked, out shortfall))
            {
                uint texture = backend.UploadCoverage(
                    bake.Coverage, bake.AtlasWidth, bake.AtlasHeight,
                    FormattableString.Invariant($"{debugName}-x{baked}"));
                next = new CanvasSharpGlyphs(texture, baked, bake.Glyphs, (long)bake.AtlasWidth * bake.AtlasHeight);
            }
        }
        CanvasSharpGlyphs? replaced = font.PrepareSharp(scale, next);
        if (replaced is not null)
            backend.ReleaseCoverage(replaced.AtlasTexture);
        return (next?.AtlasBytes ?? 0L) - before;
    }

    /// <summary>Gives back a font's sharper bake, when it has one; the font's own atlas is the caller's.</summary>
    /// <returns>The bytes of sharper atlas let go.</returns>
    internal static long Release(CanvasFont font, IPluginFontBackend backend)
    {
        CanvasSharpGlyphs? replaced = font.PrepareSharp(1f, null);
        if (replaced is null) return 0L;
        backend.ReleaseCoverage(replaced.AtlasTexture);
        return replaced.AtlasBytes;
    }
}
```

- [ ] **Step 4: The bundled cache** (replace `src/AcDream.App/Plugins/BundledCanvasFontCache.cs` with)

```csharp
using AcDream.App.UI;

namespace AcDream.App.Plugins;

/// <summary>
/// The bundled font's bakes, shared by every plugin: one per size, uploaded
/// once, held while any plugin holds that size and given back on the last
/// release. Like client art, a shared bake costs no plugin anything against
/// its byte budget. Every size shares one face of the font file, and each
/// size's sharper bake for the canvas scale is shared the same way: the
/// scale is the interface's, the same for every plugin.
/// </summary>
internal sealed class BundledCanvasFontCache : IDisposable
{
    private readonly IPluginFontBackend _backend;
    private readonly Func<byte[]> _readFontBytes;
    private readonly int _maximumGlyphs;
    private readonly Dictionary<float, (CanvasFont Font, int Holds)> _bySize = [];
    private byte[]? _fontBytes;
    private CanvasFontFace? _face;
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
        // The face is made once the bytes have passed a bake's checks.
        _face ??= new CanvasFontFace(_fontBytes);
        uint texture = _backend.UploadCoverage(
            bake.Coverage, bake.AtlasWidth, bake.AtlasHeight, DebugName(pixelSize));
        var font = new CanvasFont(bake, texture, _face, ownsFace: false);
        _bySize.Add(pixelSize, (font, 1));
        return font;
    }

    /// <summary>
    /// Prepares one of the cache's bakes for the canvas scale. A bake another
    /// plugin already prepared for the scale is left as it is.
    /// </summary>
    /// <param name="shortfall">Why the font is not drawn at <paramref name="scale"/> itself, or null.</param>
    internal void PrepareScale(CanvasFont font, float scale, out string? shortfall)
    {
        ArgumentNullException.ThrowIfNull(font);
        shortfall = null;
        if (_disposed || font.PreparedScale == scale
            || !_bySize.TryGetValue(font.PixelSize, out (CanvasFont Font, int Holds) held)
            || !ReferenceEquals(held.Font, font))
            return;
        CanvasFontSharpening.Prepare(
            font, scale, CanvasFontBaker.DefaultRanges, _maximumGlyphs, long.MaxValue,
            _backend, DebugName(font.PixelSize), out shortfall);
    }

    private static string DebugName(float pixelSize) =>
        FormattableString.Invariant($"plugin-font-bundled-{pixelSize}px");

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
        CanvasFontSharpening.Release(font, _backend);
        _backend.ReleaseCoverage(font.AtlasTexture);
        font.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach ((CanvasFont font, int _) in _bySize.Values)
        {
            CanvasFontSharpening.Release(font, _backend);
            _backend.ReleaseCoverage(font.AtlasTexture);
            font.Dispose();
        }
        _bySize.Clear();
        _face?.Dispose();
        _face = null;
    }
}
```

- [ ] **Step 5: The plugin's font table** (replace `src/AcDream.App/Plugins/PluginFontTable.cs` with)

```csharp
using System.Diagnostics.CodeAnalysis;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Plugins;

/// <summary>
/// How many fonts one plugin may hold and how much memory its own fonts may
/// take. <paramref name="MaximumBytes"/> counts the font files the plugin
/// supplied and the atlases baked from them; the bundled font is shared and
/// costs nothing against it. The sharper bakes a high-density display gets
/// are the host's choice, not the plugin's, and are counted separately
/// (<see cref="MaximumSharpBytes"/>), so a plugin that fits one display fits
/// every display.
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

    /// <summary>
    /// How much memory the sharper bakes of the plugin's own fonts may take
    /// together: 32 MB, room for sixteen 16 px fonts at 2x several times
    /// over. A font whose sharper bake would pass it is drawn from a lower
    /// step, or from its own bake.
    /// </summary>
    public long MaximumSharpBytes { get; init; } = 32L * 1024 * 1024;
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

    private sealed class Entry(Key key, int id, CanvasFont font, long ownedBytes, IReadOnlyList<(int First, int Last)> ranges)
    {
        internal Key Key { get; } = key;
        internal int Id { get; } = id;
        internal CanvasFont Font { get; } = font;

        /// <summary>The characters baked, for a sharper bake of the same set.</summary>
        internal IReadOnlyList<(int First, int Last)> Ranges { get; } = ranges;

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
    private long _sharpBytes;
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

    /// <summary>The bytes the sharper bakes of the plugin's own fonts take.</summary>
    internal long SharpBytes => _sharpBytes;

    internal bool IsBound => _backend is not null;

    /// <summary>
    /// True while one of the plugin's canvases paints. A font not already held
    /// is refused then, because preparing one takes longer than a paint may.
    /// </summary>
    internal bool IsPainting { get; set; }

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
        pixelSize = RoundSize(pixelSize);
        var key = new Key(Bundled: true, Name: "", pixelSize, Ranges: "");
        if (TryHoldAgain(key, out PluginFont again))
            return again;
        if (RefusedWhilePainting())
            return PluginFont.None;
        if (!HasRoomForOneMore(FormattableString.Invariant($"the bundled font at {pixelSize} px")))
            return PluginFont.None;
        CanvasFont? font = bundled.Acquire(pixelSize, out string? failure);
        if (font is null)
        {
            ReportOnce(FormattableString.Invariant($"bundled:{pixelSize}"), FormattableString.Invariant($"the bundled font at {pixelSize} px could not be prepared: {failure}"));
            return PluginFont.None;
        }
        return Add(key, font, ownedBytes: 0L, CanvasFontBaker.DefaultRanges).Handle;
    }

    internal PluginFont AcquireStream(
        string name, Func<Stream> open, float pixelSize, IReadOnlyList<PluginCodepointRange>? ranges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(open);
        if (!TryEnter(out IPluginFontBackend? backend, out _) || !IsSizeAllowed(pixelSize))
            return PluginFont.None;
        pixelSize = RoundSize(pixelSize);

        IReadOnlyList<(int First, int Last)> baked = ranges is null
            ? CanvasFontBaker.DefaultRanges
            : [.. ranges.Select(range => (range.First, range.Last))];
        var key = new Key(Bundled: false, name, pixelSize, string.Join(",", baked.Select(r => $"{r.First:X}-{r.Last:X}")));
        if (TryHoldAgain(key, out PluginFont again))
            return again;
        if (RefusedWhilePainting())
            return PluginFont.None;
        if (!HasRoomForOneMore(FormattableString.Invariant($"font '{name}'")))
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
            ReportOnce($"bytes:{name}", FormattableString.Invariant($"font '{name}' would take the plugin's own fonts past the budget of {Budget.MaximumBytes:N0} bytes"));
            return PluginFont.None;
        }

        if (!CanvasFontBaker.TryBake(bytes, pixelSize, baked, Budget.MaximumGlyphs, out CanvasFontBake? bake, out string? why))
        {
            ReportOnce($"bake:{key}", FormattableString.Invariant($"font '{name}' at {pixelSize} px could not be prepared: {why}"));
            return PluginFont.None;
        }
        long owned = bytes.LongLength + (long)bake.AtlasWidth * bake.AtlasHeight;
        if (_ownedBytes + owned > Budget.MaximumBytes)
        {
            ReportOnce($"bytes:{key}", FormattableString.Invariant($"font '{name}' at {pixelSize} px would take the plugin's own fonts to {_ownedBytes + owned:N0} bytes; the budget is {Budget.MaximumBytes:N0}"));
            return PluginFont.None;
        }

        uint texture = backend.UploadCoverage(
            bake.Coverage, bake.AtlasWidth, bake.AtlasHeight, DebugName(name, pixelSize));
        _ownedBytes += owned;
        return Add(key, new CanvasFont(bake, texture, bytes), owned, baked).Handle;
    }

    /// <summary>
    /// Readies every font the plugin holds for canvases painted at
    /// <paramref name="scale"/>: each gets a sharper bake at that multiple
    /// of its size, or the largest lower step that fits, and the bake for
    /// any other scale is given back. A font already prepared for the scale
    /// is left alone, so this costs nothing on a paint that follows another
    /// at the same scale. Bundled fonts are prepared in the shared cache;
    /// the plugin's own count against <see cref="PluginFontBudget.MaximumSharpBytes"/>.
    /// A font drawn less sharply than the scale is reported once.
    /// </summary>
    internal void PrepareScale(float scale)
    {
        if (_disposed || _backend is not { } backend || _bundled is not { } bundled)
            return;
        ThrowIfWrongThread();
        foreach (Entry entry in _byId.Values)
        {
            CanvasFont font = entry.Font;
            if (font.PreparedScale == scale)
                continue;
            string? shortfall;
            if (entry.Key.Bundled)
            {
                bundled.PrepareScale(font, scale, out shortfall);
            }
            else
            {
                long room = Budget.MaximumSharpBytes - _sharpBytes + (font.Sharp?.AtlasBytes ?? 0L);
                _sharpBytes += CanvasFontSharpening.Prepare(
                    font, scale, entry.Ranges, Budget.MaximumGlyphs, room, backend,
                    DebugName(entry.Key.Name, entry.Key.PixelSize), out shortfall);
                // Short of the largest atlas, the room is what stopped the bake.
                if (shortfall is not null && room < (long)CanvasFontBaker.MaximumAtlasSide * CanvasFontBaker.MaximumAtlasSide)
                {
                    shortfall = FormattableString.Invariant(
                        $"the plugin's sharper fonts have {Math.Max(room, 0L):N0} of their {Budget.MaximumSharpBytes:N0} bytes left");
                }
            }
            if (shortfall is not null)
            {
                string what = entry.Key.Bundled
                    ? FormattableString.Invariant($"the bundled font at {entry.Key.PixelSize} px")
                    : FormattableString.Invariant($"font '{entry.Key.Name}' at {entry.Key.PixelSize} px");
                ReportOnce(
                    FormattableString.Invariant($"sharp:{entry.Id}:{scale}"),
                    FormattableString.Invariant(
                        $"{what} is drawn at {font.Sharp?.Scale ?? 1f}x on canvases painted at {scale}x, so it is less sharp than it could be: {shortfall}"));
            }
        }
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
            FormattableString.Invariant($"size:{pixelSize}"),
            FormattableString.Invariant($"a font size of {pixelSize} px was refused; sizes run from {Budget.MinimumPixelSize:0.##} to {Budget.MaximumPixelSize:0.##} px"));
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

    private bool RefusedWhilePainting()
    {
        if (!IsPainting)
            return false;
        ReportOnce("paint", "a font was asked for inside a paint callback and refused: fonts are prepared when asked for, so ask before painting");
        return true;
    }

    private bool HasRoomForOneMore(string what)
    {
        if (_byId.Count < Budget.MaximumCount)
            return true;
        ReportOnce("count", FormattableString.Invariant($"{what} refused: the plugin already holds {Budget.MaximumCount} fonts, which is the most it may"));
        return false;
    }

    private Entry Add(Key key, CanvasFont font, long ownedBytes, IReadOnlyList<(int First, int Last)> ranges)
    {
        var entry = new Entry(key, checked(++_nextId), font, ownedBytes, ranges);
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
        if (_backend is { } backend)
        {
            _sharpBytes -= CanvasFontSharpening.Release(entry.Font, backend);
            backend.ReleaseCoverage(entry.Font.AtlasTexture);
        }
        entry.Font.Dispose();
    }

    /// <summary>
    /// A size allowed by the budget, to the nearest quarter pixel, so
    /// near-equal requests share one bake. Rounding cannot leave the budget's
    /// range while its ends are whole quarters.
    /// </summary>
    private static float RoundSize(float pixelSize) =>
        MathF.Round(pixelSize * 4f, MidpointRounding.AwayFromZero) / 4f;

    private string DebugName(string name, float pixelSize) =>
        FormattableString.Invariant($"plugin-{_ownerId}-font-{name}-{pixelSize}px");

    private void ReportOnce(string key, string message)
    {
        if (_reported.Add(key))
            _report($"Plugin '{_ownerId}': {message}.");
    }
}
```

Compared with PR 1's file, the changes are as follows. `PluginFontBudget` gains `MaximumSharpBytes`. `Entry` keeps its `Ranges`, and `Add` takes them. Both `Acquire*` round the size with `RoundSize` **after** `IsSizeAllowed`. Debug names go through the invariant `DebugName`. `PrepareScale` and `SharpBytes` are new. `Remove` releases the sharper bake before the font's own atlas.

- [ ] **Step 6: The contract says sizes are quarter pixels** (`src/AcDream.Plugin.Abstractions/PluginFonts.cs`)

Replace

```csharp
/// <param name="PixelSize">The size the font was asked for, in pixels.</param>
```

with

```csharp
/// <param name="PixelSize">The size the font was prepared at, in pixels: the size asked for, to the nearest quarter pixel.</param>
```

and in both `Bundled` and `FromStream`, replace

```csharp
    /// <param name="pixelSize">The size in canvas pixels, from <see cref="MinimumPixelSize"/> to <see cref="MaximumPixelSize"/>.</param>
```

with

```csharp
    /// <param name="pixelSize">The size in canvas pixels, from <see cref="MinimumPixelSize"/> to <see cref="MaximumPixelSize"/>, prepared to the nearest quarter pixel.</param>
```

- [ ] **Step 7: Run the tests**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | grep -E "rror\(s\)|arning\(s\)"`, then `dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~Font&Lane!=Vulkan" 2>&1 | grep -E "Passed!|Failed!"` and `dotnet test tests/AcDream.Plugin.Tests -c Release --no-build 2>&1 | grep -E "Passed!|Failed!"`
Expected: `0 Warning(s)`; `Passed!`, 0 failed, twice.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App/Plugins/CanvasFontSharpening.cs src/AcDream.App/Plugins/BundledCanvasFontCache.cs \
  src/AcDream.App/Plugins/PluginFontTable.cs src/AcDream.Plugin.Abstractions/PluginFonts.cs \
  tests/AcDream.App.Tests/Plugins/BundledCanvasFontCacheTests.cs tests/AcDream.App.Tests/Plugins/PluginFontTableScaleTests.cs
git commit -m "plugin fonts: every held font readied for the canvas scale, within a budget of its own

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Plugins/CanvasFontSharpening.cs", "src/AcDream.App/Plugins/BundledCanvasFontCache.cs", "src/AcDream.App/Plugins/PluginFontTable.cs", "src/AcDream.Plugin.Abstractions/PluginFonts.cs", "tests/AcDream.App.Tests/Plugins/BundledCanvasFontCacheTests.cs", "tests/AcDream.App.Tests/Plugins/PluginFontTableScaleTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~Font&Lane!=Vulkan\"", "acceptanceCriteria": ["bundled sizes share a face; sharper bake shared and released last", "PrepareScale(2) sharpens bundled and own fonts, handles and metrics unchanged", "same scale bakes nothing; other scales release old bakes", "64px bundled steps to 1.75 and reports once with exact text", "sharper budget limits own fonts with exact report; bundled unaffected", "quarter-pixel sizes; out-of-range still refused", "existing font tests pass"], "modelTier": "standard"}
```

---

### Task 9: The canvas readies its plugin's fonts before painting

**Goal:** Before each repaint, outside the guard, the element asks the plugin's fonts to prepare for the interface's scale (not the canvas's, which is lower only for an oversized canvas), so the first paint at 2× already draws from sharper bakes.

**Files:**
- Modify: `src/AcDream.App/Plugins/PluginFonts.cs` (a `PrepareScale` forwarder before `TryResolve`)
- Modify: `src/AcDream.App/UI/Layout/PluginCanvasElement.cs` (`_interfaceScale` field; `FollowPixelScale`; `RepaintIfInvalidated`)
- Modify: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs` (`Harness.FontBackend`)
- Test: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.HiDpi.cs` (one test before `SurfaceProjection`)

**Acceptance Criteria:**
- [ ] With the bundled 16 px font held and framebuffer-per-point 2, the first frame uploads a 1024×512 sharper atlas, and the canvas's text is one coverage run of it
- [ ] Back at 1, the sharper atlas is released and the text uses the font's own atlas
- [ ] Every `PluginCanvas*` and font test passes

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "(FullyQualifiedName~PluginCanvas|FullyQualifiedName~Font)&Lane!=Vulkan"` → `Passed!`, 0 failed

**Steps:**

- [ ] **Step 1: The harness exposes its font backend** (`tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs`)

After `public List<string> Reports { get; } = [];` in `Harness` add

```csharp
        public AcDream.App.Tests.Plugins.PluginFontTableTests.FakeFontBackend FontBackend { get; } = new();
```

and replace `Registry.BindFontServices(new AcDream.App.Tests.Plugins.PluginFontTableTests.FakeFontBackend());` with `Registry.BindFontServices(FontBackend);`.

- [ ] **Step 2: Write the failing test** (`PluginCanvasElementTests.HiDpi.cs`, before the `SurfaceProjection` helper)

```csharp
    [Fact]
    public void FontsAreReadiedForTheScaleBeforeThePaintThatDrawsWithThem()
    {
        var harness = new Harness { FramebufferPerPoint = new Vector2(2f, 2f) };
        PluginFont font = harness.Registry.FontsFor(harness.Owner).Bundled(16f);
        uint ownAtlas = Assert.Single(harness.FontBackend.Uploaded).Texture;
        (PluginCanvasRegistration registration, _) = harness.Mount(Hud(), painter =>
            painter.DrawText("AV", new PluginPoint(10, 10), PluginColor.White, font));

        harness.Frame();

        (uint sharpAtlas, int width, int height) = harness.FontBackend.Uploaded[1];
        Assert.Equal((1024, 512), (width, height));
        Assert.Equal([sharpAtlas], harness.Surface.Renderer.DebugSpriteSegmentCoverage);

        // Back on a display of one pixel per point: the sharper bake is given back.
        harness.FramebufferPerPoint = Vector2.One;
        harness.Frame();

        Assert.Equal([sharpAtlas], harness.FontBackend.Released);
        Assert.Equal([ownAtlas], harness.Surface.Renderer.DebugSpriteSegmentCoverage);
        _ = registration;
    }

```

- [ ] **Step 3: Run it to see it fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -E "rror\(s\)"` then `dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~FontsAreReadiedForTheScale" 2>&1 | grep -E "Passed!|Failed!"`
Expected: `Failed!` (no second upload: `Uploaded[1]` is out of range).

- [ ] **Step 4: The forwarder** (`src/AcDream.App/Plugins/PluginFonts.cs`)

Before `/// <summary>The baked font behind a handle, for the painter.</summary>` add

```csharp
    /// <summary>Readies the plugin's fonts for canvases painted at a scale; see <see cref="PluginFontTable.PrepareScale"/>.</summary>
    internal void PrepareScale(float scale)
    {
        if (!_disposed)
            _table.PrepareScale(scale);
    }

```

- [ ] **Step 5: The element prepares them** (`src/AcDream.App/UI/Layout/PluginCanvasElement.cs`)

After `private float _pixelScale = 1f;` add `private float _interfaceScale = 1f;`.

In `FollowPixelScale`, replace

```csharp
        float interfaceScale = CanvasPixelScale.ForInterface(
            _surface.Services.FramebufferPerPoint(), FindRoot()?.CanvasScale ?? Vector2.One);
        float scale = CanvasPixelScale.ForCanvas(
            interfaceScale,
```

with

```csharp
        _interfaceScale = CanvasPixelScale.ForInterface(
            _surface.Services.FramebufferPerPoint(), FindRoot()?.CanvasScale ?? Vector2.One);
        float scale = CanvasPixelScale.ForCanvas(
            _interfaceScale,
```

In `RepaintIfInvalidated`, replace

```csharp
        bool drew = _surface.Repaint(
            _registration, target.Target, _guard, _images(), _fonts(), _shapeProblems, _pixelScale);
```

with

```csharp
        // Sharper font bakes are made here, outside the guard: baking takes
        // longer than a paint may. They follow the interface's scale rather
        // than this canvas's, which is lower only when the canvas is too
        // large for the device, so two canvases of one plugin never make
        // its fonts bake back and forth.
        PluginFonts? fonts = _fonts();
        fonts?.PrepareScale(_interfaceScale);
        bool drew = _surface.Repaint(
            _registration, target.Target, _guard, _images(), fonts, _shapeProblems, _pixelScale);
```

- [ ] **Step 6: Run the tests**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | grep -E "rror\(s\)|arning\(s\)"`, then `dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "(FullyQualifiedName~PluginCanvas|FullyQualifiedName~Font)&Lane!=Vulkan" 2>&1 | grep -E "Passed!|Failed!"`
Expected: `0 Warning(s)`; `Passed!`, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.App/Plugins/PluginFonts.cs src/AcDream.App/UI/Layout/PluginCanvasElement.cs \
  tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.HiDpi.cs
git commit -m "plugin canvas: a canvas readies its plugin's fonts for the scale before it paints

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Plugins/PluginFonts.cs", "src/AcDream.App/UI/Layout/PluginCanvasElement.cs", "tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs", "tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.HiDpi.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter \"(FullyQualifiedName~PluginCanvas|FullyQualifiedName~Font)&Lane!=Vulkan\"", "acceptanceCriteria": ["first 2x paint draws from a 1024x512 sharper atlas", "back at 1 the sharper atlas is released and own atlas used", "PluginCanvas and font tests pass"], "modelTier": "standard"}
```

---

### Task 10: The fringe on a real device at 2×

**Goal:** A `Lane=Vulkan` test paints through the real `PluginCanvasSurface` at pixel scale 2 and reads the target back. An edge on a device-pixel boundary is sharp, and an edge through a device-pixel centre half-covers it, so the fringe is one device pixel, not one canvas pixel.

**Files:**
- Test: `tests/AcDream.App.Tests/Rendering/Gpu/Vk/PluginCanvasHiDpiOffscreenTests.cs` (create)

**Acceptance Criteria:**
- [ ] `FillRoundedRect(0, 0, 8, 16)` at 2×: device pixels 14 and 15 read 255±1 and pixel 16 reads 0±1 (with a one-canvas-pixel fringe, pixels 15 and 16 would read about 191 and 64)
- [ ] `FillRoundedRect(0, 0, 8.25, 16)` at 2×: pixel 15 reads 255±1, 16 reads 128±2, 17 reads 0
- [ ] The existing `Lane=Vulkan` canvas tests still pass

**Verify:** `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~Canvas&Lane=Vulkan"` → `Passed!  - Failed:     0, Passed:     5`

**Steps:**

- [ ] **Step 1: Write the test** (`tests/AcDream.App.Tests/Rendering/Gpu/Vk/PluginCanvasHiDpiOffscreenTests.cs`)

```csharp
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Rendering.Gpu.Vk;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Rendering.Gpu.Vk;

/// <summary>
/// A canvas painted through the canvas surface at twice the density, on a
/// real device: the target holds two pixels per canvas pixel, and a shape's
/// fringe is one of those pixels wide, not one canvas pixel.
/// </summary>
public sealed class PluginCanvasHiDpiOffscreenTests
{
    private const int Extent = 16;

    private const float Scale = 2f;

    private const int Device = Extent * 2;

    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    [Trait("Lane", "Vulkan")]
    [Fact]
    public void AnEdgeOnADevicePixelBoundaryIsSharpAtTwiceTheDensity()
    {
        // At one pixel per canvas pixel the fringe of this edge would cover
        // canvas pixels 7.5 to 8.5, so device pixels 15 and 16 would read 3/4 and 1/4.
        byte[] painted = Paint(painter => painter.FillRoundedRect(
            new PluginRect(0, 0, 8, Extent), default, PluginColor.White));

        AssertNear([255, 255, 255, 255], Pixel(painted, 14, 8), 1);
        AssertNear([255, 255, 255, 255], Pixel(painted, 15, 8), 1);
        AssertNear([0, 0, 0, 0], Pixel(painted, 16, 8), 1);
    }

    [Trait("Lane", "Vulkan")]
    [Fact]
    public void AnEdgeThroughADevicePixelCentreCoversHalfOfIt()
    {
        byte[] painted = Paint(painter => painter.FillRoundedRect(
            new PluginRect(0, 0, 8.25, Extent), default, PluginColor.White));

        AssertNear([255, 255, 255, 255], Pixel(painted, 15, 8), 1);
        AssertNear([128, 128, 128, 128], Pixel(painted, 16, 8), 2);
        AssertNear([0, 0, 0, 0], Pixel(painted, 17, 8), 0);
    }

    private static byte[] Paint(Action<IPluginPainter> paint)
    {
        using var host = HeadlessVulkanTestHost.Create(HeadlessVulkanTestHost.CommittedShaderDirectory());
        VulkanGpuDevice device = host.Device;
        var frames = new FrameSource();
        var registry = new BufferedUiRegistry();
        var registration = (PluginCanvasRegistration)registry.RegisterCanvas(
            new PluginUiOwner("example.plugin", "Example"), new PluginCanvasDescriptor("hud", Extent, Extent), paint);
        using var surface = new PluginCanvasSurface(
            new PluginCanvasHostServices(device, frames, "unused", null), font: null);
        using IGpuRenderTarget canvas = device.CreateRenderTarget(new GpuRenderTargetDescription(
            "canvas-hidpi", Device, Device, GpuTextureFormat.Rgba8UnormRenderTarget, DepthFormat: null, SampleCount: 1));

        using (IGpuFrame frame = device.BeginFrame())
        {
            frames.CurrentFrame = frame;
            Assert.True(surface.Repaint(
                registration, canvas, new UiDrawCallbackGuard("hidpi"), images: null, pixelScale: Scale));
            frames.CurrentFrame = null;
        }
        device.WaitIdle();
        return host.ReadBack(canvas, Device, Device);
    }

    private static ReadOnlySpan<byte> Pixel(byte[] image, int x, int y) => image.AsSpan((y * Device + x) * 4, 4);

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

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -E "rror\(s\)"`, then `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~Canvas&Lane=Vulkan" 2>&1 | grep -E "Passed!|Failed!"`
Expected: `Passed!  - Failed:     0, Passed:     5`.

- [ ] **Step 3: Prove it can fail** (then undo)

```bash
sed -i '' 's|_devicePixel = 1f / pixelScale;|_devicePixel = 1f; // MUTATION|' src/AcDream.App/UI/Layout/PluginPainter.cs
dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -E "rror\(s\)"
DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~HiDpiOffscreen" 2>&1 | grep -E "expected|Passed!|Failed!"
sed -i '' 's|_devicePixel = 1f; // MUTATION|_devicePixel = 1f / pixelScale;|' src/AcDream.App/UI/Layout/PluginPainter.cs
git diff --stat src/AcDream.App/UI/Layout/PluginPainter.cs
```

Expected: `Failed!` with `expected 255±1, read 191`, then an empty `git diff --stat` (the mutation is undone).

- [ ] **Step 4: Commit**

```bash
git add tests/AcDream.App.Tests/Rendering/Gpu/Vk/PluginCanvasHiDpiOffscreenTests.cs
git commit -m "tests: at twice the density a shape's fringe is one device pixel wide

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["tests/AcDream.App.Tests/Rendering/Gpu/Vk/PluginCanvasHiDpiOffscreenTests.cs"], "verifyCommand": "DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~Canvas&Lane=Vulkan\"", "acceptanceCriteria": ["edge on device boundary sharp at 2x", "edge through device centre half-covers it", "existing Vulkan canvas tests pass"], "modelTier": "standard"}
```

---

### Task 11: Documentation

**Goal:** `docs/plugin-api.md` gains `### High-density displays` under Canvases. The Shapes text says "screen pixel" and notes the extra vertices at 2×, and Fonts says sizes are quarter pixels. `docs/plugin-ui-markup.md` lists the new tests and the demo.

**Files:**
- Modify: `docs/plugin-api.md` (Fonts: the paragraph starting `Each size of each font is its own`; Shapes: the paragraphs starting `The edges of these shapes` and `One paint may draw`; a new subsection before `### Pointer input`)
- Modify: `docs/plugin-ui-markup.md` (after the canvas-shapes test sentence, ~:419)

**Acceptance Criteria:**
- [ ] `grep -c "### High-density displays" docs/plugin-api.md` is 1, and the anchor `#high-density-displays` is linked from the Shapes text
- [ ] The Fonts section says sizes are prepared to the nearest quarter pixel
- [ ] `docs/plugin-ui-markup.md` names `CanvasPixelScaleTests`, `PluginFontTableScaleTests`, `PluginCanvasPixelScaleContractTests`, `PluginCanvasHiDpiOffscreenTests` and the demo sample

**Verify:** `grep -c -E "High-density displays|high-density-displays|nearest quarter pixel" docs/plugin-api.md && grep -c -E "PluginCanvasHiDpiOffscreenTests|AcDream.Plugins.CanvasDemo" docs/plugin-ui-markup.md` → `3` and `2`

**Steps:**

- [ ] **Step 1: Fonts** (`docs/plugin-api.md`)

Replace

```markdown
Each size of each font is its own `PluginFont`, carrying its `PixelSize`,
`LineHeight` and `Ascent` (how far below the top of a line the baseline
sits) so text in different fonts can share a baseline.
```

with

```markdown
Each size of each font is its own `PluginFont`, carrying its `PixelSize`,
`LineHeight` and `Ascent` (how far below the top of a line the baseline
sits) so text in different fonts can share a baseline. Sizes are prepared
to the nearest quarter pixel, and `PixelSize` is the size prepared: asking
for 16.1 px gives the 16 px font.
```

- [ ] **Step 2: Shapes**

Replace

```markdown
The edges of these shapes are anti-aliased over one pixel; `FillRect`,
`StrokeRect` and `DrawLine` keep their hard edges. Strokes are centred on
the outline, so half the thickness falls outside the shape. A stroke
thinner than a pixel is drawn one pixel wide and proportionally fainter.
A shape with no area or no thickness simply draws nothing.
```

with

```markdown
The edges of these shapes are anti-aliased over one screen pixel;
`FillRect`, `StrokeRect` and `DrawLine` keep their hard edges. Strokes are
centred on the outline, so half the thickness falls outside the shape. A
stroke thinner than a screen pixel is drawn one screen pixel wide and
proportionally fainter. A shape with no area or no thickness simply draws
nothing.
```

and replace

```markdown
One paint may draw at most 32,768 shape vertices; a large circle takes a
few hundred. Past that limit the remaining shapes are skipped and the log
says so once. The paint also counts against the paint budget, like a slow
one, so a callback that keeps going over is dropped. On a host that
predates shapes, they draw nothing.
```

with

```markdown
One paint may draw at most 32,768 shape vertices; a large circle takes a
few hundred, and about 1.4 times as many on a high-density display (see
[High-density displays](#high-density-displays)). Past that limit the
remaining shapes are skipped and the log says so once. The paint also
counts against the paint budget, like a slow one, so a callback that keeps
going over is dropped. On a host that predates shapes, they draw nothing.
```

(Measured: a 100 px-radius circle is 642 vertices at 1× and 894 at 2×; a 200×60 panel with 8 px corners is 246 and 318.)

- [ ] **Step 3: The new subsection**, immediately before `### Pointer input`:

````markdown
### High-density displays

On a high-density display -- a Retina Mac, or Windows at 150 % -- the host
paints each canvas at more than one screen pixel per canvas pixel, so
shape edges and text in a font from [Fonts](#fonts) come out as sharp as
the display can show them rather than magnified. Nothing changes for the
plugin: coordinates, sizes, `Width`, `Height`, `MeasureText` and pointer
positions stay in canvas pixels, and the canvas takes the same room on
screen. `painter.PixelScale` says how many screen pixels one canvas pixel
covers in this paint, for a detail that should be exactly one screen pixel:

```csharp
painter.FillRect(new PluginRect(0, 40, painter.Width, 1 / painter.PixelScale), divider); // a hairline
```

The scale is the window's framebuffer pixels per point, times the stretch
of the fixed-size screens before the world, rounded up to a quarter, from
1 to 4. A canvas too large for the graphics card at that scale is painted
at the largest quarter that fits. When the scale changes -- the window
moved to another display -- the host repaints the canvas; the paint
callback reads the new value then.

The client's interface font keeps its look: it is drawn on whole canvas
pixels, as the rest of the interface draws it, so on a 2x display each of
its pixels is a crisp 2 x 2 block. Images are drawn from the same texture
as before, now at the display's resolution. Fonts from `host.Ui.Fonts` are
prepared again at the scale
before the canvas paints, without the plugin asking: this does not count
against `MaximumBytes`, and a plugin's own fonts have a separate 32 MB for
it. A font too large to prepare at the full scale (the bundled font at
64 px does not fit at 2x) uses the largest quarter that fits, and the
client's log says so once.
````

- [ ] **Step 4: The test list** (`docs/plugin-ui-markup.md`)

In the Tests section, the canvas-shapes sentence ends with the two lines `and, on a Vulkan device, by` … `PluginCanvasShapeOffscreenTests` / `(`Lane=Vulkan`).`. Directly after them, add:

```markdown
Canvases on high-density displays are covered by `UI/Layout/CanvasPixelScaleTests`,
the high-density cases in `UI/Layout/PluginCanvasElementTests`, the sharper-bake
cases in `CanvasFontBakerTests`, `CanvasFontTests` and
`BundledCanvasFontCacheTests`, and `Plugins/PluginFontTableScaleTests` under
`tests/AcDream.App.Tests/`, by `PluginCanvasPixelScaleContractTests` for the
answer of an older host, and, on a Vulkan device, by
`Rendering/Gpu/Vk/PluginCanvasHiDpiOffscreenTests` (`Lane=Vulkan`).
`samples/AcDream.Plugins.CanvasDemo` paints one canvas with every kind of
content, for looking at by eye.
```

- [ ] **Step 5: Commit**

```bash
git add docs/plugin-api.md docs/plugin-ui-markup.md
git commit -m "docs: canvases on high-density displays

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["docs/plugin-api.md", "docs/plugin-ui-markup.md"], "verifyCommand": "grep -c \"### High-density displays\" docs/plugin-api.md", "acceptanceCriteria": ["High-density displays subsection present and linked", "Fonts says nearest quarter pixel", "markup docs list the new tests and the demo"], "modelTier": "mechanical"}
```

---

### Task 12: After screenshots on the Retina display, compared

**Goal:** The same capture as Task 2, from the branch's head, saved as `/tmp/openac-hidpi/after-canvas.png` and `after-text.png`, and compared side by side with the before crops. This is the PR's acceptance gate: text and shape edges must be visibly sharper, with the layout unchanged.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:** none in the repository.

**Acceptance Criteria:**
- [ ] `run-after.log` contains `screenshot-complete name=after` at twice the window size
- [ ] `after-canvas.png` is 720×440, shows `Pixel scale 2`, and the panel's outline and every element sit where they sit in `before-canvas.png`
- [ ] Side by side, the Noto Sans text, the circle, ellipse and triangle edges and both lines are sharp in the after crop and soft in the before crop, and the interface-font line is crisp pixel-doubled. The user confirms
- [ ] Both capture worktrees are removed and the branch is clean

**Verify:** `grep -o "screenshot-complete name=after .*size=[0-9]*x[0-9]*" /tmp/openac-hidpi/run-after.log && sips -g pixelWidth -g pixelHeight /tmp/openac-hidpi/after-canvas.png` → a `size=` at 2×, `pixelWidth: 720`, `pixelHeight: 440`

**Steps:**

- [ ] **Step 1: A capture worktree at HEAD, with the same size patch**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree add --detach /tmp/openac-hidpi/after HEAD
cd /tmp/openac-hidpi/after
```

Apply the capture-size patch. It is the one from Task 2, repeated here, and both anchors are unchanged by this PR:

```bash
python3 - <<'EOF'
p='src/AcDream.App/Diagnostics/FrameScreenshotController.cs'
s=open(p).read()
a='''    public bool CapturePending(int width, int height)
    {
        if (_pending.Count == 0)
            return false;
'''
b='''    // CAPTURE-WORKTREE ONLY: the framebuffer's size, not the window's, on a high-density display.
    internal static Func<(int Width, int Height)>? CaptureSizeOverride;

    public bool CapturePending(int width, int height)
    {
        if (_pending.Count == 0)
            return false;
        if (CaptureSizeOverride is { } captureSize)
            (width, height) = captureSize();
'''
assert a in s; open(p,'w').write(s.replace(a,b))
p='src/AcDream.App/Composition/InteractionRetainedUiComposition.cs'
s=open(p).read()
a='''            var screenshots = new FrameScreenshotController('''
b='''            FrameScreenshotController.CaptureSizeOverride = () => (d.Window.FramebufferSize.X, d.Window.FramebufferSize.Y);
            var screenshots = new FrameScreenshotController('''
assert a in s; open(p,'w').write(s.replace(a,b))
EOF
```

Then:

```bash
dotnet build src/AcDream.App/AcDream.App.csproj -c Release 2>&1 | grep -E "rror\(s\)"
dotnet build samples/AcDream.Plugins.CanvasDemo/AcDream.Plugins.CanvasDemo.csproj -c Release 2>&1 | grep -E "rror\(s\)"
mkdir -p /tmp/openac-hidpi/root-after/plugins/sample.canvas-demo
cp samples/AcDream.Plugins.CanvasDemo/bin/Release/net10.0/* /tmp/openac-hidpi/root-after/plugins/sample.canvas-demo/
```

Expected: `0 Error(s)` twice.

- [ ] **Step 2: Ask the user, then capture**

Tell the user, before running: "A client window is about to open on your QHD monitor. Drag it onto the MacBook's built-in display and leave it there; it saves its own frame after 45 seconds and closes itself." Then:

```bash
/tmp/openac-hidpi/capture.sh /tmp/openac-hidpi/after /tmp/openac-hidpi/root-after after 45
/tmp/openac-hidpi/crop.sh after
grep -iE "less sharp|cannot be painted" /tmp/openac-hidpi/run-after.log
```

Expected: `framebuffer resize event` at 2×, `screenshot-complete name=after … size=<2× size>`, `pixelWidth: 720`, `pixelHeight: 440`, and no `less sharp` or `cannot be painted` lines (the demo's fonts are 13 and 20 px, which fit at 2×).

- [ ] **Step 3: Compare**

Read `before-canvas.png`, `after-canvas.png`, `before-text.png` and `after-text.png`, and check each point in the acceptance criteria. Show the four images to the user (with `SendUserFile` when available) and ask them to confirm the difference. Keep the four PNGs in `/tmp/openac-hidpi/` for the PR description.

- [ ] **Step 4: Clean up the capture worktrees**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree remove --force /tmp/openac-hidpi/before
git worktree remove --force /tmp/openac-hidpi/after
git worktree list
git status --short
```

Expected: neither capture worktree listed; `git status` empty apart from lock files.

```json:metadata
{"files": [], "verifyCommand": "grep -o \"screenshot-complete name=after .*size=[0-9]*x[0-9]*\" /tmp/openac-hidpi/run-after.log && sips -g pixelWidth -g pixelHeight /tmp/openac-hidpi/after-canvas.png", "acceptanceCriteria": ["run-after.log shows screenshot-complete name=after at twice the window size", "after-canvas.png is 720x440, shows Pixel scale 2, same layout as before", "text, shape edges and lines sharp after and soft before; interface font crisp; user confirms", "capture worktrees removed, branch clean"], "modelTier": "standard", "userGate": true, "tags": ["user-gate"], "requireEvidenceTokens": [["before"], ["after"], ["Pixel scale 2"]]}
```

---

### Task 13: Full verification and push

**Goal:** The branch builds with 0 warnings, the full portable suite shows no failures beyond Task 0's baseline, the canvas Vulkan tests pass, and the branch is pushed to `origin`.

**Files:** none

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`
- [ ] The portable suite with `TMPDIR=/tmp/` fails only tests listed in `/tmp/openac-hidpi/baseline-failures.txt` (HostParity peer IPC timing tests may vary within that set)
- [ ] `Lane=Vulkan` tests matching `Canvas` pass (5)
- [ ] No staged or committed `packages.*.lock.json` changes except the demo's new one from Task 1
- [ ] `git log --oneline origin/main..HEAD` shows the ten commits of Tasks 1, 3–11 and nothing under `docs/superpowers`
- [ ] `origin/painter-v2/hidpi` equals `HEAD`

**Verify:** `git fetch origin && test "$(git rev-parse HEAD)" = "$(git rev-parse origin/painter-v2/hidpi)" && echo pushed` → `pushed`

**Steps:**

- [ ] **Step 1: Build and test everything**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test AcDream.slnx -c Release --no-build --filter "Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure" 2>&1 \
  | grep -E "^\s+Failed " | sed -E 's/ \[[^]]*\]$//' | sort -u > /tmp/openac-hidpi/final-failures.txt
comm -23 /tmp/openac-hidpi/final-failures.txt /tmp/openac-hidpi/baseline-failures.txt | grep -v "HostParity.Tests.Peer" || echo "no new failures"
DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~Canvas&Lane=Vulkan" 2>&1 | grep -E "Passed!|Failed!"
```

Expected: `0 Warning(s)`, `0 Error(s)`; `no new failures`; `Passed!  - Failed:     0, Passed:     5`. If a non-peer test fails, investigate with superpowers-extended-cc:systematic-debugging before going on.

- [ ] **Step 2: Check what the branch carries**

```bash
git checkout -- '*.lock.json'
git status --short
git log --oneline origin/main..HEAD
git diff --stat origin/main..HEAD | tail -1
git diff --name-only origin/main..HEAD | grep -E "lock.json|docs/superpowers" || true
```

Expected: a clean status, ten commits, and only `samples/AcDream.Plugins.CanvasDemo/packages.neutral.lock.json` from the last command.

- [ ] **Step 3: Push**

```bash
git push -u origin painter-v2/hidpi
git fetch origin && test "$(git rev-parse HEAD)" = "$(git rev-parse origin/painter-v2/hidpi)" && echo pushed
```

Expected: `pushed`. Do not open a pull request: ask the user which repository it goes to, and whether fork `main` should take the branch (with a merge commit, as PR 3 was).

```json:metadata
{"files": [], "verifyCommand": "git fetch origin && test \"$(git rev-parse HEAD)\" = \"$(git rev-parse origin/painter-v2/hidpi)\" && echo pushed", "acceptanceCriteria": ["solution builds with 0 warnings", "no portable failures beyond baseline", "5 canvas Vulkan tests pass", "no stray lock files", "ten commits, nothing under docs/superpowers", "pushed to origin"], "modelTier": "mechanical"}
```
