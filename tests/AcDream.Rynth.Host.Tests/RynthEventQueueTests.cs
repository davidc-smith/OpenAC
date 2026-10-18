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
    public void TheTickDeliversEventsBeforeThePluginsOwnTick()
    {
        QueueRecorder.Calls.Clear();
        using var host = new RynthHost(_view, _ => { }, [typeof(QueueRecorder)]);

        host.Bridge.OnBusyCountIncremented();
        host.Bridge.OnBusyCountDecremented();
        host.Tick();

        Assert.Equal(["busy+", "busy-", "tick"], QueueRecorder.Calls);
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

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnChatWindowText", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Chat(IntPtr text, int chatType, IntPtr eat) =>
            Calls.Add($"chat {chatType:X} {Marshal.PtrToStringUni(text)}");
    }
}
