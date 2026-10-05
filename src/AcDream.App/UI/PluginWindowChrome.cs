using System.Globalization;
using System.Xml.Linq;

namespace AcDream.App.UI;

/// <summary>
/// The host-drawn chrome around a plugin window's content area: the frame's
/// border and, when the window has one, its title bar. A window with a
/// content area places its authored children inside these insets, and its
/// authored <c>w</c>/<c>h</c>/<c>minw</c>/<c>minh</c> describe that area.
/// Classic and the modern themes share the same insets.
/// </summary>
internal static class PluginWindowChrome
{
    /// <summary>The Classic border's width; the left, right and bottom inset.</summary>
    internal const float Border = RetailChromeSprites.Border;

    /// <summary>The title bar's height; the top inset of a window with a bar.</summary>
    internal const float TitleBarHeight = PluginUiStyle.HeaderHeight;

    /// <summary>The close button's side: the bar below the frame's top border.</summary>
    internal const float CloseButtonSize = TitleBarHeight - Border;

    internal const float HorizontalInsets = 2f * Border;
    internal const float VerticalInsets = TitleBarHeight + Border;

    /// <summary>The top inset of a content area: the title bar, or the border alone without one.</summary>
    internal static float TopInset(bool titleBar) => titleBar ? TitleBarHeight : Border;

    /// <summary>The chrome above and below a content area, with or without a title bar.</summary>
    internal static float VerticalInsetsFor(bool titleBar) => titleBar ? VerticalInsets : 2f * Border;

    /// <summary>
    /// Bumped whenever the insets above change, so windows whose chrome grew
    /// or shrank drop their saved size once.
    /// </summary>
    internal const int Version = 1;

    /// <summary>The retail inventory close button's art, 24x25 (element 0x100001D2 of layout 0x21000023).</summary>
    internal const uint ClassicCloseSprite = 0x06004D0Cu;

    /// <summary>The same element's pressed-state art (Normal_pressed), 24x25. The retail art has no hover state.</summary>
    internal const uint ClassicClosePressedSprite = 0x06004D0Du;

    internal const string CloseTooltip = "Close";
    internal const string DockedCloseTooltip = "Close (reopen from the dock)";

    private static readonly string[] GeometryAttributes =
        ["w", "h", "minw", "minh", "resizable", "resize", "layout", "titlebar"];

    /// <summary>
    /// What the markup states about a window's geometry, as written: each
    /// geometry attribute's raw value (null when absent), then the chrome
    /// <see cref="Version"/>. Nothing measured, so a bound caption or a theme
    /// never resets a player's saved size.
    /// </summary>
    internal static string?[] AuthoredInputs(XElement root)
    {
        // scroll changes the window's minimum; it is added only when present,
        // so the revision of every window without it is what it was.
        string? scroll = (string?)root.Attribute("scroll");
        var inputs = new string?[GeometryAttributes.Length + (scroll is null ? 1 : 2)];
        for (int i = 0; i < GeometryAttributes.Length; i++)
            inputs[i] = (string?)root.Attribute(GeometryAttributes[i]);
        inputs[GeometryAttributes.Length] = Version.ToString(CultureInfo.InvariantCulture);
        if (scroll is not null) inputs[^1] = "scroll=" + scroll;
        return inputs;
    }

    /// <summary>
    /// <paramref name="text"/> if it fits in <paramref name="maxWidth"/>, else
    /// its longest prefix that fits with "..." after it (dat fonts have no
    /// ellipsis glyph), else "..." alone, else nothing.
    /// </summary>
    internal static string Ellipsize(string text, Func<string, float> measure, float maxWidth)
    {
        if (measure(text) <= maxWidth) return text;
        const string Dots = "...";
        for (int n = text.Length - 1; n > 0; n--)
        {
            string candidate = text[..n].TrimEnd() + Dots;
            if (measure(candidate) <= maxWidth) return candidate;
        }
        return measure(Dots) <= maxWidth ? Dots : string.Empty;
    }
}
