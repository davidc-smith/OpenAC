using AcDream.Rynth.Host;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AcDream.Rynth.Host.Tests;

internal sealed class FakeGameView : IRynthGameView
{
    public uint PlayerId
    {
        get => Throw ? throw new InvalidOperationException("view failed") : _playerId;
        set => _playerId = value;
    }

    private uint _playerId;
    public Dictionary<uint, string> Names { get; } = new();
    public Dictionary<(uint Object, uint Property), long> Int64s { get; } = new();
    public List<(string Text, int ChatType)> ChatLines { get; } = new();
    public bool Throw { get; set; }
    public uint SelectedObjectId { get; set; }
    public List<string> SubmittedCommands { get; } = new();

    public bool SubmitChatCommand(string text)
    {
        SubmittedCommands.Add(text);
        return true;
    }

    public bool TryGetObjectName(uint objectId, out string name)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        return Names.TryGetValue(objectId, out name!);
    }

    public bool TryGetInt64Property(uint objectId, uint propertyId, out long value)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        return Int64s.TryGetValue((objectId, propertyId), out value);
    }

    public void WriteChatLine(string text, int chatType)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        ChatLines.Add((text, chatType));
    }
}
