using System.Globalization;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    private enum Pane { None, Form, Presets, Test }

    private Pane _pane;
    private ChatDeckRule? _draft;
    private string? _editingId;
    private ChatDeckLineFacts? _fromLine;
    private bool _anySender, _anyNumber;
    private object? _previewFor;
    private ChatDeckPreviewResult? _preview;

    private void CreateRuleForm()
    {
        DraftChannels = new ChatDeckChannelPicker(
            () => _draft?.Channels ?? ChatDeckRule.AnyChannel,
            set => { if (_draft is not null) _draft = _draft with { Channels = set }; });
    }

    private void ResetPanes()
    {
        _pane = Pane.None;
        _draft = null;
        _editingId = null;
        _fromLine = null;
        _anySender = _anyNumber = false;
        _previewFor = null;
        ResetTest();               // ChatDeckEditorBinding.Test.cs
    }

    private void StartForm(ChatDeckRule rule, string? editingId, ChatDeckLineFacts? fromLine)
    {
        _ = M;                     // bind the model first: its first access resets the panes
        _page = ChatDeckEditorPage.Rules;
        _pane = Pane.Form;
        _draft = rule;
        _editingId = editingId;
        _fromLine = fromLine;
        _anySender = _anyNumber = false;
    }

    public bool FormOpen => _pane == Pane.Form && _draft is not null;
    public string FormTitle => _editingId is null ? "New rule" : "Edit rule";
    public string SaveText => _editingId is null ? "Add" : "Save";

    public Action NewRule => () => StartForm(
        new ChatDeckRule { Id = "", Kind = ChatDeckMatchKind.Contains, Action = ChatDeckRuleAction.Hide },
        editingId: null, fromLine: null);

    private void SetDraft(Func<ChatDeckRule, ChatDeckRule> change) { if (_draft is not null) _draft = change(_draft); }

    public bool IsPresetDraft => _draft?.Kind == ChatDeckMatchKind.Preset;
    public bool KindEditable => FormOpen && !IsPresetDraft;
    public string PresetLabel => IsPresetDraft && _draft is not null ? ChatDeckRuleSummary.When(_draft) : "";
    /// <summary>0 Contains, 1 Regex, 2 My name.</summary>
    public int DraftKind => _draft?.Kind switch
    {
        ChatDeckMatchKind.Regex => 1,
        ChatDeckMatchKind.MyName => 2,
        _ => 0,
    };
    public Action<int> SetDraftKind => v => SetDraft(r => r with
    {
        Kind = v switch { 1 => ChatDeckMatchKind.Regex, 2 => ChatDeckMatchKind.MyName, _ => ChatDeckMatchKind.Contains },
    });

    public bool PatternEditable => _draft?.Kind is ChatDeckMatchKind.Contains or ChatDeckMatchKind.Regex;
    public string Pattern => _draft?.Pattern ?? "";
    public Action<string> SetPattern => v => SetDraft(r => r with { Pattern = v });
    public int DraftField => (int)(_draft?.Field ?? ChatDeckMatchField.Text);
    public Action<int> SetDraftField => v => SetDraft(r => r with { Field = (ChatDeckMatchField)Math.Clamp(v, 0, 2) });

    public ChatDeckChannelPicker DraftChannels { get; private set; } = null!;
    public IReadOnlyList<ChatDeckChannelPicker.GroupRow> DraftChannelGroups => DraftChannels.Groups;
    public bool ChannelsInForm => FormOpen && !IsPresetDraft;

    /// <summary>The action, in <see cref="ChatDeckRuleAction"/> order: Hide, Move to, Also show in, Colour line, Colour match.</summary>
    public int DraftAction => (int)(_draft?.Action ?? ChatDeckRuleAction.Hide);
    public Action<int> SetDraftAction => v => SetDraft(r => r with
    {
        Action = (ChatDeckRuleAction)Math.Clamp(v, 0, 4),
        Colour = r.Colour == 0 ? ChatDeckRuleDraft.DefaultColour : r.Colour,
    });
    public bool ShowsTarget => _draft?.Action is ChatDeckRuleAction.MoveTo or ChatDeckRuleAction.CopyTo;
    public bool ShowsColour => _draft?.Action is ChatDeckRuleAction.ColourLine or ChatDeckRuleAction.ColourMatch;

    public IReadOnlyList<string> TargetNames => S.Tabs.Where(t => t.Id != ChatDeckDefaults.AllTabId).Select(t => t.Name).ToList();
    public string TargetName => S.Tabs.FirstOrDefault(t => t.Id == _draft?.TargetTabId)?.Name ?? "";
    public Action<string> SetTarget => v =>
    {
        if (S.Tabs.FirstOrDefault(t => t.Id != ChatDeckDefaults.AllTabId && t.Name == v) is { } tab)
            SetDraft(r => r with { TargetTabId = tab.Id });
    };

    public string DraftColour => $"#{_draft?.Colour ?? ChatDeckRuleDraft.DefaultColour:X8}";
    public Action<string> SetDraftColour => v =>
    {
        if (uint.TryParse(v.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            SetDraft(r => r with { Colour = argb });
    };

    public bool DraftAlert => _draft?.Alert ?? false;
    public Action<bool> SetDraftAlert => v => SetDraft(r => r with { Alert = v });

    // ── rule-from-line ──
    public bool FromLine => FormOpen && _fromLine is not null;
    public bool CanAnySender => _fromLine is { } f && ChatDeckRuleDraft.CanGeneraliseSender(f);
    public bool CanAnyNumber => _fromLine is { } f && ChatDeckRuleDraft.CanGeneraliseNumber(f);
    public string AnySenderText => $"Any name in place of \"{_fromLine?.Sender.Trim()}\"";
    public bool AnySender => _anySender;
    public bool AnyNumber => _anyNumber;
    public Action<bool> SetAnySender => v => { _anySender = v; Regeneralise(); };
    public Action<bool> SetAnyNumber => v => { _anyNumber = v; Regeneralise(); };
    public string PatternPreview => _fromLine is { } f ? ChatDeckRuleDraft.Preview(f, _anySender, _anyNumber) : "";

    private void Regeneralise()
    {
        if (_fromLine is not { } f) return;
        (ChatDeckMatchKind kind, string pattern) = ChatDeckRuleDraft.Pattern(f, _anySender, _anyNumber);
        SetDraft(r => r with { Kind = kind, Pattern = pattern });
    }

    // ── preview ──
    private ChatDeckPreviewResult? Preview
    {
        get
        {
            if (!FormOpen || M is not { } m || m.FindTab(ChatDeckDefaults.AllTabId) is not { } all) return null;
            var key = (_draft, all, all.Version);
            if (Equals(key, _previewFor)) return _preview;
            _previewFor = key;
            _preview = ChatDeckRulePreview.Run(_draft!, all.Snapshot(), m.CharacterName);
            return _preview;
        }
    }

    public string PreviewText => Preview switch
    {
        null => "",
        { Status: not ChatDeckRuleStatus.Ok } p => p.Error is { } e ? $"Invalid: {e}" : ChatDeckRuleSummary.Status(ChatDeckMatcher.Compile(_draft!, M?.CharacterName ?? "")),
        { TimedOut: true } p => $"Too slow: it timed out on a line after {p.Count} matches. It would be turned off.",
        { MatchesEverything: true } => "This pattern matches every line.",
        var p => $"Matches {p.Count} of the last {p.Total} lines.",
    };
    public uint PreviewColour => Preview is { Status: ChatDeckRuleStatus.Ok, TimedOut: false, MatchesEverything: false }
        ? 0xFF000000u | InkColour : 0xFF000000u | ProblemColour;

    public sealed record SampleRow(string Text);
    public IReadOnlyList<SampleRow> PreviewSamples => Preview?.Samples.Select(s => new SampleRow(s)).ToList() ?? [];

    // ── save ──
    public bool CanSave => _draft is { } d && Preview is { Status: ChatDeckRuleStatus.Ok }
        && (!ChatDeckSettingsEdits.Targets(d) || S.Tabs.Any(t => t.Id == d.TargetTabId && t.Id != ChatDeckDefaults.AllTabId));

    public Action SaveRule => () =>
    {
        if (!CanSave || _draft is not { } draft) return;
        if (_editingId is { } id)
        {
            Edit(s => ChatDeckSettingsEdits.UpdateRule(s, id, draft));
            _ruleId = id;
        }
        else
        {
            string? added = null;
            if (Edit(s => ChatDeckSettingsEdits.AddRule(s, draft with { Enabled = true }, out added))) _ruleId = added;
        }
        CloseForm();
    };

    public Action CancelRule => CloseForm;

    private void CloseForm()
    {
        _pane = Pane.None;
        _draft = null;
        _editingId = null;
        _fromLine = null;
        _previewFor = null;
    }

    private const string RuleFormMarkup = """
            <group layout="column" gap="5" h="270" visible="{FormOpen}">
              <separator h="1" />
              <label text="{FormTitle}" h="16" />
              <label text="{PresetLabel}" visible="{IsPresetDraft}" h="16" />
              <group layout="row" gap="8" align="center" h="22" visible="{KindEditable}">
                <radio items="Contains|Regex|My name" selected="{DraftKind}" onchange="{SetDraftKind}" orientation="horizontal" style="segmented" w="240" h="20" />
                <radio items="Text|Sender|Whole line" selected="{DraftField}" onchange="{SetDraftField}" orientation="horizontal" style="segmented" grow="1" h="20" />
              </group>
              <field text="{Pattern}" onchange="{SetPattern}" maxlength="256" h="20" visible="{PatternEditable}" />
              <group layout="row" gap="12" h="16" visible="{FromLine}">
                <checkbox text="{AnySenderText}" checked="{AnySender}" onchange="{SetAnySender}" enabled="{CanAnySender}" grow="1" h="16" />
                <checkbox text="Any number" checked="{AnyNumber}" onchange="{SetAnyNumber}" enabled="{CanAnyNumber}" w="120" h="16" />
              </group>
              <label text="{PatternPreview}" visible="{FromLine}" h="16" />
              <group layout="row" gap="8" h="96">
                <group layout="column" gap="2" scroll="y" w="200" visible="{ChannelsInForm}">
                  <label text="Channels (none = any)" h="16" />
                  <repeat items="{DraftChannelGroups}" key="{Name}">
                    <checkbox text="{Name}" checked="{All}" onchange="{SetAll}" h="16" />
                    <repeat items="{Channels}" key="{Id}">
                      <group layout="row" h="16">
                        <group w="18" />
                        <checkbox text="{Name}" checked="{On}" onchange="{SetOn}" grow="1" h="16" />
                      </group>
                    </repeat>
                  </repeat>
                </group>
                <group layout="column" gap="5" grow="1">
                  <radio items="Hide|Move to|Also show in|Colour line|Colour match" selected="{DraftAction}" onchange="{SetDraftAction}" orientation="horizontal" style="segmented" h="20" />
                  <group layout="row" gap="8" align="center" h="22" visible="{ShowsTarget}">
                    <label text="Tab" w="40" />
                    <menu items="{TargetNames}" selected="{TargetName}" onchange="{SetTarget}" rows="15" grow="1" h="20" />
                  </group>
                  <group layout="row" gap="8" align="center" h="22" visible="{ShowsColour}">
                    <label text="Colour" w="40" />
                    <color value="{DraftColour}" onchange="{SetDraftColour}" w="40" h="16" />
                  </group>
                  <checkbox text="Alert (red badge, peek, beep)" checked="{DraftAlert}" onchange="{SetDraftAlert}" h="16" />
                </group>
              </group>
              <label text="{PreviewText}" color="{PreviewColour}" h="16" />
              <group layout="column" gap="1" h="50">
                <repeat items="{PreviewSamples}">
                  <label text="{Text}" h="16" />
                </repeat>
              </group>
              <group layout="row" gap="8" h="22">
                <group grow="1" />
                <button text="{SaveText}" onclick="{SaveRule}" enabled="{CanSave}" w="64" h="22" />
                <button text="Cancel" onclick="{CancelRule}" w="64" h="22" />
              </group>
            </group>
        """;
}
