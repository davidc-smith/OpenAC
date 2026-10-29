using System.Numerics;

namespace AcDream.App.UI.Hud;

/// <summary>
/// Measures, places and draws a Modern HUD tooltip in the deck's own look,
/// not the retail tooltip art. Measuring and placing are pure so they can be
/// tested without a renderer. Every size here is at deck scale 1; a deck drawn
/// at another scale passes it, and the pads, gaps, wrap width and faces follow.
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

    /// <summary>The tooltip's size and, for a card, its description wrapped at <see cref="WrapWidth"/>
    /// (sizes at <paramref name="scale"/>; <paramref name="measure"/> and <paramref name="lineHeight"/> are already at it).</summary>
    internal static (float W, float H, IReadOnlyList<string> DetailLines) Measure(
        HudTooltipContent c, HudTooltipStyle s, Func<string, float> measure, float lineHeight, float scale = 1f)
    {
        if (s == HudTooltipStyle.Compact)
        {
            float line = measure(c.Title) + (c.State is null ? 0f : CompactStateGap * scale + measure(c.State));
            return (line + 2 * CompactPadX * scale, lineHeight + 2 * CompactPadY * scale, []);
        }

        IReadOnlyList<string> lines = c.Detail is null ? [] : Wrap(c.Detail, measure, WrapWidth * scale);
        float width = measure(c.Title) + (c.State is null ? 0f : (BadgeGap + 2 * BadgePadX) * scale + measure(c.State));
        foreach (string l in lines) width = MathF.Max(width, measure(l));
        if (c.Hint is not null) width = MathF.Max(width, measure(c.Hint));

        float height = lineHeight;
        if (lines.Count > 0) height += CardRowGap * scale + lines.Count * lineHeight;
        if (c.Hint is not null) height += CardRowGap * scale + lineHeight;
        return (width + 2 * CardPadX * scale, height + 2 * CardPadY * scale, lines);
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
        DeckRect anchor, float w, float h, float screenLeft, float screenTop, float? screenWidth, float scale = 1f)
    {
        float gap = Gap * scale, margin = ScreenMargin * scale;
        float x = anchor.X + anchor.W / 2f - w / 2f;
        float y = anchor.Y - gap - h;
        bool below = screenTop + y < 0f;
        if (below) y = anchor.Y + anchor.H + gap;

        if (screenWidth is float sw)
        {
            float screenX = screenLeft + x;
            screenX = MathF.Min(screenX, sw - margin - w);
            screenX = MathF.Max(screenX, margin);
            x = screenX - screenLeft;
        }
        return (x, y, below);
    }

    /// <summary>Draws the tooltip pointing at <paramref name="anchor"/> (local coordinates), at deck scale
    /// <paramref name="scale"/>: the faces are the deck's at that scale, and every size follows it.</summary>
    internal static void Draw(
        UiRenderContext ctx, HudTooltipContent c, HudTooltipStyle s, DeckRect anchor,
        Vector2 screenPosition, float? screenWidth, HudTooltipLayoutCache? cache = null,
        DeckFonts? fonts = null, float scale = 1f)
    {
        DeckFontSet? set = fonts?.At(scale);
        var bodyFace = new DeckFace(set?.Text);
        // The title is measured in the body face: the bold face is barely wider.
        var titleFace = new DeckFace(set?.Bold);
        Func<string, float> measure = text => bodyFace.Measure(ctx, text);
        float lh = bodyFace.LineHeight(ctx);
        var (w, h, lines) = cache is null
            ? Measure(c, s, measure, lh, scale)
            : cache.Get(c, s, (object?)bodyFace.Font ?? ctx.DefaultFont, measure, lh, scale);
        var (x, y, below) = Place(anchor, w, h, screenPosition.X, screenPosition.Y, screenWidth, scale);
        bool card = s == HudTooltipStyle.Card;
        float radius = (card ? CardRadius : CompactRadius) * scale;
        float pointer = PointerSize * scale, line = MathF.Max(1f, MathF.Round(scale));

        // The pointer: a square turned on its corner, half under the body,
        // aimed at the anchor's centre but kept clear of the rounded corners.
        float half = pointer / 2f;
        float inset = radius + half + 2f * scale;
        float px = w > 2f * inset
            ? Math.Clamp(anchor.X + anchor.W / 2f, x + inset, x + w - inset)
            : x + w / 2f;
        float py = below ? y : y + h;
        Vector4 edge = card ? CardEdge : PillEdge;
        Vector4 body = card ? (below ? CardTop : CardBottom) : PillFill;

        Diamond(ctx, px, py, pointer + 2f * line, edge);
        if (card)
        {
            ctx.FillRoundedRect(x - line, y - line, w + 2f * line, h + 2f * line, radius + line, edge);
            Diamond(ctx, px, py, pointer, body);
            ctx.FillVerticalGradient(x, y, w, h, new CanvasCornerRadii(radius, radius, radius, radius), CardTop, CardBottom);
            float hair = 2f * scale;
            ctx.StrokeRoundedRect(x + hair, y + hair, w - 2f * hair, h - 2f * hair, radius - hair, Hairline, line);
            DrawCardText(ctx, c, lines, x, y, w, lh, measure, bodyFace, titleFace, scale);
        }
        else
        {
            Diamond(ctx, px, py, pointer, body);
            ctx.FillRoundedRect(x, y, w, h, radius, PillFill);
            ctx.StrokeRoundedRect(x + line / 2f, y + line / 2f, w - line, h - line, radius, edge, line);
            float tx = x + CompactPadX * scale, ty = y + CompactPadY * scale;
            bodyFace.Draw(ctx, c.Title, tx, ty, TitleInk);
            if (c.State is not null)
                bodyFace.Draw(ctx, c.State, tx + measure(c.Title) + CompactStateGap * scale, ty, StateInk);
        }
    }

    private static void DrawCardText(
        UiRenderContext ctx, HudTooltipContent c, IReadOnlyList<string> lines,
        float x, float y, float w, float lh, Func<string, float> measure, DeckFace body, DeckFace title, float scale)
    {
        float tx = x + CardPadX * scale, ty = y + CardPadY * scale, rowGap = CardRowGap * scale;
        title.Draw(ctx, c.Title, tx, ty, TitleInk);
        // DefaultFont has no bold face: a second pass half a point over thickens the title.
        if (title.Font is null) title.Draw(ctx, c.Title, tx + 0.5f * scale, ty, TitleInk);
        if (c.State is not null)
        {
            float padX = BadgePadX * scale, rise = scale;
            float bw = measure(c.State) + 2 * padX;
            float bx = x + w - CardPadX * scale - bw;
            ctx.FillRoundedRect(bx, ty - rise, bw, lh + 2f * rise, (lh + 2f * rise) / 2f, BadgeFill);
            body.Draw(ctx, c.State, bx + padX, ty, StateInk);
        }
        ty += lh;
        if (lines.Count > 0)
        {
            ty += rowGap;
            foreach (string line in lines)
            {
                body.Draw(ctx, line, tx, ty, DetailInk);
                ty += lh;
            }
        }
        if (c.Hint is not null)
            body.Draw(ctx, c.Hint, tx, ty + rowGap, HintInk);
    }

    /// <summary>A <paramref name="size"/>-point square rotated 45° about (cx, cy).</summary>
    private static void Diamond(UiRenderContext ctx, float cx, float cy, float size, Vector4 color)
    {
        float d = size / (2f * MathF.Sqrt(2f));
        ctx.DrawSmoothLine(cx - d, cy - d, cx + d, cy + d, color, size);
    }
}

/// <summary>
/// The last measured tooltip layout, so a tooltip that stays the same from
/// frame to frame is wrapped and measured once. Content is compared by value,
/// so a fresh but equal <see cref="HudTooltipContent"/> each frame still hits.
/// </summary>
internal sealed class HudTooltipLayoutCache
{
    private HudTooltipContent? _content;
    private HudTooltipStyle _style;
    private object? _font;
    private float _lineHeight;
    private float _scale;
    private (float W, float H, IReadOnlyList<string> DetailLines) _layout;

    /// <summary>The layout for this content and style; measured again only when either (or the font or scale) changes.</summary>
    internal (float W, float H, IReadOnlyList<string> DetailLines) Get(
        HudTooltipContent c, HudTooltipStyle s, object? font, Func<string, float> measure, float lineHeight, float scale = 1f)
    {
        if (_content is null || !_content.Equals(c) || _style != s
            || !ReferenceEquals(_font, font) || _lineHeight != lineHeight || _scale != scale)
        {
            _layout = HudTooltip.Measure(c, s, measure, lineHeight, scale);
            _content = c;
            _style = s;
            _font = font;
            _lineHeight = lineHeight;
            _scale = scale;
        }
        return _layout;
    }
}
