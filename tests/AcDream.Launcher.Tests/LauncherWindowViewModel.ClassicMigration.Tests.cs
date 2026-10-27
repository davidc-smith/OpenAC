using AcDream.Launcher.ViewModels;

namespace AcDream.Launcher.Tests;

public sealed partial class LauncherWindowViewModelTests
{
    /// <summary>Mutation: leaving the dialog out of IsModalOpen fails this.</summary>
    [Fact]
    public void TheClassicImportDialogIsAModalOfTheWindow()
    {
        using var orchestrator = new FakeLauncherOrchestrator();
        using var window = new LauncherWindowViewModel(orchestrator, new ImmediateUiDispatcher());
        window.Initialize();
        var viewModel = new ClassicMigrationViewModel(
            Path.Combine(Path.GetTempPath(), "openac-window-import-unused"), () => null, () => null);
        window.ConfigureClassicMigration(viewModel);

        Assert.True(window.HasClassicMigration);
        viewModel.OpenCommand.Execute(null);
        Assert.True(window.IsModalOpen);

        window.CloseActiveModal();
        Assert.False(viewModel.IsOpen);
        Assert.False(window.IsModalOpen);
    }
}
