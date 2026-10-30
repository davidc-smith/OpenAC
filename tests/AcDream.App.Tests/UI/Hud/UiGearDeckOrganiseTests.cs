using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiGearDeckOrganiseTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, Coins30 = 0xC30, Coins50 = 0xC50, Misc0 = 0xA00, Misc1 = 0xA01, Misc2 = 0xA02, Misc3 = 0xA03;
    private const uint Helm = 0xE01, Sword = 0xE02;
    private const uint CoinWeenie = 273;

    private sealed class Fake : IGearItemActions
    {
        public readonly List<string> Calls = [];
        public bool Ready() => true;
        public void Report(string text) => Calls.Add($"report {text}");
        public bool PutInContainer(uint item, uint container, int placement, bool fromGround)
        {
            Calls.Add($"put {item:X} {container:X} {placement}");
            return true;
        }
        public bool Merge(uint from, uint to) { Calls.Add($"merge {from:X} {to:X}"); return true; }
        public bool WieldFromPaperdoll(uint item, EquipMask mask) => true;
        public bool WieldSecondary(uint item) => true;
        public bool Activate(uint item) => true;
        public bool OpenPack(uint pack) => true;
    }

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
        public required UiGearDeck Deck;
        public required Fake Actions;
        public required GearMoveQueue Queue;
        public required Channel Channel;
        public required Sender Sender;
        public required ItemLocks Locks;
        public required GearDeckSession Session;
        public readonly List<GearSplitRequest> SplitRequests = [];
        public (bool Toggle, bool Range) Mods;
    }

    private static void Place(ClientObjectTable t, ClientObject o, uint container, int slot)
    {
        t.AddOrUpdate(o);
        Assert.True(t.MoveItem(o.ObjectId, container, slot));
    }

    private static ClientObject Coins(uint id, int size) => new()
    {
        ObjectId = id, Name = "Pyreal", Type = ItemType.Money, WeenieClassId = CoinWeenie, StackSize = size, StackSizeMax = 100,
    };

    private static Rig Make(bool worn = false)
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        Place(t, new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, Name = "Pack" }, Player, 0);
        Place(t, Coins(Coins30, 30), Player, 0);
        Place(t, Coins(Coins50, 50), Player, 1);
        uint[] misc = [Misc0, Misc1, Misc2, Misc3];
        for (int i = 0; i < misc.Length; i++)
            Place(t, new ClientObject { ObjectId = misc[i], Name = $"Item {i}", Type = ItemType.Misc, WeenieClassId = 0x2000u + (uint)i }, Player, 2 + i);
        if (worn)
        {
            Wear(t, new ClientObject { ObjectId = Helm, WielderId = Player, Name = "Helm", ValidLocations = EquipMask.HeadWear }, EquipMask.HeadWear);
            Wear(t, new ClientObject { ObjectId = Sword, WielderId = Player, Name = "Sword", ValidLocations = EquipMask.MeleeWeapon }, EquipMask.MeleeWeapon);
        }
        var locks = new ItemLocks();
        var session = new GearDeckSession(null, () => "", () => false, locks: locks);
        var actions = new Fake();
        var channel = new Channel();
        var sender = new Sender();
        var queue = new GearMoveQueue(channel, sender, () => 0.0);
        var rig = new Rig { Deck = null!, Actions = actions, Queue = queue, Channel = channel, Sender = sender, Locks = locks, Session = session };
        var b = new GearDeckBindings(() => t, () => Player, () => 100, (_, _, _, _, _) => 0u, (_, _, _, _, _) => 0u,
            o => o.Name, session, actions, _ => false, () => { }, Modifiers: () => rig.Mods, Queue: queue, Locks: locks,
            OpenSplit: rig.SplitRequests.Add);
        rig.Deck = new UiGearDeck(b, null, null, () => 0.0);
        rig.Deck.Refresh();
        return rig;
    }

    private static void Wear(ClientObjectTable t, ClientObject o, EquipMask mask)
    {
        t.AddOrUpdate(o);
        Assert.True(t.MoveItem(o.ObjectId, Player, -1, mask));
    }

    /// <summary>The main pack's order after the puts, each taking its item out and inserting it at its slot.</summary>
    private static List<uint> After(Rig rig, IEnumerable<GearMove> moves)
    {
        var order = rig.Deck.Model.Contents(Player).Select(o => o.ObjectId).ToList();
        foreach (GearMove m in moves)
        {
            Assert.Equal(Player, m.Target);
            order.Remove(m.Item);
            order.Insert(m.Slot, m.Item);
        }
        return order;
    }

    /// <summary>Confirms each put as it goes out, so the queue sends them all.</summary>
    private static void RunQueue(Rig rig)
    {
        for (int guard = 0; rig.Queue.IsRunning && guard < 20; guard++)
        {
            rig.Channel.Complete(rig.Sender.Sent[^1].Item);
            rig.Queue.Tick();
        }
        Assert.False(rig.Queue.IsRunning);
    }

    private static (float X, float Y) Centre(DeckRect r) => (r.X + r.W / 2f, r.Y + r.H / 2f);

    private static void Send(UiGearDeck deck, int type, DeckRect at, object? payload = null)
    {
        var (x, y) = Centre(at);
        deck.HitTestLocal(x, y);
        deck.OnEvent(new UiEvent(0, deck, type, Data1: (int)x, Data2: (int)y, Payload: payload));
    }

    private static void Click(UiGearDeck deck, DeckRect at)
    {
        Send(deck, UiEventType.MouseDown, at);
        Send(deck, UiEventType.Click, at);
    }

    private static int IndexOf(Rig rig, uint id)
    {
        IReadOnlyList<GearItemView> shown = rig.Deck.Shown;
        for (int i = 0; i < shown.Count; i++)
            if (shown[i].Id == id) return i;
        throw new InvalidOperationException($"0x{id:X} is not shown");
    }

    private static DeckRect CellOf(Rig rig, uint id) => rig.Deck.Geometry.GridCell(IndexOf(rig, id), rig.Deck.ScrollRow)!.Value;

    /// <summary>Clicks the first row, then toggle-clicks the rest.</summary>
    private static void SelectRows(Rig rig, params uint[] ids)
    {
        for (int i = 0; i < ids.Length; i++)
        {
            rig.Mods = (i > 0, false);
            Click(rig.Deck, CellOf(rig, ids[i]));
        }
        rig.Mods = (false, false);
        Assert.Equal(ids.Order(), rig.Deck.Selection.Order());
    }

    /// <summary>A drag of <paramref name="id"/> from the deck, released over <paramref name="target"/>.</summary>
    private static void DropOn(Rig rig, uint id, DeckRect target)
    {
        var payload = new ItemDragPayload(id, ItemDragSource.Inventory, IndexOf(rig, id), null);
        rig.Deck.OnEvent(new UiEvent(0, rig.Deck, UiEventType.DragBegin, Payload: payload));
        Send(rig.Deck, UiEventType.DropReleased, target, payload);
    }

    [Fact]
    public void CombineButton_ShowsWhenStacksCanMerge_AndStartsCombining()
    {
        Rig rig = Make();
        Assert.True(rig.Deck.Geometry.Input.CombineButton);
        Click(rig.Deck, rig.Deck.Geometry.CombineButton);
        Assert.True(rig.Queue.IsRunning);
        Assert.Equal("Combining", rig.Queue.Label);
        Assert.Equal(GearMove.Merge(Coins30, Coins50, 30), rig.Sender.Sent.Single());
        rig.Deck.Refresh();
        Assert.True(rig.Deck.Geometry.Input.Queue);
        Assert.False(rig.Deck.Geometry.Input.CombineButton);
        Assert.Equal("Combining 0 / 1", rig.Deck.FooterLeftText);
        Click(rig.Deck, rig.Deck.Geometry.StopButton);
        Assert.False(rig.Queue.IsRunning);
        Assert.False(rig.Deck.Geometry.Input.Queue);
    }

    [Fact]
    public void QueueText_FollowsTheQueue()
    {
        Rig rig = Make();
        rig.Queue.Start("Moving", [GearMove.Put(Misc0, Bag, 0), GearMove.Put(Misc1, Bag, 1)]);
        Assert.Equal("Moving 0 / 2", rig.Deck.FooterLeftText);
        rig.Channel.Complete(Misc0);
        Assert.Equal("Moving 1 / 2", rig.Deck.FooterLeftText);
        rig.Queue.Cancel();
        Assert.Equal("", rig.Deck.FooterLeftText);   // nothing selected in the pack view
    }

    [Fact]
    public void ALockedEmptierStack_HidesCombine_AndTheRowsCarryTheLock()
    {
        Rig rig = Make();
        rig.Locks.Set(Coins30, true);
        rig.Deck.Refresh();
        Assert.True(rig.Deck.Shown[IndexOf(rig, Coins30)].Locked);
        Assert.Empty(rig.Deck.CombineMoves);
        Assert.False(rig.Deck.Geometry.Input.CombineButton);
    }

    [Fact]
    public void ADragFromTheDeck_CancelsARunningQueue()
    {
        Rig rig = Make();
        rig.Queue.Start("Tidying", [GearMove.Put(Misc0, Player, 5)]);
        rig.Deck.OnEvent(new UiEvent(0, rig.Deck, UiEventType.DragBegin, Payload: new ItemDragPayload(Misc0, ItemDragSource.Inventory, 0, null)));
        Assert.False(rig.Queue.IsRunning);
    }

    [Fact]
    public void DroppingASelection_OnAPack_MovesEveryPickedItem_InOrder()
    {
        Rig rig = Make();
        SelectRows(rig, Misc0, Misc1, Misc2);
        DropOn(rig, Misc1, rig.Deck.Geometry.SidePack(1));   // the side pack's cell
        Assert.Equal("Moving", rig.Queue.Label);
        Assert.Equal(GearMove.Put(Misc0, Bag, 0), rig.Sender.Sent[0]);
        rig.Channel.Complete(Misc0); rig.Queue.Tick();
        Assert.Equal(GearMove.Put(Misc1, Bag, 1), rig.Sender.Sent[1]);
        rig.Channel.Complete(Misc1); rig.Queue.Tick();
        Assert.Equal(GearMove.Put(Misc2, Bag, 2), rig.Sender.Sent[2]);
        Assert.DoesNotContain(rig.Actions.Calls, c => c.StartsWith("put"));   // nothing went round the queue
    }

    [Fact]
    public void DroppingASelection_OnACell_MovesFromThatCellsSlot()
    {
        Rig rig = Make();
        SelectRows(rig, Misc1, Misc2);
        GearItemView coins = rig.Deck.Shown[IndexOf(rig, Coins50)];
        DropOn(rig, Misc2, CellOf(rig, Coins50));
        Assert.Equal(GearMove.Put(Misc1, Player, coins.Slot), rig.Sender.Sent[0]);
        rig.Channel.Complete(Misc1); rig.Queue.Tick();
        Assert.Equal(GearMove.Put(Misc2, Player, coins.Slot + 1), rig.Sender.Sent[1]);
    }

    [Fact]
    public void ASelectionDroppedOnItself_IsAlreadyInPlace_AndSendsNothing()
    {
        Rig rig = Make();
        SelectRows(rig, Misc0, Misc1, Misc2);   // Misc3 follows, unselected
        DropOn(rig, Misc2, CellOf(rig, Misc1));
        Assert.False(rig.Queue.IsRunning);
        Assert.Empty(rig.Sender.Sent);
        Assert.DoesNotContain(rig.Actions.Calls, c => c.StartsWith("put"));
    }

    [Fact]
    public void ADropOnASelectedRow_WithAnUnselectedItemAfter_KeepsTheSelectionTogether()
    {
        Rig rig = Make();
        SelectRows(rig, Misc0, Misc2);
        DropOn(rig, Misc2, CellOf(rig, Misc0));   // the anchor is Misc1, the first unselected item from Misc0's slot
        RunQueue(rig);
        Assert.Equal([Coins30, Coins50, Misc0, Misc2, Misc1, Misc3], After(rig, rig.Sender.Sent));
    }

    [Fact]
    public void ADropOnAnUnselectedRowLaterInThePack_PlacesTheSelectionJustBeforeIt()
    {
        Rig rig = Make();
        SelectRows(rig, Misc0, Misc1);
        DropOn(rig, Misc0, CellOf(rig, Misc3));   // Misc2 sits between the selection and the target
        RunQueue(rig);
        Assert.Equal(2, rig.Sender.Sent.Count);
        Assert.Equal([Coins30, Coins50, Misc2, Misc0, Misc1, Misc3], After(rig, rig.Sender.Sent));
    }

    [Fact]
    public void ADropOnAnEarlierRow_PlacesTheSelectionJustBeforeIt()
    {
        Rig rig = Make();
        SelectRows(rig, Misc1, Misc3);
        DropOn(rig, Misc3, CellOf(rig, Coins30));
        RunQueue(rig);
        Assert.Equal([Misc1, Misc3, Coins30, Coins50, Misc0, Misc2], After(rig, rig.Sender.Sent));
    }

    [Fact]
    public void DraggingAWornRow_OfAWornSelection_TakesTheOneItemPath()
    {
        Rig rig = Make(worn: true);
        rig.Deck.SetQuery(GearQuery.Empty.With(GearChip.WornOnly));
        rig.Deck.Refresh();
        SelectRows(rig, Helm, Sword);
        DropOn(rig, Helm, rig.Deck.Geometry.SidePack(1));
        Assert.False(rig.Queue.IsRunning);
        Assert.Empty(rig.Sender.Sent);
        Assert.Contains(rig.Actions.Calls, c => c.StartsWith($"put {Helm:X}"));
    }

    [Fact]
    public void AMouseUpAfterADrag_EndsTheDecksDrag()
    {
        Rig rig = Make();
        SelectRows(rig, Misc0, Misc1);
        var payload = new ItemDragPayload(Misc1, ItemDragSource.Inventory, 0, null);
        rig.Deck.OnEvent(new UiEvent(0, rig.Deck, UiEventType.DragBegin, Payload: payload));
        Send(rig.Deck, UiEventType.MouseUp, rig.Deck.Geometry.SidePack(1));   // that drag ended elsewhere
        Send(rig.Deck, UiEventType.DropReleased, rig.Deck.Geometry.SidePack(1), payload);
        Assert.False(rig.Queue.IsRunning);
        Assert.Empty(rig.Sender.Sent);
    }

    [Fact]
    public void DroppingOneUnselectedItem_StillMovesJustThatItem()
    {
        Rig rig = Make();
        SelectRows(rig, Misc0, Misc1);
        DropOn(rig, Misc2, rig.Deck.Geometry.SidePack(1));
        Assert.False(rig.Queue.IsRunning);
        Assert.Contains(rig.Actions.Calls, c => c.StartsWith("put"));
    }

    [Fact]
    public void ADropFromOutsideTheDeck_IsNeverAGroupMove()
    {
        Rig rig = Make();
        SelectRows(rig, Misc0, Misc1);
        var payload = new ItemDragPayload(Misc1, ItemDragSource.Inventory, 0, null);   // no DragBegin on the deck
        Send(rig.Deck, UiEventType.DropReleased, rig.Deck.Geometry.SidePack(1), payload);
        Assert.False(rig.Queue.IsRunning);
        Assert.Empty(rig.Sender.Sent);
    }

    private static (UiRoot Root, Rig Rig) Rooted()
    {
        Rig rig = Make();
        var root = new UiRoot { Width = 1200, Height = 900 };
        root.AddChild(rig.Deck);
        rig.Deck.Refresh();
        return (root, rig);
    }

    private static void RightClick(UiRoot root, UiGearDeck deck, DeckRect r)
    {
        var o = deck.ScreenPosition;
        int x = (int)(o.X + r.X + r.W / 2f), y = (int)(o.Y + r.Y + r.H / 2f);
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Right, x, y);
        root.OnMouseUp(UiMouseButton.Right, x, y);
    }

    private static int SlotOf(Rig rig, uint id) => rig.Deck.Shown[IndexOf(rig, id)].Slot;

    private static void Choose(Rig rig, string label)
    {
        IReadOnlyList<string> items = rig.Deck.Menu.ItemsSource!();
        int i = items.ToList().IndexOf(label);
        Assert.True(i >= 0, $"no '{label}' in [{string.Join(", ", items)}]");
        Assert.True(rig.Deck.Menu.ItemsEnabledSource!()[i], $"'{label}' is disabled");
        rig.Deck.Menu.Selected!(i);
        rig.Deck.Menu.Close();
    }

    [Fact]
    public void LockFromTheItemMenu_LocksTheSelection_AndTheMenuThenOffersUnlock()
    {
        var (root, rig) = Rooted();
        SelectRows(rig, Misc0, Misc1);
        RightClick(root, rig.Deck, CellOf(rig, Misc0));
        Choose(rig, "Lock");
        Assert.True(rig.Locks.IsLocked(Misc0) && rig.Locks.IsLocked(Misc1));
        Assert.Equal(new uint[] { Misc0, Misc1 }.Order(), rig.Session.Settings.LockedIds);
        RightClick(root, rig.Deck, CellOf(rig, Misc0));
        Choose(rig, "Unlock");
        Assert.Empty(rig.Locks.Ids);
    }

    [Fact]
    public void AModifiedDragOfAStack_OntoAPack_AsksForASplit()
    {
        Rig rig = Make();
        rig.Mods = (true, false);
        DropOn(rig, Coins50, rig.Deck.Geometry.SidePack(1));
        GearSplitRequest r = rig.SplitRequests.Single();
        Assert.Equal((Coins50, 50, Bag, 0), (r.Stack, r.Max, r.Container, r.Slot));
        Assert.Empty(rig.Sender.Sent);
        Assert.False(rig.Queue.IsRunning);
        Assert.Empty(rig.Actions.Calls);
    }

    [Fact]
    public void SplitFromTheItemMenu_AsksForTheStackAndItsNextSlot()
    {
        var (root, rig) = Rooted();
        RightClick(root, rig.Deck, CellOf(rig, Coins50));
        Choose(rig, "Split...");
        GearSplitRequest r = rig.SplitRequests.Single();
        Assert.Equal((Coins50, 50, Player), (r.Stack, r.Max, r.Container));
        Assert.Equal(SlotOf(rig, Coins50) + 1, r.Slot);
    }

    [Fact]
    public void SplitIsDisabledForASingleItem()
    {
        var (root, rig) = Rooted();
        RightClick(root, rig.Deck, CellOf(rig, Misc0));
        int i = rig.Deck.Menu.ItemsSource!().ToList().IndexOf("Split...");
        Assert.False(rig.Deck.Menu.ItemsEnabledSource!()[i]);
    }

    [Fact]
    public void TidyFromThePackMenu_QueuesTheTidyMoves()
    {
        var (root, rig) = Rooted();
        RightClick(root, rig.Deck, rig.Deck.Geometry.SidePack(0));
        Assert.Equal(new[] { "Tidy this pack", "Combine stacks", "Apply pack rules", "-", "Pack rules..." }, rig.Deck.Menu.ItemsSource());
        Choose(rig, "Tidy this pack");
        Assert.Equal("Tidying", rig.Queue.Label);
    }

    [Fact]
    public void CombineLikeStacks_TakesOnlyTheSelectionsKinds()
    {
        var (root, rig) = Rooted();
        RightClick(root, rig.Deck, CellOf(rig, Coins30));
        Choose(rig, "Combine like stacks");
        Assert.Equal(GearMove.Merge(Coins30, Coins50, 30), rig.Sender.Sent.Single());
    }
}
