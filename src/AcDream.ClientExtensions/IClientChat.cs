namespace AcDream.ClientExtensions;

/// <summary>
/// The client's chat, for an extension that runs its own chat window.
/// Game thread.
/// </summary>
public interface IClientChat
{
    /// <summary>
    /// Adds a claim on any action that focuses the client's chat entry
    /// (EnterChatMode, Enter by default; ToggleChatEntry; FocusChatEntry;
    /// ChatStartCommand; the replies; tell-to-selected). Claims run in order
    /// before the chat bar opens; the first that returns true takes the action
    /// and the chat bar stays shut. Dispose the result to remove the claim.
    /// </summary>
    IDisposable ClaimChatEntry(Func<bool> tryClaim);

    /// <summary>Hides (true) or shows again (false) the client's main chat window.</summary>
    void SetChatWindowHidden(bool hidden);
}
