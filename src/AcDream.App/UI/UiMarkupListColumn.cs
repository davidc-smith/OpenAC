namespace AcDream.App.UI;

public enum UiMarkupListColumnKind
{
    Text,
    Check,
    Icon,
    Button,
}

public enum UiMarkupListSortKind
{
    None,
    Text,
    Number,
    Bool,
}

public sealed class UiMarkupListColumn
{
    public UiMarkupListColumnKind Kind { get; internal init; }
    public float Width { get; internal init; }
    public bool IsAutoWidth { get; internal init; }

    public Func<IReadOnlyList<string>>? TextSource { get; internal init; }
    public Func<IReadOnlyList<uint>>? ColorsSource { get; internal init; }
    public Action<int>? TextClicked { get; internal init; }

    public Func<IReadOnlyList<bool>>? CheckSource { get; internal init; }
    public Action<int>? CheckChanged { get; internal init; }

    /// <summary>A check column drawn as tick boxes (<c>type="checkbox"</c>) instead of lamps.</summary>
    public bool Boxed { get; internal init; }

    public Func<IReadOnlyList<uint>>? IconValuesSource { get; internal init; }
    public Func<uint, (uint tex, int w, int h)>? IconResolve { get; internal init; }
    public Action<int>? IconClicked { get; internal init; }

    public Action<int>? ButtonClicked { get; internal init; }

    /// <summary>A button's size inside its cell; 0 fills the cell. Never larger than the cell.</summary>
    public float ButtonWidth { get; internal init; }
    public float ButtonHeight { get; internal init; }

    /// <summary>The column's header caption (a literal or a bound string); null for none.</summary>
    public Func<string?>? HeaderSource { get; internal set; }

    /// <summary>How a header click sorts by this column; None is not sortable.</summary>
    public UiMarkupListSortKind Sort { get; internal set; }

    /// <summary>Keys that replace the cell values when sorting (one of the two, or neither).</summary>
    public Func<IReadOnlyList<double>>? SortNumberKeys { get; internal set; }
    public Func<IReadOnlyList<string>>? SortTextKeys { get; internal set; }

    /// <summary>A checkbox column whose header is a select-all box (<c>header="all"</c>).</summary>
    public bool SelectAll { get; internal set; }

    /// <summary>Called once with the value every row should take; null draws a blank header cell.</summary>
    public Action<bool>? SelectAllChanged { get; internal set; }

    public static UiMarkupListColumn Text(
        float width,
        Func<IReadOnlyList<string>> textSource,
        Func<IReadOnlyList<uint>>? colorsSource,
        Action<int>? onClick = null,
        bool isAutoWidth = false) => new()
    {
        Kind = UiMarkupListColumnKind.Text,
        Width = width,
        IsAutoWidth = isAutoWidth,
        TextSource = textSource,
        ColorsSource = colorsSource,
        TextClicked = onClick,
    };

    public static UiMarkupListColumn Check(
        float width,
        Func<IReadOnlyList<bool>> checkSource,
        Action<int> onChange,
        bool isAutoWidth = false,
        bool boxed = false) => new()
    {
        Kind = UiMarkupListColumnKind.Check,
        Width = width,
        IsAutoWidth = isAutoWidth,
        CheckSource = checkSource,
        CheckChanged = onChange,
        Boxed = boxed,
    };

    public static UiMarkupListColumn Icon(
        float width,
        Func<IReadOnlyList<uint>> valuesSource,
        Func<uint, (uint tex, int w, int h)>? resolve,
        Action<int> onClick,
        bool isAutoWidth = false) => new()
    {
        Kind = UiMarkupListColumnKind.Icon,
        Width = width,
        IsAutoWidth = isAutoWidth,
        IconValuesSource = valuesSource,
        IconResolve = resolve,
        IconClicked = onClick,
    };

    /// <summary>
    /// A column of buttons: each row's caption is drawn on a button face, an
    /// empty caption draws no button, and a click on one fires
    /// <paramref name="onClick"/> with the row index without selecting the row.
    /// A button <paramref name="buttonWidth"/> by <paramref name="buttonHeight"/>
    /// is centred in its cell; 0 fills the cell that way.
    /// </summary>
    public static UiMarkupListColumn Button(
        float width,
        Func<IReadOnlyList<string>> captionSource,
        Action<int> onClick,
        bool isAutoWidth = false,
        float buttonWidth = 0f,
        float buttonHeight = 0f) => new()
    {
        Kind = UiMarkupListColumnKind.Button,
        Width = width,
        IsAutoWidth = isAutoWidth,
        TextSource = captionSource,
        ButtonClicked = onClick,
        ButtonWidth = buttonWidth,
        ButtonHeight = buttonHeight,
    };

    public int RowCount() => Kind switch
    {
        UiMarkupListColumnKind.Text or UiMarkupListColumnKind.Button => TextSource?.Invoke().Count ?? 0,
        UiMarkupListColumnKind.Check => CheckSource?.Invoke().Count ?? 0,
        UiMarkupListColumnKind.Icon => IconValuesSource?.Invoke().Count ?? 0,
        _ => 0,
    };
}
