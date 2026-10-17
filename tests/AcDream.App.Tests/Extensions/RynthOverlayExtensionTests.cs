using System.Numerics;
using AcDream.ClientExtensions;
using AcDream.Rynth.Host;
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.ScriptWindows;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RynthOverlayExtensionTests : IDisposable
{
    private readonly Dictionary<string, Action<string>> _commands = new();
    private readonly List<string> _log = new();
    private readonly IntPtr _imgui;

    public RynthOverlayExtensionTests()
    {
        ScriptWindowRegistry.Reset();
        ImGuiPanelHost.Reset();
        _imgui = ImGui.CreateContext();
        ImGui.SetCurrentContext(_imgui);
        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(1280, 800);
        io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
        io.Fonts.SetTexID(1);
    }

    public void Dispose()
    {
        ScriptWindowRegistry.Reset();
        ImGuiPanelHost.Reset();
        ImGui.DestroyContext(_imgui);
    }

    private ClientExtensionContext Context(AcDream.Runtime.GameRuntime runtime, IClientOverlay? overlay) => new()
    {
        Runtime = runtime,
        Log = _log.Add,
        RegisterChatCommand = (verb, handler) =>
        {
            _commands[verb] = handler;
            return new Registration(() => _commands.Remove(verb));
        },
        Overlay = overlay,
    };

    [Fact]
    public void WithAnOverlayOraclesWindowDrawsFromTheFrameCallback()
    {
        var overlay = new FakeOverlay();
        var extension = new RynthClientExtension();
        extension.Attach(Context(GameRuntimeTestFactory.Create(), overlay));

        _commands["ro"]("");
        extension.Tick(1 / 60.0);
        extension.Tick(1 / 60.0);
        Action draw = Assert.Single(overlay.Callbacks);
        Frame(draw);
        int vertices = Frame(draw);

        extension.Dispose();
        Assert.True(vertices > 100, $"only {vertices} vertices");
        Assert.True(overlay.CallbackRemoved);
        Assert.Empty(_commands);
    }

    [Fact]
    public void WithoutAnOverlaySlashRoSaysSoInChat()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var extension = new RynthClientExtension();
        extension.Attach(Context(runtime, overlay: null));

        _commands["ro"]("");
        extension.Tick(1 / 60.0);
        extension.Dispose();

        Assert.Contains(runtime.CommunicationOwner.Chat.Snapshot(), entry =>
            entry.Text.Contains("can't show plugin windows", StringComparison.Ordinal));
    }

    private static int Frame(Action draw)
    {
        ImGui.GetIO().DeltaTime = 1f / 60f;
        ImGui.NewFrame();
        draw();
        ImGui.Render();
        return ImGui.GetDrawData().TotalVtxCount;
    }

    private sealed class FakeOverlay : IClientOverlay
    {
        public List<Action> Callbacks { get; } = new();
        public bool CallbackRemoved { get; private set; }
        public bool WantsMouse => false;
        public bool WantsKeyboard => false;

        public IDisposable AddFrameCallback(Action draw)
        {
            Callbacks.Add(draw);
            return new Registration(() => { Callbacks.Remove(draw); CallbackRemoved = true; });
        }
    }

    private sealed class Registration(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
