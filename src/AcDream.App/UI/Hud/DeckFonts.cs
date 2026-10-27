using System.Numerics;
using AcDream.App.Rendering;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The Modern HUD's fonts: the bundled sans, baked on first use at each deck
/// scale it is drawn at. Without them (tests, or before textures exist) the
/// context's default font stands in.
/// </summary>
internal sealed class DeckFonts(Func<float, BundledUiFontWeight, UiDatFont> bake)
{
    /// <summary>Values: vital numbers, tooltip body.</summary>
    public const float TextPixelHeight = 14f;
    /// <summary>Labels and the casting line, a size below the values.</summary>
    public const float SmallPixelHeight = 12f;

    private readonly Dictionary<float, DeckFontSet> _sets = [];

    public static DeckFonts Load(TextureCache textures) =>
        new((pixelHeight, weight) => BundledUiFont.Load(textures, pixelHeight, weight));

    /// <summary>The faces at scale 1, used by the tooltips.</summary>
    public UiDatFont Text => At(1f).Text;
    public UiDatFont Small => At(1f).Small;
    public UiDatFont Bold => At(1f).Bold;

    /// <summary>The faces for a deck drawn at <paramref name="scale"/>; each bakes when first used.</summary>
    public DeckFontSet At(float scale)
    {
        if (!_sets.TryGetValue(scale, out DeckFontSet? set))
        {
            float text = PixelHeight(TextPixelHeight, scale), small = PixelHeight(SmallPixelHeight, scale);
            _sets[scale] = set = new DeckFontSet(
                new(() => bake(text, BundledUiFontWeight.Regular)),
                new(() => bake(small, BundledUiFontWeight.Regular)),
                new(() => bake(text, BundledUiFontWeight.SemiBold)),
                new(() => bake(small, BundledUiFontWeight.SemiBold)));
        }
        return set;
    }

    /// <summary>A base pixel height at a deck scale, in whole pixels.</summary>
    internal static float PixelHeight(float basePixels, float scale) =>
        MathF.Round(basePixels * scale, MidpointRounding.AwayFromZero);
}

/// <summary>The deck's faces at one scale.</summary>
internal sealed class DeckFontSet(Lazy<UiDatFont> text, Lazy<UiDatFont> small, Lazy<UiDatFont> bold, Lazy<UiDatFont> smallBold)
{
    public UiDatFont Text => text.Value;
    public UiDatFont Small => small.Value;
    public UiDatFont Bold => bold.Value;
    /// <summary>The small size in SemiBold: button labels (the Command Deck's CAST).</summary>
    public UiDatFont SmallBold => smallBold.Value;
}

/// <summary>One face to draw with: a bundled sans face, or the context's default font when there is none.</summary>
internal readonly record struct DeckFace(UiDatFont? Font)
{
    /// <summary>The text or small face for a deck drawn at <paramref name="scale"/>; the default font without fonts.</summary>
    public static DeckFace For(DeckFonts? fonts, float scale, bool small)
    {
        if (fonts is null) return new(null);
        DeckFontSet set = fonts.At(scale);
        return new(small ? set.Small : set.Text);
    }

    /// <summary>The small SemiBold face for a deck drawn at <paramref name="scale"/>; the default font without fonts.</summary>
    public static DeckFace SmallBoldFor(DeckFonts? fonts, float scale) =>
        new(fonts?.At(scale).SmallBold);

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
