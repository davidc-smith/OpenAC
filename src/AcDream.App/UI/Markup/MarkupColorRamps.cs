namespace AcDream.App.UI.Markup;

/// <summary>
/// The colour picker's gradient pixels, straight-alpha RGBA8, rows top to
/// bottom. The square is a fill of the pure hue with <see cref="WhiteToClear"/>
/// and then <see cref="ClearToBlack"/> drawn over it, which is exactly the
/// saturation/value square, so no texture depends on the hue.
/// </summary>
internal static class MarkupColorRamps
{
    /// <summary>White, opaque at the left, clear at the right.</summary>
    public static byte[] WhiteToClear(int width, int height) =>
        Fill(width, height, (x, _) => (255, 255, 255, Ramp(x, width, falling: true)));

    /// <summary>Black, clear at the top, opaque at the bottom.</summary>
    public static byte[] ClearToBlack(int width, int height) =>
        Fill(width, height, (_, y) => (0, 0, 0, Ramp(y, height, falling: false)));

    /// <summary>The hues top to bottom, red at the top, opaque.</summary>
    public static byte[] Hue(int width, int height) =>
        Fill(width, height, (_, y) =>
        {
            uint c = MarkupColorValue.FromHsv(360f * y / height, 1f, 1f, 0xFF);
            return ((byte)(c >> 16), (byte)(c >> 8), (byte)c, 255);
        });

    /// <summary>White, opaque at the top, clear at the bottom; drawn tinted with the colour.</summary>
    public static byte[] AlphaRamp(int width, int height) =>
        Fill(width, height, (_, y) => (255, 255, 255, Ramp(y, height, falling: true)));

    private static byte Ramp(int i, int count, bool falling)
    {
        float t = count > 1 ? (float)i / (count - 1) : 0f;
        return MarkupColorValue.ToByte(falling ? 1f - t : t);
    }

    private static byte[] Fill(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                int i = (y * width + x) * 4;
                rgba[i] = r;
                rgba[i + 1] = g;
                rgba[i + 2] = b;
                rgba[i + 3] = a;
            }
        }
        return rgba;
    }
}
