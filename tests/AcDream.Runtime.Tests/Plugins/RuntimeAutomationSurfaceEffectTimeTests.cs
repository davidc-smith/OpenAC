using AcDream.Core.Spells;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Plugins;
using AcDream.Runtime.Tests.Support;

namespace AcDream.Runtime.Tests.Plugins;

/// <summary>
/// The time left on an effect, as a plugin reads it. Both hosts stamp an
/// effect with the runtime's simulation clock when it arrives, so the surface
/// has to measure "now" on that same clock.
/// </summary>
public sealed class RuntimeAutomationSurfaceEffectTimeTests
{
    private const uint SpellId = 0x0400u;
    private const uint CooldownId = 0x0010u;

    /// <summary>
    /// Mutation: measure "now" on any other clock (a stopwatch, the wall
    /// clock) and the buff reads that clock's offset too long or too short.
    /// </summary>
    [Fact]
    public void ATimedBuffReadsItsDurationLessTheSimulationTimeSinceItArrived()
    {
        using var host = new NoWindowGameRuntimeHost();
        GameRuntime runtime = host.Runtime;
        _ = runtime.AdvanceFrameClock(100d);
        using var surface = new RuntimeAutomationSurface();
        surface.Bind(runtime, runtime.CharacterOwner, runtime.ActionOwner.SpellCast);

        runtime.CharacterOwner.Spellbook.OnEnchantmentAdded(
            new ActiveEnchantmentRecord(
                SpellId,
                LayerId: 1u,
                Duration: 60d,
                CasterGuid: 0u,
                Bucket: 1u,
                StartTime: runtime.Clock.SimulationTimeSeconds));
        _ = runtime.AdvanceFrameClock(10d);

        PluginActiveEnchantment buff = Assert.Single(surface.TimedEnchantments);
        Assert.Equal(SpellId, buff.SpellId);
        Assert.Equal(50d, buff.SecondsRemaining, 6);
    }

    /// <summary>
    /// Mutation: as above, for the cooldown a plugin waits out before it
    /// uses an item again.
    /// </summary>
    [Fact]
    public void ACooldownReadsItsDurationLessTheSimulationTimeSinceItArrived()
    {
        using var host = new NoWindowGameRuntimeHost();
        GameRuntime runtime = host.Runtime;
        _ = runtime.AdvanceFrameClock(100d);
        using var surface = new RuntimeAutomationSurface();
        surface.Bind(runtime, runtime.CharacterOwner, runtime.ActionOwner.SpellCast);

        runtime.CharacterOwner.Spellbook.OnEnchantmentAdded(
            new ActiveEnchantmentRecord(
                CooldownId + Spellbook.CooldownSpellOffset,
                LayerId: 1u,
                Duration: 30d,
                CasterGuid: 0u,
                Bucket: Spellbook.CooldownBucket,
                StartTime: runtime.Clock.SimulationTimeSeconds));
        _ = runtime.AdvanceFrameClock(10d);

        Assert.Equal(20d, surface.GetCooldownRemaining(CooldownId), 6);
    }
}
