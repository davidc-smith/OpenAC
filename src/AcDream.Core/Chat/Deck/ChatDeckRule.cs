namespace AcDream.Core.Chat.Deck;

public enum ChatDeckMatchKind { Contains, Regex, MyName, Preset }

/// <summary>Which part of a line a rule reads.</summary>
public enum ChatDeckMatchField
{
    /// <summary>The message body, without sender or channel prefix.</summary>
    Text,
    Sender,
    /// <summary>The whole line as worded on screen (<see cref="ChatLineWording.Format"/>).</summary>
    Line,
}

public enum ChatDeckRuleAction { Hide, MoveTo, CopyTo, ColourLine, ColourMatch }

public sealed record ChatDeckRule
{
    public static readonly IReadOnlySet<ChatDeckChannel> AnyChannel = new HashSet<ChatDeckChannel>();

    public required string Id { get; init; }
    public bool Enabled { get; init; } = true;
    public ChatDeckMatchKind Kind { get; init; }
    public string Pattern { get; init; } = "";
    public string PresetId { get; init; } = "";
    public ChatDeckMatchField Field { get; init; } = ChatDeckMatchField.Text;
    /// <summary>Channels the rule applies to; empty means any. Ignored for presets (they carry their own).</summary>
    public IReadOnlySet<ChatDeckChannel> Channels { get; init; } = AnyChannel;
    public ChatDeckRuleAction Action { get; init; }
    /// <summary>Tab id for <see cref="ChatDeckRuleAction.MoveTo"/> / <see cref="ChatDeckRuleAction.CopyTo"/>.</summary>
    public string TargetTabId { get; init; } = "";
    /// <summary>0xAARRGGBB for the colour actions.</summary>
    public uint Colour { get; init; }
    public bool Alert { get; init; }
    /// <summary>Set when the rule timed out on a line; it stays off until edited.</summary>
    public bool Faulted { get; init; }
}
