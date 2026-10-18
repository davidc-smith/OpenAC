using AcDream.Core.Items;
using AcDream.Core.Net.Messages;
using AcDream.Runtime.Gameplay;

namespace AcDream.Runtime.Tests.Gameplay;

public sealed class RuntimeTradeStateTests
{
    private const uint Self = 0x50000001u;
    private const uint Partner = 0x50000002u;
    private const uint ItemA = 0x60000001u;
    private const uint ItemB = 0x60000002u;

    [Fact]
    public void RegisterOpensAndDerivesPartnerDespiteAceGuidLandmine()
    {
        using var trade = new RuntimeTradeState();

        trade.ApplyRegister(new GameEvents.RegisterTrade(Partner, Partner, 0uL), Self);

        RuntimeTradeSnapshot snapshot = trade.View.Snapshot;
        Assert.True(snapshot.IsOpen);
        Assert.Equal(Partner, snapshot.PartnerGuid);
        Assert.False(snapshot.SelfAccepted);
        Assert.False(snapshot.PartnerAccepted);
    }

    [Fact]
    public void AddStagesPerSideAndDropsBothAcceptances()
    {
        using var trade = new RuntimeTradeState();
        trade.ApplyRegister(new GameEvents.RegisterTrade(Partner, Partner, 0uL), Self);
        trade.ApplyAccept(Self, Self);
        trade.ApplyAccept(Partner, Self);

        trade.ApplyAdd(new GameEvents.AddToTrade(ItemA, (uint)RuntimeTradeSide.Self, 0u));
        trade.ApplyAdd(new GameEvents.AddToTrade(ItemB, (uint)RuntimeTradeSide.Partner, 0u));
        // Duplicate echo is a no-op stage-wise.
        trade.ApplyAdd(new GameEvents.AddToTrade(ItemA, (uint)RuntimeTradeSide.Self, 0u));

        RuntimeTradeSnapshot snapshot = trade.View.Snapshot;
        Assert.Equal(1, snapshot.SelfItemCount);
        Assert.Equal(1, snapshot.PartnerItemCount);
        Assert.Equal([ItemA], trade.View.GetItems(RuntimeTradeSide.Self));
        Assert.Equal([ItemB], trade.View.GetItems(RuntimeTradeSide.Partner));
        Assert.False(snapshot.SelfAccepted);
        Assert.False(snapshot.PartnerAccepted);
    }

    [Fact]
    public void AcceptDeclineTrackSelfVersusPartnerByGuid()
    {
        using var trade = new RuntimeTradeState();
        trade.ApplyRegister(new GameEvents.RegisterTrade(Partner, Partner, 0uL), Self);

        trade.ApplyAccept(Partner, Self);
        Assert.True(trade.View.Snapshot.PartnerAccepted);
        Assert.False(trade.View.Snapshot.SelfAccepted);

        trade.ApplyAccept(Self, Self);
        Assert.True(trade.View.Snapshot.SelfAccepted);

        trade.ApplyDecline(Partner, Self);
        Assert.False(trade.View.Snapshot.PartnerAccepted);
        Assert.True(trade.View.Snapshot.SelfAccepted);
    }

    [Fact]
    public void ResetClearsBothSidesButKeepsTheWindowOpen()
    {
        using var trade = new RuntimeTradeState();
        trade.ApplyRegister(new GameEvents.RegisterTrade(Partner, Partner, 0uL), Self);
        trade.ApplyAdd(new GameEvents.AddToTrade(ItemA, (uint)RuntimeTradeSide.Self, 0u));
        trade.ApplyAdd(new GameEvents.AddToTrade(ItemB, (uint)RuntimeTradeSide.Partner, 0u));
        trade.ApplyAccept(Self, Self);

        trade.ApplyReset();

        RuntimeTradeSnapshot snapshot = trade.View.Snapshot;
        Assert.True(snapshot.IsOpen);
        Assert.Equal(0, snapshot.SelfItemCount);
        Assert.Equal(0, snapshot.PartnerItemCount);
        Assert.False(snapshot.SelfAccepted);
    }

    [Fact]
    public void FailureRemovesTheItemAndRecordsTheReason()
    {
        using var trade = new RuntimeTradeState();
        trade.ApplyRegister(new GameEvents.RegisterTrade(Partner, Partner, 0uL), Self);
        trade.ApplyAdd(new GameEvents.AddToTrade(ItemA, (uint)RuntimeTradeSide.Self, 0u));

        trade.ApplyFailure(new GameEvents.TradeFailure(ItemA, 0x426u));

        RuntimeTradeSnapshot snapshot = trade.View.Snapshot;
        Assert.Equal(0, snapshot.SelfItemCount);
        Assert.Equal(ItemA, snapshot.LastFailureItemGuid);
        Assert.Equal(0x426u, snapshot.LastFailureReason);
    }

    [Fact]
    public void CloseAndGenerationClearConvergeTheLedger()
    {
        var trade = new RuntimeTradeState();
        trade.ApplyRegister(new GameEvents.RegisterTrade(Partner, Partner, 0uL), Self);
        trade.ApplyAdd(new GameEvents.AddToTrade(ItemA, (uint)RuntimeTradeSide.Self, 0u));

        trade.ApplyClose();
        Assert.False(trade.View.Snapshot.IsOpen);
        Assert.Equal(0, trade.View.Snapshot.SelfItemCount);

        trade.ApplyRegister(new GameEvents.RegisterTrade(Partner, Partner, 0uL), Self);
        trade.Clear();
        Assert.False(trade.View.Snapshot.IsOpen);

        trade.Dispose();
        Assert.True(trade.CaptureOwnership().IsConverged);
    }

    [Fact]
    public void EventsBeforeRegisterAreIgnored()
    {
        using var trade = new RuntimeTradeState();

        trade.ApplyAdd(new GameEvents.AddToTrade(ItemA, (uint)RuntimeTradeSide.Self, 0u));
        trade.ApplyAccept(Partner, Self);
        trade.ApplyReset();

        RuntimeTradeSnapshot snapshot = trade.View.Snapshot;
        Assert.False(snapshot.IsOpen);
        Assert.Equal(0, snapshot.SelfItemCount);
        Assert.False(snapshot.PartnerAccepted);
    }

    [Fact]
    public void ApplyAdd_marksTheObjectAndPublishesTheChangeOnce()
    {
        var objects = new ClientObjectTable();
        objects.AddOrUpdate(new ClientObject { ObjectId = ItemA });
        using var trade = new RuntimeTradeState(objects);
        trade.ApplyRegister(new GameEvents.RegisterTrade(Partner, Partner, 0uL), Self);
        int updates = 0;
        objects.ObjectUpdated += _ => updates++;

        trade.ApplyAdd(new GameEvents.AddToTrade(ItemA, (uint)RuntimeTradeSide.Self, 0u));
        Assert.Equal(1, objects.Get(ItemA)!.TradeState);
        Assert.Equal(1, updates);

        trade.ApplyAdd(new GameEvents.AddToTrade(ItemA, (uint)RuntimeTradeSide.Self, 0u));
        Assert.Equal(1, updates);
    }

    [Fact]
    public void CountersFollowEveryTradeEventLikeRynthCore()
    {
        using var trade = new RuntimeTradeState();

        trade.ApplyRegister(new GameEvents.RegisterTrade(Partner, Partner, 0uL), Self);
        trade.ApplyAdd(new GameEvents.AddToTrade(ItemA, (uint)RuntimeTradeSide.Self, 0u));
        trade.ApplyAccept(Partner, Self);
        trade.ApplyAccept(Self, Self);
        trade.ApplyFailure(new GameEvents.TradeFailure(ItemA, 0x0426u));
        trade.ApplyDecline(Partner, Self);
        trade.ApplyReset(Partner);
        trade.ApplyClearAcceptance();
        trade.ApplyTradeComplete();
        trade.ApplyClose(0x51u);

        RuntimeTradeCounters c = trade.View.Counters;
        Assert.Equal(1u, c.Generation);
        Assert.Equal(10u, c.Sequence);
        Assert.Equal(Partner, c.InitiatorId);
        Assert.Equal(0x01FFu, c.LastEventType);
        Assert.Equal(1u, c.PartnerAcceptCount);
        Assert.Equal(Self, c.LastAcceptedBy);
        Assert.Equal(Partner, c.LastDeclinedBy);
        Assert.Equal(Partner, c.LastResetBy);
        Assert.Equal(1u, c.FailureCount);
        Assert.Equal(ItemA, c.LastFailureItemId);
        Assert.Equal(0x0426u, c.LastFailureReason);
        Assert.Equal(1u, c.CompletedCount);
        Assert.Equal(0x51u, c.LastCloseReason);
    }

    [Fact]
    public void CountersMoveEvenWithNoTradeOpen()
    {
        using var trade = new RuntimeTradeState();

        trade.ApplyAccept(Partner, Self);
        trade.ApplyReset(Partner);

        RuntimeTradeCounters c = trade.View.Counters;
        Assert.Equal(2u, c.Sequence);
        Assert.Equal(0x0205u, c.LastEventType);
        Assert.Equal(0u, c.Generation);
        Assert.False(trade.View.Snapshot.IsOpen);
    }

    [Fact]
    public void ARegisterAfterACloseStartsANewGeneration()
    {
        using var trade = new RuntimeTradeState();

        trade.ApplyRegister(new GameEvents.RegisterTrade(Self, Partner, 0uL), Self);
        trade.ApplyClose(1u);
        trade.ApplyRegister(new GameEvents.RegisterTrade(Partner, Partner, 0uL), Self);

        Assert.Equal(2u, trade.View.Counters.Generation);
        Assert.Equal(Partner, trade.View.Counters.InitiatorId);
        Assert.Equal(1u, trade.View.Counters.LastCloseReason);
    }
}
