using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// The markup radio button's 11 px round button: the lamp's octagon as the
/// ring, a dark well inside it, and a 3 px mark when chosen. Classic borrows
/// the lamp's colours, as the checkbox does.
/// </summary>
internal static class UiRadioGlyph
{
    public const float Size = 11f;

    public static readonly Vector4 ClassicRing = UiCheckLamp.UncheckedInner;
    public static readonly Vector4 ClassicWell = UiCheckBoxGlyph.ClassicFill;
    public static readonly Vector4 ClassicMark = UiCheckLamp.CheckedInner;

    public static void Draw(
        UiRenderContext ctx, float x, float y, bool selected, PluginUiPalette? palette, float alpha = 1f)
    {
        Vector4 ring = Alpha(palette is { } p ? (selected ? p.Accent : p.Border) : ClassicRing, alpha);
        Vector4 well = Alpha(palette?.Field ?? ClassicWell, alpha);
        Vector4 mark = Alpha(palette?.Accent ?? ClassicMark, alpha);

        if (palette is not null)
        {
            ctx.FillEllipse(x, y, Size, Size, ring);
            ctx.FillEllipse(x + 1f, y + 1f, Size - 2f, Size - 2f, well);
            if (selected)
                ctx.FillEllipse(x + 3f, y + 3f, Size - 6f, Size - 6f, mark);
            return;
        }

        ctx.DrawFill(x + 3f, y, 5f, 1f, ring);
        ctx.DrawFill(x + 1f, y + 1f, 9f, 2f, ring);
        ctx.DrawFill(x, y + 3f, 11f, 5f, ring);
        ctx.DrawFill(x + 1f, y + 8f, 9f, 2f, ring);
        ctx.DrawFill(x + 3f, y + 10f, 5f, 1f, ring);
        ctx.DrawFill(x + 3f, y + 2f, 5f, 1f, well);
        ctx.DrawFill(x + 2f, y + 3f, 7f, 5f, well);
        ctx.DrawFill(x + 3f, y + 8f, 5f, 1f, well);
        if (selected)
            ctx.DrawFill(x + 4f, y + 4f, 3f, 3f, mark);
    }

    private static Vector4 Alpha(Vector4 color, float alpha) => color with { W = color.W * alpha };
}
