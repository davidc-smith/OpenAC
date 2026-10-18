using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthAiDashboardTests : IDisposable
{
    private static readonly string[] TheirFiles =
    [
        @"ImGui\Panels\RynthAiFace.cs", @"ImGui\Panels\RynthAiFace.Drawers.cs", @"ImGui\Panels\SettingsRows.cs",
        @"ImGui\Panels\QuickSettingsDrawer.cs", @"UI\Data\RynthAiData.cs", @"UI\Data\SettingsData.cs",
        @"UI\Data\NavData.cs", @"UI\Data\RynthSortData.cs", @"UI\RynthAiDashboardState.cs",
        @"UI\PanelTextScale.cs", @"UI\QuickSettings.cs", @"UI\BarLayout.cs",
    ];

    private readonly List<string> _log = new();

    public RynthAiDashboardTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
    }

    public void Dispose()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
    }

    [Fact]
    public void TheProjectCompilesTheirFilesAndHasNoCopies()
    {
        string root = RepoRoot();
        string project = File.ReadAllText(Path.Combine(root, "src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj"));
        foreach (string file in TheirFiles)
            Assert.Contains("$(RynthEngine)" + file, project, StringComparison.Ordinal);

        var names = TheirFiles.Select(f => f[(f.LastIndexOf('\\') + 1)..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var copies = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => names.Contains(Path.GetFileName(p)))
            .ToList();
        Assert.Empty(copies);
    }

    [Fact]
    public void TheDashboardRegistersAndDrawsHeadless()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        RynthAiFace.Register();
        Assert.True(ImGuiPanelHost.HasFace(RynthAiFace.Title));
        ImGuiPanelHost.Open(RynthAiFace.Title);

        for (int i = 0; i < 3; i++)
        {
            int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
            Assert.True(vertices > 100, $"frame {i}: only {vertices} vertices");
        }

        Assert.Empty(_log);
    }

    internal static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AcDream.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
