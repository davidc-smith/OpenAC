using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.Plugins;
using RynthCore.Engine.UI.ScriptWindows;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class ImGuiPanelHostTests : IDisposable
{
    private readonly HeadlessImGui _imgui = new();

    public ImGuiPanelHostTests() => ImGuiPanelHost.Reset();

    public void Dispose()
    {
        ImGuiPanelHost.Reset();
        _imgui.Dispose();
    }

    [Fact]
    public void AnOpenPanelDrawsInsideAWindow()
    {
        var panel = new RecordingPanel();
        ImGuiPanelHost.RegisterDynamic("script:test", "Test", new PanelSpec(new Vector2(200, 100), new Vector2(50, 50)), () => panel);
        ImGuiPanelHost.Open("script:test");

        _imgui.Frame(ImGuiPanelHost.DrawAll);
        int vertices = _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.True(panel.Shown);
        Assert.True(panel.Draws >= 1);
        Assert.True(vertices > 0);
        Assert.Equal(new Vector2(200, 100), panel.WindowSize);
    }

    [Fact]
    public void APanelThatThrowsLeavesImGuiBalanced()
    {
        var panel = new RecordingPanel { Throw = true };
        var log = new List<string>();
        ImGuiPanelHost.Log = log.Add;
        ImGuiPanelHost.RegisterDynamic("script:bad", "Bad", new PanelSpec(new Vector2(200, 100), new Vector2(50, 50)), () => panel);
        ImGuiPanelHost.Open("script:bad");

        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        void After() { ImGui.Begin("after"); ImGui.Text("still fine"); ImGui.End(); }
        _imgui.Frame(After);
        int vertices = _imgui.Frame(After);

        Assert.Contains(log, line => line.Contains("Bad", StringComparison.Ordinal));
        Assert.True(vertices > 0);
    }

    [Fact]
    public void APanelThatAlwaysThrowsIsStoppedAndLoggedBriefly()
    {
        var panel = new RecordingPanel { Throw = true };
        var log = new List<string>();
        ImGuiPanelHost.Log = log.Add;
        ImGuiPanelHost.RegisterDynamic("script:bad", "Bad", new PanelSpec(new Vector2(200, 100), new Vector2(50, 50)), () => panel);
        ImGuiPanelHost.Open("script:bad");

        for (int i = 0; i < 10; i++)
            _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.True(log.Count <= 4, string.Join("\n", log));
        Assert.True(panel.Draws <= 3);
    }

    [Fact]
    public void CloseCallsOnHidden()
    {
        var panel = new RecordingPanel();
        ImGuiPanelHost.RegisterDynamic("script:x", "X", new PanelSpec(new Vector2(200, 100), new Vector2(50, 50)), () => panel);
        ImGuiPanelHost.Open("script:x");
        ImGuiPanelHost.Close("script:x");

        Assert.True(panel.Hidden);
        Assert.False(ImGuiPanelHost.IsOpen("script:x"));
    }

    [Fact]
    public void GetInfoReportsImGuiOnlyWhenEnabled()
    {
        var info = new UiInfoNative { Size = (uint)sizeof(UiInfoNative) };
        EngineSettings.EnableImGuiBackend = false;
        Assert.Equal(0, ScriptWindowRegistry.GetInfo(&info));

        EngineSettings.EnableImGuiBackend = true;
        info = new UiInfoNative { Size = (uint)sizeof(UiInfoNative) };
        int written = ScriptWindowRegistry.GetInfo(&info);
        EngineSettings.EnableImGuiBackend = false;

        Assert.Equal(480, sizeof(UiInfoNative));
        Assert.Equal(480, written);
        Assert.Equal(1u, info.Flags & 1u);
        Assert.Equal((ushort)1, info.MaxFormatVersion);
    }

    [Fact]
    public void AnOpenCenteredPanelOpensInTheMiddleOfTheDisplay()
    {
        var panel = new PositionPanel();
        ImGuiPanelHost.RegisterDynamic("script:map", "Map",
            new PanelSpec(new Vector2(480, 520), new Vector2(240, 200), OpenCentered: true), () => panel);
        ImGuiPanelHost.Open("script:map");

        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.Equal(new Vector2(400, 140), panel.WindowPos);
    }

    [Fact]
    public void AnOpenCenteredPanelIsNotPlacedWhileTheDisplaySizeIsUnknown()
    {
        var panel = new PositionPanel();
        ImGuiPanelHost.RegisterDynamic("script:map", "Map",
            new PanelSpec(new Vector2(480, 520), new Vector2(240, 200), OpenCentered: true), () => panel);
        ImGuiPanelHost.Open("script:map");
        ImGui.GetIO().DisplaySize = Vector2.Zero;
        try
        {
            _imgui.Frame(ImGuiPanelHost.DrawAll);
            _imgui.Frame(ImGuiPanelHost.DrawAll);
        }
        finally { ImGui.GetIO().DisplaySize = new Vector2(1280, 800); }

        Assert.NotEqual(new Vector2(400, 140), panel.WindowPos);
    }

    [Fact]
    public void ASavedSpotWinsOverOpenCentered()
    {
        var layout = new FakePanelLayout();
        layout.Saved["script:map"] = new PanelPlacement(new PanelRect(new Vector2(10, 20), new Vector2(480, 520)), false);
        ImGuiPanelHost.Layout = layout;
        try
        {
            var panel = new PositionPanel();
            ImGuiPanelHost.RegisterDynamic("script:map", "Map",
                new PanelSpec(new Vector2(480, 520), new Vector2(240, 200), OpenCentered: true), () => panel);
            ImGuiPanelHost.Open("script:map");

            _imgui.Frame(ImGuiPanelHost.DrawAll);
            _imgui.Frame(ImGuiPanelHost.DrawAll);

            Assert.Equal(new Vector2(10, 20), panel.WindowPos);
        }
        finally { ImGuiPanelHost.Layout = null; }
    }

    [Fact]
    public void AHiddenWhileDockedPanelIsNotDrawnButStaysOpen()
    {
        var panel = new PositionPanel { Hide = true };
        ImGuiPanelHost.RegisterDynamic("script:map", "Map", new PanelSpec(new Vector2(200, 100), new Vector2(50, 50)), () => panel);
        ImGuiPanelHost.Open("script:map");

        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.Equal(0, panel.Draws);
        Assert.True(ImGuiPanelHost.IsOpen("script:map"));

        panel.Hide = false;
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.True(panel.Draws >= 1);
    }

    [Fact]
    public void AThrowingHiddenWhileDockedGetterCountsAsShown()
    {
        var panel = new PositionPanel { HideThrows = true };
        ImGuiPanelHost.RegisterDynamic("script:map", "Map", new PanelSpec(new Vector2(200, 100), new Vector2(50, 50)), () => panel);
        ImGuiPanelHost.Open("script:map");

        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.True(panel.Draws >= 1);
    }

    [Fact]
    public void AStoppedPanelIsNeverHidden()
    {
        var panel = new PositionPanel { Throw = true };
        var log = new List<string>();
        ImGuiPanelHost.Log = log.Add;
        ImGuiPanelHost.RegisterDynamic("script:map", "Map", new PanelSpec(new Vector2(200, 100), new Vector2(50, 50)), () => panel);
        ImGuiPanelHost.Open("script:map");
        for (int i = 0; i < 5; i++)
            _imgui.Frame(ImGuiPanelHost.DrawAll);   // stopped after MaxFailures

        panel.Hide = true;
        int vertices = _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.True(vertices > 0, "the stopped panel's error line should still draw");
    }

    private sealed class PositionPanel : IImGuiPanel
    {
        public bool Hide { get; set; }
        public bool HideThrows { get; init; }
        public bool Throw { get; init; }
        public int Draws { get; private set; }
        public Vector2 WindowPos { get; private set; }

        public bool HiddenWhileDocked => HideThrows ? throw new InvalidOperationException("getter failed") : Hide;

        public void Draw()
        {
            Draws++;
            WindowPos = ImGui.GetWindowPos();
            ImGui.Text("panel body");
            if (Throw)
                throw new InvalidOperationException("body failed");
        }
    }

    private sealed class RecordingPanel : IImGuiPanel
    {
        public bool Throw { get; init; }
        public bool Shown { get; private set; }
        public bool Hidden { get; private set; }
        public int Draws { get; private set; }
        public Vector2 WindowSize { get; private set; }

        public void OnShown() => Shown = true;

        public void OnHidden() => Hidden = true;

        public void Draw()
        {
            Draws++;
            WindowSize = ImGui.GetWindowSize();
            ImGui.Text("panel body");
            if (Throw)
                throw new InvalidOperationException("body failed");
        }
    }
}
