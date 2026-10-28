namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    private void CreateRules() { }
    private void ResetRules() { }
    private void OpenRules(ChatDeckEditorRequest request) { }

    private const string RulesPageMarkup = """
          <group layout="column" gap="6" grow="1" visible="{OnRulesPage}">
            <label text="Rules" h="16" />
          </group>
        """;
}
