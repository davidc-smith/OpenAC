using AcDream.App.UI.Hud;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearInventoryWatcherTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, Corpse = 0xC0, Loot = 0x1001, Coins = 0x1002;
    private const uint CoinWeenie = 9;

    private sealed class Channel : IGearMoveChannel
    {
        public bool CanSend { get; set; } = true;
        public event Action<uint>? Completed;
        public event Action<uint>? Failed;
        public void Complete(uint id) => Completed?.Invoke(id);
        public void Fail(uint id) => Failed?.Invoke(id);
    }

    private sealed class Sender : IGearMoveSender
    {
        public readonly List<GearMove> Sent = [];
        public GearMoveResult Send(GearMove move) { Sent.Add(move); return GearMoveResult.Sent; }
    }

    private sealed class Rig
    {
        public required ClientObjectTable Table;
        public required GearDeckSession Session;
        public required GearMoveQueue Queue;
        public required Sender Sender;
        public GearInventoryWatcher Watcher = null!;
        public double Now;

        /// <summary>The login listings, as the server sends them: the player's list, then the side pack's.</summary>
        public void MarkInventoryComplete()
        {
            Table.InitializeInventoryManifest(Player, [new ContainerContentEntry(Bag, 1), new ContainerContentEntry(Coins, 0)]);
            Table.ReplaceContents(Bag, Array.Empty<uint>());
            Assert.True(Table.HasCompleteInventory(Player));
        }
    }

    private static ClientObject CoinStack(uint id, int size) => new()
    {
        ObjectId = id, Name = "Coins", Type = ItemType.Money, WeenieClassId = CoinWeenie, StackSize = size, StackSizeMax = 100,
    };

    private static Rig Make(bool complete = true)
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        t.AddOrUpdate(new ClientObject { ObjectId = Bag, Name = "Pack", Type = ItemType.Container, ItemsCapacity = 24 });
        t.AddOrUpdate(new ClientObject { ObjectId = Corpse, Name = "Corpse", Type = ItemType.Container, ItemsCapacity = 120 });
        t.AddOrUpdate(CoinStack(Loot, 10));
        t.AddOrUpdate(CoinStack(Coins, 40));
        Assert.True(t.MoveItem(Loot, Corpse, 0));
        Assert.True(t.MoveItem(Bag, Player, 0));
        Assert.True(t.MoveItem(Coins, Player, 0));

        var session = new GearDeckSession(null, () => "Toon", () => true);
        session.Update(s => s.WithSaved(new SavedFilter("Coins", GearQuery.Empty.With(GearChip.NameContains("coin"))))
            .WithRules([new GearPackRule("Coins", Bag, OnArrival: true)]) with { CombineOnArrival = true });
        var sender = new Sender();
        var rig = new Rig { Table = t, Session = session, Sender = sender, Queue = null! };
        rig.Queue = new GearMoveQueue(new Channel(), sender, () => rig.Now);
        rig.Watcher = new GearInventoryWatcher(() => t, () => Player, session, rig.Queue, o => o.Name, null, () => rig.Now);
        if (complete) rig.MarkInventoryComplete();
        return rig;
    }

    [Fact]
    public void ALootedStack_IsCombined_ThenSortedByRule_AfterTheQuietTime()
    {
        Rig rig = Make();
        rig.Table.ApplyConfirmedServerMove(Loot, Player, 0u, 0);   // the server's InventoryPutObjInContainer
        rig.Now = 0.4; rig.Watcher.Tick();
        Assert.False(rig.Queue.IsRunning);
        rig.Now = 0.5; rig.Watcher.Tick();
        Assert.Equal("Sorting new items", rig.Queue.Label);
        Assert.Equal(GearMove.Merge(Loot, Coins, 10), rig.Sender.Sent[0]);
    }

    [Fact]
    public void MovesInsideTheInventory_AreNotArrivals()
    {
        Rig rig = Make();
        rig.Table.ApplyConfirmedServerMove(Coins, Bag, 0u, 0);
        rig.Now = 5; rig.Watcher.Tick();
        Assert.False(rig.Queue.IsRunning);
    }

    [Fact]
    public void ALocalProjection_IsNotAnArrival()
    {
        Rig rig = Make();
        Assert.True(rig.Table.MoveItem(Loot, Player, 0));
        rig.Now = 5; rig.Watcher.Tick();
        Assert.False(rig.Queue.IsRunning);
    }

    [Fact]
    public void AnObjectAdded_IntoTheInventory_AfterItIsComplete_IsAnArrival()
    {
        Rig rig = Make();
        rig.Watcher.Tick();                                    // the first complete tick
        rig.Table.Ingest(new WeenieData(0x1003u, "Coins", ItemType.Money, CoinWeenie, 0u, 0u, 0u, 0u, Value: null,
            StackSize: 5, StackSizeMax: 100, Burden: null, ContainerId: Player, WielderId: 0u, ValidLocations: null,
            CurrentWieldedLocation: null, Priority: null, ItemsCapacity: null, ContainersCapacity: null,
            Structure: null, MaxStructure: null, Workmanship: null));
        rig.Now = 1; rig.Watcher.Tick();
        Assert.Equal("Sorting new items", rig.Queue.Label);
        Assert.Equal(GearMove.Merge(0x1003, Coins, 5), rig.Sender.Sent[0]);
    }

    [Fact]
    public void WithoutCombine_OnlyTheArrivalRulesRun()
    {
        Rig rig = Make();
        rig.Session.Update(s => s with { CombineOnArrival = false });
        rig.Table.ApplyConfirmedServerMove(Loot, Player, 0u, 0);
        rig.Now = 1; rig.Watcher.Tick();
        Assert.Equal(new[] { GearMove.Put(Loot, Bag, -1) }, rig.Sender.Sent);
    }

    [Fact]
    public void ARunningQueue_HoldsTheArrivals_UntilItEnds()
    {
        Rig rig = Make();
        Assert.True(rig.Queue.Start("Tidying", [GearMove.Put(0x9999, Player, 0)]));
        rig.Table.ApplyConfirmedServerMove(Loot, Player, 0u, 0);
        rig.Now = 1; rig.Watcher.Tick();
        Assert.Equal("Tidying", rig.Queue.Label);
        rig.Queue.Cancel();
        rig.Watcher.Tick();
        Assert.Equal("Sorting new items", rig.Queue.Label);
    }

    [Fact]
    public void StaleLocks_AreDroppedOnce_WhenTheInventoryIsComplete()
    {
        Rig rig = Make(complete: false);
        rig.Session.Update(s => s.WithLock([Coins, 0xDEADu], true));
        rig.Watcher.Tick();
        Assert.Contains(0xDEADu, rig.Session.Settings.LockedIds);
        rig.MarkInventoryComplete();
        rig.Watcher.Tick();
        Assert.Equal(new[] { Coins }, rig.Session.Settings.LockedIds);
        rig.Session.Update(s => s.WithLock([0xBEEFu], true));
        rig.Watcher.Tick();
        Assert.Contains(0xBEEFu, rig.Session.Settings.LockedIds);   // once per login
    }

    [Fact]
    public void SessionEnded_PrunesAgain_OnTheNextLogin()
    {
        Rig rig = Make();
        rig.Watcher.Tick();
        rig.Session.EndSession();
        rig.Session.Update(s => s.WithLock([0xBEEFu], true));
        rig.Watcher.Tick();
        Assert.Empty(rig.Session.Settings.LockedIds);
    }

    [Fact]
    public void Dispose_StopsListening()
    {
        Rig rig = Make();
        rig.Watcher.Dispose();
        rig.Table.ApplyConfirmedServerMove(Loot, Player, 0u, 0);
        rig.Now = 1; rig.Watcher.Tick();
        Assert.False(rig.Queue.IsRunning);
    }
}
