using AcDream.Launcher.Core.Profiles;

namespace AcDream.Launcher.Core.Orchestration;

public sealed record LauncherCharacterSnapshot(
    string ServerName,
    string AccountName,
    string Name,
    string? Id,
    LaunchMode LaunchMode,
    IReadOnlyList<string> Plugins,
    IReadOnlyList<string> LoginCommands,
    bool HasRunningSession,
    string SessionStatus,
    bool HasOwnPlugins = false);

public sealed record LauncherAccountSnapshot(
    string ServerName,
    string AccountName,
    IReadOnlyList<LauncherCharacterSnapshot> Characters,
    bool HasRunningActivity,
    string ActivityStatus,
    string? SelectedCharacter = null,
    LaunchMode? SelectedLaunchMode = null,
    IReadOnlyList<string>? AccountPlugins = null,
    IReadOnlyList<string>? AccountProfiles = null,
    IReadOnlyList<string>? AccountLoginCommands = null)
{
    /// <summary>The plugins every character on the account launches with unless it has its own.</summary>
    public IReadOnlyList<string> Plugins => AccountPlugins ?? [];

    /// <summary>The account's profile tags.</summary>
    public IReadOnlyList<string> Profiles => AccountProfiles ?? [];

    /// <summary>Commands run in order after any character on the account logs in.</summary>
    public IReadOnlyList<string> LoginCommands => AccountLoginCommands ?? [];
}

public sealed record LauncherServerSnapshot(
    string Name,
    string Host,
    int Port,
    IReadOnlyList<LauncherAccountSnapshot> Accounts);

public enum LauncherActivityKind
{
    Play,
    Probe,
}

public enum LauncherActivityState
{
    Starting,
    Running,
    Connected,
    InWorld,
    Disconnected,
    Stopping,
    Exited,
    Failed,
    Cancelled,
}

public sealed record LauncherSessionSnapshot(
    string SessionId,
    LauncherActivityKind Kind,
    string ServerName,
    string AccountName,
    string? CharacterName,
    LaunchMode? LaunchMode,
    LauncherActivityState State,
    string Status,
    int? ExitCode,
    string? Error,
    DateTimeOffset CreatedAt,
    string? ExitReason = null,
    bool ExitedGracefully = false,
    string? PluginNotice = null,
    bool? HasLiveProcess = null,
    bool StartupInFlight = false,
    LauncherSessionWindow? Window = null)
{
    public bool IsActive => State is not (
        LauncherActivityState.Exited
        or LauncherActivityState.Failed
        or LauncherActivityState.Cancelled);

    /// <summary>Physical process/start ownership, independent of an early host exit notice.
    /// Older implementations without process information retain their activity-state behavior.</summary>
    public bool IsProcessOrStartActive => StartupInFlight || (HasLiveProcess ?? IsActive);

    /// <summary>The session's game window as its client reports it. Implementations without
    /// window reports count a graphical launch as open and a headless one as closed.</summary>
    public LauncherSessionWindow ReportedWindow =>
        Window ?? new LauncherSessionWindow(IsOpen: LaunchMode is not Profiles.LaunchMode.Headless);
}

/// <summary>
/// Whether a running play session has a game window, as its client last reported, and a
/// switch its row asked for. The row's mode box switches a running session between
/// graphical and headless without a restart.
/// </summary>
/// <param name="IsOpen">The client reports a game window open on the session (a graphical
/// launch counts as open until the client says otherwise).</param>
/// <param name="SwitchingTo">The state a switch asked for, until the client answers or the
/// switch times out.</param>
/// <param name="Error">Why the last switch did not happen, until the next one.</param>
/// <param name="IsSwitchable">The session's client can open and close its window (one
/// started without window support cannot).</param>
public sealed record LauncherSessionWindow(
    bool IsOpen = false,
    bool? SwitchingTo = null,
    string? Error = null,
    bool IsSwitchable = true)
{
    /// <summary>What the row shows: the asked-for state while a switch is pending, otherwise
    /// the reported one.</summary>
    public bool ShowsWindow => SwitchingTo ?? IsOpen;
}

public sealed record LauncherStateSnapshot(
    IReadOnlyList<LauncherServerSnapshot> Servers,
    IReadOnlyList<LauncherSessionSnapshot> Sessions,
    LauncherPlatformCapabilities Platform,
    bool IsInstallationReady,
    string InstallationStatus,
    bool ShowBetaPlugins = false);

public sealed class LauncherOperationException : Exception
{
    public LauncherOperationException(string message)
        : base(message)
    {
    }
}
