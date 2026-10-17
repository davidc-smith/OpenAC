using AcDream.Core.Items;
using AcDream.Rynth.Host;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RuntimeRynthGameViewTests
{
    [Fact]
    public void ReadsThePlayerAndObjectsFromTheRuntime()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var item = new ClientObject { ObjectId = 0x80000010, Name = "Drudge Skulker" };
        item.Properties.Int64s[1] = 42;
        runtime.InventoryOwner.Objects.AddOrUpdate(item);
        var view = new RuntimeRynthGameView(runtime);

        Assert.Equal(0x50000001u, view.PlayerId);
        Assert.True(view.TryGetObjectName(0x80000010, out string name));
        Assert.Equal("Drudge Skulker", name);
        Assert.False(view.TryGetObjectName(0x80000011, out _));
        Assert.True(view.TryGetInt64Property(0x80000010, 1, out long value));
        Assert.Equal(42, value);
        Assert.False(view.TryGetInt64Property(0x80000010, 6, out _));
    }

    [Fact]
    public void WriteChatLine_AddsASystemLine()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var view = new RuntimeRynthGameView(runtime);

        view.WriteChatLine("Rynth tracker: no data yet", 0);

        Assert.Contains(
            runtime.CommunicationOwner.Chat.Snapshot(),
            entry => entry.Text == "Rynth tracker: no data yet");
    }
}
