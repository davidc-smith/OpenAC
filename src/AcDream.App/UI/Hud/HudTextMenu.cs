using System.Globalization;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The "Text size" block a deck's frame menu lists. The deck menus have no submenus, so the
/// block is a heading and its choices in the same list: "Text size" (disabled), "Same as HUD (N%)",
/// then 80% … 150%. The deck's current choice is marked "* ", as the Gear Deck's sort menu marks its sort.
/// </summary>
internal static class HudTextMenu
{
    public const string Heading = "Text size";
    public const string Mark = "* ";

    /// <summary>The block's entries: the heading, Same as HUD, and one per size.</summary>
    public static int Count => 2 + HudTextSettings.Steps.Count;

    public static string[] Labels(HudTextSettings settings, HudDeck deck)
    {
        var labels = new string[Count];
        bool own = settings.HasOverride(deck);
        labels[0] = Heading;
        labels[1] = (own ? "" : Mark) + "Same as HUD (" + settings.Global.ToString(CultureInfo.InvariantCulture) + "%)";
        for (int i = 0; i < HudTextSettings.Steps.Count; i++)
        {
            int step = HudTextSettings.Steps[i];
            labels[2 + i] = (own && settings.PercentFor(deck) == step ? Mark : "")
                + step.ToString(CultureInfo.InvariantCulture) + "%";
        }
        return labels;
    }

    /// <summary>Whether block entry <paramref name="index"/> can be chosen: every entry but the heading.</summary>
    public static bool Enabled(int index) => index > 0 && index < Count;

    /// <summary>The block's enabled flags, one per entry (<see cref="Enabled"/>), for a menu's ItemsEnabledSource.</summary>
    public static bool[] EnabledFlags()
    {
        var flags = new bool[Count];
        for (int i = 0; i < flags.Length; i++) flags[i] = Enabled(i);
        return flags;
    }

    /// <summary>Applies block entry <paramref name="index"/>: Same as HUD drops the deck's override, a size sets it.</summary>
    public static void Choose(HudText text, HudDeck deck, int index)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (index == 1) text.SetOverride(deck, null);
        else if (index >= 2 && index < Count) text.SetOverride(deck, HudTextSettings.Steps[index - 2]);
    }

    /// <summary>A deck menu at the deck's scale: rows, insets and widths at <paramref name="scale"/>, the
    /// small face at scale × factor, and each row grown to hold that face.</summary>
    public static void StyleMenu(UiMarkupContextMenu menu, DeckFonts? fonts, float scale, float textFactor)
    {
        menu.Scale = scale;
        menu.DatFont = fonts?.At(scale, textFactor).Small;
        menu.RowGrow = DeckFonts.RowGrow(fonts, DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, scale, textFactor);
    }
}
