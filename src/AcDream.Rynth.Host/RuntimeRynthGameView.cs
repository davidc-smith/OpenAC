using AcDream.Core.Items;
using AcDream.Runtime;

namespace AcDream.Rynth.Host;

/// <summary>The RynthCore table's game data, read from the client's runtime.</summary>
public sealed class RuntimeRynthGameView : IRynthGameView
{
    private readonly GameRuntime _runtime;

    private readonly Func<string, bool>? _submitChatCommand;

    public RuntimeRynthGameView(GameRuntime runtime, Func<string, bool>? submitChatCommand = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _submitChatCommand = submitChatCommand;
    }

    public uint SelectedObjectId => _runtime.ActionOwner.Selection.SelectedObjectId ?? 0u;

    public bool SubmitChatCommand(string text) => _submitChatCommand?.Invoke(text) ?? false;

    public uint PlayerId => _runtime.PlayerIdentity.ServerGuid;

    public bool TryGetObjectName(uint objectId, out string name)
    {
        name = _runtime.InventoryOwner.Objects.Get(objectId)?.Name ?? string.Empty;
        return name.Length > 0;
    }

    // The player's own properties come from the player description first (what
    // the plugin API's TotalExperience reads), then from the player object.
    public bool TryGetInt64Property(uint objectId, uint propertyId, out long value)
    {
        if (objectId != 0 && objectId == PlayerId
            && _runtime.CharacterOwner.LocalPlayer is { HasDescription: true } player
            && player.Properties.Int64s.TryGetValue(propertyId, out value))
        {
            return true;
        }

        ClientObject? item = _runtime.InventoryOwner.Objects.Get(objectId);
        if (item is not null && item.Properties.Int64s.TryGetValue(propertyId, out value))
            return true;

        value = 0;
        return false;
    }

    public void WriteChatLine(string text, int chatType) =>
        _runtime.CommunicationOwner.Chat.OnSystemMessage(text, unchecked((uint)chatType));
}
