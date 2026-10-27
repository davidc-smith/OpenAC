using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using AcDream.Content;
using AcDream.Content.Pak;
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Net.Messages;
using AcDream.Core.Physics;
using AcDream.Core.Properties;
using AcDream.Core.Spells;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Gameplay;
using AcDream.Runtime.Physics;
using AcDream.Runtime.Session;
using CoreSpellTable = AcDream.Core.Spells.SpellTable;

namespace AcDream.App.Tests.Hosting;

/// <summary>
/// The same window-less fights, driven the way a plugin drives them: a cast
/// through the runtime's spell-cast owner with spell tables installed as the
/// session's game files install them, a combat-mode request parked behind
/// the character's arrival pose and sent by the session's own frame, a swing
/// loaded and released through the plugin surface and fired by the frame,
/// and a connection opened again after the first is stopped.
///
/// Each fails with the session's operations left out of the slots (the four
/// <c>UseSessionOperations</c> lines in the session host commented out): the
/// cast is refused for components, the parked request never leaves, the
/// swing never leaves, and nothing goes out on the second connection. The
/// cast also fails with only the session's spell tables left out
/// (<c>UseMagicCatalog</c> in <c>UseMagic</c> commented out).
/// </summary>
public sealed partial class WindowlessSessionCombatTests
{
    private const uint TargetedSpell = 0x0000003Eu;
    private const uint UntargetedSpell = 0x0000003Fu;
    private const uint ScarabComponentId = 10u;
    private const uint ScarabWeenieClass = 0x00001A2Bu;
    private const uint CarriedScarab = 0x50000099u;

    /// <summary>
    /// A cast at a monster through the runtime's spell-cast owner, the path
    /// a plugin's cast takes: refused while the character lacks the
    /// formula's component, sent once it carries it.
    /// </summary>
    [Fact]
    public void A_cast_with_no_window_through_the_spell_cast_owner_checks_components_and_target_and_goes_out()
    {
        using var session = new WindowlessSessionFixture();
        session.EnterWorld();
        StageMonster(session);
        GameRuntime runtime = session.Host.Runtime;
        session.Host.UseMagic(TestCatalog());
        runtime.CharacterOwner.Spellbook.OnSpellLearned(TargetedSpell);
        List<byte[]> sent = Capture(session);

        Assert.Equal(
            CastRequestResult.MissingComponents,
            runtime.ActionOwner.SpellCast.Cast(TargetedSpell, Monster));
        Assert.Empty(sent);

        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject
        {
            ObjectId = CarriedScarab,
            Type = ItemType.SpellComponents,
            WeenieClassId = ScarabWeenieClass,
            ContainerId = Player,
            StackSize = 1,
        });
        Assert.Equal(
            SpellCastGate.TargetCompatible,
            runtime.ActionOwner.SpellCast.EvaluateCastGate(TargetedSpell, Monster));
        Assert.Equal(
            CastRequestResult.Sent,
            runtime.ActionOwner.SpellCast.Cast(TargetedSpell, Monster));

        byte[] cast = Assert.Single(sent);
        Assert.Equal(CastSpellRequest.TargetedSubOpcode, Opcode(cast));
        Assert.Equal(Monster, BinaryPrimitives.ReadUInt32LittleEndian(cast.AsSpan(12)));
        Assert.Equal(TargetedSpell, BinaryPrimitives.ReadUInt32LittleEndian(cast.AsSpan(16)));
    }

    /// <summary>
    /// A plugin asks for melee while the character is still taking up its
    /// arrival pose: the request is parked, and the session's own frame
    /// sends it once the body is ready.
    /// </summary>
    [Fact]
    public void A_parked_combat_mode_request_with_no_window_is_sent_by_the_sessions_frame()
    {
        using var session = new WindowlessSessionFixture();
        session.EnterWorld();
        using WindowlessBody body = WindowlessBody.Stage(session);
        List<byte[]> sent = Capture(session);

        PluginCombatCommandResult result =
            session.Automation.Combat.EnterMode(PluginCombatMode.Melee);

        Assert.Equal(PluginCombatCommandStatus.ModeChangeSent, result.Status);
        Assert.Equal(CombatMode.Melee, session.Host.Runtime.ActionOwner.CombatMode.PendingMode);
        Assert.DoesNotContain(sent, action => Opcode(action) == CharacterActions.ChangeCombatModeOpcode);

        for (int frame = 0;
             frame < 30 && !sent.Any(action => Opcode(action) == CharacterActions.ChangeCombatModeOpcode);
             frame++)
        {
            body.Tick();
        }

        byte[] request = Assert.Single(sent, action =>
            Opcode(action) == CharacterActions.ChangeCombatModeOpcode);
        Assert.Equal(
            (uint)CombatMode.Melee,
            BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(12)));
        Assert.Null(session.Host.Runtime.ActionOwner.CombatMode.PendingMode);
    }

    /// <summary>
    /// In melee, a plugin loads a swing at a monster and lets go; the
    /// session's frame fills the power bar and the swing goes out.
    /// </summary>
    [Fact]
    public void A_plugins_swing_with_no_window_is_fired_by_the_sessions_frame()
    {
        using var session = new WindowlessSessionFixture();
        session.EnterWorld();
        using WindowlessBody body = WindowlessBody.Stage(session);
        body.Settle();
        ICombatAutomation combat = session.Automation.Combat;
        Assert.Equal(
            PluginCombatCommandStatus.ModeChangeSent,
            combat.EnterMode(PluginCombatMode.Melee).Status);
        for (int frame = 0;
             frame < 60 && !session.Host.CombatAttackOperations.PlayerReadyForAttack;
             frame++)
        {
            body.Tick();
        }
        List<byte[]> sent = Capture(session);

        PluginCombatCommandResult begun = combat.BeginPhysicalAttack(
            Monster, PluginAttackHeight.Medium, 0.5f);
        PluginCombatCommandResult released = combat.ReleasePhysicalAttack();
        for (int frame = 0;
             frame < 80 && !sent.Any(action => Opcode(action) == AttackTargetRequest.TargetedMeleeAttackOpcode);
             frame++)
        {
            body.Tick();
        }

        Assert.Equal(PluginCombatCommandStatus.Started, begun.Status);
        Assert.True(released.Accepted, $"release answered {released.Status}");
        byte[] swing = Assert.Single(sent, action =>
            Opcode(action) == AttackTargetRequest.TargetedMeleeAttackOpcode);
        Assert.Equal(Monster, BinaryPrimitives.ReadUInt32LittleEndian(swing.AsSpan(12)));
    }

    /// <summary>
    /// The session stops and logs in again on a new connection: the
    /// session's operations follow it there, and nothing goes out on the
    /// connection that was let go of.
    /// </summary>
    [Fact]
    public void After_logging_in_again_with_no_window_a_cast_goes_out_on_the_new_connection()
    {
        using var session = new WindowlessSessionFixture();
        session.EnterWorld();
        GameRuntime runtime = session.Host.Runtime;
        session.Host.UseMagic(TestCatalog());
        List<byte[]> first = Capture(session);

        var commands = session.Host.CommandsWithoutWindow.Session;
        commands.Stop(runtime.Generation);
        commands.Start(runtime.Generation);
        for (int frame = 0; frame < 20 && !runtime.Session.IsInWorld; frame++)
            Assert.False(session.Update());
        Assert.True(runtime.Session.IsInWorld);
        Assert.Equal(2, session.Connections.Sessions.Count);
        List<byte[]> second = Capture(session);
        // A new login starts a new spellbook; the character's own record
        // says components are not required, so only the connection matters.
        runtime.CharacterOwner.Spellbook.OnSpellLearned(UntargetedSpell);
        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject
        {
            ObjectId = Player,
            Type = ItemType.Creature,
            PublicWeenieBitfield = SelectedObjectHealthPolicy.BfPlayer,
            Properties =
            {
                Bools = { [SpellComponentRequirementService.SpellComponentsRequiredProperty] = false },
            },
        });

        Assert.Equal(
            CastRequestResult.Sent,
            runtime.ActionOwner.SpellCast.Cast(UntargetedSpell, 0u));

        byte[] cast = Assert.Single(second);
        Assert.Equal(CastSpellRequest.UntargetedSubOpcode, Opcode(cast));
        Assert.Empty(first);
    }

    /// <summary>
    /// Two spells: a war bolt cast at a creature, and one cast at nothing.
    /// Each needs one scarab, which the catalog knows by its weenie class.
    /// </summary>
    private static MagicCatalog TestCatalog()
    {
        SpellMetadata bolt = Spell(TargetedSpell, "Test Bolt", targetMask: (uint)ItemType.Creature, untargeted: false);
        SpellMetadata ward = Spell(UntargetedSpell, "Test Ward", targetMask: 0u, untargeted: true);
        var formulas = new Dictionary<uint, SpellFormulaDefinition>
        {
            [TargetedSpell] = new(0u, [ScarabComponentId], (uint)MagicSchool.WarMagic),
            [UntargetedSpell] = new(0u, [ScarabComponentId], (uint)MagicSchool.WarMagic),
        };
        return new MagicCatalog(
            CoreSpellTable.Create([bolt, ward]),
            new Dictionary<uint, SpellComponentDescriptor>
            {
                [ScarabWeenieClass] = new(ScarabWeenieClass, "Test Scarab", 0u, 0u)
                {
                    SpellComponentId = ScarabComponentId,
                },
            },
            formulas,
            new Dictionary<uint, uint> { [ScarabComponentId] = ScarabWeenieClass },
            new Dictionary<uint, uint>(),
            new Dictionary<uint, int> { [TargetedSpell] = 1, [UntargetedSpell] = 1 });
    }

    private static SpellMetadata Spell(uint id, string name, uint targetMask, bool untargeted) =>
        new(
            SpellId: id,
            Name: name,
            School: "War Magic",
            Family: 1u,
            IconId: 0u,
            SpellWords: string.Empty,
            Duration: 0f,
            ManaCost: 10,
            IsDebuff: false,
            IsFellowship: false,
            Description: string.Empty,
            SortKey: (int)id,
            Difficulty: 1,
            Flags: 0u,
            Generation: 1,
            IsFastWindup: false,
            IsOffensive: !untargeted,
            IsUntargeted: untargeted,
            Speed: 1f,
            CasterEffect: 0u,
            TargetEffect: 0u,
            TargetMask: targetMask,
            SpellType: 0)
        {
            SchoolId = MagicSchool.WarMagic,
            FormulaComponents = [ScarabComponentId],
        };

    /// <summary>
    /// A body for the character of a window-less session, standing on flat
    /// ground with a monster three metres out. The runtime's own first-entry
    /// drive builds it; what stands in is only what a test machine cannot
    /// supply: the installed files a body's shape is read from (a plain
    /// sphere) and the drives that need them, so the drive is stepped here
    /// before each of the session's own frames.
    /// </summary>
    private sealed class WindowlessBody : IDisposable
    {
        private const uint Landblock = 0xA9B40000u;
        private const uint Cell = Landblock | 0x0001u;
        private const float GroundHeight = 50f;
        private const float PlayerX = 96f;
        private const float PlayerY = 97f;
        private const uint CombatTable = 0x30000000u;

        private readonly WindowlessSessionFixture _session;
        private readonly RuntimeFirstEntryDriveController _firstEntry;
        private readonly RuntimePlacementProjectionSubscription _placements;

        private WindowlessBody(
            WindowlessSessionFixture session,
            RuntimeFirstEntryDriveController firstEntry,
            RuntimePlacementProjectionSubscription placements)
        {
            _session = session;
            _firstEntry = firstEntry;
            _placements = placements;
        }

        internal static WindowlessBody Stage(WindowlessSessionFixture session)
        {
            GameRuntime runtime = session.Host.Runtime;
            RuntimePhysicsState physics = runtime.EntityObjects.Physics;
            physics.SetPosition.BeginCollisionGeneration(Landblock, 1UL);
            AddFlatGround(physics.Engine);
            physics.SetPosition.CommitCollisionGeneration(Landblock, 1UL, ready: true);
            physics.ObserveLocalWorldFrame(Cell, teleportAdvanced: false);
            var placements = new RuntimePlacementProjectionSubscription(
                runtime,
                new AcceptingPlacementSink(runtime));
            var firstEntry = new RuntimeFirstEntryDriveController(
                runtime.EntityObjects,
                runtime.Clock,
                new SphereCollisionSource(),
                () => PlayerMovementConstructionOptions.From(
                    runtime.CharacterOwner.MovementSkills.Snapshot),
                static _ => new RuntimeLocalPlayerPhysicsActivationPreparation(
                    Radius: 0.48f,
                    Height: 1.835f,
                    RuntimeLocalPlayerShadowDisposition.ProvenShapeless));
            var body = new WindowlessBody(session, firstEntry, placements);

            runtime.PlayerIdentity.ServerGuid = Player;
            RuntimeEntityRecord record = runtime.EntityObjects
                .RegisterEntityWithInitialResidence(
                    Spawn(Player, PlayerX, Cell, PlayerY, GroundHeight),
                    isLocalPlayer: true)
                .Canonical!;
            _ = runtime.EntityObjects.ApplyAcceptedSpawn(
                record,
                record.CreateIntegrationVersion,
                record.Snapshot,
                replaceGeneration: false);
            runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject
            {
                ObjectId = Player,
                Type = ItemType.Creature,
                PublicWeenieBitfield = SelectedObjectHealthPolicy.BfPlayer,
                ItemsCapacity = 102,
                ContainersCapacity = 7,
                Properties = { DataIds = { [(uint)PropertyDataId.CombatTable] = CombatTable } },
            });
            Add(runtime, Monster, PlayerX + 3f, new ClientObject
            {
                ObjectId = Monster,
                Type = ItemType.Creature,
                Name = "Monster",
                PublicWeenieBitfield = SelectedObjectHealthPolicy.BfAttackable
                    | (uint)PublicWeenieFlags.Stuck,
            }, Cell, PlayerY, GroundHeight);
            firstEntry.DriveAll();
            return body;
        }

        /// <summary>One of the session's own frames, with the body's drive stepped first.</summary>
        internal void Tick()
        {
            _firstEntry.DriveAll();
            _session.Tick();
        }

        /// <summary>Runs frames until the body has finished its arrival pose.</summary>
        internal void Settle()
        {
            for (int frame = 0; frame < 30; frame++)
            {
                if (_session.Host.Runtime.MovementOwner.Controller is { } controller
                    && !controller.Motion.MotionsPending())
                {
                    return;
                }
                Tick();
            }
            throw new InvalidOperationException("The body is still taking up its arrival pose.");
        }

        public void Dispose() => _placements.Dispose();

        private static void AddFlatGround(PhysicsEngine engine)
        {
            var heights = new byte[81];
            Array.Fill(heights, (byte)GroundHeight);
            var heightTable = new float[256];
            for (int index = 0; index < heightTable.Length; index++)
                heightTable[index] = index;
            engine.AddLandblock(
                Landblock | 0xFFFFu,
                new TerrainSurface(heights, heightTable),
                [],
                [],
                worldOffsetX: 0f,
                worldOffsetY: 0f);
        }

        /// <summary>
        /// Nothing is drawn, so a placement is taken as shown at once, except
        /// while the character is still arriving, which the arrival itself
        /// acknowledges in step with the body it builds.
        /// </summary>
        private sealed class AcceptingPlacementSink(GameRuntime runtime)
            : IRuntimePlacementProjectionSink
        {
            public bool TryApply(in RuntimePlacementProjectionSnapshot projection)
            {
                RuntimePlacementProjectionToken token = projection.Token;
                if (projection.Kind
                        is RuntimePlacementProjectionKind.Place
                        or RuntimePlacementProjectionKind.Withdraw
                    && token.IsValid
                    && runtime.EntityObjects.Entities.TryGetByLocalId(
                        token.Entity.LocalEntityId,
                        out RuntimeEntityRecord arriving)
                    && runtime.EntityObjects.Entities.IsCurrent(arriving)
                    && arriving.Key == token.Entity
                    && runtime.EntityObjects.TryGetInitialCreateResidence(
                        arriving,
                        out _))
                {
                    return false;
                }
                return true;
            }
        }

        private sealed class SphereCollisionSource : IPreparedCollisionSource
        {
            public PreparedAssetPresence ProbeCollision(
                PakAssetType type,
                uint sourceFileId) => PreparedAssetPresence.Available;

            public PreparedCollisionReadResult<FlatSetupCollision> ReadSetupCollision(
                uint sourceFileId,
                CancellationToken cancellationToken = default) =>
                PreparedCollisionReadResult<FlatSetupCollision>.Loaded(
                    new FlatSetupCollision(
                        ImmutableArray<FlatCollisionCylinder>.Empty,
                        [new FlatCollisionSphere(Vector3.Zero, 0.48f)],
                        height: 0f,
                        radius: 0f,
                        stepUpHeight: 0.4f,
                        stepDownHeight: 0.4f));

            public PreparedCollisionReadResult<FlatGfxObjCollisionAsset> ReadGfxObjCollision(
                uint sourceFileId,
                CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public PreparedCollisionReadResult<FlatCellStructureCollisionAsset> ReadCellStructureCollision(
                uint sourceFileId,
                CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public PreparedCollisionReadResult<FlatEnvCellTopology> ReadEnvCellTopology(
                uint sourceFileId,
                CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public PreparedCollisionSourceStats CollisionStats => default;

            public void Dispose()
            {
            }
        }
    }
}
