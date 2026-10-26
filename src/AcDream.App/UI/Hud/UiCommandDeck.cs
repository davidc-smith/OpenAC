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
    private CommandDeckView _view = CommandDeckView.Empty;
    private AttackHeight? _held;
    private bool _draggingPower;
    private DeckZone _hover;
    /// <summary>The zone under the last hit test; decides click and drag ownership.</summary>
    private DeckZone _hitZone;

    public UiCommandDeck(
        Func<CommandDeckInput> input,
        CommandDeckModel model,
        ICommandDeckActions actions,
        EmbeddedSvgSpriteCache? icons,
        Func<double> clock)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _icons = icons;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Width = CommandDeckLayout.Width;
        Height = CommandDeckLayout.Height;
        MinWidth = MaxWidth = Width;
        MinHeight = MaxHeight = Height;
        Resizable = false;
        RuntimeTooltipTextSource = () => _hover == DeckZone.None ? null : TooltipFor(_hover, _view);
    }

    public override bool HandlesClick => IsControl(_hitZone);
    public override bool ReceivesHoverMouseMove => true;

    internal CommandDeckView View => _view;

    /// <summary>Samples the game and rebuilds the view; releases a held attack if the power row went away.</summary>
    internal void Refresh()
    {
        _view = _model.Update(_input(), _clock());
        if (_held is not null && _view.Row != DeckContextRow.Power)
            ReleaseHeldAttack();
    }

    protected override void OnTick(double deltaSeconds) => Refresh();

    internal bool HitTestLocal(float x, float y) => OnHitTest(x, y);

    protected override bool OnHitTest(float localX, float localY)
    {
        _hitZone = CommandDeckLayout.ZoneAt(_view, localX, localY);
        CapturesPointerDrag = IsControl(_hitZone);
        return _hitZone != DeckZone.None
            || CommandDeckLayout.Frame.Contains(localX, localY)
            || CommandDeckLayout.InOrb(localX, localY);
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

    /// <summary>Called when the deck is torn down.</summary>
    public void Detach()
    {
        _draggingPower = false;
        ReleaseHeldAttack();
    }

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.MouseDown:
                return Press(CommandDeckLayout.ZoneAt(_view, e.Data1, e.Data2), e.Data1);
            case UiEventType.MouseMove:
                _hover = CommandDeckLayout.ZoneAt(_view, e.Data1, e.Data2);
                if (_draggingPower)
                {
                    _actions.SetDesiredPower(CommandDeckLayout.PowerAt(e.Data1));
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
                _hover = DeckZone.None;
                return true;
            case UiEventType.Scroll:
                if (_view.Row != DeckContextRow.Power
                    || CommandDeckLayout.ZoneAt(_view, e.Data1, e.Data2) != DeckZone.PowerTrack
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
                _actions.SetDesiredPower(CommandDeckLayout.PowerAt(x));
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

    internal static string TooltipFor(DeckZone zone, CommandDeckView view) => zone switch
    {
        DeckZone.Orb => view.Orb == DeckOrbMode.Peace
            ? "Peace. Click to enter combat."
            : "Combat. Click to return to peace.",
        DeckZone.StudRepeat => "Repeat attacks: " + OnOff(view.RepeatAttacks),
        DeckZone.StudAutoTarget => "Auto-target: " + OnOff(view.AutoTarget),
        DeckZone.StudKeepInView => "Keep target in view: " + OnOff(view.KeepInView),
        DeckZone.Health => "Health " + view.Health.Text,
        DeckZone.Stamina => "Stamina " + view.Stamina.Text,
        DeckZone.Mana => "Mana " + view.Mana.Text,
        DeckZone.PowerTrack => $"{(view.PowerLabel == "ACCURACY" ? "Accuracy" : "Power")} {MathF.Round(view.DesiredPower * 100f):0}%. Drag or scroll to change.",
        DeckZone.High => "Hold to attack high",
        DeckZone.Medium => "Hold to attack medium",
        DeckZone.Low => "Hold to attack low",
        DeckZone.Recast => view.RecastEnabled ? "Recast " + view.CastingText : "Nothing to recast",
        DeckZone.Recall0 => "Lifestone recall",
        DeckZone.Recall1 => "Marketplace recall",
        DeckZone.Recall2 => "House recall",
        DeckZone.Recall3 => "Mansion recall",
        DeckZone.Recall4 => "Allegiance hometown recall",
        DeckZone.Jump => "Jump charge",
        _ => "",
    };

    private static string OnOff(bool on) => on ? "on" : "off";
}
