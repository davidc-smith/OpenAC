using AcDream.Core.Combat;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;

namespace AcDream.App.UI.Hud;

/// <summary>What the Command Deck asks the game to do. Implemented over the
/// same handlers the retail windows and keybinds use.</summary>
public interface ICommandDeckActions
{
    void ToggleCombat();
    void PressAttack(AttackHeight height);
    void ReleaseAttack();
    void SetDesiredPower(float power);
    void SetOption(CharacterOptionId option, bool value);
    void Recall(RuntimePortalCommand command);
    void CastOutlined();
}
