using AcDream.App.Rendering;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using VideoMode = Silk.NET.Windowing.VideoMode;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// Windows can refuse a display-settings query while the display is changing
/// (a Remote Desktop connect or disconnect, many processes starting at
/// once). The window's display queries then throw. The catalog must survive
/// that and leave the built-in resolution list in charge.
/// </summary>
public sealed class DisplayModeCatalogQueryFailureTests : IDisposable
{
    private const string WindowsQueryFailure =
        "PlatformError: Win32: Failed to query display settings";

    public DisplayModeCatalogQueryFailureTests() => DisplayModeCatalog.ResetForTests();

    public void Dispose() => DisplayModeCatalog.ResetForTests();

    [Fact]
    public void TryInstall_CurrentModeQueryFails_StaysUninstalledAndSaysSo()
    {
        var log = new List<string>();
        var monitor = new FakeMonitor { FailVideoMode = true };

        bool installed = DisplayModeCatalog.TryInstall(() => monitor, log.Add);

        Assert.False(installed);
        Assert.False(DisplayModeCatalog.IsInstalled);
        Assert.Null(DisplayModeCatalog.Resolutions);
        Assert.Null(DisplayModeCatalog.WindowedResolutions);
        Assert.Null(DisplayModeCatalog.DesktopResolution);
        Assert.Contains(log, line => line.Contains(WindowsQueryFailure, StringComparison.Ordinal));
    }

    [Fact]
    public void TryInstall_ModeListQueryFails_StaysUninstalled()
    {
        var monitor = new FakeMonitor { FailModeList = true };

        Assert.False(DisplayModeCatalog.TryInstall(() => monitor, log: null));
        Assert.False(DisplayModeCatalog.IsInstalled);
    }

    [Fact]
    public void TryInstall_MonitorQueryFails_StaysUninstalled()
    {
        Assert.False(DisplayModeCatalog.TryInstall(
            () => throw new GlfwException(WindowsQueryFailure),
            log: null));
        Assert.False(DisplayModeCatalog.IsInstalled);
    }

    [Fact]
    public void EnsureInstalled_AfterAFailure_InstallsOnceTheDisplayAnswers()
    {
        var monitor = new FakeMonitor { FailVideoMode = true };
        Assert.False(DisplayModeCatalog.EnsureInstalled(() => monitor, log: null));

        monitor.FailVideoMode = false;
        Assert.True(DisplayModeCatalog.EnsureInstalled(() => monitor, log: null));

        Assert.Equal(["1280x720", "1920x1080"], DisplayModeCatalog.Resolutions);
        Assert.Equal("1920x1080", DisplayModeCatalog.DesktopResolution);
    }

    [Fact]
    public void EnsureInstalled_WhenInstalled_DoesNotQueryAgain()
    {
        var monitor = new FakeMonitor();
        Assert.True(DisplayModeCatalog.TryInstall(() => monitor, log: null));

        int queries = 0;
        Assert.True(DisplayModeCatalog.EnsureInstalled(
            () =>
            {
                queries++;
                return monitor;
            },
            log: null));
        Assert.Equal(0, queries);
    }

    private sealed class FakeMonitor : IMonitor
    {
        public bool FailVideoMode { get; set; }

        public bool FailModeList { get; set; }

        public string Name => "fake";

        public int Index => 0;

        public Rectangle<int> Bounds => new(0, 0, 1920, 1080);

        public VideoMode VideoMode => FailVideoMode
            ? throw new GlfwException(WindowsQueryFailure)
            : new VideoMode(new Vector2D<int>(1920, 1080), 60);

        public float Gamma { get; set; } = 1f;

        public IEnumerable<VideoMode> GetAllVideoModes() => FailModeList
            ? throw new GlfwException(WindowsQueryFailure)
            :
            [
                new VideoMode(new Vector2D<int>(1280, 720), 60),
                new VideoMode(new Vector2D<int>(1920, 1080), 60),
            ];

        public IWindow CreateWindow(WindowOptions opts) =>
            throw new NotSupportedException();
    }
}
