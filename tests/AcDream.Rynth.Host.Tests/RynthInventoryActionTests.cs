using RynthCore.Engine.Compatibility;
using static AcDream.Rynth.Host.Tests.InventoryFixture;

namespace AcDream.Rynth.Host.Tests;

/// <summary>Their InventoryModel's item actions, run through the real model against the fake game.</summary>
public sealed class RynthInventoryActionTests : IDisposable
{
    private const uint Npc = 0x50000077u;
    private const uint Other = 0x80000020u;

    private readonly InventoryFixture _fx = new();
    private FakeGameView _view => _fx.View;

    private void Put(uint id, string name, RynthObjectInfo info) => _fx.Put(id, name, info);

    public RynthInventoryActionTests() => InventoryModel.Subscribe();

    public void Dispose() => _fx.Dispose();

    private static void Ticks(int n = 3)
    {
        for (int i = 0; i < n; i++) { InventoryModel.MainThreadTick(); Thread.Sleep(120); }
    }

    private static InventoryRequest Req(InventoryActionKind kind, uint item, string name, uint target = 0, string targetName = "", int amount = 0, int slot = -1) =>
        new() { Kind = kind, ItemId = item, ItemName = name, TargetId = target, TargetName = targetName, Amount = amount, Slot = slot };

    private void Send(InventoryRequest r)
    {
        Ticks(2); // let the first capture run
        Assert.True(InventoryModel.Request(r, out string why), why);
        Ticks(1);
    }

    private void Fill(uint pack, params (uint Id, string Name)[] items)
    {
        var list = _view.Contents[pack];
        int slot = list.Count;
        foreach ((uint id, string name) in items)
        {
            Put(id, name, Item(pack, slot: slot++));
            list.Add(id);
        }
    }

    [Fact]
    public void UseSendsUseAndFinishesOnTheUseDone()
    {
        Send(Req(InventoryActionKind.Use, MainItem, "Healing Kit"));
        Assert.Equal([$"use {MainItem:X8}"], _view.Actions);
        Assert.Equal("Using Healing Kit", InventoryModel.Pending!.Label);
        _view.Replies = new AcDream.Core.Items.ServerReplySnapshot(1, 0, 0, 0, 0, 0);
        Ticks();
        Assert.Equal("Used Healing Kit.", InventoryModel.LastResult!.Text);
        Assert.True(InventoryModel.LastResult.Ok);
        Assert.Null(InventoryModel.Pending);
    }

    [Fact]
    public void MoveWithASlotSendsThePacketAndFinishesWhenTheItemLands()
    {
        Send(Req(InventoryActionKind.Move, MainItem, "Healing Kit", Pack, "Backpack", slot: 2));
        Assert.Equal([$"move {MainItem:X8} {Pack:X8} 2 0"], _view.Actions);
        Put(MainItem, "Healing Kit", Item(Pack, slot: 2));
        Ticks();
        Assert.Equal("Moved Healing Kit to Backpack.", InventoryModel.LastResult!.Text);
        Assert.True(InventoryModel.LastResult.Ok);
    }

    [Fact]
    public void MoveWithoutASlotGoesToTheFirstEmptyOne()
    {
        Fill(Pack, (Other, "Dagger"));   // the pack now holds Arrow and Dagger
        Put(MainItem, "Healing Kit", Item(Player, slot: 0, stack: 5));
        Send(Req(InventoryActionKind.Move, MainItem, "Healing Kit", Pack, "Backpack"));
        Assert.Equal([$"move {MainItem:X8} {Pack:X8} 2 5"], _view.Actions);
    }

    [Fact]
    public void MoveIntoAFullPackSendsNothing()
    {
        Put(Pack, "Backpack", Item(Player, slot: 0, bitfield: PackSlotFlag, capacity: 1));
        _view.ContentCounts[Pack] = (1, 0);
        Send(Req(InventoryActionKind.Move, MainItem, "Healing Kit", Pack, "Backpack"));
        Assert.Empty(_view.Actions);
        Assert.Equal("Backpack is full, or the move isn't possible.", InventoryModel.LastResult!.Text);
        Assert.False(InventoryModel.LastResult.Ok);
    }

    [Fact]
    public void SplitSendsTheCountAndFinishesWhenTheStackShrinks()
    {
        Put(MainItem, "Healing Kit", Item(Player, slot: 0, stack: 10));
        Fill(Pack, (Other, "Dagger"));
        Send(Req(InventoryActionKind.Split, MainItem, "Healing Kit", Pack, "Backpack", amount: 3));
        Assert.Equal([$"move {MainItem:X8} {Pack:X8} 2 3"], _view.Actions);
        Assert.Equal("Splitting 3 Healing Kit into Backpack", InventoryModel.Pending!.Label);
        Put(MainItem, "Healing Kit", Item(Player, slot: 0, stack: 7));
        Ticks();
        Assert.Equal("Split 3 Healing Kit into Backpack.", InventoryModel.LastResult!.Text);
        Assert.True(InventoryModel.LastResult.Ok);
    }

    [Fact]
    public void WieldSendsTheSlotMask()
    {
        Put(MainItem, "Healing Kit", Item(Player, slot: 0) with { ValidLocations = 0x100u });
        Send(Req(InventoryActionKind.Wield, MainItem, "Healing Kit", target: 0x100u));
        Assert.Equal([$"wield {MainItem:X8} 00000100"], _view.Actions);
        Put(MainItem, "Healing Kit", Item(0, wielder: Player));
        Ticks();
        Assert.Equal("Equipped Healing Kit.", InventoryModel.LastResult!.Text);
        Assert.True(InventoryModel.LastResult.Ok);
    }

    [Fact]
    public void DropSendsAMoveToTheWorldAndFinishesWhenTheItemIsGone()
    {
        Send(Req(InventoryActionKind.Drop, MainItem, "Healing Kit"));
        Assert.Equal([$"moveext {MainItem:X8} 00000000 0"], _view.Actions);
        _view.Objects.Remove(MainItem);
        Ticks();
        Assert.Equal("Dropped Healing Kit.", InventoryModel.LastResult!.Text);
        Assert.True(InventoryModel.LastResult.Ok);
    }

    [Fact]
    public void GiveGoesToACreature()
    {
        Put(Npc, "Bob", Item(0) with { ItemType = 0x10 });
        Send(Req(InventoryActionKind.Give, MainItem, "Healing Kit", Npc, "Bob"));
        Assert.Equal([$"give {MainItem:X8} {Npc:X8} 0"], _view.Actions);
        _view.Objects.Remove(MainItem);
        Ticks();
        Assert.Equal("Gave Healing Kit to Bob.", InventoryModel.LastResult!.Text);
    }

    [Fact]
    public void AnInventoryRefusalForTheItemIsReported()
    {
        Send(Req(InventoryActionKind.Move, MainItem, "Healing Kit", Pack, "Backpack", slot: 2));
        _view.Replies = new AcDream.Core.Items.ServerReplySnapshot(0, 0, 1, 0x36, 0x00A0, MainItem);
        Ticks();
        Assert.Equal("Refused: Moving Healing Kit to Backpack (error 0x36).", InventoryModel.LastResult!.Text);
        Assert.False(InventoryModel.LastResult.Ok);
    }

    [Fact]
    public void ABusyClientHoldsAMoveUntilItsGateOpens()
    {
        Put(Other, "Torch", Item(Player, slot: 1));
        _view.Contents[Player].Add(Other);
        Ticks(2);
        _view.PendingItemRequest = (3, Other);
        Assert.True(InventoryModel.Request(Req(InventoryActionKind.Move, MainItem, "Healing Kit", Pack, "Backpack", slot: 2), out string why), why);
        Ticks(1);
        Assert.Empty(_view.Actions);
        Assert.True(InventoryModel.Pending!.WaitingForGame);
        Assert.Equal("move of Torch", InventoryModel.Pending.BusyWith);
        _view.PendingItemRequest = null;
        Ticks(1);
        Assert.Equal([$"move {MainItem:X8} {Pack:X8} 2 0"], _view.Actions);
        Assert.False(InventoryModel.Pending!.WaitingForGame);
    }

    [Fact]
    public void ABatchSendsOneItemAtATimeAndStopsWhenCancelled()
    {
        Fill(Player, (0x80000021u, "Rope"), (0x80000022u, "Lamp"));
        Ticks(2);
        InventoryRequest[] drops =
        [
            Req(InventoryActionKind.Drop, MainItem, "Healing Kit"),
            Req(InventoryActionKind.Drop, 0x80000021u, "Rope"),
            Req(InventoryActionKind.Drop, 0x80000022u, "Lamp"),
        ];
        Assert.True(InventoryModel.RequestBatch(drops, out string why), why);
        Ticks(3);
        Assert.Equal([$"moveext {MainItem:X8} 00000000 0"], _view.Actions);
        Ticks(2);
        Assert.Single(_view.Actions);   // the first is still in the pack: nothing else goes out
        _view.Objects.Remove(MainItem);
        Ticks(5);
        Assert.Equal([$"moveext {MainItem:X8} 00000000 0", $"moveext 80000021 00000000 0"], _view.Actions);
        InventoryModel.CancelBatch();
        _view.Objects.Remove(0x80000021u);
        Ticks(5);
        Assert.Equal(2, _view.Actions.Count);
        Assert.False(InventoryModel.BatchRunning);
    }

    [Fact]
    public void HoveringAnUnidentifiedItemAsksTheServerOnce()
    {
        Ticks(2);
        InventoryModel.Hover(MainItem);
        Ticks(6);
        Assert.Equal([$"id {MainItem:X8}"], _view.Actions);
    }

    [Fact]
    public void HoveringAnIdentifiedItemAsksNothing()
    {
        Put(MainItem, "Healing Kit", Item(Player, slot: 0) with { Identified = true });
        Ticks(2);
        InventoryModel.Hover(MainItem);
        Ticks(6);
        Assert.Empty(_view.Actions);
    }
}
