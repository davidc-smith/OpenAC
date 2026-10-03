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
                PlaceOverObject(icons, index, anchor, labelLines, lineHeight, view, projection, viewport, output);
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
        float lineHeight,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector2 viewport,
        List<WorldIconPlacement> output)
    {
        uint objectId = icons[first].Icon.Anchor.ObjectId;
        if (anchor(objectId) is not { } at)
            return;
        Vector3 head = at.BasePosition + new Vector3(0f, 0f, at.Height);
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
