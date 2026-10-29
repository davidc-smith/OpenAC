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
    private (IReadOnlyList<GearChip> Chips, IReadOnlyList<SavedFilter> Saved)? _pillsFor;
    /// <summary>The pack ids, in side-column order, the Pack chip labels were built with (empty without one).</summary>
    private uint[] _pillsPacks = [];
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

    /// <summary>The ★ flow's inline name field, over <see cref="GearDeckGeometry.QueryText"/> while a filter is being named.</summary>
    internal UiField SaveNameInput { get; } = new()
    {
        OneLine = true, Selectable = true,
        BackgroundColor = ChatDeckPalette.FieldFill, TextColor = DeckChrome.TextInk,
        ClearOnSubmit = false, RecordHistory = false, Visible = false,
        MaxCharacters = SavedFilter.MaxNameLength,
    };

    private bool _naming;
    /// <summary>The name field lost the focus to a press on the ★ (which shows it on); the click that follows
    /// leaves it closed instead of opening it again, so the ★ toggles. Read and cleared by the press.</summary>
    private bool _namingClosedByStar;
    private bool _starCloses;

    /// <summary>No room for a new saved filter (a same-named one can still be replaced).</summary>
    private bool SavedFull => _b.Session.Settings.SavedFilters.Count >= SavedFilter.MaxCount;
    private string?[] _fittedSaved = [];
    /// <summary>Which saved filters equal the live query, cached per query version and saved-filters list.</summary>
    private bool[] _savedLit = [];
    private int _savedLitVersion = -1;
    private IReadOnlyList<SavedFilter>? _savedLitFor;

    private bool SavedLit(IReadOnlyList<SavedFilter> saved, int index)
    {
        if (_savedLitVersion != _queryVersion || !ReferenceEquals(_savedLitFor, saved) || _savedLit.Length != saved.Count)
        {
            if (_savedLit.Length != saved.Count) _savedLit = new bool[saved.Count];
            for (int i = 0; i < _savedLit.Length; i++) _savedLit[i] = _query.Equals(saved[i].Query);
            _savedLitVersion = _queryVersion;
            _savedLitFor = saved;
        }
        return index < _savedLit.Length && _savedLit[index];
    }
    private string _savedMoreText = "";
    private int _savedMoreFor = -1;
    private (GearSortField Field, string Text)? _sortText;

    /// <summary>The chips the typed text suggests, best first (the name chip last).</summary>
    internal IReadOnlyList<GearChip> Suggestions => _suggestions;

    /// <summary>The highlighted suggestion: the one Enter takes.</summary>
    internal int SuggestIndex => _suggestIndex;

    /// <summary>The suggestion rows shown: all of them while the field has focus and text, else none.</summary>
    private int VisibleSuggestions => _model.Packs.Count > 0 && QueryInput.IsFocused ? _suggestions.Count : 0;

    private void CreateQueryInput()
    {
        QueryInput.Keyboard = _b.Keyboard;
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

        SaveNameInput.Keyboard = _b.Keyboard;
        SetFieldFonts();
        SaveNameInput.OnSubmit = text => FinishSave(text, "enter");
        SaveNameInput.OnFocusLost = _ => FinishSave(null, "focus lost");   // like Esc: leaving the field cancels
        SaveNameInput.OnEscape = () => FinishSave(null, "escape");
        AddChild(SaveNameInput);
        PlaceQueryInput();
    }

    /// <summary>The query and name fields type in the deck's small face at its text size, capped to the field's box.</summary>
    private void SetFieldFonts()
    {
        float padding = 4f * _scale;
        UiDatFont? face = _fonts?.FitFace(DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, _scale, _textFactor,
            GearDeckGeometry.QueryH * _scale - 2f * padding);
        QueryInput.DatFont = face;
        SaveNameInput.DatFont = face;
        QueryInput.Padding = SaveNameInput.Padding = padding;
    }

    /// <summary>★: opens the name field, pre-filled from the chip labels and selected. Does nothing while the query is empty.</summary>
    private void StartSave()
    {
        if (_naming || _query.IsEmpty || _model.Packs.Count == 0)
        {
            _b.Log?.Invoke($"[gear-deck] save: star ignored (naming {_naming}, query empty {_query.IsEmpty}, packs {_model.Packs.Count})");
            return;
        }
        _naming = true;
        string name = string.Join(" ", ChipLabels);
        if (name.Length == 0) name = _query.Text;
        if (name.Length > SavedFilter.MaxNameLength) name = name[..SavedFilter.MaxNameLength].TrimEnd();
        SaveNameInput.SetText(name);
        SaveNameInput.Visible = true;
        PlaceQueryInput();
        FindRoot()?.SetKeyboardFocus(SaveNameInput);
        SaveNameInput.SelectAllText();
        _b.Log?.Invoke($"[gear-deck] save: name field open, focused {SaveNameInput.IsFocused}");
    }

    /// <summary>Saves the live query under a non-blank <paramref name="name"/> (null cancels) and hides the field;
    /// a second call does nothing. <paramref name="why"/> goes to the log line, for live checks.</summary>
    private void FinishSave(string? name, string why)
    {
        if (!_naming) return;
        _naming = false;
        // The root hit-tests a press before it moves the focus, so _hit is the press's zone here.
        _namingClosedByStar = why == "focus lost" && _hit.Zone == GearZone.SaveStud && FindRoot() is { LeftButtonDown: true };
        _b.Log?.Invoke($"[gear-deck] save: name field closed ({why}){(name is null ? "" : " saving")}");
        SaveNameInput.Visible = false;
        if (FindRoot() is { } root && ReferenceEquals(root.KeyboardFocus, SaveNameInput)) root.SetKeyboardFocus(null);
        if (name is not null && name.Trim().Length > 0 && !_query.IsEmpty)
        {
            GearQuery query = _query;
            if (SavedFull && !_b.Session.Settings.SavedFilters.Any(f => string.Equals(f.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
                _b.Actions.Report($"You can keep up to {SavedFilter.MaxCount.ToString(CultureInfo.InvariantCulture)} saved filters.");
            _b.Session.Update(s => s.WithSaved(new SavedFilter(name, query)));
        }
        SyncGeometry();
    }

    /// <summary>Makes <paramref name="filter"/>'s query the live one, with its text in the field so both agree.</summary>
    private void ApplySaved(SavedFilter filter)
    {
        QueryInput.SetText("");   // first: its change handler would strip the filter's own text
        SetQuery(filter.Query);
        QueryInput.SetText(filter.Query.Text);   // the handler then sets the same trimmed text: no change
    }

    /// <summary>A click on saved stud <paramref name="index"/>: applies it, or clears the query when it is already lit.</summary>
    private void ClickSaved(int index)
    {
        IReadOnlyList<SavedFilter> saved = _b.Session.Settings.SavedFilters;
        if (index < 0 || index >= saved.Count) return;
        if (_query.Equals(saved[index].Query))
        {
            SetQuery(GearQuery.Empty);
            QueryInput.SetText("");   // after: its handler then sets the same empty query, no second change
        }
        else ApplySaved(saved[index]);
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
        float inset = 2f * _scale;
        QueryInput.Left = r.X + inset;
        QueryInput.Top = r.Y;
        QueryInput.Width = MathF.Max(0f, r.W - 2f * inset);
        QueryInput.Height = r.H;
        bool shown = _model.Packs.Count > 0;
        QueryInput.Visible = shown && !_naming;
        if (_naming && (!shown || _query.IsEmpty)) FinishSave(null, "nothing to name");
        if (SaveNameInput.Anchors != AnchorEdges.None) SaveNameInput.Anchors = AnchorEdges.None;
        DeckRect q = _g.QueryText;
        SaveNameInput.Left = q.X;
        SaveNameInput.Top = q.Y;
        SaveNameInput.Width = q.W;
        SaveNameInput.Height = q.H;
        // A hidden field must not keep typing into a query nobody can see.
        if (!shown && QueryInput.IsFocused && FindRoot() is { } root && ReferenceEquals(root.KeyboardFocus, QueryInput))
            root.SetKeyboardFocus(null);
    }

    /// <summary>The chip labels and saved names the geometry lays out, rebuilt when they change.</summary>
    private GearPills Pills()
    {
        IReadOnlyList<GearChip> chipsNow = _query.Chips;
        IReadOnlyList<SavedFilter> saved = _b.Session.Settings.SavedFilters;
        // Only a Pack chip's label ("Pack N", a place in the side column) follows the model, and only
        // through the packs: any other table change leaves the pills, and so the geometry, alone.
        bool packChip = false;
        for (int i = 0; i < chipsNow.Count; i++)
            if (chipsNow[i].Field == GearField.Pack) { packChip = true; break; }
        if (_pillsFor is { } k && ReferenceEquals(k.Chips, chipsNow) && ReferenceEquals(k.Saved, saved)
            && (packChip ? SamePacks(_pillsPacks) : _pillsPacks.Length == 0))
            return _pills;
        var chips = new string[_query.Chips.Count];
        for (int i = 0; i < chips.Length; i++) chips[i] = GearChipText.Label(_query.Chips[i], PackName);
        var names = new string[saved.Count];
        for (int i = 0; i < names.Length; i++) names[i] = saved[i].Name;
        DeckFace bold = DeckFace.SmallBoldFor(_fonts, _scale);   // pill widths at the deck's own size: the pills are fixed boxes
        float fallback = 6f * _scale;   // the fontless estimate at the scale, as the faces are
        _pills = new GearPills(chips, names, t => bold.Font?.MeasureWidth(t) ?? t.Length * fallback);
        _pillsFor = (chipsNow, saved);
        _pillsPacks = packChip ? PackIds() : [];
        _pillsVersion++;
        _fittedChips = new string?[chips.Length];
        _fittedSaved = new string?[names.Length];
        return _pills;
    }

    private bool SamePacks(uint[] ids)
    {
        IReadOnlyList<GearPack> packs = _model.Packs;
        if (packs.Count != ids.Length) return false;
        for (int i = 0; i < ids.Length; i++)
            if (packs[i].Id != ids[i]) return false;
        return true;
    }

    private uint[] PackIds()
    {
        var ids = new uint[_model.Packs.Count];
        for (int i = 0; i < ids.Length; i++) ids[i] = _model.Packs[i].Id;
        return ids;
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
