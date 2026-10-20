using AcDream.Launcher.Core.NavData;
using AcDream.Launcher.Core.Updates;
using AcDream.Launcher.ViewModels;
using AcDream.Rynth.NavData;

namespace AcDream.Launcher.Tests;

public sealed class RynthNavDataViewModelTests : IDisposable
{
    private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly NavDataPin Pin = new("https://x.invalid/a.zip", 1572337084, A, 40203, 4731616273, A, "s", "2026-10-19");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "navdata-vm-" + Guid.NewGuid().ToString("N"));
    private int _installs;
    private bool _sessionRunning;
    private TaskCompletionSource<string>? _pendingInstall;
    private Exception? _installFailure;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string NavDataDir => Path.Combine(_root, "NavData");

    private LauncherNavDataConsentStore Consent => new(Path.Combine(_root, "config", LauncherNavDataConsentStore.FileName));

    private RynthNavDataViewModel Create() =>
        new(new LauncherNavDataService(new PinClient(), () => Pin, NavDataDir, Path.Combine(_root, "cache"),
                new UpdateSessionBarrier(Path.Combine(_root, "data")), () => _sessionRunning,
                (_, dir, _, progress, ct) =>
                {
                    _installs++;
                    progress?.Report(new NavDataProgress(NavDataStage.Downloading, 50, "half"));
                    if (_installFailure is { } failure) return Task.FromException<string>(failure);
                    if (_pendingInstall is { } pending) return pending.Task.WaitAsync(ct);
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "navdata.id"), A);
                    return Task.FromResult("installed");
                }),
            Consent, new ImmediateUiDispatcher(), canStartInstall: () => !_sessionRunning);

    [Fact]
    public async Task MissingAndNotAgreedShowsTheBanner()
    {
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        Assert.True(vm.ShowBanner);
        Assert.Equal("RynthNav navdata — 1.6 GB download, 4.7 GB on disk", vm.BannerText);
        Assert.Equal(0, _installs);
    }

    [Fact]
    public async Task OutdatedAndNotAgreedShowsTheBanner()
    {
        Directory.CreateDirectory(NavDataDir);
        File.WriteAllText(Path.Combine(NavDataDir, "navdata.id"), new string('b', 64));
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        Assert.True(vm.ShowBanner);
        Assert.Equal(0, _installs);
    }

    [Theory]
    [InlineData("navdata.local", "")]
    [InlineData("navdata.id", "0123456789abcdef0123456789abcdef")]
    public async Task LocalOrRynthCoreTilesNeedNothing(string file, string content)
    {
        Directory.CreateDirectory(NavDataDir);
        File.WriteAllText(Path.Combine(NavDataDir, file), content);
        Consent.Agree();
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        Assert.False(vm.ShowBanner);
        Assert.Equal(0, _installs);
    }

    [Fact]
    public async Task NotNowHidesForThisRunOnly()
    {
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        vm.NotNowCommand.Execute(null);
        Assert.False(vm.ShowBanner);
        Assert.False(Consent.IsAgreed);
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        Assert.False(vm.ShowBanner);
        RynthNavDataViewModel next = Create();
        await next.CheckAsync(TestContext.Current.CancellationToken);
        Assert.True(next.ShowBanner);
    }

    [Fact]
    public async Task InstallAgreesInstallsAndHidesTheBanner()
    {
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.True(Consent.IsAgreed);
        Assert.Equal(1, _installs);
        Assert.False(vm.ShowBanner);
        Assert.Equal("installed", vm.Status);
        Assert.True(vm.IsOpen);
        vm.CloseCommand.Execute(null);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public async Task TheMenuInstallsEvenAfterNotNowAndBeforeAnyCheck()
    {
        RynthNavDataViewModel vm = Create();
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.Equal(1, _installs);
        Assert.True(Consent.IsAgreed);

        RynthNavDataViewModel dismissed = Create();
        await dismissed.CheckAsync(TestContext.Current.CancellationToken);
        dismissed.NotNowCommand.Execute(null);
        await dismissed.InstallCommand.ExecuteAsync(null);
        Assert.Equal(2, _installs);
    }

    [Fact]
    public async Task AgreedAndMissingInstallsOnItsOwn()
    {
        Consent.Agree();
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, _installs);
        Assert.False(vm.ShowBanner);
    }

    [Fact]
    public async Task AnAutomaticInstallClosesItsDialogOnSuccess()
    {
        Consent.Agree();
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, _installs);
        Assert.False(vm.IsOpen);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task AnAutomaticInstallLeavesItsDialogOpenOnFailure()
    {
        Consent.Agree();
        _installFailure = new IOException("disk full");
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        Assert.True(vm.IsOpen);
        Assert.Contains("disk full", vm.Error);
    }

    [Fact]
    public async Task AnAutomaticInstallLeavesItsDialogOpenWhenCancelled()
    {
        Consent.Agree();
        _pendingInstall = new TaskCompletionSource<string>();
        RynthNavDataViewModel vm = Create();
        Task check = vm.CheckAsync(TestContext.Current.CancellationToken);
        vm.CancelCommand.Execute(null);
        await check;
        Assert.True(vm.IsOpen);
        Assert.True(vm.HasError);
    }

    [Fact]
    public async Task AManualInstallKeepsItsDialogOpenOnSuccess()
    {
        RynthNavDataViewModel vm = Create();
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.True(vm.IsOpen);
    }

    [Theory]
    [InlineData("navdata.id", "0123456789abcdef0123456789abcdef", "RynthCore's own launcher manages")]
    [InlineData("navdata.local", "", "holds your own tiles (navdata.local); left alone.")]
    public async Task AManualInstallOverAnUnmanagedFolderSaysSoInLauncherWords(string file, string content, string expected)
    {
        Directory.CreateDirectory(NavDataDir);
        File.WriteAllText(Path.Combine(NavDataDir, file), content);
        RynthNavDataViewModel vm = Create();
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.Equal(0, _installs);
        Assert.Contains(expected, vm.Status);
        Assert.Contains(NavDataDir, vm.Status);
        Assert.DoesNotContain("--", vm.Status);
        Assert.False(vm.HasError);
        Assert.True(vm.IsOpen);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task AgreedButASessionRunsWaits()
    {
        Consent.Agree();
        _sessionRunning = true;
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, _installs);
        Assert.False(vm.ShowBanner);
        _sessionRunning = false;
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, _installs);
    }

    [Fact]
    public async Task AgreedButTheWindowIsBusyWaits()
    {
        Consent.Agree();
        RynthNavDataViewModel vm = Create();
        vm.CanAutoInstall = () => false;
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, _installs);
    }

    [Fact]
    public async Task CurrentTilesNeedNothing()
    {
        Directory.CreateDirectory(NavDataDir);
        File.WriteAllText(Path.Combine(NavDataDir, "navdata.id"), A);
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        Assert.False(vm.ShowBanner);
        Assert.Equal(0, _installs);
    }

    [Fact]
    public async Task BusyWhileInstallingAndCancelStopsIt()
    {
        _pendingInstall = new TaskCompletionSource<string>();
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        Task running = vm.InstallCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.True(vm.IsOpen);
        Assert.Equal(50, vm.ProgressPercent);
        Assert.False(vm.IsProgressIndeterminate);
        Assert.Equal("half", vm.Status);
        Assert.False(vm.InstallCommand.CanExecute(null));
        Assert.False(vm.CloseCommand.CanExecute(null));
        vm.CancelCommand.Execute(null);
        await running;
        Assert.False(vm.IsBusy);
        Assert.True(vm.HasError);
        Assert.True(vm.ShowBanner);
    }

    [Fact]
    public async Task AFailureShowsTheErrorAndKeepsTheBanner()
    {
        _installFailure = new IOException("disk full");
        Consent.Agree();
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, _installs);
        Assert.True(vm.HasError);
        Assert.Contains("disk full", vm.Error);
        Assert.True(vm.ShowBanner);
    }

    [Fact]
    public async Task ARunningSessionRefusesTheManualInstall()
    {
        _sessionRunning = true;
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.Equal(0, _installs);
        Assert.True(vm.HasError);
        Assert.True(vm.ShowBanner);
    }

    [Fact]
    public async Task WithoutAServiceItIsInert()
    {
        var vm = new RynthNavDataViewModel(null, null, new ImmediateUiDispatcher(), () => true);
        await vm.CheckAsync(TestContext.Current.CancellationToken);
        Assert.False(vm.ShowBanner);
        Assert.Equal(string.Empty, vm.BannerText);
        Assert.False(vm.InstallCommand.CanExecute(null));
    }

    private sealed class PinClient : IReleaseManifestClient
    {
        public Task<ReleaseManifest> FetchAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
