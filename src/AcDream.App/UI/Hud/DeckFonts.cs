using System.Numerics;
using AcDream.App.Rendering;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The Modern HUD's fonts: the bundled sans, baked on first use. Without them
/// (tests, or before textures exist) the context's default font stands in.
/// </summary>
internal sealed class DeckFonts(Lazy<UiDatFont> text, Lazy<UiDatFont> small, Lazy<UiDatFont> bold)
{
    /// <summary>Values: vital numbers, tooltip body.</summary>
    public const float TextPixelHeight = 14f;
    /// <summary>Labels and the casting line, a size below the values.</summary>
    public const float SmallPixelHeight = 12f;

    public static DeckFonts Load(TextureCache textures) => new(
        new(() => BundledUiFont.Load(textures, TextPixelHeight)),
        new(() => BundledUiFont.Load(textures, SmallPixelHeight)),
        new(() => BundledUiFont.Load(textures, TextPixelHeight, BundledUiFontWeight.SemiBold)));

    public UiDatFont Text => text.Value;
    public UiDatFont Small => small.Value;
    public UiDatFont Bold => bold.Value;
}

/// <summary>One face to draw with: a bundled sans face, or the context's default font when there is none.</summary>
internal readonly record struct DeckFace(UiDatFont? Font)
{
    public float Measure(UiRenderContext ctx, string text) =>
        Font?.MeasureWidth(text) ?? ctx.DefaultFont?.MeasureWidth(text) ?? 0f;

    public float LineHeight(UiRenderContext ctx) =>
        Font?.LineHeight ?? ctx.DefaultFont?.LineHeight ?? 14f;

    public void Draw(UiRenderContext ctx, string text, float x, float y, Vector4 ink)
    {
        if (Font is not null) ctx.DrawStringDat(Font, text, x, y, ink);
        else ctx.DrawString(text, x, y, ink);
    }
}
