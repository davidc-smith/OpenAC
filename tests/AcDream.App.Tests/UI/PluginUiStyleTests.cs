using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class PluginUiStyleTests
{
    private static readonly PluginUiPalette P = PluginUiPalette.Moss;

    [Fact]
    public void DerivedColoursFollowThePalette()
    {
        Assert.Equal(PluginUiStyle.Mix(P.Field, P.Border, 0.30f), PluginUiStyle.Hover(P, P.Field));
        Assert.Equal(PluginUiStyle.Mix(P.Field, new Vector4(0, 0, 0, 1), 0.25f), PluginUiStyle.Pressed(P.Field));
        Assert.Equal(0.30f, PluginUiStyle.FocusGlow(P).W);
        Assert.Equal(0.60f, PluginUiStyle.FocusRingColor(P).W);
        Assert.Equal(P.Field.W * 0.45f, PluginUiStyle.Faded(P.Field).W, 5);
        Assert.Equal(P.Field.W, PluginUiStyle.Hover(P, P.Field).W);
    }

    [Theory]
    [InlineData(UiControlState.Normal)]
    [InlineData(UiControlState.Hovered)]
    [InlineData(UiControlState.Pressed)]
    public void AButtonDrawsItsStateColour(UiControlState state)
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        PluginUiStyle.Button(ctx, P, 80f, 24f, P.Field, P.Border, state);
        Vector4 expected = state switch
        {
            UiControlState.Hovered => PluginUiStyle.Hover(P, P.Field),
            UiControlState.Pressed => PluginUiStyle.Pressed(P.Field),
            _ => P.Field,
        };
        Assert.True(ThemeDrawCapture.HasColor(ThemeDrawCapture.Vertices(renderer), expected));
    }

    [Fact]
    public void ADisabledButtonIsFaded()
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        PluginUiStyle.Button(ctx, P, 80f, 24f, P.Field, P.Border, UiControlState.Disabled);
        Assert.All(ThemeDrawCapture.Vertices(renderer), v => Assert.True(v.Color.W <= 0.45f + 1e-4f));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheSwitchKnobSitsOnTheSideOfItsValue(bool on)
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        PluginUiStyle.Switch(ctx, P, 0f, 0f, on, enabled: true);
        Vector4 knob = on ? P.Text : P.Muted;
        var knobVerts = ThemeDrawCapture.Vertices(renderer)
            .Where(v => v.Color.W > 0.99f && Vector4.Distance(v.Color, knob) < 0.01f).ToList();
        Assert.NotEmpty(knobVerts);
        float centre = knobVerts.Average(v => v.Position.X);
        if (on) Assert.True(centre > PluginUiStyle.SwitchWidth / 2f);
        else Assert.True(centre < PluginUiStyle.SwitchWidth / 2f);
    }

    [Fact]
    public void PointerStateFollowsHoverAndPress()
    {
        var s = new UiPointerState();
        Assert.Equal(UiControlState.Normal, s.State(enabled: true));
        s.Observe(new UiEvent { Type = UiEventType.HoverEnter });
        Assert.Equal(UiControlState.Hovered, s.State(true));
        s.Observe(new UiEvent { Type = UiEventType.MouseDown });
        Assert.Equal(UiControlState.Pressed, s.State(true));
        s.Observe(new UiEvent { Type = UiEventType.HoverLeave });
        Assert.Equal(UiControlState.Normal, s.State(true));
        s.Observe(new UiEvent { Type = UiEventType.HoverEnter });
        Assert.Equal(UiControlState.Pressed, s.State(true));
        s.Observe(new UiEvent { Type = UiEventType.MouseUp });
        Assert.Equal(UiControlState.Hovered, s.State(true));
        Assert.Equal(UiControlState.Disabled, s.State(false));
    }

    [Fact]
    public void ThePaletteCheckDrawsTheModernCheck()
    {
        var (a, ctxA) = ThemeDrawCapture.Context();
        var (b, ctxB) = ThemeDrawCapture.Context();
        P.DrawCheck(ctxA, 3f, 4f, true);
        PluginUiStyle.Check(ctxB, P, 3f, 4f, true);
        Assert.Equal(ThemeDrawCapture.Vertices(b), ThemeDrawCapture.Vertices(a));
    }
}
