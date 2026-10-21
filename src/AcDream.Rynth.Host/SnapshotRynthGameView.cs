using System.Diagnostics;
using System.Numerics;
using AcDream.Core.Items;
using AcDream.Plugin.Abstractions;
using AcDream.Rynth.EngineUi;

namespace AcDream.Rynth.Host;

/// <summary>
/// The view off the frame thread: every read answers from one published snapshot, every action
/// goes to the frame thread's queue and answers "queued" (RynthCore's AcMainThreadQueue).
/// <paramref name="live"/> is only touched inside queued work (which runs on the frame thread)
/// and for the two reads backed by concurrent dictionaries.
/// </summary>
public sealed class SnapshotRynthGameView(
    RynthWorldSnapshot snapshot, RynthActionQueue queue, IRynthGameView live, Action<string> onVendorQueuedFailure)
    : IRynthGameView
{
    private readonly RynthWorldSnapshot _s = snapshot;

    // ── Reads ────────────────────────────────────────────────────────────

    public uint PlayerId => _s.PlayerId;
    public uint SelectedObjectId => _s.SelectedObjectId;
    public string AccountName => _s.AccountName;
    public string WorldName => _s.WorldName;
    public bool IsPortaling => _s.IsPortaling;
    public int BusyCount => _s.BusyCount;
    public int CombatMode => _s.CombatMode;
    public bool IsAttackInProgress => _s.IsAttackInProgress;
    public bool IsCastGestureActive => _s.IsCastGestureActive;
    public bool AutoRunActive => _s.AutoRunActive;
    public PluginMovementIntent? CurrentMovementIntent => _s.MovementIntent;
    public uint OpenContainerId => _s.OpenContainerId;
    public ServerReplySnapshot ServerReplies => _s.ServerReplies;
    public double ServerTime => _s.ServerTimeAt(Stopwatch.GetTimestamp());
    public ulong InventoryRevision => _s.Objects.Revision;
    public IReadOnlyList<uint>? LiveObjectIds => _s.Objects.LiveObjectIds;
    public RynthVendor? Vendor => _s.Vendor;
    public RynthTradeState? Trade => _s.Trade;
    // The live view's answer with no surface is 1; the spellbook part always carries the live answer.
    public float VitaeMultiplier => _s.Spellbook?.VitaeMultiplier ?? 1f;
    public IReadOnlyList<RynthEnchantment>? PlayerEnchantments => _s.Spellbook?.Enchantments;
    public IReadOnlyList<RynthStatMod>? PlayerStatMods => _s.Spellbook?.StatMods;
    public IReadOnlyList<uint>? KnownSpells => _s.Spellbook?.KnownSpells;
    public RynthPlayerProgress? PlayerProgress => _s.Character?.Progress;

    public int IsSpellKnown(uint objectId, uint spellId) =>
        !IsPlayer(objectId) || _s.Spellbook is not { SpellsAvailable: true } sb ? -1 : sb.KnownSet.Contains(spellId) ? 1 : 0;

    public bool TryGetSkill(uint objectId, uint skillId, out int buffed, out int baseLevel, out int training)
    {
        if (IsPlayer(objectId) && _s.Character is { } c && c.Skills.TryGetValue(skillId, out var s))
        {
            (buffed, baseLevel, training) = s;
            return true;
        }
        buffed = baseLevel = training = 0;
        return false;
    }

    public bool TryGetAttribute(uint objectId, uint attributeId, out uint buffed, out uint baseLevel)
    {
        if (IsPlayer(objectId) && _s.Character is { } c && c.Attributes.TryGetValue(attributeId, out var a))
        {
            (buffed, baseLevel) = a;
            return true;
        }
        buffed = baseLevel = 0;
        return false;
    }

    public bool TryGetVitalBase(uint objectId, uint vitalStatId, out uint baseMaximum)
    {
        if (IsPlayer(objectId) && _s.Character is { } c && c.VitalBases.TryGetValue(vitalStatId, out baseMaximum))
            return true;
        baseMaximum = 0;
        return false;
    }

    public bool TryGetPlayerVitals(out RynthVitals vitals)
    {
        if (_s.Character is { HasVitals: true } c)
        {
            vitals = c.Vitals;
            return true;
        }
        vitals = default;
        return false;
    }

    public bool TryGetPlayerBaseVitals(out uint health, out uint stamina, out uint mana)
    {
        if (_s.Character is { HasBaseVitals: true } c)
        {
            (health, stamina, mana) = (c.BaseHealth, c.BaseStamina, c.BaseMana);
            return true;
        }
        health = stamina = mana = 0;
        return false;
    }

    public bool TryGetPlayerHeading(out float headingDegrees)
    {
        headingDegrees = _s.HasHeading ? _s.Heading : 0;
        return _s.HasHeading;
    }

    public bool TryGetPendingItemRequest(out uint acType, out uint objectId)
    {
        (acType, objectId) = _s.HasPendingItemRequest ? (_s.PendingAcType, _s.PendingObjectId) : (0u, 0u);
        return _s.HasPendingItemRequest;
    }

    public bool TryGetPlayerPose(out uint cellId, out Vector3 origin, out Quaternion orientation)
    {
        if (_s.PlayerId != 0 && _s.Entities.TryGetValue(_s.PlayerId, out RynthEntityPose p) && p.Placed)
        {
            (cellId, origin, orientation) = (p.CellId, p.Origin, p.Orientation);
            return true;
        }
        (cellId, origin, orientation) = (0, default, Quaternion.Identity);
        return false;
    }

    public bool TryGetObjectPosition(uint objectId, out uint cellId, out Vector3 origin)
    {
        if (_s.Entities.TryGetValue(objectId, out RynthEntityPose p) && p.Placed)
        {
            (cellId, origin) = (p.CellId, p.Origin);
            return true;
        }
        (cellId, origin) = (0, default);
        return false;
    }

    // The live view's own answer (the surface's object projection), captured at publish.
    public bool TryGetObjectHeading(uint objectId, out float headingDegrees)
    {
        if (_s.Entities.TryGetValue(objectId, out RynthEntityPose p) && p.HasHeading)
        {
            headingDegrees = p.Heading;
            return true;
        }
        headingDegrees = 0;
        return false;
    }

    public bool TryGetObjectState(uint objectId, out uint state)
    {
        bool found = _s.Entities.TryGetValue(objectId, out RynthEntityPose p);
        state = found ? p.RawState : 0;
        return found;
    }

    public bool TryGetObjectMotionOn(uint objectId, out bool isOn)
    {
        if (_s.Entities.TryGetValue(objectId, out RynthEntityPose p) && p.MotionOn is { } on)
        {
            isOn = on;
            return true;
        }
        isOn = false;
        return false;
    }

    // As the live view: an entity's swaps; an object the table knows but no entity, none; else unknown.
    public IReadOnlyList<(uint SubPaletteId, uint Offset)>? GetPalettes(uint objectId) =>
        _s.Entities.TryGetValue(objectId, out RynthEntityPose p) ? p.Palettes
        : _s.Objects.TryGet(objectId, out _) ? []
        : null;

    public bool TryGetObject(uint objectId, out RynthObjectInfo info)
    {
        if (_s.Objects.TryGet(objectId, out RynthObjectEntry e))
        {
            info = e.Info;
            return true;
        }
        info = null!;
        return false;
    }

    public bool TryGetObjectName(uint objectId, out string name)
    {
        name = _s.Objects.TryGet(objectId, out RynthObjectEntry e) ? e.Name : string.Empty;
        return name.Length > 0;
    }

    public bool TryGetInt64Property(uint objectId, uint propertyId, out long value)
    {
        if (_s.Objects.TryGet(objectId, out RynthObjectEntry e) && e.Int64s.TryGetValue(propertyId, out value))
            return true;
        value = 0;
        return false;
    }

    public bool IsOwnedItem(uint objectId) => _s.Objects.TryGet(objectId, out RynthObjectEntry e) && e.IsOwnedItem;

    public bool IsOwnedTinkeringTool(uint objectId) =>
        _s.Objects.TryGet(objectId, out RynthObjectEntry e) && e.IsOwnedTinkeringTool;

    // As the live view's policy: nothing before login; target 0 and the player itself always, known or not.
    public bool IsAttackable(uint objectId) =>
        _s.PlayerId != 0
        && (objectId == 0 || objectId == _s.PlayerId
            || _s.Objects.TryGet(objectId, out RynthObjectEntry e) && e.IsAttackable);

    // The player's follow the spellbook part (refreshed each second); the object part is shared on idle frames.
    public IReadOnlyList<RynthEnchantment>? GetObjectEnchantments(uint objectId) =>
        IsPlayer(objectId) ? _s.Spellbook?.PlayerObjectEnchantments
        : _s.Objects.TryGet(objectId, out RynthObjectEntry e) ? e.Enchantments
        : null;

    public IReadOnlyList<uint> GetContainerContents(uint containerId) => _s.Objects.Contents(containerId);

    public bool TryCountContents(uint containerId, out int items, out int containers) =>
        _s.Objects.TryCountContents(containerId, out items, out containers);

    // Concurrent dictionaries: read live from any thread.
    public bool TryGetTargetVitals(uint objectId, out RynthVitals vitals) => live.TryGetTargetVitals(objectId, out vitals);

    public bool TryGetHealthRatio(uint objectId, out float ratio) => live.TryGetHealthRatio(objectId, out ratio);

    public bool TryWorldToScreen(float x, float y, float z, out float screenX, out float screenY)
    {
        screenX = screenY = 0f;
        if (_s.Projection is not { } projection || !TryGetPlayerPose(out uint cell, out _, out _))
            return false;
        if (!projection.TryProject(RuntimeRynthGameView.ToNavigationPosition(cell, x, y, z), out Vector2 point))
            return false;
        (screenX, screenY) = (point.X, point.Y);
        return true;
    }

    public bool TryGetViewportSize(out uint width, out uint height)
    {
        (width, height) = _s.HasViewport ? (_s.ViewportWidth, _s.ViewportHeight) : (0u, 0u);
        return _s.HasViewport;
    }

    public bool TryGetRadarRect(out int x0, out int y0, out int x1, out int y1)
    {
        (x0, y0, x1, y1) = _s.HasRadar ? _s.Radar : (0, 0, 0, 0);
        return _s.HasRadar;
    }

    // RuntimeRynthGameView.IsPlayer: object 0 is never the player.
    private bool IsPlayer(uint objectId) => objectId != 0 && objectId == _s.PlayerId;

    // ── Actions: queued, run on the live view at the frame thread's drain ─

    private bool Start(string label, Func<bool> work) => queue.TryEnqueue(RynthActionKind.Start, label, work);

    private bool Stop(string label, Func<bool> work) => queue.TryEnqueue(RynthActionKind.Stop, label, work);

    public bool SelectObject(uint objectId) => Start("SelectObject", () => live.SelectObject(objectId));
    public bool UseObject(uint objectId) => Start("UseObject", () => live.UseObject(objectId));
    public bool RequestIdentify(uint objectId) => Start("RequestIdentify", () => live.RequestIdentify(objectId));
    public bool QueryHealth(uint objectId) => Start("QueryHealth", () => live.QueryHealth(objectId));
    public bool ChangeCombatMode(int rynthMode) => Start("ChangeCombatMode", () => live.ChangeCombatMode(rynthMode));

    public bool MoveItem(uint objectId, uint containerId, int slot, int amount) =>
        Start("MoveItem", () => live.MoveItem(objectId, containerId, slot, amount));

    public bool MergeStack(uint sourceObjectId, uint targetObjectId) =>
        Start("MergeStack", () => live.MergeStack(sourceObjectId, targetObjectId));

    public void ResetBusyCount() => Stop("ResetBusyCount", () => { live.ResetBusyCount(); return true; });
    public bool WieldItem(uint objectId, uint equipMask) => Start("WieldItem", () => live.WieldItem(objectId, equipMask));

    // Refused on the pump while the snapshot says portalling (spec 3).
    public bool CastSpell(uint targetId, uint spellId) =>
        !_s.IsPortaling && Start("CastSpell", () => live.CastSpell(targetId, spellId));

    public bool MeleeAttack(uint targetId, int height, float power) =>
        Start("MeleeAttack", () => live.MeleeAttack(targetId, height, power));

    public bool MissileAttack(uint targetId, int height, float accuracy) =>
        Start("MissileAttack", () => live.MissileAttack(targetId, height, accuracy));

    public bool CancelAttack() => Stop("CancelAttack", live.CancelAttack);

    public bool UseObjectOn(uint sourceObjectId, uint targetObjectId) =>
        Start("UseObjectOn", () => live.UseObjectOn(sourceObjectId, targetObjectId));

    public bool SetAutoRun(bool enabled) => enabled
        ? Start("SetAutoRun", () => live.SetAutoRun(true))
        : Stop("SetAutoRun(false)", () => live.SetAutoRun(false));

    public bool SnapHeading(float headingDegrees) => Start("SnapHeading", () => live.SnapHeading(headingDegrees));

    public bool SetMovementIntent(in PluginMovementIntent intent)
    {
        PluginMovementIntent copy = intent;
        return Start("SetMovementIntent", () => live.SetMovementIntent(copy));
    }

    public bool ClearMovementIntent() => Stop("ClearMovementIntent", live.ClearMovementIntent);
    public bool StopCompletely() => Stop("StopCompletely", live.StopCompletely);
    public bool BeginJump(float power) => Start("BeginJump", () => live.BeginJump(power));

    public bool MoveItemExternal(uint objectId, uint targetId, uint amount) =>
        Start("MoveItemExternal", () => live.MoveItemExternal(objectId, targetId, amount));

    public bool GiveItem(uint objectId, uint targetId, uint amount) =>
        Start("GiveItem", () => live.GiveItem(objectId, targetId, amount));

    public bool CloseContainer(uint containerId) => Stop("CloseContainer", () => live.CloseContainer(containerId));

    public bool Salvage(uint toolId, IReadOnlyList<uint> itemIds)
    {
        uint[] copy = [.. itemIds];
        return Start("Salvage", () => live.Salvage(toolId, copy));
    }

    public bool RequestAdvancement(int kind, uint statId, uint amount) =>
        Start("RequestAdvancement", () => live.RequestAdvancement(kind, statId, amount));

    public bool TradeOpen(uint partnerId) => Start("TradeOpen", () => live.TradeOpen(partnerId));
    public bool TradeAdd(uint itemId) => Start("TradeAdd", () => live.TradeAdd(itemId));
    public bool TradeAccept() => Start("TradeAccept", live.TradeAccept);
    public bool TradeDecline() => Stop("TradeDecline", live.TradeDecline);
    public bool TradeReset() => Start("TradeReset", live.TradeReset);
    public bool TradeClose() => Stop("TradeClose", live.TradeClose);

    public bool SubmitChatCommand(string text) => Start("SubmitChatCommand", () => live.SubmitChatCommand(text));

    // A stop, so chat lines are never refused while portalling (RynthCore enqueues WriteToChat the same way).
    public void WriteChatLine(string text, int chatType) =>
        Stop("WriteChatLine", () => { live.WriteChatLine(text, chatType); return true; });

    public bool VendorBuy(IReadOnlyList<(uint ObjectId, int Amount)> items, out string? notice)
    {
        notice = null;
        (uint, int)[] copy = [.. items];
        return Start("VendorBuy", () =>
        {
            bool sent = live.VendorBuy(copy, out string? refused);
            if (!sent)
                onVendorQueuedFailure(refused ?? "the client would not send it");
            return sent;
        });
    }

    public bool VendorSell(IReadOnlyList<uint> itemIds, out string? notice)
    {
        notice = null;
        uint[] copy = [.. itemIds];
        return Start("VendorSell", () =>
        {
            bool sent = live.VendorSell(copy, out string? refused);
            if (!sent)
                onVendorQueuedFailure(refused ?? "the client would not send it");
            return sent;
        });
    }

    // Raised on the frame thread; subscriptions go straight to the live view.
    public event Action<RynthVendorOutcome>? VendorTransactionCompleted
    {
        add => live.VendorTransactionCompleted += value;
        remove => live.VendorTransactionCompleted -= value;
    }
}
