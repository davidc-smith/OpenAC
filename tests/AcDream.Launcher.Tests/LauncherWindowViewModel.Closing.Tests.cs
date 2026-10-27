using AcDream.Launcher.Core.Orchestration;
using AcDream.Launcher.ViewModels;

namespace AcDream.Launcher.Tests;

/// <summary>
/// Closing the launcher while clients it started still run asks first.
/// Ported from the Rust launcher's tests
/// (crates/ac-launcher-core/tests/window.rs:
/// closing_the_launcher_while_clients_run_asks_first,
/// closing_the_launcher_with_no_client_running_closes_at_once,
/// cancelling_the_close_keeps_everything_running).
/// </summary>
public sealed partial class LauncherWindowViewModelTests
{
    private static LauncherSessionSnapshot RunningSession(string id) =>
        FakeLauncherOrchestrator.CreateSession() with
        {
            SessionId = id,
            ServerName = "One",
            AccountName = "Alice",
            HasLiveProcess = true,
        };

    [Fact]
    public void ClosingTheLauncherWhileClientsRunAsksFirst()
    {
        using var core = BatchOrchestrator();
        core.SessionsOverride = [RunningSession("first")];
        using var vm = CreateInitialized(core);

        Assert.False(vm.RequestClose());
        Assert.True(vm.IsClosePromptOpen);
        Assert.True(vm.IsModalOpen);
        Assert.Equal("Close the launcher?", LauncherWindowViewModel.ClosePromptTitle);
        Assert.Equal(
            "Closing the launcher will log off and close 1 running client.",
            vm.ClosePromptMessage);
        Assert.Equal(
            "Closing the launcher will log off and close 3 running clients.",
            LauncherWindowViewModel.ClosePromptText(3));
        Assert.False(vm.IsCloseConfirmed);

        bool closeRequested = false;
        vm.CloseConfirmed += () => closeRequested = true;
        vm.ConfirmCloseCommand.Execute(null);

        Assert.False(vm.IsClosePromptOpen);
        Assert.True(vm.IsCloseConfirmed);
        Assert.True(closeRequested);
        // The confirmed close goes through without asking again.
        Assert.True(vm.RequestClose());
    }

    [Fact]
    public void ClosingTheLauncherWithNoClientRunningClosesAtOnce()
    {
        using var core = BatchOrchestrator();
        core.SessionsOverride = [];
        using var vm = CreateInitialized(core);

        Assert.True(vm.RequestClose());
        Assert.False(vm.IsClosePromptOpen);

        // A session that has ended does not count.
        core.SessionsOverride =
        [
            RunningSession("ended") with
            {
                State = LauncherActivityState.Exited,
                HasLiveProcess = false,
            },
        ];
        Assert.True(vm.RequestClose());
        Assert.False(vm.IsClosePromptOpen);
    }

    [Fact]
    public void ACountOfRunningClientsCountsStartsStillInFlight()
    {
        using var core = BatchOrchestrator();
        core.SessionsOverride =
        [
            RunningSession("first"),
            RunningSession("starting") with
            {
                State = LauncherActivityState.Cancelled,
                HasLiveProcess = false,
                StartupInFlight = true,
            },
        ];
        using var vm = CreateInitialized(core);

        Assert.False(vm.RequestClose());
        Assert.Equal(
            "Closing the launcher will log off and close 2 running clients.",
            vm.ClosePromptMessage);
    }

    [Fact]
    public void CancellingTheCloseKeepsEverythingRunning()
    {
        using var core = BatchOrchestrator();
        core.SessionsOverride = [RunningSession("first")];
        using var vm = CreateInitialized(core);
        bool closeRequested = false;
        vm.CloseConfirmed += () => closeRequested = true;

        Assert.False(vm.RequestClose());
        vm.CancelCloseCommand.Execute(null);
        Assert.False(vm.IsClosePromptOpen);
        Assert.False(vm.IsCloseConfirmed);

        // Escape cancels it too.
        Assert.False(vm.RequestClose());
        vm.CloseActiveModal();
        Assert.False(vm.IsClosePromptOpen);
        Assert.False(vm.IsCloseConfirmed);
        Assert.False(closeRequested);
        Assert.Null(core.StoppedSessionId);
    }
}
