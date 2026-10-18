using AcDream.App.Plugins;
using AcDream.ClientExtensions;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// The overlay's icons: the client interface's own icon services (bound once
/// the interface exists), answered as ImGui texture ids. Every handle handed
/// out is remembered so the renderer draws it and nothing else. Icon and spell
/// answers are memoised here, hits and misses, once a backend is bound: the
/// script-window replay asks every frame and each backend call reads the dats
/// under their lock. That is safe because issued handles are never released
/// while the session runs. Object icons are not memoised: an object's icon,
/// overlay and effects can change, and that path is cheap. Game thread.
/// </summary>
internal sealed class OverlayIcons : IClientOverlayIcons
{
    private readonly Action<string> _log;
    private readonly HashSet<uint> _issued = new();
    private readonly HashSet<(char Kind, uint Id)> _reported = new();
    private readonly Dictionary<(char Kind, uint Id), IntPtr> _memo = new();
    private IPluginImageBackend? _backend;
    private bool _reportedRebind;

    public OverlayIcons(Action<string> log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>Binds the interface's icon services; only the first call counts.</summary>
    public void Bind(IPluginImageBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (_backend is null)
        {
            _backend = backend;
            return;
        }

        if (!_reportedRebind)
        {
            _reportedRebind = true;
            _log("[ImGui] overlay icons are already bound; keeping the first.");
        }
    }

    public IntPtr Icon(uint iconId)
    {
        uint id = PluginIcons.Normalize(iconId);
        return id == 0 ? IntPtr.Zero : Look('i', id, true, (IPluginImageBackend b, out uint t) => b.TryGetClientArt(id, out t, out _, out _));
    }

    public IntPtr SpellIcon(uint spellId) =>
        Look('s', spellId, true, (IPluginImageBackend b, out uint t) => b.TryGetSpellIcon(spellId, out t, out _, out _));

    public IntPtr ObjectIcon(uint objectId) =>
        Look('o', objectId, false, (IPluginImageBackend b, out uint t) => b.TryGetObjectIcon(objectId, out t, out _, out _));

    /// <summary>True for a texture id this service handed out.</summary>
    public bool IsIssued(IntPtr textureId)
    {
        long value = textureId.ToInt64();
        return value > 0 && value <= uint.MaxValue && _issued.Contains((uint)value);
    }

    private delegate bool Lookup(IPluginImageBackend backend, out uint texture);

    private IntPtr Look(char kind, uint id, bool memoise, Lookup lookup)
    {
        if (_backend is not { } backend)
            return IntPtr.Zero;
        if (memoise && _memo.TryGetValue((kind, id), out IntPtr known))
            return known;
        IntPtr answer = IntPtr.Zero;
        try
        {
            if (lookup(backend, out uint texture) && texture != 0)
            {
                _issued.Add(texture);
                answer = (IntPtr)texture;
            }
        }
        catch (Exception error)
        {
            if (_reported.Add((kind, id)))
                _log($"[ImGui] icon lookup failed ({kind} 0x{id:X8}): {error.GetType().Name}: {error.Message}");
        }

        if (memoise)
            _memo[(kind, id)] = answer;
        return answer;
    }
}
