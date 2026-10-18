using System.Numerics;
using AcDream.Content;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Maps;
using AcDream.Runtime.Tests.Plugins;
using DatReaderWriter;
using DatReaderWriter.Options;
using Xunit.Abstractions;

namespace AcDream.Runtime.Tests.Maps;

/// <summary>
/// Doorways on real dungeons from the installed game files. A doorway is
/// authored on each side, so the cell graph is only right if every doorway
/// between two cells has a partner leading back. Most partners stand in the
/// same place, which is what proves the placement is read the way the game
/// places it. Not all: a big room's opening in its ceiling is listed whole
/// from below while each smaller cell above lists only its own piece of it,
/// so those partners are apart, but never by more than half a ten-metre
/// tile's diagonal.
/// </summary>
[Trait("Lane", "InstalledDat")]
public sealed class IndoorCellDoorwayInstalledDatTests
{
    /// <summary>Two sides of one doorway this close, in metres, stand in the same place.</summary>
    private const float SamePlaceMetres = 0.05f;

    /// <summary>The furthest a piece of a shared opening is from the whole: half a 10 m tile's diagonal.</summary>
    private const float SharedOpeningMetres = 7.08f;

    private readonly ITestOutputHelper _output;

    public IndoorCellDoorwayInstalledDatTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(0x8A020000u)]
    [InlineData(0x01250000u)]
    [InlineData(0x00070000u)]
    public void EveryDoorwayHasItsPartnerOnTheOtherSide(uint landblock)
    {
        string? datDirectory = InstalledDatTestPath.Resolve();
        if (datDirectory is null)
        {
            Assert.Fail("Lane=InstalledDat requires an installed retail DAT directory; see docs/release-gate.md.");
            return;
        }
        using var dats = new DatCollectionAdapter(new DatCollection(datDirectory, DatAccessType.Read));
        var map = new RuntimeDungeonMapAutomation();
        map.BindContent(dats, new object());

        IReadOnlyList<PluginIndoorCell> cells = map.CaptureIndoorCells(landblock);
        Assert.NotEmpty(cells);
        Dictionary<uint, PluginIndoorCell> byId = cells.ToDictionary(static cell => cell.CellId);

        int doorways = 0;
        int samePlace = 0;
        var unpartnered = new List<string>();
        foreach (PluginIndoorCell cell in cells)
        {
            foreach (PluginCellDoorway doorway in cell.Doorways)
            {
                doorways++;
                if (doorway.LeadsOutside)
                    continue;
                float nearest = float.MaxValue;
                if (byId.TryGetValue(doorway.OtherCellId, out PluginIndoorCell other))
                {
                    foreach (PluginCellDoorway back in other.Doorways)
                    {
                        if (back.OtherCellId == cell.CellId)
                            nearest = MathF.Min(nearest, Vector3.Distance(back.Center, doorway.Center));
                    }
                }
                if (nearest <= SamePlaceMetres)
                    samePlace++;
                if (nearest > SharedOpeningMetres)
                    unpartnered.Add($"0x{cell.CellId:X8} -> 0x{doorway.OtherCellId:X8} at {doorway.Center}");
            }
        }

        _output.WriteLine(
            $"0x{landblock:X8}: {cells.Count} cells, {doorways} doorways, {samePlace} with a partner in the same place");
        foreach (string line in unpartnered)
            _output.WriteLine($"  unpartnered {line}");
        Assert.True(doorways > 0);
        Assert.Empty(unpartnered);
        // Shared openings are the exception, not the rule.
        Assert.True(samePlace * 10 >= doorways * 9, $"{samePlace} of {doorways} in the same place");
    }
}
