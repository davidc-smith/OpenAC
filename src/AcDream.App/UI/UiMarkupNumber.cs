using System.Globalization;
using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// A markup number field: a one-line <see cref="UiField"/> that only ever
/// reports a valid value, clamped to <see cref="Min"/>..<see cref="Max"/> and
/// rounded to <see cref="Decimals"/>, with spin buttons at its right edge.
/// Values travel as <see cref="decimal"/>; the builder converts to and from
/// the bound type.
/// </summary>
public sealed class UiMarkupNumber : UiElement
{
    /// <summary>The spin buttons' width at the right edge.</summary>
    public const float SpinWidth = 14f;

    internal const double RepeatDelay = 0.40;
    internal const double RepeatRate = 0.06;

    public static readonly Vector4 ErrorColor = new(0.95f, 0.38f, 0.32f, 1f);
    public static readonly Vector4 ClassicSpinFace = new(0.16f, 0.14f, 0.10f, 1f);
    public static readonly Vector4 ClassicSpinPressed = new(0.26f, 0.23f, 0.16f, 1f);
    public static readonly Vector4 ClassicSpinBorder = UiCheckLamp.UncheckedInner;
    public static readonly Vector4 ClassicSpinArrow = new(0.80f, 0.74f, 0.55f, 1f);

    /// <summary>The editing field; it fills the control left of the spin buttons.</summary>
    public UiField Field { get; }

    public PluginUiPalette? ThemePalette { get; set; }
    public Func<decimal>? ValueSource { get; set; }
    public Action<decimal>? Changed { get; set; }
    public decimal Min { get; set; } = decimal.MinValue;
    public decimal Max { get; set; } = decimal.MaxValue;
    public decimal Step { get; set; } = 1m;
    public int Decimals { get; set; }

    /// <summary>Whether Shift is held, so a step counts ten. Defaults to the root's keyboard.</summary>
    public Func<bool>? ShiftHeld { get; set; }

    /// <summary>A bound caption colour; re-read every frame in place of the field's own.</summary>
    public Func<Vector4>? TextColorSource { get; set; }

    private bool _cancelling;
    private int _spinDirection;
    private double _spinTimer;

    public UiMarkupNumber()
    {
        CapturesPointerDrag = true;
        Field = new UiField
        {
            OneLine = true,
            Selectable = true,
            TabStop = true,
            ClearOnSubmit = false,
            RecordHistory = false,
            SelectAllOnFocus = true,
            MaxCharacters = 41,   // sign + 29 digits + point + 10 decimals
            // LayoutField places the field; an anchor would put back the size
            // it was captured at, which is 0 for a number a flex row sizes.
            Anchors = AnchorEdges.None,
        };
        Field.CharacterFilter = AcceptsCharacter;
        Field.OnUp = () => StepBy(1);
        Field.OnDown = () => StepBy(-1);
        Field.OnEscape = () => _cancelling = true;
        Field.OnFocusLost = text =>
        {
            if (_cancelling)
            {
                _cancelling = false;
                ShowValue();
            }
            else
            {
                Commit(text);
            }
        };
        Field.BoundText = () => Format(Current);
        Field.TextColorSource = () => Fade(IsValidText(Field.Text)
            ? TextColorSource?.Invoke() ?? Field.TextColor
            : ErrorColor);
        AddChild(Field);
    }

    public override bool HandlesClick => true;

    /// <summary>The bound value.</summary>
    public decimal Current => ValueSource?.Invoke() ?? 0m;

    public string Format(decimal value) =>
        value.ToString("F" + Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>Digits always; a minus sign only when negatives are allowed; a point only with decimals.</summary>
    public bool AcceptsCharacter(char c) =>
        char.IsAsciiDigit(c) || (c == '-' && Min < 0m) || (c == '.' && Decimals > 0);

    public static bool TryParse(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);

    public bool IsValidText(string text) => TryParse(text, out decimal value) && value >= Min && value <= Max;

    /// <summary>Applies typed text; text that isn't a number puts the bound value back.</summary>
    public void Commit(string text)
    {
        if (text == Format(Current))
        {
            ShowValue();   // nothing was edited; don't report a formatting difference
            return;
        }
        if (TryParse(text, out decimal typed))
            Apply(typed);
        else
            ShowValue();
    }

    /// <summary>Steps once up (+1) or down (-1); ten steps while Shift is held.</summary>
    public void StepBy(int direction)
    {
        if (!Enabled)
            return;
        decimal baseValue = Field.IsFocused && Field.Text != Format(Current)
            && TryParse(Field.Text, out decimal typed) ? typed : Current;
        bool shift = ShiftHeld?.Invoke() ?? RootShiftHeld();
        decimal requested;
        try
        {
            requested = baseValue + direction * Step * (shift ? 10m : 1m);
        }
        catch (OverflowException)
        {
            requested = direction > 0 ? Max : Min;
        }
        Apply(requested);
    }

    private void Apply(decimal requested)
    {
        decimal value = Math.Clamp(
            Math.Round(requested, Decimals, MidpointRounding.AwayFromZero), Min, Max);
        if (value != Math.Round(Current, Decimals, MidpointRounding.AwayFromZero))
            Changed?.Invoke(value);
        ShowValue();
    }

    private void ShowValue() => Field.SetText(Format(Current));

    private bool RootShiftHeld() => FindRoot()?.Keyboard is { } keyboard
        && (keyboard.IsKeyPressed(Silk.NET.Input.Key.ShiftLeft)
            || keyboard.IsKeyPressed(Silk.NET.Input.Key.ShiftRight));

    private Vector4 Fade(Vector4 color) =>
        Enabled ? color : color with { W = color.W * UiMarkupToggle.DisabledAlpha };

    /// <summary>Keeps the field beside the spin buttons and read-only while disabled.</summary>
    internal void SyncField()
    {
        LayoutField();
        if (Field.Editable != Enabled)
        {
            if (!Enabled && Field.IsFocused)
                _cancelling = true;   // restore the edit; a disabled control mustn't commit it
            Field.Editable = Enabled;
        }
    }

    private void LayoutField()
    {
        Field.Left = 0f;
        Field.Top = 0f;
        Field.Width = MathF.Max(0f, Width - SpinWidth);
        Field.Height = Height;
    }

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.MouseDown:
                if (!Enabled || e.Data1 < Width - SpinWidth)
                    return true;
                _spinDirection = e.Data2 < MathF.Floor(Height * 0.5f) ? 1 : -1;
                _spinTimer = RepeatDelay;
                StepBy(_spinDirection);
                return true;
            case UiEventType.MouseUp:
            case UiEventType.CaptureChanged:
                _spinDirection = 0;
                return true;
            case UiEventType.Scroll:
                if (!Enabled || !Field.IsFocused || e.Data0 == 0)
                    return false;
                StepBy(Math.Sign(e.Data0));
                return true;
        }
        return false;
    }

    protected override void OnTick(double deltaSeconds)
    {
        SyncField();
        if (_spinDirection == 0)
            return;
        if (!Enabled)
        {
            _spinDirection = 0;
            return;
        }
        _spinTimer -= deltaSeconds;
        if (_spinTimer <= 0)
        {
            StepBy(_spinDirection);
            _spinTimer = RepeatRate;
        }
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        LayoutField();
        float x = Width - SpinWidth;
        float half = MathF.Floor(Height * 0.5f);
        if (ThemePalette is { } p)
        {
            // Ghost spin buttons beside the field: a pressed face, and chevrons.
            Vector4 pressedFace = Fade(PluginUiStyle.Pressed(PluginUiStyle.Hover(p, p.Field)));
            if (_spinDirection > 0)
                ctx.FillRoundedRect(x + 1f, 1f, SpinWidth - 2f, half - 1f, PluginUiStyle.SmallRadius, pressedFace);
            else if (_spinDirection < 0)
                ctx.FillRoundedRect(x + 1f, half, SpinWidth - 2f, Height - half - 1f, PluginUiStyle.SmallRadius, pressedFace);
            Vector4 chevron = Fade(p.Muted);
            float cx = x + (SpinWidth - 6f) * 0.5f;
            float upY = MathF.Floor(half * 0.5f);
            ctx.DrawSmoothLine(cx, upY + 1.5f, cx + 3f, upY - 1.5f, chevron, 1.5f);
            ctx.DrawSmoothLine(cx + 3f, upY - 1.5f, cx + 6f, upY + 1.5f, chevron, 1.5f);
            PluginUiStyle.Chevron(ctx, cx, MathF.Floor(half + (Height - half) * 0.5f) - 1.5f, 6f, 3f, chevron);
            return;
        }

        Vector4 face = Fade(ThemePalette?.Field ?? ClassicSpinFace);
        Vector4 pressed = Fade(ThemePalette?.Selected ?? ClassicSpinPressed);
        Vector4 border = Fade(ThemePalette?.Border ?? ClassicSpinBorder);
        Vector4 arrow = Fade(ThemePalette?.Text ?? ClassicSpinArrow);

        ctx.DrawFill(x, 0f, SpinWidth, half, _spinDirection > 0 ? pressed : face);
        ctx.DrawFill(x, half, SpinWidth, Height - half, _spinDirection < 0 ? pressed : face);
        ctx.DrawRectOutline(x, 0f, SpinWidth, Height, border, 1f);
        ctx.DrawFill(x, half, SpinWidth, 1f, border);
        float centre = x + SpinWidth * 0.5f;
        DrawArrow(ctx, centre, half * 0.5f, up: true, arrow);
        DrawArrow(ctx, centre, half + (Height - half) * 0.5f, up: false, arrow);
    }

    /// <summary>A pixel triangle three rows tall (1, 3 and 5 px wide), centred on (cx, cy).</summary>
    private static void DrawArrow(UiRenderContext ctx, float cx, float cy, bool up, Vector4 color)
    {
        float top = MathF.Floor(cy - 1.5f);
        for (int row = 0; row < 3; row++)
        {
            int width = up ? 1 + row * 2 : 5 - row * 2;
            ctx.DrawFill(MathF.Floor(cx - width * 0.5f), top + row, width, 1f, color);
        }
    }
}
