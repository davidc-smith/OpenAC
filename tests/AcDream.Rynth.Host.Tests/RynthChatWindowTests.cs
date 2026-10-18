using System.Text.RegularExpressions;
using AcDream.Rynth.EngineUi;
using AcDream.TestSupport;
using AcDream.Rynth.Host;
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

    private readonly List<string> _log = new();
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };

    [Fact]
    public void ALineReachesTheChatSource()
    {
        using var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthChat.PluginExports)]);
        using var hookup = new RynthChatHookup(host, _view, _log.Add);
        RynthChatWindow.Start();
        UiSources.Chat.Subscribe();
        try
        {
            // Their chat source can keep earlier tests' lines: this line is its own.
            string token = Guid.NewGuid().ToString("N");
            host.Plugins[0].OnChatWindowText($"Bob says, \"{token}\"", 2);
            UiSources.Chat.RequestRefresh();
            RynthChatWindow.Tick();
            Assert.Contains(UiSources.Chat.Current!.Value, line => line.Text.Contains(token, StringComparison.Ordinal));
        }
        finally { UiSources.Chat.Unsubscribe(); }
    }

    [Fact]
    public void TheBinderFindsRynthChatOnlyWhileTheHostRuns()
    {
        var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthChat.PluginExports)]);
        var hookup = new RynthChatHookup(host, _view, _log.Add);
        Assert.NotEqual(IntPtr.Zero, PluginExportBinder.Resolve("RynthChat", "RynthChatSendLine"));
        hookup.Dispose();
        host.Dispose();
        Assert.Equal(IntPtr.Zero, PluginExportBinder.Resolve("RynthChat", "RynthChatSendLine"));
    }

    [Fact]
    public void TheBinderAnswersZeroOnceTheHostIsDisposedEvenBeforeTheHookupIs()
    {
        var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthChat.PluginExports)]);
        using var hookup = new RynthChatHookup(host, _view, _log.Add);
        host.Dispose();
        Assert.Equal(IntPtr.Zero, PluginExportBinder.Resolve("RynthChat", "RynthChatSendLine"));
    }

    [Fact]
    public void TheHooksAnswerFromTheView()
    {
        _view.SelectedObjectId = 0x7000_0001u;
        _view.Names[0x7000_0001u] = "Drudge";
        _view.Names[_view.PlayerId] = "Golem";
        using var host = new RynthHost(_view, _log.Add, []);
        using var hookup = new RynthChatHookup(host, _view, _log.Add);

        Assert.Equal(_view.PlayerId, RynthCore.Engine.Compatibility.ClientHelperHooks.GetPlayerId());
        Assert.Equal(0x7000_0001u, RynthCore.Engine.Compatibility.SelectedTargetHooks.ReadCurrentSelectedId());
        Assert.True(RynthCore.Engine.Compatibility.ClientObjectHooks.TryGetObjectName(0x7000_0001u, out string name));
        Assert.Equal("Drudge", name);
        Assert.Equal("Golem", RynthCore.Engine.Compatibility.SessionStateRegistry.LastCharacterName);
    }

    [Fact]
    public void AThrowingViewGivesEmptyAnswers()
    {
        using var host = new RynthHost(_view, _log.Add, []);
        using var hookup = new RynthChatHookup(host, _view, _log.Add);
        _view.Throw = true;
        Assert.Equal(0u, RynthCore.Engine.Compatibility.ClientHelperHooks.GetPlayerId());
        Assert.Equal(0u, RynthCore.Engine.Compatibility.SelectedTargetHooks.ReadCurrentSelectedId());
        Assert.False(RynthCore.Engine.Compatibility.ClientObjectHooks.TryGetObjectName(1, out _));
        Assert.False(RynthCore.Engine.Compatibility.ClientObjectHooks.TryGetItemType(1, out _));
        Assert.Equal("", RynthCore.Engine.Compatibility.SessionStateRegistry.LastCharacterName);
        Assert.Contains(_log, l => l.Contains("view failed", StringComparison.Ordinal));
    }

    [Fact]
    public void DisposingTheHookupUnpointsTheStandIns()
    {
        using var host = new RynthHost(_view, _log.Add, []);
        var hookup = new RynthChatHookup(host, _view, _log.Add);
        hookup.Dispose();
        Assert.Equal(0u, RynthCore.Engine.Compatibility.ClientHelperHooks.GetPlayerId());
        Assert.Equal(0u, RynthCore.Engine.Compatibility.SelectedTargetHooks.ReadCurrentSelectedId());
        Assert.False(RynthCore.Engine.Compatibility.ClientObjectHooks.TryGetObjectName(1, out _));
        Assert.Equal("", RynthCore.Engine.Compatibility.SessionStateRegistry.LastCharacterName);
    }

    // Their OnSend trims the box text and passes it as typed (no "/say" prefix) to RynthChatSendLine.
    [Fact]
    public void TypedTextIsSentThroughRynthChat()
    {
        using var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthChat.PluginExports)]);
        using var hookup = new RynthChatHookup(host, _view, _log.Add);
        RynthChatWindow.Start();
        UiSources.Chat.Subscribe();
        try
        {
            UiSources.Chat.RequestRefresh();
            RynthChatWindow.Tick();   // binds SendLinePtr, as their pump does
            Win32Backend.ChatCaptureActive = true;
            Win32Backend.OnChatCaptureActivated!();
            Win32Backend.OnChatChar!('h');
            Win32Backend.OnChatChar!('i');
            Win32Backend.ChatCaptureActive = false;
            Win32Backend.OnChatSend!();
            Assert.Contains("hi", _view.SubmittedCommands);
        }
        finally { UiSources.Chat.Unsubscribe(); }
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
    public void StopClearsCaptureAndTheBindings()
    {
        RynthChatWindow.Start();
        Win32Backend.ChatCaptureActive = true;
        RynthChatWindow.Stop();
        Assert.False(Win32Backend.ChatCaptureActive);
        // Their Reset keeps the lines already shown; it drops the plugin bindings.
        Assert.Equal(IntPtr.Zero, ChatModel.SendLinePtr);
    }

    [Fact]
    public void TheProjectCompilesTheirFilesAndHasNoCopies()
    {
        string root = RepoRoot();
        string project = File.ReadAllText(Path.Combine(root, "src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj"));
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
