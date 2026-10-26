using System.Buffers.Binary;
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Net;
using AcDream.Core.Net.Messages;
using AcDream.Core.Physics;
using AcDream.Core.Spells;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.Tests.Hosting;

/// <summary>
/// A graphical client's session with no window fights: it changes its combat
/// mode, picks a target, swings and casts through the session's own
/// operations, the ones the windowless client plays through. A window's
/// operations take over while it is attached, and the session's answer again
/// once it lets go.
///
/// Each of these failed before the session had operations of its own: with
/// no window the four operation slots were empty, so a plugin's combat-mode
/// request came back Unavailable, a swing or a cast sent nothing, a target
/// search found nothing, and a slot a closed window had let go of was empty
/// again.
/// </summary>
public sealed partial class WindowlessSessionCombatTests
{
    private const uint Player = WindowlessSessionFixture.CharacterId;
    private const uint Monster = 0x80000101u;
    private const uint SpellId = 0x0000003Eu;

    /// <summary>
    /// What a bot does first: a plugin asks for melee and the request is
    /// taken. Before the fix this answered Unavailable (the session was not
    /// "in the world" as far as combat was concerned) and nothing was ever
    /// sent: the bot stood still saying "Combat mode is unavailable". The
    /// stand-in server places no body, so the request is parked until the
    /// body is ready, as it is with a window; what leaves for the server once
    /// it is ready is the session's own change-combat-mode request.
    /// </summary>
    [Fact]
    public void A_plugins_combat_mode_request_with_no_window_is_taken_and_reaches_the_server()
    {
        using var session = new WindowlessSessionFixture();
        session.EnterWorld();
        List<byte[]> sent = Capture(session);
        ICombatAutomation combat = session.Automation.Combat;

        PluginCombatCommandResult result = combat.EnterMode(PluginCombatMode.Melee);

        Assert.Equal(PluginCombatCommandStatus.ModeChangeSent, result.Status);
        Assert.Equal(CombatMode.Melee, session.Host.Runtime.ActionOwner.CombatMode.PendingMode);
        Assert.True(session.Host.CombatModeOperations.IsInWorld);

        // What the parked request does once the body is ready.
        session.Host.CombatModeOperations.SendChangeCombatMode(CombatMode.Melee);

        byte[] request = Assert.Single(sent);
        Assert.Equal(CharacterActions.ChangeCombatModeOpcode, Opcode(request));
        Assert.Equal(
            (uint)CombatMode.Melee,
            BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(12)));
    }

    /// <summary>
    /// The nearest monster is found and selected with no window, as the
    /// select-nearest key does with one.
    /// </summary>
    [Fact]
    public void Selecting_the_closest_target_with_no_window_selects_the_monster()
    {
        using var session = new WindowlessSessionFixture();
        session.EnterWorld();
        StageMonster(session);

        uint? selected = session.Host.CombatTargetOperations.SelectClosestTarget();

        Assert.Equal(Monster, selected);
        Assert.Equal(
            Monster,
            session.Host.Runtime.ActionOwner.Selection.SelectedObjectId);
    }

    /// <summary>
    /// In melee with a monster selected, a swing goes out at it.
    /// </summary>
    [Fact]
    public void A_swing_with_no_window_goes_out_at_the_selected_monster()
    {
        using var session = new WindowlessSessionFixture();
        session.EnterWorld();
        StageMonster(session);
        GameRuntime runtime = session.Host.Runtime;
        runtime.ActionOwner.Combat.SetCombatMode(CombatMode.Melee);
        runtime.ActionOwner.Selection.Select(
            Monster,
            AcDream.Core.Selection.SelectionChangeSource.Plugin);
        List<byte[]> sent = Capture(session);

        Assert.True(session.Host.CombatAttackOperations.CanStartAttack(allowAutoTarget: false));
        Assert.True(session.Host.CombatAttackOperations.SendAttack(
            AttackHeight.High,
            0.75f,
            allowAutoTarget: false));

        byte[] swing = Assert.Single(sent);
        Assert.Equal(AttackTargetRequest.TargetedMeleeAttackOpcode, Opcode(swing));
        Assert.Equal(Monster, BinaryPrimitives.ReadUInt32LittleEndian(swing.AsSpan(12)));
    }

    /// <summary>A targeted and an untargeted cast go out with no window.</summary>
    [Fact]
    public void A_cast_with_no_window_goes_out()
    {
        using var session = new WindowlessSessionFixture();
        session.EnterWorld();
        StageMonster(session);
        List<byte[]> sent = Capture(session);
        IRuntimeSpellCastOperations spells = session.Host.SpellCastOperations;

        Assert.True(spells.CanSend);
        Assert.Equal(Player, spells.LocalPlayerId);
        spells.SendTargeted(Monster, SpellId);
        spells.SendUntargeted(SpellId);

        Assert.Equal(
            [CastSpellRequest.TargetedSubOpcode, CastSpellRequest.UntargetedSubOpcode],
            sent.Select(Opcode));
        Assert.Equal(Monster, BinaryPrimitives.ReadUInt32LittleEndian(sent[0].AsSpan(12)));
    }

    /// <summary>
    /// A window attached to the session binds its own operations, and those
    /// are what the session uses while it is attached; when the window lets
    /// go (its bindings are disposed as it closes), the session's own are
    /// used again, not nothing.
    /// </summary>
    [Fact]
    public void A_windows_operations_are_used_while_it_is_attached_and_the_sessions_after_it_closes()
    {
        using var session = new WindowlessSessionFixture();
        session.EnterWorld();
        StageMonster(session);
        List<byte[]> sent = Capture(session);
        ICombatAutomation combat = session.Automation.Combat;
        var window = new WindowOperations();

        using (BindWindow(session, window))
        {
            session.Host.CombatModeOperations.SendChangeCombatMode(CombatMode.Melee);
            Assert.Null(session.Host.CombatTargetOperations.SelectClosestTarget());
            session.Host.SpellCastOperations.SendUntargeted(SpellId);
            Assert.False(session.Host.CombatAttackOperations.SendAttack(
                AttackHeight.Low, 1f, allowAutoTarget: true));

            Assert.Equal(
                ["mode Melee", "closest", "cast", "attack"],
                window.Calls);
            Assert.Empty(sent);
        }

        // The window closed; the session plays on with its own.
        session.Tick();
        Assert.Equal(
            PluginCombatCommandStatus.ModeChangeSent,
            combat.EnterMode(PluginCombatMode.Melee).Status);
        session.Host.CombatModeOperations.SendChangeCombatMode(CombatMode.Melee);
        Assert.Equal(Monster, session.Host.CombatTargetOperations.SelectClosestTarget());
        session.Host.SpellCastOperations.SendUntargeted(SpellId);
        Assert.Equal(4, window.Calls.Count);
        // (Selecting the monster also asks the server for its health.)
        Assert.Equal(
            [CharacterActions.ChangeCombatModeOpcode, CastSpellRequest.UntargetedSubOpcode],
            sent.Select(Opcode).Where(static opcode =>
                opcode is CharacterActions.ChangeCombatModeOpcode
                    or CastSpellRequest.UntargetedSubOpcode));

        // A second window takes them over again.
        var second = new WindowOperations();
        using (BindWindow(session, second))
        {
            session.Host.SpellCastOperations.SendUntargeted(SpellId);
            Assert.Equal(["cast"], second.Calls);
        }
    }

    private static IDisposable BindWindow(
        WindowlessSessionFixture session,
        WindowOperations window)
    {
        var bindings = new List<IDisposable>
        {
            session.Host.CombatAttackOperations.BindOwned(window),
            session.Host.CombatTargetOperations.BindOwned(window),
            session.Host.CombatModeOperations.BindOwned(window),
            session.Host.SpellCastOperations.BindOwned(window),
        };
        return new Bindings(bindings);
    }

    private sealed class Bindings(List<IDisposable> bindings) : IDisposable
    {
        public void Dispose()
        {
            foreach (IDisposable binding in bindings)
                binding.Dispose();
        }
    }

    private static List<byte[]> Capture(WindowlessSessionFixture session)
    {
        var sent = new List<byte[]>();
        session.Connections.Sessions[^1].GameActionCapture = sent.Add;
        return sent;
    }

    private static uint Opcode(byte[] action) =>
        BinaryPrimitives.ReadUInt32LittleEndian(action.AsSpan(8));

    private static void StageMonster(WindowlessSessionFixture session)
    {
        GameRuntime runtime = session.Host.Runtime;
        Add(runtime, Player, 10f, new ClientObject
        {
            ObjectId = Player,
            Type = ItemType.Creature,
            PublicWeenieBitfield = SelectedObjectHealthPolicy.BfPlayer,
        });
        Add(runtime, Monster, 13f, new ClientObject
        {
            ObjectId = Monster,
            Type = ItemType.Creature,
            PublicWeenieBitfield = SelectedObjectHealthPolicy.BfAttackable,
        });
    }

    private static void Add(
        GameRuntime runtime,
        uint guid,
        float x,
        ClientObject item,
        uint cell = 0x01010001u,
        float y = 10f,
        float z = 5f)
    {
        RuntimeEntityRecord record = runtime.EntityObjects
            .RegisterEntity(Spawn(guid, x, cell, y, z))
            .Canonical!;
        runtime.EntityObjects.ApplyAcceptedSpawn(
            record,
            record.CreateIntegrationVersion,
            record.Snapshot,
            replaceGeneration: false);
        runtime.InventoryOwner.Objects.AddOrUpdate(item);
    }

    private static WorldSession.EntitySpawn Spawn(
        uint guid,
        float x,
        uint cell = 0x01010001u,
        float y = 10f,
        float z = 5f)
    {
        var position = new CreateObject.ServerPosition(
            cell, x, y, z, 1f, 0f, 0f, 0f);
        var timestamps = new PhysicsTimestamps(
            Position: 1,
            Movement: 1,
            State: 1,
            Vector: 1,
            Teleport: 0,
            ServerControlledMove: 1,
            ForcePosition: 0,
            ObjDesc: 1,
            Instance: 1);
        var physics = new PhysicsSpawnData(
            RawState: 0u,
            Position: position,
            Movement: null,
            AnimationFrame: null,
            SetupTableId: 0x02000001u,
            MotionTableId: null,
            SoundTableId: null,
            PhysicsScriptTableId: null,
            Parent: null,
            Children: null,
            Scale: null,
            Friction: null,
            Elasticity: null,
            Translucency: null,
            Velocity: null,
            Acceleration: null,
            AngularVelocity: null,
            DefaultScriptType: null,
            DefaultScriptIntensity: null,
            Timestamps: timestamps);
        return new WorldSession.EntitySpawn(
            guid,
            position,
            0x02000001u,
            [],
            [],
            [],
            null,
            null,
            guid.ToString("X8"),
            null,
            null,
            null,
            PhysicsState: physics.RawState,
            InstanceSequence: 1,
            MovementSequence: 1,
            ServerControlSequence: 1,
            PositionSequence: 1,
            Physics: physics);
    }

    /// <summary>
    /// Stands in for a window's operations: it records what it is asked and
    /// sends nothing, so a call that reaches it is told apart from one that
    /// reaches the session's own.
    /// </summary>
    private sealed class WindowOperations
        : IRuntimeCombatAttackOperations,
          IRuntimeCombatTargetOperations,
          IRuntimeCombatModeOperations,
          IRuntimeSpellCastOperations
    {
        internal List<string> Calls { get; } = [];

        public bool CanStartAttack(bool allowAutoTarget) => false;
        public void PrepareAttackRequest() { }
        public bool SendAttack(AttackHeight height, float power, bool allowAutoTarget)
        {
            Calls.Add("attack");
            return false;
        }
        public void SendCancelAttack() { }
        public bool IsDualWield => false;
        public bool PlayerReadyForAttack => false;
        public bool AutoRepeatAttack => false;

        public bool AutoTarget => false;
        public uint? SelectClosestTarget()
        {
            Calls.Add("closest");
            return null;
        }

        public bool IsInWorld => true;
        public IReadOnlyList<ClientObject> GetOrderedEquipment() => [];
        public void NotifyExplicitCombatModeRequest() { }
        public void SendChangeCombatMode(CombatMode mode) => Calls.Add($"mode {mode}");

        public uint LocalPlayerId => Player;
        public bool CanSend => true;
        public bool HasRequiredComponents(uint spellId) => true;
        public bool IsTargetCompatible(uint targetId, SpellMetadata spell, bool showMessage) => true;
        public void StopCompletely() { }
        public void SendUntargeted(uint spellId) => Calls.Add("cast");
        public void SendTargeted(uint targetId, uint spellId) => Calls.Add("cast");
        public void DisplayMessage(string message) { }
        public void IncrementBusy() { }
    }
}
