using AcDream.Plugin.Abstractions;

namespace AcDream.HostParity.Tests;

/// <summary>
/// A plugin reading the ground the character stands on. The area is the
/// runtime's own on both clients and, with no lease on the game files in
/// either arm, the terrain is answered the same inert way.
/// </summary>
public sealed class TerrainParityTests
{
    [Fact]
    public void TheTerrainIsAnsweredTheSameOnBothClients() =>
        ParityScenario.Run(static (arm, transcript) =>
        {
            _ = ParityWorld.Stage(arm);
            ITerrainAutomation terrain = arm.Host.Automation.Terrain;

            transcript.Step("the ground, without the files to read");
            PluginLandblockTerrain ground = terrain.CaptureLandblockTerrain(ParityPlayerBody.Cell);
            transcript.Record("empty", ground.IsEmpty);
            transcript.Record("cells", ground.Cells.Count);
            Assert.True(ground.IsEmpty);
            Assert.Empty(ground.Cells);
        });
}
