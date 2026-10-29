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

    internal UiMarkupContextMenu Menu => _menu;

    private void CreateMenu()
    {
        _menu.DatFont = _fonts?.Face(DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular);
        _menu.Opening = (x, y) => PickMenu(_g.HitAt(x, y));
        _menu.ItemsSource = MenuItems;
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
            _ => MenuKind.None,
        };
        _menuChips = [];
        _menuLabels = [];
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
            default:
                return [];
        }
    }

    private void ChooseMenuItem(int index)
    {
        switch (_menuKind)
        {
            case MenuKind.HiddenChips when index >= 0 && index < _menuChips.Length:
                int at = IndexOfChip(_menuChips[index]);
                if (at >= 0) SetQuery(_query.Without(at));   // gone since the menu opened: nothing to remove
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
