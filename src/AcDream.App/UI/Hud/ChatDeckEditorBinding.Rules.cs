using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    public const uint ProblemColour = 0xFF6B6Bu;   // 0xRRGGBB, as list colours are
    private static readonly uint InkColour = ChatDeckChannelColours.Argb(ChatDeckPalette.TextInk) & 0xFFFFFFu;

    private string? _ruleId;
    private ChatDeckSettings? _rowsSettings;
    private object? _rowsCompiled;
    private RuleRows _rows = new([], [], [], [], [], []);

    private sealed record RuleRows(
        IReadOnlyList<bool> On, IReadOnlyList<string> When, IReadOnlyList<string> From,
        IReadOnlyList<string> Do, IReadOnlyList<uint> Colours, IReadOnlyList<string> Status);

    private void CreateRules()
    {
        CreateRuleForm();          // ChatDeckEditorBinding.RuleForm.cs
    }

    private void ResetRules()
    {
        _ruleId = null;
        _rowsSettings = null;
        _rowsCompiled = null;
        ResetPanes();              // ChatDeckEditorBinding.RuleForm.cs
    }

    private void OpenRules(ChatDeckEditorRequest request)
    {
        if (request.NewRule is { } rule) StartForm(rule, editingId: null, request.FromLine);   // RuleForm.cs
        if (request.TestLine is { } line) StartTest(line);                                      // Test.cs
    }

    /// <summary>The table's columns, rebuilt only when the settings or the compiled rules change.</summary>
    private RuleRows Rows
    {
        get
        {
            ChatDeckModel? m = M;
            ChatDeckSettings s = S;
            IReadOnlyList<ChatDeckCompiledRule> compiled = m?.CompiledRules ?? [];
            if (ReferenceEquals(s, _rowsSettings) && ReferenceEquals(compiled, _rowsCompiled)) return _rows;
            _rowsSettings = s;
            _rowsCompiled = compiled;
            ChatDeckRuleStatus StatusAt(int i) => i < compiled.Count ? compiled[i].Status : ChatDeckRuleStatus.Ok;
            _rows = new RuleRows(
                s.Rules.Select(r => r.Enabled).ToList(),
                s.Rules.Select(ChatDeckRuleSummary.When).ToList(),
                s.Rules.Select(ChatDeckRuleSummary.From).ToList(),
                s.Rules.Select(r => ChatDeckRuleSummary.Do(r, s.Tabs)).ToList(),
                s.Rules.Select((_, i) => ChatDeckRuleSummary.IsProblem(StatusAt(i)) ? ProblemColour : InkColour).ToList(),
                s.Rules.Select((_, i) => i < compiled.Count ? ChatDeckRuleSummary.Status(compiled[i]) : "").ToList());
            return _rows;
        }
    }

    public IReadOnlyList<bool> RuleOn => Rows.On;
    public IReadOnlyList<string> RuleWhen => Rows.When;
    public IReadOnlyList<string> RuleFrom => Rows.From;
    public IReadOnlyList<string> RuleDo => Rows.Do;
    public IReadOnlyList<uint> RuleColours => Rows.Colours;

    public int SelectedRuleIndex => _ruleId is null ? -1 : S.Rules.ToList().FindIndex(r => r.Id == _ruleId);
    public Action<int> SelectRuleRow => i => _ruleId = i >= 0 && i < S.Rules.Count ? S.Rules[i].Id : null;
    public string SelectedRuleStatus => SelectedRuleIndex is var i and >= 0 && i < Rows.Status.Count ? Rows.Status[i] : "";
    public bool HasSelectedRule => SelectedRuleIndex >= 0;

    public Action<int> ToggleRule => i =>
    {
        if (i < 0 || i >= S.Rules.Count) return;
        ChatDeckRule r = S.Rules[i];
        Edit(s => ChatDeckSettingsEdits.SetRuleEnabled(s, r.Id, !r.Enabled));
    };

    public IReadOnlyList<string> RuleMenu { get; } = ["Edit", "Duplicate", "Delete", "-", "Move up", "Move down"];
    public Action<int> RuleMenuOpen => row => SelectRuleRow(row);
    public Action<int> RuleMenuSelect => index =>
    {
        if (_ruleId is not { } id) return;
        switch (index)
        {
            case 0: EditSelectedRule(); break;
            case 1: Edit(s => ChatDeckSettingsEdits.DuplicateRule(s, id)); break;
            case 2: Edit(s => ChatDeckSettingsEdits.DeleteRule(s, id)); _ruleId = null; break;
            case 4: Edit(s => ChatDeckSettingsEdits.MoveRule(s, id, -1)); break;
            case 5: Edit(s => ChatDeckSettingsEdits.MoveRule(s, id, 1)); break;
        }
    };

    public Action MoveRuleUp => () => { if (_ruleId is { } id) Edit(s => ChatDeckSettingsEdits.MoveRule(s, id, -1)); };
    public Action MoveRuleDown => () => { if (_ruleId is { } id) Edit(s => ChatDeckSettingsEdits.MoveRule(s, id, 1)); };
    public Action EditRule => EditSelectedRule;

    private void EditSelectedRule()
    {
        if (S.Rules.FirstOrDefault(r => r.Id == _ruleId) is { } rule) StartForm(rule, rule.Id, fromLine: null);
    }

    private const string RulesPageMarkup = """
          <group layout="column" gap="6" grow="1" visible="{OnRulesPage}">
            <list header="true" selected="{SelectedRuleIndex}" onchange="{SelectRuleRow}" rowheight="18" selectionband="true" grow="1">
              <column type="checkbox" header="On" width="30" values="{RuleOn}" onchange="{ToggleRule}" />
              <column type="text" header="When" width="*" items="{RuleWhen}" colors="{RuleColours}" />
              <column type="text" header="From" width="130" items="{RuleFrom}" colors="{RuleColours}" />
              <column type="text" header="Do" width="150" items="{RuleDo}" colors="{RuleColours}" />
              <contextmenu items="{RuleMenu}" onopen="{RuleMenuOpen}" onselect="{RuleMenuSelect}" />
            </list>
            <label text="{SelectedRuleStatus}" color="#FFFF6B6B" h="16" />
            <group layout="row" gap="6" h="22">
              <button text="+ Rule" onclick="{NewRule}" w="70" h="22" />
              <button text="Standard filters..." onclick="{ShowPresets}" w="130" h="22" />
              <button text="Test a line..." onclick="{ShowTest}" w="100" h="22" />
              <group grow="1" />
              <button text="Edit" onclick="{EditRule}" enabled="{HasSelectedRule}" w="44" h="22" />
              <button text="Up" onclick="{MoveRuleUp}" enabled="{HasSelectedRule}" w="40" h="22" />
              <button text="Down" onclick="{MoveRuleDown}" enabled="{HasSelectedRule}" w="44" h="22" />
            </group>
        """ + RuleFormMarkup + PresetsMarkup + TestMarkup + """
          </group>
        """;
}
