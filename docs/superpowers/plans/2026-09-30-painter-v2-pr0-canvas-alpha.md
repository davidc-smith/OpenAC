# Painter v2 — PR 0: Canvas alpha compositing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make translucent plugin-canvas content composite at the alpha it was painted with (a 50 % fill shows at 50 %, not 25 %).

**Architecture:** Canvas repaints draw straight-alpha sources into an off-screen target with a new blend mode whose alpha channel uses `One, OneMinusSrcAlpha`, so the target ends up holding premultiplied colour and true coverage. The interface then composites the canvas quad with the existing `PremultipliedAlpha` mode through a second, lazily created `TextRenderer` pipeline. No shader changes.

**Tech Stack:** C# / .NET 10, Vulkan 1.3 via Silk.NET, xUnit, the repository's `RecordingGpuDevice` fake, MoltenVK for the local `Lane=Vulkan` run.

**Spec:** `docs/superpowers/specs/2026-09-30-plugin-painter-v2-design.md` (section 3, and "Plan-time corrections").

## Global Constraints

- Branch `painter-v2/canvas-alpha`, created from `main` (never from `docs/painter-v2-spec`; nothing under `docs/superpowers/` goes on a code branch).
- Commit subjects follow the repo's style: `gpu: …`, `plugin canvas: …`, `tests: …`. Every commit message ends with the line `Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB`.
- Warnings are errors. No shader source or SPIR-V changes (the shader manifest and `RetailOracleSpirvSha256` must stay untouched).
- `TextRenderer` construction still creates exactly one pipeline (`ResourceCleanupGroupTests.TextRendererConstructionCreatesAndDisposesOnlyOnePipeline` must keep passing unchanged).
- Existing `GpuBlendMode` values keep identical colour *and* alpha factors.
- Environment for every command: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` (the pinned 10.0.300 SDK). `Lane=Vulkan` commands also need `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib`.
- Portable gate filter (from CONTRIBUTING.md): `Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure`.

**User decisions (already made):**
- "One spec, a PR series" — this is PR 0 of the series; it lands on its own.
- "Keep it on our repo only" — specs and plans stay on the fork's docs branch; code branches start from `main`.
- AA approach "CPU feathered geometry" — which is why this blend fix comes first.
- Branches are pushed to `origin`; the PR's target repository is decided by the user when it is opened (do not open a PR without asking).

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `tests/AcDream.App.Tests/Rendering/Gpu/Vk/HeadlessVulkanTestHost.cs` | Create | Shared headless Vulkan device for `Lane=Vulkan` tests (moved from `MeshModernSharedIndexOffscreenTests`). |
| `tests/AcDream.App.Tests/Rendering/Gpu/Vk/VulkanImageReadback.cs` | Create | Shared colour-image readback (moved, parameterised by size). |
| `tests/AcDream.App.Tests/Rendering/Gpu/Vk/MeshModernSharedIndexOffscreenTests*.cs` (3 files) | Modify | Use the shared host and readback; the source-inspecting test reads the new file. |
| `src/AcDream.App/Rendering/Gpu/GpuEnums.cs` | Modify | New `GpuBlendMode.StraightAlphaIntoPremultiplied`. |
| `src/AcDream.App/Rendering/Gpu/Vk/VulkanViewportMapping.cs` | Modify | Map the new mode; new `AlphaBlendFactorsOf`. |
| `src/AcDream.App/Rendering/Gpu/Vk/VulkanGpuPipeline.cs` | Modify | Alpha blend factors from `AlphaBlendFactorsOf`. |
| `src/AcDream.App/Rendering/TextRenderer.cs` | Modify | Blend choice at construction; premultiplied sprite runs; lazy composite pipeline. |
| `src/AcDream.App/UI/UiRenderContext.cs` | Modify | `DrawSpritePremultiplied`. |
| `src/AcDream.App/UI/Layout/PluginCanvasSurface.cs` | Modify | Surface renderer paints into premultiplied. |
| `src/AcDream.App/UI/Layout/PluginCanvasElement.cs` | Modify | Canvas quad drawn premultiplied. |
| `tests/AcDream.App.Tests/Rendering/Gpu/Vk/VulkanViewportMappingTests.cs` | Modify | Alpha-factor test. |
| `tests/AcDream.App.Tests/Rendering/TextRendererBlendTests.cs` | Create | Renderer blend/pipeline tests on the recording device. |
| `tests/AcDream.App.Tests/Rendering/Gpu/Vk/PluginCanvasCompositeOffscreenTests.cs` | Create | `Lane=Vulkan` pixel proof. |
| `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs` | Modify | Canvas uses the new pipelines; window alpha still fades it. |

---

### Task 0: Create the branch

**Goal:** A clean code branch from `main`.

**Files:** none

**Acceptance Criteria:**
- [ ] `git branch --show-current` prints `painter-v2/canvas-alpha`
- [ ] `git log -1 --format=%H main` equals `git merge-base HEAD main`
- [ ] `docs/superpowers` does not exist in the working tree

**Verify:** `git branch --show-current && test ! -e docs/superpowers && echo clean`

**Steps:**

- [ ] **Step 1: Branch from main**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git switch main
git pull --ff-only origin main
git switch -c painter-v2/canvas-alpha
test ! -e docs/superpowers && echo clean
```

Expected: `clean`.

---

### Task 1: Share the headless Vulkan host and readback between test classes

**Goal:** Move the private headless Vulkan host and readback out of `MeshModernSharedIndexOffscreenTests` into two internal test helpers so a new `Lane=Vulkan` test can use them, with no behaviour change.

**Files:**
- Create: `tests/AcDream.App.Tests/Rendering/Gpu/Vk/HeadlessVulkanTestHost.cs`
- Create: `tests/AcDream.App.Tests/Rendering/Gpu/Vk/VulkanImageReadback.cs`
- Modify: `tests/AcDream.App.Tests/Rendering/Gpu/Vk/MeshModernSharedIndexOffscreenTests.cs` (remove `ReadBack` at ~212-346, `CreateHostReadBarrier` at ~348-364, `HeadlessVulkanHost` at ~402-515, `RepositoryRoot` at ~517-524; update call sites and the source-inspecting test at ~40-77)
- Modify: `tests/AcDream.App.Tests/Rendering/Gpu/Vk/MeshModernSharedIndexOffscreenTests.AtmosphericLighting.cs` (call sites)
- Modify: `tests/AcDream.App.Tests/Rendering/Gpu/Vk/MeshModernSharedIndexOffscreenTests.EnvCellLighting.cs` (call sites)

**Acceptance Criteria:**
- [ ] The three `MeshModernSharedIndexOffscreenTests` Vulkan tests pass locally under MoltenVK, as before the move
- [ ] `ReadbackDependency_PublishesTheExactCopiedRangeBeforeEndAndSubmit` passes and inspects `VulkanImageReadback.cs`
- [ ] No test file other than the three above and the two new helpers changes

**Verify:** `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~MeshModernSharedIndexOffscreenTests"` → `Passed!` with 0 failed

**Steps:**

- [ ] **Step 1: Record the baseline**

Run: `dotnet build AcDream.slnx -c Release && DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~MeshModernSharedIndexOffscreenTests"`
Expected: all pass. Note the count (N).

- [ ] **Step 2: Create `VulkanImageReadback.cs`**

Move the bodies of `ReadBack` and `CreateHostReadBarrier` verbatim from `MeshModernSharedIndexOffscreenTests.cs`, changing only: the class they live in, `private` → `internal`, and `Extent` → the new `width`/`height` parameters (in `byteCount` and `ImageExtent`). The source-inspecting test depends on the statement text inside `ReadBack`, so keep every statement byte-identical otherwise.

```csharp
using AcDream.App.Rendering.Gpu.Vk;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace AcDream.App.Tests.Rendering.Gpu.Vk;

/// <summary>
/// Copies a colour image the device has finished with into host memory, for
/// the offscreen proofs in the Vulkan lane. The image is expected in
/// shader-read layout, which is where a render target is left after its pass.
/// </summary>
internal static unsafe class VulkanImageReadback
{
    internal static byte[] ReadBack(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDevice,
        Device device,
        Queue queue,
        uint queueFamily,
        Image image,
        int width,
        int height)
    {
        uint byteCount = (uint)width * (uint)height * 4u;
        // ... the moved body, unchanged except:
        //     ImageExtent = new Extent3D((uint)width, (uint)height, 1),
    }

    internal static BufferMemoryBarrier2 CreateHostReadBarrier(Buffer readback, ulong byteCount)
    {
        // ... the moved body, unchanged.
    }
}
```

(The two `// ...` lines stand for the moved code; paste it in full — it is existing code, not new code.)

- [ ] **Step 3: Create `HeadlessVulkanTestHost.cs`**

Move the nested `HeadlessVulkanHost` class verbatim, renamed `HeadlessVulkanTestHost`, made `internal sealed` at namespace level, with its `Create` factory and `Dispose` unchanged. Add three members:

```csharp
    /// <summary>The committed SPIR-V directory the production pipelines load from.</summary>
    internal static string CommittedShaderDirectory() =>
        Path.Combine(RepositoryRoot(), "src", "AcDream.App", "Rendering", "Shaders", "spv");

    /// <summary>The repository root, found by walking up to <c>AcDream.slnx</c>.</summary>
    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcDream.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    /// <summary>Reads a render target's colour back once the device is idle.</summary>
    internal byte[] ReadBack(IGpuRenderTarget target, int width, int height) =>
        VulkanImageReadback.ReadBack(
            Vk,
            PhysicalDevice,
            LogicalDevice,
            Queue,
            QueueFamily,
            ((VulkanGpuRenderTarget)target).ColorResult.Image,
            width,
            height);
```

Usings: `AcDream.App.Rendering.Gpu`, `AcDream.App.Rendering.Gpu.Vk`, `Silk.NET.Vulkan`.

- [ ] **Step 4: Point the mesh tests at the helpers**

In the three `MeshModernSharedIndexOffscreenTests*.cs` files:
- delete the moved members from `MeshModernSharedIndexOffscreenTests.cs`;
- replace `HeadlessVulkanHost` with `HeadlessVulkanTestHost` (type and `.Create`);
- replace `RepositoryRoot()` with `HeadlessVulkanTestHost.RepositoryRoot()`;
- replace each `ReadBack(vk, physicalDevice, logicalDevice, queue, queueFamily, image)` call with `VulkanImageReadback.ReadBack(vk, physicalDevice, logicalDevice, queue, queueFamily, image, Extent, Extent)` (same arguments, plus the two sizes);
- replace `CreateHostReadBarrier(` with `VulkanImageReadback.CreateHostReadBarrier(` in the source-inspecting test.

In `ReadbackDependency_PublishesTheExactCopiedRangeBeforeEndAndSubmit`, change the file it reads and its two markers:

```csharp
        string source = File.ReadAllText(Path.Combine(
            HeadlessVulkanTestHost.RepositoryRoot(), "tests", "AcDream.App.Tests", "Rendering", "Gpu", "Vk",
            "VulkanImageReadback.cs"));
        int readbackStart = source.LastIndexOf("internal static byte[] ReadBack(", StringComparison.Ordinal);
        int readbackEnd = source.LastIndexOf("internal static BufferMemoryBarrier2 CreateHostReadBarrier(", StringComparison.Ordinal);
```

`CountPixels` stays in the mesh test (only it uses it).

- [ ] **Step 5: Build and run**

Run: `dotnet build AcDream.slnx -c Release && DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~MeshModernSharedIndexOffscreenTests"`
Expected: 0 warnings, the same N tests pass.

- [ ] **Step 6: Commit**

```bash
git add tests/AcDream.App.Tests/Rendering/Gpu/Vk/
git commit -m "tests: share the headless Vulkan host and image readback

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["tests/AcDream.App.Tests/Rendering/Gpu/Vk/HeadlessVulkanTestHost.cs", "tests/AcDream.App.Tests/Rendering/Gpu/Vk/VulkanImageReadback.cs", "tests/AcDream.App.Tests/Rendering/Gpu/Vk/MeshModernSharedIndexOffscreenTests.cs", "tests/AcDream.App.Tests/Rendering/Gpu/Vk/MeshModernSharedIndexOffscreenTests.AtmosphericLighting.cs", "tests/AcDream.App.Tests/Rendering/Gpu/Vk/MeshModernSharedIndexOffscreenTests.EnvCellLighting.cs"], "verifyCommand": "DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter FullyQualifiedName~MeshModernSharedIndexOffscreenTests", "acceptanceCriteria": ["Mesh Vulkan tests pass as before", "source-inspecting test reads VulkanImageReadback.cs", "only the listed files change"], "modelTier": "mechanical"}
```

---

### Task 2: A blend mode that paints into a premultiplied target

**Goal:** `GpuBlendMode.StraightAlphaIntoPremultiplied` with colour `SrcAlpha, OneMinusSrcAlpha` and alpha `One, OneMinusSrcAlpha`, honoured by the Vulkan pipeline.

**Files:**
- Modify: `src/AcDream.App/Rendering/Gpu/GpuEnums.cs:87-106`
- Modify: `src/AcDream.App/Rendering/Gpu/Vk/VulkanViewportMapping.cs:97-107`
- Modify: `src/AcDream.App/Rendering/Gpu/Vk/VulkanGpuPipeline.cs:169-179`
- Test: `tests/AcDream.App.Tests/Rendering/Gpu/Vk/VulkanViewportMappingTests.cs`

**Acceptance Criteria:**
- [ ] `AlphaBlendFactorsOf(mode) == BlendFactorsOf(mode)` for every existing mode
- [ ] The new mode maps to colour `(SrcAlpha, OneMinusSrcAlpha)` and alpha `(One, OneMinusSrcAlpha)`
- [ ] The pipeline's `SrcAlphaBlendFactor`/`DstAlphaBlendFactor` come from `AlphaBlendFactorsOf`

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~VulkanViewportMappingTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing test** (append to `VulkanViewportMappingTests`)

```csharp
    [Fact]
    public void AlphaFactorsFollowColourFactorsExceptWhenPaintingIntoAPremultipliedTarget()
    {
        foreach (GpuBlendMode mode in Enum.GetValues<GpuBlendMode>())
        {
            if (mode == GpuBlendMode.StraightAlphaIntoPremultiplied)
                continue;
            Assert.Equal(
                VulkanViewportMapping.BlendFactorsOf(mode),
                VulkanViewportMapping.AlphaBlendFactorsOf(mode));
        }

        Assert.Equal(
            (BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha),
            VulkanViewportMapping.BlendFactorsOf(GpuBlendMode.StraightAlphaIntoPremultiplied));
        Assert.Equal(
            (BlendFactor.One, BlendFactor.OneMinusSrcAlpha),
            VulkanViewportMapping.AlphaBlendFactorsOf(GpuBlendMode.StraightAlphaIntoPremultiplied));
    }
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release`
Expected: compile error — `StraightAlphaIntoPremultiplied` / `AlphaBlendFactorsOf` do not exist.

- [ ] **Step 3: Add the mode** (last member of `GpuBlendMode`, so no existing value changes)

```csharp
    InverseAlpha,

    /// <summary>
    /// Straight-alpha sources painted into a target that holds premultiplied
    /// colour: colour <c>SrcAlpha, OneMinusSrcAlpha</c>, alpha
    /// <c>One, OneMinusSrcAlpha</c>. Over a target cleared to transparent the
    /// result is premultiplied colour with the true coverage in alpha, to be
    /// composited with <see cref="PremultipliedAlpha"/>. Straight alpha on both
    /// channels would store alpha squared.
    /// </summary>
    StraightAlphaIntoPremultiplied,
}
```

- [ ] **Step 4: Map it** (in `VulkanViewportMapping`)

Add to the `BlendFactorsOf` switch, before `GpuBlendMode.None`:

```csharp
        GpuBlendMode.StraightAlphaIntoPremultiplied => (BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha),
```

Add below `BlendFactorsOf`:

```csharp
    /// <summary>
    /// The factors for the alpha channel. The same as the colour factors for
    /// every mode except <see cref="GpuBlendMode.StraightAlphaIntoPremultiplied"/>,
    /// whose alpha accumulates coverage instead of squaring it.
    /// </summary>
    internal static (BlendFactor Source, BlendFactor Destination) AlphaBlendFactorsOf(GpuBlendMode blend) => blend switch
    {
        GpuBlendMode.StraightAlphaIntoPremultiplied => (BlendFactor.One, BlendFactor.OneMinusSrcAlpha),
        _ => BlendFactorsOf(blend),
    };
```

- [ ] **Step 5: Use it in the pipeline** (`VulkanGpuPipeline.cs`)

```csharp
            (BlendFactor source, BlendFactor destination) =
                VulkanViewportMapping.BlendFactorsOf(description.Blend);
            (BlendFactor alphaSource, BlendFactor alphaDestination) =
                VulkanViewportMapping.AlphaBlendFactorsOf(description.Blend);
            var attachment = new PipelineColorBlendAttachmentState
            {
                BlendEnable = description.Blend != GpuBlendMode.None,
                SrcColorBlendFactor = source,
                DstColorBlendFactor = destination,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = alphaSource,
                DstAlphaBlendFactor = alphaDestination,
                AlphaBlendOp = BlendOp.Add,
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~VulkanViewportMappingTests"`
Expected: `Passed!`, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.App/Rendering/Gpu/GpuEnums.cs src/AcDream.App/Rendering/Gpu/Vk/VulkanViewportMapping.cs src/AcDream.App/Rendering/Gpu/Vk/VulkanGpuPipeline.cs tests/AcDream.App.Tests/Rendering/Gpu/Vk/VulkanViewportMappingTests.cs
git commit -m "gpu: a blend mode that paints straight alpha into a premultiplied target

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Rendering/Gpu/GpuEnums.cs", "src/AcDream.App/Rendering/Gpu/Vk/VulkanViewportMapping.cs", "src/AcDream.App/Rendering/Gpu/Vk/VulkanGpuPipeline.cs", "tests/AcDream.App.Tests/Rendering/Gpu/Vk/VulkanViewportMappingTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter FullyQualifiedName~VulkanViewportMappingTests", "acceptanceCriteria": ["existing modes keep identical alpha factors", "new mode maps colour SrcAlpha/OneMinusSrcAlpha and alpha One/OneMinusSrcAlpha", "pipeline uses AlphaBlendFactorsOf"], "modelTier": "standard"}
```

---

### Task 3: TextRenderer — blend at construction, premultiplied sprite runs

**Goal:** `TextRenderer` can be built to paint into a premultiplied target, and any `TextRenderer` can draw premultiplied sprites through a composite pipeline it creates on first use and binds only for those runs.

**Files:**
- Modify: `src/AcDream.App/Rendering/TextRenderer.cs`
- Test: `tests/AcDream.App.Tests/Rendering/TextRendererBlendTests.cs` (create)

**Acceptance Criteria:**
- [ ] Default construction creates one pipeline named `ui-text` with `StraightAlpha` (existing cleanup test unchanged and green)
- [ ] Construction with `StraightAlphaIntoPremultiplied` creates one pipeline named `ui-text-into-premultiplied` with that blend
- [ ] Any other blend passed to the constructor throws `ArgumentOutOfRangeException`
- [ ] The first `DrawPremultipliedSprite` creates `ui-text-premultiplied` (`PremultipliedAlpha`); later calls do not create more
- [ ] A flush binds `ui-text`, then `ui-text-premultiplied` only around premultiplied runs, then `ui-text` again before later straight runs
- [ ] A premultiplied sprite's vertex colour is the tint with rgb multiplied by alpha
- [ ] A straight and a premultiplied sprite of the same texture never share a run
- [ ] `Dispose` disposes both pipelines

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~TextRendererBlendTests|FullyQualifiedName~ResourceCleanupGroupTests|FullyQualifiedName~TextRendererLinearTwinTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** (`tests/AcDream.App.Tests/Rendering/TextRendererBlendTests.cs`)

```csharp
using System.Linq;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// The interface renderer paints with straight alpha into the frame, or into
/// a canvas target that then holds premultiplied colour; a canvas is shown
/// through a premultiplied composite run whose pipeline is made on first use.
/// </summary>
public sealed class TextRendererBlendTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    private static readonly Vector2 Screen = new(64f, 64f);

    [Fact]
    public void ARendererForCanvasTargetsBuildsItsOnePipelineWithTheIntoPremultipliedBlend()
    {
        using var device = new RecordingGpuDevice();
        int before = device.CreatedPipelines.Count;

        using var renderer = new TextRenderer(
            device, new FrameSource(), "unused", GpuBlendMode.StraightAlphaIntoPremultiplied);

        RecordingGpuPipeline pipeline = Assert.Single(device.CreatedPipelines.Skip(before));
        Assert.Equal(TextRenderer.IntoPremultipliedPipelineName, pipeline.Description.Name);
        Assert.Equal(GpuBlendMode.StraightAlphaIntoPremultiplied, pipeline.Description.Blend);
    }

    [Theory]
    [InlineData(GpuBlendMode.None)]
    [InlineData(GpuBlendMode.PremultipliedAlpha)]
    [InlineData(GpuBlendMode.Additive)]
    public void ABlendTheInterfaceDoesNotPaintWithIsRefused(GpuBlendMode blend)
    {
        using var device = new RecordingGpuDevice();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TextRenderer(device, new FrameSource(), "unused", blend));
    }

    [Fact]
    public void TheCompositePipelineIsMadeOnFirstUseAndBoundOnlyAroundItsRun()
    {
        using var device = new RecordingGpuDevice();
        var frames = new FrameSource();
        using var renderer = new TextRenderer(device, frames, "unused");
        int afterConstruction = device.CreatedPipelines.Count;

        renderer.Begin(Screen);
        renderer.DrawFill(0f, 0f, 10f, 10f, Vector4.One);
        renderer.DrawPremultipliedSprite(5u, 0f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, Vector4.One);
        renderer.DrawPremultipliedSprite(5u, 10f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, Vector4.One);
        renderer.DrawFill(0f, 20f, 10f, 10f, Vector4.One);

        RecordingGpuPipeline composite = Assert.Single(device.CreatedPipelines.Skip(afterConstruction));
        Assert.Equal(TextRenderer.PremultipliedPipelineName, composite.Description.Name);
        Assert.Equal(GpuBlendMode.PremultipliedAlpha, composite.Description.Blend);

        device.Clear();
        using (IGpuFrame frame = device.BeginFrame())
        {
            frames.CurrentFrame = frame;
            renderer.Flush(null);
            frames.CurrentFrame = null;
        }

        Assert.Equal(
            [TextRenderer.PipelineName, TextRenderer.PremultipliedPipelineName, TextRenderer.PipelineName],
            device.OfKind<GpuRecordedPipelineBind>().Select(bind => bind.PipelineName).ToArray());
        Assert.Equal([false, true, false], renderer.DebugSpriteSegmentPremultiplied);
        Assert.Equal(afterConstruction + 1, device.CreatedPipelines.Count);
    }

    [Fact]
    public void APremultipliedSpriteCarriesItsTintPremultiplied()
    {
        using var device = new RecordingGpuDevice();
        using var renderer = new TextRenderer(device, new FrameSource(), "unused");
        renderer.Begin(Screen);

        renderer.DrawPremultipliedSprite(5u, 0f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, new Vector4(1f, 0.5f, 0.25f, 0.5f));

        (uint _, IReadOnlyList<float> verts) = Assert.Single(renderer.DebugSpriteSegmentVerts);
        Assert.Equal([0.5f, 0.25f, 0.125f, 0.5f], verts.Skip(4).Take(4).ToArray());
    }

    [Fact]
    public void StraightAndPremultipliedSpritesOfOneTextureStayInSeparateRuns()
    {
        using var device = new RecordingGpuDevice();
        using var renderer = new TextRenderer(device, new FrameSource(), "unused");
        renderer.Begin(Screen);

        renderer.DrawSprite(5u, 0f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, Vector4.One);
        renderer.DrawPremultipliedSprite(5u, 0f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, Vector4.One);

        Assert.Equal([5u, 5u], renderer.DebugSpriteSegments.Select(seg => seg.Texture).ToArray());
        Assert.Equal([false, true], renderer.DebugSpriteSegmentPremultiplied);
    }

    [Fact]
    public void DisposeDisposesTheCompositePipelineToo()
    {
        using var device = new RecordingGpuDevice();
        var renderer = new TextRenderer(device, new FrameSource(), "unused");
        renderer.Begin(Screen);
        renderer.DrawPremultipliedSprite(5u, 0f, 0f, 1f, 1f, 0f, 0f, 1f, 1f, Vector4.One);

        renderer.Dispose();

        Assert.All(
            device.CreatedPipelines.Where(pipeline => pipeline.Description.Name.StartsWith("ui-text", StringComparison.Ordinal)),
            pipeline => Assert.True(pipeline.IsDisposed));
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release`
Expected: compile errors — the constructor overload, `DrawPremultipliedSprite`, the name constants and `DebugSpriteSegmentPremultiplied` do not exist.

- [ ] **Step 3: Implement in `TextRenderer.cs`**

Fields and names (replace the `_pipeline` field and the `SpriteSeg` declaration):

```csharp
    /// <summary>The pipeline that paints the interface into the frame.</summary>
    internal const string PipelineName = "ui-text";

    /// <summary>The pipeline that paints a canvas into its premultiplied target.</summary>
    internal const string IntoPremultipliedPipelineName = "ui-text-into-premultiplied";

    /// <summary>The pipeline that composites a premultiplied texture, made on first use.</summary>
    internal const string PremultipliedPipelineName = "ui-text-premultiplied";

    private readonly IGpuDevice _device;
    private readonly ICurrentGpuFrameSource _frameSource;
    private readonly IGpuPipeline _pipeline;
    private IGpuPipeline? _premultipliedPipeline;

    private sealed class SpriteSeg
    {
        public uint Texture;
        public bool Premultiplied;
        public readonly List<float> Verts = new(256);
    }
```

Constructor (replaces the existing one):

```csharp
    /// <summary>
    /// A renderer that paints with <paramref name="blend"/>: straight alpha
    /// into the frame (the default), or
    /// <see cref="GpuBlendMode.StraightAlphaIntoPremultiplied"/> into a canvas
    /// target. Exactly one pipeline is made here; the composite pipeline for
    /// <see cref="DrawPremultipliedSprite"/> is made on first use.
    /// </summary>
    internal TextRenderer(
        IGpuDevice device,
        ICurrentGpuFrameSource frameSource,
        string shaderDir,
        GpuBlendMode blend = GpuBlendMode.StraightAlpha)
    {
        ArgumentNullException.ThrowIfNull(device);
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderDir);
        if (blend is not (GpuBlendMode.StraightAlpha or GpuBlendMode.StraightAlphaIntoPremultiplied))
        {
            throw new ArgumentOutOfRangeException(
                nameof(blend),
                blend,
                "The interface paints with straight alpha, into the frame or into a premultiplied target.");
        }
        _device = device;
        _pipeline = device.CreatePipeline(Describe(
            blend == GpuBlendMode.StraightAlpha ? PipelineName : IntoPremultipliedPipelineName,
            blend));
    }

    private static GpuPipelineDescription Describe(string name, GpuBlendMode blend) => new()
    {
        Name = name,
        Shaders = new GpuShaderSet("ui_text"),
        VertexLayout = SpriteVertexLayout,
        Topology = GpuPrimitiveTopology.TriangleList,
        Blend = blend,
        Depth = GpuDepthState.Disabled,
        Cull = GpuCullMode.None,
        AlphaToCoverage = false,
        ColorWrite = true,
        SampleCount = 1,
    };
```

Debug accessor (next to `DebugSpriteSegments`):

```csharp
    internal IReadOnlyList<bool> DebugSpriteSegmentPremultiplied
    {
        get
        {
            var result = new List<bool>(_segUsed);
            for (int i = 0; i < _segUsed; i++)
                result.Add(_spriteSegs[i].Premultiplied);
            return result;
        }
    }
```

The premultiplied draw (after `DrawSprite`):

```csharp
    /// <summary>
    /// Draws a texture whose colour is already multiplied by its alpha, such
    /// as a canvas target painted with
    /// <see cref="GpuBlendMode.StraightAlphaIntoPremultiplied"/>, composited
    /// with <see cref="GpuBlendMode.PremultipliedAlpha"/>. The tint is
    /// premultiplied here so a faded window still fades what it shows. The
    /// run never joins a straight-alpha run of the same texture. The
    /// pipeline is made on first use: most interfaces never show a canvas.
    /// </summary>
    internal void DrawPremultipliedSprite(uint texture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 tint)
    {
        _premultipliedPipeline ??= _device.CreatePipeline(
            Describe(PremultipliedPipelineName, GpuBlendMode.PremultipliedAlpha));
        SpriteSeg seg = OverlayMode
            ? NextSpriteSeg(_overlaySpriteSegs, ref _overlaySegUsed, texture, premultiplied: true)
            : NextSpriteSeg(_spriteSegs,        ref _segUsed,        texture, premultiplied: true);
        var premultipliedTint = new Vector4(tint.X * tint.W, tint.Y * tint.W, tint.Z * tint.W, tint.W);
        AppendQuad(seg.Verts, x, y, w, h, u0, v0, u1, v1, premultipliedTint);
    }
```

`NextSpriteSeg` (replace):

```csharp
    private static SpriteSeg NextSpriteSeg(List<SpriteSeg> segs, ref int used, uint texture, bool premultiplied = false)
    {
        if (used > 0 && segs[used - 1].Texture == texture && segs[used - 1].Premultiplied == premultiplied)
            return segs[used - 1];
        if (used < segs.Count)
        {
            var s = segs[used++];
            s.Texture = texture;
            s.Premultiplied = premultiplied;
            s.Verts.Clear();
            return s;
        }
        var ns = new SpriteSeg { Texture = texture, Premultiplied = premultiplied };
        segs.Add(ns);
        used++;
        return ns;
    }
```

`FlushInto`, `DrawLayer` and a bind helper (replace the existing two methods):

```csharp
    private void FlushInto(GpuPassDescription pass, string stageName, BitmapFont? font)
    {
        IGpuFrame frame = _frameSource.CurrentFrame
            ?? throw new InvalidOperationException(
                "TextRenderer.Flush requires an open IGpuFrame (see GpuDeviceFrameLifetime) — " +
                "the host must drive IGpuDevice.BeginFrame() before rendering the retained UI.");

        using IGpuPassEncoder encoder = frame.BeginPass(pass);
        using IDisposable? stage = AcDream.App.Diagnostics.GpuStageProfiler.Measure(
            encoder, stageName);
        encoder.BindPipeline(_pipeline);
        IGpuPipeline bound = _pipeline;

        DrawLayer(_spriteSegs, _segUsed, _rectBuf, _rectVerts, _textBuf, _textVerts, font, frame, encoder, ref bound);
        DrawLayer(_overlaySpriteSegs, _overlaySegUsed, _overlayRectBuf, _overlayRectVerts, _overlayTextBuf, _overlayTextVerts, font, frame, encoder, ref bound);
    }

    private void DrawLayer(
        List<SpriteSeg> spriteSegs, int segUsed,
        List<float> rectBuf, int rectVerts,
        List<float> textBuf, int textVerts, BitmapFont? font,
        IGpuFrame frame, IGpuPassEncoder encoder, ref IGpuPipeline bound)
    {
        for (int i = 0; i < segUsed; i++)
        {
            var seg = spriteSegs[i];
            if (seg.Verts.Count == 0) continue;
            Bind(encoder, seg.Premultiplied ? _premultipliedPipeline! : _pipeline, ref bound);
            SetTextures(encoder, colorHandle: seg.Texture, coverageHandle: UiTextureTableHandle.None);
            DrawRing(frame, encoder, seg.Verts);
        }

        if (rectVerts > 0 || (textVerts > 0 && font is not null))
            Bind(encoder, _pipeline, ref bound);

        if (rectVerts > 0)
        {
            SetTextures(encoder, UiTextureTableHandle.None, UiTextureTableHandle.None);
            DrawRing(frame, encoder, rectBuf);
        }

        if (textVerts > 0 && font is not null)
        {
            SetTextures(encoder, UiTextureTableHandle.None, coverageHandle: font.TextureId);
            DrawRing(frame, encoder, textBuf);
        }
    }

    private static void Bind(IGpuPassEncoder encoder, IGpuPipeline wanted, ref IGpuPipeline bound)
    {
        if (ReferenceEquals(wanted, bound)) return;
        encoder.BindPipeline(wanted);
        bound = wanted;
    }
```

`Dispose` (replace):

```csharp
    public void Dispose()
    {
        _pipeline.Dispose();
        _premultipliedPipeline?.Dispose();
    }
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~TextRendererBlendTests|FullyQualifiedName~ResourceCleanupGroupTests|FullyQualifiedName~TextRendererLinearTwinTests"`
Expected: `Passed!`, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/Rendering/TextRenderer.cs tests/AcDream.App.Tests/Rendering/TextRendererBlendTests.cs
git commit -m "gpu: the interface renderer paints into premultiplied targets and composites them

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Rendering/TextRenderer.cs", "tests/AcDream.App.Tests/Rendering/TextRendererBlendTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter \"FullyQualifiedName~TextRendererBlendTests|FullyQualifiedName~ResourceCleanupGroupTests|FullyQualifiedName~TextRendererLinearTwinTests\"", "acceptanceCriteria": ["default construction still makes one ui-text pipeline", "canvas construction makes one ui-text-into-premultiplied pipeline", "other blends refused", "composite pipeline lazy and single", "bind order ui-text/premultiplied/ui-text", "tint premultiplied", "separate runs per blend", "dispose disposes both"], "modelTier": "standard"}
```

---

### Task 4: Pixel proof in the Vulkan lane

**Goal:** On a real device, a 50 % white fill painted into a canvas and composited over opaque black reads back as 50 % grey, and the canvas target stores alpha 50 %, not 25 %.

**Files:**
- Test: `tests/AcDream.App.Tests/Rendering/Gpu/Vk/PluginCanvasCompositeOffscreenTests.cs` (create)

**Acceptance Criteria:**
- [ ] Canvas texel reads back `(128, 128, 128, 128)` ±2
- [ ] Composited texel reads back `(128, 128, 128, 255)` ±2
- [ ] Temporarily painting with `StraightAlpha` and compositing with `DrawSprite` reads back composite red ≈ 32 (the bug), proving the test discriminates; the change is reverted before commit
- [ ] The test carries `[Trait("Lane", "Vulkan")]`

**Verify:** `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~PluginCanvasCompositeOffscreenTests"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the test**

```csharp
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Rendering.Gpu.Vk;

namespace AcDream.App.Tests.Rendering.Gpu.Vk;

/// <summary>
/// A plugin canvas on a real device: what is painted translucent into the
/// canvas's target is shown at the alpha it was painted with.
/// </summary>
public sealed class PluginCanvasCompositeOffscreenTests
{
    private const int Extent = 8;

    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    [Trait("Lane", "Vulkan")]
    [Fact]
    public void AHalfTransparentFillOnACanvasShowsAtHalfNotAQuarter()
    {
        using var host = HeadlessVulkanTestHost.Create(HeadlessVulkanTestHost.CommittedShaderDirectory());
        VulkanGpuDevice device = host.Device;
        var frames = new FrameSource();
        using var canvasRenderer = new TextRenderer(
            device, frames, "unused", GpuBlendMode.StraightAlphaIntoPremultiplied);
        using var screenRenderer = new TextRenderer(device, frames, "unused");
        using IGpuRenderTarget canvas = device.CreateRenderTarget(new GpuRenderTargetDescription(
            "canvas-alpha-canvas", Extent, Extent, GpuTextureFormat.Rgba8UnormRenderTarget,
            DepthFormat: null, SampleCount: 1));
        using IGpuRenderTarget screen = device.CreateRenderTarget(new GpuRenderTargetDescription(
            "canvas-alpha-screen", Extent, Extent, GpuTextureFormat.Rgba8UnormRenderTarget,
            DepthFormat: null, SampleCount: 1));
        GpuTextureSlot slot = device.RegisterTexture(
            canvas.ColorTexture, device.CreateSampler(GpuSamplerDescription.WorldClamp));
        uint canvasHandle = UiTextureTableHandle.FromSlot(slot);
        var size = new Vector2(Extent, Extent);

        using (IGpuFrame frame = device.BeginFrame())
        {
            frames.CurrentFrame = frame;
            canvasRenderer.Begin(size);
            canvasRenderer.DrawFill(0f, 0f, Extent, Extent, new Vector4(1f, 1f, 1f, 0.5f));
            canvasRenderer.FlushTo(canvas, Vector4.Zero, null, "canvas-alpha-paint");

            screenRenderer.Begin(size);
            screenRenderer.DrawPremultipliedSprite(
                canvasHandle, 0f, 0f, Extent, Extent, 0f, 0f, 1f, 1f, Vector4.One);
            screenRenderer.FlushTo(screen, new Vector4(0f, 0f, 0f, 1f), null, "canvas-alpha-composite");
            frames.CurrentFrame = null;
        }
        device.WaitIdle();

        byte[] painted = host.ReadBack(canvas, Extent, Extent);
        byte[] shown = host.ReadBack(screen, Extent, Extent);
        device.ReleaseTextureSlot(slot);

        int centre = ((Extent / 2 * Extent) + Extent / 2) * 4;
        AssertNear([128, 128, 128, 128], painted.AsSpan(centre, 4));
        AssertNear([128, 128, 128, 255], shown.AsSpan(centre, 4));
    }

    private static void AssertNear(int[] expected, ReadOnlySpan<byte> actual)
    {
        for (int channel = 0; channel < 4; channel++)
        {
            Assert.True(
                Math.Abs(expected[channel] - actual[channel]) <= 2,
                $"channel {channel}: expected {expected[channel]}±2, read {actual[channel]} "
                + $"(pixel {actual[0]},{actual[1]},{actual[2]},{actual[3]})");
        }
    }
}
```

- [ ] **Step 2: Run it**

Run: `dotnet build AcDream.slnx -c Release && DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~PluginCanvasCompositeOffscreenTests"`
Expected: `Passed!`.

- [ ] **Step 3: Prove it discriminates, then revert**

Temporarily change the canvas renderer to `new TextRenderer(device, frames, "unused")` and the composite call to `screenRenderer.DrawSprite(...)` with the same arguments. Rebuild and rerun.
Expected: FAIL — the painted alpha reads ≈ 64 and the shown red reads ≈ 32. Record the two numbers for the PR description. Revert both changes (`git diff` shows only the new file as added), rebuild, rerun: `Passed!`.

- [ ] **Step 4: Commit**

```bash
git add tests/AcDream.App.Tests/Rendering/Gpu/Vk/PluginCanvasCompositeOffscreenTests.cs
git commit -m "tests: a translucent canvas fill composites at the alpha it was painted with

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["tests/AcDream.App.Tests/Rendering/Gpu/Vk/PluginCanvasCompositeOffscreenTests.cs"], "verifyCommand": "DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter FullyQualifiedName~PluginCanvasCompositeOffscreenTests", "acceptanceCriteria": ["canvas texel 128,128,128,128 +-2", "composite texel 128,128,128,255 +-2", "straight/straight variant reads ~64 alpha and ~32 red, then reverted", "Lane=Vulkan trait"], "modelTier": "standard"}
```

---

### Task 5: Canvases paint into premultiplied and are shown premultiplied

**Goal:** The plugin canvas surface paints with the new blend and the canvas element draws its quad through `DrawSpritePremultiplied`, with window alpha still applied.

**Files:**
- Modify: `src/AcDream.App/UI/UiRenderContext.cs` (add `DrawSpritePremultiplied` after `DrawSprite`, ~line 171)
- Modify: `src/AcDream.App/UI/Layout/PluginCanvasSurface.cs:71`
- Modify: `src/AcDream.App/UI/Layout/PluginCanvasElement.cs:309`
- Test: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs`

**Acceptance Criteria:**
- [ ] The surface's renderer pipeline is `ui-text-into-premultiplied`
- [ ] After a frame, the main renderer's single run is premultiplied and the `ui-text-premultiplied` pipeline is bound in the `ui-text` pass
- [ ] Under `PushAlpha(0.5f)` the canvas quad's vertex colour is `(0.5, 0.5, 0.5, 0.5)`
- [ ] All existing `PluginCanvasElementTests` and `PluginCanvasTeardownOrderTests` still pass

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~PluginCanvas"` → `Passed!`

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to `PluginCanvasElementTests`)

```csharp
    [Fact]
    public void TheCanvasIsPaintedIntoPremultipliedAndShownThroughTheCompositePipeline()
    {
        var harness = new Harness();
        harness.Mount(Hud(), painter =>
            painter.FillRect(new PluginRect(0, 0, 200, 100), new PluginColor(255, 255, 255, 128)));

        harness.Frame();

        Assert.Contains(
            harness.Device.CreatedPipelines,
            pipeline => pipeline.Description.Name == TextRenderer.IntoPremultipliedPipelineName
                && pipeline.Description.Blend == GpuBlendMode.StraightAlphaIntoPremultiplied);
        Assert.Equal([true], harness.MainRenderer.DebugSpriteSegmentPremultiplied);
        Assert.Contains(
            harness.Device.OfKind<GpuRecordedPipelineBind>(),
            bind => bind.PipelineName == TextRenderer.PremultipliedPipelineName);
    }

    [Fact]
    public void AFadedInterfaceStillFadesTheCanvas()
    {
        var harness = new Harness();
        (_, PluginCanvasElement element) = harness.Mount(Hud(), painter => painter.Clear(PluginColor.White));
        harness.Frame();

        harness.MainRenderer.Begin(new Vector2(800f, 600f));
        harness.MainContext.Begin(new Vector2(800f, 600f), null);
        harness.MainContext.PushAlpha(0.5f);
        harness.MainContext.DrawSpritePremultiplied(
            element.ShownTextureHandle, 0f, 0f, 200f, 100f, 0f, 0f, 1f, 1f, Vector4.One);

        (uint _, IReadOnlyList<float> verts) = Assert.Single(harness.MainRenderer.DebugSpriteSegmentVerts);
        Assert.Equal([0.5f, 0.5f, 0.5f, 0.5f], verts.Skip(4).Take(4).ToArray());
    }
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build tests/AcDream.App.Tests -c Release`
Expected: compile error — `UiRenderContext.DrawSpritePremultiplied` does not exist.

- [ ] **Step 3: Add `UiRenderContext.DrawSpritePremultiplied`** (after `DrawSprite`)

```csharp
    /// <summary>
    /// Draws a texture whose colour is already multiplied by its alpha, such
    /// as a plugin canvas's target. Translation, clipping and the alpha stack
    /// apply as they do for <see cref="DrawSprite"/>.
    /// </summary>
    internal void DrawSpritePremultiplied(uint texture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 tint)
    {
        x += _current.X;
        y += _current.Y;
        if (_clip is { } clip
            && !UiClipRect.TryClipSprite(
                clip, ref x, ref y, ref w, ref h, ref u0, ref v0, ref u1, ref v1))
            return;
        TextRenderer.DrawPremultipliedSprite(texture, x, y, w, h, u0, v0, u1, v1, ApplyAlpha(tint));
    }
```

- [ ] **Step 4: Paint into premultiplied** (`PluginCanvasSurface` constructor)

```csharp
        // The target is cleared to transparent and holds premultiplied colour
        // once painted; straight alpha on both channels would store alpha
        // squared and every translucent pixel would show too faint.
        _renderer = new TextRenderer(
            services.Device,
            services.Frames,
            services.ShaderDirectory,
            GpuBlendMode.StraightAlphaIntoPremultiplied);
```

- [ ] **Step 5: Show premultiplied** (`PluginCanvasElement.OnDraw`, replacing the `DrawSprite` line)

```csharp
        // The target holds premultiplied colour (see PluginCanvasSurface).
        ctx.DrawSpritePremultiplied(shown.Handle, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
```

- [ ] **Step 6: Run the canvas suites**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~PluginCanvas"`
Expected: `Passed!`, 0 failed (including the two new tests and the existing `APaintLandsAsSpriteRunsOnTheSurfaceAndOneBlitOnTheInterface`).

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.App/UI/UiRenderContext.cs src/AcDream.App/UI/Layout/PluginCanvasSurface.cs src/AcDream.App/UI/Layout/PluginCanvasElement.cs tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs
git commit -m "plugin canvas: translucent content is shown at the alpha it was painted with

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/UiRenderContext.cs", "src/AcDream.App/UI/Layout/PluginCanvasSurface.cs", "src/AcDream.App/UI/Layout/PluginCanvasElement.cs", "tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter FullyQualifiedName~PluginCanvas", "acceptanceCriteria": ["surface pipeline is ui-text-into-premultiplied", "main run premultiplied and composite pipeline bound", "PushAlpha 0.5 gives 0.5,0.5,0.5,0.5", "existing canvas suites pass"], "modelTier": "standard"}
```

---

### Task 6: Full verification and push

**Goal:** The branch is green on the portable gate and the Vulkan lane locally, and is pushed to `origin` with a ready PR description.

**Files:** none (a PR description is drafted in the conversation, not committed)

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` reports 0 warnings, 0 errors
- [ ] The portable filter reports 0 failed across the solution
- [ ] `Lane=Vulkan` on `AcDream.App.Tests` reports 0 failed under MoltenVK
- [ ] `git diff main --stat` touches no file under `src/AcDream.App/Rendering/Shaders/`
- [ ] Branch pushed to `origin`; PR not opened until the user picks the target

**Verify:** the four commands in the steps below, each with the stated output

**Steps:**

- [ ] **Step 1: Build**

Run: `dotnet build AcDream.slnx -c Release 2>&1 | tail -4`
Expected: `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 2: Portable gate**

Run: `dotnet test AcDream.slnx -c Release --no-build --filter "Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure" 2>&1 | grep -E "Passed!|Failed!"`
Expected: every line `Passed!`. If an unrelated failure appears, rerun that assembly alone and compare against `main` before claiming anything; report it rather than fixing it here.

- [ ] **Step 3: Vulkan lane**

Run: `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --no-build --filter "Lane=Vulkan" 2>&1 | grep -E "Passed!|Failed!"`
Expected: `Passed!`. MoltenVK is not the CI device (CI runs NVIDIA); note that in the PR.

- [ ] **Step 4: No shader changes; push**

```bash
git diff main --stat -- src/AcDream.App/Rendering/Shaders/ | wc -l   # expect 0
git push -u origin painter-v2/canvas-alpha
```

- [ ] **Step 5: Draft the PR description and ask the user where to open it**

Draft (fill in the numbers recorded in Task 4 Step 3):

```markdown
## Plugin canvas: translucent content composites at the alpha it was painted with

A canvas is painted into an off-screen target cleared to transparent, then drawn
on the interface. Both steps used straight alpha on every channel, so the target
stored alpha squared and the composite applied it again: a 50 % fill showed at
about 25 %.

- New `GpuBlendMode.StraightAlphaIntoPremultiplied` (colour `SrcAlpha, OneMinusSrcAlpha`,
  alpha `One, OneMinusSrcAlpha`); the Vulkan pipeline now takes alpha factors
  from `VulkanViewportMapping.AlphaBlendFactorsOf`, identical to the colour
  factors for every existing mode.
- The canvas surface paints with it; the canvas quad is composited with the
  existing `PremultipliedAlpha` through a pipeline `TextRenderer` creates on
  first use (construction still creates exactly one pipeline).
- No shader or SPIR-V changes.
- Tests: recording-device tests for pipelines, bind order and tint; a
  `Lane=Vulkan` readback (8×8, 50 % white over black): canvas alpha 128 and
  composite 128 after, versus <alpha> and <red> before. Run locally on MoltenVK.
- Test refactor: the headless Vulkan host and readback moved to shared helpers.

API change: none (plugin contract untouched; plugins see correct translucency).

https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB
```

Ask the user whether to open it against `origin` (`davidc-smith/OpenAC`) or upstream (`eriknihlen/OpenAC`).

```json:metadata
{"files": [], "verifyCommand": "dotnet build AcDream.slnx -c Release", "acceptanceCriteria": ["0 warnings 0 errors", "portable filter 0 failed", "Lane=Vulkan 0 failed on MoltenVK", "no shader files changed", "branch pushed, PR target asked"], "modelTier": "mechanical"}
```
