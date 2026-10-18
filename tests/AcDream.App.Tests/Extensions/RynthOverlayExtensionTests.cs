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
        ScriptIcons.Source = null;
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
        ScriptIcons.Source = null;
        ScriptWindowRegistry.Reset();
        ImGuiPanelHost.Reset();
        ImGui.DestroyContext(_imgui);
    }

    private ClientExtensionContext Context(AcDream.Runtime.GameRuntime runtime, IClientOverlay? overlay, string? stateDirectory = null) => new()
    {
        Runtime = runtime,
        Log = _log.Add,
        RegisterChatCommand = (verb, handler) =>
        {
            _commands[verb] = handler;
            return new Registration(() => _commands.Remove(verb));
        },
        Overlay = overlay,
        StateDirectory = stateDirectory,
    };

    [Fact]
    public void WithAnOverlayTheLayoutIsHookedUpUntilDispose()
    {
        var extension = new RynthClientExtension();
        extension.Attach(Context(GameRuntimeTestFactory.Create(), new FakeOverlay()));
        Assert.NotNull(ImGuiPanelHost.Layout);

        extension.Dispose();
        Assert.Null(ImGuiPanelHost.Layout);
    }

    [Fact]
    public void WithAnOverlayScriptIconsAskTheOverlayAndPhosphorIsMerged()
    {
        var overlay = new FakeOverlay();
        var extension = new RynthClientExtension();
        extension.Attach(Context(GameRuntimeTestFactory.Create(), overlay));

        Assert.True(ScriptIcons.TryGet(ScriptIconKind.Icon, 0x06001234u, out IntPtr icon));
        Assert.True(ScriptIcons.TryGet(ScriptIconKind.Spell, 7, out IntPtr spell));
        Assert.True(ScriptIcons.TryGet(ScriptIconKind.Object, 0x80000001u, out IntPtr obj));
        Assert.Equal(((IntPtr)1, (IntPtr)2, (IntPtr)3), (icon, spell, obj));
        Assert.Equal(new[] { "icon:06001234", "spell:7", "object:80000001" }, overlay.FakeIcons.Asked);
        (int bytes, string glyphs, float scale) = Assert.Single(overlay.Fonts);
        Assert.True(bytes > 10_000);
        Assert.Equal(PhosphorIcons.Baked, glyphs);
        Assert.Equal(1.2f, scale);

        extension.Dispose();
        Assert.Null(ScriptIcons.Source);
    }

    [Fact]
    public void WithoutAnOverlayThereIsNoLayout()
    {
        using var extension = new RynthClientExtension();
        extension.Attach(Context(GameRuntimeTestFactory.Create(), overlay: null));

        Assert.Null(ImGuiPanelHost.Layout);
    }

    [Fact]
    public void AnOpenedWindowIsSavedInTheStateFolder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rynth-ext-" + Guid.NewGuid().ToString("N"));
        try
        {
            var overlay = new FakeOverlay();
            var extension = new RynthClientExtension();
            extension.Attach(Context(GameRuntimeTestFactory.Create(), overlay, dir));
            _commands["ro"]("");
            extension.Tick(1 / 60.0);
            extension.Tick(1 / 60.0);
            Frame(Assert.Single(overlay.Callbacks));
            // Read before Dispose: shutting the plugin down may record its window as closed.
            string json = File.ReadAllText(Path.Combine(dir, "rynth-layout.json"));
            extension.Dispose();

            Assert.Contains("script:RynthOracle/Oracle", json, StringComparison.Ordinal);
            Assert.Contains("\"open\": true", json, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

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
        public FakeIcons FakeIcons { get; } = new();
        public AcDream.ClientExtensions.IClientOverlayIcons Icons => FakeIcons;
        public List<(int Bytes, string Glyphs, float Scale)> Fonts { get; } = new();

        public void AddMergedFont(byte[] ttf, string glyphs, float scale) => Fonts.Add((ttf.Length, glyphs, scale));

        public IDisposable AddFrameCallback(Action draw)
        {
            Callbacks.Add(draw);
            return new Registration(() => { Callbacks.Remove(draw); CallbackRemoved = true; });
        }
    }

    private sealed class FakeIcons : AcDream.ClientExtensions.IClientOverlayIcons
    {
        public List<string> Asked { get; } = new();

        public IntPtr Icon(uint iconId) { Asked.Add($"icon:{iconId:X8}"); return (IntPtr)1; }

        public IntPtr SpellIcon(uint spellId) { Asked.Add($"spell:{spellId}"); return (IntPtr)2; }

        public IntPtr ObjectIcon(uint objectId) { Asked.Add($"object:{objectId:X8}"); return (IntPtr)3; }
    }

    private sealed class Registration(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
