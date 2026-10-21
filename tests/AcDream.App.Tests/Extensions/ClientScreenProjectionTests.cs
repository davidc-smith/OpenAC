using System.Numerics;
using AcDream.App.Extensions;
using AcDream.App.World;
using AcDream.ClientExtensions;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Extensions;

public sealed class ClientScreenProjectionTests
{
    [Fact]
    public void ACapturedProjectionAnswersAsTheLiveScreenDoes()
    {
        var origin = new LiveWorldOriginState();
        origin.TryInitialize(0xA9, 0xB4);
        var screen = new ClientScreen(origin);
        Assert.False(screen.TryCaptureProjection(out _));

        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, -10, 5), Vector3.Zero, Vector3.UnitZ);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(1f, 16f / 9f, 0.1f, 1000f);
        screen.BindCamera(() => (view, projection, new Vector2(1600, 900)));
        Assert.True(screen.TryCaptureProjection(out ClientScreenProjection captured));

        foreach (var local in new[] { new Vector3(96, 96, 0), new Vector3(10, 180, 3), new Vector3(96, -500, 0) })
        {
            PluginNavigationPosition p = AcDream.Runtime.Navigation.RuntimeNavigationPositions.FromCellLocal(0xA9B40001, local);
            bool live = screen.TryProject(p, out Vector2 a);
            bool snap = captured.TryProject(p, out Vector2 b);
            Assert.Equal(live, snap);
            Assert.Equal(a, b);
        }
    }
}
