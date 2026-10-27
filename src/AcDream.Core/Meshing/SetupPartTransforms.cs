using System.Collections.Generic;
using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;

namespace AcDream.Core.Meshing;

public static class SetupPartTransforms
{
    /// <summary>
    /// The placement a server object rests in when no motion cycle poses it.
    /// An object the server sent motion data for keeps Resting. Otherwise it
    /// takes the placement the server named (the physics AnimationFrame, e.g.
    /// Hook for an item hung on a house hook), and Default when it named none.
    /// </summary>
    public static uint ObjectPlacementId(uint? placementId, bool hasMovementData) =>
        hasMovementData
            ? (uint)Placement.Resting
            : placementId ?? (uint)Placement.Default;

    /// <summary>
    /// The frame a setup is posed in for a placement: that placement's frame,
    /// else Default, else none.
    /// </summary>
    public static AnimationFrame? ResolvePlacementFrame(Setup setup, uint placementId)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (setup.PlacementFrames.TryGetValue((Placement)placementId, out var placed))
            return placed;
        if (setup.PlacementFrames.TryGetValue(Placement.Default, out var fallback))
            return fallback;
        return null;
    }

    /// <summary>
    /// The pose a server object rests in for a placement. When the setup has
    /// neither that placement nor Default, the pose has no part frames: every
    /// part sits at the object's origin, unrotated.
    /// </summary>
    public static AnimationFrame ResolveObjectPose(Setup setup, uint placementId) =>
        ResolvePlacementFrame(setup, placementId) ?? new AnimationFrame(0);

    public static IReadOnlyList<Matrix4x4> Compute(
        Setup setup,
        AnimationFrame? motionFrameOverride = null,
        float objectScale = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (ResolveComputeSource(setup, motionFrameOverride) is null)
            return Array.Empty<Matrix4x4>();
        var result = new Matrix4x4[setup.Parts.Count];
        ComputeInto(setup, result, motionFrameOverride, objectScale);
        return result;
    }

    /// <summary>
    /// <see cref="Compute"/> written into the caller's span (one entry per
    /// part, so at least the setup's part count long). Returns how many
    /// entries were written: the part count, or 0 when the setup has no
    /// frame to pose it in.
    /// </summary>
    public static int ComputeInto(
        Setup setup,
        System.Span<Matrix4x4> destination,
        AnimationFrame? motionFrameOverride = null,
        float objectScale = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(setup);
        AnimationFrame? source = ResolveComputeSource(setup, motionFrameOverride);
        if (source is null)
            return 0;

        int partCount = setup.Parts.Count;
        if (destination.Length < partCount)
            throw new ArgumentException("The destination is shorter than the part count.", nameof(destination));
        for (int i = 0; i < partCount; i++)
        {
            destination[i] = i < source.Frames.Count
                ? Matrix4x4.CreateFromQuaternion(source.Frames[i].Orientation)
                    * Matrix4x4.CreateTranslation(source.Frames[i].Origin * objectScale)
                : Matrix4x4.CreateFromQuaternion(Quaternion.Identity)
                    * Matrix4x4.CreateTranslation(Vector3.Zero * objectScale);
        }
        return partCount;
    }

    private static AnimationFrame? ResolveComputeSource(
        Setup setup,
        AnimationFrame? motionFrameOverride)
    {
        AnimationFrame? source = motionFrameOverride;
        if (source is null && setup.PlacementFrames.TryGetValue(Placement.Resting, out var resting))
        {
            source = resting;
        }
        else if (source is null && setup.PlacementFrames.TryGetValue(Placement.Default, out var def))
        {
            source = def;
        }
        else if (source is null)
        {
            foreach (var kvp in setup.PlacementFrames)
            {
                source = kvp.Value;
                break;
            }
        }

        return source;
    }
}
