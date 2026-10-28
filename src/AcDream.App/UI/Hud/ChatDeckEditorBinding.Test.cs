using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    public Action ShowTest => () => { _pane = Pane.Test; _page = ChatDeckEditorPage.Rules; };
    private void StartTest(ChatDeckLineFacts line) => ShowTest();
    private void ResetTest() { }
    private const string TestMarkup = "";
}
