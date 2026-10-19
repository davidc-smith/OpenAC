using AcDream.Plugin.Abstractions;
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

    /// <summary>The client's chat; null without a retail UI (tests, headless).</summary>
    public IClientChat? Chat { get; init; }

    /// <summary>
    /// A folder the extension may keep its own files in (one per install; the
    /// extension creates it when it first writes). Null: don't save anything.
    /// </summary>
    public string? StateDirectory { get; init; }

    /// <summary>
    /// The client's plugin automation surface, bound to <see cref="Runtime"/>.
    /// Null when the client has none (tests); it answers unavailable before login.
    /// </summary>
    public IAutomationSurface? Automation { get; init; }

    /// <summary>The client's world-triangle layers; null in a host that draws no world.</summary>
    public AcDream.Plugin.Abstractions.IPluginWorldTriangles? WorldTriangles { get; init; }

    /// <summary>The directory the client read its dats from; null in a host without dats.</summary>
    public string? DatDirectory { get; init; }

    /// <summary>The client's view and radar on screen; null in a host without a window.</summary>
    public IClientScreen? Screen { get; init; }
}
