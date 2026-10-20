using System.ComponentModel;

namespace AcDream.Launcher.ViewModels;

public sealed partial class LauncherWindowViewModel
{
    public RynthNavDataViewModel NavData { get; private set; } = null!;

    /// <summary>The navdata banner, once the installation check has finished.</summary>
    public bool ShowNavDataBanner => NavData.ShowBanner && !IsInstallationChecking;

    /// <summary>Why a launch waits while the navdata folder is being swapped.</summary>
    private string? NavDataLaunchBlock => NavData.IsBusy ? "Installing RynthNav navdata…" : null;

    private void InitializeNavData(RynthNavDataViewModel? navData, IUiDispatcher dispatcher)
    {
        NavData = navData ?? new RynthNavDataViewModel(null, null, dispatcher, () => true);
        // An install nobody clicked waits for a quiet window: no dialog, setup or launch in progress.
        NavData.CanAutoInstall = () => !_disposed && CanInteract && !IsFirstRunRequired
            && _rowLaunches.Count == 0 && !_stopGate.IsPending && !_automaticRelaunch.IsLaunching
            && Sessions.All(session => !session.IsActive);
        NavData.PropertyChanged += OnModalPropertyChanged;
        NavData.PropertyChanged += OnNavDataPropertyChanged;
    }

    private void DisposeNavData()
    {
        NavData.PropertyChanged -= OnModalPropertyChanged;
        NavData.PropertyChanged -= OnNavDataPropertyChanged;
        NavData.Dispose();
    }

    private void OnNavDataPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RynthNavDataViewModel.ShowBanner))
        {
            OnPropertyChanged(nameof(ShowNavDataBanner));
        }
        else if (e.PropertyName == nameof(RynthNavDataViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(CanLaunchGui));
            OnPropertyChanged(nameof(CanLaunchHeadless));
            OnPropertyChanged(nameof(CanLaunchAccountGuiSelect));
            OnPropertyChanged(nameof(ShowGuiLaunchDisabledReason));
            OnPropertyChanged(nameof(ShowHeadlessLaunchDisabledReason));
            NotifyCommandStates();
        }
    }
}
