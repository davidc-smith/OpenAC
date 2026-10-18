using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host;

/// <summary>
/// The bar's icons: RynthCore's title-to-Phosphor table (ImGuiBar's
/// PanelIcons), drawn only when the overlay's font has the glyph; otherwise
/// the title's first letter, as RynthCore does for titles it doesn't know.
/// </summary>
internal static class RynthBarIcons
{
    private static readonly Dictionary<string, string> Icons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RynthAi"] = PhosphorIcons.Robot,
        ["Chat"] = PhosphorIcons.ChatCircleText,
        ["ChatFilters"] = PhosphorIcons.Funnel,
        ["Radar"] = PhosphorIcons.Compass,
        ["Vision"] = PhosphorIcons.Eye,
        ["Nav"] = PhosphorIcons.MapTrifold,
        ["RynthNav"] = PhosphorIcons.NavigationArrow,
        ["Tracker"] = PhosphorIcons.ChartLineUp,
        ["Lua"] = PhosphorIcons.FileCode,
        ["Settings"] = PhosphorIcons.Gear,
        ["Damage"] = PhosphorIcons.ChartBar,
        ["Meta"] = PhosphorIcons.ListChecks,
        ["Items"] = PhosphorIcons.Backpack,
        ["Loot Editor"] = PhosphorIcons.TreasureChest,
        ["Skills"] = PhosphorIcons.GraduationCap,
        ["Sense"] = PhosphorIcons.Binoculars,
        ["Status"] = PhosphorIcons.Info,
        ["Inventory"] = PhosphorIcons.Package,
        ["Log"] = PhosphorIcons.TerminalWindow,
        ["Item Count"] = PhosphorIcons.Bag,
        ["Mini Remote"] = PhosphorIcons.SquaresFour,
        ["Characters"] = PhosphorIcons.UsersThree,
        ["RynthSort"] = PhosphorIcons.SortAscending,
        ["Oracle"] = PhosphorIcons.Sparkle,
        ["Global Inventory"] = PhosphorIcons.GlobeHemisphereWest,
    };

    /// <summary>RynthCore's icon for a title, or null.</summary>
    internal static string? Icon(string title) => Icons.TryGetValue(title, out string? icon) ? icon : null;

    /// <summary>The title's icon when the current font has it, else its first letter or digit, else "?".</summary>
    internal static string Label(string title)
    {
        if (Icon(title) is { } icon && InFont(icon))
            return icon;
        foreach (char c in title)
        {
            if (char.IsLetterOrDigit(c))
                return new string(char.ToUpperInvariant(c), 1);
        }

        return "?";
    }

    /// <summary>True when the current ImGui font draws <paramref name="glyph"/> (one character).</summary>
    internal static unsafe bool InFont(string glyph)
    {
        ImFontPtr font = ImGui.GetFont();
        return glyph.Length == 1 && font.NativePtr != null
            && ImGuiNative.ImFont_FindGlyphNoFallback(font.NativePtr, glyph[0]) != null;
    }
}
