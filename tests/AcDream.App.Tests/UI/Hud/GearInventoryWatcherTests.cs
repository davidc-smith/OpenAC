using AcDream.App.UI;
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

    private sealed class Requests : IGearRequestWatch
    {
        public PendingInventoryRequest? Pending { get; set; }
        public event Action<PendingInventoryRequest>? Completed;
        public void Complete(PendingInventoryRequest r) { Pending = null; Completed?.Invoke(r); }
    }

    private sealed class Store : IGearDeckStore
    {
        public string? Json;
        public string? Load(string toonKey) => Json;
        public void Save(string toonKey, string json) => Json = json;
    }

    private sealed class Rig
    {
        public required ClientObjectTable Table;
        public required Channel Channel;
        public readonly Requests Requests = new();
        public readonly Store Store = new();
        public readonly List<string> Reports = [];
        public string Key = "Toon";
        public required GearDeckSession Session;
        public required GearMoveQueue Queue;
        public required Sender Sender;

        /// <summary>Confirms each move as it goes out, so the queue sends them all.</summary>
        public void RunQueue()
        {
            for (int guard = 0; Queue.IsRunning && guard < 20; guard++)
            {
                Channel.Complete(Sender.Sent[^1].Item);
                Queue.Tick();
            }
            Assert.False(Queue.IsRunning);
        }

        public void Add(uint id, int size) => Table.Ingest(new WeenieData(id, "Coins", ItemType.Money, CoinWeenie, 0u, 0u, 0u, 0u,
            Value: null, StackSize: size, StackSizeMax: 100, Burden: null, ContainerId: Player, WielderId: 0u, ValidLocations: null,
            CurrentWieldedLocation: null, Priority: null, ItemsCapacity: null, ContainersCapacity: null,
            Structure: null, MaxStructure: null, Workmanship: null));
        public GearInventoryWatcher Watcher = null!;
        public double Now;
        public bool Dragging;

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

    private static Rig Make(bool complete = true, bool character = true)
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

        var rig = new Rig { Table = t, Session = null!, Sender = new Sender(), Queue = null!, Channel = new Channel() };
        var session = new GearDeckSession(rig.Store, () => rig.Key, () => true);
        rig.Session = session;
        if (character) session.Tick();
        session.Update(s => s.WithSaved(new SavedFilter("Coins", GearQuery.Empty.With(GearChip.NameContains("coin"))))
            .WithRules([new GearPackRule("Coins", Bag, OnArrival: true)]) with { CombineOnArrival = true });
        rig.Queue = new GearMoveQueue(rig.Channel, rig.Sender, () => rig.Now);
        rig.Watcher = new GearInventoryWatcher(() => t, () => Player, session, rig.Queue, o => o.Name, null, () => rig.Now,
            report: rig.Reports.Add, requests: rig.Requests, dragging: () => rig.Dragging);
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
        rig.RunQueue();
        // The loot drains into Coins, so the rule sorts Coins (the loot is gone).
        Assert.Equal(new[] { GearMove.Merge(Loot, Coins, 10), GearMove.Put(Coins, Bag, -1) }, rig.Sender.Sent);
    }

    [Fact]
    public void APartlyDrainedArrival_KeepsItsOwnRulePut_AndTheTargetStays()
    {
        Rig rig = Make();
        rig.Table.Get(Coins)!.StackSize = 90;
        rig.Watcher.Tick();
        rig.Add(0x1003, 30);                                   // 10 of it fills Coins to 100; 20 stay
        rig.Now = 1; rig.Watcher.Tick();
        rig.RunQueue();
        Assert.Equal(new[] { GearMove.Merge(0x1003, Coins, 10), GearMove.Put(0x1003, Bag, -1) }, rig.Sender.Sent);
    }

    [Fact]
    public void AnArrivalMergedInto_KeepsItsRulePut()
    {
        Rig rig = Make();
        rig.Watcher.Tick();
        rig.Add(0x1003, 80);                                   // fullest first: Coins tops up the new 80 stack
        rig.Now = 1; rig.Watcher.Tick();
        rig.RunQueue();
        Assert.Equal(new[] { GearMove.Merge(Coins, 0x1003, 20), GearMove.Put(0x1003, Bag, -1) }, rig.Sender.Sent);
    }

    [Fact]
    public void AFullPack_IsReportedOnce()
    {
        Rig rig = Make();
        rig.Session.Update(s => s with { CombineOnArrival = false });
        rig.Watcher.Tick();
        for (uint i = 0; i < 25; i++) rig.Add(0x2000 + i, 1);
        rig.Now = 1; rig.Watcher.Tick();
        Assert.Equal(new[] { "Pack 2 is full; 1 left where they were." }, rig.Reports);
    }

    [Fact]
    public void ASplitThisClientAskedFor_IsNotAnArrival_WhilePending()
    {
        Rig rig = Make();
        rig.Watcher.Tick();
        rig.Requests.Pending = new PendingInventoryRequest(1, InventoryRequestKind.SplitToContainer, Coins, rig.Table.Get(Coins), true);
        rig.Add(0x1003, 5);
        rig.Requests.Complete(rig.Requests.Pending.Value);
        rig.Now = 5; rig.Watcher.Tick();
        Assert.False(rig.Queue.IsRunning);
    }

    [Fact]
    public void ASplitThisClientAskedFor_IsNotAnArrival_JustAfterItCompleted()
    {
        Rig rig = Make();
        rig.Watcher.Tick();
        rig.Requests.Complete(new PendingInventoryRequest(1, InventoryRequestKind.SplitToContainer, Coins, rig.Table.Get(Coins), true));
        rig.Now = 0.9;
        rig.Add(0x1003, 5);
        rig.Now = 5; rig.Watcher.Tick();
        Assert.False(rig.Queue.IsRunning);
    }

    [Fact]
    public void AnAddLongAfterASplit_StillArrives()
    {
        Rig rig = Make();
        rig.Watcher.Tick();
        rig.Requests.Complete(new PendingInventoryRequest(1, InventoryRequestKind.SplitToContainer, Coins, rig.Table.Get(Coins), true));
        rig.Now = 1.0;
        rig.Add(0x1003, 5);
        rig.Now = 2; rig.Watcher.Tick();
        Assert.Equal("Sorting new items", rig.Queue.Label);
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
        rig.Add(0x1003, 5);
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
    public void ALockedItemRemovedFromTheInventory_LosesItsLock()
    {
        Rig rig = Make();
        rig.Add(0x1003, 5);
        rig.Table.ApplyConfirmedServerMove(0x1003, Bag, 0u, 0);
        rig.Session.Update(s => s.WithLock([Coins, 0x1003u, 0xBEEFu], true));
        Assert.True(rig.Table.Remove(Coins));                  // on the player
        Assert.True(rig.Table.Remove(0x1003));                 // in the side pack
        Assert.Equal(new[] { 0xBEEFu }, rig.Session.Settings.LockedIds);
    }

    [Fact]
    public void ALockedIdNotLoadedAtLogin_KeepsItsLock()
    {
        Rig rig = Make(complete: false);
        rig.Session.Update(s => s.WithLock([Coins, 0xDEADu], true));   // 0xDEAD: in house storage, say
        rig.Watcher.Tick();
        rig.MarkInventoryComplete();
        rig.Now = 5; rig.Watcher.Tick();
        Assert.Equal(new[] { Coins, 0xDEADu }, rig.Session.Settings.LockedIds);
    }

    [Fact]
    public void AClearedTable_KeepsTheLocks()
    {
        Rig rig = Make();
        rig.Session.Update(s => s.WithLock([Coins], true));
        rig.Table.Clear();                                     // logout or teleport
        rig.Watcher.Tick();
        Assert.Equal(new[] { Coins }, rig.Session.Settings.LockedIds);
    }

    [Fact]
    public void ALockedItemRemovedFromAnOutsideContainer_KeepsItsLock()
    {
        Rig rig = Make();
        rig.Session.Update(s => s.WithLock([Loot], true));
        Assert.True(rig.Table.Remove(Loot));                   // the corpse decays
        Assert.Equal(new[] { Loot }, rig.Session.Settings.LockedIds);
    }

    [Fact]
    public void ADraggedArrival_IsNotSorted()
    {
        Rig rig = Make();
        rig.Table.ApplyConfirmedServerMove(Loot, Player, 0u, 0);
        rig.Now = 0.2;
        GearDragHook.OnDragStarted(new ItemDragPayload(Loot, ItemDragSource.Inventory, 0, null), rig.Queue, rig.Watcher);
        rig.Now = 5; rig.Watcher.Tick();
        Assert.False(rig.Queue.IsRunning);
        Assert.Empty(rig.Sender.Sent);
    }

    [Fact]
    public void ArrivalsWait_WhileADragIsInProgress()
    {
        Rig rig = Make();
        rig.Table.ApplyConfirmedServerMove(Loot, Player, 0u, 0);
        rig.Dragging = true;
        rig.Now = 5; rig.Watcher.Tick();
        Assert.False(rig.Queue.IsRunning);
        rig.Dragging = false;
        rig.Watcher.Tick();
        Assert.Equal("Sorting new items", rig.Queue.Label);
    }

    [Fact]
    public void AnItemDragFromAnyWindow_CancelsTheRunningQueue()
    {
        Rig rig = Make();
        Assert.True(rig.Queue.Start("Tidying", [GearMove.Put(0x9999, Player, 0), GearMove.Put(0x9998, Player, 1)]));
        var log = new List<string>();
        Assert.True(GearDragHook.OnDragStarted(new ItemDragPayload(Coins, ItemDragSource.Ground, 0, null), rig.Queue, rig.Watcher, log.Add));
        Assert.False(rig.Queue.IsRunning);
        Assert.Single(log);
    }

    [Fact]
    public void AShortcutDrag_LeavesTheQueueRunning()
    {
        Rig rig = Make();
        Assert.True(rig.Queue.Start("Tidying", [GearMove.Put(0x9999, Player, 0)]));
        Assert.False(GearDragHook.OnDragStarted(new ItemDragPayload(Coins, ItemDragSource.ShortcutBar, 0, null), rig.Queue, rig.Watcher));
        Assert.True(rig.Queue.IsRunning);
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
