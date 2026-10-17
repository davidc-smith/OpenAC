using System.Runtime.CompilerServices;

namespace AcDream.Core.Player;

/// <summary>
/// The level curve past the top of the experience table, as ACECustom
/// servers run it: each level costs a fixed ratio more than the one before,
/// starting from the table's last step (274 to 275 on retail files). The
/// retail client stops at the table, so those servers report progress in
/// chat; extending the curve lets the sheet, the fellowship panel and the
/// plugin surface measure it like any other level.
/// </summary>
public static class ExtendedLevelCurve
{
    /// <summary>
    /// How much more each level past the table costs than the one before.
    /// ACECustom's <c>Player.levelRatio</c>.
    /// </summary>
    public const double LevelRatio = 0.014234603;

    private static readonly ConditionalWeakTable<IReadOnlyList<ulong>, IReadOnlyList<ulong>> Extended = new();

    /// <summary>
    /// The table's levels followed by the server's levels past them, up to
    /// the last level whose threshold the server can still send as a total
    /// (a signed 64-bit count). The same list comes back for the same table.
    /// A table with fewer than two levels has no last step to grow from and
    /// is returned as it is.
    /// </summary>
    public static IReadOnlyList<ulong> Extend(IReadOnlyList<ulong> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count < 2)
            return levels;
        return Extended.GetValue(levels, Build);
    }

    private static IReadOnlyList<ulong> Build(IReadOnlyList<ulong> levels)
    {
        var extended = new List<ulong>(levels);
        // The server accumulates in doubles; so does this, step for step, so
        // the thresholds land where its level-up check puts them.
        double step = levels[^1] - levels[^2];
        double total = levels[^1];
        while (true)
        {
            step += step * LevelRatio;
            total += step;
            if (total > long.MaxValue)
                break;
            extended.Add((ulong)total);
        }
        return extended;
    }
}
