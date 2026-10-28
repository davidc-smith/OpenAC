namespace AcDream.App.UI.Hud;

/// <summary>Where the Chat Deck's settings popup sits when it opens: docked to the deck's top-right corner.</summary>
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
}
