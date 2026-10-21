using System.Collections.Frozen;
using System.Diagnostics;
using System.Numerics;
using AcDream.ClientExtensions;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Navigation;
using AcDream.Rynth.EngineUi;
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

[Collection("Rynth")]
public sealed class ThreadRoutedGameViewTests : IDisposable
{
    public void Dispose() => RynthFrameThread.Reset();

    [Fact]
    public void TheFrameThreadReadsLiveAndOthersReadTheSnapshot()
    {
        var live = new FakeGameView { PlayerId = 1 };
        var other = new FakeGameView { PlayerId = 2 };
        var routed = new ThreadRoutedGameView(live, () => other);

        Assert.Equal(1u, routed.PlayerId);           // not latched: live
        RynthFrameThread.Latch();
        Assert.Equal(1u, routed.PlayerId);           // frame thread: live
        uint seen = 0;
        var t = new Thread(() => seen = routed.PlayerId);
        t.Start();
        t.Join();
        Assert.Equal(2u, seen);                      // other thread: snapshot view
    }

    [Fact]
    public void ActionsAndInParametersRouteTheSameWay()
    {
        var live = new FakeGameView();
        var other = new FakeGameView();
        var routed = new ThreadRoutedGameView(live, () => other);
        RynthFrameThread.Latch();

        var intent = new PluginMovementIntent(Forward: true);
        Assert.True(routed.SetMovementIntent(in intent));
        Assert.True(routed.SelectObject(5));
        var t = new Thread(() => routed.SelectObject(6));
        t.Start();
        t.Join();

        Assert.Equal([intent], live.Intents);
        Assert.Equal(["select 00000005"], live.Actions);
        Assert.Equal(["select 00000006"], other.Actions);
    }

    [Fact]
    public void VendorOutcomesAreSubscribedOnTheLiveViewFromAnyThread()
    {
        var live = new FakeGameView();
        var other = new FakeGameView();
        var routed = new ThreadRoutedGameView(live, () => other);
        RynthFrameThread.Latch();
        var seen = new List<RynthVendorOutcome>();
        var t = new Thread(() => routed.VendorTransactionCompleted += seen.Add);
        t.Start();
        t.Join();

        live.CompleteVendorTransaction(true, "done");
        other.CompleteVendorTransaction(false, "not this one");
        Assert.Equal([new RynthVendorOutcome(true, "done")], seen);
    }
}

[Collection("Rynth")]
public sealed class SnapshotRynthGameViewActionTests
{
    private bool _blocked;
    private readonly List<string> _log = [];
    private readonly List<string> _vendorFailures = [];

    private (SnapshotRynthGameView View, RynthActionQueue Queue, FakeGameView Live) Make(RynthWorldSnapshot? snapshot = null)
    {
        var live = new FakeGameView { PlayerId = 0x50000001 };
        var queue = new RynthActionQueue(_log.Add, () => _blocked);
        return (new SnapshotRynthGameView(snapshot ?? new RynthWorldSnapshot { PlayerId = 0x50000001 }, queue, live,
            _vendorFailures.Add), queue, live);
    }

    // Each action, its kind, and what the fake view records when it runs.
    public static TheoryData<string, bool, string> Actions => new()
    {
        { "SelectObject", false, "select 00000005" },
        { "UseObject", false, "use 00000005" },
        { "RequestIdentify", false, "id 00000005" },
        { "QueryHealth", false, "health 00000005" },
        { "ChangeCombatMode", false, "mode 2" },
        { "MoveItem", false, "move 00000005 00000006 1 3" },
        { "MergeStack", false, "merge 00000005 00000006" },
        { "ResetBusyCount", true, "reset busy" },
        { "WieldItem", false, "wield 00000005 00000010" },
        { "CastSpell", false, "cast 00000005 7" },
        { "MeleeAttack", false, "melee 00000005 2 0.50" },
        { "MissileAttack", false, "missile 00000005 2 0.50" },
        { "CancelAttack", true, "cancel" },
        { "UseObjectOn", false, "useon 00000005 00000006" },
        { "SetAutoRun(true)", false, "autorun 1" },
        { "SetAutoRun(false)", true, "autorun 0" },
        { "SnapHeading", false, "heading 90" },
        { "StopCompletely", true, "stop" },
        { "BeginJump", false, "jump 0.5" },
        { "MoveItemExternal", false, "moveext 00000005 00000006 2" },
        { "GiveItem", false, "give 00000005 00000006 2" },
        { "CloseContainer", true, "close 00000005" },
        { "Salvage", false, "salvage 00000005 00000006,00000007" },
        { "TradeOpen", false, "trade open 00000005" },
        { "TradeAdd", false, "trade add 00000005" },
        { "TradeAccept", false, "trade accept" },
        { "TradeDecline", true, "trade decline" },
        { "TradeReset", false, "trade reset" },
        { "TradeClose", true, "trade close" },
        { "VendorBuy", false, "buy 00000005x2" },
        { "VendorSell", false, "sell 00000005" },
    };

    private static bool Invoke(IRynthGameView v, string action)
    {
        string? notice;
        switch (action)
        {
            case "SelectObject": return v.SelectObject(5);
            case "UseObject": return v.UseObject(5);
            case "RequestIdentify": return v.RequestIdentify(5);
            case "QueryHealth": return v.QueryHealth(5);
            case "ChangeCombatMode": return v.ChangeCombatMode(2);
            case "MoveItem": return v.MoveItem(5, 6, 1, 3);
            case "MergeStack": return v.MergeStack(5, 6);
            case "ResetBusyCount": v.ResetBusyCount(); return true;
            case "WieldItem": return v.WieldItem(5, 0x10);
            case "CastSpell": return v.CastSpell(5, 7);
            case "MeleeAttack": return v.MeleeAttack(5, 2, 0.5f);
            case "MissileAttack": return v.MissileAttack(5, 2, 0.5f);
            case "CancelAttack": return v.CancelAttack();
            case "UseObjectOn": return v.UseObjectOn(5, 6);
            case "SetAutoRun(true)": return v.SetAutoRun(true);
            case "SetAutoRun(false)": return v.SetAutoRun(false);
            case "SnapHeading": return v.SnapHeading(90f);
            case "StopCompletely": return v.StopCompletely();
            case "BeginJump": return v.BeginJump(0.5f);
            case "MoveItemExternal": return v.MoveItemExternal(5, 6, 2);
            case "GiveItem": return v.GiveItem(5, 6, 2);
            case "CloseContainer": return v.CloseContainer(5);
            case "Salvage": return v.Salvage(5, [6, 7]);
            case "TradeOpen": return v.TradeOpen(5);
            case "TradeAdd": return v.TradeAdd(5);
            case "TradeAccept": return v.TradeAccept();
            case "TradeDecline": return v.TradeDecline();
            case "TradeReset": return v.TradeReset();
            case "TradeClose": return v.TradeClose();
            case "VendorBuy":
                bool bought = v.VendorBuy([(5u, 2)], out notice);
                Assert.Null(notice);
                return bought;
            case "VendorSell":
                bool sold = v.VendorSell([5u], out notice);
                Assert.Null(notice);
                return sold;
            default: throw new ArgumentException(action);
        }
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void EveryActionIsQueuedAndRunsOnTheLiveViewAtDrain(string action, bool stop, string recorded)
    {
        _ = stop;
        var (view, queue, live) = Make();
        Assert.True(Invoke(view, action));
        Assert.Empty(live.Actions);
        Assert.Equal(1, queue.Drain());
        Assert.Equal([recorded], live.Actions);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void StartsAreRefusedWhileBlockedAndStopsAreNot(string action, bool stop, string recorded)
    {
        _ = recorded;
        _blocked = true;
        var (view, queue, live) = Make();
        Assert.Equal(stop, Invoke(view, action));
        queue.Drain();
        Assert.Equal(stop ? 1 : 0, live.Actions.Count);
    }

    [Fact]
    public void MovementIntentAndChatAreQueued()
    {
        _blocked = true;
        var (view, queue, live) = Make();
        var intent = new PluginMovementIntent(Forward: true);
        Assert.False(view.SetMovementIntent(in intent));   // a start
        Assert.True(view.ClearMovementIntent());           // a stop
        view.WriteChatLine("hello", 1);                     // never refused
        _blocked = false;
        Assert.True(view.SetMovementIntent(in intent));
        Assert.True(view.SubmitChatCommand("/say hi"));
        Assert.Empty(live.Intents);
        Assert.Empty(live.ChatLines);
        queue.Drain();
        Assert.Equal([null, intent], live.Intents);
        Assert.Equal([("hello", 1)], live.ChatLines);
        Assert.Equal(["/say hi"], live.SubmittedCommands);
    }

    [Fact]
    public void AChatBurstLeavesRoomForAStop()
    {
        var (view, queue, live) = Make();
        for (int i = 0; i < 300; i++)
            view.WriteChatLine($"line {i}", 1);
        Assert.True(view.StopCompletely());
        queue.Drain();
        Assert.Equal("stop", live.Actions[^1]);
    }

    [Fact]
    public void CastingIsRefusedWhileTheSnapshotSaysPortalling()
    {
        var (view, queue, live) = Make(new RynthWorldSnapshot { PlayerId = 0x50000001, IsPortaling = true });
        Assert.False(view.CastSpell(5, 7));
        queue.Drain();
        Assert.Empty(live.Actions);
    }

    [Fact]
    public void AVendorFailureAtDrainIsReportedWithTheLiveNotice()
    {
        var (view, queue, live) = Make();
        live.ActionAnswer = false;
        live.VendorNotice = "You cannot afford that.";
        Assert.True(view.VendorBuy([(5u, 1)], out string? notice));
        Assert.Null(notice);
        Assert.Empty(_vendorFailures);
        queue.Drain();
        live.VendorNotice = null;
        Assert.True(view.VendorSell([5u], out notice));
        Assert.Null(notice);
        queue.Drain();
        Assert.Equal(["You cannot afford that.", "the client would not send it"], _vendorFailures);
    }

    [Fact]
    public void ServerTimeRunsOnFromThePublishedPair()
    {
        long published = Stopwatch.GetTimestamp() - Stopwatch.Frequency;   // a second ago
        var snapshot = new RynthWorldSnapshot { ServerTime = 100, PublishedAt = published };
        var (view, _, _) = Make(snapshot);
        long before = Stopwatch.GetTimestamp();
        double now = view.ServerTime;
        long after = Stopwatch.GetTimestamp();
        Assert.InRange(now, snapshot.ServerTimeAt(before), snapshot.ServerTimeAt(after));
        Assert.True(now >= 101);
    }

    [Fact]
    public void WorldToScreenUsesTheCapturedProjectionAndThePlayersCell()
    {
        const uint player = 0x50000001, cell = 0xA9B4001Au;
        var pose = new RynthEntityPose(cell, new Vector3(1, 2, 3), Quaternion.Identity, 0, null, null);
        var noProjection = new RynthWorldSnapshot
        {
            PlayerId = player,
            Entities = new Dictionary<uint, RynthEntityPose> { [player] = pose }.ToFrozenDictionary(),
        };
        Assert.False(Make(noProjection).View.TryWorldToScreen(10, 0, 10, out _, out _));

        var projection = new ClientScreenProjection(Matrix4x4.CreateScale(1e-3f), new Vector2(800, 600), 0xA9, 0xB4);
        var (view, _, _) = Make(noProjection with { Projection = projection });
        Assert.True(projection.TryProject(RuntimeNavigationPositions.FromCellLocal(cell, new Vector3(10, 10, 0)), out Vector2 expected));
        Assert.True(view.TryWorldToScreen(10, 0, 10, out float x, out float y));
        Assert.Equal((expected.X, expected.Y), (x, y));

        // No player pose: false, as the live view.
        Assert.False(Make(new RynthWorldSnapshot { PlayerId = player, Projection = projection })
            .View.TryWorldToScreen(10, 0, 10, out _, out _));
    }
}
