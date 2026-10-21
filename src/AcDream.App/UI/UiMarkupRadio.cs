using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// A markup radio group: one choice out of several, drawn as round buttons
/// with captions or as a joined segmented bar. Its box is split equally into
/// one cell per option along its orientation. It reports the option a click
/// or arrow key picks; the plugin stores it and the group draws whatever
/// <see cref="SelectedSource"/> returns.
/// </summary>
public sealed class UiMarkupRadio : UiElement
{
    public static readonly Vector4 ClassicSegmentFace = new(0.16f, 0.14f, 0.10f, 1f);
    public static readonly Vector4 ClassicSegmentSelected = new(0.32f, 0.27f, 0.15f, 1f);
    public static readonly Vector4 ClassicSegmentBorder = UiCheckLamp.UncheckedInner;
    public static readonly Vector4 FocusColor = new(1f, 0.82f, 0.25f, 1f);

    public PluginUiPalette? ThemePalette { get; set; }
    public Func<IReadOnlyList<string>>? ItemsSource { get; set; }

    /// <summary>The chosen option; −1 or out of range is none.</summary>
    public Func<int>? SelectedSource { get; set; }

    /// <summary>Called with the option a click or arrow key picks.</summary>
    public Action<int>? Changed { get; set; }

    public bool Horizontal { get; set; }
    public bool Segmented { get; set; }
    public UiDatFont? DatFont { get; set; }
    public Vector4 TextColor { get; set; } = new(0.86f, 0.84f, 0.74f, 1f);

    /// <summary>Re-read every frame in place of <see cref="TextColor"/>.</summary>
    public Func<Vector4>? TextColorSource { get; set; }

    public IReadOnlyList<string> Items => ItemsSource?.Invoke() ?? Array.Empty<string>();
    public int Selected => SelectedSource?.Invoke() ?? -1;

    public override bool HandlesClick => true;

    private readonly UiKeyboardActivation _keyboardActivation = new();
    private int _pressed = -1;

    /// <summary>The option under a point in local coordinates, or −1.</summary>
    public int IndexAt(float x, float y)
    {
        int count = Items.Count;
        if (count == 0 || x < 0f || y < 0f || x >= Width || y >= Height)
            return -1;
        float along = Horizontal ? x : y;
        float cell = (Horizontal ? Width : Height) / count;
        return Math.Min(count - 1, (int)(along / cell));
    }

    private void Select(int index)
    {
        if (!Enabled || index < 0 || index >= Items.Count || index == Selected)
            return;
        Changed?.Invoke(index);
    }

    /// <summary>One option along, wrapping; from no selection, the first or last.</summary>
    private void Move(int delta)
    {
        int count = Items.Count;
        if (count == 0)
            return;
        int current = Selected;
        int next = current >= 0 && current < count
            ? (current + delta + count) % count
            : delta > 0 ? 0 : count - 1;
        Select(next);
    }

    public override bool OnEvent(in UiEvent e)
    {
        if (_keyboardActivation.HandleEvent(in e, TabStop, Enabled, activate: null))
            return true;
        switch (e.Type)
        {
            case UiEventType.MouseDown:
                _pressed = Enabled ? IndexAt(e.Data1, e.Data2) : -1;
                return true;
            case UiEventType.MouseUp:
                if (_pressed >= 0 && IndexAt(e.Data1, e.Data2) == _pressed)
                    Select(_pressed);
                _pressed = -1;
                return true;
            case UiEventType.CaptureChanged:
                _pressed = -1;
                return true;
            case UiEventType.KeyDown when _keyboardActivation.Focused && Enabled:
                switch ((Silk.NET.Input.Key)e.Data0)
                {
                    case Silk.NET.Input.Key.Up or Silk.NET.Input.Key.Left:
                        Move(-1);
                        return true;
                    case Silk.NET.Input.Key.Down or Silk.NET.Input.Key.Right:
                        Move(1);
                        return true;
                }
                return false;
        }
        return false;
    }

    private float Alpha => Enabled ? 1f : UiMarkupToggle.DisabledAlpha;

    private Vector4 Fade(Vector4 color) => color with { W = color.W * Alpha };

    private (float X, float Y, float W, float H) Cell(int index, int count)
    {
        if (Horizontal)
        {
            float w = Width / count;
            return (index * w, 0f, w, Height);
        }
        float h = Height / count;
        return (0f, index * h, Width, h);
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        IReadOnlyList<string> items = Items;
        int selected = Selected;
        Vector4 text = Fade(TextColorSource?.Invoke() ?? TextColor);
        bool outline = ThemePalette is null;
        if (Segmented)
            DrawSegments(ctx, items, selected, text, outline);
        else
            DrawButtons(ctx, items, selected, text, outline);
        if (_keyboardActivation.Focused)
            ctx.DrawRectOutline(0f, 0f, Width, Height, FocusColor, 1f);
    }

    private void DrawButtons(
        UiRenderContext ctx, IReadOnlyList<string> items, int selected, Vector4 text, bool outline)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var (x, y, w, h) = Cell(i, items.Count);
            ctx.PushClip(x, y, w, h);
            try
            {
                float glyphY = y + MathF.Max(1f, (h - UiRadioGlyph.Size) * 0.5f);
                UiRadioGlyph.Draw(ctx, x + 1f, glyphY, i == selected, ThemePalette, Alpha);
                UiCaption.Draw(ctx, DatFont, items[i] ?? string.Empty, x + 17f, y, h, text, outline);
            }
            finally
            {
                ctx.PopClip();
            }
        }
    }

    private void DrawSegments(
        UiRenderContext ctx, IReadOnlyList<string> items, int selected, Vector4 text, bool outline)
    {
        Vector4 face = Fade(ThemePalette?.Field ?? ClassicSegmentFace);
        Vector4 chosen = Fade(ThemePalette?.Selected ?? ClassicSegmentSelected);
        Vector4 border = Fade(ThemePalette?.Border ?? ClassicSegmentBorder);
        for (int i = 0; i < items.Count; i++)
        {
            var (x, y, w, h) = Cell(i, items.Count);
            ctx.DrawFill(x, y, w, h, i == selected ? chosen : face);
            if (i > 0)
            {
                if (Horizontal) ctx.DrawFill(x, y, 1f, h, border);
                else ctx.DrawFill(x, y, w, 1f, border);
            }
            if (i > 0 && Horizontal) ctx.PushClip(x + 1f, y, w - 1f, h);
            else if (i > 0) ctx.PushClip(x, y + 1f, w, h - 1f);
            else ctx.PushClip(x, y, w, h);
            try
            {
                UiCaption.DrawCentred(ctx, DatFont, items[i] ?? string.Empty, x, y, w, h, text, outline);
            }
            finally
            {
                ctx.PopClip();
            }
        }
        ctx.DrawRectOutline(0f, 0f, Width, Height, border, 1f);
    }
}
