using System.Globalization;
using System.Text.Json;

namespace AcDream.Rynth.Host;

/// <summary>RynthTracker's snapshot JSON as one chat line.</summary>
public static class TrackerSnapshotText
{
    private const string Unreadable = "Rynth tracker: unreadable snapshot";

    public static string Format(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return "Rynth tracker: no data yet";

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryDouble(root, "ss", out double sessionSeconds)
                || !TryLong(root, "kt", out long kills)
                || !TryDouble(root, "kh", out double killsPerHour)
                || !TryLong(root, "xt", out long xp)
                || !TryLong(root, "xh", out long xpPerHour)
                || !TryLong(root, "lt", out long lum)
                || !TryLong(root, "lh", out long lumPerHour)
                || !TryLong(root, "dt", out long deaths))
            {
                return Unreadable;
            }

            var session = TimeSpan.FromSeconds(sessionSeconds);
            return string.Format(
                CultureInfo.InvariantCulture,
                "Rynth tracker: {0:N0} kills ({1:0.0}/h), {2:N0} XP ({3:N0}/h), {4:N0} lum ({5:N0}/h), {6:N0} deaths, session {7}m {8}s",
                kills,
                killsPerHour,
                xp,
                xpPerHour,
                lum,
                lumPerHour,
                deaths,
                (int)session.TotalMinutes,
                session.Seconds);
        }
        catch (JsonException)
        {
            return Unreadable;
        }
        catch (OverflowException)
        {
            return Unreadable;
        }
    }

    private static bool TryLong(JsonElement root, string key, out long value)
    {
        value = 0;
        return root.TryGetProperty(key, out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out value);
    }

    private static bool TryDouble(JsonElement root, string key, out double value)
    {
        value = 0;
        return root.TryGetProperty(key, out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out value)
            && double.IsFinite(value)
            && Math.Abs(value) < int.MaxValue * 60.0;
    }
}
