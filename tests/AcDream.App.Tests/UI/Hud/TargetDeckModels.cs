using AcDream.App.UI.Hud;
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Properties;
using AcDream.Core.Targeting;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>Plate models for the Target Deck tests, one per kind, built the way the game builds them.</summary>
internal static class TargetDeckModels
{
    public const uint Me = 0x50000001u;

    public sealed class NoActions : ITargetDeckActions
    {
        public void Run(TargetAction action, TargetPlateModel target) { }
        public void RunPluginAction(string pluginId, uint objectId, string actionId) { }
    }

    public sealed class Recorder : ITargetDeckActions
    {
        public readonly List<string> Calls = [];
        public void Run(TargetAction action, TargetPlateModel target) => Calls.Add($"run {action} {target.ObjectId}");
        public void RunPluginAction(string pluginId, uint objectId, string actionId) =>
            Calls.Add($"plugin {pluginId} {objectId} {actionId}");
    }

    public static ClientObject Monster()
    {
        var o = new ClientObject { ObjectId = 7, Name = "Drudge Ravener", Type = ItemType.Creature,
            PublicWeenieBitfield = (uint)PublicWeenieFlags.Attackable };
        o.Properties.Ints[(uint)PropertyInt.Level] = 45;
        return o;
    }

    public static ClientObject Player() =>
        new() { ObjectId = 2, Name = "Sammy", Type = ItemType.Creature, PublicWeenieBitfield = (uint)PublicWeenieFlags.Player };

    /// <summary>One object of each of the ten kinds.</summary>
    public static ClientObject[] EveryKind() =>
    [
        Monster(),
        new() { ObjectId = 3, Name = "Wolf", Type = ItemType.Creature, PetOwnerId = Me },
        Player(),
        new() { ObjectId = 4, Name = "Thorsten", Type = ItemType.Creature, PublicWeenieBitfield = (uint)PublicWeenieFlags.Vendor },
        new() { ObjectId = 5, Name = "Sword", Type = ItemType.MeleeWeapon },
        new() { ObjectId = 6, Name = "Corpse of Drudge", Type = ItemType.Container, PublicWeenieBitfield = (uint)PublicWeenieFlags.Corpse },
        new() { ObjectId = 8, Name = "Chest", Type = ItemType.Container },
        new() { ObjectId = 10, Name = "Portal to Ayan Baqur", Type = ItemType.Portal, PublicWeenieBitfield = (uint)PublicWeenieFlags.Portal },
        new() { ObjectId = 11, Name = "Door", Type = ItemType.Misc, PublicWeenieBitfield = (uint)PublicWeenieFlags.Door },
        new() { ObjectId = Me, Name = "Me", Type = ItemType.Creature, PublicWeenieBitfield = (uint)PublicWeenieFlags.Player },
    ];

    public static TargetPlateModel Build(ClientObject obj, uint selectedPackItem = 0,
        TargetAppraisalLog? log = null, IReadOnlyList<TargetPluginRow>? pluginRows = null,
        string? overflow = null, IReadOnlyList<TargetPluginAction>? pluginActions = null)
    {
        var input = new TargetPlateInput(
            Object: obj, PlayerId: Me, PlayerLevel: 100, Combat: new CombatState(),
            Log: log ?? new TargetAppraisalLog(), Now: 50.0, DistanceMeters: 4f,
            SelectedPackItemId: selectedPackItem, IsFellow: _ => false,
            PluginRows: pluginRows ?? Array.Empty<TargetPluginRow>(), PluginOverflowTooltip: overflow,
            PluginActions: pluginActions ?? Array.Empty<TargetPluginAction>(), NameOf: _ => null);
        return TargetPlateModelBuilder.Build(input)!;
    }

    public static readonly TargetPluginRow PluginRow = new("Weak to", "Fire", 0xF0B43CFFu, "Takes extra fire damage");

    /// <summary>Every kind with one plugin row.</summary>
    public static IEnumerable<object[]> EveryKindWithAPluginRow()
    {
        foreach (ClientObject o in EveryKind())
            yield return [Build(o, pluginRows: [PluginRow])];
    }
}
