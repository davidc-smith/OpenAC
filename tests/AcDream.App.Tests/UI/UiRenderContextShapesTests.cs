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
        ctx.DrawSoftShadow(100f, 100f, 50f, 50f, 10f, 12f, new Vector4(0f, 0f, 0f, 0.45f));
        Assert.True(ThemeDrawCapture.Vertices(renderer).Min(v => v.Position.X) < 100f);
        Assert.Equal(1, ctx.ClipStackDepth);
        ctx.FillRoundedRect(0f, 0f, 20f, 20f, 4f, Red);
        Assert.DoesNotContain(ThemeDrawCapture.Vertices(renderer), v => v.Color == Red && v.Position.X < 100f);
    }
}
