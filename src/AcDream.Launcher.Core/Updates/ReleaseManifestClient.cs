using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcDream.Launcher.Core.Plugins;

namespace AcDream.Launcher.Core.Updates;

public interface IReleaseManifestClient
{
    Task<ReleaseManifest> FetchAsync(CancellationToken cancellationToken = default);

    /// <summary>The launcher fingerprint published beside the manifest, or null when the release
    /// has none or it cannot be read; the launcher then compares versions instead.</summary>
    Task<LauncherFingerprintDocument?> FetchLauncherFingerprintAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<LauncherFingerprintDocument?>(null);
}

public sealed class ReleaseManifestClient : IReleaseManifestClient, IDisposable
{
    public const int MaximumManifestBytes = 1024 * 1024;
    public const long MaximumArtifactBytes = 4L * 1024 * 1024 * 1024;
    public const int MaximumRedirects = 5;

    /// <summary>owner/name of the repository whose releases are the update feed
    /// (Directory.Build.props OpenACReleaseRepository).</summary>
    public static string ReleaseRepository { get; } =
        typeof(ReleaseManifestClient).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "OpenAC.ReleaseRepository")?.Value
        ?? throw new InvalidOperationException("The launcher was built without a release repository.");

    /// <summary>The tag prefix of this product's releases (Directory.Build.props
    /// OpenACReleaseTagPrefix: <c>gateway-v</c> or <c>gateway-rynth-v</c>).</summary>
    public static string ReleaseTagPrefix { get; } =
        typeof(ReleaseManifestClient).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "OpenAC.ReleaseTagPrefix")?.Value
        ?? throw new InvalidOperationException("The launcher was built without a release tag prefix.");

    /// <summary>The production update feed: the repository's releases Atom feed, read for the newest
    /// release tagged <see cref="ReleaseTagPrefix"/>. Not GitHub's "latest" release: both products
    /// release from one repository and GitHub has one "latest" per repository.</summary>
    public static Uri ProductionUpdateFeedUri { get; } =
        GitHubReleaseLocator.ReleasesFeed(ReleaseRepository);

    /// <summary>Ten entries with full release notes (see <see cref="ProductReleaseFeed"/>).</summary>
    public const int MaximumFeedBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
    };

    private readonly HttpClient _httpClient;
    /// <summary>A fixed manifest (overrides and fixtures); null in feed mode.</summary>
    private readonly Uri? _manifestUri;

    /// <summary>Feed mode (production): where to look and which tags are this product's.</summary>
    private readonly (Uri FeedUri, string Repository, string TagPrefix)? _feed;

    private readonly bool _allowLoopbackHttp;

    /// <summary>The manifest the last feed-mode <see cref="FetchAsync"/> downloaded; files published
    /// beside it (the fingerprint) are read from the same release. Volatile: on the rynth branch the
    /// navdata pin check reads it from outside the updater's operation gate.</summary>
    private volatile Uri? _releaseManifestUri;

    public ReleaseManifestClient(TimeSpan? timeout = null)
        : this(
            manifestUri: null,
            (ProductionUpdateFeedUri, ReleaseRepository, ReleaseTagPrefix),
            allowLoopbackHttp: false,
            CreateRedirectDisabledHandler(),
            timeout)
    {
    }

    private ReleaseManifestClient(
        Uri manifestUri,
        bool allowLoopbackHttp,
        HttpMessageHandler handler,
        TimeSpan? timeout)
        : this(
            manifestUri ?? throw new ArgumentNullException(nameof(manifestUri)),
            feed: null,
            allowLoopbackHttp,
            handler,
            timeout)
    {
    }

    private ReleaseManifestClient(
        Uri? manifestUri,
        (Uri FeedUri, string Repository, string TagPrefix)? feed,
        bool allowLoopbackHttp,
        HttpMessageHandler handler,
        TimeSpan? timeout)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if ((manifestUri is null) == (feed is null))
        {
            throw new ArgumentException("Exactly one of a manifest URI and a release feed is required.");
        }

        _manifestUri = manifestUri;
        _feed = feed;
        _allowLoopbackHttp = allowLoopbackHttp;
        RequireTransport(manifestUri ?? feed!.Value.FeedUri, "manifest", _allowLoopbackHttp);
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(15),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenAC-launcher/1");
    }

    internal static ReleaseManifestClient CreateLoopbackFixture(
        Uri manifestUri,
        TimeSpan? timeout = null) => new(
            manifestUri,
            allowLoopbackHttp: true,
            CreateRedirectDisabledHandler(),
            timeout);

    public static ReleaseManifestClient CreateLocalUpdateFeedOverride(
        Uri manifestUri,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(manifestUri);
        if (!string.IsNullOrEmpty(manifestUri.UserInfo)
            || !string.IsNullOrEmpty(manifestUri.Query)
            || !string.IsNullOrEmpty(manifestUri.Fragment))
        {
            throw new LauncherUpdateException(
                "A process-local manifest URI cannot contain user information, "
                + "a query, or a fragment.");
        }

        bool allowLoopbackHttp = string.Equals(
                manifestUri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.Ordinal)
            && manifestUri.IsLoopback;
        if (!string.Equals(
                manifestUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.Ordinal)
            && !allowLoopbackHttp)
        {
            throw new LauncherUpdateException(
                "A process-local manifest URI must use HTTPS "
                + "(loopback HTTP is fixture-only).");
        }

        return new ReleaseManifestClient(
            manifestUri,
            allowLoopbackHttp,
            CreateRedirectDisabledHandler(),
            timeout);
    }

    internal static ReleaseManifestClient CreateForTransportTest(
        Uri manifestUri,
        bool allowLoopbackHttp,
        HttpMessageHandler handler) => new(
            manifestUri,
            allowLoopbackHttp,
            handler,
            TimeSpan.FromSeconds(15));

    internal static ReleaseManifestClient CreateFeedForTransportTest(
        Uri feedUri,
        string repository,
        string tagPrefix,
        HttpMessageHandler handler) => new(
            manifestUri: null,
            (feedUri, repository, tagPrefix),
            allowLoopbackHttp: false,
            handler,
            TimeSpan.FromSeconds(15));

    public async Task<ReleaseManifest> FetchAsync(
        CancellationToken cancellationToken = default)
    {
        if (_feed is not { } feed)
        {
            return Parse(
                await FetchBytesAsync(_manifestUri!, "release manifest", MaximumManifestBytes, cancellationToken)
                    .ConfigureAwait(false),
                _allowLoopbackHttp);
        }

        // The previous release stays readable while the refetch is in flight (a concurrent pin or
        // fingerprint read still sees it); a failed refetch must not leave it readable afterwards.
        try
        {
            ProductFeedRelease release = ProductReleaseFeed.Newest(
                    await FetchBytesAsync(feed.FeedUri, "releases feed", MaximumFeedBytes, cancellationToken)
                        .ConfigureAwait(false),
                    feed.Repository,
                    feed.TagPrefix)
                ?? throw new LauncherUpdateException(
                    $"No '{feed.TagPrefix}' release is among the newest releases of {feed.Repository}.");
            Uri manifestUri = GitHubReleaseLocator.TaggedAsset(feed.Repository, release.Tag, "manifest.json");
            ReleaseManifest manifest = Parse(
                await FetchBytesAsync(manifestUri, "release manifest", MaximumManifestBytes, cancellationToken)
                    .ConfigureAwait(false),
                _allowLoopbackHttp);
            if (manifest.Version.CompareTo(release.Version) != 0)
            {
                throw new LauncherUpdateException(
                    $"Release {release.Tag} carries a manifest for version {manifest.Version.Value}.");
            }

            _releaseManifestUri = manifestUri;
            return manifest;
        }
        catch
        {
            _releaseManifestUri = null;
            throw;
        }
    }

    /// <summary>A file published beside the release manifest, or null in feed mode before
    /// <see cref="FetchAsync"/> has found the release.</summary>
    private Uri? ReleaseFileUri(string fileName) =>
        (_manifestUri ?? _releaseManifestUri) is { } manifestUri ? new Uri(manifestUri, fileName) : null;

    /// <summary>How long the optional fingerprint may take before the check goes on without it.</summary>
    internal TimeSpan FingerprintTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public async Task<LauncherFingerprintDocument?> FetchLauncherFingerprintAsync(
        CancellationToken cancellationToken = default)
    {
        if (ReleaseFileUri(LauncherFingerprintDocument.FileName) is not { } fingerprintUri)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FingerprintTimeout);
        try
        {
            byte[] bytes = await FetchBytesAsync(
                    fingerprintUri,
                    "launcher fingerprint",
                    MaximumManifestBytes,
                    timeout.Token)
                .ConfigureAwait(false);
            return ParseFingerprint(bytes);
        }
        // The file is optional: a release from before fingerprints has none, and one that is slow,
        // unreachable or unreadable leaves the check to compare versions, as it always could.
        catch (LauncherUpdateException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return null;
        }
    }

    internal static LauncherFingerprintDocument ParseFingerprint(ReadOnlySpan<byte> utf8)
    {
        try
        {
            FingerprintDocument? document = JsonSerializer.Deserialize<FingerprintDocument>(utf8, SerializerOptions);
            if (document is null
                || document.SchemaVersion != LauncherFingerprintDocument.CurrentSchemaVersion
                || !IsSha256(document.Fingerprint)
                || !LauncherVersion.TryParse(document.Version, out LauncherVersion? version))
            {
                throw new LauncherUpdateException("The launcher fingerprint is invalid.");
            }

            return new LauncherFingerprintDocument(version, document.Fingerprint!.ToLowerInvariant());
        }
        catch (JsonException ex)
        {
            throw new LauncherUpdateException($"The launcher fingerprint is invalid: {ex.Message}", ex);
        }
    }

    private async Task<byte[]> FetchBytesAsync(
        Uri start,
        string description,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            Uri current = start;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (int redirectCount = 0;;)
            {
                RequireTransport(current, $"{description} redirect", _allowLoopbackHttp);
                if (!visited.Add(current.AbsoluteUri))
                {
                    throw new LauncherUpdateException(
                        $"The {description} redirect chain contains a loop.");
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using HttpResponseMessage response = await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount >= MaximumRedirects)
                    {
                        throw new LauncherUpdateException(
                            $"The {description} exceeded {MaximumRedirects} redirects.");
                    }

                    Uri? location = response.Headers.Location;
                    if (location is null)
                    {
                        throw new LauncherUpdateException(
                            $"The {description} redirect has no Location header.");
                    }

                    Uri next = location.IsAbsoluteUri
                        ? location
                        : new Uri(current, location);
                    RequireTransport(next, $"{description} redirect", _allowLoopbackHttp);
                    current = next;
                    redirectCount++;
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await ReadBytesAsync(response, description, maximumBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LauncherUpdateException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   or IOException
                                   or JsonException
                                   or NotSupportedException)
        {
            throw new LauncherUpdateException(
                $"The {description} could not be loaded: {ex.Message}",
                ex);
        }
    }

    internal static ReleaseManifest Parse(
        ReadOnlySpan<byte> utf8,
        bool allowLoopbackHttpArtifacts = false)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                utf8.ToArray(),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                });
            RejectDuplicateProperties(document.RootElement, "$" );
            ManifestDocument? value = document.RootElement.Deserialize<ManifestDocument>(
                SerializerOptions);
            return Validate(value, allowLoopbackHttpArtifacts);
        }
        catch (LauncherUpdateException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException
                                   or FormatException
                                   or InvalidOperationException)
        {
            throw new LauncherUpdateException(
                $"The release manifest is invalid: {ex.Message}",
                ex);
        }
    }

    public void Dispose() => _httpClient.Dispose();

    internal static void RequireTransport(
        Uri uri,
        string description,
        bool allowLoopbackHttp)
    {
        if (!uri.IsAbsoluteUri
            || (uri.Scheme != Uri.UriSchemeHttps
                && !(allowLoopbackHttp
                    && uri.Scheme == Uri.UriSchemeHttp
                    && uri.IsLoopback)))
        {
            throw new LauncherUpdateException(
                $"The {description} URI must use HTTPS"
                + (allowLoopbackHttp ? " (or fixture-only loopback HTTP)." : "."));
        }
    }

    internal static void RequireSecureOrLoopback(Uri uri, string description) =>
        RequireTransport(uri, description, allowLoopbackHttp: true);

    private static async Task<byte[]> ReadBytesAsync(
        HttpResponseMessage response,
        string description,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long contentLength
            && contentLength > maximumBytes)
        {
            throw new LauncherUpdateException(
                $"The {description} is larger than {maximumBytes} bytes.");
        }

        await using Stream input = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new LauncherUpdateException(
                    $"The {description} is larger than {maximumBytes} bytes.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static ReleaseManifest Validate(
        ManifestDocument? document,
        bool allowLoopbackHttpArtifacts)
    {
        if (document is null)
        {
            throw new LauncherUpdateException("The release manifest is empty.");
        }

        if (document.SchemaVersion != ReleaseManifest.CurrentSchemaVersion)
        {
            throw new LauncherUpdateException(
                $"Release manifest schema version {document.SchemaVersion} is not supported.");
        }

        LauncherVersion version = LauncherVersion.Parse(
            document.Version
            ?? throw new LauncherUpdateException("The release version is missing."));
        LauncherVersion minimum = LauncherVersion.Parse(
            document.MinimumLauncherVersion
            ?? throw new LauncherUpdateException(
                "The minimum launcher version is missing."));
        if (minimum > version)
        {
            throw new LauncherUpdateException(
                "The minimum launcher version cannot exceed the release version.");
        }

        IReadOnlyDictionary<string, ReleaseArtifact> clients = ValidateArtifacts(
            document.Clients,
            "clients",
            allowLoopbackHttpArtifacts);
        IReadOnlyDictionary<string, ReleaseArtifact> launchers = ValidateArtifacts(
            document.Launchers,
            "launchers",
            allowLoopbackHttpArtifacts);
        return new ReleaseManifest(version, minimum, clients, launchers);
    }

    private static IReadOnlyDictionary<string, ReleaseArtifact> ValidateArtifacts(
        Dictionary<string, ArtifactDocument>? artifacts,
        string field,
        bool allowLoopbackHttpArtifacts)
    {
        if (artifacts is null || artifacts.Count == 0)
        {
            throw new LauncherUpdateException($"Manifest field '{field}' must not be empty.");
        }

        var result = new Dictionary<string, ReleaseArtifact>(StringComparer.Ordinal);
        foreach ((string rid, ArtifactDocument value) in artifacts)
        {
            if (!LauncherRuntimeIdentity.IsValidRid(rid))
            {
                throw new LauncherUpdateException(
                    $"Manifest field '{field}' contains invalid RID '{rid}'.");
            }

            if (value is null)
            {
                throw new LauncherUpdateException(
                    $"Manifest payload '{field}.{rid}' is null.");
            }

            if (!Uri.TryCreate(value.Url, UriKind.Absolute, out Uri? uri))
            {
                throw new LauncherUpdateException(
                    $"Manifest payload '{field}.{rid}' has an invalid URL.");
            }

            RequireTransport(
                uri,
                $"{field}.{rid} artifact",
                allowLoopbackHttpArtifacts);
            if (!IsSha256(value.Sha256))
            {
                throw new LauncherUpdateException(
                    $"Manifest payload '{field}.{rid}' has an invalid SHA-256 digest.");
            }

            if (value.Size <= 0 || value.Size > MaximumArtifactBytes)
            {
                throw new LauncherUpdateException(
                    $"Manifest payload '{field}.{rid}' has an invalid size.");
            }

            result.Add(
                rid,
                new ReleaseArtifact(uri, value.Sha256!.ToLowerInvariant(), value.Size));
        }

        return result;
    }

    internal static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    /// <summary>The transport every release download uses: redirects are left to the caller, which
    /// validates each hop before requesting it.</summary>
    internal static HttpMessageHandler CreateRedirectDisabledHandler() =>
        new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
        };

    private static void RejectDuplicateProperties(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new LauncherUpdateException(
                        $"Duplicate JSON property '{path}.{property.Name}' is not allowed.");
                }

                RejectDuplicateProperties(property.Value, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                RejectDuplicateProperties(item, $"{path}[{index++}]");
            }
        }
    }

    private sealed class ManifestDocument
    {
        public int SchemaVersion { get; init; }

        public string? Version { get; init; }

        public string? MinimumLauncherVersion { get; init; }

        public Dictionary<string, ArtifactDocument>? Clients { get; init; }

        public Dictionary<string, ArtifactDocument>? Launchers { get; init; }
    }

    private sealed class FingerprintDocument
    {
        public int SchemaVersion { get; init; }

        public string? Version { get; init; }

        public string? Fingerprint { get; init; }
    }

    private sealed class ArtifactDocument
    {
        public string? Url { get; init; }

        public string? Sha256 { get; init; }

        public long Size { get; init; }
    }
}
