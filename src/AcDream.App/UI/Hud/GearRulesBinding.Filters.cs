using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The Filters tab: the saved filters (the deck's ★ studs, from the same settings) and an editor for one.
/// The query field works as the deck's query bar does: what is typed lists <see cref="GearChipSuggester"/>'s
/// chips, a picked one becomes a chip and clears the text, and text left over when saving is the
/// query's name-contains text.
/// </summary>
public sealed partial class GearRulesBinding
{
    private bool _filtersPage;
    private GearDeckSession? _seenSession;

    /// <summary>The selected filter, by name: the deck can add or delete filters while the window is open.</summary>
    private string? _filterSelected;
    /// <summary>The filter the editor was loaded from (or last saved as): Save renames it when the name changes.</summary>
    private string? _loadedName;
    private string _draftName = "";
    private string _draftText = "";
    private IReadOnlyList<GearChip> _draftChips = [];
    /// <summary>The loaded filter's sort, kept on save: the editor has no sort of its own.</summary>
    private GearSort _draftSort = GearSort.Default;
    private int _chipSelected = -1;
    private IReadOnlyList<GearChip> _suggestions = [];

    public bool OnRulesPage => !_filtersPage;
    public bool OnFiltersPage => _filtersPage;
    public Action ShowRulesPage => () => _filtersPage = false;
    public Action ShowFiltersPage => () => _filtersPage = true;

    /// <summary>The settings, after clearing the editor if the session is not the one it last saw (a new character).</summary>
    private IReadOnlyList<SavedFilter> Saved
    {
        get
        {
            GearDeckSession? session = _session();
            if (!ReferenceEquals(session, _seenSession))
            {
                _seenSession = session;
                ClearEditor();
                _filterSelected = null;
            }
            return S.SavedFilters;
        }
    }

    /// <summary>Reads the session first: a new one clears the editor before an edit lands in it.</summary>
    private void Sync() => _ = Saved;

    // ── the list ──
    private IReadOnlyList<SavedFilter>? _listFor;
    private IReadOnlyList<GearPack>? _listPacks;
    private (string[] Names, string[] Summaries) _list = ([], []);

    /// <summary>The list's columns, rebuilt only when the saved filters or the packs change.</summary>
    private (string[] Names, string[] Summaries) FilterList
    {
        get
        {
            IReadOnlyList<SavedFilter> saved = Saved;
            IReadOnlyList<GearPack> packs = _packs();
            if (ReferenceEquals(saved, _listFor) && ReferenceEquals(packs, _listPacks)) return _list;
            _listFor = saved;
            _listPacks = packs;
            _list = (saved.Select(f => f.Name).ToArray(), saved.Select(f => GearRules.Summary(f.Query, packs)).ToArray());
            return _list;
        }
    }

    public IReadOnlyList<string> SavedNames => FilterList.Names;
    public IReadOnlyList<string> SavedSummaries => FilterList.Summaries;

    public int SelectedFilterIndex => _filterSelected is { } name ? IndexOf(name) : -1;

    private int IndexOf(string name) => Saved.ToList().FindIndex(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    private void SelectSaved(int i)
    {
        IReadOnlyList<SavedFilter> saved = Saved;
        _filterSelected = i >= 0 && i < saved.Count ? saved[i].Name : null;
    }
    public bool HasSelectedFilter => SelectedFilterIndex >= 0;

    /// <summary>Selecting a row loads that filter into the editor.</summary>
    public Action<int> SelectFilterRow => i =>
    {
        SelectSaved(i);
        LoadSelected();
    };

    public Action EditFilter => LoadSelected;

    private void LoadSelected()
    {
        if (SelectedFilterIndex is not (var i and >= 0)) return;
        SavedFilter f = Saved[i];
        _loadedName = f.Name;
        _draftName = f.Name;
        _draftText = f.Query.Text;
        _draftChips = f.Query.Chips;
        _draftSort = f.Query.Sort;
        _chipSelected = -1;
        _suggestions = GearChipSuggester.Suggest(_draftText);
    }

    /// <summary>Deletes the selected filter; a rule that names it then shows "(missing)" and is skipped.</summary>
    public Action DeleteFilter => () =>
    {
        if (SelectedFilterIndex is not (var i and >= 0)) return;
        string name = Saved[i].Name;
        Edit(s => s.WithoutSaved(name));
        if (string.Equals(_draftName.Trim(), name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(_loadedName, name, StringComparison.OrdinalIgnoreCase)) ClearEditor();
        _filterSelected = null;
    };

    public IReadOnlyList<string> FilterMenu { get; } = ["Edit", "Delete"];
    public Action<int> FilterMenuOpen => SelectSaved;
    public Action<int> FilterMenuSelect => index =>
    {
        if (index == 0) LoadSelected();
        else if (index == 1) DeleteFilter();
    };

    // ── the editor ──
    public string EditorTitle =>
        LoadedName is not { } loaded ? "New filter"
        : Renaming ? $"Edit filter \"{loaded}\" (Save renames it)"
        : $"Edit filter \"{loaded}\" (Save replaces it)";

    public string DraftName { get { Sync(); return _draftName; } }
    public Action<string> SetDraftName => v => { Sync(); _draftName = v; };

    /// <summary>The query field's text; each change lists the chips it suggests.</summary>
    public string DraftText { get { Sync(); return _draftText; } }
    public Action<string> SetDraftText => v =>
    {
        Sync();
        _draftText = v;
        _suggestions = GearChipSuggester.Suggest(v);
    };

    /// <summary>Enter takes the first suggestion, as the deck's highlighted one.</summary>
    public Action<string> SubmitDraftText => v =>
    {
        SetDraftText(v);
        PickSuggestion(0);
    };

    public IReadOnlyList<string> SuggestionTexts
    {
        get
        {
            Sync();
            return _suggestions.Select(GearChipText.Suggestion).ToArray();
        }
    }
    /// <summary>The suggestions list never keeps a selection: a click on a row takes it.</summary>
    public int NoSuggestionSelected => -1;

    /// <summary>Adds suggestion <paramref name="index"/> as a chip and clears the typed text.</summary>
    public Action<int> PickSuggestion => index =>
    {
        Sync();
        if (index < 0 || index >= _suggestions.Count) return;
        GearChip chip = _suggestions[index];
        if (!_draftChips.Contains(chip)) _draftChips = [.. _draftChips, chip];
        _draftText = "";
        _suggestions = [];
    };

    public IReadOnlyList<string> DraftChipLabels
    {
        get
        {
            Sync();
            IReadOnlyList<GearPack> packs = _packs();
            return _draftChips.Select(c => GearChipText.Label(c, id => GearRules.PackName(packs, id))).ToArray();
        }
    }

    public int SelectedChipIndex => _chipSelected >= 0 && _chipSelected < _draftChips.Count ? _chipSelected : -1;
    public Action<int> SelectChipRow => i => { Sync(); _chipSelected = i >= 0 && i < _draftChips.Count ? i : -1; };
    public bool HasSelectedChip => SelectedChipIndex >= 0;

    public Action RemoveChip => () =>
    {
        Sync();
        if (SelectedChipIndex is not (var i and >= 0)) return;
        _draftChips = [.. _draftChips.Take(i), .. _draftChips.Skip(i + 1)];
        _chipSelected = -1;
    };

    public IReadOnlyList<string> ChipMenu { get; } = ["Remove chip"];
    public Action<int> ChipMenuSelect => index => { if (index == 0) RemoveChip(); };

    /// <summary>The query the editor would save: its chips, the leftover text (trimmed) and the loaded sort.</summary>
    internal GearQuery DraftQuery => new(_draftChips, _draftText.Trim(), _draftSort);

    private bool NameIsNew => !Saved.Any(f => string.Equals(f.Name, _draftName.Trim(), StringComparison.OrdinalIgnoreCase));
    private bool SavedFull => NameIsNew && Saved.Count >= SavedFilter.MaxCount;

    /// <summary>The loaded filter's current name, or null when none is loaded or the deck deleted it.</summary>
    private string? LoadedName => _loadedName is { } n && IndexOf(n) is var i and >= 0 ? Saved[i].Name : null;

    /// <summary>A loaded filter whose name was changed (not just its case): Save renames it.</summary>
    private bool Renaming => LoadedName is { } loaded && _draftName.Trim().Length > 0
        && !string.Equals(loaded, _draftName.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The name belongs to a filter other than the loaded one: saving would overwrite it, so it is
    /// refused (a new draft or a rename alike). Only the loaded filter's own name saves over it.</summary>
    private bool NameTaken => !NameIsNew
        && !(LoadedName is { } loaded && string.Equals(loaded, _draftName.Trim(), StringComparison.OrdinalIgnoreCase));

    private bool CanSaveAny => _draftName.Trim().Length > 0 && !DraftQuery.IsEmpty && _session() is not null;

    public bool CanSaveFilter => CanSaveAny && !NameTaken && (Renaming || !SavedFull);

    /// <summary>Save as new: a loaded filter's draft under a name not yet used, kept beside the original.</summary>
    public bool CanSaveAsNew => CanSaveAny && Renaming && NameIsNew && !SavedFull;

    public string SaveHint =>
        NameTaken ? $"A filter named \"{_draftName.Trim()}\" already exists."
        : SavedFull && !Renaming ? $"You can keep up to {SavedFilter.MaxCount} saved filters."
        : "";

    /// <summary>Saves the draft. A loaded filter is saved over (renamed in place when its name changed; its
    /// rules follow); a new draft is added up to the cap. A name another filter has is refused.</summary>
    public Action SaveFilter => () =>
    {
        if (!CanSaveFilter) return;
        var filter = new SavedFilter(_draftName, DraftQuery);
        if (Renaming && LoadedName is { } loaded) Edit(s => s.WithRenamed(loaded, filter));
        else Edit(s => s.WithSaved(filter));
        AfterSave();
    };

    /// <summary>Saves the draft as a new filter, leaving the loaded one as it was.</summary>
    public Action SaveFilterAsNew => () =>
    {
        if (!CanSaveAsNew) return;
        Edit(s => s.WithSaved(new SavedFilter(_draftName, DraftQuery)));
        AfterSave();
    };

    /// <summary>Selects the saved filter and treats it as the loaded one, so a later name change renames it.</summary>
    private void AfterSave()
    {
        string name = _draftName.Trim();
        if (name.Length > SavedFilter.MaxNameLength) name = name[..SavedFilter.MaxNameLength].TrimEnd();   // as WithSaved stores it
        if (IndexOf(name) is var i and >= 0)
        {
            _filterSelected = Saved[i].Name;
            _loadedName = _filterSelected;
            _draftName = _filterSelected;
        }
    }

    /// <summary>New: an empty editor (and no filter selected).</summary>
    public Action NewFilter => () =>
    {
        ClearEditor();
        _filterSelected = null;
    };

    private void ClearEditor()
    {
        _loadedName = null;
        _draftName = "";
        _draftText = "";
        _draftChips = [];
        _draftSort = GearSort.Default;
        _chipSelected = -1;
        _suggestions = [];
    }

    private const string FiltersPageMarkup = """
          <group layout="column" gap="6" grow="1" visible="{OnFiltersPage}">
            <list header="true" selected="{SelectedFilterIndex}" onchange="{SelectFilterRow}" rowheight="18" selectionband="true" h="112">
              <column type="text" header="Name" width="120" items="{SavedNames}" />
              <column type="text" header="Matches" width="*" items="{SavedSummaries}" />
              <contextmenu items="{FilterMenu}" onopen="{FilterMenuOpen}" onselect="{FilterMenuSelect}" />
            </list>
            <separator h="1" />
            <label text="{EditorTitle}" h="16" />
            <group layout="row" gap="6" align="center" h="22">
              <label text="Name" w="44" />
              <field text="{DraftName}" onchange="{SetDraftName}" maxlength="24" grow="1" h="20" />
            </group>
            <group layout="row" gap="6" align="center" h="22">
              <label text="Query" w="44" />
              <field text="{DraftText}" onchange="{SetDraftText}" onsubmit="{SubmitDraftText}" maxlength="40" grow="1" h="20" />
            </group>
            <group layout="row" gap="8" grow="1">
              <group layout="column" gap="2" grow="1">
                <label text="Suggestions (click to add)" h="16" />
                <list items="{SuggestionTexts}" selected="{NoSuggestionSelected}" onchange="{PickSuggestion}" keepfocus="true" rowheight="18" grow="1" />
              </group>
              <group layout="column" gap="2" grow="1">
                <label text="Chips" h="16" />
                <list items="{DraftChipLabels}" selected="{SelectedChipIndex}" onchange="{SelectChipRow}" rowheight="18" selectionband="true" grow="1">
                  <contextmenu items="{ChipMenu}" onopen="{SelectChipRow}" onselect="{ChipMenuSelect}" />
                </list>
              </group>
            </group>
            <label text="{SaveHint}" color="#FFFF6B6B" h="16" />
            <group layout="row" gap="6" h="22">
              <button text="New" onclick="{NewFilter}" w="50" h="22" />
              <button text="Save" onclick="{SaveFilter}" enabled="{CanSaveFilter}" w="50" h="22" />
              <button text="Save as new" onclick="{SaveFilterAsNew}" enabled="{CanSaveAsNew}" w="84" h="22" />
              <button text="Remove chip" onclick="{RemoveChip}" enabled="{HasSelectedChip}" w="90" h="22" />
              <group grow="1" />
              <button text="Edit" onclick="{EditFilter}" enabled="{HasSelectedFilter}" w="44" h="22" />
              <button text="Delete" onclick="{DeleteFilter}" enabled="{HasSelectedFilter}" w="56" h="22" />
            </group>
          </group>
        """;
}
