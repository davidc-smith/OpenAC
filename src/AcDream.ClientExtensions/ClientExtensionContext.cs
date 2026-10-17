using AcDream.Runtime;

namespace AcDream.ClientExtensions;

/// <summary>What the client gives an extension when it attaches it.</summary>
public sealed class ClientExtensionContext
{
    /// <summary>The client's live runtime: session, objects, character, events.</summary>
    public required GameRuntime Runtime { get; init; }

    /// <summary>Writes a line to the client log.</summary>
    public required Action<string> Log { get; init; }

    /// <summary>
    /// Claims a chat command word (without the slash). The handler gets the
    /// text after the word. Dispose the result to give the word up.
    /// </summary>
    public required Func<string, Action<string>, IDisposable> RegisterChatCommand { get; init; }

    /// <summary>The ImGui overlay, or null when the client has none.</summary>
    public IClientOverlay? Overlay { get; init; }

    /// <summary>
    /// Runs text as if typed in the chat bar (commands, tells, say). True when
    /// the client accepted it. Null when the client offers no submission.
    /// </summary>
    public Func<string, bool>? SubmitChatCommand { get; init; }
}
