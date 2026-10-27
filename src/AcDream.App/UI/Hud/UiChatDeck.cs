using System.Numerics;
using AcDream.App.UI.Layout;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Chat;
using AcDream.UI.Abstractions.Panels.Chat;

namespace AcDream.App.UI.Hud;

/// <summary>What the Chat Deck reads and sends through.</summary>
/// <param name="Model">The character's model, or null between sessions.</param>
/// <param name="Entry">The one chat entry every front end shares (draft, recall).</param>
internal sealed record ChatDeckBindings(
    Func<ChatDeckModel?> Model,
    RuntimeChatEntryOwner Entry,
    IChatCommandFeedback Feedback,
    Func<ICommandBus> Bus,
    Func<string?> LastIncomingTellSender,
    Func<bool> StayInChatMode,
    Action<PluginChatLinkClicked>? LinkClicked = null,
    Silk.NET.Input.IKeyboard? Keyboard = null);

/// <summary>
/// The Chat Deck window (pinned layout): a tab bar, a transcript of the selected
/// tab, and an input that sends on the tab's channel. The deck draws its frame,
/// tabs and studs itself; the transcript, the search field, the input and the
/// menus are child elements, so selection, copy, scrolling and typing behave as
/// they do everywhere else.
/// </summary>
internal sealed partial class UiChatDeck : UiElement
{
    private const float LabelPixels = 12f;

    private readonly ChatDeckBindings _b;
    private readonly DeckFonts? _fonts;
    private readonly EmbeddedSvgSpriteCache? _icons;
    private readonly ChatDeckTranscript _transcriptBuilder = new();
    private readonly UiMarkupContextMenu _tabMenu = new();
    private ChatDeckTranscriptLayout _layout = new();
    private ChatDeckModel? _model;
    private ChatDeckGeometry _geometry;
    private IReadOnlyList<ChatDeckTabLabel> _labels = [];
    private object? _builtFor;
    private bool _searchOpen;
    private string _searchText = "";
    private bool _shown;
    private long? _addedAtScrollUp;
    private int _menuTab = -1;
    private ChatDeckHit _hit;

    public UiChatDeck(ChatDeckBindings bindings, DeckFonts? fonts, EmbeddedSvgSpriteCache? icons)
    {
        _b = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _fonts = fonts;
        _icons = icons;
        Width = ChatDeckGeometry.DefaultWidth;
        Height = ChatDeckGeometry.DefaultHeight;
        MinWidth = ChatDeckGeometry.MinWidth;
        MinHeight = ChatDeckGeometry.MinHeight;
        Resizable = true;
        _geometry = ChatDeckGeometry.Create(Width, Height, [], false, ChipText, MeasureLabel);

        Transcript = new UiText
        {
            Selectable = true,
            WheelScrollEnabled = true,
            PreserveEndOnLayout = true,
            Padding = 2f,
            MarginLeft = 6f,
            Keyboard = bindings.Keyboard,
            TagColor = ChatDeckPalette.TagInk,
            LinesProvider = () => _layout.Lines,
            LineKeysProvider = () => _layout.Keys,
        };
        Transcript.LineRunsProvider = i => i >= 0 && i < _layout.Runs.Count ? _layout.Runs[i] : null;
        Transcript.LineMarkerProvider = i => i >= 0 && i < _layout.Markers.Count ? _layout.Markers[i] : null;
        Transcript.OnCharClick = OnTranscriptClick;

        NewLinesMarker = new NewLines(this) { Visible = false };
        SearchField = new UiField
        {
            OneLine = true, Selectable = true, Keyboard = bindings.Keyboard,
            BackgroundColor = ChatDeckPalette.FieldFill, TextColor = ChatDeckPalette.TextInk,
            ClearOnSubmit = false, RecordHistory = false, Visible = false,
        };
        SearchField.OnTextChanged = text => _searchText = text.Trim();
        SearchField.OnEscape = CloseSearch;

        _tabMenu.Opening = (x, y) => _menuTab = _geometry.HitAt(x, y) is { Zone: ChatDeckZone.Tab } hit ? hit.Tab : -1;
        _tabMenu.ItemsSource = () => _menuTab >= 0 ? TabMenuItems : [];
        _tabMenu.Selected = ChooseTabMenuItem;
        ContextMenu = _tabMenu;

        AddChild(Transcript);
        AddChild(NewLinesMarker);
        AddChild(SearchField);
        AddChild(_tabMenu);
        CreateInputRow();          // UiChatDeck.Input.cs (Task 10); until then an empty partial method
    }

    private static readonly string[] TabMenuItems = ["Mark as read", "Mark all read"];

    internal UiText Transcript { get; }
    internal UiField SearchField { get; }
    internal UiElement NewLinesMarker { get; }
    internal ChatDeckGeometry Geometry => _geometry;
    internal IReadOnlyList<ChatDeckTabLabel> TabLabels => _labels;
    internal bool SearchOpen => _searchOpen;
    internal int NewLineCount { get; private set; }
    internal int TranscriptBuilds { get; private set; }

    public override bool HandlesClick => IsControl(_hit);

    private static bool IsControl(ChatDeckHit hit) =>
        hit.Zone is ChatDeckZone.Tab or ChatDeckZone.SearchStud or ChatDeckZone.Chip;

    internal bool HitTestLocal(float x, float y) => OnHitTest(x, y);

    protected override bool OnHitTest(float localX, float localY)
    {
        _hit = _geometry.HitAt(localX, localY);
        CapturesPointerDrag = IsControl(_hit);
        return _hit.Zone != ChatDeckZone.None;
    }

    protected override void OnTick(double deltaSeconds) => Refresh();

    /// <summary>The window is on screen (the switch showed it).</summary>
    public void OnShown() => _shown = true;

    /// <summary>The window left the screen: lines arriving now are unread.</summary>
    public void OnHidden()
    {
        _shown = false;
        _tabMenu.Close(restoreFocus: false);
        OnInputHidden();           // UiChatDeck.Input.cs (Task 10)
    }

    internal ChatDeckModel? Model => _model;

    private ChatDeckTabState? SelectedTab => _model?.FindTab(_model.SelectedTabId);

    /// <summary>Samples the model, lays the window out and rebuilds the transcript if anything it shows changed.</summary>
    internal void Refresh()
    {
        ChatDeckModel? model = _b.Model();
        if (!ReferenceEquals(model, _model))
        {
            if (_model is not null) _model.Cleared -= ResetView;
            _model = model;
            if (_model is not null) _model.Cleared += ResetView;
            ResetView();
        }

        _labels = _model is null
            ? []
            : _model.Settings.Tabs
                .Select(t => new ChatDeckTabLabel(t.Name, ChatDeckBadges.Text(_model.FindTab(t.Id)?.Unread ?? 0)))
                .ToList();
        _geometry = ChatDeckGeometry.Create(Width, Height, _labels, _searchOpen, ChipText, MeasureLabel);

        Place(Transcript, _geometry.Transcript);
        Place(SearchField, _searchOpen ? _geometry.SearchField : default);
        SearchField.Visible = _searchOpen;
        Place(NewLinesMarker, _geometry.NewLines);
        LayoutInputRow();          // UiChatDeck.Input.cs (Task 10)

        ChatDeckAppearance appearance = _model?.Settings.Appearance ?? new ChatDeckAppearance();
        Transcript.DatFont = _fonts?.Face(appearance.FontSize, BundledUiFontWeight.Regular);
        SearchField.DatFont = _fonts?.Face(appearance.InputFontSize, BundledUiFontWeight.Regular);

        RebuildTranscript(appearance);
        TrackNewLines();
        _model?.SetView(_shown, Transcript.Scroll.AtEnd);
    }

    private static void Place(UiElement child, DeckRect r)
    {
        child.Left = r.X;
        child.Top = r.Y;
        child.Width = r.W;
        child.Height = r.H;
    }

    private void RebuildTranscript(ChatDeckAppearance appearance)
    {
        ChatDeckTabState? tab = SelectedTab;
        float wrap = MathF.Max(1f, Transcript.Width - 2 * Transcript.Padding - Transcript.MarginLeft - Transcript.MarginRight);
        UiDatFont? font = Transcript.DatFont;
        var key = (_model, tab, tab?.Version ?? -1, wrap, font, _searchText, _model?.Settings);
        if (Equals(key, _builtFor)) return;
        _builtFor = key;
        TranscriptBuilds++;

        if (tab is null)
        {
            _layout = new ChatDeckTranscriptLayout();
            return;
        }
        Func<string, float> measure = font is not null ? font.MeasureWidth : static s => s.Length * 7f;
        _layout = _transcriptBuilder.Build(tab.Snapshot(), new ChatDeckTranscriptStyle(
            wrap, measure, font, appearance.Timestamps,
            line => ChannelColour(line, appearance),
            ChatDeckPalette.TagInk, ChatDeckPalette.TimestampInk, _searchText));
    }

    private static Vector4 ChannelColour(ChatDeckLine line, ChatDeckAppearance appearance) =>
        appearance.ChannelColours.TryGetValue(line.Channel, out uint argb) ? ChatDeckTranscript.Argb(argb)
        : RetailChatColorTable.TryGetColor(line.Entry.LogTextType, out Vector4 colour) ? colour
        : ChatDeckPalette.TextInk;

    private void TrackNewLines()
    {
        ChatDeckTabState? tab = SelectedTab;
        if (tab is null || Transcript.Scroll.AtEnd)
        {
            _addedAtScrollUp = null;
            NewLineCount = 0;
        }
        else
        {
            if (_addedAtScrollUp is not long since || tab.Added < since) _addedAtScrollUp = since = tab.Added;
            NewLineCount = (int)Math.Min(int.MaxValue, tab.Added - since);
        }
        NewLinesMarker.Visible = NewLineCount > 0;
    }

    private void ResetView()
    {
        _addedAtScrollUp = null;
        NewLineCount = 0;
        _builtFor = null;
        Transcript.Scroll.ScrollToEnd();
        OnViewReset();             // UiChatDeck.Input.cs (Task 10): drops a one-line channel override
    }

    private void FollowNewest()
    {
        Transcript.Scroll.ScrollToEnd();
        _addedAtScrollUp = null;
        NewLineCount = 0;
        NewLinesMarker.Visible = false;
    }

    private void SelectTab(int index)
    {
        if (_model is null || index < 0 || index >= _model.Settings.Tabs.Count) return;
        _model.SelectTab(_model.Settings.Tabs[index].Id);
        ResetView();
    }

    private void ToggleSearch()
    {
        if (_searchOpen)
        {
            CloseSearch();
            return;
        }
        _searchOpen = true;
        FindRoot()?.SetKeyboardFocus(SearchField);
    }

    private void CloseSearch()
    {
        _searchOpen = false;
        _searchText = "";
        SearchField.SetText("");
        if (FindRoot() is { } root && ReferenceEquals(root.KeyboardFocus, SearchField))
            root.SetKeyboardFocus(null);
    }

    /// <summary>The tab menu's items for a right-click at a point (empty off the tabs), as the menu reads them.</summary>
    internal IReadOnlyList<string> TabMenuItemsAt(float x, float y)
    {
        _tabMenu.Opening!(x, y);
        return _tabMenu.ItemsSource();
    }

    internal void ChooseTabMenuItem(int index)
    {
        if (_model is null || _menuTab < 0 || _menuTab >= _model.Settings.Tabs.Count) return;
        if (index == 0) _model.FindTab(_model.Settings.Tabs[_menuTab].Id)?.MarkRead();
        else if (index == 1) _model.MarkAllRead();
    }

    private bool OnTranscriptClick(UiText.Pos position)
    {
        if (position.Line < 0 || position.Line >= _layout.Tags.Count || _layout.Tags[position.Line] is not { } ranges)
            return false;
        foreach ((int start, int length, ChatTextTag tag) in ranges)
        {
            if (position.Col < start || position.Col >= start + length) continue;
            if (tag.TryGetCoordinate(out double eastWest, out double northSouth))
            {
                _b.LinkClicked?.Invoke(new PluginChatLinkClicked(
                    PluginChatLinkKind.Coordinate,
                    ChatCoordinateLinks.Describe(eastWest, northSouth),
                    new PluginChatCoordinate(eastWest, northSouth)));
                return true;
            }
            if (tag.IsTell && tag.TryGetIidString(out _, out string name) && name.Length > 0)
            {
                StartTell(name);   // UiChatDeck.Input.cs (Task 10)
                return true;
            }
        }
        return false;
    }

    private float MeasureLabel(string text) =>
        _fonts?.Face(LabelPixels, BundledUiFontWeight.Regular).MeasureWidth(text) ?? text.Length * 7f;

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.MouseDown:
            {
                ChatDeckHit hit = _geometry.HitAt(e.Data1, e.Data2);
                switch (hit.Zone)
                {
                    case ChatDeckZone.Tab: SelectTab(hit.Tab); return true;
                    case ChatDeckZone.SearchStud: ToggleSearch(); return true;
                    case ChatDeckZone.Chip: OpenChipMenu(e.Data1, e.Data2); return true;   // Task 10
                    default: return false;
                }
            }
            case UiEventType.Click:
                return IsControl(_hit);
            default:
                return false;
        }
    }

    /// <summary>"N new" over the bottom of the transcript while scrolled up; a click follows the newest line.</summary>
    private sealed class NewLines(UiChatDeck deck) : UiElement
    {
        public override bool HandlesClick => true;

        protected override bool OnHitTest(float localX, float localY) => true;

        public override bool OnEvent(in UiEvent e)
        {
            if (e.Type is not (UiEventType.MouseDown or UiEventType.Click)) return false;
            if (e.Type == UiEventType.MouseDown) deck.FollowNewest();
            return true;
        }

        protected override void OnDraw(UiRenderContext ctx) => deck.DrawNewLines(ctx, Width, Height);
    }
}
