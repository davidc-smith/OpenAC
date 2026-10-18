using System.Numerics;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.World;

/// <summary>
/// A plugin's navigation position in the client's world metres. Map units
/// are 240 m; the world is measured from the landblock it is centred on, and
/// a landblock is 192 m with the map's origin 84 m into it.
/// </summary>
internal static class PluginNavigationProjection
{
    internal static Vector3 ToWorld(PluginNavigationPosition position, int centerX, int centerY) => new(
        (float)(position.EastWest * 240d + (127 - centerX) * 192d + 84d),
        (float)(position.NorthSouth * 240d + (127 - centerY) * 192d + 84d),
        (float)(position.Elevation * 240d));
}
