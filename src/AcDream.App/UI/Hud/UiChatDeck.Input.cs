using System.Numerics;
using AcDream.Core.Chat.Deck;
using AcDream.Runtime.Chat;
using AcDream.UI.Abstractions.Input;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck : IChatEntryTarget
{
    private readonly UiMarkupContextMenu _chipMenu = new() { ThemePalette = ChatDeckPalette.Menu };
    internal UiMarkupContextMenu ChipMenu => _chipMenu;
    private ChatDeckSendChannel? _override;

    internal UiField Input { get; private set; } = null!;
    internal ChatDeckSendChannel? ChannelOverride => _override;
    internal string ChipLabel => ChipText;

    private ChatDeckSendChannel SendChannel =>
        _model?.SendChannelFor(_model.SelectedTabId, _override) ?? _override ?? ChatDeckSendChannel.Say;

    private string ChipText => ChatDeckSend.DisplayName(SendChannel);

    private void CreateInputRow()
    {
        Input = new UiField
        {
            OneLine = true,
            Selectable = true,
            Keyboard = _b.Keyboard,
            BackgroundColor = ChatDeckPalette.FieldFill,
            TextColor = ChatDeckPalette.TextInk,
            // The entry owner sends the line, remembers it and clears the draft;
            // the field keeps the caret, the selection and the drawing.
            RecordHistory = false,
            SharedHistory = _b.Entry,
            StayFocusedAfterSubmit = _b.StayInChatMode,
        };
        Input.TextReplacer = text => ChatTextReplacements.Expand(text, _b.LastIncomingTellSender());
        Input.OnSubmit = Send;
        Input.OnTextChanged = text => _b.Entry.SetDraft(text);
        Input.OnControlKey = (key, shift) => key == Silk.NET.Input.Key.Tab && CycleTab(shift ? -1 : 1);
        _b.Entry.DraftChanged += OnDraftChanged;
        Input.ShowText(_b.Entry.Draft);

        _chipMenu.ItemsSource = () => ChatDeckSend.All.Select(ChatDeckSend.DisplayName).ToArray();
        _chipMenu.Selected = index =>
        {
            if (index >= 0 && index < ChatDeckSend.All.Count) ChooseChannel(ChatDeckSend.All[index]);
            FindRoot()?.SetKeyboardFocus(Input);
        };

        AddChild(Input);
        AddChild(_chipMenu);
    }

    /// <summary>Stops following the shared draft and the model's clears; the deck is being torn down.</summary>
    internal void Detach()
    {
        _b.Entry.DraftChanged -= OnDraftChanged;
        if (_model is not null) _model.Cleared -= ResetView;
    }

    private void OnDraftChanged(string text)
    {
        if (!string.Equals(Input.Text, text, StringComparison.Ordinal))
            Input.ShowText(text);
    }

    private void LayoutInputRow()
    {
        Place(Input, _geometry.Input);
        ChatDeckAppearance appearance = _model?.Settings.Appearance ?? new ChatDeckAppearance();
        Input.DatFont = _fonts?.Face(appearance.InputFontSize, BundledUiFontWeight.Regular);
        _chipMenu.DatFont = _fonts?.Face(appearance.FontSize, BundledUiFontWeight.Regular);
    }

    /// <summary>Sends the next line on <paramref name="channel"/> instead of the tab's own (null: the tab's).</summary>
    internal void ChooseChannel(ChatDeckSendChannel? channel)
    {
        ChatDeckSendChannel tabChannel = _model?.SendChannelFor(_model.SelectedTabId, null) ?? ChatDeckSendChannel.Say;
        _override = channel == tabChannel ? null : channel;
    }

    private void OpenChipMenu(float x, float y)
    {
        Vector2 origin = ScreenPosition;
        _chipMenu.TryOpen((int)(origin.X + x), (int)(origin.Y + y));
    }

    private void Send(string text)
    {
        ChatChannelKind channel = ChatDeckSend.ToKind(SendChannel);
        _override = null;
        _b.Entry.SubmitTo(text, channel, _b.Feedback, _b.Bus());
    }

    private bool CycleTab(int step)
    {
        if (_model is null || _model.Settings.Tabs.Count == 0) return false;
        IReadOnlyList<ChatDeckTab> tabs = _model.Settings.Tabs;
        int at = Math.Max(0, tabs.ToList().FindIndex(t => t.Id == _model.SelectedTabId));
        SelectTab(((at + step) % tabs.Count + tabs.Count) % tabs.Count);
        return true;
    }

    private void OnInputHidden()
    {
        _chipMenu.Close(restoreFocus: false);
        if (FindRoot() is { } root
            && (ReferenceEquals(root.KeyboardFocus, Input) || ReferenceEquals(root.KeyboardFocus, SearchField)))
            root.SetKeyboardFocus(null);
    }

    private void OnViewReset() => _override = null;

    // ── IChatEntryTarget: the retail chat window's behaviour, on this input ──

    public void EnterChatMode(KeyChord? physicalChord)
    {
        UiRoot? root = FindRoot();
        root?.SetKeyboardFocus(Input);
        if (physicalChord is { Device: 0 } chord)
            root?.SuppressPhysicalKeyUntilRelease(chord.Key);
        Input.SelectAllText();
    }

    public void ToggleChatEntry(KeyChord? physicalChord)
    {
        if (FindRoot() is not { } root) return;
        root.SetKeyboardFocus(ReferenceEquals(root.KeyboardFocus, Input) ? null : Input);
        if (physicalChord is { Device: 0 } chord)
            root.SuppressPhysicalKeyUntilRelease(chord.Key);
    }

    public void StartCommand()
    {
        Input.SetText("/");
        FindRoot()?.SetKeyboardFocus(Input);
    }

    public void StartTell(string name)
    {
        Input.SetText($"@tell {name}, ");
        FindRoot()?.SetKeyboardFocus(Input);
    }

    public void StartReply(string? name)
    {
        if (!string.IsNullOrEmpty(name)) StartTell(name);
    }

    public bool Focus()
    {
        if (FindRoot() is not { } root) return false;
        root.SetKeyboardFocus(Input);
        return true;
    }

    private void DrawInputRow(UiRenderContext ctx)
    {
        DeckRect chip = _geometry.Chip;
        bool overridden = _override is not null;
        ctx.FillRoundedRect(chip.X, chip.Y, chip.W, chip.H, 5f,
            overridden ? ChatDeckPalette.StudOn : ChatDeckPalette.StudOff);
        DrawLabel(ctx, LabelBold, ChipText, chip.X + ChatDeckGeometry.TabPadX, chip,
            overridden ? ChatDeckPalette.TextInk : ChatDeckPalette.LabelInk);
        DrawIcon(ctx, HudIcons.ChevronDown, chip.X + chip.W - ChatDeckGeometry.ChipArrow - 2f,
            chip.Y + (chip.H - 10f) / 2f, 10f, ChatDeckPalette.StudOffInk);

        DeckRect input = _geometry.Input;
        ctx.FillRoundedRect(input.X, input.Y, input.W, input.H, 4f, ChatDeckPalette.FieldFill);
    }
}
