using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed class PanelHostLayoutTests : IDisposable
{
    private const string Key = "script:test";
    private readonly HeadlessImGui _imgui = new();
    private readonly FakePanelLayout _layout = new();

    public PanelHostLayoutTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Layout = _layout;
        RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = true;
    }

    public void Dispose()
    {
        ImGuiPanelHost.Reset();
        RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = false;
        _imgui.Dispose();
    }

    private static PanelSpec Spec => new(new Vector2(200, 100), new Vector2(50, 50), DefaultPos: new Vector2(10, 10));

    private MovingPanel RegisterAndOpen()
    {
        var panel = new MovingPanel();
        ImGuiPanelHost.RegisterDynamic(Key, "Test", Spec, () => panel);
        ImGuiPanelHost.Open(Key);
        return panel;
    }

    [Fact]
    public void ASavedRectanglePlacesTheWindowOnItsFirstFrame()
    {
        _layout.Saved[Key] = new PanelPlacement(new PanelRect(new Vector2(40, 50), new Vector2(300, 220)), false);
        MovingPanel panel = RegisterAndOpen();

        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.Equal(new Vector2(40, 50), panel.Pos);
        Assert.Equal(new Vector2(300, 220), panel.Size);
        Assert.Empty(_layout.RectCalls);
    }

    [Fact]
    public void ASavedPositionOffTheDisplayIsClampedOntoIt()
    {
        _layout.Saved[Key] = new PanelPlacement(new PanelRect(new Vector2(5000, 5000), new Vector2(300, 220)), false);
        MovingPanel panel = RegisterAndOpen();

        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.Equal(new Vector2(980, 580), panel.Pos);
    }

    private static readonly Vector2 Game = new(2560, 1400);
    private static readonly Vector2 Login = new(1280, 800);

    private void DisplayIs(Vector2 size)
    {
        ImGui.GetIO().DisplaySize = size;
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
    }

    // The client logs in at the character list's size and is made bigger after: the
    // spot saved for the bigger display comes back once the display is that size again.
    [Fact]
    public void TheSpotSavedForADisplaySizeComesBackWhenTheDisplayIsThatSizeAgain()
    {
        _layout.SavedFor[(Key, Game)] = new PanelRect(new Vector2(1500, 900), new Vector2(300, 220));
        _layout.Saved[Key] = new PanelPlacement(new PanelRect(new Vector2(1500, 900), new Vector2(300, 220)), false);
        MovingPanel panel = RegisterAndOpen();
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.Equal(new Vector2(980, 580), panel.Pos);   // nothing for this size: the last spot, held on screen

        DisplayIs(new Vector2(1700, 1000));
        Assert.Equal(new Vector2(1400, 780), panel.Pos);

        DisplayIs(Game);
        Assert.Equal(new Vector2(1500, 900), panel.Pos);
        Assert.Equal(new Vector2(300, 220), panel.Size);
        Assert.Empty(_layout.RectCalls);
    }

    // Logging out shrinks the display while the window stays open: being pulled onto
    // the smaller display is not the player's doing and is not saved.
    [Fact]
    public void AWindowPulledOntoASmallerDisplayIsNotSavedThere()
    {
        MovingPanel panel = RegisterAndOpen();
        DisplayIs(Game);
        panel.MoveTo = new Vector2(2200, 1200);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.Single(_layout.RectCalls);

        DisplayIs(Login);
        DisplayIs(new Vector2(1400, 900));
        Assert.Single(_layout.RectCalls);

        DisplayIs(Game);
        Assert.Equal(new Vector2(2200, 1200), panel.Pos);
    }

    [Fact]
    public void EachDisplaySizeKeepsItsOwnSpot()
    {
        MovingPanel panel = RegisterAndOpen();
        DisplayIs(Game);
        panel.MoveTo = new Vector2(2000, 1100);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        DisplayIs(Login);
        panel.MoveTo = new Vector2(100, 120);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        DisplayIs(Game);
        Assert.Equal(new Vector2(2000, 1100), panel.Pos);
        DisplayIs(Login);
        Assert.Equal(new Vector2(100, 120), panel.Pos);
        Assert.Equal(new[] { (Key, Game), (Key, Login) }, _layout.RectCalls.Select(c => (c.Key, c.Display)).ToArray());
    }

    [Fact]
    public void APlayerMoveIsSavedOnceTheMouseIsUp()
    {
        MovingPanel panel = RegisterAndOpen();
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        ImGui.GetIO().AddMouseButtonEvent(0, true);
        panel.MoveTo = new Vector2(300, 200);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.Empty(_layout.RectCalls);

        ImGui.GetIO().AddMouseButtonEvent(0, false);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        (string key, PanelRect rect, _) = Assert.Single(_layout.RectCalls);
        Assert.Equal(Key, key);
        Assert.Equal(new PanelRect(new Vector2(300, 200), new Vector2(200, 100)), rect);
    }

    [Fact]
    public void APluginRequestIsAppliedButNotSaved()
    {
        MovingPanel panel = RegisterAndOpen();
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        ImGuiPanelHost.RequestPos(Key, new Vector2(400, 300));
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.Equal(new Vector2(400, 300), panel.Pos);
        Assert.Empty(_layout.RectCalls);
    }

    [Fact]
    public void OpenAndCloseAreRecordedButUnregisterIsNot()
    {
        RegisterAndOpen();
        ImGuiPanelHost.Close(Key);
        ImGuiPanelHost.Open(Key);
        ImGuiPanelHost.Unregister(Key);

        Assert.Equal([(Key, true), (Key, false), (Key, true)], _layout.OpenCalls);
    }

    [Fact]
    public void AWindowSavedAsOpenIsReopenedOnceOnTheNextDraw()
    {
        var reopened = new List<string>();
        ImGuiPanelHost.RestoreOpener = reopened.Add;
        _layout.Saved[Key] = new PanelPlacement(null, true);
        ImGuiPanelHost.RegisterDynamic(Key, "Test", Spec, () => new MovingPanel());

        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.Equal([Key], reopened);
    }

    [Fact]
    public void ASavedOpenWindowIsNotReopenedUntilThePlayerIsInTheWorld()
    {
        var reopened = new List<string>();
        ImGuiPanelHost.RestoreOpener = reopened.Add;
        RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = false;
        _layout.Saved[Key] = new PanelPlacement(null, true);
        ImGuiPanelHost.RegisterDynamic(Key, "Test", Spec, () => new MovingPanel());

        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.Empty(reopened);

        RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = true;
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.Equal([Key], reopened);
    }

    [Fact]
    public void APluginRequestBeatsASavedPlacementInTheSameFrame()
    {
        _layout.Saved[Key] = new PanelPlacement(new PanelRect(new Vector2(40, 50), new Vector2(300, 220)), false);
        var panel = new MovingPanel();
        ImGuiPanelHost.RegisterDynamic(Key, "Test", Spec, () => panel);
        ImGuiPanelHost.RequestPos(Key, new Vector2(400, 300));
        ImGuiPanelHost.Open(Key);

        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.Equal(new Vector2(400, 300), panel.Pos);
        Assert.Empty(_layout.RectCalls);
    }

    [Fact]
    public void AWindowAlreadyOpenIsNotReopened()
    {
        var reopened = new List<string>();
        ImGuiPanelHost.RestoreOpener = reopened.Add;
        _layout.Saved[Key] = new PanelPlacement(null, true);
        RegisterAndOpen();

        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.Empty(reopened);
    }

    private sealed class MovingPanel : IImGuiPanel
    {
        public Vector2? MoveTo { get; set; }
        public Vector2 Pos { get; private set; }
        public Vector2 Size { get; private set; }

        public void Draw()
        {
            if (MoveTo is { } to)
            {
                ImGui.SetWindowPos(to);
                MoveTo = null;
            }

            Pos = ImGui.GetWindowPos();
            Size = ImGui.GetWindowSize();
            ImGui.TextUnformatted("body");
        }
    }
}
