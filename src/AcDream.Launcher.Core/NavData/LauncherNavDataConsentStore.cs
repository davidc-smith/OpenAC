using System.Text.Json;
using AcDream.Launcher.Core.Updates;
using AcDream.Platform;

namespace AcDream.Launcher.Core.NavData;

/// <summary>Whether the player agreed to the RynthNav navdata download: asked once, then kept current.</summary>
public sealed class LauncherNavDataConsentStore
{
    public const string FileName = "launcher-navdata.json";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public LauncherNavDataConsentStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public static LauncherNavDataConsentStore ForApplicationPaths(ApplicationPathSet paths) =>
        new(Path.Combine(paths.ConfigDirectory, FileName));

    /// <summary>A missing or unreadable file means not agreed: the banner simply asks again.</summary>
    public bool IsAgreed
    {
        get
        {
            try
            {
                return File.Exists(FilePath)
                    && JsonSerializer.Deserialize<Consent>(File.ReadAllBytes(FilePath), Options)?.Agreed == true;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public void Agree()
    {
        AtomicJsonFile.WriteAsync(FilePath, new Consent(true), Options).GetAwaiter().GetResult();
    }

    private sealed record Consent(bool Agreed);
}
