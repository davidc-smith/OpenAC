using AcDream.ClientExtensions;

namespace AcDream.App.Extensions;

/// <summary>
/// Whether the player is typing in the client: the chat entry is active, or the retained
/// UI has a keyboard focus element. Both read live (the UI binds after extensions attach).
/// </summary>
internal sealed class ClientInputState(Func<bool> uiHasKeyboardFocus, Func<bool> chatEntryActive) : IClientInput
{
    public bool IsTyping => chatEntryActive() || uiHasKeyboardFocus();

    public string TypingDescription =>
        chatEntryActive() ? "the chat entry" : uiHasKeyboardFocus() ? "a client text box" : "nothing";
}
