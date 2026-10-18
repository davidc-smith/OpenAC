namespace AcDream.App.UI;

/// <summary>
/// The icon resolver one plugin window's markup draws through: the client's
/// own icons from the shared retail resolver, and file icons from the
/// plugin's install folder. Each path is looked up once per window and the
/// answer kept, so a missing file is not looked for again on every frame; a
/// hot reload builds the window, and so this resolver, again.
/// </summary>
internal sealed class PluginMarkupIconResolver(
    IMarkupIconResolver retail,
    PluginFileIconCache files,
    string pluginId,
    string? pluginDirectory) : IMarkupIconResolver
{
    /// <summary>A bound path can name anything; past this many, the answers are forgotten and asked again.</summary>
    private const int MaximumRemembered = 4096;

    private readonly Dictionary<string, PluginFileIconCache.Entry?> _files = new(StringComparer.Ordinal);

    public (uint tex, int w, int h) ResolveDid(uint did) => retail.ResolveDid(did);

    public (uint tex, int w, int h) ResolveSpell(uint spellId) => retail.ResolveSpell(spellId);

    public (uint tex, int w, int h) ResolveItem(uint objectId) => retail.ResolveItem(objectId);

    public (uint tex, int w, int h) ResolveFile(string path)
    {
        if (pluginDirectory is null)
        {
            files.Reject($"{pluginId}/*", "file icons need a plugin folder, and this window has no plugin folder");
            return (0u, 0, 0);
        }

        if (!_files.TryGetValue(path, out PluginFileIconCache.Entry? entry))
        {
            if (_files.Count >= MaximumRemembered)
                _files.Clear();
            entry = files.Acquire(pluginId, pluginDirectory, path);
            _files[path] = entry;
        }
        return entry is { Texture: not 0u and var texture }
            ? (texture, entry.Width, entry.Height)
            : (0u, 0, 0);
    }
}
