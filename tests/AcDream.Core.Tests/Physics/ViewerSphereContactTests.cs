using System;
using System.Numerics;
using AcDream.Core.Physics;
using Xunit;
using Xunit.Abstractions;

namespace AcDream.Core.Tests.Physics;

// The camera's viewer sphere swept into a sphere-shaped prop (a lifestone has one
// sphere and no physics BSP) stops where the two spheres touch: the exact
// time-of-impact pull-back moves the sphere back along its own movement, never
// out along the contact direction.
public class ViewerSphereContactTests
{
    private readonly ITestOutputHelper _out;
    public ViewerSphereContactTests(ITestOutputHelper output) => _out = output;

    private const uint TestLandblockId = 0xA9B40000u;
    private const uint TestCellId = TestLandblockId | 0x0001u;
    private const float ViewerSphereRadius = 0.3f;

    private static readonly ObjectInfoState CameraMoverFlags =
        ObjectInfoState.IsViewer | ObjectInfoState.PathClipped
        | ObjectInfoState.FreeRotate | ObjectInfoState.PerfectClip;

    // The lifestone's one collision sphere: radius 1.504 m, centred 1.504 m up.
    private static readonly Vector3 PropCenter = new(12f, 12f, 1.504f);
    private const float PropRadius = 1.504f;

    [Theory]
    [InlineData(2.1f)]
    [InlineData(2.2f)]
    [InlineData(2.6f)]
    [InlineData(3.5f)]
    public void ViewerSweptIntoSphereProp_StopsTouchingIt(float reach)
    {
        var engine = BuildEngine();
        engine.ShadowObjects.Register(
            0x00005001u, gfxObjId: 0u,
            PropCenter, Quaternion.Identity, PropRadius,
            worldOffsetX: 0f, worldOffsetY: 0f, landblockId: TestLandblockId,
            collisionType: ShadowCollisionType.Sphere,
            cylHeight: 0f, scale: 1f,
            state: 0x410u,
            flags: EntityCollisionFlags.HasWeenie,
            isStatic: false);

        // A boom from 3.7 m in front of the prop toward it and slightly up, as a
        // third-person camera behind a player whose back is to the lifestone.
        Vector3 pivot = new(12f, 15.7f, 1.5f);
        Vector3 boom = Vector3.Normalize(new Vector3(0f, -2.5f, 0.75f));
        Vector3 eye = pivot + boom * reach;

        ResolveResult r = engine.ResolveWithTransition(
            currentPos:     pivot - new Vector3(0f, 0f, ViewerSphereRadius),
            targetPos:      eye - new Vector3(0f, 0f, ViewerSphereRadius),
            cellId:         TestCellId,
            sphereRadius:   ViewerSphereRadius,
            sphereHeight:   0f,
            stepUpHeight:   0f,
            stepDownHeight: 0f,
            isOnGround:     false,
            body:           null,
            moverFlags:     CameraMoverFlags,
            movingEntityId: 0);

        Vector3 stop = r.Position + new Vector3(0f, 0f, ViewerSphereRadius);
        float gap = Vector3.Distance(stop, PropCenter) - (PropRadius + ViewerSphereRadius);
        float along = Vector3.Dot(stop - pivot, boom);
        float off = Vector3.Distance(stop, pivot + boom * along);
        _out.WriteLine(FormattableString.Invariant(
            $"reach={reach} ok={r.Ok} stop=({stop.X:F4},{stop.Y:F4},{stop.Z:F4}) along={along:F4} off={off:F4} gap={gap:F4}"));

        Assert.True(r.Ok, "the sweep must find a valid position");
        Assert.InRange(off, 0f, 0.001f);
        Assert.InRange(gap, -0.001f, 0.01f);
    }

    private static PhysicsEngine BuildEngine()
    {
        var engine = new PhysicsEngine { DataCache = new PhysicsDataCache() };
        engine.AddLandblock(
            landblockId:  TestLandblockId,
            terrain:      new TerrainSurface(new byte[81], new float[256]),
            cells:        Array.Empty<CellSurface>(),
            portals:      Array.Empty<PortalPlane>(),
            worldOffsetX: 0f,
            worldOffsetY: 0f);
        return engine;
    }
}
