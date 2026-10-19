using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthVendorTradesTests
{
    private const uint VendorId = 0x80000700, Sword = 0x80000701, Gems = 0x80000702, Mine = 0x80000101;
    private readonly List<string> _log = new();

    private static RynthVendor Shop() => new(
        VendorId, "Thorsten the Weaponsmith", 0x1u, 0, 10_000, true, 0.9f, 1.3f,
        0, "", 0, -1, 1_234,
        [
            new(Sword, 30, "Sword", 1, 0x06001234, Amount: 5, StackSize: 1, MaxStackSize: 0, Value: 100, UnitValue: 100,
                UnitPrice: 130, Burden: 200, NeedsContainerSlot: false),
            new(Gems, 31, "Gem", 0x800, 0x06005678, Amount: -1, StackSize: 1, MaxStackSize: 100, Value: 10, UnitValue: 10,
                UnitPrice: 13, Burden: 5, NeedsContainerSlot: false),
        ]);

    [Fact]
    public void InfoAndItemsReadTheOpenVendor()
    {
        var view = new FakeGameView { PlayerId = 0x50000001, OpenVendor = Shop() };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);
        table.Vendor.NoteOpened();

        Assert.True(host.HasVendorTrade);
        Assert.True(host.TryGetVendorInfo(out VendorInfo info));
        Assert.Equal(VendorId, info.VendorId);
        Assert.Equal("Thorsten the Weaponsmith", info.Name);
        Assert.Equal(1u, info.Generation);
        Assert.Equal(1.3f, info.SellRate);
        Assert.Equal(1_234, info.PlayerCoins);
        Assert.Equal(2, info.ItemCount);
        Assert.True(info.TradingAvailable);
        Assert.False(info.TradeInFlight);

        VendorItem[] items = host.GetVendorItems();
        Assert.Equal(2, items.Length);
        Assert.Equal((Sword, 5, 130, false, false), (items[0].ObjectId, items[0].Amount, items[0].UnitPrice, items[0].Unlimited, items[0].Stackable));
        Assert.Equal((Gems, -1, true, true), (items[1].ObjectId, items[1].Amount, items[1].Unlimited, items[1].Stackable));
        Assert.Equal("Gem", items[1].Name);

        view.OpenVendor = null;
        Assert.False(host.TryGetVendorInfo(out _));
        Assert.Empty(host.GetVendorItems());
    }

    [Fact]
    public void BuyFollowsRynthCoresChecksThenSends()
    {
        var view = new FakeGameView { PlayerId = 0x50000001, OpenVendor = Shop() };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.Equal(0u, host.VendorBuy(0x80000999, [new(Sword, 1)]));              // not the open vendor
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus refused));
        Assert.Equal(VendorTradeState.Refused, refused.State);
        Assert.Contains("not the open vendor", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0u, host.VendorBuy(0, [new(Sword, 6)]));                       // more than its stock
        Assert.Equal(0u, host.VendorBuy(0, [new(Sword, 1), new(Sword, 1)]));        // listed twice
        Assert.Equal(0u, host.VendorBuy(0, [new(0x80000999, 1)]));                  // not on the list
        Assert.Equal(0u, host.VendorBuy(0, [new(Gems, 0)]));                        // bad amount
        Assert.Empty(view.Actions);

        uint id = host.VendorBuy(0, [new(Sword, 2), new(Gems, 500)]);
        Assert.NotEqual(0u, id);
        Assert.Equal(new[] { "buy 80000701x2,80000702x500" }, view.Actions);
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus sent));
        Assert.Equal((id, VendorTradeState.Sent, true, 2), (sent.RequestId, sent.State, sent.IsBuy, sent.EntryCount));
        Assert.True(host.TryGetVendorInfo(out VendorInfo busy));
        Assert.True(busy.TradeInFlight);

        Assert.Equal(0u, host.VendorBuy(0, [new(Sword, 1)]));                       // one at a time
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus stillSent));
        Assert.Equal((id, VendorTradeState.Sent), (stillSent.RequestId, stillSent.State));
        view.CompleteVendorTransaction(success: true);
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus done));
        Assert.Equal((id, VendorTradeState.Done, VendorTradeResult.Accepted), (done.RequestId, done.State, done.Result));
        Assert.True(done.Succeeded);
    }

    [Fact]
    public void SellRefusesVendorItemsAndRepeatsAndReportsAFailedAnswer()
    {
        var view = new FakeGameView { PlayerId = 0x50000001, OpenVendor = Shop() };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.Equal(0u, host.VendorSell(0, [Sword]));
        Assert.Equal(0u, host.VendorSell(0, [VendorId]));
        Assert.Equal(0u, host.VendorSell(0, [Mine, Mine]));
        Assert.Equal(0u, host.VendorSell(0, [0u]));
        uint id = host.VendorSell(0, [Mine]);
        Assert.NotEqual(0u, id);
        view.CompleteVendorTransaction(success: false, notice: "Vendor transaction failed.");
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus status));
        Assert.Equal((VendorTradeState.Done, VendorTradeResult.AnsweredNoRefresh), (status.State, status.Result));
        Assert.Equal("Vendor transaction failed.", status.Message);
        Assert.Equal(new[] { "sell 80000101" }, view.Actions);
    }

    [Fact]
    public void AViewRefusalAndATimeoutAreReported()
    {
        var view = new FakeGameView { PlayerId = 0x50000001, OpenVendor = Shop(), ActionAnswer = false, VendorNotice = "busy" };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);
        long now = 1_000;
        table.Vendor.Clock = () => now;

        Assert.Equal(0u, host.VendorBuy(0, [new(Sword, 1)]));
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus refused));
        Assert.Equal((VendorTradeState.Refused, "busy"), (refused.State, refused.Message));

        view.ActionAnswer = true;
        uint id = host.VendorBuy(0, [new(Sword, 1)]);
        now += 9_999;
        table.Vendor.Tick();
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus waiting));
        Assert.Equal(VendorTradeState.Sent, waiting.State);
        now += 1;
        table.Vendor.Tick();
        Assert.True(host.TryGetVendorTradeStatus(out VendorTradeStatus timedOut));
        Assert.Equal((id, VendorTradeState.Done, VendorTradeResult.TimedOut), (timedOut.RequestId, timedOut.State, timedOut.Result));
    }

    [Fact]
    public void NoStatusBeforeTheFirstRequest()
    {
        using var table = new RynthApiTable(new FakeGameView(), _log.Add);
        var host = new RynthCoreHost(*table.Native);
        Assert.False(host.TryGetVendorTradeStatus(out _));
    }
}
