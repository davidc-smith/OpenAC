using System.Numerics;
using AcDream.App.Rendering.Scene;
using AcDream.App.Rendering.Wb;
using AcDream.Core.World;

namespace AcDream.App.Tests.Rendering;

public sealed class CandidateTupleCapacityTests
{
    // The per-frame draw list grows by doubling: sized exactly, a view that
    // gained a few entities a frame reallocated its large array every frame.
    [Fact]
    public void DrawListGrowsByDoublingNotToTheExactCount()
    {
        var source = new List<(WorldEntity Entity, int MeshRefIndex, uint LandblockId)>();
        var destination = new List<RenderInstanceTuple>();
        for (uint id = 1; id <= 1000; id++)
            source.Add((Entity(id), 0, 0xA9B4FFFFu));
        WbDrawDispatcher.BuildCurrentCandidateTuples(source, null, destination);
        Assert.Equal(1000, destination.Count);
        int capacity = destination.Capacity;

        source.Add((Entity(1001), 0, 0xA9B4FFFFu));
        WbDrawDispatcher.BuildCurrentCandidateTuples(source, null, destination);

        Assert.Equal(1001, destination.Count);
        Assert.True(destination.Capacity >= 2 * capacity,
            $"capacity {capacity} -> {destination.Capacity}");
        Assert.Equal(1001u, destination[^1].Candidate.LocalEntityId);
    }

    private static WorldEntity Entity(uint id) => new()
    {
        Id = id,
        SourceGfxObjOrSetupId = 0x01000001u,
        Position = new Vector3(id, 0f, 0f),
        Rotation = Quaternion.Identity,
        MeshRefs = [new MeshRef(0x01000001u, Matrix4x4.Identity)],
    };
}
