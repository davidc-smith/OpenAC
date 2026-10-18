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
}
