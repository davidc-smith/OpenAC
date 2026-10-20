using System.Reflection;
using AcDream.Rynth.Host;
using AcDream.TestSupport;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.Hotkeys;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthHotkeysTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-hotkeys-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000007u };
    private readonly string _file;
    private readonly string? _oldFile = Environment.GetEnvironmentVariable(HotkeyStore.EnvOverride);

    public RynthHotkeysTests()
    {
        _file = Path.Combine(Directory.CreateTempSubdirectory("rynth-hotkeys-file-").FullName, "hotkeys.txt");
        Environment.SetEnvironmentVariable(HotkeyStore.EnvOverride, _file);
        ResetHotkeyService();
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
        UiDataHub.Clear();
    }

    public void Dispose()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Stop();
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
        ResetHotkeyService();
        Environment.SetEnvironmentVariable(HotkeyStore.EnvOverride, _oldFile);
        _sandbox.Dispose();
    }

    /// <summary>HotkeyService keeps one generation's statics; each test starts from an unloaded file.</summary>
    internal static void ResetHotkeyService()
    {
        HotkeyService.Shutdown();
        Type service = typeof(HotkeyService);
        service.GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
        service.GetField("_nextFileCheckMs", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0L);
        service.GetField("_file", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, new HotkeyFile(true, new List<HotkeyBinding>()));
    }

    internal static string WaitForFile(string path, Func<string, bool> done)
    {
        for (int i = 0; i < 100; i++)
        {
            string text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            if (done(text)) return text;
            Thread.Sleep(20);
        }
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private List<string> Rc(string arguments)
    {
        var lines = new List<string>();
        RynthCoreChatCommands.TryHandle(arguments, lines.Add);
        return lines;
    }

    [Fact]
    public void RcHotkeyAddsListsAndRemovesAndSavesTheFile()
    {
        List<string> added = Rc("hotkey add ctrl+f1 /ra help");
        Assert.Contains(added, line => line.StartsWith("Hotkey added: Ctrl+F1", StringComparison.Ordinal));
        Assert.Contains("Ctrl+F1", WaitForFile(_file, text => text.Contains("Ctrl+F1", StringComparison.Ordinal)), StringComparison.Ordinal);

        Assert.Contains(Rc("hotkey list"), line => line.Contains("Ctrl+F1", StringComparison.Ordinal));

        Assert.Equal(["Removed 1 hotkey(s) on Ctrl+F1."], Rc("hotkey remove ctrl+f1"));
        Assert.DoesNotContain("Ctrl+F1", WaitForFile(_file, text => !text.Contains("Ctrl+F1", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void AChatCommandRunsOnTheNextDrainNotAtOnce()
    {
        var ran = new List<string>();
        AcMainThreadQueue.ChatCommand = ran.Add;
        try
        {
            Assert.True(AcMainThreadQueue.EnqueueChatCommand("/ra help"));
            Assert.Empty(ran);
            UiDataHub.Drain(_log.Add);
            Assert.Equal(["/ra help"], ran);
        }
        finally { AcMainThreadQueue.ChatCommand = null; }
    }

    [Fact]
    public void RcHotkeysOpensTheSettingsHotkeysTabAndItDraws()
    {
        using var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthAi.PluginExports)]);
        using var exports = new RynthChatHookup(host, _view, _log.Add);
        using var ai = new RynthAiHookup(host, _view, _log.Add);
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();

        Assert.Empty(Rc("hotkeys"));
        for (int i = 0; i < 3; i++)
        {
            AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
            host.Tick();
            int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
            Assert.True(vertices > 300, $"frame {i}: only {vertices} vertices");
        }

        Assert.True(ImGuiPanelHost.IsOpen(SettingsFace.Title));
        Assert.DoesNotContain(_log, line => line.Contains("Exception", StringComparison.Ordinal) || line.Contains("threw", StringComparison.Ordinal));
    }
}
