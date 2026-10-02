using static AcDream.App.UI.PluginUiStyle;

namespace AcDream.App.UI;

/// <summary>A rectangle in the dock's own space.</summary>
internal readonly record struct DockRect(float X, float Y, float W, float H)
{
    public bool Contains(float x, float y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

/// <summary>
/// Where everything in the plugin dock goes, worked out from the mode, whether
/// it is collapsed, which plugin owns each slot, and how tall it may be. Pure:
/// <see cref="PluginSidePanel"/> applies the result to its children.
/// <para>From the top: the handle row, the slots (a divider between two
/// plugins, a gap between one plugin's windows), a divider, the gear slot,
/// and padding. When the slots do not fit, only <see cref="VisibleCount"/> of
/// them from <see cref="FirstRow"/> are laid out; the handle row and gear stay.</para>
/// </summary>
internal sealed record PluginDockLayout(
    float Width,
    float Height,
    IReadOnlyList<DockRect?> Slots,
    IReadOnlyList<float> Dividers,
    DockRect Gear,
    DockRect Handle,
    DockRect Toggle,
    int FirstRow,
    int VisibleCount,
    int MaxFirstRow,
    bool FadeTop,
    bool FadeBottom,
    float ListTop,
    float ListBottom)
{
    private const float DividerBlock = DockDividerSpace * 2f + 1f;

    public static PluginDockLayout Compute(
        PluginDockMode mode, bool collapsed, IReadOnlyList<string> ownerIds, float maxHeight, int firstRow)
    {
        bool rail = mode != PluginDockMode.Floating;
        int count = ownerIds.Count;
        var slots = new DockRect?[count];

        if (collapsed)
        {
            return rail
                ? new(RailTabWidth, RailTabHeight, slots, [], default,
                    new(0f, 0f, RailTabWidth, 14f), new(0f, 14f, RailTabWidth, RailTabHeight - 14f),
                    0, 0, 0, false, false, 0f, 0f)
                : new(DockWidth, DockCollapsedHeight, slots, [], default,
                    new(0f, 0f, DockHandleWidth, DockCollapsedHeight),
                    new(DockHandleWidth, 0f, DockCollapseSize, DockCollapsedHeight),
                    0, 0, 0, false, false, 0f, 0f);
        }

        float width = rail ? RailWidth : DockWidth;
        float slotX = rail ? 0f : DockPadding;
        float slotW = rail ? RailWidth : DockSlot;
        float slotH = rail ? RailSlot : DockSlot;
        float gap = rail ? RailGap : DockGap;
        float Spacing(int i) => i > 0 && ownerIds[i] != ownerIds[i - 1] ? DividerBlock : gap;

        // Everything that is not the slot list: the handle row above it, and the
        // divider, gear and padding below it.
        float chrome = DockSlotsTop + (count > 0 ? DividerBlock : 0f) + slotH + DockPadding;
        float room = maxHeight - chrome;

        int FitFrom(int first)
        {
            int fitted = 0;
            float used = 0f;
            for (int i = first; i < count; i++)
            {
                float next = used + (fitted > 0 ? Spacing(i) : 0f) + slotH;
                if (next > room && fitted > 0) break;
                used = next;
                fitted++;
            }
            return fitted;
        }

        int fitBackward = 0;
        {
            float used = 0f;
            for (int i = count - 1; i >= 0; i--)
            {
                float next = used + (fitBackward > 0 ? Spacing(i + 1) : 0f) + slotH;
                if (next > room && fitBackward > 0) break;
                used = next;
                fitBackward++;
            }
        }
        int maxFirst = Math.Max(0, count - fitBackward);
        int first = Math.Clamp(firstRow, 0, maxFirst);
        int visible = count == 0 ? 0 : FitFrom(first);

        var dividers = new List<float>();
        float y = DockSlotsTop;
        for (int i = first; i < first + visible; i++)
        {
            if (i > first)
            {
                float spacing = Spacing(i);
                if (spacing == DividerBlock) dividers.Add(y + DockDividerSpace + 0.5f);
                y += spacing;
            }
            slots[i] = new DockRect(slotX, y, slotW, slotH);
            y += slotH;
        }
        float listBottom = y;
        if (count > 0)
        {
            dividers.Add(y + DockDividerSpace + 0.5f);
            y += DividerBlock;
        }
        var gear = new DockRect(slotX, y, slotW, slotH);
        float height = y + slotH + DockPadding;

        return new(width, height, slots, dividers, gear,
            new(0f, 0f, DockHandleWidth, DockSlotsTop),
            new(width - DockPadding - DockCollapseSize, DockHandleTop, DockCollapseSize, DockHandleHeight),
            first, visible, maxFirst, first > 0, first + visible < count, DockSlotsTop, listBottom);
    }
}
