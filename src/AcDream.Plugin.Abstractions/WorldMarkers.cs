namespace AcDream.Plugin.Abstractions;

/// <summary>What a world marker is pinned to.</summary>
public enum PluginMarkerAnchorKind
{
    /// <summary>
    /// Nothing: the default anchor. A marker pinned to nothing is dropped.
    /// </summary>
    None = 0,

    /// <summary>An object the client holds, by the id the server gave it.</summary>
    Object = 1,

    /// <summary>A fixed position in the world.</summary>
    Position = 2,
}

/// <summary>
/// Where a world marker is: over an object, following it wherever it goes,
/// or at a fixed position. Made with <see cref="Object"/> or
/// <see cref="At"/>; the default value pins nothing.
/// </summary>
public readonly record struct PluginMarkerAnchor
{
    private PluginMarkerAnchor(
        PluginMarkerAnchorKind kind,
        uint objectId,
        PluginNavigationPosition position)
    {
        Kind = kind;
        ObjectId = objectId;
        Position = position;
    }

    /// <summary>What the marker is pinned to.</summary>
    public PluginMarkerAnchorKind Kind { get; }

    /// <summary>
    /// The object followed, when <see cref="Kind"/> is
    /// <see cref="PluginMarkerAnchorKind.Object"/>; zero otherwise.
    /// </summary>
    public uint ObjectId { get; }

    /// <summary>
    /// The position marked, when <see cref="Kind"/> is
    /// <see cref="PluginMarkerAnchorKind.Position"/>; the default otherwise.
    /// </summary>
    public PluginNavigationPosition Position { get; }

    /// <summary>
    /// Pins a marker over an object. While the client does not hold the
    /// object the marker is simply not drawn; it comes back when the object
    /// does. An id of zero pins nothing, and the marker is dropped.
    /// </summary>
    /// <param name="objectId">The object's id, as the server gave it.</param>
    public static PluginMarkerAnchor Object(uint objectId) =>
        new(PluginMarkerAnchorKind.Object, objectId, default);

    /// <summary>
    /// Pins a marker to a fixed position, the same kind of position world
    /// lines and navigation use. A position with a coordinate that is not a
    /// finite number pins nothing, and the marker is dropped.
    /// </summary>
    /// <param name="position">Where the marker is.</param>
    public static PluginMarkerAnchor At(PluginNavigationPosition position) =>
        new(PluginMarkerAnchorKind.Position, 0u, position);
}

/// <summary>The form a ground shape takes.</summary>
public enum PluginGroundShapeKind
{
    /// <summary>Nothing: the default. A shape of no kind is dropped.</summary>
    None = 0,

    /// <summary>
    /// A band all the way round the anchor, from
    /// <see cref="PluginGroundShape.Radius"/> less
    /// <see cref="PluginGroundShape.Width"/> out to the radius.
    /// </summary>
    Ring = 1,

    /// <summary>A circle around the anchor, filled from its centre.</summary>
    Disc = 2,

    /// <summary>
    /// Part of a circle, over <see cref="PluginGroundShape.SweepDegrees"/>
    /// from <see cref="PluginGroundShape.StartDegrees"/>: a wedge filled from
    /// the centre, or a band like a ring's.
    /// </summary>
    Arc = 3,
}

/// <summary>
/// A shape lying on the ground around an anchor: a ring under a target, a
/// disc over an area, a wedge in front of a creature. Shapes are drawn in the
/// world, so walls and hills hide them, and outdoors they follow the slope of
/// the land. Made with <see cref="Ring"/>, <see cref="Disc"/> or
/// <see cref="Arc"/>; the default value is of no kind and is dropped.
/// </summary>
public readonly record struct PluginGroundShape
{
    /// <summary>The smallest radius a shape may have, in metres.</summary>
    public const float MinimumRadius = 0.1f;

    /// <summary>The largest radius a shape may have, in metres.</summary>
    public const float MaximumRadius = 100f;

    /// <summary>The width of an arc's band when none is given, in metres.</summary>
    public const float DefaultWidth = 0.15f;

    /// <summary>
    /// Where the shape is: around an object's feet, following it, or around a
    /// fixed position.
    /// </summary>
    public PluginMarkerAnchor Anchor { get; init; }

    /// <summary>What form the shape takes.</summary>
    public PluginGroundShapeKind Kind { get; init; }

    /// <summary>
    /// The shape's outer radius in metres, from <see cref="MinimumRadius"/>
    /// to <see cref="MaximumRadius"/>; a shape outside that is dropped.
    /// </summary>
    public float Radius { get; init; }

    /// <summary>
    /// How wide a ring's or unfilled arc's band is, in metres, inward from
    /// <see cref="Radius"/>. More than zero and at most the radius, or the
    /// shape is dropped. Not used by discs and filled arcs.
    /// </summary>
    public float Width { get; init; }

    /// <summary>
    /// Where an arc starts, as a compass bearing in degrees: 0 is north, 90
    /// is east. With <see cref="FacesObject"/>, measured from the way the
    /// object faces instead. Ignored by rings and discs.
    /// </summary>
    public float StartDegrees { get; init; }

    /// <summary>
    /// How far round an arc goes, clockwise from its start, in degrees: more
    /// than 0 and at most 360, or the arc is dropped. Ignored by rings and
    /// discs.
    /// </summary>
    public float SweepDegrees { get; init; }

    /// <summary>
    /// True for an arc filled from the centre (a wedge); false for a band
    /// like a ring's. Ignored by rings and discs.
    /// </summary>
    public bool Filled { get; init; }

    /// <summary>
    /// True for an arc over an object to turn with it: its start is measured
    /// from the way the object faces. Ignored for rings, discs and arcs at a
    /// fixed position.
    /// </summary>
    public bool FacesObject { get; init; }

    /// <summary>The shape's colour; its alpha is the opacity, 255 solid.</summary>
    public PluginColor Color { get; init; }

    /// <summary>A band all the way round the anchor.</summary>
    /// <param name="anchor">Where the ring is.</param>
    /// <param name="radius">The ring's outer radius, in metres.</param>
    /// <param name="width">How wide the band is, inward from the radius, in metres.</param>
    /// <param name="color">The ring's colour; its alpha is the opacity.</param>
    public static PluginGroundShape Ring(PluginMarkerAnchor anchor, float radius, float width, PluginColor color) => new()
    {
        Anchor = anchor,
        Kind = PluginGroundShapeKind.Ring,
        Radius = radius,
        Width = width,
        SweepDegrees = 360f,
        Color = color,
    };

    /// <summary>A filled circle around the anchor.</summary>
    /// <param name="anchor">Where the disc is.</param>
    /// <param name="radius">The disc's radius, in metres.</param>
    /// <param name="color">The disc's colour; its alpha is the opacity.</param>
    public static PluginGroundShape Disc(PluginMarkerAnchor anchor, float radius, PluginColor color) => new()
    {
        Anchor = anchor,
        Kind = PluginGroundShapeKind.Disc,
        Radius = radius,
        SweepDegrees = 360f,
        Filled = true,
        Color = color,
    };

    /// <summary>
    /// Part of a circle: a wedge filled from the anchor, or a band. Set
    /// <see cref="FacesObject"/> with a <c>with</c> expression for an arc that
    /// turns with its object.
    /// </summary>
    /// <param name="anchor">Where the arc is centred.</param>
    /// <param name="radius">The arc's outer radius, in metres.</param>
    /// <param name="startDegrees">Where the arc starts, as a compass bearing: 0 is north, 90 east.</param>
    /// <param name="sweepDegrees">How far round it goes, clockwise, in degrees.</param>
    /// <param name="filled">True for a wedge filled from the centre; false for a band.</param>
    /// <param name="color">The arc's colour; its alpha is the opacity.</param>
    /// <param name="width">How wide the band is when not filled, in metres.</param>
    public static PluginGroundShape Arc(
        PluginMarkerAnchor anchor,
        float radius,
        float startDegrees,
        float sweepDegrees,
        bool filled,
        PluginColor color,
        float width = DefaultWidth) => new()
    {
        Anchor = anchor,
        Kind = PluginGroundShapeKind.Arc,
        Radius = radius,
        Width = width,
        StartDegrees = startDegrees,
        SweepDegrees = sweepDegrees,
        Filled = filled,
        Color = color,
    };
}

/// <summary>
/// One image hung in the world, drawn at a constant screen size over the
/// interface's view of the world. Icons are not hidden by walls or hills:
/// like labels, they are drawn after the world.
/// </summary>
/// <param name="Anchor">
/// Where the icon is. Over an object, the client lays the object's icons out
/// in rows above its head and above any labels on it, in the order given; at
/// a position, the icon is centred on the position.
/// </param>
/// <param name="Image">
/// The image, from this plugin's own <see cref="IUiRegistry.Images"/>. It is
/// looked up in this plugin's images only. An icon whose image is not valid
/// is dropped; one whose image is released, or dropped when the interface
/// is torn down, is not drawn until the plugin sets its icons again.
/// </param>
public readonly record struct PluginWorldIcon(PluginMarkerAnchor Anchor, PluginImage Image)
{
    /// <summary>The smallest size an icon is drawn at, in interface points.</summary>
    public const float MinimumSize = 8f;

    /// <summary>The largest size an icon is drawn at, in interface points.</summary>
    public const float MaximumSize = 128f;

    /// <summary>
    /// The side of the square the image is fitted into, keeping its shape, in
    /// interface points. Clamped to between <see cref="MinimumSize"/> and
    /// <see cref="MaximumSize"/>; an icon whose size is not a finite number is
    /// dropped.
    /// </summary>
    public float SizePixels { get; init; } = 24f;

    /// <summary>
    /// Multiplied into the image: white leaves it unchanged, and the alpha is
    /// the icon's opacity.
    /// </summary>
    public PluginColor Tint { get; init; } = PluginColor.White;

    /// <summary>A thin frame drawn around the image, or null for none.</summary>
    public PluginColor? Border { get; init; }

    /// <summary>
    /// How far from the camera, in metres, the icon is still drawn. It fades
    /// out over the last fifth of that distance. Must be a positive finite
    /// number, or the icon is dropped.
    /// </summary>
    public float MaxRange { get; init; } = 60f;
}

/// <summary>
/// A set of world markers one plugin owns. Setting a kind of marker replaces
/// that kind's set on this layer; disposing the layer removes its markers
/// from the world.
/// </summary>
public interface IPluginWorldMarkerLayer : IDisposable
{
    /// <summary>
    /// Replaces this layer's icons with <paramref name="icons"/>. The list is
    /// copied and may be reused. Icons pinned to nothing, with an image that
    /// is not valid, or with a size or range that is not a finite number (or
    /// a range that is not positive) are dropped from the set. An empty list
    /// clears the layer's icons.
    /// </summary>
    /// <param name="icons">The icons to show from now on.</param>
    /// <returns>
    /// True when the set was taken. False when the entries given, added to
    /// the icons on this plugin's other layers, come to more than
    /// <see cref="IPluginWorldMarkers.MaximumIcons"/>; the layer then keeps
    /// what it had.
    /// </returns>
    bool SetIcons(IReadOnlyList<PluginWorldIcon> icons);


    /// <summary>
    /// Replaces this layer's ground shapes with <paramref name="shapes"/>.
    /// The list is copied and may be reused. Shapes pinned to nothing, of no
    /// kind, or with a radius, width, start or sweep out of range (see
    /// <see cref="PluginGroundShape"/>) are dropped from the set. An empty
    /// list clears the layer's shapes; its icons are left alone.
    /// </summary>
    /// <param name="shapes">The shapes to show from now on.</param>
    /// <returns>
    /// True when the set was taken. False when the entries given, added to
    /// the shapes on this plugin's other layers, come to more than
    /// <see cref="IPluginWorldMarkers.MaximumShapes"/>; the layer then keeps
    /// what it had.
    /// </returns>
    bool SetShapes(IReadOnlyList<PluginGroundShape> shapes);
}

/// <summary>
/// Markers a plugin draws in the world -- icons over objects or at fixed
/// positions, and shapes on the ground -- in layers it owns. The layers go
/// with the plugin when it is unloaded, and every layer's markers are cleared
/// when the character leaves the world (the layers stay usable).
/// </summary>
public interface IPluginWorldMarkers
{
    /// <summary>
    /// The most icons one plugin may have set at once, across all its layers.
    /// </summary>
    const int MaximumIcons = 256;

    /// <summary>
    /// The most ground shapes one plugin may have set at once, across all its
    /// layers. Counted apart from its icons.
    /// </summary>
    const int MaximumShapes = 256;

    /// <summary>
    /// Creates a layer to draw into, or null when this host draws nothing (a
    /// process with no window).
    /// </summary>
    IPluginWorldMarkerLayer? CreateLayer();
}

/// <summary>The world markers of a host that draws nothing.</summary>
public sealed class NoOpPluginWorldMarkers : IPluginWorldMarkers
{
    /// <summary>The one shared instance.</summary>
    public static NoOpPluginWorldMarkers Instance { get; } = new();

    private NoOpPluginWorldMarkers()
    {
    }

    /// <inheritdoc />
    public IPluginWorldMarkerLayer? CreateLayer() => null;
}
