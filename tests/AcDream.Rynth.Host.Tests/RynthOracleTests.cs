using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthOracleTests
{
    private readonly List<string> _log = new();
    private readonly FakeGameView _view = new()
    {
        PlayerId = 0x50000000u | (uint)Random.Shared.Next(1, 0xFFFF),
    };

    private RynthHost CreateOracleHost() =>
        new(_view, _log.Add, [typeof(RynthCore.Plugin.RynthOracle.PluginExports)]);

    [Fact]
    public void OracleLoads()
    {
        using RynthHost host = CreateOracleHost();
        Assert.True(Assert.Single(host.Plugins).IsActive);
    }

    [Fact]
    public void WithoutScriptWindowsSlashRoSaysSoInChat()
    {
        using RynthHost host = CreateOracleHost();

        Assert.True(host.Plugins[0].OnChatBarEnter("/ro"));
        host.Tick();

        Assert.Contains(_view.ChatLines, line =>
            line.Text.StartsWith("[RynthOracle] This RynthCore engine can't show plugin windows", StringComparison.Ordinal));
    }

    [Fact]
    public void SlashRoAboutPrintsTheAttribution()
    {
        using RynthHost host = CreateOracleHost();

        host.Plugins[0].OnChatBarEnter("/ro about");
        host.Tick();

        Assert.Contains(_view.ChatLines, line => line.Text.Contains("Based on Oracle of Dereth", StringComparison.Ordinal));
    }

    [Fact]
    public void ChatParserAndSelectionReachTheView()
    {
        _view.SelectedObjectId = 0x80000042;
        using var table = new RynthApiTable(_view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.InvokeChatParser("/myquests"));
        Assert.Equal("/myquests", Assert.Single(_view.SubmittedCommands));
        Assert.Equal(0x80000042u, host.GetSelectedItemId());
    }

    [Fact]
    public void DispatchIsSetOnlyInsideCalls()
    {
        using RynthHost host = CreateOracleHost();
        Assert.Null(RynthDispatch.Current);
        host.Tick();
        Assert.Null(RynthDispatch.Current);
    }
}
