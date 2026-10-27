using System.Numerics;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The Modern HUD decks' shared look: the palette, the frame and the stud
/// backing. The Command Deck and the Spell Deck draw through these so they
/// read as one piece.
/// </summary>
internal static class DeckChrome
{
    internal static Vector4 Rgb(int hex, float a = 1f) =>
        new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, a);

    internal static readonly Vector4 FrameTop = Rgb(0x2B2A31);
    internal static readonly Vector4 FrameBottom = Rgb(0x17161B);
    internal static readonly Vector4 FrameEdge = Rgb(0x0B0A0D);
    internal static readonly Vector4 Bronze = Rgb(0x6B5A3A, 0.7f);
    internal static readonly Vector4 Track = Rgb(0x0C0B0F);
    internal static readonly Vector4 TrailInk = Rgb(0xF3E2C0, 0.45f);
    internal static readonly Vector4 TextInk = Rgb(0xFFFFFF);
    internal static readonly Vector4 LabelInk = Rgb(0xC9BFA6);
    internal static readonly Vector4 StudOff = Rgb(0x34323B);
    internal static readonly Vector4 StudOffInk = Rgb(0xA8A3B5);
    internal static readonly Vector4 StudOn = Rgb(0xD14A5C);
    internal static readonly Vector4 CombatGlow = Rgb(0xC2233A, 0.55f);
    internal static readonly Vector4 PeaceGlow = Rgb(0x78C8A0, 0.35f);
    internal static readonly (Vector4 Top, Vector4 Bottom) HealthInk = (Rgb(0xFF6B7D), Rgb(0x7D0F1C));
    internal static readonly (Vector4 Top, Vector4 Bottom) StaminaInk = (Rgb(0xFFE27A), Rgb(0x8A6C10));
    internal static readonly (Vector4 Top, Vector4 Bottom) ManaInk = (Rgb(0x8FB0FF), Rgb(0x1A3590));
    internal static readonly Vector4 PowerInk = Rgb(0xFFF6D8);
    internal static readonly Vector4 Handle = Rgb(0xE2C27A);
    internal static readonly Vector4 JumpInk = Rgb(0x7FD6A8);
    internal static readonly Vector4 CastLit = Rgb(0xCFE0FF);
    internal static readonly Vector4 GripInk = Rgb(0xE2C27A, 0.55f);

    /// <summary>A line weight at a deck scale, in whole points, never under one.</summary>
    internal static float Line(float points, float scale) => MathF.Max(1f, MathF.Round(points * scale));

    /// <summary>The dark rounded frame with its gradient and inner bronze rule. With
    /// <paramref name="squareBottom"/> the bottom corners are square (an attached Spell Deck
    /// meeting the Command Deck on one seam).</summary>
    internal static void DrawFrame(UiRenderContext ctx, DeckRect f, float scale, bool squareBottom = false)
    {
        float P(float points) => points * scale;
        CanvasCornerRadii Radii(float r) => new(P(r), P(r), squareBottom ? 0f : P(r), squareBottom ? 0f : P(r));
        ctx.FillRoundedRect(f.X - P(2), f.Y - P(2), f.W + P(4), f.H + P(4), Radii(10), FrameEdge);
        ctx.FillVerticalGradient(f.X, f.Y, f.W, f.H, Radii(8), FrameTop, FrameBottom);
        ctx.StrokeRoundedRect(f.X + P(3), f.Y + P(3), f.W - P(6), f.H - P(6), Radii(5), Bronze, Line(1, scale));
    }

    /// <summary>A stud's backing (lit when <paramref name="on"/>). Returns the ink for its icon or letter.</summary>
    internal static Vector4 DrawStud(UiRenderContext ctx, DeckRect r, float scale, bool on, bool dim = false)
    {
        float P(float points) => points * scale;
        ctx.FillRoundedRect(r.X - P(1), r.Y - P(1), r.W + P(2), r.H + P(2), P(6), FrameEdge);
        ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, P(5), on ? StudOn : StudOff);
        Vector4 ink = on ? TextInk : StudOffInk;
        return dim ? ink with { W = 0.4f } : ink;
    }

    /// <summary>A faint bronze stroke on the right edge, or an L at the right
    /// corner, while that grip is hovered or being dragged.</summary>
    internal static void DrawResizeGrip(UiRenderContext ctx, ResizeEdges edges, DeckRect frame, float scale)
    {
        if ((edges & ResizeEdges.Right) == 0) return;
        DeckRect f = frame;
        float w = Line(2, scale), right = f.X + f.W - w, arm = 24f * scale;
        if ((edges & ResizeEdges.Top) != 0)
        {
            ctx.DrawFill(right, f.Y, w, arm, GripInk);
            ctx.DrawFill(right - arm + w, f.Y, arm, w, GripInk);
        }
        else if ((edges & ResizeEdges.Bottom) != 0)
        {
            ctx.DrawFill(right, f.Y + f.H - arm, w, arm, GripInk);
            ctx.DrawFill(right - arm + w, f.Y + f.H - w, arm, w, GripInk);
        }
        else
        {
            ctx.DrawFill(right, f.Y + 10f * scale, w, f.H - 20f * scale, GripInk);
        }
    }
}
