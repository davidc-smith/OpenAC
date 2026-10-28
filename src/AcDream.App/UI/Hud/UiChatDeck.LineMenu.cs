using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck
{
    private readonly UiMarkupContextMenu _lineMenu = new() { ThemePalette = ChatDeckPalette.Menu };
    private IReadOnlyList<ChatDeckLine> _menuLines = [];
    private ChatDeckLine? _menuLine;   // the line the rule is written from

    internal UiMarkupContextMenu LineMenu => _lineMenu;

    private void CreateLineMenu()
    {
        _lineMenu.Opening = (x, y) => PickMenuLines(x, y);
        _lineMenu.ItemsSource = () => _menuLines.Count == 0 ? [] :
            [_menuLines.Count > 1 ? "Copy lines" : "Copy line", "-",
             "Move these to…", "Also show in…", "Hide these", "Colour these…", "-", "Test this line"];
        _lineMenu.ItemsEnabledSource = () => [true, true, true, true, true, true, true, _menuLines.Count == 1];
        _lineMenu.Selected = ChooseLineMenuItem;
        Transcript.AddChild(_lineMenu);
        Transcript.ContextMenu = _lineMenu;
    }

    /// <summary>The line menu's items for a right-click at a transcript-local point (empty off the lines).</summary>
    internal IReadOnlyList<string> LineMenuItemsAt(float x, float y)
    {
        PickMenuLines(x, y);
        return _lineMenu.ItemsSource();
    }

    /// <summary>The selected lines when the click is inside the selection, else the line under the click.</summary>
    private void PickMenuLines(float x, float y)
    {
        _menuLines = [];
        _menuLine = null;
        if (_layout.SourceOf.Count == 0) return;
        UiText.Pos at = Transcript.HitCharAt(x, y);
        if (at.Line < 0 || at.Line >= _layout.SourceOf.Count) return;
        ChatDeckLine clicked = _layout.Sources[_layout.SourceOf[at.Line]];

        if (Transcript.TryGetSelection(out UiText.Pos start, out UiText.Pos end) && at.Line >= start.Line && at.Line <= end.Line)
        {
            var lines = new List<ChatDeckLine>();
            for (int i = start.Line; i <= end.Line && i < _layout.SourceOf.Count; i++)
            {
                ChatDeckLine line = _layout.Sources[_layout.SourceOf[i]];
                if (lines.Count == 0 || !ReferenceEquals(lines[^1], line)) lines.Add(line);
            }
            _menuLines = lines;
        }
        else
        {
            _menuLines = [clicked];
        }
        _menuLine = clicked;
    }

    internal void ChooseLineMenuItem(int index)
    {
        if (_menuLine is not { } line || _model is null) return;
        switch (index)
        {
            case 0:
                if (_b.Keyboard is { } keyboard) keyboard.ClipboardText = string.Join("\n", _menuLines.Select(ShownText));
                break;
            case 2: OpenRuleFromLines(ChatDeckRuleAction.MoveTo); break;
            case 3: OpenRuleFromLines(ChatDeckRuleAction.CopyTo); break;
            case 4: OpenRuleFromLines(ChatDeckRuleAction.Hide); break;
            case 5: OpenRuleFromLines(ChatDeckRuleAction.ColourLine); break;
            case 7:
                if (_menuLines.Count == 1)
                    _b.OpenEditor?.Invoke(new ChatDeckEditorRequest { Page = ChatDeckEditorPage.Rules, TestLine = line.Facts });
                break;
        }
    }

    private void OpenRuleFromLines(ChatDeckRuleAction action)
    {
        if (_menuLine is not { } line || _model is null) return;
        ChatDeckRule rule = ChatDeckRuleDraft.FromLine(line.Facts, action) with
        {
            Channels = _menuLines.Select(l => l.Channel).ToHashSet(),
        };
        if (ChatDeckSettingsEdits.Targets(rule))
            rule = rule with
            {
                TargetTabId = _model.Settings.Tabs.FirstOrDefault(t => t.Id != ChatDeckDefaults.AllTabId)?.Id ?? "",
            };
        _b.OpenEditor?.Invoke(new ChatDeckEditorRequest
        {
            Page = ChatDeckEditorPage.Rules, NewRule = rule, FromLine = line.Facts,
        });
    }
}
