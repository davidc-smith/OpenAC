using System.Numerics;
using AcDream.App.Extensions;
using AcDream.App.World;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Extensions;

public sealed class ClientScreenTests
{
    // With the origin centred on landblock (127, 127) a navigation position maps to
    // world metres as EW*240 + 84 (X east), NS*240 + 84 (Y north), Elevation*240 (Z up).
    private const double MetresPerUnit = 240d;

    private static PluginNavigationPosition At(double eastMetres, double northMetres, double upMetres) => new(
        CellId: 0u,
        EastWest: (eastMetres - 84d) / MetresPerUnit,
        NorthSouth: (northMetres - 84d) / MetresPerUnit,
        Elevation: upMetres / MetresPerUnit,
        HeadingDegrees: 0f,
        IsOutdoor: true);

    private static (Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport) NorthFacingCamera()
    {
        var eye = new Vector3(84f, 84f, 10f);
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, eye + new Vector3(0f, 100f, 0f), Vector3.UnitZ);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 3f, 800f / 600f, 0.1f, 1000f);
        return (view, projection, new Vector2(800f, 600f));
    }

    private static LiveWorldOriginState KnownOrigin()
    {
        var origin = new LiveWorldOriginState();
        Assert.True(origin.TryInitialize(127, 127));
        return origin;
    }

    [Fact]
    public void UnboundAnswersFalseEverywhere()
    {
        var screen = new ClientScreen(KnownOrigin());

        Assert.False(screen.TryGetViewportSize(out _));
        Assert.False(screen.TryProject(At(84d, 134d, 10d), out _));
        Assert.False(screen.TryGetRadarDisc(out _, out _));
    }

    [Fact]
    public void ViewportSizeComesFromTheCamera()
    {
        var screen = new ClientScreen(KnownOrigin());
        screen.BindCamera(NorthFacingCamera);

        Assert.True(screen.TryGetViewportSize(out Vector2 size));
        Assert.Equal(new Vector2(800f, 600f), size);
    }

    [Fact]
    public void AnEmptyViewportIsNotASize()
    {
        var screen = new ClientScreen(KnownOrigin());
        screen.BindCamera(() => (Matrix4x4.Identity, Matrix4x4.Identity, new Vector2(800f, 0f)));

        Assert.False(screen.TryGetViewportSize(out _));
        Assert.False(screen.TryProject(At(84d, 134d, 10d), out _));
    }

    [Fact]
    public void UnknownOriginDoesNotProject()
    {
        var screen = new ClientScreen(new LiveWorldOriginState());
        screen.BindCamera(NorthFacingCamera);

        Assert.False(screen.TryProject(At(84d, 134d, 10d), out _));
    }

    [Fact]
    public void APointStraightAheadLandsInTheMiddle()
    {
        var screen = new ClientScreen(KnownOrigin());
        screen.BindCamera(NorthFacingCamera);

        Assert.True(screen.TryProject(At(84d, 134d, 10d), out Vector2 point));
        Assert.InRange(point.X, 399.5f, 400.5f);
        Assert.InRange(point.Y, 299.5f, 300.5f);
    }

    [Fact]
    public void EastIsRightAndUpIsUpOnScreen()
    {
        var screen = new ClientScreen(KnownOrigin());
        screen.BindCamera(NorthFacingCamera);

        Assert.True(screen.TryProject(At(94d, 134d, 20d), out Vector2 point));
        Assert.True(point.X > 400f);
        Assert.True(point.Y < 300f);
    }

    [Fact]
    public void APointBehindTheCameraDoesNotProject()
    {
        var screen = new ClientScreen(KnownOrigin());
        screen.BindCamera(NorthFacingCamera);

        Assert.False(screen.TryProject(At(84d, 34d, 10d), out _));
    }

    [Fact]
    public void UnbindingTheCameraStopsProjection()
    {
        var screen = new ClientScreen(KnownOrigin());
        screen.BindCamera(NorthFacingCamera);
        screen.BindCamera(null);

        Assert.False(screen.TryGetViewportSize(out _));
        Assert.False(screen.TryProject(At(84d, 134d, 10d), out _));
    }

    [Fact]
    public void NoRadarWithoutTheRetainedUi()
    {
        var screen = new ClientScreen(KnownOrigin());
        screen.BindCamera(NorthFacingCamera);

        Assert.False(screen.TryGetRadarDisc(out _, out float radius));
        Assert.Equal(0f, radius);
    }
}
