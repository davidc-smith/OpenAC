using System.Numerics;
using AcDream.App.Interaction;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// The batching the icon overlay relies on. Sprite quads join a run only
/// when their texture matches the quad drawn just before, so drawing every
/// border (all on the fill texture) and then every image keeps a set of
/// icons with one image to two runs however many there are.
/// </summary>
public sealed class WorldIconOverlayDrawTests
{
    private const uint FillTex = 0u;
    private const uint IconTex = 7u;
    private const uint OtherTex = 8u;
    private const int VerticesPerQuad = 6;

    private sealed class NullGpuFrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }

    private static readonly Matrix4x4 View =
        Matrix4x4.CreateLookAt(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ);

    private static readonly Matrix4x4 Projection =
        Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 4f / 3f, 0.1f, 100f);

    private static readonly Vector2 Viewport = new(800f, 600f);

    // Objects spread out in front of the camera, ids from 1, each at its own
    // distance so the far-to-near order is fixed.
    private static WorldLabelAnchor? Anchor(uint id) => id == 0u
        ? null
        : new WorldLabelAnchor(new Vector3((id % 8) - 4f, 8f + id, 0f), 1.5f, WorldLabelAnchorSource.PhysicsCylinder);

    private static WorldIconTexture? Resolve(string ownerId, PluginImage image) => image.Handle switch
    {
        1 => new WorldIconTexture(IconTex, 32, 32),
        2 => new WorldIconTexture(OtherTex, 32, 32),
        _ => null,
    };

    private static WorldIconEntry Icon(uint objectId, int imageHandle = 1, bool border = false) =>
        new("a.plugin", new PluginWorldIcon(PluginMarkerAnchor.Object(objectId), new PluginImage(imageHandle, 32, 32))
        {
            Border = border ? new PluginColor(255, 0, 0) : null,
        });

    private static IReadOnlyList<(uint Texture, int VertexCount, float Alpha)> DrawOnce(
        IReadOnlyList<WorldIconEntry> icons,
        out WorldIconOverlayController controller,
        IReadOnlyList<PluginWorldLabel>? labels = null)
    {
        var device = new RecordingGpuDevice();
        var renderer = new TextRenderer(device, new NullGpuFrameSource(), "unused");
        renderer.Begin(Viewport);
        var ctx = new UiRenderContext(renderer, Viewport);
        var root = new UiRoot { Width = Viewport.X, Height = Viewport.Y };
        UiOverlayHost host = UiOverlayHost.Mount(root);
        controller = WorldIconOverlayController.Mount(
            host,
            () => icons,
            Resolve,
            Anchor,
            _ => null,
            labels is null ? null : () => labels,
            16f,
            () => (View, Projection, Viewport));

        controller.Tick();
        root.Draw(ctx);
        return renderer.DebugSpriteSegments;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(40)] // object 40 stands 48 m away, inside the default 60 m range
    public void BorderedIconsWithOneImageCostTwoRunsHoweverManyThereAre(int count)
    {
        var icons = Enumerable.Range(1, count).Select(i => Icon((uint)i, border: true)).ToArray();

        var segments = DrawOnce(icons, out WorldIconOverlayController controller);

        Assert.Equal(count, controller.Element.PlacementCount);
        Assert.Equal(2, segments.Count);
        Assert.Equal(FillTex, segments[0].Texture);
        Assert.Equal(count * 4 * VerticesPerQuad, segments[0].VertexCount);
        Assert.Equal(IconTex, segments[1].Texture);
        Assert.Equal(count * VerticesPerQuad, segments[1].VertexCount);
    }

    [Fact]
    public void UnborderedIconsWithOneImageCostOneRun()
    {
        var segments = DrawOnce([Icon(1u), Icon(2u), Icon(3u)], out _);

        Assert.Equal(IconTex, Assert.Single(segments).Texture);
    }

    [Fact]
    public void EachChangeOfImageInDrawOrderStartsARun()
    {
        // Far to near is object 3, 2, 1: images 1, 2, 1 -> three runs.
        var segments = DrawOnce([Icon(1u, 1), Icon(2u, 2), Icon(3u, 1)], out _);

        Assert.Equal(new[] { IconTex, OtherTex, IconTex }, segments.Select(s => s.Texture));
    }

    [Fact]
    public void AnIconWhoseImageDoesNotResolveIsPlacedButDrawsNothing()
    {
        var segments = DrawOnce([Icon(1u, imageHandle: 99, border: true)], out WorldIconOverlayController controller);

        Assert.Equal(1, controller.Element.PlacementCount);
        Assert.Empty(segments);
    }

    [Fact]
    public void LabelLinesOverAnObjectLiftItsIcons()
    {
        DrawOnce([Icon(1u)], out WorldIconOverlayController bare);
        float bareY = bare.Placements.Single().Y;

        DrawOnce(
            [Icon(1u)],
            out WorldIconOverlayController lifted,
            labels: [new PluginWorldLabel(1u, "a", Vector4.One, Line: 0), new PluginWorldLabel(1u, "b", Vector4.One, Line: 2)]);

        Assert.Equal(bareY - 3 * 16f, lifted.Placements.Single().Y, 3);
    }

    [Fact]
    public void NothingPlacedHidesTheLayer()
    {
        DrawOnce([], out WorldIconOverlayController controller);

        Assert.False(controller.LayerVisible);
    }

    [Theory]
    [InlineData(32, 16, 0f, 6f, 24f, 12f)]
    [InlineData(16, 32, 6f, 0f, 12f, 24f)]
    [InlineData(32, 32, 0f, 0f, 24f, 24f)]
    public void AnImageIsFittedIntoItsSquareKeepingItsShape(
        int width, int height, float x, float y, float w, float h)
    {
        Assert.Equal((x, y, w, h), WorldIconLayerElement.Fit(24f, width, height));
    }
}
