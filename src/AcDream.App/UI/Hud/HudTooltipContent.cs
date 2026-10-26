namespace AcDream.App.UI.Hud;

/// <summary>
/// What a Modern HUD tooltip says: a title, an optional state or value
/// (shown in green), and, on a card, a description and a hint.
/// </summary>
public sealed record HudTooltipContent(string Title, string? State, string? Detail, string? Hint);

/// <summary>A card (title, state badge, wrapped description and hint) or a one-line compact pill.</summary>
public enum HudTooltipStyle { Card, Compact }
