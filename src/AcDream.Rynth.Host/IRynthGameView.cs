namespace AcDream.Rynth.Host;

/// <summary>
/// The game data the RynthCore table functions read. The client implements it
/// over <c>GameRuntime</c> (<see cref="RuntimeRynthGameView"/>); tests use a fake.
/// </summary>
public interface IRynthGameView
{
    /// <summary>The player's object id; 0 before login.</summary>
    uint PlayerId { get; }

    /// <summary>The name of a known object.</summary>
    bool TryGetObjectName(uint objectId, out string name);

    /// <summary>An Int64 ("quad") property of an object, by property id.</summary>
    bool TryGetInt64Property(uint objectId, uint propertyId, out long value);

    /// <summary>The selected object's id, 0 when nothing is selected.</summary>
    uint SelectedObjectId { get; }

    /// <summary>Runs text as if typed in the chat bar. True when the client accepted it.</summary>
    bool SubmitChatCommand(string text);

    /// <summary>Adds a line to the chat window.</summary>
    void WriteChatLine(string text, int chatType);
}
