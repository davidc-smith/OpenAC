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

    private readonly Dictionary<(float, BundledUiFontWeight), Lazy<UiDatFont>> _faces = [];

    /// <summary>A bundled sans face at a whole-pixel height (the Chat Deck's text sizes), baked once.</summary>
    public UiDatFont Face(float pixelHeight, BundledUiFontWeight weight)
    {
        float px = PixelHeight(pixelHeight, 1f);
        if (!_faces.TryGetValue((px, weight), out Lazy<UiDatFont>? face))
            _faces[(px, weight)] = face = new(() => bake(px, weight));
        return face.Value;
    }

    public static DeckFonts Load(TextureCache textures) =>
        new((pixelHeight, weight) => BundledUiFont.Load(textures, pixelHeight, weight));

    /// <summary>The faces at scale 1, used by the tooltips.</summary>
    public UiDatFont Text => At(1f).Text;
    public UiDatFont Small => At(1f).Small;
    public UiDatFont Bold => At(1f).Bold;

    /// <summary>The faces for a deck drawn at <paramref name="scale"/>; each bakes when first used,
    /// once per pixel height and weight however many scales round to it.</summary>
    public DeckFontSet At(float scale)
    {
        if (!_sets.TryGetValue(scale, out DeckFontSet? set))
        {
            float text = PixelHeight(TextPixelHeight, scale), small = PixelHeight(SmallPixelHeight, scale);
            _sets[scale] = set = new DeckFontSet(
                new(() => Face(text, BundledUiFontWeight.Regular)),
                new(() => Face(small, BundledUiFontWeight.Regular)),
                new(() => Face(text, BundledUiFontWeight.SemiBold)),
                new(() => Face(small, BundledUiFontWeight.SemiBold)));
        }
        return set;
    }

    /// <summary>A base pixel height at a deck scale, in whole pixels.</summary>
    internal static float PixelHeight(float basePixels, float scale) =>
        MathF.Round(basePixels * scale, MidpointRounding.AwayFromZero);

    /// <summary>The faces for a deck at <paramref name="scale"/> with text factor <paramref name="textFactor"/>:
    /// each face is round(base × scale × factor) pixels.</summary>
    public DeckFontSet At(float scale, float textFactor) => At(scale * textFactor);

    /// <summary>Raised by <see cref="FitFace"/> with what it chose (tests check that no face is taller than its box).</summary>
    internal Action<DeckFit>? Fitted { get; set; }

    /// <summary>
    /// The face for text in a fixed box: round(base × scale × factor) pixels, or, when
    /// that face's line is taller than <paramref name="boxHeight"/>, the largest smaller
    /// face whose line fits. Never below the face at the deck's own scale (factor 1), so
    /// a deck at 100% draws exactly as before; never above the factor's face.
    /// </summary>
    public UiDatFont FitFace(float basePixels, BundledUiFontWeight weight, float scale, float textFactor, float boxHeight)
    {
        float floor = PixelHeight(basePixels, scale);
        float px = PixelHeight(basePixels, scale * textFactor);
        UiDatFont? face = Face(px, weight);
        while (px > floor && face is not null && face.LineHeight > boxHeight + 0.01f)
        {
            px -= 1f;
            face = Face(px, weight);
        }
        Fitted?.Invoke(new DeckFit(basePixels, weight, scale, textFactor, boxHeight, px, face?.LineHeight ?? px));
        return face!;
    }

    /// <summary>The line height of the face at round(base × scale): the baked face's, or the pixel height without fonts.</summary>
    public static float LineHeightAt(DeckFonts? fonts, float basePixels, BundledUiFontWeight weight, float scale)
    {
        float px = PixelHeight(basePixels, scale);
        UiDatFont? face = fonts?.Face(px, weight);
        return face?.LineHeight ?? px;
    }

    /// <summary>
    /// How much a row holding one line of this text grows at <paramref name="textFactor"/>:
    /// the line's growth over the deck's own size, never negative.
    /// </summary>
    public static float RowGrow(DeckFonts? fonts, float basePixels, BundledUiFontWeight weight, float scale, float textFactor) =>
        MathF.Max(0f, LineHeightAt(fonts, basePixels, weight, scale * textFactor) - LineHeightAt(fonts, basePixels, weight, scale));
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

/// <summary>One <see cref="DeckFonts.FitFace"/> choice: the asked size and box, and the face's pixels and line.</summary>
internal readonly record struct DeckFit(
    float BasePixels, BundledUiFontWeight Weight, float Scale, float TextFactor, float BoxHeight, float Pixels, float LineHeight);

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

    /// <summary>The face for text in a box <paramref name="boxHeight"/> tall (<see cref="DeckFonts.FitFace"/>); the default font without fonts.</summary>
    public static DeckFace In(DeckFonts? fonts, float basePixels, BundledUiFontWeight weight, float scale, float textFactor, float boxHeight) =>
        new(fonts?.FitFace(basePixels, weight, scale, textFactor, boxHeight));

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
