namespace AcDream.ClientExtensions;

/// <summary>
/// Code built into the client that sees the client's own runtime, unlike a
/// plugin, which sees only AcDream.Plugin.Abstractions. The client creates
/// each extension once, attaches it after its runtime exists, ticks it every
/// update frame and disposes it at shutdown, all on the game thread.
/// </summary>
public interface IClientExtension : IDisposable
{
    void Attach(ClientExtensionContext context);

    void Tick(double deltaSeconds);
}
