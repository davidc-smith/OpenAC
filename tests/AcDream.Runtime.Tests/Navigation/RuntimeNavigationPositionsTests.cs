using System.Numerics;
using AcDream.Core.Physics;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Navigation;

namespace AcDream.Runtime.Tests.Navigation;

public sealed class RuntimeNavigationPositionsTests
{
    [Theory]
    [InlineData(0xA9B40001u)] // outdoor cell
    [InlineData(0x01D90105u)] // dungeon cell
    public void FromCellLocalIsTheNavigationProjectionOfThatPoint(uint cellId)
    {
        var origin = new Vector3(12f, 34f, 5f);

        PluginNavigationPosition actual = RuntimeNavigationPositions.FromCellLocal(cellId, origin);
        PluginNavigationPosition expected = RuntimeNavigationProjection.Position(
            new Position(cellId, new CellFrame(origin, Quaternion.Identity)));

        Assert.Equal(expected, actual);
    }
}
