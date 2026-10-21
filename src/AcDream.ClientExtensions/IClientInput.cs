namespace AcDream.ClientExtensions;

/// <summary>The client's own keyboard state as an extension sees it. Game thread.</summary>
public interface IClientInput
{
    /// <summary>True while the player is typing in a client text box (the chat entry, a name field...).</summary>
    bool IsTyping { get; }

    /// <summary>Where the typing goes, for status lines: "the chat entry", "a client text box" or "nothing".</summary>
    string TypingDescription { get; }
}
