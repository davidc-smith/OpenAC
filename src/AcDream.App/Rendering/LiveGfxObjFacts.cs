using System.Numerics;
using AcDream.Core.Meshing;
using DatReaderWriter.DBObjs;

namespace AcDream.App.Rendering;

/// <summary>
/// What an arriving object's appearance needs to know about each of its
/// part models, read from the game files once per model and kept: whether
/// the model exists, its bounds, the model it shows up close, its surfaces
/// and their original textures. The game files' own object cache holds only a
/// few hundred objects and a trip's landblocks push the common body parts out
/// of it, so without this every arriving creature read and decoded its part
/// models again on the update thread. The facts never change while the files
/// are open.
/// </summary>
internal sealed class LiveGfxObjFacts
{
    private readonly Func<uint, GfxObj?> _gfxObjs;
    private readonly Func<uint, GfxObjDegradeInfo?> _degrades;
    private readonly Func<uint, Surface?> _surfaces;
    private readonly Func<uint, bool> _collisionPublished;
    private readonly Action<uint, GfxObj> _publishCollision;
    private readonly Dictionary<uint, Part> _parts = [];
    private readonly Dictionary<uint, (bool Found, uint ResolvedId)> _close = [];
    private readonly Dictionary<uint, uint> _originalTextures = [];

    private readonly record struct Part(
        bool Exists,
        (Vector3 Min, Vector3 Max)? Bounds,
        uint[] Surfaces);

    /// <param name="collisionPublished">Whether a model's collision is in
    /// the physics cache already.</param>
    /// <param name="publishCollision">Puts a model's collision in the physics
    /// cache.</param>
    public LiveGfxObjFacts(
        Func<uint, GfxObj?> gfxObjs,
        Func<uint, GfxObjDegradeInfo?> degrades,
        Func<uint, Surface?> surfaces,
        Func<uint, bool> collisionPublished,
        Action<uint, GfxObj> publishCollision)
    {
        _gfxObjs = gfxObjs ?? throw new ArgumentNullException(nameof(gfxObjs));
        _degrades = degrades ?? throw new ArgumentNullException(nameof(degrades));
        _surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
        _collisionPublished = collisionPublished
            ?? throw new ArgumentNullException(nameof(collisionPublished));
        _publishCollision = publishCollision
            ?? throw new ArgumentNullException(nameof(publishCollision));
    }

    /// <summary>
    /// Whether the model exists; when it does, its collision is published and
    /// <paramref name="bounds"/> are its vertex bounds (null with no vertices).
    /// </summary>
    public bool TryGetPart(uint gfxObjId, out (Vector3 Min, Vector3 Max)? bounds)
    {
        Part part = GetPart(gfxObjId, out GfxObj? read);
        bounds = part.Bounds;
        if (part.Exists)
            EnsureCollision(gfxObjId, read);
        return part.Exists;
    }

    /// <summary>
    /// The model a part shows up close: false when the part model does not
    /// exist; otherwise <paramref name="resolvedId"/> is the close model, or
    /// the part itself when it has none, and that model's collision is
    /// published.
    /// </summary>
    public bool TryResolveClose(uint gfxObjId, out uint resolvedId)
    {
        GfxObj? read = null;
        if (!_close.TryGetValue(gfxObjId, out (bool Found, uint ResolvedId) close))
        {
            bool found = GfxObjDegradeResolver.TryResolveCloseGfxObj(
                _gfxObjs,
                _degrades,
                gfxObjId,
                out uint resolved,
                out read);
            close = (found, resolved);
            _close[gfxObjId] = close;
        }
        resolvedId = close.ResolvedId;
        if (close.Found)
            EnsureCollision(close.ResolvedId, read);
        return close.Found;
    }

    /// <summary>The model's surfaces; empty when the model does not exist.
    /// The model's collision is published when it exists.</summary>
    public IReadOnlyList<uint> Surfaces(uint gfxObjId)
    {
        Part part = GetPart(gfxObjId, out GfxObj? read);
        if (part.Exists)
            EnsureCollision(gfxObjId, read);
        return part.Surfaces;
    }

    /// <summary>A surface's original texture; 0 when the surface does not
    /// exist.</summary>
    public uint OriginalTexture(uint surfaceId)
    {
        if (!_originalTextures.TryGetValue(surfaceId, out uint texture))
        {
            texture = _surfaces(surfaceId) is { } surface
                ? (uint)surface.OrigTextureId
                : 0u;
            _originalTextures[surfaceId] = texture;
        }
        return texture;
    }

    private Part GetPart(uint gfxObjId, out GfxObj? read)
    {
        read = null;
        if (_parts.TryGetValue(gfxObjId, out Part part))
            return part;
        read = _gfxObjs(gfxObjId);
        part = read is null
            ? new Part(false, null, [])
            : new Part(
                true,
                GfxObjBounds.Get(read),
                [.. read.Surfaces.Select(static surface => (uint)surface)]);
        _parts[gfxObjId] = part;
        return part;
    }

    private void EnsureCollision(uint gfxObjId, GfxObj? read)
    {
        if (_collisionPublished(gfxObjId))
            return;
        GfxObj? model = read is not null && read.Id == gfxObjId
            ? read
            : _gfxObjs(gfxObjId);
        if (model is not null)
            _publishCollision(gfxObjId, model);
    }
}
