namespace AcDream.App.UI.Hud;

/// <summary>The deck's one popup menu. What it lists depends on where it was opened.</summary>
internal sealed partial class UiGearDeck
{
    /// <summary>What the open menu lists; later plans fill in Sort, SavedFilter, SavedMore and Item.</summary>
    private enum MenuKind { None, HiddenChips, Sort, SavedFilter, SavedMore, Item }

    private readonly UiMarkupContextMenu _menu = new() { ThemePalette = ChatDeckPalette.Menu };
    private MenuKind _menuKind;
    /// <summary>For <see cref="MenuKind.HiddenChips"/>: the index of the first hidden chip when the menu opened.</summary>
    private int _menuFirstChip;

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
        _menuFirstChip = _g.Chips.Count;
    }

    private IReadOnlyList<string> MenuItems()
    {
        switch (_menuKind)
        {
            case MenuKind.HiddenChips:
            {
                IReadOnlyList<string> labels = ChipLabels;
                if (_menuFirstChip >= labels.Count) return [];
                return labels.Skip(_menuFirstChip).ToArray();
            }
            default:
                return [];
        }
    }

    private void ChooseMenuItem(int index)
    {
        switch (_menuKind)
        {
            case MenuKind.HiddenChips:
                SetQuery(_query.Without(_menuFirstChip + index));
                break;
        }
    }

    /// <summary>Opens the menu from a left-click on <paramref name="stud"/>, at a point just inside its
    /// bottom-left corner so <see cref="PickMenu"/> sees the same zone a right-click there would.</summary>
    private void OpenMenuAt(DeckRect stud)
    {
        System.Numerics.Vector2 origin = ScreenPosition;
        _menu.TryOpen((int)(origin.X + stud.X + 1f), (int)(origin.Y + stud.Y + stud.H - 1f));
    }
}
