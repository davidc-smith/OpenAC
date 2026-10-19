using RynthCore.Engine.Compatibility;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host;

/// <summary>
/// Points the chat window's engine stand-ins at this host and game view, and
/// unpoints them on Dispose (only the ones still pointing here). Every answer
/// from the view is guarded: a failure logs and answers empty.
/// </summary>
internal sealed class RynthChatHookup : IDisposable
{
    private readonly RynthHost _host;
    private readonly IRynthGameView _view;
    private readonly Action<string> _log;
    private readonly Func<string, string, IntPtr> _resolver;
    private readonly Func<uint> _playerId;
    private readonly Func<uint> _selectedId;
    private readonly ClientObjectHooks.NameReader _name;
    private readonly ClientObjectHooks.ItemTypeReader _itemType;
    private readonly Func<string> _characterName;
    private readonly ClientObjectHooks.FactsReader _facts;
    private readonly ClientObjectHooks.InstanceIdReader _instanceId;
    private readonly Func<uint, bool> _attackable;
    private readonly PlayerPhysicsHooks.PositionReader _position;

    public RynthChatHookup(RynthHost host, IRynthGameView view, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        _host = host;
        _view = view;
        _log = log;
        _resolver = Resolve;
        _playerId = () => Guard(nameof(ClientHelperHooks.GetPlayerId), () => _view.PlayerId, 0u);
        _selectedId = () => Guard(nameof(SelectedTargetHooks.ReadCurrentSelectedId), () => _view.SelectedObjectId, 0u);
        _name = TryGetName;
        _itemType = TryGetItemType;
        _characterName = () => Guard(
            nameof(SessionStateRegistry.LastCharacterName),
            () => _view.TryGetObjectName(_view.PlayerId, out string name) ? name : string.Empty,
            string.Empty);

        _facts = TryGetFacts;
        _instanceId = TryGetInstanceId;
        _attackable = id => Guard(nameof(ClientObjectHooks.TryGetSnapshotAttackable), () => _view.IsAttackable(id), false);
        _position = TryGetPlayerPosition;

        PluginExportBinder.Resolver = _resolver;
        ClientHelperHooks.PlayerId = _playerId;
        SelectedTargetHooks.SelectedId = _selectedId;
        ClientObjectHooks.Name = _name;
        ClientObjectHooks.ItemType = _itemType;
        SessionStateRegistry.CharacterName = _characterName;
        ClientObjectHooks.Facts = _facts;
        ClientObjectHooks.InstanceId = _instanceId;
        ClientObjectHooks.Attackable = _attackable;
        PlayerPhysicsHooks.Position = _position;
    }

    public void Dispose()
    {
        if (PluginExportBinder.Resolver == _resolver) PluginExportBinder.Resolver = null;
        if (ClientHelperHooks.PlayerId == _playerId) ClientHelperHooks.PlayerId = null;
        if (SelectedTargetHooks.SelectedId == _selectedId) SelectedTargetHooks.SelectedId = null;
        if (ClientObjectHooks.Name == _name) ClientObjectHooks.Name = null;
        if (ClientObjectHooks.ItemType == _itemType) ClientObjectHooks.ItemType = null;
        if (SessionStateRegistry.CharacterName == _characterName) SessionStateRegistry.CharacterName = null;
        if (ClientObjectHooks.Facts == _facts) ClientObjectHooks.Facts = null;
        if (ClientObjectHooks.InstanceId == _instanceId) ClientObjectHooks.InstanceId = null;
        if (ClientObjectHooks.Attackable == _attackable) ClientObjectHooks.Attackable = null;
        if (PlayerPhysicsHooks.Position == _position) PlayerPhysicsHooks.Position = null;
    }

    private IntPtr Resolve(string pluginNamePart, string exportName)
    {
        foreach (RynthPlugin plugin in _host.Plugins)
        {
            if (plugin.Name.Contains(pluginNamePart, StringComparison.OrdinalIgnoreCase)
                && plugin.TryGetExport(exportName, out IntPtr pointer))
                return pointer;
        }

        return IntPtr.Zero;
    }

    private bool TryGetName(uint objectId, out string name)
    {
        string found = string.Empty;
        bool ok = Guard(nameof(ClientObjectHooks.TryGetObjectName), () => _view.TryGetObjectName(objectId, out found), false);
        name = ok ? found : string.Empty;
        return ok;
    }

    private bool TryGetItemType(uint objectId, out uint typeFlags)
    {
        RynthObjectInfo? info = null;
        bool ok = Guard(nameof(ClientObjectHooks.TryGetItemType), () => _view.TryGetObject(objectId, out info), false);
        typeFlags = ok && info is not null ? info.ItemType : 0u;
        return ok && info is not null;
    }

    private bool TryGetFacts(uint objectId, out uint wcid, out uint bitfield, out uint containerId, out uint wielderId)
    {
        RynthObjectInfo? info = null;
        bool ok = Guard(nameof(ClientObjectHooks.TryGetSnapshotPwdInfo), () => _view.TryGetObject(objectId, out info), false) && info is not null;
        wcid = ok ? info!.Wcid : 0u;
        bitfield = ok ? info!.Bitfield : 0u;
        containerId = ok ? info!.ContainerId : 0u;
        wielderId = ok ? info!.WielderId : 0u;
        return ok;
    }

    private bool TryGetInstanceId(uint objectId, uint stat, out uint value)
    {
        RynthObjectInfo? info = null;
        uint found = 0;
        bool ok = Guard(
            nameof(ClientObjectHooks.TryGetObjectInstanceIdProperty),
            () => _view.TryGetObject(objectId, out info) && info is not null && RynthObjectProperties.TryGetInstanceId(info, stat, out found),
            false);
        value = ok ? found : 0u;
        return ok;
    }

    private bool TryGetPlayerPosition(out uint cell, out float x, out float y, out float z)
    {
        uint foundCell = 0;
        System.Numerics.Vector3 origin = default;
        bool ok = Guard(
            nameof(PlayerPhysicsHooks.TryGetPlayerPoseSnapshot),
            () => _view.TryGetObjectPosition(_view.PlayerId, out foundCell, out origin),
            false);
        cell = ok ? foundCell : 0u;
        (x, y, z) = ok ? (origin.X, origin.Y, origin.Z) : (0f, 0f, 0f);
        return ok;
    }

    private T Guard<T>(string what, Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception error)
        {
            _log($"[Rynth] chat {what}: view failed: {error.GetType().Name}: {error.Message}");
            return fallback;
        }
    }
}
