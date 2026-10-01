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

/// <summary>
/// Where a canvas sits in the interface's stacking order. The layer is
/// chosen when the canvas is registered and does not change.
/// </summary>
public enum PluginCanvasLayer
{
    /// <summary>
    /// Over the world and its labels, under every window: a HUD that never
    /// hides the interface. The default.
    /// </summary>
    World = 0,

    /// <summary>
    /// Over every window, under dialogs, tooltips, menus and the item being
    /// dragged. A canvas here that takes input takes the pointer from the
    /// windows beneath its rectangle.
    /// </summary>
    AboveWindows = 1,
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
    /// Which layer the canvas is drawn in: <see cref="PluginCanvasLayer.World"/>,
    /// the default, under every window, or
    /// <see cref="PluginCanvasLayer.AboveWindows"/>. Fixed at registration.
    /// A host that predates layers draws every canvas in the world layer.
    /// </summary>
    public PluginCanvasLayer Layer { get; init; } = PluginCanvasLayer.World;

    /// <summary>
    /// The canvas's starting place among this plugin's canvases in the same
    /// layer: higher is drawn on top, and canvases with equal values keep the
    /// order they were registered in. It orders a plugin's own canvases
    /// only; it never lifts one plugin's canvas over another plugin's. Change
    /// it later through <see cref="IPluginCanvas.ZOrder"/>.
    /// </summary>
    public int ZOrder { get; init; }
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
/// <para>Shapes -- polygons, rounded rectangles, ellipses, circles and
/// gradients -- have edges anti-aliased over one screen pixel; the other
/// primitives have hard edges. A shape given input it cannot draw draws
/// nothing and never throws; the client's log says so once per canvas.</para>
///
/// <para>Text is drawn in the client's own interface font unless a
/// <see cref="PluginFont"/> from <see cref="IPluginFonts"/> is given. Every
/// clip pushed during a paint must be popped before it returns.</para>
///
/// <para>On a high-density display the host paints the canvas at more than
/// one screen pixel per canvas pixel (<see cref="PixelScale"/>), so shapes
/// and text in a <see cref="PluginFont"/> come out sharp. Coordinates and
/// sizes stay in canvas pixels whatever the scale.</para>
/// </summary>
public interface IPluginPainter
{
    /// <summary>The canvas's width in pixels.</summary>
    int Width { get; }

    /// <summary>The canvas's height in pixels.</summary>
    int Height { get; }

    /// <summary>
    /// How many screen pixels one canvas pixel covers in this paint: 2 on a
    /// typical high-density display, 1 on a host that does not scale.
    /// Coordinates are canvas pixels either way; read this only to align a
    /// detail to screen pixels, for example a hairline one screen pixel
    /// wide (1 / <see cref="PixelScale"/> canvas pixels). It can change
    /// between paints, when the window moves to another display; the host
    /// repaints the canvas when it does.
    /// </summary>
    double PixelScale => 1.0;

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

    /// <summary>
    /// Draws one line of text in a font from <see cref="IPluginFonts"/>, with
    /// its top-left corner at a position. A released, dropped or invalid font
    /// draws nothing. A host that predates fonts draws the text in the
    /// interface font instead.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="position">Where the text's top-left corner goes.</param>
    /// <param name="color">The text colour.</param>
    /// <param name="font">The font to draw in.</param>
    /// <param name="outline">Whether to draw a dark outline around the glyphs.</param>
    void DrawText(string text, PluginPoint position, PluginColor color, PluginFont font, bool outline = false) =>
        DrawText(text, position, color, outline);

    /// <summary>
    /// How much room one line of text takes in a font from
    /// <see cref="IPluginFonts"/>: its advance width, kerning included, and
    /// the font's line height. (0, 0) for a released, dropped or invalid
    /// font; a host that predates fonts answers with the interface font's
    /// measure.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="font">The font to measure in.</param>
    /// <returns>The text's width and the font's line height, in pixels.</returns>
    PluginSize MeasureText(string text, PluginFont font) => MeasureText(text);

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

    /// <summary>
    /// Fills a convex polygon in one colour, with anti-aliased edges. The
    /// points go round the outline in order, either way round, 3 to 64 of
    /// them. Too few or too many points, or an outline that is not convex,
    /// draws nothing. A host that predates shapes draws nothing.
    /// </summary>
    /// <param name="points">The polygon's corners, in order round its outline.</param>
    /// <param name="color">The fill colour.</param>
    void FillPolygon(ReadOnlySpan<PluginPoint> points, PluginColor color)
    {
    }

    /// <summary>
    /// Fills a convex polygon with a colour at each corner, blended across
    /// the polygon, with anti-aliased edges; otherwise as the one-colour
    /// overload. A colour count that differs from the point count draws
    /// nothing. Colours blend as given, alpha included: to fade a colour
    /// out, fade to the same colour with zero alpha
    /// (<c>new PluginColor(r, g, b, 0)</c>), not to
    /// <see cref="PluginColor.Transparent"/>, which is transparent black and
    /// darkens the middle of the fade. A host that predates shapes draws
    /// nothing.
    /// </summary>
    /// <param name="points">The polygon's corners, in order round its outline.</param>
    /// <param name="colors">One colour per corner, in the same order as the points.</param>
    void FillPolygon(ReadOnlySpan<PluginPoint> points, ReadOnlySpan<PluginColor> colors)
    {
    }

    /// <summary>
    /// Fills a rectangle with rounded corners and anti-aliased edges. A host
    /// that predates shapes draws nothing.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="radii">Each corner's radius; see <see cref="PluginCornerRadii"/>.</param>
    /// <param name="color">The fill colour.</param>
    void FillRoundedRect(PluginRect rect, PluginCornerRadii radii, PluginColor color)
    {
    }

    /// <summary>
    /// Draws a rounded rectangle's outline, centred on the outline so half
    /// the thickness falls outside the rectangle, with anti-aliased edges.
    /// Outside a square corner the outline stays square. A host that
    /// predates shapes draws nothing.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="radii">Each corner's radius; see <see cref="PluginCornerRadii"/>.</param>
    /// <param name="color">The outline colour.</param>
    /// <param name="thickness">The outline's width in pixels; thinner than one screen pixel is drawn one screen pixel wide and proportionally fainter.</param>
    void StrokeRoundedRect(PluginRect rect, PluginCornerRadii radii, PluginColor color, float thickness = 1f)
    {
    }

    /// <summary>
    /// Fills the ellipse that fits a rectangle, with an anti-aliased edge. A
    /// host that predates shapes draws nothing.
    /// </summary>
    /// <param name="bounds">The rectangle the ellipse fits.</param>
    /// <param name="color">The fill colour.</param>
    void FillEllipse(PluginRect bounds, PluginColor color)
    {
    }

    /// <summary>
    /// Draws the outline of the ellipse that fits a rectangle, centred on the
    /// outline, with anti-aliased edges. A host that predates shapes draws
    /// nothing.
    /// </summary>
    /// <param name="bounds">The rectangle the ellipse fits.</param>
    /// <param name="color">The outline colour.</param>
    /// <param name="thickness">The outline's width in pixels; thinner than one screen pixel is drawn one screen pixel wide and proportionally fainter.</param>
    void StrokeEllipse(PluginRect bounds, PluginColor color, float thickness = 1f)
    {
    }

    /// <summary>
    /// Fills a circle, with an anti-aliased edge: the ellipse in the square
    /// around it. A host that predates shapes draws nothing.
    /// </summary>
    /// <param name="center">The circle's centre.</param>
    /// <param name="radius">The circle's radius in pixels.</param>
    /// <param name="color">The fill colour.</param>
    void FillCircle(PluginPoint center, double radius, PluginColor color) =>
        FillEllipse(new PluginRect(center.X - radius, center.Y - radius, radius * 2, radius * 2), color);

    /// <summary>
    /// Fills a rectangle blending from one colour at one edge to another at
    /// the opposite edge, with anti-aliased edges. Colours blend as given,
    /// alpha included: to fade a colour out, fade to the same colour with
    /// zero alpha (<c>new PluginColor(r, g, b, 0)</c>), not to
    /// <see cref="PluginColor.Transparent"/>, which is transparent black and
    /// darkens the middle of the fade. A host that predates shapes draws
    /// nothing.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="from">The colour at the left or top edge.</param>
    /// <param name="to">The colour at the right or bottom edge.</param>
    /// <param name="direction">Whether the blend runs across the rectangle or down it.</param>
    void FillRectGradient(PluginRect rect, PluginColor from, PluginColor to, PluginGradientDirection direction)
    {
    }
}

/// <summary>
/// A rectangle the plugin paints, shown over the world and under every
/// window (or, in <see cref="PluginCanvasLayer.AboveWindows"/>, over every
/// window), taking no input unless it opted in through
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
    /// The canvas's place among this plugin's canvases in the same layer:
    /// higher is drawn on top, and equal values keep registration order.
    /// Starts at <see cref="PluginCanvasDescriptor.ZOrder"/>; set it to
    /// restack, from the next frame. A host that predates layers answers 0
    /// and ignores the set.
    /// </summary>
    int ZOrder
    {
        get => 0;
        set { }
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
        ZOrder = descriptor.ZOrder;
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

    /// <summary>Kept so the plugin's own logic runs unchanged; nothing is stacked.</summary>
    public int ZOrder { get; set; }

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
