using System.Numerics;
using System.Runtime.InteropServices;
using AcDream.App.Interaction;
using AcDream.App.Rendering.Gpu;
using AcDream.App.World;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Rendering;

/// <summary>
/// Draws the shapes plugins lay on the ground -- rings, discs and wedges --
/// as see-through triangles in the world pass, after the world, so walls
/// and hills hide them.
/// </summary>
/// <remarks>
/// Bounded here, not by the plugin: a shape whose centre is more than
/// <see cref="DrawRangeMeters"/> from the camera, or that lies wholly outside
/// its view, is skipped, and the triangles of one frame never exceed the
/// batch's budget. When the shapes in range and in view would, the nearest
/// are drawn and the farthest are left out.
/// </remarks>
internal sealed class PluginGroundShapeRenderer(
    Func<IReadOnlyList<PluginGroundShape>> shapes,
    GroundShapeBatch batch,
    IWorldFrameCameraSource camera,
    LiveWorldOriginState origin,
    Func<uint, WorldLabelAnchor?> objects,
    Func<float, float, float?> terrain)
{
    // The same reach as plugins' world lines.
    internal const float DrawRangeMeters = PluginWorldLineRenderer.DrawRangeMeters;

    // A shape follows the land only when its anchor is in an outdoor cell and
    // no more than this far above the land at its centre (at or below the land
    // counts). Anything else (a floor, a roof, a bridge, a dungeon) gets a
    // flat shape at the anchor's height.
    internal const float OnTheLandMeters = 1f;

    // Each point moves this fraction of the way toward the camera. Along the
    // line of sight it stays at the same place on the screen; only its depth
    // comes forward, so the land it lies on doesn't flicker through it.
    internal const float DepthNudge = 0.002f;

    // How far the land under a shape that follows it may rise or fall per
    // metre out from its centre, for the box the view is tested against:
    // steeper than any slope the land has but a cliff.
    internal const float LandSlopeAllowance = 2f;

    private readonly List<PlannedShape> _planned = [];
    private readonly List<Vector3> _triangles = [];

    public void Render(IGpuPassEncoder encoder, int width, int height)
    {
        if (!origin.IsKnown || width <= 0 || height <= 0)
            return;
        IReadOnlyList<PluginGroundShape> all = shapes();
        if (all.Count == 0)
            return;
        WorldCameraFrame frame = camera.Resolve();
        _planned.Clear();
        long vertices = 0;
        for (int i = 0; i < all.Count; i++)
        {
            PluginGroundShape shape = all[i];
            if (!TryPlace(shape, objects, terrain, origin.CenterX, origin.CenterY, out GroundShapePlacement placement))
                continue;
            float distanceSquared = Vector3.DistanceSquared(placement.Centre, frame.Position);
            if (!(distanceSquared <= DrawRangeMeters * DrawRangeMeters))
                continue;
            if (!IsInView(frame.Frustum, placement, shape.Radius))
                continue;
            int count = GroundShapeTessellator.VertexCount(shape);
            _planned.Add(new PlannedShape(shape, placement, distanceSquared, count));
            vertices += count;
        }
        if (_planned.Count == 0)
            return;

        batch.Begin();
        // Only when the shapes do not all fit is the order worth a sort.
        if (vertices > batch.RemainingVertexBudget)
            _planned.Sort(static (a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));
        foreach (ref readonly PlannedShape planned in CollectionsMarshal.AsSpan(_planned))
        {
            if (planned.VertexCount > batch.RemainingVertexBudget)
                break;
            Draw(in planned, frame.Position);
        }
        _planned.Clear();
        batch.Flush(encoder, frame.ViewProjection, width, height);
    }

    /// <summary>
    /// Where a shape lies this frame: around an object's base, its start
    /// turned by the object's heading when it faces with the object, or
    /// around a fixed position. False when the anchor is not in the world.
    /// </summary>
    internal static bool TryPlace(
        in PluginGroundShape shape,
        Func<uint, WorldLabelAnchor?> objects,
        Func<float, float, float?> terrain,
        int centerX,
        int centerY,
        out GroundShapePlacement placement)
    {
        placement = default;
        switch (shape.Anchor.Kind)
        {
            case PluginMarkerAnchorKind.Object:
            {
                if (objects(shape.Anchor.ObjectId) is not { } anchor)
                    return false;
                Vector3 feet = anchor.BasePosition;
                if (!float.IsFinite(feet.X + feet.Y + feet.Z))
                    return false;
                bool onTheLand = anchor.IsOutdoor && StandsOnTheLand(feet, terrain);
                float start = shape.FacesObject
                    ? shape.StartDegrees + anchor.HeadingDegrees
                    : shape.StartDegrees;
                placement = new GroundShapePlacement(feet, start, onTheLand);
                return true;
            }
            case PluginMarkerAnchorKind.Position:
            {
                Vector3 spot = PluginNavigationProjection.ToWorld(shape.Anchor.Position, centerX, centerY);
                if (!float.IsFinite(spot.X + spot.Y + spot.Z))
                    return false;
                bool onTheLand = shape.Anchor.Position.IsOutdoor && StandsOnTheLand(spot, terrain);
                placement = new GroundShapePlacement(spot, shape.StartDegrees, onTheLand);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Whether any of a shape of <paramref name="radius"/> metres lying at
    /// <paramref name="placement"/> may be in the camera's view: a box round
    /// it, as deep as the land under it could rise or fall, touches the
    /// frustum.
    /// </summary>
    internal static bool IsInView(in FrustumPlanes frustum, in GroundShapePlacement placement, float radius)
    {
        float rise = placement.FollowTerrain
            ? (radius * LandSlopeAllowance) + GroundShapeTessellator.LiftMeters
            : GroundShapeTessellator.LiftMeters;
        var reach = new Vector3(radius, radius, rise);
        return FrustumCuller.IsAabbVisible(frustum, placement.Centre - reach, placement.Centre + reach);
    }

    // No more than OnTheLandMeters above the sampled land at the centre.
    private static bool StandsOnTheLand(Vector3 centre, Func<float, float, float?> terrain) =>
        terrain(centre.X, centre.Y) is { } ground && centre.Z - ground <= OnTheLandMeters;

    /// <summary><paramref name="point"/> moved <see cref="DepthNudge"/> of the way toward <paramref name="camera"/>.</summary>
    internal static Vector3 TowardCamera(Vector3 point, Vector3 camera) =>
        point + ((camera - point) * DepthNudge);

    private void Draw(in PlannedShape planned, Vector3 cameraPosition)
    {
        _triangles.Clear();
        GroundShapeTessellator.Tessellate(
            planned.Shape,
            planned.Placement.Centre,
            planned.Placement.StartDegrees,
            planned.Placement.FollowTerrain,
            terrain,
            _triangles);
        PluginColor c = planned.Shape.Color;
        var color = new Vector4(c.R, c.G, c.B, c.A) / 255f;
        ReadOnlySpan<Vector3> points = CollectionsMarshal.AsSpan(_triangles);
        for (int i = 0; i + 2 < points.Length; i += 3)
        {
            batch.AddTriangle(
                TowardCamera(points[i], cameraPosition),
                TowardCamera(points[i + 1], cameraPosition),
                TowardCamera(points[i + 2], cameraPosition),
                color);
        }
    }

    private readonly record struct PlannedShape(
        PluginGroundShape Shape,
        GroundShapePlacement Placement,
        float DistanceSquared,
        int VertexCount);
}

/// <summary>
/// Where one ground shape lies this frame: its centre in world metres, the
/// bearing its arc starts at (the object's heading already added), and
/// whether it follows the land (its anchor is in an outdoor cell and no more
/// than <see cref="PluginGroundShapeRenderer.OnTheLandMeters"/> above the land
/// at the centre) or lies flat at the centre's height.
/// </summary>
internal readonly record struct GroundShapePlacement(Vector3 Centre, float StartDegrees, bool FollowTerrain);
