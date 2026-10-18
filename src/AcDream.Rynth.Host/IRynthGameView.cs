using System.Numerics;

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

    /// <summary>An object's fields and property tables; false when the client does not know it.</summary>
    bool TryGetObject(uint objectId, out RynthObjectInfo info);

    /// <summary>
    /// The objects inside a container and the objects it wields, ascending by id,
    /// as RynthCore lists them (its inventory walk finds worn items this way).
    /// </summary>
    IReadOnlyList<uint> GetContainerContents(uint containerId);

    /// <summary>The logged-in account's name; empty before the character list.</summary>
    string AccountName { get; }

    /// <summary>The server's world name; empty before the server sends it.</summary>
    string WorldName { get; }

    /// <summary>A world object's cell and its position inside that cell.</summary>
    bool TryGetObjectPosition(uint objectId, out uint cellId, out Vector3 origin);
}
