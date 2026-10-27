using System.Globalization;
using AcDream.App.UI.Layout;
using AcDream.Core.Targeting;
using Silk.NET.Input;

namespace AcDream.App.UI.Hud;

/// <summary>One line of the ⋯ menu: a deck action, a plugin action, or a plugin's name heading its actions.</summary>
internal readonly record struct TargetMenuEntry(
    string Label, TargetAction Action, TargetPluginAction? Plugin, bool Header, bool Enabled);

/// <summary>
/// The Target Deck: a fixed-size nameplate for the selected object (shape B,
/// info on the left, an action column on the right) that pulls a
/// <see cref="TargetPlateModel"/> each frame and turns stud and menu presses
/// into <see cref="ITargetDeckActions"/>. Only the studs and the open ⋯ menu
/// capture the pointer: a press anywhere else on the plate drags the window.
/// With no target the deck draws nothing and hits nothing.
/// </summary>
internal sealed partial class UiTargetDeck : UiElement
{
    /// <summary>The alpha of stale or failed vitals, and of dim rows.</summary>
    internal const float DimAlpha = 0.45f;

    private const float MenuWidth = 200f;
    private const float MenuLine = 18f;
    private const float MenuInset = 4f;
    private const float MenuGap = 4f;

    private readonly Func<TargetPlateModel?> _model;
    private readonly ITargetDeckActions _actions;
    private readonly EmbeddedSvgSpriteCache? _icons;
    private readonly Func<double> _clock;
    private readonly DeckFonts? _fonts;
    private TargetPlateModel? _view;
    private TargetZone _hover;
    /// <summary>When the pointer reached <see cref="_hover"/>; the tooltip waits <see cref="UiCommandDeck.TooltipDwell"/> from here.</summary>
    private double _hoverSince;
    /// <summary>Set by a press; keeps the tooltip hidden until the pointer moves to another zone.</summary>
    private bool _tooltipSuppressed;
    /// <summary>The zone under the last hit test; decides click and drag ownership.</summary>
    private TargetZone _hitZone;
    private bool _menuOpen;
    /// <summary>The target the menu was opened for; a new target closes it.</summary>
    private uint _menuObjectId;
    /// <summary>The menu line under the pointer, or -1.</summary>
    private int _menuHover = -1;
    private UiElement? _returnFocus;

    public UiTargetDeck(
        Func<TargetPlateModel?> model,
        ITargetDeckActions actions,
        EmbeddedSvgSpriteCache? icons,
        Func<double> clock,
        DeckFonts? fonts = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _icons = icons;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _fonts = fonts;
        Width = TargetDeckGeometry.Width;
        Height = TargetDeckGeometry.Height;
        Resizable = false;
    }

    /// <summary>How the runtime mounts the deck: imported chrome, a fixed size, hidden until there is a target.</summary>
    internal static RetailWindowFrame.Options MountOptions(float left, float top) => new()
    {
        WindowName = WindowNames.TargetDeck,
        Chrome = RetailWindowChrome.Imported,
        Left = left,
        Top = top,
        Resizable = false,
        ResizeX = false,
        ResizeY = false,
        ResizableEdges = ResizeEdges.None,
        MinWidth = TargetDeckGeometry.Width,
        MaxWidth = TargetDeckGeometry.Width,
        MinHeight = TargetDeckGeometry.Height,
        MaxHeight = TargetDeckGeometry.Height,
        Visible = false,
        ContentClickThrough = false,
    };

    /// <summary>Raised at the end of each tick, after the model is pulled.</summary>
    internal event Action? Ticked;

    internal TargetPlateModel? View => _view;
    internal bool MenuOpen => _menuOpen;

    public override bool HandlesClick => _menuOpen || _hitZone >= TargetZone.Stud0;
    public override bool ReceivesHoverMouseMove => true;
    /// <summary>The deck takes the keyboard only while its menu is open, for Escape.</summary>
    public override bool AcceptsFocus => _menuOpen;

    /// <summary>Pulls the model; a target going away closes the menu and forgets the hover.</summary>
    internal void Refresh()
    {
        _view = _model();
        if (_view is null)
        {
            ResetHover();
            CloseMenu();
        }
        else if (_menuOpen && _view.ObjectId != _menuObjectId)
        {
            CloseMenu();
        }
    }

    protected override void OnTick(double deltaSeconds)
    {
        Refresh();
        Ticked?.Invoke();
    }

    /// <summary>Called when the deck is hidden: closes the menu and forgets the hover,
    /// so a deck shown again under a still pointer waits out a fresh dwell.</summary>
    public void OnHidden()
    {
        CloseMenu();
        ResetHover();
    }

    // ---- layout ----

    /// <summary>The kind rows shown: none under vitals bars, else as many as fit beside the plugin rows.</summary>
    private static int KindRowCount(TargetPlateModel v) =>
        v.Vitals is not null ? 0 : Math.Min(v.Rows.Count, 4 - PluginRowCount(v));

    private static int PluginRowCount(TargetPlateModel v) => Math.Min(v.PluginRows.Count, 2);

    /// <summary>Where plugin row <paramref name="i"/> sits: under the bars, or after the kind rows.</summary>
    private static DeckRect PluginRowRect(TargetPlateModel v, int i) =>
        v.Vitals is not null ? TargetDeckGeometry.PluginLine(i) : TargetDeckGeometry.Line(KindRowCount(v) + i);

    internal static DeckRect RectFor(TargetZone zone, TargetPlateModel v) => zone switch
    {
        TargetZone.Portrait => TargetDeckGeometry.Portrait,
        TargetZone.Name => TargetDeckGeometry.Name,
        TargetZone.Health => TargetDeckGeometry.Health,
        TargetZone.Stamina => TargetDeckGeometry.Stamina,
        TargetZone.Mana => TargetDeckGeometry.Mana,
        >= TargetZone.Row0 and <= TargetZone.Row3 => TargetDeckGeometry.Line(zone - TargetZone.Row0),
        TargetZone.PluginRow0 or TargetZone.PluginRow1 => PluginRowRect(v, zone - TargetZone.PluginRow0),
        >= TargetZone.Stud0 => TargetDeckGeometry.Stud(zone - TargetZone.Stud0),
        _ => default,
    };

    /// <summary>The zone at a local point for this model, or <see cref="TargetZone.None"/>.</summary>
    internal static TargetZone ZoneAt(TargetPlateModel v, float x, float y)
    {
        for (int i = 0; i < Math.Min(v.Studs.Count, TargetDeckGeometry.StudCount); i++)
            if (TargetDeckGeometry.Stud(i).Contains(x, y)) return TargetZone.Stud0 + i;
        if (TargetDeckGeometry.Portrait.Contains(x, y)) return TargetZone.Portrait;
        if (TargetDeckGeometry.Name.Contains(x, y)) return TargetZone.Name;
        if (v.Vitals is not null)
        {
            if (TargetDeckGeometry.Health.Contains(x, y)) return TargetZone.Health;
            if (TargetDeckGeometry.Stamina.Contains(x, y)) return TargetZone.Stamina;
            if (TargetDeckGeometry.Mana.Contains(x, y)) return TargetZone.Mana;
        }
        for (int i = 0; i < KindRowCount(v); i++)
            if (TargetDeckGeometry.Line(i).Contains(x, y)) return TargetZone.Row0 + i;
        for (int i = 0; i < PluginRowCount(v); i++)
            if (PluginRowRect(v, i).Contains(x, y)) return TargetZone.PluginRow0 + i;
        return TargetZone.None;
    }

    // ---- the ⋯ menu ----

    /// <summary>The menu's lines: the kind's more actions, then each plugin's name over its actions.</summary>
    internal IReadOnlyList<TargetMenuEntry> MenuEntries => Entries();

    private List<TargetMenuEntry> _menuEntries = [];
    /// <summary>The model <see cref="_menuEntries"/> was built from; a new model rebuilds them.</summary>
    private TargetPlateModel? _menuEntriesFor;
    /// <summary>Set when the menu opened: true when there was no room below the plate, so it opens upward.</summary>
    private bool _menuAbove;

    /// <summary>The menu lines for the current model, built once per model.</summary>
    private List<TargetMenuEntry> Entries()
    {
        if (!ReferenceEquals(_menuEntriesFor, _view))
        {
            _menuEntriesFor = _view;
            _menuEntries = _view is null ? [] : BuildMenu(_view);
        }
        return _menuEntries;
    }

    /// <summary>The kind's more actions, then the plugin actions grouped by plugin in the order
    /// each plugin first appears, one heading per plugin.</summary>
    internal static List<TargetMenuEntry> BuildMenu(TargetPlateModel v)
    {
        var entries = new List<TargetMenuEntry>();
        foreach (TargetAction a in v.MoreMenu)
            entries.Add(new(LabelFor(a), a, null, Header: false, Enabled: true));
        foreach (IGrouping<string, TargetPluginAction> plugin in v.PluginActions.GroupBy(p => p.PluginId, StringComparer.Ordinal))
        {
            entries.Add(new(plugin.First().PluginName, TargetAction.None, null, Header: true, Enabled: false));
            foreach (TargetPluginAction p in plugin)
                entries.Add(new(p.Label, TargetAction.None, p, Header: false, p.Enabled));
        }
        return entries;
    }

    private static float MenuHeight(int count) => MenuInset * 2 + count * MenuLine;

    /// <summary>The menu's top in local coordinates: below the plate, or above it when it opened upward.</summary>
    private float MenuTop(int count) => _menuAbove ? -MenuGap - MenuHeight(count) : Height + MenuGap;

    /// <summary>The open menu's rectangle at the plate's right edge, in local coordinates.</summary>
    private DeckRect MenuRect(int count) => new(Width - MenuWidth, MenuTop(count), MenuWidth, MenuHeight(count));

    /// <summary>Menu line <paramref name="i"/> in local coordinates.</summary>
    internal DeckRect MenuEntryRect(int i) =>
        new(Width - MenuWidth, MenuTop(Entries().Count) + MenuInset + i * MenuLine, MenuWidth, MenuLine);

    private int MenuEntryAt(float x, float y)
    {
        if (!_menuOpen || _view is null) return -1;
        int count = Entries().Count;
        for (int i = 0; i < count; i++)
            if (MenuEntryRect(i).Contains(x, y)) return i;
        return -1;
    }

    private bool InMenu(float x, float y) =>
        _menuOpen && _view is not null && MenuRect(Entries().Count).Contains(x, y);

    private void OpenMenu()
    {
        if (_view is null) return;
        _menuOpen = true;
        _menuObjectId = _view.ObjectId;
        _menuHover = -1;
        UiRoot? root = FindRoot();
        _menuAbove = root is not null
            && ScreenPosition.Y + Height + MenuGap + MenuHeight(Entries().Count) > root.Height;
        if (root is not null)
        {
            root.SetActivePopup(this, () => CloseMenu());
            _returnFocus = root.KeyboardFocus;
            root.SetKeyboardFocus(this);
        }
    }

    private void CloseMenu()
    {
        if (!_menuOpen) return;
        _menuOpen = false;
        _menuHover = -1;
        UiElement? previous = _returnFocus;
        _returnFocus = null;
        if (FindRoot() is { } root)
        {
            root.ClearActivePopup(this);
            if (ReferenceEquals(root.KeyboardFocus, this))
                root.SetKeyboardFocus(previous);
        }
    }

    private void PickMenuEntry(int index)
    {
        if (_view is not { } view) return;
        List<TargetMenuEntry> entries = Entries();
        if (index < 0 || index >= entries.Count) return;
        TargetMenuEntry entry = entries[index];
        if (entry.Header || !entry.Enabled) return;
        CloseMenu();
        if (entry.Plugin is { } p) _actions.RunPluginAction(p.PluginId, view.ObjectId, p.ActionId);
        else _actions.Run(entry.Action, view);
    }

    // ---- hover and tooltips ----

    /// <summary>
    /// The tooltip the deck is showing now, or null. The deck is one element
    /// with many zones, so it times its own tooltip, as the Command Deck does.
    /// </summary>
    internal HudTooltipContent? VisibleTooltip =>
        _view is null || _menuOpen || _hover == TargetZone.None || _tooltipSuppressed
            || _clock() - _hoverSince < UiCommandDeck.TooltipDwell
            ? null
            : TooltipFor(_hover, _view);

    private readonly HudTooltipLayoutCache _tooltipLayout = new();

    private void SetHover(TargetZone zone)
    {
        if (zone == _hover) return;
        _hover = zone;
        _hoverSince = _clock();
        _tooltipSuppressed = false;
    }

    private void ResetHover()
    {
        SetHover(TargetZone.None);
        _tooltipSuppressed = false;
    }

    protected override bool ExpandsClipForPopup => true;

    /// <summary>The open menu hangs below the plate, so a hit test must reach past its bounds.
    /// The deck has no children, and draws inside its frame.</summary>
    protected override bool ClipsChildren => false;

    protected override void OnDrawOverlay(UiRenderContext ctx)
    {
        if (_view is null) return;
        if (_menuOpen)
        {
            DrawMenu(ctx, _view);
            return;
        }
        HudTooltipContent? tip = VisibleTooltip;
        if (tip is null) return;
        ctx.PushAlphaAbsolute(1f);
        try
        {
            HudTooltip.Draw(ctx, tip, HudTooltipStyle.Card, RectFor(_hover, _view),
                ScreenPosition, FindRoot()?.Width, _tooltipLayout, _fonts);
        }
        finally { ctx.PopAlpha(); }
    }

    // ---- input ----

    internal bool HitTestLocal(float x, float y) => OnHitTest(x, y);

    protected override bool OnHitTest(float localX, float localY)
    {
        if (_view is null)
        {
            _hitZone = TargetZone.None;
            CapturesPointerDrag = false;
            return false;
        }
        if (InMenu(localX, localY))
        {
            _hitZone = TargetZone.None;
            CapturesPointerDrag = true;   // a press in the menu never drags the window
            return true;
        }
        _hitZone = ZoneAt(_view, localX, localY);
        CapturesPointerDrag = _hitZone >= TargetZone.Stud0;
        return TargetDeckGeometry.Frame.Contains(localX, localY);
    }

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.MouseDown:
                _tooltipSuppressed = true;
                if (_menuOpen)
                {
                    int entry = MenuEntryAt(e.Data1, e.Data2);
                    if (entry >= 0) PickMenuEntry(entry);
                    else if (!InMenu(e.Data1, e.Data2)) CloseMenu();   // a press on the plate, ⋯ included
                    return true;
                }
                if (_view is null) return false;
                TargetZone zone = ZoneAt(_view, e.Data1, e.Data2);
                return zone >= TargetZone.Stud0 && Press(zone - TargetZone.Stud0);
            case UiEventType.MouseMove:
                if (_view is null) return false;
                _menuHover = MenuEntryAt(e.Data1, e.Data2);
                SetHover(InMenu(e.Data1, e.Data2) ? TargetZone.None : ZoneAt(_view, e.Data1, e.Data2));
                return _hover != TargetZone.None || _menuOpen;
            case UiEventType.HoverLeave:
                _menuHover = -1;
                SetHover(TargetZone.None);
                return true;
            case UiEventType.Click:
                return _menuOpen || _hitZone >= TargetZone.Stud0;
            case UiEventType.KeyDown:
                if (!_menuOpen) return false;
                if ((Key)e.Data0 == Key.Escape) CloseMenu();
                return true;
            case UiEventType.FocusLost:
                // Something else took the focus; the menu goes with it.
                _returnFocus = null;
                CloseMenu();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Presses stud <paramref name="i"/>: a disabled one takes the press and does nothing.</summary>
    private bool Press(int i)
    {
        if (_view is not { } view || i < 0 || i >= view.Studs.Count) return false;
        TargetStud stud = view.Studs[i];
        if (!stud.Enabled) return true;
        if (stud.Action == TargetAction.More)
        {
            if (_menuOpen) CloseMenu();
            else OpenMenu();
            return true;
        }
        _actions.Run(stud.Action, view);
        return true;
    }

    // ---- words and icons ----

    internal static string LabelFor(TargetAction a) => a switch
    {
        TargetAction.Assess => "Assess again",
        TargetAction.UseItemOn => "Use selected item on it",
        TargetAction.Follow => "Follow",
        TargetAction.More => "More",
        TargetAction.Trade => "Trade",
        TargetAction.Give => "Give selected item",
        TargetAction.Tell => "Tell",
        TargetAction.FellowshipInvite => "Invite to fellowship",
        TargetAction.UseOrTalk => "Use / talk",
        TargetAction.PickUpOrUse => "Pick up / use",
        TargetAction.Wield => "Wield",
        TargetAction.Open => "Open",
        TargetAction.UseKey => "Use selected key or lockpick",
        TargetAction.UsePortal => "Use portal",
        TargetAction.Use => "Use",
        TargetAction.CharacterSheet => "Character sheet",
        TargetAction.Examine => "Examine",
        TargetAction.CopyName => "Copy name",
        TargetAction.SwearAllegiance => "Swear allegiance",
        _ => "",
    };

    /// <summary>The HUD icon for an action's stud. Menu-only actions get a near fit; menus draw no icons.</summary>
    internal static string IconFor(TargetAction a) => a switch
    {
        TargetAction.Assess or TargetAction.Examine or TargetAction.CopyName => HudIcons.Search,
        TargetAction.UseItemOn or TargetAction.Use => HudIcons.Cast,
        TargetAction.Follow => HudIcons.Follow,
        TargetAction.Trade => HudIcons.Trade,
        TargetAction.Give => HudIcons.Give,
        TargetAction.Tell or TargetAction.UseOrTalk => HudIcons.Tell,
        TargetAction.FellowshipInvite or TargetAction.CharacterSheet => HudIcons.Character,
        TargetAction.PickUpOrUse => HudIcons.PickUp,
        TargetAction.Wield => HudIcons.Melee,
        TargetAction.Open => HudIcons.Open,
        TargetAction.UseKey => HudIcons.Key,
        TargetAction.UsePortal => HudIcons.Portal,
        TargetAction.SwearAllegiance => HudIcons.RecallAllegiance,
        _ => HudIcons.More,
    };

    /// <summary>The portrait's icon for a kind.</summary>
    internal static string KindIcon(TargetKind kind) => kind switch
    {
        TargetKind.Monster => HudIcons.Melee,
        TargetKind.Summon => HudIcons.Magic,
        TargetKind.Npc => HudIcons.Tell,
        TargetKind.Item => HudIcons.PickUp,
        TargetKind.Corpse or TargetKind.Container => HudIcons.Open,
        TargetKind.Portal => HudIcons.Portal,
        TargetKind.WorldObject => HudIcons.Key,
        _ => HudIcons.Character,   // players and the player
    };

    private const string AssessFailedDetail = "Your assess skill check failed; health is still live.";
    private const string StaleDetail = "Last assessed over 10 s ago";

    internal static HudTooltipContent? TooltipFor(TargetZone zone, TargetPlateModel view)
    {
        switch (zone)
        {
            case >= TargetZone.Stud0:
            {
                int i = zone - TargetZone.Stud0;
                if (i >= view.Studs.Count) return null;
                TargetStud stud = view.Studs[i];
                return new(LabelFor(stud.Action), null, stud.Enabled ? null : stud.DisabledReason, null);
            }
            case TargetZone.Name:
                return new(view.Name, null, null, null);
            case TargetZone.Health when view.Vitals is { } h:
                return new("Health", Percent(h.Health), null, null);
            case TargetZone.Stamina or TargetZone.Mana when view.Vitals is { } vitals:
            {
                string title = zone == TargetZone.Stamina ? "Stamina" : "Mana";
                float? value = zone == TargetZone.Stamina ? vitals.Stamina : vitals.Mana;
                return vitals.State switch
                {
                    TargetVitalsState.Failed => new("Assess failed", null, AssessFailedDetail, null),
                    TargetVitalsState.Stale => new(title, value is float s ? Percent(s) : null, StaleDetail, null),
                    TargetVitalsState.Fresh when value is float f => new(title, Percent(f), null, null),
                    _ => null,
                };
            }
            case TargetZone.PluginRow0 or TargetZone.PluginRow1:
            {
                int i = zone - TargetZone.PluginRow0;
                if (i >= view.PluginRows.Count) return null;
                TargetPluginRow row = view.PluginRows[i];
                string? detail = view.PluginOverflowTooltip ?? row.Tooltip;
                return detail is null ? null : new(row.Label.Length > 0 ? row.Label : row.Value, null, detail, null);
            }
            default:
                return null;
        }
    }

    private static string Percent(float fraction) =>
        string.Create(CultureInfo.InvariantCulture, $"{MathF.Round(fraction * 100f):0}%");
}
