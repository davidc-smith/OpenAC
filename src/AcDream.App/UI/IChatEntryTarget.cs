using AcDream.UI.Abstractions.Input;

namespace AcDream.App.UI;

/// <summary>
/// The chat input the chat-entry actions act on: the retail chat window, or the
/// Chat Deck while it is shown. Every front end shares the one entry owner
/// (draft, recall history), so switching between them loses nothing.
/// </summary>
internal interface IChatEntryTarget
{
    /// <summary>Focus the input and select its text; the key that did it is not typed.</summary>
    void EnterChatMode(KeyChord? physicalChord);
    /// <summary>Focus the input, or give the keyboard back to the game if it has it.</summary>
    void ToggleChatEntry(KeyChord? physicalChord);
    /// <summary>Focus the input with "/" typed.</summary>
    void StartCommand();
    /// <summary>Focus the input with a tell to <paramref name="name"/> started.</summary>
    void StartTell(string name);
    /// <summary>A tell to the last person who spoke on that line, when there is one.</summary>
    void StartReply(string? name);
    /// <summary>Focus the input; false when it cannot take the keyboard.</summary>
    bool Focus();
}
