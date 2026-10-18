using System.Diagnostics;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.ChatWindow;

/// <summary>
/// Runs RynthCore's chat window: Start does what RynthCore's EntryPoint does when
/// RynthChat is installed, Tick polls the chat source the way UiDataHub.Step does
/// (only while a face subscribes, only when due), Stop is UiDataHub's reset after
/// plugins unload. Game thread.
/// </summary>
internal static class RynthChatWindow
{
    private static bool _started;

    /// <summary>Their chat panel's title (the bar button and the layout key).</summary>
    internal const string Title = ChatFace.Title;

    internal static void Start()
    {
        if (!_started)
        {
            _started = true;
            ChatModel.EnsureSettingsLoaded();
            ChatModel.InstallInput();
        }
        // The panel host can be reset (tests, an extension restart): register again then.
        if (!ImGuiPanelHost.HasFace(ChatFace.Title))
        {
            ChatFace.Register();
            ChatFiltersFace.Register();
        }
    }

    /// <summary>One hub step for the chat source. Returns true when it polled.</summary>
    internal static bool Tick()
    {
        if (!_started)
            return false;
        ChatSource source = UiSources.Chat;
        source.PromoteDeferredRefresh();
        long now = Stopwatch.GetTimestamp();
        if (!source.HasSubscribers || !source.IsDue(now))
            return false;
        source.Poll();
        source.MarkPolled(now, Stopwatch.GetTimestamp() - now);
        return true;
    }

    internal static void Stop()
    {
        Win32Backend.ChatCaptureActive = false;
        Win32Backend.TellRequested = false;
        UiSources.Chat.Reset();
    }
}
