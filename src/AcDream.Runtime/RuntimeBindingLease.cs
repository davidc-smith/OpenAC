namespace AcDream.Runtime;

/// <summary>
/// Gives back a binding a host lent the runtime, once: the runtime outlives
/// the window that lent it, and must not keep calling into it after it has
/// gone.
/// </summary>
internal sealed class RuntimeBindingLease(Action release) : IDisposable
{
    private Action? _release = release ?? throw new ArgumentNullException(nameof(release));

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
