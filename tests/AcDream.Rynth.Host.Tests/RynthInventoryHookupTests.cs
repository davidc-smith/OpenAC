using System.Reflection;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.Net;
using static AcDream.Rynth.Host.Tests.InventoryFixture;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthInventoryHookupTests : IDisposable
{
    private readonly InventoryFixture _fx = new();
    private FakeGameView _view => _fx.View;

    public void Dispose() => _fx.Dispose();

    [Fact]
    public void TheModelPublishesThePlayersPacksItemsAndWornThings()
    {
        InventoryModel.Subscribe();
        try
        {
            for (int i = 0; i < 3; i++) { InventoryModel.MainThreadTick(); Thread.Sleep(200); }
            InventoryView v = InventoryModel.Current;
            Assert.Contains(v.Packs, p => p.Id == Pack && p.Items.Any(it => it.Id == InPack));
            Assert.Contains(v.Worn, it => it.Id == Worn);
            Assert.True(v.Packs[0].IsMain);
            Assert.Equal(Player, v.Packs[0].Id);
            Assert.Contains(v.Packs[0].Items, it => it.Id == MainItem);
            Assert.Equal(1200, v.Burden);
            Assert.Equal(5000L, v.Pyreals);
        }
        finally { InventoryModel.Unsubscribe(); }
    }

    [Fact]
    public void ItemFieldsComeFromTheObjectTable()
    {
        Assert.True(ClientObjectHooks.TryReadItemFields(InPack, out ClientObjectHooks.ItemPwdFields f));
        Assert.Equal(Pack, f.Container);
        Assert.Equal(7u, f.StackSize);
        Assert.True(ClientObjectHooks.TryReadItemFields(Pack, out f));
        Assert.Equal(PackSlotFlag, f.Bitfield);
        Assert.False(ClientObjectHooks.TryReadItemFields(0x80000099u, out _));
    }

    [Fact]
    public void StrengthIsThePlayersBuffedAttributeOnly()
    {
        _view.Progress = new RynthPlayerProgress([], [new RynthStatProgress(1, 0, 0, 0, 200, 100)], [], 1, 0, 0, 0, 0, 0, 1f);
        Assert.True(ClientObjectHooks.TryGetObjectAttribute(Player, 1, 0, out uint buffed));
        Assert.Equal(200u, buffed);
        Assert.True(ClientObjectHooks.TryGetObjectAttribute(Player, 1, 1, out uint unbuffed));
        Assert.Equal(100u, unbuffed);
        Assert.False(ClientObjectHooks.TryGetObjectAttribute(MainItem, 1, 0, out _));
    }

    [Fact]
    public void TheGateIsTheClientsPendingRequestAndAttack()
    {
        Assert.True(ClientActionGates.IsRequestGateAvailable);
        _view.PendingItemRequest = (3, InPack);
        Assert.True(ClientActionGates.TryReadPending(out uint type, out uint obj, out double age));
        Assert.Equal((3u, InPack, -1d), (type, obj, age));
        _view.PendingItemRequest = null;
        Assert.True(ClientActionGates.TryReadPending(out type, out _, out _));
        Assert.Equal(0u, type);
        _view.AttackInProgress = true;
        Assert.True(ClientActionGates.TryReadAttacking(out bool attacking) && attacking);
    }

    [Fact]
    public void OrderComesFromSlotsItemsAndPacksApart()
    {
        const uint Second = 0x80000021u;
        _fx.Put(Second, "Second", Item(Player, slot: 1));
        _view.Contents[Player] = [Pack, Second, MainItem, Worn];   // MainItem is slot 0

        Assert.True(InventoryOrder.TryGetOrder(Player, false, out uint[] items));
        Assert.Equal([MainItem, Second], items);
        Assert.True(InventoryOrder.TryGetOrder(Player, true, out uint[] packs));
        Assert.Equal([Pack], packs);

        _fx.Put(Second, "Second", Item(Player, slot: -1));
        Assert.False(InventoryOrder.TryGetOrder(Player, false, out _));
    }

    [Fact]
    public void ARevisionChangeMarksTheModelDirty()
    {
        FieldInfo dirty = typeof(InventoryModel).GetField("_dirty", BindingFlags.Static | BindingFlags.NonPublic)!;
        dirty.SetValue(null, 0);
        _fx.Hookup.Tick();
        Assert.Equal(0, dirty.GetValue(null));
        _view.Revision++;
        _fx.Hookup.Tick();
        Assert.Equal(1, dirty.GetValue(null));
    }

    [Fact]
    public void DisposeUnhooksOnlyWhatIsStillOurs()
    {
        _fx.Hookup.Dispose();
        Assert.False(ClientHelperHooks.HasDropItem);
        Assert.False(ClientActionGates.IsRequestGateAvailable);
    }
}
