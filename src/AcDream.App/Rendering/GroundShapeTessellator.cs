using System.Numerics;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Rendering;

/// <summary>
/// Cuts a plugin's ground shape into triangles lying on the ground. Pure:
/// the renderer hands it the shape's centre, the bearing its arc starts at
/// and whether (and how) to follow the land, and gets back three points per
/// triangle. Bearings are compass bearings: 0 is north (+Y), 90 east (+X),
/// increasing clockwise.
/// </summary>
internal static class GroundShapeTessellator
{
    /// <summary>About how long each piece of a shape's edge is, in metres.</summary>
    internal const float SegmentMeters = 0.5f;

    /// <summary>The fewest pieces a full turn is cut into.</summary>
    internal const int MinimumSegments = 16;

    /// <summary>The most pieces a full turn is cut into.</summary>
    internal const int MaximumSegments = 128;

    /// <summary>
    /// About how deep each ring of a filled shape is, in metres, so a large
    /// disc still has points on the land every few metres.
    /// </summary>
    internal const float FillRingMeters = 4f;

    /// <summary>The most rings a filled shape is cut into.</summary>
    internal const int MaximumFillRings = 8;

    /// <summary>How far above the land, or the anchor's height, a shape lies.</summary>
    internal const float LiftMeters = 0.05f;

    /// <summary>
    /// How many pieces an edge of <paramref name="radius"/> metres is cut into
    /// over <paramref name="sweepDegrees"/>: about every half metre, between
    /// <see cref="MinimumSegments"/> and <see cref="MaximumSegments"/> for a
    /// full turn, an arc taking its share and at least one.
    /// </summary>
    internal static int SegmentsFor(float radius, float sweepDegrees)
    {
        double full = Math.Clamp(
            Math.Ceiling(2d * Math.PI * radius / SegmentMeters), MinimumSegments, MaximumSegments);
        return (int)Math.Max(1d, Math.Ceiling(full * Math.Clamp(sweepDegrees, 0f, 360f) / 360d));
    }

    /// <summary>How many rings a filled shape of <paramref name="radius"/> metres is cut into.</summary>
    internal static int FillRingsFor(float radius) =>
        (int)Math.Clamp(Math.Ceiling(radius / FillRingMeters), 1d, MaximumFillRings);

    /// <summary>How many points <see cref="Tessellate"/> emits for this normalised shape.</summary>
    internal static int VertexCount(in PluginGroundShape shape)
    {
        int segments = SegmentsFor(shape.Radius, shape.SweepDegrees);
        return shape.Filled
            ? 3 * segments * ((2 * FillRingsFor(shape.Radius)) - 1)
            : 6 * segments;
    }

    /// <summary>
    /// Appends the triangles of <paramref name="shape"/> to
    /// <paramref name="triangles"/>, three points each. A band runs from
    /// <c>Radius - Width</c> to <c>Radius</c>; a filled shape is a fan round
    /// the centre and then bands out to the radius. Following the land, each
    /// point lies <see cref="LiftMeters"/> above <paramref name="terrain"/>'s
    /// height under it (or the centre's height where it has none); otherwise
    /// every point lies that far above the centre's height.
    /// </summary>
    /// <param name="shape">A shape the store has normalised.</param>
    /// <param name="centre">Where the shape is centred, in world metres.</param>
    /// <param name="startDegrees">The bearing the arc starts at, the object's heading already added.</param>
    /// <param name="followTerrain">Whether the shape follows the land.</param>
    /// <param name="terrain">The land's height at a world (x, y), or null where it is not known.</param>
    /// <param name="triangles">Receives three points per triangle.</param>
    internal static void Tessellate(
        in PluginGroundShape shape,
        Vector3 centre,
        float startDegrees,
        bool followTerrain,
        Func<float, float, float?> terrain,
        List<Vector3> triangles)
    {
        int segments = SegmentsFor(shape.Radius, shape.SweepDegrees);
        bool closed = shape.SweepDegrees >= 360f;
        Span<Vector2> directions = stackalloc Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            if (closed && i == segments)
            {
                // A full turn ends exactly where it began, so it closes without a seam.
                directions[i] = directions[0];
                break;
            }
            float radians = (startDegrees + (shape.SweepDegrees * i / segments)) * (MathF.PI / 180f);
            directions[i] = new Vector2(MathF.Sin(radians), MathF.Cos(radians));
        }

        Span<Vector3> inner = stackalloc Vector3[segments + 1];
        Span<Vector3> outer = stackalloc Vector3[segments + 1];
        if (!shape.Filled)
        {
            Row(shape.Radius - shape.Width, directions, centre, followTerrain, terrain, inner);
            Row(shape.Radius, directions, centre, followTerrain, terrain, outer);
            Band(inner, outer, triangles);
            return;
        }

        int rings = FillRingsFor(shape.Radius);
        Vector3 middle = Lift(centre.X, centre.Y, centre, followTerrain, terrain);
        Row(shape.Radius / rings, directions, centre, followTerrain, terrain, outer);
        for (int i = 0; i < segments; i++)
        {
            triangles.Add(middle);
            triangles.Add(outer[i]);
            triangles.Add(outer[i + 1]);
        }
        for (int ring = 2; ring <= rings; ring++)
        {
            outer.CopyTo(inner);
            Row(shape.Radius * ring / rings, directions, centre, followTerrain, terrain, outer);
            Band(inner, outer, triangles);
        }
    }

    private static void Row(
        float radius,
        ReadOnlySpan<Vector2> directions,
        Vector3 centre,
        bool followTerrain,
        Func<float, float, float?> terrain,
        Span<Vector3> row)
    {
        for (int i = 0; i < directions.Length; i++)
        {
            row[i] = Lift(
                centre.X + (directions[i].X * radius),
                centre.Y + (directions[i].Y * radius),
                centre,
                followTerrain,
                terrain);
        }
    }

    private static Vector3 Lift(float x, float y, Vector3 centre, bool followTerrain, Func<float, float, float?> terrain)
    {
        float ground = followTerrain && terrain(x, y) is { } z ? z : centre.Z;
        return new Vector3(x, y, ground + LiftMeters);
    }

    private static void Band(ReadOnlySpan<Vector3> inner, ReadOnlySpan<Vector3> outer, List<Vector3> triangles)
    {
        for (int i = 0; i + 1 < inner.Length; i++)
        {
            triangles.Add(inner[i]);
            triangles.Add(outer[i]);
            triangles.Add(outer[i + 1]);
            triangles.Add(inner[i]);
            triangles.Add(outer[i + 1]);
            triangles.Add(inner[i + 1]);
        }
    }
}
