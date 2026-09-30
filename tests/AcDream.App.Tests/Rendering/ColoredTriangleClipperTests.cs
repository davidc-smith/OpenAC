using System.Numerics;
using AcDream.App.Rendering;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// One coloured triangle cut to the clip: whole triangles pass untouched,
/// cut ones gain corners on the clip's edges with the colour blended to
/// match where the cut fell.
/// </summary>
public sealed class ColoredTriangleClipperTests
{
    private static readonly Vector4 Red = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 Blue = new(0f, 0f, 1f, 1f);
    private static readonly Vector4 Purple = new(0.5f, 0f, 0.5f, 1f);

    private static UiColorVertex V(float x, float y, Vector4 color) => new(new Vector2(x, y), color);

    private static void AssertNear(UiColorVertex expected, UiColorVertex actual)
    {
        Assert.InRange(Vector2.Distance(expected.Position, actual.Position), 0f, 1e-4f);
        Assert.InRange(Vector4.Distance(expected.Color, actual.Color), 0f, 1e-4f);
    }

    [Fact]
    public void ATriangleWhollyInsideComesBackUnchanged()
    {
        Span<UiColorVertex> output = stackalloc UiColorVertex[ColoredTriangleClipper.MaxClippedVertices];
        UiColorVertex a = V(1f, 1f, Red), b = V(9f, 1f, Blue), c = V(1f, 9f, Red);

        int count = ColoredTriangleClipper.Clip(0f, 0f, 10f, 10f, a, b, c, output);

        Assert.Equal(3, count);
        Assert.Equal([a, b, c], output[..3].ToArray());
    }

    [Fact]
    public void ATriangleWhollyOutsideOrAnEmptyClipLeavesNothing()
    {
        Span<UiColorVertex> output = stackalloc UiColorVertex[ColoredTriangleClipper.MaxClippedVertices];
        UiColorVertex a = V(20f, 1f, Red), b = V(29f, 1f, Blue), c = V(20f, 9f, Red);

        Assert.Equal(0, ColoredTriangleClipper.Clip(0f, 0f, 10f, 10f, a, b, c, output));
        Assert.Equal(0, ColoredTriangleClipper.Clip(0f, 0f, 0f, 10f, V(1f, 1f, Red), V(2f, 1f, Red), V(1f, 2f, Red), output));
    }

    [Fact]
    public void ACutGainsCornersOnTheEdgeWithTheColourBlended()
    {
        Span<UiColorVertex> output = stackalloc UiColorVertex[ColoredTriangleClipper.MaxClippedVertices];

        int count = ColoredTriangleClipper.Clip(
            0f, 0f, 10f, 100f, V(0f, 0f, Red), V(20f, 0f, Blue), V(0f, 20f, Red), output);

        Assert.Equal(4, count);
        AssertNear(V(0f, 0f, Red), output[0]);
        AssertNear(V(10f, 0f, Purple), output[1]);
        AssertNear(V(10f, 10f, Purple), output[2]);
        AssertNear(V(0f, 20f, Red), output[3]);
    }

    [Fact]
    public void ATriangleCutOnEveryEdgeHasSevenCornersAllInside()
    {
        Span<UiColorVertex> output = stackalloc UiColorVertex[ColoredTriangleClipper.MaxClippedVertices];

        int count = ColoredTriangleClipper.Clip(
            0f, 0f, 20f, 20f, V(-5f, -5f, Red), V(25f, 5f, Blue), V(5f, 25f, Red), output);

        Assert.Equal(7, count);
        foreach (UiColorVertex corner in output[..count])
        {
            Assert.InRange(corner.Position.X, -1e-4f, 20f + 1e-4f);
            Assert.InRange(corner.Position.Y, -1e-4f, 20f + 1e-4f);
        }
    }

    [Fact]
    public void ADestinationTooSmallForSevenCornersIsRefused()
    {
        var output = new UiColorVertex[ColoredTriangleClipper.MaxClippedVertices - 1];

        Assert.Throws<ArgumentException>(() => ColoredTriangleClipper.Clip(
            0f, 0f, 10f, 10f, V(1f, 1f, Red), V(2f, 1f, Red), V(1f, 2f, Red), output));
    }
}
