using System.Numerics;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed class ChatPanelHostTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly HeadlessImGui _imgui = new();

    public ChatPanelHostTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
    }

    public void Dispose()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        _imgui.Dispose();
    }

    private sealed class Panel(Action draw) : IImGuiPanel
    {
        public int Shown;
        public void OnShown() => Shown++;
        public void Draw() => draw();
    }

    private static readonly PanelSpec Spec = new(new Vector2(460, 300), new Vector2(260, 140), Background: 0, EdgeToEdge: true, GripInBody: true);

    [Fact]
    public void RegisterThenOpenShowsThePanel()
    {
        var panel = new Panel(() => { });
        ImGuiPanelHost.Register("Chat", Spec, () => panel);
        ImGuiPanelHost.Open("Chat");
        Assert.True(ImGuiPanelHost.IsOpen("Chat"));
        Assert.Equal(1, panel.Shown);
    }

    [Fact]
    public void HasFaceKnowsRegisteredTitles()
    {
        ImGuiPanelHost.Register("Chat", Spec, () => new Panel(() => { }));
        Assert.True(ImGuiPanelHost.HasFace("Chat"));
        Assert.False(ImGuiPanelHost.HasFace("RynthNav"));
    }

    [Fact]
    public void ToggleFlipsOpenState()
    {
        ImGuiPanelHost.Register("ChatFilters", Spec, () => new Panel(() => { }));
        ImGuiPanelHost.Toggle("ChatFilters");
        Assert.True(ImGuiPanelHost.IsOpen("ChatFilters"));
        ImGuiPanelHost.Toggle("ChatFilters");
        Assert.False(ImGuiPanelHost.IsOpen("ChatFilters"));
    }

    [Fact]
    public void AMissingWindowsLibraryIsLoggedOnceAndTheWindowStays()
    {
        int draws = 0;
        ImGuiPanelHost.Register("Chat", Spec, () => new Panel(() => { draws++; throw new DllNotFoundException("user32.dll"); }));
        ImGuiPanelHost.Open("Chat");
        for (int i = 0; i < 5; i++)
            _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.True(ImGuiPanelHost.IsOpen("Chat"));
        Assert.Equal(5, draws);
        Assert.Single(_log, l => l.Contains("Chat", StringComparison.Ordinal) && l.Contains("DllNotFoundException", StringComparison.Ordinal));
    }

    // Existing behaviour: after 3 failures the body is no longer drawn (the window stays open showing a "stopped" note).
    [Fact]
    public void OtherFailuresStopTheBodyAfterThree()
    {
        int draws = 0;
        ImGuiPanelHost.Register("Chat", Spec, () => new Panel(() => { draws++; throw new InvalidOperationException("boom"); }));
        ImGuiPanelHost.Open("Chat");
        for (int i = 0; i < 6; i++)
            _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.Equal(3, draws);
        Assert.Contains(_log, l => l.Contains("stopped after repeated failures", StringComparison.Ordinal));
    }
}
