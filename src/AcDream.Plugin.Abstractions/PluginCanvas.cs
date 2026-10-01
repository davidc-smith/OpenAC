namespace AcDream.Plugin.Abstractions;

/// <summary>
/// Which corner or edge of the screen a canvas is measured from. The
/// canvas's offset is added from there, so a canvas anchored at the bottom
/// right with an offset of (-10, -10) sits ten pixels in from that corner
/// and stays there when the window is resized.
/// </summary>
public enum PluginCanvasAnchor
{
    /// <summary>The canvas's top-left corner sits at the screen's top-left corner plus the offset.</summary>
    TopLeft,

    /// <summary>The canvas is centred along the top edge, plus the offset.</summary>
    TopCenter,

    /// <summary>The canvas's top-right corner sits at the screen's top-right corner plus the offset.</summary>
    TopRight,

    /// <summary>The canvas is centred along the left edge, plus the offset.</summary>
    CenterLeft,

    /// <summary>The canvas is centred on the screen, plus the offset.</summary>
    Center,

    /// <summary>The canvas is centred along the right edge, plus the offset.</summary>
    CenterRight,

    /// <summary>The canvas's bottom-left corner sits at the screen's bottom-left corner plus the offset.</summary>
    BottomLeft,

    /// <summary>The canvas is centred along the bottom edge, plus the offset.</summary>
    BottomCenter,

    /// <summary>The canvas's bottom-right corner sits at the screen's bottom-right corner plus the offset.</summary>
    BottomRight,
}

/// <summary>A width and a height, in pixels.</summary>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct PluginSize(double Width, double Height);

/// <summary>
/// How a plugin's canvas is identified, how large it is and where it sits.
/// A canvas is exactly its declared size: everything painted is clipped to
/// that rectangle, and there is no way to draw anywhere else on the screen.
/// </summary>
/// <param name="CanvasId">The plugin's own short name for this canvas, unique within the plugin.</param>
/// <param name="Width">The canvas's width in pixels, at least 1.</param>
/// <param name="Height">The canvas's height in pixels, at least 1.</param>
public sealed record PluginCanvasDescriptor(string CanvasId, int Width, int Height)
{
    /// <summary>Which corner or edge of the screen the canvas is measured from.</summary>
    public PluginCanvasAnchor Anchor { get; init; } = PluginCanvasAnchor.TopLeft;

    /// <summary>How far from the anchor the canvas sits, in pixels; negative values move it back towards the screen's middle from a right or bottom anchor.</summary>
    public PluginPoint Offset { get; init; }

    /// <summary>Whether the canvas is shown as soon as it is registered.</summary>
    public bool StartVisible { get; init; } = true;

    /// <summary>
    /// Whether the canvas takes pointer input. Off, the default, the canvas
    /// is click-through: presses, drags and the wheel over it go to the
    /// world and the windows beneath as if it were not there. On, the two
    /// are exclusive and input wins: while the canvas is shown and has a
    /// <see cref="IPluginCanvas.PointerHandler"/>, everything the pointer
    /// does inside the canvas's rectangle goes to that handler and no
    /// further, and the world beneath gets no mouse there. Outside the
    /// rectangle nothing changes either way. On a host without a window
    /// the flag is kept and nothing is ever delivered.
    /// </summary>
    public bool AcceptsPointerInput { get; init; }

    /// <summary>
    /// Whether the canvas can take keyboard focus. Off, the default, the
    /// canvas never sees a key. On, a left press on the canvas (which needs
    /// <see cref="AcceptsPointerInput"/> and a pointer handler as well) or
    /// <see cref="IPluginCanvas.RequestKeyboardFocus"/> gives it the
    /// keyboard while it is shown and has an
    /// <see cref="IPluginCanvas.KeyHandler"/>; until focus goes back, keys
    /// and typed text go to that handler and not to the game. On a host
    /// without a window the flag is kept and the canvas never takes focus.
    /// </summary>
    public bool AcceptsKeyboardInput { get; init; }
}

/// <summary>Which mouse button a pointer event is about.</summary>
public enum PluginPointerButton
{
    /// <summary>No button: a wheel turn, or a move with nothing held.</summary>
    None = 0,

    /// <summary>The left button.</summary>
    Left = 1,

    /// <summary>The right button.</summary>
    Right = 2,

    /// <summary>The middle button, the wheel pressed.</summary>
    Middle = 3,
}

/// <summary>The modifier keys held while a pointer or key event happened.</summary>
[Flags]
public enum PluginKeyModifiers
{
    /// <summary>No modifier key held.</summary>
    None = 0,

    /// <summary>Either shift key.</summary>
    Shift = 1,

    /// <summary>Either control key.</summary>
    Control = 2,

    /// <summary>Either alt key.</summary>
    Alt = 4,
}

/// <summary>What the pointer did over a canvas that takes input.</summary>
public enum PluginPointerEventKind
{
    /// <summary>A button went down inside the canvas. The canvas holds the pointer until the button comes up.</summary>
    Down,

    /// <summary>The button that went down came up, wherever the pointer is by then.</summary>
    Up,

    /// <summary>The pointer moved with the button still held, wherever it is; the position may lie outside the canvas.</summary>
    Move,

    /// <summary>The wheel turned with the pointer over the canvas; <see cref="PluginPointerEvent.WheelDelta"/> says how far.</summary>
    Wheel,

    /// <summary>
    /// A press ended without its <see cref="Up"/>: the canvas was hidden or
    /// removed, or the host took the pointer for something else. No
    /// <see cref="Up"/> follows. A release the plugin asked for through
    /// <see cref="IPluginCanvas.ReleasePointer"/> is not reported.
    /// </summary>
    Cancelled,
}

/// <summary>
/// One pointer event on a canvas, in the canvas's own pixels from its
/// top-left corner, y down, at whatever anchor, offset or interface
/// scale the canvas is shown at.
/// </summary>
/// <param name="Kind">What the pointer did.</param>
/// <param name="Position">Where, in the canvas's own pixels; outside the canvas's rectangle only during a drag.</param>
/// <param name="Button">The button the event is about: the one pressed, released or held during a move; <see cref="PluginPointerButton.None"/> for a wheel turn.</param>
/// <param name="Modifiers">The modifier keys held at the time.</param>
/// <param name="WheelDelta">How far the wheel turned, in notches, positive away from the user; zero for everything but <see cref="PluginPointerEventKind.Wheel"/>.</param>
public readonly record struct PluginPointerEvent(
    PluginPointerEventKind Kind,
    PluginPoint Position,
    PluginPointerButton Button,
    PluginKeyModifiers Modifiers,
    int WheelDelta = 0);

/// <summary>What happened to a canvas that has keyboard focus.</summary>
public enum PluginKeyEventKind
{
    /// <summary>A key went down, or is held and repeating (<see cref="PluginKeyEvent.IsRepeat"/>).</summary>
    Down,

    /// <summary>A key came up.</summary>
    Up,

    /// <summary>The player typed text; <see cref="PluginKeyEvent.Text"/> holds it.</summary>
    Text,

    /// <summary>The canvas took keyboard focus; keys arrive from now on.</summary>
    FocusGained,

    /// <summary>The canvas gave keyboard focus back; no keys arrive until it takes focus again.</summary>
    FocusLost,
}

/// <summary>
/// One keyboard event on a canvas that has keyboard focus.
/// <see cref="Key"/> is meaningful for <see cref="PluginKeyEventKind.Down"/>
/// and <see cref="PluginKeyEventKind.Up"/> and is
/// <see cref="PluginKey.Unknown"/> otherwise; <see cref="Text"/> carries
/// the typed characters for <see cref="PluginKeyEventKind.Text"/> and is
/// null otherwise. Keys <see cref="PluginKey"/> cannot name, the modifier
/// keys among them, are not delivered as <c>Down</c> or <c>Up</c>; the
/// modifiers held travel with every event instead.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Key">The key that went down or came up.</param>
/// <param name="Modifiers">The modifier keys held at the time.</param>
/// <param name="IsRepeat">True for a <c>Down</c> the host repeats while the key stays held.</param>
/// <param name="Text">The characters typed, for <see cref="PluginKeyEventKind.Text"/>; never a control character.</param>
public readonly record struct PluginKeyEvent(
    PluginKeyEventKind Kind,
    PluginKey Key,
    PluginKeyModifiers Modifiers,
    bool IsRepeat = false,
    string? Text = null);

/// <summary>
/// What a canvas's paint callback draws with. Coordinates are pixels from
/// the canvas's own top-left corner, y down; everything is clipped to the
/// canvas. The painter is valid only for the duration of the paint
/// callback: keeping it and drawing later throws.
///
/// <para>Text is drawn in the client's own interface font, at its one
/// size, which is why there is no font or size to choose. Every clip
/// pushed during a paint must be popped before it returns.</para>
/// </summary>
public interface IPluginPainter
{
    /// <summary>The canvas's width in pixels.</summary>
    int Width { get; }

    /// <summary>The canvas's height in pixels.</summary>
    int Height { get; }

    /// <summary>Fills the whole canvas with one colour; transparent black clears it.</summary>
    /// <param name="color">The colour to fill with.</param>
    void Clear(PluginColor color);

    /// <summary>Fills a rectangle.</summary>
    /// <param name="rect">The rectangle to fill.</param>
    /// <param name="color">The fill colour.</param>
    void FillRect(PluginRect rect, PluginColor color);

    /// <summary>Draws a rectangle's outline, inside its edges.</summary>
    /// <param name="rect">The rectangle to outline.</param>
    /// <param name="color">The outline colour.</param>
    /// <param name="thickness">The outline's width in pixels.</param>
    void StrokeRect(PluginRect rect, PluginColor color, float thickness = 1f);

    /// <summary>Draws a straight line, centred on the segment, with square ends.</summary>
    /// <param name="from">Where the line starts.</param>
    /// <param name="to">Where the line ends.</param>
    /// <param name="color">The line's colour.</param>
    /// <param name="thickness">The line's width in pixels.</param>
    void DrawLine(PluginPoint from, PluginPoint to, PluginColor color, float thickness = 1f);

    /// <summary>Draws one line of text with its top-left corner at a position.</summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="position">Where the text's top-left corner goes.</param>
    /// <param name="color">The text colour.</param>
    /// <param name="outline">Whether to draw a dark outline around the glyphs, as the client does over the world.</param>
    void DrawText(string text, PluginPoint position, PluginColor color, bool outline = false);

    /// <summary>How much room one line of text takes when drawn.</summary>
    /// <param name="text">The text to measure.</param>
    /// <returns>The text's width and the font's line height, in pixels.</returns>
    PluginSize MeasureText(string text);

    /// <summary>Draws an image stretched over a rectangle.</summary>
    /// <param name="image">An image from <see cref="IPluginImages"/>; a released or invalid image draws nothing.</param>
    /// <param name="destination">The rectangle the image fills.</param>
    /// <param name="tint">A colour the image is multiplied by; <see cref="PluginColor.White"/> leaves it unchanged.</param>
    void DrawImage(PluginImage image, PluginRect destination, PluginColor tint);

    /// <summary>
    /// Draws an image over a rectangle after scaling and turning that
    /// rectangle about a pivot. The pivot is measured in pixels from the
    /// rectangle's own top-left corner, so (0, 0) turns the image about its
    /// top-left and (width / 2, height / 2) about its middle. Rotation is
    /// clockwise on screen.
    /// </summary>
    /// <param name="image">An image from <see cref="IPluginImages"/>; a released or invalid image draws nothing.</param>
    /// <param name="destination">The rectangle the image would fill unturned and unscaled.</param>
    /// <param name="tint">A colour the image is multiplied by; <see cref="PluginColor.White"/> leaves it unchanged.</param>
    /// <param name="rotationRadians">How far to turn the rectangle about the pivot, in radians, clockwise on screen.</param>
    /// <param name="pivot">The point, in pixels from the rectangle's top-left corner, the rectangle turns and scales about.</param>
    /// <param name="scaleX">How much to stretch the rectangle sideways about the pivot; 1 leaves it.</param>
    /// <param name="scaleY">How much to stretch the rectangle vertically about the pivot; 1 leaves it.</param>
    void DrawImageTransformed(
        PluginImage image,
        PluginRect destination,
        PluginColor tint,
        double rotationRadians,
        PluginPoint pivot,
        double scaleX = 1.0,
        double scaleY = 1.0);

    /// <summary>
    /// Restricts what follows to a rectangle, intersected with whatever clip
    /// is already in force. Every push must be matched by a
    /// <see cref="PopClip"/> before the paint callback returns.
    /// </summary>
    /// <param name="rect">The rectangle to clip to.</param>
    void PushClip(PluginRect rect);

    /// <summary>Undoes the most recent <see cref="PushClip"/>.</summary>
    void PopClip();
}

/// <summary>
/// A rectangle the plugin paints, shown over the world and under every
/// window, taking no input unless it opted in through
/// <see cref="PluginCanvasDescriptor.AcceptsPointerInput"/> or
/// <see cref="PluginCanvasDescriptor.AcceptsKeyboardInput"/>. Painting is retained: the host keeps what was
/// last painted and calls the paint callback again only after
/// <see cref="Invalidate"/>, at most once per frame, on the tick thread,
/// with a painter that is valid only for the duration of that call.
///
/// <para>A paint callback that keeps running over its budget on several
/// frames in a row, throws, or leaves a clip pushed is dropped for the
/// rest of the session and the canvas hidden; the client's log says why.
/// Disposing the canvas removes it. On a host that draws nothing the
/// canvas is accepted, <see cref="IsAvailable"/> is false and the paint
/// callback is never called.</para>
/// </summary>
public interface IPluginCanvas : IDisposable
{
    /// <summary>The plugin's own name for this canvas, as registered.</summary>
    string CanvasId { get; }

    /// <summary>The canvas's width in pixels, as registered.</summary>
    int Width { get; }

    /// <summary>The canvas's height in pixels, as registered.</summary>
    int Height { get; }

    /// <summary>Whether the host draws this canvas at all: false without a window.</summary>
    bool IsAvailable => false;

    /// <summary>Whether the canvas is shown. Set it to show or hide the canvas; what was painted is kept while hidden.</summary>
    bool IsVisible { get; set; }

    /// <summary>Which corner or edge of the screen the canvas is measured from. Set it to move the canvas.</summary>
    PluginCanvasAnchor Anchor { get; set; }

    /// <summary>How far from the anchor the canvas sits, in pixels. Set it to move the canvas.</summary>
    PluginPoint Offset { get; set; }

    /// <summary>
    /// Asks for the paint callback to run again, on the next frame at the
    /// earliest. Calling it several times before that frame paints once.
    /// </summary>
    void Invalidate();

    /// <summary>
    /// Where pointer events go, on a canvas registered with
    /// <see cref="PluginCanvasDescriptor.AcceptsPointerInput"/>. Null, the
    /// default, and the canvas is click-through whatever the descriptor
    /// said: nobody is listening, so nothing is taken. Set, and every
    /// press, held move, release and wheel turn over the canvas arrives
    /// here, on the tick thread, in the canvas's own pixels.
    ///
    /// <para>The handler is measured like the paint callback: one that
    /// keeps running over its budget on several events in a row, or
    /// throws, is dropped for the rest of the session and the canvas goes
    /// back to click-through; painting continues and the client's log says
    /// why. Setting the handler on a canvas that did not opt in, or on a
    /// host without a window, keeps the value and delivers nothing; a host
    /// that predates pointer input answers null and ignores the set.</para>
    /// </summary>
    Action<PluginPointerEvent>? PointerHandler
    {
        get => null;
        set { }
    }

    /// <summary>
    /// Ends the press the canvas is holding, if any: no further
    /// <see cref="PluginPointerEventKind.Move"/> or
    /// <see cref="PluginPointerEventKind.Up"/> arrives for it, and no
    /// <see cref="PluginPointerEventKind.Cancelled"/> is sent for a release
    /// the plugin asked for. Safe to call from inside the handler and at any
    /// other time; does nothing when nothing is held, on a canvas without
    /// input, or on a host without a window.
    /// </summary>
    void ReleasePointer()
    {
    }

    /// <summary>
    /// Where keyboard events go while the canvas has keyboard focus, on a
    /// canvas registered with
    /// <see cref="PluginCanvasDescriptor.AcceptsKeyboardInput"/>. Null, the
    /// default, and the canvas cannot take focus. The handler answers true
    /// when it handled the event; only the answer to
    /// <see cref="PluginKey.Escape"/> going down changes what the host does:
    /// an Escape the handler did not handle gives focus back.
    ///
    /// <para>The handler is measured like the pointer handler: one that
    /// keeps running over its budget on several events in a row, or throws,
    /// is dropped for the rest of the session and focus goes back; pointer
    /// input and painting continue. Setting the handler on a canvas that did
    /// not opt in, or on a host without a window, keeps the value and
    /// delivers nothing; a host that predates keyboard input answers null
    /// and ignores the set.</para>
    /// </summary>
    Func<PluginKeyEvent, bool>? KeyHandler
    {
        get => null;
        set { }
    }

    /// <summary>
    /// Whether the canvas has keyboard focus right now. Always false on a
    /// host without a window or one that predates keyboard input.
    /// </summary>
    bool HasKeyboardFocus => false;

    /// <summary>
    /// Asks for keyboard focus without a press. Succeeds, and answers true,
    /// only when the canvas opted in, is mounted and shown, has a
    /// <see cref="KeyHandler"/>, nothing else in the interface has keyboard
    /// focus, no modal dialog is open and no key rebind is being captured;
    /// it never takes focus from the chat bar, a text field or a dialog.
    /// Answers true at once when the canvas already has focus, and false on
    /// a host without a window or one that predates keyboard input.
    /// </summary>
    /// <returns>True when the canvas has keyboard focus afterwards.</returns>
    bool RequestKeyboardFocus() => false;

    /// <summary>
    /// Gives keyboard focus back, with a
    /// <see cref="PluginKeyEventKind.FocusLost"/> to the handler first. Safe
    /// to call from inside the handler and at any other time; does nothing
    /// when the canvas does not have focus.
    /// </summary>
    void ReleaseKeyboardFocus()
    {
    }
}

/// <summary>
/// The canvas a host that draws nothing hands out: it keeps the state the
/// plugin sets, so the plugin's own logic runs unchanged, and never paints.
/// </summary>
public sealed class NoOpPluginCanvas : IPluginCanvas
{
    /// <summary>Makes an inert canvas that answers with the descriptor's values.</summary>
    /// <param name="descriptor">How the canvas was described when registered.</param>
    public NoOpPluginCanvas(PluginCanvasDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        CanvasId = descriptor.CanvasId;
        Width = descriptor.Width;
        Height = descriptor.Height;
        IsVisible = descriptor.StartVisible;
        Anchor = descriptor.Anchor;
        Offset = descriptor.Offset;
    }

    /// <inheritdoc/>
    public string CanvasId { get; }

    /// <inheritdoc/>
    public int Width { get; }

    /// <inheritdoc/>
    public int Height { get; }

    /// <inheritdoc/>
    public bool IsVisible { get; set; }

    /// <inheritdoc/>
    public PluginCanvasAnchor Anchor { get; set; }

    /// <inheritdoc/>
    public PluginPoint Offset { get; set; }

    /// <summary>True once <see cref="Dispose"/> has been called.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Kept so the plugin's own logic runs unchanged; never called, because nothing is shown to point at.</summary>
    public Action<PluginPointerEvent>? PointerHandler { get; set; }

    /// <summary>Does nothing; there is nothing to paint on.</summary>
    public void Invalidate()
    {
    }

    /// <summary>Does nothing; nothing is ever held.</summary>
    public void ReleasePointer()
    {
    }

    /// <summary>Kept so the plugin's own logic runs unchanged; never called, because there is no keyboard to focus.</summary>
    public Func<PluginKeyEvent, bool>? KeyHandler { get; set; }

    /// <summary>Always false; there is no keyboard to focus.</summary>
    public bool HasKeyboardFocus => false;

    /// <summary>Always false; there is no keyboard to focus.</summary>
    /// <returns>False.</returns>
    public bool RequestKeyboardFocus() => false;

    /// <summary>Does nothing; the canvas never has focus.</summary>
    public void ReleaseKeyboardFocus()
    {
    }

    /// <summary>Marks the canvas disposed; there is nothing to remove.</summary>
    public void Dispose() => IsDisposed = true;
}
