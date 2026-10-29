using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiGearDeckSelectionTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, First = 0xA00, Salvage = 0xD1, BagSteel = 0xD3;
    private const uint Steel = 64;

    private sealed class Fake : IGearItemActions
    {
        public readonly List<string> Calls = [];
        public bool Ready() => true;
        public void Report(string text) { }
        public bool PutInContainer(uint item, uint container, int placement, bool fromGround) => true;
        public bool Merge(uint from, uint to) => true;
        public bool WieldFromPaperdoll(uint item, EquipMask mask) => true;
        public bool WieldSecondary(uint item) => true;
        public bool Activate(uint item) => true;
        public bool OpenPack(uint pack) { Calls.Add($"open {pack:X}"); return true; }
    }

    private static void Place(ClientObjectTable t, ClientObject o, uint container, int slot)
    {
        t.AddOrUpdate(o);
        Assert.True(t.MoveItem(o.ObjectId, container, slot));
    }

    private static ClientObject Steely(uint id) => new()
    {
        ObjectId = id, Name = $"Steel {id:X}", Type = ItemType.TinkeringMaterial, MaterialType = Steel, WeenieClassId = 0x1000u + id,
    };

    private sealed class Rig
    {
        public required UiGearDeck Deck;
        public required Fake Actions;
        public (bool Toggle, bool Range) Mods;
    }

    private static Rig Make(int miscCount = 6, bool list = false)
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        Place(t, new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, Name = "Pack" }, Player, 0);
        Place(t, Steely(Salvage), Player, 0);
        for (int i = 0; i < miscCount; i++)
            Place(t, new ClientObject { ObjectId = First + (uint)i, Name = $"Item {i}", Type = ItemType.Misc }, Player, 1 + i);
        Place(t, Steely(BagSteel), Bag, 0);
        var session = new GearDeckSession(null, () => "", () => false);
        if (list) session.Update(s => s with { View = GearPackView.List });
        var actions = new Fake();
        var rig = new Rig { Deck = null!, Actions = actions };
        var b = new GearDeckBindings(() => t, () => Player, () => 100, (_, _, _, _, _) => 0u, (_, _, _, _, _) => 0u,
            o => o.Name, session, actions, _ => false, () => { }, Modifiers: () => rig.Mods);
        rig.Deck = new UiGearDeck(b, null, null, () => 0.0);
        rig.Deck.Refresh();
        return rig;
    }

    private static (UiRoot Root, Rig Rig) Rooted()
    {
        Rig rig = Make();
        var root = new UiRoot { Width = 1200, Height = 900 };
        root.AddChild(rig.Deck);
        rig.Deck.Refresh();
        return (root, rig);
    }

    private static (float X, float Y) Centre(DeckRect r) => (r.X + r.W / 2f, r.Y + r.H / 2f);

    private static void Send(UiGearDeck deck, int type, DeckRect at)
    {
        var (x, y) = Centre(at);
        deck.HitTestLocal(x, y);
        deck.OnEvent(new UiEvent(0, deck, type, Data1: (int)x, Data2: (int)y));
    }

    private static void RightClick(UiRoot root, UiGearDeck deck, DeckRect r)
    {
        var o = deck.ScreenPosition;
        int x = (int)(o.X + r.X + r.W / 2f), y = (int)(o.Y + r.Y + r.H / 2f);
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Right, x, y);
        root.OnMouseUp(UiMouseButton.Right, x, y);
    }

    private static void ClickItem(Rig rig, int index, bool toggle = false, bool range = false)
    {
        rig.Mods = (toggle, range);
        DeckRect cell = rig.Deck.Geometry.GridCell(index, rig.Deck.ScrollRow)!.Value;
        Send(rig.Deck, UiEventType.MouseDown, cell);
        Send(rig.Deck, UiEventType.Click, cell);
    }

    private static uint Id(Rig rig, int index) => rig.Deck.Shown[index].Id;

    [Fact]
    public void PlainAndToggleClicks_BuildTheSelection()
    {
        Rig rig = Make();
        ClickItem(rig, 2);
        Assert.Equal(new[] { Id(rig, 2) }, rig.Deck.Selection);
        ClickItem(rig, 4, toggle: true);
        Assert.Equal(new[] { Id(rig, 2), Id(rig, 4) }.Order(), rig.Deck.Selection.Order());
        ClickItem(rig, 2, toggle: true);
        Assert.Equal(new[] { Id(rig, 4) }, rig.Deck.Selection);
    }

    [Fact]
    public void ARangeClick_SelectsFromTheAnchor()
    {
        Rig rig = Make();
        ClickItem(rig, 1);
        ClickItem(rig, 3, range: true);
        Assert.Equal(new[] { Id(rig, 1), Id(rig, 2), Id(rig, 3) }.Order(), rig.Deck.Selection.Order());
        ClickItem(rig, 0, range: true);
        Assert.Equal(new[] { Id(rig, 0), Id(rig, 1) }.Order(), rig.Deck.Selection.Order());
    }

    [Fact]
    public void APressOnEmptyContentsSpace_ClearsTheSelection()
    {
        Rig rig = Make(miscCount: 2);
        ClickItem(rig, 1);
        DeckRect c = rig.Deck.Geometry.Contents;
        var empty = new DeckRect(c.X + 4f, c.Y + c.H - 6f, 4f, 4f);
        Assert.Equal(GearZone.Frame, rig.Deck.Geometry.HitAt(empty.X + 2f, empty.Y + 2f).Zone);
        Send(rig.Deck, UiEventType.MouseDown, empty);
        Assert.Empty(rig.Deck.Selection);
    }

    [Fact]
    public void ANewQuery_PrunesTheSelectionToWhatIsStillShown()
    {
        Rig rig = Make();
        ClickItem(rig, 2);
        ClickItem(rig, 3, toggle: true);
        rig.Deck.SetQuery(GearQuery.Empty.With(GearChip.OfMaterial(Steel)));
        Assert.Empty(rig.Deck.Selection);
    }

    [Fact]
    public void ARightClickOnAnItem_SelectsItAndListsTheItemMenu()
    {
        var (root, rig) = Rooted();
        DeckRect cell = rig.Deck.Geometry.GridCell(3, 0)!.Value;
        RightClick(root, rig.Deck, cell);
        Assert.True(rig.Deck.Menu.IsOpen);
        Assert.Equal(new[] { "Show in pack", "-", "Select all", "Clear selection" }, rig.Deck.Menu.ItemsSource());
        Assert.Equal(new[] { Id(rig, 3) }, rig.Deck.Selection);
        Assert.False(rig.Deck.Menu.ItemsEnabledSource!()[0]);   // not in results

        rig.Deck.Menu.Selected!(2);
        Assert.Equal(rig.Deck.Shown.Count, rig.Deck.Selection.Count);
        rig.Deck.Menu.Selected!(3);
        Assert.Empty(rig.Deck.Selection);
    }

    [Fact]
    public void ShowInPack_LeavesTheSearch_OpensThePack_AndSelectsTheRow()
    {
        var (root, rig) = Rooted();
        rig.Deck.SetQuery(GearQuery.Empty.With(GearChip.OfMaterial(Steel)));
        rig.Deck.QueryInput.SetText("steel");
        int at = -1;
        for (int i = 0; i < rig.Deck.Shown.Count; i++) if (rig.Deck.Shown[i].Id == BagSteel) at = i;
        Assert.True(at >= 0);
        DeckRect cell = rig.Deck.Geometry.GridCell(at, rig.Deck.ScrollRow)!.Value;
        RightClick(root, rig.Deck, cell);
        Assert.True(rig.Deck.Menu.ItemsEnabledSource!()[0]);

        rig.Deck.Menu.Selected!(0);
        Assert.True(rig.Deck.Query.IsEmpty);
        Assert.Equal("", rig.Deck.QueryInput.Text);
        Assert.Equal(Bag, rig.Deck.SelectedPackId);
        Assert.Contains($"open {Bag:X}", rig.Actions.Calls);
        Assert.Equal(new[] { BagSteel }, rig.Deck.Selection);
        int row = rig.Deck.Shown.ToList().FindIndex(r => r.Id == BagSteel) / GearDeckGeometry.Columns;
        Assert.InRange(row, rig.Deck.ScrollRow, rig.Deck.ScrollRow + rig.Deck.Geometry.VisibleGridRows - 1);
    }

    [Fact]
    public void TheFooter_CountsTheSelection()
    {
        Rig rig = Make();
        ClickItem(rig, 1);
        ClickItem(rig, 2, toggle: true);
        Assert.Equal("2 selected", rig.Deck.FooterLeftText);
        rig.Deck.SetQuery(GearQuery.Empty.With(GearChip.OfMaterial(Steel)));
        Assert.DoesNotContain("selected", rig.Deck.FooterLeftText);
        ClickItem(rig, 0);
        Assert.EndsWith(" · 1 selected", rig.Deck.FooterLeftText);
    }
}
