namespace AcDream.App.UI.Hud;

/// <summary>What the Spell Deck asks the game to do: the same calls the retail spell bar makes.</summary>
public interface ISpellDeckActions
{
    void CastSpell(uint spellId);
    void CastItemSpell(uint itemId);
    void SetFavorite(int tab, int position, uint spellId);
    void RemoveFavorite(int tab, uint spellId);
    void ExamineSpell(uint spellId);
    void ShowMessage(string text);
}
