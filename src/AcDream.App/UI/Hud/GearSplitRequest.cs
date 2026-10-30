namespace AcDream.App.UI.Hud;

/// <summary>What the split picker asks about: which stack, how many it holds, and where the split goes.</summary>
public sealed record GearSplitRequest(uint Stack, string Name, int Max, uint Container, int Slot);
