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
}

/// <summary>
/// Markers a plugin draws in the world -- icons over objects or at fixed
/// positions -- in layers it owns. The layers go with the plugin when it is
/// unloaded, and every layer's markers are cleared when the character leaves
/// the world (the layers stay usable).
/// </summary>
public interface IPluginWorldMarkers
{
    /// <summary>
    /// The most icons one plugin may have set at once, across all its layers.
    /// </summary>
    const int MaximumIcons = 256;

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
