using System.Numerics;
using AcDream.App.Extensions.Overlay;
using ImGuiNET;
using Silk.NET.Input;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class ImGuiOverlayTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly FakeRenderer _renderer = new();
    private readonly ImGuiOverlay _overlay;

    public ImGuiOverlayTests() => _overlay = new ImGuiOverlay(_log.Add);

    public void Dispose() => _overlay.Dispose();

    private void Start() =>
        _overlay.Initialize(_renderer, () => (new Vector2(800, 600), new Vector2(1600, 1200)));

    private void RunFrame()
    {
        _overlay.BeginFrame(1f / 60f);
        _overlay.Render(1 / 60.0, 800, 600);
    }

    [Fact]
    public void BeforeInitializeNothingRuns()
    {
        int calls = 0;
        _overlay.AddFrameCallback(() => calls++);
        RunFrame();
        _overlay.AbortFrame();
        Assert.Equal(0, calls);
        Assert.False(_overlay.WantsMouse);
    }

    [Fact]
    public void AFrameRunsCallbacksAndHandsTheRendererDrawData()
    {
        Start();
        int calls = 0;
        _overlay.AddFrameCallback(() => { calls++; ImGui.Begin("t"); ImGui.Text("hello"); ImGui.End(); });
        RunFrame();
        RunFrame();
        Assert.Equal(2, calls);
        Assert.Equal(2, _renderer.Prepared);
        Assert.True(_renderer.LastVertexCount > 0);
        Assert.Equal(new Vector2(2, 2), _renderer.LastScale);
    }

    [Fact]
    public void PrepareFrameRunsBeforeNewFrame()
    {
        Start();
        RunFrame();
        RunFrame();
        Assert.Equal(2, _renderer.Prepared);
        Assert.Equal(new[] { true, true }, _renderer.PreparedBeforeNewFrame);
        Assert.Equal(new[] { true, true }, _renderer.RenderedAfterOneNewFrame);
    }

    [Fact]
    public void AThrowingCallbackIsRemovedAndTheNextFrameWorks()
    {
        Start();
        int good = 0;
        _overlay.AddFrameCallback(() => throw new InvalidOperationException("boom"));
        _overlay.AddFrameCallback(() => good++);
        RunFrame();
        RunFrame();
        Assert.Equal(2, good);
        Assert.Contains(_log, line => line.Contains("frame callback removed", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnbalancedCallbackDoesNotCrashAndTheNextFrameRenders()
    {
        Start();
        ImGuiIOPtr io = ImGui.GetIO();
        Assert.True(io.ConfigErrorRecovery);
        Assert.False(io.ConfigErrorRecoveryEnableAssert);
        int calls = 0;
        _overlay.AddFrameCallback(() =>
        {
            if (calls++ == 0)
                ImGui.Begin("leaky");
        });
        RunFrame();
        RunFrame();
        RunFrame();
        Assert.Equal(3, calls);
        Assert.Equal(3, _renderer.Rendered);
    }

    [Fact]
    public void AbortLeavesImGuiBalancedAndIsIdempotent()
    {
        Start();
        int calls = 0;
        _overlay.AddFrameCallback(() => { calls++; ImGui.Begin("t"); ImGui.Text("hello"); ImGui.End(); });
        _overlay.AbortFrame();
        _overlay.BeginFrame(1f / 60f);
        _overlay.AbortFrame();
        _overlay.AbortFrame();
        Assert.Equal(0, calls);
        Assert.Equal(0, _renderer.Rendered);
        RunFrame();
        RunFrame();
        Assert.Equal(2, calls);
        Assert.True(_renderer.LastVertexCount > 0);
    }

    [Fact]
    public void MouseOverAWindowSetsWantsMouse()
    {
        Start();
        _overlay.AddFrameCallback(() =>
        {
            ImGui.SetNextWindowPos(new Vector2(100, 100));
            ImGui.SetNextWindowSize(new Vector2(200, 200));
            ImGui.Begin("hover");
            ImGui.End();
        });
        RunFrame();
        _overlay.Input.MouseMove(new Vector2(150, 150));
        RunFrame();
        RunFrame();
        Assert.True(_overlay.WantsMouse);
    }

    [Fact]
    public void InputQueuedBeforeInitializeNeverReachesImGui()
    {
        _overlay.Input.MouseMove(new Vector2(150, 150));
        Start();
        RunFrame();
        Vector2 pos = ImGui.GetIO().MousePos;
        Assert.NotEqual(new Vector2(150, 150), pos);
    }

    [Fact]
    public void InitializeTwiceIsRejectedWithoutASecondContext()
    {
        Start();
        IntPtr context = ImGui.GetCurrentContext();
        Assert.Throws<InvalidOperationException>(Start);
        Assert.Equal(context, ImGui.GetCurrentContext());
        RunFrame();
        Assert.Equal(1, _renderer.Rendered);
    }

    private sealed class FakeRenderer : IImGuiDrawRenderer
    {
        private bool _built;
        private int _frameCountAfterRender;
        private int _frameCountAtPrepare;
        public List<bool> PreparedBeforeNewFrame { get; } = new();
        public List<bool> RenderedAfterOneNewFrame { get; } = new();
        public int Prepared { get; private set; }
        public int Rendered { get; private set; }
        public int LastVertexCount { get; private set; }
        public Vector2 LastScale { get; private set; }

        public void PrepareFrame(ImGuiIOPtr io, Vector2 framebufferScale)
        {
            Prepared++;
            _frameCountAtPrepare = ImGui.GetFrameCount();
            PreparedBeforeNewFrame.Add(_frameCountAtPrepare == _frameCountAfterRender);
            LastScale = framebufferScale;
            if (!_built)
            {
                io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
                io.Fonts.SetTexID(1);
                _built = true;
            }
        }

        public void Render(ImDrawDataPtr drawData, Vector2 framebufferSize)
        {
            Rendered++;
            _frameCountAfterRender = ImGui.GetFrameCount();
            RenderedAfterOneNewFrame.Add(_frameCountAfterRender == _frameCountAtPrepare + 1);
            LastVertexCount = drawData.TotalVtxCount;
        }

        public void Dispose()
        {
        }
    }
}

public sealed class ImGuiKeyMapTests
{
    [Fact]
    public void MapsKnownKeys()
    {
        Assert.Equal(ImGuiKey.A, ImGuiKeyMap.Map(Key.A));
        Assert.Equal(ImGuiKey.Enter, ImGuiKeyMap.Map(Key.Enter));
        Assert.Equal(ImGuiKey.Backspace, ImGuiKeyMap.Map(Key.Backspace));
    }

    [Fact]
    public void UnknownKeyIsNone() => Assert.Equal(ImGuiKey.None, ImGuiKeyMap.Map(Key.Unknown));
}
