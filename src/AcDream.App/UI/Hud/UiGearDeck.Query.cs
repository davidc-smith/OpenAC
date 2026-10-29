using System.Globalization;
using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

/// <summary>The query bar's input: the text field, its chips and the suggestions under it.</summary>
internal sealed partial class UiGearDeck
{
    /// <summary>What the field shows while it is empty and unfocused (ASCII: the deck fonts' safe range).</summary>
    internal const string QueryPlaceholder = "Filter...";

    private IReadOnlyList<GearChip> _suggestions = [];
    /// <summary>Each suggestion's row text fitted to its row; reset with the suggestions.</summary>
    private string?[] _suggestTexts = [];
    private int _suggestIndex;
    /// <summary>Set when an Enter took a suggestion: only then does the field keep focus after the submit.</summary>
    private bool _tookOnSubmit;

    // The pills the geometry was built with: rebuilt when the chips, the saved names or the packs change.
    private GearPills _pills = new([], [], static t => t.Length * 6f);
    private (IReadOnlyList<GearChip> Chips, IReadOnlyList<SavedFilter> Saved, ulong Build)? _pillsFor;
    private int _pillsVersion;
    private int _geometryPills = -1;
    /// <summary>Chip labels fitted to their pills, by chip; cleared with the geometry.</summary>
    private string?[] _fittedChips = [];
    private string _moreText = "";
    private int _moreFor = -1;

    /// <summary>The query bar's text field; typing in it narrows the results live.</summary>
    internal UiField QueryInput { get; } = new()
    {
        OneLine = true, Selectable = true,
        TextColor = DeckChrome.TextInk,
        ClearOnSubmit = false, RecordHistory = false, MaxCharacters = 40,
    };

    /// <summary>The chips the typed text suggests, best first (the name chip last).</summary>
    internal IReadOnlyList<GearChip> Suggestions => _suggestions;

    /// <summary>The highlighted suggestion: the one Enter takes.</summary>
    internal int SuggestIndex => _suggestIndex;

    /// <summary>The suggestion rows shown: all of them while the field has focus and text, else none.</summary>
    private int VisibleSuggestions => _model.Packs.Count > 0 && QueryInput.IsFocused ? _suggestions.Count : 0;

    private void CreateQueryInput()
    {
        QueryInput.Keyboard = _b.Keyboard;
        QueryInput.DatFont = _fonts?.Face(DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular);
        QueryInput.OnTextChanged = text =>
        {
            _suggestions = GearChipSuggester.Suggest(text);
            _suggestTexts = new string?[_suggestions.Count];
            _suggestIndex = 0;
            SetQuery(_query with { Text = text.Trim() });
            SyncGeometry();
        };
        QueryInput.OnSubmit = _ => _tookOnSubmit = TakeSuggestion(_suggestIndex);
        QueryInput.StayFocusedAfterSubmit = () =>   // Enter that added a chip keeps typing; else it ends the edit
        {
            bool took = _tookOnSubmit;
            _tookOnSubmit = false;
            return took;
        };
        QueryInput.OnEscape = () => QueryInput.SetText("");   // the field then releases focus itself
        QueryInput.OnBackspaceWhenEmpty = () => SetQuery(_query.Without(_query.Chips.Count - 1));
        QueryInput.OnUp = () => MoveSuggestion(-1);
        QueryInput.OnDown = () => MoveSuggestion(1);
        QueryInput.OnFocusGained = SyncGeometry;   // the suggestions show (losing focus waits for the tick, so
                                                   // a press on a suggestion still finds its row)
        AddChild(QueryInput);
        PlaceQueryInput();
    }

    private void MoveSuggestion(int step)
    {
        if (_suggestions.Count == 0) return;
        _suggestIndex = Math.Clamp(_suggestIndex + step, 0, _suggestions.Count - 1);
    }

    /// <summary>Adds suggestion <paramref name="index"/> as a chip and clears the text, keeping the field
    /// focused; false (and nothing changed) when there is no such suggestion.</summary>
    internal bool TakeSuggestion(int index)
    {
        if (index < 0 || index >= _suggestions.Count) return false;
        SetQuery(_query.With(_suggestions[index]) with { Text = "" });
        QueryInput.SetText("");
        FindRoot()?.SetKeyboardFocus(QueryInput);
        return true;
    }

    /// <summary>Puts the field over the bar's free space after the chips; hidden until there is a player.</summary>
    private void PlaceQueryInput()
    {
        DeckRect r = _g.QueryField;
        if (QueryInput.Anchors != AnchorEdges.None) QueryInput.Anchors = AnchorEdges.None;
        QueryInput.Left = r.X + 2f;
        QueryInput.Top = r.Y;
        QueryInput.Width = MathF.Max(0f, r.W - 4f);
        QueryInput.Height = r.H;
        bool shown = _model.Packs.Count > 0;
        QueryInput.Visible = shown;
        // A hidden field must not keep typing into a query nobody can see.
        if (!shown && QueryInput.IsFocused && FindRoot() is { } root && ReferenceEquals(root.KeyboardFocus, QueryInput))
            root.SetKeyboardFocus(null);
    }

    /// <summary>The chip labels and saved names the geometry lays out, rebuilt when they change.</summary>
    private GearPills Pills()
    {
        IReadOnlyList<GearChip> chipsNow = _query.Chips;
        IReadOnlyList<SavedFilter> saved = _b.Session.Settings.SavedFilters;
        // Only a Pack chip's label ("Pack N") follows the model; any other leaves the pills alone.
        ulong build = 0;
        foreach (GearChip chip in chipsNow)
            if (chip.Field == GearField.Pack) { build = _modelBuild; break; }
        if (_pillsFor is { } k && ReferenceEquals(k.Chips, chipsNow) && ReferenceEquals(k.Saved, saved) && k.Build == build)
            return _pills;
        var chips = new string[_query.Chips.Count];
        for (int i = 0; i < chips.Length; i++) chips[i] = GearChipText.Label(_query.Chips[i], PackName);
        var names = new string[saved.Count];
        for (int i = 0; i < names.Length; i++) names[i] = saved[i].Name;
        DeckFace bold = Bold;
        _pills = new GearPills(chips, names, t => bold.Font?.MeasureWidth(t) ?? t.Length * 6f);
        _pillsFor = (chipsNow, saved, build);
        _pillsVersion++;
        _fittedChips = new string?[chips.Length];
        return _pills;
    }

    /// <summary>A Pack chip's label: "Main", "Pack N" by its place in the side column, or "Pack ?" when gone.</summary>
    private string PackName(uint id)
    {
        for (int i = 0; i < _model.Packs.Count; i++)
            if (_model.Packs[i].Id == id)
                return _model.Packs[i].IsMain ? "Main" : "Pack " + (i + 1).ToString(CultureInfo.InvariantCulture);
        return "Pack ?";
    }

    /// <summary>The chip labels as the bar draws them (before fitting).</summary>
    internal IReadOnlyList<string> ChipLabels => Pills().Chips;

    /// <summary>"+N": how many chips the bar has no room for.</summary>
    private string MoreText
    {
        get
        {
            if (_moreFor != _g.HiddenChips)
            {
                _moreText = "+" + _g.HiddenChips.ToString(CultureInfo.InvariantCulture);
                _moreFor = _g.HiddenChips;
            }
            return _moreText;
        }
    }
}
