using AcDream.Rynth.EngineUi;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.Plugins;
using RynthCore.Engine.UI;

namespace AcDream.Rynth.Host;

/// <summary>
/// Points the RynthAi dashboard's engine stand-ins at this host and game view
/// (the loaded-plugin list, the server-menu commands, chat writes, the pump's
/// log) and unpoints on Dispose, only the ones still pointing here. Plugin
/// exports are found through RynthChatHookup's binder, which serves every
/// plugin. Every call into the view is guarded: a failure logs and carries on.
/// </summary>
internal sealed class RynthAiHookup : IDisposable
{
    private readonly IRynthGameView _view;
    private readonly Action<string> _log;
    private readonly IReadOnlyList<LoadedPlugin> _plugins;
    private readonly Action<string> _runner;
    private readonly Action<string, int> _writeToChat;
    private readonly Action<string> _pumpLog;

    public RynthAiHookup(RynthHost host, IRynthGameView view, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        _view = view;
        _log = log;
        _plugins = host.Plugins.Select(plugin => plugin.Owner).ToArray();
        _runner = command => Guard(nameof(ServerMenus.Run), () => _view.SubmitChatCommand(command));
        _writeToChat = (text, chatType) => Guard(nameof(AcMainThreadQueue.EnqueueWriteToChat), () =>
        {
            _view.WriteChatLine(text, chatType);
            return true;
        });
        _pumpLog = log;

        PluginManager.Plugins = _plugins;
        ServerMenus.Runner = _runner;
        AcMainThreadQueue.ChatCommand = _runner;
        AcMainThreadQueue.WriteToChat = _writeToChat;
        RynthAiDashboard.Log = _pumpLog;
    }

    public void Dispose()
    {
        if (ReferenceEquals(PluginManager.Plugins, _plugins)) PluginManager.Plugins = Array.Empty<LoadedPlugin>();
        if (ServerMenus.Runner == _runner) ServerMenus.Runner = null;
        if (AcMainThreadQueue.ChatCommand == _runner) AcMainThreadQueue.ChatCommand = null;
        if (AcMainThreadQueue.WriteToChat == _writeToChat) AcMainThreadQueue.WriteToChat = null;
        if (RynthAiDashboard.Log == _pumpLog) RynthAiDashboard.Log = _ => { };
    }

    private void Guard(string what, Func<bool> call)
    {
        try { call(); }
        catch (Exception error)
        {
            _log($"[Rynth] RynthAi {what}: view failed: {error.GetType().Name}: {error.Message}");
        }
    }
}
