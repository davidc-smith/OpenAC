using System.Numerics;
using AcDream.App.Rendering;

namespace AcDream.App.Tests.Rendering;

public sealed class PaperdollPoseTests
{
    [Fact]
    public void Default_IsAtRest()
    {
        Assert.True(PaperdollPose.Default.IsDefault);
        Assert.Equal(0f, PaperdollPose.Default.Yaw);
        Assert.Equal(1f, PaperdollPose.Default.Zoom);
        Assert.False(PaperdollPose.Default.Turned(0.2f).IsDefault);
        Assert.False(PaperdollPose.Default.Zoomed(1).IsDefault);
    }

    [Fact]
    public void Turned_WrapsIntoOneTurn()
    {
        PaperdollPose p = PaperdollPose.Default.Turned(MathF.PI * 3f);
        Assert.InRange(p.Yaw, -MathF.PI, MathF.PI + 1e-5f);
        Assert.Equal(MathF.PI, MathF.Abs(p.Yaw), 3);
    }

    [Fact]
    public void Zoomed_StepsAndClamps_AndSnapsBackToOne()
    {
        Assert.Equal(1.12f, PaperdollPose.Default.Zoomed(1).Zoom, 4);
        Assert.Equal(PaperdollPose.MaxZoom, PaperdollPose.Default.Zoomed(40).Zoom);
        Assert.Equal(1f, PaperdollPose.Default.Zoomed(-3).Zoom);
        Assert.Equal(1f, PaperdollPose.Default.Zoomed(3).Zoomed(-3).Zoom);
        Assert.True(PaperdollPose.Default.Zoomed(3).Zoomed(-3).IsDefault);
    }

    [Fact]
    public void Apply_RotatesAboutUp()
    {
        Quaternion baseRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f);
        Assert.Equal(baseRotation, PaperdollPose.Default.Apply(baseRotation));
        Quaternion turned = PaperdollPose.Default.Turned(0.25f).Apply(baseRotation);
        Vector3 x = Vector3.Transform(Vector3.UnitX, turned);
        Assert.Equal(MathF.Cos(0.75f), x.X, 4);
        Assert.Equal(MathF.Sin(0.75f), x.Y, 4);
        Assert.Equal(0f, x.Z, 4);
    }

    [Fact]
    public void Fov_NarrowsWithZoom()
    {
        float baseFov = MathF.PI / 4f;
        Assert.Equal(baseFov, PaperdollPose.Default.Fov(baseFov));
        PaperdollPose twice = new(0f, 2f);
        Assert.Equal(MathF.Tan(baseFov / 2f) / 2f, MathF.Tan(twice.Fov(baseFov) / 2f), 5);
    }
}
