using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal enum ChatDeckEditorPage { Tabs, Rules }

/// <summary>What the deck asks the Filters &amp; Tabs editor to show when it opens.</summary>
internal sealed record ChatDeckEditorRequest
{
    public ChatDeckEditorPage Page { get; init; }
    /// <summary>The tab to select on the Tabs page; null keeps the current one.</summary>
    public string? TabId { get; init; }
    /// <summary>Open with the selected tab's delete confirmation showing (the tab menu's Delete).</summary>
    public bool ConfirmDeleteTab { get; init; }
    /// <summary>A new rule to open in the rule form; added only when the player presses Add.</summary>
    public ChatDeckRule? NewRule { get; init; }
    /// <summary>The line <see cref="NewRule"/> was written from, for the generalise options.</summary>
    public ChatDeckLineFacts? FromLine { get; init; }
    /// <summary>A line to put in the test box, with its real channel and sender.</summary>
    public ChatDeckLineFacts? TestLine { get; init; }
}
