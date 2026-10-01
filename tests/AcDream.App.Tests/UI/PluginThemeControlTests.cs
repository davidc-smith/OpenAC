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
}
