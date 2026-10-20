using System.Numerics;
using RynthCore.Engine.Compatibility;

namespace AcDream.Rynth.Host;

/// <summary>
/// Points the map faces' engine stand-ins (World Map, Mini Map, Radar, the
/// Characters feed) at the game view, and unpoints them on Dispose (only the
/// ones still pointing here). Every answer is guarded: a failure logs and
/// answers no data.
/// </summary>
internal sealed class RynthMapHookup : IDisposable
{
    private readonly IRynthGameView _view;
    private readonly Action<string> _log;
    private readonly Func<uint[]> _liveIds;
    private readonly ClientObjectHooks.ObjectPositionReader _position;
    private readonly PlayerVitalsHooks.SnapshotReader _vitals;
    private readonly Func<bool> _portaling;

    public RynthMapHookup(IRynthGameView view, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        _view = view;
        _log = log;
        _liveIds = () => Guard(nameof(ClientObjectHooks.LiveObjectIds), () => _view.LiveObjectIds switch
        {
            null => [],
            uint[] ids => ids,
            var ids => ids.ToArray(),
        }, []);
        _position = TryGetPosition;
        _vitals = TryGetVitals;
        _portaling = () => Guard(nameof(TeleportStateHooks.IsPortaling), () => _view.IsPortaling, false);

        ClientObjectHooks.LiveIds = _liveIds;
        ClientObjectHooks.PositionSnapshot = _position;
        PlayerVitalsHooks.Reader = _vitals;
        TeleportStateHooks.Portaling = _portaling;
    }

    public void Dispose()
    {
        if (ClientObjectHooks.LiveIds == _liveIds) ClientObjectHooks.LiveIds = null;
        if (ClientObjectHooks.PositionSnapshot == _position) ClientObjectHooks.PositionSnapshot = null;
        if (PlayerVitalsHooks.Reader == _vitals) PlayerVitalsHooks.Reader = null;
        if (TeleportStateHooks.Portaling == _portaling) TeleportStateHooks.Portaling = null;
    }

    private bool TryGetPosition(uint objectId, out uint cell, out float x, out float y, out float z)
    {
        uint foundCell = 0;
        Vector3 origin = default;
        bool ok = Guard(nameof(ClientObjectHooks.TryGetSnapshotPosition),
            () => _view.TryGetObjectPosition(objectId, out foundCell, out origin), false);
        cell = ok ? foundCell : 0u;
        (x, y, z) = ok ? (origin.X, origin.Y, origin.Z) : (0f, 0f, 0f);
        return ok;
    }

    private bool TryGetVitals(out PlayerVitalsSnapshot snapshot)
    {
        RynthVitals v = default;
        bool ok = Guard(nameof(PlayerVitalsHooks.TryGetSnapshot), () => _view.TryGetPlayerVitals(out v), false);
        snapshot = ok ? new PlayerVitalsSnapshot(v.Health, v.MaxHealth, v.Stamina, v.MaxStamina, v.Mana, v.MaxMana) : default;
        return ok;
    }

    private T Guard<T>(string what, Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception error)
        {
            _log($"[Rynth] map {what}: view failed: {error.GetType().Name}: {error.Message}");
            return fallback;
        }
    }
}
