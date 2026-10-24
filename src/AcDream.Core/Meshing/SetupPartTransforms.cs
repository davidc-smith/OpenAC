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

        if (source is null)
            return Array.Empty<Matrix4x4>();

        int partCount = setup.Parts.Count;
        var result = new Matrix4x4[partCount];
        for (int i = 0; i < partCount; i++)
        {
            Frame frame = i < source.Frames.Count
                ? source.Frames[i]
                : new Frame { Origin = Vector3.Zero, Orientation = Quaternion.Identity };
            result[i] = Matrix4x4.CreateFromQuaternion(frame.Orientation)
                      * Matrix4x4.CreateTranslation(frame.Origin * objectScale);
        }
        return result;
    }
}
