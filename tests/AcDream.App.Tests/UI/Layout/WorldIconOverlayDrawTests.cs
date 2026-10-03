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

    /// <summary>A mounted overlay and the renderer it draws into, for tests that draw more than one frame.</summary>
    private sealed class Scene
    {
        private readonly TextRenderer _renderer;
        private readonly UiRenderContext _ctx;
        private readonly UiRoot _root;

        internal Scene(
            IReadOnlyList<WorldIconEntry> icons,
            IReadOnlyList<PluginWorldLabel>? labels = null,
            Func<bool>? hidden = null)
        {
            var device = new RecordingGpuDevice();
            _renderer = new TextRenderer(device, new NullGpuFrameSource(), "unused");
            _ctx = new UiRenderContext(_renderer, Viewport);
            _root = new UiRoot { Width = Viewport.X, Height = Viewport.Y };
            UiOverlayHost host = UiOverlayHost.Mount(_root);
            Controller = WorldIconOverlayController.Mount(
                host,
                () => icons,
                Resolve,
                Anchor,
                _ => null,
                labels is null ? null : () => labels,
                16f,
                () => (View, Projection, Viewport),
                hidden);
        }

        internal WorldIconOverlayController Controller { get; }

        /// <summary>One frame: the controller ticks, then the root draws.</summary>
        internal IReadOnlyList<(uint Texture, int VertexCount, float Alpha)> Frame()
        {
            _renderer.Begin(Viewport);
            Controller.Tick();
            _root.Draw(_ctx);
            return _renderer.DebugSpriteSegments;
        }
    }

    private static IReadOnlyList<(uint Texture, int VertexCount, float Alpha)> DrawOnce(
        IReadOnlyList<WorldIconEntry> icons,
        out WorldIconOverlayController controller,
        IReadOnlyList<PluginWorldLabel>? labels = null)
    {
        var scene = new Scene(icons, labels);
        controller = scene.Controller;
        return scene.Frame();
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
    public void ALabelsHeightOffsetLiftsTheIconsWithIt()
    {
        DrawOnce([Icon(1u)], out WorldIconOverlayController level,
            labels: [new PluginWorldLabel(1u, "a", Vector4.One)]);
        DrawOnce([Icon(1u)], out WorldIconOverlayController raised,
            labels: [new PluginWorldLabel(1u, "a", Vector4.One, HeightOffset: 1f)]);

        // The label hangs from its offset head; so must the row above it.
        WorldLabelAnchor at = Anchor(1u)!.Value;
        float lift = ScreenY(at.BasePosition + new Vector3(0f, 0f, at.Height))
            - ScreenY(at.BasePosition + new Vector3(0f, 0f, at.Height + 1f));
        Assert.True(lift > 0f);
        Assert.Equal(level.Placements.Single().Y - lift, raised.Placements.Single().Y, 3);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ANegativeOrNonFiniteLabelOffsetLeavesTheIconsWhereTheyWere(float offset)
    {
        DrawOnce([Icon(1u)], out WorldIconOverlayController level,
            labels: [new PluginWorldLabel(1u, "a", Vector4.One)]);
        DrawOnce([Icon(1u)], out WorldIconOverlayController odd,
            labels: [new PluginWorldLabel(1u, "a", Vector4.One, HeightOffset: offset)]);

        Assert.Equal(level.Placements.Single().Y, odd.Placements.Single().Y, 3);
    }

    private static float ScreenY(Vector3 point)
    {
        Vector4 clip = Vector4.Transform(new Vector4(point, 1f), View * Projection);
        return (1f - clip.Y / clip.W) * 0.5f * Viewport.Y;
    }

    [Fact]
    public void NothingPlacedHidesTheLayer()
    {
        DrawOnce([], out WorldIconOverlayController controller);

        Assert.False(controller.LayerVisible);
    }

    [Fact]
    public void NothingIsPlacedOrDrawnWhileThePortalViewIsShowing()
    {
        bool portal = true;
        var scene = new Scene([Icon(1u, border: true), Icon(2u)], hidden: () => portal);

        var segments = scene.Frame();

        Assert.Equal(0, scene.Controller.Element.PlacementCount);
        Assert.False(scene.Controller.LayerVisible);
        Assert.Empty(segments);

        portal = false;
        segments = scene.Frame();

        Assert.Equal(2, scene.Controller.Element.PlacementCount);
        Assert.True(scene.Controller.LayerVisible);
        Assert.NotEmpty(segments);
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
