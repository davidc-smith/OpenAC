using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.UI;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class PluginTitleBarTests
{
    private const uint CloseTexture = 77u;

    private const uint PressedTexture = 78u;

    private static (uint, int, int) Resolve(uint id) =>
        id == PluginWindowChrome.ClassicCloseSprite ? (CloseTexture, 24, 25)
        : id == PluginWindowChrome.ClassicClosePressedSprite ? (PressedTexture, 24, 25)
        : (0u, 0, 0);

    private static bool HasHoverWhite(TextRenderer r) => ThemeDrawCapture.Vertices(r)
        .Any(v => v.Color.X > 0.99f && v.Color.Y > 0.99f && v.Color.Z > 0.99f && Math.Abs(v.Color.W - 0.18f) < 0.01f);

    private static (UiRoot Root, UiPanel Frame, PluginTitleBar Bar) Mount(string title = "Buffs")
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var frame = new UiPanel { Left = 100, Top = 100, Width = 300, Height = 200 };
        var bar = new PluginTitleBar(Resolve, frame.Width) { Title = title };
        frame.AddChild(bar);
        root.AddChild(frame);
        root.Tick(0, 0);
        return (root, frame, bar);
    }

    [Fact]
    public void TheBarSpansTheTopAndTheCloseButtonSitsBelowTheBorderAtTheRight()
    {
        var (_, _, bar) = Mount();
        Assert.Equal((0f, 0f, 300f, 24f), (bar.Left, bar.Top, bar.Width, bar.Height));
        Assert.Equal(AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right, bar.Anchors);
        Assert.True(bar.ClickThrough);
        PluginCloseButton close = bar.Close;
        Assert.Equal((276f, 5f, 19f, 19f), (close.Left, close.Top, close.Width, close.Height));
        Assert.Equal(AnchorEdges.Top | AnchorEdges.Right, close.Anchors);
        Assert.True(close.TabStop);
        Assert.True(close.AcceptsFocus);
        Assert.False(close.FocusOnMouseClick);
    }

    [Fact]
    public void ATheme_CentresTheCloseButtonInTheBand()
    {
        var (_, _, bar) = Mount();
        bar.ThemePalette = PluginUiPalette.Moss;
        Assert.Same(PluginUiPalette.Moss, bar.Close.ThemePalette);
        Assert.Equal(3f, bar.Close.Top);
        bar.ThemePalette = null;
        Assert.Null(bar.Close.ThemePalette);
        Assert.Equal(5f, bar.Close.Top);
    }

    [Fact]
    public void ClickingTheCloseButtonRequestsClose()
    {
        var (root, _, bar) = Mount();
        int requests = 0;
        bar.CloseRequested += () => requests++;
        (int x, int y) = (100 + 276 + 9, 100 + 5 + 9);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
        Assert.Equal(1, requests);
        Assert.Null(root.KeyboardFocus);   // a click never takes focus from the game
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void EnterOrSpaceOnTheFocusedCloseButtonRequestsClose(Key key)
    {
        var (root, _, bar) = Mount();
        int requests = 0;
        bar.CloseRequested += () => requests++;
        root.SetKeyboardFocus(bar.Close);
        root.OnKeyDown((int)key);
        Assert.Equal(1, requests);
    }

    [Fact]
    public void Classic_DrawsTheRetailCloseArt()
    {
        var (root, _, _) = Mount();
        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        root.Draw(ctx);
        Assert.Contains(renderer.DebugSpriteSegmentVerts, s => s.Texture == CloseTexture);
    }

    [Fact]
    public void Classic_AtRestDrawsTheNormalArtWithNoHoverFill()
    {
        var (root, _, _) = Mount();
        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        root.Draw(ctx);
        Assert.Contains(renderer.DebugSpriteSegmentVerts, s => s.Texture == CloseTexture);
        Assert.DoesNotContain(renderer.DebugSpriteSegmentVerts, s => s.Texture == PressedTexture);
        Assert.False(HasHoverWhite(renderer));
    }

    [Fact]
    public void Classic_HoverBrightensTheRetailArt()
    {
        var (root, _, _) = Mount();
        root.OnMouseMove(100 + 276 + 9, 100 + 5 + 9);
        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        root.Draw(ctx);
        Assert.Contains(renderer.DebugSpriteSegmentVerts, s => s.Texture == CloseTexture);
        Assert.True(HasHoverWhite(renderer));
    }

    [Fact]
    public void Classic_PressedDrawsTheRetailPressedArt()
    {
        var (root, _, _) = Mount();
        root.OnMouseDown(UiMouseButton.Left, 100 + 276 + 9, 100 + 5 + 9);
        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        root.Draw(ctx);
        Assert.Contains(renderer.DebugSpriteSegmentVerts, s => s.Texture == PressedTexture);
        Assert.DoesNotContain(renderer.DebugSpriteSegmentVerts, s => s.Texture == CloseTexture);
    }

    [Fact]
    public void Themed_CloseButtonIsAFlatFieldFaceWithAnX()
    {
        var (root, _, bar) = Mount();
        var p = PluginUiPalette.Moss;
        bar.ThemePalette = p;
        root.Tick(0.016, 1);

        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        root.Draw(ctx);
        Assert.DoesNotContain(renderer.DebugSpriteSegmentVerts, s => s.Texture == CloseTexture);
        Assert.Equal("x", bar.Close.Text);
        Assert.Equal(p.Text, bar.Close.TextColor);
        var field = ThemeDrawCapture.Vertices(renderer).Where(v => SameColor(v.Color, p.Field)).ToList();
        Assert.NotEmpty(field);
        // The close button sits at (276, 3) in the bar, 19 points square, frame at (100, 100).
        Assert.True(field.Min(v => v.Position.X) <= 376f && field.Max(v => v.Position.X) >= 395f);
        Assert.True(field.Min(v => v.Position.Y) <= 103f && field.Max(v => v.Position.Y) >= 122f);
        Assert.True(ThemeDrawCapture.HasColor(ThemeDrawCapture.Vertices(renderer), p.Border));
    }

    [Fact]
    public void Themed_ThenClassic_TheCloseButtonDrawsNoFace()
    {
        var (root, _, bar) = Mount();
        var p = PluginUiPalette.Moss;
        bar.ThemePalette = p;
        bar.ThemePalette = null;
        root.Tick(0.016, 1);

        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        root.Draw(ctx);
        Assert.Contains(renderer.DebugSpriteSegmentVerts, s => s.Texture == CloseTexture);
        Assert.Equal(string.Empty, bar.Close.Text);
        var vertices = ThemeDrawCapture.Vertices(renderer);
        Assert.False(ThemeDrawCapture.HasColor(vertices, p.Field));
        Assert.False(ThemeDrawCapture.HasColor(vertices, p.Border));
    }

    private static bool SameColor(Vector4 a, Vector4 b) =>
        a.W > 0.05f && MathF.Abs(a.X - b.X) <= 0.004f && MathF.Abs(a.Y - b.Y) <= 0.004f && MathF.Abs(a.Z - b.Z) <= 0.004f;

    [Fact]
    public void TheTitleIsShortenedToEndBeforeTheCloseButton()
    {
        var font = BundledUiFont.Bake(12).CreateFont(1);
        var (_, _, bar) = Mount(new string('W', 80));
        bar.DatFont = font;
        string shown = bar.DisplayedTitle(s => font.MeasureWidth(s));
        Assert.EndsWith("...", shown);
        Assert.True(PluginTitleBar.ClassicTextLeft + font.MeasureWidth(shown) <= bar.Close.Left - 4f);
    }

    [Fact]
    public void TheTitleWidthFollowsTheBarWidthWithoutADraw()
    {
        var font = BundledUiFont.Bake(12).CreateFont(1);
        var (_, _, bar) = Mount(new string('W', 80));
        bar.DatFont = font;
        string wide = bar.DisplayedTitle(s => font.MeasureWidth(s));
        bar.Width = 200;
        string narrow = bar.DisplayedTitle(s => font.MeasureWidth(s));
        Assert.True(narrow.Length < wide.Length);
        Assert.True(PluginTitleBar.ClassicTextLeft + font.MeasureWidth(narrow) <= 200 - 24f - 4f);
    }

    [Theory]
    [InlineData(14f)]
    [InlineData(17f)]
    public void TheTitleTextOriginIsPinnedPerLook(float lineHeight)
    {
        var (_, _, bar) = Mount();
        Assert.Equal((8f, 5f + MathF.Floor((19f - lineHeight) / 2f + 0.5f)), bar.TextOrigin(lineHeight));
        bar.ThemePalette = PluginUiPalette.Moss;
        Assert.Equal((12f, MathF.Floor((24f - lineHeight) / 2f + 0.5f)), bar.TextOrigin(lineHeight));
    }

    [Fact]
    public void TheCloseButtonSaysWhatItDoes()
    {
        var (_, _, bar) = Mount();
        Assert.Equal("Close", bar.Close.GetTooltipText());
        bar.Close.Tooltip = PluginWindowChrome.DockedCloseTooltip;
        Assert.Equal("Close (reopen from the plugin sidepanel)", bar.Close.GetTooltipText());
    }

    [Fact]
    public void TheContentHostIsATransparentStretchingClip()
    {
        var host = new UiPluginContentHost();
        Assert.True(host.ClickThrough);
        Assert.Equal(AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right | AnchorEdges.Bottom, host.Anchors);
        Assert.True(host.ClipsChildrenForTest);
    }
}
