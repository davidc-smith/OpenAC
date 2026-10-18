using AcDream.Core.Items;
using AcDream.Rynth.Host;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RuntimeRynthGameViewTests
{
    [Fact]
    public void ReadsThePlayerAndObjectsFromTheRuntime()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var item = new ClientObject { ObjectId = 0x80000010, Name = "Drudge Skulker" };
        item.Properties.Int64s[1] = 42;
        runtime.InventoryOwner.Objects.AddOrUpdate(item);
        var view = new RuntimeRynthGameView(runtime);

        Assert.Equal(0x50000001u, view.PlayerId);
        Assert.True(view.TryGetObjectName(0x80000010, out string name));
        Assert.Equal("Drudge Skulker", name);
        Assert.False(view.TryGetObjectName(0x80000011, out _));
        Assert.True(view.TryGetInt64Property(0x80000010, 1, out long value));
        Assert.Equal(42, value);
        Assert.False(view.TryGetInt64Property(0x80000010, 6, out _));
    }

    [Fact]
    public void WriteChatLine_AddsASystemLine()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var view = new RuntimeRynthGameView(runtime);

        view.WriteChatLine("Rynth tracker: no data yet", 0);

        Assert.Contains(
            runtime.CommunicationOwner.Chat.Snapshot(),
            entry => entry.Text == "Rynth tracker: no data yet");
    }

    [Fact]
    public void ReadsObjectFieldsAndContentsWithWieldedItems()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var pack = new ClientObject
        {
            ObjectId = 0x80000020,
            Name = "Pack",
            Type = ItemType.Container,
            ContainerId = 0x50000001,
            ItemsCapacity = 24,
            IconId = 0x06001111,
            WeenieClassId = 136,
            PublicWeenieBitfield = 0x1,
        };
        pack.Properties.Ints[28] = 5;
        var sword = new ClientObject
        {
            ObjectId = 0x80000010,
            Name = "Sword",
            WielderId = 0x50000001,
            CurrentlyEquippedLocation = EquipMask.MeleeWeapon,
        };
        runtime.InventoryOwner.Objects.AddOrUpdate(pack);
        runtime.InventoryOwner.Objects.AddOrUpdate(sword);
        // AddOrUpdate indexes wielded items only; pack contents arrive as a server listing.
        runtime.InventoryOwner.Objects.ReplaceContents(0x50000001, new uint[] { 0x80000020 });
        var view = new RuntimeRynthGameView(runtime);

        Assert.False(view.TryGetObject(0x80000099, out _));
        Assert.True(view.TryGetObject(0x80000020, out RynthObjectInfo info));
        Assert.Equal(0x50000001u, info.ContainerId);
        Assert.Equal((uint)ItemType.Container, info.ItemType);
        Assert.Equal(24, info.ItemsCapacity);
        Assert.Equal(0x06001111u, info.IconId);
        Assert.Equal(136u, info.Wcid);
        Assert.Equal(0x1u, info.Bitfield);
        Assert.False(info.Identified);
        Assert.Equal(5, info.Ints[28]);
        Assert.True(view.TryGetObject(0x80000010, out RynthObjectInfo swordInfo));
        Assert.Equal((uint)EquipMask.MeleeWeapon, swordInfo.EquipLocation);
        Assert.Equal(0u, swordInfo.Bitfield);

        Assert.Equal([0x80000010u, 0x80000020u], view.GetContainerContents(0x50000001));
        Assert.Empty(view.GetContainerContents(0x80000020));
        Assert.False(view.TryGetObjectPosition(0x80000020, out _, out _));
        Assert.Equal(string.Empty, view.WorldName);
        Assert.Equal(string.Empty, view.AccountName);
    }

    [Fact]
    public void ThePlayerObjectMergesTheDescriptionOverItsOwnTables()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var player = new ClientObject { ObjectId = 0x50000001, Name = "Tester" };
        player.Properties.Ints[7] = 1;
        player.Properties.Ints[8] = 3;
        runtime.InventoryOwner.Objects.AddOrUpdate(player);
        var description = new PropertyBundle();
        description.Ints[7] = 2;
        var view = new RuntimeRynthGameView(runtime);

        // Before a description lands, the object's own table answers.
        Assert.True(view.TryGetObject(0x50000001, out RynthObjectInfo before));
        Assert.Equal(1, before.Ints[7]);

        runtime.CharacterOwner.LocalPlayer.OnProperties(description);

        Assert.True(view.TryGetObject(0x50000001, out RynthObjectInfo merged));
        Assert.Equal(2, merged.Ints[7]);
        Assert.Equal(3, merged.Ints[8]);
        Assert.Equal(1, player.Properties.Ints[7]);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void IdentifiedNeedsAnAnsweredAppraisalThatSucceeded(bool answered, bool unsuccessful, bool expected)
    {
        var runtime = GameRuntimeTestFactory.Create();
        var item = new ClientObject
        {
            ObjectId = 0x80000010,
            AppraisalAnswered = answered,
            LastAppraisalUnsuccessful = unsuccessful,
        };
        runtime.InventoryOwner.Objects.AddOrUpdate(item);
        var view = new RuntimeRynthGameView(runtime);

        Assert.True(view.TryGetObject(0x80000010, out RynthObjectInfo info));
        Assert.Equal(expected, info.Identified);
    }
}
