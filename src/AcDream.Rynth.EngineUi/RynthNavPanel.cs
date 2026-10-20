using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// Runs RynthCore's RynthNav panel: Start does what RynthCore's EntryPoint does when
/// RynthNav is installed (register the panel, which registers the arrow overlay and
/// subscribes the arrow watch), joins the engine hub with the panel's sources, and makes
/// sure the Atlas is installed. Stop leaves the hub. Game thread.
/// </summary>
internal static class RynthNavPanel
{
    /// <summary>Their panel's title (the bar button and the layout key).</summary>
    internal const string Title = RynthNavFace.Title;

    private static readonly RynthEngineHub.Part HubPart = new("RynthNav",
        [UiSources.RynthNav, UiSources.RynthNavAtlas, UiSources.RynthNavArrowWatch]);

    internal static void Start()
    {
        NavAtlasInstall.EnsureOnce(RynthEngineHub.Log);
        // The panel host can be reset (tests, an extension restart): register again then.
        if (!ImGuiPanelHost.HasFace(RynthNavFace.Title))
            RynthNavFace.Register();
        RynthEngineHub.Join(HubPart);
    }

    internal static void Stop() => RynthEngineHub.Leave(HubPart.Name);
}
