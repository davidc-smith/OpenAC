using AcDream.Launcher.Core.Orchestration;
using AcDream.Launcher.Core.Profiles;
using AcDream.Launcher.ViewModels;

namespace AcDream.Launcher.Tests;

/// <summary>
/// A running row's mode box switches its session between graphical and headless without a
/// restart. Ported from the Rust launcher's tests
/// (crates/ac-launcher-core/tests/window.rs, each named below).
/// </summary>
public sealed partial class LauncherWindowViewModelTests
{
    /// <summary>A batch window whose Alice on One play session runs with a window.</summary>
    private static FakeLauncherOrchestrator RunningGraphical()
    {
        FakeLauncherOrchestrator core = BatchOrchestrator();
        core.SessionsOverride =
        [
            RunningSession("session-1") with { Window = new LauncherSessionWindow(IsOpen: true) },
        ];
        return core;
    }

    private static LauncherAccountServerRowViewModel AliceOnOne(LauncherWindowViewModel vm) =>
        vm.Accounts.Single(group => group.AccountName == "Alice").Rows.Single(row => row.ServerName == "One");

    // Rust: a_running_rows_mode_box_switches_its_session_without_a_restart
    [Fact]
    public void ARunningRowsModeBoxSwitchesItsSessionWithoutARestart()
    {
        using FakeLauncherOrchestrator core = RunningGraphical();
        using var vm = CreateInitialized(core);
        LauncherAccountServerRowViewModel row = AliceOnOne(vm);
        Assert.True(row.IsActive);
        Assert.True(row.CanSwitchWindow);
        Assert.True(row.CanChangeLaunchMode);
        Assert.Equal("Graphical", row.DisplayedLaunchMode);
        Assert.False(row.CanOpenConsole);

        row.DisplayedLaunchMode = "Headless";

        // The running session is asked to close its window: no launch, no stop.
        Assert.Equal([("session-1", false)], core.WindowSwitches);
        Assert.Empty(core.LaunchRequests);
        Assert.Null(core.StoppedSessionId);
        // The next launch follows the choice, as an idle row's pick does.
        Assert.Equal(LaunchMode.Headless, core.SavedRowSelection?.Mode);
        // While the client has not answered, the box shows the choice and takes no other.
        row = AliceOnOne(vm);
        Assert.Equal("Headless", row.DisplayedLaunchMode);
        Assert.False(row.CanSwitchWindow);
        Assert.False(row.CanChangeLaunchMode);
        row.DisplayedLaunchMode = "Graphical";
        Assert.Single(core.WindowSwitches);

        // The client reports the window closed: the row is headless now and its console opens.
        core.UpdateSession("session-1", session => session with
        {
            Window = session.ReportedWindow with { SwitchingTo = null, IsOpen = false },
        });
        core.RaiseStateChanged();
        row = AliceOnOne(vm);
        Assert.Equal("Headless", row.DisplayedLaunchMode);
        Assert.True(row.CanSwitchWindow);
        Assert.True(row.CanOpenConsole);
        Assert.Null(vm.LastError);

        // And back: the window is asked for again.
        row.DisplayedLaunchMode = "Graphical";
        Assert.Equal(("session-1", true), core.WindowSwitches[^1]);
    }

    // Rust: a_switch_that_cannot_be_sent_puts_the_box_back_and_says_why
    [Fact]
    public void ASwitchThatCannotBeSentPutsTheBoxBackAndSaysWhy()
    {
        using FakeLauncherOrchestrator core = RunningGraphical();
        core.WindowSwitchRefusal = "Could not switch the session: its client takes no console input.";
        using var vm = CreateInitialized(core);

        AliceOnOne(vm).DisplayedLaunchMode = "Headless";

        LauncherAccountServerRowViewModel row = AliceOnOne(vm);
        Assert.Equal("Graphical", row.DisplayedLaunchMode);
        Assert.Equal("Graphical", row.SelectedLaunchMode);
        Assert.Null(core.SavedRowSelection);
        Assert.Equal("Could not switch the session: its client takes no console input.", vm.LastError);
    }

    // Rust: a_window_the_client_could_not_open_puts_the_box_back_and_says_why
    [Fact]
    public void AWindowTheClientCouldNotOpenPutsTheBoxBackAndSaysWhy()
    {
        using FakeLauncherOrchestrator core = BatchOrchestrator();
        core.SessionsOverride =
        [
            RunningSession("session-1") with
            {
                LaunchMode = LaunchMode.Headless,
                Window = new LauncherSessionWindow(IsOpen: false),
            },
        ];
        using var vm = CreateInitialized(core);
        Assert.Equal("Headless", AliceOnOne(vm).DisplayedLaunchMode);

        AliceOnOne(vm).DisplayedLaunchMode = "Graphical";
        Assert.Equal([("session-1", true)], core.WindowSwitches);
        Assert.Equal("Graphical", AliceOnOne(vm).DisplayedLaunchMode);

        // The client answers that no window opened.
        core.UpdateSession("session-1", session => session with
        {
            Window = session.ReportedWindow with
            {
                SwitchingTo = null,
                Error = "The game window could not open: no window support",
            },
        });
        core.RaiseStateChanged();
        Assert.Equal("Headless", AliceOnOne(vm).DisplayedLaunchMode);
        Assert.Equal("The game window could not open: no window support", vm.LastError);
    }

    // Rust: a_row_that_is_starting_does_not_switch
    [Fact]
    public void ARowThatIsStartingDoesNotSwitch()
    {
        using FakeLauncherOrchestrator core = BatchOrchestrator();
        core.SessionsOverride = [RunningSession("session-1") with { StartupInFlight = true }];
        using var vm = CreateInitialized(core);
        Assert.False(AliceOnOne(vm).CanSwitchWindow);
        AliceOnOne(vm).DisplayedLaunchMode = "Headless";
        Assert.Empty(core.WindowSwitches);
    }

    /// <summary>
    /// A session of the windowless host (a headless row where no display exists) cannot open
    /// a window: its row keeps the console and takes no switch. C#-only.
    /// </summary>
    [Fact]
    public void ARowWhoseClientCannotOpenAWindowDoesNotSwitch()
    {
        using FakeLauncherOrchestrator core = BatchOrchestrator();
        core.SessionsOverride =
        [
            RunningSession("session-1") with
            {
                LaunchMode = LaunchMode.Headless,
                Window = new LauncherSessionWindow(IsOpen: false, IsSwitchable: false),
            },
        ];
        using var vm = CreateInitialized(core);
        LauncherAccountServerRowViewModel row = AliceOnOne(vm);
        Assert.False(row.CanSwitchWindow);
        Assert.True(row.CanOpenConsole);
        Assert.Equal("Headless", row.DisplayedLaunchMode);
        row.DisplayedLaunchMode = "Graphical";
        Assert.Empty(core.WindowSwitches);
    }
}
