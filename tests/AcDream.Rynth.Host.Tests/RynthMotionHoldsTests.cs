using AcDream.Plugin.Abstractions;
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthMotionHoldsTests
{
    private const uint WalkForward = 0x45000005, WalkBackward = 0x45000006, RunForward = 0x41000003,
        TurnRight = 0x6500000D, TurnLeft = 0x6500000E, SidestepRight = 0x6500000F, SidestepLeft = 0x65000010;

    private readonly FakeGameView _view = new();

    [Fact]
    public void EachMotionHoldsItsKey()
    {
        var holds = new RynthMotionHolds(_view);

        Assert.True(holds.SetMotion(RunForward, true));
        Assert.Equal(new PluginMovementIntent(Forward: true, Run: true), _view.Intents[^1]);
        Assert.True(holds.SetMotion(RunForward, false));
        Assert.True(holds.SetMotion(WalkForward, true));
        Assert.Equal(new PluginMovementIntent(Forward: true, Run: false), _view.Intents[^1]);
        holds.SetMotion(WalkForward, false);

        holds.SetMotion(WalkBackward, true);
        holds.SetMotion(TurnLeft, true);
        holds.SetMotion(TurnRight, true);
        holds.SetMotion(SidestepLeft, true);
        holds.SetMotion(SidestepRight, true);
        Assert.Equal(
            new PluginMovementIntent(Backward: true, TurnLeft: true, TurnRight: true, StrafeLeft: true, StrafeRight: true, Run: false),
            _view.Intents[^1]);
    }

    [Fact]
    public void ReleasingTheLastKeyClearsAndReleasingAnUnheldKeySendsNothing()
    {
        var holds = new RynthMotionHolds(_view);

        Assert.True(holds.SetMotion(TurnLeft, false));
        Assert.Empty(_view.Intents);

        holds.SetMotion(TurnLeft, true);
        holds.SetMotion(TurnLeft, false);
        Assert.Null(_view.Intents[^1]);
    }

    [Fact]
    public void AnUnknownMotionIsRefused()
    {
        var holds = new RynthMotionHolds(_view);

        Assert.False(holds.SetMotion(0x41000012, true));
        Assert.Empty(_view.Intents);
    }

    [Fact]
    public void WithAutorunOnAHeldTurnStillRuns()
    {
        _view.AutoRun = true;
        var holds = new RynthMotionHolds(_view);

        holds.SetMotion(TurnRight, true);

        Assert.Equal(new PluginMovementIntent(Forward: true, TurnRight: true, Run: true), _view.Intents[^1]);
    }

    [Fact]
    public void SyncFollowsAutorunAndSendsNothingWhenUnchanged()
    {
        var holds = new RynthMotionHolds(_view);
        holds.SetMotion(TurnRight, true);
        int sent = _view.Intents.Count;

        holds.Sync();
        Assert.Equal(sent, _view.Intents.Count);

        _view.AutoRun = true;
        holds.Sync();
        Assert.Equal(new PluginMovementIntent(Forward: true, TurnRight: true, Run: true), _view.Intents[^1]);
    }

    [Fact]
    public void CommenceHoldsJumpAndDoJumpReleasesIt()
    {
        var holds = new RynthMotionHolds(_view);

        Assert.True(holds.CommenceJump());
        Assert.Equal(new PluginMovementIntent(Run: false, Jump: true), _view.Intents[^1]);
        Assert.True(holds.DoJump());
        Assert.Null(_view.Intents[^1]);
    }

    [Fact]
    public void LaunchWithMotionReleasesWithItsKeysForOneSync()
    {
        var holds = new RynthMotionHolds(_view);
        holds.SetMotion(TurnLeft, true);
        holds.CommenceJump();

        Assert.True(holds.LaunchJumpWithMotion(shift: false, forward: true, backward: false, strafeLeft: false, strafeRight: true));
        Assert.Equal(new PluginMovementIntent(Forward: true, StrafeRight: true, TurnLeft: true, Run: true), _view.Intents[^1]);

        holds.Sync();
        Assert.Equal(new PluginMovementIntent(TurnLeft: true, Run: false), _view.Intents[^1]);
    }

    [Fact]
    public void LaunchWithShiftWalks()
    {
        var holds = new RynthMotionHolds(_view);
        holds.CommenceJump();

        holds.LaunchJumpWithMotion(shift: true, forward: true, backward: false, strafeLeft: false, strafeRight: false);

        Assert.Equal(new PluginMovementIntent(Forward: true, Run: false), _view.Intents[^1]);
    }

    [Fact]
    public void StopCompletelyLetsGoOfEverything()
    {
        var holds = new RynthMotionHolds(_view);
        holds.SetMotion(RunForward, true);
        holds.CommenceJump();

        Assert.True(holds.StopCompletely());

        Assert.Null(_view.Intents[^1]);
        Assert.Contains("stop", _view.Actions);
        holds.Sync();
        Assert.Null(_view.Intents[^1]);
    }

    [Fact]
    public void ReleaseSendsOneClearAndNothingWhenNothingWasSent()
    {
        var holds = new RynthMotionHolds(_view);
        holds.Release();
        Assert.Empty(_view.Intents);

        holds.SetMotion(RunForward, true);
        holds.Release();

        Assert.Null(_view.Intents[^1]);
        Assert.Equal(2, _view.Intents.Count);
        Assert.DoesNotContain("stop", _view.Actions);
    }

    [Fact]
    public void SyncResendsHeldKeysTheClientDropped()
    {
        _view.AutoRun = true;
        var holds = new RynthMotionHolds(_view);
        holds.SetMotion(TurnRight, true);
        var held = new PluginMovementIntent(Forward: true, TurnRight: true, Run: true);
        Assert.Equal(held, _view.Intents[^1]);
        int sent = _view.Intents.Count;

        _view.HeldIntent = null; // a portal clears the client's command input
        holds.Sync();

        Assert.Equal(sent + 1, _view.Intents.Count);
        Assert.Equal(held, _view.Intents[^1]);
        Assert.Equal(held, _view.HeldIntent);
    }

    [Fact]
    public void SyncResendsHeldKeysAnotherPartyReplaced()
    {
        var holds = new RynthMotionHolds(_view);
        holds.SetMotion(TurnLeft, true);
        int sent = _view.Intents.Count;

        _view.HeldIntent = new PluginMovementIntent(Backward: true);
        holds.Sync();

        Assert.Equal(sent + 1, _view.Intents.Count);
        Assert.Equal(new PluginMovementIntent(TurnLeft: true, Run: false), _view.Intents[^1]);
    }

    [Fact]
    public void ARepeatHoldResendsWhatTheClientDropped()
    {
        var holds = new RynthMotionHolds(_view);
        holds.SetMotion(RunForward, true);
        int sent = _view.Intents.Count;

        _view.HeldIntent = null;
        Assert.True(holds.SetMotion(RunForward, true));

        Assert.Equal(sent + 1, _view.Intents.Count);
        Assert.Equal(new PluginMovementIntent(Forward: true, Run: true), _view.Intents[^1]);
    }

    [Fact]
    public void ARepeatHoldSendsNothingWhileTheClientStillHoldsIt()
    {
        var holds = new RynthMotionHolds(_view);
        holds.SetMotion(RunForward, true);
        int sent = _view.Intents.Count;

        Assert.True(holds.SetMotion(RunForward, true));

        Assert.Equal(sent, _view.Intents.Count);
    }

    [Fact]
    public void WithNothingHeldAnotherPartysIntentIsLeftAlone()
    {
        var holds = new RynthMotionHolds(_view);
        var theirs = new PluginMovementIntent(Forward: true);
        _view.HeldIntent = theirs;

        holds.Sync();
        Assert.True(holds.SetMotion(TurnLeft, false));
        holds.Refresh();

        Assert.Empty(_view.Intents);
        Assert.Equal(theirs, _view.HeldIntent);
    }

    [Fact]
    public void AfterLettingGoAnotherPartysLaterIntentIsLeftAlone()
    {
        var holds = new RynthMotionHolds(_view);
        holds.SetMotion(TurnLeft, true);
        holds.SetMotion(TurnLeft, false);
        int sent = _view.Intents.Count;

        _view.HeldIntent = new PluginMovementIntent(Forward: true);
        holds.Sync();

        Assert.Equal(sent, _view.Intents.Count);
    }

    [Fact]
    public void StopCompletelyAfterTheClientDroppedTheKeysStillStops()
    {
        var holds = new RynthMotionHolds(_view);
        holds.SetMotion(RunForward, true);
        _view.HeldIntent = null;
        int sent = _view.Intents.Count;

        Assert.True(holds.StopCompletely());

        Assert.Equal(sent, _view.Intents.Count);
        Assert.Contains("stop", _view.Actions);
    }
}
