using AcDream.App.UI.Hud;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearMoveQueueTests
{
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
        public Func<GearMove, GearMoveResult> Result = _ => GearMoveResult.Sent;
        public GearMoveResult Send(GearMove move) { GearMoveResult r = Result(move); if (r == GearMoveResult.Sent) Sent.Add(move); return r; }
    }

    private sealed class Rig
    {
        public readonly Channel Channel = new();
        public readonly Sender Sender = new();
        public readonly List<string> Lines = [];
        public double Now;
        public readonly GearMoveQueue Queue;
        public Rig() => Queue = new GearMoveQueue(Channel, Sender, () => Now, Lines.Add);
    }

    private static readonly GearMove[] Three = [GearMove.Put(1, 9, 0), GearMove.Put(2, 9, 1), GearMove.Put(3, 9, 2)];

    [Fact]
    public void SendsOneAtATime_AndFinishesOnTheLastConfirm()
    {
        var rig = new Rig();
        Assert.True(rig.Queue.Start("Tidying", Three));
        Assert.Single(rig.Sender.Sent);
        rig.Queue.Tick();
        Assert.Single(rig.Sender.Sent);                // still waiting for item 1
        rig.Channel.Complete(77);                      // someone else's request
        rig.Queue.Tick();
        Assert.Single(rig.Sender.Sent);
        rig.Channel.Complete(1);
        rig.Channel.CanSend = false;
        rig.Queue.Tick();
        Assert.Single(rig.Sender.Sent);                // not free yet
        rig.Channel.CanSend = true;
        rig.Queue.Tick();
        Assert.Equal(2, rig.Sender.Sent.Count);
        rig.Channel.Complete(2); rig.Queue.Tick();
        rig.Channel.Complete(3); rig.Queue.Tick();
        Assert.False(rig.Queue.IsRunning);
        Assert.Equal(3, rig.Queue.Done);
        Assert.Empty(rig.Lines);
    }

    [Fact]
    public void AFailure_Stops_WithoutALineOfItsOwn()
    {
        var rig = new Rig();
        rig.Queue.Start("Tidying", Three);
        rig.Channel.Fail(1);
        rig.Queue.Tick();
        Assert.False(rig.Queue.IsRunning);
        Assert.Single(rig.Sender.Sent);
        Assert.Empty(rig.Lines);                       // the runtime already printed the server's message
    }

    [Fact]
    public void ATimeout_Stops_WithALine()
    {
        var rig = new Rig();
        rig.Queue.Start("Tidying", Three);
        rig.Now = GearMoveQueue.TimeoutSeconds - 0.01;
        rig.Queue.Tick();
        Assert.True(rig.Queue.IsRunning);
        rig.Now = GearMoveQueue.TimeoutSeconds;
        rig.Queue.Tick();
        Assert.False(rig.Queue.IsRunning);
        Assert.Equal(["Tidying stopped: no answer from the server."], rig.Lines);
    }

    [Fact]
    public void ARefusal_Stops_AndSaysWhere_AndSkipsCountAsDone()
    {
        var rig = new Rig();
        rig.Sender.Result = m => m.Item switch { 1 => GearMoveResult.Skipped, 2 => GearMoveResult.Refused, _ => GearMoveResult.Sent };
        rig.Queue.Start("Combining", Three);
        Assert.False(rig.Queue.IsRunning);
        Assert.Equal(["Combining stopped at 1 of 3."], rig.Lines);
    }

    [Fact]
    public void Cancel_IsSilent_AndStartRefusesWhileRunning()
    {
        var rig = new Rig();
        rig.Queue.Start("Tidying", Three);
        Assert.False(rig.Queue.Start("Combining", Three));
        Assert.Equal(["Tidying is still running."], rig.Lines);
        rig.Queue.Cancel();
        Assert.False(rig.Queue.IsRunning);
        Assert.False(rig.Queue.Start("Combining", []));
        Assert.True(rig.Queue.Start("Combining", Three));
    }
}
