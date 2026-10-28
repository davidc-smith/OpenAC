using AcDream.Core.Targeting;

namespace AcDream.App.UI.Hud;

/// <summary>What the Target Deck asks the game to do.</summary>
public interface ITargetDeckActions
{
    void Run(TargetAction action, TargetPlateModel target);
    void RunPluginAction(string pluginId, uint objectId, string actionId);
}
