namespace AcDream.ClientExtensions;

/// <summary>
/// The client's chat, for an extension that runs its own chat window.
/// Game thread.
/// </summary>
public interface IClientChat
{
    /// <summary>
    /// Adds a claim on the chat-entry key (the ChatStartCommand action, Enter
    /// by default). When the player presses it, claims run in order before the
    /// client's chat bar opens; the first that returns true takes the key and
    /// the chat bar stays shut. Dispose the result to remove the claim.
    /// </summary>
    IDisposable ClaimChatEntry(Func<bool> tryClaim);

    /// <summary>Hides (true) or shows again (false) the client's main chat window.</summary>
    void SetChatWindowHidden(bool hidden);
}
