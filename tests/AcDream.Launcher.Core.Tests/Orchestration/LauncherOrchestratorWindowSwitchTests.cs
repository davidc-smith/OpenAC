using AcDream.Launcher.Core.Launching;
using AcDream.Launcher.Core.Orchestration;
using AcDream.Launcher.Core.Profiles;
using AcDream.Launcher.Core.Status;

namespace AcDream.Launcher.Core.Tests.Orchestration;

/// <summary>
/// Switching a running session's window between graphical and headless. The switch is a
/// console line to the client; the session's window follows the client's <c>window*</c>
/// status events. Ported from the Rust launcher's tests
/// (crates/ac-launcher-core/tests/orchestration.rs, each named below).
/// </summary>
public sealed partial class LauncherOrchestratorTests
{
    private static StatusEvent WindowOpened(string sessionId) =>
        new WindowOpenedStatusEvent
        {
            V = 1,
            E = "windowOpened",
            T = DateTimeOffset.UtcNow,
            SessionId = sessionId,
        };

    private static StatusEvent WindowClosed(string sessionId, string reason) =>
        new WindowClosedStatusEvent
        {
            V = 1,
            E = "windowClosed",
            T = DateTimeOffset.UtcNow,
            SessionId = sessionId,
            Reason = reason,
        };

    private static StatusEvent WindowFailed(string sessionId, string error) =>
        new WindowFailedStatusEvent
        {
            V = 1,
            E = "windowFailed",
            T = DateTimeOffset.UtcNow,
            SessionId = sessionId,
            Error = error,
        };

    /// <summary>An orchestrator with one running <c>+Acdream</c> session in <paramref name="mode"/>.</summary>
    private async Task<(LauncherOrchestrator Core, FakeSupervisorFactory Supervisors, QueueStatusSourceFactory Statuses)>
        RunningAsync(LaunchMode mode, TimeProvider? timeProvider = null)
    {
        var supervisors = new FakeSupervisorFactory();
        var statuses = new QueueStatusSourceFactory();
        LauncherOrchestrator core = CreateOrchestrator(
            supervisorFactory: supervisors,
            statusSourceFactory: statuses,
            timeProvider: timeProvider);
        await core.LaunchAsync("Local ACE", "testaccount", "+Acdream", mode);
        return (core, supervisors, statuses);
    }

    private static LauncherSessionWindow WindowOf(LauncherOrchestrator core) =>
        Assert.Single(core.GetSnapshot().Sessions).ReportedWindow;

    // Rust: a_running_headless_session_opens_a_window_with_its_id_and_closes_it_again
    [Fact]
    public async Task ARunningHeadlessSessionOpensAWindowWithItsIdAndClosesItAgain()
    {
        var (core, supervisors, statuses) = await RunningAsync(LaunchMode.Headless);
        using (core)
        {
            LauncherSessionWindow started = WindowOf(core);
            Assert.False(started.IsOpen);
            Assert.Null(started.SwitchingTo);

            core.SetSessionWindow("s1", open: true);
            FakeSupervisor supervisor = Assert.Single(supervisors.Created);
            Assert.Equal(["/window s1"], supervisor.Lines);
            LauncherSessionWindow switching = WindowOf(core);
            Assert.Equal(true, switching.SwitchingTo);
            Assert.False(switching.IsOpen);
            Assert.True(switching.ShowsWindow);
            // No restart and no reconnect: the one process keeps running.
            Assert.Single(supervisors.Created);
            Assert.Equal(0, supervisor.StopCallCount);

            // The client reports the window open.
            QueueStatusSource source = Assert.Single(statuses.Created);
            source.Enqueue(WindowOpened("s1"));
            core.PollStatus();
            LauncherSessionWindow opened = WindowOf(core);
            Assert.True(opened.IsOpen);
            Assert.Null(opened.SwitchingTo);
            Assert.Null(opened.Error);

            // And back to headless.
            core.SetSessionWindow("s1", open: false);
            Assert.Equal(["/window s1", "/close"], supervisor.Lines);
            source.Enqueue(WindowClosed("s1", "requested"));
            core.PollStatus();
            LauncherSessionSnapshot closed = Assert.Single(core.GetSnapshot().Sessions);
            Assert.False(closed.ReportedWindow.IsOpen);
            Assert.Null(closed.ReportedWindow.SwitchingTo);
            Assert.True(closed.IsActive);
            Assert.Equal(0, supervisor.StopCallCount);

            // Asking for the state it is in sends nothing.
            core.SetSessionWindow("s1", open: false);
            Assert.Equal(2, supervisor.Lines.Count);
        }
    }

    // Rust: a_graphical_launch_counts_as_open_and_follows_the_player_closing_the_window
    [Fact]
    public async Task AGraphicalLaunchCountsAsOpenAndFollowsThePlayerClosingTheWindow()
    {
        var (core, supervisors, statuses) = await RunningAsync(LaunchMode.Gui);
        using (core)
        {
            Assert.True(WindowOf(core).IsOpen);

            core.SetSessionWindow("s1", open: false);
            Assert.Equal(["/close"], Assert.Single(supervisors.Created).Lines);
            QueueStatusSource source = Assert.Single(statuses.Created);
            source.Enqueue(WindowClosed("s1", "requested"));
            core.PollStatus();
            Assert.False(WindowOf(core).IsOpen);

            // Opened again from the row, then closed by the player.
            core.SetSessionWindow("s1", open: true);
            source.Enqueue(WindowOpened("s1"));
            source.Enqueue(WindowClosed("s1", "closedByUser"));
            core.PollStatus();
            LauncherSessionSnapshot session = Assert.Single(core.GetSnapshot().Sessions);
            Assert.False(session.ReportedWindow.IsOpen);
            Assert.Null(session.ReportedWindow.SwitchingTo);
            Assert.True(session.IsActive);
        }
    }

    // Rust: a_switch_the_client_cannot_take_fails_and_changes_nothing
    [Fact]
    public async Task ASwitchTheClientCannotTakeFailsAndChangesNothing()
    {
        var (core, supervisors, _) = await RunningAsync(LaunchMode.Headless);
        using (core)
        {
            Assert.Single(supervisors.Created).RefusesLines = true;

            var error = Assert.Throws<LauncherOperationException>(
                () => core.SetSessionWindow("s1", open: true));
            Assert.Contains("takes no console input", error.Message, StringComparison.Ordinal);
            LauncherSessionWindow window = WindowOf(core);
            Assert.False(window.IsOpen);
            Assert.Null(window.SwitchingTo);
            Assert.NotNull(window.Error);
        }
    }

    // Rust: a_window_the_client_could_not_open_reverts_the_switch_with_its_reason
    [Fact]
    public async Task AWindowTheClientCouldNotOpenRevertsTheSwitchWithItsReason()
    {
        var (core, _, statuses) = await RunningAsync(LaunchMode.Headless);
        using (core)
        {
            core.SetSessionWindow("s1", open: true);
            Assert.Single(statuses.Created).Enqueue(
                WindowFailed("s1", "this client runs without window support"));
            core.PollStatus();
            LauncherSessionSnapshot session = Assert.Single(core.GetSnapshot().Sessions);
            Assert.False(session.ReportedWindow.IsOpen);
            Assert.Null(session.ReportedWindow.SwitchingTo);
            Assert.Equal(
                "The game window could not open: this client runs without window support",
                session.ReportedWindow.Error);
            // A failed window leaves the session playing headless.
            Assert.True(session.IsActive);
        }
    }

    // Rust: an_unanswered_switch_is_given_up_after_the_timeout
    [Fact]
    public async Task AnUnansweredSwitchIsGivenUpAfterTheTimeout()
    {
        var clock = new SwitchClock(new DateTimeOffset(2027, 1, 15, 8, 0, 0, TimeSpan.Zero));
        DateTimeOffset start = clock.Now;
        var (core, supervisors, _) = await RunningAsync(LaunchMode.Headless, clock);
        using (core)
        {
            core.SetSessionWindow("s1", open: true);

            clock.Now = start + LauncherOrchestrator.WindowSwitchTimeout - TimeSpan.FromSeconds(1);
            core.PollStatus();
            Assert.Equal(true, WindowOf(core).SwitchingTo);
            // A second switch waits for the first.
            Assert.Throws<LauncherOperationException>(() => core.SetSessionWindow("s1", open: false));

            clock.Now = start + LauncherOrchestrator.WindowSwitchTimeout;
            core.PollStatus();
            LauncherSessionWindow window = WindowOf(core);
            Assert.Null(window.SwitchingTo);
            Assert.False(window.IsOpen);
            Assert.NotNull(window.Error);
            Assert.Equal(["/window s1"], Assert.Single(supervisors.Created).Lines);
        }
    }

    // Rust: only_a_running_play_session_switches
    [Fact]
    public async Task OnlyARunningPlaySessionSwitches()
    {
        var (core, supervisors, _) = await RunningAsync(LaunchMode.Headless);
        using (core)
        {
            Assert.Throws<LauncherOperationException>(() => core.SetSessionWindow("missing", open: true));
            FakeSupervisor supervisor = Assert.Single(supervisors.Created);
            supervisor.Exit(0);
            Assert.Throws<LauncherOperationException>(() => core.SetSessionWindow("s1", open: true));
            Assert.Empty(supervisor.Lines);
        }
    }

    /// <summary>
    /// A headless row started where no display exists runs the windowless host, which cannot
    /// open a window: its row cannot switch, and nothing is written to its console (the
    /// windowless host would take <c>/window</c> for chat). C#-only: the Rust launcher has one
    /// program for both.
    /// </summary>
    [Fact]
    public async Task ASessionOfTheWindowlessHostDoesNotSwitch()
    {
        var supervisors = new FakeSupervisorFactory();
        using LauncherOrchestrator core = CreateOrchestrator(
            supervisorFactory: supervisors,
            platform: WindowsCapabilities() with { HasDisplay = false });
        await core.LaunchAsync("Local ACE", "testaccount", "+Acdream", LaunchMode.Headless);

        FakeSupervisor supervisor = Assert.Single(supervisors.Created);
        Assert.Equal("headless-host", supervisor.Spec!.ExecutablePath);
        LauncherSessionWindow window = WindowOf(core);
        Assert.False(window.IsSwitchable);
        Assert.False(window.IsOpen);
        Assert.Throws<LauncherOperationException>(() => core.SetSessionWindow("s1", open: true));
        Assert.Empty(supervisor.Lines);
    }

    /// <summary>
    /// Every play row starts the graphical client with its console; a headless row adds
    /// <c>--no-window</c>. Port of the spec half of the Rust test
    /// launch_composes_the_selected_mode_and_spawns_through_the_injected_seam.
    /// </summary>
    [Theory]
    [InlineData(LaunchMode.Gui, false)]
    [InlineData(LaunchMode.GuiSelect, false)]
    [InlineData(LaunchMode.Headless, true)]
    public async Task EveryPlayRowStartsTheGraphicalClientWithItsConsole(LaunchMode mode, bool noWindow)
    {
        var config = new RecordingConfigService();
        var supervisors = new FakeSupervisorFactory();
        using LauncherOrchestrator core = CreateOrchestrator(
            configService: config,
            supervisorFactory: supervisors);
        await core.LaunchAsync("Local ACE", "testaccount", "+Acdream", mode);

        LauncherProcessSpec spec = Assert.Single(supervisors.Created).Spec!;
        Assert.Equal("gui-host", spec.ExecutablePath);
        string[] expected = noWindow
            ? ["--session-config", config.LastComposed!.ConfigFilePath, "--console", "--no-window"]
            : ["--session-config", config.LastComposed!.ConfigFilePath, "--console"];
        Assert.Equal(expected, spec.Arguments);
        Assert.True(spec.KeepStandardInputOpen);
        Assert.Equal("/quit", spec.StopLine);
    }

    private sealed class SwitchClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
