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
        Assert.All(ThemeDrawCapture.Vertices(renderer), v => Assert.InRange(v.Position.X, -1e-4f, 20f + 1e-4f));
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
        ctx.DrawSoftShadow(100f, 100f, 50f, 50f, 10f, 4f, 12f, new Vector4(0f, 0f, 0f, 0.45f));
        Assert.True(ThemeDrawCapture.Vertices(renderer).Min(v => v.Position.X) < 100f);
        Assert.Equal(1, ctx.ClipStackDepth);
        ctx.FillRoundedRect(0f, 0f, 20f, 20f, 4f, Red);
        Assert.DoesNotContain(ThemeDrawCapture.Vertices(renderer), v => v.Color == Red && v.Position.X < 100f);
    }

    /// <summary>
    /// The shadow of a caster at (100, 100), 60 by 50 with radius 10, dropped
    /// 4 and spread 12: as dark at each point outside the caster as six
    /// stacked fills, each grown a further 2, would make it.
    /// </summary>
    [Theory]
    [InlineData(130f, 152f, 6)]   // below the caster, inside the dropped rectangle
    [InlineData(95f, 130f, 4)]    // 5 left of it: the layers grown 6 and more
    [InlineData(125f, 161f, 3)]   // 7 below it: grown 8, 10 and 12
    [InlineData(171f, 130f, 1)]   // 11 right of it: only the layer grown 12
    [InlineData(174f, 130f, 0)]   // past the spread
    public void AShadowIsAsDarkAsSixStackedLayersOutsideItsCaster(float px, float py, int layers)
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        ctx.DrawSoftShadow(100f, 100f, 60f, 50f, 10f, 4f, 12f, new Vector4(0f, 0f, 0f, 0.45f));
        float expected = 1f - MathF.Pow(1f - 0.45f / 6f, layers);
        Assert.Equal(expected, AlphaAt(ThemeDrawCapture.Vertices(renderer), new Vector2(px, py)), 3);
    }

    [Theory]
    [InlineData(130f, 125f)]   // the middle
    [InlineData(101f, 130f)]   // just inside its left side
    [InlineData(159f, 115f)]   // just inside its right side
    public void AShadowDrawsNothingUnderTheBodyOfItsCaster(float px, float py)
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        ctx.DrawSoftShadow(100f, 100f, 60f, 50f, 10f, 4f, 12f, new Vector4(0f, 0f, 0f, 0.45f));
        Assert.Equal(0f, AlphaAt(ThemeDrawCapture.Vertices(renderer), new Vector2(px, py)));
    }

    /// <summary>How dark the drawn triangles make a point, alpha over alpha.</summary>
    private static float AlphaAt(List<(Vector2 Position, Vector4 Color)> v, Vector2 p)
    {
        float clear = 1f;
        for (int i = 0; i + 2 < v.Count; i += 3)
        {
            Vector2 a = v[i].Position, b = v[i + 1].Position, c = v[i + 2].Position;
            float area = Cross(b - a, c - a);
            if (MathF.Abs(area) < 1e-6f) continue;
            float wa = Cross(b - p, c - p) / area;
            float wb = Cross(c - p, a - p) / area;
            float wc = 1f - wa - wb;
            if (wa < 0f || wb < 0f || wc < 0f) continue;
            float alpha = wa * v[i].Color.W + wb * v[i + 1].Color.W + wc * v[i + 2].Color.W;
            clear *= 1f - alpha;
        }
        return 1f - clear;

        static float Cross(Vector2 x, Vector2 y) => x.X * y.Y - x.Y * y.X;
    }
}
