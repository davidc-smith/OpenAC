using System.Globalization;
using AcDream.Launcher.Core.NavData;
using AcDream.Rynth.NavData;

namespace AcDream.Launcher.ViewModels;

/// <summary>
/// The RynthNav navdata the client's navigation reads: a banner offering the download once, then
/// kept current on its own (at startup and after a client update), and a progress dialog.
/// </summary>
public sealed class RynthNavDataViewModel : ObservableObject, IDisposable
{
    private readonly LauncherNavDataService? _service;
    private readonly LauncherNavDataConsentStore? _consent;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<bool> _canStartInstall;
    private NavDataCheck? _check;
    private bool _dismissed;
    private bool _disposed;
    private bool _isBusy;
    private bool _isOpen;
    private string _status = string.Empty;
    private string? _error;
    private double _progressPercent;
    private bool _isProgressIndeterminate = true;
    private CancellationTokenSource? _cancellation;

    public RynthNavDataViewModel(
        LauncherNavDataService? service,
        LauncherNavDataConsentStore? consent,
        IUiDispatcher dispatcher,
        Func<bool> canStartInstall)
    {
        _service = service;
        _consent = consent;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _canStartInstall = canStartInstall ?? throw new ArgumentNullException(nameof(canStartInstall));
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => _service is not null && !IsBusy);
        NotNowCommand = new RelayCommand(NotNow, () => !IsBusy);
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsBusy && _cancellation is not null);
        CloseCommand = new RelayCommand(Close, () => !IsBusy);
    }

    public AsyncRelayCommand InstallCommand { get; }

    public RelayCommand NotNowCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand CloseCommand { get; }

    /// <summary>The window's own conditions for an install nobody clicked (no dialog open, no
    /// launch starting); a manual install is refused by the service if a session runs.</summary>
    public Func<bool> CanAutoInstall { get; set; } = () => true;

    /// <summary>Offered until agreed; after that only when an install failed.</summary>
    public bool ShowBanner =>
        !_dismissed && !IsBusy && NeedsInstall && (_consent?.IsAgreed != true || HasError);

    public string BannerText => _check is null
        ? string.Empty
        : $"RynthNav navdata — {Gb(_check.Pin.Size)} download, {Gb(_check.Pin.Bytes)} on disk";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowBanner));
                NotifyCommandStates();
            }
        }
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(ShowBanner));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    private bool NeedsInstall =>
        _check?.Inspection.State is NavDataInstallState.Missing or NavDataInstallState.Outdated;

    /// <summary>Reads the pin and the folder; once agreed, an install that is due starts on its own
    /// when no session runs (otherwise the next startup or client update tries again).</summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || IsBusy || _disposed)
            return;
        if (!await RefreshAsync(cancellationToken).ConfigureAwait(true))
            return;
        if (NeedsInstall && _consent?.IsAgreed == true && _canStartInstall() && CanAutoInstall()
            && !cancellationToken.IsCancellationRequested)
        {
            await RunInstallAsync().ConfigureAwait(true);
        }
    }

    public void Close()
    {
        if (!IsBusy)
            IsOpen = false;
    }

    public void NotifyCommandStates()
    {
        InstallCommand.NotifyCanExecuteChanged();
        NotNowCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        CloseCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cancellation?.Cancel();
    }

    private async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            _check = await _service!.CheckAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            // Never fatal: the launcher works without navdata, and the next check tries again.
            Status = "RynthNav navdata could not be checked: " + ex.Message;
            return false;
        }

        OnPropertyChanged(nameof(BannerText));
        OnPropertyChanged(nameof(ShowBanner));
        return true;
    }

    private async Task InstallAsync()
    {
        if (_service is null || IsBusy || _disposed)
            return;
        try
        {
            _consent?.Agree();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The install still runs; the banner simply asks again next time.
        }

        OnPropertyChanged(nameof(ShowBanner));
        if (_check is null && !await RefreshAsync(CancellationToken.None).ConfigureAwait(true))
            return;
        await RunInstallAsync().ConfigureAwait(true);
    }

    private async Task RunInstallAsync()
    {
        if (_service is null || _check is null || IsBusy)
            return;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Error = null;
        Status = "Starting the RynthNav navdata install…";
        ProgressPercent = 0;
        IsProgressIndeterminate = true;
        IsOpen = true;
        IsBusy = true;
        try
        {
            var progress = new DispatchedProgress(_dispatcher, report =>
            {
                if (!string.IsNullOrWhiteSpace(report.Detail))
                    Status = report.Detail;
                IsProgressIndeterminate = report.Percent is null;
                ProgressPercent = report.Percent ?? 0;
            });
            string result = await _service.InstallAsync(_check, progress, cancellation.Token).ConfigureAwait(true);
            Status = result;
            IsProgressIndeterminate = false;
            ProgressPercent = 100;
            await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            // The refresh may report its own failure; the install's message is what the player asked about.
            Status = result;
        }
        catch (OperationCanceledException)
        {
            IsProgressIndeterminate = false;
            Error = "The RynthNav navdata install was cancelled; the installed tiles are unchanged.";
        }
        catch (Exception ex)
        {
            IsProgressIndeterminate = false;
            Error = "The RynthNav navdata install failed: "
                + (string.IsNullOrWhiteSpace(ex.Message) ? "the operation failed." : ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
                _cancellation = null;
            IsBusy = false;
            OnPropertyChanged(nameof(ShowBanner));
        }
    }

    private void NotNow()
    {
        _dismissed = true;
        OnPropertyChanged(nameof(ShowBanner));
    }

    private static string Gb(long bytes) =>
        (bytes / 1e9).ToString("0.0", CultureInfo.InvariantCulture) + " GB";

    private sealed class DispatchedProgress(IUiDispatcher dispatcher, Action<NavDataProgress> apply)
        : IProgress<NavDataProgress>
    {
        public void Report(NavDataProgress value) => dispatcher.Post(() => apply(value));
    }
}
