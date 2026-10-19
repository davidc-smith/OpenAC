using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AcDream.Runtime;
using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

/// <summary>RynthCore queues plugin events and delivers them once per frame, by type.</summary>
public sealed class RynthEventQueueTests
{
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001 };

    [Fact]
    public void EventsWaitForTheDeliveryAndArriveInRynthCoresOrder()
    {
        QueueRecorder.Calls.Clear();
        using var host = new RynthHost(_view, _ => { }, [typeof(QueueRecorder)]);

        host.Bridge.OnStatusText("You're too busy!");
        host.Bridge.OnEnchantmentRemoved(2);
        host.Bridge.OnKillLanded("You killed the drudge!");
        host.Bridge.OnHealthChanged(0x80000010, 0.5f);
        host.Bridge.OnSelectedTargetChange(0x80000010, 0);
        host.Bridge.OnEntity(new RuntimeEntityDelta(
            default, RuntimeEntityChange.Registered,
            new RuntimeEntitySnapshot(new RuntimeEntityIdentity(0x80000020, 1, 1), 0, 0, null)));
        host.Bridge.OnBusyCountIncremented();
        Assert.Empty(QueueRecorder.Calls);

        host.Bridge.DeliverQueued();

        Assert.Equal(
            [
                "busy+", "create 80000020", "select 80000010 00000000", "health 80000010 0.50",
                "kill You killed the drudge!", "ench- 2", "chat 1A You're too busy!",
            ],
            QueueRecorder.Calls);
        host.Bridge.DeliverQueued();
        Assert.Equal(7, QueueRecorder.Calls.Count);
    }

    [Fact]
    public void LoginArrivesOnTheDeliveryAfterThatDeliverysQueuedEvents()
    {
        QueueRecorder.Calls.Clear();
        using var host = new RynthHost(_view, _ => { }, [typeof(QueueRecorder)]);

        host.Bridge.OnLifecycle(new RuntimeLifecycleDelta(
            default, RuntimeLifecycleState.Starting, RuntimeLifecycleState.InWorld));
        host.Bridge.OnEntity(new RuntimeEntityDelta(
            default, RuntimeEntityChange.Registered,
            new RuntimeEntitySnapshot(new RuntimeEntityIdentity(0x80000020, 1, 1), 0, 0, null)));

        Assert.True(RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete);
        Assert.Empty(QueueRecorder.Calls);

        host.Bridge.DeliverQueued();
        Assert.Equal(["create 80000020", "login"], QueueRecorder.Calls);
        host.Bridge.DeliverQueued();
        Assert.Equal(["create 80000020", "login"], QueueRecorder.Calls);
    }

    [Fact]
    public void ALogoutBeforeTheDeliveryMeansNoLoginIsDelivered()
    {
        QueueRecorder.Calls.Clear();
        using var host = new RynthHost(_view, _ => { }, [typeof(QueueRecorder)]);
        int loggedIn = 0;
        host.Bridge.LoggedIn += () => loggedIn++;

        host.Bridge.OnLifecycle(new RuntimeLifecycleDelta(
            default, RuntimeLifecycleState.Starting, RuntimeLifecycleState.InWorld));
        host.Bridge.OnLeavingWorld();
        host.Bridge.DeliverQueued();

        Assert.Equal(["logout"], QueueRecorder.Calls);
        Assert.Equal(0, loggedIn);
    }

    [Fact]
    public void LoggedOutFiresOncePerLeavingWorldAndAThrowingHandlerIsLogged()
    {
        QueueRecorder.Calls.Clear();
        var log = new List<string>();
        using var host = new RynthHost(_view, log.Add, [typeof(QueueRecorder)]);
        int closed = 0;
        host.Bridge.LoggedOut += () => closed++;
        host.Bridge.LoggedOut += () => throw new InvalidOperationException("boom");

        host.Bridge.OnLeavingWorld();

        Assert.Equal(1, closed);
        Assert.Equal(["logout"], QueueRecorder.Calls);
        Assert.Contains(log, line => line.Contains("LoggedOut handler failed"));
    }

    // A plugin export cannot throw across the UnmanagedCallersOnly boundary (the
    // process would fail fast), so a throwing delivery is queued directly.
    [Fact]
    public void AThrowingDeliveryIsLoggedAndTheRestOfTheDeliveryStillRuns()
    {
        QueueRecorder.Calls.Clear();
        var log = new List<string>();
        using var host = new RynthHost(_view, log.Add, [typeof(QueueRecorder)]);

        host.Bridge.Enqueue(RynthEventBridge.EventKind.BusyIncremented,
            () => throw new InvalidOperationException("boom"));
        host.Bridge.OnBusyCountIncremented();
        host.Bridge.OnKillLanded("You killed the drudge!");
        host.Bridge.OnLifecycle(new RuntimeLifecycleDelta(
            default, RuntimeLifecycleState.Starting, RuntimeLifecycleState.InWorld));
        host.Bridge.DeliverQueued();

        Assert.Equal(["busy+", "kill You killed the drudge!", "login"], QueueRecorder.Calls);
        Assert.Contains("[Rynth] BusyIncremented delivery threw: InvalidOperationException: boom", log);
    }

    [Fact]
    public void AThrowingLoginFollowUpIsLoggedAndLaterDeliveriesStillRun()
    {
        QueueRecorder.Calls.Clear();
        var log = new List<string>();
        using var host = new RynthHost(_view, log.Add, [typeof(QueueRecorder)]);
        host.Bridge.LoggedIn += () => throw new InvalidOperationException("push failed");

        host.Bridge.OnLifecycle(new RuntimeLifecycleDelta(
            default, RuntimeLifecycleState.Starting, RuntimeLifecycleState.InWorld));
        host.Bridge.DeliverQueued();
        host.Bridge.OnBusyCountIncremented();
        host.Bridge.DeliverQueued();

        Assert.Equal(["login", "busy+"], QueueRecorder.Calls);
        Assert.Contains("[Rynth] Login delivery threw: InvalidOperationException: push failed", log);
    }

    [Fact]
    public void TheTickDeliversEventsBeforeThePluginsOwnTick()
    {
        QueueRecorder.Calls.Clear();
        using var host = new RynthHost(_view, _ => { }, [typeof(QueueRecorder)]);

        host.Bridge.OnBusyCountIncremented();
        host.Bridge.OnBusyCountDecremented();
        host.Tick();

        Assert.Equal(["busy+", "busy-", "tick"], QueueRecorder.Calls);
    }

    [Fact]
    public void AnEventRaisedWhileDeliveringJoinsThisDeliveryWhenLaterAndTheNextOneOtherwise()
    {
        ReentrantRecorder.Calls.Clear();
        using var host = new RynthHost(_view, _ => { }, [typeof(ReentrantRecorder)]);
        ReentrantRecorder.Bridge = host.Bridge;
        try
        {
            host.Bridge.OnEntity(Created(0x80000020));   // its callback raises kill, busy+ and another create
            host.Bridge.DeliverQueued();

            Assert.Equal(["create 80000020", "kill raised while delivering"], ReentrantRecorder.Calls);

            host.Bridge.DeliverQueued();
            Assert.Equal(
                ["create 80000020", "kill raised while delivering", "busy+", "create 80000021"],
                ReentrantRecorder.Calls);
        }
        finally
        {
            ReentrantRecorder.Bridge = null;
        }
    }

    [Fact]
    public void QueuedEventsAreNeverDeliveredAfterTheHostIsDisposed()
    {
        QueueRecorder.Calls.Clear();
        var host = new RynthHost(_view, _ => { }, [typeof(QueueRecorder)]);
        RynthEventBridge bridge = host.Bridge;
        bridge.OnBusyCountIncremented();
        bridge.OnKillLanded("You killed the drudge!");
        bridge.OnLifecycle(new RuntimeLifecycleDelta(
            default, RuntimeLifecycleState.Starting, RuntimeLifecycleState.InWorld));

        host.Dispose();
        bridge.DeliverQueued();
        host.Tick();

        Assert.Empty(QueueRecorder.Calls);
    }

    [Fact]
    public void VendorEventsArriveInRynthCoresDrainOrder()
    {
        QueueRecorder.Calls.Clear();
        using var host = new RynthHost(_view, _ => { }, [typeof(QueueRecorder)]);

        host.Bridge.OnContainerContentsChanged(0x80000100);
        host.OnVendorChanged(RynthVendorChange.Opened, previousVendorId: 0x80000600, vendorId: 0x80000700);
        host.Bridge.OnContainerClosed(0x80000100);
        Assert.Empty(QueueRecorder.Calls);
        host.Tick();

        // RynthCore queues the replaced vendor's close before the new open, but drains the
        // open queue before the close queue (PluginManager.ProcessPendingActions), so a
        // switch inside one frame reaches plugins as the new open, then the old close.
        Assert.Equal(
            ["stop-viewing 80000100", "vendor-open 80000700", "vendor-close 80000600", "update-inventory 80000100", "tick"],
            QueueRecorder.Calls);
        Assert.Equal(1u, host.Vendor.Generation);
    }

    [Fact]
    public void RefreshOpensAgainAndResetCloses()
    {
        QueueRecorder.Calls.Clear();
        using var host = new RynthHost(_view, _ => { }, [typeof(QueueRecorder)]);

        host.OnVendorChanged(RynthVendorChange.Opened, previousVendorId: 0, vendorId: 0x80000700);
        host.Bridge.DeliverQueued();
        Assert.Equal(["vendor-open 80000700"], QueueRecorder.Calls);
        Assert.Equal(1u, host.Vendor.Generation);

        host.OnVendorChanged(RynthVendorChange.Refreshed, previousVendorId: 0x80000700, vendorId: 0x80000700);
        host.Bridge.DeliverQueued();
        Assert.Equal(["vendor-open 80000700", "vendor-open 80000700"], QueueRecorder.Calls);
        Assert.Equal(2u, host.Vendor.Generation);

        host.OnVendorChanged(RynthVendorChange.Reset, previousVendorId: 0x80000700, vendorId: 0);
        host.Bridge.DeliverQueued();
        Assert.Equal(["vendor-open 80000700", "vendor-open 80000700", "vendor-close 80000700"], QueueRecorder.Calls);

        host.OnVendorChanged(RynthVendorChange.Opened, previousVendorId: 0, vendorId: 0x80000800);
        host.OnVendorChanged(RynthVendorChange.Closed, previousVendorId: 0x80000800, vendorId: 0);
        host.Bridge.DeliverQueued();
        Assert.Equal(
            ["vendor-open 80000700", "vendor-open 80000700", "vendor-close 80000700", "vendor-open 80000800", "vendor-close 80000800"],
            QueueRecorder.Calls);
        Assert.Equal(3u, host.Vendor.Generation);
    }

    [Fact]
    public void AResetClearsTheRequestInFlight()
    {
        QueueRecorder.Calls.Clear();
        _view.OpenVendor = new RynthVendor(0x80000700, "Vendor", 0, 0, 0, false, 1f, 1f, 0, "", 0, -1, 0,
            [new(0x80000701, 1, "Sword", 1, 0, Amount: -1, StackSize: 1, MaxStackSize: 0, Value: 1, UnitValue: 1,
                UnitPrice: 1, Burden: 1, NeedsContainerSlot: false)]);
        using var host = new RynthHost(_view, _ => { }, [typeof(QueueRecorder)]);

        Assert.NotEqual(0u, host.Vendor.Buy(0, [(0x80000701, 1)]));
        Assert.True(host.Vendor.InFlight);
        host.OnVendorChanged(RynthVendorChange.Reset, previousVendorId: 0x80000700, vendorId: 0);
        Assert.False(host.Vendor.InFlight);
        Assert.Equal((RynthVendorTrades.StateDone, RynthVendorTrades.ResultTimedOut),
            (host.Vendor.Status.State, host.Vendor.Status.Result));
    }

    private static RuntimeEntityDelta Created(uint objectId) =>
        new(default, RuntimeEntityChange.Registered,
            new RuntimeEntitySnapshot(new RuntimeEntityIdentity(objectId, 1, 1), 0, 0, null));

    // Raises bridge events from inside its own create callback, once.
    private static unsafe class ReentrantRecorder
    {
        public static readonly List<string> Calls = new();
        public static RynthEventBridge? Bridge;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnBusyCountIncremented", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void BusyUp() => Calls.Add("busy+");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnCreateObject", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Create(uint id)
        {
            Calls.Add($"create {id:X8}");
            if (id != 0x80000020 || Bridge is null)
                return;
            Bridge.OnKillLanded("raised while delivering");   // a later kind: this delivery
            Bridge.OnBusyCountIncremented();                  // an earlier kind: the next one
            Bridge.OnEntity(Created(0x80000021));             // the same kind: the next one
        }

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnKillNotification", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Kill(IntPtr text) => Calls.Add($"kill {Marshal.PtrToStringUni(text)}");
    }

    private static unsafe class QueueRecorder
    {
        public static readonly List<string> Calls = new();

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginTick", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Tick() => Calls.Add("tick");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnLoginComplete", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Login() => Calls.Add("login");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnLogout", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Logout() => Calls.Add("logout");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnBusyCountIncremented", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void BusyUp() => Calls.Add("busy+");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnBusyCountDecremented", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void BusyDown() => Calls.Add("busy-");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnCreateObject", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Create(uint id) => Calls.Add($"create {id:X8}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnSelectedTargetChange", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Selected(uint current, uint previous) => Calls.Add($"select {current:X8} {previous:X8}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnUpdateHealth", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Health(uint id, float ratio, uint current, uint max) =>
            Calls.Add($"health {id:X8} {ratio.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnKillNotification", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Kill(IntPtr text) => Calls.Add($"kill {Marshal.PtrToStringUni(text)}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnEnchantmentRemoved", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Removed(uint spellId) => Calls.Add($"ench- {spellId}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnStopViewingObjectContents", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void StopViewing(uint id) => Calls.Add($"stop-viewing {id:X8}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnUpdateObjectInventory", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void UpdateInventory(uint id) => Calls.Add($"update-inventory {id:X8}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnVendorOpen", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void VendorOpen(uint id) => Calls.Add($"vendor-open {id:X8}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnVendorClose", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void VendorClose(uint id) => Calls.Add($"vendor-close {id:X8}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnChatWindowText", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Chat(IntPtr text, int chatType, IntPtr eat) =>
            Calls.Add($"chat {chatType:X} {Marshal.PtrToStringUni(text)}");
    }
}
