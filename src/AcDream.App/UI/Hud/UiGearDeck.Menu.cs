using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

/// <summary>The deck's one popup menu. What it lists depends on where it was opened.</summary>
internal sealed partial class UiGearDeck
{
    /// <summary>What the open menu lists; later plans fill in Sort, SavedFilter, SavedMore and Item.</summary>
    private enum MenuKind { None, HiddenChips, Sort, SavedFilter, SavedMore, Item }

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

    /// <summary>The sort menu's fields, in the order it lists them.</summary>
    private static readonly GearSortField[] SortFields =
        [GearSortField.Name, GearSortField.Type, GearSortField.Material, GearSortField.Workmanship,
         GearSortField.Value, GearSortField.Burden, GearSortField.Stack];

    internal UiMarkupContextMenu Menu => _menu;

    private void CreateMenu()
    {
        _menu.DatFont = _fonts?.Face(DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular);
        _menu.Opening = (x, y) => PickMenu(_g.HitAt(x, y));
        _menu.ItemsSource = MenuItems;
        _menu.ItemsEnabledSource = MenuEnabled;
        _menu.Selected = ChooseMenuItem;
        AddChild(_menu);
        ContextMenu = _menu;
    }

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
            _ => MenuKind.None,
        };
        _menuChips = [];
        _menuLabels = [];
        _menuSaved = [];
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
            _menuCanShow = ShowsResults && _selection.Count == 1 && !clicked.Worn && !clicked.IsPack;
        }
        if (_menuKind != MenuKind.HiddenChips) return;
        int first = _g.Chips.Count;
        IReadOnlyList<string> labels = ChipLabels;
        int count = Math.Max(0, Math.Min(_query.Chips.Count, labels.Count) - first);
        _menuChips = _query.Chips.Skip(first).Take(count).ToArray();
        _menuLabels = labels.Skip(first).Take(count).ToArray();
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
                    labels[i] = (SortFields[i] == _query.Sort.Field ? "* " : "") + GearChipText.SortLabel(SortFields[i]);
                return labels;
            case MenuKind.SavedFilter:
                return ["Delete"];
            case MenuKind.SavedMore:
                return _menuSaved.Select(f => f.Name).ToArray();
            case MenuKind.Item:
                return ItemMenuItems;
            default:
                return [];
        }
    }

    private static readonly string[] ItemMenuItems = ["Show in pack", UiMarkupContextMenu.Separator, "Select all", "Clear selection"];

    private bool[] MenuEnabled()
    {
        int count = MenuItems().Count;
        var flags = new bool[count];
        Array.Fill(flags, true);
        if (_menuKind == MenuKind.Item)
        {
            flags[0] = _menuCanShow;
            flags[3] = _selection.Count > 0;
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
            case MenuKind.Item when index == 0 && _menuCanShow && _menuRow is not null:
                ShowInPack(_menuRow);
                break;
            case MenuKind.Item when index == 2:
                SelectAll();
                break;
            case MenuKind.Item when index == 3:
                ClearSelection();
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
