using System.Globalization;
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

/// <summary>
/// A markup splitter: two panes with a bar between them that the player drags.
/// Horizontal places the panes side by side (the bar is vertical); vertical
/// stacks them. The position is the first pane's size in pixels, clamped to
/// <see cref="Min"/>/<see cref="Max"/> and to the room the split has. The split
/// lays its panes and bar out itself (they have no anchors), so when it
/// resizes the first pane keeps its size and the second takes the rest.
/// </summary>
public sealed class UiMarkupSplit : UiElement
{
    public const float BarSize = 5f;

    /// <summary>How far one arrow key press moves the bar.</summary>
    public const float KeyStep = 8f;

    public bool Horizontal { get; }
    public UiMarkupSplitBar Bar { get; }
    public UiElement? First { get; private set; }
    public UiElement? Second { get; private set; }

    public float Min { get; set; }
    public float? Max { get; set; }

    /// <summary>The markup's <c>at</c> (or half the room): where unbound state starts, and what isn't remembered.</summary>
    public float DefaultPosition { get; set; }

    /// <summary>Bound position: when set, the plugin owns it and the layout follows it.</summary>
    public Func<float>? PositionSource { get; set; }

    /// <summary>Called with each new position a drag or key press asks for.</summary>
    public Action<float>? Changed { get; set; }

    internal RememberedValue? Remembered { get; set; }

    public bool IsRemembered => Remembered?.IsActive == true;

    /// <summary>The first pane's size as last laid out (where the bar is).</summary>
    public float LaidOutPosition { get; private set; }

    private float? _hostPosition;
    private float _asked;
    private float _moveStart;

    public UiMarkupSplit(bool horizontal)
    {
        Horizontal = horizontal;
        ClickThrough = true;
        Bar = new UiMarkupSplitBar(this)
        {
            AcceptsFocus = true,
            FocusOnMouseClick = false,
            TabStop = true,
            Anchors = AnchorEdges.None,
        };
        AddChild(Bar);
    }

    /// <summary>Adopts the two built panes; the split sizes them from now on.</summary>
    internal void SetPanes(UiElement first, UiElement second)
    {
        First = first;
        Second = second;
        first.Anchors = AnchorEdges.None;
        second.Anchors = AnchorEdges.None;
    }

    /// <summary>The position asked for (bound, remembered or default), before clamping.</summary>
    public float Position => PositionSource?.Invoke() ?? HostPosition;

    private float HostPosition
    {
        get
        {
            if (Remembered is { } remembered && remembered.TryRefresh(out string? text))
                _hostPosition = Parse(text) ?? DefaultPosition;
            return _hostPosition ??= DefaultPosition;
        }
    }

    private static float? Parse(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0
            ? value
            : null;

    private static string Encode(float position) =>
        ((int)MathF.Round(position)).ToString(CultureInfo.InvariantCulture);

    private float Room => MathF.Max(0f, (Horizontal ? Width : Height) - BarSize);

    /// <summary>Whole pixels, within min/max and the room.</summary>
    internal float Clamp(float position)
    {
        if (float.IsNaN(position))
            position = DefaultPosition;
        float room = Room;
        float hi = MathF.Min(Max ?? room, room);
        float lo = MathF.Min(Min, hi);
        return MathF.Round(Math.Clamp(position, lo, hi));
    }

    internal void Layout()
    {
        float at = Clamp(Position);
        LaidOutPosition = at;
        float second = at + BarSize;
        if (Horizontal)
        {
            Place(First, 0f, 0f, at, Height);
            Place(Bar, at, 0f, BarSize, Height);
            Place(Second, second, 0f, MathF.Max(0f, Width - second), Height);
        }
        else
        {
            Place(First, 0f, 0f, Width, at);
            Place(Bar, 0f, at, Width, BarSize);
            Place(Second, 0f, second, Width, MathF.Max(0f, Height - second));
        }
    }

    private static void Place(UiElement? element, float x, float y, float w, float h)
    {
        if (element is null)
            return;
        element.Left = x;
        element.Top = y;
        element.Width = w;
        element.Height = h;
    }

    protected override void OnTick(double deltaSeconds) => Layout();

    // Runs after the parent's anchor pass sized the split and before the panes'
    // children are anchored, so a resize reaches the panes in the same frame.
    protected override void OnDraw(UiRenderContext ctx) => Layout();

    /// <summary>A drag or key press starts from where the bar is now (a fresh read).</summary>
    internal void BeginMove()
    {
        Layout();
        _asked = LaidOutPosition;
        _moveStart = LaidOutPosition;
    }

    /// <summary>Asks for a new position; each distinct whole pixel is reported once.</summary>
    internal void MoveTo(float position)
    {
        float at = Clamp(position);
        if (at == _asked)
            return;
        if (PositionSource is not null && Changed is null)
            return;
        _asked = at;
        if (PositionSource is null)
            _hostPosition = at;
        Changed?.Invoke(at);
        Layout();
    }

    /// <summary>The player let go (or pressed a key): remember an unbound change.</summary>
    internal void EndMove()
    {
        if (PositionSource is not null || _asked == _moveStart)
            return;
        string encoded = Encode(_asked);
        Remembered?.Remember(encoded == Encode(DefaultPosition) ? null : encoded);
    }

    internal void Nudge(int direction)
    {
        BeginMove();
        MoveTo(LaidOutPosition + direction * KeyStep);
        EndMove();
    }
}
