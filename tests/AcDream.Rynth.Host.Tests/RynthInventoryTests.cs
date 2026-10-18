using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthInventoryTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly string _config = Directory.CreateTempSubdirectory("rynth-inventory-").FullName;
    private readonly string? _previousConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly FakeGameView _view = new()
    {
        PlayerId = 0x50000000u | (uint)Random.Shared.Next(1, 0xFFFF),
    };

    // RynthInventory saves under ApplicationData, which .NET reads from XDG_CONFIG_HOME here.
    public RynthInventoryTests() => Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _config);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _previousConfig);
        try { Directory.Delete(_config, recursive: true); }
        catch (IOException) { }
    }

    private RynthHost CreateInventoryHost() =>
        new(_view, _log.Add, [typeof(RynthCore.Plugin.RynthInventory.PluginExports)]);

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
}
