using System.Numerics;
using System.Runtime.InteropServices;
using AcDream.App.Plugins;
using AcDream.App.Rendering.Gpu;
using AcDream.App.World;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Rendering;

/// <summary>
/// Draws the filled triangles plugins put in the world as see-through,
/// unlit triangles in the world pass, after the world, so walls and hills
/// hide them but they never hide each other.
/// </summary>
/// <remarks>
/// A plugin may send thousands of triangles, so the draw is bounded here, not
/// by the plugin: a triangle no part of which is within
/// <see cref="DrawRangeMeters"/> of the camera, or which lies wholly outside
/// its view, is skipped, and the triangles of one frame never exceed the
/// batch's budget. When the triangles in range and in view would, the
/// nearest are drawn and the farthest are left out. A triangle with a corner
/// that is not a finite position is dropped.
/// </remarks>
internal sealed class PluginWorldTriangleRenderer(
    PluginWorldTriangleStore store,
    GroundShapeBatch batch,
    IWorldFrameCameraSource camera,
    LiveWorldOriginState origin)
{
    // The same reach as plugins' world lines.
    internal const float DrawRangeMeters = PluginWorldLineRenderer.DrawRangeMeters;

    /// <summary>
    /// The batch's budget: 1 MiB of vertices, a sixteenth of the frame's
    /// upload ring, which is some twelve thousand triangles a frame.
    /// </summary>
    internal const int VertexBudget = 1024 * 1024 / (GroundShapeBatch.FloatsPerVertex * sizeof(float));

    private readonly List<PlannedTriangle> _planned = [];

    public void Render(IGpuPassEncoder encoder, int width, int height)
    {
        if (!origin.IsKnown || width <= 0 || height <= 0)
            return;
        IReadOnlyList<PluginWorldTriangle[]> layers = store.Layers;
        if (layers.Count == 0)
            return;
        WorldCameraFrame frame = camera.Resolve();
        Plan(layers, frame.Position, frame.Frustum, origin.CenterX, origin.CenterY, _planned);
        if (_planned.Count == 0)
            return;

        batch.Begin();
        // Only when the triangles do not all fit is the order worth a sort.
        if ((long)_planned.Count * 3 > batch.RemainingVertexBudget)
            _planned.Sort(static (a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));
        foreach (ref readonly PlannedTriangle planned in CollectionsMarshal.AsSpan(_planned))
        {
            if (batch.RemainingVertexBudget < 3)
                break;
            batch.AddTriangle(
                PluginGroundShapeRenderer.TowardCamera(planned.A, frame.Position),
                PluginGroundShapeRenderer.TowardCamera(planned.B, frame.Position),
                PluginGroundShapeRenderer.TowardCamera(planned.C, frame.Position),
                planned.Color);
        }
        _planned.Clear();
        batch.Flush(encoder, frame.ViewProjection);
    }

    /// <summary>
    /// Every triangle of <paramref name="layers"/> that is drawn this frame,
    /// in the live world's frame, with its squared distance from
    /// <paramref name="camera"/>: those with finite corners, some part within
    /// <see cref="DrawRangeMeters"/> and some part in view, in layer order.
    /// </summary>
    internal static void Plan(
        IReadOnlyList<PluginWorldTriangle[]> layers,
        Vector3 camera,
        in FrustumPlanes frustum,
        int centerX,
        int centerY,
        List<PlannedTriangle> planned)
    {
        planned.Clear();
        const float rangeSquared = DrawRangeMeters * DrawRangeMeters;
        for (int layer = 0; layer < layers.Count; layer++)
        {
            foreach (PluginWorldTriangle triangle in layers[layer])
            {
                Vector3 a = PluginNavigationProjection.ToWorld(triangle.A, centerX, centerY);
                Vector3 b = PluginNavigationProjection.ToWorld(triangle.B, centerX, centerY);
                Vector3 c = PluginNavigationProjection.ToWorld(triangle.C, centerX, centerY);
                if (!float.IsFinite(a.X + a.Y + a.Z + b.X + b.Y + b.Z + c.X + c.Y + c.Z))
                    continue;
                float distanceSquared = Vector3.DistanceSquared(camera, ClosestPoint(camera, a, b, c));
                if (!(distanceSquared <= rangeSquared))
                    continue;
                if (!FrustumCuller.IsAabbVisible(frustum, Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c))))
                    continue;
                PluginColor color = triangle.Color;
                planned.Add(new PlannedTriangle(
                    a, b, c, new Vector4(color.R, color.G, color.B, color.A) / 255f, distanceSquared));
            }
        }
    }

    /// <summary>
    /// The point of triangle <paramref name="a"/>, <paramref name="b"/>,
    /// <paramref name="c"/> nearest <paramref name="p"/>, on its face, an
    /// edge or a corner.
    /// </summary>
    internal static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
            return a;
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
            return b;
        float vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            return a + (ab * (d1 / (d1 - d3)));
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
            return c;
        float vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            return a + (ac * (d2 / (d2 - d6)));
        float va = (d3 * d6) - (d5 * d4);
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            return b + ((c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))));
        float denominator = va + vb + vc;
        // A triangle with no area: its corners are as near as any of it.
        if (!(MathF.Abs(denominator) > 1e-12f))
            return NearestOf(p, a, b, c);
        float v = vb / denominator, w = vc / denominator;
        return a + (ab * v) + (ac * w);
    }

    private static Vector3 NearestOf(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 best = a;
        if (Vector3.DistanceSquared(p, b) < Vector3.DistanceSquared(p, best))
            best = b;
        if (Vector3.DistanceSquared(p, c) < Vector3.DistanceSquared(p, best))
            best = c;
        return best;
    }
}

/// <summary>
/// One world triangle drawn this frame: its corners in the live world's
/// frame, its colour as 0 to 1, and its squared distance in metres from the
/// camera to its nearest point.
/// </summary>
internal readonly record struct PlannedTriangle(Vector3 A, Vector3 B, Vector3 C, Vector4 Color, float DistanceSquared);
