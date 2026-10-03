using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI;

/// <summary>A dock button with an SVG icon: it wins over every other icon, is tinted with
/// the dock's state colour, falls back when it cannot be drawn, and is given back with its window.</summary>
public sealed class PluginShelfSvgIconTests : IDisposable
{
    private const string Icon = """<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="8"/></svg>""";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"acdream-shelf-svg-{Guid.NewGuid():N}.svg");
    private readonly Backend _backend = new();
    private readonly PluginSvgIconCache _cache;

    public PluginShelfSvgIconTests()
    {
        File.WriteAllText(_path, Icon);
        _cache = new PluginSvgIconCache(_backend, _ => { });
    }

    public void Dispose()
    {
        _cache.Dispose();
        File.Delete(_path);
    }

    private sealed class Backend : IPluginFontBackend
    {
        private uint _next = 500;
        public bool Refuse { get; set; }
        public List<(uint Texture, int Size)> Uploaded { get; } = [];

        public uint UploadCoverage(byte[] coverage, int width, int height, string debugName)
        {
            if (Refuse) return 0;
            uint texture = _next++;
            Uploaded.Add((texture, width));
            return texture;
        }

        public bool ReleaseCoverage(uint texture) => true;
    }

    private sealed class NullFrames : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }

    private sealed record Rig(
        UiRoot Root, PluginSidePanel Shelf, RetailWindowHandle Handle, UiPanel Frame,
        PluginSidePanel.PluginShelfButton Button);

    private Rig Build(PluginUiThemeSettings? themes = null, bool visible = false, (uint, int, int)? fileIcon = null,
        uint surface = 0)
    {
        var root = new UiRoot { Width = 800f, Height = 600f };
        var shelf = new PluginSidePanel(
            root.WindowManager,
            _ => throw new InvalidOperationException("the DAT resolver must not run"),
            font: null,
            themes);
        root.AddChild(shelf);
        var frame = new UiPanel { Left = 300f, Width = 200f, Height = 100f, Visible = visible };
        root.AddChild(frame);
        RetailWindowHandle handle = root.WindowManager.Register("plugin:acdream.test:main", frame);
        shelf.Add(
            new PluginUiOwner("acdream.test", "Test Plugin"),
            new PluginPanelDescriptor("main", "Test Plugin") { IconText = "TP", IconSurfaceId = surface },
            handle,
            fileIcon,
            _cache.Acquire("acdream.test/icon.svg", _path));
        return new Rig(root, shelf, handle, frame,
            Assert.Single(shelf.Children.OfType<PluginSidePanel.PluginShelfButton>()));
    }

    private static (TextRenderer Renderer, UiRenderContext Context) Context(float pixelScale = 1f)
    {
        var renderer = new TextRenderer(new RecordingGpuDevice(), new NullFrames(), "unused");
        renderer.Begin(new Vector2(200f, 200f));
        var context = new UiRenderContext(renderer, new Vector2(200f, 200f));
        context.Begin(new Vector2(200f, 200f), null, pixelScale);
        return (renderer, context);
    }

    private static Vector4 CoverageColour(TextRenderer renderer, uint texture)
    {
        int index = renderer.DebugSpriteSegmentCoverage.ToList().IndexOf(texture);
        Assert.True(index >= 0, $"no coverage sprite drew texture {texture}");
        IReadOnlyList<float> v = renderer.DebugSpriteSegmentVerts[index].Verts;
        return new Vector4(v[4], v[5], v[6], v[7]);
    }

    private static Vector4 DrawnColour(Rig rig, uint texture)
    {
        (TextRenderer renderer, UiRenderContext context) = Context();
        rig.Button.DrawSelfAndChildren(context);
        return CoverageColour(renderer, texture);
    }

    [Fact]
    public void AnSvgWinsOverTheFileIconAndTheDatSurface()
    {
        Rig rig = Build(fileIcon: (7u, 64, 64), surface: 0x165u);
        Assert.Equal(string.Empty, rig.Button.Text);
        (TextRenderer renderer, UiRenderContext context) = Context();

        rig.Button.DrawSelfAndChildren(context);

        (uint texture, _) = Assert.Single(_backend.Uploaded);
        Assert.Contains(texture, renderer.DebugSpriteSegmentCoverage);
        Assert.DoesNotContain(renderer.DebugSpriteSegmentVerts, seg => seg.Texture == 7u);
    }

    [Fact]
    public void AtTwiceTheScaleTheBakeIsInDevicePixels()
    {
        Rig rig = Build(new PluginUiThemeSettings { Theme = PluginUiTheme.Moss });
        rig.Button.DrawSelfAndChildren(Context(pixelScale: 2f).Context);
        // The dock's artwork box is 24 points: 48 device pixels at 2x.
        Assert.Equal(48, Assert.Single(_backend.Uploaded).Size);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void TheIconLandsOnTheDeviceGrid(float scale)
    {
        Rig rig = Build(new PluginUiThemeSettings { Theme = PluginUiTheme.Moss });
        rig.Button.Left = 10.3f;
        rig.Button.Top = 20.7f;
        (TextRenderer renderer, UiRenderContext context) = Context(pixelScale: scale);
        rig.Button.DrawSelfAndChildren(context);

        int index = renderer.DebugSpriteSegmentCoverage.ToList().IndexOf(_backend.Uploaded[0].Texture);
        IReadOnlyList<float> v = renderer.DebugSpriteSegmentVerts[index].Verts;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i + TextRenderer.FloatsPerVertex <= v.Count; i += TextRenderer.FloatsPerVertex)
        {
            minX = MathF.Min(minX, v[i]);
            maxX = MathF.Max(maxX, v[i]);
            minY = MathF.Min(minY, v[i + 1]);
            maxY = MathF.Max(maxY, v[i + 1]);
        }
        Assert.Equal(MathF.Round(minX * scale), minX * scale, 2);
        Assert.Equal(MathF.Round(minY * scale), minY * scale, 2);
        float expected = PluginSvgIconCache.DevicePixels(24f, scale) / scale;
        Assert.Equal(expected, maxX - minX, 3);
        Assert.Equal(expected, maxY - minY, 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MossTintsMutedWhenClosedAndAccentWhenOpen(bool open)
    {
        Rig rig = Build(new PluginUiThemeSettings { Theme = PluginUiTheme.Moss }, visible: open);
        Vector4 expected = open ? PluginUiPalette.Moss.Accent : PluginUiPalette.Moss.Muted;
        Assert.Equal(expected, DrawnColour(rig, FirstTexture(rig)));
    }

    [Fact]
    public void MossTintsTextWhileHovered()
    {
        Rig rig = Build(new PluginUiThemeSettings { Theme = PluginUiTheme.Moss });
        rig.Root.Tick(0.016d, 16L);
        rig.Root.OnMouseMove(
            (int)(rig.Shelf.Left + rig.Button.Left + 10), (int)(rig.Shelf.Top + rig.Button.Top + 10));
        rig.Root.Tick(0.016d, 32L);
        Assert.Equal(UiControlState.Hovered, rig.Button.State);

        Assert.Equal(PluginUiPalette.Moss.Text, DrawnColour(rig, FirstTexture(rig)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassicTintsInTheClassicDockColours(bool open)
    {
        Rig rig = Build(visible: open);
        Vector4 expected = open ? PluginUiPalette.ClassicDock.Accent : PluginUiPalette.ClassicDock.Muted;
        Assert.Equal(expected, DrawnColour(rig, FirstTexture(rig)));
    }

    [Fact]
    public void AnIconThatCannotBeUploadedFallsBackToTheInitialsAndIsGivenBack()
    {
        _backend.Refuse = true;
        Rig rig = Build();
        rig.Button.DrawSelfAndChildren(Context().Context);
        Assert.Equal("TP", rig.Button.Text);
        Assert.Equal(0, _cache.EntryCount);
    }

    [Fact]
    public void AnIconThatDrawsNothingFallsBackToTheInitialsAndIsGivenBack()
    {
        File.WriteAllText(_path, """<svg viewBox="0 0 24 24"><path d="M0 0h5"/></svg>""");
        Rig rig = Build();
        rig.Button.DrawSelfAndChildren(Context().Context);
        Assert.Equal("TP", rig.Button.Text);
        Assert.Empty(_backend.Uploaded);
        Assert.Equal(0, _cache.EntryCount);
    }

    [Fact]
    public void AnIconThatCannotBeUploadedFallsBackToTheFileIcon()
    {
        _backend.Refuse = true;
        Rig rig = Build(fileIcon: (7u, 64, 64));
        (TextRenderer renderer, UiRenderContext context) = Context();
        rig.Button.DrawSelfAndChildren(context);
        Assert.Equal(string.Empty, rig.Button.Text);
        Assert.Contains(renderer.DebugSpriteSegmentVerts, seg => seg.Texture == 7u);
        Assert.Equal(0, _cache.EntryCount);
    }

    [Fact]
    public void UnregisteringTheWindowGivesTheIconBack()
    {
        Rig rig = Build();
        Assert.Equal(1, _cache.EntryCount);
        rig.Root.WindowManager.Unregister("plugin:acdream.test:main");
        Assert.Equal(0, _cache.EntryCount);
        rig.Shelf.Dispose();
    }

    [Fact]
    public void DisposingTheDockGivesTheIconBack()
    {
        Rig rig = Build();
        rig.Shelf.Dispose();
        Assert.Equal(0, _cache.EntryCount);
    }

    [Fact]
    public void AddingTheSameWindowTwiceGivesTheSecondHoldBack()
    {
        Rig rig = Build();
        PluginSvgIconCache.PluginSvgIconEntry entry = _cache.Acquire("acdream.test/icon.svg", _path)!;
        Assert.Equal(2, entry.Holders);
        rig.Shelf.Add(
            new PluginUiOwner("acdream.test", "Test Plugin"),
            new PluginPanelDescriptor("main", "Test Plugin"),
            rig.Handle,
            null,
            entry);
        Assert.Equal(1, entry.Holders);
        rig.Shelf.Dispose();
    }

    [Fact]
    public void AddingToADisposedDockStillGivesTheHoldBack()
    {
        Rig rig = Build();
        rig.Shelf.Dispose();
        PluginSvgIconCache.PluginSvgIconEntry entry = _cache.Acquire("acdream.test/icon.svg", _path)!;
        Assert.Equal(1, entry.Holders);
        Assert.Throws<ObjectDisposedException>(() => rig.Shelf.Add(
            new PluginUiOwner("acdream.test", "Test Plugin"),
            new PluginPanelDescriptor("other", "Other"),
            rig.Handle,
            null,
            entry));
        Assert.Equal(0, entry.Holders);
        Assert.Equal(0, _cache.EntryCount);
    }

    /// <summary>Bakes the button's icon at 1x with a throwaway draw and returns its texture.</summary>
    private uint FirstTexture(Rig rig)
    {
        if (_backend.Uploaded.Count == 0)
            rig.Button.DrawSelfAndChildren(Context().Context);
        return _backend.Uploaded[0].Texture;
    }
}
