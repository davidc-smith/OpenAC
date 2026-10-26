using AcDream.UI.Abstractions.Input;

namespace AcDream.App.UI.Hud;

/// <summary>
/// Where a spell key goes. With the Spell Deck active it has the spell
/// actions; otherwise the retail spell bar has them as before, and a
/// client-only Spell Deck action (rows 2-10, <see cref="InputAction.CastItemSpell"/>)
/// is replaced by the Game-scope action bound to the same key, so a key the
/// Spell Deck took over in magic (Ctrl+1 for row 3) still does its retail job
/// when the Spell Deck is not there.
/// </summary>
public static class SpellActionRouter
{
    /// <summary>True when the action was handled (the caller stops there).</summary>
    /// <param name="spellDeck">The Spell Deck's handler, used while <paramref name="spellDeckActive"/>.</param>
    /// <param name="retail">The retail spell bar's handler, used otherwise.</param>
    /// <param name="gameAction">The Game-scope action bound to the key that fired, or null.</param>
    /// <param name="redispatch">Sends that Game-scope action through input routing again.</param>
    public static bool Route(
        InputAction action,
        bool spellDeckActive,
        Func<InputAction, bool> spellDeck,
        Func<InputAction, bool> retail,
        Func<InputAction?> gameAction,
        Action<InputAction> redispatch)
    {
        if (spellDeckActive) return spellDeck(action);
        if (!IsClientOnly(action)) return retail(action);

        // Never re-send a client-only action: it would come straight back here.
        if (gameAction() is { } game && game != InputAction.None && !IsClientOnly(game))
            redispatch(game);
        return true;
    }

    /// <summary>
    /// The Game-scope action bound to <paramref name="chord"/> (the key that
    /// fired <paramref name="action"/>), or null. The chord must be bound to
    /// <paramref name="action"/>, so an automation press never reuses a stale key.
    /// </summary>
    public static InputAction? GameActionOnSameKey(KeyBindings bindings, KeyChord? chord, InputAction action)
    {
        if (chord is not { } key) return null;
        bool fromThisKey = false;
        foreach (Binding binding in bindings.All)
            if (binding.Chord == key && binding.Action == action) { fromThisKey = true; break; }
        return fromThisKey ? bindings.Find(key, ActivationType.Press, InputScope.Game)?.Action : null;
    }

    /// <summary>The actions only the Spell Deck knows: spell bar rows 2-10 and the item spell.</summary>
    public static bool IsClientOnly(InputAction action) =>
        action == InputAction.CastItemSpell
        || (SpellBarActions.TryParse(action, out int row, out _) && row >= 1);
}
