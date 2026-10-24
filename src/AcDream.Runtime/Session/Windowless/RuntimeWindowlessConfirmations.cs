using AcDream.Core.Net;
using AcDream.Core.Net.Messages;

namespace AcDream.Runtime.Session.Windowless;

/// <summary>
/// The confirmation the server is waiting on while nothing shows it: the
/// yes/no question a window would put in a dialog. A client with no window
/// keeps the one the server asked last, tells its plugins, and answers it
/// when asked to -- with the context id the server gave, so an answer to a
/// question already answered or withdrawn goes nowhere.
/// </summary>
internal sealed class RuntimeWindowlessConfirmations
{
    private readonly Func<WorldSession?> _session;
    private GameEvents.CharacterConfirmationRequest? _pending;

    /// <param name="session">The connection an answer goes out on.</param>
    internal RuntimeWindowlessConfirmations(Func<WorldSession?> session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>The question waiting for an answer, if any.</summary>
    internal GameEvents.CharacterConfirmationRequest? Pending => _pending;

    /// <summary>The server asked a question; it replaces any earlier one.</summary>
    internal void Request(GameEvents.CharacterConfirmationRequest request) =>
        _pending = request;

    /// <summary>
    /// The server settled a question on its own (another client answered,
    /// the request timed out). Only the question with that context id goes;
    /// a newer one asked since is left waiting.
    /// </summary>
    internal void Done(GameEvents.CharacterConfirmationDone done)
    {
        if (_pending is { } pending && pending.ContextId == done.ContextId)
            _pending = null;
    }

    /// <summary>
    /// Answers the question with this context id, as a dialog's Yes or No
    /// would. False when no such question is waiting.
    /// </summary>
    internal bool TryAnswer(uint contextId, bool accept)
    {
        if (_pending is not { } pending || pending.ContextId != contextId)
            return false;
        Respond(accept);
        return true;
    }

    /// <summary>Answers the question waiting; there must be one.</summary>
    internal void Respond(bool accepted)
    {
        if (_pending is not { } request)
        {
            throw new InvalidOperationException(
                "No confirmation request is pending.");
        }
        _session()?.SendConfirmationResponse(
            request.Type,
            request.ContextId,
            accepted);
        _pending = null;
    }

    /// <summary>
    /// Forgets the question: a new connection asks its own, and a window that
    /// takes the session over answers in its own dialog.
    /// </summary>
    internal void Forget() => _pending = null;
}
