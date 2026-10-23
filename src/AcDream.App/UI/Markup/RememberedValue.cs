namespace AcDream.App.UI.Markup;

/// <summary>
/// One value a markup control remembers for the logged-in character, such as
/// an expander's open state. It re-reads after a character switch and writes
/// to whichever character is current, so a control that builds each new value
/// from <see cref="TryRefresh"/> never carries one character's state into
/// another's. Inactive (does nothing) without state or without a control name:
/// unnamed controls aren't remembered.
/// Call <see cref="TryRefresh"/>, and apply any value it reports, immediately
/// before computing a value to remember (e.g. at the start of a click handler),
/// not only once per tick.
/// Every distinct value remembered makes the client rewrite the settings file on
/// the next frame, so a control remembers only when the player commits a change
/// (a drag end, a sort click), never on every drag frame.
/// </summary>
internal sealed class RememberedValue(IMarkupControlState? state, string? control, string kind)
{
    private int _generation = int.MinValue;

    public bool IsActive => state is not null && !string.IsNullOrWhiteSpace(control);

    /// <summary>
    /// True, with the remembered text (null when none), the first time it is
    /// asked and after every character switch; false when nothing changed.
    /// </summary>
    public bool TryRefresh(out string? value)
    {
        value = null;
        if (!IsActive)
            return false;
        int generation = state!.Generation;
        if (generation == _generation)
            return false;
        _generation = generation;
        value = state.Get(control!, kind);
        return true;
    }

    /// <summary>Remembers what the player just chose; null forgets it (back at the default).</summary>
    public void Remember(string? value)
    {
        if (!IsActive)
            return;
        if (value is null)
            state!.Remove(control!, kind);
        else
            state!.Set(control!, kind, value);
    }
}
