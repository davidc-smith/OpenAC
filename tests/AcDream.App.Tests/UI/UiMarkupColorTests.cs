using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public class UiMarkupColorTests
{
    private sealed class Model
    {
        public uint Value;
        public List<uint> Reports { get; } = new();
    }

    private sealed class FakeTextures : IMarkupTextureService
    {
        private uint _next = 100;
        public List<(uint Handle, int W, int H, string Name)> Uploads { get; } = new();
        public List<uint> Released { get; } = new();
        public uint Upload(byte[] rgba, int width, int height, string debugName)
        {
            Assert.Equal(width * height * 4, rgba.Length);
            uint handle = _next++;
            Uploads.Add((handle, width, height, debugName));
            return handle;
        }
        public void Release(uint texture) => Released.Add(texture);
    }

    private static (UiRoot Root, UiMarkupColor Swatch, Model Model) Mount(
        uint value = 0xFFFF0000u, bool alpha = false, IMarkupTextureService? textures = null,
        float top = 10f, MarkupRecentColors? recent = null)
    {
        var model = new Model { Value = value };
        var swatch = new UiMarkupColor
        {
            Left = 10, Top = top, Width = 40, Height = 18,
            ValueSource = () => model.Value,
            Changed = v => { model.Reports.Add(v); model.Value = v; },
            AllowAlpha = alpha,
            Textures = textures,
        };
        if (recent is not null)
            swatch.Recent = recent;
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(swatch);
        return (root, swatch, model);
    }

    private static void Press(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    private static void Key1(UiRoot root, Key key)
    {
        root.OnKeyDown((int)key);
        root.OnKeyUp((int)key);
    }

    private static void Tick(UiRoot root) => root.Tick(0.016, 16);

    private static (UiRoot, UiMarkupColor, Model) Opened(uint value = 0xFFFF0000u, bool alpha = false,
        IMarkupTextureService? textures = null)
    {
        var mounted = Mount(value, alpha, textures);
        Press(mounted.Root, 20, 15);
        Assert.True(mounted.Swatch.IsOpen);
        return mounted;
    }

    [Fact]
    public void Click_OpensThePopupWhichTakesFocus()
    {
        var (root, swatch, _) = Opened();
        Assert.Same(swatch.Popup, root.KeyboardFocus);
        Assert.Equal((10f, 30f, 162f, 200f), swatch.Popup.PopupScreenRect);
    }

    [Fact]
    public void Click_OnADisabledSwatchDoesNothing()
    {
        var (root, swatch, _) = Mount();
        swatch.Enabled = false;
        Press(root, 20, 15);
        Assert.False(swatch.IsOpen);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Keyboard_EnterOpensAndEscapeGivesFocusBack()
    {
        var (root, swatch, _) = Mount();
        root.SetKeyboardFocus(swatch);
        Key1(root, Key.Enter);
        Assert.True(swatch.IsOpen);
        Key1(root, Key.Escape);
        Assert.False(swatch.IsOpen);
        Assert.Same(swatch, root.KeyboardFocus);
    }

    [Fact]
    public void Square_ReportsOnTheNextTickOnly()
    {
        var (root, _, model) = Opened();
        Press(root, 16 + 64, 36 + 64);   // s = 0.5, v = 0.5 of red
        Assert.Empty(model.Reports);
        Tick(root);
        Assert.Equal(new uint[] { 0xFF804040u }, model.Reports);
        Tick(root);
        Assert.Single(model.Reports);
    }

    [Fact]
    public void Drag_ReportsOncePerTickWithTheLatestColour()
    {
        var (root, _, model) = Opened();
        root.OnMouseDown(UiMouseButton.Left, 16 + 64, 36 + 64);
        root.OnMouseMove(16 + 32, 36 + 32);
        root.OnMouseMove(16, 36 + 300);   // clamped: s = 0, v = 0
        Assert.Empty(model.Reports);
        Tick(root);
        Assert.Equal(new uint[] { 0xFF000000u }, model.Reports);
        root.OnMouseUp(UiMouseButton.Left, 16, 36 + 300);
    }

    [Fact]
    public void Drag_BackToTheBoundColourReportsNothing()
    {
        var (root, _, model) = Opened();
        root.OnMouseDown(UiMouseButton.Left, 16 + 64, 36 + 64);
        root.OnMouseMove(16 + 500, 36 - 50);   // clamped: s = 1, v = 1, pure red = the bound value
        Tick(root);
        root.OnMouseUp(UiMouseButton.Left, 16 + 500, 36 - 50);
        Assert.Empty(model.Reports);
    }

    [Fact]
    public void HueStrip_SetsTheHue()
    {
        var (root, _, model) = Opened();
        Press(root, 158, 36 + 64);   // h = 180 at s = v = 1
        Tick(root);
        Assert.Equal(new uint[] { 0xFF00FFFFu }, model.Reports);
    }

    [Fact]
    public void WithoutAlpha_ReportsAreOpaqueAndThereIsNoAlphaStrip()
    {
        var (root, swatch, model) = Opened(0x80FF0000u);
        Assert.Equal(162f, swatch.Popup.PopupScreenRect.W);
        Press(root, 16 + 7, 194 + 7);   // first Classic palette swatch: white
        Tick(root);
        Assert.Equal(new uint[] { 0xFFFFFFFFu }, model.Reports);
    }

    [Fact]
    public void WithAlpha_TheAlphaStripSetsTheAlpha()
    {
        var (root, swatch, model) = Opened(0xFFFF0000u, alpha: true);
        Assert.Equal(184f, swatch.Popup.PopupScreenRect.W);
        Press(root, 180, 36 + 64);   // a = 0.5
        Tick(root);
        Assert.Equal(new uint[] { 0x80FF0000u }, model.Reports);
    }

    [Fact]
    public void ThemedPalette_RowUsesTheTokens()
    {
        var (root, swatch, model) = Mount();
        swatch.ThemePalette = PluginUiPalette.Moss;
        Press(root, 20, 15);
        Press(root, 16 + 5 * 16 + 7, 194 + 7);   // sixth swatch: Accent
        Tick(root);
        Assert.Equal(new[] { MarkupColorValue.FromVector4(PluginUiPalette.Moss.Accent) }, model.Reports);
    }

    [Fact]
    public void Hex_TypedColourAppliesOnEnter()
    {
        var (root, _, model) = Opened();
        Press(root, 30, 175);
        Assert.Equal("#FF0000", ((UiMarkupColor)root.Children[0]).Popup.HexText);
        for (int i = 0; i < 7; i++)
            Key1(root, Key.Backspace);
        foreach (char c in "00ff00zz")
            root.OnChar(c);
        Key1(root, Key.Enter);
        Tick(root);
        Assert.Equal(new uint[] { 0xFF00FF00u }, model.Reports);
    }

    [Fact]
    public void Hex_CapsAtNineCharacters()
    {
        var (root, swatch, _) = Opened(alpha: true);
        Press(root, 30, 175);
        foreach (char c in "123456789")
            root.OnChar(c);
        Assert.Equal("#FFFF0000", swatch.Popup.HexText);
    }

    [Fact]
    public void Hex_UnreadableTextChangesNothing()
    {
        var (root, _, model) = Opened();
        Press(root, 30, 175);
        for (int i = 0; i < 5; i++)
            Key1(root, Key.Backspace);
        Key1(root, Key.Enter);   // "#F" doesn't parse
        Tick(root);
        Assert.Empty(model.Reports);
    }

    [Fact]
    public void Hex_APressElsewhereInThePopupAppliesIt()
    {
        var (root, _, model) = Opened();
        Press(root, 30, 175);
        Key1(root, Key.Backspace);
        root.OnChar('F');               // "#FF000F"
        Press(root, 100, 215);          // an empty slot in the (empty) recent row
        Tick(root);
        Assert.Equal(new uint[] { 0xFFFF000Fu }, model.Reports);
    }

    [Fact]
    public void Escape_RestoresTheOpeningColour()
    {
        var recent = new MarkupRecentColors(null);
        var (root, swatch, model) = Mount(recent: recent);
        Press(root, 20, 15);
        Press(root, 158, 36 + 64);
        Tick(root);
        Press(root, 16 + 64, 36 + 64);   // pending, never reported
        Key1(root, Key.Escape);
        Tick(root);
        Assert.Equal(new uint[] { 0xFF00FFFFu, 0xFFFF0000u }, model.Reports);
        Assert.False(swatch.IsOpen);
        Assert.Null(root.KeyboardFocus);
        Assert.Empty(recent.Colors);
    }

    [Fact]
    public void OutsidePress_KeepsTheColourAndRemembersIt()
    {
        var recent = new MarkupRecentColors(null);
        var (root, swatch, model) = Mount(recent: recent);
        Press(root, 20, 15);
        Press(root, 158, 36 + 64);       // pending cyan
        int fallThrough = 0;
        root.WorldMouseFallThrough += (_, _, _, _) => fallThrough++;
        Press(root, 600, 500);
        Assert.False(swatch.IsOpen);
        Assert.Equal(new uint[] { 0xFF00FFFFu }, model.Reports);
        Assert.Equal(new uint[] { 0xFF00FFFFu }, recent.Colors);
        Assert.Equal(0, fallThrough);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void OutsidePress_SwallowsTheReleaseThenFreesTheCapture()
    {
        var (root, swatch, _) = Opened();
        int fallThrough = 0;
        root.WorldMouseFallThrough += (_, _, _, _) => fallThrough++;
        root.OnMouseDown(UiMouseButton.Left, 600, 500);
        Assert.Same(swatch.Popup, root.Captured);
        root.OnMouseUp(UiMouseButton.Left, 600, 500);
        Assert.Null(root.Captured);
        Assert.Equal(0, fallThrough);
        Press(root, 600, 500);           // nothing open now: the world gets it
        Assert.Equal(2, fallThrough);
        Press(root, 20, 15);
        Assert.True(swatch.IsOpen);
    }

    [Fact]
    public void OutsidePress_WithNoChangeRemembersNothing()
    {
        var recent = new MarkupRecentColors(null);
        var (root, swatch, model) = Mount(recent: recent);
        Press(root, 20, 15);
        Press(root, 20, 15);             // the swatch itself counts as outside
        Assert.False(swatch.IsOpen);
        Assert.Empty(model.Reports);
        Assert.Empty(recent.Colors);
    }

    [Fact]
    public void RecentRow_SetsTheColour()
    {
        var recent = new MarkupRecentColors(null);
        recent.Add(0xFF123456u);
        var (root, _, model) = Mount(recent: recent);
        Press(root, 20, 15);
        Press(root, 16 + 7, 210 + 7);
        Tick(root);
        Assert.Equal(new uint[] { 0xFF123456u }, model.Reports);
    }

    [Fact]
    public void Disabling_ClosesOnTheNextTickAndKeeps()
    {
        var (root, swatch, model) = Opened();
        Press(root, 158, 36 + 64);
        swatch.Enabled = false;
        Tick(root);
        Assert.False(swatch.IsOpen);
        Assert.Equal(new uint[] { 0xFF00FFFFu }, model.Reports);
    }

    [Fact]
    public void Hiding_ClosesAtOnce()
    {
        var textures = new FakeTextures();
        var (_, swatch, _) = Opened(textures: textures);
        swatch.Visible = false;
        Assert.False(swatch.IsOpen);
        Assert.Equal(textures.Uploads.Select(u => u.Handle), textures.Released);
    }

    [Fact]
    public void Textures_UploadedOnOpenReleasedOnClose()
    {
        var textures = new FakeTextures();
        var (root, _, _) = Opened(textures: textures);
        Assert.Equal(
            new[] { (128, 128), (128, 128), (16, 128) },
            textures.Uploads.Select(u => (u.W, u.H)));
        Assert.Empty(textures.Released);
        Key1(root, Key.Escape);
        Assert.Equal(textures.Uploads.Select(u => u.Handle), textures.Released);
    }

    [Fact]
    public void Textures_AlphaAddsTheRamp()
    {
        var textures = new FakeTextures();
        Opened(alpha: true, textures: textures);
        Assert.Equal(4, textures.Uploads.Count);
        Assert.Equal((16, 128), (textures.Uploads[3].W, textures.Uploads[3].H));
    }

    [Fact]
    public void Placement_FlipsAboveNearTheBottom()
    {
        var (root, swatch, _) = Mount(top: 570f);
        Press(root, 20, 575);
        Assert.Equal(570f - 2f - 200f, swatch.Popup.PopupScreenRect.Y);
    }

    [Fact]
    public void Draw_ClassicSwatchBorder()
    {
        var (_, swatch, _) = Mount();
        var renderer = MarkupDrawProbe.Draw(swatch);
        Assert.Equal(4, MarkupDrawProbe.CountQuads(renderer, UiMarkupColor.ClassicBorder));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupColor.CheckerLight));
    }

    [Fact]
    public void Draw_CheckerboardBehindTranslucentColours()
    {
        var (_, swatch, _) = Mount(0x80FF0000u);
        var renderer = MarkupDrawProbe.Draw(swatch);
        Assert.True(MarkupDrawProbe.CountQuads(renderer, UiMarkupColor.CheckerLight) > 0);
    }

    [Fact]
    public void Draw_ThemedSwatchUsesTheBorderToken()
    {
        var (_, swatch, _) = Mount();
        swatch.ThemePalette = PluginUiPalette.Moss;
        var renderer = MarkupDrawProbe.Draw(swatch);
        Assert.Equal(4, MarkupDrawProbe.CountQuads(renderer, PluginUiPalette.Moss.Border));
    }

    [Fact]
    public void Draw_PopupWithTexturesUsesTheHandles()
    {
        var textures = new FakeTextures();
        var (root, _, _) = Opened(alpha: true, textures: textures);
        var renderer = MarkupDrawProbe.DrawOverlays(root);
        var drawn = renderer.DebugSpriteSegmentVerts.Select(s => s.Texture).ToHashSet();
        foreach (var upload in textures.Uploads)
            Assert.Contains(upload.Handle, drawn);
    }

    [Fact]
    public void Draw_PopupWithoutTexturesDrawsFills()
    {
        var (root, _, _) = Opened(alpha: true);
        var renderer = MarkupDrawProbe.DrawOverlays(root);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(renderer, UiMarkupContextMenu.ClassicBackground));
        Assert.True(renderer.DebugSpriteSegmentVerts.Where(s => s.Texture == 0u).Sum(s => s.Verts.Count) / 48 > 256);
    }
}
