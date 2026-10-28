using System.Numerics;

namespace AcDream.App.UI.Hud;

/// <summary>The Chat Deck's colours: the Command Deck's frame and studs, plus tabs, badges and fields.</summary>
internal static class ChatDeckPalette
{
    internal static Vector4 Rgb(int hex, float a = 1f) =>
        new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, a);

    internal static readonly Vector4 FrameTop = Rgb(0x2B2A31);
    internal static readonly Vector4 FrameBottom = Rgb(0x17161B);
    internal static readonly Vector4 FrameEdge = Rgb(0x0B0A0D);
    internal static readonly Vector4 Bronze = Rgb(0x6B5A3A, 0.7f);
    internal static readonly Vector4 TextInk = Rgb(0xFFFFFF);
    internal static readonly Vector4 LabelInk = Rgb(0xC9BFA6);
    internal static readonly Vector4 StudOff = Rgb(0x34323B);
    internal static readonly Vector4 StudOffInk = Rgb(0xA8A3B5);
    internal static readonly Vector4 StudOn = Rgb(0xD14A5C);
    internal static readonly Vector4 TabSelected = Rgb(0x3A3842);
    internal static readonly Vector4 TabUnderline = Rgb(0xE2C27A);
    internal static readonly Vector4 BadgeGold = Rgb(0xE2C27A);
    internal static readonly Vector4 BadgeRed = Rgb(0xD14A5C);
    internal static readonly Vector4 BadgeInk = Rgb(0x17161B);
    internal static readonly Vector4 FieldFill = Rgb(0x0C0B0F, 0.9f);
    internal static readonly Vector4 TagInk = Rgb(0x8FD3FF);
    internal static readonly Vector4 TimestampInk = Rgb(0x8C8698);
    internal static readonly Vector4 MarkerFill = Rgb(0x3A3842, 0.95f);

    /// <summary>The deck's popup menus (channel chip, tab right-click): dark face, bronze edge, gold accent.</summary>
    internal static readonly PluginUiPalette Menu = new(
        Background: FrameBottom, Field: FrameTop, Border: Bronze with { W = 1f }, Text: TextInk,
        Muted: StudOffInk, Accent: TabUnderline, Selected: TabSelected);

    /// <summary>The settings popup's window: the deck's frame colours, bronze edge, gold accent, dark fields.</summary>
    internal static readonly PluginUiPalette Window = new(
        Background: FrameBottom, Field: FieldFill with { W = 1f }, Border: Bronze with { W = 1f }, Text: TextInk,
        Muted: StudOffInk, Accent: TabUnderline, Selected: TabSelected);

    /// <summary>The dark rounded frame, its fill at <paramref name="opacity"/> (the edge and rule stay solid).</summary>
    internal static void DrawFrame(UiRenderContext ctx, DeckRect f, float opacity)
    {
        ctx.FillRoundedRect(f.X, f.Y, f.W, f.H, 8f, FrameEdge with { W = opacity });
        ctx.FillVerticalGradient(f.X + 1f, f.Y + 1f, f.W - 2f, f.H - 2f, new CanvasCornerRadii(7f, 7f, 7f, 7f),
            FrameTop with { W = opacity }, FrameBottom with { W = opacity });
        ctx.StrokeRoundedRect(f.X + 3f, f.Y + 3f, f.W - 6f, f.H - 6f, 5f, Bronze, 1f);
    }
}
