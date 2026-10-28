namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    public Action ShowPresets => () => { _pane = Pane.Presets; _page = ChatDeckEditorPage.Rules; };
    private const string PresetsMarkup = "";
}
