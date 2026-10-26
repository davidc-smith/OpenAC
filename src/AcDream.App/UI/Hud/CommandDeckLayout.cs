using AcDream.Core.Combat;

namespace AcDream.App.UI.Hud;

public readonly record struct DeckRect(float X, float Y, float W, float H)
{
    public bool Contains(float x, float y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

public enum DeckZone
{
    None,
    Orb,
    StudRepeat, StudAutoTarget, StudKeepInView,
    Health, Stamina, Mana,
    PowerTrack, High, Medium, Low,
    Recast,
    Recall0, Recall1, Recall2, Recall3, Recall4,
    Jump,
}

/// <summary>The deck's fixed geometry in points. Nothing here depends on the
/// combat mode except which zones are live, so the window never changes size.</summary>
public static class CommandDeckLayout
{
    public const float Width = 420f;
    public const float Height = 150f;

    public static DeckRect Frame { get; } = new(70f, 4f, 350f, 142f);
    public static DeckRect Orb { get; } = new(32f, 29f, 92f, 92f);

    private const float ContentLeft = 134f;
    private const float ContentRight = 408f;
    private const float LabelWidth = 60f;
    private const float RowValueLeft = ContentLeft + LabelWidth + 6f;   // 200

    public static DeckRect HealthBar { get; } = new(ContentLeft, 16f, ContentRight - ContentLeft, 20f);
    public static DeckRect StaminaBar { get; } = new(ContentLeft, 41f, ContentRight - ContentLeft, 20f);
    public static DeckRect ManaBar { get; } = new(ContentLeft, 66f, ContentRight - ContentLeft, 20f);

    public static DeckRect ContextRow { get; } = new(ContentLeft, 92f, ContentRight - ContentLeft, 22f);
    public static DeckRect RowLabel { get; } = new(ContentLeft, 92f, LabelWidth, 22f);

    public static DeckRect PowerTrack { get; } = new(RowValueLeft, 98f, 118f, 10f);
    public static DeckRect HeightButton(int index) => new(326f + index * 28f, 92f, 26f, 22f);

    public static DeckRect CastingText { get; } = new(RowValueLeft, 92f, 128f, 22f);
    public static DeckRect Recast { get; } = new(334f, 92f, 74f, 22f);

    public static DeckRect RecallStud(int index) => new(RowValueLeft + index * 30f, 92f, 26f, 22f);

    public static DeckRect JumpLabel { get; } = new(ContentLeft, 122f, LabelWidth, 14f);
    public static DeckRect JumpTrack { get; } = new(RowValueLeft, 125f, ContentRight - RowValueLeft, 8f);

    public static DeckRect Stud(int index) => new(2f, 44f + index * 26f, 26f, 22f);

    public static IEnumerable<DeckRect> AllRects()
    {
        yield return Frame; yield return Orb;
        yield return HealthBar; yield return StaminaBar; yield return ManaBar;
        yield return ContextRow; yield return RowLabel; yield return PowerTrack;
        for (int i = 0; i < 3; i++) yield return HeightButton(i);
        yield return CastingText; yield return Recast;
        for (int i = 0; i < 5; i++) yield return RecallStud(i);
        yield return JumpLabel; yield return JumpTrack;
        for (int i = 0; i < 3; i++) yield return Stud(i);
    }

    /// <summary>The rectangle a zone's tooltip points at; the whole frame for <see cref="DeckZone.None"/>.</summary>
    internal static DeckRect RectFor(DeckZone zone) => zone switch
    {
        DeckZone.Orb => Orb,
        DeckZone.StudRepeat => Stud(0),
        DeckZone.StudAutoTarget => Stud(1),
        DeckZone.StudKeepInView => Stud(2),
        DeckZone.Health => HealthBar,
        DeckZone.Stamina => StaminaBar,
        DeckZone.Mana => ManaBar,
        DeckZone.PowerTrack => PowerTrack,
        DeckZone.High => HeightButton(0),
        DeckZone.Medium => HeightButton(1),
        DeckZone.Low => HeightButton(2),
        DeckZone.Recast => Recast,
        >= DeckZone.Recall0 and <= DeckZone.Recall4 => RecallStud(zone - DeckZone.Recall0),
        DeckZone.Jump => JumpTrack,
        _ => Frame,
    };

    /// <summary>Desired power for a pointer at <paramref name="x"/>, clamped to 0..1.</summary>
    public static float PowerAt(float x) =>
        Math.Clamp((x - PowerTrack.X) / PowerTrack.W, 0f, 1f);

    public static DeckZone ZoneAt(CommandDeckView view, float x, float y)
    {
        if (InCircle(Orb, x, y)) return DeckZone.Orb;

        if (view.ShowStuds)
        {
            if (Stud(0).Contains(x, y)) return DeckZone.StudRepeat;
            if (Stud(1).Contains(x, y)) return DeckZone.StudAutoTarget;
            if (Stud(2).Contains(x, y)) return DeckZone.StudKeepInView;
        }

        if (HealthBar.Contains(x, y)) return DeckZone.Health;
        if (StaminaBar.Contains(x, y)) return DeckZone.Stamina;
        if (ManaBar.Contains(x, y)) return DeckZone.Mana;
        if (JumpTrack.Contains(x, y)) return DeckZone.Jump;

        switch (view.Row)
        {
            case DeckContextRow.Power:
                // The track's hit band is the full row height so it is easy to grab.
                if (new DeckRect(PowerTrack.X, ContextRow.Y, PowerTrack.W, ContextRow.H).Contains(x, y))
                    return DeckZone.PowerTrack;
                if (HeightButton(0).Contains(x, y)) return DeckZone.High;
                if (HeightButton(1).Contains(x, y)) return DeckZone.Medium;
                if (HeightButton(2).Contains(x, y)) return DeckZone.Low;
                break;
            case DeckContextRow.Casting:
                if (Recast.Contains(x, y)) return DeckZone.Recast;
                break;
            case DeckContextRow.Recall:
                for (int i = 0; i < 5; i++)
                    if (RecallStud(i).Contains(x, y)) return DeckZone.Recall0 + i;
                break;
        }
        return DeckZone.None;
    }

    /// <summary>Whether a point is on the orb's circle.</summary>
    public static bool InOrb(float x, float y) => InCircle(Orb, x, y);

    private static bool InCircle(DeckRect r, float x, float y)
    {
        float cx = r.X + r.W / 2f, cy = r.Y + r.H / 2f, radius = r.W / 2f;
        float dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy <= radius * radius;
    }
}
