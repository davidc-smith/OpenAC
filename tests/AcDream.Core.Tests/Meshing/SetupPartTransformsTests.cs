using System.Numerics;
using AcDream.Core.Meshing;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;

namespace AcDream.Core.Tests.Meshing;

public class SetupPartTransformsTests
{
    [Fact]
    public void ComputeInto_WritesWhatComputeReturns_IncludingPartsPastTheFrames()
    {
        var setup = new Setup
        {
            Parts = { 0x01000100u, 0x01000101u, 0x01000102u },
            DefaultScale = { Vector3.One, Vector3.One, Vector3.One },
            PlacementFrames =
            {
                [Placement.Default] = new AnimationFrame(2)
                {
                    Frames =
                    {
                        new Frame
                        {
                            Origin = new Vector3(1f, 2f, 3f),
                            Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f),
                        },
                        new Frame { Origin = new Vector3(0, 0, 1f), Orientation = Quaternion.Identity },
                    },
                },
            },
        };

        IReadOnlyList<Matrix4x4> expected = SetupPartTransforms.Compute(setup, objectScale: 1.5f);
        var written = new Matrix4x4[4];
        int count = SetupPartTransforms.ComputeInto(setup, written, objectScale: 1.5f);

        Assert.Equal(3, count);
        Assert.Equal(expected, written.Take(count));
        Assert.Equal(Matrix4x4.Identity, written[2]);

        Assert.Equal(0, SetupPartTransforms.ComputeInto(
            new Setup { Parts = { 0x01000100u } },
            written));
        Assert.Empty(SetupPartTransforms.Compute(new Setup { Parts = { 0x01000100u } }));
    }

    [Fact]
    public void Compute_PrefersRestingPlacement_OverDefault()
    {
        var setup = new Setup
        {
            Parts = { 0x01000100u, 0x01000101u },
            DefaultScale = { Vector3.One, Vector3.One },
            PlacementFrames =
            {
                [Placement.Resting] = new AnimationFrame(2)
                {
                    Frames =
                    {
                        new Frame { Origin = Vector3.Zero, Orientation = Quaternion.Identity },
                        new Frame { Origin = new Vector3(0, 0, 1f), Orientation = Quaternion.Identity },
                    },
                },
                [Placement.Default] = new AnimationFrame(2)
                {
                    Frames =
                    {
                        new Frame { Origin = Vector3.Zero, Orientation = Quaternion.Identity },
                        new Frame { Origin = Vector3.Zero, Orientation = Quaternion.Identity },
                    },
                },
            },
        };

        var transforms = SetupPartTransforms.Compute(setup);

        Assert.Equal(2, transforms.Count);
        var probe = Vector3.Transform(Vector3.Zero, transforms[1]);
        Assert.Equal(new Vector3(0, 0, 1f), probe);
    }

    [Fact]
    public void Compute_FallsBackToDefault_WhenRestingMissing()
    {
        var setup = new Setup
        {
            Parts = { 0x01000100u },
            DefaultScale = { Vector3.One },
            PlacementFrames =
            {
                [Placement.Default] = new AnimationFrame(1)
                {
                    Frames =
                    {
                        new Frame { Origin = new Vector3(2f, 0, 0), Orientation = Quaternion.Identity },
                    },
                },
            },
        };

        var transforms = SetupPartTransforms.Compute(setup);

        Assert.Single(transforms);
        var probe = Vector3.Transform(Vector3.Zero, transforms[0]);
        Assert.Equal(new Vector3(2f, 0, 0), probe);
    }

    [Fact]
    public void ResolvePlacementFrame_UsesServerPlacement_WhenSetupHasIt()
    {
        Setup setup = HookedItemSetup();

        AnimationFrame? frame = SetupPartTransforms.ResolvePlacementFrame(
            setup,
            (uint)Placement.Hook);

        Assert.Same(setup.PlacementFrames[Placement.Hook], frame);
    }

    [Fact]
    public void ResolvePlacementFrame_FallsBackToDefault_WhenServerPlacementMissing()
    {
        Setup setup = HookedItemSetup();

        AnimationFrame? frame = SetupPartTransforms.ResolvePlacementFrame(
            setup,
            (uint)Placement.RightHandCombat);

        Assert.Same(setup.PlacementFrames[Placement.Default], frame);
    }

    [Fact]
    public void ResolvePlacementFrame_UsesNoFrame_WhenSetupHasNeitherPlacementNorDefault()
    {
        Setup setup = HookedItemSetup();
        setup.PlacementFrames.Remove(Placement.Default);

        AnimationFrame? frame = SetupPartTransforms.ResolvePlacementFrame(
            setup,
            (uint)Placement.RightHandCombat);

        Assert.Null(frame);
    }

    [Fact]
    public void ResolveObjectPose_WithNoFrame_LeavesEveryPartAtTheOrigin()
    {
        Setup setup = HookedItemSetup();
        setup.PlacementFrames.Remove(Placement.Default);

        AnimationFrame pose = SetupPartTransforms.ResolveObjectPose(
            setup,
            (uint)Placement.RightHandCombat);

        Assert.Empty(pose.Frames);
        Assert.Equal(Matrix4x4.Identity, Assert.Single(SetupPartTransforms.Compute(setup, pose)));
    }

    [Theory]
    [InlineData(null, false, (uint)Placement.Default)]
    [InlineData((uint)Placement.Hook, false, (uint)Placement.Hook)]
    [InlineData(null, true, (uint)Placement.Resting)]
    public void ObjectPlacementId_FollowsWhatTheServerSent(
        uint? placementId,
        bool hasMovementData,
        uint expected)
    {
        Assert.Equal(
            expected,
            SetupPartTransforms.ObjectPlacementId(placementId, hasMovementData));
    }

    private static Setup HookedItemSetup() => new()
    {
        Parts = { 0x01000100u },
        DefaultScale = { Vector3.One },
        PlacementFrames =
        {
            [Placement.Default] = new AnimationFrame(1)
            {
                Frames = { new Frame { Origin = Vector3.Zero, Orientation = Quaternion.Identity } },
            },
            [Placement.Resting] = new AnimationFrame(1)
            {
                Frames = { new Frame { Origin = new Vector3(0, 0, 1f), Orientation = Quaternion.Identity } },
            },
            [Placement.Hook] = new AnimationFrame(1)
            {
                Frames =
                {
                    new Frame
                    {
                        Origin = Vector3.Zero,
                        Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f),
                    },
                },
            },
        },
    };

    [Fact]
    public void Compute_ReturnsEmpty_WhenNoPlacementFrames()
    {
        var setup = new Setup
        {
            Parts = { 0x01000100u, 0x01000101u },
        };

        var transforms = SetupPartTransforms.Compute(setup);

        Assert.Empty(transforms);
    }

    [Fact]
    public void Compute_ExcludesVisualDefaultScale_FromRigidPartFrame()
    {
        var setup = new Setup
        {
            Parts = { 0x01000100u },
            DefaultScale = { new Vector3(2f, 2f, 2f) },
            PlacementFrames =
            {
                [Placement.Resting] = new AnimationFrame(1)
                {
                    Frames =
                    {
                        new Frame { Origin = Vector3.Zero, Orientation = Quaternion.Identity },
                    },
                },
            },
        };

        var transforms = SetupPartTransforms.Compute(setup);

        Assert.Single(transforms);
        var probe = Vector3.Transform(new Vector3(1f, 1f, 1f), transforms[0]);
        Assert.Equal(new Vector3(1f, 1f, 1f), probe);
    }

    [Fact]
    public void Compute_ObjectScaleMultipliesOriginButNotOrientationAxes()
    {
        var setup = new Setup
        {
            Parts = { 0x01000100u },
            DefaultScale = { new Vector3(7f, 8f, 9f) },
            PlacementFrames =
            {
                [Placement.Resting] = new AnimationFrame(1)
                {
                    Frames =
                    {
                        new Frame
                        {
                            Origin = new Vector3(2f, 0f, 0f),
                            Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f),
                        },
                    },
                },
            },
        };

        Matrix4x4 rigid = Assert.Single(SetupPartTransforms.Compute(setup, objectScale: 3f));

        Assert.Equal(new Vector3(6f, 0f, 0f), rigid.Translation);
        Vector3 rotatedAxis = Vector3.TransformNormal(Vector3.UnitX, rigid);
        Assert.InRange(rotatedAxis.X, -0.0001f, 0.0001f);
        Assert.InRange(rotatedAxis.Y, 0.9999f, 1.0001f);
    }
}
