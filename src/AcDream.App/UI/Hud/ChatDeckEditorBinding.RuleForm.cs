using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    private void CreateRuleForm() { }
    private void ResetPanes() { }
    private void StartForm(ChatDeckRule rule, string? editingId, ChatDeckLineFacts? fromLine) { _page = ChatDeckEditorPage.Rules; }
    private void StartTest(ChatDeckLineFacts line) { _page = ChatDeckEditorPage.Rules; }
    public Action NewRule => () => { };
    public Action ShowPresets => () => { };
    public Action ShowTest => () => { };
    private const string RuleFormMarkup = "";
    private const string PresetsMarkup = "";
    private const string TestMarkup = "";
}
