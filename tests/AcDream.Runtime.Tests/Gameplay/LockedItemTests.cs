using AcDream.Core.Items;
using AcDream.Runtime.Gameplay;

namespace AcDream.Runtime.Tests.Gameplay;

/// <summary>
/// An item the player has locked is not given, dropped, sold or salvaged by
/// the runtime entry points: each sends nothing, returns false and says so
/// once. A pack is refused for a locked item inside it. With no
/// <see cref="ItemLocks"/> attached nothing changes.
/// </summary>
public sealed class LockedItemTests
{
    private const uint Player = 0x50000001u;
    private const uint Sword = 0x8000F001u;
    private const uint Bag = 0x8000F002u;
    private const uint Gem = 0x8000F003u;
    private const uint Npc = 0x8000F004u;
    private const uint Vendor = 0x8000F005u;
    private const uint Tool = 0x8000F006u;

    [Fact]
    public void Give_Drop_Sell_AndSalvage_RefuseALockedItem_WithOneLine()
    {
        using var h = new Harness();
        h.Interaction.Locks = h.Locks;
        h.Locks.Set(Sword, true);

        Assert.False(h.Interaction.TryGiveItemForAutomation(Sword, Npc));
        Assert.False(h.Interaction.TryDropItemForAutomation(Sword));
        Assert.False(h.Interaction.TrySell(Vendor, [(1, Sword)]));
        Assert.False(h.Interaction.TrySalvageItems(Tool, [Sword]));

        Assert.Empty(h.Gives);
        Assert.Empty(h.Drops);
        Assert.Empty(h.Sells);
        Assert.Empty(h.Salvages);
        Assert.Equal(4, h.Notices.Count(n => n == "The Sword is locked."));
    }

    [Fact]
    public void APackHoldingALockedItem_IsRefused_ByItsContent()
    {
        using var h = new Harness();
        h.Interaction.Locks = h.Locks;
        h.Locks.Set(Gem, true);

        Assert.False(h.Interaction.TryDropItemForAutomation(Bag));
        Assert.Contains("The Gem is locked.", h.Notices);
        Assert.Empty(h.Drops);
    }

    [Fact]
    public void WithoutLocks_NothingChanges()
    {
        using var h = new Harness();

        Assert.True(h.Interaction.TryGiveItemForAutomation(Sword, Npc));
        Assert.Single(h.Gives);
    }

    private sealed class Harness : IDisposable
    {
        internal Harness()
        {
            Objects.AddOrUpdate(new ClientObject
            {
                ObjectId = Player,
                Name = "Player",
                Type = ItemType.Creature,
                ItemsCapacity = 8,
                ContainersCapacity = 2,
            });
            Objects.AddOrUpdate(Item(Sword, "Sword", ItemType.MeleeWeapon, Player));
            Objects.AddOrUpdate(Item(Tool, "Tool", ItemType.TinkeringTool, Player));
            Objects.AddOrUpdate(new ClientObject
            {
                ObjectId = Bag,
                Name = "Bag",
                Type = ItemType.Container,
                ContainerId = Player,
                ItemsCapacity = 8,
            });
            Objects.AddOrUpdate(Item(Gem, "Gem", ItemType.Gem, Bag));
            Objects.ReplaceContents(Player, [Sword, Tool, Bag]);
            Objects.ReplaceContents(Bag, [Gem]);
            Objects.AddOrUpdate(new ClientObject { ObjectId = Npc, Name = "Npc", Type = ItemType.Creature });
            Objects.AddOrUpdate(new ClientObject { ObjectId = Vendor, Name = "Vendor", Type = ItemType.Creature });

            Interaction = new RuntimeItemInteraction(
                Objects,
                new RuntimeInteractionTransactionState(
                    new InventoryTransactionState(Objects)),
                new InteractionState(),
                playerGuid: () => Player,
                sendUse: null,
                sendUseWithTarget: null,
                sendWield: null,
                sendDrop: Drops.Add,
                sendGive: (target, item, amount) => Gives.Add((target, item, amount)),
                systemMessage: Notices.Add,
                sendSell: (vendor, items) =>
                {
                    Sells.Add((vendor, items));
                    return true;
                },
                sendSalvage: (tool, items) =>
                {
                    Salvages.Add((tool, items));
                    return true;
                });
        }

        internal ClientObjectTable Objects { get; } = new();
        internal RuntimeItemInteraction Interaction { get; }
        internal ItemLocks Locks { get; } = new();
        internal List<(uint Target, uint Item, uint Amount)> Gives { get; } = [];
        internal List<uint> Drops { get; } = [];
        internal List<(uint Vendor, IReadOnlyList<(int Amount, uint ItemGuid)> Items)> Sells { get; } = [];
        internal List<(uint Tool, IReadOnlyList<uint> Items)> Salvages { get; } = [];
        internal List<string> Notices { get; } = [];

        public void Dispose() => Interaction.Dispose();

        private static ClientObject Item(uint id, string name, ItemType type, uint container) => new()
        {
            ObjectId = id,
            Name = name,
            Type = type,
            ContainerId = container,
            StackSize = 1,
            StackSizeMax = 1,
        };
    }
}
