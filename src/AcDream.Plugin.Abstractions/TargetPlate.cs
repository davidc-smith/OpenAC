namespace AcDream.Plugin.Abstractions;

/// <summary>What the selected object is, as the Target Deck sorts it.</summary>
public enum PluginTargetKind
{
    /// <summary>Nothing is selected.</summary>
    None,
    /// <summary>A hostile creature.</summary>
    Monster,
    /// <summary>A creature the player summoned.</summary>
    Summon,
    /// <summary>Another player.</summary>
    Player,
    /// <summary>A non-player character.</summary>
    Npc,
    /// <summary>An item that can be carried.</summary>
    Item,
    /// <summary>A corpse.</summary>
    Corpse,
    /// <summary>A chest or other container in the world.</summary>
    Container,
    /// <summary>A portal.</summary>
    Portal,
    /// <summary>Any other object in the world.</summary>
    WorldObject,
    /// <summary>The player's own character.</summary>
    Self,
}

/// <summary>
/// The selected object a provider is asked to describe. Vitals are fractions
/// (0..1); stamina and mana are null until an identify has answered with them.
/// </summary>
public readonly record struct PluginTargetPlateQuery(
    uint ObjectId,
    PluginTargetKind Kind,
    string Name,
    uint WeenieClassId,
    int? Level,
    bool OwnedByPlayer,
    bool Identified,
    float? Health,
    float? Stamina,
    float? Mana);

/// <summary>One line a plugin adds to the plate. A null colour uses the plate's ink.</summary>
public readonly record struct PluginTargetPlateRow(string Label, string Value, PluginColor? Color = null, string? Tooltip = null);

/// <summary>One ⋯ menu entry a plugin adds; <see cref="IPluginTargetPlateProvider.OnAction"/> gets its id.</summary>
public readonly record struct PluginTargetPlateAction(string Id, string Label, bool Enabled = true);

/// <summary>What a provider adds for one object. Rows past <see cref="MaximumRows"/> and actions past <see cref="MaximumActions"/> are dropped.</summary>
public sealed record PluginTargetPlateContent(
    IReadOnlyList<PluginTargetPlateRow> Rows,
    IReadOnlyList<PluginTargetPlateAction> Actions)
{
    /// <summary>The most rows one provider may add for an object.</summary>
    public const int MaximumRows = 2;
    /// <summary>The most menu actions one provider may add for an object.</summary>
    public const int MaximumActions = 4;
}

/// <summary>
/// Describes selected objects for the Target Deck. Called on the UI thread when
/// the selection changes, when an identify for the object arrives, and after
/// <see cref="IPluginTargetPlate.Refresh"/>; never every frame. Must return
/// quickly: a provider with a slow lookup returns null, then calls Refresh.
/// </summary>
public interface IPluginTargetPlateProvider
{
    /// <summary>Describes <paramref name="query"/>'s object, or returns null to add nothing for now.</summary>
    PluginTargetPlateContent? Describe(PluginTargetPlateQuery query);
    /// <summary>Called when the player picks one of this provider's actions for <paramref name="objectId"/>.</summary>
    void OnAction(uint objectId, string actionId);
}

/// <summary>The Target Deck's plugin surface.</summary>
public interface IPluginTargetPlate
{
    /// <summary>False while the Modern HUD is off, or on a host with no window.</summary>
    bool IsAvailable { get; }

    /// <summary>Adds a provider; disposing the result removes its rows and actions at once.</summary>
    IDisposable AddProvider(IPluginTargetPlateProvider provider);

    /// <summary>Asks every provider of this plugin to describe <paramref name="objectId"/> again.</summary>
    void Refresh(uint objectId);
}

/// <summary>The target plate of a host that draws nothing.</summary>
public sealed class NoOpPluginTargetPlate : IPluginTargetPlate
{
    /// <summary>The one inert instance.</summary>
    public static NoOpPluginTargetPlate Instance { get; } = new();
    private NoOpPluginTargetPlate() { }
    /// <inheritdoc/>
    public bool IsAvailable => false;
    /// <inheritdoc/>
    public IDisposable AddProvider(IPluginTargetPlateProvider provider) => Nothing.Instance;
    /// <inheritdoc/>
    public void Refresh(uint objectId) { }

    private sealed class Nothing : IDisposable
    {
        public static Nothing Instance { get; } = new();
        public void Dispose() { }
    }
}
