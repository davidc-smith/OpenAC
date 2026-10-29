using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearSalvageHandOffTests
{
    private const uint Player = 0x50000001, First = 0xA00;
    private const uint Steel = 64;

    private sealed class FakeSalvage : IGearSalvage
    {
        public readonly HashSet<uint> Addable = [];
        public readonly HashSet<uint> RefuseOnAdd = [];
        public readonly List<uint> Added = [];
        public readonly List<string> Reports = [];
        public bool Open;
        private int _revision;
        public bool IsOpen => Open;
        public int Revision => _revision;
        public bool CanAdd(uint id) => Addable.Contains(id);
        public bool Add(uint id)
        {
            if (RefuseOnAdd.Contains(id) || !Addable.Remove(id)) return false;
            Added.Add(id);
            _revision++;
            return true;
        }
        public void Report(string text) => Reports.Add(text);
    }

    private static GearItemView Row(uint id, bool worn = false, bool pack = false) =>
        new(id, $"Row {id:X}", GearTypeGroup.Salvage, Steel, 0f, 0, 0, 1, 1, Player, 0, 0, worn, pack);

    [Fact]
    public void Candidates_DropWornPackAndRefusedRows_KeepingOrder()
    {
        var s = new FakeSalvage();
        foreach (uint id in new uint[] { 1, 2, 3, 4, 6 }) s.Addable.Add(id);   // 5 is not addable
        GearItemView[] rows = [Row(1), Row(2, worn: true), Row(3, pack: true), Row(4), Row(5), Row(6)];
        Assert.Equal(new uint[] { 1, 4, 6 }, GearSalvageHandOff.Candidates(rows, s).Select(r => r.Id));
    }

    [Fact]
    public void AddAll_ReportsAShortfall()
    {
        var s = new FakeSalvage();
        foreach (uint id in new uint[] { 1, 2, 3 }) s.Addable.Add(id);
        s.RefuseOnAdd.Add(2);
        int added = GearSalvageHandOff.AddAll([Row(1), Row(2), Row(3)], s);
        Assert.Equal(2, added);
        Assert.Equal(new uint[] { 1, 3 }, s.Added);
        Assert.Equal(["Added 2 of 3 to salvage."], s.Reports);
    }

    [Fact]
    public void AddAll_WithEveryRowRefused_ReportsNone()
    {
        var s = new FakeSalvage();
        Assert.Equal(0, GearSalvageHandOff.AddAll([Row(1), Row(2)], s));
        Assert.Equal(["None of those can be salvaged with that tool."], s.Reports);
    }

    [Fact]
    public void AddAll_ThatAddsEverything_ReportsNothing()
    {
        var s = new FakeSalvage();
        s.Addable.Add(1);
        Assert.Equal(1, GearSalvageHandOff.AddAll([Row(1)], s));
        Assert.Empty(s.Reports);
    }

    private static (UiGearDeck Deck, FakeSalvage Salvage) MakeDeck()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        for (uint i = 0; i < 3; i++)
        {
            t.AddOrUpdate(new ClientObject
            {
                ObjectId = First + i, Name = $"Steel {i}", Type = ItemType.TinkeringMaterial, MaterialType = Steel, WeenieClassId = 0x1000u + i,
            });
            Assert.True(t.MoveItem(First + i, Player, (int)i));
        }
        t.AddOrUpdate(new ClientObject { ObjectId = 0xC00, Name = "Plain", Type = ItemType.Misc });
        Assert.True(t.MoveItem(0xC00, Player, 3));
        var salvage = new FakeSalvage { Open = true };
        for (uint i = 0; i < 3; i++) salvage.Addable.Add(First + i);
        var session = new GearDeckSession(null, () => "", () => false);
        var b = new GearDeckBindings(() => t, () => Player, () => 100, (_, _, _, _, _) => 0u, (_, _, _, _, _) => 0u,
            o => o.Name, session, new NoActions(), _ => false, () => { }, Modifiers: () => (false, false), Salvage: salvage);
        var deck = new UiGearDeck(b, null, null, () => 0.0);
        deck.Refresh();
        deck.SetQuery(GearQuery.Empty.With(GearChip.OfType(GearTypeGroup.Salvage)));
        deck.Refresh();
        return (deck, salvage);
    }

    private sealed class NoActions : IGearItemActions
    {
        public bool Ready() => true;
        public void Report(string text) { }
        public bool PutInContainer(uint item, uint container, int placement, bool fromGround) => true;
        public bool Merge(uint from, uint to) => true;
        public bool WieldFromPaperdoll(uint item, EquipMask mask) => true;
        public bool WieldSecondary(uint item) => true;
        public bool Activate(uint item) => true;
        public bool OpenPack(uint pack) => true;
    }

    private static void Click(UiGearDeck deck, DeckRect r)
    {
        float x = r.X + r.W / 2f, y = r.Y + r.H / 2f;
        deck.HitTestLocal(x, y);
        deck.OnEvent(new UiEvent(0, deck, UiEventType.MouseDown, Data1: (int)x, Data2: (int)y));
        deck.OnEvent(new UiEvent(0, deck, UiEventType.Click, Data1: (int)x, Data2: (int)y));
    }

    [Fact]
    public void ClosedSalvage_HasNoFooterButton()
    {
        var (deck, salvage) = MakeDeck();
        Assert.True(deck.Geometry.Input.SalvageButton);
        salvage.Open = false;
        salvage.Addable.Add(0xFFFF);   // a revision-visible change so the cache recomputes
        deck.Refresh();
        deck.SetQuery(GearQuery.Empty);
        deck.Refresh();
        Assert.False(deck.Geometry.Input.SalvageButton);
    }

    [Fact]
    public void FooterButton_CountsTargetsAndAddsExactlyTheSelection()
    {
        var (deck, salvage) = MakeDeck();
        Assert.Equal(3, deck.Shown.Count);
        Assert.Equal(3, deck.SalvageCount);

        Click(deck, deck.Geometry.GridCell(1, 0)!.Value);
        deck.Refresh();
        Assert.Equal(1, deck.SalvageCount);
        Assert.True(deck.Geometry.Input.SalvageButton);

        uint picked = deck.Shown[1].Id;
        Click(deck, deck.Geometry.FooterButton);
        Assert.Equal(new[] { picked }, salvage.Added);
    }

    [Fact]
    public void PlainPackView_ShowsTheFooterButtonOnlyWhileSomethingIsSelected()
    {
        var (deck, salvage) = MakeDeck();
        deck.SetQuery(GearQuery.Empty);
        deck.Refresh();
        Assert.False(deck.ShowsResults);
        Assert.False(deck.Geometry.Input.SalvageButton);

        Click(deck, deck.Geometry.GridCell(0, 0)!.Value);
        deck.Refresh();
        Assert.True(deck.Geometry.Input.SalvageButton);
        Assert.Equal(1, deck.SalvageCount);
        Click(deck, deck.Geometry.FooterButton);
        Assert.Single(salvage.Added);
    }

    [Fact]
    public void FooterButton_WithNoSelection_AddsEveryShownRow()
    {
        var (deck, salvage) = MakeDeck();
        Click(deck, deck.Geometry.FooterButton);
        Assert.Equal(deck.Shown.Select(r => r.Id), salvage.Added);
    }

    [Fact]
    public void ItemMenu_StartsWithTheSalvageEntry()
    {
        var (deck, _) = MakeDeck();
        var root = new UiRoot { Width = 1200, Height = 900 };
        root.AddChild(deck);
        deck.Refresh();
        DeckRect cell = deck.Geometry.GridCell(0, 0)!.Value;
        var o = deck.ScreenPosition;
        int x = (int)(o.X + cell.X + cell.W / 2f), y = (int)(o.Y + cell.Y + cell.H / 2f);
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Right, x, y);
        root.OnMouseUp(UiMouseButton.Right, x, y);
        IReadOnlyList<string> items = deck.Menu.ItemsSource!();
        Assert.Equal("Add 1 to salvage", items[0]);
        Assert.Equal(UiMarkupContextMenu.Separator, items[1]);
        Assert.True(deck.Menu.ItemsEnabledSource!()[0]);
    }
}
