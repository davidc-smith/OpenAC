using AcDream.ClientExtensions;

namespace AcDream.App.Extensions;

/// <summary>
/// The client side of <see cref="IClientChat"/>: claims on the chat-entry key,
/// asked by the retail UI before it opens its chat bar, and whether an
/// extension wants the main chat window hidden. Game thread.
/// </summary>
internal sealed class ClientChatSeam : IClientChat
{
    private readonly Action<string> _log;
    private readonly List<Func<bool>> _claims = new();

    public ClientChatSeam(Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public bool ChatWindowHidden { get; private set; }

    public IDisposable ClaimChatEntry(Func<bool> tryClaim)
    {
        ArgumentNullException.ThrowIfNull(tryClaim);
        _claims.Add(tryClaim);
        return new Registration(() => _claims.Remove(tryClaim));
    }

    public void SetChatWindowHidden(bool hidden) => ChatWindowHidden = hidden;

    /// <summary>True when an extension took the chat-entry key.</summary>
    public bool TryClaimEntry()
    {
        foreach (Func<bool> claim in _claims.ToArray())
        {
            try
            {
                if (claim())
                    return true;
            }
            catch (Exception error)
            {
                _claims.Remove(claim);
                _log($"[Extensions] chat-entry claim removed: {error.GetType().Name}: {error.Message}");
            }
        }

        return false;
    }

    private sealed class Registration(Action remove) : IDisposable
    {
        private Action? _remove = remove;

        public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
    }
}
