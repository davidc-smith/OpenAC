namespace AcDream.Rynth.Host;

/// <summary>
/// RynthCore's property reads by stat number. The numbers the client keeps in
/// typed fields answer from those (as RynthCore reads its public description
/// first); the rest come from the object's property tables.
/// </summary>
public static class RynthObjectProperties
{
    public static bool TryGetInt(RynthObjectInfo info, uint stat, out int value)
    {
        ArgumentNullException.ThrowIfNull(info);
        int? known = stat switch
        {
            1 => unchecked((int)info.ItemType),
            5 => info.Burden,
            6 => info.ItemsCapacity,
            10 => unchecked((int)info.EquipLocation),
            11 => info.StackSizeMax,
            12 => info.StackSize,
            19 => info.Value,
            91 => info.MaxStructure,
            92 => info.Structure,
            105 => (int)MathF.Round(info.Workmanship, MidpointRounding.AwayFromZero),
            131 => info.MaterialType is uint material ? unchecked((int)material) : null,
            _ => null,
        };
        if (known is int found)
        {
            value = found;
            return true;
        }

        return info.Ints.TryGetValue(stat, out value);
    }

    // Icon, overlay and underlay answer true with 0 for "none", as RynthCore does.
    public static bool TryGetDataId(RynthObjectInfo info, uint stat, out uint value)
    {
        ArgumentNullException.ThrowIfNull(info);
        switch (stat)
        {
            case 8:
                value = info.IconId;
                return true;
            case 50:
                value = info.IconOverlayId;
                return true;
            case 52:
                value = info.IconUnderlayId;
                return true;
            case 28 when info.SpellId is uint spell:
                value = spell;
                return true;
            default:
                return info.DataIds.TryGetValue(stat, out value);
        }
    }

    // 0 is "not set": RynthCore answers false for it.
    public static bool TryGetInstanceId(RynthObjectInfo info, uint stat, out uint value)
    {
        ArgumentNullException.ThrowIfNull(info);
        value = stat switch
        {
            2 => info.ContainerId,
            3 => info.WielderId,
            32 => info.HouseOwnerId ?? 0,
            _ => info.InstanceIds.TryGetValue(stat, out uint found) ? found : 0,
        };
        return value != 0;
    }
}
