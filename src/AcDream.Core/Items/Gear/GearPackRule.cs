namespace AcDream.Core.Items.Gear;

/// <summary>Send what a saved filter (by name) matches to a pack; "on arrival" also applies it to new items.</summary>
public sealed record GearPackRule(string Filter, uint PackId, bool OnArrival, bool Enabled = true);
