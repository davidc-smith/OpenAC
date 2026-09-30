using AcDream.Launcher.Core.Orchestration;
using AcDream.Launcher.Core.Profiles;

namespace AcDream.Launcher.Tests;

public sealed partial class LauncherWindowViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdlePollDoesNotRebuildProfileOrSessionSnapshots(bool automatic)
    {
        using var core = BatchOrchestrator();
        core.Session = FakeLauncherOrchestrator.CreateSession() with { ServerName = "One", AccountName = "Alice", HasLiveProcess = true };
        using var vm = CreateInitialized(core);
        vm.Accounts[0].Rows[0].IsChecked = true;
        vm.RelaunchCheckedClientsAutomatically = automatic;
        int before = core.SnapshotReadCount;
        for (int index = 0; index < 50; index++) vm.PollStatus();
        Assert.Equal(before, core.SnapshotReadCount);
    }

    [Fact]
    public void CheckedFilterCombinesWithProfilesAndKeepsSelectionsWhileActiveCanBeUnchecked()
    {
        using var core = ProfiledOrchestrator();
        core.Session = FakeLauncherOrchestrator.CreateSession() with { ServerName = "One", AccountName = "Alice", HasLiveProcess = true };
        using var vm = CreateInitialized(core);
        Assert.False(vm.ShowOnlyCheckedAccounts);
        Assert.False(vm.RelaunchCheckedClientsAutomatically);
        Assert.Equal("180", vm.RelaunchDelaySecondsText);
        var row = vm.Accounts[0].Rows[0];
        Assert.False(row.CanEditSelection);
        row.IsChecked = true;
        vm.Accounts[0].Rows[1].IsChecked = true;
        vm.ShowOnlyCheckedAccounts = true;
        Assert.Equal([true, false], vm.Accounts.Select(group => group.IsVisible));
        vm.ProfileFilter = "bots";
        Assert.Equal([true, false], vm.Accounts.Select(group => group.IsVisible));
        Assert.Equal([false, true], vm.Accounts[0].Rows.Select(item => item.IsVisible));
        Assert.True(row.IsChecked);
        row.IsChecked = false;
        vm.ProfileFilter = null;
        Assert.Equal([true, false], vm.Accounts.Select(group => group.IsVisible));
        vm.ShowOnlyCheckedAccounts = false;
        Assert.All(vm.Accounts, group => Assert.True(group.IsVisible));
        Assert.True(vm.Accounts[0].Rows[1].IsChecked);
    }

    [Theory]
    [InlineData("return")]
    [InlineData("throw")]
    [InlineData("cancel")]
    public async Task AllStopButtonsStayDisabledUntilActualRequestedProcessExit(string result)
    {
        using var core = BatchOrchestrator();
        var first = FakeLauncherOrchestrator.CreateSession() with { ServerName = "One", AccountName = "Alice", HasLiveProcess = true };
        var second = first with { SessionId = "second", AccountName = "Bob" };
        core.SessionsOverride = [first, second];
        core.StopHandler = _ => result switch
        {
            "throw" => throw new IOException("stop failed"),
            "cancel" => throw new OperationCanceledException(),
            _ => Task.CompletedTask,
        };
        using var vm = CreateInitialized(core);
        var row = vm.Accounts[0].Rows[0];
        await row.StopCommand.ExecuteAsync();
        Assert.False(vm.IsBusy);
        Assert.All(vm.Accounts.SelectMany(group => group.Rows), item => Assert.False(item.StopCommand.CanExecute(null)));
        Assert.All(vm.Sessions, item => Assert.False(item.StopCommand.CanExecute(null)));
        core.SessionsOverride = [first with { State = LauncherActivityState.Exited }, second];
        core.RaiseStateChanged();
        Assert.False(vm.Accounts[1].Rows[0].StopCommand.CanExecute(null));
        Assert.False(vm.Accounts[0].Rows[1].PlayCommand.CanExecute(null));
        // A stopped session still finishing startup retains the gate too.
        core.SessionsOverride = [first with { State = LauncherActivityState.Cancelled, HasLiveProcess = false, StartupInFlight = true }, second];
        core.RaiseStateChanged();
        Assert.False(vm.Accounts[1].Rows[0].StopCommand.CanExecute(null));
        core.SessionsOverride = [first with { State = LauncherActivityState.Exited, HasLiveProcess = false }, second];
        core.RaiseStateChanged();
        Assert.True(vm.Accounts[1].Rows[0].StopCommand.CanExecute(null));
        Assert.True(vm.Sessions.Single(item => item.SessionId == "second").StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task AutoRelaunchIncludesHiddenCheckedAccountsAndRespectsInvalidDelay()
    {
        using var core = ProfiledOrchestrator(); var clock = new RelaunchClock();
        core.Session = FakeLauncherOrchestrator.CreateSession() with { ServerName = "One", AccountName = "Alice", HasLiveProcess = true };
        using var vm = CreateInitialized(core, timeProvider: clock);
        await vm.StartBackgroundInitializationAsync(); vm.CloseActiveModal();
        var row = vm.Accounts[0].Rows[0]; row.IsChecked = true;
        vm.RelaunchCheckedClientsAutomatically = true;
        vm.RelaunchDelaySecondsText = "5";
        vm.ProfileFilter = "bots";
        Assert.False(vm.Accounts[0].Rows[0].IsVisible);
        core.Session = core.Session with { State = LauncherActivityState.Exited, HasLiveProcess = false };
        core.RaiseStateChanged();
        clock.Advance(5);
        vm.RelaunchDelaySecondsText = "4.5";
        vm.PollStatus(); await vm.AutomaticRelaunchTask;
        Assert.Empty(core.LaunchRequests);
        vm.RelaunchDelaySecondsText = "5";
        vm.PollStatus(); await vm.AutomaticRelaunchTask;
        Assert.Equal(("One", "Alice"), (Assert.Single(core.LaunchRequests).Server, core.LaunchRequests[0].Account));
    }

    private sealed class RelaunchClock : TimeProvider
    {
        private long _seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => _seconds;
        public void Advance(int seconds) => _seconds += seconds;
    }
}
