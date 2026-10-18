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

    public Dictionary<uint, RynthObjectInfo> Objects { get; } = new();
    public Dictionary<uint, List<uint>> Contents { get; } = new();
    public Dictionary<uint, (uint Cell, System.Numerics.Vector3 Origin)> Positions { get; } = new();
    public string AccountName { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;

    public bool TryGetObject(uint objectId, out RynthObjectInfo info)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        return Objects.TryGetValue(objectId, out info!);
    }

    public IReadOnlyList<uint> GetContainerContents(uint containerId)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        return Contents.TryGetValue(containerId, out List<uint>? ids) ? ids : [];
    }

    public bool TryGetObjectPosition(uint objectId, out uint cellId, out System.Numerics.Vector3 origin)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        bool found = Positions.TryGetValue(objectId, out var position);
        (cellId, origin) = position;
        return found;
    }
}
