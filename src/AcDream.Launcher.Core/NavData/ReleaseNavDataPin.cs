using System.Text.Json;
using System.Text.Json.Serialization;
using AcDream.Launcher.Core.Updates;
using AcDream.Rynth.NavData;

namespace AcDream.Launcher.Core.NavData;

/// <summary>
/// The RynthNav navdata pin a release carries beside manifest.json (rynth-navdata.json), read as
/// strictly as the manifest, and the pin this launcher was built with.
/// </summary>
public static class ReleaseNavDataPin
{
    public const string FileName = "rynth-navdata.json";
    public const int CurrentSchemaVersion = 1;
    private const string BuiltInResource = "OpenAC.RynthNavDataPin.json";

    private static readonly JsonSerializerOptions Strict = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 4,
    };

    public static NavDataPin Parse(ReadOnlySpan<byte> utf8)
    {
        Document? document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(utf8, Strict);
        }
        catch (JsonException ex)
        {
            throw new LauncherUpdateException($"The navdata pin is invalid: {ex.Message}", ex);
        }

        if (document is null
            || document.SchemaVersion != CurrentSchemaVersion
            || !Uri.TryCreate(document.Url, UriKind.Absolute, out Uri? url) || url.Scheme != Uri.UriSchemeHttps
            || document.Size <= 0 || document.Files <= 0 || document.Bytes <= 0
            || !IsSha256(document.Sha256) || !IsSha256(document.Id)
            || string.IsNullOrWhiteSpace(document.RynthSuite) || string.IsNullOrWhiteSpace(document.Pinned))
        {
            throw new LauncherUpdateException("The navdata pin is invalid.");
        }

        return new NavDataPin(document.Url!, document.Size, document.Sha256!.ToLowerInvariant(), document.Files,
            document.Bytes, document.Id!.ToLowerInvariant(), document.RynthSuite!, document.Pinned!);
    }

    public static NavDataPin BuiltIn()
    {
        using Stream stream = typeof(ReleaseNavDataPin).Assembly.GetManifestResourceStream(BuiltInResource)
            ?? throw new InvalidOperationException("The launcher was built without a navdata pin.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return NavDataPin.Parse(memory.ToArray());
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private sealed record Document(
        int SchemaVersion, string? Url, long Size, string? Sha256, int Files, long Bytes,
        string? Id, string? RynthSuite, string? Pinned);
}
