using DatReaderWriter;
using DatReaderWriter.DBObjs;

namespace AcDream.Content;

/// <summary>
/// The trade-note classes and their values in pyreals, from the portal
/// data's trade-note map. A class is a trade note only if this map lists it;
/// its value is the map's number for it, not the item's own value.
/// </summary>
public static class TradeNoteDenominations
{
    private const uint ClientEnumCategory = 0x10000001u;
    private const uint TradeNoteMapEnum = 0xAu;

    public static IReadOnlyDictionary<uint, int> Empty { get; } = new Dictionary<uint, int>();

    /// <summary>Weenie class id to note value. Empty when the map is missing.</summary>
    public static IReadOnlyDictionary<uint, int> Load(IDatReaderWriter dats)
    {
        ArgumentNullException.ThrowIfNull(dats);

        uint did = RetailDataIdResolver.Resolve(dats, TradeNoteMapEnum, ClientEnumCategory);
        if (did == 0u
            || !dats.Portal.TryGet<DualEnumIDMap>(did, out DualEnumIDMap? map)
            || map is null)
        {
            return Empty;
        }

        var denominations = new Dictionary<uint, int>();
        foreach ((uint value, uint wcid) in map.ClientEnumToID)
        {
            if (wcid != 0u && value is > 0u and <= int.MaxValue)
                denominations[wcid] = (int)value;
        }
        return denominations;
    }
}
