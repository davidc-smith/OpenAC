using AcDream.Rynth.Host;
using AcDream.TestSupport;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthInventoryTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-inventory-");
    private string _config => _sandbox.Root;
    private readonly FakeGameView _view = new()
    {
        PlayerId = 0x50000000u | (uint)Random.Shared.Next(1, 0xFFFF),
    };

    public void Dispose() => _sandbox.Dispose();

    private RynthHost CreateInventoryHost() =>
        new(_view, _log.Add, [typeof(RynthCore.Plugin.RynthInventory.PluginExports)]);

    [Fact]
    public void InventoryStoreLivesUnderTheSandbox()
    {
        using RynthHost host = CreateInventoryHost();

        string line = Assert.Single(_log, l => l.Contains("[RynthInventory]", StringComparison.Ordinal)
            && l.Contains("Files:", StringComparison.Ordinal));
        string files = line[(line.IndexOf("Files:", StringComparison.Ordinal) + "Files:".Length)..].Trim();
        string root = Path.GetFullPath(_config).TrimEnd(Path.DirectorySeparatorChar);
        // The temp dir may be reached through a symlink (/var -> /private/var on macOS).
        string realRoot = Directory.ResolveLinkTarget(root, returnFinalTarget: true)?.FullName ?? root;
        Assert.True(
            files.StartsWith(root, StringComparison.Ordinal) || files.StartsWith(realRoot, StringComparison.Ordinal),
            $"Files path '{files}' is outside the sandbox '{root}'");
    }

    [Fact]
    public void InventoryLoads()
    {
        using RynthHost host = CreateInventoryHost();
        Assert.True(Assert.Single(host.Plugins).IsActive);
    }

    [Fact]
    public void SlashGinvHelpPrintsTheCommands()
    {
        using RynthHost host = CreateInventoryHost();

        Assert.True(host.Plugins[0].OnChatBarEnter("/ginv help"));
        host.Tick();

        Assert.Contains(_view.ChatLines, line => line.Text.Contains("/ginv (window)", StringComparison.Ordinal));
    }

    private static RynthObjectInfo Item(uint container, uint wielder, uint type, int capacity = 0) => new()
    {
        ContainerId = container,
        WielderId = wielder,
        EquipLocation = wielder != 0 ? 0x100u : 0u,
        ItemType = type,
        Wcid = 1,
        Bitfield = 0,
        IconId = 0x06001234,
        IconUnderlayId = 0,
        IconOverlayId = 0,
        SpellId = null,
        HouseOwnerId = null,
        StackSize = 1,
        StackSizeMax = 1,
        Value = 10,
        Burden = 5,
        ItemsCapacity = capacity,
        Structure = 0,
        MaxStructure = 0,
        Workmanship = 0,
        MaterialType = null,
        Identified = false,
        AppraisedSpellIds = [],
        Ints = new Dictionary<uint, int>(),
        DataIds = new Dictionary<uint, uint>(),
        InstanceIds = new Dictionary<uint, uint>(),
    };

    [Fact]
    public void ScansTheCharactersCarriedItems()
    {
        const uint pack = 0x80000020, sword = 0x80000010;
        _view.AccountName = "acct";
        _view.WorldName = "Local ACE";
        _view.Names[_view.PlayerId] = "Tester";
        _view.Names[pack] = "Pack";
        _view.Names[sword] = "Sword";
        _view.Objects[_view.PlayerId] = Item(0, 0, 0x10);
        _view.Objects[pack] = Item(_view.PlayerId, 0, 0x200, capacity: 24);
        _view.Objects[sword] = Item(0, _view.PlayerId, 0x1);
        _view.Contents[_view.PlayerId] = [sword, pack];
        using RynthHost host = CreateInventoryHost();

        host.Bridge.OnLifecycle(new AcDream.Runtime.RuntimeLifecycleDelta(
            default, AcDream.Runtime.RuntimeLifecycleState.Starting, AcDream.Runtime.RuntimeLifecycleState.InWorld));
        host.Tick();
        Assert.True(host.Plugins[0].OnChatBarEnter("/ginv scan"));
        host.Tick();
        Assert.True(host.Plugins[0].OnChatBarEnter("/ginv status"));
        host.Tick();

        Assert.Contains(_view.ChatLines, line =>
            line.Text.Contains("Tester on Local ACE (account acct)", StringComparison.Ordinal)
            && line.Text.Contains("2 items carried", StringComparison.Ordinal));
    }
}
