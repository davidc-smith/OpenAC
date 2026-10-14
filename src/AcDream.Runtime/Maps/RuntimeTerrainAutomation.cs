using AcDream.Core.Content;
using AcDream.Core.Physics;
using AcDream.Plugin.Abstractions;
using DatReaderWriter.DBObjs;

namespace AcDream.Runtime.Maps;

/// <summary>
/// The plugin terrain API over the game files, handed out by every host: any
/// landblock's outdoor ground, read from the files rather than from what is
/// loaded, so a landblock far from the character answers too. Until the
/// files are bound it answers the inert default. The ground is cut into
/// triangles by the same <see cref="TerrainSurface"/> the physics stands
/// bodies on, so a plugin sees the slopes and water the character meets.
/// </summary>
internal sealed class RuntimeTerrainAutomation : ITerrainAutomation
{
    /// <summary>The region file holding the table that turns a corner's height index into metres.</summary>
    private const uint RegionFileId = 0x13000000u;

    /// <summary>
    /// How many landblocks' terrain is kept; the landblock read longest ago
    /// goes first once it is full.
    /// </summary>
    internal const int MaximumCachedLandblocks = 64;

    private readonly object _gate = new();
    private readonly Dictionary<uint, PluginLandblockTerrain> _terrain = new();
    private readonly Queue<uint> _order = new();
    private IDatObjectSource? _content;
    private object? _contentLock;
    private float[]? _heightTable;

    public void BindContent(IDatObjectSource content, object contentLock)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(contentLock);
        lock (_gate)
        {
            if (ReferenceEquals(_content, content) && ReferenceEquals(_contentLock, contentLock))
                return;
            _content = content;
            _contentLock = contentLock;
            _heightTable = null;
            _terrain.Clear();
            _order.Clear();
        }
    }

    public PluginLandblockTerrain CaptureLandblockTerrain(uint landblockId)
    {
        uint landblock = landblockId & 0xFFFF0000u;
        IDatObjectSource? content;
        object? contentLock;
        float[]? heightTable;
        lock (_gate)
        {
            if (_terrain.TryGetValue(landblock, out PluginLandblockTerrain? kept))
                return kept;
            content = _content;
            contentLock = _contentLock;
            heightTable = _heightTable;
        }
        if (content is null || contentLock is null)
            return PluginLandblockTerrain.Empty;

        LandBlock? block;
        lock (contentLock)
        {
            heightTable ??= content.Get<Region>(RegionFileId)?.LandDefs?.LandHeightTable;
            block = content.Get<LandBlock>(landblock | 0xFFFFu);
        }
        if (heightTable is null || heightTable.Length < 256)
            return PluginLandblockTerrain.Empty;

        PluginLandblockTerrain terrain = block is null
            ? PluginLandblockTerrain.Empty
            : Build(landblock, block, heightTable);
        lock (_gate)
        {
            // Content replaced while this was read: hand back what was read,
            // and keep nothing from the files that are gone.
            if (!ReferenceEquals(_content, content))
                return terrain;
            _heightTable ??= heightTable;
            if (terrain.IsEmpty)
                return terrain;
            if (_terrain.TryGetValue(landblock, out PluginLandblockTerrain? kept))
                return kept;
            if (_terrain.Count >= MaximumCachedLandblocks)
                _terrain.Remove(_order.Dequeue());
            _terrain[landblock] = terrain;
            _order.Enqueue(landblock);
            return terrain;
        }
    }

    /// <summary>
    /// The landblock's ground: the physics' own surface built from the
    /// stored corners, then each cell's two triangles read back from it at a
    /// point inside each, so the cut, the corners and the normals are the
    /// physics' exactly.
    /// </summary>
    internal static PluginLandblockTerrain Build(uint landblock, LandBlock block, float[] heightTable)
    {
        const int corners = PluginLandblockTerrain.CornersPerSide * PluginLandblockTerrain.CornersPerSide;
        if (block.Height is not { Length: >= corners } heightIndices
            || block.Terrain is not { Length: >= corners } terrainWords)
        {
            return PluginLandblockTerrain.Empty;
        }

        var terrainBytes = new byte[corners];
        var types = new byte[corners];
        for (int index = 0; index < corners; index++)
        {
            ushort word = (ushort)terrainWords[index];
            terrainBytes[index] = (byte)word;
            types[index] = (byte)((word >> 2) & 0x1F);
        }
        var surface = new TerrainSurface(
            heightIndices,
            heightTable,
            (landblock >> 24) & 0xFFu,
            (landblock >> 16) & 0xFFu,
            terrainBytes);

        var heights = new float[corners];
        var water = new bool[corners];
        for (int x = 0; x < PluginLandblockTerrain.CornersPerSide; x++)
        {
            for (int y = 0; y < PluginLandblockTerrain.CornersPerSide; y++)
            {
                int index = (x * PluginLandblockTerrain.CornersPerSide) + y;
                heights[index] = surface.CornerHeight(x, y);
                water[index] = surface.IsWaterCorner(x, y);
            }
        }

        const float size = PluginLandblockTerrain.CellSize;
        var cells = new PluginTerrainCell[PluginLandblockTerrain.CellsPerSide * PluginLandblockTerrain.CellsPerSide];
        for (int x = 0; x < PluginLandblockTerrain.CellsPerSide; x++)
        {
            for (int y = 0; y < PluginLandblockTerrain.CellsPerSide; y++)
            {
                bool split = surface.IsCellSplitSWtoNE(x, y);
                // A point well inside each triangle: below and above the
                // south-west to north-east diagonal for that cut, either side
                // of the other diagonal for the other.
                (float firstX, float firstY, float secondX, float secondY) = split
                    ? (0.75f, 0.25f, 0.25f, 0.75f)
                    : (0.25f, 0.25f, 0.75f, 0.75f);
                PluginTerrainTriangle first = Triangle(
                    surface.SampleSurfacePolygon((x + firstX) * size, (y + firstY) * size));
                PluginTerrainTriangle second = Triangle(
                    surface.SampleSurfacePolygon((x + secondX) * size, (y + secondY) * size));
                cells[(x * PluginLandblockTerrain.CellsPerSide) + y] = new PluginTerrainCell(
                    landblock | (uint)(1 + (x * PluginLandblockTerrain.CellsPerSide) + y),
                    (PluginTerrainWater)surface.CellWaterType(x, y),
                    split,
                    first,
                    second);
            }
        }

        // Read-only, because every plugin is handed the same terrain.
        return new PluginLandblockTerrain(
            landblock,
            Array.AsReadOnly(heights),
            Array.AsReadOnly(types),
            Array.AsReadOnly(water),
            Array.AsReadOnly(cells));
    }

    private static PluginTerrainTriangle Triangle(TerrainSurfacePolygon polygon) =>
        new(
            polygon.Vertices[0],
            polygon.Vertices[1],
            polygon.Vertices[2],
            polygon.Normal,
            polygon.Normal.Z >= PhysicsGlobals.FloorZ);
}
