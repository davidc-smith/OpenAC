namespace AcDream.Rynth.Host;

/// <summary>
/// Which plugin is inside a call from the host, as RynthCore's
/// PluginManager.CurrentDispatch does: the script-window functions use it to
/// know whose windows a UiSubmit or UiPollEvents is for. Thread-bound.
/// </summary>
public static class RynthDispatch
{
    [ThreadStatic]
    private static RynthPlugin? _current;

    /// <summary>The plugin in the innermost open call on this thread, or null.</summary>
    public static RynthPlugin? Current => _current;

    /// <summary>Marks <paramref name="plugin"/> as current until the result is disposed.</summary>
    public static Scope Enter(RynthPlugin plugin)
    {
        RynthPlugin? outer = _current;
        _current = plugin;
        return new Scope(outer);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly RynthPlugin? _outer;

        internal Scope(RynthPlugin? outer) => _outer = outer;

        public void Dispose() => _current = _outer;
    }
}
