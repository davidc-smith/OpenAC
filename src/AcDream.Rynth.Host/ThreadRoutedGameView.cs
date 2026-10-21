using System.Numerics;
using AcDream.Core.Items;
using AcDream.Plugin.Abstractions;
using AcDream.Rynth.EngineUi;

namespace AcDream.Rynth.Host;

/// <summary>
/// The view every slot and hook delegate holds: the live view on the frame thread (or before
/// the pump latched it), the current pass's snapshot view anywhere else.
/// </summary>
public sealed class ThreadRoutedGameView(IRynthGameView live, Func<IRynthGameView> offFrame) : IRynthGameView
{
    private IRynthGameView V => !RynthFrameThread.Latched || RynthFrameThread.IsCurrent ? live : offFrame();

    public uint PlayerId => V.PlayerId;
    public bool TryGetObjectName(uint objectId, out string name) => V.TryGetObjectName(objectId, out name);
    public bool TryGetInt64Property(uint objectId, uint propertyId, out long value) => V.TryGetInt64Property(objectId, propertyId, out value);
    public uint SelectedObjectId => V.SelectedObjectId;
    public bool SubmitChatCommand(string text) => V.SubmitChatCommand(text);
    public void WriteChatLine(string text, int chatType) => V.WriteChatLine(text, chatType);
    public bool TryGetObject(uint objectId, out RynthObjectInfo info) => V.TryGetObject(objectId, out info);
    public IReadOnlyList<uint> GetContainerContents(uint containerId) => V.GetContainerContents(containerId);
    public IReadOnlyList<uint>? LiveObjectIds => V.LiveObjectIds;
    public string AccountName => V.AccountName;
    public string WorldName => V.WorldName;
    public bool TryGetObjectPosition(uint objectId, out uint cellId, out Vector3 origin) => V.TryGetObjectPosition(objectId, out cellId, out origin);
    public bool TryGetPlayerPose(out uint cellId, out Vector3 origin, out Quaternion orientation) => V.TryGetPlayerPose(out cellId, out origin, out orientation);
    public bool TryGetPlayerHeading(out float headingDegrees) => V.TryGetPlayerHeading(out headingDegrees);
    public bool TryGetObjectHeading(uint objectId, out float headingDegrees) => V.TryGetObjectHeading(objectId, out headingDegrees);
    public bool IsPortaling => V.IsPortaling;
    public int BusyCount => V.BusyCount;
    public int CombatMode => V.CombatMode;
    public bool TryGetPlayerVitals(out RynthVitals vitals) => V.TryGetPlayerVitals(out vitals);
    public bool TryGetPlayerBaseVitals(out uint health, out uint stamina, out uint mana) => V.TryGetPlayerBaseVitals(out health, out stamina, out mana);
    public float VitaeMultiplier => V.VitaeMultiplier;
    public double ServerTime => V.ServerTime;
    public IReadOnlyList<RynthEnchantment>? PlayerEnchantments => V.PlayerEnchantments;
    public RynthPlayerProgress? PlayerProgress => V.PlayerProgress;
    public IReadOnlyList<RynthStatMod>? PlayerStatMods => V.PlayerStatMods;
    public bool RequestAdvancement(int kind, uint statId, uint amount) => V.RequestAdvancement(kind, statId, amount);
    public bool TryGetSkill(uint objectId, uint skillId, out int buffed, out int baseLevel, out int training) => V.TryGetSkill(objectId, skillId, out buffed, out baseLevel, out training);
    public bool TryGetAttribute(uint objectId, uint attributeId, out uint buffed, out uint baseLevel) => V.TryGetAttribute(objectId, attributeId, out buffed, out baseLevel);
    public bool TryGetVitalBase(uint objectId, uint vitalStatId, out uint baseMaximum) => V.TryGetVitalBase(objectId, vitalStatId, out baseMaximum);
    public int IsSpellKnown(uint objectId, uint spellId) => V.IsSpellKnown(objectId, spellId);
    public IReadOnlyList<uint>? KnownSpells => V.KnownSpells;
    public bool IsAttackable(uint objectId) => V.IsAttackable(objectId);
    public uint OpenContainerId => V.OpenContainerId;
    public ServerReplySnapshot ServerReplies => V.ServerReplies;
    public bool TryGetPendingItemRequest(out uint acType, out uint objectId) => V.TryGetPendingItemRequest(out acType, out objectId);
    public bool IsAttackInProgress => V.IsAttackInProgress;
    public ulong InventoryRevision => V.InventoryRevision;
    public bool SelectObject(uint objectId) => V.SelectObject(objectId);
    public bool UseObject(uint objectId) => V.UseObject(objectId);
    public bool RequestIdentify(uint objectId) => V.RequestIdentify(objectId);
    public bool QueryHealth(uint objectId) => V.QueryHealth(objectId);
    public bool ChangeCombatMode(int rynthMode) => V.ChangeCombatMode(rynthMode);
    public bool MoveItem(uint objectId, uint containerId, int slot, int amount) => V.MoveItem(objectId, containerId, slot, amount);
    public bool MergeStack(uint sourceObjectId, uint targetObjectId) => V.MergeStack(sourceObjectId, targetObjectId);
    public void ResetBusyCount() => V.ResetBusyCount();
    public bool WieldItem(uint objectId, uint equipMask) => V.WieldItem(objectId, equipMask);
    public bool IsCastGestureActive => V.IsCastGestureActive;
    public bool TryGetTargetVitals(uint objectId, out RynthVitals vitals) => V.TryGetTargetVitals(objectId, out vitals);
    public bool TryGetHealthRatio(uint objectId, out float ratio) => V.TryGetHealthRatio(objectId, out ratio);
    public IReadOnlyList<RynthEnchantment>? GetObjectEnchantments(uint objectId) => V.GetObjectEnchantments(objectId);
    public bool CastSpell(uint targetId, uint spellId) => V.CastSpell(targetId, spellId);
    public bool MeleeAttack(uint targetId, int height, float power) => V.MeleeAttack(targetId, height, power);
    public bool MissileAttack(uint targetId, int height, float accuracy) => V.MissileAttack(targetId, height, accuracy);
    public bool CancelAttack() => V.CancelAttack();
    public bool UseObjectOn(uint sourceObjectId, uint targetObjectId) => V.UseObjectOn(sourceObjectId, targetObjectId);
    public bool AutoRunActive => V.AutoRunActive;
    public bool SetAutoRun(bool enabled) => V.SetAutoRun(enabled);
    public bool SnapHeading(float headingDegrees) => V.SnapHeading(headingDegrees);
    public bool SetMovementIntent(in PluginMovementIntent intent) => V.SetMovementIntent(in intent);
    public bool ClearMovementIntent() => V.ClearMovementIntent();
    public PluginMovementIntent? CurrentMovementIntent => V.CurrentMovementIntent;
    public bool StopCompletely() => V.StopCompletely();
    public bool BeginJump(float power) => V.BeginJump(power);
    public bool TryGetObjectState(uint objectId, out uint state) => V.TryGetObjectState(objectId, out state);
    public bool TryGetObjectMotionOn(uint objectId, out bool isOn) => V.TryGetObjectMotionOn(objectId, out isOn);
    public bool MoveItemExternal(uint objectId, uint targetId, uint amount) => V.MoveItemExternal(objectId, targetId, amount);
    public bool GiveItem(uint objectId, uint targetId, uint amount) => V.GiveItem(objectId, targetId, amount);
    public bool CloseContainer(uint containerId) => V.CloseContainer(containerId);
    public bool IsOwnedTinkeringTool(uint objectId) => V.IsOwnedTinkeringTool(objectId);
    public bool IsOwnedItem(uint objectId) => V.IsOwnedItem(objectId);
    public bool Salvage(uint toolId, IReadOnlyList<uint> itemIds) => V.Salvage(toolId, itemIds);
    public IReadOnlyList<(uint SubPaletteId, uint Offset)>? GetPalettes(uint objectId) => V.GetPalettes(objectId);
    public bool TryCountContents(uint containerId, out int items, out int containers) => V.TryCountContents(containerId, out items, out containers);
    public RynthVendor? Vendor => V.Vendor;
    public bool VendorBuy(IReadOnlyList<(uint ObjectId, int Amount)> items, out string? notice) => V.VendorBuy(items, out notice);
    public bool VendorSell(IReadOnlyList<uint> itemIds, out string? notice) => V.VendorSell(itemIds, out notice);
    public RynthTradeState? Trade => V.Trade;
    public bool TradeOpen(uint partnerId) => V.TradeOpen(partnerId);
    public bool TradeAdd(uint itemId) => V.TradeAdd(itemId);
    public bool TradeAccept() => V.TradeAccept();
    public bool TradeDecline() => V.TradeDecline();
    public bool TradeReset() => V.TradeReset();
    public bool TradeClose() => V.TradeClose();
    public bool TryWorldToScreen(float x, float y, float z, out float screenX, out float screenY) => V.TryWorldToScreen(x, y, z, out screenX, out screenY);
    public bool TryGetViewportSize(out uint width, out uint height) => V.TryGetViewportSize(out width, out height);
    public bool TryGetRadarRect(out int x0, out int y0, out int x1, out int y1) => V.TryGetRadarRect(out x0, out y0, out x1, out y1);

    // Raised on the frame thread by the live view; subscribed there from any thread.
    public event Action<RynthVendorOutcome>? VendorTransactionCompleted
    {
        add => live.VendorTransactionCompleted += value;
        remove => live.VendorTransactionCompleted -= value;
    }
}
