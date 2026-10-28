using AcDream.Launcher.ViewModels;

namespace AcDream.Launcher.Tests;

public sealed class ClassicMigrationViewModelTests : IDisposable
{
    private readonly string _scratch = Path.Combine(
        Path.GetTempPath(),
        "openac-classic-vm-" + Guid.NewGuid().ToString("N"));

    private readonly string _vtank;
    private readonly string _profiles;

    public ClassicMigrationViewModelTests()
    {
        _vtank = Path.Combine(_scratch, "VirindiTank");
        _profiles = Path.Combine(_scratch, "OpenAC", "vtank");
        Directory.CreateDirectory(_vtank);
        File.WriteAllText(Path.Combine(_vtank, "Hunt.met"), "meta");
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, recursive: true);
    }

    /// <summary>Mutation: not pre-filling the found folder on open fails this.</summary>
    [Fact]
    public void OpeningFillsInTheFoldersThatWereFound()
    {
        var viewModel = new ClassicMigrationViewModel(_profiles, () => _vtank, () => null);

        Assert.False(viewModel.ImportCommand.CanExecute(null));
        viewModel.Open();

        Assert.True(viewModel.IsOpen);
        Assert.Equal(_vtank, viewModel.VirindiTankPath);
        Assert.Equal("", viewModel.UtilityBeltPath);
        Assert.True(viewModel.ImportCommand.CanExecute(null));
    }

    /// <summary>Mutation: applying the plan on preview fails this.</summary>
    [Fact]
    public async Task PreviewListsTheFilesAndImportCopiesThem()
    {
        var viewModel = new ClassicMigrationViewModel(_profiles, () => _vtank, () => null);
        viewModel.Open();
        string copied = Path.Combine(_profiles, "mosstank", "metas", "Hunt.met");

        await viewModel.PreviewCommand.ExecuteAsync();

        Assert.False(File.Exists(copied));
        Assert.StartsWith("Would copy 1 file(s)", viewModel.Status, StringComparison.Ordinal);
        Assert.Single(viewModel.Lines);

        await viewModel.ImportCommand.ExecuteAsync();

        Assert.Equal("meta", File.ReadAllText(copied));
        Assert.StartsWith("Copied 1 file(s)", viewModel.Status, StringComparison.Ordinal);
    }

    /// <summary>Mutation: running the plan without checking the folder fails this.</summary>
    [Fact]
    public async Task AMissingFolderIsReportedAndNothingIsCopied()
    {
        var viewModel = new ClassicMigrationViewModel(_profiles, () => null, () => null)
        {
            UtilityBeltPath = Path.Combine(_scratch, "missing"),
        };

        await viewModel.ImportCommand.ExecuteAsync();

        Assert.True(viewModel.HasError);
        Assert.Contains("does not exist", viewModel.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_profiles));
    }

    /// <summary>Mutation: ignoring a cancelled picker by clearing the path fails this.</summary>
    [Fact]
    public async Task BrowseUsesThePickedFolderAndKeepsThePathWhenCancelled()
    {
        var shell = new PickingShell { Picked = _vtank };
        var viewModel = new ClassicMigrationViewModel(_profiles, () => null, () => null);
        viewModel.AttachShell(shell);

        await viewModel.BrowseVirindiTankCommand.ExecuteAsync();
        Assert.Equal(_vtank, viewModel.VirindiTankPath);

        shell.Picked = null;
        await viewModel.BrowseVirindiTankCommand.ExecuteAsync();
        Assert.Equal(_vtank, viewModel.VirindiTankPath);
    }

    private sealed class PickingShell : IInstallFolderShell
    {
        public string? Picked { get; set; }

        public Task<bool> OpenFolderAsync(string path) => Task.FromResult(true);

        public Task CopyTextAsync(string text) => Task.CompletedTask;

        public Task<string?> PickFolderAsync(string title) => Task.FromResult(Picked);
    }
}
