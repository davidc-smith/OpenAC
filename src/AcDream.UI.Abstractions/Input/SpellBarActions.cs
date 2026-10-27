namespace AcDream.UI.Abstractions.Input;

/// <summary>The Spell Deck's hotkey actions: row 0 is the retail
/// <c>UseSpellSlot_1..12</c>, rows 1-9 the client-only <c>UseSpellRow{n}Slot{m}</c>.
/// Rows and slots are zero-based here.</summary>
public static class SpellBarActions
{
    public const int Rows = 10;
    public const int SlotsPerRow = 12;

    private static readonly InputAction[] Row0 =
    [
        InputAction.UseSpellSlot_1, InputAction.UseSpellSlot_2, InputAction.UseSpellSlot_3,
        InputAction.UseSpellSlot_4, InputAction.UseSpellSlot_5, InputAction.UseSpellSlot_6,
        InputAction.UseSpellSlot_7, InputAction.UseSpellSlot_8, InputAction.UseSpellSlot_9,
        InputAction.UseSpellSlot_10, InputAction.UseSpellSlot_11, InputAction.UseSpellSlot_12,
    ];

    public static InputAction ForSlot(int row, int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, SlotsPerRow);
        return row == 0
            ? Row0[slot]
            : InputAction.UseSpellRow2Slot1 + (row - 1) * SlotsPerRow + slot;
    }

    public static bool TryParse(InputAction action, out int row, out int slot)
    {
        int index = Array.IndexOf(Row0, action);
        if (index >= 0) { row = 0; slot = index; return true; }
        int offset = action - InputAction.UseSpellRow2Slot1;
        if (offset >= 0 && offset < (Rows - 1) * SlotsPerRow)
        {
            row = 1 + offset / SlotsPerRow;
            slot = offset % SlotsPerRow;
            return true;
        }
        row = slot = -1;
        return false;
    }
}
