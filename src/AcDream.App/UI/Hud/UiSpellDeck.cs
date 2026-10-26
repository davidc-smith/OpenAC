using AcDream.App.UI.Layout;
using AcDream.Core.Spells;
using AcDream.UI.Abstractions.Input;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The Spell Deck: one element that draws a <see cref="SpellDeckController"/>'s
/// rows in the Command Deck's style and turns clicks, double-clicks, hovers
/// and tab-chip clicks into controller calls. Slots, the item socket and the
/// chips take clicks; a press anywhere else on the frame drags the window.
/// </summary>
internal sealed partial class UiSpellDeck : UiElement
{
    private readonly SpellDeckController _controller;
    private readonly Func<uint, uint> _spellIcon;
    private readonly Func<uint, SpellMetadata?> _spellInfo;
    private readonly Func<InputAction, string> _keyLabel;
    private readonly Func<uint?> _pendingSpell;
    private readonly Func<double> _clock;
    private readonly DeckFonts? _fonts;
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
        DeckFonts? fonts = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _spellIcon = spellIcon ?? throw new ArgumentNullException(nameof(spellIcon));
        _spellInfo = spellInfo ?? throw new ArgumentNullException(nameof(spellInfo));
        _keyLabel = keyLabel ?? throw new ArgumentNullException(nameof(keyLabel));
        _pendingSpell = pendingSpell ?? throw new ArgumentNullException(nameof(pendingSpell));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _fonts = fonts;
        _geometry = SpellDeckGeometry.Create(1f, 1f, controller.Rows.Count);
        Width = _geometry.Width;
        Height = _geometry.Height;
        MinWidth = MinWindowWidth;
        MaxWidth = MaxWindowWidth;
        MinHeight = MinWindowHeight;
        MaxHeight = MaxWindowHeight;
        Resizable = true;
        ResizableEdges = ResizeEdges.Right;
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

    /// <summary>The narrowest the deck may be: its fullest row must fit.</summary>
    private float MinStretchNow => SpellDeckGeometry.StretchFor(_controller.MinSlots);

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
        int rows = _controller.Rows.Count;
        if (Width != _geometry.Width || Height != _geometry.Height)
        {
            SpellDeckGeometry g = SpellDeckGeometry.FromSize(Width, Height, _geometry.Rows).WithRows(rows);
            ApplyGeometry(g.WithStretch(MathF.Max(g.Stretch, MinStretchNow)));
        }
        else if (rows != _geometry.Rows)
            ApplyGeometry(_geometry.WithRows(rows));
    }

    protected override void OnTick(double deltaSeconds) => Refresh();

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
            case SpellDeckZone.TabChip when hit.Row < _controller.Rows.Count:
                return new($"Tab {_controller.Rows[hit.Row].Tab + 1}", null, null, ChipHint);
            case SpellDeckZone.Item:
                return _controller.ItemSpell is { } item
                    ? new("Item spell", null, SpellName(item.SpellId), castHint)
                    : new("Item spell", null, "No wielded item has a spell.", null);
            case SpellDeckZone.Slot when SlotSpell(hit.Row, hit.Slot) is uint id:
                string key = KeyLabel(hit.Row, hit.Slot);
                return new(SpellName(id), key.Length == 0 ? null : key, _spellInfo(id)?.School, castHint);
            default:
                return null;
        }
    }

    private string SpellName(uint id) => _spellInfo(id)?.Name is { Length: > 0 } name ? name : $"Spell {id}";

    /// <summary>The key label for a slot; slots past the twelfth have no action.</summary>
    private string KeyLabel(int row, int slot) =>
        row < SpellBarActions.Rows && slot < SpellBarActions.SlotsPerRow
            ? _keyLabel(SpellBarActions.ForSlot(row, slot))
            : string.Empty;

    /// <summary>The spell shown in a slot, or null when the slot is empty or the row is not shown.</summary>
    private uint? SlotSpell(int row, int slot)
    {
        if (row < 0 || row >= _controller.Rows.Count || slot < 0) return null;
        IReadOnlyList<uint> spells = _controller.Visible(_controller.Rows[row].Tab);
        return slot < spells.Count ? spells[slot] : null;
    }

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
            case SpellDeckZone.TabChip when hit.Row < _controller.Rows.Count:
                SpellDeckRow row = _controller.Rows[hit.Row];
                _controller.SetRowTab(row.Id, (row.Tab + 1) % SpellDeckModel.Tabs);
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
}
