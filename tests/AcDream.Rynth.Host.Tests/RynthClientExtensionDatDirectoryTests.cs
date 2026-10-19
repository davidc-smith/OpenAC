using AcDream.ClientExtensions;
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Spells;
using AcDream.Runtime;
using AcDream.Runtime.Gameplay;
using AcDream.Rynth.Plugin.RynthAi.StandIns;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthClientExtensionDatDirectoryTests
{
    [Fact]
    public void AttachSetsTheStandInPathAndDisposeClearsIt()
    {
        var ops = new Noop();
        var runtime = new GameRuntime(new GameRuntimeDependencies(ops, ops, ops, ops));
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = static _ => { },
            RegisterChatCommand = static (_, _) => new NoRegistration(),
            DatDirectory = "/x",
        };
        var extension = new RynthClientExtension();
        try
        {
            try
            {
                extension.Attach(context);
                Assert.Equal("/x", RegistryStandIn.InstallPath);
            }
            finally
            {
                extension.Dispose();
            }

            Assert.Null(RegistryStandIn.InstallPath);
        }
        finally
        {
            RegistryStandIn.InstallPath = null;
        }
    }

    private sealed class NoRegistration : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class Noop :
        IRuntimeCombatAttackOperations,
        IRuntimeCombatTargetOperations,
        IRuntimeCombatModeOperations,
        IRuntimeSpellCastOperations
    {
        public bool CanStartAttack(bool allowAutoTarget) => false;
        public void PrepareAttackRequest() { }
        public bool SendAttack(AttackHeight height, float power, bool allowAutoTarget) => false;
        public void SendCancelAttack() { }
        public bool IsDualWield => false;
        public bool PlayerReadyForAttack => false;
        public bool AutoRepeatAttack => false;
        public bool AutoTarget => false;
        public uint? SelectClosestTarget() => null;
        public bool IsInWorld => false;
        public IReadOnlyList<ClientObject> GetOrderedEquipment() => [];
        public void NotifyExplicitCombatModeRequest() { }
        public void SendChangeCombatMode(CombatMode mode) { }
        public uint LocalPlayerId => 0u;
        public bool CanSend => false;
        public bool HasRequiredComponents(uint spellId) => false;
        public bool IsTargetCompatible(uint targetId, SpellMetadata spell, bool showMessage) => false;
        public void StopCompletely() { }
        public void SendUntargeted(uint spellId) { }
        public void SendTargeted(uint targetId, uint spellId) { }
        public void DisplayMessage(string message) { }
        public void IncrementBusy() { }
    }
}
