using System.Collections.Immutable;
using System.Numerics;
using AcDream.Content;
using AcDream.Runtime.Maps;
using AcDream.Runtime.Tests.Plugins;
using DatReaderWriter;
using DatReaderWriter.Options;
using Xunit.Abstractions;

namespace AcDream.Runtime.Tests.Maps;

/// <summary>
/// Tunnel of Love (0x0032) from the installed game files: its bridges are
/// objects placed in floorless cells, 6 and 12 metres over the room below.
/// Their decks are the cells' object floors, and band the cells there.
/// </summary>
[Trait("Lane", "InstalledDat")]
public sealed class DungeonObjectFloorsInstalledDatTests
{
    private const uint Landblock = 0x00320000u;

    private readonly ITestOutputHelper _output;

    public DungeonObjectFloorsInstalledDatTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TunnelOfLovesBridgesAreFloorsInTheirOwnBands()
    {
        string? datDirectory = InstalledDatTestPath.Resolve();
        Assert.True(datDirectory is not null, "Lane=InstalledDat requires an installed retail DAT directory; see docs/release-gate.md.");
        using var dats = new DatCollectionAdapter(new DatCollection(datDirectory, DatAccessType.Read));
        DungeonFloorplan plan = new DungeonFloorplanBuilder(dats, new object()).TryBuild(Landblock)!;
        _output.WriteLine(plan.Counts.ToString());

        DungeonFloorplanCell Cell(uint low) => plan.Cells.Single(cell => cell.CellId == (Landblock | low));
        foreach (uint low in new uint[] { 0x01C0, 0x01BA, 0x0192, 0x018C, 0x0193, 0x0206, 0x024E })
        {
            DungeonFloorplanCell cell = Cell(low);
            ImmutableArray<ImmutableArray<Vector3>> faces = cell.ObjectFloors;
            string extent = faces.IsEmpty
                ? "none"
                : $"x {faces.Min(MinX):F1}..{faces.Max(MaxX):F1} y {faces.Min(MinY):F1}..{faces.Max(MaxY):F1}"
                    + $" z {faces.Min(MinZ):F1}..{faces.Max(MaxZ):F1}";
            _output.WriteLine($"0x{low:X4}: band {cell.LayerZ} own floors {cell.Floors.Length} object floors {faces.Length} {extent}");
        }

        DungeonFloorplanCell bridge = Cell(0x01C0);
        Assert.Equal(18f, bridge.LayerZ);
        // The arch's seven up-facing faces; its seven undersides face down and are left out.
        Assert.Equal(7, bridge.ObjectFloors.Length);
        Assert.InRange(bridge.ObjectFloors.Min(MinZ), 17f, 18f);
        Assert.InRange(bridge.ObjectFloors.Max(MaxZ), 20f, 21f);
        Assert.InRange(bridge.ObjectFloors.Min(MinX), 64.9f, 65.1f);
        Assert.InRange(bridge.ObjectFloors.Max(MaxX), 74.9f, 75.1f);

        Assert.Equal(12f, Cell(0x0192).LayerZ);
        Assert.Equal(12f, Cell(0x018C).LayerZ);
        Assert.Equal(6f, Cell(0x0193).LayerZ);
        Assert.Empty(Cell(0x0206).ObjectFloors);
        Assert.Empty(Cell(0x024E).ObjectFloors);

        // The bridge's deck is drawn in band 18, from x 55 to 75 across y -42 to -38.
        DungeonFloorplanLayer band = plan.Layers.Single(static layer => layer.Z == 18f);
        ImmutableArray<Vector2>[] deck = band.Floors
            .Where(static floor => floor.All(static corner => corner.Y is >= -42.2f and <= -37.8f && corner.X is >= 54.9f and <= 75.1f))
            .ToArray();
        Assert.NotEmpty(deck);
        Assert.InRange(deck.Min(static floor => floor.Min(static corner => corner.X)), 54.9f, 55.1f);
        Assert.InRange(deck.Max(static floor => floor.Max(static corner => corner.X)), 74.9f, 75.1f);
    }

    private static float MinX(ImmutableArray<Vector3> face) => face.Min(static corner => corner.X);
    private static float MaxX(ImmutableArray<Vector3> face) => face.Max(static corner => corner.X);
    private static float MinY(ImmutableArray<Vector3> face) => face.Min(static corner => corner.Y);
    private static float MaxY(ImmutableArray<Vector3> face) => face.Max(static corner => corner.Y);
    private static float MinZ(ImmutableArray<Vector3> face) => face.Min(static corner => corner.Z);
    private static float MaxZ(ImmutableArray<Vector3> face) => face.Max(static corner => corner.Z);
}
