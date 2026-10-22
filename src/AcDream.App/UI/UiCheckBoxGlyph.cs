using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// The markup checkbox's 11px square: an empty box, a pixel tick, or a dash
/// for indeterminate. The retail client draws its checkboxes as lamps
/// (<see cref="UiCheckLamp"/>), so Classic borrows the lamp's colours for a
/// square box rather than a dat sprite.
/// </summary>
internal static class UiCheckBoxGlyph
{
    public const float Size = 11f;

    public static readonly Vector4 ClassicBorder = UiCheckLamp.UncheckedInner;
    public static readonly Vector4 ClassicFill = new(0.10f, 0.09f, 0.06f, 1f);
    public static readonly Vector4 ClassicMark = UiCheckLamp.CheckedInner;

    /// <summary>The tick's 2×2 steps, left arm down then right arm up, in box pixels.</summary>
    private static readonly (float X, float Y)[] Tick =
        [(2f, 5f), (3f, 6f), (4f, 7f), (5f, 6f), (6f, 5f), (7f, 4f), (8f, 3f)];

    public static void Draw(UiRenderContext ctx, float x, float y, bool? state, PluginUiPalette? palette)
    {
        Vector4 fill = palette?.Field ?? ClassicFill;
        Vector4 border = palette is { } p ? (state == false ? p.Border : p.Accent) : ClassicBorder;
        Vector4 mark = palette?.Accent ?? ClassicMark;

        ctx.DrawFill(x, y, Size, Size, fill);
        ctx.DrawRectOutline(x, y, Size, Size, border, 1f);
        if (state is null)
        {
            ctx.DrawFill(x + 3f, y + 4f, 5f, 3f, mark);
        }
        else if (state == true)
        {
            foreach (var (dx, dy) in Tick)
                ctx.DrawFill(x + dx, y + dy, 2f, 2f, mark);
        }
    }
}
