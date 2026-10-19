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

    public void Dispose()
    {
        _renderer.ThrowOnDispose = false;
        _overlay.Dispose();
    }

    private sealed class RecordingSink : AcDream.ClientExtensions.IOverlayKeyboardSink
    {
        public bool Capturing { get; set; }
        public bool ThrowOnKey { get; set; }
        public List<(AcDream.ClientExtensions.OverlayKey, AcDream.ClientExtensions.OverlayModifiers)> Downs { get; } = new();
        public List<char> Chars { get; } = new();
        public void KeyDown(AcDream.ClientExtensions.OverlayKey key, AcDream.ClientExtensions.OverlayModifiers modifiers)
        {
            if (ThrowOnKey) throw new InvalidOperationException("sink failed");
            Downs.Add((key, modifiers));
        }
        public void KeyUp(AcDream.ClientExtensions.OverlayKey key) { }
        public void Character(char value) => Chars.Add(value);
    }

    private sealed class FakeClipboard : IClipboardSource
    {
        public string ClipboardText { get; set; } = "";
    }

    [Fact]
    public void ACapturingSinkWantsTheKeyboardAtOnce()
    {
        var sink = new RecordingSink();
        using IDisposable registration = _overlay.AddKeyboardSink(sink);
        Assert.False(_overlay.WantsKeyboard);
        sink.Capturing = true;
        Assert.True(_overlay.WantsKeyboard);
        sink.Capturing = false;
        Assert.False(_overlay.WantsKeyboard);
    }

    [Fact]
    public void ACapturingSinkLetsTheKeyboardGoOnceTheOverlayFails()
    {
        Start();
        var sink = new RecordingSink { Capturing = true };
        using IDisposable registration = _overlay.AddKeyboardSink(sink);
        Assert.True(_overlay.WantsKeyboard);
        _renderer.ThrowOnRender = true;
        RunFrame();
        Assert.False(_overlay.IsAvailable);
        Assert.False(_overlay.WantsKeyboard);
    }

    [Fact]
    public void KeysAndCharactersReachEverySink()
    {
        var a = new RecordingSink();
        var b = new RecordingSink { Capturing = true };
        using IDisposable ra = _overlay.AddKeyboardSink(a);
        using IDisposable rb = _overlay.AddKeyboardSink(b);

        _overlay.SinkKeyDown(Key.Enter, ctrl: false, shift: false);
        _overlay.SinkKeyDown(Key.V, ctrl: true, shift: false);
        _overlay.SinkKeyDown(Key.C, ctrl: false, shift: true);
        _overlay.SinkCharacter('h');

        foreach (RecordingSink sink in new[] { a, b })
        {
            Assert.Equal((AcDream.ClientExtensions.OverlayKey.Enter, AcDream.ClientExtensions.OverlayModifiers.None), sink.Downs[0]);
            Assert.Equal((AcDream.ClientExtensions.OverlayKey.V, AcDream.ClientExtensions.OverlayModifiers.Control), sink.Downs[1]);
            Assert.Equal((AcDream.ClientExtensions.OverlayKey.C, AcDream.ClientExtensions.OverlayModifiers.Shift), sink.Downs[2]);
            Assert.Equal(['h'], sink.Chars);
        }
    }

    [Fact]
    public void AThrowingSinkIsDroppedAndOthersCarryOn()
    {
        var bad = new RecordingSink { ThrowOnKey = true };
        var good = new RecordingSink();
        _overlay.AddKeyboardSink(bad);
        using IDisposable r = _overlay.AddKeyboardSink(good);
        _overlay.SinkKeyDown(Key.Enter, false, false);
        _overlay.SinkKeyDown(Key.Enter, false, false);
        Assert.Equal(2, good.Downs.Count);
        Assert.Single(_log, l => l.Contains("sink failed", StringComparison.Ordinal));
    }

    [Fact]
    public void DisposingTheRegistrationRemovesTheSink()
    {
        var sink = new RecordingSink { Capturing = true };
        _overlay.AddKeyboardSink(sink).Dispose();
        Assert.False(_overlay.WantsKeyboard);
        _overlay.SinkKeyDown(Key.Enter, false, false);
        Assert.Empty(sink.Downs);
    }

    [Fact]
    public void ClipboardWithoutAKeyboardIsEmpty()
    {
        _overlay.ClipboardText = "ignored";
        Assert.Equal("", _overlay.ClipboardText);
    }

    [Fact]
    public void ClipboardRoundTripsThroughTheKeyboard()
    {
        var keyboard = new FakeClipboard();
        _overlay.ClipboardSource = keyboard;
        _overlay.ClipboardText = "hello";
        Assert.Equal("hello", keyboard.ClipboardText);
        Assert.Equal("hello", _overlay.ClipboardText);
    }

    // RynthCore's filter test box (and every other plain ImGui text field) pastes
    // through ImGui's own clipboard calls, not through their chat input's code.
    [Fact]
    public void ImGuiTextFieldsUseTheSystemClipboard()
    {
        Start();
        Assert.Equal("", ImGui.GetClipboardText());

        var keyboard = new FakeClipboard { ClipboardText = "You say, \"héllo ✓\"" };
        _overlay.ClipboardSource = keyboard;
        Assert.Equal("You say, \"héllo ✓\"", ImGui.GetClipboardText());

        ImGui.SetClipboardText("copied ✓");
        Assert.Equal("copied ✓", keyboard.ClipboardText);
    }

    [Fact]
    public void CommandVPastesIntoAnImGuiTextFieldOnTheMac()
    {
        if (!OperatingSystem.IsMacOS())
            return;   // Command is Super; elsewhere the shortcut is Ctrl+V
        Start();
        _overlay.ClipboardSource = new FakeClipboard { ClipboardText = "pasted line" };
        byte[] buffer = new byte[64];
        _overlay.AddFrameCallback(() =>
        {
            ImGui.SetNextWindowPos(new Vector2(0, 0));
            ImGui.SetNextWindowSize(new Vector2(300, 100));
            ImGui.Begin("paste");
            ImGui.SetKeyboardFocusHere();
            ImGui.InputText("##test", buffer, (uint)buffer.Length);
            ImGui.End();
        });
        for (int i = 0; i < 3; i++)
            RunFrame();   // the field takes focus
        _overlay.Input.KeyChange(Key.SuperLeft, true);
        _overlay.Input.KeyChange(Key.V, true);
        RunFrame();
        _overlay.Input.KeyChange(Key.V, false);
        _overlay.Input.KeyChange(Key.SuperLeft, false);
        RunFrame();

        Assert.Equal("pasted line", System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0'));
    }

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

    [Fact]
    public void IconsAnswerOnceBoundToTheBackend()
    {
        Assert.Equal(IntPtr.Zero, _overlay.Icons.SpellIcon(1));

        _overlay.BindIcons(new SpellBackend(31));

        IntPtr icon = _overlay.Icons.SpellIcon(1);
        Assert.Equal((IntPtr)31, icon);
        Assert.True(((OverlayIcons)_overlay.Icons).IsIssued(icon));
    }

    [Fact]
    public void FontsAreFreedEvenWhenTheRendererFailsToDispose()
    {
        _renderer.ThrowOnDispose = true;
        Start();
        _overlay.AddMergedFont(
            RynthCore.Engine.ImGuiBackend.PhosphorFont.Load()!, RynthCore.Engine.ImGuiBackend.PhosphorIcons.Baked, 1.2f);
        Assert.Equal(1, _overlay.Fonts.Count);

        Assert.Throws<InvalidOperationException>(_overlay.Dispose);

        Assert.Equal(0, _overlay.Fonts.Count);
    }

    [Fact]
    public void AnOverlayWithoutIconsOrFontsUsesTheInterfaceDefaults()
    {
        AcDream.ClientExtensions.IClientOverlay bare = new BareOverlay();

        Assert.NotNull(bare.Icons);
        Assert.Equal(IntPtr.Zero, bare.Icons.Icon(0x06001234u));
        Assert.Equal(IntPtr.Zero, bare.Icons.SpellIcon(1));
        Assert.Equal(IntPtr.Zero, bare.Icons.ObjectIcon(0x80000001u));
        bare.AddMergedFont(new byte[] { 1 }, "x", 1f);
    }

    private sealed class BareOverlay : AcDream.ClientExtensions.IClientOverlay
    {
        public bool WantsMouse => false;
        public bool WantsKeyboard => false;

        public IDisposable AddFrameCallback(Action draw) => throw new NotSupportedException();
    }

    private sealed class SpellBackend(uint texture) : AcDream.App.Plugins.IPluginImageBackend
    {
        public bool TryGetClientArt(uint surfaceId, out uint texture2, out int width, out int height) => None(out texture2, out width, out height);

        public bool TryGetSpellIcon(uint spellId, out uint result, out int width, out int height)
        {
            result = texture;
            width = height = 32;
            return true;
        }

        public bool TryGetObjectIcon(uint objectId, out uint texture2, out int width, out int height) => None(out texture2, out width, out height);

        public uint UploadOwned(byte[] rgba, int width, int height, string debugName) => throw new NotSupportedException();

        public bool ReleaseOwned(uint texture2) => throw new NotSupportedException();

        public bool IsLinearFiltered(uint texture2) => true;

        private static bool None(out uint texture2, out int width, out int height)
        {
            texture2 = 0;
            width = height = 0;
            return false;
        }
    }

    [Fact]
    public void AMergedFontReachesTheFontList()
    {
        int before = _overlay.Fonts.Version;
        ((AcDream.ClientExtensions.IClientOverlay)_overlay).AddMergedFont(
            RynthCore.Engine.ImGuiBackend.PhosphorFont.Load()!, RynthCore.Engine.ImGuiBackend.PhosphorIcons.Baked, 1.2f);
        Assert.Equal(before + 1, _overlay.Fonts.Version);
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
        public bool ThrowOnDispose { get; set; }
        public bool ThrowOnRender { get; set; }

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
            if (ThrowOnRender)
                throw new InvalidOperationException("render failed");
            Rendered++;
            _frameCountAfterRender = ImGui.GetFrameCount();
            RenderedAfterOneNewFrame.Add(_frameCountAfterRender == _frameCountAtPrepare + 1);
            LastVertexCount = drawData.TotalVtxCount;
        }

        public void Dispose()
        {
            if (ThrowOnDispose)
                throw new InvalidOperationException("renderer dispose failed");
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

    [Fact]
    public void OverlayUnknownKeyIsOther() =>
        Assert.Equal(AcDream.ClientExtensions.OverlayKey.Other, OverlayKeyMap.Map(Key.Unknown));
}
