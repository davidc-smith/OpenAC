using System.Text.Json;
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
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException
                or NotSupportedException or ArgumentException)
            {
                return false;
            }
        }
    }

    /// <summary>Writes {"agreed": true} atomically (temp file, then a replacing move); synchronous,
    /// so the caller's thread never waits on an async continuation.</summary>
    public void Agree()
    {
        string directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("The consent path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Consent(true), Options);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       bufferSize: 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Consent(bool Agreed);
}
