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
    private readonly string _fileDirectory;
    private readonly string? _oldFile = Environment.GetEnvironmentVariable(HotkeyStore.EnvOverride);

    public RynthHotkeysTests()
    {
        _fileDirectory = Directory.CreateTempSubdirectory("rynth-hotkeys-file-").FullName;
        _file = Path.Combine(_fileDirectory, "hotkeys.txt");
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
        AcDream.Rynth.EngineUi.Win32KeyStateStandIn.Control = false;
        AcDream.Rynth.EngineUi.Win32KeyStateStandIn.Shift = false;
        AcDream.Rynth.EngineUi.Win32KeyStateStandIn.Alt = false;
        AcDream.Rynth.EngineUi.Win32KeyStateStandIn.Focused = false;
        Environment.SetEnvironmentVariable(HotkeyStore.EnvOverride, _oldFile);
        try { Directory.Delete(_fileDirectory, recursive: true); } catch (IOException) { }
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

    private static AcDream.ClientExtensions.OverlayKeyEvent Press(int vk, bool down = true, bool repeat = false,
        AcDream.ClientExtensions.OverlayModifiers mods = AcDream.ClientExtensions.OverlayModifiers.Control) =>
        new(vk, mods, down, repeat, Extended: false);

    private const int VkF1 = 0x70, VkF2 = 0x71, VkF5 = 0x74, VkEscape = 0x1B;

    private (RynthHotkeyKeyboard Keys, List<string> Ran) Bound(bool typing = false, bool overlayWants = false)
    {
        var ran = new List<string>();
        AcMainThreadQueue.ChatCommand = ran.Add;
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        var keys = new RynthHotkeyKeyboard(() => overlayWants);
        AcKeyboardFocus.IsTyping = () => typing;
        HotkeyService.Add(new HotkeyBinding(new KeyCombo(VkF1, HotkeyMods.Ctrl), HotkeyActions.Command, "/ra help"));
        return (keys, ran);
    }

    [Fact]
    public void ABoundKeyFiresOnceAndIsTakenWholly()
    {
        (RynthHotkeyKeyboard keys, List<string> ran) = Bound();
        try
        {
            Assert.True(keys.KeyPress(Press(VkF1)));
            Assert.True(keys.KeyPress(Press(VkF1, repeat: true)));
            keys.Character('\u0001');
            Assert.True(keys.KeyPress(Press(VkF1, down: false)));
            UiDataHub.Drain(_log.Add);

            Assert.Equal(["/ra help"], ran);
            Assert.False(keys.KeyPress(Press(VkF2)));
        }
        finally { AcMainThreadQueue.ChatCommand = null; AcKeyboardFocus.IsTyping = null; }
    }

    [Theory]
    [InlineData(true, false, true)]    // typing in OpenAC
    [InlineData(false, true, true)]    // an ImGui field or Rynth chat has the keyboard
    [InlineData(false, false, false)]  // before login
    public void HeldBackWhileTypingOrOutOfWorld(bool typing, bool overlayWants, bool inWorld)
    {
        (RynthHotkeyKeyboard keys, List<string> ran) = Bound(typing, overlayWants);
        try
        {
            LoginLifecycleHooks.HasObservedLoginComplete = inWorld;

            Assert.False(keys.KeyPress(Press(VkF1)));
            UiDataHub.Drain(_log.Add);
            Assert.Empty(ran);
        }
        finally { AcMainThreadQueue.ChatCommand = null; AcKeyboardFocus.IsTyping = null; }
    }

    [Fact]
    public void AKeyAfterAFocusLossStillFires()
    {
        // OpenAC sends keys only while its window has them, so the key itself is focus:
        // a loss must not leave hotkeys blocked (their tracker resets on WM_KILLFOCUS).
        (RynthHotkeyKeyboard keys, List<string> ran) = Bound();
        try
        {
            keys.FocusLost();
            Assert.False(AcDream.Rynth.EngineUi.Win32KeyStateStandIn.Focused);

            Assert.True(keys.KeyPress(Press(VkF1)));
            UiDataHub.Drain(_log.Add);
            Assert.Equal(["/ra help"], ran);
        }
        finally { AcMainThreadQueue.ChatCommand = null; AcKeyboardFocus.IsTyping = null; }
    }

    [Fact]
    public void SettingsCaptureTakesTheNextComboAndEscCancels()
    {
        var keys = new RynthHotkeyKeyboard(() => false);
        HotkeyService.BeginCapture();
        Assert.True(keys.KeyPress(Press(VkF5, mods: AcDream.ClientExtensions.OverlayModifiers.Control | AcDream.ClientExtensions.OverlayModifiers.Shift)));
        Assert.True(HotkeyService.TryTakeCapture(out KeyCombo? combo));
        Assert.Equal("Ctrl+Shift+F5", combo?.ToString());

        HotkeyService.BeginCapture();
        Assert.True(keys.KeyPress(Press(VkEscape, mods: AcDream.ClientExtensions.OverlayModifiers.None)));
        Assert.False(HotkeyService.Capturing);
        Assert.True(HotkeyService.TryTakeCapture(out KeyCombo? cancelled) && cancelled is null);
    }

    [Fact]
    public void ReloadWritesItsLineOnTheNextDrainNotAtOnce()
    {
        // RynthCore signals reload from a thread-pool thread: the chat line waits for the pump.
        var lines = new List<string>();
        AcMainThreadQueue.WriteToChat = (text, _) => lines.Add(text);
        try
        {
            RynthCore.Engine.EngineLifecycle.SignalReload();
            Assert.Empty(lines);
            UiDataHub.Drain(_log.Add);
            Assert.Equal(["[RynthCore] Reload isn't in OpenAC."], lines);
        }
        finally { AcMainThreadQueue.WriteToChat = null; }
    }

    [Fact]
    public void APanelBindingTogglesThePanel()
    {
        using var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthAi.PluginExports)]);
        using var exports = new RynthChatHookup(host, _view, _log.Add);
        using var ai = new RynthAiHookup(host, _view, _log.Add);
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        AcKeyboardFocus.IsTyping = () => false;
        try
        {
            var keys = new RynthHotkeyKeyboard(() => false);
            HotkeyService.Add(new HotkeyBinding(new KeyCombo(VkF2, HotkeyMods.Ctrl), HotkeyActions.PanelPrefix + DungeonMapFace.Title));

            Assert.True(keys.KeyPress(Press(VkF2)));

            Assert.True(ImGuiPanelHost.IsOpen(DungeonMapFace.Title));
        }
        finally { AcKeyboardFocus.IsTyping = null; }
    }
}
