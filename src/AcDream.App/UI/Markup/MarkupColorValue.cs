using System.Globalization;
using System.Numerics;

namespace AcDream.App.UI.Markup;

/// <summary>
/// Colours as the colour picker handles them: packed 0xAARRGGBB (the byte
/// order of the #AARRGGBB literal), their text, and HSV with the hue in
/// degrees [0, 360) and saturation and value in [0, 1].
/// </summary>
internal static class MarkupColorValue
{
    public const uint White = 0xFFFFFFFFu;

    public static string Format(uint argb, bool withAlpha = true) =>
        withAlpha ? $"#{argb:X8}" : $"#{argb & 0xFFFFFFu:X6}";

    /// <summary>
    /// What the hex field takes: an optional '#', then 6 or 8 hex digits, any
    /// case, surrounding spaces ignored. Six digits get <paramref name="alphaForRgb"/>.
    /// </summary>
    public static uint? Parse(string? text, byte alphaForRgb = 0xFF)
    {
        if (text is null)
            return null;
        ReadOnlySpan<char> digits = text.AsSpan().Trim();
        if (digits.Length > 0 && digits[0] == '#')
            digits = digits[1..];
        if (digits.Length is not (6 or 8))
            return null;
        foreach (char c in digits)
        {
            if (!char.IsAsciiHexDigit(c))
                return null;
        }
        uint value = uint.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return digits.Length == 6 ? (uint)alphaForRgb << 24 | value : value;
    }

    /// <summary>Exactly '#' and 8 hex digits, as a bound or stored colour string.</summary>
    public static uint? ParseStrict(string? text) =>
        text is { Length: 9 } && text[0] == '#' ? Parse(text) : null;

    public static byte Alpha(uint argb) => (byte)(argb >> 24);

    public static uint WithAlpha(uint argb, byte alpha) => (argb & 0xFFFFFFu) | (uint)alpha << 24;

    public static Vector4 ToVector4(uint argb) => new(
        ((argb >> 16) & 0xFFu) / 255f,
        ((argb >> 8) & 0xFFu) / 255f,
        (argb & 0xFFu) / 255f,
        (argb >> 24) / 255f);

    public static uint FromVector4(Vector4 color) =>
        Pack(ToByte(color.W), ToByte(color.X), ToByte(color.Y), ToByte(color.Z));

    public static uint Pack(byte a, byte r, byte g, byte b) =>
        (uint)a << 24 | (uint)r << 16 | (uint)g << 8 | b;

    public static byte ToByte(float channel) =>
        (byte)MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f, MidpointRounding.AwayFromZero);

    /// <summary>The colour's hue, saturation and value; grey has hue 0.</summary>
    public static (float H, float S, float V) ToHsv(uint argb)
    {
        float r = ((argb >> 16) & 0xFFu) / 255f;
        float g = ((argb >> 8) & 0xFFu) / 255f;
        float b = (argb & 0xFFu) / 255f;
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float delta = max - min;
        float s = max <= 0f ? 0f : delta / max;
        float h;
        if (delta <= 0f)
            h = 0f;
        else if (max == r)
            h = 60f * ((g - b) / delta);
        else if (max == g)
            h = 60f * ((b - r) / delta + 2f);
        else
            h = 60f * ((r - g) / delta + 4f);
        if (h < 0f)
            h += 360f;
        return (h, s, max);
    }

    /// <summary>The colour at a hue (any degrees, wrapped), saturation and value, with <paramref name="alpha"/>.</summary>
    public static uint FromHsv(float h, float s, float v, byte alpha)
    {
        h = ((h % 360f) + 360f) % 360f;
        s = Math.Clamp(s, 0f, 1f);
        v = Math.Clamp(v, 0f, 1f);
        float c = v * s;
        float x = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
        float m = v - c;
        (float r, float g, float b) = (int)(h / 60f) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return Pack(alpha, ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }
}
