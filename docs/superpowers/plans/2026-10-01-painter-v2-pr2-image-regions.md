# Painter v2 — PR 2: Image Regions and Nine-Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A plugin's canvas painter can draw part of an image (one frame of a sprite sheet), the same turned and scaled about a pivot, and a nine-slice frame, without the neighbouring frames on the sheet bleeding in.

**Architecture:** Three default members on `IPluginPainter` (`DrawImageRegion`, `DrawImageRegionTransformed`, `DrawImageNineSlice`) and a `PluginInsets` record keep older painters building and drawing nothing. The host's `PluginPainter` turns each call into upright sprites with chosen texture coordinates through `UiRenderContext.DrawSprite`/`DrawSpriteTransformed`, which already take arbitrary UVs and clip. The arithmetic lives in a new pure `CanvasImageRegions` class. To keep neighbours out, the image table learns from its backend whether each texture is sampled linearly (`IPluginImageBackend.IsLinearFiltered`); on linear textures a region edge that lies inside the image is pulled in by half a pixel. A plugin's own uploads move to a clamping sampler. No shader change.

**Tech Stack:** C# / .NET 10, xUnit, the recording GPU device used by the existing canvas suites.

**Spec:** `docs/superpowers/specs/2026-09-30-plugin-painter-v2-design.md` (section 5, and the PR 2 entries under "Plan-time corrections", which supersede section 5 where they differ).

**Checked:** this plan's code was applied to upstream `main` (`bbc83275`) in a throwaway worktree before hand-off, one commit per task. Every "replace this" anchor was generated from those commits and checked to occur exactly once in the file it edits, and applying them in order reproduces each commit. Each task's commit built with 0 warnings and passed its own tests; the full portable suite at the last commit failed only 4 HostParity peer tests (the baseline range is 2–7). Removing the half-pixel pull-in failed 5 tests and removing the pivot correction failed 1 (six in all). A scratch merge into fork `main` (`84ada220`) conflicted in four files; Task 7 gives each resolution, and the merged tree's results are recorded there.

## Global Constraints

- Branch `painter-v2/image-regions`, created from upstream `main` at `bbc83275` (`upstream/main`; fork `main` contains it). PR 2 depends on no other PR in the series, so it starts from `main` (the spec's order-of-work rule) and can go upstream as one topic. Nothing under `docs/superpowers/` goes on a code branch.
- Contract additions are default interface members that draw nothing, and one new `public readonly record struct PluginInsets(double Left, double Top, double Right, double Bottom)` with `static Uniform(double)`. Every public member carries XML docs (the Abstractions build fails on undocumented members).
- Exact signatures, in `IPluginPainter`, after `DrawImageTransformed`:
  - `void DrawImageRegion(PluginImage image, PluginRect source, PluginRect destination, PluginColor tint)`
  - `void DrawImageRegionTransformed(PluginImage image, PluginRect source, PluginRect destination, PluginColor tint, double rotationRadians, PluginPoint pivot, double scaleX = 1.0, double scaleY = 1.0)`
  - `void DrawImageNineSlice(PluginImage image, PluginRect destination, PluginInsets insets, PluginColor tint, PluginRect? source = null, bool drawCenter = true)`
- Sources and insets are in the image's own pixels. A source is cut to the image's bounds and the destination shrinks with it in proportion (so what remains lands where it was); a transformed region keeps the pivot where the plugin put it. A nine-slice's source is cut and its destination is not moved.
- Half-pixel pull-in: only on linearly sampled textures, only on edges strictly inside the image (an edge on the image's border is sampled as `DrawImage` samples it), and for a nine-slice only on the frame's outer edges, never at the seams between its pieces. A span narrower than its pulls shares them, so both ends meet in its middle.
- What is linear: client art (the shared `GetOrUploadRenderSurface(…, nearest: false)` upload) and the plugin's own art. What is nearest: the composed spell and object icons (`IconComposer` uploads them `nearest: true`). The table asks the backend once, when an image is first held.
- `TextureCache.UploadReleasableRgba8` samples with `GpuSamplerDescription.WorldClamp` (it used `WorldRepeat`). Nothing else changes sampler.
- Invalid input (non-finite or non-positive rectangles, a source wholly off the image, negative or non-finite insets) draws nothing, never throws, and is not reported.
- No shader, pipeline, guard or budget change. A nine-slice is at most nine sprites.
- Commit subjects: `plugin api: …`, `plugin canvas: …`, `docs: …`. Every commit message ends with `Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB`.
- Environment for every command: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`.
- Builds on this Mac rewrite `src/AcDream.Launcher/packages.neutral.lock.json`, and an unlocked restore touches `packages.osx-arm64.lock.json` files. Never stage a `packages.*.lock.json`. Stage files by explicit path, and run `git checkout -- '*.lock.json'` before pushing.
- The first solution build in a fresh worktree can fail once with `NETSDK1047 … AcDream.Bake/obj/project.assets.json doesn't have a target for 'net10.0/osx-arm64'`, a restore race. Build again; the second build is clean.
- Portable gate filter: `Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure`.
- Baseline failures on this Mac, not this PR's: run the full portable suite with `TMPDIR=/tmp/` (the default temp path makes Unix-socket paths longer than 104 characters). Even then 2–7 `AcDream.HostParity.Tests` `Peer*ParityTests` fail, varying run to run (4 at the prototype's last commit). Earlier runs on `bbc83275` have also failed `GraphicalPluginSessionTests.ReloadCommandLoadsAFreshCopyAndTheOldOneLeavesMemory`, `GameWindowRenderLeafCompositionTests.PaperdollComposition_SkipsEitherMissingOptionalUiSurface`, `LiveEntityNetworkUpdateControllerForcePositionWiringTests.CommittedOrDeferredCellReturnsBeforeReachingTheGenericTail`, three `RenderPackValidatorCommandTests.External*` and `HeadlessSessionIsolationTests.ThirtySessionMixedWorkloadMaintainsIsolationAndConverges`. Record the baseline set in Task 0 and compare against it; do not try to fix them here.
- The unit tests must run outside the command sandbox, or the peer and pipe tests fail on socket permissions too.

**User decisions (already made):**
- "One spec, a PR series"; this is PR 2 (`painter-v2/image-regions`), agreed 2026-09-30 with no dependencies, skipped then and taken up now ("lets plan PR2", 2026-10-01).
- After the branch is done it is merged into fork `main` with a merge commit, as PRs 3–6b were.
- Branches are pushed to `origin`. Do not open a pull request without asking which repository it goes to.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AcDream.Plugin.Abstractions/PluginImageRegions.cs` | Create | `PluginInsets`. |
| `src/AcDream.Plugin.Abstractions/PluginCanvas.cs` | Modify | The three default painter members and their docs. |
| `tests/AcDream.Plugin.Tests/PluginCanvasImageRegionsContractTests.cs` | Create | An older painter accepts every region call and draws nothing; `Uniform`. |
| `src/AcDream.App/Rendering/TextureCache.cs` | Modify | Releasable uploads clamp; `IsNearestUiTexture`. |
| `src/AcDream.App/Plugins/PluginImageTable.cs` | Modify | Backend reports filtering; entries keep it; `TryResolve` overload reports it. |
| `src/AcDream.App/Plugins/PluginImages.cs` | Modify | The same `TryResolve` overload for the painter. |
| `src/AcDream.App/UI/RetailPluginImageBackend.cs` | Modify | `IsLinearFiltered` from the texture cache. |
| `src/AcDream.App/UI/CanvasImageRegions.cs` | Create | Pure region → UV and nine-slice → pieces arithmetic. |
| `src/AcDream.App/UI/Layout/PluginPainter.cs` | Modify | The three members, over `DrawSprite`/`DrawSpriteTransformed`. |
| `tests/AcDream.App.Tests/Plugins/PluginImageTableTests.cs` | Modify | Filter flag per kind, asked once. |
| `tests/AcDream.App.Tests/Rendering/TextureCacheReleasableUiTextureTests.cs` | Modify | Clamp sampler; nearest query. |
| `tests/AcDream.App.Tests/Plugins/BufferedUiRegistryImagesTests.cs` | Modify | Fake implements the new backend member. |
| `tests/AcDream.App.Tests/UI/Layout/PluginCanvasTeardownOrderTests.cs` | Modify | Fake implements the new backend member. |
| `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs` | Modify | Fake gains a nearest spell icon; class becomes `partial`. |
| `tests/AcDream.App.Tests/UI/CanvasImageRegionsTests.cs` | Create | The arithmetic. |
| `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.Regions.cs` | Create | Regions and nine-slices through a real paint. |
| `docs/plugin-api.md` | Modify | New "Image regions" subsection under Canvases. |
| `docs/plugin-ui-markup.md` | Modify | Tests paragraph names the new suites. |

`ScopedPluginHost` needs no change: it forwards the paint callback, and the painter the callback receives is the host's own `PluginPainter`, never wrapped.

---

### Task 0: Create the branch and record the baseline

**Goal:** `painter-v2/image-regions` from upstream `main`, building cleanly, with the baseline test failures written down.

**Files:** none

**Acceptance Criteria:**
- [ ] `git branch --show-current` prints `painter-v2/image-regions`
- [ ] `git rev-parse HEAD` is `bbc83275…` (upstream `main`)
- [ ] `docs/superpowers` does not exist in the working tree
- [ ] `dotnet build AcDream.slnx -c Release` reports `0 Warning(s)` and `0 Error(s)`
- [ ] The names of the portable-suite failures (run with `TMPDIR=/tmp/`) are saved to `/tmp/openac-regions/baseline-failures.txt`

**Verify:** `git branch --show-current && git merge-base --is-ancestor HEAD upstream/main && test ! -e docs/superpowers && echo ok` → `painter-v2/image-regions`, `ok`

**Steps:**

- [ ] **Step 1: Branch**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git fetch upstream && git fetch origin
git rev-parse --short upstream/main   # expect bbc83275; if upstream moved, stop and ask
git worktree add -b painter-v2/image-regions .worktrees/image-regions upstream/main
cd .worktrees/image-regions
git merge-base --is-ancestor HEAD upstream/main && test ! -e docs/superpowers && echo ok
```

- [ ] **Step 2: Build**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet build AcDream.slnx -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'
```

Expected: `0 Warning(s)`, `0 Error(s)` (if `NETSDK1047` appears, build again).

- [ ] **Step 3: Record the baseline** (outside the command sandbox)

```bash
mkdir -p /tmp/openac-regions
TMPDIR=/tmp/ dotnet test AcDream.slnx -c Release --no-build \
  --filter 'Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure' \
  2>&1 | grep -E '^\s+Failed ' | sed -E 's/^[[:space:]]+Failed ([^ ]+).*/\1/' | sort > /tmp/openac-regions/baseline-failures.txt
cat /tmp/openac-regions/baseline-failures.txt
git checkout -- '*.lock.json'
```

Expected: only names from the baseline list in Global Constraints.

---

### Task 1: The painter contract

**Goal:** `IPluginPainter` gains `DrawImageRegion`, `DrawImageRegionTransformed` and `DrawImageNineSlice` as default members that draw nothing, and `PluginInsets` with `Uniform` exists.

**Files:**
- Create: `src/AcDream.Plugin.Abstractions/PluginImageRegions.cs`
- Modify: `src/AcDream.Plugin.Abstractions/PluginCanvas.cs`
- Test: `tests/AcDream.Plugin.Tests/PluginCanvasImageRegionsContractTests.cs`

**Acceptance Criteria:**
- [ ] A painter implementing only the members from before this PR builds, and every region and nine-slice call on it reaches none of its members
- [ ] `PluginInsets.Uniform(6) == new PluginInsets(6, 6, 6, 6)`
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`

**Verify:** `dotnet test tests/AcDream.Plugin.Tests -c Release --filter "FullyQualifiedName~PluginCanvasImageRegionsContractTests"` → `Passed!  - Failed:     0, Passed:     2`

**Steps:**

- [ ] **Step 1: Write the failing test**

Create `tests/AcDream.Plugin.Tests/PluginCanvasImageRegionsContractTests.cs`:

```csharp
// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;

namespace AcDream.Plugin.Tests;

/// <summary>
/// Image regions are additive: a painter written before them still builds
/// and accepts every region call, drawing nothing rather than the whole
/// sheet the region was cut from.
/// </summary>
public sealed class PluginCanvasImageRegionsContractTests
{
    /// <summary>A painter that implements only what the contract had before image regions.</summary>
    private sealed class OlderPainter : IPluginPainter
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

    [Fact]
    public void APainterWrittenBeforeImageRegionsAcceptsEveryRegionCallAndDrawsNothing()
    {
        var older = new OlderPainter();
        IPluginPainter painter = older;
        var sheet = new PluginImage(1, 64, 64);
        var source = new PluginRect(16, 0, 16, 16);
        var destination = new PluginRect(0, 0, 32, 32);

        painter.DrawImageRegion(sheet, source, destination, PluginColor.White);
        painter.DrawImageRegionTransformed(sheet, source, destination, PluginColor.White, 0.5, new PluginPoint(16, 16));
        painter.DrawImageNineSlice(sheet, destination, PluginInsets.Uniform(4), PluginColor.White);
        painter.DrawImageNineSlice(
            sheet, destination, new PluginInsets(1, 2, 3, 4), PluginColor.White, source, drawCenter: false);

        Assert.Equal(0, older.Calls);
    }

    [Fact]
    public void UniformInsetsAreTheSameOnEverySide()
    {
        Assert.Equal(new PluginInsets(6, 6, 6, 6), PluginInsets.Uniform(6));
        Assert.Equal(new PluginInsets(0, 0, 0, 0), default(PluginInsets));
    }
}
```


- [ ] **Step 2: Run it to see it fail**

Run: `dotnet build tests/AcDream.Plugin.Tests -c Release 2>&1 | grep -E 'error CS' | head -3`
Expected: `error CS0246: The type or namespace name 'PluginInsets' could not be found` (and `CS1061` for the new members).

- [ ] **Step 3: Add the contract**

Create `src/AcDream.Plugin.Abstractions/PluginImageRegions.cs`:

```csharp
namespace AcDream.Plugin.Abstractions;

/// <summary>
/// How far in from each edge of an image the borders of a nine-slice
/// frame run, in the image's own pixels. The four corners are drawn at
/// these sizes; the edges and the middle between them stretch.
/// </summary>
/// <param name="Left">The width of the left column.</param>
/// <param name="Top">The height of the top row.</param>
/// <param name="Right">The width of the right column.</param>
/// <param name="Bottom">The height of the bottom row.</param>
public readonly record struct PluginInsets(double Left, double Top, double Right, double Bottom)
{
    /// <summary>The same inset on all four sides.</summary>
    /// <param name="all">The inset, in the image's pixels.</param>
    /// <returns>Insets of <paramref name="all"/> on every side.</returns>
    public static PluginInsets Uniform(double all) => new(all, all, all, all);
}
```


In `src/AcDream.Plugin.Abstractions/PluginCanvas.cs`, replace

```csharp
        double scaleY = 1.0);

    /// <summary>
    /// Restricts what follows to a rectangle, intersected with whatever clip
    /// is already in force. Every push must be matched by a
    /// <see cref="PopClip"/> before the paint callback returns.
```

with

```csharp
        double scaleY = 1.0);

    /// <summary>
    /// Draws part of an image, such as one frame of a sprite sheet, stretched
    /// over a rectangle. The part is cut to the image's bounds, and the
    /// rectangle shrinks with it, so a source that runs off the image draws
    /// only the part that is on it, where it would have been. An empty
    /// source, or one wholly off the image, draws nothing. On a host that
    /// predates image regions this draws nothing.
    /// </summary>
    /// <param name="image">An image from <see cref="IPluginImages"/>; a released or invalid image draws nothing.</param>
    /// <param name="source">The part of the image to draw, in the image's own pixels.</param>
    /// <param name="destination">The rectangle the part fills.</param>
    /// <param name="tint">A colour the image is multiplied by; <see cref="PluginColor.White"/> leaves it unchanged.</param>
    void DrawImageRegion(PluginImage image, PluginRect source, PluginRect destination, PluginColor tint)
    {
    }

    /// <summary>
    /// Draws part of an image over a rectangle after scaling and turning that
    /// rectangle about a pivot, as <see cref="DrawImageTransformed"/> does
    /// for a whole image. The part is cut to the image's bounds as in
    /// <see cref="DrawImageRegion"/>; the pivot stays where it was. On a host
    /// that predates image regions this draws nothing.
    /// </summary>
    /// <param name="image">An image from <see cref="IPluginImages"/>; a released or invalid image draws nothing.</param>
    /// <param name="source">The part of the image to draw, in the image's own pixels.</param>
    /// <param name="destination">The rectangle the part would fill unturned and unscaled.</param>
    /// <param name="tint">A colour the image is multiplied by; <see cref="PluginColor.White"/> leaves it unchanged.</param>
    /// <param name="rotationRadians">How far to turn the rectangle about the pivot, in radians, clockwise on screen.</param>
    /// <param name="pivot">The point, in pixels from the rectangle's top-left corner, the rectangle turns and scales about.</param>
    /// <param name="scaleX">How much to stretch the rectangle sideways about the pivot; 1 leaves it.</param>
    /// <param name="scaleY">How much to stretch the rectangle vertically about the pivot; 1 leaves it.</param>
    void DrawImageRegionTransformed(
        PluginImage image,
        PluginRect source,
        PluginRect destination,
        PluginColor tint,
        double rotationRadians,
        PluginPoint pivot,
        double scaleX = 1.0,
        double scaleY = 1.0)
    {
    }

    /// <summary>
    /// Draws an image as a nine-slice frame stretched over a rectangle: the
    /// four corners at their own size, the edges stretched along the frame
    /// and the middle stretched both ways, so a panel background or a button
    /// keeps crisp corners at any size. Nothing is tiled. When the rectangle
    /// is narrower or shorter than the two corners on that axis, those
    /// corners shrink to fit, in proportion. Negative or non-finite insets
    /// draw nothing. On a host that predates image regions this draws
    /// nothing.
    /// </summary>
    /// <param name="image">An image from <see cref="IPluginImages"/>; a released or invalid image draws nothing.</param>
    /// <param name="destination">The rectangle the frame fills.</param>
    /// <param name="insets">How far in from the source's edges the corners run, in the image's own pixels; insets that add up to more than the source are scaled down to fit it.</param>
    /// <param name="tint">A colour the image is multiplied by; <see cref="PluginColor.White"/> leaves it unchanged.</param>
    /// <param name="source">The frame's part of the image, in the image's own pixels, cut to the image's bounds; null takes the whole image.</param>
    /// <param name="drawCenter">False leaves the middle out, drawing only the frame around it.</param>
    void DrawImageNineSlice(
        PluginImage image,
        PluginRect destination,
        PluginInsets insets,
        PluginColor tint,
        PluginRect? source = null,
        bool drawCenter = true)
    {
    }

    /// <summary>
    /// Restricts what follows to a rectangle, intersected with whatever clip
    /// is already in force. Every push must be matched by a
    /// <see cref="PopClip"/> before the paint callback returns.
```


- [ ] **Step 4: Run it to see it pass**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)' && dotnet test tests/AcDream.Plugin.Tests -c Release --no-build --filter "FullyQualifiedName~PluginCanvasImageRegionsContractTests"`
Expected: `0 Warning(s)`, `0 Error(s)`, `Passed!  - Failed:     0, Passed:     2`

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.Plugin.Abstractions/PluginImageRegions.cs src/AcDream.Plugin.Abstractions/PluginCanvas.cs \
  tests/AcDream.Plugin.Tests/PluginCanvasImageRegionsContractTests.cs
git commit -m "plugin api: image regions and nine-slice on the painter contract" \
  -m "Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

---

### Task 2: Images report their filtering; plugin art is clamped

**Goal:** Every image the table holds knows whether its texture is sampled linearly (asked of the backend once, when first held), `TryResolve` can report it, and the plugin's own uploads sample with a clamping sampler.

**Files:**
- Modify: `src/AcDream.App/Rendering/TextureCache.cs`
- Modify: `src/AcDream.App/Plugins/PluginImageTable.cs`
- Modify: `src/AcDream.App/Plugins/PluginImages.cs`
- Modify: `src/AcDream.App/UI/RetailPluginImageBackend.cs`
- Test: `tests/AcDream.App.Tests/Plugins/PluginImageTableTests.cs`
- Test: `tests/AcDream.App.Tests/Rendering/TextureCacheReleasableUiTextureTests.cs`
- Modify (fakes): `tests/AcDream.App.Tests/Plugins/BufferedUiRegistryImagesTests.cs`, `tests/AcDream.App.Tests/UI/Layout/PluginCanvasTeardownOrderTests.cs`, `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs`

**Acceptance Criteria:**
- [ ] Client art and the plugin's own art resolve with `linearFiltered == true`; a texture the backend calls nearest resolves `false`; a released handle resolves `false` with `linearFiltered == false`
- [ ] The backend is asked once per distinct image, not again on a repeat request
- [ ] `UploadReleasableRgba8` registers its texture with `GpuSamplerDescription.WorldClamp`
- [ ] `TextureCache.IsNearestUiTexture` is true for an ad-hoc `nearest: true` upload, false for a linear one and for 0
- [ ] `RetailPluginImageBackend.IsLinearFiltered(t) == !textures.IsNearestUiTexture(t)`
- [ ] The element tests' fake hands out a 32×32 spell icon (`IconTexture = 78`) that it reports nearest
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginImageTableTests|FullyQualifiedName~TextureCacheReleasableUiTextureTests|FullyQualifiedName~BufferedUiRegistryImagesTests|FullyQualifiedName~PluginCanvasTeardownOrderTests"` → `Passed!  - Failed:     0, Passed:    31`

**Steps:**

- [ ] **Step 1: Write the failing tests and give the fakes the new member**

In `tests/AcDream.App.Tests/Plugins/PluginImageTableTests.cs`, replace

```csharp
        public List<(uint Texture, int Width, int Height, string Name)> Uploads { get; } = [];
        public List<uint> Released { get; } = [];
        public HashSet<uint> KnownArt { get; } = [0x06001234u];

        public bool TryGetClientArt(uint surfaceId, out uint texture, out int width, out int height)
        {
```

with

```csharp
        public List<(uint Texture, int Width, int Height, string Name)> Uploads { get; } = [];
        public List<uint> Released { get; } = [];
        public HashSet<uint> KnownArt { get; } = [0x06001234u];
        public HashSet<uint> NearestTextures { get; } = [];
        public List<uint> FilterQueries { get; } = [];

        public bool TryGetClientArt(uint surfaceId, out uint texture, out int width, out int height)
        {
```

In `tests/AcDream.App.Tests/Plugins/PluginImageTableTests.cs`, replace

```csharp
        {
            Released.Add(texture);
            return true;
        }
    }

```

with

```csharp
        {
            Released.Add(texture);
            return true;
        }

        public bool IsLinearFiltered(uint texture)
        {
            FilterQueries.Add(texture);
            return !NearestTextures.Contains(texture);
        }
    }

```

In `tests/AcDream.App.Tests/Plugins/PluginImageTableTests.cs`, replace

```csharp
        Assert.Equal([backend.Uploads[0].Texture], backend.Released);
        Assert.Equal(0, table.Count);
        Assert.Equal(0L, table.OwnedBytes);
    }

    [Fact]
```

with

```csharp
        Assert.Equal([backend.Uploads[0].Texture], backend.Released);
        Assert.Equal(0, table.Count);
        Assert.Equal(0L, table.OwnedBytes);
    }

    [Fact]
    public void EachImageCarriesTheBackendsFilterAskedOnceWhenFirstHeld()
    {
        (PluginImageTable table, FakeBackend backend, _) = Bound();
        backend.NearestTextures.Add(8u);

        PluginImageHandle art = table.AcquireClientArt(0x06001234u);
        PluginImageHandle icon = table.AcquireSpellIcon(42u);
        PluginImageHandle own = table.AcquireDecoded("map", Png64());
        PluginImageHandle iconAgain = table.AcquireSpellIcon(42u);

        Assert.True(table.TryResolve(art, out uint artTexture, out _, out _, out bool artLinear));
        Assert.True(table.TryResolve(icon, out uint iconTexture, out _, out _, out bool iconLinear));
        Assert.True(table.TryResolve(own, out uint ownTexture, out _, out _, out bool ownLinear));
        Assert.Equal((7u, true), (artTexture, artLinear));
        Assert.Equal((8u, false), (iconTexture, iconLinear));
        Assert.Equal((backend.Uploads[0].Texture, true), (ownTexture, ownLinear));
        Assert.Equal(icon, iconAgain);
        Assert.Equal([7u, 8u, backend.Uploads[0].Texture], backend.FilterQueries);

        Assert.True(table.Release(own));
        Assert.False(table.TryResolve(own, out _, out _, out _, out bool releasedLinear));
        Assert.False(releasedLinear);
    }

    [Fact]
```


In `tests/AcDream.App.Tests/Rendering/TextureCacheReleasableUiTextureTests.cs`, replace

```csharp
            device.OfKind<GpuRecordedTextureRelease>(),
            call => call.Slot == UiTextureTableHandle.ToSlot(handle).Index);
    }
}
```

with

```csharp
            device.OfKind<GpuRecordedTextureRelease>(),
            call => call.Slot == UiTextureTableHandle.ToSlot(handle).Index);
    }

    [Fact]
    public void AReleasableUploadIsSampledLinearAndClampedSoASheetsEdgesDoNotWrap()
    {
        (RecordingGpuDevice device, TextureCache cache, _) = Build();

        uint handle = cache.UploadReleasableRgba8(new byte[4 * 4 * 4], 4, 4, "test-sheet");

        GpuRecordedTextureRegistration registration = Assert.Single(
            device.OfKind<GpuRecordedTextureRegistration>(), r => r.TextureName == "test-sheet");
        Assert.Equal(GpuSamplerDescription.WorldClamp, registration.Sampler);
        Assert.False(cache.IsNearestUiTexture(handle));
    }

    [Fact]
    public void AnAdHocNearestUploadIsReportedNearestAndALinearOneIsNot()
    {
        (_, TextureCache cache, _) = Build();

        uint nearest = cache.UploadRgba8(new byte[4 * 4 * 4], 4, 4, nearest: true);
        uint linear = cache.UploadRgba8(new byte[4 * 4 * 4], 4, 4);

        Assert.True(cache.IsNearestUiTexture(nearest));
        Assert.False(cache.IsNearestUiTexture(linear));
        Assert.False(cache.IsNearestUiTexture(0u));
    }
}
```


In `tests/AcDream.App.Tests/Plugins/BufferedUiRegistryImagesTests.cs`, replace

```csharp
            Released.Add(texture);
            return true;
        }
    }

    private sealed class SilentLogger : IPluginLogger
```

with

```csharp
            Released.Add(texture);
            return true;
        }

        public bool IsLinearFiltered(uint texture) => true;
    }

    private sealed class SilentLogger : IPluginLogger
```


In `tests/AcDream.App.Tests/UI/Layout/PluginCanvasTeardownOrderTests.cs`, replace

```csharp
            cache.UploadReleasableRgba8(rgba, width, height, debugName);

        public bool ReleaseOwned(uint texture) => cache.ReleaseUiTexture(texture);
    }

    private sealed class Harness
```

with

```csharp
            cache.UploadReleasableRgba8(rgba, width, height, debugName);

        public bool ReleaseOwned(uint texture) => cache.ReleaseUiTexture(texture);

        public bool IsLinearFiltered(uint texture) => !cache.IsNearestUiTexture(texture);
    }

    private sealed class Harness
```


In `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs`, replace

```csharp
    private sealed class FakeImageBackend : IPluginImageBackend
    {
        public const uint ArtTexture = 77u;

        public bool TryGetClientArt(uint surfaceId, out uint texture, out int width, out int height)
        {
```

with

```csharp
    private sealed class FakeImageBackend : IPluginImageBackend
    {
        public const uint ArtTexture = 77u;
        public const uint IconTexture = 78u;

        public bool TryGetClientArt(uint surfaceId, out uint texture, out int width, out int height)
        {
```

In `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs`, replace

```csharp

        public bool TryGetSpellIcon(uint spellId, out uint texture, out int width, out int height)
        {
            texture = 0u; width = 0; height = 0;
            return false;
        }

        public bool TryGetObjectIcon(uint objectId, out uint texture, out int width, out int height)
```

with

```csharp

        public bool TryGetSpellIcon(uint spellId, out uint texture, out int width, out int height)
        {
            texture = IconTexture; width = 32; height = 32;
            return true;
        }

        public bool TryGetObjectIcon(uint objectId, out uint texture, out int width, out int height)
```

In `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs`, replace

```csharp

        public uint UploadOwned(byte[] rgba, int width, int height, string debugName) => 0u;
        public bool ReleaseOwned(uint texture) => false;
    }

    /// <summary>A clock the test advances by hand: each guarded call costs what the test says.</summary>
```

with

```csharp

        public uint UploadOwned(byte[] rgba, int width, int height, string debugName) => 0u;
        public bool ReleaseOwned(uint texture) => false;

        // Client art is linear, icons nearest, as the live backend reports them.
        public bool IsLinearFiltered(uint texture) => texture != IconTexture;
    }

    /// <summary>A clock the test advances by hand: each guarded call costs what the test says.</summary>
```


- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -E 'error CS' | sort -u | head -5`
Expected: errors that `TryResolve` has no overload taking 5 arguments and that `TextureCache` has no `IsNearestUiTexture`.

- [ ] **Step 3: Implement**

In `src/AcDream.App/Rendering/TextureCache.cs`, replace

```csharp
        }
    }

    private GpuUiTextureEntry UploadUiTexture(DecodedTexture decoded, bool nearest, string debugName)
    {
        IGpuTexture texture = _device.CreateTexture(new GpuTextureDescription(
            debugName,
```

with

```csharp
        }
    }

    private GpuUiTextureEntry UploadUiTexture(
        DecodedTexture decoded, bool nearest, string debugName, bool clamp = false)
    {
        IGpuTexture texture = _device.CreateTexture(new GpuTextureDescription(
            debugName,
```

In `src/AcDream.App/Rendering/TextureCache.cs`, replace

```csharp
            uint glName = UploadAccountingName(texture);
            TrackUploadedTexture(glName, decoded.Width, decoded.Height);

            IGpuSampler sampler = _device.CreateSampler(nearest ? UiNearestRepeat : GpuSamplerDescription.WorldRepeat);
            GpuTextureSlot slot = _device.RegisterTexture(texture, sampler);
            uint handle = UiTextureTableHandle.FromSlot(slot);
            if (nearest)
```

with

```csharp
            uint glName = UploadAccountingName(texture);
            TrackUploadedTexture(glName, decoded.Width, decoded.Height);

            IGpuSampler sampler = _device.CreateSampler(
                nearest ? UiNearestRepeat
                : clamp ? GpuSamplerDescription.WorldClamp
                : GpuSamplerDescription.WorldRepeat);
            GpuTextureSlot slot = _device.RegisterTexture(texture, sampler);
            uint handle = UiTextureTableHandle.FromSlot(slot);
            if (nearest)
```

In `src/AcDream.App/Rendering/TextureCache.cs`, replace

```csharp
            throw;
        }
    }

    internal uint GetOrCreateLinearUiTwin(uint handle)
    {
```

with

```csharp
            throw;
        }
    }

    /// <summary>
    /// True for an interface texture uploaded to be sampled nearest, such as
    /// a composed icon; false for one sampled linearly, and for any handle
    /// this cache did not upload that way.
    /// </summary>
    internal bool IsNearestUiTexture(uint handle) => _nearestUiTextureSources.ContainsKey(handle);

    internal uint GetOrCreateLinearUiTwin(uint handle)
    {
```

In `src/AcDream.App/Rendering/TextureCache.cs`, replace

```csharp
    /// Uploads an interface texture that the caller will give back through
    /// <see cref="ReleaseUiTexture"/>. The ad-hoc path keeps every upload for
    /// the life of the cache; this one is for art whose owner comes and goes,
    /// such as a plugin's own images.
    /// </summary>
    internal uint UploadReleasableRgba8(byte[] rgba, int width, int height, string debugName)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentException.ThrowIfNullOrWhiteSpace(debugName);
        GpuUiTextureEntry entry = UploadUiTexture(
            new DecodedTexture(rgba, width, height), nearest: false, debugName);
        uint handle = UiTextureTableHandle.FromSlot(entry.Slot);
        _releasableUiTextures.Add(handle, entry);
        return handle;
```

with

```csharp
    /// Uploads an interface texture that the caller will give back through
    /// <see cref="ReleaseUiTexture"/>. The ad-hoc path keeps every upload for
    /// the life of the cache; this one is for art whose owner comes and goes,
    /// such as a plugin's own images. It is sampled linearly and clamped, so
    /// a part cut from the edge of a sheet does not pick up the opposite
    /// edge.
    /// </summary>
    internal uint UploadReleasableRgba8(byte[] rgba, int width, int height, string debugName)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentException.ThrowIfNullOrWhiteSpace(debugName);
        GpuUiTextureEntry entry = UploadUiTexture(
            new DecodedTexture(rgba, width, height), nearest: false, debugName, clamp: true);
        uint handle = UiTextureTableHandle.FromSlot(entry.Slot);
        _releasableUiTextures.Add(handle, entry);
        return handle;
```


In `src/AcDream.App/Plugins/PluginImageTable.cs`, replace

```csharp

    /// <summary>Gives back a texture from <see cref="UploadOwned"/>.</summary>
    bool ReleaseOwned(uint texture);
}

/// <summary>
```

with

```csharp

    /// <summary>Gives back a texture from <see cref="UploadOwned"/>.</summary>
    bool ReleaseOwned(uint texture);

    /// <summary>
    /// Whether a texture this backend handed out is sampled linearly. A part
    /// cut from a linear texture blends in the pixels just outside it unless
    /// its edges are pulled in by half a pixel; a nearest one does not.
    /// </summary>
    bool IsLinearFiltered(uint texture);
}

/// <summary>
```

In `src/AcDream.App/Plugins/PluginImageTable.cs`, replace

```csharp

    private readonly record struct Key(Kind Kind, uint Id, string? Name);

    private sealed class Entry(Key key, int id, uint texture, int width, int height, long ownedBytes)
    {
        internal Key Key { get; } = key;
        internal int Id { get; } = id;
        internal uint Texture { get; } = texture;
        internal int Width { get; } = width;
        internal int Height { get; } = height;

        /// <summary>Zero for shared client art; the upload size for the plugin's own.</summary>
        internal long OwnedBytes { get; } = ownedBytes;
```

with

```csharp

    private readonly record struct Key(Kind Kind, uint Id, string? Name);

    private sealed class Entry(
        Key key, int id, uint texture, int width, int height, long ownedBytes, bool linearFiltered)
    {
        internal Key Key { get; } = key;
        internal int Id { get; } = id;
        internal uint Texture { get; } = texture;
        internal int Width { get; } = width;
        internal int Height { get; } = height;

        /// <summary>Whether the texture is sampled linearly, asked of the backend once, when it is first held.</summary>
        internal bool LinearFiltered { get; } = linearFiltered;

        /// <summary>Zero for shared client art; the upload size for the plugin's own.</summary>
        internal long OwnedBytes { get; } = ownedBytes;
```

In `src/AcDream.App/Plugins/PluginImageTable.cs`, replace

```csharp
        uint texture = backend.UploadOwned(
            decoded.Data, decoded.Width, decoded.Height, $"plugin-{_ownerId}-{name}");
        _ownedBytes += bytes;
        return Add(key, texture, decoded.Width, decoded.Height, bytes).Handle;
    }

    /// <summary>
```

with

```csharp
        uint texture = backend.UploadOwned(
            decoded.Data, decoded.Width, decoded.Height, $"plugin-{_ownerId}-{name}");
        _ownedBytes += bytes;
        return Add(key, texture, decoded.Width, decoded.Height, bytes, backend.IsLinearFiltered(texture)).Handle;
    }

    /// <summary>
```

In `src/AcDream.App/Plugins/PluginImageTable.cs`, replace

```csharp
    /// The interface texture behind a handle, for the painter. False for a
    /// handle that was released, issued by another table, or never issued.
    /// </summary>
    internal bool TryResolve(PluginImageHandle handle, out uint texture, out int width, out int height)
    {
        if (handle.IsValid && !_disposed && _byId.TryGetValue(handle.Id, out Entry? entry))
        {
            texture = entry.Texture;
            width = entry.Width;
            height = entry.Height;
            return true;
        }
        texture = 0u;
        width = 0;
        height = 0;
        return false;
    }

```

with

```csharp
    /// The interface texture behind a handle, for the painter. False for a
    /// handle that was released, issued by another table, or never issued.
    /// </summary>
    internal bool TryResolve(PluginImageHandle handle, out uint texture, out int width, out int height) =>
        TryResolve(handle, out texture, out width, out height, out _);

    /// <summary>
    /// The interface texture behind a handle and whether it is sampled
    /// linearly, which decides whether a region of it is pulled in by half
    /// a pixel.
    /// </summary>
    internal bool TryResolve(
        PluginImageHandle handle, out uint texture, out int width, out int height, out bool linearFiltered)
    {
        if (handle.IsValid && !_disposed && _byId.TryGetValue(handle.Id, out Entry? entry))
        {
            texture = entry.Texture;
            width = entry.Width;
            height = entry.Height;
            linearFiltered = entry.LinearFiltered;
            return true;
        }
        texture = 0u;
        width = 0;
        height = 0;
        linearFiltered = false;
        return false;
    }

```

In `src/AcDream.App/Plugins/PluginImageTable.cs`, replace

```csharp
            ReportOnce($"missing:{what}", $"{what} is not something the client can draw");
            return PluginImageHandle.None;
        }
        return Add(key, texture, width, height, ownedBytes: 0L).Handle;
    }

    private bool TryEnter(out IPluginImageBackend backend)
```

with

```csharp
            ReportOnce($"missing:{what}", $"{what} is not something the client can draw");
            return PluginImageHandle.None;
        }
        return Add(key, texture, width, height, ownedBytes: 0L, backend.IsLinearFiltered(texture)).Handle;
    }

    private bool TryEnter(out IPluginImageBackend backend)
```

In `src/AcDream.App/Plugins/PluginImageTable.cs`, replace

```csharp
        return false;
    }

    private Entry Add(Key key, uint texture, int width, int height, long ownedBytes)
    {
        var entry = new Entry(key, checked(++_nextId), texture, width, height, ownedBytes);
        _byKey.Add(key, entry);
        _byId.Add(entry.Id, entry);
        return entry;
```

with

```csharp
        return false;
    }

    private Entry Add(Key key, uint texture, int width, int height, long ownedBytes, bool linearFiltered)
    {
        var entry = new Entry(key, checked(++_nextId), texture, width, height, ownedBytes, linearFiltered);
        _byKey.Add(key, entry);
        _byId.Add(entry.Id, entry);
        return entry;
```


In `src/AcDream.App/Plugins/PluginImages.cs`, replace

```csharp
    public int MaximumDimension => _table.Budget.MaximumDimension;

    /// <summary>The interface texture behind an image, for the painter.</summary>
    internal bool TryResolve(PluginImage image, out uint texture, out int width, out int height)
    {
        if (!_disposed)
            return _table.TryResolve(ToHandle(image), out texture, out width, out height);
        texture = 0u;
        width = 0;
        height = 0;
        return false;
    }

```

with

```csharp
    public int MaximumDimension => _table.Budget.MaximumDimension;

    /// <summary>The interface texture behind an image, for the painter.</summary>
    internal bool TryResolve(PluginImage image, out uint texture, out int width, out int height) =>
        TryResolve(image, out texture, out width, out height, out _);

    /// <summary>The interface texture behind an image and whether it is sampled linearly, for the painter.</summary>
    internal bool TryResolve(
        PluginImage image, out uint texture, out int width, out int height, out bool linearFiltered)
    {
        if (!_disposed)
            return _table.TryResolve(ToHandle(image), out texture, out width, out height, out linearFiltered);
        texture = 0u;
        width = 0;
        height = 0;
        linearFiltered = false;
        return false;
    }

```


In `src/AcDream.App/UI/RetailPluginImageBackend.cs`, replace

```csharp
        _textures.UploadReleasableRgba8(rgba, width, height, debugName);

    public bool ReleaseOwned(uint texture) => _textures.ReleaseUiTexture(texture);
}
```

with

```csharp
        _textures.UploadReleasableRgba8(rgba, width, height, debugName);

    public bool ReleaseOwned(uint texture) => _textures.ReleaseUiTexture(texture);

    // Client art and the plugin's own art are uploaded linear; the composed
    // spell and object icons are uploaded nearest, as the client draws them.
    public bool IsLinearFiltered(uint texture) => !_textures.IsNearestUiTexture(texture);
}
```


- [ ] **Step 4: Run them to see them pass**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)' && dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~PluginImageTableTests|FullyQualifiedName~TextureCacheReleasableUiTextureTests|FullyQualifiedName~BufferedUiRegistryImagesTests|FullyQualifiedName~PluginCanvasTeardownOrderTests|FullyQualifiedName~PluginCanvasElementTests"`
Expected: `0 Warning(s)`, `0 Error(s)`, `Passed!  - Failed:     0, Passed:    44` (the 31 of the Verify filter plus the 13 existing element tests, which still pass with the changed fake).

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/Rendering/TextureCache.cs src/AcDream.App/Plugins/PluginImageTable.cs \
  src/AcDream.App/Plugins/PluginImages.cs src/AcDream.App/UI/RetailPluginImageBackend.cs \
  tests/AcDream.App.Tests/Plugins/PluginImageTableTests.cs \
  tests/AcDream.App.Tests/Rendering/TextureCacheReleasableUiTextureTests.cs \
  tests/AcDream.App.Tests/Plugins/BufferedUiRegistryImagesTests.cs \
  tests/AcDream.App.Tests/UI/Layout/PluginCanvasTeardownOrderTests.cs \
  tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs
git commit -m "plugin canvas: images report their filtering; plugin art is clamped" \
  -m "Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

---

### Task 3: Region and nine-slice arithmetic

**Goal:** A pure `CanvasImageRegions` maps a source rectangle to a destination piece with texture coordinates, and a nine-slice to up to nine pieces, following the Global Constraints' cut, shrink and pull-in rules.

**Files:**
- Create: `src/AcDream.App/UI/CanvasImageRegions.cs`
- Test: `tests/AcDream.App.Tests/UI/CanvasImageRegionsTests.cs`

**Acceptance Criteria:**
- [ ] Source (16, 8, 16, 24) of a 64² nearest image over (5, 6, 32, 48) maps to UVs (0.25, 0.125, 0.5, 0.5) with the destination unchanged
- [ ] On a linear image an inner edge is pulled in by 0.5/64 and an edge on the image's border is not; the whole image maps to (0, 0, 1, 1)
- [ ] A source half off the image keeps the matching half of the destination, in place
- [ ] A nine-slice of (10, 20, 100, 50) with insets (4, 6, 8, 10) is nine pieces with corners at inset size; a destination 6 wide with side insets 4 + 8 shrinks them to 2 + 4 and drops the middle column; `drawCenter: false` gives eight pieces
- [ ] With a source inside a linear sheet only the frame's outer edges are pulled in, never a seam
- [ ] Insets adding to more than the source are scaled down to fit; negative or non-finite insets, empty or non-finite rectangles and off-image sources give nothing

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~CanvasImageRegionsTests"` → `Passed!  - Failed:     0, Passed:    24`

**Steps:**

- [ ] **Step 1: Write the failing tests**

Create `tests/AcDream.App.Tests/UI/CanvasImageRegionsTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI;

/// <summary>
/// Source rectangles become texture coordinates: cut to the image, the
/// destination shrinking with the cut; on a linear texture an edge inside
/// the image is pulled in by half a pixel and an edge on its border is not.
/// A nine-slice is up to nine such pieces, corners at their own size,
/// shrinking when the destination is too small for them.
/// </summary>
public sealed class CanvasImageRegionsTests
{
    private const int Sheet = 64;

    private static CanvasImagePiece Region(PluginRect source, PluginRect destination, bool linear)
    {
        Assert.True(CanvasImageRegions.TryMapRegion(Sheet, Sheet, linear, source, destination, out CanvasImagePiece piece));
        return piece;
    }

    private static CanvasImagePiece[] NineSlice(
        PluginRect destination, PluginInsets insets, PluginRect? source = null,
        bool drawCenter = true, bool linear = false)
    {
        var pieces = new CanvasImagePiece[CanvasImageRegions.MaximumNineSlicePieces];
        int count = CanvasImageRegions.NineSlice(Sheet, Sheet, linear, destination, insets, source, drawCenter, pieces);
        return pieces[..count];
    }

    private static float Texel(double pixels) => (float)(pixels / Sheet);

    [Fact]
    public void ARegionOnANearestTextureMapsToItsExactCoordinates()
    {
        CanvasImagePiece piece = Region(new PluginRect(16, 8, 16, 24), new PluginRect(5, 6, 32, 48), linear: false);

        Assert.Equal(new CanvasImagePiece(5, 6, 32, 48, 0.25f, 0.125f, 0.5f, 0.5f), piece);
    }

    [Fact]
    public void ARegionOnALinearTextureIsPulledInByHalfAPixelOnlyWhereItsEdgeIsInsideTheImage()
    {
        // The top edge is the image's own top: not pulled in. The others are inside.
        CanvasImagePiece piece = Region(new PluginRect(16, 0, 16, 16), new PluginRect(0, 0, 16, 16), linear: true);

        Assert.Equal((Texel(16.5), 0f, Texel(31.5), Texel(15.5)), (piece.U0, piece.V0, piece.U1, piece.V1));
        Assert.Equal((0f, 0f, 16f, 16f), (piece.X, piece.Y, piece.Width, piece.Height));
    }

    [Fact]
    public void TheWholeImageAsARegionSamplesExactlyAsAWholeImageDraw()
    {
        CanvasImagePiece piece = Region(new PluginRect(0, 0, Sheet, Sheet), new PluginRect(1, 2, 3, 4), linear: true);

        Assert.Equal(new CanvasImagePiece(1, 2, 3, 4, 0f, 0f, 1f, 1f), piece);
    }

    [Fact]
    public void ASourceRunningOffTheImageIsCutAndTheDestinationShrinksWithIt()
    {
        // Half the source is left of the image; the right half of the destination remains.
        CanvasImagePiece piece = Region(new PluginRect(-16, 56, 32, 16), new PluginRect(0, 0, 64, 32), linear: false);

        Assert.Equal(new CanvasImagePiece(32, 0, 32, 16, 0f, Texel(56), Texel(16), 1f), piece);
    }

    [Fact]
    public void ARegionNarrowerThanAPixelSamplesItsMiddleOnALinearTexture()
    {
        CanvasImagePiece piece = Region(new PluginRect(10.25, 10, 0.5, 4), new PluginRect(0, 0, 8, 8), linear: true);

        Assert.Equal(Texel(10.5), piece.U0);
        Assert.Equal(Texel(10.5), piece.U1);
        Assert.Equal((Texel(10.5), Texel(13.5)), (piece.V0, piece.V1));
    }

    [Theory]
    [InlineData(0, 0, 0, 16)]
    [InlineData(0, 0, 16, -1)]
    [InlineData(64, 0, 16, 16)]
    [InlineData(-32, 0, 16, 16)]
    [InlineData(double.NaN, 0, 16, 16)]
    [InlineData(0, double.PositiveInfinity, 16, 16)]
    public void AnEmptyOffImageOrNonFiniteSourceDrawsNothing(double x, double y, double width, double height)
    {
        Assert.False(CanvasImageRegions.TryMapRegion(
            Sheet, Sheet, true, new PluginRect(x, y, width, height), new PluginRect(0, 0, 16, 16), out _));
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, -4)]
    [InlineData(double.NaN, 16)]
    public void AnEmptyOrNonFiniteDestinationDrawsNothing(double width, double height)
    {
        Assert.False(CanvasImageRegions.TryMapRegion(
            Sheet, Sheet, false, new PluginRect(0, 0, 16, 16), new PluginRect(0, 0, width, height), out _));
    }

    [Fact]
    public void ALargeNineSliceDrawsCornersAtTheirSizeAndStretchesTheRest()
    {
        CanvasImagePiece[] pieces = NineSlice(new PluginRect(10, 20, 100, 50), new PluginInsets(4, 6, 8, 10));

        Assert.Equal(9, pieces.Length);
        Assert.Equal(
            [
                (10f, 20f, 4f, 6f), (14f, 20f, 88f, 6f), (102f, 20f, 8f, 6f),
                (10f, 26f, 4f, 34f), (14f, 26f, 88f, 34f), (102f, 26f, 8f, 34f),
                (10f, 60f, 4f, 10f), (14f, 60f, 88f, 10f), (102f, 60f, 8f, 10f),
            ],
            pieces.Select(p => (p.X, p.Y, p.Width, p.Height)).ToArray());
        // Columns 0..4, 4..56, 56..64 and rows 0..6, 6..54, 54..64 of the whole image.
        Assert.Equal((0f, 0f, Texel(4), Texel(6)), (pieces[0].U0, pieces[0].V0, pieces[0].U1, pieces[0].V1));
        Assert.Equal((Texel(4), Texel(6), Texel(56), Texel(54)), (pieces[4].U0, pieces[4].V0, pieces[4].U1, pieces[4].V1));
        Assert.Equal((Texel(56), Texel(54), 1f, 1f), (pieces[8].U0, pieces[8].V0, pieces[8].U1, pieces[8].V1));
    }

    [Fact]
    public void ADestinationSmallerThanItsCornersShrinksThemInProportion()
    {
        // 6 wide for corners of 4 + 8: they shrink to 2 + 4 and the middle column goes.
        CanvasImagePiece[] pieces = NineSlice(new PluginRect(0, 0, 6, 40), new PluginInsets(4, 4, 8, 4));

        Assert.Equal(6, pieces.Length);
        Assert.Equal(
            [(0f, 0f, 2f, 4f), (2f, 0f, 4f, 4f), (0f, 4f, 2f, 32f), (2f, 4f, 4f, 32f), (0f, 36f, 2f, 4f), (2f, 36f, 4f, 4f)],
            pieces.Select(p => (p.X, p.Y, p.Width, p.Height)).ToArray());
        // The corners still show their whole inset of the image.
        Assert.Equal((0f, Texel(4)), (pieces[0].U0, pieces[0].U1));
        Assert.Equal((Texel(56), 1f), (pieces[1].U0, pieces[1].U1));
    }

    [Fact]
    public void AFrameWithoutItsMiddleIsTheEightPiecesAroundIt()
    {
        CanvasImagePiece[] pieces = NineSlice(new PluginRect(0, 0, 40, 40), PluginInsets.Uniform(8), drawCenter: false);

        Assert.Equal(8, pieces.Length);
        Assert.DoesNotContain(pieces, p => p.X == 8f && p.Y == 8f);
    }

    [Fact]
    public void ASourceSelectsTheFrameInsideASheetAndOnlyItsOuterEdgesArePulledIn()
    {
        // A 16x16 frame at (16, 16) on a linear sheet, insets of 4.
        CanvasImagePiece[] pieces = NineSlice(
            new PluginRect(0, 0, 32, 32), PluginInsets.Uniform(4), new PluginRect(16, 16, 16, 16), linear: true);

        Assert.Equal(9, pieces.Length);
        // Top-left corner: its outer edges are pulled in, the seams with its neighbours are not.
        Assert.Equal((Texel(16.5), Texel(16.5), Texel(20), Texel(20)), (pieces[0].U0, pieces[0].V0, pieces[0].U1, pieces[0].V1));
        // The middle touches no outer edge.
        Assert.Equal((Texel(20), Texel(20), Texel(28), Texel(28)), (pieces[4].U0, pieces[4].V0, pieces[4].U1, pieces[4].V1));
        // Bottom-right corner.
        Assert.Equal((Texel(28), Texel(28), Texel(31.5), Texel(31.5)), (pieces[8].U0, pieces[8].V0, pieces[8].U1, pieces[8].V1));
    }

    [Fact]
    public void WithoutSideInsetsTheMiddleColumnCarriesTheOuterEdges()
    {
        CanvasImagePiece[] pieces = NineSlice(
            new PluginRect(0, 0, 32, 32), new PluginInsets(0, 4, 0, 4), new PluginRect(16, 16, 16, 16), linear: true);

        Assert.Equal(3, pieces.Length);
        Assert.All(pieces, p => Assert.Equal((Texel(16.5), Texel(31.5)), (p.U0, p.U1)));
        Assert.Equal([(0f, 4f), (4f, 24f), (28f, 4f)], pieces.Select(p => (p.Y, p.Height)).ToArray());
    }

    [Fact]
    public void InsetsLargerThanTheSourceAreScaledDownToFitIt()
    {
        // The source is 16 wide; insets of 24 + 8 = 32 halve to 12 + 4.
        CanvasImagePiece[] pieces = NineSlice(
            new PluginRect(0, 0, 100, 100), new PluginInsets(24, 0, 8, 0), new PluginRect(0, 0, 16, 64));

        // No middle is left between the halves, so two columns, one row.
        Assert.Equal(2, pieces.Length);
        Assert.Equal((0f, Texel(12)), (pieces[0].U0, pieces[0].U1));
        Assert.Equal((Texel(12), Texel(16)), (pieces[1].U0, pieces[1].U1));
        Assert.Equal((12f, 4f), (pieces[0].Width, pieces[1].Width));
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, double.NaN, 0, 0)]
    [InlineData(0, 0, double.PositiveInfinity, 0)]
    public void ABadInsetDrawsNothing(double left, double top, double right, double bottom)
    {
        Assert.Empty(NineSlice(new PluginRect(0, 0, 40, 40), new PluginInsets(left, top, right, bottom)));
    }

    [Fact]
    public void ANineSliceWithASourceOffTheImageOrAnEmptyDestinationDrawsNothing()
    {
        Assert.Empty(NineSlice(new PluginRect(0, 0, 40, 40), PluginInsets.Uniform(4), new PluginRect(100, 0, 16, 16)));
        Assert.Empty(NineSlice(new PluginRect(0, 0, 0, 40), PluginInsets.Uniform(4)));
    }
}
```


- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -E 'error CS' | sort -u | head -3`
Expected: `error CS0246: The type or namespace name 'CanvasImagePiece' could not be found`.

- [ ] **Step 3: Implement**

Create `src/AcDream.App/UI/CanvasImageRegions.cs`:

```csharp
using AcDream.Plugin.Abstractions;

namespace AcDream.App.UI;

/// <summary>
/// One upright textured quad: where it goes, in canvas pixels, and which
/// part of the texture it shows, in texture coordinates.
/// </summary>
internal readonly record struct CanvasImagePiece(
    float X, float Y, float Width, float Height,
    float U0, float V0, float U1, float V1);

/// <summary>
/// The arithmetic behind drawing part of an image: a source rectangle in
/// the image's pixels becomes texture coordinates, and a nine-slice frame
/// becomes up to nine such pieces. Pure, so it is tested on its own.
///
/// <para>On a linearly sampled texture a sample at a region's edge blends
/// in the pixel just outside it, which on a sheet is the neighbouring
/// frame. An edge that lies inside the image is therefore pulled in by half
/// a pixel, so the outermost sample is the centre of the region's own edge
/// pixel. An edge on the image's border is left alone: there it samples
/// exactly as a whole-image draw does. Seams between the pieces of one
/// nine-slice are left alone too, since what lies across a seam is the
/// neighbouring piece of the same frame. Nearest sampling never blends,
/// so nothing is pulled in.</para>
/// </summary>
internal static class CanvasImageRegions
{
    /// <summary>The most pieces one nine-slice produces.</summary>
    internal const int MaximumNineSlicePieces = 9;

    /// <summary>
    /// The piece that draws <paramref name="source"/> of an image over
    /// <paramref name="destination"/>. The source is cut to the image's
    /// bounds and the destination shrinks with it, in proportion, so what
    /// remains lands where it would have been. False when nothing is left
    /// to draw: an empty or non-finite rectangle, or a source wholly off
    /// the image.
    /// </summary>
    internal static bool TryMapRegion(
        int imageWidth,
        int imageHeight,
        bool linearFiltered,
        PluginRect source,
        PluginRect destination,
        out CanvasImagePiece piece)
    {
        piece = default;
        if (imageWidth <= 0 || imageHeight <= 0
            || !IsFinite(source) || !IsFinite(destination)
            || !(source.Width > 0 && source.Height > 0)
            || !(destination.Width > 0 && destination.Height > 0))
        {
            return false;
        }

        if (!TryCut(source, imageWidth, imageHeight, out double x0, out double y0, out double x1, out double y1))
            return false;

        double scaleX = destination.Width / source.Width;
        double scaleY = destination.Height / source.Height;
        Coordinates(x0, x1, imageWidth, linearFiltered && x0 > 0, linearFiltered && x1 < imageWidth,
            out float u0, out float u1);
        Coordinates(y0, y1, imageHeight, linearFiltered && y0 > 0, linearFiltered && y1 < imageHeight,
            out float v0, out float v1);
        piece = new CanvasImagePiece(
            (float)(destination.X + (x0 - source.X) * scaleX),
            (float)(destination.Y + (y0 - source.Y) * scaleY),
            (float)((x1 - x0) * scaleX),
            (float)((y1 - y0) * scaleY),
            u0, v0, u1, v1);
        return true;
    }

    /// <summary>
    /// The pieces of a nine-slice frame over <paramref name="destination"/>,
    /// written to <paramref name="pieces"/> row by row, top-left first.
    /// Returns how many were written: none for bad input, fewer than nine
    /// when a row or column is empty or the middle is left out.
    ///
    /// <para>The source (the whole image when null) is cut to the image's
    /// bounds; the destination is not moved. Insets that add up to more
    /// than the source on an axis are scaled down to fit it, and corners
    /// that add up to more than the destination on an axis shrink to fit
    /// it, both in proportion.</para>
    /// </summary>
    internal static int NineSlice(
        int imageWidth,
        int imageHeight,
        bool linearFiltered,
        PluginRect destination,
        PluginInsets insets,
        PluginRect? source,
        bool drawCenter,
        Span<CanvasImagePiece> pieces)
    {
        if (pieces.Length < MaximumNineSlicePieces)
            throw new ArgumentException("A nine-slice needs room for nine pieces.", nameof(pieces));
        PluginRect frame = source ?? new PluginRect(0, 0, imageWidth, imageHeight);
        if (imageWidth <= 0 || imageHeight <= 0
            || !IsFinite(frame) || !IsFinite(destination)
            || !(frame.Width > 0 && frame.Height > 0)
            || !(destination.Width > 0 && destination.Height > 0)
            || !IsValid(insets.Left) || !IsValid(insets.Top)
            || !IsValid(insets.Right) || !IsValid(insets.Bottom))
        {
            return 0;
        }

        if (!TryCut(frame, imageWidth, imageHeight, out double x0, out double y0, out double x1, out double y1))
            return 0;

        (double left, double right) = Fit(insets.Left, insets.Right, x1 - x0);
        (double top, double bottom) = Fit(insets.Top, insets.Bottom, y1 - y0);
        (double leftOut, double rightOut) = Fit(left, right, destination.Width);
        (double topOut, double bottomOut) = Fit(top, bottom, destination.Height);

        ReadOnlySpan<double> sourceXs = [x0, x0 + left, x1 - right, x1];
        ReadOnlySpan<double> sourceYs = [y0, y0 + top, y1 - bottom, y1];
        double destinationRight = destination.X + destination.Width;
        double destinationBottom = destination.Y + destination.Height;
        ReadOnlySpan<double> destinationXs =
            [destination.X, destination.X + leftOut, destinationRight - rightOut, destinationRight];
        ReadOnlySpan<double> destinationYs =
            [destination.Y, destination.Y + topOut, destinationBottom - bottomOut, destinationBottom];

        int count = 0;
        for (int row = 0; row < 3; row++)
        {
            if (!(sourceYs[row + 1] > sourceYs[row]) || !(destinationYs[row + 1] > destinationYs[row]))
                continue;
            // Only the frame's own outer edges are pulled in: a seam is
            // between two pieces of the same frame.
            Coordinates(
                sourceYs[row], sourceYs[row + 1], imageHeight,
                linearFiltered && sourceYs[row] == y0 && y0 > 0,
                linearFiltered && sourceYs[row + 1] == y1 && y1 < imageHeight,
                out float v0, out float v1);
            for (int column = 0; column < 3; column++)
            {
                if (row == 1 && column == 1 && !drawCenter)
                    continue;
                if (!(sourceXs[column + 1] > sourceXs[column])
                    || !(destinationXs[column + 1] > destinationXs[column]))
                {
                    continue;
                }
                Coordinates(
                    sourceXs[column], sourceXs[column + 1], imageWidth,
                    linearFiltered && sourceXs[column] == x0 && x0 > 0,
                    linearFiltered && sourceXs[column + 1] == x1 && x1 < imageWidth,
                    out float u0, out float u1);
                pieces[count++] = new CanvasImagePiece(
                    (float)destinationXs[column],
                    (float)destinationYs[row],
                    (float)(destinationXs[column + 1] - destinationXs[column]),
                    (float)(destinationYs[row + 1] - destinationYs[row]),
                    u0, v0, u1, v1);
            }
        }
        return count;
    }

    /// <summary>The rectangle cut to the image, as edges; false when nothing of it is on the image.</summary>
    private static bool TryCut(
        PluginRect rect, int imageWidth, int imageHeight,
        out double x0, out double y0, out double x1, out double y1)
    {
        x0 = Math.Max(rect.X, 0);
        y0 = Math.Max(rect.Y, 0);
        x1 = Math.Min(rect.X + rect.Width, imageWidth);
        y1 = Math.Min(rect.Y + rect.Height, imageHeight);
        return x1 > x0 && y1 > y0;
    }

    /// <summary>
    /// Texture coordinates for the pixels from <paramref name="low"/> to
    /// <paramref name="high"/> on an axis of <paramref name="size"/> pixels,
    /// each end pulled in by half a pixel when asked. A span too narrow for
    /// both pulls shares what it has between them, so both ends meet in its
    /// middle rather than cross.
    /// </summary>
    private static void Coordinates(
        double low, double high, int size, bool pullLow, bool pullHigh, out float t0, out float t1)
    {
        double pullIn = pullLow ? 0.5 : 0.0;
        double pullOut = pullHigh ? 0.5 : 0.0;
        double room = high - low;
        if (pullIn + pullOut > room)
        {
            double share = room / (pullIn + pullOut);
            pullIn *= share;
            pullOut *= share;
        }
        t0 = (float)((low + pullIn) / size);
        t1 = (float)((high - pullOut) / size);
    }

    /// <summary>Two lengths along one axis, scaled down together when they add up to more than it.</summary>
    private static (double First, double Second) Fit(double first, double second, double length)
    {
        double sum = first + second;
        if (sum <= length) return (first, second);
        double share = length / sum;
        return (first * share, second * share);
    }

    private static bool IsValid(double inset) => double.IsFinite(inset) && inset >= 0;

    private static bool IsFinite(PluginRect rect) =>
        double.IsFinite(rect.X) && double.IsFinite(rect.Y)
        && double.IsFinite(rect.Width) && double.IsFinite(rect.Height);
}
```


- [ ] **Step 4: Run them to see them pass**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)' && dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~CanvasImageRegionsTests"`
Expected: `0 Warning(s)`, `0 Error(s)`, `Passed!  - Failed:     0, Passed:    24`

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/CanvasImageRegions.cs tests/AcDream.App.Tests/UI/CanvasImageRegionsTests.cs
git commit -m "plugin canvas: region and nine-slice arithmetic" \
  -m "Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

---

### Task 4: The painter draws regions and nine-slices

**Goal:** `PluginPainter` implements the three members over `DrawSprite` and `DrawSpriteTransformed` with the arithmetic from Task 3 and the filter flag from Task 2.

**Files:**
- Modify: `src/AcDream.App/UI/Layout/PluginPainter.cs`
- Modify: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs` (class becomes `partial`)
- Test: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.Regions.cs`

**Acceptance Criteria:**
- [ ] A region of the 16² linear client art lands as one quad of texture 77 with UVs pulled in by 0.5/16; a region of the 32² nearest icon lands as one quad of texture 78, not pulled in
- [ ] An invalid image, an off-image source and a negative inset draw nothing; a region under a clip is clipped with its UVs
- [ ] Two nine-slices between two fills give runs `[0, 77, 0]`, the middle with (9 + 8) × 6 vertices, corners at inset size
- [ ] A transformed region whose source is half off the image, scaled 2× about the destination's middle, spans x 60–100 and y 10–50
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~PluginCanvasElementTests"` → `Passed!  - Failed:     0, Passed:    17`

**Steps:**

- [ ] **Step 1: Write the failing tests**

In `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs`, replace

```csharp
/// the guard, with a painter that dies with the call; shown as one quad
/// on the interface; taken down through the tree and the retirement queue.
/// </summary>
public sealed class PluginCanvasElementTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
```

with

```csharp
/// the guard, with a painter that dies with the call; shown as one quad
/// on the interface; taken down through the tree and the retirement queue.
/// </summary>
public sealed partial class PluginCanvasElementTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
```


Create `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.Regions.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// Image regions through a real paint: a region is one quad of the image's
/// texture with the region's coordinates, pulled in on linear art and not
/// on nearest icons; a nine-slice is up to nine quads in one run, in
/// painter's order with everything else; a transformed region turns about
/// the pivot the plugin gave even when its source was cut.
/// </summary>
public sealed partial class PluginCanvasElementTests
{
    /// <summary>The <paramref name="index"/>th quad of a sprite run as (x, y, w, h, u0, v0, u1, v1).</summary>
    private static CanvasImagePiece QuadOf(IReadOnlyList<float> verts, int index)
    {
        int first = index * 6 * TextRenderer.FloatsPerVertex;
        int second = first + TextRenderer.FloatsPerVertex;
        float x = verts[first], y = verts[first + 1], u0 = verts[first + 2], v0 = verts[first + 3];
        float right = verts[second], bottom = verts[second + 1], u1 = verts[second + 2], v1 = verts[second + 3];
        return new CanvasImagePiece(x, y, right - x, bottom - y, u0, v0, u1, v1);
    }

    private static (float X, float Y) VertexOf(IReadOnlyList<float> verts, int index) =>
        (verts[index * TextRenderer.FloatsPerVertex], verts[index * TextRenderer.FloatsPerVertex + 1]);

    [Fact]
    public void ARegionIsOneQuadOfTheImageWithItsCoordinatesPulledInOnLinearArtOnly()
    {
        var harness = new Harness();
        IPluginImages images = harness.Registry.ImagesFor(harness.Owner);
        PluginImage art = images.FromClientArt(1u);  // 16x16, linear
        PluginImage icon = images.FromSpellIcon(1u); // 32x32, nearest
        (PluginCanvasRegistration registration, _) = harness.Mount(Hud(), painter =>
        {
            painter.DrawImageRegion(art, new PluginRect(4, 4, 8, 8), new PluginRect(10, 10, 16, 16), PluginColor.White);
            painter.DrawImageRegion(icon, new PluginRect(8, 0, 8, 8), new PluginRect(40, 10, 16, 16), PluginColor.White);
        });

        harness.Frame();

        Assert.True(registration.IsAvailable);
        Assert.Equal(
            [FakeImageBackend.ArtTexture, FakeImageBackend.IconTexture],
            harness.SurfaceRuns.Select(run => run.Texture).ToArray());
        IReadOnlyList<(uint Texture, IReadOnlyList<float> Verts)> runs = harness.Surface.Renderer.DebugSpriteSegmentVerts;
        Assert.Equal(
            new CanvasImagePiece(10, 10, 16, 16, 4.5f / 16, 4.5f / 16, 11.5f / 16, 11.5f / 16),
            QuadOf(runs[0].Verts, 0));
        Assert.Equal(
            new CanvasImagePiece(40, 10, 16, 16, 0.25f, 0f, 0.5f, 0.25f),
            QuadOf(runs[1].Verts, 0));
    }

    [Fact]
    public void ARegionIsClippedLikeAnyImageAndAnInvalidOneDrawsNothing()
    {
        var harness = new Harness();
        PluginImage art = harness.Registry.ImagesFor(harness.Owner).FromClientArt(1u);
        (PluginCanvasRegistration registration, _) = harness.Mount(Hud(), painter =>
        {
            painter.DrawImageRegion(PluginImage.None, new PluginRect(0, 0, 8, 8), new PluginRect(0, 0, 8, 8), PluginColor.White);
            painter.DrawImageRegion(art, new PluginRect(32, 0, 8, 8), new PluginRect(0, 0, 8, 8), PluginColor.White);
            painter.DrawImageNineSlice(art, new PluginRect(0, 0, 40, 40), PluginInsets.Uniform(-1), PluginColor.White);
            painter.PushClip(new PluginRect(0, 0, 20, 100));
            // The whole image over 10..42; the clip keeps 10..20 of it.
            painter.DrawImageRegion(art, new PluginRect(0, 0, 16, 16), new PluginRect(10, 0, 32, 32), PluginColor.White);
            painter.PopClip();
        });

        harness.Frame();

        Assert.True(registration.IsAvailable);
        (uint texture, IReadOnlyList<float> verts) = Assert.Single(harness.Surface.Renderer.DebugSpriteSegmentVerts);
        Assert.Equal(FakeImageBackend.ArtTexture, texture);
        CanvasImagePiece clipped = QuadOf(verts, 0);
        Assert.Equal((10f, 0f, 10f, 32f), (clipped.X, clipped.Y, clipped.Width, clipped.Height));
        Assert.Equal((0f, 10f / 32), (clipped.U0, clipped.U1));
    }

    [Fact]
    public void ANineSliceIsItsPiecesInOneRunBetweenWhatWasDrawnBeforeAndAfter()
    {
        var harness = new Harness();
        PluginImage art = harness.Registry.ImagesFor(harness.Owner).FromClientArt(1u);
        (PluginCanvasRegistration registration, _) = harness.Mount(Hud(), painter =>
        {
            painter.FillRect(new PluginRect(0, 0, 200, 100), new PluginColor(0, 0, 0, 160));
            painter.DrawImageNineSlice(art, new PluginRect(10, 10, 100, 50), PluginInsets.Uniform(4), PluginColor.White);
            painter.DrawImageNineSlice(
                art, new PluginRect(120, 10, 40, 40), PluginInsets.Uniform(4), PluginColor.White, drawCenter: false);
            painter.FillRect(new PluginRect(0, 90, 200, 10), PluginColor.White);
        });

        harness.Frame();

        Assert.True(registration.IsAvailable);
        Assert.Equal(
            [0u, FakeImageBackend.ArtTexture, 0u],
            harness.SurfaceRuns.Select(run => run.Texture).ToArray());
        Assert.Equal((9 + 8) * 6, harness.SurfaceRuns[1].VertexCount);
        IReadOnlyList<float> verts = harness.Surface.Renderer.DebugSpriteSegmentVerts[1].Verts;
        // The whole image is the source: its outer edges are on the image's border, so nothing is pulled in.
        Assert.Equal(new CanvasImagePiece(10, 10, 4, 4, 0f, 0f, 0.25f, 0.25f), QuadOf(verts, 0));
        Assert.Equal(new CanvasImagePiece(14, 14, 92, 42, 0.25f, 0.25f, 0.75f, 0.75f), QuadOf(verts, 4));
        Assert.Equal(new CanvasImagePiece(106, 56, 4, 4, 0.75f, 0.75f, 1f, 1f), QuadOf(verts, 8));
    }

    [Fact]
    public void ATransformedRegionTurnsAboutThePluginsPivotEvenWhenItsSourceIsCut()
    {
        var harness = new Harness();
        PluginImage art = harness.Registry.ImagesFor(harness.Owner).FromClientArt(1u);
        (PluginCanvasRegistration registration, _) = harness.Mount(Hud(), painter =>
        {
            // The left half of the source is off the image, so the right half
            // of the destination (60..80) remains. Doubling about the
            // destination's middle (60, 30) puts it at 60..100 by 10..50.
            painter.DrawImageRegionTransformed(
                art, new PluginRect(-8, 0, 16, 16), new PluginRect(40, 20, 40, 20), PluginColor.White,
                rotationRadians: 0, pivot: new PluginPoint(20, 10), scaleX: 2, scaleY: 2);
        });

        harness.Frame();

        Assert.True(registration.IsAvailable);
        (uint texture, IReadOnlyList<float> verts) = Assert.Single(harness.Surface.Renderer.DebugSpriteSegmentVerts);
        Assert.Equal(FakeImageBackend.ArtTexture, texture);
        float[] xs = Enumerable.Range(0, verts.Count / TextRenderer.FloatsPerVertex).Select(i => VertexOf(verts, i).X).ToArray();
        float[] ys = Enumerable.Range(0, verts.Count / TextRenderer.FloatsPerVertex).Select(i => VertexOf(verts, i).Y).ToArray();
        Assert.Equal((60f, 100f), (xs.Min(), xs.Max()));
        Assert.Equal((10f, 50f), (ys.Min(), ys.Max()));
    }
}
```


- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release 2>&1 | grep -E 'Error\(s\)' && dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~PluginCanvasElementTests" 2>&1 | grep -E 'Passed!|Failed!'`
Expected: it builds (the members are the contract's defaults) and `Failed!  - Failed:     4, Passed:    13`: the default members draw nothing.

- [ ] **Step 3: Implement**

In `src/AcDream.App/UI/Layout/PluginPainter.cs`, replace

```csharp
            new Vector2((float)pivot.X, (float)pivot.Y));
    }

    public void PushClip(PluginRect rect) =>
        Context.PushClip((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);

```

with

```csharp
            new Vector2((float)pivot.X, (float)pivot.Y));
    }

    public void DrawImageRegion(PluginImage image, PluginRect source, PluginRect destination, PluginColor tint)
    {
        UiRenderContext context = Context;
        if (!TryResolve(image, out uint texture, out int width, out int height, out bool linear)) return;
        if (!CanvasImageRegions.TryMapRegion(width, height, linear, source, destination, out CanvasImagePiece piece))
            return;
        context.DrawSprite(
            texture,
            piece.X, piece.Y, piece.Width, piece.Height,
            piece.U0, piece.V0, piece.U1, piece.V1,
            ToVector(tint));
    }

    public void DrawImageRegionTransformed(
        PluginImage image,
        PluginRect source,
        PluginRect destination,
        PluginColor tint,
        double rotationRadians,
        PluginPoint pivot,
        double scaleX = 1.0,
        double scaleY = 1.0)
    {
        UiRenderContext context = Context;
        if (!TryResolve(image, out uint texture, out int width, out int height, out bool linear)) return;
        if (!CanvasImageRegions.TryMapRegion(width, height, linear, source, destination, out CanvasImagePiece piece))
            return;
        // The pivot is measured from the rectangle's corner; a cut source
        // moves that corner, so the pivot is measured from the new one to
        // stay where the plugin put it.
        context.DrawSpriteTransformed(
            texture,
            piece.X, piece.Y, piece.Width, piece.Height,
            piece.U0, piece.V0, piece.U1, piece.V1,
            ToVector(tint),
            (float)rotationRadians,
            new Vector2((float)scaleX, (float)scaleY),
            new Vector2((float)(destination.X + pivot.X) - piece.X, (float)(destination.Y + pivot.Y) - piece.Y));
    }

    public void DrawImageNineSlice(
        PluginImage image,
        PluginRect destination,
        PluginInsets insets,
        PluginColor tint,
        PluginRect? source = null,
        bool drawCenter = true)
    {
        UiRenderContext context = Context;
        if (!TryResolve(image, out uint texture, out int width, out int height, out bool linear)) return;
        Span<CanvasImagePiece> pieces = stackalloc CanvasImagePiece[CanvasImageRegions.MaximumNineSlicePieces];
        int count = CanvasImageRegions.NineSlice(width, height, linear, destination, insets, source, drawCenter, pieces);
        Vector4 color = ToVector(tint);
        foreach (CanvasImagePiece piece in pieces[..count])
        {
            context.DrawSprite(
                texture,
                piece.X, piece.Y, piece.Width, piece.Height,
                piece.U0, piece.V0, piece.U1, piece.V1,
                color);
        }
    }

    public void PushClip(PluginRect rect) =>
        Context.PushClip((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);

```

In `src/AcDream.App/UI/Layout/PluginPainter.cs`, replace

```csharp
        _context ?? throw new InvalidOperationException(
            "The painter is valid only for the duration of the paint callback it was handed to.");

    private bool TryResolve(PluginImage image, out uint texture)
    {
        texture = 0u;
        return _images is not null
            && _images.TryResolve(image, out texture, out _, out _)
            && texture != 0u;
    }

```

with

```csharp
        _context ?? throw new InvalidOperationException(
            "The painter is valid only for the duration of the paint callback it was handed to.");

    private bool TryResolve(PluginImage image, out uint texture) =>
        TryResolve(image, out texture, out _, out _, out _);

    private bool TryResolve(PluginImage image, out uint texture, out int width, out int height, out bool linear)
    {
        texture = 0u;
        width = 0;
        height = 0;
        linear = false;
        return _images is not null
            && _images.TryResolve(image, out texture, out width, out height, out linear)
            && texture != 0u;
    }

```


- [ ] **Step 4: Run them to see them pass**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)' && dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~PluginCanvasElementTests"`
Expected: `0 Warning(s)`, `0 Error(s)`, `Passed!  - Failed:     0, Passed:    17`

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/Layout/PluginPainter.cs tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs \
  tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.Regions.cs
git commit -m "plugin canvas: the painter draws image regions and nine-slices" \
  -m "Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

---

### Task 5: Documentation

**Goal:** The plugin API guide documents image regions and nine-slices under Canvases, and the markup guide's Tests section names the new suites.

**Files:**
- Modify: `docs/plugin-api.md`
- Modify: `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] `docs/plugin-api.md` has a `### Image regions` subsection directly before `### Pointer input`, covering the cut and shrink rule, nine-slice corners, shrink and insets, the half-pixel pull-in and which images it applies to, and that an older host draws nothing (with `minHostVersion`)
- [ ] `docs/plugin-ui-markup.md`'s Tests section has a sentence naming `UI/CanvasImageRegionsTests` and `PluginCanvasImageRegionsContractTests`
- [ ] Every member name in the new text exists in the contract

**Verify:** `grep -c 'DrawImageNineSlice\|DrawImageRegionTransformed\|PluginInsets' docs/plugin-api.md && grep -c 'CanvasImageRegionsTests' docs/plugin-ui-markup.md` → `4` (or more), `1`

**Steps:**

- [ ] **Step 1: Edit the guides**

In `docs/plugin-api.md`, replace

```markdown
Without a window the canvas is accepted, `IsAvailable` is false, the
state the plugin sets is kept, and the paint callback is never called.

### Pointer input

A canvas is click-through by default. One that wants to be dragged,
```

with

````markdown
Without a window the canvas is accepted, `IsAvailable` is false, the
state the plugin sets is kept, and the paint callback is never called.

### Image regions

Part of an image, such as one frame of a sprite sheet or one icon of an
atlas, is drawn with `DrawImageRegion`, and a panel or button background
that keeps its corners at any size with `DrawImageNineSlice`:

```csharp
// The third 16x16 frame of a strip, at twice its size:
painter.DrawImageRegion(sheet, new PluginRect(32, 0, 16, 16), new PluginRect(10, 10, 32, 32), PluginColor.White);

// The same frame, turned about its middle:
painter.DrawImageRegionTransformed(sheet, new PluginRect(32, 0, 16, 16), new PluginRect(50, 10, 32, 32),
    PluginColor.White, rotationRadians: heading, pivot: new PluginPoint(16, 16));

// A panel from a frame with 8-pixel corners, stretched over the canvas:
painter.DrawImageNineSlice(panel, new PluginRect(0, 0, painter.Width, painter.Height),
    PluginInsets.Uniform(8), PluginColor.White);
```

A source rectangle is in the image's own pixels. It is cut to the
image's bounds and the destination shrinks with it, so a source that runs
off the image draws only the part on it, where that part would have been.
An empty source, or one wholly off the image, draws nothing.

A nine-slice draws its four corners at the size of the insets, stretches
the edges along the frame and the middle both ways; nothing is tiled.
Insets are in the image's pixels. When the destination is narrower or
shorter than its two corners, those corners shrink to fit in proportion.
Insets that add up to more than the source are scaled down to fit it, and
then there is no middle to stretch. The optional `source` picks the frame
out of a sheet (cut to the image's bounds; the destination is not moved),
and `drawCenter: false` draws only the frame around the middle. Negative
or non-finite insets draw nothing.

The client's art and the plugin's own images are smoothed when stretched,
which would blend in the pixels just outside a region: the neighbouring
frame on a sheet. So a region's edges that lie inside the image are
pulled in by half a pixel, and frames need no padding between them. An
edge on the image's own border is drawn exactly as `DrawImage` draws it,
and the seams between a nine-slice's pieces are left alone, since what
lies across them is the same frame. Spell and object icons are drawn with
hard pixels, as the client draws them, and are not pulled in.

On a host that predates image regions these calls draw nothing, rather
than the whole sheet. A plugin that needs them declares a
`minHostVersion` (see the [manifest guide](plugin-manifest.md)) of a
client that has them.

### Pointer input

A canvas is click-through by default. One that wants to be dragged,
````


In `docs/plugin-ui-markup.md`, replace

```markdown
under `tests/AcDream.App.Tests/`, by `ScopedUiRegistryImagesTests` and
`ScopedUiRegistryCanvasTests` for the scoped forwarder, and by the contract
and headless suites for the inert answers a host without a window gives.

## Scrolling transcripts

```

with

```markdown
under `tests/AcDream.App.Tests/`, by `ScopedUiRegistryImagesTests` and
`ScopedUiRegistryCanvasTests` for the scoped forwarder, and by the contract
and headless suites for the inert answers a host without a window gives.
Image regions and nine-slices are covered by `UI/CanvasImageRegionsTests`,
the region cases in `UI/Layout/PluginCanvasElementTests` and the filtering
cases in `PluginImageTableTests` and
`Rendering/TextureCacheReleasableUiTextureTests` under
`tests/AcDream.App.Tests/`, and by `PluginCanvasImageRegionsContractTests`
under `tests/AcDream.Plugin.Tests/` for the inert answers of a host that
predates them.

## Scrolling transcripts

```


- [ ] **Step 2: Commit**

```bash
git add docs/plugin-api.md docs/plugin-ui-markup.md
git commit -m "docs: image regions and nine-slice on canvases" \
  -m "Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

---

### Task 6: Full verification and push

**Goal:** The branch builds cleanly, the full portable suite fails only baseline tests, and the branch is on `origin`.

**Files:** none

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`
- [ ] Every portable-suite failure (with `TMPDIR=/tmp/`) is in `/tmp/openac-regions/baseline-failures.txt` or is a HostParity `Peer*ParityTests`
- [ ] `git log --oneline upstream/main..HEAD` lists exactly the five commits of Tasks 1–5
- [ ] `git diff --name-only upstream/main` lists no `docs/superpowers/` path and no `packages.*.lock.json`
- [ ] `origin/painter-v2/image-regions` equals `HEAD`

**Verify:** `git fetch origin && test "$(git rev-parse HEAD)" = "$(git rev-parse origin/painter-v2/image-regions)" && echo pushed` → `pushed`

**Steps:**

- [ ] **Step 1: Build and run the portable suite** (outside the command sandbox)

```bash
dotnet build AcDream.slnx -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'
TMPDIR=/tmp/ dotnet test AcDream.slnx -c Release --no-build \
  --filter 'Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure' \
  2>&1 | grep -E '^\s+Failed ' | sed -E 's/^[[:space:]]+Failed ([^ ]+).*/\1/' | sort > /tmp/openac-regions/branch-failures.txt
comm -13 /tmp/openac-regions/baseline-failures.txt /tmp/openac-regions/branch-failures.txt | grep -v 'HostParity.Tests.Peer' || echo "no new failures"
```

Expected: `0 Warning(s)`, `0 Error(s)`, `no new failures`. A new failure outside HostParity is this branch's to fix before going on.

- [ ] **Step 2: Check what the branch carries, then push**

```bash
git checkout -- '*.lock.json'
git log --oneline upstream/main..HEAD
git diff --name-only upstream/main | grep -E 'docs/superpowers|packages\..*lock\.json' || echo clean
git push -u origin painter-v2/image-regions
```

Expected: five commits, `clean`, and the push succeeds.

---

### Task 7: Merge into fork main

**Goal:** Fork `main` carries PR 2 as a merge commit, with the four known conflicts resolved, building cleanly and passing the suites.

**Files:** (conflicts only)
- `docs/plugin-api.md`, `docs/plugin-ui-markup.md`, `src/AcDream.App/UI/Layout/PluginPainter.cs`, `tests/AcDream.App.Tests/Rendering/TextureCacheReleasableUiTextureTests.cs`

**Acceptance Criteria:**
- [ ] The merge commit's parents are fork `main` (`84ada220` or later) and `painter-v2/image-regions`
- [ ] No conflict markers remain; `### Image regions` sits directly before `### Shapes` in `docs/plugin-api.md`
- [ ] `PluginPainter` has the font `TryResolve` and both image `TryResolve` overloads
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`
- [ ] The canvas suites pass, the `Lane=Vulkan` canvas tests pass, and the portable suite fails only HostParity peer tests
- [ ] Fork `main` is pushed only after the user says so

**Verify:** `git log -1 --format=%P main | wc -w && ! git grep -n '^<<<<<<< ' -- docs src tests` → `2`

**Steps:**

- [ ] **Step 1: Merge** (in the main checkout's fork `main`, or a worktree on it)

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git fetch origin
git worktree add .worktrees/main-merge main 2>/dev/null || true   # skip if main is already checked out somewhere; use that checkout
cd .worktrees/main-merge
git merge --ff-only origin/main
git merge --no-ff --no-commit painter-v2/image-regions
git diff --name-only --diff-filter=U
```

Expected: exactly the four files listed above.

- [ ] **Step 2: Resolve**

Each file has one conflict hunk. In each, "ours" is fork `main` and "theirs" is PR 2:

- `docs/plugin-api.md`: fork `main` added `### Shapes`, `### High-density displays` and `### Layers and order` where PR 2 added `### Image regions`. Keep both: PR 2's section first, then fork `main`'s three.
- `docs/plugin-ui-markup.md`: both appended sentences to the Tests paragraph. Keep both: fork `main`'s first, PR 2's image-regions sentence last.
- `src/AcDream.App/UI/Layout/PluginPainter.cs`: fork `main` added a `TryResolve(PluginFont, …)` above the image `TryResolve`; PR 2 made the image one a two-overload pair. Keep fork `main`'s font overload, then PR 2's two image overloads; the shared body below the hunk is the five-argument overload's.
- `tests/AcDream.App.Tests/Rendering/TextureCacheReleasableUiTextureTests.cs`: both appended tests after the last shared one. Keep both, fork `main`'s two coverage tests first.

This script applies exactly those resolutions:

```bash
cat > /tmp/openac-regions/resolve.py <<'EOF'
import re
ORDER = {
    'docs/plugin-api.md': 'theirs-ours',
    'docs/plugin-ui-markup.md': 'ours-theirs',
    'src/AcDream.App/UI/Layout/PluginPainter.cs': 'painter',
    'tests/AcDream.App.Tests/Rendering/TextureCacheReleasableUiTextureTests.cs': 'tests',
}
pat = re.compile(r'<<<<<<< [^\n]*\n(.*?)=======\n(.*?)>>>>>>> [^\n]*\n', re.S)
for path, how in ORDER.items():
    s = open(path).read()
    hunks = pat.findall(s)
    assert len(hunks) == 1, (path, len(hunks))
    ours, theirs = hunks[0]
    if how == 'theirs-ours':
        merged = theirs + ours
    elif how == 'ours-theirs':
        merged = ours + theirs
    elif how == 'painter':
        font = ours[:ours.index('    private bool TryResolve(PluginImage image, out uint texture)')]
        merged = font + theirs
    else:
        merged = ours + '    }\n\n    [Fact]\n' + theirs
    s = pat.sub(lambda m: merged, s, count=1)
    assert '<<<<<<<' not in s and '>>>>>>>' not in s
    open(path, 'w').write(s)
    print('resolved', path)
EOF
python3 /tmp/openac-regions/resolve.py
git grep -n '^<<<<<<< \|^>>>>>>> ' -- docs src tests || echo "no markers"
grep -n '^### ' docs/plugin-api.md | sed -n '/Image regions/,+1p'
```

Expected: four `resolved` lines, `no markers`, and `### Image regions` followed by `### Shapes`. If the script's assertion fails (fork `main` has moved since `84ada220`), resolve by hand following the four rules above.

- [ ] **Step 2b: Pass PR 4's pixel scale to the region arithmetic** (added after the final review)

The final review made the pull-in depend on device pixels, and the branch's painter passes a constant `DevicePixelsPerPixel = 1.0`. Fork `main`'s painter has PR 4's `PixelScale` (device pixels per canvas pixel for this paint). In `src/AcDream.App/UI/Layout/PluginPainter.cs`, replace the line `private const double DevicePixelsPerPixel = 1.0;` (and its comment, which names this step) with a property that answers `PixelScale`, for example `private double DevicePixelsPerPixel => PixelScale;`, keeping a one-line comment that it is the pixel scale of the current paint. Then add an element test in `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.Regions.cs` using the high-density harness from fork `main`'s element tests (a fake pixel scale of 2): a linear inner region drawn at its own size in canvas pixels is a 2× device magnification and is pulled by `0.5 * (m - n) / (m - 1)` with m = 2n, not 0.

- [ ] **Step 3: Build and test the merged tree** (outside the command sandbox)

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet build AcDream.slnx -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'
TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --no-build \
  --filter 'Lane!=Vulkan&(FullyQualifiedName~CanvasImageRegions|FullyQualifiedName~PluginImageTable|FullyQualifiedName~TextureCacheReleasable|FullyQualifiedName~PluginCanvas|FullyQualifiedName~BufferedUiRegistryImages|FullyQualifiedName~CanvasGeometry|FullyQualifiedName~CanvasFont)' \
  2>&1 | grep -E '^\s+Failed |Passed!|Failed!'
TMPDIR=/tmp/ dotnet test tests/AcDream.Plugin.Tests -c Release --no-build 2>&1 | grep -E '^\s+Failed |Passed!|Failed!'
DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib TMPDIR=/tmp/ dotnet test tests/AcDream.App.Tests -c Release --no-build \
  --filter 'Lane=Vulkan&FullyQualifiedName~PluginCanvas' 2>&1 | grep -E '^\s+Failed |Passed!|Failed!'
TMPDIR=/tmp/ dotnet test AcDream.slnx -c Release --no-build \
  --filter 'Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure' \
  2>&1 | grep -E '^\s+Failed |Failed!'
```

Expected (as measured on the scratch merge): `0 Warning(s)`, `0 Error(s)`; the canvas suites `Passed!  - Failed:     0, Passed:   223`; `AcDream.Plugin.Tests` `Passed!  - Failed:     0, Passed:    91`; the `Lane=Vulkan` canvas tests `Passed!  - Failed:     0, Passed:     5`; and in the portable suite only HostParity `Peer*ParityTests` fail (7 on the scratch merge, inside the 2–7 range). Without `DYLD_FALLBACK_LIBRARY_PATH` the five Vulkan canvas tests fail to load the Vulkan loader; that is the environment, not the merge.

- [ ] **Step 4: Commit the merge**

```bash
git checkout -- '*.lock.json'
git add docs/plugin-api.md docs/plugin-ui-markup.md src/AcDream.App/UI/Layout/PluginPainter.cs \
  tests/AcDream.App.Tests/Rendering/TextureCacheReleasableUiTextureTests.cs
git commit -m "Merge painter-v2/image-regions: plugin canvas image regions and nine-slice (PR 2)" \
  -m "Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
git log -1 --format=%P | wc -w
```

Expected: `2`.

- [ ] **Step 5: Ask, then push**

Ask the user whether to push fork `main`. On yes:

```bash
git push origin main
```

Then remove the merge worktree if Step 1 created it: `cd /Users/davidsmith/code/OpenAC/OpenAC && git worktree remove .worktrees/main-merge`.
