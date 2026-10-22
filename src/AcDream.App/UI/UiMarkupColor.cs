using System.Numerics;
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

/// <summary>
/// A markup colour picker: a swatch of the bound colour that opens a
/// <see cref="UiMarkupColorPopup"/> on click, or Enter/Space while focused.
/// The plugin owns the colour; the swatch draws whatever
/// <see cref="ValueSource"/> returns. Clicking doesn't take keyboard focus
/// from the game; the popup holds it while it is open.
/// </summary>
public sealed class UiMarkupColor : UiElement
{
    public const float CheckerCell = 4f;

    public static readonly Vector4 ClassicBorder = UiCheckLamp.UncheckedInner;
    public static readonly Vector4 FocusColor = UiMarkupHotkey.FocusColor;
    public static readonly Vector4 CheckerLight = new(0.8f, 0.8f, 0.8f, 1f);
    public static readonly Vector4 CheckerDark = new(0.5f, 0.5f, 0.5f, 1f);

    private readonly UiKeyboardActivation _keyboard = new();

    public UiMarkupColor()
    {
        AcceptsFocus = true;
        FocusOnMouseClick = false;
        TabStop = true;
        Popup = new UiMarkupColorPopup(this);
        AddChild(Popup);
    }

    public UiMarkupColorPopup Popup { get; }

    /// <summary>The bound colour, packed 0xAARRGGBB.</summary>
    public Func<uint>? ValueSource { get; set; }

    /// <summary>Called with each colour the player picks, at most once per tick.</summary>
    public Action<uint>? Changed { get; set; }

    /// <summary>Whether the picker edits alpha; without it every reported colour is opaque.</summary>
    public bool AllowAlpha { get; set; }

    public PluginUiPalette? ThemePalette { get; set; }
    public UiDatFont? DatFont { get; set; }

    /// <summary>Where the popup's gradients come from; null draws them with fills.</summary>
    public IMarkupTextureService? Textures { get; set; }

    /// <summary>The recent colours row, shared with the document's other pickers.</summary>
    public MarkupRecentColors Recent { get; set; } = new(null);

    public uint Value => ValueSource?.Invoke() ?? MarkupColorValue.White;

    public bool IsOpen => Popup.IsOpen;

    public override bool HandlesClick => true;

    public bool Open() => Enabled && Popup.TryOpen();

    public override bool OnEvent(in UiEvent e)
    {
        if (_keyboard.HandleEvent(e, TabStop, Enabled, () => Open()))
            return true;
        if (e.Type == UiEventType.Click)
        {
            Open();
            return true;
        }
        return false;
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        PluginUiPalette? p = ThemePalette;
        uint argb = Value;
        float w = Width - 2f, h = Height - 2f;
        bool focused = _keyboard.Focused || IsOpen;
        if (p is not null)
        {
            if (MarkupColorValue.Alpha(argb) < 0xFF)
                DrawChecker(ctx, 1f, 1f, w, h, Fade);
            ctx.FillRoundedRect(1f, 1f, w, h, PluginUiStyle.ControlRadius - 1f, Fade(MarkupColorValue.ToVector4(argb)));
            PluginUiStyle.Field(ctx, p, Width, Height, Vector4.Zero, focused);
            return;
        }
        if (MarkupColorValue.Alpha(argb) < 0xFF)
            DrawChecker(ctx, 1f, 1f, w, h, Fade);
        ctx.DrawFill(1f, 1f, w, h, Fade(MarkupColorValue.ToVector4(argb)));
        ctx.DrawRectOutline(0f, 0f, Width, Height, Fade(focused ? FocusColor : ClassicBorder), 1f);
    }

    /// <summary>The light/dark squares drawn behind a translucent colour.</summary>
    internal static void DrawChecker(UiRenderContext ctx, float x, float y, float w, float h,
        Func<Vector4, Vector4> fade)
    {
        for (float cy = 0f; cy < h; cy += CheckerCell)
        {
            for (float cx = 0f; cx < w; cx += CheckerCell)
            {
                bool light = ((int)(cx / CheckerCell) + (int)(cy / CheckerCell)) % 2 == 0;
                ctx.DrawFill(x + cx, y + cy, MathF.Min(CheckerCell, w - cx), MathF.Min(CheckerCell, h - cy),
                    fade(light ? CheckerLight : CheckerDark));
            }
        }
    }

    private Vector4 Fade(Vector4 color) =>
        Enabled ? color : color with { W = color.W * UiMarkupToggle.DisabledAlpha };
}
