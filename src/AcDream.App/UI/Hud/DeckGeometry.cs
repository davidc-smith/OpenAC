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
    Cast,
    Recall0, Recall1, Recall2, Recall3, Recall4,
    Jump,
}

/// <summary>
/// The deck's geometry in points for one size. <see cref="Scale"/> multiplies
/// everything; <see cref="Stretch"/> widens the content right of the orb: the
/// bars, the tracks and the casting line get longer while the orb, studs,
/// labels and buttons keep their size. Nothing here depends on the combat
/// mode, so a mode change never changes the window's size.
/// </summary>
public sealed record DeckGeometry
{
    public const float BaseWidth = 420f;
    public const float BaseHeight = 150f;
    public const float MinScale = 0.75f;
    public const float MaxScale = 1.5f;
    public const float ScaleStep = 0.05f;
    /// <summary>A scale this close to 1 snaps to exactly 1.</summary>
    public const float LockToOne = 0.03f;
    public const float MinStretch = 1f;
    public const float MaxStretch = 2f;

    public static DeckGeometry Default { get; } = new(1f, 1f);

    public float Scale { get; }
    public float Stretch { get; }

    private DeckGeometry(float scale, float stretch)
    {
        Scale = scale;
        Stretch = stretch;
    }

    public float Width => BaseWidth * Stretch * Scale;
    public float Height => BaseHeight * Scale;

    /// <summary>A geometry with the scale snapped and the stretch limited.</summary>
    public static DeckGeometry Create(float scale, float stretch) => Create(scale, stretch, MinStretch);

    /// <summary>As <see cref="Create(float, float)"/>, with the stretch no lower than <paramref name="minStretch"/>.</summary>
    public static DeckGeometry Create(float scale, float stretch, float minStretch)
    {
        float min = Math.Min(Math.Max(MinStretch, float.IsFinite(minStretch) ? minStretch : MinStretch), MaxStretch);
        return new(SnapScale(scale), Math.Clamp(float.IsFinite(stretch) ? stretch : min, min, MaxStretch));
    }

    /// <summary>A scale limited to the range, locked onto 1 when close, else on a 0.05 step.</summary>
    public static float SnapScale(float raw)
    {
        if (!float.IsFinite(raw)) return 1f;
        float clamped = Math.Clamp(raw, MinScale, MaxScale);
        if (MathF.Abs(clamped - 1f) <= LockToOne) return 1f;
        return MathF.Round(MathF.Round(clamped / ScaleStep) * ScaleStep, 2);
    }

    /// <summary>The geometry for a window size: the scale from the height, the
    /// stretch from the width. It never grows past the size: a scale that
    /// rounds up past the height, or a width too narrow for the scale, lowers
    /// the scale to the largest step that fits.</summary>
    public static DeckGeometry FromSize(float width, float height)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height)) return Default;
        float scale = SnapScale(height / BaseHeight);
        if (BaseHeight * scale > height + 0.5f)
            scale = StepDown(height / BaseHeight);
        if (BaseWidth * scale > width + 0.5f)
            scale = StepDown(width / BaseWidth);
        return Create(scale, width / (BaseWidth * scale));
    }

    /// <summary>This geometry limited to a room: when it does not fit, the scale
    /// steps down to the largest step that does (at the smallest stretch), then
    /// the stretch narrows until the width fits. A room smaller than the
    /// smallest deck gives the smallest deck.</summary>
    public DeckGeometry FitWithin(float width, float height)
    {
        const float slack = 0.01f;
        if (!float.IsFinite(width) || !float.IsFinite(height)) return this;
        if (Width <= width + slack && Height <= height + slack) return this;
        float scale = Math.Min(Scale, StepDown(Math.Min(height / BaseHeight, width / BaseWidth)));
        return Create(scale, Math.Min(Stretch, width / (BaseWidth * scale)));
    }

    /// <summary>The largest scale step at or under <paramref name="raw"/>, never under the minimum.</summary>
    private static float StepDown(float raw) =>
        Math.Max(MinScale, MathF.Floor(raw / ScaleStep + 1e-4f) * ScaleStep);

    /// <summary>A side-edge resize: the same scale, the width decides the stretch.</summary>
    public DeckGeometry WithWidth(float width) => Create(Scale, width / (BaseWidth * Scale));

    /// <summary>A corner resize from this geometry (the one at the drag start):
    /// whichever axis moved further from its size decides the scale; the
    /// stretch is kept.</summary>
    public DeckGeometry WithCornerSize(float width, float height)
    {
        float fromWidth = width / (BaseWidth * Stretch);
        float fromHeight = height / BaseHeight;
        float raw = MathF.Abs(fromWidth - Scale) > MathF.Abs(fromHeight - Scale) ? fromWidth : fromHeight;
        return Create(raw, Stretch);
    }

    // Layout in unscaled points; every public rectangle goes through S().
    private const float ContentLeft = 134f;
    private const float LabelWidth = 60f;
    private const float RowValueLeft = ContentLeft + LabelWidth + 6f;   // 200
    private float Extra => BaseWidth * (Stretch - 1f);
    private float ContentRight => 408f + Extra;
    private float HeightButtonX(int index) => ContentRight - 82f + index * 28f;

    private DeckRect S(float x, float y, float w, float h) => new(x * Scale, y * Scale, w * Scale, h * Scale);

    public DeckRect Frame => S(70f, 4f, 350f + Extra, 142f);
    public DeckRect Orb => S(32f, 29f, 92f, 92f);

    public DeckRect HealthBar => S(ContentLeft, 16f, ContentRight - ContentLeft, 20f);
    public DeckRect StaminaBar => S(ContentLeft, 41f, ContentRight - ContentLeft, 20f);
    public DeckRect ManaBar => S(ContentLeft, 66f, ContentRight - ContentLeft, 20f);

    public DeckRect ContextRow => S(ContentLeft, 92f, ContentRight - ContentLeft, 22f);
    public DeckRect RowLabel => S(ContentLeft, 92f, LabelWidth, 22f);

    public DeckRect PowerTrack => S(RowValueLeft, 98f, HeightButtonX(0) - 8f - RowValueLeft, 10f);
    public DeckRect HeightButton(int index) => S(HeightButtonX(index), 92f, 26f, 22f);

    public DeckRect Cast => S(ContentRight - 26f, 92f, 26f, 22f);
    public DeckRect CastingText => S(RowValueLeft, 92f, ContentRight - 26f - 6f - RowValueLeft, 22f);

    public DeckRect RecallStud(int index) => S(RowValueLeft + index * 30f, 92f, 26f, 22f);

    public DeckRect JumpLabel => S(ContentLeft, 122f, LabelWidth, 14f);
    public DeckRect JumpTrack => S(RowValueLeft, 125f, ContentRight - RowValueLeft, 8f);

    public DeckRect Stud(int index) => S(2f, 44f + index * 26f, 26f, 22f);

    public IEnumerable<DeckRect> AllRects()
    {
        yield return Frame; yield return Orb;
        yield return HealthBar; yield return StaminaBar; yield return ManaBar;
        yield return ContextRow; yield return RowLabel; yield return PowerTrack;
        for (int i = 0; i < 3; i++) yield return HeightButton(i);
        yield return CastingText; yield return Cast;
        for (int i = 0; i < 5; i++) yield return RecallStud(i);
        yield return JumpLabel; yield return JumpTrack;
        for (int i = 0; i < 3; i++) yield return Stud(i);
    }

    /// <summary>The rectangle a zone's tooltip points at; the whole frame for <see cref="DeckZone.None"/>.</summary>
    internal DeckRect RectFor(DeckZone zone) => zone switch
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
        DeckZone.Cast => Cast,
        >= DeckZone.Recall0 and <= DeckZone.Recall4 => RecallStud(zone - DeckZone.Recall0),
        DeckZone.Jump => JumpTrack,
        _ => Frame,
    };

    /// <summary>Desired power for a pointer at <paramref name="x"/>, clamped to 0..1.</summary>
    public float PowerAt(float x) =>
        Math.Clamp((x - PowerTrack.X) / PowerTrack.W, 0f, 1f);

    /// <summary>How close to a quarter mark a dragged power locks onto it.</summary>
    public const float PowerSnap = 0.04f;

    /// <summary>A power locked onto 25%, 50% or 75% when within <see cref="PowerSnap"/>; else unchanged.</summary>
    public static float SnapPower(float power)
    {
        float mark = MathF.Round(power * 4f) / 4f;
        return mark is > 0f and < 1f && MathF.Abs(power - mark) <= PowerSnap + 1e-4f ? mark : power;
    }

    public DeckZone ZoneAt(CommandDeckView view, float x, float y)
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
                if (Cast.Contains(x, y)) return DeckZone.Cast;
                break;
            case DeckContextRow.Recall:
                for (int i = 0; i < 5; i++)
                    if (RecallStud(i).Contains(x, y)) return DeckZone.Recall0 + i;
                break;
        }
        return DeckZone.None;
    }

    /// <summary>Whether a point is on the orb's circle.</summary>
    public bool InOrb(float x, float y) => InCircle(Orb, x, y);

    private static bool InCircle(DeckRect r, float x, float y)
    {
        float cx = r.X + r.W / 2f, cy = r.Y + r.H / 2f, radius = r.W / 2f;
        float dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy <= radius * radius;
    }
}
