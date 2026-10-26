using System.Globalization;
using AcDream.App.UI.Layout;
using AcDream.Core.Combat;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The Command Deck: one fixed-size element that draws a
/// <see cref="CommandDeckView"/> and turns pointer input into
/// <see cref="ICommandDeckActions"/>. The frame, orb and studs hit-test, but
/// only the controls capture the pointer: a press on the bars, the jump strip
/// or the frame body drags the window. The transparent corners around the
/// orb and frame fall through to the world.
/// </summary>
internal sealed partial class UiCommandDeck : UiElement
{
    private static readonly RuntimePortalCommand[] Recalls =
    [
        RuntimePortalCommand.RecallLifestone,
        RuntimePortalCommand.RecallMarketplace,
        RuntimePortalCommand.RecallHouse,
        RuntimePortalCommand.RecallMansion,
        RuntimePortalCommand.RecallAllegiance,
    ];

    private readonly Func<CommandDeckInput> _input;
    private readonly CommandDeckModel _model;
    private readonly ICommandDeckActions _actions;
    private readonly EmbeddedSvgSpriteCache? _icons;
    private readonly Func<double> _clock;
    private readonly DeckFonts? _fonts;
    private DeckGeometry _geometry = DeckGeometry.Default;
    private CommandDeckView _view = CommandDeckView.Empty;
    private AttackHeight? _held;
    private bool _draggingPower;
    private DeckZone _hover;
    /// <summary>When the pointer reached <see cref="_hover"/>; the tooltip waits <see cref="TooltipDwell"/> from here.</summary>
    private double _hoverSince;
    /// <summary>Set by a press; keeps the tooltip hidden until the pointer moves to another zone.</summary>
    private bool _tooltipSuppressed;
    /// <summary>The zone under the last hit test; decides click and drag ownership.</summary>
    private DeckZone _hitZone;

    public UiCommandDeck(
        Func<CommandDeckInput> input,
        CommandDeckModel model,
        ICommandDeckActions actions,
        EmbeddedSvgSpriteCache? icons,
        Func<double> clock,
        DeckFonts? fonts = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _icons = icons;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _fonts = fonts;
        Width = _geometry.Width;
        Height = _geometry.Height;
        MinWidth = MinWindowWidth;
        MaxWidth = MaxWindowWidth;
        MinHeight = MinWindowHeight;
        MaxHeight = MaxWindowHeight;
        Resizable = true;
        ResizableEdges = ResizeEdges.Right;
    }

    // The window's full size range. Scale and stretch together are enforced
    // by ConstrainResize and SyncGeometry, not by these.
    private const float MinWindowWidth = DeckGeometry.BaseWidth * DeckGeometry.MinStretch * DeckGeometry.MinScale;
    private const float MaxWindowWidth = DeckGeometry.BaseWidth * DeckGeometry.MaxStretch * DeckGeometry.MaxScale;
    private const float MinWindowHeight = DeckGeometry.BaseHeight * DeckGeometry.MinScale;
    private const float MaxWindowHeight = DeckGeometry.BaseHeight * DeckGeometry.MaxScale;

    /// <summary>A pointer within this many points of the right edge hits the
    /// deck even outside the frame, so the right-hand grips are easy to grab.</summary>
    private const float GripBand = 6f;

    /// <summary>How the runtime mounts the deck: imported chrome, resizable from the right only.</summary>
    internal static RetailWindowFrame.Options MountOptions(float left, float top) => new()
    {
        WindowName = WindowNames.CommandDeck,
        Chrome = RetailWindowChrome.Imported,
        Left = left,
        Top = top,
        Resizable = true,
        ResizeX = true,
        ResizeY = true,
        ResizableEdges = ResizeEdges.Right,
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
        _geometry = corner ? _geometry.WithCornerSize(width, height) : _geometry.WithWidth(width);
        width = _geometry.Width;
        height = _geometry.Height;
    }

    /// <summary>Moves the geometry to the deck's current size (after a restore,
    /// a reset or a screen limit), and the size to the nearest valid one.</summary>
    private void SyncGeometry()
    {
        if (Width == _geometry.Width && Height == _geometry.Height) return;
        _geometry = DeckGeometry.FromSize(Width, Height);
        Width = _geometry.Width;
        Height = _geometry.Height;
    }

    /// <summary>How long the pointer rests on a zone before its tooltip shows.</summary>
    internal const double TooltipDwell = 0.45;

    public override bool HandlesClick => IsControl(_hitZone);
    public override bool ReceivesHoverMouseMove => true;

    internal CommandDeckView View => _view;
    internal DeckGeometry Geometry => _geometry;

    /// <summary>Sets the deck to <paramref name="geometry"/>'s size and geometry.</summary>
    internal void ApplyGeometry(DeckGeometry geometry)
    {
        _geometry = geometry;
        Width = geometry.Width;
        Height = geometry.Height;
    }

    /// <summary>
    /// The tooltip the deck is showing now, or null. The deck is one element
    /// with many zones, so it times its own tooltip rather than using the
    /// root's one-per-element tooltip.
    /// </summary>
    internal HudTooltipContent? VisibleTooltip =>
        _hover == DeckZone.None || _tooltipSuppressed || _held is not null || _draggingPower
            || _clock() - _hoverSince < TooltipDwell
            ? null
            : TooltipFor(_hover, _view);

    /// <summary>A card in peace; a compact pill in combat, where it should not cover the fight.</summary>
    internal HudTooltipStyle TooltipStyle =>
        _view.Orb == DeckOrbMode.Peace ? HudTooltipStyle.Card : HudTooltipStyle.Compact;

    private readonly HudTooltipLayoutCache _tooltipLayout = new();

    private void SetHover(DeckZone zone)
    {
        if (zone == _hover) return;
        _hover = zone;
        _hoverSince = _clock();
        _tooltipSuppressed = false;
    }

    protected override bool ExpandsClipForPopup => true;

    protected override void OnDrawOverlay(UiRenderContext ctx)
    {
        HudTooltipContent? tip = VisibleTooltip;
        if (tip is null) return;
        ctx.PushAlphaAbsolute(1f);
        try
        {
            HudTooltip.Draw(ctx, tip, TooltipStyle, _geometry.RectFor(_hover),
                ScreenPosition, FindRoot()?.Width, _tooltipLayout, _fonts);
        }
        finally { ctx.PopAlpha(); }
    }

    /// <summary>Samples the game and rebuilds the view; releases a held attack if the power row went away.</summary>
    internal void Refresh()
    {
        SyncGeometry();
        _view = _model.Update(_input(), _clock());
        if (_held is not null && _view.Row != DeckContextRow.Power)
            ReleaseHeldAttack();
    }

    protected override void OnTick(double deltaSeconds) => Refresh();

    internal bool HitTestLocal(float x, float y) => OnHitTest(x, y);

    protected override bool OnHitTest(float localX, float localY)
    {
        _hitZone = _geometry.ZoneAt(_view, localX, localY);
        CapturesPointerDrag = IsControl(_hitZone);
        return _hitZone != DeckZone.None
            || _geometry.Frame.Contains(localX, localY)
            || _geometry.InOrb(localX, localY)
            || (localX >= Width - GripBand && localY >= 0 && localY < Height);
    }

    /// <summary>Zones that act on a press; every other part of the deck drags the window.</summary>
    internal static bool IsControl(DeckZone zone) => zone switch
    {
        DeckZone.Orb
            or DeckZone.StudRepeat or DeckZone.StudAutoTarget or DeckZone.StudKeepInView
            or DeckZone.PowerTrack or DeckZone.High or DeckZone.Medium or DeckZone.Low
            or DeckZone.Recast => true,
        >= DeckZone.Recall0 and <= DeckZone.Recall4 => true,
        _ => false,
    };

    /// <summary>Ends an attack started from the deck, once.</summary>
    public void ReleaseHeldAttack()
    {
        if (_held is null) return;
        _held = null;
        _actions.ReleaseAttack();
    }

    /// <summary>Called when the deck is hidden: ends a held attack and forgets the hover,
    /// so a deck shown again under a still pointer waits out a fresh dwell.</summary>
    public void OnHidden()
    {
        _draggingPower = false;
        ReleaseHeldAttack();
        ResetHover();
    }

    /// <summary>Called when the deck is torn down.</summary>
    public void Detach() => OnHidden();

    private void ResetHover()
    {
        SetHover(DeckZone.None);
        _tooltipSuppressed = false;
    }

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.MouseDown:
                _tooltipSuppressed = true;
                return Press(_geometry.ZoneAt(_view, e.Data1, e.Data2), e.Data1);
            case UiEventType.MouseMove:
                SetHover(_geometry.ZoneAt(_view, e.Data1, e.Data2));
                if (_draggingPower)
                {
                    _actions.SetDesiredPower(_geometry.PowerAt(e.Data1));
                    return true;
                }
                return _hover != DeckZone.None;
            case UiEventType.MouseUp:
            case UiEventType.CaptureChanged:
                _draggingPower = false;
                ReleaseHeldAttack();
                return true;
            case UiEventType.Click:
                return IsControl(_hitZone);
            case UiEventType.HoverLeave:
                SetHover(DeckZone.None);
                return true;
            case UiEventType.Scroll:
                if (_view.Row != DeckContextRow.Power
                    || _geometry.ZoneAt(_view, e.Data1, e.Data2) != DeckZone.PowerTrack
                    || e.Data0 == 0)
                    return false;
                _actions.SetDesiredPower(Math.Clamp(
                    _view.DesiredPower + Math.Sign(e.Data0) * RuntimeCombatAttackState.DesiredPowerStep,
                    0f, 1f));
                return true;
            default:
                return false;
        }
    }

    private bool Press(DeckZone zone, float x)
    {
        switch (zone)
        {
            case DeckZone.Orb:
                _actions.ToggleCombat();
                return true;
            case DeckZone.StudRepeat:
                _actions.SetOption(CharacterOptionId.AutoRepeatAttack, !_view.RepeatAttacks);
                return true;
            case DeckZone.StudAutoTarget:
                _actions.SetOption(CharacterOptionId.AutoTarget, !_view.AutoTarget);
                return true;
            case DeckZone.StudKeepInView:
                _actions.SetOption(CharacterOptionId.ViewCombatTarget, !_view.KeepInView);
                return true;
            case DeckZone.PowerTrack:
                _draggingPower = true;
                _actions.SetDesiredPower(_geometry.PowerAt(x));
                return true;
            case DeckZone.High:
            case DeckZone.Medium:
            case DeckZone.Low:
                ReleaseHeldAttack();
                _held = zone switch
                {
                    DeckZone.High => AttackHeight.High,
                    DeckZone.Medium => AttackHeight.Medium,
                    _ => AttackHeight.Low,
                };
                _actions.PressAttack(_held.Value);
                return true;
            case DeckZone.Recast:
                if (_view.RecastEnabled) _actions.Recast();
                return true;
            case >= DeckZone.Recall0 and <= DeckZone.Recall4:
                _actions.Recall(Recalls[zone - DeckZone.Recall0]);
                return true;
            default:
                return false;   // bars, jump and frame: the window drag has the press
        }
    }

    private const string ClickToToggle = "Click to toggle";
    private const string ClickToRecall = "Click to recall";
    private const string HoldToAttack = "Hold to attack";

    internal static HudTooltipContent? TooltipFor(DeckZone zone, CommandDeckView view) => zone switch
    {
        DeckZone.Orb => view.Orb == DeckOrbMode.Peace
            ? new("Peace", null, "You are not fighting.", "Click to enter combat")
            : new("Combat - " + view.Orb, null, "Your stance follows what you wield.", "Click to return to peace"),
        DeckZone.StudRepeat => new("Repeat attacks", OnOff(view.RepeatAttacks),
            "Keep attacking after the first swing until you stop.", ClickToToggle),
        DeckZone.StudAutoTarget => new("Auto-target", OnOff(view.AutoTarget),
            "Pick the nearest enemy when you attack with nothing selected.", ClickToToggle),
        DeckZone.StudKeepInView => new("Keep target in view", OnOff(view.KeepInView),
            "The camera follows your combat target.", ClickToToggle),
        DeckZone.Health => new("Health", NullIfEmpty(view.Health.Text), "Damage taken shows as a fading trail.", null),
        DeckZone.Stamina => new("Stamina", NullIfEmpty(view.Stamina.Text), "Spent by running, jumping and attacking.", null),
        DeckZone.Mana => new("Mana", NullIfEmpty(view.Mana.Text), "Spent by casting spells.", null),
        DeckZone.PowerTrack => view.PowerLabel == "ACCURACY"
            ? new("Accuracy", Percent(view.DesiredPower), "Higher accuracy aims better but draws slower.", "Drag or scroll to change")
            : new("Power", Percent(view.DesiredPower), "More power hits harder but swings slower.", "Drag or scroll to change"),
        DeckZone.High => new("High attack", null, "Aim at the head and shoulders.", HoldToAttack),
        DeckZone.Medium => new("Medium attack", null, "Aim at the body.", HoldToAttack),
        DeckZone.Low => new("Low attack", null, "Aim at the legs.", HoldToAttack),
        DeckZone.Recast => new("Recast", view.RecastEnabled ? view.CastingText : null,
            "Cast your last spell again on the same target.",
            view.RecastEnabled ? "Click to recast" : "Nothing to recast yet"),
        DeckZone.Recall0 => new("Lifestone recall", null, "Return to the lifestone you are tied to.", ClickToRecall),
        DeckZone.Recall1 => new("Marketplace recall", null, "Travel to the Marketplace.", ClickToRecall),
        DeckZone.Recall2 => new("House recall", null, "Return to your house.", ClickToRecall),
        DeckZone.Recall3 => new("Mansion recall", null, "Return to your allegiance mansion.", ClickToRecall),
        DeckZone.Recall4 => new("Allegiance hometown", null, "Travel to your allegiance's hometown.", ClickToRecall),
        DeckZone.Jump => new("Jump", view.JumpCharging ? Percent(view.JumpFill) : null,
            "Hold jump to charge, release to leap.", null),
        _ => null,
    };

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    private static string OnOff(bool on) => on ? "ON" : "OFF";

    private static string Percent(float fraction) =>
        string.Create(CultureInfo.InvariantCulture, $"{MathF.Round(fraction * 100f):0}%");
}
