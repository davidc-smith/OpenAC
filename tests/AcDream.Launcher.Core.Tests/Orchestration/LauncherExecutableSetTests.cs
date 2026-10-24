using AcDream.Launcher.Core;
using AcDream.Launcher.Core.Orchestration;
using AcDream.Launcher.Core.Profiles;
using AcDream.Launcher.Core.Updates;

namespace AcDream.Launcher.Core.Tests.Orchestration;

public sealed class LauncherExecutableSetTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "acdream-launcher-layout-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// A client without the single-install-folder marker (0.1.15 and older)
    /// is never started: it would look for settings and plugins in the old
    /// per-user folders. Mutation: dropping the marker check fails this.
    /// </summary>
    [Fact]
    public void AClientOlderThanTheSingleInstallFolderIsNotStarted()
    {
        Directory.CreateDirectory(_root);
        string suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        string graphical = Path.Combine(_root, PayloadExecutableNames.GraphicalHostForCurrentOs() + suffix);
        string headless = Path.Combine(_root, "acdream-headless" + suffix);
        File.WriteAllText(graphical, string.Empty);
        File.WriteAllText(headless, string.Empty);
        MakeExecutableOnUnix(graphical);
        MakeExecutableOnUnix(headless);

        LauncherExecutableSet set = LauncherExecutableSet.FromDirectory(_root);

        LauncherCapability gui = set.GetAvailability(LaunchMode.Gui);
        Assert.False(gui.IsAvailable);
        Assert.Contains("older than the single OpenAC install folder", gui.Reason, StringComparison.Ordinal);
        Assert.False(set.GetAvailability(LaunchMode.Headless).IsAvailable);
        Assert.Throws<LauncherOperationException>(
            () => set.CreatePlaySpec(LaunchMode.Gui, "session.json"));
    }

    [Fact]
    public void FromDirectoryResolvesThePublishedCoDeploymentLayout()
    {
        Directory.CreateDirectory(_root);
        string suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        string graphical = Path.Combine(
            _root,
            PayloadExecutableNames.GraphicalHostForCurrentOs() + suffix);
        string headless = Path.Combine(_root, "acdream-headless" + suffix);
        File.WriteAllText(graphical, string.Empty);
        File.WriteAllText(headless, string.Empty);
        File.WriteAllText(
            Path.Combine(_root, AcDream.Platform.ClientCapabilities.SingleInstallRootMarkerFileName),
            string.Empty);
        MakeExecutableOnUnix(graphical);
        MakeExecutableOnUnix(headless);

        LauncherExecutableSet set = LauncherExecutableSet.FromDirectory(_root);

        Assert.Equal(Path.GetFullPath(_root), set.WorkingDirectory);
        Assert.Equal(graphical, set.GraphicalHostPath);
        Assert.Equal(headless, set.HeadlessHostPath);
        Assert.True(set.GetAvailability(LaunchMode.Gui).IsAvailable);
        Assert.True(set.GetAvailability(LaunchMode.GuiSelect).IsAvailable);
        Assert.True(set.GetAvailability(LaunchMode.Headless).IsAvailable);
        Assert.Equal(
            graphical,
            set.CreatePlaySpec(LaunchMode.GuiSelect, "session.json").ExecutablePath);
        Assert.Equal(
            headless,
            set.CreateProbeSpec("session.json").ExecutablePath);
        Assert.False(
            set.CreatePlaySpec(LaunchMode.Gui, "session.json")
                .SupportsConsoleGracefulStop);
        Assert.False(
            set.CreatePlaySpec(LaunchMode.GuiSelect, "session.json")
                .SupportsConsoleGracefulStop);
        Assert.True(set.CreateProbeSpec("session.json").SupportsConsoleGracefulStop);

        // Ported from the Rust launcher's
        // from_directory_resolves_the_published_co_deployment_layout
        // (crates/ac-launcher-core/tests/orchestration.rs). Every play row
        // starts the graphical client with its console on and its input left
        // open, so the launcher's console window can talk to it and its row can
        // close the window and open it again; a headless row starts it with no
        // window. Stop asks it to log off on that console: the graphical client
        // has no console of its own for a console break to reach. A probe runs
        // the windowless host without a console.
        foreach (LaunchMode mode in new[] { LaunchMode.Gui, LaunchMode.GuiSelect, LaunchMode.Headless })
        {
            var spec = set.CreatePlaySpec(mode, "session.json");
            Assert.Equal(graphical, spec.ExecutablePath);
            Assert.Equal(
                mode == LaunchMode.Headless
                    ? ["--session-config", "session.json", "--console", "--no-window"]
                    : ["--session-config", "session.json", "--console"],
                spec.Arguments);
            Assert.True(spec.KeepStandardInputOpen);
            Assert.Equal("/quit", spec.StopLine);
            Assert.False(spec.SupportsConsoleGracefulStop);
        }
        Assert.True(set.GetPlayAvailability(LaunchMode.Headless).IsAvailable);
        Assert.DoesNotContain("--console", set.CreateProbeSpec("session.json").Arguments);
        Assert.False(set.CreateProbeSpec("session.json").KeepStandardInputOpen);
        Assert.Null(set.CreateProbeSpec("session.json").StopLine);
    }

    /// <summary>
    /// Where no display exists (Linux with neither X11 nor Wayland) a headless row runs
    /// the windowless host with its console, as before; it needs only that host.
    /// </summary>
    [Fact]
    public void WithoutWindowSupportAHeadlessRowRunsTheWindowlessHost()
    {
        bool graphicalExists = false;
        var set = new LauncherExecutableSet(
            "gui-host",
            "headless-host",
            fileExists: path => path != "gui-host" || graphicalExists,
            hasUnixExecutePermission: _ => true);

        Assert.True(set.GetPlayAvailability(LaunchMode.Headless, windowSupport: false).IsAvailable);
        // With window support the headless row needs the graphical client.
        Assert.False(set.GetPlayAvailability(LaunchMode.Headless).IsAvailable);

        var spec = set.CreatePlaySpec(LaunchMode.Headless, "session.json", windowSupport: false);
        Assert.Equal("headless-host", spec.ExecutablePath);
        Assert.Equal(["run", "--config", "session.json", "--console"], spec.Arguments);
        Assert.True(spec.KeepStandardInputOpen);
        Assert.True(spec.SupportsConsoleGracefulStop);
        Assert.Null(spec.StopLine);

        graphicalExists = true;
        Assert.Equal("gui-host", set.CreatePlaySpec(LaunchMode.Headless, "session.json").ExecutablePath);
    }

    [Fact]
    public void MissingPublishedHostsHaveSpecificUnavailableReasons()
    {
        Directory.CreateDirectory(_root);
        LauncherExecutableSet set = LauncherExecutableSet.FromDirectory(_root);

        LauncherCapability gui = set.GetAvailability(LaunchMode.Gui);
        LauncherCapability probe = set.GetAvailability(LaunchMode.Headless);

        Assert.False(gui.IsAvailable);
        Assert.Contains("graphical client", gui.Reason, StringComparison.Ordinal);
        Assert.Contains(set.GraphicalHostPath, gui.Reason, StringComparison.Ordinal);
        Assert.False(probe.IsAvailable);
        Assert.Contains("headless host", probe.Reason, StringComparison.Ordinal);
        Assert.Contains(set.HeadlessHostPath, probe.Reason, StringComparison.Ordinal);
        Assert.Throws<LauncherOperationException>(() =>
            set.CreatePlaySpec(LaunchMode.Gui, "session.json"));
        Assert.Throws<LauncherOperationException>(() =>
            set.CreateProbeSpec("session.json"));
    }

    [Fact]
    [Trait("Lane", "Unix")]
    public void UnixRequiresExecutePermissionForBothCoDeployedHosts()
    {
        if (!LauncherOperatingSystem.IsUnix)
        {
            throw new PlatformNotSupportedException("Lane=Unix requires a native Unix host.");
        }

        Directory.CreateDirectory(_root);
        string graphical = Path.Combine(
            _root,
            PayloadExecutableNames.GraphicalHostForCurrentOs());
        string headless = Path.Combine(_root, "acdream-headless");
        File.WriteAllText(graphical, string.Empty);
        File.WriteAllText(headless, string.Empty);
        // A client that knows the single install folder, so only the
        // execute permission decides.
        File.WriteAllText(
            Path.Combine(_root, AcDream.Platform.ClientCapabilities.SingleInstallRootMarkerFileName),
            string.Empty);
        UnixFileMode notExecutable = UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        File.SetUnixFileMode(graphical, notExecutable);
        File.SetUnixFileMode(headless, notExecutable);
        LauncherExecutableSet set = LauncherExecutableSet.FromDirectory(_root);

        LauncherCapability gui = set.GetAvailability(LaunchMode.Gui);
        LauncherCapability headlessCapability =
            set.GetAvailability(LaunchMode.Headless);

        Assert.False(gui.IsAvailable);
        Assert.Contains("not executable", gui.Reason, StringComparison.Ordinal);
        Assert.Contains("chmod +x", gui.Reason, StringComparison.Ordinal);
        Assert.False(headlessCapability.IsAvailable);
        Assert.Contains("not executable", headlessCapability.Reason, StringComparison.Ordinal);
        Assert.Throws<LauncherOperationException>(() =>
            set.CreatePlaySpec(LaunchMode.GuiSelect, "session.json"));
        Assert.Throws<LauncherOperationException>(() =>
            set.CreateProbeSpec("session.json"));

        MakeExecutableOnUnix(graphical);
        MakeExecutableOnUnix(headless);

        Assert.True(set.GetAvailability(LaunchMode.GuiSelect).IsAvailable);
        Assert.True(set.GetAvailability(LaunchMode.Headless).IsAvailable);
    }

    [Fact]
    public void AvailabilityRequiresExecutePermissionOnlyOnUnix()
    {
        var set = new LauncherExecutableSet(
            "graphical.exe",
            "headless.exe",
            fileExists: _ => true,
            hasUnixExecutePermission: _ => false);

        Assert.Equal(
            !LauncherOperatingSystem.IsUnix,
            set.GetAvailability(LaunchMode.Gui).IsAvailable);
        Assert.Equal(
            !LauncherOperatingSystem.IsUnix,
            set.GetAvailability(LaunchMode.Headless).IsAvailable);
    }

    private static void MakeExecutableOnUnix(string path)
    {
        if (LauncherOperatingSystem.IsUnix)
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);
        }
    }
}
