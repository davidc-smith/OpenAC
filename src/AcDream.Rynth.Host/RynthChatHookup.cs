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
    private readonly Func<PlayerProgress, bool> _progress;
    private readonly EnchantmentHooks.StatModCopier _statMods;
    private readonly Func<double> _serverTime;
    private readonly Func<PlayerTraining.TrainKind, uint, uint, bool> _train;

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
        _progress = ReadProgress;
        _statMods = CopyStatMods;
        _serverTime = () => Guard(nameof(TimeSyncHooks.GetCurrentServerTime), () => _view.ServerTime, 0d);
        _train = (kind, stype, amount) => Guard(
            nameof(PlayerTraining.Raise), () => _view.RequestAdvancement((int)kind, stype, amount), false);

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
        AccountHooks.WorldName = _serverName;
        ClientObjectHooks.ProgressReader = _progress;
        EnchantmentHooks.Copier = _statMods;
        TimeSyncHooks.ServerTime = _serverTime;
        PlayerTraining.Sender = _train;
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
        if (AccountHooks.WorldName == _serverName) AccountHooks.WorldName = null;
        if (ClientObjectHooks.ProgressReader == _progress) ClientObjectHooks.ProgressReader = null;
        if (EnchantmentHooks.Copier == _statMods) EnchantmentHooks.Copier = null;
        if (TimeSyncHooks.ServerTime == _serverTime) TimeSyncHooks.ServerTime = null;
        if (PlayerTraining.Sender == _train) PlayerTraining.Sender = null;
    }

    // As RynthCore's ReadPlayerProgressLive fills its work buffer: indexed by skill id, attribute 1..6, vital 1..3.
    private bool ReadProgress(PlayerProgress dst)
    {
        RynthPlayerProgress? read = Guard(nameof(ClientObjectHooks.ReadPlayerProgressLive), () => _view.PlayerProgress, null);
        if (read is null)
            return false;
        dst.Clear();
        dst.PlayerId = Guard(nameof(ClientHelperHooks.GetPlayerId), () => _view.PlayerId, 0u);
        dst.SkillTableRead = true;
        foreach (RynthSkillProgress s in read.Skills)
        {
            if (s.SkillId is 0 or > PlayerProgress.MaxSkill)
                continue;
            int i = (int)s.SkillId;
            dst.SkillInTable[i] = true;
            dst.SkillClass[i] = s.Class;
            dst.SkillXpSpent[i] = s.XpSpent;
            dst.SkillRanks[i] = s.Ranks;
            dst.SkillInit[i] = s.Init;
            dst.SkillLevelKnown[i] = s.LevelKnown;
            dst.SkillBuffed[i] = s.Buffed;
            dst.SkillBase[i] = s.Base;
        }

        dst.AttributeCacheRead = read.Attributes.Count > 0;
        foreach (RynthStatProgress a in read.Attributes)
        {
            if (a.Id is 0 or > 6)
                continue;
            int i = (int)a.Id;
            (dst.AttrRanks[i], dst.AttrInit[i], dst.AttrXpSpent[i], dst.AttrBuffed[i], dst.AttrBase[i]) = (a.Ranks, a.Init, a.XpSpent, a.Buffed, a.Base);
        }

        foreach (RynthStatProgress v in read.Vitals)
        {
            if (v.Id is 0 or > 3)
                continue;
            int i = (int)v.Id;
            (dst.VitalRanks[i], dst.VitalInit[i], dst.VitalXpSpent[i], dst.VitalBuffed[i], dst.VitalBase[i]) = (v.Ranks, v.Init, v.XpSpent, v.Buffed, v.Base);
        }

        (dst.Level, dst.SkillCredits, dst.TotalXp, dst.UnassignedXp, dst.Luminance, dst.MaxLuminance, dst.Vitae) =
            (read.Level, read.SkillCredits, read.TotalXp, read.UnassignedXp, read.Luminance, read.MaxLuminance, read.Vitae);
        return true;
    }

    private int CopyStatMods(uint[] spell, double[] expiry, uint[] type, uint[] key, float[] value, uint[] category)
    {
        IReadOnlyList<RynthStatMod>? mods = Guard(nameof(EnchantmentHooks.CopyPlayerStatMods), () => _view.PlayerStatMods, null);
        if (mods is null)
            return 0;
        int n = Math.Min(mods.Count, spell.Length);
        for (int i = 0; i < n; i++)
            (spell[i], expiry[i], type[i], key[i], value[i], category[i]) =
                (mods[i].SpellId, mods[i].ExpiryServerTime, mods[i].Type, mods[i].Key, mods[i].Value, mods[i].Category);
        return n;
    }

    private IntPtr Resolve(string pluginNamePart, string exportName)
    {
        foreach (RynthPlugin plugin in RynthPluginLookup.Candidates(_host.Plugins, pluginNamePart))
        {
            if (plugin.TryGetExport(exportName, out IntPtr pointer))
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
