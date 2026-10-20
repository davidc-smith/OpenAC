using AcDream.Launcher.Core.Orchestration;
using AcDream.Launcher.Core.Installation;
using AcDream.Launcher.Core.NavData;
using AcDream.Launcher.Core.Updates;
using AcDream.Launcher.ViewModels;
using AcDream.Rynth.NavData;

namespace AcDream.Launcher.Tests;

public sealed partial class LauncherWindowViewModelTests
{
    private const string NavDataId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task RynthNavDataInstallBlocksRowLaunchesAndHoldsTheModal()
    {
        string root = Path.Combine(Path.GetTempPath(), "navdata-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pending = new TaskCompletionSource<string>();
            using var core = BatchOrchestrator();
            using var vm = CreateWithNavData(core, root, verifiedContent: false, installer: (_, _, _, _, ct) => pending.Task.WaitAsync(ct));
            await vm.StartBackgroundInitializationAsync();
            await vm.NavDataStartupCheck;
            vm.CloseActiveModal();
            var row = vm.Accounts[0].Rows[0];
            Assert.True(row.CanPlay);
            Assert.True(vm.ShowNavDataBanner);

            Task install = vm.NavData.InstallCommand.ExecuteAsync(null);
            Assert.True(vm.IsModalOpen);
            Assert.False(vm.ShowNavDataBanner);
            Assert.Equal("Installing RynthNav navdata…", row.DisabledReason);
            Assert.False(row.PlayCommand.CanExecute(null));
            Assert.False(vm.CanLaunchGui);

            vm.NavData.CancelCommand.Execute(null);
            await install;
            vm.CloseActiveModal();
            Assert.False(vm.IsModalOpen);
            Assert.True(row.CanPlay);
            Assert.True(vm.ShowNavDataBanner);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RynthNavDataAgreedInstallsAfterStartupOnItsOwn()
    {
        string root = Path.Combine(Path.GetTempPath(), "navdata-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            new LauncherNavDataConsentStore(Path.Combine(root, "config", LauncherNavDataConsentStore.FileName)).Agree();
            int installs = 0;
            using var core = BatchOrchestrator();
            using var vm = CreateWithNavData(core, root, verifiedContent: true, installer: (_, dir, _, _, _) =>
            {
                installs++;
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "navdata.id"), NavDataId);
                return Task.FromResult("installed");
            });
            await vm.StartBackgroundInitializationAsync();
            await vm.NavDataStartupCheck;
            Assert.False(vm.IsFirstRunRequired);
            Assert.Equal(1, installs);
            Assert.False(vm.ShowNavDataBanner);
            Assert.Equal("installed", vm.NavData.Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RynthNavDataAgreedInstallsRightAfterTheFirstRunSetupCompletes()
    {
        string root = Path.Combine(Path.GetTempPath(), "navdata-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            new LauncherNavDataConsentStore(Path.Combine(root, "config", LauncherNavDataConsentStore.FileName)).Agree();
            int installs = 0;
            using var orchestrator = new FakeLauncherOrchestrator
            {
                Session = FakeLauncherOrchestrator.CreateSession(LauncherActivityState.Exited, "Exited cleanly."),
            };
            var content = new FakeLauncherInstaller();
            using var vm = CreateWithNavData(orchestrator, root, verifiedContent: false, installer: (_, dir, _, _, _) =>
            {
                installs++;
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "navdata.id"), NavDataId);
                return Task.FromResult("installed");
            }, content);
            await vm.StartBackgroundInitializationAsync();
            await vm.NavDataStartupCheck;
            Assert.True(vm.IsFirstRunRequired);
            Assert.Equal(0, installs);

            vm.FirstRunWizardShell.OpenCommand.Execute(null);
            await vm.FirstRunWizardShell.StartCommand.ExecuteAsync();
            vm.CloseActiveModal();
            await vm.NavDataStartupCheck;

            Assert.False(vm.IsFirstRunRequired);
            Assert.Equal(1, installs);
            Assert.False(vm.ShowNavDataBanner);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RynthNavDataIsInertWithoutAService()
    {
        using var core = BatchOrchestrator();
        using var vm = CreateInitialized(core);
        await vm.StartBackgroundInitializationAsync();
        await vm.NavDataStartupCheck;
        Assert.False(vm.ShowNavDataBanner);
        Assert.False(vm.NavData.InstallCommand.CanExecute(null));
    }

    private static LauncherWindowViewModel CreateWithNavData(
        FakeLauncherOrchestrator orchestrator, string root, bool verifiedContent, LauncherNavDataService.Installer installer,
        FakeLauncherInstaller? contentInstaller = null)
    {
        var content = contentInstaller ?? new FakeLauncherInstaller();
        if (verifiedContent)
        {
            content.NextVerification = new InstallRecordVerification(
                InstallRecordVerificationState.Verified, content.Record, "Client content verified.");
        }

        var pin = new NavDataPin("https://x.invalid/a.zip", 1572337084, NavDataId, 40203, 4731616273, NavDataId, "s", "2026-10-19");
        var service = new LauncherNavDataService(new NullManifestClient(), () => pin, Path.Combine(root, "NavData"),
            Path.Combine(root, "cache"), new UpdateSessionBarrier(Path.Combine(root, "data")), () => false, installer);
        var navData = new RynthNavDataViewModel(service,
            new LauncherNavDataConsentStore(Path.Combine(root, "config", LauncherNavDataConsentStore.FileName)),
            new ImmediateUiDispatcher(), () => true);
        var viewModel = new LauncherWindowViewModel(orchestrator, new ImmediateUiDispatcher(), content, navData: navData);
        viewModel.Initialize();
        return viewModel;
    }

    private sealed class NullManifestClient : IReleaseManifestClient
    {
        public Task<ReleaseManifest> FetchAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
