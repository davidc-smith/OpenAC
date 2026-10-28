using AcDream.App.Plugins;

namespace AcDream.App.UI;

/// <summary>
/// The icon resolver one plugin window's markup draws through: the client's
/// own icons from the shared retail resolver, and file icons from the
/// plugin's install folder. A file is read in the background the first time
/// the window draws it, and draws nothing until it is ready. The image is
/// then held in the plugin's image table, under the plugin's image budget,
/// for as long as this window holds it: the window lets go when it is
/// unmounted (closed for good, reloaded, or its plugin unloaded), and two
/// windows showing the same file each hold it, so one window going does not
/// take the image from the other. Used from the interface thread only.
/// </summary>
internal sealed class PluginMarkupIconResolver(
    IMarkupIconResolver retail,
    PluginFileImageLoader files,
    Func<PluginImageTable?> imageTable,
    string pluginId,
    string? pluginDirectory) : IMarkupIconResolver, IDisposable
{
    /// <summary>A bound path can name anything; past this many, every hold is let go and the paths asked again.</summary>
    private const int MaximumRemembered = 4096;

    private sealed class FileIcon(Task<PluginFileImageResult> reading)
    {
        /// <summary>The read, until its result has been taken; then null.</summary>
        internal Task<PluginFileImageResult>? Reading { get; set; } = reading;

        /// <summary>The table holding the image for this window, once it does.</summary>
        internal PluginImageTable? Table { get; set; }

        internal PluginImageHandle Handle { get; set; }
    }

    private readonly Dictionary<string, FileIcon> _files = new(StringComparer.Ordinal);
    private bool _disposed;

    public (uint tex, int w, int h) ResolveDid(uint did) => retail.ResolveDid(did);

    public (uint tex, int w, int h) ResolveSpell(uint spellId) => retail.ResolveSpell(spellId);

    public (uint tex, int w, int h) ResolveItem(uint objectId) => retail.ResolveItem(objectId);

    public (uint tex, int w, int h) ResolveFile(string path)
    {
        if (_disposed)
            return (0u, 0, 0);
        if (pluginDirectory is null)
        {
            files.Report(pluginId, "*", "file icons need a plugin folder, and this window has no plugin folder");
            return (0u, 0, 0);
        }

        if (!_files.TryGetValue(path, out FileIcon? icon))
        {
            if (_files.Count >= MaximumRemembered)
                ReleaseAll();
            icon = new FileIcon(files.Load(pluginDirectory, path));
            _files[path] = icon;
        }

        if (icon.Reading is { IsCompleted: true } reading)
        {
            icon.Reading = null;
            PluginFileImageResult result = reading.IsCompletedSuccessfully
                ? reading.Result
                : new PluginFileImageResult(null, "could not be read: " + reading.Exception?.GetBaseException().Message);
            if (result.Image is { } image)
            {
                if (imageTable() is { } table)
                {
                    icon.Handle = table.AcquireFile(image);
                    icon.Table = table;
                }
            }
            else
            {
                files.Report(pluginId, path, result.Reason ?? "could not be read");
            }
        }

        return icon.Table is { } held && held.TryResolve(icon.Handle, out uint texture, out int width, out int height)
            ? (texture, width, height)
            : (0u, 0, 0);
    }

    /// <summary>Lets go of every image this window holds. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ReleaseAll();
    }

    private void ReleaseAll()
    {
        foreach (FileIcon icon in _files.Values)
            icon.Table?.Release(icon.Handle);
        _files.Clear();
    }
}
