using System.Numerics;
using AcDream.App.Extensions.Overlay;

namespace AcDream.App.Tests.Extensions;

public sealed class ImGuiScissorTests
{
    [Fact]
    public void ScaleOne()
    {
        Assert.True(ImGuiScissor.TryMap(new Vector4(10, 20, 110, 70), Vector2.Zero, Vector2.One, 800, 600, out var s));
        Assert.Equal((10, 530, 100, 50), s);
    }

    [Fact]
    public void ScaleTwo()
    {
        Assert.True(ImGuiScissor.TryMap(new Vector4(10, 20, 110, 70), Vector2.Zero, new Vector2(2, 2), 1600, 1200, out var s));
        Assert.Equal((20, 1060, 200, 100), s);
    }

    [Fact]
    public void EmptyAndOffScreenAreSkipped()
    {
        Assert.False(ImGuiScissor.TryMap(new Vector4(10, 10, 10, 50), Vector2.Zero, Vector2.One, 800, 600, out _));
        Assert.False(ImGuiScissor.TryMap(new Vector4(900, 10, 950, 50), Vector2.Zero, Vector2.One, 800, 600, out _));
    }

    [Fact]
    public void PartlyOffScreenIsClamped()
    {
        Assert.True(ImGuiScissor.TryMap(new Vector4(-20, -10, 30, 40), Vector2.Zero, Vector2.One, 800, 600, out var s));
        Assert.Equal((0, 560, 30, 40), s);
    }

    [Fact]
    public void NonFiniteClipValuesAreRejected()
    {
        Assert.False(ImGuiScissor.TryMap(new Vector4(float.NaN, 0, 50, 50), Vector2.Zero, Vector2.One, 800, 600, out _));
        Assert.False(ImGuiScissor.TryMap(new Vector4(0, 0, 50, float.PositiveInfinity), Vector2.Zero, Vector2.One, 800, 600, out _));
        Assert.False(ImGuiScissor.TryMap(new Vector4(0, 0, 50, 50), Vector2.Zero, new Vector2(float.NaN, 1), 800, 600, out _));
    }
}
