using System.Numerics;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Navigation;
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthNav3DTests
{
    private const uint Cell = 0xA9B40001;
    private const uint Red = 0xC0FF0000;
    private readonly FakeGameView _view = new() { Pose = (Cell, new Vector3(50, 60, 10), Quaternion.Identity) };
    private readonly FakeTriangleLayer _layer = new();

    private RynthNav3D Nav() => new(_view, _layer);

    [Fact]
    public void ARingIsAFadingWallAndAGroundBand()
    {
        RynthNav3D nav = Nav();
        nav.BeginTick();
        nav.AddRing(10, 2, 20, radius: 3, thickness: 0.5f, Red);
        nav.CommitTick();

        PluginWorldTriangle[] set = Assert.Single(_layer.Sets);
        Assert.Equal(128, set.Length);
        var top = new PluginColor(255, 0, 0, 0x10);
        var solid = new PluginColor(255, 0, 0, 0xC0);
        Assert.Equal(64, set.Count(t => t.Color == solid && (t.ColorB == top || t.ColorC == top)));
        Assert.Equal(64, set.Count(t => t.Color == solid && t.ColorB is null && t.ColorC is null));
        double maxElevation = set.SelectMany(t => new[] { t.A, t.B, t.C }).Max(p => p.Elevation);
        Assert.Equal(RynthNav3DTests.Elevation(2 + RynthNav3D.DefaultRingHeight), maxElevation, precision: 6);
    }

    [Fact]
    public void TheGroundBandSpansRadiusMinusToRadiusPlusThickness()
    {
        RynthNav3D nav = Nav();
        nav.BeginTick();
        nav.AddRing(10, 2, 20, radius: 3, thickness: 0.5f, Red);
        nav.CommitTick();

        PluginNavigationPosition centre = RuntimeNavigationPositions.FromCellLocal(Cell, new Vector3(10, 20, 2));
        double[] distances = Assert.Single(_layer.Sets)
            .Where(t => t.ColorB is null && t.ColorC is null)
            .SelectMany(t => new[] { t.A, t.B, t.C })
            .Select(p => p.HorizontalDistanceMeters(centre))
            .ToArray();
        Assert.Equal(3.5, distances.Max(), precision: 2);
        Assert.Equal(2.5, distances.Min(), precision: 2);
    }

    [Fact]
    public void ARingExKeepsATenthOfAMetreWallAtLeast()
    {
        RynthNav3D nav = Nav();
        nav.BeginTick();
        nav.AddRingEx(10, 2, 20, radius: 3, thickness: 0.5f, height: 0.05f, Red);
        nav.CommitTick();

        double maxElevation = Assert.Single(_layer.Sets).SelectMany(t => new[] { t.A, t.B, t.C }).Max(p => p.Elevation);
        Assert.Equal(Elevation(2.1f), maxElevation, precision: 6);
    }

    [Fact]
    public void ALineIsAGroundStripAndALowWall()
    {
        RynthNav3D nav = Nav();
        nav.BeginTick();
        nav.AddLine(0, 1, 0, 10, 1, 0, thickness: 0.2f, Red);
        nav.AddLine(5, 1, 5, 5.0001f, 1, 5, thickness: 0.2f, Red);
        nav.CommitTick();

        Assert.Equal(4, Assert.Single(_layer.Sets).Length);
    }

    [Fact]
    public void ATrianglePassesThroughInTheAnchorCellsFrame()
    {
        RynthNav3D nav = Nav();
        nav.BeginTick();
        nav.AddTriangle(1, 2, 3, 4, 5, 6, 7, 8, 9, Red);
        nav.CommitTick();

        PluginWorldTriangle triangle = Assert.Single(Assert.Single(_layer.Sets));
        Assert.Equal(RuntimeNavigationPositions.FromCellLocal(Cell, new Vector3(1, 3, 2)), triangle.A);
        Assert.Equal(RuntimeNavigationPositions.FromCellLocal(Cell, new Vector3(4, 6, 5)), triangle.B);
        Assert.Equal(RuntimeNavigationPositions.FromCellLocal(Cell, new Vector3(7, 9, 8)), triangle.C);
        Assert.Equal(new PluginColor(255, 0, 0, 0xC0), triangle.Color);
    }

    [Fact]
    public void TheCapsAreRynthCoresAndATickStartsEmpty()
    {
        RynthNav3D nav = Nav();
        nav.BeginTick();
        for (int i = 0; i < RynthNav3D.MaxTriangles + 5; i++)
            nav.AddTriangle(0, 0, 0, 1, 0, 0, 0, 0, i, Red);
        for (int i = 0; i < RynthNav3D.MaxLines + 5; i++)
            nav.AddLine(0, 0, i, 1, 0, i, 0.1f, Red);
        for (int i = 0; i < RynthNav3D.MaxRings + 5; i++)
            nav.AddRing(i, 0, 0, 1, 0.1f, Red);
        nav.CommitTick();
        Assert.Equal(RynthNav3D.MaxTriangles + RynthNav3D.MaxLines * 4 + RynthNav3D.MaxRings * 128, _layer.Sets[^1].Length);

        nav.BeginTick();
        nav.CommitTick();
        Assert.Empty(_layer.Sets[^1]);
    }

    [Fact]
    public void ATickThatCrossedALandblockIsDropped()
    {
        RynthNav3D nav = Nav();
        nav.BeginTick();
        nav.AddTriangle(1, 2, 3, 4, 5, 6, 7, 8, 9, Red);
        nav.CommitTick();

        nav.BeginTick();
        nav.AddTriangle(1, 2, 3, 4, 5, 6, 7, 8, 10, Red);
        _view.Pose = (0xA9B50001, new Vector3(50, 1, 10), Quaternion.Identity);
        nav.CommitTick();

        Assert.Single(_layer.Sets);
    }

    [Fact]
    public void AnUnchangedTickIsNotSentAgainAndNoPoseEmptiesTheLayerOnce()
    {
        RynthNav3D nav = Nav();
        for (int tick = 0; tick < 2; tick++)
        {
            nav.BeginTick();
            nav.AddTriangle(1, 2, 3, 4, 5, 6, 7, 8, 9, Red);
            nav.CommitTick();
        }
        Assert.Single(_layer.Sets);

        _view.Pose = null;
        for (int tick = 0; tick < 2; tick++)
        {
            nav.BeginTick();
            nav.AddTriangle(1, 2, 3, 4, 5, 6, 7, 8, 9, Red);
            nav.CommitTick();
        }
        Assert.Equal(2, _layer.Sets.Count);
        Assert.Empty(_layer.Sets[^1]);
    }

    [Fact]
    public void WithoutALayerNothingIsDrawnAndNothingFails()
    {
        var nav = new RynthNav3D(_view, null);
        nav.BeginTick();
        nav.AddRing(1, 1, 1, 1, 0.1f, Red);
        nav.CommitTick();
        Assert.Equal((1, 0, 0), nav.Counts);
        nav.Dispose();
    }

    [Fact]
    public void DisposingDisposesTheLayer()
    {
        RynthNav3D nav = Nav();
        nav.Dispose();
        Assert.True(_layer.Disposed);
    }

    private static double Elevation(float metres) =>
        RuntimeNavigationPositions.FromCellLocal(Cell, new Vector3(0, 0, metres)).Elevation;

    private sealed class FakeTriangleLayer : IPluginWorldTriangleLayer
    {
        public List<PluginWorldTriangle[]> Sets { get; } = new();
        public bool Disposed { get; private set; }
        public void SetTriangles(IReadOnlyList<PluginWorldTriangle> triangles) => Sets.Add([.. triangles]);
        public void Dispose() => Disposed = true;
    }
}
