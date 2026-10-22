namespace AcDream.Plugins.ControlsDemo;

/// <summary>One row of the Repeat window: a tick box, a name, a level menu and a number.</summary>
public sealed class RepeatRow(int id, bool on, int value)
{
    private static readonly string[] LevelNames = ["Low", "Normal", "High"];

    /// <summary>What the window keys its rows by, so a shuffled row keeps its focus and typed text.</summary>
    public int Id { get; } = id;
    public string Label { get; } = $"Rule {id}";
    public bool On { get; private set; } = on;
    public Action<bool> SetOn => v => On = v;
    public IReadOnlyList<string> Levels => LevelNames;
    public string Level { get; private set; } = "Normal";
    public Action<string> SetLevel => v => Level = v;
    public int Value { get; private set; } = value;
    public Action<int> SetValue => v => Value = v;
}

/// <summary>
/// The Repeat window: one row per item of a list, kept by <c>Id</c>. Add,
/// Remove last and Shuffle publish a new list; a row's own controls edit the
/// row object in place. Shuffle always uses the same sequence, so it behaves
/// the same on every run.
/// </summary>
public sealed class RepeatBinding
{
    private readonly Random _random = new(42);
    private int _nextId = 1;

    public RepeatBinding()
    {
        Rows = Enumerable.Range(0, 5).Select(i => NewRow(i)).ToArray();
    }

    public IReadOnlyList<RepeatRow> Rows { get; private set; }

    public Action AddRow => Add;
    public Action RemoveLastRow => RemoveLast;
    public Action ShuffleRows => Shuffle;

    public void Add() => Rows = [.. Rows, NewRow(Rows.Count)];

    public void RemoveLast()
    {
        if (Rows.Count > 0)
            Rows = Rows.Take(Rows.Count - 1).ToArray();
    }

    public void Shuffle()
    {
        RepeatRow[] rows = [.. Rows];
        for (int i = rows.Length - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (rows[i], rows[j]) = (rows[j], rows[i]);
        }
        Rows = rows;
    }

    public string Status => $"{Rows.Count(r => r.On)} of {Rows.Count} on";

    private RepeatRow NewRow(int index) => new(_nextId++, index % 2 == 1, 10 * (index + 1));
}
