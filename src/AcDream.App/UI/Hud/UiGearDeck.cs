using System.Globalization;
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
    Action Close,
    Action<string>? Log = null);

/// <summary>
/// The Gear Deck: the Modern HUD inventory and paper doll, one 300-point
/// column that resizes in height only. Each tick it rebuilds its
/// <see cref="GearInventoryModel"/> when the object table, the player or the
/// strength changed, and its <see cref="GearDeckGeometry"/> when the height or
/// the layout input changed. Drawing is in UiGearDeck.Draw.cs, input in
/// UiGearDeck.Input.cs.
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

    // Built with the model (Refresh's gated rebuild) so a frame does no table scans or string work.
    /// <summary>Each <see cref="GearSlotCatalog.Slots"/> entry's occupant id, or 0.</summary>
    private uint[] _slotOccupants = new uint[GearSlotCatalog.Slots.Count];
    private AetheriaUnlockState _aetheria;
    private string _burdenText = "";
    private string[] _badges = [];
    private string _footer = "";
    private uint _footerFor;
    /// <summary>Stack corner texts by stack size; cleared with the model.</summary>
    private readonly Dictionary<int, string> _stackTexts = [];
    /// <summary>List names fitted to their row, by item id; cleared with the model and the geometry.</summary>
    private readonly Dictionary<uint, string> _fittedNames = [];
    /// <summary>Empty-slot labels fitted to one or two lines, by slot; cleared with the geometry.</summary>
    private readonly (string First, string? Second)?[] _slotLabels = new (string, string?)?[GearSlotCatalog.Slots.Count];

    public UiGearDeck(GearDeckBindings bindings, DeckFonts? fonts, EmbeddedSvgSpriteCache? icons,
        Func<double>? clock = null)
    {
        _b = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _fonts = fonts;
        _icons = icons;
        _clock = clock ?? (() => Environment.TickCount64 / 1000.0);
        _actions = new LoggedGearItemActions(_b.Actions);
        _dispatcherTable = _b.Objects();
        _dispatcher = new GearDropDispatcher(_dispatcherTable, _b.PlayerGuid, _actions);
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

    /// <summary>The zone under the pointer, for hover backings and the tooltip.</summary>
    internal GearDeckHit Hover { get; private set; } = new(GearZone.None);

    /// <summary>The drop target being dragged over and whether it accepts, or null.</summary>
    internal (GearDeckHit Hit, bool Accept)? DropHighlight { get; private set; }

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
        TrackDropHighlight();
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
            RebuildCaches(table, player);
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
        FitMinHeight(input);
        if (input != _g.Input || MathF.Max(Height, GearDeckGeometry.MinHeight) != _g.Frame.H)
        {
            _g = GearDeckGeometry.Create(Height, input);
            _fittedNames.Clear();
            Array.Clear(_slotLabels);
        }
    }

    /// <summary>
    /// The window's minimum is the height that shows every side-pack cell for
    /// the fold state. A deck below it (unfolded, a pack slot gained, an older
    /// saved size) grows to it, no taller than its parent and kept inside it.
    /// </summary>
    private void FitMinHeight(GearDeckLayoutInput input)
    {
        float min = GearDeckGeometry.MinHeightFor(input);
        MinHeight = min;
        if (Height >= min) return;
        UiElement? parent = Parent;
        float target = parent is null ? min : MathF.Min(min, MathF.Max(Height, parent.Height));
        if (target <= Height) return;
        Height = target;
        if (parent is not null && Top + Height > parent.Height) Top = MathF.Max(0f, parent.Height - Height);
        ResetAnchorCapture();
        FindRoot()?.NotifyWindowResized(this);   // the layout records the new size
    }

    /// <summary>What the draw reads per model: slot occupants (a whole-table scan each),
    /// the Aetheria unlocks, the burden label and the side-pack badges.</summary>
    private void RebuildCaches(ClientObjectTable table, uint player)
    {
        IReadOnlyList<GearSlot> slots = GearSlotCatalog.Slots;
        for (int i = 0; i < slots.Count; i++)
            _slotOccupants[i] = _model.Packs.Count == 0 ? 0u : GearSlotCatalog.Occupant(table, player, slots[i]);
        _aetheria = AetheriaUnlocks.Read(table.Get(player));
        _burdenText = _model.BurdenPercent.ToString(CultureInfo.InvariantCulture) + "% burden";
        _badges = new string[_model.Packs.Count];
        for (int i = 0; i < _badges.Length; i++)
            _badges[i] = _model.Packs[i].IsMain ? "M" : (i + 1).ToString(CultureInfo.InvariantCulture);
        _footer = "";
        _footerFor = 0;
        _stackTexts.Clear();
        _fittedNames.Clear();
    }

    /// <summary>The occupant drawn in slot <paramref name="index"/> of <see cref="GearSlotCatalog.Slots"/>, or 0.</summary>
    internal uint SlotOccupant(int index) => _slotOccupants[index];

    private string CachedStackText(int stack)
    {
        if (stack <= 1) return "";
        if (!_stackTexts.TryGetValue(stack, out string? text)) _stackTexts[stack] = text = StackText(stack);
        return text;
    }

    /// <summary>The window is on screen: nothing to restore, the scroll is kept.</summary>
    public void OnShown() => ClearHover();

    /// <summary>The window left the screen: forget the hover and any drop highlight, keep the scroll.</summary>
    public void OnHidden() => ClearHover();

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
            if (selected != _footerFor || _footer.Length == 0)
            {
                _footer = BuildFooter(selected);
                _footerFor = selected;
            }
            return _footer;
        }
    }

    private string BuildFooter(uint selected)
    {
        for (int i = 0; i < _model.Packs.Count; i++)
        {
            GearPack p = _model.Packs[i];
            if (p.Id != selected) continue;
            string name = p.IsMain ? "Main" : $"Pack {i + 1}";
            return $"{name} · {_model.Contents(p.Id).Count} / {p.Capacity}";
        }
        return "";
    }

    /// <summary>A stack count for a cell corner: empty for one, thousands as "9k".</summary>
    internal static string StackText(int stack) => stack switch
    {
        <= 1 => "",
        < 1000 => stack.ToString(CultureInfo.InvariantCulture),
        _ => (stack / 1000).ToString(CultureInfo.InvariantCulture) + "k",
    };
}
