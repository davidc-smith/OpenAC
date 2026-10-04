using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// How a ground shape is cut into triangles: how many segments and rings,
/// which way an arc runs, and how high each point sits over the land.
/// </summary>
public sealed class GroundShapeTessellatorTests
{
    private const float Tolerance = 1e-3f;
    private static readonly PluginColor Paint = new(255, 0, 0, 128);
    private static readonly PluginMarkerAnchor Anchor = PluginMarkerAnchor.Object(1u);
    private static readonly Func<float, float, float?> NoLand = static (_, _) => null;
    private static readonly Func<float, float, float?> Slope = static (x, _) => x * 0.5f;

    private static List<Vector3> Cut(
        PluginGroundShape shape,
        Vector3 centre = default,
        float? start = null,
        bool follow = false,
        Func<float, float, float?>? terrain = null)
    {
        Assert.True(WorldMarkerRules.TryNormalizeShape(shape, out PluginGroundShape normalized));
        var triangles = new List<Vector3>();
        GroundShapeTessellator.Tessellate(
            normalized, centre, start ?? normalized.StartDegrees, follow, terrain ?? NoLand, triangles);
        Assert.Equal(GroundShapeTessellator.VertexCount(normalized), triangles.Count);
        return triangles;
    }

    private static float Across(Vector3 point, Vector3 centre) =>
        Vector2.Distance(new Vector2(point.X, point.Y), new Vector2(centre.X, centre.Y));

    [Theory]
    [InlineData(0.5f, 360f, 16)]  // a small ring still gets the minimum
    [InlineData(5f, 360f, 63)]    // 2π·5 m / 0.5 m = 62.8
    [InlineData(20f, 360f, 128)]  // a large ring is capped
    [InlineData(5f, 90f, 16)]     // a quarter takes a quarter of 63, rounded up
    [InlineData(0.5f, 10f, 1)]    // a sliver is at least one segment
    public void EdgesAreCutIntoHalfMetreSegmentsWithinBounds(float radius, float sweep, int expected)
    {
        Assert.Equal(expected, GroundShapeTessellator.SegmentsFor(radius, sweep));
    }

    [Theory]
    [InlineData(1f, 1)]
    [InlineData(4f, 1)]
    [InlineData(6f, 2)]
    [InlineData(100f, 8)]
    public void FilledShapesAreCutIntoRingsAboutFourMetresDeep(float radius, int expected)
    {
        Assert.Equal(expected, GroundShapeTessellator.FillRingsFor(radius));
    }

    [Fact]
    public void ARingIsABandFromItsInnerToItsOuterRadius()
    {
        var centre = new Vector3(10f, 20f, 3f);

        List<Vector3> points = Cut(PluginGroundShape.Ring(Anchor, 2f, 0.5f, Paint), centre);

        Assert.All(points, p => Assert.InRange(Across(p, centre), 1.5f - Tolerance, 2f + Tolerance));
        Assert.Contains(points, p => MathF.Abs(Across(p, centre) - 1.5f) < Tolerance);
        Assert.Contains(points, p => MathF.Abs(Across(p, centre) - 2f) < Tolerance);
    }

    [Fact]
    public void ADiscIsFilledFromItsCentreThroughItsRings()
    {
        var centre = new Vector3(10f, 20f, 3f);

        List<Vector3> points = Cut(PluginGroundShape.Disc(Anchor, 6f, Paint), centre);

        Assert.Contains(points, p => Across(p, centre) < Tolerance);
        // Two rings: the boundary between them is half way out.
        Assert.Contains(points, p => MathF.Abs(Across(p, centre) - 3f) < Tolerance);
        Assert.Contains(points, p => MathF.Abs(Across(p, centre) - 6f) < Tolerance);
        Assert.All(points, p => Assert.True(Across(p, centre) <= 6f + Tolerance));
    }

    [Fact]
    public void AnArcSweepsClockwiseFromItsStartBearing()
    {
        // From east (90°) a quarter turn clockwise ends at south.
        List<Vector3> points = Cut(PluginGroundShape.Arc(Anchor, 5f, 90f, 90f, filled: true, Paint));

        Assert.All(points, p =>
        {
            Assert.True(p.X >= -Tolerance);
            Assert.True(p.Y <= Tolerance);
        });
        Assert.Contains(points, p => MathF.Abs(p.X - 5f) < Tolerance && MathF.Abs(p.Y) < Tolerance);
        Assert.Contains(points, p => MathF.Abs(p.X) < Tolerance && MathF.Abs(p.Y + 5f) < Tolerance);
    }

    [Fact]
    public void TheStartGivenOverridesTheShapesOwnStart()
    {
        // A thin wedge pointing north, started from 85° instead (its object faces east): it points east.
        List<Vector3> points = Cut(PluginGroundShape.Arc(Anchor, 5f, -5f, 10f, filled: true, Paint), start: 85f);

        Assert.All(points, p => Assert.True(p.X >= -Tolerance));
        Assert.Contains(points, p => p.X > 4.9f);
    }

    [Fact]
    public void AnUnfilledArcIsABandOverItsSweepOnly()
    {
        // North round through east to south: the eastern half.
        List<Vector3> points = Cut(PluginGroundShape.Arc(Anchor, 5f, 0f, 180f, filled: false, Paint, width: 1f));

        Assert.All(points, p =>
        {
            Assert.InRange(Across(p, default), 4f - Tolerance, 5f + Tolerance);
            Assert.True(p.X >= -Tolerance);
        });
    }

    [Fact]
    public void OnTheLandEveryPointSitsJustAboveTheGroundUnderIt()
    {
        List<Vector3> points = Cut(
            PluginGroundShape.Disc(Anchor, 6f, Paint), new Vector3(0f, 0f, 100f), follow: true, terrain: Slope);

        Assert.All(points, p => Assert.Equal((p.X * 0.5f) + GroundShapeTessellator.LiftMeters, p.Z, 3));
    }

    [Fact]
    public void WhereTheLandIsUnknownAPointSitsAtTheCentresHeight()
    {
        List<Vector3> points = Cut(
            PluginGroundShape.Ring(Anchor, 2f, 0.2f, Paint), new Vector3(0f, 0f, 7f), follow: true, terrain: NoLand);

        Assert.All(points, p => Assert.Equal(7f + GroundShapeTessellator.LiftMeters, p.Z, 3));
    }

    [Fact]
    public void OffTheLandTheShapeIsFlatAtTheAnchorsHeight()
    {
        List<Vector3> points = Cut(
            PluginGroundShape.Disc(Anchor, 6f, Paint), new Vector3(0f, 0f, 100f), follow: false, terrain: Slope);

        Assert.All(points, p => Assert.Equal(100f + GroundShapeTessellator.LiftMeters, p.Z, 3));
    }
}
