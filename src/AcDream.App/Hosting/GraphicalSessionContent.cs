using System.Collections.Immutable;
using AcDream.App.Composition;
using AcDream.Content;
using DatReaderWriter.DBObjs;

namespace AcDream.App.Hosting;

/// <summary>
/// The game files the session is played from, opened once by the session
/// host and kept for the life of the process: the installed data files, the
/// prepared assets, the spell tables, and the land's height table. The
/// session's collision and its windowless route read them with no window; a
/// window borrows them for drawing and never closes them.
/// </summary>
internal sealed class GraphicalSessionContent : IDisposable
{
    private bool _disposed;

    internal GraphicalSessionContent(
        IDatReaderWriter dats,
        IPreparedAssetSource preparedAssets,
        MagicCatalog magic,
        ImmutableArray<float> heightTable)
    {
        Dats = dats ?? throw new ArgumentNullException(nameof(dats));
        PreparedAssets = preparedAssets
            ?? throw new ArgumentNullException(nameof(preparedAssets));
        PreparedCollision = preparedAssets as IPreparedCollisionSource
            ?? throw new NotSupportedException(
                "Production prepared assets must expose collision data.");
        Magic = magic ?? throw new ArgumentNullException(nameof(magic));
        HeightTable = heightTable;
    }

    internal IDatReaderWriter Dats { get; }

    internal IPreparedAssetSource PreparedAssets { get; }

    internal IPreparedCollisionSource PreparedCollision { get; }

    internal MagicCatalog Magic { get; }

    internal ImmutableArray<float> HeightTable { get; }

    /// <summary>
    /// The one lock every reader of these files takes, the window's drawing
    /// and the session's collision alike: the files are shared.
    /// </summary>
    internal object DatLock { get; } = new();

    /// <summary>Opens the files the options name.</summary>
    internal static GraphicalSessionContent Open(
        RuntimeOptions options,
        Action<string> diagnostic)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostic);
        var factory = new RetailContentEffectsAudioCompositionFactory();
        IDatReaderWriter? dats = null;
        IPreparedAssetSource? prepared = null;
        try
        {
            dats = factory.OpenDatCollection(options.DatDir);
            prepared = factory.OpenPreparedAssetSource(
                options.PreparedAssetPath,
                options.PreparedAssetOverlayPath,
                options.PreparedAssetBaseRecipeVersion,
                options.PreparedAssetEffectiveRecipeVersion,
                dats,
                diagnostic);
            MagicCatalog magic = factory.LoadMagicCatalog(dats);
            Region region = dats.Get<Region>(0x13000000u)
                ?? throw new InvalidOperationException(
                    "Region dat id 0x13000000 is missing.");
            float[]? heights = region.LandDefs.LandHeightTable;
            if (heights is null || heights.Length < 256)
            {
                throw new InvalidOperationException(
                    "Region.LandDefs.LandHeightTable is missing or truncated.");
            }
            return new GraphicalSessionContent(
                dats,
                prepared,
                magic,
                ImmutableArray.CreateRange(heights));
        }
        catch
        {
            try
            {
                prepared?.Dispose();
            }
            finally
            {
                dats?.Dispose();
            }
            throw;
        }
    }

    /// <summary>What the session's collision is built from.</summary>
    internal GraphicalCollisionContent CreateCollisionContent() =>
        new(Dats, DatLock, PreparedCollision, HeightTable);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            PreparedAssets.Dispose();
        }
        finally
        {
            Dats.Dispose();
        }
    }
}
