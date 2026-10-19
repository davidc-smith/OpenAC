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
    private readonly Func<string> _serverName;
    private readonly Func<InventoryView?> _inventory;

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
        _serverName = () => Guard(nameof(SessionStateRegistry.LastServerName), () => _view.WorldName, string.Empty);
        _inventory = () => Guard(nameof(InventoryModel.Current), BuildInventory, null);

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
        SessionStateRegistry.ServerName = _serverName;
        InventoryModel.Source = _inventory;
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
        if (SessionStateRegistry.ServerName == _serverName) SessionStateRegistry.ServerName = null;
        if (InventoryModel.Source == _inventory)
        {
            InventoryModel.Source = null;
            InventoryModel.Reset();
        }
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

    /// <summary>
    /// The player's carried items as RynthCore's InventoryView: the main pack, each side pack
    /// in it, and what the player wears. Null when there is no player.
    /// </summary>
    private InventoryView? BuildInventory()
    {
        uint player = _view.PlayerId;
        if (player == 0)
            return null;

        var main = new List<InventoryItem>();
        var worn = new List<InventoryItem>();
        var packs = new List<InventoryPack>();
        foreach (uint id in _view.GetContainerContents(player))
        {
            if (!_view.TryGetObject(id, out RynthObjectInfo info))
                continue;
            InventoryItem item = ToInventoryItem(id, info);
            if (info.WielderId == player)
            {
                worn.Add(item);
                continue;
            }

            main.Add(item);
            if (info.ItemsCapacity > 0)
            {
                var contents = new List<InventoryItem>();
                foreach (uint inner in _view.GetContainerContents(id))
                    if (_view.TryGetObject(inner, out RynthObjectInfo innerInfo))
                        contents.Add(ToInventoryItem(inner, innerInfo));
                packs.Add(Pack(id, item.Name, info.ItemsCapacity, isMain: false, contents));
            }
        }

        _view.TryGetObjectName(player, out string playerName);
        packs.Insert(0, Pack(player, playerName ?? string.Empty, 102, isMain: true, main));
        int count = 0;
        foreach (InventoryPack pack in packs)
            count += pack.Items.Length;
        return new InventoryView
        {
            PlayerId = player,
            PlayerName = playerName ?? string.Empty,
            Packs = [.. packs],
            Worn = [.. worn],
            ItemCount = count,
            ItemCountText = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            WornCountText = worn.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private static InventoryPack Pack(uint id, string name, int capacity, bool isMain, List<InventoryItem> items) => new()
    {
        Id = id,
        Name = name,
        Capacity = capacity,
        IsMain = isMain,
        Items = [.. items],
        Used = items.Count,
        FillText = $"{items.Count}/{capacity}",
    };

    private InventoryItem ToInventoryItem(uint id, RynthObjectInfo info)
    {
        _view.TryGetObjectName(id, out string name);
        name ??= string.Empty;
        return new InventoryItem
        {
            Id = id,
            Container = info.ContainerId,
            Wielder = info.WielderId,
            Location = info.EquipLocation,
            Type = info.ItemType,
            Value = (uint)Math.Max(0, info.Value),
            Icon = info.IconId,
            Wcid = info.Wcid,
            SpellId = info.SpellId ?? 0,
            Bitfield = info.Bitfield,
            Burden = info.Burden,
            Stack = Math.Max(1, info.StackSize),
            MaxStack = info.StackSizeMax,
            ItemsCapacity = info.ItemsCapacity,
            MaterialType = (int)(info.MaterialType ?? 0),
            Workmanship = info.Workmanship,
            Name = name,
            SearchName = name.ToLowerInvariant(),
            StackText = info.StackSize > 1 ? info.StackSize.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty,
        };
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
