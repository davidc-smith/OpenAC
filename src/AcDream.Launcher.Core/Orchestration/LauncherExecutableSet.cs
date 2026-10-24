using AcDream.Launcher.Core.Launching;
using AcDream.Launcher.Core.Profiles;
using AcDream.Launcher.Core.Updates;
using AcDream.Launcher.Core;

namespace AcDream.Launcher.Core.Orchestration;

public sealed class LauncherExecutableSet
{
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _hasUnixExecutePermission;
    private readonly Func<ExecutablePaths> _resolve;

    public LauncherExecutableSet(
        string graphicalHostPath,
        string headlessHostPath,
        string? workingDirectory = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? hasUnixExecutePermission = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphicalHostPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(headlessHostPath);
        string graphical = graphicalHostPath;
        string headless = headlessHostPath;
        _resolve = () => new ExecutablePaths(graphical, headless, workingDirectory);
        _fileExists = fileExists ?? File.Exists;
        _hasUnixExecutePermission =
            hasUnixExecutePermission ?? HasUnixExecutePermission;
    }

    private LauncherExecutableSet(
        Func<ExecutablePaths> resolve,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? hasUnixExecutePermission = null)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _fileExists = fileExists ?? File.Exists;
        _hasUnixExecutePermission =
            hasUnixExecutePermission ?? HasUnixExecutePermission;
    }

    public string GraphicalHostPath => _resolve().GraphicalHostPath;

    public string HeadlessHostPath => _resolve().HeadlessHostPath;

    public string? WorkingDirectory => _resolve().WorkingDirectory;

    public LauncherCapability GetAvailability(LaunchMode mode)
    {
        ExecutablePaths paths;
        try
        {
            paths = _resolve();
        }
        catch (Exception ex) when (ex is LauncherUpdateException
                                   or InvalidOperationException
                                   or IOException
                                   or UnauthorizedAccessException)
        {
            return LauncherCapability.Unavailable(
                $"The active versioned client is unavailable: {ex.Message}");
        }

        string path = mode == LaunchMode.Headless
            ? paths.HeadlessHostPath
            : paths.GraphicalHostPath;
        string host = mode == LaunchMode.Headless
            ? "headless host"
            : "graphical client";
        if (!_fileExists(path))
        {
            return LauncherCapability.Unavailable(
                $"The co-deployed {host} is missing at '{path}'. Reinstall or update "
                + "the client before launching.");
        }

        if (LauncherOperatingSystem.IsUnix && !_hasUnixExecutePermission(path))
        {
            return LauncherCapability.Unavailable(
                $"The co-deployed {host} at '{path}' exists but is not executable. "
                + "Restore its executable permission (for example, chmod +x) or "
                + "reinstall/update the client before launching.");
        }

        string? clientDirectory = Path.GetDirectoryName(path);
        if (clientDirectory is null
            || !_fileExists(Path.Combine(
                clientDirectory,
                AcDream.Platform.ClientCapabilities.SingleInstallRootMarkerFileName)))
        {
            return LauncherCapability.Unavailable(OlderClientRefusal(clientDirectory));
        }

        return LauncherCapability.Available;
    }

    /// <summary>
    /// Whether a play row in <paramref name="mode"/> can start: every row runs the graphical
    /// client, except a headless row without <paramref name="windowSupport"/> (no display),
    /// which runs the windowless host.
    /// </summary>
    public LauncherCapability GetPlayAvailability(LaunchMode mode, bool windowSupport = true) =>
        GetAvailability(PlayHost(mode, windowSupport));

    /// <summary>
    /// How a play row starts its session. Every row starts the graphical client with its
    /// console on and its input left open: the launcher's console window types lines to it,
    /// and the row switches the running session between graphical and headless with
    /// <c>/window &lt;id&gt;</c> and <c>/close</c>. A headless row adds <c>--no-window</c>, so
    /// no window opens until it is asked for. Stop asks the client to log off with
    /// <c>/quit</c> on that console (the graphical client has no console of its own for a
    /// console break to reach), then closes its window. Where no display exists
    /// (<paramref name="windowSupport"/> false) a headless row runs the windowless host with
    /// its console instead.
    /// </summary>
    public LauncherProcessSpec CreatePlaySpec(
        LaunchMode mode,
        string configFilePath,
        string? stderrLogPath = null,
        bool windowSupport = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        LaunchMode host = PlayHost(mode, windowSupport);
        ExecutablePaths paths = RequireAvailable(host);

        if (host == LaunchMode.Headless)
        {
            return new LauncherProcessSpec(
                paths.HeadlessHostPath,
                // A session with no window is talked to through its console:
                // what it says goes to the session log, and the launcher's
                // console window sends it what the player types.
                ["run", "--config", configFilePath, "--console"],
                paths.WorkingDirectory,
                StderrLogPath: stderrLogPath,
                KeepStandardInputOpen: true);
        }

        string[] arguments = mode == LaunchMode.Headless
            ? ["--session-config", configFilePath, "--console", "--no-window"]
            : ["--session-config", configFilePath, "--console"];
        return new LauncherProcessSpec(
            paths.GraphicalHostPath,
            arguments,
            paths.WorkingDirectory,
            SupportsConsoleGracefulStop: false,
            StderrLogPath: stderrLogPath,
            KeepStandardInputOpen: true)
        {
            StopLine = GraphicalClientStopLine,
        };
    }

    /// <summary>The console line that logs the graphical client off and ends it.</summary>
    public const string GraphicalClientStopLine = "/quit";

    /// <summary>Which host a play row runs: the graphical client, or the windowless host for
    /// a headless row where no window can open.</summary>
    private static LaunchMode PlayHost(LaunchMode mode, bool windowSupport) =>
        mode == LaunchMode.Headless && !windowSupport ? LaunchMode.Headless : LaunchMode.Gui;

    public LauncherProcessSpec CreateProbeSpec(
        string configFilePath,
        string? stderrLogPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        ExecutablePaths paths = RequireAvailable(LaunchMode.Headless);
        return new LauncherProcessSpec(
            paths.HeadlessHostPath,
            ["run", "--config", configFilePath],
            paths.WorkingDirectory,
            StderrLogPath: stderrLogPath);
    }

    public static LauncherExecutableSet FromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string fullDirectory = Path.GetFullPath(directory);
        string executableSuffix = PayloadExecutableNames.SuffixForCurrentOs();
        return new LauncherExecutableSet(
            Path.Combine(
                fullDirectory,
                PayloadExecutableNames.GraphicalHostForCurrentOs() + executableSuffix),
            Path.Combine(
                fullDirectory,
                PayloadExecutableNames.HeadlessHost + executableSuffix),
            fullDirectory);
    }

    public static LauncherExecutableSet FromCurrentVersionStore(
        ClientVersionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new LauncherExecutableSet(() =>
        {
            ClientVersionResolution resolution = store.CachedResolution;
            if (!resolution.IsVerified || resolution.Directory is null)
            {
                throw new LauncherUpdateException(resolution.Status);
            }

            return FromDirectoryPaths(resolution.Directory);
        });
    }

    public static LauncherExecutableSet Unavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new LauncherExecutableSet(
            () => throw new LauncherUpdateException(reason));
    }

    private ExecutablePaths RequireAvailable(LaunchMode mode)
    {
        LauncherCapability capability = GetAvailability(mode);
        if (!capability.IsAvailable)
        {
            throw new LauncherOperationException(
                capability.Reason ?? "The selected launcher host is unavailable.");
        }

        return _resolve();
    }

    private static ExecutablePaths FromDirectoryPaths(string directory)
    {
        string fullDirectory = Path.GetFullPath(directory);
        string executableSuffix = PayloadExecutableNames.SuffixForCurrentOs();
        return new ExecutablePaths(
            Path.Combine(
                fullDirectory,
                PayloadExecutableNames.GraphicalHostForCurrentOs() + executableSuffix),
            Path.Combine(
                fullDirectory,
                PayloadExecutableNames.HeadlessHost + executableSuffix),
            fullDirectory);
    }

    /// <summary>
    /// Why a client that predates the single install folder is not started:
    /// it would read and write settings and plugins in the old per-user
    /// folders, which the new install does not use.
    /// </summary>
    internal static string OlderClientRefusal(string? clientDirectory) =>
        $"The installed client{(clientDirectory is null ? string.Empty : $" ({Path.GetFileName(clientDirectory)})")} "
        + "is older than the single OpenAC install folder and would not find your settings, "
        + "plugins or game files there. Update the client from the launcher before playing.";

    private static bool HasUnixExecutePermission(string path)
    {
        if (!LauncherOperatingSystem.IsUnix)
        {
            return true;
        }

        try
        {
            const UnixFileMode executeBits =
                UnixFileMode.UserExecute
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & executeBits) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record ExecutablePaths(
        string GraphicalHostPath,
        string HeadlessHostPath,
        string? WorkingDirectory);
}
