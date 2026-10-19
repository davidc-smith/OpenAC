using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Extensions;

/// <summary>An automation surface whose areas tests set and whose commands it records.</summary>
internal sealed class FakeRynthAutomation : IAutomationSurface
{
    public bool Available { get; set; } = true;
    public FakeCharacter CharacterArea { get; } = new();
    public FakeSpells SpellArea { get; } = new();
    public FakeCombat CombatArea { get; } = new();
    public FakeItems ItemArea { get; } = new();
    public FakeEquipment EquipmentArea { get; } = new();
    public FakeObjects ObjectArea { get; } = new();
    public FakeRecovery RecoveryArea { get; } = new();
    public FakeNavigation NavigationArea { get; } = new();
    public FakeLoot LootArea { get; } = new();

    public bool IsAvailable => Available;
    public ICharacterInfo Character => CharacterArea;
    public ISpellCatalog Spells => SpellArea;
    public IMagicCommands Magic => NoOpAutomationSurface.Instance;
    public IPluginChat Chat => NoOpAutomationSurface.Instance;
    public ICombatAutomation Combat => CombatArea;
    public IItemAutomation Items => ItemArea;
    public IEquipmentAutomation Equipment => EquipmentArea;
    public IWorldObjectAutomation Objects => ObjectArea;
    public IRecoveryAutomation Recovery => RecoveryArea;
    public INavigationAutomation Navigation => NavigationArea;
    public ILootAutomation Loot => LootArea;
}

internal sealed class FakeLoot : ILootAutomation
{
    public List<string> Calls { get; } = new();

    public PluginItemCommandResult Pickup(uint objectId, bool mainPack = false)
    {
        Calls.Add($"pickup {objectId:X8}");
        return new(PluginItemCommandStatus.Started);
    }

    public PluginItemCommandResult Pickup(uint objectId, uint preferredContainerObjectId)
    {
        Calls.Add($"pickup {objectId:X8} into {preferredContainerObjectId:X8}");
        return new(PluginItemCommandStatus.Started);
    }
}

internal sealed class FakeCharacter : ICharacterInfo
{
    public bool IsInWorld { get; set; } = true;
    public uint ObjectId { get; set; }
    public uint CurrentHealth { get; set; }
    public uint MaxHealth { get; set; }
    public uint CurrentStamina { get; set; }
    public uint MaxStamina { get; set; }
    public uint CurrentMana { get; set; }
    public uint MaxMana { get; set; }
    public uint BaseHealth { get; set; }
    public uint BaseStamina { get; set; }
    public uint BaseMana { get; set; }
    public float VitaeMultiplier { get; set; } = 1f;
    public List<PluginSkillInfo> SkillList { get; } = new();
    public List<PluginAttributeInfo> AttributeList { get; } = new();
    public List<PluginVitalInfo> VitalList { get; } = new();
    public List<PluginActiveEnchantment> EnchantmentList { get; } = new();
    public IReadOnlyList<PluginSkillInfo> Skills => SkillList;
    public IReadOnlyList<PluginAttributeInfo> Attributes => AttributeList;
    public IReadOnlyList<PluginActiveEnchantment> ActiveEnchantments => EnchantmentList;

    public bool TryGetSkill(uint skillId, out PluginSkillInfo skill)
    {
        skill = SkillList.FirstOrDefault(s => s.SkillId == skillId);
        return skill.SkillId == skillId && skillId != 0;
    }

    public bool TryGetVital(int kind, out PluginVitalInfo vital)
    {
        vital = VitalList.FirstOrDefault(v => v.Kind == kind);
        return VitalList.Any(v => v.Kind == kind);
    }
}

internal sealed class FakeSpells : ISpellCatalog
{
    public HashSet<uint> Known { get; } = new();
    public IReadOnlyList<PluginSpellInfo> KnownSelfBuffs => Array.Empty<PluginSpellInfo>();
    public bool TryGet(uint spellId, out PluginSpellInfo info)
    {
        info = default;
        return false;
    }

    public bool IsKnown(uint spellId) => Known.Contains(spellId);
}

internal sealed class FakeCombat : ICombatAutomation
{
    public PluginCombatSnapshot Snapshot { get; set; }
    public List<PluginCombatMode> ModesRequested { get; } = new();
    public PluginCombatCommandResult EnterDefaultMode() => new(PluginCombatCommandStatus.Unavailable);
    public PluginCombatCommandResult BeginPhysicalAttack(uint targetObjectId, PluginAttackHeight height, float power) =>
        new(PluginCombatCommandStatus.Unavailable);
    public PluginCombatCommandResult ReleasePhysicalAttack() => new(PluginCombatCommandStatus.Unavailable);
    public PluginCombatCommandResult AbortPhysicalAttack() => new(PluginCombatCommandStatus.Unavailable);
    public IReadOnlyList<PluginCombatTarget> CaptureHostileTargets(float maximumDistance) =>
        Array.Empty<PluginCombatTarget>();

    public PluginCombatCommandResult EnterMode(PluginCombatMode mode)
    {
        ModesRequested.Add(mode);
        return new(PluginCombatCommandStatus.ModeChangeSent);
    }
}

internal sealed class FakeItems : IItemAutomation
{
    public List<string> Calls { get; } = new();
    public PluginItemCommandStatus Answer { get; set; } = PluginItemCommandStatus.Started;

    public PluginItemCommandResult Apply(uint objectId, uint targetObjectId)
    {
        Calls.Add($"apply {objectId:X8} {targetObjectId:X8}");
        return new(Answer);
    }

    public PluginItemCommandResult Use(uint objectId)
    {
        Calls.Add($"use {objectId:X8}");
        return new(Answer);
    }

    public PluginItemCommandResult MoveToContainer(uint objectId, uint containerObjectId, uint amount = 0u, int placement = 0)
    {
        Calls.Add($"move {objectId:X8} {containerObjectId:X8} {amount} {placement}");
        return new(Answer);
    }

    public PluginItemCommandResult Merge(uint sourceObjectId, uint targetObjectId, uint amount = 0u)
    {
        Calls.Add($"merge {sourceObjectId:X8} {targetObjectId:X8} {amount}");
        return new(Answer);
    }

    public PluginItemCommandResult Drop(uint objectId, uint amount = 0u)
    {
        Calls.Add($"drop {objectId:X8} {amount}");
        return new(Answer);
    }

    public PluginItemCommandResult Give(uint objectId, uint targetObjectId, uint amount = 0u)
    {
        Calls.Add($"give {objectId:X8} {targetObjectId:X8} {amount}");
        return new(Answer);
    }
}

internal sealed class FakeEquipment : IEquipmentAutomation
{
    public List<(uint ObjectId, uint Location)> Equipped { get; } = new();

    public PluginEquipmentCommandResult Equip(uint objectId, uint requestedLocation = 0u)
    {
        Equipped.Add((objectId, requestedLocation));
        return new(PluginEquipmentCommandStatus.Started);
    }
}

internal sealed class FakeObjects : IWorldObjectAutomation
{
    public Dictionary<uint, PluginWorldObject> Known { get; } = new();
    public List<uint> Identified { get; } = new();
    public uint OpenContainer { get; set; }
    public uint OpenContainerObjectId => OpenContainer;

    public bool TryGet(uint objectId, out PluginWorldObject value) => Known.TryGetValue(objectId, out value);

    public PluginItemCommandResult Identify(uint objectId)
    {
        Identified.Add(objectId);
        return new(PluginItemCommandStatus.Started);
    }
}

internal sealed class FakeRecovery : IRecoveryAutomation
{
    public int Busy { get; set; }
    public PluginBusyState CaptureBusyState() => new(Busy, false, 0, 0, 0, false);

    public PluginRecoveryResult ClearOneBusyReference()
    {
        if (Busy == 0)
            return new(false);
        int previous = Busy--;
        return new(true, previous, Busy);
    }
}

internal sealed class FakeNavigation : INavigationAutomation
{
    public PluginNavigationSnapshot Snapshot { get; set; }
    public List<PluginMovementIntent?> Intents { get; } = new();
    public PluginNavigationCommandStatus Answer { get; set; } = PluginNavigationCommandStatus.Accepted;
    public PluginNavigationCommandStatus SetMovementIntent(in PluginMovementIntent intent)
    {
        Intents.Add(intent);
        return Answer;
    }
    public PluginNavigationCommandStatus ClearMovementIntent()
    {
        Intents.Add(null);
        return Answer;
    }
    public bool TryGetObject(uint objectId, out PluginNavigationObject value)
    {
        value = default;
        return false;
    }
}
