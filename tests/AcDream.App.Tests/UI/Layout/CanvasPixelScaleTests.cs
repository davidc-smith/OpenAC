using System.Numerics;
using AcDream.App.UI.Layout;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// A canvas is painted at framebuffer pixels per point, rounded up to a
/// quarter, from 1 to 4, and lowered only to fit the device.
/// </summary>
public sealed class CanvasPixelScaleTests
{
    [Theory]
    [InlineData(1f, 1f, 1f)]
    [InlineData(2f, 2f, 2f)]
    [InlineData(1.5f, 1.5f, 1.5f)]
    [InlineData(1.1f, 1.1f, 1.25f)]
    [InlineData(2f, 1f, 2f)]
    [InlineData(1f, 1.6f, 1.75f)]
    [InlineData(3f, 3f, 3f)]
    [InlineData(5f, 5f, 4f)]
    [InlineData(0.5f, 0.5f, 1f)]
    public void TheInterfaceScaleIsTheLargerAxisRoundedUpToAQuarter(
        float framebufferX, float framebufferY, float expected)
    {
        Assert.Equal(expected, CanvasPixelScale.ForInterface(new Vector2(framebufferX, framebufferY)));
    }

    [Fact]
    public void AScaleAHairOverAStepIsThatStep()
    {
        Assert.Equal(2f, CanvasPixelScale.ForInterface(new Vector2(2880f / 1440f + 1e-6f)));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(0f)]
    public void AScaleThatIsNotAPositiveNumberIsOne(float framebuffer)
    {
        Assert.Equal(1f, CanvasPixelScale.ForInterface(new Vector2(framebuffer)));
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
