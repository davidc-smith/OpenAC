using System;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Tests.Physics;
using AcDream.Core.Physics;
using AcDream.Core.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace AcDream.App.Tests.Rendering;

// A player with their back to a lifestone: the boom points into the lifestone's
// one collision sphere, so the camera is pulled in to where its sphere touches
// it and must stay there, frame after frame, instead of re-extending and
// snapping back.
[Collection(CameraDiagnosticsCollection.Name)]
public class LifestoneCameraSettleTests
{
    private const uint LandblockId = 0xA9B40000u;
    private const uint CellId = LandblockId | 0x0001u;
    private const uint PlayerId = 0x000F4243u;

    private static readonly Vector3 LifestonePosition = new(12f, 12f, 0f);
    private const float LifestoneSphereRadius = 1.504f;

    private readonly ITestOutputHelper _out;
    public LifestoneCameraSettleTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData(2.3f)]
    [InlineData(2.8f)]
    [InlineData(3.2f)]
    [InlineData(3.7f)]
    public void BackToTheLifestone_CameraSettlesAgainstIt(float distance)
    {
        var engine = new PhysicsEngine { DataCache = new PhysicsDataCache() };
        engine.AddLandblock(LandblockId, new TerrainSurface(new byte[81], new float[256]),
            Array.Empty<CellSurface>(), Array.Empty<PortalPlane>(), 0f, 0f);
        engine.ShadowObjects.Register(
            0x7AA0C34Fu, gfxObjId: 0u,
            LifestonePosition + new Vector3(0f, 0f, LifestoneSphereRadius),
            Quaternion.Identity, LifestoneSphereRadius,
            worldOffsetX: 0f, worldOffsetY: 0f, landblockId: LandblockId,
            collisionType: ShadowCollisionType.Sphere,
            cylHeight: 0f, scale: 1f,
            state: 0x410u,
            flags: EntityCollisionFlags.HasWeenie,
            isStatic: false);

        // Facing +Y, away from the lifestone south of the player.
        Vector3 player = LifestonePosition + new Vector3(0f, distance, 0f);
        CameraRun.AssertSettles(_out, engine, player, MathF.PI / 2f, CellId, PlayerId,
            LifestonePosition + new Vector3(0f, 0f, LifestoneSphereRadius), LifestoneSphereRadius);
    }
}

[Collection(CameraDiagnosticsCollection.Name)]
[Trait("Lane", "InstalledDat")]
public class LifestoneCameraSettleInstalledDatTests
{
    private const uint ArrivalCell = 0xA9B40019u;
    private const uint PlayerId = 0x000F4243u;

    private readonly ITestOutputHelper _out;
    public LifestoneCameraSettleInstalledDatTests(ITestOutputHelper output) => _out = output;

    // The Holtburg lifestone, real setup collision; the player north of it,
    // from where arrival placement puts them (beside it) to a step away.
    [Fact]
    public void BackToTheHoltburgLifestone_CameraSettlesAgainstIt()
    {
        var scene = new LifestoneArrivalPlacementInstalledDatTests.Scene();
        foreach (float y in new[] { 14.02f, 14.5f, 15.0f, 15.5f })
            CameraRun.AssertSettles(_out, scene.Engine, new Vector3(81.3304f, y, 94.005f),
                MathF.PI / 2f, ArrivalCell, PlayerId,
                new Vector3(81.3304f, 11.7974f, 94.005f + 1.504f), 1.504f);
    }
}

internal static class CameraRun
{
    public static void AssertSettles(
        ITestOutputHelper output, PhysicsEngine engine, Vector3 player, float yaw, uint cellId, uint selfId,
        Vector3 lifestoneSphereCenter, float lifestoneSphereRadius)
    {
        bool savedAlign = CameraDiagnostics.AlignToSlope;
        bool savedCollide = CameraDiagnostics.CollideCamera;
        float savedT = CameraDiagnostics.TranslationStiffness;
        float savedR = CameraDiagnostics.RotationStiffness;
        try
        {
            CameraDiagnostics.AlignToSlope = true;
            CameraDiagnostics.CollideCamera = true;
            CameraDiagnostics.TranslationStiffness = 0.45f;
            CameraDiagnostics.RotationStiffness = 0.45f;

            var cam = new RetailChaseCamera { CollisionProbe = new PhysicsCameraCollisionProbe(engine) };
            Vector3 pivot = player + new Vector3(0f, 0f, cam.PivotHeight);
            float min = float.MaxValue, max = float.MinValue, gap = 0f;
            for (int frame = 0; frame < 600; frame++)
            {
                cam.Update(player, yaw, Vector3.Zero, inContact: true, Vector3.UnitZ,
                    dt: 1f / 60f, cellId: cellId, selfEntityId: selfId);
                if (frame < 300) continue;
                float d = Vector3.Distance(cam.Position, pivot);
                min = MathF.Min(min, d);
                max = MathF.Max(max, d);
                gap = Vector3.Distance(cam.Position, lifestoneSphereCenter)
                    - (lifestoneSphereRadius + PhysicsCameraCollisionProbe.ViewerSphereRadius);
            }

            output.WriteLine(FormattableString.Invariant(
                $"player=({player.X:F3},{player.Y:F3}) eye distance over 5 s: min={min:F4} max={max:F4} m, gap to the lifestone {gap:F4} m"));
            Assert.True(min < cam.Distance - 0.05f, "the lifestone must pull the camera in");
            Assert.True(max - min < 0.001f,
                FormattableString.Invariant($"camera distance must settle; it moved between {min:F4} and {max:F4} m"));
            Assert.True(gap > -0.001f && gap < 0.005f,
                FormattableString.Invariant($"the camera must rest touching the lifestone; gap {gap:F4} m"));
        }
        finally
        {
            CameraDiagnostics.AlignToSlope = savedAlign;
            CameraDiagnostics.CollideCamera = savedCollide;
            CameraDiagnostics.TranslationStiffness = savedT;
            CameraDiagnostics.RotationStiffness = savedR;
        }
    }
}
