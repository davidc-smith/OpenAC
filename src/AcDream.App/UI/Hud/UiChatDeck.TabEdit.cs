using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck
{
    private string? _renaming;   // the tab id being renamed in place

    internal UiField RenameField { get; private set; } = null!;

    /// <summary>Called from the constructor, after the input row.</summary>
    private void CreateRenameField()
    {
        RenameField = new UiField
        {
            OneLine = true, Selectable = true, Keyboard = _b.Keyboard,
            BackgroundColor = ChatDeckPalette.FieldFill, TextColor = ChatDeckPalette.TextInk,
            ClearOnSubmit = false, RecordHistory = false, Visible = false,
            MaxCharacters = ChatDeckLimits.MaxTabNameLength,
        };
        RenameField.OnSubmit = text => FinishRename(text);
        RenameField.OnFocusLost = text => FinishRename(text);
        RenameField.OnEscape = () => FinishRename(null);
        AddChild(RenameField);
    }

    private void StartRename(int tabIndex)
    {
        if (_model is null || tabIndex < 0 || tabIndex >= _model.Settings.Tabs.Count) return;
        _renaming = _model.Settings.Tabs[tabIndex].Id;
        RenameField.SetText(_model.Settings.Tabs[tabIndex].Name);
        RenameField.Visible = true;
        LayoutRenameField();
        FindRoot()?.SetKeyboardFocus(RenameField);
        RenameField.SelectAllText();
    }

    /// <summary>Applies a non-blank name (null cancels) and hides the field; a second call does nothing.</summary>
    private void FinishRename(string? text)
    {
        if (_renaming is not { } id) return;
        _renaming = null;
        RenameField.Visible = false;
        if (FindRoot() is { } root && ReferenceEquals(root.KeyboardFocus, RenameField)) root.SetKeyboardFocus(null);
        if (text is { } name && name.Trim().Length > 0)
            _model?.Edit(s => ChatDeckSettingsEdits.UpdateTab(s, id, t => t with { Name = name.Trim() }));
    }

    private void LayoutRenameField()
    {
        if (_renaming is not { } id || _model is null) return;
        int i = _model.Settings.Tabs.ToList().FindIndex(t => t.Id == id);
        if (i < 0 || i >= _geometry.Tabs.Count || _geometry.Tabs[i].W <= 0)
        {
            FinishRename(null);
            return;
        }
        DeckRect r = _geometry.Tabs[i];
        Place(RenameField, r with { W = MathF.Max(r.W, 90f) });
        RenameField.DatFont = _fonts?.Face(LabelPixels, BundledUiFontWeight.Regular);
    }

    private void AddTabAndEdit()
    {
        if (_model is null) return;
        string? id = null;
        if (!_model.Edit(s => ChatDeckSettingsEdits.AddTab(s, "New tab", out id)) || id is null) return;
        _model.SelectTab(id);
        ResetView();
        _b.OpenEditor?.Invoke(new ChatDeckEditorRequest { Page = ChatDeckEditorPage.Tabs, TabId = id });
    }
}
