using System.Globalization;
using System.Text.Json;

namespace AcDream.Rynth.Host;

/// <summary>RynthTracker's snapshot JSON as one chat line.</summary>
public static class TrackerSnapshotText
{
    public static string Format(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return "Rynth tracker: no data yet";

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        var session = TimeSpan.FromSeconds(root.GetProperty("ss").GetDouble());
        CultureInfo culture = CultureInfo.InvariantCulture;
        return string.Format(
            culture,
            "Rynth tracker: {0:N0} kills ({1:0.0}/h), {2:N0} XP ({3:N0}/h), {4:N0} lum, session {5}m {6}s",
            root.GetProperty("kt").GetInt64(),
            root.GetProperty("kh").GetDouble(),
            root.GetProperty("xt").GetInt64(),
            root.GetProperty("xh").GetInt64(),
            root.GetProperty("lt").GetInt64(),
            (int)session.TotalMinutes,
            session.Seconds);
    }
}
