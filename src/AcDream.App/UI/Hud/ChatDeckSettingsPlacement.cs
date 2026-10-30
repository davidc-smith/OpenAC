namespace AcDream.App.UI.Hud;

/// <summary>Where a deck popup sits when it opens: docked to the deck's top-right corner, or centred on the screen.</summary>
internal static class ChatDeckSettingsPlacement
{
    /// <summary>
    /// The popup's top-left. Above the deck with right edges aligned when there is room, else below it,
    /// then kept on screen.
    /// </summary>
    internal static (float Left, float Top) DockedPlace(DeckRect deck, float popupW, float popupH, float rootW, float rootH)
    {
        float left = deck.X + deck.W - popupW;
        float top = deck.Y - popupH;
        if (top < 0f) top = deck.Y + deck.H;
        left = MathF.Max(0f, MathF.Min(left, rootW - popupW));
        top = MathF.Max(0f, MathF.Min(top, rootH - popupH));
        return (left, top);
    }

    /// <summary>Docks the popup to the deck (the deck being a child of the same root); no deck, no move.</summary>
    internal static void Dock(UiRoot root, UiElement? deck, UiElement popup)
    {
        if (deck is null) return;
        (popup.Left, popup.Top) = DockedPlace(new DeckRect(deck.Left, deck.Top, deck.Width, deck.Height),
            popup.Width, popup.Height, root.Width, root.Height);
        popup.ResetAnchorCapture();
    }

    /// <summary>The popup's top-left in the middle of the root, kept on screen when it is the larger.</summary>
    internal static (float Left, float Top) CentredPlace(float popupW, float popupH, float rootW, float rootH) =>
        (MathF.Max(0f, (rootW - popupW) / 2f), MathF.Max(0f, (rootH - popupH) / 2f));

    /// <summary>Puts the popup in the middle of the root.</summary>
    internal static void Centre(UiRoot root, UiElement popup)
    {
        (popup.Left, popup.Top) = CentredPlace(popup.Width, popup.Height, root.Width, root.Height);
        popup.ResetAnchorCapture();
    }
}
