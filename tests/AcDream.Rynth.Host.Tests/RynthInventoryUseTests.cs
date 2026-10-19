using System.Diagnostics;
using RynthCore.Engine.Compatibility;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthInventoryUseTests : IDisposable
{
    private const uint Player = 0x50000001u, Kit = 0x80000020u, Pack = 0x80000010u;
    private long _now = 1_000_000;
    private bool _busy;
    private bool _sendAnswer = true;
    private readonly List<uint> _sent = new();
    private (int UseDoneSeq, uint UseDoneError, int ErrorSeq, uint Error) _replies;
    private readonly Dictionary<uint, (uint Container, uint Wielder, int Stack)> _items = new();

    public RynthInventoryUseTests()
    {
        InventoryModel.ResetActions();
        InventoryModel.Clock = () => _now;
        InventoryModel.PlayerId = () => Player;
        InventoryModel.Busy = () => _busy;
        InventoryModel.UseSender = id => { _sent.Add(id); return _sendAnswer; };
        InventoryModel.Replies = () => _replies;
        InventoryModel.ReadItem = (uint id, out uint container, out uint wielder, out int stack) =>
        {
            bool found = _items.TryGetValue(id, out var f);
            (container, wielder, stack) = f;
            return found;
        };
        _items[Kit] = (Pack, 0, 5);
        _items[Pack] = (Player, 0, 1);
    }

    public void Dispose()
    {
        InventoryModel.ResetActions();
        InventoryModel.Clock = Stopwatch.GetTimestamp;
        InventoryModel.PlayerId = null;
        InventoryModel.Busy = null;
        InventoryModel.UseSender = null;
        InventoryModel.Replies = null;
        InventoryModel.ReadItem = null;
    }

    private static InventoryRequest Use(uint id = Kit, string name = "Healing Kit") =>
        new() { Kind = InventoryActionKind.Use, ItemId = id, ItemName = name };

    private void After(double seconds) => _now += (long)(seconds * Stopwatch.Frequency);

    private void Tick() => InventoryModel.MainThreadTick();

    [Fact]
    public void AUseIsSentOnTheNextTickAndUseDoneEndsIt()
    {
        Assert.True(InventoryModel.Request(Use(), out string why));
        Assert.Equal("", why);
        Assert.Empty(_sent);

        Tick();
        Assert.Equal([Kit], _sent);
        Assert.Equal("Using Healing Kit", InventoryModel.Pending!.Label);
        Assert.False(InventoryModel.Request(Use(), out why));
        Assert.Equal("Still waiting for the server: Using Healing Kit", why);

        _replies = (1, 0, 0, 0);
        Tick();
        Assert.Null(InventoryModel.Pending);
        Assert.Equal(("Used Healing Kit.", true), (InventoryModel.LastResult!.Text, InventoryModel.LastResult.Ok));
    }

    [Fact]
    public void ASecondRequestBeforeTheTickIsRefused()
    {
        Assert.True(InventoryModel.Request(Use(), out _));
        Assert.False(InventoryModel.Request(Use(), out string why));
        Assert.Equal("Another item action is about to be sent.", why);
    }

    [Fact]
    public void AnErrorUseDoneOrWeenieErrorIsAFailure()
    {
        InventoryModel.Request(Use(), out _);
        Tick();
        _replies = (1, 0x3E8, 0, 0);
        Tick();
        Assert.Equal(("Couldn't use Healing Kit (error 0x3E8).", false), (InventoryModel.LastResult!.Text, InventoryModel.LastResult.Ok));

        InventoryModel.Request(Use(), out _);
        Tick();
        _replies = (1, 0x3E8, 1, 0x1D);
        Tick();
        Assert.Equal(("Refused: Using Healing Kit (error 0x1D).", false), (InventoryModel.LastResult!.Text, InventoryModel.LastResult.Ok));
    }

    [Fact]
    public void TheItemGoneOrChangedMeansUsed()
    {
        InventoryModel.Request(Use(), out _);
        Tick();
        _items[Kit] = (Pack, 0, 4);
        Tick();
        Assert.Equal("Used Healing Kit.", InventoryModel.LastResult!.Text);

        InventoryModel.Request(Use(), out _);
        Tick();
        _items.Remove(Kit);
        Tick();
        Assert.Equal(("Used Healing Kit.", true), (InventoryModel.LastResult!.Text, InventoryModel.LastResult.Ok));
    }

    [Fact]
    public void NoAnswerForSixSecondsLetsThePlayerActAgain()
    {
        InventoryModel.Request(Use(), out _);
        Tick();
        After(5.9);
        Tick();
        Assert.NotNull(InventoryModel.Pending);
        After(0.2);
        Tick();
        Assert.Equal(("No answer seen for: Using Healing Kit. You can act again.", false),
            (InventoryModel.LastResult!.Text, InventoryModel.LastResult.Ok));
    }

    [Fact]
    public void ABusyGameHoldsTheUseThenSendsOrGivesUp()
    {
        _busy = true;
        InventoryModel.Request(Use(), out _);
        Tick();
        Assert.Empty(_sent);
        Assert.True(InventoryModel.Pending!.WaitingForGame);
        Assert.Equal("another item action", InventoryModel.Pending.BusyWith);
        Assert.False(InventoryModel.Request(Use(), out string why));
        Assert.Equal("Still waiting for the game: Using Healing Kit", why);

        _busy = false;
        Tick();
        Assert.Equal([Kit], _sent);

        InventoryModel.ResetActions();
        _busy = true;
        InventoryModel.Request(Use(), out _);
        Tick();
        After(10.1);
        Tick();
        Assert.Equal("Not sent: the game stayed busy with another item action. Try again.", InventoryModel.LastResult!.Text);
    }

    [Theory]
    [InlineData("gone")]
    [InlineData("notcarried")]
    [InlineData("noplayer")]
    [InlineData("sendfails")]
    [InlineData("otherkind")]
    public void ChecksBeforeSendingFinishWithTheirTexts(string scenario)
    {
        InventoryRequest r = Use();
        string expected = "";
        switch (scenario)
        {
            case "gone": _items.Remove(Kit); expected = "Healing Kit is gone."; break;
            case "notcarried": _items[Kit] = (0x80009999u, 0, 5); expected = "Healing Kit is no longer in your packs."; break;
            case "noplayer": InventoryModel.PlayerId = () => 0; expected = "Not in the world."; break;
            case "sendfails": _sendAnswer = false; expected = "Couldn't send: Using Healing Kit."; break;
            case "otherkind": r = new InventoryRequest { Kind = InventoryActionKind.Drop, ItemId = Kit, ItemName = "Healing Kit" }; expected = "Not available in OpenAC."; break;
        }
        InventoryModel.Request(r, out _);
        Tick();
        Assert.Equal((expected, false), (InventoryModel.LastResult!.Text, InventoryModel.LastResult.Ok));
        Assert.Null(InventoryModel.Pending);
    }

    [Fact]
    public void AWornItemCanBeUsed()
    {
        _items[Kit] = (0, Player, 1);
        InventoryModel.Request(Use(), out _);
        Tick();
        Assert.Equal([Kit], _sent);
    }
}
