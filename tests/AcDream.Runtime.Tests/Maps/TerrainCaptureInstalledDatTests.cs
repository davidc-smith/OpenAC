using System.Globalization;
using AcDream.Content;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Maps;
using AcDream.Runtime.Tests.Plugins;
using DatReaderWriter;
using DatReaderWriter.Options;
using Xunit.Abstractions;

namespace AcDream.Runtime.Tests.Maps;

/// <summary>
/// The terrain capture on the installed game files: Holtburg's landblock has
/// ground, slopes too steep to stand on and no sea; a dungeon's landblock
/// reads as open sea; and somewhere in the world is open sea.
/// </summary>
[Trait("Lane", "InstalledDat")]
public sealed class TerrainCaptureInstalledDatTests
{
    private const uint Holtburg = 0xA9B40000u;
    private const uint Dungeon = 0x01250000u;

    private readonly ITestOutputHelper _output;

    public TerrainCaptureInstalledDatTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void RealLandblocksHaveGroundSlopesAndWater()
    {
        string? datDirectory = InstalledDatTestPath.Resolve();
        if (datDirectory is null)
        {
            Assert.Fail("Lane=InstalledDat requires an installed retail DAT directory; see docs/release-gate.md.");
            return;
        }
        using var dats = new DatCollectionAdapter(new DatCollection(datDirectory, DatAccessType.Read));
        var terrain = new RuntimeTerrainAutomation();
        terrain.BindContent(dats, new object());

        PluginLandblockTerrain holtburg = terrain.CaptureLandblockTerrain(Holtburg);
        Assert.False(holtburg.IsEmpty);
        Assert.False(holtburg.IsEntirelyWater);
        Report("Holtburg", holtburg);

        PluginLandblockTerrain dungeon = terrain.CaptureLandblockTerrain(Dungeon);
        Report("dungeon", dungeon);
        Assert.True(dungeon.IsEntirelyWater);

        int sea = 0, steep = 0, shore = 0;
        for (uint x = 0; x < 0xFF; x += 4)
        {
            for (uint y = 0; y < 0xFF; y += 4)
            {
                PluginLandblockTerrain block = terrain.CaptureLandblockTerrain((x << 24) | (y << 16));
                if (block.IsEntirelyWater) sea++;
                if (block.Cells.Any(static c => c.Water == PluginTerrainWater.Partial)) shore++;
                if (block.Cells.Any(static c => !c.First.Walkable || !c.Second.Walkable)) steep++;
            }
        }
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"sampled: sea={sea} shore={shore} steep={steep}"));
        Assert.True(sea > 0);
        Assert.True(shore > 0);
        Assert.True(steep > 0);
    }

    private void Report(string name, PluginLandblockTerrain block)
    {
        int unwalkable = block.Cells.Count(static c => !c.First.Walkable || !c.Second.Walkable);
        int water = block.Cells.Count(static c => c.Water != PluginTerrainWater.None);
        _output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{name} 0x{block.LandblockId:X8}: heights {block.Heights.DefaultIfEmpty().Min():0.0}..{block.Heights.DefaultIfEmpty().Max():0.0} m, unwalkable cells {unwalkable}, water cells {water}"));
    }
}
