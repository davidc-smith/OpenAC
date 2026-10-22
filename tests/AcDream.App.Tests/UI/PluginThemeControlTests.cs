using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>Themed controls draw the modern shapes and states; see PluginUiStyle.</summary>
public sealed class PluginThemeControlTests
{
    private static readonly PluginUiPalette P = PluginUiPalette.Moss;

    internal sealed class Binding
    {
        public Action Click => () => { };
        public bool On => true;
        public bool Off => false;
        public Action Toggle => () => { };
        public IReadOnlyList<string> Items => ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];
        public string Selected => "Beta";
        public int Index => 1;
        public float Value => 0.5f;
        public Action<float> Changed => _ => { };
        public IReadOnlyList<string> Log => Enumerable.Range(0, 40).Select(i => $"line {i}").ToArray();
        public float Fill => 0.5f;
        public bool? Maybe => null;
        public Action<bool> SetFlag => _ => { };
        public int Choice => 1;
        public Action<int> Choose => _ => { };
        public int Count => 3;
        public Action<int> SetCount => _ => { };
        public string Key => "Ctrl+K";
        public Action<string> SetKey => _ => { };
        public uint Colour => 0xFF336699;
        public Action<uint> SetColour => _ => { };
        public IReadOnlyList<string> TreeNames => ["A", "A1", "B"];
        public IReadOnlyList<int> TreeLevels => [0, 1, 0];
        public IReadOnlyList<string> TreeKeys => ["a", "a1", "b"];
    }

    /// <summary>Builds one themed window around <paramref name="body"/>, switched to Moss.</summary>
    internal static (UiRoot Root, UiNineSlicePanel Panel) Themed(string body)
    {
        var settings = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"400\" h=\"300\" theme=\"plugin\">{body}</panel>",
            new Binding(), _ => (0u, 0, 0), themes: settings);
        var root = new UiRoot { Width = 600, Height = 600 };
        root.AddChild(panel);
        root.Tick(0.016, 1);
        return (root, panel);
    }

    internal static List<(Vector2 Position, Vector4 Color)> DrawElement(UiElement element)
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        element.DrawSelfAndChildren(ctx);
        return ThemeDrawCapture.Vertices(renderer);
    }

    [Fact]
    public void AThemedButtonShowsHoverAndPressWithoutChangingWhatItHandles()
    {
        var (_, panel) = Themed("<button x=\"10\" y=\"10\" w=\"80\" h=\"24\" text=\"Go\" onclick=\"{Click}\" />");
        var button = Assert.IsType<UiSimpleButton>(panel.Children[0]);
        Assert.Same(P, button.ThemePalette);
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(button), P.Field));

        button.OnEvent(new UiEvent { Type = UiEventType.HoverEnter });
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(button), PluginUiStyle.Hover(P, P.Field)));
        bool handledDown = button.OnEvent(new UiEvent { Type = UiEventType.MouseDown });
        Assert.False(handledDown);
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(button), PluginUiStyle.Pressed(P.Field)));
    }

    private static List<float> CaptionAlphas(UiSimpleButton button)
    {
        button.DatFont = BundledUiFont.Bake().CreateFont(42);
        var (renderer, ctx) = ThemeDrawCapture.Context();
        button.DrawSelfAndChildren(ctx);
        var alphas = new List<float>();
        foreach (var seg in renderer.DebugSpriteSegmentVerts.Where(s => s.Texture == 42u))
            for (int i = 0; i + TextRenderer.FloatsPerVertex <= seg.Verts.Count; i += TextRenderer.FloatsPerVertex)
                alphas.Add(seg.Verts[i + 7]);
        return alphas;
    }

    [Fact]
    public void ADisabledThemedButtonFadesItsCaptionToo()
    {
        var (_, panel) = Themed(
            "<button x=\"10\" y=\"10\" w=\"80\" h=\"24\" text=\"Start\" onclick=\"{Click}\" />" +
            "<button x=\"100\" y=\"10\" w=\"80\" h=\"24\" text=\"Stop\" enabled=\"{Off}\" onclick=\"{Click}\" />");
        var enabled = Assert.IsType<UiSimpleButton>(panel.Children[0]);
        var disabled = Assert.IsType<UiSimpleButton>(panel.Children[1]);
        Assert.False(disabled.Enabled);

        var on = CaptionAlphas(enabled);
        Assert.NotEmpty(on);
        Assert.All(on, a => Assert.Equal(1f, a, 1e-4f));

        var off = CaptionAlphas(disabled);
        Assert.NotEmpty(off);
        Assert.All(off, a => Assert.True(a <= PluginUiStyle.DisabledAlpha + 1e-4f, $"alpha {a}"));
    }

    [Fact]
    public void ASelectedThemedTabIsAPillWithoutUnderline()
    {
        var (_, panel) = Themed(
            "<tab x=\"10\" y=\"10\" w=\"80\" h=\"24\" text=\"Status\" selected=\"{On}\" onclick=\"{Click}\" />" +
            "<tab x=\"100\" y=\"10\" w=\"80\" h=\"24\" text=\"Spells\" selected=\"{Off}\" onclick=\"{Click}\" />");
        var selected = Assert.IsType<UiMarkupTabButton>(panel.Children[0]);
        var other = Assert.IsType<UiMarkupTabButton>(panel.Children[1]);
        var v = DrawElement(selected);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Selected));
        Assert.False(ThemeDrawCapture.HasColor(v, P.Accent));
        Assert.Equal(P.Text, selected.TextColor);
        Assert.Equal(P.Muted, other.TextColor);
    }

    [Fact]
    public void AThemedToggleIsASwitchWithItsCaptionAfterIt()
    {
        var (_, panel) = Themed("<toggle x=\"10\" y=\"10\" w=\"160\" h=\"20\" text=\"Auto\" checked=\"{On}\" onclick=\"{Toggle}\" />");
        var toggle = Assert.IsType<UiMarkupToggle>(panel.Children[0]);
        var v = DrawElement(toggle);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Text));                    // knob, on
        Assert.True(ThemeDrawCapture.HasColor(v, P.Accent));                  // switch edge, on

        // With a real font the caption is drawn; its ink starts after the switch and the gap.
        toggle.DatFont = BundledUiFont.Bake().CreateFont(42);
        var (renderer, ctx) = ThemeDrawCapture.Context();
        toggle.DrawSelfAndChildren(ctx);
        var caption = renderer.DebugSpriteSegmentVerts.Where(s => s.Texture == 42u).ToList();
        Assert.NotEmpty(caption);
        float minX = float.MaxValue;
        foreach (var seg in caption)
            for (int i = 0; i + TextRenderer.FloatsPerVertex <= seg.Verts.Count; i += TextRenderer.FloatsPerVertex)
                minX = MathF.Min(minX, seg.Verts[i]);
        float expected = toggle.Left + 1f + PluginUiStyle.SwitchWidth + PluginUiStyle.SwitchCaptionGap;
        Assert.InRange(minX, expected - 0.5f, expected + 4f);
    }

    [Fact]
    public void AThemedFieldGlowsWhenFocused()
    {
        var (_, panel) = Themed("<field x=\"10\" y=\"10\" w=\"160\" h=\"24\" text=\"abc\" />");
        var field = Assert.IsType<UiField>(panel.Children[0]);
        Assert.Same(P, field.ThemePalette);
        var blurred = DrawElement(field);
        Assert.True(ThemeDrawCapture.HasColor(blurred, P.Border));
        Assert.False(ThemeDrawCapture.HasColor(blurred, P.Accent));
        field.OnEvent(new UiEvent { Type = UiEventType.FocusGained });
        Assert.True(field.IsFocused);
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(field), P.Accent));
    }

    [Fact]
    public void AThemedMenuHasARoundedFaceAndAShadowedPopup()
    {
        var (root, panel) = Themed("<menu x=\"10\" y=\"10\" w=\"160\" h=\"24\" items=\"{Items}\" selected=\"{Selected}\" rows=\"3\" />");
        var menu = Assert.IsType<UiMenu>(panel.Children[0]);
        Assert.Same(P, menu.ThemePalette);
        menu.OnEvent(new UiEvent { Type = UiEventType.HoverEnter });
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(menu), PluginUiStyle.Hover(P, menu.PlainBackgroundColor)));

        int x = (int)(menu.Left + 20), y = (int)(menu.Top + 12);
        root.OnMouseDown(UiMouseButton.Left, x, y, 0);
        root.OnMouseUp(UiMouseButton.Left, x, y, 0);
        Assert.True(menu.IsOpen);
        var (renderer, ctx) = ThemeDrawCapture.Context();
        root.Draw(ctx);
        var v = ThemeDrawCapture.Vertices(renderer);
        Assert.Contains(v, p => p.Color.X == 0f && p.Color.Y == 0f && p.Color.Z == 0f && p.Color.W > 0f
            && p.Position.Y > menu.Top + menu.Height);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Accent));
    }

    [Fact]
    public void AThemedListHasInsetRowsAndASlimThumb()
    {
        var (_, panel) = Themed("<list x=\"0\" y=\"0\" w=\"200\" h=\"54\" items=\"{Items}\" selected=\"{Index}\" selectionband=\"true\" />");
        var list = Assert.IsType<UiMarkupList>(panel.Children[0]);
        var v = DrawElement(list);
        var band = v.Where(p => p.Color.W > 0.99f && Vector4.Distance(p.Color, P.Selected) < 0.01f).ToList();
        Assert.NotEmpty(band);
        Assert.True(band.Min(p => p.Position.X) >= PluginUiStyle.RowInset - 0.51f);
        var thumb = v.Where(p => p.Color.W > 0.99f && Vector4.Distance(p.Color, P.Muted) < 0.01f).ToList();
        Assert.NotEmpty(thumb);
        Assert.True(thumb.Max(p => p.Position.X) - thumb.Min(p => p.Position.X) <= PluginUiStyle.ScrollThumbWidth + 1.01f);
    }

    [Fact]
    public void AThemedSliderHasAFillAndARoundThumb()
    {
        var (root, panel) = Themed("<slider x=\"0\" y=\"0\" w=\"200\" h=\"16\" value=\"{Value}\" onchange=\"{Changed}\" />");
        var slider = Assert.IsType<UiScrollbar>(panel.Children[0]);
        root.Tick(0.016, 2);
        var v = DrawElement(slider);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Accent));
        Assert.True(ThemeDrawCapture.HasColor(v, P.Text));
    }

    [Fact]
    public void AThemedMeterIsRoundedInItsOwnColour()
    {
        var (_, panel) = Themed("<meter x=\"0\" y=\"0\" w=\"200\" h=\"12\" fill=\"{Fill}\" color=\"#FFCC3333\" />");
        var meter = Assert.IsType<UiMeter>(panel.Children[0]);
        Assert.Same(P, meter.ThemePalette);
        var v = DrawElement(meter);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Field));
        Assert.True(ThemeDrawCapture.HasColor(v, new Vector4(0xCC / 255f, 0x33 / 255f, 0x33 / 255f, 1f)));
        Assert.True(v.Count > 12);  // rounded, not two rectangles
    }

    [Fact]
    public void AThemedLogSitsInARoundedContainer()
    {
        var (root, panel) = Themed("<log x=\"0\" y=\"0\" w=\"200\" h=\"80\" items=\"{Log}\" />");
        root.Tick(0.016, 2);
        var v = DrawElement(panel.Children[0]);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Field));
        Assert.True(v.Count > 12);
    }

    [Fact]
    public void AThemedMenuPopupHoversInHoverColourDistinctFromSelected()
    {
        var settings = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build(
            "<panel x=\"0\" y=\"0\" w=\"400\" h=\"300\" theme=\"plugin\"><menu x=\"10\" y=\"10\" w=\"160\" h=\"24\" items=\"{Items}\" selected=\"{Selected}\" rows=\"3\" /></panel>",
            new Binding(), _ => (0u, 0, 0), themes: settings);
        var root = new UiRoot { Width = 600, Height = 600 };
        root.AddChild(panel);
        root.Tick(0.016, 1);
        var menu = panel.Children.OfType<UiMenu>().First();
        var classicHover = new UiMenu().PlainHoverColor;
        Assert.Equal(PluginUiStyle.Hover(P, P.Field), menu.PlainHoverColor);
        Assert.NotEqual(menu.PlainSelectedColor, menu.PlainHoverColor);
        settings.Theme = PluginUiTheme.Classic;
        root.Tick(0.016, 2);
        Assert.Equal(classicHover, menu.PlainHoverColor);
    }

    private static bool VertexAt(IEnumerable<(Vector2 Position, Vector4 Color)> v, float x, float y, Vector4 color) =>
        v.Any(p => MathF.Abs(p.Position.X - x) < 0.01f && MathF.Abs(p.Position.Y - y) < 0.01f
            && Vector4.Distance(p.Color, color) < 0.01f);

    /// <summary>Whether a vertex carries this exact colour, alpha included (HasColor ignores alpha).</summary>
    private static bool HasExactColor(IEnumerable<(Vector2 Position, Vector4 Color)> v, Vector4 color) =>
        v.Any(p => Vector4.Distance(p.Color, color) < 0.01f);

    private static readonly Vector4 Gold = new(1f, 0.82f, 0.25f, 1f);

    [Fact]
    public void AThemedCheckboxIsARoundedTickBoxWithAFocusRing()
    {
        var (root, panel) = Themed(
            "<checkbox x=\"10\" y=\"10\" w=\"120\" h=\"20\" text=\"Loot\" checked=\"{On}\" onchange=\"{SetFlag}\" />" +
            "<checkbox x=\"10\" y=\"40\" w=\"120\" h=\"20\" text=\"Some\" checked=\"{Maybe}\" onchange=\"{SetFlag}\" />");
        var on = Assert.IsType<UiMarkupCheckbox>(panel.Children[0]);
        var some = Assert.IsType<UiMarkupCheckbox>(panel.Children[1]);
        var v = DrawElement(on);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Accent));
        // The box sits at (1, 4.5) inside the checkbox at (10, 10): its square corner would be (11, 14.5).
        Assert.False(VertexAt(v, 11f, 14.5f, P.Accent));                  // rounded, no square corner
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(some), P.Field)); // the dash

        root.SetKeyboardFocus(on);
        var focused = DrawElement(on);
        Assert.True(HasExactColor(focused, PluginUiStyle.FocusRingColor(P)));
        Assert.False(ThemeDrawCapture.HasColor(focused, Gold));
    }

    [Fact]
    public void AThemedRadioButtonIsRound()
    {
        var (_, panel) = Themed(
            "<radio x=\"10\" y=\"10\" w=\"120\" h=\"40\" items=\"A|B\" selected=\"{Choice}\" onchange=\"{Choose}\" />");
        var radio = Assert.IsType<UiMarkupRadio>(panel.Children[0]);
        var accent = DrawElement(radio).Where(p => Vector4.Distance(p.Color, P.Accent) < 0.01f).ToList();
        Assert.NotEmpty(accent);
        // Option 1 is the second 20-point cell (local y 20); its glyph sits at (11, 10 + 20 + 4.5).
        var centre = new Vector2(11f + 5.5f, 30f + 4.5f + 5.5f);
        Assert.All(accent, p => Assert.True(Vector2.Distance(p.Position, centre) <= 5.6f, $"{p.Position}"));
    }

    [Fact]
    public void AThemedSegmentedRadioIsARoundedTrackWithASelectedPill()
    {
        var (_, panel) = Themed(
            "<radio x=\"10\" y=\"10\" w=\"120\" h=\"24\" items=\"A|B\" orientation=\"horizontal\" style=\"segmented\" selected=\"{Choice}\" onchange=\"{Choose}\" />");
        var radio = Assert.IsType<UiMarkupRadio>(panel.Children[0]);
        Assert.True(radio.Segmented);
        var v = DrawElement(radio);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Selected));
        Assert.False(VertexAt(v, 10f, 10f, P.Field));
    }

    [Fact]
    public void AThemedNumberSpinHasChevronsAndNoSquareOutline()
    {
        var (_, panel) = Themed(
            "<number x=\"10\" y=\"10\" w=\"100\" h=\"24\" value=\"{Count}\" onchange=\"{SetCount}\" />");
        var number = Assert.IsType<UiMarkupNumber>(panel.Children[0]);
        var v = DrawElement(number);
        float spinX = number.Left + number.Width - UiMarkupNumber.SpinWidth;
        Assert.DoesNotContain(v, p => p.Position.X > spinX + 0.5f && Vector4.Distance(p.Color, P.Border) < 0.01f);
        Assert.Contains(v, p => p.Position.X > spinX && Vector4.Distance(p.Color, P.Muted) < 0.01f);
    }

    [Fact]
    public void AThemedHotkeyAndColourGlowWhenFocused()
    {
        var (root, panel) = Themed(
            "<hotkey x=\"10\" y=\"10\" w=\"120\" h=\"24\" value=\"{Key}\" onchange=\"{SetKey}\" />" +
            "<color x=\"10\" y=\"40\" w=\"40\" h=\"24\" value=\"{Colour}\" onchange=\"{SetColour}\" />");
        var hotkey = Assert.IsType<UiMarkupHotkey>(panel.Children[0]);
        var colour = Assert.IsType<UiMarkupColor>(panel.Children[1]);
        Assert.False(VertexAt(DrawElement(hotkey), 10f, 10f, P.Border));

        root.SetKeyboardFocus(hotkey);
        Assert.True(HasExactColor(DrawElement(hotkey), PluginUiStyle.FocusGlow(P)));
        root.SetKeyboardFocus(colour);
        Assert.True(HasExactColor(DrawElement(colour), PluginUiStyle.FocusGlow(P)));
    }

    [Fact]
    public void AThemedListHeaderSitsInsideTheRoundedContainer()
    {
        var (_, panel) = Themed(
            "<list x=\"0\" y=\"0\" w=\"200\" h=\"80\" header=\"true\" selected=\"{Index}\" onchange=\"{Choose}\">" +
            "<column type=\"text\" header=\"Name\" items=\"{Items}\"/></list>");
        var list = Assert.IsType<UiMarkupList>(panel.Children[0]);
        var v = DrawElement(list);
        Assert.False(VertexAt(v, 0f, 0f, P.Field));                           // no square band at the corner
        var underline = v.Where(p => MathF.Abs(p.Position.Y - (UiMarkupList.HeaderHeight - 1f)) < 0.01f
            && Vector4.Distance(p.Color, P.Border) < 0.01f).ToList();
        Assert.NotEmpty(underline);
        Assert.Equal(PluginUiStyle.RowInset, underline.Min(p => p.Position.X), 2);
        Assert.Equal(200f - PluginUiStyle.RowInset, underline.Max(p => p.Position.X), 2);
    }

    [Fact]
    public void AThemedTreeArrowIsASmoothChevron()
    {
        var (_, panel) = Themed(
            "<list x=\"0\" y=\"0\" w=\"200\" h=\"80\" tree=\"true\" levels=\"{TreeLevels}\" keys=\"{TreeKeys}\" selected=\"{Index}\" onchange=\"{Choose}\">" +
            "<column type=\"text\" items=\"{TreeNames}\"/></list>");
        var list = Assert.IsType<UiMarkupList>(panel.Children[0]);
        // Row 0 (the parent "A") spans y 0..18; its arrow slot is x 3..15.
        var arrow = DrawElement(list)
            .Where(p => p.Position.X < 16f && p.Position.Y < 18f && p.Color.W > 0.05f
                && MathF.Abs(p.Color.X - P.Text.X) < 0.004f && MathF.Abs(p.Color.Y - P.Text.Y) < 0.004f
                && MathF.Abs(p.Color.Z - P.Text.Z) < 0.004f)
            .ToList();
        Assert.NotEmpty(arrow);
        Assert.Contains(arrow, p => MathF.Abs(p.Position.X - MathF.Round(p.Position.X)) > 0.01f);
    }

    [Fact]
    public void AThemedExpanderArrowIsAMutedChevron()
    {
        var (_, panel) = Themed("<expander x=\"8\" y=\"8\" w=\"200\" h=\"100\" text=\"Advanced\"/>");
        var expander = Assert.IsType<UiMarkupExpander>(panel.Children[0]);
        float left = expander.Header.ScreenPosition.X;
        var arrow = DrawElement(expander.Header)
            .Where(p => p.Position.X < left + UiMarkupExpanderHeader.CaptionX && p.Color.W > 0.05f
                && MathF.Abs(p.Color.X - P.Muted.X) < 0.004f && MathF.Abs(p.Color.Y - P.Muted.Y) < 0.004f
                && MathF.Abs(p.Color.Z - P.Muted.Z) < 0.004f)
            .ToList();
        Assert.NotEmpty(arrow);
        Assert.Contains(arrow, p => MathF.Abs(p.Position.X - MathF.Round(p.Position.X)) > 0.01f);
    }

    [Fact]
    public void AThemedSplitBarIsASlimGripPill()
    {
        var (_, panel) = Themed(
            "<split x=\"10\" y=\"10\" w=\"305\" h=\"200\"><group/><group/></split>");
        var split = Assert.IsType<UiMarkupSplit>(panel.Children[0]);
        var v = DrawElement(split.Bar);
        Assert.False(ThemeDrawCapture.HasColor(v, P.Field));
        var grip = v.Where(p => p.Color.W > 0.05f && MathF.Abs(p.Color.X - P.Border.X) < 0.004f
            && MathF.Abs(p.Color.Y - P.Border.Y) < 0.004f && MathF.Abs(p.Color.Z - P.Border.Z) < 0.004f).ToList();
        Assert.NotEmpty(grip);
        float Along((Vector2 Position, Vector4 Color) p) => split.Horizontal ? p.Position.Y : p.Position.X;
        Assert.True(grip.Max(Along) - grip.Min(Along) <= 24.01f);
    }

    private static List<(Vector2 Position, Vector4 Color)> DrawOverlays(UiRoot root)
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        root.DrawOverlays(ctx);
        return ThemeDrawCapture.Vertices(renderer);
    }

    private static bool IsShadow((Vector2 Position, Vector4 Color) p) =>
        p.Color.W > 0f && p.Color.X == 0f && p.Color.Y == 0f && p.Color.Z == 0f;

    [Fact]
    public void AThemedContextMenuIsShadowedAndHoversInTheHoverColour()
    {
        var (root, panel) = Themed(
            "<button x=\"10\" y=\"10\" w=\"80\" h=\"20\" text=\"Go\" onclick=\"{Click}\">" +
            "<contextmenu items=\"{Items}\" onselect=\"{Choose}\"/></button>");
        var menu = panel.Children[0].Children.OfType<UiMarkupContextMenu>().Single();
        root.OnMouseDown(UiMouseButton.Right, 20, 15);
        root.OnMouseUp(UiMouseButton.Right, 20, 15);
        Assert.True(menu.IsOpen);
        var (x, y, w, h) = menu.PopupScreenRect;
        Assert.Contains(DrawOverlays(root), p => IsShadow(p) && p.Position.Y > y + h);

        root.OnKeyDown((int)Silk.NET.Input.Key.Down);
        var keyed = DrawOverlays(root);
        Assert.True(HasExactColor(keyed, PluginUiStyle.Hover(P, P.Field)));
        Assert.False(ThemeDrawCapture.HasColor(keyed, P.Selected));
    }

    [Fact]
    public void AThemedColourPopupIsShadowed()
    {
        var (root, panel) = Themed(
            "<color x=\"10\" y=\"40\" w=\"40\" h=\"24\" value=\"{Colour}\" onchange=\"{SetColour}\" />");
        var colour = Assert.IsType<UiMarkupColor>(panel.Children[0]);
        root.OnMouseDown(UiMouseButton.Left, 20, 50);
        root.OnMouseUp(UiMouseButton.Left, 20, 50);
        Assert.True(colour.IsOpen);
        var (x, y, w, h) = colour.Popup.PopupScreenRect;
        Assert.Contains(DrawOverlays(root), p => IsShadow(p)
            && (p.Position.Y > y + h || p.Position.X > x + w || p.Position.X < x || p.Position.Y < y));
    }

    [Fact]
    public void AThemedDrawnDialogIsARoundedWindowWithItsTitleInTheTitleFont()
    {
        var atlas = BundledUiFont.Bake();
        var body = atlas.CreateFont(41);
        var title = atlas.CreateFont(42);
        var dialog = new UiDrawnPluginDialog(
            "Delete", "Delete it?", ["OK", "Cancel"], body, () => P, titleFont: title);
        Assert.All(dialog.Buttons, b => Assert.Same(P, b.ThemePalette));
        Assert.Same(P, dialog.CloseButton.ThemePalette);

        var (renderer, ctx) = ThemeDrawCapture.Context();
        dialog.DrawSelfAndChildren(ctx);
        var segments = renderer.DebugSpriteSegmentVerts;
        float MinY(IReadOnlyList<float> f) => Enumerable.Range(0, f.Count / 8).Min(i => f[i * 8 + 1]);
        float MaxY(IReadOnlyList<float> f) => Enumerable.Range(0, f.Count / 8).Max(i => f[i * 8 + 1]);
        var titled = segments.Where(s => s.Texture == 42u && s.Verts.Count > 0).ToList();
        Assert.NotEmpty(titled);
        Assert.All(titled, s => Assert.True(MaxY(s.Verts) <= UiDrawnPluginDialog.TitleHeight));
        Assert.Contains(segments, s => s.Texture == 41u && s.Verts.Count > 0
            && MinY(s.Verts) >= UiDrawnPluginDialog.TitleHeight);
        Assert.False(VertexAt(ThemeDrawCapture.Vertices(renderer), 0f, 0f, P.Border));
    }

    [Fact]
    public void AThemedDrawnDialogsCloseButtonIsTheTitleBarsGhostX()
    {
        var font = BundledUiFont.Bake().CreateFont(41);
        var themed = new UiDrawnPluginDialog("Delete", "Delete it?", ["OK"], font, () => P);
        var (renderer, ctx) = ThemeDrawCapture.Context();
        themed.CloseButton.DrawSelfAndChildren(ctx);
        Assert.DoesNotContain(renderer.DebugSpriteSegmentVerts, s => s.Texture == 41u && s.Verts.Count > 0);
        var vertices = ThemeDrawCapture.Vertices(renderer);
        Assert.True(ThemeDrawCapture.HasColor(vertices, P.Muted));
        Assert.False(ThemeDrawCapture.HasColor(vertices, P.Field));

        var classic = new UiDrawnPluginDialog("Delete", "Delete it?", ["OK"], font, static () => null);
        (renderer, ctx) = ThemeDrawCapture.Context();
        classic.CloseButton.DrawSelfAndChildren(ctx);
        Assert.Contains(renderer.DebugSpriteSegmentVerts, s => s.Texture == 41u && s.Verts.Count > 0);
    }

    [Fact]
    public void SwitchingToClassicClearsEveryControlsPalette()
    {
        var settings = new PluginUiThemeSettings { Theme = PluginUiTheme.Brass };
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"400\" h=\"400\" theme=\"plugin\">{PluginThemeClassicIdentityTests.Body}</panel>",
            new PluginThemeClassicIdentityTests.Binding(), _ => (0u, 0, 0), themes: settings);
        var root = new UiRoot { Width = 600, Height = 600 };
        root.AddChild(panel);
        root.Tick(0.016, 1);
        PluginUiPalette?[] Palettes() =>
        [
            panel.Children.OfType<UiSimpleButton>().First().ThemePalette,
            panel.Children.OfType<UiMarkupTabButton>().First().ThemePalette,
            panel.Children.OfType<UiMarkupToggle>().First().ThemePalette,
            panel.Children.OfType<UiField>().First().ThemePalette,
            panel.Children.OfType<UiMenu>().First().ThemePalette,
            panel.Children.OfType<UiScrollbar>().First().ThemePalette,
            panel.Children.OfType<UiMarkupList>().First().ThemePalette,
            panel.Children.OfType<UiMeter>().First().ThemePalette,
        ];
        Assert.All(Palettes(), p => Assert.Same(PluginUiPalette.Brass, p));
        settings.Theme = PluginUiTheme.Classic;
        root.Tick(0.016, 2);
        Assert.All(Palettes(), Assert.Null);
    }

    [Fact]
    public void A_themed_horizontal_scroll_bar_draws_a_slim_thumb_along_its_track()
    {
        var model = new UiScrollable();
        model.SetExtents(400, 100);
        var bar = new UiScrollbar
        {
            Model = model, Horizontal = true, RetailArt = false, ThemePalette = P,
            Width = 100f, Height = 16f,
        };
        var root = new UiRoot { Width = 200, Height = 100 };
        root.AddChild(bar);

        (var renderer, var context) = ThemeDrawCapture.Context(200f, 100f);
        root.Draw(context);
        var thumb = ThemeDrawCapture.Vertices(renderer)
            .Where(v => v.Color.W > 0.05f && MathF.Abs(v.Color.X - P.Muted.X) < 0.004f && MathF.Abs(v.Color.Y - P.Muted.Y) < 0.004f)
            .ToList();

        // A 4-point thumb centred in the 16-point lane, a quarter of the 68-point track long.
        Assert.NotEmpty(thumb);
        Assert.InRange(thumb.Min(v => v.Position.Y), 5.5f, 6.5f);
        Assert.InRange(thumb.Max(v => v.Position.Y), 9.5f, 10.5f);
        Assert.InRange(thumb.Max(v => v.Position.X) - thumb.Min(v => v.Position.X), 16f, 18f);
    }
}
