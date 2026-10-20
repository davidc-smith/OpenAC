using AcDream.Launcher.Core.Updates;
using AcDream.Rynth.NavData;

namespace AcDream.Launcher.Core.NavData;

public enum NavDataPinSource { Release, BuiltIn }

public sealed record NavDataCheck(NavDataPin Pin, NavDataPinSource Source, NavDataInspection Inspection);

/// <summary>
/// The RynthNav navdata the client's RynthNav reads: which pin applies (the release's, else the
/// one built in) and installing exactly it. The folder swap would pull tiles out from under a
/// running client, so an install runs only with no session, under the exclusive session lease.
/// </summary>
public sealed class LauncherNavDataService
{
    public delegate Task<string> Installer(NavDataPin pin, string navDataDirectory, string cacheDirectory,
        IProgress<NavDataProgress>? progress, CancellationToken cancellationToken);

    private readonly IReleaseManifestClient _manifestClient;
    private readonly Func<NavDataPin> _builtInPin;
    private readonly UpdateSessionBarrier _barrier;
    private readonly Func<bool> _hasRunningSessions;
    private readonly Installer _installer;

    public LauncherNavDataService(
        IReleaseManifestClient manifestClient,
        Func<NavDataPin> builtInPin,
        string navDataDirectory,
        string cacheDirectory,
        UpdateSessionBarrier barrier,
        Func<bool> hasRunningSessions,
        Installer? installer = null)
    {
        _manifestClient = manifestClient ?? throw new ArgumentNullException(nameof(manifestClient));
        _builtInPin = builtInPin ?? throw new ArgumentNullException(nameof(builtInPin));
        NavDataDirectory = navDataDirectory;
        CacheDirectory = cacheDirectory;
        _barrier = barrier ?? throw new ArgumentNullException(nameof(barrier));
        _hasRunningSessions = hasRunningSessions ?? throw new ArgumentNullException(nameof(hasRunningSessions));
        // Task.Run: RynthCore's installer runs synchronously up to its first await (a cached zip is
        // SHA-256 hashed first, 1.57 GB) and its awaits do not ConfigureAwait(false), so called
        // directly it would hash and resume its download chunks on the UI thread.
        _installer = installer ?? ((pin, dir, cache, progress, ct) =>
            Task.Run(() => NavDataInstaller.InstallAsync(pin, dir, cache, progress, ct), ct));
    }

    public string NavDataDirectory { get; }

    public string CacheDirectory { get; }

    /// <summary>The service the launcher uses: the client's own folders.</summary>
    public static LauncherNavDataService ForThisMachine(IReleaseManifestClient manifestClient,
        UpdateSessionBarrier barrier, Func<bool> hasRunningSessions) =>
        new(manifestClient, ReleaseNavDataPin.BuiltIn, NavDataFolders.NavData(), NavDataFolders.Cache(),
            barrier, hasRunningSessions);

    public async Task<NavDataCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        NavDataPin? released = await _manifestClient.FetchNavDataPinAsync(cancellationToken).ConfigureAwait(false);
        NavDataPin pin = released ?? _builtInPin();
        return new NavDataCheck(pin, released is null ? NavDataPinSource.BuiltIn : NavDataPinSource.Release,
            NavDataInspector.Inspect(pin, NavDataDirectory));
    }

    public async Task<string> InstallAsync(NavDataCheck check, IProgress<NavDataProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(check);
        RefuseRunningSessions();
        using UpdateSessionBarrier.ExclusiveLease lease = _barrier.AcquireExclusive();
        RefuseRunningSessions();
        return await _installer(check.Pin, NavDataDirectory, CacheDirectory, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private void RefuseRunningSessions()
    {
        if (_hasRunningSessions())
            throw new LauncherUpdateException("Stop every launcher session before installing the RynthNav navdata.");
    }
}
