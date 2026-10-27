using AcDream.App.UI.Layout;
using AcDream.Core.Spells;
using AcDream.UI.Abstractions.Input;

namespace AcDream.App.UI.Hud;

/// <summary>A spell lifted from a Spell Deck slot. Nothing changes until it lands.</summary>
public sealed record SpellDeckDragPayload(int Tab, uint SpellId);

/// <summary>
/// The Spell Deck: one element that draws a <see cref="SpellDeckController"/>'s
/// rows in the Command Deck's style and turns clicks, double-clicks, hovers
/// and tab-chip clicks into controller calls. Slots, the item socket and the
/// chips take clicks; a press anywhere else on the frame drags the window.
/// Filled slots are drag sources: nothing changes when a drag starts, only
/// when it lands (on the deck, or outside every window). Right-click opens
/// the row/slot menu.
/// </summary>
internal sealed partial class UiSpellDeck : UiElement
{
    private readonly SpellDeckController _controller;
    private readonly Func<uint, uint> _spellIcon;
    private readonly Func<uint, SpellMetadata?> _spellInfo;
    private readonly Func<InputAction, string> _keyLabel;
    private readonly Func<uint?> _pendingSpell;
    private readonly Func<double> _clock;
    private readonly Func<bool> _isLocked;
    private readonly Action _toggleClickCasts;
    private readonly Action<bool> _setAttached;
    private readonly DeckFonts? _fonts;
    private readonly Func<uint, string?> _itemName;
    private readonly UiMarkupContextMenu _menu;
    private SpellDeckGeometry _geometry;
    /// <summary>The geometry when the current resize drag started; null between drags.</summary>
    private SpellDeckGeometry? _resizeFrom;
    private SpellDeckHit _hover = NoHit;
    /// <summary>When the pointer reached <see cref="_hover"/>; the tooltip waits <see cref="UiCommandDeck.TooltipDwell"/> from here.</summary>
    private double _hoverSince;
    /// <summary>Set by a press; keeps the tooltip hidden until the pointer moves to another zone.</summary>
    private bool _tooltipSuppressed;
    /// <summary>The zone under the last hit test; decides click ownership.</summary>
    private SpellDeckHit _hit = NoHit;

    private static readonly SpellDeckHit NoHit = new(SpellDeckZone.None, -1, -1);

    public UiSpellDeck(
        SpellDeckController controller,
        Func<uint, uint> spellIcon,
        Func<uint, SpellMetadata?> spellInfo,
        Func<InputAction, string> keyLabel,
        Func<uint?> pendingSpell,
        Func<double> clock,
        Func<bool> isLocked,
        Action toggleClickCasts,
        Action<bool> setAttached,
        DeckFonts? fonts = null,
        Func<uint, string?>? itemName = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _spellIcon = spellIcon ?? throw new ArgumentNullException(nameof(spellIcon));
        _spellInfo = spellInfo ?? throw new ArgumentNullException(nameof(spellInfo));
        _keyLabel = keyLabel ?? throw new ArgumentNullException(nameof(keyLabel));
        _pendingSpell = pendingSpell ?? throw new ArgumentNullException(nameof(pendingSpell));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _isLocked = isLocked ?? throw new ArgumentNullException(nameof(isLocked));
        _toggleClickCasts = toggleClickCasts ?? throw new ArgumentNullException(nameof(toggleClickCasts));
        _setAttached = setAttached ?? throw new ArgumentNullException(nameof(setAttached));
        _fonts = fonts;
        _itemName = itemName ?? (_ => null);
        _geometry = SpellDeckGeometry.Create(1f, 1f, controller.RowCount);
        Width = _geometry.Width;
        Height = _geometry.Height;
        MinWidth = MinWindowWidth;
        MaxWidth = MaxWindowWidth;
        MinHeight = MinWindowHeight;
        MaxHeight = MaxWindowHeight;
        Resizable = true;
        ResizableEdges = ResizeEdges.Right;

        _menu = new UiMarkupContextMenu
        {
            DatFont = fonts?.Small,
            ItemsSource = () => _menuItems.ConvertAll(i => i.Text),
            ItemsEnabledSource = () => _menuItems.ConvertAll(i => i.Enabled),
            Selected = SelectMenuItem,
            Opening = (x, y) => MenuItemsAt(x, y),
        };
        AddChild(_menu);
        ContextMenu = _menu;
    }

    // Every size the deck can have: 1-10 rows at any scale and stretch.
    private const float MinWindowWidth = SpellDeckGeometry.BaseWidth * DeckGeometry.MinStretch * DeckGeometry.MinScale;
    private const float MaxWindowWidth = SpellDeckGeometry.BaseWidth * DeckGeometry.MaxStretch * DeckGeometry.MaxScale;
    private static float MinWindowHeight => SpellDeckGeometry.BaseHeight(SpellDeckGeometry.MinRows) * DeckGeometry.MinScale;
    private static float MaxWindowHeight => SpellDeckGeometry.BaseHeight(SpellDeckGeometry.MaxRows) * DeckGeometry.MaxScale;

    /// <summary>How the runtime mounts the Spell Deck: imported chrome, resizable from the right only, hidden until magic combat.</summary>
    internal static RetailWindowFrame.Options MountOptions(float left, float top) => new()
    {
        WindowName = WindowNames.SpellDeck,
        Chrome = RetailWindowChrome.Imported,
        Left = left,
        Top = top,
        Resizable = true,
        ResizeX = true,
        ResizeY = true,
        ResizableEdges = ResizeEdges.Right,
        ConstrainResizeToParent = true,
        MinWidth = MinWindowWidth,
        MaxWidth = MaxWindowWidth,
        MinHeight = MinWindowHeight,
        MaxHeight = MaxWindowHeight,
        Visible = false,
        ContentClickThrough = false,
    };

    internal override ResizeEdges FilterResizeEdges(ResizeEdges hit) => hit switch
    {
        ResizeEdges.Right
            or (ResizeEdges.Right | ResizeEdges.Top)
            or (ResizeEdges.Right | ResizeEdges.Bottom) => hit,
        _ => ResizeEdges.None,
    };

    internal override void ConstrainResize(ResizeEdges edges, ref float width, ref float height)
    {
        bool corner = (edges & (ResizeEdges.Top | ResizeEdges.Bottom)) != 0;
        SpellDeckGeometry from = _resizeFrom ??= _geometry;
        SpellDeckGeometry g = corner ? from.WithCornerSize(width, height) : from.WithWidth(width);
        _geometry = g.WithStretch(MathF.Max(g.Stretch, MinStretchNow));
        width = _geometry.Width;
        height = _geometry.Height;
    }

    /// <summary>The narrowest the deck may be: its rows must hold the fullest tab.</summary>
    private float MinStretchNow => SpellDeckGeometry.StretchFor(_controller.MinSlotsPerRow);

    public override bool HandlesClick => IsControl(_hit.Zone);
    public override bool ReceivesHoverMouseMove => true;

    internal SpellDeckGeometry Geometry => _geometry;
    internal SpellDeckController Controller => _controller;

    /// <summary>Sets the deck to <paramref name="geometry"/>'s size and geometry.</summary>
    internal void ApplyGeometry(SpellDeckGeometry geometry)
    {
        _geometry = geometry;
        Width = geometry.Width;
        Height = geometry.Height;
    }

    /// <summary>Re-checks the outline and fits the height to the row count.
    /// A size set from outside (a restore or a resize) moves the geometry to it.</summary>
    internal void Refresh()
    {
        if (FindRoot()?.ActiveResizeEdges is null or ResizeEdges.None)
            _resizeFrom = null;
        _controller.Tick();
        int rows = _controller.RowCount;
        if (Width != _geometry.Width || Height != _geometry.Height)
        {
            SpellDeckGeometry g = SpellDeckGeometry.FromSize(Width, Height, _geometry.Rows).WithRows(rows);
            ApplyGeometry(g.WithStretch(MathF.Max(g.Stretch, MinStretchNow)));
        }
        else if (rows != _geometry.Rows)
            ApplyGeometry(_geometry.WithRows(rows));
    }

    protected override void OnTick(double deltaSeconds)
    {
        Refresh();
        TrackDropHighlight();
    }

    internal bool HitTestLocal(float x, float y) => OnHitTest(x, y);

    protected override bool OnHitTest(float localX, float localY)
    {
        _hit = _geometry.HitAt(localX, localY);
        return _geometry.Frame.Contains(localX, localY);
    }

    /// <summary>Zones that take a click; the rest of the frame drags the window.</summary>
    internal static bool IsControl(SpellDeckZone zone) =>
        zone is SpellDeckZone.Slot or SpellDeckZone.Item or SpellDeckZone.TabChip;

    // ── Tooltip ───────────────────────────────────────────────────

    /// <summary>The tooltip the deck is showing now, or null (the deck times its own, as the Command Deck does).</summary>
    internal HudTooltipContent? VisibleTooltip =>
        _hover.Zone == SpellDeckZone.None || _tooltipSuppressed
            || _clock() - _hoverSince < UiCommandDeck.TooltipDwell
            ? null
            : TooltipFor(_hover);

    private readonly HudTooltipLayoutCache _tooltipLayout = new();

    protected override bool ExpandsClipForPopup => true;

    protected override void OnDrawOverlay(UiRenderContext ctx)
    {
        HudTooltipContent? tip = VisibleTooltip;
        if (tip is null) return;
        ctx.PushAlphaAbsolute(1f);
        try
        {
            HudTooltip.Draw(ctx, tip, HudTooltipStyle.Card, RectFor(_hover),
                ScreenPosition, FindRoot()?.Width, _tooltipLayout, _fonts);
        }
        finally { ctx.PopAlpha(); }
    }

    private DeckRect RectFor(SpellDeckHit hit) => hit.Zone switch
    {
        SpellDeckZone.Item => _geometry.ItemSlot,
        SpellDeckZone.TabChip => _geometry.TabChip(hit.Row),
        SpellDeckZone.Slot => _geometry.Slot(hit.Row, hit.Slot),
        _ => _geometry.Frame,
    };

    private const string ChipHint = "Click for the next tab, right-click for more";

    internal HudTooltipContent? TooltipFor(SpellDeckHit hit)
    {
        string castHint = _controller.ClickCasts ? "Click to cast" : "Double-click to cast";
        switch (hit.Zone)
        {
            case SpellDeckZone.TabChip when hit.Row < _controller.RowCount:
                return new($"Tab {_controller.Tab + 1}", null, null, ChipHint);
            case SpellDeckZone.Item:
                if (_controller.ItemSpell is not { } item)
                    return new("Item spell", null, "No wielded item has a spell.", null);
                return _itemName(item.ItemId) is { Length: > 0 } name
                    ? new(ItemSpellText(name, SpellName(item.SpellId)), null, null, castHint)
                    : new("Item spell", null, SpellName(item.SpellId), castHint);
            case SpellDeckZone.Slot when SlotSpell(hit.Row, hit.Slot) is uint id:
                string key = KeyLabel(hit.Row, hit.Slot);
                return new(SpellName(id), key.Length == 0 ? null : key, _spellInfo(id)?.School, castHint);
            default:
                return null;
        }
    }

    /// <summary>How the item spell is named in its tooltip and on the Command Deck's CASTING row.</summary>
    internal static string ItemSpellText(string itemName, string spellName) => $"{itemName} ({spellName})";

    private string SpellName(uint id) => _spellInfo(id)?.Name is { Length: > 0 } name ? name : $"Spell {id}";

    /// <summary>The key label for a slot; slots past the twelfth have no action.</summary>
    private string KeyLabel(int row, int slot) =>
        row < SpellBarActions.Rows && slot < SpellBarActions.SlotsPerRow
            ? _keyLabel(SpellBarActions.ForSlot(row, slot))
            : string.Empty;

    /// <summary>The spell shown in a slot, or null when the slot is empty or the row is not shown.</summary>
    private uint? SlotSpell(int row, int slot) => _controller.SlotSpell(row, slot);

    private void SetHover(SpellDeckHit hit)
    {
        if (!IsControl(hit.Zone)) hit = NoHit;
        if (hit == _hover) return;
        _hover = hit;
        _hoverSince = _clock();
        _tooltipSuppressed = false;
    }

    /// <summary>Called when the deck is hidden: forgets the hover, so a deck shown again waits out a fresh dwell.</summary>
    public void OnHidden()
    {
        _resizeFrom = null;
        SetHover(NoHit);
        _tooltipSuppressed = false;
    }

    // ── Input ─────────────────────────────────────────────────────

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.MouseDown:
                _tooltipSuppressed = true;
                _press = _geometry.HitAt(e.Data1, e.Data2);
                return false;   // the window drag or the click has the press
            case UiEventType.Click:
                return Click(_geometry.HitAt(e.Data1, e.Data2));
            case UiEventType.DoubleClick:
                return DoubleClick(_geometry.HitAt(e.Data1, e.Data2));
            case UiEventType.MouseMove:
                SetHover(_geometry.HitAt(e.Data1, e.Data2));
                return _hover.Zone != SpellDeckZone.None;
            case UiEventType.HoverLeave:
                SetHover(NoHit);
                return true;
            case UiEventType.DragEnter:
                _dropPayload = e.Payload;
                SetDropHighlight(e.Data1, e.Data2);
                return false;
            case UiEventType.DragOver:   // the leave
                ClearDropHighlight();
                return false;
            case UiEventType.DropReleased:
                ClearDropHighlight();
                return DropReleased(e.Payload, e.Data1, e.Data2);
            default:
                return false;
        }
    }

    private bool Click(SpellDeckHit hit)
    {
        switch (hit.Zone)
        {
            case SpellDeckZone.Slot:
                _controller.Click(hit.Row, hit.Slot);
                return true;
            case SpellDeckZone.Item:
                _controller.ClickItem();
                return true;
            case SpellDeckZone.TabChip when hit.Row < _controller.RowCount:
                _controller.NextTab();
                return true;
            default:
                return false;
        }
    }

    private bool DoubleClick(SpellDeckHit hit)
    {
        switch (hit.Zone)
        {
            case SpellDeckZone.Slot:
                _controller.DoubleClick(hit.Row, hit.Slot);
                return true;
            case SpellDeckZone.Item:
                _controller.DoubleClickItem();
                return true;
            case SpellDeckZone.TabChip:
                return true;   // both clicks already stepped the tab
            default:
                return false;
        }
    }

    // ── Drag and drop ─────────────────────────────────────────────

    /// <summary>Where the last press landed; the drag lifts the slot under it.</summary>
    private SpellDeckHit _press = NoHit;
    /// <summary>The payload of a drag over the deck (DragEnter to leave or drop); null otherwise.</summary>
    private object? _dropPayload;

    /// <summary>The slot a drag over the deck would land on, and whether the controller would accept it.</summary>
    internal (int Row, int Slot, bool Accept)? DropHighlight { get; private set; }

    // _hit is the hit test of the press itself: the root asks before it sends the MouseDown.
    public override bool IsDragSource => _hit.Zone == SpellDeckZone.Slot && !_isLocked();

    public override object? GetDragPayload()
    {
        if (_isLocked() || _press.Zone != SpellDeckZone.Slot) return null;
        if (SlotSpell(_press.Row, _press.Slot) is not uint id) return null;
        return new SpellDeckDragPayload(_controller.Tab, id);
    }

    public override (uint tex, int w, int h)? GetDragGhost() =>
        GetDragPayload() is SpellDeckDragPayload p ? (_spellIcon(p.SpellId), 32, 32) : null;

    /// <summary>A payload the deck takes as a drop, as the controller's drag source; null for any other.</summary>
    private static SpellDeckDragSource? SourceOf(object? payload) => payload switch
    {
        SpellDeckDragPayload p => new SpellDeckDragSource(p.Tab, p.SpellId),
        SpellbookShortcutDragPayload s => SpellDeckDragSource.FromSpellbook(s.SpellId),
        _ => null,
    };

    /// <summary>The (row, slot) a drop at a point lands on: a slot, or the nearest slot to its
    /// left when the point is in the row's band between or past its slots; null anywhere else.</summary>
    private (int Row, int Slot)? DropTargetAt(float x, float y)
    {
        SpellDeckHit hit = _geometry.HitAt(x, y);
        if (hit.Zone == SpellDeckZone.Slot && hit.Row < _controller.RowCount)
            return (hit.Row, hit.Slot);
        if (hit.Zone != SpellDeckZone.Frame) return null;
        for (int row = 0; row < _geometry.Rows && row < _controller.RowCount; row++)
        {
            DeckRect first = _geometry.Slot(row, 0);
            if (y >= first.Y && y < first.Y + first.H && x >= first.X)
            {
                int slot = (int)((x - first.X) / (_geometry.Slot(row, 1).X - first.X));
                return (row, Math.Min(slot, _geometry.SlotsPerRow - 1));
            }
        }
        return null;
    }

    private bool DropReleased(object? payload, float x, float y)
    {
        if (SourceOf(payload) is not { } source) return false;
        if (DropTargetAt(x, y) is { } at) _controller.DropOnSlot(source, at.Row, at.Slot);
        return true;
    }

    /// <summary>The runtime calls this for a drag released outside every window:
    /// a slot dragged off the deck leaves its tab. Any other payload is ignored.</summary>
    internal void OnDragReleasedOutsideUi(object payload)
    {
        if (payload is SpellDeckDragPayload p)
            _controller.DropOutside(new SpellDeckDragSource(p.Tab, p.SpellId));
    }

    private void SetDropHighlight(float x, float y)
    {
        if (SourceOf(_dropPayload) is not { } source || DropTargetAt(x, y) is not { } at
            || at.Slot >= _geometry.SlotsPerRow)
        {
            DropHighlight = null;
            return;
        }
        DropHighlight = (at.Row, at.Slot, _controller.CanDropOnSlot(source, at.Row, at.Slot));
    }

    private void ClearDropHighlight()
    {
        _dropPayload = null;
        DropHighlight = null;
    }

    /// <summary>The root sends DragEnter once per element, so the highlight follows the
    /// pointer from slot to slot here; a drag that ended without a leave clears it.</summary>
    private void TrackDropHighlight()
    {
        if (_dropPayload is null) return;
        if (FindRoot() is not { } root || root.DragSource is null)
        {
            ClearDropHighlight();
            return;
        }
        System.Numerics.Vector2 origin = ScreenPosition;
        SetDropHighlight(root.MouseX - origin.X, root.MouseY - origin.Y);
    }

    // ── Right-click menu ──────────────────────────────────────────

    private List<(string Text, bool Enabled)> _menuItems = [];
    private readonly List<Action?> _menuActions = [];

    /// <summary>Builds the menu for a right click at a point (owner coordinates):
    /// the slot items over a filled slot, then the deck items over a slot or tab chip.
    /// Empty anywhere else, so no menu opens. "-" is a separator.</summary>
    internal IReadOnlyList<(string Text, bool Enabled)> MenuItemsAt(float x, float y)
    {
        _menuItems = [];
        _menuActions.Clear();
        SpellDeckHit hit = _geometry.HitAt(x, y);
        if (hit.Zone is not (SpellDeckZone.Slot or SpellDeckZone.TabChip) || hit.Row >= _controller.RowCount)
            return _menuItems;

        if (hit.Zone == SpellDeckZone.Slot && SlotSpell(hit.Row, hit.Slot) is uint spellId)
        {
            // The spell, not the slot: the favourites can change while the menu is
            // open, and the item then acts on that spell only if the deck still shows it.
            int tab = _controller.Tab;
            Add("Examine spell", true, () => _controller.Examine(tab, spellId));
            Add("Remove from tab", true, () => _controller.RemoveFromTab(tab, spellId));
            Separator();
        }
        Add("Add row", _controller.CanAddRow(), _controller.AddRow);
        Add("Remove row", _controller.CanRemoveRow, _controller.RemoveRow);
        Separator();
        // Until the tab strip (Task 18): one item per tab that exists.
        for (int tab = 0; tab < _controller.TabCount; tab++)
        {
            int t = tab;
            Add(tab == _controller.Tab ? $"Show tab {tab + 1} (shown)" : $"Show tab {tab + 1}", true,
                () => _controller.ShowTab(t));
        }
        Separator();
        Add(_controller.ClickCasts ? "Click casts: on" : "Click casts: off", true, _toggleClickCasts);
        Separator();
        bool attached = _controller.Attached;
        Add(attached ? "Detach from deck" : "Attach to deck", true, () => _setAttached(!attached));
        return _menuItems;

        void Add(string text, bool enabled, Action action)
        {
            _menuItems.Add((text, enabled));
            _menuActions.Add(action);
        }

        void Separator()
        {
            _menuItems.Add((UiMarkupContextMenu.Separator, false));
            _menuActions.Add(null);
        }
    }

    /// <summary>Runs menu item <paramref name="index"/> from the last <see cref="MenuItemsAt"/>; separators and disabled items do nothing.</summary>
    internal void SelectMenuItem(int index)
    {
        if (index < 0 || index >= _menuActions.Count || !_menuItems[index].Enabled) return;
        _menuActions[index]?.Invoke();
    }
}
