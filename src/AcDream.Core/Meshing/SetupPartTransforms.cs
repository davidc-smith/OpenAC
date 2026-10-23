using System.Collections.Generic;
using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;

namespace AcDream.Core.Meshing;

public static class SetupPartTransforms
{
    /// <summary>
    /// The pose an object without a motion cycle rests in. When the server
    /// sends a placement (the physics AnimationFrame, e.g. Hook for an item
    /// hung on a house hook), use that frame, or Default when the setup lacks
    /// it. Without one, Resting is preferred, then Default, then whatever
    /// frame the setup has.
    /// </summary>
    public static AnimationFrame? ResolvePlacementFrame(Setup setup, uint? placementId)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (placementId is { } id)
        {
            if (setup.PlacementFrames.TryGetValue((Placement)id, out var placed))
                return placed;
            if (setup.PlacementFrames.TryGetValue(Placement.Default, out var fallback))
                return fallback;
        }
        if (setup.PlacementFrames.TryGetValue(Placement.Resting, out var resting))
            return resting;
        if (setup.PlacementFrames.TryGetValue(Placement.Default, out var def))
            return def;
        foreach (var kvp in setup.PlacementFrames)
            return kvp.Value;
        return null;
    }

    public static IReadOnlyList<Matrix4x4> Compute(
        Setup setup,
        AnimationFrame? motionFrameOverride = null,
        float objectScale = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(setup);
        AnimationFrame? source = motionFrameOverride ?? ResolvePlacementFrame(setup, placementId: null);

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
