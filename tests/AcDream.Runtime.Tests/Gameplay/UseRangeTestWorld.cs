using System.Collections.Immutable;
using System.Numerics;
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Net;
using AcDream.Core.Physics;
using AcDream.Core.Net.Messages;
using AcDream.Core.Spells;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Gameplay;

namespace AcDream.Runtime.Tests.Gameplay;

internal static class UseRangeTestWorld
{
    internal const uint SetupId = 0x02000001u;

    internal static void CacheBody(GameRuntime runtime, float radius, float height) =>
        runtime.EntityObjects.Physics.DataCache.CacheSetup(
            SetupId,
            new FlatSetupCollision(
                ImmutableArray<FlatCollisionCylinder>.Empty,
                [new FlatCollisionSphere(Vector3.Zero, radius)],
                height: height,
                radius: radius,
                stepUpHeight: 0.4f,
                stepDownHeight: 0.4f));

    internal static void SetPosition(
        RuntimeEntityRecord record,
        uint landblock,
        float x,
        float y,
        float z = 5f)
    {
        record.Snapshot = record.Snapshot with
        {
            Position = new CreateObject.ServerPosition(
                landblock, x, y, z, 1f, 0f, 0f, 0f),
        };
    }

    internal static GameRuntime Create()
    {
        var operations = new Operations();
        return new GameRuntime(new GameRuntimeDependencies(
            operations,
            operations,
            operations,
            operations));
    }

    internal static RuntimeEntityRecord Add(
        GameRuntime runtime,
        uint guid,
        uint landblock,
        float x,
        float y,
        float z = 5f,
        float? useRadius = null)
    {
        RuntimeEntityRecord record = runtime.EntityObjects
            .RegisterEntity(Spawn(guid, landblock, x, y, z, useRadius))
            .Canonical!;
        Assert.True(runtime.EntityObjects.ApplyAcceptedSpawn(
            record,
            record.CreateIntegrationVersion,
            record.Snapshot,
            replaceGeneration: false));
        return record;
    }

    internal static WorldSession.EntitySpawn Spawn(
        uint guid,
        uint landblock,
        float x,
        float y,
        float z,
        float? useRadius) =>
        new(
            guid,
            new CreateObject.ServerPosition(landblock, x, y, z, 1f, 0f, 0f, 0f),
            SetupId,
            [],
            [],
            [],
            null,
            null,
            guid.ToString("X8"),
            null,
            null,
            null,
            UseRadius: useRadius);

    internal sealed class Operations :
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
        public bool IsTargetCompatible(
            uint targetId,
            SpellMetadata spell,
            bool showMessage) => false;
        public void StopCompletely() { }
        public void SendUntargeted(uint spellId) { }
        public void SendTargeted(uint targetId, uint spellId) { }
        public void DisplayMessage(string message) { }
        public void IncrementBusy() { }
    }
}
