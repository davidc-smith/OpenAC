using System.Numerics;
using AcDream.Core.Items;

namespace AcDream.Rynth.Host;

/// <summary>
/// The game data the RynthCore table functions read. The client implements it
/// over <c>GameRuntime</c> (<see cref="RuntimeRynthGameView"/>); tests use a fake.
/// </summary>
public interface IRynthGameView
{
    /// <summary>The player's object id; 0 before login.</summary>
    uint PlayerId { get; }

    /// <summary>The name of a known object.</summary>
    bool TryGetObjectName(uint objectId, out string name);

    /// <summary>An Int64 ("quad") property of an object, by property id.</summary>
    bool TryGetInt64Property(uint objectId, uint propertyId, out long value);

    /// <summary>The selected object's id, 0 when nothing is selected.</summary>
    uint SelectedObjectId { get; }

    /// <summary>Runs text as if typed in the chat bar. True when the client accepted it.</summary>
    bool SubmitChatCommand(string text);

    /// <summary>Adds a line to the chat window.</summary>
    void WriteChatLine(string text, int chatType);

    /// <summary>An object's fields and property tables; false when the client does not know it.</summary>
    bool TryGetObject(uint objectId, out RynthObjectInfo info);

    /// <summary>
    /// The objects inside a container and the objects it wields, ascending by id,
    /// as RynthCore lists them (its inventory walk finds worn items this way).
    /// </summary>
    IReadOnlyList<uint> GetContainerContents(uint containerId);

    /// <summary>
    /// Every object the client knows (RynthCore: acclient's live object table, world
    /// objects and items alike). Null out of world.
    /// </summary>
    IReadOnlyList<uint>? LiveObjectIds { get; }

    /// <summary>The logged-in account's name; empty before the character list.</summary>
    string AccountName { get; }

    /// <summary>The server's world name; empty before the server sends it.</summary>
    string WorldName { get; }

    /// <summary>A world object's cell and its position inside that cell.</summary>
    bool TryGetObjectPosition(uint objectId, out uint cellId, out Vector3 origin);

    /// <summary>The player's cell, origin in that cell, and orientation.</summary>
    bool TryGetPlayerPose(out uint cellId, out Vector3 origin, out Quaternion orientation);

    /// <summary>The player's heading: degrees clockwise from north.</summary>
    bool TryGetPlayerHeading(out float headingDegrees);

    /// <summary>A world object's heading: degrees clockwise from north.</summary>
    bool TryGetObjectHeading(uint objectId, out float headingDegrees);

    /// <summary>True while the player is in portal space.</summary>
    bool IsPortaling { get; }

    /// <summary>The client's busy count: 0 idle, more while actions are outstanding.</summary>
    int BusyCount { get; }

    /// <summary>RynthCore's combat mode: 1 NonCombat, 2 Melee, 4 Missile, 8 Magic.</summary>
    int CombatMode { get; }

    bool TryGetPlayerVitals(out RynthVitals vitals);

    /// <summary>The unbuffed maximum health, stamina and mana.</summary>
    bool TryGetPlayerBaseVitals(out uint health, out uint stamina, out uint mana);

    /// <summary>1 with no vitae penalty; 0.95 for five percent.</summary>
    float VitaeMultiplier { get; }

    /// <summary>Server time in seconds; 0 before login.</summary>
    double ServerTime { get; }

    /// <summary>The player's enchantments in force; null when not known (before login).</summary>
    IReadOnlyList<RynthEnchantment>? PlayerEnchantments { get; }

    /// <summary>The player's skills, attributes, vitals, level, XP and credits; null when not in the world.</summary>
    RynthPlayerProgress? PlayerProgress { get; }

    /// <summary>The player's enchantments that carry a stat change; null when not in the world.</summary>
    IReadOnlyList<RynthStatMod>? PlayerStatMods { get; }

    /// <summary>
    /// Raise or train as RynthCore's PlayerTraining does: kind 1 attribute, 2 vital (stat id 1/3/5),
    /// 3 skill (amount = XP), 4 train a skill (amount = credits). True when the client sent it.
    /// </summary>
    bool RequestAdvancement(int kind, uint statId, uint amount);

    /// <summary>A skill of the player: buffed and base level, training (0 unusable, 1 untrained, 2 trained, 3 specialized).</summary>
    bool TryGetSkill(uint objectId, uint skillId, out int buffed, out int baseLevel, out int training);

    /// <summary>A primary attribute of the player (1 Strength … 6 Self).</summary>
    bool TryGetAttribute(uint objectId, uint attributeId, out uint buffed, out uint baseLevel);

    /// <summary>The player's unbuffed maximum of a vital, by stat 1 health, 3 stamina, 5 mana.</summary>
    bool TryGetVitalBase(uint objectId, uint vitalStatId, out uint baseMaximum);

    /// <summary>1 known, 0 not known, -1 cannot tell (not the player, before login).</summary>
    int IsSpellKnown(uint objectId, uint spellId);

    /// <summary>Every spell the player knows, ascending; null before login.</summary>
    IReadOnlyList<uint>? KnownSpells { get; }

    /// <summary>Retail's ObjectIsAttackable for the player against this object.</summary>
    bool IsAttackable(uint objectId);

    /// <summary>The open external container (corpse, chest); 0 when none.</summary>
    uint OpenContainerId { get; }

    /// <summary>The server's UseDone and refusal counters.</summary>
    ServerReplySnapshot ServerReplies { get; }

    /// <summary>The one item request the client has outstanding (acclient's request slot), as acclient's request type and object; false when none.</summary>
    bool TryGetPendingItemRequest(out uint acType, out uint objectId);

    /// <summary>True while an attack request, its server answer, or a repeat attack is in progress.</summary>
    bool IsAttackInProgress { get; }

    /// <summary>Changes whenever an object is added, moved, removed or updated in the client's table.</summary>
    ulong InventoryRevision { get; }

    /// <summary>Selects an object (0 clears). True when it is selected afterwards.</summary>
    bool SelectObject(uint objectId);

    /// <summary>Uses an object, approaching it first when out of reach. True when sent.</summary>
    bool UseObject(uint objectId);

    /// <summary>Asks the server to identify (appraise) an object. True when sent.</summary>
    bool RequestIdentify(uint objectId);

    /// <summary>Asks the server for a creature's health. True when sent.</summary>
    bool QueryHealth(uint objectId);

    /// <summary>Changes combat mode, in RynthCore's 1/2/4/8. True when sent.</summary>
    bool ChangeCombatMode(int rynthMode);

    /// <summary>Moves (or, with an amount below the stack, splits) an owned item into a container slot.</summary>
    bool MoveItem(uint objectId, uint containerId, int slot, int amount);

    /// <summary>Merges one stack into another.</summary>
    bool MergeStack(uint sourceObjectId, uint targetObjectId);

    /// <summary>Clears every busy reference the client holds.</summary>
    void ResetBusyCount();

    /// <summary>Wields an item into the given equip slot mask.</summary>
    bool WieldItem(uint objectId, uint equipMask);

    /// <summary>True while the player's cast or action gesture is still playing (RynthCore's cast gate).</summary>
    bool IsCastGestureActive { get; }

    /// <summary>A creature's vitals from its last appraisal; stamina and mana 0 when it did not show them.</summary>
    bool TryGetTargetVitals(uint objectId, out RynthVitals vitals);

    /// <summary>The latest health ratio (0..1) the client has for an object, ratio-only updates included; false when none was seen.</summary>
    bool TryGetHealthRatio(uint objectId, out float ratio);

    /// <summary>An object's enchantments in force; null when the client keeps none for it (it keeps the player's only).</summary>
    IReadOnlyList<RynthEnchantment>? GetObjectEnchantments(uint objectId);

    /// <summary>Casts at a target without changing the selection; a self spell goes to the player. True when sent.</summary>
    bool CastSpell(uint targetId, uint spellId);

    /// <summary>Sends a melee attack at once, past the power bar. Height 1 high, 2 medium, 3 low; power 0..1. True when sent.</summary>
    bool MeleeAttack(uint targetId, int height, float power);

    /// <summary>Sends a missile attack at once, past the power bar. Accuracy 0..1. True when sent.</summary>
    bool MissileAttack(uint targetId, int height, float accuracy);

    /// <summary>Cancels the attack in progress. True when sent.</summary>
    bool CancelAttack();

    /// <summary>Uses one object on another (a kit on the player, a mana stone on an item). True when sent.</summary>
    bool UseObjectOn(uint sourceObjectId, uint targetObjectId);

    /// <summary>True while the player's autorun is on.</summary>
    bool AutoRunActive { get; }

    /// <summary>Turns autorun on or off. True when taken.</summary>
    bool SetAutoRun(bool enabled);

    /// <summary>Turns the player to a compass heading at once (0 north, clockwise). True when taken.</summary>
    bool SnapHeading(float headingDegrees);

    /// <summary>Holds movement keys for the player, replacing the keyboard while held. True when taken.</summary>
    bool SetMovementIntent(in AcDream.Plugin.Abstractions.PluginMovementIntent intent);

    /// <summary>Lets go of every held movement key. True when taken.</summary>
    bool ClearMovementIntent();

    /// <summary>
    /// The movement keys the client holds for a plugin right now, null when none are held.
    /// The client clears them on its own (portal, relog, stop commands, another plugin).
    /// </summary>
    AcDream.Plugin.Abstractions.PluginMovementIntent? CurrentMovementIntent { get; }

    /// <summary>Stops the player completely, autorun included. True when taken.</summary>
    bool StopCompletely();

    /// <summary>Charges a jump to a power of 0..1 over the frames that takes, then releases it. True when taken.</summary>
    bool BeginJump(float power);

    /// <summary>An object's raw physics-state bits (0x4 ethereal: an open door); false when the object is unknown.</summary>
    bool TryGetObjectState(uint objectId, out uint state);

    /// <summary>Whether an object's last motion was On (true) or Off (false); false when neither has been seen.</summary>
    bool TryGetObjectMotionOn(uint objectId, out bool isOn);

    /// <summary>
    /// RynthAi's general move (RynthCore's MoveItemExternal), chosen by the objects: an item in the
    /// open corpse or chest is looted, whole, into the target pack (0 or the player: the client's
    /// own choice); an owned item goes to the ground (target 0), into a pack the player owns or the
    /// player's main pack, or to any other known target as a give. Amount 0 is the whole stack.
    /// True when sent.
    /// </summary>
    bool MoveItemExternal(uint objectId, uint targetId, uint amount);

    /// <summary>Gives all (0) or part of an owned stack to a world target. True when sent.</summary>
    bool GiveItem(uint objectId, uint targetId, uint amount);

    /// <summary>Closes the open corpse or chest. True when sent.</summary>
    bool CloseContainer(uint containerId);

    /// <summary>True when the object is a tinkering tool (an Ust) the player owns.</summary>
    bool IsOwnedTinkeringTool(uint objectId);

    /// <summary>True when the player owns the item (in a pack or worn).</summary>
    bool IsOwnedItem(uint objectId);

    /// <summary>Salvages the items with the tool in one request. True when sent.</summary>
    bool Salvage(uint toolId, IReadOnlyList<uint> itemIds);

    /// <summary>An object's sub-palettes (id, offset) in offset order; null when the object is unknown.</summary>
    IReadOnlyList<(uint SubPaletteId, uint Offset)>? GetPalettes(uint objectId);

    /// <summary>A container's direct contents split into items and containers (worn items are not contents); false when it is unknown.</summary>
    bool TryCountContents(uint containerId, out int items, out int containers);

    /// <summary>The open vendor; null when none is open.</summary>
    RynthVendor? Vendor { get; }

    /// <summary>Buys the given listings in one request. False (with the reason) when the client would not send it.</summary>
    bool VendorBuy(IReadOnlyList<(uint ObjectId, int Amount)> items, out string? notice);

    /// <summary>Sells the given owned items, whole stacks, in one request. False (with the reason) when the client would not send it.</summary>
    bool VendorSell(IReadOnlyList<uint> itemIds, out string? notice);

    /// <summary>Raised on the game thread with the server's answer to a buy or sell the view sent.</summary>
    event Action<RynthVendorOutcome>? VendorTransactionCompleted;

    /// <summary>The trade window and counters; null when the client cannot trade (out of world).</summary>
    RynthTradeState? Trade { get; }

    /// <summary>Trade actions; true when sent. Open asks the partner, Add stages an owned item, Close ends the trade.</summary>
    bool TradeOpen(uint partnerId);
    bool TradeAdd(uint itemId);
    bool TradeAccept();
    bool TradeDecline();
    bool TradeReset();
    bool TradeClose();

    /// <summary>
    /// Where a point in metres from the player's landblock (X east, Y up, Z north) falls
    /// on screen, in points, Y down. False when behind the camera or the view is not ready.
    /// </summary>
    bool TryWorldToScreen(float x, float y, float z, out float screenX, out float screenY);

    /// <summary>The window size in points.</summary>
    bool TryGetViewportSize(out uint width, out uint height);

    /// <summary>
    /// The radar's box on screen in points, as RynthCore's surface box: RynthAi draws its
    /// walls at min(width, height) / 2 - 2 from the centre. False while the radar is hidden.
    /// </summary>
    bool TryGetRadarRect(out int x0, out int y0, out int x1, out int y1);
}
