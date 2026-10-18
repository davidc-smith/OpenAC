using System;
using AcDream.Core.Physics;
using Xunit;

namespace AcDream.Core.Tests.Physics;

/// <summary>
/// The terrain height sampler answers exactly as
/// <see cref="PhysicsEngine.SampleTerrainZ"/> does, however the resident
/// landblocks change between samples.
/// </summary>
public class TerrainHeightSamplerTests
{
    private const uint West = 0xAAAA0000u;
    private const uint East = 0xAAAB0000u;

    private static float[] LinearHeightTable()
    {
        var table = new float[256];
        for (int i = 0; i < 256; i++) table[i] = i * 1.0f;
        return table;
    }

    private static TerrainSurface Flat(byte height)
    {
        var heights = new byte[81];
        Array.Fill(heights, height);
        return new TerrainSurface(heights, LinearHeightTable());
    }

    private static TerrainSurface RisingEast(byte from)
    {
        var heights = new byte[81];
        for (int x = 0; x < 9; x++)
        for (int y = 0; y < 9; y++)
            heights[x * 9 + y] = (byte)(from + (x * 3));
        return new TerrainSurface(heights, LinearHeightTable());
    }

    private static void Add(PhysicsEngine engine, uint id, TerrainSurface terrain, float offsetX) =>
        engine.AddLandblock(id, terrain, Array.Empty<CellSurface>(), Array.Empty<PortalPlane>(),
            worldOffsetX: offsetX, worldOffsetY: 0f);

    [Fact]
    public void ItAnswersAsTheEngineDoesAcrossLandblocksAndOffThem()
    {
        var engine = new PhysicsEngine();
        Add(engine, West, RisingEast(10), 0f);
        Add(engine, East, RisingEast(60), 192f);
        PhysicsEngine.TerrainHeightSampler sampler = engine.CreateTerrainHeightSampler();

        // Back and forth over the seam at x = 192, and out past both ends.
        for (float x = -20f; x <= 404f; x += 7.25f)
        for (float y = -10f; y <= 200f; y += 30f)
            Assert.Equal(engine.SampleTerrainZ(x, y), sampler.SampleZ(x, y));
        for (float x = 404f; x >= -20f; x -= 7.25f)
            Assert.Equal(engine.SampleTerrainZ(x, 96f), sampler.SampleZ(x, 96f));
        Assert.Equal(engine.SampleTerrainZ(192f, 96f), sampler.SampleZ(192f, 96f));
    }

    [Fact]
    public void ALandblockReplacedSinceTheLastSampleIsReadAfresh()
    {
        var engine = new PhysicsEngine();
        Add(engine, West, Flat(10), 0f);
        PhysicsEngine.TerrainHeightSampler sampler = engine.CreateTerrainHeightSampler();
        Assert.Equal(10f, sampler.SampleZ(96f, 96f));

        Add(engine, West, Flat(40), 0f);

        Assert.Equal(40f, sampler.SampleZ(96f, 96f));
    }

    [Fact]
    public void ALandblockRemovedSinceTheLastSampleIsNoLongerRead()
    {
        var engine = new PhysicsEngine();
        Add(engine, West, Flat(10), 0f);
        PhysicsEngine.TerrainHeightSampler sampler = engine.CreateTerrainHeightSampler();
        Assert.Equal(10f, sampler.SampleZ(96f, 96f));

        engine.RemoveLandblock(West);

        Assert.Null(sampler.SampleZ(96f, 96f));
    }

    [Fact]
    public void ALandblockMovedSinceTheLastSampleIsReadAtItsNewPlace()
    {
        var engine = new PhysicsEngine();
        TerrainSurface land = Flat(10);
        Add(engine, West, land, 0f);
        PhysicsEngine.TerrainHeightSampler sampler = engine.CreateTerrainHeightSampler();
        Assert.Equal(10f, sampler.SampleZ(96f, 96f));

        // The same terrain re-registered a landblock east, as when the world's origin moves.
        Add(engine, West, land, 192f);

        Assert.Null(sampler.SampleZ(96f, 96f));
        Assert.Equal(10f, sampler.SampleZ(288f, 96f));
    }
}
