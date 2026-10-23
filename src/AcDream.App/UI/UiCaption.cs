using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// The one-line caption markup toggles, checkboxes and radio buttons share.
/// A dat font is centred on the row's height (outlined in Classic); the
/// bitmap fallback sits one pixel below the row's top.
/// </summary>
internal static class UiCaption
{
    public static void Draw(
        UiRenderContext ctx, UiDatFont? font, string text, float x, float top, float height,
        Vector4 color, bool outline)
    {
        if (font is { } dat)
            ctx.DrawStringDat(dat, text, x, top + (height - dat.LineHeight) * 0.5f, color, outline: outline);
        else
            ctx.DrawString(text, x, top + 1f, color);
    }

    /// <summary>The caption centred in a box, measured the way a markup button measures its own.</summary>
    public static void DrawCentred(
        UiRenderContext ctx, UiDatFont? font, string text, float x, float top, float width, float height,
        Vector4 color, bool outline)
    {
        float textWidth = font?.MeasureWidth(text) ?? ctx.DefaultFont?.MeasureWidth(text) ?? text.Length * 7f;
        float lineHeight = font?.LineHeight ?? ctx.DefaultFont?.LineHeight ?? 14f;
        float tx = x + (width - textWidth) * 0.5f;
        float ty = top + (height - lineHeight) * 0.5f;
        if (font is { } dat)
            ctx.DrawStringDat(dat, text, tx, ty, color, outline: outline);
        else
            ctx.DrawString(text, tx, ty, color);
    }
}
