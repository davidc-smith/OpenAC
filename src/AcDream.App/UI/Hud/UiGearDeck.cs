using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

/// <summary>What the Gear Deck reads from the game and the session it saves its view state through.</summary>
internal sealed record GearDeckBindings(
    Func<ClientObjectTable> Objects,
    Func<uint> PlayerGuid,
    Func<int?> Strength,
    Func<ItemType, uint, uint, uint, uint, uint> ResolveIcon,
    Func<ItemType, uint, uint, uint, uint, uint> ResolveDragIcon,
    Func<ClientObject, string> DisplayName,
    GearDeckSession Session,
    IGearItemActions Actions,
    Func<uint, bool> IsPending,
    Action Close);

/// <summary>
/// The Gear Deck: the Modern HUD inventory and paper doll, one 300-point
/// column that resizes in height only. Each tick it rebuilds its
/// <see cref="GearInventoryModel"/> when the object table, the player or the
/// strength changed, and its <see cref="GearDeckGeometry"/> when the height or
/// the layout input changed. Drawing is in UiGearDeck.Draw.cs.
/// </summary>
internal sealed partial class UiGearDeck : UiElement
{
    /// <summary>The alpha of pending items and locked Aetheria slots.</summary>
    internal const float DimAlpha = 0.45f;

    private readonly GearDeckBindings _b;
    private readonly DeckFonts? _fonts;
    /// <summary>Held for later plans (Figure view, tool studs); plan 1 draws its glyphs as lines.</summary>
    private readonly EmbeddedSvgSpriteCache? _icons;
    private GearInventoryModel _model = GearInventoryModel.Empty;
    private GearDeckGeometry _g;
    private int _scrollRow;
    private ulong _lastRevision;
    private ClientObjectTable? _lastTable;
    private uint _lastPlayer;
    private int? _lastStrength;
    private bool _built;

    public UiGearDeck(GearDeckBindings bindings, DeckFonts? fonts, EmbeddedSvgSpriteCache? icons)
    {
        _b = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _fonts = fonts;
        _icons = icons;
        Width = GearDeckGeometry.Width;
        Height = GearDeckGeometry.DefaultHeight;
        MinHeight = GearDeckGeometry.MinHeight;
        Resizable = true;
        ResizeX = false;
        ResizeY = true;
        _g = GearDeckGeometry.Create(Height, LayoutInput());
    }

    /// <summary>How the runtime mounts the deck: imported chrome, 300 wide, resized from the top or bottom, hidden.</summary>
    internal static RetailWindowFrame.Options MountOptions(float left, float top) => new()
    {
        WindowName = WindowNames.GearDeck,
        Chrome = RetailWindowChrome.Imported,
        Left = left,
        Top = top,
        Resizable = true,
        ResizeX = false,
        ResizeY = true,
        ResizableEdges = ResizeEdges.Top | ResizeEdges.Bottom,
        ConstrainResizeToParent = true,
        MinWidth = GearDeckGeometry.Width,
        MaxWidth = GearDeckGeometry.Width,
        MinHeight = GearDeckGeometry.MinHeight,
        Visible = false,
        ContentClickThrough = false,
    };

    internal GearDeckGeometry Geometry => _g;
    internal GearInventoryModel Model => _model;
    internal GearDeckBindings Bindings => _b;

    /// <summary>The first shown grid or list row.</summary>
    internal int ScrollRow => _scrollRow;

    /// <summary>The zone under the pointer, for hover backings; Task 8's input sets it.</summary>
    internal GearDeckHit Hover { get; set; } = new(GearZone.None);

    /// <summary>The drop target being dragged over and whether it accepts, or null.</summary>
    internal (GearDeckHit Hit, bool Accept)? DropHighlight { get; set; }

    /// <summary>The selected pack when it still exists, else the main pack (the player).</summary>
    internal uint SelectedPackId
    {
        get
        {
            uint selected = _b.Session.Settings.SelectedPack;
            foreach (GearPack p in _model.Packs)
                if (p.Id == selected) return selected;
            return _model.Packs.Count > 0 ? _model.Packs[0].Id : 0u;
        }
    }

    /// <summary>The loose items of the selected pack, in slot order.</summary>
    internal IReadOnlyList<ClientObject> Items => _model.Contents(SelectedPackId);

    /// <summary>The rows the selected view holds for <see cref="Items"/>.</summary>
    private int ContentRows => _b.Session.Settings.View == GearPackView.List
        ? Items.Count
        : (Items.Count + GearDeckGeometry.Columns - 1) / GearDeckGeometry.Columns;

    private int VisibleRows => _b.Session.Settings.View == GearPackView.List ? _g.VisibleListRows : _g.VisibleGridRows;

    /// <summary>The last row that can be first: the rows past what is visible.</summary>
    internal int MaxScrollRow => Math.Max(0, ContentRows - VisibleRows);

    /// <summary>Shows <paramref name="row"/> first, clamped to <c>[0, MaxScrollRow]</c>.</summary>
    internal void ScrollTo(int row)
    {
        _scrollRow = Math.Clamp(row, 0, MaxScrollRow);
        SyncGeometry();
    }

    protected override void OnTick(double deltaSeconds)
    {
        _b.Session.Tick();
        Refresh();
    }

    /// <summary>Rebuilds the model when the table, player or strength changed, then the geometry when needed.</summary>
    internal void Refresh()
    {
        ClientObjectTable table = _b.Objects();
        uint player = _b.PlayerGuid();
        int? strength = _b.Strength();
        if (!_built || !ReferenceEquals(table, _lastTable) || table.MutationRevision != _lastRevision
            || player != _lastPlayer || strength != _lastStrength)
        {
            _model = GearInventoryModel.Build(table, player, strength);
            _lastTable = table;
            _lastRevision = table.MutationRevision;
            _lastPlayer = player;
            _lastStrength = strength;
            _built = true;
        }
        SyncGeometry();
        // Items can go away under the scroll; keep it in range.
        int clamped = Math.Clamp(_scrollRow, 0, MaxScrollRow);
        if (clamped != _scrollRow)
        {
            _scrollRow = clamped;
            SyncGeometry();
        }
    }

    private GearDeckLayoutInput LayoutInput() => new(
        _b.Session.Settings.Folded,
        _b.Session.Settings.View == GearPackView.List,
        _model.Worn.Count,
        Items.Count,
        _model.SidePackSlots,
        _scrollRow);

    private void SyncGeometry()
    {
        GearDeckLayoutInput input = LayoutInput();
        if (input != _g.Input || MathF.Max(Height, GearDeckGeometry.MinHeight) != _g.Frame.H)
            _g = GearDeckGeometry.Create(Height, input);
    }

    /// <summary>The window is on screen: nothing to restore, the scroll is kept.</summary>
    public void OnShown() => ClearHover();

    /// <summary>The window left the screen: forget the hover and any drop highlight, keep the scroll.</summary>
    public void OnHidden() => ClearHover();

    private void ClearHover()
    {
        Hover = new GearDeckHit(GearZone.None);
        DropHighlight = null;
    }

    internal override void ConstrainResize(ResizeEdges edges, ref float width, ref float height)
    {
        width = GearDeckGeometry.Width;
        height = MathF.Max(height, MinHeight);
    }

    /// <summary>"Main · 96 / 102" or "Pack 3 · 10 / 24": the selected pack, its item count and capacity.</summary>
    internal string FooterText
    {
        get
        {
            uint selected = SelectedPackId;
            for (int i = 0; i < _model.Packs.Count; i++)
            {
                GearPack p = _model.Packs[i];
                if (p.Id != selected) continue;
                string name = p.IsMain ? "Main" : $"Pack {i + 1}";
                return $"{name} · {_model.Contents(p.Id).Count} / {p.Capacity}";
            }
            return "";
        }
    }

    /// <summary>A stack count for a cell corner: empty for one, thousands as "9k".</summary>
    internal static string StackText(int stack) => stack switch
    {
        <= 1 => "",
        < 1000 => stack.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => (stack / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture) + "k",
    };
}
