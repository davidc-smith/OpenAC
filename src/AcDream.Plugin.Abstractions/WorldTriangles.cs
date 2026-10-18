namespace AcDream.Plugin.Abstractions;

/// <summary>
/// One filled triangle a plugin draws in the world, flat between its three
/// corners. It is see-through by its colour's alpha, drawn from both sides
/// and unlit; hills and walls in front of it hide it. It does not follow the
/// ground: a triangle meant to lie on the ground is lifted a little off it by
/// the caller, as the terrain's own triangles raised about 0.1 m lie on it.
/// Its corners can each have a colour (<c>Color</c> for A, <c>ColorB</c>,
/// <c>ColorC</c>), blended across the face.
/// </summary>
/// <param name="A">The first corner.</param>
/// <param name="B">The second corner.</param>
/// <param name="C">The third corner.</param>
/// <param name="Color">The fill colour; its alpha is how opaque the fill is.</param>
public readonly record struct PluginWorldTriangle(
    PluginNavigationPosition A,
    PluginNavigationPosition B,
    PluginNavigationPosition C,
    PluginColor Color)
{
    /// <summary>
    /// Corner <see cref="B"/>'s colour, blended across the face toward the
    /// other corners; null uses <see cref="Color"/>. A host before contract
    /// <c>0.1.25-fork.8</c> ignores it and fills the triangle in <see cref="Color"/>.
    /// </summary>
    public PluginColor? ColorB { get; init; }

    /// <summary>Corner <see cref="C"/>'s colour, as <see cref="ColorB"/>.</summary>
    public PluginColor? ColorC { get; init; }
}

/// <summary>
/// A set of triangles one caller owns. Setting the triangles replaces the
/// set; disposing the layer removes them from the world.
/// </summary>
public interface IPluginWorldTriangleLayer : IDisposable
{
    /// <summary>
    /// Replaces the layer's triangles with <paramref name="triangles"/>. The
    /// list is copied, so the caller may reuse it. Meant for sets of up to a
    /// few thousand replaced a few times a second, not every frame.
    /// </summary>
    void SetTriangles(IReadOnlyList<PluginWorldTriangle> triangles);
}

/// <summary>Filled triangles a plugin draws in the world, in layers it owns.</summary>
public interface IPluginWorldTriangles
{
    /// <summary>
    /// Creates a layer to draw into, or null when this host draws nothing
    /// (a process with no window).
    /// </summary>
    IPluginWorldTriangleLayer? CreateLayer();
}

/// <summary>The world triangles of a host that draws nothing.</summary>
public sealed class NoOpPluginWorldTriangles : IPluginWorldTriangles
{
    /// <summary>The one shared instance.</summary>
    public static NoOpPluginWorldTriangles Instance { get; } = new();

    /// <inheritdoc />
    public IPluginWorldTriangleLayer? CreateLayer() => null;
}
