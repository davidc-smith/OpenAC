using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class PluginDockStyleTests
{
    private static readonly PluginUiPalette Moss = PluginUiPalette.Moss;

    [Fact]
    public void FloatingBody_FillsInTheBackground_WithARimAndAShadowBelow()
    {
        var (renderer, ctx) = ThemeDrawCapture.Context(400, 400);
        ctx.PushTransform(100f, 100f);
        PluginUiStyle.DockBody(ctx, Moss, PluginDockMode.Floating, PluginUiStyle.DockWidth, 200f);
        var vertices = ThemeDrawCapture.Vertices(renderer);

        Assert.True(ThemeDrawCapture.HasColor(vertices, Moss.Background));
        Assert.True(ThemeDrawCapture.HasColor(vertices, PluginUiStyle.Mix(Moss.Border, Moss.Text, 0.08f)));
        Assert.Contains(vertices, v => v.Position.Y > 300f && v.Color.W > 0f);
    }

    [Fact]
    public void LeftRail_IsSquareOnItsEdgeSide()
    {
        Assert.Equal(new CanvasCornerRadii(0f, 12f, 12f, 0f), PluginUiStyle.DockCorners(PluginDockMode.Left));
        Assert.Equal(new CanvasCornerRadii(12f, 0f, 0f, 12f), PluginUiStyle.DockCorners(PluginDockMode.Right));
        Assert.Equal(new CanvasCornerRadii(14f, 14f, 14f, 14f), PluginUiStyle.DockCorners(PluginDockMode.Floating));
    }

    [Theory]
    [InlineData(false, 0f, 3f)]
    [InlineData(true, 43f, 46f)]
    public void EdgePill_SitsOnTheScreenEdge_CentredOnTheSlot(bool right, float minX, float maxX)
    {
        DockSide side = right ? DockSide.Right : DockSide.Left;
        var (renderer, ctx) = ThemeDrawCapture.Context(200, 200);
        PluginUiStyle.EdgePill(ctx, Moss.Accent, side, PluginUiStyle.RailWidth, 40f, 38f, PluginUiStyle.RailPillOpen);
        var ink = ThemeDrawCapture.Vertices(renderer).Where(v => v.Color.W > 0.5f).ToList();

        Assert.NotEmpty(ink);
        Assert.All(ink, v => Assert.InRange(v.Position.X, minX - 0.01f, maxX + 0.01f));
        // An 8pt pill centred on a slot from 40 to 78: 55 to 63, give or take the half-pixel soft edge.
        Assert.InRange(ink.Min(v => v.Position.Y), 54.5f, 55.5f);
        Assert.InRange(ink.Max(v => v.Position.Y), 62.5f, 63.5f);
    }

    [Fact]
    public void EdgePill_OfZeroHeight_DrawsNothing()
    {
        var (renderer, ctx) = ThemeDrawCapture.Context(200, 200);
        PluginUiStyle.EdgePill(ctx, Moss.Accent, DockSide.Left, 46f, 40f, 38f, 0f);
        Assert.Empty(ThemeDrawCapture.Vertices(renderer));
    }

    [Theory]
    [InlineData(false, 1.5f, 4.5f)]
    [InlineData(true, 43.5f, 46.5f)]
    public void OpenDot_SitsOnTheSideNearerTheScreenEdge(bool right, float minX, float maxX)
    {
        DockSide side = right ? DockSide.Right : DockSide.Left;
        var (renderer, ctx) = ThemeDrawCapture.Context(200, 200);
        PluginUiStyle.OpenDot(ctx, Moss, side, PluginUiStyle.DockWidth, 16f, 36f);
        var ink = ThemeDrawCapture.Vertices(renderer).Where(v => v.Color.W > 0.5f).ToList();

        Assert.True(ThemeDrawCapture.HasColor(ink, Moss.Accent));
        Assert.All(ink, v => Assert.InRange(v.Position.X, minX - 0.01f, maxX + 0.01f));
    }

    [Fact]
    public void SlotWell_IsEmptyWhenIdle_AndFilledOnHover()
    {
        var (idleRenderer, idle) = ThemeDrawCapture.Context(100, 100);
        PluginUiStyle.DockSlotWell(idle, Moss, 0f, 0f, 36f, 36f, UiControlState.Normal);
        Assert.Empty(ThemeDrawCapture.Vertices(idleRenderer));

        var (hoverRenderer, hover) = ThemeDrawCapture.Context(100, 100);
        PluginUiStyle.DockSlotWell(hover, Moss, 0f, 0f, 36f, 36f, UiControlState.Hovered);
        Assert.True(ThemeDrawCapture.HasColor(ThemeDrawCapture.Vertices(hoverRenderer), PluginUiStyle.Hover(Moss, Moss.Field)));
    }

    [Fact]
    public void MonogramHue_IsStableForAnId_AndOneOfTheEightFixedHues()
    {
        Vector4 first = PluginUiStyle.MonogramHue("acdream.mosstank");
        Assert.Equal(first, PluginUiStyle.MonogramHue("acdream.mosstank"));
        var hues = new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l" }
            .Select(PluginUiStyle.MonogramHue).Distinct().ToList();
        Assert.True(hues.Count > 1);
        Assert.True(hues.Count <= 8);
        // FNV-1a of "a" is 0xE40C292C, and 0xE40C292C % 8 = 4: the fifth hue, #8B8259.
        Assert.Equal(new Vector4(0x8B / 255f, 0x82 / 255f, 0x59 / 255f, 1f), PluginUiStyle.MonogramHue("a"));
    }

    [Fact]
    public void HoverLabel_DrawsItsBoxAndAShadow()
    {
        var (renderer, ctx) = ThemeDrawCapture.Context(300, 300);
        PluginUiStyle.HoverLabel(ctx, Moss, 60f, 20f, 90f, 36f);
        var vertices = ThemeDrawCapture.Vertices(renderer);
        Assert.True(ThemeDrawCapture.HasColor(vertices, Moss.Background));
        Assert.True(ThemeDrawCapture.HasColor(vertices, Moss.Border));
        Assert.Contains(vertices, v => v.Position.Y > 56f && v.Color.W > 0f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DockDivider_IsAOnePointHairline_FadedFromTheBorderTowardTheFill(bool classic)
    {
        PluginUiPalette p = classic ? PluginUiPalette.ClassicDock : Moss;
        Vector4 expected = PluginUiStyle.Mix(p.Border, p.Background, PluginUiStyle.DockDividerFade);
        var (renderer, ctx) = ThemeDrawCapture.Context(200, 200);
        PluginUiStyle.DockDivider(ctx, p, PluginUiStyle.DockWidth, 50f);
        var vertices = ThemeDrawCapture.Vertices(renderer);

        Assert.Equal(6, vertices.Count);
        Assert.All(vertices, v => Assert.Equal(expected, v.Color));
        Assert.NotEqual(p.Border, expected);
        Assert.Equal(1f, vertices.Max(v => v.Position.Y) - vertices.Min(v => v.Position.Y), 3);
    }
}
