using System.Collections.ObjectModel;
using System.Numerics;

namespace AcDream.Plugin.Abstractions;

/// <summary>How much of an outdoor cell lies under water.</summary>
public enum PluginTerrainWater : byte
{
    /// <summary>None of its corners is water.</summary>
    None = 0,

    /// <summary>Some of its corners are water: a shore.</summary>
    Partial = 1,

    /// <summary>All four corners are water.</summary>
    Entire = 2,
}

/// <summary>
/// One of the two triangles an outdoor cell's ground is made of, in
/// landblock-local metres (see <see cref="PluginLandblockTerrain"/>).
/// </summary>
/// <param name="A">The first corner.</param>
/// <param name="B">The second corner.</param>
/// <param name="C">The third corner.</param>
/// <param name="Normal">The unit normal, pointing up.</param>
/// <param name="Walkable">
/// Whether a body can stand on it: the normal is at least
/// <see cref="PluginLandblockTerrain.WalkableNormalZ"/> upright, the same
/// rule the physics applies to every floor. Water is not taken into account.
/// </param>
public readonly record struct PluginTerrainTriangle(
    Vector3 A,
    Vector3 B,
    Vector3 C,
    Vector3 Normal,
    bool Walkable);

/// <summary>
/// One 24-metre outdoor cell of a landblock: its id, its water and the two
/// triangles its ground is split into.
/// </summary>
/// <param name="CellId">The full cell id, landblock in the high half; the low half runs 1 to 64.</param>
/// <param name="Water">How much of the cell is under water.</param>
/// <param name="SplitSouthWestToNorthEast">
/// True when the cell is cut along the diagonal from its south-west corner to
/// its north-east one; false when it is cut from south-east to north-west.
/// The game picks the cut per cell; it is not the same everywhere.
/// </param>
/// <param name="First">
/// The south-east triangle of a south-west to north-east cut, or the
/// south-west triangle of the other cut.
/// </param>
/// <param name="Second">The other triangle.</param>
public readonly record struct PluginTerrainCell(
    uint CellId,
    PluginTerrainWater Water,
    bool SplitSouthWestToNorthEast,
    PluginTerrainTriangle First,
    PluginTerrainTriangle Second);

/// <summary>
/// The ground at one point of a landblock.
/// </summary>
/// <param name="Height">The ground's height in metres.</param>
/// <param name="Normal">The unit normal of the triangle the point is on.</param>
/// <param name="Walkable">Whether that triangle is level enough to stand on.</param>
/// <param name="IsWater">
/// Whether the point is in water as the physics counts it: anywhere in a cell
/// that is all water, and in a shore cell where the corner nearest the point
/// is water.
/// </param>
/// <param name="CellId">The full id of the outdoor cell the point is in.</param>
public readonly record struct PluginTerrainSample(
    float Height,
    Vector3 Normal,
    bool Walkable,
    bool IsWater,
    uint CellId);

/// <summary>
/// One landblock's outdoor ground as the game data authors it: a 9 by 9 grid
/// of corner heights 24 metres apart, cut into 8 by 8 cells of two triangles
/// each. Everything is in the landblock's own frame and in metres: x grows
/// east and y grows north from the landblock's south-west corner, z up, the
/// frame <see cref="PluginDungeonFloorplan"/> also uses. Only the ground is
/// described: buildings, rocks and trees standing on it are not. The terrain
/// is built once and never changed, so it is safe to keep and to read from
/// any thread.
/// </summary>
/// <param name="LandblockId">The landblock, with a zero low half.</param>
/// <param name="Heights">
/// The 81 corner heights in metres, corner (x, y) at index x * 9 + y, x
/// counted east and y north from 0 to 8, the order the data stores them in.
/// </param>
/// <param name="TerrainTypes">
/// The 81 corners' terrain types (0 to 31, grass, sand, water and so on, as
/// the region names them), in the same order.
/// </param>
/// <param name="WaterCorners">Whether each of the 81 corners is water, in the same order.</param>
/// <param name="Cells">The 64 cells, cell (x, y) at index x * 8 + y, so the cell with low id n is at index n - 1.</param>
public sealed record PluginLandblockTerrain(
    uint LandblockId,
    IReadOnlyList<float> Heights,
    IReadOnlyList<byte> TerrainTypes,
    IReadOnlyList<bool> WaterCorners,
    IReadOnlyList<PluginTerrainCell> Cells)
{
    /// <summary>Corners along each side of a landblock.</summary>
    public const int CornersPerSide = 9;

    /// <summary>Cells along each side of a landblock.</summary>
    public const int CellsPerSide = 8;

    /// <summary>The side of a cell in metres.</summary>
    public const float CellSize = 24f;

    /// <summary>The side of a landblock in metres.</summary>
    public const float LandblockSize = 192f;

    /// <summary>
    /// How upright a surface's unit normal has to be to stand on: its z at
    /// least this, which is a slope of at most about 48 degrees. The physics
    /// applies the same number to every floor.
    /// </summary>
    public const float WalkableNormalZ = 0.6642f;

    /// <summary>
    /// The terrain of nothing: what comes back for a landblock the data does
    /// not have and on a host that cannot read the game data.
    /// </summary>
    public static PluginLandblockTerrain Empty { get; } = new(
        0u,
        ReadOnlyCollection<float>.Empty,
        ReadOnlyCollection<byte>.Empty,
        ReadOnlyCollection<bool>.Empty,
        ReadOnlyCollection<PluginTerrainCell>.Empty);

    /// <summary>True when there is no ground to read.</summary>
    public bool IsEmpty => Cells.Count == 0;

    /// <summary>True when every cell is all water: the open sea, which nothing walks into.</summary>
    public bool IsEntirelyWater
    {
        get
        {
            if (IsEmpty)
                return false;
            foreach (PluginTerrainCell cell in Cells)
            {
                if (cell.Water != PluginTerrainWater.Entire)
                    return false;
            }
            return true;
        }
    }

    /// <summary>The height of corner (x, y), each 0 to 8.</summary>
    /// <param name="x">Corners east of the south-west corner.</param>
    /// <param name="y">Corners north of the south-west corner.</param>
    public float CornerHeight(int x, int y) => Heights[(x * CornersPerSide) + y];

    /// <summary>Cell (x, y), each 0 to 7.</summary>
    /// <param name="x">Cells east of the south-west cell.</param>
    /// <param name="y">Cells north of the south-west cell.</param>
    public PluginTerrainCell Cell(int x, int y) => Cells[(x * CellsPerSide) + y];

    /// <summary>
    /// The ground at a landblock-local point, on the same triangle and with
    /// the same height the physics finds there. A point outside the landblock
    /// is read at the nearest point on its edge. The default sample comes
    /// back when the terrain is empty.
    /// </summary>
    /// <param name="x">Metres east of the landblock's south-west corner.</param>
    /// <param name="y">Metres north of the landblock's south-west corner.</param>
    public PluginTerrainSample Sample(float x, float y)
    {
        if (IsEmpty)
            return default;

        float fx = Math.Clamp(x / CellSize, 0f, CellsPerSide - 0.001f);
        float fy = Math.Clamp(y / CellSize, 0f, CellsPerSide - 0.001f);
        int cellX = Math.Clamp((int)fx, 0, CellsPerSide - 1);
        int cellY = Math.Clamp((int)fy, 0, CellsPerSide - 1);
        float tx = fx - cellX;
        float ty = fy - cellY;

        PluginTerrainCell cell = Cell(cellX, cellY);
        bool first = cell.SplitSouthWestToNorthEast ? tx > ty : tx + ty <= 1f;
        PluginTerrainTriangle triangle = first ? cell.First : cell.Second;

        // The plane through the triangle, read at the clamped point.
        float px = fx * CellSize;
        float py = fy * CellSize;
        Vector3 n = triangle.Normal;
        float height = triangle.A.Z
            - (((n.X * (px - triangle.A.X)) + (n.Y * (py - triangle.A.Y))) / n.Z);

        bool isWater = cell.Water switch
        {
            PluginTerrainWater.Entire => true,
            PluginTerrainWater.Partial => WaterCorners[
                ((cellX + (tx >= 0.5f ? 1 : 0)) * CornersPerSide) + cellY + (ty >= 0.5f ? 1 : 0)],
            _ => false,
        };

        return new PluginTerrainSample(height, n, triangle.Walkable, isWater, cell.CellId);
    }

    /// <summary>The ground at a landblock-local point; see <see cref="Sample(float, float)"/>.</summary>
    /// <param name="point">Metres east and north of the landblock's south-west corner.</param>
    public PluginTerrainSample Sample(Vector2 point) => Sample(point.X, point.Y);

    /// <summary>
    /// A position in this terrain's frame, the same conversion
    /// <see cref="PluginDungeonFloorplan.ToLandblockLocal"/> makes. Compare
    /// the position's landblock with <see cref="LandblockId"/> first.
    /// </summary>
    /// <param name="position">A position as the navigation surface reports it.</param>
    public static Vector3 ToLandblockLocal(in PluginNavigationPosition position) =>
        PluginDungeonFloorplan.ToLandblockLocal(position);
}

/// <summary>
/// The outdoor ground: height, slope and water, read from the game data for
/// any landblock, loaded near the character or not.
/// </summary>
public interface ITerrainAutomation
{
    /// <summary>
    /// A landblock's outdoor ground, read from the game data the first time
    /// it is asked for and the same object every time after while it is kept.
    /// Any cell id in the landblock will do. A sealed dungeon's landblock
    /// has a terrain record too, usually open sea at height 0; it says
    /// nothing about the dungeon. <see cref="PluginLandblockTerrain.Empty"/>
    /// comes back for a landblock the data does not have and on a host that
    /// cannot read the game data, which is what the default implementation
    /// always returns.
    /// </summary>
    /// <param name="landblockId">The landblock, or any cell in it.</param>
    PluginLandblockTerrain CaptureLandblockTerrain(uint landblockId) => PluginLandblockTerrain.Empty;
}
