using AcDream.Launcher.Core.NavData;
using AcDream.Launcher.Core.Updates;
using AcDream.Rynth.NavData;

namespace AcDream.Launcher.Core.Tests.NavData;

public sealed class LauncherNavDataServiceTests : IDisposable
{
    private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly NavDataPin BuiltIn = new("https://x.invalid/a.zip", 10, A, 1, 10, A, "s", "2026-10-19");
    private static readonly NavDataPin Released = new("https://x.invalid/b.zip", 20, B, 2, 20, B, "s", "2026-10-20");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "navdata-service-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private LauncherNavDataService Service(NavDataPin? releasePin, Func<bool>? running = null,
        LauncherNavDataService.Installer? installer = null) =>
        new(new FakeManifestClient(releasePin), () => BuiltIn,
            navDataDirectory: Path.Combine(_root, "NavData"),
            cacheDirectory: Path.Combine(_root, "cache"),
            barrier: new UpdateSessionBarrier(Path.Combine(_root, "data")),
            hasRunningSessions: running ?? (() => false),
            installer: installer ?? ((_, _, _, _, _) => Task.FromResult("installed")));

    [Fact]
    public async Task ReleasePinWins()
    {
        NavDataCheck check = await Service(Released).CheckAsync();
        Assert.Equal(Released, check.Pin);
        Assert.Equal(NavDataPinSource.Release, check.Source);
        Assert.Equal(NavDataInstallState.Missing, check.Inspection.State);
    }

    [Fact]
    public async Task BuiltInPinWithoutARelease()
    {
        NavDataCheck check = await Service(null).CheckAsync();
        Assert.Equal(BuiltIn, check.Pin);
        Assert.Equal(NavDataPinSource.BuiltIn, check.Source);
    }

    [Fact]
    public async Task InstallRefusesWhileASessionRuns()
    {
        LauncherNavDataService service = Service(null, running: () => true);
        NavDataCheck check = await service.CheckAsync();
        var error = await Assert.ThrowsAsync<LauncherUpdateException>(() => service.InstallAsync(check, null));
        Assert.Contains("Stop every launcher session", error.Message);
    }

    [Fact]
    public async Task InstallPassesThePinFolderAndCache()
    {
        (NavDataPin, string, string)? seen = null;
        LauncherNavDataService service = Service(Released, installer: (pin, dir, cache, _, _) =>
        {
            seen = (pin, dir, cache);
            return Task.FromResult("done");
        });
        NavDataCheck check = await service.CheckAsync();
        Assert.Equal("done", await service.InstallAsync(check, null));
        Assert.Equal((Released, Path.Combine(_root, "NavData"), Path.Combine(_root, "cache")), seen);
    }

    private sealed class FakeManifestClient(NavDataPin? pin) : IReleaseManifestClient
    {
        public Task<ReleaseManifest> FetchAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<NavDataPin?> FetchNavDataPinAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(pin);
    }
}
