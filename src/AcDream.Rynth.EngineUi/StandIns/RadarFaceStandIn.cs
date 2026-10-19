using RynthCore.Engine.UI.Data;

namespace RynthCore.Engine.ImGuiBackend.Panels
{
    /// <summary>
    /// RynthCore's round Radar panel; not in OpenAC (its outdoor backdrop builds Direct3D 9
    /// world-map tiles). The Dungeon Map's Markers window lists the marker kinds in the
    /// radar's legend order, so only that table is here, copied from theirs.
    /// </summary>
    internal static class RadarFace
    {
        // Legend order: creatures, then corpses, then places, then items.
        internal static readonly (byte Kind, string Label)[] KindRowsOrder =
        {
            (RadarKind.Monster, "Monsters"), (RadarKind.Player, "Players"), (RadarKind.Fellow, "Fellows"),
            (RadarKind.Npc, "NPCs"), (RadarKind.Vendor, "Vendors"), (RadarKind.Pet, "Your pets"),
            (RadarKind.Corpse, "Corpses"), (RadarKind.OwnCorpse, "Your corpses"), (RadarKind.Lifestone, "Lifestones"),
            (RadarKind.Portal, "Portals"), (RadarKind.Door, "Doors"), (RadarKind.GroundItem, "Items on the ground"),
        };
    }
}
