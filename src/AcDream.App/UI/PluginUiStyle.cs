using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// How the shared plugin themes draw: every rounded shape, state colour and
/// size of the modern look lives here, so a themed control only says what
/// it is and which state it is in. Colours come from the palette, with
/// hover, pressed and focus colours worked out from it. Classic never comes
/// here.
/// <para>Everything is drawn inside the bounds given: an element clips to
/// its own rectangle, so a ring or glow outside it would be cut off.</para>
/// </summary>
internal static partial class PluginUiStyle
{
    internal const float WindowRadius = 10f;
    internal const float ControlRadius = 6f;
    internal const float SmallRadius = 4f;
    internal const float CloseGlyphSize = 8f;
    internal const float ContainerRadius = 8f;
    internal const float RowRadius = 5f;
    internal const float RowInset = 3f;
    internal const float HeaderHeight = 24f;
    internal const float SwitchWidth = 26f;
    internal const float SwitchHeight = 14f;
    internal const float SwitchKnob = 8f;
    internal const float SwitchCaptionGap = 8f;
    internal const float CheckSize = 11f;
    internal const float SliderTrackHeight = 4f;
    internal const float SliderThumbSize = 14f;
    internal const float ScrollThumbWidth = 4f;
    internal const float ScrollThumbActiveWidth = 6f;
    internal const float DisabledAlpha = 0.45f;

    private static readonly Vector4 Black = new(0f, 0f, 0f, 1f);

    /// <summary>Moves <paramref name="a"/>'s colour toward <paramref name="b"/>'s; keeps <paramref name="a"/>'s alpha.</summary>
    internal static Vector4 Mix(Vector4 a, Vector4 b, float t) => new(
        a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.W);

    internal static Vector4 Hover(PluginUiPalette p, Vector4 fill) => Mix(fill, p.Border, 0.30f);
    internal static Vector4 HoverEdge(PluginUiPalette p, Vector4 edge) => Mix(edge, p.Text, 0.15f);
    internal static Vector4 Pressed(Vector4 fill) => Mix(fill, Black, 0.25f);
    internal static Vector4 FocusGlow(PluginUiPalette p) => p.Accent with { W = 0.30f };
    internal static Vector4 FocusRingColor(PluginUiPalette p) => p.Accent with { W = 0.60f };
    internal static Vector4 Faded(Vector4 c) => c with { W = c.W * DisabledAlpha };

    /// <summary>A rounded fill with a one-pixel edge lying just inside it.</summary>
    internal static void Surface(
        UiRenderContext ctx, float x, float y, float w, float h, float radius, Vector4 fill, Vector4 edge)
    {
        if (!(w > 0f && h > 0f)) return;
        if (fill.W > 0f) ctx.FillRoundedRect(x, y, w, h, radius, fill);
        if (edge.W > 0f)
            ctx.StrokeRoundedRect(x + 0.5f, y + 0.5f, w - 1f, h - 1f, MathF.Max(0f, radius - 0.5f), edge, 1f);
    }

    /// <summary>A two-pixel ring one pixel inside a control's edge.</summary>
    internal static void FocusRing(UiRenderContext ctx, PluginUiPalette p, float w, float h, float radius) =>
        ctx.StrokeRoundedRect(2f, 2f, w - 4f, h - 4f, MathF.Max(0f, radius - 2f), FocusRingColor(p), 2f);

    internal static void Button(
        UiRenderContext ctx, PluginUiPalette p, float w, float h, Vector4 fill, Vector4 edge, UiControlState state)
    {
        (fill, edge) = state switch
        {
            UiControlState.Hovered => (Hover(p, fill), HoverEdge(p, edge)),
            UiControlState.Pressed => (Pressed(fill), edge),
            UiControlState.Disabled => (Faded(fill), Faded(edge)),
            _ => (fill, edge),
        };
        Surface(ctx, 0f, 0f, w, h, ControlRadius, fill, edge);
    }

    /// <summary>A button with no face until the pointer is over it.</summary>
    internal static void GhostButton(UiRenderContext ctx, PluginUiPalette p, float w, float h, UiControlState state)
    {
        if (state == UiControlState.Hovered)
            ctx.FillRoundedRect(0f, 0f, w, h, SmallRadius, Hover(p, p.Field));
        else if (state == UiControlState.Pressed)
            ctx.FillRoundedRect(0f, 0f, w, h, SmallRadius, Pressed(Hover(p, p.Field)));
    }

    /// <summary>
    /// A title bar's close button: a ghost face and an X, muted at rest and in
    /// the text colour while hovered or pressed, ringed while focused.
    /// </summary>
    internal static void CloseButton(
        UiRenderContext ctx, PluginUiPalette p, float w, float h, UiControlState state, bool focused)
    {
        GhostButton(ctx, p, w, h, state);
        Vector4 color = state is UiControlState.Hovered or UiControlState.Pressed ? p.Text : p.Muted;
        float x = (w - CloseGlyphSize) / 2f, y = (h - CloseGlyphSize) / 2f;
        ctx.DrawSmoothLine(x, y, x + CloseGlyphSize, y + CloseGlyphSize, color, 1.5f);
        ctx.DrawSmoothLine(x + CloseGlyphSize, y, x, y + CloseGlyphSize, color, 1.5f);
        if (focused) FocusRing(ctx, p, w, h, SmallRadius);
    }

    internal static void Tab(UiRenderContext ctx, PluginUiPalette p, float w, float h, bool selected, UiControlState state)
    {
        if (selected)
            ctx.FillRoundedRect(0f, 0f, w, h, ControlRadius, p.Selected);
        else if (state is UiControlState.Hovered or UiControlState.Pressed)
            ctx.FillRoundedRect(0f, 0f, w, h, ControlRadius, Hover(p, p.Field));
    }

    internal static void Field(UiRenderContext ctx, PluginUiPalette p, float w, float h, Vector4 fill, bool focused)
    {
        Surface(ctx, 0f, 0f, w, h, ControlRadius, fill, focused ? p.Accent : p.Border);
        if (focused)
            ctx.StrokeRoundedRect(2f, 2f, w - 4f, h - 4f, ControlRadius - 2f, FocusGlow(p), 2f);
    }

    /// <summary>A pill switch at (x, y), <see cref="SwitchWidth"/> by <see cref="SwitchHeight"/>.</summary>
    internal static void Switch(UiRenderContext ctx, PluginUiPalette p, float x, float y, bool on, bool enabled)
    {
        Vector4 track = on ? p.Accent with { W = 0.35f } : p.Field;
        Vector4 edge = on ? p.Accent : p.Border;
        Vector4 knob = on ? p.Text : p.Muted;
        if (!enabled) { track = Faded(track); edge = Faded(edge); knob = Faded(knob); }
        Surface(ctx, x, y, SwitchWidth, SwitchHeight, SwitchHeight / 2f, track, edge);
        float knobX = on ? x + SwitchWidth - 3f - SwitchKnob : x + 3f;
        ctx.FillEllipse(knobX, y + (SwitchHeight - SwitchKnob) / 2f, SwitchKnob, SwitchKnob, knob);
    }

    /// <summary>A rounded box with a tick, for check columns in lists.</summary>
    internal static void Check(UiRenderContext ctx, PluginUiPalette p, float x, float y, bool on)
    {
        Surface(ctx, x, y, CheckSize, CheckSize, SmallRadius, on ? p.Accent : p.Field, on ? p.Accent : p.Border);
        if (!on) return;
        ctx.DrawSmoothLine(x + 2.5f, y + 5.5f, x + 4.5f, y + 7.5f, p.Field, 1.5f);
        ctx.DrawSmoothLine(x + 4.5f, y + 7.5f, x + 8.5f, y + 3.5f, p.Field, 1.5f);
    }

    /// <summary>A slider's track, filled part and round thumb centred on <paramref name="thumbCentre"/>.</summary>
    internal static void Slider(
        UiRenderContext ctx, PluginUiPalette p, float w, float h, float thumbCentre, bool active)
    {
        float trackY = (h - SliderTrackHeight) / 2f;
        float r = SliderTrackHeight / 2f;
        ctx.FillRoundedRect(0f, trackY, w, SliderTrackHeight, r, p.Field);
        float filled = Math.Clamp(thumbCentre, 0f, w);
        if (filled > 0f) ctx.FillRoundedRect(0f, trackY, filled, SliderTrackHeight, r, p.Accent);
        float size = MathF.Min(SliderThumbSize, h);
        float tx = Math.Clamp(thumbCentre - size / 2f, 0f, MathF.Max(0f, w - size));
        float ty = (h - size) / 2f;
        ctx.FillEllipse(tx, ty + 1f, size, size, Black with { W = 0.35f });
        ctx.FillEllipse(tx, ty, size, size, p.Text);
        if (active) ctx.StrokeEllipse(tx + 1f, ty + 1f, size - 2f, size - 2f, p.Accent, 2f);
    }

    /// <summary>A slim rounded scroll thumb centred in a lane <paramref name="laneWidth"/> wide.</summary>
    internal static void ScrollThumb(
        UiRenderContext ctx, PluginUiPalette p, float x, float y, float laneWidth, float h, bool active)
    {
        float w = active ? ScrollThumbActiveWidth : ScrollThumbWidth;
        ctx.FillRoundedRect(x + (laneWidth - w) / 2f, y, w, h, w / 2f, active ? p.Text with { W = 0.7f } : p.Muted);
    }

    /// <summary>A slim rounded scroll thumb <paramref name="w"/> long, centred in a horizontal lane <paramref name="laneHeight"/> tall.</summary>
    internal static void ScrollThumbHorizontal(
        UiRenderContext ctx, PluginUiPalette p, float x, float y, float w, float laneHeight, bool active)
    {
        float h = active ? ScrollThumbActiveWidth : ScrollThumbWidth;
        ctx.FillRoundedRect(x, y + (laneHeight - h) / 2f, w, h, h / 2f, active ? p.Text with { W = 0.7f } : p.Muted);
    }

    /// <summary>A row highlight inset from the container's sides.</summary>
    internal static void Row(UiRenderContext ctx, float x, float y, float w, float h, Vector4 color) =>
        ctx.FillRoundedRect(x + RowInset, y + 1f, w - 2f * RowInset, h - 2f, RowRadius, color);

    /// <summary>A downward chevron, <paramref name="w"/> wide and <paramref name="h"/> tall.</summary>
    internal static void Chevron(UiRenderContext ctx, float x, float y, float w, float h, Vector4 color)
    {
        ctx.DrawSmoothLine(x, y, x + w / 2f, y + h, color, 1.5f);
        ctx.DrawSmoothLine(x + w / 2f, y + h, x + w, y, color, 1.5f);
    }

    internal static void Meter(
        UiRenderContext ctx, PluginUiPalette p, float w, float h, (float X, float Y, float W, float H) fill, Vector4 bar)
    {
        float r = MathF.Min(3f, h / 2f);
        ctx.FillRoundedRect(0f, 0f, w, h, r, p.Field);
        if (fill.W > 0f && fill.H > 0f) ctx.FillRoundedRect(fill.X, fill.Y, fill.W, fill.H, r, bar);
    }

    internal static void WindowShadow(UiRenderContext ctx, float w, float h) =>
        ctx.DrawSoftShadow(0f, 0f, w, h, WindowRadius, 4f, 12f, Black with { W = 0.45f });

    internal static void PopupShadow(UiRenderContext ctx, float x, float y, float w, float h) =>
        ctx.DrawSoftShadow(x, y, w, h, ContainerRadius, 3f, 8f, Black with { W = 0.35f });

    internal static void ShelfShadow(UiRenderContext ctx, float w, float h) =>
        ctx.DrawSoftShadow(0f, 0f, w, h, WindowRadius, 3f, 8f, Black with { W = 0.40f });

    /// <summary>The band behind a window's title, with a separator line under it.</summary>
    internal static void Header(UiRenderContext ctx, PluginUiPalette p, float w)
    {
        ctx.FillVerticalGradient(0f, 0f, w, HeaderHeight,
            new CanvasCornerRadii(WindowRadius, WindowRadius, 0f, 0f),
            Mix(p.Background, p.Text, 0.06f), Mix(p.Background, p.Text, 0.02f));
        ctx.DrawFill(0f, HeaderHeight, w, 1f, p.Border);
    }

    /// <summary>The resize grip: three dots in the bottom-right corner.</summary>
    internal static void ResizeGrip(UiRenderContext ctx, PluginUiPalette p, float w, float h)
    {
        for (int i = 0; i < 3; i++)
            ctx.FillEllipse(w - 8f - i * 4f, h - 6f, 2f, 2f, p.Muted);
    }
}
