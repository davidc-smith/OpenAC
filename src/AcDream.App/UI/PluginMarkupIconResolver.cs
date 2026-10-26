namespace AcDream.App.UI;

/// <summary>
/// The icon resolver one plugin window's markup draws through: the client's
/// own icons from the shared retail resolver, and file icons from the
/// plugin's install folder. Each path is looked up once per window and the
/// answer kept, so a missing file is not looked for again on every frame; a
/// hot reload builds the window, and so this resolver, again. A file with a
/// light or dark version beside it (<see cref="PluginImageVariants"/>) draws
/// the one <paramref name="isLight"/> asks for, so it follows the theme.
/// </summary>
internal sealed class PluginMarkupIconResolver(
    IMarkupIconResolver retail,
    PluginFileIconCache files,
    string pluginId,
    string? pluginDirectory,
    Func<bool>? isLight = null) : IMarkupIconResolver
{
    /// <summary>A bound path can name anything; past this many, the answers are forgotten and asked again.</summary>
    private const int MaximumRemembered = 4096;

    private readonly Dictionary<string, PluginFileIconCache.Entry?> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginFileIconCache.Entry?> _variants = new(StringComparer.Ordinal);

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

        if (PluginImageVariants.PathFor(path, PluginImageVariants.For(isLight?.Invoke() == true)) is { } variant
            && Lookup(_variants, variant, pluginDirectory, onlyIfThere: true) is { Texture: not 0u and var own } chosen)
            return (own, chosen.Width, chosen.Height);

        PluginFileIconCache.Entry? entry = Lookup(_files, path, pluginDirectory, onlyIfThere: false);
        return entry is { Texture: not 0u and var texture }
            ? (texture, entry.Width, entry.Height)
            : (0u, 0, 0);
    }

    private PluginFileIconCache.Entry? Lookup(
        Dictionary<string, PluginFileIconCache.Entry?> remembered, string path, string directory, bool onlyIfThere)
    {
        if (!remembered.TryGetValue(path, out PluginFileIconCache.Entry? entry))
        {
            if (remembered.Count >= MaximumRemembered)
                remembered.Clear();
            entry = onlyIfThere && !PluginImageVariants.Exists(directory, path)
                ? null
                : files.Acquire(pluginId, directory, path);
            remembered[path] = entry;
        }
        return entry;
    }
}
