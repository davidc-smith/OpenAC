using System.Net;
using AcDream.Launcher.Core.Plugins;
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

        viewModel.OpenCommand.Execute(null);
        Assert.True(window.IsModalOpen);

        window.CloseActiveModal();
        Assert.False(viewModel.IsOpen);
        Assert.False(window.IsModalOpen);
    }

    /// <summary>
    /// The import is only offered once MossTank is installed, and the button
    /// appears when the installed list learns of it. Mutation: showing the
    /// button whenever the dialog is configured, or not re-raising
    /// ShowClassicMigration when the installed list changes, fails this.
    /// </summary>
    [Fact]
    public async Task TheClassicImportIsOfferedOnlyWhenMossTankIsInstalled()
    {
        using var fixture = new PluginPanelFixture();
        fixture.WriteManifest("someone.other", "1.0.0", ["graphical"]);
        var handler = new RoutedHandler(request => request.RequestUri == PluginListUri
            ? Ok(fixture.ListJson())
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        using LauncherPluginComposition composition = LauncherPluginComposition.CreateForTest(
            fixture.Paths, PluginListUri, handler);
        using var orchestrator = new FakeLauncherOrchestrator();
        using var viewModel = CreateInitialized(orchestrator);
        viewModel.ConfigurePlugins(composition, () => null);
        viewModel.ConfigureClassicMigration(new ClassicMigrationViewModel(
            fixture.Paths.VtankProfilesDirectory, () => null, () => null));

        await viewModel.Plugins.CheckNowCommand.ExecuteAsync();
        Assert.False(viewModel.ShowClassicMigration);

        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        fixture.WriteManifest("acdream.mosstank", "1.0.0", ["graphical", "headless"]);
        await viewModel.Plugins.CheckNowCommand.ExecuteAsync();

        Assert.True(viewModel.ShowClassicMigration);
        Assert.Contains(nameof(LauncherWindowViewModel.ShowClassicMigration), raised);
    }
}
