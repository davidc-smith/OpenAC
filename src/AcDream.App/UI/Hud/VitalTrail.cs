namespace AcDream.App.UI.Hud;

/// <summary>
/// The pale "damage taken" segment behind one vital bar. After a drop the
/// trail holds the old value for <see cref="HoldSeconds"/>, then shrinks to
/// the new value over <see cref="ShrinkSeconds"/>. Rises have no trail. The
/// first value (and the first after <see cref="Reset"/> or a null) seeds both
/// ends, so nothing slides at login.
/// </summary>
internal sealed class VitalTrail
{
    public const double HoldSeconds = 0.3;
    public const double ShrinkSeconds = 0.6;

    private float? _fill;
    private float _from;
    private double _holdUntil;

    public void Reset() => _fill = null;

    /// <summary>The trail fraction to draw for <paramref name="fill"/> at <paramref name="now"/> (seconds); never below the fill, 0 for an unknown vital.</summary>
    public float Update(float? fill, double now)
    {
        if (fill is not float value)
        {
            _fill = null;
            return 0f;
        }

        if (_fill is not float previous)
        {
            Seed(value, now);
            return value;
        }

        if (value < previous)
        {
            _from = Math.Max(DrawnAt(previous, now), previous);
            _holdUntil = now + HoldSeconds;
        }
        else if (value > previous && DrawnAt(previous, now) <= value)
        {
            Seed(value, now);
        }

        _fill = value;
        return DrawnAt(value, now);
    }

    private void Seed(float value, double now)
    {
        _fill = value;
        _from = value;
        _holdUntil = now - ShrinkSeconds;
    }

    private float DrawnAt(float fill, double now)
    {
        if (now < _holdUntil) return Math.Max(_from, fill);
        double t = (now - _holdUntil) / ShrinkSeconds;
        if (t >= 1.0) return fill;
        return Math.Max(fill, (float)(_from + (fill - _from) * t));
    }
}
