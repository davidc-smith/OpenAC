using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

/// <summary>The deck's one popup menu. What it lists depends on where it was opened.</summary>
internal sealed partial class UiGearDeck
{
    /// <summary>What the open menu lists; later plans fill in Sort, SavedFilter, SavedMore and Item.</summary>
    private enum MenuKind { None, HiddenChips, Sort, SavedFilter, SavedMore, Item, TextSize, Pack }

    private readonly UiMarkupContextMenu _menu = new() { ThemePalette = ChatDeckPalette.Menu };
    private MenuKind _menuKind;
    /// <summary>For <see cref="MenuKind.HiddenChips"/>: the hidden chips and their labels when the menu opened.
    /// A choice removes its chip by value, so chips that change while the menu is open never shift it.</summary>
    private GearChip[] _menuChips = [];
    private string[] _menuLabels = [];
    /// <summary>For <see cref="MenuKind.SavedFilter"/> (the one clicked) and <see cref="MenuKind.SavedMore"/> (the hidden
    /// ones): the filters when the menu opened, so a choice acts on them by value, never by a shifted index.</summary>
    private SavedFilter[] _menuSaved = [];
    /// <summary>For <see cref="MenuKind.Item"/>: the clicked row and whether Show in pack applies, as of the open.</summary>
    private GearItemView? _menuRow;
    private bool _menuCanShow;
    /// <summary>For <see cref="MenuKind.Item"/>: the selected ids in <see cref="Shown"/> order when the menu opened.</summary>
    private uint[] _menuSelection = [];
    /// <summary>For <see cref="MenuKind.Item"/>: the rows Add to salvage would take (from <see cref="_menuSelection"/>) and
    /// the labels listed, as of the open. The salvage entry is first and keyed by its label like the others.</summary>
    private List<GearItemView> _menuSalvage = [];
    private string? _menuSalvageLabel;
    private string[] _menuItems = [];
    /// <summary>For <see cref="MenuKind.Item"/>: the lock entry's label, the stack the Split entry would act on
    /// (single selected loose stack) and the merges Combine like stacks would run, as of the open.</summary>
    private string _menuLockLabel = LockLabel;
    private GearItemView? _menuSplit;
    private IReadOnlyList<GearMove> _menuCombine = [];
    /// <summary>For <see cref="MenuKind.Pack"/>: the pack clicked and the plans its entries would run, as of the open.</summary>
    private uint _menuPack;
    private IReadOnlyList<GearMove> _menuTidy = [];
    private GearRulePlan _menuRules = NoRules;
    private static readonly GearRulePlan NoRules = new([], new Dictionary<uint, int>());

    /// <summary>The sort menu's fields, in the order it lists them: Pack order (the default) first.</summary>
    private static readonly GearSortField[] SortFields =
        [GearSortField.Pack, GearSortField.Name, GearSortField.Type, GearSortField.Material, GearSortField.Workmanship,
         GearSortField.Value, GearSortField.Burden, GearSortField.Stack];

    internal UiMarkupContextMenu Menu => _menu;

    private void CreateMenu()
    {
        SetMenuScale();
        _menu.Opening = (x, y) => PickMenu(HitAt(x, y));
        _menu.ItemsSource = MenuItems;
        _menu.ItemsEnabledSource = MenuEnabled;
        _menu.Selected = ChooseMenuItem;
        AddChild(_menu);
        ContextMenu = _menu;
    }

    /// <summary>The menu draws at the deck's scale: its rows, insets and widths, in the deck's small face at its text size.</summary>
    private void SetMenuScale() => HudTextMenu.StyleMenu(_menu, _fonts, _scale, _textFactor);

    /// <summary>The kind a right-click (or a left-click opener's point inside its stud) asks for.</summary>
    private void PickMenu(GearDeckHit hit)
    {
        _menuKind = hit.Zone switch
        {
            GearZone.ChipMore when _g.HiddenChips > 0 => MenuKind.HiddenChips,
            GearZone.SortStud => MenuKind.Sort,
            GearZone.SavedFilter when hit.Index >= 0 && hit.Index < _b.Session.Settings.SavedFilters.Count => MenuKind.SavedFilter,
            GearZone.SavedMore when _g.HiddenSaved > 0 => MenuKind.SavedMore,
            GearZone.Item when hit.Index >= 0 && hit.Index < Shown.Count => MenuKind.Item,
            GearZone.SidePack when hit.Index >= 0 && hit.Index < _model.Packs.Count => MenuKind.Pack,
            // The title and the empty frame; items, chips and studs keep their own zones and menus.
            GearZone.Frame when _b.Text is not null => MenuKind.TextSize,
            _ => MenuKind.None,
        };
        _menuChips = [];
        _menuLabels = [];
        _menuSaved = [];
        _menuSelection = [];
        _menuSalvage = [];
        _menuSalvageLabel = null;
        _menuItems = ItemMenuItems;
        _menuSplit = null;
        _menuCombine = [];
        _menuTidy = [];
        _menuRules = NoRules;
        if (_menuKind == MenuKind.Pack)
        {
            _menuPack = _model.Packs[hit.Index].Id;
            _menuTidy = PackTidyPlanner.Plan(_menuPack, _projection.ForPack(_menuPack));
            _menuRules = RulePlan(null);
            _menuItems = PackMenuItems;
        }
        if (_menuKind == MenuKind.SavedFilter)
            _menuSaved = [_b.Session.Settings.SavedFilters[hit.Index]];
        else if (_menuKind == MenuKind.SavedMore)
            _menuSaved = _b.Session.Settings.SavedFilters.Skip(_g.SavedStuds.Count).ToArray();
        if (_menuKind == MenuKind.Item)
        {
            // An item outside the selection becomes the selection, as in a file list.
            GearItemView clicked = Shown[hit.Index];
            _menuRow = clicked;
            if (!_selection.Contains(clicked.Id)) SelectOnly(hit.Index);
            var picked = new List<uint>(_selection.Count);
            foreach (GearItemView row in Shown)
                if (_selection.Contains(row.Id)) picked.Add(row.Id);
            _menuSelection = picked.ToArray();
            _menuCanShow = ShowsResults && _selection.Count == 1 && !clicked.Worn && !clicked.IsPack;
            BuildItemMenu();
        }
        if (_menuKind != MenuKind.HiddenChips) return;
        int first = _g.Chips.Count;
        IReadOnlyList<string> labels = ChipLabels;
        int count = Math.Max(0, Math.Min(_query.Chips.Count, labels.Count) - first);
        _menuChips = _query.Chips.Skip(first).Take(count).ToArray();
        _menuLabels = labels.Skip(first).Take(count).ToArray();
    }

    /// <summary>While salvage is open the item menu starts with "Add N to salvage" and a separator; N is
    /// recomputed over the menu's own selection snapshot.</summary>
    private void BuildItemMenu()
    {
        var rows = new List<GearItemView>(_menuSelection.Length);
        foreach (GearItemView row in Shown)
            if (Array.IndexOf(_menuSelection, row.Id) >= 0) rows.Add(row);
        bool anyUnlocked = _menuSelection.Any(id => _b.Locks?.IsLocked(id) != true);
        _menuLockLabel = anyUnlocked ? LockLabel : UnlockLabel;
        _menuCombine = StackCombinePlanner.Plan(_projection.All, only: _menuSelection);
        _menuSplit = rows.Count == 1 && rows[0] is { Worn: false, IsPack: false, Stack: > 1 } one ? one : null;
        string[] organise = [SplitLabel, _menuLockLabel, CombineLikeLabel, UiMarkupContextMenu.Separator];
        if (_b.Salvage is not { IsOpen: true } salvage)
        {
            _menuItems = [.. organise, .. ItemMenuItems];
            return;
        }
        _menuSalvage = GearSalvageHandOff.Candidates(rows, salvage);
        _menuSalvageLabel = SalvageText(_menuSalvage.Count);
        _menuItems = [_menuSalvageLabel, UiMarkupContextMenu.Separator, .. organise, .. ItemMenuItems];
    }

    private IReadOnlyList<string> MenuItems()
    {
        switch (_menuKind)
        {
            case MenuKind.HiddenChips:
                return _menuLabels;
            case MenuKind.Sort:
                var labels = new string[SortFields.Length];
                for (int i = 0; i < labels.Length; i++)
                    labels[i] = (SortFields[i] == _query.Sort.Field ? "* " : "") + GearChipText.SortMenuLabel(SortFields[i]);
                return labels;
            case MenuKind.SavedFilter:
                return ["Delete"];
            case MenuKind.SavedMore:
                return _menuSaved.Select(f => f.Name).ToArray();
            case MenuKind.Item:
            case MenuKind.Pack:
                return _menuItems;
            case MenuKind.TextSize:
                return HudTextMenu.Labels(_b.Text!.Settings, HudDeck.Gear);
            default:
                return [];
        }
    }

    private const string SplitLabel = "Split...", LockLabel = "Lock", UnlockLabel = "Unlock", CombineLikeLabel = "Combine like stacks";
    private const string TidyLabel = "Tidy this pack", CombineStacksLabel = "Combine stacks", ApplyRulesLabel = "Apply pack rules", RulesLabel = "Pack rules...";
    private static readonly string[] PackMenuItems = [TidyLabel, CombineStacksLabel, ApplyRulesLabel, UiMarkupContextMenu.Separator, RulesLabel];
    private const string ShowInPackLabel = "Show in pack", SelectAllLabel = "Select all", ClearSelectionLabel = "Clear selection";
    private static readonly string[] ItemMenuItems = [ShowInPackLabel, UiMarkupContextMenu.Separator, SelectAllLabel, ClearSelectionLabel];

    private bool[] MenuEnabled()
    {
        if (_menuKind == MenuKind.TextSize) return HudTextMenu.EnabledFlags();
        int count = MenuItems().Count;
        var flags = new bool[count];
        Array.Fill(flags, true);
        if (_menuKind == MenuKind.Item)
        {
            for (int i = 0; i < count; i++)
                flags[i] = _menuItems[i] switch
                {
                    ShowInPackLabel => _menuCanShow,
                    SplitLabel => _menuSplit is not null,
                    LockLabel or UnlockLabel => _b.Locks is not null,
                    CombineLikeLabel => _menuCombine.Count > 0,
                    ClearSelectionLabel => _menuSelection.Length > 0,
                    string label when label == _menuSalvageLabel => _menuSalvage.Count > 0,
                    _ => true,
                };
        }
        else if (_menuKind == MenuKind.Pack)
        {
            flags[0] = _menuTidy.Count > 0;
            flags[1] = CombineMoves.Count > 0;
            flags[2] = _menuRules.Moves.Count > 0 || _menuRules.Full.Count > 0;   // a full pack still reports
            flags[3] = true;
            flags[4] = _b.OpenRules is not null;
        }
        return flags;
    }

    private void ChooseMenuItem(int index)
    {
        switch (_menuKind)
        {
            case MenuKind.HiddenChips when index >= 0 && index < _menuChips.Length:
                int at = IndexOfChip(_menuChips[index]);
                if (at >= 0) SetQuery(_query.Without(at));   // gone since the menu opened: nothing to remove
                break;
            case MenuKind.Sort when index >= 0 && index < SortFields.Length:
                SetQuery(_query with { Sort = GearSort.DefaultFor(SortFields[index]) });
                break;
            case MenuKind.SavedFilter when index == 0 && _menuSaved.Length == 1:
                string name = _menuSaved[0].Name;
                _b.Session.Update(s => s.WithoutSaved(name));
                SyncGeometry();
                break;
            case MenuKind.SavedMore when index >= 0 && index < _menuSaved.Length:
                ApplySaved(_menuSaved[index]);
                break;
            case MenuKind.Item when index >= 0 && index < _menuItems.Length:
                switch (_menuItems[index])
                {
                    case string label when label == _menuSalvageLabel:
                        if (_menuSalvage.Count > 0 && _b.Salvage is { IsOpen: true } salvage)
                            GearSalvageHandOff.AddAll(_menuSalvage, salvage);
                        break;
                    case SplitLabel when _menuSplit is not null: RequestSplit(_menuSplit); break;
                    case LockLabel when _b.Locks is not null: SetLocks(_menuSelection, true); break;
                    case UnlockLabel when _b.Locks is not null: SetLocks(_menuSelection, false); break;
                    case CombineLikeLabel when _menuCombine.Count > 0: Run("Combining", _menuCombine); break;
                    case ShowInPackLabel when _menuCanShow && _menuRow is not null: ShowInPack(_menuRow); break;
                    case SelectAllLabel: SelectAll(); break;
                    case ClearSelectionLabel: ClearSelection(); break;
                }
                break;
            case MenuKind.Pack when index >= 0 && index < _menuItems.Length:
                switch (_menuItems[index])
                {
                    case TidyLabel when _menuTidy.Count > 0: Run("Tidying", _menuTidy); break;
                    case CombineStacksLabel: CombineAll(); break;
                    case ApplyRulesLabel when _menuRules.Moves.Count > 0 || _menuRules.Full.Count > 0: ApplyRules(_menuRules); break;
                    case RulesLabel: _b.OpenRules?.Invoke(); break;
                }
                break;
            case MenuKind.TextSize when _b.Text is { } text:
                HudTextMenu.Choose(text, HudDeck.Gear, index);
                break;
        }
    }

    private int IndexOfChip(GearChip chip)
    {
        for (int i = 0; i < _query.Chips.Count; i++)
            if (_query.Chips[i].Equals(chip)) return i;
        return -1;
    }

    /// <summary>Opens the menu from a left-click on <paramref name="stud"/>, at a point just inside its
    /// bottom-left corner so <see cref="PickMenu"/> sees the same zone a right-click there would.</summary>
    private void OpenMenuAt(DeckRect stud)
    {
        System.Numerics.Vector2 origin = ScreenPosition;
        _menu.TryOpen((int)(origin.X + stud.X + 1f), (int)(origin.Y + stud.Y + stud.H - 1f));
    }
}
