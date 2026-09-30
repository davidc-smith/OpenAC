namespace AcDream.Plugin.Abstractions;

/// <summary>
/// A font at one size that the host holds on a plugin's behalf, for drawing
/// canvas text. The handle means nothing outside the host that issued it; the
/// metrics are carried so the plugin can lay text out without asking again.
/// Sizes and metrics are in canvas pixels.
/// </summary>
/// <param name="Handle">The host's number for this font; 0 means no font.</param>
/// <param name="PixelSize">The size the font was asked for, in pixels.</param>
/// <param name="LineHeight">How far apart two lines of this font sit, in pixels.</param>
/// <param name="Ascent">How far below the top of a line its baseline sits, in pixels.</param>
public readonly record struct PluginFont(int Handle, float PixelSize, float LineHeight, float Ascent)
{
    /// <summary>No font: what every refused request returns.</summary>
    public static PluginFont None => default;

    /// <summary>True for a font the host issued. A released or dropped font stays valid by this test but draws nothing.</summary>
    public bool IsValid => Handle != 0;
}

/// <summary>An inclusive range of Unicode code points, such as U+0041 to U+005A for A to Z.</summary>
/// <param name="First">The first code point in the range.</param>
/// <param name="Last">The last code point in the range; at least <paramref name="First"/>.</param>
public readonly record struct PluginCodepointRange(int First, int Last);

/// <summary>How a font the plugin ships is prepared.</summary>
public sealed record PluginFontOptions
{
    /// <summary>
    /// The characters to prepare. Null prepares the client's default set:
    /// U+0020–U+024F (Latin), U+0370–U+052F (Greek and Cyrillic) and
    /// U+2000–U+206F (punctuation). Only characters the font actually has
    /// are prepared and counted, so an icon font can name the whole
    /// private-use area, U+E000–U+F8FF.
    /// </summary>
    public IReadOnlyList<PluginCodepointRange>? Ranges { get; init; }
}

/// <summary>
/// Fonts a plugin can draw canvas text with: the client's bundled sans-serif
/// at a chosen size, and TrueType (.ttf) or OpenType (.otf) fonts the plugin
/// ships, prepared by the host. Each size of each font is its own
/// <see cref="PluginFont"/>.
///
/// <para>Every request is counted once per distinct font, size and set of
/// characters, and held as many times as it was asked for: asking twice
/// returns the same font, and it takes two releases to let it go. A plugin
/// may hold at most <see cref="MaximumCount"/> fonts, and its own fonts may
/// take at most <see cref="MaximumBytes"/> of memory, counting the font files
/// it supplied and the glyph textures made from them. A request past either
/// limit, a size outside <see cref="MinimumPixelSize"/> to
/// <see cref="MaximumPixelSize"/>, or a font with more than
/// <see cref="MaximumGlyphs"/> of the requested characters is refused with
/// <see cref="PluginFont.None"/> and reported once in the client's log. The
/// bundled font is shared with every other plugin and costs nothing against
/// the byte budget.</para>
///
/// <para>Preparing a font takes some milliseconds and happens when it is
/// asked for, never while a canvas paints, so ask for fonts up front rather
/// than inside a paint callback: a request made inside a paint callback for a
/// font not already held answers <see cref="PluginFont.None"/> and is
/// reported. Call this only from the thread the plugin's own callbacks run
/// on; the host throws <see cref="InvalidOperationException"/> on any other. On a host without a
/// window, or before the client's interface is up, every request answers
/// <see cref="PluginFont.None"/> and <see cref="IsAvailable"/> is false.
/// Fonts are dropped when the interface is torn down, for example on a
/// reconnect; text drawn with a dropped font draws nothing, and the plugin
/// asks again once it is drawing again.</para>
/// </summary>
public interface IPluginFonts
{
    /// <summary>
    /// Whether requests can currently be answered: false on a host without a
    /// window and until the client's interface is up.
    /// </summary>
    bool IsAvailable => false;

    /// <summary>The client's bundled sans-serif font, Noto Sans, at a size.</summary>
    /// <param name="pixelSize">The size in canvas pixels, from <see cref="MinimumPixelSize"/> to <see cref="MaximumPixelSize"/>.</param>
    /// <returns>The font, or <see cref="PluginFont.None"/> when the request was refused.</returns>
    PluginFont Bundled(float pixelSize) => PluginFont.None;

    /// <summary>
    /// A font the plugin ships, opened through <paramref name="open"/> only
    /// when the host does not already hold it at that size with those
    /// characters. The host reads the stream and disposes it; TrueType and
    /// OpenType (CFF) outlines are accepted, and the plugin never sees pixels.
    /// </summary>
    /// <param name="name">The plugin's own name for the font file, unique within the plugin, such as a relative path.</param>
    /// <param name="open">Opens a fresh readable stream of the font file.</param>
    /// <param name="pixelSize">The size in canvas pixels, from <see cref="MinimumPixelSize"/> to <see cref="MaximumPixelSize"/>.</param>
    /// <param name="options">Which characters to prepare; null prepares the default set.</param>
    /// <returns>The font, or <see cref="PluginFont.None"/> when the stream could not be opened or read, the font has none of the characters, a limit would be passed, or the request was refused.</returns>
    PluginFont FromStream(string name, Func<Stream> open, float pixelSize, PluginFontOptions? options = null) =>
        PluginFont.None;

    /// <summary>
    /// Lets go of one hold on a font. The font stays while another hold
    /// remains and is freed on the last.
    /// </summary>
    /// <param name="font">A font this surface issued.</param>
    /// <returns>False for a font this surface did not issue or has already let go of completely.</returns>
    bool Release(PluginFont font) => false;

    /// <summary>How many distinct fonts the plugin currently holds.</summary>
    int Count => 0;

    /// <summary>The most distinct fonts the plugin may hold at once; 0 on a host that draws nothing.</summary>
    int MaximumCount => 0;

    /// <summary>The most memory, in bytes, the plugin's own fonts may take; 0 on a host that draws nothing.</summary>
    long MaximumBytes => 0;

    /// <summary>The most characters one font may prepare; 0 on a host that draws nothing.</summary>
    int MaximumGlyphs => 0;

    /// <summary>The smallest size a font may be asked for, in pixels; 0 on a host that draws nothing.</summary>
    float MinimumPixelSize => 0f;

    /// <summary>The largest size a font may be asked for, in pixels; 0 on a host that draws nothing.</summary>
    float MaximumPixelSize => 0f;
}

/// <summary>
/// The font surface a host that draws nothing hands out: every request
/// answers <see cref="PluginFont.None"/> and nothing is held.
/// </summary>
public sealed class NoOpPluginFonts : IPluginFonts
{
    /// <summary>The shared instance; this type holds no state.</summary>
    public static NoOpPluginFonts Instance { get; } = new();

    private NoOpPluginFonts()
    {
    }
}
