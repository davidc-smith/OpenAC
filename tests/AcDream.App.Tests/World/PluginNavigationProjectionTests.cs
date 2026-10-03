using System.Numerics;
using AcDream.App.World;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.World;

/// <summary>
/// A plugin's navigation position in the client's world metres: map units of
/// 240 m, measured from the landblock the world is centred on.
/// </summary>
public sealed class PluginNavigationProjectionTests
{
    private static PluginNavigationPosition At(double eastWest, double northSouth, double elevation) =>
        new(0u, eastWest, northSouth, elevation, 0f, IsOutdoor: true);

    [Fact]
    public void TheOriginOfTheMapSitsInTheMiddleOfTheCentreLandblock()
    {
        Assert.Equal(new Vector3(84f, 84f, 0f), PluginNavigationProjection.ToWorld(At(0, 0, 0), 127, 127));
    }

    [Fact]
    public void OneMapUnitIsTwoHundredFortyMetresOnEveryAxis()
    {
        Assert.Equal(new Vector3(324f, -156f, 240f), PluginNavigationProjection.ToWorld(At(1, -1, 1), 127, 127));
    }

    [Fact]
    public void MovingTheCentreOneLandblockWestMovesEverythingOneLandblockEast()
    {
        Assert.Equal(new Vector3(276f, 84f, 0f), PluginNavigationProjection.ToWorld(At(0, 0, 0), 126, 127));
    }
}
