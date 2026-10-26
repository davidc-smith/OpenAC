using System.Numerics;
using AcDream.Core.Meshing;
using AcDream.Core.World;
using DatReaderWriter.DBObjs;

namespace AcDream.App.Rendering.Vfx;

internal static class IndexedSetupPartPoseBuilder
{
    public static (Matrix4x4[] Poses, bool[] Available) Build(
        Setup setup,
        WorldEntity entity)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(entity);

        int partCount = setup.Parts.Count;
        var poses = new Matrix4x4[partCount];
        if (SetupPartTransforms.ComputeInto(setup, poses, objectScale: entity.Scale) == 0)
            Array.Fill(poses, Matrix4x4.Identity);

        // Every dat static of an arriving landblock passes through here; the
        // scratch stays on the stack for the usual small part counts.
        const int StackLimit = 64;
        Span<uint> expectedGfx = partCount <= StackLimit
            ? stackalloc uint[StackLimit].Slice(0, partCount)
            : new uint[partCount];
        for (int i = 0; i < partCount; i++)
            expectedGfx[i] = (uint)setup.Parts[i];
        for (int i = 0; i < entity.PartOverrides.Count; i++)
        {
            PartOverride replacement = entity.PartOverrides[i];
            if (replacement.PartIndex < expectedGfx.Length)
                expectedGfx[replacement.PartIndex] = replacement.GfxObjId;
        }

        var available = new bool[partCount];
        int drawableCount = entity.MeshRefs.Count;
        Span<bool> consumed = drawableCount <= StackLimit
            ? stackalloc bool[StackLimit].Slice(0, drawableCount)
            : new bool[drawableCount];
        consumed.Clear();
        for (int partIndex = 0; partIndex < partCount; partIndex++)
        {
            for (int drawableIndex = 0; drawableIndex < entity.MeshRefs.Count; drawableIndex++)
            {
                if (consumed[drawableIndex]
                    || entity.MeshRefs[drawableIndex].GfxObjId != expectedGfx[partIndex])
                {
                    continue;
                }

                consumed[drawableIndex] = true;
                available[partIndex] = true;
                break;
            }
        }

        return (poses, available);
    }
}
