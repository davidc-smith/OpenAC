using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Net.Messages;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.Tests.UI.Layout;

public sealed class HousePurchaseUiControllerTests
{
    private const uint Player = 0x50000001u;
    private const uint Slumlord = 0x70000100u;
    private sealed class Element : UiElement { }

    private sealed class Harness : IDisposable
    {
        public ClientObjectTable Objects { get; } = new();
        public RuntimeHousePurchaseState State { get; }
        public UiButton BuyTab { get; } = new(new ElementInfo { Id = HousePurchaseUiController.BuyTabId, Type = 1 }, _ => (0u, 0, 0));
        public UiButton RentTab { get; } = new(new ElementInfo { Id = HousePurchaseUiController.MaintenanceTabId, Type = 1 }, _ => (0u, 0, 0));
        public UiElement BuyPage { get; } = new Element();
        public UiElement RentPage { get; } = new Element();
        public UiText BuyText { get; } = new();
        public UiText RentText { get; } = new();
        public UiText BuyOwner { get; } = new();
        public UiItemList BuyList { get; } = new() { Width = 555, Height = 32 };
        public UiItemList RentList { get; } = new() { Width = 555, Height = 32 };
        public UiButton BuyButton { get; } = new(new ElementInfo { Id = HousePurchaseUiController.BuyButtonId, Type = 1 }, _ => (0u, 0, 0));
        public UiButton RentButton { get; } = new(new ElementInfo { Id = HousePurchaseUiController.MaintenanceButtonId, Type = 1 }, _ => (0u, 0, 0));
        public UiButton Close { get; } = new(new ElementInfo { Id = HousePurchaseUiController.CloseButtonId, Type = 1 }, _ => (0u, 0, 0));
        public HousePurchaseUiController Controller { get; }
        public bool Visible;
        public List<string> Reports { get; } = [];
        public List<string> Sent { get; } = [];

        public Harness()
        {
            Objects.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature });
            State = new RuntimeHousePurchaseState(Objects, () => Player);
            var root = new Element();
            foreach (UiElement e in new UiElement[]
                     { BuyTab, RentTab, BuyPage, RentPage, BuyText, RentText, BuyOwner,
                       BuyList, RentList, BuyButton, RentButton, Close })
                root.AddChild(e);
            var layout = new ImportedLayout(root, new()
            {
                [HousePurchaseUiController.BuyTabId] = BuyTab,
                [HousePurchaseUiController.MaintenanceTabId] = RentTab,
                [HousePurchaseUiController.BuyPageId] = BuyPage,
                [HousePurchaseUiController.MaintenancePageId] = RentPage,
                [HousePurchaseUiController.BuyRequirementsTextId] = BuyText,
                [HousePurchaseUiController.MaintenanceRequirementsTextId] = RentText,
                [HousePurchaseUiController.BuyOwnerId] = BuyOwner,
                [HousePurchaseUiController.BuyItemsListId] = BuyList,
                [HousePurchaseUiController.MaintenanceItemsListId] = RentList,
                [HousePurchaseUiController.BuyButtonId] = BuyButton,
                [HousePurchaseUiController.MaintenanceButtonId] = RentButton,
                [HousePurchaseUiController.CloseButtonId] = Close,
            });
            Controller = HousePurchaseUiController.Bind(layout, new(
                Purchase: State.View,
                Objects: Objects,
                Stage: State.Stage,
                Unstage: State.Unstage,
                Revalidate: () => State.Revalidate(),
                Close: State.Close,
                BuyHouse: (s, items) => Sent.Add($"buy:{s:X8}:{string.Join(",", items)}"),
                RentHouse: (s, items) => Sent.Add($"rent:{s:X8}:{string.Join(",", items)}"),
                ResolveIcon: (_, icon, _, _, _) => icon,
                SetWindowVisible: v => Visible = v,
                Report: Reports.Add,
                ResolveAppropriateName: ItemTooltipCaptionNames.Resolve))!;
        }

        public void Item(uint id, uint wcid, int stack = 1)
        {
            Objects.AddOrUpdate(new ClientObject { ObjectId = id, WeenieClassId = wcid, StackSize = stack, IconId = 5 });
            Objects.MoveItem(id, Player, 0);
        }

        public void Open(uint owner = 0u, bool free = false)
        {
            State.ApplyProfile(new GameEvents.HouseProfile(
                Slumlord, 1u, owner, 1u, -1, -1, -1, -1, free, 1u, owner == 0u ? "" : "Sammy",
                [new(50_000, 0, 273u, "Pyreal", "Pyreals")],
                [new(5_000, 0, 273u, "Pyreal", "Pyreals")]));
            Controller.Tick();
        }

        public void Drop(UiItemList list, uint id)
        {
            var cell = new UiItemSlot();
            Controller.HandleDropRelease(list, cell, new ItemDragPayload(id, ItemDragSource.Inventory, 0, cell));
            Controller.Tick();
        }

        // The strip pads itself with empty slots, so count only filled cells.
        public static int Staged(UiItemList list) =>
            Enumerable.Range(0, list.GetNumUIItems()).Count(i => list.GetItem(i)?.ItemId != 0);

        public static string Text(UiText text) =>
            string.Join("\n", text.LinesProvider().Select(l => l.Text));

        public void Dispose() => Controller.Dispose();
    }

    [Fact]
    public void Hidden_UntilOpened_ThenShownOnBuyForAnUnownedHouse()
    {
        using var h = new Harness();
        Assert.False(h.Visible);

        h.Open();

        Assert.True(h.Visible);
        Assert.True(h.BuyPage.Visible);
        Assert.False(h.RentPage.Visible);
        Assert.Equal("For Sale", Harness.Text(h.BuyOwner));
        Assert.Contains("Pyreals: 0 / 50,000", Harness.Text(h.BuyText));
    }

    [Fact]
    public void AnOwnedHouse_OpensOnMaintenance_AndTabsSwitch()
    {
        using var h = new Harness();
        h.Open(owner: Player);

        Assert.True(h.RentPage.Visible);
        Assert.False(h.BuyPage.Visible);

        h.BuyTab.OnClick!();

        Assert.True(h.BuyPage.Visible);
        Assert.False(h.RentPage.Visible);
    }

    [Fact]
    public void Drop_StagesAndEnablesTheButton_WhichSendsTheStagedGuids()
    {
        using var h = new Harness();
        h.Item(1, 273u, stack: 20_000);
        h.Open();
        Assert.False(h.BuyButton.Enabled);

        h.Drop(h.BuyList, 1);

        Assert.Equal(1, Harness.Staged(h.BuyList));
        Assert.True(h.BuyButton.Enabled);
        Assert.Contains("(+20,000 staged)", Harness.Text(h.BuyText));

        h.BuyButton.OnClick!();

        Assert.Equal(["buy:70000100:1"], h.Sent);
    }

    [Fact]
    public void Drop_Refused_IsReportedOnce()
    {
        using var h = new Harness();
        h.Item(1, 1234u);
        h.Open();

        h.Drop(h.BuyList, 1);

        Assert.Equal([HousePurchaseText.Refusal(HouseStageResult.NotRequired)], h.Reports);
        Assert.Equal(0, Harness.Staged(h.BuyList));
    }

    [Fact]
    public void DragOffAndRightClick_Unstage()
    {
        using var h = new Harness();
        h.Item(1, 273u, stack: 10);
        h.Item(2, 273u, stack: 10);
        h.Open(owner: Player);
        h.Drop(h.RentList, 1);
        h.Drop(h.RentList, 2);

        var cell = new UiItemSlot();
        h.Controller.OnDragLift(h.RentList, cell, new ItemDragPayload(1, ItemDragSource.Inventory, 0, cell));
        h.RentList.ExamineItemRequested!(2);
        h.Controller.Tick();

        Assert.Empty(h.State.View.GetStaged(HousePurchaseTab.Rent));
        Assert.False(h.RentButton.Enabled);
    }

    [Fact]
    public void Buttons_DisabledWhenOwnedBuy_AndFreeMaintenance_EvenWithItemsStaged()
    {
        using var h = new Harness();
        h.Item(1, 273u, stack: 10);
        h.Open();
        h.Drop(h.BuyList, 1);
        Assert.True(h.BuyButton.Enabled);

        h.Open(owner: Player);
        Assert.False(h.BuyButton.Enabled);
        h.BuyButton.OnClick!();
        Assert.Empty(h.Sent);

        h.Drop(h.RentList, 1);
        Assert.True(h.RentButton.Enabled);
        h.Open(owner: Player, free: true);
        Assert.False(h.RentButton.Enabled);
        h.RentButton.OnClick!();
        Assert.Empty(h.Sent);
    }

    [Fact]
    public void AStagedStackChanging_RefreshesTheRequirementText()
    {
        using var h = new Harness();
        h.Item(1, 273u, stack: 10);
        h.Open();
        h.Drop(h.BuyList, 1);
        Assert.Contains("(+10 staged)", Harness.Text(h.BuyText));

        h.Objects.Get(1)!.StackSize = 25;
        h.Objects.NotifyObjectUpdated(1);
        h.Controller.Tick();

        Assert.Contains("(+25 staged)", Harness.Text(h.BuyText));
    }

    [Fact]
    public void CloseButton_ClosesAndHides()
    {
        using var h = new Harness();
        h.Open();

        h.Close.OnClick!();
        h.Controller.Tick();

        Assert.False(h.State.View.Snapshot.IsOpen);
        Assert.False(h.Visible);
    }

    [Fact]
    public void AnItemLeavingTheInventory_IsUnstaged()
    {
        using var h = new Harness();
        h.Item(1, 273u, stack: 10);
        h.Open();
        h.Drop(h.BuyList, 1);

        h.Objects.Remove(1);
        h.Controller.Tick();

        Assert.Equal(0, Harness.Staged(h.BuyList));
    }
}
