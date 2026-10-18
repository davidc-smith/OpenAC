using System.Text.RegularExpressions;
using AcDream.Rynth.ChatWindow;
using AcDream.TestSupport;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthChatWindowTests : IDisposable
{
    private readonly RynthConfigSandbox _sandbox = new("rynth-chatwin-");

    public RynthChatWindowTests() => ImGuiPanelHost.Reset();

    public void Dispose()
    {
        RynthChatWindow.Stop();
        PluginExportBinder.Resolver = null;
        ImGuiPanelHost.Reset();
        _sandbox.Dispose();
    }

    [Fact]
    public void StartRegistersTheChatAndFilterPanelsOnce()
    {
        RynthChatWindow.Start();
        RynthChatWindow.Start();
        Assert.True(ImGuiPanelHost.HasFace("Chat"));
        Assert.True(ImGuiPanelHost.HasFace("ChatFilters"));
    }

    [Fact]
    public void StartAfterAPanelHostResetRegistersAgain()
    {
        RynthChatWindow.Start();
        ImGuiPanelHost.Reset();
        RynthChatWindow.Start();
        Assert.True(ImGuiPanelHost.HasFace("Chat"));
        Assert.True(ImGuiPanelHost.HasFace("ChatFilters"));
    }

    [Fact]
    public void TickPollsOnlyWhileSubscribed()
    {
        RynthChatWindow.Start();
        Assert.False(RynthChatWindow.Tick());
        UiSources.Chat.Subscribe();
        try
        {
            UiSources.Chat.RequestRefresh();
            Assert.True(RynthChatWindow.Tick());
        }
        finally { UiSources.Chat.Unsubscribe(); }
    }

    [Fact]
    public void StopClearsCaptureAndTheSnapshot()
    {
        RynthChatWindow.Start();
        Win32Backend.ChatCaptureActive = true;
        RynthChatWindow.Stop();
        Assert.False(Win32Backend.ChatCaptureActive);
        Assert.Null(UiSources.Chat.Current);
    }

    [Fact]
    public void TheProjectCompilesTheirFilesAndHasNoCopies()
    {
        string root = RepoRoot();
        string project = File.ReadAllText(Path.Combine(root, "src/AcDream.Rynth.ChatWindow/AcDream.Rynth.ChatWindow.csproj"));
        string[] theirs =
        [
            @"UI\Data\Chat*.cs", @"UI\Data\UiSource.cs", @"UI\Panels\RynthChatPresets.cs",
            @"ImGui\Panels\ChatFace.cs", @"ImGui\Panels\ChatFiltersFace.cs", @"ImGui\Panels\FaceKit.cs",
            @"ImGui\Panels\DashboardDrawers.cs", @"ImGui\SafeImGui.cs", @"ImGui\RynthTheme.cs",
            @"Compatibility\TellMemory.cs",
        ];
        foreach (string file in theirs)
            Assert.Contains("$(RynthEngine)" + file, project, StringComparison.Ordinal);

        string engine = Path.Combine(root, "external/RynthCore/src/RynthCore.Engine");
        var names = Directory.GetFiles(Path.Combine(engine, "UI/Data"), "Chat*.cs").Select(Path.GetFileName)
            .Concat(["UiSource.cs", "RynthChatPresets.cs", "ChatFace.cs", "ChatFiltersFace.cs", "FaceKit.cs",
                "DashboardDrawers.cs", "SafeImGui.cs", "RynthTheme.cs", "TellMemory.cs"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var copies = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => names.Contains(Path.GetFileName(p)))
            .ToList();
        Assert.Empty(copies);
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AcDream.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
