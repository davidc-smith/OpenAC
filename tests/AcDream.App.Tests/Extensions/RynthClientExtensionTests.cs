using AcDream.ClientExtensions;
using AcDream.TestSupport;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RynthClientExtensionTests : IDisposable
{
    private readonly RynthConfigSandbox _sandbox = new("rynth-ext-");

    public void Dispose() => _sandbox.Dispose();

    private sealed class Registration : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void GinvIsRegisteredAndContainerEventsAreWired()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var log = new List<string>();
        var verbs = new List<string>();
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = log.Add,
            RegisterChatCommand = (verb, _) =>
            {
                verbs.Add(verb);
                return new Registration();
            },
        };
        var extension = new RynthClientExtension();
        extension.Attach(context);

        Assert.Contains("ginv", verbs);
        Assert.Contains(log, line => line.Contains("[RynthInventory]", StringComparison.Ordinal));
        var containers = runtime.InventoryOwner.ExternalContainers;
        Assert.True(containers.RequestOpen(0x80000100));
        Assert.True(containers.ApplyViewContents(0x80000100));
        runtime.InventoryOwner.Objects.ReplaceContents(0x80000100, [0x80000101u]);
        Assert.True(containers.ApplyClose(0x80000100));

        extension.Dispose();
        Assert.True(containers.RequestOpen(0x80000200));
        Assert.True(containers.ApplyViewContents(0x80000200));
    }

    [Fact]
    public void TheNav3DLayerIsCreatedFromTheContextAndLetGoOnDispose()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var store = new AcDream.App.Plugins.PluginWorldTriangleStore();
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = _ => { },
            RegisterChatCommand = (_, _) => new Registration(),
            WorldTriangles = store,
        };
        var extension = new RynthClientExtension();
        extension.Attach(context);
        extension.Tick(0.016);
        Assert.Single(store.Layers);

        extension.Dispose();
        Assert.Empty(store.Layers);
    }

    [Fact]
    public void AfterDisposeContainerEventsReachNoPlugin()
    {
        ContainerRecorder.Calls.Clear();
        var runtime = GameRuntimeTestFactory.Create();
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = _ => { },
            RegisterChatCommand = (_, _) => new Registration(),
        };
        var extension = new RynthClientExtension { AdditionalPluginExports = [typeof(ContainerRecorder)] };
        extension.Attach(context);
        var containers = runtime.InventoryOwner.ExternalContainers;
        Assert.True(containers.RequestOpen(0x80000100));
        Assert.True(containers.ApplyViewContents(0x80000100));
        extension.Tick(1.0 / 60);
        Assert.Equal(["view 80000100"], ContainerRecorder.Calls);

        extension.Dispose();
        runtime.InventoryOwner.Objects.ReplaceContents(0x80000100, [0x80000101u]);
        Assert.True(containers.ApplyClose(0x80000100));
        Assert.True(containers.RequestOpen(0x80000200));
        Assert.True(containers.ApplyViewContents(0x80000200));

        Assert.Equal(["view 80000100"], ContainerRecorder.Calls);
    }

    [Fact]
    public void EventsQueuedBeforeDisposeAreNeverDelivered()
    {
        ContainerRecorder.Calls.Clear();
        var runtime = GameRuntimeTestFactory.Create();
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = _ => { },
            RegisterChatCommand = (_, _) => new Registration(),
        };
        var extension = new RynthClientExtension { AdditionalPluginExports = [typeof(ContainerRecorder)] };
        extension.Attach(context);
        var containers = runtime.InventoryOwner.ExternalContainers;
        Assert.True(containers.RequestOpen(0x80000100));
        Assert.True(containers.ApplyViewContents(0x80000100));   // queued, not yet delivered

        extension.Dispose();
        extension.Tick(1.0 / 60);

        Assert.Empty(ContainerRecorder.Calls);
    }

    // RynthInventory (loaded before any additional plugin) eats every /ginv line, so a
    // recorder after it never sees the text; the proof of forwarding is the plugin's own
    // answer to "/ginv help", which only the argument can produce.
    [Fact]
    public void GinvForwardsItsArgumentsToThePluginsChatBarHandler()
    {
        var runtime = GameRuntimeTestFactory.Create();
        Action<string>? ginv = null;
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = _ => { },
            RegisterChatCommand = (verb, handler) =>
            {
                if (verb == "ginv")
                    ginv = handler;
                return new Registration();
            },
        };
        using var extension = new RynthClientExtension();
        extension.Attach(context);
        Assert.NotNull(ginv);

        ginv!("help");
        extension.Tick(1.0 / 60);

        Assert.Contains(
            runtime.CommunicationOwner.Chat.Snapshot(),
            entry => entry.Text.Contains("/ginv scan (rescan and save now)", StringComparison.Ordinal));
    }

    [Fact]
    public void RcIsRoutedToRynthCoresChatCommandsAndRepliesInChat()
    {
        var runtime = GameRuntimeTestFactory.Create();
        Action<string>? rc = null;
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = _ => { },
            RegisterChatCommand = (verb, handler) =>
            {
                if (verb == "rc")
                    rc = handler;
                return new Registration();
            },
        };
        using var extension = new RynthClientExtension();
        extension.Attach(context);
        Assert.NotNull(rc);

        rc!("bogus");

        Assert.Contains(
            runtime.CommunicationOwner.Chat.Snapshot(),
            entry => entry.Text.Contains("/rc bogus isn't in OpenAC (only /rc miniremote and /rc inventory).", StringComparison.Ordinal));
    }

    [Fact]
    public void OpeningASecondContainerClosesTheFirstInThePluginsInOrder()
    {
        ContainerRecorder.Calls.Clear();
        var runtime = GameRuntimeTestFactory.Create();
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = _ => { },
            RegisterChatCommand = (_, _) => new Registration(),
        };
        using var extension = new RynthClientExtension { AdditionalPluginExports = [typeof(ContainerRecorder)] };
        extension.Attach(context);

        // A tick after each step, so each change is its own delivery (one delivery would
        // group them by type, as RynthCore does).
        var containers = runtime.InventoryOwner.ExternalContainers;
        Assert.True(containers.RequestOpen(0x80000100));
        extension.Tick(1.0 / 60);
        Assert.True(containers.ApplyViewContents(0x80000100));
        extension.Tick(1.0 / 60);
        Assert.True(containers.RequestOpen(0x80000200));
        extension.Tick(1.0 / 60);
        Assert.True(containers.ApplyViewContents(0x80000200));
        extension.Tick(1.0 / 60);
        Assert.True(containers.ApplyClose(0x80000200));
        extension.Tick(1.0 / 60);

        Assert.Equal(
            ["view 80000100", "stop 80000100", "view 80000200", "stop 80000200"],
            ContainerRecorder.Calls);
    }

    private static unsafe class ContainerRecorder
    {
        public static readonly List<string> Calls = new();

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnViewObjectContents", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void View(uint id) => Calls.Add($"view {id:X8}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnStopViewingObjectContents", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Stop(uint id) => Calls.Add($"stop {id:X8}");
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
        runtime.CommunicationOwner.ReportLocalPlayerDeath("You died.");

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
            RegisterChatCommand = (verb, handler) =>
            {
                if (verb == "rynth")
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
