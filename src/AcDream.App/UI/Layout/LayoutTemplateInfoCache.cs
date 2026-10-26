namespace AcDream.App.UI.Layout;

/// <summary>
/// Reads each list-row template's element tree once. A list builds one row
/// per entry from the same template; reading the template from the game
/// files again for every row made opening a long list (the keyboard
/// configuration has well over a hundred rows) slow. Every row still gets
/// its own widgets built from the shared, read-only tree. A template that is
/// missing is remembered as missing.
/// </summary>
internal sealed class LayoutTemplateInfoCache
{
    private readonly Func<uint, uint, ElementInfo?> _import;
    private readonly Dictionary<(uint LayoutId, uint ElementId), ElementInfo?> _imported = new();

    /// <param name="import">Reads one template's element tree, or null when it is missing.</param>
    internal LayoutTemplateInfoCache(Func<uint, uint, ElementInfo?> import)
    {
        _import = import ?? throw new ArgumentNullException(nameof(import));
    }

    /// <summary>The element tree of element <paramref name="elementId"/> of layout <paramref name="layoutId"/>.</summary>
    internal ElementInfo? Get(uint layoutId, uint elementId)
    {
        if (!_imported.TryGetValue((layoutId, elementId), out ElementInfo? info))
        {
            info = _import(layoutId, elementId);
            _imported[(layoutId, elementId)] = info;
        }
        return info;
    }
}
