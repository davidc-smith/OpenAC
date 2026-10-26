using System;
using System.Collections.Generic;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Runtime.Gameplay;
using Xunit;

namespace AcDream.App.Tests.UI.Layout;

// Right-clicking an item on either side of the trade window examines it,
// like right-clicking any other physical item cell.
public sealed class SecureTradeUiControllerExamineTests
{
    private const uint Partner = 0x50000002u;
    private const uint OwnItem = 0x60000001u;
    private const uint PartnerItem = 0x60000002u;

    private sealed class TestElement : UiElement { }

    private sealed class StagedTrade : IRuntimeTradeView
    {
        public readonly List<uint> Self = [];
        public readonly List<uint> Other = [];

        public RuntimeTradeSnapshot Snapshot => new(
            Revision: 1L,
            IsOpen: true,
            PartnerGuid: Partner,
            SelfAccepted: false,
            PartnerAccepted: false,
            SelfItemCount: Self.Count,
            PartnerItemCount: Other.Count,
            LastFailureItemGuid: 0u,
            LastFailureReason: 0u);

        public IReadOnlyList<uint> GetItems(RuntimeTradeSide side)
            => side == RuntimeTradeSide.Self ? Self : Other;

        public RuntimeTradeCounters Counters => default;
    }

    private sealed record Harness(
        UiItemList SelfList,
        UiItemList PartnerList,
        SecureTradeUiController Controller,
        List<uint> Examined);

    private static Harness Build()
    {
        var selfList = new UiItemList { Width = 320f, Height = 32f };
        var partnerList = new UiItemList { Width = 320f, Height = 32f };
        var root = new TestElement { Width = 320f, Height = 200f };
        root.AddChild(selfList);
        root.AddChild(partnerList);
        var layout = new ImportedLayout(root, new Dictionary<uint, UiElement>
        {
            [SecureTradeUiController.SelfListId] = selfList,
            [SecureTradeUiController.PartnerListId] = partnerList,
        });

        var objects = new ClientObjectTable();
        objects.AddOrUpdate(ItemTooltipCaptionNames.Plain(OwnItem));
        objects.AddOrUpdate(ItemTooltipCaptionNames.Plain(PartnerItem));

        var trade = new StagedTrade();
        trade.Self.Add(OwnItem);
        trade.Other.Add(PartnerItem);

        var examined = new List<uint>();
        SecureTradeUiController controller = SecureTradeUiController.Bind(
            layout,
            new SecureTradeUiController.Bindings(
                Trade: trade,
                Objects: objects,
                ResolveIcon: static (_, _, _, _, _) => 0u,
                OpenTrade: static _ => { },
                CloseTrade: static () => { },
                AddToTrade: static _ => { },
                AcceptTrade: static (_, _, _) => { },
                DeclineTrade: static () => { },
                ResetTrade: static () => { },
                SetWindowVisible: static _ => { },
                ResolveAppropriateName: ItemTooltipCaptionNames.Resolve,
                ExamineItem: examined.Add))!;
        controller.Tick();
        return new Harness(selfList, partnerList, controller, examined);
    }

    private static void RightClick(UiItemSlot cell)
        => cell.OnEvent(new UiEvent(0u, cell, UiEventType.RightClick));

    [Fact]
    public void RightClickingOwnTradeItem_ExaminesIt()
    {
        Harness h = Build();
        using SecureTradeUiController _ = h.Controller;

        RightClick(h.SelfList.GetItem(0)!);

        Assert.Equal([OwnItem], h.Examined);
    }

    [Fact]
    public void RightClickingPartnerTradeItem_ExaminesIt()
    {
        Harness h = Build();
        using SecureTradeUiController _ = h.Controller;

        RightClick(h.PartnerList.GetItem(0)!);

        Assert.Equal([PartnerItem], h.Examined);
    }
}
