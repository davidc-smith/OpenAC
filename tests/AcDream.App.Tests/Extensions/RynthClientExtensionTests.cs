using AcDream.ClientExtensions;
using AcDream.Rynth.Host;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RynthClientExtensionTests
{
    private sealed class Registration : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void AttachesTicksAndReleasesItsSlotOnDispose()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var log = new List<string>();
        var verbs = new List<string>();
        var registration = new Registration();
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = log.Add,
            RegisterChatCommand = (verb, _) =>
            {
                verbs.Add(verb);
                return registration;
            },
        };

        var extension = new RynthClientExtension();
        extension.Attach(context);

        Assert.Contains("rynth", verbs);
        Assert.Contains("[Rynth] [RynthTracker] Plugin initialized.", log);

        runtime.ActionOwner.Combat.OnUpdateHealth(0x80000010, 0.5f);

        extension.Dispose();
        Assert.True(registration.Disposed);

        var second = new RynthClientExtension();
        second.Attach(context);
        second.Dispose();
    }

    [Fact]
    public void RealRuntimeFeedsTheTrackerAndTheCommandPrintsItsLine()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000000u | (uint)Random.Shared.Next(1, 0xFFFF);
        var player = new AcDream.Core.Items.ClientObject { ObjectId = runtime.PlayerIdentity.ServerGuid };
        player.Properties.Int64s[1] = 1000;
        runtime.InventoryOwner.Objects.AddOrUpdate(player);
        Action<string>? command = null;
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = _ => { },
            RegisterChatCommand = (_, handler) =>
            {
                command = handler;
                return new Registration();
            },
        };
        using var extension = new RynthClientExtension();
        extension.Attach(context);

        // The test runtime cannot reach InWorld without a live session, so login is
        // never raised here (that join is covered by the live check). The tracker
        // therefore ignores the kill, which is still routed through the real events.
        for (int i = 0; i < 60; i++)
            extension.Tick(1.0 / 60);
        runtime.ActionOwner.Combat.OnKillerNotification("You killed the drudge!", 0u);
        player.Properties.Int64s[1] = 1600;
        for (int i = 0; i < 60; i++)
            extension.Tick(1.0 / 60);
        Assert.NotNull(command);
        command!("tracker");

        Assert.Contains(
            runtime.CommunicationOwner.Chat.Snapshot(),
            entry => entry.Text.StartsWith("Rynth tracker:", StringComparison.Ordinal));
    }
}
