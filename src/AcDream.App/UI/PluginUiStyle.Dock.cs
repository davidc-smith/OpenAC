using System.Numerics;
using System.Text;

namespace AcDream.App.UI;

/// <summary>Which side of the screen a dock piece faces.</summary>
internal enum DockSide { Left, Right }

/// <summary>Which way a dock chevron points.</summary>
internal enum ChevronDirection { Up, Down, Left, Right }

/// <summary>
/// The plugin dock's shapes and sizes, in interface points. All three themes
/// draw the dock through here; Classic passes <see cref="PluginUiPalette.ClassicDock"/>.
/// </summary>
internal static partial class PluginUiStyle
{
    internal const float DockPadding = 6f;
    internal const float DockSlot = 36f;
    internal const float DockGap = 4f;
    internal const float DockArt = 24f;
    internal const float DockRadius = 14f;
    internal const float DockWidth = DockPadding + DockSlot + DockPadding;
    internal const float DockHandleTop = 2f;
    internal const float DockHandleHeight = 12f;
    internal const float DockSlotsTop = DockHandleTop + DockHandleHeight + 2f;
    internal const float DockHandleWidth = 30f;
    internal const float DockCollapseSize = 12f;
    internal const float DockDividerSpace = 4f;
    internal const float DockDividerInset = 8f;
    /// <summary>How far a divider's colour moves from Border toward the dock's fill, so it reads as a faint hairline.</summary>
    internal const float DockDividerFade = 0.6f;
    internal const float DockFadeHeight = 12f;
    internal const float DockSlotRadius = 9f;
    internal const float DockOpenDot = 3f;
    internal const float DockOpenDotInset = 1.5f;
    internal const float DockCollapsedHeight = 24f;
    internal const float RailWidth = 46f;
    internal const float RailRadius = 12f;
    internal const float RailSlot = 38f;
    internal const float RailGap = 2f;
    internal const float RailPillWidth = 3f;
    internal const float RailPillHover = 4f;
    internal const float RailPillOpen = 8f;
    internal const float RailPillFront = 20f;
    internal const float RailPillSeconds = 0.12f;
    internal const float RailTabWidth = 12f;
    internal const float RailTabHeight = 44f;
    internal const float LabelGap = 8f;
    internal const float LabelPadX = 9f;
    internal const float LabelPadY = 5f;
    internal const float LabelRadius = 7f;
    internal const float MonogramRadius = 6f;
    internal const float ClosedArtAlpha = 0.85f;

    private static readonly Vector4 MonogramInk = Rgb(0xF1ECE2);

    private static readonly Vector4[] MonogramHues =
    [
        Rgb(0x6E8B5E), Rgb(0x5E7F8B), Rgb(0x8B6E5E), Rgb(0x7A6A99),
        Rgb(0x8B8259), Rgb(0x5E6E8B), Rgb(0x8B5E74), Rgb(0x5E8B7A),
    ];

    private static Vector4 Rgb(uint rgb) =>
        new((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, 1f);

    /// <summary>The dock's rounded corners: all four when floating, only the inner two on a rail.</summary>
    internal static CanvasCornerRadii DockCorners(PluginDockMode mode) => mode switch
    {
        PluginDockMode.Left => new(0f, RailRadius, RailRadius, 0f),
        PluginDockMode.Right => new(RailRadius, 0f, 0f, RailRadius),
        _ => new(DockRadius, DockRadius, DockRadius, DockRadius),
    };

    /// <summary>The dock's body: shadow, fill, a faint top sheen and a rim (left off the screen-edge side of a rail).</summary>
    internal static void DockBody(UiRenderContext ctx, PluginUiPalette p, PluginDockMode mode, float w, float h)
    {
        bool rail = mode != PluginDockMode.Floating;
        float radius = rail ? RailRadius : DockRadius;
        ctx.DrawSoftShadow(0f, 0f, w, h, radius, rail ? 2f : 4f, 14f, Black with { W = 0.42f });
        CanvasCornerRadii corners = DockCorners(mode);
        ctx.FillRoundedRect(0f, 0f, w, h, corners, p.Background);
        Vector4 sheen = p.Text with { W = 0.04f };
        ctx.FillVerticalGradient(0f, 0f, w, MathF.Min(12f, h),
            corners with { BottomLeft = 0f, BottomRight = 0f }, sheen, sheen with { W = 0f });
        Vector4 rim = Mix(p.Border, p.Text, 0.08f);
        // A rail's rim runs off the element on its edge side, where the dock's clip cuts it away.
        float x = mode == PluginDockMode.Left ? -radius : 0f;
        float rimWidth = rail ? w + radius : w;
        ctx.StrokeRoundedRect(x + 0.5f, 0.5f, rimWidth - 1f, h - 1f, radius - 0.5f, rim, 1f);
    }

    /// <summary>The well behind a slot while it is hovered or pressed; nothing when idle.</summary>
    internal static void DockSlotWell(UiRenderContext ctx, PluginUiPalette p, float x, float y, float w, float h, UiControlState state)
    {
        if (state == UiControlState.Hovered)
            ctx.FillRoundedRect(x, y, w, h, DockSlotRadius, Hover(p, p.Field));
        else if (state == UiControlState.Pressed)
            ctx.FillRoundedRect(x, y, w, h, DockSlotRadius, Pressed(Hover(p, p.Field)));
    }

    /// <summary>The small accent dot beside an open window's slot, on the side nearer the screen edge.</summary>
    internal static void OpenDot(UiRenderContext ctx, PluginUiPalette p, DockSide side, float dockWidth, float slotTop, float slotHeight)
    {
        float x = side == DockSide.Left ? DockOpenDotInset : dockWidth - DockOpenDotInset - DockOpenDot;
        ctx.FillEllipse(x, slotTop + (slotHeight - DockOpenDot) / 2f, DockOpenDot, DockOpenDot, p.Accent);
    }

    /// <summary>The pill on a rail's screen edge beside a slot, <paramref name="height"/> tall.</summary>
    internal static void EdgePill(UiRenderContext ctx, Vector4 color, DockSide side, float dockWidth, float slotTop, float slotHeight, float height)
    {
        if (!(height > 0f)) return;
        float r = RailPillWidth;
        float x = side == DockSide.Left ? 0f : dockWidth - RailPillWidth;
        CanvasCornerRadii corners = side == DockSide.Left ? new(0f, r, r, 0f) : new(r, 0f, 0f, r);
        ctx.FillRoundedRect(x, slotTop + (slotHeight - height) / 2f, RailPillWidth, height, corners, color);
    }

    /// <summary>A faint hairline between two plugins' slots, at <paramref name="y"/>: the border colour faded toward the fill.</summary>
    internal static void DockDivider(UiRenderContext ctx, PluginUiPalette p, float dockWidth, float y) =>
        ctx.DrawFill(DockDividerInset, y, dockWidth - 2f * DockDividerInset, 1f, Mix(p.Border, p.Background, DockDividerFade));

    /// <summary>A fade into the dock's fill, marking the end where slots are cut off.</summary>
    internal static void DockFade(UiRenderContext ctx, PluginUiPalette p, float y, float w, bool darkAtBottom)
    {
        Vector4 solid = p.Background;
        Vector4 clear = solid with { W = 0f };
        ctx.FillVerticalGradient(0f, y, w, DockFadeHeight, default,
            darkAtBottom ? clear : solid, darkAtBottom ? solid : clear);
    }

    /// <summary>Three small dots, the dock's move handle, centred at (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
    internal static void HandleDots(UiRenderContext ctx, PluginUiPalette p, float cx, float cy)
    {
        for (int i = -1; i <= 1; i++)
            ctx.FillEllipse(cx + i * 5f - 1f, cy - 1f, 2f, 2f, p.Muted);
    }

    /// <summary>A two-stroke chevron centred at (<paramref name="cx"/>, <paramref name="cy"/>), <paramref name="size"/> across.</summary>
    internal static void Chevron(UiRenderContext ctx, float cx, float cy, float size, ChevronDirection direction, Vector4 color)
    {
        float a = size / 2f;
        float b = size / 4f;
        (Vector2 p0, Vector2 tip, Vector2 p1) = direction switch
        {
            ChevronDirection.Up => (new Vector2(-a, b), new Vector2(0f, -b), new Vector2(a, b)),
            ChevronDirection.Down => (new Vector2(-a, -b), new Vector2(0f, b), new Vector2(a, -b)),
            ChevronDirection.Left => (new Vector2(b, -a), new Vector2(-b, 0f), new Vector2(b, a)),
            _ => (new Vector2(-b, -a), new Vector2(b, 0f), new Vector2(-b, a)),
        };
        ctx.DrawSmoothLine(cx + p0.X, cy + p0.Y, cx + tip.X, cy + tip.Y, color, 1.5f);
        ctx.DrawSmoothLine(cx + tip.X, cy + tip.Y, cx + p1.X, cy + p1.Y, color, 1.5f);
    }

    /// <summary>The gear in the dock's appearance slot, filling a <paramref name="size"/> box at (x, y).</summary>
    internal static void Gear(UiRenderContext ctx, float x, float y, float size, Vector4 color)
    {
        float c = size / 2f;
        float ring = size * 0.34f;
        ctx.StrokeEllipse(x + c - ring, y + c - ring, ring * 2f, ring * 2f, color, 1.75f);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * MathF.PI / 4f;
            float dx = MathF.Cos(angle);
            float dy = MathF.Sin(angle);
            ctx.DrawSmoothLine(x + c + dx * ring, y + c + dy * ring,
                x + c + dx * size * 0.46f, y + c + dy * size * 0.46f, color, 2f);
        }
    }

    /// <summary>
    /// The colour behind a plugin's monogram: one of eight fixed hues, picked by
    /// FNV-1a of the plugin id, so a plugin keeps its colour between sessions.
    /// </summary>
    internal static Vector4 MonogramHue(string pluginId)
    {
        uint hash = 2166136261u;
        foreach (byte b in Encoding.UTF8.GetBytes(pluginId))
            hash = (hash ^ b) * 16777619u;
        return MonogramHues[hash % (uint)MonogramHues.Length];
    }

    /// <summary>A plugin's two-letter monogram filling the <paramref name="size"/> box at (x, y).</summary>
    internal static void Monogram(UiRenderContext ctx, UiDatFont? font, string letters, Vector4 hue,
        float x, float y, float size, float alpha)
    {
        Vector4 top = Mix(hue, Vector4.One, 0.12f);
        ctx.FillVerticalGradient(x, y, size, size,
            new CanvasCornerRadii(MonogramRadius, MonogramRadius, MonogramRadius, MonogramRadius),
            top with { W = alpha }, hue with { W = alpha });
        if (font is null || letters.Length == 0) return;
        float width = font.MeasureWidth(letters);
        ctx.DrawStringDat(font, letters, x + (size - width) / 2f, y + (size - font.LineHeight) / 2f,
            MonogramInk with { W = alpha });
    }

    /// <summary>The hover label's box: background, edge and shadow, at (x, y) in the dock's space.</summary>
    internal static void HoverLabel(UiRenderContext ctx, PluginUiPalette p, float x, float y, float w, float h)
    {
        ctx.DrawSoftShadow(x, y, w, h, LabelRadius, 3f, 8f, Black with { W = 0.35f });
        Surface(ctx, x, y, w, h, LabelRadius, p.Background, p.Border);
    }
}
