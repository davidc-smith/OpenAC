using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using AcDream.Core.Content;
using AcDream.Core.Physics;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Maps;
using AcDream.Runtime.Plugins;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;

namespace AcDream.Runtime.Tests.Maps;

/// <summary>
/// A plugin shading cliffs and water reads a landblock's ground from the
/// game files: the heights in metres, which triangles are too steep to stand
/// on, and where the water is, all as the physics finds them.
///
/// Mutation check (2026-10-14): choosing the triangle by the other diagonal
/// in <see cref="PluginLandblockTerrain.Sample(float, float)"/>, and reading a
/// shore cell's water from the far corner, each turned
/// <see cref="ASampleMatchesThePhysicsEverywhere"/> red.
/// </summary>
public sealed class TerrainCaptureTests
{
    [Fact]
    public void UnboundTheAreaAnswersTheDefault()
    {
        using var surface = new RuntimeAutomationSurface();

        Assert.True(surface.Terrain.CaptureLandblockTerrain(TerrainFiles.Hillside).IsEmpty);
    }

    [Fact]
    public void HeightsAreInMetresAndInTheDataOrder()
    {
        var files = new TerrainFiles();
        var terrain = new RuntimeTerrainAutomation();
        terrain.BindContent(files, files.Lock);

        PluginLandblockTerrain hillside = terrain.CaptureLandblockTerrain(TerrainFiles.Hillside | 0x0012u);

        Assert.False(hillside.IsEmpty);
        Assert.Equal(TerrainFiles.Hillside, hillside.LandblockId);
        Assert.Equal(81, hillside.Heights.Count);
        Assert.Equal(64, hillside.Cells.Count);
        Assert.Equal(TerrainFiles.FlatHeight, hillside.CornerHeight(0, 0));
        Assert.Equal(TerrainFiles.GentleTop, hillside.CornerHeight(3, 5));
        Assert.Equal(TerrainFiles.CliffTop, hillside.CornerHeight(8, 8));
        Assert.Equal(TerrainFiles.WaterType, hillside.TerrainTypes[(7 * 9) + 7]);
        Assert.Equal(TerrainFiles.GrassType, hillside.TerrainTypes[0]);
        for (int index = 0; index < 64; index++)
            Assert.Equal(TerrainFiles.Hillside | (uint)(index + 1), hillside.Cells[index].CellId);
        Assert.Equal(0, files.ReadsOutsideTheLock);
    }

    [Fact]
    public void ACellTooSteepToStandOnIsNotWalkable()
    {
        var files = new TerrainFiles();
        var terrain = new RuntimeTerrainAutomation();
        terrain.BindContent(files, files.Lock);
        PluginLandblockTerrain hillside = terrain.CaptureLandblockTerrain(TerrainFiles.Hillside);

        // Flat, a 10 m rise over a cell, then a 30 m rise over a cell.
        AssertCell(hillside.Cell(0, 4), walkable: true);
        AssertCell(hillside.Cell(2, 4), walkable: true);
        AssertCell(hillside.Cell(3, 4), walkable: false);
        AssertCell(hillside.Cell(4, 4), walkable: true);

        PluginTerrainSample onTheCliff = hillside.Sample(3.5f * 24f, 4.5f * 24f);
        Assert.False(onTheCliff.Walkable);
        Assert.Equal(TerrainFiles.GentleTop + 15f, onTheCliff.Height, 3);
        Assert.Equal(TerrainFiles.Hillside | 0x001Du, onTheCliff.CellId);

        static void AssertCell(PluginTerrainCell cell, bool walkable)
        {
            Assert.Equal(walkable, cell.First.Walkable);
            Assert.Equal(walkable, cell.Second.Walkable);
        }
    }

    [Fact]
    public void WaterIsKnownByCornerCellAndPoint()
    {
        var files = new TerrainFiles();
        var terrain = new RuntimeTerrainAutomation();
        terrain.BindContent(files, files.Lock);
        PluginLandblockTerrain hillside = terrain.CaptureLandblockTerrain(TerrainFiles.Hillside);

        Assert.True(hillside.WaterCorners[(6 * 9) + 6]);
        Assert.False(hillside.WaterCorners[(5 * 9) + 6]);
        Assert.Equal(PluginTerrainWater.Entire, hillside.Cell(7, 7).Water);
        Assert.Equal(PluginTerrainWater.Partial, hillside.Cell(5, 6).Water);
        Assert.Equal(PluginTerrainWater.None, hillside.Cell(4, 4).Water);
        Assert.False(hillside.IsEntirelyWater);

        Assert.True(hillside.Sample(7.5f * 24f, 7.5f * 24f).IsWater);
        // The shore cell west of the lake: wet near its east side only.
        Assert.True(hillside.Sample((5f * 24f) + 20f, (6f * 24f) + 20f).IsWater);
        Assert.False(hillside.Sample((5f * 24f) + 4f, (6f * 24f) + 20f).IsWater);

        Assert.True(terrain.CaptureLandblockTerrain(TerrainFiles.OpenSea).IsEntirelyWater);
    }

    [Fact]
    public void ASampleMatchesThePhysicsEverywhere()
    {
        var files = new TerrainFiles();
        var terrain = new RuntimeTerrainAutomation();
        terrain.BindContent(files, files.Lock);
        PluginLandblockTerrain rough = terrain.CaptureLandblockTerrain(TerrainFiles.Rough);
        TerrainSurface physics = files.Surface(TerrainFiles.Rough);

        Assert.Contains(rough.Cells, static cell => cell.SplitSouthWestToNorthEast);
        Assert.Contains(rough.Cells, static cell => !cell.SplitSouthWestToNorthEast);
        Assert.Contains(rough.Cells, static cell => cell.Water == PluginTerrainWater.Partial);

        for (int step = 0; step < 4800; step++)
        {
            float x = (step % 80) * 2.4f + 0.37f;
            float y = (step / 80) * 3.2f + 0.11f;
            PluginTerrainSample sample = rough.Sample(x, y);
            TerrainSurfacePolygon expected = physics.SampleSurfacePolygon(x, y);

            Assert.True(MathF.Abs(expected.Z - sample.Height) < 0.001f, $"({x}, {y}): {expected.Z} vs {sample.Height}");
            Assert.True(Vector3.Distance(expected.Normal, sample.Normal) < 0.0001f, $"({x}, {y})");
            Assert.Equal(expected.Normal.Z >= PhysicsGlobals.FloorZ, sample.Walkable);
            Assert.Equal(physics.SampleWaterDepth(x, y) >= 0.45f, sample.IsWater);
            Assert.Equal(TerrainSurface.ComputeOutdoorCellId(TerrainFiles.Rough, x, y), sample.CellId);
        }
    }

    [Fact]
    public void TheWalkableSlopeIsThePhysicsOwn() =>
        Assert.Equal(PhysicsGlobals.FloorZ, PluginLandblockTerrain.WalkableNormalZ);

    [Fact]
    public void ALandblockIsReadOnceAndHandedOutUnchangeable()
    {
        var files = new TerrainFiles();
        var terrain = new RuntimeTerrainAutomation();
        terrain.BindContent(files, files.Lock);

        PluginLandblockTerrain first = terrain.CaptureLandblockTerrain(TerrainFiles.Hillside);
        int reads = files.Reads;
        Assert.Same(first, terrain.CaptureLandblockTerrain(TerrainFiles.Hillside | 0x0040u));
        Assert.Equal(reads, files.Reads);

        var writable = Assert.IsAssignableFrom<IList<float>>(first.Heights);
        Assert.Throws<NotSupportedException>(() => writable[0] = 1f);
        Assert.True(terrain.CaptureLandblockTerrain(0x45670000u).IsEmpty);
    }

    [Fact]
    public void BoundThroughTheSurfaceAPluginSeesTheSameGround()
    {
        var files = new TerrainFiles();
        using var surface = new RuntimeAutomationSurface();
        surface.BindTerrain(files, files.Lock);

        Assert.False(surface.Terrain.CaptureLandblockTerrain(TerrainFiles.Hillside).IsEmpty);
    }

    /// <summary>
    /// Game files with a height table of one metre per index and three
    /// landblocks: a hillside running flat, gently up, up a cliff and flat
    /// again from west to east with a lake in its north-east corner; rough
    /// ground of random heights and scattered water; and the open sea.
    /// </summary>
    private sealed class TerrainFiles : IDatObjectSource
    {
        public const uint Hillside = 0xA9B40000u;
        public const uint Rough = 0x7D640000u;
        public const uint OpenSea = 0x10100000u;
        public const float FlatHeight = 10f;
        public const float GentleTop = 20f;
        public const float CliffTop = 50f;
        public const byte GrassType = 1;
        public const byte WaterType = 0x10;

        private readonly Dictionary<uint, object> _objects = new();
        private readonly float[] _heightTable = Enumerable.Range(0, 256).Select(static i => (float)i).ToArray();

        public object Lock { get; } = new();
        public int Reads { get; private set; }
        public int ReadsOutsideTheLock { get; private set; }

        public TerrainFiles()
        {
            _objects[0x13000000u] = new Region { LandDefs = new DatReaderWriter.Types.LandDefs { LandHeightTable = _heightTable } };
            _objects[Hillside | 0xFFFFu] = Block(
                static (x, _) => x switch { <= 2 => FlatHeight, 3 => GentleTop, _ => CliffTop },
                static (x, y) => x >= 6 && y >= 6);
            var random = new Random(67);
            byte[] heights = Enumerable.Range(0, 81).Select(_ => (byte)random.Next(0, 60)).ToArray();
            bool[] water = Enumerable.Range(0, 81).Select(_ => random.Next(4) == 0).ToArray();
            _objects[Rough | 0xFFFFu] = Block((x, y) => heights[(x * 9) + y], (x, y) => water[(x * 9) + y]);
            _objects[OpenSea | 0xFFFFu] = Block(static (_, _) => 0f, static (_, _) => true);
        }

        public TerrainSurface Surface(uint landblock)
        {
            var block = (LandBlock)_objects[landblock | 0xFFFFu];
            byte[] types = block.Terrain.Select(static info => (byte)(ushort)info).ToArray();
            return new TerrainSurface(block.Height, _heightTable, (landblock >> 24) & 0xFFu, (landblock >> 16) & 0xFFu, types);
        }

        private static LandBlock Block(Func<int, int, float> height, Func<int, int, bool> water)
        {
            var block = new LandBlock { Height = new byte[81], Terrain = new TerrainInfo[81] };
            for (int x = 0; x < 9; x++)
            {
                for (int y = 0; y < 9; y++)
                {
                    block.Height[(x * 9) + y] = (byte)height(x, y);
                    block.Terrain[(x * 9) + y] = (TerrainInfo)(ushort)((water(x, y) ? WaterType : GrassType) << 2);
                }
            }
            return block;
        }

        [return: MaybeNull]
        public T Get<T>(uint fileId) where T : DatReaderWriter.Lib.IO.IDBObj
        {
            Reads++;
            if (!Monitor.IsEntered(Lock))
                ReadsOutsideTheLock++;
            return _objects.TryGetValue(fileId, out object? value) && value is T typed ? typed : default;
        }

        public bool TryGet<T>(uint fileId, [MaybeNullWhen(false)] out T value)
            where T : DatReaderWriter.Lib.IO.IDBObj
        {
            value = Get<T>(fileId);
            return value is not null;
        }
    }
}
