using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthScreenSlotsTests
{
    private readonly List<string> _log = new();

    [Fact]
    public void TheSdkSeesTheThreeSlots()
    {
        using var table = new RynthApiTable(new FakeGameView(), _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.HasWorldToScreen);
        Assert.True(host.HasGetViewportSize);
        Assert.True(host.HasGetRadarRect);
    }

    [Fact]
    public void WorldToScreenWritesThePointOrAnswersZero()
    {
        var view = new FakeGameView { Screen = (true, 12.5f, 34.5f) };
        using var table = new RynthApiTable(view, _log.Add);
        var fn = (delegate* unmanaged[Cdecl]<float, float, float, float*, float*, int>)table.Native->WorldToScreenFn;
        float sx = 0, sy = 0;

        Assert.Equal(1, fn(1, 2, 3, &sx, &sy));
        Assert.Equal((12.5f, 34.5f), (sx, sy));
        Assert.Equal(0, fn(1, 2, 3, null, &sy));
        Assert.Equal(0, fn(1, 2, 3, &sx, null));

        view.Screen = (false, 0, 0);
        Assert.Equal(0, fn(1, 2, 3, &sx, &sy));

        view.Throw = true;
        Assert.Equal(0, fn(1, 2, 3, &sx, &sy));
        Assert.Single(_log, l => l.Contains("WorldToScreen", StringComparison.Ordinal) && l.Contains("threw", StringComparison.Ordinal));
    }

    [Fact]
    public void GetViewportSizeWritesTheSizeOrAnswersZero()
    {
        var view = new FakeGameView { Viewport = (true, 1280, 720) };
        using var table = new RynthApiTable(view, _log.Add);
        var fn = (delegate* unmanaged[Cdecl]<uint*, uint*, int>)table.Native->GetViewportSizeFn;
        uint w = 0, h = 0;

        Assert.Equal(1, fn(&w, &h));
        Assert.Equal((1280u, 720u), (w, h));
        Assert.Equal(0, fn(null, &h));
        Assert.Equal(0, fn(&w, null));

        view.Viewport = (false, 0, 0);
        Assert.Equal(0, fn(&w, &h));

        view.Throw = true;
        Assert.Equal(0, fn(&w, &h));
        Assert.Single(_log, l => l.Contains("GetViewportSize", StringComparison.Ordinal) && l.Contains("threw", StringComparison.Ordinal));
    }

    [Fact]
    public void GetRadarRectWritesTheBoxOrAnswersZero()
    {
        var view = new FakeGameView { Radar = (true, 1, 2, 3, 4) };
        using var table = new RynthApiTable(view, _log.Add);
        var fn = (delegate* unmanaged[Cdecl]<int*, int*, int*, int*, int>)table.Native->GetRadarRectFn;
        int a = 0, b = 0, c = 0, d = 0;

        Assert.Equal(1, fn(&a, &b, &c, &d));
        Assert.Equal((1, 2, 3, 4), (a, b, c, d));
        Assert.Equal(0, fn(null, &b, &c, &d));
        Assert.Equal(0, fn(&a, null, &c, &d));
        Assert.Equal(0, fn(&a, &b, null, &d));
        Assert.Equal(0, fn(&a, &b, &c, null));

        view.Radar = (false, 0, 0, 0, 0);
        Assert.Equal(0, fn(&a, &b, &c, &d));

        view.Throw = true;
        Assert.Equal(0, fn(&a, &b, &c, &d));
        Assert.Single(_log, l => l.Contains("GetRadarRect", StringComparison.Ordinal) && l.Contains("threw", StringComparison.Ordinal));
    }
}
