using AcDream.ClientExtensions;
using RynthCore.Engine;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.Net;

namespace AcDream.Rynth.Host;

/// <summary>
/// Points the acclient hooks RynthCore's InventoryModel and Inventory face call (item
/// fields, properties, appraisals, the client's item gate, the item actions, the order of
/// each pack, retail pictures) at this game view, and unpoints them on Dispose (only the
/// ones still pointing here). Every answer from the view is guarded: a failure logs and
/// answers empty.
/// </summary>
internal sealed class RynthInventoryHookup : IDisposable
{
    /// <summary>ObjectDescriptionFlag.RequiresPackSlot (their InventoryItem.RequiresPackSlotFlag).</summary>
    private const uint RequiresPackSlot = InventoryItem.RequiresPackSlotFlag;

    private readonly IRynthGameView _view;
    private readonly Action<string> _log;
    private readonly ClientObjectHooks.ItemFieldsReader _itemFields;
    private readonly ClientObjectHooks.IntPropertyReader _intProperty;
    private readonly ClientObjectHooks.AttributeReader _attribute;
    private readonly Func<uint, IReadOnlyList<uint>> _contents;
    private readonly AppraisalHooks.AppraisalReader _appraisal;
    private readonly Func<uint, bool> _identify;
    private readonly Func<uint, bool> _select;
    private readonly ClientActionGates.PendingReader _pending;
    private readonly Func<bool> _attacking;
    private readonly Func<(int, uint, int, uint, uint, uint)> _replies;
    private readonly Func<uint, bool> _use;
    private readonly Func<uint, uint, int, int, bool> _move;
    private readonly Func<uint, uint, bool> _wield;
    private readonly Func<uint, bool> _drop;
    private readonly Func<uint, uint, int, bool> _give;
    private readonly Func<uint, bool> _containerFull;
    private readonly Func<uint, uint, int> _firstEmptySlot;
    private readonly Func<uint, IReadOnlyList<InventoryOrder.Placed>?> _order;
    private readonly Func<ulong> _revision;
    private readonly Func<byte[], int, int, IntPtr>? _upload;
    private readonly Func<IntPtr, bool>? _release;
    private ulong _lastRevision;

    public RynthInventoryHookup(IRynthGameView view, IClientOverlayIcons? icons, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        _view = view;
        _log = log;

        _itemFields = ReadItemFields;
        _intProperty = ReadIntProperty;
        _attribute = ReadAttribute;
        _contents = id => Guard(nameof(ClientObjectHooks.CollectOwnedIdsFromSnapshot), () => _view.GetContainerContents(id), (IReadOnlyList<uint>)[]);
        _appraisal = ReadAppraisal;
        _identify = id => Guard(nameof(ClientActionHooks.RequestId), () => _view.RequestIdentify(id), false);
        _select = id => Guard(nameof(AcMainThreadQueue.EnqueueSetSelectedObject), () => _view.SelectObject(id), false);
        _pending = ReadPending;
        _attacking = () => Guard(nameof(ClientActionGates.TryReadAttacking), () => _view.IsAttackInProgress, false);
        _replies = () => Guard(nameof(SmartBoxHooks), () =>
        {
            AcDream.Core.Items.ServerReplySnapshot r = _view.ServerReplies;
            return (r.UseDoneSequence, r.LastUseDoneError, r.WeenieErrorSequence, r.LastWeenieError,
                r.LastWeenieErrorEventType, r.LastWeenieErrorObjectId);
        }, default);
        _use = id => Guard(nameof(ClientHelperHooks.UseObject), () => _view.UseObject(id), false);
        _move = (item, container, slot, amount) =>
            Guard(nameof(ClientHelperHooks.Move), () => _view.MoveItem(item, container, slot, amount), false);
        _wield = (item, mask) => Guard(nameof(ClientHelperHooks.WieldItem), () => _view.WieldItem(item, mask), false);
        _drop = item => Guard(nameof(ClientHelperHooks.DropItem), () => _view.MoveItemExternal(item, 0, 0), false);
        _give = (item, target, amount) =>
            Guard(nameof(ClientHelperHooks.GiveObjectTo), () => _view.GiveItem(item, target, (uint)Math.Max(0, amount)), false);
        _containerFull = target => Guard(nameof(ClientHelperHooks.IsOwnedContainerFull), () => IsFull(target), false);
        _firstEmptySlot = (container, moving) =>
            Guard(nameof(ClientHelperHooks.FirstEmptySlot), () => FirstEmptySlot(container, moving), 0);
        _order = container => Guard(nameof(InventoryOrder.TryGetOrder), () => Order(container), null);
        _revision = () => Guard(nameof(InventoryOrder.Version), () => _view.InventoryRevision, 0ul);
        if (icons is not null)
        {
            _upload = (rgba, width, height) =>
                Guard(nameof(DX9Backend.CreateTextureRgba), () => icons.Upload(rgba, width, height, "rynth retail"), IntPtr.Zero);
            _release = texture => Guard(nameof(DX9Backend.ReleaseTexture), () => icons.Release(texture), false);
        }

        _lastRevision = _revision();

        ClientObjectHooks.ItemFields = _itemFields;
        ClientObjectHooks.IntProperty = _intProperty;
        ClientObjectHooks.Attribute = _attribute;
        ClientObjectHooks.Contents = _contents;
        AppraisalHooks.Reader = _appraisal;
        ClientActionHooks.Identify = _identify;
        AcMainThreadQueue.SelectObject = _select;
        ClientActionGates.Pending = _pending;
        ClientActionGates.Attacking = _attacking;
        SmartBoxHooks.Replies = _replies;
        ClientHelperHooks.Use = _use;
        ClientHelperHooks.Move = _move;
        ClientHelperHooks.Wield = _wield;
        ClientHelperHooks.Drop = _drop;
        ClientHelperHooks.Give = _give;
        ClientHelperHooks.ContainerFull = _containerFull;
        ClientHelperHooks.FirstEmptySlot = _firstEmptySlot;
        InventoryOrder.Source = _order;
        InventoryOrder.Revision = _revision;
        if (_upload is not null)
        {
            DX9Backend.Upload = _upload;
            DX9Backend.Release = _release;
        }
    }

    /// <summary>Game thread, every tick: the client's objects changed, so their model captures again soon.</summary>
    public void Tick()
    {
        ulong revision = _revision();
        if (revision == _lastRevision)
            return;
        _lastRevision = revision;
        InventoryModel.MarkDirty();
    }

    public void Dispose()
    {
        if (ClientObjectHooks.ItemFields == _itemFields) ClientObjectHooks.ItemFields = null;
        if (ClientObjectHooks.IntProperty == _intProperty) ClientObjectHooks.IntProperty = null;
        if (ClientObjectHooks.Attribute == _attribute) ClientObjectHooks.Attribute = null;
        if (ClientObjectHooks.Contents == _contents) ClientObjectHooks.Contents = null;
        if (AppraisalHooks.Reader == _appraisal) AppraisalHooks.Reader = null;
        if (ClientActionHooks.Identify == _identify) ClientActionHooks.Identify = null;
        if (AcMainThreadQueue.SelectObject == _select) AcMainThreadQueue.SelectObject = null;
        if (ClientActionGates.Pending == _pending) ClientActionGates.Pending = null;
        if (ClientActionGates.Attacking == _attacking) ClientActionGates.Attacking = null;
        if (SmartBoxHooks.Replies == _replies) SmartBoxHooks.Replies = null;
        if (ClientHelperHooks.Use == _use) ClientHelperHooks.Use = null;
        if (ClientHelperHooks.Move == _move) ClientHelperHooks.Move = null;
        if (ClientHelperHooks.Wield == _wield) ClientHelperHooks.Wield = null;
        if (ClientHelperHooks.Drop == _drop) ClientHelperHooks.Drop = null;
        if (ClientHelperHooks.Give == _give) ClientHelperHooks.Give = null;
        if (ClientHelperHooks.ContainerFull == _containerFull) ClientHelperHooks.ContainerFull = null;
        if (ClientHelperHooks.FirstEmptySlot == _firstEmptySlot) ClientHelperHooks.FirstEmptySlot = null;
        if (InventoryOrder.Source == _order) InventoryOrder.Source = null;
        if (InventoryOrder.Revision == _revision) InventoryOrder.Revision = null;

        // Their retail pictures go back to the overlay while our upload is still the one in use.
        EngineThreads.StoppingFlag = true;
        try { Guard(nameof(RetailSprites.Shutdown), () => { RetailSprites.Shutdown(); return true; }, false); }
        finally { EngineThreads.StoppingFlag = false; }

        if (_upload is not null && DX9Backend.Upload == _upload) DX9Backend.Upload = null;
        if (_release is not null && DX9Backend.Release == _release) DX9Backend.Release = null;
    }

    private bool ReadItemFields(uint id, out ClientObjectHooks.ItemPwdFields f)
    {
        f = default;
        RynthObjectInfo? o = Guard(nameof(ClientObjectHooks.TryReadItemFields), () => _view.TryGetObject(id, out RynthObjectInfo i) ? i : null, null);
        if (o is null) return false;
        f = new ClientObjectHooks.ItemPwdFields
        {
            Wcid = o.Wcid, Icon = o.IconId, Container = o.ContainerId, Wielder = o.WielderId,
            ValidLocations = o.ValidLocations, Location = o.EquipLocation, Type = o.ItemType, Value = (uint)o.Value,
            Useability = o.Useability, StackSize = (uint)o.StackSize, MaxStackSize = (uint)o.StackSizeMax,
            Bitfield = o.Bitfield, SpellId = o.SpellId ?? 0, ItemsCapacity = o.ItemsCapacity, Burden = o.Burden,
            MaterialType = (int)(o.MaterialType ?? 0), Workmanship = o.Workmanship,
        };
        return true;
    }

    private bool ReadIntProperty(uint id, uint stype, out int value)
    {
        int found = 0;
        bool ok = Guard(
            nameof(ClientObjectHooks.TryGetObjectIntProperty),
            () => _view.TryGetObject(id, out RynthObjectInfo o) && o.Ints.TryGetValue(stype, out found),
            false);
        value = ok ? found : 0;
        return ok;
    }

    // Only the player's attributes are known (the Skills panel's progress): buffed for raw 0, base otherwise.
    private bool ReadAttribute(uint id, uint stype, int raw, out uint value)
    {
        uint? found = Guard<uint?>(nameof(ClientObjectHooks.TryGetObjectAttribute), () =>
        {
            if (id == 0 || id != _view.PlayerId || _view.PlayerProgress is not { } progress)
                return null;
            foreach (RynthStatProgress a in progress.Attributes)
                if (a.Id == stype)
                    return raw == 0 ? a.Buffed : a.Base;
            return null;
        }, null);
        value = found ?? 0;
        return found is not null;
    }

    private bool ReadAppraisal(uint guid, out AppraisalHooks.RynthAppraisal appraisal)
    {
        AppraisalHooks.RynthAppraisal? found = Guard<AppraisalHooks.RynthAppraisal?>(
            nameof(AppraisalHooks.HasAppraisalData),
            () => _view.TryGetObject(guid, out RynthObjectInfo o)
                ? new AppraisalHooks.RynthAppraisal(o.Identified, o.AppraisedSpellIds, o.Ints, o.Bools, o.Floats)
                : null,
            null);
        appraisal = found!;
        return found is not null;
    }

    private bool ReadPending(out uint type, out uint objectId)
    {
        uint t = 0, o = 0;
        bool ok = Guard(nameof(ClientActionGates.TryReadPending), () => _view.TryGetPendingItemRequest(out t, out o), false);
        (type, objectId) = ok ? (t, o) : (0u, 0u);
        return ok;
    }

    private bool IsFull(uint target) =>
        _view.TryCountContents(target, out int items, out _)
        && _view.TryGetObject(target, out RynthObjectInfo c) && c.ItemsCapacity > 0 && items >= c.ItemsCapacity;

    private bool TakesPackSlot(RynthObjectInfo o) => (o.Bitfield & RequiresPackSlot) != 0 || o.ItemsCapacity > 0;

    // acclient's first empty slot: ACE keeps places contiguous, so the count of the pack's items.
    private int FirstEmptySlot(uint container, uint moving)
    {
        int n = 0;
        foreach (uint id in _view.GetContainerContents(container))
        {
            if (id == moving || !_view.TryGetObject(id, out RynthObjectInfo o))
                continue;
            if (o.WielderId == 0 && !TakesPackSlot(o))
                n++;
        }

        return n;
    }

    private IReadOnlyList<InventoryOrder.Placed>? Order(uint container)
    {
        if (container == 0 || (container != _view.PlayerId && !_view.TryGetObject(container, out _)))
            return null;
        var placed = new List<InventoryOrder.Placed>();
        foreach (uint id in _view.GetContainerContents(container))
        {
            if (!_view.TryGetObject(id, out RynthObjectInfo o) || o.WielderId != 0)
                continue;
            placed.Add(new InventoryOrder.Placed(id, o.ContainerSlot, TakesPackSlot(o)));
        }

        return placed;
    }

    private T Guard<T>(string what, Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception error)
        {
            _log($"[Rynth] inventory {what}: view failed: {error.GetType().Name}: {error.Message}");
            return fallback;
        }
    }
}
