using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AcDream.ClientExtensions;
using AcDream.Core.Chat;
using AcDream.Core.Combat;
using AcDream.Core.Selection;
using AcDream.Core.Spells;
using AcDream.Runtime;
using AcDream.Rynth.Host;
using AcDream.TestSupport;
using RynthCore.PluginSdk;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RynthCombatEventsTests : IDisposable
{
    private readonly RynthConfigSandbox _sandbox = new("rynth-combat-");

    public void Dispose() => _sandbox.Dispose();

    // Other lines (chat the plugins write) are not what these tests are about.
    private static string[] Recorded() =>
        [.. CombatRecorder.Calls.Where(call => !call.StartsWith("chat ", StringComparison.Ordinal)
            || call.StartsWith("chat 1A ", StringComparison.Ordinal))];

    [Fact]
    public void BusyHealthStatusAndCooldownsReachThePluginInRynthCoresTerms()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        using var host = new RynthHost(new RuntimeRynthGameView(runtime), _ => { }, [typeof(CombatRecorder)]);
        using var events = new RynthCombatEvents(runtime, host.Bridge);
        CombatState combat = runtime.ActionOwner.Combat;
        Spellbook spellbook = runtime.CharacterOwner.Spellbook;

        spellbook.OnEnchantmentAdded(new ActiveEnchantmentRecord(0x8005u, 1u, 30.0, 0x50000001u,
            Bucket: Spellbook.CooldownBucket));
        CombatRecorder.Calls.Clear();   // the cooldown's added line is not under test; its removal is
        spellbook.OnEnchantmentRemoved(1u, 0x8005u);
        Assert.Equal(["ench- 32773"], Recorded());   // RynthCore reports the server's cooldown removals
        CombatRecorder.Calls.Clear();
        runtime.InventoryOwner.Transactions.IncrementBusyCount();
        runtime.InventoryOwner.Transactions.ClearBusy();
        combat.OnCreatureProfile(0x80000010, new CombatState.CreatureVitals(75, 100, null, null, null, null));
        combat.OnUpdateHealth(0x80000010, 0.5f);
        combat.OnUpdateHealth(0x80000011, 0.25f);
        runtime.CommunicationOwner.AddText("You're too busy!", RetailLogTextType.ClientLocal);

        events.FlushStatusText();

        Assert.Equal(
            [
                "busy+", "busy-",
                "health 80000010 0.75 75 100", "health 80000010 0.50 50 100", "health 80000011 0.25 0 0",
                "chat 1A You're too busy!",
            ],
            Recorded());
    }

    [Fact]
    public void AHealthShareAboveOneForwardsTheRatioWithCurrentHealthCappedAtTheMaximum()
    {
        var runtime = GameRuntimeTestFactory.Create();
        using var host = new RynthHost(new RuntimeRynthGameView(runtime), _ => { }, [typeof(CombatRecorder)]);
        using var events = new RynthCombatEvents(runtime, host.Bridge);
        CombatState combat = runtime.ActionOwner.Combat;
        combat.OnCreatureProfile(0x80000010, new CombatState.CreatureVitals(100, 100, null, null, null, null));
        CombatRecorder.Calls.Clear();

        combat.OnUpdateHealth(0x80000010, 1.5f);

        Assert.Equal(["health 80000010 1.50 100 100"], Recorded());
    }

    [Fact]
    public void ACooldownTheClientDropsOnExpiryIsNotForwarded()
    {
        var runtime = GameRuntimeTestFactory.Create();
        using var host = new RynthHost(new RuntimeRynthGameView(runtime), _ => { }, [typeof(CombatRecorder)]);
        using var events = new RynthCombatEvents(runtime, host.Bridge);
        Spellbook spellbook = runtime.CharacterOwner.Spellbook;
        spellbook.OnEnchantmentAdded(new ActiveEnchantmentRecord(0x8005u, 1u, 30.0, 0u,
            Bucket: Spellbook.CooldownBucket, StartTime: 0d));
        CombatRecorder.Calls.Clear();

        Assert.False(spellbook.OnCooldown(5u, currentTime: 100d, out _));

        Assert.Equal(0, spellbook.ActiveCount);
        Assert.Empty(Recorded());
    }

    [Fact]
    public void AStatusLineIsHeldUntilTheFlush()
    {
        var runtime = GameRuntimeTestFactory.Create();
        using var host = new RynthHost(new RuntimeRynthGameView(runtime), _ => { }, [typeof(CombatRecorder)]);
        using var events = new RynthCombatEvents(runtime, host.Bridge);
        CombatRecorder.Calls.Clear();

        runtime.CommunicationOwner.AddText("You're too busy!", RetailLogTextType.ClientLocal);
        Assert.DoesNotContain(Recorded(), call => call.StartsWith("chat 1A", StringComparison.Ordinal));

        events.FlushStatusText();
        Assert.Single(Recorded(), call => call.StartsWith("chat 1A", StringComparison.Ordinal));
        events.FlushStatusText();
        Assert.Single(Recorded(), call => call.StartsWith("chat 1A", StringComparison.Ordinal));
    }

    [Fact]
    public void TheExtensionTickFlushesStatusLines()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = _ => { },
            RegisterChatCommand = (_, _) => new Registration(),
        };
        var extension = new RynthClientExtension { AdditionalPluginExports = [typeof(CombatRecorder)] };
        extension.Attach(context);
        CombatRecorder.Calls.Clear();

        runtime.CommunicationOwner.AddText("You're too busy!", RetailLogTextType.ClientLocal);
        Assert.DoesNotContain(Recorded(), call => call.StartsWith("chat 1A", StringComparison.Ordinal));
        extension.Tick(1.0 / 60);
        extension.Dispose();

        Assert.Contains(Recorded(), call => call.StartsWith("chat 1A", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectionModeDamageAndEnchantmentsAreForwardedInOrder()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        using var host = new RynthHost(new RuntimeRynthGameView(runtime), _ => { }, [typeof(CombatRecorder)]);
        using var events = new RynthCombatEvents(runtime, host.Bridge);
        CombatRecorder.Calls.Clear();
        CombatState combat = runtime.ActionOwner.Combat;

        runtime.ActionOwner.Selection.Select(0x80000010, SelectionChangeSource.System);
        combat.SetCombatMode(CombatMode.Melee);
        combat.OnAttackerNotification("drudge", 4u, 25u, 0.1, critical: 1u);
        combat.OnDefenderNotification("drudge", 0x80000010u, 1u, 7u, 0u, 0u);
        combat.OnAttackerNotification("drudge", 4u, 0u, 0.0);
        runtime.CharacterOwner.Spellbook.OnEnchantmentAdded(
            new ActiveEnchantmentRecord(2u, 1u, 1800.0, 0x50000001u, Bucket: 1u));
        runtime.CharacterOwner.Spellbook.OnEnchantmentRemoved(1u, 2u);

        Assert.Equal(
            [
                "select 80000010 00000000", "mode 2 1", "damage 25 4 1 1", "damage 7 1 0 0",
                "ench+ 2 1800", "ench- 2",
            ],
            Recorded());
    }

    [Fact]
    public void EnteringTheWorldPushesTheModeAndTheSelection()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        using var host = new RynthHost(new RuntimeRynthGameView(runtime), _ => { }, [typeof(CombatRecorder)]);
        using var events = new RynthCombatEvents(runtime, host.Bridge);
        runtime.ActionOwner.Selection.Select(0x80000010, SelectionChangeSource.System);
        runtime.ActionOwner.Combat.SetCombatMode(CombatMode.Magic);
        CombatRecorder.Calls.Clear();

        host.Bridge.OnLifecycle(new RuntimeLifecycleDelta(
            default, RuntimeLifecycleState.Starting, RuntimeLifecycleState.InWorld));

        Assert.Equal(["login", "mode 8 1", "select 80000010 00000000"], Recorded());
    }

    [Fact]
    public void TheExtensionForwardsUntilDisposed()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = _ => { },
            RegisterChatCommand = (_, _) => new Registration(),
        };
        var extension = new RynthClientExtension { AdditionalPluginExports = [typeof(CombatRecorder)] };
        extension.Attach(context);
        CombatRecorder.Calls.Clear();

        runtime.ActionOwner.Selection.Select(0x80000010, SelectionChangeSource.System);
        extension.Dispose();
        runtime.ActionOwner.Selection.Select(0x80000020, SelectionChangeSource.System);

        Assert.Equal(["select 80000010 00000000"], Recorded());
    }

    private sealed class Registration : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private static unsafe class CombatRecorder
    {
        public static readonly List<string> Calls = new();

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnLoginComplete", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Login() => Calls.Add("login");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnBusyCountIncremented", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void BusyUp() => Calls.Add("busy+");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnBusyCountDecremented", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void BusyDown() => Calls.Add("busy-");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnSelectedTargetChange", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Selected(uint current, uint previous) => Calls.Add($"select {current:X8} {previous:X8}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnCombatModeChange", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Mode(int current, int previous) => Calls.Add($"mode {current} {previous}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnCombatDamage", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Damage(uint damage, uint type, uint critical, uint isAttacker) =>
            Calls.Add($"damage {damage} {type} {critical} {isAttacker}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnEnchantmentAdded", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Added(uint spellId, double seconds) =>
            Calls.Add($"ench+ {spellId} {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnEnchantmentRemoved", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Removed(uint spellId) => Calls.Add($"ench- {spellId}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnUpdateHealth", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Health(uint id, float ratio, uint current, uint max) =>
            Calls.Add($"health {id:X8} {ratio.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} {current} {max}");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnChatWindowText", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Chat(IntPtr text, int chatType, IntPtr eat) =>
            Calls.Add($"chat {chatType:X} {Marshal.PtrToStringUni(text)}");
    }
}
