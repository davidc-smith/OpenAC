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
