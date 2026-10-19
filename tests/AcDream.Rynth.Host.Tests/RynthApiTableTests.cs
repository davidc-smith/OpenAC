using System.Numerics;
using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthApiTableTests
{
    private readonly List<string> _log = new();

    [Fact]
    public void NewTable_HasApiVersionAndOnlyTheImplementedSlots()
    {
        using var table = new RynthApiTable(new FakeGameView(), _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.Equal(86u, host.Version);
        Assert.Equal(RynthCoreHost.CurrentApiVersion, RynthApiTable.ApiVersion);
        Assert.True(host.HasGetPlayerId);
        Assert.True(host.HasGetObjectName);
        Assert.True(host.HasGetObjectQuadProperty);
        Assert.True(host.HasCastSpell);
        Assert.True(host.HasUseObject);
    }

    [Fact]
    public void TableFunctions_ReadTheGameView()
    {
        var view = new FakeGameView { PlayerId = 0x50000001 };
        view.Names[0x80000010] = "Drudge Skulker";
        view.Int64s[(0x50000001, 1)] = 123_456;
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.Equal(0x50000001u, host.GetPlayerId());
        Assert.True(host.TryGetObjectName(0x80000010, out string name));
        Assert.Equal("Drudge Skulker", name);
        Assert.False(host.TryGetObjectName(0x80000011, out _));
        Assert.True(host.TryGetObjectQuadProperty(0x50000001, 1, out long xp));
        Assert.Equal(123_456, xp);
        Assert.False(host.TryGetObjectQuadProperty(0x50000001, 6, out _));

        Assert.True(host.WriteToChat("hello", 3));
        Assert.Equal(("hello", 3), Assert.Single(view.ChatLines));

        host.Log("from the plugin");
        Assert.Contains("from the plugin", _log);
    }

    [Fact]
    public void TableFunctions_ReturnNotAvailableWhenTheViewThrows()
    {
        var view = new FakeGameView { Throw = true };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.False(host.TryGetObjectName(1, out _));
        Assert.False(host.TryGetObjectQuadProperty(1, 1, out _));
        Assert.Equal(0u, host.GetPlayerId());
        Assert.False(host.WriteToChat("hello", 3));
        Assert.Contains(_log, line => line.Contains("GetObjectName threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("GetPlayerId threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("WriteToChat threw", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyOneTableCanBeLive()
    {
        using var first = new RynthApiTable(new FakeGameView(), _log.Add);
        Assert.Throws<InvalidOperationException>(() => new RynthApiTable(new FakeGameView(), _log.Add));
    }

    [Fact]
    public void StateSlots_ReadTheGameView()
    {
        const uint player = 0x50000001;
        var view = new FakeGameView
        {
            PlayerId = player,
            Pose = (0xA9B40021, new Vector3(10, 20, 30), new Quaternion(0, 0, 0.5f, 0.8660254f)),
            PlayerHeading = 90f,
            Portaling = true,
            Busy = 2,
            Mode = 8,
            Vitals = new RynthVitals(50, 100, 60, 110, 70, 120),
            BaseVitals = (90, 100, 110),
            Vitae = 0.95f,
            Time = 1_234.5,
            Enchantments = [new RynthEnchantment(2, 1_300.0), new RynthEnchantment(3, double.MaxValue)],
            Known = [1, 2, 3],
            OpenContainer = 0x80000044,
            Replies = new AcDream.Core.Items.ServerReplySnapshot(7, 0x36, 4, 0x29, 0xA0, 0x80000010),
        };
        view.Headings[0x80000010] = 180f;
        view.Skills[(player, 6)] = (250, 200, 3);
        view.Attributes[(player, 1)] = (150, 100);
        view.VitalBases[(player, 1)] = 180;
        view.Attackable.Add(0x80000010);
        view.Objects[0x80000010] = ObjectWith(bools: new() { [1] = true }, floats: new() { [5] = 2.5 },
            strings: new() { [1] = "Drudge" });
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.TryGetPlayerPose(out uint cell, out float x, out float y, out float z,
            out float qw, out float qx, out float qy, out float qz));
        Assert.Equal((0xA9B40021u, 10f, 20f, 30f), (cell, x, y, z));
        Assert.Equal((0.8660254f, 0f, 0f, 0.5f), (qw, qx, qy, qz));
        Assert.True(host.TryGetPlayerHeading(out float heading));
        Assert.Equal(90f, heading);
        Assert.True(host.TryGetObjectHeading(0x80000010, out float objectHeading));
        Assert.Equal(180f, objectHeading);
        Assert.True(host.IsPortaling());
        Assert.Equal(2, host.GetBusyState());
        Assert.Equal(8, host.GetCurrentCombatMode());
        Assert.True(host.TryGetPlayerVitals(out uint hp, out uint maxHp, out uint st, out uint maxSt, out uint mp, out uint maxMp));
        Assert.Equal((50u, 100u, 60u, 110u, 70u, 120u), (hp, maxHp, st, maxSt, mp, maxMp));
        Assert.True(host.TryGetPlayerBaseVitals(out uint baseHp, out uint baseSt, out uint baseMp));
        Assert.Equal((90u, 100u, 110u), (baseHp, baseSt, baseMp));
        Assert.Equal(0.95f, host.GetVitae(player));
        Assert.Equal(1_234.5, host.GetServerTime());

        var spellIds = new uint[1];
        var expiries = new double[1];
        Assert.Equal(1, host.ReadPlayerEnchantments(spellIds, expiries, 1));   // clamped to max, as RynthCore does
        Assert.Equal((2u, 1_300.0), (spellIds[0], expiries[0]));

        Assert.True(host.TryGetObjectBoolProperty(0x80000010, 1, out bool flag));
        Assert.True(flag);
        Assert.True(host.TryGetObjectDoubleProperty(0x80000010, 5, out double number));
        Assert.Equal(2.5, number);
        Assert.True(host.TryGetObjectStringProperty(0x80000010, 1, out string text));
        Assert.Equal("Drudge", text);
        Assert.False(host.TryGetObjectStringProperty(0x80000010, 2, out _));

        Assert.True(host.TryGetObjectSkill(player, 6, out int buffed, out int training));
        Assert.Equal((250, 3), (buffed, training));
        Assert.True(host.TryGetObjectSkillLevel(player, 6, 1, out int baseLevel));
        Assert.Equal(200, baseLevel);
        Assert.True(host.TryGetObjectSkillLevel(player, 6, 0, out int buffedLevel));
        Assert.Equal(250, buffedLevel);
        Assert.True(host.TryGetObjectAttribute(player, 1, 1, out uint strengthBase));
        Assert.Equal(100u, strengthBase);
        Assert.True(host.TryGetObjectAttribute2ndBaseLevel(player, 1, out uint healthBase));
        Assert.Equal(180u, healthBase);

        Assert.True(host.IsSpellKnown(player, 2, out bool known));
        Assert.True(known);
        Assert.True(host.IsSpellKnown(player, 9, out bool unknown));
        Assert.False(unknown);
        var knownIds = new uint[8];
        Assert.Equal(3, host.ReadKnownSpells(knownIds, 8));
        Assert.Equal(new uint[] { 1, 2, 3 }, knownIds[..3]);
        Assert.Equal(2, host.ReadKnownSpells(new uint[2], 2));

        Assert.True(host.ObjectIsAttackable(0x80000010));
        Assert.False(host.ObjectIsAttackable(0x80000011));
        Assert.Equal(0x80000044u, host.GetGroundContainerId());
        Assert.Equal(7, host.GetUseDoneSeq());
        Assert.True(host.TryGetLastUseDone(out int useSeq, out uint useError));
        Assert.Equal((7, 0x36u), (useSeq, useError));
        Assert.True(host.TryGetLastWeenieError(out int errSeq, out uint err, out uint evt, out uint obj));
        Assert.Equal((4, 0x29u, 0xA0u, 0x80000010u), (errSeq, err, evt, obj));
    }

    [Fact]
    public void StateSlots_ReturnNotAvailableWhenTheViewThrows()
    {
        var view = new FakeGameView { Throw = true };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.False(host.TryGetPlayerPose(out _, out _, out _, out _, out _, out _, out _, out _));
        Assert.False(host.IsPortaling());
        Assert.Equal(0, host.GetBusyState());
        Assert.Equal(1, host.GetCurrentCombatMode());
        Assert.False(host.TryGetPlayerVitals(out _, out _, out _, out _, out _, out _));
        Assert.Equal(1f, host.GetVitae(1));
        Assert.Equal(0, host.GetServerTime());
        Assert.Equal(-1, host.ReadPlayerEnchantments(new uint[4], new double[4], 4));
        Assert.False(host.TryGetObjectBoolProperty(1, 1, out _));
        Assert.False(host.IsSpellKnown(1, 1, out _));
        Assert.Equal(-1, host.ReadKnownSpells(new uint[4], 4));
        Assert.False(host.ObjectIsAttackable(1));
        Assert.Equal(0, host.GetUseDoneSeq());
        Assert.False(host.TryGetPlayerHeading(out _));
        Assert.False(host.TryGetObjectHeading(0x80000010, out _));
        Assert.False(host.TryGetPlayerBaseVitals(out _, out _, out _));
        Assert.False(host.TryGetObjectDoubleProperty(0x80000010, 5, out _));
        Assert.False(host.TryGetObjectStringProperty(0x80000010, 1, out _));
        Assert.False(host.TryGetObjectSkill(1, 6, out _, out _));
        Assert.False(host.TryGetObjectSkillLevel(1, 6, 1, out _));
        Assert.False(host.TryGetObjectAttribute(1, 1, 1, out _));
        Assert.False(host.TryGetObjectAttribute2ndBaseLevel(1, 1, out _));
        Assert.Equal(0u, host.GetGroundContainerId());
        Assert.False(host.TryGetLastUseDone(out _, out _));
        Assert.False(host.TryGetLastWeenieError(out _, out _, out _, out _));
        Assert.Contains(_log, line => line.Contains("GetCurrentCombatMode threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("ReadPlayerEnchantments threw", StringComparison.Ordinal));
    }

    [Fact]
    public void MovementSlotsDriveTheView()
    {
        var view = new FakeGameView();
        view.ObjectStates[0x7A9B4001] = 0x4;
        view.MotionOn[0x7A9B4001] = true;
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.HasSetAutoRun && host.HasTurnToHeading && host.HasSetMotion && host.HasStopCompletely);
        Assert.True(host.HasJumpNonAutonomous && host.HasTapJump && host.HasCommenceJump && host.HasDoJump && host.HasLaunchJumpWithMotion);
        Assert.True(host.HasGetObjectState && host.HasGetObjectMotionOn);

        Assert.True(host.SetAutoRun(true));
        Assert.True(host.TurnToHeading(123.5f));
        Assert.True(host.SetMotion(0x41000003, true));
        Assert.Equal(new AcDream.Plugin.Abstractions.PluginMovementIntent(Forward: true, Run: true), view.Intents[^1]);
        Assert.False(host.SetMotion(0x41000012, true));
        Assert.True(host.JumpNonAutonomous(0.5f));
        Assert.True(host.TapJump());
        Assert.True(host.CommenceJump());
        Assert.True(host.DoJump(true));
        Assert.True(host.CommenceJump());
        Assert.True(host.LaunchJumpWithMotion(false, true, false, false, false));
        Assert.True(host.StopCompletely());
        Assert.Equal(
            new[] { "autorun 1", "heading 123.5", "jump 0.5", FormattableString.Invariant($"jump {RynthApiTable.TapJumpPower:0.###}"), "stop" },
            view.Actions);

        Assert.True(host.TryGetObjectState(0x7A9B4001, out uint state));
        Assert.Equal(0x4u, state);
        Assert.False(host.TryGetObjectState(0x7A9B4002, out _));
        Assert.True(host.TryGetObjectMotionOn(0x7A9B4001, out bool on));
        Assert.True(on);
        Assert.False(host.TryGetObjectMotionOn(0x7A9B4002, out _));

        view.Throw = true;
        Assert.False(host.SetAutoRun(false));
        Assert.False(host.TryGetObjectState(0x7A9B4001, out _));
        Assert.Contains(_log, line => line.Contains("SetAutoRun threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("GetObjectState threw", StringComparison.Ordinal));
    }

    [Fact]
    public void LootSlots_MoveGiveAndCloseReachTheView()
    {
        const uint player = 0x50000001, item = 0x80000100, pack = 0x80000200, corpse = 0x80000300;
        var view = new FakeGameView { PlayerId = player };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.HasMoveItemExternal);
        Assert.True(host.HasGiveObjectTo);
        Assert.True(host.HasCloseContainer);
        Assert.True(host.MoveItemExternal(item, 0, 0));
        Assert.True(host.MoveItemExternal(item, player, 0));
        Assert.True(host.MoveItemExternal(item, pack, 0));
        Assert.True(host.MoveItemExternal(item, pack, 5));
        Assert.False(host.MoveItemExternal(item, pack, -1));
        Assert.False(host.MoveItemExternal(0, pack, 0));
        Assert.True(host.GiveObjectTo(item, 0x50000002, 0));
        Assert.True(host.GiveObjectTo(item, 0x50000002, 3));
        Assert.False(host.GiveObjectTo(item, 0x50000002, -1));
        Assert.False(host.GiveObjectTo(item, 0, 0));
        Assert.True(host.CloseContainer(corpse));
        Assert.False(host.CloseContainer(0));
        Assert.Equal(
            new[]
            {
                "moveext 80000100 00000000 0", "moveext 80000100 50000001 0", "moveext 80000100 80000200 0",
                "moveext 80000100 80000200 5",
                "give 80000100 50000002 0", "give 80000100 50000002 3", "close 80000300",
            },
            view.Actions);
    }

    [Fact]
    public void LootSlots_PalettesAndCountsFollowRynthCoresReturns()
    {
        var view = new FakeGameView { PlayerId = 0x50000001 };
        view.Palettes[0x80000100] = [(0x04001234, 0), (0x04005678, 8), (0x04009ABC, 16)];
        view.ContentCounts[0x50000001] = (40, 3);
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.HasGetObjectPalettes);
        var ids = new uint[2];
        var offsets = new uint[2];
        Assert.Equal(3, host.GetObjectPalettes(0x80000100, ids, offsets, 2));
        Assert.Equal(new uint[] { 0x04001234, 0x04005678 }, ids);
        Assert.Equal(new uint[] { 0, 8 }, offsets);
        Assert.Equal(-1, host.GetObjectPalettes(0x80000999, ids, offsets, 2));

        Assert.Equal(40, host.GetNumContainedItems(0x50000001));
        Assert.Equal(3, host.GetNumContainedContainers(0x50000001));
        Assert.Equal(-1, host.GetNumContainedItems(0x80000999));
        Assert.Equal(-1, host.GetNumContainedContainers(0x80000999));

        view.Throw = true;
        Assert.Equal(-1, host.GetNumContainedItems(0x50000001));
        Assert.False(host.CloseContainer(0x80000300));
        Assert.Contains(_log, line => line.Contains("GetNumContainedItems threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("CloseContainer threw", StringComparison.Ordinal));
    }

    [Fact]
    public void DeferredSlotsStayUnset()
    {
        using var table = new RynthApiTable(new FakeGameView(), _log.Add);
        Assert.Equal(IntPtr.Zero, table.Native->NativeAttackFn);
        Assert.Equal(IntPtr.Zero, table.Native->StopMovementFn);
        Assert.Equal(IntPtr.Zero, table.Native->DoMovementFn);
        Assert.Equal(IntPtr.Zero, table.Native->SetAutonomyLevelFn);
        Assert.Equal(IntPtr.Zero, table.Native->WorldToScreenFn);
        Assert.Equal(IntPtr.Zero, table.Native->GetViewportSizeFn);
        Assert.Equal(IntPtr.Zero, table.Native->GetRadarRectFn);
        Assert.Equal(IntPtr.Zero, table.Native->SetRadarSuppressedFn);
        Assert.Equal(IntPtr.Zero, table.Native->GetPluginInterfaceFn);
        Assert.Equal(IntPtr.Zero, table.Native->IsPlayerReadyFn);
        Assert.Equal(IntPtr.Zero, table.Native->GetScreenModeFn);
    }

    private static RynthObjectInfo ObjectWith(
        Dictionary<uint, bool> bools, Dictionary<uint, double> floats, Dictionary<uint, string> strings) => new()
    {
        ContainerId = 0, WielderId = 0, EquipLocation = 0, ItemType = 0, Wcid = 0, Bitfield = 0,
        IconId = 0, IconUnderlayId = 0, IconOverlayId = 0, SpellId = null, HouseOwnerId = null,
        StackSize = 0, StackSizeMax = 0, Value = 0, Burden = 0, ItemsCapacity = 0, Structure = 0,
        MaxStructure = 0, Workmanship = 0, MaterialType = null, Identified = false,
        AppraisedSpellIds = [], Ints = new Dictionary<uint, int>(), DataIds = new Dictionary<uint, uint>(),
        InstanceIds = new Dictionary<uint, uint>(),
        Bools = bools, Floats = floats, Strings = strings,
    };

    [Fact]
    public void ActionSlots_CallTheGameView()
    {
        var view = new FakeGameView();
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.SelectItem(0x80000010));
        Assert.True(host.UseObject(0x80000010));
        Assert.True(host.RequestId(0x80000011));
        Assert.True(host.QueryHealth(0x80000012));
        Assert.True(host.ChangeCombatMode(8));
        Assert.True(host.MoveItemInternal(0x80000013, 0x50000001, 2, 5));
        Assert.True(host.SplitStackInternal(0x80000014, 0x80000020, 0, 3));
        Assert.True(host.MergeStackInternal(0x80000015, 0x80000016));
        host.ForceResetBusyCount();
        Assert.True(host.WieldItem(0x80000017, 0x00200000));

        Assert.Equal(
            new[]
            {
                "select 80000010", "use 80000010", "id 80000011", "health 80000012", "mode 8",
                "move 80000013 50000001 2 5", "move 80000014 80000020 0 3", "merge 80000015 80000016",
                "reset busy", "wield 80000017 00200000",
            },
            view.Actions);

        view.ActionAnswer = false;
        Assert.False(host.UseObject(0x80000010));
    }

    [Fact]
    public void ActionSlots_RefuseBadArgumentsWithoutCallingTheView()
    {
        var view = new FakeGameView();
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.False(host.ChangeCombatMode(3));
        Assert.False(host.MoveItemInternal(0x80000013, 0x50000001, 0, 0));
        Assert.False(host.SplitStackInternal(0x80000013, 0x50000001, 0, -1));
        Assert.Empty(view.Actions);
    }

    [Fact]
    public void ActionSlots_ReturnFalseWhenTheViewThrows()
    {
        var view = new FakeGameView { Throw = true };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.False(host.UseObject(1));
        Assert.False(host.SelectItem(1));
        Assert.False(host.WieldItem(1, 1));
        host.ForceResetBusyCount();
        Assert.Contains(_log, line => line.Contains("UseObject threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("ForceResetBusyCount threw", StringComparison.Ordinal));
    }

    [Fact]
    public void CombatSlots_CallTheGameView()
    {
        const uint player = 0x50000001;
        var view = new FakeGameView
        {
            PlayerId = player,
            CastGesture = true,
            Enchantments = [new RynthEnchantment(2, 1_300.0), new RynthEnchantment(3, double.MaxValue)],
        };
        view.TargetVitals[0x80000010] = new RynthVitals(75, 100, 40, 80, 0, 0);
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.HasGetCastBusyState);
        Assert.True(host.HasMeleeAttack);
        Assert.True(host.HasMissileAttack);
        Assert.True(host.HasCancelAttack);
        Assert.True(host.HasUseObjectOn);
        Assert.True(host.HasReadObjectEnchantments);
        Assert.NotEqual(IntPtr.Zero, table.Native->GetTargetVitalsFn);

        Assert.Equal(1, host.GetCastBusyState());
        Assert.False(host.CanCastNow);
        Assert.True(host.CastSpell(0x80000010, 1234));
        Assert.True(host.CastSpell(player, 2));
        Assert.True(host.MeleeAttack(0x80000010, 2, 0.75f));
        Assert.True(host.MissileAttack(0x80000010, 1, 1.5f));   // clamped to 1
        Assert.True(host.CancelAttack());
        Assert.True(host.UseObjectOn(0x80000020, player));
        Assert.Equal(
            new[]
            {
                "cast 80000010 1234", "cast 50000001 2", "melee 80000010 2 0.75", "missile 80000010 1 1.00",
                "cancel", "useon 80000020 50000001",
            },
            view.Actions);

        Assert.True(host.TryGetTargetVitals(0x80000010, out uint hp, out uint maxHp, out uint st, out uint maxSt,
            out uint mp, out uint maxMp));
        Assert.Equal((75u, 100u, 40u, 80u, 0u, 0u), (hp, maxHp, st, maxSt, mp, maxMp));
        Assert.False(host.TryGetTargetVitals(0x80000011, out _, out _, out _, out _, out _, out _));

        var spellIds = new uint[4];
        var expiries = new double[4];
        Assert.Equal(2, host.ReadObjectEnchantments(player, spellIds, expiries, 4));
        Assert.Equal((2u, 1_300.0, 3u, double.MaxValue), (spellIds[0], expiries[0], spellIds[1], expiries[1]));
        Assert.Equal(1, host.ReadObjectEnchantments(player, spellIds, expiries, 1));
        Assert.Equal(-1, host.ReadObjectEnchantments(0x80000010, spellIds, expiries, 4));

        view.CastGesture = false;
        Assert.True(host.CanCastNow);
        view.ActionAnswer = false;
        Assert.False(host.CastSpell(0x80000010, 1234));
    }

    [Fact]
    public void CastSpell_AtTargetZeroCastsOnThePlayer()
    {
        var view = new FakeGameView { PlayerId = 0x50000001 };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.CastSpell(0, 7));

        Assert.Equal(new[] { "cast 50000001 7" }, view.Actions);
    }

    [Fact]
    public void CombatSlots_RefuseBadArgumentsWithoutCallingTheView()
    {
        var view = new FakeGameView();
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.False(host.CastSpell(0x80000010, 0));
        Assert.False(host.CastSpell(0x80000010, -5));
        Assert.False(host.MeleeAttack(0x80000010, 0, 0.5f));
        Assert.False(host.MissileAttack(0x80000010, 4, 0.5f));
        Assert.False(host.MeleeAttack(0, 2, 0.5f));
        Assert.False(host.UseObjectOn(0, 0x80000010));
        Assert.False(host.UseObjectOn(0x80000010, 0));
        Assert.Empty(view.Actions);
    }

    [Fact]
    public void CombatSlots_ReturnNotAvailableWhenTheViewThrows()
    {
        var view = new FakeGameView { Throw = true };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.Equal(0, host.GetCastBusyState());
        Assert.False(host.CastSpell(1, 1));
        Assert.False(host.MeleeAttack(1, 2, 0.5f));
        Assert.False(host.MissileAttack(1, 2, 0.5f));
        Assert.False(host.CancelAttack());
        Assert.False(host.TryGetTargetVitals(1, out _, out _, out _, out _, out _, out _));
        Assert.False(host.UseObjectOn(1, 2));
        Assert.Equal(-1, host.ReadObjectEnchantments(1, new uint[2], new double[2], 2));
        Assert.Contains(_log, line => line.Contains("CastSpell threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("GetCastBusyState threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("GetTargetVitals threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("ReadObjectEnchantments threw", StringComparison.Ordinal));
    }

    [Fact]
    public void Nav3DSlotsFillTheBuffer()
    {
        var view = new FakeGameView { Pose = (0xA9B40001, new Vector3(50, 60, 10), Quaternion.Identity) };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);
        Assert.True(host.HasNav3D && host.HasNav3DTriangle && host.HasNav3DRingHeight);

        table.Nav3D.BeginTick();
        host.Nav3DAddRing(1, 2, 3, 1, 0.1f, 0xFFFF0000);
        host.Nav3DAddRingEx(1, 2, 3, 1, 0.1f, 2f, 0xFFFF0000);
        host.Nav3DAddLine(0, 0, 0, 5, 0, 0, 0.1f, 0xFFFF0000);
        host.Nav3DAddTriangle(0, 0, 0, 1, 0, 0, 0, 0, 1, 0xFFFF0000);
        Assert.Equal((2, 1, 1), table.Nav3D.Counts);
        host.Nav3DClear();
        Assert.Equal((0, 0, 0), table.Nav3D.Counts);
    }

    [Fact]
    public void TradeSlots_ReportTheWindowAndCounters()
    {
        var view = new FakeGameView
        {
            PlayerId = 0x50000001,
            TradeState = new RynthTradeState(
                IsOpen: true, YouAccepted: false, PartnerAccepted: true, PartnerId: 0x50000002,
                YourItems: [0x80000101], PartnerItems: [0x80000201, 0x80000202],
                Generation: 3, Sequence: 17, InitiatorId: 0x50000002, LastEventType: 0x0202,
                FailureCount: 1, LastFailureItemId: 0x80000103, LastFailureReason: 0x0426, LastCloseReason: 1,
                LastAcceptedBy: 0x50000002, LastDeclinedBy: 0, LastResetBy: 0x50000001,
                CompletedCount: 2, PartnerAcceptCount: 4),
        };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.HasTrade);
        Assert.True(host.TryGetTradeState(out TradeState state));
        Assert.True(state.IsOpen);
        Assert.False(state.YouAccepted);
        Assert.True(state.PartnerAccepted);
        Assert.True(state.Watching);
        Assert.True(state.ActionsAvailable);
        Assert.Equal((3u, 17u, 0x50000002u, 0x50000002u), (state.Generation, state.Sequence, state.PartnerId, state.InitiatorId));
        Assert.Equal((1, 2), (state.YourItemCount, state.PartnerItemCount));
        Assert.Equal((0x0202u, 1u, 0x80000103u, 0x0426u, 1u), (state.LastEventType, state.FailureCount, state.LastFailureItemId, state.LastFailureReason, state.LastCloseReason));
        Assert.Equal((0x50000002u, 0u, 0x50000001u, 2u, 4u), (state.LastAcceptedBy, state.LastDeclinedBy, state.LastResetBy, state.CompletedCount, state.PartnerAcceptCount));
        Assert.Equal(new uint[] { 0x80000101 }, host.GetTradeItems(TradeSide.You));
        Assert.Equal(new uint[] { 0x80000201, 0x80000202 }, host.GetTradeItems(TradeSide.Partner));

        var raw = new TradeStateNative { Size = 8 };
        Assert.Equal(8, ((delegate* unmanaged[Cdecl]<TradeStateNative*, int>)table.Native->GetTradeStateFn)(&raw));
        Assert.Equal(8u, raw.Size);
        Assert.Equal(0u, raw.Generation);                    // past the 8 bytes asked for
        raw = new TradeStateNative { Size = 4 };
        Assert.Equal(0, ((delegate* unmanaged[Cdecl]<TradeStateNative*, int>)table.Native->GetTradeStateFn)(&raw));
        Assert.Equal(-1, ((delegate* unmanaged[Cdecl]<int, uint*, int, int>)table.Native->GetTradeItemsFn)(3, null, 0));

        view.TradeState = null;
        Assert.False(host.TryGetTradeState(out _));
    }

    [Fact]
    public void TradeSlots_ActionsReachTheView()
    {
        var view = new FakeGameView { PlayerId = 0x50000001 };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.TradeOpen(0x50000002));
        Assert.False(host.TradeOpen(0));
        Assert.True(host.TradeAdd(0x80000101, 4));
        Assert.False(host.TradeAdd(0));
        Assert.True(host.TradeAccept());
        Assert.True(host.TradeDecline());
        Assert.True(host.TradeReset());
        Assert.True(host.TradeClose());
        Assert.Equal(
            new[] { "trade open 50000002", "trade add 80000101", "trade accept", "trade decline", "trade reset", "trade close" },
            view.Actions);
    }
}
