using System.Collections.ObjectModel;
using AcDream.Launcher.Core.Migration;

namespace AcDream.Launcher.ViewModels;

/// <summary>
/// The import dialog that copies a classic VirindiTank and UtilityBelt setup
/// into this install's shared profile folder, where MossTank reads it.
/// </summary>
public sealed class ClassicMigrationViewModel : ObservableObject
{
    private readonly string _vtankProfilesDirectory;
    private readonly Func<string?> _findVirindiTank;
    private readonly Func<string?> _findUtilityBelt;
    private IInstallFolderShell? _shell;
    private bool _isOpen;
    private bool _isBusy;
    private string _virindiTankPath = "";
    private string _utilityBeltPath = "";
    private bool _overwrite;
    private string? _status;
    private string? _error;

    /// <param name="vtankProfilesDirectory">The install's shared profile folder.</param>
    /// <param name="findVirindiTank">Finds a classic VirindiTank folder, or null.</param>
    /// <param name="findUtilityBelt">Finds a classic UtilityBelt folder, or null.</param>
    public ClassicMigrationViewModel(
        string vtankProfilesDirectory,
        Func<string?>? findVirindiTank = null,
        Func<string?>? findUtilityBelt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vtankProfilesDirectory);
        _vtankProfilesDirectory = vtankProfilesDirectory;
        _findVirindiTank = findVirindiTank ?? ClassicMigration.FindVirindiTank;
        _findUtilityBelt = findUtilityBelt ?? ClassicMigration.FindUtilityBelt;
        OpenCommand = new RelayCommand(Open);
        CloseCommand = new RelayCommand(Close, () => !IsBusy);
        BrowseVirindiTankCommand = new AsyncRelayCommand(
            async () => VirindiTankPath = await PickAsync("Choose the VirindiTank folder") ?? VirindiTankPath,
            () => !IsBusy);
        BrowseUtilityBeltCommand = new AsyncRelayCommand(
            async () => UtilityBeltPath = await PickAsync("Choose the UtilityBelt folder") ?? UtilityBeltPath,
            () => !IsBusy);
        PreviewCommand = new AsyncRelayCommand(() => RunAsync(apply: false), CanRun);
        ImportCommand = new AsyncRelayCommand(() => RunAsync(apply: true), CanRun);
    }

    /// <summary>Where the imported profiles go.</summary>
    public string TargetPath => Path.Combine(_vtankProfilesDirectory, ClassicMigration.MossTankFolder);

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                NotifyCommands();
        }
    }

    /// <summary>The classic VirindiTank folder; empty to leave it out.</summary>
    public string VirindiTankPath
    {
        get => _virindiTankPath;
        set
        {
            if (SetProperty(ref _virindiTankPath, value ?? ""))
                NotifyCommands();
        }
    }

    /// <summary>The classic UtilityBelt folder; empty to leave it out.</summary>
    public string UtilityBeltPath
    {
        get => _utilityBeltPath;
        set
        {
            if (SetProperty(ref _utilityBeltPath, value ?? ""))
                NotifyCommands();
        }
    }

    /// <summary>Whether a different file already in the install is replaced.</summary>
    public bool Overwrite
    {
        get => _overwrite;
        set => SetProperty(ref _overwrite, value);
    }

    /// <summary>The outcome of the last preview or import.</summary>
    public string? Status
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
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    /// <summary>One line per file of the last preview or import.</summary>
    public ObservableCollection<string> Lines { get; } = [];

    public RelayCommand OpenCommand { get; }

    public RelayCommand CloseCommand { get; }

    public AsyncRelayCommand BrowseVirindiTankCommand { get; }

    public AsyncRelayCommand BrowseUtilityBeltCommand { get; }

    public AsyncRelayCommand PreviewCommand { get; }

    public AsyncRelayCommand ImportCommand { get; }

    /// <summary>Gives the dialog the window's folder picker.</summary>
    public void AttachShell(IInstallFolderShell? shell) => _shell = shell;

    public void Open()
    {
        if (VirindiTankPath.Length == 0)
            VirindiTankPath = _findVirindiTank() ?? "";
        if (UtilityBeltPath.Length == 0)
            UtilityBeltPath = _findUtilityBelt() ?? "";
        Status = null;
        Error = null;
        Lines.Clear();
        IsOpen = true;
    }

    public void Close()
    {
        if (!IsBusy)
            IsOpen = false;
    }

    private bool CanRun() =>
        !IsBusy && (!string.IsNullOrWhiteSpace(VirindiTankPath) || !string.IsNullOrWhiteSpace(UtilityBeltPath));

    private async Task<string?> PickAsync(string title) =>
        _shell is { } shell ? await shell.PickFolderAsync(title).ConfigureAwait(true) : null;

    private async Task RunAsync(bool apply)
    {
        string? vtank = NonEmpty(VirindiTankPath);
        string? ub = NonEmpty(UtilityBeltPath);
        foreach ((string? path, string name) in new[] { (vtank, "VirindiTank"), (ub, "UtilityBelt") })
        {
            if (path is not null && !Directory.Exists(path))
            {
                Error = $"The {name} folder {path} does not exist.";
                return;
            }
        }

        Error = null;
        Status = apply ? "Importing…" : "Looking through the folders…";
        IsBusy = true;
        try
        {
            bool overwrite = Overwrite;
            IReadOnlyList<ClassicMigrationItem> items = await Task.Run(() =>
            {
                IReadOnlyList<ClassicMigrationItem> plan =
                    ClassicMigration.Plan(vtank, ub, _vtankProfilesDirectory, overwrite);
                if (apply)
                    ClassicMigration.Apply(plan);
                return plan;
            }).ConfigureAwait(true);

            Lines.Clear();
            foreach (ClassicMigrationItem item in items)
                Lines.Add(ClassicMigration.Describe(item));
            Status = ClassicMigration.Summarize(items, apply);
            if (items.Any(static item => item.Action == ClassicMigrationAction.Conflict))
                Status += " Tick \"Replace files that differ\" to replace the conflicting files.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = null;
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyCommands()
    {
        CloseCommand.NotifyCanExecuteChanged();
        BrowseVirindiTankCommand.NotifyCanExecuteChanged();
        BrowseUtilityBeltCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
    }

    private static string? NonEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
