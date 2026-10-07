using System.Collections.Generic;
using System.Numerics;
using AcDream.App.Interaction;
using AcDream.App.Plugins;
using AcDream.Core.Selection;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.UI.Layout;

/// <summary>
/// One icon placed on the screen: which icon it is, the square it is fitted
/// into, how far its anchor is from the camera in metres, and how faded it is.
/// </summary>
internal readonly record struct WorldIconPlacement(
    int IconIndex,
    float X,
    float Y,
    float Size,
    float Depth,
    float Alpha);

/// <summary>
/// Where each icon goes on the screen this frame. The same icons, anchors and
/// camera give the same placements, which is what the tests hold it to; the
/// instance only keeps scratch collections so a frame allocates nothing.
/// </summary>
internal sealed class WorldIconLayout
{
    /// <summary>The most icons in one row over an object.</summary>
    internal const int IconsPerRow = 8;

    /// <summary>The gap between icons, between rows, and above the labels, in interface points.</summary>
    internal const float Gap = 2f;

    private static readonly Comparison<WorldIconPlacement> FarToNear = static (left, right) =>
    {
        int byDepth = right.Depth.CompareTo(left.Depth);
        return byDepth != 0 ? byDepth : left.IconIndex.CompareTo(right.IconIndex);
    };

    private readonly HashSet<uint> _placedObjects = [];
    private readonly List<int> _row = [];

    /// <summary>
    /// Fills <paramref name="output"/> with this frame's placements, farthest
    /// first, so that where icons overlap the nearer anchor's are drawn last.
    /// </summary>
    /// <param name="icons">Every icon of every plugin, in the store's order.</param>
    /// <param name="anchor">An object's base and height by server id, or null when the client does not hold it.</param>
    /// <param name="position">A navigation position in world metres, or null when the client cannot place it yet.</param>
    /// <param name="labelLines">How many label lines hang over an object, by server id.</param>
    /// <param name="labelHeightOffset">
    /// How far above an object's head, in metres, its labels hang, by server
    /// id; the icons hang from the same point so they stay above the labels.
    /// </param>
    /// <param name="lineHeight">The label font's line height in interface points.</param>
    /// <param name="view">The camera's view matrix.</param>
    /// <param name="projection">The camera's projection matrix.</param>
    /// <param name="viewport">The size of the screen the world is drawn into.</param>
    /// <param name="output">Cleared, then filled; never reallocated.</param>
    internal void Place(
        IReadOnlyList<WorldIconEntry> icons,
        Func<uint, WorldLabelAnchor?> anchor,
        Func<PluginNavigationPosition, Vector3?> position,
        Func<uint, int> labelLines,
        Func<uint, float> labelHeightOffset,
        float lineHeight,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector2 viewport,
        List<WorldIconPlacement> output)
    {
        output.Clear();
        _placedObjects.Clear();
        for (int index = 0; index < icons.Count; index++)
        {
            PluginMarkerAnchor at = icons[index].Icon.Anchor;
            if (at.Kind == PluginMarkerAnchorKind.Position)
            {
                PlaceAtPosition(icons, index, position, view, projection, viewport, output);
            }
            else if (at.Kind == PluginMarkerAnchorKind.Object && _placedObjects.Add(at.ObjectId))
            {
                PlaceOverObject(
                    icons, index, anchor, labelLines, labelHeightOffset, lineHeight, view, projection, viewport, output);
            }
        }

        output.Sort(FarToNear);
    }

    private static void PlaceAtPosition(
        IReadOnlyList<WorldIconEntry> icons,
        int index,
        Func<PluginNavigationPosition, Vector3?> position,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector2 viewport,
        List<WorldIconPlacement> output)
    {
        PluginWorldIcon icon = icons[index].Icon;
        if (position(icon.Anchor.Position) is not { } point
            || !TryProject(point, view, projection, viewport, out Vector2 centre, out float depth)
            || depth > icon.MaxRange)
        {
            return;
        }

        float size = icon.SizePixels;
        float x = centre.X - size * 0.5f;
        float y = centre.Y - size * 0.5f;
        if (!IsOnScreen(x, y, size, viewport))
            return;
        output.Add(new WorldIconPlacement(index, x, y, size, depth, WorldLabelLayout.Fade(depth, icon.MaxRange)));
    }

    private void PlaceOverObject(
        IReadOnlyList<WorldIconEntry> icons,
        int first,
        Func<uint, WorldLabelAnchor?> anchor,
        Func<uint, int> labelLines,
        Func<uint, float> labelHeightOffset,
        float lineHeight,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector2 viewport,
        List<WorldIconPlacement> output)
    {
        uint objectId = icons[first].Icon.Anchor.ObjectId;
        if (anchor(objectId) is not { } at)
            return;
        // Labels hang from the head raised by their offset; so does the row.
        Vector3 head = at.BasePosition + new Vector3(0f, 0f, at.Height + labelHeightOffset(objectId));
        if (!TryProject(head, view, projection, viewport, out Vector2 centre, out float depth))
            return;

        // Every icon over this object in the order given, less the ones out
        // of range, so a row closes up rather than keeping a hole.
        _row.Clear();
        for (int index = first; index < icons.Count; index++)
        {
            PluginWorldIcon icon = icons[index].Icon;
            if (icon.Anchor.Kind == PluginMarkerAnchorKind.Object
                && icon.Anchor.ObjectId == objectId
                && depth <= icon.MaxRange)
            {
                _row.Add(index);
            }
        }

        // The first row's bottom sits a gap above the object's labels; each
        // further row sits a gap above the tallest icon of the row below.
        float rowBottom = centre.Y - Math.Max(0, labelLines(objectId)) * lineHeight - Gap;
        for (int start = 0; start < _row.Count; start += IconsPerRow)
        {
            int end = Math.Min(start + IconsPerRow, _row.Count);
            float width = Gap * (end - start - 1);
            float height = 0f;
            for (int k = start; k < end; k++)
            {
                float size = icons[_row[k]].Icon.SizePixels;
                width += size;
                height = MathF.Max(height, size);
            }

            float x = centre.X - width * 0.5f;
            for (int k = start; k < end; k++)
            {
                int index = _row[k];
                PluginWorldIcon icon = icons[index].Icon;
                float size = icon.SizePixels;
                float y = rowBottom - size;
                if (IsOnScreen(x, y, size, viewport))
                {
                    output.Add(new WorldIconPlacement(
                        index, x, y, size, depth, WorldLabelLayout.Fade(depth, icon.MaxRange)));
                }
                x += size + Gap;
            }

            rowBottom -= height + Gap;
        }
    }

    private static bool TryProject(
        Vector3 point,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector2 viewport,
        out Vector2 centre,
        out float depth)
    {
        if (!ScreenProjection.TryProjectSphereToScreenRect(
                point, 0f, view, projection, viewport,
                out Vector2 minimum, out Vector2 maximum, out depth, minSidePixels: 0f))
        {
            centre = default;
            return false;
        }

        centre = (minimum + maximum) * 0.5f;
        return true;
    }

    private static bool IsOnScreen(float x, float y, float size, Vector2 viewport) =>
        x + size >= 0f && x <= viewport.X && y + size >= 0f && y <= viewport.Y;
}

/// <summary>The interface texture behind an icon's image, and the image's size in texels.</summary>
internal readonly record struct WorldIconTexture(uint Texture, int Width, int Height);

/// <summary>
/// The one element that draws every icon. Sprite quads are batched into runs
/// by texture in emission order, so it draws every border first -- borders
/// are fills, all on one texture -- and then every image, in the far-to-near
/// order of the placements. A border can then sit under a nearer icon's
/// neighbour; that is the price of not paying a run per bordered icon.
/// </summary>
internal sealed class WorldIconLayerElement : UiElement
{
    private readonly Func<string, PluginImage, WorldIconTexture?> _resolve;
    private IReadOnlyList<WorldIconEntry> _icons = Array.Empty<WorldIconEntry>();
    private IReadOnlyList<WorldIconPlacement> _placements = Array.Empty<WorldIconPlacement>();

    internal WorldIconLayerElement(Func<string, PluginImage, WorldIconTexture?> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        _resolve = resolve;
        ClickThrough = true;
        Anchors = AnchorEdges.None;
    }

    /// <summary>
    /// What to draw from now on. The placements list is read until the next
    /// call and never written by this element.
    /// </summary>
    internal void Present(IReadOnlyList<WorldIconEntry> icons, IReadOnlyList<WorldIconPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(placements);
        _icons = icons;
        _placements = placements;
    }

    /// <summary>How many placements the element is currently drawing.</summary>
    internal int PlacementCount => _placements.Count;

    /// <summary>The placements the element is currently drawing, for tests.</summary>
    internal IReadOnlyList<WorldIconPlacement> Placements => _placements;

    /// <summary>
    /// Where an image of <paramref name="width"/> by <paramref name="height"/>
    /// texels sits inside a square of side <paramref name="size"/>: as large as
    /// fits, keeping its shape, centred.
    /// </summary>
    internal static (float X, float Y, float Width, float Height) Fit(float size, int width, int height)
    {
        float w = size, h = size;
        if (width > height)
            h = size * height / width;
        else if (height > width)
            w = size * width / height;
        return ((size - w) * 0.5f, (size - h) * 0.5f, w, h);
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        IReadOnlyList<WorldIconEntry> icons = _icons;
        IReadOnlyList<WorldIconPlacement> placements = _placements;
        float scale = ctx.PixelScale;

        // Pass 1: every border, one run on the fill texture.
        for (int i = 0; i < placements.Count; i++)
        {
            WorldIconPlacement placement = placements[i];
            WorldIconEntry entry = icons[placement.IconIndex];
            if (entry.Icon.Border is not { } border
                || !TryFit(entry, placement, scale, out _, out float x, out float y, out float w, out float h))
            {
                continue;
            }
            ctx.PushAlpha(placement.Alpha);
            ctx.DrawRectOutline(x - 1f, y - 1f, w + 2f, h + 2f, ToVector(border), 1f);
            ctx.PopAlpha();
        }

        // Pass 2: every image; consecutive icons with one image share a run.
        for (int i = 0; i < placements.Count; i++)
        {
            WorldIconPlacement placement = placements[i];
            WorldIconEntry entry = icons[placement.IconIndex];
            if (!TryFit(entry, placement, scale, out uint texture, out float x, out float y, out float w, out float h))
                continue;
            ctx.PushAlpha(placement.Alpha);
            ctx.DrawSprite(texture, x, y, w, h, 0f, 0f, 1f, 1f, ToVector(entry.Icon.Tint));
            ctx.PopAlpha();
        }
    }

    private bool TryFit(
        WorldIconEntry entry,
        WorldIconPlacement placement,
        float scale,
        out uint texture,
        out float x,
        out float y,
        out float w,
        out float h)
    {
        if (_resolve(entry.OwnerId, entry.Icon.Image) is not { Width: > 0, Height: > 0 } resolved)
        {
            texture = 0u;
            x = y = w = h = 0f;
            return false;
        }

        (float dx, float dy, w, h) = Fit(placement.Size, resolved.Width, resolved.Height);
        // Whole device pixels, so an icon does not shimmer as its anchor drifts.
        x = MathF.Round((placement.X + dx) * scale) / scale;
        y = MathF.Round((placement.Y + dy) * scale) / scale;
        texture = resolved.Texture;
        return true;
    }

    private static Vector4 ToVector(PluginColor color) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
}

/// <summary>
/// Hangs plugin icons in the world on the shared overlay band, on a layer of
/// its own mounted before the label layer, so label text reads over icons.
///
/// <para>Each frame it counts the label lines over each object and how far
/// they are raised, places every icon through <see cref="WorldIconLayout"/>,
/// and hands the result to one drawing element. Two placement lists alternate, as the label overlay's do,
/// so the list being drawn is never the list being written. Icons are not
/// occluded: the interface is drawn after the world.</para>
/// </summary>
internal sealed class WorldIconOverlayController
{
    private readonly UiOverlayLayer _layer;
    private readonly WorldIconLayerElement _element;
    private readonly WorldIconLayout _layout = new();
    private readonly Func<IReadOnlyList<WorldIconEntry>> _icons;
    private readonly Func<uint, WorldLabelAnchor?> _anchor;
    private readonly Func<PluginNavigationPosition, Vector3?> _position;
    private readonly Func<IReadOnlyList<PluginWorldLabel>>? _labels;
    private readonly float _labelLineHeight;
    private readonly Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)> _camera;
    private readonly Func<bool>? _hidden;
    private readonly Dictionary<uint, int> _labelLines = [];
    private readonly Func<uint, int> _labelLinesOf;
    private readonly Dictionary<uint, float> _labelOffsets = [];
    private readonly Func<uint, float> _labelOffsetOf;
    private List<WorldIconPlacement> _front = [];
    private List<WorldIconPlacement> _back = [];

    private WorldIconOverlayController(
        UiOverlayLayer layer,
        WorldIconLayerElement element,
        Func<IReadOnlyList<WorldIconEntry>> icons,
        Func<uint, WorldLabelAnchor?> anchor,
        Func<PluginNavigationPosition, Vector3?> position,
        Func<IReadOnlyList<PluginWorldLabel>>? labels,
        float labelLineHeight,
        Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)> camera,
        Func<bool>? hidden)
    {
        _layer = layer;
        _element = element;
        _icons = icons;
        _anchor = anchor;
        _position = position;
        _labels = labels;
        _labelLineHeight = labelLineHeight;
        _camera = camera;
        _hidden = hidden;
        _labelLinesOf = id => _labelLines.GetValueOrDefault(id);
        _labelOffsetOf = id => _labelOffsets.GetValueOrDefault(id);
    }

    /// <summary>The element doing the drawing, for the tests that count its runs.</summary>
    internal WorldIconLayerElement Element => _element;

    /// <summary>This frame's placements, for tests.</summary>
    internal IReadOnlyList<WorldIconPlacement> Placements => _element.Placements;

    /// <summary>Whether the layer is showing, for tests.</summary>
    internal bool LayerVisible => _layer.Visible;

    /// <summary>Mounts the icon layer on <paramref name="host"/>.</summary>
    /// <param name="hidden">
    /// When it answers true -- the client is showing the portal view instead
    /// of the world -- nothing is placed or drawn that frame. Null never hides.
    /// </param>
    internal static WorldIconOverlayController Mount(
        UiOverlayHost host,
        Func<IReadOnlyList<WorldIconEntry>> icons,
        Func<string, PluginImage, WorldIconTexture?> resolve,
        Func<uint, WorldLabelAnchor?> anchor,
        Func<PluginNavigationPosition, Vector3?> position,
        Func<IReadOnlyList<PluginWorldLabel>>? labels,
        float labelLineHeight,
        Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)> camera,
        Func<bool>? hidden = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(camera);
        UiOverlayLayer layer = host.AddLayer("PluginWorldIconOverlay");
        var element = new WorldIconLayerElement(resolve)
        {
            Name = "PluginWorldIcons",
            Left = 0f,
            Top = 0f,
            Width = layer.Width,
            Height = layer.Height,
        };
        layer.AddChild(element);
        return new WorldIconOverlayController(
            layer, element, icons, anchor, position, labels, labelLineHeight, camera, hidden);
    }

    internal void Tick()
    {
        IReadOnlyList<WorldIconEntry> icons = _icons();
        if (_hidden?.Invoke() == true)
        {
            _back.Clear();
            Present(icons);
            return;
        }

        var camera = _camera();
        if (icons.Count == 0 || camera.Viewport.X <= 0f || camera.Viewport.Y <= 0f)
        {
            _back.Clear();
            Present(icons);
            return;
        }

        _element.Width = camera.Viewport.X;
        _element.Height = camera.Viewport.Y;
        CountLabelLines();
        _layout.Place(
            icons,
            _anchor,
            _position,
            _labelLinesOf,
            _labelOffsetOf,
            _labelLineHeight,
            camera.View,
            camera.Projection,
            camera.Viewport,
            _back);
        Present(icons);
    }

    /// <summary>
    /// How many lines of labels hang over each object -- its highest line plus
    /// one -- and the largest finite upward offset among them, in metres.
    /// </summary>
    private void CountLabelLines()
    {
        _labelLines.Clear();
        _labelOffsets.Clear();
        if (_labels is null)
            return;
        IReadOnlyList<PluginWorldLabel> labels = _labels();
        for (int i = 0; i < labels.Count; i++)
        {
            PluginWorldLabel label = labels[i];
            if (label.ObjectId == 0u)
                continue;
            int lines = Math.Max(0, label.Line) + 1;
            if (lines > _labelLines.GetValueOrDefault(label.ObjectId))
                _labelLines[label.ObjectId] = lines;
            float offset = label.HeightOffset;
            if (float.IsFinite(offset) && offset > _labelOffsets.GetValueOrDefault(label.ObjectId))
                _labelOffsets[label.ObjectId] = offset;
        }
    }

    private void Present(IReadOnlyList<WorldIconEntry> icons)
    {
        (_front, _back) = (_back, _front);
        _element.Present(icons, _front);
        _layer.Visible = _front.Count > 0;
    }
}
