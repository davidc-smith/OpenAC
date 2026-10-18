using System.Numerics;
using AcDream.Core.Physics;
using AcDream.Plugin.Abstractions;

namespace AcDream.Runtime.Navigation;

/// <summary>Positions in the client's cell frames as plugins' navigation positions.</summary>
public static class RuntimeNavigationPositions
{
    /// <summary>
    /// A point in <paramref name="cellId"/>'s landblock frame (metres, Z up) as a
    /// navigation position; a point past the landblock's edge lands in the
    /// neighbouring one.
    /// </summary>
    public static PluginNavigationPosition FromCellLocal(uint cellId, Vector3 origin) =>
        RuntimeNavigationProjection.Position(new Position(cellId, new CellFrame(origin, Quaternion.Identity)));
}
