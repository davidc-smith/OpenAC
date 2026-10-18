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
        Assert.False(host.HasCastSpell);
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
        Assert.Equal(2, host.ReadPlayerEnchantments(spellIds, expiries, 1));   // count exceeds max
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
        Assert.False(host.TryGetLastWeenieError(out _, out _, out _, out _));
        Assert.Contains(_log, line => line.Contains("GetCurrentCombatMode threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("ReadPlayerEnchantments threw", StringComparison.Ordinal));
    }

    [Fact]
    public void DeferredSlotsStayUnset()
    {
        using var table = new RynthApiTable(new FakeGameView(), _log.Add);
        Assert.Equal(IntPtr.Zero, table.Native->GetCastBusyStateFn);
        Assert.Equal(IntPtr.Zero, table.Native->GetTargetVitalsFn);
        Assert.Equal(IntPtr.Zero, table.Native->ReadObjectEnchantmentsFn);
        Assert.Equal(IntPtr.Zero, table.Native->StopMovementFn);
        Assert.Equal(IntPtr.Zero, table.Native->GetObjectStateFn);
        Assert.Equal(IntPtr.Zero, table.Native->GetObjectMotionOnFn);
        Assert.Equal(IntPtr.Zero, table.Native->WorldToScreenFn);
        Assert.Equal(IntPtr.Zero, table.Native->MoveItemExternalFn);
        Assert.Equal(IntPtr.Zero, table.Native->GetObjectPalettesFn);
        Assert.Equal(IntPtr.Zero, table.Native->GetNumContainedItemsFn);
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
}
