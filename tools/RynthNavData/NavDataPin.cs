using System;
using System.IO;
using System.Text.Json;
using RynthCore.App.Avalonia;

namespace AcDream.Tools.RynthNavData;

/// <summary>
/// external/rynth-navdata.json: the one RynthNav archive this OpenAC build was tested with.
/// <see cref="Id"/> is the archive's SHA-256 and is what the installed navdata.id holds.
/// <see cref="RynthSuite"/> is the RynthSuite commit it was tested against.
/// </summary>
internal sealed record NavDataPin(string Url, long Size, string Sha256, int Files, long Bytes, string Id, string RynthSuite, string Pinned)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static NavDataPin Load(string path) =>
        JsonSerializer.Deserialize<NavDataPin>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"{path} holds no pin");

    internal void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Json) + "\n");

    /// <summary>Their feed entry for this archive (release 0: not from a feed).</summary>
    internal RynthUpdater.NavDataEntry ToEntry() => new(Id, 0, Url, Size, Sha256, Files, Bytes);
}
