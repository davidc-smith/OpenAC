using System.Numerics;

namespace AcDream.App.UI.Hud;

/// <summary>
/// Measures, places and draws a Modern HUD tooltip in the deck's own look,
/// not the retail tooltip art. Measuring and placing are pure so they can be
/// tested without a renderer.
/// </summary>
internal static class HudTooltip
{
    /// <summary>The width a card's description wraps at.</summary>
    internal const float WrapWidth = 220f;

    /// <summary>Space between the tooltip and the control it points at.</summary>
    internal const float Gap = 8f;

    private const float ScreenMargin = 4f;
    private const float CardPadX = 11f, CardPadY = 8f, CardRadius = 8f, CardRowGap = 3f;
    private const float CompactPadX = 9f, CompactPadY = 5f, CompactRadius = 6f, CompactStateGap = 6f;
    private const float BadgeGap = 12f, BadgePadX = 5f;
    private const float PointerSize = 10f;

    private static Vector4 Rgb(int hex, float a = 1f) =>
        new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, a);

    private static readonly Vector4 CardTop = Rgb(0x2B2A31);
    private static readonly Vector4 CardBottom = Rgb(0x141318);
    private static readonly Vector4 CardEdge = Rgb(0x0B0A0D);
    private static readonly Vector4 Hairline = Rgb(0x6B5A3A, 0.6f);
    private static readonly Vector4 PillFill = Rgb(0x0C0B0F, 0.94f);
    private static readonly Vector4 PillEdge = Rgb(0x6B5A3A);
    private static readonly Vector4 TitleInk = Rgb(0xFFFFFF);
    private static readonly Vector4 StateInk = Rgb(0x7FD6A8);
    private static readonly Vector4 BadgeFill = Rgb(0x7FD6A8, 0.16f);
    private static readonly Vector4 DetailInk = Rgb(0xB9B3C6);
    private static readonly Vector4 HintInk = Rgb(0xC9BFA6);

    /// <summary>The tooltip's size and, for a card, its description wrapped at <see cref="WrapWidth"/>.</summary>
    internal static (float W, float H, IReadOnlyList<string> DetailLines) Measure(
        HudTooltipContent c, HudTooltipStyle s, Func<string, float> measure, float lineHeight)
    {
        if (s == HudTooltipStyle.Compact)
        {
            float line = measure(c.Title) + (c.State is null ? 0f : CompactStateGap + measure(c.State));
            return (line + 2 * CompactPadX, lineHeight + 2 * CompactPadY, []);
        }

        IReadOnlyList<string> lines = c.Detail is null ? [] : Wrap(c.Detail, measure, WrapWidth);
        float width = measure(c.Title) + (c.State is null ? 0f : BadgeGap + measure(c.State) + 2 * BadgePadX);
        foreach (string l in lines) width = MathF.Max(width, measure(l));
        if (c.Hint is not null) width = MathF.Max(width, measure(c.Hint));

        float height = lineHeight;
        if (lines.Count > 0) height += CardRowGap + lines.Count * lineHeight;
        if (c.Hint is not null) height += CardRowGap + lineHeight;
        return (width + 2 * CardPadX, height + 2 * CardPadY, lines);
    }

    /// <summary>Greedy word wrap; a word wider than the limit keeps a line to itself.</summary>
    internal static IReadOnlyList<string> Wrap(string text, Func<string, float> measure, float maxWidth)
    {
        var lines = new List<string>();
        string current = "";
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && measure(candidate) > maxWidth)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    /// <summary>
    /// Where a <paramref name="w"/> × <paramref name="h"/> tooltip goes, in the
    /// element's local coordinates: centred <see cref="Gap"/> above the anchor,
    /// or below it when above would leave the screen; kept on screen sideways
    /// when the screen width is known.
    /// </summary>
    internal static (float X, float Y, bool Below) Place(
        DeckRect anchor, float w, float h, float screenLeft, float screenTop, float? screenWidth)
    {
        float x = anchor.X + anchor.W / 2f - w / 2f;
        float y = anchor.Y - Gap - h;
        bool below = screenTop + y < 0f;
        if (below) y = anchor.Y + anchor.H + Gap;

        if (screenWidth is float sw)
        {
            float screenX = screenLeft + x;
            screenX = MathF.Min(screenX, sw - ScreenMargin - w);
            screenX = MathF.Max(screenX, ScreenMargin);
            x = screenX - screenLeft;
        }
        return (x, y, below);
    }

    /// <summary>Draws the tooltip pointing at <paramref name="anchor"/> (local coordinates).</summary>
    internal static void Draw(
        UiRenderContext ctx, HudTooltipContent c, HudTooltipStyle s, DeckRect anchor,
        Vector2 screenPosition, float? screenWidth)
    {
        Func<string, float> measure = text => ctx.DefaultFont?.MeasureWidth(text) ?? 0f;
        float lh = ctx.DefaultFont?.LineHeight ?? 14f;
        var (w, h, lines) = Measure(c, s, measure, lh);
        var (x, y, below) = Place(anchor, w, h, screenPosition.X, screenPosition.Y, screenWidth);
        bool card = s == HudTooltipStyle.Card;
        float radius = card ? CardRadius : CompactRadius;

        // The pointer: a square turned on its corner, half under the body,
        // aimed at the anchor's centre but kept clear of the rounded corners.
        float half = PointerSize / 2f;
        float inset = radius + half + 2f;
        float px = w > 2f * inset
            ? Math.Clamp(anchor.X + anchor.W / 2f, x + inset, x + w - inset)
            : x + w / 2f;
        float py = below ? y : y + h;
        Vector4 edge = card ? CardEdge : PillEdge;
        Vector4 body = card ? (below ? CardTop : CardBottom) : PillFill;

        Diamond(ctx, px, py, PointerSize + 2f, edge);
        if (card)
        {
            ctx.FillRoundedRect(x - 1f, y - 1f, w + 2f, h + 2f, radius + 1f, edge);
            Diamond(ctx, px, py, PointerSize, body);
            ctx.FillVerticalGradient(x, y, w, h, new CanvasCornerRadii(radius, radius, radius, radius), CardTop, CardBottom);
            ctx.StrokeRoundedRect(x + 2f, y + 2f, w - 4f, h - 4f, radius - 2f, Hairline, 1f);
            DrawCardText(ctx, c, lines, x, y, w, lh, measure);
        }
        else
        {
            Diamond(ctx, px, py, PointerSize, body);
            ctx.FillRoundedRect(x, y, w, h, radius, PillFill);
            ctx.StrokeRoundedRect(x + 0.5f, y + 0.5f, w - 1f, h - 1f, radius, edge, 1f);
            float tx = x + CompactPadX, ty = y + CompactPadY;
            ctx.DrawString(c.Title, tx, ty, TitleInk);
            if (c.State is not null)
                ctx.DrawString(c.State, tx + measure(c.Title) + CompactStateGap, ty, StateInk);
        }
    }

    private static void DrawCardText(
        UiRenderContext ctx, HudTooltipContent c, IReadOnlyList<string> lines,
        float x, float y, float w, float lh, Func<string, float> measure)
    {
        float tx = x + CardPadX, ty = y + CardPadY;
        // DefaultFont has no bold face: a second pass half a point over thickens the title.
        ctx.DrawString(c.Title, tx, ty, TitleInk);
        ctx.DrawString(c.Title, tx + 0.5f, ty, TitleInk);
        if (c.State is not null)
        {
            float bw = measure(c.State) + 2 * BadgePadX;
            float bx = x + w - CardPadX - bw;
            ctx.FillRoundedRect(bx, ty - 1f, bw, lh + 2f, (lh + 2f) / 2f, BadgeFill);
            ctx.DrawString(c.State, bx + BadgePadX, ty, StateInk);
        }
        ty += lh;
        if (lines.Count > 0)
        {
            ty += CardRowGap;
            foreach (string line in lines)
            {
                ctx.DrawString(line, tx, ty, DetailInk);
                ty += lh;
            }
        }
        if (c.Hint is not null)
            ctx.DrawString(c.Hint, tx, ty + CardRowGap, HintInk);
    }

    /// <summary>A <paramref name="size"/>-point square rotated 45° about (cx, cy).</summary>
    private static void Diamond(UiRenderContext ctx, float cx, float cy, float size, Vector4 color)
    {
        float d = size / (2f * MathF.Sqrt(2f));
        ctx.DrawSmoothLine(cx - d, cy - d, cx + d, cy + d, color, size);
    }
}
