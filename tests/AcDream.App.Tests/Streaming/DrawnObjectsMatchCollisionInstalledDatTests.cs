using System.Globalization;
using System.Numerics;
using AcDream.App.Streaming;
using AcDream.App.Tests.Rendering;
using AcDream.Content;
using AcDream.Core.World;
using DatReaderWriter.DBObjs;
using Xunit.Abstractions;

namespace AcDream.App.Tests.Streaming;

/// <summary>
/// A landblock the window draws carries no collision of its own: its static
/// objects find their collision, built by the session host, by id. So the
/// objects the window draws must be the very objects the host builds
/// collision for, under the same ids and in the same places: a town with
/// buildings, a dungeon, and the landblocks the navigation examples use.
/// </summary>
[Trait("Lane", "InstalledDat")]
public sealed class DrawnObjectsMatchCollisionInstalledDatTests
{
    private readonly ITestOutputHelper _output;

    public DrawnObjectsMatchCollisionInstalledDatTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(0x7E03FFFFu)]
    [InlineData(0xA9B4FFFFu)]
    [InlineData(0x0156FFFFu)]
    [InlineData(0x7D64FFFFu)]
    public void TheObjectsALandblockDrawsAreTheObjectsItsCollisionIsBuiltFor(uint landblockId)
    {
        string? datDirectory = InstalledDatTestPath.Resolve();
        if (datDirectory is null)
        {
            Assert.Fail("Lane=InstalledDat requires an installed retail DAT directory; see docs/release-gate.md.");
            return;
        }

        using var dats = new BoundedTestDatCollection(datDirectory);
        var bounded = (IDatReaderWriter)dats;
        float[] heights = Assert.IsType<Region>(bounded.Get<Region>(0x13000000u)).LandDefs.LandHeightTable;
        var origin = new LandblockBuildOrigin((int)((landblockId >> 24) & 0xFFu), (int)((landblockId >> 16) & 0xFFu));

        LandblockBuild drawn = Assert.IsType<LandblockBuild>(
            new LandblockBuildFactory(bounded, new object(), heights).Build(
                new LandblockBuildRequest(landblockId, LandblockStreamJobKind.LoadNear, Generation: 1, origin)));
        using var collisionSource = new AcDream.App.Tests.Navigation.InstalledDatCollisionSource(bounded);
        Assert.True(LandblockPhysicsContentBuilder.TryLoadCollisionLandblock(
            bounded,
            collisionSource,
            heights,
            landblockId,
            Vector3.Zero,
            out LoadedLandblock collision,
            out _));

        string drawnObjects = Objects(drawn.Landblock.Entities);
        string collisionObjects = Objects(collision.Entities);
        _output.WriteLine($"drawn:     {drawn.Landblock.Entities.Count} objects");
        _output.WriteLine($"collision: {collision.Entities.Count} objects");
        Assert.Equal(drawnObjects, collisionObjects);
    }

    /// <summary>Every object by id, what it is made from and where it stands, in id order.</summary>
    private static string Objects(IEnumerable<WorldEntity> entities) =>
        string.Join(
            "\n",
            entities
                .OrderBy(entity => entity.Id)
                .Select(entity => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{entity.Id:X8} {entity.SourceGfxObjOrSetupId:X8} shell={entity.IsBuildingShell} "
                    + $"{entity.Position.X:0.000},{entity.Position.Y:0.000},{entity.Position.Z:0.000} "
                    + $"cell={entity.ParentCellId ?? 0u:X8} parts={entity.MeshRefs.Count}")));
}
